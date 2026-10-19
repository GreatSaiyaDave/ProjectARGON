using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The P3 sheet exports (BUILD_PLAN §6 P3): <c>Data/sheets_&lt;layout&gt;.json</c> (every part, rect, text spec and the page's
    /// rules), <c>sheets_trace_&lt;layout&gt;.json</c> (the page's per-frame Ring bearings) and <c>cracks_sheets_&lt;layout&gt;.json</c>.
    /// Read-only; every miss is null and every caller skips what is missing.
    /// </summary>
    public static class SheetData
    {
        public static string Lay(MenuLayoutKind k) => k == MenuLayoutKind.Landscape ? "land" : "port";

        public static Dictionary<string, object> Root(MenuLayoutKind k) => CrackData.Json("sheets_" + Lay(k));

        public static Dictionary<string, object> Trace(MenuLayoutKind k) => CrackData.Json("sheets_trace_" + Lay(k));

        public static bool Has(MenuLayoutKind k) => Root(k) != null;

        public static Vector2 Stage(Dictionary<string, object> root, MenuLayoutKind k)
        {
            var st = RJ.D(root, "stage");
            return st != null
                ? new Vector2((float)RJ.N(st, "W", 1920), (float)RJ.N(st, "H", 1080))
                : k == MenuLayoutKind.Landscape ? new Vector2(1920, 1080) : new Vector2(1080, 2340);
        }

        /// <summary>A frame rect {x, y, w, h} (stage px).</summary>
        public static Rect Frame(object f) => new((float)RJ.N(f, "x"), (float)RJ.N(f, "y"), (float)RJ.N(f, "w"), (float)RJ.N(f, "h"));

        /// <summary>A part's sprite: its local-only file (MenuV6Art.LocalOr) else its tracked one; null = none.</summary>
        public static Sprite Sprite(object part)
        {
            if (part == null) return null;
            var local = RJ.S(part, "local");
            var tracked = RJ.S(part, "sprite");
            return !string.IsNullOrEmpty(local) ? MenuV6Art.LocalOr(local, tracked) : MenuV6Art.Sprite(tracked);
        }

        public static bool IsLocal(object part)
        {
            var local = RJ.S(part, "local");
            return !string.IsNullOrEmpty(local) && MenuV6Art.HasLocal(local);
        }

        static float Px(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "normal" || s == "none") return 0f;
            s = s.Trim();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        /// <summary>A recorded DOM text spec (texts.*: font, size, weight, tracking, padLeft, colour, shadow, lines) as the
        /// TextFx slot the P1 / P2 builders use. CSS centres a tracked line with its padding-left equal to the tracking, so the
        /// slot's box starts after that padding (TextFx aligns the run with its trailing tracking, as CSS does).</summary>
        public static MenuV6Layout.TextSlot Slot(object spec)
        {
            if (spec == null) return null;
            var r = RJ.R(spec, "rect");
            var pad = Px(RJ.S(spec, "padLeft"));
            if (pad > 0f && pad < r.width) r = new Rect(r.x + pad, r.y, r.width - pad, r.height);
            var lines = RJ.L(spec, "lines");
            float.TryParse(RJ.S(spec, "opacity") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var op);
            int.TryParse(RJ.S(spec, "weight") ?? "400", NumberStyles.Integer, CultureInfo.InvariantCulture, out var w);
            var s = new MenuV6Layout.TextSlot
            {
                Sel = RJ.S(spec, "cls"),
                Text = RJ.S(spec, "text"),
                Rect = r,
                Opacity = op <= 0 ? 1f : op,
                Family = RJ.S(spec, "font"),
                Weight = w <= 0 ? 400 : w,
                Size = Px(RJ.S(spec, "size")),
                Color = MenuV6Layout.Css.ColorOf(RJ.S(spec, "color"), Color.white),
                LetterSpacing = Px(RJ.S(spec, "tracking")),
                Upper = RJ.S(spec, "transform") == "uppercase",
                Align = RJ.S(spec, "align") ?? "start",
                LineHeight = Px(RJ.S(spec, "lineHeight")),
                NoWrap = lines == null || lines.Count <= 1
            };
            var sh = RJ.S(spec, "shadow");
            if (!string.IsNullOrEmpty(sh) && sh != "none" && MenuV6Layout.Css.Shadow(sh, out var sc, out var off, out var blur))
            {
                s.Shadow = true;
                s.ShadowColor = sc;
                s.ShadowOffset = off;
                s.ShadowBlur = blur;
            }

            return s;
        }

        /// <summary>The first spec of a list with this class (cls) and, when given, this mockup text.</summary>
        public static Dictionary<string, object> Spec(List<object> list, string cls, string text = null, int nth = 0)
        {
            if (list == null) return null;
            foreach (var o in list)
            {
                if (o is not Dictionary<string, object> d) continue;
                if (cls != null && RJ.S(d, "cls") != cls) continue;
                if (text != null && RJ.S(d, "text") != text) continue;
                if (nth-- > 0) continue;
                return d;
            }

            return null;
        }
    }

    /// <summary>Small builders for the sheets (stage-px placement, parts, labels, the disc stencil).</summary>
    public static class SheetDraw
    {
        public static RectTransform Node(Transform parent, string name, Rect r, Rect parentRect)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            MenuV6Layout.Place(n, r, parentRect);
            return n;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            UiTheme.Stretch(n);
            return n;
        }

        /// <summary>An Image of <paramref name="sp"/> at stage rect <paramref name="r"/>; null when there is no sprite (nothing
        /// is drawn, never a white box).</summary>
        public static Image Img(Transform parent, string name, Sprite sp, Rect r, Rect parentRect, bool sliced = false)
        {
            if (sp == null || parent == null) return null;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;
            img.type = sliced && sp.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            MenuV6Layout.Place(img.rectTransform, r, parentRect);
            return img;
        }

        /// <summary>A part at its own recorded rect (sheets_&lt;layout&gt;.json parts: sprite / local, rect).</summary>
        public static Image Part(Transform parent, string name, object part, Rect parentRect, bool sliced = false)
        {
            if (part == null) return null;
            return Img(parent, name, SheetData.Sprite(part), RJ.R(part, "rect"), parentRect, sliced);
        }

        /// <summary>A part at another rect (9-slices, units drawn several times).</summary>
        public static Image PartAt(Transform parent, string name, object part, Rect r, Rect parentRect, bool sliced = false) =>
            part == null ? null : Img(parent, name, SheetData.Sprite(part), r, parentRect, sliced);

        /// <summary>A Text from a recorded spec, showing <paramref name="shown"/> (real data or the approved copy).</summary>
        public static Text Label(Transform parent, string name, object spec, string shown, Rect parentRect, int minSize = 0)
        {
            var slot = SheetData.Slot(spec);
            return slot == null ? null : TextFx.Label(parent, name, slot, shown, parentRect, minSize);
        }

        public static void SetA(Graphic g, double a)
        {
            if (g == null) return;
            var c = g.color;
            var na = Mathf.Clamp01((float)a);
            if (Mathf.Abs(c.a - na) < 1e-4f) return;
            c.a = na;
            g.color = c;
        }

        public static void SetA(CanvasGroup g, double a)
        {
            if (g == null) return;
            var na = Mathf.Clamp01((float)a);
            if (Mathf.Abs(g.alpha - na) > 1e-4f) g.alpha = na;
        }

        public static CanvasGroup Group(GameObject go)
        {
            if (go == null) return null;
            if (!go.TryGetComponent(out CanvasGroup g)) g = go.AddComponent<CanvasGroup>();
            return g;
        }

        static Sprite _disc;

        /// <summary>A filled disc (round stencils: the door, the map window); generated once, never saved with a scene.</summary>
        public static Sprite Disc()
        {
            if (_disc != null) return _disc;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
                name = "SheetDisc"
            };
            var px = new Color32[n * n];
            var c = (n - 1) * .5f;
            var r = n * .5f - .5f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(r - d + .5f) * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            _disc = UnityEngine.Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
            _disc.hideFlags = HideFlags.HideAndDontSave;
            _disc.name = "SheetDisc";
            return _disc;
        }

        /// <summary>A node in the Stone's own stage frame (its size and its fit), under <paramref name="host"/>: sheets over the
        /// Stone are built in stage px there and follow the Stone's fit.</summary>
        public static RectTransform StageSpace(Transform host, MenuV6Stage stone, string name)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(host, false);
            n.anchorMin = n.anchorMax = new Vector2(.5f, .5f);
            n.pivot = new Vector2(.5f, .5f);
            var size = stone != null && stone.Layout != null ? stone.Layout.Stage : MenuAspect.LandStage;
            n.sizeDelta = size;
            n.anchoredPosition = Vector2.zero;
            if (stone != null && stone.Stage != null)
            {
                n.localScale = stone.Stage.localScale;
                n.gameObject.AddComponent<StageFollow>().Source = stone.Stage;
            }

            return n;
        }

        /// <summary>Canvas units per mockup stage px for anything under a canvas (the plan's MenuAspect.StageScale, P3: the
        /// overlay canvases keep their own scaler until P4, so the frame sizes itself by it).</summary>
        public static float CanvasK(Transform t)
        {
            if (t == null) return 1f;
            var cv = t.GetComponentInParent<Canvas>();
            var root = cv != null ? cv.rootCanvas : null;
            var rt = root != null ? root.transform as RectTransform : null;
            if (rt == null) return 1f;
            var r = rt.rect;
            if (r.width <= 1f || r.height <= 1f) return 1f;
            var stage = r.width >= r.height ? MenuAspect.LandStage : new Vector2(1080f, 2340f);
            return Mathf.Max(.05f, Mathf.Min(r.width / stage.x, r.height / stage.y));
        }

        /// <summary>A 9-slice of ppu 200 (2 texels per stage px) drawn at canvas scale k.</summary>
        public static void SliceAt(Image img, float k)
        {
            if (img == null) return;
            img.pixelsPerUnitMultiplier = k > 1e-3f ? 1f / k : 1f;
        }
    }

    /// <summary>Keeps a stage-space node at its source's fit (the Stone's Stage scale).</summary>
    [DisallowMultipleComponent]
    public sealed class StageFollow : MonoBehaviour
    {
        public RectTransform Source;

        void LateUpdate()
        {
            if (Source == null) return;
            var rt = (RectTransform)transform;
            if (rt.localScale != Source.localScale) rt.localScale = Source.localScale;
        }
    }

    /// <summary>The sheet fractures' timing (cracks_sheets_&lt;layout&gt;.json "timing", anime_v61_cracks.js:720-727): grown in
    /// sheet time .9-2.0 to their rest length, creeping 15 % more over ~30 s, breathing ±3 %; full alpha, no mist, no bolts;
    /// the sheet's clip rolls them up with it.</summary>
    public sealed class SheetCrackDriver : ICrackDriver
    {
        readonly List<CrackNet> _nets;
        public Func<double> SheetTime;   // u_s (sheet time since the open)

        public SheetCrackDriver(IList<CrackNet> nets) => _nets = new List<CrackNet>(nets);

        public bool Frame(double t, List<CrackItem> items, out double tc, out double waver, out int dir)
        {
            tc = t;
            waver = 0;
            dir = 1;
            var us = SheetTime != null ? SheetTime() : 3;
            if (us < .9) return false;
            foreach (var n in _nets)
            {
                var e = SheetMath.CrackExtent(n.L, n.Sp != null ? n.Sp.Rest : .7, us);
                items.Add(new CrackItem { N = n, E = e, A = 1, Open = 1 + .03 * Math.Sin(tc * Math.PI * 2 / 5.3 + n.Seed), Mist = 0 });
            }

            return true;
        }

        public double MistA => 0;
        public CrackBeat[] Bolts => null;
        public double BoltA => 0;

        /// <summary>The nets of one sheet key (sheets{} in the JSON: "duel", "recessdeck", "ringseek", "quit", "host_compact" …).</summary>
        public static List<CrackNet> Of(CrackSet set, string key)
        {
            var list = new List<CrackNet>();
            if (set == null || string.IsNullOrEmpty(key)) return list;
            var p = key + ":";
            foreach (var n in set.Nets)
                if (n != null && n.Id != null && n.Id.StartsWith(p, StringComparison.Ordinal))
                    list.Add(n);
            return list;
        }

        /// <summary>The F rect the page built a key's nets for (sheets{key}.F).</summary>
        public static Rect FrameOf(string key, MenuLayoutKind kind)
        {
            var root = CrackData.Json("cracks_sheets_" + SheetData.Lay(kind));
            var s = RJ.D(RJ.D(root, "sheets"), key);
            return s != null ? SheetData.Frame(RJ.D(s, "F")) : Rect.zero;
        }
    }
}
