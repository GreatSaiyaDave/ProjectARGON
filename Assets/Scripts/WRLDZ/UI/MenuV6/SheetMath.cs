using System;
using System.Collections.Generic;

namespace WRLDZ.UI.MenuV6
{
    // The sheets' pure math (BUILD_PLAN §6 P3): the Puzzle-lock reveal, the envelopes, the DUEL beats and the keystone's
    // flight, read from the live v6.3 page (menu.js:34-41, :569-600, :832-897, :1023-1074; sheets_<layout>.json "reveal",
    // "envelopes", "keystone", "duel.beats"). System types only, so the offline check runs the same code against the page's
    // own 60 Hz traces (sheets_trace_<layout>.json). u = seconds since the sheet opened, v = seconds since it began to close
    // (v < 0 = still open); "sheet time" = seconds × sp (1 for DUEL, 1.35 for every other sheet).

    /// <summary>Which of the page's sheet timelines a sheet runs.</summary>
    public enum SheetKind
    {
        Duel,
        Recess,
        Ring,
        Quit,
        Generic
    }

    public static class SheetMath
    {
        /// <summary>menu.js:36 SHEET_SP: every sheet but DUEL runs the Puzzle-lock 35 % faster.</summary>
        public const double SheetSp = 1.35;

        /// <summary>menu.js:38 PICK_BEAT: a picked mode card holds its "duel begins" beat before the sheet locks.</summary>
        public const double PickBeat = .95;

        /// <summary>menu.js:40 CLASP_HOLD: the clasp stays whole until the lintel has folded into it (sheet time).</summary>
        public const double ClaspHoldDuel = .4, ClaspHoldSheet = .76;

        /// <summary>menu.js:41 SHEET_END (DUEL, real s) / SHEET_END_S (recess, sheet time): when a closed sheet is gone; the Ring
        /// sheet ends at 1.95 sheet time.</summary>
        public const double SheetEnd = 1.95, SheetEndS = 2.45;

        /// <summary>menu.js:37 PZ_LEAD: the piece that reaches the clasp first (the lintel unfurls from it).</summary>
        public const int PzLead = 1;

        public static double Sp(SheetKind k) => k == SheetKind.Duel ? 1 : SheetSp;

        public static bool IsDuel(SheetKind k) => k == SheetKind.Duel;

        /// <summary>Sheet time of a real-time u / v (v &lt; 0 stays -1).</summary>
        public static void SheetTime(SheetKind k, double u, double v, out double us, out double vs)
        {
            var sp = Sp(k);
            us = u * sp;
            vs = v < 0 ? -1 : v * sp;
        }

        /// <summary>The real seconds after the close starts when the sheet is gone (the page clears it).</summary>
        public static double EndAfterClose(SheetKind k) => k switch
        {
            SheetKind.Duel => SheetEnd,
            SheetKind.Ring => SheetEnd / SheetSp,
            _ => SheetEndS / SheetSp
        };

        // ------------------------------------------------------------------ the Puzzle-lock reveal (menu.js:583-591)

        public struct Reveal
        {
            /// <summary>The lintel's revealed half-width (0..1) and the body's revealed height (0..1).</summary>
            public double H, B;
        }

        public static Reveal RevealAt(SheetKind k, double u, double v)
        {
            SheetTime(k, u, v, out var us, out var vs);
            var duel = IsDuel(k);
            double o0 = duel ? .3 : .28, o1 = duel ? .58 : .54, o2 = duel ? .5 : .46, o3 = duel ? .86 : .78;
            double c0 = duel ? .3 : .12, c1 = duel ? .56 : .8, c2 = duel ? .5 : .74, c3 = duel ? .7 : .98;
            var h = MenuV6Math.Out(MenuV6Math.Seg(us, o0, o1));
            var b = MenuV6Math.InOut(MenuV6Math.Seg(us, o2, o3));
            if (vs >= 0)
            {
                b = Math.Min(b, 1 - MenuV6Math.InOut(MenuV6Math.Seg(vs, c0, c1)));
                h = Math.Min(h, 1 - MenuV6Math.InOut(MenuV6Math.Seg(vs, c2, c3)));
            }

            return new Reveal { H = h, B = b };
        }

