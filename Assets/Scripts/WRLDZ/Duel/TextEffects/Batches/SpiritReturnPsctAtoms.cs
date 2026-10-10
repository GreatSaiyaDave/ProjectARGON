namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card fix "SpiritReturnPsct": the current (PSCT) prints of the Spirit return compile to the same clause as the
    /// old "This card returns to its owner's hand during the End Phase of the turn it is Normal Summoned or flipped
    /// face-up." (PhaseTriggerTemplates' Spirit return: EndPhase ReturnToHand with
    /// <see cref="EffectClause.RequiresSummonedOrFlippedThisTurn"/>, resolved by <see cref="SpiritsAndMiscRuntime"/>).
    /// That template's hand regex does not match "Return it to the hand", so these sentences used to reach the
    /// generic PSCT path, which compiled a targeted "return 1 card on the field" when the Spirit was Normal Summoned
    /// (Asura Priest, Otohime, Dark Dust Spirit) or an End Phase self-return with the "this turn" rider dropped
    /// (Tsukuyomi); since v145 PsctConditionGate refuses them. ygopro: Spirit.AddProcedure / aux.EnableSpiritReturn.
    /// </summary>
    public static class SpiritReturnPsctAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string SentenceStart = @"(?:^|(?<=[.!?]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Asura Priest (2134346), Otohime (39751093 / 39751094), Dark Dust Spirit (89111398).
            new AtomRegistration.Atom(
                SentenceStart + @"During the End Phase of the turn this card is Normal Summoned or flipped face-up: " +
                @"Return it to the hand" + SentenceEnd,
                _ => SpiritReturn()),
            // Tsukuyomi (34853266): the later print. "Once per turn" adds nothing: the return fires once per End Phase.
            new AtomRegistration.Atom(
                SentenceStart + @"Once per turn, during the End Phase, if this card was Normal Summoned or flipped " +
                @"face-up this turn: Return it to the hand" + SentenceEnd,
                _ => SpiritReturn()),
        };

        /// <summary>The clause PhaseTriggerTemplates builds for the old Spirit sentence.</summary>
        static EffectClause SpiritReturn() => new EffectClause
        {
            Timing = EffectTiming.EndPhase,
            Action = EffectActionKind.ReturnToHand,
            RequiresSummonedOrFlippedThisTurn = true,
            MakesChainLink = true
        };
    }
}
