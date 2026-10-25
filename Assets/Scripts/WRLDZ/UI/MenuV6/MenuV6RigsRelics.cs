using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;

namespace WRLDZ.UI.MenuV6
{
    // ====================================================================== the WORLD MAP niche (menu.js:138-219)

    /// <summary>
    /// The live WORLD MAP niche (makeCityDoor.draw): Battle City at night through the Solid Vision window — stars, two
    /// searchlights, the 3 Tears pulsing with his eye's sliver opening in Tear 0, the horizon, the blimp at 3.2 px/s with
    /// its nose light, the skyline, the windows reseeded every 2.9 s, the site marker, the scan lines, two pings and the
    /// niche Ring bearing on its targets; on hover a blue wash. Everything inside the window is clipped to it (RectMask2D);
    /// the niche's own frame and shadow are drawn over it. Approximation (recorded): the Ring's refraction of the niche
    /// itself and the rays' CSS blur (a soft stroke instead).
    /// </summary>
    public sealed class NicheRig : MenuV6Rig
    {
        Rect _canvas;
        double _nx0, _ny0, _nw, _nh, _hz;
        double _ox, _oy;   // stage px of niche-local (0, 0)
        QuadLayer _skygrad, _sliver, _horizon, _blimp, _sky, _frame, _shadow, _glowA, _glowNose, _glowSite, _glowPing;
        ShapeLayer _stars, _lights, _windows, _site, _scan, _pings, _hover;
        Rect _skygradR, _horizonR, _blimpR, _skyR, _frameR, _shadowR, _sliverR;
        double _blimpBx, _blimpBy;
        readonly List<Rect> _sliverUv = new();
        readonly List<(double x, double y, double sz)> _tears = new();
        readonly List<(double x, double y, bool cyan, double ph)> _wins = new();
        double _siteX, _siteY;
        RingDraw _ring;
        double _ringX, _ringY, _ringS;
        readonly double[] _aimX = new double[5], _aimY = new double[5], _from = new double[5], _kk = new double[5];
        readonly double[] _px3 = new double[3], _py3 = new double[3];
        readonly double[] _px = new double[5], _py = new double[5], _rot = new double[5];
        const float G = MenuV6Linear.Dark;

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("niche", ctx, host, frame);
            var n = ctx.Rig("niche");
            var parts = RJ.D(n, "parts");
            if (n == null || parts == null) return false;
            var sky = Part(RJ.D(parts, "sky"));
            var fr = Part(RJ.D(parts, "frame"));
            if (sky == null || fr == null) return false;
            _canvas = RJ.R(n, "canvasRect");
            _nx0 = RJ.N(n, "nx0");
            _ny0 = RJ.N(n, "ny0");
            _nw = RJ.N(n, "nw");
            _nh = RJ.N(n, "nh");
            _hz = RJ.N(n, "hz");
            _ox = _canvas.x + _nx0;
            _oy = _canvas.y + _ny0;

            // the window clip (nx0, ny0, nw, nh + 6) as a RectMask2D node; its graphics still use stage px
            var clipRect = new Rect((float)_ox, (float)_oy, (float)_nw, (float)(_nh + 6));
            var clip = Gfx.Clip(host, "NicheClip", clipRect, frame);
            QuadLayer Q(string nm, RigTex t, bool add = false)
            {
                var l = Gfx.Quads(clip, "Niche_" + nm, clipRect, t);
                l.Add = add;
                return l;
            }

            ShapeLayer Sh(string nm, bool add = false)
            {
                var l = Gfx.Shapes(clip, "Niche_" + nm, clipRect);
                l.Add = add;
                return l;
            }
            Rect CR(string part) => Canv(RJ.R(RJ.D(parts, part), "rect"));

            var sg = Part(RJ.D(parts, "skygrad"));
            if (sg != null)
            {
                _skygrad = Q("skygrad", sg);
                _skygradR = CR("skygrad");
            }

            _stars = Sh("stars");
            _lights = Sh("lights", true);
            _glowA = Q("tearGlow", Gfx.Glow(G), true);
            var sl = RJ.D(n, "sliver");
            var sheet = Gfx.Sheet(RJ.S(sl, "file"));
            if (sheet != null)
            {
                _sliver = Q("sliver", sheet);
                _sliverR = Canv(RJ.R(sl, "rect"));
                _sliverUv.AddRange(Cells(sheet, sl));
            }

