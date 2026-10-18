using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Data;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>Read-only accessors over the exporter's rig JSON (Data/rigs_&lt;layout&gt;.json through MenuV6Json).</summary>
    public static class RJ
    {
        public static Dictionary<string, object> D(object o, string k) =>
            o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> L(object o, string k) =>
            o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v as List<object> : null;

        public static string S(object o, string k) =>
            o is Dictionary<string, object> d && d.TryGetValue(k, out var v) ? v as string : null;

        public static double N(object o, string k, double def = 0) =>
            o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is double x ? x : def;

        public static bool B(object o, string k) =>
            o is Dictionary<string, object> d && d.TryGetValue(k, out var v) && v is bool b && b;

        public static double At(List<object> a, int i, double def = 0) => a != null && i < a.Count && a[i] is double x ? x : def;

        public static Vector2 V(object o, string k)
        {
            var a = L(o, k);
            return a != null && a.Count >= 2 ? new Vector2((float)At(a, 0), (float)At(a, 1)) : Vector2.zero;
        }

        /// <summary>A rect as [x, y, w, h] or {x, y, w, h}.</summary>
        public static Rect R(object o, string k)
        {
            var a = L(o, k);
            if (a != null && a.Count >= 4)
                return new Rect((float)At(a, 0), (float)At(a, 1), (float)At(a, 2), (float)At(a, 3));
            var d = D(o, k);
            return d != null ? new Rect((float)N(d, "x"), (float)N(d, "y"), (float)N(d, "w"), (float)N(d, "h")) : Rect.zero;
        }

        public static bool Has(object o, string k) => o is Dictionary<string, object> d && d.ContainsKey(k) && d[k] != null;

        public static Color Css(string css, Color fallback) => MenuV6Layout.Css.ColorOf(css, fallback);
    }

    /// <summary>A control's hover / press state as the rigs read it (menu.js:547-548, :741).</summary>
    public interface IRigPointer
    {
        double Value(double t);
        double Press(double t, double dur);
        bool Over { get; }

        /// <summary>Captures and tests: a hover tween from → to started <paramref name="since"/> s before t (NaN = no
        /// hover), a press <paramref name="pressAge"/> s old (NaN = none).</summary>
        void Force(double t, double from, double to, double since, double pressAge);
    }

    /// <summary>The hover / press state itself (no engine types: the offline harness drives it too).</summary>
    public sealed class RigPointerState : IRigPointer
    {
        public MenuV6RigMath.HoverTween Hover;
        public double PressT = double.NaN;
        public bool Over { get; set; }

        public double Value(double t) => Hover.Value(t);
        public double Press(double t, double dur) => MenuV6RigMath.Press(t, PressT, dur);

        public void Enter(double t)
        {
            Over = true;
            Hover.Go(1, t);
        }

        public void Exit(double t)
        {
            Over = false;
            Hover.Go(0, t);
        }

        public void Down(double t) => PressT = t;

        public void Force(double t, double from, double to, double since, double pressAge)
        {
            if (double.IsNaN(since))
            {
                Hover = default;
                Over = false;
            }
            else
            {
                Hover = new MenuV6RigMath.HoverTween { Set = true, From = from, To = to, T0 = t - since };
                Over = to > .5;
            }

            PressT = double.IsNaN(pressAge) ? double.NaN : t - pressAge;
        }
    }

    /// <summary>
    /// A control's hover / press state for the rigs: pointer enter / exit start the page's .2 s hover tween, pointer down
    /// stamps the press time (menu clock). It sits beside the control's ArcaneButton (the EventSystem gives every handler
    /// on the object the same event) and changes nothing else on it.
    /// </summary>
    public sealed class RigPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IRigPointer
    {
        [NonSerialized] readonly RigPointerState _s = new();

        public bool Over => _s.Over;
        public double Value(double t) => _s.Value(t);
        public double Press(double t, double dur) => _s.Press(t, dur);
        public void OnPointerEnter(PointerEventData e) => _s.Enter(MenuV6Clock.Now);
        public void OnPointerExit(PointerEventData e) => _s.Exit(MenuV6Clock.Now);
        public void OnPointerDown(PointerEventData e) => _s.Down(MenuV6Clock.Now);
        public void Force(double t, double from, double to, double since, double pressAge) => _s.Force(t, from, to, since, pressAge);

        public static RigPointer On(Component c)
        {
            if (c == null) return null;
            var p = c.GetComponent<RigPointer>();
            return p != null ? p : c.gameObject.AddComponent<RigPointer>();
        }
    }

    /// <summary>What a rig needs from the stage that builds it.</summary>
    public sealed class RigContext
    {
        public MenuLayoutKind Kind;
        public Dictionary<string, object> Rigs;   // rigs_<layout>.json "rigs"
        public double K = 1;                      // stage k (land 1, port 1.4)
        public TorchFlicker Torch;
        public MenuV6Snapshot Data;
        public IRigPointer Door, Map, Quit, Plaque, Ring;
        public readonly Dictionary<string, IRigPointer> Recess = new();
        public readonly Dictionary<string, (Text title, Text blurb)> RecessLabels = new();
        public Text QuitTitle;
        public Text VoiceLine;
        public IRigGfx Gfx;
        public double[] Noise;                    // schedules.json torch.NZF_row7 (the flick's noise row)
        public Func<double> RealTime = () => 0;   // wall-clock seconds (CSS transitions); the stage passes Time.unscaledTime
        public string Layout => Kind == MenuLayoutKind.Landscape ? "Land" : "Port";
        public Dictionary<string, object> Rig(string id) => RJ.D(Rigs, id);
    }

    /// <summary>
    /// One idle rig: built once into its host (a RectTransform covering the stage rect <see cref="Frame"/>, at the page's
    /// z position of the layer it replaces) and redrawn for menu time t by <see cref="MenuV6Life"/>.
    /// </summary>
    public abstract class MenuV6Rig
    {
        public string Name { get; protected set; }
        protected object Host;
        protected Rect Frame;
        protected RigContext Ctx;
        protected IRigGfx Gfx => Ctx.Gfx;

        /// <summary>Set up the rig into <paramref name="host"/> (a handle covering the stage rect
        /// <paramref name="frame"/>); false (or a throw) = the stage keeps the P1a still for this part.</summary>
        public abstract bool Build(RigContext ctx, object host, Rect frame);

        public abstract void Tick(double t);

        /// <summary>A rig that follows wall-clock time too (the page's CSS transitions run on real time, not on the
        /// canvas clock): ticked every frame, even while a capture holds the menu clock.</summary>
        public virtual bool EveryFrame => false;

        protected void Init(string name, RigContext ctx, object host, Rect frame)
        {
            Name = name;
            Ctx = ctx;
            Host = host;
            Frame = frame;
        }

        protected ShapeLayer Shapes(string name, bool add = false)
        {
            var l = Gfx.Shapes(Host, name, Frame);
            l.Add = add;
            return l;
        }

        protected QuadLayer Quads(string name, RigTex tex, bool add = false)
        {
            var l = Gfx.Quads(Host, name, Frame, tex);
            l.Add = add;
            return l;
        }

        /// <summary>A layer of the page's glow sprites (SPR): always drawn 'lighter' by the page.</summary>
        protected QuadLayer GlowQuads(string name, float gamma) => Quads(name, Gfx.Glow(gamma), true);

        protected static void Spr(QuadLayer q, MenuV6Glow.Kind kind, double x, double y, double size, double a, float gamma)
        {
            if (q == null || a <= .003 || size <= 0) return;
            q.Sprite(x, y, size, size, MenuV6Glow.Uv(kind), new Color(1f, 1f, 1f, MenuV6Linear.Alpha(Math.Min(1, a), gamma)));
        }

        protected static Color Col(int rgb, double a) =>
            new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, (float)MenuV6MathF.Clamp01(a));

        /// <summary>A part sprite (tracked art; MenuV6Local paths go through <see cref="LocalPart"/>).</summary>
        protected RigTex Part(object part)
        {
            var f = RJ.S(part, "file");
            return string.IsNullOrEmpty(f) ? null : Gfx.Sprite(f);
        }

        protected RigTex LocalPart(object part)
        {
            var f = RJ.S(part, "file");
            return string.IsNullOrEmpty(f) ? null : Gfx.Local(f);
        }

        /// <summary>A flipbook's cell UVs (frames[i].x / .y, cell w × h in sheet px).</summary>
        protected static List<Rect> Cells(RigTex sheet, Dictionary<string, object> fb)
        {
            var list = new List<Rect>();
            var cell = RJ.L(fb, "cell");
            double cw = RJ.At(cell, 0), ch = RJ.At(cell, 1);
            var frames = RJ.L(fb, "frames");
            if (sheet == null || frames == null) return list;
            foreach (var f in frames)
                list.Add(QuadList.Cell(sheet.W, sheet.H, RJ.N(f, "x"), RJ.N(f, "y"), cw, ch));
            return list;
        }
    }

    internal static class MenuV6MathF
    {
        public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }

    /// <summary>
    /// The Stone's idle life (BUILD_PLAN §6 P1b): ticks every rig at menu time t (MenuV6Clock), only when t moved (a
    /// frozen capture clock costs nothing). A rig that throws is dropped once with a warning; the rest keep running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuV6Life : MonoBehaviour
    {
        public readonly List<MenuV6Rig> Rigs = new();
        public readonly List<string> Dropped = new();
        double _last = double.NaN;

        public void Add(MenuV6Rig rig)
        {
            if (rig != null) Rigs.Add(rig);
        }

        public MenuV6Rig Find(string name)
        {
            foreach (var r in Rigs)
                if (r != null && r.Name == name)
                    return r;
            return null;
        }

        public void TickNow()
        {
            _last = double.NaN;
            Tick(MenuV6Clock.Now);
        }

        void LateUpdate() => Tick(MenuV6Clock.Now);

        void Tick(double t)
        {
            var moved = t != _last;
            _last = t;
            for (var i = Rigs.Count - 1; i >= 0; i--)
            {
                var r = Rigs[i];
                if (!moved && !r.EveryFrame) continue;
                try
                {
                    r.Tick(t);
                }
                catch (Exception ex)
                {
                    Dropped.Add(r.Name);
                    Rigs.RemoveAt(i);
                    Debug.LogWarning("[WRLDZ MenuV6] rig " + r.Name + " stopped: " + ex.Message);
                }
            }
        }
    }

    // ====================================================================== the room at rest (menu.js:659-660, :675-676)

    /// <summary>The emissive breath of the Stone's blur layer (.55 bre) and the vignette's pr, on the P1a layers (their
    /// Linear-space gains included).</summary>
    public sealed class BreathRig : MenuV6Rig
    {
        Graphic _blur;
        readonly List<Graphic> _vignettes = new();
        float _gBlur = 1f, _gVig = 1f;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("breath", ctx, host, frame);
            return true;
        }

        public void Blur(Graphic g, float gain)
        {
            _blur = g;
            _gBlur = gain;
        }

        public void Vignette(Graphic g, float gain)
        {
            if (g == null) return;
            _vignettes.Add(g);
            _gVig = gain;
        }

        public override void Tick(double t)
        {
            if (_blur != null) SetA(_blur, .55 * MenuV6RigMath.EmissiveBreath(t) * _gBlur);
            var pr = MenuV6RigMath.VignettePr(t);
            foreach (var v in _vignettes) SetA(v, pr * _gVig);
        }

        static void SetA(Graphic g, double a)
        {
            var c = g.color;
            var na = (float)MenuV6MathF.Clamp01(a);
            if (Mathf.Abs(c.a - na) < 1e-4f) return;
            c.a = na;
            g.color = c;
        }
    }

    // ====================================================================== recesses (anime_menu_v6.js:97-127)

    /// <summary>The six recesses' hover and press: the gold glow, the hot picto, the gold aura ring (5 ops at alpha k each),
    /// 3 sparkles, the Solid Vision beam with its holo and scan lines, and on press the inverted crescent, 12 rays and a
    /// flash ring; the label lights (.lit) while v &gt; .45 with the page's CSS transitions.</summary>
    public sealed class RecessRig : MenuV6Rig
    {
        sealed class One
        {
            public string Id;
            public double X, Y, Rr;
            public IRigPointer P;
            public QuadLayer Glow, Hot, Holo, Crescent;
            public readonly QuadLayer[] Ring = new QuadLayer[5];
            public readonly Rect[] RingRect = new Rect[5];
            public Rect HotRect, CrescentRect;
            public Vector2 HotAbout;
            public ShapeLayer Spark, Beam, Scan, Burst, Flash;
        }

        static readonly string[] RingParts = { "ring_base", "ring_sh", "ring_hi", "ring_ink1", "ring_ink0" };
        static readonly double[] RayX = new double[3], RayY = new double[3];
        readonly List<One> _all = new();
        double _k = 1;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("recess", ctx, host, frame);
            var spec = ctx.Rig("recess");
            var ids = RJ.D(spec, "ids");
            if (ids == null) return false;
            _k = ctx.K;
            foreach (var kv in ids)
            {
                if (!ctx.Recess.TryGetValue(kv.Key, out var ptr) || ptr == null) continue;
                var r = kv.Value as Dictionary<string, object>;
                var parts = RJ.D(r, "parts");
                var c = RJ.V(r, "centre");
                var one = new One { Id = kv.Key, X = c.x, Y = c.y, Rr = RJ.N(r, "rr"), P = ptr };
                one.Glow = GlowQuads("Recess_" + one.Id + "_glow", MenuV6Linear.Dark);
                var hot = Part(RJ.D(parts, "hot1"));
                if (hot != null)
                {
                    one.Hot = Quads("Recess_" + one.Id + "_hot", hot);
                    one.HotRect = RJ.R(RJ.D(parts, "hot1"), "rect");
                    one.HotAbout = RJ.V(RJ.D(parts, "hot1"), "scaleAbout");
                }

                for (var i = 0; i < RingParts.Length; i++)
                {
                    var p = RJ.D(parts, RingParts[i]);
                    var s = Part(p);
                    if (s == null) continue;
                    one.Ring[i] = Quads("Recess_" + one.Id + "_" + RingParts[i], s);
                    one.RingRect[i] = RJ.R(p, "rect");
                }

                one.Spark = Shapes("Recess_" + one.Id + "_sparkles");
                one.Beam = Shapes("Recess_" + one.Id + "_beam", true);
                var holo = Part(RJ.D(parts, "holo"));
                if (holo != null) one.Holo = Quads("Recess_" + one.Id + "_holo", holo, true);
                one.Scan = Shapes("Recess_" + one.Id + "_scan", true);
                var cres = Part(RJ.D(parts, "crescent"));
                if (cres != null)
                {
                    one.Crescent = Quads("Recess_" + one.Id + "_crescent", cres);
                    one.CrescentRect = RJ.R(RJ.D(parts, "crescent"), "rect");
                }

                one.Burst = Shapes("Recess_" + one.Id + "_burst");
                one.Flash = Shapes("Recess_" + one.Id + "_flash", true);
                _all.Add(one);
            }

            return _all.Count > 0;
        }

        public override void Tick(double t)
        {
            foreach (var o in _all) Draw(o, t);
        }

        void Draw(One o, double t)
        {
            var K = _k;
            var v = o.P != null ? o.P.Value(t) : 0;
            var pk = o.P != null ? o.P.Press(t, .45) : 0;
            var k = MenuV6RigMath.RecessK(v, pk);
            double x = o.X, y = o.Y, rr = o.Rr;
            o.Glow.Clear();
            o.Hot?.Clear();
            foreach (var q in o.Ring) q?.Clear();
            o.Spark.Clear();
            o.Beam.Clear();
            o.Holo?.Clear();
            o.Scan.Clear();
            o.Crescent?.Clear();
            o.Burst.Clear();
            o.Flash.Clear();
            if (k > .01)
            {
                var pop = MenuV6RigMath.RecessPop(k);
                Spr(o.Glow, MenuV6Glow.Kind.GoldHot, x, y, rr * 3.2 * pop, .3 * Math.Min(1, k), MenuV6Linear.Dark);
                if (o.Hot != null)
                {
                    var f = pop / 1.06;
                    var hr = Scaled(o.HotRect, o.HotAbout.x, o.HotAbout.y, f);
                    o.Hot.Rect(hr, QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Alpha(MenuV6RigMath.Clamp01(.85 * k))));
                }

                var fr = (rr + 5.5 * K * pop) / (rr + 5.5 * K * 1.06);
                var ka = MenuV6Linear.Alpha(MenuV6RigMath.Clamp01(k));
                for (var i = 0; i < o.Ring.Length; i++)
                    o.Ring[i]?.Rect(Scaled(o.RingRect[i], x, y, fr), QuadList.Full, new Color(1, 1, 1, ka));
                for (var s2 = 0; s2 < 3; s2++)
                {
                    var an = -2.3 + s2 * 1.9 + t * .35;
                    var sp = (4 + 3 * Math.Sin(t * 2.6 + s2 * 2)) * K * Math.Min(1, k);
                    if (sp > .5)
                        o.Spark.Sparkle(x + Math.Cos(an) * (rr + 12 * K), y + Math.Sin(an) * (rr + 12 * K), sp,
                            Col(s2 == 1 ? 0xFFF1B8 : 0xEDE49E, ka), .3);
                }
            }

            var hv = MenuV6RigMath.Clamp01(v);
            if (hv > .02)
            {
                var top = y - rr * .55 - 26 * K * hv;
                double w0 = rr * .6, w1 = rr * .95;
                var c1 = Col(0x33B8E8, MenuV6Linear.Alpha(.16 * hv, MenuV6Linear.Dark));
                o.Beam.Quad(x - w0, y - rr * .2, x + w0, y - rr * .2, x + w1, top, x - w1, top, c1, c1, c1, c1);
                var c2 = Col(0x9FE6FF, MenuV6Linear.Alpha(.14 * hv, MenuV6Linear.Dark));
                o.Beam.Quad(x - w0 * .55, y - rr * .2, x + w0 * .55, y - rr * .2, x + w1 * .55, top, x - w1 * .55, top, c2, c2, c2, c2);
                var hs = rr * 1.05;
                o.Holo?.Rect(new Rect((float)(x - hs / 2), (float)(top - hs * .5 + Math.Sin(t * 2.2) * 2 * K), (float)hs, (float)hs),
                    QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Alpha(.7 * hv, MenuV6Linear.Dark)));
                var sc = Col(0x9FE6FF, MenuV6Linear.Alpha(.35 * hv, MenuV6Linear.Dark));
                for (var k2 = 0; k2 < 6; k2++)
                    o.Scan.RectAA(x - hs / 2, top - hs * .55 + MenuV6RigMath.Mod(t * 30 + k2 * hs / 6, hs), hs, 1, sc);
            }

            if (pk > .01)
            {
                var age = MenuV6RigMath.PressAge(pk, .45);
                var inv = Math.Min(1, pk * 1.6);
                o.Crescent?.Rect(o.CrescentRect, QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Alpha(inv)));
                MenuV6RigMath.Burst(age, out var bk, out var sh);
                if (bk > 0)
                {
                    var gc = Col(0xEDE49E, MenuV6Linear.Alpha(bk));
                    for (var q = 0; q < 12; q++)
                    {
                        MenuV6RigMath.RecessRay(q, rr, age, sh, out var an, out var r0, out var r1, out var ww);
                        RayX[0] = x + Math.Cos(an) * r1;
                        RayY[0] = y + Math.Sin(an) * r1;
                        RayX[1] = x + Math.Cos(an + ww / r0) * r0;
                        RayY[1] = y + Math.Sin(an + ww / r0) * r0;
                        RayX[2] = x + Math.Cos(an - ww / r0) * r0;
                        RayY[2] = y + Math.Sin(an - ww / r0) * r0;
                        o.Burst.Poly(RayX, RayY, 3, gc);
                    }
                }

                o.Flash.Circle(x, y, rr + (6 + 26 * (1 - pk)) * K, 2.5 * K, Col(0x9FE6FF, MenuV6Linear.Alpha(.8 * pk, MenuV6Linear.Dark)));
            }

            o.Glow.Commit();
            o.Hot?.Commit();
            foreach (var q in o.Ring) q?.Commit();
            o.Spark.Commit();
            o.Beam.Commit();
            o.Holo?.Commit();
            o.Scan.Commit();
            o.Crescent?.Commit();
            o.Burst.Commit();
            o.Flash.Commit();
        }

        static Rect Scaled(Rect r, double ax, double ay, double f)
        {
            if (Math.Abs(f - 1) < 1e-6) return r;
            var x0 = ax + (r.xMin - ax) * f;
            var y0 = ay + (r.yMin - ay) * f;
            return new Rect((float)x0, (float)y0, (float)(r.width * f), (float)(r.height * f));
        }
    }

    /// <summary>The six recess labels' lit state: .lit while the recess's hover value (menu clock) is over .45; the CSS
    /// transitions run on wall-clock time, as the page's do.</summary>
    public sealed class RecessLabelsRig : MenuV6Rig
    {
        readonly List<(IRigPointer p, LabelLit lit)> _labels = new();
        public override bool EveryFrame => true;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("recessLabels", ctx, host, frame);
            var spec = RJ.D(ctx.Rig("recess"), "label");
            foreach (var kv in ctx.RecessLabels)
                if (ctx.Recess.TryGetValue(kv.Key, out var p) && p != null && (kv.Value.title != null || kv.Value.blurb != null))
                    _labels.Add((p, new LabelLit(kv.Value.title, kv.Value.blurb, spec)));
            return _labels.Count > 0;
        }

        public override void Tick(double t)
        {
            var now = Ctx.RealTime();
            foreach (var (p, lit) in _labels) lit.Tick(now, p.Value(t) > .45);
        }
    }

    /// <summary>
    /// The .rl label's lit state (menu.js:1037: .lit while v &gt; .45) with the page's CSS transitions (wrldz.css:34-35):
    /// the title's colour (.2 s ease) and tracking (.25 s ease-out, .2 em → .25 em), the sub-line's colour and opacity
    /// (.2 s ease). Transitions start from the current value, as CSS does.
    /// </summary>
    public sealed class LabelLit
    {
        readonly Text _t, _s;
        readonly LetterSpacingEffect _ls;
        readonly Color _tOff, _tOn, _sOff, _sOn;
        readonly float _lsOff, _lsOn;
        bool _lit;
        double _t0 = double.NegativeInfinity;
        Color _tFrom, _sFrom;
        float _lsFrom;

        public LabelLit(Text title, Text blurb, Dictionary<string, object> spec)
        {
            _t = title;
            _s = blurb;
            _ls = title != null ? title.GetComponent<LetterSpacingEffect>() : null;
            _tOff = title != null ? title.color : Color.white;
            _sOff = blurb != null ? blurb.color : Color.white;
            _lsOff = _ls != null ? _ls.Spacing : 0f;
            var on = RJ.D(spec, "on");
            var off = RJ.D(spec, "off");
            _tOn = RJ.Css(RJ.S(RJ.D(on, "t"), "color"), _tOff);
            _tOn.a = _tOff.a;
            var sCol = RJ.Css(RJ.S(RJ.D(on, "s"), "color"), _sOff);
            float.TryParse(RJ.S(RJ.D(on, "s"), "opacity") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var sOp);
            float.TryParse(RJ.S(RJ.D(off, "s"), "opacity") ?? "1", NumberStyles.Float, CultureInfo.InvariantCulture, out var sOpOff);
            // the rest colour already carries the rest opacity (TextFx.Apply): the lit one carries the lit opacity
            var restA = sOpOff > 0 ? _sOff.a / sOpOff : _sOff.a;
            sCol.a = restA * sOp;
            _sOn = sCol;
            // .lit tracking .25 em against .2 em at rest (the recorded 5.75 / 4.6 px of a 23 px label)
            var lsOn = Px(RJ.S(RJ.D(on, "t"), "letterSpacing"));
            var lsOffRec = Px(RJ.S(RJ.D(off, "t"), "letterSpacing"));
            _lsOn = lsOffRec > 0 ? _lsOff * lsOn / lsOffRec : _lsOff * 1.25f;
            _tFrom = _tOff;
            _sFrom = _sOff;
            _lsFrom = _lsOff;
        }

        static float Px(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            s = s.Trim();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        Color CurT(double t) => Color.Lerp(_tFrom, _lit ? _tOn : _tOff, (float)MenuV6RigMath.Ease((t - _t0) / .2));
        Color CurS(double t) => Color.Lerp(_sFrom, _lit ? _sOn : _sOff, (float)MenuV6RigMath.Ease((t - _t0) / .2));
        float CurLs(double t) => Mathf.Lerp(_lsFrom, _lit ? _lsOn : _lsOff, (float)MenuV6RigMath.EaseOut((t - _t0) / .25));

        /// <param name="now">wall-clock seconds (CSS transitions do not follow the canvas clock)</param>
        public void Tick(double now, bool lit)
        {
            var t = now;
            if (lit != _lit)
            {
                _tFrom = CurT(t);
                _sFrom = CurS(t);
                _lsFrom = CurLs(t);
                _lit = lit;
                _t0 = t;
                _settled = false;
            }

            if (_settled) return;
            if (_t != null)
            {
                var c = CurT(t);
                if (_t.color != c) _t.color = c;
            }

            if (_s != null)
            {
                var c = CurS(t);
                if (_s.color != c) _s.color = c;
            }

            if (_ls != null)
            {
                var l = CurLs(t);
                if (!Mathf.Approximately(_ls.Spacing, l)) _ls.Spacing = l;
            }

            // the transitions are over: nothing changes until the next switch
            _settled = t - _t0 > .3;
        }

        bool _settled = true;
    }
}
