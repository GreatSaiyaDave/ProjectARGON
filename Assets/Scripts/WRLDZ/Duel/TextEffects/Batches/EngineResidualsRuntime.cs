using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EngineResiduals (v163): one human card pick for <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>,
    /// the generic pick every new choice should use instead of its own PendingActivation dictionary and
    /// TryResolvePick / TryCancelPick hooks.
    /// </summary>
    public sealed class CardPickRequest
    {
        /// <summary>The cards to choose from, read again when a queued pick opens.</summary>
        public Func<List<CardInstance>> Candidates;
        /// <summary>The picker kind the UI shows (<see cref="PendingActivation.TargetKind"/>).</summary>
        public EffectTargetKind Kind;
        /// <summary>The prompt (<see cref="PendingActivation.PromptOverride"/>).</summary>
        public string Prompt;
        /// <summary>The AI's (and a prompt-less) answer; null: the first card (or, with <see cref="AskWithOneCandidate"/>, no card).</summary>
        public Func<List<CardInstance>, CardInstance> AiChoose;
        /// <summary>Runs with the chosen card (null: nothing to choose, or a declined yes/no tap).</summary>
        public Action<CardInstance> Finish;
        /// <summary>Cancel's answer (decline, or undo the activation being made). Null: the pick is mandatory and Cancel
        /// makes the <see cref="AiChoose"/> choice.</summary>
        public Action Cancel;
        /// <summary>Ask even when only one card qualifies: a yes/no tap (Dice Re-Roll: tap the card to redo).</summary>
        public bool AskWithOneCandidate;
        /// <summary>Hold the live Standby Phase until the answer (then the upkeeps run). Null: when the chooser is the
        /// turn player in the Standby Phase.</summary>
        public bool? HoldsStandby;
        /// <summary>Hold the Chain while it resolves (<see cref="PendingActivation.HoldsChainResolution"/>).</summary>
        public bool HoldsChain = true;
        /// <summary>A cost pick made while a card is being activated: nothing is held or resumed afterwards (the
        /// activation goes on by itself).</summary>
        public bool DuringActivation;
        /// <summary>BattleDestroyedTriggers batch (Agido): the chosen card is Special Summoned, so the UI asks its face-up
        /// position and zone before the tap (SpellTrapEffects.PendingTargetIsSpecialSummon; Finish reads
        /// DuelEngine.PendingSpecialSummonPosition / PendingSpecialSummonZone).</summary>
        public bool AsksSpecialSummonPlacement;
    }

    /// <summary>
    /// EngineResiduals (v163) — the open engine residuals of ENGINE_MAP section 4:
    /// <list type="bullet">
    /// <item>Counter Traps' discard cost (Magic Jammer): a human chooses the card (<see cref="AskChainAnswerDiscard"/>).</item>
    /// <item>Free-chain cards with a plain "Discard 1 card" cost (Raigeki Break, Spell Purification, Malice Dispersion)
    /// can be chained to a Chain Link (<see cref="IsChainableDiscardCost"/>, <see cref="PayChainDiscard"/>).</item>
    /// <item>Cancel at a monster ignition's cost or target pick undoes the activation (<see cref="UndoMonsterIgnition"/>).</item>
    /// </list>
    /// </summary>
    public static class EngineResidualsRuntime
    {
        /// <summary>
        /// A free-chain card's only cost is "Discard 1 card" (any card, or a monster of an Attribute): no other cost, no
        /// random discard. TextEffectRuntime.CanActivateFreeChainOnChain lets it answer a Chain Link.
        /// </summary>
        public static bool IsChainableDiscardCost(EffectClause c) =>
            c != null && c.RequiresDiscardCost && !c.DiscardCostRandom && TextEffectRuntime.DiscardNeed(c) == 1 &&
            !c.RequiresTributeThis && c.RequiresTributeCount <= 0 && !c.RequiresDiscardSelf &&
            !c.RequiresSendThisToGy && !c.RequiresSendHandToGy && !c.RequiresSendOtherYouControl &&
            !c.RequiresSendRaceFromDeck && !c.RequiresSendNamedToGy && !c.RequiresSendEquipSpellOnThisToGy &&
            c.PayLpAmount <= 0 && c.BanishFromGyCount <= 0 && c.RequiresRemoveSpellCounters <= 0 &&
            c.RemoveSpellCountersOnYourSide <= 0; // SpellCounterSpenders batch

        /// <summary>
        /// TextEffectRuntime.ActivateFreeChainOnChain: discard 1 card for <paramref name="card"/> before it is placed as a
        /// Chain Link, then <paramref name="then"/> chains it. The AI discards its least useful card; a human picks (a lone
        /// card is discarded with no prompt); Cancel activates nothing and leaves the chain window open. If the chaining
        /// fails after the discard, the card returns to the hand.
        /// </summary>
        internal static bool PayChainDiscard(DuelEngine engine, DuelistState who, CardInstance card, EffectClause cost,
            bool human, Func<bool> stillLegal, Func<CardInstance, bool> then)
        {
            if (engine == null || who == null || card == null || cost == null || then == null) return false;
            var attribute = TextEffectRuntime.DiscardCostAttributeOf(cost);
            List<CardInstance> Fodder() => TextEffectRuntime.CollectDiscardCost(who, attribute, card);
            CardInstance AiPick(List<CardInstance> list) => TextEffectRuntime.PickAiDiscards(list, 1).FirstOrDefault();
            if (!human)
            {
                var pick = AiPick(Fodder());
                return pick != null && DiscardThen(engine, who, pick, then);
            }

            var answering = engine.PendingResponse;
            var activated = true;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Fodder,
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"discard 1 card from your hand to activate {card.Name} (cost), or Cancel to not activate it.",
                AiChoose = AiPick,
                DuringActivation = true,
                Finish = chosen =>
                {
                    if (chosen == null || !who.Hand.Contains(chosen) ||
                        !ReferenceEquals(engine.PendingResponse, answering) || stillLegal != null && !stillLegal())
                    {
                        engine.Log($"{card.Name} is not activated.");
                        activated = false;
                        engine.NotifyPublic();
                        return;
                    }

                    activated = DiscardThen(engine, who, chosen, then);
                },
                Cancel = () => engine.Log($"{card.Name} is not activated — nothing is discarded.")
            });
            return activated;
        }

        static bool DiscardThen(DuelEngine engine, DuelistState who, CardInstance chosen, Func<CardInstance, bool> then)
        {
            var gone = MonsterEffects.DiscardFromHandByInstance(who, chosen);
            if (gone == null) return false;
            engine.Log($"Cost: discard {gone.Name}.");
            if (then(gone)) return true;
            TextEffectRuntime.RefundResponseDiscards(engine, who, new List<CardInstance> { gone });
            return false;
        }

        /// <summary>
        /// DuelEngine.TryActivateChainResponse (a human's Counter Trap / monster Quick Effect with "Discard 1 card"):
        /// the player chooses the card. <paramref name="seat"/> pays the costs and seats the link with that card; a lone
        /// card is chosen with no prompt; Cancel activates nothing (the card stays Set, the window stays open). True while
        /// the pick is open or once the link is seated.
        /// </summary>
        internal static bool AskChainAnswerDiscard(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause cost, Func<CardInstance, bool> seat)
        {
            if (engine == null || who == null || card == null || cost == null || seat == null) return false;
            var attribute = TextEffectRuntime.DiscardCostAttributeOf(cost);
            List<CardInstance> Fodder() => TextEffectRuntime.CollectDiscardCost(who, attribute, card);
            if (Fodder().Count == 0) return false;
            var seated = true;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = Fodder,
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"discard 1 card from your hand to activate {card.Name} (cost), or Cancel to not activate it.",
                AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, card) ?? list.FirstOrDefault(),
                DuringActivation = true,
                Finish = chosen =>
                {
                    seated = chosen != null && who.Hand.Contains(chosen) && seat(chosen);
                    if (!seated) engine.NotifyPublic();
                },
                Cancel = () => engine.Log($"{card.Name} is not activated — nothing is discarded.")
            });
            return seated;
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveTextTarget (a monster ignition's cost pick): the discards paid at the cost pick
        /// move to the target pick that follows, so its Cancel refunds them (<see cref="UndoMonsterIgnition"/>).
        /// </summary>
        internal static bool CarryPaidDiscards(DuelEngine engine, PendingActivation costPick, bool result)
        {
            var next = engine?.PendingActivation;
            if (costPick == null || costPick.PaidDiscards.Count == 0 || !costPick.IsMonsterEffect || next == null ||
                ReferenceEquals(next, costPick) || !ReferenceEquals(next.Card, costPick.Card) || !next.IsMonsterEffect)
                return result;
            foreach (var c in costPick.PaidDiscards)
                if (!next.PaidDiscards.Contains(c))
                    next.PaidDiscards.Add(c);
            return result;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending (already cleared): Cancel at the cost or target pick of a monster effect
        /// activated through DuelEngine.TryActivateSpellTrap (Abyss Soldier) undoes the activation — its Chain Link is
        /// removed, a discard paid at the cost pick returns to the hand and a paid LP cost is refunded. False for any other
        /// monster pick (FLIP and trigger effects: their Cancel keeps its meaning).
        /// </summary>
        internal static bool UndoMonsterIgnition(DuelEngine engine, PendingActivation p)
        {
            var card = p?.Card;
            var who = p?.Controller;
            if (engine == null || who == null || card?.Def == null || !card.Def.IsMonster) return false;
            var link = engine.Chain?.Last;
            if (link == null || !ReferenceEquals(link.Card, card) || link.Resolved || link.EffectKey != "manual")
                return false;
            if (!engine.DropUnresolvedLink(card)) return false;
            if (p.PaidDiscards.Count > 0)
                TextEffectRuntime.RefundResponseDiscards(engine, who, p.PaidDiscards);
            if (p.PaidLpCost > 0)
            {
                who.LifePoints += p.PaidLpCost;
                ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, who, p.PaidLpCost);
                engine.Log($"{card.Name}: {p.PaidLpCost} LP cost refunded ({who.LifePoints} LP).");
            }

            engine.Log($"Cancelled {card.Name}'s effect — the activation is undone.");
            return true;
        }
    }
}
