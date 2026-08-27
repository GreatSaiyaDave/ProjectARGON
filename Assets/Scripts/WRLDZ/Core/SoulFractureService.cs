using System;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Shadow Game soul stakes. Lore: Umbrax's prison is the Shadow Realm;
    /// the Lemurian contract-barrier is the ancestor of Shadow Games. A loss
    /// with souls on the line fractures the duelist's soul. Default 3
    /// fractures; story episode clears raise the number they can endure.
    /// </summary>
    public static class SoulFractureService
    {
        public const long RestoreIntervalSec = 24L * 60L * 60L;

        public const string DeactivatedLoginMessage =
            "This Spirit Dueler is deactivated. Every soul fracture was spent in a Shadow Game — Umbrax holds the last fragment. One soul restores every 24 hours; then you may log in again.";

        static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static bool IsShadowGame(ArDuelMatchConfig match)
        {
            if (match == null) return false;
            if (match.Launch == ArDuelLaunchKind.Practice ||
                match.Launch == ArDuelLaunchKind.LabTest)
                return false;

            switch (match.Launch)
            {
                case ArDuelLaunchKind.StoryEra:
                case ArDuelLaunchKind.TearBoss:
                case ArDuelLaunchKind.RaidBoss:
                    return true;
                case ArDuelLaunchKind.Hub:
                    var id = match.FormatId ?? "";
                    var title = match.FormatTitle ?? "";
                    return id.IndexOf("story", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           id.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           title.IndexOf("Umbrax", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           title.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    var fid = match.FormatId ?? "";
                    return fid.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fid.IndexOf("umbrax", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public static string ApplyLoss(PlayerProgress p)
        {
            if (p == null) return "";
            p.EnsureValid();
            TickRegen(p);
            var wasFull = p.soulFractures <= 0;
            if (p.soulFractures < p.soulFractureCapacity)
                p.soulFractures++;
            if (wasFull || p.soulRegenUnix <= 0)
                p.soulRegenUnix = NowUnix();
            if (p.SoulShattered)
                return "Soul shattered — this Spirit Dueler is deactivated. Umbrax holds the last fragment.";
            return $"Soul fractured  {p.SoulRemaining}/{p.soulFractureCapacity} remain.";
        }

        public static string ApplyLoss(LocalAccountStore.Account acc)
        {
            if (acc == null) return "";
            acc.EnsureProgress();
            var line = ApplyLoss(acc.progress);
            SyncDeactivation(acc);
            return line;
        }

        /// <summary>
        /// Tick 24h restore, then deactivate if no souls remain, or restore
        /// the account if a soul has returned.
        /// Returns true when the account is (still) deactivated.
        /// </summary>
        public static bool SyncDeactivation(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            acc.EnsureProgress();
            TickRegen(acc.progress);
            return ApplyDeactivationFlags(acc.progress,
                ref acc.deactivated, ref acc.deactivatedUtc, ref acc.deactivatedReason);
        }

        public static bool SyncDeactivation(PlayerAccountDatabase.Account acc)
        {
            if (acc == null) return false;
            acc.EnsureProgress();
            TickRegen(acc.progress);
            return ApplyDeactivationFlags(acc.progress,
                ref acc.deactivated, ref acc.deactivatedUtc, ref acc.deactivatedReason);
        }

        static bool ApplyDeactivationFlags(PlayerProgress p,
            ref bool deactivated, ref string deactivatedUtc, ref string deactivatedReason)
        {
            if (p != null && p.SoulShattered)
            {
                if (!deactivated)
                {
                    deactivated = true;
                    deactivatedUtc = DateTime.UtcNow.ToString("o");
                    deactivatedReason = "Soul shattered in a Shadow Game.";
                    Debug.Log("[WRLDZ] Spirit Dueler deactivated — all soul fractures spent.");
                }

                return true;
            }

            if (deactivated)
            {
                deactivated = false;
                deactivatedUtc = "";
                deactivatedReason = "";
                Debug.Log("[WRLDZ] Spirit Dueler restored — a soul fragment returned.");
            }

            return false;
        }

        /// <summary>
        /// Restore one missing soul per 24h. Never exceeds owned capacity.
        /// Catches up for offline time (48h away = two souls, still capped).
        /// </summary>
        public static bool TickRegen(PlayerProgress p)
        {
            if (p == null) return false;
            p.EnsureValid();
            var now = NowUnix();

            if (p.soulFractures <= 0)
            {
                p.soulFractures = 0;
                if (p.soulRegenUnix != 0)
                {
                    p.soulRegenUnix = 0;
                    return true;
                }

                return false;
            }

            if (p.soulRegenUnix <= 0)
            {
                p.soulRegenUnix = now;
                return true;
            }

            if (p.soulRegenUnix > now)
            {
                p.soulRegenUnix = now;
                return true;
            }

            var cycles = (now - p.soulRegenUnix) / RestoreIntervalSec;
            if (cycles <= 0) return false;

            var grant = (int)Math.Min(cycles, p.soulFractures);
            if (grant <= 0) return false;
            p.soulFractures -= grant;
            if (p.soulFractures <= 0)
            {
                p.soulFractures = 0;
                p.soulRegenUnix = 0;
            }
            else
                p.soulRegenUnix += grant * RestoreIntervalSec;

            return true;
        }

        public static bool TickAndPersist(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            acc.EnsureProgress();
            if (!TickRegen(acc.progress)) return false;
            ProgressionService.Persist(acc);
            return true;
        }

        public static string TryUnlockFromStory(PlayerProgress p, string episodeId)
        {
            if (p == null) return "";
            p.EnsureValid();
            episodeId = (episodeId ?? "").Trim();
            if (episodeId.Length == 0) episodeId = "story";
            var csv = p.soulStoryUnlockCsv ?? "";
            if (ContainsId(csv, episodeId))
                return "";
            if (p.soulFractureCapacity >= PlayerProgress.MaxSoulFractureCapacity)
                return "";
            p.soulFractureCapacity++;
            p.soulStoryUnlockCsv = string.IsNullOrEmpty(csv) ? episodeId : csv + "," + episodeId;
            return $"Soul tempered  capacity {p.soulFractureCapacity}.";
        }

        static bool ContainsId(string csv, string id)
        {
            if (string.IsNullOrEmpty(csv)) return false;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
                if (string.Equals(parts[i].Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
