using System;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Core
{
    /// <summary>
    /// Per-opponent difficulty overrides layered on top of the roster's declared baseline.
    /// Stored by duelist so the player can keep (or up/down-shift) a favourite's tier without
    /// rewriting the shipped roster file. Defaults fall back to <see cref="OpponentDef.difficulty"/>.
    /// </summary>
    public static class OpponentDifficultyPrefs
    {
        public const string KeyPrefix = "WRLDZ_OpponentDifficulty_";

        /// <summary>Effective difficulty for <paramref name="opp"/> (override, else roster baseline).</summary>
        public static AiDifficulty Get(OpponentDef opp)
        {
            if (opp == null) return AiDifficulty.Normal;
            var key = KeyPrefix + (string.IsNullOrEmpty(opp.id) ? opp.file : opp.id);
            if (!PlayerPrefs.HasKey(key)) return opp.difficulty;
            var stored = (AiDifficulty)PlayerPrefs.GetInt(key, (int)opp.difficulty);
            return stored;
        }

        /// <summary>Persist a specific tier for <paramref name="opp"/>.</summary>
        public static void Set(OpponentDef opp, AiDifficulty difficulty)
        {
            if (opp == null) return;
            var key = KeyPrefix + (string.IsNullOrEmpty(opp.id) ? opp.file : opp.id);
            PlayerPrefs.SetInt(key, (int)difficulty);
            PlayerPrefs.Save();
        }

        /// <summary>Cycle up one tier, persist, and return the new tier.</summary>
        public static AiDifficulty Cycle(OpponentDef opp)
        {
            var current = Get(opp);
            var next = (AiDifficulty)(((int)current + 1) % 4);
            Set(opp, next);
            return next;
        }

        public static bool HasOverride(OpponentDef opp)
        {
            if (opp == null || string.IsNullOrEmpty(opp.id) && string.IsNullOrEmpty(opp.file)) return false;
            var key = KeyPrefix + (string.IsNullOrEmpty(opp.id) ? opp.file : opp.id);
            return PlayerPrefs.HasKey(key);
        }

        public static void ClearOverride(OpponentDef opp)
        {
            if (opp == null) return;
            var key = KeyPrefix + (string.IsNullOrEmpty(opp.id) ? opp.file : opp.id);
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
