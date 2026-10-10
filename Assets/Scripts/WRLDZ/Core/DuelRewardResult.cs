namespace WRLDZ.Core
{
    /// <summary>Outcome of post-duel progression (XP, currencies, level-ups).</summary>
    public struct DuelRewardResult
    {
        public bool Applied;
        public int XpAwarded;
        public int LevelsGained;
        public int LevelBefore;
        public int LevelAfter;
        public int DigizeniGained;
        public int DuelCoinGained;
        public int XpIntoLevel;
        public int XpToNext;
        public string SummaryLine;
        public bool PracticeNoReward;
        public string SoulLine;
        public bool AccountDeactivated;
        public int SetEnergyGained;
        public string StoryStageCleared;
        public string StoryLootLine;
        /// <summary>This duel's result was already paid; nothing changed.</summary>
        public bool AlreadyClaimed;
        /// <summary>Recorded as a forfeit loss (left before the duel ended).</summary>
        public bool Forfeit;
    }
}
