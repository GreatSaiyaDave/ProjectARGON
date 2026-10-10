namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card fix "EmptyHandStandbyDraw": a Standby Phase draw gated on what its controller held at the end of the
    /// opponent's last End Phase. The generic PSCT path used to drop the whole condition and compile an
    /// unconditional Standby draw, so The Agent of Wisdom - Mercury drew every turn whatever its hand.
    /// ygopro c38730226: a continuous EVENT_PHASE+PHASE_END effect (EFFECT_FLAG_CANNOT_DISABLE, face-up in the
    /// Monster Zone) checks <c>Duel.IsTurnPlayer(1-tp)</c> and <c>GetFieldGroupCount(tp,LOCATION_HAND,0)==0</c>,
    /// then registers on the card a mandatory EVENT_PHASE|PHASE_STANDBY trigger "Draw 1", reset when the card
    /// leaves the field or is turned face-down (RESETS_STANDARD) and at the end of the next Standby Phase.
    /// Runtime: <see cref="EmptyHandStandbyDrawRuntime"/>.
    /// </summary>
    public static class EmptyHandStandbyDrawAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // The Agent of Wisdom - Mercury (38730226). The whole sentence, from a sentence start to its period:
            // the cards_db print ("… at the end of the opponent's last End Phase") and the current errata
            // ("Once per turn, during …, if you controlled this face-up card in your Monster Zone … last turn").
            // "Once per turn" is not stamped as OncePerTurn: that flag drives ignition bookkeeping, and a
            // Standby Phase trigger already happens once per turn.
            new AtomRegistration.Atom(
                @"(?:^|(?<=[.!?]\s))(?:Once per turn, during|During) your Standby Phase, if you controlled this " +
                @"face-up card(?: in your Monster Zone)? and had no cards in your hand at the end of (?:the|your) " +
                @"opponent's last (?:End Phase|turn): Draw (\d+) cards?\.?(?=\s|$)",
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = int.TryParse(m.Groups[1].Value, out var n) && n > 0 ? n : 1,
                    RequiresEmptyHandAtOpponentsLastEndPhase = true,
                    MakesChainLink = true
                }),
        };
    }
}
