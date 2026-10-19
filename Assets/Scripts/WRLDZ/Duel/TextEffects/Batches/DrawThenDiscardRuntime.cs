using System;
using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Runtime for <see cref="EffectActionKind.DrawThenDiscard"/> (Graceful Charity, <see cref="DrawThenDiscardAtoms"/>).
    /// <list type="bullet">
    /// <item>Activation: legal only while the controller can draw Amount cards (<see cref="CanDrawAmount"/>).</item>
    /// <item>Resolution: draw Amount; only if every card was drawn (a "cannot draw" effect or a short Deck stops it),
    ///   discard <see cref="EffectClause.ThenDiscardCount"/> cards of the controller's choice as a separate step after
    ///   the draw. A human selects them through a <see cref="PendingActivation"/>
    ///   (<see cref="PendingActivation.AwaitingDrawThenDiscard"/>); Cancel cannot skip the discard, it completes the
    ///   selection with the lowest-value cards. The AI discards its lowest <see cref="AiHeuristicPolicy.DiscardPriority"/>
    ///   cards. The chosen cards go to the GY together (ygopro DiscardHand, REASON_EFFECT|REASON_DISCARD).</item>
    /// </list>
    /// A Normal Spell is always Chain Link 1, the last link to resolve, so the pick never holds up another link.
    /// Because the discard happens after the draw, a "when your opponent draws" card misses the timing (OCG ruling).
    /// </summary>
    public static class DrawThenDiscardRuntime
    {
        /// <summary>ActivationCondition: ygopro <c>IsPlayerCanDraw(tp, Amount)</c>.</summary>
        public const string CanDrawAmount = "ControllerCanDrawAmount";

        /// <summary>The Deck holds <paramref name="count"/>+ cards and no "cannot draw" effect applies to <paramref name="who"/>.</summary>
        public static bool CanDraw(DuelEngine engine, DuelistState who, int count)
        {
            if (engine == null || who?.Deck == null) return false;
            return who.Deck.Count >= Math.Max(1, count) &&
                   !ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, who) &&
                   // ChoiceAmongEffects batch: Penalty Game!'s Draw Phase lock.
                   !ChoiceAmongEffectsRuntime.CannotDrawNow(engine, who);
        }

        /// <summary>ApplyClause arm: draw, then start (or, for the AI, make) the discard.</summary>
        public static void Resolve(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Hand == null || who.Deck == null || clause == null || engine.GameOver) return;
            var draw = Math.Max(1, clause.Amount);
            var deckBefore = who.Deck.Count;
            engine.Draw(who, draw, silent: false);
            if (engine.GameOver) return;
            var drawn = deckBefore - who.Deck.Count;
            if (drawn < draw)
            {
                engine.Log($"{source?.Name}: {who.Name} drew {drawn} of {draw} card(s) — nothing is discarded.");
                return;
            }

            StartChosenDiscard(engine, who, source, Math.Max(1, clause.ThenDiscardCount));
        }

        /// <summary>
        /// <paramref name="who"/> discards <paramref name="count"/> cards of their choice (all of them when they hold that
        /// many or fewer): a human picks them (<see cref="PendingActivation.AwaitingDrawThenDiscard"/>), the AI discards its
        /// lowest-value cards. Also Forced Requisition's discard (EngineHygiene batch): <paramref name="byEffectOf"/> is
        /// the player whose card's effect makes <paramref name="who"/> discard; when that is <paramref name="who"/>'s
        /// opponent the cards are discarded "by an opponent's card effect" (Minar, Electric Snake —
        /// <see cref="DiscardTriggersRuntime.NoteDiscard"/>). Null (Graceful Charity) = <paramref name="who"/>'s own effect.
        /// </summary>
        public static void StartChosenDiscard(DuelEngine engine, DuelistState who, CardInstance source, int count,
            DuelistState byEffectOf = null)
        {
            if (engine == null || who?.Hand == null || engine.GameOver) return;
            count = Math.Max(1, count);
            var hand = who.Hand.Where(h => h != null).ToList();
            if (hand.Count <= count)
            {
                Discard(engine, who, source, hand, byEffectOf);
                return;
            }

            if (engine.IsHumanControlled(who))
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingDrawThenDiscard = true,
                    TargetKind = EffectTargetKind.DiscardFromHand,
                    TargetPicksRemaining = count
                };
                pending.LegalTargets.AddRange(hand);
                if (byEffectOf != null) s_byEffectOf.AddOrUpdate(pending, byEffectOf);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            Discard(engine, who, source, LowestValue(hand, count), byEffectOf);
        }

        /// <summary>The discarding effect's controller of an open human pick (<see cref="StartChosenDiscard"/>).</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PendingActivation, DuelistState> s_byEffectOf =
            new();

        static DuelistState ByEffectOf(PendingActivation p) =>
            p != null && s_byEffectOf.TryGetValue(p, out var by) ? by : null;

        /// <summary>TextEffectRuntime.TryResolveTextTarget: one card chosen; the last pick discards them all.</summary>
        public static bool ContinueDiscardPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (engine == null || p == null || target == null || !p.LegalTargets.Contains(target)) return false;
            p.LegalTargets.Remove(target);
            p.ChosenTargets.Add(target);
            p.TargetPicksRemaining--;
            engine.Log($"{p.Card?.Name}: {target.Name} chosen to discard.");
            if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
            {
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var who = p.Controller;
            var source = p.Card;
            var picks = p.ChosenTargets.ToList();
            engine.ClearPendingActivation();
            Discard(engine, who, source, picks, ByEffectOf(p));
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: the discard belongs to a resolving effect and cannot be skipped — the
        /// remaining picks are the lowest-value cards still offered.
        /// </summary>
        public static bool AutoCompleteDiscard(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingDrawThenDiscard) return false;
            engine.Log($"{p.Card?.Name}: the discard cannot be skipped — the remaining cards are chosen.");
            foreach (var pick in LowestValue(p.LegalTargets, p.TargetPicksRemaining))
                if (engine.PendingActivation != p || !ContinueDiscardPick(engine, p, pick))
                    break;
            if (engine.PendingActivation == p)
            {
                // Nothing left to offer: discard what was chosen.
                var picks = p.ChosenTargets.ToList();
                engine.ClearPendingActivation();
                Discard(engine, p.Controller, p.Card, picks, ByEffectOf(p));
            }

            return true;
        }

        static List<CardInstance> LowestValue(IEnumerable<CardInstance> cards, int count)
        {
            var pool = cards.Where(c => c != null).ToList();
            var picks = new List<CardInstance>();
            for (var i = 0; i < count; i++)
            {
                var c = AiHeuristicPolicy.PickLowestValue(pool, null);
                if (c == null) break;
                picks.Add(c);
                pool.Remove(c);
            }

            return picks;
        }

        /// <summary>Send the chosen hand cards to the GY together (a card no longer in the hand is skipped).</summary>
        static void Discard(DuelEngine engine, DuelistState who, CardInstance source, List<CardInstance> cards,
            DuelistState byEffectOf = null)
        {
            if (engine == null || who?.Hand == null || cards == null) return;
            var gone = new List<string>();
            // Discarded by an effect of the opponent's card (Forced Requisition) or of its own (Graceful Charity): one
            // discard event whose "discarded / sent from your hand to your GY by a card effect" triggers fire once all the
            // cards are gone (EenDarkWorldDiscards batch: the Dark World triggers start on either player's effect).
            var by = byEffectOf ?? who;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                foreach (var c in cards)
                {
                    if (c == null || !who.Hand.Contains(c)) continue;
                    engine.SendCardToGrave(who, c, sentBy: source);
                    DiscardEvents.Record(engine, who, c, DiscardCause.Effect, byEffectOf);
                    DiscardTriggersRuntime.NoteDiscard(engine, who, c, by);
                    gone.Add(c.Name);
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            if (gone.Count > 0)
                engine.Log($"{who.Name} discards {string.Join(", ", gone)} ({source?.Name}).");
            engine.NotifyPublic();
        }
    }
}
