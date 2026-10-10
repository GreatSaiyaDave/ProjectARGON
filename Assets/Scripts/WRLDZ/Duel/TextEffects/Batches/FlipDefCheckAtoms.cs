using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// FlipDefCheck batch: the whole Acid Trap Hole sentence. Before this atom the generic PSCT
    /// "destroy it" fallback compiled it to "destroy any monster" (no face-down filter, no flip, no DEF check,
    /// no return face-down). The pattern is anchored to a sentence start and end, and both DEF numbers must be
    /// the same, so a longer or different card never compiles part of its text here.
    /// Runtime: <see cref="FlipDefCheckRuntime"/>.
    /// </summary>
    public static class FlipDefCheckAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Acid Trap Hole (c41356845): EFFECT_FLAG_CARD_TARGET on a face-down monster in either Monster Zone;
            // ChangePosition(POS_FACEUP_DEFENSE), then Destroy if IsDefenseBelow(2000), else back to
            // POS_FACEDOWN_DEFENSE.
            new AtomRegistration.Atom(
                Start + @"Target 1 face-down Defense Position monster on the field;\s*flip it face-up, then " +
                @"destroy it if its DEF is (\d+) or less, or return it face-down if its DEF is more than \1" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown,
                    Zone = EffectZoneFilter.FieldFaceDownMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    // The DEF threshold. Not Amount: the target prompts read Amount as a number of targets.
                    ConditionAmount = Parse(m, 1, 2000),
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
