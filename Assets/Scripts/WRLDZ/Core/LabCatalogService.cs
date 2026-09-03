using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Lab / Desktop test account helpers: unlock full card catalog into home storage
    /// so deck editor and duels can use any card without tablet SE grinding.
    /// </summary>
    public static class LabCatalogService
    {
        public const int CopiesPerCard = 3; // TCG max copies
        static bool _granting;

        /// <summary>
        /// Ensure <paramref name="acc"/> owns <see cref="CopiesPerCard"/> of every card in the DB.
        /// Expands home storage boxes as needed (1000 slots each, max 20).
        /// Safe to call repeatedly — only tops up missing copies.
        /// </summary>
        public static int GrantFullCatalog(LocalAccountStore.Account acc, CardDatabase db = null)
        {
            if (acc == null || _granting) return 0;
            _granting = true;
            try
            {
                return GrantFullCatalogBody(acc, db);
            }
            finally
            {
                _granting = false;
            }
        }

        static int GrantFullCatalogBody(LocalAccountStore.Account acc, CardDatabase db)
        {
            if (acc == null) return 0;
            acc.EnsureProgress();
            acc.EnsureInventory();
            db ??= CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                Debug.LogWarning("[WRLDZ Lab] No card database — cannot grant catalog.");
                return 0;
            }

            var inv = acc.inventory;
            // Set before EnsureValid — SyncBackpackOccupancy otherwise force-homes 4×3 boxes.
            inv.labCarryAllStorage = true;
            inv.EnsureValid();
            KeepCatalogOnHand(inv);
            // Always top up to CopiesPerCard. The granted flag + "owned ≥ unique cards"
            // used to skip this and leave the test user 1-of-each.

            // Multiple open deck boxes for select-deck UI testing
            if (inv.deckBoxSlotCount < 3)
                inv.deckBoxSlotCount = 3;
            inv.EnsureDeckBoxSlots();
            if (inv.deckBoxes != null && inv.deckBoxes.Length > 0 && inv.deckBoxes[0] != null)
                inv.deckBoxes[0].occupied = true;

            EnsureStorageCapacity(inv, db.Count * CopiesPerCard);
            // Lab: entire catalog is on-hand (travel) so deck editor doesn't grey everything
            if (inv.storageBoxes != null)
            {
                foreach (var b in inv.storageBoxes)
                {
                    if (b == null) continue;
                    b.atHome = false;
                    if (string.IsNullOrEmpty(b.name) ||
                        b.name.IndexOf("Home", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        b.name = "Lab Travel Case";
                }
            }

            var granted = 0;
            var leftoverTotal = 0;
            foreach (var def in db.GetAllCards())
            {
                if (def == null || def.id <= 0) continue;
                var have = inv.CountOf(def.id);
                var need = CopiesPerCard - have;
                if (need <= 0) continue;
                var left = inv.AddCards(def.id, need);
                granted += need - left;
                leftoverTotal += left;
            }

            // Lab tester: every copy stays on-hand. Do not siphon into a home box.
            KeepCatalogOnHand(inv);
            acc.progress.labFullCatalogGranted = true;
            // Write the account file only. Do NOT call ProgressionService.Persist —
            // that re-enters AppSession.SetAccount and used to recurse forever.
            if (granted > 0 || leftoverTotal > 0)
                LocalAccountStore.UpdateAccount(acc);
            Debug.Log(
                $"[WRLDZ Lab] Full catalog · granted={granted} leftover={leftoverTotal} " +
                $"storageBoxes={inv.storageBoxes?.Length ?? 0} · totalOwned≈{inv.TotalStorageUsed()}");
            return granted;
        }

        /// <summary>True when account already marked as full-catalog lab.</summary>
        public static bool IsGranted(LocalAccountStore.Account acc) =>
            acc?.progress != null && acc.progress.labFullCatalogGranted;

        /// <summary>
        /// Lab boxes stay travel / on-hand even when the backpack cannot hold them.
        /// Safe to call after <see cref="PlayerInventory.EnsureValid"/>.
        /// </summary>
        public static bool KeepCatalogOnHand(PlayerInventory inv)
        {
            if (inv == null) return false;
            var dirty = !inv.labCarryAllStorage;
            inv.labCarryAllStorage = true;
            if (inv.storageBoxes == null) return dirty;
            foreach (var b in inv.storageBoxes)
            {
                if (b == null) continue;
                if (b.atHome)
                {
                    b.atHome = false;
                    dirty = true;
                }

                if (string.IsNullOrEmpty(b.name) ||
                    b.name.IndexOf("Home", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    b.name = "Lab Travel Case";
                    dirty = true;
                }
            }

            return dirty;
        }

        static void EnsureStorageCapacity(PlayerInventory inv, int minSlots)
        {
            inv.EnsureValid();
            inv.storageBoxes ??= System.Array.Empty<StorageBoxState>();
            var capacity = 0;
            foreach (var b in inv.storageBoxes)
                if (b != null) capacity += Mathf.Max(100, b.capacity);

            var list = new List<StorageBoxState>(inv.storageBoxes);
            var n = 0;
            while (capacity < minSlots && list.Count < PlayerInventory.MaxStorageBoxes)
            {
                // Lab boxes are on-hand (travel) so deck editor treats them as available, not grey home
                list.Add(new StorageBoxState
                {
                    name = list.Count == 0
                        ? "Lab Travel Catalog (1000)"
                        : $"Lab Travel Catalog {list.Count + 1}",
                    capacity = PlayerInventory.StorageBoxSizeDefault,
                    atHome = false,
                    stacks = System.Array.Empty<CardStackEntry>()
                });
                capacity += PlayerInventory.StorageBoxSizeDefault;
                n++;
            }

            // Existing home-only boxes: leave as-is; new grants fill free space including travel boxes
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                // Prefer marking pure lab catalog boxes as travel
                if (list[i].name != null &&
                    list[i].name.IndexOf("Lab", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    list[i].atHome = false;
            }

            inv.storageBoxes = list.ToArray();
            if (n > 0)
                Debug.Log($"[WRLDZ Lab] Expanded storage +{n} boxes → capacity≥{capacity}");
        }

        /// <summary>Tiny home box with a few cards so editor shows greyed home-base section.</summary>
        static void EnsureSampleHomeBox(PlayerInventory inv)
        {
            if (inv?.storageBoxes == null) return;
            foreach (var b in inv.storageBoxes)
                if (b != null && b.atHome) return;

            if (inv.storageBoxes.Length >= PlayerInventory.MaxStorageBoxes) return;

            var homeStacks = new List<CardStackEntry>();
            var moved = 0;
            foreach (var box in inv.storageBoxes)
            {
                if (box == null || box.atHome || box.stacks == null) continue;
                var stacks = new List<CardStackEntry>();
                foreach (var s in box.stacks)
                {
                    if (s == null || s.cardId <= 0 || s.qty <= 0) continue;
                    if (moved < 8)
                    {
                        homeStacks.Add(new CardStackEntry { cardId = s.cardId, qty = 1 });
                        s.qty -= 1;
                        moved++;
                    }

                    if (s.qty > 0) stacks.Add(s);
                }

                box.stacks = stacks.ToArray();
                break;
            }

            var list = new List<StorageBoxState>(inv.storageBoxes)
            {
                new StorageBoxState
                {
                    name = "Home Card Box (sample)",
                    capacity = PlayerInventory.StorageBoxSizeSmall,
                    atHome = true,
                    stacks = homeStacks.ToArray()
                }
            };
            inv.storageBoxes = list.ToArray();
        }
    }
}
