using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Player vs AI — auto surface scan sizes the AR arena.
    /// Walk the length of your table/floor (GPS outdoors, pad/WASD indoors);
    /// stop to lock distance, then START.
    /// </summary>
    public class ArDuelCreateScreen : MonoBehaviour
    {
        ArenaSurfaceScanner _scan;
        Text _live;
        Text _status;
        Text _step;
        Button _confirm;
        Button _start;
        Button _rescan;
        Coroutine _tick;
        Action _onClose;

        public static RectTransform Build(Transform parent, Action onClose)
        {
            var host = new GameObject("PlayerVsAiCreate", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var ui = host.AddComponent<ArDuelCreateScreen>();
            ui.BuildUi(host.GetComponent<RectTransform>(), onClose);
            return host.GetComponent<RectTransform>();
        }

        /// <summary>Full-screen overlay (overworld has no MenuShell).</summary>
        public static GameObject OpenOverlay(Transform lifetimeHost, Action onClosed = null)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            var canvas = WrldzTheme.Canvas("PlayerVsAiCreateCanvas", 90);
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.UmbraxRift, showAgeCaption: false);
            var root = WrldzTheme.StretchFill(canvas, "Dim", new Color(0.01f, 0.02f, 0.05f, 0.35f));
            root.GetComponent<Image>().raycastTarget = true;

            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null)
                    UnityEngine.Object.Destroy(host);
            }

            Build(root, Close);
            return host;
        }

        void BuildUi(RectTransform root, Action onClose)
        {
            _onClose = onClose;
            _scan = new ArenaSurfaceScanner();
            _scan.Begin(ArenaSurfaceScanner.ScanMode.Pve);
            _scan.OnChanged += RefreshLabels;

            var panel = FloatingPanel.Create(root, "PvAiPanel", goldEdge: true);
            FloatingPanel.Place(panel, 0.04f, 0.10f, 0.96f, 0.90f);

            var title = FloatingPanel.Title(panel, "PLAYER VS AI", 22);
            FloatingPanel.Place(title.rectTransform, 0.05f, 0.90f, 0.72f, 0.98f);

            var close = FloatingPanel.PrimaryButton(panel, "✕", () =>
            {
                StopTick();
                onClose?.Invoke();
            });
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.82f, 0.90f, 0.96f, 0.98f);

            var blurb = FloatingPanel.Body(panel,
                "Auto surface scan · walk the length of your play space. " +
                "The AR arena sizes itself to that span. Stop walking to lock.",
                13);
            FloatingPanel.Place(blurb.rectTransform, 0.05f, 0.78f, 0.95f, 0.88f);
            blurb.color = DuelystUi.TextMuted;

            _step = FloatingPanel.Body(panel, "SCANNING", 15);
            FloatingPanel.Place(_step.rectTransform, 0.05f, 0.70f, 0.95f, 0.78f);
            _step.color = DuelystUi.GoldHot;

            _live = FloatingPanel.Title(panel, "— m", 32);
            FloatingPanel.Place(_live.rectTransform, 0.05f, 0.50f, 0.95f, 0.68f);
            _live.alignment = TextAnchor.MiddleCenter;
            _live.color = DuelystUi.Cyan;

            _status = FloatingPanel.Body(panel, "", 13);
            FloatingPanel.Place(_status.rectTransform, 0.05f, 0.38f, 0.95f, 0.50f);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.color = DuelystUi.TextMuted;

            _confirm = FloatingPanel.PrimaryButton(panel, "LOCK HERE", () =>
            {
                FreeUiKit.PlaySelect();
                _scan.ConfirmHere();
            }, gold: true);
            FloatingPanel.Place(_confirm.GetComponent<RectTransform>(), 0.08f, 0.24f, 0.48f, 0.36f);

            _rescan = FloatingPanel.PrimaryButton(panel, "RESCAN", () =>
            {
                FreeUiKit.PlayClick();
                _scan.Reset();
            });
            FloatingPanel.Place(_rescan.GetComponent<RectTransform>(), 0.52f, 0.24f, 0.92f, 0.36f);

            _start = FloatingPanel.PrimaryButton(panel, "START AR DUEL", () =>
            {
                var session = AppSession.Ensure();
                var cfg = _scan.BuildMatchConfig();
                if (AppSession.RequiresOriginalBadgeForLaunch(cfg.Launch)
                    && !session.CanStartConstructedPvAi())
                {
                    if (_status != null)
                        _status.text = "ERAZ · LOCKED — finish tutorial";
                    return;
                }

                FreeUiKit.PlayConfirm();
                cfg.FormatTitle = "Player vs AI · Surface Scan";
                cfg.FormatId = "pvai";
                cfg.EntrySource = AppSession.SceneOverworld;
                StopTick();
                onClose?.Invoke();
                session.StartArDuel(cfg);
            }, gold: true);
            FloatingPanel.Place(_start.GetComponent<RectTransform>(), 0.12f, 0.08f, 0.88f, 0.20f);

            RefreshLabels();
            _tick = StartCoroutine(LiveTick());
        }

        IEnumerator LiveTick()
        {
            var last = Time.unscaledTime;
            while (true)
            {
                var now = Time.unscaledTime;
                var dt = now - last;
                last = now;
                _scan?.Tick(dt);
                RefreshLabels();
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }

        void StopTick()
        {
            if (_tick != null)
            {
                StopCoroutine(_tick);
                _tick = null;
            }

            if (_scan != null)
                _scan.OnChanged -= RefreshLabels;
        }

        void OnDestroy() => StopTick();

        void RefreshLabels()
        {
            if (_scan == null) return;

            if (_step != null)
            {
                _step.text = _scan.State switch
                {
                    ArenaSurfaceScanner.Phase.Scanning => "WALK SURFACE · AUTO LOCK WHEN STILL",
                    ArenaSurfaceScanner.Phase.Ready => "SURFACE LOCKED · START WHEN READY",
                    _ => "SURFACE SCAN"
                };
            }

            if (_live != null)
            {
                var m = _scan.State == ArenaSurfaceScanner.Phase.Ready
                    ? _scan.SurfaceDepthM
                    : _scan.LiveMeters;
                _live.text = $"{m:0.0} m";
            }

            if (_status != null)
                _status.text = _scan.StatusLine ?? "";

            if (_confirm != null)
                _confirm.interactable = _scan.State == ArenaSurfaceScanner.Phase.Scanning;
            if (_start != null)
                _start.interactable = _scan.State == ArenaSurfaceScanner.Phase.Ready
                                      || _scan.LiveMeters >= 1.25f;
        }
    }
}
