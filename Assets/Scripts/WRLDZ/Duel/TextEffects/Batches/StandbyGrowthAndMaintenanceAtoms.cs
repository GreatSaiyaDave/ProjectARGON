using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "StandbyGrowthAndMaintenance" (v142): ATK/DEF that grows on the controller's Standby Phases
    /// (Castle of Dark Illusions, Pumpking the King of Ghosts), Mirror Wall's halving of the opponent's
    /// attackers (its "pay 2000 LP or destroy this card" upkeep is the shared
    /// <see cref="ContinuousRestrictionTemplates"/> pay-or-destroy clause) and Skull Invitation's burn per card
    /// sent to the GY. Every atom is a whole sentence (or the whole text, where its sentences refer to each
    /// other: "each of those Zombie-Type monsters", "this effect resets"), anchored at a sentence start and a
    /// sentence end, so a longer card that merely contains one of these phrases stays uncompiled.
    /// Runtime: <see cref="StandbyGrowthAndMaintenanceRuntime"/>.
    /// </summary>
    public static class StandbyGrowthAndMaintenanceAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        const string Count = @"(\d+|one|two|three|four|five|six|seven|eight|nine|ten)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gaia Soul the Combustible Collective (c51355346): "During the End Phase: Destroy this card." — a
            // maintenance self-destruction (Lua e4 TRIGGER_F EVENT_PHASE+PHASE_END, SetCountLimit(1)). No atom
            // emitted EffectActionKind.DestroyThisCard at EndPhase before this; the phase-trigger driver fires any
            // EndPhase clause, so only the clause was missing. It must stay sentence-anchored: Gaia Soul's other two
            // sentences (piercing, the Pyro tribute) are compiled by other batches.
            new AtomRegistration.Atom(
                S + @"During the End Phase: Destroy this card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.DestroyThisCard,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    MakesChainLink = true
                }),
            // Castle of Dark Illusions (c62121): the whole text. FLIP (Lua EFFECT_TYPE_FLIP): every monster on the
            // field when it resolves (GetFieldID ≤ the highest at resolution, both sides) that is a Zombie gains
            // 200 ATK/DEF; a mandatory trigger on each of the controller's next 4 Standby Phases raises it by 200
            // (ct*200, reset after 4 of your Standby Phases); all of it ends when the Castle leaves the field, is
            // turned face-down or has its effects negated (RESETS_STANDARD_DISABLE).
            new AtomRegistration.Atom(
                S + @"FLIP:\s*All (\w+)(?:-Type)? monsters gain (\d+) ATK and DEF\.\s*" +
                @"During each of your next " + Count + @" Standby Phases, each of those \1(?:-Type)? monsters " +
                @"gains (\d+) more ATK and DEF\.\s*" +
                @"These effects last as long as this card is face-up on the field" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp,
                    RaceFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 200),
                    DefAmount = Parse(m, 2, 200),
                    StandbyGrowthSteps = Parse(m, 3, 4),
                    StandbyGrowthAmount = Parse(m, 4, 200),
                    Side = EffectSide.Both
                }),
            // Pumpking the King of Ghosts (c29155212): the whole text. +100 ATK/DEF while a face-up "Castle of
            // Dark Illusions" is on the field; a mandatory trigger in your Standby Phase, only while that card is
            // on the field, adds 100 more, for four of your Standby Phases; with no such card on the field the
            // whole gain disappears and the count restarts (printed text; the Lua keeps the Standby part).
            new AtomRegistration.Atom(
                S + @"Gains (\d+) ATK/DEF while ""([^""]+)"" is on the field\.\s*" +
                @"Also, during your Standby Phase, if ""\2"" is on the field:\s*This card gains (\d+) ATK/DEF\.\s*" +
                @"This effect activates once per turn, for " + Count + @" of your Standby Phases " +
                @"\(counting this as the first\)\.\s*" +
                @"""\2"" must be on the field to activate and to resolve this effect\.\s*" +
                @"If ""\2"" is not on the field, this ATK/DEF gain disappears and this effect resets" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby,
                    NamedCard = m.Groups[2].Value,
                    Amount = Parse(m, 1, 100),
                    DefAmount = Parse(m, 1, 100),
                    StandbyGrowthAmount = Parse(m, 3, 100),
                    StandbyGrowthSteps = Parse(m, 4, 4),
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Mirror Wall (c22359980), first sentence. Activate (e1, free chain): the group restarts and the
            // monster attacking right now, if it is the opponent's, face-up, is added. Continuous (e2 on
            // EVENT_ATTACK_ANNOUNCE + e3 EFFECT_SET_ATTACK_FINAL GetAttack()/2 on the opponent's Monster Zone):
            // each opponent's monster that declares an attack is added, and every monster in the group has its
            // final ATK halved while this card is on the field.
            AtomRegistration.Atom.Sequence(
                S + @"Each of your opponent['’]s monsters that conducted an attack while this card was face-up on " +
                @"the field has its ATK halved as long as this card remains on the field" + E,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField,
                        Side = EffectSide.Opponent,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField,
                        Side = EffectSide.Opponent,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // Skull Invitation (c98139712): EVENT_TO_GRAVE continuous — 300 x the cards sent that the opponent
            // owns to the opponent, 300 x the ones you own to you, at the same time. The Activate clause is the
            // plain free-chain activation (e1): the watch starts once it resolves.
            AtomRegistration.Atom.Sequence(
                S + @"Each time a card\(s\) is sent to the (?:Graveyard|GY), inflict (\d+) damage to its owner " +
                @"for each card sent" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOwnerPerCardSentToGy,
                        Amount = Parse(m, 1, 300),
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.InflictDamageToOwnerPerCardSentToGy,
                        Amount = Parse(m, 1, 300),
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            var v = m.Groups[group].Value;
            if (int.TryParse(v, out var n)) return n;
            switch (v.ToLowerInvariant())
            {
                case "one": return 1;
                case "two": return 2;
                case "three": return 3;
                case "four": return 4;
                case "five": return 5;
                case "six": return 6;
                case "seven": return 7;
                case "eight": return 8;
                case "nine": return 9;
                case "ten": return 10;
                default: return fallback;
            }
        }
    }
}
