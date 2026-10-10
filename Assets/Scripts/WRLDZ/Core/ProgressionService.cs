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
        // Flat win table (Master Duel shape). Loss pays LossShare of the same row.
        public const float LossShare = 0.1f;
        public const int PracticeDuelXp = 0;
        public const int LabTestXp = 40;          // Desktop Lab — enough to smoke-test leveling
        public const int QuickDuelWinXp = 650;
        public const int TearZoneWinXp = 900;
        public const int StoryDuelWinXp = 1100;
        public const int RaidDuelWinXp = 1800;
        public const int PvpWinXp = 1200;
        public const int QuickDuelWinSe = 4;
        public const int TearZoneWinSe = 4;
        public const int StoryDuelWinSe = 4;
        public const int RaidDuelWinSe = 12;
        public const int QuickDuelWinDc = 10;
        public const int TearZoneWinDc = 12;
        public const int StoryDuelWinDc = 12;
        public const int RaidDuelWinDc = 16;
        public const int PvpWinDc = 15;

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
        /// Practice / pure training = no XP (canon). Same-device hotseat pays nothing (unverifiable result).
        /// Idempotent per <see cref="ArDuelMatchConfig.DuelResultId"/>: a finished duel pays exactly once, so a
        /// second call (game-over overlay + forfeit on leave, a double tap, a replayed callback) is a no-op
        /// with <see cref="DuelRewardResult.AlreadyClaimed"/> set.
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

            // Claim first: the claim mark and the rewards land in the same account write (Persist below).
            var duelId = match?.DuelResultId;
            // The running-duel marker (crash forfeit) goes in the same write as the claim.
            PendingDuelMarker.ClearIfMatches(p, duelId);
            if (!string.IsNullOrEmpty(duelId) && !p.TryMarkDuelClaimed(duelId))
            {
                result.AlreadyClaimed = true;
                result.LevelAfter = p.level;
                result.XpToNext = p.XpToNextLevel();
                result.SummaryLine = "Rewards for this duel were already claimed.";
                Debug.Log($"[WRLDZ] Duel {duelId} already claimed — no second payout.");
                return result;
            }

            if (match != null && match.IsSameDeviceHotseat)
            {
                result.PracticeNoReward = true;
                result.LevelAfter = p.level;
                result.XpToNext = p.XpToNextLevel();
                result.SummaryLine = "Hotseat duel — friendly match on one device, no rewards.";
                Persist(acc);
                return result;
            }

            var winXp = ComputeDuelXp(match, playerWon, turnNumber, out var practice);
            var xp = ScaleForOutcome(winXp, playerWon, minOneIfPositive: true);
            if (xp > 0)
            {
                xp = ApplyMasteryPlateau(xp, p.level, IsPvpXp(match));
                xp = ApplyRoleXp(xp, p.level, p.Team, match, playerWon);
            }

            result.PracticeNoReward = practice;
            result.XpAwarded = xp;

            if (!practice)
                ShellLoops.NoteDuel(p, match, playerWon);

            if (practice || xp <= 0 && winXp <= 0)
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

            var levels = 0;
            if (xp > 0)
                AwardXp(acc, xp, out levels);
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

            var coins = ScaleForOutcome(WinDuelCoins(match), playerWon, minOneIfPositive: true);
            if (coins > 0)
                ArtifactService.Grant(acc, ArtifactService.DuelCoin, coins);
            result.DuelCoinGained = coins;

            var se = ScaleForOutcome(WinSetEnergy(match), playerWon, minOneIfPositive: false);
            se = ApplyKuribohSetEnergy(se, p.Team, match != null ? match.Launch : ArDuelLaunchKind.Hub);
            if (se > 0)
            {
                acc.EnsureInventory();
                ArtifactService.GrantUntaggedSetEnergy(acc.progress, acc.inventory, se);
                result.SetEnergyGained += se;
            }

            RecordStreetAndTearStreak(p, match, playerWon);
            SetOrbService.TryDropFromDuel(acc, match, playerWon, out var orbToast);
            if (playerWon)
                ApplyStoryFirstClear(acc, match, result);
            QuestService.CreditDuel(p, match, playerWon);
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
            if (result.SetEnergyGained > 0)
                result.SummaryLine += " · +" + result.SetEnergyGained + " SE";
            if (!string.IsNullOrEmpty(result.StoryLootLine))
                result.SummaryLine += " · " + result.StoryLootLine;
            if (!string.IsNullOrEmpty(result.SoulLine))
                result.SummaryLine += " · " + result.SoulLine;

            Debug.Log($"[WRLDZ] Duel rewards: {result.SummaryLine}");
            return result;
        }

        /// <summary>
        /// True when leaving <paramref name="match"/> now would be a forfeit: a rewarded (non-practice, non-lab,
        /// non-hotseat) duel that has started (<see cref="ArDuelMatchConfig.DuelResultId"/> set) and whose
        /// result has not been claimed yet.
        /// </summary>
        public static bool IsForfeitable(LocalAccountStore.Account acc, ArDuelMatchConfig match)
        {
            if (acc == null || match == null || string.IsNullOrEmpty(match.DuelResultId)) return false;
            if (match.IsPracticeOrLab || match.IsSameDeviceHotseat) return false;
            acc.EnsureProgress();
            return !acc.progress.HasClaimedDuel(match.DuelResultId);
        }

        /// <summary>
        /// Owner rule 2026-09-23 #3: leaving a rewarded duel before it ends (Map / Restart / Leave) is a loss —
        /// loss-share rewards, streak reset, Shadow Game soul stakes. Claims the duel id, so the duel cannot
        /// pay again afterwards. Callers confirm with the player first (UI).
        /// </summary>
        public static DuelRewardResult ForfeitDuel(LocalAccountStore.Account acc, ArDuelMatchConfig match, int turnNumber)
        {
            var r = AwardDuelRewards(acc, match, playerWon: false, turnNumber);
            if (!r.AlreadyClaimed)
            {
                r.Forfeit = true;
                r.SummaryLine = "Forfeit — counted as a loss · " + r.SummaryLine;
            }

            return r;
        }

        /// <summary>Win-table XP for this match. Caller applies <see cref="ScaleForOutcome"/>.</summary>
        public static int ComputeDuelXp(
            ArDuelMatchConfig match,
            bool playerWon,
            int turnNumber,
            out bool practiceNoReward)
        {
            practiceNoReward = false;
            _ = playerWon;
            _ = turnNumber;
            if (match == null)
                return QuickDuelWinXp;

            if (match.Launch == ArDuelLaunchKind.Practice ||
                match.FormatId == "practice")
            {
                practiceNoReward = true;
                return PracticeDuelXp;
            }

            if (match.Launch == ArDuelLaunchKind.LabTest)
                return LabTestXp * 2;

            switch (match.Launch)
            {
                case ArDuelLaunchKind.TearZone:
                case ArDuelLaunchKind.NpcStreet:
                    return TearZoneWinXp;
                case ArDuelLaunchKind.TearBoss:
                case ArDuelLaunchKind.RaidBoss:
                    return RaidDuelWinXp;
                case ArDuelLaunchKind.StoryEra:
                    return StoryDuelWinXp;
                case ArDuelLaunchKind.PvpZone:
                case ArDuelLaunchKind.NearbyChallenge:
                    return PvpWinXp;
                case ArDuelLaunchKind.Tournament:
                    // AI-filled seat = CPU duel; only a human opponent earns the PvP row.
                    return match.IsHumanOpponent ? PvpWinXp : QuickDuelWinXp;
                case ArDuelLaunchKind.Hub:
                    return match.FormatId != null && match.FormatId.Contains("story")
                        ? StoryDuelWinXp
                        : QuickDuelWinXp;
                default:
                    return match.IsHumanOpponent ? PvpWinXp : QuickDuelWinXp;
            }
        }

        /// <summary>
        /// Win pays <paramref name="winAmount"/>. Loss pays 10% (floor).
        /// XP / DC use min 1 when the win amount is positive.
        /// </summary>
        public static int ScaleForOutcome(int winAmount, bool playerWon, bool minOneIfPositive)
        {
            if (winAmount <= 0) return 0;
            if (playerWon) return winAmount;
            var n = Mathf.FloorToInt(winAmount * LossShare);
            if (minOneIfPositive && n < 1) n = 1;
            return n;
        }

        public static int WinSetEnergy(ArDuelMatchConfig match)
        {
            if (match == null) return QuickDuelWinSe;
            if (match.Launch == ArDuelLaunchKind.Practice || match.FormatId == "practice")
                return 0;
            if (match.Launch == ArDuelLaunchKind.LabTest)
                return 0;
            if (IsPvpXp(match))
                return 0;
            switch (match.Launch)
            {
                case ArDuelLaunchKind.TearBoss:
                case ArDuelLaunchKind.RaidBoss:
                    return RaidDuelWinSe;
                case ArDuelLaunchKind.StoryEra:
                    return StoryDuelWinSe;
                case ArDuelLaunchKind.TearZone:
                case ArDuelLaunchKind.NpcStreet:
                    return TearZoneWinSe;
                case ArDuelLaunchKind.Hub:
                    return match.FormatId != null && match.FormatId.Contains("story")
                        ? StoryDuelWinSe
                        : QuickDuelWinSe;
                default:
                    return QuickDuelWinSe;
            }
        }

        public static int WinDuelCoins(ArDuelMatchConfig match)
        {
            if (match == null) return QuickDuelWinDc;
            if (match.Launch == ArDuelLaunchKind.Practice || match.FormatId == "practice")
                return 0;
            if (match.Launch == ArDuelLaunchKind.LabTest)
                return 0;
            if (IsPvpXp(match))
                return PvpWinDc;
            switch (match.Launch)
            {
                case ArDuelLaunchKind.TearBoss:
                case ArDuelLaunchKind.RaidBoss:
                    return RaidDuelWinDc;
                case ArDuelLaunchKind.StoryEra:
                    return StoryDuelWinDc;
                case ArDuelLaunchKind.TearZone:
                case ArDuelLaunchKind.NpcStreet:
                    return TearZoneWinDc;
                case ArDuelLaunchKind.Hub:
                    return match.FormatId != null && match.FormatId.Contains("story")
                        ? StoryDuelWinDc
                        : QuickDuelWinDc;
                default:
                    return QuickDuelWinDc;
            }
        }

        static void RecordStreetAndTearStreak(PlayerProgress p, ArDuelMatchConfig match, bool playerWon)
        {
            if (p == null || match == null) return;
            var now = ClockGuard.NowUnix(p);
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

        /// <summary>Level 5 Tear/street wins, and Kuribandit's story-duel XP.</summary>
        public static int ApplyRoleXp(int xp, int level, KuribohTeam team, ArDuelMatchConfig match, bool playerWon)
        {
            if (xp <= 0 || match == null) return xp;
            if (team == KuribohTeam.Kuribandit && match.Launch == ArDuelLaunchKind.StoryEra)
                xp = (xp * 115 + 99) / 100;
            if (playerWon && level >= 5 &&
                (match.Launch == ArDuelLaunchKind.TearZone || match.Launch == ArDuelLaunchKind.NpcStreet))
                xp = (xp * 110 + 99) / 100;
            return xp;
        }

        /// <summary>Junkuriboh turns Tear and Tear-boss Set Energy up by 10%.</summary>
        public static int ApplyKuribohSetEnergy(int se, KuribohTeam team, ArDuelLaunchKind launch)
        {
            if (se <= 0 || team != KuribohTeam.Junkuriboh) return se;
            if (launch != ArDuelLaunchKind.TearZone && launch != ArDuelLaunchKind.TearBoss)
                return se;
            return (se * 110 + 99) / 100;
        }

        /// <summary>
        /// After Duelist 50: PVE XP −90%, PvP XP +100%. Magic plateaus; ranked is the climb.
        /// </summary>
        public static int ApplyMasteryPlateau(int xp, int level, bool pvp)
        {
            if (xp <= 0 || level < PlayerProgress.MvpSoftCap) return xp;
            return pvp ? xp * 2 : Mathf.Max(1, xp / 10);
        }

        /// <summary>
        /// PvP rates apply to a human opponent (and the PvP-only launches). A tournament seat filled by the AI
        /// is a CPU duel: it pays CPU rates, never PvP XP / Duel-Coin (QC core track 2026-09-23).
        /// </summary>
        public static bool IsPvpXp(ArDuelMatchConfig match)
        {
            if (match == null) return false;
            if (match.IsHumanOpponent) return true;
            return match.Launch == ArDuelLaunchKind.PvpZone
                   || match.Launch == ArDuelLaunchKind.NearbyChallenge;
        }

        static void ApplyStoryFirstClear(
            LocalAccountStore.Account acc, ArDuelMatchConfig match, DuelRewardResult result)
        {
            if (acc == null || match == null) return;
            if (match.Launch != ArDuelLaunchKind.StoryEra) return;
            acc.EnsureInventory();
            var p = acc.progress;
            var inv = acc.inventory;
            var stageId = match.StoryStageId;
            if (string.IsNullOrEmpty(stageId))
                stageId = StoryCampaignService.Current(p)?.id;
            var stage = StoryCampaignService.Stage(stageId);
            if (stage == null || !StoryCampaignService.TryComplete(p, stage.id))
                return;
            result.StoryStageCleared = stage.id;
            result.StoryLootLine = StoryCampaignService.GrantFirstClearRewards(
                p, inv, stage, out var se);
            result.SetEnergyGained += se;
        }

        static string ApplySoulStakes(LocalAccountStore.Account acc, ArDuelMatchConfig match, bool playerWon)
        {
            if (acc == null || match == null) return "";
            var p = acc.progress;
            if (p == null) return "";
            if (playerWon && match.Launch == ArDuelLaunchKind.StoryEra)
                return "";
            if (!playerWon && SoulFractureService.IsShadowGame(match))
                return SoulFractureService.ApplyLoss(acc);
            return "";
        }

        static bool _persisting;

        public static void Persist(LocalAccountStore.Account acc)
        {
            if (acc == null || _persisting) return;
            if (string.IsNullOrEmpty(acc.username) ||
                acc.username.StartsWith("__test", System.StringComparison.Ordinal))
                return;
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
