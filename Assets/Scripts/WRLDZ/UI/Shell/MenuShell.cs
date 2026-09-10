using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Production chrome host: safe-area canvas, HUD, content, modal, toast, battery-saver overlay.
    /// <list type="bullet">
    /// <item><see cref="Create"/> — full hub shell (bottom nav + HUD + atmosphere)</item>
    /// <item><see cref="CreateOverlay"/> — map/hub modal only (no second nav bar)</item>
    /// </list>
    /// </summary>
    public class MenuShell : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public RectTransform ContentHost { get; private set; }
        public RectTransform ModalHost { get; private set; }
        public RectTransform ToastHost { get; private set; }
        public HudTokens Hud { get; private set; }
        public bool BatterySaver { get; private set; }

        /// <summary>
        /// True when built via <see cref="CreateOverlay"/> — sheets open over map/hub
        /// without replacing navigation with a second bottom bar.
        /// </summary>
        public bool OverlayOnly { get; private set; }

        Image _batteryVeil;
        Text _toastText;
        float _toastUntil;
        readonly Dictionary<MenuId, GameObject> _overlays = new();

        public static MenuShell Create(int sortOrder = 60)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            WrldzTheme.ApplyPortrait();

            var canvas = WrldzTheme.Canvas("MenuShellCanvas", sortOrder);
            var shell = canvas.gameObject.AddComponent<MenuShell>();
            shell.OverlayOnly = false;
            shell.BuildFull(canvas);
            ScreenRouter.Ensure(shell);
            return shell;
        }

        /// <summary>
        /// Lightweight modal host for Overworld / hub nested menus.
        /// No Egyptian full-screen, no bottom nav, no HUD tokens — map stays readable.
        /// </summary>
        public static MenuShell CreateOverlay(int sortOrder = 85)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            var canvas = WrldzTheme.Canvas("MenuShellOverlay", sortOrder);
            var shell = canvas.gameObject.AddComponent<MenuShell>();
            shell.OverlayOnly = true;
            shell.BuildOverlay(canvas);
            ScreenRouter.Ensure(shell);
            return shell;
        }

        void BuildFull(RectTransform canvas)
        {
            Root = canvas;

            // Egyptian night / ages atmosphere (animated, Intermediate Kingdom for systems shell)
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.IntermediateKingdom, showAgeCaption: false);

            Hud = HudTokens.Create(canvas);
            Hud.RefreshFromSession();

            ContentHost = new GameObject("ContentHost", typeof(RectTransform)).GetComponent<RectTransform>();
            ContentHost.SetParent(canvas, false);
            FloatingPanel.Place(ContentHost, 0.03f, 0.12f, 0.97f, 0.88f);

            // Bottom nav — one-hand primary destinations (full hub only)
            BuildBottomNav(canvas);

            BuildModalAndToast(canvas);

            // Battery saver veil (dims neon)
            var veil = new GameObject("BatteryVeil", typeof(RectTransform), typeof(Image));
            veil.transform.SetParent(canvas, false);
            FloatingPanel.Stretch(veil.GetComponent<RectTransform>());
            _batteryVeil = veil.GetComponent<Image>();
            _batteryVeil.sprite = UiFoundation.WhiteSprite();
            _batteryVeil.color = new Color(0f, 0f, 0f, 0.28f);
            _batteryVeil.raycastTarget = false;
            veil.SetActive(false);
        }

        void BuildOverlay(RectTransform canvas)
        {
            Root = canvas;
            // Transparent root — overworld / hub remains visible behind glass menus
            ContentHost = null;
            Hud = null;
            BuildModalAndToast(canvas);
        }

        void BuildModalAndToast(RectTransform canvas)
        {
            ModalHost = new GameObject("ModalHost", typeof(RectTransform)).GetComponent<RectTransform>();
            ModalHost.SetParent(canvas, false);
            FloatingPanel.Stretch(ModalHost);
            ModalHost.SetAsLastSibling();

            ToastHost = new GameObject("ToastHost", typeof(RectTransform)).GetComponent<RectTransform>();
            ToastHost.SetParent(canvas, false);
            FloatingPanel.Place(ToastHost, 0.08f, 0.14f, 0.92f, 0.20f);
            var toastBg = ToastHost.gameObject.AddComponent<Image>();
            toastBg.sprite = UiFoundation.WhiteSprite();
            toastBg.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
            toastBg.raycastTarget = false;
            _toastText = FloatingPanel.Body(ToastHost, "", 14);
            _toastText.alignment = TextAnchor.MiddleCenter;
            FloatingPanel.Stretch(_toastText.rectTransform, 8f);
            ToastHost.gameObject.SetActive(false);
        }

        void BuildBottomNav(Transform canvas)
        {
            var nav = new GameObject("BottomNav", typeof(RectTransform), typeof(Image));
            nav.transform.SetParent(canvas, false);
            FloatingPanel.Place(nav.GetComponent<RectTransform>(), 0f, 0f, 1f, 0.10f);
            var nimg = nav.GetComponent<Image>();
            nimg.sprite = ImagineAssets.BarBottom() ?? UiFoundation.WhiteSprite();
            nimg.type = nimg.sprite != null && nimg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            nimg.color = Color.white;

            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(nav.transform, false);
            FloatingPanel.Stretch(row.GetComponent<RectTransform>(), 6f);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 6;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.padding = new RectOffset(8, 8, 6, 6);

            // Same words as overworld map bar where possible
            NavBtn(row.transform, "VS AI", DuelystUi.IconAtk() ?? DuelystUi.IconMenu(),
                () => ScreenRouter.Ensure(this).OpenPlayerVsAi(), gold: true);
            NavBtn(row.transform, "DECK", DuelystUi.IconDeck(), () => ScreenRouter.Ensure(this).OpenDeck());
            NavBtn(row.transform, "MAP", ImagineAssets.IconCompass() ?? DuelystUi.IconMenu(),
                () => ScreenRouter.Ensure(this).GoHome());
            NavBtn(row.transform, "STORY", DuelystUi.IconStory(), () => ScreenRouter.Ensure(this).OpenStory());
            NavBtn(row.transform, "YOU", DuelystUi.IconMenu(), () => ScreenRouter.Ensure(this).OpenAvatar());
        }

        static void NavBtn(Transform parent, string label, Sprite icon, System.Action onClick, bool gold = false)
        {
            var b = HubChrome.Capsule(parent, label, onClick,
                gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary,
                centerTitle: true, titleSize: 14);
            b.GetComponent<LayoutElement>().minHeight = FloatingPanel.MinButtonHeight;
            if (icon != null)
            {
                var iGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iGo.transform.SetParent(b.transform, false);
                var irt = iGo.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.08f, 0.22f);
                irt.anchorMax = new Vector2(0.32f, 0.78f);
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                var iimg = iGo.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
                iimg.color = Color.white;
                // Nudge label right of icon
                var t = b.GetComponentInChildren<Text>();
                if (t != null)
                {
                    t.alignment = TextAnchor.MiddleLeft;
                    FloatingPanel.Place(t.rectTransform, 0.34f, 0.10f, 0.96f, 0.90f);
                }
            }
        }

        public void SetBatterySaver(bool on)
        {
            BatterySaver = on;
            if (_batteryVeil != null)
                _batteryVeil.gameObject.SetActive(on);
            Application.targetFrameRate = on ? 30 : 60;
        }

        public void Toast(string msg)
        {
            if (_toastText == null || ToastHost == null) return;
            _toastText.text = msg ?? "";
            ToastHost.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.2f;
        }

        void Update()
        {
            if (ToastHost != null && ToastHost.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                ToastHost.gameObject.SetActive(false);
        }

        /// <summary>Show a real screen for a menu id (PvAI create / formats mount full UIs).</summary>
        public void ShowOverlay(MenuId id)
        {
            DualMenuPresenter.HideTransientMenus();

            // Overlay shells hide when empty — show canvas when opening a sheet
            if (OverlayOnly && Root != null)
                Root.gameObject.SetActive(true);

            ClearOverlays(keepCanvasVisible: true);
            if (ToastHost != null)
                ToastHost.gameObject.SetActive(false);

            // ── Player vs AI: opponent sheet, then surface-scan create ──
            if (id == MenuId.ArDuelCreate)
            {
                void CloseCreate()
                {
                    OpponentCatalog.ClearPending();
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                }

                var oppPanel = OpponentSelectScreen.Build(ModalHost, CloseCreate, labTest: false,
                    onPicked: _ =>
                    {
                        ClearOverlays(keepCanvasVisible: true);
                        var createPanel = ArDuelCreateScreen.Build(ModalHost, CloseCreate);
                        _overlays[id] = createPanel.gameObject;
                        PresentMounted();
                    });
                _overlays[id] = oppPanel.gameObject;
                PresentMounted();
                return;
            }

            // ── Player vs Player + auto distance scan ──
            if (id == MenuId.ArDuelPvpCreate)
            {
                var pvpPanel = PlayerVsPlayerCreateScreen.Build(ModalHost, () =>
                {
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                });
                _overlays[id] = pvpPanel.gameObject;
                PresentMounted();
                return;
            }

            // ── AR formats (all PvAI endpoints) ──
            if (id == MenuId.FormatSelect)
            {
                var fmt = FormatSelectScreen.Build(ModalHost, () =>
                {
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                });
                _overlays[id] = fmt.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.AvatarProfile)
            {
                // Avatar uses its own canvas — hide this empty overlay so it cannot block ×
                if (OverlayOnly && Root != null)
                    Root.gameObject.SetActive(false);
                AvatarCustomizerUI.Ensure().OpenProfile(() => Hud?.RefreshFromSession());
                return;
            }

            if (id == MenuId.Settings)
            {
                var settingsPanel = SettingsScreen.Build(ModalHost, this, () =>
                {
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                });
                _overlays[id] = settingsPanel.gameObject;
                PresentMounted();
                return;
            }

            // ── Deck & Collection (same glass UI in AR + non-AR; chrome from Settings) ──
            if (id == MenuId.DeckCollection)
            {
                var mode = DualMenuPresenter.ResolveDefaultPresentation();
                var deckRoot = DeckCollectionScreen.Build(ModalHost, () =>
                {
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                }, mode);
                _overlays[id] = deckRoot.gameObject;
                PresentMounted();
                return;
            }

            // ── Backpack & Inventory (Pack + View anywhere) ──
            if (id == MenuId.Inventory)
            {
                var invRoot = InventoryScreen.Build(ModalHost, () =>
                {
                    ClearOverlays();
                    ScreenRouter.Ensure(this).Back();
                });
                _overlays[id] = invRoot.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.Artifacts)
            {
                var router = ScreenRouter.Ensure(this);
                var focus = router.PendingArtifactFocus;
                router.PendingArtifactFocus = null;
                var art = ArtifactBoxScreen.Build(ModalHost, CloseCurrent, null, focus);
                _overlays[id] = art.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.TomeRaid)
            {
                var tome = SystemsSheets.BuildTome(ModalHost, CloseCurrent);
                _overlays[id] = tome.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.StorySeason)
            {
                var story = SystemsSheets.BuildStory(ModalHost, CloseCurrent);
                _overlays[id] = story.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.Bazaar)
            {
                var bazaar = SystemsSheets.BuildBazaar(ModalHost, CloseCurrent);
                _overlays[id] = bazaar.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.Trade)
            {
                var trade = SystemsSheets.BuildTrade(ModalHost, CloseCurrent);
                _overlays[id] = trade.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.FreeView)
            {
                var free = FreeViewScreen.Build(ModalHost, CloseCurrent);
                _overlays[id] = free.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.Tournament)
            {
                var tourney = TournamentRoomScreen.Build(ModalHost, CloseCurrent);
                _overlays[id] = tourney.gameObject;
                PresentMounted();
                return;
            }

            if (id == MenuId.ZonePrompt)
            {
                var zone = ZoneModePrompt.Build(ModalHost, "overlay", "Zone", null, CloseCurrent);
                _overlays[id] = zone.gameObject;
                PresentMounted();
                return;
            }

            // Last-resort unknown destination — same DualMenuPresenter chrome as scan sheets.
            var frame = DualMenuPresenter.BuildFrame(
                ModalHost, TitleFor(id), NavCopy.HowToLeave, CloseCurrent);
            var body = FloatingPanel.Body(frame.BodyHost, BodyFor(id), 15);
            FloatingPanel.Grid.Full(body.rectTransform, 0.20f, 0.98f);
            body.alignment = TextAnchor.UpperLeft;
            var close = HubChrome.Capsule(frame.BodyHost, "CLOSE", CloseCurrent,
                MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 16);
            FloatingPanel.Grid.Full(close.GetComponent<RectTransform>(), 0.04f, 0.16f);

            _overlays[id] = frame.Root.gameObject;
            PresentMounted();
        }

        void PresentMounted()
        {
            Hud?.RefreshFromSession();
            if (ModalHost == null) return;
            var g = MenuMotion.EnsureGroup(ModalHost.gameObject);
            g.alpha = 0f;
            g.blocksRaycasts = false;
            MenuMotion.Play(this, MenuMotion.SheetIn(ModalHost.gameObject));
        }

        void CloseCurrent()
        {
            if (isActiveAndEnabled && ModalHost != null && ModalHost.childCount > 0)
                StartCoroutine(CloseCurrentCo());
            else
            {
                ClearOverlays();
                ScreenRouter.Ensure(this).Back();
            }
        }

        IEnumerator CloseCurrentCo()
        {
            yield return MenuMotion.SheetOut(ModalHost.gameObject, MenuMotion.Snap);
            ModalHost.gameObject.SetActive(true);
            ClearOverlays();
            ScreenRouter.Ensure(this).Back();
        }

        /// <param name="keepCanvasVisible">
        /// When false (default), overlay-only shells hide entirely so the map is free of
        /// ghost canvas / raycast blockers after × close.
        /// </param>
        public void ClearOverlays(bool keepCanvasVisible = false)
        {
            _overlays.Clear();
            FloatingPanel.DestroyChildrenNow(ModalHost);

            if (OverlayOnly && !keepCanvasVisible && Root != null)
                Root.gameObject.SetActive(false);
        }

        static string TitleFor(MenuId id) => id switch
        {
            MenuId.ArDuelCreate => "PLAYER VS AI",
            MenuId.ArDuelPvpCreate => "PLAYER VS PLAYER",
            MenuId.FormatSelect => "PLAYER VS AI · FORMATS",
            MenuId.DuelLive => "AR DUEL",
            MenuId.DeckCollection => "DECK & COLLECTION",
            MenuId.Inventory => "BAG",
            MenuId.Artifacts => "ARTIFACTS",
            MenuId.StorySeason => "STORY & SEASON",
            MenuId.Bazaar => "BAZAAR",
            MenuId.AvatarProfile => "AVATAR",
            MenuId.Settings => "SETTINGS",
            MenuId.TomeRaid => "TOME (RAID)",
            MenuId.Trade => "TRADE",
            MenuId.ZonePrompt => "ZONE MODE",
            MenuId.Tournament => "TOURNAMENT",
            _ => id.ToString().ToUpperInvariant()
        };

        static string BodyFor(MenuId id) => id switch
        {
            MenuId.DeckCollection =>
                "COLLECTION (left) → MAIN / EXTRA / SIDE (right)\n" +
                "Search + filter · + TO DECK / − FROM DECK · SAVE\n" +
                "Lab account owns full catalog (3× each) for testing.",
            MenuId.Inventory =>
                "CASE — RE4 tetris grid (max 10×8; story then seamstress expansions)\n" +
                "POCKETS — clothing holsters, one deck box each\n" +
                "DECKS — create / delete boxes; editor stays the 40-card builder\n" +
                "HOME — pack from base · collection glance · trade transport",
            MenuId.Artifacts =>
                "Endless Artifact Deck Box. Currencies, badges, keys. Always with you.\n" +
                "Open from wallet chips or BAG · HOME / ON YOU.",
            MenuId.StorySeason =>
                "Season 1 Duelist Kingdom · parchment gates.\nDailies under Referobot. Indoor DUEL needs no GPS.",
            MenuId.ArDuelCreate =>
                "Player vs AI · pick opponent · size the AR field\n" +
                "Yugi / Kaiba / Joey / street lists → walk the space → START.",
            MenuId.ArDuelPvpCreate =>
                "Player vs Player · auto-scan distance between duelists\n" +
                "Mark P1 + P2 · AR arena sized to measured meters · hotseat.",
            MenuId.FormatSelect =>
                "Player vs AI AR formats · field distance presets\n" +
                "format.dk PLAY is 2000 LP · no directs. Story stays 8000.",
            MenuId.Bazaar =>
                "Stone tablets: 1,000 SE of set X → 10 cards of X.\n" +
                "Fuse 5 ERAZ shards + 2,500 SE into the next badge.\n" +
                "SE from story / CPU / Tears — never PvP.",
            MenuId.Settings =>
                "AR quality · Battery saver · Eye-tracking · Right-arm disk\nHigh contrast · Volume\n\nEssentials only.",
            MenuId.TomeRaid =>
                "Raid-only spell-book. Capacity = level/10 (max 10).\nReal S/T cards as pages.",
            MenuId.AvatarProfile =>
                "Live preview · parts · colors · title · Save look.",
            MenuId.Tournament =>
                "Host a room. Friends join. Start when enough seats fill.\nNot a map pin — from the Eye, anywhere.",
            _ => "See UI_SPEC.md for full layout and components."
        };
    }
}
