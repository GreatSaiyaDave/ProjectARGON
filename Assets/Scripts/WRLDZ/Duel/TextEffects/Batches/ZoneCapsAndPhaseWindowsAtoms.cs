namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ZoneCapsAndPhaseWindows batch. Each atom is the card's whole text, anchored at both ends, so a longer or reworded
    /// card stays uncompiled; the runtimes are in ZoneCapsAndPhaseWindowsRuntime.cs.
    /// <list type="bullet">
    /// <item>Thunder of Ruler (c91781589): Normal Trap, EFFECT_TYPE_ACTIVATE free chain with the condition
    /// IsTurnPlayer(1-tp) and IsPhase(PHASE_STANDBY); EFFECT_CANNOT_BP on the opponent until the End Phase. Offered in the
    /// opponent's Standby window (<see cref="StandbyWindowRuntime"/>).</item>
    /// <item>Neko Mane King (c11021521): SINGLE+TRIGGER_F on EVENT_TO_GRAVE — during the opponent's turn, sent from its
    /// controller's possession by an effect of the opponent: the opponent's turn jumps to its End Phase, no Battle Phase
    /// (<see cref="PhaseJumpRuntime"/>).</item>
    /// <item>Kaiser Colosseum (c35059553): Continuous Spell, free-chain activation; EFFECT_MAX_MZONE on the opponent equal to
    /// its controller's monster count while that is 1+ (not for control changes, nor for monsters its controller's effects
    /// place), and the matching Tribute limit (EFFECT_UNRELEASABLE_SUM) — <see cref="ZoneCapsRuntime"/>. Its last sentence
    /// (cards already on the field are unaffected) is the cap's own nature: nothing already there leaves.</item>
    /// <item>Spatial Collapse (c20644748): Continuous Trap, free chain, activation condition "both players have 5 or fewer
    /// cards on the field"; EFFECT_MAX_MZONE / EFFECT_MAX_SZONE (5 cards per player in all), the Field Spell limits and the
    /// EVENT_ADJUST send — <see cref="ZoneCapsRuntime"/>.</item>
    /// </list>
    /// </summary>
    public static class ZoneCapsAndPhaseWindowsAtoms
    {
        const string Gy = @"(?:Graveyard|GY)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Thunder of Ruler (91781589).
            new AtomRegistration.Atom(
                @"^\s*During your opponent's Standby Phase: You can activate this card; there is no Battle Phase this " +
                @"turn\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ForbidBattlePhaseThisTurn,
                    Side = EffectSide.Opponent,
                    ActivationCondition = StandbyWindowRuntime.OpponentStandbyPhase,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Neko Mane King (11021521).
            new AtomRegistration.Atom(
                @"^\s*During your opponent's turn, when this card in your possession is sent to your " + Gy +
                @" by an opponent's card effect: It becomes the End Phase of this turn\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentToGyFromAnywhere,
                    Action = EffectActionKind.PhaseJumpToEndPhase,
                    Side = EffectSide.Opponent,
                    RequiresSentByOpponentEffectOnTheirTurn = true,
                    MakesChainLink = true
                }),
            // Kaiser Colosseum (35059553).
            new AtomRegistration.Atom(
                @"^\s*If there is 1 or more monster\(s\) on the field of the controller of this card, his/her opponent cannot " +
                @"place a monster on the field if his/her number of monsters would exceed the number of monsters that are " +
                @"on the field of this card's controller\. The cards that are already on the field before this card's " +
                @"activation are unaffected by this effect\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentMonstersCappedByYourMonsterCount,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Spatial Collapse (20644748).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card if both you and your opponent each have 5 or less cards on the " +
                @"field\. The maximum number of cards that each player can have on the field is 5\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.EachPlayerFieldCardsCappedAtFive,
                    Side = EffectSide.Both,
                    ActivationCondition = ZoneCapsRuntime.BothPlayersFiveOrFewerFieldCards,
                    CheckedAt = ConditionCheckedAt.Activation,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
        };
    }
}
