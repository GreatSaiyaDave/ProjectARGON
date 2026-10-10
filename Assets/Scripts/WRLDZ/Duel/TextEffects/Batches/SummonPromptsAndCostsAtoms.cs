namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonPromptsAndCosts" (v163): an optional end-of-Battle-Phase Special Summon prompt and a Special Summon
    /// cost with a human pick. Runtime: <see cref="SummonPromptsAndCostsRuntime"/>.
    /// <list type="bullet">
    /// <item>Vampire Baby (c56387350): EVENT_PHASE+PHASE_BATTLE optional trigger in the Monster Zone, once per turn, while
    /// its EVENT_BATTLE_DESTROYING flag (RESETS_STANDARD, reset at the end of the Battle Phase) is set. s.filter: in either
    /// GY, REASON_BATTLE, GetReasonCard() == this card, moved there this turn, can be Special Summoned. s.spop: select 1 to
    /// ft of them, Duel.SpecialSummon(…, tp, tp, false, false, POS_FACEUP).</item>
    /// <item>Tyrant Dragon (c94568601) e5: EFFECT_SPSUMMON_COST, range GY, EFFECT_FLAG_CANNOT_DISABLE — s.spcost
    /// Duel.CheckReleaseGroupCost(tp, Card.IsRace, 1, …, RACE_DRAGON), s.spcop SelectReleaseGroupCost + Release. Its other
    /// two sentences are NegationMonsters' (v161) atoms.</item>
    /// </list>
    /// Each atom is a whole sentence (start of text or after a sentence end, through its final period), so a longer card
    /// that only contains part of one stays uncompiled.
    /// </summary>
    public static class SummonPromptsAndCostsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Vampire Baby (c56387350).
            new AtomRegistration.Atom(
                S + @"At the end of the Battle Phase, if this card destroyed a monster by battle and sent it to the " +
                @"(?:Graveyard|GY) this turn: You can Special Summon that monster to your side of the field" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfBattlePhase,
                    Action = EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    OncePerTurn = true,
                    MakesChainLink = true
                }),

            // Tyrant Dragon (c94568601): the GY summon cost (a rule of the card, applied by SpecialSummonLegality).
            new AtomRegistration.Atom(
                S + @"This card cannot be Special Summoned from the (?:GY|Graveyard), unless you Tribute 1 " +
                @"(Dragon|Spellcaster|Zombie|Warrior|Beast|Fiend|Fairy|Insect|Dinosaur|Reptile|Fish|Sea Serpent|Aqua|" +
                @"Pyro|Thunder|Rock|Plant|Machine|Winged Beast|Beast-Warrior|Psychic)(?:-Type)? monster" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GySpecialSummonNeedsTributeCost,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    TributeRaceFilter = m.Groups[1].Value
                }),
        };
    }
}
