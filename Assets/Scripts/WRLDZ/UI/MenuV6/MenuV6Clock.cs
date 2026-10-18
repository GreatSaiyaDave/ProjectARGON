using System;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The menu's clock (BUILD_PLAN §4): unscaled seconds since the stage was built, the same t the mockup pages
    /// run on (SPEC §8: every animated value is a pure function of the clock). <see cref="Freeze"/> is the pages'
    /// <c>#freeze&amp;t=</c>, used by the captures (MenuV6Capture) to render one exact moment.
    /// </summary>
    public static class MenuV6Clock
    {
        static float _origin = float.NaN;
        static bool _frozen;
        static float _frozenAt;

        /// <summary>Seconds of menu time: frozen value, else unscaled time since <see cref="Restart"/>.</summary>
        public static float Now
        {
            get
            {
                if (_frozen) return _frozenAt;
                if (float.IsNaN(_origin)) _origin = Time.unscaledTime;
                return Mathf.Max(0f, Time.unscaledTime - _origin);
            }
        }

        public static bool Frozen => _frozen;

        /// <summary>t = 0 now (the stage calls this when it is built). A frozen clock stays frozen.</summary>
        public static void Restart() => _origin = Time.unscaledTime;

        /// <summary>Hold the clock at <paramref name="t"/> seconds (captures, tests).</summary>
        public static void Freeze(float t)
        {
            _frozen = true;
            _frozenAt = Mathf.Max(0f, t);
        }

        /// <summary>Let the clock run again, continuing from the frozen time.</summary>
        public static void Unfreeze()
        {
            if (!_frozen) return;
            _frozen = false;
            _origin = Time.unscaledTime - _frozenAt;
        }
    }

    /// <summary>
    /// Ports of the mockup's shared math (<c>V6/assets/core.js:11-20</c>) and the eye's monotone cubic
    /// (<c>anime_v61_eye.js:47-55</c>, copied into <c>Data/schedules.json</c> eye.eye61Mono). System types only, so the
    /// schedules can be checked offline against the page's own numbers (schedules.json tests).
    /// </summary>
    public static class MenuV6Math
    {
        public static double Clamp(double x, double a, double b) => x < a ? a : x > b ? b : x;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>core.js sstep: smoothstep of x between a and b.</summary>
        public static double SStep(double a, double b, double x)
        {
            var t = Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>core.js seg.</summary>
        public static double Seg(double t, double a, double b) => Clamp((t - a) / (b - a), 0, 1);

        /// <summary>core.js eOut (cubic out).</summary>
        public static double Out(double t) => 1 - Math.Pow(1 - Clamp(t, 0, 1), 3);

        /// <summary>core.js eOut5 (quint out).</summary>
        public static double Out5(double t) => 1 - Math.Pow(1 - Clamp(t, 0, 1), 5);

        /// <summary>core.js eIn (cubic in).</summary>
        public static double In(double t)
        {
            t = Clamp(t, 0, 1);
            return t * t * t;
        }

        /// <summary>core.js eIO (cubic in-out).</summary>
        public static double InOut(double t)
        {
            t = Clamp(t, 0, 1);
            return t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        /// <summary>core.js eBack (back out, overshoot 1.4).</summary>
        public static double Back(double t)
        {
            t = Clamp(t, 0, 1);
            const double c1 = 1.4, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        /// <summary>core.js win: in over [a, b], out over [c, d].</summary>
        public static double Win(double t, double a, double b, double c, double d) =>
            SStep(a, b, t) * (1 - SStep(c, d, t));

        /// <summary>core.js rng(seed): mulberry32, uint arithmetic = the page's Math.imul / |0 / &gt;&gt;&gt; bits.</summary>
        public static Func<double> Rng(uint seed)
        {
            var a = seed;
            return () =>
            {
                unchecked
                {
                    a += 0x6D2B79F5u;
                    var t = (a ^ (a >> 15)) * (1u | a);
                    t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            };
        }

        /// <summary>core.js hash2(x, y, s) for integer x, y, s.</summary>
        public static double Hash2(int x, int y, int s)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }

        /// <summary>
        /// eye61Mono: the Fritsch–Carlson monotone cubic through (T, Y), as the page evaluates it
        /// (no kinks, no ringing between keys; an AnimationCurve's tangents would differ).
        /// </summary>
        public static double Mono(double[] T, double[] Y, double x)
        {
            var n = T.Length;
            if (x <= T[0]) return Y[0];
            if (x >= T[n - 1]) return Y[n - 1];
            var i = 0;
            while (x > T[i + 1]) i++;

            double D(int k) => (Y[k + 1] - Y[k]) / (T[k + 1] - T[k]);

            double M(int k)
            {
                if (k == 0) return D(0);
                if (k == n - 1) return D(n - 2);
                var a = D(k - 1);
                var b = D(k);
                return a * b <= 0
                    ? 0
                    : 3 * (T[k + 1] - T[k - 1]) / ((2 * T[k + 1] - T[k] - T[k - 1]) / a + (T[k + 1] - 2 * T[k] + T[k - 1]) / b);
            }

            var h = T[i + 1] - T[i];
            var s = (x - T[i]) / h;
            var s2 = s * s;
            var s3 = s2 * s;
            return (2 * s3 - 3 * s2 + 1) * Y[i] + (s3 - 2 * s2 + s) * h * M(i) + (-2 * s3 + 3 * s2) * Y[i + 1] +
                   (s3 - s2) * h * M(i + 1);
        }

        /// <summary>
        /// The torch flicker (menu.js:653 via schedules.json torch.flick):
        /// clamp(.5 + .5 sin(7.3t + 2 sin 2.1t) · sin(3.7t + 1.3) · .9 + (NZF_row7[(t·40|0) &amp; 511] − .5) · .5, 0, 1) · (1 − .75 sg).
        /// <paramref name="noiseRow"/> is NZF_row7 (512 values); null or short = no noise term.
        /// </summary>
        public static double Flick(double t, double[] noiseRow, double sg = 0)
        {
            var wave = .5 + .5 * Math.Sin(7.3 * t + 2 * Math.Sin(2.1 * t)) * Math.Sin(3.7 * t + 1.3) * .9;
            var noise = 0.0;
            if (noiseRow != null && noiseRow.Length >= 512)
            {
                var k = (int)Math.Floor(t * 40);   // JS (t * 40) | 0 for t >= 0
                noise = (noiseRow[k & 511] - .5) * .5;
            }

            return Clamp(wave + noise, 0, 1) * (1 - .75 * sg);
        }

        /// <summary>Torch pool weight of basis b0 (menu.js:658 stop 0; schedules.json torch.drives).</summary>
        public static double TorchPoolA0(double flick, double sg = 0) => .2 * (.8 + .3 * flick) * (1 - .6 * sg);
    }
}
