using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;
using WRLDZ.UI.MenuV6;

namespace WRLDZ.UI
{
    /// <summary>
    /// Opening cascade (BUILD_PLAN §6 P2, D8-D10): the v6.3 boot — the approved Genesis cut (a local VP8 clip) into the
    /// live title (<see cref="TitleStage"/>: Nut, the carved Stone, the Items, the Puzzle, the Tears clawing open, TOUCH
    /// TO BEGIN). On TOUCH a returning player goes into the main menu in one shot (the D8 handoff over the kept title
    /// canvas); everyone else takes today's steps: BootLoad → DOB? → Auth → Terms? → Prologue* → PickKuriboh* →
    /// GiftSummary* → SpiritMagic* → TutorialAsk* → WorldLoad (a 0.3 s fade to Void) → MainMenu (its cold open).
    /// * first-time only. Splash and Credits leave the automatic path (their code stays: the legacy fallback of a build
    /// without the v6 title data, the kill switch WRLDZ_MenuV6Off, or a v6 build error, runs the old cascade Genesis →
    /// Splash → Credits → Title).
    /// The Overworld is reached from the Duel Disk via WORLD MAP.
    ///
    /// Materially the cascade is the barrier-stone seal's own history, read in one arc: the stone
    /// whole and quiet before history (Splash / Credits), Akun's stone with Andrax's light at full
    /// (Title), a deliberately unmarked contract-barrier through the transactional block (DOB →
    /// Terms), the seal visibly failing on screen while the Prologue is told, and the last plate
    /// driven past Breached as the world loads. <see cref="SealFor"/> is that table; every
    /// FlowChrome primitive inherits it through <see cref="FlowChrome.BeginStep"/>, so a step only
    /// names a state where it deliberately breaks the arc.
    /// </summary>
    public class BootFlowUI : MonoBehaviour
    {
        public const int MinAge = 13;

        enum Step
        {
            Genesis,
            Splash,
            Credits,
            Title,
            BootLoad,
            Dob,
            AuthChoice,
            Login,
            Create,
            Terms,
            Prologue,
            PickKuriboh,
            GiftSummary,
            SpiritMagic,
            TutorialAsk,
            WorldLoad,
            Done
        }

        Step _step;
        RectTransform _root;
        GameObject _panelHost;

        /// <summary>
        /// Everything this step built, and nothing else. The backdrop plates, the persistent
        /// BootDressing and the full-screen tap catcher stay as untouched siblings on
        /// <see cref="_panelHost"/>, so the panel wipe destroys exactly one object and the reveal
        /// is one tracked coroutine instead of one per child racing that wipe.
        /// </summary>
        GameObject _stepHost;
        Transform Panels => _stepHost != null ? _stepHost.transform : _panelHost.transform;

        Text _statusLine;
        Coroutine _routine;
        Coroutine _artCo;
        Coroutine _revealCo;
        Coroutine _beatCo;
        readonly List<Coroutine> _fx = new();
        RectTransform _liveBackdrop;

        /// <summary>How far the seal has failed on the live step. Drives every primitive.</summary>
        SealState _stepSeal = SealState.Sealed;

        // DOB dropdowns
        Dropdown _dobMonth;
        Dropdown _dobDay;
        Dropdown _dobYear;
        readonly List<int> _yearValues = new();
        // Auth
        InputField _user, _pass, _pass2, _display, _email;
        Text _error;
        /// <summary>A Google sign-in is in flight (one loopback listener at a time).</summary>
        bool _googleBusy;

        /// <summary>
        /// Lab / test doors (DESKTOP LAB, TEST DUEL) exist only in the Editor and Development builds
        /// (owner decision 5). A shipped build never shows them and refuses their entry points.
        /// </summary>
        static bool LabDoorsEnabled => WrldzLab.LabDoorsEnabled; // UNITY_EDITOR || DEVELOPMENT_BUILD

        /// <summary>
        /// Google sign-in is a desktop PKCE loopback flow: shown only on desktop players that have a
        /// client id configured. On phones / glasses it can never succeed, so the button is hidden.
        /// </summary>
        static bool GoogleButtonAvailable =>
            !Application.isMobilePlatform && GoogleSignIn.TryLoadClientId(out _, out _);

        // Live seal handles for the steps that escalate their own stone after the build.
        BarrierStone _panelSeal;
        BarrierStone _loadSeal;
        RectTransform _loadLead;

        public void BuildAndRun()
        {
            FlowChrome.ApplyPortrait();
            AppSession.Ensure();
#if UNITY_EDITOR
            // Editor art hot-reload only: on device this re-decodes every boot backdrop each boot.
            StreamingSprite.ClearCache("WRLDZ/Imagine/bg/");
#endif
            // D1: the canvas opts in to the aspect plan (the title is the 1920×1080 stage at a landscape surface); the
            // FlowChrome steps switch it back to today's portrait phone scaler (Go → ApplyScaler)
            var canvas = FlowChrome.CreateCanvas("BootCanvas", 60, aspect: true);
            _canvas = canvas;
            _root = FlowChrome.RootAtmosphere(canvas);
            _panelHost = new GameObject("Panels", typeof(RectTransform));
            _panelHost.transform.SetParent(_root, false);
            FlowChrome.Stretch(_panelHost.GetComponent<RectTransform>());

            _titleKind = MenuAspect.Current;
            var why = TitleStage.WhyNot(_titleKind);
            _v6Title = why == null;
            if (!_v6Title) Debug.Log("[WRLDZ] Boot: legacy cascade (" + why + ").");

            // Genesis is a coroutine cinematic (legacy) / a video (v6), and neither ticks in edit mode: an editor preview
            // or smoke build builds the v6 title's settled idle statically (D9), or the legacy Splash.
            if (Application.isPlaying) Go(Step.Genesis);
            else Go(_v6Title ? Step.Title : Step.Splash);
        }

        RectTransform _canvas;
        MenuLayoutKind _titleKind;
        bool _v6Title;
        TitleStage _title;

        /// <summary>The v6.3 title (Genesis video + live title, D9) owns this step.</summary>
        bool IsV6TitleStep(Step step) => _v6Title && (step == Step.Genesis || step == Step.Title);

        /// <summary>D1 per step: the v6 title lays out on the aspect plan; every FlowChrome step keeps today's portrait
        /// phone scaler (so DOB, Auth, Terms and onboarding look exactly as before, D10).</summary>
        void ApplyScaler(Step step)
        {
            var scaler = _canvas != null ? _canvas.GetComponent<CanvasScaler>() : null;
            if (scaler == null) return;
            if (IsV6TitleStep(step)) UiTheme.ApplyForAspect(scaler, MenuAspect.Surface);
            else UiTheme.ApplyPortraitPhone(scaler);
        }

        /// <summary>The FlowChrome dressing (tentacles, rails, the ambient motes) is not in the v6.3 title.</summary>
        void ShowBootDressing(bool on)
        {
            var dress = _panelHost != null ? _panelHost.transform.Find("BootDressing") : null;
            if (dress != null) dress.gameObject.SetActive(on);
            var field = _canvas != null ? _canvas.Find("EmberField") : null;
            if (field != null) field.gameObject.SetActive(on);
            if (!on)
            {
                var sky = _panelHost != null ? _panelHost.transform.Find("LivingSky") : null;
                if (sky != null) sky.gameObject.SetActive(false);
            }
        }

        /// <summary>The v6.3 boot (D9): the Genesis clip (play mode, when the local clip exists) into the live title, or the
        /// settled idle (edit mode). A build error falls back to the legacy step (never a blank screen).</summary>
        void BuildTitleV6(Step step)
        {
            try
            {
                _title = TitleStage.Build(_stepHost.GetComponent<RectTransform>(), _titleKind, new TitleStage.Options
                {
                    Video = step == Step.Genesis && Application.isPlaying,
                    StartRaw = Application.isPlaying ? TitleStage.GenEndRaw : TitleStage.SettledRaw,
                    OnTouch = OnTitleTouch
                });
                if (_title.Missing.Count > 0)
                    Debug.LogWarning("[WRLDZ] Boot title v6: " + _title.Missing.Count + " part(s) not found: " + string.Join(", ", _title.Missing));
                WrldzBuild.Log();
                // the session check runs synchronously as the title settles (§6 P2): it decides TOUCH's route. Play mode
                // only: an edit-mode build (MenuSmokeTest) never touches, and must not refresh the account store
                if (Application.isPlaying) SessionCheck();
            }
            catch (Exception ex)
            {
                Debug.LogError("[WRLDZ] Boot title v6 failed; showing the legacy cascade. " + ex);
                _title = null;
                _v6Title = false;
                ShowBootDressing(true);   // the legacy steps wear the FlowChrome dressing the v6 title hid
                Go(step);
            }
        }

        bool _sessionChecked, _returning;

        /// <summary>BootLoadRoutine's session check, synchronous: a returning player (age gate passed, signed in, terms
        /// accepted, onboarding complete) goes into the menu by the D8 handoff; anyone else takes today's steps.</summary>
        bool SessionCheck()
        {
            try
            {
                LocalAccountStore.EnsureLoaded();
                AppSession.Ensure().RefreshFromStore();
                var s = AppSession.Ensure();
                var acc = s.Account;
                acc?.EnsureProgress();
                _returning = LocalAccountStore.AgeGatePassed && s.IsLoggedIn && LocalAccountStore.TermsAccepted &&
                             acc != null && acc.progress != null && acc.progress.onboardingComplete;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Boot session check: " + ex.Message);
                _returning = false;
            }

            _sessionChecked = true;
            return _returning;
        }

        /// <summary>TOUCH TO BEGIN on the v6 title (raw ≥ 21.2; TitleStage handles TAP TO SKIP before that).</summary>
        void OnTitleTouch()
        {
            if (_step == Step.Done) return;
            if (!_sessionChecked) SessionCheck();
            if (_returning && HandoffToMenu()) return;
            FreeUiKit.PlayConfirm();
            Go(Step.BootLoad);
        }

        /// <summary>
        /// D8, a returning player: the onboarding / world persistence first (nothing after GoMainMenu may be relied on),
        /// the Millennium Eye (title.js:153), the title fades its touch line and its Tears under the rising room, the boot
        /// canvas is kept alive across the scene load (DontDestroyOnLoad, raycasts off, sort 49), the token is set, and
        /// <c>AppSession.GoMainMenu()</c> runs as today (its audio switch included).
        /// </summary>
        bool HandoffToMenu()
        {
            var session = AppSession.Ensure();
            var acc = session.Account;
            if (acc == null || !session.IsLoggedIn || _canvas == null) return false;
            try
            {
                acc.EnsureProgress();
                ErazProgress.GrantTutorialBadgeIfOnboarded(acc.progress);
                if (acc.progress.onboardingTutorialDuelDone || acc.progress.onboardingComplete)
                    ProgressionService.Persist(acc);
                PersistForWorld(acc);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Boot handoff persistence: " + ex.Message);
            }

            StopStepCoroutines();
            _step = Step.Done;
            try { WrldzAudio.PlayMillenniumEye(); }
            catch { /* audio optional */ }
            if (_title != null) _title.Leave();
            MenuEntry.KeepTitle(_canvas.gameObject, _title);
            session.GoMainMenu();
            return true;
        }

        void Go(Step step)
        {
            _step = step;
            StopStepCoroutines();
            ClearPanels();
            ApplyScaler(step);

            // Seal context first: the scrim tint, every panel/button default and the ember rate all
            // read it, so it has to be parked before the backdrop or any primitive runs.
            _stepSeal = SealFor(step);
            FlowChrome.BeginStep(_stepSeal, _root);

            if (IsV6TitleStep(step))
            {
                // the v6.3 title paints the whole screen itself: no FlowChrome backdrop, dressing, living sky or motes.
                // The boot's Scarab bed plays under the Genesis and the title (survey_boot §5.7: kept).
                ApplyBootMusic(Step.Genesis);
                ShowBootDressing(false);
                _stepHost = new GameObject("Step", typeof(RectTransform));
                _stepHost.transform.SetParent(_panelHost.transform, false);
                FlowChrome.Stretch(_stepHost.GetComponent<RectTransform>());
                BuildTitleV6(step);
                return;
            }

            if (_title != null)
            {
                _title = null;   // its step host was retired by ClearPanels
                ShowBootDressing(true);
            }

            ApplyBootMusic(step);
            ApplyBootBackdrop(step);

            // After the backdrop, never before: SetBootBackdrop reshuffles the host's first two
            // children every step, and the dressing re-seats itself from where the art landed.
            FlowChrome.BootDressing(_panelHost.transform);
            ApplyLivingSky(step);

            _stepHost = new GameObject("Step", typeof(RectTransform));
            _stepHost.transform.SetParent(_panelHost.transform, false);
            FlowChrome.Stretch(_stepHost.GetComponent<RectTransform>());

            switch (step)
            {
                case Step.Genesis: _routine = StartCoroutine(GenesisRoutine()); break;
                case Step.Splash: BuildSplash(); break;
                case Step.Credits: BuildCredits(); break;
                case Step.Title: BuildTitle(); break;
                case Step.BootLoad: _routine = StartCoroutine(BootLoadRoutine()); break;
                case Step.Dob: BuildDob(); break;
                case Step.AuthChoice: BuildAuthChoice(); break;
                case Step.Login: BuildLogin(); break;
                case Step.Create: BuildCreate(); break;
                case Step.Terms: BuildTerms(); break;
                case Step.Prologue: BuildPrologue(); break;
                case Step.PickKuriboh: BuildPickKuriboh(); break;
                case Step.GiftSummary: BuildGiftSummary(); break;
                case Step.SpiritMagic: BuildSpiritMagic(); break;
                case Step.TutorialAsk: BuildTutorialAsk(); break;
                case Step.WorldLoad: _routine = StartCoroutine(WorldLoadRoutine()); break;
            }

            RevealChrome();
        }

