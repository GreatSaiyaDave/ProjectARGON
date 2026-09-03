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
        SurfaceScanSheet.Bindings _ui;
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
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null)
                    UnityEngine.Object.Destroy(host);
            }

            Build(canvas, Close);
            return host;
        }

        void BuildUi(RectTransform root, Action onClose)
        {
            _onClose = onClose;
            _scan = new ArenaSurfaceScanner();
            _scan.Begin(ArenaSurfaceScanner.ScanMode.Pve);
            _scan.OnChanged += RefreshLabels;

            var frame = DualMenuPresenter.BuildFrame(
                root,
                "PLAYER VS AI",
                "Walk the length of your play space. The AR arena sizes itself to that span.",
                () =>
                {
                    StopTick();
                    onClose?.Invoke();
                });

            _ui = SurfaceScanSheet.Mount(frame.BodyHost, ArenaSurfaceScanner.ScanMode.Pve,
                onLock: () =>
                {
                    FreeUiKit.PlaySelect();
                    _scan.ConfirmHere();
                },
                onRescan: () =>
                {
                    FreeUiKit.PlayClick();
                    _scan.Reset();
                },
                onStart: StartDuel);

            RefreshLabels();
            _tick = StartCoroutine(LiveTick());
        }

        void StartDuel()
        {
            var session = AppSession.Ensure();
            var cfg = _scan.BuildMatchConfig();
            if (AppSession.RequiresOriginalBadgeForLaunch(cfg.Launch)
                && !session.CanStartConstructedPvAi())
            {
                if (_ui.Status != null)
                    _ui.Status.text = "ERAZ · LOCKED — finish tutorial";
                return;
            }

            FreeUiKit.PlayConfirm();
            cfg.FormatTitle = "Player vs AI · Surface Scan";
            cfg.FormatId = "pvai";
            cfg.EntrySource = AppSession.SceneOverworld;
            StopTick();
            _onClose?.Invoke();
            session.StartArDuel(cfg);
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

        void RefreshLabels() => SurfaceScanSheet.Refresh(_ui, _scan);
    }
}
