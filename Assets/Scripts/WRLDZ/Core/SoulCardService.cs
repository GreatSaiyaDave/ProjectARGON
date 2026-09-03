using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// TCG overflow: soul cards in the backpack with a wall-clock timer.
    /// Logged out / backgrounded / offline still ticks. Duel and Make Room pause.
    /// </summary>
    public static class SoulCardService
    {
        public const int BandCommonRare = 0;
        public const int BandSuperUltra = 1;
        public const int BandSecretFavorite = 2;

        public static int DurationHours(int rarityBand) => rarityBand switch
        {
            BandSuperUltra => 8,
            BandSecretFavorite => 12,
            _ => 4
        };

        public static bool TryReceiveTcgCard(
            PlayerProgress p,
            PlayerInventory inv,
            int cardId,
            int qty,
            bool atHome,
            long nowUnix,
            out int leftover,
            out string error)
        {
            leftover = 0;
            error = null;
            if (inv == null || cardId <= 0 || qty <= 0)
            {
                error = "Invalid receive.";
                leftover = qty;
                return false;
            }

            inv.EnsureValid();
            leftover = atHome
                ? PutInBoxes(inv, cardId, qty, carriedOnly: false)
                : PutInBoxes(inv, cardId, qty, carriedOnly: true);

            if (leftover <= 0) return true;

            for (var i = 0; i < leftover; i++)
            {
                if (!TryPlaceSoulTile(inv, cardId, atHome, nowUnix))
                {
                    error = "Make Room";
                    leftover -= i;
                    inv.pendingReceiveCardId = cardId;
                    inv.pendingReceiveQty = leftover;
                    return leftover == qty ? false : true;
                }
            }

            leftover = 0;
            inv.pendingReceiveCardId = 0;
            inv.pendingReceiveQty = 0;
            return true;
        }

        /// <summary>
        /// Expire soul cards whose wall-clock has passed. Returns how many copies left the account.
        /// <paramref name="paused"/> is duel / Make Room — do not expire, do not advance.
        /// Frozen tiles (<c>expiresUnix == 0</c>) never expire (home boxes full).
        /// </summary>
        public static int Tick(PlayerInventory inv, long nowUnix, bool paused)
        {
            if (inv?.backpack?.items == null || paused) return 0;
            var kept = new List<BackpackItem>();
            var gone = 0;
            foreach (var it in inv.backpack.items)
            {
                if (it == null) continue;
                if (it.Kind != BackpackItemKind.SoulCard)
                {
                    kept.Add(it);
                    continue;
                }

                if (it.expiresUnix <= 0)
                {
                    kept.Add(it);
                    continue;
                }

                if (nowUnix >= it.expiresUnix)
                {
                    gone++;
                    continue;
                }

                kept.Add(it);
            }

            inv.backpack.items = kept.ToArray();
            return gone;
        }

        public static void PauseBegin(PlayerInventory inv, long nowUnix)
        {
            if (inv == null || inv.soulPauseStartedUnix > 0) return;
            inv.soulPauseStartedUnix = nowUnix;
        }

        public static void PauseEnd(PlayerInventory inv, long nowUnix)
        {
            if (inv == null || inv.soulPauseStartedUnix <= 0) return;
            var delta = nowUnix - inv.soulPauseStartedUnix;
            inv.soulPauseStartedUnix = 0;
            if (delta <= 0 || inv.backpack?.items == null) return;
            foreach (var it in inv.backpack.items)
            {
                if (it == null || it.Kind != BackpackItemKind.SoulCard) continue;
                if (it.expiresUnix <= 0) continue;
                it.expiresUnix += delta;
            }
        }

        public static void UnfreezeOnLeaveHome(PlayerInventory inv, long nowUnix)
        {
            if (inv?.backpack?.items == null) return;
            foreach (var it in inv.backpack.items)
            {
                if (it == null || it.Kind != BackpackItemKind.SoulCard) continue;
                if (it.expiresUnix != 0) continue;
                it.expiresUnix = nowUnix + DurationHours(it.rarityBand) * 3600L;
            }
        }

        static int PutInBoxes(PlayerInventory inv, int cardId, int qty, bool carriedOnly)
        {
            if (inv.storageBoxes == null || inv.storageBoxes.Length == 0) return qty;
            var left = qty;

            bool TryBox(StorageBoxState box)
            {
                if (box == null) return false;
                box.EnsureValid();
                var free = box.FreeSlots();
                if (free <= 0) return false;
                var put = Mathf.Min(left, free);
                AddToStacks(ref box.stacks, cardId, put);
                left -= put;
                return left <= 0;
            }

            if (carriedOnly)
            {
                var preferred = inv.preferredStorageBoxIndex;
                if (preferred >= 0 && preferred < inv.storageBoxes.Length)
                {
                    var box = inv.storageBoxes[preferred];
                    if (box != null && !box.atHome && TryBox(box)) return 0;
                }

                var best = -1;
                var bestFree = -1;
                for (var i = 0; i < inv.storageBoxes.Length; i++)
                {
                    var box = inv.storageBoxes[i];
                    if (box == null || box.atHome) continue;
                    var free = box.FreeSlots();
                    if (free > bestFree)
                    {
                        bestFree = free;
                        best = i;
                    }
                }

                if (best >= 0 && TryBox(inv.storageBoxes[best]) && left <= 0) return 0;
                for (var i = 0; i < inv.storageBoxes.Length && left > 0; i++)
                {
                    var box = inv.storageBoxes[i];
                    if (box == null || box.atHome) continue;
                    TryBox(box);
                }

                return left;
            }

            for (var i = 0; i < inv.storageBoxes.Length && left > 0; i++)
                TryBox(inv.storageBoxes[i]);
            return left;
        }

        static bool TryPlaceSoulTile(PlayerInventory inv, int cardId, bool atHome, long nowUnix)
        {
            inv.backpack ??= new BackpackState();
            inv.backpack.EnsureValid();
            if (!PlayerInventory.TryFindFreeCell(inv.backpack, 1, 1, out var x, out var y))
                return false;

            var list = new List<BackpackItem>(inv.backpack.items ?? System.Array.Empty<BackpackItem>());
            list.Add(new BackpackItem
            {
                Kind = BackpackItemKind.SoulCard,
                refIndex = -1,
                gridX = x,
                gridY = y,
                gridW = 1,
                gridH = 1,
                label = "Soul",
                cardId = cardId,
                expiresUnix = atHome ? 0 : nowUnix + DurationHours(BandCommonRare) * 3600L,
                rarityBand = BandCommonRare
            });
            inv.backpack.items = list.ToArray();
            return true;
        }

        static void AddToStacks(ref CardStackEntry[] stacks, int cardId, int qty)
        {
            stacks ??= System.Array.Empty<CardStackEntry>();
            var list = new List<CardStackEntry>(stacks);
            var found = false;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].cardId != cardId) continue;
                list[i].qty += qty;
                found = true;
                break;
            }

            if (!found)
                list.Add(new CardStackEntry { cardId = cardId, qty = qty });
            stacks = list.ToArray();
        }
    }
}
