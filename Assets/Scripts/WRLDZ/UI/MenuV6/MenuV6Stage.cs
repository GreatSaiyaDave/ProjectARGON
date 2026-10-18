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
    /// "The Stone Beneath Nut" v6.3 main menu, P1a (BUILD_PLAN §6 P1a): the approved landscape still rebuilt from the
    /// exported layers (layout_land.json, back to front as the page draws them), the slates with real data (§7),
    /// his eye blinking (flipbooks, D20) and the torch flicker. Only the painting changes: every control keeps the old
    /// hub's name and handler (DuelDiskMenuUI passes its own), all of them under <c>Tiles</c> with the old stagger.
    /// <para>
    /// Null-safe by construction: a missing sprite is skipped (never a white box), a missing text slot is skipped, and
    /// DuelDiskMenuUI falls back to the legacy menu when <see cref="Supports"/> / <see cref="HasArt"/> say no, when the
    /// kill switch is set, or when this throws.
    /// </para>
    /// </summary>
    public sealed class MenuV6Stage : MonoBehaviour
    {
        /// <summary>PlayerPrefs kill switch: 1 = always the legacy menu (rollback without a recompile).</summary>
        public const string KillSwitchPref = "WRLDZ_MenuV6Off";

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
        // from the sprite), the soul gem on the ring (D15), the placeholder strip's ◇, the status line.
        static readonly Rect AvatarDisc = new(127f, 235f, 74f, 74f);
        static readonly Rect SoulGem = new(178f, 286f, 24f, 24f);
        static readonly Rect DeckPlaqueRect = new(122f, 531f, 382f, 150f);
        static readonly Rect Diamond = new(23f, 1054f, 9f, 9f);
        static readonly Rect StatusRect = new(660f, 1004f, 600f, 30f);

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

        Handlers _h;
        Rect _stageRect;
        Vector2 _lastRootSize;

        // ------------------------------------------------------------------ gates

        public static bool KillSwitchOn
        {
            get
            {
                try { return PlayerPrefs.GetInt(KillSwitchPref, 0) == 1; }
                catch { return false; }
            }
        }

        /// <summary>The plans that exist: landscape in P1a; portrait from P1b.</summary>
        public static bool Supports(MenuLayoutKind kind) => kind == MenuLayoutKind.Landscape;

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
            if (KillSwitchOn) return "kill switch " + KillSwitchPref + " = 1";
            if (!Supports(kind)) return kind + " has no v6 plan yet";
            if (!HasArt(kind)) return "v6 art missing (Resources/" + MenuV6Art.DataRoot + MenuV6Layout.FileFor(kind) + ")";
            return null;
        }

        // ------------------------------------------------------------------ build

        /// <summary>Build the Stone into <paramref name="canvas"/>. Throws on a real error (the caller falls back).</summary>
        public static MenuV6Stage Build(RectTransform canvas, Handlers handlers, MenuLayoutKind kind)
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

            var rootGo = new GameObject("Root", typeof(RectTransform));
            rootGo.transform.SetParent(canvas, false);
            var root = rootGo.GetComponent<RectTransform>();
            UiTheme.Stretch(root);
            rootGo.AddComponent<SanctuaryChrome>();
            var stage = rootGo.AddComponent<MenuV6Stage>();
            stage.Root = root;
            stage.Layout = layout;
            stage.Kind = kind;
            stage._h = handlers ?? new Handlers();
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
            var slateL = Node(slates, "SlateL", SlateLRect, _stageRect);
            var slateR = Node(slates, "SlateR", SlateRRect, _stageRect);
            Tiles = Group("Tiles");
            var fx2 = Group("Fx2");
            var ph5 = Group("Placeholder");

            // controls first, so their own chrome can be parented to them
            var controls = BuildControls();

            Torch = TorchFlicker.Attach(gameObject, LoadNoiseRow());
            foreach (var L in Layout.Layers)
            {
                switch (L.Group)
                {
                    case "bg":
                        StaticLayer(room, _stageRect, L);
                        break;
                    case "crkU":
                        StaticLayer(cracks, _stageRect, L);
                        break;
                    case "fx":
                        StaticLayer(fx, _stageRect, L);
                        break;
                    case "fx2":
                        StaticLayer(fx2, _stageRect, L);
                        break;
                    case "ph5":
                        StaticLayer(ph5, _stageRect, L);
                        break;
                    case "ui":
                        UiLayer(L, slateL, slateR, controls);
                        break;
                }
            }

            BuildSlateTexts(slateL, slateR, controls);
            BuildControlTexts(controls);
            BuildPlaceholder(ph5);

            Status = TextFx.Label(Stage, "Status", StatusSlot(), "", _stageRect);
            if (Status == null)
            {
                Status = GoTheme.Label(Stage, "Status", "", 16, WrldzAnimePalette.Calc, TextAnchor.MiddleCenter, bold: false);
                MenuV6Layout.Place(Status.rectTransform, StatusRect, _stageRect);
            }
            Status.alignment = TextAnchor.MiddleCenter;

            // quiet hover motes (ArcaneButton quiet: ≤ 3 per hover) need a field on the canvas; no ambient motes
            UiEmberField.Attach(canvas, 12, false);

            if (Torch != null) Torch.Apply(MenuV6Clock.Now);
            if (Application.isPlaying)
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

        // ------------------------------------------------------------------ controls

        sealed class Controls
        {
            public ArcaneButton Hero, MapBack, Quit, DeckPlaque;
            public Rect HeroRect, MapRect, QuitRect, PlaqueRect;
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

            // QUIT → OpenQuitConfirm (sub-line per D14)
            c.QuitRect = Layout.HasQuit ? Layout.Quit : new Rect(1416f, 757.5f, 250f, 64f);
            c.Quit = Control(Tiles, "Quit", "QUIT", _h.OpenQuitConfirm, EldritchTier.Danger, "fold the disk",
                c.QuitRect, round: false);

            // the Spirit Dueler plaque in the Puzzle's box → the DECK screen (D16: an existing route)
            c.PlaqueRect = DeckPlaqueRect;
            c.DeckPlaque = Control(Tiles, "DeckPlaque", null, _h.OpenDeckHub, EldritchTier.Secondary, null,
                c.PlaqueRect, round: false);
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
            return btn;
        }

        // ------------------------------------------------------------------ layers

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
            // every layer is drawn straight-alpha "over" in P1a (UI/Default; no new shader before P5, D3). E7 measured
            // this choice at p99 8 against the page (the per-layer add / over split is p99 6; P1b).
            img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(L.AlphaAtRest));
            if (Torch != null)
            {
                if (L.Id == "stone_B" || L.Id == "keystone_over_f1") Torch.DriveAlpha(img);
                else if (L.Id == "torch_pool_b0") Torch.DrivePool(img);
            }
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
            return null;
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
            if (id.StartsWith("niche_")) { ControlArt(c.MapBack, c.MapRect, L); return; }
            if (id.StartsWith("quit_plate") || id == "icon_door") { ControlArt(c.Quit, c.QuitRect, L); return; }
            if (id == "box_rest" || id == "disk_glyph") { ControlArt(c.DeckPlaque, c.PlaqueRect, L); return; }

            if (id == "meter")
            {
                var pick = MeterFile(L);
                var m = new MenuV6Layout.Layer
                {
                    Id = L.Id, Z = L.Z, Group = L.Group, File = pick, Rect = L.Rect, Ppu = L.Ppu, Blend = L.Blend,
                    AlphaAtRest = L.AlphaAtRest
                };
                if (Data.ChapterIndex > 0) StaticLayer(slateL, SlateLRect, m);
                return;
            }

            if (id == "kuriboh_medal") { CompanionMedal(slateL, L); return; }

            if (id.StartsWith("xp_fill"))
            {
                var img = StaticLayer(slateL, SlateLRect, L);
                if (img != null)
                {
                    // the page's fill is a sample (62 %); the bar math is the Overworld's (XpProgress01, min 2 %)
                    var track = Layout.LayerById("xp_track_0");
                    var inner = track != null ? track.Rect.width - 2f : L.Rect.width;
                    var r = L.Rect;
                    r.width = Mathf.Max(inner * Mathf.Clamp(Data.Xp01, 0.02f, 1f), 3f);
                    MenuV6Layout.Place(img.rectTransform, r, SlateLRect);
                }

                return;
            }

            var left = L.Rect.center.x < 960f;
            StaticLayer(left ? slateL : slateR, left ? SlateLRect : SlateRRect, L);
        }

        readonly Dictionary<Transform, int> _artCount = new();

        /// <summary>A control's own chrome (label plate, QUIT plate, niche, box): drawn under the control's labels, in
        /// the page's z order.</summary>
        void ControlArt(ArcaneButton owner, Rect ownerRect, MenuV6Layout.Layer L, string name = null)
        {
            if (owner == null) return;
            var img = StaticLayer(owner.transform, ownerRect, L, name);
            if (img == null) return;
            _artCount.TryGetValue(owner.transform, out var k);
            img.transform.SetSiblingIndex(k);
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

        void CompanionMedal(RectTransform slateL, MenuV6Layout.Layer L)
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
                StaticLayer(slateL, SlateLRect, L);
                return;
            }

            var mask = new GameObject("TeamMedal", typeof(RectTransform), typeof(Image), typeof(Mask));
            mask.transform.SetParent(slateL, false);
            var mimg = mask.GetComponent<Image>();
            MenuV6Layout.Place(mimg.rectTransform, L.Rect, SlateLRect);
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
        void Run(Transform parent, Rect parentRect, string name, Rect line, params (string sel, string key, string shown, float gapBefore)[] parts)
        {
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
            }
        }

        void BuildSlateTexts(RectTransform slateL, RectTransform slateR, Controls c)
        {
            var d = Data;
            var L = SlateLRect;
            var R = SlateRRect;

            // DUELIST: the player's name (Marcellus 50, tracked), fitted to the slate
            var name = T(slateL, L, "Name", "n > span.n", "DUELIST", d.Name);
            if (name != null)
            {
                // one line, up to the slate's inner edge (the rule under it ends at x 511), shrunk to fit
                var slot = Layout.Text("n > span.n", "DUELIST");
                var r = slot.Rect;
                r.width = 511f - r.x;
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                MenuV6Layout.Place(name.rectTransform, r, L);
                TextFx.FitWidth(name, r.width, 24);
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
            SeasonCap(slateL, L);

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

            // WORLD MAP niche: name, place, the real Tear pin count (the Millennium site and locators have no source)
            var map = c.MapBack;
            if (map != null)
            {
                var title = map.Title;
                var blurb = map.Blurb;
                ApplyTo(title, c.MapRect, "t > div.t", "WORLD MAP", "WORLD MAP");
                var s = Layout.Text("s > div.s", "Battle City · night near");
                if (blurb != null && s != null)
                {
                    TextFx.Apply(blurb, s, "Battle City · night");
                    var line1 = s.Rect;
                    line1.height = s.LineHeight > 0f ? s.LineHeight : line1.height * 0.5f;
                    MenuV6Layout.Place(blurb.rectTransform, line1, c.MapRect);
                    blurb.alignment = TextAnchor.MiddleLeft;
                    var line2 = line1;
                    line2.y += line1.height;
                    Run(map.transform, c.MapRect, "Tears", line2,
                        ("s > b", "3 Tears", d.TearPins + " Tears", 0f),
                        ("s > div.s", "Battle City · night near", "near", 5f));
                }
            }

            // the Spirit Dueler plaque: deck name, main count / 40, the format's LP
            var plaque = c.DeckPlaque;
            if (plaque != null)
            {
                var pr = c.PlaqueRect;
                var k = Layout.Text("k > div.k", "SPIRIT DUELER");
                if (k != null)
                {
                    // the disk glyph sits at the start of this line (22 px + its gap, read off the still)
                    var t = TextFx.Label(plaque.transform, "Kicker", k, "SPIRIT DUELER", pr);
                    var kr = k.Rect;
                    kr.x += 30f;
                    kr.width -= 30f;
                    if (t != null) MenuV6Layout.Place(t.rectTransform, kr, pr);
                }

                if (!string.IsNullOrEmpty(d.DeckName))
                    T(plaque.transform, pr, "DeckName", "t > div.t", "Junkuriboh Starter", d.DeckName);
                var sline = Layout.Text("s > div.s", "· LP");
                var sRect = sline != null ? sline.Rect : new Rect(284f, 631f, 220f, 32f);
                Run(plaque.transform, pr, "DeckLine", sRect,
                    ("s > b", "40/40", MenuV6Data.DeckCountLine(d), 0f),
                    ("s > div.s", "· LP", MenuV6Data.DeckCountLine(d) != null && d.StartingLp > 0 ? "·" : null, 5f),
                    ("s > b", "8000", d.StartingLp > 0 ? d.StartingLp.ToString() : null, 5f),
                    ("s > div.s", "· LP", d.StartingLp > 0 ? "LP" : null, 4f));
            }

            // NEWS: Tears from the pin count; the Bazaar's tablet packs (SystemsSheets rows)
            T(slateR, R, "NewsCap", "cap > div.cap", "News", "News");
            T(slateR, R, "News1", "t > div.t", "A Tear stirs over", "Tears open in Battle City");
            T(slateR, R, "News1Sub", "s > div.s", "3 spirits · 1 boss · 0.4 mi ·", d.TearPins + " on the map");
            T(slateR, R, "News2", "t > div.t", "Tablet packs at the Bazaar", "Tablet packs at the Bazaar");
            T(slateR, R, "News2Sub", "s > div.s", "LOB · MRD · SRL · 1,000 SE of a set · one 10-card pack", d.BazaarLine);

            // THE PHARAOH'S VOICE: the one sourced line (Blueprint l.29; menu.js:61), shown whole until P1b types it
            T(slateR, R, "VoiceCap", "cap > div.cap", "The Pharaoh's voice", "The Pharaoh's voice");
            T(slateR, R, "Voice", "q > span", "“Duelist from beyond—interrupt this shadow!”",
                "“Duelist from beyond—interrupt this shadow!”");

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
            Avatar(slateL, L);
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

        void SeasonCap(RectTransform slateL, Rect L)
        {
            var slot = Layout.Text("cap > div.cap", "Season 1 · Duelist Kingdom");
            var r = slot != null ? slot.Rect : new Rect(122f, 408f, 382f, 17f);
            var host = Node(slateL, "SeasonCap", r, L);
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

        void Avatar(RectTransform slateL, Rect L)
        {
            var face = new GameObject("FaceHost", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(Button));
            face.transform.SetParent(slateL, false);
            var fimg = face.GetComponent<Image>();
            MenuV6Layout.Place(fimg.rectTransform, AvatarDisc, L);
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

            var soul = SoulBadgeView.Create(slateL);
            MenuV6Layout.Place(soul.GetComponent<RectTransform>(), SoulGem, L);
            soul.Bind(Data.Progress);
            var count = soul.transform.Find("Count");
            var ct = count != null ? count.GetComponent<Text>() : null;
            if (ct != null)
            {
                ct.font = TextFx.FontFor("Cairo", 700, out _);
                ct.fontSize = 11;
                ct.fontStyle = FontStyle.Normal;
            }
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
            var t = TextFx.Label(ph5, "PlaceholderNote", slot,
                "placeholder art · the Stone · Puzzle body + pieces · Ring · Rod · awaiting existing models", _stageRect);
            if (t != null)
            {
                var r = slot.Rect;
                r.width -= Diamond.xMax + 4f - r.x;
                r.x = Diamond.xMax + 4f;
                MenuV6Layout.Place(t.rectTransform, r, _stageRect);
            }

            var dia = new GameObject("PlaceholderMark", typeof(RectTransform), typeof(HollowDiamond));
            dia.transform.SetParent(ph5, false);
            var g = dia.GetComponent<HollowDiamond>();
            g.color = t != null ? t.color : slot.Color;
            g.raycastTarget = false;
            MenuV6Layout.Place(g.rectTransform, Diamond, _stageRect);
        }

        MenuV6Layout.TextSlot StatusSlot()
        {
            // the old hub's status line (toasts), in the slate's sub-line face; not part of the approved still (empty)
            var src = Layout.Text("s > div.s", "LOB · MRD · SRL · 1,000 SE of a set · one 10-card pack");
            if (src == null) return null;
            return new MenuV6Layout.TextSlot
            {
                Sel = src.Sel, Text = "", Rect = StatusRect, Opacity = 1f, Family = src.Family, Weight = src.Weight,
                Size = src.Size, Color = src.Color, Align = "center", NoWrap = true
            };
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

        /// <summary>Fit the 16:9 stage into the canvas (uniform "contain"); a resize only rescales, so no sheet,
        /// popover or overlay shell is ever rebuilt (§11 F14).</summary>
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
