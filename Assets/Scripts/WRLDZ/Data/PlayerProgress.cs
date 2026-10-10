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

        /// <summary>Owned ERAZ band badges (CSV). Empty until fortune teller grants original.</summary>
        public string erazBadgesCsv = "";
        /// <summary>Owned format badges (CSV): dk, raid, ddm, genesys, speed, deckmaster.</summary>
        public string formatBadgesCsv = "";

        /// <summary>Cleared Story stage ids (CSV).</summary>
        public string storyClearedCsv = "";
        /// <summary>Next playable Story stage id. Empty when Season 1 is fully cleared.</summary>
        public string storyCurrentId = "";
        /// <summary>Referobot daily JSON (day + three slots).</summary>
        public string questStateJson = "";

        /// <summary>Non-practice wins toward a streak purse. Clears on a loss.</summary>
        public int pveWinStreak;
        /// <summary>Streak purses waiting. The real pack is not minted yet.</summary>
        public int streakPurses;
        /// <summary>Highest level-quiz gate already answered (10, 20, …).</summary>
        public int quizClaimedThrough;
        /// <summary>Story wins with the chosen Kuriboh. Caps at 10. No extra reward.</summary>
        public int kuribohBond;

        /// <summary>
        /// Spirit companion shown on the map and in AR duels (<see cref="SpiritCompanions"/> id). Empty = the Kuriboh of
        /// <see cref="kuribohTeam"/>. Players keep their team's companion; the lab admin may pick any of them.
        /// </summary>
        public string companionId = "";

        /// <summary>Lab account owns full catalog (3× each card) for deck editor testing.</summary>
        public bool labFullCatalogGranted;

        /// <summary>Lab admin: maxed currencies, unlocks, and travel catalog. Not a live player.</summary>
        public bool labAdmin;

        /// <summary>
        /// Most recent duel result ids whose rewards were paid (newest last, capped at
        /// <see cref="ClaimedDuelIdCap"/>). A finished duel pays exactly once.
        /// </summary>
        public string[] claimedDuelIds = Array.Empty<string>();
        public const int ClaimedDuelIdCap = 32;

        /// <summary>
        /// Latest UTC unix second this account has seen. Timers and daily/weekly resets never run on a
        /// clock earlier than this (device clock rolled back). See WRLDZ.Core.ClockGuard.
        /// </summary>
        public long clockHighWaterUnix;

        /// <summary>Per-account zone loot locks, "zoneId@yyyy-MM-dd" CSV, pruned to the last 7 days.</summary>
        public string zoneLootCsv = "";

        /// <summary>Per-account Tear harvest stamps, "zoneId@unix" CSV, pruned once the cooldown has passed.</summary>
        public string tearHarvestCsv = "";

        /// <summary>Walk Set Energy daily cap, per account on the guarded clock (see WRLDZ.Core.WalkEnergyLedger).</summary>
        public string walkSeDayUtc = "";
        public int walkSeToday;

        /// <summary>
        /// Rewarded duel in progress (owner rule 2026-09-23 #3): its result id and a JSON copy of its match
        /// config, written when the duel starts and cleared when the result is claimed. A marker still here on
        /// the next launch means the app was killed mid-duel — it is settled as a forfeit loss
        /// (see WRLDZ.Core.PendingDuelMarker).
        /// </summary>
        public string pendingDuelId = "";
        public string pendingDuelMatchJson = "";
        public long pendingDuelStartedUnix;

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
                formatBadgesCsv = "",
                storyClearedCsv = "",
                storyCurrentId = "",
                questStateJson = "",
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
            companionId ??= "";
            if (companionId.Length > 0 && SpiritCompanions.Find(companionId) == null) companionId = "";
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
            if (formatBadgesCsv == null) formatBadgesCsv = "";
            if (storyClearedCsv == null) storyClearedCsv = "";
            if (storyCurrentId == null) storyCurrentId = "";
            if (questStateJson == null) questStateJson = "";
            if (pveWinStreak < 0) pveWinStreak = 0;
            if (pveWinStreak > 3) pveWinStreak = 3;
            if (streakPurses < 0) streakPurses = 0;
            if (quizClaimedThrough < 0) quizClaimedThrough = 0;
            if (kuribohBond < 0) kuribohBond = 0;
            if (kuribohBond > 10) kuribohBond = 10;
            claimedDuelIds ??= Array.Empty<string>();
            if (clockHighWaterUnix < 0) clockHighWaterUnix = 0;
            zoneLootCsv ??= "";
            tearHarvestCsv ??= "";
            walkSeDayUtc ??= "";
            if (walkSeToday < 0) walkSeToday = 0;
            pendingDuelId ??= "";
            pendingDuelMatchJson ??= "";
        }

        public bool HasClaimedDuel(string duelResultId)
        {
            if (string.IsNullOrEmpty(duelResultId) || claimedDuelIds == null) return false;
            for (var i = 0; i < claimedDuelIds.Length; i++)
                if (string.Equals(claimedDuelIds[i], duelResultId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Remember <paramref name="duelResultId"/> as paid. False when it was already claimed.</summary>
        public bool TryMarkDuelClaimed(string duelResultId)
        {
            if (string.IsNullOrEmpty(duelResultId) || HasClaimedDuel(duelResultId)) return false;
            var old = claimedDuelIds ?? Array.Empty<string>();
            var keep = Mathf.Min(old.Length, ClaimedDuelIdCap - 1);
            var next = new string[keep + 1];
            Array.Copy(old, old.Length - keep, next, 0, keep);
            next[keep] = duelResultId;
            claimedDuelIds = next;
            return true;
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
            // A clock set before the win must not read as "inside the window" forever (QC F7).
            return nowUnix >= lastStreet4000WinUnix && nowUnix - lastStreet4000WinUnix <= StreetAccessWindowSec;
        }

        public bool TearBossReady(string zoneId) =>
            tearStreak8000 >= 3
            && !string.IsNullOrEmpty(zoneId)
            && string.Equals(tearStreakZoneId, zoneId, StringComparison.Ordinal);
    }
}