            var hz = Part(RJ.D(parts, "horizon"));
            if (hz != null)
            {
                _horizon = Q("horizon", hz, true);
                _horizonR = CR("horizon");
            }

            var bl = Part(RJ.D(parts, "blimp"));
            if (bl != null)
            {
                _blimp = Q("blimp", bl);
                _blimpR = CR("blimp");
                _blimpBx = RJ.N(RJ.D(parts, "blimp"), "bx");
                _blimpBy = RJ.N(RJ.D(parts, "blimp"), "by");
            }

            // the page's draw order (menu.js:186-219): the nose light under the skyline, the site and the pings over it
            _glowNose = Q("noseLight", Gfx.Glow(G), true);
            _sky = Q("sky", sky);
            _skyR = CR("sky");
            _windows = Sh("windows");
            _glowSite = Q("siteGlow", Gfx.Glow(G), true);
            _site = Sh("site", true);
            _scan = Sh("scan");
            _pings = Sh("pings", true);
            _glowPing = Q("pingGlow", Gfx.Glow(G), true);
            _ring = RingDraw.From(Gfx, clip, clipRect, RJ.D(n, "ring"), "Niche_Ring");
            if (_ring != null)
            {
                _ring.SetGamma(G);
                var rc = RJ.V(n, "ringC");
                _ringS = RJ.N(n, "ringS", .46);
                _ringX = _ox + rc.x;
                _ringY = _oy + rc.y;
                _ring.X = _ringX;
                _ring.Y = _ringY;
                _ring.S = _ringS;
                _ring.R = 48 * _ringS;
                _ring.r = 38 * _ringS;
                var notch = RJ.L(ctx.Rig("ringEcho"), "notch");
                if (notch != null)
                {
                    _ring.Notch = new double[notch.Count];
                    for (var i = 0; i < notch.Count; i++) _ring.Notch[i] = RJ.At(notch, i);
                }

                var aims = RJ.L(n, "aims");
                for (var i = 0; i < 5; i++)
                {
                    var a = aims != null && i < aims.Count ? aims[i] as List<object> : null;
                    _aimX[i] = a != null ? _ox + RJ.At(a, 0) : double.NaN;
                    _aimY[i] = a != null ? _oy + RJ.At(a, 1) : double.NaN;
                }
            }

            _hover = Sh("hover", true);

            var tears = RJ.L(n, "tears");
            if (tears != null)
                foreach (var o in tears)
                    if (o is List<object> a)
                        _tears.Add((_ox + RJ.At(a, 0), _oy + RJ.At(a, 1), RJ.At(a, 2, 1)));
            var site = RJ.L(n, "site");
            _siteX = _ox + RJ.At(site, 0);
            _siteY = _oy + RJ.At(site, 1);
            var wins = RJ.L(n, "wins");
            if (wins != null)
                foreach (var o in wins)
                    if (o is List<object> a)
                        _wins.Add((_ox + RJ.At(a, 0), _oy + RJ.At(a, 1), RJ.At(a, 2) > .5, RJ.At(a, 3)));

            // outside the clip: the niche's frame and its shadow
            _frame = Gfx.Quads(host, "Niche_frame", frame, fr);
            _frameR = CR("frame");
            var shd = Part(RJ.D(parts, "shadow"));
            if (shd != null)
            {
                _shadow = Gfx.Quads(host, "Niche_shadow", frame, shd);
                _shadowR = CR("shadow");
            }

            return true;
        }

        Rect Canv(Rect r) => new(_canvas.x + r.x, _canvas.y + r.y, r.width, r.height);

        static void C(QuadLayer q) => q?.Clear();
        static void D(QuadLayer q) => q?.Commit();

