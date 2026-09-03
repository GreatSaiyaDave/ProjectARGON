using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Set orbs unlock constructed sets after the starter three.
    /// First three (LOB / MRD / SRL) are always open. Orbs drop from Tear
    /// harvests, street NPC wins, and PvP wins through duelist level 50.
    /// After L50 remaining sets follow story / era cadence instead.
    /// </summary>
    public static class SetOrbService
    {
        public const int OrbsPerUnlock = 8;
        public const int DropThroughLevel = 50;
        public const int TearHarvestSe = 3;
        public const long TearHarvestCooldownSec = 5 * 60;

        public static readonly string[] StarterSets = { "LOB", "MRD", "SRL" };

        /// <summary>Unlock ladder through L50 after the starter three.</summary>
        public static readonly string[] UnlockLadder =
        {
            "PSV", "LON", "LOD", "PGD", "MFC", "DCR",
            "IOC", "AST", "SOD", "RDS", "FET", "TLM"
        };

        public static readonly string[] StarterTitles =
        {
            "Legend of Blue Eyes", "Metal Raiders", "Spell Ruler"
        };

        public static void Ensure(PlayerProgress p)
        {
            if (p == null) return;
            var unlocked = ParseCsv(p.unlockedSetsCsv);
            var dirty = false;
            for (var i = 0; i < StarterSets.Length; i++)
            {
                if (unlocked.Contains(StarterSets[i])) continue;
                unlocked.Add(StarterSets[i]);
                dirty = true;
            }

            if (dirty)
                p.unlockedSetsCsv = Join(unlocked);
            if (p.setOrbCsv == null)
                p.setOrbCsv = "";
        }

        public static bool IsUnlocked(PlayerProgress p, string setId)
        {
            Ensure(p);
            if (string.IsNullOrEmpty(setId) || p == null) return false;
            var unlocked = ParseCsv(p.unlockedSetsCsv);
            return unlocked.Contains(setId.Trim().ToUpperInvariant());
        }

        public static string NextLockedSet(PlayerProgress p)
        {
            Ensure(p);
            var unlocked = ParseCsv(p?.unlockedSetsCsv);
            for (var i = 0; i < UnlockLadder.Length; i++)
                if (!unlocked.Contains(UnlockLadder[i]))
                    return UnlockLadder[i];
            return "";
        }

        public static int OrbCount(PlayerProgress p, string setId)
        {
            if (p == null || string.IsNullOrEmpty(setId)) return 0;
            var map = ParseCounts(p.setOrbCsv);
            return map.TryGetValue(setId.Trim().ToUpperInvariant(), out var n) ? n : 0;
        }

        public static bool CanDrop(PlayerProgress p)
        {
            if (p == null) return false;
            Ensure(p);
            if (p.level > DropThroughLevel) return false;
            return !string.IsNullOrEmpty(NextLockedSet(p));
        }

        /// <summary>Grant orbs toward the next locked set. Returns unlocked set id if a set opened.</summary>
        public static bool TryGrantOrbs(LocalAccountStore.Account acc, int amount, out string unlockedSet, out string toast)
        {
            unlockedSet = "";
            toast = "";
            if (acc == null || amount <= 0) return false;
            acc.EnsureProgress();
            var p = acc.progress;
            Ensure(p);
            if (p.level > DropThroughLevel)
            {
                toast = "Set orbs stop at L50 — later sets follow the story.";
                return false;
            }

            var set = NextLockedSet(p);
            if (string.IsNullOrEmpty(set))
            {
                toast = "All L50 sets unlocked.";
                return false;
            }

            var map = ParseCounts(p.setOrbCsv);
            map.TryGetValue(set, out var have);
            have += amount;
            string opened = null;
            while (have >= OrbsPerUnlock)
            {
                have -= OrbsPerUnlock;
                var unlocked = ParseCsv(p.unlockedSetsCsv);
                if (!unlocked.Contains(set))
                    unlocked.Add(set);
                p.unlockedSetsCsv = Join(unlocked);
                opened = set;
                set = NextLockedSet(p);
                if (string.IsNullOrEmpty(set))
                {
                    have = 0;
                    break;
                }

                map.TryGetValue(set, out have);
            }

            if (!string.IsNullOrEmpty(set) && have > 0)
                map[set] = have;
            else if (!string.IsNullOrEmpty(set))
                map[set] = have;
            p.setOrbCsv = JoinCounts(map);
            ProgressionService.Persist(acc);

            if (!string.IsNullOrEmpty(opened))
            {
                unlockedSet = opened;
                toast = $"SET UNLOCKED · {opened}";
            }
            else
                toast = $"+{amount} {set} orb  ({have}/{OrbsPerUnlock})";
            return true;
        }

        public static bool ShouldDropFor(ArDuelMatchConfig match, bool playerWon)
        {
            if (!playerWon || match == null) return false;
            switch (match.Launch)
            {
                case ArDuelLaunchKind.NpcStreet:
                case ArDuelLaunchKind.PvpZone:
                case ArDuelLaunchKind.NearbyChallenge:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryDropFromDuel(LocalAccountStore.Account acc, ArDuelMatchConfig match, bool playerWon,
            out string toast)
        {
            toast = "";
            if (!ShouldDropFor(match, playerWon)) return false;
            var amount = match.Launch == ArDuelLaunchKind.NpcStreet ? 2 : 1;
            return TryGrantOrbs(acc, amount, out _, out toast);
        }

        public static bool TearHarvestReady(string zoneId, long nowUnix)
        {
            var last = PlayerPrefs.GetInt(HarvestKey(zoneId), 0);
            return nowUnix - last >= TearHarvestCooldownSec;
        }

        public static long TearHarvestRemainSec(string zoneId, long nowUnix)
        {
            var last = PlayerPrefs.GetInt(HarvestKey(zoneId), 0);
            var left = TearHarvestCooldownSec - (nowUnix - last);
            return left > 0 ? left : 0;
        }

        /// <summary>PokéStop-style Tear spin: small SE + a set orb (through L50).</summary>
        public static bool TryHarvestTear(string zoneId, out int seGained, out string toast)
        {
            seGained = 0;
            toast = "";
            var acc = AppSession.Ensure()?.Account;
            if (acc == null)
            {
                toast = "No session";
                return false;
            }

            acc.EnsureProgress();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!TearHarvestReady(zoneId, now))
            {
                toast = $"Rift cooling · {TearHarvestRemainSec(zoneId, now)}s";
                return false;
            }

            seGained = TearHarvestSe;
            acc.EnsureInventory();
            ArtifactService.GrantUntaggedSetEnergy(acc.progress, acc.inventory, seGained);
            PlayerPrefs.SetInt(HarvestKey(zoneId), (int)now);
            PlayerPrefs.Save();

            var line = $"+{seGained} SE";
            if (TryGrantOrbs(acc, 1, out _, out var orbToast) && !string.IsNullOrEmpty(orbToast))
                line += " · " + orbToast;
            else
                ProgressionService.Persist(acc);
            toast = line;
            return true;
        }

        static string HarvestKey(string zoneId) =>
            "wrldz.tearHarvest." + (string.IsNullOrEmpty(zoneId) ? "tear" : zoneId);

        static List<string> ParseCsv(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim().ToUpperInvariant();
                if (s.Length == 0) continue;
                if (!list.Contains(s)) list.Add(s);
            }

            return list;
        }

        static string Join(List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder();
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(list[i]);
            }

            return sb.ToString();
        }

        static Dictionary<string, int> ParseCounts(string csv)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(csv)) return map;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                if (p.Length == 0) continue;
                var colon = p.IndexOf(':');
                if (colon <= 0) continue;
                var id = p.Substring(0, colon).Trim().ToUpperInvariant();
                if (!int.TryParse(p.Substring(colon + 1), out var n)) continue;
                map[id] = n;
            }

            return map;
        }

        static string JoinCounts(Dictionary<string, int> map)
        {
            if (map == null || map.Count == 0) return "";
            var sb = new StringBuilder();
            var first = true;
            foreach (var kv in map)
            {
                if (kv.Value <= 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(kv.Key);
                sb.Append(':');
                sb.Append(kv.Value);
            }

            return sb.ToString();
        }
    }
}
