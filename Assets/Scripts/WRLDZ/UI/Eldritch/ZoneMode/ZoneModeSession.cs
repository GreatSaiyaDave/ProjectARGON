using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Zone Mode: the Spirit Dueler appears folded on the player's arm inside an AR stage and the
    /// whole main menu lives on its hub (<see cref="ArHoloHubMenu"/>). One session at a time.
    /// Entered from <see cref="ZoneModePrompt"/> (in-range zone) or the hub (preview zone).
    /// </summary>
    public sealed class ZoneModeSession : MonoBehaviour
    {
        public const string CanvasName = "ZoneModeCanvas";
        public const int CanvasSort = 88;
        public const string PreviewZoneId = "hub_preview";
        public const string PreviewZoneTitle = "Spirit Dueler · Preview";
        const float ExitFadeSeconds = 0.6f;
        const float StagePollTimeout = 8f;

        public static ZoneModeSession Active { get; private set; }
        public static bool IsActive => Active != null;

        public ArDuelSpace Space { get; private set; }
        public ArDuelDiskRig PlayerDisk =>
            Space != null && Space.Interaction != null ? Space.Interaction.PlayerDisk : null;
        /// <summary>Null until the first hub tap.</summary>
        public ArHoloHubMenu HubMenu { get; private set; }
        public string ZoneId { get; private set; }
        public string ZoneTitle { get; private set; }
        public MapZoneKind Kind { get; private set; }
        /// <summary>LP the zone was entered with; ZoneModePrompt.Launch reads it to pick the street band.</summary>
        public int StartingLp { get; private set; }

        /// <summary>Overlay canvas (sort 88). Kept at scene root so it is never a nested canvas.</summary>
        public RectTransform CanvasRoot { get; private set; }
        /// <summary>AR RawImage ("ArDuelSpaceView"); null when the stage could not build.</summary>
        public RawImage Viewport { get; private set; }
        public ArDiskHubTapRouter TapRouter { get; private set; }
        public ArDiskHubHit HubHit { get; private set; }
        public bool StageConfigured { get; private set; }
        public bool Exiting => _exiting;

        Action _onExit;
        bool _exiting;
        bool _exitFired;
        CanvasGroup _hintGroup;
        bool _hintHidden;
        Coroutine _stagePoll;

        // ───────────────────────── entry ─────────────────────────

        public static ZoneModeSession Enter(Transform lifetimeHost, string zoneId, string zoneTitle,
            MapZoneKind kind, Action onExit = null, int startingLp = 0)
        {
            if (Active != null)
            {
                Debug.Log("[WRLDZ Eldritch] Zone Mode re-entered — replacing live session.");
                var old = Active;
                Active = null;
                old.SetPresentation(UiPresentation.NonArPortrait);
                ArObjectUtil.Destroy(old.gameObject);
            }

            var stale = GameObject.Find(CanvasName);
            if (stale != null) ArObjectUtil.Destroy(stale);

            var go = new GameObject("ZoneModeSession");
            if (lifetimeHost != null) go.transform.SetParent(lifetimeHost, false);
            var s = go.AddComponent<ZoneModeSession>();
            s.ZoneId = string.IsNullOrEmpty(zoneId) ? PreviewZoneId : zoneId;
            s.ZoneTitle = string.IsNullOrEmpty(zoneTitle) ? MapZoneCatalog.Label(kind) : zoneTitle;
            s.Kind = kind;
            s.StartingLp = Mathf.Max(0, startingLp);
            s._onExit = onExit;
            Active = s;
            // Owner rule: in Zone Mode the player's physical body is there — hide the map avatar body
            // (released in OnDestroy; per-instance key so a replaced session cannot unhide the new one).
            OverworldAvatar3d.HideBody(s.BodyHideKey, true);
            s.Build();
            Debug.Log($"[WRLDZ Eldritch] Zone Mode ENTER · {s.ZoneId} · {s.ZoneTitle} · {kind}");
            return s;
        }

        /// <summary>
        /// Hub entry. contract note: the hub has no GPS zone context (Overworld owns proximity), so
        /// this always opens the Training preview zone; DUEL HERE then runs MakeTrainingConfig.
        /// </summary>
        public static ZoneModeSession EnterFromHub(Transform lifetimeHost, Action onExit = null) =>
            Enter(lifetimeHost, PreviewZoneId, PreviewZoneTitle, MapZoneKind.Training, onExit);

        // ───────────────────────── build ─────────────────────────

        void Build()
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            CanvasRoot = WrldzTheme.Canvas(CanvasName, CanvasSort);
            WrldzTheme.StretchFill(CanvasRoot, "Backdrop", EldritchPalette.Void);

            try
            {
                Space = ArDuelSpace.CreateInUi(CanvasRoot, 0f, 0f, 1f, 1f, transform);
                Viewport = FindViewport();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] Zone Mode stage unavailable — chrome only: " + ex.Message);
                Space = null;
                Viewport = null;
            }

            BuildChrome();
            SetPresentation(UiPresentation.ArDiskHolo);

            if (Space == null) return;
            if (Application.isPlaying)
                _stagePoll = StartCoroutine(WaitForDiskThenConfigure());
            else
                TryConfigureStage();
        }

        RawImage FindViewport()
        {
            if (CanvasRoot == null) return null;
            var view = CanvasRoot.Find("ArDuelFrame/ArDuelSpaceView");
            var raw = view != null ? view.GetComponent<RawImage>() : null;
            return raw != null ? raw : CanvasRoot.GetComponentInChildren<RawImage>(true);
        }

        void BuildChrome()
        {
            // ZONE MODE · {zone} chip — Eldritch slate, top-left
            RectTransform chipRect = null;
            SolidVisionSlate chipSlate = null;
            try
            {
                chipSlate = SolidVisionSlate.Create(CanvasRoot, "ZoneChip", EldritchTier.Eldritch,
                    hoverGlow: false, raycast: false);
                chipRect = chipSlate != null ? chipSlate.Rect : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] Zone chip slate unavailable: " + ex.Message);
                chipSlate = null;
            }

            if (chipRect == null)
            {
                chipRect = WrldzTheme.StretchFill(CanvasRoot, "ZoneChip",
                    new Color(EldritchPalette.Abyss.r, EldritchPalette.Abyss.g, EldritchPalette.Abyss.b, 0.9f));
                HubChrome.FilamentRim(chipRect, EldritchPalette.EldritchViolet);
            }

            FloatingPanel.Place(chipRect, 0.04f, 0.925f, 0.70f, 0.975f);
            // contract note: PlayOpen's ember burst samples the rect synchronously, so it must run
            // AFTER the chip is anchored. Created it is a 0×0 RectTransform pinned at the canvas
            // corner, and all ten embers erupted from there instead of from the chip.
            if (chipSlate != null && Application.isPlaying) chipSlate.PlayOpen(0.5f);
            var chipLabel = Label(chipRect, "Label", "ZONE MODE · " + ZoneTitle, 15, EldritchPalette.TextCream,
                TextAnchor.MiddleLeft, true);
            FloatingPanel.Stretch(chipLabel.rectTransform);
            chipLabel.rectTransform.offsetMin = new Vector2(22f, 0f);
            chipLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

            // Hint under the chip — fades after the first hub tap
            var hint = Label(CanvasRoot, "Hint", "Tap your Duel Disk", 13, EldritchPalette.TextMuted,
                TextAnchor.MiddleLeft, false);
            FloatingPanel.Place(hint.rectTransform, 0.06f, 0.885f, 0.70f, 0.920f);
            _hintGroup = MenuMotion.EnsureGroup(hint.gameObject);
            _hintGroup.blocksRaycasts = false;
            if (Space == null)
                hint.text = "Solid Vision stage offline";

            // LEAVE — top-right, Danger tier
            RectTransform leaveRect = null;
            try
            {
                var leave = ArcaneButton.Create(CanvasRoot, "LEAVE", Exit, EldritchTier.Danger,
                    titleSize: 16, centerTitle: true);
                leaveRect = leave != null ? leave.Rect : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] LEAVE arcane button unavailable: " + ex.Message);
            }

            if (leaveRect == null)
            {
                var btn = HubChrome.Capsule(CanvasRoot, "LEAVE", Exit, MenuCommandButton.Kind.Danger,
                    centerTitle: true, titleSize: 16);
                leaveRect = btn.GetComponent<RectTransform>();
            }

            FloatingPanel.Place(leaveRect, 0.74f, 0.925f, 0.96f, 0.975f);
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

        // ───────────────────────── stage ─────────────────────────

        IEnumerator WaitForDiskThenConfigure()
        {
            var deadline = Time.unscaledTime + StagePollTimeout;
            while (!TryConfigureStage())
            {
                if (_exiting) yield break;
                if (Time.unscaledTime > deadline)
                {
                    Debug.LogWarning("[WRLDZ Eldritch] Zone Mode: player disk never appeared — hub tap disabled.");
                    yield break;
                }

                yield return null;
            }

            _stagePoll = null;
        }

        /// <summary>Hide the opponent, fold the player disk, wire the hub tap. True once done.</summary>
        public bool TryConfigureStage()
        {
            if (StageConfigured) return true;
            if (Space == null || !Space.Ready) return false;
            var it = Space.Interaction;
            if (it == null || it.PlayerDisk == null) return false;
            StageConfigured = true;

            HideOpponentPresence(it);

            var disk = it.PlayerDisk;
            disk.FadeInRetracted();
            HubHit = ArDiskHubHit.Attach(disk);
            ArDiskDuelButton.Create(disk);

            try
            {
                // Ember ring on the wrist (same anchor as the hub hit) so it stays put while the blade folds.
                var pivot = new GameObject("HubEmberPivot").transform;
                pivot.SetParent(disk.transform, false);
                pivot.localPosition = ArDiskHubHit.DefaultCenter + new Vector3(0f, 0.006f, 0f);
                pivot.localRotation = Quaternion.identity;
                pivot.gameObject.layer = disk.Layer;
                ArcaneEmberParticles.Create(pivot, EldritchPalette.EldritchViolet, 0.06f, disk.Layer, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] Hub ember ring skipped: " + ex.Message);
            }

            if (Viewport != null)
            {
                TapRouter = ArDiskHubTapRouter.Attach(Viewport, Space.StageCamera, OnHubTapped, it);
                if (TapRouter != null)
                    TapRouter.OnDuelMode = BeginDuelFromDisk;
            }

            // contract note: lenses disable the RawImage, so there is no tap surface — open the
            // hub menu straight away on the disk-mounted world canvas.
            if (ArPresentationTarget.IsWearable && Application.isPlaying)
                OnHubTapped();

            Debug.Log("[WRLDZ Eldritch] Zone Mode stage configured · disk folded · hub hit live");
            return true;
        }

        static void HideOpponentPresence(ArDuelInteractionSystem it)
        {
            if (it.OppDisk != null) it.OppDisk.gameObject.SetActive(false);
            if (it.HandVolume != null) it.HandVolume.gameObject.SetActive(false);
            if (it.OppHandVolume != null) it.OppHandVolume.gameObject.SetActive(false);
            if (it.Arena != null) it.Arena.gameObject.SetActive(false);
        }

        // ───────────────────────── hub tap ─────────────────────────

        /// <summary>Hub DUEL chip — same handoff as the menu's DUEL HERE row.</summary>
        public void BeginDuelFromDisk()
        {
            ZoneModePrompt.Launch(ZoneId, ZoneTitle, digital: false, onClose: () => Exit(),
                kind: Kind, startingLp: StartingLp, boss: false);
        }

        /// <summary>Toggle the hub menu (also the router callback).</summary>
        public void OnHubTapped()
        {
            if (_exiting) return;
            HideHint();

            if (HubMenu != null && HubMenu.IsOpen)
            {
                HubMenu.Close();
                HubMenu = null;
                FreeUiKit.PlayClick();
                return;
            }

            HubMenu = ArHoloHubMenu.Open(this);
        }

        void HideHint()
        {
            if (_hintHidden || _hintGroup == null) return;
            _hintHidden = true;
            if (Application.isPlaying)
                MenuMotion.PlayOn(_hintGroup.gameObject, MenuMotion.Fade(_hintGroup, _hintGroup.alpha, 0f, 0.35f));
            else
                _hintGroup.alpha = 0f;
        }

        // ───────────────────────── exit ─────────────────────────

        public void Exit()
        {
            if (_exiting) return;
            _exiting = true;
            FreeUiKit.PlayClick();

            if (_stagePoll != null)
            {
                StopCoroutine(_stagePoll);
                _stagePoll = null;
            }

            if (HubMenu != null && HubMenu.IsOpen) HubMenu.Close();
            var disk = PlayerDisk;
            if (disk != null) disk.RetractThenFadeOut();
            SetPresentation(UiPresentation.NonArPortrait);
            Debug.Log("[WRLDZ Eldritch] Zone Mode EXIT · " + ZoneId);

            if (Application.isPlaying && CanvasRoot != null)
            {
                var g = MenuMotion.EnsureGroup(CanvasRoot.gameObject);
                g.blocksRaycasts = false;
                g.interactable = false;
                MenuMotion.PlayOn(CanvasRoot.gameObject, MenuMotion.Fade(g, g.alpha, 0f, ExitFadeSeconds * 0.9f));
                ArObjectUtil.Destroy(gameObject, ExitFadeSeconds);
            }
            else
            {
                ArObjectUtil.Destroy(gameObject);
            }
        }

        void SetPresentation(UiPresentation p)
        {
            // contract note: Ensure() spawns a DontDestroyOnLoad router, which edit mode rejects —
            // only reuse an existing instance there.
            var router = Application.isPlaying ? ScreenRouter.Ensure() : ScreenRouter.Instance;
            if (router != null) router.Presentation = p;
        }

        void FireExit()
        {
            if (_exitFired) return;
            _exitFired = true;
            try
            {
                _onExit?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ Eldritch] Zone Mode onExit threw: " + ex.Message);
            }
        }

        string BodyHideKey => OverworldAvatar3d.HideForZone + ":" + GetEntityId();

        void OnDestroy()
        {
            OverworldAvatar3d.HideBody(BodyHideKey, false);
            if (Active == this)
            {
                Active = null;
                SetPresentation(UiPresentation.NonArPortrait);
            }

            if (HubMenu != null) ArObjectUtil.Destroy(HubMenu.gameObject);
            if (CanvasRoot != null) ArObjectUtil.Destroy(CanvasRoot.gameObject);
            FireExit();
        }
    }
}
