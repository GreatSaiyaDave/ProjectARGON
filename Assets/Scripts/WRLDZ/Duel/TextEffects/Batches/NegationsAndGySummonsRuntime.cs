using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NegationsAndGySummons" (v159): Quick negations and Graveyard Special Summon events.
    /// <list type="bullet">
    /// <item>Maryokutai (c71466592): a monster Quick Effect that answers the opponent's Spell Card activation during
    /// the opponent's turn; cost Tribute this card (<see cref="CanTributeThisForChainAnswer"/>,
    /// <see cref="TributeThisForChainAnswer"/>, gated by <see cref="ChainAnswerAllowed"/>).</item>
    /// <item>Riryoku Field (c70344351): a Counter Trap that answers a Spell Card activation (either player's) that
    /// targets exactly 1 monster on the field and no other cards (<see cref="TargetsExactlyOneMonster"/>).</item>
    /// <item>Gilasaurus (c45894482): "You can Special Summon this card (from your hand)." — an unconditional hand
    /// procedure (<see cref="CanSummonByOwnHandProcedure"/> / <see cref="TrySummonByOwnHandProcedure"/>), and when
    /// Summoned that way the opponent may Special Summon 1 monster from their GY
    /// (FlipMassEffectsRuntime.ResolveOpponentMaySummonOneFromOwnGy).</item>
    /// <item>Card of Safe Return (c57953380): draw 1 when a monster is Special Summoned from its controller's GY
    /// (<see cref="OnSpecialSummoned"/>, called by DuelEngine.CompleteSpecialSummon).</item>
    /// <item>Chosen One (c21888494): two hand picks, then a random pick among the 3 cards
    /// (<see cref="ResolveChosenOne"/>).</item>
    /// </list>
    /// </summary>
    public static class NegationsAndGySummonsRuntime
    {
        // ───────────────────── Maryokutai / Riryoku Field: chain answers ─────────────────────

        /// <summary>
        /// The extra conditions of this batch's chain answers, checked by TextEffectRuntime.CanActivateChainResponse
        /// after the shared Spell/Trap kind checks: "During your opponent's turn" (<see cref="EffectClause.OpponentTurnOnly"/>),
        /// the Tribute-this cost (<see cref="EffectClause.RequiresTributeThis"/>), and Riryoku Field's single monster
        /// target (<see cref="EffectClause.NegateRequiresSingleMonsterTarget"/>).
        /// </summary>
        public static bool ChainAnswerAllowed(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause clause, ChainLink target, out string reason)
        {
            reason = "OK";
            if (engine == null || clause == null) return true;
            if (clause.OpponentTurnOnly && engine.TurnPlayer == who)
            {
                reason = "Only during your opponent's turn.";
                return false;
            }

            if (clause.NegateRequiresSingleMonsterTarget && !TargetsExactlyOneMonster(engine, target))
            {
                reason = "Only when a Spell Card that targets exactly 1 monster on the field is activated.";
                return false;
            }

            if (clause.RequiresTributeThis && !CanTributeThisForChainAnswer(engine, who, card, out reason))
                return false;
            return true;
        }

        /// <summary>
        /// "You can Tribute this card" as the cost of a monster's chain answer (Maryokutai, ygopro Cost.SelfTribute):
        /// the monster is face-up on <paramref name="who"/>'s field and can be Tributed (not under Mask of Restrict,
        /// not "cannot be Tributed").
        /// </summary>
        public static bool CanTributeThisForChainAnswer(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = "OK";
            if (engine == null || who == null || card == null || !who.TryFindMonster(card, out _))
            {
                reason = "This card must be on your field to Tribute it.";
                return false;
            }

            if (card.CannotBeTributed || ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                reason = "This card cannot be Tributed.";
                return false;
            }

            return true;
        }

        /// <summary>Pay the Tribute-this cost (DuelEngine.TryActivateChainResponse, before the Chain Link is added).</summary>
        public static void TributeThisForChainAnswer(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null) return;
            engine.Log($"Cost: Tribute {card.Name}.");
            ReleaseEvents.Record(engine, card, who); // EngineHygiene: EVENT_RELEASE
            engine.SendCardToGrave(who, card);
        }

        /// <summary>
        /// Riryoku Field (Lua s.condition): <paramref name="link"/> is the activation of a Spell Card (not an effect of
        /// one already face-up) whose effect targets (PSCT "target", ygopro EFFECT_FLAG_CARD_TARGET), and the only card
        /// it targets is 1 monster in a Monster Zone (either field, face-up or face-down). An Equip Spell's activation
        /// targets the monster it equips to.
        /// </summary>
        public static bool TargetsExactlyOneMonster(DuelEngine engine, ChainLink link)
        {
            var def = link?.Card?.Def;
            if (engine == null || def == null || !def.IsSpell || def.IsTrap || link.Resolved) return false;
            if (!link.FromHand && !link.WasSetOnField) return false;
            var target = link.Target ?? (link.Targets.Count == 1 ? link.Targets[0] : null);
            if (target == null) return false;
            if (link.Targets.Count > 1 || (link.Targets.Count == 1 && !ReferenceEquals(link.Targets[0], target)))
                return false;
            if (engine.Player?.TryFindMonster(target, out _) != true &&
                engine.Opponent?.TryFindMonster(target, out _) != true)
                return false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return false;
            var activate = prog.ClausesFor(EffectTiming.Activate);
            if (activate == null || activate.Count == 0) return false;
            var targeting = 0;
            foreach (var c in activate)
            {
                if (c == null) continue;
                // A second target or a group of targets is more than 1 card.
                if (c.RequiresSecondTarget || c.ControllerTargetCount > 1 || c.OpponentTargetCount > 0) return false;
                if (!c.RequiresTargetChoice) continue;
                if (c.TargetCount > 1 || c.TargetUpTo || c.DistinctTargetGroup) return false;
                // A choice made at resolution (Fissure) or one that does not target is no target.
                if (c.ChoiceDoesNotTarget) return false;
                if (c.IsPsctTarget || IsEquipSpell(def)) targeting++;
                else return false;
            }

            return targeting > 0;
        }

        static bool IsEquipSpell(CardDef def) =>
            def?.race != null && def.race.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// AI: a Tribute-this negation (Maryokutai) is worth its monster only against a Spell that does more than an
        /// LP gain or a small burn (the rule Solemn Judgment's half-LP cost uses).
        /// </summary>
        public static bool AiWorthTributeNegation(EffectClause clause, ChainLink answered) =>
            clause == null || !clause.RequiresTributeThis || SummonNegationRuntime.AiWorthHalfLpNegation(answered);

        // ───────────────────── Gilasaurus: "You can Special Summon this card (from your hand)." ─────────────────────

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        /// <summary>The card's own unconditional hand Special Summon procedure (Gilasaurus), else false.</summary>
        public static bool HasOwnHandProcedure(CardInstance card) =>
            ClauseOf(card, EffectActionKind.SpecialSummonThisFromHandByOwnProcedure) != null;

        /// <summary>
        /// Lua EFFECT_SPSUMMON_PROC (s.spcon: a free Monster Zone): the card is in <paramref name="who"/>'s hand, it is
        /// their Main Phase with no Chain, response window, pick or Summon in progress, and a Monster Zone is free.
        /// </summary>
        public static bool CanSummonByOwnHandProcedure(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = "No printed Special Summon procedure.";
            if (engine == null || who == null || !HasOwnHandProcedure(card)) return false;
            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (who.Hand == null || !who.Hand.Contains(card))
            {
                reason = "Not in hand.";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = "Only during your Main Phase.";
                return false;
            }

            if (engine.Chain.HasLinks || engine.Chain.IsResolving || engine.IsResolvingChain ||
                engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget || engine.SummonAwaitingSuccess != null)
            {
                reason = "Not while a Chain, a response or a choice is open.";
                return false;
            }

            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                reason = "No free Monster Zone.";
                return false;
            }

            // GuardiansAndEquipEvents batch: Guardian Grarl — "If this is the only card in your hand", and its summon gate.
            if (!GuardiansAndEquipEventsRuntime.OwnHandProcedureAllows(who, card,
                    ClauseOf(card, EffectActionKind.SpecialSummonThisFromHandByOwnProcedure), out reason))
                return false;

            reason = "OK";
            return true;
        }

        /// <summary>
        /// Perform the procedure: Special Summon the card from the hand in face-up Attack Position (or the UI's pending
        /// placement) with a summon-negation window (DuelEngine.SpecialSummonByOwnHandProcedure). It does not use the
        /// Normal Summon. The card remembers it was Summoned this way (<see cref="CardInstance.SpecialSummonedByOwnProcedure"/>).
        /// </summary>
        public static bool TrySummonByOwnHandProcedure(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (!CanSummonByOwnHandProcedure(engine, who, card, out var reason))
            {
                engine?.Log($"Cannot Special Summon {card?.Name}: {reason}");
                return false;
            }

            engine.Log($"{who.Name} Special Summons {card.Name} from the hand by its own procedure.");
            return engine.SpecialSummonByOwnHandProcedure(who, card, null);
        }

        /// <summary>
        /// AI: the procedure gives the opponent a free Special Summon from their GY, so the AI uses it only when the
        /// opponent cannot take one (no monster in their GY, or no free Monster Zone).
        /// </summary>
        public static bool AiWantsOwnHandProcedure(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (!CanSummonByOwnHandProcedure(engine, ai, card, out _)) return false;
            // GuardiansAndEquipEvents batch: Guardian Grarl's procedure gives the opponent nothing.
            if (ClauseOf(card, EffectActionKind.SpecialSummonThisFromHandByOwnProcedure)?.RequiresOnlyCardInHand == true)
                return true;
            var opp = engine.OpponentOf(ai);
            if (opp == null) return true;
            if (engine.FirstEmpty(opp.MonsterZones) < 0) return true;
            return !opp.Graveyard.Any(g => g?.Def != null && g.Def.IsMonster);
        }

        // ───────────────────── Card of Safe Return ─────────────────────

        /// <summary>
        /// Called by DuelEngine.CompleteSpecialSummon for a Special Summon whose origin was a GY
        /// (DuelEngine.NextSpecialSummonFromGraveyard; every GY → field Special Summon path sets it).
        /// <paramref name="gyOwner"/> is the player whose GY the monster left (its owner: Monster Reborn on an
        /// opponent's monster records <see cref="CardInstance.ControlOwner"/>). Each face-up Card of Safe Return that
        /// player controls draws 1 (Lua: EVENT_SPSUMMON_SUCCESS, IsPreviousLocation(GRAVE) and IsPreviousControler(tp);
        /// IsPlayerCanDraw(tp,1)). An optional "When …" trigger misses its timing when the Summon was not the last
        /// thing to happen: a Summon by a Chain Link with more links still to resolve. A copy whose activation is still
        /// on the Chain, whose effects are negated (Spell Canceller, Imperial Order) or whose triggers are locked (Cold
        /// Wave) does nothing. "You can": the draw is always taken when the Deck has a card.
        /// </summary>
        public static void OnSpecialSummoned(DuelEngine engine, DuelistState gyOwner, CardInstance summoned)
        {
            if (engine == null || gyOwner == null || summoned == null || engine.GameOver) return;
            // One event for a group Special Summon (BeginGroupSpecialSummon): answered once it is complete.
            if (s_group.TryGetValue(engine, out var group) && group.Depth > 0)
            {
                group.FromGy.Add((gyOwner, summoned));
                return;
            }

            foreach (var st in gyOwner.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || !st.FaceUp || st.IsNegated || st.ActivationUnresolvedOnChain) continue;
                var clause = ClauseOf(st, EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy);
                if (clause == null || clause.Timing != EffectTiming.ContinuousWhileFaceUp) continue;
                if (ContinuousNegationRuntime.NegatedBy(engine, st) != null) continue;
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                if (engine.IsResolvingChain && engine.Chain.Count > 0)
                {
                    engine.Log($"{st.Name}: the Special Summon was not the last thing to happen — it misses the timing.");
                    continue;
                }

                if (gyOwner.Deck == null || gyOwner.Deck.Count == 0)
                {
                    engine.Log($"{st.Name}: {gyOwner.Name} has no card to draw.");
                    continue;
                }

                var n = Math.Max(1, clause.Amount);
                engine.Log($"{st.Name}: {summoned.Name} was Special Summoned from {gyOwner.Name}'s GY — draw {n}.");
                engine.Draw(gyOwner, n);
                if (engine.GameOver) return;
            }
        }

        sealed class GroupSummon
        {
            public int Depth;
            public readonly List<(DuelistState gyOwner, CardInstance summoned)> FromGy =
                new List<(DuelistState, CardInstance)>();
        }

        static readonly ConditionalWeakTable<DuelEngine, GroupSummon> s_group =
            new ConditionalWeakTable<DuelEngine, GroupSummon>();

        /// <summary>
        /// v159-lon-b audit: an effect that Special Summons several monsters at once (De-Fusion's materials, Lua
        /// Duel.SpecialSummon(group)) raises one EVENT_SPSUMMON_SUCCESS, so Card of Safe Return draws once per GY owner
        /// for the whole group, not once per monster. Pair with <see cref="EndGroupSpecialSummon"/> (try/finally).
        /// </summary>
        public static void BeginGroupSpecialSummon(DuelEngine engine)
        {
            if (engine == null) return;
            s_group.GetOrCreateValue(engine).Depth++;
        }

        /// <summary>Ends the group opened by <see cref="BeginGroupSpecialSummon"/> and answers its one event.</summary>
        public static void EndGroupSpecialSummon(DuelEngine engine)
        {
            if (engine == null || !s_group.TryGetValue(engine, out var group) || group.Depth <= 0) return;
            if (--group.Depth > 0) return;
            var events = group.FromGy.ToList();
            group.FromGy.Clear();
            foreach (var owner in events.Select(x => x.gyOwner).Distinct().ToList())
                OnSpecialSummoned(engine, owner, events.First(x => x.gyOwner == owner).summoned);
        }

        // ───────────────────── Chosen One ─────────────────────

        /// <summary>ActivationCondition of Chosen One: Lua s.target chk.</summary>
        public const string ChosenOneHand = "ChosenOneHand";

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet: <see cref="ChosenOneHand"/> — a free Monster Zone, a monster in
        /// the hand that can be Special Summoned, and 2 Spell/Trap Cards in the hand other than this card. Returns
        /// false for any other condition.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c?.ActivationCondition != ChosenOneHand) return false;
            reason = "Needs a free Monster Zone, a monster in your hand that can be Special Summoned, and 2 other " +
                     "Spell/Trap Cards in your hand.";
            ok = engine != null && who != null && engine.FirstEmpty(who.MonsterZones) >= 0 &&
                 HandMonsters(engine, who, card).Count > 0 && HandSpellTraps(who, card).Count >= 2;
            return true;
        }

        static List<CardInstance> HandMonsters(DuelEngine engine, DuelistState who, CardInstance source) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(h => h?.Def != null && h.Def.IsMonster &&
                                      FlipMassEffectsRuntime.HandOrDeckCopyCanBeSpecialSummoned(who, h) &&
                                      !HandSummonLocked(engine, who, h, source)).ToList();

        /// <summary>
        /// RuleLocks batch (v159-lon-b audit): Lua s.spfilter IsCanBeSpecialSummoned is also false under Jowgen the
        /// Spiritualist / Jam Breeding Machine, and for a card in the hand when Chain Energy's SPSUMMON_COST cannot be
        /// paid. While Chosen One is still in the hand its own activation toll is already taken
        /// (DuelEngine.HandActionCostPaid); ygopro checks the target before paying the cost, so the LP check is skipped.
        /// </summary>
        static bool HandSummonLocked(DuelEngine engine, DuelistState who, CardInstance monster, CardInstance source)
        {
            if (engine == null) return false;
            if (RuleLocksRuntime.SpecialSummonLocked(engine, who, monster)) return true;
            if (source != null && who.Hand.Contains(source) && engine.HandActionCostPaid.Contains(source)) return false;
            return RuleLocksRuntime.HandSpecialSummonUnaffordable(engine, who);
        }

        static List<CardInstance> HandSpellTraps(DuelistState who, CardInstance except) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(h => h?.Def != null && h != except && (h.Def.IsSpell || h.Def.IsTrap)).ToList();

        sealed class ChosenOnePlan
        {
            public DuelistState Who;
            public CardInstance Source;
            public CardInstance Monster;
            public BattlePosition Position = BattlePosition.Attack;
            public int Zone = -1;
            public readonly List<CardInstance> SpellTraps = new List<CardInstance>();
        }

        static readonly ConditionalWeakTable<PendingActivation, ChosenOnePlan> s_chosenOne =
            new ConditionalWeakTable<PendingActivation, ChosenOnePlan>();

        /// <summary>
        /// Chosen One resolves (Lua s.activate): with a free Monster Zone, a monster that can be Special Summoned and 2
        /// Spell/Trap Cards in the hand, the controller selects the monster, then the 2 Spell/Traps. A human picks them
        /// from the hand (the monster pick also asks its face-up Attack or Defense Position, Lua POS_FACEUP); the AI
        /// takes its highest-ATK monster and its 2 lowest-value Spell/Traps. Then <see cref="FinishChosenOne"/>.
        /// </summary>
        public static void ResolveChosenOne(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: no free Monster Zone — nothing happens.");
                return;
            }

            var monsters = HandMonsters(engine, who, source);
            var spellTraps = HandSpellTraps(who, source);
            if (monsters.Count == 0 || spellTraps.Count < 2)
            {
                engine.Log($"{source?.Name}: no monster and 2 Spell/Trap Cards to select in the hand — nothing happens.");
                return;
            }

            var plan = new ChosenOnePlan { Who = who, Source = source };
            if (engine.IsHumanControlled(who) && engine.PendingActivation == null)
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    HoldsChainResolution = true,
                    TargetKind = EffectTargetKind.DiscardFromHand,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"{source?.Name}: select 1 monster in your hand (Summon ATK: face-up Attack " +
                                     "Position, Summon DEF: face-up Defense Position if it is Special Summoned)."
                };
                pending.LegalTargets.AddRange(monsters);
                s_chosenOne.Add(pending, plan);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            AutoComplete(plan, monsters, spellTraps);
            FinishChosenOne(engine, plan);
        }

        static void AutoComplete(ChosenOnePlan plan, List<CardInstance> monsters, List<CardInstance> spellTraps)
        {
            if (plan.Monster == null)
            {
                plan.Monster = monsters.OrderByDescending(m => m.Def?.atk ?? 0).ThenBy(m => m.Name).FirstOrDefault();
                var d = plan.Monster?.Def;
                plan.Position = d != null && d.def > d.atk ? BattlePosition.Defense : BattlePosition.Attack;
            }

            var pool = spellTraps.Where(c => !plan.SpellTraps.Contains(c)).ToList();
            while (plan.SpellTraps.Count < 2 && pool.Count > 0)
            {
                var c = AiHeuristicPolicy.PickLowestValue(pool, null) ?? pool[0];
                plan.SpellTraps.Add(c);
                pool.Remove(c);
            }
        }

        /// <summary>
        /// SpellTrapEffects.PendingTargetIsSpecialSummon: the monster pick of Chosen One asks Attack / Defense and a
        /// zone (the monster may be Special Summoned face-up); the Spell/Trap picks do not. False for any other pick.
        /// </summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = false;
            if (pending == null || !s_chosenOne.TryGetValue(pending, out var plan)) return false;
            asksPlacement = plan.Monster == null;
            return true;
        }

        /// <summary>This batch owns the open pick (tests read it).</summary>
        public static bool IsChosenOnePick(PendingActivation pending) =>
            pending != null && s_chosenOne.TryGetValue(pending, out _);

        /// <summary>
        /// TextEffectRuntime.TryResolveTextTarget: a tap on one of the offered hand cards. The monster comes first, then
        /// the 2 Spell/Traps; the last pick finishes the effect. Returns false for any other pending activation.
        /// </summary>
        public static bool TryContinueHandPick(DuelEngine engine, PendingActivation p, CardInstance target, out bool ok)
        {
            ok = false;
            if (engine == null || p == null || !s_chosenOne.TryGetValue(p, out var plan)) return false;
            var who = plan.Who;
            if (target == null || !p.LegalTargets.Contains(target) || who?.Hand == null || !who.Hand.Contains(target))
            {
                engine.Log("Not one of the offered cards.");
                return true;
            }

            ok = true;
            if (plan.Monster == null)
            {
                plan.Monster = target;
                plan.Position = engine.PendingSpecialSummonPosition == BattlePosition.Defense
                    ? BattlePosition.Defense
                    : BattlePosition.Attack;
                plan.Zone = engine.PendingSpecialSummonZone;
                engine.ClearPendingSpecialSummonPlacement();
                engine.Log($"{plan.Source?.Name}: {target.Name} selected.");
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(HandSpellTraps(who, plan.Source));
                p.TargetPicksRemaining = 2;
                p.PromptOverride = $"{plan.Source?.Name}: select 2 Spell/Trap Cards in your hand.";
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            plan.SpellTraps.Add(target);
            p.LegalTargets.Remove(target);
            p.TargetPicksRemaining = Math.Max(0, 2 - plan.SpellTraps.Count);
            engine.Log($"{plan.Source?.Name}: {target.Name} selected.");
            if (plan.SpellTraps.Count < 2 && p.LegalTargets.Count > 0)
            {
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            s_chosenOne.Remove(p);
            engine.ClearPendingActivation();
            FinishChosenOne(engine, plan);
            engine.ResumeChainAfterChoice();
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: the selection belongs to a resolving effect and cannot be skipped — the
        /// remaining choices are made automatically (highest-ATK monster in face-up Attack or Defense Position, the
        /// lowest-value Spell/Traps). Returns false for any other pending activation.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_chosenOne.TryGetValue(p, out var plan)) return false;
            s_chosenOne.Remove(p);
            engine.ClearPendingActivation();
            engine.ClearPendingSpecialSummonPlacement();
            engine.Log($"{plan.Source?.Name}: the selection cannot be skipped — the remaining cards are chosen.");
            AutoComplete(plan, HandMonsters(engine, plan.Who, plan.Source), HandSpellTraps(plan.Who, plan.Source));
            FinishChosenOne(engine, plan);
            engine.ResumeChainAfterChoice();
            return true;
        }

        /// <summary>
        /// The 3 selected cards are shown; the opponent selects 1 at random (engine.Rng). The monster: it is Special
        /// Summoned face-up and the 2 Spell/Traps go to the GY. A Spell/Trap: all 3 go to the GY.
        /// </summary>
        static void FinishChosenOne(DuelEngine engine, ChosenOnePlan plan)
        {
            var who = plan.Who;
            var source = plan.Source;
            if (engine == null || who?.Hand == null || plan.Monster == null || plan.SpellTraps.Count < 2) return;
            var picks = new List<CardInstance> { plan.Monster };
            picks.AddRange(plan.SpellTraps.Take(2));
            if (picks.Any(c => !who.Hand.Contains(c)))
            {
                engine.Log($"{source?.Name}: a selected card left the hand — nothing happens.");
                return;
            }

            engine.Log($"{source?.Name}: {who.Name} shows {string.Join(", ", picks.Select(c => c.Name))}.");
            var chosen = picks[Math.Max(0, engine.Rng.PickIndex(picks.Count))];
            engine.Log($"{source?.Name}: {engine.OpponentOf(who)?.Name ?? "The opponent"} randomly selects {chosen.Name}.");
            List<CardInstance> toGrave;
            if (ReferenceEquals(chosen, plan.Monster) &&
                FlipMassEffectsRuntime.HandOrDeckCopyCanBeSpecialSummoned(who, chosen) &&
                !HandSummonLocked(engine, who, chosen, null) &&
                engine.FirstEmpty(who.MonsterZones) >= 0)
            {
                who.Hand.Remove(chosen);
                engine.MarkSpecialSummonFromHand(chosen); // Chain Energy's hand toll (RuleLocks batch)
                if (engine.SpecialSummonToField(who, chosen, plan.Position, true, SummonKind.SpecialSummon, plan.Zone))
                    engine.Log($"{source?.Name}: {chosen.Name} is Special Summoned in face-up " +
                               $"{(plan.Position == BattlePosition.Defense ? "Defense" : "Attack")} Position.");
                else
                {
                    if (!who.Hand.Contains(chosen)) who.Hand.Add(chosen);
                    engine.Log($"{source?.Name}: {chosen.Name} could not be Special Summoned.");
                }

                toGrave = plan.SpellTraps.Take(2).ToList();
            }
            else
                toGrave = picks;

            foreach (var c in toGrave)
                if (who.Hand.Contains(c))
                    engine.SendCardToGrave(who, c, sentBy: source);
            engine.Log($"{source?.Name}: {string.Join(", ", toGrave.Select(c => c.Name))} sent to the GY.");
            engine.NotifyPublic();
        }
    }
}
