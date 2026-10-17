using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Duelist Intelligence Upgrade system.
    /// Enhances AI duelist intelligence with adaptive learning and variance controls.
    /// </summary>
    public class DuelistIntelligenceUpgrade : MonoBehaviour
    {
        [UnityEngine.SerializeField] private bool enableUpgrade = true;
        [UnityEngine.SerializeField] private float baseIntelligence = 1.0f;
        [UnityEngine.SerializeField] private float varianceFactor = 0.2f;

        public static DuelistIntelligenceUpgrade Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public float GetEffectiveIntelligence()
        {
            if (!enableUpgrade) return baseIntelligence;
            return baseIntelligence * AiIntelligencePrefs.Value * DuelistIntelligence.IntelligenceMultiplier;
        }

        public float GetEffectiveVariance()
        {
            if (!enableUpgrade) return varianceFactor;
            return AiVariancePrefs.Enabled ? AiVariancePrefs.Value : varianceFactor;
        }
    }
}
