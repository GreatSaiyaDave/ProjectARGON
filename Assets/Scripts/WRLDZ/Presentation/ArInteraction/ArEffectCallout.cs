using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Retired: arena effect-text glass boxes (name + desc beside holos).
    /// Ensure() strips leftovers. ATK/DEF gauges and LP callouts stay elsewhere.
    /// </summary>
    [DefaultExecutionOrder(800)]
    public class ArEffectCallout : MonoBehaviour
    {
        const float CanvasWorld = 0.00085f;
        const int CanvasW = 280;
        public const float BoxWorldWidth = CanvasW * CanvasWorld;

        public static ArEffectCallout Ensure(ArArenaCardVisual host)
        {
            if (host == null) return null;
            foreach (var existing in host.GetComponentsInChildren<ArEffectCallout>(true))
                ArObjectUtil.Destroy(existing.gameObject);
            return null;
        }

        void OnEnable()
        {
            ArObjectUtil.Destroy(gameObject);
        }
    }
}
