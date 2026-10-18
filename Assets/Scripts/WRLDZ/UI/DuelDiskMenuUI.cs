using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.MenuV6;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Main menu (MainMenu scene). Landscape: "The Stone Beneath Nut" v6.3 (<see cref="MenuV6Stage"/>, BUILD_PLAN P1a).
    /// Portrait, the kill switch <c>WRLDZ_MenuV6Off</c> = 1, missing v6 art or any v6 build error: the legacy Eldritch
    /// Solid Vision hierarchy (ELDRITCH_CONTRACT §6) — Identity slate → DUEL hero (Solid Vision disk) → COMMAND 2×2 →
    /// VIEW/SET → WORLD MAP | QUIT. Both paths use the same handlers and routes.
    /// Boot lands here; WORLD MAP leaves for the Overworld.
    /// </summary>
    public class DuelDiskMenuUI : MonoBehaviour
    {
        public string duelSceneName = "DuelSlice";
        public const string GameTitle = "Duel Monsters: WRLDZ";
        public const string ProjectCode = "ProjectARGON";

        Text _status;
        SpiritDuelerDiskView _hubDisk;
        RectTransform _root;
        // Only one popover (DuelChoice / QuitConfirm) lives at a time.
        GameObject _popoverHost;
        HieroglyphPanel _popover;
        // The v6.3 Stone when it was built (null on the legacy path).
        MenuV6Stage _v6;
        GameObject _v6Canvas;

        /// <summary>True when the last <see cref="Build"/> took the v6.3 path.</summary>
        public bool IsMenuV6 => _v6 != null;

        /// <summary>The canvas the last <see cref="Build"/> made (DiskMenuCanvas), for tests and captures.</summary>
        public RectTransform MenuCanvas { get; private set; }

        public void Build()
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            GoTheme.EnsureAssets();
            WrldzTheme.ApplyPortrait();
            BuildVisuals();
        }

        void OnDestroy()
        {
            if (_hubDisk != null)
                Destroy(_hubDisk.gameObject);
            HubPropView.DestroyAll();
        }

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>
        /// The v6.3 Stone when this aspect has a plan and its art is in, else the legacy menu. A v6 build error never
        /// leaves a blank home: the partial canvas is destroyed and the legacy menu is built instead.
        /// </summary>
        void BuildVisuals()
        {
            var kind = MenuAspect.Current;
            var why = MenuV6Stage.WhyNot(kind);
            if (why == null)
            {
                try
                {
                    BuildVisualsV6(kind);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogError("[WRLDZ] Main menu v6 build failed; showing the legacy menu. " + ex);
                    DestroyPartialV6();
                }
            }
            else
            {
                Debug.Log("[WRLDZ] Main menu: legacy layout (" + why + ").");
            }

            BuildVisualsLegacy();
        }

        void BuildVisualsV6(MenuLayoutKind kind)
        {
            var canvas = WrldzTheme.Canvas("DiskMenuCanvas", 50, aspect: true);
            _v6Canvas = canvas.gameObject;
            MenuCanvas = canvas;
            var handlers = new MenuV6Stage.Handlers
            {
                OpenDeckHub = OpenDeckHub,
                ShowInventory = ShowInventory,
                OpenStory = OpenStory,
                OpenBazaar = OpenBazaar,
                OpenFreeView = OpenFreeView,
                OpenSettings = OpenSettings,
                OpenDuelChoice = OpenDuelChoice,
                GoOverworld = GoOverworld,
                OpenQuitConfirm = OpenQuitConfirm,
                OpenAvatarMenus = OpenAvatarMenus
            };
            var stage = MenuV6Stage.Build(canvas, handlers, kind);
            _v6 = stage;
            _root = stage.Root;
            _status = stage.Status;
            if (stage.Missing.Count > 0)
                Debug.LogWarning("[WRLDZ] Main menu v6: " + stage.Missing.Count + " layer(s) / text(s) not found: " +
                                 string.Join(", ", stage.Missing));
        }

        void DestroyPartialV6()
        {
            var go = _v6Canvas;
            _v6 = null;
            _v6Canvas = null;
            _root = null;
            _status = null;
            MenuCanvas = null;
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        void GoOverworld()
        {
            FreeUiKit.PlayConfirm();
            AppSession.Ensure().GoOverworld();
        }

        /// <summary>Today's menu, unchanged: the portrait plan, the kill switch and the v6 fallback.</summary>
        void BuildVisualsLegacy()
        {
            var canvas = WrldzTheme.Canvas("DiskMenuCanvas", 50);
            MenuCanvas = canvas;

            var bg = WrldzTheme.StretchFill(canvas, "HubBg", Color.white, ImagineAssets.BgHub());
            var bgImg = bg.GetComponent<Image>();
            bgImg.preserveAspect = false;
            bgImg.raycastTarget = false;

            // Animated motes (stars · drifting glyphs · gold dust) float over the painted backdrop.
            // The atmosphere's opaque sky / sand / veil plates would bury bg_hub, so only the
            // moving layers stay on; Attach() forces index 0 — move it above the bg sprite.
            var atmosphere = EgyptianAgesAtmosphere.Attach(canvas, MenuAge.NewKingdom, false);
            if (atmosphere != null)
            {
                atmosphere.transform.SetSiblingIndex(1);
                EldritchDressing.KeepOnlyStars(atmosphere.transform);
            }

            var root = WrldzTheme.StretchFill(canvas, "Root", new Color(0.02f, 0.03f, 0.07f, 0.30f));
            root.GetComponent<Image>().raycastTarget = false;
            _root = root;

            var account = AppSession.Ensure().Account;
            account?.EnsureProgress();
            var who = account?.displayName ?? "Duelist";
            var lvl = account?.progress?.level ?? 1;
            var team = account != null
                ? KuribohTeamInfo.DisplayName(account.progress.Team)
                : "Unbound";

            var dusk = WrldzTheme.StretchFill(root, "LowerDusk", new Color(0.02f, 0.03f, 0.08f, 0.46f));
            GoTheme.Place(dusk, 0f, 0f, 1f, 0.58f);
            dusk.GetComponent<Image>().raycastTarget = false;

            // Thin dim hieroglyph rails hugging the screen edges (decorative).
            var railGlow = new Color(EldritchPalette.Sandstone.r, EldritchPalette.Sandstone.g,
                EldritchPalette.Sandstone.b, 0.55f);
            var railL = HieroglyphBorder.Create(root, HieroglyphBorder.Edge.Left, 28f, railGlow, 1.2f);
            if (railL != null) railL.SetIntensity(0.6f);
            var railR = HieroglyphBorder.Create(root, HieroglyphBorder.Edge.Right, 28f, railGlow, 1.2f);
            if (railR != null)
            {
                railR.Reverse = true;
                railR.SetIntensity(0.6f);
            }

            EldritchDressing.ScreenTentacles(root);
            BuildIdentity(root, account, who, lvl, team);
            BuildDiskCap(root);

            HubChrome.SectionCap(root, "SysCap", "COMMAND", EldritchPalette.SolidVisionCyan,
                0.06f, 0.530f, 0.46f, 0.568f);

            // Tiles host: every interactive plate lives here so one stagger reveals them in order.
            var tiles = new GameObject("Tiles", typeof(RectTransform)).GetComponent<RectTransform>();
            tiles.SetParent(root, false);
            GoTheme.Stretch(tiles);

            BuildHero(tiles);

            // COMMAND 2×2 (lapis / cyan) — icons from ImagineAssets.
            DestTile(tiles, 0.04f, 0.440f, 0.49f, 0.525f, "DECK", ImagineAssets.IconDeck(), OpenDeckHub);
            DestTile(tiles, 0.51f, 0.440f, 0.96f, 0.525f, "BAG", ImagineAssets.IconBag(), ShowInventory);
            DestTile(tiles, 0.04f, 0.345f, 0.49f, 0.430f, "STORY", ImagineAssets.IconStory(), OpenStory);
            DestTile(tiles, 0.51f, 0.345f, 0.96f, 0.430f, "BAZAAR", ImagineAssets.IconBazaar(), OpenBazaar);

            // Tertiary row (compact).
            CompactTile(tiles, 0.04f, 0.250f, 0.49f, 0.335f, "VIEW", ImagineAssets.IconView(), OpenFreeView);
            CompactTile(tiles, 0.51f, 0.250f, 0.96f, 0.335f, "SET", ImagineAssets.IconSettings(), OpenSettings);

            // WORLD MAP | QUIT (QUIT = right 28% of the width).
            // contract note: "Primary-outline" has no tier of its own; Primary tier carries the gold line.
            var mapBtn = ArcaneButton.Create(tiles, "WORLD MAP", GoOverworld, EldritchTier.Primary,
                "Return to the Battle City streets",
                ImagineAssets.IconCompass() ?? WrldzPresentation.SpiritEye() ?? DuelystUi.IconDeck(),
                24, false, false);
            mapBtn.name = "MapBack";
            EldritchDressing.SealFace(mapBtn, SealState.Intact);
            GoTheme.Place(mapBtn.Rect, 0.04f, 0.135f, 0.66f, 0.235f);
            HardenLabel(mapBtn.Title, 24, display: true);
            HardenLabel(mapBtn.Blurb, 14, display: false);

            var quitBtn = ArcaneButton.Create(tiles, "QUIT", OpenQuitConfirm, EldritchTier.Danger,
                null, null, 24, true, false);
            quitBtn.name = "Quit";
            EldritchDressing.SealFace(quitBtn, SealState.Condemned);
            GoTheme.Place(quitBtn.Rect, 0.68f, 0.135f, 0.96f, 0.235f);
            HardenLabel(quitBtn.Title, 24, display: true);

            _status = GoTheme.Label(root, "S", "", 14, EldritchPalette.TextMuted, TextAnchor.MiddleCenter, bold: false);
            WrldzType.Style(_status, 14, display: false, heavyOutline: true);
            _status.color = EldritchPalette.TextMuted;
            GoTheme.Place(_status.rectTransform, 0.08f, 0.100f, 0.92f, 0.130f);

            // Ambient dust + rare embers across the whole canvas (canvas-safe pooled Images).
            var embers = UiEmberField.Attach(canvas, 48, true);
            if (embers != null)
                embers.AmbientRate = 1.5f;

            if (Application.isPlaying)
                MenuMotion.PlayOn(tiles.gameObject, MenuMotion.StaggerChildren(tiles, 0.26f, 0.045f));
        }

        void BuildIdentity(RectTransform root, LocalAccountStore.Account account, string who, int lvl, string team)
        {
            var slate = SolidVisionSlate.Create(root, "Identity", EldritchTier.Tertiary, false, false);
            var header = slate.Rect;
            GoTheme.Place(header, 0.04f, 0.905f, 0.96f, 0.985f);
            if (header.GetComponent<RectMask2D>() == null)
                header.gameObject.AddComponent<RectMask2D>();

            var faceHost = new GameObject("FaceHost", typeof(RectTransform), typeof(Image), typeof(Button));
            faceHost.transform.SetParent(header, false);
            GoTheme.Place(faceHost.GetComponent<RectTransform>(), 0.02f, 0.08f, 0.18f, 0.92f);
            var faceImg = faceHost.GetComponent<Image>();
            faceImg.sprite = DuelystUi.OrbRing() ?? DuelystUi.BtnCircle();
            faceImg.color = Color.white;
            faceImg.preserveAspect = true;
            // The avatar frame is a plain Image, not an Eldritch surface, so it has no shader edge
            // of its own — it needs the shared hairline. LiftPlate self-skips ArcaneButton and
            // SolidVisionSlate faces, so this is the one hub plate it still acts on, and routing
            // it through HubChrome is what hub_overlay_chrome_check.py checks for.
            HubChrome.LiftPlate(faceImg, EldritchPalette.EdgeFor(EldritchTier.Tertiary));
            var portrait = AvatarPortraitView.CreateFullBodyFill(faceHost.transform, hideBackground: true, badgeCrop: true);
            portrait.Apply(account != null ? account.GetAvatarOrDefault() : AvatarAppearance.Default());
            faceHost.GetComponent<Button>().onClick.AddListener(OpenAvatarMenus);

            var look = account != null ? account.GetAvatarOrDefault() : null;
            var title = GoTheme.Label(header, "Title", who, 28, EldritchPalette.GoldHot, TextAnchor.MiddleLeft);
            WrldzType.StyleGoldTitle(title, 28);
            title.alignment = TextAnchor.MiddleLeft;
            GoTheme.Place(title.rectTransform, 0.20f, 0.46f, 0.82f, 0.94f);
            var sub = GoTheme.Label(header, "Sub",
                $"Lv{lvl}  ·  {team}  ·  {(look?.title ?? "Spirit Dueler")}", 16,
                EldritchPalette.TextCream, TextAnchor.MiddleLeft, bold: true);
            WrldzType.Style(sub, 16, display: false, heavyOutline: true);
            sub.color = EldritchPalette.TextCream;
            WrldzType.ApplyOutline(sub, heavy: true, buttonContrast: true);
            GoTheme.Place(sub.rectTransform, 0.20f, 0.08f, 0.82f, 0.48f);

            // Soul gem at the right end of the slate.
            var soul = SoulBadgeView.Create(header);
            GoTheme.Place(soul.GetComponent<RectTransform>(), 0.855f, 0.14f, 0.975f, 0.86f);
            soul.Bind(account?.progress);

            slate.PlayOpen();
        }

        void BuildDiskCap(RectTransform root)
        {
            var cap = new GameObject("DiskCap", typeof(RectTransform)).GetComponent<RectTransform>();
            cap.SetParent(root, false);
            GoTheme.Place(cap, 0.04f, 0.862f, 0.96f, 0.900f);

            var label = GoTheme.Label(cap, "Cap", "DUEL DISK · SOLID VISION", 16, EldritchPalette.MillenniumGold,
                TextAnchor.MiddleLeft);
            WrldzType.Style(label, 16, display: true, heavyOutline: true);
            label.color = EldritchPalette.MillenniumGold;
            WrldzType.ApplyOutline(label, heavy: true, buttonContrast: true);
            GoTheme.Place(label.rectTransform, 0.02f, 0f, 0.56f, 1f);

            // Glyph strip runs the remainder of the cap width.
            var strip = new GameObject("GlyphStrip", typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(cap, false);
            GoTheme.Place(strip, 0.58f, 0.10f, 1f, 0.90f);
            HieroglyphBorder.Create(strip, HieroglyphBorder.Edge.Top, 40f, EldritchPalette.MillenniumGold, 3f);
        }

        void BuildHero(RectTransform tiles)
        {
            var hero = ArcaneButton.Create(tiles, "DUEL", OpenDuelChoice, EldritchTier.Primary,
                "Summon the Solid Vision", null, 34, false, true);
            hero.name = "Hero";
            EldritchDressing.SealFace(hero, SealState.Intact);
            GoTheme.Place(hero.Rect, 0.04f, 0.575f, 0.96f, 0.855f);

            // The hero is the one true Solid Vision slate: thin enough for the sealed pyramid in
            // bg_hub to ghost through it; labels keep the heavy outline for legibility.
            if (hero.Mat != null)
            {
                if (hero.Mat.HasProperty(EldritchMaterials.FillColorAId))
                {
                    var a = hero.Mat.GetColor(EldritchMaterials.FillColorAId);
                    a.a = 0.56f;
                    hero.Mat.SetColor(EldritchMaterials.FillColorAId, a);
                }

                if (hero.Mat.HasProperty(EldritchMaterials.FillColorBId))
                {
                    var b = hero.Mat.GetColor(EldritchMaterials.FillColorBId);
                    b.a = 0.74f;
                    hero.Mat.SetColor(EldritchMaterials.FillColorBId, b);
                }
            }

            // Copy on the left 58%; the disk showcase owns the right 38%.
            if (hero.Title != null)
            {
                GoTheme.Place(hero.Title.rectTransform, 0.06f, 0.50f, 0.58f, 0.90f);
                hero.Title.alignment = TextAnchor.MiddleLeft;
                HardenLabel(hero.Title, 34, display: true);
            }

            if (hero.Blurb != null)
            {
                GoTheme.Place(hero.Blurb.rectTransform, 0.06f, 0.14f, 0.58f, 0.50f);
                hero.Blurb.alignment = TextAnchor.UpperLeft;
                HardenLabel(hero.Blurb, 16, display: false);
            }

            // Arcane summoning ring behind the disk — slow spin in play mode, static in edit mode.
            var ringSpr = EldritchAssets.RuneRing();
            if (ringSpr != null)
            {
                var ring = new GameObject("SummonRing", typeof(RectTransform), typeof(Image), typeof(RuneRingSpin));
                ring.transform.SetParent(hero.transform, false);
                GoTheme.Place(ring.GetComponent<RectTransform>(), 0.60f, 0.06f, 0.98f, 0.94f);
                var rimg = ring.GetComponent<Image>();
                rimg.sprite = ringSpr;
                rimg.preserveAspect = true;
                rimg.raycastTarget = false;
                rimg.color = new Color(EldritchPalette.MillenniumGold.r, EldritchPalette.MillenniumGold.g,
                    EldritchPalette.MillenniumGold.b, 0.30f);
            }

            _hubDisk = SpiritDuelerDiskView.CreateInUi(hero.transform,
                SpiritDuelerDiskView.PoseMode.HubShowcase,
                0.60f, 0.06f, 0.98f, 0.94f, EldritchPalette.MillenniumGold,
                HubPropView.EnsureWorldRoot(), false);
        }

        /// <summary>Slow clockwise spin + breathing alpha for the hero summoning ring (unscaled, play mode only).</summary>
        sealed class RuneRingSpin : MonoBehaviour
        {
            Image _img;
            float _t;

            void Update()
            {
                if (!Application.isPlaying) return;
                _t += Time.unscaledDeltaTime;
                transform.Rotate(0f, 0f, -7f * Time.unscaledDeltaTime);
                if (_img == null) _img = GetComponent<Image>();
                if (_img == null) return;
                var c = _img.color;
                c.a = 0.22f + 0.12f * (0.5f + 0.5f * Mathf.Sin(_t * 1.3f));
                _img.color = c;
            }
        }

        void DestTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, Sprite icon, Action onClick)
        {
            var btn = ArcaneButton.Create(parent, title, onClick, EldritchTier.Secondary,
                null, icon, 22, false, false);
            btn.name = "Dest_" + title;
            GoTheme.Place(btn.Rect, x0, y0, x1, y1);
            HardenLabel(btn.Title, 22, display: true);
            EldritchDressing.CarveFace(btn, glyphAlpha: 0.85f);
            EldritchDressing.SealFace(btn, SealState.Fractured);
        }

        void CompactTile(Transform parent, float x0, float y0, float x1, float y1,
            string title, Sprite icon, Action onClick)
        {
            var btn = ArcaneButton.Create(parent, title, onClick, EldritchTier.Tertiary,
                null, icon, 20, false, false);
            btn.name = "Tert_" + title;
            GoTheme.Place(btn.Rect, x0, y0, x1, y1);
            HardenLabel(btn.Title, 20, display: true);
            EldritchDressing.CarveFace(btn, glyphAlpha: 0.5f);
            EldritchDressing.SealFace(btn, SealState.Sealed);
        }

        /// <summary>Outdoor readability on busy plates: white ink + thick black outline, tier color preserved for display titles.</summary>
        static void HardenLabel(Text t, int designSize, bool display)
        {
            if (t == null) return;
            var keep = t.color;
            WrldzType.StyleButtonLabel(t, designSize, display);
            // Display titles keep the tier's ink (gold / cream); body copy stays pure white.
            if (display) t.color = keep;
        }

        // ── Popovers ────────────────────────────────────────────────────────

        void OpenDuelChoice()
        {
            FreeUiKit.PlaySelect();
            var panel = OpenPopover("DuelChoice", EldritchTier.Primary, "CHOOSE YOUR DUEL", 0.30f, 0.80f);
            if (panel == null) return;
            var body = panel.Body;

            var ai = ArcaneButton.Create(body, "VS AI", () =>
            {
                ClosePopover();
                OpenPlayerVsAi();
            }, EldritchTier.Primary, "Practice · table · street", ImagineAssets.IconDuel(), 24, false, false);
            GoTheme.Place(ai.Rect, 0.04f, 0.70f, 0.96f, 0.94f);
            HardenLabel(ai.Title, 24, display: true);
            HardenLabel(ai.Blurb, 14, display: false);

            var pvp = ArcaneButton.Create(body, "VS PLAYER", () =>
            {
                ClosePopover();
                OpenPlayerVsPlayer();
            }, EldritchTier.Secondary, "Scan · shared arena", ImagineAssets.IconVsPvp(), 24, false, false);
            GoTheme.Place(pvp.Rect, 0.04f, 0.42f, 0.96f, 0.66f);
            HardenLabel(pvp.Title, 24, display: true);
            HardenLabel(pvp.Blurb, 14, display: false);

            var zone = ArcaneButton.Create(body, "ZONE MODE", () =>
            {
                FreeUiKit.PlayConfirm();
                ClosePopover();
                ZoneModeSession.EnterFromHub(transform);
            }, EldritchTier.Eldritch, "Your Duel Disk at your location", ImagineAssets.IconCompass(), 24, false, false);
            GoTheme.Place(zone.Rect, 0.04f, 0.14f, 0.96f, 0.38f);
            HardenLabel(zone.Title, 24, display: true);
            HardenLabel(zone.Blurb, 14, display: false);

            var close = ArcaneButton.Create(body, "CLOSE", ClosePopover, EldritchTier.Tertiary,
                null, null, 18, true, false);
            GoTheme.Place(close.Rect, 0.30f, 0.00f, 0.70f, 0.10f);
            HardenLabel(close.Title, 18, display: true);

            panel.Open();
        }

        /// <summary>QUIT folds the disk (BUILD_PLAN D14): "Fold your Spirit Dueler?" STAY / FOLD. FOLD signs out and
        /// returns to Boot, so the body keeps the accurate line (§11 F15).</summary>
        void OpenQuitConfirm()
        {
            FreeUiKit.PlaySelect();
            var panel = OpenPopover("QuitConfirm", EldritchTier.Danger, "Fold your Spirit Dueler?", 0.38f, 0.62f);
            if (panel == null) return;
            var body = panel.Body;

            var copy = GoTheme.Label(body, "Copy",
                "Your progress is saved. The Duel Disk powers down and you return to the gate.",
                16, EldritchPalette.TextCream, TextAnchor.MiddleCenter, bold: true);
            WrldzType.Style(copy, 16, display: false, heavyOutline: true);
            copy.color = EldritchPalette.TextCream;
            copy.horizontalOverflow = HorizontalWrapMode.Wrap;
            GoTheme.Place(copy.rectTransform, 0.06f, 0.52f, 0.94f, 0.96f);

            var quit = ArcaneButton.Create(body, "FOLD", () =>
            {
                FreeUiKit.PlayConfirm();
                ClosePopover();
                var s = AppSession.Ensure();
                s.Logout();
                s.GoBoot();
            }, EldritchTier.Danger, null, null, 24, true, false);
            GoTheme.Place(quit.Rect, 0.06f, 0.06f, 0.48f, 0.46f);
            HardenLabel(quit.Title, 24, display: true);

            var stay = ArcaneButton.Create(body, "STAY", ClosePopover, EldritchTier.Tertiary,
                null, null, 24, true, false);
            GoTheme.Place(stay.Rect, 0.52f, 0.06f, 0.94f, 0.46f);
            HardenLabel(stay.Title, 24, display: true);

            panel.Open();
        }

        /// <summary>Dim + HieroglyphPanel over Root; replaces any live popover.</summary>
        HieroglyphPanel OpenPopover(string name, EldritchTier tier, string title, float y0, float y1)
        {
            if (_root == null) return null;
            if (_popoverHost != null) DestroyPopoverNow();

            var host = new GameObject("Popover_" + name, typeof(RectTransform));
            host.transform.SetParent(_root, false);
            GoTheme.Stretch(host.GetComponent<RectTransform>());
            _popoverHost = host;

            HubChrome.OverlayDim(host.transform, ClosePopover);

            var panel = HieroglyphPanel.Create(host.transform, name, tier, title, true, true);
            if (panel == null)
            {
                DestroyPopoverNow();
                return null;
            }

            // over the Stone the old slate takes the false door's place (frame x 548-1372 of the 1920 stage, P3)
            if (_v6 != null) GoTheme.Place(panel.Root, 0.285f, y0, 0.715f, y1);
            else GoTheme.Place(panel.Root, 0.06f, y0, 0.94f, y1);
            _popover = panel;
            return panel;
        }

        void ClosePopover()
        {
            if (_popoverHost == null) return;
            var host = _popoverHost;
            var panel = _popover;
            _popoverHost = null;
            _popover = null;

            if (!Application.isPlaying || panel == null)
            {
                if (Application.isPlaying) Destroy(host);
                else DestroyImmediate(host);
                return;
            }

            // Dim drops out while the slate sheets away; the host follows once the panel is gone.
            var dim = host.transform.Find("Dim");
            if (dim != null)
            {
                var dimImg = dim.GetComponent<Image>();
                if (dimImg != null) dimImg.raycastTarget = false;
                MenuMotion.PlayOn(dim.gameObject, MenuMotion.Fade(MenuMotion.EnsureGroup(dim.gameObject), 1f, 0f, 0.18f));
            }

            panel.Close(() =>
            {
                if (host != null) Destroy(host);
            });
        }

        void DestroyPopoverNow()
        {
            var host = _popoverHost;
            _popoverHost = null;
            _popover = null;
            if (host == null) return;
            if (Application.isPlaying) Destroy(host);
            else DestroyImmediate(host);
        }

        // ── Navigation ──────────────────────────────────────────────────────

        /// <summary>Hub primary: Player vs AI create → START → AR DuelSlice.</summary>
        void OpenPlayerVsAi() => OpenHubOverlay(MenuId.ArDuelCreate);

        /// <summary>Hub: Player vs Player distance scan → START → AR DuelSlice (hotseat).</summary>
        void OpenPlayerVsPlayer() => OpenHubOverlay(MenuId.ArDuelPvpCreate);

        void OpenFreeView() => OpenHubOverlay(MenuId.FreeView);

        void OpenDeckHub() => OpenHubOverlay(MenuId.DeckCollection);

        void OpenBazaar() => OpenHubOverlay(MenuId.Bazaar);

        void OpenStory() => OpenHubOverlay(MenuId.StorySeason);

        void OpenSettings() => OpenHubOverlay(MenuId.Settings);

        /// <summary>One overlay shell for every hub sheet — no stacked canvases, no toast stubs.</summary>
        void OpenHubOverlay(MenuId id)
        {
            FreeUiKit.PlaySelect();
            ScreenRouter.Ensure().Presentation = UiPresentation.NonArPortrait;
            var shell = EnsureHubShell();
            ScreenRouter.Ensure(shell);
            shell.ShowOverlay(id);
        }

        MenuShell _hubShell;

        MenuShell EnsureHubShell()
        {
            if (_hubShell != null) return _hubShell;
            var existing = FindObjectsByType<MenuShell>(FindObjectsSortMode.None);
            foreach (var s in existing)
            {
                if (s != null && s.OverlayOnly)
                {
                    _hubShell = s;
                    return _hubShell;
                }
            }

            _hubShell = MenuShell.CreateOverlay(80);
            return _hubShell;
        }

        void OpenAvatarMenus()
        {
            FreeUiKit.PlaySelect();
            var ui = AvatarCustomizerUI.Ensure(transform);
            ui.OpenProfile(() =>
            {
                AppSession.Ensure().RefreshFromStore();
                Toast("Avatar saved. Re-open hub to refresh header portrait.");
            });
        }

        void ShowInventory()
        {
            FreeUiKit.PlaySelect();
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                Toast("No account.");
                return;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();

            OpenHubOverlay(MenuId.Inventory);
        }

        void Toast(string msg)
        {
            if (_status != null) _status.text = msg;
            Debug.Log("[WRLDZ] Hub: " + msg);
        }
    }
}
