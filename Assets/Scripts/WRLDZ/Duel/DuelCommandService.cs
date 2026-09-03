using System;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Single legal gateway for anime TCG moves — tap UI and verbal announce both use this.
    /// Illegal moves never mutate the board; they return a clear anime-style refusal line.
    ///
    /// Discipline (from industry simulators / Yugioh Shorts client-server pattern):
    /// · One action at a time (<see cref="DuelEngine.IsProcessingAction"/>)
    /// · try/catch so exceptions become Fail, not a frozen duel
    /// · finally always runs soft-lock recovery so we return to an open/recoverable state
    /// </summary>
    public static class DuelCommandService
    {
        public static DuelCommandResult Execute(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (Ocg.OcgLabDuelHost.IsActive && Ocg.OcgLabDuelHost.Current != null)
                return Ocg.OcgLabDuelHost.Current.TryExecute(who, intent);

            if (engine == null || who == null || intent == null || intent.Kind == DuelIntentKind.None)
                return DuelCommandResult.Fail(intent, "No move declared.");

            if (engine.GameOver)
                return DuelCommandResult.Fail(intent, "The duel is over.");

            // Action lock — reject double-tap / re-entrant executes
            if (engine.IsProcessingAction)
            {
                engine.Log("[RULE] Action already processing — ignore re-entrant command.");
                return DuelCommandResult.Fail(intent, "Wait — previous action still resolving.");
            }

            // Resolve card by name hint if instance missing (voice path)
            if (intent.Card == null && !string.IsNullOrEmpty(intent.CardNameHint))
            {
                intent.Card = VerbalMoveParser.FindCard(engine, who, intent.CardNameHint,
                    preferHand: true, preferFieldSt: true, preferFieldMon: true);
            }

            if (intent.Target == null && !string.IsNullOrEmpty(intent.TargetNameHint))
                intent.Target = VerbalMoveParser.FindOpponentMonster(engine, who, intent.TargetNameHint);

            var src = string.IsNullOrEmpty(intent.Source) ? "ui" : intent.Source;
            if ((src is "voice" or "type") && !string.IsNullOrEmpty(intent.RawText))
                engine.Log($"🎙 [{who.Name}] \"{intent.RawText.Trim()}\"");

            engine.IsProcessingAction = true;
            DuelCommandResult result = DuelCommandResult.Fail(intent, "No result.");
            try
            {
                result = Dispatch(engine, who, intent);
            }
            catch (Exception ex)
            {
                // Never leave the duel frozen on an unexpected exception
                var msg = "Action failed: " + (ex.GetBaseException().Message ?? ex.Message);
                engine.Log("[RULE] " + msg);
                result = DuelCommandResult.Fail(intent, msg);
            }
            finally
            {
                // Always leave a recoverable open state (even if Dispatch threw)
                try
                {
                    engine.RecoverStuckCombat();
                    engine.TryCompleteDeferredBattleDestruction();
                }
                catch (Exception recoverEx)
                {
                    engine.Log("[RULE] Recovery finally failed: " + recoverEx.Message);
                }

                engine.IsProcessingAction = false;
            }

            return result;
        }

        static DuelCommandResult Dispatch(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            return intent.Kind switch
            {
                DuelIntentKind.PassResponse => DoPass(engine, who, intent),
                DuelIntentKind.CancelTarget => DoCancelTarget(engine, who, intent),
                DuelIntentKind.ClearTributes => ClearTributes(engine, intent),
                DuelIntentKind.EndTurn => DoEndTurn(engine, who, intent),
                DuelIntentKind.EnterBattlePhase => DoBattle(engine, who, intent),
                DuelIntentKind.EnterMainPhase2 => DoMain2(engine, who, intent),
                DuelIntentKind.NormalSummonAtk => DoSummon(engine, who, intent, asSet: false),
                DuelIntentKind.SetMonsterDef => DoSummon(engine, who, intent, asSet: true),
                DuelIntentKind.SetSpellTrap => DoSetSt(engine, who, intent),
                DuelIntentKind.Activate => DoActivate(engine, who, intent),
                DuelIntentKind.FlipSummon => DoFlip(engine, who, intent),
                DuelIntentKind.ChangePosition => DoPos(engine, who, intent),
                DuelIntentKind.Attack => DoAttack(engine, who, intent, forceDirect: false),
                DuelIntentKind.DirectAttack => DoAttack(engine, who, intent, forceDirect: true),
                _ => DuelCommandResult.Fail(intent, "Unknown declaration.")
            };
        }

        static DuelCommandResult ClearTributes(DuelEngine engine, DuelIntent intent)
        {
            engine.ClearTributes();
            return DuelCommandResult.Success(intent, "Tributes cleared.");
        }

        /// <summary>Parse verbal/typed line and execute if recognized.</summary>
        public static DuelCommandResult Announce(DuelEngine engine, DuelistState who, string utterance,
            string source = "voice")
        {
            var intent = VerbalMoveParser.Parse(utterance, engine, who);
            if (intent == null)
            {
                engine?.Log($"🎙 Could not understand: \"{utterance}\"");
                return DuelCommandResult.Fail(null,
                    "Could not understand that declaration. Try: \"Summon …\", \"Activate …\", \"Attack …\", \"End turn\", \"Pass\".");
            }

            intent.Source = source;
            intent.RawText = utterance;
            return Execute(engine, who, intent);
        }

        static DuelCommandResult DoPass(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (!engine.IsAwaitingResponse)
                return DuelCommandResult.Fail(intent, "No response window is open.");
            if (engine.PendingResponse.Responder != who)
                return DuelCommandResult.Fail(intent, "It is not your response.");
            engine.PassResponse();
            return DuelCommandResult.Success(intent, $"{who.Name} passes.");
        }

        static DuelCommandResult DoCancelTarget(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (!engine.IsAwaitingEffectTarget)
                return DuelCommandResult.Fail(intent, "Nothing to cancel.");
            engine.CancelEffectTargeting();
            return DuelCommandResult.Success(intent, "Target cancelled.");
        }

        static DuelCommandResult DoEndTurn(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            // Recover stuck attack/empty windows, then end
            if (!engine.TryEndTurnSafe(who))
            {
                if (engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget || engine.HasDeclaredAttack)
                    return DuelCommandResult.Fail(intent, "Finish the response or target first.");
                return DuelCommandResult.Fail(intent, "Cannot end turn right now.");
            }

            return DuelCommandResult.Success(intent, $"{who.Name} ends their turn.");
        }

        static DuelCommandResult DoBattle(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (!engine.CanConductBattlePhase)
                return DuelCommandResult.Fail(intent, "Cannot enter Battle Phase now (first turn or wrong phase).");
            if (!engine.TryEnterBattlePhase(who))
                return DuelCommandResult.Fail(intent, "Battle Phase denied.");
            return DuelCommandResult.Success(intent, "Battle Phase!");
        }

        static DuelCommandResult DoMain2(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (!engine.TryEnterMainPhase2(who))
                return DuelCommandResult.Fail(intent, "Cannot enter Main Phase 2 now.");
            return DuelCommandResult.Success(intent, "Main Phase 2.");
        }

        static DuelCommandResult DoSummon(DuelEngine engine, DuelistState who, DuelIntent intent, bool asSet)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (engine.IsAwaitingResponse)
                return DuelCommandResult.Fail(intent, "Respond or pass first.");
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent,
                    string.IsNullOrEmpty(intent.CardNameHint)
                        ? "Name a monster to summon."
                        : $"I don't see \"{intent.CardNameHint}\" in your hand.");
            if (!engine.CanNormalSummonOrSet(who, intent.Card))
                return DuelCommandResult.Fail(intent,
                    $"Cannot {(asSet ? "Set" : "Summon")} {intent.Card.Name} right now (summon used, tributes, or zone).");
            if (!engine.TryNormalSummonToZone(who, intent.Card, asSet, intent.ZoneIndex))
                return DuelCommandResult.Fail(intent, $"Summon failed for {intent.Card.Name}.");
            return DuelCommandResult.Success(intent,
                asSet ? $"Set {intent.Card.Name}!" : $"Normal Summon {intent.Card.Name}!");
        }

        static DuelCommandResult DoSetSt(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent, "Name a Spell/Trap to Set.");
            if (!engine.CanSetSpellTrap(who, intent.Card))
                return DuelCommandResult.Fail(intent, $"Cannot Set {intent.Card.Name} now.");
            if (!engine.TrySetSpellTrapToZone(who, intent.Card, intent.ZoneIndex))
                return DuelCommandResult.Fail(intent, "Set failed.");
            return DuelCommandResult.Success(intent, $"Set Spell/Trap: {intent.Card.Name}.");
        }

        static DuelCommandResult DoActivate(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent,
                    string.IsNullOrEmpty(intent.CardNameHint)
                        ? "Name a card to activate."
                        : $"No legal \"{intent.CardNameHint}\" to activate.");

            var fromHand = who.Hand.Contains(intent.Card);
            var onMonster = who.TryFindMonster(intent.Card, out _);
            if (onMonster)
                fromHand = false;
            else if (!fromHand && !who.TryFindSpellTrap(intent.Card, out _))
            {
                // Try field if voice found hand incorrectly
                fromHand = false;
            }

            // Response window: LegalCards is authority (field traps OR hand QEs like Kuriboh)
            if (engine.IsAwaitingResponse && engine.PendingResponse?.Responder == who)
            {
                var pr = engine.PendingResponse;
                var legal = pr.LegalCards;
                var legalMatch = legal?.Find(c =>
                    c != null && (c == intent.Card || c.InstanceId == intent.Card.InstanceId));
                if (legalMatch == null)
                {
                    return DuelCommandResult.Fail(intent,
                        $"{intent.Card.Name} is not legal in this response window.");
                }

                // Prefer the engine's live card instance (hand/field), not a UI/intent clone
                intent.Card = legalMatch;
                fromHand = who.Hand.Contains(legalMatch) ||
                           who.Hand.Exists(c => c != null && c.InstanceId == legalMatch.InstanceId);
                if (fromHand)
                {
                    // Bind to the actual Hand list entry for discard costs
                    for (var i = 0; i < who.Hand.Count; i++)
                    {
                        if (who.Hand[i] != null && who.Hand[i].InstanceId == legalMatch.InstanceId)
                        {
                            intent.Card = who.Hand[i];
                            break;
                        }
                    }
                }
                // Field response must still be a Set S/T (or a legal hand QE already bound)
                else if (!who.TryFindSpellTrap(intent.Card, out _) &&
                         !who.TryFindMonster(intent.Card, out _))
                    return DuelCommandResult.Fail(intent,
                        $"{intent.Card.Name} is not a Set card you can respond with.");
            }

            if (!engine.CanActivateSpellTrap(who, intent.Card, fromHand))
            {
                // Retry other zone
                if (fromHand && who.TryFindSpellTrap(intent.Card, out _))
                    fromHand = false;
                else if (!fromHand && who.Hand.Contains(intent.Card))
                    fromHand = true;

                if (!engine.CanActivateSpellTrap(who, intent.Card, fromHand))
                {
                    OfficialEffectRegistry.CanActivateOfficial(engine, who, intent.Card, fromHand,
                        out var why);
                    return DuelCommandResult.Fail(intent,
                        string.IsNullOrEmpty(why)
                            ? $"Cannot activate {intent.Card.Name} right now (timing, Set-this-turn, or not legal)."
                            : $"Cannot activate {intent.Card.Name}: {why}");
                }
            }

            if (!engine.TryActivateSpellTrap(who, intent.Card, fromHand))
                return DuelCommandResult.Fail(intent, $"Activation of {intent.Card.Name} failed.");
            return DuelCommandResult.Success(intent, $"Activate {intent.Card.Name}!");
        }

        static DuelCommandResult DoFlip(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent, "Name a face-down monster to Flip Summon.");
            if (!engine.CanFlipSummon(who, intent.Card))
                return DuelCommandResult.Fail(intent, $"Cannot Flip Summon {intent.Card.Name}.");
            if (!engine.TryFlipSummon(who, intent.Card))
                return DuelCommandResult.Fail(intent, "Flip Summon failed.");
            return DuelCommandResult.Success(intent, $"Flip Summon {intent.Card.Name}!");
        }

        static DuelCommandResult DoPos(DuelEngine engine, DuelistState who, DuelIntent intent)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent, "Name a monster to change position.");
            if (!engine.CanChangePosition(who, intent.Card))
                return DuelCommandResult.Fail(intent, $"Cannot change position of {intent.Card.Name}.");
            if (!engine.TryChangePosition(who, intent.Card))
                return DuelCommandResult.Fail(intent, "Position change failed.");
            return DuelCommandResult.Success(intent, $"{intent.Card.Name} → {intent.Card.Position}.");
        }

        static DuelCommandResult DoAttack(DuelEngine engine, DuelistState who, DuelIntent intent,
            bool forceDirect)
        {
            if (engine.TurnPlayer != who)
                return DuelCommandResult.Fail(intent, "Not your turn.");
            if (engine.Phase != DuelPhase.Battle)
                return DuelCommandResult.Fail(intent, "Not Battle Phase.");
            if (intent.Card == null)
                return DuelCommandResult.Fail(intent, "Name an attacker.");
            if (!engine.CanAttack(who, intent.Card))
                return DuelCommandResult.Fail(intent, $"{intent.Card.Name} cannot attack now.");

            CardInstance target = forceDirect ? null : intent.Target;
            if (forceDirect && engine.OpponentOf(who).MonsterCount > 0 &&
                !engine.CanAttackDirectly(who, intent.Card))
                return DuelCommandResult.Fail(intent,
                    "Cannot attack directly — opponent controls a monster.");
            if (!forceDirect && target == null && engine.OpponentOf(who).MonsterCount > 0)
                return DuelCommandResult.Fail(intent, "Choose an attack target (or Direct if empty field).");

            if (!engine.TryAttack(who, intent.Card, target))
                return DuelCommandResult.Fail(intent, "Attack declaration failed.");
            return DuelCommandResult.Success(intent,
                target == null
                    ? $"{intent.Card.Name} attacks directly!"
                    : $"{intent.Card.Name} attacks {target.Name}!");
        }
    }
}
