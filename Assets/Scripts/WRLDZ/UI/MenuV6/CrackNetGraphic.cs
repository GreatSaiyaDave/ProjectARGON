using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// One crack mesh on UI/Default (BUILD_PLAN D3, D11): a <see cref="CrackMesh"/> (vertex colours, optionally the realm
    /// tile's UVs) or a <see cref="QuadList"/> (the mist cels) mapped from stage px into this graphic's rect, plus the time
    /// stutter's two ghost copies when <see cref="Waver"/> is on. No shader of its own, no material: the texture is the
    /// realm tile, the mist sheet or none (white).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CrackNetGraphic : MaskableGraphic
    {
        public Rect Frame;
        [NonSerialized] public CrackMesh Mesh;   // built at runtime from the cracks json, never saved
        [NonSerialized] public QuadList Quads;
        Texture _tex;

        /// <summary>The stutter's ghosts (applyWaver): w = waver, dir = ±1, k = the net's k.</summary>
        public double Waver, WaverK = 1;
        public int WaverDir = 1;

        /// <summary>Realm UV drift (stage px) applied at populate time, so the realm's slow drift needs no geometry rebuild.</summary>
        public double UvShift;

        static readonly List<UIVertex> Verts = new();
        static readonly List<int> Idx = new();

        public override Texture mainTexture => _tex != null ? _tex : s_WhiteTexture;

        public static CrackNetGraphic Create(Transform parent, string name, Rect frame, Texture tex = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CrackNetGraphic));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<CrackNetGraphic>();
            g.Frame = frame;
            g._tex = tex;
            g.raycastTarget = false;
            UiTheme.Stretch(g.rectTransform);
            return g;
        }

        public void SetTexture(Texture t)
        {
            if (_tex == t) return;
            _tex = t;
            SetMaterialDirty();
        }

        public void Dirty() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            List<StageVert> V;
            List<int> I;
            double tile = 0;
            if (Mesh != null)
            {
                V = Mesh.V;
                I = Mesh.I;
                tile = Mesh.UvTile;
            }
            else if (Quads != null)
            {
                V = Quads.V;
                I = Quads.I;
            }
            else return;

            if (V.Count == 0) return;
            var map = new StageMap(Frame, rectTransform.rect);
            var passes = Waver > .01 ? 3 : 1;
            Verts.Clear();
            Idx.Clear();
            var du = tile > 0 ? (float)(UvShift / tile) : 0f;
            for (var p = 0; p < passes; p++)
            {
                double dx = 0, dy = 0, ka = 1;
                if (p == 1)
                {
                    dx = WaverDir * 7 * WaverK * Waver;
                    dy = -2 * WaverK * Waver;
                    ka = .34 * Waver;
                }
                else if (p == 2)
                {
                    dx = -WaverDir * 4 * WaverK * Waver;
                    dy = 1.5 * WaverK * Waver;
                    ka = .2 * Waver;
                }

                var b = Verts.Count;
                foreach (var sv in V)
                {
                    var v = UIVertex.simpleVert;
                    v.position = map.P(sv.X + dx, sv.Y + dy);
                    var c = sv.C;
                    if (p > 0) c.a = (byte)Math.Round(c.a * ka);
                    v.color = c;
                    v.uv0 = new Vector2(sv.U + du, sv.V);
                    Verts.Add(v);
                }

                foreach (var i in I) Idx.Add(i + b);
            }

            vh.AddUIVertexStream(Verts, Idx);
        }
    }

    /// <summary>What a crack layer draws at time t: its items (net, extent, alpha, breathe, mist), the crack clock tc (the
    /// stutter's), the waver, and whether the mist and the bolts run.</summary>
    public interface ICrackDriver
    {
        /// <summary>Fill <paramref name="items"/> for clock <paramref name="t"/>; false = nothing to draw this frame.</summary>
        bool Frame(double t, List<CrackItem> items, out double tc, out double waver, out int dir);

        /// <summary>The layer alpha the mist is drawn at (0 = no mist on this layer).</summary>
        double MistA { get; }

        /// <summary>The bolt schedule (null = no bolts on this layer) and the alpha the strikes take (eA).</summary>
        CrackBeat[] Bolts { get; }

        double BoltA { get; }
    }

    /// <summary>
    /// One crack layer of a page (#crkU / the menu's Nut fracture / the through-the-dim copy): its nets in the page's pass
    /// order (every net's lips, then every gap, then every net's depth / rim / hairlines / web), the bolts and the mist on
    /// top, on its own nested Canvas so a mesh rebuild never re-batches the stage. A net's meshes are rebuilt only when its
    /// extent / breathe / alpha moved; the realm's drift is a UV shift. The clock comes from <see cref="Clock"/> (the menu
    /// clock, the title's raw clock); in edit mode <see cref="Render"/> draws one static frame.
    /// </summary>
    public sealed class CrackLayer : MonoBehaviour
    {
        sealed class NetGfx
        {
            public CrackNet N;
            public CrackNetGraphic Lips, Gap, Depth, Top;
            public readonly CrackMesh LipsM = new(), GapM = new(), DepthM = new(), TopM = new();
            public long Key = long.MinValue;    // the alpha / the gaps' alpha (rebuilt at once when it moves)
            public long KeyO = long.MinValue;   // the breathe (P5: 1/400 steps, a few nets per frame)
            public double LastE = double.NaN;   // the extent the meshes were built at
            public double LastDrift = double.NaN;
        }

        public const string RealmTile = "WRLDZ/MenuV6/Common/realm_tile";
        public const double RealmTileSize = 512, RealmDrift = 4;   // drawVoidIn drift 5 px/s, clouds at .6-1 of it

        readonly List<NetGfx> _g = new();
        readonly List<CrackItem> _items = new();
        readonly CrackPainter _paint = new();
        CrackNetGraphic _bolts, _mist;
        readonly CrackMesh _boltM = new();
        readonly QuadList _mistQ = new();
        MistCels _cels;
        bool _realm;
        Rect _frame;
        bool _boltsOn, _mistOn, _waverOn;

        public ICrackDriver Driver;
        public Func<double> Clock;

        /// <summary>P5: the gaps' (and their depth bands') alpha instead of the items' largest (the grade's crack copy: the
        /// gaps at ea, the rest of the break at the items' .35 ea); null = the page's one drawVoidIn alpha.</summary>
        public Func<double> GapAlpha;

        /// <summary>P5 (BUILD_PLAN §6 P5 step 7, the P2 review's 75k verts / 2.4 ms per frame): the breathe and the creep
        /// moved every net past its rebuild key nearly every frame. Open is now keyed at 1/<see cref="OpenSteps"/> (a .25 %
        /// width step: ~.05 px on a 20 px break); the breathe and the creep's small steps (the extent at .125 px, under
        /// <see cref="ExtentJump"/>) share a budget of <see cref="BreatheBudget"/> nets per frame (round robin); an alpha
        /// change and an extent jump (the grow-in) rebuild at once. Modelled with the real driver and painter over 60 s of
        /// the menu idle (p5/perf): landscape 11.5 -> 3.9 nets / frame, 1.33 -> .54 ms, p95 1.70 -> .80 ms; portrait 6.2 -> 2.6.</summary>
        public const double OpenSteps = 400;

        public const double ExtentJump = 2;

        public int BreatheBudget = 4;
        int _rr;
        bool _rendered;
        public CrackSet Set { get; private set; }
        public int VertexCount { get; private set; }

        /// <summary>Build the layer for <paramref name="nets"/> under <paramref name="parent"/> (covering the stage rect
        /// <paramref name="frame"/>). Null when there is nothing to draw.</summary>
        public static CrackLayer Create(Transform parent, string name, Rect frame, CrackSet set, IList<CrackNet> nets,
            ICrackDriver driver, Func<double> clock, MistCels cels = null, bool nestedCanvas = true)
        {
            if (parent == null || set == null || nets == null || nets.Count == 0 || driver == null) return null;
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            UiTheme.Stretch(rt);
            if (nestedCanvas)
            {
                var cv = go.AddComponent<Canvas>();   // nested: a rebuild re-batches only the cracks
                cv.overrideSorting = false;
            }
            var L = go.AddComponent<CrackLayer>();
            L.Set = set;
            L.Driver = driver;
            L.Clock = clock;
            L._frame = frame;
            L._cels = cels;
            var realm = MenuV6Art.Texture(RealmTile);
            L._realm = realm != null;
            foreach (var n in nets)
                if (n != null)
                    L._g.Add(new NetGfx { N = n });
            foreach (var g in L._g) g.Lips = CrackNetGraphic.Create(rt, "Lips_" + g.N.Id, frame);
            foreach (var g in L._g)
            {
                // the gaps are a stencil mask (shown): the dark depth bands are the page's lip strokes clipped to them
                g.Gap = CrackNetGraphic.Create(rt, "Gap_" + g.N.Id, frame, realm);
                g.Gap.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                g.Depth = CrackNetGraphic.Create(g.Gap.transform, "Depth_" + g.N.Id, frame);
            }

            foreach (var g in L._g) g.Top = CrackNetGraphic.Create(rt, "Top_" + g.N.Id, frame);
            foreach (var g in L._g)
            {
                g.Lips.Mesh = g.LipsM;
                g.Gap.Mesh = g.GapM;
                g.Depth.Mesh = g.DepthM;
                g.Top.Mesh = g.TopM;
                if (L._realm) g.GapM.UvTile = RealmTileSize;
            }

            if (driver.Bolts != null)
            {
                L._bolts = CrackNetGraphic.Create(rt, "Bolts", frame);
                L._bolts.Mesh = L._boltM;
            }

            if (cels != null && driver.MistA > 0)
            {
                var sheet = MenuV6Art.Texture(MistCels.Sheet);
                if (sheet != null)
                {
                    cels.SheetW = sheet.width;
                    cels.SheetH = sheet.height;
                    L._mist = CrackNetGraphic.Create(rt, "Mist", frame, sheet);
                    L._mist.Quads = L._mistQ;
                }
            }

            return L;
        }

        void LateUpdate()
        {
            // Battery Saver (SPEC §8.7: end states, no seam runs, no lightning): the layer holds the frame it last drew. The
            // grade's crack copy (GapAlpha) keeps following the grade, which keeps its timing in Battery Saver; its clock is the
            // Stone's held frame (MenuV6Stage.GradeCrackCopy), so it spares exactly the breaks on screen (P5 review)
            if (_rendered && WrldzTheme.BatterySaver && GapAlpha == null) return;
            if (Clock != null) Render(Clock());
        }

        /// <summary>The clock time of the last <see cref="Render"/> (NaN before the first): the frame a held layer shows.</summary>
        public double LastT { get; private set; } = double.NaN;

        static long KeyOf(in CrackItem it) => (long)Math.Round(it.A * 400);   // the alpha 1/400 (extent and breathe apart)

        static long KeyOpen(in CrackItem it) => (long)Math.Round(it.Open * OpenSteps);

        /// <summary>Draw the layer for clock t (play mode: every frame; edit mode: once, from the builder).</summary>
        public void Render(double t)
        {
            if (Driver == null) return;
            _items.Clear();
            var on = Driver.Frame(t, _items, out var tc, out var waver, out var dir);
            if (!on) _items.Clear();
            // the gaps' alpha: the layer's largest (the page's one drawVoidIn over the union)
            double am = 0;
            foreach (var it in _items) am = Math.Max(am, it.A);
            if (GapAlpha != null) am = on ? Math.Max(0, Math.Min(1, GapAlpha())) : 0;
            var depthA = GapAlpha != null;
            var verts = 0;
            var drift = tc * RealmDrift;
            // edit mode and a frozen capture clock draw every net exact at once (the stills); the live menu spreads the soft steps
            var budget = Application.isPlaying && !MenuV6Clock.Frozen ? Math.Max(1, BreatheBudget) : int.MaxValue;
            var n0 = _g.Count;
            if (n0 > 0) _rr = (_rr + 1) % n0;
            for (var ii = 0; ii < n0; ii++)
            {
                var i = (ii + _rr) % n0;
                var g = _g[i];
                var idx = -1;
                for (var j = 0; j < _items.Count; j++)
                    if (_items[j].N == g.N)
                    {
                        idx = j;
                        break;
                    }

                var has = idx >= 0 && _items[idx].A > .004 && _items[idx].E >= 1;
                var key = has ? KeyOf(_items[idx]) ^ ((long)Math.Round(am * 400) << 50) : long.MinValue + 1;
                var keyO = has ? KeyOpen(_items[idx]) : long.MinValue + 1;
                var e = has ? _items[idx].E : double.NaN;
                var jump = has != !double.IsNaN(g.LastE) || (has && Math.Abs(e - g.LastE) >= ExtentJump);
                var soft = key == g.Key && !jump && (keyO != g.KeyO || (has && Math.Round(e * 8) != Math.Round(g.LastE * 8)));
                if (soft && budget <= 0) soft = false;   // its turn comes next frame (round robin)
                else if (soft) budget--;
                if (key != g.Key || jump || soft)
                {
                    g.Key = key;
                    g.KeyO = keyO;
                    g.LastE = e;
                    g.LipsM.Clear();
                    g.GapM.Clear();
                    g.DepthM.Clear();
                    g.TopM.Clear();
                    if (has)
                    {
                        var it = _items[idx];
                        var sh = _paint.ShapesOf(g.N);
                        if (_paint.Shape(g.N, it.E, it.Open) > 0)
                        {
                            _paint.Lips(g.N, sh, it.A, g.LipsM);
                            _paint.Gaps(g.N, sh, am, _realm ? new Color32(255, 255, 255, 255) : CrackPainter.RealmDeep, g.GapM);
                            _paint.DepthBands(g.N, sh, depthA ? am : it.A, g.DepthM, true);
                            _paint.Rest(g.N, sh, it.A, it.E, g.TopM);
                        }
                    }

                    g.Lips.Dirty();
                    g.Gap.Dirty();
                    g.Depth.Dirty();
                    g.Top.Dirty();
                    g.LastDrift = drift;
                }
                else if (_realm && g.GapM.V.Count > 0 && (double.IsNaN(g.LastDrift) || Math.Abs(drift - g.LastDrift) >= .5))
                {
                    g.LastDrift = drift;
                    g.Gap.Dirty();
                }

                g.Gap.UvShift = g.LastDrift;
                verts += g.LipsM.V.Count + g.GapM.V.Count + g.DepthM.V.Count + g.TopM.V.Count;
            }

            // the bolts (black lightning, l.60): on their own schedule, drawn before the waver as the page does
            if (_bolts != null)
            {
                _boltM.Clear();
                if (on && Driver.BoltA > .01)
                    BoltGraphic.Strikes(Driver.Bolts, Set.BoltNets, ExtentOf, tc, Driver.BoltA, 1000, _boltM);
                var anyB = _boltM.V.Count > 0;
                if (anyB || _boltsOn) _bolts.Dirty();
                _boltsOn = anyB;
                verts += _boltM.V.Count;
            }

            if (_mist != null)
            {
                _mistQ.Clear();
                if (on) _cels.Build(_items, tc, Driver.MistA, _mistQ);
                var anyM = _mistQ.V.Count > 0;
                if (anyM || _mistOn) _mist.Dirty();
                _mistOn = anyM;
                verts += _mistQ.V.Count;
            }

            // the time stutter: two ghost copies of the whole layer that converge (no brightness change)
            var wv = waver > .01;
            if (wv || _waverOn)
            {
                foreach (var g in _g)
                {
                    foreach (var gr in new[] { g.Lips, g.Gap, g.Depth, g.Top })
                    {
                        gr.Waver = wv ? waver : 0;
                        gr.WaverDir = dir;
                        gr.WaverK = g.N.K;
                        gr.Dirty();
                    }
                }

                if (_bolts != null)
                {
                    _bolts.Waver = wv ? waver : 0;
                    _bolts.WaverDir = dir;
                    _bolts.WaverK = Set.K;
                    _bolts.Dirty();
                }
            }

            _waverOn = wv;
            VertexCount = verts;
            _rendered = true;
            LastT = t;
        }

        double ExtentOf(CrackNet n)
        {
            foreach (var it in _items)
                if (it.N == n)
                    return it.E;
            return 0;
        }
    }

    // ===================================================================================================================== drivers

    /// <summary>The menu's wall cracks (#crkU, anime_v61_cracks.js:631-680): grow in eased in and out (menu 0.25-2.0 s; the
    /// boot handoff 1.9-2.95 s after the Items have left the row), creep by time only (+30 % over ~80 s), breathe ±3 % over
    /// 5.3 s, the LivingSky spasm, the time stutter (seed 912) and the black lightning (seed 911). No DUEL-press reaction.</summary>
    public sealed class MenuCrackDriver : ICrackDriver
    {
        readonly List<CrackNet> _nets;
        readonly bool _enter;
        readonly CrackBeat[] _stut, _bolts;
        double _eA = 1;

        /// <summary>The alpha each item takes (the through-the-dim copy multiplies it; 1 for #crkU).</summary>
        public Func<double> AlphaK;

        /// <summary>The through-the-dim copy: items take sp.lit (anime_v61_cracks.js:673).</summary>
        public bool DimCopy;

        /// <summary>P5: the copy over the dread grade (CRK.wrapDrop's cut-outs): items at <see cref="AlphaK"/>, no bolts, no
        /// stutter ghosts; the layer's <see cref="CrackLayer.GapAlpha"/> carries the gaps.</summary>
        public bool GradeCopy;

        /// <summary>The Stone at rest (no entrance: edit mode, captures, a menu rebuilt without a token): the breaks are grown
        /// from t = 0 and only creep and breathe.</summary>
        public bool Rest;

        public MenuCrackDriver(IList<CrackNet> nets, bool enter, bool deck = false)
        {
            _nets = new List<CrackNet>(nets);
            _enter = enter;
            // menu BOLTS 911 / STUT 912; deck 921 / 922 (anime_v61_cracks.js:526, :733)
            _bolts = deck ? CrackTime.Schedule(921, 5.5, 8, 15, 600) : CrackTime.Schedule(911, 6.5, 7, 14, 600);
            _stut = deck ? CrackTime.Schedule(922, 26, 43, 71, 200) : CrackTime.Schedule(912, 21.5, 43, 71, 200);
        }

        public static double Intro(bool enter, double t) =>
            enter ? MenuV6Math.InOut(MenuV6Math.Seg(t, 1.9, 2.95)) : MenuV6Math.InOut(MenuV6Math.Seg(t, .25, 2.0));

        public static double EnterA(bool enter, double t) => enter ? MenuV6Math.Out((t - .45) / .9) : 1;

        public bool Frame(double t, List<CrackItem> items, out double tc, out double waver, out int dir)
        {
            tc = CrackTime.StutterTime(_stut, t, out waver, out dir);
            _eA = Rest ? 1 : EnterA(_enter, t);
            var intro = Rest ? 1 : Intro(_enter, t);
            var k = AlphaK != null ? AlphaK() : 1;
            if (DimCopy && k <= .01) return false;
            if (GradeCopy && k <= .002) return false;
            foreach (var n in _nets)
            {
                var sps = CrackTime.Spasm(tc, n.Seed);
                var a = _eA * k * (DimCopy ? n.Sp.Lit : 1);
                items.Add(new CrackItem
                {
                    N = n, E = CrackTime.Extent(n, intro, t), A = a, Open = 1 + .03 * Math.Sin(tc * Math.PI * 2 / 5.3 + n.Seed) + .16 * sps,
                    Mist = 1 + .4 * sps
                });
            }

            if (DimCopy || GradeCopy) waver = 0;
            return true;
        }

        public double MistA => 0;   // the menu's floor mist stays in the baked smoke plate until P5's live smoke (decision)
        public CrackBeat[] Bolts => DimCopy || GradeCopy ? null : _bolts;
        public double BoltA => _eA;
    }

    /// <summary>Nut's old sky fracture drawn as a break (anime_v61_cracks.js:592-602): run to riftRun (the dread ladder runs
    /// it on: P5), breathing; in the boot handoff it runs with the walls (eIO(seg(u, 1.9, 2.95))); its alpha is the room's
    /// (E.bg), set by the caller.</summary>
    public sealed class NutCrackDriver : ICrackDriver
    {
        readonly CrackNet _n;
        readonly double _riftRun;
        readonly bool _enter;
        readonly CrackBeat[] _stut = CrackTime.Schedule(912, 21.5, 43, 71, 200);
        public Func<double> AlphaK;

        /// <summary>P5: the dread ladder's run (0..1, to the fracture's end) and widening (+ .4 max(omen, .6 appar)) at t
        /// (anime_v61_cracks.js:595-598); null = at rest.</summary>
        public Func<double, (double run, double open)> Dread;

        public NutCrackDriver(CrackNet nut, double riftRun, bool enter)
        {
            _n = nut;
            _riftRun = riftRun;
            _enter = enter;
        }

        public bool Frame(double t, List<CrackItem> items, out double tc, out double waver, out int dir)
        {
            tc = CrackTime.StutterTime(_stut, t, out waver, out dir);
            waver = 0;   // the page waves only #crkU; Nut's fracture is on the main canvas
            var e = AlphaK != null ? AlphaK() : 1;
            if (e <= .01 || _n == null) return false;
            var ent = _enter ? MenuV6Math.InOut(MenuV6Math.Seg(t, 1.9, 2.95)) : 1;
            if (ent <= .0005) return false;
            var run = 0.0;
            var open = 0.0;
            if (Dread != null) (run, open) = Dread(t);
            items.Add(new CrackItem
            {
                N = _n, E = _n.L * MenuV6Math.Lerp(_riftRun, 1, run) * ent, A = e,
                Open = 1 + .03 * Math.Sin(tc * Math.PI * 2 / 5.3 + _n.Seed) + open, Mist = 0
            });
            return true;
        }

        public double MistA => 0;
        public CrackBeat[] Bolts => null;
        public double BoltA => 0;
    }

    /// <summary>
    /// The title's Tears (anime_v61_cracks.js:764-783, v6.3 final): they open as the l.49 + l.64 caption is read (base 19.6
    /// = raw 22.2, the sky Tears .35 / .5 s later) over 1.6 s on v6.1's timing with a soft start, creep and breathe, seep
    /// mist, and at TOUCH stay under the rising room and fade as it covers it (leaving + .45 .. + 1.35). No bolts, no
    /// stutter, no eye. t is the RAW title clock.
    /// </summary>
    public sealed class TitleCrackDriver : ICrackDriver
    {
        public const double T0 = 19.6;
        readonly List<CrackNet> _nets;
        double _a = 1;

        /// <summary>Raw time of TOUCH (NaN = not leaving).</summary>
        public double Leaving = double.NaN;

        public TitleCrackDriver(IList<CrackNet> nets) => _nets = new List<CrackNet>(nets);

        /// <summary>title.js origR: the base clock (the origin shot's 2.6 s freeze at base 7.4).</summary>
        public static double Base(double raw) => TitleMath.Base(raw);

        public bool Frame(double tr, List<CrackItem> items, out double tc, out double waver, out int dir)
        {
            tc = tr;
            waver = 0;
            dir = 1;
            var tb = Base(tr);
            var idle = MenuV6Math.Seg(tb, T0, T0 + 1.6);
            if (idle <= 0) return false;
            var lv = double.IsNaN(Leaving) ? 0 : MenuV6Math.InOut(MenuV6Math.Seg(tr, Leaving + .45, Leaving + 1.35));
            _a = 1 - lv;
            if (_a <= .01) return false;
            foreach (var n in _nets)
            {
                var sps = CrackTime.Spasm(tc, n.Seed);
                var dl = n.Sp.Delay;
                var uI = MenuV6Math.Seg(tb, T0 + dl, T0 + dl + 1.6);
                var intro = MenuV6Math.Out(uI) * MenuV6Math.SStep(0, .1, uI);
                items.Add(new CrackItem
                {
                    N = n, E = CrackTime.Extent(n, intro, tb - T0 - dl), A = _a,
                    Open = 1 + .03 * Math.Sin(tc * Math.PI * 2 / 5.3 + n.Seed) + .16 * sps, Mist = .8 + .3 * sps
                });
            }

            return true;
        }

        public double MistA => _a;
        public CrackBeat[] Bolts => null;
        public double BoltA => 0;
    }

    /// <summary>Parsed crack / mist JSON, cached per TextAsset (a reimport hands back a new asset and is parsed again).</summary>
    public static class CrackData
    {
        static readonly Dictionary<string, (TextAsset ta, object parsed)> Cache = new();

        public static Dictionary<string, object> Json(string name)
        {
            var ta = MenuV6Art.Data(name);
            if (ta == null) return null;
            if (Cache.TryGetValue(name, out var hit) && hit.ta == ta) return hit.parsed as Dictionary<string, object>;
            var parsed = MenuV6Json.Parse(ta.text);
            Cache[name] = (ta, parsed);
            return parsed as Dictionary<string, object>;
        }

        static readonly Dictionary<string, (TextAsset ta, CrackSet set)> Sets = new();

        /// <summary>cracks_&lt;page&gt;_&lt;land|port&gt;.json as a <see cref="CrackSet"/> (null when missing or malformed).</summary>
        public static CrackSet Load(string page, MenuLayoutKind kind)
        {
            var name = "cracks_" + page + "_" + (kind == MenuLayoutKind.Landscape ? "land" : "port");
            try
            {
                var ta = MenuV6Art.Data(name);
                if (ta == null) return null;
                if (Sets.TryGetValue(name, out var hit) && hit.ta == ta) return hit.set;
                var root = Json(name);
                if (root == null) return null;
                var set = CrackSet.From(root);
                Sets[name] = (ta, set);
                return set;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + name + ".json: " + ex.Message);
                return null;
            }
        }

        static (TextAsset ta, MistCels cels) _mist;

        public static MistCels Mist()
        {
            try
            {
                var ta = MenuV6Art.Data("mist");
                if (ta == null) return null;
                if (_mist.ta == ta && _mist.cels != null) return _mist.cels;
                var root = Json("mist");
                var c = root != null ? MistCels.From(root) : null;
                _mist = (ta, c);
                return c;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] mist.json: " + ex.Message);
                return null;
            }
        }
    }
}
