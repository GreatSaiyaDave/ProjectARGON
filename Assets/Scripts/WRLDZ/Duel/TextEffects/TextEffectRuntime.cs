using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Executes <see cref="CompiledCardProgram"/> clauses produced from official card text.
    /// Structural rules stay in <see cref="DuelEngine"/>; this only applies remembered text ops.
    /// </summary>
    public static class TextEffectRuntime
    {
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
            var continuousPlay = card.Def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                                 engine.PendingResponse == null &&
                                 (prog.HasTiming(EffectTiming.EndPhase) ||
                                  prog.HasTiming(EffectTiming.StandbyPhase) ||
                                  prog.HasTiming(EffectTiming.ContinuousWhileFaceUp) ||
                                  activate.Count > 0);
            if (activate.Count == 0 && attack.Count == 0 && summon.Count == 0 && dmg.Count == 0 &&
                takeLp.Count == 0 && !continuousPlay)
            {
                reason = "No activatable clauses in learned text.";
                return false;
            }

            // Response-window only cards
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
            if (activate.Count == 0)
            {
                if (attack.Count > 0 || summon.Count > 0 || dmg.Count > 0 || takeLp.Count > 0)
                {
                    reason = "Only legal in response to the matching trigger (attack / summon / damage).";
                    return false;
                }

                if (!continuousPlay)
                {
                    reason = "No activatable clauses in learned text.";
                    return false;
                }
                // Continuous S/T: playing the card is the activation even when the
                // printed effect is only End Phase / Standby / while-face-up.
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
                    CollectDiscardCost(who, c.DiscardCostAttribute, card).Count == 0)
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

                if (c.Action == EffectActionKind.SpecialSummonFromGy &&
                    engine.FirstEmpty(who.MonsterZones) < 0)
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
        /// </summary>
        public static bool TryResolveActivation(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget)
        {
            if (prog == null) return false;

            // Response windows
            if (engine.PendingResponse != null && engine.PendingResponse.Responder == who)
            {
                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
                {
                    var clauses = prog.ClausesFor(EffectTiming.AttackDeclared);
                    var freeChain = SpellTrapEffects.IsCompiledFreeChain(card.Def, prog)
                        ? prog.ClausesFor(EffectTiming.Activate)
                        : prog.ClausesFor(EffectTiming.Activate).FindAll(c =>
                            c != null &&
                            (c.Action == EffectActionKind.ApplyWabokuStyle ||
                             c.OpponentTurnOnly ||
                             c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn));
                    if (clauses.Count == 0 && freeChain.Count == 0)
                        return false;

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
                    var open = prog.ClausesFor(EffectTiming.Activate);
                    if (open.Count == 0 ||
                        !SpellTrapEffects.IsCompiledFreeChain(card.Def, prog))
                        return false;
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

            var activate = prog.ClausesFor(EffectTiming.Activate)
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
            if (!isMonster)
                PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");

            var ign = activate.FirstOrDefault(c =>
                HasStructuredIgnitionCost(c) &&
                IgnitionCostLegal(engine, who, card, c, out _));
            if (ign != null)
            {
                return StartIgnition(engine, who, card, fromHand,
                    new List<EffectClause> { ign }, ign,
                    autoPickTarget, isMonster);
            }

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
                if (targeted.RequiresDiscardCost)
                {
                    if (!StartOrPayDiscardThenTarget(engine, who, card, fromHand, activate, targeted,
                            autoPickTarget, monsterIgnition))
                        return false;
                    return true;
                }

                var targets = CollectTargets(engine, who, targeted, card);
                if (!HasEnoughFromList(engine, who, targeted, targets))
                {
                    engine.Log($"{card.Name}: no legal target — activation fails.");
                    if (!monsterIgnition)
                        FinishSpellTrap(engine, who, card, stays: false);
                    engine.NotifyPublic();
                    return true;
                }

                if (autoPickTarget || !who.IsPlayer)
                {
                    var picks = AutoPickMany(targeted, targets, who, engine);
                    var dummy = false;
                    foreach (var c in activate)
                    {
                        if (c.RequiresTargetChoice)
                        {
                            if (picks.Count == 0)
                                ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                            else
                                foreach (var pick in picks)
                                    ApplyClause(engine, who, card, c, pick, ref dummy, ref dummy);
                        }
                        else
                            ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    }

                    if (monsterIgnition)
                        card.EffectUsedThisTurn = targeted.OncePerTurn || card.EffectUsedThisTurn;
                    else
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
                    WasSetOnField = !fromHand && !monsterIgnition,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = monsterIgnition,
                    UsesTextProgram = true
                };
                InitMultiTargetPending(pending, targeted);
                pending.LegalTargets.AddRange(IsMultiTarget(targeted)
                    ? FilterLegalForNextPick(engine, who, targeted, card, pending)
                    : targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

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
            if (prog == null)
            {
                engine.ClearPendingActivation();
                return false;
            }

            var targeted = prog.ClausesFor(EffectTiming.Activate)
                .FirstOrDefault(c => c != null && c.RequiresTargetChoice);
            if (targeted != null && IsMultiTarget(targeted))
            {
                if (!p.ChosenTargets.Contains(target))
                    p.ChosenTargets.Add(target);
                RecordSidePick(engine, who, p, target);
                if (p.TargetPicksRemaining > 0)
                    p.TargetPicksRemaining--;

                var more = FilterLegalForNextPick(engine, who, targeted, card, p);
                if (StillNeedPicks(p, targeted) && more.Count > 0)
                {
                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(more);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                return FinishMultiTargetPending(engine, p, prog, monsterIgnition);
            }

            engine.ClearPendingActivation();

            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
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
            else
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
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
                    var pick = AutoPick(targeted, targets, who, engine);
                    if (pick == null)
                    {
                        engine.Log($"{monster.Name} Flip: AI found no pickable target.");
                        return true;
                    }

                    engine.Log($"{monster.Name} targets {pick.Name}.");
                    var d = false;
                    foreach (var c in flip)
                        ApplyClause(engine, who, monster, c,
                            c.RequiresTargetChoice ? pick : null, ref d, ref d);
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
                    UsesTextProgram = true
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
            var clauses = prog?.ClausesFor(EffectTiming.ThisCardSummoned);
            if (clauses == null || clauses.Count == 0) return false;

            var applicable = new List<EffectClause>();
            foreach (var c in clauses)
            {
                if (c == null) continue;
                if (c.RequiresThisFlipSummoned && !flipSummon) continue;
                if (c.RequiresThisNormalSummoned && (flipSummon || specialSummon)) continue;
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
            var clauses = prog?.ClausesFor(EffectTiming.SentFromFieldToGy);
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
                EffectActionKind.ChangeBattlePosition => true,
                EffectActionKind.EffectDamageBothFromOriginalAtk => true,
                EffectActionKind.Destroy when c.Zone is EffectZoneFilter.FieldAnyMonster
                    or EffectZoneFilter.OppFaceUpMonsters => true,
                EffectActionKind.Destroy when c.Zone is EffectZoneFilter.FieldSpellTraps &&
                                              c.Side != EffectSide.Both => true,
                EffectActionKind.ReturnToHand when c.Zone is EffectZoneFilter.FieldMonsters => false,
                EffectActionKind.ReturnToHand => true,
                EffectActionKind.Banish when c.Zone is EffectZoneFilter.EitherGyMonsters
                    or EffectZoneFilter.OpponentGyMonsters
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

            if (p.AwaitingDiscardCost || p.AwaitingSendNamedCost || p.AwaitingIgnitionCost)
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
            engine.ClearPendingActivation();
            if (prog == null) return false;

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
            foreach (var c in clauses)
            {
                if (c.RequiresTargetChoice)
                    ApplyClause(engine, who, card, c, target, ref d, ref d);
                else
                    ApplyClause(engine, who, card, c, null, ref d, ref d);
            }

            engine.NotifyPublic();
            return true;
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

        static bool HasLegalTargetsConsideringDiscard(DuelEngine engine, DuelistState who,
            EffectClause c, CardInstance except)
        {
            var list = CollectTargets(engine, who, c, except);
            if (HasEnoughFromList(engine, who, c, list)) return true;
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
                c.Zone != EffectZoneFilter.EitherGyMonsters &&
                c.Zone != EffectZoneFilter.OpponentGyMonsters)
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
                var n = CollectBanishGy(who, c).Count;
                if (n < (c.BanishFromGyUpTo ? 1 : c.BanishFromGyCount))
                {
                    reason = "Not enough monsters in GY to banish.";
                    return false;
                }
            }

            if (c.Action == EffectActionKind.SpecialSummonNamed ||
                c.Action == EffectActionKind.SpecialSummonFromGy ||
                c.Action == EffectActionKind.SpecialSummonFromHand)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0 && !c.RequiresTributeThis &&
                    !c.RequiresSendThisToGy)
                {
                    reason = "No free Monster Zone.";
                    return false;
                }
            }

            if (c.Action == EffectActionKind.RitualSummon)
            {
                var target = FindRitualMonsterInHand(who, c);
                if (target == null)
                {
                    reason = "No matching Ritual Monster in your hand.";
                    return false;
                }

                var need = RitualNeed(target, c);
                var tributes = SelectRitualTributes(who, target, need, c.RitualExactLevel);
                var have = TotalLevel(tributes);
                if (c.RitualExactLevel ? have != need : have < need)
                {
                    reason = $"Tribute Levels for the Ritual Summon must " +
                             $"{(c.RitualExactLevel ? "exactly equal" : "total ≥")} {need}.";
                    return false;
                }

                var fieldTribute = tributes.Exists(t => who.TryFindMonster(t, out _));
                if (engine.FirstEmpty(who.MonsterZones) < 0 && !fieldTribute)
                {
                    reason = "No free Monster Zone for the Ritual Summon.";
                    return false;
                }
            }

            if (c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                !DeckHasNamed(engine, who, c.NamedCard))
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
                list.Add(m);
            }

            return list;
        }

        // —— Ritual Summon (activated by a Ritual Spell) ——

        /// <summary>The Ritual Monster this spell can summon that is currently in hand.</summary>
        static CardInstance FindRitualMonsterInHand(DuelistState who, EffectClause clause)
        {
            if (who?.Hand == null || clause == null) return null;
            foreach (var c in who.Hand)
            {
                if (c?.Def == null || !c.Def.IsRitualMonster) continue;
                if (!string.IsNullOrEmpty(clause.NamedCard))
                {
                    if (c.IsNamed(clause.NamedCard) ||
                        string.Equals(c.Name, clause.NamedCard, System.StringComparison.OrdinalIgnoreCase))
                        return c;
                }
                else if (!string.IsNullOrEmpty(clause.AttributeFilter))
                {
                    if (string.Equals(c.Def.attribute, clause.AttributeFilter,
                            System.StringComparison.OrdinalIgnoreCase))
                        return c;
                }
                else
                {
                    return c; // no name/attribute restriction
                }
            }

            return null;
        }

        /// <summary>The Ritual Summon Tribute requirement — the summoned monster's Level.</summary>
        static int RitualNeed(CardInstance target, EffectClause clause)
        {
            var lvl = target != null ? Mathf.Max(0, target.Level) : 0;
            if (lvl > 0) return lvl;
            return Mathf.Max(1, clause?.Amount ?? 1); // fallback to printed number
        }

        static List<CardInstance> RitualTributeCandidates(DuelistState who, CardInstance target)
        {
            var cand = new List<CardInstance>();
            if (who != null)
            {
                foreach (var m in who.MonstersOnField())
                    if (m != null && m != target) cand.Add(m);
                if (who.Hand != null)
                    foreach (var c in who.Hand)
                        if (c != null && c != target && c.Def != null && c.Def.IsMonster) cand.Add(c);
            }

            return cand;
        }

        /// <summary>
        /// Auto-select Tribute material (monsters on field + in hand, excluding the Ritual
        /// Monster). For the usual "Level N or more" the total must be ≥ <paramref name="need"/>
        /// (highest-Level first, fewest monsters). For "exactly equal" (Chant cards) the total
        /// must be EXACTLY <paramref name="need"/> — an exact subset is found or none is returned.
        /// </summary>
        static List<CardInstance> SelectRitualTributes(DuelistState who, CardInstance target, int need,
            bool exact)
        {
            var cand = RitualTributeCandidates(who, target);
            if (exact)
                return SelectExactLevelSubset(cand, need);

            cand.Sort((a, b) => Mathf.Max(0, b.Level).CompareTo(Mathf.Max(0, a.Level)));
            var chosen = new List<CardInstance>();
            var sum = 0;
            foreach (var c in cand)
            {
                if (sum >= need) break;
                chosen.Add(c);
                sum += Mathf.Max(0, c.Level);
            }

            return chosen;
        }

        /// <summary>Find a subset of monsters whose Levels sum EXACTLY to need (or empty).</summary>
        static List<CardInstance> SelectExactLevelSubset(List<CardInstance> cand, int need)
        {
            var chosen = new List<CardInstance>();
            if (need <= 0) return chosen;
            // Highest-first with pruning; counts are small (hand + field).
            cand.Sort((a, b) => Mathf.Max(0, b.Level).CompareTo(Mathf.Max(0, a.Level)));

            bool Dfs(int i, int remaining, List<CardInstance> acc)
            {
                if (remaining == 0) return true;
                if (i >= cand.Count || remaining < 0) return false;
                for (var k = i; k < cand.Count; k++)
                {
                    var lvl = Mathf.Max(0, cand[k].Level);
                    if (lvl <= 0 || lvl > remaining) continue;
                    acc.Add(cand[k]);
                    if (Dfs(k + 1, remaining - lvl, acc)) return true;
                    acc.RemoveAt(acc.Count - 1);
                }

                return false;
            }

            return Dfs(0, need, chosen) ? chosen : new List<CardInstance>();
        }

        static int TotalLevel(List<CardInstance> cards)
        {
            var sum = 0;
            if (cards != null)
                foreach (var c in cards)
                    sum += Mathf.Max(0, c?.Level ?? 0);
            return sum;
        }

        static bool RitualSummonResolve(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return false;
            var target = FindRitualMonsterInHand(who, clause);
            if (target == null)
            {
                engine.Log($"{source?.Name}: no matching Ritual Monster in hand for the Ritual Summon.");
                return false;
            }

            var need = RitualNeed(target, clause);
            var tributes = SelectRitualTributes(who, target, need, clause.RitualExactLevel);
            var have = TotalLevel(tributes);
            var meets = clause.RitualExactLevel ? have == need : have >= need;
            if (!meets)
            {
                engine.Log($"{source?.Name}: Tribute Levels {have} do not " +
                           $"{(clause.RitualExactLevel ? "exactly equal" : "meet")} {need}.");
                return false;
            }

            // Tribute first (field Tributes free a zone), then Special Summon from hand.
            foreach (var t in tributes)
                engine.SendCardToGrave(who, t);

            who.Hand.Remove(target);
            if (!engine.SpecialSummonToField(who, target, BattlePosition.Attack, faceUp: true))
            {
                who.Hand.Add(target); // no zone — undo the summon (Tributes stay paid, as in official rules on a full board this can't be declared)
                engine.Log($"{source?.Name}: no Monster Zone for the Ritual Summon.");
                return false;
            }

            engine.Log(
                $"{source?.Name}: Ritual Summon {target.Name} " +
                $"(Tributed {tributes.Count}, total Level {have} ≥ {need}).");
            return true;
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

        static List<CardInstance> CollectBanishGy(DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    (g.Def.attribute == null ||
                     !g.Def.attribute.Equals(c.AttributeFilter,
                         System.StringComparison.OrdinalIgnoreCase)))
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
                return CollectBanishGy(who, c);
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
                var gy = CollectBanishGy(who, c);
                var n = c.BanishFromGyUpTo
                    ? Mathf.Min(c.BanishFromGyCount, gy.Count)
                    : c.BanishFromGyCount;
                for (var i = 0; i < n && i < gy.Count; i++)
                    engine.BanishCard(who, gy[i]);
                costNumeric = n;
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

            return true;
        }

        static bool StartIgnition(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, List<EffectClause> activate, EffectClause clause, bool autoPick,
            bool isMonster)
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
                return FinishIgnitionAfterCost(engine, who, card, fromHand, isMonster, costNumeric);
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
            bool fromHand, bool isMonster, int costNumeric)
        {
            var prog = CompiledEffectCache.GetOrCompile(card);
            var activate = prog?.ClausesFor(EffectTiming.Activate);
            if (activate == null || activate.Count == 0) return false;
            var clause = activate.FirstOrDefault(c => c.RequiresTargetChoice) ?? activate[0];

            if (clause.RequiresTargetChoice)
            {
                var targets = CollectTargets(engine, who, clause, card, costNumeric);
                if (targets.Count == 0)
                {
                    engine.Log($"{card.Name}: no legal target after cost.");
                    if (isMonster && activate.Any(c => c.OncePerTurn))
                        card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                if (!who.IsPlayer)
                {
                    var pick = AutoPick(clause, targets, who, engine);
                    ApplyIgnitionClauses(engine, who, card, activate, pick, costNumeric, isMonster);
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
                    CostNumeric = costNumeric
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            ApplyIgnitionClauses(engine, who, card, activate, null, costNumeric, isMonster);
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
        }

        static bool DeckHasNamed(DuelEngine engine, DuelistState who, string name)
        {
            if (engine?.Database == null || who?.Deck == null || string.IsNullOrEmpty(name))
                return false;
            for (var i = 0; i < who.Deck.Count; i++)
            {
                if (!engine.Database.TryGet(who.Deck[i], out var def) || def == null) continue;
                if (string.Equals(def.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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
                        target.UntilEndOfTurnAtk -= amount;
                        if (payClause.DefAmount != 0)
                            target.UntilEndOfTurnDef -= amount;
                        engine.Log(
                            $"{target.Name} loses {amount} ATK" +
                            (payClause.DefAmount != 0 ? $"/{amount} DEF" : "") +
                            $" until the End Phase (now {target.CurrentAtk}/{target.CurrentDef}).");
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

        static void ApplyClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget, ref bool attackNegated, ref bool battleEnded,
            int costNumeric = 0)
        {
            if (clause == null) return;
            var opp = engine.OpponentOf(who);
            void Destroy(CardInstance c) => DestroyCard(engine, c, source, clause.BanishIfDestroyed);

            // Archfiend die-roll protection: as an opponent's targeting effect would
            // resolve against a monster, its controller's die-roll Archfiend may negate
            // it and destroy the opponent's card. Skip the effect when negated.
            if (chosenTarget != null && clause.RequiresTargetChoice &&
                clause.Action != EffectActionKind.DieRollNegateWhenTargeted &&
                ArchfiendTargetNegation.TryNegate(engine, who, source, chosenTarget))
                return;

            switch (clause.Action)
            {
                case EffectActionKind.DieRollNegateWhenTargeted:
                    // Continuous protection — resolved reactively by ArchfiendTargetNegation.
                    break;

                case EffectActionKind.RitualSummon:
                    RitualSummonResolve(engine, who, source, clause);
                    break;

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
                        if (clause.Zone == EffectZoneFilter.AttackingMonster)
                            attackNegated = true;
                        Destroy(chosenTarget);
                        break;
                    }

                    var mass = CollectAllMatching(engine, who, clause).ToList();
                    if (clause.AmountIsLevel && clause.Amount > 0)
                        mass.RemoveAll(m => m == null || m.Level != clause.Amount);
                    foreach (var m in mass)
                        Destroy(m);
                    if (mass.Count > 0)
                        DuelPresentationPacer.Extend(DuelPresentationPacer.ReadAfterCombatResult,
                            "Monsters destroyed — read the field…");
                    break;

                case EffectActionKind.SpecialSummonFromGy:
                    if (chosenTarget == null) break;
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
                        if (clause.SummonCannotBeTributed)
                            chosenTarget.CannotBeTributedForSummon = true;
                        if (clause.SummonDestroyAtEndPhase)
                        {
                            chosenTarget.TempDestroyOnEndOfTurn = engine.TurnNumber;
                            engine.Log(
                                $"{chosenTarget.Name} will be destroyed during the End Phase this turn.");
                        }
                    }
                    break;

                case EffectActionKind.SpecialSummonFromHand:
                {
                    if (clause.RequiresLordOfDOnField && !LordOfDOnField(engine))
                    {
                        engine.Log("Lord of D. is not on the field — effect resolves without summoning.");
                        break;
                    }

                    var race = string.IsNullOrEmpty(clause.RaceFilter) ? "Dragon" : clause.RaceFilter;
                    var dragons = who.Hand
                        .Where(c => c?.Def != null && c.Def.IsMonster && c.Def.race != null &&
                                    c.Def.race.IndexOf(race, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(Mathf.Max(1, clause.Amount))
                        .ToList();
                    foreach (var d in dragons)
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
                        chosenTarget.Position = chosenTarget.Position == BattlePosition.Attack
                            ? BattlePosition.Defense
                            : BattlePosition.Attack;
                        engine.Log($"{chosenTarget.Name} → {chosenTarget.Position} Position.");
                    }

                    break;

                case EffectActionKind.SetTargetFaceDownDefense:
                    if (chosenTarget != null)
                    {
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
                        engine.ApplyEffectDamage(opp, chosenTarget.CurrentAtk, source.Name);
                    break;

                case EffectActionKind.GainLpEqualToAtk:
                    if (chosenTarget != null)
                    {
                        var gain = chosenTarget.CurrentAtk;
                        who.LifePoints += gain;
                        engine.Log($"{who.Name} gains {gain} LP ({who.LifePoints}).");
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

                case EffectActionKind.GainLifePoints:
                {
                    var gained = AmountWithGyCopies(who, source, clause);
                    if (clause.ScaleAmountByControllerMonsters)
                        gained = Mathf.Max(0, clause.Amount) * who.MonsterCount;
                    who.LifePoints += gained;
                    engine.Log($"{who.Name} gains {gained} LP ({who.LifePoints}).");
                    break;
                }

                case EffectActionKind.TakeEffectDamage:
                    engine.ApplyEffectDamage(who, Mathf.Max(0, clause.Amount), source?.Name);
                    break;

                case EffectActionKind.PreventControllerBattleDamage:
                case EffectActionKind.FieldTreatedAsName:
                case EffectActionKind.SelfDestroyUnlessNamedFaceUp:
                    // Continuous — applied by FieldSpellEffects while face-up.
                    break;

                case EffectActionKind.ApplyLingeringAtkDef:
                {
                    var t = chosenTarget ?? source;
                    if (t != null)
                    {
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
                            engine.ReturnCardToHand(m);
                    }

                    foreach (var m in opp.MonstersOnField().ToList())
                    {
                        if (m?.Def != null && m.FaceUp &&
                            m.Def.type != null &&
                            m.Def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0)
                            engine.ReturnCardToHand(m);
                    }

                    break;

                case EffectActionKind.BanishThenSameNameFromOppHandDeck:
                {
                    var t = chosenTarget;
                    if (t == null) break;
                    var id = t.CardId;
                    var owner = engine.ControllerOf(t);
                    if (owner != null) engine.BanishCard(owner, t);
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
                        var owner = OwnerOf(engine, who, chosenTarget);
                        if (owner != null) engine.BanishCard(owner, chosenTarget);
                    }
                    else
                    {
                        foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                        {
                            var owner = OwnerOf(engine, who, m);
                            if (owner != null) engine.BanishCard(owner, m);
                        }
                    }

                    break;

                case EffectActionKind.ApplyWabokuStyle:
                    who.WabokuActive = true;
                    engine.Log(
                        $"{who.Name}: no battle damage this turn; monsters cannot be destroyed by battle.");
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

                case EffectActionKind.EffectDamageBothFromOriginalAtk:
                    if (chosenTarget == null) break;
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
                            else engine.ReturnCardToHand(m);
                        }
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
                    TrySpecialSummonNamed(engine, who, clause);
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
                    engine.Rng.SetPresentationContext(who != null && who.IsPlayer, source?.Name);
                    var heads = engine.Rng.TossCoinsCountHeads(n);
                    engine.Log($"{source?.Name}: {heads}/{n} heads (need {need}).");
                    if (heads >= need && chosenTarget != null)
                        Destroy(chosenTarget);
                    break;
                }

                case EffectActionKind.RollDieZorc:
                {
                    engine.Rng.SetPresentationContext(who != null && who.IsPlayer, source?.Name);
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

                case EffectActionKind.EquipThisToTarget:
                    if (chosenTarget == null || source == null) break;
                    EquipCardTo(engine, who, source, chosenTarget, clause.EquipAtkBonus);
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

        public static void FirePhaseTriggers(DuelEngine engine, DuelistState who, EffectTiming timing)
        {
            if (engine == null || who == null) return;
            FirePhaseTriggersFor(engine, who, timing, opponentTurnClauses: false);
            if (timing == EffectTiming.StandbyPhase || timing == EffectTiming.EndPhase)
            {
                var other = engine.OpponentOf(who);
                if (other != null)
                    FirePhaseTriggersFor(engine, other, timing, opponentTurnClauses: true);
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
                    AwaitingIgnitionCost = true,
                    CostPicksRemaining = 1
                };
                pending.LegalTargets.AddRange(tributes);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
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

        static void ResolveStandbyMaintenancePayLp(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause c)
        {
            if (engine == null || who == null || card == null) return;
            var n = c != null && c.PayLpAmount > 0 ? c.PayLpAmount : 500;
            // Pandemonium: neither player pays LP for their Archfiend monsters.
            if (FieldSpellEffects.ArchfiendMaintenanceWaived(engine))
            {
                engine.Log($"{card.Name}: Standby maintenance ({n} LP) waived (Pandemonium).");
                return;
            }
            // Mandatory, not optional — pay as much as possible (LP cannot go negative).
            engine.PayLifePointCost(who, n, $"Standby maintenance: {card.Name}");
            MirrorStandbyPayment(engine, who, card, n);
        }

        static void ResolvePayLpOrDestroyThis(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause c)
        {
            var n = c != null && c.PayLpAmount > 0 ? c.PayLpAmount : 100;
            if (who != null && who.LifePoints >= n)
            {
                who.LifePoints -= n;
                engine.Log($"Cost: pay {n} LP or destroy {card?.Name} → {who.Name} at {who.LifePoints} LP.");
                MirrorStandbyPayment(engine, who, card, n);
                return;
            }

            engine.Log($"{card?.Name}: not enough LP — destroy this card.");
            engine.SendCardToGrave(who, card);
        }

        /// <summary>
        /// Battle-Scarred: when the controller pays the linked Archfiend's Standby
        /// maintenance, the opponent pays the same amount.
        /// </summary>
        static void MirrorStandbyPayment(DuelEngine engine, DuelistState who,
            CardInstance monster, int amount)
        {
            if (engine == null || who == null || monster?.Equips == null || amount <= 0) return;
            foreach (var eq in monster.Equips)
            {
                if (eq?.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null) continue;
                if (!prog.ClauseList.Exists(x => x != null && x.MirrorStandbyPaymentToOpponent))
                    continue;
                var opp = engine.OpponentOf(who);
                if (opp == null) continue;
                engine.PayLifePointCost(opp, amount, $"Battle-Scarred mirror ({monster.Name})");
                return;
            }
        }

        static void FirePhaseTriggersFor(DuelEngine engine, DuelistState who, EffectTiming timing,
            bool opponentTurnClauses)
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
                var prog = CompiledEffectCache.GetOrCompile(card.Def);
                var clauses = prog?.ClausesFor(timing);
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

                    if (c.Action == EffectActionKind.StandbyMaintenancePayLp)
                    {
                        if (!card.FaceUp) continue;
                        if (!who.TryFindMonster(card, out _)) continue;
                        ResolveStandbyMaintenancePayLp(engine, who, card, c);
                        continue;
                    }

                    if (c.Action == EffectActionKind.PayLpOrDestroyThis)
                    {
                        if (!card.FaceUp) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        // Pandemonium waives the Archfiend Standby maintenance cost
                        // (but not non-Archfiend upkeep like Messenger of Peace).
                        if (FieldSpellEffects.IsArchfiendMonster(card.Def) &&
                            FieldSpellEffects.ArchfiendMaintenanceWaived(engine))
                        {
                            engine.Log($"{card.Name}: Standby cost waived (Pandemonium).");
                            continue;
                        }
                        ResolvePayLpOrDestroyThis(engine, who, card, c);
                        continue;
                    }
                    if (c.ResolvesFromGy && (who.Graveyard == null || !who.Graveyard.Contains(card)))
                        continue;
                    if (!c.ResolvesFromGy && who.Graveyard != null && who.Graveyard.Contains(card) &&
                        timing != EffectTiming.EndPhase)
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
                    ApplyClause(engine, who, card, c, phaseTarget, ref dummy, ref dummy);
                    if (c.RequiresNextControllerStandby)
                        card.SentByContinuousSpellEffect = false;
                }
            }
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
            engine.Rng.SetPresentationContext(who != null && who.IsPlayer, card?.Name);
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
                return;
            }

            engine.Log($"{source?.Name}: call wrong — destroy your monsters, take half ATK damage.");
            var atk = 0;
            foreach (var m in who.MonstersOnField().ToList())
            {
                atk += m.CurrentAtk;
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
            var n = clause.TokenCount > 0 ? clause.TokenCount : 1;
            var pos = clause.TokenToOpponent ? BattlePosition.Defense : BattlePosition.Attack;
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

        static void EquipCardTo(DuelEngine engine, DuelistState from, CardInstance equip,
            CardInstance host, int atkBonus)
        {
            if (equip == null || host == null) return;
            var owner = engine.ControllerOf(equip) ?? from;
            if (owner != null)
            {
                if (owner.TryFindMonster(equip, out var mi))
                    owner.MonsterZones[mi].Occupant = null;
            }

            equip.EquippedTo = host;
            if (!host.Equips.Contains(equip))
                host.Equips.Add(equip);
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
                else if (!string.Equals(def.name, clause.NamedCard,
                             System.StringComparison.OrdinalIgnoreCase))
                    return false;
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

        static void TrySpecialSummonNamed(DuelEngine engine, DuelistState who, EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var hasName = !string.IsNullOrEmpty(clause.NamedCard);
            var hasFilter = !string.IsNullOrEmpty(clause.RaceFilter) ||
                            !string.IsNullOrEmpty(clause.AttributeFilter) ||
                            clause.AmountIsAtkMax || clause.AmountIsDefMax;
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
                     string.Equals(c.Name, clause.NamedCard, System.StringComparison.OrdinalIgnoreCase)));
                if (hit != null)
                {
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
                    if (!string.Equals(def.name, clause.NamedCard,
                            System.StringComparison.OrdinalIgnoreCase))
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

        /// <summary>Card belongs to a named "series" (archetype / printed name / treated-as).</summary>
        static bool CardMatchesSeries(WRLDZ.Data.CardDef def, string series)
        {
            if (def == null || string.IsNullOrEmpty(series)) return false;
            var cmp = System.StringComparison.OrdinalIgnoreCase;
            if (!string.IsNullOrEmpty(def.archetype) && def.archetype.IndexOf(series, cmp) >= 0)
                return true;
            if (!string.IsNullOrEmpty(def.name) && def.name.IndexOf(series, cmp) >= 0)
                return true;
            return def.desc != null && def.desc.IndexOf($"treated as an \"{series}\"", cmp) >= 0;
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
                        if (st != except) list.Add(st);
                    foreach (var st in opp.SpellTrapsOnField())
                        list.Add(st);
                    break;
                case EffectZoneFilter.EitherGyMonsters:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    break;
                case EffectZoneFilter.OpponentGyMonsters:
                    if (opp?.Graveyard != null)
                    {
                        foreach (var g in opp.Graveyard)
                            if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    }

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
                case EffectZoneFilter.FieldAnyMonster:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == except) continue;
                        if (c.Action == EffectActionKind.Banish && !m.FaceUp) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == except) continue;
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
                        if (!string.IsNullOrEmpty(c.EquipHostName) && !m.IsNamed(c.EquipHostName) &&
                            (m.Name == null ||
                             !string.Equals(m.Name, c.EquipHostName, System.StringComparison.OrdinalIgnoreCase)))
                            continue;
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
            if (!string.IsNullOrEmpty(c.TargetSeriesName))
                list.RemoveAll(t => !CardMatchesSeries(t?.Def, c.TargetSeriesName));
            if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                c.Action == EffectActionKind.EquipThisToTarget)
                list.RemoveAll(t => t?.Def?.attribute == null ||
                                    !t.Def.attribute.Equals(c.AttributeFilter,
                                        System.StringComparison.OrdinalIgnoreCase));
            if (c.Action == EffectActionKind.EquipThisToTarget)
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp);
            if (c.Action == EffectActionKind.SetTargetFaceDownDefense)
                list.RemoveAll(t => t == null || !t.FaceUp);
            if (c.Action == EffectActionKind.ChangeBattlePosition)
                list.RemoveAll(t => t == null || !t.FaceUp);

            return list;
        }

        static IEnumerable<CardInstance> CollectAllMatching(DuelEngine engine, DuelistState who,
            EffectClause c)
        {
            var opp = engine.OpponentOf(who);
            switch (c.Zone)
            {
                case EffectZoneFilter.FieldMonsters:
                    if (c.Side == EffectSide.Opponent || c.Side == EffectSide.Both)
                        foreach (var m in opp.MonstersOnField()) yield return m;
                    if (c.Side == EffectSide.Controller || c.Side == EffectSide.Both)
                        foreach (var m in who.MonstersOnField()) yield return m;
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
                case EffectZoneFilter.OpponentGyMonsters:
                    if (opp?.Graveyard != null)
                    {
                        foreach (var g in opp.Graveyard)
                            if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck)
                                yield return g;
                    }

                    break;
            }
        }

        static bool IsMultiTarget(EffectClause c) =>
            c != null &&
            (c.TargetCount > 1 || c.TargetUpTo ||
             c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0);

        static int MaxPicks(EffectClause c)
        {
            if (c == null) return 1;
            if (c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0)
                return c.ControllerTargetCount + c.OpponentTargetCount;
            return c.TargetCount > 0 ? c.TargetCount : 1;
        }

        static void InitMultiTargetPending(PendingActivation pending, EffectClause c)
        {
            if (pending == null || c == null || !IsMultiTarget(c)) return;
            pending.TargetPicksRemaining = MaxPicks(c);
            pending.TargetUpTo = c.TargetUpTo;
            pending.ControllerPicksRemaining = c.ControllerTargetCount;
            pending.OpponentPicksRemaining = c.OpponentTargetCount;
        }

        /// <summary>
        /// TargetUpTo (Ghoul): after ≥1 pick, CONFIRM is legal even when more remain.
        /// Required mixed-side counts (Two-Pronged) cannot early-confirm.
        /// </summary>
        public static bool CanConfirmPendingTargets(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || !p.TargetUpTo) return false;
            if (p.AwaitingIgnitionCost || p.AwaitingDiscardCost || p.AwaitingSendNamedCost ||
                p.AwaitingLpCost || p.AwaitingCoinCall)
                return false;
            return p.ChosenTargets != null && p.ChosenTargets.Count >= 1;
        }

        public static bool TryConfirmPendingTargets(DuelEngine engine)
        {
            if (!CanConfirmPendingTargets(engine)) return false;
            var p = engine.PendingActivation;
            var card = p.Card;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null)
            {
                engine.ClearPendingActivation();
                return false;
            }

            var monsterIgnition = p.IsMonsterEffect || (card?.Def != null && card.Def.IsMonster);
            engine.Log($"{card?.Name}: confirmed {p.ChosenTargets.Count} target(s).");
            return FinishMultiTargetPending(engine, p, prog, monsterIgnition);
        }

        static bool FinishMultiTargetPending(DuelEngine engine, PendingActivation p,
            CompiledCardProgram prog, bool monsterIgnition)
        {
            var who = p.Controller;
            var card = p.Card;
            var costNum = p.CostNumeric;
            var chosen = p.ChosenTargets.ToList();
            engine.ClearPendingActivation();
            ApplyChosenTargets(engine, who, card, prog, chosen, costNum, monsterIgnition);
            return true;
        }

        static bool HasEnoughFromList(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> list)
        {
            if (c == null || list == null) return false;
            if (c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0)
            {
                var you = 0;
                var them = 0;
                var opp = engine?.OpponentOf(who);
                foreach (var t in list)
                {
                    if (t == null) continue;
                    if (who != null && who.TryFindMonster(t, out _)) you++;
                    else if (opp != null && opp.TryFindMonster(t, out _)) them++;
                }

                return you >= c.ControllerTargetCount && them >= c.OpponentTargetCount;
            }

            if (c.TargetUpTo) return list.Count >= 1;
            if (c.TargetCount > 1) return list.Count >= c.TargetCount;
            return list.Count > 0;
        }

        static void RecordSidePick(DuelEngine engine, DuelistState who, PendingActivation p,
            CardInstance target)
        {
            if (p == null || target == null || who == null) return;
            if (who.TryFindMonster(target, out _))
                p.ControllerPicksRemaining = Mathf.Max(0, p.ControllerPicksRemaining - 1);
            else
            {
                var opp = engine?.OpponentOf(who);
                if (opp != null && opp.TryFindMonster(target, out _))
                    p.OpponentPicksRemaining = Mathf.Max(0, p.OpponentPicksRemaining - 1);
            }
        }

        static bool StillNeedPicks(PendingActivation p, EffectClause c)
        {
            if (p == null || c == null) return false;
            if (c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0)
                return p.ControllerPicksRemaining > 0 || p.OpponentPicksRemaining > 0;
            return p.TargetPicksRemaining > 0;
        }

        static List<CardInstance> FilterLegalForNextPick(DuelEngine engine, DuelistState who,
            EffectClause c, CardInstance except, PendingActivation p)
        {
            var list = CollectTargets(engine, who, c, except);
            if (p?.ChosenTargets != null && p.ChosenTargets.Count > 0)
                list.RemoveAll(t => t != null && p.ChosenTargets.Contains(t));
            if (c != null && (c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0) &&
                p != null)
            {
                var opp = engine?.OpponentOf(who);
                if (p.ControllerPicksRemaining <= 0)
                    list.RemoveAll(t => who != null && who.TryFindMonster(t, out _));
                if (p.OpponentPicksRemaining <= 0)
                    list.RemoveAll(t => opp != null && opp.TryFindMonster(t, out _));
            }

            return list;
        }

        static bool StillResolves(DuelEngine engine, DuelistState who, EffectClause c,
            CardInstance t)
        {
            if (t == null || c == null || engine == null || who == null) return false;
            var opp = engine.OpponentOf(who);
            if (c.Action == EffectActionKind.Banish &&
                c.Zone == EffectZoneFilter.OpponentGyMonsters)
                return opp != null && opp.Graveyard != null && opp.Graveyard.Contains(t);
            if (c.Action == EffectActionKind.Destroy)
                return who.TryFindMonster(t, out _) ||
                       (opp != null && opp.TryFindMonster(t, out _));
            var live = CollectTargets(engine, who, c, null);
            return live.Contains(t);
        }

        static List<CardInstance> AutoPickMany(EffectClause c, List<CardInstance> targets,
            DuelistState who, DuelEngine engine)
        {
            var list = new List<CardInstance>();
            if (c == null || targets == null || targets.Count == 0) return list;
            var opp = engine?.OpponentOf(who);
            if (c.ControllerTargetCount > 0 || c.OpponentTargetCount > 0)
            {
                foreach (var t in targets
                             .Where(x => who != null && who.TryFindMonster(x, out _))
                             .OrderBy(x => x.CurrentAtk)
                             .Take(Mathf.Max(0, c.ControllerTargetCount)))
                    list.Add(t);
                foreach (var t in targets
                             .Where(x => opp != null && opp.TryFindMonster(x, out _))
                             .OrderByDescending(x => x.CurrentAtk)
                             .Take(Mathf.Max(0, c.OpponentTargetCount)))
                    if (!list.Contains(t))
                        list.Add(t);
                return list;
            }

            var n = c.TargetUpTo
                ? Mathf.Min(c.TargetCount > 0 ? c.TargetCount : 1, targets.Count)
                : c.TargetCount > 1 ? Mathf.Min(c.TargetCount, targets.Count) : 1;
            var ordered = c.Zone == EffectZoneFilter.OpponentGyMonsters ||
                          c.Zone == EffectZoneFilter.EitherGyMonsters
                ? targets.OrderByDescending(t => t.CurrentAtk)
                : targets.OrderByDescending(t =>
                    opp != null && opp.TryFindMonster(t, out _) ? t.CurrentAtk + 100000 : t.CurrentAtk);
            foreach (var t in ordered)
            {
                if (t == null || list.Contains(t)) continue;
                list.Add(t);
                if (list.Count >= n) break;
            }

            return list;
        }

        static void ApplyChosenTargets(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, List<CardInstance> chosen, int costNum, bool monsterIgnition)
        {
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c == null) continue;
                if (c.RequiresTargetChoice)
                {
                    var any = false;
                    if (chosen != null)
                    {
                        foreach (var t in chosen)
                        {
                            if (!StillResolves(engine, who, c, t)) continue;
                            ApplyClause(engine, who, card, c, t, ref dummy, ref dummy, costNum);
                            any = true;
                        }
                    }

                    if (!any)
                        engine.Log($"{card.Name}: no remaining legal targets.");
                }
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy, costNum);
            }

            if (monsterIgnition)
            {
                if (prog.ClausesFor(EffectTiming.Activate).Any(x => x != null && x.OncePerTurn) &&
                    card != null)
                    card.EffectUsedThisTurn = true;
            }
            else
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            engine.NotifyPublic();
        }

        static CardInstance AutoPick(EffectClause c, List<CardInstance> targets, DuelistState who,
            DuelEngine engine)
        {
            var opp = engine.OpponentOf(who);
            switch (c.Zone)
            {
                case EffectZoneFilter.EitherGyMonsters:
                    return targets
                        .OrderByDescending(t => who.Graveyard.Contains(t) ? 1 : 0)
                        .ThenByDescending(t => t.CurrentAtk)
                        .First();
                case EffectZoneFilter.OpponentGyMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
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
            EffectZoneFilter.OpponentGyMonsters => EffectTargetKind.MonsterInOppGy,
            EffectZoneFilter.FieldSpellTraps => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.OppFaceUpMonsters when c.Action == EffectActionKind.EffectDamageBothFromOriginalAtk
                => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
            EffectZoneFilter.OppFaceUpMonsters => EffectTargetKind.OppFaceUpMonster,
            EffectZoneFilter.FieldAnyMonster => EffectTargetKind.AnyMonsterOnField,
            EffectZoneFilter.ControllerGySpells => EffectTargetKind.SpellInYourGy,
            EffectZoneFilter.ControllerGyTraps => EffectTargetKind.TrapInYourGy,
            EffectZoneFilter.AnyCardOnField => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.ControllerHandMonsters => EffectTargetKind.DiscardMonsterInHand,
            EffectZoneFilter.ControllerGyMonsters => EffectTargetKind.MonsterInEitherGy,
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
            if (fromHand)
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
                if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
                {
                    var zi = engine.FirstEmptySpellTrap(who);
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
