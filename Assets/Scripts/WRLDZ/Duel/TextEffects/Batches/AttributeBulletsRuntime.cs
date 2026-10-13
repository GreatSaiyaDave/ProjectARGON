using System;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Coverage family F1 (v180, AttributeBullets) runtime for the Element monsters' conditional bullet effects
    /// (<see cref="AttributeBulletsAtoms"/>). Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><see cref="FieldAttributePresent"/> answers the printed condition "while there is a monster(s) with the
    /// following Attribute(s) on the field": a face-up monster in either Monster Zone whose live Attribute matches (the
    /// printed Attribute as changed by effects such as Scroll of Bewitchment / DNA Transplant). Every engine site that
    /// honors a continuous clause consults it against <see cref="EffectClause.RequiresFieldAttribute"/>: the stat aura
    /// (<see cref="WRLDZ.Duel.FieldSpellEffects.ApplyStatClause"/>), the extra attack (DuelEngine.ExtraAttacksAllowed),
    /// the control lock (MonsterNegationAndControlLocksRuntime.ControlLocked), and the battle-destroyed negation
    /// (NegationMonstersRuntime.BattleNegator).</item>
    /// <item><see cref="DestroyedMonsterByBattleThisTurn"/> is the rider on the WIND bullet "If this card destroyed your
    /// opponent's monster by battle, it can attack once again in a row": read by
    /// <see cref="NegationMonstersRuntime.ExtraAttackConditionMet"/> against
    /// <see cref="CardInstance.DestroyedByBattleThisTurn"/>.</item>
    /// </list>
    /// </summary>
    public static class AttributeBulletsRuntime
    {
        /// <summary>The <see cref="EffectClause.ActivationCondition"/> of the Element WIND bullet's extra attack.</summary>
        public const string DestroyedMonsterByBattleThisTurn = "AttributeBullets.DestroyedMonsterByBattleThisTurn";

        /// <summary>
        /// True while a face-up monster with <paramref name="attribute"/> (case-insensitive) is on the field, either
        /// side. Empty/null <paramref name="attribute"/> is unconditional (true) so the gate is a no-op for clauses
        /// that do not carry one.
        /// </summary>
        public static bool FieldAttributePresent(DuelEngine engine, string attribute)
        {
            if (string.IsNullOrEmpty(attribute)) return true;
            if (engine == null) return false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                {
                    if (m?.Def == null || !m.Def.IsMonster || !m.FaceUp) continue;
                    var a = m.Attribute;
                    if (!string.IsNullOrEmpty(a) && a.Equals(attribute, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
