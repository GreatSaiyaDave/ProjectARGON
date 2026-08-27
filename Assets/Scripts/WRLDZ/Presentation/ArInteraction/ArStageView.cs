using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Stage RT camera + world-space UI facing.
    /// Card quads face the camera with +Z; uGUI canvases are the opposite
    /// (readable on −Z), so billboard UI with <see cref="UiFacing"/>.
    /// </summary>
    public static class ArStageView
    {
        static Camera _cached;
        static int _cachedFrame = -1;

        public static Camera FindCamera(Transform from = null)
        {
            if (_cached != null && _cachedFrame == Time.frameCount && _cached.isActiveAndEnabled)
                return _cached;

            Camera cam = null;
            if (from != null)
            {
                var t = from;
                for (var i = 0; i < 8 && t != null; i++, t = t.parent)
                {
                    cam = t.GetComponentInChildren<Camera>(true);
                    if (cam != null && cam.isActiveAndEnabled) break;
                    cam = null;
                }
            }

            if (cam == null)
            {
                var cams = Camera.allCameras;
                for (var i = 0; i < cams.Length; i++)
                {
                    var c = cams[i];
                    if (c == null || !c.isActiveAndEnabled) continue;
                    if (c.name == "ArCam" || c.targetTexture != null)
                    {
                        cam = c;
                        break;
                    }
                }

                if (cam == null)
                    cam = Camera.main;
            }

            _cached = cam;
            _cachedFrame = Time.frameCount;
            return cam;
        }

        /// <summary>World rotation so a World Space Canvas is readable from <paramref name="cam"/>.</summary>
        public static Quaternion UiFacing(Vector3 worldPos, Camera cam)
        {
            if (cam == null) return Quaternion.identity;
            var away = worldPos - cam.transform.position;
            if (away.sqrMagnitude < 1e-8f) return cam.transform.rotation;
            return Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
