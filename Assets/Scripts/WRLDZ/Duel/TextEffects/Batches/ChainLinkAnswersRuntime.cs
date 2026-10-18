using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChainLinkAnswers batch runtime: Chain answers whose legality reads the answered link (<see cref="ChainLinkAnswersAtoms"/>).
    /// <list type="bullet">
    /// <item><b>Legality</b> (<see cref="TryCanAnswer"/>, from TextEffectRuntime.CanActivateChainResponse, ahead of the
    /// generic "card activation only" refusals): each card's own condition on the answered link, the card's place (a Set
    /// card not Set this turn; a Quick-Play Spell also from the hand during its controller's turn, with a free Spell &amp;
    /// Trap Zone), its Spell Speed against the last link, and its costs.</item>
    /// <item><b>Activation</b> (<see cref="TryActivateAnswer"/>, from DuelEngine.TryActivateChainResponse): Spell Shield
    /// Type-8's bullet (the free bullet 1 or the bullet 2 send cost, a human picks when both are legal), then
    /// DuelEngine.SeatChainAnswer pays the LP cost and seats the link. Hand Quick-Plays are collected by
    /// <see cref="CollectHandAnswers"/> (SpellTrapEffects.CollectLegalResponseCards).</item>
    /// <item><b>Resolution</b> (<see cref="TryResolveLink"/>, from DuelEngine.ResolveChainStack after the negated check):
    /// Jar Robber negates the answered activation's EFFECT and draws; the answered activation then resolves with no
    /// effect (its card still counts as a resolved Spell); My Body as a Shield negates a monster effect's activation and
    /// destroys that monster (a Spell/Trap answered goes through the generic negation); Dramatic Rescue returns the
    /// targeted series monster(s) and Special Summons from the hand.</item>
    /// <item><b>Chaos Command Magician</b> (ygopro EVENT_CHAIN_SOLVING): a monster effect whose only target is a face-up
    /// copy is negated as its link starts to resolve (<see cref="NegateResolvingLinkTargetingOnlyThis"/>), or, for a
    /// monster effect that resolves without a Chain Link (no one could respond), as its single-target clause applies
    /// (<see cref="NegatesTargetingMonsterClause"/>, from TextEffectRuntime.ApplyClause).</item>
    /// </list>
    /// </summary>
    public static class ChainLinkAnswersRuntime
    {
        sealed class State
        {
            /// <summary>Links whose effect Jar Robber negated: they resolve with no effect (Duel.NegateEffect).</summary>
            public readonly HashSet<ChainLink> EffectNegated = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── clause shapes ─────────────────────────────

        /// <summary>A ChainLinkActivated clause this runtime answers with.</summary>
        public static bool IsBatchAnswer(EffectClause c) =>
            c != null && c.Timing == EffectTiming.ChainLinkActivated &&
            (c.Action == EffectActionKind.NegateEffectOfNamedActivationThenDraw ||
             c.Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand ||
             // TargetingTruth batch (v177): Big Burn.
             c.Action == EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster ||
             c.NegateRequiresWouldDestroyMonsterOnField ||
             // Spell Shield Type-8: "Activate 1 of these effects" — each bullet is its own activation.
             (c.Action == EffectActionKind.NegateActivation && c.EffectChoiceIndex > 0));

        static bool IsBullets(List<EffectClause> clauses) =>
            clauses.Count > 1 && clauses.TrueForAll(c => c != null && c.EffectChoiceIndex > 0 &&
                                                         c.Action == EffectActionKind.NegateActivation);

        // ───────────────────────────── the answered link ─────────────────────────────

        /// <summary>
        /// The activation of a Spell/Trap Card (ygopro re:IsHasType(EFFECT_TYPE_ACTIVATE)): not the effect of a face-up
        /// Spell/Trap, not a trigger effect (the rule SummonNegationRuntime.ChainNegationAnswers reads).
        /// </summary>
        internal static bool IsCardActivation(ChainLink link)
        {
            var def = link?.Card?.Def;
            if (def == null || def.IsMonster || (!def.IsSpell && !def.IsTrap)) return false;
            if (PerCountBurnsRuntime.IsCardEffectLink(link) || FaceUpQuickEffectsRuntime.IsQuickEffectLink(link) ||
                TriggerChainResponsesRuntime.IsTriggerLink(link))
                return false;
            return link.FromHand || link.WasSetOnField;
        }

        /// <summary>A monster's effect (ygopro re:IsMonsterEffect()): an ignition, a trigger or a Quick Effect.</summary>
        internal static bool IsMonsterEffectLink(ChainLink link) =>
            link?.Card?.Def != null && link.Card.Def.IsMonster;

        /// <summary>The cards the link records (TargetingTruthRuntime.LinkCards).</summary>
        internal static List<CardInstance> TargetsOf(ChainLink link) => TargetingTruthRuntime.LinkCards(link);

        /// <summary>
        /// The link's effect targets (ygopro EFFECT_FLAG_CARD_TARGET): TargetingTruth batch (v177), the one answer
        /// (<see cref="TargetingTruthRuntime.LinkEffectTargets"/>), shared with NegationMonstersRuntime.
        /// </summary>
        static bool EffectTargets(ChainLink link) => TargetingTruthRuntime.LinkEffectTargets(link);

        /// <summary>The clause targets (<see cref="TargetingTruthRuntime.ClauseTargets"/>).</summary>
        internal static bool IsTargetingClause(EffectClause c) => TargetingTruthRuntime.ClauseTargets(c);

        /// <summary>ygopro Duel.IsChainNegatable: the answered card's activation may be negated.</summary>
        static bool ActivationNegatable(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            if (prog == null || !prog.FullyCompiled) return true;
            return !prog.ClauseList.Exists(c => c != null && c.Timing != EffectTiming.ContinuousWhileFaceUp &&
                                                !c.ActivationNegatable);
        }

        /// <summary>ygopro Duel.NegateEffect fails on an effect that cannot be negated.</summary>
        static bool EffectNegatable(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog == null || !prog.ClauseList.Exists(c => c != null && !c.EffectNegatable);
        }

        static bool OnFieldMonster(DuelEngine engine, CardInstance c) =>
            c != null && (engine.Player?.TryFindMonster(c, out _) == true ||
                          engine.Opponent?.TryFindMonster(c, out _) == true);

        // ───────────────────────────── My Body as a Shield: "would destroy a monster on the field" ─────────────────────────────

        /// <summary>
        /// ygopro GetOperationInfo(ev, CATEGORY_DESTROY) holds a monster on the field: resolving <paramref name="link"/>
        /// would destroy at least one monster in a Monster Zone (either side). Fail-closed: only the plain Destroy action of
        /// a FullyCompiled program; a targeted clause reads the targets recorded on the link; when the link could be one
        /// of several effects of its card (a monster's trigger), every one of them must destroy.
        /// </summary>
        public static bool LinkWouldDestroyMonsterOnField(DuelEngine engine, ChainLink link) =>
            WouldDestroy(engine, link).Count > 0;

        static List<CardInstance> WouldDestroy(DuelEngine engine, ChainLink link)
        {
            var none = new List<CardInstance>();
            var card = link?.Card;
            if (engine == null || card?.Def == null || link.Negated) return none;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return none;
            var controller = link.Controller ?? engine.ControllerOf(card);
            if (controller == null) return none;
            var groups = ResolvingEffects(prog, link);
            if (groups.Count == 0) return none;
            var all = new List<CardInstance>();
            foreach (var group in groups)
            {
                var hit = new List<CardInstance>();
                foreach (var c in group)
                    CollectDestroyed(engine, controller, link, c, hit);
                if (hit.Count == 0) return none;
                foreach (var h in hit)
                    if (!all.Contains(h))
                        all.Add(h);
            }

            return all;
        }

        /// <summary>
        /// The clauses the link resolves, one list per effect it may be: a card activation's Activate clauses (plus the
        /// clauses of the window it answered), one list per bullet; a monster's ignition ("manual" link) its Activate
        /// clauses; a monster's trigger link each trigger timing's clauses. Any other monster link: none (fail-closed).
        /// </summary>
        internal static List<List<EffectClause>> ResolvingEffects(CompiledCardProgram prog, ChainLink link)
        {
            var groups = new List<List<EffectClause>>();
            List<EffectClause> pool;
            if (IsCardActivation(link))
            {
                pool = new List<EffectClause>(prog.ClausesFor(EffectTiming.Activate));
                switch (link.SeatedFrom)
                {
                    case ResponseTiming.AttackDeclared:
                        pool.AddRange(prog.ClausesFor(EffectTiming.AttackDeclared));
                        break;
                    case ResponseTiming.MonsterSummoned:
                        pool.AddRange(prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon));
                        break;
                    case ResponseTiming.DamageCalculation:
                        pool.AddRange(prog.ClausesFor(EffectTiming.DamageCalculation));
                        break;
                    case ResponseTiming.YouTakeDamage:
                        pool.AddRange(prog.ClausesFor(EffectTiming.YouTakeLifePointDamage));
                        break;
                }

                var bullets = pool.Where(c => c != null && c.EffectChoiceIndex > 0).Select(c => c.EffectChoiceIndex)
                    .Distinct().ToList();
                var shared = pool.Where(c => c != null && c.EffectChoiceIndex <= 0).ToList();
                if (bullets.Count == 0)
                {
                    if (shared.Count > 0) groups.Add(shared);
                    return groups;
                }

                foreach (var b in bullets)
                {
                    var g = new List<EffectClause>(shared);
                    g.AddRange(pool.Where(c => c != null && c.EffectChoiceIndex == b));
                    groups.Add(g);
                }

                return groups;
            }

            if (!IsMonsterEffectLink(link)) return groups;
            if (link.EffectKey == "manual")
            {
                pool = prog.ClausesFor(EffectTiming.Activate).Where(c => c != null && !c.ActivatesFromHand).ToList();
                if (pool.Count > 0) groups.Add(pool);
                return groups;
            }

            if (!TriggerChainResponsesRuntime.IsTriggerLink(link)) return groups;
            foreach (var byTiming in prog.ClauseList
                         .Where(c => c != null && c.Timing != EffectTiming.Activate &&
                                     c.Timing != EffectTiming.ContinuousWhileFaceUp &&
                                     c.Timing != EffectTiming.ChainLinkActivated &&
                                     c.Timing != EffectTiming.MonsterWouldBeSummoned)
                         .GroupBy(c => c.Timing))
                groups.Add(byTiming.ToList());
            return groups;
        }

        static bool ZoneCanHoldMonster(EffectZoneFilter zone)
        {
            if (zone == EffectZoneFilter.None) return false;
            var n = zone.ToString();
            return (n.IndexOf("Monster", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("AnyCard", StringComparison.Ordinal) >= 0 ||
                    zone == EffectZoneFilter.AllOtherCardsOnField) &&
                   n.IndexOf("Gy", StringComparison.Ordinal) < 0 && n.IndexOf("Hand", StringComparison.Ordinal) < 0 &&
                   n.IndexOf("Deck", StringComparison.Ordinal) < 0;
        }

        static void CollectDestroyed(DuelEngine engine, DuelistState controller, ChainLink link, EffectClause c,
            List<CardInstance> hit)
        {
            if (c == null || c.Action != EffectActionKind.Destroy || !ZoneCanHoldMonster(c.Zone)) return;
            if (c.TributeChosenTarget) return; // a Tribute, not a destruction
            if (IsTargetingClause(c) || c.Timing == EffectTiming.OpponentNormalOrFlipSummon)
            {
                // Targets are chosen as the card activates: only what the link recorded counts.
                foreach (var t in TargetsOf(link))
                    if (t?.Def != null && t.Def.IsMonster && OnFieldMonster(engine, t) && !hit.Contains(t))
                        hit.Add(t);
                return;
            }

            // A destruction with no target (Raigeki, Dark Hole, Mirror Force): what it would destroy now. Filters this
            // read cannot decide (a declared Type, Spell/Trap-only riders) are not counted (fail-closed).
            if (c.RaceFilterIsDeclared || c.FaceUpContinuousSpellsOnly || c.FaceUpContinuousTrapsOnly) return;
            IEnumerable<CardInstance> mass;
            if (c.Zone == EffectZoneFilter.AllOtherCardsOnField)
                mass = engine.Player.MonstersOnField().Concat(engine.Opponent.MonstersOnField())
                    .Where(m => m != null && !ReferenceEquals(m, link.Card));
            else
                mass = TextEffectRuntime.CollectAllMatching(engine, controller, c);
            foreach (var m in mass)
            {
                if (m?.Def == null || !OnFieldMonster(engine, m)) continue;
                if (c.AmountIsLevel && c.Amount > 0 && m.Level != c.Amount) continue;
                if (!hit.Contains(m)) hit.Add(m);
            }
        }

        // ───────────────────────────── Dramatic Rescue ─────────────────────────────

        /// <summary>
        /// ygopro s.confilter: the answered link's targets that are face-up monsters of the clause's series in a Monster
        /// Zone (either side), that can go to the hand, with a Monster Zone free for <paramref name="who"/> once they leave.
        /// </summary>
        static List<CardInstance> RescueTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, ChainLink answered)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || clause == null || answered == null) return list;
            foreach (var t in TargetsOf(answered))
            {
                if (t?.Def == null || !t.Def.IsMonster || !t.FaceUp || t.IsToken) continue;
                var holder = engine.ControllerOf(t);
                if (holder == null || !holder.TryFindMonster(t, out _)) continue;
                if (!TextEffectRuntime.CardMatchesSeries(t.Def, clause.NamedCard)) continue;
                if (!ReferenceEquals(holder, who) && engine.FirstEmpty(who.MonsterZones) < 0) continue;
                if (source != null && ContinuousProtections.IsUnaffectedBy(engine, t, source)) continue;
                list.Add(t);
            }

            return list;
        }

        static List<CardInstance> SummonableFromHand(DuelEngine engine, DuelistState who, CardInstance source,
            ICollection<CardInstance> except, bool requireZone)
        {
            var list = new List<CardInstance>();
            if (who?.Hand == null) return list;
            foreach (var h in who.Hand)
            {
                if (h?.Def == null || !h.Def.IsMonster || ReferenceEquals(h, source)) continue;
                if (except != null && except.Contains(h)) continue;
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, h, source, SummonOrigin.Hand,
                        checkHandToll: true, requireZone: requireZone))
                    continue;
                list.Add(h);
            }

            return list;
        }

        // ───────────────────────────── legality ─────────────────────────────

        static List<CardInstance> SpellsInHand(DuelistState who, CardInstance except) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(c => c?.Def != null && c.Def.IsSpell && !ReferenceEquals(c, except)).ToList();

        /// <summary>
        /// TextEffectRuntime.CanActivateChainResponse: true (handled) when <paramref name="clauses"/> are this batch's;
        /// <paramref name="ok"/> / <paramref name="reason"/> then hold the verdict for <paramref name="card"/> answering
        /// <paramref name="target"/>.
        /// </summary>
        public static bool TryCanAnswer(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> clauses, ChainLink target, out bool ok, out string reason)
        {
            ok = false;
            reason = "OK";
            if (clauses == null || !clauses.Exists(IsBatchAnswer)) return false;
            ok = CanAnswer(engine, who, card, clauses, target, out reason, out _);
            return true;
        }

        /// <summary>The verdict, and the clauses (bullets) that may answer.</summary>
        static bool CanAnswer(DuelEngine engine, DuelistState who, CardInstance card, List<EffectClause> clauses,
            ChainLink target, out string reason, out List<EffectClause> legal)
        {
            legal = new List<EffectClause>();
            reason = "OK";
            if (engine == null || who == null || card?.Def == null || target?.Card?.Def == null)
            {
                reason = "Not a chain-activation response.";
                return false;
            }

            if (target.Negated || target.Resolved)
            {
                reason = "That Chain Link is already negated.";
                return false;
            }

            if (!PlaceAllows(engine, who, card, out reason)) return false;
            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            if (!engine.Chain.CanAddLink(speed, OfficialEffectRegistry.ClassOfActivation(card.Def, false)))
            {
                reason = $"Spell Speed {(int)speed} cannot chain to Chain Link {target.LinkNumber}.";
                return false;
            }

            // LocksRedirectsAndActivity batch: Goblin of Greed — no discard cost (bullets are checked one by one below).
            if (!IsBullets(clauses) && LocksRedirectsAndActivityRuntime.DiscardCostLocked(engine, who, clauses, out reason))
                return false;

            if (IsBullets(clauses))
            {
                // Spell Shield Type-8: two separate activations; each bullet only when its own condition holds.
                if (!IsCardActivation(target))
                {
                    reason = "Only answers the activation of a card.";
                    return false;
                }

                var why = "";
                foreach (var b in clauses.OrderBy(c => c.EffectChoiceIndex))
                {
                    if (!TextEffectRuntime.CanAnswerChainLinkWithClause(engine, who, card, b, target, out var r))
                    {
                        why = r;
                        continue;
                    }

                    if (b.CostSendSpellsFromHandCount > 0 &&
                        SpellsInHand(who, card).Count < b.CostSendSpellsFromHandCount)
                    {
                        why = "No Spell Card in your hand to send to the GY.";
                        continue;
                    }

                    legal.Add(b);
                }

                if (legal.Count == 0)
                {
                    reason = string.IsNullOrEmpty(why) ? "No effect can be activated." : why;
                    return false;
                }

                return true;
            }

            var clause = clauses.First(IsBatchAnswer);
            switch (clause.Action)
            {
                case EffectActionKind.NegateEffectOfNamedActivationThenDraw:
                    if (!IsCardActivation(target) || !target.Card.IsNamed(clause.NamedCard))
                    {
                        reason = $"Only when \"{clause.NamedCard}\" is activated.";
                        return false;
                    }

                    if (!ActivationNegatable(target.Card))
                    {
                        reason = "That card's activation cannot be negated.";
                        return false;
                    }

                    // ygopro Duel.IsPlayerCanDraw(tp, 1).
                    if (who.DeckCount < Math.Max(1, clause.Amount) ||
                        ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, who))
                    {
                        reason = "You cannot draw.";
                        return false;
                    }

                    break;

                case EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand:
                    if (!IsCardActivation(target) && !IsMonsterEffectLink(target))
                    {
                        reason = "Only answers a card activation or a monster effect.";
                        return false;
                    }

                    if (!EffectTargets(target) || RescueTargets(engine, who, card, clause, target).Count == 0)
                    {
                        reason = $"It does not target a face-up \"{clause.NamedCard}\" monster.";
                        return false;
                    }

                    if (SummonableFromHand(engine, who, card, null, requireZone: false).Count == 0)
                    {
                        reason = "No monster in your hand can be Special Summoned.";
                        return false;
                    }

                    break;

                case EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster:
                    // TargetingTruth batch (v177), Big Burn: an effect of the opponent whose targets include a monster in a
                    // GY (TargetingTruthRuntime: an old-text "select" whose Lua targets counts, a Lua Select does not),
                    // and at least one monster it can banish.
                    if (!TargetingTruthRuntime.OpponentTargetsGyMonster(engine, who, target))
                    {
                        reason = "Only when your opponent's effect targets a monster in a GY.";
                        return false;
                    }

                    if (TargetingTruthRuntime.AllGyMonstersToBanish(engine, who).Count == 0)
                    {
                        reason = "No monster in either GY can be banished.";
                        return false;
                    }

                    break;

                default:
                    if (!clause.NegateRequiresWouldDestroyMonsterOnField)
                    {
                        reason = "Not a chain-activation response.";
                        return false;
                    }

                    if (target.Controller == who)
                    {
                        reason = "Only answers your opponent's activation.";
                        return false;
                    }

                    if (!IsCardActivation(target) && !IsMonsterEffectLink(target))
                    {
                        reason = "Only answers a Spell/Trap Card activation or a monster effect.";
                        return false;
                    }

                    if (!ActivationNegatable(target.Card))
                    {
                        reason = "That activation cannot be negated.";
                        return false;
                    }

                    if (!LinkWouldDestroyMonsterOnField(engine, target))
                    {
                        reason = "It would not destroy a monster on the field.";
                        return false;
                    }

                    if (clause.PayLpAmount > 0 && who.LifePoints < clause.PayLpAmount &&
                        !ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
                    {
                        reason = $"Not enough LP to pay {clause.PayLpAmount}.";
                        return false;
                    }

                    break;
            }

            legal.Add(clause);
            return true;
        }

        /// <summary>
        /// The answering card's place: a Set card, not Set this turn; a Quick-Play Spell may also come from the hand during
        /// its controller's own turn, into a free Spell &amp; Trap Zone.
        /// </summary>
        static bool PlaceAllows(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = "OK";
            if (who.Hand.Contains(card))
            {
                if (!SpellTrapEffects.IsQuickPlay(card.Def))
                {
                    reason = "Must be Set on the field first.";
                    return false;
                }

                if (engine.TurnPlayer != who)
                {
                    reason = "Quick-Play Spells activate from the hand only during your turn.";
                    return false;
                }

                if (engine.FirstEmptySpellTrap(who) < 0)
                {
                    reason = "No free Spell/Trap Zone.";
                    return false;
                }

                return true;
            }

            if (!who.TryFindSpellTrap(card, out _))
            {
                reason = "Must be Set on the field.";
                return false;
            }

            if (card.FaceUp)
            {
                reason = "Already face-up.";
                return false;
            }

            if (card.SetThisTurn)
            {
                reason = "Cannot activate the turn it was Set.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CollectLegalResponseCards (Chain window): Quick-Play Spells in the hand whose program answers the
        /// last link through this batch (Jar Robber, My Body as a Shield), during their controller's turn.
        /// </summary>
        public static void CollectHandAnswers(DuelEngine engine, DuelistState who, List<CardInstance> into)
        {
            if (engine == null || who?.Hand == null || into == null || engine.TurnPlayer != who) return;
            foreach (var h in who.Hand.ToList())
            {
                if (h?.Def == null || into.Contains(h) || !SpellTrapEffects.IsQuickPlay(h.Def)) continue;
                if (!OfficialEffectRegistry.ProgramMayActivate(h.Def)) continue;
                var prog = CompiledEffectCache.GetOrCompile(h.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                if (!prog.ClausesFor(EffectTiming.ChainLinkActivated).Exists(IsBatchAnswer)) continue;
                if (TextEffectRuntime.CanActivate(engine, who, h, fromHand: true, prog, out _))
                    into.Add(h);
            }
        }

        // ───────────────────────────── activation ─────────────────────────────

        /// <summary>
        /// DuelEngine.TryActivateChainResponse: true (handled) when <paramref name="clauses"/> are this batch's;
        /// <paramref name="seated"/> = the card was activated (or a human's bullet / cost pick is now open).
        /// </summary>
        public static bool TryActivateAnswer(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            bool autoPick, List<EffectClause> clauses, out bool seated)
        {
            seated = false;
            if (engine == null || clauses == null || !clauses.Exists(IsBatchAnswer)) return false;
            var answering = engine.PendingResponse;
            var target = answering?.ChainTarget;
            if (fromHand != who.Hand.Contains(card))
            {
                engine.Log($"Activation illegal: {card?.Name} is not {(fromHand ? "in your hand" : "Set on the field")}.");
                return true;
            }

            if (!CanAnswer(engine, who, card, clauses, target, out var reason, out var legal))
            {
                engine.Log($"Activation illegal: {reason}");
                return true;
            }

            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, false);
            var skipSpellPayLp = ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card);
            var free = legal.FirstOrDefault(c => c.CostSendSpellsFromHandCount <= 0);
            var paid = legal.FirstOrDefault(c => c.CostSendSpellsFromHandCount > 0);
            if (paid == null)
            {
                seated = engine.SeatChainAnswer(who, card, free ?? legal[0], speed, cls, skipSpellPayLp, null, false);
                return true;
            }

            var human = !autoPick && engine.IsHumanControlled(who);
            if (!human)
            {
                // The AI keeps its Spells: the free bullet whenever it is legal.
                if (free != null)
                {
                    seated = engine.SeatChainAnswer(who, card, free, speed, cls, skipSpellPayLp, null, false);
                    return true;
                }

                var fodder = AiHeuristicPolicy.PickLowestValue(SpellsInHand(who, card), card) ??
                             SpellsInHand(who, card).FirstOrDefault();
                seated = fodder != null && SendSpellAsCostThenSeat(engine, who, card, paid, fodder, speed, cls,
                    skipSpellPayLp);
                return true;
            }

            // A human. With both bullets legal: tap this card for bullet 1 (no cost), or Cancel for bullet 2. Bullet 2: pick
            // the Spell to send (Cancel: not activated, nothing is paid).
            var result = true;
            bool StillAnswering() =>
                ReferenceEquals(engine.PendingResponse, answering) && who.TryFindSpellTrap(card, out _) && !card.FaceUp &&
                engine.Chain.CanAddLink(speed, cls);

            void AskSpell()
            {
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
                {
                    Candidates = () => SpellsInHand(who, card),
                    Kind = EffectTargetKind.DiscardFromHand,
                    Prompt = $"send 1 Spell Card from your hand to the GY to activate {card.Name} (cost), or Cancel to " +
                             "not activate it.",
                    AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, card) ?? list.FirstOrDefault(),
                    DuringActivation = true,
                    Finish = chosen =>
                    {
                        result = chosen != null && StillAnswering() &&
                                 SendSpellAsCostThenSeat(engine, who, card, paid, chosen, speed, cls, skipSpellPayLp);
                        if (!result)
                        {
                            engine.Log($"{card.Name} is not activated.");
                            engine.NotifyPublic();
                        }
                    },
                    Cancel = () =>
                    {
                        result = false;
                        engine.Log($"{card.Name} is not activated — nothing is sent.");
                    }
                });
            }

            if (free == null)
            {
                AskSpell();
                seated = result;
                return true;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => new List<CardInstance> { card },
                Kind = EffectTargetKind.SpellTrapOnField,
                Prompt = $"{card.Name}: tap it to negate that Spell for no cost (effect 1), or Cancel to send 1 Spell " +
                         "Card from your hand to the GY instead (effect 2).",
                AiChoose = _ => card,
                AskWithOneCandidate = true,
                DuringActivation = true,
                Finish = chosen =>
                {
                    if (chosen == null)
                    {
                        AskSpell();
                        return;
                    }

                    result = StillAnswering() &&
                             engine.SeatChainAnswer(who, card, free, speed, cls, skipSpellPayLp, null, false);
                    if (!result)
                    {
                        engine.Log($"{card.Name} is not activated.");
                        engine.NotifyPublic();
                    }
                },
                Cancel = AskSpell
            });
            seated = result;
            return true;
        }

        static bool SendSpellAsCostThenSeat(DuelEngine engine, DuelistState who, CardInstance card, EffectClause bullet,
            CardInstance spell, SpellSpeed speed, EffectClass cls, bool skipSpellPayLp)
        {
            if (spell == null || !who.Hand.Contains(spell) || spell.Def == null || !spell.Def.IsSpell) return false;
            // A send, not a discard (no discard event): Duel.DiscardHand(..., REASON_COST) on a Spell filter.
            engine.SendCardToGrave(who, spell, sentBy: card);
            engine.Log($"Cost: send {spell.Name} from the hand to the GY.");
            return engine.SeatChainAnswer(who, card, bullet, speed, cls, skipSpellPayLp, null, false);
        }

        // ───────────────────────────── resolution ─────────────────────────────

        static EffectClause AnswerClauseOf(ChainLink link)
        {
            if (link?.Card?.Def == null || link.TargetLink == null || link.EffectKey != "chain-response") return null;
            var prog = CompiledEffectCache.GetOrCompile(link.Card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ChainLinkActivated).FirstOrDefault(IsBatchAnswer);
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack (after the negated check): true when this batch resolved <paramref name="link"/>
        /// (the engine then sends a one-shot card to the GY and honours a pick that holds the Chain).
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null) return false;
            // The activation whose effect Jar Robber negated: it resolves with no effect (still a resolved Spell).
            if (s_state.TryGetValue(engine, out var st) && st.EffectNegated.Remove(link))
            {
                link.DeferredEffect = null;
                engine.Log($"{link.Card?.Name ?? "Effect"}: its effect is negated — it resolves with no effect.");
                if (link.Card?.Def != null && link.Card.Def.IsSpell)
                    TextEffectRuntime.NotifySpellResolved(engine, link.Controller, link.Card);
                return true;
            }

            var clause = AnswerClauseOf(link);
            if (clause == null) return false;
            var who = link.Controller ?? engine.ControllerOf(link.Card);
            var answered = link.TargetLink;
            if (who == null) return false;

            if (clause.NegateRespondsToMonsterEffect)
            {
                // Divine Wrath: the answered link is a monster effect. It is not a card activation, so the generic
                // ApplyActivationNegation never runs for it — this is the whole resolution.
                if (!IsMonsterEffectLink(answered))
                {
                    NotifyResolvedSpell(engine, who, link);
                    return false;
                }

                if (ContinuousNegationRuntime.ChainLinkEffectNegated(engine, link)) return true;
                NegateMonsterActivationAndDestroy(engine, link, who, answered);
                NotifyResolvedSpell(engine, who, link);
                return true;
            }

            if (clause.NegateRequiresWouldDestroyMonsterOnField)
            {
                // A Spell/Trap Card's activation: the generic negation (DuelEngine.ApplyActivationNegation) applies it; this
                // Quick-Play Spell still resolves (Spell Counters).
                if (!IsMonsterEffectLink(answered))
                {
                    NotifyResolvedSpell(engine, who, link);
                    return false;
                }

                if (ContinuousNegationRuntime.ChainLinkEffectNegated(engine, link)) return true;
                NegateMonsterActivationAndDestroy(engine, link, who, answered);
                NotifyResolvedSpell(engine, who, link);
                return true;
            }

            if (ContinuousNegationRuntime.ChainLinkEffectNegated(engine, link)) return true;
            switch (clause.Action)
            {
                case EffectActionKind.NegateEffectOfNamedActivationThenDraw:
                    ResolveJarRobber(engine, link, who, answered, clause);
                    NotifyResolvedSpell(engine, who, link);
                    return true;
                case EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand:
                    ResolveDramaticRescue(engine, link, who, answered, clause);
                    return true;
                case EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster:
                    TargetingTruthRuntime.BanishAllGyMonsters(engine, who, link.Card);
                    return true;
            }

            return false;
        }

        static void NotifyResolvedSpell(DuelEngine engine, DuelistState who, ChainLink link)
        {
            if (link.Card?.Def != null && link.Card.Def.IsSpell)
                TextEffectRuntime.NotifySpellResolved(engine, who, link.Card);
        }

        static void ResolveJarRobber(DuelEngine engine, ChainLink link, DuelistState who, ChainLink answered,
            EffectClause clause)
        {
            // Lua: if Duel.NegateEffect(ev) then Duel.Draw(tp, 1).
            if (answered == null || answered.Negated || answered.Resolved || !EffectNegatable(answered.Card) ||
                !engine.Chain.Links.Contains(answered))
            {
                engine.Log($"{link.Card.Name}: the effect of {answered?.Card?.Name ?? "that card"} cannot be negated — no draw.");
                return;
            }

            StateOf(engine).EffectNegated.Add(answered);
            if (answered.Card != null)
                Presentation.ArInteraction.ActivationBeat.NoteNegated(answered.Card.InstanceId);
            engine.Log($"{link.Card.Name}: the effect of {answered.Card?.Name} is negated.");
            engine.Draw(who, Math.Max(1, clause.Amount));
        }

        static void NegateMonsterActivationAndDestroy(DuelEngine engine, ChainLink link, DuelistState who,
            ChainLink answered)
        {
            if (answered == null || answered.Negated) return;
            answered.Negated = true;
            var monster = answered.Card;
            if (monster != null)
                Presentation.ArInteraction.ActivationBeat.NoteNegated(monster.InstanceId);
            engine.Log($"{link.Card.Name}: the activation of {monster?.Name ?? "that effect"} is negated.");
            // "and if you do, destroy it": only while that monster is still where it activated (re:GetHandler()
            // IsRelateToEffect) — on the field.
            if (link.DestroyNegatedCard && monster != null && OnFieldMonster(engine, monster))
                TextEffectRuntime.DestroyCard(engine, monster, link.Card, false, who);
        }

        static void ResolveDramaticRescue(DuelEngine engine, ChainLink link, DuelistState who, ChainLink answered,
            EffectClause clause)
        {
            var source = link.Card;
            // Lua: eg:Filter(s.confilter):Match(Card.IsRelateToEffect, nil, re) — still targeted by that effect.
            var rescue = RescueTargets(engine, who, source, clause, answered);
            if (rescue.Count == 0)
            {
                engine.Log($"{source.Name}: no targeted \"{clause.NamedCard}\" monster is still on the field.");
                return;
            }

            var returned = new List<CardInstance>();
            foreach (var t in rescue)
            {
                var ownerBefore = engine.ControllerOf(t);
                if (!engine.ReturnCardToHand(t, by: who)) continue;
                if (engine.Player.Hand.Contains(t) || engine.Opponent.Hand.Contains(t))
                    returned.Add(t);
                else if (ownerBefore != null && engine.ControllerOf(t) == null)
                    engine.Log($"{t.Name} left the field but did not reach a hand.");
            }

            if (returned.Count == 0) return;
            List<CardInstance> Pool() => SummonableFromHand(engine, who, source, returned, requireZone: true);
            if (Pool().Count == 0)
            {
                engine.Log($"{source.Name}: no other monster in the hand can be Special Summoned.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Pool,
                Kind = EffectTargetKind.MonsterInYourHand,
                Prompt = $"{source.Name}: choose 1 monster in your hand to Special Summon.",
                AiChoose = list => list.OrderByDescending(m => Math.Max(m.CurrentAtk, m.CurrentDef)).FirstOrDefault(),
                // Mandatory: Cancel makes the automatic choice.
                Finish = chosen => SummonChosen(engine, who, source, chosen)
            });
        }

        static void SummonChosen(DuelEngine engine, DuelistState who, CardInstance source, CardInstance chosen)
        {
            if (chosen == null || !who.Hand.Contains(chosen) ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, chosen, source, SummonOrigin.Hand,
                    checkHandToll: true, requireZone: true))
            {
                engine.Log($"{source?.Name}: nothing is Special Summoned.");
                return;
            }

            void Summon(BattlePosition pos)
            {
                if (!who.Hand.Contains(chosen)) return;
                who.Hand.Remove(chosen);
                engine.MarkSpecialSummonFromHand(chosen); // Chain Energy toll (RuleLocks batch)
                if (!engine.SpecialSummonToField(who, chosen, pos, true))
                {
                    who.Hand.Add(chosen);
                    engine.Log($"{source?.Name}: {chosen.Name} could not be Special Summoned.");
                    return;
                }

                engine.Log($"{source?.Name}: Special Summoned {chosen.Name} from the hand " +
                           $"in {(pos == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                engine.NotifyPublic();
            }

            // POS_FACEUP: face-up Attack or Defense Position, the summoner's choice. A human taps the monster for Defense
            // (Cancel: Attack); the AI takes Defense when its DEF is higher.
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Hand.Contains(chosen) ? new List<CardInstance> { chosen } : new List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInYourHand,
                Prompt = $"{source?.Name}: tap {chosen.Name} to Special Summon it in Defense Position, or Cancel for " +
                         "Attack Position.",
                AiChoose = list => chosen.CurrentDef > chosen.CurrentAtk ? chosen : null,
                AskWithOneCandidate = true,
                Finish = pick => Summon(pick != null ? BattlePosition.Defense : BattlePosition.Attack),
                Cancel = () => Summon(BattlePosition.Attack)
            });
        }

        // ───────────────────────────── Chaos Command Magician ─────────────────────────────

        static bool IsActiveProtector(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            var holder = engine.ControllerOf(card);
            if (holder == null || !holder.TryFindMonster(card, out _)) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            // FullyCompiled only: a Stub's fragment never applies.
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                       c != null && c.Action == EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis &&
                       c.AffectsThisCardOnly);
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack, as <paramref name="link"/> starts to resolve (ygopro EVENT_CHAIN_SOLVING): a monster
        /// effect whose only card target is a face-up Chaos Command Magician is negated (it does nothing; nothing is
        /// destroyed). Either player's effect.
        /// </summary>
        public static void NegateResolvingLinkTargetingOnlyThis(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || link.Negated || !IsMonsterEffectLink(link)) return;
            var targets = TargetsOf(link);
            if (targets.Count != 1 || !EffectTargets(link)) return;
            var protector = targets[0];
            if (ReferenceEquals(protector, link.Card) || !IsActiveProtector(engine, protector)) return;
            if (!EffectNegatable(link.Card)) return;
            link.Negated = true;
            Presentation.ArInteraction.ActivationBeat.NoteNegated(link.Card.InstanceId);
            engine.Log($"{protector.Name}: the effect of {link.Card.Name} that targets it is negated.");
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: a monster effect applying its single-target clause to a face-up Chaos Command
        /// Magician without a Chain Link (it resolved at once: no one could respond) — the same negation as
        /// <see cref="NegateResolvingLinkTargetingOnlyThis"/>. Only a clause that takes exactly 1 target counts.
        /// </summary>
        public static bool NegatesTargetingMonsterClause(DuelEngine engine, CardInstance source, EffectClause clause,
            CardInstance chosenTarget)
        {
            if (engine == null || source?.Def == null || !source.Def.IsMonster || clause == null || chosenTarget == null)
                return false;
            if (ReferenceEquals(source, chosenTarget) || !IsTargetingClause(clause) || !clause.EffectNegatable) return false;
            if (clause.Timing == EffectTiming.ContinuousWhileFaceUp || clause.RequiresSecondTarget ||
                clause.DistinctTargetGroup || clause.ControllerTargetCount > 1 || clause.OpponentTargetCount > 0 ||
                TargetedClassicsRuntime.ExactTargetCount(clause) > 1 ||
                TextEffectRuntime.TargetPicks(clause, out _) != 1)
                return false;
            return NegatesRegisteredMonsterTarget(engine, source, chosenTarget);
        }

        /// <summary>
        /// A monster effect run by a registered script (MonsterEffects: Man-Eater Bug's FLIP, Abyss Soldier's bounce)
        /// about to apply to its only target <paramref name="target"/>: true (logged) when a face-up Chaos Command Magician
        /// is that target — the effect is negated.
        /// </summary>
        public static bool NegatesRegisteredMonsterTarget(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (engine == null || source?.Def == null || !source.Def.IsMonster || target == null ||
                ReferenceEquals(source, target))
                return false;
            if (!IsActiveProtector(engine, target) || !EffectNegatable(source)) return false;
            engine.Log($"{target.Name}: the effect of {source.Name} that targets it is negated.");
            return true;
        }

        // ───────────────────────────── AI ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond: the AI answers with this batch's cards only when it pays off — My Body as a
        /// Shield saves one of its own monsters, Dramatic Rescue saves its own targeted monster, Spell Shield Type-8's
        /// bullet 2 spends a Spell only against an activation worth a card. Other answers: always (Big Burn, TargetingTruth
        /// v177: its only use is to deny the opponent's GY target, so the AI answers whenever it is legal).
        /// </summary>
        public static bool AiWorthAnswer(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            ChainLink answered)
        {
            if (!IsBatchAnswer(clause)) return true;
            if (clause.NegateRequiresWouldDestroyMonsterOnField)
                return WouldDestroy(engine, answered).Exists(m => ReferenceEquals(engine.ControllerOf(m), who));
            if (clause.Action == EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand)
                return RescueTargets(engine, who, card, clause, answered)
                    .Exists(m => ReferenceEquals(engine.ControllerOf(m), who));
            if (clause.EffectChoiceIndex > 0)
            {
                var prog = CompiledEffectCache.GetOrCompile(card);
                var clauses = prog?.ClausesFor(EffectTiming.ChainLinkActivated) ?? new List<EffectClause>();
                if (!CanAnswer(engine, who, card, clauses, answered, out _, out var legal)) return false;
                return legal.Exists(c => c.CostSendSpellsFromHandCount <= 0) ||
                       SummonNegationRuntime.AiWorthHalfLpNegation(answered);
            }

            return true;
        }
    }
}
