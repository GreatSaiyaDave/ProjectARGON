using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The v6.3 sheet frame, the false door (BUILD_PLAN §6 P3 step 1, SPEC §10.2; sheets_&lt;layout&gt;.json "frames"). It is drawn
    /// whole and revealed by the Puzzle-lock clip: the lintel unfurls from the clasp both ways, the body unrolls beneath it,
    /// and on the close it rolls back up and folds into the clasp (menu.js:583-600; the page's own finding: v6.3 sheets do not
    /// draw their lines on, SPEC §8.5 / §8.6 are pre-v3.1). A RectMask2D's padding is the clip box; nothing dissolves.
    /// <list type="bullet">
    /// <item><b>Over the Stone</b> (<see cref="OverStone"/>, the recess / Ring / QUIT sheets): the exact full bake at its stage
    /// rect (portrait: the local bake with its tracked fallback); the owning sheet drives <see cref="Apply"/>.</item>
    /// <item><b>Any rect</b> (<see cref="AnyRect"/>, every DualMenuPresenter sheet over the map or the Stone): frame9 (Sliced) at
    /// F grown by 6 stage px, the cornice and star tiles (Tiled), the FM band where the lintel is ≥ 140 px (local), then
    /// frame9_l2 and frame9_l1 (Sliced) — the page's own composition rule; the fill takes the menu's opacity
    /// (MenuChromePrefs.PanelAlpha), the lintel and the lines stay opaque. It runs its own clock and the clasp's jewel.</item>
    /// </list>
    /// Null-safe: a missing part is skipped, a missing set still yields a clipped, empty frame (never a throw on the hot path).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FalseDoorFrame : MonoBehaviour
    {
        public SheetKind Kind { get; private set; }
        public MenuLayoutKind Layout { get; private set; }

        /// <summary>The frame's own stage rect (F) and lintel height (stage px).</summary>
        public Rect F { get; private set; }

        public float Lintel { get; private set; }

        /// <summary>The clipped node: the frame's parts, its fractures and the sheet's content.</summary>
        public RectTransform Body { get; private set; }

        /// <summary>The frame's parts (what FrameGhost copies).</summary>
        public RectTransform Door { get; private set; }

        /// <summary>Over the Stone: where the sheet's own content goes (under the clip, over the frame and its fractures).</summary>
        public RectTransform Content { get; private set; }

        public CrackLayer Cracks { get; private set; }
        public SheetMath.Reveal Reveal { get; private set; }

        RectMask2D _mask;
        Image _roll, _unfurlL, _unfurlR;
        RectTransform _seams;
        Rect _space;      // the stage rect the mask node covers (over the Stone: the whole stage)
        bool _anyRect;
        float _k = 1f;
        double _t0 = double.NaN, _t1 = double.NaN;
        Image _dim;
        float _dimBase;
        readonly List<CanvasGroup> _content = new();
        PuzzleLock _jewel;
        RectTransform _jewelHost;
        Vector2 _jewelPx;
        SheetCrackDriver _crackDrv;
        readonly List<(Image img, Rect stageRect)> _sliced = new();
        Vector2 _rootStageSize;
        RectTransform _over;

        /// <summary>Over the Stone: the owning sheet's clock (its u, real seconds since its open; StoneSheet binds it when it
        /// takes the frame). Such a frame is never <see cref="Open"/>ed, so without it the fractures read u = 1e3 and showed at
        /// full extent from the first frame instead of growing at sheet time .9-2.0 (anime_v61_cracks.js sheetCracks).</summary>
        public Func<double> Clock { get; set; }

        /// <summary>Real seconds since the open (the owning sheet's u over the Stone; 1e3 = at rest before any clock).</summary>
        public double U => Clock != null ? Clock() : double.IsNaN(_t0) ? 1e3 : MenuV6Clock.Now - _t0;

        public double V => double.IsNaN(_t1) ? -1 : Math.Max(0, MenuV6Clock.Now - _t1);

        // ================================================================== over the Stone

        /// <param name="space">a node covering the stage rect (SheetDraw.StageSpace: stage px 1:1)</param>
        /// <param name="kind">Recess / Ring / Quit (Duel builds its own parts through <see cref="Duel"/>)</param>
        /// <param name="crackKey">the cracks_sheets key of its fractures ("recessdeck", "ringseek", "quit"); null = none</param>
        public static FalseDoorFrame OverStone(RectTransform space, SheetKind kind, MenuLayoutKind lay, string crackKey)
        {
            var root = SheetData.Root(lay);
            var stage = SheetData.Stage(root, lay);
            var f = Build(space, kind, lay, new Rect(0, 0, stage.x, stage.y));
            var key = kind == SheetKind.Ring ? "ring" : kind == SheetKind.Quit ? "quit" : "recess";
            var spec = RJ.D(RJ.D(RJ.D(root, "frames"), "kinds"), key);
            f.F = SheetData.Frame(RJ.D(spec, "F"));
            f.Lintel = (float)RJ.N(spec, "lh", lay == MenuLayoutKind.Landscape ? 108 : 150);
            SheetDraw.Part(f.Door, "Frame_full", RJ.D(RJ.D(spec, "parts"), "full"), f._space);
            f.FinishStage(root, crackKey);
            return f;
        }

        /// <summary>The DUEL contract's frame: fill (+ the lintel's pin), the FM band (.lint, local; its tracked ink fallback),
        /// l1, l2 at their recorded rects (sheets_&lt;layout&gt;.json duel.parts).</summary>
        public static FalseDoorFrame Duel(RectTransform space, MenuLayoutKind lay)
        {
            var root = SheetData.Root(lay);
            var stage = SheetData.Stage(root, lay);
            var f = Build(space, SheetKind.Duel, lay, new Rect(0, 0, stage.x, stage.y));
            var cf = RJ.D(RJ.D(root, "geo"), "cf");
            f.F = SheetData.Frame(RJ.D(cf, "frame"));
            f.Lintel = (float)RJ.N(RJ.D(RJ.D(root, "geo"), "lintel"), "duel", lay == MenuLayoutKind.Landscape ? 150 : 214);
            var parts = RJ.D(RJ.D(root, "duel"), "parts");
            SheetDraw.Part(f.Door, "Fill", RJ.D(parts, "fill"), f._space);
            SheetDraw.Part(f.Door, "Lint", RJ.D(parts, "lint"), f._space);
            SheetDraw.Part(f.Door, "L2", RJ.D(parts, "l2"), f._space);
            SheetDraw.Part(f.Door, "L1", RJ.D(parts, "l1"), f._space);
            f.FinishStage(root, "duel");
            return f;
        }

        static FalseDoorFrame Build(RectTransform space, SheetKind kind, MenuLayoutKind lay, Rect stageRect)
        {
            var go = new GameObject("FalseDoor", typeof(RectTransform), typeof(RectMask2D), typeof(FalseDoorFrame));
            go.transform.SetParent(space, false);
            var rt = go.GetComponent<RectTransform>();
            UiTheme.Stretch(rt);
            var f = go.GetComponent<FalseDoorFrame>();
            f.Kind = kind;
            f.Layout = lay;
            f._space = stageRect;
            f._mask = go.GetComponent<RectMask2D>();
            f.Body = rt;
            f.Door = SheetDraw.Stretch(rt, "Door");
            return f;
        }

        void FinishStage(Dictionary<string, object> root, string crackKey)
        {
            Cracks = BuildCracks(Body, crackKey, _space);
            Content = SheetDraw.Stretch(Body, "Content");
            // the seams of light ride the courier canvas, above the sheet (they are never clipped)
            _seams = SheetDraw.Stretch(Body.parent, "Seams");
            BuildSeams(root);
            Apply(new SheetMath.Reveal { H = 0, B = 0 });
        }

        CrackLayer BuildCracks(RectTransform parent, string key, Rect frame)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                var set = CrackData.Load("sheets", Layout);
                var nets = SheetCrackDriver.Of(set, key);
                if (nets.Count == 0) return null;
                _crackDrv = new SheetCrackDriver(nets) { SheetTime = () => SheetMath.Sp(Kind) * (Application.isPlaying ? U : 3) };
                var L = CrackLayer.Create(parent, "Fractures", frame, set, nets, _crackDrv, () => MenuV6Clock.Now);
                if (L != null) L.Render(MenuV6Clock.Now);
                return L;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] sheet fractures " + key + " skipped: " + ex.Message);
                return null;
            }
        }

        void BuildSeams(Dictionary<string, object> root)
        {
            var fx = RJ.D(root, "fx");
            var roll = SheetData.Sprite(RJ.D(fx, "seam_roll"));
            var unf = SheetData.Sprite(RJ.D(fx, "seam_unfurl"));
            var host = _seams != null ? _seams : Body;
            if (roll != null) _roll = SheetDraw.Img(host, "Seam_roll", roll, new Rect(0, 0, 10, 10), _space);
            if (unf != null)
            {
                _unfurlL = SheetDraw.Img(host, "Seam_unfurlL", unf, new Rect(0, 0, 10, 10), _space);
                _unfurlR = SheetDraw.Img(host, "Seam_unfurlR", unf, new Rect(0, 0, 10, 10), _space);
            }
        }

        /// <summary>The Puzzle-lock clip and its seams for this frame's reveal (stage px; any rect: scaled by its k).</summary>
        public void Apply(SheetMath.Reveal r)
        {
            Reveal = r;
            var c = SheetMath.ClipBox(F.x, F.y, F.width, F.height, Lintel, r);
            if (_mask != null)
            {
                if (!_anyRect)
                {
                    // the mask node covers the whole stage: padding left / bottom / right / top, stage px (top = no clip)
                    var W = _space.width;
                    var H = _space.height;
                    _mask.padding = new Vector4((float)Math.Max(0, c.X0), (float)Math.Max(0, H - c.Y1), (float)Math.Max(0, W - c.X1), 0f);
                }
                else
                {
                    // the root is F grown by Margin stage px (k units): its local units are stage px × k
                    var x0 = (c.X0 - (F.x - Margin)) * _k;
                    var x1 = (F.xMax + Margin - c.X1) * _k;
                    var y1 = (F.yMax + Margin - c.Y1) * _k;
                    _mask.padding = new Vector4((float)Math.Max(0, x0), (float)Math.Max(0, y1), (float)Math.Max(0, x1), 0f);
                }
            }

            SheetMath.Seams(r, out var rollA, out var unfA);
            if (_roll != null)
            {
                _roll.enabled = rollA > .003;
                if (_roll.enabled)
                {
                    PlaceSeam(_roll.rectTransform, new Rect((float)c.X0, (float)(c.Y1 - 2 - 12), (float)(c.X1 - c.X0), 24f));
                    SheetDraw.SetA(_roll, MenuV6Linear.Alpha(rollA));
                }
            }

            foreach (var (img, x) in new[] { (_unfurlL, c.X0 + 2), (_unfurlR, c.X1 - 2) })
            {
                if (img == null) continue;
                img.enabled = unfA > .003;
                if (!img.enabled) continue;
                PlaceSeam(img.rectTransform, new Rect((float)(x - 12), F.y - 10 - 12, 24f, Lintel + 18 + 24));
                SheetDraw.SetA(img, MenuV6Linear.Alpha(unfA));
            }
        }

        void PlaceSeam(RectTransform rt, Rect stageRect)
        {
            if (!_anyRect)
            {
                MenuV6Layout.Place(rt, stageRect, _space);
                return;
            }

            // any rect: the seam node shares the root's rect (F grown by Margin, k units per stage px)
            var px = new Rect(F.x - Margin, F.y - Margin, F.width + 2 * Margin, F.height + 2 * Margin);
            MenuV6Layout.Place(rt, stageRect, px);
        }

        // ================================================================== any rect (DualMenuPresenter)

        /// <summary>The margin (stage px) the any-rect root grows F by, so the 6 px of line overhang is never clipped.</summary>
        public const float Margin = 8f;

        /// <summary>
        /// Paint the false door into a DualMenuPresenter root. <paramref name="root"/> is anchored by the caller; its rect is F
        /// grown by <see cref="Margin"/> stage px (k canvas units per stage px). <paramref name="fStage"/> is the stage-px rect
        /// F stands for (the host it matches: GetWindowAnchors Compact / Medium / Large of the stage, or the Stone's contract
        /// rect), <paramref name="crackKey"/> the cracks_sheets key built for that host.
        /// </summary>
        public static FalseDoorFrame AnyRect(RectTransform root, MenuLayoutKind lay, Rect fStage, float lintel, string crackKey,
            Image dim, float k)
        {
            if (root == null) return null;
            var json = SheetData.Root(lay);
            var mask = root.GetComponent<RectMask2D>();
            if (mask == null) mask = root.gameObject.AddComponent<RectMask2D>();
            var f = root.GetComponent<FalseDoorFrame>();
            if (f == null) f = root.gameObject.AddComponent<FalseDoorFrame>();
            f.Kind = SheetKind.Generic;
            f.Layout = lay;
            f._anyRect = true;
            f._mask = mask;
            f.Body = root;
            f.F = fStage;
            f.Lintel = lintel;
            f._k = k > 1e-3f ? k : 1f;
            f._rootStageSize = new Vector2(fStage.width + 2 * Margin, fStage.height + 2 * Margin);
            f._space = new Rect(fStage.x - Margin, fStage.y - Margin, fStage.width + 2 * Margin, fStage.height + 2 * Margin);
            f.Door = SheetDraw.Stretch(root, "Door");
            f.Door.SetAsFirstSibling();
            f._dim = dim;
            f._dimBase = dim != null ? dim.color.a : 0f;
            f.PaintAnyRect(json);
            f.Cracks = f.BuildCracks(f.Door, crackKey, f.CrackFrame(crackKey));
            // the seams and the clasp ride above the clip (the page draws them on its courier canvas): a sibling that follows
            // the root's rect, scale and visibility
            f._over = FollowRect.For(root, root.name + "_over");
            f._seams = f._over;
            f.BuildSeams(json);
            f.BuildJewel(json);
            if (Application.isPlaying) f.Open();
            else f.Apply(new SheetMath.Reveal { H = 1, B = 1 });
            return f;
        }

        /// <summary>The cracks' stage frame: the nets were built for their host's F; map that F onto this frame's F.</summary>
        Rect CrackFrame(string key)
        {
            var hostF = SheetCrackDriver.FrameOf(key, Layout);
            if (hostF.width <= 0 || hostF.height <= 0) return _space;
            // the Door node covers the root (= F ± Margin); the layer maps 'frame' onto it, so frame = the host's F ± the same
            // margin, scaled from this F's margin
            var sx = hostF.width / F.width;
            var sy = hostF.height / F.height;
            return new Rect(hostF.x - Margin * sx, hostF.y - Margin * sy, hostF.width + 2 * Margin * sx, hostF.height + 2 * Margin * sy);
        }

        void PaintAnyRect(Dictionary<string, object> json)
        {
            var frames = RJ.D(json, "frames");
            var nine = RJ.D(frames, "nine");
            var tiles = RJ.D(frames, "tiles");
            var common = RJ.D(json, "commonParts");
            var fill = MenuChromePrefsAlpha();
            // the whole frame at the fill's opacity, then its lintel band again at full (the lintel stays opaque)
            Slice(RJ.D(nine, "frame9"), "Frame9", Grow(F, 6), fill);
            var f9 = Door.Find("Frame9");
            _fill = f9 != null ? f9.GetComponent<Image>() : null;
            var lintClip = new GameObject("LintelOpaque", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            lintClip.SetParent(Door, false);
            PlaceAny(lintClip, new Rect(F.x - 6, F.y - 6, F.width + 12, Lintel + 6));
            var lintStage = new Rect(F.x - 6, F.y - 6, F.width + 12, Lintel + 6);
            Slice(RJ.D(nine, "frame9"), "Frame9_lintel", Grow(F, 6), 1f, lintClip, lintStage);
            Tile(RJ.D(tiles, "cornice"), "Cornice", new Rect(F.x + 52, F.y, F.width - 104, 18));
            if (Lintel < 140)
                Tile(RJ.D(tiles, "starsA"), "StarsA", new Rect(F.x + 26, F.y + 26, F.width - 52, 26));
            Tile(RJ.D(tiles, "starsB"), "StarsB", new Rect(F.x, F.y + 52, F.width, Lintel - 52));
            if (Lintel >= 140)
            {
                // the FM band (local; no fallback: the lapis shows, as the clean run) with its 1.5 px ink rule round it
                var band = RJ.D(common, "band") ?? RJ.D(tiles, "band");
                var bandRect = new Rect(F.x + 56, F.y + 23, F.width - 112, 34);
                var ink = new GameObject("BandInk", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                ink.transform.SetParent(Door, false);
                ink.sprite = UiFoundation.WhiteSprite();
                ink.color = Color.black;
                ink.raycastTarget = false;
                PlaceAny(ink.rectTransform, Grow(bandRect, 1.5f));
                ink.enabled = SheetData.IsLocal(band);
                Tile(band, "Band", bandRect);
            }

            Slice(RJ.D(nine, "frame9_l2"), "Frame9_l2", Grow(F, 6), 1f);
            Slice(RJ.D(nine, "frame9_l1"), "Frame9_l1", Grow(F, 6), 1f);
        }

        static float MenuChromePrefsAlpha()
        {
            try { return WRLDZ.UI.Shell.MenuChromePrefs.PanelAlpha; }
            catch { return .94f; }
        }

        static Rect Grow(Rect r, float d) => new(r.x - d, r.y - d, r.width + 2 * d, r.height + 2 * d);

        void PlaceAny(RectTransform rt, Rect stageRect) => MenuV6Layout.Place(rt, stageRect, _space);

        void Slice(object part, string name, Rect stageRect, float alpha, RectTransform parent = null, Rect parentStage = default)
        {
            var sp = SheetData.Sprite(part);
            if (sp == null) return;
            var p = parent != null ? parent : Door;
            var img = SheetDraw.Img(p, name, sp, stageRect, parent != null ? parentStage : _space, sliced: true);
            if (img == null) return;
            img.color = new Color(1f, 1f, 1f, alpha);
            SheetDraw.SliceAt(img, _k);
            _sliced.Add((img, stageRect));
        }

        void Tile(object part, string name, Rect stageRect)
        {
            if (part == null || stageRect.width <= 0 || stageRect.height <= 0) return;
            var sp = SheetData.Sprite(part);
            if (sp == null) return;
            var img = SheetDraw.Img(Door, name, sp, stageRect, _space);
            if (img == null) return;
            img.type = Image.Type.Tiled;
            SheetDraw.SliceAt(img, _k);
            _sliced.Add((img, stageRect));
        }

        void BuildJewel(Dictionary<string, object> json)
        {
            // the clasp at the lintel's top centre (F.y + 12), scale .5: a frame over the map has no keystone to come from,
            // so the jewel forms at the clasp with its lock flash (decision, recorded in PROGRESS_p3code)
            _jewelPx = new Vector2(F.x + F.width / 2, F.y + 12);
            try
            {
                _jewelHost = SheetDraw.Stretch(_over != null ? _over : Body, "Clasp");
                _jewel = PuzzleLock.Build(new UguiGfx(), _jewelHost, _space, Layout, null);
                if (_jewel == null)
                {
                    MenuEntrance.Kill(_jewelHost.gameObject);
                    _jewelHost = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] sheet clasp skipped: " + ex.Message);
                _jewel = null;
            }
        }

        /// <summary>The content the frame fades in with its envelope (the header, the body well, the footer).</summary>
        public void Track(GameObject content)
        {
            var g = SheetDraw.Group(content);
            if (g != null) _content.Add(g);
            if (!Application.isPlaying) return;
            SheetDraw.SetA(g, 0);
        }

        public void Open()
        {
            _t0 = MenuV6Clock.Now;
            _t1 = double.NaN;
            Tick();
        }

        /// <summary>Start the close (the owning sheet runs it; an any-rect frame is closed through <see cref="FrameGhost"/>).</summary>
        public void BeginClose()
        {
            if (double.IsNaN(_t1)) _t1 = MenuV6Clock.Now;
        }

        public bool Closing => !double.IsNaN(_t1);

        void Update()
        {
            if (!_anyRect || !Application.isPlaying || double.IsNaN(_t0)) return;
            Tick();
        }

        bool _settled;

        void Tick()
        {
            if (!_anyRect) return;
            double u = U, v = V;
            var r = SheetMath.RevealAt(SheetKind.Generic, u, v);
            Apply(r);
            var K = SheetMath.K(SheetKind.Generic, u, v);
            if (_dim != null)
            {
                var c = _dim.color;
                c.a = (float)(_dimBase * MenuV6Math.InOut(MenuV6Math.Clamp(u * SheetMath.SheetSp / .8, 0, 1)));
                _dim.color = c;
            }

            var ca = SheetMath.ContentA(SheetKind.Generic, u, v);
            foreach (var g in _content) SheetDraw.SetA(g, MenuV6Linear.Fade(ca));
            if (_jewel != null) _jewel.Jewel(_jewelPx, .5, u * SheetMath.SheetSp, MenuV6Clock.Now);
            if (Cracks != null) Cracks.Render(MenuV6Clock.Now);
            SyncWell();
            _settled = r.H >= 1 && r.B >= 1 && ca >= 1 && u * SheetMath.SheetSp > 2.1;
        }

        Image _fill, _well;

        /// <summary>SettingsScreen recolours a sheet's BodyWell when the player cycles Transparency (well.color =
        /// MenuChromePrefs.InsetColor, written for today's frame): in the false door the well only clips, so it is put back
        /// to clear and the door's own fill takes the new opacity instead (a navy slab over the door otherwise).</summary>
        void SyncWell()
        {
            if (_well == null)
            {
                var w = transform.Find("BodyWell");
                if (w == null) return;
                _well = w.GetComponent<Image>();
                if (_well == null) return;
            }

            if (_well.color.a <= 0f) return;
            _well.color = new Color(0f, 0f, 0f, 0f);
            if (_fill == null) return;
            var c = _fill.color;
            c.a = MenuChromePrefsAlpha();
            _fill.color = c;
        }

        void OnRectTransformDimensionsChange()
        {
            if (!_anyRect || _sliced.Count == 0) return;
            var nk = SheetDraw.CanvasK(transform);
            if (Mathf.Abs(nk - _k) < 1e-4f) return;
            _k = nk;
            foreach (var (img, _) in _sliced) SheetDraw.SliceAt(img, _k);
        }

        /// <summary>Tests / captures: show the frame at rest now (no animation).</summary>
        public void Settle()
        {
            _t0 = MenuV6Clock.Now - 10;
            _t1 = double.NaN;
            Apply(new SheetMath.Reveal { H = 1, B = 1 });
            foreach (var g in _content) SheetDraw.SetA(g, 1);
            if (_dim != null)
            {
                var c = _dim.color;
                c.a = _dimBase;
                _dim.color = c;
            }

            if (_jewel != null) _jewel.Jewel(_jewelPx, .5, 3, MenuV6Clock.Now);
        }

        public bool Settled => _settled;

        void OnEnable()
        {
            if (_over != null && !_over.gameObject.activeSelf) _over.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            // the root goes inactive at once on a close (D19): its unclipped seams and clasp go with it
            if (_over != null && _over.gameObject.activeSelf) _over.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_over != null) MenuEntrance.Kill(_over.gameObject);
        }
    }

    /// <summary>A sibling that keeps its source's rect, scale and visibility (the any-rect frame's unclipped seams and clasp),
    /// and goes when the source goes.</summary>
    [DisallowMultipleComponent]
    public sealed class FollowRect : MonoBehaviour
    {
        public RectTransform Source;

        public static RectTransform For(RectTransform source, string name)
        {
            if (source == null || source.parent == null) return null;
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(source.parent, false);
            rt.SetSiblingIndex(source.GetSiblingIndex() + 1);
            var f = rt.gameObject.AddComponent<FollowRect>();
            f.Source = source;
            f.Sync();
            return rt;
        }

        public void Sync()
        {
            if (Source == null) return;
            var rt = (RectTransform)transform;
            rt.anchorMin = Source.anchorMin;
            rt.anchorMax = Source.anchorMax;
            rt.pivot = Source.pivot;
            rt.offsetMin = Source.offsetMin;
            rt.offsetMax = Source.offsetMax;
            rt.localScale = Source.localScale;
            rt.localRotation = Source.localRotation;
        }

        void LateUpdate()
        {
            if (Source == null)
            {
                MenuEntrance.Kill(gameObject);
                return;
            }

            Sync();
        }
    }
}
