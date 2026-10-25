using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Every overlay sheet in the game (deck · inventory · artifacts · tome · story · bazaar ·
    /// trade · settings · format · opponent · tournament · zone prompt · AR companion) is framed
    /// here, so this is where the Eldritch Solid Vision chrome reaches them.
    ///
    /// Phone: lapis/sandstone <see cref="HieroglyphPanel"/> (Secondary) over the dusk dim so the
    /// map stays visible. AR: the same panel in the Eldritch tier, compact and glanceable on the
    /// left-arm disk. Both wear the cartouche title, the animated glyph ring and tentacle corners.
    ///
    /// Frozen contract — 18 call sites destructure <see cref="Frame"/> and MenuSmokeTest looks the
    /// panels up by name: the roots stay "PhoneMenu_"/"ArHoloMenu_" + title, the sheet keeps a
    /// direct child "BodyWell" (SettingsScreen recolors it) and a Button named "Close".
    ///
    /// P3 (BUILD_PLAN §6 P3 step 4): under a SanctuaryChrome marker (the overlay shells' ModalHost) the phone sheet is the
    /// v6.3 false door (<see cref="FalseDoorFrame"/>, revealed by the Puzzle-lock) at the window anchors over the map and at
    /// the Stone's contract rect over the Stone; the same names, the same Frame, the same HubChrome header / well / BACK.
    /// The AR frame keeps today's look. P4: the deck builder (BuildFrame deckHall) is the v6.3 offering hall (<see cref="DeckHall"/>): the
    /// same Frame (PhoneMenu_ root, BodyWell → BodyHost, the Close chip, Title / Subtitle), full screen.
    /// </summary>
    public static class DualMenuPresenter
    {
        /// <summary>Panel tier per presentation — lapis stone on phone, abyssal violet in AR.</summary>
        const EldritchTier PhoneTier = EldritchTier.Secondary;
        const EldritchTier ArTier = EldritchTier.Eldritch;

        /// <summary>Cartouche band + its top offset in HieroglyphPanel (64 + 14), plus breathing room.</summary>
        const float HeadBandPx = 88f;
        const float SubtitlePx = 32f;
        const float ChipPx = 56f;   // touch-target floor

        public struct Frame
        {
            public RectTransform Root;
            public RectTransform BodyHost;
            public RectTransform Dim;
            public Text Title;
            public Text Subtitle;
            public UiPresentation Presentation;
        }

        /// <summary>
        /// Hide and destroy a frame. Must not DestroyImmediate from a Close onClick —
        /// the chip lives on the panel.
        /// </summary>
        public static void Dismiss(Frame frame)
        {
            // P3 (D19): the frame still goes at once; a content-free copy of the false door plays its close
            if (frame.Root != null && frame.Root.TryGetComponent(out FalseDoorFrame door))
                FrameGhost.Play(door, frame.Dim != null ? frame.Dim.GetComponent<Image>() : null);
            if (frame.Dim != null)
                FloatingPanel.DestroyDeferred(frame.Dim.gameObject);
            if (frame.Root != null)
                FloatingPanel.DestroyDeferred(frame.Root.gameObject);
        }

        /// <summary>
        /// Build chrome for the active presentation. Uses router presentation if none passed.
        /// </summary>
        /// <param name="dense">
        /// True for grid tools (deck builder, collection, binder) that need the screen rather than
        /// a glanceable sheet. The Compact chrome preset is ~72%x64%, which leaves a deck board
        /// with unreadable card cells — dense screens get a near-full-bleed window instead.
        /// </param>
        /// <param name="deckHall">
        /// P4: the deck builder's own opt-in to the v6.3 offering hall (<see cref="DeckHall"/>). Only DeckCollectionScreen
        /// passes it: other dense sheets (CHOOSE YOUR OPPONENT, DUEL SETUP) keep today's dense frame.
        /// </param>
        public static Frame BuildFrame(
            Transform modalHost,
            string title,
            string subtitle,
            Action onClose,
            UiPresentation? force = null,
            bool dense = false,
            bool deckHall = false)
        {
            // explicit != null, not ?.: ScreenRouter is a MonoBehaviour (Unity null)
            var router = ScreenRouter.Instance;
            var presentation = force ?? (router != null ? router.Presentation : UiPresentation.NonArPortrait);

            return presentation == UiPresentation.ArDiskHolo
                ? BuildArFrame(modalHost, title, subtitle, onClose, dense)
                : BuildPhoneFrame(modalHost, title, subtitle, onClose, dense, deckHall);
        }

        /// <summary>Close avatar / leftover sheets so they cannot sit on top of the next menu.</summary>
        public static void HideTransientMenus()
        {
            var av = UnityEngine.Object.FindAnyObjectByType<AvatarCustomizerUI>();
            if (av != null) av.HideAll();
        }

        public static Frame BuildPhoneFrame(
            Transform modalHost, string title, string subtitle, Action onClose, bool dense = false, bool deckHall = false)
        {
            // P4 (BUILD_PLAN §6 P4): the deck builder (its own deckHall opt-in, never "dense" alone: other dense sheets keep
            // today's frame) is the v6.3 offering hall — the whole screen, its back chip the Close; its rows keep today's
            // painters (the hall paints itself, never under a Sanctuary marker)
            if (dense && deckHall)
            {
                try
                {
                    var hall = DeckHall.Build(modalHost, title, subtitle, onClose);
                    if (hall != null)
                        return new Frame
                        {
                            Root = hall.Root,
                            BodyHost = hall.BodyHost,
                            Dim = null,
                            Title = hall.Title,
                            Subtitle = hall.Subtitle,
                            Presentation = UiPresentation.NonArPortrait
                        };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ MenuV6] deck hall " + title + " fell back to today's frame: " + ex.Message);
                    var broken = modalHost != null ? modalHost.Find("PhoneMenu_" + title) : null;
                    if (broken != null) FloatingPanel.DestroyNow(broken.gameObject);
                }
            }

            // P3: the v6.3 false door under a SanctuaryChrome marker (never the dense deck builder: P4, §11 F3)
            if (!dense && SanctuaryChrome.Under(modalHost))
            {
                try
                {
                    var v6 = BuildSanctuaryFrame(modalHost, title, subtitle, onClose);
                    if (v6.Root != null) return v6;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WRLDZ MenuV6] false-door sheet " + title + " fell back to today's frame: " + ex.Message);
                }
            }

            // Overlay shells keep the map / hub visible. Full hub already has canvas atmosphere.
            // Attaching another night sky here painted dunes over the previous screen (glitch).
            var dimImg = HubChrome.OverlayDim(modalHost, onClose);

            // The panel supplies the slate, ring and carved corners; the HEADER is hub chrome.
            // Passing a null title skips HieroglyphPanel's own cartouche so the two never stack.
            // Hand chrome is not optional here: Tools/hub_overlay_chrome_check.py and
            // ygo_ui_lore_check.py both require HubChrome.HeaderBar on the phone overlay, and the
            // Eldritch reskin quietly replaced it with a bespoke cartouche — HeaderBar is itself
            // already reskinned (Millennium-gold QuietFill, gold rule, hieroglyph strip), so the
            // look is unchanged and the contract is kept.
            var panel = HieroglyphPanel.Create(modalHost, "PhoneMenu_" + title, PhoneTier,
                title: null, ring: true, embersOnOpen: true);
            var root = panel != null ? panel.Root : null;
            if (root == null)
                return default;
            // today's look for everything inside it, even under a v6.3 host (the deck builder until P4)
            if (SanctuaryChrome.Of(modalHost) != null) SanctuaryChrome.Mark(root.gameObject, false);

            MenuChromePrefs.GetWindowAnchors(UiPresentation.NonArPortrait,
                out var x0, out var y0, out var x1, out var y1);
            if (dense) { x0 = 0.015f; y0 = 0.020f; x1 = 0.985f; y1 = 0.980f; }
            HubChrome.Place(root, x0, y0, x1, y1);

            var hasSub = !string.IsNullOrEmpty(subtitle);
            // HeaderBar builds title, subtitle and the close chip together.
            HubChrome.HeaderBar(root, title, subtitle, onClose, out var titleT, out var subT);

            // Body well stays a DIRECT child named "BodyWell": SettingsScreen recolors it when the
            // user cycles chrome opacity (frame.Root.Find("BodyWell")). RectMask2D clips content.
            var well = MountWell(root, 0.055f, 0.118f, 0.945f, hasSub ? 0.850f : 0.878f, 0.52f);
            MountFooterBack(root, onClose);

            var body = new GameObject("BodyHost", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(well != null ? well : root, false);
            FloatingPanel.Stretch(body);

            panel.Open();
            Debug.Log("[WRLDZ Eldritch] sheet · PhoneMenu_" + title + " · " + WrldzBuild.Stamp);

            return new Frame
            {
                Root = root,
                BodyHost = body,
                Dim = dimImg != null ? dimImg.rectTransform : null,
                Title = titleT,
                Subtitle = subT,
                Presentation = UiPresentation.NonArPortrait
            };
        }

        public static Frame BuildArFrame(
            Transform modalHost, string title, string subtitle, Action onClose, bool dense = false)
        {
            // Compact holo panel — glanceable, not full-screen binder chrome. No dim: the AR
            // field behind the sheet is the point.
            var panel = HieroglyphPanel.Create(modalHost, "ArHoloMenu_" + title, ArTier,
                title, ring: true, embersOnOpen: true);
            var root = panel != null ? panel.Root : null;
            if (root == null)
                return default;
            // the AR holo keeps its own look (BUILD_PLAN §6 P3: the AR frame is not touched)
            if (SanctuaryChrome.Of(modalHost) != null) SanctuaryChrome.Mark(root.gameObject, false);

            // Dense grid tools need the volume even in AR; everything else stays glanceable
            // so the field behind the sheet is still readable.
            if (dense) FloatingPanel.Place(root, 0.04f, 0.07f, 0.96f, 0.93f);
            else       FloatingPanel.Place(root, 0.12f, 0.18f, 0.88f, 0.78f);

            var hasSub = !string.IsNullOrEmpty(subtitle);
            var subT = MountSubtitle(panel, subtitle);
            MountCloseChip(root, onClose);

            var well = MountWell(root, 0.062f, 0.070f, 0.938f, hasSub ? 0.836f : 0.866f, 0.40f);

            var body = new GameObject("BodyHost", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(well != null ? well : root, false);
            FloatingPanel.Stretch(body);

            panel.Open();
            Debug.Log("[WRLDZ Eldritch] sheet · ArHoloMenu_" + title + " · " + WrldzBuild.Stamp);

            return new Frame
            {
                Root = root,
                BodyHost = body,
                Dim = null,
                Title = panel.TitleText,
                Subtitle = subT,
                Presentation = UiPresentation.ArDiskHolo
            };
        }

        // ------------------------------------------------------------------ the v6.3 false door (P3)

        /// <summary>
        /// The false-door sheet: the dusk dim (HubChrome.OverlayDim, v6.3 under the marker), the root "PhoneMenu_" + title at
        /// F grown by the frame's margin (over the map: GetWindowAnchors of the menu's size; over the Stone: the contract rect
        /// of the Stone's stage, SPEC §9.3), the frame (FalseDoorFrame.AnyRect), the HubChrome header on the lintel, the body
        /// well below it (a direct child "BodyWell") and the BACK footer.
        /// </summary>
        static Frame BuildSanctuaryFrame(Transform modalHost, string title, string subtitle, Action onClose)
        {
            var overStone = SanctuaryChrome.OverStone(modalHost);
            var lay = SanctuaryPaint.Lay(modalHost);
            var json = SheetData.Root(lay);
            if (json == null) return default;
            var k = SheetDraw.CanvasK(modalHost);
            var stage = SheetData.Stage(json, lay);

            var dimImg = HubChrome.OverlayDim(modalHost, onClose);
            var go = new GameObject("PhoneMenu_" + title, typeof(RectTransform));
            go.transform.SetParent(modalHost, false);
            var root = go.GetComponent<RectTransform>();
            // nothing half-built stays under the host when this throws (BuildPhoneFrame then builds today's frame instead:
            // a stray dim would still take every tap and a second PhoneMenu_ root would confuse the name lookups)
            try
            {
                var hosts = RJ.D(RJ.D(json, "frames"), "hosts");
                string crackKey;
                Rect F;
                float lh;
                var m = FalseDoorFrame.Margin;
                if (overStone)
                {
                    // the Stone's contract rect (the DUEL sheet's frame), centred in the canvas as the Stone's stage is
                    F = SheetData.Frame(RJ.D(RJ.D(RJ.D(json, "geo"), "cf"), "frame"));
                    lh = (float)RJ.N(RJ.D(hosts, "host_compact"), "lh", lay == MenuLayoutKind.Landscape ? 108 : 150);
                    crackKey = "stone_contract";
                    root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
                    root.pivot = new Vector2(.5f, .5f);
                    root.sizeDelta = new Vector2((F.width + 2 * m) * k, (F.height + 2 * m) * k);
                    root.anchoredPosition = new Vector2((F.center.x - stage.x / 2) * k, -(F.center.y - stage.y / 2) * k);
                }
                else
                {
                    MenuChromePrefs.GetWindowAnchors(UiPresentation.NonArPortrait, out var x0, out var y0, out var x1, out var y1);
                    var key = MenuChromePrefs.Size switch
                    {
                        MenuChromePrefs.SizeLevel.Medium => "host_medium",
                        MenuChromePrefs.SizeLevel.Large => "host_large",
                        _ => "host_compact"
                    };
                    crackKey = key;
                    var host = RJ.D(hosts, key);
                    lh = (float)RJ.N(host, "lh", lay == MenuLayoutKind.Landscape ? 108 : 150);
                    F = HostRect(modalHost, x0, y0, x1, y1, k, stage, SheetData.Frame(RJ.D(host, "F")));
                    HubChrome.Place(root, x0, y0, x1, y1);
                    root.offsetMin = new Vector2(-m * k, -m * k);
                    root.offsetMax = new Vector2(m * k, m * k);
                }

                var door = FalseDoorFrame.AnyRect(root, lay, F, lh, crackKey, dimImg, k, overStone);
                if (door == null)
                {
                    FloatingPanel.DestroyNow(go);
                    if (dimImg != null) FloatingPanel.DestroyNow(dimImg.gameObject);
                    return default;
                }

                var hasSub = !string.IsNullOrEmpty(subtitle);
                var header = HubChrome.HeaderBar(root, title, subtitle, onClose, out var titleT, out var subT);
                // the header sits on the lintel (lh stage px under the frame's top line), the well right under it
                header.anchorMin = new Vector2(0f, 1f);
                header.anchorMax = new Vector2(1f, 1f);
                header.pivot = new Vector2(.5f, 1f);
                header.offsetMin = new Vector2((m + 30f) * k, -(m + lh) * k);
                header.offsetMax = new Vector2(-(m + 30f) * k, -(m + 4f) * k);

                // Body well stays a DIRECT child named "BodyWell": SettingsScreen recolors it when the
                // user cycles chrome opacity (frame.Root.Find("BodyWell")). RectMask2D clips content.
                var well = MountWell(root, 0.055f, 0.118f, 0.945f, hasSub ? 0.850f : 0.878f, 0.52f);
                if (well != null)
                {
                    well.anchorMax = new Vector2(well.anchorMax.x, 1f);
                    well.offsetMax = new Vector2(well.offsetMax.x, -(m + lh + 14f) * k);
                }

                var back = MountFooterBack(root, onClose);
                var body = new GameObject("BodyHost", typeof(RectTransform)).GetComponent<RectTransform>();
                body.SetParent(well != null ? well : root, false);
                FloatingPanel.Stretch(body);
                door.Track(header.gameObject);
                if (well != null) door.Track(well.gameObject);
                if (back != null) door.Track(back.gameObject);
                Debug.Log("[WRLDZ MenuV6] sheet · PhoneMenu_" + title + " · false door " + (overStone ? "over the Stone" : crackKey) + " · " + WrldzBuild.Stamp);

                return new Frame
                {
                    Root = root,
                    BodyHost = body,
                    Dim = dimImg != null ? dimImg.rectTransform : null,
                    Title = titleT,
                    Subtitle = subT,
                    Presentation = UiPresentation.NonArPortrait
                };
            }
            catch
            {
                FloatingPanel.DestroyNow(go);
                if (dimImg != null) FloatingPanel.DestroyNow(dimImg.gameObject);
                throw;
            }
        }

        /// <summary>The window anchors' rect in stage px for this canvas (its stage fit); the host's recorded F when the canvas
        /// has no size yet (an edit-mode build).</summary>
        static Rect HostRect(Transform modalHost, float x0, float y0, float x1, float y1, float k, Vector2 stage, Rect fallback)
        {
            var cv = modalHost != null ? modalHost.GetComponentInParent<Canvas>() : null;
            var rt = cv != null && cv.rootCanvas != null ? cv.rootCanvas.transform as RectTransform : null;
            if (rt == null || rt.rect.width <= 1f || rt.rect.height <= 1f || k <= 0f) return fallback;
            float W = rt.rect.width, H = rt.rect.height;
            float ox = (W - stage.x * k) * .5f, oy = (H - stage.y * k) * .5f;
            return new Rect((x0 * W - ox) / k, ((1f - y1) * H - oy) / k, (x1 - x0) * W / k, (y1 - y0) * H / k);
        }

        // ------------------------------------------------------------------ shared chrome

        /// <summary>
        /// Content well: HubChrome builds it (name "BodyWell" + RectMask2D, both load-bearing),
        /// then it is re-tinted to a translucent abyss so the slate's lapis, scanlines and veins
        /// still read around the content instead of being buried under an opaque navy inset.
        /// </summary>
        static RectTransform MountWell(RectTransform root, float x0, float y0, float x1, float y1,
            float alpha)
        {
            var well = HubChrome.BodyWell(root, x0, y0, x1, y1);
            if (well == null) return null;
            var img = well.GetComponent<Image>();
            if (img != null && !SanctuaryChrome.Under(root))   // v6.3: the false door's own fill is the ground
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = EldritchPalette.WithAlpha(EldritchPalette.Abyss, alpha);
                img.raycastTarget = true;
            }

            return well;
        }

        /// <summary>
        /// Caption under the cartouche. Pixel-anchored to the top edge, not normalized: the
        /// cartouche band is a fixed 64 px, so a percentage would drift with sheet height.
        /// </summary>
        static Text MountSubtitle(HieroglyphPanel panel, string subtitle)
        {
            if (panel == null || string.IsNullOrEmpty(subtitle)) return null;
            var t = FloatingPanel.Caption(panel.transform, subtitle, 15);
            t.name = "Subtitle";
            t.alignment = TextAnchor.UpperCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;

            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.10f, 1f);
            rt.anchorMax = new Vector2(0.90f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, SubtitlePx);
            rt.anchoredPosition = new Vector2(0f, -HeadBandPx);
            return t;
        }

        /// <summary>
        /// Top-right dismiss. MenuSmokeTest resolves it by the name "Close" and invokes its
        /// Button, so the name and the component both stay put.
        /// </summary>
        static Button MountCloseChip(RectTransform root, Action onClose)
        {
            if (root == null || onClose == null) return null;
            var ab = ArcaneButton.Create(root, "X", onClose, EldritchTier.Danger,
                titleSize: 20, centerTitle: true);
            if (ab == null) return null;

            ab.gameObject.name = "Close";
            var le = ab.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;

            var rt = ab.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(ChipPx, ChipPx);
                rt.anchoredPosition = new Vector2(-18f, -18f);   // clear of the glyph ring
            }

            return ab.Button;
        }

        /// <summary>Footer dismiss on phone sheets — thumb-reachable, gold on obsidian.</summary>
        static Button MountFooterBack(RectTransform root, Action onClose)
        {
            if (root == null || onClose == null) return null;
            // Hub chrome, not a bespoke ArcaneButton: hub_overlay_chrome_check.py requires
            // HubChrome.FooterBack here. It builds on the same reskinned Capsule, so the BACK
            // capsule looks identical while the overlay keeps the shared chrome contract.
            var btn = HubChrome.FooterBack(root, onClose);
            if (btn == null) return null;
            var le = btn.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;
            return btn;
        }

        /// <summary>Prefer AR holo when a live AR duel is active; else phone portrait.</summary>
        public static UiPresentation ResolveDefaultPresentation()
        {
            if (ScreenRouter.Instance != null &&
                ScreenRouter.Instance.Presentation != UiPresentation.NonArPortrait)
                return ScreenRouter.Instance.Presentation;

            // If DuelSlice is loaded, default menus that open mid-session to AR holo
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
            if (scene.IndexOf("Duel", StringComparison.OrdinalIgnoreCase) >= 0)
                return UiPresentation.ArDiskHolo;

            return UiPresentation.NonArPortrait;
        }
    }
}
