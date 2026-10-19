using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// MfcLabCards3 batch (v168) runtime helpers.
    /// </summary>
    public static class MfcLabCards3Runtime
    {
        /// <summary>
        /// <see cref="EffectActionKind.EquipThisToAttackingMonster"/>: Blast Sphere automatically equips itself
        /// to the monster that attacked it (face-down Defense, before damage calculation).
        /// </summary>
        public static void EquipThisToAttackingMonster(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            if (!source.FaceUp || source.Position != BattlePosition.Defense)
            {
                engine.Log($"{source.Name}: not in face-down Defense — cannot equip.");
                return;
            }

            if (!engine.TryGetPendingDamageCalculation(out var attacker, out _) || attacker == null)
            {
                engine.Log($"{source.Name}: no attacking monster found.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, attacker, source))
            {
                engine.Log($"{source.Name}: {attacker.Name} is protected.");
                return;
            }

            TextEffectRuntime.EquipCardTo(engine, who, source, attacker, 0, clause);
            engine.Log($"{source.Name} equipped to {attacker.Name}.");
        }

        /// <summary>
        /// <see cref="EffectActionKind.BlastSphereEquipThenStandbyDestroyInflict"/>: during opponent's next Standby Phase,
        /// destroy the monster this card is equipped to, and if you do, inflict damage equal to its ATK.
        /// </summary>
        public static void BlastSphereEquipThenStandbyDestroyInflict(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            if (source.EquippedTo == null)
            {
                engine.Log($"{source.Name}: not equipped to anything.");
                return;
            }

            var target = source.EquippedTo;
            var atk = target.CurrentAtk;
            TextEffectRuntime.DestroyCard(engine, target, source, false, engine.ControllerOf(target) ?? who);
            engine.Log($"{source.Name}: destroyed {target.Name}, inflicting {atk} damage.");
            engine.ApplyEffectDamage(engine.OpponentOf(who), atk, source.Name);
        }

        /// <summary>
        /// <see cref="EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck"/>: declare 1 card type (Monster/Spell/Trap);
        /// the opponent sends 1 card of that type from their Deck to the GY. BattleAndPositionRecords batch: the declaration was
        /// never asked here (it read a Monster Type declaration that no clause records, so nothing was ever sent); the
        /// declaration and the opponent's pick are now <see cref="BattleAndPositionRecordsRuntime.DeclareCardTypeThenOpponentSendsFromDeck"/>
        /// (Vampire Lord, Vampire Lady).
        /// </summary>
        public static void MillCardOfDeclaredTypeFromOpponentDeck(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause) =>
            BattleAndPositionRecordsRuntime.DeclareCardTypeThenOpponentSendsFromDeck(engine, who, source, clause);

        /// <summary>
        /// OncePerTurnSoft group (v211): <paramref name="card"/>'s trip into <paramref name="who"/>'s GY was a destruction by a
        /// card effect of the other player while <paramref name="who"/> controlled it (ygopro s.spr: REASON_DESTROY|REASON_EFFECT,
        /// rp ~= tp, IsPreviousControler(tp)), read from its GY entry stamp (DuelEngine.DestroyMonster: reason, the destroying
        /// card's holder, the controller it left). CardInstance.SentByOpponentEffect is computed after the card has left the
        /// field (no controller then), so it stayed false for the player's monsters destroyed by the AI.
        /// </summary>
        public static bool DestroyedByOpponentEffect(DuelistState who, CardInstance card) =>
            who != null && card != null && who.Graveyard != null && who.Graveyard.Contains(card) &&
            (card.GyEntryReason & GyEntryReason.Destroyed) != 0 && (card.GyEntryReason & GyEntryReason.Effect) != 0 &&
            (card.GyEntryReason & GyEntryReason.Battle) == 0 &&
            card.GyEntryReasonPlayer != null && !ReferenceEquals(card.GyEntryReasonPlayer, who) &&
            ReferenceEquals(card.GyEntryPreviousHolder, who);

        /// <summary>
        /// "during your next Standby Phase after …": <see cref="DestroyedByOpponentEffect"/>, and this Standby Phase of
        /// <paramref name="who"/> is the first one after the destruction (ygopro s.spr / s.spcon). The flag resets at the end
        /// of <paramref name="who"/>'s own Standby Phase (RESET_PHASE|PHASE_STANDBY|RESET_SELF_TURN):
        /// <list type="bullet">
        /// <item>destroyed during the opponent's turn: <paramref name="who"/>'s next turn (one turn later);</item>
        /// <item>destroyed during <paramref name="who"/>'s own Draw Phase: that same turn's Standby Phase, and never later
        /// (count 1: the flag is gone after that Standby Phase);</item>
        /// <item>destroyed during <paramref name="who"/>'s own Standby Phase or later in that turn: the next own turn (two
        /// turns later; label = the turn count keeps s.spcon false for the rest of that Standby Phase).</item>
        /// </list>
        /// The phase comes from the GY entry stamp (<see cref="CardInstance.GyEntryPhase"/>).
        /// </summary>
        public static bool NextStandbyAfterDestroyedByOpponent(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || !DestroyedByOpponentEffect(who, card) || card.GyEntryTurn <= 0 ||
                engine.TurnPlayer != who)
                return false;
            // It is who's turn now and turns alternate: a gap of 0 or 2 means the trip happened in who's own turn.
            var gap = engine.TurnNumber - card.GyEntryTurn;
            var ownDrawPhase = card.GyEntryPhase == DuelPhase.Draw;
            return gap == 0 ? ownDrawPhase : gap == 1 || (gap == 2 && !ownDrawPhase);
        }

        /// <summary>
        /// <see cref="EffectActionKind.GyRevivalAfterStandbyWhenDestroyedByOpponent"/>: once per turn, during
        /// controller's Standby Phase after this card was destroyed by opponent's effect and sent to GY,
        /// Special Summon it from GY.
        /// </summary>
        public static void GyRevivalAfterStandbyWhenDestroyedByOpponent(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            if (!who.Graveyard.Contains(source))
            {
                engine.Log($"{source.Name}: not in GY.");
                return;
            }

            if (!NextStandbyAfterDestroyedByOpponent(engine, who, source))
            {
                engine.Log($"{source.Name}: was not destroyed by opponent's effect.");
                return;
            }

            if (source.OncePerTurnStandbyRevivalUsed)
            {
                engine.Log($"{source.Name}: revival already used this turn.");
                return;
            }

            source.OncePerTurnStandbyRevivalUsed = true;
            // OncePerTurnSoft group (v211): the card leaves the GY (it had stayed there while also put on the field), in a
            // face-up position its controller chooses (Lua SpecialSummon(c, 0, tp, tp, false, false, POS_FACEUP)) — the
            // Sacred Phoenix of Nephthys flow: Necrovalley / zone / legality checks, then a human taps it and picks
            // Summon ATK or DEF; the AI takes the better stat.
            if (ContinuousProtections.GyMovementLocked(engine, source, source))
            {
                engine.Log("Necrovalley prevents the card from leaving the GY.");
                return;
            }

            if (ZoneCapsRuntime.UsableMonsterZones(who) <= 0 ||
                !SpecialSummonLegality.CanBeSpecialSummoned(engine, who, source, source, SummonOrigin.Graveyard))
            {
                engine.Log($"{source.Name} cannot be Special Summoned now — it stays in the GY.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Graveyard.Contains(source)
                    ? new System.Collections.Generic.List<CardInstance> { source }
                    : new System.Collections.Generic.List<CardInstance>(),
                Kind = EffectTargetKind.MonsterInEitherGy,
                Prompt = $"{source.Name}: Special Summon it from the GY — tap it, then Summon ATK or Summon DEF (face-up).",
                AskWithOneCandidate = engine.IsHumanControlled(who),
                AsksSpecialSummonPlacement = true,
                AiChoose = list => list.FirstOrDefault(),
                Finish = pick =>
                {
                    var pos = engine.PendingSpecialSummonPosition ??
                              (source.Def != null && source.Def.def > source.Def.atk
                                  ? BattlePosition.Defense
                                  : BattlePosition.Attack);
                    var zone = engine.PendingSpecialSummonZone;
                    engine.ClearPendingSpecialSummonPlacement();
                    if (pick == null || !who.Graveyard.Contains(source)) return;
                    who.Graveyard.Remove(source);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (!engine.SpecialSummonToField(who, source, pos, true, SummonKind.SpecialSummon, zone))
                    {
                        engine.NextSpecialSummonFromGraveyard = false;
                        source.ControlOwner = null;
                        if (!who.Graveyard.Contains(source)) who.Graveyard.Add(source);
                        engine.Log($"{source.Name} cannot be Special Summoned — it stays in the GY.");
                        return;
                    }

                    engine.Log($"{source.Name}: Special Summoned from the GY.");
                    engine.FlushQueuedSummonResponses();
                    engine.NotifyPublic();
                }
            });
        }
    }
}
