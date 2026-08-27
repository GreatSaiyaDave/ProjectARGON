using UnityEngine;
using UnityEngine.SceneManagement;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;

namespace WRLDZ.Core
{
    /// <summary>
    /// App-wide session + scene navigation (DontDestroyOnLoad).
    ///
    /// Canon menu graph (single source of truth):
    /// <code>
    /// Boot  (entry only — splash → load → DOB? → auth → terms? → world load)
    ///   └─► Overworld   ← HOME / default after login
    ///         ├─ Disk HUB ──► MainMenu (Mr. Referobot hub)
    ///         │                  ├─ Shadow Duel ──► DuelSlice
    ///         │                  ├─ stubs (Deck / Binder / …)
    ///         │                  └─ MAP back ──► Overworld
    ///         ├─ Tear pin ──► DuelSlice
    ///         ├─ Street NPC ──► Possession (placeholder) ──► DuelSlice
    ///         ├─ Eye TOURNEY ──► room lobby ──► DuelSlice
    ///         └─ QUIT ──► logout ──► Boot
    ///
    /// DuelSlice exit (Menu / Map) ──► Overworld (home)
    /// Lab TEST DUEL from Boot Auth/Create ──► DuelSlice; Map ──► Boot
    /// </code>
    /// Protected scenes (Overworld, MainMenu, Duel) require a logged-in session.
    /// </summary>
    public class AppSession : MonoBehaviour
    {
        public const string SceneBoot = "Boot";
        public const string SceneOverworld = "Overworld";
        public const string SceneMainMenu = "MainMenu";
        public const string SceneDuel = "DuelSlice";
        public const string ScenePossession = "Possession";

        public static AppSession Instance { get; private set; }

        public LocalAccountStore.Account Account { get; private set; }
        public bool IsLoggedIn =>
            Account != null &&
            !string.IsNullOrEmpty(Account.username) &&
            !Account.deactivated;

        /// <summary>Where the player entered the last duel from (telemetry / future return logic).</summary>
        public string DuelEntrySource { get; private set; } = SceneOverworld;

        /// <summary>
        /// Active AR match parameters (separation meters, launch kind, zone).
        /// Set by <see cref="StartArDuel"/> before loading DuelSlice; consumed by AR stage.
        /// </summary>
        public ArDuelMatchConfig PendingArMatch { get; private set; }

        /// <summary>
        /// Lab test duel: skip full onboarding, fixed starter vs Kaiba, leave returns to Boot.
        /// Set by <see cref="StartLabTestDuel"/> from the Boot create-account / auth screens.
        /// </summary>
        public bool TestDuelMode { get; private set; }

        /// <summary>Deck files under StreamingAssets/Decks/ when <see cref="TestDuelMode"/>.</summary>
        /// <summary>Guaranteed-legal decks: every S/T is scripted; Flip monsters have Flip scripts.</summary>
        public string TestPlayerDeckFile { get; private set; } = "lab_rules_player.json";
        public string TestAiDeckFile { get; private set; } = "lab_rules_ai.json";

        public const string LabTestUsername = "lab_tester";
        public const string LabTestPassword = "labtest";

        public static AppSession Ensure()
        {
            if (Instance != null)
            {
                if (Instance.Account == null)
                    Instance.RefreshFromStore();
                return Instance;
            }

            var go = new GameObject("AppSession");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AppSession>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;

            PlayerAccountDatabase.Initialize();
            RefreshFromStore();
            Debug.Log($"[WRLDZ] AppSession awake. loggedIn={IsLoggedIn} db={PlayerAccountDatabase.DatabasePath}");
        }

        public void RefreshFromStore()
        {
            PlayerAccountDatabase.Initialize();
            Account = LocalAccountStore.GetSessionAccount();
            if (Account != null)
            {
                Account.EnsureProgress();
                if (SoulFractureService.SyncDeactivation(Account))
                {
                    Debug.Log("[WRLDZ] Session dropped — Spirit Dueler deactivated (soul shattered).");
                    LocalAccountStore.UpdateAccount(Account);
                    Logout();
                    return;
                }

                LocalAccountStore.UpdateAccount(Account);
                Debug.Log($"[WRLDZ] Session restored: {Account.username} ({Account.displayName})");
            }
            else
                Debug.Log("[WRLDZ] No session account on disk/prefs.");
        }

