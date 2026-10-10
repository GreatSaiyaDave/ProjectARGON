using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// An untargeted Spell/Trap whose cost is "Discard 1 card" (Lightning Vortex, Malice Dispersion, Spell Purification)
    /// takes the structured ignition-cost path (TextEffectRuntime.StartIgnition). Its discard was never offered to the
    /// player: the engine discarded the first card in the hand (found 2026-09-29 with a headless probe). Now a human
    /// chooses the card to discard before the card is placed, Cancel undoes the activation with nothing discarded, and the
    /// AI keeps its automatic pick. When compiled activations are seated as a Chain Link
    /// (TextEffects.CompiledActivationChainRuntime), the discard is paid at activation and the opponent may respond
    /// before the effect applies.
    /// </summary>
    public static class DiscardCostPickRegressionTests
    {
        const int LightningVortex = 69162969;   // Normal Spell: Discard 1 card; destroy all face-up monsters your opponent controls
        const int MaliceDispersion = 13626450;  // Quick-Play: Discard 1 card from your hand. Destroy all face-up Continuous Traps
        const int SpellPurification = 1669772;  // Normal Trap: Discard 1 card from your hand. Destroy all face-up Continuous Spells
        const int MessengerOfPeace = 44656491;  // Continuous Spell
        const int GravityBind = 85742772;       // Continuous Trap
        const int MagicJammer = 77414722;       // Counter Trap: When a Spell Card is activated: Discard 1 card; negate …
        const int SevenTools = 3819470;         // Counter Trap: When a Trap Card is activated: Pay 1000 LP; negate …
        const int PotOfGreed = 55144522;        // a Spell as discard fodder: "Discard 1 card" is any card
        const int Kuriboh = 40640057;
        const int Celtic = 91152256;
        const int GiantSoldier = 13039848;

        /// <summary>
        /// Compiled Spell/Trap activations are a real Chain Link once CompiledActivationChainRuntime is in the engine
        /// (its link field). Before that they resolve as they are activated. The checks below expect whichever is true.
        /// </summary>
        static readonly bool ChainSeated = typeof(ChainLink).GetField("DeferredEffect") != null;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("Discard cost pick: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            foreach (var c in Cases)
            {
                PlayerChoosesTheDiscard(db, pDeck, aDeck, c, Check);
                CancelDiscardsNothing(db, pDeck, aDeck, c, Check);
            }

            NoCardToDiscard(db, pDeck, aDeck, Check);
            AiStillPicksItself(db, pDeck, aDeck, Check);
            OpponentMayRespondAfterTheCost(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        sealed class Case
        {
            public int Id;
            public bool FromHand;
            /// <summary>The opponent's card the effect destroys.</summary>
            public Func<DuelEngine, CardInstance> Victim;
        }

        static readonly Case[] Cases =
        {
            new Case
            {
                Id = LightningVortex, FromHand = true,
                Victim = e => PlaceMonster(e, e.Opponent, GiantSoldier, 0, BattlePosition.Attack, true)
            },
            new Case { Id = MaliceDispersion, FromHand = true, Victim = e => FaceUpSpellTrap(e, e.Opponent, GravityBind) },
            new Case
            {
                Id = SpellPurification, FromHand = false,
                Victim = e => FaceUpSpellTrap(e, e.Opponent, MessengerOfPeace)
            },
        };

        /// <summary>A fresh duel with empty boards, hands and GYs; the player is in their Main Phase 1.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman = false)
        {
            var e = Fresh(db, pDeck, aDeck);
            e.HumanVsHuman = humanVsHuman;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id)
        {
            var c = PlaceSetTrap(e, who, id, 0);
            c.FaceUp = true;
            return c;
        }

        /// <summary>The card in the hand (Spells) or Set, not this turn (Spell Purification).</summary>
        static CardInstance Ready(DuelEngine e, Case c) =>
            c.FromHand ? PutInHand(e, e.Player, c.Id) : PlaceSetTrap(e, e.Player, c.Id, 2);

        static bool Destroyed(DuelistState owner, CardInstance card) =>
            owner.Graveyard.Contains(card) && !owner.TryFindMonster(card, out _) && !owner.TryFindSpellTrap(card, out _);

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            DrainCombat(e);
        }

        static string State(DuelEngine e)
        {
            var pend = e.PendingActivation;
            return $"pick={e.IsAwaitingEffectTarget} cost={pend?.AwaitingIgnitionCost} kind={pend?.TargetKind} " +
                   $"pr={e.PendingResponse?.Timing.ToString() ?? "-"}/{e.PendingResponse?.Responder?.Name ?? "-"} " +
                   $"chain={e.Chain.Count} hand={e.Player.Hand.Count} gy={e.Player.Graveyard.Count} " +
                   $"oHand={e.Opponent.Hand.Count} oGy={e.Opponent.Graveyard.Count} seated={ChainSeated}";
        }

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { LightningVortex, MaliceDispersion, SpellPurification })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate).Find(c => c != null && c.RequiresDiscardCost);
                var zoneOk = id == LightningVortex
                    ? clause?.Zone == EffectZoneFilter.OppFaceUpMonsters
                    : id == MaliceDispersion
                        ? clause != null && clause.FaceUpContinuousTrapsOnly
                        : clause != null && clause.FaceUpContinuousSpellsOnly;
                Check($"shape: {def?.name ?? id.ToString()} is Implemented; one untargeted Destroy with a " +
                      "\"discard 1 card\" cost (any card)",
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.Action == EffectActionKind.Destroy && !clause.RequiresTargetChoice &&
                    clause.DiscardCostAttribute == "*" && clause.DiscardCostCount <= 1 && zoneOk,
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"action={clause.Action} targeted={clause.RequiresTargetChoice} attr={clause.DiscardCostAttribute} " +
                          $"n={clause.DiscardCostCount} zone={clause.Zone}");
            }
        }

        /// <summary>The bug: the first card in the hand was discarded. The player picks the second one.</summary>
        static void PlayerChoosesTheDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Case c,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, PotOfGreed);
            var card = Ready(e, c);
            var victim = c.Victim(e);
            var name = card.Name;

            var act = e.TryActivateSpellTrap(p, card, c.FromHand);
            var pend = e.PendingActivation;
            var offered = pend != null && pend.LegalTargets.Contains(first) && pend.LegalTargets.Contains(chosen) &&
                          !pend.LegalTargets.Contains(card);
            var prompt = pend?.Prompt ?? "";
            Check($"{name}: activating asks which card to discard (any card, not itself); nothing is discarded or " +
                  "destroyed yet",
                act && e.IsAwaitingEffectTarget && pend.AwaitingIgnitionCost &&
                pend.TargetKind == EffectTargetKind.DiscardMonsterInHand && offered &&
                prompt.Contains("discard 1 card") && p.Graveyard.Count == 0 && p.Hand.Contains(first) &&
                p.Hand.Contains(chosen) && !Destroyed(o, victim),
                $"act={act} offered={offered} prompt=\"{prompt}\" {State(e)}");

            var picked = e.TrySelectEffectTarget(chosen);
            Settle(e);
            Check($"{name}: the chosen card is discarded, not the first in the hand, and the effect resolves",
                picked && p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && !p.Hand.Contains(chosen) &&
                Destroyed(o, victim) && p.Graveyard.Contains(card) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget &&
                !e.IsAwaitingResponse,
                $"picked={picked} chosenGy={p.Graveyard.Contains(chosen)} firstInHand={p.Hand.Contains(first)} " +
                $"victimGone={Destroyed(o, victim)} cardGy={p.Graveyard.Contains(card)} {State(e)}");
        }

        static void CancelDiscardsNothing(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Case c,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var other = PutInHand(e, p, PotOfGreed);
            var card = Ready(e, c);
            var victim = c.Victim(e);
            var name = card.Name;

            var act = e.TryActivateSpellTrap(p, card, c.FromHand);
            var prompted = e.IsAwaitingEffectTarget;
            var cancelled = prompted && e.CancelEffectTargeting();
            var back = c.FromHand
                ? p.Hand.Contains(card) && !p.TryFindSpellTrap(card, out _)
                : p.TryFindSpellTrap(card, out _) && !card.FaceUp;
            Check($"{name}: Cancel undoes the activation — nothing discarded, the card " +
                  (c.FromHand ? "back in the hand" : "still Set") + ", no Chain Link",
                act && prompted && cancelled && back && p.Graveyard.Count == 0 && p.Hand.Contains(first) &&
                p.Hand.Contains(other) && p.Hand.Count == (c.FromHand ? 3 : 2) && !Destroyed(o, victim) &&
                !e.Chain.HasLinks && e.PendingActivation == null && !e.IsAwaitingResponse,
                $"act={act} prompted={prompted} cancelled={cancelled} back={back} faceUp={card.FaceUp} {State(e)}");

            var again = e.TryActivateSpellTrap(p, card, c.FromHand);
            var pickedAgain = again && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(first);
            Settle(e);
            Check($"{name}: activated again after the Cancel — this time the first card is chosen and it resolves",
                pickedAgain && p.Graveyard.Contains(first) && p.Hand.Contains(other) && Destroyed(o, victim) &&
                p.Graveyard.Contains(card) && !e.Chain.HasLinks,
                $"again={again} pickedAgain={pickedAgain} {State(e)}");
        }

        /// <summary>Fail closed: with no other card in the hand the cost cannot be paid, so nothing activates.</summary>
        static void NoCardToDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var vortex = PutInHand(e, p, LightningVortex);
                var foe = PlaceMonster(e, e.Opponent, GiantSoldier, 0, BattlePosition.Attack, true);
                var legal = e.CanActivateSpellTrap(p, vortex, fromHand: true);
                var act = e.TryActivateSpellTrap(p, vortex, fromHand: true);
                Check("Lightning Vortex: cannot be activated with no other card in the hand to discard",
                    !legal && !act && p.Hand.Contains(vortex) && e.Opponent.TryFindMonster(foe, out _) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"legal={legal} act={act} {State(e)}");
            }

            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var trap = PlaceSetTrap(e, p, SpellPurification, 2);
                var msg = FaceUpSpellTrap(e, e.Opponent, MessengerOfPeace);
                var legal = e.CanActivateSpellTrap(p, trap, fromHand: false);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                Check("Spell Purification: cannot be activated with an empty hand",
                    !legal && !act && !trap.FaceUp && p.TryFindSpellTrap(trap, out _) &&
                    e.Opponent.TryFindSpellTrap(msg, out _) && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"legal={legal} act={act} {State(e)}");
            }
        }

        /// <summary>The AI activates Lightning Vortex on its own turn: no prompt, it pays the discard itself.</summary>
        static void AiStillPicksItself(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            o.Graveyard.Clear();
            PutInHand(e, o, Kuriboh);
            PutInHand(e, o, Celtic);
            var vortex = PutInHand(e, o, LightningVortex);
            var mine = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Attack, true);
            var act = e.TryActivateSpellTrap(o, vortex, fromHand: true);
            var noPrompt = !e.IsAwaitingEffectTarget;
            Settle(e);
            Check("Lightning Vortex (AI): no prompt; the AI discards 1 card itself and destroys the player's monster",
                reached && act && noPrompt && o.Hand.Count == 1 && o.Graveyard.Count == 2 && o.Graveyard.Contains(vortex) &&
                Destroyed(p, mine) && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                $"reached={reached} act={act} noPrompt={noPrompt} {State(e)}");
        }

        /// <summary>
        /// Human vs human, the opponent holds a Counter Trap. With the chain-seated activation the discard is paid, the
        /// card sits on the field as Chain Link 1 and the opponent may respond; before it, the effect resolves at once.
        /// </summary>
        static void OpponentMayRespondAfterTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var jammed in new[] { false, true })
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var foe = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Attack, true);
                var jam = PlaceSetTrap(e, o, MagicJammer, 4);
                PutInHand(e, o, Kuriboh); // Magic Jammer's own discard
                var first = PutInHand(e, p, Kuriboh);
                var chosen = PutInHand(e, p, Celtic);
                var vortex = PutInHand(e, p, LightningVortex);
                var act = e.TryActivateSpellTrap(p, vortex, fromHand: true);
                var picked = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(chosen);
                var costPaid = p.Graveyard.Contains(chosen) && p.Hand.Contains(first);
                var pr = e.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o &&
                             pr.LegalCards.Contains(jam) && e.Chain.Count == 1 && e.Chain.Last?.Card == vortex;
                var notYet = o.TryFindMonster(foe, out _) && p.TryFindSpellTrap(vortex, out _) && vortex.FaceUp;
                var detail = $"act={act} picked={picked} costPaid={costPaid} window={window} notYet={notYet} {State(e)}";

                if (!ChainSeated)
                {
                    if (jammed) continue;
                    Check("Lightning Vortex (no chain seat yet): the chosen discard is paid and it resolves at once",
                        act && picked && costPaid && pr == null && Destroyed(o, foe) && p.Graveyard.Contains(vortex) &&
                        !jam.FaceUp && !e.Chain.HasLinks,
                        detail);
                    continue;
                }

                if (!jammed)
                {
                    Check("Lightning Vortex (window): the chosen discard is paid at activation, then the opponent may " +
                          "respond; nothing is destroyed yet",
                        act && picked && costPaid && window && notYet, detail);
                    e.PassResponse();
                    Settle(e);
                    Check("Lightning Vortex: resolves after the opponent passes",
                        Destroyed(o, foe) && p.Graveyard.Contains(vortex) && !jam.FaceUp && o.Hand.Count == 1 &&
                        !e.Chain.HasLinks && !e.IsAwaitingResponse,
                        $"foeGone={Destroyed(o, foe)} {State(e)}");
                    continue;
                }

                var answered = window && e.TryActivateChainResponse(o, jam, fromHand: false, autoPickTarget: false);
                Settle(e);
                Check("Lightning Vortex (Jammer): Magic Jammer negates it — the monster survives, the discard stays paid",
                    act && picked && costPaid && answered && o.TryFindMonster(foe, out _) && p.Graveyard.Contains(vortex) &&
                    p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && o.Graveyard.Contains(jam) &&
                    !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    $"answered={answered} {detail} after: foeOnField={o.TryFindMonster(foe, out _)}");
            }

            // A Trap: Spell Purification Set, the opponent holds Seven Tools of the Bandit.
            if (ChainSeated)
            {
                var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
                var p = e.Player;
                var o = e.Opponent;
                var msg = FaceUpSpellTrap(e, o, MessengerOfPeace);
                var tools = PlaceSetTrap(e, o, SevenTools, 4);
                var first = PutInHand(e, p, Kuriboh);
                var chosen = PutInHand(e, p, Celtic);
                var trap = PlaceSetTrap(e, p, SpellPurification, 2);
                var act = e.TryActivateSpellTrap(p, trap, fromHand: false);
                var picked = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(chosen);
                var pr = e.PendingResponse;
                var window = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o &&
                             pr.LegalCards.Contains(tools) && e.Chain.Count == 1 && e.Chain.Last?.Card == trap;
                Check("Spell Purification (window): the chosen discard is paid, the Trap is face-up as Chain Link 1, " +
                      "the opponent may respond, Messenger of Peace is still there",
                    act && picked && window && p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && trap.FaceUp &&
                    o.TryFindSpellTrap(msg, out _),
                    $"act={act} picked={picked} window={window} {State(e)}");
                e.PassResponse();
                Settle(e);
                Check("Spell Purification: resolves after the opponent passes",
                    Destroyed(o, msg) && p.Graveyard.Contains(trap) && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                    State(e));
            }
        }
    }
}
