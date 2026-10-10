using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TurnNegationTraps" (v141): Normal Traps chained to the activation of a Field Spell /
    /// Continuous Spell / Continuous Trap Card that negate every card of that family on the field for the
    /// rest of the turn. Each atom is the card's whole text (both sentences, anchored at both ends), so a
    /// longer or reworded card stays uncompiled.
    /// ygopro c12253117 / c49251811 / c75646520: EFFECT_TYPE_ACTIVATE on EVENT_CHAINING whose condition is
    /// <c>re:IsHasType(EFFECT_TYPE_ACTIVATE)</c> (the activation of a card, not an effect of a card already
    /// on the field) of that type, by either player; the operation registers a field EFFECT_DISABLE on
    /// LOCATION_SZONE of both players for that type, reset at the End Phase. Nothing is targeted.
    /// Runtime: <see cref="TurnNegationTrapsRuntime"/>.
    /// Armored Glass (EVENT_EQUIP, "when a monster is equipped with an Equip Card") is in <see cref="EquipModelAtoms"/>:
    /// the same family negation (<see cref="TurnNegationTrapsRuntime.EquipCard"/>), activated in the window after an equip.
    /// </summary>
    public static class TurnNegationTrapsAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // World Suppression (12253117). "the Field Spell Card": the Lua negates every Field Spell in either
            // Field Zone this turn (one shared Field Zone when the card was printed).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when a Field Spell Card is activated\. " +
                @"Negate the Field Spell Card during the turn this card is activated\.?\s*$",
                _ => Clause(TurnNegationTrapsRuntime.FieldSpell)),
            // Mystic Probe (49251811).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when a Continuous Spell Card is activated\. " +
                @"Negate all Continuous Spell Cards during the turn this card is activated\.?\s*$",
                _ => Clause(TurnNegationTrapsRuntime.ContinuousSpell)),
            // Metal Detector (75646520).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when a Continuous Trap Card is activated\. " +
                @"Negate all Continuous Trap Cards during the turn this card is activated\.?\s*$",
                _ => Clause(TurnNegationTrapsRuntime.ContinuousTrap)),
        };

        /// <summary>
        /// Activate (Normal Trap, Spell Speed 2): legal only as a response to the last Chain Link when that
        /// link is the activation of a card of <paramref name="family"/>; resolves by negating that family on
        /// both fields through this turn.
        /// </summary>
        static EffectClause Clause(string family) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.NegateCardFamilyOnFieldThisTurn,
            Side = EffectSide.Both,
            NegatedCardFamily = family,
            ActivationCondition = TurnNegationTrapsRuntime.ChainLinkActivatesCardFamily,
            ConditionName = family,
            CheckedAt = ConditionCheckedAt.Activation,
            MakesChainLink = true
        };
    }
}
