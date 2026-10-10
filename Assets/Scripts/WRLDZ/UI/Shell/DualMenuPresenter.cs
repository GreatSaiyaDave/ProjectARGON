using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Eldritch;

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
        public static Frame BuildFrame(
            Transform modalHost,
            string title,
            string subtitle,
            Action onClose,
            UiPresentation? force = null,
            bool dense = false)
        {
            var presentation = force
                               ?? ScreenRouter.Instance?.Presentation
                               ?? UiPresentation.NonArPortrait;

            return presentation == UiPresentation.ArDiskHolo
                ? BuildArFrame(modalHost, title, subtitle, onClose, dense)
                : BuildPhoneFrame(modalHost, title, subtitle, onClose, dense);
        }

        /// <summary>Close avatar / leftover sheets so they cannot sit on top of the next menu.</summary>
        public static void HideTransientMenus()
        {
            var av = UnityEngine.Object.FindAnyObjectByType<AvatarCustomizerUI>();
            av?.HideAll();
        }

        public static Frame BuildPhoneFrame(
            Transform modalHost, string title, string subtitle, Action onClose, bool dense = false)
        {
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
            if (img != null)
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