        /// <summary>
        /// The cascade read as the seal's own history. Same shape as <see cref="BootDriftFor"/> and
        /// <see cref="ApplyBootMusic"/> — one table, every step named, so nothing falls through.
        ///
        /// The transactional block (DOB → Terms) is deliberately the quietest stone in the file:
        /// the contrast against the narrative steps is what makes the Prologue's failure land, and
        /// unbroken dread reads as noise. WorldLoad is Breached rather than Condemned on purpose —
        /// Condemned is blood-lit and means destructive (QUIT, a failed login), and spending it on
        /// a handoff would break the one meaning that state already carries.
        /// </summary>
        static SealState SealFor(Step step) => step switch
        {
            Step.Genesis => SealState.Sealed,       // creation: nothing is broken yet
            Step.Splash => SealState.Sealed,        // before history: the stone is whole
            Step.Credits => SealState.Sealed,
            Step.Title => SealState.Intact,         // Akun's barrier-stone, Andrax's light at full
            Step.BootLoad => SealState.Sealed,      // a seam re-struck, not opened
            Step.Dob => SealState.Sealed,
            Step.AuthChoice => SealState.Sealed,
            Step.Login => SealState.Sealed,
            Step.Create => SealState.Sealed,
            Step.Terms => SealState.Sealed,         // the contract-barrier itself
            Step.Prologue => SealState.Breached,    // Crawford opens the rift, on screen
            Step.PickKuriboh => SealState.Fractured,
            Step.GiftSummary => SealState.Fractured,
            Step.SpiritMagic => SealState.Breached,
            Step.TutorialAsk => SealState.Fractured,
            Step.WorldLoad => SealState.Breached,
            _ => SealState.Breached                 // Done inherits the handoff
        };

        void StopStepCoroutines()
        {
            if (_routine != null) StopCoroutine(_routine);
            if (_revealCo != null) StopCoroutine(_revealCo);
            if (_beatCo != null) StopCoroutine(_beatCo);
            _routine = _revealCo = _beatCo = null;
            GoogleSignIn.Cancel();
            _googleBusy = false;
            for (var i = 0; i < _fx.Count; i++)
                if (_fx[i] != null) StopCoroutine(_fx[i]);
            _fx.Clear();
        }

        /// <summary>The one shared ember field (canvas level, above Root and the painted backdrop).</summary>
        UiEmberField Embers() => _root != null ? UiEmberField.Field(_root) : null;

        void Fx(IEnumerator co)
        {
            if (!Application.isPlaying || co == null) return;
            _fx.Add(StartCoroutine(co));
        }

        /// <summary>
        /// Burst one frame late. A rect anchored this frame has no resolved size until layout runs,
        /// and <see cref="UiEmberField.BurstFromRect"/> reads that rect.
        /// </summary>
        void Burst(RectTransform rt, int count, UiEmberField.Kind kind, Color tint,
            float y0 = 0f, float y1 = 1f)
        {
            Fx(BurstCo(rt, count, kind, tint, y0, y1));
        }

        IEnumerator BurstCo(RectTransform rt, int count, UiEmberField.Kind kind, Color tint,
            float y0, float y1)
        {
            yield return null;
            var field = Embers();
            if (field != null && rt != null)
                field.BurstFromRect(rt, count, kind, tint, y0, y1);
        }

        /// <summary>
        /// Hold a fragment shut so a later <see cref="BarrierStone.PlayOpen"/> actually reveals it.
        /// Materials are born at _Open = 1, so a staggered arrival needs the plate closed first.
        /// </summary>
        static void HoldClosed(BarrierStone stone)
        {
            if (!Application.isPlaying) return;
            if (stone != null && stone.Mat != null && stone.Mat.HasProperty("_Open"))
                stone.Mat.SetFloat("_Open", 0f);
        }

        static void SetProp(BarrierStone stone, string prop, float v)
        {
            if (stone != null && stone.Mat != null && stone.Mat.HasProperty(prop))
                stone.Mat.SetFloat(prop, v);
        }

        /// <summary>Full-bleed Imagine art per boot step (behind panels).</summary>
        void ApplyBootBackdrop(Step step)
        {
            if (_panelHost == null) return;
            Sprite art = step switch
            {
                // Genesis paints its own cosmos; the void plate is only what sits behind it.
                Step.Genesis => ImagineAssets.BgMenuVoid(),
                // Splash / Title are the living sky, not a painting — null art leaves the
                // backdrop as a void plate and LivingSky supplies every moving band above it.
                Step.Splash => null,
                Step.Title => null,
                Step.Credits => ImagineAssets.BgCredits(),
                // Its own plate on purpose: sharing BgTitle made SetBootBackdrop reuse the live
                // art, so Title → BootLoad was a dead cut with the Ken Burns drift mid-stride.
                Step.BootLoad => ImagineAssets.BgMenuVoid(),
                Step.Dob => ImagineAssets.BgAuth(),
                Step.AuthChoice => ImagineAssets.BgAuth(),
                Step.Login => ImagineAssets.BgAuth(),
                Step.Create => ImagineAssets.BgAuth(),
                Step.Terms => ImagineAssets.BgAuth(),
                Step.Prologue => ImagineAssets.BgCredits(),
                Step.SpiritMagic => ImagineAssets.BgCredits(),
                Step.PickKuriboh => ImagineAssets.BgTitle(),
                Step.GiftSummary => ImagineAssets.BgTitle(),
                Step.TutorialAsk => ImagineAssets.BgMenuVoid(),
                Step.WorldLoad => ImagineAssets.BgHub(),
                _ => ImagineAssets.BgSplash()
            };
            var incoming = FlowChrome.SetBootBackdrop(_panelHost.transform, art);
            if (incoming == null) return;

            var reused = incoming == _liveBackdrop;
            _liveBackdrop = incoming;
            if (reused) return;

            var drift = incoming.GetComponent<BootArtDrift>();
            if (drift == null)
                drift = incoming.gameObject.AddComponent<BootArtDrift>();
            BootDriftFor(step, out var fromS, out var toS, out var fromP, out var toP, out var dur);
            drift.Play(fromS, toS, fromP, toP, dur);

            var incomingCg = MenuMotion.EnsureGroup(incoming.gameObject);
            incomingCg.blocksRaycasts = false;
            var prev = _panelHost.transform.Find("BootBackdropPrev");
            if (!Application.isPlaying)
            {
                incomingCg.alpha = 1f;
                if (prev != null)
                    Destroy(prev.gameObject);
                return;
            }

            if (_artCo != null) StopCoroutine(_artCo);
            if (prev != null)
            {
                incomingCg.alpha = 0f;
                _artCo = StartCoroutine(CrossfadeBackdropCo(prev.gameObject, incomingCg));
            }
            else
            {
                incomingCg.alpha = 0f;
                _artCo = StartCoroutine(FadeArtInCo(incomingCg));
            }
        }

        IEnumerator FadeArtInCo(CanvasGroup g)
        {
            yield return MenuMotion.Fade(g, 0f, 1f, MenuMotion.BootFadeIn);
            if (g != null)
            {
                g.blocksRaycasts = false;
                g.interactable = false;
            }
            _artCo = null;
        }

        static void BootDriftFor(Step step, out float fromS, out float toS,
            out Vector2 fromP, out Vector2 toP, out float dur)
        {
            dur = 18f;
            fromS = 1f;
            toS = 1.06f;
            fromP = Vector2.zero;
            toP = new Vector2(0f, 24f);
            switch (step)
            {
                case Step.Splash:
                    toS = 1.07f;
                    toP = new Vector2(0f, 28f);
                    dur = 16f;
                    break;
                case Step.Credits:
                case Step.Prologue:
                case Step.SpiritMagic:
                    fromS = 1.04f;
                    toS = 1.00f;
                    fromP = new Vector2(0f, 12f);
                    toP = Vector2.zero;
                    dur = 20f;
                    break;
                case Step.Title:
                case Step.BootLoad:
                case Step.PickKuriboh:
                case Step.GiftSummary:
                    toS = 1.08f;
                    fromP = new Vector2(0f, -8f);
                    toP = new Vector2(0f, 18f);
                    break;
                case Step.Dob:
                case Step.AuthChoice:
                case Step.Login:
                case Step.Create:
                case Step.Terms:
                    toS = 1.05f;
                    fromP = new Vector2(0f, 16f);
                    toP = new Vector2(0f, -22f);
                    dur = 20f;
                    break;
                case Step.WorldLoad:
                case Step.TutorialAsk:
                    toS = 1.04f;
                    toP = new Vector2(0f, 10f);
                    dur = 22f;
                    break;
            }
        }

        IEnumerator CrossfadeBackdropCo(GameObject outgoing, CanvasGroup incoming)
        {
            var outCg = outgoing != null ? MenuMotion.EnsureGroup(outgoing) : null;
            yield return MenuMotion.CrossFade(outCg, incoming, MenuMotion.BootCross);
            if (outgoing != null)
                Destroy(outgoing);
            _artCo = null;
        }

        /// <summary>
        /// Step content arrives one element after another instead of everything cross-fading at
        /// once. One TRACKED coroutine over a single host — the old per-child untracked fades were
        /// what let a tap within ~0.2 s of a step build throw on a destroyed CanvasGroup
        /// (ELDRITCH_CONTRACT §11), and per-step seal beats only multiply that race.
        ///
        /// 0.035 s of delay rather than the menu's 0.045: StaggerChildren holds blocksRaycasts off
        /// until each child lands, so the whole step is untappable for dur + delay*(n-1).
        /// </summary>
        void RevealChrome()
        {
            // Genesis animates itself on an exact timeline; a stagger fighting it would pop.
            if (!Application.isPlaying || _stepHost == null || _step == Step.Genesis || IsV6TitleStep(_step)) return;
            _revealCo = StartCoroutine(MenuMotion.StaggerChildren(_stepHost.transform, 0.30f, 0.035f));
        }

        /// <summary>
        /// Spirit-eye bed on splash, credits, and title; Scarab Under Stone through the account steps;
        /// Alternate Under Stone after entering the world.
        /// Never throws — boot UI must still render if audio fails.
        /// </summary>
        static void ApplyBootMusic(Step step)
        {
            if (!Application.isPlaying) return;   // edit-mode builds (MenuSmokeTest) touch no audio (DontDestroyOnLoad)
            try
            {
                WrldzAudio.Ensure();
                switch (step)
                {
                    case Step.Splash:
                    case Step.Credits:
                    case Step.Title:
                        WrldzAudio.SetBgmVolume(0.30f);
                        WrldzAudio.PlaySpiritEyeBgm();
                        break;
                    case Step.Genesis:
                    case Step.BootLoad:
                    case Step.Dob:
                    case Step.AuthChoice:
                    case Step.Login:
                    case Step.Create:
                    case Step.Terms:
                        WrldzAudio.SetBgmVolume(0.30f);
                        WrldzAudio.PlayTitleBgm();
                        break;
                    case Step.Prologue:
                    case Step.PickKuriboh:
                    case Step.GiftSummary:
                    case Step.TutorialAsk:
                        WrldzAudio.SetBgmVolume(0.26f);
                        WrldzAudio.PlayTitleBgm();
                        break;
                    case Step.WorldLoad:
                    case Step.Done:
                        WrldzAudio.SetBgmVolume(0.18f);
                        WrldzAudio.PlayAmbientBgm();
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Boot music skipped: " + ex.Message);
            }
        }

        // The per-step Egyptian-ages pass used to sit here. It is gone rather than restyled:
        // EgyptianAgesAtmosphere.Attach forces itself to sibling 0, so it could never be above the
        // opaque backdrop every step paints; ApplyBootBackdrop hands back a non-null sprite for all
        // fifteen steps, so FlowChrome switched it off on the very first one and it never returned;
        // and its glyph stream is legacy Text over the U+13000 block, i.e. tofu boxes. Worse, its
        // trailing _root.SetAsLastSibling() pushed Root (and the painting) back over the
        // canvas-level ember layer, re-burying the ambient motes. The seal arc carries the per-step
        // variation now, and animates itself. MenuAge / EgyptianAgesAtmosphere stay — the Duel Disk
        // menu and the Desktop Lab still use them.

        /// <summary>After Terms / login: finish onboarding or enter world.</summary>
        void ContinueAfterAuth()
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                Go(Step.AuthChoice);
                return;
            }