        public struct Clip
        {
            /// <summary>Stage px: the sheet is visible in [X0, X1] × [0, Y1] (no dissolve, menu.js:592).</summary>
            public double X0, X1, Y1, Cx;
        }

        /// <param name="fx">the frame's stage rect (x, y, w, h)</param>
        /// <param name="lh">its lintel height</param>
        public static Clip ClipBox(double fx, double fy, double fw, double fh, double lh, Reveal r)
        {
            var cx = fx + fw / 2;
            var hw = (fw / 2 + 8) * r.H;
            return new Clip { X0 = cx - hw, X1 = cx + hw, Y1 = MenuV6Math.Lerp(fy + lh + 7, fy + fh + 6, r.B), Cx = cx };
        }

        /// <summary>The seams of light on the moving edge (menu.js:593-600): the roll seam while 0 &lt; b &lt; 1, else the two
        /// unfurl seams while the lintel is still opening.</summary>
        public static void Seams(Reveal r, out double rollA, out double unfurlA)
        {
            rollA = r.B > .001 && r.B < .999 ? Math.Sin(Math.PI * r.B) * .9 : 0;
            unfurlA = r.B <= .001 && r.H < .999 && r.H > 0 ? Math.Sin(Math.PI * r.H) * .95 : 0;
        }

        // ------------------------------------------------------------------ envelopes (menu.js:575-582)

        public struct Env
        {
            public double Dim, Fill, Frame, Frame2, Content;
        }

        /// <summary>sheetK: the Ring / recess / QUIT sheets' envelopes (sheet time; cl(k) = v &lt; 0 ? 1 : 1 − k).</summary>
        public static Env K(SheetKind k, double u, double v)
        {
            SheetTime(k, u, v, out var us, out var vs);
            double Cl(double x) => vs < 0 ? 1 : 1 - x;
            return new Env
            {
                Dim = MenuV6Math.InOut(MenuV6Math.Clamp(us / .8, 0, 1)) * Cl(MenuV6Math.InOut(MenuV6Math.Clamp((vs - 1.16) / .9, 0, 1))),
                Fill = MenuV6Math.Out((us - .34) / .4) * Cl(MenuV6Math.InOut(MenuV6Math.Clamp((vs - .12) / .64, 0, 1))),
                Frame = MenuV6Math.Out((us - .2) / .5) * Cl(MenuV6Math.InOut(MenuV6Math.Clamp((vs - .08) / .6, 0, 1))),
                Frame2 = MenuV6Math.Out((us - .4) / .5) * Cl(MenuV6Math.InOut(MenuV6Math.Clamp((vs - .04) / .56, 0, 1))),
                Content = MenuV6Math.Out((us - .7) / .35) * Cl(MenuV6Math.InOut(MenuV6Math.Clamp((vs - .76) / .06, 0, 1)))
            };
        }

        /// <summary>The #dim opacity (menu.js:1025-1035): DUEL dimC (radial at the door), every other sheet .9 K.dim (flat);
        /// opacity = dim × .8.</summary>
        public static double DimOpacity(SheetKind k, double u, double v)
        {
            if (IsDuel(k))
            {
                var d = MenuV6Math.InOut(MenuV6Math.Clamp(u / .8, 0, 1)) *
                        (v < 0 ? 1 : 1 - MenuV6Math.InOut(MenuV6Math.Clamp((v - .85) / .8, 0, 1)));
                return d * .8;
            }

            return .9 * K(k, u, v).Dim * .8;
        }

        /// <summary>The dim value itself (before × .8): the DOM plates under a sheet keep a (1 − .78 dim).</summary>
        public static double Dim(SheetKind k, double u, double v) => DimOpacity(k, u, v) / .8;

