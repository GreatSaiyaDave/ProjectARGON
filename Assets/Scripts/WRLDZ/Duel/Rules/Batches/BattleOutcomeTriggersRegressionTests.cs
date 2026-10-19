using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "BattleOutcomeTriggers" (v162): Zombyra the Dark, Timeater, Kryuel, Mystical Knight of Jackal, Winged Sage
    /// Falcos, Dark Scorpion Burglars, Don Zaloog, Mucus Yolk — monster triggers on a battle's outcome (destroys by battle,
    /// sent to the GY by battle, inflicts battle damage). Compile shapes, mini-duels through the engine entry points
    /// (TryAttack, the coin call, the target picker, turn changes) and near misses.
    /// </summary>
    public static class BattleOutcomeTriggersRegressionTests
    {
        const int Zombyra = 88472456;
        const int Timeater = 44913552;
        const int Kryuel = 82642348;
        const int Jackal = 98745000;
        const int Falcos = 87523462;
        const int Burglars = 40933924;
        const int DonZaloog = 76922029;
        const int MucusYolk = 70307656;
        const int Cliff = 6967870;
        const int Chick = 61587183;
        const int Marron = 94667532;        // 1000/1000

        const int Celtic = 91152256;        // 1400/1200
        const int Bewd = 89631139;          // 3000/2500
        const int SkullServant = 32274490;  // 300/200
        const int GiantSoldier = 13039848;  // 1300/2000
        const int PotOfGreed = 55144522;
        const int Raigeki = 12580477;
        const int Hinotama = 46130346;

        delegate void Chk(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("BattleOutcomeTriggers: " + name, ok, detail);

            Guard(Check, "Compile shapes", () => CompileShapes(db, Check));
            Guard(Check, "Near misses", () => NearMisses(Check));
            Guard(Check, "Zombyra", () => ZombyraDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Timeater", () => TimeaterDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Kryuel", () => KryuelDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Jackal / Falcos", () => DeckTopDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Burglars", () => BurglarsDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Don Zaloog", () => DonDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Mucus Yolk", () => MucusDuels(db, pDeck, aDeck, Check));
            Guard(Check, "Mecha-Dog Marron", () => MarronDuels(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk Check, string label, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Check($"{label}: no exception", false, ex.GetType().Name + ": " + ex.Message + " @ " +
                                                       (ex.StackTrace ?? "").Split('\n').FirstOrDefault());
            }
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static string Dump(CompiledCardProgram prog) =>
            prog == null ? "null" : string.Join(" | ", prog.ClauseList.Select(c => $"{c.Timing}:{c.Action}:{c.Amount}"));

        static (CompiledCardProgram prog, CardEffectStatusKind st) Prog(CardDatabase db, int id)
        {
            var def = db.Get(id);
            return def == null
                ? (null, CardEffectStatusKind.Unimplemented)
                : (CardTextEffectCompiler.Compile(def), CardEffectStatus.Classify(def));
        }

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var (prog, st) = Prog(db, Zombyra);
                Check("Zombyra the Dark: Implemented — direct-attack lock + ThisCardDestroysByBattle -200 ATK (lasting)",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 2 &&
                    prog.ClauseList.Exists(c => c.Action == EffectActionKind.CannotAttackDirectly) &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardDestroysByBattle &&
                                                c.Action == EffectActionKind.ChangeThisCardAtkWhileFaceUp &&
                                                c.Amount == -200 && !c.IsOptional),
                    $"{st} {Dump(prog)}");
            }
            {
                var (prog, st) = Prog(db, Timeater);
                Check("Timeater: Implemented — ThisCardDestroysByBattle -> opponent skips their next Main Phase 1",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.ThisCardDestroysByBattle &&
                    prog.ClauseList[0].Action == EffectActionKind.OpponentSkipsNextMainPhase1 &&
                    !prog.ClauseList[0].IsOptional, $"{st} {Dump(prog)}");
            }
            {
                var (prog, st) = Prog(db, Kryuel);
                Check("Kryuel: Implemented — SentFromFieldToGy (destroyed by battle) -> coin call, destroy 1 opp monster",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.SentFromFieldToGy &&
                    prog.ClauseList[0].Action == EffectActionKind.CoinCallRightDestroyOneOppMonster &&
                    prog.ClauseList[0].RequiresThisDestroyedByBattle && !prog.ClauseList[0].RequiresTargetChoice,
                    $"{st} {Dump(prog)}");
            }
            foreach (var (id, name, faceUpAtk) in new[] { (Jackal, "Mystical Knight of Jackal", false), (Falcos, "Winged Sage Falcos", true) })
            {
                var (prog, st) = Prog(db, id);
                Check($"{name}: Implemented — optional ThisCardDestroysByBattle, the destroyed monster to the top of the Deck" +
                      (faceUpAtk ? " (face-up Attack Position only)" : ""),
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.ThisCardDestroysByBattle &&
                    prog.ClauseList[0].Action == EffectActionKind.PlaceTargetOnTopOfDeck &&
                    prog.ClauseList[0].TargetIsMonsterDestroyedByThisBattle && prog.ClauseList[0].IsOptional &&
                    prog.ClauseList[0].RequiresDestroyedWasFaceUpAttack == faceUpAtk, $"{st} {Dump(prog)}");
            }
            {
                var (prog, st) = Prog(db, Burglars);
                Check("Dark Scorpion Burglars: Implemented — mandatory battle-damage trigger, the opponent sends 1 Spell",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                    prog.ClauseList[0].Action == EffectActionKind.OpponentSendsSpellFromDeckToGy &&
                    !prog.ClauseList[0].IsOptional, $"{st} {Dump(prog)}");
            }
            {
                var (prog, st) = Prog(db, DonZaloog);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                Check("Don Zaloog: Implemented — optional 1-of-2 choice: random discard / mill 2 (neither targets)",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented && cl.Count == 2 &&
                    cl.TrueForAll(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.IsOptional &&
                                       !c.RequiresTargetChoice) &&
                    cl.Exists(c => c.EffectChoiceIndex == 1 && c.Action == EffectActionKind.DiscardRandomFromOpponentHand &&
                                   c.Amount == 1) &&
                    cl.Exists(c => c.EffectChoiceIndex == 2 && c.Action == EffectActionKind.SendFromTopOfDeckToGy &&
                                   c.Side == EffectSide.Opponent && c.Amount == 2), $"{st} {Dump(prog)}");
            }
            {
                var (prog, st) = Prog(db, MucusYolk);
                Check("Mucus Yolk: Implemented — direct attack + battle damage -> +1000 ATK at the next Standby Phase",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 2 &&
                    prog.ClauseList.Exists(c => c.Action == EffectActionKind.CanAttackDirectly) &&
                    prog.ClauseList.Exists(c => c.Timing == EffectTiming.ThisCardInflictsBattleDamage &&
                                                c.Action == EffectActionKind.GainThisCardAtkAtNextStandbyWhileFaceUp &&
                                                c.Amount == 1000), $"{st} {Dump(prog)}");
            }
            {
                // The shared "1 of these effects" atom compiles Cliff (one targeted bullet + mill) and Chick (return + top-or-bottom).
                var (cliff, cst) = Prog(db, Cliff);
                var (chick, kst) = Prog(db, Chick);
                Check("Dark Scorpions: Cliff and Chick are both FullyCompiled (2 bullets each)",
                    cliff != null && cliff.FullyCompiled && cst == CardEffectStatusKind.Implemented &&
                    cliff.ClauseList.Count(c => c.EffectChoiceIndex > 0) == 2 &&
                    chick != null && chick.FullyCompiled && kst == CardEffectStatusKind.Implemented &&
                    chick.ClauseList.Count(c => c.EffectChoiceIndex > 0) == 2,
                    $"cliff={cst} chick={kst}");
            }
            {
                var (prog, st) = Prog(db, Marron);
                Check("Mecha-Dog Marron: Implemented — 2 clauses: battle-destroy inflict 1000 to both players + except-by-battle inflict 1000 to opponent",
                    prog != null && prog.FullyCompiled && st == CardEffectStatusKind.Implemented &&
                    prog.ClauseList.Count == 2 &&
                    prog.ClauseList.Count(c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                               c.Action == EffectActionKind.InflictDamageToBothPlayers &&
                                               c.RequiresThisDestroyedByBattle && c.Amount == 1000) == 1 &&
                    prog.ClauseList.Count(c => c.Timing == EffectTiming.SentFromFieldToGy &&
                                               c.Action == EffectActionKind.InflictDamageToOpponent &&
                                               c.Side == EffectSide.Opponent &&
                                               c.RequiresNotDestroyedByBattle && c.Amount == 1000) == 1,
                    $"{st} {Dump(prog)}");
            }
        }

        static CompiledCardProgram CompileMonster(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990162, name = "Near-miss Monster", type = "Effect Monster", frameType = "effect", race = "Warrior",
                attribute = "DARK", level = 4, atk = 1500, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == a);

        static bool FullyWith(CompiledCardProgram prog, Func<EffectClause, bool> shape) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && shape(c));

        static void NearMisses(Chk Check)
        {
            Check("Near miss: 'loses 200 ATK until the end of this turn' is not Zombyra's lasting loss",
                !Has(CompileMonster("If this card destroys a monster by battle: This card loses 200 ATK until the end of " +
                                    "this turn."), EffectActionKind.ChangeThisCardAtkWhileFaceUp));
            Check("Near miss: skipping the next Battle Phase is not Timeater's Main Phase 1 skip",
                !Has(CompileMonster("If this card destroys an opponent's monster by battle: Your opponent skips their next " +
                                    "Battle Phase."), EffectActionKind.OpponentSkipsNextMainPhase1));
            Check("Near miss: Kryuel's coin with 'destroy all monsters' is not compiled as 1 monster",
                !Has(CompileMonster("When this card is sent to the Graveyard as a result of battle, toss a coin and call it. " +
                                    "If you call it right, destroy all monsters on your opponent's side of the field."),
                    EffectActionKind.CoinCallRightDestroyOneOppMonster));
            Check("Near miss: a Falcos that returns the monster to the hand is not compiled",
                !FullyWith(CompileMonster("When this card destroys an opponent's face-up Attack Position monster by battle " +
                                          "and sends it to their Graveyard: You can return that monster to their hand."),
                    c => c.TargetIsMonsterDestroyedByThisBattle));
            Check("Near miss: Burglars sending a Trap is not compiled",
                !Has(CompileMonster("When this card inflicts Battle Damage to your opponent: Your opponent sends 1 Trap Card " +
                                    "from their Deck to the Graveyard."), EffectActionKind.OpponentSendsSpellFromDeckToGy));
            Check("Near miss: a Don Zaloog discarding 2 random cards is not compiled",
                !FullyWith(CompileMonster("When this card inflicts battle damage to your opponent: You can activate 1 of these " +
                                          "effects;\n● Discard 2 random cards from their hand.\n● Send the top 2 cards of " +
                                          "their Deck to the GY."), c => c.EffectChoiceIndex > 0));
            Check("Near miss: a Mucus Yolk gain without its lasting sentence is not compiled",
                !Has(CompileMonster("Each time this card inflicts Battle Damage to your opponent's Life Points, increase the " +
                                    "ATK of this card by 1000 points during your next Standby Phase."),
                    EffectActionKind.GainThisCardAtkAtNextStandbyWhileFaceUp));
            Check("Near miss: Mecha-Dog Marron's 'except by battle' bullet does not fire on a battle destroy",
                !FullyWith(CompileMonster("When this card is destroyed by battle and sent to the Graveyard, inflict 1000 " +
                                          "damage to your opponent."),
                    c => c.RequiresNotDestroyedByBattle));
        }

        // ───────────────────────── helpers ─────────────────────────

        static bool ReachMain(DuelEngine engine, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn)
                    return true;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn;
        }

        /// <summary>A fresh duel at <paramref name="who"/>'s Main Phase 1 (turn 3+), empty board, hands and GYs.</summary>
        static DuelEngine MainOf(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool player, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = ReachMain(e, player ? e.Player : e.Opponent, 3);
            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        /// <summary>The turn player attacks (entering the Battle Phase first if needed); the combat is drained.</summary>
        static bool Attack(DuelEngine e, DuelistState who, CardInstance attacker, CardInstance target)
        {
            PassAll(e);
            if (e.Phase == DuelPhase.Main1) e.TryEnterBattlePhase(who);
            PassAll(e);
            var ok = e.TryAttack(who, attacker, target);
            DrainCombat(e);
            PassAll(e);
            return ok;
        }

        static void SetDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        static bool RunAiTurn(DuelEngine e, int maxSteps)
        {
            var prevDelay = SimpleAi.StepDelay;
            SimpleAi.StepDelay = 0f;
            try
            {
                var it = SimpleAi.TakeTurnRoutine(e, null);
                for (var steps = 0; steps < maxSteps; steps++)
                {
                    DuelPresentationPacer.Clear();
                    Presentation.ArInteraction.SpellActivationPresentation.Clear();
                    if (e.PendingResponse != null && e.PendingResponse.Responder == e.Player) e.PassResponse();
                    if (!it.MoveNext()) return true;
                }

                return false;
            }
            finally
            {
                SimpleAi.StepDelay = prevDelay;
                DuelPresentationPacer.Clear();
            }
        }

        // ───────────────────────── Zombyra the Dark ─────────────────────────

        static void ZombyraDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var z = PlaceMonster(e, p, Zombyra, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var atk = Attack(e, p, z, celtic);
                e.NotifyPublic();
                var after = z.CurrentAtk;
                Check("Zombyra (attacker): destroys Celtic Guardian by battle -> loses 200 ATK (2100 -> 1900), kept after refresh",
                    reached && atk && opp.Graveyard.Contains(celtic) && after == 1900, $"atk={atk} ATK={after}");

                // Turned face-down: the loss ends with that field presence (RESETS_STANDARD).
                z.FaceUp = false;
                e.NotifyPublic();
                z.FaceUp = true;
                e.NotifyPublic();
                Check("Zombyra: turned face-down and back up — the ATK loss is gone (2100)", z.CurrentAtk == 2100,
                    $"ATK={z.CurrentAtk}");
            }

            {
                // No direct attack even with the opponent's field empty (the lock is live now that the card compiles).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var z = PlaceMonster(e, p, Zombyra, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                PassAll(e);
                e.TryEnterBattlePhase(p);
                PassAll(e);
                var zDirect = e.TryAttack(p, z, null);
                DrainCombat(e);
                var celticDirect = e.TryAttack(p, celtic, null);
                DrainCombat(e);
                Check("Zombyra: cannot attack the opponent directly (another monster can)",
                    reached && !zDirect && celticDirect && opp.LifePoints == lp - 1400, $"z={zDirect} lp {lp}->{opp.LifePoints}");
            }

            {
                // Defender path: the opponent's Celtic Guardian attacks Zombyra in Attack Position.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var z = PlaceMonster(e, p, Zombyra, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var atk = Attack(e, opp, celtic, z);
                e.NotifyPublic();
                Check("Zombyra (defender): destroys the attacking Celtic Guardian -> loses 200 ATK",
                    reached && atk && opp.Graveyard.Contains(celtic) && p.TryFindMonster(z, out _) && z.CurrentAtk == 1900,
                    $"atk={atk} ATK={z.CurrentAtk}");
            }

            {
                // Two kills: -400. Negating its effects ends the loss for good (RESETS_STANDARD_DISABLE).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var z = PlaceMonster(e, p, Zombyra, 0, BattlePosition.Attack, true);
                var s1 = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, z, s1);
                // A second battle in the same field presence (a fresh attack allowance for the test).
                z.AttacksDeclaredThisTurn = 0;
                var s2 = PlaceMonster(e, opp, SkullServant, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, z, s2);
                e.NotifyPublic();
                var twice = z.CurrentAtk;
                z.IsNegated = true;
                e.NotifyPublic();
                z.IsNegated = false;
                e.NotifyPublic();
                Check("Zombyra: two battles -> 1700; negated once -> the loss ends for good (2100)",
                    reached && twice == 1700 && z.CurrentAtk == 2100, $"twice={twice} now={z.CurrentAtk}");
            }

            {
                // A negated Zombyra does not activate its trigger.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var z = PlaceMonster(e, p, Zombyra, 0, BattlePosition.Attack, true);
                var s1 = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Attack, true);
                z.IsNegated = true;
                e.NotifyPublic();
                Attack(e, p, z, s1);
                z.IsNegated = false;
                e.NotifyPublic();
                Check("Zombyra: negated while it destroys a monster — no ATK loss",
                    reached && opp.Graveyard.Contains(s1) && z.CurrentAtk == 2100, $"ATK={z.CurrentAtk}");
            }
        }

        // ───────────────────────── Timeater ─────────────────────────

        static void TimeaterDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Timeater destroys the AI's monster: the AI's next turn starts in its Battle Phase.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var te = PlaceMonster(e, p, Timeater, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 1, BattlePosition.Defense, true);
                e.NotifyPublic();
                var atk = Attack(e, p, te, celtic);
                var ended = e.TryEndTurnSafe(p);
                PassAll(e);
                var skipped = e.TurnPlayer == opp && e.Phase == DuelPhase.Battle;
                Check("Timeater: destroys an opponent's monster -> the opponent's next turn skips Main Phase 1 (Battle Phase)",
                    reached && atk && opp.Graveyard.Contains(celtic) && ended && skipped,
                    $"atk={atk} turn={e.TurnPlayer?.Name} phase={e.Phase}");

                // The AI plays the skipped turn: it attacks from that Battle Phase (Blue-Eyes switched to Attack).
                bewd.Position = BattlePosition.Attack;
                e.NotifyPublic();
                var lp = p.LifePoints;
                var done = RunAiTurn(e, 400);
                Check("Timeater: the AI's turn without Main Phase 1 still battles (Blue-Eyes destroys Timeater) and ends",
                    done && p.Graveyard.Contains(te) && e.TurnPlayer == p && p.LifePoints == lp - 1100,
                    $"done={done} teGy={p.Graveyard.Contains(te)} turn={e.TurnPlayer?.Name} lp {lp}->{p.LifePoints}");

                // Only the next Main Phase 1: the AI's following turn has one.
                e.TryEndTurnSafe(p);
                PassAll(e);
                Check("Timeater: the skip applies once — the opponent's following turn has its Main Phase 1",
                    e.TurnPlayer == opp && e.Phase == DuelPhase.Main1, $"turn={e.TurnPlayer?.Name} phase={e.Phase}");
            }

            {
                // The AI's Timeater destroys the player's monster: the player's next turn starts in the Battle Phase.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var te = PlaceMonster(e, opp, Timeater, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var mine = PlaceMonster(e, p, Bewd, 1, BattlePosition.Defense, true);
                e.NotifyPublic();
                var atk = Attack(e, opp, te, celtic);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var inBattle = e.TurnPlayer == p && e.Phase == DuelPhase.Battle;
                mine.Position = BattlePosition.Attack;
                e.NotifyPublic();
                var canAttack = e.TryAttack(p, mine, te);
                DrainCombat(e);
                PassAll(e);
                var main2 = e.TryEnterMainPhase2(p);
                Check("Timeater (AI): the player's next turn skips Main Phase 1 — Battle Phase at once, then Main Phase 2",
                    reached && atk && p.Graveyard.Contains(celtic) && inBattle && canAttack &&
                    opp.Graveyard.Contains(te) && main2 && e.Phase == DuelPhase.Main2,
                    $"atk={atk} inBattle={inBattle} canAttack={canAttack} main2={main2} phase={e.Phase}");
            }

            {
                // No Battle Phase possible that turn (a pending Battle Phase skip as well): straight to the End Phase.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var te = PlaceMonster(e, opp, Timeater, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, opp, te, celtic);
                p.SkipNextBattlePhase = true;
                var turn = e.TurnNumber;
                e.TryEndTurnSafe(opp);
                PassAll(e);
                Check("Timeater: no Battle Phase allowed either -> the player's turn goes straight to the End Phase",
                    reached && p.Graveyard.Contains(celtic) && e.TurnPlayer == opp && e.TurnNumber == turn + 2,
                    $"turn {turn}->{e.TurnNumber} player={e.TurnPlayer?.Name} phase={e.Phase}");
            }

            {
                // Review fix (v162): the skipped turn opens the player's Battle-Phase-start window (a Set Reinforcements).
                // The AI has no monster that can attack; it still waits for that window before it goes to Main Phase 2.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var te = PlaceMonster(e, p, Timeater, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Bewd, 1, BattlePosition.Defense, true);
                var trap = InteractionRegressionTests.PlaceSetTrap(e, p, 17814387, 0); // Reinforcements
                trap.SetThisTurn = false;
                // The AI's draw for the skipped turn is fixed: a shuffled deck could hand it Monster Reborn, which it
                // legally activates in Main Phase 2 on the destroyed Celtic Guardian (seeds 4, 15, 25).
                SetDeck(opp, Celtic, Celtic, Celtic, Celtic, Celtic);
                e.NotifyPublic();
                Attack(e, p, te, celtic);
                var destroyed = opp.Graveyard.Contains(celtic);
                e.TryEndTurnSafe(p);
                var battleWindow = e.PendingResponse;
                var openAtStart = e.TurnPlayer == opp && e.Phase == DuelPhase.Battle &&
                                  battleWindow != null && battleWindow.Responder == p;
                var prevDelay = SimpleAi.StepDelay;
                SimpleAi.StepDelay = 0f;
                var leftUnderWindow = "";
                var done = false;
                try
                {
                    var it = SimpleAi.TakeTurnRoutine(e, null);
                    for (var steps = 0; steps < 400; steps++)
                    {
                        DuelPresentationPacer.Clear();
                        Presentation.ArInteraction.SpellActivationPresentation.Clear();
                        var open = e.PendingResponse != null && e.PendingResponse.Responder == p;
                        // The battle-start window is this object. A later Main Phase 2 chain is a new response.
                        if (open && ReferenceEquals(e.PendingResponse, battleWindow) &&
                            e.TurnPlayer == opp && e.Phase != DuelPhase.Battle)
                            leftUnderWindow += $" step{steps}:{e.Phase}";
                        if (open && steps >= 8) e.PassResponse(); // the player answers late
                        if (!it.MoveNext()) { done = true; break; }
                    }
                }
                finally
                {
                    SimpleAi.StepDelay = prevDelay;
                    DuelPresentationPacer.Clear();
                }

                Check("Timeater: the AI's skipped turn waits for the player's Battle-Phase-start window (no attacker) before Main Phase 2",
                    reached && destroyed && opp.Graveyard.Contains(celtic) && openAtStart && leftUnderWindow.Length == 0 &&
                    done && e.TurnPlayer == p,
                    $"reached={reached} destroyed={destroyed} celticGy={opp.Graveyard.Contains(celtic)} openAtStart={openAtStart} left:{leftUnderWindow} done={done} turn={e.TurnPlayer?.Name} phase={e.Phase}");
            }
        }

        // ───────────────────────── Kryuel ─────────────────────────

        static void KryuelDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The AI destroys the player's Kryuel: the player calls (right) and selects 1 of the AI's 2 monsters.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var kryuel = PlaceMonster(e, p, Kryuel, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, opp, Celtic, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                Attack(e, opp, bewd, kryuel);
                var asked = e.PendingActivation != null && e.PendingActivation.AwaitingCoinCall &&
                            e.PendingActivation.Controller == p;
                e.Rng.QueueCoin(true);
                var called = e.TrySelectCoinCall(true);
                var pick = e.PendingActivation;
                var offered = pick != null && pick.Controller == p && pick.LegalTargets.Count == 2 &&
                              pick.LegalTargets.Contains(set) && pick.LegalTargets.Contains(bewd);
                var picked = e.TrySelectEffectTarget(set);
                PassAll(e);
                Check("Kryuel: destroyed by battle -> coin call (right) -> the player selects the Set monster and destroys it",
                    reached && p.Graveyard.Contains(kryuel) && asked && called && offered && picked &&
                    opp.Graveyard.Contains(set) && opp.TryFindMonster(bewd, out _) && e.PendingActivation == null,
                    $"asked={asked} called={called} offered={offered} picked={picked} setGy={opp.Graveyard.Contains(set)}");
            }

            {
                // Wrong call: nothing is destroyed.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var kryuel = PlaceMonster(e, p, Kryuel, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, opp, bewd, kryuel);
                e.Rng.QueueCoin(false);
                var called = e.TrySelectCoinCall(true);
                PassAll(e);
                Check("Kryuel: wrong call -> no monster is destroyed",
                    reached && called && p.Graveyard.Contains(kryuel) && opp.TryFindMonster(bewd, out _) &&
                    e.PendingActivation == null, $"called={called}");
            }

            {
                // The AI's Kryuel: it calls by itself; a right call destroys the player's only monster (the attacker).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var kryuel = PlaceMonster(e, opp, Kryuel, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                e.Rng.QueueCoin(true);
                Attack(e, p, bewd, kryuel);
                Check("Kryuel (AI): right call -> it destroys the attacking Blue-Eyes (selection, no prompt)",
                    reached && opp.Graveyard.Contains(kryuel) && p.Graveyard.Contains(bewd) && e.PendingActivation == null,
                    $"bewdGy={p.Graveyard.Contains(bewd)}");
            }

            {
                // Sent to the GY by a card effect, not by battle: no coin.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var kryuel = PlaceMonster(e, p, Kryuel, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                TextEffectRuntime.DestroyCard(e, kryuel, null, false, opp);
                e.NotifyPublic();
                Check("Kryuel: destroyed by a card effect -> no coin call",
                    reached && p.Graveyard.Contains(kryuel) && e.PendingActivation == null && opp.TryFindMonster(bewd, out _));
            }
        }

        // ───────────────────────── Mystical Knight of Jackal / Winged Sage Falcos ─────────────────────────

        static void DeckTopDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jackal = PlaceMonster(e, p, Jackal, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Defense, true);
                e.NotifyPublic();
                var deck = opp.Deck.Count;
                Attack(e, p, jackal, celtic);
                var pend = e.PendingActivation;
                var offered = pend != null && pend.Controller == p && pend.LegalTargets.Count == 1 &&
                              pend.LegalTargets[0] == celtic && opp.Graveyard.Contains(celtic);
                var picked = e.TrySelectEffectTarget(celtic);
                Check("Mystical Knight of Jackal: destroys a Defense Position monster -> tap it -> top of the opponent's Deck",
                    reached && offered && picked && !opp.Graveyard.Contains(celtic) && opp.Deck.Count == deck + 1 &&
                    opp.Deck[0] == Celtic, $"offered={offered} picked={picked} deck {deck}->{opp.Deck.Count}");
            }

            {
                // CANCEL declines: the monster stays in the GY.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jackal = PlaceMonster(e, p, Jackal, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, jackal, celtic);
                var asked = e.PendingActivation != null;
                e.CancelEffectTargeting();
                Check("Mystical Knight of Jackal: CANCEL — the destroyed monster stays in the GY",
                    reached && asked && opp.Graveyard.Contains(celtic) && e.PendingActivation == null);
            }

            {
                // The AI's Jackal returns the player's monster automatically.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var jackal = PlaceMonster(e, opp, Jackal, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, opp, jackal, celtic);
                Check("Mystical Knight of Jackal (AI): the player's destroyed monster goes on top of the player's Deck",
                    reached && !p.Graveyard.Contains(celtic) && p.Deck.Count > 0 && p.Deck[0] == Celtic &&
                    e.PendingActivation == null);
            }

            {
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var falcos = PlaceMonster(e, p, Falcos, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, falcos, celtic);
                var offered = e.PendingActivation != null && e.PendingActivation.LegalTargets.Contains(celtic);
                var picked = e.TrySelectEffectTarget(celtic);
                Check("Winged Sage Falcos: destroys a face-up Attack Position monster -> top of the opponent's Deck",
                    reached && offered && picked && !opp.Graveyard.Contains(celtic) && opp.Deck[0] == Celtic);
            }

            {
                // Near miss: a Defense Position monster destroyed by Falcos is not offered.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var falcos = PlaceMonster(e, p, Falcos, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(e, opp, SkullServant, 0, BattlePosition.Defense, true);
                e.NotifyPublic();
                Attack(e, p, falcos, servant);
                Check("Winged Sage Falcos: a Defense Position monster it destroys is not offered (stays in the GY)",
                    reached && opp.Graveyard.Contains(servant) && e.PendingActivation == null);
            }
        }

        // ───────────────────────── Dark Scorpion Burglars ─────────────────────────

        static void BurglarsDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The player's Burglars hits the AI directly: the AI sends its least useful Spell (a Hinotama copy).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(opp, Celtic, PotOfGreed, Hinotama, Hinotama, Bewd, Raigeki);
                var b = PlaceMonster(e, p, Burglars, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var lp = opp.LifePoints;
                Attack(e, p, b, null);
                var spells = opp.Graveyard.Where(c => c?.Def != null && c.Def.IsSpell).ToList();
                Check("Dark Scorpion Burglars: battle damage -> the AI sends 1 Spell from its Deck to the GY",
                    reached && opp.LifePoints == lp - 1000 && spells.Count == 1 && opp.Deck.Count == 5 &&
                    e.PendingActivation == null, $"spells={string.Join(",", spells.Select(s => s.Name))} deck={opp.Deck.Count}");
                // v162 cross-group audit: the sent Spell was sent by the Burglars controller's card effect (Fairy Guardian).
                Check("Dark Scorpion Burglars: the sent Spell counts as sent to the GY by the opponent's card effect this turn",
                    spells.Count == 1 && GyEntryStamps.SentByOpponentThisTurn(e, opp, spells[0]) &&
                    !GyEntryStamps.SentByOpponentThisTurn(e, p, spells[0]),
                    $"reasonPlayer={spells.FirstOrDefault()?.GyEntryReasonPlayer?.Name}");
            }

            {
                // The AI's Burglars beats the player's Skull Servant: the player picks the Spell while the destruction waits.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(p, Celtic, PotOfGreed, Raigeki, Bewd);
                var b = PlaceMonster(e, opp, Burglars, 0, BattlePosition.Attack, true);
                var servant = PlaceMonster(e, p, SkullServant, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, opp, b, servant);
                var pend = e.PendingActivation;
                var offered = pend != null && pend.Controller == p && pend.LegalTargets.Count == 2 &&
                              pend.LegalTargets.All(c => c.Def.IsSpell);
                var waiting = p.TryFindMonster(servant, out _);
                var raigeki = pend?.LegalTargets.FirstOrDefault(c => c.CardId == Raigeki);
                var picked = raigeki != null && e.TrySelectEffectTarget(raigeki);
                DrainCombat(e);
                Check("Dark Scorpion Burglars (AI): the player picks Raigeki from their own Deck; then Skull Servant is destroyed",
                    reached && offered && waiting && picked && p.Graveyard.Any(c => c.CardId == Raigeki) &&
                    !p.Deck.Contains(Raigeki) && p.Deck.Count == 3 && p.Graveyard.Contains(servant) &&
                    e.PendingActivation == null,
                    $"offered={offered} waiting={waiting} picked={picked} servantGy={p.Graveyard.Contains(servant)}");
            }

            {
                // No Spell in the Deck: nothing happens.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(opp, Celtic, Bewd);
                var b = PlaceMonster(e, p, Burglars, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, b, null);
                Check("Dark Scorpion Burglars: no Spell in the opponent's Deck -> nothing is sent",
                    reached && opp.Deck.Count == 2 && opp.Graveyard.Count == 0 && e.PendingActivation == null);
            }
        }

        // ───────────────────────── Don Zaloog ─────────────────────────

        static void DonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Tap Don Zaloog: the AI discards 1 random card.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(opp, Celtic, Bewd, PotOfGreed);
                PutInHand(e, opp, Celtic);
                PutInHand(e, opp, Raigeki);
                var don = PlaceMonster(e, p, DonZaloog, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, don, null);
                var pend = e.PendingActivation;
                var offered = pend?.EffectChoice != null && pend.LegalTargets.Count == 1 && pend.LegalTargets[0] == don &&
                              pend.EffectChoice.UntargetedAlt != null;
                var picked = e.TrySelectEffectTarget(don);
                Check("Don Zaloog: battle damage -> tap Don Zaloog -> 1 random card of the AI's hand is discarded",
                    reached && offered && picked && opp.Hand.Count == 1 && opp.Graveyard.Count == 1 && opp.Deck.Count == 3,
                    $"offered={offered} picked={picked} hand={opp.Hand.Count} gy={opp.Graveyard.Count}");
            }

            {
                // CANCEL -> the other effect is offered -> tap -> mill 2. CANCEL twice -> neither.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(opp, Celtic, Bewd, PotOfGreed);
                PutInHand(e, opp, Celtic);
                var don = PlaceMonster(e, p, DonZaloog, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, don, null);
                e.CancelEffectTargeting();
                var second = e.PendingActivation?.EffectChoice;
                var reoffered = second != null && second.UntargetedAlt == null &&
                                second.Untargeted?.Action == EffectActionKind.SendFromTopOfDeckToGy;
                var picked = e.TrySelectEffectTarget(don);
                Check("Don Zaloog: CANCEL -> the mill is offered -> tap -> the top 2 cards of the AI's Deck go to the GY",
                    reached && reoffered && picked && opp.Deck.Count == 1 && opp.Graveyard.Count == 2 && opp.Hand.Count == 1,
                    $"reoffered={reoffered} deck={opp.Deck.Count} gy={opp.Graveyard.Count}");

                var e2 = MainOf(db, pDeck, aDeck, true, out _);
                SetDeck(e2.Opponent, Celtic, Bewd, PotOfGreed);
                PutInHand(e2, e2.Opponent, Celtic);
                var don2 = PlaceMonster(e2, e2.Player, DonZaloog, 0, BattlePosition.Attack, true);
                e2.NotifyPublic();
                Attack(e2, e2.Player, don2, null);
                e2.CancelEffectTargeting();
                e2.CancelEffectTargeting();
                Check("Don Zaloog: CANCEL twice -> neither effect",
                    e2.PendingActivation == null && e2.Opponent.Hand.Count == 1 && e2.Opponent.Deck.Count == 3 &&
                    e2.Opponent.Graveyard.Count == 0);
            }

            {
                // Only the legal bullet is offered: an empty hand leaves the mill.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                SetDeck(opp, Celtic, Bewd, PotOfGreed);
                var don = PlaceMonster(e, p, DonZaloog, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, p, don, null);
                var ch = e.PendingActivation?.EffectChoice;
                var onlyMill = ch != null && ch.UntargetedAlt == null &&
                               ch.Untargeted?.Action == EffectActionKind.SendFromTopOfDeckToGy;
                Check("Don Zaloog: the opponent's hand is empty -> only the mill is offered", reached && onlyMill);
            }

            {
                // The AI's Don Zaloog: it takes the discard.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PutInHand(e, p, Celtic);
                PutInHand(e, p, Bewd);
                var deck = p.Deck.Count;
                var don = PlaceMonster(e, opp, DonZaloog, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                Attack(e, opp, don, null);
                Check("Don Zaloog (AI): it activates the random discard against the player's hand",
                    reached && p.Hand.Count == 1 && p.Graveyard.Count == 1 && p.Deck.Count == deck &&
                    e.PendingActivation == null, $"hand={p.Hand.Count}");
            }
        }

        // ───────────────────────── Mucus Yolk ─────────────────────────

        static void MucusDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var mucus = PlaceMonster(e, p, MucusYolk, 0, BattlePosition.Attack, true);
                // A Giant Soldier on the opponent's side: Mucus Yolk still attacks directly.
                PlaceMonster(e, opp, GiantSoldier, 0, BattlePosition.Defense, true);
                mucus.UntilEndOfTurnAtk = 500;
                e.NotifyPublic();
                var lp = opp.LifePoints;
                var atk = Attack(e, p, mucus, null);
                var hit = opp.LifePoints == lp - 500;
                e.TryEndTurnSafe(p);
                PassAll(e);
                var oppTurnAtk = mucus.CurrentAtk;
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var gained = e.TurnPlayer == p && mucus.CurrentAtk == 1000;
                e.TryEndTurnSafe(p);
                PassAll(e);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var once = mucus.CurrentAtk == 1000;
                Check("Mucus Yolk: direct battle damage -> +1000 ATK at its controller's next Standby Phase (not the opponent's), once",
                    reached && atk && hit && oppTurnAtk == 0 && gained && once,
                    $"atk={atk} hit={hit} oppTurn={oppTurnAtk} gained={gained} now={mucus.CurrentAtk}");

                // Turned face-down: the gain ends.
                mucus.FaceUp = false;
                e.NotifyPublic();
                mucus.FaceUp = true;
                e.NotifyPublic();
                Check("Mucus Yolk: turned face-down — the ATK gain ends", mucus.CurrentAtk == 0, $"ATK={mucus.CurrentAtk}");
            }

            {
                // Two hits before the Standby Phase: still +1000 once (one flag, once per turn).
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var mucus = PlaceMonster(e, p, MucusYolk, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                TextEffectRuntime.NotifyInflictedBattleDamage(e, p, opp, mucus, true, 500);
                TextEffectRuntime.NotifyInflictedBattleDamage(e, p, opp, mucus, true, 500);
                e.TryEndTurnSafe(p);
                PassAll(e);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                Check("Mucus Yolk: two battle-damage events -> +1000 once at the next Standby Phase",
                    reached && e.TurnPlayer == p && mucus.CurrentAtk == 1000, $"ATK={mucus.CurrentAtk}");
            }

            {
                // Negated at its Standby Phase: no gain, and the flag ends there.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var mucus = PlaceMonster(e, p, MucusYolk, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                TextEffectRuntime.NotifyInflictedBattleDamage(e, p, opp, mucus, true, 500);
                e.TryEndTurnSafe(p);
                PassAll(e);
                mucus.IsNegated = true;
                e.TryEndTurnSafe(opp);
                PassAll(e);
                var none = mucus.CurrentAtk == 0;
                mucus.IsNegated = false;
                e.TryEndTurnSafe(p);
                PassAll(e);
                e.TryEndTurnSafe(opp);
                PassAll(e);
                Check("Mucus Yolk: negated at the Standby Phase -> no gain, and none later either",
                    reached && none && mucus.CurrentAtk == 0, $"ATK={mucus.CurrentAtk}");
            }
        }

        // ───────────────────────── Mecha-Dog Marron ─────────────────────────

        static void MarronDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Battle destroy: the AI's Blue-Eyes destroys the player's Marron -> both players take 1000.
                var e = MainOf(db, pDeck, aDeck, false, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var marron = PlaceMonster(e, p, Marron, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var pLpBefore = p.LifePoints;
                var oppLpBefore = opp.LifePoints;
                var ok = Attack(e, opp, bewd, marron);
                e.NotifyPublic();
                Check("Mecha-Dog Marron: destroyed by battle -> both players take 1000 damage",
                    reached && ok && p.Graveyard.Contains(marron) && p.LifePoints == pLpBefore - 3000 &&
                    opp.LifePoints == oppLpBefore - 1000,
                    $"reached={reached} ok={ok} gy={p.Graveyard.Contains(marron)} pLP {pLpBefore}->{p.LifePoints} oppLP {oppLpBefore}->{opp.LifePoints}");
            }

            {
                // Destroyed by a card effect (not by battle): only the opponent takes 1000.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var marron = PlaceMonster(e, p, Marron, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, Bewd, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var pLpBefore = p.LifePoints;
                var oppLpBefore = opp.LifePoints;
                TextEffectRuntime.DestroyCard(e, marron, null, false, opp);
                e.NotifyPublic();
                Check("Mecha-Dog Marron: destroyed except by battle -> only the opponent takes 1000 damage",
                    reached && p.Graveyard.Contains(marron) && p.LifePoints == pLpBefore &&
                    opp.LifePoints == oppLpBefore - 1000,
                    $"pLP {pLpBefore}->{p.LifePoints} oppLP {oppLpBefore}->{opp.LifePoints}");
            }

            {
                // Near miss: returned to hand (not destroyed) -> no damage to either player.
                var e = MainOf(db, pDeck, aDeck, true, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var marron = PlaceMonster(e, p, Marron, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var pLpBefore = p.LifePoints;
                var oppLpBefore = opp.LifePoints;
                e.ReturnCardToHand(marron);
                e.NotifyPublic();
                Check("Mecha-Dog Marron: returned to hand (not destroyed) -> no damage",
                    reached && !p.Graveyard.Contains(marron) && p.Hand.Contains(marron) &&
                    p.LifePoints == pLpBefore && opp.LifePoints == oppLpBefore,
                    $"pLP {pLpBefore}->{p.LifePoints} oppLP {oppLpBefore}->{opp.LifePoints}");
            }
        }
    }
}
