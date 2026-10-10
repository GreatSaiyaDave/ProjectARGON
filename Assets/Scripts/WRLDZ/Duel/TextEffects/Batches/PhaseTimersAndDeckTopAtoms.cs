namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PhaseTimersAndDeckTop" (v163): Standby / End Phase timers of Continuous and field-staying Spells/Traps,
    /// turn counts, and FLIP Deck-to-top effects with a "Necrovalley" alternative. Each atom is the card's whole text (every
    /// sentence of it is honoured). Runtime: <see cref="PhaseTimersAndDeckTopRuntime"/>.
    /// </summary>
    public static class PhaseTimersAndDeckTopAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Bottomless Shifting Sand (c76532077): e2 FIELD+TRIGGER_F at EVENT_PHASE+PHASE_END, count limit 1, condition
            // IsTurnPlayer(1-tp); destroys Duel.GetMatchingGroup(IsFaceup, both Monster Zones):GetMaxGroup(GetAttack).
            // e3 FIELD+CONTINUOUS at EVENT_PHASE|PHASE_STANDBY, condition turn player = tp and hand <= 4: destroy this card.
            AtomRegistration.Atom.Sequence(
                Begin + @"At the end of your opponent's turn, destroy the face-up monster\(s\) with the highest ATK on the " +
                @"field\.\s*During your own Standby Phase, if you have (\d+) or less cards in your hand, this card is " +
                @"destroyed" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.EndPhase,
                        Action = EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk,
                        Side = EffectSide.Both,
                        OpponentTurnOnly = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 4),
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Mirage of Nightmare (c41482598): e2 TRIGGER_F in the opponent's Standby Phase (hand < 4): Draw(4 - hand) and
            // label = 4 - hand on the activation effect (cleared when the card is activated). e3 TRIGGER_F in the
            // controller's Standby Phase while the label is set: discard label cards at random (REASON_DISCARD).
            AtomRegistration.Atom.Sequence(
                Begin + @"Once per turn, during your opponent's Standby Phase: Draw cards until you have (\d+) cards in your " +
                @"hand\.\s*If you do, during your next Standby Phase after that: Randomly discard the same number of " +
                @"cards you drew \(or your entire hand, if you do not have enough cards\)" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard,
                        Side = EffectSide.Controller,
                        Amount = Parse(m, 1, 4),
                        OpponentTurnOnly = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.DiscardRandomRecordedDrawCountAtStandby,
                        Side = EffectSide.Controller,
                        StaysOnField = true,
                        MakesChainLink = true
                    }
                }),

            // Different Dimension Capsule (c11961740): aux.RemainFieldCost; target chk a Deck card that can be banished
            // face-down; banish the chosen card face-down, then a continuous Standby effect on the Capsule (own turn,
            // reset after 2 own Standby Phases): 2nd time destroy this card (REASON_RULE) and add the card to the hand
            // while its flag remains (still banished).
            new AtomRegistration.Atom(
                Begin + @"Select 1 card from your Deck and (?:remove it from play|banish it) face-down\.\s*During your " +
                @"2nd Standby Phase after activation, destroy this card and add the (?:removed|banished) card to your " +
                @"hand" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishDeckCardFaceDownAddAtSecondStandby,
                    Side = EffectSide.Controller,
                    ActivationCondition = HandDisruptionRuntime.ControllerDeckAtLeast,
                    ConditionAmount = 1,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Pyro Clock of Destiny (c1082946): legal while some card has effect code 1082946 (a turn count); choose 1 and
            // run that effect's operation once.
            new AtomRegistration.Atom(
                Begin + @"Move the turn count forward by 1 turn\.\s*The turn in which this card is activated continues as " +
                @"normal" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AdvanceTurnCountByOne,
                    Side = EffectSide.Both,
                    ActivationCondition = PhaseTimersAndDeckTopRuntime.TurnCountExists,
                    MakesChainLink = true
                }),

            // A Cat of Ill Omen (c24140059): FLIP, no target; SelectMatchingCard(IsTrap, Deck); if
            // Duel.IsEnvironment(CARD_NECROVALLEY) and SelectYesNo: to hand (confirmed); else ShuffleDeck + MoveSequence 0.
            new AtomRegistration.Atom(
                Begin + @"FLIP:\s*Choose 1 (Trap) from your Deck and place it on top of your Deck, or, if ""([^""]+)"" is on " +
                @"the field, you can add that Trap to your hand instead" + Finish,
                m => DeckTopClause("Trap", m.Groups[2].Value)),

            // An Owl of Luck (c23927567): the same with IsType(TYPE_FIELD).
            new AtomRegistration.Atom(
                Begin + @"FLIP:\s*Select 1 Field Spell Card from your Deck and place it on top of your Deck\.\s*If " +
                @"""([^""]+)"" is on the field, you can add the selected Field Spell Card to your hand instead" + Finish,
                m => DeckTopClause("FieldSpell", m.Groups[1].Value)),

            // Royal Tribute (c72405967): condition Duel.IsEnvironment(CARD_NECROVALLEY, tp); target chk a card other than
            // this one in either hand; SendtoGrave(every monster in both hands, REASON_EFFECT + REASON_DISCARD).
            new AtomRegistration.Atom(
                Begin + @"If you control ""([^""]+)"":\s*Both players discard any monsters in their hands" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothPlayersDiscardAllMonstersInHand,
                    Side = EffectSide.Both,
                    RequiresFaceUpName = m.Groups[1].Value,
                    RequiresControllerNamedCard = true,
                    ActivationCondition = PhaseTimersAndDeckTopRuntime.CardOtherThanThisInEitherHand,
                    MakesChainLink = true
                }),
        };

        static EffectClause DeckTopClause(string kind, string environment) => new EffectClause
        {
            Timing = EffectTiming.Flip,
            Action = EffectActionKind.PlaceNamedFromDeckOnTop,
            Side = EffectSide.Controller,
            DeckPickCardKind = kind,
            AddToHandInsteadIfNamedOnField = environment,
            MakesChainLink = true
        };

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
