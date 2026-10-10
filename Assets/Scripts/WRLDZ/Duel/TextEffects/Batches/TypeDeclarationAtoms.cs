namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TypeDeclaration batch (v142): cards that declare a Monster Type or select Attributes — DNA Surgery,
    /// The Regulation of Tribe, Earthshaker. Whole sentences only: every atom starts at a sentence boundary and
    /// ends at one, and a declaration is compiled only together with the sentence that uses it ("that Type",
    /// "the declared Type"), so a card that declares for another purpose is not compiled here: The Hunter with 7 Weapons
    /// has its own atom in <see cref="BattleBoostsAtoms"/> and Array of Revealing Light's summon-turn lock is in
    /// <see cref="BattleRulesAtoms"/> (v161 batches).
    /// Runtime: <see cref="TypeDeclarationRuntime"/>.
    /// Prohibition (declare a card name) is not here: see the batch notes.
    /// </summary>
    public static class TypeDeclarationAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string End = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // DNA Surgery (c74701381): the Type is announced in the activation's target step (not a target);
            // while the card is face-up (EFFECT_CHANGE_RACE, LOCATION_MZONE both sides) every face-up monster on
            // the field is that Type.
            AtomRegistration.Atom.Sequence(
                Start + @"Activate this card by declaring 1 Monster Type\.\s*" +
                @"All face-up monsters on the field become that Type" + End,
                m => new[]
                {
                    DeclareTypeOnActivation(),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AllFaceUpMonstersBecomeDeclaredType,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // The Regulation of Tribe (c296499), sentences 1–2: the Type is announced in the activation's target
            // step; face-up monsters of that Type on either field cannot declare an attack
            // (EFFECT_CANNOT_ATTACK_ANNOUNCE, c:GetRace() == the declared Type).
            AtomRegistration.Atom.Sequence(
                Start + @"Declare 1 Type of monster\.\s*Monsters of the declared Type cannot attack" + End,
                m => new[]
                {
                    DeclareTypeOnActivation(),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousCannotAttack,
                        RaceFilterIsDeclared = true,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // The Regulation of Tribe, sentences 3–4: the Standby Phase upkeep (EFFECT_TYPE_CONTINUOUS, count
            // limit 1, only in its controller's Standby Phase, EFFECT_FLAG_CANNOT_DISABLE). Tribute 1 monster
            // (Duel.Release, REASON_COST) or destroy this card.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 monster from your side of the field during each of your Standby Phases\.\s*" +
                @"If you do not, this card is destroyed" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.StandbyTributeOrDestroyThis,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Earthshaker (c60866277): Normal Trap, free chain. Needs face-up monsters with 2+ Attributes (target
            // chk); at resolution the activator announces 2 of the Attributes present, the opponent announces 1 of
            // those 2, and every face-up monster with it is destroyed (REASON_EFFECT). Nothing is targeted.
            new AtomRegistration.Atom(
                Start + @"Select 2 Monster Card Attributes\.\s*Your opponent then selects 1 of the 2 Attributes " +
                @"and destroys all face-up monsters with that Attribute on the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo,
                    Side = EffectSide.Both,
                    ActivationCondition = TypeDeclarationRuntime.TwoAttributesFaceUp,
                    MakesChainLink = true
                }),
        };

        /// <summary>
        /// "declaring 1 Monster Type" / "Declare 1 Type of monster": the Type is picked while the card is
        /// activated, as a choice that does not target.
        /// </summary>
        internal static EffectClause DeclareTypeOnActivation() => new()
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.DeclareMonsterType,
            RequiresTargetChoice = true,
            ChoiceDoesNotTarget = true,
            IsPsctTarget = false,
            Zone = EffectZoneFilter.DeclaredMonsterTypes,
            Side = EffectSide.Both,
            StaysOnField = true,
            MakesChainLink = true
        };
    }
}
