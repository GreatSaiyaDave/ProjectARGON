using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Equipment-free desktop / Editor lab for WRLDZ.
    /// No headset, phone, GPS, or camera required.
    /// Simulates overworld (WASD), AR duel stage (RT EditorSim), and AI duels.
    /// </summary>
    public class DesktopLabApp : MonoBehaviour
    {
        public const string PrefSkipPreDuel = "WRLDZ_SkipPreDuel";
        public const string PrefDesktopLab = "WRLDZ_DesktopLab";

        static DesktopLabApp _instance;
        Text _status;
        Text _modeLine;

        /// <summary>True when lab mode is active this session.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Open the desktop lab hub (destroys previous instance).</summary>
        public static DesktopLabApp Open()
        {
            ArPresentationTarget.SetOverride(null); // EditorSim default
            WrldzLab.Apply();

            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }

            var go = new GameObject("DesktopLabApp");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DesktopLabApp>();
            IsActive = true;
            PlayerPrefs.SetInt(PrefDesktopLab, 1);
            _instance.BuildUi();
            return _instance;
        }

        /// <summary>Boot / Editor entry: ensure lab account then open hub.</summary>
        public static void Launch()
        {
            var session = AppSession.Ensure();
            if (!session.EnsureLabTestAccount(out var err))
            {
                Debug.LogError("[WRLDZ DesktopLab] Lab account failed: " + err);
                // Still open UI with error
            }

            Open();
            if (_instance != null)
                _instance.SetStatus(
                    string.IsNullOrEmpty(err)
                        ? "Desktop Lab ready — no phone, headset, or GPS required."
                        : "Lab account issue: " + err);
        }

        /// <summary>
        /// Opt-in only. Editor no longer auto-skips splash/title boot.
        /// Use CLI <c>-desktopLab</c> / <c>-lab</c>, or PrefSkipBootCascade=1, to jump to lab.
        /// </summary>
        public static bool ShouldAutoLaunch()
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a is "-desktopLab" or "-lab" or "--desktop-lab" or "-skipBoot")
                    return true;
            }

            // Explicit sticky skip (not the same as "lab was opened once")
            if (PlayerPrefs.GetInt(PrefSkipBootCascade, 0) == 1)
                return true;

            return false;
        }

        /// <summary>When 1, Boot scene jumps straight to Desktop Lab (skips splash/title).</summary>
        public const string PrefSkipBootCascade = "WRLDZ_SkipBootCascade";

        public static bool SkipPreDuel
        {
            get => PlayerPrefs.GetInt(PrefSkipPreDuel, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefSkipPreDuel, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        void BuildUi()
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();
            WrldzTheme.ApplyPortrait();

            var canvas = WrldzTheme.Canvas("DesktopLabCanvas", 200);
            canvas.transform.SetParent(transform, false);
            // Lab Necropolis age — cooler cyber-sand Egyptian night
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.LabNecropolis, showAgeCaption: true);
            var root = WrldzTheme.StretchFill(canvas, "Root", new Color(0.04f, 0.06f, 0.10f, 0.15f));
            root.GetComponent<Image>().raycastTarget = true;
            // Dim dunes so gold / cream titles are not lost on the horizon band
            root.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.06f, 0.52f);

            // Header
            var title = FloatingPanel.Title(root, "WRLDZ · DESKTOP LAB", 30);
            FloatingPanel.Place(title.rectTransform, 0.06f, 0.90f, 0.94f, 0.98f);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = DuelystUi.TextCream;
            WrldzType.ApplyOutline(title, heavy: true, buttonContrast: true);

            var sub = FloatingPanel.Body(root,
                "Test without equipment · WASD map · AR stage sim · AI duels",
                16);
            FloatingPanel.Place(sub.rectTransform, 0.06f, 0.85f, 0.94f, 0.90f);
            sub.alignment = TextAnchor.MiddleCenter;
            sub.color = DuelystUi.TextMuted;

            _modeLine = FloatingPanel.Body(root, ModeBanner(), 15);
            FloatingPanel.Place(_modeLine.rectTransform, 0.06f, 0.80f, 0.94f, 0.85f);
            _modeLine.alignment = TextAnchor.MiddleCenter;
            _modeLine.color = DuelystUi.Cyan;

            // Primary actions
            float y = 0.68f;
            float h = 0.075f;
            float gap = 0.012f;

            // Quick AR always runs deploy → shuffle → draw (toggle no longer kills this path).
            // Use INSTANT DUEL when you want hands already drawn.
            Row(root, ref y, h, gap, "QUICK AR DUEL (SIM)",
                "EditorSim dual disks + arena · vs AI · shuffle + draw anim",
                true, () => LaunchQuickDuel(cinematic: true));

            Row(root, ref y, h, gap, "INSTANT DUEL (SKIP CINEMATIC)",
                "Hands already drawn · fastest rules/UI check",
                false, () => LaunchQuickDuel(cinematic: false));

            Row(root, ref y, h, gap, "OVERWORLD (WASD MAP)",
                "Battle City map · Tears · Zone Mode · no GPS needed",
                true, LaunchOverworld);

            Row(root, ref y, h, gap, "SYSTEMS HUB",
                "Deck / Profile / Settings shell",
                false, LaunchHub);

            Row(root, ref y, h, gap, "ENGINE STRESS (10 DUELS)",
                "Headless AI-vs-AI · no AR · console report",
                false, RunStress);

            Row(root, ref y, h, gap, "OCG LAB DUEL (STUB CORE)",
                "Viewer over ocgcore tape · ocg_lab decks · see OCGCORE_LAB.md",
                true, LaunchOcgLab);

            // Options
            var optPanel = FloatingPanel.Create(root, "Opts", goldEdge: false);
            FloatingPanel.Place(optPanel, 0.08f, 0.14f, 0.92f, 0.28f);

            var optTitle = FloatingPanel.Body(optPanel, "OPTIONS", 12);
            FloatingPanel.Place(optTitle.rectTransform, 0.04f, 0.72f, 0.96f, 0.95f);
            optTitle.color = DuelystUi.GoldHot;

            // Tip only — skip is no longer a global sticky toggle (it broke all duels).
            // Use INSTANT DUEL for no-cinematic; QUICK AR for full shuffle/draw.
            var skipNote = FloatingPanel.Body(optPanel,
                "QUICK AR = shuffle + draw  ·  INSTANT = skip cinematic",
                13);
            FloatingPanel.Place(skipNote.rectTransform, 0.06f, 0.35f, 0.94f, 0.65f);
            skipNote.alignment = TextAnchor.MiddleCenter;
            skipNote.color = DuelystUi.Cyan;

            // Clear any leftover sticky skip from older builds so prefs cannot resurrect
            try
            {
                if (PlayerPrefs.GetInt(PrefSkipPreDuel, 0) == 1)
                {
                    PlayerPrefs.SetInt(PrefSkipPreDuel, 0);
                    PlayerPrefs.Save();
                    Debug.Log("[WRLDZ DesktopLab] Cleared sticky SkipPreDuel PlayerPrefs (legacy)");
                }
            }
            catch { /* fine */ }

            var tip = FloatingPanel.Body(optPanel,
                "Controls: WASD = walk map · Space = draw opening hand · Game view portrait 1080×2340",
                11);
            FloatingPanel.Place(tip.rectTransform, 0.04f, 0.05f, 0.96f, 0.30f);
            tip.color = DuelystUi.TextMuted;
            tip.alignment = TextAnchor.MiddleCenter;

            // Status
            _status = FloatingPanel.Body(root, "", 12);
            FloatingPanel.Place(_status.rectTransform, 0.06f, 0.04f, 0.94f, 0.12f);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.color = DuelystUi.TextCream;

            var who = AppSession.Ensure().Account?.displayName ?? "not logged in";
            SetStatus($"Account: {who} · lab_tester / labtest · Ready");
        }

        void RefreshOptions()
        {
            SetStatus("QUICK AR = deploy · shuffle · draw  ·  INSTANT = hands ready");
            if (_modeLine != null) _modeLine.text = ModeBanner();
        }

        static string ModeBanner()
        {
            return
                $"{ArPresentationTarget.StatusLabel()} · " +
                $"platform={Application.platform} · " +
                (Application.isEditor ? "Editor Play Mode" : "Standalone");
        }

        static void Row(Transform root, ref float y, float h, float gap,
            string title, string blurb, bool gold, Action onClick)
        {
            var b = HubChrome.Capsule(root, title, () =>
            {
                FreeUiKit.PlayConfirm();
                onClick?.Invoke();
            }, gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary,
                blurb, centerTitle: false, titleSize: 20);
            FloatingPanel.Place(b.GetComponent<RectTransform>(), 0.08f, y - h, 0.92f, y);
            // Subtitle under button via status only for compact layout
            y -= h + gap;
            Debug.Log($"[WRLDZ DesktopLab] {title} · {blurb}");
        }

        void LaunchQuickDuel(bool cinematic)
        {
            if (!AppSession.Ensure().EnsureLabTestAccount(out var err))
            {
                SetStatus("Login failed: " + err);
                return;
            }

            // Match config is the only skip authority (survives async scene load).
            // Quick AR always forces cinematic; Instant Duel forces skip.
            // Sticky PlayerPrefs no longer affects CreateMenu / Overworld / this Quick path.
            var skipCinematic = !cinematic;

            var c = ArDuelMatchConfig.Practice();
            c.Launch = ArDuelLaunchKind.LabTest;
            c.Opponent = ArDuelOpponentKind.AiLocal;
            c.PreferDigital = true; // no camera
            c.SkipPreDuelCinematic = skipCinematic;
            c.SeparationMeters = ArDuelMatchConfig.DefaultStandM;
            c.FormatTitle = skipCinematic
                ? "Desktop Lab · Instant Duel"
                : "Desktop Lab · AR Sim Duel";
            c.EntrySource = AppSession.SceneBoot;
            c.ClampSeparation();

            ArPresentationTarget.SetOverride(null); // EditorSim
            SetStatus(skipCinematic
                ? "Loading DuelSlice (instant · hands drawn)…"
                : "Loading DuelSlice (EditorSim AR · deploy · shuffle · draw)…");
            Debug.Log(
                $"[WRLDZ DesktopLab] LaunchQuickDuel cinematic={!skipCinematic} · skipFlag={c.SkipPreDuelCinematic}");
            CloseHubVisual();
            AppSession.Ensure().StartArDuel(c);
        }

        void LaunchOverworld()
        {
            if (!AppSession.Ensure().EnsureLabTestAccount(out var err))
            {
                SetStatus("Login failed: " + err);
                return;
            }

            AppSession.Ensure().ClearTestDuelMode();
            SetStatus("Loading Overworld — WASD to walk, tap Tears…");
            CloseHubVisual();
            AppSession.Ensure().GoOverworld();
        }

        void LaunchHub()
        {
            if (!AppSession.Ensure().EnsureLabTestAccount(out var err))
            {
                SetStatus("Login failed: " + err);
                return;
            }

            SetStatus("Loading Systems Hub…");
            CloseHubVisual();
            AppSession.Ensure().GoMainMenu();
        }

        void LaunchOcgLab()
        {
            SetStatus("OCG lab duel (stub core)…");
            CloseHubVisual();
            if (!AppSession.Ensure().StartLabTestDuel("ocg_lab_player.json", "ocg_lab_ai.json"))
                SetStatus("OCG lab start failed — see Console.");
        }

        void RunStress()
        {
            SetStatus("Running 10 AI stress duels… (see Console)");
            try
            {
                var report = DuelEngineStressTests.Run(10);
                SetStatus(report.Ok
                    ? $"Stress PASS · {report.DuelsCompleted}/{report.DuelsPlayed} duels"
                    : $"Stress FAIL · see Console / wrldz_stress_report.txt");
                Debug.Log(report.Summary);
            }
            catch (Exception ex)
            {
                SetStatus("Stress failed: " + ex.Message);
                Debug.LogException(ex);
            }
        }

        void CloseHubVisual()
        {
            // Keep DDOL host for IsActive flag; hide canvas
            var canvas = transform.Find("DesktopLabCanvas");
            if (canvas != null)
                canvas.gameObject.SetActive(false);
        }

        /// <summary>Re-show hub (e.g. after returning from duel to Boot).</summary>
        public void ShowAgain()
        {
            var canvas = transform.Find("DesktopLabCanvas");
            if (canvas != null)
                canvas.gameObject.SetActive(true);
            else
                BuildUi();
            if (_modeLine != null) _modeLine.text = ModeBanner();
        }

        void SetStatus(string msg)
        {
            if (_status != null) _status.text = msg ?? "";
            Debug.Log("[WRLDZ DesktopLab] " + msg);
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                IsActive = false;
            }
        }
    }
}
