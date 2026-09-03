using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Duelist level / XP (Pokémon GO pace — hard slowdown at L50).
    /// XP primarily from real duels; practice awards none.
    /// </summary>
    public static class ProgressionService
    {
        // Base XP by format (scaled to DuelistXpCurve)
        public const int PracticeDuelXp = 0;
        public const int LabTestXp = 40;          // Desktop Lab — enough to smoke-test leveling
        public const int QuickDuelWinXp = 650;
        public const int QuickDuelLossXp = 220;
        public const int TearZoneWinXp = 900;
        public const int TearZoneLossXp = 300;
        public const int StoryDuelWinXp = 1100;
        public const int StoryDuelLossXp = 350;
        public const int RaidDuelWinXp = 1800;
        public const int RaidDuelLossXp = 500;
        public const int PvpWinXp = 1200;
        public const int PvpLossXp = 500;

        public static PlayerProgress Get(LocalAccountStore.Account acc)
        {
            if (acc == null) return PlayerProgress.DefaultNew();
            acc.EnsureProgress();
            return acc.progress;
        }

        /// <summary>Add raw XP and process level-ups. Awards Digizeni on each level-up.</summary>
        public static bool AwardXp(LocalAccountStore.Account acc, int amount, out int levelsGained)
        {
            levelsGained = 0;
            if (acc == null || amount <= 0) return false;
            acc.EnsureProgress();
            var p = acc.progress;
            p.EnsureValid();

            if (!p.storyModeComplete && p.level >= PlayerProgress.SoftLevelCap)
            {
                Persist(acc);
                return false;
            }

            p.xp += amount;
            var digiBonus = 0;
            while (true)
            {
                var need = p.XpToNextLevel();
                if (need <= 0 || p.xp < need) break;
                if (!p.storyModeComplete && p.level >= PlayerProgress.SoftLevelCap) break;

                p.xp -= need;
                p.level++;
                levelsGained++;
                var pay = DuelistXpCurve.DigizeniOnLevelUp(p.level);
                digiBonus += pay;
                ArtifactService.Grant(acc, ArtifactService.Digizeni, pay);
                Debug.Log(
                    $"[WRLDZ] Level up → {p.level} ({DuelistXpCurve.BandName(p.level)}) · " +
                    $"+Đ{pay} · Tome cap {p.TomeCapacity()} · next needs {p.XpToNextLevel()} XP");
            }

            // Overflow safety if capped mid-award
            if (!p.storyModeComplete && p.level >= PlayerProgress.SoftLevelCap)
                p.xp = 0;

            acc.spiritRank = p.level;
            Persist(acc);
            if (digiBonus > 0)
                Debug.Log($"[WRLDZ] Level-up Digizeni total +{digiBonus}");
            return levelsGained > 0 || amount > 0;
        }

        /// <summary>
        /// Full post-duel reward: XP by match type + win/loss, duel coins, level-ups.
        /// Practice / pure training = no XP (canon).
        /// </summary>
        public static DuelRewardResult AwardDuelRewards(
            LocalAccountStore.Account acc,
            ArDuelMatchConfig match,
            bool playerWon,
            int turnNumber)
        {
            var result = new DuelRewardResult();
            if (acc == null)
            {
                result.SummaryLine = "No account — no rewards.";
                return result;
            }

            acc.EnsureProgress();
            var p = acc.progress;
            p.EnsureValid();
            result.LevelBefore = p.level;
            result.XpIntoLevel = p.xp;

            var xp = ComputeDuelXp(match, playerWon, turnNumber, out var practice);
            result.PracticeNoReward = practice;
            result.XpAwarded = xp;

            if (xp <= 0)
            {
                result.LevelAfter = p.level;
                result.XpToNext = p.XpToNextLevel();
                result.SoulLine = ApplySoulStakes(acc, match, playerWon);
                result.AccountDeactivated = acc.deactivated;
                result.SummaryLine = practice
                    ? "Practice duel — no XP (train freely)."
                    : "No XP awarded.";
                if (!string.IsNullOrEmpty(result.SoulLine))
                    result.SummaryLine += " · " + result.SoulLine;
                Persist(acc);
                return result;
            }

            AwardXp(acc, xp, out var levels);
            // re-fetch after award
            p = acc.progress;
            result.Applied = true;
            result.LevelsGained = levels;
            result.LevelAfter = p.level;
            result.XpIntoLevel = p.xp;
            result.XpToNext = p.XpToNextLevel();
            result.DigizeniGained = 0; // included inside AwardXp digizeni; sum for UI below
            if (levels > 0)
            {
                // Approximate digizeni from levels for display (already applied)
                for (var l = result.LevelBefore + 1; l <= result.LevelAfter; l++)
                    result.DigizeniGained += DuelistXpCurve.DigizeniOnLevelUp(l);
            }

            // Duel Coins — small always (even on loss)
            var coins = playerWon ? 8 + Mathf.Min(12, turnNumber) : 3;
            if (match != null && match.IsHumanOpponent) coins += 5;
            ArtifactService.Grant(acc, ArtifactService.DuelCoin, coins);
            result.DuelCoinGained = coins;
            RecordStreetAndTearStreak(p, match, playerWon);
            SetOrbService.TryDropFromDuel(acc, match, playerWon, out var orbToast);
            result.SoulLine = ApplySoulStakes(acc, match, playerWon);
            result.AccountDeactivated = acc.deactivated;
            Persist(acc);

            var band = DuelistXpCurve.BandName(p.level);
            if (levels > 0)
            {
                result.SummaryLine =
                    $"+{xp} XP · LEVEL UP! {result.LevelBefore} → {result.LevelAfter} ({band})" +
                    (result.DigizeniGained > 0 ? $" · +Đ{result.DigizeniGained}" : "") +
                    $" · +{coins} DC";
            }
            else
            {
                var need = result.XpToNext;
                var left = Mathf.Max(0, need - p.xp);
                result.SummaryLine =
                    $"+{xp} XP  ·  Lv{p.level} {p.xp}/{need}  ({left} to next)  ·  +{coins} DC";
            }

            if (!string.IsNullOrEmpty(orbToast))
                result.SummaryLine += " · " + orbToast;
            if (!string.IsNullOrEmpty(result.SoulLine))
                result.SummaryLine += " · " + result.SoulLine;

            Debug.Log($"[WRLDZ] Duel rewards: {result.SummaryLine}");
            return result;
        }

        public static int ComputeDuelXp(
            ArDuelMatchConfig match,
            bool playerWon,
            int turnNumber,
            out bool practiceNoReward)
        {
            practiceNoReward = false;
            if (match == null)
                return playerWon ? QuickDuelWinXp : QuickDuelLossXp;

            // Practice / Training Zone = free training, no XP (canon)
            if (match.Launch == ArDuelLaunchKind.Practice ||
                match.FormatId == "practice")
            {
                practiceNoReward = true;
                return PracticeDuelXp;
            }

            // Desktop Lab / TEST DUEL — tiny XP so curve can be smoked in Editor
            if (match.Launch == ArDuelLaunchKind.LabTest)
                return playerWon ? LabTestXp * 2 : LabTestXp;

            int win;
            int loss;
            switch (match.Launch)
            {
                case ArDuelLaunchKind.TearZone:
                case ArDuelLaunchKind.NpcStreet:
                    win = TearZoneWinXp;
                    loss = TearZoneLossXp;
                    break;
                case ArDuelLaunchKind.TearBoss:
                    win = RaidDuelWinXp;
                    loss = RaidDuelLossXp;
                    break;
                case ArDuelLaunchKind.RaidBoss:
                    win = RaidDuelWinXp;
                    loss = RaidDuelLossXp;
                    break;
                case ArDuelLaunchKind.StoryEra:
                    win = StoryDuelWinXp;
                    loss = StoryDuelLossXp;
                    break;
                case ArDuelLaunchKind.PvpZone:
                case ArDuelLaunchKind.NearbyChallenge:
                    win = PvpWinXp;
                    loss = PvpLossXp;
                    break;
                case ArDuelLaunchKind.Hub:
                    win = match.FormatId != null && match.FormatId.Contains("story")
                        ? StoryDuelWinXp
                        : QuickDuelWinXp;
                    loss = match.FormatId != null && match.FormatId.Contains("story")
                        ? StoryDuelLossXp
                        : QuickDuelLossXp;
                    break;
                default:
                    if (match.IsHumanOpponent)
                    {
                        win = PvpWinXp;
                        loss = PvpLossXp;
                    }
                    else
                    {
                        win = QuickDuelWinXp;
                        loss = QuickDuelLossXp;
                    }

                    break;
            }

            var xp = playerWon ? win : loss;
            // Slight turn-length bonus (longer duels = more XP, soft cap)
            xp += Mathf.Clamp(turnNumber * 8, 0, 120);
            return xp;
        }

        static void RecordStreetAndTearStreak(PlayerProgress p, ArDuelMatchConfig match, bool playerWon)
        {
            if (p == null || match == null) return;
            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (match.Launch == ArDuelLaunchKind.NpcStreet && match.StartingLp <= MapZoneCatalog.Street4000Lp)
            {
                if (playerWon) p.lastStreet4000WinUnix = now;
                return;
            }

            if (match.Launch != ArDuelLaunchKind.NpcStreet || match.StartingLp < MapZoneCatalog.Street8000Lp)
                return;

            var zone = match.ZoneId ?? "";
            if (!playerWon)
            {
                p.tearStreak8000 = 0;
                return;
            }

            if (string.Equals(p.tearStreakZoneId, zone, System.StringComparison.Ordinal))
                p.tearStreak8000++;
            else
            {
                p.tearStreakZoneId = zone;
                p.tearStreak8000 = 1;
            }
        }

        static string ApplySoulStakes(LocalAccountStore.Account acc, ArDuelMatchConfig match, bool playerWon)
        {
            if (acc == null || match == null) return "";
            var p = acc.progress;
            if (p == null) return "";
            if (playerWon && match.Launch == ArDuelLaunchKind.StoryEra)
                return SoulFractureService.TryUnlockFromStory(p, match.ZoneId);
            if (!playerWon && SoulFractureService.IsShadowGame(match))
                return SoulFractureService.ApplyLoss(acc);
            return "";
        }

        static bool _persisting;

        public static void Persist(LocalAccountStore.Account acc)
        {
            if (acc == null || _persisting) return;
            _persisting = true;
            try
            {
                LocalAccountStore.UpdateAccount(acc);
                var session = AppSession.Ensure();
                if (session.Account != acc)
                    session.SetAccount(acc);
            }
            finally
            {
                _persisting = false;
            }
        }
    }
}
