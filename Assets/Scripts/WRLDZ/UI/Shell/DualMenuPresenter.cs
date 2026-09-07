using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Dual-menu rule used by almost every systems screen:
    /// <list type="bullet">
    /// <item><see cref="UiPresentation.NonArPortrait"/> — hub-matching phone overlay (capsules over dusk)</item>
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
            var dimImg = HubChrome.OverlayDim(modalHost, onClose);

            var panel = new GameObject("PhoneMenu_" + title, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(modalHost, false);
            MenuChromePrefs.GetWindowAnchors(UiPresentation.NonArPortrait,
                out var x0, out var y0, out var x1, out var y1);
            HubChrome.Place(panel.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var pImg = panel.GetComponent<Image>();
            pImg.raycastTarget = true;
            HubChrome.PaintWell(pImg);

            var (titleT, subT) = (default(Text), default(Text));
            HubChrome.HeaderBar(panel.transform, title, subtitle, onClose, out titleT, out subT);
            var well = HubChrome.BodyWell(panel.transform);
            HubChrome.FooterBack(panel.transform, onClose);

            var body = new GameObject("BodyHost", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(well, false);
            FloatingPanel.Stretch(body);

            Debug.Log("[WRLDZ] HubChrome overlay · PhoneMenu_" + title + " · " + WrldzBuild.Stamp);

            return new Frame
            {
                Root = panel.GetComponent<RectTransform>(),
                BodyHost = body,
                Dim = dimImg.rectTransform,
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
                HubChrome.PaintWell(rim);

            MenuHoloPulse.Attach(panel.gameObject, scan: true, breathe: false);

            var well = HubChrome.BodyWell(panel, 0.070f, 0.080f, 0.930f, 0.900f);
            var hasSub = !string.IsNullOrEmpty(subtitle);
            var (titleT, subT, body) = MountArChrome(well, title, subtitle, onClose);
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

        static (Text title, Text subtitle, RectTransform body) MountArChrome(
            RectTransform well, string title, string subtitle, Action onClose)
        {
            var hasSub = !string.IsNullOrEmpty(subtitle);
            var titleT = FloatingPanel.Title(well, title ?? "", 18);
            titleT.color = DuelystUi.Cyan;
            titleT.fontStyle = FontStyle.Bold | FontStyle.Italic;
            FloatingPanel.Place(titleT.rectTransform, 0.02f, hasSub ? 0.90f : 0.88f, 0.80f, 0.99f);
            titleT.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleT.verticalOverflow = VerticalWrapMode.Truncate;

            Text subT = null;
            if (hasSub)
            {
                subT = FloatingPanel.Body(well, subtitle, 12);
                FloatingPanel.Place(subT.rectTransform, 0.02f, 0.82f, 0.80f, 0.90f);
                subT.color = DuelystUi.TextCream;
            }

            if (onClose != null)
                HubChrome.CloseChip(well, onClose);

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
