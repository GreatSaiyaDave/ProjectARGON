using System.Linq;
using WRLDZ.Data;

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
        /// <see cref="EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck"/>: opponent sends 1 card of the
        /// declared type (Monster/Spell/Trap) from their Deck to the GY.
        /// </summary>
        public static void MillCardOfDeclaredTypeFromOpponentDeck(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;
            var opp = engine.OpponentOf(who);
            if (opp == null || opp.Deck == null || opp.Deck.Count == 0)
            {
                engine.Log($"{source.Name}: opponent has no Deck.");
                return;
            }

            var db = CardDatabase.Instance ?? CardDatabase.Load();
            var typeWord = TypeDeclarationRuntime.DeclaredTypeOf(source);
            if (string.IsNullOrEmpty(typeWord))
            {
                engine.Log($"{source.Name}: no type declared.");
                return;
            }

            int matchingId = 0;
            foreach (var id in opp.Deck)
            {
                var def = db.Get(id);
                if (def == null) continue;
                if (typeWord == "Monster" && def.IsMonster ||
                    typeWord == "Spell" && def.IsSpell ||
                    typeWord == "Trap" && def.IsTrap)
                {
                    matchingId = id;
                    break;
                }
            }
            if (matchingId == 0)
            {
                engine.Log($"{source.Name}: no {typeWord} in opponent's Deck.");
                return;
            }

            opp.Deck.Remove(matchingId);
            var inst = engine.CreateCardInstance(matchingId);
            TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(engine, opp, inst);
            engine.Log($"{source.Name}: opponent mills {inst.Name} ({typeWord}).");
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

            if (!source.SentByOpponentEffect)
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
            engine.SpecialSummonToField(who, source, BattlePosition.Defense, true);
            engine.Log($"{source.Name}: revived from GY.");
        }
    }
}
