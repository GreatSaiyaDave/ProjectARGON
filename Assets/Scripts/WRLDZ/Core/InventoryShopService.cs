using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Buy home card boxes (100/500/1000), binders, and binder pages with Digizeni / Duel Coins.
    /// </summary>
    public static class InventoryShopService
    {
        public static bool TryBuyStorageBox(LocalAccountStore.Account acc, int size, out string error)
        {
            error = null;
            if (acc == null) { error = "No account."; return false; }
            acc.EnsureProgress();
            acc.EnsureInventory();
            var inv = acc.inventory;
            inv.EnsureValid();

            if (!inv.CanBuyStorageBox(size))
            {
                error = "Cannot buy that box (invalid size or at max boxes).";
                return false;
            }

            var price = PlayerInventory.DigiPriceForBox(size);
            if (!ArtifactService.TrySpend(acc, ArtifactService.Digizeni, price, out error))
                return false;
            var list = new System.Collections.Generic.List<StorageBoxState>(inv.storageBoxes ?? System.Array.Empty<StorageBoxState>());
            list.Add(new StorageBoxState
            {
                name = $"Card Box ({size})",
                capacity = size,
                stacks = System.Array.Empty<CardStackEntry>(),
                atHome = true
            });
            inv.storageBoxes = list.ToArray();
            ProgressionService.Persist(acc);
            Debug.Log($"[WRLDZ Inv] Bought storage box {size} for Đ{price}");
            return true;
        }

        public static bool TryBuyBinder(LocalAccountStore.Account acc, out string error)
        {
            error = null;
            if (acc == null) { error = "No account."; return false; }
            acc.EnsureProgress();
            acc.EnsureInventory();
            var inv = acc.inventory;
            inv.EnsureValid();

            if (!inv.CanBuyBinder())
            {
                error = "Max binders reached.";
                return false;
            }

            var price = PlayerInventory.PriceBinderDigi;
            if (!ArtifactService.TrySpend(acc, ArtifactService.Digizeni, price, out error))
                return false;
            var list = new System.Collections.Generic.List<BinderState>(inv.binders ?? System.Array.Empty<BinderState>());
            list.Add(new BinderState
            {
                name = "Card Binder",
                pageCount = PlayerInventory.BinderFreePages,
                atHome = true
            });
            inv.binders = list.ToArray();
            inv.binderCount = inv.binders.Length;
            ProgressionService.Persist(acc);
            Debug.Log($"[WRLDZ Inv] Bought binder for Đ{price} · pages={PlayerInventory.BinderFreePages}");
            return true;
        }

        /// <summary>Add one page to a binder (max 20). Prefer Digizeni; optional Duel Coin price.</summary>
        public static bool TryBuyBinderPage(LocalAccountStore.Account acc, int binderIndex,
            bool payWithDuelCoins, out string error)
        {
            error = null;
            if (acc == null) { error = "No account."; return false; }
            acc.EnsureProgress();
            acc.EnsureInventory();
            var inv = acc.inventory;
            inv.EnsureValid();

            if (!inv.CanBuyBinderPage(binderIndex))
            {
                error = "Cannot add page (max 20 or invalid binder).";
                return false;
            }

            if (payWithDuelCoins)
            {
                var price = PlayerInventory.PriceBinderPageDc;
                if (!ArtifactService.TrySpend(acc, ArtifactService.DuelCoin, price, out error))
                    return false;
            }
            else
            {
                var price = PlayerInventory.PriceBinderPageDigi;
                if (!ArtifactService.TrySpend(acc, ArtifactService.Digizeni, price, out error))
                    return false;
            }

            var b = inv.binders[binderIndex];
            b.pageCount++;
            b.EnsureValid(); // resizes slots array
            ProgressionService.Persist(acc);
            Debug.Log(
                $"[WRLDZ Inv] Binder {binderIndex} page → {b.pageCount}/{PlayerInventory.BinderMaxPages} " +
                $"(cap {b.MaxCards} cards)");
            return true;
        }
    }
}
