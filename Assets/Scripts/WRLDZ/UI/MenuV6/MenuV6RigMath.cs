using System;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The per-frame formulas of the P1b idle rigs (BUILD_PLAN §6 P1b), ported from the page as the exporter recorded
    /// them (<c>Data/rigs_&lt;layout&gt;.json</c>, every formula with its page line) and from the executable reference
    /// <c>unity_build/export/p1b/rigs_check.py</c>. System types only (doubles, no UnityEngine), so the offline harness
    /// checks them against the page's own numbers (the flame test vectors, the voice table, the Ring rotation samples,
    /// the glint picks) without an Editor. The drawing lives in <see cref="MenuV6Rigs"/>.
    /// </summary>
    public static class MenuV6RigMath
    {
        public const double Tau = Math.PI * 2;

        static double Clamp(double x, double a, double b) => MenuV6Math.Clamp(x, a, b);

        public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        static double Lerp(double a, double b, double t) => MenuV6Math.Lerp(a, b, t);
        static double SStep(double a, double b, double x) => MenuV6Math.SStep(a, b, x);
        static double Win(double t, double a, double b, double c, double d) => MenuV6Math.Win(t, a, b, c, d);
        static double Out(double t) => MenuV6Math.Out(t);
        static double Back(double t) => MenuV6Math.Back(t);
        static double InOut(double t) => MenuV6Math.InOut(t);

        /// <summary>JS <c>a % b</c> for b &gt; 0 brought into [0, b) (the page's clocks only take t ≥ 0, so this is the
        /// page's own value there).</summary>
        public static double Mod(double a, double b)
        {
            var r = a % b;
            return r < 0 ? r + b : r;
        }

        // ------------------------------------------------------------------ hover / press (menu.js:547-548, :741)

        /// <summary>One control's hover tween: pointerenter / pointerleave set {from: the current value, to: 1 | 0, t0: now}.</summary>
        public struct HoverTween
        {
            public bool Set;
            public double From, To, T0;

            /// <summary>menu.js:548 hovVal: lerp(from, to, eOut((t − t0) / .2)); 0 before the first event.</summary>
            public double Value(double t) => !Set ? 0 : Lerp(From, To, Out((t - T0) / .2));

            /// <summary>Start a tween towards <paramref name="to"/> from the value at <paramref name="t"/>.</summary>
            public void Go(double to, double t)
            {
                var v = Value(t);
                From = v;
                To = to;
                T0 = t;
                Set = true;
            }
        }

        /// <summary>A press decay: max(0, 1 − (t − tDown) / dur) (recess .45 s, door / niche .4 s); 0 with no press.</summary>
        public static double Press(double t, double tDown, double dur) =>
            double.IsNaN(tDown) ? 0 : Math.Max(0, 1 - (t - tDown) / dur);

        /// <summary>The press burst's age, taken the page's float way (press time = t − age, so age = (1 − pk) · dur):
        /// an age of exactly .05 then falls on the page's side of the ray-length branch (rigs_check.py).</summary>
        public static double PressAge(double pk, double dur) => (1 - pk) * dur;

        /// <summary>The burst's opacity bk and shoot-out sh at <paramref name="age"/> (recess and door rays).</summary>
        public static void Burst(double age, out double bk, out double sh)
        {
            bk = age < .05 ? 1 : 1 - Clamp((age - .05) / .12, 0, 1);
            sh = Out(Clamp(age / .15, 0, 1));
        }

        // ------------------------------------------------------------------ recesses (anime_menu_v6.js:97-127)

        /// <summary>k = clamp(v + .6 pk, 0, 1.6) (the entrance term is 0 at the idle, t &gt; 2.35 s).</summary>
        public static double RecessK(double v, double pk) => Clamp(v + .6 * pk, 0, 1.6);

        /// <summary>pop = 1 + .06 eBack(clamp(k)) − .02 win(k, 0, .05, .1, .2) (anticipation, then overshoot).</summary>
        public static double RecessPop(double k) => 1 + .06 * Back(Clamp(k, 0, 1)) - .02 * Win(k, 0, .05, .1, .2);

        /// <summary>Recess press ray q of 12: base radius r0, tip radius r1, half width ww (tip at angle an).</summary>
        public static void RecessRay(int q, double rr, double age, double sh, out double an, out double r0, out double r1,
            out double ww)
        {
            an = q / 12.0 * Tau + .13;
            r0 = rr * (1.15 + .1 * (q % 3));
            r1 = r0 + rr * (.5 + .25 * (q * 7 % 3)) * (age < .05 ? 1 : 1 + age * 2) * sh;
            ww = rr * .07;
        }

        // ------------------------------------------------------------------ the door (anime_menu_v6.js:131-139)

        /// <summary>The 5-slot sweep: slot k's weight at t (zs = (t mod 5.2) / 1.6).</summary>
        public static double DoorSweep(double t, int k)
        {
            var zs = Mod(t, 5.2) / 1.6;
            return Win(zs, k * .16, k * .16 + .12, k * .16 + .3, k * .16 + .5);
        }

        /// <summary>The slot's angle: (126 − 18 k) degrees.</summary>
        public static double DoorSweepAngle(int k) => (126 - k * 18) * Math.PI / 180;

        /// <summary>Door press ray q of 14.</summary>
        public static void DoorRay(int q, double R0, double sh, out double an, out double r0, out double r1, out double ww)
        {
            an = q / 14.0 * Tau;
            r0 = R0 * 1.12;
            r1 = r0 + (R0 * (1.5 + .12 * (q % 3)) - r0) * sh;
            ww = R0 * .045;
        }

        // ------------------------------------------------------------------ the Puzzle keystone (menu.js:832-841)

        /// <summary>The Wedjat's emissive drive: .68 + .14 sin(1.1 t) + .45 hovDoor.</summary>
        public static double KeystoneEye(double t, double hovDoor) => .68 + .14 * Math.Sin(t * 1.1) + .45 * hovDoor;

        /// <summary>The glint phase ((t + 1.2) mod 6.2) / .9 (a frame while in [0, 1)), else −1.</summary>
        public static double KeystoneGlint(double t)
        {
            var g = Mod(t + 1.2, 6.2) / .9;
            return g < 1 ? g : -1;
        }

        /// <summary>The seam hot point's phase ((t + 3.1) mod 7.5) / 1.3, else −1.</summary>
        public static double KeystoneHot(double t)
        {
            var h = Mod(t + 3.1, 7.5) / 1.3;
            return h < 1 ? h : -1;
        }

        /// <summary>The iris glint: sin(pi i) while i = (t mod 4.3) / .5 &lt; 1, else 0.</summary>
        public static double KeystoneIris(double t)
        {
            var i = Mod(t, 4.3) / .5;
            return i < 1 ? Math.Sin(i * Math.PI) : 0;
        }

        /// <summary>relics.js:206-212: the emissive passes of the eye drive <paramref name="eye"/> (ek = clamp(eye, 0, 2)):
        /// pass 1 at min(1, ek), pass 2 at ek − 1 when ek &gt; 1; <paramref name="ga"/> = the last pass's alpha (the page
        /// never resets globalAlpha after them, so the eye glow, the hot point and the iris glint are multiplied by it);
        /// with no pass (ek ≤ .01) ga is the alpha before them (<paramref name="gaBefore"/>).</summary>
        public static void Emissive(double eye, double gaBefore, out double ek, out double pass1, out double pass2,
            out double ga)
        {
            ek = Clamp(eye, 0, 2);
            pass1 = pass2 = 0;
            ga = gaBefore;
            if (ek <= .01) return;
            pass1 = Math.Min(1, ek);
            ga = pass1;
            if (ek > 1)
            {
                pass2 = ek - 1;
                ga = pass2;
            }
        }

        /// <summary>A point at <paramref name="u"/> ∈ [0, 1] along a polyline (the seam hot point).</summary>
        public static void Polyline(double[] xs, double[] ys, double u, out double x, out double y)
        {
            var n = xs.Length - 1;
            var s = u * n;
            var k = Math.Min(n - 1, (int)Math.Floor(s));
            if (k < 0) k = 0;
            x = Lerp(xs[k], xs[k + 1], s - k);
            y = Lerp(ys[k], ys[k + 1], s - k);
        }

        // ------------------------------------------------------------------ THE PHARAOH'S VOICE (menu.js:601-607)

        /// <summary>The voice clock: u = (t − 6) mod 22 for t &gt; 6 (−1 before: the whole line, opacity 1).</summary>
        public static double VoiceU(double t)
        {
            if (t <= 6) return -1;
            var n = Math.Floor((t - 6) / 22);
            return t - 6 - n * 22;
        }

        /// <summary>The Puzzle glow k = win(u, 0, .4, 2.3, 2.8) (0 before the first cycle).</summary>
        public static double VoiceGlow(double u) => u >= 0 ? Win(u, 0, .4, 2.3, 2.8) : 0;

        /// <summary>The typing: characters shown and the line's opacity. u &lt; .36: the old line fades out
        /// (1 − eIO(u / .32)); from .36 the same line re-types, round(len · clamp((u − .42) / 1.3, 0, 1)) characters.</summary>
        public static void VoiceTyping(double u, int len, out int chars, out double opacity)
        {
            if (u < 0)
            {
                chars = len;
                opacity = 1;
                return;
            }

            if (u < .36)
            {
                chars = len;
                opacity = 1 - InOut(u / .32);
                return;
            }

            var typed = Clamp((u - .42) / 1.3, 0, 1);
            chars = (int)Math.Round(len * typed, MidpointRounding.AwayFromZero);
            opacity = 1;
        }

        /// <summary>The voice Puzzle's glint phase (t + 2) mod 5.5 (a frame while &lt; 1), else −1.</summary>
        public static double VoiceGlint(double t)
        {
            var g = Mod(t + 2, 5.5);
            return g < 1 ? g : -1;
        }

        /// <summary>relics.js:262 drawRays ray k of 20: half-angle .07, length r (k odd ? .62 : 1)(.85 + .15 sin(1.3 t + k)),
        /// the whole fan rotated by .12 t.</summary>
        public static void Ray(int k, double r, double t, out double an, out double len)
        {
            an = k / 20.0 * Tau + t * .12;
            len = r * (k % 2 == 1 ? .62 : 1) * (.85 + .15 * Math.Sin(t * 1.3 + k));
        }

        // ------------------------------------------------------------------ Nut (core.js:476-489)

        /// <summary>A star's twinkle f = max(0, sin(t (.35 + .5 tw) + 40 tw))^12.</summary>
        public static double Twinkle(double t, double tw) => Math.Pow(Math.Max(0, Math.Sin(t * (.35 + tw * .5) + tw * 40)), 12);

        /// <summary>The sun's 30 s journey: position at t and its visibility.</summary>
        public static void Sun(double t, double inX, double inY, double outX, double outY, double top, double side,
            double R, double W, out double x, out double y, out double vis, out double ph)
        {
            ph = Mod(t, 30) / 30;
            var k = InOut(ph);
            var yb = top * .5;
            if (k < .12)
            {
                var u = k / .12;
                x = Lerp(inX, side + R * .3, u);
                y = Lerp(inY, yb, u);
            }
            else if (k < .88)
            {
                var u = (k - .12) / .76;
                x = Lerp(side + R * .3, W - side - R * .3, u);
                y = yb;
            }
            else
            {
                var u = (k - .88) / .12;
                x = Lerp(W - side - R * .3, outX, u);
                y = Lerp(yb, outY, u);
            }

            vis = SStep(0, .05, ph) * (1 - SStep(.95, 1, ph));
        }

        // ------------------------------------------------------------------ the Ring (relics.js:333-374, menu.js:625-640)

        /// <summary>The idle sway of pointer i: .025 sin(.9 t + 1.3 i) + .012 sin(2.3 t + i) + .12 hovR sin(5 t + i).</summary>
        public static double RingRot(double t, int i, double hovR = 0) =>
            .025 * Math.Sin(t * .9 + i * 1.3) + .012 * Math.Sin(t * 2.3 + i) + .12 * hovR * Math.Sin(t * 5 + i);

        /// <summary>The Ring's shimmer: .84 + .1 sin(3.1 t + seed) + .06 sin(7.3 t + 1.7 seed).</summary>
        public static double RingShim(double t, double seed) =>
            .84 + .1 * Math.Sin(t * 3.1 + seed) + .06 * Math.Sin(t * 7.3 + seed * 1.7);

        /// <summary>The refraction offset (device-independent stage px): (1.3 s sin(2.3 t + seed), .9 s cos(1.9 t + seed)).</summary>
        public static void RingRefract(double t, double s, double seed, out double dx, out double dy)
        {
            dx = 1.3 * s * Math.Sin(t * 2.3 + seed);
            dy = .9 * s * Math.Cos(t * 1.9 + seed);
        }

        /// <summary>Ring mote k of n: position relative to the centre and its alpha factor sin(pi ph) (× .7 I by the caller).</summary>
        public static void RingMote(int k, int n, double t, double seed, double R, out double dx, out double dy,
            out double a)
        {
            var ph = Mod(t * (.22 + .05 * (k % 3)) + (double)k / n + seed * .13, 1);
            var an = k * 2.39996 + seed;
            var rr = R * (.9 + .55 * ph);
            dx = Math.Cos(an) * rr;
            dy = Math.Sin(an) * rr * .9 - ph * R * .55;
            a = Math.Sin(ph * Math.PI);
        }

        /// <summary>relics.js:381-392 ringBearings for the niche Ring: pivots and rotations of the 5 pointers aiming at
        /// <paramref name="aimX"/> / <paramref name="aimY"/> (NaN = no target), with the idle "from" rotations.</summary>
        public static void RingBearings(double x, double y, double s, double[] aimX, double[] aimY, double[] from,
            double[] kk, double t, double[] pivX, double[] pivY, double[] rot, int lead = 2)
        {
            int[] att = { 0, 58, 90, 122, 180 };
            var Rr = (48 + 3.2) * s;
            for (var i = 0; i < 5; i++)
            {
                var a0 = att[i] * Math.PI / 180;
                pivX[i] = x + Math.Cos(a0) * Rr;
                pivY[i] = y + Math.Sin(a0) * Rr + 3 * s;
            }

            for (var i = 0; i < 5; i++)
            {
                var f0 = from[i];
                if (double.IsNaN(aimX[i]))
                {
                    rot[i] = f0;
                    continue;
                }

                var ki = Clamp(kk[i], 0, 1.2);
                if (i == lead)
                {
                    var a0 = att[i] * Math.PI / 180;
                    var a1 = Math.Atan2(aimY[i] - y, aimX[i] - x);
                    var an = a0 + Wrap(a1 - a0) * ki;
                    pivX[i] = x + Math.Cos(an) * Rr;
                    pivY[i] = y + Math.Sin(an) * Rr + 3 * s * (1 - Clamp(ki, 0, 1));
                    var exact = Aim(pivX[i], pivY[i], aimX[i], aimY[i]);
                    var rad = Math.Atan2(-Math.Cos(an), Math.Sin(an));
                    var r1 = rad + Wrap(exact - rad) * Clamp(ki, 0, 1);
                    rot[i] = Lerp(f0, r1, Clamp(ki * 1.6, 0, 1)) + .02 * Math.Sin(t * 2.2);
                    continue;
                }

                var bx = x + Math.Cos(att[i] * Math.PI / 180) * Rr;
                var by = y + Math.Sin(att[i] * Math.PI / 180) * Rr + 3 * s;
                rot[i] = Lerp(f0, Clamp(Aim(bx, by, aimX[i], aimY[i]), -1.45, 1.45), ki) + .025 * Math.Sin(t * 2.2 + i);
            }
        }

        static double Aim(double px, double py, double tx, double ty) => Math.Atan2(-(tx - px), ty - py);

        static double Wrap(double d)
        {
            while (d > Math.PI) d -= Tau;
            while (d < -Math.PI) d += Tau;
            return d;
        }

        // ------------------------------------------------------------------ the torch flame (anime_menu_v6.js:289-297)

        /// <summary>The 4 tongues back to front: size factor k, colour (hex) and phase.</summary>
        public static readonly (double k, int rgb, double ph)[] Tongues =
        {
            (1, 0xC8461E, 0), (.74, 0xF08A2A, 1.3), (.5, 0xFFC94A, 2.1), (.26, 0xFFF3C4, 3.0)
        };

        /// <summary>One tongue's outline (the page's path with its 3 quadratic segments flattened into
        /// <paramref name="qSteps"/> pieces each), appended to <paramref name="xs"/> / <paramref name="ys"/> (stage px).
        /// Returns the point count. The page's own commands are <see cref="TonguePathCommands"/>.</summary>
        public static int Tongue(double x, double y, double t, double w, double h, double ph, int qSteps, double[] xs,
            double[] ys)
        {
            var n = 0;
            var cx = x - w;
            var cy = y;
            Add(ref n, xs, ys, cx, cy);
            Quad(ref n, xs, ys, ref cx, ref cy, x - w * 1.05, y - h * .35, x - w * .55, y - h * .55, qSteps);
            var sw = Math.Sin(t * 7.1 + ph) * .18 + Math.Sin(t * 11.3 + ph * 2) * .08;
            const int segs = 3;
            for (var i = 0; i < segs; i++)
            {
                var u = (i + .5) / segs;
                var tx = x + (u - .5) * w * 1.3 + sw * w * (1.6 - u);
                var th = h * (i == 1 ? 1 : .72 + .12 * Math.Sin(t * 9 + i * 2 + ph));
                cx = tx;
                cy = y - th;
                Add(ref n, xs, ys, cx, cy);
                cx = x + (u - .5 + .5 / segs) * w * 1.25 + sw * w;
                cy = y - h * (.5 + .08 * Math.Sin(t * 6 + i));
                Add(ref n, xs, ys, cx, cy);
            }

            Quad(ref n, xs, ys, ref cx, ref cy, x + w * 1.05, y - h * .35, x + w, y, qSteps);
            Quad(ref n, xs, ys, ref cx, ref cy, x, y + w * .55, x - w, y, qSteps);
            // the path closes on its own start point: drop the duplicate
            if (n > 1 && Math.Abs(xs[n - 1] - xs[0]) < 1e-9 && Math.Abs(ys[n - 1] - ys[0]) < 1e-9) n--;
            return n;
        }

        /// <summary>The tongue path as the page's own canvas calls (moveTo / quadraticCurveTo / lineTo), for the harness:
        /// rigs_land.json flame.testVectors records them at 12 times.</summary>
        public static System.Collections.Generic.List<(string op, double[] a)> TonguePathCommands(double x, double y,
            double t, double w, double h, double ph)
        {
            var list = new System.Collections.Generic.List<(string, double[])>();
            list.Add(("moveTo", new[] { x - w, y }));
            list.Add(("quadraticCurveTo", new[] { x - w * 1.05, y - h * .35, x - w * .55, y - h * .55 }));
            var sw = Math.Sin(t * 7.1 + ph) * .18 + Math.Sin(t * 11.3 + ph * 2) * .08;
            for (var i = 0; i < 3; i++)
            {
                var u = (i + .5) / 3;
                list.Add(("lineTo", new[]
                {
                    x + (u - .5) * w * 1.3 + sw * w * (1.6 - u),
                    y - h * (i == 1 ? 1 : .72 + .12 * Math.Sin(t * 9 + i * 2 + ph))
                }));
                list.Add(("lineTo", new[]
                {
                    x + (u - .5 + .5 / 3) * w * 1.25 + sw * w, y - h * (.5 + .08 * Math.Sin(t * 6 + i))
                }));
            }

            list.Add(("quadraticCurveTo", new[] { x + w * 1.05, y - h * .35, x + w, y }));
            list.Add(("quadraticCurveTo", new[] { x, y + w * .55, x - w, y }));
            list.Add(("closePath", Array.Empty<double>()));
            return list;
        }

        static void Add(ref int n, double[] xs, double[] ys, double x, double y)
        {
            if (n >= xs.Length) return;
            xs[n] = x;
            ys[n] = y;
            n++;
        }

        static void Quad(ref int n, double[] xs, double[] ys, ref double cx, ref double cy, double qx, double qy,
            double ex, double ey, int steps)
        {
            for (var i = 1; i <= steps; i++)
            {
                var u = (double)i / steps;
                var a = (1 - u) * (1 - u);
                var b = 2 * (1 - u) * u;
                var c = u * u;
                Add(ref n, xs, ys, a * cx + b * qx + c * ex, a * cy + b * qy + c * ey);
            }

            cx = ex;
            cy = ey;
        }

        // ------------------------------------------------------------------ the torch motes (menu.js:305 table, :669 draw)

        /// <summary>One mote at t: position and alpha (a = win(age, 0, .8, life − 1.2, life) .55 clamp(1.3 − dist / 420k)).</summary>
        public static void Mote(double t, double x0, double y0, double v, double sw, double life, double off,
            double torchX, double torchY, double k, out double x, out double y, out double a)
        {
            var age = Mod(t + off, life);
            y = y0 - v * age;
            x = x0 + Math.Sin(t * .6 + sw) * 5;
            var d = Math.Sqrt((x - torchX) * (x - torchX) + (y - torchY) * (y - torchY));
            a = Win(age, 0, .8, life - 1.2, life) * .55 * Clamp(1.3 - d / (420 * k), 0, 1);
        }

        // ------------------------------------------------------------------ the breach glow (menu_v41.js:57-59)

        /// <summary>fl = .62 + .25 sin(5.1 t) sin(1.7 t + 2) + .13 sin(13.3 t).</summary>
        public static double BreachFl(double t) => .62 + .25 * Math.Sin(t * 5.1) * Math.Sin(t * 1.7 + 2) + .13 * Math.Sin(t * 13.3);

        /// <summary>The crack glow's alpha clamp(fl (1 + .7 sg), 0, 1.6) .6 (sg = 0 at the idle).</summary>
        public static double CrackGlowAlpha(double fl, double sg = 0) => Clamp(fl * (1 + .7 * sg), 0, 1.6) * .6;

        /// <summary>The fissure glow's alpha clamp((.25 + .12 fl)(1 + .6 sg), 0, 1).</summary>
        public static double FissureGlowAlpha(double fl, double sg = 0) => Clamp((.25 + .12 * fl) * (1 + .6 * sg), 0, 1);

        // ------------------------------------------------------------------ the room at rest (menu.js:659-660, :675-676)

        /// <summary>The emissive breath bre = .55 + .25 sin(.9 t) + .1 sin(2.3 t) (the blur layer's alpha is .55 bre).</summary>
        public static double EmissiveBreath(double t) => .55 + .25 * Math.Sin(t * .9) + .1 * Math.Sin(t * 2.3);

        /// <summary>The vignette's strength pr = .55 + .12 sin(.37 t) + .05 sin(1.3 t) + .3 sg.</summary>
        public static double VignettePr(double t, double sg = 0) => .55 + .12 * Math.Sin(t * .37) + .05 * Math.Sin(t * 1.3) + .3 * sg;

        // ------------------------------------------------------------------ the deck box (menu.js:608-623, anime_box_v6.js)

        /// <summary>The box at the idle (no demo, no SAVE): the lid, the card rise, the light, the Wdjat breath and the
        /// plaque's inscription swap, all from the hover value hv.</summary>
        public static void BoxState(double t, double hv, out double lid, out double rise, out double light, out double eye,
            out double ins)
        {
            lid = .25 * Back(hv);
            rise = .25 * hv;
            light = .45 * hv;
            eye = .62 + .1 * Math.Sin(t * 1.3);
            ins = SStep(.2, .8, hv);
        }

        /// <summary>The lid's motion: kk = clamp(lid / 1.9, 0, 1.1); k1 = sstep(0, .22, kk); k2 = eIO(seg(kk, .16, 1)).</summary>
        public static void BoxLid(double lid, out double k1, out double k2)
        {
            var kk = Clamp(lid / 1.9, 0, 1.1);
            k1 = SStep(0, .22, kk);
            k2 = InOut(MenuV6Math.Seg(kk, .16, 1));
        }

        /// <summary>relics.js:431 boxProj: the box frame's (x, y, z) to the sprite plane (yaw 30°, pitch 21°).</summary>
        public static void BoxProj(double x, double y, double z, out double px, out double py)
        {
            const double yaw = 30 * Math.PI / 180, pitch = 21 * Math.PI / 180;
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            px = x * cy - z * sy;
            py = -((-sp * sy) * x + cp * y + (-sp * cy) * z);
        }

        /// <summary>The Wdjat on the lid: alpha clamp((eye − .6) / .9, 0, 1).</summary>
        public static double BoxEyeAlpha(double eye) => Clamp((eye - .6) / .9, 0, 1);

        // ------------------------------------------------------------------ the WORLD MAP niche (menu.js:186-219)

        /// <summary>A niche star's alpha .3 + .5 max(0, sin(t (.6 + hash2(i, 3, 3)) + 2 i))^6.</summary>
        public static double NicheStar(double t, int i) =>
            .3 + .5 * Math.Pow(Math.Max(0, Math.Sin(t * (.6 + MenuV6Math.Hash2(i, 3, 3)) + i * 2)), 6);

        /// <summary>A searchlight's angle −pi/2 + .5 sin(sp t + ph).</summary>
        public static double Searchlight(double t, double sp, double ph) => -Math.PI / 2 + Math.Sin(t * sp + ph) * .5;

        /// <summary>A Tear's pulse tp = .6 + .4 sin(1.3 t + 2 i).</summary>
        public static double TearPulse(double t, int i) => .6 + .4 * Math.Sin(t * 1.3 + i * 2);

        /// <summary>The sliver flipbook's phase p = (t + 2.3) mod 9.7: a frame floor(60 p) while it shows
        /// (ks = sin(pi p / .55) &gt; .01, p &lt; .55), else −1.</summary>
        public static int SliverFrame(double t, int frames)
        {
            var p = Mod(t + 2.3, 9.7);
            if (p >= .55 || Math.Sin(p / .55 * Math.PI) <= .01) return -1;
            return Math.Min(frames - 1, (int)Math.Floor(p * 60));
        }

        /// <summary>The blimp: bx = ((3.2 t) mod (nw + 30)) − 15, by = .2 nh + 1.2 sin(.4 t); its nose light is on while
        /// (1.1 t) mod 1 &lt; .5.</summary>
        public static void Blimp(double t, double nw, double nh, out double bx, out double by, out bool light)
        {
            bx = Mod(t * 3.2, nw + 30) - 15;
            by = nh * .2 + Math.Sin(t * .4) * 1.2;
            light = Mod(t * 1.1, 1) < .5;
        }

        /// <summary>Window i is lit while hash2(i, floor(.35 t + 9 ph), 7) &gt; .12.</summary>
        public static bool WindowOn(double t, int i, double ph) => MenuV6Math.Hash2(i, (int)Math.Floor(t * .35 + ph * 9), 7) > .12;

        /// <summary>A ping j (0 or 1): phase k ∈ [0, 1) while ((t − 1.3 j) mod 2.6) &lt; 1.3, else −1.</summary>
        public static double Ping(double t, int j)
        {
            var ph = Mod(t - j * 1.3, 2.6);
            return ph > 1.3 ? -1 : ph / 1.3;
        }

        /// <summary>The niche Ring's idle "from" rotation of pointer i: .05 sin(t (1 + .2 i) + 1.7 i).</summary>
        public static double NicheFrom(double t, int i) => .05 * Math.Sin(t * (1 + .2 * i) + i * 1.7);

        /// <summary>A bearing ray's travelling glint phase (.9 t + .23 i) mod 1.</summary>
        public static double RayGlint(double t, int i) => Mod(t * .9 + i * .23, 1);

        // ------------------------------------------------------------------ CSS transitions (the .rl label, QUIT)

        /// <summary>CSS cubic-bezier(x1, y1, x2, y2) at progress p (Newton on x, then y).</summary>
        public static double Bezier(double x1, double y1, double x2, double y2, double p)
        {
            p = Clamp(p, 0, 1);
            if (p <= 0 || p >= 1) return p;
            var u = p;
            for (var i = 0; i < 8; i++)
            {
                var x = BezierAt(x1, x2, u) - p;
                var dx = BezierDeriv(x1, x2, u);
                if (Math.Abs(x) < 1e-6) break;
                if (Math.Abs(dx) < 1e-6) break;
                u -= x / dx;
                u = Clamp(u, 0, 1);
            }

            return BezierAt(y1, y2, u);
        }

        static double BezierAt(double a, double b, double u) => 3 * a * u * (1 - u) * (1 - u) + 3 * b * u * u * (1 - u) + u * u * u;

        static double BezierDeriv(double a, double b, double u) =>
            3 * a * (1 - u) * (1 - u) + 6 * (b - a) * u * (1 - u) + 3 * (1 - b) * u * u;

        /// <summary>CSS "ease" (cubic-bezier(.25, .1, .25, 1)).</summary>
        public static double Ease(double p) => Bezier(.25, .1, .25, 1, p);

        /// <summary>wrldz.css --ease-out (cubic-bezier(.16, 1, .3, 1)).</summary>
        public static double EaseOut(double p) => Bezier(.16, 1, .3, 1, p);
    }
}
