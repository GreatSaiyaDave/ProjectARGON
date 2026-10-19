namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmLeftoverSpellTraps" (TLM / SOI Spell &amp; Trap leftovers). Each atom consumes the card's whole text.
    /// Runtime: <see cref="TlmLeftoverSpellTrapsRuntime"/>.
    /// <list type="bullet">
    /// <item>Grave Lure (c57270476): turn the top card of the opponent's Deck face-up, they shuffle; drawn, it is sent to the GY.</item>
    /// <item>Pikeru's Second Sight (c58015506): until the opponent's 2nd End Phase, every card they draw is shown to you.</item>
    /// <item>Level Conversion Lab (c84397023): show 1 hand monster, roll a die: 1 = to the GY, 2-6 = its Level this turn.</item>
    /// <item>Shifting Shadows (c59237154): once per turn, pay 300 LP: your face-down monsters are rearranged at random.</item>
    /// <item>Mesmeric Control (c48642904): during the opponent's next turn they cannot change battle positions by hand.</item>
    /// </list>
    /// </summary>
    public static class TlmLeftoverSpellTrapsAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Grave Lure (c57270476): Lua EFFECT_TYPE_ACTIVATE + EVENT_FREE_CHAIN; s.target chk: the opponent's Deck is not
            // empty; s.activate: ConfirmDecktop, ReverseInDeck, ShuffleDeck(opp), then an EVENT_DRAW continuous effect on
            // that card (SendtoGrave REASON_EFFECT; during a Chain, at EVENT_CHAIN_SOLVED; reset when it leaves the Deck
            // other than to the hand).
            new AtomRegistration.Atom(
                Start + @"Turn the top card of your opponent" + Apos + @"s Deck face-up, then your opponent shuffles their " +
                @"Deck\.\s*When your opponent draws the face-up card, immediately send it to the Graveyard" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TurnOpponentDeckTopFaceUpSendToGyWhenDrawn,
                    Side = EffectSide.Opponent,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty,
                    MakesChainLink = true
                }),

            // Pikeru's Second Sight (c58015506): Lua EVENT_FREE_CHAIN; Duel.RegisterEffect(e1, tp) — EVENT_DRAW,
            // ConfirmCards(1-ep, eg) when the drawer is not the owner; RESET_PHASE|PHASE_END|RESET_OPPO_TURN, 2.
            new AtomRegistration.Atom(
                Start + @"Until your opponent" + Apos + @"s second End Phase, your opponent shows you all cards that they " +
                @"draw before adding them to their hand" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentShowsDrawnCardsUntilTheirSecondEndPhase,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),

            // Level Conversion Lab (c84397023): Lua EVENT_FREE_CHAIN; s.tg chk: a monster in your hand; s.op: SelectMatchingCard
            // (not a target), ConfirmCards, TossDice(tp, 1) (s.roll_dice); 1: SendtoGrave REASON_EFFECT; else EFFECT_CHANGE_LEVEL
            // = result, RESET_EVENT|(RESETS_STANDARD_PHASE_END & ~RESET_TOFIELD).
            new AtomRegistration.Atom(
                Start + @"Select 1 monster in your hand and show it to your opponent, then roll a six-sided die\.\s*" +
                @"If the result is 1, send the selected monster to the Graveyard\.\s*" +
                @"If the result is 2\s*[-–]\s*6, the Level of the selected monster becomes equal to the result until the " +
                @"end of this turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ShowHandMonsterRollDieLevelBecomesResultOrSendToGy,
                    Side = EffectSide.Controller,
                    // Card.IsMonster in the hand: every monster in a hand has a Level of 1 or more.
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.HandHasLevelOrHigherMonster,
                    MinLevelOrHigher = 1,
                    MakesChainLink = true
                }),

            // Shifting Shadows (c59237154): Lua e1 EFFECT_TYPE_ACTIVATE (no effect); e2 EFFECT_TYPE_IGNITION in the Spell &
            // Trap Zone, SetCountLimit(1), Cost.PayLP(300), s.target chk: 2+ face-down monsters in your Main Monster Zones;
            // s.operation: Duel.ShuffleSetCard of those monsters (no relate check).
            new AtomRegistration.Atom(
                Start + @"Once per turn, by paying (\d+) Life Points, rearrange the face-down Defense Position monsters in " +
                @"your Main Monster Zones, then return them in face-down Defense Position" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.ShuffleYourFaceDownMainZoneMonsters,
                    Side = EffectSide.Controller,
                    PayLpAmount = int.TryParse(m.Groups[1].Value, out var lp) ? lp : 300,
                    // The face-down monsters it needs (Lua IsExistingMatchingCard(..., 2, nil)).
                    ConditionAmount = 2,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Mesmeric Control (c48642904): Lua EVENT_FREE_CHAIN; s.regop registers EFFECT_CANNOT_CHANGE_POSITION to the
            // activator, target range (0, LOCATION_MZONE), RESET_PHASE|PHASE_END, 2 (this turn and the next).
            new AtomRegistration.Atom(
                Start + @"During your opponent" + Apos + @"s next turn after you activate this card, your opponent cannot " +
                @"change the battle positions of monsters, except with a card effect" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentCannotChangeBattlePositionsDuringTheirNextTurn,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
        };
    }
}
