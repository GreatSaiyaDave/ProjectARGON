using System;
using System.Collections.Generic;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Alternate table-law format badges. Collection only until that format's rules lock.
    /// Quick / PvAI / Practice / Shadow TCG never need one.
    /// </summary>
    public static class FormatProgress
    {
        public static readonly string[] Ids =
        {
            "dk", "raid", "ddm", "genesys", "speed", "deckmaster"
        };

        public static readonly string[] Titles =
        {
            "Duelist Kingdom", "Raid", "Dungeon Dice Monsters",
            "GENESYS", "Speed Duel", "Deck Master"
        };

        public static string Title(string id)
        {
            for (var i = 0; i < Ids.Length; i++)
                if (string.Equals(Ids[i], id, StringComparison.OrdinalIgnoreCase))
                    return Titles[i];
            return id;
        }

        public static bool HasBadge(PlayerProgress p, string formatId)
        {
            if (p == null || string.IsNullOrEmpty(formatId)) return false;
            var owned = Owned(p);
            for (var i = 0; i < owned.Length; i++)
                if (string.Equals(owned[i], formatId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static void GrantBadge(PlayerProgress p, string formatId)
        {
            if (p == null || string.IsNullOrEmpty(formatId)) return;
            if (HasBadge(p, formatId)) return;
            var list = ParseCsv(p.formatBadgesCsv);
            list.Add(formatId.Trim());
            p.formatBadgesCsv = string.Join(",", list);
        }

        public static void GrantBadge(LocalAccountStore.Account acc, string formatId)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            acc.EnsureInventory();
            GrantBadge(acc.progress, formatId);
            ArtifactService.Grant(acc.progress, acc.inventory, ArtifactService.FormatId(formatId), 1);
        }

        public static string[] Owned(PlayerProgress p)
        {
            if (p == null) return Array.Empty<string>();
            return ParseCsv(p.formatBadgesCsv).ToArray();
        }

        static List<string> ParseCsv(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(csv)) return list;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var s = parts[i].Trim();
                if (s.Length == 0) continue;
                var seen = false;
                for (var j = 0; j < list.Count; j++)
                    if (string.Equals(list[j], s, StringComparison.OrdinalIgnoreCase))
                        seen = true;
                if (!seen) list.Add(s);
            }

            return list;
        }
    }
}
