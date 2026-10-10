using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DiscardTriggers" (v142) runtime.
    /// <list type="bullet">
    /// <item><b>Cause of a discard / mill.</b> Every effect resolution that discards from a hand or sends cards
    /// directly from a Deck to the GY tells this class which player's card effect moved each card
    /// (<see cref="NoteDiscard"/> / <see cref="NoteSentFromDeck"/>, called from the TextEffectRuntime arms:
    /// Card Destruction / Morphing Jar, Confiscation, Robbin' Goblin and the other random discards, Drop Off,
    /// Hiro's Shadow Scout, the Duo, mills, Chain Destruction). Costs (Gravekeeper's Servant, discard costs), the
    /// hand size rule and destruction in the hand never note anything, and a card moved by an effect of a card
    /// its owner controls is ignored, so none of them trigger.</item>
    /// <item><b>Triggers.</b> A noted card whose program has <see cref="EffectTiming.DiscardedFromHandByOpponentEffect"/>
    /// or <see cref="EffectTiming.SentFromDeckToGyByOpponentEffect"/> is queued. The queue fires once the moving
    /// effect has finished resolving (<see cref="BeginScope"/> / <see cref="EndScope"/> around an effect that moves
    /// several cards, so Card Destruction's redraw comes before Electric Snake's draw) and, when that effect was a
    /// Chain Link, once the whole chain has resolved (DuelEngine.ResolveChainStack calls <see cref="Flush"/>), as a
    /// mandatory "When" trigger activates after its event. A queued card that has left the GY meanwhile does not
    /// activate. The effects resolve for the card's owner (who is "you" in its text), inline like the engine's
    /// other GY triggers (Sangan), not as a new chain the opponent can answer.</item>
    /// <item><b>Delinquent Duo</b> (<see cref="ApplyOpponentDiscardsRandomThenChosen"/>): the random discard, then
    /// the victim's own choice. A human victim chooses through the Magic Drain flow (PendingActivation with
    /// TargetKind DiscardFromHand, <see cref="PendingActivation.AwaitingForcedDiscardChoice"/>); the choice is
    /// mandatory, so Cancel discards the card the AI would. The AI chooses with <see cref="PickOwnDiscard"/>.
    /// Triggers of the random discard wait for that choice. Its 1000 LP cost is paid as the card is activated
    /// (<see cref="ActivationLpCostRuntime"/>), not when it resolves, so a negated Duo still costs the LP.</item>
    /// <item><b>Penguin Knight</b>: <see cref="ApplyShuffleGraveyardIntoDeck"/>.</item>
    /// </list>
    /// </summary>
    public static class DiscardTriggersRuntime
    {
        readonly struct Queued
        {
            public readonly DuelistState Owner;
            public readonly CardInstance Card;
            public readonly EffectTiming Timing;

            public Queued(DuelistState owner, CardInstance card, EffectTiming timing)
            {
                Owner = owner;
                Card = card;
                Timing = timing;
            }
        }

        sealed class State
        {
            /// <summary>Open <see cref="BeginScope"/> calls: triggers wait until the outermost one ends.</summary>
            public int Depth;
            public readonly List<Queued> Queue = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────── Cause tracking and triggers ─────────────────────

        /// <summary>An effect that may discard or mill several cards starts resolving: hold the triggers.</summary>
        public static void BeginScope(DuelEngine engine)
        {
            if (engine == null) return;
            StateOf(engine).Depth++;
            DiscardEvents.Begin(engine); // EngineHygiene: the cards this effect discards form one discard event
        }

        /// <summary>That effect finished resolving: fire what it triggered.</summary>
        public static void EndScope(DuelEngine engine)
        {
            if (engine == null) return;
            var st = StateOf(engine);
            if (st.Depth > 0) st.Depth--;
            DiscardEvents.End(engine);
            if (st.Depth == 0) Flush(engine);
        }

        /// <summary>
        /// <paramref name="card"/> was just discarded from <paramref name="owner"/>'s hand to the GY by an effect of
        /// a card <paramref name="byEffectOf"/> controls. Call only from effect resolutions, never for a cost.
        /// </summary>
        public static void NoteDiscard(DuelEngine engine, DuelistState owner, CardInstance card,
            DuelistState byEffectOf)
        {
            DiscardEvents.NoteCause(engine, card, byEffectOf); // EngineHygiene: the central discard event's cause
            Note(engine, owner, card, byEffectOf, EffectTiming.DiscardedFromHandByOpponentEffect);
        }

        /// <summary><see cref="NoteDiscard"/> for each card of a mass discard (Card Destruction).</summary>
        public static void NoteDiscards(DuelEngine engine, DuelistState owner, IEnumerable<CardInstance> cards,
            DuelistState byEffectOf)
        {
            if (cards == null) return;
            foreach (var c in cards)
                NoteDiscard(engine, owner, c, byEffectOf);
        }

        /// <summary>
        /// <paramref name="card"/> was just sent directly from <paramref name="owner"/>'s Deck to the GY (milled, or
        /// destroyed there) by an effect of a card <paramref name="byEffectOf"/> controls. Never for a cost.
        /// </summary>
        public static void NoteSentFromDeck(DuelEngine engine, DuelistState owner, CardInstance card,
            DuelistState byEffectOf)
        {
            // v162 cross-group audit: the GY stamp records whose card effect sent it (ygopro reason player), so Fairy
            // Guardian ("sent to your GY by your opponent's card effect") sees Spells milled by Burglars / Don Zaloog.
            if (byEffectOf != null)
                GyEntryStamps.AddReason(engine, card, GyEntryReason.Effect, byEffectOf);
            Note(engine, owner, card, byEffectOf, EffectTiming.SentFromDeckToGyByOpponentEffect);
        }

        static void Note(DuelEngine engine, DuelistState owner, CardInstance card, DuelistState byEffectOf,
            EffectTiming timing)
        {
            if (engine == null || owner == null || card?.Def == null || byEffectOf == null) return;
            // "by an effect of a card controlled by your opponent": its owner's own effects do not count.
            if (ReferenceEquals(byEffectOf, owner)) return;
            if (owner.Graveyard == null || !owner.Graveyard.Contains(card)) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled || prog.ClausesFor(timing).Count == 0) return;
            var st = StateOf(engine);
            st.Queue.Add(new Queued(owner, card, timing));
            if (st.Depth == 0) Flush(engine);
        }

        /// <summary>
        /// Fire the queued triggers, unless an effect is still resolving, a chain is still resolving (DuelEngine
        /// calls this again once the whole chain has resolved, as "When" triggers activate after the chain), or a
        /// Delinquent Duo choice is open (the random discard's triggers wait for it).
        /// </summary>
        public static void Flush(DuelEngine engine)
        {
            if (engine == null) return;
            var st = StateOf(engine);
            if (st.Depth > 0 || st.Queue.Count == 0) return;
            if (engine.IsResolvingChain) return;
            if (engine.PendingActivation != null && engine.PendingActivation.AwaitingForcedDiscardChoice) return;
            var batch = st.Queue.ToList();
            st.Queue.Clear();
            foreach (var q in batch)
            {
                if (engine.GameOver) break;
                Fire(engine, q);
            }
        }

        static void Fire(DuelEngine engine, Queued q)
        {
            // A card that already left the GY (a second Penguin Knight shuffled back by the first) does not activate;
            // nor does anything queued in a duel this engine has since restarted.
            if (!ReferenceEquals(q.Owner, engine.Player) && !ReferenceEquals(q.Owner, engine.Opponent)) return;
            if (q.Owner?.Graveyard == null || !q.Owner.Graveyard.Contains(q.Card)) return;
            var prog = CompiledEffectCache.GetOrCompile(q.Card);
            if (prog == null || !prog.FullyCompiled) return;
            var clauses = prog.ClausesFor(q.Timing);
            if (clauses.Count == 0) return;
            engine.Log(q.Timing == EffectTiming.SentFromDeckToGyByOpponentEffect
                ? $"{q.Card.Name} was sent from the Deck to the GY by the opponent's card effect — its effect activates."
                : $"{q.Card.Name} was discarded by the opponent's card effect — its effect activates.");
            foreach (var c in clauses)
            {
                if (c == null) continue;
                TextEffectRuntime.ApplyTriggeredClause(engine, q.Owner, q.Card, c);
                if (engine.GameOver) break;
            }

            engine.NotifyPublic();
        }

        // ───────────────────── Penguin Knight ─────────────────────

        /// <summary>
        /// Every card in <paramref name="who"/>'s GY returns to the Deck (an Extra Deck monster to the Extra Deck),
        /// then the Deck is shuffled (Lua: SendtoDeck of the whole GY group, then ShuffleDeck if any reached the
        /// Deck). Necrovalley keeps its locked cards in the GY.
        /// </summary>
        public static void ApplyShuffleGraveyardIntoDeck(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who?.Graveyard == null || who.Deck == null) return;
            var toDeck = 0;
            var toExtra = 0;
            foreach (var g in who.Graveyard.ToList())
            {
                if (g == null) continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g)) continue;
                who.Graveyard.Remove(g);
                if (g.IsToken) continue;
                if (g.Def != null && g.Def.IsExtraDeck)
                {
                    who.ExtraDeck?.Add(g.CardId);
                    toExtra++;
                }
                else
                {
                    who.Deck.Add(g.CardId);
                    toDeck++;
                }
            }

            if (toDeck > 0)
                engine.Rng.Shuffle(who.Deck);
            engine.Log(toExtra > 0
                ? $"{source?.Name ?? "Effect"}: {who.Name} shuffles {toDeck} card(s) from the GY into the Deck " +
                  $"({toExtra} to the Extra Deck)."
                : $"{source?.Name ?? "Effect"}: {who.Name} shuffles {toDeck} card(s) from the GY into the Deck.");
        }

        // ───────────────────── Delinquent Duo ─────────────────────

        /// <summary>
        /// The opponent of <paramref name="who"/> discards 1 card at random; then, if they still hold a card, 1 more
        /// of their choice. With one card left there is nothing to choose. A human victim with 2+ cards gets the
        /// Magic Drain-style prompt (<see cref="PendingActivation.AwaitingForcedDiscardChoice"/>). Both discards are
        /// by the effect of a card <paramref name="who"/> controls (Electric Snake / Minar trigger).
        /// </summary>
        public static void ApplyOpponentDiscardsRandomThenChosen(DuelEngine engine, DuelistState who,
            CardInstance source)
        {
            var victim = engine?.OpponentOf(who);
            if (victim?.Hand == null) return;
            var src = source?.Name ?? "Effect";
            BeginScope(engine);
            try
            {
                if (victim.Hand.Count == 0)
                {
                    engine.Log($"{src}: {victim.Name} has no cards in hand to discard.");
                    return;
                }

                // PickIndex: the same stream draw as Next(count), but test-queued picks come first.
                var i = engine.Rng.PickIndex(victim.Hand.Count);
                if (i < 0 || i >= victim.Hand.Count) i = 0;
                var gone = MonsterEffects.DiscardFromHandByInstance(victim, victim.Hand[i]);
                if (gone != null)
                {
                    engine.Log($"{src}: {victim.Name} discards {gone.Name} at random.");
                    NoteDiscard(engine, victim, gone, who);
                }

                var rest = victim.Hand.Where(h => h != null).ToList();
                if (rest.Count == 0)
                {
                    engine.Log($"{src}: {victim.Name} has no other cards in hand.");
                    return;
                }

                if (rest.Count > 1 && engine.IsHumanControlled(victim))
                {
                    var pending = new PendingActivation
                    {
                        Controller = victim,
                        Card = source,
                        FromHand = false,
                        TargetKind = EffectTargetKind.DiscardFromHand,
                        UsesTextProgram = true,
                        AwaitingForcedDiscardChoice = true
                    };
                    pending.LegalTargets.AddRange(rest);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return;
                }

                DiscardChosen(engine, victim, who, rest.Count == 1 ? rest[0] : PickOwnDiscard(rest), src);
            }
            finally
            {
                EndScope(engine);
            }
        }

        /// <summary>The human victim picked the card to discard (TrySelectEffectTarget → TryResolveTextTarget).</summary>
        public static bool FinishForcedDiscardChoice(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (engine == null || p == null || !p.AwaitingForcedDiscardChoice) return false;
            var victim = p.Controller;
            if (victim?.Hand == null) return false;
            var legal = p.LegalTargets.Where(c => c != null && victim.Hand.Contains(c)).ToList();
            if (target != null && !legal.Contains(target)) return false;
            var pick = target ?? PickOwnDiscard(legal);
            engine.ClearPendingActivation();
            if (pick != null)
                DiscardChosen(engine, victim, engine.OpponentOf(victim), pick, p.Card?.Name ?? "Effect");
            // The random discard's triggers waited for this choice.
            Flush(engine);
            engine.NotifyPublic();
            // EffectDamageEvents batch (v162): Trap of Board Eraser's discard held the resolving Chain; it goes on now
            // (no-op when no Chain waits on this choice, as for Delinquent Duo).
            engine.ResumeChainAfterChoice();
            return true;
        }

        /// <summary>The discard is not optional: Cancel discards the card the AI would choose.</summary>
        public static bool CancelForcedDiscardChoice(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingForcedDiscardChoice) return false;
            engine.Log($"{p.Card?.Name ?? "Effect"}: a card must be discarded.");
            return FinishForcedDiscardChoice(engine, p, null);
        }

        static void DiscardChosen(DuelEngine engine, DuelistState victim, DuelistState byEffectOf,
            CardInstance pick, string src)
        {
            if (pick == null) return;
            var gone = MonsterEffects.DiscardFromHandByInstance(victim, pick);
            if (gone == null) return;
            engine.Log($"{src}: {victim.Name} discards {gone.Name}.");
            NoteDiscard(engine, victim, gone, byEffectOf);
        }

        /// <summary>
        /// LP the AI keeps after paying a Delinquent Duo-style cost (<see cref="AiWantsToActivateFromHand"/>).
        /// </summary>
        public const int AiLpLeftAfterCost = 2000;

        /// <summary>
        /// SimpleAi Main Phase: cast a Spell whose effect makes the opponent discard 1 random card and then 1 of their
        /// choice (<see cref="EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice"/>, Delinquent Duo) when it
        /// takes 2 cards for 1 (the opponent holds 2 or more) and the AI keeps at least
        /// <see cref="AiLpLeftAfterCost"/> LP after paying. The caller still checks CanActivateSpellTrap.
        /// </summary>
        public static bool AiWantsToActivateFromHand(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null || !card.Def.IsSpell) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            var c = prog.ClausesFor(EffectTiming.Activate).Find(x =>
                x != null && x.Action == EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice);
            if (c == null) return false;
            var opp = engine.OpponentOf(ai);
            return opp?.Hand != null && opp.Hand.Count >= 2 &&
                   ai.LifePoints - c.PayLpAmount >= AiLpLeftAfterCost;
        }

        /// <summary>
        /// The card a player without a prompt discards when an opponent's effect makes them choose: one that gains
        /// from being discarded that way (Electric Snake / Minar) if any, else the least valuable card.
        /// </summary>
        public static CardInstance PickOwnDiscard(IList<CardInstance> hand)
        {
            if (hand == null || hand.Count == 0) return null;
            foreach (var c in hand)
            {
                if (c?.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(c);
                if (prog != null && prog.FullyCompiled &&
                    prog.ClausesFor(EffectTiming.DiscardedFromHandByOpponentEffect).Count > 0)
                    return c;
            }

            return AiHeuristicPolicy.PickLowestValue(hand, null) ?? hand[0];
        }
    }
}