        public static double RoomDom(double dim) => 1 - .78 * dim;

        /// <summary>#sht opacity clamp(content × 1.4) (menu.js:983-985); the Ring's #rov takes content as it is.</summary>
        public static double ContentA(SheetKind k, double u, double v)
        {
            var c = K(k, u, v).Content;
            return k == SheetKind.Ring ? MenuV6Math.Clamp(c, 0, 1) : MenuV6Math.Clamp(c * 1.4, 0, 1);
        }

        /// <summary>Pointer events when content &gt; .5 and the sheet is not closing.</summary>
        public static bool ContentLive(SheetKind k, double u, double v) => v < 0 && K(k, u, v).Content > .5;

        // ------------------------------------------------------------------ the sheet's own fractures (cracks_sheets_*.json timing)

        /// <summary>grow = eIO(seg(u_s, .9, 2.0)); E = L clamp(rest grow + .15 (1 − exp(−max(0, u_s − 2) / 30)), 0, 1).</summary>
        public static double CrackExtent(double len, double rest, double us)
        {
            var grow = MenuV6Math.InOut(MenuV6Math.Seg(us, .9, 2.0));
            if (grow <= 0) return 0;
            return len * MenuV6Math.Clamp(rest * grow + .15 * (1 - Math.Exp(-Math.Max(0, us - 2) / 30)), 0, 1);
        }

        // ------------------------------------------------------------------ CHOOSE YOUR DUEL (menu.js:1023-1074, :804-830)

        public static double DoorOpen(double u, double v) =>
            MenuV6Math.InOut((u - .1) / .55) * (v < 0 ? 1 : 1 - MenuV6Math.InOut((v - .5) / .45));

        /// <summary>The lock's jolt of the door halves (px at k = 1).</summary>
        public static double DoorJolt(double u) => MenuV6Math.Win(u, .62, .64, .66, .7) * 2.2;

        public static double DoorLight(double u, double v, bool picked)
        {
            if (picked && v >= 0) return MenuV6Math.Win(v, 0, .1, .3, .6);
            return MenuV6Math.Win(u, .55, .75, 1.05, 1.5) + (v >= 0 ? MenuV6Math.Win(v, .05, .15, .35, .6) : 0);
        }

        /// <summary>The crest's scale c (scaleX c, scaleY min(1, 1.2 c), opacity clamp(4 c)).</summary>
        public static double Crest(double u, double v) =>
            MenuV6Math.Back((u - .72) / .45) * (1 - (v < 0 ? 0 : MenuV6Math.InOut((v - .5) / .2)));

        /// <summary>The title: opacity, and its track-in tkIn (tracking lerp(.36 land / .32 port, .22, tkIn) em).</summary>
        public static double Title(double u, double v, out double tkIn)
        {
            tkIn = MenuV6Math.Out((u - .75) / .55);
            return tkIn * (1 - (v < 0 ? 0 : MenuV6Math.Out(v / .22)));
        }

        public static double PickK(double pk) => pk >= 0 ? MenuV6Math.Out(MenuV6Math.Seg(pk, 0, .3)) : 0;

        public static double Subtitle(double u, double v, double pickK) =>
            MenuV6Math.Out((u - .95) / .5) * (1 - (v < 0 ? 0 : MenuV6Math.Out(v / .3))) * (1 - pickK);

        public static double Begins(double v, double pickK) => pickK * (1 - (v < 0 ? 0 : MenuV6Math.Out(v / .3)));

        public struct Card
        {
            public double X, Y, Scale, RotDeg, Opacity, Flip, Glow, Bright;
            public bool Hl;
        }

