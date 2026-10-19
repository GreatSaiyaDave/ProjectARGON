using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// QC group "OncePerTurnSoft" (v211): Implemented cards whose text says "Once per turn" with no clause setting
    /// <see cref="EffectClause.OncePerTurn"/> (~/.cache/wrldz-card-compile/v211-tlm-fet.notes/OncePerTurnGap.md, SOFT list).
    /// Per card: a phase trigger (ygopro EVENT_PHASE + PHASE_STANDBY / PHASE_END, count limit 1) can only happen once per
    /// turn anyway — the engine fires each phase's triggers once per phase pass (DuelEngine.BeginTurn → FirePhaseTriggers
    /// StandbyPhase; DuelEngine.EndTurn → FirePhaseTriggers EndPhase), so the printed "Once per turn" is a restatement and
    /// the mini-duels below prove exactly one application per phase and per copy. Dice Re-Roll's "(You can only gain this
    /// effect once per turn.)" is a player flag kept by CoinDiceInterruptsRuntime (ygopro RegisterFlagEffect(tp, id)).
    /// Sasuke Samurai #2 is an ignition that could repeat: its clause now sets OncePerTurn (PerInstance), on top of its
    /// ygopro player flag. <see cref="InherentOncePerTurn"/> is the list the OncePerTurnHard corpus guard exempts.
    /// </summary>
    public static class OncePerTurnSoftRegressionTests
    {
        // This group.
        const int Tsukuyomi = 34853266;           // Spirit, Spellcaster Lv4 1100/1400
        const int ExodiaNecross = 12600382;       // Spellcaster Lv4 1800/0
        const int DiceReRoll = 83241722;          // Normal Trap
        const int VampireLord = 53839837;         // Zombie Lv5 2000/1500
        const int Sasuke2 = 11760174;             // Warrior Lv1 200/300
        const int Pumpking = 29155212;            // Zombie Lv6 1800/2000
        const int Bowganian = 52090844;           // Machine Lv3 1300/1000
        const int LavaGolem = 102380;             // Fiend Lv8 3000/2500

        // The rest of the notes' SOFT list.
        const int BlindDestruction = 32015116;
        const int DarklordMarie = 57579381;
        const int Ectoplasmer = 97342942;
        const int ImperialOrder = 61740673;
        const int LabyrinthOfNightmare = 66526672;
        const int LegendaryFiend = 99747800;
        const int MaskOfTheAccursed = 56948373;
        const int MessengerOfPeace = 44656491;
        const int MirageOfNightmare = 41482598;
        const int NeedleWall = 38299233;
        const int NightmareWheel = 54704216;
        const int OminousFortunetelling = 56995655;

        // Props.
        const int Exodia = 33396948;
        const int RightArm = 70903634;
        const int LeftArm = 7902349;
        const int RightLeg = 8124921;
        const int LeftLeg = 44519536;
        const int Castle = 62121;                 // Castle of Dark Illusions
        const int Fissure = 66788016;
        const int DarkHole = 53129443;
        const int GracefulDice = 74137509;
        const int PotOfGreed = 55144522;
        const int JarOfGreed = 83968380;
        const int Celtic = 91152256;              // Warrior Lv4 1400/1200
        const int MysticalElf = 15025844;         // Spellcaster Lv4 800/2000
        const int Freed = 49681811;               // Freed the Matchless General: holds a human's Draw Phase

        /// <summary>
        /// Cards whose printed "once per turn" sentence compiles with no <see cref="EffectClause.OncePerTurn"/> because the
        /// limit is inherent: a Standby / End Phase trigger (one phase pass per turn) or a limit the card's runtime keeps
        /// (Dice Re-Roll's player flag, Vampire Lord's revival flag, Pumpking's per-turn growth step). The OncePerTurnHard
        /// corpus guard exempts exactly these passcodes; every other FullyCompiled "once per turn" card must set the flag.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, string> InherentOncePerTurn = new Dictionary<int, string>
        {
            { Tsukuyomi, "End Phase Spirit return (Lua Spirit.AddProcedure, EVENT_PHASE+PHASE_END)" },
            { ExodiaNecross, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { DiceReRoll, "player flag: CoinDiceInterruptsRuntime.DiceReRollUsedTurn (Lua RegisterFlagEffect(tp, id))" },
            { VampireLord, "Standby Phase trigger from the GY (+ CardInstance.OncePerTurnStandbyRevivalUsed)" },
            { Pumpking, "Standby Phase growth step (StandbyGrowthGrant.LastStepTurn; Lua count limit 1)" },
            { Bowganian, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { LavaGolem, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE+PHASE_STANDBY, count limit 1)" },
            { BlindDestruction, "Standby Phase trigger (Lua TRIGGER_F, count limit 1)" },
            { DarklordMarie, "Standby Phase trigger from the GY (Lua TRIGGER_F, count limit 1)" },
            { Ectoplasmer, "End Phase trigger (Lua TRIGGER_F EVENT_PHASE+PHASE_END, count limit 1)" },
            { ImperialOrder, "Standby Phase maintenance (Lua CONTINUOUS EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { LabyrinthOfNightmare, "End Phase trigger (Lua TRIGGER_F EVENT_PHASE+PHASE_END, count limit 1)" },
            { LegendaryFiend, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { MaskOfTheAccursed, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE+PHASE_STANDBY, count limit 1)" },
            { MessengerOfPeace, "Standby Phase maintenance (Lua CONTINUOUS EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { MirageOfNightmare, "Standby Phase triggers (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { NeedleWall, "Standby Phase trigger (Lua TRIGGER_F, count limit 1)" },
            { NightmareWheel, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
            { OminousFortunetelling, "Standby Phase trigger (Lua TRIGGER_F EVENT_PHASE|PHASE_STANDBY, count limit 1)" },
        };

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("OncePerTurnSoft: " + name, ok, detail);

            void Guard(string name, Action body)
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    Check(name + " threw", false, ex.GetType().Name + ": " + ex.Message);
                }
            }

            Guard("inherent list", () => InherentListShapes(db, Check));
            Guard("sasuke shape", () => SasukeShape(db, Check));
            Guard("sasuke duel", () => SasukeDuels(db, pDeck, aDeck, Check));
            Guard("standby burns", () => StandbyBurnsOncePerCopy(db, pDeck, aDeck, Check));
            Guard("lava golem", () => LavaGolemOncePerOwnStandby(db, pDeck, aDeck, Check));
            Guard("atk growth", () => StandbyAtkGrowthOnce(db, pDeck, aDeck, Check));
            Guard("vampire lord", () => VampireLordRevivesOnce(db, pDeck, aDeck, Check));
            Guard("vampire lord near miss", () => VampireLordOwnEffectStays(db, pDeck, aDeck, Check));
            Guard("vampire lord draw phase", () => VampireLordDrawPhaseDestroy(db, pDeck, aDeck, Check));
            Guard("pumpking", () => PumpkingStepOncePerTurn(db, pDeck, aDeck, Check));
            Guard("tsukuyomi", () => TsukuyomiReturnsOnce(db, pDeck, aDeck, Check));
            Guard("dice re-roll", () => DiceReRollOncePerTurn(db, pDeck, aDeck, Check));
            Guard("upkeeps", () => UpkeepsOncePerStandby(db, pDeck, aDeck, Check));
            Guard("labyrinth", () => LabyrinthOncePerEndPhase(db, pDeck, aDeck, Check));
            Guard("ectoplasmer", () => EctoplasmerOncePerEndPhase(db, pDeck, aDeck, Check));
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static string Describe(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"full={p.FullyCompiled} " + string.Join(" | ", p.ClauseList.Select(c =>
                    $"{c.Timing}/{c.Action} opt={c.OncePerTurn}/{c.OptScope}"));

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++)
                e.PassResponse();
        }

        /// <summary>From any point, play on to <paramref name="who"/>'s next Main Phase 1 (a later turn).</summary>
        static bool NextMain(DuelEngine e, DuelistState who)
        {
            var start = e.TurnNumber;
            for (var i = 0; i < 14; i++)
            {
                PassAll(e);
                e.RecoverStuckCombat();
                if (e.TurnPlayer == who && e.TurnNumber > start && e.Phase == DuelPhase.Main1) return true;
                if (e.Phase == DuelPhase.Standby)
                {
                    e.ContinueAfterStandby();
                    continue;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            PassAll(e);
            return e.TurnPlayer == who && e.TurnNumber > start && e.Phase == DuelPhase.Main1;
        }

        /// <summary>Play on to <paramref name="who"/>'s Main Phase 1 of the current turn (through a held Standby Phase).</summary>
        static bool MainNow(DuelEngine e, DuelistState who)
        {
            for (var i = 0; i < 6; i++)
            {
                PassAll(e);
                if (e.TurnPlayer == who && e.Phase == DuelPhase.Main1) return true;
                if (e.Phase == DuelPhase.Standby && !e.ContinueAfterStandby()) break;
            }

            PassAll(e);
            return e.TurnPlayer == who && e.Phase == DuelPhase.Main1;
        }

        /// <summary>The opponent's Main Phase of turn 2, both boards, hands and GYs emptied.</summary>
        static DuelEngine OpponentMainCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachOpponentMain(e);
            Empty(e);
            return e;
        }

        static void Empty(DuelEngine e)
        {
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
                who.LifePoints = 8000;
            }
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static string Where(DuelEngine e) => $"turn={e.TurnNumber} {e.TurnPlayer?.Name} {e.Phase} " +
                                              $"pick={e.IsAwaitingEffectTarget} response={e.IsAwaitingResponse} " +
                                              $"links={e.Chain.Count}";

        // ───────────────────────────── the inherent list ─────────────────────────────

        /// <summary>
        /// Every listed card is Implemented, prints "once per turn", sets no OncePerTurn, and the sentence lives on a
        /// Standby / End Phase clause (Dice Re-Roll: its Activate grant, limited by the player flag). None has a monster
        /// ignition (an Activate clause on a monster) that could repeat without the flag.
        /// </summary>
        static void InherentListShapes(CardDatabase db, Chk Check)
        {
            var bad = new List<string>();
            foreach (var kv in InherentOncePerTurn)
            {
                var def = db.Get(kv.Key);
                var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var name = def?.name ?? kv.Key.ToString();
                if (def == null || p == null || !Implemented(db, kv.Key))
                {
                    bad.Add(name + ": not Implemented");
                    continue;
                }

                if ((def.desc ?? "").IndexOf("once per turn", StringComparison.OrdinalIgnoreCase) < 0)
                    bad.Add(name + ": no 'once per turn' in its text");
                if (p.ClauseList.Exists(c => c != null && c.OncePerTurn))
                    bad.Add(name + ": sets OncePerTurn (then it does not belong on the exemption list)");
                var phase = p.ClauseList.Exists(c => c != null &&
                    (c.Timing == EffectTiming.StandbyPhase || c.Timing == EffectTiming.EndPhase));
                var pumpking = p.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby);
                var diceGrant = p.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.GrantDiceReRollThisTurn);
                if (!phase && !pumpking && !diceGrant)
                    bad.Add(name + ": no Standby / End Phase clause — " + Describe(p));
                if (def.IsMonster && p.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate))
                    bad.Add(name + ": a monster ignition without the flag — " + Describe(p));
            }

            Check("Inherent once-per-turn list: 19 cards, each Implemented with its once-per-turn sentence on a phase " +
                  "trigger (or a runtime-kept limit), no OncePerTurn, no repeatable ignition",
                InherentOncePerTurn.Count == 19 && bad.Count == 0, string.Join("; ", bad));
            Check("Inherent once-per-turn list does not exempt Sasuke Samurai #2 (its ignition sets the flag)",
                !InherentOncePerTurn.ContainsKey(Sasuke2));
        }

        // ───────────────────────────── Sasuke Samurai #2 ─────────────────────────────

        static void SasukeShape(CardDatabase db, Chk Check)
        {
            var p = Prog(db, Sasuke2);
            var k = p != null && p.FullyCompiled && p.ClauseList.Count == 1 ? p.ClauseList[0] : null;
            Check("Sasuke Samurai #2: its ignition sets OncePerTurn (PerInstance) beside the player-flag condition",
                Implemented(db, Sasuke2) && k != null && k.Timing == EffectTiming.Activate &&
                k.Action == EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn && k.PayLpAmount == 800 &&
                k.OncePerTurn && k.OptScope == OncePerTurnScope.PerInstance &&
                k.ActivationCondition == SpellTrapAnswersAndActivationLocksRuntime.TurnLockNotAppliedByYouThisTurn,
                Describe(p));

            // Near miss: the same sentence without "Once per turn, " is not this atom (no flagless twin compiles).
            var text = db.Get(Sasuke2)?.desc ?? "";
            var bare = text.Replace("Once per turn, during your Main Phase, you", "During your Main Phase, you");
            var near = CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990011760, name = "Near Miss Samurai", type = "Effect Monster", frameType = "effect", race = "Warrior",
                attribute = "WIND", level = 1, atk = 200, def = 300, desc = bare
            });
            Check("Near miss: Sasuke Samurai #2's text without 'Once per turn' does not compile to its lock",
                bare != text && (near == null || !near.FullyCompiled ||
                                 !near.ClauseList.Exists(c => c != null &&
                                     c.Action == EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn)),
                Describe(near));
        }

        /// <summary>The player's Main Phase of turn 3, boards emptied.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            var r1 = ReachOpponentMain(e);
            reached = r1 && NextMain(e, e.Player);
            Empty(e);
            return e;
        }

        static void SasukeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // Resolved: the copy is used for this turn (EffectUsedThisTurn), the activation gate refuses it.
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var sasuke = PlaceMonster(e, p, Sasuke2, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = p.LifePoints;
                var act = reached && e.CanActivateSpellTrap(p, sasuke, fromHand: false) &&
                          e.TryActivateSpellTrap(p, sasuke, fromHand: false);
                PassAll(e);
                var used = sasuke.EffectUsedThisTurn;
                var again = e.CanActivateSpellTrap(p, sasuke, fromHand: false);
                var again2 = e.TryActivateSpellTrap(p, sasuke, fromHand: false);
                PassAll(e);
                Check("Sasuke Samurai #2: once per turn — the copy is marked used; a second activation is refused " +
                      "and costs nothing",
                    act && used && !again && !again2 && p.LifePoints == lp - 800,
                    $"reached={reached} act={act} used={used} again={again}/{again2} lp {lp}->{p.LifePoints} {Where(e)}");

                var next = NextMain(e, e.Opponent) && NextMain(e, p);
                var reset = next && !sasuke.EffectUsedThisTurn && e.CanActivateSpellTrap(p, sasuke, fromHand: false);
                Check("Sasuke Samurai #2: the limit is per turn — usable again in the player's next turn",
                    reset, $"next={next} used={sasuke.EffectUsedThisTurn} {Where(e)}");
            }
        }

        // ───────────────────────────── Standby Phase triggers ─────────────────────────────

        /// <summary>
        /// Two Bowganians: 600 each in the player's Standby Phase (soft: per copy), nothing in the opponent's Standby Phase,
        /// and 1200 again only in the player's next Standby Phase — one application per copy per turn.
        /// </summary>
        static void StandbyBurnsOncePerCopy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var b1 = PlaceMonster(e, p, Bowganian, 0, BattlePosition.Attack, true);
            var b2 = PlaceMonster(e, p, Bowganian, 1, BattlePosition.Attack, true);
            e.NotifyPublic();
            var mine = reached && NextMain(e, p);
            var afterMine = o.LifePoints;
            var theirs = mine && NextMain(e, o);
            var afterTheirs = o.LifePoints;
            var mine2 = theirs && NextMain(e, p);
            var afterMine2 = o.LifePoints;
            Check("Bowganian x2: 600 per copy once in each of the player's Standby Phases (1200), none in the opponent's",
                mine && theirs && mine2 && afterMine == 8000 - 1200 && afterTheirs == afterMine &&
                afterMine2 == afterMine - 1200 && p.LifePoints == 8000 &&
                p.TryFindMonster(b1, out _) && p.TryFindMonster(b2, out _),
                $"mine={mine} theirs={theirs} mine2={mine2} oLp 8000->{afterMine}->{afterTheirs}->{afterMine2} " +
                $"pLp={p.LifePoints} {Where(e)}");
        }

        static void LavaGolemOncePerOwnStandby(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var golem = PlaceMonster(e, p, LavaGolem, 0, BattlePosition.Attack, true);
            e.NotifyPublic();
            var mine = reached && NextMain(e, p);
            var afterMine = p.LifePoints;
            var theirs = mine && NextMain(e, o);
            var afterTheirs = p.LifePoints;
            Check("Lava Golem: its controller takes 1000 once in their Standby Phase, nothing in the opponent's",
                mine && theirs && afterMine == 7000 && afterTheirs == 7000 && o.LifePoints == 8000 &&
                p.TryFindMonster(golem, out _),
                $"mine={mine} theirs={theirs} pLp 8000->{afterMine}->{afterTheirs} oLp={o.LifePoints} {Where(e)}");
        }

        /// <summary>Exodia Necross (+500, the five pieces in the GY) and Legendary Fiend (+700): one gain per Standby.</summary>
        static void StandbyAtkGrowthOnce(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            foreach (var id in new[] { Exodia, RightArm, LeftArm, RightLeg, LeftLeg })
                p.Graveyard.Add(e.CreateCardInstance(id));
            var necross = PlaceMonster(e, p, ExodiaNecross, 0, BattlePosition.Attack, true);
            var fiend = PlaceMonster(e, p, LegendaryFiend, 1, BattlePosition.Attack, true);
            e.NotifyPublic();
            var n0 = necross.CurrentAtk;
            var f0 = fiend.CurrentAtk;
            var mine = reached && NextMain(e, p);
            var n1 = necross.CurrentAtk;
            var f1 = fiend.CurrentAtk;
            var theirs = mine && NextMain(e, o);
            var n2 = necross.CurrentAtk;
            var f2 = fiend.CurrentAtk;
            Check("Exodia Necross +500 / Legendary Fiend +700: once in the player's Standby Phase, not in the opponent's",
                mine && theirs && n0 == 1800 && n1 == 2300 && n2 == 2300 && f1 == f0 + 700 && f2 == f1 &&
                p.TryFindMonster(necross, out _) && p.TryFindMonster(fiend, out _),
                $"mine={mine} theirs={theirs} necross {n0}->{n1}->{n2} fiend {f0}->{f1}->{f2} {Where(e)}");
        }

        /// <summary>
        /// The opponent's Fissure destroys Vampire Lord; in the player's next Standby Phase it comes back once (a human taps
        /// it and picks its position; it leaves the GY, so the GY trigger cannot fire again), nothing in the opponent's
        /// Standby Phase. The AI's Vampire Lord destroyed by the player's Fissure comes back by itself.
        /// </summary>
        static void VampireLordRevivesOnce(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lord = PlaceMonster(e, p, VampireLord, 0, BattlePosition.Attack, true);
                var fissure = PutInHand(e, o, Fissure);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(o, fissure, fromHand: true);
                PassAll(e);
                var destroyed = p.Graveyard.Contains(lord) && MfcLabCards3Runtime.DestroyedByOpponentEffect(p, lord);
                var ended = destroyed && e.TryEndTurnSafe(o);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                            ReferenceEquals(e.PendingActivation.Card, lord) && e.Phase == DuelPhase.Standby &&
                            e.TurnPlayer == p;
                var tapped = false;
                if (asked)
                {
                    e.SetPendingSpecialSummonPlacement(2, BattlePosition.Attack);
                    tapped = e.TrySelectEffectTarget(lord);
                }

                var mine = MainNow(e, p);
                var back = mine && p.TryFindMonster(lord, out _) && lord.FaceUp &&
                           lord.Position == BattlePosition.Attack && !p.Graveyard.Contains(lord);
                var copies = p.MonstersOnField().Count(m => m != null && m.CardId == VampireLord) +
                             p.Graveyard.Count(c => c != null && c.CardId == VampireLord);
                Check("Vampire Lord: destroyed by the opponent's Fissure, Special Summoned once (from the GY, the player's " +
                      "chosen position) in the player's next Standby Phase",
                    act && destroyed && ended && asked && tapped && back && copies == 1,
                    $"act={act} destroyed={destroyed} ended={ended} asked={asked} tapped={tapped} mine={mine} " +
                    $"back={back} pos={lord.Position} copies={copies} reason={lord.GyEntryReason} {Where(e)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var lord = PlaceMonster(e, o, VampireLord, 0, BattlePosition.Attack, true);
                var fissure = PutInHand(e, p, Fissure);
                e.NotifyPublic();
                var act = reached && e.TryActivateSpellTrap(p, fissure, fromHand: true);
                PassAll(e);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(lord);
                PassAll(e);
                var destroyed = o.Graveyard.Contains(lord);
                var theirs = destroyed && NextMain(e, o);
                Check("Vampire Lord (AI): destroyed by the player's Fissure, the AI's copy comes back in its Standby Phase",
                    act && destroyed && theirs && o.TryFindMonster(lord, out _) && !o.Graveyard.Contains(lord),
                    $"act={act} destroyed={destroyed} theirs={theirs} onField={o.TryFindMonster(lord, out _)} {Where(e)}");
            }
        }

        /// <summary>Near miss: Vampire Lord destroyed by its own controller's Dark Hole does not come back.</summary>
        static void VampireLordOwnEffectStays(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var lord = PlaceMonster(e, p, VampireLord, 0, BattlePosition.Attack, true);
            var hole = PutInHand(e, p, DarkHole);
            e.NotifyPublic();
            var act = reached && e.TryActivateSpellTrap(p, hole, fromHand: true);
            PassAll(e);
            var inGy = p.Graveyard.Contains(lord);
            var byOpp = MfcLabCards3Runtime.DestroyedByOpponentEffect(p, lord);
            var mine = inGy && NextMain(e, e.Opponent) && NextMain(e, p);
            Check("Near miss: Vampire Lord destroyed by its controller's own Dark Hole stays in the GY",
                act && inGy && !byOpp && mine && p.Graveyard.Contains(lord) && !p.TryFindMonster(lord, out _),
                $"act={act} inGy={inGy} byOpp={byOpp} mine={mine} reason={lord.GyEntryReason} {Where(e)}");
        }

        /// <summary>
        /// Destroyed by the opponent's card effect during the player's own Draw Phase (held by Freed's Deck pick): ygopro
        /// s.spr is not in the Standby Phase, so label 0 and a flag that ends with this turn's Standby Phase — Vampire Lord
        /// comes back in that same turn's Standby Phase. When that revival cannot happen (no free Monster Zone), it does not
        /// come back in the player's following turn either (the flag is gone).
        /// </summary>
        static void VampireLordDrawPhaseDestroy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var blocked in new[] { false, true })
            {
                var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                PlaceMonster(e, p, Freed, 0, BattlePosition.Attack, true);
                var lord = PlaceMonster(e, p, VampireLord, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);
                p.Deck.Clear();
                p.Deck.AddRange(new[] { Celtic, Celtic, Celtic, Celtic, Celtic });
                e.NotifyPublic();
                var ended = reached && e.TryEndTurnSafe(o);
                PassAll(e);
                var held = ended && e.TurnPlayer == p && e.Phase == DuelPhase.Draw && e.IsAwaitingEffectTarget;
                var turn = e.TurnNumber;
                if (held) e.DestroyMonsterPublic(p, lord, elf);
                var destroyed = held && p.Graveyard.Contains(lord) && lord.GyEntryPhase == DuelPhase.Draw &&
                                MfcLabCards3Runtime.DestroyedByOpponentEffect(p, lord);
                var fillers = new System.Collections.Generic.List<CardInstance>();
                if (blocked && destroyed)
                    for (var z = 1; z < 5; z++)
                        fillers.Add(PlaceMonster(e, p, Celtic, z, BattlePosition.Attack, true));
                if (held) e.CancelEffectTargeting();
                PassAll(e);
                if (!blocked)
                {
                    var asked = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                                ReferenceEquals(e.PendingActivation.Card, lord) && e.Phase == DuelPhase.Standby &&
                                e.TurnPlayer == p && e.TurnNumber == turn;
                    var tapped = false;
                    if (asked)
                    {
                        e.SetPendingSpecialSummonPlacement(2, BattlePosition.Attack);
                        tapped = e.TrySelectEffectTarget(lord);
                    }

                    var mine = MainNow(e, p);
                    Check("Vampire Lord: destroyed by the opponent's effect in the player's own Draw Phase, it comes back in " +
                          "that same turn's Standby Phase",
                        destroyed && asked && tapped && mine && e.TurnNumber == turn && p.TryFindMonster(lord, out _) &&
                        !p.Graveyard.Contains(lord),
                        $"held={held} destroyed={destroyed} phase={lord.GyEntryPhase} asked={asked} tapped={tapped} " +
                        $"mine={mine} turn={turn}->{e.TurnNumber} {Where(e)}");
                }
                else
                {
                    var mine = MainNow(e, p);
                    var stayed = mine && p.Graveyard.Contains(lord) && !p.TryFindMonster(lord, out _);
                    if (mine && fillers.Count > 0) e.DestroyMonsterPublic(p, fillers[0]);
                    // No Warrior left in the Deck: Freed does not hold the next Draw Phase.
                    p.Deck.Clear();
                    p.Deck.AddRange(new[] { MysticalElf, MysticalElf, MysticalElf, MysticalElf, MysticalElf });
                    var later = stayed && NextMain(e, p);
                    Check("Near miss: Vampire Lord destroyed in the player's Draw Phase, with no free Monster Zone in that " +
                          "Standby Phase, does not come back in the player's next turn",
                        destroyed && fillers.Count == 4 && stayed && later && e.TurnNumber == turn + 2 &&
                        p.Graveyard.Contains(lord) && !p.TryFindMonster(lord, out _),
                        $"held={held} destroyed={destroyed} fillers={fillers.Count} stayed={stayed} later={later} " +
                        $"turn={turn}->{e.TurnNumber} {Where(e)}");
                }
            }
        }

        /// <summary>Pumpking's growth step is kept once per turn by its runtime: a second tick in the same turn adds nothing.</summary>
        static void PumpkingStepOncePerTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var pk = PlaceMonster(e, p, Pumpking, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, Castle, 1, BattlePosition.Defense, true);
            e.NotifyPublic();
            var mine = reached && NextMain(e, p);
            var a1 = pk.CurrentAtk;
            StandbyGrowthAndMaintenanceRuntime.TickStandby(e, p);
            e.NotifyPublic();
            var a2 = pk.CurrentAtk;
            Check("Pumpking: +100 in the player's Standby Phase (2000); a second Standby tick in the same turn adds nothing",
                mine && a1 == 2000 && a2 == 2000 && pk.CurrentDef == 2200, $"mine={mine} atk {a1}->{a2} {Where(e)}");
        }

        // ───────────────────────────── End Phase ─────────────────────────────

        /// <summary>Tsukuyomi Normal Summoned (it turns the opponent's monster face-down) goes back to the hand once.</summary>
        static void TsukuyomiReturnsOnce(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var foe = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var tsu = PutInHand(e, p, Tsukuyomi);
            e.NotifyPublic();
            var summoned = reached && e.TryNormalSummon(p, tsu, asSet: false);
            PassAll(e);
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(foe);
            PassAll(e);
            var flipped = !foe.FaceUp && foe.Position == BattlePosition.Defense;
            var theirs = summoned && NextMain(e, o);
            var inHand = p.Hand.Count(c => c != null && c.CardId == Tsukuyomi);
            Check("Tsukuyomi: Normal Summoned, sets the opponent's monster; returns to the hand once in the End Phase",
                summoned && flipped && theirs && inHand == 1 && !p.TryFindMonster(tsu, out _),
                $"summoned={summoned} flipped={flipped} theirs={theirs} inHand={inHand} {Where(e)}");
        }

        /// <summary>
        /// Labyrinth of Nightmare: in each End Phase the turn player's face-up monsters change position once (twice would
        /// put them back).
        /// </summary>
        static void LabyrinthOncePerEndPhase(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            FaceUpSpellTrap(e, p, LabyrinthOfNightmare, 0);
            var foe = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
            e.NotifyPublic();
            var mine = reached && NextMain(e, p);
            var foeAfterTheirEnd = foe.Position;
            var elfAfterTheirEnd = elf.Position;
            var theirs = mine && NextMain(e, o);
            Check("Labyrinth of Nightmare: one position change per End Phase, only the turn player's monsters",
                mine && theirs && foeAfterTheirEnd == BattlePosition.Defense && elfAfterTheirEnd == BattlePosition.Attack &&
                elf.Position == BattlePosition.Defense && foe.Position == BattlePosition.Defense,
                $"mine={mine} theirs={theirs} foe={foeAfterTheirEnd}->{foe.Position} elf={elfAfterTheirEnd}->{elf.Position} " +
                $"{Where(e)}");
        }

        /// <summary>
        /// Ectoplasmer: the turn player Tributes exactly one face-up monster per End Phase — the AI on its own, and the
        /// player through the pick that pauses the End Phase (the resumed End Phase does not ask again).
        /// </summary>
        static void EctoplasmerOncePerEndPhase(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, Ectoplasmer, 0);
                var c1 = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var mine = reached && NextMain(e, p);
                var gone = new[] { c1, c2 }.Count(c => o.Graveyard.Contains(c));
                var dmg = 8000 - p.LifePoints;
                var expected = o.Graveyard.Contains(c1) ? 700 : o.Graveyard.Contains(c2) ? 400 : -1;
                Check("Ectoplasmer: the AI turn player Tributes one face-up monster in its End Phase; half its ATK to the player",
                    mine && gone == 1 && dmg == expected, $"mine={mine} gone={gone} dmg={dmg} {Where(e)}");
            }

            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                FaceUpSpellTrap(e, p, Ectoplasmer, 0);
                var c1 = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var c2 = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var ended = reached && e.TryEndTurnSafe(p);
                PassAll(e);
                var asked = e.IsAwaitingEffectTarget && e.PendingActivation != null &&
                            e.PendingActivation.LegalTargets.Contains(c1);
                var picked = asked && e.TrySelectEffectTarget(c1);
                PassAll(e);
                var askedAgain = e.IsAwaitingEffectTarget;
                var theirs = picked && !askedAgain && (e.TurnPlayer == o || NextMain(e, o));
                Check("Ectoplasmer: the player picks one Tribute in their End Phase; the resumed End Phase does not ask " +
                      "again (one Tribute, 700 to the opponent)",
                    ended && asked && picked && !askedAgain && theirs && p.Graveyard.Contains(c1) &&
                    p.TryFindMonster(c2, out _) && o.LifePoints == 8000 - 700,
                    $"ended={ended} asked={asked} picked={picked} again={askedAgain} theirs={theirs} " +
                    $"c1Gy={p.Graveyard.Contains(c1)} c2={p.TryFindMonster(c2, out _)} oLp={o.LifePoints} {Where(e)}");
            }
        }

        // ───────────────────────────── Standby maintenance ─────────────────────────────

        /// <summary>
        /// The AI's Messenger of Peace (pay 100 in its controller's Standby Phase) and Imperial Order (pay 700 in every
        /// Standby Phase): each cost once per Standby Phase.
        /// </summary>
        static void UpkeepsOncePerStandby(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var mop = FaceUpSpellTrap(e, o, MessengerOfPeace, 0);
                e.NotifyPublic();
                var mine = reached && NextMain(e, p);
                var afterMine = o.LifePoints;
                var theirs = mine && NextMain(e, o);
                var afterTheirs = o.LifePoints;
                var kept = o.TryFindSpellTrap(mop, out _);
                Check("Messenger of Peace: 100 once in its controller's Standby Phase (none in the opponent's)",
                    mine && theirs && afterMine == 8000 &&
                    ((kept && afterTheirs == 7900) || (!kept && afterTheirs == 8000)),
                    $"mine={mine} theirs={theirs} oLp 8000->{afterMine}->{afterTheirs} kept={kept} {Where(e)}");
            }

            {
                var e = OpponentMainCleared(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var io = FaceUpSpellTrap(e, o, ImperialOrder, 0);
                e.NotifyPublic();
                var mine = reached && NextMain(e, p);
                var afterMine = o.LifePoints;
                var theirs = mine && NextMain(e, o);
                var afterTheirs = o.LifePoints;
                Check("Imperial Order: 700 once in each Standby Phase (the player's, then its controller's)",
                    mine && theirs && afterMine == 7300 && afterTheirs == 6600 && o.TryFindSpellTrap(io, out _),
                    $"mine={mine} theirs={theirs} oLp 8000->{afterMine}->{afterTheirs} {Where(e)}");
            }
        }

        // ───────────────────────────── Dice Re-Roll ─────────────────────────────

        /// <summary>
        /// "(You can only gain this effect once per turn.)": after the re-roll is used, a second Dice Re-Roll resolved the
        /// same turn gives nothing — the next roll applies at once.
        /// </summary>
        static void DiceReRollOncePerTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            Empty(e);
            var p = e.Player;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var drr = PlaceSetTrap(e, p, DiceReRoll, 0);
            var drr2 = PlaceSetTrap(e, p, DiceReRoll, 1);
            var gd = PutInHand(e, p, GracefulDice);
            var gd2 = PutInHand(e, p, GracefulDice);
            e.NotifyPublic();
            var act = e.TryActivateSpellTrap(p, drr, fromHand: false);
            PassAll(e);
            var granted = act && CoinDiceInterruptsRuntime.HasDiceReRoll(e, p);
            e.Rng.QueueDie(1);
            e.Rng.QueueDie(6);
            e.TryActivateSpellTrap(p, gd, fromHand: true);
            PassAll(e);
            var offered = e.PendingActivation != null && ReferenceEquals(e.PendingActivation.Card, drr);
            var tapped = offered && e.TrySelectEffectTarget(drr);
            PassAll(e);
            var rerolled = celtic.CurrentAtk == 2000;
            var act2 = e.TryActivateSpellTrap(p, drr2, fromHand: false);
            PassAll(e);
            var regained = CoinDiceInterruptsRuntime.HasDiceReRoll(e, p);
            e.Rng.QueueDie(2);
            e.TryActivateSpellTrap(p, gd2, fromHand: true);
            PassAll(e);
            Check("Dice Re-Roll: once used, a second Dice Re-Roll the same turn gives no re-roll (the next roll applies)",
                granted && offered && tapped && rerolled && act2 && !regained && e.PendingActivation == null &&
                celtic.CurrentAtk == 2200 && p.Graveyard.Contains(drr2),
                $"granted={granted} offered={offered} tapped={tapped} rerolled={rerolled} act2={act2} " +
                $"regained={regained} atk={celtic.CurrentAtk} {Where(e)}");
        }
    }
}
