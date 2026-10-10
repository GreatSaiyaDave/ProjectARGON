using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    [Serializable]
    public class QuestStateFile
    {
        public string dayUtc;
        public QuestSlot[] slots;
        public int stamps;
        public string stampDayUtc;
        public string weekUtc;
        public int weeklyProgress;
        public int weeklyGoal = 3;
        public bool weeklyClaimed;
    }

    [Serializable]
    public class QuestSlot
    {
        public string id;
        public string title;
        public int progress;
        public int goal;
        public bool claimed;
    }

    /// <summary>
    /// Referobot research. Three daily tasks, one weekly win task, and a
    /// seven-stamp breakthrough. State is JSON on <see cref="PlayerProgress.questStateJson"/>.
    /// </summary>
    public static class QuestService
    {
        public const string WinStreetOrTear = "win_street_or_tear";
        public const string HarvestTear = "harvest_tear";
        public const string PlayStory = "play_story";
        public const string WinAny = "win_any";
        public const string WeeklyWins = "weekly_wins";
        public const string Breakthrough = "breakthrough";

        public const int ClaimDigizeni = 40;
        public const int ClaimSe = 15;
        public const int WeeklyDigizeni = 120;
        public const int BreakthroughDigizeni = 200;
        public const int BreakthroughSe = 40;
        /// <summary>
        /// Premium currency on every challenge claim (owner's planned features, 2026-03-22: "Daily and weekly
        /// challenges award premium currency and rare cards"). Scaled to the duel-win Duel Coin table
        /// (ProgressionService.QuickDuelWinDc 10 … RaidDuelWinDc 16): a daily is half a street win, the weekly two
        /// wins, the seven-stamp breakthrough three. Tune here; nothing else hard-codes them.
        /// </summary>
        public const int ClaimDuelCoin = 5;
        public const int WeeklyDuelCoin = 20;
        public const int BreakthroughDuelCoin = 30;
        public const int StampsToBreak = 7;
        public const int WeeklyGoal = 3;

        /// <summary>Today's state on the account's guarded clock (<see cref="ClockGuard"/>).</summary>
        public static QuestStateFile Ensure(PlayerProgress p) => Ensure(p, ClockGuard.UtcNow(p));

        /// <summary>
        /// State for <paramref name="utc"/>. Days and weeks only move forward: a date earlier than the stored
        /// one (device clock rolled back) keeps the stored day, so slots are not regenerated (QC F7).
        /// </summary>
        public static QuestStateFile Ensure(PlayerProgress p, DateTime utc)
        {
            var today = ClockGuard.DayKey(utc);
            var week = ClockGuard.DayKey(MondayUtc(utc));
            if (p == null) return Fresh(today, week);
            var state = Parse(p.questStateJson) ?? new QuestStateFile();
            if (!string.IsNullOrEmpty(state.dayUtc) && string.CompareOrdinal(today, state.dayUtc) < 0)
                today = state.dayUtc;
            if (!string.IsNullOrEmpty(state.weekUtc) && string.CompareOrdinal(week, state.weekUtc) < 0)
                week = state.weekUtc;
            var changed = false;
            if (state.slots == null || state.slots.Length != 3 || state.dayUtc != today)
            {
                var stamps = state.stamps;
                var stampDay = state.stampDayUtc;
                var sameWeek = state.weekUtc == week;
                state.dayUtc = today;
                state.slots = PickThree(today);
                state.stamps = stamps;
                state.stampDayUtc = stampDay;
                state.weekUtc = week;
                state.weeklyGoal = WeeklyGoal;
                if (!sameWeek)
                {
                    state.weeklyProgress = 0;
                    state.weeklyClaimed = false;
                }

                changed = true;
            }
            else if (state.weekUtc != week)
            {
                state.weekUtc = week;
                state.weeklyGoal = WeeklyGoal;
                state.weeklyProgress = 0;
                state.weeklyClaimed = false;
                changed = true;
            }

            if (changed) Save(p, state);
            return state;
        }

        public static void CreditDuel(PlayerProgress p, ArDuelMatchConfig match, bool playerWon) =>
            CreditDuel(p, match, playerWon, ClockGuard.UtcNow(p));

        public static void CreditDuel(PlayerProgress p, ArDuelMatchConfig match, bool playerWon, DateTime utc)
        {
            if (p == null || match == null) return;
            if (IsPractice(match)) return;
            var state = Ensure(p, utc);
            if (playerWon)
                BumpWeekly(state);
            if (playerWon && (match.Launch == ArDuelLaunchKind.TearZone
                || match.Launch == ArDuelLaunchKind.NpcStreet
                || match.Launch == ArDuelLaunchKind.TearBoss))
                Bump(state, WinStreetOrTear);
            if (match.Launch == ArDuelLaunchKind.StoryEra)
                Bump(state, PlayStory);
            if (playerWon)
                Bump(state, WinAny);
            Save(p, state);
        }

        public static void CreditHarvest(PlayerProgress p)
        {
            if (p == null) return;
            var state = Ensure(p);
            Bump(state, HarvestTear);
            Save(p, state);
        }

        public static bool TryClaim(PlayerProgress p, PlayerInventory inv, string questId, out string error) =>
            TryClaim(p, inv, questId, ClockGuard.UtcNow(p), out error);

        public static bool TryClaim(PlayerProgress p, PlayerInventory inv, string questId, DateTime utc, out string error)
        {
            error = null;
            if (p == null || inv == null)
            {
                error = "No account.";
                return false;
            }

            var state = Ensure(p, utc);
            // Ensure never lets the day go backwards, so this is max(requested day, stored day).
            var today = state.dayUtc;
            if (string.Equals(questId, Breakthrough, StringComparison.OrdinalIgnoreCase))
            {
                if (state.stamps < StampsToBreak)
                {
                    error = "Not finished.";
                    return false;
                }

                state.stamps = 0;
                ArtifactService.Grant(p, inv, ArtifactService.Digizeni, BreakthroughDigizeni);
                ArtifactService.Grant(p, inv, ArtifactService.DuelCoin, BreakthroughDuelCoin);
                if (ArtifactService.CanEarnSetEnergy(p, "LOB"))
                    ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), BreakthroughSe);
                Save(p, state);
                return true;
            }

            if (string.Equals(questId, WeeklyWins, StringComparison.OrdinalIgnoreCase))
            {
                if (state.weeklyClaimed)
                {
                    error = "Already claimed.";
                    return false;
                }

                if (state.weeklyProgress < WeeklyGoal)
                {
                    error = "Not finished.";
                    return false;
                }

                state.weeklyClaimed = true;
                ArtifactService.Grant(p, inv, ArtifactService.Digizeni, WeeklyDigizeni);
                ArtifactService.Grant(p, inv, ArtifactService.DuelCoin, WeeklyDuelCoin);
                Save(p, state);
                return true;
            }

            var slot = Find(state, questId);
            if (slot == null)
            {
                error = "Unknown task.";
                return false;
            }

            if (slot.claimed)
            {
                error = "Already claimed.";
                return false;
            }

            if (slot.progress < slot.goal)
            {
                error = "Not finished.";
                return false;
            }

            slot.claimed = true;
            ArtifactService.Grant(p, inv, ArtifactService.Digizeni, ClaimDigizeni);
            ArtifactService.Grant(p, inv, ArtifactService.DuelCoin, ClaimDuelCoin);
            if (ArtifactService.CanEarnSetEnergy(p, "LOB"))
                ArtifactService.Grant(p, inv, ArtifactService.SetEnergyId("LOB"), ClaimSe);
            // One stamp per new day, and only for a day later than the last stamp (no stamp on a rolled-back date).
            if (string.CompareOrdinal(today, state.stampDayUtc ?? "") > 0 && state.stamps < StampsToBreak)
            {
                state.stamps++;
                state.stampDayUtc = today;
            }

            Save(p, state);
            return true;
        }

        /// <summary>What a claim pays, in the HUD's own currency marks (Đ Digizeni, DC Duel Coin, SE Set Energy).</summary>
        public static string RewardLine(string questId)
        {
            if (string.Equals(questId, Breakthrough, StringComparison.OrdinalIgnoreCase))
                return "+Đ" + BreakthroughDigizeni + "  ·  +DC" + BreakthroughDuelCoin + "  ·  +SE" + BreakthroughSe;
            if (string.Equals(questId, WeeklyWins, StringComparison.OrdinalIgnoreCase))
                return "+Đ" + WeeklyDigizeni + "  ·  +DC" + WeeklyDuelCoin;
            return "+Đ" + ClaimDigizeni + "  ·  +DC" + ClaimDuelCoin + "  ·  +SE" + ClaimSe;
        }

        public static string NextPerkLine(int level)
        {
            if (level < 5) return "Level 5 · +10% XP from Tear and street wins";
            if (level < 10) return "Level 10 · Tome opens";
            if (level < 20) return "Level 20 · Mascot mark";
            if (level < 40) return "Level 40 · Disk flair mark";
            return "Level 40 disk flair is yours";
        }

        public static bool DailySetContains(string dayUtc, string id)
        {
            var slots = PickThree(dayUtc);
            for (var i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].id == id) return true;
            return false;
        }

        public static DateTime MondayUtc(DateTime utc)
        {
            var date = utc.ToUniversalTime().Date;
            var delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return date.AddDays(-delta);
        }

        /// <summary>Practice, lab and same-device hotseat duels never advance Referobot research.</summary>
        static bool IsPractice(ArDuelMatchConfig match) =>
            match.Launch == ArDuelLaunchKind.Practice
            || match.Launch == ArDuelLaunchKind.LabTest
            || match.FormatId == "practice"
            || match.IsSameDeviceHotseat;

        static void BumpWeekly(QuestStateFile state)
        {
            if (state == null || state.weeklyClaimed) return;
            if (state.weeklyProgress < WeeklyGoal)
                state.weeklyProgress++;
        }

        static void Bump(QuestStateFile state, string id)
        {
            var slot = Find(state, id);
            if (slot == null || slot.claimed) return;
            if (slot.progress < slot.goal)
                slot.progress++;
        }

        static QuestSlot Find(QuestStateFile state, string id)
        {
            if (state?.slots == null || string.IsNullOrEmpty(id)) return null;
            for (var i = 0; i < state.slots.Length; i++)
            {
                var s = state.slots[i];
                if (s != null && string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

        static QuestStateFile Fresh(string dayUtc, string weekUtc) => new()
        {
            dayUtc = dayUtc,
            slots = PickThree(dayUtc),
            weekUtc = weekUtc,
            weeklyGoal = WeeklyGoal
        };

        static QuestSlot[] PickThree(string dayUtc)
        {
            var pool = new[]
            {
                Slot(WinStreetOrTear, "Win a Tear or street duel", 1),
                Slot(HarvestTear, "Harvest a Tear", 1),
                Slot(PlayStory, "Play a Story stage", 1),
                Slot(WinAny, "Win any duel", 1)
            };
            var bag = new List<int> { 0, 1, 2, 3 };
            var h = 17;
            if (!string.IsNullOrEmpty(dayUtc))
            {
                for (var i = 0; i < dayUtc.Length; i++)
                    h = unchecked(h * 31 + dayUtc[i]);
            }

            var picked = new QuestSlot[3];
            for (var i = 0; i < 3; i++)
            {
                h = unchecked(h * 1103515245 + 12345);
                var n = (int)((uint)h % (uint)bag.Count);
                picked[i] = pool[bag[n]];
                bag.RemoveAt(n);
            }

            return picked;
        }

        static QuestSlot Slot(string id, string title, int goal) => new()
        {
            id = id,
            title = title,
            goal = goal
        };

        static QuestStateFile Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<QuestStateFile>(json);
            }
            catch
            {
                return null;
            }
        }

        static void Save(PlayerProgress p, QuestStateFile state)
        {
            if (p == null) return;
            p.questStateJson = state == null ? "" : JsonUtility.ToJson(state);
        }
    }
}
