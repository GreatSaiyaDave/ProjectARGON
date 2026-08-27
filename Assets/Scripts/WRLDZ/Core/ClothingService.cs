using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Outfit pockets are the only on-hand deck limit.
    /// Default clothes = 1 pocket = 1 deck. Buy extra-pocket clothes at bazaar
    /// clothiers, or pay a tailor to sew pockets onto owned shirts/bottoms.
    /// </summary>
    public static class ClothingService
    {
        public static void EnsureWardrobe(LocalAccountStore.Account acc)
        {
            if (acc == null) return;
            if (acc.avatar == null) acc.avatar = AvatarAppearance.Default();
            if (acc.inventory == null) acc.inventory = PlayerInventory.Empty();
            EnsureOutfit(acc.avatar, acc.inventory);
        }

        public static void EnsureOutfit(AvatarAppearance avatar, PlayerInventory inv)
        {
            if (avatar == null) avatar = AvatarAppearance.Default();
            avatar.EnsureClothingDefaults();
            if (inv == null) return;
            inv.wardrobe ??= new ClothingWardrobe();
            inv.wardrobe.EnsureValid();
            ClothingCatalog.GrantStarter(inv.wardrobe);
            GrantIfMissing(inv, avatar.hatId);
            GrantIfMissing(inv, avatar.facialId);
            GrantIfMissing(inv, avatar.shirtId);
            GrantIfMissing(inv, avatar.handsId);
            GrantIfMissing(inv, avatar.bottomsId);
            GrantIfMissing(inv, avatar.shoesId);
            SyncPockets(avatar, inv);
        }

        static void GrantIfMissing(PlayerInventory inv, string id)
        {
            if (string.IsNullOrEmpty(id) || inv?.wardrobe == null) return;
            inv.wardrobe.Grant(id);
        }

        public static void SyncPockets(LocalAccountStore.Account acc)
        {
            if (acc?.inventory == null) return;
            SyncPockets(acc.avatar, acc.inventory);
        }

        public static void SyncPockets(AvatarAppearance look, PlayerInventory inv)
        {
            if (inv == null) return;
            inv.wardrobe ??= new ClothingWardrobe();
            var n = ClothingCatalog.ComputeOutfitPockets(look, inv.wardrobe);
            inv.carryDeckBoxSlots = n;
            inv.pockets ??= new AvatarPocketState();
            inv.pockets.pocketCount = n;
            inv.pockets.EnsureValid(inv.deckBoxes?.Length ?? 0, n);
        }

        public static int PreviewPockets(AvatarAppearance look, ClothingWardrobe wardrobe) =>
            ClothingCatalog.ComputeOutfitPockets(look, wardrobe);

        public static bool TryBuy(LocalAccountStore.Account acc, string itemId, out string error)
        {
            error = null;
            if (!Ready(acc, out error)) return false;
            var item = ClothingCatalog.Get(itemId);
            if (item == null)
            {
                error = "Unknown clothing.";
                return false;
            }

            if (item.EmptySlot || item.StarterOwned)
            {
                error = "Already owned.";
                return false;
            }

            if (acc.inventory.wardrobe.Owns(item.Id))
            {
                error = "Already owned.";
                return false;
            }

            if (item.PriceDigi <= 0)
            {
                error = "Not for sale.";
                return false;
            }

            acc.EnsureProgress();
            if (acc.progress.digizeni < item.PriceDigi)
            {
                error = $"Need Đ{item.PriceDigi} (have {acc.progress.digizeni}).";
                return false;
            }

            acc.progress.digizeni -= item.PriceDigi;
            acc.inventory.wardrobe.Grant(item.Id);
            Persist(acc);
            return true;
        }

        public static bool TryEquip(LocalAccountStore.Account acc, string itemId, out string error)
        {
            error = null;
            if (!Ready(acc, out error)) return false;
            var item = ClothingCatalog.Get(itemId);
            if (item == null)
            {
                error = "Unknown clothing.";
                return false;
            }

            if (!acc.inventory.wardrobe.Owns(item.Id) && !item.StarterOwned)
            {
                error = "Not owned.";
                return false;
            }

            acc.avatar.SetSlot(item.Slot, item.Id);
            SyncPockets(acc);
            Persist(acc);
            return true;
        }

        public static bool TryBuyAndEquip(LocalAccountStore.Account acc, string itemId, out string error)
        {
            if (!Ready(acc, out error)) return false;
            var item = ClothingCatalog.Get(itemId);
            if (item == null)
            {
                error = "Unknown clothing.";
                return false;
            }

            if (!acc.inventory.wardrobe.Owns(item.Id) && !item.StarterOwned)
            {
                if (!TryBuy(acc, itemId, out error)) return false;
            }

            return TryEquip(acc, itemId, out error);
        }

        /// <summary>Sew +1 pocket onto an owned shirt or bottoms. Price scales with extras already sewn.</summary>
        public static bool TrySewPocket(LocalAccountStore.Account acc, string itemId, out string error)
        {
            error = null;
            if (!Ready(acc, out error)) return false;
            var item = ClothingCatalog.Get(itemId);
            if (item == null)
            {
                error = "Unknown clothing.";
                return false;
            }

            if (!item.CanHoldPockets || item.MaxSewn <= 0)
            {
                error = "The tailor cannot sew pockets onto that.";
                return false;
            }

            if (!acc.inventory.wardrobe.Owns(item.Id) && !item.StarterOwned)
            {
                error = "Bring clothing you own.";
                return false;
            }

            var extra = acc.inventory.wardrobe.SewnExtra(item.Id);
            if (extra >= item.MaxSewn)
            {
                error = "No more fabric on that piece.";
                return false;
            }

            var look = acc.avatar.Clone();
            var previewWardrobe = CloneWardrobe(acc.inventory.wardrobe);
            previewWardrobe.SetSewnExtra(item.Id, extra + 1);
            var nextTotal = ClothingCatalog.ComputeOutfitPockets(look, previewWardrobe);
            var currentTotal = ClothingCatalog.ComputeOutfitPockets(look, acc.inventory.wardrobe);
            if (IsEquipped(acc.avatar, item.Id) && nextTotal <= currentTotal && currentTotal >= ClothingCatalog.MaxOutfitPockets)
            {
                error = "Outfit already carries the max of 6 decks.";
                return false;
            }

            var price = SewPrice(extra);
            acc.EnsureProgress();
            if (acc.progress.digizeni < price)
            {
                error = $"Need Đ{price} (have {acc.progress.digizeni}).";
                return false;
            }

            acc.progress.digizeni -= price;
            acc.inventory.wardrobe.SetSewnExtra(item.Id, extra + 1);
            SyncPockets(acc);
            Persist(acc);
            return true;
        }

        public static int SewPrice(int currentExtra) =>
            ClothingCatalog.PriceSewnPocketDigi * Mathf.Max(1, currentExtra + 1);

        public static bool IsEquipped(AvatarAppearance look, string id)
        {
            if (look == null || string.IsNullOrEmpty(id)) return false;
            return look.hatId == id || look.facialId == id || look.shirtId == id
                   || look.handsId == id || look.bottomsId == id || look.shoesId == id;
        }

        public static IEnumerable<ClothingItem> SewTargets(ClothingWardrobe wardrobe)
        {
            for (var i = 0; i < ClothingCatalog.All.Length; i++)
            {
                var it = ClothingCatalog.All[i];
                if (!it.CanHoldPockets || it.MaxSewn <= 0) continue;
                if (it.StarterOwned || (wardrobe != null && wardrobe.Owns(it.Id)))
                    yield return it;
            }
        }

        static ClothingWardrobe CloneWardrobe(ClothingWardrobe src)
        {
            var w = new ClothingWardrobe();
            w.EnsureValid();
            if (src == null) return w;
            src.EnsureValid();
            w.ownedIds = (string[])src.ownedIds.Clone();
            w.sewn = new ClothingSewEntry[src.sewn.Length];
            for (var i = 0; i < src.sewn.Length; i++)
            {
                var e = src.sewn[i];
                w.sewn[i] = e == null
                    ? null
                    : new ClothingSewEntry { itemId = e.itemId, extraPockets = e.extraPockets };
            }

            return w;
        }

        static bool Ready(LocalAccountStore.Account acc, out string error)
        {
            error = null;
            if (acc == null)
            {
                error = "No account.";
                return false;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            EnsureWardrobe(acc);
            return true;
        }

        static void Persist(LocalAccountStore.Account acc)
        {
            ProgressionService.Persist(acc);
            AppSession.Ensure()?.RefreshFromStore();
        }
    }
}
