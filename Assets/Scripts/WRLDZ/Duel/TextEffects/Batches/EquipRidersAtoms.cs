using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipRiders batch: Equip Spell riders and linked curses, each compiled from its WHOLE printed text (anchored
    /// to the start and the end of the text), so a longer or different card never compiles part of it here.
    /// <list type="bullet">
    /// <item>Cyclon Laser (c5494820): Equip only to the named monster, on either field (aux.AddEquipProcedure with
    /// p = nil); +N ATK (EFFECT_UPDATE_ATTACK); the old "inflict the difference as Battle Damage" wording is piercing
    /// (EFFECT_PIERCE).</item>
    /// <item>Mask of the Accursed (c56948373): Equip to any face-up monster on either field; it cannot attack; your
    /// Standby Phase, once per turn, mandatory: N damage to the equipped monster's current controller.</item>
    /// <item>Ekibyo Drakmord (c69954399) is not compiled here: EngineHygieneAtoms / EngineHygieneRuntime (v160) own it.</item>
    /// <item>Mask of Dispel (c20765952) is compiled whole by <see cref="StandbyBurnsAndDiceAtoms"/> (v162): its link,
    /// Standby burn and leave-field destruction run in <see cref="StandbyBurnsAndDiceRuntime"/>.</item>
    /// </list>
    /// No EquipRidersRuntime: Mask of the Accursed's Standby burn resolves in TextEffectRuntime.ApplyClause.
    /// </summary>
    public static class EquipRidersAtoms
    {
        /// <summary>Whole text: nothing before the first sentence.</summary>
        const string Begin = @"^\s*";

        /// <summary>Whole text: the last sentence's period (optional), then only whitespace.</summary>
        const string Finish = @"\.?\s*$";

        /// <summary>A straight or typographic apostrophe.</summary>
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cyclon Laser. The name must be the same in all three sentences (\1).
            AtomRegistration.Atom.Sequence(
                Begin + @"You can only equip this card to ""([^""]+)""\.\s*" +
                @"Increase the ATK of ""\1"" by (\d+) points?\.\s*" +
                @"When the equipped ""\1"" attacks with an ATK that is higher than the DEF of a Defense Position " +
                @"monster, inflict the difference as Battle Damage to your opponent" + Apos + @"s Life Points" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        EquipHostName = m.Groups[1].Value,
                        EquipAtkBonus = Parse(m, 2, 300),
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedGainsPiercing,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Mask of the Accursed.
            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster cannot attack\.\s*" +
                @"Once per turn, during your Standby Phase: Inflict (\d+) damage to the controller of the equipped " +
                @"monster" + Finish,
                m => new[]
                {
                    CurseEquip(),
                    EquippedCannotAttack(),
                    new EffectClause
                    {
                        // Lua: EFFECT_TYPE_TRIGGER_F, EVENT_PHASE+PHASE_STANDBY, Duel.IsTurnPlayer(tp), count limit 1.
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.InflictDamageToLinkedCardController,
                        Amount = Parse(m, 1, 500),
                        StaysOnField = true,
                        MakesChainLink = true
                    }
                }),

            // Ekibyo Drakmord is compiled by EngineHygieneAtoms (v160), with its End Phase countdown runtime.

            // Mask of Dispel is compiled by StandbyBurnsAndDiceAtoms (v162), with its link and leave-field runtime.
        };

        /// <summary>aux.AddEquipProcedure(c): any face-up monster on either field.</summary>
        static EffectClause CurseEquip() => new()
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.EquipThisToTarget,
            Zone = EffectZoneFilter.FieldAnyMonster,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            StaysOnField = true,
            MakesChainLink = true
        };

        /// <summary>EFFECT_TYPE_EQUIP + EFFECT_CANNOT_ATTACK.</summary>
        static EffectClause EquippedCannotAttack() => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.EquippedCannotAttack,
            StaysOnField = true,
            MakesChainLink = false
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
