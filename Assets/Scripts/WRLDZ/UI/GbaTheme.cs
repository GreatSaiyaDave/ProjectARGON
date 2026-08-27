using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI
{
    /// <summary>
    /// Visual language inspired by GBA-era Yu-Gi-Oh! RPGs —
    /// The Sacred Cards and Worldwide Edition (Stairway to the Destined Duel).
    ///
    /// Hallmarks: deep navy command windows, gold double borders, cream dialog
    /// boxes, bold high-contrast type, red opponent / blue player field halves.
    /// Original WRLDZ branding only — no Konami assets.
    /// </summary>
    public static class GbaTheme
    {
        // ── Core palette — cyberpunk × Master Duel (still used by DuelUI buttons) ──
        public static readonly Color NightSky = Hex(0x050810);
        public static readonly Color NightSkyMid = Hex(0x0A1228);
        public static readonly Color NavyWindow = Hex(0x0C1838);     // glass panels
        public static readonly Color NavyDeep = Hex(0x060C1C);
        public static readonly Color NavySelected = Hex(0x1A3A78);
        public static readonly Color GoldBorder = Hex(0xFFD44A);     // MD gold frame
        public static readonly Color GoldBright = Hex(0xFFE878);
        public static readonly Color GoldDim = Hex(0xB89428);
        public static readonly Color CreamPanel = Hex(0xF8F4E8);
        public static readonly Color CreamDark = Hex(0xD0C8A8);
        public static readonly Color InkText = Hex(0x101018);
        public static readonly Color WhiteText = Hex(0xFAF8F4);
        public static readonly Color SoftText = Hex(0xB0C8E8);
        public static readonly Color CursorYellow = Hex(0xFFE020);
        public static readonly Color PlayerBlue = Hex(0x0A2848);     // your field
        public static readonly Color PlayerBlueInner = Hex(0x061828);
        public static readonly Color OppRed = Hex(0x3A0A1A);         // opponent field
        public static readonly Color OppRedInner = Hex(0x220810);
        public static readonly Color FieldGreen = Hex(0x0A2830);
        public static readonly Color ZoneSlot = Hex(0x081020);
        public static readonly Color CmdSummon = Hex(0x0A3A68);      // cyan-leaning
        public static readonly Color CmdBattle = Hex(0xA84818);      // battle orange
        public static readonly Color CmdDanger = Hex(0x881828);
        public static readonly Color CmdSafe = Hex(0x0A6040);
        public static readonly Color CmdNeutral = Hex(0x142848);
        public static readonly Color CmdMuted = Hex(0x1A2438);
        public static readonly Color HandBar = Hex(0x0A1428);
        public static readonly Color OverlayDim = new(0.01f, 0.02f, 0.06f, 0.92f);

        static Sprite _frameGold;
        static Sprite _frameCream;
        static Sprite _frameNavy;
        static Sprite _pixelBorder;
        static Sprite _checkPattern;

        public static Font Font() => UiFoundation.BuiltinFont();

        public static Color Hex(int rgb)
        {
            var r = ((rgb >> 16) & 0xFF) / 255f;
            var g = ((rgb >> 8) & 0xFF) / 255f;
            var b = (rgb & 0xFF) / 255f;
            return new Color(r, g, b, 1f);
        }

        /// <summary>9-slice-ish gold window border on transparent fill (draw fill separately).</summary>
        public static Sprite FrameGold()
        {
            if (_frameGold != null) return _frameGold;
            _frameGold = MakeBorderFrame(64, 64, 5, GoldBorder, GoldBright, GoldDim);
            return _frameGold;
        }

        public static Sprite FrameNavy()
        {
            if (_frameNavy != null) return _frameNavy;
            _frameNavy = MakeBorderFrame(64, 64, 4, SoftText, WhiteText, NavyDeep);
            return _frameNavy;
        }

        public static Sprite FrameCream()
        {
            if (_frameCream != null) return _frameCream;
            _frameCream = MakeBorderFrame(64, 64, 4, GoldDim, GoldBorder, CreamDark);
            return _frameCream;
        }

        /// <summary>Subtle pixel grid for GBA backdrop texture.</summary>
        public static Sprite CheckPattern()
        {
            if (_checkPattern != null) return _checkPattern;
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var on = ((x / 2) + (y / 2)) % 2 == 0;
                tex.SetPixel(x, y, on
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : new Color(0f, 0f, 0f, 0.04f));
            }

            tex.Apply(false, true);
            _checkPattern = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
            _checkPattern.name = "WRLDZ_GbaCheck";
            return _checkPattern;
        }

        public static Sprite PixelBorder()
        {
            if (_pixelBorder != null) return _pixelBorder;
            _pixelBorder = MakeBorderFrame(32, 32, 2, Color.white, Color.white, Color.clear);
            return _pixelBorder;
        }

        static Sprite MakeBorderFrame(int w, int h, int thickness, Color outer, Color highlight, Color shadow)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var edgeL = x < thickness;
                var edgeR = x >= w - thickness;
                var edgeB = y < thickness;
                var edgeT = y >= h - thickness;
                var inBorder = edgeL || edgeR || edgeB || edgeT;
                if (!inBorder)
                {
                    tex.SetPixel(x, y, Color.clear);
                    continue;
                }

                // Outer pixel row = highlight top/left, shadow bottom/right (GBA bevel)
                var outerRing = x == 0 || y == h - 1 || x == w - 1 || y == 0;
                var topLeft = (y >= h - thickness && !edgeL && !edgeR) || (x < thickness && !edgeB);
                Color c;
                if (outerRing) c = outer;
                else if (topLeft) c = highlight;
                else c = shadow;
                // Inner lip slightly darker
                if (x == thickness - 1 || y == thickness || x == w - thickness || y == h - thickness - 1)
                    c = Color.Lerp(c, shadow, 0.35f);
                tex.SetPixel(x, y, c);
            }

            tex.Apply(false, true);
            var border = thickness;
            var sp = Sprite.Create(
                tex,
                new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sp.name = "WRLDZ_GbaFrame";
            return sp;
        }

        // ── Builders ────────────────────────────────────────────────────────

        public static RectTransform StretchFill(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            return rt;
        }

        /// <summary>GBA-style framed window: solid fill + gold border overlay.</summary>
        public static RectTransform Window(
            Transform parent,
            string name,
            Color fill,
            Sprite frameSprite = null,
            bool raycast = false)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(root.transform, false);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.sprite = UiFoundation.WhiteSprite();
            fillImg.type = Image.Type.Simple;
            fillImg.color = fill;
            fillImg.raycastTarget = raycast;
            Stretch(fillGo.GetComponent<RectTransform>());

            var borderGo = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderGo.transform.SetParent(root.transform, false);
            var borderImg = borderGo.GetComponent<Image>();
            borderImg.sprite = frameSprite ?? FrameGold();
            borderImg.type = Image.Type.Sliced;
            borderImg.color = Color.white;
            borderImg.raycastTarget = false;
            Stretch(borderGo.GetComponent<RectTransform>());

            return rt;
        }

        public static Text Label(
            Transform parent,
            string name,
            string text,
            int size,
            Color color,
            FontStyle style = FontStyle.Bold,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = false;
            t.raycastTarget = false;
            // Soft shadow via second label is optional; keep single layer for clarity
            return t;
        }

        public static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static ColorBlock ButtonColors()
        {
            var c = ColorBlock.defaultColorBlock;
            c.normalColor = Color.white;
            c.highlightedColor = new Color(1.15f, 1.1f, 0.9f, 1f);
            c.pressedColor = new Color(0.75f, 0.7f, 0.55f, 1f);
            c.selectedColor = new Color(1.1f, 1.05f, 0.85f, 1f);
            c.disabledColor = new Color(0.4f, 0.4f, 0.45f, 0.65f);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.08f;
            return c;
        }
    }
}

