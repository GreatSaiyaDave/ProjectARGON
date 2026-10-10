using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// TriggerChainResponses batch (v163): trigger effects are real Chain Links (<see cref="TriggerChainResponsesRuntime"/>).
    /// A FLIP effect (Flip Summon, or a SEGOC Chain of monsters flipped by Curse of Fiend), a "sent to the GY" trigger
    /// that happened while a Chain resolved (Sangan, Witch of the Black Forest), Freed the Matchless General's Draw Phase
    /// search, Life Absorbing Machine's Standby effect and Spirit's Invitation's return trigger give the opponent a Chain
    /// response window when they have a legal response; with none, play is as before.
    /// </summary>
    public static class TriggerChainResponsesRegressionTests
    {
        const int ManEaterBug = 54652250;
        const int Sangan = 26202165;
        const int Witch = 78010363;
        const int Freed = 49681811;
        const int SpiritsInvitation = 92394653;
        const int LifeAbsorbingMachine = 14318794;
        const int Mst = 5318639;
        const int DarkHole = 53129443;
        const int Fissure = 66788016;
        const int Celtic = 91152256;          // Celtic Guardian: Warrior, Level 4, 1400 / 1200
        const int RingOfDestruction = 83555666;
        const int RoyalDecree = 51452091;
        const int SevenTools = 3819470;
        const int TrapMaster = 46461247;      // FLIP: destroys a Trap Card
        const int ArmedNinja = 9076207;       // FLIP: destroys a Spell Card (a Set Trap is returned)
        const int Waboku = 12607053;
        const int CurseOfFiend = 12470447;
        const int PinchHopper = 26185991;     // "When this card you control is sent to your GY: You can …"
        const int InabaWhiteRabbit = 77084837; // Spirit
        const int HaneHane = 7089711;         // FLIP: return 1 monster on the field to the hand (compiled)
        const int RoyalCommand = 33950246;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("TriggerChainResponses: " + name, ok, detail ?? "");

            CompileShape(db, Check);
            FlipSummonAnsweredByMst(db, pDeck, aDeck, Check);
            FlipSummonNobodyCanRespond(db, pDeck, aDeck, Check);
            RoyalCommandChainedToFlip(db, pDeck, aDeck, Check);
            SanganAfterDarkHole(db, pDeck, aDeck, Check);
            WitchAfterFissure(db, pDeck, aDeck, Check);
            SegocChainWithResponse(db, pDeck, aDeck, Check);
            FreedSearchAnswered(db, pDeck, aDeck, Check);
            LifeAbsorbingMachineNegated(db, pDeck, aDeck, Check);
            SpiritsInvitationAfterMst(db, pDeck, aDeck, Check);
            TwoMachinesOneChain(db, pDeck, aDeck, Check);
            TwoInvitationsOneChain(db, pDeck, aDeck, Check);
            AiAnswersSensibly(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

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

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static bool TriggerWindow(DuelEngine e, DuelistState responder, CardInstance source, int links)
        {
            var pr = e.PendingResponse;
            return pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == responder &&
                   e.Chain.Count == links && TriggerChainResponsesRuntime.IsTriggerLink(e.Chain.Last) &&
                   e.Chain.Last.Card == source && e.Chain.Last.Speed == SpellSpeed.Speed1 &&
                   e.Chain.Last.Class == EffectClass.Trigger;
        }

        static string State(DuelEngine e) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} " +
            $"pick={(e.PendingActivation != null ? e.PendingActivation.Prompt : "-")} " +
            $"resp={(e.PendingResponse != null ? e.PendingResponse.Timing + "/" + e.PendingResponse.Responder?.Name : "-")} " +
            $"chain={e.Chain.Describe()} busy={SimultaneousTriggersRuntime.Busy(e)}";

        // ───────────────────────────── compile shape ─────────────────────────────

        static void CompileShape(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var (id, timing, action) in new[]
                     {
                         (ManEaterBug, EffectTiming.Flip, EffectActionKind.Destroy),
                         (Sangan, EffectTiming.SentFromFieldToGy, EffectActionKind.AddFromDeckToHand),
                         (Witch, EffectTiming.SentFromFieldToGy, EffectActionKind.AddFromDeckToHand),
                         (Freed, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.AddFromDeckInsteadOfNormalDraw),
                         (SpiritsInvitation, EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand),
                         (LifeAbsorbingMachine, EffectTiming.StandbyPhase, EffectActionKind.GainHalfLpCostsPaidLastTurn)
                     })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check($"{def?.name ?? id.ToString()}: still Implemented, trigger clause {timing}/{action}",
                    prog != null && prog.FullyCompiled &&
                    prog.ClauseList.Exists(c => c != null && c.Timing == timing && c.Action == action) &&
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                    def != null ? CardEffectStatus.Classify(def).ToString() : "missing");
            }

            // The families the deferral reads: Sangan's "If" trigger is a Chain Link that cannot miss the timing; Pinch
            // Hopper's "When … : You can" can, so it is never deferred past a Chain.
            var sangan = CardTextEffectCompiler.Compile(db.Get(Sangan));
            var hopper = CardTextEffectCompiler.Compile(db.Get(PinchHopper));
            Check("Sangan's trigger is an \"If\" Chain Link; Pinch Hopper's is a missable \"When … you can\"",
                sangan != null && sangan.ClausesFor(EffectTiming.SentFromFieldToGy)
                    .TrueForAll(c => c.MakesChainLink && !(c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation)) &&
                hopper != null && hopper.ClausesFor(EffectTiming.SentFromFieldToGy)
                    .Exists(c => c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation), "");
        }

        // ───────────────────────────── FLIP (Flip Summon) ─────────────────────────────

        /// <summary>
        /// The opponent Flip Summons Man-Eater Bug: its FLIP effect is Chain Link 1 (target chosen), you chain a Set
        /// Mystical Space Typhoon (Chain Link 2). MST resolves first, then the Bug destroys its target.
        /// </summary>
        static void FlipSummonAnsweredByMst(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var mst = PlaceSetTrap(e, p, Mst, 0);
            var hole = PlaceSetTrap(e, o, DarkHole, 0); // a Set Normal Spell: MST's target, it cannot be chained
            var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
            e.NotifyPublic();
            var holeAt = -1;
            var celticAt = -1;
            var n = 0;
            e.OnLog += _ =>
            {
                n++;
                if (holeAt < 0 && o.Graveyard.Contains(hole)) holeAt = n;
                if (celticAt < 0 && p.Graveyard.Contains(celtic)) celticAt = n;
            };
            var flipped = reached && e.TryFlipSummon(o, bug);
            var window = TriggerWindow(e, p, bug, 1) && e.Chain.Last.Target == celtic &&
                         e.PendingResponse.LegalCards.Contains(mst) && p.TryFindMonster(celtic, out _);
            Check("Man-Eater Bug Flip Summoned: its FLIP effect is Chain Link 1 (target chosen) and you may respond",
                flipped && window, $"reached={reached} flipped={flipped} {State(e)}");

            var chained = window && e.TryActivateSpellTrap(p, mst, fromHand: false);
            if (chained && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(hole);
            PassAll(e);
            Check("Man-Eater Bug: your MST (Chain Link 2) resolves first, then the FLIP effect destroys its target",
                chained && o.Graveyard.Contains(hole) && p.Graveyard.Contains(mst) && p.Graveyard.Contains(celtic) &&
                holeAt > 0 && celticAt > holeAt && !e.Chain.HasLinks && !e.IsAwaitingResponse &&
                !e.IsAwaitingEffectTarget && e.TurnPlayer == o && e.Phase == DuelPhase.Main1,
                $"chained={chained} hole@{holeAt} celtic@{celticAt} {State(e)}");
        }

        /// <summary>Nobody can respond (no Set card): the FLIP effect resolves at once, as before — no Chain is left.</summary>
        static void FlipSummonNobodyCanRespond(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var bug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
            e.NotifyPublic();
            var count = e.Chain.ActivationCount;
            var flipped = reached && e.TryFlipSummon(o, bug);
            Check("Man-Eater Bug with nothing to chain: the FLIP effect resolves at once (no window, no Chain Link)",
                flipped && p.Graveyard.Contains(celtic) && !e.IsAwaitingResponse && !e.Chain.HasLinks &&
                e.Chain.ActivationCount == count,
                $"flipped={flipped} activations {count}->{e.Chain.ActivationCount} {State(e)}");
        }

        /// <summary>
        /// The opponent's Hane-Hane FLIP effect (Chain Link 1) targets your monster; you chain Royal Command. Royal Command
        /// negates only FLIP effects activated while it is face-up (Lua EVENT_CHAIN_ACTIVATING), so Hane-Hane resolves.
        /// </summary>
        static void RoyalCommandChainedToFlip(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var command = PlaceSetTrap(e, p, RoyalCommand, 0);
            var hane = PlaceMonster(e, o, HaneHane, 0, BattlePosition.Defense, false);
            e.NotifyPublic();
            var flipped = reached && e.TryFlipSummon(o, hane);
            var window = TriggerWindow(e, p, hane, 1) && e.Chain.Last.Target == celtic &&
                         e.PendingResponse.LegalCards.Contains(command);
            var chained = window && e.TryActivateSpellTrap(p, command, fromHand: false);
            PassAll(e);
            Check("Royal Command chained to an activated FLIP effect does not negate it: Hane-Hane still returns its target",
                flipped && window && chained && command.FaceUp && p.Hand.Contains(celtic) && !e.Chain.HasLinks &&
                !e.IsAwaitingResponse,
                $"flipped={flipped} window={window} chained={chained} hand={p.Hand.Contains(celtic)} {State(e)}");
        }

        // ───────────────────────────── "sent to the GY" after a Chain ─────────────────────────────

        /// <summary>
        /// Your Dark Hole destroys the opponent's Sangan: its trigger waits for Dark Hole's Chain, then is Chain Link 1 of
        /// a new Chain; you chain a Set MST to it. MST resolves, then Sangan searches.
        /// </summary>
        static void SanganAfterDarkHole(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var sangan = PlaceMonster(e, o, Sangan, 0, BattlePosition.Attack, true);
            var mst = PlaceSetTrap(e, p, Mst, 0);
            var oHole = PlaceSetTrap(e, o, DarkHole, 0);
            var hole = PutInHand(e, p, DarkHole);
            e.NotifyPublic();
            var hand = o.Hand.Count;
            var cast = e.Phase == DuelPhase.Main1 && e.TryActivateSpellTrap(p, hole, fromHand: true);
            var window = TriggerWindow(e, p, sangan, 1) && o.Graveyard.Contains(sangan) && o.Hand.Count == hand &&
                         p.Graveyard.Contains(hole);
            Check("Sangan destroyed by Dark Hole: its trigger is Chain Link 1 of a new Chain after Dark Hole's; you may respond",
                cast && window, $"cast={cast} hand {hand}->{o.Hand.Count} {State(e)}");

            var chained = window && e.TryActivateSpellTrap(p, mst, fromHand: false);
            if (chained && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(oHole);
            PassAll(e);
            var added = o.Hand.Count == hand + 1 ? o.Hand[o.Hand.Count - 1] : null;
            Check("Sangan: your MST (Chain Link 2) resolves, then Sangan adds a monster with 1500 or less ATK",
                chained && o.Graveyard.Contains(oHole) && p.Graveyard.Contains(mst) && added?.Def != null &&
                added.Def.IsMonster && added.Def.atk <= 1500 && !e.Chain.HasLinks && !e.IsAwaitingResponse &&
                !SimultaneousTriggersRuntime.Busy(e),
                $"chained={chained} added={added?.Name} {State(e)}");
        }

        /// <summary>
        /// Fissure destroys the opponent's Witch of the Black Forest: its trigger waits for Fissure's Chain, then you may
        /// respond to it (you pass: it searches). Pinch Hopper's "When … you can" trigger is not held past the Chain.
        /// </summary>
        static void WitchAfterFissure(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var witch = PlaceMonster(e, o, Witch, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, Mst, 0);
                PlaceSetTrap(e, o, DarkHole, 0);
                var fissure = PutInHand(e, p, Fissure);
                e.NotifyPublic();
                var hand = o.Hand.Count;
                var cast = e.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(witch);
                var window = TriggerWindow(e, p, witch, 1) && o.Graveyard.Contains(witch) && o.Hand.Count == hand &&
                             p.Graveyard.Contains(fissure);
                Check("Witch of the Black Forest destroyed by Fissure: its trigger goes on a new Chain you may respond to",
                    cast && window, $"cast={cast} {State(e)}");
                PassAll(e);
                var added = o.Hand.Count == hand + 1 ? o.Hand[o.Hand.Count - 1] : null;
                Check("Witch of the Black Forest: you pass — it adds a monster with 1500 or less DEF",
                    window && added?.Def != null && added.Def.IsMonster && added.Def.def <= 1500 && !e.Chain.HasLinks &&
                    !e.IsAwaitingResponse, $"added={added?.Name} {State(e)}");
            }

            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var hopper = PlaceMonster(e, o, PinchHopper, 0, BattlePosition.Attack, true);
                PlaceSetTrap(e, p, Mst, 0);
                PlaceSetTrap(e, o, DarkHole, 0);
                var fissure = PutInHand(e, p, Fissure);
                e.NotifyPublic();
                var cast = e.TryActivateSpellTrap(p, fissure, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(hopper);
                Check("Pinch Hopper (\"When … you can\", can miss the timing): not held past the Chain — no new window",
                    cast && o.Graveyard.Contains(hopper) && !e.IsAwaitingResponse && !e.Chain.HasLinks &&
                    !SimultaneousTriggersRuntime.Busy(e), $"cast={cast} {State(e)}");
            }
        }

        // ───────────────────────────── SEGOC Chain with a response ─────────────────────────────

        /// <summary>
        /// Your Curse of Fiend flips your and the opponent's Man-Eater Bugs: your FLIP is Chain Link 1, the opponent's
        /// Chain Link 2, and you chain a Set MST as Chain Link 3. MST resolves, then the opponent's Bug destroys yours,
        /// then your Bug's effect still destroys its target.
        /// </summary>
        static void SegocChainWithResponse(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var curse = PlaceSetTrap(e, p, CurseOfFiend, 0);
            var mst = PlaceSetTrap(e, p, Mst, 1);
            var oHole = PlaceSetTrap(e, o, DarkHole, 0);
            var myBug = PlaceMonster(e, p, ManEaterBug, 0, BattlePosition.Defense, false);
            var oppBug = PlaceMonster(e, o, ManEaterBug, 0, BattlePosition.Defense, false);
            e.TryEndTurnSafe(p);
            PassAll(e);
            e.TryEndTurnSafe(o);
            PassAll(e);
            var paused = e.Phase == DuelPhase.Standby && e.TurnPlayer == p && e.StandbyPausedFor == p;
            int n = 0, holeAt = -1, myBugAt = -1, oppBugAt = -1;
            e.OnLog += _ =>
            {
                n++;
                if (holeAt < 0 && o.Graveyard.Contains(oHole)) holeAt = n;
                if (myBugAt < 0 && p.Graveyard.Contains(myBug)) myBugAt = n;
                if (oppBugAt < 0 && o.Graveyard.Contains(oppBug)) oppBugAt = n;
            };
            var act = paused && e.TryActivateSpellTrap(p, curse, fromHand: false);
            PassAll(e);
            var asked = act && e.PendingActivation != null && e.PendingActivation.Card == myBug &&
                        e.PendingActivation.LegalTargets.Contains(oppBug);
            var picked = asked && e.TrySelectEffectTarget(oppBug);
            var window = TriggerWindow(e, p, oppBug, 2) && e.Chain.Links[0].Card == myBug &&
                         TriggerChainResponsesRuntime.IsTriggerLink(e.Chain.Links[0]) &&
                         e.Chain.Links[0].Target == oppBug && e.Chain.Last.Target == myBug &&
                         p.TryFindMonster(myBug, out _) && o.TryFindMonster(oppBug, out _);
            Check("Curse of Fiend: both FLIP effects go on one Chain (yours CL1, the opponent's CL2); you may respond",
                paused && act && picked && window, $"paused={paused} act={act} asked={asked} picked={picked} {State(e)}");

            var chained = window && e.TryActivateSpellTrap(p, mst, fromHand: false);
            if (chained && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(oHole);
            PassAll(e);
            Check("Curse of Fiend: MST (CL3) resolves, the opponent's Bug (CL2) destroys yours, your Bug (CL1) still " +
                  "destroys its target",
                chained && o.Graveyard.Contains(oHole) && p.Graveyard.Contains(myBug) && o.Graveyard.Contains(oppBug) &&
                holeAt > 0 && myBugAt > holeAt && oppBugAt > myBugAt &&
                !e.Chain.HasLinks && !e.IsAwaitingResponse && !SimultaneousTriggersRuntime.Busy(e),
                $"chained={chained} hole@{holeAt} myBug@{myBugAt} oppBug@{oppBugAt} {State(e)}");
            var main = e.CanContinueAfterStandby(p) && e.ContinueAfterStandby() && e.Phase == DuelPhase.Main1;
            Check("Curse of Fiend: after the trigger Chain, the turn goes on to Main Phase 1", main, State(e));
        }

        // ───────────────────────────── Freed the Matchless General ─────────────────────────────

        /// <summary>
        /// The opponent's Freed searches instead of drawing: you chain Ring of Destruction to it. Freed has left the field
        /// when its effect resolves: nothing is added, and the normal draw is still replaced. Passing lets it search.
        /// </summary>
        static void FreedSearchAnswered(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var answer in new[] { true, false })
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var freed = PlaceMonster(e, o, Freed, 0, BattlePosition.Attack, true);
                o.Deck.Add(Celtic);
                // A Trap: Freed negates a Spell's effect that targets it (Book of Moon would be negated and destroyed).
                var ring = PlaceSetTrap(e, p, RingOfDestruction, 0);
                e.NotifyPublic();
                var hand = o.Hand.Count;
                var deck = o.Deck.Count;
                e.TryEndTurnSafe(p);
                var window = e.TurnPlayer == o && e.Phase == DuelPhase.Draw && TriggerWindow(e, p, freed, 1) &&
                             o.Hand.Count == hand && e.PendingResponse.LegalCards.Contains(ring);
                Check($"Freed: its search instead of the draw is Chain Link 1 — the Draw Phase waits for your answer " +
                      $"({(answer ? "Ring of Destruction" : "pass")})", window, State(e));
                if (answer)
                {
                    var chained = window && e.TryActivateSpellTrap(p, ring, fromHand: false);
                    if (chained && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(freed);
                    PassAll(e);
                    Check("Freed: Ring of Destruction destroyed it first — nothing is added and there is no normal draw",
                        chained && o.Graveyard.Contains(freed) && o.Hand.Count == hand && o.Deck.Count == deck &&
                        e.TurnPlayer == o && e.Phase == DuelPhase.Main1 && !e.Chain.HasLinks,
                        $"chained={chained} hand {hand}->{o.Hand.Count} deck {deck}->{o.Deck.Count} {State(e)}");
                }
                else
                {
                    PassAll(e);
                    var added = o.Hand.Count == hand + 1 ? o.Hand[o.Hand.Count - 1] : null;
                    Check("Freed: you pass — it adds a Level 4 or lower Warrior instead of drawing",
                        window && added?.Def != null && added.Def.race == "Warrior" && added.Def.level <= 4 &&
                        o.Deck.Count == deck - 1 && e.Phase == DuelPhase.Main1 && !e.Chain.HasLinks,
                        $"added={added?.Name} deck {deck}->{o.Deck.Count} {State(e)}");
                }
            }
        }

        // ───────────────────────────── Life Absorbing Machine ─────────────────────────────

        /// <summary>
        /// The opponent's Life Absorbing Machine in their Standby Phase (they paid 1000 LP last turn): its effect is a
        /// Chain Link; Seven Tools of the Bandit cannot answer it (an effect, not a card activation). You chain Royal
        /// Decree: the effect is negated as it resolves. Passing gains 500.
        /// </summary>
        static void LifeAbsorbingMachineNegated(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var answer in new[] { true, false })
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var machine = PlaceFaceUpSpellTrap(e, o, LifeAbsorbingMachine, 0);
                var decree = PlaceSetTrap(e, p, RoyalDecree, 0);
                var tools = PlaceSetTrap(e, p, SevenTools, 1);
                e.TryEndTurnSafe(p);
                PassAll(e);
                var paid = e.TurnPlayer == o && e.Phase == DuelPhase.Main1;
                if (paid) e.PayLifePoints(o, 1000, "Test: pay 1000 LP.", isCost: true);
                e.TryEndTurnSafe(o);
                PassAll(e);
                e.TryEndTurnSafe(p);
                var lp = o.LifePoints;
                var window = paid && e.TurnPlayer == o && e.Phase == DuelPhase.Standby &&
                             TriggerWindow(e, p, machine, 1) && e.PendingResponse.LegalCards.Contains(decree) &&
                             !e.PendingResponse.LegalCards.Contains(tools);
                Check($"Life Absorbing Machine: its Standby effect is Chain Link 1; Seven Tools cannot answer it " +
                      $"({(answer ? "Royal Decree" : "pass")})", window, $"paid={paid} {State(e)}");
                if (answer)
                {
                    var chained = window && e.TryActivateSpellTrap(p, decree, fromHand: false);
                    PassAll(e);
                    Check("Life Absorbing Machine: Royal Decree chained to it negates its effect (no LP gained)",
                        chained && decree.FaceUp && o.LifePoints == lp && e.TurnPlayer == o &&
                        e.Phase == DuelPhase.Main1 && !e.Chain.HasLinks,
                        $"chained={chained} LP {lp}->{o.LifePoints} {State(e)}");
                }
                else
                {
                    PassAll(e);
                    Check("Life Absorbing Machine: you pass — it gains half of the 1000 LP paid",
                        window && o.LifePoints == lp + 500 && e.Phase == DuelPhase.Main1 && !e.Chain.HasLinks,
                        $"LP {lp}->{o.LifePoints} {State(e)}");
                }
            }
        }

        // ───────────────────────────── Spirit's Invitation ─────────────────────────────

        /// <summary>
        /// The opponent's Spirit returns in their End Phase: Spirit's Invitation's trigger is Chain Link 1 and the End
        /// Phase waits; your MST destroys Invitation first, and its effect still resolves (Lua: no IsRelateToEffect).
        /// </summary>
        static void SpiritsInvitationAfterMst(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            var rabbit = PlaceMonster(e, o, InabaWhiteRabbit, 0, BattlePosition.Attack, true);
            rabbit.NormalOrFlipSummonedTurn = e.TurnNumber;
            rabbit.SummonedThisTurn = true;
            var invitation = PlaceFaceUpSpellTrap(e, o, SpiritsInvitation, 0);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var mst = PlaceSetTrap(e, p, Mst, 0);
            e.NotifyPublic();
            var turn = e.TurnNumber;
            e.TryEndTurnSafe(o);
            var window = reached && e.TurnPlayer == o && e.Phase == DuelPhase.End && o.Hand.Contains(rabbit) &&
                         TriggerWindow(e, p, invitation, 1) && p.TryFindMonster(celtic, out _);
            Check("Spirit's Invitation: the Spirit's return makes it Chain Link 1 and the End Phase waits",
                window, $"reached={reached} {State(e)}");
            var chained = window && e.TryActivateSpellTrap(p, mst, fromHand: false);
            if (chained && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(invitation);
            PassAll(e);
            Check("Spirit's Invitation: destroyed by your MST first, its effect still returns your monster; the turn ends",
                chained && o.Graveyard.Contains(invitation) && p.Hand.Contains(celtic) && e.TurnPlayer == p &&
                e.TurnNumber == turn + 1 && !e.Chain.HasLinks,
                $"chained={chained} {State(e)}");
        }

        /// <summary>
        /// Review (v163): two face-up Life Absorbing Machines trigger in the same Standby Phase, so both are Chain Links of
        /// one Chain (CL1, CL2). Your Royal Decree chained to them negates both: no LP is gained, and none is gained before
        /// you can respond (the second used to resolve at once while your response window was open).
        /// </summary>
        static void TwoMachinesOneChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var m1 = PlaceFaceUpSpellTrap(e, o, LifeAbsorbingMachine, 0);
            var m2 = PlaceFaceUpSpellTrap(e, o, LifeAbsorbingMachine, 1);
            var decree = PlaceSetTrap(e, p, RoyalDecree, 0);
            e.TryEndTurnSafe(p);
            PassAll(e);
            var paid = e.TurnPlayer == o && e.Phase == DuelPhase.Main1;
            if (paid) e.PayLifePoints(o, 1000, "Test: pay 1000 LP.", isCost: true);
            e.TryEndTurnSafe(o);
            PassAll(e);
            var lpBefore = o.LifePoints;
            e.TryEndTurnSafe(p);
            var window = paid && e.TurnPlayer == o && e.Phase == DuelPhase.Standby && o.LifePoints == lpBefore &&
                         TriggerWindow(e, p, m2, 2) && e.Chain.Links[0].Card == m1 &&
                         TriggerChainResponsesRuntime.IsTriggerLink(e.Chain.Links[0]);
            Check("Two Life Absorbing Machines: both Standby effects are one Chain (CL1, CL2); no LP gained before you respond",
                window, $"paid={paid} lpBefore={lpBefore} now={o.LifePoints} {State(e)}");
            var chained = window && e.TryActivateSpellTrap(p, decree, fromHand: false);
            PassAll(e);
            Check("Two Life Absorbing Machines: Royal Decree chained to them negates both (no LP gained)",
                chained && o.LifePoints == lpBefore && !e.Chain.HasLinks && e.TurnPlayer == o &&
                e.Phase == DuelPhase.Main1,
                $"chained={chained} lpBefore={lpBefore} after={o.LifePoints} {State(e)}");
        }

        /// <summary>
        /// Review (v163): two Spirit's Invitations trigger on the same Spirit return, so both are Chain Links of one Chain.
        /// Nothing is returned while your response window is open; then each returns 1 of your monsters.
        /// </summary>
        static void TwoInvitationsOneChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var reached = ReachOpponentMain(e);
            var rabbit = PlaceMonster(e, o, InabaWhiteRabbit, 0, BattlePosition.Attack, true);
            rabbit.NormalOrFlipSummonedTurn = e.TurnNumber;
            rabbit.SummonedThisTurn = true;
            var inv1 = PlaceFaceUpSpellTrap(e, o, SpiritsInvitation, 0);
            var inv2 = PlaceFaceUpSpellTrap(e, o, SpiritsInvitation, 1);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            PlaceSetTrap(e, p, Mst, 0);
            e.NotifyPublic();
            var turn = e.TurnNumber;
            e.TryEndTurnSafe(o);
            var window = reached && e.TurnPlayer == o && e.Phase == DuelPhase.End && o.Hand.Contains(rabbit) &&
                         TriggerWindow(e, p, inv2, 2) && e.Chain.Links[0].Card == inv1 && p.TryFindMonster(celtic, out _);
            Check("Two Spirit's Invitations: one Chain (CL1, CL2); nothing is returned while you may respond",
                window, $"reached={reached} {State(e)}");
            PassAll(e);
            Check("Two Spirit's Invitations: you pass — your only monster returns; the turn ends",
                window && p.Hand.Contains(celtic) && e.TurnPlayer == p && e.TurnNumber == turn + 1 && !e.Chain.HasLinks,
                State(e));
        }

        // ───────────────────────────── the AI's answers ─────────────────────────────

        /// <summary>
        /// Your Trap Master's FLIP targets the AI's Set Waboku: the AI chains it (it would be destroyed). Your Armed Ninja
        /// targets it instead: Armed Ninja destroys only a Spell, so the AI keeps Waboku Set.
        /// </summary>
        static void AiAnswersSensibly(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var (flipper, label) in new[] { (TrapMaster, "Trap Master"), (ArmedNinja, "Armed Ninja") })
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var m = PlaceMonster(e, p, flipper, 0, BattlePosition.Defense, false);
                var waboku = PlaceSetTrap(e, o, Waboku, 0);
                e.NotifyPublic();
                var flipped = e.TryFlipSummon(p, m);
                if (e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(waboku))
                    e.TrySelectEffectTarget(waboku);
                PassAll(e);
                var ok = flipped && !e.Chain.HasLinks && !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget &&
                         (flipper == TrapMaster
                             ? o.Graveyard.Contains(waboku) && o.WabokuActive
                             : o.TryFindSpellTrap(waboku, out _) && !waboku.FaceUp && !o.WabokuActive);
                Check(flipper == TrapMaster
                        ? "AI: your Trap Master's FLIP targets its Set Waboku — it chains Waboku (use it or lose it)"
                        : "AI: your Armed Ninja's FLIP targets its Set Waboku (a Trap is not destroyed) — it keeps it Set",
                    ok, $"{label}: flipped={flipped} wabokuGy={o.Graveyard.Contains(waboku)} active={o.WabokuActive} {State(e)}");
            }
        }
    }
}