        public void SetAccount(LocalAccountStore.Account account)
        {
            Account = account;
            if (account != null)
            {
                PlayerAccountDatabase.SetSession(account.ToDb());
                Debug.Log($"[WRLDZ] Session set: {account.username}");
            }
            else
            {
                PlayerAccountDatabase.ClearSession();
            }
        }

        public void Logout()
        {
            Account = null;
            PlayerAccountDatabase.ClearSession();
            LocalAccountStore.ClearSession();
            DuelEntrySource = SceneOverworld;
        }

        public void LoadScene(string sceneName)
        {
            if (Account == null)
                RefreshFromStore();
            Debug.Log($"[WRLDZ] LoadScene → {sceneName} (loggedIn={IsLoggedIn})");
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>HOME — Battle City map. Requires login.</summary>
        public void GoOverworld()
        {
            if (!RequireLogin(SceneOverworld)) return;
            // Leave boot theme (Scarab Under Stone) behind when entering the world
            try
            {
                WrldzAudio.SetBgmVolume(0.18f);
                WrldzAudio.PlayAmbientBgm();
            }
            catch { /* audio optional */ }
            LoadScene(SceneOverworld);
        }

        /// <summary>Mr. Referobot hub (systems). Requires login. Enter only from map Disk.</summary>
        public void GoMainMenu()
        {
            if (!RequireLogin(SceneMainMenu)) return;
            try
            {
                WrldzAudio.SetBgmVolume(0.18f);
                WrldzAudio.PlayAmbientBgm();
            }
            catch { /* audio optional */ }
            LoadScene(SceneMainMenu);
        }

        /// <summary>
        /// Shadow Duel vertical slice (always AR stage). Requires login.
        /// Prefer <see cref="StartArDuel"/> so field separation is set.
        /// <paramref name="entrySource"/> should be SceneOverworld (tear) or SceneMainMenu (hub).
        /// Exit returns via <see cref="LeaveDuel"/> (Overworld, or Boot when TestDuelMode).
        /// </summary>
        public void GoDuel(string entrySource = null)
        {
            if (!RequireLogin(SceneDuel)) return;
            if (!TestDuelMode)
                DuelEntrySource = string.IsNullOrEmpty(entrySource) ? SceneOverworld : entrySource;
            // Legacy entry: ensure a default AR match exists
            if (PendingArMatch == null && !TestDuelMode)
            {
                PendingArMatch = ArDuelMatchConfig.DefaultQuick();
                PendingArMatch.EntrySource = DuelEntrySource;
                PendingArMatch.CaptureHostLocation(MapEnvironment.Instance);
            }

            if (!TestDuelMode
                && PendingArMatch != null
                && RequiresOriginalBadgeForLaunch(PendingArMatch.Launch)
                && !CanStartConstructedPvAi())
            {
                Debug.LogWarning(
                    "[WRLDZ] GoDuel refused — Original ERAZ badge required (finish tutorial).");
                PendingArMatch = null;
                return;
            }

            Debug.Log($"[WRLDZ] Entering AR duel from {DuelEntrySource} testMode={TestDuelMode} " +
                      $"sep={PendingArMatch?.SeparationMeters:0.0}m");
            LoadScene(SceneDuel);
        }

        /// <summary>
        /// Hub / overworld CREATE / quick PvAI require the Original ERAZ badge.
        /// Practice, lab TEST DUEL, and boot tutorial (Practice launch) do not.
        /// </summary>
        public static bool RequiresOriginalBadgeForLaunch(ArDuelLaunchKind launch) =>
            launch == ArDuelLaunchKind.Hub || launch == ArDuelLaunchKind.CreateMenu;

        public bool CanStartConstructedPvAi()
        {
            if (Account == null) return false;
            Account.EnsureProgress();
            return Account.progress != null
                   && ErazProgress.CanSelectBand(Account.progress, ErazFormat.Original);
        }

        /// <summary>
        /// Canonical AR duel entry from overworld / hub: all live duels are AR and
        /// the field is anchored by player separation meters.
        /// </summary>
        public void StartArDuel(ArDuelMatchConfig config)
        {
            if (config == null)
                config = ArDuelMatchConfig.DefaultQuick();
            config.ClampSeparation();
            if (string.IsNullOrEmpty(config.ErazBandId))
                config.ErazBandId = ErazFormat.Original;
            if (string.IsNullOrEmpty(config.EntrySource))
                config.EntrySource = SceneOverworld;
            // PvP already marked host+guest GPS — do not overwrite host with current (often guest) pos
            if (!config.HostGpsValid)
                config.CaptureHostLocation(MapEnvironment.Instance ?? MapEnvironment.Ensure());
            else if (config.GuestGpsValid)
                config.RecomputeSeparationFromGps();

            // Remember surface scan so tear / training duels can reuse arena size
            if (config.AutoSurfaceScan)
                MapZoneService.PersistSurfaceScan(config);

            if (!RequireLogin(SceneDuel)) return;

            if (RequiresOriginalBadgeForLaunch(config.Launch) && !CanStartConstructedPvAi())
            {
                Debug.LogWarning(
                    "[WRLDZ] StartArDuel refused — Original ERAZ badge required (finish tutorial).");
                return;
            }

            TestDuelMode = false;
            PendingArMatch = config;
            DuelEntrySource = config.EntrySource;
            // Deck overrides live on config; DuelBootstrap reads PendingArMatch

            Debug.Log($"[WRLDZ] StartArDuel · {config.SummaryLine()} · launch={config.Launch}");
            if (config.PossessionCinematic && !config.SkipPreDuelCinematic)
            {
                LoadScene(ScenePossession);
                return;
            }

            LoadScene(SceneDuel);
        }

        /// <summary>Possession cinematic finished — keep pending match and load DuelSlice.</summary>
        public void ContinuePendingDuel()
        {
            if (!RequireLogin(SceneDuel)) return;
            Debug.Log("[WRLDZ] ContinuePendingDuel → DuelSlice");
            LoadScene(SceneDuel);
        }

        /// <summary>Consume and clear pending AR match after the stage applies it (optional).</summary>
        public ArDuelMatchConfig TakePendingArMatch()
        {
            var m = PendingArMatch;
            return m;
        }

        public void ClearPendingArMatch()
        {
            PendingArMatch = null;
        }

        /// <summary>
        /// Editor / batch smoke tests: inject match without loading DuelSlice.
        /// Production code should use <see cref="StartArDuel"/>.
        /// </summary>
        public void SetPendingArMatchForTest(ArDuelMatchConfig config)
        {
            PendingArMatch = config;
        }

        /// <summary>
        /// Fast path for lab testing: ensure a disposable lab account, skip onboarding gates,
        /// load DuelSlice with fixed starter vs Kaiba. Leave returns to Boot.
        /// </summary>
        public bool StartLabTestDuel(string playerDeck = null, string aiDeck = null)
        {
            if (!EnsureLabTestAccount(out var err))
            {
                Debug.LogError("[WRLDZ] Lab test duel aborted: " + err);
                return false;
            }

            TestDuelMode = true;
            TestPlayerDeckFile = string.IsNullOrEmpty(playerDeck) ? "lab_rules_player.json" : playerDeck;
            TestAiDeckFile = string.IsNullOrEmpty(aiDeck) ? "lab_rules_ai.json" : aiDeck;
            DuelEntrySource = SceneBoot;
            LocalAccountStore.TermsAccepted = true;

            // Prefer AR match config so EditorSim separation + dual field apply
            var c = ArDuelMatchConfig.Practice();
            c.Launch = ArDuelLaunchKind.LabTest;
            c.Opponent = ArDuelOpponentKind.AiLocal;
            c.PreferDigital = true;
            c.PlayerDeckFile = TestPlayerDeckFile;
            c.AiDeckFile = TestAiDeckFile;
            c.FormatTitle = "Lab Test Duel · EditorSim";
            c.EntrySource = SceneBoot;
            c.ClampSeparation();
            PendingArMatch = c;

            Debug.Log(
                $"[WRLDZ] LAB TEST DUEL — you={TestPlayerDeckFile} vs AI={TestAiDeckFile} " +
                $"(account={Account?.username}) · {c.SummaryLine()}");
            LoadScene(SceneDuel);
            return true;
        }

        /// <summary>Open equipment-free Desktop Lab hub (Editor / PC).</summary>
        public void OpenDesktopLab()
        {
            EnsureLabTestAccount(out _);
            UI.DesktopLabApp.Launch();
        }

        /// <summary>Create or log in fixed lab_tester account with onboarding already complete.</summary>
        public bool EnsureLabTestAccount(out string error)
        {
            error = null;
            PlayerAccountDatabase.Initialize();
            LocalAccountStore.EnsureLoaded();

            LocalAccountStore.Account acc = null;
            if (LocalAccountStore.UsernameExists(LabTestUsername))
            {
                if (!LocalAccountStore.TryLogin(LabTestUsername, LabTestPassword, out acc, out error))
                {
                    // Password may have been changed — still try session refresh after re-create fails
                    error = "lab_tester exists but password is not labtest: " + error;
                    return false;
                }
            }
            else
            {
                if (!LocalAccountStore.TryCreate(LabTestUsername, LabTestPassword, "Great SaiyaDave",
                        "lab@wrldz.local", out acc, out error))
                    return false;
            }

            if (acc == null)
            {
                error = "No lab account.";
                return false;
            }

            acc.displayName = "Great SaiyaDave";
            acc.EnsureProgress();
            acc.EnsureInventory();
            // Skip full onboarding cascade for fast iteration
            acc.progress.onboardingPrologueDone = true;
            acc.progress.onboardingKuribohChosen = true;
            acc.progress.onboardingTutorialDuelDone = true;
            acc.progress.onboardingComplete = true;
            ErazProgress.GrantTutorialBadge(acc.progress);
            if (acc.progress.kuribohTeam == 0)
                acc.progress.kuribohTeam = (int)KuribohTeam.Kuribandit;
            StarterKitService.GrantIfNeeded(acc);
            // Lab tester is a local admin: full catalog, cap currencies, every unlock
            LabAdminService.MaxOut(acc);
            ProgressionService.Persist(acc);

            LocalAccountStore.TermsAccepted = true;
            // Age gate not re-prompted mid-lab
            SetAccount(acc);
            RefreshFromStore();
            if (!IsLoggedIn)
            {
                error = "Session not set after lab login.";
                return false;
            }

            Debug.Log(
                $"[WRLDZ] Lab account ready · {LabTestUsername} · admin={LabAdminService.IsAdmin(acc)} · " +
                $"Lv{acc.progress?.level ?? 0} · catalog={(LabCatalogService.IsGranted(acc) ? "FULL" : "partial")} · " +
                $"storage≈{acc.inventory?.TotalStorageUsed() ?? 0} cards");
            return true;
        }

        public void ClearTestDuelMode()
        {
            TestDuelMode = false;
        }

        /// <summary>Leave duel → Overworld (home), or Boot when lab test mode.</summary>
        public void LeaveDuel()
        {
            ClearPendingArMatch();
            if (Account != null && Account.deactivated)
            {
                Logout();
                GoBoot();
                return;
            }

            if (TestDuelMode)
            {
                TestDuelMode = false;
                Debug.Log("[WRLDZ] Lab test duel ended → Boot (fast re-test).");
                GoBoot();
                return;
            }

            GoOverworld();
        }

        public void GoBoot()
        {
            // Keep TestDuelMode false when intentionally booting unless about to re-enter
            LoadScene(SceneBoot);
        }

        bool RequireLogin(string target)
        {
            RefreshFromStore();
            if (IsLoggedIn) return true;
            Debug.LogWarning($"[WRLDZ] {target} blocked — not logged in. Returning to Boot.");
            LoadScene(SceneBoot);
            return false;
        }
    }
}
