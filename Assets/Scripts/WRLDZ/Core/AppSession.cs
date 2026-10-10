using System;
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
    ///   └─► MainMenu   ← Duel Disk (Eldritch Solid Vision) / default after login
    ///         ├─ DUEL ──► VS AI · VS PLAYER ──► DuelSlice
    ///         │           └─ ZONE MODE ──► ZoneModeSession (AR disk at your location)
    ///         ├─ DECK / BAG / STORY / BAZAAR / VIEW / SET (overlay sheets)
    ///         ├─ WORLD MAP ──► Overworld
    ///         │                  ├─ Tear pin ──► DuelSlice
    ///         │                  ├─ Street NPC ──► Possession (placeholder) ──► DuelSlice
    ///         │                  ├─ Eye TOURNEY ──► room lobby ──► DuelSlice
    ///         │                  └─ Disk HUB ──► MainMenu
    ///         └─ QUIT ──► logout ──► Boot
    ///
    /// DuelSlice exit (Menu / Map) ──► Overworld
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

        /// <summary>
        /// The lab door's fixed account. Reserved: <see cref="PlayerAccountDatabase.TryCreate"/> refuses it for
        /// players, and it never confers admin by name (see <see cref="LabAdminService.IsAdmin"/>).
        /// </summary>
        public const string LabTestUsername = "lab_tester";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Lab-only credential; compiled out of release players together with the lab doors.
        const string LabTestPassword = "labtest";
