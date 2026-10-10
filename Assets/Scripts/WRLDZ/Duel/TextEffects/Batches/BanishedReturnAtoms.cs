namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card fix "BanishedReturn": an End Phase Special Summon of a card from its owner's banished pile, gated on
    /// "if this card is currently banished, and was banished this turn". The generic PSCT path used to drop the
    /// whole condition and compile "Special Summon it" as a targeted either-GY revive, so D.D. Scout Plane never
    /// came back from banishment and, face-up on the field, revived a monster from either GY every End Phase.
    /// ygopro c3773196: an EVENT_REMOVE continuous effect flags the card when it is banished face-up (the flag
    /// resets at the End Phase or when the card moves); a mandatory EVENT_PHASE+PHASE_END trigger with range
    /// LOCATION_REMOVED checks the flag, once per turn, and Special Summons the card in face-up Attack Position
    /// for its controller (the owner), or sends it to the GY when no Monster Zone is free.
    /// Runtime: <see cref="BanishedReturnRuntime"/>.
    /// </summary>
    public static class BanishedReturnAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // D.D. Scout Plane (3773196). The whole sentence, from a sentence start to its period.
            new AtomRegistration.Atom(
                @"(?:^|(?<=[.!?]\s))(Once per turn, during|During) the End Phase, if this card is currently " +
                @"banished, and was banished this turn: Special Summon it in face-up Attack Position\.?(?=\s|$)",
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.SpecialSummonThisFromBanished,
                    Side = EffectSide.Controller,
                    SummonInAttack = true,
                    RequiresBanishedThisTurn = true,
                    OncePerTurn = m.Groups[1].Value.StartsWith("Once", System.StringComparison.OrdinalIgnoreCase),
                    MakesChainLink = true
                }),
        };
    }
}
