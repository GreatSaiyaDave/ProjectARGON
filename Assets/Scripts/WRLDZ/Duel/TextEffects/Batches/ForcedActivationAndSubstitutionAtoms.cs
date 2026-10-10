namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ForcedActivationAndSubstitution batch (v163). Each atom is the card's whole text, anchored at both ends, so a
    /// longer or reworded card stays uncompiled; the runtimes are in ForcedActivationAndSubstitutionRuntime.cs.
    /// <list type="bullet">
    /// <item>Spirit Elimination (c69832741): Normal Spell, free chain, no target; its resolution registers a player effect
    /// on the activator until the End Phase (aux.SpElimFilter): a monster they control is banished instead of each
    /// monster that would be banished from their GY (<see cref="SpiritEliminationRuntime"/>).</item>
    /// <item>Major Riot (c9074847): Normal Trap, EFFECT_TYPE_ACTIVATE on EVENT_TO_HAND when the opponent's effect returned
    /// a monster of the activator's from their Monster Zone to their hand; returns every monster on the field to the hand,
    /// then each player Special Summons from their hand as many Level 4 or lower monsters that can be Normal Summoned/Set
    /// as went to their hand, in face-down Defense Position (<see cref="MajorRiotRuntime"/>).</item>
    /// </list>
    /// </summary>
    public static class ForcedActivationAndSubstitutionAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Spirit Elimination (69832741).
            new AtomRegistration.Atom(
                @"^\s*When monsters in the Graveyard are removed from play, remove monsters on your side of the field " +
                @"from play as substitutes\. This card remains active until the end of the turn that it is activated\. " +
                @"The substitutes remain removed from play\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Major Riot (9074847).
            new AtomRegistration.Atom(
                @"^\s*You can activate this card when 1 or more of your monsters are returned from the field to your " +
                @"hand by your opponent's effect\. Return all Monster Cards on the field to your respective hands\. " +
                @"Both you and your opponent then Special Summon from your hand the same number of Monster Cards on " +
                @"the field in face-down Defense Position\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown,
                    Side = EffectSide.Both,
                    ActivationCondition = MajorRiotRuntime.YourMonstersReturnedToHandByOpponent,
                    CheckedAt = ConditionCheckedAt.Activation,
                    // Lua s.spfilter: c:GetLevel()<=4 and c:IsSummonableCard().
                    SummonLevelMax = 4,
                    MakesChainLink = true
                }),
        };
    }
}
