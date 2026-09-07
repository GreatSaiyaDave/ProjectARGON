using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using Debug = UnityEngine.Debug;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Headless stress: unit regressions + lab text compile + many AI-vs-AI lab duels.
    /// Editor: WRLDZ → Rules → Run Engine Stress Tests
    /// Batch: -executeMethod WRLDZ.EditorTools.DuelStressMenu.RunStressBatch
    /// </summary>
    public static class DuelEngineStressTests
    {
        public const int DefaultDuelCount = 40;
        public const int MaxTurnsPerDuel = 80;
        public const int SoftLockStepsThreshold = 24;

        public struct StressReport
        {
            public int UnitPass;
            public int UnitFail;
            public int DuelsPlayed;
            public int DuelsCompleted;
            public int DuelsTurnCapped;
            public int SoftLocksRecovered;
            public int Exceptions;
            public int PlayerWins;
            public int OppWins;
            public int DrawsOrCap;
            public int TextProgramsFull;
            public int TextProgramsPartial;
            public long ElapsedMs;
            public string Summary;
            public bool Ok => UnitFail == 0 && Exceptions == 0 && SoftLocksRecovered == 0;
        }

        public static string RunAll(int duelCount = DefaultDuelCount) =>
            Run(duelCount).Summary;

        public static StressReport Run(int duelCount = DefaultDuelCount)
        {
            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            var report = new StressReport();

            sb.AppendLine("═══ WRLDZ ENGINE STRESS ═══");
            sb.AppendLine($"duels={duelCount} maxTurns={MaxTurnsPerDuel}");

            // ── 1) Unit regressions ──
            sb.AppendLine();
            sb.AppendLine("── Unit: TcgRegressionTests ──");
            var unit = TcgRegressionTests.RunAll();
            sb.AppendLine(unit.TrimEnd());
            ParseUnitCounts(unit, out report.UnitPass, out report.UnitFail);

            // ── 1b) Live interaction regressions (Sangan / Flip / zones) ──
            sb.AppendLine();
            sb.AppendLine("── Unit: InteractionRegressionTests ──");
            var ix = InteractionRegressionTests.RunAll();
            sb.AppendLine(ix.TrimEnd());
            ParseUnitCounts(ix, out var ixPass, out var ixFail);
            report.UnitPass += ixPass;
            report.UnitFail += ixFail;

            // ── 1c) Corpus trigger sweep (every cards_db FullyCompiled card) ──
            sb.AppendLine();
            sb.AppendLine("── Unit: CorpusTriggerStressTests ──");
            var corpus = CorpusTriggerStressTests.Run();
            sb.AppendLine(corpus.TrimEnd());
            ParseUnitCounts(corpus, out var cPass, out var cFail);
            report.UnitPass += cPass;
            report.UnitFail += cFail;

            // ── 1d) Multi-link chain rules (spell speed, LIFO, negation) ──
            sb.AppendLine();
            sb.AppendLine("── Unit: ChainRegressionTests ──");
            var chain = ChainRegressionTests.RunAll();
            sb.AppendLine(chain.TrimEnd());
            ParseUnitCounts(chain, out var chPass, out var chFail);
            report.UnitPass += chPass;
            report.UnitFail += chFail;

            // ── 1e) Coin toss / die roll mechanic + arena presentation bus ──
            sb.AppendLine();
            sb.AppendLine("── Unit: CoinDiceRegressionTests ──");
            var coindice = CoinDiceRegressionTests.RunAll();
            sb.AppendLine(coindice.TrimEnd());
            ParseUnitCounts(coindice, out var cdPass, out var cdFail);
            report.UnitPass += cdPass;
            report.UnitFail += cdFail;

            // ── 2) Text effect compile (lab unique cards) ──
            sb.AppendLine();
            sb.AppendLine("── Text: lab deck compile (regex, no AI) ──");
            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                report.UnitFail++;
                sb.AppendLine("FAIL  cards_db.json load");
            }
            else
            {
                var ids = CollectLabIds();
                var full = 0;
                var partial = 0;
                var emptyEffect = 0;
                foreach (var id in ids)
                {
                    var def = db.Get(id);
                    if (def == null)
                    {
                        report.UnitFail++;
                        sb.AppendLine($"FAIL  missing card {id}");
                        continue;
                    }

                    var prog = CardTextEffectCompiler.Compile(def);
                    if (OfficialCardAuthority.HasNoActivatableEffect(def) || prog.FullyCompiled)
                        full++;
                    else if (prog.ClauseList.Count > 0)
                        partial++;
                    else if (!string.IsNullOrWhiteSpace(OfficialCardAuthority.OfficialText(def)))
                        emptyEffect++;
                }

                report.TextProgramsFull = full;
                report.TextProgramsPartial = partial;
                sb.AppendLine(
                    $"PASS  lab unique={ids.Count} full={full} partial={partial} emptyEffect={emptyEffect}");
                // Soft: empty effects for unregistered text is OK (legacy path)
            }

            // ── 3) Many lab AI vs AI duels ──
            sb.AppendLine();
            sb.AppendLine("── Stress: lab AI vs AI ──");
            if (db != null && db.Count > 0)
            {
                var pDeck = CardDatabase.LoadDeck("lab_rules_player.json");
                var aDeck = CardDatabase.LoadDeck("lab_rules_ai.json");
                if (pDeck == null || aDeck == null)
                {
                    report.UnitFail++;
                    sb.AppendLine("FAIL  lab decks missing");
                }
                else
                {
                    // Silence log spam during bulk games
                    var prevDelay = SimpleAi.StepDelay;
                    SimpleAi.StepDelay = 0f;
                    try
                    {
                        for (var g = 0; g < duelCount; g++)
                        {
                            // Deterministic shuffle variety
                            UnityEngine.Random.InitState(1000 + g * 17);
                            try
                            {
                                var one = PlayOneDuel(db, pDeck, aDeck, g);
                                report.DuelsPlayed++;
                                report.SoftLocksRecovered += one.SoftLocks;
                                if (one.Exception)
                                {
                                    report.Exceptions++;
                                    sb.AppendLine($"FAIL  duel#{g}: exception — {one.Note}");
                                }
                                else if (one.TurnCapped)
                                {
                                    report.DuelsTurnCapped++;
                                    report.DrawsOrCap++;
                                    sb.AppendLine(
                                        $"WARN  duel#{g}: turn cap @{one.Turns} LP {one.PlayerLp}-{one.OppLp}");
                                }
                                else
                                {
                                    report.DuelsCompleted++;
                                    if (one.PlayerWon) report.PlayerWins++;
                                    else if (one.OppWon) report.OppWins++;
                                    else report.DrawsOrCap++;
                                }

                                if (one.SoftLocks > 0)
                                    sb.AppendLine(
                                        $"FAIL  duel#{g}: soft-lock recoveries={one.SoftLocks} — {one.Note}");
                            }
                            catch (Exception ex)
                            {
                                report.Exceptions++;
                                report.DuelsPlayed++;
                                sb.AppendLine($"FAIL  duel#{g}: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        SimpleAi.StepDelay = prevDelay;
                    }

                    sb.AppendLine(
                        $"duels played={report.DuelsPlayed} completed={report.DuelsCompleted} " +
                        $"turnCap={report.DuelsTurnCapped} softLocks={report.SoftLocksRecovered} " +
                        $"exceptions={report.Exceptions}");
                    sb.AppendLine(
                        $"wins: player={report.PlayerWins} opp={report.OppWins} draw/cap={report.DrawsOrCap}");
                }
            }

            // ── 4) Battle math fuzz ──
            sb.AppendLine();
            sb.AppendLine("── Fuzz: battle math ──");
            var fuzzFail = 0;
            for (var i = 0; i < 200; i++)
            {
                var atk = 100 + (i * 37) % 4000;
                var defA = 100 + (i * 53) % 4000;
                var defD = 100 + (i * 71) % 4000;
                var a = Mock(atk, defA, BattlePosition.Attack, true);
                var d = Mock(defA, defD, i % 2 == 0 ? BattlePosition.Defense : BattlePosition.Attack, true);
                var r = BattleMechanics.Calculate(a, d, false, false, false, false, false);
                BattleMechanics.Sanitize(ref r, a, d);
                if (r.DamageToAttackingPlayer < 0 || r.DamageToDefendingPlayer < 0)
                    fuzzFail++;
                if (d.Position == BattlePosition.Defense && r.DestroyDefender && atk <= defD)
                    fuzzFail++; // illegal destroy of higher/equal DEF
            }

            if (fuzzFail == 0)
            {
                report.UnitPass++;
                sb.AppendLine("PASS  200 battle fuzz cases");
            }
            else
            {
                report.UnitFail += fuzzFail;
                sb.AppendLine($"FAIL  battle fuzz failures={fuzzFail}");
            }

            sw.Stop();
            report.ElapsedMs = sw.ElapsedMilliseconds;
            sb.AppendLine();
            sb.AppendLine(
                $"── RESULT: unitPass={report.UnitPass} unitFail={report.UnitFail} " +
                $"exceptions={report.Exceptions} softLocks={report.SoftLocksRecovered} " +
                $"ok={(report.Ok ? "YES" : "NO")} elapsedMs={report.ElapsedMs} ──");
            report.Summary = sb.ToString();

            if (report.Ok)
                Debug.Log("[WRLDZ STRESS]\n" + report.Summary);
            else
                Debug.LogError("[WRLDZ STRESS]\n" + report.Summary);
            return report;
        }

        struct OneDuel
        {
            public int Turns;
            public int SoftLocks;
            public bool TurnCapped;
            public bool Exception;
            public bool PlayerWon;
            public bool OppWon;
            public int PlayerLp;
            public int OppLp;
            public string Note;
        }

        static OneDuel PlayOneDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, int seed)
        {
            var result = new OneDuel();
            var engine = new DuelEngine();
            engine.StartDuel(db, pDeck, aDeck);

            // Integrity: starting hands
            if (engine.Player.Hand.Count != TcgRules.StartingHandSize ||
                engine.Opponent.Hand.Count != TcgRules.StartingHandSize)
            {
                result.Exception = true;
                result.Note = $"bad starting hand sizes {engine.Player.Hand.Count}/{engine.Opponent.Hand.Count}";
                return result;
            }

            if (engine.Player.LifePoints != TcgRules.StartingLifePoints ||
                engine.Opponent.LifePoints != TcgRules.StartingLifePoints)
            {
                result.Exception = true;
                result.Note = "bad starting LP";
                return result;
            }

            var stuckSameTurn = 0;
            var lastTurn = -1;
            var lastPhase = DuelPhase.GameOver;
            var lastStatus = "";

            for (var step = 0; step < MaxTurnsPerDuel * 4 && !engine.GameOver; step++)
            {
                result.Turns = engine.TurnNumber;
                var who = engine.TurnPlayer;
                if (who == null)
                {
                    result.SoftLocks++;
                    result.Note = "null TurnPlayer";
                    break;
                }

                // Soft-lock detection: same turn+phase+status after full play attempt
                var status = $"{engine.TurnNumber}:{engine.Phase}:{engine.Player.LifePoints}:{engine.Opponent.LifePoints}:" +
                             $"{engine.IsAwaitingResponse}:{engine.IsAwaitingEffectTarget}:{engine.HasDeclaredAttack}";
                if (status == lastStatus)
                {
                    stuckSameTurn++;
                    if (stuckSameTurn >= SoftLockStepsThreshold)
                    {
                        result.SoftLocks++;
                        result.Note = $"soft-lock at {status}";
                        // attempt recovery then force end
                        engine.RecoverStuckCombat();
                        DuelStressAgent.DrainWindows(engine);
                        if (!engine.GameOver && engine.TurnPlayer == who)
                            engine.TryEndTurnSafe(who);
                        if (status ==
                            $"{engine.TurnNumber}:{engine.Phase}:{engine.Player.LifePoints}:{engine.Opponent.LifePoints}:" +
                            $"{engine.IsAwaitingResponse}:{engine.IsAwaitingEffectTarget}:{engine.HasDeclaredAttack}")
                            break;
                        stuckSameTurn = 0;
                    }
                }
                else
                {
                    stuckSameTurn = 0;
                    lastStatus = status;
                }

                try
                {
                    DuelStressAgent.DrainWindows(engine);
                    if (engine.GameOver) break;
                    DuelStressAgent.PlayTurn(engine, engine.TurnPlayer);
                    DuelStressAgent.DrainWindows(engine);
                }
                catch (Exception ex)
                {
                    result.Exception = true;
                    result.Note = ex.Message;
                    return result;
                }

                // LP integrity
                if (engine.Player.LifePoints < 0 || engine.Opponent.LifePoints < 0)
                {
                    result.Exception = true;
                    result.Note = "negative LP";
                    return result;
                }

                // Hand size sanity (deckout draws may empty deck first)
                if (engine.Player.Hand.Count > 30 || engine.Opponent.Hand.Count > 30)
                {
                    result.Exception = true;
                    result.Note = "absurd hand size";
                    return result;
                }

                if (engine.TurnNumber > MaxTurnsPerDuel)
                {
                    result.TurnCapped = true;
                    break;
                }

                if (engine.TurnNumber == lastTurn && engine.Phase == lastPhase && stuckSameTurn > 8)
                {
                    // force recovery path
                    engine.RecoverStuckCombat();
                    result.SoftLocks += engine.RecoverStuckCombat() ? 1 : 0;
                }

                lastTurn = engine.TurnNumber;
                lastPhase = engine.Phase;
            }

            result.PlayerLp = engine.Player.LifePoints;
            result.OppLp = engine.Opponent.LifePoints;
            if (engine.GameOver)
            {
                result.PlayerWon = engine.Winner == engine.Player;
                result.OppWon = engine.Winner == engine.Opponent;
            }
            else if (!result.TurnCapped && result.SoftLocks == 0)
            {
                result.TurnCapped = true;
            }

            return result;
        }

        static HashSet<int> CollectLabIds()
        {
            var ids = new HashSet<int>();
            foreach (var file in new[] { "lab_rules_player.json", "lab_rules_ai.json" })
            {
                var deck = CardDatabase.LoadDeck(file);
                if (deck == null) continue;
                void Add(DeckCardEntry[] arr)
                {
                    if (arr == null) return;
                    foreach (var e in arr)
                        if (e != null && e.id > 0) ids.Add(e.id);
                }

                Add(deck.main);
                Add(deck.extra);
                Add(deck.side);
            }

            return ids;
        }

        static void ParseUnitCounts(string unit, out int pass, out int fail)
        {
            pass = 0;
            fail = 0;
            if (string.IsNullOrEmpty(unit)) return;
            foreach (var line in unit.Split('\n'))
            {
                if (line.StartsWith("PASS", StringComparison.Ordinal)) pass++;
                else if (line.StartsWith("FAIL", StringComparison.Ordinal)) fail++;
            }
        }

        static CardInstance Mock(int atk, int def, BattlePosition pos, bool faceUp) =>
            new()
            {
                InstanceId = atk * 10 + def,
                CardId = 1,
                FaceUp = faceUp,
                Position = pos,
                Def = new CardDef
                {
                    id = 1,
                    name = $"Fuzz{atk}/{def}",
                    type = "Effect Monster",
                    atk = atk,
                    def = def,
                    level = 4
                }
            };
    }
}