        /// <param name="i">card index 0..2</param>
        /// <param name="dx">the door centre</param>
        /// <param name="xs">its resting x</param>
        /// <param name="cy">the cards' resting y</param>
        /// <param name="rest">its resting rotation (deg)</param>
        /// <param name="lift">its hover lift 0..1 (1 for the picked card)</param>
        /// <param name="pick">the picked card (-1 = none)</param>
        /// <param name="t">the page clock (the picked card's brightness pulse)</param>
        public static Card CardAt(int i, double u, double v, double dx, double dy, double xs, double cy, double rest, double lift,
            int pick, double pickK, double t)
        {
            var deal = MenuV6Math.Out5((u - .7 - i * .07) / .55);
            var ret = v < 0 ? 0 : MenuV6Math.InOut((v - .08 - (2 - i) * .04) / .26);
            var flip = MenuV6Math.InOut((u - 1.3 - i * .09) / .45) * (1 - (v < 0 ? 0 : MenuV6Math.InOut((v - (2 - i) * .03) / .3)));
            var k = deal * (1 - ret);
            if (pick == i && (v >= 0 || pickK > 0)) lift = 1;
            var me = pick == i ? pickK : 0;
            var other = pick >= 0 && pick != i ? pickK : 0;
            var c = new Card
            {
                X = MenuV6Math.Lerp(dx, xs, k) + (dx - xs) * .07 * me,
                Y = MenuV6Math.Lerp(dy, cy, k) - lift * 22 - me * 10 + other * 16,
                Scale = MenuV6Math.Lerp(v < 0 ? .3 : .08, 1, k) * (1 + lift * .04 + me * .05 - other * .06),
                RotDeg = MenuV6Math.Lerp(0, rest, k) * (1 - me) + MenuV6Math.Lerp(-90 + i * 30, 0, deal) * (1 - deal),
                Opacity = MenuV6Math.Clamp(deal * 3, 0, 1) * (1 - MenuV6Math.Seg(ret, .72, 1)) * (1 - .6 * other),
                Flip = flip,
                Glow = lift * .9 + .15 * flip,
                Hl = lift > .5,
                Bright = me > .01 ? 1 + .25 * me * (.7 + .3 * Math.Sin(t * 9)) : 1
            };
            return c;
        }

        public static double Caption(int i, double u, double v, double lift) =>
            MenuV6Math.Out((u - 1.6 - i * .07) / .4) * (1 - (v < 0 ? 0 : MenuV6Math.Out(v / .25))) * (.75 + .25 * lift);

        /// <summary>The armed row, YOU · STAY · OPP and the stand-in note.</summary>
        public static double Armed(double u, double v) => MenuV6Math.Out((u - 1.65) / .5) * (1 - (v < 0 ? 0 : MenuV6Math.Out(v / .25)));

        /// <summary>The CSS hover tween of a card (.2 s eOut from the other end).</summary>
        public static double Hover(bool on, double since) => MenuV6Math.Lerp(on ? 0 : 1, on ? 1 : 0, MenuV6Math.Out(since / .2));

        // ------------------------------------------------------------------ the keystone's flight (menu.js:832-897)

        public enum OpKind
        {
            Intact,      // the whole Puzzle: drawShadow(.5 a) + draw (idle state)
            Piece,       // drawPieceAbs(i, x, y, rot, s, a, th, eye)
            Spr,         // drawSpr(kind, x, y, size, a) 'lighter'
            Rays,        // drawRays(x, y, r, a, t)
            Flash,       // drawLockFlash(x, y, r, k)
            Embers       // burstEmbers(x, y, k)
        }

        public enum Glow
        {
            Gold,
            GoldHot,
            Blue,
            BlueHot,
            White
        }

        public struct Op
        {
            public OpKind Kind;
            public int I;
            public double X, Y, Rot, S, A, Th, Eye, R, K;
            public Glow G;
        }

        /// <summary>The PK geometry the flight needs (coldopen_&lt;layout&gt;.json bakes.PK).</summary>
        public sealed class Pk
        {
            public double Kx, Ky, K1 = 1;
            public double OriginDx, OriginDy;   // K − origin at scale 1 (= (X0 + ANC) wpx)
            public double CentreDx, CentreDy;   // centre − origin at scale 1
            public double S = 2;
            public readonly List<(double cbx, double cby, double dx, double dy, double h, double h2)> Pieces = new();

