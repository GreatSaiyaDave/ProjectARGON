using System;
using System.Collections;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Player vs Player — same auto surface scan as PvE.
    /// Marks Player 1 automatically, walk to Player 2 stand point, stop to lock
    /// arena separation, then START AR duel.
    /// </summary>
    public class PlayerVsPlayerCreateScreen : MonoBehaviour
    {
        ArenaSurfaceScanner _scan;
        SurfaceScanSheet.Bindings _ui;
        Coroutine _tick;
        Action _onClose;

        public static RectTransform Build(Transform parent, Action onClose)
        {
            var host = new GameObject("PlayerVsPlayerCreate", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var ui = host.AddComponent<PlayerVsPlayerCreateScreen>();
            ui.BuildUi(host.GetComponent<RectTransform>(), onClose);
            return host.GetComponent<RectTransform>();
        }

        public static GameObject OpenOverlay(Transform lifetimeHost, Action onClosed = null)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            var canvas = WrldzTheme.Canvas("PvpCreateCanvas", 90);
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.UmbraxRift, showAgeCaption: false);
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null) UnityEngine.Object.Destroy(host);
            }

            Build(canvas, Close);
            return host;
        }

        void BuildUi(RectTransform root, Action onClose)
        {
            _onClose = onClose;
            _scan = new ArenaSurfaceScanner();
            _scan.Begin(ArenaSurfaceScanner.ScanMode.Pvp);
            _scan.OnChanged += RefreshLabels;

            var frame = DualMenuPresenter.BuildFrame(
                root,
                "PLAYER VS PLAYER",
                "Player 1 is marked. Walk to Player 2. Stop to lock the field.",
                () =>
                {
                    StopTick();
                    onClose?.Invoke();
                });

            _ui = SurfaceScanSheet.Mount(frame.BodyHost, ArenaSurfaceScanner.ScanMode.Pvp,
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
            FreeUiKit.PlayConfirm();
            var cfg = _scan.BuildMatchConfig();
            cfg.EntrySource = AppSession.SceneOverworld;
            StopTick();
            _onClose?.Invoke();
            AppSession.Ensure().StartArDuel(cfg);
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
