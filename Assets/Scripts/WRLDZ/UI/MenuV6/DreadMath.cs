using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// One frame of the dread director (BUILD_PLAN §6 P5 step 5; dread_&lt;layout&gt;.json director): the page's
    /// <c>Menu.prototype.dreadState</c> chain menu_v4.js:55-74 → menu_v41.js:35-41 (drop) → menu_v5.js:11-16 (no bind)
    /// → anime_v61_eye.js:210-211 (the lid window). A pure function of the menu clock t.
    /// </summary>
    public struct DreadState
    {
        public int Tier;
        public double OmenU, WhisperU, ApparU;
        public double Omen, Whisper, Appar, Still, Lid, Surf, Push, Relief, Sg, Drop;

        /// <summary>The Stone at rest (no beat): every rung reads 0.</summary>
        public static DreadState Rest => new() { Tier = 2, OmenU = 99, WhisperU = 99, ApparU = 99 };

        /// <summary>True while any rung moves (the director's windows are open).</summary>
        public bool Any => Sg > .0005 || Drop > .0005 || Whisper > .0005 || Lid > .0005 || Push > .0005 ||
                           (OmenU > -1.5 && OmenU < 3.4) || (WhisperU > -.05 && WhisperU < 2.2) || (ApparU > -1 && ApparU < 8);
    }

    /// <summary>
    /// The dread ladder's math (System types only, so the offline check can replay the page's own test vectors). The idle
    /// (the product, landscape.html#demo=0) runs the omen at 8 + 70 n + 20 hash2(n, 1, 3) and the whisper at 34 + 70 n
    /// (menu_v4.js:59-60): SPEC §8.3's "every 11 s from 5.2 s" is v2 / v3 text (decision, PROGRESS_p5exports). The story
    /// beat (#dread, d0 2.8 s, tiers 1-3, the sky-lid apparition) runs only when <see cref="State"/> gets a d0.
    /// </summary>
    public static class DreadMath
    {
        public const double StoryD0 = 2.8;

        static double W(double u, double a, double b, double c, double d) => u < -1 || u > 50 ? 0 : MenuV6Math.Win(u, a, b, c, d);

        /// <summary>The director at menu time <paramref name="t"/>; <paramref name="storyD0"/> NaN = the idle.</summary>
        public static DreadState State(double t, double storyD0 = double.NaN)
        {
            var S = DreadState.Rest;
            var story = !double.IsNaN(storyD0);
            if (story)
            {
                var u = t - storyD0;
                S.Tier = u < 5.0 ? 1 : u < 9.0 ? 2 : 3;
                S.WhisperU = u - 2.6;
                S.OmenU = u - 5.0;
                S.ApparU = u - 9.0;
            }
            else if (t > 8)
            {
                var n = (int)Math.Floor((t - 8) / 70);
                S.OmenU = t - 8 - n * 70 - 20 * MenuV6Math.Hash2(n, 1, 3);
                S.WhisperU = ((t - 34) % 70 + 70) % 70;
            }

            S.Omen = W(S.OmenU, 0, .45, 1.3, 2.6);
            S.Whisper = W(S.WhisperU, 0, .2, .9, 1.6);
            S.Appar = W(S.ApparU, 0, .6, 4.7, 5.9);
            S.Still = W(S.ApparU, 0, .3, .6, .9);
            S.Lid = W(S.ApparU, .6, 1.3, 2.1, 2.6);
            S.Surf = W(S.ApparU, 2.4, 2.9, 4.4, 4.9);
            S.Push = Math.Max(W(S.OmenU, .75, .95, 1.35, 2.2), W(S.ApparU, 4.3, 4.45, 4.9, 5.8));
            S.Relief = story ? MenuV6Math.Seg(t - storyD0, 14.2, 15.4) : 0;
            S.Sg = Math.Max(S.Omen, S.Appar * .9);
            S.Drop = Math.Max(.16 * S.Whisper, Math.Max(.34 * S.Omen, .82 * S.Appar));   // v41 (v5: bind = 0)
            if (S.ApparU > -1 && S.ApparU < 8) S.Lid = MenuV6Math.Win(S.ApparU, .6, 1.3, 2.6, 3.1);   // v61_eye
            return S;
        }

        /// <summary>The idle omen of index n starts at 8 + 70 n + 20 hash2(n, 1, 3) (the first at 18.468 s).</summary>
        public static double OmenStart(int n) => 8 + 70 * n + 20 * MenuV6Math.Hash2(n, 1, 3);

        /// <summary>The idle whisper of index n starts at 34 + 70 n.</summary>
        public static double WhisperStart(int n) => 34 + 70 * n;

        /// <summary>The omen strike envelope blEnv (anime_v61_cracks.js:613).</summary>
        public static double BlEnv(double u) =>
            u < 0 || u > .66 ? 0 : u < .1 ? MenuV6Math.InOut(u / .1) : u < .24 ? 1 : 1 - MenuV6Math.InOut((u - .24) / .42);

        /// <summary>The strikes' strength eIO(clamp(sg / .85)) (drawOmenLightning).</summary>
        public static double BoltK(double sg) => MenuV6Math.InOut(MenuV6Math.Clamp(sg / .85, 0, 1));

        /// <summary>Nut's fracture run (anime_v61_cracks.js:595): the omen and the apparition run it to its end.</summary>
        public static double NutRun(in DreadState S) =>
            Math.Max(MenuV6Math.InOut(MenuV6Math.Seg(S.OmenU, .05, 1.0)) * (1 - MenuV6Math.InOut(MenuV6Math.Seg(S.OmenU, 2.4, 3.4))),
                MenuV6Math.InOut(MenuV6Math.Seg(S.ApparU, .6, 1.35)) * (1 - MenuV6Math.InOut(MenuV6Math.Seg(S.ApparU, 5.0, 6.2))));

        /// <summary>Nut's fracture widening + .4 max(omen, .6 appar) (added to the breathe).</summary>
        public static double NutOpen(in DreadState S) => .4 * Math.Max(S.Omen, S.Appar * .6);

        /// <summary>The breach's breathe 1 + .006 sin(t 2π / 8.3) + .005 sin(t 2π / 6.1 + 1) + .02 omen; Unity keeps the
        /// rest plates still (P1), so only the omen's term scales them (decision, PROGRESS_p5code).</summary>
        public static double BreachGrow(in DreadState S) => 1 + .02 * S.Omen;

        /// <summary>The eyes' lit 1 − .45 sg (menu_v41.js:45) as a vertex tint: the page darkens by #050207 at (1 − lit) .85
        /// source-atop (≤ 6 levels off, dread_&lt;layout&gt;.json rungs.eyes).</summary>
        public static float EyeTint(double lit) => (float)(1 - .85 * (1 - MenuV6Math.Clamp(lit, 0, 1)));

        /// <summary>The keystone's eye gutters on the omen: × (1 − .95 win(omenU, .2, .32, .5, .7)) (menu.js:836).</summary>
        public static double KeystoneGutter(in DreadState S) => 1 - .95 * MenuV6Math.Win(S.OmenU, .2, .32, .5, .7);

        /// <summary>The omen's mouth k = win(omenU, .3, .7, 1.3, 1.9) (menu_v41.js:69).</summary>
        public static double MouthK(in DreadState S) => MenuV6Math.Clamp(MenuV6Math.Win(S.OmenU, .3, .7, 1.3, 1.9), 0, 1);

        /// <summary>The whisper's ink k = seg(whisperU, −.05, 2.15) while −.05 &lt; whisperU &lt; 2.2 (else NaN = not drawn).</summary>
        public static double InkK(in DreadState S) =>
            S.WhisperU > -.05 && S.WhisperU < 2.2 ? MenuV6Math.Seg(S.WhisperU, -.05, 2.15) : double.NaN;

        /// <summary>The fissure lens' opening max(omen, .6 surf)(1 − push) (menu_v41.js:61).</summary>
        public static double FissureOp(in DreadState S) => Math.Max(S.Omen, S.Surf * .6) * (1 - S.Push);

        /// <summary>The sky-lid's openness eOut(clamp(lid)) and its 12-frame step (0 = closed).</summary>
        public static int SkyLidStep(in DreadState S, out double op)
        {
            op = MenuV6Math.Out(MenuV6Math.Clamp(S.Lid, 0, 1));
            if (op <= .01) return 0;
            return (int)MenuV6Math.Clamp(Math.Round(12 * op), 1, 12);
        }
    }

    /// <summary>
    /// His eye's ep.1 timing (anime_v61_eye.js:41-61, 113-119, 175-201): the reference open / close, the shut and wide
    /// states, blinkAt, and the menu's eye plan (reveals, the fissure lens, the Puzzle's squeeze, the stillness, the quiet
    /// rule). A state is (p, sy, sx): the frame of a book nearest to it is drawn (D20).
    /// </summary>
    public static class EyeMath
    {
        public struct St
        {
            public double P, Sy, Sx;

            public St(double p, double sy, double sx)
            {
                P = p;
                Sy = sy;
                Sx = sx;
            }
        }

        static readonly double[] KT = { 0, .05, .10, .17, .21, .25, .29, .33, .37, .41, .45, .49, .54 };
        static readonly double[] KP = { 0, .47, .63, .78, .88, 1.02, 1.17, 1.165, 1.14, 1.10, 1.06, 1.02, 1 };
        static readonly double[] KSy = { .12, .40, .50, .63, .76, .94, 1.16, 1.15, 1.12, 1.075, 1.035, 1.01, 1 };
        static readonly double[] KSx = { 1.08, 1.06, 1.04, 1.0, .985, .965, .93, .935, .95, .97, .99, .998, 1 };
        public const double OpenDur = .54, CloseDur = .13;
        public static readonly St Shut = new(0, .12, 1.08), Wide = new(1, 1, 1);

        /// <summary>eye61Mono: the monotone cubic (Fritsch-Carlson) through the keys.</summary>
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
                double a = D(k - 1), b = D(k);
                return a * b <= 0 ? 0 : 3 * (T[k + 1] - T[k - 1]) / ((2 * T[k + 1] - T[k] - T[k - 1]) / a + (T[k + 1] - 2 * T[k] + T[k - 1]) / b);
            }

            var h = T[i + 1] - T[i];
            var s = (x - T[i]) / h;
            double s2 = s * s, s3 = s2 * s;
            return (2 * s3 - 3 * s2 + 1) * Y[i] + (s3 - 2 * s2 + s) * h * M(i) + (-2 * s3 + 3 * s2) * Y[i + 1] + (s3 - s2) * h * M(i + 1);
        }

        public static St Open(double dt) => dt <= 0 ? new St(0, KSy[0], KSx[0]) : new St(Mono(KT, KP, dt), Mono(KT, KSy, dt), Mono(KT, KSx, dt));

        public static St Close(double dt)
        {
            var q = Math.Pow(MenuV6Math.Clamp(dt / CloseDur, 0, 1), 2);
            return new St(1 - q, MenuV6Math.Lerp(1, .12, q), MenuV6Math.Lerp(1, 1.08, q));
        }

        /// <summary>Close at tc, hold, open at to (the reference open): the state at t.</summary>
        public static St BlinkAt(double t, double tc, double to) => t < tc ? Wide : t < to ? Close(t - tc) : Open(t - to);

        /// <summary>An idle blink of the P1 schedule (close at Tc, shut for Hold, the reference open) at t; null-free:
        /// <paramref name="running"/> false = no blink at t.</summary>
        public static St Idle(EyeBlinkSchedule sch, double t, out bool running)
        {
            running = false;
            if (sch == null) return Wide;
            var list = sch.PlayedUntil(t);
            for (var i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.Tc > t) break;
                if (t >= b.End) continue;
                running = true;
                return t < b.Tc + CloseDur ? Close(t - b.Tc) : t < b.To ? Shut : Open(t - b.To);
            }

            return Wide;
        }

        static readonly Dictionary<int, double> Off = new() { { 11, 0 }, { 12, .22 }, { 13, .37 } };

        /// <summary>
        /// The menu's eye plan (anime_v61_eye.js:176-201) for a breach eye of <paramref name="seed"/> (11 / 12 / 13) or the
        /// fissure lens (<paramref name="fissure"/>) at PAGE time <paramref name="t"/>: the reveal windows of the story ([0,
        /// 3.45]; the idle plays none — the page's menu reveals at 0 / 16.82 are its recorded cuts' first appearances, and the
        /// product's cold-open reveal is P2's own EyeBlinkSchedule.RevealTo, outside the dread window), the fissure lens' open
        /// on the omen / the apparition's surfacing, the Puzzle's squeeze (close, hold, the ep.1 open), the apparition's
        /// stillness, then the idle blinks under the quiet rule (<paramref name="idle"/> carries its reveal windows).
        /// </summary>
        public static St Plan(int seed, double t, in DreadState DS, bool fissure, bool story, EyeBlinkSchedule idle)
        {
            var off = Off.TryGetValue(seed, out var o) ? o : 0;
            var reveal = story ? StoryReveal : NoReveal;
            for (var i = 0; i < reveal.Length; i += 2)
            {
                double tc = reveal[i], to = reveal[i + 1] + off;
                if (t >= tc - .01 && t < to + OpenDur)
                    return tc <= 0 ? (t < to ? Shut : Open(t - to)) : BlinkAt(t, tc + off * .3, to);
            }

            if (fissure)
            {
                if (DS.OmenU > -.1 && DS.OmenU < 2.4) return DS.OmenU < .4 ? Shut : Open(DS.OmenU - .4);
                if (DS.ApparU > 2.3 && DS.ApparU < 5) return DS.ApparU < 2.8 ? Shut : Open(DS.ApparU - 2.8);
            }

            if (DS.OmenU > .75 - .01 && DS.OmenU < 1.35 + OpenDur) return BlinkAt(DS.OmenU, .75, 1.35);
            if (DS.ApparU > 4.3 - .01 && DS.ApparU < 4.9 + OpenDur) return BlinkAt(DS.ApparU, 4.3, 4.9);
            if (DS.ApparU > -.2 && DS.ApparU < 5.8) return Wide;
            if (DS.OmenU > -1.5 && DS.OmenU < 3) return Wide;   // the quiet rule's omen term (the idle blink returns null)
            var s = Idle(idle, t, out var running);
            return running ? s : Wide;
        }

        static readonly double[] NoReveal = new double[0];
        static readonly double[] StoryReveal = { 0, 3.45 };

        /// <summary>The page's menu reveal windows (anime_v61_eye.js:177, schedules.json eye.reveal.menu): the idle blinks'
        /// quiet rule keeps off them (the P1 flipbooks' schedules use the same).</summary>
        public static readonly double[][] MenuRevealWindows = { new[] { 0, 1.15 }, new[] { 16.82, 17.65 } };

        /// <summary>The story's ([#dread] page clock).</summary>
        public static readonly double[][] StoryRevealWindows = { new[] { 0, 3.45 } };

        public static double OffOf(int seed) => Off.TryGetValue(seed, out var o) ? o : 0;

        /// <summary>The window in which the dread eyes replace the P1 idle flipbooks (the quiet rule's omen term, the squeeze,
        /// the fissure lens and the grow): outside it the P1 flipbook is the page's eye exactly.</summary>
        public static bool DreadWindow(in DreadState DS, bool story) => story || (DS.OmenU > -1.5 && DS.OmenU < 3.0);
    }

    /// <summary>One of his eyes as the page draws it this frame: eye61Draw(x, y, px, state) (after the breach's .8 wrapper),
    /// its lit and alpha.</summary>
    public struct EyeDraw
    {
        public int Seed;
        public double X, Y, Px, Lit, A;
        public EyeMath.St S;
    }

    /// <summary>
    /// His eyes under the ladder (menu_v41.js:46-64, anime_v61_eye.js:175-204), pure: the deep eyes 12 / 13 (px x (1 + .3
    /// omen), open opK (1 − .8 surf) ≥ .82, alpha .8), the master eye 11 (grown 1 + .1 omen + .24 surf about the breach's
    /// centre, open opK (1 − .15 still) ≥ .82), each through the .8 wrapper about its ball centre; the fissure lens' eye
    /// (170 k at the lens, open 1). <paramref name="t"/> is the PAGE time (the story's own clock in the story).
    /// </summary>
    public static class DreadEyes
    {
        public static double OpK(double t, in DreadState S, bool story) =>
            S.Tier <= 1 || (story && S.OmenU < .45)
                ? MenuV6Math.Lerp(.82 + .06 * Math.Sin(t * .7), 1, MenuV6Math.InOut(MenuV6Math.Seg(S.OmenU, -.6, .45)))
                : 1;

        static void Wrap(DreadData D, ref double x, ref double y, ref double px)
        {
            x += .2 * D.EyeB.x * px;
            y += .2 * D.EyeB.y * px;
            px *= .8;
        }

        public static void Breach(DreadData D, double t, in DreadState S, bool story, EyeBlinkSchedule i11, EyeBlinkSchedule i12,
            EyeBlinkSchedule i13, List<EyeDraw> into)
        {
            into.Clear();
            if (D == null) return;
            var lit = 1 - .45 * S.Sg;
            var opK = OpK(t, S, story);
            foreach (var (p, px0, seed) in D.EyeD)
            {
                var st = EyeMath.Plan(seed, t, S, false, story, seed == 12 ? i12 : i13);
                st.P *= MenuV6Math.Clamp(Math.Max(opK * (1 - .8 * S.Surf), .82), 0, 1);
                double x = p.x, y = p.y, px = px0 * (1 + .3 * S.Omen);
                Wrap(D, ref x, ref y, ref px);
                into.Add(new EyeDraw { Seed = seed, X = x, Y = y, Px = px, S = st, Lit = lit, A = .8 });
            }

            {
                var grow = 1 + .1 * S.Omen + .24 * S.Surf;
                var c = D.BreachC;
                double x = c.x + (D.EyeM.x - c.x) * grow, y = c.y + (D.EyeM.y - c.y) * grow, px = D.EyeMPx * grow;
                var st = EyeMath.Plan(11, t, S, false, story, i11);
                st.P *= MenuV6Math.Clamp(Math.Max(opK * (1 - .15 * S.Still), .82), 0, 1);
                Wrap(D, ref x, ref y, ref px);
                into.Add(new EyeDraw { Seed = 11, X = x, Y = y, Px = px, S = st, Lit = Math.Max(lit, S.Surf), A = 1 });
            }
        }

        /// <summary>The fissure lens' eye; false while the lens is shut (op ≤ .02).</summary>
        public static bool Fissure(DreadData D, double t, in DreadState S, bool story, out EyeDraw e, out double op)
        {
            e = default;
            op = DreadMath.FissureOp(S);
            if (D == null || op <= .02) return false;
            var px = 170 * D.K;
            e = new EyeDraw
            {
                Seed = 11, X = D.FisEye.x + .03 * px, Y = D.FisEye.y + .041 * px, Px = px, S = EyeMath.Plan(11, t, S, true, story, null),
                Lit = 1, A = 1
            };
            return true;
        }
    }

    /// <summary>One eye flipbook at a dread size (dread_&lt;layout&gt;.json eyes.books): his pixels, unclipped, gaze 0.</summary>
    public sealed class EyeBook
    {
        public string Id, File;
        public double Px, Rot;
        public double CellW, CellH, AnchorX, AnchorY;
        public double SheetW, SheetH;   // grid x pitch: the sheet's size as exported (UVs never use an import's downscale)
        public readonly List<(double x, double y, EyeMath.St s)> Frames = new();

        public int Nearest(in EyeMath.St s, out double err)
        {
            var best = 0;
            err = double.MaxValue;
            for (var i = 0; i < Frames.Count; i++)
            {
                var f = Frames[i].s;
                var e = Math.Max(Math.Abs(f.P - s.P), Math.Max(Math.Abs(f.Sy - s.Sy), Math.Abs(f.Sx - s.Sx)));
                if (e < err)
                {
                    err = e;
                    best = i;
                }
            }

            return best;
        }

        public static EyeBook From(string id, Dictionary<string, object> b)
        {
            if (b == null) return null;
            var e = new EyeBook
            {
                Id = id, File = RJ.S(b, "file"), Px = RJ.N(b, "px", 1), Rot = RJ.N(b, "rot")
            };
            var cell = RJ.L(b, "cell");
            var anc = RJ.L(b, "anchor");
            e.CellW = RJ.At(cell, 0);
            e.CellH = RJ.At(cell, 1);
            e.AnchorX = RJ.At(anc, 0);
            e.AnchorY = RJ.At(anc, 1);
            var grid = RJ.L(b, "grid");
            var pitch = RJ.L(b, "pitch");
            e.SheetW = RJ.At(grid, 0) * RJ.At(pitch, 0);
            e.SheetH = RJ.At(grid, 1) * RJ.At(pitch, 1);
            var frames = RJ.L(b, "frames");
            if (frames != null)
                foreach (var f in frames)
                {
                    var s = RJ.D(f, "S");
                    e.Frames.Add((RJ.N(f, "x"), RJ.N(f, "y"), new EyeMath.St(RJ.N(s, "p", 1), RJ.N(s, "sy", 1), RJ.N(s, "sx", 1))));
                }

            return string.IsNullOrEmpty(e.File) || e.Frames.Count == 0 || e.CellW <= 0 ? null : e;
        }
    }

    /// <summary>A parsed dread_&lt;layout&gt;.json (cached per TextAsset): geometry, books, masks, the grade, the bloom, the
    /// runes, the 1500 omen strikes, the sky-lid steps and its track.</summary>
    public sealed class DreadData
    {
        public double W, H, K = 1;
        public Rect Stone;
        public Vector2 BreachC, EyeM, InkAt, GrinAt, FisEye, BloomC;
        public double EyeMPx = 203;
        public Vector2 EyeB;   // eyeGeo.B (relative to px)
        public readonly List<(Vector2 p, double px, int seed)> EyeD = new();
        public readonly List<Rect> Panels = new();
        public readonly Dictionary<string, EyeBook> Books = new();
        public readonly Dictionary<string, (string file, Rect rect)> Masks = new();
        public readonly Dictionary<string, (string file, Rect rect)> Grade = new();
        public (string file, Rect rect) Bloom;
        public readonly List<(string glow, string core, double size)> Runes = new();
        public double[] StrikeT;
        public readonly List<List<(double[] x, double[] y)>> StrikePaths = new();
        public double StrikeWrap = 539.6;
        public readonly List<SkyStep> Sky = new();
        public Rect SkyRect;
        public readonly Dictionary<string, (string file, int cols, double pw, double ph, List<Vector2> pos)> SkyParts = new();
        public double[] SkyTrackU, SkyTrackX, SkyTrackY;
        public double SkyPx = 127.8;

        public sealed class SkyStep
        {
            public double Op, Lid;
            public Vector2 C;
            public double Wd, Hd;
            public readonly List<Vector2> Polygon = new();
        }

        static readonly Dictionary<string, (TextAsset ta, DreadData d)> Cache = new();

        /// <summary>Data/dread_land.json or dread_port.json; null when the P5 art set is not installed or malformed.</summary>
        public static DreadData Load(MenuLayoutKind kind)
        {
            var name = kind == MenuLayoutKind.Landscape ? "dread_land" : "dread_port";
            try
            {
                var ta = MenuV6Art.Data(name);
                if (ta == null) return null;
                if (Cache.TryGetValue(name, out var hit) && hit.ta == ta) return hit.d;
                var d = From(MenuV6Json.Parse(ta.text) as Dictionary<string, object>);
                Cache[name] = (ta, d);
                return d;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + name + ".json: " + ex.Message);
                return null;
            }
        }

        static Vector2 P(object o, string k)
        {
            var l = RJ.L(o, k);
            return l != null && l.Count >= 2 ? new Vector2((float)RJ.At(l, 0), (float)RJ.At(l, 1)) : Vector2.zero;
        }

        static Rect R4(List<object> l) =>
            l != null && l.Count >= 4 ? new Rect((float)RJ.At(l, 0), (float)RJ.At(l, 1), (float)RJ.At(l, 2), (float)RJ.At(l, 3)) : Rect.zero;

        public static DreadData From(Dictionary<string, object> root)
        {
            if (root == null) return null;
            var d = new DreadData();
            var st = RJ.D(root, "stage");
            d.W = RJ.N(st, "W", 1920);
            d.H = RJ.N(st, "H", 1080);
            d.K = RJ.N(root, "k", 1);
            var g = RJ.D(root, "geo");
            var stone = RJ.D(g, "stone");
            d.Stone = new Rect((float)RJ.N(stone, "bx"), (float)RJ.N(stone, "by"), (float)RJ.N(stone, "bw"), (float)RJ.N(stone, "bh"));
            d.BreachC = P(g, "breachC");
            var em = RJ.D(g, "eyeM");
            d.EyeM = new Vector2((float)RJ.N(em, "x"), (float)RJ.N(em, "y"));
            d.EyeMPx = RJ.N(em, "px", 203);
            var eg = RJ.D(g, "eyeGeo");
            d.EyeB = P(eg, "B");
            var ed = RJ.L(g, "eyeD");
            if (ed != null)
                foreach (var e in ed)
                    d.EyeD.Add((P(e, "p"), RJ.N(e, "px"), (int)RJ.N(e, "seed")));
            d.InkAt = P(g, "inkAt");
            d.GrinAt = P(g, "grinAt");
            d.FisEye = new Vector2((float)RJ.N(RJ.D(RJ.D(g, "fis"), "eye"), "x"), (float)RJ.N(RJ.D(RJ.D(g, "fis"), "eye"), "y"));
            d.BloomC = P(g, "bloomC");
            var panels = RJ.L(g, "panels");
            if (panels != null)
                foreach (var p in panels)
                    d.Panels.Add(R4(p as List<object>));

            var books = RJ.D(RJ.D(root, "eyes"), "books");
            if (books != null)
                foreach (var kv in books)
                {
                    var b = EyeBook.From(kv.Key, kv.Value as Dictionary<string, object>);
                    if (b != null) d.Books[kv.Key] = b;
                }

            foreach (var (dst, key) in new[] { (d.Masks, "masks"), (d.Grade, "grade") })
            {
                var m = RJ.D(root, key);
                if (m == null) continue;
                foreach (var kv in m)
                {
                    var f = RJ.S(kv.Value, "file");
                    if (string.IsNullOrEmpty(f)) continue;
                    dst[kv.Key] = (f, R4(RJ.L(kv.Value, "rect")));
                }
            }

            var bl = RJ.D(root, "bloom");
            d.Bloom = (RJ.S(bl, "file"), R4(RJ.L(bl, "rect")));
            var runes = RJ.L(RJ.D(root, "runes"), "list");
            if (runes != null)
                foreach (var r in runes)
                    d.Runes.Add((RJ.S(r, "glow"), RJ.S(r, "core"), RJ.N(r, "size", 84)));

            var strikes = RJ.L(RJ.D(root, "bolts"), "strikes");
            if (strikes != null)
            {
                d.StrikeT = new double[strikes.Count];
                for (var i = 0; i < strikes.Count; i++)
                {
                    d.StrikeT[i] = RJ.N(strikes[i], "t");
                    var paths = new List<(double[] x, double[] y)>();
                    var pl = RJ.L(strikes[i], "paths");
                    if (pl != null)
                        foreach (var path in pl)
                        {
                            if (path is not List<object> pts || pts.Count < 2) continue;
                            var xs = new double[pts.Count];
                            var ys = new double[pts.Count];
                            for (var j = 0; j < pts.Count; j++)
                            {
                                var q = pts[j] as List<object>;
                                xs[j] = RJ.At(q, 0);
                                ys[j] = RJ.At(q, 1);
                            }

                            paths.Add((xs, ys));
                        }

                    d.StrikePaths.Add(paths);
                }

                if (strikes.Count > 0) d.StrikeWrap = d.StrikeT[strikes.Count - 1] + .33;
            }

            var sky = RJ.D(root, "skylid");
            if (sky != null)
            {
                d.SkyRect = R4(RJ.L(sky, "rect"));
                d.SkyPx = RJ.N(sky, "px", 127.8);
                var steps = RJ.L(sky, "steps");
                if (steps != null)
                    foreach (var s in steps)
                    {
                        var k = new SkyStep
                        {
                            Op = RJ.N(s, "op"), Lid = RJ.N(s, "lid"), C = P(s, "C"), Wd = RJ.N(s, "Wd"), Hd = RJ.N(s, "Hd")
                        };
                        var poly = RJ.L(s, "polygon");
                        if (poly != null)
                            foreach (var q in poly)
                                k.Polygon.Add(new Vector2((float)RJ.At(q as List<object>, 0), (float)RJ.At(q as List<object>, 1)));
                        d.Sky.Add(k);
                    }

                var parts = RJ.D(sky, "parts");
                if (parts != null)
                    foreach (var kv in parts)
                    {
                        var grid = RJ.L(kv.Value, "grid");
                        var pitch = RJ.L(kv.Value, "pitch");
                        var pos = new List<Vector2>();
                        var pl = RJ.L(kv.Value, "pos");
                        if (pl != null)
                            foreach (var q in pl)
                                pos.Add(new Vector2((float)RJ.At(q as List<object>, 0), (float)RJ.At(q as List<object>, 1)));
                        d.SkyParts[kv.Key] = (RJ.S(kv.Value, "file"), (int)RJ.At(grid, 0, 3), RJ.At(pitch, 0), RJ.At(pitch, 1), pos);
                    }

                var rows = RJ.L(RJ.D(sky, "track"), "rows");
                if (rows != null)
                {
                    d.SkyTrackU = new double[rows.Count];
                    d.SkyTrackX = new double[rows.Count];
                    d.SkyTrackY = new double[rows.Count];
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i] as List<object>;
                        d.SkyTrackU[i] = RJ.At(r, 0);
                        d.SkyTrackX[i] = RJ.At(r, 2);
                        d.SkyTrackY[i] = RJ.At(r, 3);
                    }
                }
            }

            return d;
        }

        /// <summary>The sky-lid eye's position at apparU (the page's track at 30 Hz, interpolated; held at its ends).</summary>
        public Vector2 SkyEyeAt(double apparU)
        {
            var U = SkyTrackU;
            if (U == null || U.Length == 0) return new Vector2(SkyRect.center.x, SkyRect.center.y);
            if (apparU <= U[0]) return new Vector2((float)SkyTrackX[0], (float)SkyTrackY[0]);
            var n = U.Length;
            if (apparU >= U[n - 1]) return new Vector2((float)SkyTrackX[n - 1], (float)SkyTrackY[n - 1]);
            var lo = 0;
            var hi = n - 1;
            while (hi - lo > 1)
            {
                var mid = (lo + hi) >> 1;
                if (U[mid] <= apparU) lo = mid;
                else hi = mid;
            }

            var f = (apparU - U[lo]) / Math.Max(1e-9, U[hi] - U[lo]);
            return new Vector2((float)MenuV6Math.Lerp(SkyTrackX[lo], SkyTrackX[hi], f), (float)MenuV6Math.Lerp(SkyTrackY[lo], SkyTrackY[hi], f));
        }

        /// <summary>The first strike index whose envelope may still be open at crack time tc (u ≤ .66), by bisection.</summary>
        public int FirstStrike(double tc)
        {
            var T = StrikeT;
            if (T == null || T.Length == 0) return 0;
            int lo = 0, hi = T.Length;
            var t0 = tc - .66;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (T[mid] < t0) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }
    }
}
