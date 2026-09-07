using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Shared PLAYER VS AI / PLAYER VS PLAYER scan body: meter well + equal action row.
    /// Both screens use DualMenuPresenter chrome, then this grid.
    /// </summary>
    public static class SurfaceScanSheet
    {
        public struct Bindings
        {
            public Text Step;
            public Text Live;
            public Text Status;
            public Image Well;
            public Image MeterFill;
            public Button Lock;
            public Button Rescan;
            public Button Start;
        }

        public static Bindings Mount(RectTransform body, ArenaSurfaceScanner.ScanMode mode,
            Action onLock, Action onRescan, Action onStart)
        {
            var b = new Bindings();

            var wellGo = new GameObject("MeterWell", typeof(RectTransform), typeof(Image));
            wellGo.transform.SetParent(body, false);
            FloatingPanel.Grid.Full(wellGo.GetComponent<RectTransform>(), 0.38f, 0.96f);
            b.Well = wellGo.GetComponent<Image>();
            b.Well.sprite = UiFoundation.WhiteSprite();
            b.Well.type = Image.Type.Simple;
            b.Well.color = HubChrome.WellFill;
            b.Well.raycastTarget = false;
            HubChrome.LiftPlate(b.Well, DuelystUi.Cyan);

            var stepSeed = mode == ArenaSurfaceScanner.ScanMode.Pvp
                ? "WALK TO PLAYER 2 · AUTO LOCK WHEN STILL"
                : "WALK SURFACE · AUTO LOCK WHEN STILL";
            b.Step = FloatingPanel.Body(wellGo.transform, stepSeed, 15);
            FloatingPanel.Place(b.Step.rectTransform, 0.05f, 0.78f, 0.95f, 0.96f);
            b.Step.alignment = TextAnchor.MiddleCenter;
            WrldzType.StyleGoldTitle(b.Step, 16);
            b.Step.alignment = TextAnchor.MiddleCenter;
            b.Step.horizontalOverflow = HorizontalWrapMode.Wrap;
            b.Step.resizeTextForBestFit = false;

            b.Live = FloatingPanel.Title(wellGo.transform, "0.0 m", 28);
            FloatingPanel.Place(b.Live.rectTransform, 0.06f, 0.30f, 0.94f, 0.74f);
            b.Live.alignment = TextAnchor.MiddleCenter;
            WrldzType.StyleCyan(b.Live, 28);
            b.Live.alignment = TextAnchor.MiddleCenter;
            b.Live.horizontalOverflow = HorizontalWrapMode.Overflow;
            b.Live.verticalOverflow = VerticalWrapMode.Overflow;
            b.Live.resizeTextForBestFit = false;

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(wellGo.transform, false);
            FloatingPanel.Place(track.GetComponent<RectTransform>(), 0.10f, 0.10f, 0.90f, 0.22f);
            var trackImg = track.GetComponent<Image>();
            trackImg.sprite = UiFoundation.WhiteSprite();
            trackImg.color = new Color(0.08f, 0.12f, 0.18f, 0.90f);
            trackImg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(track.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0.08f, 1f);
            fillRt.offsetMin = new Vector2(2f, 2f);
            fillRt.offsetMax = new Vector2(-2f, -2f);
            b.MeterFill = fillGo.GetComponent<Image>();
            b.MeterFill.sprite = UiFoundation.WhiteSprite();
            b.MeterFill.color = DuelystUi.Cyan;
            b.MeterFill.raycastTarget = false;

            b.Status = FloatingPanel.Body(body, "", 13);
            FloatingPanel.Grid.Full(b.Status.rectTransform, 0.26f, 0.34f);
            b.Status.alignment = TextAnchor.MiddleCenter;
            b.Status.color = DuelystUi.TextMuted;
            b.Status.horizontalOverflow = HorizontalWrapMode.Wrap;

            b.Lock = HubChrome.Capsule(body, "LOCK HERE", onLock, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 16);
            b.Rescan = HubChrome.Capsule(body, "RESCAN", onRescan, MenuCommandButton.Kind.Primary,
                centerTitle: true, titleSize: 16);
            FloatingPanel.Grid.Pair(
                b.Lock.GetComponent<RectTransform>(),
                b.Rescan.GetComponent<RectTransform>(),
                0.14f, 0.24f);

            b.Start = HubChrome.Capsule(body, "START DUEL", onStart, MenuCommandButton.Kind.Gold,
                centerTitle: true, titleSize: 20);
            FloatingPanel.Grid.Full(b.Start.GetComponent<RectTransform>(), 0.02f, 0.12f);

            return b;
        }

        public static void Refresh(Bindings b, ArenaSurfaceScanner scan)
        {
            if (scan == null) return;

            var ready = scan.State == ArenaSurfaceScanner.Phase.Ready;
            var meters = ready ? scan.SurfaceDepthM : scan.LiveMeters;

            if (b.Step != null)
            {
                b.Step.text = scan.State switch
                {
                    ArenaSurfaceScanner.Phase.Scanning when scan.Mode == ArenaSurfaceScanner.ScanMode.Pvp
                        => "WALK TO PLAYER 2 · AUTO LOCK WHEN STILL",
                    ArenaSurfaceScanner.Phase.Scanning
                        => "WALK SURFACE · AUTO LOCK WHEN STILL",
                    ArenaSurfaceScanner.Phase.Ready
                        => "SURFACE LOCKED · START WHEN READY",
                    _ => "SURFACE SCAN"
                };
            }

            if (b.Live != null)
                b.Live.text = $"{meters:0.0} m";

            if (b.Status != null)
                b.Status.text = scan.StatusLine ?? "";

            if (b.MeterFill != null)
            {
                var t = Mathf.Clamp01(meters / ArDuelMatchConfig.MaxSeparationM);
                var rt = b.MeterFill.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(Mathf.Max(0.06f, t), 1f);
                rt.offsetMin = new Vector2(2f, 2f);
                rt.offsetMax = new Vector2(-2f, -2f);
                b.MeterFill.color = ready ? DuelystUi.GoldHot : DuelystUi.Cyan;
            }

            if (b.Well != null)
            {
                var ol = b.Well.GetComponent<Outline>();
                if (ol != null)
                    ol.effectColor = ready
                        ? new Color(0.95f, 0.82f, 0.32f, 0.85f)
                        : new Color(0.35f, 0.82f, 0.98f, 0.70f);
            }

            if (b.Lock != null)
                b.Lock.interactable = scan.State == ArenaSurfaceScanner.Phase.Scanning;
            if (b.Start != null)
                b.Start.interactable = ready || scan.LiveMeters >= 1.25f;
        }
    }
}
