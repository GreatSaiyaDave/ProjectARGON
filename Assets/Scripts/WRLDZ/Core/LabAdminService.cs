using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// Maxes the Desktop Lab test account (Great SaiyaDave / lab_tester)
    /// into a local admin: full catalog, cap currencies, every unlock.
    /// Not for live players.
    /// </summary>
    public static class LabAdminService
    {
        public const int AdminCurrency = 9_999_999;
        public const int AdminTradeCharges = 99;
        public const int AdminSeals = 12;
        public const float AdminPathKm = 10_000f;
        public const int AdminDuels = 999;

        static readonly int[] AllTomePages =
        {
            53129443, 12580477, 44095762,
            19613556, 66788016, 40605147,
            83764718, 55144522, 32807846
        };

        public static bool IsAdmin(LocalAccountStore.Account acc) =>
            acc != null && (acc.progress != null && acc.progress.labAdmin
                            || acc.username == AppSession.LabTestUsername);

        /// <summary>
        /// Idempotent. Catalog + level 100 + story complete + currencies + soul +
        /// sets + pack + clothes + tome. Does not persist — caller writes the file.
        /// </summary>
        public static void MaxOut(LocalAccountStore.Account acc)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            acc.EnsureInventory();
            acc.deactivated = false;
            acc.deactivatedUtc = "";
            acc.deactivatedReason = "";
            acc.displayName = string.IsNullOrEmpty(acc.displayName)
                ? "Great SaiyaDave"
                : acc.displayName;

            LabCatalogService.GrantFullCatalog(acc);

            MaxProgress(acc);
            MaxInventory(acc);
            MaxClothes(acc);
            MaxTome(acc);

            acc.spiritRank = acc.progress.level;
            acc.sealsUnlocked = Mathf.Max(acc.sealsUnlocked, AdminSeals);
            acc.pathKm = Mathf.Max(acc.pathKm, AdminPathKm);
            acc.duelsCompleted = Mathf.Max(acc.duelsCompleted, AdminDuels);
            acc.cardsCollected = acc.inventory.TotalStorageUsed();

            Debug.Log(
                $"[WRLDZ Lab] Admin max-out · {acc.username} · Lv{acc.progress.level} · " +
                $"Đ{acc.progress.digizeni} · boxes={acc.inventory.storageBoxes?.Length ?? 0} · " +
                $"owned≈{acc.cardsCollected}");
        }

        static void MaxProgress(LocalAccountStore.Account acc)
        {
            var p = acc.progress;
            p.labAdmin = true;
            p.labFullCatalogGranted = true;
            p.storyModeComplete = true;
            p.level = PlayerProgress.SoftLevelCap;
            p.xp = 0;
            var digi = Mathf.Max(p.digizeni, AdminCurrency);
            var coins = Mathf.Max(p.duelCoin, AdminCurrency);
            var se = Mathf.Max(p.setEnergy, AdminCurrency);
            ArtifactService.SetQty(p, acc.inventory, ArtifactService.Digizeni, digi);
            ArtifactService.SetQty(p, acc.inventory, ArtifactService.DuelCoin, coins);
            ArtifactService.SetQty(p, acc.inventory, ArtifactService.SetEnergyId("LOB"), se);
            p.onboardingPrologueDone = true;
            p.onboardingKuribohChosen = true;
            p.onboardingStarterGranted = true;
            p.onboardingTutorialDuelDone = true;
            p.onboardingComplete = true;
            ErazProgress.GrantTutorialBadge(acc);
            if (p.kuribohTeam == 0)
                p.kuribohTeam = (int)KuribohTeam.Kuribandit;

            p.soulFractureCapacity = PlayerProgress.MaxSoulFractureCapacity;
            p.soulFractures = 0;
            p.soulRegenUnix = 0;

            p.lastStreet4000WinUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            p.tearStreak8000 = Mathf.Max(p.tearStreak8000, 3);

            var sets = new List<string>(SetOrbService.StarterSets);
            foreach (var s in SetOrbService.UnlockLadder)
                if (!sets.Contains(s)) sets.Add(s);
            p.unlockedSetsCsv = string.Join(",", sets);
            p.setOrbCsv = "";
            p.EnsureValid();
        }

        static void MaxInventory(LocalAccountStore.Account acc)
        {
            var inv = acc.inventory;
            inv.hasBackpack = true;
            inv.hasDuelDisk = true;
            inv.labCarryAllStorage = true;
            LabCatalogService.KeepCatalogOnHand(inv);

            inv.backpack ??= new BackpackState();
            inv.backpack.unlocked = true;
            inv.backpack.capacityTier = 5;
            inv.backpack.ApplyTier();

            inv.deckBoxSlotCount = PlayerInventory.MaxPlayDeckBoxes;
            inv.unlockedThirdDeckCarry = true;
            inv.carryDeckBoxSlots = PlayerInventory.MaxCarryDeckBoxes;
            inv.EnsureDeckBoxSlots();
            if (inv.deckBoxes != null && inv.deckBoxes.Length > 0 && inv.deckBoxes[0] != null)
                inv.deckBoxes[0].occupied = true;

            inv.tradeTransportCharges = Mathf.Max(inv.tradeTransportCharges, AdminTradeCharges);
            inv.tome ??= new TomeState();
            inv.tome.hasTomeItem = true;

            if (inv.binders != null)
            {
                foreach (var b in inv.binders)
                {
                    if (b == null) continue;
                    b.pageCount = PlayerInventory.BinderMaxPages;
                    b.EnsureValid();
                }
            }

            inv.EnsureValid();
            LabCatalogService.KeepCatalogOnHand(inv);
        }

        static void MaxClothes(LocalAccountStore.Account acc)
        {
            acc.avatar ??= AvatarAppearance.Default();
            acc.inventory.wardrobe ??= new ClothingWardrobe();
            ClothingCatalog.GrantAll(acc.inventory.wardrobe);
            acc.avatar.shirtId = "shirt_duelcoat";
            acc.avatar.bottomsId = "bottoms_cargo";
            acc.avatar.EnsureClothingDefaults();
            ClothingService.EnsureOutfit(acc.avatar, acc.inventory);
            acc.inventory.carryDeckBoxSlots = PlayerInventory.MaxCarryDeckBoxes;
            acc.inventory.pockets ??= new AvatarPocketState();
            acc.inventory.pockets.pocketCount = PlayerInventory.MaxCarryDeckBoxes;
            acc.inventory.pockets.EnsureValid(acc.inventory.deckBoxes?.Length ?? 0,
                PlayerInventory.MaxCarryDeckBoxes);
        }

        static void MaxTome(LocalAccountStore.Account acc)
        {
            var tome = acc.inventory.tome ?? new TomeState();
            acc.inventory.tome = tome;
            tome.hasTomeItem = true;
            tome.pathTeam = acc.progress.kuribohTeam;
            var unlocked = new List<int>(tome.unlockedPageIds ?? Array.Empty<int>());
            foreach (var id in AllTomePages)
                if (!unlocked.Contains(id)) unlocked.Add(id);
            tome.unlockedPageIds = unlocked.ToArray();
            var cap = acc.progress.TomeCapacity();
            var equip = new List<int>();
            foreach (var id in unlocked)
            {
                if (equip.Count >= cap) break;
                equip.Add(id);
            }

            tome.equippedPageIds = equip.ToArray();
        }
    }
}
