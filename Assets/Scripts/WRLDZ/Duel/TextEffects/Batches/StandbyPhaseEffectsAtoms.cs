using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// StandbyPhaseEffects (v153). Shared sentence shapes, not a card-name list.
    /// Darklord Marie compiles onto the existing Standby <see cref="EffectActionKind.GainLifePoints"/>
    /// with <see cref="EffectClause.ResolvesFromGy"/> (PhaseTriggerTemplates already stamped that action;
    /// the whole-text atom absorbs the GY-range sentence IsBoilerplate does not know).
    /// The four Standby shapes this batch once parked (Graverobber's Retribution, Blind Destruction, The Unfriendly
    /// Amazon, Inspection) are compiled whole by <see cref="StandbyBurnsAndDiceAtoms"/> since v162.
    /// </summary>
    public static class StandbyPhaseEffectsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after a sentence-ending mark.</summary>
        const string S = @"(?<=^|[.!?]\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the text end.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Darklord Marie: mandatory trigger from the GY only in its owner's Standby Phase: gain LP.
            // The GY-range sentence is absorbed here (IsBoilerplate only knows the face-up-on-the-field
            // variant). PhaseTriggerTemplates skips a second GainLifePoints when this span is covered.
            new AtomRegistration.Atom(
                S + @"Once per turn, during your Standby Phase:\s*You gain (\d+) (?:LP|Life Points)\.\s*" +
                @"This card must be in the (?:Graveyard|GY) to activate and to resolve this effect" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainLifePoints,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 200),
                    ResolvesFromGy = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
