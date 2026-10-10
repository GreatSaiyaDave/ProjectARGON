using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Procedural anti-aliased shapes for the world-space disk / arena HUD (phase pills, LP plates,
    /// opponent board). Generated once at runtime so the holo chrome stays crisp at any size
    /// without shipping more PNGs. Sliced sprites take their corner radius from
    /// <see cref="Apply"/> (pixelsPerUnitMultiplier), so one texture serves every chip size.
    /// </summary>
    public static class ArHoloSprites
    {
        const int PillPx = 128;
        const int PillRadiusPx = 60;
        const int RoundPx = 96;
        const int RoundRadiusPx = 24;

        static Sprite _pill, _pillRim, _round, _roundRim, _disc, _ring, _glow;

        /// <summary>Full capsule fill (sliced).</summary>
        public static Sprite Pill() => _pill ??= Rounded("HoloPill", PillPx, PillRadiusPx, 0f);

        /// <summary>Capsule outline (sliced).</summary>
        public static Sprite PillRim() => _pillRim ??= Rounded("HoloPillRim", PillPx, PillRadiusPx, 6f);

        /// <summary>Rounded-rect fill (sliced).</summary>
        public static Sprite RoundRect() => _round ??= Rounded("HoloRound", RoundPx, RoundRadiusPx, 0f);

        /// <summary>Rounded-rect outline (sliced).</summary>
        public static Sprite RoundRectRim() => _roundRim ??= Rounded("HoloRoundRim", RoundPx, RoundRadiusPx, 4f);

        /// <summary>Filled circle (simple).</summary>
        public static Sprite Disc() => _disc ??= Radial("HoloDisc", 128, r => Edge(0.5f - (r - 63f)));

        /// <summary>Thin circle outline (simple).</summary>
        public static Sprite Ring() =>
            _ring ??= Radial("HoloRing", 128, r => Edge(0.5f - (r - 63f)) * Edge(0.5f + (r - 57f)));

        /// <summary>Soft radial glow (simple).</summary>
        public static Sprite Glow() => _glow ??= Radial("HoloGlow", 64, r =>
        {
            var t = Mathf.Clamp01(1f - r / 32f);
            return t * t;
        });

        /// <summary>
        /// Put a shape on an Image. <paramref name="radius"/> is the corner radius in the Image's own
        /// canvas units for sliced shapes (capsules use half the rect height).
        /// </summary>
        public static Image Apply(Image img, Sprite sprite, Color color, float radius = 0f)
        {
            if (img == null) return null;
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sprite != null && sprite.border.x > 0f)
            {
                img.type = Image.Type.Sliced;
                img.fillCenter = true;
                var borderPx = sprite.border.x;
                img.pixelsPerUnitMultiplier = radius > 0.01f ? borderPx / radius : 1f;
            }
            else
            {
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
            }

            return img;
        }

        /// <summary>Child Image stretched over <paramref name="parent"/> with an optional outset (canvas units).</summary>
        public static Image Layer(Transform parent, string name, Sprite sprite, Color color, float radius = 0f,
            float outset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-outset, -outset);
            rt.offsetMax = new Vector2(outset, outset);
            return Apply(go.GetComponent<Image>(), sprite, color, radius);
        }

        static float Edge(float v) => Mathf.Clamp01(v);

        static Sprite Rounded(string name, int size, int radius, float stroke)
        {
            var tex = NewTex(name, size);
            var px = new Color32[size * size];
            var half = size * 0.5f;
            var inner = half - radius;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - inner, 0f);
                var qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - inner, 0f);
                var d = Mathf.Sqrt(qx * qx + qy * qy) - radius + 0.5f;
                var a = Edge(0.5f - d);
                if (stroke > 0f)
                    a *= Edge(0.5f + d + stroke);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            var b = radius;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        static Sprite Radial(string name, int size, System.Func<float, float> alphaAtRadius)
        {
            var tex = NewTex(name, size);
            var px = new Color32[size * size];
            var half = size * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5f - half;
                var dy = y + 0.5f - half;
                var a = Mathf.Clamp01(alphaAtRadius(Mathf.Sqrt(dx * dx + dy * dy)));
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
        }

        static Texture2D NewTex(string name, int size) =>
            new(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave
            };
    }
}
