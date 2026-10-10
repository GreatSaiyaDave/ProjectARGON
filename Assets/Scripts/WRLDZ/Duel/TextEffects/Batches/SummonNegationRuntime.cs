using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonNegation" (v141) runtime: Counter Traps that negate a Summon or an activation.
    /// <list type="bullet">
    /// <item>Solemn Judgment / Horn of Heaven answer the summon-negation window
    /// (<see cref="ResponseTiming.SummonNegation"/>), which DuelEngine opens after a Normal / Tribute / Flip
    /// Summon (or a Special Summon by procedure) places the monster and before that Summon succeeds. The
    /// activation pays its cost and is seated on the chain; its resolution negates the Summon
    /// (<see cref="DuelEngine.NegateSummonInProgress"/>). A negated Summon never succeeded.</item>
    /// <item>Solemn Judgment's other half and Magic Drain answer a Chain Link (<see cref="EffectTiming.ChainLinkActivated"/>)
    /// through the existing Counter Trap path (DuelEngine.TryActivateChainResponse).</item>
    /// <item>Magic Drain: when its negation resolves, the answered player may discard 1 Spell Card to negate
    /// Magic Drain's effect instead; a human's choice pauses the chain (<see cref="PendingActivation.AwaitingSpellDiscardWaiver"/>).</item>
    /// </list>
    /// </summary>
    public static class SummonNegationRuntime
    {
        /// <summary>The AI pays half its LP (Solemn Judgment) only while it has at least this much.</summary>
        public const int AiHalfLpMinLife = 2000;

        /// <summary>The AI negates a Summon only of a monster this strong (or a Level 5+ Tribute-size monster).</summary>
        public const int AiSummonNegationMinAtk = 1500;

        public enum WaiverOutcome
        {
            /// <summary>No choice to make (no Spell in hand, or nothing left to negate): the negation applies.</summary>
            Proceed,
            /// <summary>The answered player discarded a Spell: this negation effect is negated.</summary>
            Waived,
            /// <summary>A human is choosing; the chain is paused.</summary>
            AwaitingChoice
        }

        // ── Costs ──

        /// <summary>"Pay half your LP" (rounded down, Lua math.floor(GetLP/2)); always payable.</summary>
        public static void PayHalfLp(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            var half = who.LifePoints / 2;
            if (half > 0)
                engine.PayLifePoints(who, half, $"Cost: pay half your LP ({half}).");
            else
                engine.Log("Cost: pay half your LP (0).");
        }

        public static bool AiWillPayHalfLp(DuelistState who) =>
            who != null && who.LifePoints >= AiHalfLpMinLife;

        /// <summary>The AI does not pay half its LP to stop a burn smaller than this.</summary>
        public const int AiHalfLpMinBurn = 1000;

        /// <summary>
        /// AI: Solemn Judgment's Spell/Trap half costs half the LP, so the AI spends it only on an activation that
        /// matters. Not worth it: an activation whose every effect only gains LP, pays LP, places counters, renames
        /// a card, or burns less than <see cref="AiHalfLpMinBurn"/> (Red Medicine, Dian Keto, Hinotama); the AI keeps
        /// Solemn for a Summon. Everything else is worth it, including a card with no compiled program.
        /// </summary>
        public static bool AiWorthHalfLpNegation(ChainLink answered)
        {
            var def = answered?.Card?.Def;
            if (def == null) return true;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled || prog.ClauseList.Count == 0) return true;
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                switch (EffectVocabulary.ResolutionOf(c))
                {
                    case EffectResolutionKind.GainLp:
                    case EffectResolutionKind.GainLpReplacement:
                    case EffectResolutionKind.PayLp:
                    case EffectResolutionKind.PlaceCounters:
                    case EffectResolutionKind.TreatAsName:
                        continue;
                    case EffectResolutionKind.Damage when c.Amount > 0 && c.Amount < AiHalfLpMinBurn:
                        continue;
                    default:
                        return true;
                }
            }

            return false;
        }

        static void TributeForCost(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            ReleaseEvents.Record(engine, pick, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(who, pick, sentBy: source);
            engine.Log($"Cost: Tribute {pick.Name}.");
        }

        /// <summary>The cheapest monster to Tribute: lowest ATK, the source card last.</summary>
        static CardInstance CheapestTribute(List<CardInstance> tributes) =>
            tributes == null || tributes.Count == 0
                ? null
                : tributes.OrderBy(m => m.IsToken ? 0 : 1).ThenBy(m => m.CurrentAtk).First();

        // ── Summon negation window ──

        /// <summary>
        /// A "When a monster(s) would be Summoned" clause may answer the open summon-negation window: a Summon is
        /// waiting in it, and the cost can be paid (Horn of Heaven needs a monster it may Tribute).
        /// </summary>
        public static bool CanNegatePendingSummon(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out string reason)
        {
            var pr = engine?.PendingResponse;
            var summoned = pr?.Summoned;
            if (pr == null || pr.Timing != ResponseTiming.SummonNegation || summoned == null ||
                engine.SummonAwaitingSuccess != summoned)
            {
                reason = "No Summon is waiting to be negated.";
                return false;
            }

            if (c == null || c.Action != EffectActionKind.NegateSummon)
            {
                reason = "Not a Summon negation.";
                return false;
            }

            if (card == null || !who.TryFindSpellTrap(card, out _) || card.FaceUp)
            {
                reason = "Counter Trap must be Set on the field.";
                return false;
            }

            if (c.RequiresTributeCount > 0)
            {
                if (ContinuousProtections.NeitherPlayerCanTribute(engine))
                {
                    reason = "Neither player can Tribute cards (Mask of Restrict).";
                    return false;
                }

                if (TextEffectRuntime.CollectTributeCost(who, card, c).Count < c.RequiresTributeCount)
                {
                    reason = "No monster to Tribute.";
                    return false;
                }
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// Activate <paramref name="card"/> in the summon-negation window: pay the cost, then seat it on the chain
        /// (the summoner may chain to it); the window's Summon resumes once the chain resolves. A player with
        /// a choice of Tributes picks one first while the window stays open (Cancel re-offers it).
        /// </summary>
        public static bool ActivateSummonNegation(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPick)
        {
            var clauses = prog?.ClausesFor(EffectTiming.MonsterWouldBeSummoned);
            if (clauses == null || clauses.Count == 0 || fromHand) return false;
            var c = clauses[0];
            if (!CanNegatePendingSummon(engine, who, card, c, out var why))
            {
                engine.Log($"{card?.Name}: {why}");
                return false;
            }

            var summoned = engine.PendingResponse.Summoned;
            if (c.RequiresTributeCount > 0)
            {
                var tributes = TextEffectRuntime.CollectTributeCost(who, card, c);
                // The activating player picks the Tribute. TryActivateSpellTrap passes autoPick for every
                // Opponent-seat activation, so a human Opponent (HvH) is recognised by IsHumanControlled; the
                // Player seat asks only when not auto-picking (AI-driven lab duels pass autoPick).
                var human = engine.IsHumanControlled(who) && (!who.IsPlayer || !autoPick);
                if (human && tributes.Count > 1)
                {
                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        FromHand = false,
                        WasSetOnField = true,
                        TargetKind = EffectTargetKind.TributeMonsterYouControl,
                        UsesTextProgram = true,
                        AwaitingSummonNegationTribute = true,
                        CostPicksRemaining = 1,
                        SeatTiming = ResponseTiming.SummonNegation,
                        SeatSummoned = summoned
                    };
                    pending.LegalTargets.AddRange(tributes);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                TributeForCost(engine, who, card, CheapestTribute(tributes));
            }

            if (c.PayHalfLpCost)
                PayHalfLp(engine, who);
            return TextEffectRuntime.SeatResponseOnChain(engine, who, card, false, ResponseTiming.SummonNegation,
                summoned, clauses, autoPickTarget: true);
        }

        /// <summary>The player picked Horn of Heaven's Tribute: pay it, then seat the card on the chain.</summary>
        public static bool FinishTributeCostPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (p == null || target == null || !p.LegalTargets.Contains(target)) return false;
            var who = p.Controller;
            var card = p.Card;
            var summoned = p.SeatSummoned;
            var prog = CompiledEffectCache.GetOrCompile(card);
            var clauses = prog?.ClausesFor(EffectTiming.MonsterWouldBeSummoned);
            if (clauses == null || clauses.Count == 0) return false;
            if (!CanNegatePendingSummon(engine, who, card, clauses[0], out var why) ||
                engine.PendingResponse.Summoned != summoned)
            {
                engine.Log($"{card?.Name}: {why}");
                return false;
            }

            if (ContinuousProtections.NeitherPlayerCanTribute(engine) || !who.TryFindMonster(target, out _))
                return false;
            engine.ClearPendingActivation();
            TributeForCost(engine, who, card, target);
            if (clauses[0].PayHalfLpCost)
                PayHalfLp(engine, who);
            return TextEffectRuntime.SeatResponseOnChain(engine, who, card, false, ResponseTiming.SummonNegation,
                summoned, clauses, autoPickTarget: true);
        }

        /// <summary>NegateSummon resolves: negate the answered Summon (and destroy that monster).</summary>
        public static void ApplyNegateSummon(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance summoned)
        {
            if (engine == null || clause == null) return;
            if (!engine.NegateSummonInProgress(summoned, source, clause.DestroyNegatedCard))
                engine.Log($"{source?.Name}: the Summon is no longer waiting — no effect.");
        }

        /// <summary>
        /// AI: answer a Summon worth a card (ATK ≥ <see cref="AiSummonNegationMinAtk"/> or Level 5+), with a card whose
        /// cost is cheap enough — half LP only while LP ≥ <see cref="AiHalfLpMinLife"/>, a Tribute only of a monster
        /// weaker than the one being Summoned.
        /// </summary>
        public static CardInstance AiPickSummonNegation(DuelEngine engine, DuelistState who,
            List<CardInstance> legal, CardInstance summoned)
        {
            if (engine == null || who == null || legal == null || summoned == null) return null;
            if (summoned.CurrentAtk < AiSummonNegationMinAtk && summoned.Level < 5) return null;
            foreach (var c in legal)
            {
                if (c?.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(c.Def);
                var clauses = prog?.ClausesFor(EffectTiming.MonsterWouldBeSummoned);
                if (clauses == null || clauses.Count == 0) continue;
                var clause = clauses[0];
                if (clause.PayHalfLpCost && !AiWillPayHalfLp(who)) continue;
                if (clause.RequiresTributeCount > 0)
                {
                    var cheapest = CheapestTribute(TextEffectRuntime.CollectTributeCost(who, c, clause));
                    if (cheapest == null || cheapest.CurrentAtk >= summoned.CurrentAtk) continue;
                }

                return c;
            }

            return null;
        }

        // ── Chain negation filters (Solemn Judgment's Spell/Trap half, Magic Drain) ──

        /// <summary>
        /// "When your opponent activates …" never answers its controller's own link; a "Card is activated" negation
        /// answers the activation of a Spell/Trap Card (from the hand or Set), not an effect of one already face-up.
        /// </summary>
        public static bool ChainNegationAnswers(EffectClause clause, DuelistState who, ChainLink target,
            out string reason)
        {
            reason = "OK";
            if (clause == null || target == null) return true;
            if (clause.NegateOpponentActivationOnly && target.Controller == who)
            {
                reason = "Only answers your opponent's activation.";
                return false;
            }

            if (clause.NegateCardActivationOnly && !target.FromHand && !target.WasSetOnField)
            {
                reason = "Only answers the activation of a card, not the effect of a face-up card.";
                return false;
            }

            return true;
        }

        // ── Magic Drain: the answered player may discard 1 Spell to negate this effect ──

        static List<CardInstance> SpellsInHand(DuelistState who) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(c => c?.Def != null && c.Def.IsSpell).ToList();

        /// <summary>
        /// Magic Drain's negation link resolves. The answered player (the Spell's controller) with a Spell Card in
        /// hand chooses: discard one (Magic Drain's effect is negated) or not (their Spell's activation is negated
        /// and destroyed). The AI always discards its least valuable Spell; a human's choice pauses the chain.
        /// </summary>
        public static WaiverOutcome OfferSpellDiscardWaiver(DuelEngine engine, ChainLink link)
        {
            var answered = link?.TargetLink;
            var chooser = answered?.Controller;
            if (engine == null || answered == null || answered.Negated || chooser == null)
                return WaiverOutcome.Proceed;
            var spells = SpellsInHand(chooser);
            if (spells.Count == 0)
            {
                engine.Log($"{link.Card?.Name}: {chooser.Name} has no Spell Card to discard.");
                return WaiverOutcome.Proceed;
            }

            if (!engine.IsHumanControlled(chooser))
            {
                var pick = AiHeuristicPolicy.PickLowestValue(spells, null);
                if (pick == null) return WaiverOutcome.Proceed;
                DiscardToWaive(engine, chooser, pick, link.Card);
                return WaiverOutcome.Waived;
            }

            var pending = new PendingActivation
            {
                Controller = chooser,
                Card = link.Card,
                FromHand = false,
                TargetKind = EffectTargetKind.DiscardFromHand,
                UsesTextProgram = true,
                AwaitingSpellDiscardWaiver = true
            };
            pending.LegalTargets.AddRange(spells);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return WaiverOutcome.AwaitingChoice;
        }

        static void DiscardToWaive(DuelEngine engine, DuelistState chooser, CardInstance spell, CardInstance drain)
        {
            engine.SendCardToGrave(chooser, spell, sentBy: drain);
            engine.Log($"{chooser.Name} discards {spell.Name} to negate the effect of {drain?.Name ?? "the Counter Trap"}.");
            DiscardEvents.Record(engine, chooser, spell, DiscardCause.Effect, engine.ControllerOf(drain));
        }

        /// <summary>The answered player picked the Spell to discard: Magic Drain's effect is negated; the chain resumes.</summary>
        public static bool FinishSpellDiscardWaiver(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (p == null || target == null || !p.LegalTargets.Contains(target)) return false;
            var chooser = p.Controller;
            if (chooser == null || !chooser.Hand.Contains(target) || target.Def == null || !target.Def.IsSpell)
                return false;
            engine.ClearPendingActivation();
            DiscardToWaive(engine, chooser, target, p.Card);
            engine.ResumeChainAfterSpellDiscardWaiver(waived: true);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>The answered player declined to discard: the negation applies; the chain resumes.</summary>
        public static bool DeclineSpellDiscardWaiver(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingSpellDiscardWaiver) return false;
            engine.ClearPendingActivation();
            engine.Log($"{p.Controller?.Name} does not discard — {p.Card?.Name} negates the Spell.");
            engine.ResumeChainAfterSpellDiscardWaiver(waived: false);
            engine.NotifyPublic();
            return true;
        }
    }
}
