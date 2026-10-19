using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "EenDarkWorldDiscards" (EEN) runtime.
    /// <list type="bullet">
    /// <item><b>"If this card is discarded to the Graveyard by a card effect"</b>
    /// (<see cref="EffectTiming.DiscardedToGyByCardEffect"/>; Beiige, Broww, Goldd, Sillva). Every effect discard calls
    /// <see cref="DiscardTriggersRuntime.NoteDiscard"/> with the player whose card effect discarded (either player's; costs
    /// and the hand size rule never call it). Once that effect — and the Chain it was on — has resolved,
    /// <see cref="DiscardTriggersRuntime.Flush"/> hands each discarded card still in its owner's GY to
    /// <see cref="QueueDiscardedTrigger"/>, which queues its mandatory trigger on the simultaneous-trigger Chain
    /// (<see cref="SimultaneousTriggersRuntime.QueueTrigger"/>): two Dark World monsters discarded together (Card
    /// Destruction) go on ONE Chain, the turn player's first, and the opponent may respond. "By an opponent's card
    /// effect" = the discarding effect's player is not the owner (Lua rp ~= tp, tp == the previous controller).</item>
    /// <item>Goldd targets as its Chain Link activates (<see cref="ActivateQueuedTrigger"/>, from the Chain's activation
    /// step; the targets show on the link through <see cref="ActivationTargets"/>), only when the opponent's effect
    /// discarded it and they control a card: up to 2 of the opponent's cards, optional (Lua SelectYesNo). It resolves:
    /// Special Summon this card, then, only if it was Special Summoned, destroy the targets still on the field.</item>
    /// <item>The Special Summons are face-up in the position the owner chooses (Lua POS_FACEUP: the human taps the card
    /// and picks Summon ATK / Summon DEF; the AI takes Attack unless its DEF is higher), only while the card is still in the
    /// GY it was discarded to (IsRelateToEffect), with the engine's Special Summon gates (Necrovalley, a full field).</item>
    /// <item>Sillva: after its Special Summon, if the opponent's effect discarded it and they hold 2 or more cards, the
    /// opponent chooses exactly 2 of them, one after the other, and each goes on the bottom of their Deck in that
    /// order.</item>
    /// <item><b>Brron</b> (<see cref="OfferOwnDiscard"/>): "You can discard 1 card." — its controller picks a hand card
    /// or declines; the discard is by its own card effect, so a discarded Dark World monster triggers.</item>
    /// <item><b>Scarr</b> (<see cref="SearchSeriesMonster"/>): mandatory search of a Level 4 or lower "Dark World"
    /// monster; nothing when the Deck holds none.</item>
    /// </list>
    /// </summary>
    public static class EenDarkWorldDiscardsRuntime
    {
        /// <summary>One queued Dark World trigger.</summary>
        sealed class Activation
        {
            public DuelistState Owner;
            public CardInstance Card;
            public EffectClause Clause;
            public bool ByOpponent;
            public int GyStamp;
            public bool Activated;
            public readonly List<(CardInstance card, int stamp)> Targets = new();
        }

        static readonly ConditionalWeakTable<Action, Activation> s_runs = new();

        static bool IsDiscardedTriggerAction(EffectActionKind a) =>
            a == EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded ||
            a == EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect ||
            a == EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent ||
            a == EffectActionKind.SpecialSummonThisFromGyThenOpponentPutsHandCardsOnDeckBottomIfDiscardedByOpponent;

        /// <summary>
        /// DiscardTriggersRuntime.Flush: <paramref name="card"/> was discarded from <paramref name="owner"/>'s hand to the
        /// GY by an effect of a card <paramref name="by"/> controls, and that effect is over. Queue its trigger on the next
        /// simultaneous-trigger Chain. False when it has none (or left the GY meanwhile).
        /// </summary>
        public static bool QueueDiscardedTrigger(DuelEngine engine, DuelistState owner, CardInstance card,
            DuelistState by)
        {
            if (engine == null || owner?.Graveyard == null || card?.Def == null || !owner.Graveyard.Contains(card))
                return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = prog.ClausesFor(EffectTiming.DiscardedToGyByCardEffect)
                .FirstOrDefault(c => c != null && IsDiscardedTriggerAction(c.Action));
            if (clause == null) return false;
            var act = new Activation
            {
                Owner = owner,
                Card = card,
                Clause = clause,
                ByOpponent = by != null && !ReferenceEquals(by, owner),
                GyStamp = card.GyEntryStamp
            };
            Action run = () => Run(engine, act);
            s_runs.Add(run, act);
            engine.Log(act.ByOpponent
                ? $"{card.Name} was discarded by the opponent's card effect — its effect activates."
                : $"{card.Name} was discarded by a card effect — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, owner, card, optional: false, run);
            return true;
        }

        /// <summary>
        /// SimultaneousTriggersRuntime, a queued trigger activating as its Chain Link: Goldd's owner chooses its targets
        /// now. False for any other trigger.
        /// </summary>
        public static bool ActivateQueuedTrigger(DuelEngine engine, Action run)
        {
            if (engine == null || run == null || !s_runs.TryGetValue(run, out var act) || act.Activated) return false;
            act.Activated = true;
            if (act.Clause.Action == EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent)
                AskTargets(engine, act, duringActivation: true, then: null);
            return true;
        }

        /// <summary>The cards a queued trigger targeted as it activated (shown to responders on its Chain Link).</summary>
        public static IEnumerable<CardInstance> ActivationTargets(Action run)
        {
            if (run == null || !s_runs.TryGetValue(run, out var act)) yield break;
            foreach (var t in act.Targets)
                if (t.card != null) yield return t.card;
        }

        static void Run(DuelEngine engine, Activation act)
        {
            if (engine == null || engine.GameOver || act == null) return;
            if (!act.Activated)
            {
                // Resolved with no activation step (no Chain was built): the activation choices are made now.
                act.Activated = true;
                if (act.Clause.Action ==
                    EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent)
                {
                    AskTargets(engine, act, duringActivation: false, then: () => Resolve(engine, act));
                    return;
                }
            }

            Resolve(engine, act);
        }

        static void Resolve(DuelEngine engine, Activation act)
        {
            if (engine.GameOver) return;
            var owner = act.Owner;
            var card = act.Card;
            switch (act.Clause.Action)
            {
                case EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect:
                {
                    // Lua s.drop: the draw does not need the card to still be in the GY.
                    var n = Math.Max(1, act.Clause.Amount) + (act.ByOpponent ? 1 : 0);
                    engine.Log($"{card.Name}: {owner.Name} draws {n} card(s)" +
                               (act.ByOpponent ? " (discarded by the opponent's card effect)." : "."));
                    engine.Draw(owner, n);
                    engine.NotifyPublic();
                    break;
                }
                case EffectActionKind.SpecialSummonThisFromGyFaceUpWhenDiscarded:
                    SummonThis(engine, act, null);
                    break;
                case EffectActionKind.SpecialSummonThisFromGyThenDestroyUpToTargetsIfDiscardedByOpponent:
                    SummonThis(engine, act, summoned =>
                    {
                        if (summoned) DestroyTargets(engine, act);
                        else if (act.Targets.Count > 0)
                            engine.Log($"{card.Name} was not Special Summoned — its targets are not destroyed.");
                    });
                    break;
                case EffectActionKind.SpecialSummonThisFromGyThenOpponentPutsHandCardsOnDeckBottomIfDiscardedByOpponent:
                    SummonThis(engine, act, summoned =>
                    {
                        if (summoned && act.ByOpponent)
                            OpponentPutsHandCardsOnBottom(engine, owner, card, Math.Max(1, act.Clause.Amount));
                    });
                    break;
            }
        }

        // ───────────────────────────── Special Summon this card ─────────────────────────────

        /// <summary>Lua IsRelateToEffect: the same card, still in the GY it was discarded to.</summary>
        static bool StillInGy(Activation act) =>
            act.Owner?.Graveyard != null && act.Owner.Graveyard.Contains(act.Card) && act.Card.GyEntryStamp == act.GyStamp;

        static BattlePosition AiPosition(CardInstance c) =>
            c?.Def != null && c.Def.def > c.Def.atk ? BattlePosition.Defense : BattlePosition.Attack;

        static void SummonThis(DuelEngine engine, Activation act, Action<bool> then)
        {
            var owner = act.Owner;
            var card = act.Card;
            if (!StillInGy(act))
            {
                engine.Log($"{card.Name} is no longer in the GY — it is not Special Summoned.");
                then?.Invoke(false);
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log($"{card.Name}: Necrovalley prevents it from leaving the GY.");
                then?.Invoke(false);
                return;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, card, card, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{card.Name} cannot be Special Summoned now — it stays in the GY.");
                then?.Invoke(false);
                return;
            }

            var human = engine.IsHumanControlled(owner);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, owner, card, new CardPickRequest
            {
                Candidates = () => StillInGy(act) ? new List<CardInstance> { card } : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{card.Name}: Special Summon it from your GY — tap it, then Summon ATK or Summon DEF (face-up).",
                AskWithOneCandidate = human,
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    var pos = human && engine.PendingSpecialSummonPosition.HasValue
                        ? engine.PendingSpecialSummonPosition.Value
                        : AiPosition(card);
                    var zone = human ? engine.PendingSpecialSummonZone : -1;
                    engine.ClearPendingSpecialSummonPlacement();
                    var ok = pick != null && SummonFromGy(engine, act, pos, zone);
                    then?.Invoke(ok);
                    engine.NotifyPublic();
                }
            });
        }

        static bool SummonFromGy(DuelEngine engine, Activation act, BattlePosition pos, int zone)
        {
            var owner = act.Owner;
            var card = act.Card;
            if (!StillInGy(act))
            {
                engine.Log($"{card.Name} is no longer in the GY — it is not Special Summoned.");
                return false;
            }

            if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, owner, card, card, SummonOrigin.Graveyard,
                    requireZone: true))
            {
                engine.Log($"{card.Name} cannot be Special Summoned now — it stays in the GY.");
                return false;
            }

            owner.Graveyard.Remove(card);
            DuelEngine.RecordOwnerOnFieldEntry(card, owner, owner);
            engine.NextSpecialSummonFromGraveyard = true;
            if (engine.SpecialSummonToField(owner, card, pos, true, SummonKind.SpecialSummon, zone))
            {
                engine.Log($"{card.Name}: {owner.Name} Special Summons it from the GY in face-up " +
                           $"{(card.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                return true;
            }

            card.ControlOwner = null;
            if (!owner.Graveyard.Contains(card)) owner.Graveyard.Add(card);
            engine.Log($"{card.Name} could not be Special Summoned — it stays in the GY.");
            return false;
        }

        // ───────────────────────────── Goldd: up to 2 targets, destroyed after the Summon ─────────────────────────────

        static EffectClause DestroyClause(Activation act) => new EffectClause
        {
            Timing = act.Clause.Timing,
            Action = EffectActionKind.Destroy,
            Side = EffectSide.Opponent,
            Zone = EffectZoneFilter.OppAnyCardOnField,
            RequiresTargetChoice = true,
            MakesChainLink = true,
            SourceSnippet = act.Clause.SourceSnippet
        };

        /// <summary>
        /// Lua s.sptg: only when the opponent's effect discarded it and they control a card, the owner may target 1 to
        /// <see cref="EffectClause.Amount"/> of their cards (a human taps them one by one, Cancel = no target / no more;
        /// the AI takes the most harmful ones it can).
        /// </summary>
        static void AskTargets(DuelEngine engine, Activation act, bool duringActivation, Action then)
        {
            var owner = act.Owner;
            var card = act.Card;
            var clause = DestroyClause(act);
            var max = Math.Max(1, act.Clause.Amount);
            List<CardInstance> Legal() => TextEffectRuntime.CollectTriggerTargets(engine, owner, clause, card)
                .Where(c => c != null && !act.Targets.Exists(t => ReferenceEquals(t.card, c))).ToList();

            if (!act.ByOpponent || Legal().Count == 0)
            {
                then?.Invoke();
                return;
            }

            void Take(CardInstance pick)
            {
                act.Targets.Add((pick, pick.FieldEntryStamp));
                engine.Log($"{card.Name}: {owner.Name} targets {pick.Name}.");
            }

            if (!engine.IsHumanControlled(owner))
            {
                foreach (var pick in TextEffectRuntime.AutoPickTriggerTargets(engine, owner, clause, Legal(), max))
                    if (pick != null) Take(pick);
                then?.Invoke();
                return;
            }

            void Next()
            {
                if (act.Targets.Count >= max || Legal().Count == 0)
                {
                    then?.Invoke();
                    return;
                }

                var first = act.Targets.Count == 0;
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, owner, card, new CardPickRequest
                {
                    Candidates = Legal,
                    Kind = TextEffectRuntime.TargetKindOf(clause),
                    Prompt = first
                        ? $"{card.Name}: you can target up to {max} cards your opponent controls — they are destroyed " +
                          "after it is Special Summoned (Cancel = no target)."
                        : $"{card.Name}: you can target 1 more card your opponent controls (Cancel = no more).",
                    AskWithOneCandidate = true,
                    DuringActivation = duringActivation,
                    AiChoose = list => TextEffectRuntime.AutoPickTriggerTargets(engine, owner, clause, list, 1)
                        .FirstOrDefault(),
                    Cancel = () => then?.Invoke(),
                    Finish = pick =>
                    {
                        if (pick == null)
                        {
                            then?.Invoke();
                            return;
                        }

                        Take(pick);
                        Next();
                    }
                });
            }

            Next();
        }

        /// <summary>Lua s.spop after BreakEffect: destroy the targets still related (on the field since targeted).</summary>
        static void DestroyTargets(DuelEngine engine, Activation act)
        {
            if (act.Targets.Count == 0) return;
            var live = act.Targets
                .Where(t => t.card != null && engine.ControllerOf(t.card) != null && t.card.FieldEntryStamp == t.stamp)
                .Select(t => t.card).ToList();
            if (live.Count == 0)
            {
                engine.Log($"{act.Card.Name}: its targets are no longer on the field — nothing is destroyed.");
                return;
            }

            var clause = DestroyClause(act);
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
                TextEffectRuntime.ApplyToTriggerTargets(engine, act.Owner, act.Card, clause, live));
            engine.NotifyPublic();
        }

        // ───────────────────────────── Sillva: the opponent's hand to the Deck bottom ─────────────────────────────

        static void OpponentPutsHandCardsOnBottom(DuelEngine engine, DuelistState owner, CardInstance source, int n)
        {
            var opp = engine.OpponentOf(owner);
            if (opp?.Hand == null) return;
            // Lua: GetFieldGroupCount(tp, 0, LOCATION_HAND) > 1 — with fewer cards nothing happens.
            if (opp.Hand.Count(h => h != null) < n)
            {
                engine.Log($"{source.Name}: {opp.Name} holds fewer than {n} cards — none are returned.");
                return;
            }

            var chosen = new List<CardInstance>();
            List<CardInstance> Left() => opp.Hand.Where(h => h != null && !chosen.Contains(h)).ToList();

            void Done()
            {
                foreach (var c in chosen)
                {
                    if (!opp.Hand.Remove(c)) continue;
                    // A card held by a player who does not own it goes to its owner's Deck (PerCopyDeck batch).
                    PerCopyDeckRuntime.OwnersPile(opp, c).Deck.Add(c.CardId);
                }

                engine.Log($"{source.Name}: {opp.Name} places {chosen.Count} card(s) from the hand on the bottom of " +
                           "the Deck.");
                engine.NotifyPublic();
            }

            void Next()
            {
                if (chosen.Count >= n || Left().Count == 0)
                {
                    Done();
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
                {
                    Candidates = Left,
                    Kind = EffectTargetKind.DiscardFromHand,
                    Prompt = $"your opponent's {source.Name}: choose {n - chosen.Count} more card(s) in your hand to put " +
                             "on the bottom of your Deck (in the order you choose).",
                    AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, null) ?? list.FirstOrDefault(),
                    Finish = c =>
                    {
                        if (c == null)
                        {
                            Done();
                            return;
                        }

                        chosen.Add(c);
                        Next();
                    }
                });
            }

            Next();
        }

        // ───────────────────────────── Brron: you can discard 1 card ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.YouCanDiscardCardsOfYourChoice"/> (Brron's battle-damage trigger): its controller can
        /// discard a hand card of their choice (a human taps it or Cancels; the AI only discards a card that gains from an
        /// effect discard). Needs a hand card to activate (Lua s.target chk).
        /// </summary>
        public static void OfferOwnDiscard(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Hand == null || clause == null || engine.GameOver) return;
            var name = source?.Name ?? "Effect";
            if (!who.Hand.Exists(h => h != null))
            {
                engine.Log($"{name}: {who.Name} has no card in hand — its effect cannot be activated.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Hand.Where(h => h != null).ToList(),
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"{name}: you can discard 1 card (Cancel = do not use the effect).",
                AskWithOneCandidate = true,
                AiChoose = AiOwnDiscard,
                Cancel = () => engine.Log($"{who.Name} does not use {name}'s effect."),
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{who.Name} does not use {name}'s effect.");
                        return;
                    }

                    DiscardOwn(engine, who, name, pick);
                }
            });
        }

        static void DiscardOwn(DuelEngine engine, DuelistState who, string name, CardInstance pick)
        {
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                var gone = MonsterEffects.DiscardFromHandByInstance(who, pick, DiscardCause.Effect, who);
                if (gone == null) return;
                engine.Log($"{name}: {who.Name} discards {gone.Name}.");
                DiscardTriggersRuntime.NoteDiscard(engine, who, gone, who);
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            engine.NotifyPublic();
        }

        /// <summary>The AI discards for Brron only a card whose own effect starts when it is discarded by a card effect.</summary>
        static CardInstance AiOwnDiscard(List<CardInstance> hand)
        {
            CardInstance best = null;
            var bestRank = 0;
            foreach (var c in hand)
            {
                if (c?.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(c);
                if (prog == null || !prog.FullyCompiled) continue;
                var clause = prog.ClausesFor(EffectTiming.DiscardedToGyByCardEffect)
                    .FirstOrDefault(x => x != null && IsDiscardedTriggerAction(x.Action));
                if (clause == null) continue;
                var rank = clause.Action == EffectActionKind.DrawPlusOneIfDiscardedByOpponentEffect ? 1 : 2;
                if (rank > bestRank || (rank == bestRank && best != null && (c.Def.atk > best.Def.atk)))
                {
                    best = c;
                    bestRank = rank;
                }
            }

            return best;
        }

        // ───────────────────────────── Scarr: search a low-Level series monster ─────────────────────────────

        /// <summary>One copy per distinct card in <paramref name="who"/>'s Deck: a monster of the series, Level 1 to max.</summary>
        static List<CardInstance> SeriesCandidates(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(clause?.NamedCard)) return list;
            var max = clause.Amount > 0 ? clause.Amount : 4;
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!def.IsMonster || def.IsExtraDeck || def.level <= 0 || def.level > max) continue;
                if (!TextEffectRuntime.CardMatchesSeries(def, clause.NamedCard)) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        /// <summary>
        /// <see cref="EffectActionKind.AddSeriesMonsterLevelLeqFromDeckToHand"/> (Scarr): its controller adds 1 Level
        /// <see cref="EffectClause.Amount"/> or lower <see cref="EffectClause.NamedCard"/> monster from the Deck to the hand
        /// (mandatory: a human picks among the candidates, Cancel takes the AI's choice); then the Deck is shuffled.
        /// </summary>
        public static void SearchSeriesMonster(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            var name = source?.Name ?? "Effect";
            if (SeriesCandidates(engine, who, clause).Count == 0)
            {
                engine.Log($"{name}: {who.Name} has no Level {clause.Amount} or lower \"{clause.NamedCard}\" monster in " +
                           "the Deck — nothing is added.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => SeriesCandidates(engine, who, clause),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"{name}: add 1 Level {clause.Amount} or lower \"{clause.NamedCard}\" monster from your Deck to " +
                         "your hand.",
                AiChoose = list => list.OrderByDescending(x => x.Def?.atk ?? 0).ThenBy(x => x.Name).FirstOrDefault(),
                Finish = chosen =>
                {
                    if (chosen?.Def == null) return;
                    var idx = who.Deck.IndexOf(chosen.CardId);
                    if (idx < 0) return;
                    PerCopyDeckRuntime.TakeFromDeck(who, idx, chosen);
                    who.Hand.Add(chosen);
                    engine.Rng.Shuffle(who.Deck);
                    engine.Log($"{name}: {who.Name} adds {chosen.Name} (Lv{chosen.Def.level}) from the Deck to the hand.");
                    engine.NotifyPublic();
                }
            });
        }
    }
}
