using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Executes <see cref="CompiledCardProgram"/> clauses produced from official card text.
    /// Structural rules stay in <see cref="DuelEngine"/>; this only applies remembered text ops.
    /// </summary>
    public static class TextEffectRuntime
    {
        /// <summary>
        /// Face-up Field Spell mandatory/trigger on NS/SS (Harpies' Hunting Ground).
        /// Fires after the summon response window so Torrential can destroy the Field first.
        /// </summary>
        public static bool TryTriggerFaceUpFieldSummon(DuelEngine engine, DuelistState summoner,
            CardInstance summoned)
        {
            if (engine == null || summoned == null) return false;
            if (engine.IsAwaitingEffectTarget) return false;
            for (var side = 0; side < 2; side++)
            {
                var who = side == 0 ? engine.Player : engine.Opponent;
                var field = who?.FieldSpellZone?.Occupant;
                if (field == null || !field.FaceUp || field.IsNegated || field.Def == null)
                    continue;
                var prog = CompiledEffectCache.GetOrCompile(field.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                var clauses = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                if (clauses.Count == 0) continue;
                var clause = clauses[0];
                if (!SpellTrapEffects.SummonWindowClauseLegal(clause, summoned, summoner, who))
                    continue;
                engine.Log($"{field.Name} trigger.");
                if (clause.RequiresTargetChoice)
                {
                    var targets = CollectTargets(engine, who, clause, field);
                    if (targets.Count == 0)
                    {
                        engine.Log($"{field.Name}: no legal Spell/Trap target.");
                        continue;
                    }

                    if (!who.IsPlayer)
                    {
                        var pick = AutoPick(clause, targets, who, engine);
                        var dummyAi = false;
                        foreach (var c in clauses)
                            ApplyClause(engine, who, field, c, pick, ref dummyAi, ref dummyAi);
                        engine.NotifyPublic();
                        return true;
                    }

                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = field,
                        FromHand = false,
                        TargetKind = MapTargetKind(clause),
                        UsesTextProgram = true,
                        FromSummonWindow = true
                    };
                    pending.LegalTargets.AddRange(targets);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                var dummy = false;
                foreach (var c in clauses)
                    ApplyClause(engine, who, field, c, summoned, ref dummy, ref dummy);
                engine.NotifyPublic();
                return true;
            }

            return false;
        }

        /// <summary>Can this compiled Activate program be used right now (timing + targets)?</summary>
        public static bool CanActivate(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || prog == null)
            {
                reason = "Invalid context.";
                return false;
            }

            var activate = prog.ClausesFor(EffectTiming.Activate);
            var attack = prog.ClausesFor(EffectTiming.AttackDeclared);
            var summon = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
            var dmg = prog.ClausesFor(EffectTiming.DamageCalculation);
            var takeLp = prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
            var chainNeg = prog.ClausesFor(EffectTiming.ChainLinkActivated);
            var continuousPlay = card.Def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                                 engine.PendingResponse == null;
            if (activate.Count == 0 && attack.Count == 0 && summon.Count == 0 && dmg.Count == 0 &&
                takeLp.Count == 0 && chainNeg.Count == 0 && !continuousPlay)
            {
                reason = "No activatable clauses in learned text.";
                return false;
            }

            // Chain-response (monster QE or Counter Trap) before ordinary combat-response paths.
            if (engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse)
                return CanActivateChainResponse(engine, who, card, prog, out reason);
            if (engine.PendingResponse != null)
            {
                if (engine.PendingResponse.Responder != who)
                {
                    reason = "Not your response.";
                    return false;
                }

                if (fromHand)
                {
                    reason = "Cannot activate from hand in this response window.";
                    return false;
                }

                if (card.SetThisTurn)
                {
                    reason = "Cannot activate the turn it was Set.";
                    return false;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                    attack.Count > 0)
                {
                    if (!SpellTrapEffects.AttackWindowClauseLegal(prog, engine))
                    {
                        reason = "Attack does not meet this card's condition.";
                        return false;
                    }
                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.DamageCalculation &&
                    dmg.Count > 0)
                {
                    if (!DamageStepClausesLegal(engine, who, card, dmg))
                    {
                        reason = "Damage Step condition not met.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned &&
                    summon.Count > 0)
                {
                    if (!SpellTrapEffects.SummonWindowClauseLegal(summon[0],
                            engine.PendingResponse.Summoned, engine.PendingResponse.Summoner, who))
                    {
                        reason = "Summon does not meet this card's condition.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                    attack.Count > 0)
                {
                    if (!SpellTrapEffects.AttackWindowClauseLegal(prog, engine))
                    {
                        reason = "Attack does not meet this card's condition.";
                        return false;
                    }
                }

                // Free-chain (Waboku) and opponent-turn Activate traps (Absolute End)
                // may answer an attack declaration.
                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared &&
                    (attack.Count > 0 ||
                     SpellTrapEffects.IsCompiledFreeChain(card.Def, prog)))
                {
                    foreach (var c in activate)
                    {
                        if (c != null && !string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                            !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                        {
                            reason = $"Requires \"{c.RequiresFaceUpName}\" on the field.";
                            return false;
                        }
                    }

                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                    SpellTrapEffects.IsCompiledFreeChain(card.Def, prog))
                {
                    if (engine.TurnPlayer == who)
                    {
                        reason = "Only during opponent's turn.";
                        return false;
                    }

                    foreach (var c in activate)
                    {
                        if (c != null && !string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                            !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                        {
                            reason = $"Requires \"{c.RequiresFaceUpName}\" on the field.";
                            return false;
                        }
                    }

                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage &&
                    takeLp.Count > 0)
                {
                    reason = "OK";
                    return true;
                }

                reason = "Not legal for this response timing.";
                return false;
            }

            // Open game state — trigger-only timings (Trap Hole, Mirror Force, …)
            // must not look legal outside their response window.
            // FullyCompiled Continuous S/T: playing the card is the activation even
            // when the printed effect is only a later trigger (Toll / Poison Fangs).
            if (activate.Count == 0)
            {
                if (!continuousPlay &&
                    (attack.Count > 0 || summon.Count > 0 || dmg.Count > 0 || takeLp.Count > 0))
                {
                    reason = "Only legal in response to the matching trigger (attack / summon / damage).";
                    return false;
                }

                if (!continuousPlay)
                {
                    reason = "No activatable clauses in learned text.";
                    return false;
                }
            }

            // Open game state
            if (engine.HasDeclaredAttack)
            {
                reason = "Finish the declared attack / response first.";
                return false;
            }

            if (engine.IsAwaitingResponse)
            {
                reason = "Respond or pass the open window first.";
                return false;
            }

            var oppTurnOnly = activate.Any(c => c.OpponentTurnOnly);
            var isQuick = activate.Any(c => c.IsQuickEffect);
            if (oppTurnOnly)
            {
                if (engine.TurnPlayer == who)
                {
                    reason = "Only during opponent's turn.";
                    return false;
                }
            }
            else if (engine.TurnPlayer != who && !isQuick && !card.Def.IsTrap)
            {
                reason = "Not your turn.";
                return false;
            }

            if (card.Def.IsMonster)
            {
                var handIgnition = fromHand && activate.Exists(c => c != null && c.ActivatesFromHand);
                if (fromHand && !handIgnition)
                {
                    reason = "Summon this monster first, then activate its effect.";
                    return false;
                }

                if (!fromHand)
                {
                    if (card.EquippedTo == null)
                    {
                        if (!who.TryFindMonster(card, out _))
                        {
                            reason = "Not a face-up monster you control.";
                            return false;
                        }

                        if (!card.FaceUp)
                        {
                            reason = "Face-down monsters cannot activate ignition effects (Flip Summon first).";
                            return false;
                        }
                    }
                }
                else if (!who.Hand.Contains(card))
                {
                    reason = "Not in hand.";
                    return false;
                }

                if (activate.Any(c => c.OncePerTurn) && card.EffectUsedThisTurn)
                {
                    reason = "Already used this effect this turn.";
                    return false;
                }
            }
            else if (fromHand)
            {
                if (!who.Hand.Contains(card))
                {
                    reason = "Not in hand.";
                    return false;
                }

                if (card.Def.IsTrap)
                {
                    reason = "Traps must be Set first.";
                    return false;
                }

                if (engine.FirstEmptySpellTrap(who) < 0)
                {
                    reason = "No free Spell/Trap Zone.";
                    return false;
                }
            }
            else
            {
                if (!who.TryFindSpellTrap(card, out _))
                {
                    reason = "Not on field.";
                    return false;
                }

                if (card.SetThisTurn && (card.Def.IsTrap || SpellTrapEffects.IsQuickPlay(card.Def)))
                {
                    reason = "Cannot activate the turn it was Set.";
                    return false;
                }
            }

            var altIgnitions = activate.FindAll(HasStructuredIgnitionCost);
            if (altIgnitions.Count > 1)
            {
                string last = "No legal tribute / cost for this effect.";
                var any = false;
                foreach (var c in altIgnitions)
                {
                    if (!IgnitionCostLegal(engine, who, card, c, out var r))
                    {
                        last = r;
                        continue;
                    }

                    if (c.RequiresTargetChoice &&
                        !HasLegalTargetsConsideringDiscard(engine, who, c, card))
                    {
                        last = "No legal targets.";
                        continue;
                    }

                    any = true;
                    break;
                }

                if (!any)
                {
                    reason = last;
                    return false;
                }
            }

            foreach (var c in activate)
            {
                if (altIgnitions.Count > 1 && HasStructuredIgnitionCost(c))
                    continue;

                if (c.RequiresLordOfDOnField && !LordOfDOnField(engine))
                {
                    reason = "Lord of D. must be on the field.";
                    return false;
                }

                if (c.RequiresSendNamedToGy)
                {
                    if (CollectSendNamedCost(who, c.RequiresFaceUpName).Count == 0)
                    {
                        reason = string.IsNullOrEmpty(c.RequiresFaceUpName)
                            ? "No face-up named card you control to send to the GY."
                            : $"No face-up \"{c.RequiresFaceUpName}\" you control to send to the GY.";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                         !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                {
                    reason = $"Requires \"{c.RequiresFaceUpName}\" on the field.";
                    return false;
                }

                if (c.RequiresDiscardCost &&
                    CollectDiscardCost(who, c.DiscardCostAttribute, card).Count < DiscardNeed(c))
                {
                    reason = c.DiscardCostAttribute == "*"
                        ? "No card in hand to discard."
                        : string.IsNullOrEmpty(c.DiscardCostAttribute)
                            ? "No monster in hand to discard."
                            : $"No {c.DiscardCostAttribute} monster in hand to discard.";
                    return false;
                }

                if (c.RequiresRemoveSpellCounters > 0 && card.Counters < c.RequiresRemoveSpellCounters)
                {
                    reason = $"Needs {c.RequiresRemoveSpellCounters} Spell Counter(s).";
                    return false;
                }

                if (c.RequiresSpellCounters > 0 && card.Counters < c.RequiresSpellCounters)
                {
                    reason = $"Needs {c.RequiresSpellCounters} Spell Counter(s).";
                    return false;
                }

                if (c.Action == EffectActionKind.UnequipThisSpecialSummon && card.EquippedTo == null)
                    continue;
                if (c.Action == EffectActionKind.EquipThisToTarget && card.EquippedTo != null)
                    continue;
                // Relinquished family (Ignis c64631466 eqcon): max. 1 — refuse while already equipped.
                if (c.Action == EffectActionKind.EquipTargetToThis &&
                    card.Equips != null && card.Equips.Count > 0)
                {
                    reason = "Already equipped (max. 1).";
                    return false;
                }

                if (c.RequiresBattlePhase && engine.Phase != DuelPhase.Battle)
                {
                    reason = "Only during the Battle Phase.";
                    return false;
                }

                if (c.Action == EffectActionKind.MagicalHatsStyle &&
                    !MagicalHatsLegal(engine, who, c, out reason))
                    return false;

                if (!IgnitionCostLegal(engine, who, card, c, out reason))
                    return false;

                if (c.RequiresTargetChoice &&
                    !HasLegalTargetsConsideringDiscard(engine, who, c, card))
                {
                    reason = "No legal targets.";
                    return false;
                }

                if (c.TakeControlOfTarget && engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    reason = "No Monster Zone to take control.";
                    return false;
                }

                if (c.Action == EffectActionKind.FusionSummonRegistered &&
                    !SpellTrapEffects.CanResolveAnyFusion(engine, who))
                {
                    reason = "No legal Fusion Materials.";
                    return false;
                }

                if (c.Action == EffectActionKind.RitualSummon &&
                    !WRLDZ.Duel.Rules.RitualProcedures.CanPerform(engine, who, card, null, null,
                        out reason))
                    return false;

                if ((c.Action == EffectActionKind.SpecialSummonFromGy &&
                    engine.FirstEmpty(who.MonsterZones) < 0) || (c.Action == EffectActionKind.SpecialSummonFromHand && engine.FirstEmpty(who.MonsterZones) < 0) || (c.Action == EffectActionKind.SpecialSummonFromDeck && engine.FirstEmpty(who.MonsterZones) < 0))
                {
                    reason = "No free Monster Zone.";
                    return false;
                }
            }

            // Normal Spells / ignition: Main Phase.
            // Traps, Quick-Play Spells, and monster Quick Effects: Main or Battle.
            var isQuickPlay = SpellTrapEffects.IsQuickPlay(card.Def) || isQuick;
            var isTrap = card.Def.IsTrap;
            if (isTrap || isQuickPlay)
            {
                if (!(engine.InMainPhase || engine.Phase == DuelPhase.Battle) && !oppTurnOnly)
                {
                    reason = "Wrong phase.";
                    return false;
                }
            }
            else if (!engine.InMainPhase && !oppTurnOnly)
            {
                reason = card.Def.IsMonster
                    ? "Ignition effects only in Main Phase."
                    : "Normal Spells only in Main Phase.";
                return false;
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// Resolve Activate clauses (or response-window AttackDeclared / Summon clauses).
        /// Returns true if handled (including target-pending).
        static bool CanActivateChainResponse(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog, out string reason)
        {
            reason = "OK";
            var pending = engine?.PendingResponse;
            var target = pending?.ChainTarget;
            var clauses = prog?.ClausesFor(EffectTiming.ChainLinkActivated);
            if (pending == null || pending.Responder != who || target?.Card?.Def == null ||
                clauses == null || clauses.Count == 0)
            {
                reason = "Not a chain-activation response.";
                return false;
            }

            var clause = clauses[0];
            var last = target.Card.Def;
            var answersSpell = clause.NegateRespondsToSpell ||
                               (!clause.NegateRespondsToSpell && !clause.NegateRespondsToTrap);
            var answersTrap = clause.NegateRespondsToTrap;
            if (last.IsSpell && !answersSpell)
            {
                reason = "Does not answer a Spell activation.";
                return false;
            }

            if (last.IsTrap && !answersTrap)
            {
                reason = "Does not answer a Trap activation.";
                return false;
            }

            if (!last.IsSpell && !last.IsTrap)
            {
                reason = "Last link is not a Spell or Trap activation.";
                return false;
            }

            if (card?.Def == null)
            {
                reason = "Invalid response card.";
                return false;
            }

            if (card.Def.IsTrap || SpellTrapEffects.IsQuickPlay(card.Def))
            {
                if (!who.TryFindSpellTrap(card, out _))
                {
                    reason = "Counter Trap must be Set on the field.";
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
            }
            else
            {
                if (!card.Def.IsMonster || !who.TryFindMonster(card, out _))
                {
                    reason = "Only a monster response on the field may answer this activation.";
                    return false;
                }

                if (clause.FlipSelfFaceUpDefense)
                {
                    if (card.FaceUp)
                    {
                        reason = "Big Shield Gardna must be face-down.";
                        return false;
                    }

                    if (target.Targets == null || target.Targets.Count != 1 || target.Targets[0] != card)
                    {
                        reason = "The Spell must target only this face-down monster.";
                        return false;
                    }
                }
                else if (!card.FaceUp)
                {
                    reason = "This monster must be face-up.";
                    return false;
                }
            }

            if (clause.PayLpAmount > 0 && who.LifePoints < clause.PayLpAmount)
            {
                reason = $"Not enough LP to pay {clause.PayLpAmount}.";
                return false;
            }

            if (clause.RequiresDiscardCost &&
                CollectDiscardCost(who, clause.DiscardCostAttribute, card).Count < DiscardNeed(clause))
            {
                reason = "No card in hand to discard.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Apply a seated chain link at resolution. Activation already paid cost / flipped
        /// the card; this must not re-enter the response window.
        /// </summary>
        public static bool TryResolveSeatedResponse(DuelEngine engine, ChainLink link,
            CompiledCardProgram prog)
        {
            if (engine == null || link?.Card == null || prog == null) return false;
            var who = link.Controller;
            var card = link.Card;
            var timing = link.SeatedFrom;
            var dummyNeg = false;
            var dummyEnd = false;
            List<EffectClause> clauses;
            CardInstance implicitTarget = null;
            if (timing == ResponseTiming.AttackDeclared)
            {
                clauses = new List<EffectClause>(
                    prog.ClausesFor(EffectTiming.AttackDeclared) ?? new List<EffectClause>());
                engine.TryGetBattlingPair(out var attackingMon, out _);
                implicitTarget = attackingMon;
                // Absolute End / Waboku-style free-chain answers live on Activate, not AttackDeclared.
                var activate = prog.ClausesFor(EffectTiming.Activate);
                if (activate != null)
                {
                    foreach (var c in activate)
                    {
                        if (c == null) continue;
                        if (c.Action == EffectActionKind.ApplyWabokuStyle ||
                            c.OpponentTurnOnly ||
                            c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn ||
                            SpellTrapEffects.IsCompiledFreeChain(card.Def, prog))
                        {
                            if (!clauses.Contains(c))
                                clauses.Add(c);
                        }
                    }
                }
            }
            else if (timing == ResponseTiming.MonsterSummoned)
            {
                clauses = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                implicitTarget = link.SeatedSummoned;
            }
            else if (timing == ResponseTiming.YouTakeDamage)
                clauses = prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
            else if (timing == ResponseTiming.DamageCalculation)
                clauses = prog.ClausesFor(EffectTiming.DamageCalculation);
            else
                clauses = prog.ClausesFor(EffectTiming.Activate);

            foreach (var c in clauses)
            {
                if (c == null) continue;
                if (!ConditionHolds(engine, who, card, c, implicitTarget, atResolution: true))
                {
                    engine.Log($"{card.Name}: condition not met at resolution — effect fizzles.");
                    continue;
                }

                var tgt = implicitTarget;
                if (c.Zone == EffectZoneFilter.AttackingMonster)
                    engine.TryGetBattlingPair(out tgt, out _);
                else if (c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                    tgt = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                ApplyClause(engine, who, card, c, tgt, ref dummyNeg, ref dummyEnd);
            }

            engine.ChainNegatedAttack |= dummyNeg;
            engine.ChainEndedBattle |= dummyEnd;
            FinishSpellTrap(engine, who, card,
                stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            return true;
        }

        static bool ClauseMakesChainLink(List<EffectClause> clauses)
        {
            if (clauses == null || clauses.Count == 0) return false;
            for (var i = 0; i < clauses.Count; i++)
                if (clauses[i] != null && clauses[i].MakesChainLink)
                    return true;
            return false;
        }

        /// <summary>
        /// PSCT when/if. "When" (Activation) needs the matching window still open.
        /// "If" (Both/Resolution) is re-checked at resolution via the event log.
        /// </summary>
        static bool ConditionHolds(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, CardInstance implicitTarget, bool atResolution)
        {
            if (c == null) return true;
            var need = c.CheckedAt;
            if (atResolution && need == ConditionCheckedAt.Activation)
                return true;
            if (!atResolution && need == ConditionCheckedAt.Resolution)
                return true;
            if (c.RequiresThisIsAttackTarget && implicitTarget != card)
            {
                if (engine.PendingResponse?.AttackTarget != card &&
                    engine.DeclaredAttackTarget != card)
                    return false;
            }

            return true;
        }

        static bool SeatResponseOnChain(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, ResponseTiming timing, CardInstance summoned)
        {
            PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");
            engine.GameEvents.Add(DuelGameEventKind.Activated, engine.TurnNumber, who, card);
            if (engine.Chain.Last != null && engine.Chain.Last.Card == card)
            {
                engine.Chain.Last.SeatedFrom = timing;
                engine.Chain.Last.SeatedSummoned = summoned;
            }

            engine.ResumeAfterChain = timing;
            var opp = engine.OpponentOf(who);
            engine.ClearPendingResponse();
            if (!engine.OpenChainResponseWindow(opp))
            {
                engine.Chain.StartResolution();
                engine.ResolveChainStackForActivation();
            }

            return true;
        }

        public static bool TryResolveActivation(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget,
            CardInstance forcedTarget = null)
        {
            if (prog == null) return false;
            var activateClauses = prog.ClausesFor(EffectTiming.Activate);

            // Response windows
            if (engine.PendingResponse != null && engine.PendingResponse.Responder == who)
            {
                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
                {
                    var clauses = prog.ClausesFor(EffectTiming.AttackDeclared);
                    var freeChain = SpellTrapEffects.IsCompiledFreeChain(card.Def, prog)
                        ? activateClauses
                        : activateClauses.FindAll(c =>
                            c != null &&
                            (c.Action == EffectActionKind.ApplyWabokuStyle ||
                             c.OpponentTurnOnly ||
                             c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn));
                    if (clauses.Count == 0 && freeChain.Count == 0)
                        return false;

                    if (ClauseMakesChainLink(clauses) || ClauseMakesChainLink(freeChain))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.AttackDeclared, null);

                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var attackNegated = false;
                    var battleEnded = false;
                    engine.TryGetBattlingPair(out var attackingMon, out _);
                    foreach (var c in clauses)
                    {
                        CardInstance tgt = null;
                        if (c.Zone == EffectZoneFilter.AttackingMonster)
                            tgt = attackingMon;
                        else if (c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                            tgt = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                        ApplyClause(engine, who, card, c, tgt, ref attackNegated, ref battleEnded);
                    }

                    foreach (var c in freeChain)
                        ApplyClause(engine, who, card, c, null, ref attackNegated, ref battleEnded);

                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    engine.ContinueAfterResponseActivation(attackNegated, battleEnded);
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState)
                {
                    var open = activateClauses;
                    if (open.Count == 0 ||
                        !SpellTrapEffects.IsCompiledFreeChain(card.Def, prog))
                        return false;
                    if (ClauseMakesChainLink(open))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.OpponentOpenState, null);
                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var dummy = false;
                    foreach (var c in open)
                        ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
                {
                    var clauses = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                    if (clauses.Count == 0) return false;
                    if (ClauseMakesChainLink(clauses))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.MonsterSummoned, engine.PendingResponse.Summoned);
                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var dummy = false;
                    var summoned = engine.PendingResponse.Summoned;
                    foreach (var c in clauses)
                        ApplyClause(engine, who, card, c, summoned, ref dummy, ref dummy);
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.DamageCalculation)
                {
                    var clauses = prog.ClausesFor(EffectTiming.DamageCalculation);
                    if (clauses.Count == 0 || !DamageStepClausesLegal(engine, who, card, clauses))
                        return false;
                    return ResolveDamageStepClauses(engine, who, card, fromHand, clauses,
                        autoPickTarget);
                }

                if (engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage)
                {
                    var clauses = prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
                    if (clauses.Count == 0) return false;
                    if (ClauseMakesChainLink(clauses))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.YouTakeDamage, null);
                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var dummy = false;
                    foreach (var c in clauses)
                        ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }
            }

            var activate = activateClauses
                .Where(c => c != null &&
                            !(c.Action == EffectActionKind.UnequipThisSpecialSummon &&
                              card.EquippedTo == null) &&
                            !(c.Action == EffectActionKind.EquipThisToTarget &&
                              card.EquippedTo != null))
                .ToList();
            if (activate.Count == 0)
            {
                if (card.Def != null && card.Def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                    engine.PendingResponse == null)
                {
                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    FinishSpellTrap(engine, who, card, stays: true);
                    engine.NotifyPublic();
                    return true;
                }

                return false;
            }

            var isMonster = card.Def != null && card.Def.IsMonster;
            var monsterIgnition = isMonster && !fromHand;

            var ign = activate.FirstOrDefault(c =>
                HasStructuredIgnitionCost(c) &&
                IgnitionCostLegal(engine, who, card, c, out _));
            if (ign != null)
            {
                return StartIgnition(engine, who, card, fromHand,
                    new List<EffectClause> { ign }, ign,
                    autoPickTarget, isMonster, forcedTarget);
            }

            var hats = activate.FirstOrDefault(c => c.Action == EffectActionKind.MagicalHatsStyle);
            if (hats != null)
                return StartMagicalHats(engine, who, card, fromHand, hats, autoPickTarget);

            var coin = activate.FirstOrDefault(c =>
                c.Action == EffectActionKind.CoinCallDestroyOppOrSelf ||
                c.Action == EffectActionKind.CoinCallDoubleOrHalveAtk);
            if (coin != null)
                return StartCoinCall(engine, who, card, fromHand, coin, autoPickTarget, isMonster);

            var sendNamed = activate.FirstOrDefault(c => c.RequiresSendNamedToGy);
            if (sendNamed != null && !sendNamed.RequiresTargetChoice)
            {
                return StartOrPaySendNamedThenResolve(engine, who, card, fromHand, activate,
                    sendNamed, autoPickTarget, monsterIgnition);
            }

            // Single targeted clause → pending or auto
            var targeted = activate.FirstOrDefault(c => c.RequiresTargetChoice);
            if (targeted != null)
            {
                if (targeted.RequiresDiscardCost && forcedTarget == null)
                {
                    if (!StartOrPayDiscardThenTarget(engine, who, card, fromHand, activate, targeted,
                            autoPickTarget, monsterIgnition))
                        return false;
                    return true;
                }

                var targets = CollectTargets(engine, who, targeted, card);
                if (forcedTarget != null)
                {
                    // Chain resolve: honor the target chosen during deferred activation.
                    if (!targets.Contains(forcedTarget))
                        targets.Insert(0, forcedTarget);
                }
                if (targets.Count == 0)
                {
                    engine.Log($"{card.Name}: no legal target — cannot activate.");
                    engine.NotifyPublic();
                    return false;
                }

                if (!isMonster &&
                    (who.FieldSpellZone == null || who.FieldSpellZone.Occupant != card) &&
                    !who.TryFindSpellTrap(card, out _))
                    PlaceFaceUp(engine, who, card, fromHand);
                engine.Log($"Activate: {card.Name}.");

                List<CardInstance> secondTargets = null;
                if (targeted.RequiresSecondTarget)
                {
                    secondTargets = CollectTargetsForZone(engine, who, targeted, card,
                        targeted.SecondZone);
                    if (secondTargets.Count == 0)
                    {
                        engine.Log($"{card.Name}: no second target — activation fails.");
                        if (!monsterIgnition)
                            FinishSpellTrap(engine, who, card, stays: false);
                        engine.NotifyPublic();
                        return true;
                    }
                }

                if (forcedTarget != null || autoPickTarget || !who.IsPlayer)
                {
                    var pick = forcedTarget ?? AutoPick(targeted, targets, who, engine);
                    var dummy = false;
                    var resolvedTargets = new List<CardInstance> { pick };
                    foreach (var c in activate)
                    {
                        if (c.RequiresTargetChoice)
                            ApplyClause(engine, who, card, c, pick, ref dummy, ref dummy);
                        else
                            ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    }

                    if (targeted.RequiresSecondTarget && secondTargets != null)
                    {
                        var secondList = secondTargets.Where(x => x != pick).ToList();
                        if (secondList.Count == 0) secondList = secondTargets;
                        var dummyClause = SecondTargetClause(targeted);
                        var pick2 = AutoPick(dummyClause, secondList, who, engine);
                        if (pick2 != null)
                        {
                            ApplySecondTarget(engine, who, card, targeted, pick2);
                            resolvedTargets.Add(pick2);
                        }
                    }

                    if (monsterIgnition)
                        card.EffectUsedThisTurn = targeted.OncePerTurn || card.EffectUsedThisTurn;
                    else
                        FinishSpellTrap(engine, who, card,
                            stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    NotifyTargetingEffectsResolved(engine, resolvedTargets, targeted.IsPsctTarget);
                    engine.NotifyPublic();
                    return true;
                }

                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    WasSetOnField = !fromHand && !monsterIgnition,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = monsterIgnition,
                    UsesTextProgram = true,
                    TargetPicksRemaining = targeted.ControllerTargetCount > 0
                        ? targeted.ControllerTargetCount
                        : (targeted.Amount > 1 ? targeted.Amount : 1),
                    TargetPicksAreUpTo = targeted.Amount > 1 && targeted.ControllerTargetCount <= 0
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            if (!isMonster)
                PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");

            var d = false;
            foreach (var c in activate)
                ApplyClause(engine, who, card, c, null, ref d, ref d);

            if (monsterIgnition)
            {
                if (activate.Any(c => c.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveTextTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || target == null) return false;
            var resolved = engine.ResolveLegalEffectTarget(target);
            if (resolved == null) return false;
            target = resolved;

            var who = p.Controller;
            var card = p.Card;
            var monsterIgnition = p.IsMonsterEffect || (card?.Def != null && card.Def.IsMonster);

            if (p.AwaitingHatsMonster)
            {
                p.LockedTarget = target;
                var hatsClause = CompiledEffectCache.GetOrCompile(card)
                    ?.ClausesFor(EffectTiming.Activate)
                    ?.Find(c => c != null && c.Action == EffectActionKind.MagicalHatsStyle);
                if (hatsClause != null && hatsClause.HatsAreTokens)
                {
                    engine.ClearPendingActivation();
                    ResolveMagicalHatsAnime(engine, who, card, target, hatsClause.HatTokenCount);
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.NotifyPublic();
                    return true;
                }

                p.AwaitingHatsMonster = false;
                p.AwaitingHatsDeck = true;
                p.ChosenTargets.Clear();
                p.TargetPicksRemaining = 2;
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(CollectHatsDeckCards(engine, who));
                engine.Log("Magical Hats: choose 2 Spell/Trap Cards from your Deck.");
                engine.NotifyPublic();
                return true;
            }

            if (p.AwaitingVirusDeck)
            {
                if (!p.LegalTargets.Contains(target) && !p.ChosenTargets.Contains(target))
                    return false;
                p.ChosenTargets.Add(target);
                p.LegalTargets.Remove(target);
                p.TargetPicksRemaining--;
                DestroyVirusDeckPick(engine, who, target, p.Card?.Name);
                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    engine.Log($"{p.Card?.Name ?? "Virus"}: opponent may destroy another matching monster in the Deck (up to 3).");
                    engine.NotifyPublic();
                    return true;
                }

                engine.ClearPendingActivation();
                engine.NotifyPublic();
                return true;
            }

            if (p.AwaitingDarkSage)
            {
                engine.ClearPendingActivation();
                FinishDarkSageSummon(engine, who, target);
                return true;
            }

            if (p.AwaitingHatsDeck)
            {
                if (!p.LegalTargets.Contains(target) && !p.ChosenTargets.Contains(target))
                    return false;
                p.ChosenTargets.Add(target);
                p.LegalTargets.Remove(target);
                p.TargetPicksRemaining--;
                if (p.TargetPicksRemaining > 0)
                {
                    engine.Log("Magical Hats: choose another Spell/Trap from your Deck.");
                    engine.NotifyPublic();
                    return true;
                }

                var host = p.LockedTarget;
                var hats = p.ChosenTargets.ToList();
                engine.ClearPendingActivation();
                ResolveMagicalHats(engine, who, card, host, hats);
                FinishSpellTrap(engine, who, card, stays: false);
                engine.NotifyPublic();
                return true;
            }

            if (!p.AwaitingIgnitionCost && !p.AwaitingDiscardCost && !p.AwaitingSendNamedCost &&
                !p.AwaitingEffectTribute &&
                TryContinueDualOrUpTo(engine, p, who, card, target, monsterIgnition))
                return true;

            if (p.AwaitingEffectTribute)
            {
                if (!p.LegalTargets.Contains(target) &&
                    engine.ResolveLegalEffectTarget(target) == null)
                    return false;
                var pick = engine.ResolveLegalEffectTarget(target) ?? target;
                var owner = engine.ControllerOf(pick) ?? who;
                engine.SendCardToGrave(owner, pick, sentBy: card);
                engine.Log($"{card?.Name}: {owner?.Name} Tributes {pick.Name}.");
                var printed = PrintedOriginalAtk(pick);
                var victim = engine.OpponentOf(engine.TurnPlayer) ?? engine.OpponentOf(who);
                engine.ClearPendingActivation();
                if (victim != null)
                    engine.ApplyEffectDamage(victim, printed / 2, card?.Name);
                engine.NotifyPublic();
                engine.ResumeEndTurnAfterTrigger();
                return true;
            }

            if (p.AwaitingIgnitionCost)
            {
                if (!PayOneIgnitionCost(engine, who, card, p, target))
                    return false;
                p.CostPicksRemaining--;
                var paidCost = p.CostNumeric;
                if (p.CostPicksRemaining > 0)
                {
                    var clauseMore = CompiledEffectCache.GetOrCompile(card)
                        ?.ClausesFor(EffectTiming.Activate)
                        .FirstOrDefault(HasStructuredIgnitionCost);
                    var more = CollectIgnitionCostCards(engine, who, card, clauseMore);
                    if (more.Count == 0)
                    {
                        engine.ClearPendingActivation();
                        return FinishIgnitionAfterCost(engine, who, card, p.FromHand,
                            p.IsMonsterEffect, paidCost);
                    }

                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(more);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                engine.ClearPendingActivation();
                if (who.Graveyard.Contains(card) && !who.TryFindSpellTrap(card, out _))
                {
                    PlaceThisOnTopOfDeck(engine, who, card);
                    engine.NotifyPublic();
                    return true;
                }

                var ecto = CompiledEffectCache.GetOrCompile(card)
                    ?.ClausesFor(EffectTiming.EndPhase)
                    .Find(c => c != null && c.TurnPlayerTributes);
                if (ecto != null)
                {
                    var printed = PrintedOriginalAtk(target);
                    var victim = engine.OpponentOf(engine.TurnPlayer) ?? engine.OpponentOf(who);
                    if (victim != null)
                        engine.ApplyEffectDamage(victim, printed / 2, card.Name);
                    engine.NotifyPublic();
                    engine.ResumeEndTurnAfterTrigger();
                    return true;
                }

                return FinishIgnitionAfterCost(engine, who, card, p.FromHand, p.IsMonsterEffect,
                    paidCost);
            }

            if (p.AwaitingSendNamedCost)
            {
                if (!PaySendNamedCost(engine, who, target))
                    return false;
                engine.ClearPendingActivation();
                var progSend = CompiledEffectCache.GetOrCompile(card);
                if (progSend == null) return false;
                var dummySend = false;
                foreach (var c in progSend.ClausesFor(EffectTiming.Activate))
                    ApplyClause(engine, who, card, c, null, ref dummySend, ref dummySend);
                if (monsterIgnition)
                {
                    if (progSend.ClausesFor(EffectTiming.Activate).Any(x => x.OncePerTurn))
                        card.EffectUsedThisTurn = true;
                }
                else
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, progSend));
                engine.NotifyPublic();
                return true;
            }

            if (p.AwaitingDiscardCost)
            {
                var discarded = MonsterEffects.DiscardFromHandByInstance(who, target);
                if (discarded == null)
                {
                    engine.Log("Discard cost failed.");
                    return false;
                }

                engine.Log($"Cost: discard {discarded.Name}.");
                var progCost = CompiledEffectCache.GetOrCompile(card);
                var bounce = progCost?.ClausesFor(EffectTiming.Activate)
                    .FirstOrDefault(c => c.RequiresTargetChoice);
                if (bounce == null)
                {
                    engine.ClearPendingActivation();
                    return false;
                }

                var bounceTargets = CollectTargets(engine, who, bounce, card);
                if (bounceTargets.Count == 0)
                {
                    engine.ClearPendingActivation();
                    engine.Log($"{card.Name}: no bounce target after cost.");
                    if (monsterIgnition)
                        card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                p.AwaitingDiscardCost = false;
                p.TargetKind = MapTargetKind(bounce);
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(bounceTargets);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var prog = CompiledEffectCache.GetOrCompile(card);
            var costNum = p.CostNumeric;
            var fromSummon = p.FromSummonWindow;
            engine.ClearPendingActivation();
            if (prog == null) return false;

            var dummy = false;
            var clauseSet = fromSummon
                ? prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon)
                : prog.ClausesFor(EffectTiming.Activate);
            foreach (var c in clauseSet)
            {
                if (c.RequiresTargetChoice)
                    ApplyClause(engine, who, card, c, target, ref dummy, ref dummy, costNum);
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy, costNum);
            }

            if (monsterIgnition)
            {
                if (prog.ClausesFor(EffectTiming.Activate).Any(x => x.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else if (!fromSummon)
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            NotifyTargetingEffectResolved(engine, target,
                clauseSet.Exists(c => c != null && c.IsPsctTarget));
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveFlip(DuelEngine engine, DuelistState who, CardInstance monster,
            CompiledCardProgram prog)
        {
            var flip = prog?.ClausesFor(EffectTiming.Flip);
            if (flip == null || flip.Count == 0) return false;

            // Patch seed/cache that dropped RequiresTargetChoice
            foreach (var c in flip)
            {
                if (c != null && NeedsTargetChoice(c))
                    c.RequiresTargetChoice = true;
            }

            engine.Log($"{monster.Name} Flip effect.");
            var targeted = flip.FirstOrDefault(c => c.RequiresTargetChoice || NeedsTargetChoice(c));
            if (targeted != null)
            {
                var targets = CollectTargets(engine, who, targeted, monster);
                if (targets.Count == 0)
                {
                    engine.Log($"{monster.Name} Flip: no legal targets.");
                    return true;
                }

                if (!who.IsPlayer)
                {
                    var remaining = new List<CardInstance>(targets);
                    var maxPicks = targeted.Amount > 1 ? targeted.Amount : 1;
                    var d = false;
                    var picked = new List<CardInstance>();
                    for (var i = 0; i < maxPicks && remaining.Count > 0; i++)
                    {
                        var pick = AutoPick(targeted, remaining, who, engine);
                        if (pick == null) break;
                        remaining.Remove(pick);
                        picked.Add(pick);
                        engine.Log($"{monster.Name} targets {pick.Name}.");
                        foreach (var c in flip)
                            ApplyClause(engine, who, monster, c,
                                c.RequiresTargetChoice ? pick : null, ref d, ref d);
                    }
                    NotifyTargetingEffectsResolved(engine, picked, targeted.IsPsctTarget);
                    engine.NotifyPublic();
                    return true;
                }

                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = monster,
                    FromHand = false,
                    WasSetOnField = false,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = targeted.Amount > 1 ? targeted.Amount : 1,
                    TargetPicksAreUpTo = targeted.Amount > 1
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var dummy = false;
            foreach (var c in flip)
                ApplyClause(engine, who, monster, c, null, ref dummy, ref dummy);
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveThisCardSummoned(DuelEngine engine, DuelistState who,
            CardInstance card, bool flipSummon = false, bool specialSummon = false)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = prog.ClausesFor(EffectTiming.ThisCardSummoned);
            if (clauses == null || clauses.Count == 0) return false;

            var applicable = new List<EffectClause>();
            foreach (var c in clauses)
            {
                if (c == null) continue;
                if (c.RequiresThisFlipSummoned && !flipSummon) continue;
                if (c.RequiresThisNormalSummoned && (flipSummon || specialSummon)) continue;
                if (c.RequiresThisTributeSummoned && (card == null || !card.WasTributeSummoned)) continue;
                applicable.Add(c);
            }
            if (applicable.Count == 0) return false;

            foreach (var c in applicable)
            {
                if (!c.RequiresTargetChoice && NeedsTargetChoice(c))
                    c.RequiresTargetChoice = true;
            }

            var targeted = applicable.FirstOrDefault(c =>
                c != null && (c.RequiresTargetChoice || NeedsTargetChoice(c)));
            if (targeted != null)
            {
                var targets = CollectTargets(engine, who, targeted, card);
                if (targets.Count == 0)
                {
                    engine.Log($"{card.Name}: no legal targets.");
                    var skip = false;
                    foreach (var c in applicable)
                    {
                        if (c.RequiresTargetChoice || NeedsTargetChoice(c)) continue;
                        ApplyClause(engine, who, card, c, null, ref skip, ref skip);
                    }
                    engine.NotifyPublic();
                    return true;
                }

                if (!who.IsPlayer)
                {
                    var pick = AutoPick(targeted, targets, who, engine);
                    var dAi = false;
                    foreach (var c in applicable)
                        ApplyClause(engine, who, card, c,
                            c.RequiresTargetChoice ? pick : null, ref dAi, ref dAi);
                    NotifyTargetingEffectResolved(engine, pick,
                        applicable.Exists(c => c != null && c.IsPsctTarget));
                    engine.NotifyPublic();
                    return true;
                }

                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = false,
                    WasSetOnField = false,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = true,
                    UsesTextProgram = true
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var d = false;
            foreach (var c in applicable)
                ApplyClause(engine, who, card, c, null, ref d, ref d);
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveSentToGy(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, bool destroyed = false, bool destroyedByBattle = false,
            CardInstance battleDestroyer = null)
        {
            if (prog == null) return false;
            var clauses = prog.ClausesFor(EffectTiming.SentFromFieldToGy);
            if (clauses == null || clauses.Count == 0) return false;

            // Fail closed: if any clause needs a target/search and nothing applied, return false
            // so registered scripts (Sangan, etc.) can still run.
            var handBefore = who?.HandCount ?? 0;
            var d = false;
            var appliedSomething = false;
            foreach (var c in clauses)
            {
                if (c == null) continue;
                if (c.RequiresDestroyed && !destroyed) continue;
                if (c.RequiresThisDestroyedByBattle && !destroyedByBattle) continue;
                if (c.Action == EffectActionKind.PlaceThisOnTopOfDeck)
                {
                    if (c.PayLpAmount > 0)
                    {
                        if (who.LifePoints < c.PayLpAmount)
                        {
                            if (c.IsOptional) continue;
                            engine.Log($"{card.Name}: not enough LP to return to the Deck.");
                            continue;
                        }

                        who.LifePoints -= c.PayLpAmount;
                        engine.Log($"Cost: pay {c.PayLpAmount} LP.");
                    }

                    if (c.RequiresTributeCount > 0)
                    {
                        var trib = CollectTributeCost(who, card, c);
                        if (trib.Count < c.RequiresTributeCount)
                        {
                            if (c.IsOptional) continue;
                            engine.Log($"{card.Name}: no monster to Tribute.");
                            continue;
                        }

                        if (who.IsPlayer)
                        {
                            var pending = new PendingActivation
                            {
                                Controller = who,
                                Card = card,
                                TargetKind = EffectTargetKind.TributeMonsterYouControl,
                                UsesTextProgram = true,
                                AwaitingIgnitionCost = true,
                                CostPicksRemaining = c.RequiresTributeCount
                            };
                            pending.LegalTargets.AddRange(trib);
                            engine.SetPendingActivation(pending);
                            engine.Log(pending.Prompt);
                            engine.NotifyPublic();
                            return true;
                        }

                        var pick = trib[0];
                        engine.SendCardToGrave(who, pick);
                        engine.Log($"Cost: Tribute {pick.Name}.");
                    }

                    ApplyClause(engine, who, card, c, null, ref d, ref d);
                    appliedSomething = true;
                    continue;
                }
                // Infer targeting when seed/cache dropped the flag
                if (!c.RequiresTargetChoice && NeedsTargetChoice(c))
                    c.RequiresTargetChoice = true;

                if (c.ImplicitTargetIsBattleDestroyer)
                {
                    ApplyClause(engine, who, card, c, battleDestroyer ?? card.BattleDestroyer,
                        ref d, ref d);
                    appliedSomething = true;
                    continue;
                }

                if (c.RequiresTargetChoice)
                {
                    var targets = CollectTargets(engine, who, c, card);
                    if (targets.Count == 0)
                    {
                        engine.Log($"{card?.Name}: no legal targets for field→GY clause.");
                        continue;
                    }

                    if (who != null && who.IsPlayer &&
                        (c.Action == EffectActionKind.Destroy ||
                         c.Action == EffectActionKind.ReturnToHand ||
                         c.Action == EffectActionKind.AddFromDeckToHand))
                    {
                        var pending = new PendingActivation
                        {
                            Controller = who,
                            Card = card,
                            FromHand = false,
                            WasSetOnField = false,
                            TargetKind = MapTargetKind(c),
                            IsMonsterEffect = true,
                            UsesTextProgram = true
                        };
                        pending.LegalTargets.AddRange(targets);
                        engine.SetPendingActivation(pending);
                        engine.Log(pending.Prompt);
                        engine.NotifyPublic();
                        return true;
                    }

                    var pick = AutoPick(c, targets, who, engine);
                    ApplyClause(engine, who, card, c, pick, ref d, ref d);
                    if (c.IsPsctTarget) NotifyTargetingEffectResolved(engine, pick, true);
                    appliedSomething = true;
                }
                else
                {
                    var h0 = who?.HandCount ?? 0;
                    ApplyClause(engine, who, card, c, null, ref d, ref d);
                    if ((who?.HandCount ?? 0) != h0)
                        appliedSomething = true;
                    else if (c.Action != EffectActionKind.AddFromDeckToHand)
                        appliedSomething = true; // non-search clauses count as applied
                }
            }

            if (causesDeckSearch(clauses) && (who?.HandCount ?? 0) <= handBefore && !appliedSomething)
                return false;

            return appliedSomething || !causesDeckSearch(clauses);
        }

        static bool causesDeckSearch(List<EffectClause> clauses)
        {
            if (clauses == null) return false;
            foreach (var c in clauses)
                if (c != null && c.Action == EffectActionKind.AddFromDeckToHand)
                    return true;
            return false;
        }

        /// <summary>Actions that always need a player/AI target even if seed omitted the flag.</summary>
        public static bool NeedsTargetChoice(EffectClause c)
        {
            if (c == null) return false;
            if (c.ImplicitTargetIsBattleDestroyer) return false;
            if (c.ResolvesFromGy && c.Action == EffectActionKind.SpecialSummonFromGy) return false;
            if (c.RequiresTargetChoice) return true;
            return c.Action switch
            {
                EffectActionKind.AddFromGyToHand => true,
                EffectActionKind.AddFromDeckToHand => true,
                EffectActionKind.SpecialSummonFromGy => true,
                EffectActionKind.SpecialSummonFromDeck => true,
                EffectActionKind.ChangeBattlePosition => c.Side != EffectSide.Both,
                EffectActionKind.EffectDamageBothFromOriginalAtk => true,
                EffectActionKind.Destroy when c.Zone is EffectZoneFilter.FieldAnyMonster
                    or EffectZoneFilter.OppFaceUpMonsters => true,
                EffectActionKind.Destroy when c.Zone is EffectZoneFilter.FieldSpellTraps &&
                                              c.Side != EffectSide.Both => true,
                EffectActionKind.ReturnToHand when c.Zone is EffectZoneFilter.FieldMonsters => false,
                EffectActionKind.ReturnToHand => true,
                EffectActionKind.Banish when c.Zone is EffectZoneFilter.EitherGyMonsters
                    or EffectZoneFilter.OppGyMonsters
                    or EffectZoneFilter.FieldAnyMonster
                    or EffectZoneFilter.OppFaceUpMonsters => true,
                _ => false
            };
        }

        public static bool TryResolveMonsterTextTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || !p.IsMonsterEffect || target == null)
                return false;

            if (p.AwaitingDiscardCost || p.AwaitingSendNamedCost || p.AwaitingIgnitionCost ||
                p.AwaitingEffectTribute ||
                p.AwaitingDarkSage || p.AwaitingVirusDeck || p.AwaitingHatsMonster ||
                p.AwaitingHatsDeck)
                return TryResolveTextTarget(engine, target);
            if (p.UsesTextProgram)
            {
                var ignProg = CompiledEffectCache.GetOrCompile(p.Card);
                // Only steal the window for ignition targeting. Swarm of Scarabs etc. also
                // have a no-target OPT set-FD ignition plus a Flip-Summon target trigger.
                if (ignProg != null &&
                    ignProg.ClausesFor(EffectTiming.Activate).Exists(c =>
                        c != null && c.RequiresTargetChoice))
                    return TryResolveTextTarget(engine, target);
            }

            var resolved = engine.ResolveLegalEffectTarget(target);
            if (resolved == null) return false;
            target = resolved;

            var who = p.Controller;
            var card = p.Card;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null) return false;

            if (!p.ChosenTargets.Contains(target))
                p.ChosenTargets.Add(target);
            p.LegalTargets.Remove(target);
            if (p.TargetPicksRemaining > 0)
                p.TargetPicksRemaining--;
            if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
            {
                engine.Log($"{card.Name}: choose another target ({p.TargetPicksRemaining} remaining).");
                engine.NotifyPublic();
                return true;
            }

            var picks = p.ChosenTargets.Count > 0
                ? p.ChosenTargets.ToList()
                : new List<CardInstance> { target };
            engine.ClearPendingActivation();

            var d = false;
            var flip = prog.ClausesFor(EffectTiming.Flip);
            var gy = prog.ClausesFor(EffectTiming.SentFromFieldToGy);
            var summoned = prog.ClausesFor(EffectTiming.ThisCardSummoned);
            List<EffectClause> clauses;
            if (flip.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = flip;
            else if (summoned.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = summoned;
            else if (gy.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = gy;
            else
                clauses = flip.Count > 0 ? flip : (summoned.Count > 0 ? summoned : gy);
            foreach (var pick in picks)
            {
                foreach (var c in clauses)
                {
                    if (c.RequiresTargetChoice)
                        ApplyClause(engine, who, card, c, pick, ref d, ref d);
                    else if (pick == picks[0])
                        ApplyClause(engine, who, card, c, null, ref d, ref d);
                }
            }

            NotifyTargetingEffectsResolved(engine, picks,
                clauses.Exists(c => c != null && c.IsPsctTarget));
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// "Up to N" targeting: cancel after at least one pick resolves the chosen set.
        /// </summary>
        public static bool TryFinishUpToTargets(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || !p.TargetPicksAreUpTo ||
                p.ChosenTargets == null || p.ChosenTargets.Count == 0)
                return false;
            var last = p.ChosenTargets[p.ChosenTargets.Count - 1];
            if (!p.LegalTargets.Contains(last))
                p.LegalTargets.Add(last);
            p.TargetPicksRemaining = 0;
            if (p.IsMonsterEffect)
                return TryResolveMonsterTextTarget(engine, last);
            return TryResolveTextTarget(engine, last);
        }

        static bool StartOrPayDiscardThenTarget(
            DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> activate, EffectClause targeted, bool autoPick, bool monsterIgnition)
        {
            var costCards = CollectDiscardCost(who, targeted.DiscardCostAttribute, card);
            var bounceTargets = CollectTargets(engine, who, targeted, card);
            if (costCards.Count == 0 ||
                (bounceTargets.Count == 0 && !costCards.Exists(c => WouldBeLegalGyTarget(targeted, c))))
            {
                engine.Log($"{card.Name}: missing cost or target — activation fails.");
                if (!monsterIgnition)
                    FinishSpellTrap(engine, who, card, stays: false);
                engine.NotifyPublic();
                return true;
            }

            if (autoPick || !who.IsPlayer)
            {
                var cost = bounceTargets.Count == 0
                    ? costCards.FirstOrDefault(c => WouldBeLegalGyTarget(targeted, c)) ?? costCards[0]
                    : costCards.OrderBy(c => c.CurrentAtk).First();
                var discarded = MonsterEffects.DiscardFromHandByInstance(who, cost);
                if (discarded == null) return false;
                engine.Log($"Cost: discard {discarded.Name}.");
                bounceTargets = CollectTargets(engine, who, targeted, card);
                if (bounceTargets.Count == 0)
                {
                    if (monsterIgnition)
                        card.EffectUsedThisTurn = targeted.OncePerTurn;
                    engine.NotifyPublic();
                    return true;
                }

                var pick = AutoPick(targeted, bounceTargets, who, engine);
                var dummy = false;
                foreach (var c in activate)
                {
                    if (c.RequiresTargetChoice)
                        ApplyClause(engine, who, card, c, pick, ref dummy, ref dummy);
                    else
                        ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                }

                if (monsterIgnition)
                    card.EffectUsedThisTurn = targeted.OncePerTurn || card.EffectUsedThisTurn;
                else
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, null));
                NotifyTargetingEffectResolved(engine, pick, targeted.IsPsctTarget);
                engine.NotifyPublic();
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.DiscardMonsterInHand,
                IsMonsterEffect = monsterIgnition,
                UsesTextProgram = true,
                AwaitingDiscardCost = true,
                DiscardCostAttribute = targeted.DiscardCostAttribute
            };
            pending.LegalTargets.AddRange(costCards);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static int DiscardNeed(EffectClause c) =>
            c == null ? 1 : (c.DiscardCostCount > 0 ? c.DiscardCostCount : 1);

        static List<CardInstance> CollectDiscardCost(DuelistState who, string attribute,
            CardInstance except = null)
        {
            var list = new List<CardInstance>();
            if (who?.Hand == null) return list;
            var anyCard = attribute == "*";
            foreach (var c in who.Hand)
            {
                if (c == null || c == except || c.Def == null) continue;
                if (anyCard)
                {
                    list.Add(c);
                    continue;
                }

                if (!c.Def.IsMonster) continue;
                if (!string.IsNullOrEmpty(attribute) &&
                    (c.Def.attribute == null ||
                     !c.Def.attribute.Equals(attribute, System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(c);
            }

            return list;
        }

        // Reborn/CotH/Premature-style GY paths stay closed for hard Nomi cards.
        // Procedure-aware Ritual/Fusion paths use SpecialSummonToField with a
        // registered SummonKind; ordinary compiled GY effects remain gated here.
        // TODO: track WasProperlySpecialSummoned before implementing Semi-Nomi
        // revival after a card has successfully completed its proper procedure.
        static bool CanSpecialSummonFromGyTarget(CardInstance card)
        {
            if (card?.Def == null) return false;
            var text = card.OfficialText;
            return !PsctGrammar.CannotBeSpecialSummonedFromGraveyard(text) &&
                   !PsctGrammar.BlocksSpecialSummon(text);
        }

        static bool HasLegalTargetsConsideringDiscard(DuelEngine engine, DuelistState who,
            EffectClause c, CardInstance except)
        {
            if (CollectTargets(engine, who, c, except).Count > 0) return true;
            if (c == null || !c.RequiresDiscardCost) return false;
            foreach (var cost in CollectDiscardCost(who, c.DiscardCostAttribute, except))
            {
                if (WouldBeLegalGyTarget(c, cost)) return true;
            }

            return false;
        }

        static bool WouldBeLegalGyTarget(EffectClause c, CardInstance card)
        {
            if (c == null || card?.Def == null) return false;
            if (c.Zone == EffectZoneFilter.ControllerGySpells) return card.Def.IsSpell;
            if (c.Zone == EffectZoneFilter.ControllerGyTraps) return card.Def.IsTrap;
            if (c.Zone != EffectZoneFilter.ControllerGyMonsters &&
                c.Zone != EffectZoneFilter.EitherGyMonsters)
                return false;
            if (!card.Def.IsMonster || card.Def.IsExtraDeck) return false;
            if (!string.IsNullOrEmpty(c.RaceFilter) &&
                (card.Def.race == null ||
                 card.Def.race.IndexOf(c.RaceFilter, System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            return true;
        }

        /// <summary>
        /// Face-up cards you control whose rules name matches (ALO is Umi; Maiden is not).
        /// </summary>
        static List<CardInstance> CollectSendNamedCost(DuelistState who, string name)
        {
            var list = new List<CardInstance>();
            if (who == null || string.IsNullOrEmpty(name)) return list;

            void Consider(CardInstance c)
            {
                if (c == null || !c.FaceUp || !c.IsNamed(name)) return;
                if (!list.Contains(c)) list.Add(c);
            }

            foreach (var st in who.SpellTrapsOnField())
                Consider(st);
            foreach (var m in who.MonstersOnField())
                Consider(m);
            if (who.PendulumZones != null)
            {
                foreach (var z in who.PendulumZones)
                    Consider(z?.Occupant);
            }

            return list;
        }

        static bool PaySendNamedCost(DuelEngine engine, DuelistState who, CardInstance cost)
        {
            if (engine == null || who == null || cost == null) return false;
            var owner = engine.ControllerOf(cost);
            if (owner != who)
            {
                engine.Log("Send-to-GY cost: you must control that card.");
                return false;
            }

            engine.SendCardToGrave(owner, cost);
            engine.Log($"Cost: send {cost.Name} to the GY.");
            return true;
        }

        static bool StartOrPaySendNamedThenResolve(
            DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> activate, EffectClause sendClause, bool autoPick,
            bool monsterIgnition)
        {
            var costs = CollectSendNamedCost(who, sendClause.RequiresFaceUpName);
            if (costs.Count == 0)
            {
                engine.Log($"{card.Name}: no face-up \"{sendClause.RequiresFaceUpName}\" to send — activation fails.");
                if (!monsterIgnition)
                    FinishSpellTrap(engine, who, card, stays: false);
                engine.NotifyPublic();
                return true;
            }

            if (autoPick || !who.IsPlayer)
            {
                var pick = costs.FirstOrDefault(c =>
                               who.FieldSpellZone != null && who.FieldSpellZone.Occupant == c)
                           ?? costs[0];
                if (!PaySendNamedCost(engine, who, pick))
                    return false;
                var dummy = false;
                foreach (var c in activate)
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                if (monsterIgnition)
                    card.EffectUsedThisTurn = sendClause.OncePerTurn || card.EffectUsedThisTurn;
                else
                    FinishSpellTrap(engine, who, card,
                        stays: activate.Any(c => c.StaysOnField));
                engine.NotifyPublic();
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = false,
                TargetKind = EffectTargetKind.SendFaceUpNamedToGy,
                IsMonsterEffect = monsterIgnition,
                UsesTextProgram = true,
                AwaitingSendNamedCost = true,
                SendNamedCost = sendClause.RequiresFaceUpName
            };
            pending.LegalTargets.AddRange(costs);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool HasStructuredIgnitionCost(EffectClause c) =>
            c != null &&
            (c.RequiresTributeThis || c.RequiresTributeCount > 0 || c.RequiresDiscardSelf ||
             c.RequiresSendThisToGy || c.RequiresSendHandToGy || c.RequiresSendOtherYouControl ||
             c.PayLpAmount > 0 || c.BanishFromGyCount > 0 ||
             c.RequiresRemoveSpellCounters > 0 ||
             (c.RequiresDiscardCost && !c.RequiresTargetChoice));

        static bool IgnitionCostLegal(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out string reason)
        {
            reason = "OK";
            if (c == null) return true;
            if (c.PayLpAmount > 0 && who.LifePoints < c.PayLpAmount)
            {
                reason = $"Not enough LP to pay {c.PayLpAmount}.";
                return false;
            }

            if (c.RequiresTributeThis)
            {
                if (!who.TryFindMonster(card, out _))
                {
                    reason = "Must Tribute this card on the field.";
                    return false;
                }
            }

            if (c.RequiresTributeCount > 0 &&
                CollectTributeCost(who, card, c).Count < (c.TributeUpTo ? 1 : c.RequiresTributeCount))
            {
                reason = "Not enough monsters to Tribute.";
                return false;
            }

            if (c.RequiresSendThisToGy && !who.TryFindMonster(card, out _) &&
                who.FieldSpellZone?.Occupant != card && !who.TryFindSpellTrap(card, out _))
            {
                reason = "This card is not on the field to send to the GY.";
                return false;
            }

            if (c.RequiresDiscardCost &&
                CollectDiscardCost(who, c.DiscardCostAttribute, card).Count < DiscardNeed(c))
            {
                reason = "Not enough cards in hand to discard.";
                return false;
            }

            if (c.RequiresSendHandToGy &&
                CollectDiscardCost(who, "", card).Count == 0)
            {
                reason = "No monster in hand to send to the GY.";
                return false;
            }

            if (c.RequiresSendOtherYouControl &&
                CollectOtherYouControl(who, card, c.AttributeFilter).Count == 0)
            {
                reason = "No other matching monster you control to send.";
                return false;
            }

            if (c.BanishFromGyCount > 0)
            {
                var n = CollectBanishGy(engine, who, c, card).Count;
                if (n < (c.BanishFromGyUpTo ? 1 : c.BanishFromGyCount))
                {
                    reason = "Not enough monsters in GY to banish.";
                    return false;
                }
            }

            if (c.Action == EffectActionKind.SpecialSummonNamed ||
                c.Action == EffectActionKind.SpecialSummonFromGy ||
                c.Action == EffectActionKind.SpecialSummonFromHand ||
                c.Action == EffectActionKind.SpecialSummonFromDeck)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0 && !c.RequiresTributeThis &&
                    !c.RequiresSendThisToGy)
                {
                    reason = "No free Monster Zone.";
                    return false;
                }
            }

            if (c.Action == EffectActionKind.SpecialSummonNamed &&
                !HasNamedInListedOrigins(engine, who, c))
            {
                reason = string.IsNullOrEmpty(c.AltNamedCard)
                    ? $"No \"{c.NamedCard}\" in the listed locations."
                    : $"No \"{c.NamedCard}\" or \"{c.AltNamedCard}\" in the listed locations.";
                return false;
            }

            if (c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                !DeckHasNamed(engine, who, c.NamedCard, c.NamedCardIsSeries) &&
                (string.IsNullOrEmpty(c.AltNamedCard) ||
                 !DeckHasNamed(engine, who, c.AltNamedCard, false)))
            {
                reason = $"No \"{c.NamedCard}\" left in Deck.";
                return false;
            }

            if (c.Action == EffectActionKind.SpecialSummonFusionFromExtra &&
                CollectExtraFusions(engine, who).Count == 0)
            {
                reason = "No Fusion Monster in the Extra Deck.";
                return false;
            }

            if (c.Action == EffectActionKind.TakeControlLevelLeq)
            {
                var opp = engine.OpponentOf(who);
                var n = 0;
                if (opp != null)
                    foreach (var m in opp.MonstersOnField())
                        if (m != null && m.FaceUp && m.Level <= (c.Amount > 0 ? c.Amount : 3))
                            n++;
                if (n == 0)
                {
                    reason = "No legal monsters to take control of.";
                    return false;
                }
            }

            return true;
        }

        static List<CardInstance> CollectTributeCost(DuelistState who, CardInstance source,
            EffectClause c)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null) continue;
                if (c.TributeFaceUpOnly && !m.FaceUp) continue;
                if (c.TributeExceptThis && m == source) continue;
                if (!string.IsNullOrEmpty(c.TributeRaceFilter) &&
                    (m.Def?.race == null ||
                     m.Def.race.IndexOf(c.TributeRaceFilter,
                         System.StringComparison.OrdinalIgnoreCase) < 0) &&
                    (m.Name == null ||
                     m.Name.IndexOf(c.TributeRaceFilter, System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (!string.IsNullOrEmpty(c.NamedCard) && !m.IsNamed(c.NamedCard) &&
                    (m.Name == null ||
                     m.Name.IndexOf(c.NamedCard, System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (c.RequiresAtkLeqCost && c.Amount > 0 && m.CurrentAtk > c.Amount)
                    continue;
                if (c.RequiresAtkGeqCost && c.Amount > 0 && m.CurrentAtk < c.Amount)
                    continue;
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    (m.Def?.attribute == null ||
                     !m.Def.attribute.Equals(c.AttributeFilter, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(m);
            }

            return list;
        }

        static List<CardInstance> CollectOtherYouControl(DuelistState who, CardInstance source,
            string attribute)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || m == source || !m.FaceUp) continue;
                if (!string.IsNullOrEmpty(attribute) &&
                    (m.Def?.attribute == null ||
                     !m.Def.attribute.Equals(attribute, System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(m);
            }

            return list;
        }

        static List<CardInstance> CollectBanishGy(DuelEngine engine, DuelistState who, EffectClause c, CardInstance source = null)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
            if (engine != null && ContinuousProtections.CannotBanishFromGraveyard(engine))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    (g.Def.attribute == null ||
                     !g.Def.attribute.Equals(c.AttributeFilter,
                         System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g))
                    continue;
                list.Add(g);
            }

            return list;
        }

        static List<CardInstance> CollectIgnitionCostCards(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause c)
        {
            if (c == null) return new List<CardInstance>();
            if (c.RequiresTributeCount > 0)
                return CollectTributeCost(who, card, c);
            if (c.RequiresSendHandToGy)
                return CollectDiscardCost(who, "", card);
            if (c.RequiresSendOtherYouControl)
                return CollectOtherYouControl(who, card, c.AttributeFilter);
            if (c.BanishFromGyCount > 0)
                return CollectBanishGy(engine, who, c, card);
            if (c.RequiresDiscardCost)
                return CollectDiscardCost(who, c.DiscardCostAttribute, card);
            return new List<CardInstance>();
        }

        static EffectTargetKind IgnitionCostTargetKind(EffectClause c)
        {
            if (c == null) return EffectTargetKind.None;
            if (c.RequiresTributeCount > 0) return EffectTargetKind.TributeMonsterYouControl;
            if (c.BanishFromGyCount > 0) return EffectTargetKind.BanishFromYourGy;
            if (c.RequiresSendOtherYouControl) return EffectTargetKind.SendMonsterYouControlToGy;
            if (c.RequiresSendHandToGy || c.RequiresDiscardCost)
                return EffectTargetKind.DiscardMonsterInHand;
            return EffectTargetKind.None;
        }

        static bool PayOneIgnitionCost(DuelEngine engine, DuelistState who, CardInstance source,
            PendingActivation pending, CardInstance pick)
        {
            if (pick == null) return false;
            var resolved = engine.ResolveLegalEffectTarget(pick) ?? pick;
            if (pending.TargetKind == EffectTargetKind.BanishFromYourGy)
            {
                if (ContinuousProtections.GyMovementLocked(engine, source, resolved))
                {
                    engine.Log($"Necrovalley prevents {resolved.Name} from leaving the GY.");
                    return false;
                }
                engine.BanishCard(who, resolved);
                pending.CostNumeric++;
                engine.Log($"Cost: banish {resolved.Name} from GY.");
                return true;
            }

            if (pending.TargetKind == EffectTargetKind.DiscardMonsterInHand)
            {
                var discarded = MonsterEffects.DiscardFromHandByInstance(who, resolved);
                if (discarded == null) return false;
                engine.Log($"Cost: send {discarded.Name} to the GY.");
                pending.CostNumeric = discarded.CurrentAtk;
                return true;
            }

            var owner = engine.ControllerOf(resolved) ?? who;
            var atk = resolved.CurrentAtk;
            engine.SendCardToGrave(owner, resolved);
            engine.Log($"Cost: Tribute/send {resolved.Name} to the GY.");
            pending.CostNumeric = atk;
            return true;
        }

        static bool PayAutoIgnitionCosts(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, bool autoPick, out int costNumeric)
        {
            costNumeric = 0;
            if (c.PayLpAmount > 0)
            {
                if (who.LifePoints < c.PayLpAmount) return false;
                who.LifePoints -= c.PayLpAmount;
                engine.Log($"Cost: pay {c.PayLpAmount} LP → {who.Name} at {who.LifePoints} LP.");
            }

            if (c.RequiresRemoveSpellCounters > 0)
            {
                if (card.Counters < c.RequiresRemoveSpellCounters) return false;
                card.Counters -= c.RequiresRemoveSpellCounters;
                engine.Log($"Cost: remove {c.RequiresRemoveSpellCounters} Spell Counter(s) (now {card.Counters}).");
            }

            if (c.RequiresDiscardSelf)
            {
                var discarded = MonsterEffects.DiscardFromHandByInstance(who, card);
                if (discarded == null) return false;
                engine.Log($"Cost: discard {discarded.Name}.");
            }

            if (c.RequiresTributeThis || c.RequiresSendThisToGy)
            {
                costNumeric = card.CurrentAtk;
                var owner = engine.ControllerOf(card) ?? who;
                engine.SendCardToGrave(owner, card);
                engine.Log($"Cost: Tribute/send {card.Name} to the GY.");
            }

            if (c.BanishFromGyCount > 0 && autoPick)
            {
                var gy = CollectBanishGy(engine, who, c, card);
                var n = c.BanishFromGyUpTo
                    ? Mathf.Min(c.BanishFromGyCount, gy.Count)
                    : c.BanishFromGyCount;
                var paid = 0;
                for (var i = 0; i < n && i < gy.Count; i++)
                {
                    if (ContinuousProtections.GyMovementLocked(engine, card, gy[i]))
                    {
                        engine.Log($"Necrovalley prevents {gy[i].Name} from leaving the GY.");
                        continue;
                    }
                    engine.BanishCard(who, gy[i]);
                    paid++;
                }
                costNumeric = paid;
            }

            if (c.RequiresTributeCount > 0 && autoPick)
            {
                var trib = CollectTributeCost(who, card, c);
                var n = c.TributeUpTo
                    ? Mathf.Min(c.RequiresTributeCount, trib.Count)
                    : c.RequiresTributeCount;
                var atk = 0;
                for (var i = 0; i < n && i < trib.Count; i++)
                {
                    atk = trib[i].CurrentAtk;
                    engine.SendCardToGrave(who, trib[i]);
                    engine.Log($"Cost: Tribute {trib[i].Name}.");
                }

                costNumeric = c.ScaleAmountByCostCount ? n : atk;
            }

            if (c.RequiresSendHandToGy && autoPick)
            {
                var hand = CollectDiscardCost(who, "", card);
                if (hand.Count == 0) return false;
                var pick = hand[0];
                costNumeric = pick.CurrentAtk;
                MonsterEffects.DiscardFromHandByInstance(who, pick);
                engine.Log($"Cost: send {pick.Name} from hand to the GY.");
            }

            if (c.RequiresSendOtherYouControl && autoPick)
            {
                var others = CollectOtherYouControl(who, card, c.AttributeFilter);
                if (others.Count == 0) return false;
                engine.SendCardToGrave(who, others[0]);
                engine.Log($"Cost: send {others[0].Name} to the GY.");
            }

            if (c.RequiresDiscardCost && autoPick)
            {
                var hand = CollectDiscardCost(who, c.DiscardCostAttribute, card);
                var n = DiscardNeed(c);
                if (hand.Count < n) return false;
                for (var i = 0; i < n; i++)
                {
                    var pick = hand[i];
                    var discarded = MonsterEffects.DiscardFromHandByInstance(who, pick);
                    if (discarded == null) return false;
                    engine.Log($"Cost: discard {discarded.Name}.");
                }
            }

            return true;
        }

        static bool StartIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, List<EffectClause> activate, EffectClause clause, bool autoPick,
            bool isMonster, CardInstance forcedTarget = null)
        {
            var chosen = CollectIgnitionCostCards(engine, who, card, clause);
            var needsPick = chosen.Count > 0 &&
                            (clause.RequiresTributeCount > 0 || clause.RequiresSendHandToGy ||
                             clause.RequiresSendOtherYouControl ||
                             (clause.BanishFromGyCount > 0 && !autoPick && who.IsPlayer));
            if (autoPick || !who.IsPlayer || !needsPick)
            {
                if (!PayAutoIgnitionCosts(engine, who, card, clause, autoPick: true,
                        out var costNumeric))
                    return false;
                return FinishIgnitionAfterCost(engine, who, card, fromHand, isMonster, costNumeric,
                    autoPick, forcedTarget);
            }

            if (!PayAutoIgnitionCosts(engine, who, card, clause, autoPick: false, out _))
                return false;

            var n = clause.RequiresTributeCount > 0
                ? (clause.TributeUpTo ? 1 : clause.RequiresTributeCount)
                : clause.BanishFromGyCount > 0
                    ? (clause.BanishFromGyUpTo ? 1 : clause.BanishFromGyCount)
                    : 1;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = false,
                TargetKind = IgnitionCostTargetKind(clause),
                IsMonsterEffect = isMonster,
                UsesTextProgram = true,
                AwaitingIgnitionCost = true,
                CostPicksRemaining = n
            };
            pending.LegalTargets.AddRange(chosen);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool FinishIgnitionAfterCost(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, bool isMonster, int costNumeric, bool autoPick = false,
            CardInstance forcedTarget = null)
        {
            var prog = CompiledEffectCache.GetOrCompile(card);
            var activate = prog?.ClausesFor(EffectTiming.Activate);
            if (activate == null || activate.Count == 0) return false;
            var clause = activate.FirstOrDefault(c => c.RequiresTargetChoice) ?? activate[0];

            if (clause.RequiresTargetChoice)
            {
                var targets = CollectTargets(engine, who, clause, card, costNumeric);
                if (forcedTarget != null && !targets.Contains(forcedTarget))
                    targets.Insert(0, forcedTarget);
                if (targets.Count == 0)
                {
                    engine.Log($"{card.Name}: no legal target after cost.");
                    if (isMonster && activate.Any(c => c.OncePerTurn))
                        card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                if (forcedTarget != null || autoPick || !who.IsPlayer)
                {
                    var pick = forcedTarget ?? AutoPick(clause, targets, who, engine);
                    ApplyIgnitionClauses(engine, who, card, activate, pick, costNumeric, isMonster);
                    if (!isMonster)
                        FinishSpellTrap(engine, who, card,
                            stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    engine.NotifyPublic();
                    return true;
                }

                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    WasSetOnField = false,
                    TargetKind = MapTargetKind(clause),
                    IsMonsterEffect = isMonster,
                    UsesTextProgram = true,
                    CostNumeric = costNumeric,
                    TargetPicksRemaining = clause.ControllerTargetCount > 0
                        ? clause.ControllerTargetCount
                        : (clause.Amount > 1 ? clause.Amount : 1),
                    TargetPicksAreUpTo = clause.Amount > 1 && clause.ControllerTargetCount <= 0
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            ApplyIgnitionClauses(engine, who, card, activate, null, costNumeric, isMonster);
            if (!isMonster)
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            engine.NotifyPublic();
            return true;
        }

        static void ApplyIgnitionClauses(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> activate, CardInstance target, int costNumeric, bool isMonster)
        {
            var dummy = false;
            foreach (var c in activate)
            {
                if (c.RequiresAtkLeqCost)
                    c.Amount = costNumeric;
                ApplyClause(engine, who, card, c, c.RequiresTargetChoice ? target : null,
                    ref dummy, ref dummy, costNumeric);
            }

            if (isMonster && activate.Any(x => x.OncePerTurn) && card != null)
                card.EffectUsedThisTurn = true;
            NotifyTargetingEffectResolved(engine, target,
                activate.Exists(c => c != null && c.IsPsctTarget));
        }

        static bool HasNamedInListedOrigins(DuelEngine engine, DuelistState who, EffectClause c)
        {
            if (engine == null || who == null || c == null) return false;
            // No origin flags yet → do not hard-block (legacy named SS clauses).
            if (!c.FromHand && !c.FromDeck && !c.FromGrave) return true;
            bool Match(CardDef def) =>
                def != null && DefMatchesSummonFilter(def, c);

            if (c.FromHand && who.Hand != null &&
                who.Hand.Any(x => x?.Def != null && Match(x.Def)))
                return true;
            if (c.FromDeck && who.Deck != null && engine.Database != null)
            {
                foreach (var id in who.Deck)
                {
                    if (engine.Database.TryGet(id, out var def) && Match(def))
                        return true;
                }
            }
            if (c.FromGrave && who.Graveyard != null &&
                who.Graveyard.Any(x => x?.Def != null && Match(x.Def)))
                return true;
            return false;
        }

        static bool DeckHasNamed(DuelEngine engine, DuelistState who, string name,
            bool series = false)
        {
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(name))
                return false;
            for (var i = 0; i < who.Deck.Count; i++)
            {
                if (!engine.Database.TryGet(who.Deck[i], out var def) || def == null) continue;
                if (NameMatchesQuoted(def, name, series))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Exact quoted name, or series ("Toon" card) via name/archetype contains.
        /// Spells included — Toon Table is not a monster-only search.
        /// </summary>
        static bool NameMatchesQuoted(CardDef def, string name, bool series)
        {
            if (def == null || string.IsNullOrEmpty(name)) return false;
            if (!series)
                return string.Equals(def.name, name, System.StringComparison.OrdinalIgnoreCase);
            var inName = def.name != null &&
                         def.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0;
            var inArch = def.archetype != null &&
                         def.archetype.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0;
            return inName || inArch;
        }

        static List<CardInstance> CollectAllOtherCards(DuelEngine engine, DuelistState who,
            CardInstance except)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null) return list;
            var opp = engine.OpponentOf(who);

            void Add(CardInstance c)
            {
                if (c == null || c == except) return;
                if (!list.Contains(c)) list.Add(c);
            }

            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    Add(m);
                foreach (var st in side.SpellTrapsOnField())
                    Add(st);
                if (side.PendulumZones == null) continue;
                foreach (var z in side.PendulumZones)
                    Add(z?.Occupant);
            }

            return list;
        }

        public static bool TryFinishLpCost(DuelEngine engine, int amount)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingLpCost || !p.UsesTextProgram) return false;
            if (!p.LpCostChoices.Contains(amount)) return false;
            var who = p.Controller;
            var card = p.Card;
            var target = p.LockedTarget;
            engine.ClearPendingActivation();
            who.LifePoints = Mathf.Max(0, who.LifePoints - amount);
            engine.Log($"Cost: pay {amount} LP → {who.Name} at {who.LifePoints} LP.");
            if (target != null)
            {
                if (ContinuousProtections.TargetedEffectBlocked(engine, target, card))
                {
                    engine.Log("Target is protected at resolution.");
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }
                target.UntilEndOfTurnAtk -= amount;
                if (p.AlsoAffectsDef) target.UntilEndOfTurnDef -= amount;
                engine.Log(
                    $"{target.Name} loses {amount} ATK" +
                    (p.AlsoAffectsDef ? $"/{amount} DEF" : "") +
                    $" until the End Phase (now {target.CurrentAtk}/{target.CurrentDef}).");
            }

            FinishSpellTrap(engine, who, card, stays: false);
            engine.ContinueAfterResponseActivation(false, false);
            return true;
        }

        static bool DamageStepClausesLegal(DuelEngine engine, DuelistState who,
            CardInstance card, List<EffectClause> clauses)
        {
            foreach (var c in clauses)
            {
                if (c == null) continue;
                if (!string.IsNullOrEmpty(c.RaceFilter) &&
                    !YgoProTriggerCatalog.ControllerHasBattlingRace(engine, who, c.RaceFilter))
                    return false;
                if (c.RequiresLpCostMultiple > 0 && who.LifePoints < c.RequiresLpCostMultiple)
                    return false;
                var oppMon = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                if (c.Zone == EffectZoneFilter.OpponentBattlingMonster &&
                    (oppMon == null || !oppMon.FaceUp))
                    return false;
                if (c.OpponentTurnOnly && engine.TurnPlayer == who)
                    return false;
                if (c.RequiresThisIsAttackTarget)
                {
                    var tgt = engine.PendingResponse?.AttackTarget;
                    if (tgt == null || card == null ||
                        (tgt != card && tgt.InstanceId != card.InstanceId))
                        return false;
                }

                if (c.OnceWhileFaceUp && card != null && card.EffectUsedWhileFaceUp)
                    return false;
            }

            return true;
        }

        static bool ResolveDamageStepClauses(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, List<EffectClause> clauses, bool autoPickTarget)
        {
            var fieldMonster = card?.Def != null && card.Def.IsMonster &&
                               who != null && who.TryFindMonster(card, out _);
            if (!fieldMonster)
                PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");
            var payClause = clauses.Find(c => c != null && c.RequiresLpCostMultiple > 0);
            if (payClause != null)
            {
                var target = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                var choices = YgoProTriggerCatalog.LpChoices(who, target, payClause.RequiresLpCostMultiple);
                if (choices.Count == 0) return false;
                if (autoPickTarget || !who.IsPlayer)
                {
                    var amount = choices[choices.Count - 1];
                    who.LifePoints = Mathf.Max(0, who.LifePoints - amount);
                    engine.Log($"Cost: pay {amount} LP → {who.Name} at {who.LifePoints} LP.");
                    if (target != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, target, card))
                        {
                            engine.Log("Target is protected at resolution.");
                        }
                        else
                        {
                            target.UntilEndOfTurnAtk -= amount;
                            if (payClause.DefAmount != 0)
                                target.UntilEndOfTurnDef -= amount;
                            engine.Log(
                                $"{target.Name} loses {amount} ATK" +
                                (payClause.DefAmount != 0 ? $"/{amount} DEF" : "") +
                                $" until the End Phase (now {target.CurrentAtk}/{target.CurrentDef}).");
                        }
                    }

                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }

                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    WasSetOnField = true,
                    UsesTextProgram = true,
                    AwaitingLpCost = true,
                    ResumeDamageCalculation = true,
                    LockedTarget = target,
                    AlsoAffectsDef = payClause.DefAmount != 0
                };
                pending.LpCostChoices.AddRange(choices);
                engine.ClearPendingResponse();
                engine.SetPendingActivation(pending);
                engine.Log($"{card.Name}: pay LP in multiples of {payClause.RequiresLpCostMultiple} (cost).");
                engine.NotifyPublic();
                return true;
            }

            var dummy = false;
            engine.TryGetBattlingPair(out var attackingMon, out _);
            foreach (var c in clauses)
            {
                var tgt = c.Zone == EffectZoneFilter.AttackingMonster
                    ? attackingMon
                    : YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                ApplyClause(engine, who, card, c, tgt, ref dummy, ref dummy);
            }

            if (!fieldMonster)
                FinishSpellTrap(engine, who, card, stays: false);
            engine.ContinueAfterResponseActivation(false, false);
            return true;
        }

        /// <summary>Apply an effect LP gain, replacing opponent gains under a face-up clause.</summary>
        public static void ApplyLifePointGain(DuelEngine engine, DuelistState gaining, int amount,
            string sourceName = null)
        {
            if (engine == null || gaining == null || amount <= 0) return;
            if (OpponentLpGainIsConverted(engine, gaining))
            {
                engine.ApplyEffectDamage(gaining, amount, sourceName);
                return;
            }

            gaining.LifePoints += amount;
            engine.Log($"{gaining.Name} gains {amount} LP ({gaining.LifePoints}).");
        }

        static bool OpponentLpGainIsConverted(DuelEngine engine, DuelistState gaining)
        {
            if (engine == null || gaining == null) return false;
            foreach (var controller in new[] { engine.Player, engine.Opponent })
            {
                if (controller == null || engine.OpponentOf(controller) != gaining) continue;
                foreach (var card in controller.SpellTrapsOnField())
                {
                    if (card == null || !card.FaceUp || card.IsNegated || card.Def == null) continue;
                    var prog = CompiledEffectCache.GetOrCompile(card.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c != null && c.Action == EffectActionKind.ConvertOpponentLpGainToDamage)
                            return true;
                    }
                }
            }

            return false;
        }

        static void ApplyClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget, ref bool attackNegated, ref bool battleEnded,
            int costNumeric = 0)
        {
            if (clause == null) return;
            var opp = engine.OpponentOf(who);
            void Destroy(CardInstance c) => DestroyCard(engine, c, source, clause.BanishIfDestroyed);

            switch (clause.Action)
            {
                case EffectActionKind.Draw:
                    engine.Draw(who, Mathf.Max(1, clause.Amount), silent: false);
                    break;

                case EffectActionKind.Destroy:
                    if (clause.Zone == EffectZoneFilter.AllOtherCardsOnField)
                    {
                        foreach (var m in CollectAllOtherCards(engine, who, source).ToList())
                            Destroy(m);
                        break;
                    }

                    if (chosenTarget != null &&
                        (clause.ImplicitTargetIsBattleDestroyer ||
                         (clause.Zone != EffectZoneFilter.FieldMonsters &&
                          clause.Zone != EffectZoneFilter.AllOtherCardsOnField &&
                          (clause.RequiresTargetChoice ||
                           clause.Timing == EffectTiming.OpponentNormalOrFlipSummon ||
                           clause.Zone == EffectZoneFilter.AttackingMonster ||
                           clause.Zone == EffectZoneFilter.OpponentBattlingMonster))))
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        if (clause.Zone == EffectZoneFilter.AttackingMonster)
                            attackNegated = true;
                        if (clause.TributeChosenTarget)
                        {
                            var owner = OwnerOf(engine, who, chosenTarget) ?? who;
                            engine.SendCardToGrave(owner, chosenTarget);
                            engine.Log($"Tributed {chosenTarget.Name}.");
                        }
                        else if (PassesDestroyIfType(engine, chosenTarget, clause))
                            Destroy(chosenTarget);
                        break;
                    }

                    var mass = CollectAllMatching(engine, who, clause).ToList();
                    if (clause.AmountIsLevel && clause.Amount > 0)
                        mass.RemoveAll(m => m == null || m.Level != clause.Amount);
                    if (clause.FaceUpContinuousSpellsOnly)
                        mass.RemoveAll(m => m == null || !m.FaceUp || m.Def == null ||
                                            !m.Def.IsSpell || !m.Def.IsContinuousSpellOrTrap);
                    if (clause.FaceUpContinuousTrapsOnly)
                        mass.RemoveAll(m => m == null || !m.FaceUp || m.Def == null ||
                                            !m.Def.IsTrap || !m.Def.IsContinuousSpellOrTrap);
                    foreach (var m in mass)
                        Destroy(m);
                    if (mass.Count > 0)
                        DuelPresentationPacer.Extend(DuelPresentationPacer.ReadAfterCombatResult,
                            "Monsters destroyed — read the field…");
                    break;

                case EffectActionKind.SpecialSummonFromGy:
                    if (chosenTarget == null) break;
                    if (!CanSpecialSummonFromGyTarget(chosenTarget))
                    {
                        engine.Log($"{chosenTarget.Name} cannot be Special Summoned from the GY.");
                        break;
                    }
                    if (ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                    {
                        engine.Log("Necrovalley prevents the target from leaving the GY.");
                        break;
                    }
                    who.Graveyard.Remove(chosenTarget);
                    opp.Graveyard.Remove(chosenTarget);
                    var ssPos = clause.SummonInDefense
                        ? BattlePosition.Defense
                        : BattlePosition.Attack;
                    if (!engine.SpecialSummonToField(who, chosenTarget, ssPos, true))
                    {
                        who.Graveyard.Add(chosenTarget);
                        engine.Log("Special Summon failed — no zone.");
                    }
                    else
                    {
                        engine.Log($"Special Summoned {chosenTarget.Name} from GY.");
                        if (clause.DestroyHostWhenThisLeaves && source != null)
                        {
                            source.EquippedTo = chosenTarget;
                            if (!chosenTarget.Equips.Contains(source))
                                chosenTarget.Equips.Add(source);
                        }
                    }
                    break;

                case EffectActionKind.SpecialSummonFromDeck:
                    TrySpecialSummonFromDeck(engine, who, clause, chosenTarget);
                    break;

                case EffectActionKind.SpecialSummonFromHand:
                {
                    if (clause.RequiresLordOfDOnField && !LordOfDOnField(engine))
                    {
                        engine.Log("Lord of D. is not on the field — effect resolves without summoning.");
                        break;
                    }

                    if (chosenTarget != null)
                    {
                        if (who.Hand != null && who.Hand.Contains(chosenTarget) &&
                            DefMatchesSummonFilter(chosenTarget.Def, clause) &&
                            engine.FirstEmpty(who.MonsterZones) >= 0)
                        {
                            who.Hand.Remove(chosenTarget);
                            if (!engine.SpecialSummonToField(who, chosenTarget, BattlePosition.Attack, true))
                                who.Hand.Add(chosenTarget);
                            else
                                engine.Log($"Special Summoned {chosenTarget.Name} from hand.");
                        }
                        break;
                    }

                    var pool = (who.Hand ?? new List<CardInstance>())
                        .Where(c => c?.Def != null && DefMatchesSummonFilter(c.Def, clause));
                    if (clause.AmountIsLevel && clause.Amount > 0)
                        pool = pool.Where(c => c.Level <= clause.Amount);
                    else if (!string.IsNullOrEmpty(clause.RaceFilter))
                        pool = pool.Where(c => c.Def.race != null &&
                                               c.Def.race.IndexOf(clause.RaceFilter,
                                                   System.StringComparison.OrdinalIgnoreCase) >= 0);

                    var take = clause.AmountIsLevel ? 1 : (clause.SpecialSummonUpTo ? Mathf.Max(1, clause.Amount) : Mathf.Max(1, clause.Amount));
                    foreach (var d in pool.Take(take).ToList())
                    {
                        if (engine.FirstEmpty(who.MonsterZones) < 0) break;
                        who.Hand.Remove(d);
                        if (!engine.SpecialSummonToField(who, d, BattlePosition.Attack, true))
                            who.Hand.Add(d);
                        else
                            engine.Log($"Special Summoned {d.Name} from hand.");
                    }

                    break;
                }

                case EffectActionKind.AddFromGyToHand:
                    if (chosenTarget != null && who.Graveyard.Contains(chosenTarget))
                    {
                        if (ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                        {
                            engine.Log("Necrovalley prevents the target from leaving the GY.");
                            break;
                        }
                        who.Graveyard.Remove(chosenTarget);
                        who.Hand.Add(chosenTarget);
                        engine.Log($"Added {chosenTarget.Name} from GY to hand.");
                    }

                    break;

                case EffectActionKind.AddFromDeckToHand:
                {
                    if (chosenTarget != null)
                    {
                        MonsterEffects.AddCardIdFromDeckToHand(engine, who, chosenTarget.CardId,
                            source?.Name ?? "Search");
                        break;
                    }

                    var picks = CollectTargets(engine, who, clause, source);
                    if (picks.Count > 0)
                    {
                        MonsterEffects.AddCardIdFromDeckToHand(engine, who, picks[0].CardId,
                            source?.Name ?? "Search");
                        break;
                    }

                    if (clause.Zone == EffectZoneFilter.DeckFieldSpells ||
                        clause.Zone == EffectZoneFilter.DeckEquipSpells ||
                        clause.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq)
                    {
                        engine.Log(
                            $"Deck search: no legal card in Deck (remaining={who.DeckCount}).");
                        break;
                    }

                    var cap = clause.Amount > 0 ? clause.Amount : 1500;
                    var added = false;
                    if (engine.Database == null)
                    {
                        engine.Log("AddFromDeckToHand: no card database — search failed.");
                        break;
                    }

                    for (var i = 0; i < who.Deck.Count; i++)
                    {
                        var id = who.Deck[i];
                        if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster)
                            continue;
                        if (def.IsExtraDeck) continue;
                        if (clause.AmountIsDefMax)
                        {
                            if (def.def < 0 || def.def > cap) continue;
                        }
                        else if (def.atk < 0 || def.atk > cap) continue;
                        who.Deck.RemoveAt(i);
                        var inst = engine.CreateCardInstance(id);
                        who.Hand.Add(inst);
                        var stat = clause.AmountIsDefMax ? $"DEF {def.def}" : $"ATK {def.atk}";
                        engine.Log(
                            $"Added {inst.Name} ({stat}) from Deck to hand " +
                            $"(search ≤{cap}). Hand={who.HandCount}.");
                        added = true;
                        break;
                    }

                    if (!added)
                        engine.Log(
                            $"Deck search: no legal card in Deck (remaining={who.DeckCount}).");
                    break;
                }

                case EffectActionKind.ChangeBattlePosition:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        if (clause.ForceAttackPosition)
                            chosenTarget.Position = BattlePosition.Attack;
                        else if (clause.ForceDefensePosition)
                            chosenTarget.Position = BattlePosition.Defense;
                        else
                            chosenTarget.Position = chosenTarget.Position == BattlePosition.Attack
                                ? BattlePosition.Defense
                                : BattlePosition.Attack;
                        chosenTarget.FaceUp = true;
                        engine.Log($"{chosenTarget.Name} → {chosenTarget.Position} Position.");
                    }
                    else if (clause.Side == EffectSide.Both || clause.ForceDefensePosition)
                    {
                        foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                        {
                            if (m == null || !m.FaceUp) continue;
                            if (clause.ForceDefensePosition)
                                m.Position = BattlePosition.Defense;
                            engine.Log($"{m.Name} → {m.Position} Position.");
                        }
                    }

                    break;

                case EffectActionKind.SetTargetFaceDownDefense:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        chosenTarget.FaceUp = false;
                        chosenTarget.Position = BattlePosition.Defense;
                        engine.Log($"{chosenTarget.Name} is Set in face-down Defense Position.");
                    }

                    break;

                case EffectActionKind.NegateAttack:
                    engine.Log("Attack negated — Battle Phase ends.");
                    if (engine.DeclaredAttackingPlayer != null)
                        engine.ForceEndBattlePhase(engine.DeclaredAttackingPlayer);
                    attackNegated = true;
                    battleEnded = true;
                    break;

                case EffectActionKind.NegateThisAttack:
                    if (chosenTarget != null &&
                        ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    engine.Log("Attack negated — that monster cannot attack again this turn.");
                    attackNegated = true;
                    break;

                case EffectActionKind.SetAttackingMonsterAtkToZeroThisCalc:
                {
                    var atkMon = chosenTarget;
                    if (atkMon == null)
                        engine.TryGetBattlingPair(out atkMon, out _);
                    if (atkMon != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, atkMon, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        atkMon.AtkBecomesZeroThisCalculation = true;
                        engine.Log($"{atkMon.Name}'s ATK becomes 0 during this damage calculation.");
                    }

                    if (source != null)
                        source.EffectUsedWhileFaceUp = true;
                    break;
                }

                case EffectActionKind.SpecialSummonThisFromHand:
                    if (who.Hand.Contains(source))
                    {
                        who.Hand.Remove(source);
                        if (engine.SpecialSummonToField(who, source, BattlePosition.Attack, true))
                            engine.Log($"Special Summoned {source.Name}!");
                        else
                        {
                            who.Hand.Add(source);
                            engine.Log("Special Summon failed — no Monster Zone.");
                        }
                    }

                    break;

                case EffectActionKind.SkipOpponentNextDrawPhase:
                    opp.SkipNextDrawPhase = true;
                    engine.Log($"{opp.Name} skips their next Draw Phase.");
                    break;

                case EffectActionKind.InflictDamageEqualToAtk:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        engine.ApplyEffectDamage(opp, chosenTarget.CurrentAtk, source.Name);
                    }
                    break;

                case EffectActionKind.GainLpEqualToAtk:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        var gain = chosenTarget.CurrentAtk;
                        ApplyLifePointGain(engine, who, gain, source?.Name);
                    }

                    break;

                case EffectActionKind.LoseAtkDefUntilEndOfTurn:
                {
                    var pay = clause.Amount;
                    if (clause.RequiresLpCostMultiple > 0 && engine.PendingActivation != null &&
                        engine.PendingActivation.AwaitingLpCost)
                        pay = 0; // paid in FinishPayLp
                    if (pay > 0 && chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        chosenTarget.UntilEndOfTurnAtk -= pay;
                        if (clause.DefAmount != 0)
                            chosenTarget.UntilEndOfTurnDef -= pay;
                        engine.Log(
                            $"{chosenTarget.Name} loses {pay} ATK" +
                            (clause.DefAmount != 0 ? $"/{pay} DEF" : "") +
                            $" until the End Phase (now {chosenTarget.CurrentAtk}/{chosenTarget.CurrentDef}).");
                    }

                    break;
                }

                case EffectActionKind.GainAtkDefUntilEndOfTurn:
                {
                    var t = chosenTarget ?? source;
                    if (t != null && (clause.Amount != 0 || clause.DefAmount != 0))
                    {
                        if (chosenTarget != null &&
                            ContinuousProtections.TargetedEffectBlocked(engine, t, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        if (clause.Amount != 0)
                            t.UntilEndOfTurnAtk += clause.Amount;
                        if (clause.DefAmount != 0)
                            t.UntilEndOfTurnDef += clause.DefAmount;
                        engine.Log(
                            $"{t.Name} gains {clause.Amount} ATK/{clause.DefAmount} DEF until the End Phase " +
                            $"(now {t.CurrentAtk}/{t.CurrentDef}).");
                    }

                    break;
                }

                case EffectActionKind.GainLifePoints:
                {
                    void ApplyGain(DuelistState gainWho)
                    {
                        if (gainWho == null) return;
                        var gained = AmountWithGyCopies(gainWho, source, clause);
                        if (clause.ScaleAmountByFieldMonsters)
                            gained = Mathf.Max(0, clause.Amount) *
                                     (who.MonsterCount + (opp != null ? opp.MonsterCount : 0));
                        else if (clause.ScaleAmountByControllerMonsters)
                            gained = Mathf.Max(0, clause.Amount) * who.MonsterCount;
                        ApplyLifePointGain(engine, gainWho, gained, source?.Name);
                    }

                    if (clause.Side == EffectSide.Both)
                    {
                        ApplyGain(who);
                        ApplyGain(opp);
                    }
                    else
                    {
                        ApplyGain(clause.Side == EffectSide.Opponent ? opp : who);
                    }
                    break;
                }

                case EffectActionKind.PayLifePoints:
                {
                    var payer = clause.Side == EffectSide.Opponent ? opp : who;
                    var paid = Mathf.Max(0, clause.Amount);
                    if (payer != null)
                    {
                        payer.LifePoints = Mathf.Max(0, payer.LifePoints - paid);
                        engine.Log($"{payer.Name} pays {paid} LP ({payer.LifePoints}).");
                    }

                    break;
                }

                case EffectActionKind.TakeEffectDamage:
                    engine.ApplyEffectDamage(who, Mathf.Max(0, clause.Amount), source?.Name);
                    break;

                case EffectActionKind.PreventControllerBattleDamage:
                case EffectActionKind.FieldTreatedAsName:
                case EffectActionKind.SelfDestroyUnlessNamedFaceUp:
                case EffectActionKind.DestroyThisAfterResolvingTargetingEffect:
                case EffectActionKind.CannotBanishFromGraveyard:
                case EffectActionKind.CannotTargetCardsInGraveyard:
                case EffectActionKind.ConvertOpponentLpGainToDamage:
                    // Continuous — applied by FieldSpellEffects / ContinuousProtections while face-up.
                    break;

                case EffectActionKind.ApplyLingeringAtkDef:
                {
                    var t = chosenTarget ?? source;
                    if (t != null)
                    {
                        if (chosenTarget != null &&
                            ContinuousProtections.TargetedEffectBlocked(engine, t, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        t.LingeringAtkModifier += clause.Amount;
                        t.LingeringDefModifier += clause.DefAmount;
                        var atkBit = clause.Amount >= 0
                            ? $"gains {clause.Amount}"
                            : $"loses {-clause.Amount}";
                        var defBit = clause.DefAmount == 0
                            ? ""
                            : clause.DefAmount >= 0
                                ? $"/{clause.DefAmount}"
                                : $"/{-clause.DefAmount}";
                        engine.Log(
                            $"{t.Name} {atkBit}{defBit} ATK/DEF (now {t.CurrentAtk}/{t.CurrentDef}).");
                    }

                    break;
                }

                case EffectActionKind.SendFromTopOfDeckToGy:
                {
                    var millWho = clause.Side == EffectSide.Opponent ? opp : who;
                    SendTopOfDeckToGy(engine, millWho, Mathf.Max(1, clause.Amount));
                    break;
                }

                case EffectActionKind.HalveOriginalAtk:
                {
                    var t = chosenTarget;
                    if (t != null &&
                        ContinuousProtections.TargetedEffectBlocked(engine, t, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    if (t?.Def != null && t.Def.atk > 0)
                    {
                        var cut = t.Def.atk / 2;
                        t.LingeringAtkModifier -= cut;
                        engine.Log($"{t.Name}'s original ATK is halved (now {t.CurrentAtk}).");
                    }

                    break;
                }

                case EffectActionKind.DestroyTokensInflictPer:
                {
                    var tokens = new List<CardInstance>();
                    foreach (var m in who.MonstersOnField())
                        if (m != null && m.IsToken) tokens.Add(m);
                    foreach (var m in opp.MonstersOnField())
                        if (m != null && m.IsToken) tokens.Add(m);
                    var n = 0;
                    foreach (var tok in tokens)
                    {
                        Destroy(tok);
                        n++;
                    }

                    if (n > 0)
                    {
                        var dmg = Mathf.Max(0, clause.Amount) * n;
                        engine.ApplyEffectDamage(opp, dmg, source?.Name);
                    }

                    break;
                }

                case EffectActionKind.ReturnAllFaceUpFusionsToExtra:
                    foreach (var m in who.MonstersOnField().ToList())
                    {
                        if (m?.Def != null && m.FaceUp &&
                            m.Def.type != null &&
                            m.Def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0)
                            if (!ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                engine.ReturnCardToHand(m);
                    }

                    foreach (var m in opp.MonstersOnField().ToList())
                    {
                        if (m?.Def != null && m.FaceUp &&
                            m.Def.type != null &&
                            m.Def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0)
                            if (!ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                engine.ReturnCardToHand(m);
                    }

                    break;

                case EffectActionKind.BanishThenSameNameFromOppHandDeck:
                {
                    var t = chosenTarget;
                    if (t == null) break;
                    var id = t.CardId;
                    var owner = engine.ControllerOf(t);
                    if (owner == null || ContinuousProtections.TargetedEffectBlocked(engine, t, source))
                        break;
                    engine.BanishCard(owner, t);
                    engine.BanishCopiesFromHandAndDeck(opp, id);
                    break;
                }

                case EffectActionKind.DestroySameNameInControllerHandAndDeck:
                {
                    var t = chosenTarget;
                    if (t == null) break;
                    var owner = engine.ControllerOf(t);
                    if (owner != null)
                        engine.DestroyCopiesFromHandAndDeck(owner, t.CardId);
                    break;
                }

                case EffectActionKind.DestroyOppAttackThenDamage:
                {
                    var hits = CollectAllMatching(engine, who, clause).ToList();
                    if (hits.Count == 0) break;
                    foreach (var m in hits)
                        Destroy(m);
                    engine.ApplyEffectDamage(opp, Mathf.Max(0, clause.Amount), source?.Name);
                    break;
                }

                case EffectActionKind.DestroyAllEquips:
                {
                    var equips = new List<CardInstance>();
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsEquipSpell) equips.Add(st);
                    foreach (var st in opp.SpellTrapsOnField())
                        if (st?.Def != null && st.Def.IsEquipSpell) equips.Add(st);
                    foreach (var e in equips)
                        Destroy(e);
                    break;
                }

                case EffectActionKind.DestroyAllEquippedMonsters:
                {
                    var hosts = new List<CardInstance>();
                    foreach (var m in who.MonstersOnField())
                        if (m?.Equips != null && m.Equips.Count > 0) hosts.Add(m);
                    foreach (var m in opp.MonstersOnField())
                        if (m?.Equips != null && m.Equips.Count > 0) hosts.Add(m);
                    foreach (var h in hosts)
                        Destroy(h);
                    break;
                }

                case EffectActionKind.Banish:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        var owner = OwnerOf(engine, who, chosenTarget);
                        if (owner?.Graveyard?.Contains(chosenTarget) == true &&
                            ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                        {
                            engine.Log("Necrovalley prevents the target from leaving the GY.");
                            break;
                        }
                        if (owner != null) engine.BanishCard(owner, chosenTarget);
                    }
                    else
                    {
                        foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                        {
                            if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                            {
                                engine.Log("Banish target is unaffected at resolution.");
                                continue;
                            }
                            var owner = OwnerOf(engine, who, m);
                            if (owner?.Graveyard?.Contains(m) == true &&
                                ContinuousProtections.GyMovementLocked(engine, source, m))
                            {
                                engine.Log("Necrovalley prevents the target from leaving the GY.");
                                continue;
                            }
                            if (owner != null) engine.BanishCard(owner, m);
                        }
                    }

                    if (clause.AlsoBanishThis && source != null)
                    {
                        var selfOwner = OwnerOf(engine, who, source) ?? who;
                        if (selfOwner != null)
                            engine.BanishCard(selfOwner, source);
                    }

                    break;

                case EffectActionKind.ApplyWabokuStyle:
                    who.WabokuActive = true;
                    engine.Log(
                        $"{who.Name}: no battle damage this turn; monsters cannot be destroyed by battle.");
                    break;

                case EffectActionKind.PreventOpponentAttacksThisTurn:
                    if (opp != null)
                    {
                        opp.CannotDeclareAttackThisTurn = true;
                        engine.Log($"{opp.Name} cannot declare an attack this turn.");
                    }
                    break;

                case EffectActionKind.ApplySwordsOfRevealingLight:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp)
                        {
                            m.FaceUp = true;
                            m.Position = BattlePosition.Defense;
                        }
                    }

                    source.ContinuousTurnsRemaining = clause.Amount > 0 ? clause.Amount : 3;
                    engine.Log(
                        "Swords of Revealing Light: flip opponent face-down monsters; " +
                        "opponent cannot declare attacks; lasts opponent's turns.");
                    break;

                case EffectActionKind.BothPlayersDiscardAndRedraw:
                {
                    var nYou = who.Hand.Count;
                    var nOpp = opp.Hand.Count;
                    DiscardAll(who);
                    DiscardAll(opp);
                    if (nYou > 0) engine.Draw(who, nYou, silent: false);
                    if (nOpp > 0) engine.Draw(opp, nOpp, silent: true);
                    engine.Log($"Card Destruction: redraw {nYou} / {nOpp}.");
                    break;
                }

                case EffectActionKind.FusionSummonRegistered:
                    if (!SpellTrapEffects.TryResolveRegisteredFusion(engine, who))
                        engine.Log("Fusion: no legal registered materials.");
                    break;

                case EffectActionKind.RitualSummon:
                    if (!WRLDZ.Duel.Rules.RitualProcedures.TryResolve(engine, who, source))
                        engine.Log("Ritual: procedure could not resolve.");
                    break;

                case EffectActionKind.EffectDamageBothFromOriginalAtk:
                    if (chosenTarget == null ||
                        ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        if (chosenTarget != null)
                            engine.Log("Target is protected at resolution.");
                        break;
                    }
                    var originalAtk = chosenTarget.Def != null && chosenTarget.Def.atk >= 0
                        ? chosenTarget.Def.atk
                        : 0;
                    engine.Log($"Destroys {chosenTarget.Name}!");
                    Destroy(chosenTarget);
                    engine.ApplyEffectDamage(who, originalAtk, source.Name);
                    if (!engine.GameOver)
                        engine.ApplyEffectDamage(opp, originalAtk, source.Name);
                    break;

                case EffectActionKind.DiscardSelfNoBattleDamageThisBattle:
                    // Kuriboh-style: cost + operation (also hard-coded in MonsterEffects for DC window)
                    if (MonsterEffects.TryActivateHandDamageCalculation(engine, who, source))
                        return; // ContinueAfterResponse already called
                    break;

                case EffectActionKind.ContinuousCannotAttack:
                    if (chosenTarget != null && source != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        source.EquippedTo = chosenTarget;
                        if (!chosenTarget.Equips.Contains(source))
                            chosenTarget.Equips.Add(source);
                        engine.Log($"{source.Name} binds {chosenTarget.Name}.");
                    }
                    break;

                case EffectActionKind.DiscardRandomFromOpponentHand:
                    DiscardRandomFromOpponentHand(engine, opp, source, clause);
                    break;

                case EffectActionKind.CyberJarStyle:
                    // Direct structural resolution (avoid re-entering flip text path)
                    MonsterEffects.ResolveCyberJarPublic(engine, who);
                    break;

                case EffectActionKind.ReturnToHand:
                    if (chosenTarget == null)
                    {
                        if (clause.RequiresTargetChoice) break;
                        foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                        {
                            if (m == null) continue;
                            if (m.IsToken) Destroy(m);
                            else if (!ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                engine.ReturnCardToHand(m);
                            else
                                engine.Log("Return target is unaffected at resolution.");
                        }
                        break;
                    }
                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Return target is protected at resolution.");
                        break;
                    }
                    if (chosenTarget.IsToken)
                    {
                        Destroy(chosenTarget);
                        break;
                    }

                    engine.ReturnCardToHand(chosenTarget);
                    break;

                case EffectActionKind.InflictDamageToOpponent:
                    engine.ApplyEffectDamage(opp, AmountWithGyCopies(who, source, clause), source?.Name);
                    break;

                case EffectActionKind.InflictDamageHalfTributedAtk:
                    engine.ApplyEffectDamage(opp, Mathf.Max(0, costNumeric / 2), source?.Name);
                    break;

                case EffectActionKind.SetThisFaceDownDefense:
                    if (source != null && who.TryFindMonster(source, out _))
                    {
                        source.FaceUp = false;
                        source.Position = BattlePosition.Defense;
                        source.ChangedPositionThisTurn = true;
                        engine.Log($"{source.Name} is flipped to face-down Defense Position.");
                    }

                    break;

                case EffectActionKind.ChangeThisBattlePosition:
                    if (source != null && who.TryFindMonster(source, out _))
                    {
                        source.Position = source.Position == BattlePosition.Attack
                            ? BattlePosition.Defense
                            : BattlePosition.Attack;
                        source.ChangedPositionThisTurn = true;
                        engine.Log($"{source.Name} → {source.Position} Position.");
                    }

                    break;

                case EffectActionKind.GrantDirectAttackThisTurn:
                    if (source != null)
                    {
                        source.DirectAttackThisTurn = true;
                        engine.Log($"{source.Name} can attack directly this turn.");
                    }

                    break;

                case EffectActionKind.ForceOpponentDirectAttacksThisTurn:
                {
                    var forced = opp;
                    if (forced != null)
                    {
                        forced.MustAttackDirectlyThisTurn = true;
                        engine.Log(
                            $"{source?.Name}: {forced.Name}'s monster attacks become direct attacks this turn.");
                        engine.ConvertDeclaredAttackToDirect();
                    }

                    break;
                }

                case EffectActionKind.GainThisAtkUntilEnd:
                {
                    var gain = clause.Amount;
                    if (clause.ScaleAmountByCostCount)
                        gain *= Mathf.Max(1, costNumeric);
                    if (source != null && gain != 0)
                    {
                        source.UntilEndOfTurnAtk += gain;
                        engine.Log(
                            $"{source.Name} gains {gain} ATK until the End Phase " +
                            $"(now {source.CurrentAtk}).");
                    }

                    break;
                }

                case EffectActionKind.SpecialSummonNamed:
                    TrySpecialSummonNamed(engine, who, clause, source);
                    break;

                case EffectActionKind.AddNamedFromDeckToHand:
                    AddNamedFromDeck(engine, who, clause);
                    break;

                case EffectActionKind.DestroySpecialSummonedMonsters:
                    foreach (var m in who.MonstersOnField().Concat(opp.MonstersOnField()).ToList())
                    {
                        if (m == null || m == source) continue;
                        if (m.WasSpecialSummoned)
                            Destroy(m);
                    }

                    break;

                case EffectActionKind.DestroyOppMonstersAtkLeq:
                {
                    var cap = costNumeric > 0 ? costNumeric : clause.Amount;
                    foreach (var m in opp.MonstersOnField().ToList())
                    {
                        if (m != null && m.CurrentAtk <= cap)
                            Destroy(m);
                    }

                    break;
                }

                case EffectActionKind.CoinCallDestroyOppOrSelf:
                    break;
                case EffectActionKind.CoinCallDoubleOrHalveAtk:
                    break;

                case EffectActionKind.CoinTossNDestroyIfHeads:
                {
                    var n = clause.CoinCount > 0 ? clause.CoinCount : 3;
                    var need = clause.Amount > 0 ? clause.Amount : 2;
                    var heads = engine.Rng.TossCoinsCountHeads(n);
                    engine.Log($"{source?.Name}: {heads}/{n} heads (need {need}).");
                    if (heads >= need && chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        Destroy(chosenTarget);
                    }
                    break;
                }

                case EffectActionKind.RollDieZorc:
                {
                    var roll = engine.Rng.RollDie();
                    engine.Log($"{source?.Name} rolls a {roll}.");
                    if (roll <= (clause.DieLowMax > 0 ? clause.DieLowMax : 2))
                    {
                        foreach (var m in opp.MonstersOnField().ToList())
                            Destroy(m);
                    }
                    else if (roll <= (clause.DieMidMax > 0 ? clause.DieMidMax : 5))
                    {
                        var pick = opp.MonstersOnField().OrderByDescending(m => m.CurrentAtk)
                            .FirstOrDefault();
                        if (pick != null) Destroy(pick);
                    }
                    else
                    {
                        foreach (var m in who.MonstersOnField().ToList())
                            Destroy(m);
                    }

                    break;
                }

                case EffectActionKind.SpecialSummonToken:
                    SummonTokens(engine, who, clause);
                    break;

                case EffectActionKind.SpecialSummonFusionFromExtra:
                {
                    var extras = CollectExtraFusions(engine, who);
                    if (extras.Count == 0)
                    {
                        engine.Log("No Fusion Monster in the Extra Deck.");
                        break;
                    }

                    var id = extras[0];
                    who.ExtraDeck.Remove(id);
                    var inst = engine.CreateCardInstance(id);
                    if (!engine.SpecialSummonToField(who, inst, BattlePosition.Attack, true))
                    {
                        who.ExtraDeck.Add(id);
                        engine.Log("Special Summon from Extra Deck failed — no zone.");
                    }
                    else
                        engine.Log($"Special Summoned {inst.Name} from the Extra Deck.");
                    break;
                }

                case EffectActionKind.TakeControlLevelLeq:
                {
                    var cap = clause.Amount > 0 ? clause.Amount : 3;
                    foreach (var m in opp.MonstersOnField().ToList())
                    {
                        if (m != null && m.FaceUp && m.Level <= cap)
                            engine.TryTakeControl(who, m);
                    }

                    break;
                }

                case EffectActionKind.CrushCardVirusStyle:
                    ResolveCrushCardVirus(engine, who, source, clause);
                    break;

                case EffectActionKind.MagicalHatsStyle:
                    break;

                case EffectActionKind.EquipThisToTarget:
                    if (chosenTarget == null || source == null) break;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    EquipCardTo(engine, who, source, chosenTarget, clause.EquipAtkBonus, clause);
                    if (clause.TakeControlOfTarget)
                    {
                        chosenTarget.TakenByEquipControl = true;
                        if (!engine.TryTakeControl(who, chosenTarget))
                            engine.Log($"{source.Name}: take control failed — no zone.");
                    }
                    break;

                case EffectActionKind.UnequipThisSpecialSummon:
                    UnequipAndSummon(engine, who, source);
                    break;

                case EffectActionKind.EquipTargetToThis:
                    if (chosenTarget == null || source == null) break;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    // Ignis eqop re-checks eqcon at resolution: do not replace an existing absorb.
                    if (source.Equips != null && source.Equips.Count > 0)
                    {
                        engine.Log($"{source.Name}: already equipped (max. 1) — absorb does not resolve.");
                        break;
                    }
                    EquipCardTo(engine, engine.ControllerOf(chosenTarget) ?? opp, chosenTarget, source, 0);
                    break;

                case EffectActionKind.PlaceSpellCounters:
                {
                    var add = clause.Amount > 0 ? clause.Amount : 1;
                    var max = clause.CounterMax;
                    source.Counters += add;
                    if (max > 0 && source.Counters > max)
                        source.Counters = max;
                    if (max > 0) source.SpellCounterMax = max;
                    engine.Log($"{source.Name}: Spell Counters → {source.Counters}.");
                    break;
                }

                case EffectActionKind.GainAtkPerSpellCounter:
                    break;

                case EffectActionKind.PlaceThisOnTopOfDeck:
                    PlaceThisOnTopOfDeck(engine, who, source);
                    break;
            }
        }

        static void PlaceThisOnTopOfDeck(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null) return;
            if (who.Graveyard.Contains(card) &&
                ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log("Necrovalley prevents the target from leaving the GY.");
                return;
            }
            who.Graveyard.Remove(card);
            who.Hand.Remove(card);
            if (who.TryFindSpellTrap(card, out var si))
                who.SpellTrapZones[si].Occupant = null;
            if (who.TryFindMonster(card, out var mi))
                who.MonsterZones[mi].Occupant = null;
            who.Deck.Insert(0, card.CardId);
            engine.Log($"{card.Name} is placed on top of the Deck.");
        }

        public static void NotifyDestroyedOpponentByBattle(DuelEngine engine, DuelistState who,
            DuelistState opp, CardInstance attacker)
        {
            if (engine == null || who == null || opp == null || attacker?.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(attacker);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardDestroysByBattle))
                ApplyClause(engine, who, attacker, c, null, ref dummy, ref dummy);
        }

        /// <summary>
        /// After battle damage actually reduced LP. Direct-attack-only clauses skip if
        /// there was a battle target. Empty opponent hand is a no-op.
        /// </summary>
        public static void NotifyInflictedBattleDamage(DuelEngine engine, DuelistState who,
            DuelistState opp, CardInstance source, bool wasDirect)
        {
            if (engine == null || who == null || opp == null || source?.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(source);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage))
            {
                if (c == null) continue;
                if (c.RequiresDirectAttack && !wasDirect) continue;
                ApplyClause(engine, who, source, c, null, ref dummy, ref dummy);
            }
        }

        /// <summary>
        /// After LP damage, before battle destruction. Wall of Illusion bounce /
        /// D.D. Warrior Lady &amp; Assailant banish-both. No cardId branches.
        /// </summary>
        public static void NotifyAfterDamageCalculation(
            DuelEngine engine, DuelistState atkPlayer, DuelistState defPlayer,
            CardInstance attacker, CardInstance defender,
            bool destroyAttacker, bool destroyDefender)
        {
            if (engine == null) return;
            FireAfterDamageFor(engine, atkPlayer, attacker, defender, attacker,
                destroyAttacker, isAttackTarget: false);
            FireAfterDamageFor(engine, defPlayer, defender, attacker, attacker,
                destroyDefender, isAttackTarget: true);
        }

        static void FireAfterDamageFor(
            DuelEngine engine, DuelistState who, CardInstance self, CardInstance other,
            CardInstance attacker, bool selfDestroyed, bool isAttackTarget)
        {
            if (engine == null || who == null || self?.Def == null) return;
            if (!who.TryFindMonster(self, out _) && !selfDestroyed) return;
            var prog = CompiledEffectCache.GetOrCompile(self);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.AfterDamageCalculation))
            {
                if (c == null) continue;
                if (c.RequiresThisDestroyedByBattle && !selfDestroyed) continue;
                if (c.RequiresThisIsAttackTarget && !isAttackTarget) continue;
                // Optional "You can" auto-resolves (same as other monster battle triggers).
                CardInstance tgt = null;
                if (c.Zone == EffectZoneFilter.AttackingMonster)
                    tgt = attacker;
                else if (c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                    tgt = other;
                else
                    tgt = other;
                if (tgt == null &&
                    (c.Zone == EffectZoneFilter.AttackingMonster ||
                     c.Zone == EffectZoneFilter.OpponentBattlingMonster))
                    continue;
                ApplyClause(engine, who, self, c, tgt, ref dummy, ref dummy);
            }
        }

        /// <summary>
        /// End of Damage Step after destructions. Hyper Hammerhead bounce when the
        /// battled opponent monster survived.
        /// </summary>
        public static void NotifyEndOfDamageStep(
            DuelEngine engine, DuelistState atkPlayer, DuelistState defPlayer,
            CardInstance attacker, CardInstance defender)
        {
            if (engine == null) return;
            FireEndOfDamageFor(engine, atkPlayer, attacker, defender);
            FireEndOfDamageFor(engine, defPlayer, defender, attacker);
        }

        static void FireEndOfDamageFor(
            DuelEngine engine, DuelistState who, CardInstance self, CardInstance other)
        {
            if (engine == null || who == null || self?.Def == null) return;
            if (!who.TryFindMonster(self, out _)) return;
            var prog = CompiledEffectCache.GetOrCompile(self);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.EndOfDamageStep))
            {
                if (c == null) continue;
                if (c.RequiresBattledMonsterNotDestroyed)
                {
                    if (other == null) continue;
                    var otherOwner = engine.OpponentOf(who);
                    if (otherOwner == null || !otherOwner.TryFindMonster(other, out _))
                        continue;
                }

                var tgt = c.Zone == EffectZoneFilter.OpponentBattlingMonster ? other
                    : c.Zone == EffectZoneFilter.AttackingMonster ? other
                    : other;
                ApplyClause(engine, who, self, c, tgt, ref dummy, ref dummy);
            }
        }

        static void DiscardRandomFromOpponentHand(DuelEngine engine,
            DuelistState opp, CardInstance source, EffectClause clause)
        {
            if (engine == null || opp?.Hand == null) return;
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 1;
            var discarded = 0;
            while (discarded < n && opp.Hand.Count > 0)
            {
                var i = engine.Rng.Next(opp.Hand.Count);
                if (i < 0 || i >= opp.Hand.Count) i = 0;
                var pick = opp.Hand[i];
                var gone = MonsterEffects.DiscardFromHandByInstance(opp, pick);
                if (gone == null) break;
                discarded++;
                var src = source != null ? source.Name : "Effect";
                engine.Log($"{src}: {opp.Name} discards {gone.Name} at random.");
            }

            if (discarded == 0 && source != null)
                engine.Log($"{source.Name}: {opp.Name} has no cards in hand to discard.");
        }

        public static void FirePhaseTriggers(DuelEngine engine, DuelistState who, EffectTiming timing)
        {
            if (engine == null || who == null) return;
            var seen = new HashSet<CardInstance>();
            FirePhaseTriggersFor(engine, who, timing, opponentTurnClauses: false, seen);
            if (timing == EffectTiming.StandbyPhase || timing == EffectTiming.EndPhase)
            {
                var other = engine.OpponentOf(who);
                if (other != null)
                    FirePhaseTriggersFor(engine, other, timing, opponentTurnClauses: true, seen);
            }

            engine.NotifyPublic();
        }

        /// <summary>
        /// Ectoplasmer family: the turn player tributes 1 face-up monster they control;
        /// if they do, inflict half that monster's printed ATK to the other player.
        /// No face-up monster → skip (mandatory trigger still resolved, no damage).
        /// </summary>
        static bool ResolveTurnPlayerTributeHalfAtk(DuelEngine engine, CardInstance card,
            EffectClause c)
        {
            if (engine == null || card == null) return false;
            var tributer = engine.TurnPlayer;
            if (tributer == null) return false;
            var tributes = CollectTributeCost(tributer, card, c);
            if (tributes.Count == 0)
            {
                engine.Log($"{card.Name}: no face-up monster for the turn player to Tribute.");
                return false;
            }

            if (tributer.IsPlayer)
            {
                var pending = new PendingActivation
                {
                    Controller = tributer,
                    Card = card,
                    FromHand = false,
                    WasSetOnField = true,
                    TargetKind = EffectTargetKind.TributeMonsterYouControl,
                    IsMonsterEffect = false,
                    UsesTextProgram = true,
                    AwaitingEffectTribute = true
                };
                pending.LegalTargets.AddRange(tributes);
                engine.SetPendingActivation(pending);
                engine.Log($"{card.Name}: Tribute 1 face-up monster you control (effect, not cost).");
                engine.NotifyPublic();
                return true;
            }

            var pick = tributes[0];
            var printed = PrintedOriginalAtk(pick);
            engine.SendCardToGrave(tributer, pick, sentBy: card);
            engine.Log($"{card.Name}: {tributer.Name} Tributes {pick.Name}.");
            var victim = engine.OpponentOf(tributer);
            if (victim != null)
                engine.ApplyEffectDamage(victim, printed / 2, card.Name);
            return true;
        }

        static int PrintedOriginalAtk(CardInstance c)
        {
            if (c?.Def == null) return 0;
            return Mathf.Max(0, c.Def.atk);
        }

        static void ResolveTurnPlayerSubjectClause(DuelEngine engine, CardInstance card,
            EffectClause c)
        {
            var turn = engine.TurnPlayer;
            if (turn == null || c == null) return;
            if (c.Action == EffectActionKind.TakeEffectDamage)
            {
                engine.ApplyEffectDamage(turn, Mathf.Max(0, c.Amount), card?.Name);
                return;
            }

            if (c.Action != EffectActionKind.ChangeBattlePosition) return;
            foreach (var m in turn.MonstersOnField())
            {
                if (m == null || !m.FaceUp) continue;
                m.Position = m.Position == BattlePosition.Attack
                    ? BattlePosition.Defense
                    : BattlePosition.Attack;
                engine.Log($"{card?.Name}: {m.Name} → {m.Position} Position.");
            }
        }

        static void ResolvePayLpOrDestroyThis(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause c)
        {
            var n = c != null && c.PayLpAmount > 0 ? c.PayLpAmount : 100;
            if (who != null && who.LifePoints >= n)
            {
                who.LifePoints -= n;
                engine.Log($"Cost: pay {n} LP or destroy {card?.Name} → {who.Name} at {who.LifePoints} LP.");
                return;
            }

            engine.Log($"{card?.Name}: not enough LP — destroy this card.");
            engine.SendCardToGrave(who, card);
        }

        static void FirePhaseTriggersFor(DuelEngine engine, DuelistState who, EffectTiming timing,
            bool opponentTurnClauses, HashSet<CardInstance> seen)
        {
            var cards = new List<CardInstance>();
            foreach (var m in who.MonstersOnField())
                cards.Add(m);
            foreach (var st in who.SpellTrapsOnField())
                cards.Add(st);
            foreach (var g in who.Graveyard)
                cards.Add(g);

            foreach (var card in cards.ToList())
            {
                if (card?.Def == null) continue;
                if (seen != null && !seen.Add(card)) continue;
                var prog = CompiledEffectCache.GetOrCompile(card.Def);
                // Fire compiled Standby/End clauses even when leftover text keeps the card a Stub
                // (Lava Golem NS-lock rider, Messenger of Peace pay-or-destroy, etc.).
                if (prog == null) continue;
                var clauses = prog.ClausesFor(timing);
                if (clauses == null || clauses.Count == 0) continue;
                foreach (var c in clauses)
                {
                    if (c == null) continue;
                    if (!c.TurnPlayerTributes &&
                        (opponentTurnClauses ? !c.OpponentTurnOnly : c.OpponentTurnOnly))
                        continue;
                    if (c.TurnPlayerTributes)
                    {
                        if (!card.FaceUp) continue;
                        if (who.Graveyard != null && who.Graveyard.Contains(card)) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        ResolveTurnPlayerTributeHalfAtk(engine, card, c);
                        if (engine.PendingActivation != null)
                            return;
                        continue;
                    }

                    if (c.TurnPlayerIsSubject)
                    {
                        if (!card.FaceUp) continue;
                        if (who.Graveyard != null && who.Graveyard.Contains(card)) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        ResolveTurnPlayerSubjectClause(engine, card, c);
                        continue;
                    }

                    if (c.Action == EffectActionKind.PayLpOrDestroyThis)
                    {
                        if (!card.FaceUp) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        ResolvePayLpOrDestroyThis(engine, who, card, c);
                        continue;
                    }
                    if (c.ResolvesFromGy && (who.Graveyard == null || !who.Graveyard.Contains(card)))
                        continue;
                    if (!c.ResolvesFromGy && who.Graveyard != null && who.Graveyard.Contains(card))
                        continue;
                    if (!c.ResolvesFromGy && !card.FaceUp)
                        continue;
                    if (c.RequiresThisAttackPosition && card.Position != BattlePosition.Attack)
                        continue;
                    if (c.RequiresThisDefensePosition && card.Position != BattlePosition.Defense)
                        continue;
                    if (c.RequiresDestroyedByBattleThisTurn && !card.DestroyedByBattleThisTurn)
                        continue;
                    // Fox Fire: GY End Phase self-SS only if THIS copy was battle-destroyed this turn.
                    // Distinct from DestroyedByBattleThisTurn (this card destroyed an opponent).
                    if (c.RequiresThisDestroyedByBattle)
                    {
                        if (!card.WasDestroyedByBattle) continue;
                        if (card.SentFromFieldTurnNumber != engine.TurnNumber) continue;
                    }
                    if (c.RequiresSentByContinuousSpell && !card.SentByContinuousSpellEffect)
                        continue;
                    if (c.RequiresNextControllerStandby &&
                        engine.TurnNumber <= card.SentFromFieldTurnNumber)
                        continue;
                    if (c.RequiresSummonedOrFlippedThisTurn && !card.SummonedThisTurn)
                        continue;
                    if (c.RequiresSendThisToGy &&
                        (who.TryFindMonster(card, out _) || who.TryFindSpellTrap(card, out _)))
                    {
                        engine.SendCardToGrave(who, card);
                        engine.Log($"Cost: send {card.Name} to the GY.");
                    }

                    var dummy = false;
                    CardInstance phaseTarget = null;
                    if (c.Action == EffectActionKind.ReturnToHand)
                        phaseTarget = card;
                    else if (c.Action == EffectActionKind.SpecialSummonFromGy && c.ResolvesFromGy)
                        phaseTarget = card;
                    if (c.Action == EffectActionKind.SpecialSummonFromGy && c.ResolvesFromGy &&
                        ContinuousProtections.GyMovementLocked(engine, card, card))
                    {
                        engine.Log("Necrovalley prevents the target from leaving the GY.");
                        continue;
                    }
                    ApplyClause(engine, who, card, c, phaseTarget, ref dummy, ref dummy);
                    if (c.RequiresNextControllerStandby)
                        card.SentByContinuousSpellEffect = false;
                }
            }
        }

        /// <summary>
        /// After a targeting card effect has finished resolving (Equip Spell, MST, Book of Moon,
        /// Flip destroy, etc.), destroy face-up field monsters that compiled
        /// <see cref="EffectActionKind.DestroyThisAfterResolvingTargetingEffect"/> and were a
        /// target of that effect. Face-down or already-left: no-op. Does not start a chain.
        /// </summary>
        public static void NotifyTargetingEffectResolved(DuelEngine engine, CardInstance target, bool isPsctTarget)
        {
            if (!isPsctTarget) return;
            if (engine == null || target == null || !target.FaceUp) return;
            if (target.IsNegated) return;
            var owner = engine.ControllerOf(target);
            if (owner == null || !owner.TryFindMonster(target, out _)) return;

            var prog = CompiledEffectCache.GetOrCompile(target.Def);
            if (prog == null) return;
            var has = false;
            foreach (var c in prog.ClauseList)
            {
                if (c != null &&
                    c.Action == EffectActionKind.DestroyThisAfterResolvingTargetingEffect)
                {
                    has = true;
                    break;
                }
            }

            if (!has) return;
            engine.Log($"{target.Name}: destroyed after a targeting effect resolved.");
            DestroyCard(engine, target, source: target);
        }

        public static void NotifyTargetingEffectsResolved(DuelEngine engine, IEnumerable<CardInstance> targets, bool isPsctTarget)
        {
            if (!isPsctTarget || targets == null) return;
            var seen = new HashSet<CardInstance>();
            foreach (var target in targets)
                if (target != null && seen.Add(target))
                    NotifyTargetingEffectResolved(engine, target, true);
        }


        public static void NotifySpellResolved(DuelEngine engine, DuelistState who)
        {
            if (engine == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    TryPlaceSpellCounterOnSpell(engine, m);
                foreach (var st in side.SpellTrapsOnField())
                    TryPlaceSpellCounterOnSpell(engine, st);
            }

            engine.NotifyPublic();
        }

        static void TryPlaceSpellCounterOnSpell(DuelEngine engine, CardInstance card)
        {
            if (card?.Def == null || !card.FaceUp) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) return;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || !c.PlaceCounterOnSpellActivate) continue;
                var add = c.Amount > 0 ? c.Amount : 1;
                var max = c.CounterMax;
                card.Counters += add;
                if (max > 0 && card.Counters > max) card.Counters = max;
                if (max > 0) card.SpellCounterMax = max;
                engine.Log($"{card.Name}: Spell Counter (spell resolve) → {card.Counters}.");
            }
        }

        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingCoinCall) return false;
            var who = p.Controller;
            var card = p.Card;
            var tossHeads = engine.Rng.TossCoin();
            engine.Log(
                $"{who?.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(tossHeads ? "Heads" : "Tails")}.");
            var right = callHeads == tossHeads;
            var prog = CompiledEffectCache.GetOrCompile(card);
            engine.ClearPendingActivation();
            var dummy = false;
            foreach (var c in prog?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>())
            {
                if (c.Action == EffectActionKind.CoinCallDestroyOppOrSelf)
                    ResolveTimeWizard(engine, who, card, right);
                else if (c.Action == EffectActionKind.CoinCallDoubleOrHalveAtk)
                    ResolveGoddessCoin(engine, card, right);
            }

            if (card != null) card.EffectUsedThisTurn = true;
            engine.NotifyPublic();
            return true;
        }

        static bool StartCoinCall(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            EffectClause clause, bool autoPick, bool isMonster)
        {
            if (autoPick || !who.IsPlayer)
            {
                var callHeads = true;
                var pending = engine.PendingActivation;
                engine.SetPendingActivation(new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    IsMonsterEffect = isMonster,
                    UsesTextProgram = true,
                    AwaitingCoinCall = true
                });
                TryFinishCoinCall(engine, callHeads);
                return true;
            }

            var p = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                IsMonsterEffect = isMonster,
                UsesTextProgram = true,
                AwaitingCoinCall = true
            };
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static void ResolveTimeWizard(DuelEngine engine, DuelistState who, CardInstance source, bool right)
        {
            var opp = engine.OpponentOf(who);
            if (right)
            {
                engine.Log($"{source?.Name}: call right — destroy opponent's monsters.");
                foreach (var m in opp.MonstersOnField().ToList())
                    DestroyCard(engine, m, source);
                OfferDarkSage(engine, who);
                return;
            }

            engine.Log($"{source?.Name}: call wrong — destroy your monsters, take half ATK damage.");
            var atk = 0;
            foreach (var m in who.MonstersOnField().ToList())
            {
                if (m != null && m.FaceUp)
                    atk += Math.Max(0, m.CurrentAtk);
                DestroyCard(engine, m, source);
            }

            engine.ApplyEffectDamage(who, atk / 2, source?.Name);
        }

        static void ResolveGoddessCoin(DuelEngine engine, CardInstance card, bool right)
        {
            if (card == null) return;
            if (right)
            {
                var gain = card.CurrentAtk;
                card.UntilEndOfTurnAtk += gain;
                engine.Log($"{card.Name} ATK doubled this turn ({card.CurrentAtk}).");
            }
            else
            {
                var half = card.CurrentAtk / 2;
                card.UntilEndOfTurnAtk -= half;
                engine.Log($"{card.Name} ATK halved this turn ({card.CurrentAtk}).");
            }
        }

        static List<int> CollectExtraFusions(DuelEngine engine, DuelistState who)
        {
            var list = new List<int>();
            if (engine?.Database == null || who?.ExtraDeck == null) return list;
            foreach (var id in who.ExtraDeck)
            {
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsExtraDeck)
                    continue;
                if (def.type != null &&
                    def.type.IndexOf("Fusion", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !list.Contains(id))
                    list.Add(id);
            }

            return list;
        }

        static void SummonTokens(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            var dest = clause.TokenToOpponent ? engine.OpponentOf(who) : who;
            if (dest == null) return;
            var n = clause.TokenFillRemainingZones
                ? dest.MonsterZones.Count(z => z != null && z.Occupant == null)
                : (clause.TokenCount > 0 ? clause.TokenCount : 1);
            var pos = clause.SummonInDefense || clause.TokenToOpponent
                ? BattlePosition.Defense
                : BattlePosition.Attack;
            for (var i = 0; i < n; i++)
            {
                if (engine.FirstEmpty(dest.MonsterZones) < 0) break;
                var tok = engine.CreateToken(clause.TokenName, clause.TokenRace, clause.TokenAttribute,
                    clause.TokenLevel, clause.TokenAtk, clause.TokenDef);
                tok.CannotBeTributedForSummon = clause.TokenCannotTribute;
                tok.TokenDestroyedDamage = clause.TokenDestroyedDamage;
                tok.Position = pos;
                if (!engine.SpecialSummonToField(dest, tok, pos, true))
                    break;
                engine.Log($"Token Special Summoned: {tok.Name} ({tok.CurrentAtk}/{tok.CurrentDef}).");
            }
        }

        static bool MagicalHatsLegal(DuelEngine engine, DuelistState who, EffectClause clause,
            out string reason)
        {
            reason = "OK";
            if (engine == null || who == null)
            {
                reason = "Invalid context.";
                return false;
            }

            if (engine.Phase != DuelPhase.Battle || engine.TurnPlayer == who)
            {
                reason = "Only during your opponent's Battle Phase.";
                return false;
            }

            var empty = 0;
            var monsters = 0;
            foreach (var z in who.MonsterZones)
            {
                if (z == null) continue;
                if (z.Occupant == null) empty++;
                else monsters++;
            }

            if (monsters < 1)
            {
                reason = "Choose 1 monster in your Main Monster Zone.";
                return false;
            }

            if (clause != null && clause.HatsAreTokens)
            {
                if (empty < 1)
                {
                    reason = "Need an empty Main Monster Zone for a Magical Hat Token.";
                    return false;
                }

                return true;
            }

            if (empty < 2)
            {
                reason = "Need 2 empty Main Monster Zones.";
                return false;
            }

            if (CollectHatsDeckCards(engine, who).Count < 2)
            {
                reason = "Need 2 Spell/Trap Cards in your Deck.";
                return false;
            }

            return true;
        }

        static List<CardInstance> CollectHatsDeckCards(DuelEngine engine, DuelistState who)
        {
            var list = new List<CardInstance>();
            if (who?.Deck == null || engine?.Database == null) return list;
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!def.IsSpell && !def.IsTrap) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        static bool StartMagicalHats(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, EffectClause clause, bool autoPick)
        {
            var monsters = who.MonstersOnField().Where(m => m != null && !m.IsToken).ToList();
            var deckSt = new List<CardInstance>();
            // Deck is ID list — materialize Spell/Trap instances for the pending picker.
            if (who.Deck != null && engine.Database != null)
            {
                for (var i = 0; i < who.Deck.Count; i++)
                {
                    if (!engine.Database.TryGet(who.Deck[i], out var def) || def == null) continue;
                    if (!def.IsSpell && !def.IsTrap) continue;
                    deckSt.Add(engine.CreateCardInstance(who.Deck[i]));
                }
            }

            if (clause != null && clause.HatsAreTokens)
            {
                if (monsters.Count == 0) return false;
                if (autoPick || !who.IsPlayer)
                {
                    ResolveMagicalHatsAnime(engine, who, card, monsters[0], clause.HatTokenCount);
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.NotifyPublic();
                    return true;
                }
            }
            else if (monsters.Count == 0 || deckSt.Count < 2) return false;
            else if (autoPick || !who.IsPlayer)
            {
                ResolveMagicalHats(engine, who, card, monsters[0],
                    new List<CardInstance> { deckSt[0], deckSt[1] });
                FinishSpellTrap(engine, who, card, stays: false);
                engine.NotifyPublic();
                return true;
            }

            SpellTrapEffects.PlaceFaceUpForActivation(engine, who, card, fromHand);
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                UsesTextProgram = true,
                AwaitingHatsMonster = true,
                TargetKind = EffectTargetKind.EquipMonsterYouControl
            };
            pending.LegalTargets.AddRange(monsters);
            engine.SetPendingActivation(pending);
            engine.Log("Magical Hats: choose 1 monster in your Main Monster Zone.");
            engine.NotifyPublic();
            return true;
        }

        static void ResolveMagicalHats(DuelEngine engine, DuelistState who, CardInstance trap,
            CardInstance host, List<CardInstance> hats)
        {
            if (engine == null || who == null || host == null || hats == null || hats.Count < 2)
                return;
            host.FaceUp = false;
            host.Position = BattlePosition.Defense;
            var placed = new List<CardInstance> { host };
            for (var i = 0; i < 2; i++)
            {
                var hat = hats[i];
                who.Deck?.Remove(hat.CardId);
                hat.FaceUp = false;
                hat.Position = BattlePosition.Defense;
                hat.MagicalHatDummy = true;
                hat.OriginalAtkOverride = 0;
                hat.OriginalDefOverride = 0;
                hat.IsToken = false;
                if (!engine.SpecialSummonToField(who, hat, BattlePosition.Defense, false))
                    engine.SendCardToGrave(who, hat);
                else
                    placed.Add(hat);
            }

            ShuffleFaceDownMonsters(engine, who, placed);
            engine.Log("Magical Hats: 3 face-down Defense Position monsters shuffled.");
        }

        static void ResolveMagicalHatsAnime(DuelEngine engine, DuelistState who, CardInstance trap,
            CardInstance host, int tokenCount)
        {
            if (engine == null || who == null || host == null) return;
            host.FaceUp = false;
            host.Position = BattlePosition.Defense;
            var n = tokenCount > 0 ? tokenCount : 4;
            var placed = new List<CardInstance> { host };
            for (var i = 0; i < n; i++)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0) break;
                var tok = engine.CreateToken("Magical Hat Token", "Spellcaster", "DARK", 1, 0, 0);
                tok.MagicalHatDummy = true;
                tok.OriginalAtkOverride = 0;
                tok.OriginalDefOverride = 0;
                tok.IsToken = true;
                tok.FaceUp = false;
                tok.Position = BattlePosition.Defense;
                if (!engine.SpecialSummonToField(who, tok, BattlePosition.Defense, false))
                    break;
                placed.Add(tok);
            }

            ShuffleFaceDownMonsters(engine, who, placed);
            engine.Log($"Magical Hats: {placed.Count} face-down hats shuffled (anime).");
        }

        static void ShuffleFaceDownMonsters(DuelEngine engine, DuelistState who,
            List<CardInstance> cards)
        {
            if (who?.MonsterZones == null || cards == null || cards.Count < 2) return;
            var idxs = new List<int>();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var occ = who.MonsterZones[i]?.Occupant;
                if (occ != null && cards.Contains(occ))
                    idxs.Add(i);
            }

            if (idxs.Count < 2) return;
            for (var i = idxs.Count - 1; i > 0; i--)
            {
                var j = engine.Rng.Next(i + 1);
                var a = idxs[i];
                var b = idxs[j];
                var tmp = who.MonsterZones[a].Occupant;
                who.MonsterZones[a].Occupant = who.MonsterZones[b].Occupant;
                who.MonsterZones[b].Occupant = tmp;
            }
        }

        public static void DestroyMagicalHatDummies(DuelEngine engine)
        {
            if (engine == null) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField().ToList())
                {
                    if (m == null || !m.MagicalHatDummy) continue;
                    engine.SendCardToGrave(who, m);
                    engine.Log($"Magical Hats: {m.Name} is destroyed at the end of the Battle Phase.");
                }
            }
        }

        static bool MatchesVirusDestroyAtk(int atk, int thresh, bool leq)
        {
            if (thresh <= 0) thresh = 1500;
            return leq ? atk <= thresh : atk >= thresh;
        }

        static void ResolveCrushCardVirus(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            var label = source?.Name ?? "Virus";
            var thresh = clause != null && clause.VirusDestroyAtk > 0 ? clause.VirusDestroyAtk : 1500;
            var leq = clause != null && clause.VirusDestroyAtkLeq;
            foreach (var m in opp.MonstersOnField().ToList())
            {
                if (m == null) continue;
                var atk = m.FaceUp ? m.CurrentAtk : (m.Def?.atk ?? 0);
                if (!MatchesVirusDestroyAtk(atk, thresh, leq)) continue;
                DestroyCard(engine, m, source);
            }

            if (opp.Hand != null)
            {
                foreach (var h in opp.Hand.ToList())
                {
                    if (h?.Def == null || !h.Def.IsMonster) continue;
                    if (!MatchesVirusDestroyAtk(h.Def.atk, thresh, leq)) continue;
                    opp.Hand.Remove(h);
                    opp.Graveyard.Add(h);
                    engine.Log($"{label} destroys {h.Name} in the hand.");
                }
            }

            if (clause != null && clause.CrushCardNoDamageUntilNextTurn)
            {
                opp.NoDamageThroughTurnNumber = engine.TurnNumber + 1;
                engine.Log($"{opp.Name} takes no damage until the end of the next turn.");
            }

            if (clause != null && clause.CrushCardLingerOpponentEnds > 0)
            {
                opp.CrushCardLingerEndsRemaining = clause.CrushCardLingerOpponentEnds;
                opp.VirusLingerDestroyAtk = thresh;
                opp.VirusLingerDestroyAtkLeq = leq;
                engine.Log($"{label} lingers for {opp.CrushCardLingerEndsRemaining} of {opp.Name}'s turns.");
            }

            if (clause == null || !clause.CrushCardDeckDestroyUpTo)
                return;
            var deckHits = CollectVirusDeckMonsters(engine, opp, thresh, leq);
            if (deckHits.Count == 0) return;
            if (!opp.IsPlayer)
            {
                var n = Math.Min(3, deckHits.Count);
                for (var i = 0; i < n; i++)
                    DestroyVirusDeckPick(engine, opp, deckHits[i], label);
                return;
            }

            var pending = new PendingActivation
            {
                Controller = opp,
                Card = source,
                UsesTextProgram = true,
                AwaitingVirusDeck = true,
                TargetPicksAreUpTo = true,
                TargetPicksRemaining = 3,
                TargetKind = EffectTargetKind.MonsterInYourDeckFiltered
            };
            pending.LegalTargets.AddRange(deckHits);
            engine.SetPendingActivation(pending);
            engine.Log($"{label}: opponent may destroy up to 3 monsters with {thresh} or {(leq ? "less" : "more")} ATK in their Deck.");
            engine.NotifyPublic();
        }

        static List<CardInstance> CollectVirusDeckMonsters(DuelEngine engine, DuelistState who,
            int thresh, bool leq)
        {
            var list = new List<CardInstance>();
            if (who?.Deck == null || engine?.Database == null) return list;
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster)
                    continue;
                if (!MatchesVirusDestroyAtk(def.atk, thresh, leq)) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        static void DestroyVirusDeckPick(DuelEngine engine, DuelistState who, CardInstance pick,
            string label = null)
        {
            if (who?.Deck == null || pick == null) return;
            who.Deck.Remove(pick.CardId);
            who.Graveyard.Add(pick);
            engine.Log($"{label ?? "Virus"} destroys {pick.Name} in the Deck.");
        }

        public static void OfferDarkSage(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            CardInstance dm = null;
            foreach (var m in who.MonstersOnField())
            {
                if (m != null && m.CardId == OfficialEffectRegistry.DarkMagicianId)
                {
                    dm = m;
                    break;
                }
            }

            if (dm == null) return;
            var sages = new List<CardInstance>();
            if (who.Hand != null)
            {
                foreach (var h in who.Hand)
                    if (h != null && h.CardId == OfficialEffectRegistry.DarkSageId)
                        sages.Add(h);
            }

            if (who.Deck != null)
            {
                foreach (var id in who.Deck)
                    if (id == OfficialEffectRegistry.DarkSageId)
                        sages.Add(engine.CreateCardInstance(id));
            }

            if (sages.Count == 0) return;
            if (!who.IsPlayer)
            {
                FinishDarkSageSummon(engine, who, sages[0]);
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = sages[0],
                UsesTextProgram = true,
                AwaitingDarkSage = true,
                IsMonsterEffect = true,
                TargetKind = EffectTargetKind.MonsterInYourDeckToSummon
            };
            pending.LegalTargets.AddRange(sages);
            engine.SetPendingActivation(pending);
            engine.Log("Time Wizard called right — you may Tribute Dark Magician to Special Summon Dark Sage.");
            engine.NotifyPublic();
        }

        static void FinishDarkSageSummon(DuelEngine engine, DuelistState who, CardInstance sage)
        {
            if (engine == null || who == null || sage == null) return;
            CardInstance dm = null;
            foreach (var m in who.MonstersOnField())
            {
                if (m != null && m.CardId == OfficialEffectRegistry.DarkMagicianId)
                {
                    dm = m;
                    break;
                }
            }

            if (dm == null)
            {
                engine.Log("Dark Sage: no Dark Magician to Tribute.");
                return;
            }

            engine.SendCardToGrave(who, dm);
            if (who.Hand != null && who.Hand.Contains(sage))
                who.Hand.Remove(sage);
            else
                who.Deck?.Remove(OfficialEffectRegistry.DarkSageId);
            if (!engine.SpecialSummonToField(who, sage, BattlePosition.Attack, true))
            {
                engine.SendCardToGrave(who, sage);
                engine.Log("Dark Sage: Special Summon failed.");
            }
        }

        public static void ApplyCrushCardOnDraw(DuelEngine engine, DuelistState who, CardInstance drawn)
        {
            if (engine == null || who == null || drawn?.Def == null) return;
            if (who.CrushCardLingerEndsRemaining <= 0) return;
            if (!drawn.Def.IsMonster) return;
            var thresh = who.VirusLingerDestroyAtk > 0 ? who.VirusLingerDestroyAtk : 1500;
            if (!MatchesVirusDestroyAtk(drawn.Def.atk, thresh, who.VirusLingerDestroyAtkLeq))
                return;
            who.Hand?.Remove(drawn);
            who.Graveyard.Add(drawn);
            engine.Log($"Virus linger destroys drawn {drawn.Name}.");
        }

        public static void TickCocoonCounters(DuelEngine engine, DuelistState who)
        {
            if (who == null) return;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || st.EquippedTo == null) continue;
                var host = st.EquippedTo;
                if (host.IsNamed("Petit Moth"))
                    st.EquipTurnCounter++;
                else
                    st.EquipTurnCounter = 0;
            }
        }

        static void EquipCardTo(DuelEngine engine, DuelistState from, CardInstance equip,
            CardInstance host, int atkBonus, EffectClause clause = null)
        {
            if (equip == null || host == null) return;
            var owner = engine.ControllerOf(equip) ?? from;
            if (owner != null)
            {
                if (owner.TryFindMonster(equip, out var mi))
                    owner.MonsterZones[mi].Occupant = null;
                if (owner.Hand != null && owner.Hand.Contains(equip))
                {
                    var zi = engine.FirstEmptySpellTrap(owner);
                    if (zi < 0)
                    {
                        engine.SendCardToGrave(owner, equip);
                        engine.Log($"{equip.Name}: no Spell/Trap Zone — sent to GY.");
                        return;
                    }

                    owner.Hand.Remove(equip);
                    owner.SpellTrapZones[zi].Occupant = equip;
                    equip.FaceUp = true;
                    equip.SetThisTurn = false;
                }
            }

            equip.EquippedTo = host;
            equip.EquipTurnCounter = 0;
            if (!host.Equips.Contains(equip))
                host.Equips.Add(equip);
            if (clause != null && clause.ReplaceHostOriginalAtkDef && equip.Def != null)
            {
                host.OriginalAtkOverride = equip.Def.atk;
                host.OriginalDefOverride = equip.Def.def;
            }

            engine.Log($"{equip.Name} is equipped to {host.Name}.");
        }

        static void UnequipAndSummon(DuelEngine engine, DuelistState who, CardInstance equip)
        {
            if (equip?.EquippedTo == null) return;
            var host = equip.EquippedTo;
            host.Equips.Remove(equip);
            equip.EquippedTo = null;
            if (engine.SpecialSummonToField(who, equip, BattlePosition.Attack, true))
                engine.Log($"{equip.Name} unequipped and Special Summoned.");
            else
                engine.SendCardToGrave(who, equip);
        }

        static bool DefMatchesSummonFilter(CardDef def, EffectClause clause)
        {
            if (def == null || clause == null || !def.IsMonster || def.IsExtraDeck) return false;
            if (!string.IsNullOrEmpty(clause.NamedCard))
            {
                if (clause.NamedCardIsSeries)
                {
                    var n = clause.NamedCard;
                    var inName = def.name != null &&
                                 def.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    var inArch = def.archetype != null &&
                                 def.archetype.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!inName && !inArch) return false;
                }
                else
                {
                    var primary = string.Equals(def.name, clause.NamedCard,
                        System.StringComparison.OrdinalIgnoreCase);
                    var alt = !string.IsNullOrEmpty(clause.AltNamedCard) &&
                              string.Equals(def.name, clause.AltNamedCard,
                                  System.StringComparison.OrdinalIgnoreCase);
                    if (!primary && !alt) return false;
                }
            }

            if (!string.IsNullOrEmpty(clause.RaceFilter) &&
                (def.race == null ||
                 def.race.IndexOf(clause.RaceFilter, System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (!string.IsNullOrEmpty(clause.AttributeFilter) &&
                (def.attribute == null ||
                 !def.attribute.Equals(clause.AttributeFilter, System.StringComparison.OrdinalIgnoreCase)))
                return false;
            if (clause.AmountIsAtkMax && (def.atk < 0 || def.atk > clause.Amount)) return false;
            if (clause.AmountIsDefMax && (def.def < 0 || def.def > clause.Amount)) return false;
            if (clause.AmountIsLevel && clause.Amount > 0 && def.level > clause.Amount) return false;
            return true;
        }

        static void SendTopOfDeckToGy(DuelEngine engine, DuelistState who, int n)
        {
            if (engine == null || who?.Deck == null) return;
            for (var i = 0; i < n && who.Deck.Count > 0; i++)
            {
                var id = who.Deck[0];
                who.Deck.RemoveAt(0);
                var inst = engine.CreateCardInstance(id);
                who.Graveyard.Add(inst);
                engine.Log($"{who.Name} sends {inst.Name} from the Deck to the GY.");
            }
        }

        static void TrySpecialSummonFromDeck(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance chosenTarget)
        {
            if (engine == null || who == null || clause == null || who.Deck == null || engine.Database == null) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0) return;
            var index = -1;
            var id = 0;
            if (chosenTarget != null)
            {
                for (var i = 0; i < who.Deck.Count; i++)
                    if (who.Deck[i] == chosenTarget.CardId) { index = i; id = who.Deck[i]; break; }
            }
            else
            {
                for (var i = 0; i < who.Deck.Count; i++)
                {
                    if (!engine.Database.TryGet(who.Deck[i], out var def) || !DefMatchesSummonFilter(def, clause)) continue;
                    index = i; id = who.Deck[i]; break;
                }
            }
            if (index < 0 || !engine.Database.TryGet(id, out var selected) || !DefMatchesSummonFilter(selected, clause)) return;
            who.Deck.RemoveAt(index);
            var inst = chosenTarget ?? engine.CreateCardInstance(id);
            if (!engine.SpecialSummonToField(who, inst, clause.SummonInDefense ? BattlePosition.Defense : BattlePosition.Attack, true))
                who.Deck.Insert(index, id);
            else
                engine.Log($"Special Summoned {inst.Name} from Deck.");
        }

        static void TrySpecialSummonNamed(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source = null)
        {
            if (engine == null || who == null || clause == null) return;
            var hasName = !string.IsNullOrEmpty(clause.NamedCard);
            var hasFilter = !string.IsNullOrEmpty(clause.RaceFilter) ||
                            !string.IsNullOrEmpty(clause.AttributeFilter) ||
                            clause.AmountIsAtkMax || clause.AmountIsDefMax ||
                            clause.AmountIsLevel;
            if (!hasName && !hasFilter) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log("Special Summon failed — no zone.");
                return;
            }

            if (clause.FromHand && who.Hand != null)
            {
                var hit = who.Hand.FirstOrDefault(c =>
                    c?.Def != null && DefMatchesSummonFilter(c.Def, clause));
                if (hit != null)
                {
                    who.Hand.Remove(hit);
                    if (engine.SpecialSummonToField(who, hit, BattlePosition.Attack, true))
                    {
                        engine.Log($"Special Summoned {hit.Name} from hand.");
                        return;
                    }

                    who.Hand.Add(hit);
                }
            }

            if (clause.FromDeck && who.Deck != null && engine.Database != null)
            {
                for (var i = 0; i < who.Deck.Count; i++)
                {
                    var id = who.Deck[i];
                    if (!engine.Database.TryGet(id, out var def) || !DefMatchesSummonFilter(def, clause))
                        continue;
                    who.Deck.RemoveAt(i);
                    var inst = engine.CreateCardInstance(id);
                    if (engine.SpecialSummonToField(who, inst, BattlePosition.Attack, true))
                    {
                        engine.Log($"Special Summoned {inst.Name} from Deck.");
                        return;
                    }

                    who.Deck.Insert(i, id);
                    break;
                }
            }

            if (clause.FromGrave && who.Graveyard != null)
            {
                var hit = who.Graveyard.FirstOrDefault(c =>
                    c != null &&
                    (c.IsNamed(clause.NamedCard) ||
                     string.Equals(c.Name, clause.NamedCard, System.StringComparison.OrdinalIgnoreCase) ||
                     (!string.IsNullOrEmpty(clause.AltNamedCard) &&
                      (c.IsNamed(clause.AltNamedCard) ||
                       string.Equals(c.Name, clause.AltNamedCard,
                           System.StringComparison.OrdinalIgnoreCase)))));
                if (hit != null)
                {
                    if (!CanSpecialSummonFromGyTarget(hit) ||
                        ContinuousProtections.GyMovementLocked(engine, source, hit))
                    {
                        engine.Log($"{hit.Name} cannot leave the GY for this Special Summon.");
                        return;
                    }
                    who.Graveyard.Remove(hit);
                    if (engine.SpecialSummonToField(who, hit, BattlePosition.Attack, true))
                    {
                        engine.Log($"Special Summoned {hit.Name} from GY.");
                        return;
                    }

                    who.Graveyard.Add(hit);
                }
            }

            engine.Log($"Special Summon: no \"{clause.NamedCard}\" in the listed locations.");
        }

        static void AddNamedFromDeck(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(clause?.NamedCard))
                return;
            var max = clause.Amount > 0 ? clause.Amount : 1;
            var added = 0;
            for (var n = 0; n < max; n++)
            {
                var idx = -1;
                for (var i = 0; i < who.Deck.Count; i++)
                {
                    if (!engine.Database.TryGet(who.Deck[i], out var def) || def == null) continue;
                    if (!NameMatchesQuoted(def, clause.NamedCard, clause.NamedCardIsSeries))
                        continue;
                    idx = i;
                    break;
                }

                if (idx < 0) break;
                var id = who.Deck[idx];
                who.Deck.RemoveAt(idx);
                var inst = engine.CreateCardInstance(id);
                who.Hand.Add(inst);
                added++;
                engine.Log($"Added {inst.Name} from Deck to hand.");
            }

            if (added == 0)
                engine.Log($"Search: no \"{clause.NamedCard}\" in Deck.");
        }

        /// <summary>
        /// Field controller, else GY/hand owner. ControllerOf is field-only;
        /// Flip GY-banish (Witch Doctor of Chaos) targets cards not on the field.
        /// </summary>
        static DuelistState OwnerOf(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || card == null) return null;
            var field = engine.ControllerOf(card);
            if (field != null) return field;
            if (who != null)
            {
                if (who.Graveyard != null && who.Graveyard.Contains(card)) return who;
                if (who.Hand != null && who.Hand.Contains(card)) return who;
                if (who.Banished != null && who.Banished.Contains(card)) return who;
            }
            var opp = engine.OpponentOf(who);
            if (opp != null)
            {
                if (opp.Graveyard != null && opp.Graveyard.Contains(card)) return opp;
                if (opp.Hand != null && opp.Hand.Contains(card)) return opp;
                if (opp.Banished != null && opp.Banished.Contains(card)) return opp;
            }
            return null;
        }

        static bool TryContinueDualOrUpTo(DuelEngine engine, PendingActivation p, DuelistState who,
            CardInstance card, CardInstance target, bool monsterIgnition)
        {
            var prog = CompiledEffectCache.GetOrCompile(card);
            var activate = prog?.ClausesFor(EffectTiming.Activate);
            var dual = activate?.FirstOrDefault(c => c != null && c.RequiresSecondTarget);
            if (dual != null && dual.ControllerTargetCount > 0 && dual.OpponentTargetCount > 0)
            {
                if (!p.ChosenTargets.Contains(target))
                    p.ChosenTargets.Add(target);
                p.LegalTargets.Remove(target);
                if (p.TargetPicksRemaining > 0)
                    p.TargetPicksRemaining--;
                if (!p.AwaitingSecondTarget)
                {
                    if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                    {
                        engine.Log($"{card.Name}: choose another of your monsters ({p.TargetPicksRemaining} remaining).");
                        engine.NotifyPublic();
                        return true;
                    }

                    var second = CollectTargetsForZone(engine, who, dual, card, dual.SecondZone);
                    second.RemoveAll(x => p.ChosenTargets.Contains(x));
                    if (second.Count == 0)
                    {
                        engine.Log($"{card.Name}: no opponent monster — activation fails.");
                        engine.ClearPendingActivation();
                        if (!monsterIgnition)
                            FinishSpellTrap(engine, who, card, stays: false);
                        engine.NotifyPublic();
                        return true;
                    }

                    p.AwaitingSecondTarget = true;
                    p.TargetPicksRemaining = dual.OpponentTargetCount;
                    p.TargetKind = MapTargetKind(SecondTargetClause(dual));
                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(second);
                    engine.Log($"{card.Name}: choose {dual.OpponentTargetCount} opponent monster(s).");
                    engine.NotifyPublic();
                    return true;
                }

                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    engine.Log($"{card.Name}: choose another opponent monster ({p.TargetPicksRemaining} remaining).");
                    engine.NotifyPublic();
                    return true;
                }

                var picks = p.ChosenTargets.ToList();
                engine.ClearPendingActivation();
                var dummyProng = false;
                foreach (var pick in picks)
                    ApplyClause(engine, who, card, dual, pick, ref dummyProng, ref dummyProng);
                if (monsterIgnition)
                    card.EffectUsedThisTurn = dual.OncePerTurn || card.EffectUsedThisTurn;
                else
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                NotifyTargetingEffectsResolved(engine, picks, dual.IsPsctTarget);
                engine.NotifyPublic();
                return true;
            }

            if (dual == null && p.TargetPicksAreUpTo)
            {
                if (!p.ChosenTargets.Contains(target))
                    p.ChosenTargets.Add(target);
                p.LegalTargets.Remove(target);
                if (p.TargetPicksRemaining > 0)
                    p.TargetPicksRemaining--;
                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    engine.Log($"{card.Name}: choose another target ({p.TargetPicksRemaining} remaining).");
                    engine.NotifyPublic();
                    return true;
                }

                var picks = p.ChosenTargets.Count > 0
                    ? p.ChosenTargets.ToList()
                    : new List<CardInstance> { target };
                engine.ClearPendingActivation();
                var dummyUp = false;
                foreach (var c in activate)
                {
                    foreach (var pick in picks)
                    {
                        if (c.RequiresTargetChoice)
                            ApplyClause(engine, who, card, c, pick, ref dummyUp, ref dummyUp);
                        else if (pick == picks[0])
                            ApplyClause(engine, who, card, c, null, ref dummyUp, ref dummyUp);
                    }
                }

                if (monsterIgnition)
                    card.EffectUsedThisTurn = activate.Exists(x => x != null && x.OncePerTurn);
                else
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                NotifyTargetingEffectsResolved(engine, picks,
                    activate.Exists(c => c != null && c.IsPsctTarget));
                engine.NotifyPublic();
                return true;
            }

            if (dual != null)
            {
                if (!p.AwaitingSecondTarget)
                {
                    var second = CollectTargetsForZone(engine, who, dual, card, dual.SecondZone);
                    second.RemoveAll(x => x == target);
                    if (second.Count == 0)
                    {
                        engine.Log($"{card.Name}: no second target — activation fails.");
                        engine.ClearPendingActivation();
                        if (!monsterIgnition)
                            FinishSpellTrap(engine, who, card, stays: false);
                        engine.NotifyPublic();
                        return true;
                    }

                    p.LockedTarget = target;
                    p.AwaitingSecondTarget = true;
                    p.TargetKind = MapTargetKind(SecondTargetClause(dual));
                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(second);
                    engine.Log($"{card.Name}: choose the second target.");
                    engine.NotifyPublic();
                    return true;
                }

                var first = p.LockedTarget;
                engine.ClearPendingActivation();
                var dummy = false;
                foreach (var c in activate)
                {
                    if (c.RequiresTargetChoice)
                        ApplyClause(engine, who, card, c, first, ref dummy, ref dummy);
                    else
                        ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                }
                ApplySecondTarget(engine, who, card, dual, target);
                if (monsterIgnition)
                    card.EffectUsedThisTurn = dual.OncePerTurn || card.EffectUsedThisTurn;
                else
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                NotifyTargetingEffectsResolved(engine, new[] { first, target }, dual.IsPsctTarget);
                engine.NotifyPublic();
                return true;
            }

            return false;
        }

        static EffectClause SecondTargetClause(EffectClause c)
        {
            return new EffectClause
            {
                Action = c.SecondAction,
                Zone = c.SecondZone,
                RequiresTargetChoice = true
            };
        }

        static void ApplySecondTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance second)
        {
            if (engine == null || c == null || second == null) return;
            var dummy = false;
            var op = new EffectClause
            {
                Action = c.SecondAction == EffectActionKind.None
                    ? EffectActionKind.Banish
                    : c.SecondAction,
                Zone = c.SecondZone,
                RequiresTargetChoice = true
            };
            ApplyClause(engine, who, source, op, second, ref dummy, ref dummy);
        }

        static List<CardInstance> CollectTargetsForZone(DuelEngine engine, DuelistState who,
            EffectClause c, CardInstance except, EffectZoneFilter zone)
        {
            if (c == null) return new List<CardInstance>();
            var saved = c.Zone;
            var savedRace = c.RaceFilter;
            c.Zone = zone;
            // Book of Life: first target is Race in your GY; second is any opp GY monster.
            if (zone == c.SecondZone && zone != saved)
                c.RaceFilter = null;
            var list = CollectTargets(engine, who, c, except);
            c.Zone = saved;
            c.RaceFilter = savedRace;
            return list;
        }

        static List<CardInstance> CollectTargets(DuelEngine engine, DuelistState who, EffectClause c,
            CardInstance except, int costNumeric = 0)
        {
            var list = new List<CardInstance>();
            var opp = engine.OpponentOf(who);
            switch (c.Zone)
            {
                case EffectZoneFilter.FieldSpellTraps:
                    foreach (var st in who.SpellTrapsOnField())
                        list.Add(st);
                    foreach (var st in opp.SpellTrapsOnField())
                        list.Add(st);
                    if (who.FieldSpellZone?.Occupant != null)
                        list.Add(who.FieldSpellZone.Occupant);
                    if (opp.FieldSpellZone?.Occupant != null)
                        list.Add(opp.FieldSpellZone.Occupant);
                    if (except != null && except.Def != null &&
                        (except.Def.IsSpell || except.Def.IsTrap) &&
                        !list.Contains(except))
                        list.Add(except);
                    if (!string.IsNullOrEmpty(c.DestroyIfType))
                        list.RemoveAll(t => t != null && t.FaceUp && !CardMatchesTypeWord(t, c.DestroyIfType));
                    break;
                case EffectZoneFilter.EitherGyMonsters:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    break;
                case EffectZoneFilter.OppFaceUpMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (c.Action == EffectActionKind.EffectDamageBothFromOriginalAtk &&
                            m.CurrentAtk > opp.LifePoints) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.OppDefensePositionMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null || m.Position != BattlePosition.Defense) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.FieldAnyMonster:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == except && c.Action != EffectActionKind.ReturnToHand) continue;
                        if (c.Action == EffectActionKind.Banish && !m.FaceUp) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == except && c.Action != EffectActionKind.ReturnToHand) continue;
                        if (c.Action == EffectActionKind.Banish && !m.FaceUp) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.ControllerGyMonsters:
                    foreach (var g in who.Graveyard)
                    {
                        if (g?.Def == null || !g.Def.IsMonster || g.Def.IsExtraDeck) continue;
                        if (c.RequiresNormalMonster &&
                            (g.Def.type == null ||
                             g.Def.type.IndexOf("Normal", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                             g.Def.type.IndexOf("Effect", System.StringComparison.OrdinalIgnoreCase) >= 0))
                            continue;
                        if (c.AmountIsLevel && c.Amount > 0 && g.Level > c.Amount)
                            continue;
                        list.Add(g);

                    }
                    break;
                case EffectZoneFilter.ControllerMonsters:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == except && c.TributeExceptThis) continue;
                        if (!string.IsNullOrEmpty(c.NamedCard) && !m.IsNamed(c.NamedCard) &&
                            (m.Name == null ||
                             !string.Equals(m.Name, c.NamedCard, System.StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (!string.IsNullOrEmpty(c.EquipHostName))
                        {
                            var hostOk = m.IsNamed(c.EquipHostName) ||
                                         (m.Name != null &&
                                          string.Equals(m.Name, c.EquipHostName,
                                              System.StringComparison.OrdinalIgnoreCase)) ||
                                         (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                          (m.IsNamed(c.AltNamedCard) ||
                                           (m.Name != null &&
                                            string.Equals(m.Name, c.AltNamedCard,
                                                System.StringComparison.OrdinalIgnoreCase))));
                            if (!hostOk && c.NamedCardIsSeries && m.Name != null &&
                                (m.Name.IndexOf(c.EquipHostName,
                                     System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                  m.Name.IndexOf(c.AltNamedCard,
                                      System.StringComparison.OrdinalIgnoreCase) >= 0)))
                                hostOk = true;
                            if (!hostOk) continue;
                        }
                        if (c.RequiresNormalMonster && (m.Def == null || !m.Def.IsNormalMonster))
                            continue;
                        if (c.TributeChosenTarget && m.IsToken) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.OppAnyCardOnField:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var st in opp.SpellTrapsOnField())
                        list.Add(st);
                    break;
                case EffectZoneFilter.ControllerGySpells:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsSpell) list.Add(g);
                    break;
                case EffectZoneFilter.ControllerGyTraps:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsTrap) list.Add(g);
                    break;
                case EffectZoneFilter.OppGyMonsters:
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    break;
                case EffectZoneFilter.OppMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;
                case EffectZoneFilter.ControllerDeckMonsters:
                    AddUniqueDeck(engine, who, list, def => DefMatchesSummonFilter(def, c));
                    break;
                case EffectZoneFilter.ControllerHandMonsters:
                    if (who.Hand != null)
                    {
                        foreach (var h in who.Hand)
                        {
                            if (h == except || h?.Def == null || !h.Def.IsMonster || h.Def.IsExtraDeck)
                                continue;
                            list.Add(h);
                        }
                    }
                    break;
                case EffectZoneFilter.AnyCardOnField:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var m in opp.MonstersOnField())
                    {
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var st in who.SpellTrapsOnField())
                        list.Add(st);
                    foreach (var st in opp.SpellTrapsOnField())
                        list.Add(st);
                    if (who.PendulumZones != null)
                    {
                        foreach (var z in who.PendulumZones)
                            if (z?.Occupant != null) list.Add(z.Occupant);
                    }

                    if (opp.PendulumZones != null)
                    {
                        foreach (var z in opp.PendulumZones)
                            if (z?.Occupant != null) list.Add(z.Occupant);
                    }

                    break;
                case EffectZoneFilter.ControllerDeckSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsSpell && !def.IsTrap);
                    break;
                case EffectZoneFilter.DeckFieldSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsFieldSpell);
                    break;
                case EffectZoneFilter.DeckEquipSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsEquipSpell);
                    break;
                case EffectZoneFilter.DeckMonstersRaceLevelLeq:
                    AddUniqueDeck(engine, who, list, def =>
                    {
                        if (def == null || !def.IsMonster || def.IsExtraDeck) return false;
                        var cap = c.Amount > 0 ? c.Amount : 4;
                        if (def.level > cap) return false;
                        if (string.IsNullOrEmpty(c.RaceFilter)) return true;
                        return def.race != null &&
                               def.race.IndexOf(c.RaceFilter,
                                   System.StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                    break;
                case EffectZoneFilter.DeckMonstersAtkLeq:
                    AddUniqueDeck(engine, who, list, def =>

                    {
                        if (def == null || !def.IsMonster || def.IsExtraDeck) return false;
                        var cap = c.Amount > 0 ? c.Amount : 1500;
                        if (c.AmountIsDefMax)
                            return def.def >= 0 && def.def <= cap;
                        return def.atk >= 0 && def.atk <= cap;
                    });
                    break;
            }
            if (c.Action == EffectActionKind.SpecialSummonFromGy &&
                (c.Zone == EffectZoneFilter.EitherGyMonsters ||
                 c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                 c.Zone == EffectZoneFilter.OppGyMonsters))
                list.RemoveAll(t => !CanSpecialSummonFromGyTarget(t));


            if (c.Zone == EffectZoneFilter.EitherGyMonsters ||
                c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                c.Zone == EffectZoneFilter.OppGyMonsters ||
                c.Zone == EffectZoneFilter.ControllerGySpells ||
                c.Zone == EffectZoneFilter.ControllerGyTraps)
                list.RemoveAll(t =>
                    ContinuousProtections.CannotTargetCardInGy(engine, except, t));

            if (c.RequiresAtkLeqCost && costNumeric > 0)
                list.RemoveAll(t => t == null || t.CurrentAtk > costNumeric);

            if (!string.IsNullOrEmpty(c.RaceFilter) &&
                c.Zone != EffectZoneFilter.DeckMonstersRaceLevelLeq &&
                c.Zone != EffectZoneFilter.DeckFieldSpells)
                list.RemoveAll(t => t?.Def?.race == null ||
                                    t.Def.race.IndexOf(c.RaceFilter,
                                        System.StringComparison.OrdinalIgnoreCase) < 0);
            if (!string.IsNullOrEmpty(c.ExceptNamedCard))
                list.RemoveAll(t => t != null && t.IsNamed(c.ExceptNamedCard));
            if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                c.Action == EffectActionKind.EquipThisToTarget)
                list.RemoveAll(t => t?.Def?.attribute == null ||
                                    !t.Def.attribute.Equals(c.AttributeFilter,
                                        System.StringComparison.OrdinalIgnoreCase));
            if (c.Action == EffectActionKind.EquipThisToTarget)
            {
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp);
                if (!string.IsNullOrEmpty(c.NamedCard))
                    list.RemoveAll(t => t == null || !t.IsNamed(c.NamedCard));
            }
            if (c.Action == EffectActionKind.SetTargetFaceDownDefense)
                list.RemoveAll(t => t == null || !t.FaceUp);
            if (c.Action == EffectActionKind.ChangeBattlePosition)
            {
                if (c.ForceAttackPosition)
                    list.RemoveAll(t => t == null || t.Position != BattlePosition.Defense);
                else if (c.ForceDefensePosition)
                    list.RemoveAll(t => t == null || !t.FaceUp ||
                                        t.Position != BattlePosition.Attack);
                else
                    list.RemoveAll(t => t == null || !t.FaceUp);
            }

            if (c.RequiresAttackPosition)
                list.RemoveAll(t => t == null || !t.FaceUp || t.Position != BattlePosition.Attack);
            if (c.Action == EffectActionKind.GainAtkDefUntilEndOfTurn)
                list.RemoveAll(t => t == null || !t.FaceUp || t.Def == null || !t.Def.IsMonster);

            // Fissure / Smashing Ground / Hammer Shot: only the extreme-stat face-ups are legal.
            if (c.PickLowestAtk && list.Count > 0)
            {
                var best = list.Where(m => m != null && m.FaceUp).Select(m => m.CurrentAtk).DefaultIfEmpty(0).Min();
                list.RemoveAll(m => m == null || !m.FaceUp || m.CurrentAtk != best);
            }
            if (c.PickHighestDef && list.Count > 0)
            {
                var best = list.Where(m => m != null && m.FaceUp).Select(m => m.CurrentDef).DefaultIfEmpty(0).Max();
                list.RemoveAll(m => m == null || !m.FaceUp || m.CurrentDef != best);
            }
            if (c.PickHighestAtk && list.Count > 0)
            {
                var best = list.Where(m => m != null && m.FaceUp).Select(m => m.CurrentAtk).DefaultIfEmpty(0).Max();
                list.RemoveAll(m => m == null || !m.FaceUp || m.CurrentAtk != best);
            }

            if (c.FaceUpContinuousSpellsOnly)
                list.RemoveAll(t => t == null || !t.FaceUp || t.Def == null ||
                                    !t.Def.IsSpell || !t.Def.IsContinuousSpellOrTrap);
            if (c.FaceUpContinuousTrapsOnly)
                list.RemoveAll(t => t == null || !t.FaceUp || t.Def == null ||
                                    !t.Def.IsTrap || !t.Def.IsContinuousSpellOrTrap);

            if (c.AmountIsLevel && c.Amount > 0)
                list.RemoveAll(t => t == null || t.Level > c.Amount);

            if (c.Zone == EffectZoneFilter.FieldSpellTraps)
                return SpellTrapEffects.FilterActivatingCardFromOffer(list, except);
            return list;
        }

        static bool MatchesRaceOrUnfiltered(CardInstance m, EffectClause c)
        {
            if (m == null) return false;
            if (c == null || string.IsNullOrEmpty(c.RaceFilter)) return true;
            return m.Def?.race != null &&
                   m.Def.race.IndexOf(c.RaceFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool CardMatchesTypeWord(CardInstance card, string typeWord)
        {
            if (card?.Def == null || string.IsNullOrEmpty(typeWord)) return false;
            if (typeWord.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                return card.Def.IsSpell;
            if (typeWord.Equals("Trap", StringComparison.OrdinalIgnoreCase))
                return card.Def.IsTrap;
            return false;
        }

        static bool PassesDestroyIfType(DuelEngine engine, CardInstance card, EffectClause clause)
        {
            if (clause == null || string.IsNullOrEmpty(clause.DestroyIfType)) return true;
            if (card == null) return false;
            if (!card.FaceUp)
                engine.Log($"Reveals {card.Name}.");
            if (CardMatchesTypeWord(card, clause.DestroyIfType))
                return true;
            engine.Log($"{card.Name} is not a {clause.DestroyIfType} — returned to its original position.");
            return false;
        }

        /// <summary>Exodia family: win if this card plus the printed named pieces are all in hand.</summary>
        public static void CheckHandWinConditions(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who?.Hand == null) continue;
                foreach (var card in who.Hand)
                {
                    if (card?.Def == null) continue;
                    var prog = CompiledEffectCache.GetOrCompile(card.Def);
                    if (prog == null) continue;
                    foreach (var c in prog.ClauseList)
                    {
                        if (c == null || c.Action != EffectActionKind.ExodiaWinStyle)
                            continue;
                        if (!HandHasNamedWinSet(who, card, c)) continue;
                        engine.Log($"{who.Name} assembled {card.Name} — win!");
                        engine.EndGamePublic(who);
                        return;
                    }
                }
            }
        }

        static bool HandHasNamedWinSet(DuelistState who, CardInstance self, EffectClause clause)
        {
            if (who?.Hand == null || self == null) return false;
            if (!who.Hand.Contains(self)) return false;
            if (string.IsNullOrEmpty(clause.NamedCard)) return false;
            foreach (var name in clause.NamedCard.Split('|'))
            {
                var piece = name.Trim();
                if (piece.Length == 0) continue;
                var found = false;
                foreach (var h in who.Hand)
                {
                    if (h != null && h.IsNamed(piece))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        static IEnumerable<CardInstance> CollectAllMatching(DuelEngine engine, DuelistState who,
            EffectClause c)
        {
            var opp = engine.OpponentOf(who);
            switch (c.Zone)
            {
                case EffectZoneFilter.FieldMonsters:
                    if (c.Side == EffectSide.Opponent || c.Side == EffectSide.Both)
                        foreach (var m in opp.MonstersOnField())
                            if (MatchesRaceOrUnfiltered(m, c)) yield return m;
                    if (c.Side == EffectSide.Controller || c.Side == EffectSide.Both)
                        foreach (var m in who.MonstersOnField())
                            if (MatchesRaceOrUnfiltered(m, c)) yield return m;
                    break;
                case EffectZoneFilter.FieldSpellTraps:
                    if (c.Side == EffectSide.Opponent || c.Side == EffectSide.Both)
                        foreach (var st in opp.SpellTrapsOnField()) yield return st;
                    if (c.Side == EffectSide.Controller || c.Side == EffectSide.Both)
                        foreach (var st in who.SpellTrapsOnField()) yield return st;
                    break;
                case EffectZoneFilter.FieldSpellsOnField:
                    if (who?.FieldSpellZone?.Occupant != null)
                        yield return who.FieldSpellZone.Occupant;
                    if (opp?.FieldSpellZone?.Occupant != null)
                        yield return opp.FieldSpellZone.Occupant;
                    break;
                case EffectZoneFilter.OppAttackPositionMonsters:
                    foreach (var m in opp.MonstersOnField())
                        if (m.FaceUp && m.Position == BattlePosition.Attack)
                            yield return m;
                    break;
                case EffectZoneFilter.OppDefensePositionMonsters:
                    foreach (var m in opp.MonstersOnField())
                        if (m.Position == BattlePosition.Defense)
                            yield return m;
                    break;
                case EffectZoneFilter.AttackingMonster:
                    if (engine.TryGetBattlingPair(out var atk, out _) && atk != null)
                        yield return atk;
                    break;
                case EffectZoneFilter.OpponentBattlingMonster:
                    var bc = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                    if (bc != null) yield return bc;
                    break;
            }
        }

        static CardInstance AutoPick(EffectClause c, List<CardInstance> targets, DuelistState who,
            DuelEngine engine)
        {
            var opp = engine.OpponentOf(who);
            if (c.PickLowestAtk)
                return targets.OrderBy(t => t.CurrentAtk).First();
            if (c.PickHighestDef)
                return targets.OrderByDescending(t => t.CurrentDef).First();
            if (c.PickHighestAtk)
                return targets.OrderByDescending(t => t.CurrentAtk).First();
            switch (c.Zone)
            {
                case EffectZoneFilter.EitherGyMonsters:
                    return targets
                        .OrderByDescending(t => who.Graveyard.Contains(t) ? 1 : 0)
                        .ThenByDescending(t => t.CurrentAtk)
                        .First();
                case EffectZoneFilter.FieldSpellTraps:
                    return targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _)) ?? targets[0];
                case EffectZoneFilter.OppFaceUpMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.FieldAnyMonster:
                    // Prefer opponent monsters (Man-Eater Bug AI); fall back to highest ATK overall.
                    // Face-down use printed ATK so set walls still rank for target choice.
                    return targets
                               .Where(t => opp.TryFindMonster(t, out _))
                               .OrderByDescending(t => t.CurrentAtk)
                               .FirstOrDefault()
                           ?? targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.ControllerGySpells:
                case EffectZoneFilter.ControllerGyTraps:
                    return targets[0];
                case EffectZoneFilter.ControllerGyMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppGyMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.ControllerHandMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.AnyCardOnField:
                    return targets.FirstOrDefault(t => opp.TryFindMonster(t, out _))
                           ?? targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _))
                           ?? targets[0];
                default:
                    return targets[0];
            }
        }

        static EffectTargetKind MapTargetKind(EffectClause c) => c.Zone switch
        {
            EffectZoneFilter.EitherGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.FieldSpellTraps => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.OppFaceUpMonsters when c.Action == EffectActionKind.EffectDamageBothFromOriginalAtk
                => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
            EffectZoneFilter.OppFaceUpMonsters => EffectTargetKind.OppFaceUpMonster,
            EffectZoneFilter.OppDefensePositionMonsters => EffectTargetKind.OppFaceUpMonster,
            EffectZoneFilter.FieldAnyMonster => EffectTargetKind.AnyMonsterOnField,
            EffectZoneFilter.ControllerGySpells => EffectTargetKind.SpellInYourGy,
            EffectZoneFilter.ControllerGyTraps => EffectTargetKind.TrapInYourGy,
            EffectZoneFilter.AnyCardOnField => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.ControllerHandMonsters when c.Action == EffectActionKind.SpecialSummonFromHand
                => EffectTargetKind.MonsterInYourHand,
            EffectZoneFilter.ControllerDeckMonsters => EffectTargetKind.MonsterInYourDeckToSummon,
            EffectZoneFilter.ControllerDeckSpells => EffectTargetKind.SpellInYourDeck,
            EffectZoneFilter.ControllerHandMonsters => EffectTargetKind.DiscardMonsterInHand,
            EffectZoneFilter.ControllerGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.OppGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.OppMonsters => EffectTargetKind.AnyMonsterOnField,
            EffectZoneFilter.ControllerMonsters when c.Action == EffectActionKind.EquipThisToTarget
                => EffectTargetKind.EquipMonsterYouControl,
            EffectZoneFilter.ControllerMonsters => EffectTargetKind.TributeMonsterYouControl,
            EffectZoneFilter.OppAnyCardOnField => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.DeckFieldSpells => EffectTargetKind.FieldSpellInYourDeck,
            EffectZoneFilter.DeckEquipSpells => EffectTargetKind.EquipSpellInYourDeck,
            EffectZoneFilter.DeckMonstersRaceLevelLeq => EffectTargetKind.MonsterInYourDeckFiltered,
            EffectZoneFilter.DeckMonstersAtkLeq => EffectTargetKind.MonsterInYourDeckAtkLeq,
            _ => EffectTargetKind.None
        };

        static void PlaceFaceUp(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand)
        {
            var zoneIndex = -2;
            if (who != null && card != null &&
                (who.TryFindSpellTrap(card, out var already) ||
                 (who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)))
            {
                card.FaceUp = true;
                card.SetThisTurn = false;
                who.Hand?.Remove(card);
                zoneIndex = who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card
                    ? -1
                    : already;
            }
            else if (fromHand)
            {
                who.Hand.Remove(card);
                var zi = engine.FirstEmptySpellTrap(who);
                if (zi >= 0)
                {
                    who.SpellTrapZones[zi].Occupant = card;
                    zoneIndex = zi;
                }

                card.FaceUp = true;
                card.SetThisTurn = false;
            }
            else
            {
                card.FaceUp = true;
                if (who.TryFindSpellTrap(card, out var zi))
                    zoneIndex = zi;
            }

            var staysOnField = card.Def != null && card.Def.StaysFlatOnFieldWhenActivated;
            Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                card.InstanceId, card.CardId, card.Name,
                who != null && who.IsPlayer, zoneIndex, staysOnField: staysOnField, flatOnBoard: false);
            DuelPresentationPacer.HoldSpellActivate(card.Name, opponentCard: who != null && !who.IsPlayer);
        }

        static void FinishSpellTrap(DuelEngine engine, DuelistState who, CardInstance card, bool stays)
        {
            if (stays)
            {
                // Field Spell already sits in the Field Zone — do not relocate into an S/T zone.
                if (who != null && who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)
                {
                    card.FaceUp = true;
                }
                else if (who != null && !who.TryFindSpellTrap(card, out _))
                {
                    who.Hand?.Remove(card);
                    var zi = engine.FirstEmptySpellTrap(who);
                    if (zi >= 0)
                        who.SpellTrapZones[zi].Occupant = card;
                }

                card.FaceUp = true;
                who.TryFindSpellTrap(card, out var stayZi);
                Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                    card.InstanceId, card.CardId, card.Name,
                    who != null && who.IsPlayer, stayZi, staysOnField: true, flatOnBoard: false);
            }
            else
            {
                Presentation.ArInteraction.SpellActivationPresentation.QueueFadeToGy(card.InstanceId);
                Presentation.ArInteraction.SpellActivationPresentation.EnqueueGhostIfNeeded(card.InstanceId);
                engine.SendCardToGrave(who, card);
            }

            if (card?.Def != null && card.Def.IsSpell)
                NotifySpellResolved(engine, who);
            engine.FlushQueuedSummonResponses();
        }

        static void AddUniqueDeck(DuelEngine engine, DuelistState who, List<CardInstance> list,
            System.Func<CardDef, bool> ok)
        {
            if (engine?.Database == null || who?.Deck == null || list == null || ok == null)
                return;
            var seen = new HashSet<int>();
            for (var i = 0; i < who.Deck.Count; i++)
            {
                var id = who.Deck[i];
                if (!seen.Add(id)) continue;
                if (!engine.Database.TryGet(id, out var def) || !ok(def)) continue;
                list.Add(engine.CreateCardInstance(id));
            }
        }

        static int AmountWithGyCopies(DuelistState who, CardInstance source, EffectClause clause)
        {
            var n = clause != null ? Mathf.Max(0, clause.Amount) : 0;
            if (clause == null || clause.ExtraAmountPerCopyInGy <= 0 || who?.Graveyard == null)
                return n;
            var name = !string.IsNullOrEmpty(clause.NamedCard) ? clause.NamedCard : source?.Name;
            if (string.IsNullOrEmpty(name)) return n;
            var copies = 0;
            foreach (var g in who.Graveyard)
            {
                if (g == null) continue;
                if (g.IsNamed(name) ||
                    string.Equals(g.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    copies++;
            }

            return n + copies * clause.ExtraAmountPerCopyInGy;
        }

        static void DestroyCard(DuelEngine engine, CardInstance card, CardInstance source = null,
            bool banishIfDestroyed = false)
        {
            if (card == null) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, card, source))
            {
                engine.Log($"{card.Name} is unaffected by {source?.Name ?? "this effect"}.");
                return;
            }

            var owner = engine.ControllerOf(card);
            if (owner == null)
            {
                engine.Log($"Destroy: {card.Name} not found on field.");
                return;
            }

            if (owner.TryFindMonster(card, out _))
                engine.DestroyMonsterPublic(owner, card, source, banishIfDestroyed);
            else if (banishIfDestroyed)
                engine.BanishCard(owner, card);
            else
                engine.SendCardToGrave(owner, card, sentBy: source);
        }

        static void DiscardAll(DuelistState who)
        {
            var copy = who.Hand.ToList();
            who.Hand.Clear();
            foreach (var c in copy)
                who.Graveyard.Add(c);
        }

        static bool LordOfDOnField(DuelEngine engine) =>
            engine.Player.MonstersOnField().Any(m => m.FaceUp && m.CardId == MonsterEffects.LordOfD) ||
            engine.Opponent.MonstersOnField().Any(m => m.FaceUp && m.CardId == MonsterEffects.LordOfD);
    }
}
