using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Opening cascade (short):
    /// Splash → Title → BootLoad → DOB? → Auth → Terms? →
    /// Prologue* → PickKuriboh* → GiftSummary* → TutorialAsk* → WorldLoad → Overworld.
    /// * first-time only. Returning users skip to WorldLoad after auth.
    /// </summary>
    public class BootFlowUI : MonoBehaviour
    {
        public const int MinAge = 13;

        enum Step
        {
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
            TutorialAsk,
            WorldLoad,
            Done
        }

        Step _step;
        RectTransform _root;
        GameObject _panelHost;
        Text _statusLine;
        Coroutine _routine;

        // DOB dropdowns
        Dropdown _dobMonth;
        Dropdown _dobDay;
        Dropdown _dobYear;
        readonly List<int> _yearValues = new();
        // Auth
        InputField _user, _pass, _pass2, _display, _email;
        Text _error;

        public void BuildAndRun()
        {
            FlowChrome.ApplyPortrait();
            AppSession.Ensure();
            var canvas = FlowChrome.CreateCanvas("BootCanvas", 60);
            _root = FlowChrome.RootAtmosphere(canvas);
            _panelHost = new GameObject("Panels", typeof(RectTransform));
            _panelHost.transform.SetParent(_root, false);
            FlowChrome.Stretch(_panelHost.GetComponent<RectTransform>());

            Go(Step.Splash);
        }

        void Go(Step step)
        {
            _step = step;
            if (_routine != null) StopCoroutine(_routine);
            ClearPanels();
            // Each boot step paints a different Egyptian night “age”
            ApplyBootAge(step);
            ApplyBootMusic(step);
            ApplyBootBackdrop(step);
            switch (step)
            {
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
                case Step.TutorialAsk: BuildTutorialAsk(); break;
                case Step.WorldLoad: _routine = StartCoroutine(WorldLoadRoutine()); break;
            }
        }

        /// <summary>Full-bleed Imagine art per boot step (behind panels).</summary>
        void ApplyBootBackdrop(Step step)
        {
            if (_panelHost == null) return;
            Sprite art = step switch
            {
                Step.Splash => ImagineAssets.BgSplash(),
                Step.Credits => ImagineAssets.BgCredits(),
                Step.Title => ImagineAssets.BgTitle(),
                Step.BootLoad => ImagineAssets.BgTitle(),
                Step.Dob => ImagineAssets.BgAuth(),
                Step.AuthChoice => ImagineAssets.BgAuth(),
                Step.Login => ImagineAssets.BgAuth(),
                Step.Create => ImagineAssets.BgAuth(),
                Step.Terms => ImagineAssets.BgAuth(),
                Step.Prologue => ImagineAssets.BgCredits(),
                Step.PickKuriboh => ImagineAssets.BgTitle(),
                Step.GiftSummary => ImagineAssets.BgTitle(),
                Step.TutorialAsk => ImagineAssets.BgMenuVoid(),
                Step.WorldLoad => ImagineAssets.BgHub(),
                _ => ImagineAssets.BgSplash()
            };
            FlowChrome.SetBootBackdrop(_panelHost.transform, art);
        }

        /// <summary>
        /// Scarab Under Stone through splash + title; Alternate Under Stone after entering the world.
        /// Never throws — boot UI must still render if audio fails.
        /// </summary>
        static void ApplyBootMusic(Step step)
        {
            try
            {
                WrldzAudio.Ensure();
                switch (step)
                {
                    case Step.Splash:
                    case Step.Credits:
                    case Step.Title:
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

        void ApplyBootAge(Step step)
        {
            if (_root == null) return;
            var canvas = _root.parent != null ? _root.parent : _root;
            var age = step switch
            {
                Step.Splash => MenuAge.PrimordialNight,
                Step.Credits => MenuAge.PrimordialNight,
                Step.Title => MenuAge.OldKingdom,
                Step.BootLoad => MenuAge.OldKingdom,
                Step.Dob => MenuAge.Courtyard,
                Step.AuthChoice => MenuAge.OldKingdom,
                Step.Login => MenuAge.Courtyard,
                Step.Create => MenuAge.IntermediateKingdom,
                Step.Terms => MenuAge.Courtyard,
                Step.Prologue => MenuAge.ScrollAges,
                Step.PickKuriboh => MenuAge.NewKingdom,
                Step.GiftSummary => MenuAge.NewKingdom,
                Step.TutorialAsk => MenuAge.UmbraxRift,
                Step.WorldLoad => MenuAge.IntermediateKingdom,
                _ => MenuAge.PrimordialNight
            };
            EgyptianAgesAtmosphere.Attach(canvas, age, showAgeCaption: false);
            // Keep root + panels above atmosphere
            if (_root != null) _root.SetAsLastSibling();
        }

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
                StarterKitService.GrantIfNeeded(acc);
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
            for (var i = _panelHost.transform.childCount - 1; i >= 0; i--)
                Destroy(_panelHost.transform.GetChild(i).gameObject);
        }

        // ── Splash ──────────────────────────────────────────────────────────

        void BuildSplash()
        {
            var plate = FlowChrome.PanelBox(_panelHost.transform, "Splash");
            FlowChrome.Place(plate, 0.08f, 0.26f, 0.92f, 0.76f);

            var emblem = ImagineAssets.EmblemSpiritEye()
                         ?? WrldzPresentation.SpiritEye()
                         ?? DuelystUi.OrbRing();
            var ring = FlowChrome.MakeImage(plate, "Ring", DuelystUi.OrbRing() ?? emblem, Color.white);
            FlowChrome.Place(ring, 0.28f, 0.52f, 0.72f, 0.94f);
            ring.GetComponent<Image>().preserveAspect = true;

            var disk = FlowChrome.MakeImage(plate, "Disk", emblem ?? DuelystUi.BtnCircle(), Color.white);
            FlowChrome.Place(disk, 0.34f, 0.58f, 0.66f, 0.88f);
            disk.GetComponent<Image>().preserveAspect = true;

            var standby = FlowChrome.Label(plate, "Std", "DUEL STANDBY", 16, FlowChrome.Cyan);
            FlowChrome.Place(standby.rectTransform, 0.06f, 0.42f, 0.94f, 0.52f);

            var series = FlowChrome.Label(plate, "S", "DUEL MONSTERS", 18, FlowChrome.Gold);
            FlowChrome.Place(series.rectTransform, 0.06f, 0.30f, 0.94f, 0.42f);

            var title = FlowChrome.Label(plate, "T", "WRLDZ", 48, FlowChrome.GoldHot);
            FlowChrome.Place(title.rectTransform, 0.06f, 0.12f, 0.94f, 0.32f);

            var tag = FlowChrome.Label(plate, "Tag", "AR GPS  ·  Shadow Games  ·  Spirit Navis",
                13, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(tag.rectTransform, 0.06f, 0.02f, 0.94f, 0.12f);

            var tap = FlowChrome.Label(_panelHost.transform, "Tap", "TAP TO CONTINUE", 20, FlowChrome.Cyan);
            FlowChrome.Place(tap.rectTransform, 0.1f, 0.10f, 0.9f, 0.18f);

            var catcher = new GameObject("TapCatch", typeof(RectTransform), typeof(Image), typeof(Button));
            catcher.transform.SetParent(_panelHost.transform, false);
            var cImg = catcher.GetComponent<Image>();
            cImg.sprite = UiFoundation.WhiteSprite();
            cImg.color = new Color(0, 0, 0, 0.01f);
            cImg.raycastTarget = true;
            FlowChrome.Stretch(catcher.GetComponent<RectTransform>());
            catcher.GetComponent<Button>().onClick.AddListener(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.Title);
            });

            _routine = StartCoroutine(AutoAdvance(1.0f, Step.Title));
        }

        IEnumerator AutoAdvance(float sec, Step next)
        {
            yield return new WaitForSeconds(sec);
            if (_step == Step.Splash || _step == Step.Credits)
                Go(next);
        }

        void BuildCredits()
        {
            Header("CREDITS", "Project ARGON · Great SaiyaDave");
            FlowChrome.StepIndicator(_panelHost.transform, 0, 6);
            var box = FlowChrome.PanelBox(_panelHost.transform, "Credits");
            FlowChrome.Place(box, 0.08f, 0.24f, 0.92f, 0.78f);

            var body = FlowChrome.Label(box, "B",
                "DUEL MONSTERS: WRLDZ\n\n" +
                "Story & design\nGreat SaiyaDave\n\n" +
                "Indie prototype\nTCG rules · AR GPS\n\n" +
                "Touch to continue",
                16, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(body.rectTransform, 0.08f, 0.1f, 0.92f, 0.9f);

            FullScreenTap(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.Title);
            });
            _routine = StartCoroutine(AutoAdvance(0.8f, Step.Title));
        }

        void BuildTitle()
        {
            var plate = FlowChrome.PanelBox(_panelHost.transform, "TitlePlate");
            FlowChrome.Place(plate, 0.08f, 0.28f, 0.92f, 0.76f);

            var spirit = ImagineAssets.EmblemSpiritEye()
                         ?? WrldzPresentation.SpiritEye()
                         ?? DuelystUi.BtnCircle();
            var ring = FlowChrome.MakeImage(plate, "Ring", DuelystUi.OrbRing() ?? spirit, Color.white);
            FlowChrome.Place(ring, 0.26f, 0.54f, 0.74f, 0.96f);
            ring.GetComponent<Image>().preserveAspect = true;

            var emblem = FlowChrome.MakeImage(plate, "Emblem", spirit, Color.white);
            FlowChrome.Place(emblem, 0.34f, 0.60f, 0.66f, 0.90f);
            emblem.GetComponent<Image>().preserveAspect = true;

            var series = FlowChrome.Label(plate, "Series", "DUEL MONSTERS", 18, FlowChrome.Gold);
            FlowChrome.Place(series.rectTransform, 0.06f, 0.42f, 0.94f, 0.54f);
            var sub = FlowChrome.Label(plate, "S", "WRLDZ", 48, FlowChrome.Cyan);
            FlowChrome.Place(sub.rectTransform, 0.06f, 0.22f, 0.94f, 0.44f);
            var tag = FlowChrome.Label(plate, "Tag", "TOUCH TO BEGIN", 16, FlowChrome.Soft,
                TextAnchor.MiddleCenter, false);
            FlowChrome.Place(tag.rectTransform, 0.1f, 0.10f, 0.9f, 0.20f);
            var build = FlowChrome.Label(plate, "BuildStamp", "BUILD " + WrldzBuild.Stamp, 14,
                FlowChrome.Gold, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(build.rectTransform, 0.1f, 0.02f, 0.9f, 0.10f);
            WrldzBuild.Log();

            FullScreenTap(() =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.BootLoad);
            });
        }

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
            return catcher;
        }

        // ── Boot load (session check) ───────────────────────────────────────

        IEnumerator BootLoadRoutine()
        {
            BuildLoadingPanel("Waking the spirits…", "Checking local session");
            yield return SimulateLoad(new[]
            {
                ("Checking account…", 0.2f)
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
            Header("DATE OF BIRTH", "Required to duel in WRLDZ");

            var box = FlowChrome.PanelBox(_panelHost.transform, "DobBox");
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
            Header("ACCOUNT", "Log in or create a Spirit Dueler ID");

            var box = FlowChrome.PanelBox(_panelHost.transform, "AuthChoice");
            FlowChrome.Place(box, 0.08f, 0.28f, 0.92f, 0.78f);

            var blurb = FlowChrome.Label(box, "B",
                "Your account is stored on this device for now.\nOnline accounts come later.",
                14, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(blurb.rectTransform, 0.06f, 0.82f, 0.94f, 0.96f);

            var create = FlowChrome.Btn(box, "Create", "CREATE ACCOUNT", new Color(0.15f, 0.4f, 0.75f, 1f),
                () => Go(Step.Create));
            FlowChrome.Place(create.GetComponent<RectTransform>(), 0.1f, 0.66f, 0.9f, 0.80f);

            var login = FlowChrome.Btn(box, "Login", "LOG IN", new Color(0.35f, 0.2f, 0.55f, 1f),
                () => Go(Step.Login));
            FlowChrome.Place(login.GetComponent<RectTransform>(), 0.1f, 0.50f, 0.9f, 0.64f);

            // Equipment-free desktop lab
            var desk = FlowChrome.Btn(box, "DeskLab", "DESKTOP LAB (NO EQUIPMENT)",
                new Color(0.15f, 0.55f, 0.65f, 1f), () =>
                {
                    FreeUiKit.PlayConfirm();
                    _step = Step.Done;
                    DesktopLabApp.Launch();
                });
            FlowChrome.Place(desk.GetComponent<RectTransform>(), 0.1f, 0.34f, 0.9f, 0.48f);

            // Lab fast path — skips onboarding, jumps to DuelSlice
            var test = FlowChrome.Btn(box, "TestDuel", "TEST DUEL (LAB)", new Color(0.75f, 0.35f, 0.12f, 1f),
                LaunchLabTestDuel);
            FlowChrome.Place(test.GetComponent<RectTransform>(), 0.1f, 0.18f, 0.9f, 0.32f);

            var guest = FlowChrome.Label(box, "G",
                "DESKTOP LAB: full hub · map · AR sim · no phone/headset.\n" +
                "TEST DUEL: lab_tester · Map → Boot.\nDB: " + LocalAccountStore.DatabasePath,
                11, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(guest.rectTransform, 0.04f, 0.01f, 0.96f, 0.16f);
        }

        // ── Login ───────────────────────────────────────────────────────────

        void BuildLogin()
        {
            Header("LOG IN", "Welcome back, Duelist");

            var box = FlowChrome.PanelBox(_panelHost.transform, "LoginBox");
            FlowChrome.Place(box, 0.06f, 0.32f, 0.94f, 0.74f);

            _user = FlowChrome.Field(box, "User", "Username");
            FlowChrome.Place(_user.GetComponent<RectTransform>(), 0.08f, 0.68f, 0.92f, 0.88f);
            _pass = FlowChrome.Field(box, "Pass", "Password", password: true);
            FlowChrome.Place(_pass.GetComponent<RectTransform>(), 0.08f, 0.44f, 0.92f, 0.64f);

            _error = FlowChrome.Label(box, "Err", "", 14, FlowChrome.Danger, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_error.rectTransform, 0.06f, 0.28f, 0.94f, 0.42f);

            var goBtn = FlowChrome.Btn(box, "Go", "LOG IN", new Color(0.15f, 0.45f, 0.35f, 1f), OnLogin);
            FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.12f, 0.08f, 0.88f, 0.24f);

            var back = FlowChrome.Btn(_panelHost.transform, "Back", "BACK", new Color(0.25f, 0.25f, 0.32f, 1f),
                () => Go(Step.AuthChoice));
            FlowChrome.Place(back.GetComponent<RectTransform>(), 0.25f, 0.18f, 0.75f, 0.26f);
        }

        void OnLogin()
        {
            if (!LocalAccountStore.TryLogin(_user?.text, _pass?.text, out var acc, out var err))
            {
                SetError(err ?? "Login failed.");
                return;
            }

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
            Header("CREATE ACCOUNT", "Forge your Spirit Dueler ID");

            var box = FlowChrome.PanelBox(_panelHost.transform, "CreateBox");
            FlowChrome.Place(box, 0.05f, 0.34f, 0.95f, 0.84f);

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

            var goBtn = FlowChrome.Btn(box, "Go", "CREATE", new Color(0.15f, 0.4f, 0.75f, 1f), OnCreate);
            FlowChrome.Place(goBtn.GetComponent<RectTransform>(), 0.12f, 0.02f, 0.88f, 0.12f);

            // Fast lab path under the form (same as AuthChoice)
            var test = FlowChrome.Btn(_panelHost.transform, "TestDuel", "TEST DUEL (LAB)",
                new Color(0.75f, 0.35f, 0.12f, 1f), LaunchLabTestDuel);
            FlowChrome.Place(test.GetComponent<RectTransform>(), 0.12f, 0.18f, 0.88f, 0.28f);

            var back = FlowChrome.Btn(_panelHost.transform, "Back", "BACK", new Color(0.25f, 0.25f, 0.32f, 1f),
                () => Go(Step.AuthChoice));
            FlowChrome.Place(back.GetComponent<RectTransform>(), 0.25f, 0.08f, 0.75f, 0.16f);
        }

        /// <summary>
        /// Skip DOB/terms/prologue/Kuriboh — log in lab_tester and open DuelSlice immediately.
        /// Map / leave returns to Boot for another quick run.
        /// </summary>
        void LaunchLabTestDuel()
        {
            FreeUiKit.PlayConfirm();
            SetError("");
            if (!AppSession.Ensure().StartLabTestDuel())
            {
                SetError("Lab test duel failed — see console (lab_tester / labtest).");
                Debug.LogError("[WRLDZ] StartLabTestDuel failed from Boot UI.");
            }
            else
            {
                _step = Step.Done;
            }
        }

        void OnCreate()
        {
            var user = _user?.text?.Trim() ?? "";
            var pass = _pass?.text ?? "";
            var pass2 = _pass2?.text ?? "";
            var display = _display?.text?.Trim() ?? "";
            var email = _email?.text?.Trim() ?? "";

            if (string.IsNullOrEmpty(user))
            {
                SetError("Enter a username (3+ characters).");
                return;
            }

            if (pass != pass2)
            {
                SetError("Passwords do not match.");
                return;
            }

            if (!LocalAccountStore.TryCreate(user, pass, display, email, out var acc, out var err))
            {
                SetError(err ?? "Could not create account.");
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
            Go(Step.Terms);
        }

        // ── Terms ───────────────────────────────────────────────────────────

        void BuildTerms()
        {
            Header("TERMS", "Indie / fan-phase agreement");

            var box = FlowChrome.PanelBox(_panelHost.transform, "Terms");
            FlowChrome.Place(box, 0.06f, 0.28f, 0.94f, 0.74f);

            var body = FlowChrome.Label(box, "Body",
                "• WRLDZ / Project ARGON is an indie prototype.\n" +
                "• No real-money purchases in this build.\n" +
                "• Account data is local to this device.\n" +
                "• Be respectful in real-world AR play.\n" +
                "• Official licensing is not claimed.",
                15, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(body.rectTransform, 0.08f, 0.22f, 0.92f, 0.92f);

            var accept = FlowChrome.Btn(box, "Accept", "I ACCEPT — CONTINUE",
                new Color(0.15f, 0.5f, 0.3f, 1f), () =>
                {
                    LocalAccountStore.TermsAccepted = true;
                    FreeUiKit.PlayConfirm();
                    ContinueAfterAuth();
                });
            FlowChrome.Place(accept.GetComponent<RectTransform>(), 0.08f, 0.04f, 0.92f, 0.16f);
        }

        // ── Prologue ────────────────────────────────────────────────────────

        void BuildPrologue()
        {
            Header("PROLOGUE", "When the gods left, the Tears remained…");
            FlowChrome.StepIndicator(_panelHost.transform, 2, 6);
            var box = FlowChrome.PanelBox(_panelHost.transform, "Prologue");
            FlowChrome.Place(box, 0.05f, 0.16f, 0.95f, 0.78f);
            var body = FlowChrome.Label(box, "B",
                "Umbrax stirs in his prison of shadow.\n" +
                "Rifts claw open across Earth —\n" +
                "duel monster spirits pour through,\n" +
                "and only a sorcerer can stand against them.\n\n" +
                "Three Kuriboh spirits seek champions.\n" +
                "They will grant you a Tome of spells…\n" +
                "and the right to walk in Shadow Games.",
                15, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(body.rectTransform, 0.06f, 0.2f, 0.94f, 0.94f);

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
        }

        // ── Pick Kuriboh team ───────────────────────────────────────────────

        void BuildPickKuriboh()
        {
            Header("CHOOSE YOUR NAVI", "Your Kuriboh is your team and guide");
            FlowChrome.StepIndicator(_panelHost.transform, 3, 6);

            var box = FlowChrome.PanelBox(_panelHost.transform, "KuribohPick");
            FlowChrome.Place(box, 0.04f, 0.10f, 0.96f, 0.74f);

            BuildNaviCard(box, KuribohTeam.Galactikuriboh, WrldzPresentation.NaviGalacti(),
                0.04f, 0.68f, 0.96f, 0.96f, new Color(0.25f, 0.35f, 0.75f, 1f));
            BuildNaviCard(box, KuribohTeam.Kuribandit, WrldzPresentation.NaviBandit(),
                0.04f, 0.36f, 0.96f, 0.64f, new Color(0.55f, 0.35f, 0.15f, 1f));
            BuildNaviCard(box, KuribohTeam.Junkuriboh, WrldzPresentation.NaviJunk(),
                0.04f, 0.04f, 0.96f, 0.32f, new Color(0.35f, 0.4f, 0.25f, 1f));
        }

        void BuildNaviCard(Transform parent, KuribohTeam team, Sprite portrait,
            float x0, float y0, float x1, float y1, Color tint)
        {
            var go = new GameObject("Navi_" + team, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            FlowChrome.Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var bg = go.GetComponent<Image>();
            bg.sprite = DuelystUi.BtnSecondary();
            bg.type = bg.sprite != null && bg.sprite.border.sqrMagnitude > 0
                ? Image.Type.Sliced
                : Image.Type.Simple;
            bg.color = Color.white;

            var ring = FlowChrome.MakeImage(go.transform, "Ring", DuelystUi.OrbRing(), Color.white);
            FlowChrome.Place(ring, 0.03f, 0.1f, 0.30f, 0.9f);
            ring.GetComponent<Image>().preserveAspect = true;

            var port = FlowChrome.MakeImage(go.transform, "Port",
                portrait ?? WrldzPresentation.SpiritEye(), Color.white);
            FlowChrome.Place(port, 0.06f, 0.18f, 0.27f, 0.82f);
            port.GetComponent<Image>().preserveAspect = true;

            var name = FlowChrome.Label(go.transform, "N", KuribohTeamInfo.DisplayName(team),
                17, FlowChrome.Cream, TextAnchor.MiddleLeft, true);
            FlowChrome.Place(name.rectTransform, 0.34f, 0.52f, 0.96f, 0.88f);

            var blurb = FlowChrome.Label(go.transform, "B", KuribohTeamInfo.Blurb(team),
                12, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(blurb.rectTransform, 0.34f, 0.12f, 0.96f, 0.55f);

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlayConfirm();
                OnKuribohPicked(team);
            });
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

            StarterKitService.GrantIfNeeded(acc);
            AppSession.Ensure().RefreshFromStore();
            Go(Step.GiftSummary);
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
            FlowChrome.StepIndicator(_panelHost.transform, 4, 6);
            var box = FlowChrome.PanelBox(_panelHost.transform, "Gifts");
            FlowChrome.Place(box, 0.06f, 0.16f, 0.94f, 0.78f);

            var navi = FlowChrome.MakeImage(box, "Navi",
                WrldzPresentation.NaviForTeam(acc?.progress?.kuribohTeam ?? 0), Color.white);
            FlowChrome.Place(navi, 0.32f, 0.72f, 0.68f, 0.98f);
            navi.GetComponent<Image>().preserveAspect = true;
            var body = FlowChrome.Label(box, "B",
                "• Backpack (travel kit)\n" +
                "• Home Card Box (1000 bulk storage)\n" +
                "• Starter Binder (5 pages · 18 cards/page · max 20p/360)\n" +
                "• Play Deck Box + Starter Deck\n" +
                "• Spirit Dueler Disk\n" +
                "• Mysterious Chest → your TOME\n" +
                $"• {cards}/{cap} cards in your home box\n" +
                $"• {digi} Digizeni\n\n" +
                "Buy more boxes (100/500/1000) & binder pages later.\n" +
                "Tome pages unlock as you level (RAID only).",
                14, FlowChrome.Soft, TextAnchor.UpperLeft, false);
            FlowChrome.Place(body.rectTransform, 0.06f, 0.14f, 0.94f, 0.70f);

            var next = FlowChrome.Btn(box, "Next", "CONTINUE", new Color(0.15f, 0.45f, 0.35f, 1f), () =>
            {
                FreeUiKit.PlayConfirm();
                Go(Step.TutorialAsk);
            });
            FlowChrome.Place(next.GetComponent<RectTransform>(), 0.15f, 0.04f, 0.85f, 0.14f);
        }

        // ── Tutorial duel ask ───────────────────────────────────────────────

        void BuildTutorialAsk()
        {
            Header("MR. REFEROBOT", "Training match — win or lose, no stakes");
            var box = FlowChrome.PanelBox(_panelHost.transform, "Tut");
            FlowChrome.Place(box, 0.06f, 0.3f, 0.94f, 0.72f);
            var body = FlowChrome.Label(box, "B",
                "Practice with your starter deck.\n" +
                "Mr. Referobot will referee and call the plays.\n\n" +
                "This duel awards no XP and cannot take cards.",
                16, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(body.rectTransform, 0.06f, 0.4f, 0.94f, 0.9f);

            var duel = FlowChrome.Btn(box, "Duel", "PLAYER VS AI", new Color(0.15f, 0.4f, 0.75f, 1f), () =>
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
                // Onboarding practice: still Player vs AI on AR stage
                var c = ArDuelMatchConfig.Practice();
                c.Launch = ArDuelLaunchKind.Practice;
                c.EntrySource = AppSession.SceneBoot;
                c.Opponent = ArDuelOpponentKind.AiLocal;
                c.FormatTitle = "Player vs AI · Tutorial";
                AppSession.Ensure().StartArDuel(c);
            });
            FlowChrome.Place(duel.GetComponent<RectTransform>(), 0.1f, 0.18f, 0.9f, 0.34f);

            var skip = FlowChrome.Btn(box, "Skip", "SKIP TO OVERWORLD", new Color(0.3f, 0.3f, 0.36f, 1f), () =>
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
            });
            FlowChrome.Place(skip.GetComponent<RectTransform>(), 0.1f, 0.04f, 0.9f, 0.14f);
        }

        // ── World load → Overworld ──────────────────────────────────────────

        IEnumerator WorldLoadRoutine()
        {
            var acc = AppSession.Ensure().Account;
            if (acc != null)
            {
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

            var name = acc?.displayName ?? "Duelist";
            BuildLoadingPanel("Opening the overworld…", "Welcome, " + name);
            yield return SimulateLoad(new[]
            {
                ("Opening the map…", 0.25f)
            });

            _step = Step.Done;
            AppSession.Ensure().GoOverworld();
        }

        // ── Loading panel helpers ───────────────────────────────────────────

        void BuildLoadingPanel(string title, string subtitle)
        {
            Header(title, subtitle);

            var box = FlowChrome.PanelBox(_panelHost.transform, "LoadBox");
            FlowChrome.Place(box, 0.1f, 0.38f, 0.9f, 0.62f);

            var disk = FlowChrome.MakeImage(box, "Disk", FreeUiKit.DiskHolo() ?? FreeUiKit.DuelHub(), Color.white);
            FlowChrome.Place(disk, 0.3f, 0.35f, 0.7f, 0.95f);
            disk.GetComponent<Image>().preserveAspect = true;

            _statusLine = FlowChrome.Label(box, "Status", "…", 15, FlowChrome.Cyan, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(_statusLine.rectTransform, 0.06f, 0.08f, 0.94f, 0.32f);

            // Progress bar track
            var track = FlowChrome.MakeImage(_panelHost.transform, "Track", null, new Color(0.1f, 0.12f, 0.2f, 1f));
            FlowChrome.Place(track, 0.12f, 0.3f, 0.88f, 0.335f);
            var fill = FlowChrome.MakeImage(track, "Fill", null, FlowChrome.Gold);
            FlowChrome.Place(fill, 0f, 0f, 0.05f, 1f);
            _loadFill = fill;
        }

        RectTransform _loadFill;

        IEnumerator SimulateLoad((string msg, float sec)[] stages)
        {
            float total = 0f;
            foreach (var s in stages) total += s.sec;
            float done = 0f;
            foreach (var (msg, sec) in stages)
            {
                if (_statusLine != null) _statusLine.text = msg;
                var t = 0f;
                while (t < sec)
                {
                    t += Time.deltaTime;
                    done += Time.deltaTime;
                    var p = Mathf.Clamp01(done / total);
                    if (_loadFill != null)
                        FlowChrome.Place(_loadFill, 0f, 0f, Mathf.Max(0.05f, p), 1f);
                    yield return null;
                }
            }
        }

        // ── Shared chrome ───────────────────────────────────────────────────

        void Header(string title, string sub)
        {
            var plate = FlowChrome.PanelBox(_panelHost.transform, "Header");
            FlowChrome.Place(plate, 0.06f, 0.86f, 0.94f, 0.98f);

            var t = FlowChrome.Label(plate, "T", title, 20, FlowChrome.Cyan, TextAnchor.MiddleCenter, true);
            FlowChrome.Place(t.rectTransform, 0.05f, 0.45f, 0.95f, 0.95f);
            var s = FlowChrome.Label(plate, "S", sub, 12, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(s.rectTransform, 0.06f, 0.08f, 0.94f, 0.48f);
        }

        void FooterNote(string msg)
        {
            var n = FlowChrome.Label(_panelHost.transform, "Note", msg, 12, FlowChrome.Soft, TextAnchor.MiddleCenter, false);
            FlowChrome.Place(n.rectTransform, 0.08f, 0.2f, 0.92f, 0.28f);
        }

        void SetError(string msg)
        {
            if (_error != null) _error.text = msg ?? "";
            Debug.LogWarning("[WRLDZ] Auth: " + msg);
        }
    }
}

