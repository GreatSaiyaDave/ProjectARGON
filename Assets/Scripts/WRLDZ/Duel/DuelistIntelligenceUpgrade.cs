namespace WRLDZ.Duel
{
    /// <summary>
    /// Duelist Intelligence Upgrade system.
    /// Enhances AI duelist intelligence with adaptive learning and variance controls.
    /// </summary>
    public class DuelistIntelligenceUpgrade
    {
        private bool enableUpgrade = true;
        private float baseIntelligence = 1.0f;
        private float varianceFactor = 0.2f;

        public static DuelistIntelligenceUpgrade Instance { get; private set; } = new DuelistIntelligenceUpgrade();

        public bool EnableUpgrade => enableUpgrade;
        public float BaseIntelligence => baseIntelligence;
        public float VarianceFactor => varianceFactor;
        public float GetEffectiveIntelligence() => baseIntelligence;
        public float GetEffectiveVariance() => varianceFactor;
    }
}
