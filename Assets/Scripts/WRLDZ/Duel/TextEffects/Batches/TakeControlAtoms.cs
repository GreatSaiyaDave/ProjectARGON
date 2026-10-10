using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TakeControl batch (v141 curriculum MRD/SRL/PSV): whole-sentence atoms for Brain Control, the four
    /// Charmers and Invader of the Throne. Each pattern is anchored to a sentence start and a sentence end,
    /// so a longer card that merely contains one of these phrases never compiles part of its text here.
    /// Runtime: <see cref="TakeControlRuntime"/>.
    /// </summary>
    public static class TakeControlAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        const string Attribute = @"(EARTH|WATER|FIRE|WIND|LIGHT|DARK|DIVINE)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Brain Control (c87910978): cost Pay LP; target a face-up opponent's monster that can be
            // Normal Summoned/Set (IsSummonableCard); GetControl until the End Phase.
            new AtomRegistration.Atom(
                Start + @"Pay (\d+) LP, then target 1 face-up monster your opponent controls that can be " +
                @"Normal Summoned/Set;\s*take control of that target until the End Phase" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TakeControlUntilEndPhase,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Amount = 1,
                    PayLpAmount = Parse(m, 1, 800),
                    TargetMustBeNormalSummonable = true,
                    MakesChainLink = true
                }),
            // Eria the Water Charmer / Hiita the Fire Charmer (c74364659 / c759393).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Target 1 face-up " + Attribute + @" monster your opponent controls;\s*" +
                @"take control of that target while this card is face-up on the field" + End,
                m => CharmerClause(m)),
            // Aussa the Earth Charmer / Wynn the Wind Charmer (c37970940 / c37744402): older wording of the
            // same effect — the Lua targets 1 face-up monster of that Attribute the opponent controls.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*While this card is face-up on the field, take control of 1 " + Attribute +
                @" monster your opponent controls" + End,
                m => CharmerClause(m)),
            // Invader of the Throne (c3056267): targets (EFFECT_FLAG_CARD_TARGET) any monster the opponent
            // controls, face-up or face-down; Duel.SwapControl; condition not Duel.IsBattlePhase().
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Select 1 monster on your opponent['’]s side of the field and switch control " +
                @"of it with this card\.\s*This effect cannot be activated during the Battle Phase" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.SwitchControlWithThis,
                    Zone = EffectZoneFilter.OppMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Amount = 1,
                    CannotActivateInBattlePhase = true
                }),
        };

        static EffectClause CharmerClause(Match m) => new EffectClause
        {
            Timing = EffectTiming.Flip,
            Action = EffectActionKind.TakeControlWhileSourceFaceUp,
            Zone = EffectZoneFilter.OppFaceUpMonsters,
            AttributeFilter = m.Groups[1].Value.ToUpperInvariant(),
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            Amount = 1
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
