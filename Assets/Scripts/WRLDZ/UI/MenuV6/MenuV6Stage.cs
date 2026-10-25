using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// "The Stone Beneath Nut" v6.3 main menu (BUILD_PLAN §6 P1a, P1b): the approved stills rebuilt from the exported
    /// layers (layout_land.json / layout_port.json, back to front as the page draws them), the slates with real data (§7),
    /// his eye blinking (flipbooks, D20), the torch flicker, and from P1b the idle life (<see cref="MenuV6Life"/>: the
    /// recesses' hover and press, the door, the keystone, Nut, the Ring echo, the box, the live niche, the voice, the
    /// flame, the motes, the breach glow) and the portrait first cut. Only the painting changes: every control keeps the
    /// old hub's name and handler (DuelDiskMenuUI passes its own), all of them under <c>Tiles</c> with the old stagger.
    /// <para>
    /// Null-safe by construction: a missing sprite is skipped (never a white box), a missing text slot is skipped, a rig
    /// that cannot build leaves the P1a still of its layer in place, and DuelDiskMenuUI falls back to the legacy menu
    /// when <see cref="Supports"/> / <see cref="HasArt"/> say no, when the kill switch is set, or when this throws.
    /// </para>
    /// </summary>
    public sealed class MenuV6Stage : MonoBehaviour
    {
        /// <summary>PlayerPrefs kill switch: 1 = always the legacy menu (rollback without a recompile).</summary>
        public const string KillSwitchPref = "WRLDZ_MenuV6Off";

        /// <summary>Tests: build the legacy menu as if the kill switch were set (no PlayerPrefs write).</summary>
        public static bool ForceLegacy;

        public sealed class Handlers
        {
            public Action OpenDeckHub, ShowInventory, OpenStory, OpenBazaar, OpenFreeView, OpenSettings;
            public Action OpenDuelChoice, GoOverworld, OpenQuitConfirm, OpenAvatarMenus;
        }

        // The six recesses: layout id, the old hub's control name, the label and sub-line (the approved copy,
        // stone.js RECESS / menu.js rl labels). The Ring echo (id "ring") is dead: not a control (D12).
        static readonly (string id, string unity, string label, string sub, EldritchTier tier)[] RecessTable =
        {
            ("deck", "Dest_DECK", "DECK", "play deck box", EldritchTier.Secondary),
            ("bag", "Dest_BAG", "BAG", "backpack", EldritchTier.Secondary),
            ("story", "Dest_STORY", "STORY", "Season 1", EldritchTier.Secondary),
            ("bazaar", "Dest_BAZAAR", "BAZAAR", "tablet packs", EldritchTier.Secondary),
            ("view", "Tert_VIEW", "VIEW", "Solid Vision · AR", EldritchTier.Tertiary),
            ("set", "Tert_SET", "SETTINGS", "options", EldritchTier.Tertiary),
        };

        // The page's slates (layout_land.json stage.panels): what is left of x 960 belongs to the DUELIST slate.
        static readonly Rect SlateLRect = new(100f, 214f, 426f, 752f);
        static readonly Rect SlateRRect = new(1394f, 214f, 426f, 752f);

        // Placed from the approved still (stage px): the avatar disc inside ava_ring (its opaque inner disc, measured
        // from the sprite), the soul gem on the ring (D15), the status line. Portrait derives them from its own ring.
        static readonly Rect AvatarDiscLand = new(127f, 235f, 74f, 74f);
        static readonly Rect SoulGemLand = new(178f, 286f, 24f, 24f);
        static readonly Rect AvaRingLand = new(119.5f, 227.5f, 90f, 98f);
        static readonly Rect DeckPlaqueLand = new(122f, 531f, 382f, 150f);
        static readonly Rect StatusLand = new(660f, 1004f, 600f, 30f);

        // Portrait (layout_port.json): the deck row (box + its lines), the status line between that row and NEWS.
        static readonly Rect DeckPlaquePort = new(40f, 1730f, 534f, 266f);
        static readonly Rect StatusPort = new(240f, 2004f, 600f, 34f);

        // The box inscription block on hover: the page's own line breaks (out_p1b/rigs/*/shots/hov_box_t2.800.png)
        const string BoxReadingLand = "THE BOX’S HIEROGLYPHS ·\nSUGOROKU’S READING";
        const string BoxQuote = "“The one who solves me shall\ngain the powers and\nknowledge of darkness.”";

        public RectTransform Root { get; private set; }
        public RectTransform Stage { get; private set; }
        public RectTransform Tiles { get; private set; }
        public Text Status { get; private set; }
        public MenuV6Layout Layout { get; private set; }
        public MenuV6Snapshot Data { get; private set; }
        public MenuLayoutKind Kind { get; private set; }
        public readonly List<string> Missing = new();
        public readonly List<EyeFlipbook> Eyes = new();
        public TorchFlicker Torch { get; private set; }

        /// <summary>The idle rigs (P1b); empty when rigs_&lt;layout&gt;.json is missing (the P1a stills then show).</summary>
        public MenuV6Life Life { get; private set; }

        /// <summary>The rigs that could not build (their layers keep the P1a still).</summary>
        public readonly List<string> RigsKept = new();

        /// <summary>How this build was entered (P2): the boot handoff, the cold open, or None (at rest).</summary>
        public MenuEntry.Mode Entry { get; private set; }

        /// <summary>The live crack layers (P2, D11): #crkU and Nut's sky fracture (null = the P1a baked plates).</summary>
        public CrackLayer Cracks { get; private set; }

        public CrackLayer NutCracks { get; private set; }

        /// <summary>The entrance (P2: cold open / boot handoff); null at rest and once it is over.</summary>
        public MenuEntrance Entrance { get; internal set; }

        /// <summary>What the entrance drives (P2).</summary>
        internal readonly StageParts Parts = new();

        Handlers _h;
        Rect _stageRect;
        Vector2 _lastRootSize;
        bool Land => Kind == MenuLayoutKind.Landscape;

        // ------------------------------------------------------------------ gates

        public static bool KillSwitchOn
        {
            get
            {
                try { return PlayerPrefs.GetInt(KillSwitchPref, 0) == 1; }
                catch { return false; }
            }
        }

        /// <summary>The plans that exist: landscape (P1a) and the portrait first cut (P1b).</summary>
        public static bool Supports(MenuLayoutKind kind) => kind == MenuLayoutKind.Landscape || kind == MenuLayoutKind.Portrait;

        /// <summary>The plan's JSON and the two base layers (room, Stone A) are importable.</summary>
        public static bool HasArt(MenuLayoutKind kind)
        {
            var layout = MenuV6Layout.Load(kind);
            if (layout == null) return false;
            var room = layout.LayerById("room");
            var stone = layout.LayerById("stone_A");
            return room != null && stone != null && MenuV6Art.Sprite(room.File) != null &&
                   MenuV6Art.Sprite(stone.File) != null;
        }

        /// <summary>Null when the v6 menu can be built for <paramref name="kind"/>; else why not (for the log).</summary>
        public static string WhyNot(MenuLayoutKind kind)
        {
            if (ForceLegacy) return "legacy forced (test)";
            if (KillSwitchOn) return "kill switch " + KillSwitchPref + " = 1";
            if (!Supports(kind)) return kind + " has no v6 plan yet";
            if (!HasArt(kind)) return "v6 art missing (Resources/" + MenuV6Art.DataRoot + MenuV6Layout.FileFor(kind) + ")";
            return null;
        }

        // ------------------------------------------------------------------ build

        /// <summary>Build the Stone into <paramref name="canvas"/>. Throws on a real error (the caller falls back).</summary>
        public static MenuV6Stage Build(RectTransform canvas, Handlers handlers, MenuLayoutKind kind) =>
            Build(canvas, handlers, kind, MenuEntry.Mode.None);

        /// <summary>P2: the same, entered by <paramref name="entry"/> (the boot handoff from the title, the cold open, or
        /// None = the Stone at rest: edit mode, captures).</summary>
        public static MenuV6Stage Build(RectTransform canvas, Handlers handlers, MenuLayoutKind kind, MenuEntry.Mode entry)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            MenuV6Art.ClearCache();
            var layout = MenuV6Layout.Load(kind) ?? new MenuV6Layout();

            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(canvas, false);
            UiTheme.Stretch(backdrop.GetComponent<RectTransform>());
            var bimg = backdrop.GetComponent<Image>();
            bimg.sprite = UiFoundation.WhiteSprite();
            bimg.color = WrldzAnimePalette.RoomBase;
            bimg.raycastTarget = false;
            if (entry == MenuEntry.Mode.FromTitle && Application.isPlaying)
                bimg.color = new Color(bimg.color.r, bimg.color.g, bimg.color.b, 0f);   // the title shows through until the room rises

            var rootGo = new GameObject("Root", typeof(RectTransform));
            rootGo.transform.SetParent(canvas, false);
            var root = rootGo.GetComponent<RectTransform>();
            UiTheme.Stretch(root);
            rootGo.AddComponent<SanctuaryChrome>().Host = SanctuaryHost.Stone;   // P3: the painters under the Stone paint its sheets
            var stage = rootGo.AddComponent<MenuV6Stage>();
            stage.Root = root;
            stage.Layout = layout;
            stage.Kind = kind;
            stage._h = handlers ?? new Handlers();
            stage.Entry = Application.isPlaying ? entry : MenuEntry.Mode.None;
            stage.Parts.Canvas = canvas;
            stage.Parts.Backdrop = backdrop.GetComponent<RectTransform>();
            stage.BuildInto(canvas);
            return stage;
        }

        void BuildInto(RectTransform canvas)
        {
            _stageRect = Layout.StageRect;
            Data = MenuV6Data.Read();
            MenuV6Clock.Restart();

            var st = new GameObject("Stage", typeof(RectTransform)).GetComponent<RectTransform>();
            st.SetParent(Root, false);
            st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f);
            st.pivot = new Vector2(0.5f, 0.5f);
            st.sizeDelta = Layout.Stage;
            st.anchoredPosition = Vector2.zero;
            Stage = st;
            Fit();

            var room = Group("Room");
            var cracks = Group("CracksU");
            var fx = Group("Fx");
            var slates = Group("Slates");
            Parts.Root = Root;
            Parts.Stage = Stage;
            Parts.Room = room;
            Parts.CracksGroup = cracks;
            Parts.Fx = fx;
            Parts.Slates = slates;
            RectTransform slateL, slateR;
            if (Land)
            {
                slateL = Node(slates, "SlateL", SlateLRect, _stageRect);
                slateR = Node(slates, "SlateR", SlateRRect, _stageRect);
            }
            else
            {
                // the portrait has no slates: the identity sits in the sky register, the rows under the Stone
                slateL = slateR = Node(slates, "Chrome", _stageRect, _stageRect);
            }

            Tiles = Group("Tiles");
            var fx2 = Group("Fx2");
            var ph5 = Group("Placeholder");
            Parts.Tiles = Tiles;
            Parts.Fx2 = fx2;
            Parts.Ph5 = ph5;
            Parts.SlateL = slateL;
            Parts.SlateR = slateR;

            // controls first, so their own chrome can be parented to them
            var controls = BuildControls();
            _ctx = NewRigContext(controls);

            _noise = LoadNoiseRow();
            Torch = TorchFlicker.Attach(gameObject, _noise);
            foreach (var L in Layout.Layers)
            {
                switch (L.Group)
                {
                    case "bg":
                        StaticLayer(room, _stageRect, L);
                        break;
                    case "crkU":
                        if (!LiveCracks(cracks)) StaticLayer(cracks, _stageRect, L);
                        break;
                    case "fx":
                        FxLayer(fx, L);
                        break;
                    case "fx2":
                        FxLayer(fx2, L);
                        break;
                    case "ph5":
                        StaticLayer(ph5, _stageRect, L);
                        break;
                    case "ui":
                        if (Land) UiLayer(L, slateL, slateR, controls);
                        else UiLayerPort(L, slateL, controls);
                        break;
                }
            }

            if (Land) BuildSlateTexts(slateL, slateR, controls);
            else BuildPortraitTexts(slateL, controls);
            BuildControlTexts(controls);
            BuildPlaceholder(ph5);

            var statusRect = Land ? StatusLand : StatusPort;
            Status = TextFx.Label(Stage, "Status", StatusSlot(statusRect), "", _stageRect);
            if (Status == null)
            {
                Status = GoTheme.Label(Stage, "Status", "", 16, WrldzAnimePalette.Calc, TextAnchor.MiddleCenter, bold: false);
                MenuV6Layout.Place(Status.rectTransform, statusRect, _stageRect);
            }

            Status.alignment = TextAnchor.MiddleCenter;

            // quiet hover motes (ArcaneButton quiet: ≤ 3 per hover) need a field on the canvas; no ambient motes
            UiEmberField.Attach(canvas, 12, false);

            if (Torch != null) Torch.Apply(MenuV6Clock.Now);
            BuildRigs(controls);
            Parts.Static = _static;
            Parts.Hosts = _hosts;
            Parts.Flip = _flip;
            Parts.Ctx = _ctx;
            Parts.Hero = controls.Hero;
            Parts.HeroRect = controls.HeroRect;
            Parts.MapBack = controls.MapBack;
            Parts.Quit = controls.Quit;
            Parts.DeckPlaque = controls.DeckPlaque;
            foreach (var kv in controls.Recess) Parts.Recess[kv.Key] = kv.Value.btn;
            Parts.Layout = Layout;
            Parts.Kind = Kind;
            Parts.Eyes = Eyes;
            // P2: the entrance (the boot handoff over the kept title canvas, or the cold open). It replaces the old Tiles
            // stagger: the page's own entrance staggers the labels, DUEL, the bezels and the pylon rows.
            if (Application.isPlaying && Entry != MenuEntry.Mode.None)
                Entrance = MenuEntrance.Attach(this, Entry == MenuEntry.Mode.FromTitle);
            if (Application.isPlaying && Entrance == null)
                MenuMotion.PlayOn(Tiles.gameObject, MenuMotion.StaggerChildren(Tiles, 0.26f, 0.045f));
        }

        RectTransform Group(string name)
        {
            var g = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            g.SetParent(Stage, false);
            UiTheme.Stretch(g);
            return g;
        }

        static RectTransform Node(Transform parent, string name, Rect r, Rect parentRect)
        {
            var n = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            n.SetParent(parent, false);
            MenuV6Layout.Place(n, r, parentRect);
            return n;
        }

        // ------------------------------------------------------------------ the live cracks (P2, D11)

        CrackSet _crackSet;
        bool _crackTried;

        CrackSet CrackSetOf()
        {
            if (_crackTried) return _crackSet;
            _crackTried = true;
            _crackSet = CrackData.Load("menu", Kind);
            return _crackSet;
        }

        /// <summary>Edit mode (MenuSmokeTest) and a frozen capture draw the cracks at rest (the approved still's 2.8 s).</summary>
        static double CrackClockNow() => Application.isPlaying || MenuV6Clock.Frozen ? MenuV6Clock.Now : Math.Max(MenuV6Clock.Now, 2.8);

        /// <summary>#crkU (the walls, pylons, cornice, floor, panels, sky): grow in, creep by time only, breathe, the spasm,
        /// the time stutter and the black lightning; false = keep the P1a baked plate.</summary>
        bool LiveCracks(RectTransform group)
        {
            var set = CrackSetOf();
            if (set == null || set.Nets.Count == 0) return false;
            try
            {
                var drv = new MenuCrackDriver(set.Nets, Entry == MenuEntry.Mode.FromTitle) { Rest = Entry == MenuEntry.Mode.None };
                Cracks = CrackLayer.Create(group, "CracksU_live", _stageRect, set, set.Nets, drv, CrackClockNow);
                if (Cracks == null) return false;
                Cracks.Render(CrackClockNow());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] live cracks kept the baked plate: " + ex.Message);
                if (Cracks != null) DestroyNow(Cracks.gameObject);
                Cracks = null;
                return false;
            }
        }

        bool LiveNutCrack(RectTransform group, MenuV6Layout.Layer plate)
        {
            var set = CrackSetOf();
            if (set?.Nut == null) return false;
            try
            {
                // clipped to the baked plate's own rect (+8 px): the page clips the fracture to Nut's body (N.body); the
                // dread ladder that runs it further is P5
                var clipRect = new Rect(plate.Rect.x - 8f, plate.Rect.y - 8f, plate.Rect.width + 16f, plate.Rect.height + 16f);
                var clip = new GameObject("NutCrackClip", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
                clip.SetParent(group, false);
                MenuV6Layout.Place(clip, clipRect, _stageRect);
                var full = new GameObject("Full", typeof(RectTransform)).GetComponent<RectTransform>();
                full.SetParent(clip, false);
                MenuV6Layout.Place(full, _stageRect, clipRect);
                var drv = new NutCrackDriver(set.Nut, set.RiftRun, Entry == MenuEntry.Mode.FromTitle);
                NutCracks = CrackLayer.Create(full, "NutCrack_live", _stageRect, set, new[] { set.Nut }, drv, CrackClockNow,
                    nestedCanvas: false);
                if (NutCracks == null)
                {
                    DestroyNow(clip.gameObject);
                    return false;
                }

                NutCracks.Render(CrackClockNow());
                Parts.NutDriver = drv;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] Nut's live fracture kept the baked plate: " + ex.Message);
                return false;
            }
        }

        /// <summary>Tests (MenuSmokeTest, edit mode): run the entrance (the boot handoff or the cold open) at a few times on
        /// this Stone and put it back at rest; throws what a frame throws.</summary>
        public void ProbeEntrance(bool enter, params double[] times) => MenuEntrance.Probe(this, enter, times);

        /// <summary>The through-the-dim copy (#crkB, anime_v61_cracks.js:673): the same breaks over a sheet's dim at 30 %
        /// (floor breaks at sp.lit), no light, no bolts. <paramref name="dimGroup"/> fades it with its dim.</summary>
        public CrackLayer DimCopy(Transform host, CanvasGroup dimGroup, int siblingIndex)
        {
            var set = CrackSetOf();
            if (set == null || Cracks == null || host == null) return null;
            var mirror = new GameObject("CracksDim", typeof(RectTransform)).GetComponent<RectTransform>();
            mirror.SetParent(host, false);
            if (siblingIndex >= 0) mirror.SetSiblingIndex(siblingIndex);
            mirror.anchorMin = mirror.anchorMax = new Vector2(.5f, .5f);
            mirror.pivot = new Vector2(.5f, .5f);
            mirror.sizeDelta = Layout.Stage;
            mirror.localScale = Stage.localScale;
            mirror.gameObject.AddComponent<StageMirror>().Source = Stage;
            var drv = new MenuCrackDriver(set.Nets, Entry == MenuEntry.Mode.FromTitle)
            {
                DimCopy = true,
                Rest = Entry == MenuEntry.Mode.None,
                AlphaK = () => .3 * (dimGroup != null ? dimGroup.alpha : 1f)
            };
            var L = CrackLayer.Create(mirror, "CracksDim_live", _stageRect, set, set.Nets, drv, CrackClockNow);
            if (L != null) L.Render(CrackClockNow());
            return L;
        }

        /// <summary>Keeps a node in the Stage's own frame (a popover's dim copy follows the stage's fit).</summary>
        sealed class StageMirror : MonoBehaviour
        {
            public RectTransform Source;

            void LateUpdate()
            {
                if (Source == null) return;
                var rt = (RectTransform)transform;
                if (rt.localScale != Source.localScale) rt.localScale = Source.localScale;
            }
        }

        // ------------------------------------------------------------------ controls

        sealed class Controls
        {
            public ArcaneButton Hero, MapBack, Quit, DeckPlaque;
            public Rect HeroRect, MapRect, QuitRect, PlaqueRect;
            public RectTransform RingZone;
            public readonly Dictionary<string, (ArcaneButton btn, Rect rect, MenuV6Layout.Recess rec)> Recess = new();
        }

        Controls BuildControls()
        {
            var c = new Controls();

            // DUEL: the door (the old Hero) → OpenDuelChoice
            c.HeroRect = Layout.HasDoor ? Layout.DoorButton : new Rect(820f, 416f, 280f, 280f);
            c.Hero = Control(Tiles, "Hero", "DUEL", _h.OpenDuelChoice, EldritchTier.Primary, null, c.HeroRect, round: true);

            foreach (var r in RecessTable)
            {
                var rec = Layout.RecessById(r.id);
                var rect = rec != null && rec.HasButton ? rec.ButtonRect : Rect.zero;
                var btn = Control(Tiles, r.unity, r.label, HandlerFor(r.id), r.tier, r.sub, rect, round: true);
                c.Recess[r.id] = (btn, rect, rec);
            }

            // WORLD MAP: the niche (the old MapBack) → GoOverworld
            c.MapRect = Layout.HasNiche ? Layout.NicheMap : new Rect(122f, 703f, 382f, 154f);
            c.MapBack = Control(Tiles, "MapBack", "WORLD MAP", _h.GoOverworld, EldritchTier.Primary,
                "Battle City · night", c.MapRect, round: false);

            // QUIT → OpenQuitConfirm (sub-line per D14; the portrait plate has no sub-line)
            c.QuitRect = Layout.HasQuit ? Layout.Quit : new Rect(1416f, 757.5f, 250f, 64f);
            c.Quit = Control(Tiles, "Quit", "QUIT", _h.OpenQuitConfirm, EldritchTier.Danger, Land ? "fold the disk" : null,
                c.QuitRect, round: false);

            // the Spirit Dueler plaque in the Puzzle's box → the DECK screen (D16: an existing route)
            c.PlaqueRect = Land ? DeckPlaqueLand : DeckPlaquePort;
            c.DeckPlaque = Control(Tiles, "DeckPlaque", null, _h.OpenDeckHub, EldritchTier.Secondary, null,
                c.PlaqueRect, round: false);

            // the Ring echo hovers (its glow and sway, menu.js hovR) but is not a control (D12): no Button, no route
            var ring = Layout.RecessById("ring");
            if (ring != null && ring.R > 0f)
            {
                var d = ring.R * 2f + 16f;
                var zr = new Rect(ring.Centre.x - d * .5f, ring.Centre.y - d * .5f, d, d);
                var zone = new GameObject("RingEcho", typeof(RectTransform), typeof(Image));
                zone.transform.SetParent(Tiles, false);
                var zi = zone.GetComponent<Image>();
                zi.sprite = UiFoundation.WhiteSprite();
                zi.color = new Color(1f, 1f, 1f, 0f);
                zi.raycastTarget = true;
                MenuV6Layout.Place(zi.rectTransform, zr, _stageRect);
                zone.AddComponent<CircleHit>();
                c.RingZone = zi.rectTransform;
            }

            return c;
        }

        Action HandlerFor(string id) => id switch
        {
            "deck" => _h.OpenDeckHub,
            "bag" => _h.ShowInventory,
            "story" => _h.OpenStory,
            "bazaar" => _h.OpenBazaar,
            "view" => _h.OpenFreeView,
            "set" => _h.OpenSettings,
            _ => null
        };

        ArcaneButton Control(Transform parent, string name, string title, Action onClick, EldritchTier tier,
            string blurb, Rect rect, bool round)
        {
            var btn = ArcaneButton.Create(parent, title ?? "", onClick, tier, blurb, null, 22, false, false,
                quiet: true);
            btn.name = name;
            MenuV6Layout.Place(btn.Rect, rect, _stageRect);
            if (round) btn.gameObject.AddComponent<CircleHit>();
            if (string.IsNullOrEmpty(title) && btn.Title != null) btn.Title.gameObject.SetActive(false);
            if (string.IsNullOrEmpty(blurb) && btn.Blurb != null) btn.Blurb.gameObject.SetActive(false);
            return btn;
        }

        // ------------------------------------------------------------------ layers

        readonly Dictionary<string, Image> _static = new();
        readonly Dictionary<string, GameObject> _flip = new();

        Image StaticLayer(Transform parent, Rect parentRect, MenuV6Layout.Layer L, string name = null)
        {
            if (L.Flipbook >= 0) return FlipbookLayer(parent, parentRect, L);
            var sp = !string.IsNullOrEmpty(L.Local) ? MenuV6Art.LocalOr(L.Local, L.File) : MenuV6Art.Sprite(L.File);
            if (sp == null)
            {
                Missing.Add(L.Id);
                return null;
            }

            var go = new GameObject(name ?? L.Id, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            MenuV6Layout.Place(img.rectTransform, L.Rect, parentRect);
            img.sprite = sp;
            img.raycastTarget = false;
            img.preserveAspect = false;
            img.type = L.Nine ? Image.Type.Sliced : Image.Type.Simple;
            img.fillCenter = true;
            // every layer is drawn straight-alpha "over" (UI/Default; no new shader before P5, D3). P1b: the layers that
            // stay static take the Linear-space gain measured against the approved still (MenuV6Linear.LayerGain).
            var gain = MenuV6Linear.LayerGain(L.Id);
            img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(L.AlphaAtRest * gain));
            if (Torch != null)
            {
                if (L.Id == "stone_B" || L.Id == "keystone_over_f1") Torch.DriveAlpha(img, gain);
                else if (L.Id == "torch_pool_b0") Torch.DrivePool(img, gain, TorchPoolSwing);
            }

            if (!string.IsNullOrEmpty(L.Id)) _static[L.Id] = img;
            return img;
        }

        Image FlipbookLayer(Transform parent, Rect parentRect, MenuV6Layout.Layer L)
        {
            if (!Layout.Flipbooks.TryGetValue(L.Flipbook, out var fb) || fb.Frames.Count == 0)
            {
                Missing.Add(L.Id);
                return null;
            }

            var tex = MenuV6Art.Texture(fb.File ?? L.File);
            if (tex == null)
            {
                Missing.Add(L.Id);
                return null;
            }

            var go = new GameObject(L.Id, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>();
            MenuV6Layout.Place(raw.rectTransform, L.Rect, parentRect);
            raw.raycastTarget = false;
            raw.color = new Color(1f, 1f, 1f, Mathf.Clamp01(L.AlphaAtRest));
            var schedule = new EyeBlinkSchedule(L.Flipbook, EyeReveals(L.Flipbook));
            var eye = EyeFlipbook.Attach(raw, tex, fb.Frames, L.Flipbook, schedule);
            if (eye != null) Eyes.Add(eye);
            if (!string.IsNullOrEmpty(L.Id)) _flip[L.Id] = go;
            return null;
        }

        // P1b: where each rig sits in the page's z order, and the P1a stills it replaces (they stay if the rig fails)
        static readonly Dictionary<string, (string rig, bool host)> FxHooks = new()
        {
            { "ring_echo_add", ("ringEcho", true) },
            { "breach_glow_add", ("breachGlow", true) },
            { "door_ring_add", ("door", true) },
            { "motes_add", ("motes", true) },
            { "flame_glow_add", ("flame", false) },
            { "flame_tongues", ("flame", true) },
            { "nut_twinkle_add", ("nut", true) },
            { "keystone_over_f0", ("keystone", false) },
            { "keystone_over_f1", ("keystone", false) },
            { "keystone_add_flick0", ("keystone", true) },
        };

        readonly Dictionary<string, RectTransform> _hosts = new();
        readonly Dictionary<string, List<GameObject>> _replaced = new();
        readonly Dictionary<string, Rect> _hostFrames = new();

        void FxLayer(RectTransform group, MenuV6Layout.Layer L)
        {
            // P2: Nut's sky fracture as a live break (anime_v61_cracks.js:592-602) in the baked plate's place
            if (L.Id == "nut_crack" && LiveNutCrack(group, L)) return;
            // the recesses' hover / press draw after the live pictos, before the Ring echo (anime_menu_v6.js:97)
            if (L.Id == "ring_refract" && !_hosts.ContainsKey("recess")) Host(group, "recess", _stageRect);
            var img = StaticLayer(group, _stageRect, L);
            if (!FxHooks.TryGetValue(L.Id ?? "", out var hook)) return;
            if (img != null) Replaced(hook.rig, img.gameObject);
            if (hook.host) Host(group, hook.rig, _stageRect);
        }

        RectTransform Host(Transform parent, string rig, Rect frame, int sibling = -1)
        {
            var h = new GameObject("Rig_" + rig, typeof(RectTransform)).GetComponent<RectTransform>();
            h.SetParent(parent, false);
            UiTheme.Stretch(h);
            if (sibling >= 0) h.SetSiblingIndex(sibling);
            _hosts[rig] = h;
            _hostFrames[rig] = frame;
            return h;
        }

        void Replaced(string rig, GameObject still)
        {
            if (still == null) return;
            if (!_replaced.TryGetValue(rig, out var list)) _replaced[rig] = list = new List<GameObject>();
            list.Add(still);
        }

        void UiLayer(MenuV6Layout.Layer L, RectTransform slateL, RectTransform slateR, Controls c)
        {
            var id = L.Id ?? "";
            // §7: no source for the artifact uses / pips or the Millennium site → hidden
            if (id.StartsWith("pip_") || id.StartsWith("site_dot")) return;

            if (id.StartsWith("rl_plate_"))
            {
                var owner = RecessForPlate(L.Rect, c);
                if (owner.btn != null) ControlArt(owner.btn, owner.rect, L, "Plate");
                return;
            }

            if (id.StartsWith("ava_ring")) { StaticLayer(slateL, SlateLRect, L); return; }
            if (id.StartsWith("niche_")) { NicheArt(c, L); return; }
            if (id.StartsWith("quit_plate") || id == "icon_door") { QuitArt(c, L); return; }
            if (id == "box_rest" || id == "disk_glyph") { PlaqueArt(c, L); return; }

            if (id == "meter")
            {
                MeterLayer(slateL, SlateLRect, L);
                return;
            }

            if (id == "kuriboh_medal") { CompanionMedal(slateL, SlateLRect, L); return; }

            if (id.StartsWith("xp_fill"))
            {
                XpFill(slateL, SlateLRect, L);
                return;
            }

            if (id == "voice_puzzle")
            {
                var img = StaticLayer(slateR, SlateRRect, L);
                if (img != null) Replaced("voice", img.gameObject);
                Host(slateR, "voice", SlateRRect);
                return;
            }

            var left = L.Rect.center.x < 960f;
            StaticLayer(left ? slateL : slateR, left ? SlateLRect : SlateRRect, L);
        }

        /// <summary>Portrait: every chrome layer sits on the stage (no slates); the controls keep their own art.</summary>
        void UiLayerPort(MenuV6Layout.Layer L, RectTransform chrome, Controls c)
        {
            var id = L.Id ?? "";
            if (id.StartsWith("pip_") || id.StartsWith("site_dot")) return;
            if (id.StartsWith("rl_plate_"))
            {
                var owner = RecessForPlate(L.Rect, c);
                if (owner.btn != null) ControlArt(owner.btn, owner.rect, L, "Plate");
                return;
            }

            if (id.StartsWith("niche_")) { NicheArt(c, L); return; }
            if (id.StartsWith("quit_plate") || id == "icon_door") { QuitArt(c, L); return; }
            if (id == "box_rest" || id == "disk_glyph") { PlaqueArt(c, L); return; }
            if (id == "meter")
            {
                // the season meter is the voice in portrait (menu.js:488): its rays go under it
                Host(chrome, "voice", _stageRect);
                MeterLayer(chrome, _stageRect, L);
                return;
            }

            if (id == "kuriboh_medal") { CompanionMedal(chrome, _stageRect, L); return; }
            if (id.StartsWith("xp_fill")) { XpFill(chrome, _stageRect, L); return; }
            StaticLayer(chrome, _stageRect, L);
        }

        void NicheArt(Controls c, MenuV6Layout.Layer L)
        {
            var img = ControlArt(c.MapBack, c.MapRect, L);
            if (L.Id == "niche_still")
            {
                if (img != null) Replaced("niche", img.gameObject);
                ArtHost(c.MapBack, c.MapRect, "niche");
            }
            else if (L.Id != null && L.Id.StartsWith("niche_frame"))
            {
                _nicheFrame = img;
                ArtHost(c.MapBack, c.MapRect, "nicheFrameHover");
            }
        }

        void QuitArt(Controls c, MenuV6Layout.Layer L)
        {
            var img = ControlArt(c.Quit, c.QuitRect, L);
            if (L.Id != null && L.Id.StartsWith("quit_plate"))
            {
                _quitPlate = img;
                ArtHost(c.Quit, c.QuitRect, "quitHover");
            }
        }

        void PlaqueArt(Controls c, MenuV6Layout.Layer L)
        {
            var img = ControlArt(c.DeckPlaque, c.PlaqueRect, L);
            if (L.Id == "box_rest")
            {
                if (img != null) Replaced("box", img.gameObject);
                ArtHost(c.DeckPlaque, c.PlaqueRect, "box");
            }
            else if (L.Id == "disk_glyph" && img != null)
            {
                _plaqueNormal.Add(img);
            }
        }

        Image _nicheFrame, _quitPlate;
        readonly List<Graphic> _plaqueNormal = new();

        void MeterLayer(RectTransform parent, Rect parentRect, MenuV6Layout.Layer L)
        {
            var pick = MeterFile(L);
            var m = new MenuV6Layout.Layer
            {
                Id = L.Id, Z = L.Z, Group = L.Group, File = pick, Rect = L.Rect, Ppu = L.Ppu, Blend = L.Blend,
                AlphaAtRest = L.AlphaAtRest
            };
            if (Data.ChapterIndex > 0) StaticLayer(parent, parentRect, m);
        }

        void XpFill(RectTransform parent, Rect parentRect, MenuV6Layout.Layer L)
        {
            var img = StaticLayer(parent, parentRect, L);
            if (img == null) return;
            // the page's fill is a sample (62 %); the bar math is the Overworld's (XpProgress01, min 2 %)
            var track = Layout.LayerById("xp_track_0");
            var inner = track != null ? track.Rect.width - 2f : L.Rect.width;
            var r = L.Rect;
            r.width = Mathf.Max(inner * Mathf.Clamp(Data.Xp01, 0.02f, 1f), 3f);
            MenuV6Layout.Place(img.rectTransform, r, parentRect);
        }

        readonly Dictionary<Transform, int> _artCount = new();

        /// <summary>A control's own chrome (label plate, QUIT plate, niche, box): drawn under the control's labels, in
        /// the page's z order.</summary>
        Image ControlArt(ArcaneButton owner, Rect ownerRect, MenuV6Layout.Layer L, string name = null)
        {
            if (owner == null) return null;
            var img = StaticLayer(owner.transform, ownerRect, L, name);
            if (img == null) return null;
            _artCount.TryGetValue(owner.transform, out var k);
            img.transform.SetSiblingIndex(k);
            _artCount[owner.transform] = k + 1;
            return img;
        }

        /// <summary>A rig host inside a control, at the next chrome index (so the control's labels stay on top).</summary>
        void ArtHost(ArcaneButton owner, Rect ownerRect, string rig)
        {
            if (owner == null) return;
            _artCount.TryGetValue(owner.transform, out var k);
            Host(owner.transform, rig, ownerRect, k);
            _artCount[owner.transform] = k + 1;
        }

        (ArcaneButton btn, Rect rect) RecessForPlate(Rect plate, Controls c)
        {
            foreach (var kv in c.Recess)
            {
                var rec = kv.Value.rec;
                if (rec == null || kv.Value.btn == null) continue;
                if (plate.Contains(rec.LabelRect.center)) return (kv.Value.btn, kv.Value.rect);
            }

            return (null, Rect.zero);
        }

        string MeterFile(MenuV6Layout.Layer L)
        {
            var n = Mathf.Clamp(Data.MeterPieces, 0, 8);
            if (L.Variants != null)
                foreach (var v in L.Variants)
                    if (v.EndsWith("_n" + n))
                        return v;
            return L.File;
        }

        void CompanionMedal(RectTransform parent, Rect parentRect, MenuV6Layout.Layer L)
        {
            // §7: the team's own project icon when the player has a team, else his Kuriboh render + "· stand-in"
            Sprite team = null;
            if (Data.Team != KuribohTeam.None)
            {
                try { team = ImagineAssets.IconTeam(Data.Team); }
                catch { team = null; }
            }

            if (team == null)
            {
                StaticLayer(parent, parentRect, L);
                return;
            }

            var mask = new GameObject("TeamMedal", typeof(RectTransform), typeof(Image), typeof(Mask));
            mask.transform.SetParent(parent, false);
            var mimg = mask.GetComponent<Image>();
            MenuV6Layout.Place(mimg.rectTransform, L.Rect, parentRect);
            mimg.sprite = Disc();
            mimg.raycastTarget = false;
            mask.GetComponent<Mask>().showMaskGraphic = false;
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(mask.transform, false);
            var iimg = icon.GetComponent<Image>();
            iimg.sprite = team;
            iimg.preserveAspect = true;
            iimg.raycastTarget = false;
            // the tile's frame and corner ticks fall outside the disc: show its centre ~72 %
            var irt = iimg.rectTransform;
            irt.anchorMin = new Vector2(-0.2f, -0.2f);
            irt.anchorMax = new Vector2(1.2f, 1.2f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            _teamIcon = true;
        }

        bool _teamIcon;

        // ------------------------------------------------------------------ slate texts (§7)

        Text T(Transform parent, Rect parentRect, string name, string sel, string key, string shown)
        {
            var slot = Layout.Text(sel, key);
            if (slot == null)
            {
                Missing.Add("text:" + key);
                return null;
            }

            return TextFx.Label(parent, name, slot, shown, parentRect);
        }

        /// <summary>A run of texts on one line, each in its own recorded style, placed left to right by measured width
        /// (the page's inline spans).</summary>
        List<Text> Run(Transform parent, Rect parentRect, string name, Rect line, params (string sel, string key, string shown, float gapBefore)[] parts)
        {
            var made = new List<Text>();
            var x = line.x;
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (string.IsNullOrEmpty(p.shown)) continue;
                var slot = Layout.Text(p.sel, p.key);
                if (slot == null)
                {
                    Missing.Add("text:" + p.key);
                    continue;
                }

                x += p.gapBefore;
                var t = TextFx.Label(parent, name + "_" + i, slot, p.shown, parentRect);
                if (t == null) continue;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.alignment = TextAnchor.MiddleLeft;
                var ls = t.GetComponent<LetterSpacingEffect>();
                var w = t.preferredWidth + (ls != null ? ls.Spacing * t.text.Length : 0f);
                MenuV6Layout.Place(t.rectTransform, new Rect(x, line.y, w + 2f, line.height), parentRect);
                x += w;
                made.Add(t);
            }

            return made;
        }

        /// <summary>A single line kept inside its column: shrunk to <paramref name="minSize"/>, then cut with an ellipsis
        /// (P1a capture: a long player name ran into the crack art at the slate's right edge).</summary>
        static void KeepInColumn(Text t, float maxWidth, int minSize)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            TextFx.FitWidth(t, maxWidth, minSize);
            var ls = t.GetComponent<LetterSpacingEffect>();
            float W() => t.preferredWidth + (ls != null ? ls.Spacing * t.text.Length : 0f);
            if (W() <= maxWidth) return;
            var s = t.text;
            for (var n = s.Length - 1; n > 1; n--)
            {
                t.text = s.Substring(0, n).TrimEnd() + "…";
                if (W() <= maxWidth) return;
            }
        }

        void BuildSlateTexts(RectTransform slateL, RectTransform slateR, Controls c)
        {
            var d = Data;
            var L = SlateLRect;
            var R = SlateRRect;

            // DUELIST: the player's name (Marcellus 50, tracked), on one line inside the page's own column (the slot,
            // x 224-452: the crack chunk at the slate's top-right corner starts at x ~470 on this row)
            var name = T(slateL, L, "Name", "n > span.n", "DUELIST", d.Name);
            if (name != null)
            {
                var slot = Layout.Text("n > span.n", "DUELIST");
                var r = slot.Rect;
                MenuV6Layout.Place(name.rectTransform, r, L);
                KeepInColumn(name, r.width, 24);
            }

            // Lv n · title (the page's "sample profile" marks sample data; the real line is the avatar's title)
            var meta = Layout.Text("meta > div.meta", "·");
            var lineRect = meta != null ? meta.Rect : new Rect(224f, 274f, 228f, 36f);
            Run(slateL, L, "Meta", lineRect,
                ("meta > b", "Lv 21", "Lv " + d.Level, 0f),
                ("meta > div.meta", "·", string.IsNullOrEmpty(d.Title) ? null : "·  " + d.Title, 6f));

            // COMPANION · team (D15 gameplay data stays: the soul gem sits on the avatar ring)
            T(slateL, L, "CompanionCap", "k > div.k", "COMPANION", "COMPANION");
            var v = Layout.Text("v > div.v", "team Kuriboh ·");
            var vRect = v != null ? v.Rect : new Rect(178f, 349.5f, 125f, 23f);
            if (_teamIcon)
                Run(slateL, L, "Companion", vRect, ("v > div.v", "team Kuriboh ·", "team " + d.TeamName, 0f));
            else
                Run(slateL, L, "Companion", vRect, ("v > div.v", "team Kuriboh ·", "team " + d.TeamName + " ·", 0f),
                    ("v > i", "stand-in", "stand-in", 3f));

            // the artifact medal: art + name only (no equipped-artifact field: uses and pips hidden, §7)
            T(slateL, L, "ArtifactCap", "k > div.k", "MILLENNIUM PUZZLE", "MILLENNIUM PUZZLE");

            // season caption through the shared SectionCap (hub_overlay_chrome_check), restyled here (§3 row)
            SeasonCap(slateL, L, "cap > div.cap", "Season 1 · Duelist Kingdom", new Rect(122f, 408f, 382f, 17f));

            // chapter n of the current gate, its title, cleared stages n / total (§7, §11 F19)
            if (d.ChapterIndex > 0)
            {
                var ch = Layout.Text("t > div.t", "Chapter");
                var chRect = ch != null ? ch.Rect : new Rect(234f, 449.5f, 214f, 27f);
                Run(slateL, L, "Chapter", chRect,
                    ("t > div.t", "Chapter", "Chapter", 0f),
                    ("pn > b.pn", "3", d.ChapterIndex.ToString(), 6f));
                var sub = string.IsNullOrEmpty(d.ChapterTitle) ? "" : d.ChapterTitle + "  ·  ";
                T(slateL, L, "ChapterSub", "s > div.s", "chapter titles: owner to name",
                    sub + d.StagesCleared + " / " + d.StagesTotal);
            }

            MapTexts(c, "s > div.s", "Battle City · night near");
            PlaqueTexts(c, "k > div.k", "THE BOX’S HIEROGLYPHS · SUGOROKU’S READING", BoxReadingLand);

            // NEWS: Tears from the pin count; the Bazaar's tablet packs (SystemsSheets rows)
            T(slateR, R, "NewsCap", "cap > div.cap", "News", "News");
            T(slateR, R, "News1", "t > div.t", "A Tear stirs over", "Tears open in Battle City");
            T(slateR, R, "News1Sub", "s > div.s", "3 spirits · 1 boss · 0.4 mi ·", d.TearPins + " on the map");
            T(slateR, R, "News2", "t > div.t", "Tablet packs at the Bazaar", "Tablet packs at the Bazaar");
            T(slateR, R, "News2Sub", "s > div.s", "LOB · MRD · SRL · 1,000 SE of a set · one 10-card pack", d.BazaarLine);

            // THE PHARAOH'S VOICE: the one sourced line (Blueprint l.29; menu.js:61), broken where the page breaks it
            // (rigs_land.json voice.pageLines: 2 lines in the .q box; P1a's wrap in the 254 px span box made 3)
            T(slateR, R, "VoiceCap", "cap > div.cap", "The Pharaoh's voice", "The Pharaoh's voice");
            VoiceLine(slateR, R);

            // EVENTS: the first unclaimed daily quest and what it pays
            T(slateR, R, "EventsCap", "cap > div.cap", "Events", "Events");
            if (!string.IsNullOrEmpty(d.QuestTitle))
            {
                T(slateR, R, "Event", "t > div.t", "Kaiba Corp tower · Season 1 tournament", d.QuestTitle);
                T(slateR, R, "EventSub", "s > div.s", "Level 20+ · starchips · Referobot referees", d.QuestReward);
            }

            // QUIT: the label stays; its sub-line is "fold the disk" (D14)
            if (c.Quit != null)
            {
                ApplyTo(c.Quit.Title, c.QuitRect, "t > div.t", "QUIT", "QUIT");
                ApplyTo(c.Quit.Blurb, c.QuitRect, "s > div.s", "close the game", "fold the disk");
            }

            // the avatar in the gold ring: the live portrait, the old tap, the shared LiftPlate, the soul gem
            Avatar(slateL, L, AvatarDiscLand, SoulGemLand);
        }

        /// <summary>WORLD MAP niche: name, place, the real Tear pin count (the Millennium site and locators have no source).</summary>
        void MapTexts(Controls c, string placeSel, string placeKey)
        {
            var map = c.MapBack;
            if (map == null) return;
            ApplyTo(map.Title, c.MapRect, "t > div.t", "WORLD MAP", "WORLD MAP");
            var blurb = map.Blurb;
            var s = Layout.Text(placeSel, placeKey);
            if (blurb == null || s == null) return;
            TextFx.Apply(blurb, s, "Battle City · night");
            var line1 = s.Rect;
            line1.height = s.LineHeight > 0f ? s.LineHeight : line1.height * 0.5f;
            MenuV6Layout.Place(blurb.rectTransform, line1, c.MapRect);
            blurb.alignment = TextAnchor.MiddleLeft;
            var line2 = line1;
            line2.y += line1.height;
            Run(map.transform, c.MapRect, "Tears", line2,
                ("s > b", "3 Tears", Data.TearPins + " Tears", 0f),
                (placeSel, placeKey, "near", 5f));
        }

        /// <summary>The Spirit Dueler plaque: deck name, main count / 40, the format's LP; on hover Sugoroku's reading
        /// of the box replaces them (PlaqueTextRig).</summary>
        void PlaqueTexts(Controls c, string readingSel, string readingKey, string readingShown)
        {
            var plaque = c.DeckPlaque;
            if (plaque == null) return;
            var d = Data;
            var pr = c.PlaqueRect;
            var k = Layout.Text("k > div.k", "SPIRIT DUELER");
            if (k != null)
            {
                // the disk glyph sits at the start of this line (its size + the page's gap)
                var t = TextFx.Label(plaque.transform, "Kicker", k, "SPIRIT DUELER", pr);
                var glyph = Layout.LayerById("disk_glyph");
                var shift = glyph != null ? glyph.Rect.xMax - k.Rect.x + 8f : 30f;
                var kr = k.Rect;
                kr.x += shift;
                kr.width -= shift;
                if (t != null)
                {
                    MenuV6Layout.Place(t.rectTransform, kr, pr);
                    _plaqueNormal.Add(t);
                }
            }

            if (!string.IsNullOrEmpty(d.DeckName))
            {
                var dn = T(plaque.transform, pr, "DeckName", "t > div.t", "Junkuriboh Starter", d.DeckName);
                if (dn != null) _plaqueNormal.Add(dn);
            }

            var sline = Layout.Text("s > div.s", "· LP");
            var sRect = sline != null ? sline.Rect : new Rect(284f, 631f, 220f, 32f);
            foreach (var t in Run(plaque.transform, pr, "DeckLine", sRect,
                         ("s > b", "40/40", MenuV6Data.DeckCountLine(d), 0f),
                         ("s > div.s", "· LP", MenuV6Data.DeckCountLine(d) != null && d.StartingLp > 0 ? "·" : null, 5f),
                         ("s > b", "8000", d.StartingLp > 0 ? d.StartingLp.ToString() : null, 5f),
                         ("s > div.s", "· LP", d.StartingLp > 0 ? "LP" : null, 4f)))
                _plaqueNormal.Add(t);

            // the approved lore line (relics.js:428 BOX_INSCRIPTION; §7 "shown"), at the page's own line breaks
            var rd = Layout.Text(readingSel, readingKey);
            if (rd != null)
            {
                var t = TextFx.Label(plaque.transform, "BoxReading", Opaque(rd), readingShown, pr);
                if (t != null)
                {
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.alignment = TextAnchor.UpperLeft;
                    _plaqueIns.Add(t);
                }
            }

            var q = Layout.Text("q > div.q", "“The one who solves me shall gain the powers and knowledge of darkness.”");
            if (q != null)
            {
                var t = TextFx.Label(plaque.transform, "BoxInscription", Opaque(q), BoxQuote, pr);
                if (t != null)
                {
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    t.alignment = TextAnchor.UpperLeft;
                    _plaqueIns.Add(t);
                }
            }
        }

        readonly List<Graphic> _plaqueIns = new();

        /// <summary>A copy of a slot recorded at opacity 0 (shown on hover only) at its lit opacity 1.</summary>
        static MenuV6Layout.TextSlot Opaque(MenuV6Layout.TextSlot s) => new()
        {
            Sel = s.Sel, Text = s.Text, Rect = s.Rect, Opacity = 1f, Family = s.Family, Weight = s.Weight, Size = s.Size,
            Italic = s.Italic, Color = s.Color, LetterSpacing = s.LetterSpacing, Upper = s.Upper, Align = s.Align,
            LineHeight = s.LineHeight, Shadow = s.Shadow, ShadowColor = s.ShadowColor, ShadowOffset = s.ShadowOffset,
            ShadowBlur = s.ShadowBlur, NoWrap = false, BgImage = s.BgImage, Filter = s.Filter
        };

        Text _voiceLine;

        /// <summary>THE PHARAOH'S VOICE line at the page's own breaks: one Text, no wrap, the page's line height.</summary>
        void VoiceLine(RectTransform parent, Rect parentRect)
        {
            const string line = "“Duelist from beyond—interrupt this shadow!”";
            var slot = Layout.Text("q > span", line);
            if (slot == null)
            {
                Missing.Add("text:voice");
                return;
            }

            var shown = line;
            var box = slot.Rect;
            var rig = RigsJson(Kind);
            var voice = RJ.D(RJ.D(rig, "rigs"), "voice");
            var pl = RJ.D(voice, "pageLines");
            var lines = RJ.L(pl, "lines");
            if (lines != null && lines.Count > 0)
            {
                var parts = new List<string>();
                foreach (var o in lines)
                {
                    var s = RJ.S(o, "text");
                    if (!string.IsNullOrEmpty(s)) parts.Add(s);
                }

                if (parts.Count > 0 && string.Concat(parts) == line) shown = string.Join("\n", parts);
                var b = RJ.D(pl, "box");
                if (b != null)
                {
                    // the line boxes start (39 - 33.75) / 2 above the .q box; the Text's lines are the .q's line height
                    box = new Rect((float)RJ.N(b, "x"), (float)RJ.N(b, "y"), (float)RJ.N(b, "w"), (float)RJ.N(b, "h"));
                }

                // P2 (the P1b landing review: the voice block sat ~3 px low): the captures' row profiles match the stills best
                // 3 stage px up in both layouts (land 7.1 vs 8.6 levels at 0, port 5.1 vs 9.1): legacy Text puts the first
                // line's ascent lower than the CSS line box
                box.y -= VoiceRaise;
            }

            var t = TextFx.Label(parent, "Voice", slot, shown, parentRect);
            if (t == null) return;
            MenuV6Layout.Place(t.rectTransform, box, parentRect);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignment = TextAnchor.UpperLeft;
            if (t.font != null && t.font.lineHeight > 0 && slot.LineHeight > 0f)
            {
                var natural = t.font.lineHeight * t.fontSize / (float)Mathf.Max(1, t.font.fontSize);
                t.lineSpacing = natural > 0f ? Mathf.Clamp(slot.LineHeight / natural, .5f, 2f) : 1f;
            }

            _voiceLine = t;
        }

        void ApplyTo(Text t, Rect parentRect, string sel, string key, string shown)
        {
            if (t == null) return;
            var slot = Layout.Text(sel, key);
            if (slot == null)
            {
                Missing.Add("text:" + key);
                return;
            }

            TextFx.Apply(t, slot, shown);
            MenuV6Layout.Place(t.rectTransform, slot.Rect, parentRect);
        }

        void SeasonCap(RectTransform parent, Rect parentRect, string sel, string key, Rect fallback)
        {
            var slot = Layout.Text(sel, key);
            var r = slot != null ? slot.Rect : fallback;
            var host = Node(parent, "SeasonCap", r, parentRect);
            // full-width anchors: SectionCap adds its glyph strip only when the gutter is real, so none is made here
            var cap = HubChrome.SectionCap(host, "SysCap", Data.SeasonCaption, WrldzAnimePalette.Ochre, 0f, 0f, 1f, 1f);
            var tick = host.Find("SysCapTick");
            if (tick != null) DestroyImmediate(tick.gameObject);
            if (cap != null && slot != null)
            {
                TextFx.Apply(cap, slot, Data.SeasonCaption);
                UiTheme.Stretch(cap.rectTransform);
            }
        }

        void Avatar(RectTransform parent, Rect parentRect, Rect disc, Rect gem)
        {
            var face = new GameObject("FaceHost", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(Button));
            face.transform.SetParent(parent, false);
            var fimg = face.GetComponent<Image>();
            MenuV6Layout.Place(fimg.rectTransform, disc, parentRect);
            fimg.sprite = Disc();
            fimg.color = Color.white;
            fimg.raycastTarget = true;
            face.GetComponent<Mask>().showMaskGraphic = false;
            // the shared hairline lift (hub_overlay_chrome_check): on the hidden mask graphic it changes no pixel of
            // the approved ring art, and the avatar frame still goes through HubChrome
            HubChrome.LiftPlate(fimg, EldritchPalette.EdgeFor(EldritchTier.Tertiary));
            try
            {
                var portrait = AvatarPortraitView.CreateFullBodyFill(face.transform, hideBackground: true, badgeCrop: true);
                portrait.Apply(Data.Look ?? AvatarAppearance.Default());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] avatar portrait: " + ex.Message);
            }

            var btn = face.GetComponent<Button>();
            btn.targetGraphic = fimg;
            btn.transition = Selectable.Transition.None;
            if (_h.OpenAvatarMenus != null) btn.onClick.AddListener(() => _h.OpenAvatarMenus());

            var soul = SoulBadgeView.Create(parent);
            MenuV6Layout.Place(soul.GetComponent<RectTransform>(), gem, parentRect);
            soul.Bind(Data.Progress);
            var count = soul.transform.Find("Count");
            var ct = count != null ? count.GetComponent<Text>() : null;
            if (ct != null)
            {
                ct.font = TextFx.FontFor("Cairo", 700, out _);
                ct.fontSize = Land ? 11 : 15;
                ct.fontStyle = FontStyle.Normal;
            }
        }

        // ------------------------------------------------------------------ portrait (P1b first cut, SPEC §9.4)

        /// <summary>
        /// The phone plan's texts (layout_port.json), real data as in landscape (§7): the identity in the sky register,
        /// the season and chapter on the Stone's header with the voice line, the deck row and WORLD MAP under the Stone,
        /// NEWS as one band, EVENTS and QUIT at the bottom. The first cut is checked for "nothing off-screen, nothing
        /// overlapping"; parity is P5. Every text stays inside its slot's column.
        /// </summary>
        void BuildPortraitTexts(RectTransform chrome, Controls c)
        {
            var d = Data;
            var S = _stageRect;

            var name = T(chrome, S, "Name", "n > span.n", "DUELIST", d.Name);
            if (name != null)
            {
                var slot = Layout.Text("n > span.n", "DUELIST");
                // the column runs to the companion medal (x 799); the slot is the mockup word's own width
                var r = slot.Rect;
                var medal = Layout.LayerById("medal_ring_0");
                r.width = (medal != null ? medal.Rect.x - 24f : r.xMax) - r.x;
                MenuV6Layout.Place(name.rectTransform, r, S);
                KeepInColumn(name, r.width, 30);
            }

            var meta = Layout.Text("meta > div.meta", "·");
            var lineRect = meta != null ? meta.Rect : new Rect(186f, 130f, 300f, 49f);
            Run(chrome, S, "Meta", lineRect,
                ("meta > b", "Lv 21", "Lv " + d.Level, 0f),
                ("meta > div.meta", "·", string.IsNullOrEmpty(d.Title) ? null : "·  " + d.Title, 6f));

            // COMPANION · stand-in under the medal; with the team's own icon: COMPANION · the team's name, kept in its column
            var cv = Layout.Text("v > div.v", "COMPANION ·");
            if (cv != null)
            {
                var col = cv.Rect;
                var gem = Layout.LayerById("medal_ring_pz_0");
                var maxW = (gem != null ? gem.Rect.x - 8f : col.xMax) - col.x;
                List<Text> made;
                if (_teamIcon)
                    made = Run(chrome, S, "Companion", col, ("v > div.v", "COMPANION ·", "COMPANION · " + d.TeamName, 0f));
                else
                    made = Run(chrome, S, "Companion", col, ("v > div.v", "COMPANION ·", "COMPANION ·", 0f),
                        ("v > i", "stand-in", "stand-in", 4f));
                if (made.Count == 1) KeepInColumn(made[0], maxW, 12);
            }

            // ✦ SEASON 1 · DUELIST KINGDOM ✦ (the ✦ drawn as the page's cel sparkle: the faces have no U+2726)
            var pcap = Layout.Text("pcap > span", "✦ Season 1 · Duelist Kingdom ✦");
            if (pcap != null)
            {
                var host = Node(chrome, "SeasonCap", pcap.Rect, S);
                var cap = HubChrome.SectionCap(host, "SysCap", d.SeasonCaption, WrldzAnimePalette.Ochre, 0f, 0f, 1f, 1f);
                var tick = host.Find("SysCapTick");
                if (tick != null) DestroyImmediate(tick.gameObject);
                if (cap != null)
                {
                    TextFx.Apply(cap, pcap, d.SeasonCaption);
                    UiTheme.Stretch(cap.rectTransform);
                    cap.alignment = TextAnchor.MiddleCenter;
                    cap.horizontalOverflow = HorizontalWrapMode.Overflow;
                    var ls = cap.GetComponent<LetterSpacingEffect>();
                    var w = cap.preferredWidth + (ls != null ? ls.Spacing * cap.text.Length : 0f);
                    var marks = RigShapes.Create(host, "SeasonMarks", pcap.Rect).Layer;
                    marks.Clear();
                    var cy = pcap.Rect.center.y;
                    var half = w * .5f;
                    var sz = pcap.Size * .3f;
                    marks.Sparkle(pcap.Rect.center.x - half - sz * 2.2f, cy, sz, cap.color, 0);
                    marks.Sparkle(pcap.Rect.center.x + half + sz * 1.6f, cy, sz, cap.color, 0);
                    marks.Commit();
                }
            }

            if (d.ChapterIndex > 0)
            {
                var em = Layout.Text("pcap > em", "Chapter");
                var pn = Layout.Text("pn > b.pn", "3");
                if (em != null)
                {
                    var made = Run(chrome, S, "Chapter", em.Rect, ("pcap > em", "Chapter", "Chapter", 0f),
                        ("pn > b.pn", "3", d.ChapterIndex.ToString(), 6f));
                    // centred as the page centres the line
                    var total = 0f;
                    foreach (var t in made)
                    {
                        var ls = t.GetComponent<LetterSpacingEffect>();
                        total += t.preferredWidth + (ls != null ? ls.Spacing * t.text.Length : 0f);
                    }

                    total += made.Count > 1 ? 6f : 0f;
                    var x0 = (pn != null ? (em.Rect.x + pn.Rect.xMax) * .5f : em.Rect.center.x) - total * .5f;
                    var x = x0;
                    for (var i = 0; i < made.Count; i++)
                    {
                        var t = made[i];
                        var ls = t.GetComponent<LetterSpacingEffect>();
                        var tw = t.preferredWidth + (ls != null ? ls.Spacing * t.text.Length : 0f);
                        if (i > 0) x += 6f;
                        MenuV6Layout.Place(t.rectTransform, new Rect(x, em.Rect.y, tw + 2f, em.Rect.height), S);
                        x += tw;
                    }
                }
            }

            VoiceLine(chrome, S);
            MapTexts(c, "s > div.s", "Battle City · night near");
            PlaqueTexts(c, "k > div.k", "SUGOROKU’S READING", "SUGOROKU’S READING");

            // NEWS: one band — the Tear pin count (the mockup's Domino Park line is a sample, §7)
            T(chrome, S, "NewsCap", "k > span.k", "NEWS", "NEWS");
            var nv = T(chrome, S, "News1", "v > span.v", "A Tear stirs over · 3 spirits · 1 boss",
                "Tears open in Battle City · " + d.TearPins + " on the map");
            if (nv != null)
            {
                var go = Layout.Text("go > span.go", "›");
                var nr = Layout.Text("v > span.v", "A Tear stirs over · 3 spirits · 1 boss").Rect;
                if (go != null) nr.width = go.Rect.x - 16f - nr.x;
                MenuV6Layout.Place(nv.rectTransform, nr, S);
                KeepInColumn(nv, nr.width, 18);
            }

            T(chrome, S, "NewsGo", "go > span.go", "›", "›");

            // EVENTS: the first unclaimed daily quest and what it pays, left of QUIT
            if (!string.IsNullOrEmpty(d.QuestTitle))
            {
                var limit = c.QuitRect.x - 24f;
                var e1 = T(chrome, S, "Event", "t > div.t", "Kaiba Corp tower · Season 1", d.QuestTitle);
                var e2 = T(chrome, S, "EventSub", "s > div.s", "Level 20+ · starchips", d.QuestReward);
                foreach (var e in new[] { e1, e2 })
                {
                    if (e == null) continue;
                    var r = e.rectTransform;
                    var slot = e == e1 ? Layout.Text("t > div.t", "Kaiba Corp tower · Season 1") : Layout.Text("s > div.s", "Level 20+ · starchips");
                    var rr = slot.Rect;
                    rr.width = limit - rr.x;
                    MenuV6Layout.Place(r, rr, S);
                    KeepInColumn(e, rr.width, 16);
                }
            }

            if (c.Quit != null) ApplyTo(c.Quit.Title, c.QuitRect, "t > div.t", "QUIT", "QUIT");

            // the avatar: the disc and the soul gem scale with the portrait's own ring (measured on the landscape ring)
            var ring = Layout.LayerById("ava_ring_0");
            var rr0 = ring != null ? ring.Rect : new Rect(49.5f, 68.5f, 122f, 130f);
            var k = rr0.width / AvaRingLand.width;
            var disc = new Rect(rr0.x + (AvatarDiscLand.x - AvaRingLand.x) * k, rr0.y + (AvatarDiscLand.y - AvaRingLand.y) * k,
                AvatarDiscLand.width * k, AvatarDiscLand.height * k);
            var gem2 = new Rect(rr0.x + (SoulGemLand.x - AvaRingLand.x) * k, rr0.y + (SoulGemLand.y - AvaRingLand.y) * k,
                SoulGemLand.width * k, SoulGemLand.height * k);
            Avatar(chrome, S, disc, gem2);
        }

        // ------------------------------------------------------------------ control texts

        void BuildControlTexts(Controls c)
        {
            // the door: DUEL in three hard gold bands with the hard drop; the bezel's DECK n / LP readouts
            if (c.Hero != null)
            {
                var slot = Layout.Text("t > div.t", "DUEL");
                if (c.Hero.Title != null && slot != null)
                {
                    TextFx.Apply(c.Hero.Title, slot, "DUEL");
                    MenuV6Layout.Place(c.Hero.Title.rectTransform, slot.Rect, c.HeroRect);
                    var bands = MenuV6Layout.Css.Bands(slot.BgImage);
                    if (bands.Count == 0)
                        bands = new List<(Color, float, float)>
                        {
                            (WrldzAnimePalette.DuelBandTop, 0f, 0.34f),
                            (WrldzAnimePalette.DuelBandMid, 0.34f, 0.72f),
                            (WrldzAnimePalette.DuelBandLow, 0.72f, 1f)
                        };
                    TextFx.Bands(c.Hero.Title, bands);
                }

                var hero = c.Hero.transform;
                T(hero, c.HeroRect, "DeckCap", "bez dk > i", "DECK", "DECK");
                if (Data.DeckMain >= 0) T(hero, c.HeroRect, "DeckCount", "bez dk > b", "40", Data.DeckMain.ToString());
                if (Data.StartingLp > 0) T(hero, c.HeroRect, "Lp", "bez lp > b", "8000", Data.StartingLp.ToString());
                T(hero, c.HeroRect, "LpCap", "bez lp > i", "LP", "LP");
            }

            foreach (var r in RecessTable)
            {
                if (!c.Recess.TryGetValue(r.id, out var e) || e.btn == null) continue;
                ApplyTo(e.btn.Title, e.rect, "t > div.t", r.label, r.label);
                ApplyTo(e.btn.Blurb, e.rect, "s > div.s", r.sub, r.sub);
            }
        }

        void BuildPlaceholder(RectTransform ph5)
        {
            // D17: the owner's own rule: anything still our drawing of a canon asset is marked on screen
            var slot = Layout.Text("ph5 > div.ph5",
                "◇ placeholder art · the Stone · Puzzle body + pieces · Ring · Rod · awaiting existing models");
            if (slot == null) return;
            // the ◇ (Cairo has no U+25C7): 9 px at the landscape's 11 px caption, 9 px in from the strip, centred on it
            var f = slot.Size > 0f ? slot.Size / 11f : 1f;
            var dsz = 9f * f;
            var diamond = new Rect(slot.Rect.x + 9f * f, slot.Rect.center.y - dsz * .5f + .5f * f, dsz, dsz);
            var t = TextFx.Label(ph5, "PlaceholderNote", slot,
                "placeholder art · the Stone · Puzzle body + pieces · Ring · Rod · awaiting existing models", _stageRect);
            if (t != null)
            {
                var r = slot.Rect;
                r.width -= diamond.xMax + 4f * f - r.x;
                r.x = diamond.xMax + 4f * f;
                MenuV6Layout.Place(t.rectTransform, r, _stageRect);
            }

            var dia = new GameObject("PlaceholderMark", typeof(RectTransform), typeof(HollowDiamond));
            dia.transform.SetParent(ph5, false);
            var g = dia.GetComponent<HollowDiamond>();
            g.color = t != null ? t.color : slot.Color;
            g.raycastTarget = false;
            MenuV6Layout.Place(g.rectTransform, diamond, _stageRect);
        }

        MenuV6Layout.TextSlot StatusSlot(Rect rect)
        {
            // the old hub's status line (toasts), in the slates' sub-line face; not part of the approved stills (empty)
            var src = Layout.Text("s > div.s", "LOB · MRD · SRL · 1,000 SE of a set · one 10-card pack") ??
                      Layout.Text("s > div.s", "Level 20+ · starchips");
            if (src == null) return null;
            return new MenuV6Layout.TextSlot
            {
                Sel = src.Sel, Text = "", Rect = rect, Opacity = 1f, Family = src.Family, Weight = src.Weight,
                Size = src.Size, Color = src.Color, Align = "center", NoWrap = true
            };
        }

        // ------------------------------------------------------------------ the idle rigs (P1b)

        RigContext _ctx;
        double[] _noise;

        RigContext NewRigContext(Controls c)
        {
            var root = RigsJson(Kind);
            var ctx = new RigContext
            {
                Kind = Kind, Rigs = RJ.D(root, "rigs"), K = RJ.N(RJ.D(root, "stage"), "k", Land ? 1 : 1.4), Data = Data,
                Gfx = new UguiGfx(), RealTime = () => Time.unscaledTimeAsDouble
            };
            ctx.Door = RigPointer.On(c.Hero);
            ctx.Map = RigPointer.On(c.MapBack);
            ctx.Quit = RigPointer.On(c.Quit);
            ctx.Plaque = RigPointer.On(c.DeckPlaque);
            if (c.RingZone != null) ctx.Ring = RigPointer.On(c.RingZone);
            foreach (var kv in c.Recess)
                if (kv.Value.btn != null)
                    ctx.Recess[kv.Key] = RigPointer.On(kv.Value.btn);
            return ctx;
        }

        Dictionary<string, object> _rigsJson;
        bool _rigsRead;

        /// <summary>Data/rigs_land.json or rigs_port.json, read once per build (null when the P1b art is not installed).</summary>
        Dictionary<string, object> RigsJson(MenuLayoutKind kind)
        {
            if (_rigsRead) return _rigsJson;
            _rigsRead = true;
            try
            {
                var ta = MenuV6Art.Data(kind == MenuLayoutKind.Landscape ? "rigs_land" : "rigs_port");
                _rigsJson = ta != null ? MenuV6Json.Parse(ta.text) as Dictionary<string, object> : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] rigs json: " + ex.Message);
                _rigsJson = null;
            }

            return _rigsJson;
        }

        void BuildRigs(Controls c)
        {
            var ctx = _ctx;
            ctx.Torch = Torch;
            ctx.Noise = _noise;
            ctx.VoiceLine = _voiceLine;
            ctx.QuitTitle = c.Quit != null ? c.Quit.Title : null;
            foreach (var kv in c.Recess)
                if (kv.Value.btn != null)
                    ctx.RecessLabels[kv.Key] = (kv.Value.btn.Title, kv.Value.btn.Blurb);
            Life = gameObject.AddComponent<MenuV6Life>();

            // the room at rest: the emissive breath and the vignette (their Linear gains included)
            var breath = new BreathRig();
            breath.Build(ctx, Root, _stageRect);
            if (_static.TryGetValue("stone_emiBlur", out var blur)) breath.Blur(blur, MenuV6Linear.LayerGain("stone_emiBlur"));
            foreach (var v in new[] { "vignette_top", "vignette_bottom", "vignette_left", "vignette_right" })
                if (_static.TryGetValue(v, out var img))
                    breath.Vignette(img, MenuV6Linear.LayerGain(v));
            Life.Add(breath);

            // the plaque's text swap needs neither the rig JSON nor the box renders: it is set up first, so the hidden
            // box inscription can never show over the deck lines (a clone swaps the text over the P1a box still; with no
            // rigs_<layout>.json the inscription stays hidden until the plaque is hovered)
            var plaque = PlaqueTextRig.For(ctx);
            foreach (var g in _plaqueNormal) plaque.Normal(g);
            foreach (var g in _plaqueIns) plaque.Inscription(g);
            Life.Add(plaque);

            if (ctx.Rigs == null)
            {
                RigsKept.Add("all (no rigs_" + (Land ? "land" : "port") + ".json)");
                foreach (var h in _hosts.Values)
                    if (h != null)
                        DestroyNow(h.gameObject);
                return;
            }

            Rig(new RecessRig(), "recess");
            var labels = new RecessLabelsRig();
            if (labels.Build(ctx, Root, _stageRect)) Life.Add(labels);
            var ring = new RingEchoRig();
            if (Rig(ring, "ringEcho") && _static.TryGetValue("ring_refract", out var refr)) ring.Refraction(refr);
            Rig(new BreachGlowRig(), "breachGlow");
            Rig(new DoorRig(), "door");
            Rig(new MotesRig(), "motes");
            Rig(new FlameRig(), "flame");
            Rig(new NutRig(), "nut");
            Rig(new KeystoneRig(), "keystone");
            Rig(new VoiceRig(), "voice");
            Rig(new NicheRig(), "niche");
            Rig(new BoxRig(), "box");

            // DOM hover chrome: the QUIT plate (+ its label colour) and the niche frame
            var dom = RJ.D(RJ.D(ctx.Rigs, "domHover"), "parts");
            HoverChrome(ctx, "quitHover", RJ.D(RJ.D(dom, "quit_plate"), "hover"), _quitPlate, ctx.Quit, c.QuitRect, .2, ctx.QuitTitle);
            HoverChrome(ctx, "nicheFrameHover", RJ.D(RJ.D(dom, "niche_frame"), "hover"), _nicheFrame, ctx.Map, c.MapRect, 0, null);

            Life.TickNow();
        }

        bool Rig(MenuV6Rig rig, string id)
        {
            if (!_hosts.TryGetValue(id, out var host) || host == null)
            {
                RigsKept.Add(id + " (no host)");
                return false;
            }

            var frame = _hostFrames.TryGetValue(id, out var f) ? f : _stageRect;
            bool ok;
            try
            {
                ok = rig.Build(_ctx, host, frame);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] rig " + id + " kept the P1a still: " + ex.Message);
                ok = false;
            }

            if (!ok)
            {
                RigsKept.Add(id);
                DestroyNow(host.gameObject);
                return false;
            }

            if (_replaced.TryGetValue(id, out var stills))
                foreach (var s in stills)
                    if (s != null)
                        s.SetActive(false);
            Life.Add(rig);
            return true;
        }

        void HoverChrome(RigContext ctx, string id, Dictionary<string, object> hover, Image rest, IRigPointer p, Rect ownerRect,
            double fade, Text label)
        {
            if (!_hosts.TryGetValue(id, out var host) || host == null) return;
            var sp = hover != null ? MenuV6Art.Sprite(RJ.S(hover, "file")) : null;
            if (sp == null || p == null)
            {
                DestroyNow(host.gameObject);
                return;
            }

            var go = new GameObject(id, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.raycastTarget = false;
            img.type = RJ.B(hover, "nine") ? Image.Type.Sliced : Image.Type.Simple;
            MenuV6Layout.Place(img.rectTransform, RJ.R(hover, "rect"), ownerRect);
            var tc = RJ.S(hover, "textColor");
            var rig = DomHoverRig.For(id, ctx, p, rest, img, fade, label,
                string.IsNullOrEmpty(tc) ? null : RJ.Css(tc, Color.white));
            if (rig != null) Life.Add(rig);
        }

        static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ data files

        double[] LoadNoiseRow()
        {
            try
            {
                var ta = MenuV6Art.Data("schedules");
                if (ta == null) return null;
                var root = MenuV6Json.Parse(ta.text) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue("torch", out var t) || t is not Dictionary<string, object> torch) return null;
                if (!torch.TryGetValue("NZF_row7", out var row) || row is not List<object> list) return null;
                var a = new double[list.Count];
                for (var i = 0; i < list.Count; i++) a[i] = list[i] is double v ? v : 0.5;
                _schedules = root;
                return a;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ MenuV6] schedules.json: " + ex.Message);
                return null;
            }
        }

        Dictionary<string, object> _schedules;

        /// <summary>The page's menu reveal windows for an eye (schedules.json eye.reveal), for the quiet rule.</summary>
        List<(double, double)> EyeReveals(int seed)
        {
            try
            {
                if (_schedules == null || !_schedules.TryGetValue("eye", out var e) || e is not Dictionary<string, object> eye)
                    return null;
                if (!eye.TryGetValue("reveal", out var r) || r is not Dictionary<string, object> rev) return null;
                var menu = new List<double[]>();
                if (rev.TryGetValue("menu", out var m) && m is List<object> ml)
                    foreach (var w in ml)
                        if (w is List<object> pair && pair.Count >= 2 && pair[0] is double a && pair[1] is double b)
                            menu.Add(new[] { a, b });
                var off = 0.0;
                if (rev.TryGetValue("off", out var o) && o is Dictionary<string, object> offs &&
                    offs.TryGetValue(seed.ToString(), out var ov) && ov is double od)
                    off = od;
                return EyeBlinkSchedule.MenuReveals(menu, off);
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ fit + helpers

        /// <summary>Fit the stage into the canvas (uniform "contain"); a resize only rescales, so no sheet, popover or
        /// overlay shell is ever rebuilt (§11 F14).</summary>
        public void Fit()
        {
            if (Stage == null || Root == null) return;
            var size = Root.rect.size;
            _lastRootSize = size;
            var k = MenuAspect.StageScale(Root, Layout != null ? Layout.Stage : MenuAspect.LandStage);
            Stage.localScale = new Vector3(k, k, 1f);
        }

        void OnRectTransformDimensionsChange()
        {
            if (Stage != null) Fit();
        }

        void LateUpdate()
        {
            if (Root != null && Root.rect.size != _lastRootSize) Fit();
        }

        public void SetStatus(string msg)
        {
            if (Status != null) Status.text = msg ?? "";
        }

        /// <summary>Captures and tests: put a control in a hover / press state at the (frozen) menu time.</summary>
        public bool ForceState(string control, double since, double pressAge)
        {
            IRigPointer p = control switch
            {
                "Hero" => _ctx?.Door,
                "MapBack" => _ctx?.Map,
                "Quit" => _ctx?.Quit,
                "DeckPlaque" => _ctx?.Plaque,
                "RingEcho" => _ctx?.Ring,
                _ => null
            };
            if (p == null && _ctx != null)
                foreach (var r in RecessTable)
                    if (r.unity == control && _ctx.Recess.TryGetValue(r.id, out var rp))
                        p = rp;
            if (p == null) return false;
            p.Force(MenuV6Clock.Now, 0, 1, since, pressAge);
            if (Life != null) Life.TickNow();
            return true;
        }

        // ------------------------------------------------------------------ P3: what a sheet does to the Stone

        readonly List<CanvasGroup> _keystoneGroups = new();
        CanvasGroup _slatesGroup, _tilesGroup, _statusGroup;

        /// <summary>The Puzzle-lock takes the keystone out of the door while its pieces are away (P3 sheets): hide / show the
        /// keystone rig and its P1a stills (the cold open's KeystoneAssembly hides the same set).</summary>
        public void SetKeystoneShown(bool on)
        {
            if (_keystoneGroups.Count == 0)
            {
                if (Parts.Hosts != null && Parts.Hosts.TryGetValue("keystone", out var h) && h != null)
                    _keystoneGroups.Add(SheetDraw.Group(h.gameObject));
                if (Parts.Static != null)
                    foreach (var id in new[] { "keystone_over_f0", "keystone_over_f1", "keystone_add_flick0" })
                        if (Parts.Static.TryGetValue(id, out var img) && img != null)
                            _keystoneGroups.Add(SheetDraw.Group(img.gameObject));
                if (Parts.Flip != null)
                    foreach (var id in new[] { "keystone_over_f0", "keystone_over_f1" })
                        if (Parts.Flip.TryGetValue(id, out var go) && go != null)
                            _keystoneGroups.Add(SheetDraw.Group(go));
            }

            foreach (var g in _keystoneGroups)
                if (g != null)
                    g.alpha = on ? 1f : 0f;
        }

        /// <summary>The room's DOM under a sheet's dim keeps a (1 − .78 dim) (menu.js:1035): the slates, the controls' own
        /// chrome and the status line; 0 = at rest. Raycasts are untouched (the sheet's dim takes the taps).</summary>
        public void SetSheetDim(double dim)
        {
            if (_slatesGroup == null && Parts.Slates != null) _slatesGroup = SheetDraw.Group(Parts.Slates.gameObject);
            if (_tilesGroup == null && Tiles != null) _tilesGroup = SheetDraw.Group(Tiles.gameObject);
            if (_statusGroup == null && Status != null) _statusGroup = SheetDraw.Group(Status.gameObject);
            // P4: solved over the dark room with the real sRGB curve (Fade's over-black power law hid the slates, P3 landing)
            var a = MenuV6Linear.FadeDark(SheetMath.RoomDom(MenuV6MathF.Clamp01(dim)));
            SheetDraw.SetA(_slatesGroup, a);
            SheetDraw.SetA(_tilesGroup, a);
            SheetDraw.SetA(_statusGroup, a);
        }

        readonly List<CanvasGroup> _torchGroups = new();

        /// <summary>The flame's strength under a sheet (QUIT: the torch gutters as the door closes): the flame rig, its P1a
        /// stills and the motes take k (1 = at rest).</summary>
        public void SetTorchGutter(double k)
        {
            if (_torchGroups.Count == 0)
            {
                if (Parts.Hosts != null)
                    foreach (var id in new[] { "flame", "motes" })
                        if (Parts.Hosts.TryGetValue(id, out var h) && h != null)
                            _torchGroups.Add(SheetDraw.Group(h.gameObject));
                if (Parts.Static != null)
                    foreach (var id in new[] { "flame_glow_add", "flame_tongues", "motes_add" })
                        if (Parts.Static.TryGetValue(id, out var img) && img != null)
                            _torchGroups.Add(SheetDraw.Group(img.gameObject));
            }

            foreach (var g in _torchGroups) SheetDraw.SetA(g, MenuV6MathF.Clamp01(k));
        }

        /// <summary>P2: the torch pool's swing gain about its P1b mean (TorchFlicker.DrivePool; torch_fit.py).</summary>
        public const float TorchPoolSwing = .6f;

        /// <summary>P2: the voice block's lift (stage px), see VoiceLine.</summary>
        public const float VoiceRaise = 3f;

        static Sprite _disc;

        /// <summary>A filled disc for round masks (generated once, never saved with a scene).</summary>
        static Sprite Disc()
        {
            if (_disc != null) return _disc;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "MenuV6Disc"
            };
            var px = new Color32[n * n];
            var c = (n - 1) * 0.5f;
            var r = n * 0.5f - 0.5f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                var a = Mathf.Clamp01(r - d + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            _disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            _disc.hideFlags = HideFlags.HideAndDontSave;
            _disc.name = "MenuV6Disc";
            return _disc;
        }

        /// <summary>Round controls hit only inside their circle (the page's round hit boxes, Ø + 16 px).</summary>
        sealed class CircleHit : MonoBehaviour, ICanvasRaycastFilter
        {
            public bool IsRaycastLocationValid(Vector2 sp, Camera cam)
            {
                var rt = transform as RectTransform;
                if (rt == null) return true;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, sp, cam, out var local)) return false;
                var r = rt.rect;
                var d = local - r.center;
                var rx = r.width * 0.5f;
                var ry = r.height * 0.5f;
                if (rx <= 0f || ry <= 0f) return false;
                return d.x * d.x / (rx * rx) + d.y * d.y / (ry * ry) <= 1f;
            }
        }
    }
}
