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
        /// Card drawn for the most recent Draw Phase normal draw (Drop Off).
        /// Set in <see cref="NotifyDraw"/> when Phase is Draw and the turn player draws;
        /// consumed by <see cref="EffectActionKind.DiscardJustDrawnCard"/>.
        /// </summary>
        static CardInstance _lastNormalDrawCard;
        static DuelistState _lastNormalDrawOwner;

        /// <summary>Test / soft hook: remember which card was the Draw Phase normal draw.</summary>
        public static void RememberNormalDrawCard(DuelistState drawer, CardInstance drawn)
        {
            _lastNormalDrawOwner = drawer;
            _lastNormalDrawCard = drawn;
        }

        /// <summary>Test / soft hook: the remembered Draw Phase normal-draw card (may be null).</summary>
        public static CardInstance LastNormalDrawCard => _lastNormalDrawCard;

        /// <summary>
        /// Resolve Drop Off-style OpponentNormalDrawInDrawPhase clauses on a Set/face-up card.
        /// Does not open a response window (Draw Phase window is a DuelEngine concern).
        /// </summary>
        public static bool TryResolveOpponentNormalDrawEffect(DuelEngine engine, DuelistState controller,
            CardInstance card)
        {
            if (engine == null || controller == null || card?.Def == null || engine.GameOver) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null) return false;
            var clauses = prog.ClausesFor(EffectTiming.OpponentNormalDrawInDrawPhase);
            if (clauses.Count == 0) return false;
            var dummy = false;
            foreach (var c in clauses)
                ApplyClause(engine, controller, card, c, null, ref dummy, ref dummy);
                        return true;
        }

        /// <summary>
        /// Resolve Heart of the Underdog-style ControllerNormalDrawInDrawPhase clauses on a face-up card.
        /// Soft / test hook — Draw Phase window remains a DuelEngine concern.
        /// Fail-closed unless the remembered just-drawn card is a Normal Monster in the controller's hand.
        /// </summary>
        public static bool TryResolveControllerNormalDrawNormalMonsterEffect(DuelEngine engine,
            DuelistState controller, CardInstance card)
        {
            if (engine == null || controller == null || card?.Def == null || engine.GameOver) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null) return false;
            var clauses = prog.ClausesFor(EffectTiming.ControllerNormalDrawInDrawPhase);
            if (clauses.Count == 0) return false;
            var just = _lastNormalDrawCard;
            if (just?.Def == null || _lastNormalDrawOwner != controller ||
                controller.Hand == null || !controller.Hand.Contains(just) ||
                !just.Def.IsNormalMonster)
                return false;
            var dummy = false;
            foreach (var c in clauses)
                ApplyClause(engine, controller, card, c, null, ref dummy, ref dummy);
            return true;
        }

        /// <summary>
        /// Face-up Field Spell mandatory/trigger on NS/SS (Harpies' Hunting Ground).
        /// Fires after the summon response window so Torrential can destroy the Field first.
        /// </summary>
        public static bool TryTriggerFaceUpFieldSummon(DuelEngine engine, DuelistState summoner,
            CardInstance summoned)
        {
            if (engine == null || summoned == null) return false;
            if (engine.IsAwaitingEffectTarget) return false;
            // SummonTriggersAndTributes batch (v163): King Tiger Wanghu, a face-up monster host answering a Normal /
            // Special Summon of a monster with 1400 or less ATK.
            SummonTriggersAndTributesRuntime.OnSummonSettled(engine, summoner, summoned);
            // MonsterNegationAndControlLocks batch: Stumbling — the summoned monster is changed to Defense Position.
            MonsterNegationAndControlLocksRuntime.OnSummonSettled(engine, summoner, summoned);
            if (engine.GameOver || engine.IsAwaitingEffectTarget) return false;
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
                // Cold Wave (LocksAndZones batch): the Field Spell's Summon trigger is an activated Spell effect
                // (ygopro TRIGGER_F) and cannot be activated under the lock.
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, field)) continue;
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

        /// <summary>
        /// Sangan / Witch name-lock: record added name for the rest of this turn.
        /// Turn-scoped via CannotActivateCardNamesTurnNumber == engine.TurnNumber.
        /// </summary>
        public static void LockActivateNameThisTurn(DuelEngine engine, DuelistState who, string cardName)
        {
            if (engine == null || who == null || string.IsNullOrWhiteSpace(cardName)) return;
            if (who.CannotActivateCardNamesThisTurn == null)
                who.CannotActivateCardNamesThisTurn = new System.Collections.Generic.List<string>();
            who.CannotActivateCardNamesTurnNumber = engine.TurnNumber;
            var exists = false;
            foreach (var n in who.CannotActivateCardNamesThisTurn)
            {
                if (string.Equals(n, cardName, System.StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
                who.CannotActivateCardNamesThisTurn.Add(cardName);
            engine.Log(
                $"{who.Name} cannot activate cards/effects named \"{cardName}\" " +
                "for the rest of this turn.");
        }

        /// <summary>True when who is name-locked from activating cardName this turn.</summary>
        public static bool IsActivateNameLockedThisTurn(DuelEngine engine, DuelistState who, string cardName)
        {
            if (engine == null || who?.CannotActivateCardNamesThisTurn == null ||
                string.IsNullOrWhiteSpace(cardName))
                return false;
            if (who.CannotActivateCardNamesTurnNumber != engine.TurnNumber)
                return false;
            foreach (var n in who.CannotActivateCardNamesThisTurn)
            {
                if (string.Equals(n, cardName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }



        /// <summary>
        /// A Spell/Trap already face-up in its controller's Spell &amp; Trap Zone was activated when it was
        /// placed or flipped. It may only activate an ignition or quick effect of its own (Magical Labyrinth's
        /// "Tribute the equipped monster", a Spell Counter cost, a "Once per turn" or Quick Effect clause),
        /// never the card itself again (v141 fuzz audit: re-activating Swords of Revealing Light reset its
        /// countdown, Snatch Steal / Premature Burial made a second Chain Link and ended up in two places).
        /// Wave-Motion Cannon's face-up ignition has its own gate
        /// (<see cref="PerCountBurnsRuntime.IsFaceUpFieldIgnitionCard"/>).
        /// </summary>
        public static bool FaceUpCardCannotActivateAgain(DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog)
        {
            if (fromHand || who == null || card?.Def == null || !card.FaceUp || card.Def.IsMonster ||
                !(card.Def.IsSpell || card.Def.IsTrap) || !who.TryFindSpellTrap(card, out _))
                return false;
            if (PerCountBurnsRuntime.IsFaceUpFieldIgnitionCard(who, card, prog)) return false;
            // FaceUpQuickEffects batch (v160): a face-up Quick Effect (Type Zero Magic Crusher, Skull Lair).
            if (FaceUpQuickEffectsRuntime.IsFaceUpQuickCard(who, card, prog)) return false;
            if (prog == null || !prog.FullyCompiled) return true;
            return !prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null &&
                ((c.RequiresTributeEquippedMonster && card.EquippedTo != null) ||
                 c.RequiresRemoveSpellCounters > 0 || c.OncePerTurn || c.IsQuickEffect));
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

            if (IsActivateNameLockedThisTurn(engine, who, card.Name))
            {
                reason = $"Cannot activate \"{card.Name}\" (name locked this turn).";
                return false;
            }

            // SimpleTargetUtility batch (v173): Fairy of the Spring — this specific card cannot be
            // activated for the rest of the turn it was added to the hand. Instance-scoped (the printed
            // "That Equip Spell Card"), stamped on the card by AddFromGyToHand; self-expiring.
            if (card.ActivationBlockedUntilTurn != 0 &&
                card.ActivationBlockedUntilTurn == engine.TurnNumber)
            {
                reason = $"Cannot activate \"{card.Name}\" this turn (blocked when it was added).";
                return false;
            }

            // Jinzo / Spell Canceller: that card kind cannot be activated (ContinuousNegation batch). Checked
            // before the face-up field ignition below, so "their effects on the field" are locked too.
            if (ContinuousNegationRuntime.ActivationLocked(engine, who, card, out var lockReason))
            {
                reason = lockReason;
                return false;
            }

            // World Suppression / Mystic Probe / Metal Detector: the effects of a face-up card of a family
            // negated this turn cannot be activated (activating a Set card or one from the hand is still
            // legal; it resolves without effect). Checked before the face-up field ignition below.
            // EquipModel batch: a monster acting as an Equip Card (a Union) negated by Armored Glass cannot either.
            if (!fromHand && card.FaceUp &&
                (card.Def.IsSpell || card.Def.IsTrap || EquipModelRuntime.IsEquipCardShape(card)) &&
                TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, card))
            {
                reason = "Its effects are negated this turn.";
                return false;
            }

            // Imperial Order / Royal Decree / Jinzo negate this face-up card: its own effects (Magical Labyrinth's
            // Tribute ignition) cannot be activated either (v141 cross-group audit; Wave-Motion Cannon checks
            // IsNegated itself). Set cards and cards in the hand stay legal, as above.
            if (!fromHand && card.FaceUp && (card.Def.IsSpell || card.Def.IsTrap) &&
                ContinuousNegationRuntime.NegatesEffectActivationOnField(engine, card))
            {
                reason = "Its effects are negated.";
                return false;
            }

            // PerCountBurns (v141): a face-up Spell/Trap with its own Main Phase ignition (Wave-Motion Cannon)
            // activates that effect from the field, never the card a second time.
            if (PerCountBurnsRuntime.IsFaceUpFieldIgnitionCard(who, card, prog))
                return PerCountBurnsRuntime.CanActivateFaceUpFieldIgnition(engine, who, card, prog, out reason);

            // FaceUpQuickEffects batch (v160): a face-up Spell/Trap's Quick Effect (Spell Speed 2) — open state on
            // either turn, the opponent's windows and chain responses (Type Zero Magic Crusher, Skull Lair).
            if (FaceUpQuickEffectsRuntime.IsFaceUpQuickCard(who, card, prog))
                return FaceUpQuickEffectsRuntime.CanActivateQuick(engine, who, card, prog, out reason);

            // ArchfiendRiders batch: a hand Quick Effect answering a named monster's destruction (Desrook Archfiend).
            if (ArchfiendRidersRuntime.IsHandEventCard(who, card, fromHand, prog))
                return ArchfiendRidersRuntime.CanActivateHandEvent(engine, who, card, prog, out reason);

            // TargetNegationsAndHandQuickEffects batch (v163): a free-chain Quick Effect from the hand (Charm of Shabti).
            if (TargetNegationsAndHandQuickEffectsRuntime.IsHandQuickCard(who, card, fromHand, prog))
                return TargetNegationsAndHandQuickEffectsRuntime.CanActivateHandQuick(engine, who, card, prog, out reason);

            // UnionRiders batch (v175): a Union in its Union state with an ignition from the S/T Zone (Kiryu, Second Goblin)
            // can be activated for that ignition even after its once-per-turn equip/unequip was used; a Union another
            // card equipped (Union Rider) cannot be activated at all.
            if (UnionRidersRuntime.TryCanActivate(engine, who, card, fromHand, prog, out var unionOk,
                    out var unionReason))
            {
                reason = unionReason;
                return unionOk;
            }

            if (FaceUpCardCannotActivateAgain(who, card, fromHand, prog))
            {
                reason = "Already face-up on the field: the card cannot be activated again.";
                return false;
            }

            var activate = prog.ClausesFor(EffectTiming.Activate);
            var attack = prog.ClausesFor(EffectTiming.AttackDeclared);
            var summon = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
            var dmg = prog.ClausesFor(EffectTiming.DamageCalculation);
            var takeLp = prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
            var chainNeg = prog.ClausesFor(EffectTiming.ChainLinkActivated);
            // SummonNegation batch (v141): "When a monster(s) would be Summoned" (Solemn Judgment / Horn of Heaven).
            var summonNeg = prog.ClausesFor(EffectTiming.MonsterWouldBeSummoned);
            var continuousPlay = card.Def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                                 engine.PendingResponse == null;
            // HandDisruption batch (v142): Appropriate's activation belongs to the "opponent drew outside either
            // Draw Phase" window (its OpponentDrewOutsideDrawPhase activation condition refuses every other one).
            var drawWindow = HandDisruptionRuntime.AnswersDrawWindow(prog);
            // A monster on the field cannot use its hand-only clauses (The Fiend Megacyber).
            if (card.Def.IsMonster && !fromHand)
                activate = activate.FindAll(c => c != null && !c.ActivatesFromHand);
            if (activate.Count == 0 && attack.Count == 0 && summon.Count == 0 && dmg.Count == 0 &&
                takeLp.Count == 0 && chainNeg.Count == 0 && summonNeg.Count == 0 && !continuousPlay &&
                !drawWindow)
            {
                reason = "No activatable clauses in learned text.";
                return false;
            }

            if (!ActivationConditionsMet(engine, who, card, prog, out var condReason))
            {
                reason = condReason;
                return false;
            }

            // RuleLocks batch: Jowgen the Spiritualist / Jam Breeding Machine — an activation that only Special Summons
            // cannot be activated by a player who cannot Special Summon.
            if (RuleLocksRuntime.ActivationLockedBySummonLock(engine, who, card, fromHand, prog, out var summonLockReason))
            {
                reason = summonLockReason;
                return false;
            }

            // Chain-response (monster QE or Counter Trap) before ordinary combat-response paths.
            if (engine.PendingResponse != null && engine.PendingResponse.Timing == ResponseTiming.ChainResponse)
                return chainNeg.Count > 0
                    ? CanActivateChainResponse(engine, who, card, prog, out reason)
                    : CanActivateFreeChainOnChain(engine, who, card, fromHand, prog, out reason);

            // SimpleSpellTraps batch: Burst Breath's Tribute / Emergency Provisions' sends are paid as the card is seated
            // in a response window (SimpleSpellTrapsRuntime.BeginSeatCost): a window offers it only when that can be paid.
            if (engine.PendingResponse != null)
            {
                var seatCost = SimpleSpellTrapsRuntime.ResponseCostClause(activate);
                if (seatCost != null && !IgnitionCostLegal(engine, who, card, seatCost, out reason))
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

                // The answer's discard cost is paid as it is seated (SeatResponseOnChain): with nothing to discard the
                // card cannot be activated. (SummonNegationRuntime checks its own Counter Traps' costs.)
                var timing = engine.PendingResponse.Timing;
                var answer = timing == ResponseTiming.AttackDeclared
                    ? attack.Concat(AttackWindowFreeChainClauses(card, prog, activate)).ToList()
                    : timing == ResponseTiming.MonsterSummoned
                        ? summon
                        : timing == ResponseTiming.YouTakeDamage
                            ? takeLp
                            : timing == ResponseTiming.OpponentOpenState ||
                              timing == ResponseTiming.SpellTrapDestroyedByOpponent
                                ? activate
                                : null;
                if (!ResponseDiscardPayable(engine, who, card, answer))
                {
                    reason = "No card in hand to discard.";
                    return false;
                }

                // SpellCounterSpenders batch: the seat pays the side-wide counter cost only when it is payable one way.
                if (!SpellCounterSpendersRuntime.ResponseSeatCostOk(engine, who, answer, out var counterWhy))
                {
                    reason = counterWhy;
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
                    if (attack.Count == 0 && !AutoTargetsAvailable(engine, who, card, activate))
                    {
                        reason = "No legal targets.";
                        return false;
                    }

                    foreach (var c in activate)
                    {
                        if (!NameGateSatisfied(engine, who, c))
                        {
                            reason = NameGateReason(c);
                            return false;
                        }
                    }

                    if (!ResponseTargetsAvailable(engine, who, card, prog, activate, out reason))
                        return false;
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

                    if (!AutoTargetsAvailable(engine, who, card, activate))
                    {
                        reason = "No legal targets.";
                        return false;
                    }

                    foreach (var c in activate)
                    {
                        if (!NameGateSatisfied(engine, who, c))
                        {
                            reason = NameGateReason(c);
                            return false;
                        }
                    }

                    if (!ResponseTargetsAvailable(engine, who, card, prog, activate, out reason))
                        return false;
                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage &&
                    takeLp.Count > 0)
                {
                    reason = "OK";
                    return true;
                }

                // SpellTrapDestroyedResponse (v142): only Traps whose activation condition is this event answer it
                // (Gust / Driving Snow — ActivationConditionsMet above checked the destroyed card kind), and only
                // with a Spell/Trap other than itself to target.
                if (engine.PendingResponse.Timing == ResponseTiming.SpellTrapDestroyedByOpponent)
                {
                    if (!SpellTrapDestroyedResponseRuntime.AnswersDestroyedWindow(prog))
                    {
                        reason = "Not legal for this response timing.";
                        return false;
                    }

                    if (!ResponseTargetsAvailable(engine, who, card, prog, activate, out reason))
                        return false;
                    reason = "OK";
                    return true;
                }

                // EquipModel batch: only Traps whose activation condition is the equip event (Armored Glass — checked
                // above by ActivationConditionsMet) answer the "a monster was equipped" window.
                if (engine.PendingResponse.Timing == ResponseTiming.MonsterEquipped)
                {
                    if (!EquipModelRuntime.AnswersEquipWindow(prog))
                    {
                        reason = "Not legal for this response timing.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                }

                // HandDisruption batch (v142): Appropriate — "Activate only when your opponent draws a card(s)
                // outside of either Draw Phase" (its activation condition already matched this window).
                if (engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase &&
                    HandDisruptionRuntime.AnswersDrawWindow(prog))
                {
                    reason = "OK";
                    return true;
                }

                // BattleWindows batch: Destruction Punch / Michizure answer their own windows only (their activation
                // conditions above checked the battle / the monster sent to the GY); Michizure needs a target.
                if (engine.PendingResponse.Timing == ResponseTiming.EndOfDamageStep ||
                    engine.PendingResponse.Timing == ResponseTiming.MonsterSentFromFieldToYourGy)
                {
                    if (!BattleWindowsRuntime.AnswersWindow(prog, engine.PendingResponse.Timing))
                    {
                        reason = "Not legal for this response timing.";
                        return false;
                    }

                    if (!ResponseTargetsAvailable(engine, who, card, prog, activate, out reason))
                        return false;
                    reason = "OK";
                    return true;
                }

                // EngineHygiene batch: Forced Requisition answers only "you discarded from your hand" (its activation
                // condition above checked the window).
                if (engine.PendingResponse.Timing == ResponseTiming.YouDiscardedFromHand)
                {
                    if (!DiscardEvents.AnswersDiscardWindow(prog))
                    {
                        reason = "Not legal for this response timing.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                }

                // ForcedActivationAndSubstitution batch (v163): Major Riot answers only "your monster(s) returned to your
                // hand by your opponent's effect" (its activation condition above checked the window and the field).
                if (engine.PendingResponse.Timing == ResponseTiming.MonstersReturnedToHandByOpponent)
                {
                    if (!MajorRiotRuntime.AnswersWindow(prog))
                    {
                        reason = "Not legal for this response timing.";
                        return false;
                    }

                    reason = "OK";
                    return true;
                }

                if (engine.PendingResponse.Timing == ResponseTiming.SummonNegation && summonNeg.Count > 0)
                    return SummonNegationRuntime.CanNegatePendingSummon(engine, who, card, summonNeg[0],
                        out reason);

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
                    (attack.Count > 0 || summon.Count > 0 || dmg.Count > 0 || takeLp.Count > 0 ||
                     summonNeg.Count > 0))
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
                else if (!NameGateSatisfied(engine, who, c))
                {
                    reason = NameGateReason(c);
                    return false;
                }

                if (c.RequiresDiscardCost &&
                    CollectDiscardCost(who, c.DiscardCostAttribute, card).Count < DiscardNeed(c))
                {
                    reason = c.DiscardCostAttribute == "*"
                        ? "No card in hand to discard."
                        : IgnitionCostsAndSimpleSpellsRuntime.NamedHandCardName(c.DiscardCostAttribute) is { } costName
                        ? $"No \"{costName}\" in hand to send to the GY."
                        : string.IsNullOrEmpty(c.DiscardCostAttribute)
                            ? "No monster in hand to discard."
                            : $"No {c.DiscardCostAttribute} monster in hand to discard.";
                    return false;
                }

                // DcrSpellTrapUtility batch: Spell Reproduction — the N cards sent as the cost are Spells.
                if (DcrSpellTrapUtilityRuntime.IsSpellSendCost(c) &&
                    DcrSpellTrapUtilityRuntime.SpellsForHandCost(who, card).Count < c.CostSendSpellsFromHandCount)
                {
                    reason = $"Needs {c.CostSendSpellsFromHandCount} other Spell Cards in your hand to send to the GY.";
                    return false;
                }

                if (c.DiscardCostCount > 0 &&
                    who.Hand.Count(h => h != null && h != card) < c.DiscardCostCount)
                {
                    reason = $"Needs {c.DiscardCostCount} other cards in your hand to discard.";
                    return false;
                }

                if (c.RequiresRemoveSpellCounters > 0 && card.Counters < c.RequiresRemoveSpellCounters)
                {
                    reason = $"Needs {c.RequiresRemoveSpellCounters} Spell Counter(s).";
                    return false;
                }

                // SpellCounterSpenders batch: "Remove N Spell Counters on your side of the field".
                if (c.RemoveSpellCountersOnYourSide > 0 &&
                    !SpellCounterSpendersRuntime.SideCostPayable(engine, who, c.RemoveSpellCountersOnYourSide))
                {
                    reason = $"Needs {c.RemoveSpellCountersOnYourSide} Spell Counter(s) on your side of the field.";
                    return false;
                }

                if (c.RequiresSpellCounters > 0 && card.Counters < c.RequiresSpellCounters)
                {
                    reason = $"Needs {c.RequiresSpellCounters} Spell Counter(s).";
                    return false;
                }

                // UnionRiders batch (v175): a Union equipped by another card's effect (Union Rider) is not in its Union
                // state and cannot unequip itself.
                if (c.Action == EffectActionKind.UnequipThisSpecialSummon &&
                    (card.EquippedTo == null || UnionRidersRuntime.UnionTextOff(card)))
                    continue;
                if (c.Action == EffectActionKind.EquipThisToTarget && card.EquippedTo != null)
                    continue;
                // Magical Labyrinth: "Tribute the equipped monster" exists only while this card is equipped.
                if (c.RequiresTributeEquippedMonster && card.EquippedTo == null)
                    continue;
                if (!TributeSummonsRuntime.ActivationLegal(engine, who, card, c, out reason))
                    return false;
                // Relinquished family (Ignis c64631466 eqcon): max. 1 — refuse while a monster it equipped with this
                // effect is still equipped (other Equip Cards on it do not count).
                if (c.Action == EffectActionKind.EquipTargetToThis && AbsorbedEquipOf(card) != null)
                {
                    reason = "Already equipped (max. 1).";
                    return false;
                }

                if (c.RequiresBattlePhase && engine.Phase != DuelPhase.Battle)
                {
                    reason = "Only during the Battle Phase.";
                    return false;
                }

                // Light of Intervention (LocksAndZones batch): "change this card to face-down Defense Position"
                // cannot be activated while monsters cannot be flipped face-down.
                if (c.Action == EffectActionKind.SetThisFaceDownDefense &&
                    LocksAndZonesRuntime.CannotTurnFaceDown(engine, card))
                {
                    reason = "Monsters cannot be flipped face-down now.";
                    return false;
                }

                if (c.RequiresOpponentHandAtLeast > 0)
                {
                    var oppHand = engine.OpponentOf(who)?.HandCount ?? 0;
                    if (oppHand < c.RequiresOpponentHandAtLeast)
                    {
                        reason = $"Your opponent must have at least {c.RequiresOpponentHandAtLeast} cards in hand.";
                        return false;
                    }
                }

                if (c.Action == EffectActionKind.MagicalHatsStyle &&
                    !MagicalHatsLegal(engine, who, c, out reason))
                    return false;

                if (!IgnitionCostLegal(engine, who, card, c, out reason))
                    return false;

                // SimpleSpellTraps batch: a targeted bullet without a target leaves the other bullet (Dragon's Gunfire).
                if (c.RequiresTargetChoice && !SimpleSpellTrapsRuntime.TargetedBulletHasAlternative(activate, c) &&
                    !HasLegalTargetsConsideringDiscard(engine, who, c, card))
                {
                    reason = "No legal targets.";
                    return false;
                }

                if (c.RequiresTargetChoice && MinTargets(c) > 1 &&
                    CollectTargets(engine, who, c, card).Count < MinTargets(c))
                {
                    reason = $"Needs {MinTargets(c)} legal targets.";
                    return false;
                }

                // Book of Life / The Shallow Grave: second-zone targets must exist to activate.
                if (c.RequiresSecondTarget &&
                    CollectTargetsForZone(engine, who, c, card, c.SecondZone).Count == 0)
                {
                    reason = "No legal second target.";
                    return false;
                }

                if (c.Action == EffectActionKind.TakeControlUntilEndPhase &&
                    engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    reason = "No Monster Zone to take control.";
                    return false;
                }

                if (c.TakeControlOfTarget && engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    reason = "No Monster Zone to take control.";
                    return false;
                }

                // Token Thanksgiving (c57182235 s.target chk: a Token in either Monster Zone). Token Feastevil's
                // chk is true, so it keeps TokenDestroyGainsLp false and stays activatable with no Token.
                if (c.Action == EffectActionKind.DestroyTokensInflictPer && c.TokenDestroyGainsLp &&
                    !who.MonstersOnField().Any(m => m != null && m.IsToken) &&
                    !(engine.OpponentOf(who)?.MonstersOnField().Any(m => m != null && m.IsToken) ?? false))
                {
                    reason = "No Token on the field.";
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

                if ((c.Action == EffectActionKind.SpecialSummonFromGy ||
                     c.Action == EffectActionKind.SpecialSummonFromHand ||
                     c.Action == EffectActionKind.SpecialSummonFromDeck ||
                     c.Action == EffectActionKind.SpecialSummonChosenFromHandOrDeck) &&
                    engine.FirstEmpty(who.MonsterZones) < 0 &&
                    !CostFreesMonsterZone(engine, who, card, c))
                {
                    reason = "No free Monster Zone.";
                    return false;
                }

                // The Shallow Grave: each player SS to their field — opponent also needs a free MZ.
                if (c.SummonToTargetsOwner &&
                    c.RequiresSecondTarget &&
                    (c.Action == EffectActionKind.SpecialSummonFromGy ||
                     c.SecondAction == EffectActionKind.SpecialSummonFromGy))
                {
                    var other = engine.OpponentOf(who);
                    if (other == null || engine.FirstEmpty(other.MonsterZones) < 0)
                    {
                        reason = "Opponent has no free Monster Zone.";
                        return false;
                    }
                }
            }

            // Normal Spells / ignition: Main Phase.
            // A standby-only ignition (Blast Juggler) is legal only in that Standby.
            // Traps, Quick-Play Spells, and monster Quick Effects: Main or Battle.
            var isQuickPlay = SpellTrapEffects.IsQuickPlay(card.Def) || isQuick;
            var isTrap = card.Def.IsTrap;
            var standbyOnly = false;
            foreach (var c in activate)
            {
                if (c != null && c.OnlyDuringOwnStandby)
                    standbyOnly = true;
            }

            if (standbyOnly)
            {
                if (engine.Phase != DuelPhase.Standby || engine.TurnPlayer != who)
                {
                    reason = "Only during your Standby Phase.";
                    return false;
                }
            }
            else if (isTrap || isQuickPlay)
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

            if (engine.Phase != DuelPhase.Main1 && activate.Exists(c => c != null && c.OnlyDuringMainPhase1))
            {
                reason = "Only during your Main Phase 1.";
                return false;
            }

            if (isTrap && TrapActivationSealed(engine, who, card))
            {
                reason = engine.SummonResponseTrapsSealed
                    ? "No Trap Cards can be activated."
                    : "Your opponent prevents Trap Cards during the Battle Phase.";
                return false;
            }

            reason = "OK";
            // LocksRedirectsAndActivity: record this activation for the ACTIVITY_CHAIN counter (a cost may
            // require "you have not activated any other cards or effects this turn"). ActivationCount
            // excludes this card's own in-progress check.
            LocksRedirectsAndActivityRuntime.NoteActivation(engine, who, card);
            return true;
        }

        /// <summary>
        /// Mirage Dragon seals the opponent's Traps for the whole Battle Phase.
        /// Invasion of Flames seals Traps only in the response window to that Normal Summon.
        /// </summary>
        public static bool TrapActivationSealed(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null || !card.Def.IsTrap) return false;
            if (engine.SummonResponseTrapsSealed) return true;
            if (engine.Phase != DuelPhase.Battle) return false;
            var opp = engine.OpponentOf(who);
            if (opp == null) return false;
            foreach (var m in opp.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.IsNegated || m.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c != null &&
                        c.Action == EffectActionKind.OpponentCannotActivateTrapsDuringBattle)
                        return true;
                }
            }

            return false;
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

            // ChainLinkAnswers batch: answers whose legality reads the answered link — Jar Robber (a named activation),
            // Spell Shield Type-8 (each bullet on its own condition), My Body as a Shield (an opponent's card activation or
            // monster effect that would destroy a monster), Dramatic Rescue (a targeted face-up series monster); a
            // Quick-Play answer may come from the hand during its controller's turn.
            if (ChainLinkAnswersRuntime.TryCanAnswer(engine, who, card, clauses, target, out var answerOk,
                    out var answerReason))
            {
                reason = answerReason;
                return answerOk;
            }

            // EffectDamageEvents batch (v162): Barrel Behind the Door / Trap of Board Eraser answer a link (a card activation or
            // a monster effect) that would inflict effect damage to their controller.
            if (EffectDamageEventsRuntime.IsDamageAnswer(clauses[0]))
                return EffectDamageEventsRuntime.CanAnswerDamageLink(engine, who, card, clauses[0], target, out reason);

            // TargetNegationsAndHandQuickEffects batch (v163): a Chain answer activated from the hand (Gravekeeper's Watcher).
            if (TargetNegationsAndHandQuickEffectsRuntime.TryCanAnswerFromHand(engine, who, card, clauses[0], target,
                    out var handAnswerOk, out var handAnswerReason))
            {
                reason = handAnswerReason;
                return handAnswerOk;
            }

            // PerCountBurns (v141): "When a Spell/Trap Card is activated" answers a card's activation, never the
            // effect of a face-up Spell on the field (Wave-Motion Cannon's ignition link).
            if (PerCountBurnsRuntime.IsCardEffectLink(target) ||
                // FaceUpQuickEffects batch (v160): a face-up Spell/Trap's Quick Effect link (Type Zero, Skull Lair).
                FaceUpQuickEffectsRuntime.IsQuickEffectLink(target) ||
                // TriggerChainResponses batch (v163): a trigger effect's link (FLIP, Life Absorbing Machine's Standby
                // effect) is a card effect, not a card activation.
                TriggerChainResponsesRuntime.IsTriggerLink(target))
            {
                reason = "The last Chain Link is a card effect, not a card activation.";
                return false;
            }

            return CanAnswerChainLinkWithClause(engine, who, card, clauses[0], target, out reason);
        }

        /// <summary>
        /// The checks of <see cref="CanActivateChainResponse"/> for one ChainLinkActivated <paramref name="clause"/> answering
        /// <paramref name="target"/> (the answered card's kind, the clause's conditions, the answering card's place and
        /// costs). ChainLinkAnswers batch: Spell Shield Type-8 asks it for each bullet.
        /// </summary>
        internal static bool CanAnswerChainLinkWithClause(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause clause, ChainLink target, out string reason)
        {
            reason = "OK";
            if (clause == null || target?.Card?.Def == null)
            {
                reason = "Not a chain-activation response.";
                return false;
            }

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

            // SummonNegation batch (v141): Magic Drain answers only the opponent; Solemn Judgment / Magic Drain
            // answer only the activation of a Spell/Trap Card, never an effect of one already face-up.
            if (!SummonNegationRuntime.ChainNegationAnswers(clause, who, target, out reason))
                return false;

            // NegationsAndGySummons batch: Maryokutai (only during the opponent's turn; Tribute this card) and
            // Riryoku Field (the Spell targets exactly 1 monster on the field).
            if (!NegationsAndGySummonsRuntime.ChainAnswerAllowed(engine, who, card, clause, target, out reason))
                return false;

            // SpellTrapAnswersAndActivationLocks batch: Trap Jammer (only during the Battle Phase), Judgment of Anubis (a Spell
            // Card activation that would destroy a Spell/Trap Card on the field), Spell Vanishing (the opponent has a card in
            // their hand or Deck).
            if (!SpellTrapAnswersAndActivationLocksRuntime.ChainAnswerAllowed(engine, who, card, clause, target, out reason))
                return false;

            // "This card's activation … cannot be negated" (Amplifier, Spiritualism): no negate-activation
            // answer may be chained to it.
            if (clause.Action == EffectActionKind.NegateActivation &&
                !ActivationCanBeNegated(target.Card))
            {
                reason = "That card's activation cannot be negated.";
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

            if (clause.PayLpAmount > 0 && who.LifePoints < clause.PayLpAmount &&
                !ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
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

            // SpellCounterSpenders batch: Anti-Spell's Spell Counters on your side of the field.
            if (clause.RemoveSpellCountersOnYourSide > 0 &&
                !SpellCounterSpendersRuntime.SideCostPayable(engine, who, clause.RemoveSpellCountersOnYourSide))
            {
                reason = $"Needs {clause.RemoveSpellCountersOnYourSide} Spell Counter(s) on your side of the field.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// False when the card's Activate clause says its activation cannot be negated
        /// (<see cref="EffectClause.ActivationNegatable"/> = false).
        /// </summary>
        static bool ActivationCanBeNegated(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            if (prog == null || !prog.FullyCompiled) return true;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && !c.ActivationNegatable)
                    return false;
            return true;
        }

        /// <summary>
        /// TCG chain rule for Spell Speed 2+ cards without a Counter-Trap clause: a Set Trap, a Set
        /// Quick-Play Spell, or a Quick-Play Spell from the hand during its controller's own turn may be
        /// activated in response to the last Chain Link (Speed 2 chains to Speed 1 or 2; nothing but
        /// Speed 3 chains to Speed 3). The card's normal Activate effect becomes the new link.
        /// Not supported in a timed chain window yet (refused, so the card is simply not offered):
        /// costs that need a pick (discard, Tribute, send, banish), multi-target activations, coin
        /// calls, Magical Hats, Ritual / Fusion procedures.
        /// </summary>
        public static bool CanActivateFreeChainOnChain(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || prog == null || !prog.FullyCompiled)
            {
                reason = "Invalid context.";
                return false;
            }

            var def = card.Def;
            if (!def.IsTrap && !SpellTrapEffects.IsQuickPlay(def))
            {
                reason = "Only Traps and Quick-Play Spells can be chained.";
                return false;
            }

            if (prog.HasTiming(EffectTiming.ChainLinkActivated))
            {
                reason = "Answers the chain through its own chain clause.";
                return false;
            }

            if (FaceUpCardCannotActivateAgain(who, card, fromHand, prog))
            {
                reason = "Already face-up on the field: the card cannot be activated again.";
                return false;
            }

            if (!prog.CanResolveAny ||
                (!SpellTrapEffects.IsCompiledFreeChain(def, prog) && !IsContinuousTrapPlacementOnly(def, prog)))
            {
                reason = "Not a free-chain activation.";
                return false;
            }

            // Soft residual: ActivationCondition (Fake Trap OppEffectWouldDestroyYourTrap)
            // must hold on free-chain-on-chain (CollectLegalResponseCards skips CanActivate).
            if (!ActivationConditionsMet(engine, who, card, prog, out reason))
                return false;

            var pr = engine.PendingResponse;
            if (pr == null || pr.Timing != ResponseTiming.ChainResponse || pr.Responder != who ||
                engine.Chain.Last == null)
            {
                reason = "Not your chain response.";
                return false;
            }

            // Michizure (BattleWindows batch) answers a monster this chain's activation sent to your GY (a Tribute
            // cost), which may be your own activation.
            if (pr.ChainAnswersOnly && !BattleWindowsRuntime.AnswersSendThisChainMade(engine, who, prog))
            {
                reason = "Only chain answers (Counter Traps …) onto your own activation.";
                return false;
            }

            var speed = OfficialEffectRegistry.SpeedOf(def);
            if (!engine.Chain.CanAddLink(speed, OfficialEffectRegistry.ClassOfActivation(def, false)))
            {
                reason = $"Spell Speed {(int)speed} cannot chain to Chain Link {engine.Chain.Last.LinkNumber}.";
                return false;
            }

            if (IsActivateNameLockedThisTurn(engine, who, card.Name))
            {
                reason = $"Cannot activate \"{card.Name}\" (name locked this turn).";
                return false;
            }

            if (fromHand)
            {
                if (def.IsTrap)
                {
                    reason = "Traps must be Set first.";
                    return false;
                }

                if (!who.Hand.Contains(card))
                {
                    reason = "Not in hand.";
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
            }
            else
            {
                if (!who.TryFindSpellTrap(card, out _))
                {
                    reason = "Not Set on the field.";
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

            if (def.IsTrap && TrapActivationSealed(engine, who, card))
            {
                reason = "Trap Cards cannot be activated now.";
                return false;
            }

            if (ContinuousNegationRuntime.ActivationLocked(engine, who, card, out var chainLockReason))
            {
                reason = chainLockReason;
                return false;
            }

            var activate = prog.ClausesFor(EffectTiming.Activate);
            foreach (var c in activate)
            {
                if (c == null) continue;
                if (c.Action == EffectActionKind.UnequipThisSpecialSummon &&
                    (card.EquippedTo == null || UnionRidersRuntime.UnionTextOff(card))) continue;
                if (c.Action == EffectActionKind.EquipThisToTarget && card.EquippedTo != null) continue;
                if (c.OpponentTurnOnly && engine.TurnPlayer == who)
                {
                    reason = "Only during opponent's turn.";
                    return false;
                }

                if (c.OnlyDuringOwnStandby)
                {
                    reason = "Only during your Standby Phase.";
                    return false;
                }

                if (c.RequiresBattlePhase && engine.Phase != DuelPhase.Battle)
                {
                    reason = "Only during the Battle Phase.";
                    return false;
                }

                // SimpleSpellTraps batch: Burst Breath Tributes as it is chained (SimpleSpellTrapsRuntime.BeginChainAnswerCost).
                if (SimpleSpellTrapsRuntime.PaysTributeWhenChained(c))
                {
                    if (!IgnitionCostLegal(engine, who, card, c, out reason))
                        return false;
                }
                // EngineResiduals (v163): a plain "Discard 1 card" cost (Raigeki Break, Spell Purification, Malice
                // Dispersion) is paid as the card is chained (EngineResidualsRuntime.PayChainDiscard; a human picks).
                else if (EngineResidualsRuntime.IsChainableDiscardCost(c))
                {
                    if (CollectDiscardCost(who, DiscardCostAttributeOf(c), card).Count < DiscardNeed(c))
                    {
                        reason = "No card in hand to discard.";
                        return false;
                    }
                }
                else if (HasStructuredIgnitionCost(c) || c.RequiresDiscardCost || c.RequiresSendNamedToGy ||
                         c.RequiresTributeCount > 0)
                {
                    reason = "Its cost needs a pick — not available in a chain window.";
                    return false;
                }

                if (c.Action == EffectActionKind.MagicalHatsStyle ||
                    c.Action == EffectActionKind.CoinCallDestroyOppOrSelf ||
                    c.Action == EffectActionKind.CoinCallDoubleOrHalveAtk ||
                    c.Action == EffectActionKind.RitualSummon ||
                    c.Action == EffectActionKind.FusionSummonRegistered)
                {
                    reason = "Not available in a chain window.";
                    return false;
                }

                if (c.RequiresLordOfDOnField && !LordOfDOnField(engine))
                {
                    reason = "Lord of D. must be on the field.";
                    return false;
                }

                if (!NameGateSatisfied(engine, who, c))
                {
                    reason = NameGateReason(c);
                    return false;
                }

                if (c.RequiresOpponentHandAtLeast > 0 &&
                    (engine.OpponentOf(who)?.HandCount ?? 0) < c.RequiresOpponentHandAtLeast)
                {
                    reason = $"Your opponent must have at least {c.RequiresOpponentHandAtLeast} cards in hand.";
                    return false;
                }

                if (c.RequiresTargetChoice)
                {
                    if (IsMultiPickClause(c))
                    {
                        reason = "Multi-target activation is not available in a chain window.";
                        return false;
                    }

                    if (CollectTargets(engine, who, c, card).Count == 0)
                    {
                        reason = "No legal targets.";
                        return false;
                    }
                }

                if ((c.TakeControlOfTarget || c.Action == EffectActionKind.TakeControlUntilEndPhase ||
                     c.Action == EffectActionKind.SpecialSummonFromGy ||
                     c.Action == EffectActionKind.SpecialSummonFromHand ||
                     c.Action == EffectActionKind.SpecialSummonFromDeck) &&
                    engine.FirstEmpty(who.MonsterZones) < 0)
                {
                    reason = "No free Monster Zone.";
                    return false;
                }
            }

            reason = "OK";
            return true;
        }

        /// <summary>
        /// A Continuous Trap whose FullyCompiled program has no Activate clause and only continuous clauses
        /// (Royal Decree, Imperial Order with its Standby upkeep, Gravity Bind, Spirit Barrier …). Its
        /// activation resolves by leaving the card face-up, and like every Continuous Trap it is a Spell
        /// Speed 2 free-chain activation (Lua EFFECT_TYPE_ACTIVATE + EVENT_FREE_CHAIN), so it may be chained
        /// to a Chain Link (ContinuousNegation batch).
        /// </summary>
        public static bool IsContinuousTrapPlacementOnly(CardDef def, CompiledCardProgram prog)
        {
            if (def == null || prog == null || !prog.FullyCompiled) return false;
            if (!def.IsTrap || !def.IsContinuousSpellOrTrap) return false;
            var any = false;
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (c.Timing == EffectTiming.ContinuousWhileFaceUp ||
                    // FaceUpQuickEffects batch (v160): a Quick Effect used later from the field, not by the activation.
                    c.Timing == EffectTiming.FaceUpQuickEffect)
                {
                    any = true;
                    continue;
                }

                // ContinuousTrapsAndSpiritSupport batch: a Standby trigger of the face-up card (Life Absorbing Machine,
                // Ominous Fortunetelling) is not part of the activation, which only places the card.
                if (ContinuousTrapsAndSpiritSupportRuntime.IsFaceUpTriggerOnly(c) ||
                    // StandbyBurnsAndDice batch (v162): Blind Destruction, Needle Wall, Graverobber's Retribution.
                    StandbyBurnsAndDiceRuntime.IsFaceUpTriggerOnly(c) ||
                    // PhaseTimersAndDeckTop batch (v163): Bottomless Shifting Sand's End Phase trigger and Standby check.
                    PhaseTimersAndDeckTopRuntime.IsFaceUpTriggerOnly(c))
                {
                    any = true;
                    continue;
                }

                // A Standby upkeep is a cost, not an activation window.
                if (c.Timing == EffectTiming.StandbyPhase &&
                    (c.Action == EffectActionKind.PayLpOrDestroyThis ||
                     c.Action == EffectActionKind.StandbyMaintenancePayLp))
                    continue;
                return false;
            }

            return any;
        }

        /// <summary>
        /// Chain a free-chain Trap / Quick-Play (see <see cref="CanActivateFreeChainOnChain"/>) to the last
        /// link. The target is chosen now — the AI auto-picks; a player picks while the chain window stays
        /// open (cancel takes the link back and re-offers the window) — then the opponent may respond.
        /// The effect resolves in chain order through the normal Activate path.
        /// </summary>
        public static bool ActivateFreeChainOnChain(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget)
        {
            if (!CanActivateFreeChainOnChain(engine, who, card, fromHand, prog, out var why))
            {
                engine?.Log($"{card?.Name ?? "Card"} cannot be chained: {why}");
                return false;
            }

            // EngineResiduals (v163): the discard is paid first (a human picks; Cancel = not activated, nothing paid).
            var discardCost = prog.ClausesFor(EffectTiming.Activate).Find(EngineResidualsRuntime.IsChainableDiscardCost);
            if (discardCost != null)
                return EngineResidualsRuntime.PayChainDiscard(engine, who, card, discardCost,
                    human: !(autoPickTarget || !who.IsPlayer),
                    stillLegal: () => CanActivateFreeChainOnChain(engine, who, card, fromHand, prog, out _),
                    then: paid => ChainFreeChainLink(engine, who, card, fromHand, prog, autoPickTarget, paid));
            return ChainFreeChainLink(engine, who, card, fromHand, prog, autoPickTarget, null);
        }

        /// <summary>
        /// <see cref="ActivateFreeChainOnChain"/> once its discard cost (if any) is paid: the link, its target and the
        /// opponent's window. A link whose discard was paid resolves as a seated response (no cost is paid again).
        /// </summary>
        static bool ChainFreeChainLink(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog, bool autoPickTarget, CardInstance paidDiscard)
        {
            var answered = engine.PendingResponse?.ChainTarget;
            var targeted = FirstPickTargetClause(prog.ClausesFor(EffectTiming.Activate));
            List<CardInstance> targets = null;
            if (targeted != null)
            {
                targets = CollectTargets(engine, who, targeted, card);
                if (targets.Count == 0) return false;
            }

            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, isTriggerWindow: false);
            var link = engine.Chain.AddLink(who, card, speed, cls,
                fromHand ? CardLocation.Hand : CardLocation.SpellTrapZone, fromHand,
                wasSet: !fromHand && !card.FaceUp, "chain-free");
            if (link == null) return false;
            link.TargetLink = answered;
            // EngineResiduals (v163): TryResolveSeatedResponse applies the Activate clauses without paying the cost again.
            if (paidDiscard != null)
                link.SeatedFrom = ResponseTiming.ChainResponse;
            TurnNegationTrapsRuntime.MarkActivationOnChain(card, fromHand); // continuous effects wait for resolution
            PlaceFaceUp(engine, who, card, fromHand);
            SpellCounterSpendersRuntime.PlaceOnActivation(engine, card, link); // Pitch-Black Power Stone
            engine.Log($"Chain {engine.Chain.Describe()} — {card.Name}");
            engine.GameEvents.Add(DuelGameEventKind.Activated, engine.TurnNumber, who, card, answered?.Card);

            // SimpleSpellTraps batch: Emergency Provisions sends / Burst Breath Tributes its cost now (a human picks while
            // the window waits).
            if (SimpleSpellTrapsRuntime.BeginChainAnswerCost(engine, who, card, fromHand, prog, autoPickTarget))
                return true;

            if (targeted != null)
            {
                if (!(autoPickTarget || !who.IsPlayer))
                {
                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        FromHand = fromHand,
                        WasSetOnField = !fromHand,
                        TargetKind = MapTargetKind(targeted),
                        ChainActivation = true
                    };
                    pending.LegalTargets.AddRange(targets);
                    // EngineResiduals (v163): Cancel at the target refunds the discard (SpellTrapEffects.CancelPending).
                    if (paidDiscard != null)
                        pending.PaidDiscards.Add(paidDiscard);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                var pick = AutoPick(targeted, targets, who, engine);
                if (pick != null)
                {
                    link.Target = pick;
                    link.Targets.Clear();
                    link.Targets.Add(pick);
                    // EquipModel batch: Tailor of the Fickle's monster target is a target of the link too.
                    EquipModelRuntime.AddMonsterTargetToLink(engine, link);
                }
            }

            engine.ClearPendingResponse();
            if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                engine.Chain.StartResolution();
                engine.ResolveChainStackForActivation();
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
            // HandDisruption batch (v142): Appropriate's activation only leaves it face-up — a copy removed before
            // its link resolved (Dust Tornado in response) is not put back on the field.
            if (timing == ResponseTiming.OpponentDrewOutsideDrawPhase && !who.TryFindSpellTrap(card, out _))
            {
                engine.Log($"{card.Name} is no longer on the field — its activation resolves with no effect.");
                return true;
            }
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
                            c.Action == EffectActionKind.PreventOpponentAttacksThisTurn ||
                            c.Action == EffectActionKind.CannotAttackFaceDownMonstersThisTurn ||
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
            else if (timing == ResponseTiming.SummonNegation)
            {
                // SummonNegation batch (v141): the Summon this link answered.
                clauses = prog.ClausesFor(EffectTiming.MonsterWouldBeSummoned);
                implicitTarget = link.SeatedSummoned;
            }
            else if (timing == ResponseTiming.YouTakeDamage)
                clauses = prog.ClausesFor(EffectTiming.YouTakeLifePointDamage);
            else if (timing == ResponseTiming.DamageCalculation)
                clauses = prog.ClausesFor(EffectTiming.DamageCalculation);
            else
                clauses = prog.ClausesFor(EffectTiming.Activate);

            // Target chosen when the link was activated (SeatResponseOnChain / TryResolveWithTarget).
            var chosen = link.Target ?? (link.Targets.Count > 0 ? link.Targets[0] : null);
            // SimpleSpellTraps batch: the ATK of the monster Burst Breath Tributed as it was seated is its DEF cap.
            SimpleSpellTrapsRuntime.TakePaidTribute(engine, card, clauses, out var seatedCostNumeric);
            foreach (var c in clauses)
            {
                if (c == null) continue;
                // Poison of the Old Man answering a window: only the bullet chosen when it was seated.
                if (PoisonMeteorCestusRuntime.SkipsBullet(card, c)) continue;
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
                else if (c.RequiresTargetChoice && timing != ResponseTiming.MonsterSummoned &&
                         (c.Timing != EffectTiming.AttackDeclared || chosen != null))
                    tgt = chosen; // Widespread Ruin pick at AttackDeclared when seated on chain
                ApplyClause(engine, who, card, c, tgt, ref dummyNeg, ref dummyEnd, seatedCostNumeric);
            }

            PoisonMeteorCestusRuntime.ForgetBullet(card);
            engine.ChainNegatedAttack |= dummyNeg;
            engine.ChainEndedBattle |= dummyEnd;
            FinishSpellTrap(engine, who, card,
                stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            return true;
        }

        /// <summary>
        /// A response answer is seated as a Chain Link (<see cref="SeatResponseOnChain"/>) when a clause has ":" or ";".
        /// A discard cost always ends with ";", so a clause carrying one is seated too, where the discard is paid:
        /// the inline answers (<see cref="BeginInlineResponseTargetPick"/>, <see cref="ResolveInlineAnswerTarget"/>)
        /// pay no cost.
        /// </summary>
        static bool ClauseMakesChainLink(List<EffectClause> clauses)
        {
            if (clauses == null || clauses.Count == 0) return false;
            for (var i = 0; i < clauses.Count; i++)
                if (clauses[i] != null && (clauses[i].MakesChainLink || HasDiscardCost(clauses[i])))
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

        /// <summary>
        /// Free-chain / trigger clauses a card may use to answer an AttackDeclared window: all its Activate
        /// clauses when it is a free-chain card, else only Waboku-style, opponent-turn and
        /// "attacks become direct" clauses.
        /// </summary>
        static List<EffectClause> AttackWindowFreeChainClauses(CardInstance card, CompiledCardProgram prog,
            List<EffectClause> activateClauses)
        {
            if (activateClauses == null) return new List<EffectClause>();
            return SpellTrapEffects.IsCompiledFreeChain(card?.Def, prog)
                ? activateClauses
                : activateClauses.FindAll(c =>
                    c != null &&
                    (c.Action == EffectActionKind.ApplyWabokuStyle ||
                     c.OpponentTurnOnly ||
                     c.Action == EffectActionKind.ForceOpponentDirectAttacksThisTurn ||
                     c.Action == EffectActionKind.PreventOpponentAttacksThisTurn ||
                     c.Action == EffectActionKind.CannotAttackFaceDownMonstersThisTurn));
        }

        /// <summary>
        /// First clause whose target the activating player must choose ("Target 1 …"). Clauses aimed at the
        /// attacking / battling monster take that monster implicitly and never need a pick.
        /// </summary>
        static EffectClause FirstPickTargetClause(List<EffectClause> clauses)
        {
            if (clauses == null) return null;
            foreach (var c in clauses)
            {
                if (c == null || !c.RequiresTargetChoice) continue;
                if (c.Zone == EffectZoneFilter.AttackingMonster ||
                    c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                    continue;
                return c;
            }

            return null;
        }

        /// <summary>A pick of more than one target (second target, "target 2 you control").</summary>
        static bool IsMultiPickClause(EffectClause c) =>
            c != null && (c.RequiresSecondTarget || c.ControllerTargetCount > 1);

        /// <summary>
        /// A targeted free-chain clause answering a response window needs a legal target now (DR-01);
        /// players pick one target here, so multi-target clauses are refused for them.
        /// </summary>
        static bool ResponseTargetsAvailable(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, List<EffectClause> activate, out string reason)
        {
            reason = null;
            var clauses = engine.PendingResponse?.Timing == ResponseTiming.AttackDeclared
                ? AttackWindowFreeChainClauses(card, prog, activate)
                : activate;
            var targeted = FirstPickTargetClause(clauses);
            if (targeted == null) return true;
            if (who.IsPlayer && IsMultiPickClause(targeted))
            {
                reason = "Multi-target activation is not available in this response window.";
                return false;
            }

            if (CollectTargets(engine, who, targeted, card).Count > 0) return true;
            reason = "No legal targets.";
            return false;
        }

        /// <summary>
        /// Put <paramref name="card"/> on the chain as the answer to <paramref name="timing"/>. The AI's
        /// response path (AiAutoRespond → BeginOrResolveManual) reaches here with no link yet, so the link
        /// is added here when missing; before, Chain.Last was null and the answer never resolved.
        /// Targeted clauses get their target now: the AI auto-picks; a player picks while the response
        /// window stays open (cancel re-offers it), and the pick becomes the link's target.
        /// A discard cost ("Discard 1 card; …", "Discard 1 card, then target …") is paid first, as the card is
        /// activated: the AI discards its least useful card, a player chooses (<see cref="ContinueResponseDiscardPick"/>),
        /// and with nothing to discard the activation is refused. Costs never note a discard for DiscardTriggers.
        /// </summary>
        /// <param name="pickClauses">Clauses whose target is chosen at activation (none for a summon answer,
        /// which targets the summoned monster).</param>
        internal static bool SeatResponseOnChain(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, ResponseTiming timing, CardInstance summoned, List<EffectClause> pickClauses,
            bool autoPickTarget)
        {
            var targeted = timing == ResponseTiming.MonsterSummoned ? null : FirstPickTargetClause(pickClauses);
            List<CardInstance> targets = null;
            CardInstance pick = null;
            var human = !(autoPickTarget || !who.IsPlayer);
            // SummonNegationRuntime pays the costs of its Counter Traps before it seats them.
            var discard = timing == ResponseTiming.SummonNegation ? null : pickClauses?.Find(HasDiscardCost);
            if (discard != null && !ResponseDiscardPayable(engine, who, card, pickClauses))
            {
                engine.Log($"{card.Name}: no card in hand to discard — cannot activate.");
                return false;
            }

            if (targeted != null)
            {
                targets = CollectTargets(engine, who, targeted, card);
                if (targets.Count == 0)
                {
                    engine.Log($"{card.Name}: no legal target — cannot activate.");
                    return false;
                }

                if (human && IsMultiPickClause(targeted))
                {
                    engine.Log($"{card.Name}: multi-target activation is not available in this response window.");
                    return false;
                }

                if (!human)
                    pick = AutoPick(targeted, targets, who, engine);
            }

            // Rock Bombardment: "Send 1 Rock monster from your Deck to the GY" is a cost here too; none, no activation.
            var deckCost = timing == ResponseTiming.SummonNegation
                ? null
                : pickClauses?.Find(x => x != null && x.RequiresSendRaceFromDeck);
            var deckFodder = deckCost != null ? CollectSendRaceFromDeck(engine, who, deckCost) : null;
            if (deckCost != null && deckFodder.Count == 0)
            {
                engine.Log($"{card.Name}: no {deckCost.RaceFilter} monster in your Deck to send — cannot activate.");
                return false;
            }

            var link = engine.Chain.Last;
            if (link == null || link.Card != card)
            {
                var speed = OfficialEffectRegistry.SpeedOf(card.Def);
                var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, isTriggerWindow: false);
                var from = fromHand
                    ? CardLocation.Hand
                    : card.Def != null && card.Def.IsMonster
                        ? CardLocation.MonsterZone
                        : CardLocation.SpellTrapZone;
                link = engine.Chain.AddLink(who, card, speed, cls, from, fromHand,
                    wasSet: !fromHand && !card.FaceUp, "response-seat");
                if (link == null)
                {
                    engine.Log($"{card.Name}: cannot be added to the chain now.");
                    return false;
                }
            }

            TurnNegationTrapsRuntime.MarkActivationOnChain(card, fromHand); // continuous effects wait for resolution
            PlaceFaceUp(engine, who, card, fromHand);
            SpellCounterSpendersRuntime.PlaceOnActivation(engine, card, link); // Pitch-Black Power Stone
            engine.Log($"Activate: {card.Name}.");
            engine.GameEvents.Add(DuelGameEventKind.Activated, engine.TurnNumber, who, card);
            link.SeatedFrom = timing;
            link.SeatedSummoned = summoned;
            // An Activate clause's LP cost is paid now (Archfiend's Roar answering an attack); the seated resolution
            // (TryResolveSeatedResponse) pays no cost. A cancelled target pick refunds it.
            var paidLp = ActivationLpCostRuntime.PayOnActivation(engine, who, card, link,
                CompiledEffectCache.GetOrCompile(card), pickClauses);
            if (engine.GameOver) return true;

            // SimpleSpellTraps batch: Burst Breath's Tribute / Emergency Provisions' sends are paid now (a human picks;
            // the window opens once they are paid).
            if (timing != ResponseTiming.SummonNegation &&
                SimpleSpellTrapsRuntime.BeginSeatCost(engine, who, card, fromHand, pickClauses, human, timing))
                return true;

            // BattleDestroyedGyRecovery batch (v162): Rope of Life's "Discard your entire hand (min. 1)" is paid now (no
            // choice); a cancelled target pick refunds it (AttachSeatCost).
            if (timing != ResponseTiming.SummonNegation)
                BattleDestroyedGyRecoveryRuntime.PaySeatCost(engine, who, card, pickClauses);
            // SpellCounterSpenders batch: Miracle Restoring's Spell Counters. The AI or a forced distribution pays now; a
            // human with a real choice taps the cards (ContinueResponseDiscardPick), then picks the target.
            if (SpellCounterSpendersRuntime.BeginSeatCost(engine, who, card, fromHand, pickClauses,
                    human && discard == null && deckCost == null, timing, summoned, targeted, paidLp))
                return true;

            // Poison of the Old Man: the bullet is chosen as it is activated (Duel.SelectOption in its target step).
            if (targeted == null && discard == null && deckCost == null &&
                PoisonMeteorCestusRuntime.OfferSeatedBullet(engine, who, card, pickClauses, human, fromHand, timing,
                    summoned, paidLp))
                return true;

            if (deckCost != null)
            {
                if (human)
                {
                    // The player chooses the Deck card (ContinueResponseDiscardPick). Cancel undoes the activation.
                    var cost = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        FromHand = fromHand,
                        WasSetOnField = !fromHand,
                        TargetKind = EffectTargetKind.SendRaceMonsterFromDeckToGy,
                        UsesTextProgram = true,
                        AwaitingIgnitionCost = true,
                        CostPicksRemaining = 1,
                        ChainActivation = true,
                        SeatTiming = timing,
                        SeatSummoned = summoned,
                        SeatTargetClause = targeted,
                        PaidLpCost = paidLp
                    };
                    cost.LegalTargets.AddRange(deckFodder);
                    engine.SetPendingActivation(cost);
                    engine.Log(cost.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                if (SendRaceFromDeckAsCost(engine, who, deckFodder[0]))
                    engine.Log($"Cost: send {deckFodder[0].Name} from the Deck to the GY.");
            }

            if (discard != null)
            {
                if (human)
                {
                    // The player chooses the discard; the target (if any) comes after it. Cancel undoes the activation.
                    var cost = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        FromHand = fromHand,
                        WasSetOnField = !fromHand,
                        TargetKind = EffectTargetKind.DiscardMonsterInHand,
                        UsesTextProgram = true,
                        AwaitingIgnitionCost = true,
                        CostPicksRemaining = DiscardNeed(discard),
                        DiscardCostAttribute = DiscardCostAttributeOf(discard),
                        ChainActivation = true,
                        SeatTiming = timing,
                        SeatSummoned = summoned,
                        SeatTargetClause = targeted,
                        PaidLpCost = paidLp
                    };
                    cost.LegalTargets.AddRange(CollectDiscardCost(who, cost.DiscardCostAttribute, card));
                    engine.SetPendingActivation(cost);
                    engine.Log(cost.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                var fodder = CollectDiscardCost(who, DiscardCostAttributeOf(discard), card);
                fodder.Remove(pick);
                foreach (var c in PickAiDiscards(fodder, DiscardNeed(discard)))
                {
                    var gone = MonsterEffects.DiscardFromHandByInstance(who, c);
                    if (gone != null) engine.Log($"Cost: discard {gone.Name}.");
                }
            }

            if (targeted != null && pick == null)
            {
                var pending = SeatedTargetPick(who, card, fromHand, targeted, targets, timing, summoned, paidLp);
                BattleDestroyedGyRecoveryRuntime.AttachSeatCost(card, pending); // Rope of Life: refunded on Cancel
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            if (pick != null)
            {
                link.Target = pick;
                link.Targets.Clear();
                link.Targets.Add(pick);
                // EquipModel batch: Tailor of the Fickle's monster target is a target of the link too.
                EquipModelRuntime.AddMonsterTargetToLink(engine, link);
                // TypeDeclaration batch (v142): a declared Type is announced with the activation.
                TypeDeclarationRuntime.NoteActivationChoice(engine, card, pick);
            }

            return OpenSeatedResponseChainWindow(engine, who, timing);
        }

        /// <summary>
        /// Every choice of a response seated on the chain is made: the window it answered is answered, and the
        /// opponent may respond to its link (or the chain resolves).
        /// </summary>
        internal static bool OpenSeatedResponseChainWindow(DuelEngine engine, DuelistState who, ResponseTiming timing)
        {
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

        /// <summary>
        /// The player's target pick for a response seated on the chain: the pick becomes the link's target
        /// (SpellTrapEffects.TryResolveWithTarget); Cancel takes the link back and re-offers the window.
        /// </summary>
        static PendingActivation SeatedTargetPick(DuelistState who, CardInstance card, bool fromHand,
            EffectClause targeted, List<CardInstance> targets, ResponseTiming timing, CardInstance summoned, int paidLp)
        {
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                TargetKind = MapTargetKind(targeted),
                ChainActivation = true,
                SeatTiming = timing,
                SeatSummoned = summoned,
                PaidLpCost = paidLp
            };
            pending.LegalTargets.AddRange(targets);
            return pending;
        }

        /// <summary>
        /// The player chose a card to discard for a response seated on the chain (<see cref="SeatResponseOnChain"/>):
        /// it is discarded now. Once the cost is paid the player picks the target, or the opponent may respond.
        /// </summary>
        static bool ContinueResponseDiscardPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            var who = p.Controller;
            var card = p.Card;
            if (who == null || card == null || !p.LegalTargets.Contains(target)) return false;
            if (p.TargetKind == EffectTargetKind.SendRaceMonsterFromDeckToGy)
            {
                // Rock Bombardment's Deck-send cost (SeatResponseOnChain): one pick, then the window.
                if (!SendRaceFromDeckAsCost(engine, who, target)) return false;
                engine.Log($"Cost: send {target.Name} from the Deck to the GY.");
                p.CostPicksRemaining = 0;
            }
            else if (SpellCounterSpendersRuntime.TryPaySeatPick(engine, p, target, out var counterPaid))
            {
                // SpellCounterSpenders batch: Miracle Restoring's Spell Counters, 1 per tapped card.
                if (!counterPaid) return false;
            }
            else
            {
                var gone = MonsterEffects.DiscardFromHandByInstance(who, target);
                if (gone == null) return false;
                engine.Log($"Cost: discard {gone.Name}.");
                p.PaidDiscards.Add(gone);
                p.LegalTargets.Remove(target);
                p.CostPicksRemaining--;
            }
            if (p.CostPicksRemaining > 0 && p.LegalTargets.Count > 0)
            {
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            var targeted = p.SeatTargetClause;
            if (targeted == null)
            {
                engine.ClearPendingActivation();
                return OpenSeatedResponseChainWindow(engine, who, p.SeatTiming);
            }

            // Checked at activation; a discard cannot take the last target away, but never seat a link without one.
            var targets = CollectTargets(engine, who, targeted, card);
            if (targets.Count == 0)
            {
                engine.Log($"{card.Name}: no legal target — the activation is undone.");
                return SpellTrapEffects.CancelPending(engine);
            }

            var pick = SeatedTargetPick(who, card, p.FromHand, targeted, targets, p.SeatTiming, p.SeatSummoned,
                p.PaidLpCost);
            pick.PaidDiscards.AddRange(p.PaidDiscards);
            engine.SetPendingActivation(pick);
            engine.Log(pick.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// A response-window activation was cancelled after it paid a discard cost (Raigeki Break's target pick):
        /// the discarded cards go back to the hand (<see cref="PendingActivation.PaidDiscards"/>).
        /// </summary>
        public static void RefundResponseDiscards(DuelEngine engine, DuelistState who, List<CardInstance> discarded)
        {
            foreach (var c in discarded)
            {
                // Banisher of the Light banished it instead of sending it to the GY.
                if (c == null || (!who.Graveyard.Remove(c) && !who.Banished.Remove(c))) continue;
                who.Hand.Add(c);
                DiscardEvents.Retract(engine, c); // EngineHygiene: the discard event is taken back
                engine.Log($"{c.Name} returns to the hand — the discard cost is refunded.");
            }
        }

        /// <summary>A "discard N cards" cost (local lineage <see cref="EffectClause.RequiresDiscardCost"/>, or
        /// <see cref="EffectClause.DiscardCostCount"/>).</summary>
        internal static bool HasDiscardCost(EffectClause c) =>
            c != null && (c.RequiresDiscardCost || c.DiscardCostCount > 0);

        /// <summary>The cards a <see cref="HasDiscardCost"/> clause may discard, for <see cref="CollectDiscardCost"/>.</summary>
        internal static string DiscardCostAttributeOf(EffectClause c) =>
            c.RequiresDiscardCost ? c.DiscardCostAttribute : "*";

        /// <summary>
        /// The discard cost of a response answered with <paramref name="clauses"/> (the first clause with one) can be
        /// paid from <paramref name="who"/>'s hand, <paramref name="card"/> itself excluded; true when there is none.
        /// </summary>
        static bool ResponseDiscardPayable(DuelEngine engine, DuelistState who, CardInstance card, List<EffectClause> clauses)
        {
            var discard = clauses?.Find(HasDiscardCost);
            // LocksRedirectsAndActivity: a live cannot-discard-as-a-cost lock refuses the activation outright.
            if (discard != null && LocksRedirectsAndActivityRuntime.DiscardAsCostLocked(engine, who))
                return false;
            return discard == null ||
                   CollectDiscardCost(who, DiscardCostAttributeOf(discard), card).Count >= DiscardNeed(discard);
        }

        /// <summary>
        /// Player picked the target of a free-chain card answering an AttackDeclared / OpponentOpenState
        /// window inline (MST, Ring of Destruction): apply the effect to that target, then the declared
        /// event continues.
        /// </summary>
        static bool FinishInlineResponseTarget(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (!p.LegalTargets.Contains(target)) return false;
            var who = p.Controller;
            var card = p.Card;
            var timing = p.InlineResponseTiming;
            engine.ClearPendingActivation();
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || who == null || card == null) return false;
            var activateClauses = prog.ClausesFor(EffectTiming.Activate);
            var attackNegated = false;
            var battleEnded = false;
            var attackClauses = timing == ResponseTiming.AttackDeclared
                ? prog.ClausesFor(EffectTiming.AttackDeclared)
                : null;
            var used = timing == ResponseTiming.AttackDeclared
                ? AttackWindowFreeChainClauses(card, prog, activateClauses)
                : activateClauses;
            if (timing == ResponseTiming.AttackDeclared)
                ApplyAttackWindowAnswer(engine, who, card, attackClauses, used, target,
                    ref attackNegated, ref battleEnded);
            else
            {
                foreach (var c in used)
                    ApplyClause(engine, who, card, c, c != null && c.RequiresTargetChoice ? target : null,
                        ref attackNegated, ref battleEnded);
            }

            FinishSpellTrap(engine, who, card,
                stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            // Prefer AttackDeclared pick clause (Widespread Ruin) over free-chain for PSCT flag.
            var pickClause = FirstPickTargetClause(attackClauses) ?? FirstPickTargetClause(used);
            NotifyTargetingEffectResolved(engine, target, pickClause == null || TargetingTruthRuntime.ClauseTargets(pickClause));
            engine.ContinueAfterResponseActivation(attackNegated, battleEnded);
            return true;
        }

        /// <summary>AttackDeclared clauses on their implicit battle targets, then free-chain clauses on the pick.</summary>
        static void ApplyAttackWindowAnswer(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> attackClauses, List<EffectClause> freeChain, CardInstance freePick,
            ref bool attackNegated, ref bool battleEnded)
        {
            engine.TryGetBattlingPair(out var attackingMon, out _);
            foreach (var c in attackClauses)
            {
                CardInstance tgt = null;
                if (c.Zone == EffectZoneFilter.AttackingMonster)
                    tgt = attackingMon;
                else if (c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                    tgt = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                else if (c.RequiresTargetChoice)
                    tgt = freePick; // Widespread Ruin: choose among tied highest-ATK Attack Position
                ApplyClause(engine, who, card, c, tgt, ref attackNegated, ref battleEnded);
            }

            foreach (var c in freeChain)
            {
                if (c == null) continue;
                CardInstance tgt = null;
                if (c.Zone == EffectZoneFilter.AttackingMonster)
                    tgt = attackingMon;
                else if (c.Zone == EffectZoneFilter.OpponentBattlingMonster)
                    tgt = YgoProTriggerCatalog.OpponentBattlingMonster(engine, who);
                else if (c.RequiresTargetChoice)
                    tgt = freePick;
                ApplyClause(engine, who, card, c, tgt, ref attackNegated, ref battleEnded);
            }
        }

        /// <summary>
        /// Start a player's target pick for a free-chain card answering a response window inline: the card
        /// is revealed, the window stays open (cancel turns it face-down and re-offers the window).
        /// </summary>
        static bool BeginInlineResponseTargetPick(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, EffectClause targeted, List<CardInstance> targets, ResponseTiming timing)
        {
            PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                TargetKind = MapTargetKind(targeted),
                UsesTextProgram = true,
                TargetPicksRemaining = 1,
                InlineResponseTiming = timing
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Target for a free-chain answer resolved inline: forced target, AI auto-pick, or null when a
        /// player must pick first (<paramref name="beginPlayerPick"/> = true). Returns false when the
        /// clause has no legal target (the activation is refused).
        /// </summary>
        static bool ResolveInlineAnswerTarget(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause targeted, bool autoPickTarget, CardInstance forcedTarget,
            out CardInstance pick, out List<CardInstance> targets, out bool beginPlayerPick)
        {
            pick = forcedTarget;
            targets = null;
            beginPlayerPick = false;
            if (targeted == null || pick != null) return true;
            targets = CollectTargets(engine, who, targeted, card);
            if (targets.Count == 0)
            {
                engine.Log($"{card.Name}: no legal target — cannot activate.");
                return false;
            }

            if (autoPickTarget || !who.IsPlayer)
            {
                pick = AutoPick(targeted, targets, who, engine);
                return pick != null;
            }

            if (IsMultiPickClause(targeted))
            {
                engine.Log($"{card.Name}: multi-target activation is not available in this response window.");
                return false;
            }

            beginPlayerPick = true;
            return true;
        }

        /// <param name="lpCostPaidOnActivation">The Chain Link being resolved already paid the activation's LP cost
        /// (<see cref="ActivationLpCostRuntime"/>, <see cref="ChainLink.ActivationLpCostPaid"/>): do not pay it again.</param>
        public static bool TryResolveActivation(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget,
            CardInstance forcedTarget = null, bool lpCostPaidOnActivation = false)
        {
            if (prog == null) return false;
            // UnionRiders batch (v175): the Union-state ignition of an equipped Union (Kiryu, Second Goblin), or the choice
            // between it and the Union's own unequip.
            if (UnionRidersRuntime.TryActivate(engine, who, card, fromHand, prog, autoPickTarget, out var unionOk))
                return unionOk;
            var activateClauses = prog.ClausesFor(EffectTiming.Activate);

            // Response windows
            if (engine.PendingResponse != null && engine.PendingResponse.Responder == who)
            {
                if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
                {
                    var clauses = prog.ClausesFor(EffectTiming.AttackDeclared);
                    var freeChain = AttackWindowFreeChainClauses(card, prog, activateClauses);
                    if (clauses.Count == 0 && freeChain.Count == 0)
                        return false;

                    // Attack-timing clauses usually keep the attacker as their implicit target.
                    // Widespread Ruin-style AttackDeclared RequiresTargetChoice clauses also need a pick
                    // (highest-ATK Attack Position, controller chooses if tied).
                    var pickClauses = new List<EffectClause>();
                    pickClauses.AddRange(clauses);
                    pickClauses.AddRange(freeChain);
                    if (ClauseMakesChainLink(clauses) || ClauseMakesChainLink(freeChain))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.AttackDeclared, null, pickClauses, autoPickTarget);

                    // Targeted AttackDeclared / free-chain clauses take a real target; they
                    // are never applied with null, which fell through to "destroy all" (DR-01).
                    var targetedPick = FirstPickTargetClause(pickClauses);
                    if (!ResolveInlineAnswerTarget(engine, who, card, targetedPick, autoPickTarget, forcedTarget,
                            out var freePick, out var freeTargets, out var playerPicks))
                        return false;
                    if (playerPicks)
                        return BeginInlineResponseTargetPick(engine, who, card, fromHand, targetedPick,
                            freeTargets, ResponseTiming.AttackDeclared);

                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var attackNegated = false;
                    var battleEnded = false;
                    ApplyAttackWindowAnswer(engine, who, card, clauses, freeChain, freePick,
                        ref attackNegated, ref battleEnded);

                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    if (freePick != null)
                        NotifyTargetingEffectResolved(engine, freePick, TargetingTruthRuntime.ClauseTargets(targetedPick));
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
                            ResponseTiming.OpponentOpenState, null, open, autoPickTarget);

                    var targetedOpen = FirstPickTargetClause(open);
                    if (!ResolveInlineAnswerTarget(engine, who, card, targetedOpen, autoPickTarget, forcedTarget,
                            out var openPick, out var openTargets, out var playerPicksOpen))
                        return false;
                    if (playerPicksOpen)
                        return BeginInlineResponseTargetPick(engine, who, card, fromHand, targetedOpen,
                            openTargets, ResponseTiming.OpponentOpenState);

                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var dummy = false;
                    foreach (var c in open)
                        ApplyClause(engine, who, card, c, c != null && c.RequiresTargetChoice ? openPick : null,
                            ref dummy, ref dummy);
                    FinishSpellTrap(engine, who, card,
                        stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                    if (openPick != null)
                        NotifyTargetingEffectResolved(engine, openPick, TargetingTruthRuntime.ClauseTargets(targetedOpen));
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }

                // SummonNegation batch (v141): pay the cost, then seat the link for the summon-negation window.
                if (engine.PendingResponse.Timing == ResponseTiming.SummonNegation)
                    return SummonNegationRuntime.ActivateSummonNegation(engine, who, card, fromHand, prog,
                        autoPickTarget);

                if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
                {
                    var clauses = prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon);
                    if (clauses.Count == 0) return false;
                    if (ClauseMakesChainLink(clauses))
                        return SeatResponseOnChain(engine, who, card, fromHand,
                            ResponseTiming.MonsterSummoned, engine.PendingResponse.Summoned, clauses,
                            autoPickTarget);
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
                            ResponseTiming.YouTakeDamage, null, clauses, autoPickTarget);
                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    var dummy = false;
                    foreach (var c in clauses)
                        ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    FinishSpellTrap(engine, who, card, stays: false);
                    engine.ContinueAfterResponseActivation(false, false);
                    return true;
                }

                // SpellTrapDestroyedResponse (v142): Gust / Driving Snow become a Chain Link answering the
                // destruction; the target is chosen now (the AI auto-picks), the opponent may chain, and the link
                // resolves with its Activate clauses (TryResolveSeatedResponse).
                if (engine.PendingResponse.Timing == ResponseTiming.SpellTrapDestroyedByOpponent)
                {
                    if (activateClauses.Count == 0 || !SpellTrapDestroyedResponseRuntime.AnswersDestroyedWindow(prog))
                        return false;
                    return SeatResponseOnChain(engine, who, card, fromHand,
                        ResponseTiming.SpellTrapDestroyedByOpponent, null, activateClauses, autoPickTarget);
                }

                // EquipModel batch: Armored Glass becomes a Chain Link answering the equip; the opponent may chain, and
                // the link resolves with its Activate clause (TryResolveSeatedResponse).
                if (engine.PendingResponse.Timing == ResponseTiming.MonsterEquipped)
                {
                    if (activateClauses.Count == 0 || !EquipModelRuntime.AnswersEquipWindow(prog))
                        return false;
                    return SeatResponseOnChain(engine, who, card, fromHand, ResponseTiming.MonsterEquipped, null,
                        activateClauses, autoPickTarget);
                }

                // HandDisruption batch (v142): Appropriate answers "your opponent drew outside either Draw Phase".
                if (engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
                    return HandDisruptionRuntime.ActivateInDrawWindow(engine, who, card, fromHand, prog,
                        autoPickTarget);

                // BattleWindows batch: Destruction Punch / Michizure become a Chain Link answering the end of the Damage
                // Step or the monster sent to the GY; Michizure's target is chosen now (the AI auto-picks), the
                // opponent may chain, and the link resolves with its Activate clauses (TryResolveSeatedResponse).
                if (engine.PendingResponse.Timing == ResponseTiming.EndOfDamageStep ||
                    engine.PendingResponse.Timing == ResponseTiming.MonsterSentFromFieldToYourGy)
                {
                    var answerTiming = engine.PendingResponse.Timing;
                    if (activateClauses.Count == 0 || !BattleWindowsRuntime.AnswersWindow(prog, answerTiming))
                        return false;
                    return SeatResponseOnChain(engine, who, card, fromHand, answerTiming, null, activateClauses,
                        autoPickTarget);
                }

                // EngineHygiene batch: Forced Requisition becomes a Chain Link answering the discard; it only stays.
                if (engine.PendingResponse.Timing == ResponseTiming.YouDiscardedFromHand)
                {
                    if (activateClauses.Count == 0 || !DiscardEvents.AnswersDiscardWindow(prog))
                        return false;
                    return SeatResponseOnChain(engine, who, card, fromHand, ResponseTiming.YouDiscardedFromHand, null,
                        activateClauses, autoPickTarget);
                }

                // ForcedActivationAndSubstitution batch (v163): Major Riot becomes a Chain Link answering the return; the
                // opponent may chain, and the link resolves with its Activate clause (TryResolveSeatedResponse).
                if (engine.PendingResponse.Timing == ResponseTiming.MonstersReturnedToHandByOpponent)
                {
                    if (activateClauses.Count == 0 || !MajorRiotRuntime.AnswersWindow(prog))
                        return false;
                    return SeatResponseOnChain(engine, who, card, fromHand,
                        ResponseTiming.MonstersReturnedToHandByOpponent, null, activateClauses, autoPickTarget);
                }
            }

            var activate = activateClauses
                .Where(c => c != null &&
                            !(c.Action == EffectActionKind.UnequipThisSpecialSummon &&
                              (card.EquippedTo == null || UnionRidersRuntime.UnionTextOff(card))) &&
                            !(c.Action == EffectActionKind.EquipThisToTarget &&
                              card.EquippedTo != null) &&
                            !(c.RequiresTributeEquippedMonster && card.EquippedTo == null) &&
                            !(c.ActivatesFromHand && !fromHand && card.Def != null && card.Def.IsMonster))
                .ToList();
            if (activate.Count == 0)
            {
                if (card.Def != null && card.Def.IsContinuousSpellOrTrap && prog.FullyCompiled &&
                    engine.PendingResponse == null)
                {
                    // Chained (Royal Decree answering a Dust Tornado) and removed before its link resolved:
                    // nothing is left to place.
                    if (engine.IsResolvingChain && !who.Hand.Contains(card) && !who.TryFindSpellTrap(card, out _) &&
                        (who.FieldSpellZone == null || who.FieldSpellZone.Occupant != card))
                    {
                        engine.Log($"{card.Name} is no longer on the field — its activation resolves with no effect.");
                        return true;
                    }

                    PlaceFaceUp(engine, who, card, fromHand);
                    engine.Log($"Activate: {card.Name}.");
                    // ContinuousWhileFaceUp resolve-on-activate (Remove Brainwashing mass return).
                    var contDummy = false;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c != null && c.Action == EffectActionKind.ReturnControlToOwners)
                            ApplyClause(engine, who, card, c, null, ref contDummy, ref contDummy);
                    }
                    FinishSpellTrap(engine, who, card, stays: true);
                    engine.NotifyPublic();
                    return true;
                }

                return false;
            }

            // SimpleSpellTraps batch: "Activate 1 of these effects" with a targeted bullet (Dragon's Gunfire) — the
            // bullet is chosen first and only that one goes on; Emergency Provisions pays its send cost now.
            if (SimpleSpellTrapsRuntime.HasTargetedBullet(activate))
            {
                if (SimpleSpellTrapsRuntime.OfferTargetedBullet(engine, who, card, fromHand, activate, autoPickTarget))
                    return true;
                activate = SimpleSpellTrapsRuntime.KeepChosenBullet(card, activate);
                if (activate.Count == 0) return false;
            }

            if (SimpleSpellTrapsRuntime.TryBeginSendCost(engine, who, card, fromHand, activate, autoPickTarget))
                return true;
            // Burst Breath chained to a link Tributed as it was chained: only its effect is left (the ATK is the DEF cap).
            if (SimpleSpellTrapsRuntime.TakePaidTribute(engine, card, activate, out var chainedTributeAtk))
            {
                ApplyIgnitionClauses(engine, who, card, activate, null, chainedTributeAtk, false);
                FinishSpellTrap(engine, who, card, stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                engine.NotifyPublic();
                return true;
            }

            var isMonster = card.Def != null && card.Def.IsMonster;
            var monsterIgnition = isMonster && !fromHand;

            // Darkness Approaches / Final Destiny (GitHub lineage): discard N cards (cost) first. The
            // card stays where it is until the cost is paid, so Cancel leaves the hand untouched.
            // A local-lineage clause that also carries RequiresDiscardCost keeps its own
            // discard-then-target flow below (StartOrPayDiscardThenTarget / DiscardNeed).
            var multiDiscard = activate.FirstOrDefault(c => c.DiscardCostCount > 0 && !c.RequiresDiscardCost);
            if (multiDiscard != null && !ReferenceEquals(s_multiDiscardPaidFor, card))
                return StartOrPayMultiDiscard(engine, who, card, fromHand, multiDiscard,
                    autoPickTarget, monsterIgnition);
            var multiCostPaid = multiDiscard != null; // ResumeAfterMultiDiscard already discarded
            s_multiDiscardPaidFor = null;

            // Ritual Spell, player side: choose the Ritual Monster and its Tributes (hand or field) before the
            // Spell is placed, whenever there is a real choice (one legal option is used as is). A normal Main
            // Phase activation never gets here: it is chain-seated first and opens the same choice from
            // SpellTrapEffects.BeginDeferredChainActivation (TryStartDeferredRitualChoice).
            var ritualClause = activate.FirstOrDefault(c => c.Action == EffectActionKind.RitualSummon);
            if (ritualClause != null && !autoPickTarget && engine.IsHumanControlled(who) &&
                !ReferenceEquals(s_ritualChosenFor, card) &&
                TryStartRitualChoice(engine, who, card, fromHand, ritualClause))
                return true;
            s_ritualChosenFor = null;
            // The card is placed face-up on each path below only once its activation is legal
            // (local lineage), not up front.

            // Magical Labyrinth: Tribute the equipped monster you control (this card goes with it), then
            // Special Summon the named monster from the Deck. The summon runs only with the cost paid here.
            var tribEquipped = activate.FirstOrDefault(c => c.RequiresTributeEquippedMonster);
            if (tribEquipped != null)
            {
                if (!IgnitionCostLegal(engine, who, card, tribEquipped, out var eqReason))
                {
                    engine.Log($"{card.Name}: {eqReason}");
                    return false;
                }

                if (!TributeSummonsRuntime.PayTributeEquippedCost(engine, who, card))
                    return false;
                try
                {
                    var dEq = false;
                    ApplyClause(engine, who, card, tribEquipped, null, ref dEq, ref dEq);
                }
                finally
                {
                    TributeSummonsRuntime.EndTributeEquipped();
                }

                engine.FlushQueuedSummonResponses();
                engine.NotifyPublic();
                return true;
            }

            // An LP cost paid when the Chain Link was created is its only structured cost (ActivationLpCostRuntime):
            // nothing is left to pay here.
            var ign = lpCostPaidOnActivation
                ? null
                : activate.FirstOrDefault(c =>
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

            // Several targets / target groups (Two-Pronged Attack, Gravedigger Ghoul, …)
            if (UsesMultiTarget(activate))
            {
                // GitHub-lineage multi-target flow expects the card already placed (it was placed
                // up front there); the local lineage defers placement, so place it here.
                if (!isMonster)
                    PlaceFaceUp(engine, who, card, fromHand);
                engine.Log($"Activate: {card.Name}.");
                return StartMultiTarget(engine, who, card, fromHand, prog, activate,
                    autoPickTarget, monsterIgnition);
            }

            // Single targeted clause → pending or auto
            if (PoisonMeteorCestusRuntime.NeedsStatChoice(activate, card))
            {
                if (PoisonMeteorCestusRuntime.OfferStat(engine, who, card, fromHand, autoPickTarget))
                    return true;
            }

            var targeted = activate.FirstOrDefault(c => c.RequiresTargetChoice);
            // EquipFamily batch: Scroll of Bewitchment's Attribute declaration (a human declares before the pick).
            if (targeted != null && forcedTarget == null &&
                EquipFamilyRuntime.NeedsAttributeDeclaration(activate, card) &&
                EquipFamilyRuntime.OfferAttributeDeclaration(engine, who, card, targeted, fromHand, autoPickTarget))
                return true;
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
                    if (targeted.Action == EffectActionKind.DiscardUpToMonstersFromHand)
                    {
                        engine.Log($"Activate: {card.Name}.");
                        engine.Log($"{card.Name}: no Monster Cards in hand.");
                        if (!isMonster)
                            FinishSpellTrap(engine, who, card, stays: false);
                        engine.NotifyPublic();
                        return true;
                    }

                    engine.Log($"{card.Name}: no legal target — cannot activate.");
                    PoisonMeteorCestusRuntime.ClearStatIfNotEquipped(card);
                    EquipFamilyRuntime.ForgetDeclarationIfNotEquipped(card);
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
                    if (targeted.Action == EffectActionKind.DiscardUpToMonstersFromHand &&
                        forcedTarget == null)
                    {
                        var cap = targeted.Amount > 0 ? targeted.Amount : 1;
                        var n = Mathf.Min(cap, targets.Count);
                        var dummyDiscard = false;
                        for (var i = 0; i < n; i++)
                            ApplyClause(engine, who, card, targeted, targets[i],
                                ref dummyDiscard, ref dummyDiscard);
                        if (monsterIgnition)
                            card.EffectUsedThisTurn = targeted.OncePerTurn || card.EffectUsedThisTurn;
                        else
                            FinishSpellTrap(engine, who, card,
                                stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
                        engine.NotifyPublic();
                        return true;
                    }

                    // Automatic picks for a multi-pick clause (AI / auto-resolve): "up to N" targets
                    // (Gravedigger Ghoul, Soul Release) or 2-of-yours + 1-of-theirs (Two-Pronged Attack)
                    // take every pick, not just the first one.
                    if (forcedTarget == null && TryAutoResolveMultiPick(engine, who, card, prog, activate,
                            targeted, targets, secondTargets, monsterIgnition))
                        return true;

                    var pick = forcedTarget ?? AutoPick(targeted, targets, who, engine);
                    var dummy = false;
                    var resolvedTargets = new List<CardInstance> { pick };
                    var wasEquippedActivation = card.EquippedTo != null;
                    foreach (var c in activate)
                    {
                        if (SkipUnionBranch(c, card, wasEquippedActivation)) continue;
                        if (c.RequiresTargetChoice)
                            ApplyClause(engine, who, card, c, pick, ref dummy, ref dummy);
                        else
                            ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
                    }

                    // SimpleSpellTraps batch: a targeted bullet (Dragon's Gunfire) was chosen for this activation only.
                    PoisonMeteorCestusRuntime.ForgetBullet(card);

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
                    NotifyTargetingEffectsResolved(engine, resolvedTargets, TargetingTruthRuntime.ClauseTargets(targeted));
                    engine.NotifyPublic();
                    return true;
                }

                var picks = TargetPicks(targeted, out var picksUpTo);
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    WasSetOnField = !fromHand && !monsterIgnition,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = monsterIgnition,
                    UsesTextProgram = true,
                    CostPaid = multiCostPaid,
                    TargetPicksRemaining = picks,
                    TargetPicksAreUpTo = picksUpTo
                };
                pending.LegalTargets.AddRange(targets);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            if (PoisonMeteorCestusRuntime.HasBulletChoice(activate) &&
                !PoisonMeteorCestusRuntime.HasChosenBullet(card))
            {
                if (PoisonMeteorCestusRuntime.OfferInlineBullet(engine, who, card, activate, fromHand, autoPickTarget))
                    return true;
            }

            if (!isMonster)
                PlaceFaceUp(engine, who, card, fromHand);
            engine.Log($"Activate: {card.Name}.");

            var d = false;
            var chosenBullet = PoisonMeteorCestusRuntime.ChosenBullet(card);
            var wasEquippedNoPending = card.EquippedTo != null;
            foreach (var c in activate)
            {
                if (c != null && c.EffectChoiceIndex > 0 && c.EffectChoiceIndex != chosenBullet)
                    continue;
                if (SkipUnionBranch(c, card, wasEquippedNoPending)) continue;
                ApplyClause(engine, who, card, c, null, ref d, ref d);
            }

            if (chosenBullet > 0)
                PoisonMeteorCestusRuntime.ForgetBullet(card);

            if (monsterIgnition)
            {
                if (activate.Any(c => c.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else if (!isMonster) // a hand monster (The Fiend Megacyber) is not a Spell/Trap to finish
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────── Multi-target activation ─────────────────────

        static int MinTargets(EffectClause c) =>
            c == null ? 0 : c.TargetUpTo ? 1 : Mathf.Max(1, c.TargetCount);

        static int MaxTargets(EffectClause c) => c == null ? 0 : Mathf.Max(1, c.TargetCount);

        /// <summary>
        /// A clause takes more than 1 target / "up to N", or clauses are declared as distinct
        /// target groups. Plain duplicate targeted clauses keep sharing one target.
        /// </summary>
        static bool UsesMultiTarget(List<EffectClause> activate)
        {
            if (activate == null) return false;
            foreach (var c in activate)
            {
                if (c == null || !c.RequiresTargetChoice) continue;
                if (c.TargetCount > 1 || c.TargetUpTo || c.DistinctTargetGroup) return true;
            }

            return false;
        }

        static List<EffectClause> TargetedClauses(List<EffectClause> activate) =>
            activate?.Where(c => c != null && c.RequiresTargetChoice).ToList() ??
            new List<EffectClause>();

        /// <summary>
        /// Choose every target for the clauses automatically (AI, and response windows that
        /// resolve synchronously). Returns null if a clause cannot reach its minimum.
        /// </summary>
        static List<(int step, CardInstance target)> PickAutoTargets(DuelEngine engine,
            DuelistState who, CardInstance card, List<EffectClause> targeted)
        {
            var picks = new List<(int step, CardInstance target)>();
            for (var i = 0; i < targeted.Count; i++)
            {
                var c = targeted[i];
                var cand = CollectTargets(engine, who, c, card)
                    .Where(t => t != null && !picks.Exists(p => p.target == t)).ToList();
                if (cand.Count < MinTargets(c)) return null;
                var take = Mathf.Min(MaxTargets(c), cand.Count);
                for (var k = 0; k < take; k++)
                {
                    var pick = AutoPick(c, cand, who, engine);
                    if (pick == null) break;
                    picks.Add((i, pick));
                    cand.Remove(pick);
                }
            }

            return picks;
        }

        /// <summary>Response-window free-chain Activate clauses that target need their targets.</summary>
        static bool AutoTargetsAvailable(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> activate)
        {
            var targeted = TargetedClauses(activate);
            if (targeted.Count == 0) return true;
            if (!UsesMultiTarget(activate))
                return CollectTargets(engine, who, targeted[0], card).Count > 0;
            return PickAutoTargets(engine, who, card, targeted) != null;
        }

        /// <summary>
        /// Apply Activate clauses inside a synchronous response window. Targeted clauses get
        /// auto-picked targets instead of resolving with no target.
        /// </summary>
        static void ApplyAutoTargeted(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> clauses, ref bool attackNegated, ref bool battleEnded)
        {
            var targeted = TargetedClauses(clauses);
            var picks = new List<(int step, CardInstance target)>();
            if (targeted.Count > 0 && UsesMultiTarget(clauses))
                picks = PickAutoTargets(engine, who, card, targeted) ?? picks;
            else if (targeted.Count > 0)
            {
                // Legacy: every targeted clause resolves against the one chosen target.
                var cand = CollectTargets(engine, who, targeted[0], card);
                var one = cand.Count > 0 ? AutoPick(targeted[0], cand, who, engine) : null;
                if (one != null)
                    for (var i = 0; i < targeted.Count; i++)
                        picks.Add((i, one));
            }

            foreach (var c in clauses)
            {
                if (c == null) continue;
                var idx = targeted.IndexOf(c);
                if (idx < 0)
                {
                    ApplyClause(engine, who, card, c, null, ref attackNegated, ref battleEnded);
                    continue;
                }

                foreach (var p in picks)
                    if (p.step == idx)
                        ApplyClause(engine, who, card, c, p.target, ref attackNegated, ref battleEnded);
            }
        }

        static bool StartMultiTarget(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, List<EffectClause> activate,
            bool autoPickTarget, bool monsterIgnition)
        {
            var targeted = TargetedClauses(activate);
            if (autoPickTarget || !who.IsPlayer)
            {
                var picks = PickAutoTargets(engine, who, card, targeted);
                if (picks == null)
                {
                    engine.Log($"{card.Name}: not enough legal targets — activation fails.");
                    if (!monsterIgnition)
                        FinishSpellTrap(engine, who, card, stays: false);
                    engine.NotifyPublic();
                    return true;
                }

                foreach (var p in picks)
                    engine.Log($"{card.Name}: selected {p.target.Name}.");
                ApplyMultiTargets(engine, who, card, prog, picks, monsterIgnition);
                return true;
            }

            var first = targeted[0];
            var cand = CollectTargets(engine, who, first, card);
            if (cand.Count < MinTargets(first) || PickAutoTargets(engine, who, card, targeted) == null)
            {
                engine.Log($"{card.Name}: not enough legal targets — activation fails.");
                if (!monsterIgnition)
                    FinishSpellTrap(engine, who, card, stays: false);
                engine.NotifyPublic();
                return true;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand && !monsterIgnition,
                TargetKind = MapTargetKind(first),
                IsMonsterEffect = monsterIgnition,
                UsesTextProgram = true,
                MultiTarget = true,
                MultiStep = 0,
                MultiStepRemaining = MaxTargets(first),
                MultiStepUpTo = first.TargetUpTo
            };
            pending.LegalTargets.AddRange(cand);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>Cancel during an "up to N" step after ≥1 pick: finish that step.</summary>
        public static bool FinishMultiTargetStep(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.MultiTarget || !p.MultiStepUpTo) return false;
            if (!p.MultiPicks.Exists(x => x.step == p.MultiStep)) return false;
            return AdvanceMultiTarget(engine, p, finishStep: true);
        }

        static bool AdvanceMultiTarget(DuelEngine engine, PendingActivation p, bool finishStep)
        {
            var who = p.Controller;
            var card = p.Card;
            var prog = CompiledEffectCache.GetOrCompile(card);
            var targeted = TargetedClauses(prog?.ClausesFor(EffectTiming.Activate));
            if (prog == null || targeted.Count == 0)
            {
                engine.ClearPendingActivation();
                return false;
            }

            while (true)
            {
                var c = targeted[Mathf.Clamp(p.MultiStep, 0, targeted.Count - 1)];
                var cand = CollectTargets(engine, who, c, card)
                    .Where(t => t != null && !p.MultiPicks.Exists(x => x.target == t)).ToList();
                if (!finishStep && p.MultiStepRemaining > 0 && cand.Count > 0)
                {
                    p.TargetKind = MapTargetKind(c);
                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(cand);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                finishStep = false;
                p.MultiStep++;
                if (p.MultiStep >= targeted.Count)
                {
                    var picks = p.MultiPicks.ToList();
                    var monsterIgnition = p.IsMonsterEffect;
                    engine.ClearPendingActivation();
                    ApplyMultiTargets(engine, who, card, prog, picks, monsterIgnition);
                    return true;
                }

                var next = targeted[p.MultiStep];
                p.MultiStepRemaining = MaxTargets(next);
                p.MultiStepUpTo = next.TargetUpTo;
            }
        }

        static void ApplyMultiTargets(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, List<(int step, CardInstance target)> picks, bool monsterIgnition)
        {
            var activate = prog.ClausesFor(EffectTiming.Activate);
            var targeted = TargetedClauses(activate);
            var d = false;
            foreach (var c in activate)
            {
                if (c == null) continue;
                var idx = targeted.IndexOf(c);
                if (idx < 0)
                {
                    ApplyClause(engine, who, card, c, null, ref d, ref d);
                    continue;
                }

                foreach (var p in picks)
                    if (p.step == idx)
                        ApplyClause(engine, who, card, c, p.target, ref d, ref d);
            }

            if (monsterIgnition)
            {
                if (activate.Any(c => c != null && c.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            engine.NotifyPublic();
        }

        public static bool TryResolveTextTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.UsesTextProgram || target == null) return false;
            var resolved = engine.ResolveLegalEffectTarget(target);
            if (resolved == null) return false;
            target = resolved;

            // ATK/DEF or an inline "activate 1 of these effects" token (the existing card picker).
            // DestinyBoard batch: the hand-or-Deck copy for Destiny Board's placement.
            if (DestinyBoardRuntime.TryResolvePick(engine, target, out var destinyPickOk))
                return destinyPickOk;
            if (PoisonMeteorCestusRuntime.TryFinishInline(engine, p, target))
                return true;
            // EquipFamily batch: Scroll of Bewitchment's Attribute token continues to the monster pick.
            if (EquipFamilyRuntime.TryFinishDeclaration(engine, p, target))
                return true;

            var who = p.Controller;
            var card = p.Card;
            var monsterIgnition = p.IsMonsterEffect || (card?.Def != null && card.Def.IsMonster);

            // SummonNegation batch (v141): Horn of Heaven's Tribute; Magic Drain's Spell discard.
            if (p.AwaitingSummonNegationTribute)
                return SummonNegationRuntime.FinishTributeCostPick(engine, p, target);
            if (p.AwaitingSpellDiscardWaiver)
                return SummonNegationRuntime.FinishSpellDiscardWaiver(engine, p, target);
            // The discard cost of a response seated on the chain (Spell Purification, Raigeki Break).
            if (p.AwaitingIgnitionCost && p.SeatTiming != ResponseTiming.None)
                return ContinueResponseDiscardPick(engine, p, target);
            // DiscardTriggers batch (v142): Delinquent Duo's discard of the victim's own choice.
            if (p.AwaitingForcedDiscardChoice)
                return DiscardTriggersRuntime.FinishForcedDiscardChoice(engine, p, target);
            // TypeDeclaration batch (v142): Earthshaker's Attributes; The Regulation of Tribe's upkeep Tribute.
            if (p.AwaitingDeclaration || p.AwaitingUpkeepTribute)
                return TypeDeclarationRuntime.FinishPick(engine, p, target);
            // HandDisruption batch (v142): Painful Choice's Deck selection, then the opponent's pick.
            if (p.AwaitingDeckRevealPicks || p.AwaitingOpponentPickAmongRevealed)
                return HandDisruptionRuntime.ContinueRevealPick(engine, p, target);

            if (p.InlineResponseTiming != ResponseTiming.None)
                return FinishInlineResponseTarget(engine, p, target);

            if (p.AwaitingRitualMonster || p.AwaitingRitualTributes)
                return ContinueRitualChoice(engine, p, target);

            // NegationsAndGySummons batch: Chosen One's monster and Spell/Trap selections.
            if (NegationsAndGySummonsRuntime.TryContinueHandPick(engine, p, target, out var chosenOneOk))
                return chosenOneOk;
            // DrawThenDiscard batch: Graceful Charity's discard at resolution (not a cost).
            if (p.AwaitingDrawThenDiscard)
                return DrawThenDiscardRuntime.ContinueDiscardPick(engine, p, target);

            if (p.AwaitingMultiDiscard)
            {
                p.MultiDiscardPicks.Add(target);
                p.LegalTargets.Remove(target);
                p.MultiDiscardRemaining--;
                if (p.MultiDiscardRemaining > 0)
                {
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                engine.ClearPendingActivation();
                // DcrSpellTrapUtility batch: Spell Reproduction's cost is a send, not a discard.
                var sendSpells = DcrSpellTrapUtilityRuntime.SpellSendCostClause(card) != null;
                foreach (var pick in p.MultiDiscardPicks)
                {
                    if (sendSpells)
                    {
                        var sent = MonsterEffects.SendFromHandToGraveByInstance(who, pick);
                        if (sent != null) engine.Log($"Cost: send {sent.Name} from the hand to the GY.");
                        continue;
                    }

                    var gone = MonsterEffects.DiscardFromHandByInstance(who, pick);
                    if (gone != null) engine.Log($"Cost: discard {gone.Name}.");
                }

                return ResumeAfterMultiDiscard(engine, who, card, p.FromHand, autoPick: false);
            }

            if (p.MultiTarget && !p.AwaitingIgnitionCost && !p.AwaitingDiscardCost &&
                !p.AwaitingSendNamedCost)
            {
                p.MultiPicks.Add((p.MultiStep, target));
                p.MultiStepRemaining--;
                engine.Log($"{card?.Name}: selected {target.Name}.");
                return AdvanceMultiTarget(engine, p, finishStep: false);
            }


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

            if (p.AwaitingOpponentTribute)
            {
                var tribute = engine.ResolveLegalEffectTarget(target) ?? target;
                if (!p.LegalTargets.Contains(tribute) && engine.ResolveLegalEffectTarget(target) == null)
                    return false;
                var owner = engine.ControllerOf(tribute) ?? engine.OpponentOf(who);
                engine.ClearPendingActivation();
                // EngineHygiene: EVENT_RELEASE — the reason player is the activating card's controller.
                ReleaseEvents.Record(engine, tribute, owner != null ? engine.OpponentOf(owner) : who);
                if (owner != null)
                    engine.SendCardToGrave(owner, tribute, sentBy: card);
                engine.Log($"{card?.Name}: {owner?.Name} Tributes {tribute.Name}.");
                engine.NotifyPublic();
                return true;
            }

            if (!p.AwaitingIgnitionCost && !p.AwaitingDiscardCost && !p.AwaitingSendNamedCost &&
                !p.AwaitingEffectTribute && !p.AwaitingOpponentTribute &&
                TryContinueDualOrUpTo(engine, p, who, card, target, monsterIgnition))
                return true;

            if (p.AwaitingEffectTribute)
            {
                if (!p.LegalTargets.Contains(target) &&
                    engine.ResolveLegalEffectTarget(target) == null)
                    return false;
                var pick = engine.ResolveLegalEffectTarget(target) ?? target;
                var owner = engine.ControllerOf(pick) ?? who;
                // EngineHygiene: EVENT_RELEASE — the reason player is the effect's controller.
                ReleaseEvents.Record(engine, pick, engine.ControllerOf(card) ?? p.Controller ?? owner);
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
                var discardedFromHand = who != null && who.Hand.Contains(target);
                if (!PayOneIgnitionCost(engine, who, card, p, target))
                    return false;
                // EngineResiduals (v163): a monster ignition's discard is refunded if its target pick is cancelled.
                if (p.IsMonsterEffect && discardedFromHand && !who.Hand.Contains(target))
                    p.PaidDiscards.Add(target);
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
                        return EngineResidualsRuntime.CarryPaidDiscards(engine, p,
                            FinishIgnitionAfterCost(engine, who, card, p.FromHand, p.IsMonsterEffect, paidCost));
                    }

                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(more);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                engine.ClearPendingActivation();
                // Equip spells whose cost sends themselves (Horn of the Unicorn) return
                // to the top of the Deck. A monster Tributed as its own cost stays in the GY.
                if (card?.Def != null && !card.Def.IsMonster &&
                    who.Graveyard.Contains(card) && !who.TryFindSpellTrap(card, out _))
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

                return EngineResidualsRuntime.CarryPaidDiscards(engine, p,
                    FinishIgnitionAfterCost(engine, who, card, p.FromHand, p.IsMonsterEffect, paidCost));
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
                // DcrEquipSpells batch: "Send 1 card from your hand to the GY" is a send, not a discard (no discard event).
                var sendCost = CompiledEffectCache.GetOrCompile(card)?.ClausesFor(EffectTiming.Activate)
                    .Exists(c => c != null && c.RequiresTargetChoice && c.RequiresDiscardCost && c.DiscardCostIsSend) == true;
                var discarded = sendCost
                    ? MonsterEffects.SendFromHandToGraveByInstance(who, target)
                    : MonsterEffects.DiscardFromHandByInstance(who, target);
                if (discarded == null)
                {
                    engine.Log("Discard cost failed.");
                    return false;
                }

                engine.Log(sendCost
                    ? $"Cost: send {discarded.Name} from the hand to the GY."
                    : $"Cost: discard {discarded.Name}.");
                if (sendCost) p.PromptOverride = null;
                // EngineResiduals (v163): Cancel at the target that follows undoes the activation and refunds this discard
                // (SpellTrapEffects.CancelPending; a monster ignition through EngineResidualsRuntime.UndoMonsterIgnition).
                p.PaidDiscards.Add(discarded);
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
            var wasEquipped = card.EquippedTo != null;
            engine.ClearPendingActivation();
            if (prog == null) return false;

            var dummy = false;
            var clauseSet = fromSummon
                ? prog.ClausesFor(EffectTiming.OpponentNormalOrFlipSummon)
                : prog.ClausesFor(EffectTiming.Activate);
            foreach (var c in clauseSet)
            {
                if (PoisonMeteorCestusRuntime.SkipsBullet(card, c)) continue;
                if (SkipUnionBranch(c, card, wasEquipped)) continue;
                if (c.RequiresTargetChoice)
                    ApplyClause(engine, who, card, c, target, ref dummy, ref dummy, costNum);
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy, costNum);
            }
            PoisonMeteorCestusRuntime.ForgetBullet(card);

            if (monsterIgnition)
            {
                if (prog.ClausesFor(EffectTiming.Activate).Any(x => x.OncePerTurn))
                    card.EffectUsedThisTurn = true;
            }
            else if (!fromSummon)
                FinishSpellTrap(engine, who, card,
                    stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            NotifyTargetingEffectResolved(engine, target,
                clauseSet.Exists(TargetingTruthRuntime.ClauseTargets));
            engine.NotifyPublic();
            return true;
        }

        public static bool TryResolveFlip(DuelEngine engine, DuelistState who, CardInstance monster,
            CompiledCardProgram prog)
        {
            var flip = prog?.ClausesFor(EffectTiming.Flip);
            if (flip == null || flip.Count == 0) return false;
            // Invader of the Throne: "This effect cannot be activated during the Battle Phase."
            if (TakeControlRuntime.FlipBlockedInBattlePhase(engine, monster, flip)) return true;
            // FlipCostsAndFusion batch: a FLIP cost (Fire Sorcerer, Lady Assailant of Flames) is paid as the effect
            // activates; if it cannot be paid the effect is not activated.
            if (!FlipCostsAndFusionRuntime.TryPayFlipCost(engine, who, monster, flip)) return true;

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
                // TargetedClassics Soft: Tornado Bird — fewer than ExactTargetCount → Flip with no targets.
                var exactFlip = TargetedClassicsRuntime.ExactTargetCount(targeted);
                if (exactFlip > 0 && targets.Count < exactFlip)
                {
                    engine.Log($"{monster.Name} Flip: fewer than {exactFlip} legal targets — no effect.");
                    return true;
                }
                if (targets.Count == 0)
                {
                    engine.Log($"{monster.Name} Flip: no legal targets.");
                    return true;
                }

                if (!who.IsPlayer)
                {
                    var remaining = new List<CardInstance>(targets);
                    var maxPicks = TargetPicks(targeted, out _);
                    var d = false;
                    var picked = new List<CardInstance>();
                    for (var i = 0; i < maxPicks && remaining.Count > 0; i++)
                    {
                        var pick = AutoPick(targeted, remaining, who, engine);
                        if (pick == null) break;
                        remaining.Remove(pick);
                        picked.Add(pick);
                        engine.Log($"{monster.Name} targets {pick.Name}.");
                    }
                    // TargetedClassics Soft: Tornado Bird — apply only while every pick is still on the field.
                    if (exactFlip > 0 &&
                        (picked.Count != exactFlip ||
                         !FlipCostsAndFusionRuntime.AllExactTargetsStillThere(engine, targeted, picked)))
                    {
                        engine.Log($"{monster.Name}: not all targets are still on the field — no effect.");
                    }
                    else
                    {
                        foreach (var pick in picked)
                            foreach (var c in flip)
                                ApplyClause(engine, who, monster, c,
                                    c.RequiresTargetChoice ? pick : null, ref d, ref d);
                    }
                    NotifyTargetingEffectsResolved(engine, picked, TargetingTruthRuntime.ClauseTargets(targeted));
                    engine.NotifyPublic();
                    return true;
                }

                var flipPicks = TargetPicks(targeted, out var flipUpTo);
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = monster,
                    FromHand = false,
                    WasSetOnField = false,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = flipPicks,
                    TargetPicksAreUpTo = flipUpTo
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

        /// <summary>
        /// Tribute Summon value of <paramref name="tribute"/> when Tributing it for
        /// <paramref name="summoning"/>: 2 for the Double Coston / The Trojan Horse family when
        /// the summoned monster's Attribute matches the printed one, otherwise 1. A face-down
        /// copy counts too — the ruling lets it be used as 2 Tributes while Set.
        /// </summary>
        public static int TributeValueFor(CardInstance tribute, CardInstance summoning)
        {
            if (tribute?.Def == null) return 0;
            if (summoning?.Def == null || string.IsNullOrEmpty(summoning.Def.attribute)) return 1;
            var prog = CompiledEffectCache.GetOrCompile(tribute.Def);
            if (prog == null || !prog.FullyCompiled) return 1;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.CountsAsTwoTributesForAttribute) continue;
                if (string.IsNullOrEmpty(c.AttributeFilter)) continue;
                if (summoning.Def.attribute.Equals(c.AttributeFilter, StringComparison.OrdinalIgnoreCase))
                    return 2;
            }

            return 1;
        }

        /// <summary>
        /// Total Tribute value on <paramref name="who"/>'s field for a Tribute Summon of
        /// <paramref name="summoning"/> (monsters that cannot be Tributed do not count).
        /// </summary>
        public static int TributesAvailableFor(DuelistState who, CardInstance summoning)
        {
            if (who == null) return 0;
            var total = 0;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || !TcgRules.CanBeTributedForSummon(who, m)) continue;
                total += TributeValueFor(m, summoning);
            }

            // Soul Exchange: the opponent's targets count as if this player controlled them.
            foreach (var forced in TributeSummonsRuntime.ForcedTributesForSummon(who))
                total += TributeValueFor(forced, summoning);
            return total;
        }

        public static bool TryResolveThisCardSummoned(DuelEngine engine, DuelistState who,
            CardInstance card, bool flipSummon = false, bool specialSummon = false,
            bool fromGraveyard = false, Predicate<EffectClause> only = null)
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
                // SpiritMonsters batch: flipped face-up by battle or a card effect — only the "or flipped face-up"
                // clauses (SpiritMonstersRuntime.NotifyFlippedFaceUp).
                if (only != null && !only(c)) continue;
                if (c.RequiresThisFlipSummoned && !flipSummon) continue;
                if (c.RequiresThisNormalSummoned && (flipSummon || specialSummon)) continue;
                // Normal OR Flip only — a Special Summon must not fire it (Senju family).
                if (c.RequiresThisNormalOrFlipSummoned && specialSummon) continue;
                // GuardiansAndEquipEvents batch: Guardian Elma — Normal or Special Summon only, never a Flip Summon.
                if (c.RequiresThisNormalOrSpecialSummoned && flipSummon) continue;
                if (c.RequiresThisTributeSummoned && (card == null || !card.WasTributeSummoned)) continue;
                // SummonTriggersAndTributes batch (v163): Moisture Creature — only a Tribute Summon by its N-Tribute procedure.
                if (c.RequiresThisTributeSummonedByTributingExactly > 0 &&
                    !SummonTriggersAndTributesRuntime.SummonedByTributeProcedure(card,
                        c.RequiresThisTributeSummonedByTributingExactly)) continue;
                // Sacred Crane / Molten Zombie: a Normal or Flip Summon must not fire these.
                if (c.RequiresThisSpecialSummoned && !specialSummon) continue;
                if (c.RequiresThisSpecialSummonedFromGy && (!specialSummon || !fromGraveyard)) continue;
                // NegationsAndGySummons batch (Gilasaurus): "If Summoned this way" — by its own procedure only.
                if (c.RequiresThisSummonedByOwnProcedure &&
                    (!specialSummon || !card.SpecialSummonedByOwnProcedure)) continue;
                if (c.RequiresNormalOrFlipSummon && specialSummon) continue;
                applicable.Add(c);
            }
            if (applicable.Count == 0) return false;

            // ChoiceBullets batch (v173) — Cyber Raider: "When this card is Summoned: You can activate 1 of these
            // effects" — all bullets are one choice, offered once through EffectChoiceRuntime.
            if (applicable.Exists(c => EffectChoiceRuntime.IsChoiceBullet(c)))
            {
                EffectChoiceRuntime.Offer(engine, who, card, applicable);
                return true;
            }

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
                        applicable.Exists(TargetingTruthRuntime.ClauseTargets));
                    engine.NotifyPublic();
                    return true;
                }

                // Mobius: "up to 2" — every pick past the first is optional.
                var summonPicks = TargetPicks(targeted, out var summonUpTo);
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = false,
                    WasSetOnField = false,
                    TargetKind = MapTargetKind(targeted),
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = summonPicks,
                    TargetPicksAreUpTo = summonUpTo,
                    // FlipDownAndPeek batch (v162): A Man with Wdjat's pick is a look, not the AnyCardOnField bounce prompt.
                    PromptOverride = targeted.Action == EffectActionKind.LookAtTargetSetCard
                        ? "select 1 Set card your opponent controls to look at."
                        : null
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
            CardInstance battleDestroyer = null, Predicate<EffectClause> only = null)
        {
            if (prog == null) return false;
            var clauses = prog.ClausesFor(EffectTiming.SentFromFieldToGy);
            if (clauses == null || clauses.Count == 0) return false;
            // EquipAndSpellProtection batch: "destroyed by a card effect while equipped" (Smoke Grenade of the Thief,
            // Blast with Chain) fires only after such a destruction, and its pick goes through its own pending.
            if (only == null && EquipAndSpellProtectionRuntime.TryResolveDestroyedWhileEquipped(engine, who, card, clauses))
                return true;

            // Cold Wave (LocksAndZones batch): a Spell/Trap Card's "sent from the field to the GY" effect (Axe of
            // Despair, Black Pendant …) is an activated trigger (ygopro TRIGGER_O / TRIGGER_F); while the lock
            // lasts it cannot be activated.
            if (card?.Def != null && !card.Def.IsMonster &&
                clauses.Exists(c => c != null && (!c.RequiresDestroyed || destroyed) &&
                                    (!c.RequiresThisDestroyedByBattle || destroyedByBattle) &&
                                    (!c.RequiresDestroyedWhileFaceDown ||
                                     CallsDiceAndSetTriggersRuntime.WasDestroyedFaceDownThisTrip(engine, card))) &&
                LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card))
                return true;

            // Fail closed: if any clause needs a target/search and nothing applied, return false
            // so registered scripts (Sangan, etc.) can still run.
            var handBefore = who?.HandCount ?? 0;
            var d = false;
            var appliedSomething = false;
            foreach (var c in clauses)
            {
                if (c == null) continue;
                // RedirectAndReplace (v142): a card destroyed but banished instead resolves only its triggers
                // that do not need the GY.
                if (only != null && !only(c)) continue;
                if (c.RequiresDestroyed && !destroyed) continue;
                if (c.RequiresThisDestroyedByBattle && !destroyedByBattle) continue;
                if (c.RequiresNotDestroyedByBattle && destroyedByBattle) continue;
                // CallsDiceAndSetTriggers batch (v163): Statue of the Wicked / Dark Coffin — destroyed while face-down on
                // the field, on this trip to the GY.
                if (c.RequiresDestroyedWhileFaceDown &&
                    !CallsDiceAndSetTriggersRuntime.WasDestroyedFaceDownThisTrip(engine, card))
                    continue;
                // Sent as a cost (SendToGraveAsCost): an optional "When … : You can" effect misses the timing.
                if (c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation &&
                    card != null && ReferenceEquals(card, s_sentAsCost))
                {
                    engine.Log($"{card.Name}: sent as a cost — its \"When … you can\" effect misses the timing.");
                    continue;
                }
                // SimultaneousTriggers batch: sent by a Chain Link that was not the last to resolve — an optional
                // "When … : You can" effect misses the timing (SimultaneousTriggersRuntime.RunHeld).
                if (c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation &&
                    SimultaneousTriggersRuntime.MissesWhenTiming(engine, card))
                {
                    engine.Log($"{card.Name}: more of the Chain resolved after it was sent — its \"When … you can\" " +
                               "effect misses the timing.");
                    continue;
                }
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

                        engine.PayLifePoints(who, c.PayLpAmount, $"Cost: pay {c.PayLpAmount} LP.");
                    }

                    if (c.RequiresTributeCount > 0)
                    {
                        var trib = CollectTributeCost(who, card, c, engine);
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
                            // Soul Exchange: the forced Tribute is the only offer.
                            pending.LegalTargets.AddRange(TributeSummonsRuntime.RestrictToForced(who, trib));
                            engine.SetPendingActivation(pending);
                            engine.Log(pending.Prompt);
                            engine.NotifyPublic();
                            return true;
                        }

                        var pick = trib[0];
                        ReleaseEvents.Record(engine, pick, who); // EngineHygiene: EVENT_RELEASE
                        engine.SendCardToGrave(engine.ControllerOf(pick) ?? who, pick);
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
                    if (TargetingTruthRuntime.ClauseTargets(c)) NotifyTargetingEffectResolved(engine, pick, true);
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
                    or EffectZoneFilter.EitherGyCards
                    or EffectZoneFilter.ControllerGyCards
                    or EffectZoneFilter.OppGyCards
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
            // FlipMassEffects (v142): a Cyber Jar / Morphing Jar #2 / Spear Cretin Special Summon pick.
            if (FlipMassEffectsRuntime.TryResolvePick(engine, target, out var massPickOk))
                return massPickOk;
            // PositionLocks Soft: Aqua Spirit / Garuda human target pick.
            if (PositionLocksRuntime.TryResolvePick(engine, target, out var positionPickOk))
                return positionPickOk;
            // EquipFamily batch: Dark Necrofear's End Phase target pick.
            if (EquipFamilyRuntime.TryResolvePick(engine, target, out var necroPickOk))
                return necroPickOk;
            // SpiritMonsters batch: Maharaghi's top-or-bottom pick before the draw.
            if (SpiritMonstersRuntime.TryResolvePick(engine, target, out var lookPickOk))
                return lookPickOk;
            // ChoiceBullets batch (v173): Chick's immediate top-or-bottom look at the opponent's Deck.
            if (ChoiceBulletsRuntime.TryResolvePick(engine, target, out var chickPickOk))
                return chickPickOk;
            // NegationMonsters batch (v161): Freed the Matchless General's search instead of the normal draw.
            if (NegationMonstersRuntime.TryResolvePick(engine, target, out var drawSearchOk))
                return drawSearchOk;

            // "You can activate 1 of these effects" (Dark Scorpions): the pick also names the effect.
            // FinishPick handles every bullet, including Cyber Raider's second targeted bullet after CANCEL
            // (TryCancelPick swaps choice.Targeted to the alt bullet).
            if (p.EffectChoice != null)
                return EffectChoiceRuntime.FinishPick(engine, p, target);
            if (p.AwaitingPositionChoice || p.AwaitingDeckOrder || p.AwaitingHandToDeckBottom)
                return ResolveOrderedPick(engine, target);

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
            var posChanged = prog.ClausesFor(EffectTiming.ThisCardPositionChanged);
            List<EffectClause> clauses;
            if (posChanged.Exists(c => c != null && c.RequiresTargetChoice) &&
                !flip.Exists(c => c != null && c.RequiresTargetChoice) &&
                !summoned.Exists(c => c != null && c.RequiresTargetChoice) &&
                !gy.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = posChanged;
            else if (flip.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = flip;
            else if (summoned.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = summoned;
            else if (gy.Exists(c => c != null && c.RequiresTargetChoice))
                clauses = gy;
            // RuleLocks batch: Kycoo's "inflicts battle damage" trigger targets (RuleLocksRuntime.TryOfferBattleDamageTargets).
            else if (prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage).Exists(c => c != null && c.RequiresTargetChoice))
                clauses = prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage)
                    .Where(c => c != null && c.RequiresTargetChoice).ToList();
            else
                clauses = flip.Count > 0 ? flip : (summoned.Count > 0 ? summoned : gy);
            // TargetedClassics Soft: Tornado Bird — only if every pick is still on the field (#dg == N).
            var exactClause = clauses.FirstOrDefault(c => c != null && TargetedClassicsRuntime.ExactTargetCount(c) > 0);
            if (exactClause != null &&
                picks.Count == TargetedClassicsRuntime.ExactTargetCount(exactClause) &&
                !FlipCostsAndFusionRuntime.AllExactTargetsStillThere(engine, exactClause, picks))
            {
                engine.Log($"{card.Name}: not all targets are still on the field — no effect.");
                engine.NotifyPublic();
                return true;
            }
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
                clauses.Exists(TargetingTruthRuntime.ClauseTargets));
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// "Up to N" targeting: cancel after at least one pick resolves the chosen set.
        /// </summary>
        /// <summary>How many targets an automatic pick takes for this clause (1 = ordinary).</summary>
        /// <summary>Every card a player controls: monsters, Spell/Trap + Field Zone, Pendulum Zones.</summary>
        static int CardsControlled(DuelistState who)
        {
            if (who == null) return 0;
            var n = who.MonsterCount + who.SpellTrapsOnField().Count();
            if (who.PendulumZones != null)
                foreach (var z in who.PendulumZones)
                    if (z?.Occupant != null) n++;
            return n;
        }

        static int AutoPickCount(EffectClause c) => TargetPicks(c, out _);

        /// <summary>
        /// How many targets one targeted clause takes outside the multi-target flow (<see cref="UsesMultiTarget"/>),
        /// for the player's prompt and for automatic picks. <paramref name="upTo"/>: every pick past the first is
        /// optional (Cancel resolves the picks made so far).
        /// </summary>
        internal static int TargetPicks(EffectClause c, out bool upTo)
        {
            upTo = false;
            if (c == null) return 1;
            // TargetedClassics Soft: Tornado Bird — exact N Spell/Trap targets (not "up to").
            var exact = TargetedClassicsRuntime.ExactTargetCount(c);
            if (exact > 0) return exact;
            if (c.ControllerTargetCount > 0) return c.ControllerTargetCount; // Two-Pronged Attack, Blast Juggler
            if (!AmountIsUpToTargetCount(c)) return 1;
            upTo = true;
            return c.Amount;
        }

        /// <summary>
        /// True when <see cref="EffectClause.Amount"/> is the N of a classic "up to N" template: Soul Release and
        /// Gravedigger Ghoul (Banish), Mobius the Frost Monarch (Destroy), Penguin Soldier and Hade-Hane (return to
        /// hand), The Cheerful Coffin (discard). Any other Amount is a stat, never a number of targets: an ATK/DEF
        /// change (Rush Recklessly +700, Energy Drain 200 per card), an ATK threshold (Trap Hole's Destroy 1000), a
        /// Level cap (Spirit Caller, Marauding Captain, Reinforcement of the Army) or a heads count (Barrel Dragon).
        /// ATK/DEF values are 100 or more, so a 3-digit Amount is never a count.
        /// </summary>
        static bool AmountIsUpToTargetCount(EffectClause c) =>
            c != null && c.Amount > 1 && c.Amount < 100 && !c.AmountIsAtkMax &&
            c.ControllerTargetCount <= 0 && c.TargetCount <= 1 && !c.TargetUpTo && !c.RequiresSecondTarget &&
            (c.Action == EffectActionKind.Banish || c.Action == EffectActionKind.Destroy ||
             c.Action == EffectActionKind.ReturnToHand ||
             c.Action == EffectActionKind.DiscardUpToMonstersFromHand);

        /// <summary>
        /// Distinct automatic picks. Harmful actions (banish / destroy / bounce) take the opponent's cards
        /// first and, when they must hit your own, your weakest monsters first.
        /// </summary>
        static List<CardInstance> AutoPickMany(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> targets, int n)
        {
            var picks = new List<CardInstance>();
            if (targets == null || n <= 0) return picks;
            var harmful = c != null && (c.Action == EffectActionKind.Banish || c.Action == EffectActionKind.Destroy ||
                                        c.Action == EffectActionKind.ReturnToHand);
            bool Mine(CardInstance t) =>
                who != null && (who.Graveyard.Contains(t) || who.Hand.Contains(t) ||
                                who.TryFindMonster(t, out _) || who.TryFindSpellTrap(t, out _));
            while (picks.Count < n)
            {
                var pool = targets.Where(t => t != null && !picks.Contains(t)).ToList();
                if (pool.Count == 0) break;
                CardInstance pick;
                if (harmful)
                {
                    var theirs = pool.Where(t => !Mine(t)).ToList();
                    pick = theirs.Count > 0
                        ? AutoPick(c, theirs, who, engine)
                        : pool.OrderBy(t => t.Def != null && t.Def.IsMonster ? t.CurrentAtk : int.MaxValue).First();
                }
                else
                    pick = AutoPick(c, pool, who, engine);
                if (pick == null) break;
                picks.Add(pick);
            }

            return picks;
        }

        static bool TryAutoResolveMultiPick(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, List<EffectClause> activate, EffectClause targeted,
            List<CardInstance> targets, List<CardInstance> secondTargets, bool monsterIgnition)
        {
            var n = AutoPickCount(targeted);
            if (n <= 1) return false;
            var first = AutoPickMany(engine, who, targeted, targets, n);
            if (first.Count == 0) return false;
            var dummy = false;
            foreach (var c in activate)
            {
                if (c == null) continue;
                if (c.RequiresTargetChoice)
                    foreach (var t in first)
                        ApplyClause(engine, who, card, c, t, ref dummy, ref dummy);
                else
                    ApplyClause(engine, who, card, c, null, ref dummy, ref dummy);
            }

            var resolved = new List<CardInstance>(first);
            if (targeted.RequiresSecondTarget && secondTargets != null)
            {
                var secondPool = secondTargets.Where(x => !first.Contains(x)).ToList();
                var secondClause = SecondTargetClause(targeted);
                var m = targeted.OpponentTargetCount > 0 ? targeted.OpponentTargetCount : 1;
                foreach (var t in AutoPickMany(engine, who, secondClause, secondPool, m))
                {
                    ApplySecondTarget(engine, who, card, targeted, t);
                    resolved.Add(t);
                }
            }

            if (monsterIgnition)
                card.EffectUsedThisTurn = targeted.OncePerTurn || card.EffectUsedThisTurn;
            else
                FinishSpellTrap(engine, who, card, stays: SpellTrapEffects.StaysOnFieldAfterActivate(card, prog));
            NotifyTargetingEffectsResolved(engine, resolved, TargetingTruthRuntime.ClauseTargets(targeted));
            engine.NotifyPublic();
            return true;
        }

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
                // DcrEquipSpells batch: "Send 1 card from your hand to the GY" is a send, not a discard (no discard event).
                var discarded = targeted.DiscardCostIsSend
                    ? MonsterEffects.SendFromHandToGraveByInstance(who, cost)
                    : MonsterEffects.DiscardFromHandByInstance(who, cost);
                if (discarded == null) return false;
                engine.Log(targeted.DiscardCostIsSend
                    ? $"Cost: send {discarded.Name} from the hand to the GY."
                    : $"Cost: discard {discarded.Name}.");
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
                NotifyTargetingEffectResolved(engine, pick, TargetingTruthRuntime.ClauseTargets(targeted));
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
                DiscardCostAttribute = targeted.DiscardCostAttribute,
                // DcrEquipSpells batch: a send cost is not asked as a discard.
                PromptOverride = targeted.DiscardCostIsSend ? "send 1 card from your hand to the GY (cost)." : null
            };
            pending.LegalTargets.AddRange(costCards);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        internal static int DiscardNeed(EffectClause c) =>
            c == null ? 1 : (c.DiscardCostCount > 0 ? c.DiscardCostCount : 1);

        /// <summary>Set while a paid N-card discard cost re-enters <see cref="TryResolveActivation"/>.</summary>
        static CardInstance s_multiDiscardPaidFor;

        static bool StartOrPayMultiDiscard(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, EffectClause clause, bool autoPick, bool monsterIgnition)
        {
            var n = clause.DiscardCostCount;
            // DcrSpellTrapUtility batch: Spell Reproduction sends N Spells (a send, not a discard: no discard event).
            var sendSpells = DcrSpellTrapUtilityRuntime.IsSpellSendCost(clause);
            var hand = sendSpells
                ? DcrSpellTrapUtilityRuntime.SpellsForHandCost(who, card)
                : who.Hand.Where(h => h != null && h != card).ToList();
            if (hand.Count < n)
            {
                engine.Log($"{card.Name}: needs {n} other cards in hand to discard — activation fails.");
                engine.NotifyPublic();
                return false;
            }

            if (autoPick || !who.IsPlayer)
            {
                foreach (var pick in PickAiDiscards(hand, n))
                {
                    if (sendSpells)
                    {
                        var sent = MonsterEffects.SendFromHandToGraveByInstance(who, pick);
                        if (sent != null) engine.Log($"Cost: send {sent.Name} from the hand to the GY.");
                        continue;
                    }

                    var gone = MonsterEffects.DiscardFromHandByInstance(who, pick);
                    if (gone != null) engine.Log($"Cost: discard {gone.Name}.");
                }

                return ResumeAfterMultiDiscard(engine, who, card, fromHand, autoPick);
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand && !monsterIgnition,
                TargetKind = EffectTargetKind.DiscardFromHand,
                IsMonsterEffect = monsterIgnition,
                UsesTextProgram = true,
                AwaitingMultiDiscard = true,
                MultiDiscardRemaining = n,
                PromptOverride = sendSpells
                    ? $"send {n} Spell Cards from your hand to the GY (cost), one at a time, or CANCEL."
                    : null
            };
            pending.LegalTargets.AddRange(hand);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        // ── Ritual Spell choice (player): Ritual Monster, then Tributes one tap at a time ──

        /// <summary>Set while a Ritual Spell whose choice is made re-enters <see cref="TryResolveActivation"/>.</summary>
        static CardInstance s_ritualChosenFor;

        /// <summary>
        /// Opens the Ritual choice for a human-controlled activation when there is one to make: several different
        /// matching Ritual Monsters in hand, or more than one legal Tribute set. Returns false (nothing opened) when
        /// the only option is used as is; resolution then auto-picks it (RitualProcedures.TryResolve).
        /// </summary>
        static bool TryStartRitualChoice(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            EffectClause clause, bool deferredChain = false)
        {
            if (card == null || who == null) return false;
            card.ChosenRitualMonster = null;
            card.ChosenRitualTributes = null;
            if (!WRLDZ.Duel.Rules.RitualProcedures.TryGetSpec(clause, out var spec)) return false;
            var monsters = WRLDZ.Duel.Rules.RitualProcedures.PerformableRitualMonsters(engine, who, spec);
            if (monsters.Count == 0) return false; // the normal path reports why it cannot resolve

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                UsesTextProgram = true,
                RitualExactLevel = spec.ExactLevel,
                RitualDeferredChain = deferredChain
            };
            if (monsters.Count > 1)
            {
                pending.AwaitingRitualMonster = true;
                pending.TargetKind = EffectTargetKind.RitualMonsterInHand;
                pending.LegalTargets.AddRange(monsters);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return true;
            }

            return BeginRitualTributeStep(engine, pending, monsters[0], spec, openedNow: true);
        }

        /// <summary>
        /// <see cref="SpellTrapEffects"/> chain-seated activation (the usual Main Phase path): opens the Ritual
        /// choice for a human-controlled Ritual Spell. False when there is nothing to choose or no Ritual clause.
        /// </summary>
        public static bool TryStartDeferredRitualChoice(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget)
        {
            if (autoPickTarget || engine == null || who == null || card == null || prog == null) return false;
            if (!engine.IsHumanControlled(who)) return false;
            var clause = prog.ClausesFor(EffectTiming.Activate)
                ?.Find(c => c != null && c.Action == EffectActionKind.RitualSummon);
            if (clause == null) return false;
            return TryStartRitualChoice(engine, who, card, fromHand, clause, deferredChain: true);
        }

        /// <summary>
        /// Tribute step for <paramref name="monster"/>. With a single legal Tribute set it is used as is (no
        /// prompt): returns false when this activation was not already pending, else resumes it.
        /// </summary>
        static bool BeginRitualTributeStep(DuelEngine engine, PendingActivation p, CardInstance monster,
            WRLDZ.Duel.Rules.RitualProcedures.Spec spec, bool openedNow)
        {
            var who = p.Controller;
            var pool = WRLDZ.Duel.Rules.RitualProcedures.CollectTributePool(who, monster);
            var required = WRLDZ.Duel.Rules.RitualProcedures.RequiredLevel(monster.Def, spec);
            var sets = WRLDZ.Duel.Rules.RitualProcedures.DistinctLegalSets(who, pool, required, spec.ExactLevel);
            // TributeSummons batch (Soul Exchange): a Tribute that must include the target is not offered as a
            // free choice; resolution picks it, the target first (RitualProcedures.ResolveTributeSelection).
            if (TributeSummonsRuntime.RitualPoolHasForced(who, pool))
                sets.Clear();
            p.RitualMonster = monster;
            p.RitualRequiredLevel = required;
            p.RitualExactLevel = spec.ExactLevel;
            if (sets.Count <= 1)
            {
                p.Card.ChosenRitualMonster = monster;
                p.Card.ChosenRitualTributes = sets.Count == 1 ? sets[0] : null;
                if (openedNow) return false;
                engine.ClearPendingActivation();
                return ResumeRitualActivation(engine, p);
            }

            p.AwaitingRitualMonster = false;
            p.AwaitingRitualTributes = true;
            p.TargetKind = EffectTargetKind.RitualTribute;
            p.RitualPicks.Clear();
            p.LegalTargets.Clear();
            p.LegalTargets.AddRange(WRLDZ.Duel.Rules.RitualProcedures.SelectableTributes(
                p.RitualPicks, pool, required, spec.ExactLevel));
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool ContinueRitualChoice(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            var card = p.Card;
            var clause = CompiledEffectCache.GetOrCompile(card)?.ClausesFor(EffectTiming.Activate)
                ?.Find(c => c != null && c.Action == EffectActionKind.RitualSummon);
            if (clause == null || !WRLDZ.Duel.Rules.RitualProcedures.TryGetSpec(clause, out var spec))
                return false;

            if (p.AwaitingRitualMonster)
            {
                engine.Log($"{card.Name}: Ritual Summon {target.Name}.");
                return BeginRitualTributeStep(engine, p, target, spec, openedNow: false);
            }

            var pool = WRLDZ.Duel.Rules.RitualProcedures.CollectTributePool(p.Controller, p.RitualMonster);
            p.RitualPicks.Add(target);
            engine.Log($"{card.Name}: Tribute {target.Name} (Level {target.Level}).");
            if (WRLDZ.Duel.Rules.RitualProcedures.IsLegalTributeSet(p.RitualPicks, p.RitualRequiredLevel,
                    p.RitualExactLevel))
            {
                card.ChosenRitualMonster = p.RitualMonster;
                card.ChosenRitualTributes = p.RitualPicks.ToList();
                engine.ClearPendingActivation();
                return ResumeRitualActivation(engine, p);
            }

            p.LegalTargets.Clear();
            p.LegalTargets.AddRange(WRLDZ.Duel.Rules.RitualProcedures.SelectableTributes(
                p.RitualPicks, pool, p.RitualRequiredLevel, p.RitualExactLevel));
            if (p.LegalTargets.Count == 0) // cannot happen: every offered card keeps a completion possible
                return RestartRitualTributes(engine);
            engine.Log(p.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>CANCEL after at least one Tribute pick: start the Tribute choice over.</summary>
        public static bool RestartRitualTributes(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingRitualTributes) return false;
            var pool = WRLDZ.Duel.Rules.RitualProcedures.CollectTributePool(p.Controller, p.RitualMonster);
            p.RitualPicks.Clear();
            p.LegalTargets.Clear();
            p.LegalTargets.AddRange(WRLDZ.Duel.Rules.RitualProcedures.SelectableTributes(
                p.RitualPicks, pool, p.RitualRequiredLevel, p.RitualExactLevel));
            engine.Log($"{p.Card?.Name}: Tribute choice cleared — choose again.");
            engine.Log(p.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool ResumeRitualActivation(DuelEngine engine, PendingActivation p)
        {
            if (p.RitualDeferredChain)
                return SpellTrapEffects.ResumeDeferredRitual(engine, p.Controller, p.Card, p.FromHand);
            var prog = CompiledEffectCache.GetOrCompile(p.Card);
            s_ritualChosenFor = p.Card;
            try
            {
                return TryResolveActivation(engine, p.Controller, p.Card, p.FromHand, prog, autoPickTarget: false);
            }
            finally
            {
                s_ritualChosenFor = null;
            }
        }

        /// <summary>AI cost picks: never an Exodia piece while another card will do; cheapest first.</summary>
        internal static IEnumerable<CardInstance> PickAiDiscards(List<CardInstance> hand, int n) =>
            hand.OrderBy(h => IsForbiddenOnePiece(h?.Def) ? 1 : 0)
                .ThenBy(h => h?.Def == null ? 0 : h.Def.IsMonster ? Mathf.Max(0, h.Def.atk) : 1500)
                .Take(n)
                .ToList();

        static bool IsForbiddenOnePiece(CardDef def) =>
            def?.name != null &&
            (def.name.IndexOf("Forbidden One", StringComparison.OrdinalIgnoreCase) >= 0 ||
             def.name.StartsWith("Exodia", StringComparison.OrdinalIgnoreCase));

        static bool ResumeAfterMultiDiscard(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, bool autoPick)
        {
            var prog = CompiledEffectCache.GetOrCompile(card);
            s_multiDiscardPaidFor = card;
            try
            {
                return TryResolveActivation(engine, who, card, fromHand, prog, autoPick);
            }
            finally
            {
                s_multiDiscardPaidFor = null;
            }
        }

        internal static List<CardInstance> CollectDiscardCost(DuelistState who, string attribute,
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

                // "Discard 1 Spell" (FaceUpQuickEffectsRuntime.DiscardSpellCard; SpellTrapAnswersAndActivationLocks batch,
                // Cursed Seal of the Forbidden Spell): any Spell Card.
                if (attribute == FaceUpQuickEffectsRuntime.DiscardSpellCard)
                {
                    if (c.Def.IsSpell) list.Add(c);
                    continue;
                }

                // IgnitionCostsAndSimpleSpells batch: "1 "Pot of Greed" from your hand" — that name only, any card kind.
                var namedCost = IgnitionCostsAndSimpleSpellsRuntime.NamedHandCardName(attribute);
                if (namedCost != null)
                {
                    if (IgnitionCostsAndSimpleSpellsRuntime.MatchesNamedHandCard(c, namedCost)) list.Add(c);
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
        internal static bool CanSpecialSummonFromGyTarget(CardInstance card)
        {
            if (card?.Def == null) return false;
            var text = card.OfficialText;
            return !PsctGrammar.CannotBeSpecialSummonedFromGraveyard(text) &&
                   !PsctGrammar.BlocksSpecialSummon(text) &&
                   // Named-card Semi-Nomi (Wall Shadow): only once properly summoned.
                   TributeSummonsRuntime.SemiNomiMayBeRevived(card) &&
                   // Toon procedure (Toons batch): only once its own procedure has summoned this copy.
                   !ToonsRuntime.BlocksSpecialSummon(card);
        }

        static bool HasLegalTargetsConsideringDiscard(DuelEngine engine, DuelistState who,
            EffectClause c, CardInstance except)
        {
            if (c != null && c.Action == EffectActionKind.DiscardUpToMonstersFromHand)
                return true;
            var found = CollectTargets(engine, who, c, except).Count;
            if (c != null && c.ControllerTargetCount > 1)
                return found >= c.ControllerTargetCount;
            if (found > 0) return true;
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
            if (c.Zone == EffectZoneFilter.ControllerGyEquipSpells)
                return card.Def.IsSpell && card.Def.IsEquipSpell;
            if (c.Zone == EffectZoneFilter.EitherGyCards ||
                c.Zone == EffectZoneFilter.ControllerGyCards ||
                c.Zone == EffectZoneFilter.OppGyCards) return true;
            if (c.Zone != EffectZoneFilter.ControllerGyMonsters &&
                c.Zone != EffectZoneFilter.EitherGyMonsters)
                return false;
            if (!card.Def.IsMonster || card.Def.IsExtraDeck) return false;
            if (!string.IsNullOrEmpty(c.RaceFilter) &&
                !ClassicEraTemplates.RaceMatches(card.Race, c.RaceFilter))
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

        /// <summary>
        /// A Special Summon whose cost Tributes a monster from your own Main Monster Zone frees the zone it
        /// needs (ygopro ft&gt;-1 with a Tribute from your field): Tribute 1 monster (Tribute Doll, Ultra
        /// Evolution Pill, Insect Imitation) or the equipped monster you control (Magical Labyrinth).
        /// </summary>
        static bool CostFreesMonsterZone(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (engine == null || who == null || c == null) return false;
            if (c.RequiresTributeEquippedMonster)
                return card?.EquippedTo != null && who.TryFindMonster(card.EquippedTo, out _);
            // TargetNegationsAndHandQuickEffects batch (v163): "by Tributing this card" frees its own Monster Zone (Great
            // Dezard, ygopro sptg: ft+1 when the card is in a Main Monster Zone).
            if (c.RequiresTributeThis) return card != null && who.TryFindMonster(card, out _);
            if (c.RequiresTributeCount <= 0) return false;
            // On a full field the candidates are your own monsters only (FilterTributeCostCandidates).
            return CollectTributeCost(who, card, c, engine).Exists(m => m != null && who.TryFindMonster(m, out _));
        }

        static bool HasStructuredIgnitionCost(EffectClause c) =>
            c != null &&
            (c.RequiresTributeThis || c.RequiresTributeCount > 0 || c.RequiresDiscardSelf ||
             c.RequiresSendThisToGy || c.RequiresSendHandToGy || c.RequiresSendOtherYouControl ||
             c.RequiresSendRaceFromDeck ||
             c.PayLpAmount > 0 || c.BanishFromGyCount > 0 ||
             c.RequiresRemoveSpellCounters > 0 || c.RequiresSendEquipSpellOnThisToGy ||
             // SpellCounterSpenders batch: "Remove N Spell Counters on your side of the field".
             c.RemoveSpellCountersOnYourSide > 0 ||
             (c.RequiresDiscardCost && !c.RequiresTargetChoice));

        static bool IgnitionCostLegal(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out string reason)
        {
            reason = "OK";
            if (c == null) return true;
            // Soft residual: Spell Economics ContinuousWhileFaceUp — controller does not pay LP
            // to activate Spell Cards (Mask Soft surgical Soft ContinuousProtections skip).
            // Fail-closed: Trap LP / monster ignition / Standby maintenance still require LP.
            if (c.PayLpAmount > 0 && who.LifePoints < c.PayLpAmount &&
                !ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
            {
                reason = $"Not enough LP to pay {c.PayLpAmount}.";
                return false;
            }

            // SpellCounterSpenders batch: enough Spell Counters on your side of the field.
            if (c.RemoveSpellCountersOnYourSide > 0 &&
                !SpellCounterSpendersRuntime.SideCostPayable(engine, who, c.RemoveSpellCountersOnYourSide))
            {
                reason = $"Needs {c.RemoveSpellCountersOnYourSide} Spell Counter(s) on your side of the field.";
                return false;
            }

            if (c.RequiresTributeThis || c.RequiresTributeCount > 0)
            {
                // Mask of Restrict Soft ContinuousProtections: neither player can Tribute (cost).
                if (ContinuousProtections.NeitherPlayerCanTribute(engine))
                {
                    reason = "Neither player can Tribute cards (Mask of Restrict).";
                    return false;
                }
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
                CollectTributeCost(who, card, c, engine).Count < (c.TributeUpTo ? 1 : c.RequiresTributeCount))
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

            // LocksRedirectsAndActivity: "You cannot discard cards as a cost to activate cards or effects"
            // (ygopro EFFECT_CANNOT_DISCARD_AS_COST, per player, per effect). Sends are not discards.
            if ((c.RequiresDiscardCost || c.RequiresDiscardSelf) && !c.DiscardCostIsSend &&
                LocksRedirectsAndActivityRuntime.DiscardAsCostLocked(engine, who))
            {
                reason = "You cannot discard cards as a cost (card effect).";
                return false;
            }

            // LocksRedirectsAndActivity: a cost that requires "you have not activated any other cards or
            // effects this turn" (Lua ACTIVITY_CHAIN counter).
            if (c.Action == EffectActionKind.RequiresNoOtherActivationThisTurn &&
                !LocksRedirectsAndActivityRuntime.ActivityCostLegal(engine, who, card))
            {
                reason = "You have already activated a card or effect this turn.";
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

            if (c.RequiresSendRaceFromDeck &&
                CollectSendRaceFromDeck(engine, who, c).Count == 0)
            {
                reason = string.IsNullOrEmpty(c.RaceFilter)
                    ? "No monster of that Type in your Deck to send."
                    : $"No {c.RaceFilter} monster in your Deck to send.";
                return false;
            }

            if (c.RequiresSendEquipSpellOnThisToGy &&
                CollectEquipSpellsOnThis(who, card).Count == 0)
            {
                reason = c.EquipCostAnyEquipCard
                    ? "No Equip Card equipped to this card to send to the GY."
                    : "No Equip Spell Card equipped to this card to send to the GY.";
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
                    !c.RequiresSendThisToGy && !CostFreesMonsterZone(engine, who, card, c))
                {
                    reason = "No free Monster Zone.";
                    return false;
                }
            }

            // A named Special Summon cannot start (or have its cost paid) when the named monster is in none
            // of the listed locations (Black Dragon's Chick, Venus, Hand of Nephthys). Restored after the
            // 2026-09-24 card-batch merge dropped it; HasNamedInListedOrigins no-ops for clauses without origins.
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

            if (c.Action == EffectActionKind.TakeControlUntilEndPhase &&
                engine.FirstEmpty(who.MonsterZones) < 0)
            {
                reason = "No Monster Zone to take control.";
                return false;
            }

            return true;
        }

        internal static List<CardInstance> CollectTributeCost(DuelistState who, CardInstance source,
            EffectClause c, DuelEngine engine = null)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            // Soul Exchange: the targets are offered as if this player controlled them (filtered below like
            // any other candidate) and moved first — a Tribute this player makes must include them.
            var candidates = who.MonstersOnField().ToList();
            var forced = TributeSummonsRuntime.ForcedTributes(who);
            foreach (var f in forced)
                if (!candidates.Contains(f))
                    candidates.Add(f);
            foreach (var m in candidates)
            {
                if (m == null) continue;
                if (c.TributeFaceUpOnly && !m.FaceUp) continue;
                if (c.TributeExceptThis && m == source) continue;
                if (c.TributeExceptTokens && m.IsToken) continue;
                if (c.TributeNormalMonsterOnly && (m.Def == null || !m.Def.IsNormalMonster)) continue;
                if (!string.IsNullOrEmpty(c.TributeRaceFilter) &&
                    (m.Race == null ||
                     m.Race.IndexOf(c.TributeRaceFilter,
                         System.StringComparison.OrdinalIgnoreCase) < 0) &&
                    (m.Name == null ||
                     m.Name.IndexOf(c.TributeRaceFilter, System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                // NamedSummonPairs batch: "Tribute 1 "X"" names the Tribute exactly (TributeNamedCard) while NamedCard
                // names the summoned monster (Dedication through Light and Darkness).
                if (!string.IsNullOrEmpty(c.TributeNamedCard))
                {
                    if (!m.IsNamed(c.TributeNamedCard) &&
                        !string.Equals(m.Def?.name, c.TributeNamedCard, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                // ArchfiendRiders batch: with TributeSeriesName (Checkmate) NamedCard names the target, not the Tribute.
                else if (string.IsNullOrEmpty(c.TributeSeriesName) &&
                    !string.IsNullOrEmpty(c.NamedCard) && !m.IsNamed(c.NamedCard) &&
                    (m.Name == null ||
                     m.Name.IndexOf(c.NamedCard, System.StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                if (c.RequiresAtkLeqCost && c.Amount > 0 && m.CurrentAtk > c.Amount)
                    continue;
                if (c.RequiresAtkGeqCost && c.Amount > 0 && m.CurrentAtk < c.Amount)
                    continue;
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    (m.Def == null || m.Attribute == null ||
                     !m.Attribute.Equals(c.AttributeFilter, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(m);
            }

            // "Tribute 1 monster" may Tribute this card itself (Little Swordsman of Aile), but an
            // automatic pick (AI / auto-resolve) spends another monster first.
            if (source != null && list.Count > 1 && list.Remove(source))
                list.Add(source);
            // TributeSummons batch: full-field Special Summon costs need your own Tribute; a Type- or
            // Level-filtered summon cost never takes an opponent's face-down monster; Level-matched summons
            // (Insect Imitation) only offer a Tribute with a Deck match; Share the Pain's cost is never the
            // opponent's last monster.
            TributeSummonsRuntime.FilterTributeCostCandidates(engine, who, c, list);
            // SimpleSpellTraps batch: Burst Breath's Tribute is a monster of the Type that has a face-up victim.
            SimpleSpellTrapsRuntime.FilterTributeCostCandidates(engine, who, c, list);
            // ArchfiendRiders batch: Checkmate's Tribute is an "Archfiend" that leaves a "Terrorking Archfiend" target.
            ArchfiendRidersRuntime.FilterTributeCostCandidates(engine, who, source, c, list);
            // TokensDeclarationsAndPicks batch: Multiplication of Ants — with 1 free zone the Tribute frees your own zone.
            TokensDeclarationsAndPicksRuntime.FilterTributeCostCandidates(engine, who, c, list);
            // IgnitionCostsAndSimpleSpells batch: Mystik Wok Tributes a monster whose ATK or DEF is above 0.
            IgnitionCostsAndSimpleSpellsRuntime.FilterTributeCostCandidates(c, list);
            // AttackShieldsAndDamageZero batch: Arcane Archer's "Tribute 1 Plant-Type monster" reads the Type only.
            AttackShieldsAndDamageZeroRuntime.FilterTributeCostCandidates(c, list);
            TributeSummonsRuntime.MoveForcedFirst(who, list, forced);
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

            // RuleLocks batch: Chain Energy — a Ritual Monster in the hand that cannot pay its Special Summon toll
            // cannot be Ritual Summoned (checked before any Tribute is sent).
            if (who.Hand.Contains(target) && RuleLocksRuntime.HandSpecialSummonUnaffordable(engine, who))
            {
                engine.Log($"{source?.Name}: {RuleLocksRuntime.HandActionLpCost(engine, who)} LP must be paid to " +
                           $"Special Summon {target.Name} from the hand.");
                return false;
            }

            // Tribute first (field Tributes free a zone), then Special Summon from hand. The Tributes' "sent to the
            // GY" triggers activate after the Ritual Summon (FlipMassEffects v142).
            var summoned = false;
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var t in tributes)
                    engine.SendCardToGrave(who, t);

                who.Hand.Remove(target);
                engine.MarkSpecialSummonFromHand(target); // Chain Energy toll (RuleLocks batch)
                summoned = engine.SpecialSummonToField(who, target, BattlePosition.Attack, faceUp: true);
                if (!summoned)
                    who.Hand.Add(target); // no zone — undo the summon (Tributes stay paid, as in official rules on a full board this can't be declared)
                else
                    target.ProcedureCompleted = true; // properly Ritual Summoned: it may be revived later
            });
            if (!summoned)
            {
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
                // Altar for Tribute (SimpleTargetUtility batch v173): a printed "select 1 monster" cost
                // may pick a face-down too (its original ATK is printed); the compiler's older shaped
                // cost ("send 1 other face-up <Attribute> monster you control") always carries an
                // AttributeFilter, whose branch keeps the face-up requirement for those cards.
                if (m == null || m == source) continue;
                if (!string.IsNullOrEmpty(attribute))
                {
                    if (!m.FaceUp) continue;
                    if (m.Def == null || m.Attribute == null ||
                        !m.Attribute.Equals(attribute, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                list.Add(m);
            }

            return list;
        }

        /// <summary>
        /// Guardian Ceal's cost candidates: face-up Equip Spell Cards in your Spell &amp; Trap Zones equipped to
        /// <paramref name="source"/> (ygopro c10755153 s.costfilter).
        /// </summary>
        static List<CardInstance> CollectEquipSpellsOnThis(DuelistState who, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (who == null || source == null) return list;
            // EquipAndSpellProtection batch: Woodland Sprite — any Equip Card equipped to it, whoever controls it.
            if (EquipAndSpellProtectionRuntime.CostTakesAnyEquipCard(source))
                return EquipAndSpellProtectionRuntime.CollectAnyEquipCardsOn(who.Engine, source);
            foreach (var st in who.SpellTrapsOnField())
                if (st?.Def != null && st.FaceUp && st.Def.IsEquipSpell && st.EquippedTo == source)
                    list.Add(st);
            return list;
        }

        /// <summary>The card <see cref="SendToGraveAsCost"/> is sending right now (null otherwise).</summary>
        static CardInstance s_sentAsCost;

        /// <summary>
        /// Sends <paramref name="card"/> to the GY as a cost. Its optional "When … : You can" effects miss the timing
        /// (<see cref="TryResolveSentToGy"/>): a cost is paid at activation, so the activation, then its resolution, is
        /// the last thing to happen (Yugipedia "If... You Can VS When... You Can", Cost vs Effect: a "Jinzo - Returner"
        /// discarded for "Lightning Vortex"). Axe of Despair or Malevolent Nuzzler sent for Guardian Ceal is not placed
        /// on top of the Deck; Black Pendant's mandatory damage still applies. Used by Guardian Ceal's cost only.
        /// </summary>
        static void SendToGraveAsCost(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var outer = s_sentAsCost;
            s_sentAsCost = card;
            try
            {
                // Woodland Sprite's cost may be an Equip Card the opponent controls: it leaves its controller's zone.
                engine.SendCardToGrave(engine.ControllerOf(card) ?? who, card);
            }
            finally
            {
                s_sentAsCost = outer;
            }

            engine.Log($"Cost: send {card.Name} to the GY.");
        }

        static List<CardInstance> CollectBanishGy(DuelEngine engine, DuelistState who, EffectClause c, CardInstance source = null)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
            // ForcedActivationAndSubstitution batch (v163): Spirit Elimination — the monsters `who` controls stand in
            // for their GY monsters (aux.SpElimFilter; face-up with the named Attribute / Type when one is named).
            if (SpiritEliminationRuntime.Affects(engine, who))
            {
                var race = c.Action == EffectActionKind.SpecialSummonThisFromHand && !string.IsNullOrEmpty(c.RaceFilter)
                    ? c.RaceFilter
                    : null;
                var attribute = string.IsNullOrEmpty(c.AttributeFilter) ? null : c.AttributeFilter;
                return SpiritEliminationRuntime.Substitutes(engine, who,
                    SpiritEliminationRuntime.SourceMustStay(c) ? source : null, attribute != null || race != null,
                    m => (attribute == null || string.Equals(EquipFamilyRuntime.CurrentAttribute(m), attribute,
                             System.StringComparison.OrdinalIgnoreCase)) &&
                         (race == null || ClassicEraTemplates.RaceMatches(TypeDeclarationRuntime.CurrentRace(m), race)));
            }
            if (engine != null && ContinuousProtections.CannotBanishFromGraveyard(engine, who))
                return list;
            // RuleLocks batch: Kycoo the Ghost Destroyer — the cost payer cannot banish from either GY.
            if (RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    (g.Def.attribute == null ||
                     !g.Def.attribute.Equals(c.AttributeFilter,
                         System.StringComparison.OrdinalIgnoreCase)))
                    continue;
                // EquipFamily batch: Dark Necrofear's procedure banishes monsters of a Type (printed, in the GY).
                if (c.Action == EffectActionKind.SpecialSummonThisFromHand && !string.IsNullOrEmpty(c.RaceFilter) &&
                    !ClassicEraTemplates.RaceMatches(g.Def.race, c.RaceFilter))
                    continue;
                if (ContinuousProtections.GyMovementLocked(engine, source, g))
                    continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>One proxy per Deck id of <see cref="EffectClause.RaceFilter"/>. Not the top of the Deck.</summary>
        static List<CardInstance> CollectSendRaceFromDeck(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null || c == null ||
                string.IsNullOrEmpty(c.RaceFilter))
                return list;
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id)) continue;
                if (!engine.Database.TryGet(id, out var def) || def == null || !def.IsMonster || def.IsExtraDeck)
                    continue;
                if (!ClassicEraTemplates.RaceMatches(def.race, c.RaceFilter)) continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        internal static bool SendRaceFromDeckAsCost(DuelEngine engine, DuelistState who, CardInstance pick)
        {
            if (engine == null || who?.Deck == null || pick == null) return false;
            var idx = who.Deck.IndexOf(pick.CardId);
            if (idx < 0) return false;
            // PerCopyDeck batch (v163): the pick was made before the removal; it takes the copy's owner.
            PerCopyDeckRuntime.TakeFromDeck(who, idx, pick);
            SendToGraveAsCost(engine, who, pick);
            return true;
        }

        static List<CardInstance> CollectIgnitionCostCards(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause c)
        {
            if (c == null) return new List<CardInstance>();
            // Soul Exchange: the forced Tribute is picked first (the only offer until it is paid).
            if (c.RequiresTributeCount > 0)
                return TributeSummonsRuntime.RestrictToForced(who, CollectTributeCost(who, card, c, engine));
            if (c.RequiresSendHandToGy)
                return CollectDiscardCost(who, "", card);
            if (c.RequiresSendOtherYouControl)
                return CollectOtherYouControl(who, card, c.AttributeFilter);
            if (c.RequiresSendRaceFromDeck)
                return CollectSendRaceFromDeck(engine, who, c);
            if (c.RequiresSendEquipSpellOnThisToGy)
                return CollectEquipSpellsOnThis(who, card);
            if (c.BanishFromGyCount > 0)
                return CollectBanishGy(engine, who, c, card);
            if (c.RequiresDiscardCost)
                return CollectDiscardCost(who, c.DiscardCostAttribute, card);
            // SpellCounterSpenders batch: the cards you control that carry a Spell Counter.
            if (c.RemoveSpellCountersOnYourSide > 0)
                return SpellCounterSpendersRuntime.SideCounterSources(engine, who);
            return new List<CardInstance>();
        }

        static EffectTargetKind IgnitionCostTargetKind(EffectClause c)
        {
            if (c == null) return EffectTargetKind.None;
            if (c.RequiresTributeCount > 0) return EffectTargetKind.TributeMonsterYouControl;
            if (c.BanishFromGyCount > 0) return EffectTargetKind.BanishFromYourGy;
            if (c.RequiresSendOtherYouControl) return EffectTargetKind.SendMonsterYouControlToGy;
            if (c.RequiresSendRaceFromDeck) return EffectTargetKind.SendRaceMonsterFromDeckToGy;
            if (c.RequiresSendEquipSpellOnThisToGy) return EffectTargetKind.SendEquipSpellOnThisToGy;
            if (c.RequiresSendHandToGy || c.RequiresDiscardCost)
                return EffectTargetKind.DiscardMonsterInHand;
            if (c.RemoveSpellCountersOnYourSide > 0) return EffectTargetKind.AnyCardOnField;
            return EffectTargetKind.None;
        }

        /// <summary>
        /// Pays one picked cost card (<see cref="PayOneIgnitionCostCore"/>). A monster it sends from the field to the GY
        /// is a cost send: Michizure (BattleWindows batch) may answer it only by chaining to this activation.
        /// </summary>
        static bool PayOneIgnitionCost(DuelEngine engine, DuelistState who, CardInstance source,
            PendingActivation pending, CardInstance pick)
        {
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                return PayOneIgnitionCostCore(engine, who, source, pending, pick);
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }
        }

        static bool PayOneIgnitionCostCore(DuelEngine engine, DuelistState who, CardInstance source,
            PendingActivation pending, CardInstance pick)
        {
            if (pick == null) return false;
            // SpellCounterSpenders batch: 1 Spell Counter from the tapped card you control.
            if (SpellCounterSpendersRuntime.TryPayOneIgnitionPick(engine, who, source, pending, pick, out var counterPaid))
                return counterPaid;
            var resolved = engine.ResolveLegalEffectTarget(pick) ?? pick;
            if (pending.TargetKind == EffectTargetKind.BanishFromYourGy)
            {
                // ForcedActivationAndSubstitution batch (v163): a Spirit Elimination substitute is on the field, where
                // the GY locks do not reach it.
                var inGy = SpiritEliminationRuntime.InAGraveyard(engine, resolved);
                if (inGy && ContinuousProtections.GyMovementLocked(engine, source, resolved))
                {
                    engine.Log($"Necrovalley prevents {resolved.Name} from leaving the GY.");
                    return false;
                }
                engine.BanishCard(who, resolved, by: who);
                pending.CostNumeric++;
                engine.Log(inGy
                    ? $"Cost: banish {resolved.Name} from GY."
                    : $"Cost: banish {resolved.Name} from the field (Spirit Elimination).");
                return true;
            }

            if (pending.TargetKind == EffectTargetKind.DiscardMonsterInHand)
            {
                // EngineHygiene batch: "send 1 card from your hand to the GY" is not a discard (no discard event).
                var sendsNotDiscards = CompiledEffectCache.GetOrCompile(source)?.ClauseList
                    .Exists(c => c != null && ((c.RequiresSendHandToGy && !c.RequiresDiscardCost) ||
                                               // IgnitionCostsAndSimpleSpells batch: Avatar of The Pot sends its cost.
                                               (c.RequiresDiscardCost && c.DiscardCostIsSend &&
                                                !c.RequiresTargetChoice))) == true;
                var discarded = sendsNotDiscards
                    ? MonsterEffects.SendFromHandToGraveByInstance(who, resolved)
                    : MonsterEffects.DiscardFromHandByInstance(who, resolved, DiscardCause.Cost);
                if (discarded == null) return false;
                engine.Log($"Cost: send {discarded.Name} to the GY.");
                pending.CostNumeric = discarded.CurrentAtk;
                return true;
            }

            if (pending.TargetKind == EffectTargetKind.SendEquipSpellOnThisToGy)
            {
                if (!CollectEquipSpellsOnThis(who, source).Contains(resolved)) return false;
                SendToGraveAsCost(engine, who, resolved);
                return true;
            }

            if (pending.TargetKind == EffectTargetKind.SendRaceMonsterFromDeckToGy)
                return SendRaceFromDeckAsCost(engine, who, resolved);

            // Altar for Tribute (SimpleTargetUtility batch v173): a human's send-you-control cost pick
            // records the monster's ORIGINAL ATK (printed) for GainLpEqualToCostAmount at resolution —
            // ahead of the generic Tribute/send fallback below, which records CurrentAtk instead.
            if (pending.TargetKind == EffectTargetKind.SendMonsterYouControlToGy)
            {
                var ownerSend = engine.ControllerOf(resolved) ?? who;
                var originalAtk = resolved.Def != null ? resolved.Def.atk : 0;
                engine.SendCardToGrave(ownerSend, resolved);
                engine.Log($"Cost: send {resolved.Name} to the GY.");
                pending.CostNumeric = originalAtk;
                return true;
            }

            // Mask of Restrict Soft: TributeMonsterYouControl cost picks cannot Tribute.
            if (pending != null &&
                pending.TargetKind == EffectTargetKind.TributeMonsterYouControl &&
                ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                engine.Log("Neither player can Tribute cards (Mask of Restrict).");
                return false;
            }
            var owner = engine.ControllerOf(resolved) ?? who;
            // Insect Imitation records the Tributed monster's Level (before it leaves), not its ATK.
            var atk = TributeSummonsRuntime.TributeCostNumeric(source, resolved, resolved.CurrentAtk);
            if (pending?.TargetKind == EffectTargetKind.TributeMonsterYouControl)
            {
                ReleaseEvents.Record(engine, resolved, who); // EngineHygiene: EVENT_RELEASE
                // IgnitionCostsAndSimpleSpells batch: Mystik Wok reads the Tributed monster's ATK and DEF.
                IgnitionCostsAndSimpleSpellsRuntime.NoteTributeCost(source, resolved);
            }
            engine.SendCardToGrave(owner, resolved);
            engine.Log($"Cost: Tribute/send {resolved.Name} to the GY.");
            pending.CostNumeric = atk;
            return true;
        }

        /// <summary>
        /// Pays the activation's costs (<see cref="PayAutoIgnitionCostsCore"/>). A monster they send from the field to
        /// the GY is a cost send: Michizure (BattleWindows batch) may answer it only by chaining to this activation.
        /// </summary>
        static bool PayAutoIgnitionCosts(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, bool autoPick, out int costNumeric)
        {
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                return PayAutoIgnitionCostsCore(engine, who, card, c, autoPick, out costNumeric);
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }
        }

        static bool PayAutoIgnitionCostsCore(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, bool autoPick, out int costNumeric)
        {
            costNumeric = 0;
            if (c.PayLpAmount > 0)
            {
                // Soft residual: Spell Economics Soft ContinuousProtections Spell activate PayLp skip
                // (Mask Soft surgical). Trap / monster / Standby maintenance paths untouched.
                if (ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
                {
                    engine.Log(
                        $"{who.Name} does not pay LP to activate Spell Cards (Spell Economics).");
                }
                else
                {
                    if (who.LifePoints < c.PayLpAmount) return false;
                    engine.PayLifePoints(who, c.PayLpAmount,
                        $"Cost: pay {c.PayLpAmount} LP → {who.Name} at {who.LifePoints - c.PayLpAmount} LP.");
                }
            }

            // SpellCounterSpenders batch: the side-wide counter cost — paid here for the AI or a forced distribution; a
            // human's real choice is tapped through the AwaitingIgnitionCost picks (StartIgnition).
            if (c.RemoveSpellCountersOnYourSide > 0)
            {
                SpellCounterSpendersRuntime.BeginPayment(engine, card);
                if (autoPick &&
                    !SpellCounterSpendersRuntime.PayAuto(engine, who, card, c.RemoveSpellCountersOnYourSide))
                    return false;
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
                if (c.RequiresTributeThis && ContinuousProtections.NeitherPlayerCanTribute(engine))
                    return false;
                costNumeric = card.CurrentAtk;
                var owner = engine.ControllerOf(card) ?? who;
                if (c.RequiresTributeThis)
                    ReleaseEvents.Record(engine, card, who); // EngineHygiene: EVENT_RELEASE
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
                // ChaosBanishSummons batch: the cost's banishes are one event (Soul Absorption).
                using var costBanish = BanishEvents.Group(engine);
                for (var i = 0; i < n && i < gy.Count; i++)
                {
                    if (SpiritEliminationRuntime.InAGraveyard(engine, gy[i]) && // v163: a substitute is on the field
                        ContinuousProtections.GyMovementLocked(engine, card, gy[i]))
                    {
                        engine.Log($"Necrovalley prevents {gy[i].Name} from leaving the GY.");
                        continue;
                    }
                    engine.BanishCard(who, gy[i], by: who);
                    paid++;
                }
                costNumeric = paid;
            }

            if (c.RequiresTributeCount > 0 && autoPick)
            {
                // Mask of Restrict Soft ContinuousProtections: refuse Tribute cost payment.
                if (ContinuousProtections.NeitherPlayerCanTribute(engine))
                    return false;
                var trib = CollectTributeCost(who, card, c, engine);
                var n = c.TributeUpTo
                    ? Mathf.Min(c.RequiresTributeCount, trib.Count)
                    : c.RequiresTributeCount;
                var atk = 0;
                // Tributed together (FlipMassEffects v142: their GY triggers activate once all are paid).
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    // Tributed at the same time: a Tributed Banisher of the Light banishes the others too
                    // (RedirectAndReplace batch, v142).
                    using var together = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                    for (var i = 0; i < n && i < trib.Count; i++)
                    {
                        atk = TributeSummonsRuntime.TributeCostNumeric(c, trib[i], trib[i].CurrentAtk);
                        // IgnitionCostsAndSimpleSpells batch: Mystik Wok reads the Tributed monster's ATK and DEF.
                        IgnitionCostsAndSimpleSpellsRuntime.NoteTributeCost(card, trib[i]);
                        // Soul Exchange: the forced Tribute leaves the opponent's field.
                        ReleaseEvents.Record(engine, trib[i], who); // EngineHygiene: EVENT_RELEASE
                        engine.SendCardToGrave(engine.ControllerOf(trib[i]) ?? who, trib[i]);
                        engine.Log($"Cost: Tribute {trib[i].Name}.");
                    }
                });

                costNumeric = c.ScaleAmountByCostCount ? n : atk;
            }

            if (c.RequiresSendHandToGy && autoPick)
            {
                var hand = CollectDiscardCost(who, "", card);
                if (hand.Count == 0) return false;
                var pick = hand[0];
                costNumeric = pick.CurrentAtk;
                MonsterEffects.SendFromHandToGraveByInstance(who, pick); // a send, not a discard (EngineHygiene)
                engine.Log($"Cost: send {pick.Name} from hand to the GY.");
            }

            if (c.RequiresSendOtherYouControl && autoPick)
            {
                var others = CollectOtherYouControl(who, card, c.AttributeFilter);
                if (others.Count == 0) return false;
                // Altar for Tribute (SimpleTargetUtility batch v173): the LP gain equals the cost
                // monster's ORIGINAL ATK — printed, before modifiers (GainLpEqualToCostAmount).
                costNumeric = others[0].Def != null ? others[0].Def.atk : 0;
                engine.SendCardToGrave(who, others[0]);
                engine.Log($"Cost: send {others[0].Name} to the GY.");
            }

            if (c.RequiresSendRaceFromDeck && autoPick)
            {
                var deck = CollectSendRaceFromDeck(engine, who, c);
                if (deck.Count == 0) return false;
                if (!SendRaceFromDeckAsCost(engine, who, deck[0])) return false;
            }

            if (c.RequiresSendEquipSpellOnThisToGy && autoPick)
            {
                var equips = CollectEquipSpellsOnThis(who, card);
                if (equips.Count == 0) return false;
                SendToGraveAsCost(engine, who, equips[0]);
            }

            if (c.RequiresDiscardCost && autoPick)
            {
                var hand = CollectDiscardCost(who, c.DiscardCostAttribute, card);
                var n = DiscardNeed(c);
                if (hand.Count < n) return false;
                for (var i = 0; i < n; i++)
                {
                    // RuleLocks batch: "discard 1 random card" (Jowgen) — nobody picks it (ygopro Group.RandomSelect).
                    if (c.DiscardCostRandom)
                    {
                        hand = CollectDiscardCost(who, c.DiscardCostAttribute, card);
                        if (hand.Count == 0) return false;
                        var randomPick = hand[engine.Rng.PickIndex(hand.Count)];
                        var gone = MonsterEffects.DiscardFromHandByInstance(who, randomPick);
                        if (gone == null) return false;
                        engine.Log($"Cost: discard {gone.Name} at random.");
                        continue;
                    }
                    var pick = hand[i];
                    // IgnitionCostsAndSimpleSpells batch: Avatar of The Pot SENDS its cost (no discard event).
                    if (c.DiscardCostIsSend)
                    {
                        var sent = MonsterEffects.SendFromHandToGraveByInstance(who, pick);
                        if (sent == null) return false;
                        engine.Log($"Cost: send {sent.Name} from hand to the GY.");
                        continue;
                    }
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
            // An untargeted "Discard 1 card; …" (Lightning Vortex, Malice Dispersion, Spell Purification): the player
            // chooses the card to discard, before the card is placed; Cancel undoes the activation with nothing
            // discarded. The AI keeps the automatic pick below.
            var needsPick = chosen.Count > 0 &&
                            (clause.RequiresTributeCount > 0 || clause.RequiresSendHandToGy ||
                             clause.RequiresSendOtherYouControl ||
                             clause.RequiresSendRaceFromDeck ||
                             // Guardian Ceal: a lone Equip Spell on it is sent as is.
                             (clause.RequiresSendEquipSpellOnThisToGy && chosen.Count > 1) ||
                             (clause.BanishFromGyCount > 0 && !autoPick && who.IsPlayer) ||
                             // SpellCounterSpenders batch: which cards give the Spell Counters (a real choice only).
                             (clause.RemoveSpellCountersOnYourSide > 0 &&
                              SpellCounterSpendersRuntime.SideChoiceMatters(engine, who,
                                  clause.RemoveSpellCountersOnYourSide)) ||
                             // A random discard (Jowgen) has no pick.
                             (clause.RequiresDiscardCost && !clause.DiscardCostRandom));
            if (autoPick || !who.IsPlayer || !needsPick)
            {
                var lpBefore = who.LifePoints;
                if (!PayAutoIgnitionCosts(engine, who, card, clause, autoPick: true,
                        out var costNumeric))
                    return false;
                return FinishIgnitionAfterCost(engine, who, card, fromHand, isMonster, costNumeric,
                    autoPick, forcedTarget, paidLp: lpBefore - who.LifePoints);
            }

            if (!PayAutoIgnitionCosts(engine, who, card, clause, autoPick: false, out _))
                return false;

            var n = clause.RequiresTributeCount > 0
                ? (clause.TributeUpTo ? 1 : clause.RequiresTributeCount)
                : clause.BanishFromGyCount > 0
                    ? (clause.BanishFromGyUpTo ? 1 : clause.BanishFromGyCount)
                    : clause.RequiresDiscardCost
                        ? DiscardNeed(clause)
                        : clause.RemoveSpellCountersOnYourSide > 0
                            ? clause.RemoveSpellCountersOnYourSide
                            : 1;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                // A Set Spell/Trap is not flipped until its cost is paid: Cancel leaves it Set.
                WasSetOnField = !isMonster && !fromHand && !card.FaceUp && who.TryFindSpellTrap(card, out _),
                TargetKind = IgnitionCostTargetKind(clause),
                IsMonsterEffect = isMonster,
                UsesTextProgram = true,
                AwaitingIgnitionCost = true,
                CostPicksRemaining = n,
                // The prompt names the discard ("discard 1 card from your hand (cost)").
                DiscardCostAttribute = clause.RequiresDiscardCost ? clause.DiscardCostAttribute : null,
                // EquipAndSpellProtection batch: Woodland Sprite sends any Equip Card, not only an Equip Spell.
                // IgnitionCostsAndSimpleSpells batch: Avatar of The Pot's send cost names its card.
                PromptOverride = IgnitionCostsAndSimpleSpellsRuntime.SendCostPrompt(clause) ??
                                 (clause.RequiresSendEquipSpellOnThisToGy && clause.EquipCostAnyEquipCard
                    ? "send 1 Equip Card equipped to this card to the GY (cost)."
                    : clause.RemoveSpellCountersOnYourSide > 0
                        ? SpellCounterSpendersRuntime.SidePickPrompt(clause.RemoveSpellCountersOnYourSide - 1)
                        : null)
            };
            pending.LegalTargets.AddRange(chosen);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool FinishIgnitionAfterCost(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, bool isMonster, int costNumeric, bool autoPick = false,
            CardInstance forcedTarget = null, int paidLp = 0)
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
                if (targets.Count == 0 ||
                    (clause.ControllerTargetCount > 1 && targets.Count < clause.ControllerTargetCount))
                {
                    engine.Log(clause.ControllerTargetCount > 1
                        ? $"{card.Name}: fewer than {clause.ControllerTargetCount} legal monsters."
                        : $"{card.Name}: no legal target after cost.");
                    if (isMonster && activate.Any(c => c.OncePerTurn))
                        card.EffectUsedThisTurn = true;
                    engine.NotifyPublic();
                    return true;
                }

                if (clause.ControllerTargetCount > 1 && forcedTarget == null &&
                    (autoPick || !who.IsPlayer))
                {
                    var dummyMulti = false;
                    var n = Mathf.Min(clause.ControllerTargetCount, targets.Count);
                    for (var i = 0; i < n; i++)
                        ApplyClause(engine, who, card, clause, targets[i], ref dummyMulti, ref dummyMulti);
                    if (isMonster)
                        card.EffectUsedThisTurn = clause.OncePerTurn || card.EffectUsedThisTurn;
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

                var ignitionPicks = TargetPicks(clause, out var ignitionUpTo);
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    FromHand = fromHand,
                    WasSetOnField = !fromHand && !isMonster,
                    TargetKind = MapTargetKind(clause),
                    IsMonsterEffect = isMonster,
                    UsesTextProgram = true,
                    CostNumeric = costNumeric,
                    PaidLpCost = Mathf.Max(0, paidLp), // refunded if the target choice is cancelled
                    TargetPicksRemaining = ignitionPicks,
                    TargetPicksAreUpTo = ignitionUpTo
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
                activate.Exists(TargetingTruthRuntime.ClauseTargets));
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
            // BattleDestroyedGyRecovery batch (v162): Revival Jam's optional LP cost (PAY = flag it).
            if (BattleDestroyedGyRecoveryRuntime.TryFinishLpCost(engine, amount, out var revivalOk))
                return revivalOk;
            var p = engine?.PendingActivation;
            // StandbyBurnsAndDice batch (v162): PAY N for Inspection in the opponent's Standby Phase.
            if (StandbyBurnsAndDiceRuntime.TryAnswerLpChoice(engine, amount)) return true;
            // "pay N LP or destroy this card" upkeep choice (StandbyGrowthAndMaintenance batch, v142): PAY N keeps it.
            if (p != null && p.AwaitingUpkeepChoice)
                return p.LpCostChoices.Contains(amount) &&
                       StandbyGrowthAndMaintenanceRuntime.AnswerUpkeepChoice(engine, pay: true);
            if (p == null || !p.AwaitingLpCost || !p.UsesTextProgram) return false;
            if (!p.LpCostChoices.Contains(amount)) return false;
            var who = p.Controller;
            var card = p.Card;
            var target = p.LockedTarget;
            engine.ClearPendingActivation();
            engine.PayLifePoints(who, amount,
                $"Cost: pay {amount} LP → {who.Name} at {Mathf.Max(0, who.LifePoints - amount)} LP.");
            // Royal Decree / Imperial Order: the cost is paid, the ATK/DEF loss is negated.
            if (target != null && ContinuousNegationRuntime.NegatesOnFieldResolution(engine, card))
                target = null;
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
                if (c.RequiresBattleDamageAtLeast > 0)
                {
                    var predicted = PredictedBattleDamageTo(engine, who);
                    if (predicted < c.RequiresBattleDamageAtLeast)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Battle damage the controller would take from the current DamageCalculation pair
        /// (Nutrient Z gate). Uses <see cref="BattleMechanics.Calculate"/> — no DuelEngine rewrite.
        /// </summary>
        public static int PredictedBattleDamageTo(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return 0;
            var pr = engine.PendingResponse;
            if (pr == null || pr.Timing != ResponseTiming.DamageCalculation) return 0;
            var attacker = pr.Attacker;
            var target = pr.AttackTarget;
            var atkPlayer = pr.AttackingPlayer;
            if (attacker == null || atkPlayer == null) return 0;
            var defPlayer = atkPlayer == engine.Player ? engine.Opponent :
                atkPlayer == engine.Opponent ? engine.Player : null;
            if (defPlayer == null) return 0;

            var piercing = OfficialEffectRegistry.HasPiercing(attacker) ||
                           GrantsPiercing(attacker) ||
                           // BattleBoosts batch (v161): Dragon's Rage grants piercing to its controller's Dragons.
                           BattleBoostsRuntime.FieldGrantsPiercing(engine, attacker);
            var atkNoDes = OfficialEffectRegistry.CannotBeDestroyedByBattle(attacker) ||
                           atkPlayer.WabokuActive ||
                           // EquipAndSpellProtection batch: Heart of Clear Water.
                           EquipAndSpellProtectionRuntime.EquipPreventsBattleDestruction(engine, attacker) ||
                           // TargetNegationsAndHandQuickEffects batch (v163): Charm of Shabti.
                           TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, attacker);
            var defNoDes = target != null &&
                           (OfficialEffectRegistry.CannotBeDestroyedByBattle(target) ||
                            defPlayer.WabokuActive ||
                            EquipAndSpellProtectionRuntime.EquipPreventsBattleDestruction(engine, target) ||
                            TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible(engine, target));
            var noDmgAtk = atkPlayer.WabokuActive || atkPlayer.PreventBattleDamageThisBattle ||
                           (engine.Continuous != null && engine.Continuous.AnyPreventsBattleDamage(atkPlayer)) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(engine, atkPlayer, attacker, target);
            var noDmgDef = defPlayer.WabokuActive || defPlayer.PreventBattleDamageThisBattle ||
                           (engine.Continuous != null && engine.Continuous.AnyPreventsBattleDamage(defPlayer)) ||
                           FieldSpellEffects.ControllerAvoidsBattleDamage(engine, defPlayer, attacker, target);

            var calc = BattleMechanics.Calculate(attacker, target, piercing, atkNoDes, defNoDes,
                noDmgAtk, noDmgDef);
            BattleMechanics.Sanitize(ref calc, attacker, target, noDmgAtk, noDmgDef);
            if (who == defPlayer) return calc.DamageToDefendingPlayer;
            if (who == atkPlayer) return calc.DamageToAttackingPlayer;
            return 0;
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
                    engine.PayLifePoints(who, amount,
                        $"Cost: pay {amount} LP → {who.Name} at {Mathf.Max(0, who.LifePoints - amount)} LP.");
                    // Royal Decree / Imperial Order: the cost is paid, the ATK/DEF loss is negated.
                    if (target != null && ContinuousNegationRuntime.NegatesOnFieldResolution(engine, card))
                        target = null;
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

            // Fire Princess: each face-up copy the gaining player controls burns the opponent.
            var burnTarget = engine.OpponentOf(gaining);
            if (burnTarget == null) return;
            foreach (var m in gaining.MonstersOnField().ToList())
            {
                if (m?.Def == null || !m.FaceUp || m.IsNegated) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.InflictDamageWhenControllerGainsLp) continue;
                    engine.ApplyEffectDamage(burnTarget, c.Amount > 0 ? c.Amount : 500, m.Name);
                    if (engine.GameOver) return;
                }
            }
        }

        /// <summary>Solemn Wishes: after a draw of one or more cards, each face-up copy gains LP once.</summary>
        public static void NotifyDrew(DuelEngine engine, DuelistState who, int drawn)
        {
            if (engine == null || who == null || drawn <= 0 || engine.GameOver) return;
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.GainLpWhenControllerDraws) continue;
                    ApplyLifePointGain(engine, who, c.Amount > 0 ? c.Amount : 500, st.Name);
                    if (engine.GameOver) return;
                }
            }
        }

        /// <summary>
        /// Depth counter for coalescing multi-monster returns in one resolution
        /// (Spring of Rebirth: monster(s) → gain once, not per card).
        /// </summary>
        static int _returnToHandNotifyDepth;
        static int _monstersReturnedToOwnerHandBatched;

        /// <summary>
        /// Begin a batch of field→hand returns so Spring of Rebirth fires once for monster(s).
        /// </summary>
        public static void BeginMonsterReturnToOwnerHandBatch()
        {
            _returnToHandNotifyDepth++;
        }

        /// <summary>
        /// End a batch and apply Spring of Rebirth once if any monsters returned to hand.
        /// </summary>
        public static void EndMonsterReturnToOwnerHandBatch(DuelEngine engine)
        {
            if (_returnToHandNotifyDepth <= 0) return;
            _returnToHandNotifyDepth--;
            if (_returnToHandNotifyDepth > 0) return;
            var n = _monstersReturnedToOwnerHandBatched;
            _monstersReturnedToOwnerHandBatched = 0;
            if (n > 0)
                NotifyMonstersReturnedToOwnerHand(engine, n);
        }

        /// <summary>
        /// Spring of Rebirth: each face-up Continuous copy gains Amount LP once when monster(s)
        /// return from the field to the owner's hand (not Extra Deck, not GY-send, not destroy).
        /// </summary>
        public static void NotifyMonstersReturnedToOwnerHand(DuelEngine engine, int monsterCount)
        {
            if (engine == null || monsterCount <= 0 || engine.GameOver) return;
            if (_returnToHandNotifyDepth > 0)
            {
                _monstersReturnedToOwnerHandBatched += monsterCount;
                return;
            }

            // ContinuousTrapsAndSpiritSupport batch: Spirit's Invitation triggers once per return event.
            ContinuousTrapsAndSpiritSupportRuntime.FireSpiritReturnTriggers(engine);
            if (engine.GameOver) return;

            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField().ToList())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null ||
                            c.Action != EffectActionKind.GainLpWhenMonsterReturnsToOwnerHand)
                            continue;
                        // Cold Wave (LocksAndZones batch): Spring of Rebirth's gain is an activated Spell effect
                        // (ygopro TRIGGER_F) and cannot be activated under the lock.
                        if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                        ApplyLifePointGain(engine, who, c.Amount > 0 ? c.Amount : 500, st.Name);
                        if (engine.GameOver) return;
                    }
                }
            }
        }

        /// <summary>
        /// Mysterious Puppeteer: after either player's Normal Summon or Flip Summon (not Set,
        /// not Special Summon), each face-up monster copy gains Amount LP for its controller.
        /// </summary>
        public static void NotifyEitherPlayerNormalOrFlipSummoned(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField().ToList())
                {
                    if (m?.Def == null || !m.FaceUp || m.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(m.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c == null ||
                            c.Action != EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons)
                            continue;
                        ApplyLifePointGain(engine, who, c.Amount > 0 ? c.Amount : 500, m.Name);
                        if (engine.GameOver) return;
                    }
                }
            }
        }

        /// <summary>Zolga: a monster Tributed for a Tribute Summon pays its owner (face-up or Set).</summary>
        public static void NotifyTributedForSummon(DuelEngine engine, DuelistState owner, CardInstance tribute)
        {
            if (engine == null || owner == null || tribute?.Def == null || engine.GameOver) return;
            var prog = CompiledEffectCache.GetOrCompile(tribute.Def);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.GainLpWhenTributedForSummon) continue;
                ApplyLifePointGain(engine, owner, c.Amount > 0 ? c.Amount : 2000, tribute.Name);
            }
        }

        /// <summary>
        /// Precious Cards from Beyond: a Tribute Summon whose Tributes are worth 2 or more
        /// (a Double Coston counts as 2) draws for each face-up copy the summoner controls.
        /// </summary>
        public static void NotifyTributeSummoned(DuelEngine engine, DuelistState who, CardInstance summoned,
            int tributeValue)
        {
            if (engine == null || who == null || summoned == null || tributeValue < 2 || engine.GameOver) return;
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.DrawWhenTributeSummonedWithTwoPlus) continue;
                    // Cold Wave (LocksAndZones batch): Precious Cards from Beyond's draw is an activated Spell
                    // effect (ygopro TRIGGER_F) and cannot be activated under the lock.
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                    engine.Log($"{st.Name}: Tribute Summon with 2+ Tributes — draw {(c.Amount > 0 ? c.Amount : 2)}.");
                    engine.Draw(who, c.Amount > 0 ? c.Amount : 2, silent: false);
                    if (engine.GameOver) return;
                }
            }
        }

        internal static bool OpponentLpGainIsConverted(DuelEngine engine, DuelistState gaining)
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

        /// <summary>
        /// Insect Soldiers / Penumbral / Luminous: ATK only for this damage calculation.
        /// </summary>
        public static void ApplyDamageStepAttributeBonus(CardInstance attacker, CardInstance defender)
        {
            GrantDamageStepAttributeAtk(attacker, defender, selfIsAttacker: true);
            GrantDamageStepAttributeAtk(defender, attacker, selfIsAttacker: false);
        }

        public static void ClearDamageStepAttributeBonus(CardInstance attacker, CardInstance defender)
        {
            if (attacker != null) attacker.DamageStepAtkBonus = 0;
            if (defender != null) defender.DamageStepAtkBonus = 0;
        }

        static void GrantDamageStepAttributeAtk(CardInstance self, CardInstance other, bool selfIsAttacker)
        {
            if (self?.Def == null || other?.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(self.Def);
            if (prog == null) return;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || c.Action != EffectActionKind.DamageStepAttributeAtk) continue;
                if (c.DamageStepOnlyWhenAttacking && !selfIsAttacker) continue;
                if (string.IsNullOrEmpty(c.AttributeFilter) || other.Attribute == null ||
                    !other.Attribute.Equals(c.AttributeFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                self.DamageStepAtkBonus += c.Amount;
            }
        }

        static void TributeOneOpponentMonster(DuelEngine engine, DuelistState who, CardInstance source)
        {
            // Mask of Restrict Soft ContinuousProtections: effect Tribute barred.
            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                engine.Log("Neither player can Tribute cards (Mask of Restrict).");
                return;
            }
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            var choices = new List<CardInstance>();
            foreach (var m in opp.MonstersOnField())
                if (m != null) choices.Add(m);
            if (choices.Count == 0)
            {
                engine.Log($"{source?.Name}: opponent controls no monster to Tribute.");
                return;
            }

            if (choices.Count > 1 && opp.IsPlayer)
            {
                var pending = new PendingActivation
                {
                    Controller = opp,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingOpponentTribute = true,
                    TargetKind = EffectTargetKind.TributeMonsterYouControl
                };
                pending.LegalTargets.AddRange(choices);
                engine.SetPendingActivation(pending);
                engine.Log($"{source?.Name}: Tribute 1 monster you control.");
                return;
            }

            var pick = choices[0];
            foreach (var m in choices)
                if (m.CurrentAtk < pick.CurrentAtk) pick = m;
            ReleaseEvents.Record(engine, pick, who); // EngineHygiene: EVENT_RELEASE (reason player: the activator)
            engine.SendCardToGrave(opp, pick, sentBy: source);
            engine.Log($"{source?.Name}: {opp.Name} Tributes {pick.Name}.");
        }

        /// <summary>
        /// Swords of Revealing Light (destroyed in the End Phase of the opponent's 3rd turn) and Swords of
        /// Concealing Light (destroyed in your 2nd Standby Phase): the turn countdown is registered when the
        /// card is activated, with EFFECT_FLAG_CANNOT_DISABLE, so it is seeded even when the effect is negated
        /// (Imperial Order).
        /// </summary>
        static void SeedSwordsCountdown(CardInstance source, EffectClause clause)
        {
            if (source == null || clause == null) return;
            if (clause.Action == EffectActionKind.ApplySwordsOfRevealingLight)
                source.ContinuousTurnsRemaining = clause.Amount > 0 ? clause.Amount : 3;
            else if (clause.Action == EffectActionKind.SetAllOppMonstersFaceDownDefense)
                source.ContinuousTurnsRemaining = clause.Amount > 0 ? clause.Amount : 2;
        }

        // ── A compiled activation seated as a Chain Link (CompiledActivationChainRuntime) ──
        // While it is being activated, its effect calls are recorded here and run when its link resolves.

        /// <summary>
        /// Records <see cref="ApplyClause"/> for the activation of <paramref name="source"/>: it runs when the link
        /// resolves, on the target chosen now. A choice that does not target (Fissure's lowest ATK) is made again
        /// then from the cards that qualify, keeping this pick while it still qualifies.
        /// </summary>
        static void DeferClause(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance chosenTarget, int costNumeric)
        {
            Func<CardInstance, CardInstance> rechoose = null;
            // TargetingTruth batch (v177): one answer to "does this clause target?" (TargetingTruthRuntime).
            var choiceOnly = TargetingTruthRuntime.IsNonTargetingChoice(clause);
            if (chosenTarget != null && choiceOnly)
                rechoose = picked =>
                {
                    var now = CollectTargets(engine, who, clause, source, costNumeric);
                    if (picked != null && now.Contains(picked)) return picked;
                    return now.Count > 0 ? AutoPick(clause, now, who, engine) : null;
                };
            CompiledActivationChainRuntime.Record(engine, chosenTarget,
                isTarget: TargetingTruthRuntime.PicksTarget(clause),
                equips: clause.Action == EffectActionKind.EquipThisToTarget,
                // Equip Spells and "face-up monster" targets do not affect a target turned face-down.
                needsFaceUp: clause.Action == EffectActionKind.EquipThisToTarget ||
                             clause.Zone == EffectZoneFilter.OppFaceUpMonsters ||
                             // EngineResiduals (v163): "Target 1 face-up monster" (Offerings to the Doomed, Micro Ray).
                             (clause.Zone == EffectZoneFilter.FieldAnyMonster && clause.RequiresTargetFaceUp) ||
                             // NegationMonsters batch (v161): Double Snare destroys its target only while face-up.
                             clause.Zone == EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects ||
                             // SummonLocksAndStatRules batch: Amazoness Spellcaster's own face-up target.
                             clause.Zone == EffectZoneFilter.ControllerFaceUpSeriesMonsters ||
                             // ConditionalSpellsAndTraps batch: Destruction Ring's face-up monster you control.
                             clause.Action == EffectActionKind.DestroyTargetThenDamageBothPlayers,
                rechoose,
                target =>
                {
                    var negated = false;
                    var ended = false;
                    ApplyClause(engine, who, source, clause, target, ref negated, ref ended, costNumeric);
                });
            // EquipModel batch: Tailor of the Fickle's monster target is a target of the link too (it is checked again
            // when the Equip Card step resolves, EquipModelRuntime.ResolveSwitch).
            if (clause.Action == EffectActionKind.MoveEquipCardToAnotherCorrectTarget)
            {
                var monster = EquipModelRuntime.ClaimPlan(engine, source, chosenTarget);
                if (monster != null)
                    CompiledActivationChainRuntime.Record(engine, monster, isTarget: true, equips: false,
                        needsFaceUp: true, rechoose: null, apply: _ => { });
            }
        }

        static void DeferSecondTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance second) =>
            CompiledActivationChainRuntime.Record(engine, second, isTarget: true, equips: false,
                // SummonLocksAndStatRules batch: Amazoness Spellcaster's face-up opponent target must stay face-up.
                needsFaceUp: c.SecondAction == EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn,
                rechoose: null, apply: t => ApplySecondTarget(engine, who, source, c, t));

        static void DeferFinishSpellTrap(DuelEngine engine, DuelistState who, CardInstance card, bool stays) =>
            CompiledActivationChainRuntime.Record(engine, null, isTarget: false, equips: false, needsFaceUp: false,
                rechoose: null, apply: _ => FinishSpellTrap(engine, who, card, stays));

        static void DeferTargetingNotice(DuelEngine engine, CardInstance target) =>
            CompiledActivationChainRuntime.Record(engine, null, isTarget: false, equips: false, needsFaceUp: false,
                rechoose: null, apply: _ => NotifyTargetingEffectResolved(engine, target, true));

        static void DeferMagicalHats(DuelEngine engine, DuelistState who, CardInstance trap, CardInstance host,
            List<CardInstance> hats, int tokenCount) =>
            CompiledActivationChainRuntime.Record(engine, host, isTarget: false, equips: false, needsFaceUp: false,
                rechoose: null, apply: t =>
                {
                    if (hats != null) ResolveMagicalHats(engine, who, trap, t, hats);
                    else ResolveMagicalHatsAnime(engine, who, trap, t, tokenCount);
                });

        internal static void ApplyClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance chosenTarget, ref bool attackNegated, ref bool battleEnded,
            int costNumeric = 0)
        {
            if (clause == null) return;
            // Skull Invitation (StandbyGrowthAndMaintenance batch, v142): settle the cards an earlier step sent to
            // the GY (a cost paid at resolution: Final Destiny's discards) before this clause can remove the Skull
            // Invitation that watches them. Does nothing while no Skull Invitation is active.
            StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint(engine);
            // RedirectAndReplace batch (v142): what one clause does happens at the same time — Kotodama's same-name
            // sweep waits until it is done (two Tokens Summoned together are both new), and a Banisher of the Light
            // face-up when it starts banishes every card it sends to the GY, itself included (Dark Hole).
            using var applying = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
            // EquipAndSpellProtection batch: while a targeting clause applies, its target is "targeted by that effect"
            // (Heart of Clear Water: the equipped monster is not destroyed by an effect that targets it).
            using var targeted = EquipAndSpellProtectionRuntime.EnterTargetedClause(clause, source, chosenTarget);
            // ChaosBanishSummons batch: every card this clause banishes is banished at the same time (one EVENT_REMOVE).
            using var banishGroup = BanishEvents.Group(engine);
            // ChaosBanishSummons batch: "This card cannot attack the turn you activate this effect" is an oath of the
            // activation (Chaos Sorcerer / BLS Lua rmcost, EFFECT_FLAG_OATH): it binds even when the effect is negated.
            if (clause.ThisCannotAttackTurnActivated)
                ChaosBanishSummonsRuntime.ApplyAttackOath(engine, source);
            // Oath (Soul Exchange: "You cannot conduct your Battle Phase the turn you activate this card"):
            // it is part of the cost (Lua s.cost registers EFFECT_CANNOT_BP with EFFECT_FLAG_OATH), so it
            // binds the activation before anything below can negate or fizzle the effect — including the
            // Imperial Order / family negation gates right below (v141 cross-group audit).
            if (clause.ForbidsBattlePhaseThisTurn)
                TributeSummonsRuntime.ApplyBattlePhaseOath(engine, who, source);
            // TokensDeclarationsAndPicks batch: Stray Lambs / Scapegoat — the summon oath is part of the cost (Lua s.cost,
            // EFFECT_FLAG_OATH), bound as the card is activated.
            if (clause.ForbidsOtherSummonsThisTurn)
                TokensDeclarationsAndPicksRuntime.ApplySummonOath(engine, who, source);
            // TypeDeclaration batch (v142): the Type is declared when the card is activated (the Lua announces it
            // in the target step), so it stays on the card even when this resolution is negated (Royal Decree,
            // Metal Detector); the continuous effect that reads it applies once the negation ends.
            if (clause.Action == EffectActionKind.DeclareMonsterType)
            {
                TypeDeclarationRuntime.RecordDeclaredType(engine, source, chosenTarget);
                return;
            }
            // TokensDeclarationsAndPicks batch: DNA Transplant's Attribute, declared on activation likewise.
            if (clause.Action == EffectActionKind.DeclareMonsterAttribute)
            {
                TokensDeclarationsAndPicksRuntime.RecordDeclaredAttribute(engine, source, chosenTarget);
                return;
            }
            // RuleLocks batch: Royal Command — an activated effect of a Flip monster has its activation negated (ygopro
            // EVENT_CHAIN_ACTIVATING + NegateActivation on re:IsActiveType(TYPE_FLIP)); its continuous effects still apply.
            if (RuleLocksRuntime.SkipsFlipMonsterClause(engine, source, clause)) return;
            // IgnitionCostsAndSimpleSpells batch: Mystik Wok chooses ATK or DEF as it is activated (Lua SelectEffect in
            // s.target); the gain itself waits for the Chain Link below.
            IgnitionCostsAndSimpleSpellsRuntime.ChooseOnActivation(engine, who, source, clause);
            // A compiled activation seated as a Chain Link: the effect applies when the link resolves.
            if (clause.Timing == EffectTiming.Activate && CompiledActivationChainRuntime.IsRecording(source))
            {
                DeferClause(engine, who, source, clause, chosenTarget, costNumeric);
                return;
            }
            // Royal Decree / Imperial Order / Jinzo: a Spell/Trap effect resolving on the field while
            // its kind is negated has no effect (ContinuousNegation batch). The Swords turn countdown is
            // set up at activation and cannot be disabled (Lua EFFECT_FLAG_CANNOT_DISABLE), so it still runs.
            if (ContinuousNegationRuntime.NegatesResolvingClause(engine, who, source, clause))
            {
                SeedSwordsCountdown(source, clause);
                return;
            }
            // World Suppression / Mystic Probe / Metal Detector: a face-up card of a family negated this
            // turn applies no effect (its costs are paid outside ApplyClause and are still paid).
            if (TurnNegationTrapsRuntime.SkipsNegatedEffect(engine, source, clause)) return;
            // ChainLinkAnswers batch: Chaos Command Magician — a monster effect whose single target is a face-up copy is
            // negated (a Chain Link is negated as it resolves; this covers an effect that resolved with no Chain Link).
            if (ChainLinkAnswersRuntime.NegatesTargetingMonsterClause(engine, source, clause, chosenTarget)) return;
            // SanctuaryAndZera batch: "If "X" is not on (your side of) the field, this effect is not applied." (Archlord
            // Zerato, Saturn, Mazera DeVille) — after the oaths above, which bind anyway.
            if (SanctuaryAndZeraRuntime.NotApplied(engine, who, source, clause)) return;
            var opp = engine.OpponentOf(who);
            // The destroying effect's controller is recorded for Gust / Driving Snow (SpellTrapDestroyedResponse).
            void Destroy(CardInstance c) => DestroyCard(engine, c, source, clause.BanishIfDestroyed, who);

            // Archfiend die-roll protection: as an opponent's targeting effect would
            // resolve against a monster, its controller's die-roll Archfiend may negate
            // it and destroy the opponent's card. Skip the effect when negated. A roll that waits on a
            // re-roll choice (CoinDiceInterrupts batch, Dice Re-Roll) applies the effect afterwards unless negated.
            // TargetingTruth batch (v177): only a clause that takes a TARGET rolls (TargetingTruthRuntime.PicksTarget) —
            // not a choice made without targeting (Fissure's lowest ATK, Lua c66788016 Select), and once per effect: an
            // implied target ("Target the attacking monster") does not roll here.
            if (chosenTarget != null && TargetingTruthRuntime.PicksTarget(clause) &&
                clause.Action != EffectActionKind.DieRollNegateWhenTargeted &&
                !CoinDiceInterruptsRuntime.ArchfiendRollsSettled(clause, chosenTarget) &&
                ArchfiendTargetNegation.TryNegate(engine, who, source, chosenTarget,
                    () => CoinDiceInterruptsRuntime.ReapplyAfterArchfiendRolls(engine, who, source, clause,
                        chosenTarget, costNumeric)))
                return;

            switch (clause.Action)
            {
                case EffectActionKind.DieRollNegateWhenTargeted:
                    // Continuous protection — resolved reactively by ArchfiendTargetNegation.
                    break;

                // ChaosBanishSummons batch: Chaos Sorcerer / BLS - Envoy of the Beginning, Chaosrider Gustaph, Primal Seed;
                // Soul Absorption (BanishEvents) and Strike Ninja (its own Quick Effect link) resolve elsewhere.
                case EffectActionKind.BanishTargetedFieldMonster:
                    ChaosBanishSummonsRuntime.BanishTargetedFieldMonster(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.MakeSecondAttackInARow:
                    ChaosBanishSummonsRuntime.GrantChainAttack(engine, who, source, clause);
                    break;
                case EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach:
                    ChaosBanishSummonsRuntime.ResolveGustaph(engine, who, source, clause);
                    break;
                case EffectActionKind.AddBanishedTargetsToHand:
                    ChaosBanishSummonsRuntime.AddBanishedToHand(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.GainLpPerCardBanished:
                case EffectActionKind.BanishThisUntilEndPhase:
                    break;
                // BattleAndPositionRecords batch: Atomic Firefly's burn (chosenTarget = the monster that destroyed it), Judgment
                // of the Desert's record (its activation), Curse of Anubis. Disc Fighter (battle confirm), Ghost Knight of Jackal
                // / Legacy Hunter (NotifyDestroyedOpponentByBattle) and The Unhappy Girl's continuous clauses apply elsewhere.
                case EffectActionKind.InflictDamageToPlayerWhoDestroyedThis:
                    BattleAndPositionRecordsRuntime.InflictDamageToDestroyer(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition:
                    if (clause.Timing == EffectTiming.Activate)
                        BattleAndPositionRecordsRuntime.StartFlipRecord(engine, who, source);
                    break;
                case EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock:
                    BattleAndPositionRecordsRuntime.ChangeToDefenseZeroDefLock(engine, who, source);
                    break;
                case EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation:
                case EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense:
                case EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle:
                case EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition:
                case EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition:
                    break;

                case EffectActionKind.Draw:
                    // Every existing Draw clause sets Side = Controller explicitly; only
                    // "your opponent draws N" (The Bistro Butcher) lands on the other side
                    // (local: Side = Opponent; GitHub: OpponentIsSubject).
                    engine.Draw((clause.Side == EffectSide.Opponent || clause.OpponentIsSubject) && opp != null ? opp : who,
                        Mathf.Max(1, clause.Amount), silent: false);
                    break;

                case EffectActionKind.DrawThenDiscard:
                    // Graceful Charity: draw, then (only if every card was drawn) discard of the drawer's choice.
                    DrawThenDiscardRuntime.Resolve(engine, who, source, clause);
                    break;

                // GyHandAndSummonEventTriggers: Emissary of the Afterlife — each player adds 1 Level N or lower
                // Normal Monster from their own Deck to their hand (selected, not targeted).
                case EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck:
                    GyHandAndSummonEventTriggersRuntime.EachPlayerAddsFromDeck(engine, who, source, clause);
                    break;

                // GyHandAndSummonEventTriggers: "7" — draw only while exactly N face-up copies are in your
                // Spell & Trap Zones, then destroy every face-up copy on the field together.
                case EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed:
                    GyHandAndSummonEventTriggersRuntime.OnSevenActivated(engine, who, source, clause);
                    break;

                // GyHandAndSummonEventTriggers: Beckoning Light — discard the whole hand as one event, then add
                // that many of the Attribute from the GY when the GY holds enough (otherwise none).
                case EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard:
                    GyHandAndSummonEventTriggersRuntime.DiscardHandThenAdd(engine, who, source, clause);
                    break;

                // LvChain: Dark Mimic LV3 — draw 1, or 2 when this card was Special Summoned by "Dark Mimic LV1".
                case EffectActionKind.DrawAmountWhenSpecialSummonedByNamed:
                    LvChainRuntime.ResolveDraw(engine, who, source, clause);
                    break;

                case EffectActionKind.Destroy:
                    s_lastDestroyHits = 0;
                    if (clause.Zone == EffectZoneFilter.FaceUpNamedSpellTraps)
                    {
                        // Dragon Piper: destroy all face-up "X" on the field (not a target). Weather Report
                        // (Side = Opponent): only "your opponent's face-up X".
                        var named = new List<CardInstance>();
                        var namedSides = clause.Side == EffectSide.Opponent ? new[] { opp }
                            : clause.Side == EffectSide.Controller ? new[] { who }
                            : new[] { who, opp };
                        foreach (var side in namedSides)
                            foreach (var st in side.SpellTrapsOnField())
                                if (st != null && st.FaceUp && st.IsNamed(clause.NamedCard))
                                    named.Add(st);
                        foreach (var st in named)
                        {
                            Destroy(st);
                            if (engine.ControllerOf(st) == null) s_lastDestroyHits++;
                        }
                        break;
                    }

                    if (chosenTarget != null && chosenTarget.FaceUp &&
                        (clause.Zone == EffectZoneFilter.OppFaceDownMonsters ||
                         clause.Zone == EffectZoneFilter.FaceDownSpellTraps))
                    {
                        engine.Log($"{source?.Name}: {chosenTarget.Name} is no longer face-down — no effect.");
                        break;
                    }

                    if (chosenTarget != null && !string.IsNullOrEmpty(clause.TargetCardKind) &&
                        !CardIsKind(chosenTarget, clause.TargetCardKind))
                    {
                        // Set card revealed: not the printed kind → returned to its position.
                        engine.Log(
                            $"{source?.Name}: {chosenTarget.Name} is revealed — not a {clause.TargetCardKind}; " +
                            "it returns to its original position.");
                        break;
                    }

                    if (clause.Zone == EffectZoneFilter.AllOtherCardsOnField)
                    {
                        // Destroyed together: their "sent to the GY" triggers activate afterwards (FlipMassEffects v142).
                        FlipMassEffectsRuntime.Simultaneously(engine, () =>
                        {
                            foreach (var m in CollectAllOtherCards(engine, who, source).ToList())
                                Destroy(m);
                        });
                        break;
                    }

                    if (chosenTarget != null && clause.PurgeDecksIfTrap)
                    {
                        // Nobleman of Extermination: destroy + banish; a Trap also leaves both Decks.
                        var wasTrap = chosenTarget.Def != null && chosenTarget.Def.IsTrap;
                        var purgeName = chosenTarget.Def?.name;
                        Destroy(chosenTarget);
                        if (engine.ControllerOf(chosenTarget) != null) break; // not destroyed
                        chosenTarget.FaceUp = true;
                        if (wasTrap)
                            PurgeNameFromBothDecks(engine, purgeName, source);
                        break;
                    }

                    // TargetedClassics Soft: Nobleman of Crossout — destroy+banish face-down; purge Decks if Flip.
                    if (chosenTarget != null && clause.PurgeDecksIfFlipMonster)
                    {
                        TargetedClassicsRuntime.DestroyBanishThenPurgeIfFlip(engine, source, chosenTarget, Destroy,
                            name => PurgeNameFromBothDecks(engine, name, source));
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
                        // TargetingTruth batch (v177): a choice that does not target (Fissure under Lord of D.) is stopped
                        // only by "unaffected", not by "cannot be targeted".
                        if (TargetingTruthRuntime.ProtectedAtResolution(engine, clause, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        if (clause.Zone == EffectZoneFilter.AttackingMonster)
                            attackNegated = true;
                        if (clause.TributeChosenTarget)
                        {
                            var owner = OwnerOf(engine, who, chosenTarget) ?? who;
                            ReleaseEvents.Record(engine, chosenTarget, who); // EngineHygiene: EVENT_RELEASE
                            engine.SendCardToGrave(owner, chosenTarget);
                            engine.Log($"Tributed {chosenTarget.Name}.");
                        }
                        else if (PassesDestroyIfType(engine, chosenTarget, clause))
                        {
                            var wasOnField = engine.ControllerOf(chosenTarget) != null;
                            var controllerBefore = engine.ControllerOf(chosenTarget);
                            Destroy(chosenTarget);
                            // Assault on GHQ: "destroy it, then send N cards from the top of your opponent's Deck to
                            // the GY" only once the target is destroyed (Lua: Duel.Destroy(tc)>0), through the
                            // same SendFromTopOfDeckToGy arm as every other effect mill.
                            if (clause.ThenMillOpponentDeck > 0 && wasOnField &&
                                engine.ControllerOf(chosenTarget) == null)
                                ApplyClause(engine, who, source, ThenMillOpponentDeckClause(clause), null,
                                    ref attackNegated, ref battleEnded);
                            // "and if you do, inflict N damage to its controller": the controller it had
                            // when it left the field, which can be the player who activated this card.
                            if (clause.DamageItsController > 0 && wasOnField && controllerBefore != null &&
                                engine.ControllerOf(chosenTarget) == null)
                                engine.ApplyEffectDamage(controllerBefore, clause.DamageItsController, source?.Name);
                        }
                        break;
                    }

                    // "Target 1 …; destroy it" with no chosen target does nothing. It must never fall
                    // through to "destroy every match" (MST acting like Heavy Storm, DR-01).
                    if (clause.RequiresTargetChoice && chosenTarget == null)
                    {
                        engine.Log($"{source?.Name ?? "Effect"}: no target chosen — nothing is destroyed.");
                        break;
                    }

                    var mass = CollectAllMatching(engine, who, clause).ToList();
                    // Declared Type (Tribe-Infecting Virus): face-up monsters of the Type announced with this
                    // activation. Face-down monsters stay. No other Destroy clause sets RaceFilterIsDeclared.
                    if (clause.RaceFilterIsDeclared)
                    {
                        var declared = TypeDeclarationRuntime.DeclaredTypeOf(source);
                        mass.RemoveAll(m => m == null || !m.FaceUp ||
                                            string.IsNullOrEmpty(declared) ||
                                            !ClassicEraTemplates.RaceMatches(m.Race, declared));
                    }
                    if (clause.AmountIsLevel && clause.Amount > 0)
                        mass.RemoveAll(m => m == null || m.Level != clause.Amount);
                    // SpellCounterSpenders batch: Legendary Flame Lord — "all monsters on the field except this card".
                    if (clause.ExceptThisCard && source != null)
                        mass.RemoveAll(m => ReferenceEquals(m, source));
                    if (clause.FaceUpContinuousSpellsOnly)
                        mass.RemoveAll(m => m == null || !m.FaceUp || m.Def == null ||
                                            !m.Def.IsSpell || !m.Def.IsContinuousSpellOrTrap);
                    if (clause.FaceUpContinuousTrapsOnly)
                        mass.RemoveAll(m => m == null || !m.FaceUp || m.Def == null ||
                                            !m.Def.IsTrap || !m.Def.IsContinuousSpellOrTrap);
                    // Cards actually destroyed (a card that stays on the field is not counted): Thunder Crash's
                    // "for each monster destroyed" reads this through CountSource PreviousClauseHits. Counted
                    // locally so a destroy nested inside one of these destructions cannot reset it.
                    var massHits = 0;
                    // Destroyed together (Dark Hole, Raigeki, Mirror Force, Torrential Tribute): their "sent to the GY"
                    // triggers activate once every one of them is in the GY (FlipMassEffects v142). The count is
                    // stored after those triggers, so a destruction they cause cannot overwrite it.
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var m in mass)
                        {
                            Destroy(m);
                            if (m != null && engine.ControllerOf(m) == null) massHits++;
                        }
                    });
                    s_lastDestroyHits = massHits;
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
                    // The target must still be in a GY (ygopro IsRelateToEffect): a chained Call of the
                    // Haunted may already have Special Summoned the same monster.
                    var gyOwner = who.Graveyard.Contains(chosenTarget) ? who
                        : opp.Graveyard.Contains(chosenTarget) ? opp
                        : null;
                    if (gyOwner == null)
                    {
                        engine.Log($"{chosenTarget.Name} is no longer in the GY — nothing is Special Summoned.");
                        break;
                    }
                    // The Shallow Grave: SS to the GY owner's field. Monster Reborn / Call keep activator.
                    var ssOwner = clause.SummonToTargetsOwner ? gyOwner : who;
                    gyOwner.Graveyard.Remove(chosenTarget);
                    var ssPos = clause.SummonInDefense || clause.SummonFaceDown
                        ? BattlePosition.Defense
                        : BattlePosition.Attack;
                    var ssFaceUp = !clause.SummonFaceDown;
                    DuelEngine.RecordOwnerOnFieldEntry(chosenTarget, gyOwner, ssOwner);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (!engine.SpecialSummonToField(ssOwner, chosenTarget, ssPos, ssFaceUp))
                    {
                        // Restore to the GY it came from.
                        chosenTarget.ControlOwner = null;
                        gyOwner.Graveyard.Add(chosenTarget);
                        engine.Log("Special Summon failed — no zone.");
                    }
                    else
                    {
                        engine.Log(clause.SummonFaceDown && !chosenTarget.FaceUp
                            ? $"Special Summoned {chosenTarget.Name} from GY in face-down Defense Position."
                            : $"Special Summoned {chosenTarget.Name} from GY.");
                        if (clause.DestroyHostWhenThisLeaves && source != null)
                        {
                            source.EquippedTo = chosenTarget;
                            if (!chosenTarget.Equips.Contains(source))
                                chosenTarget.Equips.Add(source);
                            // GuardiansAndEquipEvents batch: Premature Burial is an Equip Spell equipped by Duel.Equip
                            // (EVENT_EQUIP) — Morale Boost. A Continuous Trap link (Call of the Haunted) records nothing.
                            GuardiansAndEquipEventsRuntime.NoteEquipped(engine, source, chosenTarget);
                        }
                        if (clause.SummonCannotBeTributed)
                        {
                            // "It cannot be Tributed." — not for a Tribute Summon, and not for any other Tribute.
                            chosenTarget.CannotBeTributedForSummon = true;
                            chosenTarget.CannotBeTributed = true;
                        }
                        if (clause.SummonDestroyAtEndPhase)
                        {
                            chosenTarget.TempDestroyOnEndOfTurn = engine.TurnNumber;
                            engine.Log(
                                $"{chosenTarget.Name} will be destroyed during the End Phase this turn.");
                        }
                    }
                    break;

                case EffectActionKind.SpecialSummonFromDeck:
                    // Insect Imitation: Level = the Tributed monster's Level (costNumeric) + offset.
                    if (clause.MatchTributedLevel)
                    {
                        TributeSummonsRuntime.SpecialSummonLevelMatchedFromDeck(engine, who, source, clause,
                            chosenTarget, costNumeric);
                        break;
                    }
                    TrySpecialSummonFromDeck(engine, who, clause, chosenTarget);
                    break;

                case EffectActionKind.SpecialSummonFromHand:
                {
                    // Tribute Doll: exact Level, can be Normal Summoned/Set, cannot attack this turn.
                    if (TributeSummonsRuntime.HandlesHandSummon(clause))
                    {
                        TributeSummonsRuntime.SpecialSummonFromHandFiltered(engine, who, source, clause,
                            chosenTarget);
                        break;
                    }
                    if (clause.RequiresLordOfDOnField && !LordOfDOnField(engine))
                    {
                        engine.Log("Lord of D. is not on the field — effect resolves without summoning.");
                        break;
                    }

                    // Beast Soul Swap: same Level as prior bounced target (fail-closed).
                    // chosenTarget here is the bounce target (RequiresTargetChoice clauses share pick).
                    if (clause.MatchPriorTargetLevel)
                    {
                        if (chosenTarget == null)
                        {
                            engine.Log($"{source?.Name}: no prior target Level for hand SS — resolve without summoning.");
                            break;
                        }
                        var priorLevel = chosenTarget.Level;
                        var matchPool = (who.Hand ?? new List<CardInstance>())
                            .Where(c => c?.Def != null &&
                                        DefMatchesSummonFilter(c.Def, clause) &&
                                        c.Level == priorLevel);
                        var summon = matchPool.FirstOrDefault();
                        if (summon != null && engine.FirstEmpty(who.MonsterZones) >= 0)
                        {
                            who.Hand.Remove(summon);
                            engine.MarkSpecialSummonFromHand(summon); // Chain Energy toll (RuleLocks batch)
                            if (!engine.SpecialSummonToField(who, summon, BattlePosition.Attack, true))
                                who.Hand.Add(summon);
                            else
                                engine.Log($"Special Summoned {summon.Name} from hand (same Level {priorLevel}).");
                        }
                        else
                            engine.Log($"{source?.Name}: no hand Beast at Level {priorLevel} — resolve without summoning.");
                        break;
                    }

                    if (chosenTarget != null)
                    {
                        if (who.Hand != null && who.Hand.Contains(chosenTarget) &&
                            DefMatchesSummonFilter(chosenTarget.Def, clause) &&
                            engine.FirstEmpty(who.MonsterZones) >= 0)
                        {
                            who.Hand.Remove(chosenTarget);
                            engine.MarkSpecialSummonFromHand(chosenTarget); // Chain Energy toll (RuleLocks batch)
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
                        pool = pool.Where(c => c.Race != null &&
                                               ClassicEraTemplates.RaceMatches(c.Race, clause.RaceFilter));

                    var take = clause.AmountIsLevel ? 1 : (clause.SpecialSummonUpTo ? Mathf.Max(1, clause.Amount) : Mathf.Max(1, clause.Amount));
                    foreach (var d in pool.Take(take).ToList())
                    {
                        if (engine.FirstEmpty(who.MonsterZones) < 0) break;
                        who.Hand.Remove(d);
                        engine.MarkSpecialSummonFromHand(d); // Chain Energy toll (RuleLocks batch)
                        if (!engine.SpecialSummonToField(who, d, BattlePosition.Attack, true))
                            who.Hand.Add(d);
                        else
                            engine.Log($"Special Summoned {d.Name} from hand.");
                    }

                    break;
                }

                case EffectActionKind.AddFromGyToHand:
                    if (chosenTarget != null && who.Graveyard.Contains(chosenTarget) &&
                        chosenTarget.Def != null && chosenTarget.Def.IsExtraDeck)
                    {
                        // Backup Soldier on an effectless Fusion: it returns to the Extra Deck.
                        who.Graveyard.Remove(chosenTarget);
                        who.ExtraDeck.Add(chosenTarget.CardId);
                        engine.Log($"{chosenTarget.Name} returns from the GY to the Extra Deck.");
                    }
                    else if (chosenTarget != null && who.Graveyard.Contains(chosenTarget))
                    {
                        if (ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                        {
                            engine.Log("Necrovalley prevents the target from leaving the GY.");
                            break;
                        }
                        who.Graveyard.Remove(chosenTarget);
                        who.Hand.Add(chosenTarget);
                        engine.Log($"Added {chosenTarget.Name} from GY to hand.");
                        // SimpleTargetUtility batch (v173): Fairy of the Spring — "That Equip Spell
                        // Card cannot be activated this turn." Stamped only when the add resolved;
                        // checked in CanActivate and self-expiring at the next TurnNumber change.
                        if (clause.LocksAddedCardActivationThisTurn)
                            chosenTarget.ActivationBlockedUntilTurn = engine.TurnNumber;
                    }

                    break;

                case EffectActionKind.AddNamedFromDeckOrGyToHand:
                {
                    // MfcSimpleSpellsB batch (v174) — Emblem of Dragon Destroyer: add 1 "Buster Blader"
                    // from the Deck or the GY. Deck first, then GY (ygopro LOCATION_DECK + LOCATION_GRAVE).
                    var name = clause.NamedCard;
                    if (string.IsNullOrEmpty(name))
                    {
                        engine.Log($"{source?.Name}: no card name to search for.");
                        break;
                    }

                    var db = engine.Database ?? CardDatabase.Load();
                    CardDef def = null;
                    int deckIdx = -1;
                    for (var i = 0; i < who.Deck.Count; i++)
                    {
                        var d = db?.Get(who.Deck[i]);
                        if (d != null && d.IsMonster && NameMatchesQuoted(d, name, false))
                        {
                            def = d; deckIdx = i; break;
                        }
                    }

                    if (deckIdx >= 0)
                    {
                        who.Deck.RemoveAt(deckIdx);
                        var inst = engine.CreateCardInstance(def.id);
                        who.Hand.Add(inst);
                        engine.Log($"{source?.Name}: add {inst.Name} from Deck to hand.");
                        break;
                    }

                    CardInstance gyTarget = null;
                    foreach (var c in who.Graveyard)
                    {
                        if (c?.Def != null && c.Def.IsMonster && NameMatchesQuoted(c.Def, name, false))
                        {
                            gyTarget = c; break;
                        }
                    }

                    if (gyTarget != null)
                    {
                        if (ContinuousProtections.GyMovementLocked(engine, source, gyTarget))
                        {
                            engine.Log("Necrovalley prevents the target from leaving the GY.");
                            break;
                        }
                        who.Graveyard.Remove(gyTarget);
                        who.Hand.Add(gyTarget);
                        engine.Log($"{source?.Name}: add {gyTarget.Name} from GY to hand.");
                        break;
                    }

                    engine.Log($"{source?.Name}: no {name} in Deck or GY.");
                    break;
                }

                case EffectActionKind.AddFromDeckToHand:
                {
                    if (chosenTarget != null)
                    {
                        MonsterEffects.AddCardIdFromDeckToHand(engine, who, chosenTarget.CardId,
                            source?.Name ?? "Search");
                        if (clause.LocksAddedCardNameThisTurn)
                            LockActivateNameThisTurn(engine, who, chosenTarget.Name);
                        break;
                    }

                    var picks = CollectTargets(engine, who, clause, source);
                    if (picks.Count > 0)
                    {
                        MonsterEffects.AddCardIdFromDeckToHand(engine, who, picks[0].CardId,
                            source?.Name ?? "Search");
                        if (clause.LocksAddedCardNameThisTurn)
                            LockActivateNameThisTurn(engine, who, picks[0].Name);
                        break;
                    }

                    if (clause.Zone == EffectZoneFilter.DeckFieldSpells ||
                        clause.Zone == EffectZoneFilter.DeckEquipSpells ||
                        clause.Zone == EffectZoneFilter.DeckRitualMonsters ||
                        clause.Zone == EffectZoneFilter.DeckRitualSpells ||
                        clause.Zone == EffectZoneFilter.DeckRitualMonstersOrSpells ||
                        clause.Zone == EffectZoneFilter.DeckMonstersRaceLevelLeq ||
                        // SimpleTargetUtility batch (v173): a Guardian search with an empty pick must fail
                        // closed here, never fall into the ATK-cap monster loop below.
                        clause.Zone == EffectZoneFilter.ControllerDeckCards)
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
                        if (clause.LocksAddedCardNameThisTurn)
                            LockActivateNameThisTurn(engine, who, inst.Name);
                        added = true;
                        break;
                    }

                    if (!added)
                        engine.Log(
                            $"Deck search: no legal card in Deck (remaining={who.DeckCount}).");
                    break;
                }

                case EffectActionKind.ChangeToFaceUpAttack:
                    if (chosenTarget != null)
                        SetFaceUpPosition(engine, chosenTarget, BattlePosition.Attack,
                            clause.SuppressFlipEffects);
                    break;

                case EffectActionKind.ChangeToFaceUpDefense:
                    if (chosenTarget != null)
                        SetFaceUpPosition(engine, chosenTarget, BattlePosition.Defense);
                    break;

                case EffectActionKind.ConductBattlePhaseTwice:
                    // Weather Report (SpellTrapDestroyedResponse batch): "If X is destroyed, you can perform your
                    // Battle Phase twice …" — only when the previous Destroy clause destroyed one.
                    if (clause.RequiresPreviousClauseHit && s_lastDestroyHits <= 0)
                    {
                        engine.Log($"{source?.Name ?? "Effect"}: nothing was destroyed — no second Battle Phase.");
                        break;
                    }
                    SpellTrapDestroyedResponseRuntime.GrantBattlePhaseTwice(engine, who, source);
                    break;

                case EffectActionKind.ChangeAllToAttackPosition:
                    if (clause.RequiresPreviousClauseHit && s_lastDestroyHits <= 0)
                        break;
                    foreach (var side in new[] { who, opp })
                        foreach (var m in side.MonstersOnField().ToList())
                        {
                            if (m?.Def == null || !m.FaceUp) continue;
                            if (!ClassicEraTemplates.RaceMatches(m.Race, clause.RaceFilter)) continue;
                            if (m.Position == BattlePosition.Attack) continue;
                            m.Position = BattlePosition.Attack;
                            engine.Log($"{m.Name} → Attack Position.");
                        }
                    break;

                case EffectActionKind.LoseLifePoints:
                {
                    var loser = clause.OpponentIsSubject ? opp : who;
                    var lost = clause.HalveLifePoints
                        ? (loser.LifePoints + 1) / 2
                        : Mathf.Max(0, clause.Amount);
                    engine.LoseLifePoints(loser, lost, source?.Name);
                    break;
                }

                case EffectActionKind.DiscardRandomFromHand:
                {
                    var victim = clause.OpponentIsSubject ? opp : who;
                    // DiscardTriggers (v142): discards by this card's effect (Electric Snake / Minar).
                    DiscardTriggersRuntime.BeginScope(engine);
                    try
                    {
                        for (var i = 0; i < Mathf.Max(1, clause.Amount); i++)
                        {
                            if (victim.Hand == null || victim.Hand.Count == 0) break;
                            var idx = engine.Rng.PickIndex(victim.Hand.Count);
                            var gone = MonsterEffects.DiscardFromHandByInstance(victim, victim.Hand[idx]);
                            if (gone != null)
                            {
                                engine.Log($"{victim.Name} discards {gone.Name} at random ({source?.Name}).");
                                DiscardTriggersRuntime.NoteDiscard(engine, victim, gone, who);
                            }
                        }
                    }
                    finally
                    {
                        DiscardTriggersRuntime.EndScope(engine);
                    }
                    break;
                }

                case EffectActionKind.EndBattlePhase:
                    if (engine.Phase == DuelPhase.Battle && engine.TurnPlayer != null)
                    {
                        engine.Log($"{source?.Name}: the Battle Phase ends.");
                        engine.ForceEndBattlePhase(engine.TurnPlayer);
                        battleEnded = true;
                    }
                    break;

                case EffectActionKind.ShuffleDeck:
                    engine.Rng.Shuffle(who.Deck);
                    engine.Log($"{who.Name} shuffles their Deck ({source?.Name}).");
                    break;

                case EffectActionKind.ShuffleGySpellsIntoDeck:
                {
                    // MfcSimpleSpellsB batch (v174) — Hidden Spellbook: target 2 Spell Cards in the
                    // controller's GY, shuffle those targets into the Deck. Each is removed from the GY
                    // and appended to the Deck; the Deck is shuffled once at the end.
                    var targets = CollectTargets(engine, who, clause, source);
                    if (targets.Count == 0)
                    {
                        engine.Log($"{source?.Name}: no Spell Card in the GY to shuffle.");
                        break;
                    }

                    var removed = 0;
                    foreach (var t in targets)
                    {
                        if (t == null || !who.Graveyard.Contains(t)) continue;
                        who.Graveyard.Remove(t);
                        who.Deck.Add(t.CardId);
                        removed++;
                    }

                    if (removed > 0)
                    {
                        engine.Rng.Shuffle(who.Deck);
                        engine.Log($"{source?.Name}: shuffled {removed} Spell Card(s) from the GY into the Deck.");
                    }
                    break;
                }

                case EffectActionKind.ForbidBattlePhaseThisTurn:
                    // MfcSimpleSpellsB batch (v174) — RESERVED, no card compiles to it yet (Thunder of
                    // Ruler stays fail-closed: no activation path for a Set Normal Trap on a pure
                    // StandbyPhase timing). Reuses the same turn stamp as Soul Exchange's Battle Phase
                    // oath (TributeSummonsRuntime.ApplyBattlePhaseOath): DuelistState.
                    // BattlePhaseForbiddenTurn, read by TributeSummonsRuntime.BattlePhaseForbidden in
                    // TryEnterBattlePhase, so the ban is a property of the turn and survives this card
                    // leaving the field.
                    TributeSummonsRuntime.ApplyBattlePhaseOath(engine, who, source);
                    break;

                case EffectActionKind.DiscardChosenFromHand:
                {
                    var handOwner = chosenTarget != null && opp.Hand.Contains(chosenTarget) ? opp : who;
                    if (chosenTarget != null && handOwner.Hand.Contains(chosenTarget))
                    {
                        var gone = MonsterEffects.DiscardFromHandByInstance(handOwner, chosenTarget);
                        engine.Log($"{handOwner.Name} discards {chosenTarget.Name} ({source?.Name}).");
                        // DiscardTriggers (v142): Confiscation discards by this card's effect.
                        DiscardTriggersRuntime.NoteDiscard(engine, handOwner, gone, who);
                    }
                    break;
                }

                case EffectActionKind.ReturnChosenToDeckShuffle:
                    if (chosenTarget != null && opp.Hand.Remove(chosenTarget))
                    {
                        opp.Deck.Add(chosenTarget.CardId);
                        engine.Rng.Shuffle(opp.Deck);
                        engine.Log($"{chosenTarget.Name} returns to {opp.Name}'s Deck, which is shuffled.");
                    }
                    // Des Feral Imp (SimpleTargetUtility batch v173): a GY target is shuffled into the
                    // controller's Deck (Zone ControllerGyCards; the same shuffle arm as the hand origin
                    // above, which stays first so Forceful Sentry's behavior is unchanged).
                    else if (chosenTarget != null && who.Graveyard.Contains(chosenTarget))
                    {
                        if (ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                        {
                            engine.Log("Necrovalley prevents the target from leaving the GY.");
                            break;
                        }
                        who.Graveyard.Remove(chosenTarget);
                        // DcrSpellTrapUtility batch: an Extra Deck monster (Ray of Hope's LIGHT Fusion) returns to
                        // the Extra Deck, not the Main Deck (ygopro SendtoDeck).
                        if (chosenTarget.Def != null && chosenTarget.Def.IsExtraDeck)
                        {
                            who.ExtraDeck.Add(chosenTarget.CardId);
                            engine.Log($"{chosenTarget.Name} returns to {who.Name}'s Extra Deck.");
                            break;
                        }
                        who.Deck.Add(chosenTarget.CardId);
                        engine.Rng.Shuffle(who.Deck);
                        engine.Log($"{chosenTarget.Name} is shuffled into {who.Name}'s Deck.");
                    }
                    break;

                case EffectActionKind.ModifyTargetUntilEndOfTurn:
                    // TargetedClassics Soft: Deal of Phantom — Amount × monsters in your GY (fixed at resolution).
                    if (TargetedClassicsRuntime.IsPerCountBoost(clause))
                    {
                        TargetedClassicsRuntime.ApplyPerCountBoost(engine, who, source, clause, chosenTarget);
                        break;
                    }
                    if (chosenTarget != null)
                    {
                        chosenTarget.UntilEndOfTurnAtk += clause.Amount;
                        chosenTarget.UntilEndOfTurnDef += clause.DefAmount;
                        engine.Log(
                            $"{chosenTarget.Name}: {chosenTarget.CurrentAtk}/{chosenTarget.CurrentDef} until the end of this turn.");
                    }
                    break;

                case EffectActionKind.LinkThisToTarget:
                    if (chosenTarget != null && source != null)
                    {
                        source.EquippedTo = chosenTarget;
                        if (!chosenTarget.Equips.Contains(source))
                            chosenTarget.Equips.Add(source);
                        engine.Log($"{source.Name} binds {chosenTarget.Name}.");
                    }
                    break;

                case EffectActionKind.PlaceNamedFromDeckOnTop when string.IsNullOrEmpty(clause.NamedCard):
                    // PhaseTimersAndDeckTop batch (v163): A Cat of Ill Omen / An Owl of Luck — a chosen Trap / Field Spell.
                    PhaseTimersAndDeckTopRuntime.ResolvePlaceKindOnTop(engine, who, source, clause);
                    break;

                case EffectActionKind.PlaceNamedFromDeckOnTop:
                {
                    // Drill Bug: take 1 copy out, shuffle, then put it on top.
                    var idx = -1;
                    for (var i = 0; i < who.Deck.Count; i++)
                    {
                        if (engine.Database == null || !engine.Database.TryGet(who.Deck[i], out var dd)) continue;
                        if (!string.Equals(dd.name, clause.NamedCard, StringComparison.OrdinalIgnoreCase)) continue;
                        idx = i;
                        break;
                    }

                    if (idx < 0)
                    {
                        engine.Log($"{source?.Name}: no \"{clause.NamedCard}\" in the Deck.");
                        break;
                    }

                    var topId = who.Deck[idx];
                    who.Deck.RemoveAt(idx);
                    engine.Rng.Shuffle(who.Deck);
                    who.Deck.Insert(0, topId);
                    engine.Log($"{who.Name} shuffles the Deck and places \"{clause.NamedCard}\" on top.");
                    break;
                }

                case EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects:
                    foreach (var side in new[] { who, opp })
                        foreach (var m in side.MonstersOnField().ToList())
                            if (m != null && !m.FaceUp && m.Position == BattlePosition.Defense)
                                SetFaceUpPosition(engine, m, BattlePosition.Defense, suppressFlip: true);
                    break;

                case EffectActionKind.DoubleAtkOfYourMatchingThenDestroyAtEnd:
                    foreach (var m in who.MonstersOnField().ToList())
                    {
                        if (m?.Def == null || !m.FaceUp) continue;
                        if (!ClassicEraTemplates.RaceMatches(m.Race, clause.RaceFilter)) continue;
                        m.UntilEndOfTurnAtk += Mathf.Max(0, m.CurrentAtk);
                        m.TempDestroyOnEndOfTurn = engine.TurnNumber;
                        engine.Log($"{m.Name}: ATK doubled → {m.CurrentAtk}; destroyed in the End Phase.");
                    }
                    break;

                case EffectActionKind.LockTargetCannotAttackWhileFaceUp:
                    if (chosenTarget != null && source != null && source.FaceUp &&
                        engine.ControllerOf(source) != null && engine.ControllerOf(chosenTarget) != null)
                    {
                        chosenTarget.AttackLockedBy = source;
                        engine.Log($"{chosenTarget.Name} cannot attack while {source.Name} is face-up.");
                    }
                    break;

                case EffectActionKind.ShuffleTargetAndHandIntoDeckDraw:
                {
                    if (chosenTarget == null || engine.ControllerOf(chosenTarget) != who ||
                        chosenTarget.ControlOwner != null)
                    {
                        engine.Log($"{source?.Name}: the monster is gone — no effect.");
                        break;
                    }

                    var handCards = who.Hand.Where(h => h != null && h != source).ToList();
                    if (handCards.Count == 0)
                    {
                        engine.Log($"{source?.Name}: no cards in hand — no effect.");
                        break;
                    }

                    engine.ReturnCardToDeck(chosenTarget);
                    foreach (var h in handCards)
                    {
                        who.Hand.Remove(h);
                        who.Deck.Add(h.CardId);
                    }

                    engine.Rng.Shuffle(who.Deck);
                    engine.Log($"{who.Name} shuffles {chosenTarget.Name} and {handCards.Count} hand card(s) into the Deck.");
                    engine.Draw(who, handCards.Count);
                    break;
                }

                case EffectActionKind.SpecialSummonChosenFromHandOrDeck:
                {
                    // TargetNegationsAndHandQuickEffects batch (v163): the source names the monster, so a named-card
                    // procedure (Great Dezard → Fushioh Richie) is completed by this Summon (TributeSummonsRuntime gate).
                    var namedScope = TributeSummonsRuntime.BeginNamedSummon(source);
                    try
                    {
                    if (chosenTarget == null) break;
                    if (engine.FirstEmpty(who.MonsterZones) < 0)
                    {
                        engine.Log("Special Summon failed — no zone.");
                        break;
                    }

                    if (who.Hand.Contains(chosenTarget))
                    {
                        who.Hand.Remove(chosenTarget);
                        engine.MarkSpecialSummonFromHand(chosenTarget); // Chain Energy toll (RuleLocks batch)
                        if (engine.SpecialSummonToField(who, chosenTarget, BattlePosition.Attack, true))
                            engine.Log($"Special Summoned {chosenTarget.Name} from hand.");
                        else
                            who.Hand.Add(chosenTarget);
                        break;
                    }

                    var at = who.Deck.IndexOf(chosenTarget.CardId);
                    if (at < 0)
                    {
                        engine.Log($"{chosenTarget.Name} is no longer in the Deck.");
                        break;
                    }

                    var fromDeck = engine.CreateCardInstance(chosenTarget.CardId);
                    // PerCopyDeck batch (v163): a failed Summon puts the same copy back (owner, face-up state).
                    var fromCopy = PerCopyDeckRuntime.TakeFromDeck(who, at, fromDeck);
                    if (engine.SpecialSummonToField(who, fromDeck, BattlePosition.Attack, true))
                        engine.Log($"Special Summoned {fromDeck.Name} from the Deck.");
                    else
                        who.Deck.Insert(at, fromDeck.CardId, fromCopy);
                    engine.Rng.Shuffle(who.Deck);
                    }
                    finally
                    {
                        TributeSummonsRuntime.EndNamedSummon(namedScope);
                    }

                    break;
                }

                case EffectActionKind.DestroySummonedThisTurnLevelLeq:
                    if (source == null || !source.FaceUp ||
                        engine.ControllerOf(source)?.TryFindSpellTrap(source, out _) != true)
                        break;
                    // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var side in new[] { who, opp })
                            foreach (var m in side.MonstersOnField().ToList())
                            {
                                if (m == null || !m.FaceUp || m.NormalOrFlipSummonedTurn != engine.TurnNumber) continue;
                                if (m.Level > clause.Amount) continue;
                                Destroy(m);
                            }
                    });
                    break;

                case EffectActionKind.OpponentDrawsThenDiscardsDrawnSpells:
                {
                    var before = opp.Hand.ToList();
                    engine.Draw(opp, Mathf.Max(1, clause.Amount));
                    // HandDisruption batch (v142): the effect goes on after the draw (ygopro BreakEffect), so a
                    // "when your opponent draws" card misses the timing.
                    HandDisruptionRuntime.EffectContinuesAfterDraw(engine);
                    var drawn = opp.Hand.Where(h => h != null && !before.Contains(h)).ToList();
                    engine.Log($"{opp.Name} reveals the drawn cards: " +
                               string.Join(", ", drawn.Select(h => h.Name)) + ".");
                    // DiscardTriggers (v142): the drawn Spells are discarded by this card's effect.
                    DiscardTriggersRuntime.BeginScope(engine);
                    try
                    {
                        foreach (var h in drawn.Where(h => h.Def != null && h.Def.IsSpell))
                        {
                            var gone = MonsterEffects.DiscardFromHandByInstance(opp, h);
                            engine.Log($"{h.Name} (Spell) is discarded.");
                            DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, who);
                        }
                    }
                    finally
                    {
                        DiscardTriggersRuntime.EndScope(engine);
                    }
                    break;
                }

                case EffectActionKind.DiscardJustDrawnCard:
                {
                    // Drop Off: discard THAT card from the Draw Phase normal draw — not random, not whole hand.
                    var victim = clause.Side == EffectSide.Controller ? who : opp;
                    var just = _lastNormalDrawCard;
                    if (just == null || _lastNormalDrawOwner != victim ||
                        victim?.Hand == null || !victim.Hand.Contains(just))
                    {
                        engine.Log("Drop Off: the just-drawn card is no longer in hand — discard fails.");
                        break;
                    }

                    var goneJust = MonsterEffects.DiscardFromHandByInstance(victim, just);
                    engine.Log($"{just.Name} (just drawn) is discarded.");
                    if (_lastNormalDrawCard == just)
                        _lastNormalDrawCard = null;
                    // DiscardTriggers (v142): Drop Off discards by this card's effect.
                    DiscardTriggersRuntime.NoteDiscard(engine, victim, goneJust, who);
                    break;
                }

                case EffectActionKind.RevealJustDrawnNormalAndDraw:
                {
                    // Heart of the Underdog: reveal the just-drawn Normal Monster, then draw Amount more.
                    var just = _lastNormalDrawCard;
                    if (just?.Def == null || _lastNormalDrawOwner != who ||
                        who?.Hand == null || !who.Hand.Contains(just) ||
                        !just.Def.IsNormalMonster)
                    {
                        engine.Log("Heart of the Underdog: no Normal Monster just drawn to reveal.");
                        break;
                    }

                    engine.Log($"{who.Name} reveals {just.Name} (Normal Monster).");
                    engine.Draw(who, Math.Max(1, clause.Amount), silent: false);
                    break;
                }

                case EffectActionKind.ContinuousPiercing:
                case EffectActionKind.EquippedGainsPiercing:
                case EffectActionKind.DestroyEquipsAttachedToThis:
                    // Continuous — GrantsPiercing / SweepClassicStateChecks.
                    break;

                case EffectActionKind.DestroyThisCard:
                    if (source != null && engine.ControllerOf(source) != null)
                    {
                        engine.Log($"{source.Name} destroys itself.");
                        Destroy(source);
                    }
                    break;

                case EffectActionKind.InflictDamageToLinkedCardController:
                {
                    // Mask of the Accursed (c56948373 s.damop): your Standby Phase, the equipped monster's
                    // current controller takes the damage (GetEquipTarget():GetControler()). No host, no damage.
                    var host = source?.EquippedTo;
                    var victim = host != null ? engine.ControllerOf(host) : null;
                    if (victim != null && clause.Amount > 0)
                        engine.ApplyEffectDamage(victim, clause.Amount, source?.Name);
                    break;
                }

                case EffectActionKind.AttackCostLpForAll:
                case EffectActionKind.AttackCostMillForOpponent:
                case EffectActionKind.EquipAtkByLpComparison:
                case EffectActionKind.EquippedCannotChangePosition:
                    break;

                case EffectActionKind.CoinCallWrongLoseHalfLp:
                {
                    // The call has no strategic value (a fair coin), so the controller calls Heads. The toss is a
                    // coin-toss event (CoinDiceInterrupts batch: Second Coin Toss may redo it before it applies).
                    CoinDiceInterruptsRuntime.TossCoin(engine, who, source, heads => heads, heads =>
                    {
                        using var redoScope = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                        engine.Log($"{source?.Name}: {who.Name} calls Heads — the coin shows {(heads ? "Heads" : "Tails")}.");
                        if (!heads)
                            engine.LoseLifePoints(who, (who.LifePoints + 1) / 2, source?.Name);
                    });
                    break;
                }

                case EffectActionKind.AttackCostLp:
                case EffectActionKind.GainAtkWhenAttackingMatching:
                case EffectActionKind.AttackerCannotAttackNextTurn:
                case EffectActionKind.EquipAtkDecayPerStandby:
                case EffectActionKind.EquippedCannotAttack:
                case EffectActionKind.EquippedMustBeAttackTarget:
                // EquipLocksAndLeaveField batch: Raregold / Metalsilver Armor locks (EquipLocksAndLeaveFieldRuntime).
                case EffectActionKind.OpponentCannotAttackOtherThanEquippedHost:
                case EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost:
                case EffectActionKind.WinDuelWithNamedSetInHand:
                case EffectActionKind.ContinuousForceDefenseLockPosition:
                case EffectActionKind.GainSelfAtkPerCount:
                // DestinyBoard batch: the field win and the leave-field send (DestinyBoardRuntime.OnBoardSettled).
                case EffectActionKind.WinWithThisAndNamedSetFaceUpOnField:
                case EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField:
                    // Rule / continuous — checked by the engine and FieldSpellEffects.
                    break;

                // DestinyBoard batch: Destiny Board's End Phase placement of the next Spirit Message.
                case EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck:
                    DestinyBoardRuntime.OnPlacementTrigger(engine, who, source, clause);
                    break;

                case EffectActionKind.ChangeBattlePosition:
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        var wasFaceDown = !chosenTarget.FaceUp;
                        if (clause.ForceAttackPosition)
                            chosenTarget.Position = BattlePosition.Attack;
                        else if (clause.ForceDefensePosition)
                            chosenTarget.Position = BattlePosition.Defense;
                        else
                            chosenTarget.Position = chosenTarget.Position == BattlePosition.Attack
                                ? BattlePosition.Defense
                                : BattlePosition.Attack;
                        chosenTarget.FaceUp = true;
                        // SpiritMonsters batch: a "flipped face-up" trigger (Otohime, Maharaghi) after this resolves.
                        if (wasFaceDown)
                            SpiritMonstersRuntime.NoteFlippedFaceUpByEffect(engine, chosenTarget);
                        engine.Log($"{chosenTarget.Name} → {chosenTarget.Position} Position.");
                        NotifyPositionChanged(engine, chosenTarget,
                            toDefense: chosenTarget.Position == BattlePosition.Defense);
                    }
                    else if (clause.Side == EffectSide.Both || clause.ForceDefensePosition ||
                             // Windstorm of Etaqua: every face-up monster the opponent controls, no target.
                             (clause.Zone == EffectZoneFilter.OppFaceUpMonsters && !clause.RequiresTargetChoice))
                    {
                        foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                        {
                            if (m == null) continue;
                            if (!m.FaceUp && !clause.AlsoFlipFaceDown) continue;
                            if (clause.ForceDefensePosition)
                                engine.ApplyEffectPositionChange(m, BattlePosition.Defense, true);
                            else
                                engine.ApplyEffectPositionChange(m,
                                    m.Position == BattlePosition.Attack
                                        ? BattlePosition.Defense
                                        : BattlePosition.Attack,
                                    true);
                        }
                    }

                    break;

                case EffectActionKind.SetTargetFaceDownDefense:
                    if (chosenTarget != null && engine.ControllerOf(chosenTarget) != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        // Light of Intervention (LocksAndZones batch): monsters cannot be flipped face-down.
                        if (LocksAndZonesRuntime.CannotTurnFaceDown(engine, chosenTarget))
                        {
                            engine.Log($"{chosenTarget.Name} cannot be changed to face-down Defense Position.");
                            break;
                        }
                        chosenTarget.FaceUp = false;
                        chosenTarget.Position = BattlePosition.Defense;
                        ClearFaceUpMarks(chosenTarget);
                        engine.Log($"{chosenTarget.Name} is Set in face-down Defense Position.");
                    }

                    break;

                case EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown:
                    // Acid Trap Hole (FlipDefCheck batch).
                    FlipDefCheckRuntime.Apply(engine, source, clause, chosenTarget, Destroy);
                    break;

                case EffectActionKind.SetAllOppMonstersFaceDownDefense:
                    // Swords of Concealing Light: resolve-only mass opp → face-down Defense.
                    // Soft residual OK: new summons after activation Soft residual no auto-flip.
                    if (opp != null)
                    {
                        foreach (var m in opp.MonstersOnField().ToList())
                        {
                            if (m == null) continue;
                            // Light of Intervention (LocksAndZones batch): only monsters that can be turned
                            // face-down are changed (ygopro Card.IsCanTurnSet).
                            if (LocksAndZonesRuntime.CannotTurnFaceDown(engine, m)) continue;
                            m.FaceUp = false;
                            m.Position = BattlePosition.Defense;
                            ClearFaceUpMarks(m);
                        }
                        engine.Log(
                            $"{source?.Name}: change all monsters opponent controls to face-down Defense Position.");
                    }
                    // Soft ContinuousTurnsRemaining Standby destroy ticker (controller's Nth Standby).
                    SeedSwordsCountdown(source, clause);
                    break;

                case EffectActionKind.OpponentCannotChangeBattlePositions:
                    // ContinuousWhileFaceUp — Soft ContinuousProtections + Soft DE CanChangePosition.
                    break;

                case EffectActionKind.BanishRandomFromOppHandFaceDown:
                    // Lightforce Sword: Soft random Soft FD Soft Banish + Soft Standby return seed.
                    BanishRandomFromOppHandFaceDown(engine, opp, source, clause);
                    break;

                case EffectActionKind.DestroyThisInsteadOfControllerTraps:
                    // Fake Trap: Soft look-confirm Soft-OK + Soft destroy-replacement redirect.
                    // Prefer NO DE rewrite. Soft residual Soft ContinuousProtections.
                    {
                        var answered = engine.Chain?.Last;
                        ContinuousProtections.RegisterFakeTrapSubstitute(engine, who, source, answered);
                        if (source != null && engine.ControllerOf(source) != null)
                        {
                            engine.Log($"{source.Name} is destroyed instead (Soft Fake Trap substitute).");
                            Destroy(source);
                        }
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
                        engine.MarkSpecialSummonFromHand(source); // Chain Energy toll (RuleLocks batch)
                        // A summon procedure that does not use the chain (The Fiend Megacyber) can be negated
                        // like any Special Summon by procedure (SummonNegation batch, v141).
                        if (!clause.MakesChainLink)
                            engine.BeginInherentSpecialSummonFromHand(source);
                        if (engine.SpecialSummonToField(who, source, BattlePosition.Attack, true))
                        {
                            // Not logged when a summon-negation answer already destroyed it.
                            if (who.TryFindMonster(source, out _))
                                engine.Log($"Special Summoned {source.Name}!");
                        }
                        else
                        {
                            who.Hand.Add(source);
                            engine.Log("Special Summon failed — no Monster Zone.");
                        }
                    }

                    break;

                // DiscardTriggersSelfSs batch (v173): Fear from the Dark / Despair from the Dark. The clause fires from
                // DiscardTriggersRuntime.Fire() with `source` = the card that was just discarded/sent to the GY by the
                // opponent's card effect; the trigger window is after the moving effect resolved (or after the chain),
                // so `source` is in the GY. Same Semi-Nomi / Nomi / GY-lock gates as a targeted GY Special Summon.
                case EffectActionKind.SpecialSummonThisFromGy:
                    if (who.Graveyard == null || !who.Graveyard.Contains(source)) break;
                    if (!TextEffectRuntime.CanSpecialSummonFromGyTarget(source))
                    {
                        engine.Log($"{source.Name} cannot be Special Summoned from the GY.");
                        break;
                    }
                    if (ContinuousProtections.GyMovementLocked(engine, source, source))
                    {
                        engine.Log("Necrovalley prevents this card from leaving the GY.");
                        break;
                    }
                    who.Graveyard.Remove(source);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (!engine.SpecialSummonToField(who, source, BattlePosition.Attack, true))
                    {
                        // Restore to the GY it came from.
                        source.ControlOwner = null;
                        who.Graveyard.Add(source);
                        engine.Log("Special Summon failed — no Monster Zone.");
                    }
                    else
                    {
                        engine.Log($"Special Summoned {source.Name} from the GY!");
                    }

                    break;

                case EffectActionKind.SkipOpponentNextDrawPhase:
                    // Yata-Garasu (c3078576): its EFFECT_SKIP_DP resets at the End Phase of the opponent's turn,
                    // so battle damage inflicted during the opponent's own turn skips nothing (SpiritsAndMisc).
                    if (SpiritsAndMiscRuntime.BattleDamageSkipLapses(engine, clause, opp))
                        break;
                    opp.SkipNextDrawPhase = true;
                    engine.Log($"{opp.Name} skips their next Draw Phase.");
                    break;

                // SpiritsAndMisc batch (Yamata Dragon / Hino-Kagu-Tsuchi / Kiseitai).
                case EffectActionKind.DrawUntilHandSize:
                    SpiritsAndMiscRuntime.DrawUntilHandSize(engine, clause.Side == EffectSide.Opponent ? opp : who,
                        clause, source);
                    break;
                case EffectActionKind.DiscardOpponentHandAtNextDrawPhase:
                    SpiritsAndMiscRuntime.ScheduleHandDiscardAtNextDrawPhase(engine, opp, source);
                    break;
                // SpiritMonsters batch: Great Long Nose / Maharaghi register a later effect; Asura Priest, Inaba White
                // Rabbit and Susa Soldier are continuous markers read by DuelEngine (SpiritMonstersRuntime).
                case EffectActionKind.OpponentSkipsNextBattlePhase:
                    SpiritMonstersRuntime.ScheduleBattlePhaseSkip(engine, opp, source);
                    break;
                case EffectActionKind.LookAtTopCardAtNextDrawPhaseTopOrBottom:
                    SpiritMonstersRuntime.ScheduleTopCardLook(engine, who, source);
                    break;
                // BattleRules batch: After the Struggle and The Secret of the Bandit register for the turn; Cave Dragon,
                // Thunder Nyan Nyan and Patrician of Darkness are rule clauses read elsewhere (BattleRulesRuntime).
                case EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase:
                    BattleRulesRuntime.ArmAfterTheStruggle(engine, who, source);
                    break;
                case EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn:
                    BattleRulesRuntime.ArmBandit(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster:
                // ArchfiendRiders batch: rules read by ArchfiendRidersRuntime (summon lock, Standby waiver, the search
                // marker, Pandemonium Watchbear's protection); never resolved as an effect.
                case EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries:
                case EffectActionKind.WaiveSeriesMonsterStandbyLpCosts:
                case EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle:
                case EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects:
                case EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace:
                case EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute:
                case EffectActionKind.YouChooseOpponentsAttackTargets:
                    break;
                // NamedSummonPairs batch: Exodia Necross / Mirage Knight rule clauses, read where they apply
                // (DuelEngine.DestroyMonster, FieldSpellEffects.RefreshBoard, the damage calculation).
                case EffectActionKind.CannotBeDestroyedBySpellTrapEffects:
                case EffectActionKind.SelfDestroyUnlessAllNamedInYourGy:
                case EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation:
                    break;
                // NamedSummonPairs batch: Mirage Knight's End Phase banish.
                case EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn:
                    NamedSummonPairsRuntime.ApplyBanishIfBattled(engine, source);
                    break;
                // CoinDiceInterrupts batch: Second Coin Toss is a marker read on each coin toss (never resolved); Dice
                // Re-Roll gives its controller one re-roll this turn.
                case EffectActionKind.RedoOwnCoinTossOncePerTurn:
                    break;
                case EffectActionKind.GrantDiceReRollThisTurn:
                    CoinDiceInterruptsRuntime.GrantDiceReRoll(engine, who, source);
                    break;
                case EffectActionKind.AttackAllOpponentMonstersOnceEach:
                case EffectActionKind.CannotSelectMonsterAsAttackTarget:
                case EffectActionKind.HalveBattleDamageThisInflictsToOpponent:
                // BattleDamageEvents batch: Piranha Army / Stone Statue of the Aztecs — a marker read by DuelEngine's damage
                // calculation (BattleDamageEventsRuntime.DoubleBattleDamage).
                case EffectActionKind.DoubleBattleDamageToOpponent:
                    break;
                // ContinuousTrapsAndSpiritSupport batch: Life Absorbing Machine / Ominous Fortunetelling (Standby
                // triggers), Fengsheng Mirror (Activate). Fatal Abacus, Spirit's Invitation and Settle Machine's markers
                // are read by ContinuousTrapsAndSpiritSupportRuntime; its End Phase upkeep runs in FirePhaseTriggersFor.
                case EffectActionKind.GainHalfLpCostsPaidLastTurn:
                    ContinuousTrapsAndSpiritSupportRuntime.GainHalfLpCostsPaidLastTurn(engine, who, source);
                    break;
                // StandbyBurnsAndDice batch (v162): Standby die rolls, burns, Mask of Dispel's link, Inspection.
                case EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel:
                    StandbyBurnsAndDiceRuntime.ResolveBlindDestruction(engine, who, source);
                    break;
                case EffectActionKind.RollDieDestroyOppMonsterInRolledZone:
                    StandbyBurnsAndDiceRuntime.ResolveNeedleWall(engine, who, source);
                    break;
                case EffectActionKind.InflictDoublingDamageToBothPlayers:
                    StandbyBurnsAndDiceRuntime.ResolveDarkSnake(engine, who, source, clause);
                    break;
                case EffectActionKind.LinkThisToTargetSpell:
                    StandbyBurnsAndDiceRuntime.LinkMask(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.InflictDamageToLinkedSpellController:
                    StandbyBurnsAndDiceRuntime.ResolveMaskBurn(engine, who, source, clause);
                    break;
                case EffectActionKind.DestroyThisWhenLinkedSpellLeavesField:
                    break; // marker: StandbyBurnsAndDiceRuntime.OnBoardSettled applies it
                case EffectActionKind.InflictDamageToOpponentWhileBoundMonsterOnField:
                    StandbyBurnsAndDiceRuntime.ResolveWheelBurn(engine, who, source, clause);
                    break;
                case EffectActionKind.PayLpLookAtRandomOppHandCard:
                    StandbyBurnsAndDiceRuntime.ResolveInspection(engine, who, source, clause);
                    break;
                case EffectActionKind.CallTypeOfRandomOpponentHandCardBurn:
                    ContinuousTrapsAndSpiritSupportRuntime.ResolveFortunetelling(engine, who, source, clause);
                    break;
                case EffectActionKind.LookAtOpponentHandDiscardChosenSpirit:
                    ContinuousTrapsAndSpiritSupportRuntime.ResolveFengsheng(engine, who, source);
                    break;
                case EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy:
                case EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand:
                case EffectActionKind.SpiritsDoNotReturnWhileThisFaceUp:
                case EffectActionKind.EndPhaseDiscardOrDestroyThis:
                case EffectActionKind.ReturnFaceUpSpiritsWhenThisLeavesField:
                    break;
                case EffectActionKind.BecomeEquipOnFaceDownAttacker:
                    // Resolved from the Damage Step hook (TryBecomeEquipOnFaceDownAttacker), never as a clause.
                    break;
                case EffectActionKind.GainLpHalfEquippedMonsterAtk:
                    SpiritsAndMiscRuntime.GainLpHalfEquippedMonsterAtk(engine, who, source);
                    break;

                case EffectActionKind.SkipControllerNextDrawPhases:
                {
                    // Amount = printed N (Offerings 1, Reckless Greed 2). Fail-closed: never
                    // route to opponent skip. Stacks via count (surgical; keeps Fenrir bool).
                    var n = clause.Amount > 0 ? clause.Amount : 1;
                    who.SkipNextDrawPhaseCount += n;
                    engine.Log($"{who.Name} skips their next {n} Draw Phase(s) (count={who.SkipNextDrawPhaseCount}).");
                    break;
                }

                case EffectActionKind.InflictDamageEqualToAtk:
                {
                    // Magic Cylinder: "and if you do" — only after NegateThisAttack in this resolve.
                    // Fail-closed: bare InflictDamageEqualToAtk without prior negate must not fire.
                    if (!attackNegated) break;
                    if (chosenTarget != null)
                    {
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        engine.ApplyEffectDamage(opp, chosenTarget.CurrentAtk, source?.Name ?? "Effect");
                    }
                    break;
                }

                // SimpleTargetUtility batch (v173) — Altar for Tribute: the cost monster already left for
                // the GY when this runs (chain resolution follows payment). costNumeric = the printed
                // original ATK captured by PayOneIgnitionCostCore / PayAutoIgnitionCostsCore; fail closed
                // when the payment never recorded one.
                case EffectActionKind.GainLpEqualToCostAmount:
                    if (costNumeric > 0)
                        ApplyLifePointGain(engine, who, costNumeric, source?.Name);
                    else
                        engine.Log("No LP gain: no recorded original ATK for the sent monster.");
                    break;
                // SimpleTargetUtility batch (v173) — Cost Down: stamp the hand and every hand monster
                // for the rest of this turn (Applied each refresh through ApplyLevelStamps).
                case EffectActionKind.ReduceHandLevelsUntilEndOfTurn:
                    SimpleTargetUtilityRuntime.ApplyHandLevelStamp(engine, who, clause);
                    break;
                // SummonLocksAndStatRules batch — Amazoness Spellcaster: the first target's step remembers it, the
                // second target's step switches both original ATK. Rivalry of Warlords is a RefreshBoard rule.
                case EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn:
                    SummonLocksAndStatRulesRuntime.ApplySwitchStep(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.EachPlayerKeepsOneMonsterType:
                    break;
                // SimpleTriggersAndSwarm batch.
                case EffectActionKind.ShuffleThisFromGyIntoDeck:
                    SimpleTriggersAndSwarmRuntime.ShuffleThisFromGyIntoDeck(engine, who, source);
                    break;
                case EffectActionKind.SpecialSummonAnyNumberFromHand:
                    SimpleTriggersAndSwarmRuntime.StartAnyNumberSummon(engine, who, source, clause);
                    break;
                case EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn:
                    SimpleTriggersAndSwarmRuntime.ApplyCombination(engine, who, source, clause);
                    break;
                // DerivedAtkMonsters batch (v176): the Garzetts' ATK is set at the Tribute Summon (DuelEngine →
                // DerivedAtkMonstersRuntime.NoteTributeSummon); Guardian Tryce revives its Tributes.
                case EffectActionKind.ThisAtkBecomesTributedOriginalAtk:
                    break;
                case EffectActionKind.SpecialSummonTributeMaterialsFromGy:
                    DerivedAtkMonstersRuntime.SpecialSummonTributeMaterials(engine, who, source);
                    break;
                // SpellTrapAnswersAndActivationLocks batch: Sasuke Samurai #2's turn-long Spell/Trap lock; Invader of Darkness's
                // Quick-Play lock is read by every activation gate (ContinuousNegationRuntime.ActivationLocked).
                case EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn:
                    SpellTrapAnswersAndActivationLocksRuntime.RegisterTurnSpellTrapLock(engine, who, source);
                    break;
                case EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells:
                    break;

                case EffectActionKind.GainLpEqualToAtk:
                {
                    // Draining Shield: "and if you do" — only after NegateThisAttack in this resolve.
                    // Detected via SourceSnippet (Take sets full compound match containing
                    // "and if you do"). Bare GainLpEqualToAtk (Enchanted Javelin) has no
                    // "and if you do" → still fires without prior negate.
                    // Fail-closed: Shield gain rider must not fire if negate did not apply.
                    if (!string.IsNullOrEmpty(clause.SourceSnippet) &&
                        clause.SourceSnippet.IndexOf("and if you do", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !attackNegated)
                        break;
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
                }

                case EffectActionKind.LoseAtkDefUntilEndOfTurn:
                {
                    var pay = clause.Amount;
                    if (clause.RequiresLpCostMultiple > 0 && engine.PendingActivation != null &&
                        engine.PendingActivation.AwaitingLpCost)
                        pay = 0; // paid in FinishPayLp
                    if (pay <= 0 && clause.Amount == 0 && (clause.DefAmount > 0 || clause.SetDefToZero) &&
                        chosenTarget != null)
                    {
                        // Snake Fang: DEF only, ATK untouched. Micro Ray: DEF becomes 0.
                        if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            break;
                        }
                        var defLoss = clause.SetDefToZero
                            ? Mathf.Max(0, chosenTarget.CurrentDef)
                            : clause.DefAmount;
                        chosenTarget.UntilEndOfTurnDef -= defLoss;
                        engine.Log(
                            $"{chosenTarget.Name} loses {defLoss} DEF until the End Phase " +
                            $"(now {chosenTarget.CurrentAtk}/{chosenTarget.CurrentDef}).");
                        break;
                    }
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
                        var scale = clause.ScaleAmountByOpponentHandCards ? (opp?.HandCount ?? 0) : 1;
                        var atkGain = clause.Amount * scale;
                        var defGain = clause.DefAmount * scale;
                        if (atkGain != 0)
                            t.UntilEndOfTurnAtk += atkGain;
                        if (defGain != 0)
                            t.UntilEndOfTurnDef += defGain;
                        engine.Log(
                            $"{t.Name} gains {atkGain} ATK/{defGain} DEF until the End Phase " +
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
                        if (clause.CountSource == PerCountBurnsRuntime.PreviousClauseHits)
                        {
                            // ConditionalSpellsAndTraps batch: Gryphon's Feather Duster — N x the cards the previous
                            // Destroy clause actually destroyed (Duel.Destroy's return); none destroyed: no gain.
                            if (clause.RequiresPreviousClauseHit && s_lastDestroyHits <= 0) return;
                            gained = Mathf.Max(0, clause.Amount) * Mathf.Max(0, s_lastDestroyHits);
                        }
                        else if (clause.AmountIsEquippedBattleDamage)
                            gained = s_battleDamageJustInflicted;
                        else if (clause.ScaleAmountByFieldMonsters || clause.ScaleAmountByAllFieldMonsters)
                            gained = Mathf.Max(0, clause.Amount) *
                                     (who.MonsterCount + (opp != null ? opp.MonsterCount : 0));
                        else if (clause.ScaleAmountByControllerMonsters)
                            gained = Mathf.Max(0, clause.Amount) * who.MonsterCount;
                        else if (clause.ScaleAmountByFaceUpEffectMonsters)
                            // FlipMassAndOpponentPicks batch: The Spell Absorbing Life — per face-up Effect Monster on
                            // both fields, counted after its flips resolve (as Ceasefire's damage).
                            gained = Mathf.Max(0, clause.Amount) *
                                     who.MonstersOnField().Concat(opp != null ? opp.MonstersOnField() : Enumerable.Empty<CardInstance>())
                                         .Count(m => m != null && m.FaceUp && !m.IsToken && IsTrueEffectMonster(m.Def));
                        else
                            gained = AmountFromTarget(clause, chosenTarget, gained);
                        ApplyLifePointGain(engine, gainWho, gained, source?.Name);
                    }

                    if (clause.Side == EffectSide.Both)
                    {
                        ApplyGain(who);
                        ApplyGain(opp);
                    }
                    else
                    {
                        ApplyGain(clause.Side == EffectSide.Opponent || clause.OpponentIsSubject ? opp : who);
                    }
                    break;
                }

                case EffectActionKind.PayLifePoints:
                {
                    var payer = clause.Side == EffectSide.Opponent ? opp : who;
                    var paid = Mathf.Max(0, clause.Amount);
                    if (payer != null)
                    {
                        engine.PayLifePoints(payer, paid,
                            $"{payer.Name} pays {paid} LP ({Mathf.Max(0, payer.LifePoints - paid)}).");
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
                case EffectActionKind.NeitherPlayerCanTribute:
                case EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase:
                case EffectActionKind.NoHandSizeLimit:
                case EffectActionKind.OpponentHandSizeLimitBecomes:
                case EffectActionKind.NegateAllEquipCardEffectsOnField:
                case EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases:
                case EffectActionKind.ConvertOpponentLpGainToDamage:
                // ConditionalSpellsAndTraps batch: D.D. Borderline (ConditionalSpellsAndTrapsRuntime.BattlePhaseLocked).
                case EffectActionKind.NoBattlePhaseWhileNoSpellInControllerGy:
                    // Continuous — applied by FieldSpellEffects / ContinuousProtections while face-up.
                    break;

                case EffectActionKind.ApplyLingeringAtkDef:
                {
                    var t = chosenTarget ?? source;
                    if (clause.AppliesToEquippedHost)
                        t = source != null ? source.EquippedTo : null;
                    if (t != null && !string.IsNullOrEmpty(clause.ExcludeRaceFilter) &&
                        t.Def != null && t.Race != null &&
                        t.Race.IndexOf(clause.ExcludeRaceFilter,
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                        t = null;
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
                    // DiscardTriggers (v142): cards milled by this card's effect (Penguin Knight).
                    DiscardTriggersRuntime.BeginScope(engine);
                    try
                    {
                        SendTopOfDeckToGy(engine, millWho, Mathf.Max(1, clause.Amount), byEffectOf: who);
                    }
                    finally
                    {
                        DiscardTriggersRuntime.EndScope(engine);
                    }
                    break;
                }

                // BattleOutcomeTriggers batch (v162): Zombyra the Dark, Timeater, Kryuel, Dark Scorpion Burglars, Mucus Yolk.
                case EffectActionKind.ChangeThisCardAtkWhileFaceUp:
                    BattleOutcomeTriggersRuntime.ApplyThisCardAtkChange(engine, who, source, clause);
                    break;
                case EffectActionKind.OpponentSkipsNextMainPhase1:
                    BattleOutcomeTriggersRuntime.ScheduleMainPhase1Skip(engine, opp, source);
                    break;
                case EffectActionKind.CoinCallRightDestroyOneOppMonster:
                    BattleOutcomeTriggersRuntime.StartKryuelCall(engine, who, source);
                    break;
                case EffectActionKind.OpponentSendsSpellFromDeckToGy:
                    // DcrSpellTrapUtility batch: Arsenal Robber — the same send, Equip Spells only (Zone DeckEquipSpells).
                    if (clause.Zone == EffectZoneFilter.DeckEquipSpells)
                        DcrSpellTrapUtilityRuntime.StartOpponentEquipSend(engine, who, source);
                    else
                        BattleOutcomeTriggersRuntime.StartOpponentSpellSend(engine, who, source);
                    break;
                // DcrSpellTrapUtility batch: Interdimensional Matter Transporter, Different Dimension Gate, Final Countdown,
                // Dragged Down into the Grave.
                case EffectActionKind.BanishTargetUntilEndPhase:
                    DcrSpellTrapUtilityRuntime.BanishUntilEndPhase(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy:
                    DcrSpellTrapUtilityRuntime.BanishLinkedToGate(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.WinAfterTurnCountdown:
                    DcrSpellTrapUtilityRuntime.StartCountdown(engine, who, source, clause);
                    break;
                case EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw:
                    DcrSpellTrapUtilityRuntime.ResolveDraggedDown(engine, who, source);
                    break;
                // FlipMassAndOpponentPicks batch: Dark Cat with White Tail, Huge Revolution, Magical Merchant, Pixie
                // Knight, Pineapple Blast, Dimension Jar.
                case EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands:
                    FlipMassAndOpponentPicksRuntime.ApplyReturnTargets(engine, who, source, clause);
                    break;
                case EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards:
                    FlipMassAndOpponentPicksRuntime.ApplyRevolution(engine, who, source);
                    break;
                case EffectActionKind.ExcavateUntilSpellTrapAddItSendRestToGy:
                    FlipMassAndOpponentPicksRuntime.ApplyExcavateUntilSpellTrap(engine, who, source);
                    break;
                case EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck:
                    FlipMassAndOpponentPicksRuntime.ApplyOpponentPlacesSpellOnTop(engine, who, source);
                    break;
                case EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses:
                    FlipMassAndOpponentPicksRuntime.ApplyMatchMonsterCounts(engine, who, source);
                    break;
                case EffectActionKind.EachPlayerMayBanishUpToMonstersFromOpponentGy:
                    FlipMassAndOpponentPicksRuntime.ApplyEachPlayerBanishes(engine, who, source, clause);
                    break;
                case EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck:
                    MfcLabCards3Runtime.MillCardOfDeclaredTypeFromOpponentDeck(engine, who, source, clause);
                    break;
                case EffectActionKind.GainThisCardAtkAtNextStandbyWhileFaceUp:
                    BattleOutcomeTriggersRuntime.FlagNextStandbyGain(engine, who, source, clause);
                    break;

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
                        var each = Mathf.Max(0, clause.Amount) * n;
                        // Token Thanksgiving gains LP. Token Feastevil leaves TokenDestroyGainsLp false and burns.
                        if (clause.TokenDestroyGainsLp)
                            ApplyLifePointGain(engine, who, each, source?.Name);
                        else
                            engine.ApplyEffectDamage(opp, each, source?.Name);
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
                                engine.ReturnCardToHand(m, by: who);
                    }

                    foreach (var m in opp.MonstersOnField().ToList())
                    {
                        if (m?.Def != null && m.FaceUp &&
                            m.Def.type != null &&
                            m.Def.type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0)
                            if (!ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                engine.ReturnCardToHand(m, by: who);
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
                    engine.BanishCard(owner, t, by: who);
                    engine.BanishCopiesFromHandAndDeck(opp, id);
                    break;
                }

                case EffectActionKind.DestroySameNameInControllerHandAndDeck:
                {
                    var t = chosenTarget;
                    if (t == null) break;
                    var owner = engine.ControllerOf(t);
                    if (owner != null)
                    {
                        // DiscardTriggers (v142): the copies destroyed in the Deck are sent directly from the Deck
                        // to the GY by this card's effect (Penguin Knight). Hand copies are destroyed, not discarded.
                        var handBefore = new HashSet<CardInstance>(owner.Hand.Where(h => h != null));
                        var gyBefore = new HashSet<CardInstance>(owner.Graveyard.Where(g => g != null));
                        DiscardTriggersRuntime.BeginScope(engine);
                        try
                        {
                            engine.DestroyCopiesFromHandAndDeck(owner, t.CardId);
                            foreach (var g in owner.Graveyard.ToList())
                                if (g != null && !gyBefore.Contains(g) && !handBefore.Contains(g))
                                    DiscardTriggersRuntime.NoteSentFromDeck(engine, owner, g, who);
                        }
                        finally
                        {
                            DiscardTriggersRuntime.EndScope(engine);
                        }
                    }
                    break;
                }

                case EffectActionKind.DestroyOppAttackThenDamage:
                {
                    var hits = CollectAllMatching(engine, who, clause).ToList();
                    if (hits.Count == 0) break;
                    // Destroyed together; their GY triggers activate after the damage (FlipMassEffects v142).
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var m in hits)
                            Destroy(m);
                        engine.ApplyEffectDamage(opp, Mathf.Max(0, clause.Amount), source?.Name);
                    });
                    break;
                }

                case EffectActionKind.DestroyAllEquips:
                {
                    // Lua s.filter IsType(TYPE_EQUIP): Equip Spells, and every card acting as an Equip Card — a
                    // Union, Kiseitai, Dark Necrofear, a monster Sword Hunter equipped (BothSideAndMonsterEquip, v161).
                    var equips = new List<CardInstance>();
                    foreach (var st in who.SpellTrapsOnField())
                        if (st?.Def != null && (st.Def.IsEquipSpell || EquipModelRuntime.IsEquipCardShape(st))) equips.Add(st);
                    foreach (var st in opp.SpellTrapsOnField())
                        if (st?.Def != null && (st.Def.IsEquipSpell || EquipModelRuntime.IsEquipCardShape(st))) equips.Add(st);
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
                        if (owner != null) engine.BanishCard(owner, chosenTarget, by: who);
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
                            if (owner != null) engine.BanishCard(owner, m, by: who);
                        }
                    }

                    if (clause.AlsoBanishThis && source != null)
                    {
                        var selfOwner = OwnerOf(engine, who, source) ?? who;
                        if (selfOwner != null)
                            engine.BanishCard(selfOwner, source, by: who);
                    }

                    break;

                case EffectActionKind.ApplyWabokuStyle:
                    who.WabokuActive = true;
                    engine.Log(
                        $"{who.Name}: no battle damage this turn; monsters cannot be destroyed by battle.");
                    break;

                case EffectActionKind.NoDamageThisTurn:
                    // Hallowed Life Barrier: "any damage you take from your opponent's cards and effects becomes 0"
                    // this turn. The same per-duelist flag Crush Card Virus sets, read by battle and effect damage.
                    if (who != null)
                    {
                        who.NoDamageThroughTurnNumber = engine.TurnNumber;
                        engine.Log($"{who.Name}: any damage this turn becomes 0.");
                    }
                    break;

                case EffectActionKind.PreventOpponentAttacksThisTurn:
                    if (opp != null)
                    {
                        opp.CannotDeclareAttackThisTurn = true;
                        engine.Log($"{opp.Name} cannot declare an attack this turn.");
                    }
                    break;

                case EffectActionKind.CannotAttackFaceDownMonstersThisTurn:
                    // A Feint Plan: both players (neither player) this turn.
                    if (who != null)
                        who.CannotAttackFaceDownMonstersThisTurn = true;
                    if (opp != null)
                        opp.CannotAttackFaceDownMonstersThisTurn = true;
                    engine.Log("Neither player can attack face-down monsters this turn.");
                    break;

                case EffectActionKind.ReturnControlToOwners:
                    // Remove Brainwashing ContinuousWhileFaceUp / activate resolve.
                    ApplyReturnControlToOwnersMass(engine, startsApplying: true);
                    break;

                case EffectActionKind.ApplySwordsOfRevealingLight:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp)
                        {
                            m.FaceUp = true;
                            m.Position = BattlePosition.Defense;
                            // ygopro c72302403: Duel.ChangePosition with no NO_FLIP_EFFECT — a monster flipped
                            // face-up by Swords activates its FLIP effect (Weather Report), after Swords (and its
                            // chain) has resolved, never in the middle of it (SpellTrapDestroyedResponse, v142).
                            engine.QueueFlipEffectAfterResolution(opp, m);
                        }
                    }

                    SeedSwordsCountdown(source, clause);
                    engine.Log(
                        "Swords of Revealing Light: flip opponent face-down monsters; " +
                        "opponent cannot declare attacks; lasts opponent's turns.");
                    break;

                case EffectActionKind.BothPlayersDiscardAndRedraw:
                {
                    // Amount 0 = Card Destruction (redraw = discarded count).
                    // Amount > 0 = Morphing Jar fixed draw (each draws Amount even if hand was empty).
                    var nYou = who.Hand.Count;
                    var nOpp = opp.Hand.Count;
                    // DiscardTriggers (v142): both hands are discarded by this card's effect; the opponent's
                    // discard triggers (Electric Snake / Minar) activate after the redraw.
                    var oppDiscarded = opp.Hand.Where(h => h != null).ToList();
                    DiscardTriggersRuntime.BeginScope(engine);
                    try
                    {
                        DiscardAll(who);
                        DiscardAll(opp);
                        DiscardTriggersRuntime.NoteDiscards(engine, opp, oppDiscarded, who);
                        var drawYou = clause.Amount > 0 ? clause.Amount : nYou;
                        var drawOpp = clause.Amount > 0 ? clause.Amount : nOpp;
                        if (drawYou > 0) engine.Draw(who, drawYou, silent: false);
                        if (drawOpp > 0) engine.Draw(opp, drawOpp, silent: true);
                        if (clause.Amount > 0)
                            engine.Log($"Morphing Jar: discard-all then each draws {clause.Amount}.");
                        else
                            engine.Log($"Card Destruction: redraw {nYou} / {nOpp}.");
                    }
                    finally
                    {
                        DiscardTriggersRuntime.EndScope(engine);
                    }
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
                    // "destroy that face-up monster, and if you do, take damage ..." (ygopro: tc:IsFaceup() and
                    // Duel.Destroy(tc)~=0 gate the damage): a target that left the field, was turned face-down or
                    // survived the destruction (Exodia Necross vs a Trap, an "unaffected" monster) deals no damage.
                    var ringHolder = engine.ControllerOf(chosenTarget);
                    if (ringHolder == null || !ringHolder.TryFindMonster(chosenTarget, out _) || !chosenTarget.FaceUp)
                    {
                        engine.Log($"{source.Name}: the target is no longer a face-up monster on the field.");
                        break;
                    }
                    engine.Log($"Destroys {chosenTarget.Name}!");
                    Destroy(chosenTarget);
                    if (ringHolder.TryFindMonster(chosenTarget, out _))
                    {
                        engine.Log($"{source.Name}: {chosenTarget.Name} was not destroyed, so no damage.");
                        break;
                    }
                    // Both players take the damage at the same time: both at 0 LP is a Draw (DR-06).
                    engine.ApplyEffectDamageBoth(who, originalAtk, opp, originalAtk, source.Name);
                    break;

                case EffectActionKind.DiscardSelfNoBattleDamageThisBattle:
                    // Kuriboh-style: cost + operation (also hard-coded in MonsterEffects for DC window)
                    if (MonsterEffects.TryActivateHandDamageCalculation(engine, who, source))
                        return; // ContinueAfterResponse already called
                    break;

                case EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo:
                    // Earthshaker (TypeDeclaration batch, v142): the players' selections, then the destruction.
                    TypeDeclarationRuntime.ResolveEarthshaker(engine, who, source);
                    break;

                case EffectActionKind.AllFaceUpMonstersBecomeDeclaredType:
                case EffectActionKind.StandbyTributeOrDestroyThis:
                    // TypeDeclaration batch (v142): the Type change is read live (CardInstance.Race); the upkeep
                    // runs from the Standby Phase (TypeDeclarationRuntime.RunStandbyUpkeep). Nothing to apply here.
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

                // Ninjitsu Art of Decoy: bind the Continuous Trap to its target. The bound host's battle immunity
                // is read through OfficialEffectRegistry.CannotBeDestroyedByBattle.
                case EffectActionKind.ContinuousCannotBeDestroyedByBattle:
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
                        engine.Log($"{source.Name} binds {chosenTarget.Name} (cannot be destroyed by battle).");
                    }
                    break;

                case EffectActionKind.OpponentAttacksOnlySelectedMonsterThisTurn:
                    // Taunt (BattleRules batch): the opponent may attack only this monster for the rest of the turn.
                    BattleRulesRuntime.ArmOpponentAttackLock(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.NegateEffectsOfDefensePositionMonstersThisTurn:
                    // Gorgon's Eye (ContinuousNegation batch): a turn-scoped field rule, not a one-shot.
                    ContinuousNegationRuntime.ArmGorgonEyes(engine, who, source);
                    break;

                case EffectActionKind.DiscardRandomFromOpponentHand:
                    DiscardRandomFromOpponentHand(engine, opp, source, clause);
                    break;

                // SanctuaryAndZera batch: The Agent of Judgment - Saturn; Light of Judgment.
                case EffectActionKind.InflictLpLeadAsDamageToOpponent:
                    SanctuaryAndZeraRuntime.ApplyLpLeadDamage(engine, who, source, clause);
                    break;
                case EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand:
                    SanctuaryAndZeraRuntime.ResolveSendOrDiscard(engine, who, source, clause);
                    break;

                // DiscardTriggers batch (v142): Delinquent Duo; Penguin Knight.
                case EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice:
                    DiscardTriggersRuntime.ApplyOpponentDiscardsRandomThenChosen(engine, who, source);
                    break;

                case EffectActionKind.ShuffleGraveyardIntoDeck:
                    DiscardTriggersRuntime.ApplyShuffleGraveyardIntoDeck(engine, who, source);
                    break;

                // DeckReturnsAndMassEffects batch (v161 LOD).
                case EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws:
                    DeckReturnsAndMassEffectsRuntime.ResolveFiberJar(engine, who, source, clause);
                    break;
                case EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace:
                    DeckReturnsAndMassEffectsRuntime.ApplyGuard(engine, who, source, clause);
                    break;
                case EffectActionKind.EachPlayerChoosesOwnMonsterSwitchControl:
                    DeckReturnsAndMassEffectsRuntime.ResolveCreatureSwap(engine, who, source, clause);
                    break;
                case EffectActionKind.EachPlayerSendsHandAndFieldToGyUntilAmount:
                    DeckReturnsAndMassEffectsRuntime.ResolveBubbleCrash(engine, who, source, clause);
                    break;

                case EffectActionKind.CyberJarStyle:
                    // FlipMassEffects (v142): destroy all, reveal, summon (position choice), the rest to the hand.
                    FlipMassEffectsRuntime.ResolveCyberJar(engine, who, source, clause);
                    break;

                // FlipMassEffects (v142): Jigen Bakudan / Spear Cretin / Morphing Jar #2.
                case EffectActionKind.RecordThisCardFlipped:
                    FlipMassEffectsRuntime.RecordFlipped(engine, source);
                    break;
                case EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk:
                    FlipMassEffectsRuntime.ResolveStandbyTributeDestroyBurn(engine, who, source, clause);
                    break;
                case EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy:
                    FlipMassEffectsRuntime.ResolveEachPlayerSummonFromOwnGy(engine, who, source, clause);
                    break;
                // NegationsAndGySummons batch: Gilasaurus's gift to the opponent; Chosen One.
                case EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy:
                    FlipMassEffectsRuntime.ResolveOpponentMaySummonOneFromOwnGy(engine, who, source, clause);
                    break;
                case EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom:
                    NegationsAndGySummonsRuntime.ResolveChosenOne(engine, who, source);
                    break;
                // SimpleSpellTraps batch (v160 LOD).
                case EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps:
                    SimpleSpellTrapsRuntime.ResolveWingbeat(engine, who, source, clause);
                    break;
                case EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk:
                    SimpleSpellTrapsRuntime.ResolveBurstBreath(engine, who, source, costNumeric);
                    break;
                case EffectActionKind.DestroyTargetWithDefAtMost:
                    SimpleSpellTrapsRuntime.ResolveDestroyTargetWithDefAtMost(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy:
                    SimpleSpellTrapsRuntime.ResolveFiendComedian(engine, who, source);
                    break;
                case EffectActionKind.GainLpPerSpellTrapSentAsCost:
                    SimpleSpellTrapsRuntime.ResolveProvisions(engine, who, source, clause);
                    break;
                // EngineHygiene batch (v160): Forced Requisition's activation only places it (DiscardEvents reads the
                // face-up card); Ekibyo Drakmord's countdown is ticked at each End Phase (EngineHygieneRuntime.OnEndPhase).
                case EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard:
                case EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis:
                    break;
                case EffectActionKind.DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn:
                    SuperRejuvenation.Register(engine, who, source);
                    break;
                case EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon:
                    FlipMassEffectsRuntime.ResolveShuffleExcavateSummon(engine, who, source, clause);
                    break;

                // TargetedClassics Soft: Miracle Dig — return face-up banished monsters to the GY.
                case EffectActionKind.ReturnBanishedTargetToGy:
                    TargetedClassicsRuntime.ReturnBanishedToGraveyard(engine, source, chosenTarget);
                    break;

                case EffectActionKind.ReturnToHand:
                    if (chosenTarget == null)
                    {
                        if (clause.RequiresTargetChoice) break;
                        BeginMonsterReturnToOwnerHandBatch();
                        try
                        {
                            foreach (var m in CollectAllMatching(engine, who, clause).ToList())
                            {
                                if (m == null || m == source) continue; // Giant Trunade stays to resolve
                                if (m.IsToken) Destroy(m);
                                else if (!ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                    engine.ReturnCardToHand(m, by: who);
                                else
                                    engine.Log("Return target is unaffected at resolution.");
                            }
                        }
                        finally
                        {
                            EndMonsterReturnToOwnerHandBatch(engine);
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

                    engine.ReturnCardToHand(chosenTarget, by: who);
                    break;

                case EffectActionKind.InflictDamageToOpponent:
                    if (clause.ScaleAmountByFaceUpEffectMonsters)
                    {
                        // Ceasefire: counted after its flips resolve (both fields, face-up only).
                        var fx = who.MonstersOnField().Concat(opp.MonstersOnField())
                            .Count(m => m != null && m.FaceUp && !m.IsToken && IsTrueEffectMonster(m.Def));
                        engine.ApplyEffectDamage(opp, Mathf.Max(0, clause.Amount) * fx, source?.Name);
                        break;
                    }

                {
                    // Des Koala / Energy Drain: × opponent's hand at resolution.
                    // Just Desserts: × opponent's MonsterCount at resolution.
                    // Secret Barrel: × (opp HandCount + every card they control: monsters, Spell/Trap
                    // Zones, the Field Zone — SpellTrapsOnField includes it — and Pendulum Zones).
                    // Flat Flip burns (Poison Mummy) leave all scale flags false.
                    var baseAmt = AmountFromTarget(clause, chosenTarget,
                        AmountWithGyCopies(who, source, clause));
                    // PerCountBurns (v141): Princess of Tsurugi / D.D. Dynamite / Cemetary Bomb / Thunder Crash /
                    // Wave-Motion Cannon count what CountSource names ("if you do": nothing destroyed, no damage).
                    var perCount = PerCountBurnsRuntime.BurnCount(engine, who, source, clause, s_lastDestroyHits);
                    if (perCount >= 0)
                    {
                        if (clause.RequiresPreviousClauseHit && perCount <= 0)
                            break;
                        engine.ApplyEffectDamage(opp, Mathf.Max(0, clause.Amount) * perCount, source?.Name);
                        break;
                    }
                    if (clause.ScaleAmountByOpponentHandAndControlledCards)
                        baseAmt = Mathf.Max(0, clause.Amount) *
                                  ((opp?.HandCount ?? 0) + CardsControlled(opp));
                    else if (clause.ScaleAmountByOpponentHandCards)
                        baseAmt = Mathf.Max(0, clause.Amount) * (opp?.HandCount ?? 0);
                    else if (clause.ScaleAmountByOpponentMonsterCount)
                        baseAmt = Mathf.Max(0, clause.Amount) * (opp?.MonsterCount ?? 0);
                     engine.ApplyEffectDamage(opp, baseAmt, source?.Name);
                    // Inferno Fire Blast: the targeted "Red-Eyes B. Dragon" cannot attack this turn.
                    if (clause.TargetCannotAttackThisTurn && chosenTarget != null)
                    {
                        chosenTarget.CannotAttackDuringTurnNumber = engine.TurnNumber;
                        engine.Log($"{chosenTarget.Name} cannot attack this turn.");
                    }
                    break;
                }

                case EffectActionKind.InflictDamageToBothPlayers:
                    engine.ApplyEffectDamageBoth(who, Mathf.Max(0, clause.Amount),
                        engine.OpponentOf(who), Mathf.Max(0, clause.Amount), source?.Name);
                    break;

                case EffectActionKind.BothPlayersLifePointsBecomeZero:
                    // ConditionalSpellsAndTraps batch: Self-Destruct Button.
                    ConditionalSpellsAndTrapsRuntime.ApplyBothLifePointsBecomeZero(engine, who, source);
                    break;

                case EffectActionKind.DestroyTargetThenDamageBothPlayers:
                    // ConditionalSpellsAndTraps batch: Destruction Ring.
                    ConditionalSpellsAndTrapsRuntime.ApplyDestroyTargetThenDamageBoth(engine, who, source, clause,
                        chosenTarget, Destroy);
                    break;

                case EffectActionKind.InflictDamageHalfTributedAtk:
                    engine.ApplyEffectDamage(opp, Mathf.Max(0, costNumeric / 2), source?.Name);
                    break;


                case EffectActionKind.SetSpellTrapFromHand:
                {
                    // Effect resolution Set — do NOT gate on Main Action Window (CanSetSpellTrap).
                    // Fail-closed: hand Spell/Trap only; exclude Field Spells (Field Zone path).
                    // Cold Wave (LocksAndZones batch): no Spell/Trap can be Set, by any means, while it lasts.
                    if (LocksAndZonesRuntime.SpellTrapSetLocked(engine))
                    {
                        engine.Log($"{source?.Name}: Spell and Trap Cards cannot be Set now.");
                        break;
                    }
                    var picks = who.Hand
                        .Where(c => c?.Def != null && (c.Def.IsSpell || c.Def.IsTrap) &&
                                    !c.Def.IsFieldSpell)
                        .Take(Mathf.Max(1, clause.Amount > 0 ? clause.Amount : 1))
                        .ToList();
                    if (picks.Count == 0)
                    {
                        if (!clause.IsOptional)
                            engine.Log($"{source?.Name}: no Spell/Trap in hand to Set.");
                        break;
                    }
                    var zi = engine.FirstEmptySpellTrap(who);
                    if (zi < 0)
                    {
                        if (!clause.IsOptional)
                            engine.Log($"{source?.Name}: no Spell/Trap Zone to Set.");
                        break;
                    }
                    var pick = picks[0];
                    who.Hand.Remove(pick);
                    pick.FaceUp = false;
                    pick.SetThisTurn = true;
                    who.SpellTrapZones[zi].Occupant = pick;
                    engine.Log($"Set {pick.Name} from hand.");
                    break;
                }

                case EffectActionKind.SetThisFaceDownDefense:
                    if (source != null && who.TryFindMonster(source, out _))
                    {
                        // Light of Intervention (LocksAndZones batch): monsters cannot be flipped face-down.
                        if (LocksAndZonesRuntime.CannotTurnFaceDown(engine, source))
                        {
                            engine.Log($"{source.Name} cannot be changed to face-down Defense Position.");
                            break;
                        }
                        // FlipDownAndPeek batch (v162): ygopro checks c:IsFaceup() at resolution — a card already
                        // face-down is not changed (and Wandering Mummy's rearrangement does not follow).
                        if (!source.FaceUp)
                        {
                            engine.Log($"{source.Name} is already face-down — its position does not change.");
                            break;
                        }
                        source.FaceUp = false;
                        source.Position = BattlePosition.Defense;
                        source.ChangedPositionThisTurn = true;
                        ClearFaceUpMarks(source);
                        engine.Log($"{source.Name} is flipped to face-down Defense Position.");
                        // Wandering Mummy: then its controller's face-down monsters are rearranged.
                        if (clause.ThenShuffleFaceDownMonstersInMainZones)
                            FlipDownAndPeekRuntime.ShuffleFaceDownMainZoneMonsters(engine, who, source);
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
                    if (clause.AmountIsOriginalAtk && source?.Def != null)
                        gain = Mathf.Max(0, source.Def.atk); // "double the original ATK"
                    if (clause.ScaleAmountByCostCount)
                        gain *= Mathf.Max(1, costNumeric);
                    if (clause.DestroyThisAtEndPhase && source != null)
                    {
                        source.TempDestroyOnEndOfTurn = engine.TurnNumber;
                        engine.Log($"{source.Name} will be destroyed during the End Phase.");
                    }
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
                {
                    // Magical Labyrinth: only after its cost (Tribute the equipped monster) was paid by
                    // TryResolveActivation — never as a rider of the Equip activation's other clause.
                    if (clause.RequiresTributeEquippedMonster &&
                        !TributeSummonsRuntime.TributeEquippedCostPaid(source))
                        break;
                    // LvChain batch: the copy of the named monster this summon actually placed carries a
                    // "Special Summoned by the effect of" record, so a later clause of that card can read it
                    // (Dark Mimic LV3 draws 2 when "Dark Mimic LV1" Summoned it).
                    var lvBefore = LvChainRuntime.SnapshotNamed(who, clause);
                    TrySpecialSummonNamed(engine, who, clause, source);
                    LvChainRuntime.NoteSpecialSummoned(who, clause, source, lvBefore);
                    break;
                }

                case EffectActionKind.TributeTargetAsIfControlledThisTurn:
                    TributeSummonsRuntime.ResolveTributeTargetAsIfControlled(engine, who, source, clause,
                        chosenTarget);
                    break;

                // PositionLocks Soft: Aqua Spirit / Garuda — change 1 opp face-up monster (lock if printed).
                // No target = phase trigger (AI resolves now; a human is queued). Prefer NO DuelEngine rewrite.
                case EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase:
                    if (chosenTarget == null)
                        PositionLocksRuntime.OnPhaseTrigger(engine, who, source, clause);
                    else
                        PositionLocksRuntime.ChangeTargetPosition(engine, who, source, clause, chosenTarget);
                    break;

                // PositionLocks Soft: Curse of Fiend — every monster changes position, then rest-of-turn lock.
                case EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn:
                    PositionLocksRuntime.ChangeAllPositionsAndLock(engine, who, source, clause);
                    break;

                // BattlePhaseEnd batch (v142). The end-of-Battle-Phase clause is fired by
                // BattlePhaseEndRuntime.FireEndOfBattlePhase; this arm resolves the same way if reached here.
                case EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition:
                case EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells: // BothSideAndMonsterEquip (v161)
                case EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn: // SummonPromptsAndCosts (v163)
                    BattlePhaseEndRuntime.ApplyEndOfBattlePhaseClause(engine, who, source, clause);
                    break;

                // BattleBoosts batch (v161). Spear Dragon at the end of the Damage Step (attacker only, see
                // FireEndOfDamageFor); Gray Wing's extra attack; The Hunter with 7 Weapons' declaration (the token picked
                // when it triggered). Dragon's Rage and the Hunter's battle gain are read where they apply
                // (BattleBoostsRuntime.FieldGrantsPiercing / ApplyDamageCalculationAtk); the monster Quick Effects resolve
                // as their own Chain Links (BattleBoostsRuntime.TryResolveLink).
                case EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep:
                    BattleBoostsRuntime.ChangeThisToDefense(engine, who, source);
                    break;
                case EffectActionKind.GrantThisExtraAttackThisTurn:
                    BattleBoostsRuntime.GrantExtraAttack(engine, who, source, clause);
                    break;
                case EffectActionKind.DeclareTypeForThisCardBattles:
                    BattleBoostsRuntime.DeclareType(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.GrantPiercingToYourRaceMonsters:
                case EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType:
                case EffectActionKind.GainAtkThisDamageCalculation:
                case EffectActionKind.GainAtkDefUntilEndOfBattlePhase:
                    break;

                // PgdSpellTrapBasics batch (v162 PGD). Card Shuffle's shuffle resolves on its own face-up ignition link
                // (PerCountBurnsRuntime → PgdSpellTrapBasicsRuntime.TryResolveShuffleLink); applied directly, the AI path
                // shuffles the controller's own Deck.
                case EffectActionKind.FlipFaceDownTargetToFaceUpAttack:
                    PgdSpellTrapBasicsRuntime.FlipTargetFaceUpAttack(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase:
                    PgdSpellTrapBasicsRuntime.ModifyAllFaceUp(engine, who, source, clause);
                    break;
                case EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase:
                    PgdSpellTrapBasicsRuntime.BecomeTypeUntilEndPhase(engine, who, source, clause);
                    break;
                case EffectActionKind.DestroyAllFaceUpMonstersOnField:
                    // SummonTriggersAndTributes batch (v163): Dark Dust Spirit — "all other face-up monsters".
                    PgdSpellTrapBasicsRuntime.DestroyAllFaceUp(engine, who, source,
                        clause.ExceptThisCard ? source : null);
                    break;
                case EffectActionKind.TargetCanAttackDirectlyThisTurn:
                    PgdSpellTrapBasicsRuntime.GrantDirectAttack(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.ControllerHandSizeLimitBecomesForDuel:
                    PgdSpellTrapBasicsRuntime.SetHandLimit(engine, who, source, clause);
                    break;
                case EffectActionKind.ShuffleYourOrOpponentsDeck:
                    if (who?.Deck != null)
                        engine.Rng.Shuffle(who.Deck);
                    engine.Log($"{source?.Name}: {who?.Name} shuffles their own Deck.");
                    break;

                // FlipDownAndPeek batch (v162): 8-Claws Scorpion's damage-calculation ATK is a marker read by
                // FlipDownAndPeekRuntime.ApplyDamageCalculationAtk; Giant Axe Mummy (EndOfDamageStep: chosenTarget is the
                // attacker); Cobraman Sakuzy / A Man with Wdjat look at Set cards; Trap Dustshoot.
                case EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation:
                    break;
                case EffectActionKind.DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown:
                    FlipDownAndPeekRuntime.ResolveDestroyLowerAtkAttacker(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.LookAtOpponentSetSpellTraps:
                    FlipDownAndPeekRuntime.LookAtOpponentSetSpellTraps(engine, who, source);
                    break;
                case EffectActionKind.LookAtTargetSetCard:
                    FlipDownAndPeekRuntime.ResolveLookAtSetCard(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.LookAtOpponentHandReturnChosenMonsterToDeck:
                    FlipDownAndPeekRuntime.ResolveHandMonsterToDeck(engine, who, source);
                    break;

                case EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase:
                    // Steel Scorpion (AfterDamageCalculation): chosenTarget is the attacking monster.
                    BattlePhaseEndRuntime.MarkAttackerForDelayedDestroy(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.BlastSphereEquipThenStandbyDestroyInflict:
                    MfcLabCards3Runtime.BlastSphereEquipThenStandbyDestroyInflict(engine, who, source, clause);
                    break;

                // StandbyGrowthAndMaintenance batch (v142).
                case EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp:
                    StandbyGrowthAndMaintenanceRuntime.ApplyRaceGrowthFlip(engine, who, source, clause);
                    break;

                case EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField:
                    if (clause.Timing == EffectTiming.Activate)
                        StandbyGrowthAndMaintenanceRuntime.ResolveMirrorWallActivation(engine, who, source);
                    break;

                case EffectActionKind.InflictDamageToOwnerPerCardSentToGy:
                    if (clause.Timing == EffectTiming.Activate)
                        StandbyGrowthAndMaintenanceRuntime.StartGySendWatch(engine, who, source);
                    break;

                case EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby:
                    // Continuous: applied on each board refresh (StandbyGrowthAndMaintenanceRuntime.ApplyBoard).
                    break;

                case EffectActionKind.AddNamedFromDeckToHand:
                    AddNamedFromDeck(engine, who, clause);
                    break;

                case EffectActionKind.DestroyOneInHandOneOnFieldOneInDeck:
                    MfcSimpleSpellsRuntime.DestroyOneInHandOneOnFieldOneInDeck(engine, who, source, clause);
                    break;

                case EffectActionKind.GainAtkDefPerNamedInBothGyWhileFaceUp:
                case EffectActionKind.GainAtkDefPerNamedCardInBothGyWhileFaceUp:
                    // Continuous: applied on each board refresh (StandbyGrowthAndMaintenanceRuntime.ApplyBoard).
                    break;

                case EffectActionKind.InflictDamagePer1000TributedAtk:
                {
                    var per = clause.Amount > 0 ? clause.Amount : 1000;
                    var n = costNumeric / per;
                    engine.ApplyEffectDamage(opp, n * per, source?.Name);
                    break;
                }

                case EffectActionKind.DestroySpecialSummonedMonsters:
                    // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        // Every Special Summoned monster on the field, this card included (Jowgen: Lua
                        // GetMatchingGroup(IsSummonType SPECIAL) with no exception; Special Hurricane is a Spell).
                        foreach (var m in who.MonstersOnField().Concat(opp.MonstersOnField()).ToList())
                        {
                            if (m == null) continue;
                            if (m.WasSpecialSummoned)
                                Destroy(m);
                        }
                    });

                    break;

                case EffectActionKind.DiscardUpToMonstersFromHand:
                    if (chosenTarget != null)
                        MonsterEffects.DiscardFromHandByInstance(who, chosenTarget);
                    break;

                case EffectActionKind.OpponentTributesOneMonster:
                    TributeOneOpponentMonster(engine, who, source);
                    break;

                case EffectActionKind.SwapOriginalAtkDefUntilEndOfTurn:
                    foreach (var side in new[] { who, opp })
                    {
                        if (side == null) continue;
                        foreach (var m in side.MonstersOnField())
                        {
                            if (m == null || !m.FaceUp) continue;
                            m.SwapOriginalAtkDefThisTurn = true;
                        }
                    }

                    engine.Log($"{source?.Name}: face-up monsters swap original ATK and DEF until the End Phase.");
                    break;

                case EffectActionKind.DamageStepAttributeAtk:
                    // Applied during damage calculation, not as an activation.
                    break;

                case EffectActionKind.ShuffleControllerDeck:
                    if (who?.Deck != null)
                        engine.Rng.Shuffle(who.Deck);
                    engine.Log($"{source?.Name}: Deck shuffled.");
                    break;

                case EffectActionKind.DestroyAllFaceUpNamedThenForceRaceAttack:
                    ResolveNamedThenForceRaceAttack(engine, source, clause);
                    break;

                case EffectActionKind.ReorderTopOfOwnDeck:
                    BeginReorderTop(engine, who, source, clause);
                    break;

                case EffectActionKind.PayLpToDeclareAttack:
                case EffectActionKind.CallCoinLoseHalfLpIfWrong:
                case EffectActionKind.SealNonRaceAttackerNextTurn:
                case EffectActionKind.AttacksMustTargetEquippedHost:
                case EffectActionKind.OpponentCannotActivateTrapsDuringBattle:
                case EffectActionKind.CountsAsTwoTributesForAttribute:
                case EffectActionKind.MillToDeclareAttack:
                case EffectActionKind.InflictDamageWhenControllerGainsLp:
                case EffectActionKind.GainLpWhenControllerDraws:
                case EffectActionKind.GainLpWhenMonsterReturnsToOwnerHand:
                case EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons:
                case EffectActionKind.GainLpWhenTributedForSummon:
                case EffectActionKind.DrawWhenTributeSummonedWithTwoPlus:
                    break;

                // SpecialSummonEffects batch (v161): Skull Knight #2 (DuelEngine.CompleteNormalSummon) and Twin-Headed
                // Behemoth (FirePhaseTriggersFor) resolve through their runtime hooks, never here.
                case EffectActionKind.SpecialSummonNamedFromDeckWhenTributedForRaceSummon:
                case EffectActionKind.SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn:
                case EffectActionKind.GyRevivalAfterStandbyWhenDestroyedByOpponent:
                    MfcLabCards3Runtime.GyRevivalAfterStandbyWhenDestroyedByOpponent(engine, who, source, clause);
                    break;
                // Gradius' Option: continuous, applied on each board refresh (SpecialSummonEffectsRuntime.ApplyBoard).
                case EffectActionKind.AtkDefBecomeSelectedNamed:
                case EffectActionKind.DestroyThisWhenSelectedNamedLeavesField:
                    break;
                // SpecialSummonEffects batch (v161): Winged Minion's lasting gain; The Puppet Magic of Dark Ruler.
                case EffectActionKind.TargetGainsAtkDefWhileFaceUp:
                    SpecialSummonEffectsRuntime.ApplyGainWhileFaceUp(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.BanishLevelSumThenSpecialSummonTarget:
                    SpecialSummonEffectsRuntime.ResolvePuppetMagic(engine, who, source, chosenTarget);
                    break;

                // Toons batch (v142): the Toon procedure and continuous Toon rules — applied by ToonsRuntime hooks
                // in DuelEngine (summon procedure, Special Summon placement, destruction, attack legality), never
                // resolved as an effect.
                case EffectActionKind.SpecialSummonFromHandProcedure:
                // NegationsAndGySummons batch: Gilasaurus's hand procedure (DuelEngine.CanSpecialSummonProcedure) and
                // Card of Safe Return's draw (DuelEngine.CompleteSpecialSummon) are hooks, never resolved here.
                case EffectActionKind.SpecialSummonThisFromHandByOwnProcedure:
                case EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy:
                // AttackShieldsAndDamageZero batch: the damage calculation (AvoidsBattleDamage) and the Special Summon funnel
                // (DuelEngine.CompleteSpecialSummon -> OnSpecialSummoned) read these; never resolved here.
                case EffectActionKind.AvoidBattleDamageFromBattlesInvolving:
                case EffectActionKind.MillOpponentWhenRaceSpecialSummoned:
                // GuardiansAndEquipEvents batch: Morale Boost's equip / leave-field triggers are engine hooks
                // (GuardiansAndEquipEventsRuntime.NoteEquipped / NoteEquipLeftField), never resolved here.
                case EffectActionKind.GainLpWhenEquipSpellEquipped:
                case EffectActionKind.DamageWhenEquipSpellLeavesField:
                case EffectActionKind.CannotAttackTurnSpecialSummoned:
                case EffectActionKind.DestroyThisWhenNamedDestroyed:
                case EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon:
                    break;

                case EffectActionKind.RemoveAllSpellCounters:
                {
                    var removed = 0;
                    foreach (var side in new[] { who, opp })
                    {
                        if (side == null) continue;
                        foreach (var m in side.MonstersOnField())
                        {
                            if (m == null || m.Counters <= 0) continue;
                            removed += m.Counters;
                            m.Counters = 0;
                        }
                        foreach (var st in side.SpellTrapsOnField())
                        {
                            if (st == null || st.Counters <= 0) continue;
                            removed += st.Counters;
                            st.Counters = 0;
                        }
                        if (side.FieldSpellZone?.Occupant != null && side.FieldSpellZone.Occupant.Counters > 0)
                        {
                            removed += side.FieldSpellZone.Occupant.Counters;
                            side.FieldSpellZone.Occupant.Counters = 0;
                        }
                    }

                    engine.Log($"{source?.Name}: removed {removed} Spell Counter(s) from the field.");
                    break;
                }

                case EffectActionKind.OpponentShufflesHandThenDraws:
                {
                    if (opp?.Hand == null || opp.Deck == null) break;
                    var shuffled = opp.Hand.Count;
                    foreach (var h in opp.Hand.ToList())
                    {
                        opp.Hand.Remove(h);
                        opp.Deck.Add(h.CardId);
                    }
                    engine.Rng.Shuffle(opp.Deck);
                    var draws = clause.Amount > 0 ? clause.Amount : shuffled;
                    engine.Log($"{source?.Name}: {opp.Name} shuffles {shuffled} card(s) into the Deck and draws {draws}.");
                    if (draws > 0)
                        engine.Draw(opp, draws, silent: false);
                    break;
                }

                case EffectActionKind.SkipControllerNextStandbyPhase:
                    who.SkipNextStandbyPhase = true;
                    engine.Log($"{who.Name} skips their next Standby Phase.");
                    break;

                case EffectActionKind.SealTrapsAnsweringThisSummon:
                    engine.SealTrapsForSummonResponse();
                    engine.Log($"{source?.Name}: no Trap Cards can be activated.");
                    break;

                case EffectActionKind.OptionalPayLpShiftControl:
                case EffectActionKind.PlaceHandCardsOnBottomOfDeck:
                    break;

                case EffectActionKind.DestroyOppMonstersAtkLeq:
                {
                    var cap = costNumeric > 0 ? costNumeric : clause.Amount;
                    // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var m in opp.MonstersOnField().ToList())
                        {
                            if (m != null && m.CurrentAtk <= cap)
                                Destroy(m);
                        }
                    });

                    break;
                }

                case EffectActionKind.CoinCallDestroyOppOrSelf:
                    break;

                // IgnitionCostsAndSimpleSpells batch: Sand Gambler's 3 coins; Mystik Wok's LP gain.
                case EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours:
                    IgnitionCostsAndSimpleSpellsRuntime.ResolveCoinGamble(engine, who, source, clause);
                    break;
                case EffectActionKind.GainLpEqualToTributedAtkOrDef:
                    IgnitionCostsAndSimpleSpellsRuntime.ResolveTributedStatGain(engine, who, source);
                    break;
                case EffectActionKind.CoinCallDoubleOrHalveAtk:
                    break;

                case EffectActionKind.CoinTossNDestroyIfHeads:
                {
                    var n = clause.CoinCount > 0 ? clause.CoinCount : 3;
                    var need = clause.Amount > 0 ? clause.Amount : 2;
                    var coinTarget = chosenTarget;
                    // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo all n coins first.
                    CoinDiceInterruptsRuntime.TossCoins(engine, who, source, n, r => r.Count(h => h) >= need, r =>
                    {
                        using var redoScope = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                        var heads = r.Count(h => h);
                        engine.Log($"{source?.Name}: {heads}/{n} heads (need {need}).");
                        if (heads < need || coinTarget == null) return;
                        if (ContinuousProtections.TargetedEffectBlocked(engine, coinTarget, source))
                        {
                            engine.Log("Target is protected at resolution.");
                            return;
                        }
                        Destroy(coinTarget);
                    });
                    break;
                }

                case EffectActionKind.NegateSummon:
                    // SummonNegation batch (v141): Horn of Heaven / Solemn Judgment.
                    SummonNegationRuntime.ApplyNegateSummon(engine, who, source, clause, chosenTarget);
                    break;

                case EffectActionKind.ModifyAllAtkDefByDieUntilEndOfTurn:
                {
                    // Graceful Dice / Skull Dice: one roll; every face-up monster that side controls now
                    // gains (Amount > 0) or loses (Amount < 0) result × |Amount| ATK/DEF until the End Phase.
                    // A die-roll event (CoinDiceInterrupts batch): Dice Re-Roll may have it re-rolled first. A high
                    // roll is good for the roller when it raises their own side or lowers the opponent's.
                    var raisesRollerSide = (clause.Side == EffectSide.Opponent) == (clause.Amount < 0);
                    var affected = clause.Side == EffectSide.Opponent ? opp : who;
                    CoinDiceInterruptsRuntime.RollDie(engine, who, source,
                        roll => raisesRollerSide ? roll >= 4 : roll <= 3, roll =>
                        {
                            using var redoScope = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                            var delta = roll * clause.Amount;
                            engine.Log($"{source?.Name} rolls a {roll}.");
                            if (affected == null) return;
                            foreach (var m in affected.MonstersOnField().ToList())
                            {
                                if (m == null || !m.FaceUp) continue;
                                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                                {
                                    engine.Log($"{m.Name} is unaffected.");
                                    continue;
                                }
                                m.UntilEndOfTurnAtk += delta;
                                m.UntilEndOfTurnDef += delta;
                                engine.Log(
                                    $"{m.Name} {(delta >= 0 ? "gains" : "loses")} {Mathf.Abs(delta)} ATK/DEF until the " +
                                    $"End Phase (now {m.CurrentAtk}/{m.CurrentDef}).");
                            }
                        });

                    break;
                }

                case EffectActionKind.NegateCardFamilyOnFieldThisTurn:
                    // TurnNegationTraps batch (v141): World Suppression / Mystic Probe / Metal Detector.
                    TurnNegationTrapsRuntime.Register(engine, who, source, clause.NegatedCardFamily);
                    break;

                case EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn:
                    // LocksAndZones batch (v142): Cold Wave.
                    LocksAndZonesRuntime.RegisterSpellTrapLock(engine, who, source);
                    break;

                case EffectActionKind.BanishTargetUntilYourNextStandbyPhase:
                    // LocksAndZones batch (v142): Dimensionhole.
                    LocksAndZonesRuntime.BanishUntilNextStandby(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown:
                    // LocksAndZones batch (v142): Light of Intervention — ContinuousWhileFaceUp, honored where monsters
                    // are Set, Special Summoned face-down or turned face-down (LocksAndZonesRuntime).
                    break;

                // HandDisruption batch (v142).
                case EffectActionKind.CoinCallDrawUntilOrSkipNextTurn:
                    // Gamble: called coin — draw until N cards in hand, or skip your next turn.
                    HandDisruptionRuntime.ResolveCoinCallDrawUntilOrSkip(engine, who, source, clause);
                    break;
                case EffectActionKind.RevealDeckCardsOpponentPicksOneToHand:
                    // Painful Choice: select N from the Deck, the opponent picks 1 for the hand, the rest to the GY.
                    HandDisruptionRuntime.BeginRevealDeckPick(engine, who, source, clause);
                    break;
                case EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase:
                    // Appropriate: continuous — applied by HandDisruptionRuntime.OnDrew from DuelEngine.Draw.
                    break;

                case EffectActionKind.RedirectToAnotherAppropriateTarget:
                    // RedirectAndReplace batch (v142): Fairy's Hand Mirror / Shift.
                    RedirectAndReplaceRuntime.ResolveRedirect(engine, who, source, clause, chosenTarget);
                    break;

                // BattleWindows batch. Fairy Box / Jam Defender / Dreamsprite are triggers fired by
                // BattleWindowsRuntime at the attack declaration / before damage calculation, never applied here (the
                // card's activation only places it face-up).
                case EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase:
                case EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl:
                case EffectActionKind.BattleAnotherOwnMonsterInstead:
                    break;

                // EndOfDamageStepRiders batch: end-of-Damage-Step riders on the battled monster (fired by FireEndOfDamageFor with
                // that monster); Prickle Fairy's shield, Insect Princess's Attack Position rule and Drillago's direct attack are
                // continuous rules read by EndOfDamageStepRidersRuntime.
                case EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep:
                    EndOfDamageStepRidersRuntime.DestroyBattledMonster(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep:
                    EndOfDamageStepRidersRuntime.ChangeBattledMonsterToDefense(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep:
                    EndOfDamageStepRidersRuntime.PlaceAttackerOnTopOfDeck(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters:
                case EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack:
                case EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast:
                    break;

                case EffectActionKind.DestroyAttackerAtEndOfDamageStep:
                    // DamageStepRules batch: Thousand Needles' end-of-Damage-Step trigger and Continuous Destruction Punch's
                    // continuous marker; otherwise Destruction Punch answering the end-of-Damage-Step window.
                    if (DamageStepRulesRuntime.TryApplyDestroyAttacker(engine, who, source, clause, chosenTarget))
                        break;
                    BattleWindowsRuntime.ResolveDestructionPunch(engine, who, source);
                    break;

                // DamageStepRules batch: Meteorain's turn piercing; the other clauses are rule markers read by
                // DamageStepRulesRuntime (attack condition, equal-ATK protection, Damage Step ATK, reflected damage).
                case EffectActionKind.YourMonstersGainPiercingThisTurn:
                    DamageStepRulesRuntime.GrantTurnPiercing(engine, who, source);
                    break;
                case EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand:
                case EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster:
                case EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep:
                case EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis:
                    break;

                // BattleDestroyedTriggers batch: Agido's die-roll revival and Keldo's GY-to-Deck (battle-destroyed GY
                // triggers); Different Dimension Dragon's protections are markers read by BattleDestroyedTriggersRuntime.
                case EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll:
                    BattleDestroyedTriggersRuntime.RollDieThenSummonFromGy(engine, who, source, clause);
                    break;
                case EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck:
                    BattleDestroyedTriggersRuntime.ShuffleOpponentGyTargetsIntoDeck(engine, who, source, clause);
                    break;
                case EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis:
                case EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost:
                    break;

                case EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead:
                    if (BattleWindowsRuntime.ResolveDarkSpirit(engine, who, source, chosenTarget))
                        attackNegated = true;
                    break;

                case EffectActionKind.DestroyNewFaceUpMonstersSharingAName:
                    // Kotodama: a continuous effect applied on each board refresh
                    // (RedirectAndReplaceRuntime.SweepSameNameDestruction), never as a one-shot clause.
                case EffectActionKind.BanishCardsSentToGyInstead:
                    // Banisher of the Light: a replacement read on every GY send
                    // (RedirectAndReplaceRuntime.SendsToBanishedInstead), never as a one-shot clause.
                    break;

                case EffectActionKind.RollDieZorc:
                {
                    // A die-roll event (CoinDiceInterrupts batch): Dice Re-Roll may have it re-rolled first.
                    var lowMax = clause.DieLowMax > 0 ? clause.DieLowMax : 2;
                    var midMax = clause.DieMidMax > 0 ? clause.DieMidMax : 5;
                    CoinDiceInterruptsRuntime.RollDie(engine, who, source, roll => roll <= midMax, roll =>
                    {
                        using var redoScope = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                        engine.Log($"{source?.Name} rolls a {roll}.");
                        if (roll <= lowMax)
                        {
                            // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
                            FlipMassEffectsRuntime.Simultaneously(engine, () =>
                            {
                                foreach (var m in opp.MonstersOnField().ToList())
                                    Destroy(m);
                            });
                        }
                        else if (roll <= midMax)
                        {
                            var pick = opp.MonstersOnField().OrderByDescending(m => m.CurrentAtk)
                                .FirstOrDefault();
                            if (pick != null) Destroy(pick);
                        }
                        else
                        {
                            FlipMassEffectsRuntime.Simultaneously(engine, () =>
                            {
                                foreach (var m in who.MonstersOnField().ToList())
                                    Destroy(m);
                            });
                        }
                    });

                    break;
                }

                case EffectActionKind.SpecialSummonToken:
                    // TokensDeclarationsAndPicks batch: all-or-nothing Tokens and the summon oath's own Special Summon
                    // (Stray Lambs, Scapegoat, Multiplication of Ants); other Token clauses Summon as before.
                    TokensDeclarationsAndPicksRuntime.SummonTokens(engine, who, clause, source,
                        positions => SummonTokens(engine, who, clause, source, positions));
                    break;

                // TokensDeclarationsAndPicks batch.
                case EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute:
                    // DNA Transplant: read live by EquipFamilyRuntime.CurrentAttribute; nothing to apply here.
                    break;
                case EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop:
                    TokensDeclarationsAndPicksRuntime.ResolveOpponentPlacesOnTop(engine, who, source, clause);
                    break;
                case EffectActionKind.RandomHandCardSpecialSummonOrSendToGy:
                    TokensDeclarationsAndPicksRuntime.ResolveRandomHandSummon(engine, who, source);
                    break;
                case EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase:
                    TokensDeclarationsAndPicksRuntime.ApplyDefToAtkThenDestroy(engine, source, chosenTarget);
                    break;
                case EffectActionKind.PlaceCounterOnThisCard:
                    TokensDeclarationsAndPicksRuntime.PlaceCounter(engine, who, source, clause);
                    break;
                case EffectActionKind.InflictCountersTimesAmountToDestroyer:
                    TokensDeclarationsAndPicksRuntime.InflictCounterBurn(engine, source, clause);
                    break;

                // PhaseTriggersAndIgnitionCosts batch: Amazoness Blowpiper, Helping Robo for Combat, Senri Eye, Magical
                // Scientist, Roulette Barrel, Physical Double (Mass Driver is a face-up field ignition: PerCountBurnsRuntime).
                case EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby:
                    PhaseTriggersAndIgnitionCostsRuntime.ResolveStandbyAtkLoss(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.DrawThenPlaceHandCardOnDeckBottom:
                    PhaseTriggersAndIgnitionCostsRuntime.DrawThenBottom(engine, who, source, clause);
                    break;
                case EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby:
                    PhaseTriggersAndIgnitionCostsRuntime.ResolveSenriEye(engine, who, source, clause);
                    break;

                // RecoveryAndPhaseCosts batch: Dimension Distortion, Dimension Fusion, Recycle, Reload, Coach Goblin; Manticore
                // of Darkness is fired by RecoveryAndPhaseCostsRuntime.TryFirePhaseClause (FirePhaseTriggersFor), its arm is a marker.
                case EffectActionKind.SpecialSummonBanishedTarget:
                    RecoveryAndPhaseCostsRuntime.SummonBanishedTarget(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.EachPlayerSpecialSummonsFaceUpBanishedMonstersAsPossible:
                    FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromOwnBanished(engine, who, source, clause);
                    break;
                case EffectActionKind.PayLpReturnGyNonMonsterToDeckBottomAtYourStandby:
                    RecoveryAndPhaseCostsRuntime.ResolveRecycle(engine, who, source, clause);
                    break;
                case EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber:
                    // Reload is the activator's own hand (Side=Controller); Mind Wipe is the opponent's
                    // (Side=Opponent), whose hand is added to the Deck and redrawn.
                    RecoveryAndPhaseCostsRuntime.ShuffleHandIntoDeckThenDraw(engine,
                        clause?.Side == EffectSide.Opponent ? engine.OpponentOf(who) : who, source);
                    break;
                case EffectActionKind.ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase:
                    RecoveryAndPhaseCostsRuntime.ResolveCoachGoblin(engine, who, source, clause);
                    break;
                case EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase:
                    break;
                case EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase:
                    PhaseTriggersAndIgnitionCostsRuntime.ResolveMagicalScientist(engine, who, source, clause);
                    break;
                case EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel:
                    PhaseTriggersAndIgnitionCostsRuntime.ResolveRouletteBarrel(engine, who, source, clause);
                    break;
                case EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase:
                    PhaseTriggersAndIgnitionCostsRuntime.ResolveCopyToken(engine, who, source, clause, chosenTarget);
                    break;

                // FlipCostsAndFusion batch: Summoner of Illusions / De-Fusion (player choices via the target picker).
                case EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase:
                    FlipCostsAndFusionRuntime.ResolveSummonerOfIllusions(engine, who, source);
                    break;
                case EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials:
                    FlipCostsAndFusionRuntime.ResolveDeFusion(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.SpecialSummonFusionFromExtra:
                {
                    // SummonTriggersAndTributes batch (v163): Metamorphosis — the Tributed monster's Level (costNumeric).
                    if (clause.MatchTributedLevel)
                    {
                        SummonTriggersAndTributesRuntime.SpecialSummonLevelMatchedFusionFromExtra(engine, who, source,
                            costNumeric);
                        break;
                    }
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
                        if (m != null && m.FaceUp && m.Level <= cap &&
                            !MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, m))
                            engine.TryTakeControl(who, m);
                    }

                    break;
                }

                case EffectActionKind.TakeControlUntilEndPhase:
                {
                    if (chosenTarget == null) break;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    var old = engine.ControllerOf(chosenTarget);
                    if (old == null || old == who)
                    {
                        engine.Log($"{source?.Name}: take control failed — target is not opponent-controlled.");
                        break;
                    }
                    // "Target 1 face-up monster …" (Brain Control / Shadow Tamer / Dragon Manipulator):
                    // a target turned face-down before resolution is not taken (Lua tc:IsFaceup()).
                    if (clause.Zone == EffectZoneFilter.OppFaceUpMonsters && !chosenTarget.FaceUp)
                    {
                        engine.Log($"{source?.Name}: {chosenTarget.Name} is no longer face-up — no effect.");
                        break;
                    }
                    // MonsterNegationAndControlLocks batch: Blindly Loyal Goblin — its control cannot switch.
                    if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, chosenTarget)) break;
                    if (!engine.TryTakeControl(who, chosenTarget))
                    {
                        engine.Log($"{source?.Name}: take control failed — no zone.");
                        break;
                    }
                    if (!chosenTarget.ControlRevertsAtEndPhase)
                        chosenTarget.OriginalControllerIsPlayer = old.IsPlayer;
                    chosenTarget.ControlRevertsAtEndPhase = true;
                    // SummonTriggersAndTributes batch (v163): Jowls of Dark Demise — it can attack directly this turn.
                    if (clause.TakenMonsterCanAttackDirectlyThisTurn)
                        SummonTriggersAndTributesRuntime.GrantDirectAttackAfterControl(engine, who, source, chosenTarget);
                    break;
                }

                case EffectActionKind.TakeControlWhileSourceFaceUp:
                    TakeControlRuntime.ApplyTakeControlWhileSourceFaceUp(engine, who, source, clause, chosenTarget);
                    break;

                case EffectActionKind.SwitchControlWithThis:
                    TakeControlRuntime.ApplySwitchControlWithThis(engine, who, source, clause, chosenTarget);
                    break;

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
                    // ContinuousLocksAndIgnitions batch (v163): Buster Rancher's ATK equip limit (Duel.Equip checks it).
                    if (ContinuousLocksAndIgnitionsRuntime.BreaksAtkEquipLimit(engine, clause, chosenTarget))
                    {
                        engine.Log($"{source.Name}: {chosenTarget.Name} has more than {clause.EquipLimitAtkMax} ATK — " +
                                   "it cannot be equipped.");
                        break;
                    }
                    EquipCardTo(engine, who, source, chosenTarget, clause.EquipAtkBonus, clause);
                    if (clause.TakeControlOfTarget)
                    {
                        // Soft residual: The Emperor's Holiday — Equip Card effects negated
                        // while face-up. Equips stay; take-control does not apply.
                        // Soft FieldSpellEffects Equip-apply skip Soft pattern (Mask Soft surgical).
                        // Already-taken control Soft residual OK (no snap-back here).
                        if (ContinuousProtections.EquipCardEffectsAreNegated(engine, source))
                        {
                            engine.Log($"{source.Name}: Equip effects negated — take control does not apply.");
                            // EquipModel batch: it takes control once that negation ends (still equipped).
                            if (source.EquippedTo == chosenTarget)
                                EquipModelRuntime.SuspendControl(engine, source, chosenTarget);
                            break;
                        }
                        // MonsterNegationAndControlLocks batch: a control-locked monster stays equipped, not taken.
                        if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, chosenTarget)) break;
                        chosenTarget.TakenByEquipControl = true;
                        if (!engine.TryTakeControl(who, chosenTarget))
                            engine.Log($"{source.Name}: take control failed — no zone.");
                    }
                    break;

                case EffectActionKind.LookAtOpponentTopCardThenTopOrBottom:
                {
                    var deckOwner = engine.OpponentOf(who);
                    if (deckOwner?.Deck == null || deckOwner.Deck.Count == 0) break;
                    var topId = deckOwner.Deck[0];
                    var topCard = engine.CreateCardInstance(topId);
                    if (!engine.IsHumanControlled(who))
                    {
                        var bottom = !SpiritMonstersRuntime.AiKeepsOnTop(engine, who, topCard);
                        if (bottom) SpiritMonstersRuntime.MoveTopToBottomPublic(deckOwner);
                        engine.Log($"{source?.Name}: {who.Name} looks at the top card of {deckOwner.Name}'s Deck and puts it on the " +
                                   (bottom ? "bottom." : "top."));
                        break;
                    }
                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = topCard,
                        TargetKind = EffectTargetKind.CardInYourDeck,
                        IsMonsterEffect = true,
                        UsesTextProgram = true,
                        TargetPicksRemaining = 1,
                        DeclarationPrompt = $"{source?.Name}: the top card of your opponent's Deck is {topCard?.Name ?? "unknown"}. " +
                                            "Tap it to put it on the bottom, or CANCEL to leave it on top."
                    };
                    pending.LegalTargets.Add(pending.Card);
                    ChoiceBulletsRuntime.RegisterTopCardLook(pending, deckOwner, topId, source?.Name);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    break;
                }

                case EffectActionKind.EquipThisToAttackingMonster:
                    MfcLabCards3Runtime.EquipThisToAttackingMonster(engine, who, source, clause);
                    break;

                // DcrEquipSpells batch: Equip Card grants read where they apply (DcrEquipSpellsRuntime).
                case EffectActionKind.EquippedCanAttackDirectly:
                case EffectActionKind.EquippedGainsExtraAttacks:
                case EffectActionKind.EquippedBattleDamageToOpponentBecomes:
                    break;
                // SpellEventsAndHandSendTriggers batch: Curse of Darkness / Tower of Babel / Spirit of the Pot of Greed are
                // ContinuousWhileFaceUp markers read by SpellEventsAndHandSendTriggersRuntime (Spell resolved / activated).
                case EffectActionKind.InflictDamageToSpellActivatorAfterResolve:
                case EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator:
                case EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition:
                    break;
                case EffectActionKind.ReturnThisFromGyToHand:
                    DcrEquipSpellsRuntime.ReturnToHand(engine, who, source);
                    break;

                case EffectActionKind.UnequipThisSpecialSummon:
                    UnequipAndSummon(engine, who, source);
                    break;

                // UnionRiders batch (v175): Union Rider's take-and-equip; the Union-state riders resolve in
                // UnionRidersRuntime (ignitions, the battle-damage trigger) or are read by ContinuousProtections (Lunatite).
                case EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis:
                    UnionRidersRuntime.TakeControlAndEquip(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain:
                    UnionRidersRuntime.ResolveCombinationAttack(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly:
                case EffectActionKind.ChangeEquippedMonsterBattlePosition:
                case EffectActionKind.EquippedUnaffectedByOpponentSpellEffects:
                    break;

                // EquipAndSpellProtection batch: continuous protections / self-destruction read by
                // EquipAndSpellProtectionRuntime; Magic Reflector places its counter.
                case EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped:
                case EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects:
                case EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast:
                    break;
                case EffectActionKind.PlaceDestroyReplacementCounterOnTarget:
                    EquipAndSpellProtectionRuntime.PlaceReflectorCounter(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.EquipAllEquipCardsToTarget:
                    // EquipFamily batch: Collected Power.
                    EquipFamilyRuntime.ResolveCollectAllEquips(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.MoveEquipCardToAnotherCorrectTarget:
                    // EquipModel batch: Tailor of the Fickle (the clause's target is the Equip Card).
                    EquipModelRuntime.ResolveSwitch(engine, who, source, chosenTarget);
                    break;

                // GuardiansAndEquipEvents batch: Guardian Elma / Chopman — equip the targeted Equip Spell from the GY to this card.
                case EffectActionKind.EquipTargetedGyEquipSpellToThis:
                    GuardiansAndEquipEventsRuntime.ApplyEquipFromGy(engine, who, source, chosenTarget);
                    break;

                case EffectActionKind.EquipTargetToThis:
                    if (chosenTarget == null || source == null) break;
                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log("Target is protected at resolution.");
                        break;
                    }
                    // ChoiceBullets batch (v173) — Cyber Raider: the Equip Card is already on the field, equipped to
                    // a monster; re-host it to this card (Lua CheckEquipTarget + Duel.Equip).
                    if (chosenTarget.EquippedTo != null)
                    {
                        if (!EquipFamilyRuntime.IsCorrectEquipTarget(engine, chosenTarget, source))
                        {
                            engine.Log($"{source.Name}: {chosenTarget.Name} cannot be equipped to it.");
                            break;
                        }
                        EquipFamilyRuntime.MoveEquip(engine, chosenTarget, source, false);
                        break;
                    }
                    // Ignis eqop re-checks eqcon at resolution: do not replace an existing absorb.
                    if (AbsorbedEquipOf(source) != null)
                    {
                        engine.Log($"{source.Name}: already equipped (max. 1) — absorb does not resolve.");
                        break;
                    }
                    EquipCardTo(engine, engine.ControllerOf(chosenTarget) ?? opp, chosenTarget, source, 0);
                    // The absorbed card carries this host's flag (Ignis s.eqfilter): only it sets this card's
                    // ATK/DEF, counts for the max. 1 and is destroyed instead by battle.
                    if (chosenTarget.EquippedTo == source)
                        chosenTarget.AbsorbedBy = source;
                    break;

                case EffectActionKind.PlaceSpellCounters:
                {
                    // SpellCounterSpenders batch: Pitch-Black Power Stone's counters were placed as it was activated.
                    if (clause.PlaceSpellCountersAsActivated &&
                        SpellCounterSpendersRuntime.PlacedOnActivation(engine, source))
                        break;
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

                // SpellCounterSpenders batch: Mandragola's FLIP, Apprentice Magician's Summon trigger and battle-destroyed
                // face-down Deck summon (queued on the simultaneous-trigger Chain); Armor Exe's upkeep and Pitch-Black
                // Power Stone's move / self-destruction run from their own sites (no-op here).
                case EffectActionKind.PlaceSpellCounterOnEachFaceUpHolder:
                    SpellCounterSpendersRuntime.PlaceOnEachHolder(engine, who, source, clause);
                    break;
                case EffectActionKind.PlaceSpellCounterOnTarget:
                    SpellCounterSpendersRuntime.PlaceOnTarget(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.SpecialSummonLevelRaceFromDeckFaceDownDefense:
                    SpellCounterSpendersRuntime.QueueDeckFaceDownSummon(engine, who, source, clause);
                    break;
                case EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis:
                case EffectActionKind.MoveSpellCounterFromThisToTarget:
                case EffectActionKind.DestroyThisWhenLastSpellCounterRemoved:
                    break;

                case EffectActionKind.PlaceThisOnTopOfDeck:
                    PlaceThisOnTopOfDeck(engine, who, source);
                    break;

                case EffectActionKind.PlaceTargetOnTopOfDeck:
                {
                    if (chosenTarget == null) break;
                    // Owner's Deck, not the controller's — Phoenix Wing Wind Blast
                    // puts an opponent's card back on top of the opponent's Deck.
                    var owner = engine.ControllerOf(chosenTarget) ??
                                (opp != null && opp.Graveyard.Contains(chosenTarget) ? opp : who);
                    if (owner.Graveyard.Contains(chosenTarget) &&
                        ContinuousProtections.GyMovementLocked(engine, source, chosenTarget))
                    {
                        engine.Log("Necrovalley prevents the target from leaving the GY.");
                        break;
                    }

                    if (ContinuousProtections.TargetedEffectBlocked(engine, chosenTarget, source))
                    {
                        engine.Log($"{chosenTarget.Name} cannot be targeted by that effect.");
                        break;
                    }

                    // A card on the field leaves it through the engine (Equips unlinked, a Token vanishes) and goes
                    // on top of its owner's Deck, even while the opponent controls it (Dark Scorpion - Gorg).
                    if (engine.ControllerOf(chosenTarget) != null)
                    {
                        engine.ReturnCardToDeck(chosenTarget, onTop: true);
                        break;
                    }

                    PlaceThisOnTopOfDeck(engine, owner, chosenTarget);
                    break;
                }

                // BattleDestroyedGyRecovery batch (v162): Crimson Sentry / Fairy Guardian, Return of the Doomed, Revival
                // Jam's flag and Standby Summon, Rope of Life.
                case EffectActionKind.PlaceGyTargetOnBottomOfDeck:
                    BattleDestroyedGyRecoveryRuntime.PlaceGyTargetOnBottomOfDeck(engine, who, source, chosenTarget);
                    break;
                case EffectActionKind.AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase:
                    BattleDestroyedGyRecoveryRuntime.RegisterEndPhaseReturn(engine, who, source);
                    break;
                case EffectActionKind.ArmThisSpecialSummonAtNextStandby:
                    BattleDestroyedGyRecoveryRuntime.ArmRevival(engine, who, source, clause);
                    break;
                case EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby:
                    BattleDestroyedGyRecoveryRuntime.OnStandby(engine, who, source);
                    break;
                case EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk:
                    BattleDestroyedGyRecoveryRuntime.ResolveRope(engine, who, source, clause, chosenTarget);
                    break;

                // ContinuousLocksAndIgnitions batch (v163): Timidity and Non Aggression Area register lingering player
                // effects; Arsenal Bug's ATK/DEF (each board refresh), Call of the Mummy / Frontline Base (their face-up
                // ignition link) and Buster Rancher's damage-calculation ATK are read by ContinuousLocksAndIgnitionsRuntime.
                case EffectActionKind.SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase:
                    ContinuousLocksAndIgnitionsRuntime.ApplySetProtection(engine, who, source);
                    break;
                case EffectActionKind.OpponentCannotSummonOrSetDuringNextTurn:
                    ContinuousLocksAndIgnitionsRuntime.ApplySummonLock(engine, who, source);
                    break;
                case EffectActionKind.ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace:
                case EffectActionKind.SpecialSummonFromHandByFaceUpIgnition:
                case EffectActionKind.EquippedGainsAtkInDamageCalcVsStrongBattler:
                    break;

                // PhaseTimersAndDeckTop batch (v163).
                case EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk:
                    PhaseTimersAndDeckTopRuntime.DestroyHighestAtkFaceUpMonsters(engine, who, source);
                    break;
                case EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost:
                    // A continuous check, not a Chain Link: PhaseTimersAndDeckTopRuntime.OnStandbyPhase runs it before
                    // the Standby triggers.
                    break;
                case EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard:
                    PhaseTimersAndDeckTopRuntime.ResolveMirageDraw(engine, who, source, clause);
                    break;
                case EffectActionKind.DiscardRandomRecordedDrawCountAtStandby:
                    PhaseTimersAndDeckTopRuntime.ResolveMirageDiscard(engine, who, source);
                    break;
                case EffectActionKind.BanishDeckCardFaceDownAddAtSecondStandby:
                    PhaseTimersAndDeckTopRuntime.ResolveCapsule(engine, who, source);
                    break;
                case EffectActionKind.AdvanceTurnCountByOne:
                    PhaseTimersAndDeckTopRuntime.ResolvePyroClock(engine, who, source);
                    break;
                case EffectActionKind.BothPlayersDiscardAllMonstersInHand:
                    PhaseTimersAndDeckTopRuntime.ResolveRoyalTribute(engine, who, source);
                    break;

                // CallsDiceAndSetTriggers batch (v163): Dark Coffin, Dice Jar, Reversal Quiz, Reasoning. Ordeal of a
                // Traveler is a trigger fired by BattleWindowsRuntime at the attack declaration, never applied here (the
                // card's activation only places it face-up).
                case EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster:
                    CallsDiceAndSetTriggersRuntime.ResolveDarkCoffin(engine, who, source);
                    break;
                case EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage:
                    CallsDiceAndSetTriggersRuntime.ResolveDiceJar(engine, who, source, clause);
                    break;
                case EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker:
                    break;
                case EffectActionKind.CallTopDeckCardTypeRightExchangesLp:
                    CallsDiceAndSetTriggersRuntime.ResolveReversalQuiz(engine, who, source);
                    break;
                case EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster:
                    CallsDiceAndSetTriggersRuntime.ResolveReasoning(engine, who, source);
                    break;

                // TargetNegationsAndHandQuickEffects batch (v163).
                case EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy:
                    // A marker: TargetNegationsAndHandQuickEffectsRuntime.TrySeatForcedNegations seats the mandatory Chain
                    // Link, and the shared negation link (DuelEngine.ResolveChainStack) applies it.
                    break;
                case EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase:
                    TargetNegationsAndHandQuickEffectsRuntime.RegisterBattleProtection(engine, who, source, clause);
                    break;

                // SummonTriggersAndTributes batch (v163): Byser Shock; King Tiger Wanghu's trigger, the "only 1 face-up"
                // rule and Gravekeeper's Chief's Necrovalley immunity are hooks (SummonTriggersAndTributesRuntime).
                case EffectActionKind.ReturnAllSetCardsOnFieldToHand:
                    SummonTriggersAndTributesRuntime.ReturnAllSetCardsToHand(engine, who, source);
                    break;
                case EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned:
                case EffectActionKind.YouCanOnlyControlOneFaceUpNamed:
                case EffectActionKind.YourGyUnaffectedByNamedCard:
                    break;

                // LastTurn batch (v163).
                case EffectActionKind.SelectedMonsterShowdownLoneMonsterWins:
                    LastTurnRuntime.ResolveShowdown(engine, who, source);
                    break;
                // ForcedActivationAndSubstitution batch (v163): Spirit Elimination, Major Riot.
                case EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn:
                    SpiritEliminationRuntime.Apply(engine, who, source);
                    break;
                case EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown:
                    MajorRiotRuntime.Resolve(engine, who, source, clause);
                    break;

                // PerCopyDeck batch (v163): Parasite Paracide's FLIP, Pharaoh's Treasure's activation, Graverobber. Their
                // "when this card is drawn" clauses run from PerCopyDeckRuntime.NoteDrawn's trigger, not from here.
                case EffectActionKind.PutThisFaceUpInOpponentDeck:
                    PerCopyDeckRuntime.PutThisFaceUpInOpponentDeck(engine, who, source);
                    break;
                case EffectActionKind.ShuffleThisFaceUpIntoYourDeck:
                    PerCopyDeckRuntime.ShuffleThisFaceUpIntoYourDeck(engine, who, source);
                    break;
                case EffectActionKind.AddOpponentGySpellToHandUsableThisTurn:
                    PerCopyDeckRuntime.ResolveGraverobber(engine, who, source, clause, chosenTarget);
                    break;
                case EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace:
                case EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand:
                    break;

                case EffectActionKind.DestroyAllOppSpellTraps:
                {
                    if (opp == null) break;
                    // Lua Duel.Destroy(group): one event (SummonTriggersAndTributes batch, v163: Moisture Creature).
                    FlipMassEffectsRuntime.Simultaneously(engine, () =>
                    {
                        foreach (var st in opp.SpellTrapsOnField().ToList())
                            Destroy(st);
                        if (opp.FieldSpellZone?.Occupant != null)
                            Destroy(opp.FieldSpellZone.Occupant);
                    });
                    break;
                }
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
            // BattleDestroyedGyRecovery (v162): an Extra Deck monster (Lady Panther's Fusion target) goes to the Extra Deck.
            if (card.Def != null && card.Def.IsExtraDeck)
            {
                who.ExtraDeck.Add(card.CardId);
                engine.Log($"{card.Name} returns to the Extra Deck.");
                return;
            }
            who.Deck.Insert(0, card.CardId);
            engine.Log($"{card.Name} is placed on top of the Deck.");
        }

        /// <summary>
        /// Start of the Damage Step, before the face-down flip and before damage
        /// calculation (Sasuke Samurai / Ninja Grandmaster Sasuke / Mystic Swordsman /
        /// Paladin of White Dragon). Returns true when the defender was destroyed, in
        /// which case the caller must stop the battle — there is no damage calculation.
        /// <para>
        /// Official ruling: a face-down target is destroyed WITHOUT being flipped
        /// face-up, so its Flip effect never applies. The caller therefore has to run
        /// this before its own flip step. The destruction is by effect, not by battle,
        /// so it does not satisfy "destroyed a monster by battle" riders (the LV
        /// evolution lines on Mystic Swordsman).
        /// </para>
        /// </summary>
        public static bool TryStartOfDamageStepDestroy(DuelEngine engine, DuelistState who,
            CardInstance attacker, CardInstance defender)
        {
            if (engine == null || who == null || attacker?.Def == null || defender == null)
                return false;
            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = prog.ClausesFor(EffectTiming.StartOfDamageStep);
            if (clauses == null || clauses.Count == 0) return false;

            var isFaceDownDefense = !defender.FaceUp;
            var isFaceUpDefense = defender.FaceUp &&
                                  defender.Position == BattlePosition.Defense;
            foreach (var c in clauses)
            {
                if (c == null || c.Action != EffectActionKind.Destroy) continue;
                if (c.RequiresTargetFaceDownDefense && !isFaceDownDefense) continue;
                if (c.RequiresTargetFaceUpDefense && !isFaceUpDefense) continue;

                var owner = engine.OpponentOf(who);
                if (owner == null || !owner.TryFindMonster(defender, out _)) continue;
                engine.Log(isFaceDownDefense
                    ? $"{attacker.Name}: destroys the face-down Defense Position monster at the start of the Damage Step."
                    : $"{attacker.Name}: destroys {defender.Name} at the start of the Damage Step.");
                engine.DestroyMonsterPublic(owner, defender, attacker);
                return true;
            }

            return false;
        }

        /// <param name="destroyed">
        /// The monster this card just destroyed, already in the GY. Handed to the clause
        /// only when it measures off that monster (Guardian Angel Joan's original ATK,
        /// Absorbing Kid's Level) — every other clause keeps its null target.
        /// </param>
        public static void NotifyDestroyedOpponentByBattle(DuelEngine engine, DuelistState who,
            DuelistState opp, CardInstance attacker, CardInstance destroyed = null)
        {
            if (engine == null || who == null || opp == null || attacker?.Def == null) return;
            // TargetNegationsAndHandQuickEffects batch (v163): Great Dezard counts the monsters it destroys by battle.
            TargetNegationsAndHandQuickEffectsRuntime.NoteBattleDestroying(engine, attacker);
            // SummonPromptsAndCosts batch (v163): Vampire Baby's "destroyed a monster by battle" flag (EVENT_BATTLE_DESTROYING).
            SummonPromptsAndCostsRuntime.NoteBattleDestroying(engine, attacker);
            var prog = CompiledEffectCache.GetOrCompile(attacker);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardDestroysByBattle))
            {
                if (RedirectAndReplaceRuntime.NeedsGyButWasBanished(engine, c, destroyed)) continue;
                // BattleDestroyedTriggers batch: Shinato — the destroyed monster battled in Defense Position.
                if (BattleDestroyedTriggersRuntime.SkipsBattleDestroying(engine, c, attacker, destroyed)) continue;
                // EndOfDamageStepRiders batch: Insect Princess — the destroyed monster battled it as that Type.
                if (EndOfDamageStepRidersRuntime.SkipsBattleDestroying(engine, c, attacker, destroyed)) continue;
                if (BattleOutcomeTriggersRuntime.TryFireBattleDestroying(engine, who, attacker, c, destroyed)) continue;
                // BattleAndPositionRecords batch: Ghost Knight of Jackal (summon the destroyed monster), Legacy Hunter (it was
                // in face-down Defense Position).
                if (BattleAndPositionRecordsRuntime.TryFireBattleDestroying(engine, who, attacker, c, destroyed)) continue;
                var subject = c != null &&
                              (c.AmountFromTargetOriginalAtk || c.ScaleAmountByTargetLevel)
                    ? destroyed
                    : null;
                ApplyClause(engine, who, attacker, c, subject, ref dummy, ref dummy);
            }
            // Union batch (v167): the equipped monster destroyed an opponent's monster by battle — fire each attached
            // Union's EquippedMonsterDestroysByBattle clause (Zombie Tiger: discard 1 random card from their hand).
            // UnionRiders batch (v175): the Unions of the monster that destroyed (it was read off the destroyed monster,
            // whose Equips are already unlinked), and only in their Union state.
            if (destroyed != null && attacker.Equips != null)
            {
                foreach (var eq in attacker.Equips.ToList())
                {
                    if (eq == null || eq.IsNegated || !UnionRidersRuntime.InUnionState(eq)) continue;
                    var eqProg = CompiledEffectCache.GetOrCompile(eq);
                    if (eqProg == null) continue;
                    foreach (var ec in eqProg.ClausesFor(EffectTiming.EquippedMonsterDestroysByBattle))
                    {
                        ApplyClause(engine, who, eq, ec, destroyed, ref dummy, ref dummy);
                    }
                }
            }
        }

        /// <summary>
        /// After battle damage actually reduced LP. Direct-attack-only clauses skip if
        /// there was a battle target. Empty opponent hand is a no-op.
        /// </summary>
        public static void NotifyInflictedBattleDamage(DuelEngine engine, DuelistState who,
            DuelistState opp, CardInstance source, bool wasDirect, int damage = 0)
        {
            if (engine == null || who == null || opp == null || source?.Def == null) return;
            // UnionRiders batch (v175): Burning Beast / Freezing Beast — the equipped monster inflicted battle damage.
            UnionRidersRuntime.FireEquippedMonsterInflictedBattleDamage(engine, who, opp, source);
            var prog = CompiledEffectCache.GetOrCompile(source);
            if (prog == null) return;
            // SpiritMonsters batch (Fushi No Tori): a GainLifePoints clause whose amount is the battle damage just
            // inflicted (AmountIsEquippedBattleDamage) reads it here.
            var damageBefore = s_battleDamageJustInflicted;
            s_battleDamageJustInflicted = damage > 0 ? damage : 0;
            try
            {
                FireInflictedBattleDamage(engine, who, opp, source, wasDirect, prog);
            }
            finally
            {
                s_battleDamageJustInflicted = damageBefore;
            }
        }

        static void FireInflictedBattleDamage(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance source, bool wasDirect, CompiledCardProgram prog)
        {
            var dummy = false;
            var choiceOffered = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage))
            {
                if (c == null) continue;
                if (!source.FaceUp || source.IsNegated) break;
                if (c.AnyControllerMonsterBattleDamage) continue;
                if (c.RequiresDirectAttack && !wasDirect) continue;
                // "You can activate 1 of these effects" (Dark Scorpions): all bullets are one choice, offered once.
                if (EffectChoiceRuntime.IsChoiceBullet(c))
                {
                    if (!choiceOffered)
                        EffectChoiceRuntime.Offer(engine, who, source,
                            prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage));
                    choiceOffered = true;
                    continue;
                }
                // RuleLocks batch: "You can target up to N monsters in their Graveyard; banish those targets." (Kycoo).
                if (RuleLocksRuntime.TryOfferBattleDamageTargets(engine, who, source, c))
                    continue;
                // BattleDamageEvents batch: Aswan Apparition / Nubian Guard — "you can return 1 Trap / Continuous Spell
                // from your GY to the top of your Deck" (an optional targeted trigger: yes/no + pick).
                if (BattleDamageEventsRuntime.TryOfferOptionalTarget(engine, who, source, c))
                    continue;
                ApplyClause(engine, who, source, c, null, ref dummy, ref dummy);
            }

            // Robbin' Goblin: a face-up card of the damage dealer, not the attacker itself.
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                var trapProg = CompiledEffectCache.GetOrCompile(st.Def);
                if (trapProg == null) continue;
                foreach (var c in trapProg.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage))
                {
                    if (c == null || !c.AnyControllerMonsterBattleDamage) continue;
                    if (c.RequiresDirectAttack && !wasDirect) continue;
                    // Cold Wave (LocksAndZones batch): Robbin' Goblin / Poison Fangs are activated Spell/Trap
                    // effects (ygopro TRIGGER_F) and cannot be activated under the lock.
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                    ApplyClause(engine, who, st, c, null, ref dummy, ref dummy);
                }
            }

            // BattleDamageEvents batch: Begone, Knave! / Des Counterblow — any monster's battle damage to either player
            // (activated here; they resolve after battle destruction, BattleDamageEventsRuntime.ResolvePending).
            BattleDamageEventsRuntime.FireAnyMonsterInflicted(engine, source, wasDirect);
        }

        // ── EffectChoiceRuntime: a chosen bullet uses the ordinary target and resolution paths ──

        /// <summary>Legal targets of a choice bullet, collected as for any other targeted trigger.</summary>
        internal static List<CardInstance> CollectChoiceTargets(DuelEngine engine, DuelistState who,
            EffectClause clause, CardInstance source) =>
            clause == null ? new List<CardInstance>() : CollectTargets(engine, who, clause, source);

        /// <summary>RuleLocks batch: legal targets of a targeted trigger clause (Kycoo's battle-damage banish).</summary>
        internal static List<CardInstance> CollectTriggerTargets(DuelEngine engine, DuelistState who,
            EffectClause clause, CardInstance source) =>
            clause == null ? new List<CardInstance>() : CollectTargets(engine, who, clause, source);

        /// <summary>RuleLocks batch: up to <paramref name="n"/> distinct automatic picks (harmful: the opponent's first).</summary>
        internal static List<CardInstance> AutoPickTriggerTargets(DuelEngine engine, DuelistState who,
            EffectClause clause, List<CardInstance> targets, int n) => AutoPickMany(engine, who, clause, targets, n);

        /// <summary>RuleLocks batch: apply a targeted trigger clause to each pick, then the targeting notifications.</summary>
        internal static void ApplyToTriggerTargets(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, List<CardInstance> picks)
        {
            var d = false;
            foreach (var pick in picks)
                ApplyClause(engine, who, source, clause, pick, ref d, ref d);
            NotifyTargetingEffectsResolved(engine, picks, TargetingTruthRuntime.ClauseTargets(clause));
        }

        /// <summary>RuleLocks batch: the picker kind of a targeted clause.</summary>
        internal static EffectTargetKind TargetKindOf(EffectClause clause) => MapTargetKind(clause);

        /// <summary>The AI's pick among <paramref name="targets"/> for a choice bullet.</summary>
        internal static CardInstance AutoPickChoiceTarget(DuelEngine engine, DuelistState who, EffectClause clause,
            List<CardInstance> targets) =>
            clause == null || targets == null || targets.Count == 0 ? null : AutoPick(clause, targets, who, engine);

        /// <summary>Resolve the chosen bullet, with its target when it takes one.</summary>
        internal static void ApplyChosenClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null || clause == null) return;
            var attackNegated = false;
            var battleEnded = false;
            ApplyClause(engine, who, source, clause, target, ref attackNegated, ref battleEnded);
            if (target != null)
                NotifyTargetingEffectResolved(engine, target, TargetingTruthRuntime.ClauseTargets(clause));
        }

        /// <summary>The mill step of a targeted Destroy with <see cref="EffectClause.ThenMillOpponentDeck"/>.</summary>
        static EffectClause ThenMillOpponentDeckClause(EffectClause destroy) => new EffectClause
        {
            Timing = destroy.Timing,
            Action = EffectActionKind.SendFromTopOfDeckToGy,
            Side = EffectSide.Opponent,
            Amount = destroy.ThenMillOpponentDeck,
            SourceSnippet = destroy.SourceSnippet
        };

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
                destroyAttacker, isAttackTarget: false, otherDestroyed: destroyDefender);
            FireAfterDamageFor(engine, defPlayer, defender, attacker, attacker,
                destroyDefender, isAttackTarget: true, otherDestroyed: destroyAttacker);
        }

        static void FireAfterDamageFor(
            DuelEngine engine, DuelistState who, CardInstance self, CardInstance other,
            CardInstance attacker, bool selfDestroyed, bool isAttackTarget, bool otherDestroyed = false)
        {
            if (engine == null || who == null || self?.Def == null) return;
            if (!who.TryFindMonster(self, out _) && !selfDestroyed) return;
            // A monster whose effects are negated (NegationMonsters batch, v161: Ha Des / Twin-Headed Wolf at EVENT_BATTLED)
            // activates none of its after-damage-calculation effects.
            if (self.IsNegated) return;
            var prog = CompiledEffectCache.GetOrCompile(self);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.AfterDamageCalculation))
            {
                if (c == null) continue;
                if (c.RequiresThisDestroyedByBattle && !selfDestroyed) continue;
                if (c.RequiresThisIsAttackTarget && !isAttackTarget) continue;
                // BattleDestroyedTriggers batch (Kelbek, ygopro EVENT_BATTLED): the battled monster is not destroyed by this
                // battle (not STATUS_BATTLE_DESTROYED).
                if (c.RequiresBattledMonsterNotDestroyed && otherDestroyed) continue;
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
            // DamageStepRules batch: Continuous Destruction Punch (a continuous effect, before the triggers below).
            DamageStepRulesRuntime.FireEndOfDamageStep(engine, atkPlayer, defPlayer, attacker, defender);
            FireEndOfDamageFor(engine, atkPlayer, attacker, defender, selfIsAttacker: true);
            FireEndOfDamageFor(engine, defPlayer, defender, attacker, selfIsAttacker: false);
        }

        static void FireEndOfDamageFor(
            DuelEngine engine, DuelistState who, CardInstance self, CardInstance other, bool selfIsAttacker)
        {
            if (engine == null || who == null || self?.Def == null) return;
            // EndOfDamageStepRiders batch: a "battled" trigger of a card destroyed by this battle still activates from the
            // GY (ygopro aux.dsercon: IsRelateToBattle or STATUS_BATTLE_DESTROYED); the GY's holder controls it.
            var onField = who.TryFindMonster(self, out _);
            if (!onField && !EndOfDamageStepRidersRuntime.DestroyedByThisBattle(engine, self, other)) return;
            if (!onField) who = EndOfDamageStepRidersRuntime.HolderOffField(engine, self) ?? who;
            var prog = CompiledEffectCache.GetOrCompile(self);
            if (prog == null) return;
            var dummy = false;
            foreach (var c in prog.ClausesFor(EffectTiming.EndOfDamageStep))
            {
                if (c == null) continue;
                if (!onField && !EndOfDamageStepRidersRuntime.ActivatesAfterBattleDestroyed(c)) continue;
                // BattleBoosts batch (v161): "If this card attacks" (Spear Dragon) — the attacker only.
                if (c.DamageStepOnlyWhenAttacking && !selfIsAttacker) continue;
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

        /// <summary>
        /// Resolve one untargeted trigger clause of <paramref name="source"/> for <paramref name="who"/>
        /// (DiscardTriggers batch v142: a card discarded or milled by the opponent's card effect, fired by
        /// <see cref="DiscardTriggersRuntime"/>).
        /// </summary>
        internal static void ApplyTriggeredClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            var attackNegated = false;
            var battleEnded = false;
            ApplyClause(engine, who, source, clause, null, ref attackNegated, ref battleEnded);
        }

        internal static void DiscardRandomFromOpponentHand(DuelEngine engine,
            DuelistState opp, CardInstance source, EffectClause clause)
        {
            if (engine == null || opp?.Hand == null) return;
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 1;
            var discarded = 0;
            // DiscardTriggers (v142): discards by the effect of a card the victim's opponent controls.
            var by = engine.OpponentOf(opp);
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                while (discarded < n && opp.Hand.Count > 0)
                {
                    // PickIndex: the same stream draw as Next(count), but test-queued picks come first.
                    var i = engine.Rng.PickIndex(opp.Hand.Count);
                    if (i < 0 || i >= opp.Hand.Count) i = 0;
                    var pick = opp.Hand[i];
                    var gone = MonsterEffects.DiscardFromHandByInstance(opp, pick);
                    if (gone == null) break;
                    discarded++;
                    var src = source != null ? source.Name : "Effect";
                    engine.Log($"{src}: {opp.Name} discards {gone.Name} at random.");
                    DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, by);
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            if (discarded == 0 && source != null)
                engine.Log($"{source.Name}: {opp.Name} has no cards in hand to discard.");
        }

        /// <summary>
        /// A monster inflicted battle damage to <paramref name="victim"/>. Fires that monster's
        /// "When this card inflicts Battle Damage" clauses and its controller's face-up
        /// "Each time a monster you control inflicts Battle Damage" Continuous cards.
        /// </summary>
        /// <summary>Battle damage from the calculation that just applied, for a clause whose amount is that damage.</summary>
        static int s_battleDamageJustInflicted;

        public static void NotifyBattleDamageInflicted(DuelEngine engine, DuelistState controller,
            CardInstance monster, DuelistState victim, int damage = 0)
        {
            if (engine == null || controller == null || monster?.Def == null || victim == null) return;
            if (victim == controller) return;
            // The Secret of the Bandit (BattleRules batch): the selected monster's battle damage makes its opponent discard.
            BattleRulesRuntime.OnBattleDamage(engine, monster, victim);
            var dummy = false;
            // The monster's own "When this card inflicts battle damage" clauses (the timing both
            // lineages added under the same name) fire once, from NotifyInflictedBattleDamage.

            s_battleDamageJustInflicted = damage > 0 ? damage : 0;
            try
            {
                foreach (var st in controller.SpellTrapsOnField().ToList())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    var sp = CompiledEffectCache.GetOrCompile(st);
                    if (sp == null) continue;
                    foreach (var c in sp.ClausesFor(EffectTiming.YourMonsterInflictsBattleDamage))
                    {
                        // Cold Wave (LocksAndZones batch): an activated Spell/Trap trigger (ygopro TRIGGER_F).
                        if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                        // "If it inflicts battle damage": the equipped monster, not any monster this player controls.
                        if (c.OnlyEquippedMonster && !ReferenceEquals(st.EquippedTo, monster)) continue;
                        ApplyClause(engine, controller, st, c, null, ref dummy, ref dummy);
                    }
                }
            }
            finally
            {
                s_battleDamageJustInflicted = 0;
            }
        }

        // ───────────────────── Classic-era hooks (PSV tranche 1) ─────────────────────

        /// <summary>Solemn Wishes: once per draw (not per card), for the drawing player's face-up cards.
        /// Also records the Draw Phase normal-draw card for Drop Off (DiscardJustDrawnCard).</summary>
        public static void NotifyDraw(DuelEngine engine, DuelistState drawer)
        {
            if (engine == null || drawer == null || engine.GameOver) return;

            // Drop Off: remember the card just added for a Draw Phase normal draw (turn player, Phase=Draw).
            // Effect draws outside the Draw Phase / non-turn-player draws are fail-closed (not remembered).
            if (engine.Phase == DuelPhase.Draw && engine.TurnPlayer == drawer &&
                drawer.Hand != null && drawer.Hand.Count > 0)
            {
                RememberNormalDrawCard(drawer, drawer.Hand[drawer.Hand.Count - 1]);
            }

            var dummy = false;
            foreach (var card in drawer.SpellTrapsOnField().Concat(drawer.MonstersOnField()).ToList())
            {
                if (card?.Def == null || !card.FaceUp || card.IsNegated) continue;
                var prog = CompiledEffectCache.GetOrCompile(card);
                if (prog == null) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ControllerDraws))
                {
                    // Cold Wave (LocksAndZones batch): a Spell/Trap draw effect that makes a Chain Link is an
                    // activated trigger and cannot be activated under the lock; a continuous one still applies.
                    if (!card.Def.IsMonster && c != null && c.MakesChainLink &&
                        LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card))
                        continue;
                    ApplyClause(engine, drawer, card, c, null, ref dummy, ref dummy);
                }
            }

            // Heart of the Underdog: your Draw Phase normal draw of a Normal Monster → optional reveal + draw.
            // Fail-closed vs opp draw / Effect Monster / any-draw outside Draw Phase normal draw.
            if (engine.Phase == DuelPhase.Draw && engine.TurnPlayer == drawer &&
                _lastNormalDrawCard?.Def != null && _lastNormalDrawOwner == drawer &&
                _lastNormalDrawCard.Def.IsNormalMonster)
            {
                foreach (var card in drawer.SpellTrapsOnField().ToList())
                {
                    if (card?.Def == null || !card.FaceUp || card.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(card);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ControllerNormalDrawInDrawPhase))
                    {
                        // Cold Wave (LocksAndZones batch): Heart of the Underdog's draw is an activated Spell
                        // effect (ygopro TRIGGER_O) and cannot be activated under the lock.
                        if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) continue;
                        // Soft/AI: auto-opt-in for optional "You can".
                        ApplyClause(engine, drawer, card, c, null, ref dummy, ref dummy);
                    }
                }
            }
        }

        /// <summary>
        /// Re-entrancy guard for Remove Brainwashing mass return (TryTakeControl → Notify → snap).
        /// </summary>
        static int _returnControlNotifyDepth;

        /// <summary>Ameba / Griggle: control of this face-up card changed (once while face-up).</summary>
        public static void NotifyControlChanged(DuelEngine engine, DuelistState newController,
            CardInstance card)
        {
            if (engine == null || newController == null || card?.Def == null) return;
            if (card.FaceUp && !card.IsNegated && !card.EffectUsedWhileFaceUp)
            {
                var prog = CompiledEffectCache.GetOrCompile(card);
                if (prog != null)
                {
                    var clauses = prog.ClausesFor(EffectTiming.ThisCardControlChanged);
                    if (clauses.Count > 0)
                    {
                        var dummy = false;
                        foreach (var c in clauses)
                            ApplyClause(engine, newController, card, c, null, ref dummy, ref dummy);
                        if (clauses.Exists(c => c.OnceWhileFaceUp))
                            card.EffectUsedWhileFaceUp = true;
                    }
                }
            }

            // Remove Brainwashing: while face-up Continuous, snap control back to owners.
            if (AnyFaceUpReturnControlToOwners(engine))
                ApplyReturnControlToOwnersMass(engine, startsApplying: false);
        }

        /// <summary>
        /// Remove Brainwashing: apply the snap-back again when a turn-scoped negation of it has just
        /// ended (TurnNegationTraps batch, v141) — control changes made while it was negated revert.
        /// </summary>
        public static void ReapplyReturnControlToOwners(DuelEngine engine)
        {
            // Remove Brainwashing starts applying again (ygopro resets EFFECT_SET_CONTROL on the
            // transition, whatever caused it): same as its activation (TakeControl batch).
            if (AnyFaceUpReturnControlToOwners(engine))
                ApplyReturnControlToOwnersMass(engine, startsApplying: true);
        }

        /// <summary>True while a face-up, non-negated Remove Brainwashing applies.</summary>
        public static bool ReturnControlToOwnersApplies(DuelEngine engine) =>
            AnyFaceUpReturnControlToOwners(engine);

        /// <summary>
        /// Remove Brainwashing has just stopped applying (negated for the turn, TurnNegationTraps batch,
        /// v141): a face-up control Equip Card that is still equipped (Snatch Steal: EquipThisToTarget +
        /// TakeControlOfTarget) takes control of its monster again for its controller, since its
        /// continuous control effect is no longer overridden. Equip effects negated by The Emperor's
        /// Holiday do not.
        /// </summary>
        public static void ReapplyEquipTakeControl(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || AnyFaceUpReturnControlToOwners(engine)) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m?.Equips == null || m.Equips.Count == 0) continue;
                    foreach (var eq in m.Equips.ToList())
                    {
                        if (eq?.Def == null || !eq.FaceUp || eq.IsNegated) continue;
                        if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq)) continue;
                        var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                        if (prog == null || !prog.ClauseList.Exists(c => c != null && c.TakeControlOfTarget &&
                                c.Action == EffectActionKind.EquipThisToTarget))
                            continue;
                        var taker = engine.ControllerOf(eq);
                        if (taker == null || !taker.TryFindSpellTrap(eq, out _)) continue;
                        if (engine.ControllerOf(m) == taker) break;
                        m.TakenByEquipControl = true;
                        if (!engine.TryTakeControl(taker, m))
                        {
                            m.TakenByEquipControl = false;
                            engine.Log($"{eq.Name}: take control failed — no zone.");
                        }

                        break;
                    }
                }
            }
        }

        /// <summary>
        /// True if any face-up non-negated S/T has ContinuousWhileFaceUp ReturnControlToOwners
        /// (Remove Brainwashing).
        /// </summary>
        internal static bool AnyFaceUpReturnControlToOwners(DuelEngine engine)
        {
            if (engine == null) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(st.Def);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    {
                        if (c != null && c.Action == EffectActionKind.ReturnControlToOwners)
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Mass ControlOwner→owner via existing TryTakeControl. Used on Activate resolve and
        /// Continuous snap-back. Depth-guarded against NotifyControlChanged re-entry.
        /// Soft residual OK face-down / Token / zone-full.
        /// <paramref name="startsApplying"/>: Remove Brainwashing starts to apply (its activation). As in
        /// ygopro, the control effects on every monster its owner now holds end for good (a Change of Heart
        /// End Phase return, a Charmer's control). A Charmer take made later, while it applies, only waits
        /// on it (TakeControlRuntime ControlBinding.Suspended). ControlOwner null means the owner controls
        /// the monster already: there is nothing to return.
        /// </summary>
        static void ApplyReturnControlToOwnersMass(DuelEngine engine, bool startsApplying)
        {
            if (engine == null || engine.GameOver) return;
            if (_returnControlNotifyDepth > 0) return;
            _returnControlNotifyDepth++;
            try
            {
                var returned = false;
                foreach (var side in new[] { engine.Player, engine.Opponent })
                {
                    if (side == null) continue;
                    foreach (var m in side.MonstersOnField().ToList())
                    {
                        if (m == null) continue;
                        var owner = m.ControlOwner;
                        if (owner == null)
                        {
                            if (startsApplying)
                                EndControlEffects(m);
                            continue;
                        }
                        var current = engine.ControllerOf(m);
                        if (current == null || current == owner) continue;
                        if (!engine.TryTakeControl(owner, m))
                        {
                            engine.Log($"{m.Name}: no Monster Zone to return control — left in place.");
                            continue;
                        }
                        if (startsApplying || m.ControlBinding == null || m.ControlBinding.Source != m.ControlBoundTo)
                            EndControlEffects(m);
                        else
                        {
                            // Snap-back of a Charmer's monster: its control waits on Remove Brainwashing.
                            m.ControlRevertsAtEndPhase = false;
                            m.ReturnControlAtEndOfTurn = -1;
                            m.TakenByEquipControl = false;
                            m.ControlBinding.Suspended = true;
                        }
                        returned = true;
                    }
                }
                if (returned)
                    engine.Log("The control of all monsters on the field returns to the owners.");
            }
            finally
            {
                _returnControlNotifyDepth--;
            }
        }

        /// <summary>The control effects on a monster its owner holds end (Remove Brainwashing).</summary>
        static void EndControlEffects(CardInstance m)
        {
            m.ControlRevertsAtEndPhase = false;
            m.ReturnControlAtEndOfTurn = -1;
            m.TakenByEquipControl = false;
            m.ControlBoundTo = null;
            m.ControlBinding = null;
        }

        /// <summary>Mad Sword Beast (self) / Fairy Meteor Crush (equip) piercing.</summary>
        public static bool GrantsPiercing(CardInstance attacker)
        {
            if (attacker == null) return false;
            if (attacker.FaceUp && !attacker.IsNegated &&
                FaceUpClauses(attacker, EffectActionKind.ContinuousPiercing).Any())
                return true;
            if (attacker.Equips != null)
                foreach (var eq in attacker.Equips)
                    if (FaceUpClauses(eq, EffectActionKind.EquippedGainsPiercing).Any())
                        return true;
            return false;
        }

        /// <summary>Face-up-only bookkeeping ends when a monster is turned face-down.</summary>
        public static void ClearFaceUpMarks(CardInstance m)
        {
            if (m == null) return;
            ClearFaceDownMarks(m);
            m.AttackLockedBy = null; // turned face-down: the Invitation lock ends
        }

        /// <summary>
        /// What a face-down monster never keeps: once-while-face-up limits, the summoned-this-turn
        /// mark, End Phase self-destruction and until-end-of-turn stats (Limiter Removal + Book of Moon).
        /// An attack lock stays: Invitation to a Dark Sleep may target a monster already Set.
        /// </summary>
        static void ClearFaceDownMarks(CardInstance m)
        {
            m.EffectUsedWhileFaceUp = false;
            m.NormalOrFlipSummonedTurn = -1;
            m.TempDestroyOnEndOfTurn = -1;
            m.UntilEndOfTurnAtk = 0;
            m.UntilEndOfTurnDef = 0;
        }

        /// <summary>
        /// State checks run on every board refresh: Gearfried destroys Equip Cards attached to it;
        /// face-down monsters lose face-up-only marks; attack locks end with their source.
        /// </summary>
        public static void SweepClassicStateChecks(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m == null) continue;
                    if (m.AttackLockedBy != null &&
                        (!m.AttackLockedBy.FaceUp || engine.ControllerOf(m.AttackLockedBy) == null))
                        m.AttackLockedBy = null;
                    if (!m.FaceUp)
                    {
                        ClearFaceDownMarks(m);
                        DestroyEquipsOfFaceDownHost(engine, side, m);
                        continue;
                    }

                    if (m.Equips == null || m.Equips.Count == 0 || m.IsNegated) continue;
                    if (!FaceUpClauses(m, EffectActionKind.DestroyEquipsAttachedToThis).Any()) continue;
                    foreach (var eq in m.Equips.ToList())
                    {
                        if (!IsRealEquipCard(eq)) continue;
                        engine.Log($"{m.Name} destroys the Equip Card {eq.Name}.");
                        DestroyCard(engine, eq, m);
                    }
                }
        }

        /// <summary>
        /// An Equip Card cannot stay equipped to a face-down monster: when its monster is turned face-down (Book of
        /// Moon, a Set by effect) every Equip Card equipped to it is destroyed (ygopro adjust "equip check": target
        /// not face-up → destroy, REASON_RULE, so no "unaffected" or replacement check). The same GY path as a host
        /// leaving the field (<c>DuelEngine.UnlinkEquips</c>), so a Premature Burial-style "when this card is
        /// destroyed" still applies. Link cards that are not Equip Cards (Call of the Haunted, Spellbinding Circle)
        /// stay. SpiritsAndMisc batch: Kiseitai's gain must not follow a host turned face-down.
        /// </summary>
        static void DestroyEquipsOfFaceDownHost(DuelEngine engine, DuelistState side, CardInstance host)
        {
            if (engine == null || host?.Equips == null || host.Equips.Count == 0) return;
            foreach (var eq in host.Equips.ToList())
            {
                if (eq == null || eq.EquippedTo != host || !IsRealEquipCard(eq)) continue;
                engine.Log($"{host.Name} is face-down — its Equip Card {eq.Name} is destroyed.");
                // An absorbed monster (Relinquished) sits in no zone; it came from the host controller's opponent,
                // as DuelEngine.TryDestroyEquipInsteadOfBattle assumes.
                var holder = engine.ControllerOf(eq) ??
                             (eq.AbsorbedBy == host ? engine.OpponentOf(side) ?? side : side);
                engine.SendCardToGrave(holder, eq);
            }
        }

        /// <summary>
        /// The monster a Relinquished-family host equipped to itself with its own "equip that target to this card"
        /// effect (<see cref="CardInstance.AbsorbedBy"/>), or null. Other Equip Cards on that host (an Equip Spell,
        /// Kiseitai) never count: ygopro c64631466 filters its equip group by its own flag (s.eqfilter).
        /// </summary>
        public static CardInstance AbsorbedEquipOf(CardInstance host)
        {
            if (host?.Equips == null) return null;
            foreach (var eq in host.Equips)
                if (eq != null && eq.AbsorbedBy == host && eq.EquippedTo == host)
                    return eq;
            return null;
        }

        /// <summary>
        /// An Equip Card (Equip Spell, a card that equips itself such as Battle-Scarred, or an
        /// equipped monster) — not a link recorded in Equips (Call of the Haunted, Spellbinding Circle).
        /// </summary>
        public static bool IsRealEquipCard(CardInstance eq)
        {
            if (eq?.Def == null) return false;
            if (eq.Def.IsEquipSpell || eq.Def.IsMonster) return true;
            var prog = CompiledEffectCache.GetOrCompile(eq.Def);
            return prog != null && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.EquipThisToTarget);
        }

        /// <summary>Nobleman of Extermination: banish every copy of a Trap from both Decks.</summary>
        static void PurgeNameFromBothDecks(DuelEngine engine, string name, CardInstance source)
        {
            if (engine?.Database == null || string.IsNullOrEmpty(name)) return;
            // ChaosBanishSummons batch: both Decks' copies are banished at the same time (one EVENT_REMOVE).
            using var purge = BanishEvents.Group(engine);
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.Deck == null) continue;
                var n = 0;
                for (var i = side.Deck.Count - 1; i >= 0; i--)
                {
                    if (!engine.Database.TryGet(side.Deck[i], out var d) ||
                        !string.Equals(d.name, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var inst = engine.CreateCardInstance(side.Deck[i]);
                    // PerCopyDeck batch (v163): a copy another player owns is banished to its owner's pile.
                    PerCopyDeckRuntime.TakeFromDeck(side, i, inst);
                    inst.FaceUp = true;
                    inst.BanishedTurnNumber = engine.TurnNumber;
                    PerCopyDeckRuntime.OwnersPile(side, inst).Banished.Add(inst);
                    BanishEvents.Record(engine, inst);
                    n++;
                }

                engine.Rng.Shuffle(side.Deck);
                engine.Log($"{source?.Name}: {side.Name} reveals the Deck — {n} \"{name}\" banished.");
            }
        }

        // ───────────────────── Classic-era activation conditions (PSV tranche 1) ─────────────────────

        /// <summary>A monster with an effect: not Normal, not an effectless Fusion / Ritual (Ceasefire, Bombardment Beetle).</summary>
        public static bool IsTrueEffectMonster(CardDef def) =>
            def != null && def.IsMonster && !Rules.OfficialCardAuthority.HasNoActivatableEffect(def);

        /// <summary>Printed name, or a name the card's text says it is always treated as (Harpie Lady 1).</summary>
        static bool DefHasRulesName(CardDef def, string name)
        {
            if (def == null || string.IsNullOrEmpty(name)) return false;
            if (string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase)) return true;
            var prog = CompiledEffectCache.GetOrCompile(def);
            return prog != null && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.AlwaysTreatedAsName &&
                string.Equals(c.TreatedAsName, name, StringComparison.OrdinalIgnoreCase));
        }

        static bool DefMatchesAnyName(CardDef def, string names)
        {
            if (def == null || string.IsNullOrEmpty(names)) return false;
            foreach (var n in names.Split('|'))
                if (DefHasRulesName(def, n.Trim())) return true;
            return false;
        }

        /// <summary>
        /// Activation-only conditions on any clause of the card (checked when the card is activated,
        /// never re-checked at resolution).
        /// </summary>
        static bool ActivationConditionsMet(DuelEngine engine, DuelistState who, CardInstance card,
            CompiledCardProgram prog, out string reason)
        {
            reason = null;
            if (prog == null) return true;
            var opp = engine.OpponentOf(who);
            foreach (var c in prog.ClauseList)
            {
                if (c == null || string.IsNullOrEmpty(c.ActivationCondition)) continue;
                bool ok;
                switch (c.ActivationCondition)
                {
                    case "AnyMonsterOnField":
                        ok = who.MonsterCount + opp.MonsterCount > 0;
                        reason = "Needs a monster on the field.";
                        break;
                    case "OppLpAtMost":
                        ok = opp.LifePoints <= c.ConditionAmount;
                        reason = $"Only while your opponent has {c.ConditionAmount} or less LP.";
                        break;
                    case PoisonMeteorCestusRuntime.OppLpGreaterThan:
                        // "higher than N": strictly greater, not "or more".
                        ok = opp != null && opp.LifePoints > c.ConditionAmount;
                        reason = $"Only while your opponent has more than {c.ConditionAmount} LP.";
                        break;
                    case "YourGyMonstersAtLeast":
                        ok = who.Graveyard.Count(g => g?.Def != null && g.Def.IsMonster) >= c.ConditionAmount;
                        reason = $"Needs {c.ConditionAmount} or more monsters in your GY.";
                        break;
                    case "FaceDownDefOrFaceUpEffect":
                        ok = new[] { who, opp }.Any(side => side.MonstersOnField().Any(m =>
                            m != null && ((!m.FaceUp && m.Position == BattlePosition.Defense) ||
                                          (m.FaceUp && !m.IsToken && IsTrueEffectMonster(m.Def)))));
                        reason = "Needs a face-down Defense Position monster or an Effect Monster on the field.";
                        break;
                    case "YouControlFaceUpRace":
                        ok = who.MonstersOnField().Any(m =>
                            m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter));
                        reason = $"You must control a face-up {c.RaceFilter} monster.";
                        break;
                    case PoisonMeteorCestusRuntime.YouControlRace:
                    {
                        // "you control a Dragon monster": face-up only (Stamping Destruction c81385346 checks
                        // aux.FaceupFilter(Card.IsRace)); a face-down monster has no Type.
                        // The Type is ConditionName, not RaceFilter: RaceFilter would also filter the Spell/Trap target.
                        var race = string.IsNullOrEmpty(c.ConditionName) ? c.RaceFilter : c.ConditionName;
                        ok = who.MonstersOnField().Any(m =>
                            m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, race));
                        reason = $"You must control a {race} monster.";
                        break;
                    }
                    case "FaceUpNamedMonster":
                        ok = new[] { who, opp }.Any(side => side.MonstersOnField().Any(m =>
                            m != null && m.FaceUp && (m.IsNamed(c.ConditionName) || DefHasRulesName(m.Def, c.ConditionName))));
                        reason = $"Needs a face-up \"{c.ConditionName}\" on the field.";
                        break;
                    case "OppMonsterLeadAtLeast":
                        ok = opp.MonsterCount - who.MonsterCount >= c.ConditionAmount &&
                             engine.FirstEmpty(who.MonsterZones) >= 0;
                        reason = $"Your opponent must control at least {c.ConditionAmount} more monsters than you.";
                        break;
                    case "AnyOtherCardOnField":
                        ok = CollectAllOtherCards(engine, who, card).Count > 0;
                        reason = "Needs another card on the field.";
                        break;
                    case "HandHasOtherCard":
                        ok = who.Hand.Any(h => h != null && h != card);
                        reason = "Needs another card in your hand.";
                        break;
                    case "OppEffectWouldDestroyYourTrap":
                    {
                        // Soft residual Fake Trap: opponent Spell/Trap/Effect Monster on
                        // the chain would destroy a Trap you control. Fail-closed vs
                        // banish/send/return. Soft ≠ hard. Prefer NO DE rewrite.
                        var link = engine.PendingResponse?.ChainTarget ?? engine.Chain?.Last;
                        ok = ContinuousProtections.OppEffectWouldDestroyYourTrap(engine, who, link);
                        reason = "Only when your opponent activates an effect that would destroy a Trap Card(s) you control.";
                        break;
                    }
                    case DrawThenDiscardRuntime.CanDrawAmount:
                        // DrawThenDiscard batch (Graceful Charity): ygopro IsPlayerCanDraw(tp, N).
                        ok = DrawThenDiscardRuntime.CanDraw(engine, who, c.Amount);
                        reason = $"You must be able to draw {Mathf.Max(1, c.Amount)} card(s).";
                        break;
                    case TurnNegationTrapsRuntime.ChainLinkActivatesCardFamily:
                        // TurnNegationTraps batch (v141): chained to the activation of a card of that family.
                        ok = TurnNegationTrapsRuntime.AnsweredLinkActivatesFamily(engine, who, c.ConditionName);
                        reason = "Only in response to the activation of a card of that kind.";
                        break;
                    case SpellTrapDestroyedResponseRuntime.YourCardsDestroyedByOpponentEffect:
                        // Gust / Driving Snow (SpellTrapDestroyedResponse, v142): only in the window right after
                        // 1+ of your Spell / Trap Cards were destroyed by an opponent's card effect.
                        ok = SpellTrapDestroyedResponseRuntime.WindowAnswers(engine, who, c.ConditionName);
                        reason = $"Only when 1 or more of your {c.ConditionName} Cards are destroyed and sent from the " +
                                 "field to the GY by a card effect your opponent controls.";
                        break;
                    case RedirectAndReplaceRuntime.OpponentSpellTargetsOneMonster:
                    case RedirectAndReplaceRuntime.OpponentTargetsOneMonsterYouControl:
                    case RedirectAndReplaceRuntime.OpponentSpellTargetsOneSpellTrap: // SpellEventsAndHandSendTriggers batch
                        // RedirectAndReplace batch (v142): Fairy's Hand Mirror / Shift answer a targeting link (or an attack).
                        ok = RedirectAndReplaceRuntime.ActivationWindowOpen(engine, who, c, out reason);
                        break;
                    case BattleWindowsRuntime.MonsterSentFromFieldToYourGy:
                        // BattleWindows batch: Michizure — a monster was just sent from the field to your GY.
                        ok = BattleWindowsRuntime.MonsterSentWindowOpen(engine, who, out reason);
                        break;
                    case DiscardEvents.YouDiscardedFromHand:
                        // EngineHygiene batch: Forced Requisition — you just discarded from your hand.
                        ok = DiscardEvents.DiscardWindowOpen(engine, who, out reason);
                        break;
                    case EquipModelRuntime.MonsterEquippedWithEquipCard:
                        // EquipModel batch: Armored Glass — a monster was just equipped with an Equip Card.
                        ok = EquipModelRuntime.EquipWindowOpen(engine, who, out reason);
                        break;
                    case MajorRiotRuntime.YourMonstersReturnedToHandByOpponent:
                        // ForcedActivationAndSubstitution batch (v163): Major Riot — your monster(s) were just returned
                        // to your hand by your opponent's effect, and a monster is on the field.
                        ok = MajorRiotRuntime.WindowOpen(engine, who, out reason);
                        break;
                    case BattleWindowsRuntime.AttackerAtkBelowYourDefenderDefAtEndOfDamageStep:
                        // BattleWindows batch: Destruction Punch — the end of a Damage Step the attacker survived.
                        ok = BattleWindowsRuntime.DestructionPunchWindowOpen(engine, who, out reason);
                        break;
                    default:
                        // PerCountBurns (v141): OppBanishedAtLeast / OppGyAtLeast / YouControlMonstersAtLeast.
                        if (PerCountBurnsRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // TypeDeclaration batch (v142): Earthshaker needs 2+ Attributes among the face-up monsters.
                        if (TypeDeclarationRuntime.TryActivationCondition(engine, c, out ok, out reason))
                            break;
                        // EquipFamily batch: FaceUpEquipCardOnField (Collected Power).
                        if (EquipFamilyRuntime.TryActivationCondition(engine, c, out ok, out reason))
                            break;
                        // LocksAndZones (v142): StartOfMainPhase1 (Cold Wave).
                        if (LocksAndZonesRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // HandDisruption (v142): HandSizes / OpponentDrewOutsideDrawPhase / ControllerDeckAtLeast.
                        if (HandDisruptionRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // PositionLocks Soft: SetInYourStandbyPhase (Curse of Fiend).
                        if (PositionLocksRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // TargetedClassics Soft: YourFaceUpBanishedMonstersAtLeast (Miracle Dig).
                        if (TargetedClassicsRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // RuleLocks: SpecialSummonedMonsterOnField (Jowgen the Spiritualist).
                        if (RuleLocksRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // NegationsAndGySummons batch: ChosenOneHand (Chosen One).
                        if (NegationsAndGySummonsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // SimpleSpellTraps batch: A Wingbeat of Giant Dragon, Fiend Comedian, Emergency Provisions.
                        if (SimpleSpellTrapsRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // DeckReturnsAndMassEffects batch (v161): Creature Swap, Bubble Crash.
                        if (DeckReturnsAndMassEffectsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // BattleBoosts batch (v161): Gray Wing — Main Phase 1 of a turn with a Battle Phase, no extra attack yet.
                        if (BattleBoostsRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // GyHandAndSummonEventTriggers batch: Beckoning Light — a card in your hand (not this card)
                        // and at least that many of the Attribute in your GY.
                        if (GyHandAndSummonEventTriggersRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // LvChain batch: the LV progression clause's "not the turn this card was summoned" rider.
                        if (LvChainRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // PgdSpellTrapBasics batch (v162): D. Tribe / Pyramid Energy, Curse of Aging, Needle Ceiling.
                        if (PgdSpellTrapBasicsRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // BattleDestroyedGyRecovery batch (v162): Rope of Life — a monster destroyed by battle and sent
                        // to your GY, in its window, with a hand to discard and a Monster Zone to Summon to.
                        if (BattleDestroyedGyRecoveryRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // PhaseTimersAndDeckTop batch (v163): Pyro Clock of Destiny (a turn count exists), Royal Tribute
                        // (a card other than this one in either hand).
                        if (PhaseTimersAndDeckTopRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // CallsDiceAndSetTriggers batch (v163): Reversal Quiz's cost cards, Reasoning's excavation.
                        if (CallsDiceAndSetTriggersRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // TargetNegationsAndHandQuickEffects batch (v163): Curse of Royal, Gravekeeper's Watcher (the answered
                        // Chain Link), Great Dezard's ignition (monsters destroyed by battle).
                        if (TargetNegationsAndHandQuickEffectsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // LastTurn batch (v163): Last Turn — your opponent's turn, your LP N or less (and a monster to select).
                        if (LastTurnRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // DamageStepRules batch: Meteorain — only during your turn.
                        if (DamageStepRulesRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // SpellCounterSpenders batch: Mega Ton Magical Cannon / Legendary Flame Lord's "something to destroy".
                        if (SpellCounterSpendersRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // FlipMassAndOpponentPicks batch: Huge Revolution (your Main Phase, named monsters face-up),
                        // Pineapple Blast (your own Normal Summon, the opponent controls more monsters).
                        if (FlipMassAndOpponentPicksRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // PhaseTriggersAndIgnitionCosts batch: Magical Scientist's Extra Deck Fusion, Physical Double's Token.
                        if (PhaseTriggersAndIgnitionCostsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // UnionRiders batch (v175): Union Rider — no Union equipped by its effect, a free S/T Zone.
                        if (UnionRidersRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // SimpleTriggersAndSwarm batch: Dark Scorpion Combination (every named monster face-up on your field),
                        // Mustering of the Dark Scorpions (a monster in hand it can Special Summon, and a free zone).
                        if (SimpleTriggersAndSwarmRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // DcrSpellTrapUtility batch: Frozen Soul's LP gap, Arsenal Robber's Deck, Dragged Down's hands.
                        if (DcrSpellTrapUtilityRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // NamedSummonPairs batch: A Deal with Dark Ruler (a Level 8+ monster you controlled went to the GY
                        // this turn), Contract with Exodia (the 5 named cards in your GY).
                        if (NamedSummonPairsRuntime.TryActivationCondition(engine, who, c, out ok, out reason))
                            break;
                        // SpellTrapAnswersAndActivationLocks batch: Sasuke Samurai #2's player flag (once per turn per player).
                        if (SpellTrapAnswersAndActivationLocksRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // ConditionalSpellsAndTraps batch: Blasting the Ruins, Chaos End, Chaos Greed, Ojama Delta
                        // Hurricane!!, Gryphon's Feather Duster.
                        if (ConditionalSpellsAndTrapsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // RecoveryAndPhaseCosts batch: Dimension Distortion (empty GY), Dimension Fusion (a summonable banished
                        // monster on either side), Reload (another card in the hand).
                        if (RecoveryAndPhaseCostsRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // TokensDeclarationsAndPicks batch: Token zones + summon record (Stray Lambs, Scapegoat, Multiplication
                        // of Ants); A Hero Emerges' summonable hand monster.
                        if (TokensDeclarationsAndPicksRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // ChaosBanishSummons batch: no attack declared by this card (Chaos ignition), a named card face-up
                        // on either field (Primal Seed), a banishable Spell in your GY (Chaosrider Gustaph).
                        if (ChaosBanishSummonsRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // BattleAndPositionRecords batch: Curse of Anubis (a face-up Attack Position Effect Monster).
                        if (BattleAndPositionRecordsRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        // IgnitionCostsAndSimpleSpells batch: 3-Hump Lacooda's face-up copies, Lady Ninja Yae's opponent
                        // Spell/Trap, Solar Ray's face-up LIGHT monster.
                        if (IgnitionCostsAndSimpleSpellsRuntime.TryActivationCondition(engine, who, card, c, out ok,
                                out reason))
                            break;
                        // SanctuaryAndZera batch: an opponent's monster (Archlord Zerato), your LP lead (Saturn), a card in
                        // the opponent's hand or on their field (Light of Judgment).
                        if (SanctuaryAndZeraRuntime.TryActivationCondition(engine, who, card, c, out ok, out reason))
                            break;
                        ok = false;
                        reason = $"Unknown activation condition {c.ActivationCondition}.";
                        break;
                }

                if (!ok) return false;
            }

            reason = null;
            return true;
        }

        // ───────────────────── Classic-era hooks (MRD tranche 2) ─────────────────────

        static IEnumerable<EffectClause> FaceUpClauses(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null) yield break;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action) yield return c;
        }

        /// <summary>Germ Infection / Stim-Pack: count one more Standby Phase on each matching Equip.</summary>
                /// <summary>
        /// Soft residual: Swords of Concealing Light — during the controller's Standby Phase,
        /// decrement ContinuousTurnsRemaining on face-up hosts that carry
        /// OpponentCannotChangeBattlePositions / SetAllOppMonstersFaceDownDefense, and
        /// destroy when the counter hits 0 (your 2nd Standby after activation). Not opponent's
        /// Standby. Soft TextEffectRuntime Standby tick — Mask Soft surgical.
        /// </summary>
        static void TickConcealingLightStandbyDestroy(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                // The countdown is EFFECT_FLAG_CANNOT_DISABLE (c12923641): it keeps counting while negated.
                if (st == null || !st.FaceUp) continue;
                if (st.ContinuousTurnsRemaining <= 0) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null) continue;
                var isConcealing = prog.ClauseList.Exists(c =>
                    c != null &&
                    (c.Action == EffectActionKind.OpponentCannotChangeBattlePositions ||
                     c.Action == EffectActionKind.SetAllOppMonstersFaceDownDefense));
                if (!isConcealing) continue;
                st.ContinuousTurnsRemaining--;
                engine.Log(
                    $"{st.Name}: Standby count → {st.ContinuousTurnsRemaining} remaining " +
                    $"(destroy at 0 on controller's 2nd Standby after activation).");
                if (st.ContinuousTurnsRemaining <= 0)
                {
                    engine.Log($"{st.Name} is destroyed (your 2nd Standby Phase after activation).");
                    engine.SendCardToGrave(who, st);
                }
            }
        }

        /// <summary>
        /// Soft residual Soft Banish: Lightforce Sword delayed return — during the Banished
        /// card owner's Standby Phase, decrement ContinuousTurnsRemaining on face-down Banished
        /// copies seeded by BanishRandomFromOppHandFaceDown, and Soft-return to hand at 0
        /// (owner's 4th Standby after activation). Prefer NO DE rewrite: Soft FaceUp overwrite
        /// after BanishCard + Soft Hand.Add (ReturnCardToHand needs field ControllerOf).
        /// Soft TextEffectRuntime Standby tick — Mask Soft surgical; not CardId-hardcoded.
        /// </summary>
        static void TickLightforceSwordStandbyReturn(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who?.Banished == null) return;
            foreach (var c in who.Banished.ToList())
                if (IsBanishedReturnCountdown(c))
                    AdvanceBanishedReturnCountdown(engine, who, c);
        }

        /// <summary>A face-down banished card counting its owner's Standby Phases back to the hand (Lightforce Sword).</summary>
        public static bool IsBanishedReturnCountdown(CardInstance c) =>
            c != null && !c.FaceUp && c.ContinuousTurnsRemaining > 0;

        /// <summary>
        /// One step of that count (Lua c49587034 s.thop): its owner's Standby Phase, or Pyro Clock of Destiny
        /// (PhaseTimersAndDeckTop batch, v163). On the last it returns to its owner's hand.
        /// </summary>
        public static void AdvanceBanishedReturnCountdown(DuelEngine engine, DuelistState owner, CardInstance c)
        {
            if (engine == null || owner?.Banished == null || !IsBanishedReturnCountdown(c) || !owner.Banished.Contains(c))
                return;
            c.ContinuousTurnsRemaining--;
            engine.Log(
                $"{c.Name}: Soft banished Standby count → {c.ContinuousTurnsRemaining} remaining " +
                $"(return at 0 on owner's 4th Standby after activation).");
            if (c.ContinuousTurnsRemaining <= 0)
                SoftReturnBanishedFaceDownToHand(engine, owner, c);
        }

        /// <summary>
        /// Soft residual Soft Banish: restore a Soft face-down Banished card to owner's hand.
        /// Soft path — ReturnCardToHand needs field ControllerOf; Soft Banished.Remove + Hand.Add.
        /// </summary>
        static void SoftReturnBanishedFaceDownToHand(DuelEngine engine, DuelistState owner, CardInstance card)
        {
            if (engine == null || owner == null || card == null) return;
            owner.Banished?.Remove(card);
            card.FaceUp = true;
            card.ContinuousTurnsRemaining = 0;
            card.Position = BattlePosition.Attack;
            card.ClearAttackFlags();
            if (owner.Hand != null && !owner.Hand.Contains(card))
                owner.Hand.Add(card);
            engine.Log($"{card.Name} returns to {owner.Name}'s hand (Soft Lightforce delayed return).");
        }

        /// <summary>
        /// Lightforce Sword resolve: Soft pick random Soft from opp hand, Soft Banish face-down,
        /// seed ContinuousTurnsRemaining = Amount (default 4) for Soft opp Standby return ticker.
        /// Soft residual Soft Banish: BanishCard forces FaceUp=true — Soft overwrite FaceUp=false.
        /// Prefer NO DE rewrite. random ≠ choose (Rng.PickIndex / QueuePick Soft testable).
        /// </summary>
        static void BanishRandomFromOppHandFaceDown(DuelEngine engine,
            DuelistState opp, CardInstance source, EffectClause clause)
        {
            if (engine == null || opp?.Hand == null) return;
            if (opp.Hand.Count == 0)
            {
                if (source != null)
                    engine.Log($"{source.Name}: {opp.Name} has no cards in hand to banish.");
                return;
            }
            var i = engine.Rng.PickIndex(opp.Hand.Count);
            if (i < 0 || i >= opp.Hand.Count) i = 0;
            var pick = opp.Hand[i];
            if (pick == null) return;
            var name = pick.Name;
            engine.BanishCard(opp, pick);
            // Soft residual Soft Banish: FaceUp overwrite after BanishCard (Prefer NO DE rewrite).
            pick.FaceUp = false;
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 4;
            pick.ContinuousTurnsRemaining = n;
            // PhaseTimersAndDeckTop batch (v163): the card that holds this turn count (Pyro Clock of Destiny's pick).
            PhaseTimersAndDeckTopRuntime.NoteTurnCountHolder(engine, pick, source, engine.OpponentOf(opp));
            var src = source != null ? source.Name : "Effect";
            engine.Log(
                $"{src}: banished {name} face-down from {opp.Name}'s hand " +
                $"(Soft return on their {n}th Standby Phase after activation).");
        }

static void TickEquipStandbyDecay(DuelEngine engine, DuelistState turnPlayer)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var host in side.MonstersOnField())
                {
                    if (host?.Equips == null) continue;
                    foreach (var eq in host.Equips)
                    {
                        foreach (var c in FaceUpClauses(eq, EffectActionKind.EquipAtkDecayPerStandby))
                        {
                            var whose = c.DecayOnEquippedControllersStandby
                                ? engine.ControllerOf(host)
                                : engine.ControllerOf(eq);
                            if (whose != turnPlayer) continue;
                            eq.Counters++;
                            engine.Log($"{eq.Name}: {host.Name} loses {c.Amount} ATK (Standby).");
                        }
                    }
                }
            }
        }

        /// <summary>Total ATK loss the equip's Standby counter applies to its host.</summary>
        public static int EquipStandbyDecay(CardInstance equip)
        {
            var total = 0;
            foreach (var c in FaceUpClauses(equip, EffectActionKind.EquipAtkDecayPerStandby))
                total += c.Amount * Mathf.Max(0, equip.Counters);
            return total;
        }

        /// <summary>LP this monster must pay to declare an attack (Dark Elf). 0 = none.</summary>
        public static int AttackLpCost(CardInstance attacker)
        {
            var n = 0;
            foreach (var c in FaceUpClauses(attacker, EffectActionKind.AttackCostLp))
                n += Mathf.Max(0, c.Amount);
            return n;
        }

        /// <summary>
        /// Every attack cost for this declaration: the monster's own (Dark Elf) plus face-up
        /// Continuous cards (Toll: each player pays; Gravekeeper's Servant: the opponent mills).
        /// </summary>
        public static void AttackCosts(DuelEngine engine, DuelistState attackerSide, CardInstance attacker,
            out int lp, out int mill)
        {
            lp = AttackLpCost(attacker);
            mill = 0;
            if (engine == null || attackerSide == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    foreach (var c in FaceUpClauses(st, EffectActionKind.AttackCostLpForAll))
                        if (c.Side == EffectSide.Both || side != attackerSide)
                            lp += Mathf.Max(0, c.Amount);
                    foreach (var c in FaceUpClauses(st, EffectActionKind.AttackCostMillForOpponent))
                        if (side != attackerSide)
                            mill += Mathf.Max(0, c.Amount);
                }
            }
        }

        /// <summary>Pay the mill part of an attack cost (Gravekeeper's Servant).</summary>
        public static void PayAttackMill(DuelEngine engine, DuelistState who, int n)
        {
            if (n > 0) SendTopOfDeckToGy(engine, who, n);
        }

        /// <summary>Spellbinding Circle: a linked card forbids this monster changing position.</summary>
        public static bool PositionChangeForbiddenByEffect(CardInstance monster)
        {
            // PositionLocks Soft: ManualPositionChangeLocked is gated in DuelEngine.CanChangePosition /
            // CanFlipSummon / IsBattlePositionLocked (Goblin one-line pattern). This helper has no
            // DuelEngine — Spellbinding Circle equip lock only. Card-effect position changes
            // (ApplyEffectPositionChange) are not the printed rest-of-turn lock.
            if (monster?.Equips == null) return false;
            foreach (var eq in monster.Equips)
            {
                if (FaceUpClauses(eq, EffectActionKind.EquippedCannotChangePosition).Any())
                    return true;
                // Local-lineage bound trap (Spellbinding Circle arm): AlsoCannotChangeBattlePosition.
                if (FaceUpClauses(eq, EffectActionKind.ContinuousCannotAttack).Any(c => c.AlsoCannotChangeBattlePosition))
                    return true;
            }
            return false;
        }

        /// <summary>Megamorph: ATK change the equip applies to its host right now.</summary>
        public static int LpComparisonAtkDelta(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            if (engine == null || host?.Def == null || host.Def.atk < 0) return 0;
            if (!FaceUpClauses(equip, EffectActionKind.EquipAtkByLpComparison).Any()) return 0;
            var mine = engine.ControllerOf(equip);
            var theirs = engine.OpponentOf(mine);
            if (mine == null || theirs == null) return 0;
            var orig = host.Def.atk;
            if (mine.LifePoints < theirs.LifePoints) return orig;             // becomes double
            if (mine.LifePoints > theirs.LifePoints) return -(orig - (orig + 1) / 2); // becomes half
            return 0;
        }

        /// <summary>
        /// Effects that stop this monster declaring an attack: an Equip that forbids it
        /// (Paralyzing Potion) or an Electric Lizard lock that lasts through this turn.
        /// </summary>
        public static bool AttackForbiddenByEffect(DuelEngine engine, CardInstance attacker)
        {
            if (attacker == null) return false;
            if (attacker.CannotAttackThroughTurn >= engine.TurnNumber) return true;
            var locker = attacker.AttackLockedBy;
            if (locker != null)
            {
                if (locker.FaceUp && engine.ControllerOf(locker) != null) return true;
                attacker.AttackLockedBy = null; // the Invitation left or was turned face-down
            }
            if (attacker.Equips != null)
                foreach (var eq in attacker.Equips)
                {
                    if (FaceUpClauses(eq, EffectActionKind.EquippedCannotAttack).Any())
                        return true;
                    // Local-lineage equip / bound-trap lock (Spellbinding Circle arm): the linked card's
                    // attack ban, unless the host is of the excepted Type.
                    if (FaceUpClauses(eq, EffectActionKind.ContinuousCannotAttack).Any(c =>
                            string.IsNullOrEmpty(c.ExcludeRaceFilter) ||
                            !ClassicEraTemplates.RaceMatches(attacker.Race, c.ExcludeRaceFilter)))
                        return true;
                }
            return false;
        }

        /// <summary>
        /// Ring of Magnetism: the defending player's face-up monsters equipped with it are the
        /// only legal attack targets (and no direct attack). Empty = no restriction.
        /// </summary>
        public static List<CardInstance> ForcedAttackTargets(DuelistState defender)
        {
            var list = new List<CardInstance>();
            if (defender == null) return list;
            foreach (var m in defender.MonstersOnField())
            {
                if (m?.Equips == null || !m.FaceUp) continue;
                // GitHub lineage: EquippedMustBeAttackTarget; local lineage: AttacksMustTargetEquippedHost
                // (DuelEngine.MagnetismHosts). Either Ring of Magnetism arm forces the attack target.
                if (m.Equips.Any(eq => FaceUpClauses(eq, EffectActionKind.EquippedMustBeAttackTarget).Any() ||
                                       FaceUpClauses(eq, EffectActionKind.AttacksMustTargetEquippedHost).Any()))
                    list.Add(m);
            }

            return list;
        }

        /// <summary>Insect Soldiers of the Sky: Damage Step-only ATK when attacking a matching monster.</summary>
        public static int DamageStepAtkBonus(CardInstance attacker, CardInstance target)
        {
            if (target?.Def == null) return 0;
            var n = 0;
            foreach (var c in FaceUpClauses(attacker, EffectActionKind.GainAtkWhenAttackingMatching))
            {
                if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                    !string.Equals(target.Attribute, c.AttributeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(c.RaceFilter) &&
                    !ClassicEraTemplates.RaceMatches(target.Race, c.RaceFilter))
                    continue;
                n += c.Amount;
            }

            return n;
        }

        /// <summary>
        /// Electric Lizard: a monster (not of the excepted Type) that attacked it cannot attack
        /// during its controller's next turn.
        /// </summary>
        public static void NotifyAttackedBy(DuelEngine engine, CardInstance defender, CardInstance attacker)
        {
            if (engine == null || defender == null || attacker?.Def == null) return;
            foreach (var c in FaceUpClauses(defender, EffectActionKind.AttackerCannotAttackNextTurn))
            {
                if (!string.IsNullOrEmpty(c.ExceptRaceFilter) &&
                    ClassicEraTemplates.RaceMatches(attacker.Race, c.ExceptRaceFilter))
                    continue;
                attacker.CannotAttackThroughTurn = engine.TurnNumber + 2;
                engine.Log($"{defender.Name}: {attacker.Name} cannot attack during its controller's next turn.");
            }
        }

        /// <summary>Jirai Gumo: "When this card declares an attack:" clauses.</summary>
        public static void FireAttackDeclared(DuelEngine engine, DuelistState who, CardInstance attacker)
        {
            // Mirror Wall (StandbyGrowthAndMaintenance batch, v142): the attacker's opponent's face-up walls mark it.
            StandbyGrowthAndMaintenanceRuntime.NotifyAttackDeclared(engine, attacker);
            if (engine == null || who == null || attacker?.Def == null || !attacker.FaceUp || attacker.IsNegated)
                return;
            var prog = CompiledEffectCache.GetOrCompile(attacker);
            if (prog == null) return;
            var d = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardDeclaresAttack))
                ApplyClause(engine, who, attacker, c, null, ref d, ref d);
        }

        /// <summary>
        /// Dream Clown / Crass Clown / Tainted Wisdom: this monster's battle position changed.
        /// <paramref name="toDefense"/> = Attack → face-up Defense; false = Defense → Attack.
        /// A targeted clause asks the human controller to pick; the AI picks automatically.
        /// </summary>
        public static void NotifyPositionChanged(DuelEngine engine, CardInstance card, bool toDefense)
        {
            if (engine == null || card?.Def == null || !card.FaceUp || card.IsNegated) return;
            var who = engine.ControllerOf(card);
            if (who == null) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            var clauses = prog?.ClausesFor(EffectTiming.ThisCardPositionChanged)
                .Where(c => c != null && c.PositionChangeToDefense == toDefense).ToList();
            if (clauses == null || clauses.Count == 0) return;
            if (engine.PendingActivation != null)
            {
                engine.Log($"{card.Name}: position trigger skipped — another choice is open.");
                return;
            }

            engine.Log($"{card.Name}: position change effect.");
            var targeted = clauses.FirstOrDefault(c => c.RequiresTargetChoice);
            var d = false;
            if (targeted == null)
            {
                foreach (var c in clauses)
                    ApplyClause(engine, who, card, c, null, ref d, ref d);
                engine.NotifyPublic();
                return;
            }

            var targets = CollectTargets(engine, who, targeted, card);
            if (targets.Count == 0)
            {
                engine.Log($"{card.Name}: no legal targets.");
                return;
            }

            if (!who.IsPlayer)
            {
                var pick = AutoPick(targeted, targets, who, engine);
                foreach (var c in clauses)
                    ApplyClause(engine, who, card, c, c.RequiresTargetChoice ? pick : null, ref d, ref d);
                engine.NotifyPublic();
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                TargetKind = MapTargetKind(targeted),
                IsMonsterEffect = true,
                UsesTextProgram = true
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
        }

        public static void FirePhaseTriggers(DuelEngine engine, DuelistState who, EffectTiming timing)
        {
            if (engine == null || who == null) return;
            // TriggerChainResponses (v163 review): the phase's triggers that go on a Chain (Life Absorbing Machine, Spirit's
            // Invitation) are collected and seated together once every other trigger of this pass has resolved, so none
            // resolves, or opens a pick, while a response window is open.
            TriggerChainResponsesRuntime.BeginCollect(engine);
            try
            {
                FirePhaseTriggersPass(engine, who, timing);
            }
            finally
            {
                TriggerChainResponsesRuntime.EndCollect(engine);
            }
        }

        static void FirePhaseTriggersPass(DuelEngine engine, DuelistState who, EffectTiming timing)
        {
            // A Feint Plan / Roar-style this-turn face-down attack lock clears at End Phase.
            // Prefer TextEffects clear so DuelEngine End Phase block stays untouched.
            if (timing == EffectTiming.EndPhase)
            {
                if (engine.Player != null)
                    engine.Player.CannotAttackFaceDownMonstersThisTurn = false;
                if (engine.Opponent != null)
                    engine.Opponent.CannotAttackFaceDownMonstersThisTurn = false;
            }
            if (timing == EffectTiming.StandbyPhase)
            {
                TickEquipStandbyDecay(engine, who);
                // Soft residual: Swords of Concealing Light controller 2nd Standby self-destroy
                // (ContinuousTurnsRemaining seeded on Activate SetAllOppMonstersFaceDownDefense).
                // Soft TextEffectRuntime Standby tick — Mask Soft surgical; not CardId-hardcoded.
                TickConcealingLightStandbyDestroy(engine, who);
                // Soft residual Soft Banish: Lightforce Sword — during the Banished card owner's
                // Standby, decrement ContinuousTurnsRemaining on Soft face-down Banished copies
                // and Soft-return to hand at 0 (opponent's 4th Standby after activation).
                // Soft TextEffectRuntime Standby tick — Mask Soft surgical; Prefer NO DE rewrite.
                TickLightforceSwordStandbyReturn(engine, who);
                // PerCountBurns (v141): Wave-Motion Cannon counts each of its controller's Standby Phases.
                PerCountBurnsRuntime.TickStandbyTurnCounters(engine, who);
                // FlipMassEffects (v142): Jigen Bakudan's flip record counts its controller's Standby Phases.
                FlipMassEffectsRuntime.TickStandby(engine, who);
                // StandbyGrowthAndMaintenance (v142): Castle of Dark Illusions / Pumpking count this Standby Phase.
                StandbyGrowthAndMaintenanceRuntime.TickStandby(engine, who);
                // BattleOutcomeTriggers (v162): Mucus Yolk's ATK gain after it inflicted battle damage.
                BattleOutcomeTriggersRuntime.OnStandbyPhase(engine, who);
                // PhaseTimersAndDeckTop (v163): Bottomless Shifting Sand's hand check and Different Dimension Capsule's
                // count (continuous effects, before the Standby triggers).
                PhaseTimersAndDeckTopRuntime.OnStandbyPhase(engine, who);
            }
            // BattlePhaseEnd (v142): Steel Scorpion countdowns count this End Phase.
            if (timing == EffectTiming.EndPhase)
            {
                BattlePhaseEndRuntime.TickDelayedDestroys(engine);
                // Gorgon's Eye (ContinuousNegation batch): its turn has ended (RESET_PHASE|PHASE_END).
                ContinuousNegationRuntime.EndTurn(engine);
            }
            // BanishedReturn: D.D. Scout Plane comes back from the banished pile (both players' piles).
            if (timing == EffectTiming.EndPhase)
                BanishedReturnRuntime.FireEndPhase(engine, who);
            var seen = new HashSet<CardInstance>();
            FirePhaseTriggersFor(engine, who, timing, opponentTurnClauses: false, seen);
            if (timing == EffectTiming.StandbyPhase || timing == EffectTiming.EndPhase)
            {
                var other = engine.OpponentOf(who);
                if (other != null)
                    FirePhaseTriggersFor(engine, other, timing, opponentTurnClauses: true, seen);
            }

            // TypeDeclaration batch (v142): The Regulation of Tribe's upkeep (Tribute 1 monster or destroy it).
            if (timing == EffectTiming.StandbyPhase)
                TypeDeclarationRuntime.RunStandbyUpkeep(engine);

            // PositionLocks Soft: a human Aqua Spirit / Garuda pick queued by OnPhaseTrigger.
            // After Standby upkeep so a Tribute pick is not overwritten. AI already resolved inside
            // OnPhaseTrigger. DE BeginTurn / EndTurn already pause when a PendingActivation is open,
            // so the phase waits without a DuelEngine rewrite.
            if ((timing == EffectTiming.StandbyPhase || timing == EffectTiming.EndPhase) &&
                engine.PendingActivation == null)
                PositionLocksRuntime.OfferQueuedPick(engine);
            // StandbyBurnsAndDice batch (v162): a human's Inspection choice in the opponent's Standby Phase.
            if (timing == EffectTiming.StandbyPhase && engine.PendingActivation == null)
                StandbyBurnsAndDiceRuntime.OfferQueuedInspection(engine);
            // DestinyBoard batch: a human's Destiny Board placement with copies in both the hand and the Deck.
            if (timing == EffectTiming.EndPhase && engine.PendingActivation == null)
                DestinyBoardRuntime.OfferQueuedPick(engine);
            // EquipFamily batch: a human's Dark Necrofear target pick (the End Phase waits for it).
            if (timing == EffectTiming.EndPhase && engine.PendingActivation == null)
                EquipFamilyRuntime.OfferQueuedPick(engine);
            // SpecialSummonEffects batch (v161): a human's Twin-Headed Behemoth Yes / Cancel (the End Phase waits).
            if (timing == EffectTiming.EndPhase && engine.PendingActivation == null)
                SpecialSummonEffectsRuntime.OfferQueuedPick(engine);

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
            var tributes = CollectTributeCost(tributer, card, c, engine);
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
                // Soul Exchange: the forced Tribute is the only offer.
                pending.LegalTargets.AddRange(TributeSummonsRuntime.RestrictToForced(tributer, tributes));
                engine.SetPendingActivation(pending);
                engine.Log($"{card.Name}: Tribute 1 face-up monster you control (effect, not cost).");
                engine.NotifyPublic();
                return true;
            }

            var pick = tributes[0];
            var printed = PrintedOriginalAtk(pick);
            // EngineHygiene: EVENT_RELEASE — the reason player is the effect's controller.
            ReleaseEvents.Record(engine, pick, engine.ControllerOf(card) ?? tributer);
            engine.SendCardToGrave(engine.ControllerOf(pick) ?? tributer, pick, sentBy: card);
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
            // Pandemonium (ArchfiendRiders batch, compiled clause): neither player pays LP for their Archfiend monsters.
            if (ArchfiendRidersRuntime.StandbyLpCostWaived(engine, card, out var waiver))
            {
                engine.Log($"{card.Name}: Standby maintenance ({n} LP) waived ({waiver.Name}).");
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
            // An optional upkeep (Mirror Wall, Messenger of Peace) is never paid with the last Life Points: that
            // choice would lose the Duel on the spot, so the card is destroyed instead. A mandatory one ("you must
            // pay", Imperial Order) is paid whenever the controller can (StandbyGrowthAndMaintenance batch, v142).
            if (who != null && c != null && c.UpkeepIsOptional && who.LifePoints == n)
            {
                engine.Log($"{card?.Name}: paying {n} LP would leave 0 LP — destroy this card instead.");
                if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, card)) engine.SendCardToGrave(who, card);
                return;
            }

            // With LP to spare, an optional upkeep is the controller's choice (Lua CheckLPCost + SelectYesNo): a human
            // is asked in their Standby Phase, which waits for the answer; the AI pays only while it keeps more than
            // N LP afterwards (StandbyGrowthAndMaintenance batch, v142).
            if (who != null && c != null && c.UpkeepIsOptional && who.LifePoints > n)
            {
                var choice = StandbyGrowthAndMaintenanceRuntime.OptionalUpkeepChoice(engine, who, card, n);
                if (choice == StandbyGrowthAndMaintenanceRuntime.UpkeepDecision.Asked)
                    return;
                if (choice == StandbyGrowthAndMaintenanceRuntime.UpkeepDecision.Decline)
                {
                    engine.Log($"{card?.Name}: {who.Name} does not pay {n} LP — destroy this card.");
                    if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, card)) engine.SendCardToGrave(who, card);
                    return;
                }
            }

            if (who != null && who.LifePoints >= n)
            {
                engine.PayLifePoints(who, n,
                    $"Cost: pay {n} LP or destroy {card?.Name} → {who.Name} at {who.LifePoints - n} LP.");
                MirrorStandbyPayment(engine, who, card, n);
                return;
            }

            engine.Log($"{card?.Name}: not enough LP — destroy this card.");
            // EquipAndSpellProtection batch: a Magic Reflector counter is removed instead (Lua destroys it as a cost).
            if (!EquipAndSpellProtectionRuntime.ReplacesDestruction(engine, card)) engine.SendCardToGrave(who, card);
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
                if (eq?.Def == null || eq.IsNegated) continue;
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

        /// <summary>
        /// A Standby Phase maintenance cost ("pay N LP or destroy this card", the Archfiend upkeep, the
        /// generic "the controller pays N LP during each of their Standby Phases"). It cannot be negated
        /// (ygopro EFFECT_FLAG_CANNOT_DISABLE), so a negated card still pays it.
        /// </summary>
        static bool IsUpkeepCost(EffectClause c) =>
            c != null &&
            (c.Action == EffectActionKind.PayLpOrDestroyThis ||
             c.Action == EffectActionKind.StandbyMaintenancePayLp ||
             c.Action == EffectActionKind.StandbyTributeOrDestroyThis ||
             // ContinuousTrapsAndSpiritSupport batch: Spiritual Energy Settle Machine's End Phase discard-or-destroy.
             c.Action == EffectActionKind.EndPhaseDiscardOrDestroyThis ||
             // SpellCounterSpenders batch: Armor Exe's remove-a-Spell-Counter-or-destroy upkeep.
             c.Action == EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis ||
             (c.Action == EffectActionKind.PayLifePoints && c.Timing == EffectTiming.StandbyPhase));

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
                // NegationMonsters batch (v161): a GY card negated by Ha Des / Twin-Headed Wolf has no GY effect.
                if (NegationMonstersRuntime.NegatedInGy(engine, card)) continue;
                // BothSideAndMonsterEquip (v161): a monster Card acting as an Equip Spell with only a granted effect
                // (Sword Hunter) has none of its own Standby / End Phase effects, upkeep costs included.
                if (card.IsEquipSpellWithGrantedEffectOnly) continue;
                var prog = CompiledEffectCache.GetOrCompile(card.Def);
                // Fire compiled Standby/End clauses even when leftover text keeps the card a Stub
                // (Lava Golem NS-lock rider, Messenger of Peace pay-or-destroy, etc.).
                if (prog == null) continue;
                var clauses = prog.ClausesFor(timing);
                if (clauses == null || clauses.Count == 0) continue;
                // A face-up card on the field whose effects are negated (World Suppression / Mystic
                // Probe / Metal Detector this turn, or a stored negation) applies none of its Standby /
                // End Phase effects while it stays negated. Its upkeep cost is still paid: ygopro marks
                // every maintenance cost EFFECT_FLAG_CANNOT_DISABLE (Messenger of Peace, Spirit's
                // Invitation, Mask of Brutality, Imperial Order, the Archfiend maintenance).
                var negatedOnField = card.IsNegated &&
                                     (who.TryFindSpellTrap(card, out _) || who.TryFindMonster(card, out _) ||
                                      ReferenceEquals(who.FieldSpellZone?.Occupant, card));
                foreach (var c in clauses)
                {
                    if (c == null) continue;
                    // Resolved only from the banished pile (BanishedReturnRuntime.FireEndPhase).
                    if (c.Action == EffectActionKind.SpecialSummonThisFromBanished) continue;
                    if (negatedOnField && !c.ResolvesFromGy && !IsUpkeepCost(c)) continue;
                    // EquipFamily batch: Dark Necrofear — either player's End Phase, from its owner's GY.
                    if (c.Action == EffectActionKind.EquipThisFromGyToTargetTakeControl)
                    {
                        if (timing == EffectTiming.EndPhase && c.ResolvesFromGy && who.Graveyard.Contains(card))
                            EquipFamilyRuntime.OnEndPhaseTrigger(engine, who, card, c);
                        continue;
                    }
                    // BattleDestroyedGyRecovery batch (v162): Revival Jam — its controller's Standby Phase, from its GY, while
                    // flagged (no Chain Link).
                    if (c.Action == EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby)
                    {
                        if (timing == EffectTiming.StandbyPhase && c.ResolvesFromGy && who.Graveyard.Contains(card))
                            BattleDestroyedGyRecoveryRuntime.OnStandby(engine, who, card);
                        continue;
                    }
                    // SpecialSummonEffects batch (v161): Twin-Headed Behemoth — either player's End Phase, from its GY.
                    if (c.Action == EffectActionKind.SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn)
                    {
                        if (timing == EffectTiming.EndPhase && c.ResolvesFromGy && who.Graveyard.Contains(card))
                            SpecialSummonEffectsRuntime.OnEndPhaseTrigger(engine, who, card, c);
                        continue;
                    }
                    // RecoveryAndPhaseCosts batch: Manticore of Darkness — either player's End Phase, from its GY.
                    if (RecoveryAndPhaseCostsRuntime.TryFirePhaseClause(engine, who, card, c, timing))
                        continue;
                    var eitherTurn = c.Action == EffectActionKind.DestroySummonedThisTurnLevelLeq;
                    // "during the Standby Phase" (Imperial Order): every Standby Phase, either player's.
                    var eachStandby = c.EachStandbyPhase && timing == EffectTiming.StandbyPhase;
                    // Spirit return (proc_spirit.lua, EVENT_PHASE+PHASE_END trigger in the Monster Zone): any
                    // End Phase — a Set Spirit flipped face-up by the opponent's attack returns that turn.
                    var spiritReturn = SpiritsAndMiscRuntime.IsSpiritReturnClause(c, timing);
                    // NamedSummonPairs batch: Mirage Knight — "During the End Phase of a turn …" (either player's).
                    var eachEndPhase = NamedSummonPairsRuntime.FiresAtEachEndPhase(c, timing);
                    if (!c.TurnPlayerTributes && !eitherTurn && !eachStandby && !spiritReturn && !eachEndPhase &&
                        !c.FiresAtEachPlayersPhase &&
                        (opponentTurnClauses ? !c.OpponentTurnOnly : c.OpponentTurnOnly))
                        continue;
                    if (c.RequiresOnlyMonsterYouControl &&
                        (who.MonsterCount != 1 || !who.TryFindMonster(card, out _)))
                        continue;
                    // Infinite Dismissal works only face-up in the Spell & Trap Zone, never from the GY.
                    if (eitherTurn && (!card.FaceUp || !who.TryFindSpellTrap(card, out _)))
                        continue;
                    // Cold Wave (LocksAndZones batch): a Spell/Trap Card's Standby / End Phase effect that is
                    // activated (it makes a Chain Link: ygopro TRIGGER_O / TRIGGER_F — Burning Land, Infinite
                    // Dismissal, Snatch Steal, Weapon Change …) cannot be activated while the lock lasts.
                    // Maintenance costs and the effects that make no Chain Link (Stim-Pack) are continuous and
                    // still apply.
                    if (!card.Def.IsMonster && c.MakesChainLink && !IsUpkeepCost(c) &&
                        c.ResolvesFromGy == (who.Graveyard != null && who.Graveyard.Contains(card)) &&
                        (card.FaceUp || c.ResolvesFromGy) &&
                        LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card))
                        continue;
                    if (c.TurnPlayerTributes)
                    {
                        if (!card.FaceUp) continue;
                        if (who.Graveyard != null && who.Graveyard.Contains(card)) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        // Imperial Order / Spell Canceller / Royal Decree: a negated Spell/Trap resolves
                        // no effect here either (ContinuousNegation batch).
                        if (ContinuousNegationRuntime.NegatesResolvingClause(engine, who, card, c)) continue;
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
                        if (ContinuousNegationRuntime.NegatesResolvingClause(engine, who, card, c)) continue;
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

                    // ArchfiendRiders batch: Infernalqueen Archfiend — every Standby Phase, its controller targets 1 face-up
                    // "Archfiend" monster they control (+1000 ATK until the End Phase).
                    if (ArchfiendRidersRuntime.TryStandbyTargetBoost(engine, who, card, c))
                        continue;

                    // The Regulation of Tribe (TypeDeclaration batch, v142): queued here, run once every Standby
                    // trigger has fired (a player's Tribute pick holds the Standby Phase).
                    if (c.Action == EffectActionKind.StandbyTributeOrDestroyThis)
                    {
                        // StandbyBurnsAndDice batch (v162): The Unfriendly Amazon's upkeep is a monster's.
                        if (!card.FaceUp || (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _)))
                            continue;
                        TypeDeclarationRuntime.QueueStandbyUpkeep(engine, who, card);
                        continue;
                    }

                    // ContinuousTrapsAndSpiritSupport batch: Spiritual Energy Settle Machine's maintenance (a cost, paid
                    // while negated too): discard 1 card or destroy this card; a human's choice holds the End Phase.
                    if (c.Action == EffectActionKind.EndPhaseDiscardOrDestroyThis)
                    {
                        if (!card.FaceUp || !who.TryFindSpellTrap(card, out _)) continue;
                        ContinuousTrapsAndSpiritSupportRuntime.ResolveEndPhaseUpkeep(engine, who, card, c);
                        continue;
                    }
                    // SimpleTriggersAndSwarm batch: Two-Man Cell Battle — during each player's End Phase the turn player
                    // may Special Summon 1 Level 4 Normal Monster from that player's hand (EFFECT_FLAG_BOTH_SIDE, so the
                    // actor is engine.TurnPlayer even when this card's controller is not the turn player).
                    if (c.Action == EffectActionKind.SpecialSummonLevelNormalFromHandAtEachPlayersEndPhase)
                    {
                        if (!card.FaceUp || !who.TryFindSpellTrap(card, out _)) continue;
                        SimpleTriggersAndSwarmRuntime.TryOfferEndPhaseNormalLevelFromHand(engine, who, card, c);
                        continue;
                    }

                    // SpellCounterSpenders batch: Armor Exe — remove 1 Spell Counter on your side or destroy this card (each
                    // Standby Phase; a maintenance cost that also applies while negated; a human's choice holds the phase).
                    if (c.Action == EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis)
                    {
                        if (!card.FaceUp || !who.TryFindMonster(card, out _)) continue;
                        SpellCounterSpendersRuntime.ResolveStandbyUpkeep(engine, who, card, c);
                        continue;
                    }

                    if (c.Action == EffectActionKind.PayLpOrDestroyThis)
                    {
                        if (!card.FaceUp) continue;
                        if (!who.TryFindSpellTrap(card, out _) && !who.TryFindMonster(card, out _))
                            continue;
                        // Pandemonium waives the Archfiend Standby maintenance cost
                        // (but not non-Archfiend upkeep like Messenger of Peace). ArchfiendRiders batch: compiled clause.
                        if (ArchfiendRidersRuntime.StandbyLpCostWaived(engine, card, out var waiver))
                        {
                            engine.Log($"{card.Name}: Standby cost waived ({waiver.Name}).");
                            continue;
                        }
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
                    if (c.RequiresThisDestroyedByOpponent && !card.SentByOpponentEffect)
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
                    if (c.RequiresEmptyHandAtOpponentsLastEndPhase &&
                        !EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand(engine, who, card))
                        continue;
                    if (spiritReturn && !SpiritsAndMiscRuntime.SpiritReturnDue(engine, card))
                        continue;
                    // NamedSummonPairs batch: Mirage Knight's banish is a trigger only in a turn it battled (ygopro rmcon
                    // GetBattledGroupCount() > 0): no Chain Link otherwise.
                    if (eachEndPhase && !NamedSummonPairsRuntime.BattledThisTurn(engine, card))
                        continue;
                    if (!spiritReturn && c.RequiresSummonedOrFlippedThisTurn && !card.SummonedThisTurn)
                        continue;
                    // LvChain batch: an activation-condition rider ("You cannot activate this effect the
                    // turn this card was Normal Summoned, Special Summoned, or flipped face-up.") gates the
                    // phase trigger just as it gates any other activation (ActivationConditionsMet, the same
                    // list the ignition path uses — e.g. LvChain.NotTurnSummoned for Ultimate Insect LV1).
                    if (!string.IsNullOrEmpty(c.ActivationCondition) &&
                        !ActivationConditionsMet(engine, who, card, prog, out _))
                        continue;
                    if (c.RequiresSendThisToGy &&
                        (who.TryFindMonster(card, out _) || who.TryFindSpellTrap(card, out _)))
                    {
                        engine.SendCardToGrave(who, card);
                        engine.Log($"Cost: send {card.Name} to the GY.");
                    }

                    if (c.Action == EffectActionKind.OptionalPayLpShiftControl)
                    {
                        if (!card.FaceUp || !who.TryFindMonster(card, out _)) continue;
                        var cost = c.Amount > 0 ? c.Amount : 500;
                        if (!who.IsPlayer || who.LifePoints < cost) continue;
                        engine.SetPendingActivation(new PendingActivation
                        {
                            Controller = who,
                            Card = card,
                            IsMonsterEffect = true,
                            UsesTextProgram = true,
                            AwaitingOptionalPay = true,
                            CostNumeric = cost
                        });
                        engine.Log($"{card.Name}: pay {cost} LP to give control to your opponent, or decline.");
                        return;
                    }

                    // TriggerChainResponses batch (v163): Life Absorbing Machine's Standby effect is a Chain Link the
                    // opponent may respond to (held for the Chain FirePhaseTriggers seats once this pass is over).
                    if (ContinuousTrapsAndSpiritSupportRuntime.TrySeatStandbyGain(engine, who, card, c))
                        continue;
                    // SimpleTriggersAndSwarm batch: Little-Winguard's optional "You can change the battle position of this
                    // card" asks its controller (a yes/no tap; the AI decides) before it applies.
                    if (SimpleTriggersAndSwarmRuntime.TryOfferOptionalPhaseClause(engine, who, card, c))
                        continue;

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
            if (CompiledActivationChainRuntime.IsRecordingAny)
            {
                DeferTargetingNotice(engine, target); // after the seated link resolves
                return;
            }
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


        /// <summary>
        /// A Spell's activation resolved (<paramref name="spell"/>: that card, when the caller knows it): Spell Counters;
        /// PerCopyDeck batch (v163): the damage for using a Spell Graverobber took (<see cref="PerCopyDeckRuntime.OnSpellResolved"/>).
        /// </summary>
        public static void NotifySpellResolved(DuelEngine engine, DuelistState who, CardInstance spell = null)
        {
            if (engine == null) return;
            PerCopyDeckRuntime.OnSpellResolved(engine, who, spell);
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    TryPlaceSpellCounterOnSpell(engine, m, who);
                foreach (var st in side.SpellTrapsOnField())
                    TryPlaceSpellCounterOnSpell(engine, st, who);
            }

            // SpellEventsAndHandSendTriggers batch: Curse of Darkness damages the Spell's activator.
            SpellEventsAndHandSendTriggersRuntime.OnSpellResolved(engine, who, spell);
            engine.NotifyPublic();
        }

        static void TryPlaceSpellCounterOnSpell(DuelEngine engine, CardInstance card, DuelistState activator)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) return;
            // SpellEventsAndHandSendTriggers batch (aux.chainreg): a card whose own activation resolved earlier in this
            // Chain did not apply when the resolving Spell was activated.
            if (!SpellEventsAndHandSendTriggersRuntime.WasApplyingWhenActivated(engine, card)) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) return;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || !c.PlaceCounterOnSpellActivate) continue;
                var before = card.Counters;
                var add = c.Amount > 0 ? c.Amount : 1;
                var max = c.CounterMax;
                card.Counters += add;
                if (max > 0 && card.Counters > max) card.Counters = max;
                if (max > 0) card.SpellCounterMax = max;
                engine.Log($"{card.Name}: Spell Counter (spell resolve) → {card.Counters}.");
                // SpellEventsAndHandSendTriggers batch: Tower of Babel's 4th Spell Counter.
                SpellEventsAndHandSendTriggersRuntime.AfterOwnSpellCounter(engine, card, before, activator);
            }
        }

        public static bool HasStandbyOnlyIgnition(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var m in who.MonstersOnField())
            {
                if (m == null || !m.FaceUp || m.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                {
                    if (c != null && c.OnlyDuringOwnStandby)
                        return true;
                }
            }

            // PositionLocks Soft: Curse of Fiend is a Set Spell activatable only in your Standby Phase.
            if (PositionLocksRuntime.HasStandbyOnlySetCard(engine, who))
                return true;

            return false;
        }

        public static bool AttackLpCostBlocks(DuelistState who, CardInstance attacker)
        {
            if (who == null || attacker == null || attacker.AttackLpCostPaidThisDeclaration)
                return false;
            var cost = DeclareAttackLpCost(attacker);
            return cost > 0 && who.LifePoints < cost;
        }

        /// <summary>Local lineage: LP to pay to declare an attack (<see cref="EffectActionKind.PayLpToDeclareAttack"/>).</summary>
        internal static int DeclareAttackLpCost(CardInstance attacker)
        {
            if (attacker?.Def == null || !attacker.FaceUp || attacker.IsNegated) return 0;
            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null) return 0;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c != null && c.Action == EffectActionKind.PayLpToDeclareAttack)
                    return c.Amount > 0 ? c.Amount : 1000;
            }

            return 0;
        }

        /// <summary>Gravekeeper's Servant: cards the attacker's controller must mill to declare.</summary>
        internal static int AttackMillCost(DuelEngine engine, DuelistState attackerController)
        {
            var opp = engine?.OpponentOf(attackerController);
            if (opp == null) return 0;
            var total = 0;
            foreach (var st in opp.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.IsNegated || st.Def == null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.MillToDeclareAttack) continue;
                    total += c.Amount > 0 ? c.Amount : 1;
                }
            }

            return total;
        }

        public static bool PayAttackDeclarationCost(DuelEngine engine, DuelistState who, CardInstance attacker)
        {
            if (engine == null || who == null || attacker == null) return false;
            if (attacker.AttackLpCostPaidThisDeclaration) return true;
            var mill = AttackMillCost(engine, who);
            var cost = DeclareAttackLpCost(attacker);
            // Both costs are checked before either is paid (DR-04 / DR-08).
            if (mill > 0 && (who.Deck == null || who.Deck.Count < mill)) return false;
            if (cost > 0 && who.LifePoints < cost) return false;
            if (mill > 0)
            {
                SendTopOfDeckToGy(engine, who, mill);
                attacker.AttackLpCostPaidThisDeclaration = true;
                engine.Log($"{attacker.Name}: sent {mill} card(s) from the top of the Deck to the GY to declare an attack.");
            }
            if (cost <= 0) return true;
            attacker.AttackLpCostPaidThisDeclaration = true;
            engine.PayLifePoints(who, cost,
                $"{attacker.Name}: pay {cost} LP to declare an attack ({who.LifePoints - cost} LP).");
            return true;
        }

        public static bool BeginOwnAttackCoin(DuelEngine engine, DuelistState who, CardInstance attacker,
            CardInstance target)
        {
            if (engine == null || who == null || attacker == null || attacker.IsNegated) return false;
            if (attacker.AttackCoinResolvedThisDeclaration) return false;
            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var coin = false;
            foreach (var c in prog.ClausesFor(EffectTiming.ThisCardDeclaresAttack))
            {
                if (c != null && c.Action == EffectActionKind.CallCoinLoseHalfLpIfWrong)
                    coin = true;
            }

            if (!coin) return false;
            engine.SetPendingActivation(new PendingActivation
            {
                Controller = who,
                Card = attacker,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                AwaitingCoinCall = true,
                AttackDeclarationCoin = true,
                LockedTarget = target
            });
            engine.Log($"{attacker.Name}: call the coin.");
            engine.NotifyPublic();
            if (!who.IsPlayer)
                TryFinishCoinCall(engine, true);
            return true;
        }

        static bool FinishAttackDeclarationCoin(DuelEngine engine, bool callHeads)
        {
            var p = engine.PendingActivation;
            var who = p.Controller;
            var attacker = p.Card;
            var target = p.LockedTarget;
            engine.ClearPendingActivation();
            if (attacker != null)
                attacker.AttackCoinResolvedThisDeclaration = true;
            // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo it; the attack goes on after.
            CoinDiceInterruptsRuntime.TossCoin(engine, who, attacker, toss => toss == callHeads, tossHeads =>
            {
                var right = callHeads == tossHeads;
                engine.Log(
                    $"{who?.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(tossHeads ? "Heads" : "Tails")}.");
                if (!right && who != null)
                {
                    var next = (who.LifePoints + 1) / 2;
                    engine.PayLifePoints(who, who.LifePoints - next,
                        $"{attacker?.Name}: wrong call — {who.Name} loses half their LP ({who.LifePoints} → {next}).",
                        isCost: false);
                }

                if (!engine.GameOver && who != null && attacker != null)
                    engine.TryAttack(who, attacker, target);
                else
                    engine.NotifyPublic();
            });
            return true;
        }

        public static void NoteAttackerSeal(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null || attacker == null || target == null || !target.FaceUp || target.IsNegated)
                return;
            if (attacker.AttackSealNotedThisDeclaration) return;
            var prog = CompiledEffectCache.GetOrCompile(target.Def);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.SealNonRaceAttackerNextTurn) continue;
                if (!string.IsNullOrEmpty(c.ExcludeRaceFilter) &&
                    attacker.Race != null &&
                    attacker.Race.IndexOf(c.ExcludeRaceFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                attacker.CannotAttackDuringTurnNumber = engine.TurnNumber + 2;
                // Same lock in the GitHub lineage's terms (AttackForbiddenByEffect / Electric Lizard arm).
                attacker.CannotAttackThroughTurn = Math.Max(attacker.CannotAttackThroughTurn, engine.TurnNumber + 2);
                attacker.AttackSealNotedThisDeclaration = true;
                engine.Log(
                    $"{attacker.Name} cannot declare an attack on its controller's next turn ({target.Name}).");
                return;
            }
        }

        public static void NotifyPositionChanged(DuelEngine engine, DuelistState who, CardInstance card,
            BattlePosition from, BattlePosition to, bool destinationFaceUp, bool wasFlipSummon)
        {
            if (engine == null || card?.Def == null) return;
            // BattleAndPositionRecords batch: The Unhappy Girl's battle record ends when its position changes (EVENT_CHANGE_POS).
            BattleAndPositionRecordsRuntime.NotePositionChanged(engine, card);
            if (card.IsNegated) return;
            // SimultaneousTriggers batch: a mass change (Curse of Fiend) puts this trigger on the Chain with the
            // others it caused, instead of resolving it in the middle of the change.
            if (SimultaneousTriggersRuntime.TryCapturePositionChange(engine, who, card, from, to, destinationFaceUp,
                    wasFlipSummon))
                return;
            if (engine.PendingActivation != null) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.PositionChanged))
            {
                if (c == null) continue;
                if (c.PositionFromAttack && from != BattlePosition.Attack) continue;
                if (c.PositionToDefense && to != BattlePosition.Defense) continue;
                if (c.PositionFromDefense && from != BattlePosition.Defense) continue;
                if (c.PositionToAttack && to != BattlePosition.Attack) continue;
                if (c.RequireDestinationFaceUp && !destinationFaceUp) continue;
                if (wasFlipSummon && !c.FiresOnFlipSummon) continue;
                if (c.Action == EffectActionKind.Destroy || c.Action == EffectActionKind.ReturnToHand)
                {
                    var targets = CollectTargets(engine, who, c, card);
                    if (targets.Count == 0)
                    {
                        engine.Log($"{card.Name}: no monster to choose. Nothing else happens.");
                        continue;
                    }

                    if (who == null || !who.IsPlayer)
                    {
                        var dummy = false;
                        ApplyClause(engine, who, card, c, AutoPick(c, targets, who, engine), ref dummy, ref dummy);
                        continue;
                    }

                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        IsMonsterEffect = true,
                        UsesTextProgram = true,
                        AwaitingPositionChoice = true,
                        TargetPicksRemaining = 1
                    };
                    pending.LegalTargets.AddRange(targets);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    return;
                }

                if (c.Action == EffectActionKind.PlaceHandCardsOnBottomOfDeck)
                {
                    if (who == null || !who.IsPlayer || who.Hand == null || who.Hand.Count == 0)
                        continue;
                    var pending = new PendingActivation
                    {
                        Controller = who,
                        Card = card,
                        IsMonsterEffect = true,
                        UsesTextProgram = true,
                        AwaitingHandToDeckBottom = true,
                        TargetPicksAreUpTo = true,
                        TargetPicksRemaining = who.Hand.Count
                    };
                    pending.LegalTargets.AddRange(who.Hand);
                    engine.SetPendingActivation(pending);
                    engine.Log(pending.Prompt);
                    return;
                }

                var d = false;
                ApplyClause(engine, who, card, c, null, ref d, ref d);
            }
        }

        static bool ResolveOrderedPick(DuelEngine engine, CardInstance target)
        {
            var p = engine.PendingActivation;
            if (p == null || target == null) return false;
            var resolved = engine.ResolveLegalEffectTarget(target);
            if (resolved == null) return false;
            if (!p.ChosenTargets.Contains(resolved))
                p.ChosenTargets.Add(resolved);
            p.LegalTargets.Remove(resolved);
            if (p.TargetPicksRemaining > 0)
                p.TargetPicksRemaining--;
            if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0 &&
                (p.TargetPicksAreUpTo || p.AwaitingDeckOrder || p.AwaitingHandToDeckBottom))
            {
                engine.Log($"{p.Card?.Name}: choose another card ({p.TargetPicksRemaining} remaining).");
                engine.NotifyPublic();
                return true;
            }

            FinishOrderedPick(engine, p.ChosenTargets.Count > 0);
            return true;
        }

        public static bool FinishOrderedPick(DuelEngine engine, bool useChosen)
        {
            var p = engine?.PendingActivation;
            if (p == null) return false;
            var who = p.Controller;
            var card = p.Card;
            var chosen = p.ChosenTargets != null ? p.ChosenTargets.ToList() : new List<CardInstance>();
            var excavated = p.ExcavatedIds != null ? p.ExcavatedIds.ToList() : new List<int>();
            var excavatedInfo = p.ExcavatedInfo.ToList();
            var excavatedCards = p.ExcavatedCards.ToList();
            var deckOrder = p.AwaitingDeckOrder;
            var handBottom = p.AwaitingHandToDeckBottom;
            var position = p.AwaitingPositionChoice;
            engine.ClearPendingActivation();
            if (deckOrder)
            {
                RestoreDeckOrder(who, excavated, excavatedInfo, excavatedCards, useChosen ? chosen : null);
                engine.Log($"{card?.Name}: Deck order set.");
            }
            else if (handBottom)
            {
                if (useChosen && who?.Hand != null && who.Deck != null)
                {
                    for (var i = chosen.Count - 1; i >= 0; i--)
                    {
                        var pick = chosen[i];
                        if (pick == null || !who.Hand.Contains(pick)) continue;
                        who.Hand.Remove(pick);
                        who.Deck.Add(pick.CardId);
                    }

                    engine.Log($"{card?.Name}: {chosen.Count} card(s) placed on the bottom of the Deck.");
                }
            }
            else if (position && useChosen && chosen.Count > 0)
            {
                var prog = CompiledEffectCache.GetOrCompile(card?.Def);
                var clause = prog?.ClausesFor(EffectTiming.PositionChanged)
                    .Find(c => c != null &&
                               (c.Action == EffectActionKind.Destroy ||
                                c.Action == EffectActionKind.ReturnToHand));
                if (clause != null)
                {
                    var dummy = false;
                    ApplyClause(engine, who, card, clause, chosen[0], ref dummy, ref dummy);
                }
            }

            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// Put the excavated top cards back: the chosen ones first (top first), then the rest in their old order.
        /// PerCopyDeck batch (v163): each copy goes back with its own metadata (<paramref name="infos"/>, aligned with
        /// <paramref name="excavated"/>); a chosen picker instance is matched to its copy through
        /// <paramref name="cards"/>, else to the first unused copy with its id.
        /// </summary>
        static void RestoreDeckOrder(DuelistState who, List<int> excavated, List<DeckCopyInfo> infos,
            List<CardInstance> cards, List<CardInstance> chosen)
        {
            if (who?.Deck == null || excavated == null || excavated.Count == 0) return;
            var used = new bool[excavated.Count];
            var seq = new List<int>();
            if (chosen != null)
            {
                foreach (var c in chosen)
                {
                    if (c == null) continue;
                    var at = cards != null ? cards.IndexOf(c) : -1;
                    if (at < 0 || at >= excavated.Count || used[at] || excavated[at] != c.CardId)
                    {
                        at = -1;
                        for (var i = 0; i < excavated.Count; i++)
                            if (!used[i] && excavated[i] == c.CardId) { at = i; break; }
                    }

                    if (at < 0) continue;
                    used[at] = true;
                    seq.Add(at);
                }
            }

            for (var i = 0; i < excavated.Count; i++)
                if (!used[i]) seq.Add(i);
            for (var k = seq.Count - 1; k >= 0; k--)
            {
                var i = seq[k];
                who.Deck.Insert(0, excavated[i], infos != null && i < infos.Count ? infos[i] : null);
            }
        }

        static void ResolveNamedThenForceRaceAttack(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (engine == null || clause == null) return;
            var destroyed = 0;
            var destroyer = source != null ? engine.ControllerOf(source) : null;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                var cards = new List<CardInstance>();
                foreach (var m in side.MonstersOnField()) cards.Add(m);
                foreach (var st in side.SpellTrapsOnField()) cards.Add(st);
                if (side.FieldSpellZone?.Occupant != null)
                    cards.Add(side.FieldSpellZone.Occupant);
                foreach (var card in cards)
                {
                    if (card == null || !card.FaceUp || !card.IsNamed(clause.NamedCard)) continue;
                    engine.SendCardToGrave(side, card, source);
                    // Dragon Piper destroying the opponent's Dragon Capture Jar: Driving Snow may answer.
                    SpellTrapDestroyedResponseRuntime.NoteDestroyedByEffect(engine, side, card, destroyer);
                    destroyed++;
                }
            }

            engine.Log(destroyed > 0
                ? $"{source?.Name}: destroyed {destroyed} \"{clause.NamedCard}\"."
                : $"{source?.Name}: no face-up \"{clause.NamedCard}\".");
            if (destroyed <= 0) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m == null || !m.FaceUp || m.Race == null) continue;
                    if (m.Race.IndexOf(clause.RaceFilter ?? "", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    engine.ApplyEffectPositionChange(m, BattlePosition.Attack, true);
                }
            }
        }

        static void BeginReorderTop(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Deck == null || clause == null) return;
            var n = clause.Amount > 0 ? clause.Amount : 5;
            if (n > who.Deck.Count) n = who.Deck.Count;
            if (n <= 0) return;
            var excavated = new List<int>();
            var infos = new List<DeckCopyInfo>();
            for (var i = 0; i < n; i++)
            {
                excavated.Add(who.Deck[0]);
                // PerCopyDeck batch (v163): each copy keeps its metadata while it is out of the Deck.
                infos.Add(who.Deck.TakeAt(0));
            }

            if (who == null || !who.IsPlayer)
            {
                RestoreDeckOrder(who, excavated, infos, null, null);
                engine.Log($"{source?.Name}: looked at {n} and put them back.");
                return;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = source,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                AwaitingDeckOrder = true,
                TargetPicksAreUpTo = true,
                TargetPicksRemaining = n
            };
            pending.ExcavatedIds.AddRange(excavated);
            pending.ExcavatedInfo.AddRange(infos);
            foreach (var id in excavated)
            {
                var inst = engine.CreateCardInstance(id);
                pending.LegalTargets.Add(inst);
                pending.ExcavatedCards.Add(inst);
            }

            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
        }

        public static bool TryResolveOptionalPay(DuelEngine engine, bool pay)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingOptionalPay) return false;
            var who = p.Controller;
            var card = p.Card;
            var cost = p.CostNumeric > 0 ? p.CostNumeric : 500;
            engine.ClearPendingActivation();
            if (pay && who != null && card != null && who.LifePoints >= cost &&
                who.TryFindMonster(card, out _))
            {
                engine.PayLifePoints(who, cost);
                var opp = engine.OpponentOf(who);
                if (opp != null && !MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, card, card) &&
                    engine.TryTakeControl(opp, card))
                    engine.Log($"{card.Name}: paid {cost} LP. Control shifts to {opp.Name}.");
                else
                    engine.Log($"{card.Name}: paid {cost} LP, but control could not shift.");
            }
            else
                engine.Log($"{card?.Name}: control stays.");

            engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
            return true;
        }

        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingCoinCall) return false;
            if (p.AttackDeclarationCoin)
                return FinishAttackDeclarationCoin(engine, callHeads);
            // Fairy Box (BattleWindows batch): its controller's call at an opponent's attack declaration.
            if (BattleWindowsRuntime.TryFinishCoinCall(engine, callHeads))
                return true;
            // SimpleSpellTraps batch: Fiend Comedian's call, made as its effect resolves.
            if (SimpleSpellTrapsRuntime.TryFinishCoinCall(engine, callHeads))
                return true;
            // BattleOutcomeTriggers batch (v162): Kryuel's call when it is sent to the GY by battle.
            if (BattleOutcomeTriggersRuntime.TryFinishCoinCall(engine, callHeads))
                return true;
            var who = p.Controller;
            var card = p.Card;
            var prog = CompiledEffectCache.GetOrCompile(card);
            engine.ClearPendingActivation();
            // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo it before the call is judged.
            CoinDiceInterruptsRuntime.TossCoin(engine, who, card, toss => toss == callHeads, tossHeads =>
            {
                engine.Log(
                    $"{who?.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(tossHeads ? "Heads" : "Tails")}.");
                var right = callHeads == tossHeads;
                foreach (var c in prog?.ClausesFor(EffectTiming.Activate) ?? new List<EffectClause>())
                {
                    if (c.Action == EffectActionKind.CoinCallDestroyOppOrSelf)
                        ResolveTimeWizard(engine, who, card, right);
                    else if (c.Action == EffectActionKind.CoinCallDoubleOrHalveAtk)
                        ResolveGoddessCoin(engine, card, right);
                }

                if (card != null) card.EffectUsedThisTurn = true;
                engine.NotifyPublic();
            });
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
                // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var m in opp.MonstersOnField().ToList())
                        DestroyCard(engine, m, source);
                });
                OfferDarkSage(engine, who);
                return;
            }

            engine.Log($"{source?.Name}: call wrong — destroy your monsters, take half ATK damage.");
            // Destroyed together; their GY triggers activate after the damage (FlipMassEffects v142).
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                var atk = 0;
                foreach (var m in who.MonstersOnField().ToList())
                {
                    if (m != null && m.FaceUp)
                        atk += Math.Max(0, m.CurrentAtk);
                    DestroyCard(engine, m, source);
                }

                engine.ApplyEffectDamage(who, atk / 2, source?.Name);
            });
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

        /// <param name="positions">TokensDeclarationsAndPicks batch: the face-up position chosen for each Token (null: the
        /// clause's own position).</param>
        static void SummonTokens(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source = null,
            IReadOnlyList<BattlePosition> positions = null)
        {
            var dest = clause.TokenToOpponent ? engine.OpponentOf(who) : who;
            if (dest == null) return;
            var n = clause.TokenFillRemainingZones
                ? dest.MonsterZones.Count(z => z != null && z.IsEmpty)
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
                var tokPos = positions != null && i < positions.Count ? positions[i] : pos;
                tok.Position = tokPos;
                if (!engine.SpecialSummonToField(dest, tok, tokPos, true))
                    break;
                engine.Log($"Token Special Summoned: {tok.Name} ({tok.CurrentAtk}/{tok.CurrentDef}).");
                // CallsDiceAndSetTriggers batch (v163): Cobra Jar — destroyed by battle, the Token burns the resolving
                // player's opponent.
                if (clause.TokenBattleDestroyedDamageToOpponent > 0)
                    CallsDiceAndSetTriggersRuntime.BindTokenBattleBurn(engine, tok, who,
                        clause.TokenBattleDestroyedDamageToOpponent, source);
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
                // A zone an effect made unusable (Dimensionhole, LocksAndZones batch) is not an empty zone
                // (ygopro Duel.GetLocationCount does not count a disabled zone).
                if (z.IsEmpty) empty++;
                else if (z.Occupant != null) monsters++;
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
            if (CompiledActivationChainRuntime.IsRecording(trap))
            {
                DeferMagicalHats(engine, who, trap, host, hats, 0); // when its Chain Link resolves
                return;
            }
            // Royal Decree / Jinzo: Magical Hats resolves with no effect (nothing is Set or summoned).
            if (ContinuousNegationRuntime.NegatesOnFieldResolution(engine, trap)) return;
            // Light of Intervention (LocksAndZones batch; ygopro c81210420): the chosen monster goes to face-up
            // Defense instead of being Set, the hats are Special Summoned face-up (SpecialSummonToField), and
            // nothing is shuffled.
            var faceUpOnly = HideHatsHost(engine, host);
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

            if (faceUpOnly)
            {
                engine.Log("Magical Hats: monsters cannot be face-down (Light of Intervention) — nothing is shuffled.");
                return;
            }

            ShuffleFaceDownMonsters(engine, who, placed);
            engine.Log("Magical Hats: 3 face-down Defense Position monsters shuffled.");
        }

        /// <summary>
        /// Magical Hats' chosen monster: Set it (face-down Defense) — or, while Light of Intervention applies,
        /// change a face-up one to face-up Defense Position instead. Returns true when monsters cannot be
        /// face-down (the hats come face-up and are not shuffled).
        /// </summary>
        static bool HideHatsHost(DuelEngine engine, CardInstance host)
        {
            if (LocksAndZonesRuntime.CannotTurnFaceDown(engine, host))
            {
                if (host.FaceUp && host.Position != BattlePosition.Defense)
                    engine.ApplyEffectPositionChange(host, BattlePosition.Defense, true);
                return true;
            }

            host.FaceUp = false;
            host.Position = BattlePosition.Defense;
            return false;
        }

        static void ResolveMagicalHatsAnime(DuelEngine engine, DuelistState who, CardInstance trap,
            CardInstance host, int tokenCount)
        {
            if (engine == null || who == null || host == null) return;
            if (CompiledActivationChainRuntime.IsRecording(trap))
            {
                DeferMagicalHats(engine, who, trap, host, null, tokenCount); // when its Chain Link resolves
                return;
            }
            // Royal Decree / Jinzo: Magical Hats resolves with no effect (nothing is Set or summoned).
            if (ContinuousNegationRuntime.NegatesOnFieldResolution(engine, trap)) return;
            var faceUpOnly = HideHatsHost(engine, host); // Light of Intervention: see ResolveMagicalHats
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

            if (faceUpOnly)
            {
                engine.Log("Magical Hats: monsters cannot be face-down (Light of Intervention) — nothing is shuffled.");
                return;
            }

            ShuffleFaceDownMonsters(engine, who, placed);
            engine.Log($"Magical Hats: {placed.Count} face-down hats shuffled (anime).");
        }

        internal static void ShuffleFaceDownMonsters(DuelEngine engine, DuelistState who,
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
            // Destroyed together (FlipMassEffects v142: their GY triggers activate afterwards).
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in opp.MonstersOnField().ToList())
                {
                    if (m == null) continue;
                    var atk = m.FaceUp ? m.CurrentAtk : (m.Def?.atk ?? 0);
                    if (!MatchesVirusDestroyAtk(atk, thresh, leq)) continue;
                    DestroyCard(engine, m, source);
                }
            });

            if (opp.Hand != null)
            {
                // SpellEventsAndHandSendTriggers batch: a monster destroyed in the hand by this effect was sent from the
                // hand to the GY by the opponent's card effect (Elephant Statue of Blessing, Lua EVENT_TO_GRAVE + rp~=tp);
                // its trigger waits until every hand card is destroyed.
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    foreach (var h in opp.Hand.ToList())
                    {
                        if (h?.Def == null || !h.Def.IsMonster) continue;
                        if (!MatchesVirusDestroyAtk(h.Def.atk, thresh, leq)) continue;
                        opp.Hand.Remove(h);
                        RedirectAndReplaceRuntime.PutInGraveyard(engine, opp, h); // Banisher of the Light (v142)
                        engine.Log($"{label} destroys {h.Name} in the hand.");
                        DiscardTriggersRuntime.NoteSentFromHand(engine, opp, h, who);
                    }
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
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
                // PhaseTimersAndDeckTop batch (v163): the card that holds this turn count (Pyro Clock of Destiny's pick).
                PhaseTimersAndDeckTopRuntime.NoteTurnCountHolder(engine, opp, source, who);
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

        internal static void DestroyVirusDeckPick(DuelEngine engine, DuelistState who, CardInstance pick,
            string label = null)
        {
            if (who?.Deck == null || pick == null) return;
            // PerCopyDeck batch (v163): the pick was made before the removal; it takes the copy's owner (its owner's GY).
            var at = who.Deck.IndexOf(pick.CardId);
            if (at >= 0) PerCopyDeckRuntime.TakeFromDeck(who, at, pick);
            RedirectAndReplaceRuntime.PutInGraveyard(engine, who, pick); // Banisher of the Light (v142)
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
                { who.Hand.Remove(sage); engine.MarkSpecialSummonFromHand(sage); } // Chain Energy toll (RuleLocks batch)
            else
                who.Deck?.Remove(OfficialEffectRegistry.DarkSageId);
            if (!engine.SpecialSummonToField(who, sage, BattlePosition.Attack, true))
            {
                engine.SendCardToGrave(who, sage);
                engine.Log("Dark Sage: Special Summon failed.");
            }
        }

        /// <summary>
        /// One step of a virus's turn count on <paramref name="victim"/> (Deck Devastation Virus, Lua s.turnop): the End
        /// Phase of the victim's turn, or Pyro Clock of Destiny. At 0 the drawn cards are no longer checked.
        /// </summary>
        public static void AdvanceVirusLinger(DuelEngine engine, DuelistState victim)
        {
            if (engine == null || victim == null || victim.CrushCardLingerEndsRemaining <= 0) return;
            victim.CrushCardLingerEndsRemaining--;
            engine.Log(victim.CrushCardLingerEndsRemaining > 0
                ? $"Virus: {victim.CrushCardLingerEndsRemaining} more of {victim.Name}'s turns to check."
                : $"Virus: {victim.Name}'s draws are no longer checked.");
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
            RedirectAndReplaceRuntime.PutInGraveyard(engine, who, drawn); // Banisher of the Light (v142)
            engine.Log($"Virus linger destroys drawn {drawn.Name}.");
            // SpellEventsAndHandSendTriggers batch: destroyed in the hand by the virus player's effect (Lua rp = its owner).
            DiscardTriggersRuntime.NoteSentFromHand(engine, who, drawn, engine.OpponentOf(who));
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

        internal static void EquipCardTo(DuelEngine engine, DuelistState from, CardInstance equip,
            CardInstance host, int atkBonus, EffectClause clause = null)
        {
            if (equip == null || host == null) return;
            if (clause?.Action == EffectActionKind.EquipThisToTarget && HostAlreadyHasUnion(host))
            {
                engine.Log($"{host.Name} already has a Union — {equip.Name} cannot equip.");
                engine.SendCardToGrave(engine.ControllerOf(equip) ?? from, equip);
                return;
            }
            var owner = engine.ControllerOf(equip) ?? from;
            if (owner != null)
            {
                var foundMi = owner.TryFindMonster(equip, out var mi);
                if (foundMi)
                {
                    owner.MonsterZones[mi].Occupant = null;
                }
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
                else if (equip.Def != null && equip.Def.IsMonster &&
                         (equip.Def.type ?? "").IndexOf("Union", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                         owner.SpellTrapZones.Any(z => z.Occupant == null))
                {
                    var zi = engine.FirstEmptySpellTrap(owner);
                    if (zi >= 0)
                    {
                        owner.SpellTrapZones[zi].Occupant = equip;
                        equip.FaceUp = true;
                    }
                }
            }

            equip.EquippedTo = host;
            equip.GrantedEquipHost = null;
            equip.EquipTurnCounter = 0;
            equip.Counters = 0;
            if (!host.Equips.Contains(equip))
                host.Equips.Add(equip);
            if (clause != null && clause.ReplaceHostOriginalAtkDef && equip.Def != null)
            {
                host.OriginalAtkOverride = equip.Def.atk;
                host.OriginalDefOverride = equip.Def.def;
            }

            engine.Log($"{equip.Name} is equipped to {host.Name}.");
            EquipFamilyRuntime.NoteEquipped(engine, equip, host);
        }

        static void UnequipAndSummon(DuelEngine engine, DuelistState who, CardInstance equip)
        {
            if (equip?.EquippedTo == null) return;
            // UnionRiders batch (v175): only a Union in its Union state can unequip itself (not one Union Rider took).
            if (UnionRidersRuntime.UnionTextOff(equip)) return;
            var host = equip.EquippedTo;
            host.Equips.Remove(equip);
            equip.EquippedTo = null;
            var owner = engine.ControllerOf(equip) ?? who;
            if (owner != null)
            {
                foreach (var z in owner.SpellTrapZones)
                {
                    if (z.Occupant == equip)
                    {
                        z.Occupant = null;
                        break;
                    }
                }
            }
            if (engine.SpecialSummonToField(who, equip, BattlePosition.Attack, true))
                engine.Log($"{equip.Name} unequipped and Special Summoned.");
            else
                engine.SendCardToGrave(who, equip);
        }

        static bool DefMatchesSummonFilter(CardDef def, EffectClause clause)
        {
            if (def == null || clause == null || !def.IsMonster || def.IsExtraDeck) return false;
            // Named-card Semi-Nomi (Wall Shadow): only a clause naming it; the engine gate checks the source.
            if (!TributeSummonsRuntime.SemiNomiDefMatches(def, clause)) return false;
            // Toon procedure (Toons batch): a copy in the hand or Deck was never properly summoned.
            if (ToonsRuntime.DefNeedsOwnProcedure(def)) return false;
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
                !ClassicEraTemplates.RaceMatches(def.race, clause.RaceFilter))
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

        /// <summary>
        /// The 1-Union limit (Lua aux.CheckUnionEquip / Card.GetUnionCount): only a Union in its Union state counts
        /// (EFFECT_UNION_STATUS). UnionRiders review fix (v175): a Union another card took (Union Rider,
        /// EquipByEffectAndLimitRegister) or one equipped "as an Equip Spell Card with this effect" does not.
        /// </summary>
        public static bool HostAlreadyHasUnion(CardInstance host, CardInstance except = null)
        {
            if (host?.Equips == null) return false;
            foreach (var eq in host.Equips)
            {
                if (eq == null || ReferenceEquals(eq, except)) continue;
                if (UnionRidersRuntime.InUnionState(eq))
                    return true;
            }
            return false;
        }

        static bool SkipUnionBranch(EffectClause c, CardInstance card, bool wasEquipped) =>
            c != null &&
            (c.Action == EffectActionKind.UnequipThisSpecialSummon && !wasEquipped ||
             c.Action == EffectActionKind.EquipThisToTarget && wasEquipped);

        /// <param name="byEffectOf">
        /// The player whose card effect mills (DiscardTriggers v142: Penguin Knight); null for a cost
        /// (Gravekeeper's Servant, attack mills), which never triggers.
        /// </param>
        internal static void SendTopOfDeckToGy(DuelEngine engine, DuelistState who, int n, DuelistState byEffectOf = null)
        {
            if (engine == null || who?.Deck == null) return;
            for (var i = 0; i < n && who.Deck.Count > 0; i++)
            {
                var id = who.Deck[0];
                who.Deck.RemoveAt(0);
                var inst = engine.CreateCardInstance(id);
                // Banisher of the Light (RedirectAndReplace batch, v142): banished instead.
                if (RedirectAndReplaceRuntime.PutInGraveyard(engine, who, inst))
                    engine.Log($"{who.Name} sends {inst.Name} from the Deck to the GY.");
                // Notes only a card that reached the GY (it checks the GY): a banished Penguin Knight never activates.
                DiscardTriggersRuntime.NoteSentFromDeck(engine, who, inst, byEffectOf);
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
            var inst = chosenTarget ?? engine.CreateCardInstance(id);
            // PerCopyDeck batch (v163): the instance takes the copy's owner; a failed Summon puts the same copy back.
            var copy = PerCopyDeckRuntime.TakeFromDeck(who, index, inst);
            if (!engine.SpecialSummonToField(who, inst, clause.SummonInDefense ? BattlePosition.Defense : BattlePosition.Attack, true))
                who.Deck.Insert(index, id, copy);
            else
                engine.Log($"Special Summoned {inst.Name} from Deck.");
        }

        static void TrySpecialSummonNamed(DuelEngine engine, DuelistState who, EffectClause clause, CardInstance source = null)
        {
            // The named card's own effect completes a "Must first be Special Summoned with "X"" procedure
            // (Magical Labyrinth → Wall Shadow, Elegant Egotist → Harpie Lady Sisters; TributeSummons batch).
            var previous = TributeSummonsRuntime.BeginNamedSummon(source);
            try
            {
                TrySpecialSummonNamedCore(engine, who, clause, source);
            }
            finally
            {
                TributeSummonsRuntime.EndNamedSummon(previous);
            }
        }

        static void TrySpecialSummonNamedCore(DuelEngine engine, DuelistState who, EffectClause clause,
            CardInstance source)
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
                    engine.MarkSpecialSummonFromHand(hit); // Chain Energy toll (RuleLocks batch)
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
                var pos = clause.SummonInDefense ? BattlePosition.Defense : BattlePosition.Attack;
                var summoned = 0;
                for (var i = 0; i < who.Deck.Count; i++)
                {
                    var id = who.Deck[i];
                    if (!engine.Database.TryGet(id, out var def) || !DefMatchesSummonFilter(def, clause))
                        continue;
                    if (engine.FirstEmpty(who.MonsterZones) < 0) break;
                    var inst = engine.CreateCardInstance(id);
                    // PerCopyDeck batch (v163): a failed Summon puts the same copy back (owner, face-up state).
                    var copy = PerCopyDeckRuntime.TakeFromDeck(who, i, inst);
                    if (engine.SpecialSummonToField(who, inst, pos, !clause.SummonFaceDown))
                    {
                        engine.Log($"Special Summoned {inst.Name} from Deck" +
                                   (clause.SummonFaceDown && !inst.FaceUp ? " in face-down Defense Position." : "."));
                        summoned++;
                        if (!clause.SummonAllCopies)
                        {
                            engine.Rng.Shuffle(who.Deck);
                            return;
                        }
                        i--; // the list shifted
                        continue;
                    }

                    who.Deck.Insert(i, id, copy);
                    break;
                }

                if (summoned > 0)
                {
                    engine.Rng.Shuffle(who.Deck);
                    return;
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
                    engine.NextSpecialSummonFromGraveyard = true;
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
                NotifyTargetingEffectsResolved(engine, picks, TargetingTruthRuntime.ClauseTargets(dual));
                engine.NotifyPublic();
                return true;
            }

            var exactMulti = p.TargetPicksRemaining > 1 ||
                             (p.ChosenTargets.Count > 0 && p.TargetPicksRemaining > 0 &&
                              activate != null &&
                              activate.Exists(c => c != null && c.ControllerTargetCount > 1));
            if (dual == null && (p.TargetPicksAreUpTo || exactMulti))
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
                    activate.Exists(TargetingTruthRuntime.ClauseTargets));
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
                NotifyTargetingEffectsResolved(engine, new[] { first, target }, TargetingTruthRuntime.ClauseTargets(dual));
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
                RequiresTargetChoice = true,
                SummonFaceDown = c.SummonFaceDown,
                SummonInDefense = c.SummonInDefense,
                SummonInAttack = c.SummonInAttack,
                SummonToTargetsOwner = c.SummonToTargetsOwner
            };
        }

        static void ApplySecondTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c,
            CardInstance second)
        {
            if (engine == null || c == null || second == null) return;
            if (CompiledActivationChainRuntime.IsRecording(source))
            {
                DeferSecondTarget(engine, who, source, c, second); // when its Chain Link resolves
                return;
            }
            var dummy = false;
            var op = new EffectClause
            {
                Action = c.SecondAction == EffectActionKind.None
                    ? EffectActionKind.Banish
                    : c.SecondAction,
                Zone = c.SecondZone,
                RequiresTargetChoice = true,
                // The Shallow Grave: second pick is also own-GY face-down Defense SS to that owner.
                SummonFaceDown = c.SummonFaceDown,
                SummonInDefense = c.SummonInDefense,
                SummonInAttack = c.SummonInAttack,
                SummonToTargetsOwner = c.SummonToTargetsOwner
            };
            ApplyClause(engine, who, source, op, second, ref dummy, ref dummy);
        }

        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163): the clause banishes the targets it collects from a GY zone that
        /// Spirit Elimination substitutes (<see cref="SpiritEliminationRuntime.SubstitutesTargetsIn"/>: its own Banish
        /// action, or Book of Life's second target while <see cref="CollectTargetsForZone"/> collects it).
        /// </summary>
        static bool BanishesGyTargets(EffectClause c)
        {
            if (c == null || !SpiritEliminationRuntime.SubstitutesTargetsIn(c.Zone)) return false;
            if (c.Action == EffectActionKind.Banish) return true;
            return c.RequiresSecondTarget && c.Zone == c.SecondZone &&
                   (c.SecondAction == EffectActionKind.Banish || c.SecondAction == EffectActionKind.None);
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

        /// <summary>
        /// <see cref="CardIsKind(WRLDZ.Data.CardDef, string)"/> for a card in play: a monster card equipped as an
        /// Equip Spell Card (Kiseitai, SpiritsAndMisc batch) is a Spell there, not a monster.
        /// </summary>
        static bool CardIsKind(CardInstance card, string kind)
        {
            if (SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell(card) && !string.IsNullOrEmpty(kind))
                return string.Equals(kind, "Spell", System.StringComparison.OrdinalIgnoreCase);
            return CardIsKind(card?.Def, kind);
        }

        /// <summary>Card belongs to a named "series" (archetype / printed name / treated-as).</summary>
        static bool CardIsKind(WRLDZ.Data.CardDef def, string kind)
        {
            if (def == null || string.IsNullOrEmpty(kind)) return true;
            if (string.Equals(kind, "Spell", System.StringComparison.OrdinalIgnoreCase))
                return def.IsSpell;
            if (string.Equals(kind, "Trap", System.StringComparison.OrdinalIgnoreCase))
                return def.IsTrap;
            if (string.Equals(kind, "EffectMonster", System.StringComparison.OrdinalIgnoreCase))
                return IsTrueEffectMonster(def);
            // BattleDamageEvents batch: Nubian Guard's "1 Continuous Spell Card".
            if (string.Equals(kind, BattleDamageEventsRuntime.ContinuousSpellKind, System.StringComparison.OrdinalIgnoreCase))
                return BattleDamageEventsRuntime.IsContinuousSpell(def);
            return true;
        }

        internal static bool CardMatchesSeries(WRLDZ.Data.CardDef def, string series)
        {
            if (def == null || string.IsNullOrEmpty(series)) return false;
            var cmp = System.StringComparison.OrdinalIgnoreCase;
            // "(This card is not treated as a "Crystal" card.)" — Violet Crystal.
            if (def.desc != null &&
                (def.desc.IndexOf($"not treated as a \"{series}\" card", cmp) >= 0 ||
                 def.desc.IndexOf($"not treated as an \"{series}\" card", cmp) >= 0))
                return false;
            if (!string.IsNullOrEmpty(def.archetype) && def.archetype.IndexOf(series, cmp) >= 0)
                return true;
            if (!string.IsNullOrEmpty(def.name) && def.name.IndexOf(series, cmp) >= 0)
                return true;
            return def.desc != null && def.desc.IndexOf($"treated as an \"{series}\"", cmp) >= 0;
        }

        /// <summary>
        /// The legal targets of clause <paramref name="c"/> for <paramref name="who"/> (<paramref name="except"/> =
        /// the source card). Internal for the RedirectAndReplace batch (v142): Fairy's Hand Mirror / Shift ask which
        /// monsters an answered link's card could target.
        /// </summary>
        internal static List<CardInstance> CollectTargets(DuelEngine engine, DuelistState who, EffectClause c,
            CardInstance except, int costNumeric = 0)
        {
            // TypeDeclaration batch (v142): declaration tokens (one per Monster Type), not cards — no card filter applies.
            if (c.Zone == EffectZoneFilter.DeclaredMonsterTypes)
                return TypeDeclarationRuntime.MonsterTypeTokens(engine);
            // TokensDeclarationsAndPicks batch: DNA Transplant's Attribute declaration tokens.
            if (c.Zone == EffectZoneFilter.DeclaredAttributes)
                return TokensDeclarationsAndPicksRuntime.AttributeDeclarationTokens();
            var list = new List<CardInstance>();
            var opp = engine.OpponentOf(who);
            // TargetingTruth batch (v177): a choice that does not target (Fissure, Hammer Shot) ignores "cannot be targeted"
            // protections (Lord of D.); every other list keeps filtering them.
            var shieldable = !TargetingTruthRuntime.PicksWithoutTargeting(c);
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
                case EffectZoneFilter.OppSpellTraps:
                    // Spiritualism: the opponent's Spell/Traps only (their Field Spell included).
                    foreach (var st in opp.SpellTrapsOnField())
                        if (st != null) list.Add(st);
                    break;
                case EffectZoneFilter.EitherGyMonsters:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster && !g.Def.IsExtraDeck) list.Add(g);
                    break;
                case EffectZoneFilter.EitherGyCards:
                    foreach (var g in who.Graveyard)
                        if (g != null) list.Add(g);
                    foreach (var g in opp.Graveyard)
                        if (g != null) list.Add(g);
                    break;
                case EffectZoneFilter.ControllerGyCards:
                    foreach (var g in who.Graveyard)
                        if (g != null) list.Add(g);
                    break;
                case EffectZoneFilter.OppFaceUpMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (c.Action == EffectActionKind.EffectDamageBothFromOriginalAtk &&
                            m.CurrentAtk > opp.LifePoints) continue;
                        // Dark Spirit of the Silent (BattleWindows batch): "another" face-up monster — not the attacker.
                        if (c.Action == EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead &&
                            ReferenceEquals(m, engine.DeclaredAttacker)) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.OppDefensePositionMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null || m.Position != BattlePosition.Defense) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.FieldAnyMonster:
                    foreach (var m in who.MonstersOnField())
                    {
                        // SimpleTriggersAndSwarm batch: Tsukuyomi may target itself (TargetMayBeThisCard).
                        if (m == except && c.Action != EffectActionKind.ReturnToHand && !c.TargetMayBeThisCard) continue;
                        if (c.Action == EffectActionKind.Banish && !m.FaceUp) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == except && c.Action != EffectActionKind.ReturnToHand) continue;
                        if (c.Action == EffectActionKind.Banish && !m.FaceUp) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.ControllerGyMonsters:
                    foreach (var g in who.Graveyard)
                    {
                        if (g?.Def == null || !g.Def.IsMonster) continue;
                        // FlipCostsAndFusion batch (Supply): only monsters sent there as Fusion Material (Lua
                        // IsReason(REASON_FUSION)); a Fusion Monster qualifies too (it returns to the Extra Deck).
                        if (c.RequiresSentAsFusionMaterial)
                        {
                            if (FlipCostsAndFusionRuntime.IsFusionMaterialInGy(who, g)) list.Add(g);
                            continue;
                        }
                        // Backup Soldier: non-Effect monsters (effectless Fusions included) with ATK ≤ cap.
                        if (c.RequiresNonEffectMonster)
                        {
                            if (IsTrueEffectMonster(g.Def)) continue;
                            if (c.AmountIsAtkMax && (g.Def.atk < 0 || g.Def.atk > c.Amount)) continue;
                            list.Add(g);
                            continue;
                        }
                        if (g.Def.IsExtraDeck) continue;
                        if (c.RequiresNormalMonster &&
                            (g.Def.type == null ||
                             g.Def.type.IndexOf("Normal", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                             g.Def.type.IndexOf("Effect", System.StringComparison.OrdinalIgnoreCase) >= 0))
                            continue;
                        if (c.AmountIsLevel && c.Amount > 0 && g.Level > c.Amount)
                            continue;
                        // SummonTriggersAndTributes batch (v163): "target 1 "X" monster in your GY; Special Summon it"
                        // (Gravekeeper's Chief) — this text-path list did not read the name (the legacy mirror does).
                        // SpellCounterSpenders batch: Miracle Restoring's second name ("X" or "Y").
                        if (c.Action == EffectActionKind.SpecialSummonFromGy && !string.IsNullOrEmpty(c.NamedCard) &&
                            !g.IsNamed(c.NamedCard) && !NameMatchesQuoted(g.Def, c.NamedCard, c.NamedCardIsSeries) &&
                            (string.IsNullOrEmpty(c.AltNamedCard) ||
                             (!g.IsNamed(c.AltNamedCard) && !NameMatchesQuoted(g.Def, c.AltNamedCard, false))))
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
                             !string.Equals(m.Name, c.NamedCard, System.StringComparison.OrdinalIgnoreCase)) &&
                            (string.IsNullOrEmpty(c.AltNamedCard) ||
                             m.Name == null ||
                             !string.Equals(m.Name, c.AltNamedCard, System.StringComparison.OrdinalIgnoreCase)))
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
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
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
                // SimpleTargetUtility batch (v173): Fairy of the Spring — Equip Spells in the GY.
                case EffectZoneFilter.ControllerGyEquipSpells:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null && g.Def.IsSpell && g.Def.IsEquipSpell) list.Add(g);
                    break;
                // SimpleTargetUtility batch (v173): Arsenal Summoner — every distinct main-Deck card;
                // the shared TargetSeriesName / ExceptNamedCard filters below narrow it to the series
                // minus the printed except list.
                case EffectZoneFilter.ControllerDeckCards:
                    AddUniqueDeck(engine, who, list, def => def != null && !def.IsExtraDeck);
                    break;
                // ChoiceBullets batch (v173): Cyber Raider — face-up Equip Cards on the field currently equipped
                // to a monster (either player's Spell & Trap Zones; Lua LOCATION_SZONE + TYPE_EQUIP + GetEquipTarget()).
                case EffectZoneFilter.FieldEquipCards:
                    foreach (var side in new[] { who, opp })
                        foreach (var st in side.SpellTrapsOnField())
                            if (st != null && st.FaceUp && st.EquippedTo != null &&
                                (st.Def.IsEquipSpell || st.Def.IsMonster))
                                list.Add(st);
                    break;
                case EffectZoneFilter.OppMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
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
                            // Toon procedure (Toons batch): a hand copy cannot be Special Summoned by an effect.
                            if (c.Action == EffectActionKind.SpecialSummonFromHand &&
                                ToonsRuntime.BlocksSpecialSummon(h))
                                continue;
                            list.Add(h);
                        }
                    }
                    break;
                case EffectZoneFilter.AnyCardOnField:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    foreach (var m in opp.MonstersOnField())
                    {
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
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
                case EffectZoneFilter.OppGyMonsters:
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsMonster) list.Add(g);
                    break;
                case EffectZoneFilter.OppGyCards:
                    foreach (var g in opp.Graveyard)
                        if (g != null) list.Add(g);
                    break;
                // PerCopyDeck batch (v163): Graverobber.
                case EffectZoneFilter.OppGySpells:
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null && g.Def.IsSpell) list.Add(g);
                    break;
                case EffectZoneFilter.OppFaceDownMonsters:
                    foreach (var m in opp.MonstersOnField())
                        if (m != null && !m.FaceUp && !engine.IsDragonTargetProtected(m)) list.Add(m);
                    break;
                case EffectZoneFilter.FieldFaceDownMonsters:
                    FlipDefCheckRuntime.CollectFaceDownMonsters(engine, who, opp, except, list);
                    break;
                // FlipCostsAndFusion batch: De-Fusion — a face-up Fusion Monster on either field.
                case EffectZoneFilter.FieldFaceUpFusionMonsters:
                    FlipCostsAndFusionRuntime.CollectFaceUpFusions(engine, who, opp, list);
                    break;
                // NegationMonsters batch (v161): Double Snare — a face-up card that negates Trap effects.
                case EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects:
                    NegationMonstersRuntime.CollectDoubleSnareTargets(engine, who, opp, except, list);
                    break;
                // EquipModel batch: Tailor of the Fickle — Equip Cards with another correct face-up target.
                case EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget:
                    EquipModelRuntime.SwitchCandidates(engine, except, list);
                    break;
                // EquipAndSpellProtection batch: Magic Reflector — another face-up Spell you control.
                case EffectZoneFilter.ControllerFaceUpSpellsExceptThis:
                    EquipAndSpellProtectionRuntime.CollectControllerFaceUpSpells(who, except, list);
                    break;
                // StandbyBurnsAndDice batch (v162): Mask of Dispel — a face-up Spell on either field, not this card.
                case EffectZoneFilter.FieldFaceUpSpells:
                    StandbyBurnsAndDiceRuntime.CollectFaceUpSpells(who, opp, except, list);
                    break;
                // TargetedClassics Soft: Miracle Dig — face-up monsters in your banished pile.
                case EffectZoneFilter.ControllerBanishedFaceUpMonsters:
                    TargetedClassicsRuntime.CollectFaceUpBanishedMonsters(who, list);
                    break;
                // ChaosBanishSummons batch: Primal Seed — any card in your banished pile.
                case EffectZoneFilter.ControllerBanishedCards:
                    ChaosBanishSummonsRuntime.CollectBanishedCards(who, list);
                    break;
                case EffectZoneFilter.FaceDownSpellTraps:
                    foreach (var side in new[] { who, opp })
                        foreach (var st in side.SpellTrapsOnField())
                            if (st != null && st != except && !st.FaceUp) list.Add(st);
                    break;
                case EffectZoneFilter.HandOrDeckNamed:
                    foreach (var h in who.Hand)
                        if (h != null && h != except && h.Def != null && h.Def.IsMonster && DefMatchesAnyName(h.Def, c.NamedCard))
                            list.Add(h);
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsMonster && DefMatchesAnyName(def, c.NamedCard));
                    break;
                // SpellCounterSpenders batch: a face-up card on the field that can take a Spell Counter (Apprentice
                // Magician; "other": Pitch-Black Power Stone).
                case EffectZoneFilter.FieldFaceUpSpellCounterHolders:
                    list.AddRange(SpellCounterSpendersRuntime.CollectHolderTargets(engine, c, except));
                    break;
                case EffectZoneFilter.ControllerFaceUpSeriesMonsters:
                    // SummonLocksAndStatRules batch — Amazoness Spellcaster: a face-up archetype monster you control.
                    SummonLocksAndStatRulesRuntime.CollectFaceUpSeries(engine, who, c, list);
                    break;
                case EffectZoneFilter.ControllerDeckOrGyNamed:
                    // MfcSimpleSpellsB batch (v174) — Emblem of Dragon Destroyer: a monster in the Deck or
                    // the controller's GY whose printed name matches NamedCard. Deck first, then GY
                    // (ygopro LOCATION_DECK + LOCATION_GRAVE, resolved in that order).
                    AddUniqueDeck(engine, who, list,
                        def => def != null && def.IsMonster && NameMatchesQuoted(def, c.NamedCard, false));
                    foreach (var g in who.Graveyard)
                        if (g != null && g != except && g.Def != null && g.Def.IsMonster &&
                            NameMatchesQuoted(g.Def, c.NamedCard, false))
                            list.Add(g);
                    break;
                case EffectZoneFilter.OppHandCards:
                    foreach (var h in opp.Hand)
                        if (h?.Def != null) list.Add(h);
                    break;
                case EffectZoneFilter.AnyGyCards:
                    foreach (var g in who.Graveyard)
                        if (g?.Def != null) list.Add(g);
                    foreach (var g in opp.Graveyard)
                        if (g?.Def != null) list.Add(g);
                    break;
                case EffectZoneFilter.ControllerAnyMonsters:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == null || m == except) continue;
                        if (c.RequiresTargetOwnedByController && m.ControlOwner != null) continue; // borrowed
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.OppAnyMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.OppAttackPositionMonsters:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null || !m.FaceUp || m.Position != BattlePosition.Attack) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }

                    break;
                case EffectZoneFilter.DeckFieldSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsFieldSpell);
                    break;
                case EffectZoneFilter.DeckEquipSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsEquipSpell);
                    break;
                case EffectZoneFilter.DeckRitualMonsters:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsRitualMonster);
                    break;
                case EffectZoneFilter.DeckRitualSpells:
                    AddUniqueDeck(engine, who, list, def => def != null && def.IsRitualSpell);
                    break;
                case EffectZoneFilter.DeckRitualMonstersOrSpells:
                    AddUniqueDeck(engine, who, list,
                        def => def != null && (def.IsRitualMonster || def.IsRitualSpell));
                    break;
                case EffectZoneFilter.DeckMonstersRaceLevelLeq:
                    AddUniqueDeck(engine, who, list, def =>
                    {
                        if (def == null || !def.IsMonster || def.IsExtraDeck) return false;
                        var cap = c.Amount > 0 ? c.Amount : 4;
                        if (def.level > cap) return false;
                        if (string.IsNullOrEmpty(c.RaceFilter)) return true;
                        return ClassicEraTemplates.RaceMatches(def.race, c.RaceFilter);
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
                case EffectZoneFilter.AnotherAppropriateTarget:
                // SpellEventsAndHandSendTriggers batch: Fiend's Hand Mirror (another Spell/Trap Card on the field).
                case EffectZoneFilter.AnotherAppropriateSpellTrapTarget:
                    // RedirectAndReplace batch (v142): Fairy's Hand Mirror / Shift.
                    list.AddRange(RedirectAndReplaceRuntime.Candidates(engine, who, c, except));
                    break;
            }
            if (c.Action == EffectActionKind.SpecialSummonFromGy &&
                (c.Zone == EffectZoneFilter.EitherGyMonsters ||
                 c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                 c.Zone == EffectZoneFilter.OppGyMonsters))
            {
                list.RemoveAll(t => !CanSpecialSummonFromGyTarget(t));
                // SummonPromptsAndCosts batch (v163): a summon cost its summoner cannot pay (Tyrant Dragon's Tribute) makes
                // the monster an illegal target (ygopro IsCanBeSpecialSummoned). Mirror: SpellTrapEffects.CollectLegalTargets.
                list.RemoveAll(t => !SummonPromptsAndCostsRuntime.GyTargetSummonCostPayable(engine, who, c, t));
                // SummonLocksAndStatRules batch: Rivalry of Warlords (Lua EFFECT_FORCE_SPSUMMON_POSITION POS_FACEDOWN) —
                // a face-up Special Summon of another Type is impossible, so that monster is not a legal target
                // (ygopro IsCanBeSpecialSummoned, POS_FACEUP). Mirror: SpellTrapEffects.CollectLegalTargets.
                if (!c.SummonFaceDown)
                    list.RemoveAll(t => SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(engine,
                        c.SummonToTargetsOwner && opp.Graveyard.Contains(t) ? opp : who, t, out _));
            }


            if (c.Zone == EffectZoneFilter.EitherGyMonsters ||
                c.Zone == EffectZoneFilter.EitherGyCards ||
                c.Zone == EffectZoneFilter.ControllerGyCards ||
                c.Zone == EffectZoneFilter.ControllerGyMonsters ||
                c.Zone == EffectZoneFilter.OppGyMonsters ||
                c.Zone == EffectZoneFilter.OppGyCards ||
                c.Zone == EffectZoneFilter.OppGySpells ||
                c.Zone == EffectZoneFilter.ControllerGySpells ||
                c.Zone == EffectZoneFilter.ControllerGyTraps ||
                c.Zone == EffectZoneFilter.ControllerGyEquipSpells)
                list.RemoveAll(t =>
                    ContinuousProtections.CannotTargetCardInGy(engine, except, t));
            // RuleLocks batch: Kycoo the Ghost Destroyer — its controller's opponent cannot banish a card from either GY,
            // so a card in a GY is not a legal target of their banishing effect.
            if (c.Action == EffectActionKind.Banish && RuleLocksRuntime.BanishFromGyLocked(engine, who))
                list.RemoveAll(t => t != null && (who.Graveyard.Contains(t) || opp.Graveyard.Contains(t)));
            // ForcedActivationAndSubstitution batch (v163): Spirit Elimination — a clause that banishes its GY targets
            // (Soul Release, Gravedigger Ghoul, Kycoo, Witch Doctor of Chaos, Book of Life's second target; not Disappear)
            // targets the monsters an affected player controls instead of the monsters in their GY. The source is a
            // substitute too unless its effect needs it on the field (Witch Doctor can target itself, Lua exclusion nil).
            if (BanishesGyTargets(c))
                SpiritEliminationRuntime.SubstituteInList(engine, list,
                    SpiritEliminationRuntime.TargetSubstituteExcept(c, except), false, null, true,
                    SpiritEliminationRuntime.GyOwnersOfZone(c.Zone, who, opp));

            if (c.RequiresAtkLeqCost && costNumeric > 0)
                list.RemoveAll(t => t == null || t.CurrentAtk > costNumeric);

            // Freed: face-up monster with strictly higher ATK than this card (source = except).
            if (c.RequiresHigherAtkThanSource)
            {
                var srcAtk = except?.CurrentAtk ?? 0;
                list.RemoveAll(t => t == null || !t.FaceUp || t.CurrentAtk <= srcAtk);
            }

            if (c.RequiresTargetFaceUp)
                list.RemoveAll(t => t == null || !t.FaceUp);
            // FlipDownAndPeek batch (v162): "1 Set card" (A Man with Wdjat) — face-down cards only.
            if (c.RequiresTargetFaceDown)
                list.RemoveAll(t => t == null || t.FaceUp);

            // Throwstone Unit: face-up monster whose DEF is at most this card's ATK (source = except).
            if (c.RequiresTargetDefLeqSourceAtk)
            {
                var srcAtk = except?.CurrentAtk ?? 0;
                list.RemoveAll(t => t == null || !t.FaceUp || t.CurrentDef > srcAtk);
            }

            // Fissure: only the monster(s) with the lowest ATK are eligible.
            if (c.LowestAtkOnly && list.Count > 0)
            {
                var low = list.Min(t => t?.CurrentAtk ?? int.MaxValue);
                list.RemoveAll(t => t == null || t.CurrentAtk != low);
            }

            // Armed Ninja / Reaper: a face-up card of the wrong kind is not a legal target;
            // a Set card is (it is revealed on resolution).
            if (!string.IsNullOrEmpty(c.TargetCardKind))
                list.RemoveAll(t => t?.Def == null ||
                                    (t.FaceUp && !CardIsKind(t, c.TargetCardKind)));

            if (!string.IsNullOrEmpty(c.RaceFilter) &&
                c.Zone != EffectZoneFilter.DeckMonstersRaceLevelLeq &&
                c.Zone != EffectZoneFilter.DeckFieldSpells)
                list.RemoveAll(t => t?.Race == null ||
                                    !ClassicEraTemplates.RaceMatches(t.Race, c.RaceFilter));
            if (!string.IsNullOrEmpty(c.ExceptNamedCard))
            {
                // SimpleTargetUtility batch (v173): except lists print several quoted names; they are
                // stored pipe-separated (Arsenal Summoner's five). A single name splits to itself.
                foreach (var ex in c.ExceptNamedCard.Split('|'))
                {
                    var exName = ex.Trim();
                    if (exName.Length == 0) continue;
                    list.RemoveAll(t => t != null && t.IsNamed(exName));
                }
            }
            if (!string.IsNullOrEmpty(c.TargetSeriesName))
                list.RemoveAll(t => !CardMatchesSeries(t?.Def, c.TargetSeriesName));
            if (!string.IsNullOrEmpty(c.AttributeFilter) &&
                c.Action == EffectActionKind.EquipThisToTarget)
                list.RemoveAll(t => t?.Def == null || t.Attribute == null ||
                                    !t.Attribute.Equals(c.AttributeFilter,
                                        System.StringComparison.OrdinalIgnoreCase));
            if (c.Action == EffectActionKind.EquipThisToTarget)
            {
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp);
                if (!string.IsNullOrEmpty(c.EquipHostName) && c.Zone == EffectZoneFilter.FieldAnyMonster)
                    list.RemoveAll(t => t == null || !t.IsNamed(c.EquipHostName));
                EquipFamilyRuntime.FilterByDeclaredAttribute(c, except, list);
                list.RemoveAll(t => t != null && HostAlreadyHasUnion(t));
                if (!string.IsNullOrEmpty(c.NamedCard) || !string.IsNullOrEmpty(c.AltNamedCard))
                {
                    var primary = c.NamedCard ?? "";
                    var alt = c.AltNamedCard ?? "";
                    list.RemoveAll(t => t == null || (!t.IsNamed(primary) && !t.IsNamed(alt)));
                }
            }
            if (c.Action == EffectActionKind.ModifyTargetUntilEndOfTurn)
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp);
            // "a non Machine-Type monster" (Paralyzing Potion / Germ Infection)
            if (!string.IsNullOrEmpty(c.ExceptRaceFilter))
                list.RemoveAll(t => t?.Def == null ||
                                    ClassicEraTemplates.RaceMatches(t.Race, c.ExceptRaceFilter));
            if (c.Action == EffectActionKind.SetTargetFaceDownDefense)
                list.RemoveAll(t => t == null || !t.FaceUp || t.IsToken || // Tokens cannot be Set
                                    // Light of Intervention (LocksAndZones batch): cannot be flipped face-down.
                                    LocksAndZonesRuntime.CannotTurnFaceDown(engine, t));
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

            if (c.AmountIsAtkMax && c.ControllerTargetCount > 0 &&
                c.Zone == EffectZoneFilter.FieldAnyMonster)
                list.RemoveAll(t => t == null || !t.FaceUp || t.CurrentAtk > c.Amount);

            // TakeControl batch: Brain Control NS/Set filter, Charmer Attribute / zone / battle-destroyed.
            TakeControlRuntime.FilterTargets(engine, who, c, except, list);

            // TributeSummons batch: exact Level / can-be-Normal-Summoned (Tribute Doll), Tributed Level + N
            // (Insect Imitation).
            TributeSummonsRuntime.FilterTargets(who, c, list, costNumeric);

            // SimpleSpellTraps batch: Dragon's Gunfire — a face-up monster with N or less DEF.
            SimpleSpellTrapsRuntime.FilterTargets(c, list);

            // ContinuousLocksAndIgnitions batch (v163): Buster Rancher — a face-up monster with N or less ATK.
            ContinuousLocksAndIgnitionsRuntime.FilterTargets(c, list);

            // SpecialSummonEffects batch (v161): The Puppet Magic of Dark Ruler — a Level your monsters can total exactly.
            SpecialSummonEffectsRuntime.FilterTargets(engine, who, c, except, list);

            // PgdSpellTrapBasics batch (v162): Secret Pass to the Treasures — a face-up monster with N or less ATK.
            PgdSpellTrapBasicsRuntime.FilterTargets(c, list);

            // SummonLocksAndStatRules batch: Amazoness Spellcaster's targets (both zones) are targetable face-up monsters.
            SummonLocksAndStatRulesRuntime.FilterTargets(engine, c, except, list);

            // BattleDestroyedGyRecovery batch (v162): this turn's GY entries by battle / by the opponent; Rope of Life's
            // "those monsters".
            BattleDestroyedGyRecoveryRuntime.FilterTargets(engine, who, c, list);

            // EquipLocksAndLeaveField batch: Metalsilver Armor — monsters the opponent's card effects cannot target.
            // Mirror: SpellTrapEffects.CollectLegalTargets.
            EquipLocksAndLeaveFieldRuntime.FilterTargets(engine, who, c, list);

            // UnionRiders batch (v175): Union Rider — a face-up Union Monster; Lunatite's no-name equip — not itself;
            // Combination Attack — a monster that attacked this turn with a Union in its Union state to Special Summon.
            UnionRidersRuntime.FilterTargets(engine, who, c, except, list);

            // DcrSpellTrapUtility batch: Ray of Hope — the GY targets of a Deck shuffle are monsters of one Attribute.
            DcrSpellTrapUtilityRuntime.FilterTargets(c, list);

            // GuardiansAndEquipEvents batch: Guardian Elma / Chopman — only Equip Spells that can equip to this card, and only
            // while a Spell & Trap Zone is free (Lua s.filter CheckEquipTarget, GetLocationCount(tp,LOCATION_SZONE)>0).
            GuardiansAndEquipEventsRuntime.FilterTargets(engine, who, c, except, list);

            // MonsterNegationAndControlLocks batch: Blindly Loyal Goblin / Mataza — a take-control clause never targets a
            // monster whose control cannot switch. Mirror: SpellTrapEffects.CollectLegalTargets.
            MonsterNegationAndControlLocksRuntime.FilterTargets(engine, c, list);

            // RecoveryAndPhaseCosts batch: Dimension Distortion — a face-up banished monster you can Special Summon (and a
            // free zone); Salvage — the Attribute and ATK ceiling of a GY monster add.
            RecoveryAndPhaseCostsRuntime.FilterTargets(engine, who, c, except, list);

            // GyHandAndSummonEventTriggers batch: a Flip-monster pick (Night Assailant's GY target, Soul Reversal)
            // keeps Flip monsters only, and "except this card" excludes every card of the source's name. The generic
            // face-up kind filter never looks inside a GY. Mirror: SpellTrapEffects.CollectLegalTargets.
            GyHandAndSummonEventTriggersRuntime.FilterTargets(c, except, list);

            // TokensDeclarationsAndPicks batch: Wild Nature's Release — a target with DEF above 0.
            TokensDeclarationsAndPicksRuntime.FilterTargets(c, list);

            // Gust / Driving Snow (SpellTrapDestroyedResponse): "Destroy 1 Spell or Trap Card on the field" never
            // targets the activating card itself (Lua: IsExistingTarget(…, e:GetHandler())).
            // EngineResiduals (v163): a Spell/Trap Card being activated never targets itself with "target 1 card on the
            // field" (Raigeki Break, Lua: IsExistingTarget(aux.TRUE, …, e:GetHandler())). A monster's own effect may
            // (Abyss Soldier: Lua passes nil).
            if (c.Zone == EffectZoneFilter.AnyCardOnField && SpellTrapActivationExcludesItself(c, except))
                list.RemoveAll(t => ReferenceEquals(t, except));

            if (c.Zone == EffectZoneFilter.FieldSpellTraps && c.ExceptThisCard && except != null)
                list.RemoveAll(t => ReferenceEquals(t, except));
            else if (c.Zone == EffectZoneFilter.FieldSpellTraps)
                return SpellTrapEffects.FilterActivatingCardFromOffer(list, except);
            return list;
        }

        /// <summary>
        /// EngineResiduals (v163): <paramref name="source"/> is the Spell/Trap Card whose activation <paramref name="c"/>
        /// belongs to, so its own "card on the field" target excludes it (the card is being activated, not a target).
        /// </summary>
        internal static bool SpellTrapActivationExcludesItself(EffectClause c, CardInstance source) =>
            c != null && c.Timing == EffectTiming.Activate && source?.Def != null &&
            (source.Def.IsSpell || source.Def.IsTrap);

        static bool MatchesRaceOrUnfiltered(CardInstance m, EffectClause c)
        {
            if (m == null) return false;
            if (c == null || string.IsNullOrEmpty(c.RaceFilter)) return true;
            return m.Race != null &&
                   m.Race.Equals(c.RaceFilter, System.StringComparison.OrdinalIgnoreCase);
        }

        static bool CardMatchesTypeWord(CardInstance card, string typeWord)
        {
            if (card?.Def == null || string.IsNullOrEmpty(typeWord)) return false;
            if (typeWord.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                return card.Def.IsSpell || SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell(card);
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

        internal static IEnumerable<CardInstance> CollectAllMatching(DuelEngine engine, DuelistState who,
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
                // SpellCounterSpenders batch: Mega Ton Magical Cannon — every card the opponent controls (LOCATION_ONFIELD:
                // Monster Zones, Spell & Trap Zones, Field Zone). Every other OppAnyCardOnField clause is targeted.
                case EffectZoneFilter.OppAnyCardOnField:
                    foreach (var m in opp.MonstersOnField())
                        if (m != null) yield return m;
                    // SpellTrapsOnField already yields the Field Zone card (ConditionalSpellsAndTraps batch: it was listed
                    // twice, so a Field Spell was destroyed twice).
                    foreach (var st in opp.SpellTrapsOnField())
                        if (st != null) yield return st;
                    break;
                case EffectZoneFilter.OppFaceUpMonsters:
                    // Lightning Vortex mass destroy: face-up opponent monsters only; Sets stay.
                    foreach (var m in opp.MonstersOnField())
                        if (m != null && m.FaceUp && MatchesRaceOrUnfiltered(m, c))
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
                case EffectZoneFilter.EitherGyCards:
                    if (who?.Graveyard != null)
                        foreach (var g in who.Graveyard)
                            if (g != null) yield return g;
                    if (opp?.Graveyard != null)
                        foreach (var g in opp.Graveyard)
                            if (g != null) yield return g;
                    break;
                case EffectZoneFilter.OppGyCards:
                    if (opp?.Graveyard != null)
                        foreach (var g in opp.Graveyard)
                            if (g != null) yield return g;
                    break;
            }
        }

        internal static CardInstance AutoPick(EffectClause c, List<CardInstance> targets, DuelistState who,
            DuelEngine engine)
        {
            var opp = engine.OpponentOf(who);
            if (c.PickLowestAtk)
                return targets.OrderBy(t => t.CurrentAtk).First();
            if (c.PickHighestDef)
                return targets.OrderByDescending(t => t.CurrentDef).First();
            if (c.PickHighestAtk)
                return targets.OrderByDescending(t => t.CurrentAtk).First();
            // Dark Spirit of the Silent (BattleWindows batch): the weakest monster battles instead.
            if (c.Action == EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead)
                return BattleWindowsRuntime.AiPickDarkSpirit(engine, targets);
            // EquipModel batch: Tailor of the Fickle picks the Equip Card and its new monster together.
            if (c.Zone == EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget)
                return EquipModelRuntime.AiPickSwitch(engine, who, targets);
            // DcrEquipSpells batch: an Equip that helps its monster (Shooting Star Bow, Tryce …) goes on the AI's own.
            if (DcrEquipSpellsRuntime.TryAutoPick(engine, who, c, targets, out var dcrEquipPick))
                return dcrEquipPick;
            // EquipAndSpellProtection batch: Heart of Clear Water / Smoke Grenade go on its own monster; Magic Reflector.
            if (EquipAndSpellProtectionRuntime.TryAutoPick(engine, who, c, targets, out var protectionPick))
                return protectionPick;
            // PgdSpellTrapBasics batch (v162): Book of Taiyou's flip, Secret Pass to the Treasures' monster.
            if (PgdSpellTrapBasicsRuntime.TryAutoPick(engine, who, c, targets, out var pgdPick))
                return pgdPick;
            // SummonLocksAndStatRules batch: Amazoness Spellcaster's two targets.
            if (SummonLocksAndStatRulesRuntime.TryAutoPick(c, targets, out var switchPick))
                return switchPick;
            // ChaosBanishSummons batch: Chaos Sorcerer / BLS banish the opponent's strongest monster; Primal Seed.
            if (ChaosBanishSummonsRuntime.TryAutoPick(engine, who, c, targets, out var chaosPick))
                return chaosPick;
            switch (c.Zone)
            {
                case EffectZoneFilter.EitherGyMonsters:
                case EffectZoneFilter.EitherGyCards:
                    return targets
                        .OrderByDescending(t => who.Graveyard.Contains(t) ? 1 : 0)
                        .ThenByDescending(t => t.CurrentAtk)
                        .First();
                case EffectZoneFilter.FieldSpellTraps:
                    return targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _)) ?? targets[0];
                case EffectZoneFilter.OppFaceUpMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.FieldAnyMonster:
                    // BattleRules batch: The Secret of the Bandit goes on the AI's own strongest face-up monster.
                    if (c.Action == EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn)
                        return BattleRulesRuntime.AiPickBanditTarget(who, targets);
                    // SpecialSummonEffects batch (v161): Winged Minion boosts your own strongest monster of the Type.
                    if (c.Action == EffectActionKind.TargetGainsAtkDefWhileFaceUp)
                        return SpecialSummonEffectsRuntime.AiPickBoostTarget(engine, who, targets);
                    // TargetedClassics Soft: Deal of Phantom prefers your strongest face-up.
                    if (TargetedClassicsRuntime.IsPerCountBoost(c))
                        return TargetedClassicsRuntime.AutoPickBoostTarget(engine, who, targets);
                    // EquipFamily batch: Dragonic Attack / Scroll of Bewitchment / Collected Power go on your own
                    // strongest face-up monster.
                    // EquipLocksAndLeaveField batch: Big Bang Shot / Raregold / Metalsilver Armor (AiEquipsOwnMonster).
                    if (EquipFamilyRuntime.AiPrefersOwnMonster(c) || c.AiEquipsOwnMonster)
                        return targets.Where(t => who.TryFindMonster(t, out _))
                                   .OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
                               ?? targets.OrderByDescending(t => t.CurrentAtk).First();
                    // Prefer opponent monsters (Man-Eater Bug AI); fall back to highest ATK overall.
                    // Face-down use printed ATK so set walls still rank for target choice.
                    return targets
                               .Where(t => opp.TryFindMonster(t, out _))
                               .OrderByDescending(t => t.CurrentAtk)
                               .FirstOrDefault()
                           ?? targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.ControllerGySpells:
                case EffectZoneFilter.ControllerGyTraps:
                // SimpleTargetUtility batch (v173): Fairy of the Spring / Arsenal Summoner —
                // the shared filters leave the printed choice (or a single hit) for the AI.
                case EffectZoneFilter.ControllerGyEquipSpells:
                case EffectZoneFilter.ControllerDeckCards:
                    return targets[0];
                case EffectZoneFilter.ControllerGyMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppGyMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppGyCards:
                    return targets
                        .OrderByDescending(t => t.Def != null && t.Def.IsMonster ? t.CurrentAtk : -1)
                        .First();
                // PerCopyDeck batch (v163): Graverobber — the most recent Spell.
                case EffectZoneFilter.OppGySpells:
                    return targets[targets.Count - 1];
                case EffectZoneFilter.OppMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.FieldFaceUpSpellCounterHolders:
                    return SpellCounterSpendersRuntime.AiPickHolder(engine, who, targets);
                case EffectZoneFilter.FieldFaceUpFusionMonsters:
                    // FlipCostsAndFusion batch: De-Fusion takes the opponent's strongest Fusion first.
                    return FlipCostsAndFusionRuntime.AutoPickFusion(engine, who, targets);
                case EffectZoneFilter.FieldFaceUpSpells:
                    // StandbyBurnsAndDice batch (v162): Mask of Dispel takes an opponent's Spell first.
                    return StandbyBurnsAndDiceRuntime.AutoPickSpell(engine, who, targets);
                case EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects:
                    // NegationMonsters batch (v161): Double Snare takes the opponent's card first.
                    return NegationMonstersRuntime.AutoPickDoubleSnare(engine, who, targets);
                case EffectZoneFilter.FieldFaceDownMonsters:
                    // TargetedClassics Soft: Nobleman of Crossout prefers opp face-down (highest ATK).
                    if (c.PurgeDecksIfFlipMonster)
                        return TargetedClassicsRuntime.AutoPickFaceDownToDestroy(engine, who, targets);
                    return FlipDefCheckRuntime.AutoPick(engine, who, c, targets);
                case EffectZoneFilter.ControllerBanishedFaceUpMonsters:
                    return TargetedClassicsRuntime.AutoPickBanished(targets);
                case EffectZoneFilter.ControllerHandMonsters:
                    // Discarding (GitHub lineage: DiscardChosenFromHand) gives up the weakest card;
                    // summoning from hand (local lineage) takes the strongest.
                    return c.Action == EffectActionKind.DiscardChosenFromHand
                        ? targets.OrderBy(t => t.Def?.atk ?? 0).First()
                        : targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.AnyCardOnField:
                    return targets.FirstOrDefault(t => opp.TryFindMonster(t, out _))
                           ?? targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _))
                           ?? targets[0];
                case EffectZoneFilter.AnyGyCards:
                    // Banish the opponent's resources first, strongest monsters first.
                    return targets
                        .OrderByDescending(t => opp.Graveyard.Contains(t) ? 1 : 0)
                        .ThenByDescending(t => t.Def != null && t.Def.IsMonster ? t.CurrentAtk : -1)
                        .First();
                case EffectZoneFilter.OppHandCards:
                    // Take the opponent's strongest-looking card: Spells/Traps, then high-ATK monsters.
                    return targets
                        .OrderByDescending(t => t.Def != null && !t.Def.IsMonster ? 1 : 0)
                        .ThenByDescending(t => t.Def?.atk ?? 0)
                        .First();
                case EffectZoneFilter.DeckRitualMonsters:
                    return targets.OrderByDescending(t => t.Def?.atk ?? 0).First();
                case EffectZoneFilter.ControllerAnyMonsters:
                    // Sacrifice your weakest monsters.
                    return targets.OrderBy(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppAnyMonsters:
                case EffectZoneFilter.OppAttackPositionMonsters:
                    return targets.OrderByDescending(t => t.CurrentAtk).First();
                case EffectZoneFilter.OppDefensePositionMonsters:
                    // Expose the weakest defender to attack.
                    return targets.OrderBy(t => t.CurrentAtk).First();
                case EffectZoneFilter.DeclaredMonsterTypes:
                    return TypeDeclarationRuntime.AiPickMonsterType(engine, who, targets);
                case EffectZoneFilter.DeclaredAttributes:
                    return TokensDeclarationsAndPicksRuntime.AiPickAttribute(engine, who, targets);
                case EffectZoneFilter.AnotherAppropriateTarget:
                case EffectZoneFilter.AnotherAppropriateSpellTrapTarget: // SpellEventsAndHandSendTriggers: Fiend's Hand Mirror
                    // RedirectAndReplace batch (v142): Fairy's Hand Mirror / Shift.
                    return RedirectAndReplaceRuntime.AiPick(engine, who, c, targets) ?? targets[0];
                case EffectZoneFilter.ControllerMonsters when c.ThenMillOpponentDeck > 0:
                    // Assault on GHQ destroys your own monster to mill: give up the weakest.
                    return targets.OrderBy(t => t.CurrentAtk).First();
                default:
                    return targets[0];
            }
        }

        internal static EffectTargetKind MapTargetKind(EffectClause c) => c.Zone switch
        {
            // RedirectAndReplace batch (v142): Shift picks a monster you control; Fairy's Hand Mirror a monster on
            // either field (a monster kind, so the picker tags each candidate YOURS / OPP; the legal list itself
            // comes from CollectTargets).
            EffectZoneFilter.AnotherAppropriateTarget when c.Side == EffectSide.Controller
                => EffectTargetKind.YourMonster,
            EffectZoneFilter.AnotherAppropriateTarget => EffectTargetKind.FaceUpMonsterOnField,
            // SpellEventsAndHandSendTriggers batch: Fiend's Hand Mirror picks a Spell/Trap Card on either field.
            EffectZoneFilter.AnotherAppropriateSpellTrapTarget => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.EitherGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.EitherGyCards => EffectTargetKind.CardInEitherGy,
            EffectZoneFilter.ControllerGyCards => EffectTargetKind.CardInEitherGy,
            EffectZoneFilter.OppGyCards => EffectTargetKind.CardInEitherGy,
            // PerCopyDeck batch (v163): Graverobber.
            EffectZoneFilter.OppGySpells => EffectTargetKind.CardInEitherGy,
            EffectZoneFilter.FieldSpellTraps => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.OppSpellTraps => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.OppFaceUpMonsters when c.Action == EffectActionKind.EffectDamageBothFromOriginalAtk
                => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
            EffectZoneFilter.OppFaceUpMonsters => EffectTargetKind.OppFaceUpMonster,
            EffectZoneFilter.OppDefensePositionMonsters => EffectTargetKind.OppFaceUpMonster,
            EffectZoneFilter.FieldAnyMonster when c.Action == EffectActionKind.EquipThisToTarget
                => EffectTargetKind.EquipAnyMonster,
            EffectZoneFilter.FieldAnyMonster when c.Action == EffectActionKind.ModifyTargetUntilEndOfTurn
                => EffectTargetKind.FaceUpMonsterOnField,
            // SpecialSummonEffects batch (v161): Winged Minion's face-up monster on either field.
            EffectZoneFilter.FieldAnyMonster when c.Action == EffectActionKind.TargetGainsAtkDefWhileFaceUp
                => EffectTargetKind.FaceUpMonsterOnField,
            EffectZoneFilter.OppHandCards => EffectTargetKind.CardInOppHand,
            EffectZoneFilter.OppFaceDownMonsters => EffectTargetKind.OppMonster,
            // TargetedClassics Soft: face-down-only picker (Nobleman / Acid Trap Hole), not AnyMonsterOnField.
            EffectZoneFilter.FieldFaceDownMonsters => EffectTargetKind.FaceDownMonsterOnField,
            // FlipCostsAndFusion batch: De-Fusion's face-up Fusion Monster on either field (field-glance picker).
            EffectZoneFilter.FieldFaceUpFusionMonsters => EffectTargetKind.FaceUpMonsterOnField,
            // NegationMonsters batch (v161): Double Snare's face-up card on either field (monster or Spell/Trap).
            EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects => EffectTargetKind.AnyCardOnField,
            // EquipModel batch: Tailor of the Fickle picks an Equip Card first (then a monster, EquipModelRuntime).
            EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget => EffectTargetKind.SpellTrapOnField,
            // EquipAndSpellProtection batch: Magic Reflector's face-up Spell you control.
            EffectZoneFilter.ControllerFaceUpSpellsExceptThis => EffectTargetKind.SpellTrapOnField,
            // StandbyBurnsAndDice batch (v162): Mask of Dispel's face-up Spell on either field.
            EffectZoneFilter.FieldFaceUpSpells => EffectTargetKind.SpellTrapOnField,
            // TargetedClassics Soft: Miracle Dig banished picker.
            EffectZoneFilter.ControllerBanishedFaceUpMonsters => EffectTargetKind.MonsterInYourBanished,
            // ChaosBanishSummons batch: Primal Seed's banished picker.
            EffectZoneFilter.ControllerBanishedCards => EffectTargetKind.CardInYourBanished,
            EffectZoneFilter.FaceDownSpellTraps => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.HandOrDeckNamed => EffectTargetKind.MonsterInHandOrDeck,
            EffectZoneFilter.FieldAnyMonster when c.Action == EffectActionKind.SetTargetFaceDownDefense
                => EffectTargetKind.FaceUpMonsterOnField,
            // TokensDeclarationsAndPicks batch: Wild Nature's Release's face-up Beast / Beast-Warrior on either field.
            EffectZoneFilter.FieldAnyMonster when c.Action == EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase
                => EffectTargetKind.FaceUpMonsterOnField,
            EffectZoneFilter.FieldAnyMonster => EffectTargetKind.AnyMonsterOnField,
            EffectZoneFilter.ControllerGySpells => EffectTargetKind.SpellInYourGy,
            EffectZoneFilter.ControllerGyTraps => EffectTargetKind.TrapInYourGy,
            // SimpleTargetUtility batch (v173): Fairy of the Spring reuses the GY-Spell prompt;
            // Arsenal Summoner reuses the printed-Deck search prompt (Terraforming's kind).
            EffectZoneFilter.ControllerGyEquipSpells => EffectTargetKind.SpellInYourGy,
            EffectZoneFilter.ControllerDeckCards => EffectTargetKind.CardInYourDeck,
            EffectZoneFilter.FieldEquipCards => EffectTargetKind.SpellTrapOnField,
            EffectZoneFilter.AnyCardOnField => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.ControllerHandMonsters when c.Action == EffectActionKind.SpecialSummonFromHand
                => EffectTargetKind.MonsterInYourHand,
            EffectZoneFilter.ControllerDeckMonsters => EffectTargetKind.MonsterInYourDeckToSummon,
            EffectZoneFilter.ControllerDeckSpells => EffectTargetKind.SpellInYourDeck,
            EffectZoneFilter.ControllerHandMonsters when c.Action == EffectActionKind.DiscardChosenFromHand
                => EffectTargetKind.DiscardFromHand,
            EffectZoneFilter.ControllerHandMonsters => EffectTargetKind.DiscardMonsterInHand,
            EffectZoneFilter.ControllerGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.OppGyMonsters => EffectTargetKind.MonsterInEitherGy,
            EffectZoneFilter.OppMonsters => EffectTargetKind.OppMonster,
            EffectZoneFilter.ControllerMonsters when c.Action == EffectActionKind.EquipThisToTarget
                => EffectTargetKind.EquipMonsterYouControl,
            // Two-Pronged Attack: destroying your own monsters is a pick, not a Tribute cost.
            EffectZoneFilter.ControllerMonsters when c.Action == EffectActionKind.Destroy
                => EffectTargetKind.YourMonster,
            EffectZoneFilter.ControllerMonsters => EffectTargetKind.TributeMonsterYouControl,
            EffectZoneFilter.OppAnyCardOnField => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.FieldFaceUpSpellCounterHolders => EffectTargetKind.AnyCardOnField,
            EffectZoneFilter.DeckFieldSpells => EffectTargetKind.FieldSpellInYourDeck,
            EffectZoneFilter.DeckRitualMonsters or
                EffectZoneFilter.DeckRitualSpells or
                EffectZoneFilter.DeckRitualMonstersOrSpells => EffectTargetKind.RitualCardInYourDeck,
            EffectZoneFilter.DeckEquipSpells => EffectTargetKind.EquipSpellInYourDeck,
            EffectZoneFilter.DeckMonstersRaceLevelLeq => EffectTargetKind.MonsterInYourDeckFiltered,
            EffectZoneFilter.DeckMonstersAtkLeq => EffectTargetKind.MonsterInYourDeckAtkLeq,
            EffectZoneFilter.AnyGyCards => EffectTargetKind.CardInEitherGy,
            EffectZoneFilter.ControllerAnyMonsters => EffectTargetKind.YourMonster,
            // SummonLocksAndStatRules batch: Amazoness Spellcaster's own face-up archetype monster.
            EffectZoneFilter.ControllerFaceUpSeriesMonsters => EffectTargetKind.YourMonster,
            EffectZoneFilter.OppAnyMonsters => EffectTargetKind.OppMonster,
            EffectZoneFilter.OppAttackPositionMonsters => EffectTargetKind.OppMonster,
            EffectZoneFilter.DeclaredMonsterTypes => EffectTargetKind.DeclareMonsterType,
            // TokensDeclarationsAndPicks batch: DNA Transplant declares 1 Attribute (declaration tokens).
            EffectZoneFilter.DeclaredAttributes => EffectTargetKind.DeclareAttribute,
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
                who != null && who.IsPlayer, zoneIndex, staysOnField: staysOnField, flatOnBoard: false,
                isTrap: card.Def != null && card.Def.IsTrap);
            // Presentation only: removals queued while this effect applies (same frame) are its results.
            Presentation.ArInteraction.ActivationBeat.SetSource(card.InstanceId);
            DuelPresentationPacer.HoldSpellActivate(card.Name, opponentCard: who != null && !who.IsPlayer,
                instanceId: card.InstanceId);
        }

        static void FinishSpellTrap(DuelEngine engine, DuelistState who, CardInstance card, bool stays)
        {
            if (CompiledActivationChainRuntime.IsRecording(card))
            {
                DeferFinishSpellTrap(engine, who, card, stays); // when its Chain Link resolves
                return;
            }
            // PerCopyDeck batch (v163): Pharaoh's Treasure shuffled itself into the Deck instead of the GY.
            if (PerCopyDeckRuntime.ConsumeWentToDeckInstead(card)) return;
            // Imperial Order negated this Equip Spell's equip: it equips nothing, so it is sent to the GY.
            if (stays && ContinuousNegationRuntime.ConsumeNegatedEquip(engine, card))
                stays = false;
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
                    who != null && who.IsPlayer, stayZi, staysOnField: true, flatOnBoard: false,
                    isTrap: card.Def != null && card.Def.IsTrap);
                Presentation.ArInteraction.SpellActivationPresentation.MarkResolved(card.InstanceId);
            }
            else
            {
                // Cost / pending paths (discard, tribute, second target, Hats…) can reach here without
                // PlaceFaceUp: register now so the ghost has art, a side and a beat (was a blank stub).
                if (card != null &&
                    !Presentation.ArInteraction.SpellActivationPresentation.HasActivation(card.InstanceId))
                {
                    var zi = -2;
                    if (who != null && who.TryFindSpellTrap(card, out var onZi)) zi = onZi;
                    Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                        card.InstanceId, card.CardId, card.Name,
                        who != null && who.IsPlayer, zi, staysOnField: false, flatOnBoard: false,
                        isTrap: card.Def != null && card.Def.IsTrap);
                }

                Presentation.ArInteraction.SpellActivationPresentation.QueueFadeToGy(card.InstanceId);
                Presentation.ArInteraction.SpellActivationPresentation.EnqueueGhostIfNeeded(card.InstanceId);
                engine.SendCardToGrave(who, card);
            }

            if (card?.Def != null && card.Def.IsSpell)
                NotifySpellResolved(engine, who, card);
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

        /// <summary>
        /// Activation name gate. <see cref="EffectClause.RequiresControllerNamedCard"/>
        /// narrows "is on the field" to "you control" — Thousand Knives reads
        /// "If you control \"Dark Magician\"", so the opponent's copy must not unlock it.
        /// </summary>
        static bool NameGateSatisfied(DuelEngine engine, DuelistState who, EffectClause c)
        {
            if (c == null || string.IsNullOrEmpty(c.RequiresFaceUpName)) return true;
            return c.RequiresControllerNamedCard
                ? FieldSpellEffects.ControlsFaceUpNamed(who, c.RequiresFaceUpName)
                : FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName);
        }

        static string NameGateReason(EffectClause c) =>
            c != null && c.RequiresControllerNamedCard
                ? $"Requires \"{c.RequiresFaceUpName}\" you control."
                : $"Requires \"{c?.RequiresFaceUpName}\" on the field.";

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

        /// <summary>
        /// LP gain / effect damage measured off the clause's subject monster — the
        /// battle-destroyed monster for <see cref="EffectTiming.ThisCardDestroysByBattle"/>.
        /// Falls through to <paramref name="fallback"/> when the clause is a flat amount
        /// or the subject is missing, so an unscaled clause is never silently zeroed.
        /// </summary>
        static int AmountFromTarget(EffectClause clause, CardInstance target, int fallback)
        {
            if (clause == null) return fallback;
            if (clause.AmountFromTargetOriginalAtk)
                return target == null ? 0 : PrintedOriginalAtk(target);
            if (clause.ScaleAmountByTargetLevel)
                return target == null
                    ? 0
                    : Mathf.Max(0, clause.Amount) * Mathf.Max(0, target.Level);
            return fallback;
        }

        /// <summary>Cards the last Destroy clause removed (Dragon Piper's "If you destroy any").</summary>
        [ThreadStatic] static int s_lastDestroyHits;

        /// <summary>
        /// Change a monster to face-up Attack/Defense Position by a card effect. A Set
        /// monster flipped face-up this way activates its FLIP effect.
        /// </summary>
        internal static void SetFaceUpPosition(DuelEngine engine, CardInstance m, BattlePosition pos,
            bool suppressFlip = false)
        {
            var controller = engine.ControllerOf(m);
            if (controller == null) return;
            var wasFaceDown = !m.FaceUp;
            var before = m.Position;
            m.FaceUp = true;
            m.Position = pos;
            engine.Log($"{m.Name} → face-up {pos} Position" +
                       (wasFaceDown && suppressFlip ? " (FLIP effect not activated)." : "."));
            if (wasFaceDown && !suppressFlip)
                MonsterEffects.OnFlipSummoned(engine, controller, m);
            if (before != pos)
                NotifyPositionChanged(engine, m, toDefense: pos == BattlePosition.Defense);
        }

        /// <summary>
        /// Exodia family: a player with every named piece in hand (plus the card that
        /// names them) wins. Both at once is a draw. Returns true if the duel ended.
        /// </summary>
        /// <summary>
        /// Exodia family: a player wins if a card in hand plus its printed named pieces are all in
        /// hand. Both lineages' clauses count (local <see cref="EffectActionKind.ExodiaWinStyle"/>,
        /// GitHub <see cref="EffectActionKind.WinDuelWithNamedSetInHand"/>); both players at once is a Draw.
        /// </summary>
        public static bool CheckHandWinConditions(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return false;
            var p = HasHandWin(engine.Player);
            var o = HasHandWin(engine.Opponent);
            if (!p && !o) return false;
            engine.EndGameByCardEffect(p && o ? null : p ? engine.Player : engine.Opponent,
                p && o
                    ? "Both players assembled the Forbidden One — the Duel is a draw."
                    : "all five pieces of the Forbidden One are in hand");
            return true;
        }

        static bool HasHandWin(DuelistState who)
        {
            if (who?.Hand == null || who.Hand.Count < 2) return false;
            foreach (var card in who.Hand)
            {
                if (card?.Def == null || !card.Def.IsMonster) continue;
                if (OfficialCardAuthority_NoEffect(card)) continue;
                var prog = CompiledEffectCache.GetOrCompile(card.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClauseList)
                {
                    if (c == null) continue;
                    if (c.Action == EffectActionKind.ExodiaWinStyle)
                    {
                        if (HandHasNamedWinSet(who, card, c)) return true;
                        continue;
                    }
                    if (c.Action != EffectActionKind.WinDuelWithNamedSetInHand) continue;
                    var names = (c.NamedCard ?? "").Split('|');
                    var all = names.Length > 0;
                    foreach (var n in names)
                    {
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        if (!who.Hand.Exists(h => h != null && h != card && h.IsNamed(n.Trim())))
                        {
                            all = false;
                            break;
                        }
                    }

                    if (all) return true;
                }
            }

            return false;
        }

        static bool OfficialCardAuthority_NoEffect(CardInstance c) =>
            Rules.OfficialCardAuthority.HasNoActivatableEffect(c.Def);

        /// <param name="byPlayer">The controller of the destroying effect (defaults to the controller of
        /// <paramref name="source"/>); a Spell/Trap destroyed by the other player's effect is recorded for the Gust /
        /// Driving Snow window (<see cref="SpellTrapDestroyedResponseRuntime.NoteDestroyedByEffect"/>).</param>
        internal static void DestroyCard(DuelEngine engine, CardInstance card, CardInstance source = null,
            bool banishIfDestroyed = false, DuelistState byPlayer = null)
        {
            if (card == null) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, card, source))
            {
                engine.Log($"{card.Name} is unaffected by {source?.Name ?? "this effect"}.");
                return;
            }

            // Soft residual Fake Trap: destroy-replacement redirect (Prefer NO DE rewrite).
            // Soft ≠ hard. Fail-closed: only Soft-registered Trap Card(s) you control vs destroy.
            if (ContinuousProtections.TrySoftFakeTrapRedirectDestroy(engine, card, source))
                return;

            var owner = engine.ControllerOf(card);
            if (owner == null)
            {
                engine.Log($"Destroy: {card.Name} not found on field.");
                return;
            }

            var destroyer = byPlayer ?? (source != null ? engine.ControllerOf(source) : null);
            var destroyedMonster = owner.TryFindMonster(card, out _);
            // BattleDestroyedTriggers batch: Different Dimension Dragon — not destroyed by a Spell/Trap effect that does not
            // target it (nothing is recorded as destroyed).
            if (destroyedMonster &&
                BattleDestroyedTriggersRuntime.PreventsUntargetedSpellTrapDestruction(engine, card, source))
                return;
            // EquipAndSpellProtection batch: Fusion Sword Murasame Blade cannot be destroyed by card effects; a Magic
            // Reflector counter is removed instead. An equipped Spell/Trap destroyed here is recorded for its
            // "destroyed by a card effect while equipped" trigger (Smoke Grenade of the Thief, Blast with Chain).
            if (!destroyedMonster)
            {
                if (EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction(engine, card, source)) return;
                EquipAndSpellProtectionRuntime.NoteEffectDestroy(engine, card);
            }
            var wasFaceUp = card.FaceUp;
            if (destroyedMonster)
                engine.DestroyMonsterPublic(owner, card, source, banishIfDestroyed);
            else if (banishIfDestroyed)
                engine.BanishCard(owner, card, destroyed: true);
            else
            {
                // CallsDiceAndSetTriggers batch (v163): destroyed while Set (Statue of the Wicked, Dark Coffin).
                CallsDiceAndSetTriggersRuntime.NoteSpellTrapDestroying(card, wasFaceUp);
                engine.SendCardToGrave(owner, card, sentBy: source);
            }
            SpellTrapDestroyedResponseRuntime.NoteDestroyedByEffect(engine, owner, card, destroyer);
            // BattleDestroyedGyRecovery (v162): destroyed by a card effect of the resolving player (GY-entry stamp).
            GyEntryStamps.AddReason(engine, card, GyEntryReason.Destroyed | GyEntryReason.Effect, destroyer);
            // EquipFamily batch (Dark Necrofear): the resolving player's card destroyed it.
            if (destroyedMonster)
                EquipFamilyRuntime.NoteDestroyer(engine, card, destroyer);
            // "If "X" on the field is destroyed, destroy this card." (Toons batch): a destroyed Spell/Trap. A
            // destroyed monster already notified from DuelEngine.DestroyMonster.
            if (!destroyedMonster)
                ToonsRuntime.NotifyFaceUpCardDestroyed(engine, card, wasFaceUp);
        }

        static void DiscardAll(DuelistState who)
        {
            var copy = who.Hand.ToList();
            who.Hand.Clear();
            DiscardEvents.Begin(who.Engine);
            foreach (var c in copy)
            {
                RedirectAndReplaceRuntime.PutInGraveyard(null, who, c); // Banisher of the Light (v142)
                DiscardEvents.Record(who.Engine, who, c, DiscardCause.Effect);
            }
            DiscardEvents.End(who.Engine);
        }

        static bool LordOfDOnField(DuelEngine engine) =>
            engine.Player.MonstersOnField().Any(m => m.FaceUp && m.CardId == MonsterEffects.LordOfD) ||
            engine.Opponent.MonstersOnField().Any(m => m.FaceUp && m.CardId == MonsterEffects.LordOfD);

        /// <summary>
        /// Perfect-O gate for RequiresAllOpponentMonstersDefense continuous direct attack:
        /// every monster the opponent controls is Defense Position (face-down ⇒ DEF).
        /// Vacuous true when opponent controls no monsters (rulebook already allows direct).
        /// Does not inspect ATK values (Drillago stays on a different atom).
        /// </summary>
        public static bool AllOpponentMonstersAreDefensePosition(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null) return false;
            var who = engine.ControllerOf(source);
            if (who == null) return false;
            var opp = engine.OpponentOf(who);
            if (opp == null) return false;
            foreach (var m in opp.MonstersOnField())
            {
                if (m == null) continue;
                // Face-down monsters are Defense Position; face-up must be Defense.
                if (m.FaceUp && m.Position != BattlePosition.Defense)
                    return false;
            }
            return true;
        }


    }
}
