using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// Neuron-inspired palette + free-kit helpers.
    /// Portrait-phone first (1080×2340). Original WRLDZ branding.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Vector2 PortraitReference = new(1080f, 2340f);
        public const float PortraitMatch = 0.65f;

        public static void ApplyPortraitPhone(CanvasScaler scaler)
        {
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;

            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = PortraitReference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = PortraitMatch;
            scaler.referencePixelsPerUnit = 100f;
        }

        public static readonly Color BgDeep = new(0.02f, 0.05f, 0.14f, 1f);
        public static readonly Color BgMid = new(0.04f, 0.10f, 0.22f, 1f);
        public static readonly Color HexLine = new(0.12f, 0.35f, 0.65f, 0.35f);

        public static readonly Color NeonCyan = new(0.25f, 0.95f, 1f, 1f);
        public static readonly Color NeonCyanSoft = new(0.15f, 0.75f, 0.95f, 0.55f);
        public static readonly Color NeonBlue = new(0.15f, 0.45f, 0.95f, 1f);
        public static readonly Color GlassPanel = new(0.08f, 0.25f, 0.55f, 0.55f);
        public static readonly Color GlassPanelBright = new(0.12f, 0.40f, 0.75f, 0.72f);
        public static readonly Color DiskHolo = new(0.20f, 0.70f, 0.95f, 0.35f);
        public static readonly Color DiskRim = new(0.40f, 0.90f, 1f, 0.85f);
        public static readonly Color HubFill = new(0.15f, 0.75f, 0.95f, 0.55f);
        public static readonly Color TabBar = new(0.02f, 0.06f, 0.16f, 0.96f);

        public static readonly Color TextPrimary = new(0.98f, 1f, 1f, 1f);
        public static readonly Color TextMuted = new(0.70f, 0.86f, 0.96f, 1f);
        public static readonly Color TextGold = NeonCyan;

        // Back-compat aliases
        public static readonly Color EnergyCyan = NeonCyan;
        public static readonly Color EnergyCyanDim = NeonCyanSoft;
        public static readonly Color Panel = GlassPanel;
        public static readonly Color BrassGold = NeonCyan;
        public static readonly Color BrassBright = NeonCyan;
        public static readonly Color BrassDark = NeonBlue;
        public static readonly Color Silver = new(0.7f, 0.85f, 1f, 1f);
        public static readonly Color SilverDark = new(0.25f, 0.35f, 0.5f, 1f);
        public static readonly Color Gunmetal = new(0.08f, 0.12f, 0.22f, 1f);
        public static readonly Color CuffBlack = new(0.03f, 0.05f, 0.10f, 1f);
        public static readonly Color HubRed = HubFill;
        public static readonly Color HubRedBright = NeonCyan;
        public static readonly Color EnergyAmber = NeonCyan;
        public static readonly Color RimGold = DiskRim;
        public static readonly Color DiskGunmetal = DiskHolo;
        public static readonly Color DiskInner = DiskHolo;
        public static readonly Color BgRadial = BgMid;
        public static readonly Color SlotIdle = GlassPanel;
        public static readonly Color SlotSelected = GlassPanelBright;

        static Sprite _circle;
        static Sprite _ring;
        static Sprite _roundedRect;
        static Sprite _hex;

        public static Font Font()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.DisplayFont()
                   ?? UiFoundation.BuiltinFont();
        }

        public static Font BodyFont()
        {
            FreeUiKit.EnsureLoaded();
            return FreeUiKit.BodyFont() ?? Font();
        }

        public static Sprite CircleSprite()
        {
            // Prefer free kit / generated holo; fall back to procedural
            var kit = FreeUiKit.RoundButton();
            if (kit != null) return kit;
            if (_circle != null) return _circle;
            _circle = MakeCircle(160, true);
            return _circle;
        }

        public static Sprite RingSprite()
        {
            var kit = FreeUiKit.RoundBorder();
            if (kit != null) return kit;
            if (_ring != null) return _ring;
            _ring = MakeCircle(160, false, 0.88f);
            return _ring;
        }

        public static Sprite RoundedRectSprite()
        {
            var kit = FreeUiKit.Panel();
            if (kit != null) return kit;
            if (_roundedRect != null) return _roundedRect;
            _roundedRect = MakeRoundedRect(128, 64, 10);
            return _roundedRect;
        }

        public static Sprite HexSprite()
        {
            if (_hex != null) return _hex;
            _hex = MakeHex(64);
            return _hex;
        }

        public static Sprite BladeSegmentSprite() => CircleSprite();

        static Sprite MakeCircle(int size, bool filled, float innerRatio = 0.7f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var cx = (size - 1) * 0.5f;
            var r = size * 0.5f - 1f;
            var r2 = r * r;
            var ri = r * innerRatio;
            var ri2 = ri * ri;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cx;
                var d2 = dx * dx + dy * dy;
                float a;
                if (filled)
                    a = d2 <= r2 ? (d2 > (r - 1.5f) * (r - 1.5f) ? 0.65f : 1f) : 0f;
                else
                    a = d2 <= r2 && d2 >= ri2 ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite MakeRoundedRect(int w, int h, int radius)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var px = Mathf.Min(x, w - 1 - x);
                var py = Mathf.Min(y, h - 1 - y);
                float a = 1f;
                if (px < radius && py < radius)
                {
                    var dx = radius - px;
                    var dy = radius - py;
                    a = dx * dx + dy * dy <= radius * radius ? 1f : 0f;
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeHex(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var cx = (size - 1) * 0.5f;
            var r = size * 0.48f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var px = (x - cx) / r;
                var py = (y - cx) / r;
                var ax = Mathf.Abs(px);
                var ay = Mathf.Abs(py);
                var inside = ay <= 0.866f && ax <= 1f && ax * 0.5f + ay * 0.866f <= 0.866f;
                var a = 0f;
                if (inside)
                {
                    var near = ay > 0.75f || ax > 0.88f || (ax * 0.5f + ay * 0.866f) > 0.75f;
                    a = near ? 1f : 0f;
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
