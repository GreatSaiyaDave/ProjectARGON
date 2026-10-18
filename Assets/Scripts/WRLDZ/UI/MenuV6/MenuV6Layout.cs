using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// One layout plan (BUILD_PLAN §4): the stage-px rects the exporter took from the approved page
    /// (<c>Data/layout_land.json</c>, E5) and the maths that turns them into RectTransform anchors. Stage px have their
    /// origin top-left, y down (layout_land.json "units"). One code path, two plans: portrait reads
    /// <c>layout_port.json</c> once it exists (P1b).
    /// </summary>
    public sealed class MenuV6Layout
    {
        public sealed class Layer
        {
            public string Id;
            public int Z;
            public string Group;
            public string File;
            public string Local;
            public Rect Rect;          // stage px, top-left origin
            public float Ppu = 100f;
            public string Blend;       // opaque | alpha | additive
            public float AlphaAtRest = 1f;
            public Vector4 Border;     // Unity spriteBorder order (left, bottom, right, top), sprite px
            public bool Nine;
            public string Chrome;      // chrome9 kind, when it is a 9-slice instance
            public int Flipbook = -1;  // eye seed, when it is a flipbook
            public string Recess;
            public List<string> Variants;
            public bool Media;
        }

        public sealed class Flipbook
        {
            public int Seed;
            public string File;
            public Rect Rect;
            public Vector2 Cell;
            public readonly List<Rect> Frames = new(); // sheet px, top-left origin
        }

        public sealed class Recess
        {
            public string Id;
            public string Label;
            public string Sub;
            public bool Dead;
            public bool Minor;
            public Vector2 Centre;
            public float R;
            public Rect LabelRect;
            public Rect ButtonRect;
            public bool HasButton;
        }

        public sealed class TextSlot
        {
            public string Sel;
            public string Text;
            public Rect Rect;
            public float Opacity = 1f;
            public string Family;
            public int Weight = 400;
            public float Size;
            public bool Italic;
            public Color Color = Color.white;
            public float LetterSpacing;
            public bool Upper;
            public string Align;
            public float LineHeight;   // 0 = normal
            public bool Shadow;
            public Color ShadowColor;
            public Vector2 ShadowOffset;   // CSS px, y down
            public float ShadowBlur;
            public bool NoWrap;
            public string BgImage;         // the DUEL band gradient
            public string Filter;          // drop-shadow(...)
        }

        public string Page;
        public float Version;
        public float T;
        public Vector2 Stage = new(1920f, 1080f);
        public readonly List<Layer> Layers = new();
        public readonly Dictionary<int, Flipbook> Flipbooks = new();
        public readonly List<Recess> Recesses = new();
        public readonly List<TextSlot> Texts = new();
        public Rect DoorButton, DoorText, DoorLp, DoorDeck;
        public Vector2 DoorCentre;
        public Rect Niche, NicheMap, Quit, Placeholder;
        public bool HasDoor, HasNiche, HasQuit;
        public Vector2 Keystone;

        // ------------------------------------------------------------------ load

        public static string FileFor(MenuLayoutKind kind) =>
            kind == MenuLayoutKind.Landscape ? "layout_land" : "layout_port";

        /// <summary>The plan for <paramref name="kind"/>, or null when its JSON is missing or unreadable.</summary>
        public static MenuV6Layout Load(MenuLayoutKind kind)
        {
            var ta = MenuV6Art.Data(FileFor(kind));
            if (ta == null || string.IsNullOrEmpty(ta.text)) return null;
            try
            {
                return Parse(ta.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + FileFor(kind) + ".json unreadable: " + ex.Message);
                return null;
            }
        }

        public static MenuV6Layout Parse(string json)
        {
            var root = MenuV6Json.Parse(json) as Dictionary<string, object>
                       ?? throw new FormatException("root is not an object");
            var L = new MenuV6Layout
            {
                Page = Str(root, "page"),
                Version = Num(root, "version"),
                T = Num(root, "t")
            };
            if (Get(root, "stage") is Dictionary<string, object> st)
                L.Stage = new Vector2(Num(st, "W", 1920f), Num(st, "H", 1080f));

            if (Get(root, "layers") is List<object> layers)
                foreach (var o in layers)
                {
                    if (o is not Dictionary<string, object> d) continue;
                    var layer = new Layer
                    {
                        Id = Str(d, "id"),
                        Z = (int)Num(d, "z"),
                        Group = Str(d, "group"),
                        File = Str(d, "file"),
                        Local = Str(d, "local"),
                        Rect = RectOf(Get(d, "rect")),
                        Ppu = Num(d, "ppu", 100f),
                        Blend = Str(d, "blend") ?? "alpha",
                        AlphaAtRest = Num(d, "alphaAtRest", 1f),
                        Nine = Get(d, "nine") is bool nb && nb,
                        Chrome = Str(d, "chrome"),
                        Recess = Str(d, "recess"),
                        Media = Get(d, "media") is bool mb && mb
                    };
                    if (Get(d, "border") is List<object> b && b.Count == 4)
                        layer.Border = new Vector4(F(b[0]), F(b[1]), F(b[2]), F(b[3]));
                    if (Get(d, "flipbook") is double fbk) layer.Flipbook = (int)fbk;
                    if (Get(d, "variants") is List<object> vs)
                    {
                        layer.Variants = new List<string>();
                        foreach (var v in vs)
                            if (v is string s)
                                layer.Variants.Add(s);
                    }

                    L.Layers.Add(layer);
                }

            L.Layers.Sort((a, b) => a.Z.CompareTo(b.Z));

            if (Get(root, "flipbooks") is Dictionary<string, object> fbs)
                foreach (var kv in fbs)
                {
                    if (kv.Value is not Dictionary<string, object> d) continue;
                    if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) continue;
                    var cell = Get(d, "cell") as List<object>;
                    var fb = new Flipbook
                    {
                        Seed = seed,
                        File = Str(d, "file"),
                        Rect = RectOf(Get(d, "rect")),
                        Cell = cell != null && cell.Count == 2 ? new Vector2(F(cell[0]), F(cell[1])) : Vector2.zero
                    };
                    if (Get(d, "frames") is List<object> frames)
                        foreach (var fo in frames)
                            if (fo is Dictionary<string, object> fd)
                                fb.Frames.Add(new Rect(Num(fd, "x"), Num(fd, "y"), fb.Cell.x, fb.Cell.y));
                    L.Flipbooks[seed] = fb;
                }

            if (Get(root, "interactive") is Dictionary<string, object> ia)
            {
                if (Get(ia, "recesses") is List<object> rs)
                    foreach (var o in rs)
                    {
                        if (o is not Dictionary<string, object> d) continue;
                        var c = Get(d, "centre") as List<object>;
                        var rec = new Recess
                        {
                            Id = Str(d, "id"),
                            Label = Str(d, "label"),
                            Sub = Str(d, "sub"),
                            Dead = Get(d, "dead") is bool db && db,
                            Minor = Get(d, "minor") is bool mb && mb,
                            Centre = c != null && c.Count == 2 ? new Vector2(F(c[0]), F(c[1])) : Vector2.zero,
                            R = Num(d, "r"),
                            LabelRect = RectOf(Get(d, "labelRect")),
                            ButtonRect = RectOf(Get(d, "buttonRect")),
                            HasButton = Get(d, "buttonRect") is List<object>
                        };
                        L.Recesses.Add(rec);
                    }

                if (Get(ia, "door") is Dictionary<string, object> door)
                {
                    L.HasDoor = Get(door, "buttonRect") is List<object>;
                    L.DoorButton = RectOf(Get(door, "buttonRect"));
                    L.DoorText = RectOf(Get(door, "textRect"));
                    L.DoorLp = RectOf(Get(door, "lpRect"));
                    L.DoorDeck = RectOf(Get(door, "deckRect"));
                    if (Get(door, "centre") is List<object> dc && dc.Count == 2)
                        L.DoorCentre = new Vector2(F(dc[0]), F(dc[1]));
                }

                if (Get(ia, "keystone") is Dictionary<string, object> ks && Get(ks, "centre") is List<object> kc &&
                    kc.Count == 2)
                    L.Keystone = new Vector2(F(kc[0]), F(kc[1]));

                if (Get(ia, "niche") is Dictionary<string, object> ni)
                {
                    L.HasNiche = Get(ni, "wmapRect") is List<object>;
                    L.Niche = RectOf(Get(ni, "rect"));
                    L.NicheMap = RectOf(Get(ni, "wmapRect"));
                }

                if (Get(ia, "quit") is Dictionary<string, object> q)
                {
                    L.HasQuit = Get(q, "rect") is List<object>;
                    L.Quit = RectOf(Get(q, "rect"));
                }

                if (Get(ia, "placeholder") is Dictionary<string, object> ph)
                    L.Placeholder = RectOf(Get(ph, "rect"));
            }

            if (Get(root, "texts") is List<object> texts)
                foreach (var o in texts)
                {
                    if (o is not Dictionary<string, object> d) continue;
                    var slot = new TextSlot
                    {
                        Sel = Str(d, "sel"),
                        Text = Str(d, "text"),
                        Rect = RectOf(Get(d, "rect")),
                        Opacity = Num(d, "opacity", 1f)
                    };
                    if (Get(d, "font") is Dictionary<string, object> f)
                        ReadFont(slot, f);
                    L.Texts.Add(slot);
                }

            return L;
        }

        static void ReadFont(TextSlot s, Dictionary<string, object> f)
        {
            var fam = Str(f, "family") ?? "";
            var comma = fam.IndexOf(',');
            if (comma >= 0) fam = fam.Substring(0, comma);
            s.Family = fam.Trim().Trim('"', '\'').Trim();
            int.TryParse(Str(f, "weight") ?? "400", NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Weight);
            s.Size = Px(Str(f, "size"));
            s.Italic = Str(f, "style") == "italic";
            s.Color = Css.ColorOf(Str(f, "color"), Color.white);
            s.LetterSpacing = Px(Str(f, "letterSpacing"));
            s.Upper = Str(f, "transform") == "uppercase";
            s.Align = Str(f, "align") ?? "start";
            s.LineHeight = Px(Str(f, "lineHeight"));
            s.NoWrap = Str(f, "whiteSpace") == "nowrap";
            s.BgImage = Str(f, "bgImage");
            s.Filter = Str(f, "filter");
            var sh = Str(f, "shadow");
            if (!string.IsNullOrEmpty(sh) && sh != "none" && Css.Shadow(sh, out var sc, out var off, out var blur))
            {
                s.Shadow = true;
                s.ShadowColor = sc;
                s.ShadowOffset = off;
                s.ShadowBlur = blur;
            }
        }

        /// <summary>The mockup text slot for (selector, mockup string), or null. The mockup string is a lookup key only:
        /// what Unity shows is real data (BUILD_PLAN §7) or the approved copy.</summary>
        public TextSlot Text(string sel, string mockupText)
        {
            for (var i = 0; i < Texts.Count; i++)
                if (Texts[i].Sel == sel && Texts[i].Text == mockupText)
                    return Texts[i];
            return null;
        }

        public Layer LayerById(string id)
        {
            for (var i = 0; i < Layers.Count; i++)
                if (Layers[i].Id == id)
                    return Layers[i];
            return null;
        }

        public Recess RecessById(string id)
        {
            for (var i = 0; i < Recesses.Count; i++)
                if (Recesses[i].Id == id)
                    return Recesses[i];
            return null;
        }

        // ------------------------------------------------------------------ anchors

        /// <summary>
        /// Place <paramref name="rt"/> at stage rect <paramref name="r"/> inside a parent that covers stage rect
        /// <paramref name="parent"/> (anchors only, zero offsets, so the element scales with its parent).
        /// </summary>
        public static void Place(RectTransform rt, Rect r, Rect parent)
        {
            if (rt == null || parent.width <= 0f || parent.height <= 0f) return;
            var x0 = (r.x - parent.x) / parent.width;
            var x1 = (r.xMax - parent.x) / parent.width;
            var y1 = 1f - (r.y - parent.y) / parent.height;     // stage y is down, anchors are up
            var y0 = 1f - (r.yMax - parent.y) / parent.height;
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        public Rect StageRect => new(0f, 0f, Stage.x, Stage.y);

        // ------------------------------------------------------------------ json helpers

        static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        static string Str(Dictionary<string, object> d, string k) => Get(d, k) as string;

        static float Num(Dictionary<string, object> d, string k, float def = 0f) =>
            Get(d, k) is double v ? (float)v : def;

        static float F(object o) => o is double v ? (float)v : 0f;

        static Rect RectOf(object o)
        {
            if (o is not List<object> a || a.Count < 4) return Rect.zero;
            return new Rect(F(a[0]), F(a[1]), F(a[2]), F(a[3]));
        }

        static float Px(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "normal" || s == "none") return 0f;
            s = s.Trim();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        /// <summary>The CSS values the exporter recorded (computed style strings).</summary>
        public static class Css
        {
            /// <summary>"rgb(r, g, b)" / "rgba(r, g, b, a)" → sRGB Color (UGUI converts under Linear space).</summary>
            public static Color ColorOf(string css, Color fallback)
            {
                if (string.IsNullOrEmpty(css)) return fallback;
                var open = css.IndexOf('(');
                var close = css.LastIndexOf(')');
                if (open < 0 || close <= open) return fallback;
                var parts = css.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3) return fallback;
                float P(int i) => float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                var a = parts.Length >= 4 ? P(3) : 1f;
                return new UnityEngine.Color(P(0) / 255f, P(1) / 255f, P(2) / 255f, a);
            }

            /// <summary>"rgba(0, 0, 0, 0.85) 0px 2px 0px" → colour, offset (CSS px, y down), blur.</summary>
            public static bool Shadow(string css, out Color color, out Vector2 offset, out float blur)
            {
                color = new UnityEngine.Color(0f, 0f, 0f, 0f);
                offset = Vector2.zero;
                blur = 0f;
                if (string.IsNullOrEmpty(css)) return false;
                var close = css.IndexOf(')');
                if (close < 0) return false;
                color = ColorOf(css.Substring(0, close + 1), color);
                var rest = css.Substring(close + 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (rest.Length < 2) return false;
                offset = new Vector2(Px(rest[0]), Px(rest[1]));
                blur = rest.Length >= 3 ? Px(rest[2]) : 0f;
                return color.a > 0f;
            }

            /// <summary>The colour stops of a hard-band linear-gradient (DUEL): each "rgb(...) a%" pair.</summary>
            public static List<(Color c, float from, float to)> Bands(string gradient)
            {
                var list = new List<(Color, float, float)>();
                if (string.IsNullOrEmpty(gradient)) return list;
                var i = 0;
                var stops = new List<(Color c, float at)>();
                while (true)
                {
                    var k = gradient.IndexOf("rgb", i, StringComparison.Ordinal);
                    if (k < 0) break;
                    var e = gradient.IndexOf(')', k);
                    if (e < 0) break;
                    var col = ColorOf(gradient.Substring(k, e - k + 1), UnityEngine.Color.white);
                    var j = e + 1;
                    while (j < gradient.Length && gradient[j] == ' ') j++;
                    var sb = new StringBuilder();
                    while (j < gradient.Length && (char.IsDigit(gradient[j]) || gradient[j] == '.')) sb.Append(gradient[j++]);
                    var unit = j < gradient.Length ? gradient[j] : ' ';
                    float.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var at);
                    if (unit == '%') at /= 100f;
                    else at = 0f;   // "0px"
                    stops.Add((col, at));
                    i = e + 1;
                }

                // pairs of equal colours = one hard band
                for (var s = 0; s + 1 < stops.Count; s += 2)
                    list.Add((stops[s].c, stops[s].at, stops[s + 1].at));
                return list;
            }
        }
    }

    /// <summary>A small JSON reader for the exporter's data files (JsonUtility cannot read nested arrays or maps).
    /// Objects → Dictionary&lt;string, object&gt;, arrays → List&lt;object&gt;, numbers → double.</summary>
    public static class MenuV6Json
    {
        public static object Parse(string s)
        {
            var i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("trailing characters at " + i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            var c = s[i];
            switch (c)
            {
                case '{':
                {
                    i++;
                    var d = new Dictionary<string, object>();
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i);
                        var k = Str(s, ref i);
                        Ws(s, ref i);
                        if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i);
                        i++;
                        d[k] = Value(s, ref i);
                        Ws(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == '}') { i++; return d; }
                        throw new FormatException("',' or '}' expected at " + i);
                    }
                }
                case '[':
                {
                    i++;
                    var a = new List<object>();
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    while (true)
                    {
                        a.Add(Value(s, ref i));
                        Ws(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == ']') { i++; return a; }
                        throw new FormatException("',' or ']' expected at " + i);
                    }
                }
                case '"':
                    return Str(s, ref i);
                case 't':
                    Lit(s, ref i, "true");
                    return true;
                case 'f':
                    Lit(s, ref i, "false");
                    return false;
                case 'n':
                    Lit(s, ref i, "null");
                    return null;
                default:
                {
                    var st = i;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                    if (st == i) throw new FormatException("unexpected '" + c + "' at " + i);
                    return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
            }
        }

        static void Lit(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0) throw new FormatException(lit + " expected at " + i);
            i += lit.Length;
        }

        static string Str(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                var e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }

            throw new FormatException("unterminated string");
        }
    }
}