        public override void Tick(double t)
        {
            C(_skygrad);
            C(_sliver);
            C(_horizon);
            C(_blimp);
            C(_sky);
            C(_frame);
            C(_shadow);
            C(_glowA);
            C(_glowNose);
            C(_glowSite);
            C(_glowPing);
            _stars.Clear();
            _lights.Clear();
            _windows.Clear();
            _site.Clear();
            _scan.Clear();
            _pings.Clear();
            _hover.Clear();

            _skygrad?.Rect(_skygradR, QuadList.Full, Color.white);
            // stars i < 16 (niche-local, .8 × .8)
            for (var i = 0; i < 16; i++)
            {
                var x = _ox + MenuV6Math.Hash2(i, 1, 3) * _nw;
                var y = _oy + MenuV6Math.Hash2(i, 2, 3) * _nh * .5;
                _stars.RectAA(x, y, .8, .8, new Color(232 / 255f, 236 / 255f, 1f, MenuV6Linear.Alpha(MenuV6RigMath.NicheStar(t, i), G)));
            }

            // searchlights (additive): a wedge from the horizon, rgba(190,230,255,.34) fading along it
            var by0 = _oy + (_hz - _ny0);
            foreach (var (fx, sp, ph) in new[] { (.3, .55, 0.0), (.82, .42, 2.1) })
            {
                var bx = _ox + _nw * fx;
                var an = MenuV6RigMath.Searchlight(t, sp, ph);
                var L = _nh * 1.3;
                const double wd = .075;
                var c0 = new Color(190 / 255f, 230 / 255f, 1f, MenuV6Linear.Alpha(.34, G));
                var c1 = new Color(190 / 255f, 230 / 255f, 1f, 0f);
                _lights.Tri(bx, by0, c0, bx + Math.Cos(an - wd) * L, by0 + Math.Sin(an - wd) * L, c1,
                    bx + Math.Cos(an + wd) * L, by0 + Math.Sin(an + wd) * L, c1);
            }

            // the Tears (additive): violet + amethyst glows and the pink stroke
            for (var i = 0; i < _tears.Count; i++)
            {
                var (tx, ty, sz) = _tears[i];
                var tp = MenuV6RigMath.TearPulse(t, i);
                Spr(_glowA, MenuV6Glow.Kind.Violet, tx, ty, _nh * .9 * sz, .5 * tp, G);
                Spr(_glowA, MenuV6Glow.Kind.Ame, tx, ty, _nh * .45 * sz, .55 * tp, G);
                QuadStroke(_lights, tx, ty - _nh * .12 * sz, tx - 2.2, ty, tx, ty + _nh * .1 * sz, 1.1,
                    new Color(1f, 120 / 255f, 200 / 255f, MenuV6Linear.Alpha(.55 + .35 * tp, G)));
            }

            // his eye's sliver in Tear 0 (source-over)
            var f = MenuV6RigMath.SliverFrame(t, _sliverUv.Count);
            if (f >= 0 && _sliver != null) _sliver.Rect(_sliverR, _sliverUv[f], Color.white);
            _horizon?.Rect(_horizonR, QuadList.Full, Color.white);
            MenuV6RigMath.Blimp(t, _nw, _nh, out var bxl, out var byl, out var light);
            if (_blimp != null)
            {
                var r = _blimpR;
                r.x += (float)(bxl - _blimpBx);
                r.y += (float)(byl - _blimpBy);
                _blimp.Rect(r, QuadList.Full, Color.white);
            }

            if (light) Spr(_glowNose, MenuV6Glow.Kind.Red, _ox + bxl + 6.5, _oy + byl + .5, 6, .9, G);
            _sky.Rect(_skyR, QuadList.Full, Color.white);
            var warm = new Color(1f, 210 / 255f, 130 / 255f, .92f);
            var cyan = new Color(130 / 255f, 230 / 255f, 1f, .95f);
            for (var i = 0; i < _wins.Count; i++)
            {
                var w = _wins[i];
                if (MenuV6RigMath.WindowOn(t, i, w.ph)) _windows.RectAA(w.x, w.y, 1.1, 1.2, w.cyan ? cyan : warm);
            }

            // the site (additive)
            var b = .6 + .4 * Math.Sin(t * 1.7);
            Spr(_glowSite, MenuV6Glow.Kind.Gold, _siteX, _siteY, 26, .5 * b, G);
            Spr(_glowSite, MenuV6Glow.Kind.GoldHot, _siteX, _siteY, 9, .9, G);
            var sc = new Color(1f, 230 / 255f, 160 / 255f, MenuV6Linear.Alpha(.95, G));
            _px3[0] = _siteX; _py3[0] = _siteY - 5;
            _px3[1] = _siteX + 4.5; _py3[1] = _siteY + 1;
            _px3[2] = _siteX - 4.5; _py3[2] = _siteY + 1;
            _site.Poly(_px3, _py3, 3, sc);
            // scan lines (source-over)
            var scanC = new Color(0f, 0f, 0f, MenuV6Linear.Shade(.16));
            for (var y = MenuV6RigMath.Mod(t * 6, 2.4); y < _nh; y += 2.4)
                _scan.RectAA(_ox, _oy + y, _nw, .8, scanC);
            // pings on Tear 0 and Tear 2
            for (var j = 0; j < 2 && _tears.Count >= 3; j++)
            {
                var k = MenuV6RigMath.Ping(t, j);
                if (k < 0) continue;
                var tear = j == 0 ? _tears[0] : _tears[2];
                var rad = 3 + k * (j == 1 ? 9 : 17);
                _pings.Circle(tear.x, tear.y, rad, 1.6, new Color(191 / 255f, 230 / 255f, 1f, MenuV6Linear.Alpha(.9 * (1 - k), G)));
                _pings.Circle(tear.x, tear.y, rad, 4, new Color(95 / 255f, 182 / 255f, 1f, MenuV6Linear.Alpha(.45 * (1 - k), G)));
                Spr(_glowPing, MenuV6Glow.Kind.BlueHot, tear.x, tear.y, 16, .7 * (1 - k), G);
            }

            // the niche Ring bearing on the site (gold) and two Tears (blue)
            if (_ring != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    _from[i] = MenuV6RigMath.NicheFrom(t, i);
                    _kk[i] = i == 0 || i == 4 ? 1 : i == 2 ? 1 : 0;
                }

                MenuV6RigMath.RingBearings(_ringX, _ringY, _ringS, _aimX, _aimY, _from, _kk, t, _px, _py, _rot);
                var rays = _tears.Count >= 3
                    ? new[]
                    {
                        new RingDraw.RayTo { I = 2, X = _siteX, Y = _siteY, A = .75, Gold = true },
                        new RingDraw.RayTo { I = 0, X = _tears[2].x, Y = _tears[2].y, A = .6 },
                        new RingDraw.RayTo { I = 4, X = _tears[0].x, Y = _tears[0].y, A = .6 }
                    }
                    : null;
                _ring.Draw(t, new RingDraw.State
                {
                    A = 1, I = .95, Seed = 4.1, Sweep = 0, Aura = .4, Found = 2, Motes = 8, Rot = _rot, PivX = _px, PivY = _py,
                    Glow = new[] { .5, 0, .7, 0, .5 }, RaysTo = rays
                });
            }

