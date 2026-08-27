using System;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>
    /// Level, XP, currencies, onboarding flags, Kuriboh team.
    /// Soft level cap 100 until main story clear.
    /// XP curve is Pokémon GO–paced with a hard slowdown at L50 — see <see cref="DuelistXpCurve"/>.
    /// <see cref="xp"/> = progress toward the next level (not lifetime total).
    /// </summary>
    [Serializable]
    public class PlayerProgress
    {
        public const int SoftLevelCap = 100;
        /// <summary>Where grind intensifies (matches <see cref="DuelistXpCurve.SlowdownLevel"/>).</summary>
        public const int MvpSoftCap = DuelistXpCurve.SlowdownLevel;

        [Obsolete("Use DuelistXpCurve.XpToNextLevel — kept for older code references.")]
        public const int XpPerLevelBase = 400;

        public int level = 1;
        /// <summary>XP into the current level (0 … XpToNextLevel()-1).</summary>
        public int xp;
        public int digizeni = 500;
        public int duelCoin;
        public int setEnergy;

        /// <summary>KuribohTeam as int for JsonUtility.</summary>
        public int kuribohTeam;

        public bool onboardingPrologueDone;
        public bool onboardingKuribohChosen;
        public bool onboardingStarterGranted;
        public bool onboardingTutorialDuelDone;
        public bool onboardingComplete;
        public bool storyModeComplete;

        /// <summary>
        /// Shadow-game soul. Default 3 fractures (lives). Story episode clears
        /// raise capacity. Each Shadow Game loss adds one fracture.
        /// </summary>
        public const int DefaultSoulFractureCapacity = 3;
        public const int MaxSoulFractureCapacity = 8;
        public int soulFractureCapacity = DefaultSoulFractureCapacity;
        public int soulFractures;
        /// <summary>Unix seconds when the current 24h restore interval started. 0 = full (no regen).</summary>
        public long soulRegenUnix;
        /// <summary>CSV of Story Zone ids already used for a capacity unlock.</summary>
        public string soulStoryUnlockCsv = "";

        /// <summary>Unix seconds of last won 4000 LP street duel. 8000 LP NPCs need one in the last 24h.</summary>
        public long lastStreet4000WinUnix;
        /// <summary>Straight 8000 LP street wins around <see cref="tearStreakZoneId"/> (Tear boss needs 3).</summary>
        public int tearStreak8000;
        public string tearStreakZoneId = "";

        /// <summary>Unlocked constructed sets (CSV). Starter: LOB,MRD,SRL.</summary>
        public string unlockedSetsCsv = "LOB,MRD,SRL";
        /// <summary>Set-orb progress toward locked sets, e.g. PSV:3,LON:1.</summary>
        public string setOrbCsv = "";

        /// <summary>Owned ERAZ band badges (CSV). Empty until tutorial grants original.</summary>
        public string erazBadgesCsv = "";

        /// <summary>Lab account owns full catalog (3× each card) for deck editor testing.</summary>
        public bool labFullCatalogGranted;

        /// <summary>Lab admin: maxed currencies, unlocks, and travel catalog. Not a live player.</summary>
        public bool labAdmin;

        public KuribohTeam Team
        {
            get => (KuribohTeam)kuribohTeam;
            set => kuribohTeam = (int)value;
        }

        public static PlayerProgress DefaultNew()
        {
            return new PlayerProgress
            {
                level = 1,
                xp = 0,
                digizeni = 500,
                duelCoin = 0,
                setEnergy = 0,
                kuribohTeam = 0,
                unlockedSetsCsv = "LOB,MRD,SRL",
                setOrbCsv = "",
                erazBadgesCsv = "",
                soulFractureCapacity = DefaultSoulFractureCapacity,
                soulFractures = 0,
                soulRegenUnix = 0,
                soulStoryUnlockCsv = ""
            };
        }

        public void EnsureValid()
        {
            if (level < 1) level = 1;
            if (level > SoftLevelCap && !storyModeComplete) level = SoftLevelCap;
            if (xp < 0) xp = 0;
            // Clamp xp into current bar (handles curve changes / saves)
            var need = XpToNextLevel();
            if (need <= 0)
                xp = 0;
            else if (xp >= need)
                xp = need - 1;
            if (digizeni < 0) digizeni = 0;
            if (duelCoin < 0) duelCoin = 0;
            if (setEnergy < 0) setEnergy = 0;
            if (kuribohTeam < 0 || kuribohTeam > 3) kuribohTeam = 0;
            if (soulFractureCapacity < DefaultSoulFractureCapacity)
                soulFractureCapacity = DefaultSoulFractureCapacity;
            if (soulFractureCapacity > MaxSoulFractureCapacity)
                soulFractureCapacity = MaxSoulFractureCapacity;
            if (soulFractures < 0) soulFractures = 0;
            if (soulFractures > soulFractureCapacity)
                soulFractures = soulFractureCapacity;
            if (soulRegenUnix < 0) soulRegenUnix = 0;
            if (soulStoryUnlockCsv == null) soulStoryUnlockCsv = "";
            if (erazBadgesCsv == null) erazBadgesCsv = "";
        }

        public int SoulRemaining =>
            Mathf.Max(0, soulFractureCapacity - soulFractures);

        public bool SoulShattered => soulFractures >= soulFractureCapacity;

        /// <summary>XP needed for this level → next (GO curve).</summary>
        public int XpToNextLevel() =>
            DuelistXpCurve.XpToNextLevel(level, storyModeComplete);

        /// <summary>0–1 XP bar fill for UI.</summary>
        public float XpProgress01() =>
            DuelistXpCurve.Progress01(level, xp, storyModeComplete);

        public string BandName() => DuelistXpCurve.BandName(level);

        /// <summary>Tome page slots: +1 every 10 levels, max 10 at level 100.</summary>
        public int TomeCapacity()
        {
            if (level < 10) return 0;
            var slots = level / 10;
            return Mathf.Min(10, slots);
        }

        public const long StreetAccessWindowSec = 24 * 60 * 60;

        public bool HasStreet8000Access(long nowUnix)
        {
            if (lastStreet4000WinUnix <= 0) return false;
            return nowUnix - lastStreet4000WinUnix <= StreetAccessWindowSec;
        }

        public bool TearBossReady(string zoneId) =>
            tearStreak8000 >= 3
            && !string.IsNullOrEmpty(zoneId)
            && string.Equals(tearStreakZoneId, zoneId, StringComparison.Ordinal);
    }
}
