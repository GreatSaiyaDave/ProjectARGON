using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonLocksAndStatRules". Whole-sentence atoms; runtime in <see cref="SummonLocksAndStatRulesRuntime"/>.
    /// <list type="bullet">
    /// <item>Gyaku-Gire Panda (c9817927): +500 ATK per monster the opponent controls (any position, Lua
    /// GetFieldGroupCount(tp, 0, LOCATION_MZONE)) and EFFECT_PIERCE in its old wording.</item>
    /// <item>Amazoness Tiger (c10979723): SetUniqueOnField(1,0,id) in the "You can only control 1 X" wording, the
    /// existing per-"Amazoness" gain (LegacyTextTemplates), and EFFECT_CANNOT_SELECT_BATTLE_TARGET on the other face-up
    /// "Amazoness" monsters of its controller.</item>
    /// <item>Amazoness Spellcaster (c81325903): two targets, EFFECT_SET_BASE_ATTACK swap until the End Phase.</item>
    /// <item>Rivalry of Warlords (c90846359): the EVENT_ADJUST one-Type rule and its summon locks.</item>
    /// </list>
    /// </summary>
    public static class SummonLocksAndStatRulesAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gyaku-Gire Panda sentence 1: every monster the opponent controls, face-down included (Nuvia's count
            // source with a gain instead of a loss). Not "for each face-up monster" and not a flat "if … controls".
            new AtomRegistration.Atom(
                Start + @"Increase the ATK of this card by (\d+) points? for each monster on your opponent'?s side of " +
                @"the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 500),
                    DefAmount = 0,
                    CountSource = SelfStatMonstersRuntime.OppMonsterCount,
                    MakesChainLink = false
                }),

            // Gyaku-Gire Panda sentence 2: the old wording of piercing on itself (EFFECT_PIERCE).
            new AtomRegistration.Atom(
                Start + @"When this card attacks with an ATK that is higher than the DEF of your opponent'?s Defense " +
                @"Position monster, inflict the difference as Battle Damage to your opponent'?s Life Points" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousPiercing,
                    MakesChainLink = false
                }),

            // Amazoness Tiger sentence 1: c:SetUniqueOnField(1,0,id), the same rule as Gravekeeper's Chief's
            // "You can only control 1 face-up X" (ygopro check_unique_onfield only counts face-up cards, and a Set is
            // not checked). The "face-up" wording stays on SummonTriggersAndTributesAtoms; the quote right after "1 "
            // keeps the two apart.
            new AtomRegistration.Atom(
                Start + @"You can only control 1 ""([^""]+)""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.YouCanOnlyControlOneFaceUpNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    MakesChainLink = false
                }),

            // Amazoness Tiger sentence 3: FIELD EFFECT_CANNOT_SELECT_BATTLE_TARGET, target range (0, MZONE) — the
            // opponent's attacks; value: a face-up monster of the archetype other than this card. Read by
            // ContinuousProtections.CannotBeAttackTarget (the Marauding Captain "except this one" path, archetype
            // filter on NamedCard).
            new AtomRegistration.Atom(
                Start + @"Your opponent cannot attack any face-up ""([^""]+)"" monsters, except this one" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeAttackTarget,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    ExceptThisCard = true,
                    MakesChainLink = false
                }),

            // Amazoness Spellcaster: Lua SelectTarget twice (a face-up archetype monster you control, then a face-up
            // monster the opponent controls); both must still be face-up and related at resolution, then each gets
            // the other's original ATK (EFFECT_SET_BASE_ATTACK, RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                Start + @"Target 1 ""([^""]+)"" monster you control and 1 face-up monster your opponent controls; " +
                @"switch the original ATK of those targets until the end of this turn" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerFaceUpSeriesMonsters,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    RequiresSecondTarget = true,
                    SecondZone = EffectZoneFilter.OppFaceUpMonsters,
                    SecondAction = EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn,
                    MakesChainLink = true
                }),

            // Rivalry of Warlords: both sentences are one rule (EVENT_ADJUST keeps 1 Type per player and sends the
            // rest; the summon locks keep it so).
            new AtomRegistration.Atom(
                Start + @"Each player can only control 1 Type of monster\. Send all other face-up monsters they " +
                @"control to the (?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.EachPlayerKeepsOneMonsterType,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
