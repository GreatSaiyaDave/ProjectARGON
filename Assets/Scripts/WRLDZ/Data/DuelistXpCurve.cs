using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// Pokémon GO–paced duelist XP curve.
    /// Levels 1–50 match GO 1–20 energy; <b>50+ is the mastery plateau</b> (curve + PVE −90% / PvP +100%).
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

            // Duelist 1–50 matches Pokémon GO 1–20 energy (~200k total).
            // ── 1–10: tutorial sprint (a few wins) ──
            if (level < 10)
                return 400 + level * 80; // 480 … 1120

            // ── 10–20: still GO-early ──
            if (level < 20)
                return 1400 + (level - 10) * 120; // 1400 … 2480

            // ── 20–35: casual mid ──
            if (level < 35)
                return 2600 + (level - 20) * 180; // 2600 … 5120

            // ── 35–50: approaching mastery ──
            if (level < SlowdownLevel)
                return 5500 + (level - 35) * 250; // 5500 … 9000

            // ── 50–59: plateau wall (PVE XP also −90% in ProgressionService) ──
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
