using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Dual-menu rule used by almost every systems screen:
    /// <list type="bullet">
    /// <item><see cref="UiPresentation.NonArPortrait"/> — full phone / Editor portrait sheet</item>
    /// <item><see cref="UiPresentation.ArDiskHolo"/> — compact holographic panel (left-arm disk glanceable)</item>
    /// </list>
    /// Content screens call <see cref="BuildFrame"/> then fill <paramref name="bodyHost"/>.
    /// </summary>
    public static class DualMenuPresenter
    {
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
        public static Frame BuildFrame(
            Transform modalHost,
            string title,
            string subtitle,
            Action onClose,
            UiPresentation? force = null)
        {
            var presentation = force
                               ?? ScreenRouter.Instance?.Presentation
                               ?? UiPresentation.NonArPortrait;

            return presentation == UiPresentation.ArDiskHolo
                ? BuildArFrame(modalHost, title, subtitle, onClose)
                : BuildPhoneFrame(modalHost, title, subtitle, onClose);
        }

        /// <summary>Close avatar / leftover sheets so they cannot sit on top of the next menu.</summary>
        public static void HideTransientMenus()
        {
            var av = UnityEngine.Object.FindAnyObjectByType<AvatarCustomizerUI>();
            av?.HideAll();
        }

        public static Frame BuildPhoneFrame(
            Transform modalHost, string title, string subtitle, Action onClose)
        {
            // Overlay shells keep the map / hub visible. Full hub already has canvas atmosphere.
            // Attaching another night sky here painted dunes over the previous screen (glitch).

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(modalHost, false);
            FloatingPanel.Stretch(dim.GetComponent<RectTransform>());
            dim.transform.SetAsFirstSibling();
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = MenuChromePrefs.DimColor;
            dim.GetComponent<Button>().targetGraphic = dimImg;
            dim.GetComponent<Button>().transition = Selectable.Transition.None;
            if (onClose != null)
                dim.GetComponent<Button>().onClick.AddListener(() => onClose());

            var panel = FloatingPanel.Create(modalHost, "PhoneMenu_" + title, goldEdge: false);
            // Near full-bleed portrait sheet — keep opaque enough for button contrast
            FloatingPanel.Place(panel, 0.02f, 0.06f, 0.98f, 0.94f);
            var pImg = panel.GetComponent<Image>();
            if (pImg != null)
            {
                var holo = ImagineAssets.MenuHoloSheet() ?? ImagineAssets.PanelMenuGlass() ?? pImg.sprite;
                if (holo != null)
                {
                    pImg.sprite = holo;
                    pImg.type = holo.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                }
                pImg.color = Color.white;
            }
            MenuHoloPulse.Attach(panel.gameObject, scan: true, breathe: false);

            // Sit inside the gold L-corners of menu_holo_sheet (not the glow pad).
            var well = InnerWell(panel, 0.055f, 0.080f, 0.945f, 0.920f);
            var hasSub = !string.IsNullOrEmpty(subtitle);
            var (titleT, subT, body) = MountChrome(well, title, subtitle, onClose, titleSize: 26, subSize: 14);

            FloatingPanel.Place(body, 0.012f, 0.012f, 0.988f, hasSub ? 0.78f : 0.86f);

            return new Frame
            {
                Root = panel,
                BodyHost = body,
                Dim = dim.GetComponent<RectTransform>(),
                Title = titleT,
                Subtitle = subT,
                Presentation = UiPresentation.NonArPortrait
            };
        }

        public static Frame BuildArFrame(
            Transform modalHost, string title, string subtitle, Action onClose)
        {
            // Compact holo panel — glanceable, not full-screen binder chrome
            var panel = FloatingPanel.Create(modalHost, "ArHoloMenu_" + title, goldEdge: false);
            FloatingPanel.Place(panel, 0.12f, 0.18f, 0.88f, 0.78f);

            var rim = panel.GetComponent<Image>();
            if (rim != null)
            {
                var holo = ImagineAssets.MenuHoloSheet() ?? ImagineAssets.PanelHolo() ?? rim.sprite;
                if (holo != null)
                {
                    rim.sprite = holo;
                    rim.type = holo.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                }
                rim.color = Color.white;
            }
            MenuHoloPulse.Attach(panel.gameObject, scan: true, breathe: false);

            var well = InnerWell(panel, 0.070f, 0.100f, 0.930f, 0.900f);
            var hasSub = !string.IsNullOrEmpty(subtitle);
            var (titleT, subT, body) = MountChrome(well, title, subtitle, onClose, titleSize: 18, subSize: 12);
            if (titleT != null) titleT.color = DuelystUi.Cyan;

            FloatingPanel.Place(body, 0.016f, 0.020f, 0.984f, hasSub ? 0.74f : 0.84f);

            return new Frame
            {
                Root = panel,
                BodyHost = body,
                Dim = null,
                Title = titleT,
                Subtitle = subT,
                Presentation = UiPresentation.ArDiskHolo
            };
        }

        /// <summary>
        /// Content well inset past the hologram 9-slice so title / close / body
        /// sit on the glass, not in the transparent frame.
        /// </summary>
        static RectTransform InnerWell(RectTransform panel, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("InnerWell", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            go.transform.SetParent(panel, false);
            FloatingPanel.Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.03f, 0.05f, 0.08f, 0.22f);
            img.raycastTarget = true;
            return go.GetComponent<RectTransform>();
        }

        static (Text title, Text subtitle, RectTransform body) MountChrome(
            RectTransform well, string title, string subtitle, Action onClose,
            int titleSize, int subSize)
        {
            var hasSub = !string.IsNullOrEmpty(subtitle);
            var titleT = FloatingPanel.Title(well, title ?? "", titleSize);
            FloatingPanel.Place(titleT.rectTransform, 0.02f, hasSub ? 0.90f : 0.88f, 0.80f, 0.99f);
            titleT.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleT.verticalOverflow = VerticalWrapMode.Truncate;

            Text subT = null;
            if (hasSub)
            {
                subT = FloatingPanel.Body(well, subtitle, subSize);
                FloatingPanel.Place(subT.rectTransform, 0.02f, 0.82f, 0.80f, 0.90f);
                subT.color = new Color(0.90f, 0.94f, 1f, 0.96f);
            }

            var close = MenuCommandButton.Create(well, "X", onClose,
                MenuCommandButton.Kind.Secondary, centerTitle: true);
            close.name = "Close";
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(64f, 64f);
            closeRt.anchoredPosition = new Vector2(-6f, -6f);

            var body = new GameObject("BodyHost", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(well, false);
            return (titleT, subT, body);
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
