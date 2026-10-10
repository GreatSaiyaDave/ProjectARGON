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
            // Owner-only box (Great SaiyaDave): top up the unlimited box once.
            // Any other character keeps no collection box — revoke strays, never grant.
            if (acc.progress.onboardingStarterGranted)
            {
                try
                {
                    var existing = acc.inventory;
                    if (CollectionBoxService.IsOwnerAccount(acc))
                    {
                        existing.EnsureCollectionBox();
                        var box = existing.collectionBox;
                        if (box != null && box.owned && box.UniqueCount() == 0)
                        {
                            CollectionBoxService.GrantInfiniteAll(existing, CardDatabase.Load());
                            ProgressionService.Persist(acc);
                            return true;
                        }
                    }
                    else if (existing.collectionBox != null && existing.collectionBox.owned)
                    {
                        existing.collectionBox.owned = false;
                        existing.collectionBox.stacks = System.Array.Empty<CardStackEntry>();
                        ProgressionService.Persist(acc);
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[WRLDZ] Collection box top-up deferred: {ex.Message}");
                }

                return false;
            }

            // Load the deck before touching the inventory. Without it the kit is NOT marked granted, so the
            // next GrantIfNeeded (after the deck file is fixed / readable) still hands out the real starter
            // instead of a permanent empty deck box (QC F9).
            var team = acc.progress.Team;
            var deckFile = KuribohTeamInfo.StarterDeckFile(team);
            var deck = CardDatabase.LoadDeck(deckFile) ?? CardDatabase.LoadDeck("player_starter.json");
            if (deck == null || deck.main == null || deck.main.Length == 0)
            {
                Debug.LogError(
                    $"[WRLDZ] Starter kit not granted for {acc.username}: starter deck '{deckFile}' " +
                    "(and player_starter.json) could not be loaded. Will retry on the next call.");
                return false;
            }

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
                atHome = false,
                name = "Artifact Deck Box",
                instances = System.Array.Empty<ArtifactInstance>()
            };
            // Unlimited collection box: Great SaiyaDave alone. New characters get no box.
            if (CollectionBoxService.IsOwnerAccount(acc))
            {
                inv.collectionBox = new CollectionBoxState
                {
                    owned = true,
                    name = "Collection Box",
                    stacks = System.Array.Empty<CardStackEntry>(),
                    unlimitedStorage = true,
                    infiniteCopies = true
                };
            }
            else
            {
                inv.collectionBox = new CollectionBoxState
                {
                    owned = false,
                    name = "Collection Box",
                    stacks = System.Array.Empty<CardStackEntry>(),
                    unlimitedStorage = true,
                    infiniteCopies = true
                };
            }

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

            inv.EnsureValid();
            // Great SaiyaDave starts with infinite of every card in the unlimited box.
            // Anyone else keeps no box (see above). Failure (missing DB) is non-fatal —
            // the session-start top-up retries later.
            if (CollectionBoxService.IsOwnerAccount(acc))
            {
                try
                {
                    CollectionBoxService.GrantInfiniteAll(inv, CardDatabase.Load());
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[WRLDZ] Collection box grant deferred: {ex.Message}");
                }
            }

            acc.progress.onboardingStarterGranted = true;
            acc.progress.digizeni = Mathf.Max(acc.progress.digizeni, 500);
            ArtifactService.MigrateFromLegacy(acc.progress, inv);
            ArtifactService.Grant(acc.progress, inv, ArtifactService.Tome, 1);
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
