using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "SimultaneousTriggers" (v160): the SEGOC queue (<see cref="SimultaneousTriggersRuntime"/>) and Curse of
    /// Fiend. Curse flips both players' face-down monsters at once; their FLIP effects go on one Chain (turn player's
    /// first, so the opponent's resolves first), with targets chosen before any of them resolves, and a FLIP effect still
    /// resolves after an earlier link destroyed its monster. The held "sent to the GY" triggers of a mass destruction
    /// (Dark Hole) use the same order.
    /// </summary>
    public static class SimultaneousTriggersRegressionTests
    {
        const int CurseOfFiend = 12470447;
        const int ManEaterBug = 54652250;     // FLIP: target 1 monster on the field; destroy it
        const int Celtic = 91152256;          // 1400 / 1200
        const int DarkHole = 53129443;
        const int Immortal = 84926738;        // The Immortal of Thunder: sent from the field to the GY -> lose 5000 LP
        const int Granadora = 13944422;       // destroyed and sent to the GY -> lose 2000 LP
        const int Otohime = 39751093;         // Spirit: Normal Summoned or flipped face-up -> change 1 opp face-up position

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("SimultaneousTriggers: " + name, ok, detail ?? "");

            CompileShape(db, Check);
            CurseFlipChain(db, pDeck, aDeck, Check);
            CurseYouOrderYourFlips(db, pDeck, aDeck, Check);
            CurseOnlyWhileSetInYourStandby(db, pDeck, aDeck, Check);
            CurseFlipsOtohimeOnce(db, pDeck, aDeck, Check);
            HeldGyTriggersOneChain(db, pDeck, aDeck, Check);
        }

        static DuelEngine Start(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = new DuelEngine();
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            e.Player.Hand.Clear();
            e.Opponent.Hand.Clear();
            return e;
        }

        /// <summary>Ends turn 1 (Player) and turn 2 (Opponent): the Player's turn 3 begins.</summary>
        static void ToPlayerTurn3(DuelEngine e)
        {
            e.TryEndTurnSafe(e.Player);
            if (e.IsAwaitingResponse) e.PassResponse();
            e.TryEndTurnSafe(e.Opponent);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static string State(DuelEngine e) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} paused={e.StandbyPausedFor?.Name ?? "-"} " +
            $"pick={(e.PendingActivation != null ? e.PendingActivation.Prompt : "-")} resp={e.IsAwaitingResponse} " +
            $"chain={e.Chain.Count}";

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShape(CardDatabase db, Action<string, bool, string> Check)
        {
            var def = db.Get(CurseOfFiend);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            Check("Curse of Fiend: whole text -> one Standby-only Set activation, mass change + rest-of-turn lock",
                prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                c.Action == EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn &&
                c.Zone == EffectZoneFilter.FieldMonsters && c.Side == EffectSide.Both && c.OnlyDuringOwnStandby &&
                c.ActivationCondition == PositionLocksRuntime.SetInYourStandbyPhase &&
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                def != null ? CardEffectStatus.Classify(def).ToString() : "missing");

            // Near misses: the same swap in another phase, or without its lock sentence, is not this atom.
            foreach (var (label, text) in new[]
                     {
                         ("End Phase variant", def?.desc?.Replace("during your Standby Phase", "during your End Phase")),
                         ("no lock sentence", "Change the battle positions of all Attack Position monsters on the field " +
                                              "to Defense Position and vice-versa. You can only activate this card " +
                                              "during your Standby Phase.")
                     })
            {
                var near = CardTextEffectCompiler.Compile(new CardDef
                {
                    id = 990000001, name = "Curse Near Miss", type = "Spell Card", race = "Normal", desc = text
                });
                Check($"Curse of Fiend near miss ({label}) is not this atom",
                    text != null && (near == null || !near.FullyCompiled ||
                                     !near.ClauseList.Exists(x => x != null &&
                                         x.Action == EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn)),
                    near == null ? "null" : $"full={near.FullyCompiled}");
            }
        }

        // ───────────────────────────── Curse of Fiend ─────────────────────────────

        /// <summary>
        /// Your Set Curse of Fiend in your Standby Phase: your face-down Man-Eater Bug and the opponent's flip face-up
        /// together. Yours is Chain Link 1 (you choose its target first), the opponent's Chain Link 2 targets your Bug.
        /// Link 2 resolves first and destroys your Bug; your Bug's effect still resolves and destroys its target.
        /// </summary>
        static void CurseFlipChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var curse = PlaceSetTrap(e, p, CurseOfFiend, 0);
            var myBug = PlaceMonster(e, p, ManEaterBug, 0, BattlePosition.Defense, false);
            var oppBug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            ToPlayerTurn3(e);
            var paused = e.Phase == DuelPhase.Standby && e.TurnPlayer == p && e.StandbyPausedFor == p;
            Check("Curse of Fiend: your Standby Phase holds for the Set card", paused, State(e));

            var act = paused && e.TryActivateSpellTrap(p, curse, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var pick = e.PendingActivation;
            var asked = act && pick != null && pick.Controller == p && pick.Card == myBug &&
                        pick.LegalTargets.Contains(oppBug) && pick.LegalTargets.Contains(celtic) &&
                        !pick.LegalTargets.Contains(myBug);
            Check("Curse of Fiend: every monster changed at once; your FLIP (Chain Link 1) asks its target first",
                asked && myBug.FaceUp && myBug.Position == BattlePosition.Attack && oppBug.FaceUp &&
                oppBug.Position == BattlePosition.Attack && celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                p.Graveyard.Contains(curse) && p.TryFindMonster(myBug, out _) && o.TryFindMonster(oppBug, out _),
                $"act={act} myBug={myBug.FaceUp}/{myBug.Position} oppBug={oppBug.FaceUp}/{oppBug.Position} " +
                $"celtic={celtic.Position} curseGy={p.Graveyard.Contains(curse)} {State(e)}");

            var picked = asked && e.TrySelectEffectTarget(oppBug);
            Check("Curse of Fiend: the opponent's FLIP (link 2) destroyed your Bug first; your Bug's FLIP still resolved",
                picked && p.Graveyard.Contains(myBug) && o.Graveyard.Contains(oppBug) && o.TryFindMonster(celtic, out _) &&
                !e.IsAwaitingEffectTarget && !SimultaneousTriggersRuntime.Busy(e),
                $"picked={picked} myBugGy={p.Graveyard.Contains(myBug)} oppBugGy={o.Graveyard.Contains(oppBug)} " +
                $"celtic={o.TryFindMonster(celtic, out _)} {State(e)}");

            Check("Curse of Fiend: no battle position can be changed by hand for the rest of the turn",
                PositionLocksRuntime.ManualPositionChangeLocked(e, celtic), State(e));
            var main = e.CanContinueAfterStandby(p) && e.ContinueAfterStandby() && e.Phase == DuelPhase.Main1;
            Check("Curse of Fiend: after the Chain, the turn goes on to Main Phase 1", main, State(e));
        }

        /// <summary>
        /// Two of your FLIP monsters flipped by your Curse of Fiend: you choose their Chain order. The Immortal of Thunder
        /// as Chain Link 1 makes Man-Eater Bug Chain Link 2: the Bug asks its target second and resolves first.
        /// </summary>
        static void CurseYouOrderYourFlips(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var curse = PlaceSetTrap(e, p, CurseOfFiend, 0);
            var immortal = PlaceMonster(e, p, Immortal, 0, BattlePosition.Defense, false);
            var bug = PlaceMonster(e, p, ManEaterBug, 1, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            ToPlayerTurn3(e);
            var p0 = p.LifePoints;
            var trace = new List<(int Lp, bool Celtic)>();
            e.OnLog += msg => trace.Add((p.LifePoints, o.TryFindMonster(celtic, out _)));
            var act = e.StandbyPausedFor == p && e.TryActivateSpellTrap(p, curse, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var order = e.PendingActivation;
            var asked = act && order != null && order.Controller == p && order.LegalTargets.Count == 2 &&
                        order.LegalTargets.Contains(immortal) && order.LegalTargets.Contains(bug) && p.LifePoints == p0;
            Check("Curse of Fiend: you order your two simultaneous FLIP effects", asked, State(e));

            var ordered = asked && e.TrySelectEffectTarget(immortal);
            var target = e.PendingActivation;
            var bugAsks = ordered && target != null && target != order && target.Card == bug &&
                          target.LegalTargets.Contains(celtic) && p.LifePoints == p0;
            var picked = bugAsks && e.TrySelectEffectTarget(celtic);
            var gone = trace.FindIndex(t => !t.Celtic);
            var gained = trace.FindIndex(t => t.Lp > p0);
            Check("Curse of Fiend: Man-Eater Bug (Chain Link 2) resolves before The Immortal of Thunder (Chain Link 1)",
                picked && o.Graveyard.Contains(celtic) && p.LifePoints == p0 + 3000 && gone >= 0 && gained > gone &&
                !e.IsAwaitingEffectTarget && !SimultaneousTriggersRuntime.Busy(e),
                $"bugAsks={bugAsks} picked={picked} LP={p.LifePoints} gone={gone} gained={gained} {State(e)}");
        }

        /// <summary>Not from the hand, and not in the Main Phase while Set.</summary>
        static void CurseOnlyWhileSetInYourStandby(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
            var inHand = PutInHand(e, p, CurseOfFiend);
            var set = PlaceSetTrap(e, p, CurseOfFiend, 1);
            Check("Curse of Fiend: cannot be activated from the hand or in the Main Phase",
                e.Phase == DuelPhase.Main1 && !e.CanActivateSpellTrap(p, inHand, fromHand: true) &&
                !e.CanActivateSpellTrap(p, set, fromHand: false), State(e));
        }

        /// <summary>
        /// v160 integration audit (SimultaneousTriggers x SpiritMonsters): Curse of Fiend flips a Set Otohime. The mass
        /// change reports the flip through ApplyEffectPositionChange and through its own queue call; Otohime's "flipped
        /// face-up" trigger must still go on the Chain once (Celtic: ATK -> DEF by Curse, -> ATK by Otohime, no 2nd pick).
        /// </summary>
        static void CurseFlipsOtohimeOnce(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var curse = PlaceSetTrap(e, p, CurseOfFiend, 0);
            var oto = PlaceMonster(e, p, Otohime, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            ToPlayerTurn3(e);
            var paused = e.Phase == DuelPhase.Standby && e.TurnPlayer == p && e.StandbyPausedFor == p;
            var act = paused && e.TryActivateSpellTrap(p, curse, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Card == oto &&
                        e.PendingActivation.LegalTargets.Contains(celtic) && celtic.Position == BattlePosition.Defense;
            var picked = asked && e.TrySelectEffectTarget(celtic);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var askedAgain = e.IsAwaitingEffectTarget;
            Check("Curse of Fiend flips a Set Otohime: its \"flipped face-up\" trigger activates once",
                act && asked && picked && !askedAgain && oto.FaceUp && celtic.Position == BattlePosition.Attack &&
                !SimultaneousTriggersRuntime.Busy(e),
                $"act={act} asked={asked} picked={picked} askedAgain={askedAgain} oto={oto.FaceUp}/{oto.Position} " +
                $"celtic={celtic.Position} {State(e)}");
        }

        // ───────────────────────────── held "sent to the GY" triggers ─────────────────────────────

        /// <summary>
        /// Dark Hole destroys your The Immortal of Thunder and Granadora and the opponent's The Immortal of Thunder. Their
        /// triggers go on one Chain at once (no order pick for these): the opponent's goes on last and resolves first,
        /// then yours in the order they were destroyed (Immortal, then Granadora).
        /// </summary>
        static void HeldGyTriggersOneChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var immP = PlaceMonster(e, p, Immortal, 0, BattlePosition.Attack, true);
            var granP = PlaceMonster(e, p, Granadora, 1, BattlePosition.Attack, true);
            var immO = PlaceMonster(e, o, Immortal, 0, BattlePosition.Attack, true);
            var hole = PutInHand(e, p, DarkHole);
            int p0 = p.LifePoints, o0 = o.LifePoints;
            var trace = new List<(int P, int O)>();
            e.OnLog += _ => trace.Add((p.LifePoints, o.LifePoints));
            var cast = e.Phase == DuelPhase.Main1 && e.TryActivateSpellTrap(p, hole, fromHand: true);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var oFirst = trace.FindIndex(t => t.O < o0);
            var pFirst = trace.FindIndex(t => t.P < p0);
            var immBeforeGran = trace.Exists(t => t.P == p0 - 5000);
            Check("Dark Hole: the opponent's trigger resolves first, then yours in the order they were destroyed",
                cast && p.Graveyard.Contains(immP) && p.Graveyard.Contains(granP) && o.Graveyard.Contains(immO) &&
                p.LifePoints == p0 - 7000 && o.LifePoints == o0 - 5000 && oFirst >= 0 && pFirst > oFirst &&
                immBeforeGran && !e.IsAwaitingEffectTarget && !SimultaneousTriggersRuntime.Busy(e),
                $"cast={cast} LP={p.LifePoints}/{o.LifePoints} oFirst={oFirst} pFirst={pFirst} " +
                $"imm5000={immBeforeGran} {State(e)}");
        }
    }
}