            public void Origin(double ax, double ay, double sc, out double ox, out double oy)
            {
                ox = ax - OriginDx * sc;
                oy = ay - OriginDy * sc;
            }

            public void Home(int i, double ax, double ay, double sc, out double x, out double y)
            {
                Origin(ax, ay, sc, out var ox, out var oy);
                var p = Pieces[i];
                x = ox + p.cbx / S * sc;
                y = oy + p.cby / S * sc;
            }

            public void CentreAt(double ax, double ay, double sc, out double x, out double y)
            {
                Origin(ax, ay, sc, out var ox, out var oy);
                x = ox + CentreDx * sc;
                y = oy + CentreDy * sc;
            }
        }

        static void Bez(double ax, double ay, double bx, double by, double cx, double cy, double k, out double x, out double y)
        {
            x = (1 - k) * (1 - k) * ax + 2 * (1 - k) * k * bx + k * k * cx;
            y = (1 - k) * (1 - k) * ay + 2 * (1 - k) * k * by + k * k * cy;
        }

        static double Brot(double h, double h2) => (8 + 6 * h2) * (h > .5 ? 1 : -1) * Math.PI / 180;

        /// <summary>
        /// The Puzzle-lock's draw ops at this frame (menu.js drawKeystone, the contract branch). <paramref name="eyeIdle"/> is
        /// the keystone's idle eye value (KeystoneRig); the returned bool says whether the keystone at the door is drawn by
        /// these ops (false = the Stone's own keystone rig may show it).
        /// </summary>
        /// <param name="ca">the clasp anchor (SH.crest)</param>
        /// <param name="sc">the clasp scale (SH.cs)</param>
        /// <param name="cardFlare">1 while a mode card is lit (DUEL only)</param>
        /// <param name="t">the page clock (the jewel's breathing, the rays' turn)</param>
        public static void Keystone(Pk pk, SheetKind kind, double u, double v, double ca, double cb, double sc, double cardFlare,
            double eyeIdle, double t, List<Op> ops)
        {
            ops.Clear();
            var k1 = pk.K1;
            double kx = pk.Kx, ky = pk.Ky;
            var duel = IsDuel(kind);
            SheetTime(kind, u, v, out var us, out var vs0);
            var vs = vs0 < 0 ? -1 : Math.Max(0, vs0 - (duel ? ClaspHoldDuel : ClaspHoldSheet));
            var rayR = (duel ? 110 : 76) * k1;
            var n = pk.Pieces.Count;
            var eye = eyeIdle;
            if (vs < 0)
            {
                const double ANT = .1;
                if (us < ANT)
                {
                    var q = MenuV6Math.InOut(us / ANT);
                    ops.Add(new Op { Kind = OpKind.Intact, X = kx, Y = ky, S = 1 - .045 * q, A = 1, Eye = MenuV6Math.Lerp(eye, 1.7, q) });
                    return;
                }

                if (us >= 1.06)
                {
                    pk.CentreAt(ca, cb, sc, out var fx, out var fy);
                    ops.Add(new Op { Kind = OpKind.Rays, X = fx, Y = fy, R = rayR, A = .3 + .1 * Math.Sin(t * 1.4) + .1 * cardFlare });
                    ops.Add(new Op { Kind = OpKind.Intact, X = ca, Y = cb, S = sc, A = 1, Eye = .5 + .08 * Math.Sin(t * 2) + .18 * cardFlare });
                    ops.Add(new Op { Kind = OpKind.Flash, X = fx, Y = fy, R = 22 * k1, K = MenuV6Math.Seg(us, 1.06, 1.18) });
                    return;
                }

                var bxu = MenuV6Math.Seg(us, ANT, .42);
                var sepB = MenuV6Math.InOut(bxu);
                var drift = .16 * MenuV6Math.Out(MenuV6Math.Seg(us, .36, 1.0));
                var sep = sepB + drift;
                var hr = sepB + 2.5 * drift;
                var ps = MenuV6Math.Lerp(.955, 1, MenuV6Math.Out(bxu));
                pk.CentreAt(kx, ky, 1, out var cx0, out var cy0);
                var pour = sepB * (1 - MenuV6Math.Seg(us, .5, .85)) * (duel ? 1 : .6);
                Spr(ops, Glow.GoldHot, cx0, cy0, 150 * k1, .55 * pour);
                Spr(ops, Glow.BlueHot, cx0, cy0, 90 * k1, .5 * pour);
                Spr(ops, Glow.White, cx0, cy0, 80 * k1, .32 * MenuV6Math.Win(us, ANT, ANT + .09, ANT + .13, ANT + .32));
                pk.CentreAt(ca, cb, sc, out var cx1, out var cy1);
                var la = MenuV6Math.Seg(us, .3, .44);
                if (la > 0 && la < 1)
                {
                    Spr(ops, Glow.GoldHot, cx1, cy1, 120 * k1, .7 * (1 - la));
                    Spr(ops, Glow.White, cx1, cy1, 50 * k1, .6 * (1 - la));
                }

                for (var i = 0; i < n; i++)
                {
                    var pc = pk.Pieces[i];
                    pk.Home(i, kx, ky, ps, out var hx, out var hy);
                    var R = (34 + 22 * pc.h) * k1 * sep;
                    double p0x = hx + pc.dx * R, p0y = hy + pc.dy * R;
                    var j = i == PzLead ? -1 : i < PzLead ? i : i - 1;
                    var k = j < 0
                        ? MenuV6Math.InOut(MenuV6Math.Seg(us, ANT + .05, .32))
                        : MenuV6Math.InOut(MenuV6Math.Seg(us, .32 + .03 * j, .82 + .03 * j));
                    if (j < 0 && k > 0)
                    {
                        pk.Home(i, ca, cb, sc, out var ccx, out var ccy);
                        double p1x = (p0x + ccx) / 2 + 30 * k1, p1y = Math.Min(p0y, ccy) - 30 * k1;
                        Bez(p0x, p0y, p1x, p1y, ccx, ccy, k, out var qx, out var qy);
                        ops.Add(new Op
                        {
                            Kind = OpKind.Piece, I = i, X = qx, Y = qy, Rot = (1 - k) * .5 * hr, S = MenuV6Math.Lerp(ps, sc, k), A = 1,
                            Th = (1 - k) * .9 * sepB, Eye = .9 * sepB
                        });
                        Spr(ops, Glow.GoldHot, qx, qy, 60 * k1, .6 * Math.Sin(Math.PI * Math.Min(1, k * 1.1)));
                        for (var m = 1; m < 7; m++)
                        {
                            Bez(p0x, p0y, p1x, p1y, ccx, ccy, Math.Max(0, k - m * .05), out var mx, out var my);
                            Spr(ops, Glow.Gold, mx, my, (30 - m * 3) * k1, .35 * (1 - m / 7.0) * Math.Sin(Math.PI * k));
                        }

                        continue;
                    }

                    if (k <= 0)
                    {
                        ops.Add(new Op
                        {
                            Kind = OpKind.Piece, I = i, X = p0x, Y = p0y, Rot = Brot(pc.h, pc.h2) * hr, S = ps, A = 1, Th = sepB,
                            Eye = .8 * sepB
                        });
                        continue;
                    }

                    {
                        pk.Home(i, ca, cb, sc, out var ccx, out var ccy);
                        double p1x = (p0x + ccx) / 2 + pc.dx * 70 * k1, p1y = Math.Min(p0y, ccy) - 60 * k1;
                        Bez(p0x, p0y, p1x, p1y, ccx, ccy, k, out var qx, out var qy);
                        ops.Add(new Op
                        {
                            Kind = OpKind.Piece, I = i, X = qx, Y = qy,
                            Rot = Brot(pc.h, pc.h2) * hr * (1 - k) + (pc.h2 - .5) * 1.4 * Math.Sin(Math.PI * k),
                            S = MenuV6Math.Lerp(ps, sc, k), A = 1, Th = (1 - k) * .9, Eye = .6
                        });
                        Spr(ops, Glow.Gold, qx, qy, 40 * k1, .35 * Math.Sin(Math.PI * k));
                    }
                }

                return;
            }

            // close: the jewel gathers, bursts, and the pieces fly home and lock
            if (vs < .22)
            {
                pk.CentreAt(ca, cb, sc, out var fx, out var fy);
                ops.Add(new Op { Kind = OpKind.Rays, X = fx, Y = fy, R = rayR, A = .5 });
                ops.Add(new Op { Kind = OpKind.Intact, X = ca, Y = cb, S = sc * (1 - .04 * MenuV6Math.InOut(MenuV6Math.Seg(vs, .1, .22))), A = 1, Eye = 1.5 });
                return;
            }

            if (vs < 1.06)
            {
                var sb = MenuV6Math.InOut(MenuV6Math.Seg(vs, .22, .42));
                var sep = sb + .08 * MenuV6Math.Out(MenuV6Math.Seg(vs, .36, .9));
                var psc = sc * (1 - .04 * (1 - MenuV6Math.Out(MenuV6Math.Seg(vs, .22, .42))));
                var th0 = Math.Min(1, sb * 2);
                for (var i = 0; i < n; i++)
                {
                    var pc = pk.Pieces[i];
                    pk.Home(i, ca, cb, psc, out var ccx, out var ccy);
                    var R = (34 + 22 * pc.h) * k1 * (.45 * sc * sep);
                    double p0x = ccx + pc.dx * R, p0y = ccy + pc.dy * R;
                    pk.Home(i, kx, ky, 1, out var hx, out var hy);
                    var k = MenuV6Math.Out(MenuV6Math.Seg(vs, .34 + .03 * i, .84 + .03 * i));
                    var spin = (25 + 35 * pc.h) * (pc.h2 > .5 ? 1 : -1) * Math.PI / 180;
                    double p1x = hx + pc.dx * (120 + 100 * pc.h) * k1, p1y = hy + pc.dy * (120 + 100 * pc.h) * k1 - 40 * k1;
                    Bez(p0x, p0y, p1x, p1y, hx, hy, k, out var qx, out var qy);
                    ops.Add(new Op
                    {
                        Kind = OpKind.Piece, I = i, X = qx, Y = qy,
                        Rot = Brot(pc.h, pc.h2) * sep * .5 * (1 - k) + spin * .7 * Math.Sin(Math.PI * k), S = MenuV6Math.Lerp(psc, 1, k), A = 1,
                        Th = th0 * (1 - k), Eye = .5 * th0 * (1 - k)
                    });
                }

                return;
            }

            {
                pk.CentreAt(kx, ky, 1, out var fx, out var fy);
                ops.Add(new Op { Kind = OpKind.Flash, X = fx, Y = fy, R = 34 * k1, K = MenuV6Math.Seg(vs, 1.06, 1.18) });
                ops.Add(new Op { Kind = OpKind.Embers, X = fx, Y = fy, K = MenuV6Math.Seg(vs, 1.07, 1.6) });
                eye += 1.3 * MenuV6Math.SStep(1.06, 1.2, vs) * (1 - MenuV6Math.Seg(vs, 1.3, 1.7));
                ops.Add(new Op { Kind = OpKind.Intact, X = kx, Y = ky, S = 1, A = 1, Eye = eye });
            }
        }

        static void Spr(List<Op> ops, Glow g, double x, double y, double size, double a)
        {
            if (a <= .003) return;
            ops.Add(new Op { Kind = OpKind.Spr, G = g, X = x, Y = y, R = size, A = a });
        }
    }
}
