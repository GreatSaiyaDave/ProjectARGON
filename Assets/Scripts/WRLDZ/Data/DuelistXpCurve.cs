using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// Pokémon GO–paced duelist XP curve.
    /// Early levels fly; mid game steadies; <b>level 50+ hard slowdown</b> (GO post-40/50 feel).
    /// Values are XP needed to advance from <paramref name="level"/> → level+1
    /// (not cumulative totals). Tuned so early wins level you; late game takes many duels.
    /// </summary>
    public static class DuelistXpCurve
    {
        /// <summary>Hard soft-cap until story complete (see <see cref="PlayerProgress.SoftLevelCap"/>).</summary>
        public const int MaxLevel = PlayerProgress.SoftLevelCap;

        /// <summary>Where the GO-style wall begins — XP per level jumps hard here.</summary>
        public const int SlowdownLevel = 50;

        /// <summary>
        /// XP required to go from <paramref name="level"/> to <paramref name="level"/>+1.
        /// Returns 0 if already at soft cap (and not using unlimited post-story).
        /// </summary>
        public static int XpToNextLevel(int level, bool storyModeComplete = false)
        {
            if (level < 1) level = 1;
            if (!storyModeComplete && level >= MaxLevel) return 0;
            if (storyModeComplete && level >= 200) return 0; // absolute ceiling for now

            // Piecewise GO-inspired (scaled for duel rewards ~200–1500 XP per match early)
            // ── 1–9: tutorial sprint (a few wins) ──
            if (level < 10)
                return 400 + level * 100; // 500 … 1300

            // ── 10–19: still fast ──
            if (level < 20)
                return 1500 + (level - 10) * 250; // 1500 … 3750

            // ── 20–29: casual mid ──
            if (level < 30)
                return 4000 + (level - 20) * 450; // 4000 … 8050

            // ── 30–39: committed ──
            if (level < 40)
                return 9000 + (level - 30) * 900; // 9000 … 17100

            // ── 40–49: approaching the wall ──
            if (level < SlowdownLevel)
                return 18000 + (level - 40) * 1800; // 18000 … 34200

            // ── 50–59: HARD SLOWDOWN (user request / GO L40–50 energy) ──
            if (level < 60)
                return 50000 + (level - 50) * 8000; // 50k … 122k

            // ── 60–79: long haul ──
            if (level < 80)
                return 130000 + (level - 60) * 12000; // 130k … 358k

            // ── 80–99: prestige grind ──
            return 400000 + (level - 80) * 25000; // 400k … 875k
        }

        /// <summary>0–1 fill of the current level bar.</summary>
        public static float Progress01(int level, int xpIntoLevel, bool storyModeComplete = false)
        {
            var need = XpToNextLevel(level, storyModeComplete);
            if (need <= 0) return 1f;
            return Mathf.Clamp01(xpIntoLevel / (float)need);
        }

        /// <summary>Approximate total XP from level 1 to reach <paramref name="targetLevel"/> (for tooltips).</summary>
        public static long TotalXpToReach(int targetLevel, bool storyModeComplete = false)
        {
            if (targetLevel <= 1) return 0;
            long sum = 0;
            var cap = storyModeComplete ? Mathf.Min(targetLevel, 200) : Mathf.Min(targetLevel, MaxLevel);
            for (var l = 1; l < cap; l++)
                sum += XpToNextLevel(l, storyModeComplete);
            return sum;
        }

        /// <summary>Human label for the current band (UI / Referobot).</summary>
        public static string BandName(int level)
        {
            if (level < 10) return "Rookie";
            if (level < 20) return "Duelist";
            if (level < 30) return "Challenger";
            if (level < 40) return "Veteran";
            if (level < SlowdownLevel) return "Elite";
            if (level < 60) return "Master"; // post-50 wall
            if (level < 80) return "Grandmaster";
            if (level < 100) return "Legend";
            return "Spirit Legend";
        }

        /// <summary>Digizeni granted on each level-up (GO-style level reward).</summary>
        public static int DigizeniOnLevelUp(int newLevel)
        {
            if (newLevel <= 1) return 0;
            // Mild early, stronger at milestones
            var basePay = 25 + newLevel * 5;
            if (newLevel % 10 == 0) basePay += 100; // every 10 levels
            if (newLevel == SlowdownLevel) basePay += 250; // break the wall ceremony
            if (newLevel == MaxLevel) basePay += 500;
            return basePay;
        }
    }
}
