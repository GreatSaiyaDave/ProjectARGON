using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    // ====================================================================== the DUEL door (anime_menu_v6.js:131-139)

    /// <summary>The door at rest: the dashed Solid Vision ring crawling at 6 px/s and the 5-slot sweep every 5.2 s; hover:
    /// a gold glow and 4 sparkles; press: 14 gold rays (the opening itself is P3).</summary>
    public sealed class DoorRig : MenuV6Rig
    {
        ShapeLayer _dash, _top;
        QuadLayer _glow;
        double _cx, _cy, _R0, _k;
        readonly double[] _rx = new double[3], _ry = new double[3];
        const float G = MenuV6Linear.Glow;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("door", ctx, host, frame);
            var d = ctx.Rig("door");
            if (d == null) return false;
            var c = RJ.V(d, "centre");
            _cx = c.x;
            _cy = c.y;
            _R0 = RJ.N(d, "R0", 140);
            _k = RJ.N(d, "k", ctx.K);
            _dash = Shapes("Door_dash", true);
            _glow = GlowQuads("Door_glow", G);
            _top = Shapes("Door_sparks");
            return true;
        }

        public override void Tick(double t)
        {
            var K = _k;
            var hov = Ctx.Door != null ? MenuV6RigMath.Clamp01(Ctx.Door.Value(t)) : 0;
            var press = Ctx.Door != null ? Ctx.Door.Press(t, .4) : 0;
            _dash.Clear();
            _glow.Clear();
            _top.Clear();
            _dash.Circle(_cx, _cy, _R0 + 7 * K, 2 * K, Col(0x5FB6FF, MenuV6Linear.Alpha(.75, G)), 3 * K, 7 * K, -t * 6);
            if (hov + press > .01)
                Spr(_glow, MenuV6Glow.Kind.GoldHot, _cx, _cy, (_R0 + 40 * K) * 2.3, .22 * hov + .35 * press, G);
            for (var k = 0; k < 5; k++)
            {
                var kk = MenuV6RigMath.DoorSweep(t, k);
                if (kk <= .02) continue;
                var an = MenuV6RigMath.DoorSweepAngle(k);
                Spr(_glow, MenuV6Glow.Kind.Blue, _cx + Math.Cos(an) * 118 * K, _cy + Math.Sin(an) * 118 * K, 22 * K, .8 * kk, G);
            }

            if (hov > .01)
            {
                var a = MenuV6Linear.Alpha(hov);
                for (var s2 = 0; s2 < 4; s2++)
                {
                    var an = -2.4 + s2 * 1.6 + t * .25;
                    _top.Sparkle(_cx + Math.Cos(an) * (_R0 + 16 * K), _cy + Math.Sin(an) * (_R0 + 16 * K),
                        (5 + 3 * Math.Sin(t * 3 + s2)) * K, Col(s2 % 2 == 1 ? 0xFFF1B8 : 0xEDE49E, a), 0);
                }
            }

            if (press > .01)
            {
                var age = MenuV6RigMath.PressAge(press, .4);
                MenuV6RigMath.Burst(age, out var bk, out var sh);
                if (bk > 0)
                {
                    var gc = Col(0xEDE49E, MenuV6Linear.Alpha(bk));
                    for (var q = 0; q < 14; q++)
                    {
                        MenuV6RigMath.DoorRay(q, _R0, sh, out var an, out var r0, out var r1, out var ww);
                        _rx[0] = _cx + Math.Cos(an) * r1;
                        _ry[0] = _cy + Math.Sin(an) * r1;
                        _rx[1] = _cx + Math.Cos(an + ww / r0) * r0;
                        _ry[1] = _cy + Math.Sin(an + ww / r0) * r0;
                        _rx[2] = _cx + Math.Cos(an - ww / r0) * r0;
                        _ry[2] = _cy + Math.Sin(an - ww / r0) * r0;
                        _top.Poly(_rx, _ry, 3, gc);
                    }
                }
            }

            _dash.Commit();
            _glow.Commit();
            _top.Commit();
        }
    }

    // ====================================================================== the Puzzle (relics.js:196-215)

    /// <summary>
    /// A Puzzle relic drawn from its exported parts (A, the torch-lit B, the emissive emiB, the soft-clipped glint
    /// flipbook) with the page's per-frame lights: the Wedjat breath, the eye glow, the seam hot point, the iris glint.
    /// relics.js never resets globalAlpha after the emissive passes, so the eye glow, the hot point and the iris glint are
    /// multiplied by the last pass's alpha (ga). Used by the keystone and the voice.
    /// </summary>
    public sealed class PuzzleDraw
    {
        public Rect Body;          // origin + drawWH (stage px)
        public Vector2 EyeC, Iris;
        public double Wpx;
        public double[] SeamX, SeamY;
        public Rect GlintRect;
        public QuadLayer A, B, Emi, Glint, Lights, Eye;
        public Rect UvA = QuadList.Full, UvB = QuadList.Full, UvE = QuadList.Full;
        public List<Rect> GlintUv = new();
        public float Gamma = MenuV6Linear.Glow;

        /// <param name="flick">B's alpha (NaN = no B: the voice Puzzle)</param>
        public void Draw(double t, double flick, double eye, double glint, double hot, double iris)
        {
            A?.Clear();
            B?.Clear();
            Emi?.Clear();
            Eye?.Clear();
            Glint?.Clear();
            Lights?.Clear();
            A?.Rect(Body, UvA, Color.white);
            var gaBefore = 1.0;
            if (!double.IsNaN(flick) && B != null)
            {
                B.Rect(Body, UvB, new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(flick)));
                gaBefore = flick;
            }

            MenuV6RigMath.Emissive(eye, gaBefore, out var ek, out var p1, out var p2, out var ga);
            if (p1 > 0 && Emi != null)
            {
                Emi.Rect(Body, UvE, new Color(1, 1, 1, MenuV6Linear.Alpha(p1, Gamma)));
                if (p2 > 0) Emi.Rect(Body, UvE, new Color(1, 1, 1, MenuV6Linear.Alpha(p2, Gamma)));
            }

            if (ek > .01 && Eye != null)
                Spr(Eye, MenuV6Glow.Kind.Blue, EyeC.x, EyeC.y, Wpx * .7, ga * .22 * ek);
            if (glint >= 0 && glint < 1 && Glint != null && GlintUv.Count > 0)
            {
                var j = Math.Min(GlintUv.Count - 1, (int)Math.Floor(glint * GlintUv.Count));
                Glint.Rect(GlintRect, GlintUv[j], Color.white);
            }

            if (Lights != null)
            {
                if (hot >= 0 && hot <= 1 && SeamX != null && SeamX.Length >= 2)
                {
                    MenuV6RigMath.Polyline(SeamX, SeamY, hot, out var hx, out var hy);
                    Spr(Lights, MenuV6Glow.Kind.GoldHot, hx, hy, 16, ga * .8 * Math.Sin(hot * Math.PI));
                }

                if (iris > 0) Spr(Lights, MenuV6Glow.Kind.White, Iris.x + 2, Iris.y - 1, 12, ga * .95 * iris);
            }

            A?.Commit();
            B?.Commit();
            Emi?.Commit();
            Eye?.Commit();
            Glint?.Commit();
            Lights?.Commit();
        }

        void Spr(QuadLayer q, MenuV6Glow.Kind k, double x, double y, double size, double a)
        {
            if (a <= .003) return;
            q.Sprite(x, y, size, size, MenuV6Glow.Uv(k), new Color(1, 1, 1, MenuV6Linear.Alpha(Math.Min(1, a), Gamma)));
        }

        /// <summary>The flipbook's cell UVs (frames[i].x / .y, cell w × h in sheet px).</summary>
        public static List<Rect> Cells(RigTex sheet, Dictionary<string, object> fb)
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

    /// <summary>The Puzzle keystone at rest (menu.js:832-841): its shadow, A + the torch-lit B, the Wedjat breath
    /// (.68 ± .14, + .45 while the door is hovered), the glint every 6.2 s, the seam hot point every 7.5 s, the iris glint
    /// every 4.3 s.</summary>
    public sealed class KeystoneRig : MenuV6Rig
    {
        QuadLayer _shadow;
        Rect _shadowRect, _shadowUv = QuadList.Full;
        readonly PuzzleDraw _p = new();

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("keystone", ctx, host, frame);
            var k = ctx.Rig("keystone");
            var parts = RJ.D(k, "parts");
            var a = Part(RJ.D(parts, "A"));
            var b = Part(RJ.D(parts, "B"));
            var e = Part(RJ.D(parts, "emiB"));
            var sh = Part(RJ.D(parts, "shadow"));
            if (a == null) return false;
            var o = RJ.V(k, "origin");
            var wh = RJ.V(k, "drawWH");
            _p.Body = new Rect(o.x, o.y, wh.x, wh.y);
            _p.EyeC = RJ.V(k, "eyeC");
            _p.Iris = RJ.V(k, "iris");
            _p.Wpx = RJ.N(k, "wpx", 96);
            var seam = RJ.L(k, "seamPts");
            if (seam != null)
            {
                _p.SeamX = new double[seam.Count];
                _p.SeamY = new double[seam.Count];
                for (var i = 0; i < seam.Count; i++)
                {
                    _p.SeamX[i] = RJ.At(seam[i] as List<object>, 0);
                    _p.SeamY[i] = RJ.At(seam[i] as List<object>, 1);
                }
            }

            if (sh != null)
            {
                _shadow = Quads("Keystone_shadow", sh);
                _shadowRect = RJ.R(k, "shadowRect");
                _shadowUv = sh.ContentOf(RJ.D(parts, "shadow"));
            }

            _p.A = Quads("Keystone_A", a);
            _p.UvA = a.ContentOf(RJ.D(parts, "A"));
            if (b != null)
            {
                _p.B = Quads("Keystone_B", b);
                _p.UvB = b.ContentOf(RJ.D(parts, "B"));
            }

            if (e != null)
            {
                _p.Emi = Quads("Keystone_emiB", e, true);
                _p.UvE = e.ContentOf(RJ.D(parts, "emiB"));
            }
            _p.Eye = GlowQuads("Keystone_eyeGlow", MenuV6Linear.Glow);
            var g = RJ.D(k, "glint");
            var sheet = Gfx.Sheet(RJ.S(g, "file"));
            if (sheet != null)
            {
                _p.Glint = Quads("Keystone_glint", sheet, true);
                _p.GlintRect = RJ.R(g, "rect");
                _p.GlintUv = PuzzleDraw.Cells(sheet, g);
            }

            _p.Lights = GlowQuads("Keystone_lights", MenuV6Linear.Glow);
            return true;
        }

        public override void Tick(double t)
        {
            if (_shadow != null)
            {
                _shadow.Clear();
                _shadow.Rect(_shadowRect, _shadowUv, new Color(1, 1, 1, MenuV6Linear.Shade(.5)));
                _shadow.Commit();
            }

            // the page's flick at this t (menu.js:653): the same curve TorchFlicker drives Stone B with
            var flick = MenuV6Math.Flick(t, Ctx.Noise);
            var hovD = Ctx.Door != null ? MenuV6RigMath.Clamp01(Ctx.Door.Value(t)) : 0;
            var eye = MenuV6RigMath.KeystoneEye(t, hovD);
            if (Ctx.KeystoneEye != null) eye = Ctx.KeystoneEye(t, eye);
            _p.Draw(t, flick, eye, MenuV6RigMath.KeystoneGlint(t), MenuV6RigMath.KeystoneHot(t), MenuV6RigMath.KeystoneIris(t));
        }
    }

    // ====================================================================== THE PHARAOH'S VOICE (menu.js:601-607, :1000-1003)

    /// <summary>
    /// The voice: every 22 s (from t = 6) the line fades out and re-types (the page's one sourced line; LINES[0] ==
    /// LINES[1]), and its Puzzle glows for 2.8 s: rays, a gold glow, the emissive eye, the iris (landscape: the voice
    /// Puzzle beside the line; portrait: the season meter is the voice, its rays under the meter).
    /// </summary>
    public sealed class VoiceRig : MenuV6Rig
    {
        ShapeLayer _rays;
        QuadLayer _glow, _cord;
        readonly PuzzleDraw _p = new();
        bool _meter;
        Rect _canvas;
        Vector2 _a;
        double _rayR, _rayA, _glowSize, _glowA;
        Text _line;
        string _shown, _full;
        Color _lineColor;
        int _len;
        int _lastChars = -1;
        float _lastOpacity = -1f;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("voice", ctx, host, frame);
            var v = ctx.Rig("voice");
            if (v == null) return false;
            var pz = RJ.D(v, "puzzle");
            var meter = RJ.D(v, "meter");
            _line = ctx.VoiceLine;
            if (_line != null)
            {
                _full = _line.text ?? "";
                _len = (RJ.S(v, "line") ?? _full.Replace("\n", "")).Length;
                _lineColor = _line.color;
                _line.supportRichText = true;
            }

            _rays = Shapes("Voice_rays", true);
            _glow = GlowQuads("Voice_glow", MenuV6Linear.Dark);
            if (pz != null)
            {
                _canvas = RJ.R(pz, "canvasRect");
                _a = Off(RJ.V(pz, "a"));
                _rayR = 46;
                _rayA = .7;
                _glowSize = 90;
                _glowA = .4;
                var parts = RJ.D(pz, "parts");
                var cord = Part(RJ.D(parts, "cord"));
                if (cord != null)
                {
                    _cord = Quads("Voice_cord", cord);
                    var cr = RJ.R(RJ.D(parts, "cord"), "rect");
                    _cordRect = new Rect(_canvas.x + cr.x, _canvas.y + cr.y, cr.width, cr.height);
                }

                var a = Part(RJ.D(parts, "A"));
                var e = Part(RJ.D(parts, "emiB"));
                if (a == null) return false;
                var o = RJ.V(pz, "origin");
                var wh = RJ.V(pz, "drawWH");
                _p.Body = new Rect(_canvas.x + o.x, _canvas.y + o.y, wh.x, wh.y);
                _p.EyeC = Off(RJ.V(pz, "eyeC"));
                _p.Iris = Off(RJ.V(pz, "iris"));
                _p.Wpx = RJ.N(pz, "wpx", 50);
                _p.A = Quads("Voice_A", a);
                _p.UvA = a.ContentOf(RJ.D(parts, "A"));
                if (e != null)
                {
                    _p.Emi = Quads("Voice_emiB", e, true);
                    _p.UvE = e.ContentOf(RJ.D(parts, "emiB"));
                }
                _p.Eye = GlowQuads("Voice_eyeGlow", MenuV6Linear.Glow);
                var g = RJ.D(pz, "glint");
                var sheet = Gfx.Sheet(RJ.S(g, "file"));
                if (sheet != null)
                {
                    _p.Glint = Quads("Voice_glint", sheet, true);
                    var gr = RJ.R(g, "rect");
                    _p.GlintRect = new Rect(_canvas.x + gr.x, _canvas.y + gr.y, gr.width, gr.height);
                    _p.GlintUv = PuzzleDraw.Cells(sheet, g);
                }

                _p.Lights = GlowQuads("Voice_lights", MenuV6Linear.Glow);
                return true;
            }

            if (meter == null) return false;
            _meter = true;
            _canvas = RJ.R(meter, "canvasRect");
            _a = Off(RJ.V(meter, "a"));
            _rayR = _canvas.width * .62;
            _rayA = .55;
            _glowSize = _canvas.width * 1.2;
            _glowA = .35;
            return true;
        }

        Rect _cordRect;

        Vector2 Off(Vector2 p) => new(_canvas.x + p.x, _canvas.y + p.y);

        public override void Tick(double t)
        {
            var u = MenuV6RigMath.VoiceU(t);
            var k = MenuV6RigMath.VoiceGlow(u);
            _rays.Clear();
            _glow.Clear();
            if (k > .01)
            {
                double rx = _a.x + (_meter ? 2 : 0), ry = _a.y + (_meter ? 2 : -2);
                var a = _rayA * k;
                if (a > .005)
                    for (var q = 0; q < 20; q++)
                    {
                        MenuV6RigMath.Ray(q, _rayR, t, out var an, out var len);
                        const double w = .07;
                        var c0 = new Color(1f, 236 / 255f, 170 / 255f, MenuV6Linear.Alpha(.55 * a, MenuV6Linear.Dark));
                        var c1 = new Color(1f, 200 / 255f, 90 / 255f, 0f);
                        _rays.Tri(rx, ry, c0, rx + Math.Cos(an - w) * len, ry + Math.Sin(an - w) * len, c1,
                            rx + Math.Cos(an + w) * len, ry + Math.Sin(an + w) * len, c1);
                    }

                Spr(_glow, MenuV6Glow.Kind.Gold, _a.x, _a.y, _glowSize, _glowA * k, MenuV6Linear.Dark);
            }

            _rays.Commit();
            _glow.Commit();
            if (!_meter)
            {
                if (_cord != null)
                {
                    _cord.Clear();
                    _cord.Rect(_cordRect, QuadList.Full, Color.white);
                    _cord.Commit();
                }

                _p.Draw(t, double.NaN, .35 + 1.25 * k, MenuV6RigMath.VoiceGlint(t), -1, k > .5 ? .6 * k : 0);
            }

            Type(u);
        }

        void Type(double u)
        {
            if (_line == null) return;
            MenuV6RigMath.VoiceTyping(u, _len, out var chars, out var op);
            if (chars == _lastChars && Mathf.Abs((float)op - _lastOpacity) < 1e-4f) return;
            _lastChars = chars;
            _lastOpacity = (float)op;
            // the hidden rest keeps its layout (wrldz.css:237 opacity 0): the same string, the rest at alpha 0
            var cut = Cut(_full, chars);
            _shown = cut >= _full.Length ? _full : _full.Substring(0, cut) + "<color=#00000000>" + _full.Substring(cut) + "</color>";
            _line.text = _shown;
            var c = _lineColor;
            c.a *= MenuV6Linear.Alpha(op);
            _line.color = c;
        }

        /// <summary>The string index after <paramref name="chars"/> characters of the line (a layout "\n" is not one).</summary>
        static int Cut(string s, int chars)
        {
            var n = 0;
            for (var i = 0; i < s.Length; i++)
            {
                if (n >= chars) return i;
                if (s[i] != '\n') n++;
            }

            return s.Length;
        }
    }

    // ====================================================================== Nut (core.js:476-489)

    /// <summary>Nut's 42 twinkling stars (sin¹²) and the sun's 30 s journey across the sky band (all additive).</summary>
    public sealed class NutRig : MenuV6Rig
    {
        QuadLayer _q;
        readonly List<(double x, double y, double r, double tw)> _stars = new();
        double _inX, _inY, _outX, _outY, _top, _side, _R, _W, _hs;
        const float G = MenuV6Linear.Dark;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("nut", ctx, host, frame);
            var n = ctx.Rig("nut");
            var stars = RJ.L(n, "stars");
            if (stars == null) return false;
            foreach (var s in stars)
                if (s is List<object> a && a.Count >= 4)
                    _stars.Add((RJ.At(a, 0), RJ.At(a, 1), RJ.At(a, 2), RJ.At(a, 3)));
            var si = RJ.V(n, "sunIn");
            var so = RJ.V(n, "sunOut");
            _inX = si.x;
            _inY = si.y;
            _outX = so.x;
            _outY = so.y;
            _top = RJ.N(n, "top", 112);
            _side = RJ.N(n, "side", 40);
            _R = RJ.N(n, "R", 170);
            _W = RJ.N(n, "W", 1920);
            _hs = RJ.N(n, "hs", 0);
            _q = GlowQuads("Nut_twinkle", G);
            return true;
        }

        public override void Tick(double t)
        {
            _q.Clear();
            foreach (var (x, y, r, tw) in _stars)
            {
                var f = MenuV6RigMath.Twinkle(t, tw);
                if (f > .02) Spr(_q, MenuV6Glow.Kind.GoldHot, x, y, r * 7, f * .8, G);
            }

            MenuV6RigMath.Sun(t, _inX, _inY, _outX, _outY, _top, _side, _R, _W, out var sx, out var sy, out var vis, out var ph);
            Spr(_q, MenuV6Glow.Kind.Fire, sx, sy, _top * 2.2, .22 * vis, G);
            Spr(_q, MenuV6Glow.Kind.GoldHot, sx, sy, _top * .7, .28 * vis, G);
            var br = .5 + .5 * Math.Sin(t * .8);
            if (_hs > 0) Spr(_q, MenuV6Glow.Kind.FireDeep, _inX, _inY, _hs * .9, (.25 + .15 * br) * (1 - vis * .5), G);
            Spr(_q, MenuV6Glow.Kind.Fire, _outX, _outY, (_hs > 0 ? _hs : 70) * 1.1,
                .2 + .2 * MenuV6Math.SStep(.85, 1, ph) + .1 * br, G);
            _q.Commit();
        }
    }

    // ====================================================================== the Ring (relics.js:333-374)

    /// <summary>
    /// drawRingSpectral from the exported parts (ring, pointer, membrane, rim arc): the blue aura, the membrane, the
    /// ring, the travelling rim arc, the found notches, the motes, the 5 pointers with their tips, and (the niche) the
    /// bearing rays. Shared by the Ring echo and the WORLD MAP niche.
    /// </summary>
    public sealed class RingDraw
    {
        public double X, Y, S, R, r;
        public double[] Notch;
        public RigTex RingTex, PointerTex, MembraneTex, RimTex;
        IRigGfx _gfx;
        public double RingHalf, MemHalf, RimHalf, PtrW, PtrH, PtrPad, PtrWid, PtrLen;
        public Rect UvRing = QuadList.Full, UvPtr = QuadList.Full, UvMem = QuadList.Full, UvRim = QuadList.Full;
        public QuadLayer Glow1, Membrane, RingQ, Rim, Glow2, Pointers, Glow3;
        public ShapeLayer Rays;
        public float Gamma = MenuV6Linear.RingBlue;

        /// <summary>P2 (the P1b landing review: the Ring echo's bright blue pointer tips and its white specular arc were lost):
        /// the page ADDS these small highlights over the bright ring and pointers, so they saturate to white; drawn "over"
        /// in Linear light at the page's alpha (or its a^g) they stay dim. They take a plain gain instead (1 = the P1b look,
        /// the WORLD MAP niche's Ring; 2.2 = the echo, the title's Ring and its flight: the tips' / rim's peaks back at
        /// ~235-240 against the still's 245-255, the region's mean unchanged).</summary>
        public double TipGain = 1, RimGain = 1;

        /// <summary>The echo's / the title Ring's highlight gain (TipGain, RimGain).</summary>
        public const double EchoBright = 2.2;

        Color Bright(double a, double gain)
        {
            if (gain == 1 || MenuV6Linear.Off) return W(a);
            return new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a * gain));
        }

        void SprB(QuadLayer q, MenuV6Glow.Kind k, double x, double y, double size, double a, double gain)
        {
            if (gain == 1 || MenuV6Linear.Off)
            {
                Spr(q, k, x, y, size, a);
                return;
            }

            if (a <= .003) return;
            q.Sprite(x, y, size, size, MenuV6Glow.Uv(k), new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a * gain)));
        }

        public struct State
        {
            public double A, I, Seed, Sweep, Aura;
            public int Found, Motes;
            public double[] Rot, PivX, PivY, Glow;
            public RayTo[] RaysTo;
        }

        public struct RayTo
        {
            public int I;
            public double X, Y, A;
            public bool Gold;
        }

        public static RingDraw From(IRigGfx gfx, object host, Rect frame, Dictionary<string, object> parts, string name)
        {
            var d = new RingDraw { _gfx = gfx };
            RigTex P(string part)
            {
                var f = RJ.S(RJ.D(parts, part), "file");
                return string.IsNullOrEmpty(f) ? null : gfx.Sprite(f);
            }

            var ring = P("ring");
            var ptr = P("pointer");
            if (ring == null || ptr == null) return null;
            d.RingTex = ring;
            d.PointerTex = ptr;
            d.MembraneTex = P("membrane");
            d.RimTex = P("rim_arc");
            d.RingHalf = RJ.N(RJ.D(parts, "ring"), "half");
            d.UvRing = ring.ContentOf(RJ.D(parts, "ring"));
            d.UvPtr = ptr.ContentOf(RJ.D(parts, "pointer"));
            if (d.MembraneTex != null) d.UvMem = d.MembraneTex.ContentOf(RJ.D(parts, "membrane"));
            if (d.RimTex != null) d.UvRim = d.RimTex.ContentOf(RJ.D(parts, "rim_arc"));
            var p = RJ.D(parts, "pointer");
            var sz = RJ.L(p, "size");
            var S = RJ.N(p, "S", 3);
            d.PtrW = RJ.At(sz, 0) / S;
            d.PtrH = RJ.At(sz, 1) / S;
            d.PtrPad = RJ.N(p, "pad");
            d.PtrWid = RJ.N(p, "wid");
            d.PtrLen = RJ.N(p, "len");
            d.MemHalf = RJ.At(RJ.L(RJ.D(parts, "membrane"), "centre"), 0);
            d.RimHalf = RJ.At(RJ.L(RJ.D(parts, "rim_arc"), "centre"), 0);
            QuadLayer Q(string n, RigTex t)
            {
                var l = gfx.Quads(host, name + "_" + n, frame, t);
                l.Add = true;   // relics.js drawRingSpectral: every op 'lighter'
                return l;
            }
            d.Glow1 = Q("aura", null);
            if (d.MembraneTex != null) d.Membrane = Q("membrane", d.MembraneTex);
            d.RingQ = Q("ring", d.RingTex);
            if (d.RimTex != null) d.Rim = Q("rim", d.RimTex);
            d.Glow2 = Q("motes", null);
            d.Rays = gfx.Shapes(host, name + "_rays", frame);
            d.Rays.Add = true;
            d.Pointers = Q("pointers", d.PointerTex);
            d.Glow3 = Q("tips", null);
            return d;
        }

        public void SetGamma(float g)
        {
            Gamma = g;
            var atlas = _gfx.Glow(g);
            Glow1.Tex = atlas;
            Glow2.Tex = atlas;
            Glow3.Tex = atlas;
        }

        void Spr(QuadLayer q, MenuV6Glow.Kind k, double x, double y, double size, double a)
        {
            if (a <= .003) return;
            q.Sprite(x, y, size, size, MenuV6Glow.Uv(k), new Color(1, 1, 1, MenuV6Linear.Alpha(Math.Min(1, a), Gamma)));
        }

        Color W(double a) => new(1, 1, 1, MenuV6Linear.Alpha(MenuV6MathF.Clamp01(a), Gamma));

        public void Draw(double t, State st)
        {
            Glow1.Clear();
            Membrane?.Clear();
            RingQ.Clear();
            Rim?.Clear();
            Glow2.Clear();
            Rays.Clear();
            Pointers.Clear();
            Glow3.Clear();
            var a = st.A;
            if (a > .01)
            {
                var shim = MenuV6RigMath.RingShim(t, st.Seed);
                var I = st.I * a * shim;
                var aura = st.Aura;
                Spr(Glow1, MenuV6Glow.Kind.Blue, X, Y, R * (4.4 + .6 * aura), (.14 + .12 * aura) * I);
                Spr(Glow1, MenuV6Glow.Kind.GoldHot, X, Y + r * .1, r * 1.7, .1 * I);
                Membrane?.Sprite(X, Y, 2 * MemHalf, 2 * MemHalf, UvMem, W(I));
                RingQ.Sprite(X, Y, 2 * RingHalf, 2 * RingHalf, UvRing, W(I));
                if (I > 1) RingQ.Sprite(X, Y, 2 * RingHalf, 2 * RingHalf, UvRing, W(I - 1));
                for (var pass = 0; pass < 2; pass++)
                {
                    double sp2 = pass == 0 ? .55 : -.9, amp = pass == 0 ? .55 : .35 * st.Sweep;
                    if (amp <= .01) continue;
                    var an = t * sp2 + st.Seed * 2;
                    Rim?.Sprite(X, Y, 2 * RimHalf, 2 * RimHalf, UvRim, Bright(amp * I, RimGain), an);
                    SprB(Glow2, MenuV6Glow.Kind.BlueHot, X + Math.Cos(an + .7) * R, Y + Math.Sin(an + .7) * R,
                        14 * Math.Max(.6, S), .7 * amp * I, RimGain);
                }

                if (Notch != null)
                    for (var i = 0; i < Notch.Length && i < st.Found; i++)
                    {
                        var an = Notch[i] * Math.PI / 180;
                        var rr = (48 + 38) / 2.0 * S;
                        double nx = X + Math.Cos(an) * rr, ny = Y + Math.Sin(an) * rr;
                        Spr(Glow2, MenuV6Glow.Kind.Blue, nx, ny, 20 * Math.Max(.5, S), (.55 + .2 * Math.Sin(t * 2 + i)) * a);
                        Spr(Glow2, MenuV6Glow.Kind.BlueHot, nx, ny, 7 * Math.Max(.5, S), .8 * a);
                    }

                for (var k = 0; k < st.Motes; k++)
                {
                    MenuV6RigMath.RingMote(k, st.Motes, t, st.Seed, R, out var dx, out var dy, out var ma);
                    Spr(Glow2, k % 3 != 0 ? MenuV6Glow.Kind.BlueHot : MenuV6Glow.Kind.GoldHot, X + dx, Y + dy,
                        (5 + 4 * (k % 2)) * Math.Max(.5, S), ma * .7 * I);
                }

                if (st.Rot != null)
                {
                    if (st.RaysTo != null)
                        foreach (var ry in st.RaysTo)
                            DrawRay(t, st, ry, a);
                    for (var i = 0; i < st.Rot.Length; i++)
                    {
                        var rot = st.Rot[i];
                        double px = st.PivX[i], py = st.PivY[i];
                        Pointers.Pivoted(px, py, -(PtrPad + PtrWid / 2), -PtrPad, PtrW, PtrH, UvPtr, W(.95 * I), rot);
                        var tg = st.Glow != null && i < st.Glow.Length ? st.Glow[i] : 0;
                        double tx = px - Math.Sin(rot) * PtrLen, ty = py + Math.Cos(rot) * PtrLen;
                        SprB(Glow3, MenuV6Glow.Kind.BlueHot, tx, ty, 9 * Math.Max(.6, S), (.35 + .65 * tg) * a, TipGain);
                        if (tg > .01) Spr(Glow3, MenuV6Glow.Kind.Blue, tx, ty, 30 * Math.Max(.6, S), .7 * tg * a);
                    }
                }
            }

            Glow1.Commit();
            Membrane?.Commit();
            RingQ.Commit();
            Rim?.Commit();
            Glow2.Commit();
            Rays.Commit();
            Pointers.Commit();
            Glow3.Commit();
        }

        /// <summary>relics.js:358-364 one bearing ray: a blurred stroke (CSS blur, here a soft wide stroke), a dashed line
        /// with the ray's gradient crawling at 40 px/s, a travelling glint and the end glow.</summary>
        void DrawRay(double t, State st, RayTo ry, double a)
        {
            if (ry.A <= .01 || ry.I < 0 || ry.I >= st.Rot.Length) return;
            var rot = st.Rot[ry.I];
            double tx = st.PivX[ry.I] - Math.Sin(rot) * PtrLen, ty = st.PivY[ry.I] + Math.Cos(rot) * PtrLen;
            double ex = ry.X, ey = ry.Y;
            var d = Math.Sqrt((ex - tx) * (ex - tx) + (ey - ty) * (ey - ty));
            if (d < 4) return;
            var A = ry.A * a;
            Color c0 = ry.Gold ? new Color(1f, 226 / 255f, 150 / 255f) : new Color(191 / 255f, 230 / 255f, 1f);
            Color c1 = ry.Gold ? new Color(1f, 196 / 255f, 90 / 255f) : new Color(170 / 255f, 120 / 255f, 1f);
            Color G(double u)
            {
                // stops 0: c0 .85 A, .7: c1 .5 A, 1: c1 .15 A
                Color c;
                double al;
                if (u < .7)
                {
                    var f = u / .7;
                    c = Color.Lerp(c0, c1, (float)f);
                    al = (.85 + (.5 - .85) * f) * A;
                }
                else
                {
                    var f = (u - .7) / .3;
                    c = c1;
                    al = (.5 + (.15 - .5) * f) * A;
                }

                c.a = MenuV6Linear.Alpha(MenuV6MathF.Clamp01(al), Gamma);
                return c;
            }

            // the blurred stroke: CSS blur max(1.5, 2.4 s) of a max(3, 4.5 s) line ≈ a soft line of that width, feathered
            // by about two blur radii, at ~.6 of its alpha (recorded approximation: no blur filter in UGUI)
            var blur = Math.Max(1.5, 2.4 * S);
            var w0 = Math.Max(3, 4.5 * S);
            var keep = Rays.Feather;
            Rays.Feather = (float)(blur * 1.6);
            Seg(tx, ty, ex, ey, 0, .7, w0 * .6, G, .6);
            Seg(tx, ty, ex, ey, .7, 1, w0 * .6, G, .6);
            Rays.Feather = keep;
            // the dashed line
            var w1 = Math.Max(.9, 1.1 * S);
            var on = Math.Max(3, 5 * S);
            var off = Math.Max(3, 4 * S);
            var per = on + off;
            var o = MenuV6RigMath.Mod(-t * 40 * Math.Max(.5, S), per);
            for (var k = 0; ; k++)
            {
                var s0 = k * per - o;
                if (s0 >= d) break;
                var s1 = Math.Min(d, s0 + on);
                if (s1 <= 0) continue;
                s0 = Math.Max(0, s0);
                Seg(tx, ty, ex, ey, s0 / d, s1 / d, w1, G, 1);
            }

            var ph = MenuV6RigMath.RayGlint(t, ry.I);
            double qx = tx + (ex - tx) * ph, qy = ty + (ey - ty) * ph;
            Spr(Glow3, ry.Gold ? MenuV6Glow.Kind.GoldHot : MenuV6Glow.Kind.BlueHot, qx, qy, 10 * Math.Max(.6, S),
                .8 * A * Math.Sin(ph * Math.PI));
            Spr(Glow3, ry.Gold ? MenuV6Glow.Kind.Gold : MenuV6Glow.Kind.Blue, ex, ey, 26 * Math.Max(.6, S), .5 * A);
        }

        void Seg(double x0, double y0, double x1, double y1, double u0, double u1, double w, Func<double, Color> grad, double k)
        {
            var c0 = grad(u0);
            var c1 = grad(u1);
            c0.a *= (float)k;
            c1.a *= (float)k;
            Rays.Line(x0 + (x1 - x0) * u0, y0 + (y1 - y0) * u0, x0 + (x1 - x0) * u1, y0 + (y1 - y0) * u1, w, c0, c1);
        }
    }

    /// <summary>The Ring echo over the door (menu.js:768-773): drawRingSpectral at i .72 (+ .2 on hover), 2 notches found,
    /// 14 motes, the pointers swaying ±1.5°, and the P1a refraction still moved by the page's wobble.</summary>
    public sealed class RingEchoRig : MenuV6Rig
    {
        RingDraw _d;
        RectTransform _refract;
        Vector2 _refractBase;
        double _seed = 1.3;
        readonly double[] _rot = new double[5], _px = new double[5], _py = new double[5], _glow = new double[5];

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("ringEcho", ctx, host, frame);
            var r = ctx.Rig("ringEcho");
            _d = RingDraw.From(ctx.Gfx, host, frame, RJ.D(r, "parts"), "RingEcho");
            if (_d == null) return false;
            _d.SetGamma(MenuV6Linear.RingBlue);
            _d.TipGain = _d.RimGain = RingDraw.EchoBright;
            var c = RJ.V(r, "centre");
            _d.X = c.x;
            _d.Y = c.y;
            _d.S = RJ.N(r, "s", 1);
            _d.R = RJ.N(r, "R", 48 * _d.S);
            _d.r = RJ.N(r, "r", 38 * _d.S);
            var notch = RJ.L(r, "notch");
            if (notch != null)
            {
                _d.Notch = new double[notch.Count];
                for (var i = 0; i < notch.Count; i++) _d.Notch[i] = RJ.At(notch, i);
            }

            var piv = RJ.L(r, "piv");
            for (var i = 0; i < 5 && piv != null && i < piv.Count; i++)
            {
                _px[i] = RJ.At(piv[i] as List<object>, 0);
                _py[i] = RJ.At(piv[i] as List<object>, 1);
            }

            _seed = RJ.N(RJ.D(r, "state"), "seed", 1.3);
            return true;
        }

        /// <summary>The P1a refraction still (the stack below seen through the annulus) that the wobble moves.</summary>
        public void Refraction(Graphic g)
        {
            if (g == null) return;
            _refract = g.rectTransform;
            _refractBase = _refract.offsetMin;
        }

        public override void Tick(double t)
        {
            var hovR = Ctx.Ring != null ? MenuV6RigMath.Clamp01(Ctx.Ring.Value(t)) : 0;
            for (var i = 0; i < 5; i++) _rot[i] = MenuV6RigMath.RingRot(t, i, hovR);
            _d.Draw(t, new RingDraw.State
            {
                A = 1, I = .72 + .2 * hovR, Seed = _seed, Sweep = 0, Aura = Math.Min(1.4, Math.Max(0, .4 * hovR)), Found = 2,
                Motes = 14, Rot = _rot, PivX = _px, PivY = _py, Glow = _glow
            });
            if (_refract != null)
            {
                MenuV6RigMath.RingRefract(t, _d.S, _seed, out var dx, out var dy);
                MenuV6RigMath.RingRefract(2.8, _d.S, _seed, out var dx0, out var dy0);
                var off = new Vector2((float)(dx - dx0), (float)-(dy - dy0));
                _refract.offsetMin = _refractBase + off;
                _refract.offsetMax = _refractBase + off;
            }
        }
    }

    // ====================================================================== the torch (anime_menu_v6.js:289-297, menu.js:669)

    /// <summary>The torch flame: its fire glow and the 4 flat tongues swaying (the page's path, flattened and filled);
    /// with the P1a Stone A/B flicker and the pool this is the torch's visible flicker.</summary>
    public sealed class FlameRig : MenuV6Rig
    {
        QuadLayer _glow;
        ShapeLayer _tongues;
        double _x, _y, _sc;
        readonly double[] _xs = new double[64], _ys = new double[64];

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("flame", ctx, host, frame);
            var f = ctx.Rig("flame");
            if (f == null) return false;
            var torch = RJ.D(f, "torch");
            _x = RJ.N(torch, "x");
            _y = RJ.N(torch, "y");
            _sc = RJ.N(f, "sc", 1.9);
            _glow = GlowQuads("Flame_glow", MenuV6Linear.Torch);
            _tongues = Shapes("Flame_tongues");
            _tongues.Feather = .8f;
            return true;
        }

        public override void Tick(double t)
        {
            _glow.Clear();
            _tongues.Clear();
            Spr(_glow, MenuV6Glow.Kind.Fire, _x, _y - 10 * _sc, 96 * _sc, .34, MenuV6Linear.Torch);
            foreach (var (k, rgb, ph) in MenuV6RigMath.Tongues)
            {
                var n = MenuV6RigMath.Tongue(_x, _y, t, 9 * _sc * k + 2 * _sc, 34 * _sc * k + 4 * _sc, ph, 6, _xs, _ys);
                _tongues.Poly(_xs, _ys, n, Col(rgb, 1));
            }

            _glow.Commit();
            _tongues.Commit();
        }
    }

    /// <summary>The 30 gold motes rising round the torch (menu.js:305 table, :669 draw), additive.</summary>
    public sealed class MotesRig : MenuV6Rig
    {
        QuadLayer _q;
        readonly List<(double x, double y, double v, double sw, double s, double life, double off)> _m = new();
        double _tx, _ty, _k;
        const float G = MenuV6Linear.Glow;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("motes", ctx, host, frame);
            var m = ctx.Rig("motes");
            var list = RJ.L(m, "motes");
            if (list == null) return false;
            foreach (var o in list)
                _m.Add((RJ.N(o, "x"), RJ.N(o, "y"), RJ.N(o, "v"), RJ.N(o, "sw"), RJ.N(o, "s"), RJ.N(o, "life", 1), RJ.N(o, "off")));
            var torch = RJ.D(m, "torch");
            _tx = RJ.N(torch, "x");
            _ty = RJ.N(torch, "y");
            _k = RJ.N(m, "k", ctx.K);
            _q = GlowQuads("Motes", G);
            return true;
        }

        public override void Tick(double t)
        {
            _q.Clear();
            foreach (var mo in _m)
            {
                MenuV6RigMath.Mote(t, mo.x, mo.y, mo.v, mo.sw, mo.life, mo.off, _tx, _ty, _k, out var x, out var y, out var a);
                Spr(_q, MenuV6Glow.Kind.Gold, x, y, 6 * mo.s * _k, a, G);
            }

            _q.Commit();
        }
    }

    // ====================================================================== the breach glow (menu_v41.js:57-59)

    /// <summary>The breach's crack glow and fissure glow flickering with fl (the rest of the breach stays the P1a still).</summary>
    public sealed class BreachGlowRig : MenuV6Rig
    {
        QuadLayer _crack, _fissure;
        Rect _cr, _fr;
        float _gain = 1f;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("breachGlow", ctx, host, frame);
            var b = ctx.Rig("breachGlow");
            var parts = RJ.D(b, "parts");
            var c = Part(RJ.D(parts, "crack_glow"));
            var f = Part(RJ.D(parts, "fissure_glow"));
            if (c == null && f == null) return false;
            if (c != null)
            {
                _crack = Quads("Breach_crackGlow", c, true);
                _cr = RJ.R(RJ.D(parts, "crack_glow"), "rect");
            }

            if (f != null)
            {
                _fissure = Quads("Breach_fissureGlow", f, true);
                _fr = RJ.R(RJ.D(parts, "fissure_glow"), "rect");
            }

            _gain = MenuV6Linear.LayerGain("breach_glow_add");
            return true;
        }

        public override void Tick(double t)
        {
            var fl = MenuV6RigMath.BreachFl(t);
            if (_crack != null)
            {
                _crack.Clear();
                _crack.Rect(_cr, QuadList.Full, new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(MenuV6RigMath.CrackGlowAlpha(fl) * _gain)));
                _crack.Commit();
            }

            if (_fissure != null)
            {
                _fissure.Clear();
                _fissure.Rect(_fr, QuadList.Full, new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(MenuV6RigMath.FissureGlowAlpha(fl) * _gain)));
                _fissure.Commit();
            }
        }
    }

    // ====================================================================== DOM hover chrome (anime_v6.css:59, :64-65)

    /// <summary>The QUIT plate and the niche frame on hover: the hover sprite over the rest one (QUIT crossfades over the
    /// page's .2 s box-shadow transition and its label turns #FFF3C4; the niche frame swaps at once).</summary>
    public sealed class DomHoverRig : MenuV6Rig
    {
        Graphic _hover;
        IRigPointer _p;
        Text _label;
        Color _labelRest, _labelHover;
        double _fade;
        bool _wasOver;
        double _t0 = double.NegativeInfinity;
        double _from;

        public static DomHoverRig For(string name, RigContext ctx, IRigPointer p, Graphic rest, Graphic hover, double fade,
            Text label = null, Color? labelHover = null)
        {
            if (p == null || hover == null) return null;
            // the hover sprite lies over the rest one (the rest stays drawn under it, as the page's background swap)
            var r = new DomHoverRig { _p = p, _hover = hover, _fade = fade, _label = label };
            _ = rest;
            r.Init(name, ctx, null, Rect.zero);
            if (label != null)
            {
                r._labelRest = label.color;
                r._labelHover = labelHover ?? label.color;
                r._labelHover.a = label.color.a;
            }

            SetA(hover, 0);
            return r;
        }

        public override bool Build(RigContext ctx, object host, Rect frame) => true;

        public override bool EveryFrame => true;

        /// <param name="tMenu">menu time (unused: the CSS transition runs on wall-clock time)</param>
        public override void Tick(double tMenu)
        {
            var t = Ctx.RealTime();
            var over = _p != null && _p.Over;
            if (over != _wasOver)
            {
                _from = Current(t);
                _wasOver = over;
                _t0 = t;
                if (_label != null) _label.color = over ? _labelHover : _labelRest;
            }

            SetA(_hover, Current(t));
        }

        double Current(double t)
        {
            var target = _wasOver ? 1.0 : 0.0;
            if (_fade <= 0) return target;
            var k = MenuV6RigMath.Ease((t - _t0) / _fade);
            return _from + (target - _from) * k;
        }

        static void SetA(Graphic g, double a)
        {
            if (g == null) return;
            var c = g.color;
            var na = (float)MenuV6MathF.Clamp01(a);
            if (Mathf.Abs(c.a - na) < 1e-4f) return;
            c.a = na;
            g.color = c;
        }
    }
}