#endif

        /// <summary>
        /// Credit given to soul-card timers for a duel that was never closed (app killed mid-duel):
        /// the pause ends at min(now, pauseStart + this). Owner default 2026-09-23 (QC F6): 60 minutes.
        /// </summary>
        public const long StaleSoulPauseCreditSec = 60 * 60;

        /// <summary>True while this app session itself holds the soul-timer pause (between duel entry and exit).</summary>
        bool _soulPauseHeldBySession;

        public static AppSession Ensure()
        {
            if (Instance != null)
            {
                if (Instance.Account == null)
                    Instance.RefreshFromStore();
                return Instance;
            }

            var go = new GameObject("AppSession");
            if (Application.isPlaying)
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
            if (Application.isPlaying)
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
                if (!WrldzLab.AccountAllowedInThisBuild(Account.progress))
                {
                    Debug.LogWarning("[WRLDZ] Session dropped — " + WrldzLab.LabAccountBlockedMessage);
                    Logout();
                    return;
                }

                // A rewarded duel an earlier launch never finished (app killed / swiped away) is a forfeit
                // loss (owner rule 2026-09-23 #3) — settled before the soul pause closes.
                PendingDuelMarker.SettleAbandoned(Account);
                EndStaleSoulPause(Account);
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

        /// <summary>Battle City map. Requires login. Reached from the Duel Disk via WORLD MAP.</summary>
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

        /// <summary>Duel Disk main menu (Eldritch Solid Vision). Requires login. Boot lands here.</summary>
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
        /// Where a duel launched right now should return to. The Duel Disk menu (hub CREATE
        /// screens, Zone Mode / DUEL HERE) runs inside MainMenu and owns no scene load, so the
        /// live scene is the launch site; tear pins, street NPCs and tourney rooms are Overworld.
        /// Anything else (lab / test scenes) keeps the Overworld default.
        /// </summary>
        public static string LaunchEntrySource() =>
            SceneManager.GetActiveScene().name == SceneMainMenu ? SceneMainMenu : SceneOverworld;

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
            PauseSoulTimers();
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
            PauseSoulTimers();
            // Owner rule: no player body during duels. Hide it for the scene hop (cleared on the next
            // scene load; ArDuelSpace holds the hide for the duel itself).
            OverworldAvatar3d.HideBody(OverworldAvatar3d.HideForDuelLaunch, true);
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
            if (!WrldzLab.LabDoorsEnabled)
            {
                Debug.LogWarning("[WRLDZ] Lab test duel refused — Editor / development builds only.");
                return false;
            }

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
            if (!WrldzLab.LabDoorsEnabled)
            {
                Debug.LogWarning("[WRLDZ] Desktop Lab refused — Editor / development builds only.");
                return;
            }

            if (!EnsureLabTestAccount(out var err))
                Debug.LogWarning("[WRLDZ] Desktop Lab opened without the lab account: " + err);
            UI.DesktopLabApp.Launch();
        }

        /// <summary>
        /// Create or log in fixed lab_tester account with onboarding already complete, and mark it lab admin.
        /// Editor / development builds only; release players get <c>false</c> and no account is touched.
        /// </summary>
        public bool EnsureLabTestAccount(out string error)
        {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            error = "Lab doors are only available in the Editor and development builds.";
            return false;
#else
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
                if (!LocalAccountStore.TryCreateLabAccount(LabTestUsername, LabTestPassword, "Great SaiyaDave",
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
            ErazProgress.GrantTutorialBadge(acc);
            if (acc.progress.kuribohTeam == 0)
                acc.progress.kuribohTeam = (int)KuribohTeam.Kuribandit;
            StarterKitService.GrantIfNeeded(acc);
            // Lab tester is a local admin: full catalog, cap currencies, every unlock (explicit flag, not by name)
            LabAdminService.GrantLabAdmin(acc);
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
#endif
        }

        public void ClearTestDuelMode()
        {
            TestDuelMode = false;
        }

        /// <summary>
        /// Leave duel → back where the duel was launched from: MainMenu (Duel Disk) for hub /
        /// Zone Mode launches, Overworld for tear pins, street NPCs and tourney rooms.
        /// Boot when lab test mode.
        /// </summary>
        public void LeaveDuel()
        {
            // Captured before ClearPendingArMatch so the return route survives the reset.
            var entry = DuelEntrySource;
            // Owner rule 2026-09-23 #3: walking out of a rewarded duel before it ends is a forfeit loss
            // (streaks, stakes, Shadow Game rules); a finished duel pays its real result, once.
            SettlePendingDuel("left the duel");
            ResumeSoulTimers();
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

            if (entry == SceneMainMenu)
            {
                Debug.Log("[WRLDZ] Duel ended → MainMenu (launched from the Duel Disk).");
                GoMainMenu();
                return;
            }

            GoOverworld();
        }

        public void GoBoot()
        {
            // Keep TestDuelMode false when intentionally booting unless about to re-enter
            LoadScene(SceneBoot);
        }

        /// <summary>Live duel state, registered by <see cref="DuelBootstrap"/> while a duel scene runs.</summary>
        public delegate bool ActiveDuelProbe(out bool playerWon, out int turnNumber);

        ActiveDuelProbe _activeDuel;

        /// <summary>DuelBootstrap: report whether the running duel is over (and who won) for leave-time settling.</summary>
        public void RegisterActiveDuel(ActiveDuelProbe probe) => _activeDuel = probe;

        public void ClearActiveDuel(ActiveDuelProbe probe)
        {
            if (_activeDuel == probe) _activeDuel = null;
        }

        /// <summary>
        /// Settle the pending rewarded duel on the way out when its result has not been claimed yet: a duel that
        /// is over pays its real result (the player left during the closing beat, before the overlay claimed
        /// it); a duel still running is a forfeit loss (<see cref="ProgressionService.ForfeitDuel"/>). Practice,
        /// lab, hotseat and already-claimed duels are untouched, and so is a duel with no live engine to ask.
        /// The UI asks the player to confirm before a forfeit; this is the backstop for every exit path.
        /// </summary>
        public bool SettlePendingDuel(string reason)
        {
            var match = PendingArMatch;
            if (Account == null || match == null || TestDuelMode) return false;
            if (!ProgressionService.IsForfeitable(Account, match)) return false;
            if (_activeDuel == null)
            {
                Debug.LogWarning($"[WRLDZ] Unclaimed duel {match.DuelResultId} has no live engine — not settled.");
                return false;
            }

            DuelRewardResult r;
            if (_activeDuel(out var won, out var turns))
                r = ProgressionService.AwardDuelRewards(Account, match, won, turns);
            else
                r = ProgressionService.ForfeitDuel(Account, match, turns);
            Debug.Log($"[WRLDZ] Duel settled on exit ({reason}) · {match.FormatTitle} · {r.SummaryLine}");
            return true;
        }

        void PauseSoulTimers()
        {
            if (Account == null) return;
            Account.EnsureInventory();
            Account.EnsureProgress();
            SoulCardService.PauseBegin(Account.inventory, ClockGuard.NowUnix(Account.progress));
            _soulPauseHeldBySession = true;
            LocalAccountStore.UpdateAccount(Account);
        }

        void ResumeSoulTimers()
        {
            _soulPauseHeldBySession = false;
            if (Account == null) return;
            Account.EnsureInventory();
            Account.EnsureProgress();
            SoulCardService.PauseEnd(Account.inventory, ClockGuard.NowUnix(Account.progress));
            LocalAccountStore.UpdateAccount(Account);
        }

        /// <summary>
        /// A soul-timer pause this session did not start was left by a duel that never closed (the app was
        /// killed mid-duel). End it with a capped credit (<see cref="StaleSoulPauseCreditSec"/>) and let the
        /// expired soul cards go, instead of freezing every timer until some later duel ends (QC F6).
        /// </summary>
        void EndStaleSoulPause(LocalAccountStore.Account acc)
        {
            var inv = acc?.inventory;
            if (inv == null || inv.soulPauseStartedUnix <= 0 || _soulPauseHeldBySession) return;
            acc.EnsureProgress();
            var now = ClockGuard.NowUnix(acc.progress);
            var start = inv.soulPauseStartedUnix;
            var end = start > now ? start : Math.Min(now, start + StaleSoulPauseCreditSec);
            SoulCardService.PauseEnd(inv, end);
            var gone = SoulCardService.Tick(inv, now, paused: false);
            Debug.Log($"[WRLDZ] Stale duel pause closed · credited {Math.Max(0, end - start) / 60} min · " +
                      $"{gone} soul card(s) returned to the aether.");
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
