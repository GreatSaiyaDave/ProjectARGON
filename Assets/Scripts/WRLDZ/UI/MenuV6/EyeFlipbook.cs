using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The idle blink of one breach eye (D20, §11 F7): a flipbook recorded from the page (one ep.1 blink cycle at
    /// 60 fps, gaze 0: <c>MenuV6/Eye/breach_eye_&lt;seed&gt;</c>), played at the page's seeded blink times.
    /// Frame pick = layout_land.json flipbooks / schedules.json eye.flipbooks.pick. Drawn by a RawImage whose uvRect
    /// selects the cell, so no sprite is created per frame and nothing allocates while it runs.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class EyeFlipbook : MonoBehaviour
    {
        RawImage _img;
        Rect[] _uv;
        EyeBlinkSchedule _schedule;
        int _shown = -1;

        public int Seed { get; private set; }
        public int ShownFrame => _shown;

        /// <param name="frameRectsTopLeft">each frame's (x, y, w, h) in sheet px, y measured from the sheet's top.</param>
        public static EyeFlipbook Attach(RawImage img, Texture sheet, IList<Rect> frameRectsTopLeft, int seed,
            EyeBlinkSchedule schedule)
        {
            if (img == null || sheet == null || frameRectsTopLeft == null || frameRectsTopLeft.Count == 0 ||
                schedule == null)
                return null;
            var fb = img.gameObject.AddComponent<EyeFlipbook>();
            fb._img = img;
            fb.Seed = seed;
            fb._schedule = schedule;
            img.texture = sheet;
            float w = sheet.width, h = sheet.height;
            fb._uv = new Rect[frameRectsTopLeft.Count];
            for (var i = 0; i < frameRectsTopLeft.Count; i++)
            {
                var r = frameRectsTopLeft[i];
                fb._uv[i] = new Rect(r.x / w, 1f - (r.y + r.height) / h, r.width / w, r.height / h);
            }

            fb.Show(schedule.FrameAt(MenuV6Clock.Now));
            return fb;
        }

        void Update()
        {
            if (_schedule == null) return;
            Show(_schedule.FrameAt(MenuV6Clock.Now));
        }

        void Show(int frame)
        {
            if (_uv == null || _img == null) return;
            frame = Mathf.Clamp(frame, 0, _uv.Length - 1);
            if (frame == _shown) return;
            _shown = frame;
            _img.uvRect = _uv[frame];
        }
    }

    /// <summary>
    /// The page's idle blink plan for one eye (anime_v61_eye.js:62-66 via schedules.json eye.idle), System types only.
    /// Blinks come from <c>R = rng((seed·17 + 3)·131 + 7); t0 = −R()·4; t0 += 3.5 + R()·4.5; hold = .1 + .1·R()</c>,
    /// generated lazily (the page's list stops after 300; "Unity extends it with the same generator").
    /// The page's quiet rule is applied: no idle blink whose [tc, end) overlaps a reveal window (tc − 1, to + 1.2), so
    /// the idle blinks played here are exactly the page's played idle blinks (schedules.json eye.played). The reveals
    /// themselves belong to the cold open (P2) and are not played in P1a.
    /// </summary>
    public sealed class EyeBlinkSchedule
    {
        public const double Close = 0.13;   // schedules.json eye.CLOSE
        public const double Open = 0.54;    // schedules.json eye.OPEN
        public const int Fps = 60;
        public const int CloseFrames = 8;   // frames 0..7 (k = floor(dt·60), dt < 0.13)
        public const int ShutFrame = 8;
        public const int FrameCount = 41;   // rest + close ×7 + shut + open ×32

        public struct Blink
        {
            public double Tc;     // close starts
            public double Hold;   // shut for
            public double To => Tc + Close + Hold;   // open starts
            public double End => To + Open;
        }

        readonly Func<double> _r;
        double _t0;
        readonly List<Blink> _all = new();     // every generated blink, in order
        readonly List<Blink> _played = new();  // after the quiet rule
        readonly List<(double a, double b)> _quiet = new();
        int _cursor;
        double _lastT = double.NegativeInfinity;

        /// <param name="revealWindows">the page's reveal windows for this eye as (tc, to); P1a passes the menu
        /// reveals of schedules.json eye.reveal so the quiet rule matches the page.</param>
        public EyeBlinkSchedule(int seed, IEnumerable<(double tc, double to)> revealWindows = null)
        {
            _r = MenuV6Math.Rng(unchecked((uint)((seed * 17 + 3) * 131 + 7)));
            _t0 = -_r() * 4;
            if (revealWindows != null)
                foreach (var w in revealWindows)
                    _quiet.Add((w.tc - 1, w.to + 1.2));
        }

        /// <summary>
        /// The reveal windows of the menu page for one eye (schedules.json eye.reveal: menu [[0, 1.15], [16.82, 17.65]],
        /// off = 0 / .22 / .37 for seeds 11 / 12 / 13): the first starts shut (tc = 0), a later one closes at a + off·.3;
        /// each opens at b + off.
        /// </summary>
        public static List<(double tc, double to)> MenuReveals(IList<double[]> menu, double off)
        {
            var list = new List<(double, double)>();
            if (menu == null) return list;
            for (var i = 0; i < menu.Count; i++)
            {
                var w = menu[i];
                if (w == null || w.Length < 2) continue;
                var tc = i == 0 ? 0 : w[0] + off * .3;
                list.Add((tc, w[1] + off));
            }

            return list;
        }

        Blink Next()
        {
            _t0 += 3.5 + _r() * 4.5;
            var b = new Blink { Tc = _t0, Hold = .1 + .1 * _r() };
            _all.Add(b);
            return b;
        }

        bool Quiet(Blink b)
        {
            for (var i = 0; i < _quiet.Count; i++)
                if (b.Tc < _quiet[i].b && b.End > _quiet[i].a)
                    return true;
            return false;
        }

        void EnsureUntil(double t)
        {
            while (_all.Count == 0 || _all[_all.Count - 1].Tc <= t + 10)
            {
                var b = Next();
                if (!Quiet(b)) _played.Add(b);
                if (_all.Count > 200000) break;   // ~ 1.1e6 s of menu time
            }
        }

        /// <summary>The idle blinks the eye plays up to <paramref name="t"/> + 10 s (tests / capture logs).</summary>
        public IReadOnlyList<Blink> PlayedUntil(double t)
        {
            EnsureUntil(t);
            return _played;
        }

        /// <summary>Every generated idle blink (before the quiet rule), for the offline check against schedules.json.</summary>
        public IReadOnlyList<Blink> GeneratedUntil(double t)
        {
            EnsureUntil(t);
            return _all;
        }

        /// <summary>Flipbook frame at menu time t (schedules.json eye.flipbooks.pick).</summary>
        public int FrameAt(double t)
        {
            if (t < 0) t = 0;
            EnsureUntil(t);
            if (t < _lastT) _cursor = 0;   // a capture froze the clock earlier than the last query
            _lastT = t;
            while (_cursor < _played.Count && _played[_cursor].End <= t) _cursor++;
            if (_cursor >= _played.Count) return 0;
            var b = _played[_cursor];
            if (t < b.Tc) return 0;
            if (t < b.Tc + Close) return Math.Min(CloseFrames - 1, (int)Math.Floor((t - b.Tc) * Fps));
            if (t < b.To) return ShutFrame;
            var k = (int)Math.Floor((t - b.To) * Fps);
            return Math.Min(FrameCount - 1, ShutFrame + k);
        }
    }
}
