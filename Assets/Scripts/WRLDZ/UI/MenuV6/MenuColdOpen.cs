using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI.Eldritch;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>What the menu's entrance drives on a built Stone (MenuV6Stage fills it).</summary>
    internal sealed class StageParts
    {
        public RectTransform Canvas, Backdrop, Root, Stage, Room, CracksGroup, Fx, Slates, Tiles, Fx2, Ph5, SlateL, SlateR;
        public Dictionary<string, Image> Static;
        public Dictionary<string, RectTransform> Hosts;
        public Dictionary<string, GameObject> Flip;
        public RigContext Ctx;
        public ArcaneButton Hero, MapBack, Quit, DeckPlaque;
        public Rect HeroRect;
        public readonly Dictionary<string, ArcaneButton> Recess = new();
        public MenuV6Layout Layout;
        public MenuLayoutKind Kind;
        public List<EyeFlipbook> Eyes;
        public NutCrackDriver NutDriver;
    }

    /// <summary>
    /// The menu's entrance (BUILD_PLAN §6 P2 steps 5-6, SPEC §8.1-§8.2): the page's E table applied to the built Stone's
    /// own layers, rigs and controls through CanvasGroups (nothing is rebuilt), on the menu clock (MenuV6Clock).
    /// <list type="bullet">
    /// <item><b>Cold open</b> (D10: the first-time path's world load, a duel's return, the Desktop Lab hub): the room 0.05,
    /// the Stone 0.20, the chrome rows from 0.45, the door's rim 0.90, the recess flashes + the keystone's assembly
    /// 0.95-1.9 (its 8 pieces fly in and lock), DUEL 1.0, the labels 1.05, the bezels 1.15, the breach eyes shut until
    /// their reference open (schedules.json eye.reveal), the walls crack 0.25-2.0.</item>
    /// <item><b>Boot handoff</b> (D8, #enter): the menu canvas over the kept title canvas (alpha u / .35), the Puzzle's
    /// flare, the name giving way as gold dust, the room rising under a still Nut, the Puzzle dropping into the door and
    /// locking, the Items flying to their recesses, the carve from the keystone, the breach tearing open, the walls
    /// cracking 1.9-2.95 (<see cref="HandoffRig"/>).</item>
    /// </list>
    /// Removed when it is over (u 3.4); every value it touched ends at the rest state the P1b Stone shows.
    /// </summary>
    public sealed class MenuEntrance : MonoBehaviour
    {
        public const double EndCold = 3.0, EndEnter = 3.45;

        MenuV6Stage _stage;
        StageParts _p;
        bool _enter;
        double _end;
        readonly List<(CanvasGroup g, Func<double, EntranceE, double> a)> _alpha = new();
        readonly List<(RectTransform rt, Vector2 basePos, Func<double, EntranceE, double> dy)> _rise = new();
        CanvasGroup _rootGroup;
        Image _backdrop;
        Color _backdropRest;
        HandoffRig _handoff;
        KeystoneAssembly _assembly;
        bool _done;

        public bool IsEnter => _enter;

        internal static MenuEntrance Attach(MenuV6Stage stage, bool enter)
        {
            if (stage == null) return null;
            var e = stage.gameObject.AddComponent<MenuEntrance>();
            e._stage = stage;
            e._p = stage.Parts;
            e._enter = enter;
            e._end = enter ? EndEnter : EndCold;
            try
            {
                e.Setup();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] entrance skipped (the Stone shows at rest): " + ex.Message);
                e.Finish();
                return null;
            }

            e.Apply(MenuV6Clock.Now);
            return e;
        }

        CanvasGroup G(GameObject go)
        {
            if (go == null) return null;
            var g = go.GetComponent<CanvasGroup>();
            if (g == null) g = go.AddComponent<CanvasGroup>();
            return g;
        }

        void A(GameObject go, Func<double, EntranceE, double> f)
        {
            var g = G(go);
            if (g != null) _alpha.Add((g, f));
        }

        void Layer(string id, Func<double, EntranceE, double> f)
        {
            if (_p.Static != null && _p.Static.TryGetValue(id, out var img) && img != null) A(img.gameObject, f);
            if (_p.Flip != null && _p.Flip.TryGetValue(id, out var fl) && fl != null) A(fl, f);
        }

        void Host(string rig, Func<double, EntranceE, double> f)
        {
            if (_p.Hosts != null && _p.Hosts.TryGetValue(rig, out var h) && h != null) A(h.gameObject, f);
        }

        void Setup()
        {
            var enter = _enter;
            // the menu canvas fades in over the kept title canvas (D8: min(1, u / .35)); the backdrop is the room's colour
            if (enter)
            {
                _rootGroup = G(_p.Root.gameObject);
                if (_p.Backdrop != null)
                {
                    _backdrop = _p.Backdrop.GetComponent<Image>();
                    _backdropRest = WrldzAnimePalette.RoomBase;
                }
            }

            // ---- the room (#bg): Nut + the walls rise together (menu.js:647)
            A(_p.Room.gameObject, (t, E) => E.Bg);
            // ---- the Stone and its lights (menu.js:654-661)
            Layer("stone_A", (t, E) => E.Stone);
            Layer("stone_B", (t, E) => E.Stone);
            Layer("torch_pool_b0", (t, E) => E.Bg);
            Layer("torch_pool_b1", (t, E) => E.Bg);
            Layer("stone_emiBlur", (t, E) => E.Stone * E.Carve);
            Layer("stone_emi", (t, E) => E.Stone * E.Carve);
            // the recesses' live pictos and their hover / press / entrance light (anime_menu_v6.js:100-107)
            Func<double, EntranceE, double> ea = (t, E) => E.Stone * (enter ? MenuV6Math.SStep(.3, .7, E.Carve) : 1);
            foreach (var id in new[] { "recess_bazaar_picto", "recess_story_picto", "recess_view_picto" }) Layer(id, ea);
            if (_p.Static != null)
                foreach (var kv in _p.Static)
                    if (kv.Key.StartsWith("recess_") && kv.Key.EndsWith("_picto") && kv.Value != null &&
                        kv.Key != "recess_bazaar_picto" && kv.Key != "recess_story_picto" && kv.Key != "recess_view_picto")
                        A(kv.Value.gameObject, ea);
            Host("recess", ea);
            if (!enter && _p.Ctx != null)
            {
                // the cold open's recess flashes in RECESS order (the dead Ring is index 0): set, bazaar, bag, deck, story, view
                var order = new Dictionary<string, int> { { "set", 1 }, { "bazaar", 2 }, { "bag", 3 }, { "deck", 4 }, { "story", 5 }, { "view", 6 } };
                _p.Ctx.RecessEnt = (id, t) =>
                {
                    if (_done || !order.TryGetValue(id, out var i)) return 0;
                    const double rec = .95;
                    return MenuV6Math.Win(t, rec + i * .09, rec + .2 + i * .09, rec + .4 + i * .09, rec + .95 + i * .09);
                };
            }

            // the Ring echo (menu.js:771): handoff seg(u, 1.8, 1.86) (it takes over from the flying Ring), else sstep(.35, .95)
            Func<double, EntranceE, double> ring = (t, E) => E.Stone * (enter ? MenuV6Math.Seg(t, 1.8, 1.86) : MenuV6Math.SStep(.35, .95, t));
            Layer("ring_refract", ring);
            Layer("ring_echo_add", ring);
            Host("ringEcho", ring);
            // the breach (drawBreach at E.stone); its crack glow and floor fissure (handoff: they come with the finished breach)
            foreach (var id in new[] { "breach_under", "eye_13", "eye_11", "breach_over" }) Layer(id, (t, E) => E.Stone);
            Func<double, EntranceE, double> glow = (t, E) => E.Stone * (enter ? MenuV6Math.SStep(3.2, 3.4, t) : 1);
            Layer("breach_glow_add", glow);
            Host("breachGlow", glow);
            Layer("breach_fissure", (t, E) => glow(t, E) * (enter ? E.Bg : 1));
            // the door (drawDoor at E.door × E.carve)
            Layer("door_ring_add", (t, E) => E.Door * E.Carve);
            Host("door", (t, E) => E.Door * E.Carve);
            // the smoke (handoff: the realm smoke eases in as one layer 1.75-2.6, anime_v61_fixup.js:45)
            Layer("smoke", (t, E) => enter ? E.Stone * E.Bg * MenuV6Math.Out(MenuV6Math.Seg(t, 1.75, 2.6)) : E.Stone);
            // the torch: motes, flame (E.bg)
            Layer("motes_add", (t, E) => E.Bg);
            Host("motes", (t, E) => E.Bg);
            Layer("flame_glow_add", (t, E) => E.Bg);
            Layer("flame_tongues", (t, E) => E.Bg);
            Host("flame", (t, E) => E.Bg);
            // Nut's twinkle and the vignette (handoff only: they rise with the room)
            Layer("nut_twinkle_add", (t, E) => enter ? E.Bg : 1);
            Host("nut", (t, E) => enter ? E.Bg : 1);
            foreach (var v in new[] { "vignette_top", "vignette_bottom", "vignette_left", "vignette_right" })
                Layer(v, (t, E) => enter ? E.Bg : 1);
            // Nut's sky fracture fades in with the room on every page (menu.js:672)
            if (_p.NutDriver != null) _p.NutDriver.AlphaK = () => _done ? 1 : EntranceE.At(enter, MenuV6Clock.Now).Bg;
            Layer("nut_crack", (t, E) => E.Bg);

            // ---- the DOM: the labels, DUEL, the bezels (menu.js:1029-1031), the pylon rows (menu.js:1033)
            var labelOrder = new Dictionary<string, int> { { "set", 0 }, { "bazaar", 1 }, { "bag", 2 }, { "deck", 3 }, { "story", 4 }, { "view", 5 } };
            foreach (var kv in _p.Recess)
            {
                if (kv.Value == null || !labelOrder.TryGetValue(kv.Key, out var i)) continue;
                Func<double, EntranceE, double> la = (t, E) => HandoffMath.Label(enter, t, i);
                A(kv.Value.gameObject, la);
                _rise.Add((kv.Value.Rect, kv.Value.Rect.anchoredPosition, (t, E) => (1 - la(t, E)) * 8));
            }

            if (_p.Hero != null)
            {
                if (_p.Hero.Title != null) A(_p.Hero.Title.gameObject, (t, E) => HandoffMath.Duel(enter, t));
                foreach (var n in new[] { "DeckCap", "DeckCount", "Lp", "LpCap" })
                {
                    var b = _p.Hero.transform.Find(n);
                    if (b != null) A(b.gameObject, (t, E) => HandoffMath.Bezel(enter, t));
                }
            }

            PylonRows();

            // ---- the breach eyes: shut from the start, then the reference open (schedules.json eye.reveal; the handoff:
            // after the breach has torn open, at 3.45 + off — the #enter page holds them shut to its cut's end; decision)
            if (_p.Eyes != null)
                foreach (var eye in _p.Eyes)
                {
                    var sch = eye != null ? eye.Schedule : null;
                    if (sch == null) continue;
                    var off = eye.Seed == 12 ? .22 : eye.Seed == 13 ? .37 : 0;
                    sch.RevealTo = (enter ? 3.45 : 1.15) + off;
                }

            // ---- the keystone: the cold open assembles it from its 8 pieces; the handoff drops the title's Puzzle into it
            if (enter) _handoff = HandoffRig.Build(_stage, _p);
            else _assembly = KeystoneAssembly.Build(_stage, _p);
            if (_assembly != null)
                foreach (var id in new[] { "keystone_over_f0", "keystone_over_f1", "keystone_add_flick0" })
                    Layer(id, (t, E) => t < 1.9 ? 0 : 1);
        }

        /// <summary>The pylon rows (the page's .pyl children) rise one by one: eOut((t − E.ui − .09 j) / .5), 12 px. A row is
        /// a register between the slate's rules (reg_rule / cap_rule), so an element's row is the number of rules above it.</summary>
        void PylonRows()
        {
            var L = _p.Layout;
            if (L == null) return;
            var land = _p.Kind == MenuLayoutKind.Landscape;
            var rulesL = new List<float>();
            var rulesR = new List<float>();
            foreach (var layer in L.Layers)
            {
                var id = layer.Id ?? "";
                // only the register rules (.pyl .reg:after, the border under each of the pylon's own children): a caption's
                // rule (.cap:after) sits inside its register, so counting it put NEWS' rows, EVENTS and QUIT one to two rows
                // late (the P2 landing's cold open at 0.9 s showed EVENTS without its row and no QUIT yet)
                if (!id.StartsWith("reg_rule")) continue;
                var y = layer.Rect.center.y;
                if (!land || layer.Rect.center.x < 960f) rulesL.Add(y);
                else rulesR.Add(y);
            }

            rulesL.Sort();
            rulesR.Sort();
            var stageH = L.Stage.y;

            int RowOf(float yc, bool left)
            {
                var rules = left ? rulesL : rulesR;
                var j = 0;
                foreach (var r in rules)
                    if (r < yc - 2f) j++;
                return j;
            }

            void Rows(RectTransform slate, Rect slateRect)
            {
                if (slate == null) return;
                for (var i = 0; i < slate.childCount; i++)
                {
                    var c = slate.GetChild(i) as RectTransform;
                    if (c == null) continue;
                    // the child's stage-space centre from its anchors in the slate's rect
                    var cy = slateRect.y + (1f - (c.anchorMin.y + c.anchorMax.y) * .5f) * slateRect.height;
                    var cx = slateRect.x + (c.anchorMin.x + c.anchorMax.x) * .5f * slateRect.width;
                    var stretched = c.anchorMax.y - c.anchorMin.y > .9f;
                    if (stretched)
                    {
                        // a rig host spread over the slate: the voice's Puzzle rises with its own register (land: the
                        // voice_puzzle still's rect; port: the season meter is the voice); any other one with the first row
                        var still = L.LayerById(land ? "voice_puzzle" : "meter");
                        if (c.name == "Rig_voice" && still != null)
                        {
                            cy = still.Rect.center.y;
                            cx = still.Rect.center.x;
                        }
                        else cy = -1f;
                    }

                    var j = cy < 0 ? 0 : RowOf(cy, !land || cx < 960f);
                    Row(c, j);
                }
            }

            if (land)
            {
                Rows(_p.SlateL, new Rect(100f, 214f, 426f, 752f));
                Rows(_p.SlateR, new Rect(1394f, 214f, 426f, 752f));
            }
            else
            {
                Rows(_p.SlateL, new Rect(0, 0, L.Stage.x, stageH));
            }

            // the controls that live in the pylons (WORLD MAP, the deck plaque, QUIT) take their register's row
            void Ctl(ArcaneButton b)
            {
                if (b == null) return;
                var rt = b.Rect;
                var cy = (1f - (rt.anchorMin.y + rt.anchorMax.y) * .5f) * stageH;
                var cx = (rt.anchorMin.x + rt.anchorMax.x) * .5f * L.Stage.x;
                Row(rt, RowOf(cy, !land || cx < 960f));
            }

            Ctl(_p.MapBack);
            Ctl(_p.DeckPlaque);
            Ctl(_p.Quit);
        }

        void Row(RectTransform rt, int j)
        {
            Func<double, EntranceE, double> f = (t, E) => MenuV6Math.Out((t - E.Ui - j * .09) / .5);
            A(rt.gameObject, f);
            _rise.Add((rt, rt.anchoredPosition, (t, E) => (1 - f(t, E)) * 12));
        }

        void LateUpdate()
        {
            if (_done) return;
            var t = MenuV6Clock.Now;
            if (t >= _end)
            {
                Finish();
                return;
            }

            Apply(t);
        }

        void Apply(double t)
        {
            var E = EntranceE.At(_enter, t);
            // the cold open fades its layers in over the dark backdrop: the page's sRGB opacity a is a linear-light a^2.2
            // here (MenuV6Linear.Fade; the P2 landing's 0.3 s frame was 28.3 vs the still's 22.1 mean, the room's ratio
            // .83 vs .70 = a^(1/2.2) vs a). The boot handoff fades over the kept title canvas, not black: unchanged.
            foreach (var (g, f) in _alpha)
                if (g != null)
                {
                    var a = Mathf.Clamp01((float)f(t, E));
                    g.alpha = _enter ? a : MenuV6Linear.Fade(a);
                }
            foreach (var (rt, basePos, dy) in _rise)
                if (rt != null)
                    rt.anchoredPosition = basePos - new Vector2(0f, (float)dy(t, E));
            if (_rootGroup != null) _rootGroup.alpha = Mathf.Clamp01((float)(t / .35));
            if (_backdrop != null)
            {
                var c = _backdropRest;
                c.a = (float)(E.Bg * Math.Min(1, t / .35));
                _backdrop.color = c;
            }

            _handoff?.Tick(t, E);
            _assembly?.Tick(t);
        }

        /// <summary>The entrance is over: every touched value back at rest, the helpers gone.</summary>
        public void Finish()
        {
            if (_done) return;
            _done = true;
            foreach (var (g, _) in _alpha)
                if (g != null)
                    g.alpha = 1f;
            foreach (var (rt, basePos, _) in _rise)
                if (rt != null)
                    rt.anchoredPosition = basePos;
            if (_rootGroup != null) _rootGroup.alpha = 1f;
            if (_backdrop != null) _backdrop.color = _backdropRest;
            if (_p?.Ctx != null)
            {
                _p.Ctx.RecessEnt = null;
                _p.Ctx.KeystoneEye = null;
            }

            _handoff?.Finish();
            _assembly?.Finish();
            _handoff = null;
            _assembly = null;
            if (_stage != null) _stage.Entrance = null;
            Kill(this);
        }

        /// <summary>Destroy in play mode, DestroyImmediate in edit mode (the smoke test's probe).</summary>
        internal static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>Tests (MenuSmokeTest, edit mode): set the entrance up on a built Stone, apply it at each of
        /// <paramref name="times"/>, and finish it (back at rest). Throws what the setup or a frame throws.</summary>
        internal static void Probe(MenuV6Stage stage, bool enter, double[] times)
        {
            var e = stage.gameObject.AddComponent<MenuEntrance>();
            e._stage = stage;
            e._p = stage.Parts;
            e._enter = enter;
            e._end = enter ? EndEnter : EndCold;
            try
            {
                e.Setup();
                foreach (var t in times) e.Apply(t);
            }
            finally
            {
                e.Finish();
            }
        }
    }

    // ===================================================================================================================== fx

    /// <summary>The page's gold lock fx (relics.js:257-266): drawRays (20 tapered gold wedges, the fan turning .12 t) and
    /// drawLockFlash (a white + goldHot glow, a ring, rays; capped at 45 %, never a strobe).</summary>
    internal static class LockFx
    {
        public static void Rays(ShapeLayer s, double x, double y, double r, double a, double t)
        {
            if (s == null || a <= .005) return;
            for (var q = 0; q < 20; q++)
            {
                MenuV6RigMath.Ray(q, r, t, out var an, out var len);
                const double w = .07;
                var c0 = new Color(1f, 236 / 255f, 170 / 255f, MenuV6Linear.Alpha(Math.Min(1, .55 * a), MenuV6Linear.Glow));
                var c1 = new Color(1f, 200 / 255f, 90 / 255f, 0f);
                s.Tri(x, y, c0, x + Math.Cos(an - w) * len, y + Math.Sin(an - w) * len, c1, x + Math.Cos(an + w) * len, y + Math.Sin(an + w) * len, c1);
            }
        }

        public static void Flash(ShapeLayer s, QuadLayer glow, double x, double y, double r, double k)
        {
            if (k <= 0 || k >= 1) return;
            var f = Math.Sin(k * Math.PI);
            Spr(glow, MenuV6Glow.Kind.White, x, y, r * 2.2, .45 * f);
            Spr(glow, MenuV6Glow.Kind.GoldHot, x, y, r * 3.4, .38 * f);
            if (s != null)
                s.Circle(x, y, r * (.7 + 1.1 * k), 2.5, new Color(1f, 232 / 255f, 170 / 255f, MenuV6Linear.Alpha(.7 * (1 - k), MenuV6Linear.Glow)));
            Rays(s, x, y, r * 2.2, .5 * f, k * 2);
        }

        public static void Spr(QuadLayer q, MenuV6Glow.Kind kind, double x, double y, double size, double a)
        {
            if (q == null || a <= .003 || size <= 0) return;
            q.Sprite(x, y, size, size, MenuV6Glow.Uv(kind), new Color(1, 1, 1, MenuV6Linear.Alpha(Math.Min(1, a), MenuV6Linear.Glow)));
        }

        /// <summary>The 26 lock embers (menu.js:306): rng(91) land / rng(19) port, after the smoke (170 / 150 × 9 draws), the
        /// fog (34 / 26 × 6) and the motes (30 × 7) tables of the same stream.</summary>
        public static (double a, double v, double s)[] Embers(bool land, double k)
        {
            var R = MenuV6Math.Rng(land ? 91u : 19u);
            var skip = (land ? 170 : 150) * 9 + (land ? 34 : 26) * 6 + 30 * 7;
            for (var i = 0; i < skip; i++) R();
            var e = new (double, double, double)[26];
            for (var i = 0; i < 26; i++)
            {
                var a = i / 26.0 * Math.PI * 2 + R() * .3;
                var v = (60 + R() * 90) * k;
                var s = .6 + R();
                e[i] = (a, v, s);
            }

            return e;
        }

        /// <summary>burstEmbers (menu.js:839): goldHot sparks flying out, eOut, fading.</summary>
        public static void Burst(QuadLayer q, (double a, double v, double s)[] embers, double x, double y, double k, double k1)
        {
            if (q == null || embers == null || k <= 0 || k >= 1) return;
            var d0 = MenuV6Math.Out(k);
            foreach (var b in embers)
            {
                var d = d0 * b.v;
                Spr(q, MenuV6Glow.Kind.GoldHot, x + Math.Cos(b.a) * d, y + Math.Sin(b.a) * d * .8, 9 * b.s * k1, (1 - k) * .85);
            }
        }

        /// <summary>A RectTransform stretched over the stage maps stage point <paramref name="from"/> to <paramref name="to"/>
        /// scaled by <paramref name="s"/> (its pivot is the stage centre).</summary>
        public static void Map(RectTransform host, Vector2 stage, Vector2 from, Vector2 to, float s)
        {
            if (host == null) return;
            var c = stage * .5f;
            var mapped = c + (from - c) * s;
            var off = to - mapped;
            host.localScale = new Vector3(s, s, 1f);
            host.anchoredPosition = new Vector2(off.x, -off.y);
        }

        public static void Reset(RectTransform host)
        {
            if (host == null) return;
            host.localScale = Vector3.one;
            host.anchoredPosition = Vector2.zero;
        }
    }

    // ===================================================================================================================== cold open

    /// <summary>The cold open's keystone (menu.js:852-855, relics.js:236-252): the Puzzle's 8 pieces fly in from 120-220 px
    /// with 25-60° of spin (60 ms apart, eOut) and lock at 1.88-2.0 (the lock flash, the embers, the eye flaring 1.2).</summary>
    internal sealed class KeystoneAssembly
    {
        RectTransform _host, _keystone;
        CanvasGroup _ksGroup;
        readonly List<(QuadLayer s, QuadLayer c)> _q = new();
        readonly List<(Vector2 home, Vector2 dir, double h, double h2, double w, double hgt, Vector2 cb)> _pc = new();
        ShapeLayer _rays;
        QuadLayer _glow;
        (double a, double v, double s)[] _embers;
        Vector2 _centre;
        double _k1, _S;
        RigContext _ctx;

        public static KeystoneAssembly Build(MenuV6Stage stage, StageParts p)
        {
            var root = CrackData.Json(p.Kind == MenuLayoutKind.Landscape ? "coldopen_land" : "coldopen_port");
            var pk = RJ.D(RJ.D(root, "bakes"), "PK");
            if (pk == null || p.Fx2 == null) return null;
            var gfx = p.Ctx?.Gfx ?? new UguiGfx();
            var a = new KeystoneAssembly { _ctx = p.Ctx, _k1 = p.Kind == MenuLayoutKind.Landscape ? 1 : 1.4, _S = RJ.N(pk, "S", 2) };
            var W = RJ.N(pk, "W") / a._S;
            var H = RJ.N(pk, "H") / a._S;
            a._centre = RJ.V(pk, "centre");
            var frame = new Rect(0, 0, p.Layout.Stage.x, p.Layout.Stage.y);
            a._host = new GameObject("Rig_keystoneAssembly", typeof(RectTransform)).GetComponent<RectTransform>();
            a._host.SetParent(p.Fx2, false);
            UiTheme.Stretch(a._host);
            p.Hosts.TryGetValue("keystone", out a._keystone);
            if (a._keystone != null) a._host.SetSiblingIndex(a._keystone.GetSiblingIndex());
            foreach (var po in RJ.L(pk, "pieces") ?? new List<object>())
            {
                var c = gfx.Sprite(RJ.S(po, "c"));
                var sTex = gfx.Sprite(RJ.S(po, "s"));
                if (c == null) continue;
                var qs = sTex != null ? gfx.Quads(a._host, "Piece_s", frame, sTex) : null;
                var qc = gfx.Quads(a._host, "Piece_c", frame, c);
                a._q.Add((qs, qc));
                a._pc.Add((RJ.V(po, "home"), RJ.V(po, "dir"), RJ.N(po, "h"), RJ.N(po, "h2"), W, H, RJ.V(po, "cb")));
            }

            a._rays = gfx.Shapes(a._host, "Lock_rays", frame);
            a._glow = gfx.Quads(a._host, "Lock_glow", frame, gfx.Glow(MenuV6Linear.Glow));
            a._embers = LockFx.Embers(p.Kind == MenuLayoutKind.Landscape, a._k1);
            if (a._keystone != null && !a._keystone.TryGetComponent(out a._ksGroup))
                a._ksGroup = a._keystone.gameObject.AddComponent<CanvasGroup>();   // no ??: GetComponent returns a fake null in the Editor
            if (a._ctx != null)
                a._ctx.KeystoneEye = (t, eye) => eye + 1.2 * MenuV6Math.SStep(1.95, 2.1, t) * (1 - MenuV6Math.Seg(t, 2.2, 2.6));
            return a;
        }

        public void Tick(double t)
        {
            var k1 = _k1;
            if (_ksGroup != null) _ksGroup.alpha = t < 1.9 ? 0f : 1f;
            for (var i = 0; i < _q.Count; i++)
            {
                var (qs, qc) = _q[i];
                qs?.Clear();
                qc.Clear();
                if (t < 1.9)
                {
                    var pc = _pc[i];
                    if (HandoffMath.Piece(t, i, pc.dir.x, pc.dir.y, pc.h, pc.h2, k1, out var dx, out var dy, out var rot, out var al, out var th))
                    {
                        // drawPieceAbs(g, i, home + (dx, dy), rot, 1, a, th, 0)
                        double x = pc.home.x + dx, y = pc.home.y + dy;
                        double ox = -pc.cb.x / _S, oy = -pc.cb.y / _S;
                        var t2 = th * 3.2;
                        var col = new Color(1, 1, 1, (float)al);
                        if (t2 > .1 && qs != null) qs.Pivoted(x, y, ox + t2 * .55, oy + t2, pc.w, pc.hgt, QuadList.Full, col, rot);
                        qc.Pivoted(x, y, ox, oy, pc.w, pc.hgt, QuadList.Full, col, rot);
                    }
                }

                qs?.Commit();
                qc.Commit();
            }

            _rays.Clear();
            _glow.Clear();
            if (t >= 1.9 && t < 2.6)
            {
                LockFx.Flash(_rays, _glow, _centre.x, _centre.y, 34 * k1, MenuV6Math.Seg(t, 1.88, 2.0));
                LockFx.Burst(_glow, _embers, _centre.x, _centre.y, MenuV6Math.Seg(t, 1.9, 2.5), k1);
            }

            _rays.Commit();
            _glow.Commit();
        }

        public void Finish()
        {
            if (_ksGroup != null) _ksGroup.alpha = 1f;
            if (_ctx != null) _ctx.KeystoneEye = null;
            if (_host != null) MenuEntrance.Kill(_host.gameObject);
        }
    }

    // ===================================================================================================================== boot handoff

    /// <summary>
    /// The boot handoff's own drawing (menu.js:692-727, :844-851, anime_fix1_menu.js:18-37; handoff_&lt;layout&gt;.json): the
    /// title face over the menu's Stone (the blank title-lit stone outside the carve circle, the name's 12-strip feathered
    /// front, the Items' sockets, the front's glow inside the letters, the carve's rim), the gold dust, the Items and the
    /// Ring flying to their recesses, the Puzzle's flare and drop into the door (its title body crossfading into the
    /// keystone), the lock flash and embers, and the breach tearing open from its centre (a disc mask; the page scales the
    /// breach's own outline). Driven by <see cref="MenuEntrance"/>; gone at its end.
    /// </summary>
    internal sealed class HandoffRig
    {
        double _k1, _cx, _cy, _fx0, _fy0;
        Vector2 _K, _stageSize, _ptAnchor;
        double _s0, _rb;
        Rect _stoneRect, _maskRect;
        RectTransform _face, _flight, _drop, _ptHost, _ringHost, _ksHost, _breachMask, _breachFull;
        CanvasGroup _ksGroup, _ptGroup, _patchGroup;
        RigTex _stoneB, _stoneT, _emiB, _emiBlurB, _mask;
        QuadLayer _qB, _qT, _qSock, _qEmiBlur, _qEmi, _qFront, _dust, _glow, _dropGlow;
        ShapeLayer _rim, _rays, _flash;
        readonly List<(string id, int i, double x, double y, bool ring, Vector2 tgt, double rel, double touch, QuadLayer q, double recA)> _items = new();
        double[] _dx, _dy, _dvx, _dvy, _dlife, _ds, _dc, _dsw, _dt0;
        RingDraw _ring;
        readonly double[] _rot = new double[5], _px = new double[5], _py = new double[5], _gl = new double[5];
        Vector2 _ringC;
        double _ringS = 1;
        PuzzleDraw _pt;
        QuadLayer _ptShadow;
        Rect _ptShadowRect;
        (double a, double v, double s)[] _embers;
        readonly List<(RectTransform rt, int index)> _breach = new();
        Transform _breachParent;
        Vector2 _breachC;
        bool _breachOpen;

        public static HandoffRig Build(MenuV6Stage stage, StageParts p)
        {
            var land = p.Kind == MenuLayoutKind.Landscape;
            var root = CrackData.Json(land ? "handoff_land" : "handoff_port");
            var title = CrackData.Json(land ? "title_land" : "title_port");
            var bakes = RJ.D(root, "bakes");
            if (bakes == null || p.Fx == null) return null;
            var r = new HandoffRig { _k1 = land ? 1 : 1.4 };
            r._stageSize = p.Layout.Stage;
            var frame = new Rect(0, 0, r._stageSize.x, r._stageSize.y);
            var gfx = p.Ctx?.Gfx ?? new UguiGfx();
            var geo = RJ.D(bakes, "geo");
            r._K = RJ.V(geo, "K");
            r._cx = RJ.N(geo, "cx", r._stageSize.x / 2);
            r._cy = RJ.N(geo, "cy", 556);
            // ---- the face over the Stone, right after the menu's Stone A / B (menu.js:657)
            r._face = Node(p.Fx, "Handoff_face", After(p.Fx, p.Static, "stone_B"));
            var sB = RJ.D(bakes, "stoneB");
            r._stoneRect = RJ.R(sB, "rect");
            r._stoneB = gfx.Sprite(RJ.S(sB, "A"));
            r._emiBlurB = gfx.Sprite(RJ.S(sB, "emiBlur"));
            r._emiB = gfx.Sprite(RJ.S(sB, "emi"));
            var sT = RJ.D(bakes, "stoneT");
            var lo = RJ.D(RJ.D(sT, "A"), "localOr");
            r._stoneT = gfx.Local(RJ.S(lo, "local")) ?? gfx.Sprite(RJ.S(lo, "fallback"));
            if (r._stoneB != null) r._qB = gfx.Quads(r._face, "StoneB", frame, r._stoneB);
            if (r._stoneT != null)
            {
                r._qT = gfx.Quads(r._face, "StoneT_strips", frame, r._stoneT);
                r._qSock = gfx.Quads(r._face, "StoneT_sockets", frame, r._stoneT);
            }

            if (r._emiBlurB != null) r._qEmiBlur = gfx.Quads(r._face, "StoneB_emiBlur", frame, r._emiBlurB);
            if (r._emiB != null) r._qEmi = gfx.Quads(r._face, "StoneB_emi", frame, r._emiB);
            var nm = RJ.D(bakes, "nameMask");
            r._maskRect = RJ.R(nm, "rect");
            r._mask = gfx.Sprite(RJ.S(nm, "file"));
            if (r._mask != null) r._qFront = gfx.Quads(r._face, "Front_glow", frame, r._mask);
            var inset = new Rect(r._stoneRect.x + 20, r._stoneRect.y + 20, r._stoneRect.width - 40, r._stoneRect.height - 40);
            var clip = gfx.Clip(r._face, "CarveRimClip", inset, frame);
            // the graphic is stretched over its clip, so it maps the CLIP's stage rect (as the niche's do); mapping the whole
            // stage into the inset squeezed the carve's circle into an ellipse round the door (the P2 landing's u 1.7-1.95)
            r._rim = gfx.Shapes(clip, "Carve_rim", inset);
            r._rim.Feather = (float)(6 * r._k1);

            // ---- the dust and the Items, right after the Ring echo (menu.js:664)
            RectTransform afterRing = null;
            if (p.Hosts.TryGetValue("ringEcho", out var re)) afterRing = re;
            r._flight = Node(p.Fx, "Handoff_flight", afterRing != null ? afterRing.GetSiblingIndex() + 1 : -1);
            r._dust = gfx.Quads(r._flight, "Dust", frame, gfx.Glow(MenuV6Linear.Glow));
            var dust = RJ.D(bakes, "dust");
            var rows = RJ.L(dust, "rows");
            var t0 = RJ.L(dust, "t0");
            if (rows != null)
            {
                var n = rows.Count;
                r._dx = new double[n]; r._dy = new double[n]; r._dvx = new double[n]; r._dvy = new double[n];
                r._dlife = new double[n]; r._ds = new double[n]; r._dc = new double[n]; r._dsw = new double[n]; r._dt0 = new double[n];
                for (var i = 0; i < n; i++)
                {
                    var row = rows[i] as List<object>;
                    r._dx[i] = RJ.At(row, 0); r._dy[i] = RJ.At(row, 1); r._dvx[i] = RJ.At(row, 2); r._dvy[i] = RJ.At(row, 3);
                    r._dlife[i] = RJ.At(row, 4); r._ds[i] = RJ.At(row, 5); r._dc[i] = RJ.At(row, 6); r._dsw[i] = RJ.At(row, 7);
                    r._dt0[i] = RJ.At(t0, i);
                }
            }

            var recA = new Dictionary<string, double>();
            foreach (var rc in RJ.L(geo, "recess") ?? new List<object>())
            {
                var item = RJ.S(rc, "item");
                if (!string.IsNullOrEmpty(item)) recA[item] = RJ.N(rc, "a");
            }

            r._glow = gfx.Quads(r._flight, "Items_glow", frame, gfx.Glow(MenuV6Linear.Glow));
            r._flash = gfx.Shapes(r._flight, "Items_flash", frame);
            foreach (var it in RJ.L(bakes, "items") ?? new List<object>())
            {
                var id = RJ.S(it, "id");
                var ring = RJ.B(it, "ring");
                QuadLayer q = null;
                if (!ring)
                {
                    RigTex tex;
                    var spr = RJ.D(it, "spr");
                    if (spr != null)
                    {
                        var l2 = RJ.D(spr, "localOr");
                        tex = gfx.Local(RJ.S(l2, "local")) ?? gfx.Sprite(RJ.S(l2, "fallback"));
                    }
                    else tex = gfx.Sprite(RJ.S(it, "spr"));
                    if (tex != null) q = gfx.Quads(r._flight, "Item_" + id, frame, tex);
                }

                r._items.Add((id, (int)RJ.N(it, "i"), RJ.N(it, "x"), RJ.N(it, "y"), ring, RJ.V(it, "tgt"), RJ.N(it, "release"),
                    RJ.N(it, "touch"), q, recA.TryGetValue(id ?? "", out var a) ? a : -90));
            }

            // the Ring flies with the Items: the echo's own parts (relics.js drawRingSpectral), the host scaled to its size
            var echo = p.Ctx?.Rig("ringEcho");
            if (echo != null)
            {
                r._ringHost = Node(r._flight, "Ring_flight", -1);
                r._ring = RingDraw.From(gfx, r._ringHost, frame, RJ.D(echo, "parts"), "HandoffRing");
                if (r._ring != null)
                {
                    r._ring.SetGamma(MenuV6Linear.RingBlue);
                    r._ring.TipGain = r._ring.RimGain = RingDraw.EchoBright;
                    r._ringC = RJ.V(echo, "centre");
                    r._ringS = RJ.N(echo, "s", 1);
                    r._ring.X = r._ringC.x;
                    r._ring.Y = r._ringC.y;
                    r._ring.S = r._ringS;
                    r._ring.R = RJ.N(echo, "R", 48 * r._ringS);
                    r._ring.r = RJ.N(echo, "r", 38 * r._ringS);
                    var piv = RJ.L(echo, "piv");
                    for (var i = 0; i < 5 && piv != null && i < piv.Count; i++)
                    {
                        r._px[i] = RJ.At(piv[i] as List<object>, 0);
                        r._py[i] = RJ.At(piv[i] as List<object>, 1);
                    }
                }
            }

            // ---- the breach: the title's whole stone over the hole until it tears open (anime_fix1_menu.js:18-37)
            if (p.Static.TryGetValue("breach_under", out var bu) && bu != null && r._stoneB?.Sprite != null && bu.sprite != null)
            {
                var bRect = Rect(p, bu.rectTransform);
                r._breachC = bRect.center;
                r._rb = Math.Max(bRect.width, bRect.height) * .5;
                var patch = new GameObject("BreachPatch", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(CanvasGroup));
                patch.transform.SetParent(p.Fx, false);
                patch.transform.SetSiblingIndex(bu.transform.GetSiblingIndex());
                var pimg = patch.GetComponent<Image>();
                pimg.sprite = bu.sprite;
                pimg.raycastTarget = false;
                patch.GetComponent<Mask>().showMaskGraphic = false;
                MenuV6Layout.Place(pimg.rectTransform, bRect, frame);
                var stone = new GameObject("StoneB", typeof(RectTransform), typeof(Image));
                stone.transform.SetParent(patch.transform, false);
                var simg = stone.GetComponent<Image>();
                simg.sprite = r._stoneB.Sprite;
                simg.raycastTarget = false;
                MenuV6Layout.Place(simg.rectTransform, new Rect(r._stoneRect.x, r._stoneRect.y, r._stoneB.W, r._stoneB.H), bRect);
                r._patchGroup = patch.GetComponent<CanvasGroup>();
                // the finished breach is revealed through a disc that grows from the hole's centre
                var mask = new GameObject("BreachReveal", typeof(RectTransform), typeof(Image), typeof(Mask));
                mask.transform.SetParent(p.Fx, false);
                mask.transform.SetSiblingIndex(patch.transform.GetSiblingIndex() + 1);
                var mimg = mask.GetComponent<Image>();
                mimg.sprite = DiscSprite();
                mimg.raycastTarget = false;
                mask.GetComponent<Mask>().showMaskGraphic = false;
                r._breachMask = mimg.rectTransform;
                r._breachFull = new GameObject("Full", typeof(RectTransform)).GetComponent<RectTransform>();
                r._breachFull.SetParent(mask.transform, false);
                r._breachParent = p.Fx;
                var at = mask.transform.GetSiblingIndex() + 1;
                foreach (var id in new[] { "breach_under", "eye_13", "eye_11", "breach_over" })
                {
                    GameObject go = null;
                    if (p.Static.TryGetValue(id, out var im) && im != null) go = im.gameObject;
                    else if (p.Flip.TryGetValue(id, out var fl)) go = fl;
                    if (go == null) continue;
                    r._breach.Add((go.GetComponent<RectTransform>(), go.transform.GetSiblingIndex()));
                }

                r._breach.Sort((a, b) => a.index.CompareTo(b.index));
                foreach (var (rt, _) in r._breach) rt.SetParent(r._breachFull, false);
                r._breachOpen = true;
                r.PlaceReveal(0);
            }

            // ---- the Puzzle's drop into the door, before the keystone in fx2 (menu.js:844-851)
            p.Hosts.TryGetValue("keystone", out r._ksHost);
            r._drop = Node(p.Fx2, "Handoff_drop", r._ksHost != null ? r._ksHost.GetSiblingIndex() : -1);
            r._rays = gfx.Shapes(r._drop, "Flare_rays", frame);
            r._dropGlow = gfx.Quads(r._drop, "Lock_glow", frame, gfx.Glow(MenuV6Linear.Glow));
            var pz = RJ.D(RJ.D(title, "rigs"), "puzzle");
            var pt = RJ.D(bakes, "PT");
            r._ptAnchor = RJ.V(pt, "anchor");
            r._s0 = RJ.N(pt, "s0", 70 / 96.0);
            var parts = RJ.D(pt, "parts");
            var A = gfx.Sprite(RJ.S(RJ.D(parts, "A"), "file"));
            if (A != null && pz != null)
            {
                r._ptHost = Node(r._drop, "PT", -1);
                r._ptGroup = r._ptHost.gameObject.AddComponent<CanvasGroup>();
                var sh = gfx.Sprite(RJ.S(RJ.D(parts, "shadow"), "file"));
                if (sh != null)
                {
                    r._ptShadow = gfx.Quads(r._ptHost, "PT_shadow", frame, sh);
                    r._ptShadowRect = RJ.R(pz, "shadowRect");
                }

                var o = RJ.V(pz, "origin");
                var wh = RJ.V(pz, "drawWH");
                r._pt = new PuzzleDraw { Body = new Rect(o.x, o.y, wh.x, wh.y), EyeC = RJ.V(pz, "eyeC"), Iris = RJ.V(pz, "iris"), Wpx = RJ.N(pz, "wpx", 70) };
                r._pt.A = gfx.Quads(r._ptHost, "PT_A", frame, A);
                var e = gfx.Sprite(RJ.S(RJ.D(parts, "emiB"), "file"));
                if (e != null)
                {
                    r._pt.Emi = gfx.Quads(r._ptHost, "PT_emiB", frame, e);
                    r._pt.Emi.Add = true;
                }

                r._pt.Eye = gfx.Quads(r._ptHost, "PT_eyeGlow", frame, gfx.Glow(MenuV6Linear.Glow));
                r._pt.Lights = gfx.Quads(r._ptHost, "PT_lights", frame, gfx.Glow(MenuV6Linear.Glow));
            }

            // the PK's visual centre (the lock flash): coldopen PK.centre; the PT's = the same offset at s0
            var co = CrackData.Json(land ? "coldopen_land" : "coldopen_port");
            var pkc = RJ.V(RJ.D(RJ.D(co, "bakes"), "PK"), "centre");
            r._fx0 = pkc.x;
            r._fy0 = pkc.y;
            if (r._fx0 == 0 && r._fy0 == 0)
            {
                r._fx0 = r._K.x;
                r._fy0 = r._K.y;
            }

            r._embers = LockFx.Embers(land, r._k1);
            if (r._ksHost != null && !r._ksHost.TryGetComponent(out r._ksGroup))
                r._ksGroup = r._ksHost.gameObject.AddComponent<CanvasGroup>();   // no ??: GetComponent returns a fake null in the Editor
            if (p.Ctx != null)
                p.Ctx.KeystoneEye = (t, eye) =>
                {
                    var flare = MenuV6Math.Win(t, .3, .42, .55, .95);
                    if (t < 1.35) return .9 + .6 * flare;
                    return eye + 1.1 * (1 - MenuV6Math.Seg(t, 1.35, 1.95));
                };
            return r;
        }

        static RectTransform Node(Transform parent, string name, int sibling)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            UiTheme.Stretch(n);
            if (sibling >= 0) n.SetSiblingIndex(Math.Min(sibling, parent.childCount - 1));
            return n;
        }

        static int After(Transform group, Dictionary<string, Image> st, string id) =>
            st != null && st.TryGetValue(id, out var img) && img != null && img.transform.parent == group ? img.transform.GetSiblingIndex() + 1 : -1;

        /// <summary>A layer's stage rect from its anchors (it was placed in the stage rect by MenuV6Layout.Place).</summary>
        static Rect Rect(StageParts p, RectTransform rt)
        {
            var S = p.Layout.Stage;
            var x0 = rt.anchorMin.x * S.x;
            var x1 = rt.anchorMax.x * S.x;
            var y0 = (1 - rt.anchorMax.y) * S.y;
            var y1 = (1 - rt.anchorMin.y) * S.y;
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        static Sprite _disc;

        static Sprite DiscSprite()
        {
            if (_disc != null) return _disc;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "HandoffDisc" };
            var px = new Color32[n * n];
            var c = (n - 1) * .5f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(n * .5f - .5f - d + .5f) * 255));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            _disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f);
            _disc.hideFlags = HideFlags.HideAndDontSave;
            return _disc;
        }

        void PlaceReveal(double sc)
        {
            if (_breachMask == null) return;
            var rr = (float)Math.Max(.5, _rb * sc);
            var r = new Rect(_breachC.x - rr, _breachC.y - rr, rr * 2, rr * 2);
            var frame = new Rect(0, 0, _stageSize.x, _stageSize.y);
            MenuV6Layout.Place(_breachMask, r, frame);
            MenuV6Layout.Place(_breachFull, frame, r);
        }

        void CloseBreach()
        {
            if (!_breachOpen) return;
            _breachOpen = false;
            var at = _breachMask != null ? _breachMask.GetSiblingIndex() : 0;
            for (var i = 0; i < _breach.Count; i++)
            {
                var rt = _breach[i].rt;
                if (rt == null) continue;
                rt.SetParent(_breachParent, false);
                rt.SetSiblingIndex(at + i);
            }

            if (_breachMask != null) MenuEntrance.Kill(_breachMask.gameObject);
            if (_patchGroup != null) MenuEntrance.Kill(_patchGroup.gameObject);
            _breachMask = null;
            _patchGroup = null;
        }

        double Front(double t) => HandoffMath.Front(t, _k1);

        public void Tick(double u, EntranceE E)
        {
            var k1 = _k1;
            var fr = Front(u);
            Face(u, E, fr);
            Flight(u, fr);
            Drop(u);
            // the breach tears open over 2.2-3.2 (anime_fix1_menu.js:31): to the hole's own size by 2.85, on to 1.7x
            if (_breachOpen)
            {
                var sc = u < 2.85 ? MenuV6Math.InOut(MenuV6Math.Seg(u, 2.2, 2.85)) : 1 + .7 * MenuV6Math.InOut(MenuV6Math.Seg(u, 2.85, 3.2));
                PlaceReveal(u < 2.2 ? 0 : sc);
                if (_patchGroup != null) _patchGroup.alpha = 1f - (float)MenuV6Math.InOut(MenuV6Math.Seg(u, 2.2, 2.85));
                if (u >= 3.2) CloseBreach();
            }
        }

        // ---------------------------------------------------------------- the title face over the Stone (menu.js:692-710)

        void Face(double u, EntranceE E, double fr)
        {
            var k = E.Carve;
            var on = k < 1;
            _qB?.Clear();
            _qT?.Clear();
            _qSock?.Clear();
            _qEmiBlur?.Clear();
            _qEmi?.Clear();
            _qFront?.Clear();
            _rim.Clear();
            if (on)
            {
                var R = 440 * _k1 * k;
                var outA = 1 - MenuV6Math.SStep(.8, 1, k);
                if (_qB != null) Fan(_qB.Q, _stoneB, R, outA);
                // the name gives way: 12 strips, a 48 px feather, strip j at alpha 1 / (12 - j) (only while the carve has not
                // started: the front ends at u 1.3, the carve starts at 1.35)
                if (_qT != null && fr < 345 * _k1 && R <= 0)
                {
                    var fe = 48 * _k1;
                    const int NS = 12;
                    var st = _stoneRect;
                    for (var j = 0; j < NS; j++)
                    {
                        double x0 = _cx - fr - j * fe / NS, x1 = _cx + fr + j * fe / NS;
                        var c = new Color(1, 1, 1, (float)(outA / (NS - j)));
                        Sub(_qT, _stoneT, st.x, st.y, x0 - st.x, st.height, c);
                        Sub(_qT, _stoneT, x1, st.y, st.xMax - x1, st.height, c);
                    }
                }

                // the title's socket stays under its Item from the moment the front reaches it, then fades .25 s after release
                if (_qSock != null)
                    foreach (var it in _items)
                    {
                        if (it.ring) continue;
                        var sa = 1 - MenuV6Math.SStep(it.rel, it.rel + .25, u);
                        if (u < it.touch || sa <= .004) continue;
                        Disc(_qSock.Q, _stoneT, it.x, it.y, 25 * _k1, new Color(1, 1, 1, (float)(outA * sa)));
                    }

                if (_qEmiBlur != null) Fan(_qEmiBlur.Q, _emiBlurB, R, MenuV6Linear.Alpha(.5 * outA) * MenuV6Linear.LayerGain("stone_emiBlur"));
                if (_qEmi != null) Fan(_qEmi.Q, _emiB, R, MenuV6Linear.Alpha(.5 * outA) * MenuV6Linear.LayerGain("stone_emi"));
                // the front glows only inside the letters it is eating (destination-in the name mask), ramping in and out
                if (_qFront != null && fr > 1 && fr < 345 * _k1)
                {
                    var A = .8 * MenuV6Math.SStep(1, 70 * _k1, fr) * (1 - MenuV6Math.SStep(300 * _k1, 345 * _k1, fr));
                    for (var sgn = -1; sgn <= 1; sgn += 2) FrontGlow(_cx + sgn * fr, A);
                }

                // the carve's rim: a blurred gold ring at R, inside the slab (inset 20 px)
                if (k > 0)
                    _rim.Circle(_K.x, _K.y, R, 5 * _k1, new Color(1f, 220 / 255f, 140 / 255f, MenuV6Linear.Alpha(.8 * Math.Sin(k * Math.PI), MenuV6Linear.Glow)));
            }

            _qB?.Commit();
            _qT?.Commit();
            _qSock?.Commit();
            _qEmiBlur?.Commit();
            _qEmi?.Commit();
            _qFront?.Commit();
            _rim.Commit();
        }

        void Vert(QuadList q, double x, double y, RigTex tex, Rect area, Color c)
        {
            float u = tex != null && tex.W > 0 ? (float)((x - area.x) / tex.W) : 0f;
            float v = tex != null && tex.H > 0 ? 1f - (float)((y - area.y) / tex.H) : 0f;
            q.V.Add(new StageVert { X = x, Y = y, C = c, U = u, V = v });
        }

        /// <summary>The stone rect minus the carve's disc (R = 0: the whole rect), as a fan of rays from the keystone.</summary>
        void Fan(QuadList q, RigTex tex, double R, double alpha)
        {
            if (tex == null || alpha <= .002) return;
            var st = new Rect(_stoneRect.x, _stoneRect.y, tex.W, tex.H);
            var c = new Color(1, 1, 1, (float)alpha);
            double cx = _K.x, cy = _K.y;
            var angs = new List<double>();
            const int N = 128;
            for (var i = 0; i < N; i++) angs.Add(i * Math.PI * 2 / N);
            foreach (var (x, y) in new[] { (st.xMin, st.yMin), (st.xMax, st.yMin), (st.xMax, st.yMax), (st.xMin, st.yMax) })
                angs.Add(Math.Atan2(y - cy, x - cx) < 0 ? Math.Atan2(y - cy, x - cx) + Math.PI * 2 : Math.Atan2(y - cy, x - cx));
            angs.Sort();
            var b0 = q.V.Count;
            foreach (var a in angs)
            {
                double dx = Math.Cos(a), dy = Math.Sin(a);
                // the ray's exit through the rect
                var tx = dx > 1e-9 ? (st.xMax - cx) / dx : dx < -1e-9 ? (st.xMin - cx) / dx : double.MaxValue;
                var ty = dy > 1e-9 ? (st.yMax - cy) / dy : dy < -1e-9 ? (st.yMin - cy) / dy : double.MaxValue;
                var D = Math.Min(tx, ty);
                var r0 = Math.Min(R, D);
                Vert(q, cx + dx * r0, cy + dy * r0, tex, st, c);
                Vert(q, cx + dx * D, cy + dy * D, tex, st, c);
            }

            var n = angs.Count;
            for (var i = 0; i < n; i++)
            {
                int a0 = b0 + i * 2, a1 = b0 + ((i + 1) % n) * 2;
                q.I.Add(a0); q.I.Add(a0 + 1); q.I.Add(a1 + 1);
                q.I.Add(a1 + 1); q.I.Add(a1); q.I.Add(a0);
            }
        }

        /// <summary>A sub-rect of the stone texture at its own place (stage px).</summary>
        void Sub(QuadLayer q, RigTex tex, double x, double y, double w, double h, Color c)
        {
            if (w <= 0 || h <= 0 || tex == null) return;
            var st = _stoneRect;
            var uv = new Rect((float)((x - st.x) / tex.W), 1f - (float)((y + h - st.y) / tex.H), (float)(w / tex.W), (float)(h / tex.H));
            q.Rect(new Rect((float)x, (float)y, (float)w, (float)h), uv, c);
        }

        /// <summary>A disc of the stone texture (the title's socket under an Item).</summary>
        void Disc(QuadList q, RigTex tex, double x, double y, double r, Color c)
        {
            var st = new Rect(_stoneRect.x, _stoneRect.y, tex.W, tex.H);
            var b = q.V.Count;
            Vert(q, x, y, tex, st, c);
            const int N = 40;
            for (var i = 0; i <= N; i++)
            {
                var a = i * Math.PI * 2 / N;
                Vert(q, x + Math.Cos(a) * r, y + Math.Sin(a) * r, tex, st, c);
            }

            for (var i = 0; i < N; i++)
            {
                q.I.Add(b);
                q.I.Add(b + 1 + i);
                q.I.Add(b + 2 + i);
            }
        }

        void FrontGlow(double x, double A)
        {
            var q = _qFront.Q;
            var m = _maskRect;
            var tex = _mask;
            var w = 22 * _k1;
            double y0 = Math.Max(m.y, _cy - 200 * _k1), y1 = Math.Min(m.y + tex.H, _cy - 200 * _k1 + 240 * _k1);
            if (y1 <= y0) return;
            var area = new Rect(m.x, m.y, tex.W, tex.H);
            var cEdge = new Color(1f, 226 / 255f, 150 / 255f, 0f);
            var cMid = new Color(1f, 240 / 255f, 190 / 255f, MenuV6Linear.Alpha(Math.Min(1, .9 * A), MenuV6Linear.Glow));
            double xa = Math.Max(m.x, x - w), xm = x, xb = Math.Min(m.x + tex.W, x + w);
            if (xb <= xa) return;
            // two quads: edge → middle → edge (the gradient), clipped to the mask's rect
            void Col(double xx, Color c)
            {
                Vert(q, xx, y0, tex, area, c);
                Vert(q, xx, y1, tex, area, c);
            }

            Color At(double xx) => Color.Lerp(cEdge, cMid, (float)(1 - Math.Abs(xx - x) / w));
            var b = q.V.Count;
            var xs = new List<double> { xa };
            if (xm > xa && xm < xb) xs.Add(xm);
            xs.Add(xb);
            foreach (var xx in xs) Col(xx, At(xx));
            for (var i = 0; i + 1 < xs.Count; i++)
            {
                int p0 = b + i * 2, p1 = b + (i + 1) * 2;
                q.I.Add(p0); q.I.Add(p1); q.I.Add(p1 + 1);
                q.I.Add(p1 + 1); q.I.Add(p0 + 1); q.I.Add(p0);
            }
        }

        // ---------------------------------------------------------------- the dust, the Items, the Ring (menu.js:711-727)

        void Flight(double u, double fr)
        {
            var k1 = _k1;
            _dust.Clear();
            _glow.Clear();
            _flash.Clear();
            if (_dx != null && u < 2.3)
                for (var i = 0; i < _dx.Length; i++)
                {
                    var a = u - _dt0[i];
                    if (a < 0 || a > _dlife[i]) continue;
                    var uu = a / _dlife[i];
                    LockFx.Spr(_dust, _dc[i] < .5 ? MenuV6Glow.Kind.GoldHot : MenuV6Glow.Kind.Gold,
                        _dx[i] + _dvx[i] * a + Math.Sin(a * 6 + _dsw[i]) * 4 * k1, _dy[i] + _dvy[i] * a, _ds[i] * (1 - .5 * uu),
                        Math.Sin(Math.PI * Math.Min(1, uu * 1.3)) * .85);
                }

            foreach (var it in _items)
            {
                it.q?.Clear();
                if (u >= 2.3)
                {
                    it.q?.Commit();
                    continue;
                }

                HandoffMath.Item(u, k1, _cx, _cy, it.x, it.y, it.i, it.ring, it.rel, it.recA, it.tgt.x, it.tgt.y, out var released,
                    out var qx, out var qy, out var sz, out var alpha, out var lift, out var fly, out var land);
                if (!released && !it.ring)
                {
                    it.q?.Sprite(qx, qy, sz, sz, QuadList.Full, Color.white);
                    it.q?.Commit();
                    continue;
                }

                if (it.ring)
                {
                    Ring(u, qx, qy, lift, fly);
                    continue;
                }

                if (land >= 1)
                {
                    it.q?.Commit();
                    continue;
                }

                LockFx.Spr(_glow, MenuV6Glow.Kind.Gold, qx, qy, sz * 2.2, (.35 * lift + .3 * Math.Sin(Math.PI * fly)) * (1 - land));
                if (land > 0) LockFx.Flash(_flash, _glow, it.tgt.x, it.tgt.y, 16 * k1, land * 2 < 1 ? land * 2 : 1);
                it.q?.Sprite(qx, qy, sz, sz, QuadList.Full, new Color(1, 1, 1, (float)alpha));
                it.q?.Commit();
            }

            if (_ring != null && u >= 1.86) Ring(u, 0, 0, 0, -1);
            _dust.Commit();
            _glow.Commit();
            _flash.Commit();
        }

        void Ring(double u, double qx, double qy, double lift, double fly)
        {
            if (_ring == null || _ringHost == null) return;
            if (fly < 0 || u >= 1.86)
            {
                _ring.Draw(u, new RingDraw.State { A = 0 });
                return;
            }

            var sc = MenuV6Math.Lerp(.34, 1, fly) * _k1;
            LockFx.Map(_ringHost, _stageSize, _ringC, new Vector2((float)qx, (float)qy), (float)(sc / _ringS));
            for (var j = 0; j < 5; j++) _rot[j] = .06 * Math.Sin(u * 2 + j);
            _ring.Draw(u, new RingDraw.State
            {
                A = 1 - MenuV6Math.Seg(u, 1.8, 1.86), I = .8 + .5 * lift * (1 - fly), Seed = 2.2, Sweep = 0, Aura = .5 * lift,
                Found = fly > .9 ? 2 : 0, Motes = 10, Rot = _rot, PivX = _px, PivY = _py, Glow = _gl
            });
        }

        // ---------------------------------------------------------------- the Puzzle drops into the door and locks (menu.js:844-851)

        void Drop(double u)
        {
            var k1 = _k1;
            _rays.Clear();
            _ptShadow?.Clear();
            var kx = _K.x;
            var ky = _K.y;
            if (u < 1.35)
            {
                double sx = _ptAnchor.x, sy = _ptAnchor.y;
                HandoffMath.Drop(u, sx, sy, kx, ky, _s0, k1, out var x, out var y, out var sc, out var xf, out var flare, out var lift);
                if (flare > .01)
                {
                    // PT.centreAt(sx, sy - lift, 1): the PK's visual centre offset at the PT's own scale
                    double fx = sx + (_fx0 - kx) * _s0, fy = sy - lift + (_fy0 - ky) * _s0;
                    LockFx.Rays(_rays, fx, fy, 90 * k1, .5 * flare, u);
                }

                if (_pt != null)
                {
                    LockFx.Map(_ptHost, _stageSize, _ptAnchor, new Vector2((float)x, (float)y), (float)(sc / _s0));
                    _ptGroup.alpha = (float)(1 - xf);
                    if (_ptShadow != null) _ptShadow.Rect(_ptShadowRect, QuadList.Full, new Color(1, 1, 1, MenuV6Linear.Shade(.5)));
                    _pt.Draw(u, double.NaN, .9 + .9 * flare, -1, -1, flare);
                }

                if (_ksHost != null)
                {
                    LockFx.Map(_ksHost, _stageSize, _K, new Vector2((float)x, (float)y), (float)sc);
                    _ksGroup.alpha = (float)xf;
                }
            }
            else
            {
                if (_ptGroup != null) _ptGroup.alpha = 0f;
                if (_ksHost != null)
                {
                    LockFx.Reset(_ksHost);
                    _ksGroup.alpha = 1f;
                }

            }

            // the lock (u >= 1.35): the flash, the embers (burstEmbers seg(u, 1.37, 2.0)); drawn before the keystone
            _dropGlow.Clear();
            if (u >= 1.35)
            {
                LockFx.Flash(_rays, _dropGlow, _fx0, _fy0, 34 * k1, MenuV6Math.Seg(u, 1.35, 1.47));
                LockFx.Burst(_dropGlow, _embers, _fx0, _fy0, MenuV6Math.Seg(u, 1.37, 2.0), k1);
            }

            _rays.Commit();
            _dropGlow.Commit();
            _ptShadow?.Commit();
        }

        public void Finish()
        {
            CloseBreach();
            if (_ksHost != null) LockFx.Reset(_ksHost);
            if (_ksGroup != null) _ksGroup.alpha = 1f;
            foreach (var n in new[] { _face, _flight, _drop })
                if (n != null)
                    MenuEntrance.Kill(n.gameObject);
        }
    }
}
