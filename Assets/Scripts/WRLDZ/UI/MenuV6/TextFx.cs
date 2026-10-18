using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// D2: the v6.3 type on legacy <c>Text</c> (no TMP), with the OFL faces of SPEC §6 from <c>MenuV6/Fonts/</c>.
    /// The faces are not routed through <c>WrldzType.Style</c> (it adds the heavy outline whenever display is true);
    /// <see cref="Label"/> builds them from the CSS the exporter recorded (layout_land.json texts[].font), so size,
    /// colour, tracking and the hard drop are the approved page's. Landscape is 1:1 CSS px; the portrait plan
    /// (P1b) floors body sizes at <see cref="PhoneMinBody"/> as SPEC §6 keeps it.
    /// </summary>
    public static class TextFx
    {
        /// <summary>WrldzType.MinBody, the phone floor SPEC §6 keeps (portrait plan, P1b).</summary>
        public const int PhoneMinBody = WrldzType.MinBody;

        /// <summary>The v6 font file for a CSS family + weight (the 9 OFL files in MenuV6/Fonts/), else null.</summary>
        public static string FontFile(string family, int weight, out bool synthBold)
        {
            synthBold = false;
            switch (family)
            {
                case "Marcellus":
                    synthBold = weight >= 600;   // the page synthesises bold too (Marcellus has only 400)
                    return "Marcellus-400";
                case "Marcellus SC":
                    synthBold = weight >= 600;
                    return "MarcellusSC-400";
                case "Cairo":
                    return weight >= 650 ? "Cairo-700" : weight >= 550 ? "Cairo-600" : "Cairo-500";
                case "El Messiri":
                    synthBold = weight >= 650;
                    return weight >= 550 ? "ElMessiri-600" : "ElMessiri-500";
                case "Barlow Condensed":
                    return weight >= 650 ? "BarlowCondensed-700" : "BarlowCondensed-600";
                default:
                    return null;
            }
        }

        /// <summary>The font for a slot: the v6 file, else the project's body face (never null in a running Editor).</summary>
        public static Font FontFor(string family, int weight, out bool synthBold)
        {
            var file = FontFile(family, weight, out synthBold);
            var f = file != null ? MenuV6Art.Font(file) : null;
            if (f != null) return f;
            synthBold = weight >= 600;
            return UiTheme.BodyFont();
        }

        /// <summary>
        /// A Text styled from a recorded CSS slot. <paramref name="shown"/> is what Unity shows (real data or approved
        /// copy); the slot's own string is the mockup's and is never shown. Returns null when <paramref name="slot"/>
        /// is null (the plan has no such text).
        /// </summary>
        public static Text Label(Transform parent, string name, MenuV6Layout.TextSlot slot, string shown,
            Rect parentStageRect, int minSize = 0)
        {
            if (slot == null || parent == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            MenuV6Layout.Place(t.rectTransform, slot.Rect, parentStageRect);
            Apply(t, slot, shown, minSize);
            return t;
        }

        /// <summary>Restyle an existing Text (e.g. an ArcaneButton's Title / Blurb) from a CSS slot.</summary>
        public static void Apply(Text t, MenuV6Layout.TextSlot slot, string shown, int minSize = 0)
        {
            if (t == null || slot == null) return;
            StripLegacyEffects(t);
            t.font = FontFor(slot.Family, slot.Weight, out var synthBold);
            t.fontSize = Mathf.Max(minSize, Mathf.Max(1, Mathf.FloorToInt(slot.Size + 0.5f)));
            t.fontStyle = slot.Italic
                ? (synthBold ? FontStyle.BoldAndItalic : FontStyle.Italic)
                : (synthBold ? FontStyle.Bold : FontStyle.Normal);
            var c = slot.Color;
            c.a *= Mathf.Clamp01(slot.Opacity);
            t.color = c;
            var s = shown ?? "";
            t.text = slot.Upper ? s.ToUpperInvariant() : s;
            t.supportRichText = false;
            t.resizeTextForBestFit = false;
            t.raycastTarget = false;
            t.horizontalOverflow = slot.NoWrap ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var lines = slot.LineHeight > 0f ? slot.Rect.height / slot.LineHeight : slot.Rect.height / Mathf.Max(1f, slot.Size * 1.6f);
            var multi = !slot.NoWrap && lines > 1.6f;
            var h = slot.Align == "center" ? 1 : slot.Align == "right" || slot.Align == "end" ? 2 : 0;
            t.alignment = multi
                ? (h == 1 ? TextAnchor.UpperCenter : h == 2 ? TextAnchor.UpperRight : TextAnchor.UpperLeft)
                : (h == 1 ? TextAnchor.MiddleCenter : h == 2 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            // one line: the rect is the CSS line box and Middle centres the glyphs in it as CSS does; only a wrapped
            // block needs the CSS line-height as Unity line spacing
            t.lineSpacing = multi ? LineSpacingFor(t, slot) : 1f;
            // tracking on a wrapped block would widen lines Unity already broke without it: only single-line slots,
            // or tracking big enough to matter (the caps), get it
            if (Mathf.Abs(slot.LetterSpacing) > 0.05f && (slot.NoWrap || !multi || Mathf.Abs(slot.LetterSpacing) >= 2f))
                Spacing(t, slot.LetterSpacing);
            if (slot.Shadow)
                HardDrop(t, slot.ShadowColor, slot.ShadowOffset, slot.ShadowBlur);
            else if (!string.IsNullOrEmpty(slot.Filter) && slot.Filter.StartsWith("drop-shadow") &&
                     MenuV6Layout.Css.Shadow(slot.Filter.Substring("drop-shadow".Length).Trim('(', ')', ' '),
                         out var fc, out var fo, out var fb))
                HardDrop(t, fc, fo, fb);
        }

        static float LineSpacingFor(Text t, MenuV6Layout.TextSlot slot)
        {
            if (slot.LineHeight <= 0f || t.font == null || t.font.fontSize <= 0 || t.font.lineHeight <= 0) return 1f;
            var natural = t.font.lineHeight * t.fontSize / (float)t.font.fontSize;
            return natural > 0f ? Mathf.Clamp(slot.LineHeight / natural, 0.5f, 2f) : 1f;
        }

        /// <summary>Remove the Outline / Shadow that WrldzType.ApplyOutline / StyleButtonLabel added.</summary>
        public static void StripLegacyEffects(Text t)
        {
            if (t == null) return;
            var fx = t.GetComponents<Shadow>();   // Outline derives from Shadow
            for (var i = 0; i < fx.Length; i++)
                if (fx[i] != null)
                    Object.DestroyImmediate(fx[i]);
        }

        public static LetterSpacingEffect Spacing(Text t, float px)
        {
            if (t == null) return null;
            var e = t.GetComponent<LetterSpacingEffect>();
            if (e == null) e = t.gameObject.AddComponent<LetterSpacingEffect>();
            e.Spacing = px;
            return e;
        }

        /// <summary>The CSS hard drop (text-shadow / drop-shadow with 0 blur) as the built-in Shadow; a soft CSS shadow
        /// becomes a hard one at 60 % of its alpha (legacy Text has no blur).</summary>
        public static Shadow HardDrop(Graphic g, Color c, Vector2 cssOffset, float blur = 0f)
        {
            if (g == null) return null;
            var sh = g.gameObject.AddComponent<Shadow>();
            if (blur > 0.5f) c.a *= 0.6f;
            sh.effectColor = c;
            sh.effectDistance = new Vector2(cssOffset.x, -cssOffset.y);
            sh.useGraphicAlpha = true;
            return sh;
        }

        /// <summary>The DUEL word: three hard gold bands of the element box (CSS background-clip: text).</summary>
        public static BandFaceEffect Bands(Text t, IList<(Color c, float from, float to)> bands)
        {
            if (t == null || bands == null || bands.Count == 0) return null;
            // mesh effects run in component order: the bands must colour the glyphs before a Shadow copies them,
            // so any drop already on the Text is taken off and put back after the band effect
            var drops = new List<(Color c, Vector2 d, bool a)>();
            foreach (var sh in t.GetComponents<Shadow>())
            {
                if (sh == null) continue;
                drops.Add((sh.effectColor, sh.effectDistance, sh.useGraphicAlpha));
                Object.DestroyImmediate(sh);
            }

            var e = t.GetComponent<BandFaceEffect>();
            if (e == null) e = t.gameObject.AddComponent<BandFaceEffect>();
            e.SetBands(bands);
            t.color = Color.white;
            foreach (var (c, d, a) in drops)
            {
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = c;
                sh.effectDistance = d;
                sh.useGraphicAlpha = a;
            }

            return e;
        }

        /// <summary>Shrink the font until the text (with its tracking) fits <paramref name="maxWidth"/> canvas units.</summary>
        public static void FitWidth(Text t, float maxWidth, int minSize)
        {
            if (t == null || t.font == null || maxWidth <= 0f || string.IsNullOrEmpty(t.text)) return;
            var ls = t.GetComponent<LetterSpacingEffect>();
            var perChar = ls != null ? ls.Spacing : 0f;
            var baseSize = t.fontSize;
            var baseSpacing = perChar;
            var n = t.text.Length;
            for (var guard = 0; guard < 64 && t.fontSize > minSize; guard++)
            {
                var w = t.preferredWidth + perChar * n;
                if (w <= maxWidth) break;
                t.fontSize -= 1;
                if (ls != null) perChar = ls.Spacing = baseSpacing * t.fontSize / baseSize;
            }
        }
    }

    /// <summary>
    /// CSS letter-spacing on a legacy Text: every glyph quad moves by (index in its line) × spacing, and the line is
    /// re-aligned for the extra width (CSS adds the spacing after every character, the last one included). The glyph
    /// index comes from the generator's cursor positions, so spaces (which have no quad) still count.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class LetterSpacingEffect : BaseMeshEffect
    {
        [SerializeField] float _spacing;
        static readonly List<UIVertex> Stream = new();

        public float Spacing
        {
            get => _spacing;
            set
            {
                _spacing = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Abs(_spacing) < 0.001f) return;
            var text = graphic as Text;
            if (text == null) return;
            var gen = text.cachedTextGenerator;
            if (gen == null || gen.characterCount == 0 || gen.lineCount == 0) return;
            var chars = gen.characters;
            var lines = gen.lines;
            var upp = 1f / Mathf.Max(0.0001f, text.pixelsPerUnit);
            var len = text.text != null ? text.text.Length : 0;
            var halign = (int)text.alignment % 3;   // 0 left, 1 centre, 2 right

            Stream.Clear();
            vh.GetUIVertexStream(Stream);
            for (var q = 0; q + 5 < Stream.Count; q += 6)
            {
                float cx = 0f, cy = 0f;
                for (var k = 0; k < 6; k++)
                {
                    cx += Stream[q + k].position.x;
                    cy += Stream[q + k].position.y;
                }

                cx /= 6f;
                cy /= 6f;
                var gx = cx / upp;
                var gy = cy / upp;

                var line = 0;
                for (var l = 0; l < lines.Count; l++)
                {
                    if (gy <= lines[l].topY + 0.5f) line = l;
                    else break;
                }

                var start = lines[line].startCharIdx;
                var end = line + 1 < lines.Count ? lines[line + 1].startCharIdx : Mathf.Min(len, chars.Count);
                end = Mathf.Min(end, Mathf.Min(len, chars.Count));
                var count = 0;
                var idx = -1;
                for (var c = start; c < end; c++)
                {
                    if (text.text[c] == '\n') continue;
                    count++;
                    if (chars[c].cursorPos.x <= gx) idx = count - 1;
                }

                if (idx < 0) idx = 0;
                var shift = _spacing * idx - (halign == 1 ? _spacing * count * 0.5f : halign == 2 ? _spacing * count : 0f);
                for (var k = 0; k < 6; k++)
                {
                    var v = Stream[q + k];
                    v.position.x += shift;
                    Stream[q + k] = v;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(Stream);
        }
    }

    /// <summary>
    /// Hard horizontal colour bands over a Text's element box (the DUEL word: #EDE49E 0–34 %, #CAA95B –72 %, #917649
    /// below). Each glyph quad is cut at the band edges, which are fractions of the RectTransform height from its top,
    /// as CSS lays a background gradient over the element box.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class BandFaceEffect : BaseMeshEffect
    {
        [SerializeField] Color[] _colors = { };
        [SerializeField] float[] _from = { };
        [SerializeField] float[] _to = { };
        static readonly List<UIVertex> In = new();
        static readonly List<UIVertex> Out = new();

        public void SetBands(IList<(Color c, float from, float to)> bands)
        {
            _colors = new Color[bands.Count];
            _from = new float[bands.Count];
            _to = new float[bands.Count];
            for (var i = 0; i < bands.Count; i++)
            {
                _colors[i] = bands[i].c;
                _from[i] = bands[i].from;
                _to[i] = bands[i].to;
            }

            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _colors == null || _colors.Length == 0) return;
            var rect = graphic.rectTransform.rect;
            if (rect.height <= 0f) return;
            In.Clear();
            Out.Clear();
            vh.GetUIVertexStream(In);
            for (var q = 0; q + 5 < In.Count; q += 6)
            {
                // Text quads: v0 TL, v1 TR, v2 BR, v3 BL; the stream is (v0 v1 v2) (v2 v3 v0)
                var v0 = In[q];
                var v1 = In[q + 1];
                var v2 = In[q + 2];
                var v3 = In[q + 4];
                var yTop = v0.position.y;
                var yBot = v3.position.y;
                if (yTop - yBot < 0.0001f)
                {
                    for (var k = 0; k < 6; k++) Out.Add(In[q + k]);
                    continue;
                }

                for (var b = 0; b < _colors.Length; b++)
                {
                    var bandTop = b == 0 ? float.PositiveInfinity : rect.yMax - _from[b] * rect.height;
                    var bandBot = b == _colors.Length - 1 ? float.NegativeInfinity : rect.yMax - _to[b] * rect.height;
                    var hi = Mathf.Min(yTop, bandTop);
                    var lo = Mathf.Max(yBot, bandBot);
                    if (hi - lo <= 0.0001f) continue;
                    var u0 = (yTop - hi) / (yTop - yBot);
                    var u1 = (yTop - lo) / (yTop - yBot);
                    var a = Lerp(v0, v3, u0);
                    var bb = Lerp(v1, v2, u0);
                    var c = Lerp(v1, v2, u1);
                    var d = Lerp(v0, v3, u1);
                    var col = _colors[b];
                    var alpha = v0.color.a / 255f;
                    Color32 c32 = new Color(col.r, col.g, col.b, col.a * alpha);
                    a.color = bb.color = c.color = d.color = c32;
                    Out.Add(a);
                    Out.Add(bb);
                    Out.Add(c);
                    Out.Add(c);
                    Out.Add(d);
                    Out.Add(a);
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(Out);
        }

        static UIVertex Lerp(UIVertex a, UIVertex b, float t)
        {
            var v = a;
            v.position = Vector3.LerpUnclamped(a.position, b.position, t);
            v.uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t);
            v.uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t);
            v.normal = a.normal;
            v.tangent = a.tangent;
            return v;
        }
    }

    /// <summary>A hollow diamond (◇) drawn as four strokes: the placeholder strip's mark. Cairo has no U+25C7, and a
    /// missing glyph would fall back to whatever the OS has (tofu on a phone).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HollowDiamond : MaskableGraphic
    {
        public float Stroke = 1.2f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var c = r.center;
            var hx = r.width * 0.5f;
            var hy = r.height * 0.5f;
            Vector2[] p = { new(c.x, c.y + hy), new(c.x + hx, c.y), new(c.x, c.y - hy), new(c.x - hx, c.y) };
            for (var i = 0; i < 4; i++) Seg(vh, p[i], p[(i + 1) % 4]);
        }

        void Seg(VertexHelper vh, Vector2 a, Vector2 b)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (Stroke * 0.5f);
            var i = vh.currentVertCount;
            var col = (Color32)color;
            vh.AddVert(a - n, col, Vector2.zero);
            vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero);
            vh.AddVert(b - n, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
