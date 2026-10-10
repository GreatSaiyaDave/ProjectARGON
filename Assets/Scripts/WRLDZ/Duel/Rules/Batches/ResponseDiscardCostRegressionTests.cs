using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// A Set Spell/Trap with a "Discard 1 card" cost answering a response window (the opponent's open game state, an
    /// attack declaration) is seated on the chain by TextEffectRuntime.SeatResponseOnChain. That path never paid the
    /// discard (found 2026-09-29 with a headless probe): Spell Purification resolved against Messenger of Peace with
    /// the hand untouched, and Raigeki Break went straight to its target. Now the discard is paid as the card is
    /// activated, before any target: a player chooses the card, the AI discards its least useful one, and with
    /// nothing to discard the card cannot be activated. Cancel (at the discard or at the target after it) drops the
    /// Chain Link, turns the card face-down again, puts any discarded card back in the hand and re-offers the window.
    /// </summary>
    public static class ResponseDiscardCostRegressionTests
    {
        const int SpellPurification = 1669772;     // Normal Trap: Discard 1 card from your hand. Destroy all face-up Continuous Spells
        const int MaliceDispersion = 13626450;     // Quick-Play: Discard 1 card from your hand. Destroy all face-up Continuous Traps
        const int RaigekiBreak = 4178474;          // Normal Trap: Discard 1 card, then target 1 card on the field; destroy it
        const int PhoenixWingWindBlast = 63356631; // Normal Trap: Discard 1 card, then target 1 card your opponent controls; top of Deck
        const int MessengerOfPeace = 44656491;     // Continuous Spell (the AI pays its upkeep itself)
        const int BannerOfCourage = 10012614;      // Continuous Spell, no upkeep
        const int GravityBind = 85742772;          // Continuous Trap
        const int MysticalSpaceTyphoon = 5318639;
        const int SevenTools = 3819470;            // Counter Trap: When a Trap Card is activated: Pay 1000 LP; negate …
        const int BanisherOfTheLight = 61528025;   // Any card sent to the GY is banished instead
        const int PotOfGreed = 55144522;
        const int Kuriboh = 40640057;
        const int Celtic = 91152256;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("Response discard cost: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            foreach (var c in Cases)
            {
                PlayerChoosesTheDiscard(db, pDeck, aDeck, c, Check);
                CancelAtTheDiscard(db, pDeck, aDeck, c, Check);
            }

            foreach (var banisher in new[] { false, true })
                CancelAtTheTargetRefundsTheDiscard(db, pDeck, aDeck, banisher, Check);
            NothingToDiscard(db, pDeck, aDeck, Check);
            foreach (var id in new[] { RaigekiBreak, PhoenixWingWindBlast })
                AnswerToAnAttack(db, pDeck, aDeck, id, Check);
            foreach (var negated in new[] { false, true })
                OpponentMayRespondAfterTheCost(db, pDeck, aDeck, negated, Check);
            AiPaysItsOwnDiscard(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        sealed class Case
        {
            public int Id;
            public bool Targeted;
            /// <summary>The opponent's card the effect destroys.</summary>
            public Func<DuelEngine, CardInstance> Victim;
        }

        static readonly Case[] Cases =
        {
            new Case { Id = SpellPurification, Victim = e => FaceUpSpellTrap(e, e.Opponent, MessengerOfPeace, 0) },
            new Case { Id = MaliceDispersion, Victim = e => FaceUpSpellTrap(e, e.Opponent, GravityBind, 0) },
            new Case { Id = RaigekiBreak, Targeted = true, Victim = e => FaceUpSpellTrap(e, e.Opponent, MessengerOfPeace, 0) },
        };

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        /// <summary>A fresh duel in the player's turn with empty boards, hands and GYs.</summary>
        static DuelEngine Setup(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool humanVsHuman = false)
        {
            var e = Fresh(db, pDeck, aDeck);
            e.HumanVsHuman = humanVsHuman;
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        /// <summary>
        /// Ends the player's turn: at the start of the opponent's Main Phase 1 the player gets the open-game-state
        /// window (DuelEngine.OfferOpponentTurnTrapWindow). True when it is open for the player with
        /// <paramref name="card"/> in it.
        /// </summary>
        static bool ReachOpenState(DuelEngine e, CardInstance card)
        {
            e.TryEndTurnSafe(e.TurnPlayer);
            return OpenStateOffers(e, e.Player, card);
        }

        static bool OpenStateOffers(DuelEngine e, DuelistState who, CardInstance card)
        {
            var pr = e.PendingResponse;
            return pr != null && pr.Timing == ResponseTiming.OpponentOpenState && pr.Responder == who &&
                   e.TurnPlayer == e.OpponentOf(who) && pr.LegalCards.Contains(card);
        }

        static bool Gone(DuelistState owner, CardInstance card) =>
            !owner.TryFindSpellTrap(card, out _) && !owner.TryFindMonster(card, out _);

        static bool StillSet(DuelistState who, CardInstance card) => who.TryFindSpellTrap(card, out _) && !card.FaceUp;

        static bool DiscardPrompt(DuelEngine e, CardInstance card) =>
            e.IsAwaitingEffectTarget && e.PendingActivation is { AwaitingIgnitionCost: true } pend &&
            pend.Card == card && pend.TargetKind == EffectTargetKind.DiscardMonsterInHand &&
            (pend.Prompt ?? "").Contains("discard 1 card");

        /// <summary>Pick the discard, then (a targeted card) the target, then let the chain resolve.</summary>
        static void Finish(DuelEngine e, CardInstance discard, CardInstance target)
        {
            if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(discard);
            if (target != null && e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(target);
            Settle(e);
        }

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingResponse && e.PendingResponse.Timing == ResponseTiming.ChainResponse; i++)
                e.PassResponse();
            DrainCombat(e);
        }

        static string State(DuelEngine e)
        {
            var pend = e.PendingActivation;
            return $"pick={e.IsAwaitingEffectTarget} cost={pend?.AwaitingIgnitionCost} kind={pend?.TargetKind} " +
                   $"pr={e.PendingResponse?.Timing.ToString() ?? "-"}/{e.PendingResponse?.Responder?.Name ?? "-"} " +
                   $"chain={e.Chain.Count} turn={e.TurnPlayer?.Name} {e.Phase} hand={e.Player.Hand.Count} " +
                   $"gy={e.Player.Graveyard.Count} ban={e.Player.Banished.Count} oHand={e.Opponent.Hand.Count} " +
                   $"oGy={e.Opponent.Graveyard.Count} lp={e.Player.LifePoints}/{e.Opponent.LifePoints}";
        }

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { SpellPurification, MaliceDispersion, RaigekiBreak, PhoenixWingWindBlast })
            {
                var def = db.Get(id);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                var clause = prog?.ClausesFor(EffectTiming.Activate).Find(c => c != null && c.RequiresDiscardCost);
                var targeted = id == RaigekiBreak || id == PhoenixWingWindBlast;
                Check($"shape: {def?.name ?? id.ToString()} is Implemented; a free-chain Chain Link with a \"discard 1 " +
                      "card\" cost (any card)" + (targeted ? ", then a target" : ", no target"),
                    prog != null && prog.FullyCompiled && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented &&
                    clause != null && clause.DiscardCostAttribute == "*" && clause.DiscardCostCount <= 1 &&
                    clause.MakesChainLink && clause.RequiresTargetChoice == targeted &&
                    SpellTrapEffects.IsCompiledFreeChain(def, prog),
                    clause == null
                        ? $"full={prog?.FullyCompiled}"
                        : $"action={clause.Action} targeted={clause.RequiresTargetChoice} attr={clause.DiscardCostAttribute} " +
                          $"n={clause.DiscardCostCount} link={clause.MakesChainLink} free={SpellTrapEffects.IsCompiledFreeChain(def, prog)}");
            }
        }

        /// <summary>The bug: the opponent's Main Phase, the player answers with a Set card; nothing was discarded.</summary>
        static void PlayerChoosesTheDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Case c,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, PotOfGreed);
            var card = PlaceSetTrap(e, p, c.Id, 2);
            var victim = c.Victim(e);
            var name = card.Name;
            var window = ReachOpenState(e, card);

            var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
            var pend = e.PendingActivation;
            var offered = pend != null && pend.LegalTargets.Count == 2 && pend.LegalTargets.Contains(first) &&
                          pend.LegalTargets.Contains(chosen);
            Check($"{name} (opponent's Main Phase): activating asks which card to discard first; it is Chain Link 1 " +
                  "face-up, nothing is discarded or destroyed yet",
                act && DiscardPrompt(e, card) && offered && e.Chain.Count == 1 && e.Chain.Last?.Card == card &&
                card.FaceUp && p.Graveyard.Count == 0 && p.Hand.Count == 2 && !Gone(o, victim),
                $"window={window} act={act} offered={offered} prompt=\"{pend?.Prompt}\" {State(e)}");

            var picked = e.TrySelectEffectTarget(chosen);
            if (c.Targeted)
            {
                var tp = e.PendingActivation;
                Check($"{name}: the discard is paid before the target is chosen",
                    picked && p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && e.IsAwaitingEffectTarget &&
                    tp != null && !tp.AwaitingIgnitionCost && tp.Card == card && tp.LegalTargets.Contains(victim) &&
                    !Gone(o, victim),
                    $"picked={picked} {State(e)}");
            }

            Finish(e, null, c.Targeted ? victim : null);
            Check($"{name}: the chosen card is discarded (not the first in the hand) and the effect resolves",
                picked && p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && !p.Hand.Contains(chosen) &&
                p.Hand.Count == 1 && Gone(o, victim) && o.Graveyard.Contains(victim) && p.Graveyard.Contains(card) &&
                !e.Chain.HasLinks && !e.IsAwaitingEffectTarget && !e.IsAwaitingResponse &&
                e.TurnPlayer == o && e.InMainPhase,
                $"picked={picked} chosenGy={p.Graveyard.Contains(chosen)} victimGy={o.Graveyard.Contains(victim)} {State(e)}");
        }

        static void CancelAtTheDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Case c,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var other = PutInHand(e, p, PotOfGreed);
            var card = PlaceSetTrap(e, p, c.Id, 2);
            var victim = c.Victim(e);
            var name = card.Name;
            var window = ReachOpenState(e, card);
            var pr = e.PendingResponse;

            var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
            var prompted = DiscardPrompt(e, card);
            var cancelled = prompted && e.CancelEffectTargeting();
            Check($"{name}: Cancel at the discard undoes the activation — nothing discarded, the card Set again, no " +
                  "Chain Link, the same window offered again",
                act && prompted && cancelled && StillSet(p, card) && p.Graveyard.Count == 0 && p.Hand.Count == 2 &&
                !Gone(o, victim) && !e.Chain.HasLinks && e.PendingActivation == null &&
                ReferenceEquals(e.PendingResponse, pr) && OpenStateOffers(e, p, card),
                $"window={window} act={act} prompted={prompted} cancelled={cancelled} faceUp={card.FaceUp} {State(e)}");

            var again = e.TryActivateSpellTrap(p, card, fromHand: false);
            var promptedAgain = DiscardPrompt(e, card);
            Finish(e, first, c.Targeted ? victim : null);
            Check($"{name}: activated again from the re-offered window — it resolves, discarding the card chosen then",
                again && promptedAgain && p.Graveyard.Contains(first) && p.Hand.Contains(other) && p.Hand.Count == 1 &&
                Gone(o, victim) && p.Graveyard.Contains(card) && !e.Chain.HasLinks && !e.IsAwaitingResponse,
                $"again={again} promptedAgain={promptedAgain} {State(e)}");
        }

        /// <summary>
        /// Raigeki Break: Cancel at the target, after the discard, still undoes everything — the discarded card goes
        /// back to the hand (as a cancelled pick refunds an LP cost). With Banisher of the Light the discard was
        /// banished instead, and it comes back from there.
        /// </summary>
        static void CancelAtTheTargetRefundsTheDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool banisher,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, PotOfGreed);
            var card = PlaceSetTrap(e, p, RaigekiBreak, 2);
            var victim = FaceUpSpellTrap(e, o, MessengerOfPeace, 0);
            if (banisher) PlaceMonster(e, p, BanisherOfTheLight, 0, BattlePosition.Attack, true);
            var window = ReachOpenState(e, card);
            var pr = e.PendingResponse;

            var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
            var paid = DiscardPrompt(e, card) && e.TrySelectEffectTarget(chosen);
            var paidWhere = banisher ? p.Banished.Contains(chosen) : p.Graveyard.Contains(chosen);
            var atTarget = e.IsAwaitingEffectTarget && e.PendingActivation is { AwaitingIgnitionCost: false } tp &&
                           tp.LegalTargets.Contains(victim);
            var cancelled = atTarget && e.CancelEffectTargeting();
            Check("Raigeki Break: Cancel at the target after the discard — the discarded card " +
                  (banisher ? "(banished by Banisher of the Light) " : "") + "is back in the hand, the card Set again, " +
                  "no Chain Link, the same window offered again",
                act && paid && paidWhere && atTarget && cancelled && p.Hand.Contains(chosen) && p.Hand.Contains(first) &&
                p.Hand.Count == 2 && p.Graveyard.Count == 0 && p.Banished.Count == 0 && StillSet(p, card) &&
                !Gone(o, victim) && !e.Chain.HasLinks && e.PendingActivation == null &&
                ReferenceEquals(e.PendingResponse, pr) && OpenStateOffers(e, p, card),
                $"window={window} act={act} paid={paid} paidWhere={paidWhere} atTarget={atTarget} " +
                $"cancelled={cancelled} {State(e)}");
        }

        /// <summary>Fail closed: with an empty hand the discard cannot be paid, so the card cannot be activated.</summary>
        static void NothingToDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var id in new[] { SpellPurification, RaigekiBreak })
            {
                var e = Setup(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var card = PlaceSetTrap(e, p, id, 2);
                var mst = PlaceSetTrap(e, p, MysticalSpaceTyphoon, 3); // keeps the window open
                var victim = FaceUpSpellTrap(e, o, MessengerOfPeace, 0);
                var window = ReachOpenState(e, mst);
                var pr = e.PendingResponse;
                var offered = pr != null && pr.LegalCards.Contains(card);
                var act = e.TryActivateSpellTrap(p, card, fromHand: false);
                // The seat itself refuses too, past the window's legality check.
                var seat = SpellTrapEffects.BeginOrResolveManual(e, p, card, fromHand: false, autoPickTarget: false);
                Check($"{card.Name}: not offered and cannot be activated with no card in the hand to discard; the " +
                      "window stays open",
                    window && !offered && !act && !seat && StillSet(p, card) && !Gone(o, victim) && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget && ReferenceEquals(e.PendingResponse, pr) && p.Graveyard.Count == 0,
                    $"window={window} offered={offered} act={act} seat={seat} {State(e)}");
            }
        }

        /// <summary>
        /// The opponent attacks directly; the player answers with Raigeki Break (destroy the attacker) or Phoenix Wing
        /// Wind Blast (put it on top of the Deck): discard, then target, then the attack has no attacker.
        /// </summary>
        static void AnswerToAnAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, int id,
            Action<string, bool, string> Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.LifePoints = 8000;
            p.Hand.Clear();
            p.Graveyard.Clear();
            o.Graveyard.Clear();
            var first = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, PotOfGreed);
            var card = PlaceSetTrap(e, p, id, 0);
            var attacker = PlaceMonster(e, o, Celtic, 2, BattlePosition.Attack, true);
            attacker.ClearAttackFlags();
            e.NotifyPublic();
            if (e.IsAwaitingResponse) e.PassResponse();
            DrainCombat(e);
            var deckBefore = o.Deck.Count;
            var declared = e.TryAttack(o, attacker, null);
            var pr = e.PendingResponse;
            var window = pr != null && pr.Timing == ResponseTiming.AttackDeclared && pr.Responder == p &&
                         pr.LegalCards.Contains(card);
            var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
            var prompted = DiscardPrompt(e, card);
            var paid = prompted && e.TrySelectEffectTarget(chosen);
            var targetOffered = paid && e.IsAwaitingEffectTarget && e.PendingActivation.LegalTargets.Contains(attacker);
            var picked = targetOffered && e.TrySelectEffectTarget(attacker);
            Settle(e);
            var removed = id == RaigekiBreak
                ? o.Graveyard.Contains(attacker)
                : !o.Graveyard.Contains(attacker) && o.Deck.Count == deckBefore + 1;
            Check($"{card.Name} answering an attack: discard (the chosen card), then target the attacker; it is " +
                  (id == RaigekiBreak ? "destroyed" : "put on top of the Deck") + " and the player takes no damage",
                reached && declared && act && prompted && paid && targetOffered && picked &&
                p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && p.Hand.Count == 1 && Gone(o, attacker) &&
                removed && p.LifePoints == 8000 && p.Graveyard.Contains(card) && !e.Chain.HasLinks &&
                !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget,
                $"reached={reached} declared={declared} window={window} act={act} prompted={prompted} paid={paid} " +
                $"targetOffered={targetOffered} picked={picked} removed={removed} {State(e)}");
        }

        /// <summary>
        /// Human vs human: the discard is paid, the card is Chain Link 1 and the opponent may respond (Seven Tools of the
        /// Bandit). Passing lets it resolve; negating it keeps the discard paid.
        /// </summary>
        static void OpponentMayRespondAfterTheCost(CardDatabase db, DeckFile pDeck, DeckFile aDeck, bool negated,
            Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
            var p = e.Player;
            var o = e.Opponent;
            var first = PutInHand(e, p, Kuriboh);
            var chosen = PutInHand(e, p, Celtic);
            var card = PlaceSetTrap(e, p, SpellPurification, 2);
            var banner = FaceUpSpellTrap(e, o, BannerOfCourage, 0);
            var tools = PlaceSetTrap(e, o, SevenTools, 4);
            var window = ReachOpenState(e, card);
            var act = window && e.TryActivateSpellTrap(p, card, fromHand: false);
            var paid = DiscardPrompt(e, card) && e.TrySelectEffectTarget(chosen);
            var pr = e.PendingResponse;
            var chainWindow = pr != null && pr.Timing == ResponseTiming.ChainResponse && pr.Responder == o &&
                              pr.LegalCards.Contains(tools) && e.Chain.Count == 1 && e.Chain.Last?.Card == card &&
                              card.FaceUp;
            var costFirst = p.Graveyard.Contains(chosen) && p.Hand.Contains(first) && o.TryFindSpellTrap(banner, out _);
            var detail = $"window={window} act={act} paid={paid} chainWindow={chainWindow} costFirst={costFirst}";
            if (!negated)
            {
                Check("Spell Purification (HvH): the chosen discard is paid at activation, then the opponent may respond; " +
                      "nothing is destroyed yet",
                    act && paid && chainWindow && costFirst, $"{detail} {State(e)}");
                e.PassResponse();
                Settle(e);
                Check("Spell Purification (HvH): resolves after the opponent passes",
                    Gone(o, banner) && p.Graveyard.Contains(card) && !tools.FaceUp && !e.Chain.HasLinks &&
                    !e.IsAwaitingEffectTarget && e.PendingResponse?.Timing != ResponseTiming.ChainResponse,
                    State(e));
                return;
            }

            var answered = chainWindow && e.TryActivateSpellTrap(o, tools, fromHand: false);
            Settle(e);
            Check("Spell Purification (HvH): negated by Seven Tools of the Bandit — Banner of Courage stays, the " +
                  "discard stays paid",
                act && paid && chainWindow && costFirst && answered && o.TryFindSpellTrap(banner, out _) &&
                p.Graveyard.Contains(card) && p.Graveyard.Contains(chosen) && p.Hand.Contains(first) &&
                o.Graveyard.Contains(tools) && !e.Chain.HasLinks,
                $"{detail} answered={answered} {State(e)}");
        }

        /// <summary>
        /// The AI answers the player's open game state with Spell Purification (human vs human seats, driven through the
        /// Opponent seat, which always auto-picks): no prompt; it discards its least useful card itself.
        /// </summary>
        static void AiPaysItsOwnDiscard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = Setup(db, pDeck, aDeck, humanVsHuman: true);
            var p = e.Player;
            var o = e.Opponent;
            var card = PlaceSetTrap(e, o, SpellPurification, 2);
            var banner = FaceUpSpellTrap(e, p, BannerOfCourage, 0);
            PutInHand(e, o, Celtic);
            // To the player's next Main Phase 1, where the Opponent seat gets the open-game-state window.
            e.TryEndTurnSafe(e.TurnPlayer);
            for (var i = 0; i < 6 && !OpenStateOffers(e, o, card); i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                else e.TryEndTurnSafe(e.TurnPlayer);
            }

            var window = OpenStateOffers(e, o, card);
            o.Hand.Clear();
            var keep = PutInHand(e, o, Celtic);
            var pot = PutInHand(e, o, PotOfGreed);
            var fodder = PutInHand(e, o, Kuriboh);
            var act = window && e.TryActivateSpellTrap(o, card, fromHand: false);
            var noPrompt = !e.IsAwaitingEffectTarget;
            Settle(e);
            Check("Spell Purification (AI): no prompt; the AI discards its least useful card (Kuriboh) and destroys the " +
                  "player's Banner of Courage",
                window && act && noPrompt && o.Graveyard.Contains(fodder) && o.Hand.Contains(keep) &&
                o.Hand.Contains(pot) && o.Hand.Count == 2 && Gone(p, banner) && o.Graveyard.Contains(card) &&
                !e.Chain.HasLinks && e.TurnPlayer == p,
                $"window={window} act={act} noPrompt={noPrompt} {State(e)}");
        }
    }
}
