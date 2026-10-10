using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// The boot sky, built from moving layers instead of one flat painting.
    ///
    /// A single bg_title.png can only ever Ken Burns: the pyramid, the sky and the Tear all
    /// drift as one plate, which reads as a photograph being panned. Here each band owns its
    /// own motion — the deep field creeps, two aurora ribbons cross at different rates, the
    /// Tear breathes and sways, and the pyramid parallaxes against all of it — so the frame is
    /// never still and never repeats exactly.
    ///
    /// Lore: the stone is sealed (gold seam up the pyramid's centre) and the sky is already
    /// rent (the Tear). Those two facts are the whole premise, so the opening frame states them.
    /// </summary>
    [ExecuteAlways]
    public sealed class LivingSky : MonoBehaviour
    {
        RectTransform _stars, _auroraA, _auroraB, _tear, _tearGlow, _pyramid, _seam, _haze;
        Image _starsImg, _auroraAImg, _auroraBImg, _tearImg, _tearGlowImg, _seamImg;
        static int _skyCount;
        float _seed;

        /// <summary>Build the sky under <paramref name="host"/>, or return the one already there.</summary>
        public static LivingSky Build(Transform host)
        {
            if (host == null) return null;
            var found = host.GetComponentInChildren<LivingSky>(true);
            if (found != null) return found;

            var go = new GameObject("LivingSky", typeof(RectTransform), typeof(LivingSky));
            go.transform.SetParent(host, false);
            var root = go.GetComponent<RectTransform>();
            Stretch(root);
            var sky = go.GetComponent<LivingSky>();
            sky.Compose(root);
            return sky;
        }

        void Compose(RectTransform root)
        {
            // Only needs to differ between instances, never to be random.
            _seed = (_skyCount++ % 17) * 0.37f;

            var deep = Plate(root, "Deep", null, EldritchPalette.Void);
            Stretch(deep);

            // Deep field, oversized so it can drift without showing an edge.
            _stars = Plate(root, "Stars", ImagineAssets.CinematicGenesisStars(),
                new Color(1f, 1f, 1f, 0.85f));
            _starsImg = _stars.GetComponent<Image>();

            // Two ribbons crossing at different rates never resolve into a loop the eye can read.
            _auroraA = Plate(root, "AuroraA", EldritchAssets.EmberSoft(),
                new Color(0.20f, 0.58f, 0.86f, 0.09f));
            _auroraAImg = _auroraA.GetComponent<Image>();
            _auroraB = Plate(root, "AuroraB", EldritchAssets.EmberSoft(),
                new Color(0.52f, 0.26f, 0.96f, 0.13f));
            _auroraBImg = _auroraB.GetComponent<Image>();

            _tearGlow = Plate(root, "TearGlow", EldritchAssets.EmberSoft(),
                new Color(0.55f, 0.24f, 1f, 0.22f));
            _tearGlowImg = _tearGlow.GetComponent<Image>();

            _tear = Plate(root, "Tear", ImagineAssets.SkyTear(), new Color(1f, 1f, 1f, 0.90f));
            _tearImg = _tear.GetComponent<Image>();
            _tearImg.preserveAspect = true;

            _pyramid = Plate(root, "Pyramid", ImagineAssets.SkyPyramid(), Color.white);
            _pyramid.GetComponent<Image>().preserveAspect = true;

            // The seal itself: gold light in the pyramid's seam, breathing like the barrier does.
            _seam = Plate(root, "Seam", EldritchAssets.EmberSoft(),
                new Color(1f, 0.80f, 0.34f, 0.30f));
            _seamImg = _seam.GetComponent<Image>();

            _haze = Plate(root, "Haze", EldritchAssets.EmberSoft(),
                new Color(1f, 0.58f, 0.26f, 0.13f));

            Pose(0f);
        }

        void Update() => Pose(Application.isPlaying ? Time.unscaledTime : 0f);

        void Pose(float t)
        {
            t += _seed;

            // Deep field: a slow creep plus a very slow swell, so it never parks.
            var ss = 1.10f + 0.045f * Mathf.Sin(t * 0.055f);
            var sx = 0.018f * Mathf.Sin(t * 0.031f);
            var sy = 0.012f * Mathf.Cos(t * 0.024f);
            Place(_stars, 0.5f - ss * 0.62f + sx, 0.5f - ss * 0.60f + sy,
                0.5f + ss * 0.62f + sx, 0.5f + ss * 0.60f + sy);
            SetA(_starsImg, 0.78f + 0.10f * Mathf.Sin(t * 0.47f));

            // Aurora ribbons, opposed and at different rates.
            var ax = 0.16f * Mathf.Sin(t * 0.070f);
            Place(_auroraA, -0.45f + ax, 0.50f, 0.92f + ax, 1.16f);
            SetA(_auroraAImg, 0.055f + 0.035f * Mathf.Sin(t * 0.29f));
            var bx = 0.20f * Mathf.Sin(t * 0.046f + 2.1f);
            Place(_auroraB, 0.10f + bx, 0.54f, 1.52f + bx, 1.22f);
            SetA(_auroraBImg, 0.075f + 0.045f * Mathf.Sin(t * 0.22f + 1.3f));

            // The Tear: breathes, sways, and never fully closes.
            var pulse = 0.5f + 0.5f * Mathf.Sin(t * 0.63f);
            var spasm = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(t * 0.21f)), 26f);
            var open = 0.055f + 0.020f * pulse + 0.045f * spasm;
            var tx = 0.60f + 0.020f * Mathf.Sin(t * 0.17f);
            var ty = 0.735f + 0.014f * Mathf.Sin(t * 0.13f + 0.8f);
            Place(_tear, tx - open, ty - open * 3.0f, tx + open, ty + open * 3.0f);
            SetA(_tearImg, 0.62f + 0.24f * pulse + 0.30f * spasm);
            var gg = open * 4.4f;
            Place(_tearGlow, tx - gg, ty - gg * 1.5f, tx + gg, ty + gg * 1.5f);
            SetA(_tearGlowImg, 0.10f + 0.11f * pulse + 0.22f * spasm);

            // Pyramid parallax: opposite the sky, and far smaller, so depth reads.
            var py = -0.012f * Mathf.Sin(t * 0.031f);
            var px = -0.006f * Mathf.Sin(t * 0.043f);
            Place(_pyramid, -0.06f + px, -0.075f + py, 1.06f + px, 0.72f + py);

            // Seal seam, breathing out of phase with the Tear — the barrier still holding.
            var seam = 0.5f + 0.5f * Mathf.Sin(t * 0.83f + 1.9f);
            Place(_seam, 0.44f + px, 0.02f + py, 0.56f + px, 0.50f + py);
            SetA(_seamImg, 0.16f + 0.16f * seam);

            Place(_haze, -0.08f, -0.14f, 1.08f, 0.26f);
        }

        // ── local rect helpers (FlowChrome lives in a sibling namespace) ──

        static RectTransform Plate(Transform parent, string name, Sprite sp, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp != null ? sp : UiFoundation.WhiteSprite();
            img.color = color;
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void SetA(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = a;
            g.color = c;
        }
    }
}