            acc.EnsureProgress();
            ErazProgress.GrantTutorialBadgeIfOnboarded(acc.progress);
            if (acc.progress.onboardingTutorialDuelDone || acc.progress.onboardingComplete)
                ProgressionService.Persist(acc);
            if (acc.progress.onboardingComplete)
            {
                Go(Step.WorldLoad);
                return;
            }

            if (!acc.progress.onboardingPrologueDone)
            {
                Go(Step.Prologue);
                return;
            }

            if (!acc.progress.onboardingKuribohChosen)
            {
                Go(Step.PickKuriboh);
                return;
            }

            if (!acc.progress.onboardingStarterGranted)
            {
                if (!GrantStarterKitOrReport(acc)) return;
                AppSession.Ensure().RefreshFromStore();
                Go(Step.GiftSummary);
                return;
            }

            if (!acc.progress.onboardingTutorialDuelDone)
            {
                Go(Step.TutorialAsk);
                return;
            }

            acc.progress.onboardingComplete = true;
            ErazProgress.GrantTutorialBadge(acc.progress);
            ProgressionService.Persist(acc);
            Go(Step.WorldLoad);
        }

        void ClearPanels()
        {
            // BootDressing is persistent: tentacles, both hieroglyph rails and the violet pressure
            // veil are built once and have to survive all sixteen steps.
            for (var i = _panelHost.transform.childCount - 1; i >= 0; i--)
            {
                var ch = _panelHost.transform.GetChild(i);
                var n = ch.name;
                if (n == "BootBackdrop" || n == "BootBackdropPrev" || n == "BootDressing" ||
                    n == "LivingSky")
                    continue;
                // The step the player is looking at is never torn out from under them. It fades
                // and settles downward while the incoming step staggers up, so the cascade reads
                // as one continuous surface instead of sixteen hard cuts. Everything else under
                // the host is an invisible helper (tap catcher, an already-retiring step).
                if (n == "Step" && Application.isPlaying && ch.gameObject.activeSelf)
                {
                    RetireStep(ch.gameObject);
                    continue;
                }

                Destroy(ch.gameObject);
            }

            // Every one of these pointed into the step just destroyed.
            _stepHost = null;
            _statusLine = null;
            _loadFill = null;
            _loadLead = null;
            _loadSeal = null;
            _panelSeal = null;
            _error = null;
            _user = _pass = _pass2 = _display = _email = null;
            _dobMonth = _dobDay = _dobYear = null;
        }

        /// <summary>
        /// Hand the outgoing step to its own driver so it can fade itself out and then destroy
        /// itself. It has to run on the step's own GameObject, not on this component:
        /// <see cref="StopStepCoroutines"/> kills every coroutine this behaviour owns on the way
        /// into the next step, which would freeze a half-faded panel on screen forever.
        /// </summary>
        void RetireStep(GameObject step)
        {
            step.name = "StepOut";
            var cg = step.GetComponent<CanvasGroup>();
            if (cg == null) cg = step.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            MenuMotion.PlayOn(step, FadeStepOut(step, cg));
        }

        static IEnumerator FadeStepOut(GameObject step, CanvasGroup cg, float dur = 0.24f)
        {
            var rt = step.transform as RectTransform;
            var from = rt != null ? rt.anchoredPosition : Vector2.zero;
            var t = 0f;
            while (t < dur)
            {
                // Re-check every frame: a fast tap can start the next step, and Go() may have
                // already destroyed this one out from under the fade.
                if (step == null || cg == null) yield break;
                t += Time.unscaledDeltaTime;
                var k = MenuMotion.OutCubic(Mathf.Clamp01(t / dur));
                cg.alpha = 1f - k;
                if (rt != null) rt.anchoredPosition = from + new Vector2(0f, -26f * k);
                yield return null;
            }

            if (step != null) Destroy(step);
        }

        /// <summary>
        /// The moving sky for Splash and Title. Built once and kept: rebuilding it per step
        /// would restart every drift from zero and read as a jump cut. It seats itself directly
        /// above the backdrop plate and below BootDressing, so the tentacles and glyph rails
        /// still sit in front of it.
        /// </summary>
        void ApplyLivingSky(Step step)
        {
            if (_panelHost == null) return;
            var wants = step == Step.Splash || step == Step.Title;
            var sky = _panelHost.transform.Find("LivingSky");
            if (sky == null)
            {
                if (!wants) return;
                var built = LivingSky.Build(_panelHost.transform);
                sky = built != null ? built.transform : null;
                if (sky == null) return;
            }

            sky.gameObject.SetActive(wants);
            if (!wants) return;

            var backdrop = _panelHost.transform.Find("BootBackdrop");
            sky.SetSiblingIndex(backdrop != null ? backdrop.GetSiblingIndex() + 1 : 0);
        }

        // ── Genesis ─────────────────────────────────────────────────────────
        //
        // GDD §2, in one frame: "Holactie creates the gods … Azathot curses Umbrax. The people
        // burn him." Creation detonates above while the elder twin — the shadow, the guardian —
        // burns below. Every system in this game is downstream of that fire: the horned skull
        // Andrax buries, the seal Ptah and Anubis set, the Shadow Realm it becomes, the
        // barrier-stone Akun carries into Egypt, and the Tears it eventually opens.

        static void SetA(Graphic g, float a)
        {
            if (g == null) return;
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        /// <summary>0 before <paramref name="a"/>, 1 after <paramref name="b"/>, eased between.</summary>
        static float Beat(float t, float a, float b) =>
            MenuMotion.OutCubic(Mathf.Clamp01((t - a) / Mathf.Max(0.0001f, b - a)));

        IEnumerator GenesisRoutine()
        {
            var host = Panels;
            var skipped = false;
            FullScreenTap(() => skipped = true);

            // Back to front. Everything starts invisible; the timeline below is the only thing
            // that reveals it, so a single frame can never flash an unposed layer.
            var voidPlate = FlowChrome.MakeImage(host, "Void", null, new Color(0.010f, 0.009f, 0.026f, 1f));
            FlowChrome.Stretch(voidPlate);

            var stars = FlowChrome.MakeImage(host, "Stars", ImagineAssets.CinematicGenesisStars(),
                new Color(1f, 1f, 1f, 0f));
            FlowChrome.Place(stars, -0.25f, -0.10f, 1.25f, 1.10f);

            var nebula = FlowChrome.MakeImage(host, "Nebula", EldritchAssets.EmberSoft(),
                new Color(0.54f, 0.32f, 1f, 0f));
            FlowChrome.Place(nebula, -0.30f, 0.16f, 1.30f, 1.10f);

            var nebulaGold = FlowChrome.MakeImage(host, "NebulaGold", EldritchAssets.EmberSoft(),
                new Color(1f, 0.78f, 0.34f, 0f));
            FlowChrome.Place(nebulaGold, 0.02f, 0.38f, 0.98f, 0.94f);

            var ring = FlowChrome.MakeImage(host, "Shock", EldritchAssets.RuneRing(),
                new Color(1f, 0.94f, 0.82f, 0f));
            ring.GetComponent<Image>().preserveAspect = true;

            var core = FlowChrome.MakeImage(host, "Singularity", EldritchAssets.EmberSoft(),
                new Color(1f, 1f, 1f, 0f));
            core.GetComponent<Image>().preserveAspect = true;

            // Pyre sits on the lower third so the cosmos keeps the frame.
            var glow = FlowChrome.MakeImage(host, "PyreGlow", EldritchAssets.EmberSoft(),
                new Color(1f, 0.46f, 0.13f, 0f));
            FlowChrome.Place(glow, 0.06f, -0.06f, 0.94f, 0.40f);

            var pyre = FlowChrome.MakeImage(host, "Pyre", ImagineAssets.CinematicGenesisPyre(),
                new Color(1f, 1f, 1f, 0f));
            pyre.GetComponent<Image>().preserveAspect = true;
            FlowChrome.Place(pyre, 0.22f, 0.010f, 0.78f, 0.345f);

            // The shadow the fire makes — it climbs INTO the new sky. That is the Shadow Realm
            // being made, so it has to travel upward out of the pyre, never sit as a vignette.
            var shadow = FlowChrome.MakeImage(host, "ShadowBleed", EldritchAssets.EmberSoft(),
                new Color(0.26f, 0.05f, 0.46f, 0f));
            FlowChrome.Place(shadow, -0.10f, -0.05f, 1.10f, 0.55f);

            var flash = FlowChrome.MakeImage(host, "Flash", null, new Color(1f, 0.97f, 0.92f, 0f));
            FlowChrome.Stretch(flash);

            var line = FlowChrome.Label(host, "Line", "", 15, FlowChrome.Cream, TextAnchor.MiddleCenter);
            FlowChrome.Place(line.rectTransform, 0.08f, 0.44f, 0.92f, 0.52f);
            SetA(line, 0f);

            var vImg = voidPlate.GetComponent<Image>();
            var starImg = stars.GetComponent<Image>();
            var nebImg = nebula.GetComponent<Image>();
            var goldImg = nebulaGold.GetComponent<Image>();
            var ringImg = ring.GetComponent<Image>();
            var coreImg = core.GetComponent<Image>();
            var glowImg = glow.GetComponent<Image>();
            var pyreImg = pyre.GetComponent<Image>();
            var shadowImg = shadow.GetComponent<Image>();
            var flashImg = flash.GetComponent<Image>();

            const float Total = 9.2f;
            var t = 0f;
            var lit = false;

            while (t < Total && !skipped)
            {
                t += Time.unscaledDeltaTime;

                // 0.0–1.5  a single point of light, breathing. Holactie, before anything.
                var wake = Beat(t, 0.15f, 1.35f);
                var breath = 1f + 0.16f * Mathf.Sin(t * 5.2f);
                SetA(coreImg, wake * (t < 1.5f ? 0.95f : Mathf.Max(0f, 1f - Beat(t, 1.5f, 2.6f))));
                var coreS = Mathf.Lerp(0.012f, 0.055f, wake) * breath;
                FlowChrome.Place(core, 0.5f - coreS, 0.62f - coreS * 1.9f, 0.5f + coreS, 0.62f + coreS * 1.9f);

                // 1.5  detonation
                var det = Beat(t, 1.5f, 1.62f);
                var settle = Beat(t, 1.62f, 3.0f);
                SetA(flashImg, det * (1f - settle) * 0.92f);

                // shock ring — expands past the frame and thins as it goes
                var rs = Mathf.Lerp(0.02f, 1.35f, Beat(t, 1.5f, 4.6f));
                FlowChrome.Place(ring, 0.5f - rs, 0.62f - rs * 1.9f, 0.5f + rs, 0.62f + rs * 1.9f);
                SetA(ringImg, det * (1f - Beat(t, 2.0f, 4.6f)) * 0.75f);

                // 1.6–4.5  time-lapse: the starfield resolves and keeps expanding
                var born = Beat(t, 1.60f, 4.20f);
                SetA(starImg, born * 0.95f);
                var ss = Mathf.Lerp(0.55f, 1.28f, Beat(t, 1.55f, Total));
                FlowChrome.Place(stars, 0.5f - ss * 0.78f, 0.5f - ss * 0.66f,
                    0.5f + ss * 0.78f, 0.5f + ss * 0.66f);
                SetA(nebImg, born * 0.42f);
                SetA(goldImg, born * (1f - Beat(t, 5.0f, Total)) * 0.34f);
                SetA(vImg, 1f);

                // 3.4–6.4  below: the pyre takes light, then catches
                var rise = Beat(t, 3.40f, 5.00f);
                var burn = Beat(t, 4.30f, 6.60f);
                SetA(pyreImg, rise);
                FlowChrome.Place(pyre, 0.22f, 0.010f - 0.05f * (1f - rise),
                    0.78f, 0.345f - 0.05f * (1f - rise));
                var flicker = 0.86f + 0.14f * Mathf.Sin(t * 11f) * Mathf.Sin(t * 4.3f);
                SetA(glowImg, burn * 0.80f * flicker);

                if (!lit && t >= 4.30f)
                {
                    lit = true;
                    Burst(pyre, 26, UiEmberField.Kind.Ember,
                        new Color(1f, 0.55f, 0.18f, 1f), 0f, 0.55f);
                }

                // 6.2–9.2  what the fire makes: violet climbing into the newborn sky
                var bleed = Beat(t, 6.20f, 8.60f);
                SetA(shadowImg, bleed * 0.95f);
                FlowChrome.Place(shadow, -0.10f, -0.05f + 0.30f * bleed,
                    1.10f, 0.55f + 0.42f * bleed);

                // captions — one lore beat at a time, never two on screen
                string say = null;
                float sa = 0f;
                if (t < 1.5f) { say = "Before the world, Holactie."; sa = Beat(t, 0.5f, 1.1f) * (1f - Beat(t, 1.30f, 1.50f)); }
                else if (t < 4.2f) { say = "The gods are made."; sa = Beat(t, 2.1f, 2.7f) * (1f - Beat(t, 3.8f, 4.2f)); }
                else if (t < 6.6f) { say = "Azathot curses Umbrax. The people burn him."; sa = Beat(t, 4.5f, 5.1f) * (1f - Beat(t, 6.2f, 6.6f)); }
                else { say = "What burns is not gone. It is sealed."; sa = Beat(t, 6.9f, 7.5f) * (1f - Beat(t, 8.8f, 9.2f)); }
                if (line != null && say != null)
                {
                    if (line.text != say) line.text = say;
                    SetA(line, sa);
                }

                yield return null;
            }

            // The fire does not end in light. It ends SEALED, so the handoff closes down into
            // the Shadow Realm instead of blowing out to white — and the step crossfade then
            // carries that darkness straight into Splash as one continuous move.
            if (!skipped)
            {
                flashImg.color = new Color(0.075f, 0.040f, 0.130f, 0f);
                var o = 0f;
                while (o < 0.60f)
                {
                    o += Time.unscaledDeltaTime;
                    SetA(flashImg, MenuMotion.OutCubic(Mathf.Clamp01(o / 0.60f)) * 0.96f);
                    yield return null;
                }
            }

            Go(Step.Splash);
        }

        // ── Splash ──────────────────────────────────────────────────────────

        void BuildSplash()
        {
            // Art-first: Tear-in-the-sky atmosphere is the splash. Labels sit in the
            // lower desert so the sealed pyramid and cosmic wound stay readable.

            // The wordmark is struck INTO stone: an Intact plinth (gold and Andrax's blue at full,
            // one hairline) inside a step that is otherwise Sealed, so frame one already shows the
            // material the whole cascade is made of — and the arc has a floor to fall from.
            var plinth = FlowChrome.SealPlate(Panels, "Plinth", SealState.Intact);
            if (plinth != null)
            {
                FlowChrome.Place(plinth.Rect, 0.04f, 0.035f, 0.96f, 0.175f);
                plinth.PlayOpen(0.85f);
                Burst(plinth.Rect, 14, UiEmberField.Kind.Ember,
                    EldritchPalette.MillenniumGold, 0f, 0.4f);
            }

            // explicit == null, not ??: a destroyed / fake-null Sprite is non-null to C# (Unity null)
            var emblem = ImagineAssets.EmblemSpiritEye();
            if (emblem == null) emblem = WrldzPresentation.SpiritEye();
            if (emblem == null) emblem = DuelystUi.OrbRing();
            if (emblem == null) emblem = DuelystUi.BtnCircle();
            var disk = FlowChrome.MakeImage(Panels, "Emblem", emblem, Color.white);
            FlowChrome.Place(disk, 0.34f, 0.20f, 0.66f, 0.38f);
            disk.GetComponent<Image>().preserveAspect = true;
            MenuHoloPulse.AttachFeel(disk.gameObject, press: false, sheen: true);

            var series = FlowChrome.Label(Panels, "S", "DUEL MONSTERS", 16, FlowChrome.Gold);
            FlowChrome.Place(series.rectTransform, 0.06f, 0.125f, 0.94f, 0.170f);

            // Gold, not GoldHot: on this surface the gold is the Millennium inlay of the seal, not
            // a highlight on a logo.
            var title = FlowChrome.Label(Panels, "T", "WRLDZ", 44, FlowChrome.Gold);
            FlowChrome.Place(title.rectTransform, 0.06f, 0.060f, 0.94f, 0.128f);

            var tag = FlowChrome.Label(Panels, "Tag",
                "Tears between worlds  ·  Shadow Realm  ·  Spirit Navis",
                13, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(tag.rectTransform, 0.06f, 0.038f, 0.94f, 0.062f);

            var tap = FlowChrome.Label(Panels, "Tap", "TAP TO CONTINUE", 16, FlowChrome.Cream);
            FlowChrome.Place(tap.rectTransform, 0.1f, 0.00f, 0.9f, 0.030f);

            FullScreenTap(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.Credits);
            });

            _routine = StartCoroutine(AutoAdvance(3.5f, Step.Credits));
        }

