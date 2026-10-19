using System;

namespace WRLDZ.UI.MenuV6
{
    // The entrance's pure math (BUILD_PLAN §6 P2 steps 5-6): no engine calls, so the offline check runs the same code
    // against the page's own 60 Hz traces (handoff_<layout>.json / coldopen_<layout>.json).

    /// <summary>The page's entrance table (menu.js:645-646): E = { bg, stone, seam, door, rec, ui, carve } at time t.</summary>
    public struct EntranceE
    {
        public double Bg, Stone, Seam, Door, Rec, Ui, Carve;

        public static EntranceE At(bool enter, double t) => enter
            ? new EntranceE
            {
                Bg = MenuV6Math.Out((t - .45) / .9), Stone = 1, Seam = MenuV6Math.Clamp((t - 1.7) / 1.0, 0, 1),
                Door = MenuV6Math.Out((t - 1.35) / .6), Rec = .9, Ui = .55, Carve = MenuV6Math.InOut((t - 1.35) / .85)
            }
            : new EntranceE
            {
                Bg = MenuV6Math.Out((t - .05) / .9), Stone = MenuV6Math.Out((t - .2) / .9), Seam = MenuV6Math.Clamp((t - .55) / 1.0, 0, 1),
                Door = MenuV6Math.Out((t - .9) / .7), Rec = .95, Ui = .45, Carve = 1
            };
    }

    /// <summary>The boot handoff's and the cold open's pure motion (menu.js:403-406, :711-727, :844-855, relics.js:249-252),
    /// shared by <see cref="HandoffRig"/> / <see cref="KeystoneAssembly"/> and the offline check against the page's trace.</summary>
    public static class HandoffMath
    {
        /// <summary>hFront: the name gives way from its centre, 345 k over sstep(.45, 1.3, u).</summary>
        public static double Front(double u, double k1) => 345 * k1 * MenuV6Math.SStep(.45, 1.3, u);

        /// <summary>An Item's flight (drawHandoff): released or not, its centre, its draw size, its alpha, lift / fly / land.</summary>
        public static void Item(double u, double k1, double cx, double cy, double x, double y, int i, bool ring, double rel,
            double recA, double tgtX, double tgtY, out bool released, out double qx, out double qy, out double sz, out double alpha,
            out double lift, out double fly, out double land)
        {
            var fr = Front(u, k1);
            released = Math.Abs(x - cx) <= fr + 6 * k1;
            lift = fly = land = 0;
            if (!released && !ring)
            {
                qx = x;
                qy = y;
                sz = 36 * k1 / .86;
                alpha = 1;
                return;
            }

            lift = MenuV6Math.SStep(.5 + .05 * i, .75 + .05 * i, u) * MenuV6Math.SStep(rel, rel + .25, u);
            fly = MenuV6Math.InOut(MenuV6Math.Seg(u, 1.3 + .04 * i, 1.82 + .01 * i));
            land = MenuV6Math.Seg(u, 1.82 + .01 * i, 2.1);
            double p0x = x, p0y = y - 8 * k1 * lift;
            var a0 = Math.Atan2(p0y - cy, p0x - cx);
            var r0 = Math.Sqrt((p0x - cx) * (p0x - cx) + (p0y - cy) * (p0y - cy)) / k1;
            var a1 = (ring ? -90 : recA) * Math.PI / 180;
            const double Rb = 236;
            if (fly < .22)
            {
                var rr = MenuV6Math.Lerp(r0, Rb, MenuV6Math.InOut(fly / .22));
                qx = cx + Math.Cos(a0) * rr * k1;
                qy = cy + Math.Sin(a0) * rr * k1;
            }
            else
            {
                var f = MenuV6Math.InOut((fly - .22) / .78);
                var an = MenuV6Math.Lerp(a0, a1, f);
                double bx = cx + Math.Cos(an) * Rb * k1, by = cy + Math.Sin(an) * Rb * k1;
                var end = MenuV6Math.Seg(fly, .82, 1);
                qx = MenuV6Math.Lerp(bx, tgtX, end);
                qy = MenuV6Math.Lerp(by, tgtY, end);
            }

            sz = ring ? MenuV6Math.Lerp(.34, 1, fly) * k1 : MenuV6Math.Lerp(36, 17, fly) * k1 / .86;
            alpha = ring ? 1 - MenuV6Math.Seg(u, 1.8, 1.86) : 1 - land;
        }

