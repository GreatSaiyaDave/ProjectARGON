using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.UI
{
    /// <summary>
    /// Widgets for the duelist PROFILE and CUSTOMIZE screens (2026-10-18, owner: "This needs a AAA upgrade"). They
    /// use the approved v6.3 main-menu look: the MenuV6 fonts and chrome9 plates, WrldzAnimePalette, and the style
    /// guide's measured Millennium gold. Basalt slates carry gold rules and frames; titles are Marcellus SC, text is
    /// Cairo, small caps are Barlow Condensed.
    /// Every widget is placed in stage pixels (origin top-left, y down) inside a container whose units are those
    /// pixels (<see cref="CzStageFit"/>), so a layout reads like the 1920x1080 / 1080x2340 plan it came from.
    /// </summary>
    public static class CzKit
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color Kem = WrldzAnimePalette.Kem;
        public static readonly Color Basalt = WrldzAnimePalette.Basalt;
        public static readonly Color Gold = WrldzAnimePalette.Hex(0xCAA95B);      // style guide §2, measured off the Millennium Eye
        public static readonly Color GoldHi = WrldzAnimePalette.Hex(0xEDE49E);
        public static readonly Color GoldLo = WrldzAnimePalette.Hex(0x917649);
        public static readonly Color GoldDeep = WrldzAnimePalette.Hex(0x504327);
        public static readonly Color Label = WrldzAnimePalette.GoldLit;
        public static readonly Color Text = WrldzAnimePalette.Gyp;
        public static readonly Color Muted = WrldzAnimePalette.Muted;
        public static readonly Color Rubric = WrldzAnimePalette.Rubric;
        public static readonly Color Pylon = new(0.106f, 0.094f, 0.125f, 1f);   // the painted pylon slate in room.png (#1B1820)
        public static readonly Color Ink = Color.black;

        // ------------------------------------------------------------------ fonts
        public static Font Display => TextFx.FontFor("Marcellus SC", 400, out _);
        public static Font Body(int weight = 600) => TextFx.FontFor("Cairo", weight, out _);
        public static Font Caps => TextFx.FontFor("Barlow Condensed", 700, out _);

        // ------------------------------------------------------------------ art
        const string Chrome = "WRLDZ/MenuV6/Common/chrome9/";
        const string Land = "WRLDZ/MenuV6/Main/Land/";
        public static Sprite Plate => MenuV6Art.Sprite(Chrome + "rl_plate");
        public static Sprite Frame => MenuV6Art.Sprite(Chrome + "niche_frame");
        public static Sprite RedPlate => MenuV6Art.Sprite(Chrome + "quit_plate");
        public static Sprite Rule => MenuV6Art.Sprite(Chrome + "cap_rule");
        public static Sprite Track => MenuV6Art.Sprite(Chrome + "xp_track");
        public static Sprite Room => MenuV6Art.Sprite(Land + "room");
        public static Sprite LandArt(string name) => MenuV6Art.Sprite(Land + name);

        // ------------------------------------------------------------------ procedural sprites (made once, never assets)
        static Sprite _circle, _ring, _diamond, _well, _hgrad, _vfade, _soft;

        /// <summary>An anti-aliased filled disc.</summary>
        public static Sprite Circle() => _circle ??= Make("CzCircle", 128, (x, y) =>
        {
            var d = Mathf.Sqrt(x * x + y * y);
            return new Color(1f, 1f, 1f, Edge(1f - d, 128));
        });

        /// <summary>An anti-aliased ring (inner radius 0.80).</summary>
        public static Sprite Ring() => _ring ??= Make("CzRing", 128, (x, y) =>
        {
            var d = Mathf.Sqrt(x * x + y * y);
            return new Color(1f, 1f, 1f, Mathf.Min(Edge(1f - d, 128), Edge(d - 0.80f, 128)));
        });

        /// <summary>A diamond (the slider handle and list markers).</summary>
        public static Sprite Diamond() => _diamond ??= Make("CzDiamond", 64, (x, y) =>
            new Color(1f, 1f, 1f, Edge(1f - (Mathf.Abs(x) + Mathf.Abs(y)), 64)));

        /// <summary>The picture well of a card: a lapis-dark niche lit from the upper left (style guide §4 recess rule).</summary>
        public static Sprite Well() => _well ??= Make("CzWell", 128, (x, y) =>
        {
            var lit = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(-0.35f, 0.45f)) / 1.9f);
            var c = Color.Lerp(new Color(0.055f, 0.055f, 0.085f), new Color(0.15f, 0.16f, 0.26f), lit * lit);
            return new Color(c.r, c.g, c.b, 1f);
        });

        /// <summary>Horizontal white→white ramp with alpha 0.55 → 1 (multiplied by a tint: the gold slider fill).</summary>
        public static Sprite HGrad() => _hgrad ??= Make("CzHGrad", 64, (x, y) =>
        {
            var k = (x + 1f) * 0.5f;
            return new Color(Mathf.Lerp(0.62f, 1f, k), Mathf.Lerp(0.62f, 1f, k), Mathf.Lerp(0.62f, 1f, k), 1f);
        });

        /// <summary>Vertical alpha fade (opaque at the bottom): the "more below" lip of a scrolling list.</summary>
        public static Sprite VFade() => _vfade ??= Make("CzVFade", 64, (x, y) => new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (1f - y) * 0.5f)));

        /// <summary>A soft round falloff (backdrop glow behind the duelist).</summary>
        public static Sprite Soft() => _soft ??= Make("CzSoft", 128, (x, y) =>
        {
            var d = Mathf.Clamp01(Mathf.Sqrt(x * x + y * y));
            return new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.2f));
        });

        static float Edge(float signed, int size) => Mathf.Clamp01(signed * size * 0.5f + 0.5f);

        static Sprite Make(string name, int size, Func<float, float, Color> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            var px = new Color[size * size];
            for (var j = 0; j < size; j++)
            for (var i = 0; i < size; i++)
                px[j * size + i] = f((i + 0.5f) / size * 2f - 1f, (j + 0.5f) / size * 2f - 1f);
            tex.SetPixels(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sp.name = name;
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        // ------------------------------------------------------------------ nodes and placement

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Place in parent pixels: (x, y) = the top-left corner, y down. The pivot is the centre (hover scales evenly).</summary>
        public static T Place<T>(T c, float x, float y, float w, float h) where T : Component
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            return c;
        }

        public static void Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool raycast = false, float ppuMul = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var im = go.GetComponent<Image>();
            im.sprite = sprite != null ? sprite : UiFoundation.WhiteSprite();
            if (sprite != null && sprite.border.sqrMagnitude > 0f) { im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = ppuMul; }
            im.color = color;
            im.raycastTarget = raycast;
            return im;
        }

        public static Text Txt(Transform parent, string name, string s, Font font, int size, Color c, TextAnchor a, float track = 0f, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.color = c;
            t.alignment = a;
            t.text = s ?? "";
            t.supportRichText = false;
            t.raycastTarget = false;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 0.92f;
            if (Mathf.Abs(track) > 0.05f) TextFx.Spacing(t, track);
            return t;
        }

        /// <summary>The DUEL word's three hard gold bands + the hard ink drop: screen titles.</summary>
        public static Text Title(Transform parent, string s, int size)
        {
            var t = Txt(parent, "Title", s, Display, size, Color.white, TextAnchor.MiddleCenter, size * 0.08f);
            TextFx.HardDrop(t, new Color(0f, 0f, 0f, 0.85f), new Vector2(0f, 3f));
            TextFx.Bands(t, new List<(Color, float, float)>
            {
                (WrldzAnimePalette.DuelBandTop, 0f, 0.36f), (WrldzAnimePalette.DuelBandMid, 0.36f, 0.72f), (WrldzAnimePalette.DuelBandLow, 0.72f, 1f)
            });
            return t;
        }

        /// <summary>A dark basalt slate (rl_plate, bevel lit from the upper left).</summary>
        public static Image Slate(Transform parent, string name, Color tint, bool raycast = false)
        {
            var sp = Plate;
            var im = Img(parent, name, sp, sp != null ? tint : new Color(0.11f, 0.10f, 0.13f, tint.a), raycast);
            return im;
        }

        /// <summary>The gold niche frame stretched over its parent (outset px around the parent's rect).</summary>
        public static Image GoldFrame(Transform parent, float outset = 3f, float ppuMul = 2.4f, float alpha = 1f)
        {
            var sp = Frame;
            var im = Img(parent, "GoldFrame", sp, new Color(1f, 1f, 1f, alpha), false, ppuMul);
            if (sp == null) { im.color = new Color(Gold.r, Gold.g, Gold.b, 0f); }
            Fill(im.rectTransform, -outset);
            return im;
        }

        public static Image GoldRule(Transform parent, float x, float y, float w, float alpha = 1f)
        {
            var sp = Rule;
            var im = Img(parent, "Rule", sp, sp != null ? new Color(1f, 1f, 1f, alpha) : new Color(Gold.r, Gold.g, Gold.b, alpha));
            Place(im, x, y, w, 5f);
            return im;
        }

        // ------------------------------------------------------------------ buttons

        public enum Kind { Gold, Plain, Danger, Ghost }

        public static Button Btn(Transform parent, string name, float x, float y, float w, float h, string label, Kind kind, Action click, int size = 24)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            Place(bg, x, y, w, h);
            bg.raycastTarget = true;
            switch (kind)
            {
                case Kind.Danger:
                    bg.sprite = RedPlate ?? UiFoundation.WhiteSprite();
                    if (RedPlate != null) bg.type = Image.Type.Sliced;
                    bg.color = RedPlate != null ? Color.white : Rubric;
                    break;
                case Kind.Ghost:
                    bg.sprite = UiFoundation.WhiteSprite();
                    bg.color = new Color(0f, 0f, 0f, 0.001f);
                    break;
                default:
                    bg.sprite = Plate ?? UiFoundation.WhiteSprite();
                    if (Plate != null) bg.type = Image.Type.Sliced;
                    bg.color = Plate != null ? (kind == Kind.Gold ? new Color(0.92f, 0.82f, 0.62f) : Color.white) : new Color(0.12f, 0.11f, 0.14f);
                    break;
            }
            if (kind == Kind.Gold)
            {
                var wash = Img(go.transform, "Wash", UiFoundation.WhiteSprite(), new Color(Gold.r, Gold.g, Gold.b, 0.10f));
                Fill(wash.rectTransform, 4f);
                GoldFrame(go.transform, 2f, 2.8f);
            }
            var col = kind switch { Kind.Gold => GoldHi, Kind.Ghost => Muted, _ => Text };
            var t = Txt(go.transform, "Label", label, Display, size, col, TextAnchor.MiddleCenter, size * 0.06f);
            Fill(t.rectTransform);
            if (kind != Kind.Ghost) TextFx.HardDrop(t, new Color(0f, 0f, 0f, 0.8f), new Vector2(0f, 2f));
            var b = go.GetComponent<Button>();
            b.targetGraphic = bg;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => click?.Invoke());
            var hv = go.AddComponent<CzHover>();
            hv.Label = t; hv.LabelRest = col; hv.LabelHover = kind == Kind.Ghost ? Label : Color.Lerp(col, Color.white, 0.35f);
            return b;
        }

        /// <summary>A round icon button on a gold ring (the stage's turn arrows).</summary>
        public static Button Round(Transform parent, string name, float cx, float cy, float d, string glyph, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            Place(bg, cx - d * 0.5f, cy - d * 0.5f, d, d);
            bg.sprite = Circle(); bg.color = new Color(0.07f, 0.065f, 0.09f, 0.92f); bg.raycastTarget = true;
            var ring = Img(go.transform, "Ring", Ring(), Gold);
            Fill(ring.rectTransform, -1f);
            var t = Txt(go.transform, "Glyph", glyph, Body(700), Mathf.RoundToInt(d * 0.58f), GoldHi, TextAnchor.MiddleCenter);
            Fill(t.rectTransform);
            t.rectTransform.anchoredPosition = new Vector2(0f, d * 0.04f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = bg; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => click?.Invoke());
            var hv = go.AddComponent<CzHover>();
            hv.Label = t; hv.LabelRest = GoldHi; hv.LabelHover = Color.white; hv.HoverScale = 1.08f;
            return b;
        }

        /// <summary>A text tab (sub-category): caps with tracking; the selected one is gold with a gold rule under it.</summary>
        public static Button Chip(Transform parent, float x, float y, float h, string label, bool selected, Action click, int size, out float width)
        {
            var go = new GameObject("Chip_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = selected ? new Color(Gold.r, Gold.g, Gold.b, 0.12f) : new Color(1f, 1f, 1f, 0.025f);
            bg.raycastTarget = true;
            var col = selected ? GoldHi : Muted;
            var t = Txt(go.transform, "Label", label.ToUpperInvariant(), Caps, size, col, TextAnchor.MiddleCenter, size * 0.12f);
            var tw = t.preferredWidth + size * 0.12f * label.Length;
            width = Mathf.Ceil(tw + size * 1.3f);
            Place(bg, x, y, width, h);
            Fill(t.rectTransform);
            if (selected)
            {
                var r = Img(go.transform, "Under", Rule ?? UiFoundation.WhiteSprite(), Rule != null ? Color.white : Gold);
                r.rectTransform.anchorMin = new Vector2(0.08f, 0f); r.rectTransform.anchorMax = new Vector2(0.92f, 0f);
                r.rectTransform.pivot = new Vector2(0.5f, 0f);
                r.rectTransform.sizeDelta = new Vector2(0f, 4f); r.rectTransform.anchoredPosition = new Vector2(0f, 1f);
            }
            var b = go.GetComponent<Button>();
            b.targetGraphic = bg; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => click?.Invoke());
            var hv = go.AddComponent<CzHover>();
            hv.HoverScale = 1.0f; hv.PressScale = 0.97f;
            hv.Label = t; hv.LabelRest = col; hv.LabelHover = selected ? GoldHi : Text;
            return b;
        }

        // ------------------------------------------------------------------ cards and swatches

        /// <summary>
        /// A wardrobe card: a basalt slate, a lit picture well with the piece, the name under it, and an optional tag in
        /// the corner. The worn / chosen card wears the gold frame and a gold name.
        /// </summary>
        public static Button Card(Transform parent, string name, float x, float y, float w, float h, Sprite pic, string label,
            bool selected, Action click, Color? picTint = null, string tag = null, int labelSize = 16)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            Place(bg, x, y, w, h);
            bg.sprite = Plate ?? UiFoundation.WhiteSprite();
            if (Plate != null) bg.type = Image.Type.Sliced;
            bg.color = Plate != null ? (selected ? new Color(1f, 0.93f, 0.80f) : Color.white) : new Color(0.12f, 0.11f, 0.14f);
            bg.raycastTarget = true;

            if (selected)
            {
                var wash = Img(go.transform, "Chosen", UiFoundation.WhiteSprite(), new Color(Gold.r, Gold.g, Gold.b, 0.10f));
                Fill(wash.rectTransform, 3f);
            }
            var pad = Mathf.Round(w * 0.05f);
            var wellH = h - pad - Mathf.Round(labelSize * 2.6f);
            var well = Img(go.transform, "Well", Well(), Color.white);
            Place(well, pad, pad, w - pad * 2f, wellH);
            if (pic != null)
            {
                var p = Img(well.transform, "Picture", pic, picTint ?? Color.white);
                p.preserveAspect = true;
                Fill(p.rectTransform, 2f);
            }
            var lab = Txt(go.transform, "Name", label, Body(600), labelSize, selected ? GoldHi : Text, TextAnchor.MiddleCenter, 0f, true);
            Place(lab, pad * 0.5f, pad + wellH, w - pad, h - pad - wellH);
            lab.resizeTextForBestFit = true; lab.resizeTextMinSize = Mathf.Max(10, labelSize - 4); lab.resizeTextMaxSize = labelSize;
            lab.verticalOverflow = VerticalWrapMode.Truncate;
            if (!string.IsNullOrEmpty(tag))
            {
                var ts = Mathf.RoundToInt(labelSize * 0.8f);
                var pill = Img(go.transform, "TagPill", UiFoundation.WhiteSprite(), new Color(Kem.r, Kem.g, Kem.b, 0.82f));
                var tg = Txt(pill.transform, "Tag", tag.ToUpperInvariant(), Caps, ts, Label, TextAnchor.MiddleCenter, 1.5f);
                var tw = Mathf.Min(w - pad * 2f, tg.preferredWidth + 1.5f * tag.Length + ts * 0.9f);
                Place(pill, pad + 3f, pad + 3f, tw, ts * 1.5f);
                Fill(tg.rectTransform);
            }
            Image frame = null;
            if (selected) frame = GoldFrame(go.transform, 3f, 2.6f);
            var b = go.GetComponent<Button>();
            b.targetGraphic = bg; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => click?.Invoke());
            var hv = go.AddComponent<CzHover>();
            hv.Label = lab; hv.LabelRest = lab.color; hv.LabelHover = selected ? GoldHi : Color.white;
            if (frame == null)
            {
                // a faint gold frame fades in under the pointer
                var hf = GoldFrame(go.transform, 3f, 2.6f, 0f);
                hv.Glow = hf; hv.GlowRest = 0f; hv.GlowHover = 0.55f;
            }
            return b;
        }

        /// <summary>A colour gem: the colour in an ink ring; the chosen one gets an outer gold ring.</summary>
        public static Button Swatch(Transform parent, float x, float y, float d, Color c, bool selected, Action click, string mark = null)
        {
            var go = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var hit = go.GetComponent<Image>();
            Place(hit, x, y, d, d);
            hit.sprite = Circle(); hit.color = Ink; hit.raycastTarget = true;
            var fill = Img(go.transform, "Colour", Circle(), c);
            Fill(fill.rectTransform, d * 0.07f);
            if (!string.IsNullOrEmpty(mark))
            {
                var m = Txt(go.transform, "Mark", mark, Caps, Mathf.RoundToInt(d * 0.30f), Text, TextAnchor.MiddleCenter, 0.5f);
                Fill(m.rectTransform);
                TextFx.HardDrop(m, new Color(0f, 0f, 0f, 0.9f), new Vector2(1f, 1.5f));
            }
            if (selected)
            {
                var r = Img(go.transform, "Chosen", Ring(), GoldHi);
                Fill(r.rectTransform, -d * 0.16f);
            }
            var b = go.GetComponent<Button>();
            b.targetGraphic = hit; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => click?.Invoke());
            var hv = go.AddComponent<CzHover>();
            hv.HoverScale = 1.10f;
            return b;
        }

        // ------------------------------------------------------------------ slider

        /// <summary>
        /// A labelled slider row (label top-left, value top-right, a gold-filled track with a diamond handle under them).
        /// Row height = size * 3.4. Drags only call <paramref name="changed"/>; nothing is rebuilt under the finger.
        /// </summary>
        public static Slider SliderRow(Transform parent, float x, float y, float w, string label, float min, float max, float value,
            Func<float, string> fmt, Action<float> changed, int size = 19, bool whole = false)
        {
            var row = Node(parent, "Row_" + label);
            var rowH = size * 3.4f;
            Place(row, x, y, w, rowH);
            var lab = Txt(row, "Label", label.ToUpperInvariant(), Caps, size, Label, TextAnchor.MiddleLeft, size * 0.1f);
            Place(lab, 0f, 0f, w * 0.62f, size * 1.4f);
            var val = Txt(row, "Value", "", Body(600), size, Text, TextAnchor.MiddleRight);
            Place(val, w * 0.40f, 0f, w * 0.60f, size * 1.4f);

            var sgo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sgo.transform.SetParent(row, false);
            var srt = (RectTransform)sgo.transform;
            Place(srt, 0f, size * 1.85f, w, size * 1.4f);
            var trackH = Mathf.Round(size * 0.62f);
            var track = Img(sgo.transform, "Track", Track, Track != null ? Color.white : new Color(0.06f, 0.06f, 0.08f));
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f); track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, trackH);
            var fillArea = Node(sgo.transform, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-6f, trackH - 6f);
            var fill = Img(fillArea, "Fill", HGrad(), Gold);
            var frt = fill.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0f, 1f); frt.offsetMin = frt.offsetMax = Vector2.zero;
            var handleArea = Node(sgo.transform, "Handle Slide Area");
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(size * 0.6f, 0f); handleArea.offsetMax = new Vector2(-size * 0.6f, 0f);
            var handle = Img(handleArea, "Handle", Diamond(), GoldHi, true);
            var ink = handle.gameObject.AddComponent<Outline>();
            ink.effectColor = Ink; ink.effectDistance = new Vector2(1.6f, -1.6f);
            var hrt = handle.rectTransform; hrt.anchorMin = new Vector2(0f, 0.5f); hrt.anchorMax = new Vector2(0f, 0.5f);
            hrt.sizeDelta = new Vector2(size * 1.25f, size * 1.25f);

            var s = sgo.GetComponent<Slider>();
            s.fillRect = frt; s.handleRect = hrt; s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.transition = Selectable.Transition.None;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            if (fmt != null) val.text = fmt(s.value);
            s.onValueChanged.AddListener(v =>
            {
                if (fmt != null) val.text = fmt(v);
                changed?.Invoke(v);
            });
            return s;
        }

        // ------------------------------------------------------------------ scrolling list

        /// <summary>A vertical scroll list in parent pixels; returns its content (stage-pixel children via <see cref="Place"/>).</summary>
        public static RectTransform Scroll(Transform parent, string name, float x, float y, float w, float h, out ScrollRect sr, Color? lip = null)
        {
            var vp = Img(parent, name, UiFoundation.WhiteSprite(), new Color(0f, 0f, 0f, 0.001f), true);
            Place(vp, x, y, w, h);
            vp.gameObject.AddComponent<RectMask2D>();
            var content = Node(vp.transform, "Content");
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, h); content.anchoredPosition = Vector2.zero;
            sr = vp.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp.rectTransform; sr.content = content;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic; sr.elasticity = 0.08f;
            sr.inertia = true; sr.decelerationRate = 0.12f; sr.scrollSensitivity = 36f;
            if (lip.HasValue)
            {
                var f = Img(vp.transform, "MoreLip", VFade(), lip.Value);
                f.rectTransform.anchorMin = new Vector2(0f, 0f); f.rectTransform.anchorMax = new Vector2(1f, 0f);
                f.rectTransform.pivot = new Vector2(0.5f, 0f); f.rectTransform.sizeDelta = new Vector2(0f, 34f);
                f.rectTransform.anchoredPosition = Vector2.zero;
                var lipCtl = vp.gameObject.AddComponent<CzScrollLip>();
                lipCtl.Lip = f; lipCtl.Scroll = sr;
            }
            return content;
        }

        /// <summary>Kill children now (a rebuild in the same frame must not see last frame's rows).</summary>
        public static void Clear(Transform t)
        {
            if (t == null) return;
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(c); else UnityEngine.Object.DestroyImmediate(c);
            }
        }
    }

    /// <summary>A top-down layout cursor inside a <see cref="CzKit.Scroll"/> content: headers, grids, rows, hints.</summary>
    public sealed class CzFlow
    {
        public readonly RectTransform Content;
        public readonly float Width;
        public readonly float K;   // text scale: 1 at the landscape plan, more on the portrait phone plan
        public float Y;
        /// <summary>No fade-in (a redraw after a click: the rows are already on screen).</summary>
        public bool NoReveal;
        int _n;

        public CzFlow(RectTransform content, float width, float k)
        {
            Content = content; Width = width; K = k; Y = 4f * k;
        }

        public int S(float px) => Mathf.RoundToInt(px * K);

        public void Gap(float px) => Y += px * K;

        public void Header(string text, string right = null)
        {
            if (Y > 8f * K) Y += 8f * K;
            var t = CzKit.Txt(Content, "Header", text.ToUpperInvariant(), CzKit.Caps, S(17), CzKit.Muted, TextAnchor.MiddleLeft, 3f * K);
            CzKit.Place(t, 2f, Y, Width * 0.7f, S(24));
            if (!string.IsNullOrEmpty(right))
            {
                var r = CzKit.Txt(Content, "HeaderRight", right, CzKit.Body(600), S(15), CzKit.Muted, TextAnchor.MiddleRight);
                CzKit.Place(r, Width * 0.4f, Y, Width * 0.6f - 2f, S(24));
            }
            Y += S(26);
            CzKit.GoldRule(Content, 0f, Y, Width, 0.55f);
            Y += S(12);
        }

        public void Hint(string text)
        {
            var t = CzKit.Txt(Content, "Hint", text, CzKit.Body(500), S(16), CzKit.Muted, TextAnchor.UpperLeft, 0f, true);
            CzKit.Place(t, 2f, Y, Width - 4f, S(20));
            var h = Mathf.Max(S(22), t.preferredHeight);   // the rect width is set, so this is the wrapped height
            CzKit.Place(t, 2f, Y, Width - 4f, h + 4f);
            Y += h + S(10);
        }

        /// <summary>Lay out <paramref name="count"/> cells in <paramref name="cols"/> columns; <paramref name="make"/> builds cell i at (x, y, w, h).</summary>
        public void Grid(int count, int cols, float cellH, float gap, Action<int, float, float, float, float> make)
        {
            if (count <= 0) return;
            var w = (Width - gap * (cols - 1)) / cols;
            for (var i = 0; i < count; i++)
            {
                var c = i % cols; var r = i / cols;
                make(i, c * (w + gap), Y + r * (cellH + gap), w, cellH);
            }
            var rows = (count + cols - 1) / cols;
            Y += rows * cellH + (rows - 1) * gap + gap;
        }

        /// <summary>Stagger a new widget in (fade + rise), a little later per widget, capped so long lists never wait.</summary>
        public T Reveal<T>(T c) where T : Component
        {
            if (c == null || NoReveal) return c;
            var rv = c.gameObject.AddComponent<CzReveal>();
            rv.Delay = Mathf.Min(_n++ * 0.016f, 0.28f);
            return c;
        }

        public void Finish(float pad = 18f) => Content.sizeDelta = new Vector2(Content.sizeDelta.x, Y + pad * K);
    }

    /// <summary>Eased hover / press feedback: scale, label colour and an optional glow graphic (unscaled time).</summary>
    public sealed class CzHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public float HoverScale = 1.035f, PressScale = 0.965f;
        public Text Label;
        public Color LabelRest = Color.white, LabelHover = Color.white;
        public Graphic Glow;
        public float GlowRest, GlowHover = 1f;
        bool _over, _down;
        float _s = 1f, _k;

        public void OnPointerEnter(PointerEventData e) { _over = true; }
        public void OnPointerExit(PointerEventData e) { _over = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { _down = true; }
        public void OnPointerUp(PointerEventData e) { _down = false; }

        void OnDisable()
        {
            _over = _down = false; _s = 1f; _k = 0f;
            transform.localScale = Vector3.one;
        }

        void Update()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var a = 1f - Mathf.Exp(-dt * 18f);
            _s = Mathf.Lerp(_s, _down ? PressScale : _over ? HoverScale : 1f, a);
            _k = Mathf.Lerp(_k, _over ? 1f : 0f, a);
            transform.localScale = new Vector3(_s, _s, 1f);
            if (Label != null) Label.color = Color.Lerp(LabelRest, LabelHover, _k);
            if (Glow != null) { var c = Glow.color; c.a = Mathf.Lerp(GlowRest, GlowHover, _k); Glow.color = c; }
        }
    }

    /// <summary>Fade + rise in once, after <see cref="Delay"/> (OutCubic, unscaled time). Removes itself when done.</summary>
    public sealed class CzReveal : MonoBehaviour
    {
        public float Delay, Duration = 0.24f, Rise = 12f;
        CanvasGroup _g;
        RectTransform _rt;
        Vector2 _home;
        float _t;
        bool _armed;

        void OnEnable()
        {
            if (_armed) return;
            _armed = true;
            _rt = (RectTransform)transform;
            _home = _rt.anchoredPosition;
            _g = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _g.alpha = 0f;
            _rt.anchoredPosition = _home - new Vector2(0f, Rise);
            _t = -Delay;
        }

        void Update()
        {
            _t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            var k = MenuMotion.OutCubic(Mathf.Clamp01(_t / Duration));
            if (_g != null) _g.alpha = k;
            if (_rt != null) _rt.anchoredPosition = _home - new Vector2(0f, Rise * (1f - k));
            if (_t >= Duration)
            {
                if (_g != null) _g.alpha = 1f;
                if (_rt != null) _rt.anchoredPosition = _home;
                Destroy(this);
            }
        }
    }

    /// <summary>Shows the "more below" lip of a scroll list only while rows lie under its bottom edge.</summary>
    public sealed class CzScrollLip : MonoBehaviour
    {
        public Graphic Lip;
        public ScrollRect Scroll;

        void LateUpdate()
        {
            if (Lip == null || Scroll == null || Scroll.content == null) return;
            var more = Scroll.content.rect.height > Scroll.viewport.rect.height + 2f && Scroll.verticalNormalizedPosition > 0.02f;
            var c = Lip.color;
            var a = Mathf.MoveTowards(c.a, more ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (!Mathf.Approximately(a, c.a)) { c.a = a; Lip.color = c; }
        }
    }

    /// <summary>
    /// A stage of fixed reference size (1920x1080 or 1080x2340 units) fitted inside its parent like CSS
    /// "contain": uniform scale, centred, re-fitted whenever the parent's size changes.
    /// </summary>
    public sealed class CzStageFit : MonoBehaviour
    {
        public Vector2 Reference = new(1920f, 1080f);
        RectTransform _rt;
        Vector2 _last;

        public void FitNow()
        {
            _rt ??= (RectTransform)transform;
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;
            var size = parent.rect.size;
            if (size.x <= 1f || size.y <= 1f) return;
            _last = size;
            var s = Mathf.Min(size.x / Reference.x, size.y / Reference.y);
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
            _rt.sizeDelta = Reference;
            _rt.anchoredPosition = Vector2.zero;
            _rt.localScale = new Vector3(s, s, 1f);
        }

        void OnEnable() => FitNow();

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent != null && parent.rect.size != _last) FitNow();
        }
    }
}
