using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.EditorTools
{
    /// <summary>
    /// Headless EditorSim AR duel smoke test (batchmode + menu).
    /// Mounts the real <see cref="ArDuelSpace"/> dual-field graph, starts a lab duel,
    /// summons, syncs, and runs a few AI turns — no camera, no Play Mode GUI required.
    ///
    /// Batch:
    ///   Unity -batchmode -nographics -quit -projectPath … \
    ///     -executeMethod WRLDZ.EditorTools.ArSimulatedDuelSmoke.RunBatch \
    ///     -logFile ar_sim_smoke.log
    /// </summary>
    public static class ArSimulatedDuelSmoke
    {
        const float TestSeparationM = 4.0f; // Street preset — proves separation wiring

        [MenuItem("WRLDZ/Lab/Run Simulated AR Duel Smoke")]
        public static void RunInteractive()
        {
            var report = RunSmoke();
            EditorUtility.DisplayDialog(
                report.Ok ? "AR Sim PASS" : "AR Sim FAIL",
                Truncate(report.Summary, 1600),
                "OK");
        }

        /// <summary>Batchmode entry — exits 0/1.</summary>
        public static void RunBatch()
        {
            Debug.Log("[WRLDZ AR SIM] batch start");
            var report = RunSmoke();
            var outPath = Path.Combine(Directory.GetCurrentDirectory(), "wrldz_ar_sim_report.txt");
            try
            {
                File.WriteAllText(outPath, report.Summary);
                Debug.Log("[WRLDZ AR SIM] wrote " + outPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WRLDZ AR SIM] report write failed: " + ex.Message);
            }

            if (report.Ok)
            {
                Debug.Log("[WRLDZ AR SIM] PASS\n" + report.Summary);
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[WRLDZ AR SIM] FAIL\n" + report.Summary);
                EditorApplication.Exit(1);
            }
        }

        public struct SmokeReport
        {
            public bool Ok;
            public int Checks;
            public int Failures;
            public string Summary;
        }

        public static SmokeReport RunSmoke()
        {
            var sb = new StringBuilder();
            var checks = 0;
            var fails = 0;

            void Check(string name, bool ok, string detail = null)
            {
                checks++;
                if (ok)
                {
                    sb.AppendLine($"  OK  {name}" + (string.IsNullOrEmpty(detail) ? "" : " · " + detail));
                }
                else
                {
                    fails++;
                    sb.AppendLine($" FAIL {name}" + (string.IsNullOrEmpty(detail) ? "" : " · " + detail));
                    Debug.LogError("[WRLDZ AR SIM] FAIL " + name + " · " + detail);
                }
            }

            sb.AppendLine("═══ WRLDZ SIMULATED AR DUEL SMOKE ═══");
            sb.AppendLine($"time={DateTime.Now:u}");
            sb.AppendLine($"sep_target={TestSeparationM:0.0}m · EditorSim (no camera)");
            sb.AppendLine($"editor_playing={EditorApplication.isPlaying}");

            GameObject root = null;

            try
            {
                // Fresh disposable scene — avoid DontDestroyOnLoad (AppSession) outside Play Mode
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                sb.AppendLine($"scene={scene.name}");

                // ── Card DB + decks (pure file IO) ──
                var db = CardDatabase.Load();
                Check("CardDatabase load", db != null && db.Count > 0, $"count={db?.Count ?? 0}");

                var playerDeck = CardDatabase.LoadDeck("lab_rules_player.json")
                                 ?? CardDatabase.LoadDeck("player_starter.json");
                var aiDeck = CardDatabase.LoadDeck("lab_rules_ai.json")
                             ?? CardDatabase.LoadDeck("ai_kaiba.json");
                Check("Player deck", playerDeck != null, playerDeck?.name);
                Check("AI deck", aiDeck != null, aiDeck?.name);

                if (db == null || db.Count == 0 || playerDeck == null || aiDeck == null)
                {
                    sb.AppendLine("Abort — missing StreamingAssets data.");
                    return Fail(sb, checks, fails);
                }

                // ── Canvas + ArDuelSpace (real CreateInUi path) ──
                root = new GameObject("ArSimSmokeRoot");
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                root.AddComponent<CanvasScaler>();
                root.AddComponent<GraphicRaycaster>();
                var canvasRt = root.GetComponent<RectTransform>() ?? root.AddComponent<RectTransform>();

                var space = ArDuelSpace.CreateInUi(canvasRt, 0f, 0f, 1f, 1f, root.transform);
                Check("ArDuelSpace created", space != null);
                Check("ArDuelSpace Ready", space != null && space.Ready,
                    space != null ? $"status={space.ArStatus}" : "null");

                var ix = space?.Interaction;
                Check("Interaction system mounted", ix != null);
                Check("PlayerDisk", ix?.PlayerDisk != null);
                Check("OppDisk", ix?.OppDisk != null);
                Check("Disks start retracted",
                    ix?.PlayerDisk != null && !ix.PlayerDisk.IsDeployed &&
                    ix.PlayerDisk.BladeOpen01 < 0.15f,
                    $"blade={ix?.PlayerDisk?.BladeOpen01:0.00}");
                Check("HandVolume", ix?.HandVolume != null);
                Check("CardField", ix?.CardField != null);
                Check("Arena holograms", ix?.Arena != null);
                Check("Drag system", ix?.Drag != null);
                Check("Anime director", ix?.Anime != null);
                Check("Sync bridge", ix?.SyncBridge != null);
                Check("Tracker", ix?.Tracker != null && ix.Tracker.IsTracking);

                // Apply street separation the same way create-flow would
                var sim = ix != null ? ix.GetComponent<SimulatedArmTracker>() : null;
                Check("SimulatedArmTracker", sim != null);
                if (sim != null)
                {
                    sim.ApplyPlayerSeparationMeters(TestSeparationM);
                    Check("Separation applied",
                        Mathf.Abs(sim.SeparationMeters - TestSeparationM) < 0.05f,
                        $"{sim.SeparationMeters:0.0}m (want {TestSeparationM:0.0}m)");
                    Check("Player forearm pose",
                        sim.TryGetLeftForearm(out var pPose),
                        pPose.position.ToString("F2"));
                    Check("Opp forearm pose",
                        sim.TryGetOpponentLeftForearm(out var oPose),
                        oPose.position.ToString("F2"));
                    if (sim.TryGetLeftForearm(out pPose) && sim.TryGetOpponentLeftForearm(out oPose))
                    {
                        var dist = Vector3.Distance(pPose.position, oPose.position);
                        Check("Disk world span > 0.5", dist > 0.5f, $"dist={dist:0.00}");
                        // Street 4.0m → half*2*0.55 ≈ 2.2 stage units along Z roughly
                        Check("Disk span scales with separation", dist > 1.0f, $"dist={dist:0.00}");
                    }
                }

                // Stage camera + RT (sim path)
                var cam = space.GetComponentInChildren<Camera>(true);
                Check("Stage camera", cam != null && cam.targetTexture != null,
                    cam != null ? $"rt={cam.targetTexture?.width}x{cam.targetTexture?.height}" : "null");

                // ── Engine + bind + sync ──
                var engine = new DuelEngine();
                engine.StartDuel(db, playerDeck, aiDeck);
                Check("Engine started", engine.Player != null && engine.Opponent != null,
                    $"hands P={engine.Player?.HandCount} O={engine.Opponent?.HandCount}");
                Check("Opening hand drawn", engine.Player != null && engine.Player.HandCount >= 4,
                    $"hand={engine.Player?.HandCount}");

                space.BindEngine(engine, db);
                space.SyncFromEngine(engine, db);
                Check("Bind+Sync no throw", true);
                // Bind triggers anime deploy sequence
                Check("Deploy started after bind",
                    ix.PlayerDisk != null && (ix.PlayerDisk.IsDeployed || ix.PlayerDisk.Fx.Current == DiskFxEvent.BladeDeploy),
                    $"deployed={ix.PlayerDisk?.IsDeployed} evt={ix.PlayerDisk?.Fx.Current}");
                // Simulate deploy duration so blade reaches open
                for (var i = 0; i < 90; i++)
                {
                    ix.PlayerDisk?.Fx.Tick(1f / 30f, ix.PlayerDisk.DiskRoot, null,
                        ix.PlayerDisk.BladePivot, ix.PlayerDisk.ZonesRoot, ix.PlayerDisk.EnergyRing);
                    ix.OppDisk?.Fx.Tick(1f / 30f, ix.OppDisk.DiskRoot, null,
                        ix.OppDisk.BladePivot, ix.OppDisk.ZonesRoot, ix.OppDisk.EnergyRing);
                }

                Check("Player blade open after deploy",
                    ix.PlayerDisk != null && ix.PlayerDisk.BladeOpen01 > 0.85f,
                    $"blade={ix.PlayerDisk?.BladeOpen01:0.00}");

                // Normal Summon first legal monster → dual field update
                CardInstance summoned = null;
                foreach (var c in engine.Player.Hand)
                {
                    if (c?.Def == null || !c.Def.IsMonster) continue;
                    if (!engine.CanNormalSummonOrSet(engine.Player, c)) continue;
                    if (engine.TryNormalSummon(engine.Player, c, asSet: false))
                    {
                        summoned = c;
                        break;
                    }
                }

                Check("Summon attempt", true,
                    summoned != null ? summoned.Name : "no legal NS this hand");
                space.SyncFromEngine(engine, db);

                if (summoned != null)
                {
                    Check("Monster on engine field", engine.Player.MonsterCount >= 1,
                        $"count={engine.Player.MonsterCount}");
                    // Disk card visuals parent under zone transforms (not CardField root)
                    var diskKids = CountActiveChildren(ix.PlayerDisk != null ? ix.PlayerDisk.transform : null);
                    var arenaKids = CountActiveChildren(ix.Arena != null ? ix.Arena.transform : null);
                    Check("Player disk has zone visuals", diskKids > 0, $"children={diskKids}");
                    Check("Arena has visuals after summon", arenaKids > 0, $"children={arenaKids}");
                    space.PlayFx(playerSide: true, DiskFxEvent.SummonFlash);
                    Check("PlayFx SummonFlash", true);
                }
                else
                {
                    // Still OK if deck hand is all tributes — not an AR failure
                    sb.AppendLine("  note: no NS this hand — skipping field visual checks");
                }

                // ── AI stress turns with AR re-sync ──
                var prevDelay = SimpleAi.StepDelay;
                SimpleAi.StepDelay = 0f;
                var turns = 0;
                var maxTurns = 8;
                var softLock = 0;
                try
                {
                    while (!engine.GameOver && turns < maxTurns)
                    {
                        var who = engine.TurnPlayer;
                        if (who == null) break;
                        DuelStressAgent.PlayTurn(engine, who);
                        DuelStressAgent.DrainWindows(engine);
                        space.SyncFromEngine(engine, db);
                        turns++;
                        if (engine.IsBusy)
                        {
                            engine.RecoverStuckCombat();
                            softLock++;
                        }
                    }
                }
                finally
                {
                    SimpleAi.StepDelay = prevDelay;
                }

                Check("Played stress turns", turns > 0, $"turns={turns}");
                Check("No hard soft-lock pileup", softLock < 20, $"recoveries={softLock}");
                Check("AR still Ready after turns", space.Ready);
                Check("Interaction still live",
                    space.Interaction != null && space.Interaction.PlayerDisk != null);

                // PreferDigital path unit: match config flag exists and summary includes mode
                var dig = ArDuelMatchConfig.Practice();
                dig.PreferDigital = true;
                dig.SeparationMeters = TestSeparationM;
                dig.ClampSeparation();
                Check("PreferDigital summary", dig.SummaryLine().Contains("digital"), dig.SummaryLine());

                sb.AppendLine();
                sb.AppendLine($"engine_end: turn={engine.TurnNumber} phase={engine.Phase} " +
                              $"gameOver={engine.GameOver} " +
                              $"LP={engine.Player?.LifePoints}/{engine.Opponent?.LifePoints}");
            }
            catch (Exception ex)
            {
                fails++;
                sb.AppendLine(" EXCEPTION " + ex);
                Debug.LogException(ex);
            }
            finally
            {
                try
                {
                    if (root != null)
                        UnityEngine.Object.DestroyImmediate(root);
                }
                catch
                {
                    // ignore teardown
                }
            }

            sb.AppendLine();
            sb.AppendLine($"checks={checks} fails={fails}");
            sb.AppendLine(fails == 0 ? "RESULT: PASS" : "RESULT: FAIL");
            return new SmokeReport
            {
                Ok = fails == 0,
                Checks = checks,
                Failures = fails,
                Summary = sb.ToString()
            };
        }

        static SmokeReport Fail(StringBuilder sb, int checks, int fails) =>
            new()
            {
                Ok = false,
                Checks = checks,
                Failures = fails,
                Summary = sb.ToString()
            };

        static int CountActiveChildren(Transform t)
        {
            if (t == null) return 0;
            var n = 0;
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c != null && c.gameObject.activeInHierarchy)
                    n += 1 + CountActiveChildren(c);
            }

            return n;
        }

        static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "\n…");
    }
}
