using System;
using System.Collections.Generic;
using WRLDZ.Core;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// ERAZ badge ownership on <see cref="PlayerProgress.erazBadgesCsv"/>.
    /// Fortune teller grants Original whole. Later bands are 5 shards + SE at the Bazaar.
    /// </summary>
    public static class ErazProgress
    {
        public const string CsvField = "erazBadgesCsv";

        public static bool HasBadge(PlayerProgress p, string eraId)
        {
            if (p == null || string.IsNullOrEmpty(eraId)) return false;
            var owned = Owned(p);
            for (var i = 0; i < owned.Length; i++)
            {
                if (string.Equals(owned[i], eraId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void GrantBadge(PlayerProgress p, string eraId)
        {
            if (p == null || string.IsNullOrEmpty(eraId)) return;
            if (HasBadge(p, eraId)) return;

            var list = ParseCsv(p.erazBadgesCsv);
            list.Add(eraId.Trim());
            p.erazBadgesCsv = Join(list);
        }

        public static void GrantTutorialBadge(PlayerProgress p)
        {
            GrantBadge(p, ErazFormat.Original);
        }

        public static void GrantBadge(LocalAccountStore.Account acc, string eraId)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            GrantBadge(acc.progress, eraId);
            acc.EnsureInventory();
            ArtifactService.Grant(acc.progress, acc.inventory, ArtifactService.ErazId(eraId), 1);
        }

        public static void GrantTutorialBadge(LocalAccountStore.Account acc)
        {
            GrantBadge(acc, ErazFormat.Original);
        }

        /// <summary>
        /// Existing accounts that finished tutorial without <c>erazBadgesCsv</c>
        /// still receive Original.
        /// </summary>
        public static void GrantTutorialBadgeIfOnboarded(PlayerProgress p)
        {
            if (p == null) return;
            if (p.onboardingTutorialDuelDone || p.onboardingComplete)
                GrantTutorialBadge(p);
        }

        /// <summary>
        /// Highest whole badge in band order. Original if none (pre-tutorial).
        /// Street NPCs may only use this ERAZ.
        /// </summary>
        public static string HighestOwned(PlayerProgress p)
        {
            var order = ErazFormat.BandIdsInOrder();
            string highest = null;
            for (var i = 0; i < order.Count; i++)
            {
                if (HasBadge(p, order[i]))
                    highest = order[i];
            }

            return string.IsNullOrEmpty(highest) ? ErazFormat.Original : highest;
        }

        /// <summary>
        /// Season clear grants a shard of the next band, not the whole badge.
        /// Merge at the Bazaar (5 shards + Set Energy).
        /// </summary>
        public static string NextPieceBand(PlayerProgress p)
        {
            if (p == null) return ErazFormat.Gx;
            var highest = HighestOwned(p);
            if (!HasBadge(p, ErazFormat.Original))
                return ErazFormat.Original;
            return ErazFormat.NextBand(highest) ?? "";
        }

        public static void GrantNextOnSeasonComplete(PlayerProgress p)
        {
            // Kept for tests: season complete no longer grants a whole badge.
            // Shards are artifact cards — use GrantNextSeasonPiece.
        }

        public static void GrantNextSeasonPiece(PlayerProgress p, PlayerInventory inv)
        {
            if (p == null || inv == null) return;
            var next = NextPieceBand(p);
            if (string.IsNullOrEmpty(next)) return;
            if (string.Equals(next, ErazFormat.Original, StringComparison.OrdinalIgnoreCase)
                && !HasBadge(p, ErazFormat.Original))
            {
                GrantTutorialBadge(p);
                ArtifactService.Grant(p, inv, ArtifactService.ErazId(ErazFormat.Original), 1);
                return;
            }

            ArtifactService.Grant(p, inv, ArtifactService.ErazPieceId(next), 1);
        }

        public static bool CanSelectBand(PlayerProgress p, string eraId) =>
            HasBadge(p, eraId);

        public static string[] Owned(PlayerProgress p)
        {
            if (p == null) return Array.Empty<string>();
            return ParseCsv(p.erazBadgesCsv).ToArray();
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
                if (!ContainsIgnoreCase(list, s))
                    list.Add(s);
            }
            return list;
        }

        static string Join(List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            return string.Join(",", list);
        }

        static bool ContainsIgnoreCase(List<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