        IEnumerator AutoAdvance(float sec, Step next)
        {
            yield return new WaitForSecondsRealtime(sec);
            if (_step == Step.Splash || _step == Step.Credits)
                Go(next);
        }

        /// <summary>The credit text (the legacy cascade's credits step; P3, D9: SETTINGS → CREDITS shows it too).</summary>
        public const string CreditsText =
            "DUEL MONSTERS: WRLDZ\n\n" +
            "Story & design\nGreat SaiyaDave\n\n" +
            "Indie prototype\nTCG rules · AR GPS";

        void BuildCredits()
        {
            Header("CREDITS", "Project ARGON · Great SaiyaDave");
            FlowChrome.StepIndicator(Panels, 0, 6);
            var box = FlowChrome.PanelBox(Panels, "Credits");
            FlowChrome.Place(box, 0.08f, 0.24f, 0.92f, 0.78f);
            var seal = FlowChrome.SealOf(box);
            if (seal != null) seal.PlayOpen(0.55f);

            var body = FlowChrome.Label(box, "B",
                CreditsText + "\n\n" +
                "Touch to continue",
                16, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(body.rectTransform, 0.08f, 0.1f, 0.92f, 0.9f);

            FullScreenTap(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.Title);
            });
            // 0.8 s was not long enough to read a credits roll; the step was effectively a flash.
            _routine = StartCoroutine(AutoAdvance(2.6f, Step.Title));
        }

        void BuildTitle()
        {
            // GDD: Akun, of Andrax's line, carries a barrier-stone into Egypt — that stone is the
            // seed of the Millennium Items. This card is the home of the object the whole metaphor
            // is built from, so it is the one Intact plate in the cascade: gold dominant, Andrax's
            // blue at full, a single hairline.
            var plinth = FlowChrome.SealPlate(Panels, "Plinth", SealState.Intact);
            if (plinth != null)
            {
                FlowChrome.Place(plinth.Rect, 0.04f, 0.070f, 0.96f, 0.205f);
                plinth.PlayOpen(0.8f);
                var band = HieroglyphBorder.Create(plinth.Rect, HieroglyphBorder.Edge.Top, 34f,
                    EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.92f), 2.6f);
                if (band != null) band.SetIntensity(1.1f);
                Burst(plinth.Rect, 16, UiEmberField.Kind.Ember,
                    EldritchPalette.MillenniumGold, 0f, 0.35f);
            }

            // Title art already holds a dark lower band; keep chrome off the Tear.
            var spirit = ImagineAssets.EmblemSpiritEye();
            if (spirit == null) spirit = WrldzPresentation.SpiritEye();
            if (spirit == null) spirit = DuelystUi.BtnCircle();
            var emblem = FlowChrome.MakeImage(Panels, "Emblem", spirit, Color.white);
            FlowChrome.Place(emblem, 0.34f, 0.22f, 0.66f, 0.40f);
            emblem.GetComponent<Image>().preserveAspect = true;
            MenuHoloPulse.AttachFeel(emblem.gameObject, press: false, sheen: true);

            var series = FlowChrome.Label(Panels, "Series", "DUEL MONSTERS", 16, FlowChrome.Gold);
            FlowChrome.Place(series.rectTransform, 0.06f, 0.152f, 0.94f, 0.198f);
            // Off SolidVisionCyan: cyan is holography, gold is the seal's Millennium inlay.
            var sub = FlowChrome.Label(Panels, "S", "WRLDZ", 44, FlowChrome.Gold);
            FlowChrome.Place(sub.rectTransform, 0.06f, 0.080f, 0.94f, 0.155f);

            // The very first press in the game strains the seal: BarrierStone's own pointer-down
            // drives _Pressed, so the interaction metaphor is taught before any menu.
            var begin = FlowChrome.BtnEx(Panels, "Begin", "TOUCH TO BEGIN",
                new Color(0.15f, 0.45f, 0.35f, 1f), () =>
                {
                    FreeUiKit.PlayConfirm();
                    Go(Step.BootLoad);
                }, SealState.Intact);
            if (begin != null) FlowChrome.Place(begin.Rect, 0.22f, 0.012f, 0.78f, 0.062f);
            WrldzBuild.Log();

            // Fallback only — it is seated BELOW the step host, so the button wins the raycast.
            FullScreenTap(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.BootLoad);
            });
        }

        /// <summary>
        /// Full-bleed tap fallback. Deliberately a child of the panel host, not the step host: it
        /// must never be scaled to 0.82 by the reveal stagger or go untappable while it runs, and
        /// it is seated under the step host so real buttons always win the raycast.
        /// </summary>
        GameObject FullScreenTap(UnityEngine.Events.UnityAction onTap)
        {
            var catcher = new GameObject("TapCatch", typeof(RectTransform), typeof(Image), typeof(Button));
            catcher.transform.SetParent(_panelHost.transform, false);
            var cImg = catcher.GetComponent<Image>();
            cImg.sprite = UiFoundation.WhiteSprite();
            cImg.color = new Color(0, 0, 0, 0.01f);
            cImg.raycastTarget = true;
            FlowChrome.Stretch(catcher.GetComponent<RectTransform>());
            catcher.GetComponent<Button>().onClick.AddListener(onTap);
            if (_stepHost != null)
                catcher.transform.SetSiblingIndex(_stepHost.transform.GetSiblingIndex());
            return catcher;
        }

        // ── Boot load (session check) ───────────────────────────────────────

        IEnumerator BootLoadRoutine()
        {
            // Sealed: a seam being re-struck, not opened. Two stages rather than one 0.2 s blink,
            // so the gold inlay running into the carved channel is actually perceivable.
            BuildLoadingPanel("Waking the spirits…", "Checking local session", SealState.Sealed);
            yield return SimulateLoad(new[]
            {
                ("Reading the seam…", 0.45f),
                ("Checking account…", 0.45f)
            });

            LocalAccountStore.EnsureLoaded();
            AppSession.Ensure().RefreshFromStore();

            if (!LocalAccountStore.AgeGatePassed)
            {
                Go(Step.Dob);
                yield break;
            }

            if (AppSession.Ensure().IsLoggedIn)
            {
                if (!LocalAccountStore.TermsAccepted)
                {
                    Go(Step.Terms);
                    yield break;
                }

                ContinueAfterAuth();
                yield break;
            }

            Go(Step.AuthChoice);
        }

        // ── DOB (age gate) ──────────────────────────────────────────────────

        void BuildDob()
        {
            // The quietest stone in the file. A legal age gate should read as intact barrier, and
            // the contrast against the narrative steps is what makes the Prologue's failure land —
            // so nothing here is escalated, and every plate inherits the step's Sealed default.
            Header("DATE OF BIRTH", "Required to duel in WRLDZ");

            var box = FlowChrome.PanelBox(Panels, "DobBox");
            FlowChrome.Place(box, 0.06f, 0.34f, 0.94f, 0.74f);

            var help = FlowChrome.Label(box, "H",
                "Select your birthday.\nYou must be at least " + MinAge + " years old.",
                18, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(help.rectTransform, 0.06f, 0.82f, 0.94f, 0.96f);

            // Column labels
            var lm = FlowChrome.Label(box, "Lm", "MONTH", 15, FlowChrome.Gold, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(lm.rectTransform, 0.04f, 0.72f, 0.40f, 0.82f);
            var ld = FlowChrome.Label(box, "Ld", "DAY", 15, FlowChrome.Gold, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(ld.rectTransform, 0.41f, 0.72f, 0.62f, 0.82f);
            var ly = FlowChrome.Label(box, "Ly", "YEAR", 15, FlowChrome.Gold, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(ly.rectTransform, 0.63f, 0.72f, 0.96f, 0.82f);

            // Month names
            var monthNames = new List<string>(12);
            for (var m = 1; m <= 12; m++)
                monthNames.Add(CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m));

            // Years: current-MinAge down to 120 years back (oldest first in list = newest default)
            _yearValues.Clear();
            var maxYear = DateTime.UtcNow.Year - MinAge;
            var minYear = DateTime.UtcNow.Year - 120;
            var yearLabels = new List<string>();
            for (var y = maxYear; y >= minYear; y--)
            {
                _yearValues.Add(y);
                yearLabels.Add(y.ToString());
            }

            // Default ~18 years old if available
            var defaultYearIdx = Mathf.Clamp(18 - MinAge, 0, yearLabels.Count - 1);

            _dobMonth = FlowChrome.Dropdown(box, "Month", monthNames, defaultIndex: 0);
            FlowChrome.Place(_dobMonth.GetComponent<RectTransform>(), 0.04f, 0.48f, 0.40f, 0.70f);

            // Day 1–31 initially; refreshed when month/year changes
            var dayLabels = new List<string>(31);
            for (var d = 1; d <= 31; d++) dayLabels.Add(d.ToString());
            _dobDay = FlowChrome.Dropdown(box, "Day", dayLabels, defaultIndex: 0);
            FlowChrome.Place(_dobDay.GetComponent<RectTransform>(), 0.41f, 0.48f, 0.62f, 0.70f);

            _dobYear = FlowChrome.Dropdown(box, "Year", yearLabels, defaultIndex: defaultYearIdx);
            FlowChrome.Place(_dobYear.GetComponent<RectTransform>(), 0.63f, 0.48f, 0.96f, 0.70f);

            // Opened list draws above siblings (Continue, other dropdowns)
            EnsureDropdownOnTop(_dobMonth);
            EnsureDropdownOnTop(_dobDay);
            EnsureDropdownOnTop(_dobYear);

            // Keep day list valid for selected month/year
            _dobMonth.onValueChanged.AddListener(_ => RefreshDobDays());
            _dobYear.onValueChanged.AddListener(_ => RefreshDobDays());
            RefreshDobDays();

            _error = FlowChrome.Label(box, "Err", "", 14, FlowChrome.Danger, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_error.rectTransform, 0.06f, 0.28f, 0.94f, 0.44f);

            var next = FlowChrome.Btn(box, "Next", "CONTINUE", new Color(0.12f, 0.45f, 0.35f, 1f), OnDobContinue);
            FlowChrome.Place(next.GetComponent<RectTransform>(), 0.15f, 0.06f, 0.85f, 0.22f);

            FooterNote("We only store your age gate on this device.");
        }

        static void EnsureDropdownOnTop(Dropdown dd)
        {
            if (dd == null) return;
            var catcher = dd.gameObject.AddComponent<DropdownFront>();
            catcher.Target = dd.transform;
        }

        /// <summary>Raises dropdown in hierarchy when opened so the list is not covered.</summary>
        class DropdownFront : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
        {
            public Transform Target;
            public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (Target != null) Target.SetAsLastSibling();
            }
        }

        void RefreshDobDays()
        {
            if (_dobMonth == null || _dobDay == null || _dobYear == null) return;

            var month = _dobMonth.value + 1; // 1–12
            var year = SelectedDobYear();
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var prevDay = _dobDay.value + 1; // 1-based

            _dobDay.ClearOptions();
            var labels = new List<string>(daysInMonth);
            for (var d = 1; d <= daysInMonth; d++)
                labels.Add(d.ToString());
            _dobDay.AddOptions(labels);

            var newIdx = Mathf.Clamp(prevDay - 1, 0, daysInMonth - 1);
            _dobDay.value = newIdx;
            _dobDay.RefreshShownValue();
        }

        int SelectedDobYear()
        {
            if (_dobYear == null || _yearValues.Count == 0) return DateTime.UtcNow.Year - MinAge;
            var i = Mathf.Clamp(_dobYear.value, 0, _yearValues.Count - 1);
            return _yearValues[i];
        }

        void OnDobContinue()
        {
            if (_dobMonth == null || _dobDay == null || _dobYear == null)
            {
                SetError("Date of birth controls missing.");
                return;
            }

            var y = SelectedDobYear();
            var m = _dobMonth.value + 1;
            var d = _dobDay.value + 1;

            if (!LocalAccountStore.TryPassAgeGate(y, m, d, MinAge, out var err))
            {
                SetError(err);
                return;
            }

            FreeUiKit.PlayConfirm();
            Go(Step.AuthChoice);
        }

        // ── Auth choice ─────────────────────────────────────────────────────

        void BuildAuthChoice()
        {
            Header("ACCOUNT", "New player, log in, or Google");

            var box = FlowChrome.PanelBox(Panels, "AuthChoice");
            FlowChrome.Place(box, 0.08f, 0.28f, 0.92f, 0.78f);

            var blurb = FlowChrome.Label(box, "B",
                "New players register a Spirit Dueler ID.\nGoogle uses the account you already have.",
                14, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(blurb.rectTransform, 0.06f, 0.84f, 0.94f, 0.98f);

            var create = FlowChrome.Btn(box, "Create", "NEW PLAYER", new Color(0.15f, 0.4f, 0.75f, 1f),
                () => Go(Step.Create));
            FlowChrome.Place(create.GetComponent<RectTransform>(), 0.1f, 0.68f, 0.9f, 0.80f);

            var login = FlowChrome.Btn(box, "Login", "LOG IN", new Color(0.35f, 0.2f, 0.55f, 1f),
                () => Go(Step.Login), SealState.Fractured);
            FlowChrome.Place(login.GetComponent<RectTransform>(), 0.1f, 0.54f, 0.9f, 0.66f);

            // Optional doors stack under LOG IN; hidden ones leave no gap.
            var y1 = 0.52f;
            if (GoogleButtonAvailable)
            {
                var google = FlowChrome.Btn(box, "Google", "SIGN IN WITH GOOGLE", new Color(0.22f, 0.32f, 0.48f, 1f),
                    OnGoogle, SealState.Sealed);
                FlowChrome.Place(google.GetComponent<RectTransform>(), 0.1f, y1 - 0.12f, 0.9f, y1);
                y1 -= 0.14f;
            }

            if (LabDoorsEnabled)
            {
                var desk = FlowChrome.Btn(box, "DeskLab", "DESKTOP LAB (NO EQUIPMENT)",
                    new Color(0.15f, 0.55f, 0.65f, 1f), () =>
                    {
                        if (!LabDoorsEnabled) return;
                        FreeUiKit.PlayConfirm();
                        _step = Step.Done;
                        DesktopLabApp.Launch();
                    }, SealState.Sealed);
                FlowChrome.Place(desk.GetComponent<RectTransform>(), 0.1f, y1 - 0.10f, 0.9f, y1);
                y1 -= 0.12f;

                var test = FlowChrome.Btn(box, "TestDuel", "TEST DUEL (LAB)", new Color(0.75f, 0.35f, 0.12f, 1f),
                    LaunchLabTestDuel, SealState.Sealed);
                FlowChrome.Place(test.GetComponent<RectTransform>(), 0.1f, y1 - 0.10f, 0.9f, y1);
            }

            // Lab note + raw database path are debug aids (Editor / Development builds only).
            var note = "";
            if (LabDoorsEnabled)
                note = "DESKTOP LAB: full hub · map · AR sim · no phone/headset.\n" +
                       "TEST DUEL: lab_tester · Map → Boot.\nDB: " + LocalAccountStore.DatabasePath;
            _error = FlowChrome.Label(box, "G", note,
                11, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_error.rectTransform, 0.04f, 0.00f, 0.96f, 0.12f);
        }

        // ── Login ───────────────────────────────────────────────────────────

        void BuildLogin()
        {
            // Quiet until it fails. The failure is the payoff: SoulFractureService's deactivated
            // message already says "Umbrax holds the last fragment", so a failed login condemns
            // the stone of the slot that failed rather than printing a red sentence at it.
            Header("LOG IN", "Welcome back, Duelist");

            var box = FlowChrome.PanelBox(Panels, "LoginBox");
            FlowChrome.Place(box, 0.06f, 0.32f, 0.94f, 0.74f);

            _user = FlowChrome.Field(box, "User", "Username");
            FlowChrome.Place(_user.GetComponent<RectTransform>(), 0.08f, 0.68f, 0.92f, 0.88f);
            _pass = FlowChrome.Field(box, "Pass", "Password", password: true);
            FlowChrome.Place(_pass.GetComponent<RectTransform>(), 0.08f, 0.44f, 0.92f, 0.64f);

            _error = FlowChrome.Label(box, "Err", "", 14, FlowChrome.Danger, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_error.rectTransform, 0.06f, 0.28f, 0.94f, 0.42f);

            var goBtn = FlowChrome.Btn(box, "Go", "LOG IN", new Color(0.15f, 0.45f, 0.35f, 1f), OnLogin);
            if (GoogleButtonAvailable)
            {
                FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.12f, 0.08f, 0.48f, 0.24f);
                var google = FlowChrome.Btn(box, "Google", "GOOGLE", new Color(0.22f, 0.32f, 0.48f, 1f),
                    OnGoogle, SealState.Sealed);
                FlowChrome.Place(google.GetComponent<RectTransform>(), 0.52f, 0.08f, 0.88f, 0.24f);
            }
            else
                FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.25f, 0.08f, 0.75f, 0.24f);

            var back = FlowChrome.Btn(Panels, "Back", "BACK", new Color(0.25f, 0.25f, 0.32f, 1f),
                () => Go(Step.AuthChoice));
            FlowChrome.Place(back.GetComponent<RectTransform>(), 0.25f, 0.18f, 0.75f, 0.26f);
        }

        /// <summary>
        /// Blood-light the slots that failed for a beat, then let them seal again. Condemned is
        /// exactly the state that means this (blood pressure, blood inlay, red bleed), and
        /// keepFracture widens the crack the slot already had instead of rolling a new one.
        /// </summary>
        void FlashFieldError(params InputField[] fields)
        {
            if (fields == null) return;
            var stones = new List<BarrierStone>(fields.Length);
            foreach (var f in fields)
            {
                var s = f != null ? f.GetComponent<BarrierStone>() : null;
                if (s == null) continue;
                stones.Add(s);
                s.SetState(SealState.Condemned);
                Burst(s.Rect, 10, UiEmberField.Kind.Spark, EldritchPalette.BloodDanger);
            }

            if (stones.Count > 0) Fx(ResealCo(stones, 1.0f));
        }

        IEnumerator ResealCo(List<BarrierStone> stones, float hold)
        {
            yield return new WaitForSecondsRealtime(hold);
            for (var i = 0; i < stones.Count; i++)
                if (stones[i] != null) stones[i].SetState(SealState.Sealed);
        }

        void OnLogin()
        {
            if (!LocalAccountStore.TryLogin(Txt(_user), Txt(_pass), out var acc, out var err))
            {
                SetError(err ?? "Login failed.");
                FlashFieldError(_user, _pass);
                return;
            }

            EnterSession(acc);
        }

        void OnGoogle()
        {
            // One flow at a time: a second tap would fail on the loopback port and overwrite the
            // live "Waiting…" message, and the orphaned first flow could still enter the session.
            if (_googleBusy) return;
            if (!GoogleSignIn.TryLoadClientId(out var clientId, out var missing))
            {
                SetError(missing ?? GoogleSignIn.MissingClientMessage);
                return;
            }

            FreeUiKit.PlaySelect();
            SetError("Waiting for Google in the browser…");
            _googleBusy = true;
            _routine = StartCoroutine(GoogleSignInCo(clientId));
        }

        IEnumerator GoogleSignInCo(string clientId)
        {
            var slot = new GoogleSignIn.Profile[1];
            var errSlot = new string[1];
            var flag = new int[1];
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    slot[0] = GoogleSignIn.SignIn(clientId, out errSlot[0]);
                }
                catch (Exception ex)
                {
                    errSlot[0] = ex.Message;
                }

                Thread.MemoryBarrier();
                flag[0] = 1;
            });

            while (true)
            {
                Thread.MemoryBarrier();
                if (flag[0] == 1) break;
                yield return null;
            }

            // Cancelled (step left → StopStepCoroutines) while the worker ran: never enter a session.
            if (!_googleBusy) yield break;
            _googleBusy = false;

            var profile = slot[0];
            if (profile == null)
            {
                SetError(errSlot[0] ?? "Google sign-in failed.");
                yield break;
            }

            if (!LocalAccountStore.TryLoginGoogle(profile.Sub, profile.Email, profile.Name, out var acc, out var loginErr))
            {
                SetError(loginErr ?? "Could not open that Google player.");
                yield break;
            }

            EnterSession(acc);
        }

        void EnterSession(LocalAccountStore.Account acc)
        {
            AppSession.Ensure().SetAccount(acc);
            AppSession.Ensure().RefreshFromStore();
            if (!AppSession.Ensure().IsLoggedIn)
            {
                SetError("Login OK but session missing — restart Boot.");
                return;
            }

            FreeUiKit.PlayConfirm();
            if (!LocalAccountStore.TermsAccepted)
            {
                Go(Step.Terms);
                return;
            }

            ContinueAfterAuth();
        }

        // ── Create account ──────────────────────────────────────────────────

        void BuildCreate()
        {
            Header("REGISTER", "New player — forge a Spirit Dueler ID");

            var box = FlowChrome.PanelBox(Panels, "CreateBox");
            FlowChrome.Place(box, 0.05f, 0.34f, 0.95f, 0.84f);
            // Held for OnCreate: the successful forge is the one materialize beat on this screen.
            _panelSeal = FlowChrome.SealOf(box);

            _display = FlowChrome.Field(box, "Disp", "Display name");
            FlowChrome.Place(_display.GetComponent<RectTransform>(), 0.06f, 0.84f, 0.94f, 0.96f);
            _user = FlowChrome.Field(box, "User", "Username");
            FlowChrome.Place(_user.GetComponent<RectTransform>(), 0.06f, 0.70f, 0.94f, 0.82f);
            _pass = FlowChrome.Field(box, "Pass", "Password", password: true);
            FlowChrome.Place(_pass.GetComponent<RectTransform>(), 0.06f, 0.56f, 0.94f, 0.68f);
            _pass2 = FlowChrome.Field(box, "Pass2", "Confirm password", password: true);
            FlowChrome.Place(_pass2.GetComponent<RectTransform>(), 0.06f, 0.42f, 0.94f, 0.54f);
            _email = FlowChrome.Field(box, "Email", "Email (optional)");
            FlowChrome.Place(_email.GetComponent<RectTransform>(), 0.06f, 0.28f, 0.94f, 0.40f);

            _error = FlowChrome.Label(box, "Err", "", 13, FlowChrome.Danger, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_error.rectTransform, 0.05f, 0.14f, 0.95f, 0.26f);

            // The only gold seal on the screen.
            var goBtn = FlowChrome.Btn(box, "Go", "REGISTER", new Color(0.15f, 0.4f, 0.75f, 1f), OnCreate);
            if (GoogleButtonAvailable)
            {
                FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.08f, 0.02f, 0.48f, 0.12f);
                var google = FlowChrome.Btn(box, "Google", "GOOGLE", new Color(0.22f, 0.32f, 0.48f, 1f),
                    OnGoogle, SealState.Sealed);
                FlowChrome.Place(google.GetComponent<RectTransform>(), 0.52f, 0.02f, 0.92f, 0.12f);
            }
            else
                FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.25f, 0.02f, 0.75f, 0.12f);

            // The duplicate TEST DUEL (LAB) that used to sit here is gone: it repeated AuthChoice's
            // own lab door directly under an account form, which is the wrong place for it.
            var back = FlowChrome.Btn(Panels, "Back", "BACK", new Color(0.25f, 0.25f, 0.32f, 1f),
                () => Go(Step.AuthChoice));
            FlowChrome.Place(back.GetComponent<RectTransform>(), 0.25f, 0.20f, 0.75f, 0.28f);
        }

        /// <summary>
        /// Skip DOB/terms/prologue/Kuriboh — log in lab_tester and open DuelSlice immediately.
        /// Map / leave returns to Boot for another quick run.
        /// </summary>
        void LaunchLabTestDuel()
        {
            if (!LabDoorsEnabled)
            {
                Debug.LogWarning("[WRLDZ] TEST DUEL (LAB) is Editor / Development builds only.");
                return;
            }

            FreeUiKit.PlayConfirm();
            SetError("");
            if (!AppSession.Ensure().StartLabTestDuel())
            {
                SetError("Lab test duel failed — see console.");
                Debug.LogError("[WRLDZ] StartLabTestDuel failed from Boot UI.");
            }
            else
            {
                _step = Step.Done;
            }
        }

        void OnCreate()
        {
            var user = Txt(_user)?.Trim() ?? "";
            var pass = Txt(_pass) ?? "";
            var pass2 = Txt(_pass2) ?? "";
            var display = Txt(_display)?.Trim() ?? "";
            var email = Txt(_email)?.Trim() ?? "";

            if (string.IsNullOrEmpty(user))
            {
                SetError("Enter a username (3+ characters).");
                FlashFieldError(_user);
                return;
            }

            if (pass != pass2)
            {
                SetError("Passwords do not match.");
                FlashFieldError(_pass, _pass2);
                return;
            }

            if (!LocalAccountStore.TryCreate(user, pass, display, email, out var acc, out var err))
            {
                SetError(err ?? "Could not create account.");
                FlashFieldError(_user, _pass);
                Debug.LogWarning($"[WRLDZ] Create failed: {err} (db={LocalAccountStore.DatabasePath})");
                return;
            }

            // Persist session before any scene change
            AppSession.Ensure().SetAccount(acc);
            AppSession.Ensure().RefreshFromStore();
            if (!AppSession.Ensure().IsLoggedIn)
            {
                SetError("Account saved but session failed — try LOG IN.");
                Debug.LogError("[WRLDZ] Session null after create. DB=" + LocalAccountStore.DatabasePath);
                return;
            }

            FreeUiKit.PlayConfirm();
            // New accounts always see Terms once before the overworld.
            SetError("");

            // The ID being forged: the plate re-knits itself and throws Millennium gold. Persistence
            // is already done above — this beat only delays the navigation.
            if (Application.isPlaying && _panelSeal != null)
            {
                _panelSeal.PlayOpen(0.5f);
                Burst(_panelSeal.Rect, 22, UiEmberField.Kind.Ember, EldritchPalette.MillenniumGold);
                WrldzAudio.PlayMillenniumEye();
                _beatCo = StartCoroutine(AfterBeat(0.5f, () => Go(Step.Terms)));
                return;
            }

            Go(Step.Terms);
        }

        /// <summary>
        /// Hold for a scripted seal beat, then navigate. Tracked in <see cref="_beatCo"/> and
        /// stopped by <see cref="Go"/>, so a beat can never outlive the step that started it.
        /// </summary>
        IEnumerator AfterBeat(float sec, Action then)
        {
            yield return new WaitForSecondsRealtime(sec);
            then?.Invoke();
        }

        // ── Terms ───────────────────────────────────────────────────────────

        void BuildTerms()
        {
            // GDD: Umbrax and Andrax found Lemuria and a contract-barrier — the ancestor of Shadow
            // Games. Accepting terms IS entering the contract-barrier, so this is the quietest
            // panel in the cascade and gets no smoke and no breach; one line of copy makes the
            // rhyme explicit for the cost of nothing.
            Header("TERMS", "Indie / fan-phase agreement");

            var box = FlowChrome.PanelBox(Panels, "Terms");
            FlowChrome.Place(box, 0.06f, 0.28f, 0.94f, 0.74f);
            var seal = FlowChrome.SealOf(box);

            var lede = FlowChrome.Label(box, "Lede", "Shadow Games run on contracts. This is yours.",
                15, FlowChrome.Gold, TextAnchor.MiddleCenter, true);
            FlowChrome.Place(lede.rectTransform, 0.06f, 0.84f, 0.94f, 0.96f);

            var body = FlowChrome.Label(box, "Body",
                "• WRLDZ / Project ARGON is an indie prototype.\n" +
                "• No real-money purchases in this build.\n" +
                "• Account data is local to this device.\n" +
                "• Be respectful in real-world AR play.\n" +
                "• Official licensing is not claimed.",
                15, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(body.rectTransform, 0.08f, 0.22f, 0.92f, 0.80f);

            ArcaneButton accept = null;
            accept = FlowChrome.BtnEx(box, "Accept", "I ACCEPT — CONTINUE",
                new Color(0.15f, 0.5f, 0.3f, 1f), () =>
                {
                    LocalAccountStore.TermsAccepted = true;
                    FreeUiKit.PlayConfirm();

                    // Sworn rather than clicked: a slower-than-standard knit plus gold off the top
                    // of the plate. Latched non-interactable so ContinueAfterAuth cannot double-fire.
                    if (!Application.isPlaying || seal == null)
                    {
                        ContinueAfterAuth();
                        return;
                    }

                    if (accept != null) accept.SetInteractable(false);
                    seal.PlayOpen(0.62f);
                    Burst(seal.Rect, 16, UiEmberField.Kind.Ember,
                        EldritchPalette.MillenniumGold, 0.9f, 1f);
                    _beatCo = StartCoroutine(AfterBeat(0.62f, ContinueAfterAuth));
                });
            if (accept != null)
                FlowChrome.Place(accept.Rect, 0.08f, 0.04f, 0.92f, 0.16f);
        }

        // ── Prologue ────────────────────────────────────────────────────────

        /// <summary>
        /// The one true Breached beat. GDD: Crawford Pretoria raises the chest, makes a battery of
        /// the skull, and opens a rift — Tears open across realities. The seal has to visibly FAIL
        /// on screen while that is told, so the panel starts Sealed and escalates with the copy;
        /// keepFracture means it is the SAME crack widening across the four beats rather than four
        /// unrelated breaks. The old copy predated this canon and never named Ptah, Anubis, Akun,
        /// Crawford or the Millennium Items, so the words and the material told different stories.
        /// </summary>
        void BuildPrologue()
        {
            Header("PROLOGUE", "The seal that failed");
            FlowChrome.StepIndicator(Panels, 2, 6);
            var box = FlowChrome.PanelBox(Panels, "Prologue");
            FlowChrome.Place(box, 0.05f, 0.16f, 0.95f, 0.78f);

            var seal = FlowChrome.SealOf(box);
            if (seal != null) seal.SetState(SealState.Sealed);

            // The epigraphy fails across the hole.
            var rail = HieroglyphBorder.Create(box, HieroglyphBorder.Edge.Bottom, 30f,
                EldritchPalette.WithAlpha(EldritchPalette.EldritchViolet, 0.85f), 2.0f);
            if (rail != null) rail.SetIntensity(1.4f);

            var beats = new[]
            {
                "Ptah and Anubis sealed the soul and the skull.\nThe prison became the Shadow Realm.",
                "Akun carried a barrier-stone into Egypt.\nThat stone is the seed of the Millennium Items.",
                "Crawford Pretoria raised the chest, made a battery\nof the skull, and opened a rift.",
                "Tears open across realities.\nThree Kuribohs woke Mr. Referobot —\nand came looking for you."
            };

            var lines = new Text[beats.Length];
            var top = 0.94f;
            for (var i = 0; i < beats.Length; i++)
            {
                lines[i] = FlowChrome.Label(box, "B" + i, beats[i], 15,
                    i == 0 ? FlowChrome.Cream : FlowChrome.Soft, TextAnchor.UpperLeft, false);
                FlowChrome.Place(lines[i].rectTransform, 0.06f, top - 0.175f, 0.94f, top);
                top -= 0.185f;
                if (Application.isPlaying && i > 0)
                    lines[i].color = EldritchPalette.WithAlpha(lines[i].color, 0f);
            }

            var next = FlowChrome.Btn(box, "Next", "CONTINUE", new Color(0.35f, 0.2f, 0.55f, 1f), () =>
            {
                var acc = AppSession.Ensure().Account;
                if (acc != null)
                {
                    acc.EnsureProgress();
                    acc.progress.onboardingPrologueDone = true;
                    ProgressionService.Persist(acc);
                }

                FreeUiKit.PlayConfirm();
                Go(Step.PickKuriboh);
            });
            FlowChrome.Place(next.GetComponent<RectTransform>(), 0.15f, 0.04f, 0.85f, 0.16f);

            if (!Application.isPlaying) return;
            _beatCo = StartCoroutine(PrologueBeats(seal, lines, box));
            // Skippable. The catcher is seated under the step host, so CONTINUE still wins.
            FullScreenTap(() => SkipPrologue(seal, lines));
        }

        IEnumerator PrologueBeats(BarrierStone seal, Text[] lines, RectTransform box)
        {
            var field = Embers();
            yield return new WaitForSecondsRealtime(1.4f);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i] == null) yield break;
                switch (i)
                {
                    case 1: if (seal != null) seal.SetState(SealState.Fractured); break;
                    case 2: if (seal != null) seal.SetState(SealState.Breached); break;
                    // Past the state table and up to the shader's own ceiling.
                    case 3: if (seal != null) seal.SetBreach(1.85f); break;
                }

                if (field != null && box != null)
                    field.BurstFromRect(box, 12, UiEmberField.Kind.Ember,
                        EldritchPalette.AbyssalMagenta, 0f, 0.5f);

                yield return MenuMotion.FadeGraphic(lines[i],
                    EldritchPalette.WithAlpha(lines[i].color, 0f),
                    EldritchPalette.WithAlpha(FlowChrome.Soft, 1f), 0.55f);
                yield return new WaitForSecondsRealtime(1.05f);
            }

            _beatCo = null;
        }

        void SkipPrologue(BarrierStone seal, Text[] lines)
        {
            if (_beatCo != null) { StopCoroutine(_beatCo); _beatCo = null; }
            if (seal != null)
            {
                seal.SetState(SealState.Breached);
                seal.SetBreach(1.85f);
            }

            if (lines == null) return;
            foreach (var t in lines)
                if (t != null) t.color = EldritchPalette.WithAlpha(t.color, 1f);
        }

        // ── Pick Kuriboh team ───────────────────────────────────────────────

        void BuildPickKuriboh()
        {
            Header("CHOOSE YOUR NAVI", "Your Kuriboh is your team and guide");
            FlowChrome.StepIndicator(Panels, 3, 6);

            var box = FlowChrome.PanelBox(Panels, "KuribohPick");
            FlowChrome.Place(box, 0.04f, 0.10f, 0.96f, 0.74f);

            // Three navis = three fragments of the same breaking stone. BarrierStone staggers
            // _CrackCell / _CrackOffset / _Phase per instance, so they are visibly three different
            // fractures for free — and the three tints become three different Millennium inlays.
            _naviCards.Clear();
            BuildNaviCard(box, KuribohTeam.Galactikuriboh, WrldzPresentation.NaviGalacti(),
                0.04f, 0.68f, 0.96f, 0.96f, new Color(0.25f, 0.35f, 0.75f, 1f));
            BuildNaviCard(box, KuribohTeam.Kuribandit, WrldzPresentation.NaviBandit(),
                0.04f, 0.36f, 0.96f, 0.64f, new Color(0.55f, 0.35f, 0.15f, 1f));
            BuildNaviCard(box, KuribohTeam.Junkuriboh, WrldzPresentation.NaviJunk(),
                0.04f, 0.04f, 0.96f, 0.32f, new Color(0.35f, 0.4f, 0.25f, 1f));
        }

        readonly List<BarrierStone> _naviCards = new();

        void BuildNaviCard(Transform parent, KuribohTeam team, Sprite portrait,
            float x0, float y0, float x1, float y1, Color tint)
        {
            // Rebuilt onto ArcaneButton because the old DuelystUi.BtnSecondary() face was a SLICED
            // sprite, and the rect shaders cannot seal one (ELDRITCH_CONTRACT §10.1) — the rebuild
            // is a precondition for this screen joining the language, not a polish pass.
            var ab = ArcaneButton.Create(parent, KuribohTeamInfo.DisplayName(team), null,
                EldritchTier.Secondary, KuribohTeamInfo.Blurb(team), null, 17, false, false);
            if (ab == null) return;
            ab.name = "Navi_" + team;
            FlowChrome.Place(ab.Rect, x0, y0, x1, y1);

            // glyphAlpha 0: the portrait already owns the left margin. CarveFace still swaps the
            // face to sandstone grain, and the seal reads that as the plate's shape — so it MUST
            // run before SealFace.
            EldritchDressing.CarveFace(ab, glyphAlpha: 0f, textX0: 0.34f);
            var stone = EldritchDressing.SealFace(ab, SealState.Fractured);
            if (stone != null)
            {
                stone.FitCrackToAspect = true;
                FlowChrome.Dread(stone, SealState.Fractured);
                // The tint parameter used to be dead (overwritten by bg.color = white). It is now
                // Andrax's barrier light for this fragment — one inlay per Millennium-Item bond.
                stone.SetBarrierColor(tint);
                _naviCards.Add(stone);
            }

            var ring = FlowChrome.MakeImage(ab.transform, "Ring", DuelystUi.OrbRing(), Color.white);
            FlowChrome.Place(ring, 0.03f, 0.1f, 0.30f, 0.9f);
            ring.GetComponent<Image>().preserveAspect = true;

            var port = FlowChrome.MakeImage(ab.transform, "Port",
                portrait != null ? portrait : WrldzPresentation.SpiritEye(), Color.white);
            FlowChrome.Place(port, 0.06f, 0.18f, 0.27f, 0.82f);
            port.GetComponent<Image>().preserveAspect = true;

            if (ab.Title != null) FlowChrome.Place(ab.Title.rectTransform, 0.34f, 0.52f, 0.96f, 0.88f);
            if (ab.Blurb != null) FlowChrome.Place(ab.Blurb.rectTransform, 0.34f, 0.12f, 0.96f, 0.55f);

            if (ab.Button != null)
                ab.Button.onClick.AddListener(() =>
                {
                    FreeUiKit.PlayConfirm();
                    PickNavi(team, stone, tint);
                });
        }

        /// <summary>
        /// The seal choosing its champion: the picked fragment seals toward Intact and re-knits
        /// while the other two go dark. Persistence is untouched — this only delays the navigation.
        /// </summary>
        void PickNavi(KuribohTeam team, BarrierStone chosen, Color tint)
        {
            if (!Application.isPlaying || chosen == null)
            {
                OnKuribohPicked(team);
                return;
            }

            for (var i = 0; i < _naviCards.Count; i++)
            {
                var s = _naviCards[i];
                if (s == null) continue;
                // Latch every card: MenuMotion.Fade restores blocksRaycasts when it lands, so the
                // dimmed cards would briefly be tappable again inside the hold.
                var b = s.GetComponent<Button>();
                if (b != null) b.interactable = false;
                if (s == chosen)
                {
                    s.SetState(SealState.Intact);
                    s.PlayOpen(0.4f);
                    Burst(s.Rect, 26, UiEmberField.Kind.Ember, tint);
                }
                else
                {
                    s.SetBreach(0.2f);
                    var cg = MenuMotion.EnsureGroup(s.gameObject);
                    cg.blocksRaycasts = false;
                    Fx(MenuMotion.Fade(cg, 1f, 0.45f, 0.30f));
                }
            }

            _beatCo = StartCoroutine(AfterBeat(0.35f, () => OnKuribohPicked(team)));
        }

        void OnKuribohPicked(KuribohTeam team)
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null)
            {
                Go(Step.AuthChoice);
                return;
            }

            acc.EnsureProgress();
            acc.progress.Team = team;
            acc.progress.onboardingKuribohChosen = true;
            ProgressionService.Persist(acc);

            if (!GrantStarterKitOrReport(acc)) return;
            AppSession.Ensure().RefreshFromStore();
            Go(Step.GiftSummary);
        }

        /// <summary>
        /// Grant the starter kit; false (with an on-screen note) when it did not land, e.g. the
        /// starter deck file failed to load. Onboarding waits here instead of showing an empty box.
        /// </summary>
        bool GrantStarterKitOrReport(LocalAccountStore.Account acc)
        {
            if (acc == null) return false;
            StarterKitService.GrantIfNeeded(acc);
            acc.EnsureProgress();
            if (acc.progress.onboardingStarterGranted) return true;
            const string msg = "Your starter deck could not be loaded. Restart the game; if it keeps happening, reinstall.";
            Debug.LogError("[WRLDZ] Starter kit not granted (starter deck failed to load).");
            if (_error != null) SetError(msg);
            else FooterNote(msg);
            return false;
        }

        // ── Gift summary ────────────────────────────────────────────────────

        void BuildGiftSummary()
        {
            var acc = AppSession.Ensure().Account;
            acc?.EnsureProgress();
            acc?.EnsureInventory();
            var team = acc != null ? KuribohTeamInfo.DisplayName(acc.progress.Team) : "Kuriboh";
            var cards = acc?.inventory?.TotalStorageUsed() ?? 0;
            var cap = acc?.inventory?.TotalStorageCapacity() ?? 1000;
            var digi = acc?.progress?.digizeni ?? 0;

            Header("THE SPIRITS GRANT YOU…", team + " opens the path");
            FlowChrome.StepIndicator(Panels, 4, 6);
            var box = FlowChrome.PanelBox(Panels, "Gifts");
            FlowChrome.Place(box, 0.06f, 0.16f, 0.94f, 0.78f);

            var navi = FlowChrome.MakeImage(box, "Navi",
                WrldzPresentation.NaviForTeam(acc?.progress?.kuribohTeam ?? 0), Color.white);
            FlowChrome.Place(navi, 0.34f, 0.76f, 0.66f, 0.99f);
            navi.GetComponent<Image>().preserveAspect = true;

            // "Whole" is a seal state, so show it as one. Everything else on this screen is a
            // Fractured fragment; the ERAZ card is the single Intact chip.
            var eraz = FlowChrome.SealPlate(box, "Eraz", SealState.Intact);
            var erazRect = eraz != null ? eraz.Rect : null;
            if (erazRect != null)
            {
                FlowChrome.Place(erazRect, 0.06f, 0.670f, 0.94f, 0.750f);
                var t = FlowChrome.Label(erazRect, "T",
                    "Original ERAZ  ·  whole", 14, FlowChrome.Gold);
                FlowChrome.Place(t.rectTransform, 0.04f, 0.05f, 0.96f, 0.95f);
            }

            // Six granted artifacts, one tile each, instead of one 13 pt paragraph.
            var gifts = new[]
            {
                "BACKPACK", "CARD BOX · 1000", "BINDER",
                "PLAY DECK", "SPIRIT DUELER", "TOME"
            };
            var tiles = new BarrierStone[gifts.Length];
            var atlas = EldritchGlyphs.Atlas();
            for (var i = 0; i < gifts.Length; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var x0 = 0.05f + col * 0.46f;
                var y1 = 0.655f - row * 0.135f;
                var tile = FlowChrome.SealPlate(box, "Gift" + i, SealState.Fractured);
                if (tile == null) continue;
                tiles[i] = tile;
                FlowChrome.Place(tile.Rect, x0, y1 - 0.115f, x0 + 0.44f, y1);
                HoldClosed(tile);

                if (atlas != null && atlas.Length > 0)
                {
                    var glyph = FlowChrome.MakeImage(tile.Rect, "G",
                        atlas[(i * 3 + 2) % atlas.Length],
                        EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.75f));
                    glyph.GetComponent<Image>().preserveAspect = true;
                    FlowChrome.Place(glyph, 0.05f, 0.18f, 0.26f, 0.82f);
                }

                var cap2 = FlowChrome.Label(tile.Rect, "C", gifts[i], 13, FlowChrome.Cream,
                    TextAnchor.MiddleLeft, false);
                FlowChrome.Place(cap2.rectTransform, 0.30f, 0.10f, 0.97f, 0.90f);
            }

            Fx(GiftTilesCo(tiles));

            // Economy rules demote to a quiet well: they are reference, not ceremony.
            var well = FlowChrome.SealPlate(box, "Rules", SealState.Sealed);
            var wellRect = box;
            if (well != null)
            {
                FlowChrome.Place(well.Rect, 0.05f, 0.155f, 0.95f, 0.265f);
                wellRect = well.Rect;
            }

            var body = FlowChrome.Label(wellRect, "B",
                $"{cards}/{cap} cards · {digi} Digizeni\n" +
                "Every item is an Artifact card: SOUL shards (5 = +1 fracture), RELICS (Millennium " +
                "energies), BADGES (ERAZ and profile). Tome pages unlock as you level.",
                12, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(body.rectTransform, 0.04f, 0.04f, 0.96f, 0.96f);

            var next = FlowChrome.Btn(box, "Next", "CONTINUE", new Color(0.15f, 0.45f, 0.35f, 1f), () =>
            {
                if (acc != null)
                {
                    ErazProgress.GrantTutorialBadge(acc);
                    ProgressionService.Persist(acc);
                }

                FreeUiKit.PlayConfirm();
                Go(Step.SpiritMagic);
            });
            FlowChrome.Place(next.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.135f);
        }

        /// <summary>Gift tiles arrive one at a time, each throwing a little Millennium gold.</summary>
        IEnumerator GiftTilesCo(BarrierStone[] tiles)
        {
            yield return null;   // let layout resolve before any rect is sampled
            var field = Embers();
            for (var i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                if (t == null) continue;
                t.PlayOpen(0.34f);
                if (field != null)
                    field.BurstFromRect(t.Rect, 5, UiEmberField.Kind.Ember,
                        EldritchPalette.MillenniumGold);
                yield return new WaitForSecondsRealtime(0.10f);
            }
        }

        /// <summary>
        /// The strongest lore hook in the cascade: the seal shader's _BarrierColor IS Andrax's
        /// barrier light, and Andrax is the twin who buried the skull — so the player is picking
        /// which light holds their own fragment. It used to be the characters ● and ○.
        /// </summary>
        void BuildSpiritMagic()
        {
            Header("SPIRIT DUELER", "The color of your shadow magic");
            FlowChrome.StepIndicator(Panels, 5, 6);
            var box = FlowChrome.PanelBox(Panels, "Magic");
            FlowChrome.Place(box, 0.06f, 0.22f, 0.94f, 0.78f);
            var panel = FlowChrome.SealOf(box);
            var body = FlowChrome.Label(box, "B",
                "The Spirit Dueler is aether — a hologram of shadow magic on your arm.\nPick the light that holds your fragment.",
                15, FlowChrome.Soft, TextAnchor.UpperCenter, false);
            FlowChrome.Place(body.rectTransform, 0.06f, 0.72f, 0.94f, 0.96f);

            var acc = AppSession.Ensure().Account;
            if (acc != null && acc.avatar == null)
                acc.avatar = AvatarAppearance.Default();
            var current = acc?.avatar?.accentHex ?? "#3ECFFF";

            var presets = AvatarCatalog.AccentPresets;
            var chips = new BarrierStone[presets.Length];
            var ticks = new GameObject[presets.Length];
            // Must be a RING, not a fallback white quad — a solid block of the player's colour
            // would cover the stone it is supposed to be lighting.
            var ring = EldritchAssets.RuneRing();
            if (ring == null) ring = DuelystUi.OrbRing();

            for (var i = 0; i < presets.Length; i++)
            {
                var hex = presets[i];
                var idx = i;
                var col = i % 3;
                var row = i / 3;
                var x0 = 0.08f + col * 0.30f;
                var y1 = 0.66f - row * 0.22f;
                var on = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase);
                var tint = AvatarPortraitView.Parse(hex, Color.gray);

                // ~247×236 design px — the best-proportioned seal rect in boot.
                var chip = FlowChrome.SealPlate(box, "C" + i,
                    on ? SealState.Breached : SealState.Fractured, reactive: true, raycast: true);
                if (chip == null) continue;
                chips[i] = chip;
                FlowChrome.Place(chip.Rect, x0, y1 - 0.18f, x0 + 0.26f, y1);
                chip.SetBarrierColor(tint);

                // The barrier light is subtle by design, so the raw swatch colour still shows.
                var swatch = FlowChrome.MakeImage(chip.Rect, "Swatch", ring,
                    EldritchPalette.WithAlpha(tint, 0.95f));
                swatch.GetComponent<Image>().preserveAspect = true;
                FlowChrome.Place(swatch, 0.20f, 0.20f, 0.80f, 0.80f);

                var tick = FlowChrome.MakeImage(chip.Rect, "Tick", null,
                    EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.95f));
                FlowChrome.Place(tick, 0.42f, 0.42f, 0.58f, 0.58f);
                tick.gameObject.SetActive(on);
                ticks[i] = tick.gameObject;

                var btn = chip.gameObject.AddComponent<Button>();
                btn.targetGraphic = chip.Face;
                btn.transition = Selectable.Transition.None;   // the shader ignores vertex RGB
                btn.onClick.AddListener(() => SelectSpiritMagic(idx, hex, tint, chips, ticks, panel));
            }

            var next = FlowChrome.Btn(box, "Next", "CONTINUE", new Color(0.15f, 0.45f, 0.35f, 1f), () =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.TutorialAsk);
            });
            FlowChrome.Place(next.GetComponent<RectTransform>(), 0.15f, 0.04f, 0.85f, 0.14f);
        }

        /// <summary>
        /// Swap the two chips' states in place and retint the panel's own barrier light. The old
        /// handler re-entered Go(Step.SpiritMagic), which destroyed and re-faded every child —
        /// a full-screen flash for a swatch tap.
        /// </summary>
        void SelectSpiritMagic(int index, string hex, Color tint, BarrierStone[] chips,
            GameObject[] ticks, BarrierStone panel)
        {
            var a = AppSession.Ensure().Account;
            if (a != null)
            {
                a.avatar ??= AvatarAppearance.Default();
                a.avatar.accentHex = hex;
                a.avatarColor = hex;
                ProgressionService.Persist(a);
            }

            for (var i = 0; i < chips.Length; i++)
            {
                if (chips[i] == null) continue;
                var on = i == index;
                chips[i].SetState(on ? SealState.Breached : SealState.Fractured);
                if (ticks[i] != null) ticks[i].SetActive(on);
            }

            if (chips[index] != null)
            {
                chips[index].PlayOpen(0.3f);
                Burst(chips[index].Rect, 14, UiEmberField.Kind.Ember, tint);
            }

            if (panel != null) panel.SetBarrierColor(tint);
            FreeUiKit.PlaySelect();
        }

        // ── Tutorial duel ask ───────────────────────────────────────────────

        /// <summary>
        /// GDD: Referobot was woken by the Kuribohs and is invisible to Umbrax's spirits. This is
        /// the one place where the breach is present but cannot reach the player — a real fracture,
        /// held. So the stone is Fractured like every other choice step, but its barrier light is
        /// pushed up and its strain pulled down: it stays notably STEADY where every other fractured
        /// surface flickers, which is the safe-practice read.
        /// </summary>
        void BuildTutorialAsk()
        {
            Header("MR. REFEROBOT", "Training match — win or lose, no stakes");
            // The sequence finally resolves: six of six, every fragment re-sealed.
            FlowChrome.StepIndicator(Panels, 6, 6);

            var ring = FlowChrome.MakeImage(Panels, "Rune", EldritchAssets.RuneRing(),
                EldritchPalette.WithAlpha(EldritchPalette.MillenniumGold, 0.30f));
            ring.GetComponent<Image>().preserveAspect = true;
            FlowChrome.Place(ring, 0.18f, 0.34f, 0.82f, 0.70f);
            if (Application.isPlaying) MenuMotion.PlayOn(ring.gameObject, SpinCo(ring, 26f));

            var box = FlowChrome.PanelBox(Panels, "Tut");
            FlowChrome.Place(box, 0.06f, 0.3f, 0.94f, 0.72f);
            var seal = FlowChrome.SealOf(box);
            SetProp(seal, "_BarrierGlow", 1.10f);
            SetProp(seal, "_Strain", 0.16f);

            var portrait = FlowChrome.MakeImage(box, "Referobot",
                StreamingSprite.ReferobotPortrait(), Color.white);
            portrait.GetComponent<Image>().preserveAspect = true;
            FlowChrome.Place(portrait, 0.30f, 0.56f, 0.70f, 0.96f);

            var body = FlowChrome.Label(box, "B",
                "A practice duel against Mr. Referobot.\n" +
                "He also referees and calls the plays.\n" +
                "No XP. He cannot take cards.",
                16, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(body.rectTransform, 0.06f, 0.36f, 0.94f, 0.56f);

            // Breached: stepping into a Shadow Game IS the breach. SKIP stays Sealed, so the two
            // doors read differently at a glance.
            var duel = FlowChrome.Btn(box, "Duel", "DUEL MR. REFEROBOT", new Color(0.15f, 0.4f, 0.75f, 1f), () =>
            {
                var acc = AppSession.Ensure().Account;
                if (acc != null)
                {
                    acc.EnsureProgress();
                    acc.progress.onboardingTutorialDuelDone = true;
                    acc.progress.onboardingComplete = true;
                    ErazProgress.GrantTutorialBadge(acc.progress);
                    ProgressionService.Persist(acc);
                }

                FreeUiKit.PlayConfirm();
                _step = Step.Done;
                var c = ArDuelMatchConfig.Practice();
                c.Launch = ArDuelLaunchKind.Practice;
                c.EntrySource = AppSession.SceneBoot;
                c.Opponent = ArDuelOpponentKind.AiLocal;
                c.FormatTitle = "Tutorial · Mr. Referobot";
                c.OpponentDisplayName = "Mr. Referobot";
                AppSession.Ensure().StartArDuel(c);
            }, SealState.Breached);
            FlowChrome.Place(duel.GetComponent<RectTransform>(), 0.1f, 0.18f, 0.9f, 0.34f);

            var skip = FlowChrome.Btn(box, "Skip", "SKIP TO DUEL DISK", new Color(0.3f, 0.3f, 0.36f, 1f), () =>
            {
                var acc = AppSession.Ensure().Account;
                if (acc != null)
                {
                    acc.EnsureProgress();
                    acc.progress.onboardingTutorialDuelDone = true;
                    acc.progress.onboardingComplete = true;
                    ErazProgress.GrantTutorialBadge(acc.progress);
                    ProgressionService.Persist(acc);
                }

                FreeUiKit.PlayConfirm();
                Go(Step.WorldLoad);
            }, SealState.Sealed);
            FlowChrome.Place(skip.GetComponent<RectTransform>(), 0.1f, 0.04f, 0.9f, 0.14f);
        }

        static IEnumerator SpinCo(RectTransform rt, float degPerSec)
        {
            var a = 0f;
            while (rt != null)
            {
                a -= degPerSec * Time.unscaledDeltaTime;
                rt.localRotation = Quaternion.Euler(0f, 0f, a);
                yield return null;
            }
        }

        // ── World load → MainMenu (Duel Disk) ───────────────────────────────

        IEnumerator WorldLoadRoutine()
        {
            var acc = AppSession.Ensure().Account;
            // Everything persists before the first yield, so it is never at the mercy of the fade below. Nothing may
            // yield AFTER GoMainMenu(): the scene load destroys this MonoBehaviour and a trailing yield is dropped.
            PersistForWorld(acc);

            // D10: the old loading panel and violet veil are a 0.3 s fade to Void now; the menu then plays its cold open
            var veil = FlowChrome.MakeImage(_root, "Handoff", null, Color.clear);
            FlowChrome.Stretch(veil);
            veil.SetAsLastSibling();
            yield return MenuMotion.FadeGraphic(veil.GetComponent<Image>(), Color.clear,
                EldritchPalette.WithAlpha(EldritchPalette.Void, 1f), 0.30f);

            _step = Step.Done;
            MenuEntry.SetColdOpen();
            // Boot lands on the Duel Disk main menu; WORLD MAP takes the player to the Overworld.
            AppSession.Ensure().GoMainMenu();
        }

        /// <summary>The world load's persistence (the starter kit, onboarding complete, the tutorial badge), synchronous:
        /// the first-time path's WorldLoad and the returning player's handoff both run it (§6 P2).</summary>
        static void PersistForWorld(LocalAccountStore.Account acc)
        {
            if (acc == null) return;
            acc.EnsureProgress();
            if (acc.progress.onboardingKuribohChosen && !acc.progress.onboardingStarterGranted)
                StarterKitService.GrantIfNeeded(acc);
            if (acc.progress.onboardingStarterGranted && !acc.progress.onboardingComplete)
            {
                acc.progress.onboardingComplete = true;
                ErazProgress.GrantTutorialBadge(acc.progress);
                ProgressionService.Persist(acc);
            }
            else
            {
                ErazProgress.GrantTutorialBadgeIfOnboarded(acc.progress);
                if (acc.progress.onboardingTutorialDuelDone || acc.progress.onboardingComplete)
                    ProgressionService.Persist(acc);
            }
        }

        // ── Loading panel helpers ───────────────────────────────────────────

        void BuildLoadingPanel(string title, string subtitle, SealState seal)
        {
            Header(title, subtitle);

            var box = FlowChrome.PanelBox(Panels, "LoadBox", seal);
            FlowChrome.Place(box, 0.1f, 0.38f, 0.9f, 0.62f);
            _loadSeal = FlowChrome.SealOf(box);

            var diskSprite = FreeUiKit.DiskHolo();
            if (diskSprite == null) diskSprite = FreeUiKit.DuelHub();
            var disk = FlowChrome.MakeImage(box, "Disk", diskSprite, Color.white);
            FlowChrome.Place(disk, 0.3f, 0.35f, 0.7f, 0.95f);
            disk.GetComponent<Image>().preserveAspect = true;

            _statusLine = FlowChrome.Label(box, "Status", "…", 15, FlowChrome.Cream, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_statusLine.rectTransform, 0.06f, 0.08f, 0.94f, 0.32f);

            // Millennium inlay running into a carved sandstone channel. Neither rect is sealed:
            // the track is ~10:1, past where a single fracture degrades to a horizontal smear.
            var track = FlowChrome.MakeImage(Panels, "Track", null, new Color(0.16f, 0.14f, 0.20f, 1f));
            FlowChrome.Place(track, 0.12f, 0.3f, 0.88f, 0.335f);
            var grain = EldritchDressing.StoneSprite();
            if (grain != null)
            {
                var ti = track.GetComponent<Image>();
                ti.sprite = grain;
                ti.type = Image.Type.Tiled;
            }

            var fill = FlowChrome.MakeImage(track, "Fill", null, FlowChrome.Gold);
            FlowChrome.Place(fill, 0f, 0f, 0.05f, 1f);
            _loadFill = fill;

            // Invisible marker that rides the fill's leading edge; the ember trail spawns from it.
            _loadLead = FlowChrome.MakeImage(track, "Lead", null, Color.clear);
            FlowChrome.Place(_loadLead, 0.02f, 0f, 0.05f, 1f);
        }

        RectTransform _loadFill;

        /// <summary>
        /// Unscaled time, like every other motion in the project: these stages now run long enough
        /// (0.9 s / 1.25 s) that any timeScale change during boot would read as a hang.
        /// </summary>
        IEnumerator SimulateLoad((string msg, float sec)[] stages, Action<float> onProgress = null)
        {
            float total = 0f;
            foreach (var s in stages) total += s.sec;
            if (total <= 0f) total = 0.01f;
            float done = 0f, lastSpark = 0f;
            var field = Embers();
            foreach (var (msg, sec) in stages)
            {
                if (_statusLine != null) _statusLine.text = msg;
                var t = 0f;
                while (t < sec)
                {
                    var dt = Time.unscaledDeltaTime;
                    t += dt;
                    done += dt;
                    var p = Mathf.Clamp01(done / total);
                    if (_loadFill != null)
                        FlowChrome.Place(_loadFill, 0f, 0f, Mathf.Max(0.05f, p), 1f);
                    if (_loadLead != null)
                    {
                        var x = Mathf.Max(0.05f, p);
                        FlowChrome.Place(_loadLead, Mathf.Max(0f, x - 0.03f), 0f, x, 1f);
                        if (field != null && p - lastSpark >= 0.06f)
                        {
                            lastSpark = p;
                            field.BurstFromRect(_loadLead, 2, UiEmberField.Kind.Ember,
                                EldritchPalette.MillenniumGold);
                        }
                    }

                    onProgress?.Invoke(p);
                    yield return null;
                }
            }

            onProgress?.Invoke(1f);
        }

        // ── Shared chrome ───────────────────────────────────────────────────

        /// <summary>
        /// Identical on all thirteen steps that use it, which meant the chrome never said where in
        /// the arc the player was. The ink now reads the step's seal (gold while it holds, cream at
        /// a fracture, magenta-shot at a breach), as does the glyph strip's brightness.
        /// </summary>
        void Header(string title, string sub)
        {
            var plate = FlowChrome.PanelBox(Panels, "Header");
            FlowChrome.Place(plate, 0.06f, 0.86f, 0.94f, 0.98f);

            var t = FlowChrome.Label(plate, "T", title, 20, FlowChrome.HeaderInk(),
                TextAnchor.MiddleCenter, true);
            FlowChrome.Place(t.rectTransform, 0.05f, 0.45f, 0.95f, 0.95f);
            var s = FlowChrome.Label(plate, "S", sub, 12, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(s.rectTransform, 0.06f, 0.08f, 0.94f, 0.48f);
        }

        void FooterNote(string msg)
        {
            var n = FlowChrome.Label(Panels, "Note", msg, 12, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(n.rectTransform, 0.08f, 0.2f, 0.92f, 0.28f);
        }

        /// <summary>An input field's text, or null when the field is missing or destroyed (Unity null: no ?. on it).</summary>
        static string Txt(InputField f) => f != null ? f.text : null;

        void SetError(string msg)
        {
            if (_error != null) _error.text = msg ?? "";
            Debug.LogWarning("[WRLDZ] Auth: " + msg);
        }
    }
}