            // hover: a blue wash over the window (additive), and the press
            var hov = Ctx.Map != null && Ctx.Map.Over ? 1.0 : 0.0;
            var press = Ctx.Map != null ? Ctx.Map.Press(t, .4) : 0;
            if (hov > 0)
                _hover.RectAA(_ox, _oy, _nw, _nh, new Color(95 / 255f, 182 / 255f, 1f, MenuV6Linear.Alpha(.1 * hov + .12 * press, G)));

            _frame.Rect(_frameR, QuadList.Full, Color.white);
            _shadow?.Rect(_shadowR, QuadList.Full, Color.white);

            D(_skygrad);
            D(_sliver);
            D(_horizon);
            D(_blimp);
            D(_sky);
            D(_frame);
            D(_shadow);
            D(_glowA);
            D(_glowNose);
            D(_glowSite);
            D(_glowPing);
            _stars.Commit();
            _lights.Commit();
            _windows.Commit();
            _site.Commit();
            _scan.Commit();
            _pings.Commit();
            _hover.Commit();
        }

        /// <summary>A quadratic stroke M(x0, y0) Q(qx, qy; x1, y1), flattened into 6 segments.</summary>
        static void QuadStroke(ShapeLayer s, double x0, double y0, double qx, double qy, double x1, double y1, double w, Color c)
        {
            double px = x0, py = y0;
            for (var i = 1; i <= 6; i++)
            {
                var u = i / 6.0;
                double a = (1 - u) * (1 - u), b = 2 * (1 - u) * u, cc = u * u;
                var nx = a * x0 + b * qx + cc * x1;
                var ny = a * y0 + b * qy + cc * y1;
                s.Line(px, py, nx, ny, w, c, c);
                px = nx;
                py = ny;
            }
        }
    }

    // ====================================================================== the deck box (anime_box_v6.js:33-86)

    /// <summary>
    /// The Puzzle's box on the Spirit Dueler plaque (box6 toon renders, local-only: licence unknown, D7): at rest the
    /// closed box with the Wdjat breathing on its lid; on hover the lid lifts (.25 eBack(hv)), light leaks over the rim and
    /// the equipped deck's first 3 cards rise inside (the game's own CardArt, loaded on the first hover). The lid's cast
    /// shadow on the rim is a multiply in the page; here it is the lid shape in black through a mask of the body (a
    /// recorded approximation of multiply on UI/Default). A clone without the box6 renders keeps the P1a still.
    /// </summary>
    public sealed class BoxRig : MenuV6Rig
    {
        Rect _canvas;
        double _cx, _cy, _s, _k, _ox, _oy, _RW, _RH;
        double[] _bbox;
        (double x, double y)[] _open = new (double, double)[3];
        const double Hb = .492, D = .779, Wb = 1;
        RigTex _full, _body, _lid, _eye;
        Rect _uv = QuadList.Full;
        ShapeLayer _contact;
        QuadLayer _fullQ, _lidBehind, _bodyQ, _eyeQ, _lidQ, _beamGlow;
        ISpriteMask _lidShadow;
        ShapeLayer _rimMask, _beam, _cardInk;
        readonly QuadLayer[] _cards = new QuadLayer[3];
        bool _cardsLoaded;
        readonly List<int> _cardIds = new();

        /// <summary>P4: the deck hall's box (RelicBox) drives the lid / rise / light / Wdjat itself (open at rest, shut by SAVE)
        /// instead of the plaque's hover; null = the menu's hover state.</summary>
        public Func<double, (double lid, double rise, double light, double eye)> State;

        /// <summary>P4: the cards standing in the box (the edited deck's first 3); loaded again on the next draw.</summary>
        public void SetCards(IList<int> ids)
        {
            _cardIds.Clear();
            if (ids != null) _cardIds.AddRange(ids);
            _cardsLoaded = false;
            foreach (var c in _cards)
                if (c != null)
                    c.Tex = null;
        }

        public override bool Build(RigContext ctx, object host, Rect frame)
        {
            Init("box", ctx, host, frame);
            var b = ctx.Rig("box");
            var parts = RJ.D(b, "parts");
            _full = LocalPart(RJ.D(parts, "full"));
            _body = LocalPart(RJ.D(parts, "body"));
            _lid = LocalPart(RJ.D(parts, "lid"));
            _eye = LocalPart(RJ.D(parts, "eye"));
            if (_full == null || _body == null || _lid == null) return false;   // a clone: the P1a still stays
            _canvas = RJ.R(b, "canvasRect");
            _cx = _canvas.x + RJ.N(b, "cx");
            _cy = _canvas.y + RJ.N(b, "cy");
            _s = RJ.N(b, "s", 104);
            var bb = RJ.L(b, "bbox");
            _bbox = new[] { RJ.At(bb, 0), RJ.At(bb, 1), RJ.At(bb, 2), RJ.At(bb, 3) };
            var W1 = RJ.N(b, "W1", 1.2887741);
            var Y1 = RJ.N(b, "Y1", .21047555);
            _k = W1 * _s / (_bbox[2] - _bbox[0]);
            _ox = _cx - (_bbox[0] + _bbox[2]) / 2 * _k;
            _oy = _cy + Y1 * _s - _bbox[3] * _k;
            _RW = 2048 * _k;
            _RH = 1536 * _k;
            var open = RJ.L(b, "open");
            for (var i = 0; i < 3; i++)
            {
                var p = open != null && i < open.Count ? open[i] as List<object> : null;
                _open[i] = (_ox + RJ.At(p, 0) * _k, _oy + RJ.At(p, 1) * _k);
            }

            // the box6 renders are 2048 x 1536 frames scaled to the set's width (296 px for L104 -> 296 x 222); the export
            // padded them to x4 px at the bottom (296 x 224) and records the padded size, so the content UV comes from the
            // frame's own aspect
            _uv = _full.Content(_full.W, Math.Round(_full.W * 1536.0 / 2048.0));
            _contact = Shapes("Box_contactShadow");
            _lidBehind = Quads("Box_lidBehind", _lid);
            _fullQ = Quads("Box_full", _full);
            _bodyQ = Quads("Box_body", _body);
            // the lid's hard shadow on the rim: the lid shape (black) through a mask of the body
            // (the mask images draw the whole sprite: their rect takes the padding rows too)
            var br = BodyRect();
            br.height /= Mathf.Max(.5f, _uv.height);
            _lidShadow = Gfx.SpriteMask(host, "Box_lidShadow", br, frame, _body, _lid);
            // above the rim: the light and the cards, through a polygon mask
            var rim = Gfx.PolyMask(host, "Box_aboveRim", frame, out _rimMask);
            _beam = Gfx.Shapes(rim, "Box_beam", frame);
            _beamGlow = Gfx.Quads(rim, "Box_beamGlow", frame, Gfx.Glow(MenuV6Linear.Glow));
            _beamGlow.Add = true;
            for (var i = 0; i < 3; i++) _cards[i] = Gfx.Quads(rim, "Box_card" + i, frame, null);
            _cardInk = Gfx.Shapes(rim, "Box_cardInk", frame);
            if (_eye != null) _eyeQ = Quads("Box_eye", _eye);
            _lidQ = Quads("Box_lid", _lid);
            if (ctx.Data?.DeckCards != null) _cardIds.AddRange(ctx.Data.DeckCards);
            return true;
        }

        Rect BodyRect() => new((float)_ox, (float)_oy, (float)_RW, (float)_RH);

        void Proj(double x, double y, double z, out double px, out double py)
        {
            MenuV6RigMath.BoxProj(x, y, z, out var a, out var b);
            px = _cx + a * _s;
            py = _cy + b * _s;
        }

        public override void Tick(double t)
        {
            double lid, rise, light, eye;
            if (State != null)
                (lid, rise, light, eye) = State(t);
            else
            {
                var hv = Ctx.Plaque != null ? MenuV6RigMath.Clamp01(Ctx.Plaque.Value(t)) : 0;
                MenuV6RigMath.BoxState(t, hv, out lid, out rise, out light, out eye, out _);
            }
            MenuV6RigMath.BoxLid(lid, out var k1, out var k2);
            var lift = Hb * (.14 * k1 + .56 * k2);
            var back = -D * .66 * k2;
            MenuV6RigMath.BoxProj(0, lift, back, out var lpx, out var lpy);
            double ldx = lpx * _s, ldy = lpy * _s;
            var top = _oy + ldy + 69 * _k;
            const double m = 10;
            if (top < 2 + m) ldy += 2 + m * Math.Exp((top - 2 - m) / m) - top;
            var closed = lid < .004;
            var lidBehind = k2 > .28;

            _contact.Clear();
            _fullQ.Clear();
            _lidBehind.Clear();
            _bodyQ.Clear();
            _rimMask.Clear();
            _beam.Clear();
            _beamGlow.Clear();
            foreach (var c in _cards) c.Clear();
            _cardInk.Clear();
            _eyeQ?.Clear();
            _lidQ.Clear();

            // the hard contact shadow: the plinth footprint, shifted (.05 s, .035 s)
            var fx = new double[4];
            var fy = new double[4];
            double hw = Wb / 2, hd = D / 2;
            var pts = new[] { (-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd) };
            for (var i = 0; i < 4; i++)
            {
                Proj(pts[i].Item1, 0, pts[i].Item2, out fx[i], out fy[i]);
                fx[i] += _s * .05;
                fy[i] += _s * .035;
            }

            _contact.Poly(fx, fy, 4, new Color(6 / 255f, 4 / 255f, 9 / 255f, MenuV6Linear.Shade(.42)));
            var body = BodyRect();
            var lidR = new Rect((float)(_ox + ldx), (float)(_oy + ldy), (float)_RW, (float)_RH);
            var lidShadowA = 0.0;
            if (closed)
            {
                _fullQ.Rect(body, _uv, Color.white);
            }
            else
            {
                if (lidBehind) _lidBehind.Rect(lidR, _uv, Color.white);
                _bodyQ.Rect(body, _uv, Color.white);
                if (k1 > .02 && k2 < .5)
                {
                    var lp = Math.Sqrt(ldx * ldx + ldy * ldy);
                    // multiply by SHM rgb(183,178,204) = x (.718, .698, .802): over black at the mean's complement (.261),
                    // Linear-corrected like every dark op
                    lidShadowA = MenuV6Linear.Shade(1 - .7393) * (1 - MenuV6MathF.Clamp01(k2 * 2));
                    _lidShadowOff = (lp * .55, lp * .45);
                }

                AboveRim(t, light, k1, k2, rise);
            }

            _lidShadow?.Set(_lidShadowOff.dx, _lidShadowOff.dy, (float)lidShadowA);
            if (_eyeQ != null)
            {
                var ea = MenuV6RigMath.BoxEyeAlpha(eye);
                if (ea > .004) _eyeQ.Rect(body, _uv, new Color(1, 1, 1, MenuV6Linear.Alpha(ea)));
            }

            if (!closed && !lidBehind) _lidQ.Rect(lidR, _uv, Color.white);

            _contact.Commit();
            _fullQ.Commit();
            _lidBehind.Commit();
            _bodyQ.Commit();
            _rimMask.Commit();
            _beam.Commit();
            _beamGlow.Commit();
            foreach (var c in _cards) c.Commit();
            _cardInk.Commit();
            _eyeQ?.Commit();
            _lidQ.Commit();
        }

        readonly double[] _px = new double[8], _py = new double[8];
        (double dx, double dy) _lidShadowOff;

        void AboveRim(double t, double light, double k1, double k2, double rise)
        {
            var (oLx, oLy) = _open[0];
            var (oNx, oNy) = _open[1];
            var (oRx, oRy) = _open[2];
            double oBx = oLx + oRx - oNx, oBy = oLy + oRy - oNy;
            var s = _s;
            // the mask: from the rim line up (anime_box_v6.js above_rim)
            double[] mx = { oLx - s * .6, oLx, oNx, oRx, oRx + s * .6, oRx + s * .6, oLx - s * .6 };
            double[] my = { oLy, oLy, oNy, oRy, oRy, _cy - s * 3, _cy - s * 3 };
            _rimMask.Poly(mx, my, 7, Color.white, false);
            if (light > .01 && k1 > .05)
            {
                var a = Math.Min(1.2, Math.Max(0, light)) * k1;
                var hgt = s * (.22 + .42 * k2);
                foreach (var (sh, al, rgb) in new[] { (0.0, .13 * a, 0xFFE7A6), (s * .1, .17 * a, 0xFFF3C4) })
                {
                    _px[0] = oLx + sh; _py[0] = oLy;
                    _px[1] = oNx; _py[1] = oNy - sh * .3;
                    _px[2] = oRx - sh; _py[2] = oRy;
                    _px[3] = oRx - s * .08 - sh; _py[3] = oRy - hgt;
                    _px[4] = oBx; _py[4] = oBy - hgt * 1.05;
                    _px[5] = oLx + s * .08 + sh; _py[5] = oLy - hgt;
                    _beam.Poly(_px, _py, 6, Col(rgb, MenuV6Linear.Alpha(al)));
                }

                double c0x = (oLx + oRx) / 2, c0y = (oNy + oBy) / 2;
                Spr(_beamGlow, MenuV6Glow.Kind.GoldHot, c0x, c0y - s * .05, s * .9, .26 * a, MenuV6Linear.Glow);
            }

            if (rise <= .001 || _cardIds.Count == 0) return;
            LoadCards();
            var nC = 0;
            foreach (var c in _cards)
                if (c.Tex != null)
                    nC++;
            if (nC == 0) return;
            var hd = D / 2;
            for (var i = 0; i < nC; i++)
            {
                var kc = i - (nC - 1) / 2.0;
                const double cw = .34;
                var chh = cw * (614.0 / 421.0);
                var z = hd - .12 - i * .07;
                var baseY = .04 + rise * (.26 + .05 * (nC - 1 - i));
                var bx = kc * .05;
                Proj(bx - cw / 2, baseY + chh, z, out var s0x, out var s0y);
                Proj(bx + cw / 2, baseY + chh, z, out var s1x, out var s1y);
                Proj(bx - cw / 2, baseY, z, out var s3x, out var s3y);
                double s2x = s1x + s3x - s0x, s2y = s1y + s3y - s0y;
                _px[0] = s0x + 1.6; _py[0] = s0y + 1.8;
                _px[1] = s1x + 1.6; _py[1] = s1y + 1.8;
                _px[2] = s2x + 1.6; _py[2] = s2y + 1.8;
                _px[3] = s3x + 1.6; _py[3] = s3y + 1.8;
                _cardInk.Poly(_px, _py, 4, new Color(6 / 255f, 4 / 255f, 9 / 255f, MenuV6Linear.Shade(.45)));
                _cards[i].Corners(s0x, s0y, s1x, s1y, s2x, s2y, s3x, s3y, QuadList.Full, Color.white);
                var ink = new Color(0, 0, 0, 1);
                _cardInk.Line(s0x, s0y, s1x, s1y, .9, ink, ink);
                _cardInk.Line(s1x, s1y, s2x, s2y, .9, ink, ink);
                _cardInk.Line(s2x, s2y, s3x, s3y, .9, ink, ink);
                _cardInk.Line(s3x, s3y, s0x, s0y, .9, ink, ink);
            }
        }

        /// <summary>The equipped deck's first 3 cards through the game's CardArt, once (main-thread file reads, as every
        /// card screen does). The menu never parses cards_db.json itself: until a screen has loaded it there is nothing
        /// to show, and a later hover tries again (the latch is set only once the database is there).</summary>
        void LoadCards()
        {
            if (_cardsLoaded) return;
            var db = CardDatabase.Instance;
            if (db == null) return;
            _cardsLoaded = true;
            try
            {
                for (var i = 0; i < _cards.Length && i < _cardIds.Count; i++)
                {
                    var sp = db.GetArt(_cardIds[i]);
                    if (sp == null || sp.texture == null) continue;
                    var tr = sp.textureRect;
                    float tw = sp.texture.width, th = sp.texture.height;
                    _cards[i].Tex = new RigTex
                    {
                        Key = "card:" + _cardIds[i], Tex = sp.texture, Sprite = sp, W = (int)tr.width, H = (int)tr.height,
                        Uv = new Rect(tr.x / tw, tr.y / th, tr.width / tw, tr.height / th)
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] box cards: " + ex.Message);
            }
        }
    }

    /// <summary>The Spirit Dueler plaque's text swap on hover (menu.js drawDomRelics): the deck lines fade out
    /// (clamp(1 − 2 ins)) and Sugoroku's reading of the box fades in (clamp(2 ins − 1)), ins = sstep(.2, .8, hv).</summary>
    public sealed class PlaqueTextRig : MenuV6Rig
    {
        readonly List<(Graphic g, float a)> _nrm = new(), _ins = new();
        double _last = -1;

        public static PlaqueTextRig For(RigContext ctx)
        {
            var r = new PlaqueTextRig();
            r.Init("plaqueText", ctx, null, Rect.zero);
            return r;
        }

        public override bool Build(RigContext ctx, object host, Rect frame) => true;

        public void Normal(Graphic g)
        {
            if (g != null) _nrm.Add((g, g.color.a));
        }

        public void Inscription(Graphic g)
        {
            if (g == null) return;
            _ins.Add((g, g.color.a));
            var c = g.color;
            c.a = 0f;
            g.color = c;
        }

        public override void Tick(double t)
        {
            var hv = Ctx.Plaque != null ? MenuV6RigMath.Clamp01(Ctx.Plaque.Value(t)) : 0;
            var ins = MenuV6Math.SStep(.2, .8, hv);
            if (Math.Abs(ins - _last) < 1e-5) return;
            _last = ins;
            var an = MenuV6MathF.Clamp01(1 - 2 * ins);
            var ai = MenuV6MathF.Clamp01(2 * ins - 1);
            foreach (var (g, a) in _nrm) Set(g, a * an);
            foreach (var (g, a) in _ins) Set(g, a * ai);
        }

        static void Set(Graphic g, double a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = (float)a;
            g.color = c;
        }
    }
}
