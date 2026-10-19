using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The Puzzle-lock of every v6.3 sheet (BUILD_PLAN §6 P3 steps 2-3; menu.js:832-897 drawKeystone): the keystone's 8 pieces
    /// part, fly up to the sheet's clasp and become its jewel; on the close the jewel breaks, the pieces fly home and lock with
    /// the flash. The ops come from <see cref="SheetMath.Keystone"/> (checked against the page's trace); this draws them with
    /// the P1b keystone bakes (rigs_&lt;layout&gt;.json keystone: A / B / emiB / shadow / glint, scaled about its anchor) and the
    /// P2 piece sprites (coldopen_&lt;layout&gt;.json bakes.PK: c / s / e), on UI/Default in stage px. While it runs the Stone's
    /// own keystone is hidden (<see cref="MenuV6Stage.SetKeystoneShown"/>), and it hands it back at the sheet's end.
    /// </summary>
    public sealed class PuzzleLock
    {
        readonly SheetMath.Pk _pk = new();
        readonly List<SheetMath.Op> _ops = new();
        Vector2 _K, _origin, _wh, _eyeC, _iris;
        Rect _shadowRect, _glintRect;
        double _wpx, _k1 = 1;
        double[] _seamX, _seamY;
        QuadLayer _shadow, _a, _b, _emi, _glint, _eyeGlow, _lights, _glowFx;
        Rect _uvA = QuadList.Full, _uvB = QuadList.Full, _uvE = QuadList.Full, _uvS = QuadList.Full;
        List<Rect> _glintUv = new();
        readonly List<(QuadLayer s, QuadLayer c, QuadLayer e)> _pieces = new();
        double _pkW, _pkH;
        ShapeLayer _rays, _flash;
        (double a, double v, double s)[] _embers;
        public double[] Noise;

        public bool Ok => _a != null && _pieces.Count == 8;

        /// <summary>Build into <paramref name="host"/> (a node covering the stage rect <paramref name="frame"/>). Null when the
        /// keystone or piece bakes are missing (the sheet then opens without its lock; never a throw).</summary>
        public static PuzzleLock Build(IRigGfx gfx, Transform host, Rect frame, MenuLayoutKind kind, RigContext ctx)
        {
            var land = kind == MenuLayoutKind.Landscape;
            var rigs = ctx != null && ctx.Rigs != null ? ctx.Rigs : RJ.D(CrackData.Json(land ? "rigs_land" : "rigs_port"), "rigs");
            var ks = RJ.D(rigs, "keystone");
            var pk = RJ.D(RJ.D(CrackData.Json(land ? "coldopen_land" : "coldopen_port"), "bakes"), "PK");
            if (ks == null || pk == null || gfx == null || host == null) return null;
            var L = new PuzzleLock { _k1 = land ? 1 : 1.4 };
            if (ctx != null) L.Noise = ctx.Noise;
            L._K = RJ.V(ks, "K");
            L._origin = RJ.V(ks, "origin");
            L._wh = RJ.V(ks, "drawWH");
            L._eyeC = RJ.V(ks, "eyeC");
            L._iris = RJ.V(ks, "iris");
            L._wpx = RJ.N(ks, "wpx", 96);
            L._shadowRect = RJ.R(ks, "shadowRect");
            var seam = RJ.L(ks, "seamPts");
            if (seam != null && seam.Count >= 2)
            {
                L._seamX = new double[seam.Count];
                L._seamY = new double[seam.Count];
                for (var i = 0; i < seam.Count; i++)
                {
                    L._seamX[i] = RJ.At(seam[i] as List<object>, 0);
                    L._seamY[i] = RJ.At(seam[i] as List<object>, 1);
                }
            }

            var p = L._pk;
            p.K1 = L._k1;
            p.S = RJ.N(pk, "S", 2);
            var K = RJ.V(pk, "K");
            var org = RJ.V(pk, "origin");
            var cen = RJ.V(pk, "centre");
            p.Kx = K.x;
            p.Ky = K.y;
            p.OriginDx = K.x - org.x;
            p.OriginDy = K.y - org.y;
            p.CentreDx = cen.x - org.x;
            p.CentreDy = cen.y - org.y;
            L._pkW = RJ.N(pk, "W") / p.S;
            L._pkH = RJ.N(pk, "H") / p.S;

            var parts = RJ.D(ks, "parts");
            RigTex Part(string n) => gfx.Sprite(RJ.S(RJ.D(parts, n), "file"));
            var a = Part("A");
            if (a == null) return null;
            var sh = Part("shadow");
            if (sh != null)
            {
                L._shadow = gfx.Quads(host, "Lock_shadow", frame, sh);
                L._uvS = sh.ContentOf(RJ.D(parts, "shadow"));
            }

            // the pieces under the whole Puzzle (they never show together)
            foreach (var po in RJ.L(pk, "pieces") ?? new List<object>())
            {
                var c = gfx.Sprite(RJ.S(po, "c"));
                if (c == null) continue;
                var s = gfx.Sprite(RJ.S(po, "s"));
                var e = gfx.Sprite(RJ.S(po, "e"));
                var cb = RJ.V(po, "cb");
                var dir = RJ.V(po, "dir");
                p.Pieces.Add((cb.x, cb.y, dir.x, dir.y, RJ.N(po, "h"), RJ.N(po, "h2")));
                var qs = s != null ? gfx.Quads(host, "Lock_piece_s", frame, s) : null;
                var qc = gfx.Quads(host, "Lock_piece_c", frame, c);
                QuadLayer qe = null;
                if (e != null)
                {
                    qe = gfx.Quads(host, "Lock_piece_e", frame, e);
                    qe.Add = true;
                }

                L._pieces.Add((qs, qc, qe));
            }

            L._a = gfx.Quads(host, "Lock_A", frame, a);
            L._uvA = a.ContentOf(RJ.D(parts, "A"));
            var b = Part("B");
            if (b != null)
            {
                L._b = gfx.Quads(host, "Lock_B", frame, b);
                L._uvB = b.ContentOf(RJ.D(parts, "B"));
            }

            var em = Part("emiB");
            if (em != null)
            {
                L._emi = gfx.Quads(host, "Lock_emiB", frame, em);
                L._emi.Add = true;
                L._uvE = em.ContentOf(RJ.D(parts, "emiB"));
            }

            L._eyeGlow = gfx.Quads(host, "Lock_eyeGlow", frame, gfx.Glow(MenuV6Linear.Glow));
            var g = RJ.D(ks, "glint");
            var sheet = gfx.Sheet(RJ.S(g, "file"));
            if (sheet != null)
            {
                L._glint = gfx.Quads(host, "Lock_glint", frame, sheet);
                L._glint.Add = true;
                L._glintRect = RJ.R(g, "rect");
                L._glintUv = PuzzleDraw.Cells(sheet, g);
            }

            L._lights = gfx.Quads(host, "Lock_lights", frame, gfx.Glow(MenuV6Linear.Glow));
            L._glowFx = gfx.Quads(host, "Lock_glows", frame, gfx.Glow(MenuV6Linear.Glow));
            L._rays = gfx.Shapes(host, "Lock_rays", frame);
            L._rays.Add = true;
            L._flash = gfx.Shapes(host, "Lock_flash", frame);
            L._flash.Add = true;
            L._embers = LockFx.Embers(land, L._k1);
            return L.Ok ? L : null;
        }

        /// <summary>The ops of this frame: <see cref="SheetMath.Keystone"/> with this Stone's PK geometry.</summary>
        public void Frame(SheetKind kind, double u, double v, Vector2 clasp, double sc, double cardFlare, double eyeIdle, double t)
        {
            SheetMath.Keystone(_pk, kind, u, v, clasp.x, clasp.y, sc, cardFlare, eyeIdle, t, _ops);
            Draw(t);
        }

        /// <summary>A jewel resting at a clasp with no keystone flight (a frame over the map: there is no Stone to come from).</summary>
        public void Jewel(Vector2 clasp, double sc, double us, double t)
        {
            _ops.Clear();
            if (us >= .3)
            {
                _pk.CentreAt(clasp.x, clasp.y, sc, out var fx, out var fy);
                var a = MenuV6Math.Seg(us, .3, .44);
                if (us >= 1.06)
                    _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Rays, X = fx, Y = fy, R = 76 * _k1, A = .3 + .1 * Math.Sin(t * 1.4) });
                _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Intact, X = clasp.x, Y = clasp.y, S = sc, A = a, Eye = .5 + .08 * Math.Sin(t * 2) });
                _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Flash, X = fx, Y = fy, R = 22 * _k1, K = MenuV6Math.Seg(us, .3, .42) });
                if (us >= 1.06)
                    _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Flash, X = fx, Y = fy, R = 22 * _k1, K = MenuV6Math.Seg(us, 1.06, 1.18) });
            }

            Draw(t);
        }

        /// <summary>The clasp's jewel closing with no keystone to fly home to (a frame over the map): it gathers (rays, a 4 %
        /// squash), then bursts in its lock flash and embers. <paramref name="vh"/> = close sheet time after the clasp hold.</summary>
        public void JewelClose(Vector2 clasp, double sc, double vh, double t)
        {
            _ops.Clear();
            _pk.CentreAt(clasp.x, clasp.y, sc, out var fx, out var fy);
            if (vh < .22)
            {
                _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Rays, X = fx, Y = fy, R = 76 * _k1, A = .5 });
                _ops.Add(new SheetMath.Op
                {
                    Kind = SheetMath.OpKind.Intact, X = clasp.x, Y = clasp.y, S = sc * (1 - .04 * MenuV6Math.InOut(MenuV6Math.Seg(vh, .1, .22))),
                    A = 1, Eye = 1.5
                });
            }
            else if (vh < .9)
            {
                _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Flash, X = fx, Y = fy, R = 22 * _k1, K = MenuV6Math.Seg(vh, .22, .34) });
                _ops.Add(new SheetMath.Op { Kind = SheetMath.OpKind.Embers, X = fx, Y = fy, K = MenuV6Math.Seg(vh, .22, .8) });
            }

            Draw(t);
        }

        /// <summary>The lock is drawing the keystone at the door this frame (the Stone's own keystone must stay hidden).</summary>
        public bool DrawsKeystone { get; private set; }

        /// <summary>Clear everything (the sheet is gone).</summary>
        public void Clear()
        {
            _ops.Clear();
            Draw(0);
        }

        void Draw(double t)
        {
            _shadow?.Clear();
            _a.Clear();
            _b?.Clear();
            _emi?.Clear();
            _eyeGlow.Clear();
            _glint?.Clear();
            _lights.Clear();
            _glowFx.Clear();
            _rays.Clear();
            _flash.Clear();
            foreach (var (s, c, e) in _pieces)
            {
                s?.Clear();
                c.Clear();
                e?.Clear();
            }

            DrawsKeystone = false;
            foreach (var o in _ops)
            {
                switch (o.Kind)
                {
                    case SheetMath.OpKind.Intact:
                        Intact(o.X, o.Y, o.S, o.A, o.Eye, t);
                        if (Math.Abs(o.X - _K.x) < .5 && Math.Abs(o.Y - _K.y) < .5) DrawsKeystone = true;
                        break;
                    case SheetMath.OpKind.Piece:
                        Piece(o.I, o.X, o.Y, o.Rot, o.S, o.A, o.Th, o.Eye);
                        DrawsKeystone = true;
                        break;
                    case SheetMath.OpKind.Spr:
                        LockFx.Spr(_glowFx, Kind(o.G), o.X, o.Y, o.R, o.A);
                        break;
                    case SheetMath.OpKind.Rays:
                        LockFx.Rays(_rays, o.X, o.Y, o.R, o.A, t);
                        break;
                    case SheetMath.OpKind.Flash:
                        LockFx.Flash(_flash, _glowFx, o.X, o.Y, o.R, o.K);
                        break;
                    case SheetMath.OpKind.Embers:
                        LockFx.Burst(_glowFx, _embers, o.X, o.Y, o.K, _k1);
                        break;
                }
            }

            _shadow?.Commit();
            _a.Commit();
            _b?.Commit();
            _emi?.Commit();
            _eyeGlow.Commit();
            _glint?.Commit();
            _lights.Commit();
            _glowFx.Commit();
            _rays.Commit();
            _flash.Commit();
            foreach (var (s, c, e) in _pieces)
            {
                s?.Commit();
                c.Commit();
                e?.Commit();
            }
        }

        static MenuV6Glow.Kind Kind(SheetMath.Glow g) => g switch
        {
            SheetMath.Glow.GoldHot => MenuV6Glow.Kind.GoldHot,
            SheetMath.Glow.Blue => MenuV6Glow.Kind.Blue,
            SheetMath.Glow.BlueHot => MenuV6Glow.Kind.BlueHot,
            SheetMath.Glow.White => MenuV6Glow.Kind.White,
            _ => MenuV6Glow.Kind.Gold
        };

        Rect At(Rect r, double ax, double ay, double sc) =>
            new((float)(ax + (r.x - _K.x) * sc), (float)(ay + (r.y - _K.y) * sc), (float)(r.width * sc), (float)(r.height * sc));

        Vector2 At(Vector2 p, double ax, double ay, double sc) => new((float)(ax + (p.x - _K.x) * sc), (float)(ay + (p.y - _K.y) * sc));

        /// <summary>relics.js drawShadow(.5 a) + draw (the idle state: torch flick on B, the emissive eye passes, the glint
        /// sweep, the seam hot point, the iris glint), anchored at (ax, ay) and scaled by sc about the keystone's anchor.</summary>
        void Intact(double ax, double ay, double sc, double a, double eye, double t)
        {
            if (a <= .005) return;
            _shadow?.Rect(At(_shadowRect, ax, ay, sc), _uvS, new Color(1, 1, 1, MenuV6Linear.Shade(.5 * a)));
            var body = At(new Rect(_origin.x, _origin.y, _wh.x, _wh.y), ax, ay, sc);
            _a.Rect(body, _uvA, new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a)));
            var flick = MenuV6Math.Flick(t, Noise);
            var gaBefore = a;
            if (_b != null)
            {
                _b.Rect(body, _uvB, new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a * flick)));
                gaBefore = a * flick;
            }

            MenuV6RigMath.Emissive(eye, gaBefore, out var ek, out var p1, out var p2, out var ga);
            if (p1 > 0 && _emi != null)
            {
                _emi.Rect(body, _uvE, new Color(1, 1, 1, MenuV6Linear.Alpha(a * p1)));
                if (p2 > 0) _emi.Rect(body, _uvE, new Color(1, 1, 1, MenuV6Linear.Alpha(a * p2)));
            }

            var ec = At(_eyeC, ax, ay, sc);
            if (ek > .01) LockFx.Spr(_eyeGlow, MenuV6Glow.Kind.Blue, ec.x, ec.y, _wpx * sc * .7, ga * .22 * ek);
            var glint = MenuV6RigMath.KeystoneGlint(t);
            if (glint >= 0 && glint < 1 && _glint != null && _glintUv.Count > 0)
            {
                var j = Math.Min(_glintUv.Count - 1, (int)Math.Floor(glint * _glintUv.Count));
                _glint.Rect(At(_glintRect, ax, ay, sc), _glintUv[j], new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a)));
            }

            var hot = MenuV6RigMath.KeystoneHot(t);
            if (hot >= 0 && hot <= 1 && _seamX != null)
            {
                MenuV6RigMath.Polyline(_seamX, _seamY, hot, out var hx, out var hy);
                var hp = At(new Vector2((float)hx, (float)hy), ax, ay, sc);
                LockFx.Spr(_lights, MenuV6Glow.Kind.GoldHot, hp.x, hp.y, 16 * sc, ga * .8 * Math.Sin(hot * Math.PI));
            }

            var iris = MenuV6RigMath.KeystoneIris(t);
            if (iris > 0)
            {
                var ip = At(_iris, ax, ay, sc);
                LockFx.Spr(_lights, MenuV6Glow.Kind.White, ip.x + 2 * sc, ip.y - 1 * sc, 12 * sc, ga * .95 * iris);
            }
        }

        /// <summary>relics.js drawPieceAbs: the piece's centre at (x, y), rotated, scale s, its thickness offset when th &gt; 0,
        /// its emissive at a × min(1, eye).</summary>
        void Piece(int i, double x, double y, double rot, double s, double a, double th, double eye)
        {
            if (i < 0 || i >= _pieces.Count || a <= .005) return;
            var (qs, qc, qe) = _pieces[i];
            var pc = _pk.Pieces[i];
            double W = _pkW * s, H = _pkH * s, ox = -pc.cbx / _pk.S * s, oy = -pc.cby / _pk.S * s;
            var col = new Color(1, 1, 1, (float)MenuV6MathF.Clamp01(a));
            var t2 = th * 3.2 * s;
            if (t2 > .1 && qs != null) qs.Pivoted(x, y, ox + t2 * .55, oy + t2, W, H, QuadList.Full, col, rot);
            qc.Pivoted(x, y, ox, oy, W, H, QuadList.Full, col, rot);
            if (eye > .01 && qe != null)
                qe.Pivoted(x, y, ox, oy, W, H, QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Alpha(a * Math.Min(1, eye))), rot);
        }
    }
}
