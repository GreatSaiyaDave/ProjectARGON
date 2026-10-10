using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Four-tier AI difficulty for constructed PvAI.
    /// </summary>
    public enum AiDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Expert = 3
    }

    public static class AiDifficultyPrefs
    {
        private const string Key = "WRLDZ_AI_Difficulty";

        public static AiDifficulty Current
        {
            get => (AiDifficulty)PlayerPrefs.GetInt(Key, (int)AiDifficulty.Normal);
            set => PlayerPrefs.SetInt(Key, (int)value);
        }

        public static AiDifficulty Cycle(AiDifficulty current)
        {
            var next = (AiDifficulty)(((int)current + 1) % 4);
            Current = next;
            return next;
        }
    }
}
