using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ContinuousNegation batch (v141): continuous Trap / Spell activation locks and negation
    /// (Jinzo, Royal Decree, Imperial Order, Spell Canceller) and the Jinzo-only Equip that spares its
    /// controller's Traps (Amplifier). Whole sentences only; every sentence starts at a sentence
    /// boundary and ends at one, so "Spell and Trap Cards …" or a longer "negate … this turn" never
    /// half-matches. Runtime: <see cref="ContinuousNegationRuntime"/>.
    /// </summary>
    public static class ContinuousNegationAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period, or the end of the text.</summary>
        const string End = @"(?:\.|\s*$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Jinzo / Spell Canceller, first sentence: the card kind cannot be activated at all
            // (hand or field), and neither can effects of that kind on the field.
            new AtomRegistration.Atom(
                Start + @"(Trap|Spell) Cards, and their effects on the field, cannot be activated" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = IsTrap(m, 1)
                        ? EffectActionKind.ContinuousCannotActivateTraps
                        : EffectActionKind.ContinuousCannotActivateSpells,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Jinzo / Spell Canceller / Imperial Order ("all"), Royal Decree ("all other"): negate every
            // face-up card of that kind on the field and every effect of that kind resolving there.
            new AtomRegistration.Atom(
                Start + @"Negate all (other )?(Trap|Spell) effects on the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = IsTrap(m, 2)
                        ? EffectActionKind.ContinuousNegateTrapEffectsOnField
                        : EffectActionKind.ContinuousNegateSpellEffectsOnField,
                    ExceptThisCard = m.Groups[1].Success && m.Groups[1].Length > 0,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Amplifier: the whole card. Equip only to the named monster; while equipped, that monster's
            // Trap lock / negation spares its controller's Traps; leaving the field destroys the equipped
            // monster; activation and effect cannot be negated.
            new AtomRegistration.Atom(
                Start + @"Equip only to ""([^""]+)""\.\s*" +
                @"While this card is equipped, the equipped monster's effect does not negate the effects of " +
                @"its controller's Trap Cards\.\s*" +
                @"When this card is removed from the field, destroy the equipped monster\.\s*" +
                @"This card's activation and effect cannot be negated" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    EquipHostName = m.Groups[1].Value,
                    EquipSparesControllerTrapsFromHostNegation = true,
                    DestroyHostWhenThisLeaves = true,
                    ActivationNegatable = false,
                    EffectNegatable = false,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
        };

        static bool IsTrap(Match m, int group) =>
            m != null && m.Groups.Count > group &&
            string.Equals(m.Groups[group].Value, "Trap", System.StringComparison.OrdinalIgnoreCase);
    }
}
