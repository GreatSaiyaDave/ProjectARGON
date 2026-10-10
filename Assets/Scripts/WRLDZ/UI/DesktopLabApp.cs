using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI.Eldritch;
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

        /// <summary>
        /// The lab (and its admin test account) exists only in the Editor and Development builds
        /// (owner decision 5). Release builds refuse every entry point.
        /// </summary>
        public static bool Available => WrldzLab.LabDoorsEnabled;

        /// <summary>Open the desktop lab hub (destroys previous instance). Null in release builds.</summary>
        public static DesktopLabApp Open()
        {
            if (!Available)
            {
                Debug.LogWarning("[WRLDZ DesktopLab] Desktop Lab is Editor / Development builds only.");
                PlayerPrefs.SetInt(PrefDesktopLab, 0);
                return null;
            }

            ArPresentationTarget.SetOverride(null); // EditorSim default
            WrldzLab.Apply();

            if (_instance != null)
            {
                // Edit-mode safe: MenuSmokeTest calls Open() with isPlaying false, where
                // UnityEngine.Object.Destroy silently does nothing and the old hub would linger.
                WRLDZ.Presentation.ArInteraction.ArObjectUtil.Destroy(_instance.gameObject);
                _instance = null;
            }

            var go = new GameObject("DesktopLabApp");
            // DontDestroyOnLoad throws outside play mode, which aborted every edit-mode
            // caller (MenuSmokeTest, EldritchMenuPreview) before the hub could build.
            if (Application.isPlaying)
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
            if (!Available)
            {
                Debug.LogWarning("[WRLDZ DesktopLab] Launch refused: Editor / Development builds only.");
                PlayerPrefs.SetInt(PrefDesktopLab, 0);
                return;
            }

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
        /// <para>
        /// The PlayerPrefs route is ONE-SHOT: it is consumed here. It used to be sticky, and both
        /// <c>WRLDZ/Lab/Open Desktop Lab App</c> and the POV recorder set it without ever clearing
        /// it — so after either had run once, every later Play skipped the Boot cascade
        /// (splash → title → main menu) and dropped straight into the lab. Consuming the pref
        /// restores the normal boot flow on the next run while the Editor menu still works.
        /// </para>
        /// Called once per boot from <c>BootFlowBootstrap.Start</c>.
        /// </summary>
        public static bool ShouldAutoLaunch()
        {
            // Release builds: never skip Boot for a lab that will refuse to open (blank screen).
            if (!Available) return false;
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a is "-desktopLab" or "-lab" or "--desktop-lab" or "-skipBoot")
                {
                    Debug.Log("[WRLDZ Eldritch] Desktop Lab auto-launch via CLI flag " + a);
                    return true;
                }
            }

            if (PlayerPrefs.GetInt(PrefSkipBootCascade, 0) == 1)
            {
                try
                {
                    PlayerPrefs.SetInt(PrefSkipBootCascade, 0);
                    PlayerPrefs.Save();
                }
                catch { /* PlayerPrefs unavailable (headless import) — still honour the request */ }

                Debug.LogWarning(
                    "[WRLDZ Eldritch] Boot cascade SKIPPED this run — leftover " +
                    PrefSkipBootCascade + "=1 (set by WRLDZ/Lab/Open Desktop Lab App or the POV " +
                    "recorder, and never cleared by older builds). The pref has now been consumed: " +
                    "the next Play runs splash → title → main menu normally.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// One-shot. When 1, the next Boot jumps straight to Desktop Lab (skips splash/title)
        /// and <see cref="ShouldAutoLaunch"/> clears it again.
        /// </summary>
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
            // contract note: MenuAge.LabNecropolis paints a cyan/teal night (its "gold" token IS
            // cyan), which is exactly the old chrome this overhaul replaces. NewKingdom is the gold
            // age, and only its star motes survive — the rest are opaque plates and tofu glyphs.
            var atmosphere = EgyptianAgesAtmosphere.Attach(canvas, MenuAge.NewKingdom, showAgeCaption: false);
            if (atmosphere != null) EldritchDressing.KeepOnlyStars(atmosphere.transform);

            // Obsidian ground, not teal: the lab is a necropolis floor, not a terminal.
            var root = WrldzTheme.StretchFill(canvas, "Root",
                EldritchPalette.WithAlpha(EldritchPalette.Abyss, 0.94f));
            root.GetComponent<Image>().raycastTarget = true;
            EldritchDressing.ScreenTentacles(root, 0.22f);

            // Ambient embers drifting over the necropolis. Play mode only — the smoke test builds
            // this screen with isPlaying false and a static mote pool would just be dead objects.
            if (Application.isPlaying)
                UiEmberField.Attach(canvas, poolSize: 32, ambient: true);

            // ── Title cartouche ─────────────────────────────────────
            // Gold-on-obsidian slate + hieroglyph cornices top and bottom.
            var cartouche = SolidVisionSlate.Create(root, "TitleCartouche", EldritchTier.Primary,
                hoverGlow: false, raycast: false);
            cartouche?.Quiet();   // wide chrome plate: gold frame, no plasma
            RectTransform cartRt = null;
            if (cartouche != null)
            {
                cartRt = cartouche.Rect;
                FloatingPanel.Place(cartRt, 0.05f, 0.845f, 0.95f, 0.985f);
                var top = HieroglyphBorder.Create(cartRt, HieroglyphBorder.Edge.Top, 20f,
                    EldritchPalette.MillenniumGold, 2.4f);
                if (top != null) top.SetIntensity(0.8f);
                var bottom = HieroglyphBorder.Create(cartRt, HieroglyphBorder.Edge.Bottom, 20f,
                    EldritchPalette.MillenniumGold, 2.4f);
                if (bottom != null) bottom.SetIntensity(0.8f);
            }

            Transform head = cartRt != null ? cartRt : root;

            var title = FloatingPanel.Title(head, "WRLDZ · DESKTOP LAB", 30);
            FloatingPanel.Place(title.rectTransform,
                cartRt != null ? 0.04f : 0.06f, cartRt != null ? 0.48f : 0.90f,
                cartRt != null ? 0.96f : 0.94f, cartRt != null ? 0.92f : 0.98f);
            title.alignment = TextAnchor.MiddleCenter;
            title.color = EldritchPalette.GoldHot;
            WrldzType.ApplyOutline(title, heavy: true, buttonContrast: true);

            var sub = FloatingPanel.Body(head,
                "Test without equipment · WASD map · AR stage sim · AI duels",
                16);
            FloatingPanel.Place(sub.rectTransform,
                cartRt != null ? 0.04f : 0.06f, cartRt != null ? 0.10f : 0.85f,
                cartRt != null ? 0.96f : 0.94f, cartRt != null ? 0.46f : 0.90f);
            sub.alignment = TextAnchor.MiddleCenter;
            sub.color = EldritchPalette.TextMuted;
            WrldzBuild.Log();

            _modeLine = FloatingPanel.Body(root, ModeBanner(), 15);
            FloatingPanel.Place(_modeLine.rectTransform, 0.06f, 0.795f, 0.94f, 0.838f);
            _modeLine.alignment = TextAnchor.MiddleCenter;
            _modeLine.color = EldritchPalette.SolidVisionCyan;
            WrldzType.ApplyOutline(_modeLine, heavy: true, buttonContrast: true);

            // Primary actions
            // 8 rows × (h + gap) must stay above the OPTIONS panel top (0.28).
            float y = 0.78f;
            float h = 0.05f;
            float gap = 0.008f;

            // Quick AR always runs deploy → shuffle → draw (toggle no longer kills this path).
            // Use INSTANT DUEL when you want hands already drawn.
            Row(root, ref y, h, gap, "QUICK AR DUEL (SIM)",
                "EditorSim dual disks + arena · vs AI · shuffle + draw anim",
                true, () => LaunchQuickDuel(cinematic: true));

            Row(root, ref y, h, gap, "INSTANT DUEL (SKIP CINEMATIC)",
                "Hands already drawn · fastest rules/UI check",
                false, () => LaunchQuickDuel(cinematic: false));

            Row(root, ref y, h, gap, "VS OPPONENT",
                "Pick Yugi, Kaiba, Joey, street lists · 8000 LP TCG",
                true, LaunchOpponents);

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
                "Viewer over ocgcore tape · ocg_lab decks",
                true, LaunchOcgLab);

            Row(root, ref y, h, gap, "STREAMING FILES SELF-TEST",
                "List Decks/ · parse card DB · APK zip on Android",
                false, RunStreamingSelfTest);

            // ── OPTIONS · hieroglyph panel ──────────────────────────
            // Cartouche title + rune ring; Open() snaps to the final state in edit mode.
            var opts = HieroglyphPanel.Create(root, "Opts", EldritchTier.Secondary,
                title: "OPTIONS", ring: true, embersOnOpen: true);
            Transform optPanel = root;
            if (opts != null)
            {
                FloatingPanel.Place(opts.Root, 0.08f, 0.14f, 0.92f, 0.28f);
                optPanel = opts.Body != null ? opts.Body : opts.Root;
                opts.Open();
            }
            else
            {
                optPanel = HubChrome.Sheet(root, "Opts", 0.08f, 0.14f, 0.92f, 0.28f);
                var optSlate = optPanel != null ? optPanel.GetComponent<SolidVisionSlate>() : null;
                optSlate?.Quiet();
                var optTitle = FloatingPanel.Body(optPanel, "OPTIONS", 12);
                FloatingPanel.Place(optTitle.rectTransform, 0.04f, 0.72f, 0.96f, 0.95f);
                optTitle.color = EldritchPalette.GoldHot;
            }

            // Tip only — skip is no longer a global sticky toggle (it broke all duels).
            // Use INSTANT DUEL for no-cinematic; QUICK AR for full shuffle/draw.
            var skipNote = FloatingPanel.Body(optPanel,
                "QUICK AR = shuffle + draw  ·  INSTANT = skip cinematic",
                13);
            FloatingPanel.Place(skipNote.rectTransform, 0.04f, 0.46f, 0.96f, 0.98f);
            skipNote.alignment = TextAnchor.MiddleCenter;
            skipNote.color = EldritchPalette.SolidVisionCyan;
            WrldzType.ApplyOutline(skipNote, heavy: true, buttonContrast: true);

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
            FloatingPanel.Place(tip.rectTransform, 0.04f, 0.02f, 0.96f, 0.44f);
            tip.color = EldritchPalette.TextMuted;
            tip.alignment = TextAnchor.MiddleCenter;

            // Status — cream on obsidian, on its own thin slate so the dunes never eat it
            var statusPlate = SolidVisionSlate.Create(root, "StatusPlate", EldritchTier.Tertiary,
                hoverGlow: false, raycast: false);
            statusPlate?.Quiet();
            Transform statusHost = root;
            if (statusPlate != null)
            {
                FloatingPanel.Place(statusPlate.Rect, 0.06f, 0.035f, 0.94f, 0.115f);
                statusHost = statusPlate.Rect;
            }

            _status = FloatingPanel.Body(statusHost, "", 12);
            FloatingPanel.Place(_status.rectTransform,
                statusPlate != null ? 0.03f : 0.06f, statusPlate != null ? 0.05f : 0.04f,
                statusPlate != null ? 0.97f : 0.94f, statusPlate != null ? 0.95f : 0.12f);
            _status.alignment = TextAnchor.MiddleCenter;
            _status.color = EldritchPalette.TextCream;
            WrldzType.ApplyOutline(_status, heavy: true, buttonContrast: true);

            var who = AppSession.Ensure().Account?.displayName ?? "not logged in";
            SetStatus($"Account: {who} · lab account · Ready");
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

        /// <summary>
        /// One lab action row. Label, blurb and action are unchanged — this is a dev tool and its
        /// behaviour must not drift; only the chrome moved to <see cref="ArcaneButton"/>.
        /// <paramref name="gold"/> now picks the Primary (gold-on-obsidian, hieroglyph ring) tier
        /// over Secondary (cyan-on-lapis).
        /// </summary>
        static void Row(Transform root, ref float y, float h, float gap,
            string title, string blurb, bool gold, Action onClick)
        {
            Action fire = () =>
            {
                FreeUiKit.PlayConfirm();
                onClick?.Invoke();
            };

            var tier = gold ? EldritchTier.Primary : EldritchTier.Secondary;
            // hieroglyphBorder:false on purpose — Edge.Ring puts cells on the left and right edges,
            // straight through these long labels. Every row instead gets ONE struck glyph in a
            // reserved left margin, so gold and non-gold rows read as the same carved family.
            var ab = ArcaneButton.Create(root, title, fire, tier, blurb,
                icon: null, titleSize: 20, centerTitle: false, hieroglyphBorder: false);
            EldritchDressing.CarveFace(ab, glyphAlpha: gold ? 0.9f : 0.6f,
                glyphX0: 0.018f, glyphX1: 0.075f, textX0: 0.10f);

            RectTransform rt;
            if (ab != null)
            {
                rt = ab.Rect;
            }
            else
            {
                // Eldritch shaders unavailable — fall back to the old capsule so the lab still runs.
                var b = HubChrome.Capsule(root, title, fire,
                    gold ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary,
                    blurb, centerTitle: false, titleSize: 20);
                rt = b.GetComponent<RectTransform>();
            }

            FloatingPanel.Place(rt, 0.08f, y - h, 0.92f, y);
            y -= h + gap;
            Debug.Log($"[WRLDZ Eldritch] DesktopLab row · {title} · {blurb}");
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

        void LaunchOpponents()
        {
            if (!AppSession.Ensure().EnsureLabTestAccount(out var err))
            {
                SetStatus("Login failed: " + err);
                return;
            }

            AppSession.Ensure().ClearTestDuelMode();
            var canvas = GameObject.Find("DesktopLabCanvas");
            var host = canvas != null ? canvas.transform : transform;
            OpponentSelectScreen.Build(host, () => { }, labTest: true);
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

        /// <summary>
        /// StreamingAssets loader facade check. On an Android development build this is the quickest
        /// on-device proof that the APK zip index works (card DB + decks readable).
        /// </summary>
        void RunStreamingSelfTest()
        {
            var report = StreamingFiles.SelfTest();
            SetStatus(report.OneLine + " · see Console");
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
