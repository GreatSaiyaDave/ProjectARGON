using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The Ring's bearings (relics.js:367-398 ringBearings / ringPivots / aimAngle / tipOf): the lead pointer's link travels
    /// round the rim and it rises to the Millennium site, the others hang toward their Tears; pure math, checked offline against
    /// the page's per-frame pivots (sheets_trace_&lt;layout&gt;.json ringpiv).
    /// </summary>
    public static class RingMath
    {
        public const double R0 = 48, Rr0 = 38;
        static readonly double[] Att = { 0, 58, 90, 122, 180 };

        static double Wrap(double d)
        {
            while (d > Math.PI) d -= Math.PI * 2;
            while (d < -Math.PI) d += Math.PI * 2;
            return d;
        }

        public static double Aim(double px, double py, double tx, double ty) => Math.Atan2(-(tx - px), ty - py);

        public static void Pivot(int i, double x, double y, double s, out double px, out double py)
        {
            var an = Att[i] * Math.PI / 180;
            px = x + Math.Cos(an) * (R0 + 3.2) * s;
            py = y + Math.Sin(an) * (R0 + 3.2) * s + 3 * s;
        }

        /// <param name="tgX">targets (NaN = none: the pointer hangs from its rotation <paramref name="from"/>)</param>
        public static void Bearings(double x, double y, double s, double[] tgX, double[] tgY, double[] from, double[] k, double t,
            double[] pivX, double[] pivY, double[] rot, int lead = 2)
        {
            var Rr = (R0 + 3.2) * s;
            for (var i = 0; i < 5; i++)
            {
                Pivot(i, x, y, s, out var bx, out var by);
                pivX[i] = bx;
                pivY[i] = by;
                var ki = MenuV6Math.Clamp(k[i], 0, 1.2);
                var f0 = from[i];
                if (double.IsNaN(tgX[i]))
                {
                    rot[i] = f0;
                    continue;
                }

                if (i == lead)
                {
                    var a0 = Att[i] * Math.PI / 180;
                    var a1 = Math.Atan2(tgY[i] - y, tgX[i] - x);
                    var an = a0 + Wrap(a1 - a0) * ki;
                    pivX[i] = x + Math.Cos(an) * Rr;
                    pivY[i] = y + Math.Sin(an) * Rr + 3 * s * (1 - MenuV6Math.Clamp(ki, 0, 1));
                    var exact = Aim(pivX[i], pivY[i], tgX[i], tgY[i]);
                    var rad = Math.Atan2(-Math.Cos(an), Math.Sin(an));
                    var r1 = rad + Wrap(exact - rad) * MenuV6Math.Clamp(ki, 0, 1);
                    rot[i] = MenuV6Math.Lerp(f0, r1, MenuV6Math.Clamp(ki * 1.6, 0, 1)) + .02 * Math.Sin(t * 2.2);
                    continue;
                }

                rot[i] = MenuV6Math.Lerp(f0, MenuV6Math.Clamp(Aim(bx, by, tgX[i], tgY[i]), -1.45, 1.45), ki) + .025 * Math.Sin(t * 2.2 + i);
            }
        }

        /// <summary>relics.js:22 springStep: under-damped, ~20 % overshoot, settled ~0.8 s.</summary>
        public static double SpringStep(double tau, double z = 5.5, double w = 11)
        {
            if (tau <= 0) return 0;
            return 1 - Math.Exp(-z * tau) * (Math.Cos(w * tau) + z / w * Math.Sin(w * tau));
        }

        /// <summary>The seek's per-pointer state (menu.js:933-940): the targets [T2, T1, site, none, T0], the sweep 'from', the
        /// swing k, the tips' glow, and found.</summary>
        public static void Seek(double ur, double t, bool seek, double[] from, double[] k, double[] glow, out double found)
        {
            found = seek ? MenuV6Math.Seg(ur, 1.55, 1.8) : 1;
            for (var i = 0; i < 5; i++)
            {
                var w = ((ur * 1.25 - i * .14) % 1 + 1) % 1;
                from[i] = seek ? Math.Sin(w * Math.PI) * .9 * (w < .5 ? 1 : .6) - .4 : .06 * Math.Sin(t * .9 + i);
                k[i] = seek
                    ? i == 2 ? MenuV6Math.InOut(MenuV6Math.Seg(ur, 1.55, 2.2)) : MenuV6Math.Out(MenuV6Math.Seg(ur, 1.55 + i * .05, 1.95 + i * .05))
                    : i == 2 ? MenuV6Math.InOut(MenuV6Math.Seg(ur, .95, 1.6)) : SpringStep(ur - 1.0 - i * .07);
                glow[i] = MenuV6Math.Lerp(seek ? Math.Sin(w * Math.PI) * .6 : .2, i == 3 ? .15 : 1, found);
            }
        }

        /// <summary>umbrax.js:371 tearSliver: the rip widens for .55 s every per s.</summary>
        public static double TearSliver(double t, double per, double ph)
        {
            var p = ((t + ph) % per + per) % per;
            return p < .55 ? Math.Sin(p / .55 * Math.PI) : 0;
        }
    }

    /// <summary>
    /// WORLD MAP's Ring seek sheet (BUILD_PLAN §6 P3 step 5, §11 F10; menu.js:913-956): the false door with the Puzzle clasp,
    /// Battle City at night in a round window (its own labels: CLOCK TOWER, DOMINO PARK, THE PIER, KAIBACORP), the Tears and the
    /// Millennium site breathing on it, and the Ring manifesting over the map: its pointers search, then FOUND — the lead
    /// pointer rises to the site, three hang toward the Tears, rays run to what they found, the pings answer, his eye looks out
    /// of the first Tear's rip for a moment (HIS eye, straight out). "SEEKING / FOUND · BATTLE CITY · NIGHT" and "and three
    /// Tears · Domino Park · the pier · Clock Tower Plaza" (the count is the real pin count; the "1.2 mi … (sample)" line keeps
    /// its row with no text, §7). It plays to <see cref="GoAt"/>, holds that frame and calls today's GoOverworld (the load
    /// freeze sits on the held frame).
    /// </summary>
    public sealed class RingSheet : StoneSheet
    {
        /// <summary>The seek plays in full (FOUND 1.675, the sliver and the centre's light by 2.4), then the map loads.</summary>
        public const double GoAt = 2.4;

        Action _go;
        bool _went;
        double _held = double.NaN;
        RectTransform _window, _windowFull;
        Vector2 _mw;
        double _mr;
        Image _map;
        readonly List<(Image glow, Image stroke, Vector2 at)> _tears = new();
        Image _siteGlow, _siteMark;
        Vector2 _site;
        QuadLayer _glows, _sliver, _ringGlows;
        ShapeLayer _pings;
        RigTex _sliverTex;
        readonly List<Rect> _cells = new();
        int _shutCell = 13;
        Rect _sliverRect;
        double[][] _blinks;
        RingDraw _ring;
        Vector2 _rc;
        double _rs = 1.5;
        Text _head, _line1, _line2;
        string _foundLine;
        object _seekSpec, _foundSpec;
        readonly double[] _from = new double[5], _k = new double[5], _glow = new double[5], _px = new double[5], _py = new double[5], _rot = new double[5];
        readonly double[] _tx = new double[5], _ty = new double[5];
        double _k1 = 1;

        public static RingSheet Build(MenuV6Stage stone, RectTransform host, Image dim, Action go)
        {
            if (stone == null || host == null) return null;
            var s = host.gameObject.AddComponent<RingSheet>();
            if (!s.Init(stone, host, dim, SheetKind.Ring))
            {
                Destroy(s);
                return null;
            }

            s._go = go;
            s._k1 = s.Lay == MenuLayoutKind.Landscape ? 1 : 1.4;
            s.Frame = FalseDoorFrame.OverStone(s.Space, SheetKind.Ring, s.Lay, "ringseek");
            s.BuildMap();
            s.BuildRing();
            s.BuildCaption();
            var rf = RJ.D(RJ.D(s.J, "geo"), "ringFrame");
            s.BuildLock(RJ.V(rf, "crest"), .52);
            if (!Application.isPlaying) s.ShowAt(2.0);
            else s.Step();
            return s;
        }

        void BuildMap()
        {
            var r = RJ.D(s_ring(J), "mapWin");
            _mw = new Vector2((float)RJ.N(r, "x"), (float)RJ.N(r, "y"));
            _mr = RJ.N(r, "r", 208);
            var body = Frame != null ? Frame.Body : Space;
            var win = new GameObject("MapWindow", typeof(RectTransform), typeof(Image), typeof(Mask));
            win.transform.SetParent(body, false);
            if (Frame != null) win.transform.SetSiblingIndex(1);   // over the frame, under its fractures and the caption
            var wi = win.GetComponent<Image>();
            wi.sprite = SheetDraw.Disc();
            wi.raycastTarget = false;
            win.GetComponent<Mask>().showMaskGraphic = false;
            _window = wi.rectTransform;
            _windowFull = SheetDraw.Stretch(win.transform, "Full");
            var ring = s_ring(J);
            _map = SheetDraw.Part(_windowFull, "Map", RJ.D(ring, "map"), StageRect);
            var units = RJ.D(ring, "units");
            var mt = RJ.D(ring, "mapT");
            var ctx = Stone.Parts.Ctx;
            var gfx = ctx != null && ctx.Gfx != null ? ctx.Gfx : new UguiGfx();
            var tg = RJ.D(units, "tearGlow");
            var ts = RJ.D(units, "tearStroke");
            foreach (var o in RJ.L(mt, "tears") ?? new List<object>())
            {
                var l = o as List<object>;
                var at = new Vector2((float)RJ.At(l, 0), (float)RJ.At(l, 1));
                _tears.Add((Unit(_windowFull, "TearGlow", tg, at), Unit(_windowFull, "TearStroke", ts, at), at));
            }

            var site = RJ.L(mt, "site");
            _site = new Vector2((float)RJ.At(site, 0), (float)RJ.At(site, 1));
            _siteGlow = Unit(_windowFull, "SiteGlow", RJ.D(units, "siteGlow"), _site);
            _siteMark = Unit(_windowFull, "SiteMark", RJ.D(units, "siteMark"), _site);
            _glows = gfx.Quads(_windowFull, "MapGlows", StageRect, gfx.Glow(MenuV6Linear.Glow));
            _glows.Add = true;
            _pings = gfx.Shapes(_windowFull, "Pings", StageRect);
            _pings.Add = true;
            // his eye in the first Tear's rip: the 13-cell flipbook (wide open, straight out) + the shut cell for his blinks
            var sl = RJ.D(ring, "sliver");
            _sliverTex = gfx.Sheet(RJ.S(sl, "sprite"));
            if (_sliverTex != null)
            {
                _sliver = gfx.Quads(_windowFull, "Sliver", StageRect, _sliverTex);
                _sliverRect = RJ.R(sl, "rect");
                var cell = RJ.L(sl, "cell");
                double cw = RJ.At(cell, 0), ch = RJ.At(cell, 1);
                var n = (int)RJ.N(sl, "cells", 14);
                for (var i = 0; i < n; i++) _cells.Add(QuadList.Cell(_sliverTex.W, _sliverTex.H, i * cw, 0, cw, ch));
                _shutCell = (int)RJ.N(sl, "shutCell", 13);
                var iv = RJ.L(RJ.D(sl, "blinks"), "intervals0to600s");
                if (iv != null)
                {
                    _blinks = new double[iv.Count][];
                    for (var i = 0; i < iv.Count; i++)
                        _blinks[i] = new[] { RJ.At(iv[i] as List<object>, 0), RJ.At(iv[i] as List<object>, 1) };
                }
            }
        }

        static Dictionary<string, object> s_ring(Dictionary<string, object> j) => RJ.D(j, "ring");

        Image Unit(Transform parent, string name, Dictionary<string, object> u, Vector2 at)
        {
            if (u == null) return null;
            var sz = RJ.L(u, "size");
            double w = RJ.At(sz, 0), h = RJ.At(sz, 1);
            return SheetDraw.Img(parent, name, SheetData.Sprite(u), new Rect((float)(at.x - w / 2), (float)(at.y - h / 2), (float)w, (float)h), StageRect);
        }

        void BuildRing()
        {
            var ring = RJ.D(s_ring(J), "ring");
            var parts = RJ.D(ring, "parts");
            if (parts == null) return;
            // the P1b part format (RingDraw.From): file / half / size / S / pad / wid / len / centre
            Dictionary<string, object> P(string from)
            {
                var p = RJ.D(parts, from);
                if (p == null) return null;
                var d = new Dictionary<string, object>(p) { ["file"] = RJ.S(p, "sprite") };
                return d;
            }

            var conv = new Dictionary<string, object> { ["ring"] = P("ring"), ["pointer"] = P("pointer"), ["membrane"] = P("membrane"), ["rim_arc"] = P("rimArc") };
            var body = Frame != null ? Frame.Body : Space;
            var host = SheetDraw.Stretch(body, "Ring");
            if (Frame != null) host.SetSiblingIndex(2);
            var ctx = Stone.Parts.Ctx;
            var gfx = ctx != null && ctx.Gfx != null ? ctx.Gfx : new UguiGfx();
            _ring = RingDraw.From(gfx, host, StageRect, conv, "SheetRing");
            if (_ring == null) return;
            _ring.SetGamma(MenuV6Linear.RingBlue);
            _ring.TipGain = _ring.RimGain = RingDraw.EchoBright;
            _rc = RJ.V(ring, "centre");
            _rs = RJ.N(ring, "s", 1.5);
            _ring.X = _rc.x;
            _ring.Y = _rc.y;
            _ring.S = _rs;
            _ring.R = RJ.N(ring, "R", 48 * _rs);
            _ring.r = RJ.N(ring, "r", 38 * _rs);
            var notch = RJ.L(ring, "notch");
            if (notch != null)
            {
                _ring.Notch = new double[notch.Count];
                for (var i = 0; i < notch.Count; i++) _ring.Notch[i] = RJ.At(notch, i);
            }

            _ringGlows = gfx.Quads(host, "SheetRing_eye", StageRect, gfx.Glow(MenuV6Linear.Glow));
            _ringGlows.Add = true;
            // the targets: [Tear 2, Tear 1, the site, none, Tear 0]
            var mt = RJ.D(s_ring(J), "mapT");
            var tears = RJ.L(mt, "tears");
            Vector2 T(int i) => new((float)RJ.At(tears?[i] as List<object>, 0), (float)RJ.At(tears?[i] as List<object>, 1));
            var site = RJ.L(mt, "site");
            var tg = new[] { T(2), T(1), new Vector2((float)RJ.At(site, 0), (float)RJ.At(site, 1)), new Vector2(float.NaN, float.NaN), T(0) };
            for (var i = 0; i < 5; i++)
            {
                _tx[i] = tg[i].x;
                _ty[i] = tg[i].y;
            }
        }

        void BuildCaption()
        {
            var c = MakeContent();
            var texts = RJ.D(J, "texts");
            var seek = RJ.L(texts, "ringSeek");
            var found = RJ.L(texts, "ringFound");
            _seekSpec = SheetData.Spec(seek, "h");
            _foundSpec = SheetData.Spec(found, "h");
            _head = Label(c, "Head", _seekSpec, "SEEKING · BATTLE CITY · NIGHT", StageRect);
            // line 1: the search line while seeking; once FOUND the "1.2 mi … (sample)" line keeps its row with no text (§7)
            _line1 = Label(c, "Line1", SheetData.Spec(seek, "s"), "the Ring’s pointers search the city", StageRect);
            var n = WRLDZ.UI.OverworldUI.TearPinCount;
            _foundLine = "and " + Count(n) + " Tear" + (n == 1 ? "" : "s") + " · Domino Park · the pier · Clock Tower Plaza";
            _line2 = Label(c, "Line2", SheetData.Spec(found, "s", null, 1), "", StageRect);
        }

        /// <summary>The approved copy writes the count as a word ("three Tears"); the number is the real pin count.</summary>
        static string Count(int n) => n switch
        {
            0 => "no",
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            5 => "five",
            6 => "six",
            7 => "seven",
            8 => "eight",
            9 => "nine",
            10 => "ten",
            _ => n.ToString(CultureInfo.InvariantCulture)
        };

        bool Blinking(double t)
        {
            if (_blinks == null) return false;
            var tt = t % 600;
            foreach (var b in _blinks)
                if (tt >= b[0] && tt <= b[1])
                    return true;
            return false;
        }

        protected override void Draw(double u, double v, double t)
        {
            // the seek plays to GoAt, holds that frame and loads the map (once)
            var ur = !double.IsNaN(_held) ? _held : u;
            if (Application.isPlaying && !_went && !Closing && u >= GoAt)
            {
                _went = true;
                _held = GoAt;
                ur = GoAt;
                try { _go?.Invoke(); }
                catch (Exception ex)
                {
                    // the map did not load: give the Stone back (the held sheet and its dim would take every tap from now on)
                    Debug.LogWarning("[WRLDZ MenuV6] WORLD MAP route: " + ex.Message);
                    Close();
                    return;
                }
            }

            var K = SheetMath.K(SheetKind.Ring, ur, v);
            SheetMath.SheetTime(SheetKind.Ring, ur, v, out var us, out var vs);
            var ck = K.Content;
            var k1 = _k1;
            var found = MenuV6Math.Seg(ur, 1.55, 1.8);
            // the map window
            var ir = _mr * MenuV6Math.Out(MenuV6Math.Seg(us, .5, .98)) * (v < 0 ? 1 : 1 - MenuV6Math.InOut(MenuV6Math.Seg(vs, 0, .3)));
            if (_window != null)
            {
                _window.gameObject.SetActive(ir > 1 && ck > .005);
                var rr = (float)Math.Max(.5, ir);
                var wr = new Rect(_mw.x - rr, _mw.y - rr, rr * 2, rr * 2);
                MenuV6Layout.Place(_window, wr, StageRect);
                MenuV6Layout.Place(_windowFull, StageRect, wr);
            }

            SheetDraw.SetA(_map, MenuV6Math.Clamp(ck * 1.2, 0, 1));
            for (var i = 0; i < _tears.Count; i++)
            {
                var tp = .6 + .4 * Math.Sin(t * 1.3 + i * 2);
                SheetDraw.SetA(_tears[i].glow, MenuV6Linear.Alpha(tp * ck));
                SheetDraw.SetA(_tears[i].stroke, MenuV6Linear.Alpha((.6 + .3 * tp) * ck));
            }

            var bb = .6 + .4 * Math.Sin(t * 1.7);
            SheetDraw.SetA(_siteGlow, MenuV6Linear.Alpha(bb * ck));
            SheetDraw.SetA(_siteMark, MenuV6Linear.Alpha(.95 * ck));
            if (_glows != null)
            {
                _glows.Clear();
                LockFx.Spr(_glows, MenuV6Glow.Kind.BlueHot, _mw.x, _mw.y + _mr * .5, 16 * k1, .8 * ck);
                _glows.Commit();
            }

            if (_pings != null)
            {
                _pings.Clear();
                if (found > 0 && ck > .005)
                {
                    var pings = new (Vector2 at, bool gold, double d)[_tears.Count + 1];
                    pings[0] = (_site, true, 0);
                    var order = new[] { 0, 2, 1 };
                    for (var j = 0; j < 3 && j < _tears.Count; j++) pings[j + 1] = (_tears[order[j]].at, false, .35 * (j + 1));
                    foreach (var (at, gold, d) in pings)
                    {
                        if (ur < 1.6 + d) continue;
                        var ph = ((ur - 1.6 - d) % 1.6 + 1.6) % 1.6;
                        if (ph > 1.1) continue;
                        var kk = ph / 1.1;
                        var col = gold ? new Color(1f, 226 / 255f, 150 / 255f, MenuV6Linear.Alpha(.9 * (1 - kk) * ck))
                            : new Color(191 / 255f, 230 / 255f, 1f, MenuV6Linear.Alpha(.9 * (1 - kk) * ck));
                        _pings.Circle(at.x, at.y, (6 + 34 * kk) * k1, 2.2 * k1, col);
                    }
                }

                _pings.Commit();
            }

            if (_sliver != null)
            {
                _sliver.Clear();
                var ks = Math.Max(MenuV6Math.Win(ur, 1.7, 1.85, 2.1, 2.4), RingMath.TearSliver(ur - 2.6, 3.1, 0)) * found * (v < 0 ? 1 : 0);
                if (ks > .01 && _cells.Count > 0)
                {
                    var cell = Blinking(t) ? Math.Min(_shutCell, _cells.Count - 1) : Math.Min(12, (int)Math.Round(12 * Math.Min(1, ks)));
                    _sliver.Rect(_sliverRect, _cells[cell], Color.white);
                }

                _sliver.Commit();
            }

            // the Ring over the map
            if (_ring != null)
            {
                var ra = MenuV6Math.Out(MenuV6Math.Seg(ur, .62, 1.05)) * (v < 0 ? 1 : 1 - MenuV6Math.InOut(MenuV6Math.Seg(vs, 0, .25 / SheetMath.SheetSp)));
                var sc = _rs * MenuV6Math.Lerp(.86, 1, EBackS(MenuV6Math.Seg(ur, .62, 1.1), 1.6));
                RingMath.Seek(ur, t, true, _from, _k, _glow, out var fnd);
                RingMath.Bearings(_rc.x, _rc.y, sc, _tx, _ty, _from, _k, t, _px, _py, _rot);
                _ring.S = sc;
                _ring.R = RingMath.R0 * sc;
                _ring.r = RingMath.Rr0 * sc;
                var rays = new List<RingDraw.RayTo>();
                foreach (var i in new[] { 0, 1, 2, 4 })
                    rays.Add(new RingDraw.RayTo
                    {
                        I = i, X = _tx[i], Y = _ty[i], Gold = i == 2,
                        A = MenuV6Math.Seg(_k[i], .8, 1) * (i == 2 ? 1 : .85) * (v < 0 ? 1 : 1 - MenuV6Math.Seg(vs, 0, .12))
                    });
                _ring.Draw(t, new RingDraw.State
                {
                    A = ra, I = .95 + .35 * fnd, Rot = _rot, PivX = _px, PivY = _py, Glow = _glow, RaysTo = rays.ToArray(), Found = 2,
                    Seed = 7, Sweep = 1, Aura = .6 + .6 * fnd, Motes = 22
                });
                if (_ringGlows != null)
                {
                    _ringGlows.Clear();
                    var ke = MenuV6Math.Win(ur, 1.62, 1.8, 2.25, 2.55) * (v < 0 ? 1 : 0) * ra;
                    var r = RingMath.Rr0 * sc;
                    if (ke > .01)
                    {
                        LockFx.Spr(_ringGlows, MenuV6Glow.Kind.GoldHot, _rc.x, _rc.y + r * .16, r * 1.3, .55 * ke);
                        LockFx.Spr(_ringGlows, MenuV6Glow.Kind.White, _rc.x, _rc.y + r * .16, r * .34, .6 * ke);
                    }

                    if (ur > 1.55 && ur < 2.1)
                    {
                        var kb = MenuV6Math.Seg(ur, 1.55, 2.1);
                        LockFx.Spr(_ringGlows, MenuV6Glow.Kind.BlueHot, _rc.x, _rc.y, 380 * k1 * kb, .45 * (1 - kb));
                    }

                    _ringGlows.Commit();
                }
            }

            // the caption: SEEKING -> FOUND at found .5
            var isFound = found >= .5;
            if (_head != null)
            {
                var want = isFound ? "FOUND · BATTLE CITY · NIGHT" : "SEEKING · BATTLE CITY · NIGHT";
                if (_head.text != want) _head.text = want;
            }

            if (_line1 != null)
            {
                var want = isFound ? "" : "the Ring’s pointers search the city";
                if (_line1.text != want) _line1.text = want;
            }

            if (_line2 != null)
            {
                var want = isFound ? _foundLine : "";
                if (_line2.text != want) _line2.text = want;
            }
        }

        static double EBackS(double t, double c1)
        {
            t = MenuV6Math.Clamp(t, 0, 1);
            var c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }
    }
}
