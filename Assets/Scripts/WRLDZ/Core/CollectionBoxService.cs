using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Player collection box: unlimited storage that travels with the player,
    /// filled with infinite copies of every card in the database.
    /// Unlike <see cref="LabCatalogService"/> this is player-facing (no lab door)
    /// and never touches backpack grid, storage totals, or trade loot pools.
    /// Owner-only: auto-grants apply to Great SaiyaDave alone, never new characters.
    /// </summary>
    public static class CollectionBoxService
    {
        /// <summary>Copies per card written into the box — reads as infinite (deck needs max 3).</summary>
        public const int InfiniteCopiesPerCard = CollectionBoxState.InfiniteQtyPerCard;

        /// <summary>
        /// True when <paramref name="acc"/> is the owner's account (Great SaiyaDave).
        /// Matches username variants (greatsaiyadave, great_saiyadave, great-saiyadave, …)
        /// or the Great SaiyaDave display name (covers the lab_tester door, whose display
        /// name is also Great SaiyaDave). Everything else is a new/ordinary character.
        /// </summary>
        public static bool IsOwnerAccount(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            var user = (acc.username ?? "").Trim().ToLowerInvariant()
                .Replace("_", "").Replace("-", "").Replace(".", "").Replace(" ", "");
            if (user == "greatsaiyadave") return true;
            var display = (acc.displayName ?? "").Trim();
            if (string.Equals(display, "Great SaiyaDave", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>Ensure the account owns the unlimited collection box (empty if new).</summary>
        public static void EnsureOwned(LocalAccountStore.Account acc)
        {
            if (acc == null) return;
            acc.EnsureInventory();
            acc.inventory.EnsureCollectionBox();
        }

        /// <summary>
        /// Fill <paramref name="inv"/> collection box with infinite copies of every card in
        /// <paramref name="db"/>. Safe to call repeatedly — only tops up missing cards and
        /// refreshes depleted stacks back to infinite.
        /// Returns number of card ids topped up.
        /// </summary>
        public static int GrantInfiniteAll(PlayerInventory inv, CardDatabase db,
            int copiesPerCard = InfiniteCopiesPerCard)
        {
            if (inv == null) return 0;
            inv.EnsureCollectionBox();
            db ??= CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                Debug.LogWarning("[WRLDZ Collection] No card database — cannot grant collection.");
                return 0;
            }

            copiesPerCard = Mathf.Max(3, copiesPerCard);
            var box = inv.collectionBox;
            box.infiniteCopies = true;
            box.unlimitedStorage = true;
            box.EnsureValid();

            var map = new Dictionary<int, int>();
            if (box.stacks != null)
            {
                foreach (var s in box.stacks)
                {
                    if (s == null || s.cardId <= 0) continue;
                    // Keep the largest qty seen (protects hand-added finite stacks).
                    if (!map.TryGetValue(s.cardId, out var q) || s.qty > q)
                        map[s.cardId] = Mathf.Max(0, s.qty);
                }
            }

            var toppedUp = 0;
            foreach (var def in db.GetAllCards())
            {
                if (def == null || def.id <= 0) continue;
                if (!map.TryGetValue(def.id, out var have) || have < copiesPerCard)
                {
                    map[def.id] = copiesPerCard;
                    toppedUp++;
                }
            }

            var next = new CardStackEntry[map.Count];
            var i = 0;
            foreach (var kv in map)
                next[i++] = new CardStackEntry { cardId = kv.Key, qty = kv.Value };
            box.stacks = next;
            box.EnsureValid();
            return toppedUp;
        }

        /// <summary>
        /// Give <paramref name="acc"/> the unlimited collection box filled with infinite
        /// copies of every card. Persists the account. Returns ids topped up.
        /// Owner-only: refused for any other character (returns 0, no changes).
        /// </summary>
        public static int GrantFullCollection(LocalAccountStore.Account acc, CardDatabase db = null)
        {
            if (acc == null) return 0;
            if (!IsOwnerAccount(acc))
            {
                Debug.Log($"[WRLDZ Collection] Grant refused for {acc.username} — Great SaiyaDave only.");
                return 0;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            var n = GrantInfiniteAll(acc.inventory, db);
            acc.cardsCollected = acc.inventory.TotalStorageUsed();
            LocalAccountStore.UpdateAccount(acc);
            Debug.Log(
                $"[WRLDZ Collection] Infinite collection granted · toppedUp={n} " +
                $"unique={acc.inventory.collectionBox?.UniqueCount() ?? 0}");
            return n;
        }

        /// <summary>True when every card in <paramref name="db"/> is infinite in the box.</summary>
        public static bool IsFullyGranted(PlayerInventory inv, CardDatabase db)
        {
            if (inv?.collectionBox == null || !inv.collectionBox.owned) return false;
            if (db == null || db.Count == 0) return false;
            foreach (var def in db.GetAllCards())
            {
                if (def == null || def.id <= 0) continue;
                if (inv.collectionBox.CountOf(def.id) < 3) return false;
            }

            return true;
        }
    }
}
