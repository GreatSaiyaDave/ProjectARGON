using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The dread ladder on the Stone (BUILD_PLAN §6 P5 step 5; dread_&lt;layout&gt;.json): the director state of each frame
    /// (<see cref="DreadMath"/>) and the rungs that are not idle rigs — his eyes in the breach at the dread sizes
    /// (flipbooks, D20; the breach grows .02 omen about its centre), the fissure lens with his eye, the whisper's ink blot
    /// and runes, the omen's smoke mouth, the Stone darkening, the omen's black lightning (1500 eased strikes, seed 931,
    /// over the mist, clipped to the Stone minus the breach), the violet bloom and the pylons, the grade (the crack breaks
    /// spared by a crack copy over it) and the sky-lid apparition (story only, <see cref="SkyLid"/>). The idle rigs read the
    /// same state through <see cref="RigContext.Dread"/> (the torch, the flame, the breach glow, the Ring echo, the keystone,
    /// the vignette, Nut's fracture). No crack reacts (CRACKS §3); the smoke has no dread term.
    /// <para>The idle (the product) runs the page's schedule; the story beat (#dread, d0 2.8 s) runs only when
    /// <see cref="PlayStory"/> is called (captures). A beat that would start while a sheet is over the Stone is skipped
    /// (menu_v4.js: "beats never overlap a sheet"). Battery Saver (SPEC §8.7): no lightning, no sky-lid, no ink / mouth smoke;
    /// the grade, the torch and the Stone darkening keep their timing. Edit mode stays at rest unless probed.</para>
    /// </summary>
    public sealed class DreadLadder : MonoBehaviour
    {
        public DreadData D { get; private set; }
        public MenuLayoutKind Kind { get; private set; }

        /// <summary>The story beat's d0 (NaN = the idle).</summary>
        public double StoryD0 { get; private set; } = double.NaN;

        /// <summary>True while a sheet / popover / overlay is over the Stone (DuelDiskMenuUI).</summary>
        public Func<bool> Suppressed;

        /// <summary>Tests: the time an edit-mode probe evaluates (NaN = at rest).</summary>
        public double ProbeT = double.NaN;

        DreadState _s = DreadState.Rest;
        double _t = double.NaN;
        readonly HashSet<int> _skipOmen = new(), _skipWhisper = new();
        EyeBlinkSchedule _idle11, _idle12, _idle13;

        public bool Story => !double.IsNaN(StoryD0);

        /// <summary>Quads drawn this frame by the ladder's own rungs (the budget count, MenuV6Budget).</summary>
        public int Quads { get; private set; }

        public static DreadLadder Attach(GameObject host, MenuLayoutKind kind)
        {
            if (host == null) return null;
            var d = DreadData.Load(kind);
            if (d == null) return null;
            var l = host.AddComponent<DreadLadder>();
            l.D = d;
            l.Kind = kind;
            l.Schedules(false);
            return l;
        }

        /// <summary>Menu seconds at the story's page t = 0 (NaN = the idle: page time = menu time).</summary>
        double _storyT0 = double.NaN;

        /// <summary>The page clock the eye plan reads: the story's own (#dread starts at 0), else the menu's.</summary>
        double PageT(double t) => Story ? t - _storyT0 : t;

        /// <summary>The idle blinks' schedules with the page's quiet windows (the menu's, as the P1 flipbooks; the story's).</summary>
        void Schedules(bool story)
        {
            var w = story ? EyeMath.StoryRevealWindows : EyeMath.MenuRevealWindows;
            _idle11 = new EyeBlinkSchedule(11, EyeBlinkSchedule.MenuReveals(w, EyeMath.OffOf(11)));
            _idle12 = new EyeBlinkSchedule(12, EyeBlinkSchedule.MenuReveals(w, EyeMath.OffOf(12)));
            _idle13 = new EyeBlinkSchedule(13, EyeBlinkSchedule.MenuReveals(w, EyeMath.OffOf(13)));
        }

        /// <summary>Play the story beat from menu time t0 (captures: the dread demo's tiers, the apparition): the story's page
        /// t = menu t − t0.</summary>
        public void PlayStory(double t0)
        {
            _storyT0 = t0;
            StoryD0 = t0 + DreadMath.StoryD0;
            _t = double.NaN;
            _last = double.NaN;
            Schedules(true);
        }

        public void StopStory()
        {
            StoryD0 = double.NaN;
            _storyT0 = double.NaN;
            _t = double.NaN;
            _last = double.NaN;   // a frozen clock redraws the Stone at rest at once
            Schedules(false);
        }

        /// <summary>The director at menu time t (cached per t).</summary>
        public DreadState At(double t)
        {
            if (!Application.isPlaying && !MenuV6Clock.Frozen)
            {
                if (double.IsNaN(ProbeT)) return DreadState.Rest;
                t = ProbeT;
            }

            if (t == _t) return _s;
            _t = t;
            var S = DreadMath.State(t, StoryD0);
            if (!Story && t > 8)
            {
                var n = (int)Math.Floor((t - 8) / 70);
                var m = (int)Math.Floor((t - 34) / 70);
                var sup = Suppressed != null && SafeSup();
                // a beat whose lead-in begins under a sheet is skipped (one that already runs plays out)
                if (sup && S.OmenU > -1.5 && S.OmenU < 0) _skipOmen.Add(n);
                if (sup && S.WhisperU > 69.0) _skipWhisper.Add(m + 1);
                if (sup && S.WhisperU < 0.0) _skipWhisper.Add(m);
                var skipO = _skipOmen.Contains(n);
                var skipW = _skipWhisper.Contains(m);
                if (skipO || skipW)
                {
                    var R = DreadState.Rest;
                    R.OmenU = skipO ? 99 : S.OmenU;
                    R.WhisperU = skipW ? 99 : S.WhisperU;
                    S = Derive(R);
                }
            }

            _s = S;
            return S;
        }

        bool SafeSup()
        {
            try { return Suppressed(); }
            catch { return false; }
        }

        static DreadState Derive(DreadState S)
        {
            // the windows from the U's (idle: no apparition)
            double W(double u, double a, double b, double c, double d) => u < -1 || u > 50 ? 0 : MenuV6Math.Win(u, a, b, c, d);
            S.Omen = W(S.OmenU, 0, .45, 1.3, 2.6);
            S.Whisper = W(S.WhisperU, 0, .2, .9, 1.6);
            S.Push = W(S.OmenU, .75, .95, 1.35, 2.2);
            S.Sg = S.Omen;
            S.Drop = Math.Max(.16 * S.Whisper, .34 * S.Omen);
            return S;
        }

        public DreadState Now => At(MenuV6Clock.Now);

        // ================================================================== the rungs' graphics

        RigContext _ctx;
        Rect _stage;
        RectTransform _breach;
        Vector2 _breachBase;
        Graphic _p1Eye11, _p1Eye13;
        float _p1Eye11A = 1, _p1Eye13A = 1;
        Image _holeMask;
        QuadLayer _eyeM, _eye12, _eye13;
        RigTex _texM, _texMs, _tex12, _tex12m, _tex13, _tex13m, _texFis;
        RectTransform _fisMask;
        Image _fisFill;
        QuadLayer _fisEye;
        ShapeLayer _fisLine;
        Image _stoneDark, _bloom;
        readonly List<Image> _pylons = new();
        CrackNetGraphic _bolts;
        readonly CrackMesh _boltM = new();
        bool _boltsOn;
        readonly CrackBeat[] _stut = CrackTime.Schedule(912, 21.5, 43, 71, 200);
        readonly List<Image> _gradeOut = new(), _gradeIn = new();
        ShapeLayer _gradeMask;
        int _gradeMaskStep = -1;

        // P5 review (the batch budget): a stencil Mask draws its write and its pop every frame even with nothing under it, so
        // the bolts' clip and the grade's mask are switched off while they hold nothing (their meshes rebuild on enable)
        GameObject _boltClip, _gradeMaskGo;
        CrackLayer _gradeCopy;
        double _gradeCopyK;
        BreachSmoke _smoke;
        SkyLid _sky;

        /// <summary>The breach's layers (P1 under / eyes / over) move into one node that grows about the breach's centre; the
        /// dread eyes draw inside a hole-mask between the P1 eyes and the over plate.</summary>
        public void BuildBreach(RigContext ctx, RectTransform fx, Rect stage, Graphic under, Graphic eye13, Graphic eye11, Graphic over)
        {
            _ctx = ctx;
            _stage = stage;
            if (fx == null || under == null || over == null || D == null) return;
            var go = new GameObject("Breach", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(fx, false);
            rt.SetSiblingIndex(under.transform.GetSiblingIndex());
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2((float)((D.BreachC.x - stage.x) / stage.width), (float)(1 - (D.BreachC.y - stage.y) / stage.height));
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _breach = rt;
            _breachBase = rt.anchoredPosition;
            foreach (var g in new[] { under, eye13, eye11 })
                if (g != null)
                    g.transform.SetParent(rt, false);
            _p1Eye11 = eye11;
            _p1Eye13 = eye13;
            if (eye11 != null) _p1Eye11A = eye11.color.a;
            if (eye13 != null) _p1Eye13A = eye13.color.a;

            // the dread eyes: the breach hole as a stencil (scaled with the node), the deep eyes, then his eye
            if (D.Masks.TryGetValue("breach_hole", out var hm))
            {
                var sp = MenuV6Art.Sprite(hm.file);
                if (sp != null)
                {
                    _holeMask = Img(rt, "DreadEyes", sp, hm.rect, stage);
                    _holeMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                    _texM = Sheet("breach_11");
                    _texMs = Sheet("breach_11s");
                    _tex12 = Sheet("breach_12");
                    _tex12m = Sheet("breach_12m");
                    _tex13 = Sheet("breach_13");
                    _tex13m = Sheet("breach_13m");
                    if (_tex12 != null) _eye12 = ctx.Gfx.Quads(_holeMask.transform, "Eye12", hm.rect, _tex12);
                    if (_tex13 != null) _eye13 = ctx.Gfx.Quads(_holeMask.transform, "Eye13", hm.rect, _tex13);
                    if (_texM != null) _eyeM = ctx.Gfx.Quads(_holeMask.transform, "Eye11", hm.rect, _texM);
                    _holeMask.gameObject.SetActive(false);
                }
            }

            over.transform.SetParent(rt, false);
        }

        RigTex Sheet(string book)
        {
            if (_ctx == null || !D.Books.TryGetValue(book, out var b)) return null;
            return _ctx.Gfx.Sheet(b.File);
        }

        /// <summary>After the fissure plate (menu_v41.js:60-69): the fissure lens with his eye, the whisper's ink blot and
        /// runes, the omen's smoke mouth, then the Stone darkening (drawBlackLightning's fill).</summary>
        public void BuildAfterFissure(RigContext ctx, RectTransform host, Rect stage)
        {
            _ctx = ctx;
            _stage = stage;
            if (host == null || D == null) return;
            if (D.Masks.TryGetValue("fissure_lens_open", out var fm))
            {
                var sp = MenuV6Art.Sprite(fm.file);
                _texFis = Sheet("fissure_11");
                if (sp != null && _texFis != null)
                {
                    _fisFill = Img(host, "FissureLens", sp, fm.rect, stage);
                    _fisFill.color = new Color(3 / 255f, 2 / 255f, 8 / 255f, 1f);
                    _fisFill.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                    _fisMask = _fisFill.rectTransform;
                    // the lens opens about the eye's line (h = 22 k eOut(op)): its pivot at E.y
                    var r = fm.rect;
                    var py = (float)MenuV6Math.Clamp(1 - (D.FisEye.y - r.y) / r.height, 0, 1);
                    _fisMask.pivot = new Vector2(.5f, py);   // the anchors are the rect (Place): the rect stays put
                    _fisMask.offsetMin = _fisMask.offsetMax = Vector2.zero;
                    _fisEye = ctx.Gfx.Quads(_fisMask, "FissureEye", fm.rect, _texFis);
                    _fisLine = ctx.Gfx.Shapes(host, "FissureLensLine", stage);
                    _fisFill.gameObject.SetActive(false);
                }
            }

            _smoke = BreachSmoke.Build(host, stage, Kind, D, ctx);

            if (D.Masks.TryGetValue("stone_minus_breach", out var sm))
            {
                var sp = MenuV6Art.Sprite(sm.file);
                if (sp != null)
                {
                    _stoneDark = Img(host, "StoneDark", sp, sm.rect, stage);
                    _stoneDark.color = new Color(10 / 255f, 4 / 255f, 20 / 255f, 0f);
                    _stoneDark.enabled = false;
                }
            }
        }

        /// <summary>The omen's black lightning, right after the smoke (anime_v61_cracks.js:630: over the mist), clipped to the
        /// Stone minus the breach (never over the eye).</summary>
        public void BuildBolts(RectTransform host, Rect stage)
        {
            if (host == null || D == null || D.StrikeT == null) return;
            if (!D.Masks.TryGetValue("stone_minus_breach", out var sm)) return;
            var sp = MenuV6Art.Sprite(sm.file);
            if (sp == null) return;
            var clip = Img(host, "OmenBoltsClip", sp, sm.rect, stage);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _bolts = CrackNetGraphic.Create(clip.transform, "OmenBolts", sm.rect);
            _bolts.Mesh = _boltM;
            _boltClip = clip.gameObject;
            _boltClip.SetActive(false);   // on only while a strike draws (Bolts)
        }

        /// <summary>The violet bloom from S2(250, −280) and the pylons' darkening (menu.js:673-674), after Nut's fracture.</summary>
        public void BuildBloom(RectTransform host, Rect stage)
        {
            if (host == null || D == null) return;
            if (!string.IsNullOrEmpty(D.Bloom.file))
            {
                var sp = MenuV6Art.Sprite(D.Bloom.file);
                if (sp != null)
                {
                    _bloom = Img(host, "DreadBloom", sp, D.Bloom.rect.width > 0 ? D.Bloom.rect : stage, stage);
                    _bloom.color = new Color(1, 1, 1, 0);
                    _bloom.enabled = false;
                }
            }

            foreach (var p in D.Panels)
            {
                if (p.width <= 0) continue;
                var r = new Rect(p.x - 12, p.y - 12, p.width + 24, p.height + 24);
                var img = Img(host, "DreadPylon", UiFoundation.WhiteSprite(), r, stage);
                img.color = new Color(4 / 255f, 2 / 255f, 8 / 255f, 0f);
                img.enabled = false;
                _pylons.Add(img);
            }
        }

        /// <summary>The grade (anime_v61_fixup.js:72-82) after the vignette: the room's light drops by d everywhere but the
        /// breach front (graded × (1 − spare) inside it) and the sky-lid hole (a stencil mesh: the stage minus the step's
        /// polygon); the crack breaks are spared by a copy of the cracks over the grade (gaps at ea, the whole break at
        /// .35 ea = CRK.wrapDrop's cut-outs).</summary>
        public void BuildGrade(RigContext ctx, RectTransform host, Rect stage, MenuV6Stage owner)
        {
            _ctx = ctx;
            if (host == null || D == null || ctx == null) return;
            var maskHost = ctx.Gfx.PolyMask(host, "GradeMask", stage, out _gradeMask) as RectTransform;
            if (maskHost == null) return;
            GradeMaskFull();
            foreach (var (key, list) in new[] { ("radial_out", _gradeOut), ("band_out", _gradeOut), ("radial_in", _gradeIn), ("band_in", _gradeIn) })
            {
                if (!D.Grade.TryGetValue(key, out var gd)) continue;
                var sp = MenuV6Art.Sprite(gd.file);
                if (sp == null) continue;
                var img = Img(maskHost, "Grade_" + key, sp, gd.rect, stage);
                img.color = new Color(1, 1, 1, 0);
                img.enabled = false;
                list.Add(img);
            }

            _gradeMaskGo = maskHost.gameObject;
            _gradeMaskGo.SetActive(false);   // on only while a grade image draws (Grade)
            if (owner != null) _gradeCopy = owner.GradeCrackCopy(host, () => _gradeCopyK);
        }

        void GradeMaskFull()
        {
            if (_gradeMask == null) return;
            _gradeMask.Clear();
            var c = Color.white;
            _gradeMask.Quad(_stage.xMin, _stage.yMin, _stage.xMax, _stage.yMin, _stage.xMax, _stage.yMax, _stage.xMin, _stage.yMax, c, c, c, c);
            _gradeMask.Commit();
            _gradeMaskStep = 0;
        }

        /// <summary>The sky-lid apparition inside Nut's clip, after her fracture (story only).</summary>
        public void BuildSkyLid(RigContext ctx, RectTransform host, Rect frame)
        {
            if (host == null || D == null || ctx == null) return;
            _sky = SkyLid.Build(host, frame, D, ctx);
        }

        static Image Img(Transform parent, string name, Sprite sp, Rect r, Rect parentRect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            MenuV6Layout.Place(img.rectTransform, r, parentRect);
            return img;
        }

        // ================================================================== per frame

        double _last = double.NaN;

        void LateUpdate()
        {
            var t = (double)MenuV6Clock.Now;
            if (t == _last && !Story) return;
            _last = t;
            Apply(t);
        }

        /// <summary>Tests (edit mode): run every rung at <paramref name="times"/> (<paramref name="each"/> ticks what reads the
        /// ladder from outside: the rigs, the torch, Nut's fracture) and put the Stone back at rest.</summary>
        public void Probe(bool story, Action<double> each, params double[] times)
        {
            try
            {
                if (story) PlayStory(0);
                foreach (var t in times)
                {
                    ProbeT = t;
                    _t = double.NaN;
                    Apply(t);
                    if (each != null) each(t);
                }
            }
            finally
            {
                ProbeT = double.NaN;
                StopStory();
                _t = double.NaN;
                Apply(0);
            }
        }

        public void Apply(double t)
        {
            var S = At(t);
            var saver = WrldzTheme.BatterySaver;
            var quads = 0;
            var k = D != null ? D.K : 1;

            // the breach grows .02 omen about its centre (the rest plates stay still: P1)
            if (_breach != null)
            {
                var s = (float)DreadMath.BreachGrow(S);
                if (Mathf.Abs(_breach.localScale.x - s) > 1e-5f) _breach.localScale = new Vector3(s, s, 1f);
            }

            quads += Eyes(t, S);
            quads += Fissure(t, S, k);
            if (_smoke != null) quads += _smoke.Apply(t, S, saver);

            SetA(_stoneDark, MenuV6Linear.ShadeDark(.3 * S.Sg));
            if (_stoneDark != null && _stoneDark.enabled) quads++;
            quads += Bolts(t, S, saver);
            if (_sky != null) quads += _sky.Apply(t, S, saver || !Story);

            // the bloom (additive on the page: "over" with the dark-room exponent) and the pylons
            SetA(_bloom, MenuV6Linear.Alpha(S.Sg, MenuV6Linear.Dark));
            if (_bloom != null && _bloom.enabled) quads++;
            foreach (var p in _pylons)
            {
                SetA(p, MenuV6Linear.ShadeDark(.2 * S.Sg));
                if (p.enabled) quads++;
            }

            quads += Grade(S, saver);
            Quads = quads;
        }

        static void SetA(Graphic g, float a)
        {
            if (g == null) return;
            var on = a > .002f;
            if (g.enabled != on) g.enabled = on;
            if (!on) return;
            var c = g.color;
            if (Mathf.Abs(c.a - a) < 1e-4f) return;
            c.a = a;
            g.color = c;
        }

        // ------------------------------------------------------------------ his eyes

        int Eyes(double t, in DreadState S)
        {
            if (_holeMask == null) return 0;
            var on = EyeMath.DreadWindow(S, Story);
            if (_holeMask.gameObject.activeSelf != on) _holeMask.gameObject.SetActive(on);
            ShowP1(_p1Eye11, on ? 0f : _p1Eye11A);
            ShowP1(_p1Eye13, on ? 0f : _p1Eye13A);
            if (!on) return 0;

            var n = 0;
            DreadEyes.Breach(D, PageT(t), S, Story, _idle11, _idle12, _idle13, _eyeDraws);
            foreach (var e in _eyeDraws)
            {
                var q = e.Seed == 12 ? _eye12 : e.Seed == 13 ? _eye13 : _eyeM;
                if (q == null) continue;
                if (e.Seed == 11)
                    n += DrawEye(q, Story ? "breach_11s" : "breach_11", null, Story ? _texMs : _texM, null, e.X, e.Y, e.Px, e.S, e.Lit, e.A);
                else
                    n += DrawEye(q, e.Seed == 12 ? "breach_12" : "breach_13", Story ? (e.Seed == 12 ? "breach_12m" : "breach_13m") : null,
                        e.Seed == 12 ? _tex12 : _tex13, e.Seed == 12 ? _tex12m : _tex13m, e.X, e.Y, e.Px, e.S, e.Lit, e.A);
            }

            return n;
        }

        readonly List<EyeDraw> _eyeDraws = new();

        static void ShowP1(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            if (Mathf.Abs(c.a - a) < 1e-4f) return;
            c.a = a;
            g.color = c;
        }

        /// <summary>Draw the frame of <paramref name="bookId"/> (or its story companion) nearest to <paramref name="st"/> at
        /// the page's eye61Draw(x, y, px): the anchor at (x, y), the cell scaled px / book px about it. Breach eyes take the
        /// .8 wrapper (anime_v61_eye.js:204) first.</summary>
        int DrawEye(QuadLayer q, string bookId, string altId, RigTex tex, RigTex altTex, double x, double y, double px, EyeMath.St st,
            double lit, double a)
        {
            q.Clear();
            if (!D.Books.TryGetValue(bookId, out var book) || tex == null)
            {
                q.Commit();
                return 0;
            }

            var fi = book.Nearest(st, out var err);
            var useTex = tex;
            if (altId != null && altTex != null && D.Books.TryGetValue(altId, out var alt))
            {
                var fj = alt.Nearest(st, out var err2);
                if (err2 < err)
                {
                    book = alt;
                    fi = fj;
                    useTex = altTex;
                }
            }

            if (q.Tex != useTex) q.Tex = useTex;
            var f = book.Frames[fi];
            var s = px / Math.Max(1e-6, book.Px);
            var tint = DreadMath.EyeTint(lit);
            var uv = QuadList.Cell((int)(book.SheetW > 0 ? book.SheetW : useTex.W), (int)(book.SheetH > 0 ? book.SheetH : useTex.H), f.x, f.y, book.CellW, book.CellH);
            q.Pivoted(x, y, -book.AnchorX * s, -book.AnchorY * s, book.CellW * s, book.CellH * s, uv, new Color(tint, tint, tint, (float)a), 0);
            q.Commit();
            return 1;
        }

        // ------------------------------------------------------------------ the fissure lens (menu_v41.js:61-64)

        readonly double[] _lx = new double[64], _ly = new double[64];

        int Fissure(double t, in DreadState S, double k)
        {
            if (_fisFill == null) return 0;
            var op = DreadMath.FissureOp(S);
            var on = op > .02;
            if (_fisFill.gameObject.activeSelf != on) _fisFill.gameObject.SetActive(on);
            if (_fisLine != null)
            {
                _fisLine.Clear();
                if (!on) _fisLine.Commit();
            }

            if (!on) return 0;
            var e = MenuV6Math.Out(op);
            _fisMask.localScale = new Vector3(1f, (float)Math.Max(.001, e), 1f);
            var E = D.FisEye;
            if (DreadEyes.Fissure(D, PageT(t), S, Story, out var fe, out _))
                DrawEye(_fisEye, "fissure_11", null, _texFis, null, fe.X, fe.Y, fe.Px, fe.S, fe.Lit, fe.A);

            // the lens' rims: black .8 op at 2 k, then a pale sliver .3 op at 1 px, 1.2 px up
            if (_fisLine != null)
            {
                var h = 22 * k * e;
                var w = 92 * k;
                var n = Lens(E.x, E.y, w, h);
                for (var i = 0; i + 1 < n; i++)
                    _fisLine.Line(_lx[i], _ly[i], _lx[i + 1], _ly[i + 1], 2 * k, new Color(0, 0, 0, MenuV6Linear.Shade(.8 * op)),
                        new Color(0, 0, 0, MenuV6Linear.Shade(.8 * op)));
                var pale = new Color(214 / 255f, 200 / 255f, 176 / 255f, MenuV6Linear.Alpha(.3 * op, MenuV6Linear.Dark));
                for (var i = 0; i + 1 < n; i++) _fisLine.Line(_lx[i], _ly[i] - 1.2, _lx[i + 1], _ly[i + 1] - 1.2, 1, pale, pale);
                _fisLine.Commit();
            }

            return 3;
        }

        /// <summary>The lens path: quadratic from (x − w, y) via (x, y − 2h) to (x + w, y), back via (x, y + 1.4h).</summary>
        int Lens(double x, double y, double w, double h)
        {
            const int N = 24;
            var n = 0;
            for (var i = 0; i <= N; i++)
            {
                var u = i / (double)N;
                _lx[n] = (1 - u) * (1 - u) * (x - w) + 2 * (1 - u) * u * x + u * u * (x + w);
                _ly[n] = (1 - u) * (1 - u) * y + 2 * (1 - u) * u * (y - 2 * h) + u * u * y;
                n++;
            }

            for (var i = 1; i <= N && n < _lx.Length; i++)
            {
                var u = i / (double)N;
                _lx[n] = (1 - u) * (1 - u) * (x + w) + 2 * (1 - u) * u * x + u * u * (x - w);
                _ly[n] = (1 - u) * (1 - u) * y + 2 * (1 - u) * u * (y + 1.4 * h) + u * u * y;
                n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ the omen's black lightning

        int Bolts(double t, in DreadState S, bool saver)
        {
            if (_bolts == null) return 0;
            _boltM.Clear();
            var kk = DreadMath.BoltK(S.Sg);
            if (!saver && kk > .002 && D.StrikeT != null && D.StrikeT.Length > 0)
            {
                var tc = CrackTime.StutterTime(_stut, t, out _, out _);
                if (D.StrikeWrap > 1) tc %= D.StrikeWrap;   // the page has 1500 strikes (~539 s): wrap (decision)
                var k = D.K;
                for (var i = D.FirstStrike(tc); i < D.StrikeT.Length; i++)
                {
                    var st = D.StrikeT[i];
                    if (st > tc) break;
                    var e = DreadMath.BlEnv(tc - st);
                    if (e <= 0) continue;
                    BoltGraphic.Paint(_boltM, D.StrikePaths[i], e * kk * .95, k);
                }
            }

            var any = _boltM.V.Count > 0;
            if (_boltClip != null && _boltClip.activeSelf != any) _boltClip.SetActive(any);   // the clip's stencil only under a strike
            if (any || _boltsOn) _bolts.Dirty();
            _boltsOn = any;
            return any ? 1 : 0;
        }

        // ------------------------------------------------------------------ the grade

        int Grade(in DreadState S, bool saver)
        {
            var d = S.Drop;
            var on = d > .005;
            var spare = MenuV6Math.Clamp(S.Surf, 0, 1);
            var n = 0;
            foreach (var g in _gradeOut)
            {
                SetA(g, on ? MenuV6Linear.ShadeDark(d, MenuV6Linear.RoomDark, 8 / 255.0) : 0f);
                if (g.enabled) n++;
            }

            foreach (var g in _gradeIn)
            {
                SetA(g, on && spare < .999 ? MenuV6Linear.ShadeDark(d * (1 - spare), MenuV6Linear.RoomDark, 8 / 255.0) : 0f);
                if (g.enabled) n++;
            }

            // the mask's stencil only while a grade image draws (its polygon below is kept and rebuilt on enable)
            if (_gradeMaskGo != null && _gradeMaskGo.activeSelf != n > 0) _gradeMaskGo.SetActive(n > 0);
            // the sky-lid hole's polygon cut out of the grade (story)
            var step = !saver && Story && _sky != null ? DreadMath.SkyLidStep(S, out _) : 0;
            if (step != _gradeMaskStep) GradeMaskFor(step);
            // the breaks over the grade: eased in with it, so it never switches
            _gradeCopyK = on ? MenuV6Math.SStep(0, .08, d) : 0;
            if (_gradeCopy != null && _gradeCopyK > .004) n++;
            return n;
        }

        void GradeMaskFor(int step)
        {
            if (_gradeMask == null) return;
            if (step <= 0 || step > D.Sky.Count || D.Sky[step - 1].Polygon.Count < 3)
            {
                GradeMaskFull();
                return;
            }

            _gradeMask.Clear();
            var tris = PolyTri.RectMinusPolygon(_stage, D.Sky[step - 1].Polygon);
            var c = Color.white;
            for (var i = 0; i + 2 < tris.Count; i += 3)
                _gradeMask.Tri(tris[i].x, tris[i].y, c, tris[i + 1].x, tris[i + 1].y, c, tris[i + 2].x, tris[i + 2].y, c);
            _gradeMask.Commit();
            _gradeMaskStep = step;
        }
    }

    /// <summary>
    /// The whisper's ink blot and the omen's smoke mouth (BUILD_PLAN §6 P5 step 5; menu_v41.js:68-69, anime_v61_fixup.js:50-70,
    /// menu_v4.js:138-148): 80 dark puffs (cel kind mass) on their own layer that fades as ONE (fade = 1 − seg(k, .62, 1)), its
    /// 7 runes (glow additive, core over, the blur baked at its sharpest); the mouth's 18 dark puffs (kind mass), its black
    /// lips (a soft fill #020104 .96 k) and the faint wet depth. The page's 'ame' (lighter) puffs are consumed by its batcher.
    /// </summary>
    public sealed class BreachSmoke
    {
        CelLayer _ink, _mouth;
        CanvasGroup _inkGroup;
        SmokeSet _s;
        DreadData _d;
        readonly List<(Image glow, Image core, double size)> _runes = new();
        ShapeLayer _lips;
        double _r, _w, _lastInk = double.NaN, _lastMouth = double.NaN;

        public static BreachSmoke Build(RectTransform host, Rect stage, MenuLayoutKind kind, DreadData d, RigContext ctx)
        {
            if (host == null || d == null) return null;
            var s = SmokeSet.Load(kind);
            var sh = CelSmokeShader.Get();
            var b = new BreachSmoke { _s = s, _d = d };
            var land = kind == MenuLayoutKind.Landscape;
            b._r = (land ? 62 : 56) * d.K;
            b._w = 170 * d.K;
            if (s != null && sh != null && s.HasSprites() && s.Kinds.TryGetValue("mass", out var mass))
            {
                var inkHost = new GameObject("Ink", typeof(RectTransform), typeof(CanvasGroup));
                inkHost.transform.SetParent(host, false);
                UiTheme.Stretch(inkHost.GetComponent<RectTransform>());
                b._inkGroup = inkHost.GetComponent<CanvasGroup>();
                b._inkGroup.alpha = 0f;
                b._inkGroup.blocksRaycasts = false;
                b._inkGroup.interactable = false;
                var ri = 3.4 * b._r + 70;
                var inkRegion = Clip(new Rect((float)(d.InkAt.x - ri), (float)(d.InkAt.y - ri), (float)(2 * ri), (float)(2 * ri)), stage);
                b._ink = CelLayer.Create(inkHost.transform, "InkCel", stage, stage, inkRegion, s.Scale, mass, sh);
                foreach (var (glow, core, size) in d.Runes)
                {
                    var gs = MenuV6Art.Sprite(glow);
                    var cs = MenuV6Art.Sprite(core);
                    if (gs == null || cs == null) continue;
                    b._runes.Add((Rune(inkHost.transform, "RuneGlow", gs), Rune(inkHost.transform, "RuneCore", cs), size));
                }

                var rm = .9 * b._w + 60;
                var mouthRegion = Clip(new Rect((float)(d.GrinAt.x - rm), (float)(d.GrinAt.y - rm * .7), (float)(2 * rm), (float)(1.4 * rm)), stage);
                b._mouth = CelLayer.Create(host, "MouthCel", stage, stage, mouthRegion, s.Scale, mass, sh);
            }

            if (ctx != null)
            {
                b._lips = ctx.Gfx.Shapes(host, "MouthLips", stage);
                b._lips.Feather = (float)Math.Max(1.5, b._w * .02);
            }

            return b;
        }

        static Rect Clip(Rect r, Rect stage)
        {
            var x0 = Mathf.Max(r.xMin, stage.xMin);
            var y0 = Mathf.Max(r.yMin, stage.yMin);
            var x1 = Mathf.Min(r.xMax, stage.xMax);
            var y1 = Mathf.Min(r.yMax, stage.yMax);
            return new Rect(x0, y0, Mathf.Max(4f, x1 - x0), Mathf.Max(4f, y1 - y0));
        }

        static Image Rune(Transform parent, string name, Sprite sp)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;
            img.color = new Color(1, 1, 1, 0);
            img.enabled = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            return img;
        }

        /// <summary>Draw both at t; returns their quads.</summary>
        public int Apply(double t, in DreadState S, bool saver)
        {
            var n = 0;
            n += Ink(t, S, saver);
            n += Mouth(t, S, saver);
            return n;
        }

        int Ink(double t, in DreadState S, bool saver)
        {
            var k = DreadMath.InkK(S);
            var on = !saver && !double.IsNaN(k) && k > 0 && k < 1 && _ink != null;
            if (!on)
            {
                if (!double.IsNaN(_lastInk))
                {
                    if (_ink != null) _ink.Hide();
                    if (_inkGroup != null) _inkGroup.alpha = 0f;
                    foreach (var r in _runes)
                    {
                        r.glow.enabled = false;
                        r.core.enabled = false;
                    }
                }

                _lastInk = double.NaN;
                return 0;
            }

            var fade = 1 - MenuV6Math.Seg(k, .62, 1);
            if (_inkGroup != null) _inkGroup.alpha = MenuV6Linear.FadeDark(fade, .25);
            var x = _d.InkAt.x;
            var y = _d.InkAt.y;
            var r0 = _r;
            var bloom = MenuV6Math.Out(MenuV6Math.Seg(k, 0, .3));
            var rr = r0 * (.62 + .45 * bloom);
            if (double.IsNaN(_lastInk) || Math.Abs(t - _lastInk) >= 1 / SmokeField.MaxHz || t < _lastInk)
            {
                _lastInk = t;
                var R = MenuV6Math.Rng(3 * 31 + 7);
                _ink.Begin();
                var late = MenuV6Math.Seg(k, .5, 1);
                var grow = 1 + .7 * MenuV6Math.Seg(k, .45, 1);
                for (var i = 0; i < 80; i++)
                {
                    var an = R() * Math.PI * 2;
                    var d = rr * (.3 + .8 * Math.Sqrt(R())) + late * (6 + 26 * R());
                    var px2 = x + Math.Cos(an) * d;
                    var py2 = y + Math.Sin(an) * d * .8 + late * 14;
                    var s = r0 * (.3 + .4 * R()) * grow;
                    var a = .75 * bloom * (.45 + .55 * R());
                    _ink.Buf.Splat(_s.Dark[i % 4], px2 - s / 2, py2 - s / 2, 0, 0, s, s, 0, a);
                }

                _ink.End();
            }

            // the 7 runes: condense, then dissolve (anime_v61_fixup.js:64-67)
            var cond = MenuV6Math.Seg(k, .1, .36);
            var dis = MenuV6Math.Seg(k, .66, .96);
            for (var i = 0; i < _runes.Count; i++)
            {
                var (glow, core, size) = _runes[i];
                var an = -Math.PI * .92 + i * .31 + t * .02;
                var a = .85 * cond * (1 - dis) * MenuV6Math.Clamp(cond * 7 - i * .7, 0, 1);
                if (a <= .01)
                {
                    glow.enabled = false;
                    core.enabled = false;
                    continue;
                }

                var px2 = x + Math.Cos(an) * rr * .7;
                var py2 = y + Math.Sin(an) * rr * .56 + dis * 12;
                var sz = (float)(size * (1.3 - .3 * cond));
                foreach (var (img, alpha) in new[] { (glow, MenuV6Linear.Alpha(a, MenuV6Linear.Dark)), (core, (float)a) })
                {
                    img.enabled = true;
                    var rt = img.rectTransform;
                    rt.sizeDelta = new Vector2(sz, sz);
                    rt.anchoredPosition = new Vector2((float)px2, (float)-py2);
                    rt.localEulerAngles = new Vector3(0, 0, (float)(-(an + Math.PI / 2) * Mathf.Rad2Deg));
                    var c = img.color;
                    c.a = alpha;
                    img.color = c;
                }
            }

            return 1 + 2 * _runes.Count;
        }

        readonly double[] _mx = new double[96], _my = new double[96];

        int Mouth(double t, in DreadState S, bool saver)
        {
            var k = DreadMath.MouthK(S);
            var on = !saver && k > .01;
            if (!on)
            {
                if (!double.IsNaN(_lastMouth))
                {
                    if (_mouth != null) _mouth.Hide();
                    if (_lips != null)
                    {
                        _lips.Clear();
                        _lips.Commit();
                    }
                }

                _lastMouth = double.NaN;
                return 0;
            }

            double x = _d.GrinAt.x, y = _d.GrinAt.y, w = _w, rot = -.28;
            var h = w * .15 * MenuV6Math.Out(k);
            var sway = Math.Sin(t * .8) * .03;
            double cr = Math.Cos(rot), sr = Math.Sin(rot);
            if (_mouth != null && (double.IsNaN(_lastMouth) || Math.Abs(t - _lastMouth) >= 1 / SmokeField.MaxHz || t < _lastMouth))
            {
                _lastMouth = t;
                _mouth.Begin();
                for (var i = 0; i < 18; i++)
                {
                    var u = i / 17.0;
                    var px2 = MenuV6Math.Lerp(-w / 2, w / 2, u);
                    var py2 = MenuV6Math.Lerp(0, -h * .25, u) + Math.Sin(Math.PI * u) * h * .6;
                    _mouth.Buf.Splat(_s.Dark[i % 4], x, y, px2 - w * .12, py2 - w * .09, w * .24, w * .18, rot, .35 * k);
                }

                _mouth.End();
            }

            if (_lips == null) return 1;
            // the lips: two cubic Béziers in the mouth's frame (translate + rotate), filled soft #020104 at .96 k
            var n = 0;
            void Bez(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, bool first)
            {
                const int N = 20;
                for (var i = first ? 0 : 1; i <= N && n < _mx.Length; i++)
                {
                    var u = i / (double)N;
                    double a = (1 - u) * (1 - u) * (1 - u), b = 3 * (1 - u) * (1 - u) * u, c = 3 * (1 - u) * u * u, d = u * u * u;
                    var lx = a * x0 + b * x1 + c * x2 + d * x3;
                    var ly = a * y0 + b * y1 + c * y2 + d * y3;
                    _mx[n] = x + lx * cr - ly * sr;
                    _my[n] = y + lx * sr + ly * cr;
                    n++;
                }
            }

            Bez(-w / 2, 0, -w * .2, h * (1.6 + sway), w * .22, h * 1.7, w / 2, -h * .25, true);
            Bez(w / 2, -h * .25, w * .2, h * .35, -w * .2, h * .2, -w / 2, 0, false);
            _lips.Clear();
            _lips.Poly(_mx, _my, n - 1, new Color(2 / 255f, 1 / 255f, 4 / 255f, MenuV6Linear.Shade(.96 * k)));
            // the faint wet depth far down: a fan from the mouth's centre, rgba(60,20,70) at .35 k toward its bottom
            double cx = 0, cy = 0;
            for (var i = 0; i < n - 1; i++)
            {
                cx += _mx[i];
                cy += _my[i];
            }

            cx /= Math.Max(1, n - 1);
            cy /= Math.Max(1, n - 1);
            Color Wet(double px, double py)
            {
                // the gradient runs along the mouth's local y from 0 to h 1.6
                var ly = -(px - x) * sr + (py - y) * cr;
                var f = MenuV6Math.Clamp(ly / Math.Max(1e-3, h * 1.6), 0, 1);
                return new Color(60 / 255f, 20 / 255f, 70 / 255f, MenuV6Linear.Alpha(.35 * k * f, MenuV6Linear.Dark));
            }

            var cc = Wet(cx, cy);
            for (var i = 0; i + 1 < n - 1; i++)
                _lips.Tri(cx, cy, cc, _mx[i], _my[i], Wet(_mx[i], _my[i]), _mx[i + 1], _my[i + 1], Wet(_mx[i + 1], _my[i + 1]));
            _lips.Commit();
            return 3;
        }
    }

    /// <summary>Triangulation of "a rect minus one simple polygon inside it" (the grade's stencil around the sky-lid hole):
    /// a bridge from the rect's nearest corner to the polygon's nearest vertex makes one simple polygon, then ear clipping.</summary>
    public static class PolyTri
    {
        public static List<Vector2> RectMinusPolygon(Rect r, List<Vector2> hole)
        {
            var outTris = new List<Vector2>();
            if (hole == null || hole.Count < 3)
            {
                outTris.Add(new Vector2(r.xMin, r.yMin));
                outTris.Add(new Vector2(r.xMax, r.yMin));
                outTris.Add(new Vector2(r.xMax, r.yMax));
                outTris.Add(new Vector2(r.xMin, r.yMin));
                outTris.Add(new Vector2(r.xMax, r.yMax));
                outTris.Add(new Vector2(r.xMin, r.yMax));
                return outTris;
            }

            // outer clockwise on screen (y down): TL, TR, BR, BL; the hole the other way round
            var outer = new List<Vector2> { new(r.xMin, r.yMin), new(r.xMax, r.yMin), new(r.xMax, r.yMax), new(r.xMin, r.yMax) };
            var h = new List<Vector2>(hole);
            if (Area(h) * Area(outer) > 0) h.Reverse();
            // the bridge: the hole vertex and outer corner closest to each other
            int hi = 0, oi = 0;
            var best = float.MaxValue;
            for (var i = 0; i < h.Count; i++)
            for (var j = 0; j < outer.Count; j++)
            {
                var d = (h[i] - outer[j]).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    hi = i;
                    oi = j;
                }
            }

            var poly = new List<Vector2>();
            for (var j = 0; j <= outer.Count; j++) poly.Add(outer[(oi + j) % outer.Count]);
            for (var i = 0; i <= h.Count; i++) poly.Add(h[(hi + i) % h.Count]);
            EarClip(poly, outTris);
            return outTris;
        }

        static float Area(List<Vector2> p)
        {
            float a = 0;
            for (var i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].x * q.y - q.x * p[i].y;
            }

            return a * .5f;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(a, b, p);
            var d2 = Cross(b, c, p);
            var d3 = Cross(c, a, p);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static void EarClip(List<Vector2> poly, List<Vector2> outTris)
        {
            var idx = new List<int>();
            for (var i = 0; i < poly.Count; i++) idx.Add(i);
            var sign = Area(poly) >= 0 ? 1f : -1f;
            var guard = poly.Count * poly.Count + 16;
            while (idx.Count > 3 && guard-- > 0)
            {
                var cut = false;
                for (var i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    if (Cross(a, b, c) * sign <= 1e-6f) continue;   // reflex or degenerate
                    var ear = true;
                    for (var j = 0; j < idx.Count && ear; j++)
                    {
                        var ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        var p = poly[ij];
                        if ((p - a).sqrMagnitude < 1e-6f || (p - b).sqrMagnitude < 1e-6f || (p - c).sqrMagnitude < 1e-6f) continue;   // the bridge's twin vertices
                        if (Inside(p, a, b, c)) ear = false;
                    }

                    if (!ear) continue;
                    outTris.Add(a);
                    outTris.Add(b);
                    outTris.Add(c);
                    idx.RemoveAt(i);
                    cut = true;
                    break;
                }

                if (!cut) break;
            }

            if (idx.Count == 3)
            {
                outTris.Add(poly[idx[0]]);
                outTris.Add(poly[idx[1]]);
                outTris.Add(poly[idx[2]]);
            }
        }
    }
}
