using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Grants first-session kit: backpack, binder, deck box, duel disk, tome item, starter deck.
    /// </summary>
    public static class StarterKitService
    {
        public static bool GrantIfNeeded(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            acc.EnsureProgress();
            acc.EnsureInventory();
            if (acc.progress.onboardingStarterGranted) return false;

            var inv = acc.inventory;
            inv.hasBackpack = true;
            inv.hasDuelDisk = true;
            inv.deckBoxSlotCount = 1;
            inv.carryDeckBoxSlots = 1;
            inv.unlockedThirdDeckCarry = false;
            inv.hasDigizeniHolder = true;
            inv.hasDuelCoinHolder = true;
            inv.hasSetEnergyHolder = true;
            inv.tradeTransportCharges = 0;
            inv.tome.hasTomeItem = true;
            inv.tome.pathTeam = acc.progress.kuribohTeam;

            // Travel pack (story grows tier) + currency holders auto-placed on EnsureValid
            inv.backpack = new BackpackState { capacityTier = 1, unlocked = true };
            inv.pockets = new AvatarPocketState { pocketCount = 1 };
            inv.wardrobe = new ClothingWardrobe();
            ClothingCatalog.GrantStarter(inv.wardrobe);
            if (acc.avatar == null) acc.avatar = AvatarAppearance.Default();
            acc.avatar.EnsureClothingDefaults();
            ClothingService.EnsureOutfit(acc.avatar, inv);
            inv.artifactDeckBox = new ArtifactDeckBoxState
            {
                owned = true,
                atHome = true,
                name = "Artifact Deck Box"
            };

            // Home bulk storage: one 1000-card cardboard box
            inv.storageBoxes = new[]
            {
                new StorageBoxState
                {
                    name = "Home Card Box (1000)",
                    capacity = PlayerInventory.StorageBoxSizeDefault,
                    stacks = System.Array.Empty<CardStackEntry>(),
                    atHome = true
                }
            };

            // One free binder with 5 pages (90 slots · expandable to 20 pages / 360)
            inv.binders = new[]
            {
                new BinderState
                {
                    name = "Starter Binder",
                    pageCount = PlayerInventory.BinderFreePages,
                    atHome = true
                }
            };
            inv.binderCount = 1;

            var team = acc.progress.Team;
            var deckFile = KuribohTeamInfo.StarterDeckFile(team);
            var deck = CardDatabase.LoadDeck(deckFile) ?? CardDatabase.LoadDeck("player_starter.json");
            if (deck != null)
            {
                var main = Expand(deck.main);
                var extra = Expand(deck.extra);
                var side = Expand(deck.side);

                // Collection copies land in the home card box
                foreach (var id in main) inv.AddCards(id, 1);
                foreach (var id in extra) inv.AddCards(id, 1);
                foreach (var id in side) inv.AddCards(id, 1);

                inv.deckBoxes = new[]
                {
                    new DeckBoxState
                    {
                        name = KuribohTeamInfo.DisplayName(team) + " Starter",
                        occupied = true,
                        main = main.ToArray(),
                        extra = extra.ToArray(),
                        side = side.ToArray()
                    }
                };
            }
            else
            {
                Debug.LogWarning("[WRLDZ] Starter deck missing — empty deck box granted.");
                inv.deckBoxes = new[]
                {
                    new DeckBoxState { name = "Main Deck Box", occupied = true }
                };
            }

            inv.EnsureValid();
            acc.progress.onboardingStarterGranted = true;
            acc.progress.digizeni = Mathf.Max(acc.progress.digizeni, 500);
            acc.cardsCollected = inv.TotalStorageUsed();
            TomeService.SyncCapacityUnlocks(acc);
            ProgressionService.Persist(acc);
            Debug.Log(
                $"[WRLDZ] Starter kit granted for {acc.username} team={team} · " +
                $"box {inv.TotalStorageUsed()}/{inv.TotalStorageCapacity()} · " +
                $"binder {inv.binders[0].pageCount}p");
            return true;
        }

        static List<int> Expand(DeckCardEntry[] entries)
        {
            var list = new List<int>();
            if (entries == null) return list;
            foreach (var e in entries)
            {
                if (e == null || e.id <= 0) continue;
                var q = Mathf.Max(1, e.qty);
                for (var i = 0; i < q; i++)
                    list.Add(e.id);
            }

            return list;
        }
    }
}
