using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Edit-mode safe Object helpers for EditorSim / headless AR smoke
    /// (<see cref="Application.isPlaying"/> false still builds the full graph).
    /// </summary>
    public static class ArObjectUtil
    {
        /// <summary>
        /// <see cref="Object.Destroy"/> in play mode; <see cref="Object.DestroyImmediate"/> in edit mode.
        /// </summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>Destroy delayed only works in play mode; edit mode destroys immediately.</summary>
        public static void Destroy(Object obj, float t)
        {
            if (obj == null) return;
            if (Application.isPlaying)
                Object.Destroy(obj, t);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