        /// <summary>The Puzzle's drop (drawKeystone #enter, u &lt; 1.35): the anchor, the scale (PK units), the PT → PK crossfade,
        /// the flare.</summary>
        public static void Drop(double u, double sx, double sy, double kx, double ky, double s0, double k1, out double x, out double y,
            out double sc, out double xf, out double flare, out double lift)
        {
            lift = MenuV6Math.SStep(.35, .6, u) * 7 * k1;
            var k = MenuV6Math.InOut(MenuV6Math.Seg(u, .85, 1.35));
            flare = MenuV6Math.Win(u, .3, .42, .55, .95);
            x = MenuV6Math.Lerp(sx, kx, k);
            y = MenuV6Math.Lerp(sy - lift, ky, k) - Math.Sin(k * Math.PI) * 16 * k1;
            sc = MenuV6Math.Lerp(s0, 1, k);
            xf = MenuV6Math.Seg(u, .85, 1.0);
        }

        /// <summary>pzScatter (relics.js:249-252) for piece i of the cold open's assembly at t (k = seg(t, .95 + .06 i, 1.45 + .06 i)).</summary>
        public static bool Piece(double t, int i, double dirX, double dirY, double h, double h2, double k1, out double dx, out double dy,
            out double rot, out double a, out double th)
        {
            var k = MenuV6Math.Seg(t, .95 + i * .06, 1.45 + i * .06);
            dx = dy = rot = a = th = 0;
            if (k <= 0) return false;
            var an = Math.Atan2(dirY, dirX) + (h - .5) * 1.1;
            var R = (120 + 100 * h2) * k1;
            var e = MenuV6Math.Out(k);
            var sp = (25 + 35 * h) * (h2 > .5 ? 1 : -1) * Math.PI / 180;
            dx = Math.Cos(an) * R * (1 - e);
            dy = Math.Sin(an) * R * (1 - e) - 30 * k1 * Math.Sin(Math.PI * e) * (1 - e);
            rot = sp * (1 - e);
            a = MenuV6Math.Clamp(k * 3.5, 0, 1);
            th = 1 - e;
            return true;
        }

        /// <summary>The labels' / DUEL's / the bezels' entrance opacity (menu.js:1029-1031); label order set, bazaar, bag, deck,
        /// story, view.</summary>
        public static double Label(bool enter, double t, int i) => MenuV6Math.Out((t - (enter ? 1.72 : 1.05) - i * .05) / .45);
        public static double Duel(bool enter, double t) => MenuV6Math.Out((t - (enter ? 1.45 : 1.0)) / .5);
        public static double Bezel(bool enter, double t) => MenuV6Math.Out((t - (enter ? 1.5 : 1.15)) / .5);
    }

    /// <summary>The title's pure timing (title.js / anime_v61_title.js; title_&lt;layout&gt;.json schedules).</summary>
    public static class TitleMath
    {
        /// <summary>title.js origR: the base clock (the origin shot's 2.6 s freeze at base 7.4).</summary>
        public static double Base(double raw) => raw <= 7.4 ? raw : raw < 10.0 ? 7.4 : raw - 2.6;

        /// <summary>The DOM chrome's opacities at raw time <paramref name="raw"/> (TOUCH at <paramref name="leaving"/>, NaN = not
        /// yet): the time-jump caption (base 19.15-20.75), TOUCH TO BEGIN (in 20.8-21.45, breathing 72-100 %), the credit line,
        /// TAP TO SKIP (over the video only), the two placeholder strips (title_&lt;layout&gt;.json schedules; checked against the
        /// page's 60 Hz DOM trace offline).</summary>
        public static void Chrome(double raw, double leaving, out double cap, out double touch, out double copy, out double skip,
            out double ph5Art, out double ph5Genesis)
        {
            var tb = Base(raw);
            var lv = double.IsNaN(leaving) ? 0 : MenuV6Math.Seg(raw, leaving, leaving + .35);
            var tbT = MenuV6Math.Seg(tb, 20.8, 21.45);
            cap = MenuV6Math.Win(tb, 19.15, 19.6, 20.3, 20.75);
            touch = tbT * (.72 + .28 * Math.Sin((tb - 19) * 2.2)) * (1 - lv);
            copy = tbT * .8 * (1 - lv);
            skip = .75 * MenuV6Math.Seg(tb, .5, 1.2) * (1 - MenuV6Math.SStep(18.15, 18.5, tb));
            ph5Art = 1;
            ph5Genesis = (1 - MenuV6Math.Seg(tb, 15.85, 16.3)) * MenuV6Math.Seg(raw, .3, .8);
        }
    }
}
