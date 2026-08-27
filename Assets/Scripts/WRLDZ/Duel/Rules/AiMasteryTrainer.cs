using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using Debug = UnityEngine.Debug;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Self-play mastery training: set coverage + multi-duel AI runs + review JSONL export.
    /// Editor: WRLDZ → AI → Run Pre-Link Mastery Pass
    /// Batch: -executeMethod WRLDZ.EditorTools.AiMasteryMenu.RunMasteryBatch
    ///
    /// Phase 2 gate: starter+lab playable coverage must be 100% (effectless Fusions count)
    /// before self-play is considered a clean mastery pass.
    /// </summary>
    public static class AiMasteryTrainer
    {
        public const int DefaultDuelsPerSet = 24;

        public struct MasteryReport
        {
            public string Summary;
            public bool Ok;
            public string ExportPath;
            public bool CoverageGateOk;
        }

        public static MasteryReport Run(int duelsPerSet = DefaultDuelsPerSet)
        {
            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            sb.AppendLine("═══ WRLDZ AI PRE-LINK MASTERY ═══");
            AiHeuristicPolicy.EnsureLoaded();
            sb.AppendLine($"Heuristics lines={AiHeuristicPolicy.LoadedLines.Count}");

            var db = CardDatabase.Load();

            // Phase 2: coverage gate (structural + scripts + compiled text)
            var gateOk = EffectCoverageService.MeetsMasteryCoverageGate(out var cov, out var gateReason);
            sb.AppendLine(cov.Summary);
            sb.AppendLine("Coverage gate: " + gateReason);
            sb.AppendLine(CardEraCurriculum.MeasureAllReport());

            var ok = gateOk;
            if (!gateOk)
                sb.AppendLine("WARN: coverage gate failed — self-play still runs for telemetry, RESULT will be NEEDS WORK.");

            var totalDuels = 0;
            var completed = 0;
            var softLocks = 0;
            var exceptions = 0;

            // Use lab decks as proxy opponents; later: set-filtered builders
            var pDeck = CardDatabase.LoadDeck("lab_rules_player.json");
            var aDeck = CardDatabase.LoadDeck("lab_rules_ai.json");
            if (pDeck == null || aDeck == null)
            {
                ok = false;
                sb.AppendLine("FAIL lab decks missing");
            }
            else
            {
                var sets = CardEraCurriculum.NextTwoSets().ToList();
                if (sets.Count == 0)
                    sets = CardEraCurriculum.Load()?.sets?.OrderBy(s => s.order).Take(3).ToList()
                           ?? new List<CardEraCurriculum.SetDef>();

                foreach (var set in sets)
                {
                    sb.AppendLine($"── Self-play focus: {set.code} ({duelsPerSet} duels) ──");
                    var prev = SimpleAi.StepDelay;
                    SimpleAi.StepDelay = 0f;
                    try
                    {
                        for (var g = 0; g < duelsPerSet; g++)
                        {
                            totalDuels++;
                            UnityEngine.Random.InitState(2000 + set.order * 100 + g);
                            try
                            {
                                var engine = new DuelEngine();
                                engine.StartDuel(db, pDeck, aDeck);
                                // Tag session for review export
                                engine.LogReview(DuelLogKind.Ai, "Mastery",
                                    $"SET_FOCUS {set.code} duel#{g}");

                                var steps = 0;
                                while (!engine.GameOver && steps++ < 320)
                                {
                                    DuelStressAgent.DrainWindows(engine);
                                    if (engine.GameOver) break;
                                    var who = engine.TurnPlayer;
                                    if (who == null) break;
                                    DuelStressAgent.PlayTurn(engine, who);
                                    DuelStressAgent.DrainWindows(engine);
                                    if (engine.TurnNumber > DuelEngineStressTests.MaxTurnsPerDuel)
                                        break;
                                }

                                if (engine.GameOver) completed++;
                                engine.ReviewLog?.ExportToDisk();
                            }
                            catch (Exception ex)
                            {
                                exceptions++;
                                ok = false;
                                sb.AppendLine($"FAIL {set.code}#{g}: {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        SimpleAi.StepDelay = prev;
                    }
                }
            }

            sw.Stop();
            sb.AppendLine(
                $"duels={totalDuels} completed={completed} softLocks={softLocks} " +
                $"exceptions={exceptions} ms={sw.ElapsedMilliseconds}");
            sb.AppendLine(ok && exceptions == 0 ? "RESULT: OK" : "RESULT: NEEDS WORK");

            var summary = sb.ToString();
            var path = Path.Combine(Application.persistentDataPath, "WRLDZ", "mastery_report.txt");
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, summary);
            }
            catch
            {
                path = null;
            }

            if (ok && exceptions == 0)
                Debug.Log("[WRLDZ MASTERY]\n" + summary);
            else
                Debug.LogError("[WRLDZ MASTERY]\n" + summary);

            return new MasteryReport
            {
                Summary = summary,
                Ok = ok && exceptions == 0 && gateOk,
                ExportPath = path,
                CoverageGateOk = gateOk
            };
        }
    }
}
