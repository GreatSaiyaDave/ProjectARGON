using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The v6.3 title (BUILD_PLAN §6 P2 step 3, D9; SPEC §v6.14.11): the approved boot from the Genesis cut into the live
    /// title. The Genesis plays as a pre-rendered VP8 clip (<see cref="GenesisVideo"/>, raw 0 → 21.6 s, local-only); from
    /// raw 21.6 everything is live UGUI rebuilt from Data/title_&lt;layout&gt;.json: Nut's night (sky, the twinkling star
    /// field, the six spinning galaxies, the ground), the carved Stone with its breathing emissive, the Item row, the Ring
    /// and the Puzzle (its glint, its lock rays settling), Nut closed above, her twinkle, the time-jump caption (lore l.49 +
    /// the l.64 sub-line), the Tears clawing open across the sky and the ground (<see cref="CrackLayer"/>) with their mist,
    /// TOUCH TO BEGIN breathing and the credit line. TAP TO SKIP (over the video only) is the page's 0.4 s dip to the
    /// settled title. Edit mode builds the settled idle statically (raw 24.9, the approved still). Every DOM string is
    /// fixed copy, drawn from the page's own sprites (title_dom.mjs, dsf 2).
    /// <para>The clock is the page's raw clock; every formula below runs on the base clock (title.js origR).</para>
    /// </summary>
    public sealed class TitleStage : MonoBehaviour
    {
        public const double GenEndRaw = 21.6, SkipBefore = 21.2, SettledRaw = 24.9;

        /// <summary>Tests: build the legacy boot as if the v6 title were not there.</summary>
        public static bool ForceLegacy;

        public sealed class Options
        {
            /// <summary>Play the Genesis clip first (play mode, when the local clip exists).</summary>
            public bool Video;

            /// <summary>Where the live title starts (21.6 = its first frame; edit mode 24.9 = the settled idle).</summary>
            public double StartRaw = GenEndRaw;

            /// <summary>The player touched (raw ≥ 21.2): the boot decides the route.</summary>
            public Action OnTouch;
        }

        public RectTransform Root { get; private set; }
        public RectTransform Stage { get; private set; }
        public MenuLayoutKind Kind { get; private set; }
        public readonly List<string> Missing = new();
        public GenesisVideo Video { get; private set; }
        public CrackLayer Tears { get; private set; }

        /// <summary>Seconds of the page's raw clock.</summary>
        public double Raw { get; private set; }

        public bool IsLive => _live;
        public bool HasLeft => !double.IsNaN(_leaving);

        Dictionary<string, object> _json;
        Rect _stageRect;
        Options _o;
        bool _live;
        double _liveRaw0;
        float _liveT0;
        double _leaving = double.NaN, _skipAt = double.NaN;
        float _skipWall = float.NaN;

        /// <summary>TAP TO SKIP's 0.4 s dip (title.js:157 / :328): 0 → 1 over 0.4 s of real time from the tap.</summary>
        double SkipK() => float.IsNaN(_skipWall) ? 0 : MenuV6Math.Seg(Time.unscaledTime, _skipWall, _skipWall + .4);
        bool _frozen;
        double _frozenRaw;
        CanvasGroup _stageGroup;
        Image _dip;
        TitleCrackDriver _tearsDriver;
        readonly List<Action<double>> _tick = new();
        Graphic _cap, _touch, _copy, _skip, _ph5Art, _ph5Gen;
        AudioSource _sfx;
        AudioClip _lock;
        double _lastCueRaw = double.NaN;
        bool _touched;

        // ------------------------------------------------------------------ gates

        public static string FileFor(MenuLayoutKind kind) => kind == MenuLayoutKind.Landscape ? "title_land" : "title_port";

        /// <summary>Null when the v6 title can be built for <paramref name="kind"/>; else why not (for the log).</summary>
        public static string WhyNot(MenuLayoutKind kind)
        {
            if (ForceLegacy) return "legacy forced (test)";
            if (MenuV6Stage.KillSwitchOn) return "kill switch " + MenuV6Stage.KillSwitchPref + " = 1";
            var j = Json(kind);
            if (j == null) return "v6 title data missing (Resources/" + MenuV6Art.DataRoot + FileFor(kind) + ")";
            var lay = kind == MenuLayoutKind.Landscape ? "Land" : "Port";
            if (MenuV6Art.Sprite("WRLDZ/MenuV6/Title/" + lay + "/sky") == null) return "v6 title art missing";
            return null;
        }

        static Dictionary<string, object> Json(MenuLayoutKind kind)
        {
            try { return CrackData.Json(FileFor(kind)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] " + FileFor(kind) + ".json: " + ex.Message);
                return null;
            }
        }

        /// <summary>The local Genesis clip for this layout (null on a clone / ForceNoLocal: the title then starts live).</summary>
        public static UnityEngine.Video.VideoClip GenesisClip(MenuLayoutKind kind)
        {
            try
            {
                var g = CrackData.Json("genesis");
                var c = RJ.D(RJ.D(g, "clips"), kind == MenuLayoutKind.Landscape ? "land" : "port");
                var f = RJ.S(c, "file");
                return string.IsNullOrEmpty(f) ? null : MenuV6Art.LocalAsset<UnityEngine.Video.VideoClip>(f);
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ build

        /// <summary>Build the title into <paramref name="host"/> (stretched). Throws on a real error (the boot falls back to
        /// its legacy steps).</summary>
        public static TitleStage Build(RectTransform host, MenuLayoutKind kind, Options o)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var json = Json(kind) ?? throw new InvalidOperationException("no " + FileFor(kind) + ".json");
            MenuV6Art.ClearCache();
            var go = new GameObject("TitleV6", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var root = go.GetComponent<RectTransform>();
            UiTheme.Stretch(root);
            var ts = go.AddComponent<TitleStage>();
            ts.Root = root;
            ts.Kind = kind;
            ts._json = json;
            ts._o = o ?? new Options();
            ts.BuildInto();
            return ts;
        }

        void BuildInto()
        {
            var st = RJ.D(_json, "stage");
            var W = (float)RJ.N(st, "W", Kind == MenuLayoutKind.Landscape ? 1920 : 1080);
            var H = (float)RJ.N(st, "H", Kind == MenuLayoutKind.Landscape ? 1080 : 2340);
            _stageRect = new Rect(0, 0, W, H);

            // the night past the stage's edges (title.js:162 #020104), and the tap catcher under everything else's chrome
            var back = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(Root, false);
            UiTheme.Stretch(back.GetComponent<RectTransform>());
            var bi = back.GetComponent<Image>();
            bi.sprite = UiFoundation.WhiteSprite();
            bi.color = new Color32(2, 1, 4, 255);
            bi.raycastTarget = true;
            back.AddComponent<TitleTap>().Owner = this;

            var sg = new GameObject("Stage", typeof(RectTransform), typeof(CanvasGroup));
            sg.transform.SetParent(Root, false);
            Stage = sg.GetComponent<RectTransform>();
            Stage.anchorMin = Stage.anchorMax = new Vector2(.5f, .5f);
            Stage.pivot = new Vector2(.5f, .5f);
            Stage.sizeDelta = new Vector2(W, H);
            _stageGroup = sg.GetComponent<CanvasGroup>();
            _stageGroup.blocksRaycasts = false;
            _stageGroup.interactable = false;
            Fit();

            var gfx = new UguiGfx();
            var lay = Kind == MenuLayoutKind.Landscape ? "Land" : "Port";
            var layers = RJ.L(_json, "layers") ?? new List<object>();
            foreach (var lo in layers)
            {
                if (lo is not Dictionary<string, object> L) continue;
                var name = RJ.S(L, "name") ?? "?";
                try
                {
                    if (RJ.Has(L, "rig")) BuildRig(RJ.S(L, "rig"), gfx, lay);
                    else if (RJ.Has(L, "book")) BuildBook(RJ.S(L, "book"), gfx);
                    else if (RJ.Has(L, "cracks")) BuildTears();
                    else StaticLayer(L, name);
                }
                catch (Exception ex)
                {
                    Missing.Add(name);
                    Debug.LogWarning("[WRLDZ MenuV6] title layer " + name + ": " + ex.Message);
                }
            }

            BuildDom(lay);

            // the Genesis clip over the stage (play mode only: a RenderTexture, R20), its live chrome above it
            if (_o.Video && Application.isPlaying)
            {
                var clip = GenesisClip(Kind);
                if (clip != null) Video = GenesisVideo.Create(Root, clip, new Vector2(W, H));
                Fit();
            }

            BuildOverVideo(lay);
            _dip = MakeImage(Root, "SkipDip", null, new Rect(0, 0, 0, 0), true);
            _dip.color = new Color32(2, 1, 4, 0);
            _dip.raycastTarget = false;

            // the lock click (Tag Force, local-only; ui_confirm otherwise: D7) and the eye on its own SFX source
            _lock = Cue("sfx_puzzle_lock_click", out _);
            if (Application.isPlaying)
            {
                _sfx = gameObject.AddComponent<AudioSource>();
                _sfx.playOnAwake = false;
                _sfx.spatialBlend = 0f;
                _sfx.volume = .45f;   // = WrldzAudio's SFX source
            }

            if (Video != null)
            {
                Raw = 0;
                _live = false;
                _stageGroup.alpha = 0f;   // the clip covers it; nothing under it is drawn twice
            }
            else GoLive(_o.StartRaw);

            Render();
        }

        AudioClip Cue(string sfx, out double vol)
        {
            vol = .5;
            try
            {
                var g = CrackData.Json("genesis");
                foreach (var c in RJ.L(g, "cues") ?? new List<object>())
                    if (RJ.S(c, "sfx") == sfx)
                    {
                        vol = RJ.N(c, "volume", .5);
                        var loc = RJ.S(c, "local");
                        return string.IsNullOrEmpty(loc) ? null : MenuV6Art.LocalAsset<AudioClip>(loc);
                    }
            }
            catch
            {
                // optional
            }

            return null;
        }

        /// <summary>The page's base clock (title.js origR).</summary>
        public static double Base(double raw) => TitleCrackDriver.Base(raw);

        Image MakeImage(Transform parent, string name, Sprite sp, Rect r, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp != null ? sp : UiFoundation.WhiteSprite();
            img.raycastTarget = false;
            if (stretch) UiTheme.Stretch(img.rectTransform);
            else MenuV6Layout.Place(img.rectTransform, r, _stageRect);
            return img;
        }

        /// <summary>A sprite's stage size: its pixels × 100 / ppu (the exporter pads to ×4 with transparent pixels).</summary>
        static Vector2 StageSize(Sprite s) =>
            s == null ? Vector2.zero : new Vector2(s.rect.width * 100f / s.pixelsPerUnit, s.rect.height * 100f / s.pixelsPerUnit);

        static Sprite SpriteOf(Dictionary<string, object> L)
        {
            var lo = RJ.D(L, "localOr");
            if (lo != null) return MenuV6Art.LocalOr(RJ.S(lo, "local"), RJ.S(lo, "fallback"));
            return MenuV6Art.Sprite(RJ.S(L, "file"));
        }

        // the static layers' Linear-space corrections (MenuV6Linear: the page blends sRGB values; UGUI here blends linear
        // light), fitted on the page's own solo layers at raw 24.9 against v63_title_18 / v63_portrait_05
        // (unity_build/p2_code/title_fit.py, both layouts): the Stone's emissive .4 (= the menu's fit_static.py), the Item
        // row's gold glow .3 (item row mean 1.10 vs 3.79 levels uncorrected), the stars alpha^1.8, the galaxies .29^2.4.
        const float StarsExp = 1.8f, GalaxyExp = 2.4f;

        static float Gain(string name) => name switch
        {
            "itemsAdd" => .3f,
            _ => MenuV6Linear.LayerGain(name)
        };

        void StaticLayer(Dictionary<string, object> L, string name)
        {
            if (name == "stars_mean" && BuildStars()) return;   // the live twinkle replaces its mean layer
            var sp = SpriteOf(L);
            if (sp == null)
            {
                Missing.Add(name);
                return;
            }

            var rect = RJ.R(L, "rect");
            var lo = RJ.D(L, "localOr");
            if (lo != null && !MenuV6Art.HasLocal(RJ.S(lo, "local")) && RJ.Has(lo, "fallbackRect")) rect = RJ.R(lo, "fallbackRect");
            var sz = StageSize(sp);
            var img = MakeImage(Stage, name, sp, new Rect(rect.x, rect.y, sz.x, sz.y));
            var gain = Gain(name);
            var a = RJ.N(L, "alpha", 1);
            img.color = new Color(1f, 1f, 1f, Mathf.Clamp01((float)a * gain));
            if (name == "stone_emiBlur")
                _tick.Add(tb => SetA(img, .5 * (.55 + .25 * Math.Sin(.9 * tb)) * gain));   // anime_v61_title.js:148 br
        }

        static void SetA(Graphic g, double a)
        {
            if (g == null) return;
            var c = g.color;
            var na = Mathf.Clamp01((float)a);
            if (Mathf.Abs(c.a - na) < 1e-4f) return;
            c.a = na;
            g.color = c;
        }

        RectTransform Host(string name)
        {
            var h = new GameObject("Rig_" + name, typeof(RectTransform)).GetComponent<RectTransform>();
            h.SetParent(Stage, false);
            UiTheme.Stretch(h);
            return h;
        }

        // ------------------------------------------------------------------ rigs

        void BuildRig(string rig, IRigGfx gfx, string lay)
        {
            var R = RJ.D(_json, "rigs");
            switch (rig)
            {
                case "galaxies": BuildGalaxies(RJ.D(R, "galaxies"), gfx); break;
                case "ring": BuildRing(RJ.D(R, "ring"), gfx); break;
                case "puzzle": BuildPuzzle(RJ.D(R, "puzzle"), gfx); break;
                case "nut": BuildNut(RJ.D(R, "nut"), gfx); break;
                default: Missing.Add("rig " + rig); break;
            }
        }

        /// <summary>title.js:173-176: the stopped star wheel at the idle, each star a fillRect twinkling .8 + .2 sin(2t + tw).</summary>
        bool BuildStars()
        {
            var s = RJ.D(RJ.D(_json, "rigs"), "stars");
            var rows = RJ.L(s, "rows");
            if (rows == null || rows.Count == 0) return false;
            var cols = RJ.L(s, "cols");
            int ci(string c)
            {
                for (var i = 0; i < (cols?.Count ?? 0); i++)
                    if (cols[i] as string == c)
                        return i;
                return -1;
            }

            int ix = ci("x"), iy = ci("y"), isz = ci("size"), ib = ci("b"), iw = ci("warm"), itw = ci("tw");
            var warm = RJ.Css(RJ.S(RJ.D(s, "colours"), "warm"), new Color32(0xff, 0xe2, 0xb0, 255));
            var cool = RJ.Css(RJ.S(RJ.D(s, "colours"), "cool"), new Color32(0xee, 0xf2, 0xff, 255));
            var list = new List<(double x, double y, double sz, double b, bool w, double tw)>();
            foreach (var r in rows)
                if (r is List<object> a)
                    list.Add((RJ.At(a, ix), RJ.At(a, iy), RJ.At(a, isz), RJ.At(a, ib), RJ.At(a, iw) != 0, RJ.At(a, itw)));
            var host = Host("stars");
            var shapes = RigShapes.Create(host, "Stars", _stageRect).Layer;
            _tick.Add(tb =>
            {
                shapes.Clear();
                foreach (var st in list)
                {
                    // b (1 - .55 .45) (.8 + .2 sin(2 t + tw)) (1 - .6): night .55, inside 1 at the idle (title.js:174)
                    var al = st.b * (1 - .55 * .45) * (.8 + .2 * Math.Sin(2 * tb + st.tw)) * (1 - .6);
                    var c = st.w ? warm : cool;
                    c.a = MenuV6Linear.Alpha(MenuV6MathF.Clamp01(al), StarsExp);   // additive points on the black sky
                    shapes.RectAA(st.x - st.sz / 2, st.y - st.sz / 2, st.sz, st.sz, c);
                }

                shapes.Commit();
            });
            return true;
        }

        /// <summary>title.js:178-179: six galaxies spinning (t sp + i), squashed .55, additive at alpha .29, and their glow.</summary>
        void BuildGalaxies(Dictionary<string, object> g, IRigGfx gfx)
        {
            var items = RJ.L(g, "items");
            if (items == null) return;
            var host = Host("galaxies");
            foreach (var io in items)
            {
                var spr = gfx.Sprite(RJ.S(io, "sprite"));
                var glow = gfx.Sprite(RJ.S(io, "glow"));
                if (spr == null) continue;
                double x = RJ.N(io, "x"), y = RJ.N(io, "y"), sp = RJ.N(io, "sp"), half = RJ.N(io, "half");
                var i = (int)RJ.N(io, "i");
                var q = gfx.Quads(host, "Galaxy_" + i, _stageRect, spr);
                var qg = glow != null ? gfx.Quads(host, "GalaxyGlow_" + i, _stageRect, glow) : null;
                var gw = glow != null ? glow.W / 2.0 : 0;   // ppu 200: the glow's own diameter in stage px
                const double al = .29;                      // (1 - .55 .5)(1 - .6) at the idle
                var ca = new Color(1f, 1f, 1f, MenuV6Linear.Alpha(al, GalaxyExp));
                _tick.Add(tb =>
                {
                    q.Clear();
                    var a = tb * sp + i;
                    double c = Math.Cos(a), s = Math.Sin(a);
                    // rotated by spin about the centre, then squashed (y × .55)
                    void P(double lx, double ly, out double px, out double py)
                    {
                        px = x + lx * c - ly * s;
                        py = y + (lx * s + ly * c) * .55;
                    }

                    P(-half, -half, out var x0, out var y0);
                    P(half, -half, out var x1, out var y1);
                    P(half, half, out var x2, out var y2);
                    P(-half, half, out var x3, out var y3);
                    q.Corners(x0, y0, x1, y1, x2, y2, x3, y3, QuadList.Full, ca);
                    q.Commit();
                    if (qg != null)
                    {
                        qg.Clear();
                        qg.Sprite(x, y, gw, gw, QuadList.Full, ca);
                        qg.Commit();
                    }
                });
            }
        }

        /// <summary>title.js:457 drawRingSpectral at .34 k in the Item row (relics.js:333-374, the P1b RingDraw).</summary>
        void BuildRing(Dictionary<string, object> r, IRigGfx gfx)
        {
            var parts = RJ.D(r, "parts");
            if (parts == null) return;
            // the title's part names → RingDraw's (rimArc → rim_arc)
            var p2 = new Dictionary<string, object>(parts);
            if (parts.TryGetValue("rimArc", out var ra)) p2["rim_arc"] = ra;
            var host = Host("ring");
            var d = RingDraw.From(gfx, host, _stageRect, p2, "TitleRing");
            if (d == null)
            {
                Missing.Add("ring");
                return;
            }

            d.SetGamma(MenuV6Linear.RingBlue);
            d.TipGain = d.RimGain = RingDraw.EchoBright;
            var c = RJ.V(r, "centre");
            d.X = c.x;
            d.Y = c.y;
            d.S = RJ.N(r, "s", .34);
            d.R = RJ.N(r, "R", 48 * d.S);
            d.r = RJ.N(r, "r", 38 * d.S);
            var piv = RJ.L(r, "piv");
            double[] rot = new double[5], px = new double[5], py = new double[5], glow = new double[5];
            for (var i = 0; i < 5 && piv != null && i < piv.Count; i++)
            {
                px[i] = RJ.At(piv[i] as List<object>, 0);
                py[i] = RJ.At(piv[i] as List<object>, 1);
            }

            var seed = RJ.N(RJ.D(r, "state"), "seed", 2.2);
            var I = RJ.N(RJ.D(r, "state"), "i", .8);
            _tick.Add(tb =>
            {
                for (var j = 0; j < 5; j++) rot[j] = .06 * Math.Sin(tb * 2 + j);
                d.Draw(tb, new RingDraw.State
                {
                    A = 1, I = I, Seed = seed, Sweep = 0, Aura = 0, Found = 0, Motes = 10, Rot = rot, PivX = px, PivY = py, Glow = glow
                });
            });
        }

        /// <summary>title.js:468-469: the Puzzle in its slot (shadow, A, emiB, the eye glow, the glint every 5.5 s, the iris).</summary>
        void BuildPuzzle(Dictionary<string, object> pz, IRigGfx gfx)
        {
            var parts = RJ.D(pz, "parts");
            var a = gfx.Sprite(RJ.S(RJ.D(parts, "A"), "file"));
            if (a == null)
            {
                Missing.Add("puzzle");
                return;
            }

            var host = Host("puzzle");
            var sh = gfx.Sprite(RJ.S(RJ.D(parts, "shadow"), "file"));
            QuadLayer shadow = null;
            var shRect = RJ.R(pz, "shadowRect");
            if (sh != null) shadow = gfx.Quads(host, "Puzzle_shadow", _stageRect, sh);
            var o = RJ.V(pz, "origin");
            var wh = RJ.V(pz, "drawWH");
            var p = new PuzzleDraw
            {
                Body = new Rect(o.x, o.y, wh.x, wh.y), EyeC = RJ.V(pz, "eyeC"), Iris = RJ.V(pz, "iris"), Wpx = RJ.N(pz, "wpx", 70)
            };
            p.A = gfx.Quads(host, "Puzzle_A", _stageRect, a);
            p.UvA = a.ContentOf(RJ.D(parts, "A"));
            var e = gfx.Sprite(RJ.S(RJ.D(parts, "emiB"), "file"));
            if (e != null)
            {
                p.Emi = gfx.Quads(host, "Puzzle_emiB", _stageRect, e);
                p.Emi.Add = true;
                p.UvE = e.ContentOf(RJ.D(parts, "emiB"));
            }

            p.Eye = gfx.Quads(host, "Puzzle_eyeGlow", _stageRect, gfx.Glow(MenuV6Linear.Glow));
            var book = RJ.D(RJ.D(_json, "books"), "puzzle_glint");
            var sheet = book != null ? gfx.Sheet(RJ.S(book, "file")) : null;
            if (sheet != null)
            {
                p.Glint = gfx.Quads(host, "Puzzle_glint", _stageRect, sheet);
                p.Glint.Add = true;
                p.GlintRect = RJ.R(book, "rect");
                p.GlintUv = BookCells(sheet, book);
            }

            p.Lights = gfx.Quads(host, "Puzzle_lights", _stageRect, gfx.Glow(MenuV6Linear.Glow));
            _tick.Add(tb =>
            {
                if (shadow != null)
                {
                    shadow.Clear();
                    shadow.Rect(shRect, QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Shade(.5)));
                    shadow.Commit();
                }

                // eye = lerp(1.5, idle, seg(t, 19, 19.6)), idle = .7 + .15 sin(1.1 t); glint ((t - 19.2) mod 5.5) / 1.2; iris
                var idle = .7 + .15 * Math.Sin(1.1 * tb);
                var eye = MenuV6Math.Lerp(1.5, idle, MenuV6Math.Seg(tb, 19.0, 19.6));
                var gl = tb > 19.2 ? MenuV6RigMath.Mod(tb - 19.2, 5.5) / 1.2 : -1;
                if (gl >= 1) gl = -1;
                var ip = MenuV6RigMath.Mod(tb, 4.3) / .5;
                p.Draw(tb, double.NaN, eye, gl, -1, ip < 1 ? Math.Sin(ip * Math.PI) : 0);
            });
        }

        static List<Rect> BookCells(RigTex sheet, Dictionary<string, object> book)
        {
            var list = new List<Rect>();
            var cell = RJ.L(book, "cell");
            double cw = RJ.At(cell, 0), ch = RJ.At(cell, 1);
            foreach (var c in RJ.L(book, "cells") ?? new List<object>())
                if (c is List<object> xy)
                    list.Add(QuadList.Cell(sheet.W, sheet.H, RJ.At(xy, 0), RJ.At(xy, 1), cw, ch));
            return list;
        }

        /// <summary>core.js:476-490 drawNutTwinkle on the title's Nut (the P1b NutRig, base t).</summary>
        void BuildNut(Dictionary<string, object> n, IRigGfx gfx)
        {
            if (n == null) return;
            var spec = new Dictionary<string, object>(n);
            if (n.TryGetValue("twinkleStars", out var ts)) spec["stars"] = ts;
            var ctx = new RigContext
            {
                Kind = Kind, K = RJ.N(RJ.D(_json, "stage"), "k", 1), Gfx = gfx,
                Rigs = new Dictionary<string, object> { { "nut", spec } }
            };
            var host = Host("nut");
            var rig = new NutRig();
            if (!rig.Build(ctx, host, _stageRect))
            {
                Missing.Add("nut twinkle");
                return;
            }

            _tick.Add(tb => rig.Tick(tb));
        }

        /// <summary>The Puzzle's lock settling (raw 21.6 → 22.2: rays + embers, 36 frames at 60 Hz).</summary>
        void BuildBook(string book, IRigGfx gfx)
        {
            var b = RJ.D(RJ.D(_json, "books"), book == "settle" ? "puzzle_settle" : book);
            var sheet = b != null ? gfx.Sheet(RJ.S(b, "file")) : null;
            if (sheet == null)
            {
                Missing.Add("book " + book);
                return;
            }

            var host = Host(book);
            var q = gfx.Quads(host, "Book_" + book, _stageRect, sheet);
            q.Add = true;
            var cells = BookCells(sheet, b);
            var rect = RJ.R(b, "rect");
            _tickRaw.Add(raw =>
            {
                q.Clear();
                var j = (int)Math.Floor((raw - GenEndRaw) * 60);
                if (j >= 0 && j < cells.Count) q.Rect(rect, cells[j], Color.white);
                q.Commit();
            });
        }

        readonly List<Action<double>> _tickRaw = new();

        void BuildTears()
        {
            var set = CrackData.Load("title", Kind);
            if (set == null || set.Nets.Count == 0)
            {
                Missing.Add("tears");
                return;
            }

            _tearsDriver = new TitleCrackDriver(set.Nets);
            Tears = CrackLayer.Create(Stage, "TearsU", _stageRect, set, set.Nets, _tearsDriver, null, CrackData.Mist());
        }

        // ------------------------------------------------------------------ DOM chrome (the page's own sprites)

        Graphic Dom(string id, string lay, Transform parent)
        {
            var el = RJ.D(RJ.D(RJ.D(_json, "dom"), "elements"), id);
            var file = RJ.S(el, "full");
            var sp = string.IsNullOrEmpty(file) ? null : MenuV6Art.Sprite(file);
            if (sp == null)
            {
                Missing.Add("dom " + id);
                return null;
            }

            var clip = RJ.R(el, "clip");
            var sz = StageSize(sp);
            var img = MakeImage(parent, "Dom_" + id, sp, new Rect(clip.x, clip.y, sz.x, sz.y));
            img.color = new Color(1, 1, 1, 0);
            return img;
        }

        RectTransform _over;

        void BuildDom(string lay)
        {
            _cap = Dom("cap", lay, Stage);
            _touch = Dom("touch", lay, Stage);
            _copy = Dom("copy", lay, Stage);
        }

        void BuildOverVideo(string lay)
        {
            // the chrome over the video (D9: TAP TO SKIP and both placeholder strips are live, not in the clip)
            var o = new GameObject("OverVideo", typeof(RectTransform)).GetComponent<RectTransform>();
            o.SetParent(Root, false);
            o.anchorMin = o.anchorMax = new Vector2(.5f, .5f);
            o.pivot = new Vector2(.5f, .5f);
            o.sizeDelta = Stage.sizeDelta;
            o.localScale = Stage.localScale;
            _over = o;
            _skip = Dom("skip", lay, o);
            _ph5Gen = Dom("ph5_genesis", lay, o);
            _ph5Art = Dom("ph5_art", lay, o);
        }

        // ------------------------------------------------------------------ clock

        void GoLive(double raw)
        {
            _live = true;
            _liveRaw0 = raw;
            _liveT0 = Time.unscaledTime;
            if (_stageGroup != null) _stageGroup.alpha = 1f;
            if (Video != null)
            {
                Video.Stop();
                Destroy(Video.gameObject);
                Video = null;
            }

            Raw = raw;
        }

        /// <summary>Captures and tests: hold the raw clock (the page's #freeze&amp;t=). Without a clip the title is live.</summary>
        public void Freeze(double raw)
        {
            _frozen = true;
            _frozenRaw = raw;
            if (!_live && raw >= GenEndRaw) GoLive(raw);
            Raw = raw;
            Render();
        }

        public void Unfreeze()
        {
            if (!_frozen) return;
            _frozen = false;
            if (_live)
            {
                _liveRaw0 = _frozenRaw;
                _liveT0 = Time.unscaledTime;
            }
        }

        /// <summary>The player touched TOUCH TO BEGIN: the touch / credit lines fade over .35 s and the Tears fade under the
        /// rising room (anime_v61_cracks.js:777). <paramref name="raw"/> defaults to now.</summary>
        public void Leave(double raw = double.NaN)
        {
            if (HasLeft) return;
            _leaving = double.IsNaN(raw) ? Raw : raw;
            if (_tearsDriver != null) _tearsDriver.Leaving = _leaving;
        }

        /// <summary>The leave's clock starts NOW (D8: the menu's first frame), so a load freeze never skips the fades.</summary>
        public void RestartLeaveClock()
        {
            if (!HasLeft) return;
            _leaving = Raw;
            if (_tearsDriver != null) _tearsDriver.Leaving = _leaving;
        }

        internal void Tap()
        {
            if (HasLeft || _touched) return;
            var raw = Raw;
            if (raw < SkipBefore)
            {
                // title.js:150-159: a 0.4 s crossfade to the settled title, on the wall clock as the page's (so a stalled or
                // still-preparing clip never holds the skip)
                if (double.IsNaN(_skipAt))
                {
                    _skipAt = raw;
                    _skipWall = Time.unscaledTime;
                }
                return;
            }

            if (!_live) GoLive(Math.Max(raw, GenEndRaw));
            _touched = true;
            _o.OnTouch?.Invoke();
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (_frozen)
            {
                Raw = _frozenRaw;
            }
            else if (!_live)
            {
                // the video phase: raw = the clip's own clock; it ends on frame 648 = raw 21.6 (genesis.json)
                if (Video == null || Video.Failed)
                {
                    GoLive(GenEndRaw);
                }
                else
                {
                    Raw = Video.Raw;
                    if (!double.IsNaN(_skipAt))
                    {
                        var k = SkipK();
                        if (k >= 1) GoLive(GenEndRaw);
                    }
                    else if (Video.Ended || Raw >= GenEndRaw - 1e-3) GoLive(GenEndRaw);
                }
            }
            else
            {
                // D8: the frame after the menu's build frame resumes from the build frame's raw (Time.unscaledTime is a
                // frame's start, so the build's freeze would otherwise jump the leave fades); MenuEntry.TakenFrame
                var taken = MenuEntry.TakenFrame;
                if (HasLeft && taken >= 0 && taken != _heldFor && Time.frameCount > taken)
                {
                    _heldFor = taken;
                    _liveRaw0 = Raw;
                    _liveT0 = Time.unscaledTime;
                }

                Raw = _liveRaw0 + (Time.unscaledTime - _liveT0);
            }

            Render();
        }

        int _heldFor = -1;

        void LateUpdate()
        {
            if (Stage != null && Root != null && Root.rect.size != _lastSize) Fit();
        }

        Vector2 _lastSize;

        void Fit()
        {
            if (Stage == null || Root == null) return;
            _lastSize = Root.rect.size;
            var k = MenuAspect.StageScale(Root, Stage.sizeDelta);
            Stage.localScale = new Vector3(k, k, 1f);
            if (_over != null) _over.localScale = Stage.localScale;
            if (Video != null) Video.Fit(k);
        }

        /// <summary>Draw everything for the current raw time (edit mode: once, from the builder).</summary>
        public void Render()
        {
            var raw = Raw;
            var tb = Base(raw);
            // cues: the lock click at raw 21.32 and the Millennium Eye at 21.38 (title.js:490), once each, as raw crosses them
            if (Application.isPlaying && !double.IsNaN(_lastCueRaw))
            {
                if (_lastCueRaw < 21.32 && raw >= 21.32) PlayLock();
                if (_lastCueRaw < 21.38 && raw >= 21.38) WRLDZ.Presentation.WrldzAudio.PlayMillenniumEye();
            }

            _lastCueRaw = raw;

            if (_live)
            {
                foreach (var t in _tick) t(tb);
                foreach (var t in _tickRaw) t(raw);
                if (Tears != null) Tears.Render(raw);
            }

            // the chrome (anime_v61_title.js:182-190, title.js:482-488; schedules in title_<layout>.json)
            TitleMath.Chrome(raw, _leaving, out var cap, out var touch, out var copy, out var skip, out var art, out var gen);
            SetA(_cap, _live ? cap : 0);
            SetA(_touch, _live ? touch : 0);
            SetA(_copy, _live ? copy : 0);
            SetA(_skip, skip);
            SetA(_ph5Art, art);
            SetA(_ph5Gen, gen);
            if (_dip != null)
            {
                var k = SkipK();
                SetA(_dip, k > 0 && k < 1 ? Math.Sin(k * Math.PI) * .9 : 0);
            }
        }

        void PlayLock()
        {
            if (_lock != null && _sfx != null) _sfx.PlayOneShot(_lock, .55f);
            else WRLDZ.Presentation.WrldzAudio.PlayConfirm();   // D7 fallback: ui_confirm
        }

        /// <summary>The full-screen tap: TAP TO SKIP before raw 21.2, TOUCH after.</summary>
        sealed class TitleTap : MonoBehaviour, IPointerClickHandler
        {
            public TitleStage Owner;

            public void OnPointerClick(PointerEventData e)
            {
                if (Owner != null) Owner.Tap();
            }
        }
    }
}
