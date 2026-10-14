using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PerCountBurns" (v141) runtime: what an InflictDamageToOpponent clause counts when it carries
    /// <see cref="EffectClause.CountSource"/>, the matching "something to count" activation conditions, and the
    /// face-up field ignition of a Continuous Spell (<see cref="EffectTiming.FaceUpFieldIgnition"/>) with its
    /// Standby Phase turn counter (<see cref="CardInstance.TurnCounter"/>).
    /// Counts are taken when the clause resolves (the ygopro operations recount at resolution), except the
    /// turn counter, which is fixed when the ignition is activated (its cost sends the card to the GY).
    /// The ignition is a Spell Speed 1 Chain Link (EFFECT_TYPE_IGNITION): cost and count at activation, the
    /// opponent may respond, and the fixed damage is dealt when the link resolves.
    /// </summary>
    public static class PerCountBurnsRuntime
    {
        // ── EffectClause.CountSource values for InflictDamageToOpponent ──

        /// <summary>Every card in the opponent's Spell &amp; Trap Zones, Field Zone and Pendulum Zones (Princess of Tsurugi).</summary>
        public const string OppSpellTrapCards = "OppSpellTrapCards";

        /// <summary>Every card in the opponent's banished pile, face-up or face-down (D.D. Dynamite).</summary>
        public const string OppBanishedCards = "OppBanishedCards";

        /// <summary>Every card in the opponent's GY (Cemetary Bomb).</summary>
        public const string OppGyCards = "OppGyCards";

        /// <summary>Cards the previous Destroy clause of this resolution actually destroyed (Thunder Crash).</summary>
        public const string PreviousClauseHits = "PreviousClauseHits";

        /// <summary>
        /// This card's <see cref="CardInstance.TurnCounter"/>: the controller's Standby Phases that passed while
        /// it was face-up since it was activated (Wave-Motion Cannon).
        /// </summary>
        public const string ThisCardTurnCounter = "ThisCardTurnCounter";

        /// <summary>
        /// StandbyBurnsAndDice batch (v162): face-up monster cards in the opponent's banished pile (Graverobber's
        /// Retribution; Lua LOCATION_REMOVED with IsFaceup() and IsMonster(), counted at resolution). A card banished
        /// face-down never counts.
        /// </summary>
        public const string OppBanishedFaceUpMonsters = "OppBanishedFaceUpMonsters";

        // ── EffectClause.ActivationCondition values (ConditionAmount = minimum) ──

        /// <summary>The opponent has at least ConditionAmount banished cards.</summary>
        public const string OppBanishedAtLeast = "OppBanishedAtLeast";

        /// <summary>The opponent has at least ConditionAmount cards in their GY.</summary>
        public const string OppGyAtLeast = "OppGyAtLeast";

        /// <summary>You control at least ConditionAmount monsters (face-down included).</summary>
        public const string YouControlMonstersAtLeast = "YouControlMonstersAtLeast";

        /// <summary>
        /// Big Wave Small Wave's activation gate: you control at least one face-up monster whose live Attribute is
        /// <see cref="EffectClause.AttributeFilter"/> (Lua <c>s.target</c> <c>IsExistingMatchingCard(s.dfilter,…)</c>,
        /// where dfilter is <c>IsFaceup() and IsAttribute(ATTRIBUTE_WATER)</c>).
        /// </summary>
        public const string YouControlFaceUpAttributeMonsterAtLeast =
            "YouControlFaceUpAttributeMonsterAtLeast";

        /// <summary>
        /// The count for a per-count burn, or -1 when <paramref name="clause"/> has no CountSource this batch
        /// owns (the caller then keeps its other scale flags / flat amount). <paramref name="source"/> is the
        /// card whose effect resolves (its own turn counter).
        /// </summary>
        public static int BurnCount(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            int previousClauseHits)
        {
            if (clause == null || string.IsNullOrEmpty(clause.CountSource)) return -1;
            var opp = engine?.OpponentOf(who);
            switch (clause.CountSource)
            {
                case OppSpellTrapCards:
                    return SpellTrapZoneCards(opp);
                case OppBanishedCards:
                    return opp?.Banished?.Count(c => c != null) ?? 0;
                case OppGyCards:
                    return opp?.Graveyard?.Count(c => c != null) ?? 0;
                case PreviousClauseHits:
                    return previousClauseHits < 0 ? 0 : previousClauseHits;
                case ThisCardTurnCounter:
                    return source != null && source.TurnCounter > 0 ? source.TurnCounter : 0;
                case OppBanishedFaceUpMonsters:
                    return TargetedClassicsRuntime.CountFaceUpBanishedMonsters(opp);
                default:
                    // IgnitionCostsAndSimpleSpells batch: Solar Ray's face-up LIGHT monsters you control.
                    return IgnitionCostsAndSimpleSpellsRuntime.BurnCount(engine, who, clause);
            }
        }

        /// <summary>Cards in a player's Spell &amp; Trap Zones, Field Zone and Pendulum Zones (ygopro LOCATION_SZONE).</summary>
        public static int SpellTrapZoneCards(DuelistState who)
        {
            if (who == null) return 0;
            var n = who.SpellTrapsOnField().Count(c => c != null);
            if (who.PendulumZones != null)
                foreach (var z in who.PendulumZones)
                    if (z?.Occupant != null) n++;
            return n;
        }

        /// <summary>
        /// Checks an activation condition this batch owns. Returns false when the clause's ActivationCondition is
        /// not one of them (the caller reports it as unknown).
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause clause,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (clause == null || engine == null || who == null) return false;
            var opp = engine.OpponentOf(who);
            var min = clause.ConditionAmount > 0 ? clause.ConditionAmount : 1;
            switch (clause.ActivationCondition)
            {
                case OppBanishedAtLeast:
                    ok = (opp?.Banished?.Count(c => c != null) ?? 0) >= min;
                    reason = $"Your opponent must have at least {min} banished card(s).";
                    return true;
                case OppGyAtLeast:
                    ok = (opp?.Graveyard?.Count(c => c != null) ?? 0) >= min;
                    reason = $"Your opponent must have at least {min} card(s) in their GY.";
                    return true;
                case YouControlMonstersAtLeast:
                    ok = who.MonsterCount >= min;
                    reason = $"You must control at least {min} monster(s).";
                    return true;
                case YouControlFaceUpAttributeMonsterAtLeast:
                    ok = who.MonstersOnField().Any(m => m != null && m.FaceUp &&
                            string.Equals(m.Attribute, clause.AttributeFilter,
                                System.StringComparison.OrdinalIgnoreCase));
                    reason = $"You must control a face-up {clause.AttributeFilter} monster.";
                    return true;
                default:
                    return false;
            }
        }

        // ── Face-up field ignition (Wave-Motion Cannon) ──

        /// <summary>
        /// <paramref name="card"/> is a face-up Spell/Trap in <paramref name="who"/>'s Spell &amp; Trap Zone whose
        /// complete program has a <see cref="EffectTiming.FaceUpFieldIgnition"/> clause: activating it from there
        /// means that ignition, not the card again.
        /// </summary>
        public static bool IsFaceUpFieldIgnitionCard(DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (who == null || card?.Def == null || card.Def.IsMonster || !card.FaceUp) return false;
            if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.FaceUpFieldIgnition))
                return false;
            return who.TryFindSpellTrap(card, out _);
        }

        /// <summary>
        /// Open game state, your own Main Phase, the card's effects not negated, and a ready clause
        /// (Lua s.tg chk: <c>c:GetTurnCounter()&gt;0</c>). The cost (send this face-up card to the GY) is always
        /// payable while the card is face-up in your Spell &amp; Trap Zone.
        /// </summary>
        public static bool CanActivateFaceUpFieldIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            if (engine == null || !IsFaceUpFieldIgnitionCard(who, card, prog))
            {
                reason = "Not a face-up card with an ignition effect.";
                return false;
            }

            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (engine.PendingResponse != null || engine.PendingActivation != null)
            {
                reason = "Respond or pass the open window first.";
                return false;
            }

            if (engine.HasDeclaredAttack)
            {
                reason = "Finish the declared attack / response first.";
                return false;
            }

            // Spell Speed 1: it can only start a Chain (its own seated Chain Link 1 is allowed).
            if (engine.IsResolvingChain || engine.Chain.IsResolving ||
                (engine.Chain.HasLinks &&
                 !(engine.Chain.Count == 1 && engine.Chain.Last.Card == card && !engine.Chain.Last.Resolved)))
            {
                reason = "A Spell Speed 1 effect can only be Chain Link 1.";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = "Only during your Main Phase.";
                return false;
            }

            if (card.IsNegated)
            {
                reason = "This card's effects are negated.";
                return false;
            }

            // PgdSpellTrapBasics batch (v162): Card Shuffle's paid shuffle — once per turn, 300 LP.
            if (PgdSpellTrapBasicsRuntime.IsShuffleIgnition(prog))
                return PgdSpellTrapBasicsRuntime.CanUseShuffleIgnition(engine, who, card, prog, out reason);
            // ContinuousLocksAndIgnitions batch (v163): Call of the Mummy / Frontline Base — once per turn, a hand monster.
            if (ContinuousLocksAndIgnitionsRuntime.IsHandIgnition(prog))
                return ContinuousLocksAndIgnitionsRuntime.CanUseHandIgnition(engine, who, card, prog, out reason);
            // PhaseTriggersAndIgnitionCosts batch: Mass Driver — Tribute 1 monster (cost), no count limit.
            if (PhaseTriggersAndIgnitionCostsRuntime.IsTributeBurnIgnition(prog))
                return PhaseTriggersAndIgnitionCostsRuntime.CanUseTributeBurn(engine, who, card, prog, out reason);

            if (ReadyIgnition(card, prog) == null)
            {
                reason = "None of your Standby Phases has passed since this card was activated.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>The first FaceUpFieldIgnition clause this card can use now, or null.</summary>
        static EffectClause ReadyIgnition(CardInstance card, CompiledCardProgram prog)
        {
            foreach (var c in prog.ClausesFor(EffectTiming.FaceUpFieldIgnition))
            {
                if (c == null || c.Action != EffectActionKind.InflictDamageToOpponent || !c.RequiresSendThisToGy)
                    continue;
                if (c.CountSource == ThisCardTurnCounter && card.TurnCounter > 0)
                    return c;
            }

            return null;
        }

        /// <summary>
        /// <see cref="ChainLink.EffectKey"/> of a face-up field ignition link: the activation of a card's effect,
        /// not of a card, so "When a Spell/Trap Card is activated" answers (Magic Jammer) cannot negate it.
        /// </summary>
        public const string FaceUpFieldIgnitionLinkKey = "PerCountBurns.FaceUpFieldIgnition";

        /// <summary>The damage a seated ignition link fixed at activation (ygopro SetTargetPlayer / SetTargetParam).</summary>
        sealed class FixedDamage
        {
            public int Count;
            public int Damage;
        }

        static readonly ConditionalWeakTable<ChainLink, FixedDamage> s_fixedDamage =
            new ConditionalWeakTable<ChainLink, FixedDamage>();

        /// <summary>
        /// <paramref name="link"/> activated the effect of a face-up card (<see cref="FaceUpFieldIgnitionLinkKey"/>),
        /// not a card: chain answers that need "a Spell/Trap Card is activated" do not apply to it.
        /// </summary>
        public static bool IsCardEffectLink(ChainLink link) =>
            link != null && link.EffectKey == FaceUpFieldIgnitionLinkKey;

        /// <summary>
        /// Called from <see cref="SpellTrapEffects.BeginOrResolveManual"/> (outside response windows). Returns
        /// false for any card without a <see cref="EffectTiming.FaceUpFieldIgnition"/> clause. For such a card:
        /// the card's own activation (from the hand, or a Set card) resets its turn counter (Lua s.reset) and
        /// returns false so the normal activation goes on. From face-up on the field it is the ignition: it seats
        /// a Spell Speed 1 Chain Link (the one <see cref="DuelEngine.TryActivateSpellTrap"/> seated, or a new one),
        /// fixes the damage from the counter, pays the cost (send this card to the GY), and opens the opponent's
        /// chain window; the damage is dealt when the link resolves (<see cref="TryResolveFaceUpFieldIgnitionLink"/>).
        /// Returns true with <paramref name="handled"/> = whether the effect was activated.
        /// </summary>
        public static bool TryFaceUpFieldIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, out bool handled)
        {
            handled = false;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.FaceUpFieldIgnition))
                return false;

            if (fromHand || !IsFaceUpFieldIgnitionCard(who, card, prog))
            {
                // The card's own activation: "since this card was activated" starts here.
                card.TurnCounter = 0;
                return false;
            }

            if (!CanActivateFaceUpFieldIgnition(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                return true;
            }

            // Chain Link 1, Spell Speed 1 (checked above: the chain is empty or holds only this card's own link).
            var link = engine.Chain.Last;
            if (link == null || link.Card != card)
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed1, EffectClass.Ignition,
                    CardLocation.SpellTrapZone, fromHand: false, wasSet: false,
                    effectKey: FaceUpFieldIgnitionLinkKey);
            if (link == null)
            {
                engine.Log($"{card.Name}: its effect cannot start a Chain now.");
                return true;
            }

            link.EffectKey = FaceUpFieldIgnitionLinkKey;
            // PhaseTriggersAndIgnitionCosts batch: Mass Driver stays on the field; its Tribute is paid first (a human picks;
            // Cancel takes the activation back), then the opponent's chain window opens (the batch runtime does both).
            if (PhaseTriggersAndIgnitionCostsRuntime.IsTributeBurnIgnition(prog))
            {
                PhaseTriggersAndIgnitionCostsRuntime.ActivateTributeBurn(engine, who, card, prog, link);
                handled = true;
                return true;
            }

            if (PgdSpellTrapBasicsRuntime.IsShuffleIgnition(prog))
            {
                // PgdSpellTrapBasics batch (v162): Card Shuffle pays 300 LP and stays on the field; the Deck is chosen as
                // the link resolves (PgdSpellTrapBasicsRuntime.TryResolveShuffleLink).
                PgdSpellTrapBasicsRuntime.ActivateShuffleIgnition(engine, who, card, prog, link);
                handled = true;
                if (engine.GameOver) return true;
            }
            else if (ContinuousLocksAndIgnitionsRuntime.IsHandIgnition(prog))
            {
                // ContinuousLocksAndIgnitions batch (v163): Call of the Mummy / Frontline Base stay on the field; the monster
                // is chosen as the link resolves (ContinuousLocksAndIgnitionsRuntime.TryResolveHandIgnitionLink).
                ContinuousLocksAndIgnitionsRuntime.ActivateHandIgnition(engine, who, card, prog, link);
                handled = true;
            }
            else
            {
                var clause = ReadyIgnition(card, prog);
                // ygopro s.tg: the damage is fixed when the effect is activated (target param), not at resolution.
                var count = card.TurnCounter;
                var damage = System.Math.Max(0, clause.Amount) * count;
                s_fixedDamage.Remove(link);
                s_fixedDamage.Add(link, new FixedDamage { Count = count, Damage = damage });
                engine.Log($"Activate: {card.Name} (effect) — Chain Link {link.LinkNumber}.");
                var owner = engine.ControllerOf(card) ?? who;
                engine.SendCardToGrave(owner, card);
                engine.Log($"Cost: send {card.Name} to the GY.");
                engine.Log($"{card.Name}: {count} of your Standby Phase(s) passed — {damage} damage when it resolves.");
                handled = true;
            }

            // The opponent may respond (Spell Speed 2+); with nothing to chain, the link resolves now.
            if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                if (engine.Chain.HasLinks && !engine.Chain.IsResolving && !engine.IsResolvingChain)
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }
            }
            else
                engine.NotifyPublic();

            return true;
        }

        /// <summary>
        /// Called by the chain driver for every link it resolves. Returns false for any link that is not a
        /// face-up field ignition. For one: deals the damage fixed at activation to the activating player's
        /// opponent (Lua s.op: Duel.Damage with the chain's target param), and returns true. It never falls
        /// back to the card's own activation (the card already left the field as the cost).
        /// </summary>
        public static bool TryResolveFaceUpFieldIgnitionLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsCardEffectLink(link)) return false;
            // PgdSpellTrapBasics batch (v162): Card Shuffle's link.
            if (PgdSpellTrapBasicsRuntime.TryResolveShuffleLink(engine, link)) return true;
            // ContinuousLocksAndIgnitions batch (v163): Call of the Mummy / Frontline Base's link.
            if (ContinuousLocksAndIgnitionsRuntime.TryResolveHandIgnitionLink(engine, link)) return true;
            // PhaseTriggersAndIgnitionCosts batch: Mass Driver's link.
            if (PhaseTriggersAndIgnitionCostsRuntime.TryResolveTributeBurnLink(engine, link)) return true;
            var name = link.Card?.Name ?? "effect";
            if (!s_fixedDamage.TryGetValue(link, out var fixedDamage))
            {
                engine.Log($"{name}: its effect has nothing to resolve.");
                return true;
            }

            s_fixedDamage.Remove(link);
            // Imperial Order / Spell Canceller applying when the link resolves (IO chained to this link): the
            // effect was activated in the Spell & Trap Zone, so it is negated although the card already left as
            // the cost (IO Lua EVENT_CHAIN_SOLVING: CHAININFO_TRIGGERING_LOCATION & LOCATION_SZONE; v141 audit).
            var negatedBy = ContinuousNegationRuntime.NegatedBy(engine, link.Card);
            if (negatedBy != null)
            {
                engine.Log($"{name}: its effect is negated by {negatedBy.Name}.");
                engine.NotifyPublic();
                return true;
            }

            var opp = engine.OpponentOf(link.Controller);
            engine.Log($"{name}: resolves — {fixedDamage.Count} Standby Phase(s) → {fixedDamage.Damage} damage.");
            engine.ApplyEffectDamage(opp, fixedDamage.Damage, name);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Start of <paramref name="turnPlayer"/>'s Standby Phase: +1 on the turn counter of each face-up card they
        /// control whose program counts it (Lua e3, EVENT_PHASE_START+PHASE_STANDBY, only on the controller's
        /// turn, EFFECT_FLAG_CANNOT_DISABLE: it counts even while the card's effects are negated).
        /// </summary>
        public static void TickStandbyTurnCounters(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            foreach (var st in turnPlayer.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || !st.FaceUp) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                if (!prog.ClausesFor(EffectTiming.FaceUpFieldIgnition)
                        .Exists(c => c != null && c.CountSource == ThisCardTurnCounter))
                    continue;
                st.TurnCounter++;
                engine.Log($"{st.Name}: {st.TurnCounter} of your Standby Phase(s) since it was activated.");
            }
        }

        /// <summary>
        /// AI: fire a ready face-up field ignition burn when it is lethal, or once it has counted 3 Standby
        /// Phases (3000 for Wave-Motion Cannon). The caller still legal-checks the activation.
        /// </summary>
        public static bool AiWantsFaceUpFieldIgnition(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsFaceUpFieldIgnitionCard(ai, card, prog)) return false;
            // ContinuousLocksAndIgnitions batch (v163): Call of the Mummy / Frontline Base whenever usable.
            if (ContinuousLocksAndIgnitionsRuntime.IsHandIgnition(prog))
                return ContinuousLocksAndIgnitionsRuntime.AiWantsHandIgnition(engine, ai, card, prog);
            // PhaseTriggersAndIgnitionCosts batch: Mass Driver for lethal damage or a Token's Tribute.
            if (PhaseTriggersAndIgnitionCostsRuntime.IsTributeBurnIgnition(prog))
                return PhaseTriggersAndIgnitionCostsRuntime.AiWantsTributeBurn(engine, ai, card, prog);
            var clause = ReadyIgnition(card, prog);
            if (clause == null) return false;
            var damage = System.Math.Max(0, clause.Amount) * card.TurnCounter;
            var opp = engine.OpponentOf(ai);
            return (opp != null && damage >= opp.LifePoints) || card.TurnCounter >= 3;
        }
    }
}
