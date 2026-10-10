using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// AR variant of the main menu — a HieroglyphPanel tethered to the Spirit Dueler hub.
    /// Rows: DUEL HERE · DECK · BAG · ARTIFACTS · TOME · SETTINGS · LEAVE ZONE.
    /// Submenus build into <see cref="ModalHost"/> with <see cref="UiPresentation.ArDiskHolo"/>.
    /// </summary>
    public sealed class ArHoloHubMenu : MonoBehaviour
    {
        public const string PanelTitle = "SPIRIT DUELER";
        public const string DuelHereLabel = "DUEL HERE";
        public const string LeaveLabel = "LEAVE ZONE";

        public ZoneModeSession Session { get; private set; }
        public HieroglyphPanel Panel { get; private set; }
        public RectTransform Root => Panel != null ? Panel.Root : null;
        public RectTransform ModalHost { get; private set; }
        public ArDiskHoloAnchor Anchor { get; private set; }
        public bool IsOpen { get; private set; }
        public bool ModalOpen => _modal != null;

        RectTransform _modal;
        RectTransform _ownCanvas;
        Action _onLeave;
        bool _closing;

        /// <summary>Contract entry: open on the session's canvas, anchored to the player disk hub.</summary>
        public static ArHoloHubMenu Open(ZoneModeSession s)
        {
            Transform host = s != null ? s.CanvasRoot : null;
            RectTransform own = null;
            if (host == null)
            {
                // Standalone (preview / smoke): own canvas, centred, no AR anchor.
                own = WrldzTheme.Canvas("ArHoloHubMenuCanvas", 89);
                host = own;
            }

            var menu = Build(host, s, s != null ? (Action)s.Exit : null);
            // Close() only destroys this component's GameObject — remember the canvas we made so
            // toggling the disk does not leave one empty canvas per open.
            if (menu != null) menu._ownCanvas = own;
            return menu;
        }

        /// <summary>Build on any host. <paramref name="session"/> may be null (no anchor, rows toast).</summary>
        public static ArHoloHubMenu Build(Transform host, ZoneModeSession session = null, Action onLeave = null)
        {
            if (host == null) return null;
            var go = new GameObject("ArHoloHubMenu", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            FloatingPanel.Stretch(rt);

            var menu = go.AddComponent<ArHoloHubMenu>();
            menu.Session = session;
            menu._onLeave = onLeave;
            menu.BuildVisuals(rt);
            return menu;
        }

        void BuildVisuals(RectTransform rt)
        {
            Panel = HieroglyphPanel.Create(rt, "HubPanel", EldritchTier.Eldritch, PanelTitle, ring: true,
                embersOnOpen: true);
            MenuChromePrefs.GetWindowAnchors(UiPresentation.ArDiskHolo, out var x0, out var y0, out var x1, out var y1);
            // Narrower than a sheet: a command list, not a binder.
            var inset = (x1 - x0) * 0.06f;
            FloatingPanel.Place(Panel.Root, x0 + inset, y0, x1 - inset, y1);

            var body = Panel.Body != null ? Panel.Body : Panel.Root;

            var zone = Session != null ? Session.ZoneTitle : "Preview";
            var sub = Label(body, "Zone", zone, 13, EldritchPalette.TextMuted, TextAnchor.MiddleCenter, false);
            FloatingPanel.Place(sub.rectTransform, 0.04f, 0.905f, 0.96f, 0.975f);

            // 7 rows, top → bottom, ≥ 56 design px each at the compact AR window.
            const int rows = 7;
            const float top = 0.885f;
            const float bottom = 0.015f;
            var pitch = (top - bottom) / rows;
            var rowH = pitch * 0.88f;
            var i = 0;
            Row(body, i++, pitch, rowH, top, DuelHereLabel, "Solid Vision · this zone", OnDuelHere);
            Row(body, i++, pitch, rowH, top, "DECK", null, () => OpenModal(h => DeckCollectionScreen.Build(h, CloseModal, UiPresentation.ArDiskHolo)));
            Row(body, i++, pitch, rowH, top, "BAG", null, () => OpenModal(h => InventoryScreen.Build(h, CloseModal, UiPresentation.ArDiskHolo)));
            Row(body, i++, pitch, rowH, top, "ARTIFACTS", null, () => OpenModal(h => ArtifactBoxScreen.Build(h, CloseModal, UiPresentation.ArDiskHolo)));
            Row(body, i++, pitch, rowH, top, "TOME", null, () => OpenModal(h => SystemsSheets.BuildTome(h, CloseModal, UiPresentation.ArDiskHolo)));
            Row(body, i++, pitch, rowH, top, "SETTINGS", null, () => OpenModal(h => SettingsScreen.Build(h, null, CloseModal)));
            Row(body, i, pitch, rowH, top, LeaveLabel, null, OnLeave);

            var modal = new GameObject("ModalHost", typeof(RectTransform));
            modal.transform.SetParent(rt, false);
            ModalHost = modal.GetComponent<RectTransform>();
            FloatingPanel.Stretch(ModalHost);
            ModalHost.SetAsLastSibling();

            var cam = Session != null && Session.Space != null ? Session.Space.StageCamera : null;
            var disk = Session != null ? Session.PlayerDisk : null;
            if (cam != null && disk != null)
            {
                // Tether to the wrist hub target when it exists (stable through blade fold), else the plate.
                var target = Session.HubHit != null ? Session.HubHit.transform : disk.DiskRoot;
                var viewport = Session.Viewport != null ? Session.Viewport.rectTransform : null;
                if (target != null)
                    Anchor = ArDiskHoloAnchor.Attach(Panel.Root, target, cam, viewport);
            }

            Panel.Open();
            IsOpen = true;
            FreeUiKit.PlaySelect();
        }

        static void Row(Transform body, int index, float pitch, float rowH, float top, string title, string blurb,
            Action onClick)
        {
            var y1 = top - index * pitch;
            var y0 = y1 - rowH;
            // contract note: §7 lists every row as Secondary; the CTA reads through its blurb, not tier.
            var btn = ArcaneButton.Create(body, title, onClick, EldritchTier.Secondary, blurb,
                titleSize: 18, centerTitle: string.IsNullOrEmpty(blurb));
            if (btn != null && btn.Rect != null)
                FloatingPanel.Place(btn.Rect, 0.02f, y0, 0.98f, y1);
        }

        static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align,
            bool display)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            WrldzType.Style(t, size, display, heavyOutline: true);
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        void OnDuelHere()
        {
            if (Session == null)
            {
                FreeUiKit.PlayClick();
                Debug.Log("[WRLDZ Eldritch] DUEL HERE (standalone menu — no session)");
                return;
            }

            FreeUiKit.PlayConfirm();
            var s = Session;
            // Launch closes via onClose then hands off to AppSession.StartArDuel.
            ZoneModePrompt.Launch(s.ZoneId, s.ZoneTitle, digital: false, onClose: () => s.Exit(), kind: s.Kind,
                startingLp: s.StartingLp, boss: false);
        }

        void OnLeave()
        {
            // No PlayClick here: ArcaneButton's onClick wrapper already played one and
            // ZoneModeSession.Exit plays the leave blip.
            if (_onLeave != null) _onLeave();
            else if (Session != null) Session.Exit();
            else Close();
        }

        // contract note: SetActive(false) here would STOP the panel's own coroutines — the slate's
        // _Open reveal (0.42 s) and MenuMotion.StaggerChildren(Body) both run on this GameObject —
        // and neither has a restore path, so a tap on a row inside the open window left the plate
        // cut in half and the lower rows faded forever. Hide through the CanvasGroup instead: the
        // motion driver keeps ticking and the panel is back intact when the submenu closes.
        void SetPanelHidden(bool hidden)
        {
            var root = Root;
            if (root == null) return;
            var g = MenuMotion.EnsureGroup(root.gameObject);
            g.alpha = hidden ? 0f : 1f;
            g.blocksRaycasts = !hidden;
            g.interactable = !hidden;
        }

        void OpenModal(Func<Transform, RectTransform> build)
        {
            if (_modal != null || ModalHost == null) return;
            FreeUiKit.PlaySelect();
            SetPanelHidden(true);
            // The tether is a sibling Image the anchor keeps drawing; a CanvasGroup-hidden panel
            // does not hide it, so strike it explicitly while the submenu covers the panel.
            if (Anchor != null) Anchor.SetTetherVisible(false);
            try
            {
                _modal = build(ModalHost);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] Hub submenu failed: " + ex.Message);
                _modal = null;
            }

            if (_modal == null)
            {
                ClearModalHost();
                SetPanelHidden(false);
                if (Anchor != null) Anchor.SetTetherVisible(true);
            }
        }

        void CloseModal()
        {
            FreeUiKit.PlayClick();
            _modal = null;
            ClearModalHost();
            if (!_closing) SetPanelHidden(false);
            if (Anchor != null && !_closing) Anchor.SetTetherVisible(true);
        }

        void ClearModalHost()
        {
            if (ModalHost == null) return;
            for (var i = ModalHost.childCount - 1; i >= 0; i--)
                ArObjectUtil.Destroy(ModalHost.GetChild(i).gameObject);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
        }

        public void Close(Action onDone = null)
        {
            if (_closing) return;
            _closing = true;
            IsOpen = false;
            _modal = null;
            ClearModalHost();

            if (!Application.isPlaying || Panel == null)
            {
                ArObjectUtil.Destroy(gameObject);
                if (_ownCanvas != null) ArObjectUtil.Destroy(_ownCanvas.gameObject);
                onDone?.Invoke();
                return;
            }

            var done = false;
            Panel.Close(() =>
            {
                if (done) return;
                done = true;
                if (this != null) ArObjectUtil.Destroy(gameObject);
                if (_ownCanvas != null) ArObjectUtil.Destroy(_ownCanvas.gameObject);
                onDone?.Invoke();
            });
            // Safety net if the panel's close motion never reports back.
            ArObjectUtil.Destroy(gameObject, 0.8f);
        }
    }
}
