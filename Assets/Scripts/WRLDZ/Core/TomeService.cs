using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Raid-only Tome Deck: real TCG S/T card IDs as spell-book pages.
    /// Capacity = level/10 (max 10 at L100). Separate from Main/Extra/Side.
    /// </summary>
    public static class TomeService
    {
        // Early path seeds (real Konami ids — classic S/T flavor per team)
        static readonly int[] PathGalacti = { 53129443, 12580477, 44095762 }; // Dark Hole, Raigeki, Mirror Force
        static readonly int[] PathBandit = { 19613556, 66788016, 40605147 };  // Heavy Storm, Fissure, Solemn
        static readonly int[] PathJunk = { 83764718, 55144522, 32807846 };    // Monster Reborn, Pot of Greed, Reinforcement

        public static bool IsRaidOnly => true;

        /// <summary>Tome pages are legal in Raid Zone and Tear Zone boss duels.</summary>
        public static bool LegalFor(ArDuelMatchConfig match) =>
            match != null && match.TomeLegal;

        public static int Capacity(LocalAccountStore.Account acc)
        {
            acc?.EnsureProgress();
            return acc?.progress?.TomeCapacity() ?? 0;
        }

        public static void SyncCapacityUnlocks(LocalAccountStore.Account acc)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            acc.EnsureInventory();
            var p = acc.progress;
            var tome = acc.inventory.tome;
            tome.hasTomeItem = true;
            tome.pathTeam = p.kuribohTeam;

            var cap = p.TomeCapacity();
            var unlocked = new List<int>(tome.unlockedPageIds ?? System.Array.Empty<int>());

            // Seed first 3 path cards as level gates: 10, 20, 30
            var path = PathFor(p.Team);
            for (var i = 0; i < path.Length; i++)
            {
                var needLevel = (i + 1) * 10;
                if (p.level >= needLevel && !unlocked.Contains(path[i]))
                    unlocked.Add(path[i]);
            }

            // Cap does not auto-add random pages beyond path; capacity limits equip size
            tome.unlockedPageIds = unlocked.ToArray();
            ClampEquip(tome, cap);
            ProgressionService.Persist(acc);
        }

        public static bool TryEquipPage(LocalAccountStore.Account acc, int cardId)
        {
            if (acc == null) return false;
            acc.EnsureInventory();
            acc.EnsureProgress();
            var tome = acc.inventory.tome;
            var cap = Capacity(acc);
            if (cap <= 0) return false;

            var unlocked = new List<int>(tome.unlockedPageIds ?? System.Array.Empty<int>());
            if (!unlocked.Contains(cardId)) return false;

            var equip = new List<int>(tome.equippedPageIds ?? System.Array.Empty<int>());
            if (equip.Contains(cardId)) return true;
            if (equip.Count >= cap) return false;
            equip.Add(cardId);
            tome.equippedPageIds = equip.ToArray();
            ProgressionService.Persist(acc);
            return true;
        }

        public static bool TryUnequipPage(LocalAccountStore.Account acc, int cardId)
        {
            if (acc == null) return false;
            acc.EnsureInventory();
            var tome = acc.inventory.tome;
            var equip = new List<int>(tome.equippedPageIds ?? System.Array.Empty<int>());
            if (!equip.Remove(cardId)) return false;
            tome.equippedPageIds = equip.ToArray();
            ProgressionService.Persist(acc);
            return true;
        }

        public static IReadOnlyList<int> EquippedForRaid(LocalAccountStore.Account acc)
        {
            if (acc == null) return System.Array.Empty<int>();
            acc.EnsureInventory();
            SyncCapacityUnlocks(acc);
            return acc.inventory.tome.equippedPageIds ?? System.Array.Empty<int>();
        }

        static int[] PathFor(KuribohTeam t) => t switch
        {
            KuribohTeam.Galactikuriboh => PathGalacti,
            KuribohTeam.Kuribandit => PathBandit,
            KuribohTeam.Junkuriboh => PathJunk,
            _ => PathJunk
        };

        static void ClampEquip(TomeState tome, int cap)
        {
            var equip = new List<int>();
            var unlocked = new HashSet<int>(tome.unlockedPageIds ?? System.Array.Empty<int>());
            foreach (var id in tome.equippedPageIds ?? System.Array.Empty<int>())
            {
                if (!unlocked.Contains(id)) continue;
                if (equip.Count >= cap) break;
                if (!equip.Contains(id)) equip.Add(id);
            }

            // Auto-equip path order until cap if empty
            if (equip.Count == 0 && cap > 0)
            {
                foreach (var id in tome.unlockedPageIds ?? System.Array.Empty<int>())
                {
                    if (equip.Count >= cap) break;
                    equip.Add(id);
                }
            }

            tome.equippedPageIds = equip.ToArray();
        }
    }
}
