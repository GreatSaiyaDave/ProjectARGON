using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The P4 deck exports (BUILD_PLAN §6 P4): <c>Data/deck_&lt;layout&gt;.json</c> (the hall, the Scale of Ma'at rig, the Puzzle
    /// clasp, every DOM chrome shot with its 9-slice border, the recorded text specs, the geometry of the 10 × 4 altar and the
    /// House of Life grid, the box, DRAW 5, the timeline formulas and the decisions). Read-only; every miss is null and every
    /// caller skips what is missing, so the hall never draws a white box.
    /// </summary>
    public sealed class DeckHallSpec
    {
        public MenuLayoutKind Kind;
        public Dictionary<string, object> Root;
        public Vector2 Stage = new(1920f, 1080f);

        public bool Land => Kind == MenuLayoutKind.Landscape;
        public Rect StageRect => new(0f, 0f, Stage.x, Stage.y);
        public string Lay => Land ? "land" : "port";

        /// <summary>Tests: build the legacy deck builder as if the hall data were missing.</summary>
        public static bool ForceLegacy;

        public static string FileFor(MenuLayoutKind kind) => kind == MenuLayoutKind.Landscape ? "deck_land" : "deck_port";

        public static DeckHallSpec Load(MenuLayoutKind kind)
        {
            Dictionary<string, object> root;
            try { root = CrackData.Json(FileFor(kind)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + FileFor(kind) + ".json unreadable: " + ex.Message);
                return null;
            }

            if (root == null) return null;
            var s = new DeckHallSpec { Kind = kind, Root = root };
            var st = RJ.D(root, "stage");
            if (st != null) s.Stage = new Vector2((float)RJ.N(st, "W", 1920), (float)RJ.N(st, "H", 1080));
            else if (kind == MenuLayoutKind.Portrait) s.Stage = new Vector2(1080f, 2340f);
            return s;
        }

        /// <summary>Null when the hall can be built for <paramref name="kind"/>; else why not (the legacy builder is used).</summary>
        public static string WhyNot(MenuLayoutKind kind)
        {
            if (ForceLegacy) return "legacy forced (test)";
            if (MenuV6Stage.KillSwitchOn) return "kill switch " + MenuV6Stage.KillSwitchPref + " = 1";
            var s = Load(kind);
            if (s == null) return "deck hall data missing (Resources/" + MenuV6Art.DataRoot + FileFor(kind) + ")";
            if (s.Sprite(RJ.D(s.D("hall"), "room")) == null) return "deck hall art missing (hall/room)";
            return null;
        }

        public Dictionary<string, object> D(string k) => RJ.D(Root, k);
        public Dictionary<string, object> Geo => D("geo");
        public Dictionary<string, object> GeoD => RJ.D(Geo, "D");

        /// <summary>A named element rect of the page (geo.els), stage px; Rect.zero when the plan has none.</summary>
        public Rect El(string name) => RJ.R(RJ.D(Geo, "els"), name);

        public List<object> ElList(string name) => RJ.L(RJ.D(Geo, "els"), name);

        public List<Rect> Rects(Dictionary<string, object> o, string key)
        {
            var list = new List<Rect>();
            var a = RJ.L(o, key);
            if (a == null) return list;
            foreach (var r in a)
                if (r is List<object> l && l.Count >= 4)
                    list.Add(new Rect((float)RJ.At(l, 0), (float)RJ.At(l, 1), (float)RJ.At(l, 2), (float)RJ.At(l, 3)));
            return list;
        }

        public static Rect RectOf(object o)
        {
            if (o is List<object> l && l.Count >= 4)
                return new Rect((float)RJ.At(l, 0), (float)RJ.At(l, 1), (float)RJ.At(l, 2), (float)RJ.At(l, 3));
            return Rect.zero;
        }

        /// <summary>A part's sprite: "local" (MenuV6Art.LocalOr) with its "fallback", else "file".</summary>
        public Sprite Sprite(object part)
        {
            if (part == null) return null;
            var local = RJ.S(part, "local");
            if (!string.IsNullOrEmpty(local)) return MenuV6Art.LocalOr(local, RJ.S(part, "fallback"));
            return MenuV6Art.Sprite(RJ.S(part, "file"));
        }

        // ------------------------------------------------------------------ chrome (the DOM shots)

        public Dictionary<string, object> Chrome(string key) => RJ.D(D("chrome"), key);

        public Sprite ChromeSprite(string key) => Sprite(Chrome(key));

        /// <summary>The stage rect a chrome shot covers when its element sits at <paramref name="el"/>: the element rect grown by
        /// the shot's own margins (shadows and glows reach past the element).</summary>
        public Rect ShotFor(string key, Rect el)
        {
            var c = Chrome(key);
            if (c == null) return el;
            var e = RJ.R(c, "elRect");
            var s = RJ.R(c, "shotRect");
            if (e.width <= 0f || e.height <= 0f) return s.width > 0f ? new Rect(el.x + (s.x - e.x), el.y + (s.y - e.y), s.width, s.height) : el;
            float l = e.x - s.x, t = e.y - s.y, r = s.xMax - e.xMax, b = s.yMax - e.yMax;
            return new Rect(el.x - l, el.y - t, el.width + l + r, el.height + t + b);
        }

        /// <summary>The element rect the page recorded for a chrome shot.</summary>
        public Rect ChromeEl(string key) => RJ.R(Chrome(key), "elRect");

        public bool Sliced(string key)
        {
            var m = RJ.S(Chrome(key), "mode");
            return m == "Sliced" || m == "Tiled";
        }

        /// <summary>A chrome shot as an Image for an element at <paramref name="el"/> (stage px) under a parent covering
        /// <paramref name="parentRect"/>; null when the sprite is missing.</summary>
        public Image ChromeImg(Transform parent, string name, string key, Rect el, Rect parentRect)
        {
            var sp = ChromeSprite(key);
            if (sp == null || parent == null) return null;
            var img = SheetDraw.Img(parent, name, sp, ShotFor(key, el), parentRect, Sliced(key));
            return img;
        }

        // ------------------------------------------------------------------ text

        /// <summary>The recorded texts of a phase (text.rest / typed / inspect / legal / saved).</summary>
        public List<object> Texts(string phase = "rest") => RJ.L(RJ.D(D("text"), phase), "texts");

        /// <summary>The first text spec whose selector contains every fragment of <paramref name="selParts"/> (and, when given,
        /// whose own text starts with <paramref name="own"/>).</summary>
        public Dictionary<string, object> TextSpec(string phase, string own, params string[] selParts)
        {
            var list = Texts(phase);
            if (list == null) return null;
            foreach (var o in list)
            {
                if (o is not Dictionary<string, object> d) continue;
                var sel = RJ.S(d, "sel") ?? "";
                var ok = true;
                foreach (var p in selParts)
                    if (sel.IndexOf(p, StringComparison.Ordinal) < 0)
                    {
                        ok = false;
                        break;
                    }

                if (!ok) continue;
                if (own != null && !(RJ.S(d, "own") ?? "").StartsWith(own, StringComparison.Ordinal)) continue;
                return d;
            }

            return null;
        }

        public MenuV6Layout.TextSlot Text(string phase, string own, params string[] selParts) => Slot(TextSpec(phase, own, selParts));

        static float Px(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "normal" || s == "none") return 0f;
            s = s.Trim();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        /// <summary>A recorded deck text spec (font {family, size, weight, lineHeight}, letterSpacing, color, textShadow,
        /// opacity, textAlign, textTransform, whiteSpace, rect, lines) as the TextFx slot of the P1-P3 builders.</summary>
        public static MenuV6Layout.TextSlot Slot(object spec)
        {
            if (spec is not Dictionary<string, object> d) return null;
            var font = RJ.D(d, "font");
            var fam = (RJ.S(font, "family") ?? "").Trim().Trim('"');
            var comma = fam.IndexOf(',');
            if (comma > 0) fam = fam.Substring(0, comma).Trim().Trim('"');
            int.TryParse(RJ.S(font, "weight") ?? "400", NumberStyles.Integer, CultureInfo.InvariantCulture, out var w);
            float.TryParse(RJ.S(d, "opacity") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var op);
            var lines = RJ.L(d, "lines");
            var ws = RJ.S(d, "whiteSpace") ?? "normal";
            var s = new MenuV6Layout.TextSlot
            {
                Sel = RJ.S(d, "cls"),
                Text = RJ.S(d, "text"),
                Rect = RJ.R(d, "rect"),
                Opacity = op <= 0 ? 1f : op,
                Family = fam,
                Weight = w <= 0 ? 400 : w,
                Size = Px(RJ.S(font, "size")),
                Italic = RJ.S(font, "style") == "italic",
                Color = ColorOf(RJ.S(d, "color"), Color.white),
                LetterSpacing = Px(RJ.S(d, "letterSpacing")),
                Upper = RJ.S(d, "textTransform") == "uppercase",
                Align = RJ.S(d, "textAlign") ?? "start",
                LineHeight = Px(RJ.S(font, "lineHeight")),
                NoWrap = ws == "nowrap" || lines == null || lines.Count <= 1
            };
            var sh = RJ.S(d, "textShadow");
            if (!string.IsNullOrEmpty(sh) && sh != "none" && MenuV6Layout.Css.Shadow(sh, out var sc, out var off, out var blur))
            {
                s.Shadow = true;
                s.ShadowColor = sc;
                s.ShadowOffset = off;
                s.ShadowBlur = blur;
            }

            return s;
        }

        /// <summary>A copy of <paramref name="s"/> at another rect.</summary>
        public static MenuV6Layout.TextSlot At(MenuV6Layout.TextSlot s, Rect r)
        {
            if (s == null) return null;
            return new MenuV6Layout.TextSlot
            {
                Sel = s.Sel, Text = s.Text, Rect = r, Opacity = s.Opacity, Family = s.Family, Weight = s.Weight, Size = s.Size,
                Italic = s.Italic, Color = s.Color, LetterSpacing = s.LetterSpacing, Upper = s.Upper, Align = s.Align,
                LineHeight = s.LineHeight, Shadow = s.Shadow, ShadowColor = s.ShadowColor, ShadowOffset = s.ShadowOffset,
                ShadowBlur = s.ShadowBlur, NoWrap = s.NoWrap
            };
        }

        /// <summary>"rgb(...)" / "rgba(...)" / "#rrggbb" → sRGB colour.</summary>
        public static Color ColorOf(string css, Color fallback)
        {
            if (string.IsNullOrEmpty(css)) return fallback;
            css = css.Trim();
            if (css[0] != '#') return MenuV6Layout.Css.ColorOf(css, fallback);
            var hex = css.Substring(1);
            if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            if (hex.Length < 6) return fallback;
            int H(int i) => int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            try { return new Color(H(0) / 255f, H(2) / 255f, H(4) / 255f, 1f); }
            catch { return fallback; }
        }

        // ------------------------------------------------------------------ geometry shortcuts

        /// <summary>The altar's slot cells (geo.slots: 10 × 4 at the page's stage px).</summary>
        public List<Rect> Slots => Rects(Geo, "slots");

        /// <summary>The House of Life cells of the collection (geo.grid; the page shows 6 × 2 / 4 × 2).</summary>
        public List<Rect> Grid => Rects(Geo, "grid");

        public Rect Inspect
        {
            get
            {
                var i = RJ.D(GeoD, "inspect");
                return i != null ? new Rect((float)RJ.N(i, "x"), (float)RJ.N(i, "y"), (float)RJ.N(i, "w"), (float)RJ.N(i, "h")) : El("insp");
            }
        }

        public int GridCols => (int)RJ.N(RJ.D(GeoD, "grid"), "cols", Land ? 6 : 4);
        public float GridCw => (float)RJ.N(RJ.D(GeoD, "grid"), "cw", Land ? 132 : 218);
        public float GridCh => (float)RJ.N(RJ.D(GeoD, "grid"), "ch", Land ? 192 : 318);
        public float GridGx => (float)RJ.N(RJ.D(GeoD, "grid"), "gx", Land ? 25.6 : 36.7);
        public float GridGy => (float)RJ.N(RJ.D(GeoD, "grid"), "gy", Land ? 46 : 60);

        /// <summary>The niche sprite behind collection cell (row, col): rows ≥ 2 reuse row 1's (decision 1), with the niche's
        /// offset from its card (the niche rect − the card rect, stage px).</summary>
        public Sprite Niche(int row, int col, out Rect offsetFromCard)
        {
            offsetFromCard = Rect.zero;
            var per = RJ.L(RJ.D(D("hall"), "niches"), "perCell");
            var cells = Grid;
            if (per == null) return null;
            var r = Math.Min(row, 1);
            foreach (var o in per)
            {
                if ((int)RJ.N(o, "row") != r || (int)RJ.N(o, "col") != col) continue;
                var nr = RJ.R(o, "rect");
                var idx = r * GridCols + col;
                if (idx < cells.Count)
                {
                    var c = cells[idx];
                    offsetFromCard = new Rect(nr.x - c.x, nr.y - c.y, nr.width, nr.height);
                }

                return MenuV6Art.Sprite(RJ.S(o, "file"));
            }

            return null;
        }
    }

    /// <summary>
    /// The deck page's own formulas (deck_&lt;layout&gt;.json timeline; deck.js), in event time: u = seconds since the event
    /// (a tap, a +1, a SAVE) instead of the demo's looping clock. System types only (checked offline).
    /// </summary>
    public static class DeckHallMath
    {
        static double Seg(double t, double a, double b) => MenuV6Math.Seg(t, a, b);
        static double Out(double t) => MenuV6Math.Out(t);
        static double IO(double t) => MenuV6Math.InOut(t);
        static double SStep(double a, double b, double x) => MenuV6Math.SStep(a, b, x);

        /// <summary>Altar slot i laying in at t since the open: eOut((t − .25 − .018 i) / .35) (deck.js:134).</summary>
        public static double SlotIn(double t, int i) => Out((t - .25 - .018 * i) / .35);

        /// <summary>Collection card i dealt at t since the open: eOut((t − .9 − .04 i) / .4) (deck.js:153).</summary>
        public static double CardIn(double t, int i) => Out((t - .9 - .04 * i) / .4);

        /// <summary>The Scale's rest tilt for a Main deck of n cards: −.2 short of 40, 0 legal, +.2 over 60 (decided).</summary>
        public static double RestTilt(int n, int min, int max) => n < min ? -.2 : n > max ? .2 : 0;

        /// <summary>The tilt u seconds after the count changed from tilt a to tilt b: lerp(a, b, eOut(seg(u, .05, .8)))
        /// (deck.js:189: the level-off runs .75 s after the odometer's start).</summary>
        public static double Tilt(double a, double b, double u) => a + (b - a) * Out(Seg(u, .05, .8));

        /// <summary>legalK u seconds after the deck turned legal (or illegal, k reversed): eIO(seg(u, .12, .45)).</summary>
        public static double LegalK(double u) => IO(Seg(u, .12, .45));

        /// <summary>The odometer roll k (0 = the old count at rest, 1 = the new one) u seconds after a change: eIO(seg(u, 0, .34)).</summary>
        public static double Roll(double u) => IO(Seg(u, 0, .34));

        /// <summary>The old status line's opacity u seconds after the change (1 − eIO(seg(u, .02, .2))) and the new one's
        /// (eIO(seg(u, .22, .42))): the old fades before the new (§v6.14.10 F12).</summary>
        public static double StatusOld(double u) => 1 - IO(Seg(u, .02, .2));

        public static double StatusNew(double u) => IO(Seg(u, .22, .42));

        /// <summary>The +1 flight at u since the press (deck.js:167): fk = seg(u, .05, .7); e = eIO(fk); the arc lifts 160 px
        /// (sin pi e) and rolls −8 sin(pi e) degrees.</summary>
        public static void Flight(double u, double bx, double by, double bw, double sx, double sy, double sw, out double x,
            out double y, out double w, out double rotDeg, out double e)
        {
            var fk = Seg(u, .05, .7);
            e = IO(fk);
            x = bx + (sx - bx) * e;
            y = by + (sy - by) * e - 160 * Math.Sin(Math.PI * e);
            w = bw + (sw - bw) * e;
            rotDeg = -8 * Math.Sin(Math.PI * e);
        }

        /// <summary>The landing burst bk = seg(u, .68, 1.4) (u since the press): ring scale .3 + 2.4 bk, alpha 1 − bk.</summary>
        public static double Burst(double u) => Seg(u, .68, 1.4);

        /// <summary>Mote i of the 36 at burst bk: angle i / 36 · 2pi + i, distance eOut(bk) (60 + (37 i mod 50)).</summary>
        public static void Mote(int i, double bk, out double dx, out double dy, out double a)
        {
            var ang = i / 36.0 * Math.PI * 2 + i;
            var d = Out(bk) * (60 + (37 * i) % 50);
            dx = Math.Cos(ang) * d;
            dy = Math.Sin(ang) * d;
            a = .9 * (1 - bk);
        }

        /// <summary>The inspect sheet's lock state u seconds after the tap (deck.js:221-225, the demo's ph 3.2 = u 0):
        /// h = eOut(seg(u, .25, .46)), b = eIO(seg(u, .4, .75)); closing (v ≥ 0 seconds since the close):
        /// b = min(b, 1 − es(seg(v, 0, .62))), h = min(h, 1 − eIO(seg(v, .56, .76))).</summary>
        public static void Lock(double u, double v, out double h, out double b)
        {
            h = Out(Seg(u, .25, .46));
            b = IO(Seg(u, .4, .75));
            if (v >= 0)
            {
                var es = .5 - .5 * Math.Cos(Math.PI * Seg(v, 0, .62));
                b = Math.Min(b, 1 - es);
                h = Math.Min(h, 1 - IO(Seg(v, .56, .76)));
            }
        }

        /// <summary>The inspect sheet's art + text: whole while open (the clip reveals them), faded on the close
        /// (1 − eIO(seg(v, −.12, .2)), deck.js:163: the page starts the fade .12 s before its close beat, Unity at the close).</summary>
        public static double InspectContent(double v) => v < 0 ? 1 : 1 - IO(Seg(v, 0, .32));

        /// <summary>A tap ripple u seconds old: opacity 1 − u / .5, scale .4 + 2.2 u (deck.js:157).</summary>
        public static void Ripple(double u, out double a, out double s)
        {
            a = u < .5 ? 1 - u / .5 : 0;
            s = .4 + 2.2 * u;
        }

        /// <summary>The SAVE plate's press pk u seconds after the tap (deck.js:208, ph 7.13 = u 0): sstep(0, .1, u)(1 − sstep(.17, .29, u)).</summary>
        public static double SavePress(double u) => SStep(0, .1, u) * (1 - SStep(.17, .29, u));

        /// <summary>The box: shut = eIn(seg(u, .12, .32)) (u since the SAVE tap); the reopen eBack(seg(v, 0, .4)).</summary>
        public static double BoxShut(double u) => MenuV6Math.In(Seg(u, .12, .32));

        public static double BoxReopen(double v) => MenuV6Math.Back(Seg(v, 0, .4));

        /// <summary>The Wdjat after the shut: eyeOn = eIO(seg(u, .3, .6)); eye = lerp(.15 open + .3 (1 − open),
        /// lerp(1.5, .7, seg(u, .32, 1.17)), eyeOn).</summary>
        public static double BoxEye(double u, double open, double eyeOn)
        {
            var baseEye = .15 * open + .3 * (1 - open);
            var lit = 1.5 + (.7 - 1.5) * Seg(u, .32, 1.17);
            return baseEye + (lit - baseEye) * eyeOn;
        }

        public static double BoxEyeOn(double u) => IO(Seg(u, .3, .6));

        /// <summary>The shut flash at u since the SAVE tap: .6 sstep(.3, .37)(1 − sstep(.37, .55)).</summary>
        public static double BoxFlash(double u) => .6 * SStep(.3, .37, u) * (1 - SStep(.37, .55, u));

        /// <summary>The exclusion chip scale exK = eBack(seg(u, 0, .35)) (u since the "-word" appeared).</summary>
        public static double ExclusionChip(double u) => MenuV6Math.Back(Seg(u, 0, .35));

        /// <summary>The level-curve bar height for n monsters in its band: 3 n + 3 px (deck.js:100), capped at the 28 px box.</summary>
        public static double CurveBar(int n, double box = 28) => Math.Min(box, 3 * n + 3);

        /// <summary>The curve's six bands by Level: 1-2 / 3 / 4 / 5-6 / 7 / 8+ (deck.js:100).</summary>
        public static int CurveBand(int level) => level <= 2 ? 0 : level == 3 ? 1 : level == 4 ? 2 : level <= 6 ? 3 : level == 7 ? 4 : 5;

        /// <summary>Colour lerp a → b by k, per channel (deck.js:263 mixes rounded channels).</summary>
        public static Color Mix(Color a, Color b, double k)
        {
            var t = (float)MenuV6Math.Clamp(k, 0, 1);
            return new Color(Mathf.Round(Mathf.Lerp(a.r, b.r, t) * 255f) / 255f, Mathf.Round(Mathf.Lerp(a.g, b.g, t) * 255f) / 255f,
                Mathf.Round(Mathf.Lerp(a.b, b.b, t) * 255f) / 255f, Mathf.Lerp(a.a, b.a, t));
        }
    }
}
