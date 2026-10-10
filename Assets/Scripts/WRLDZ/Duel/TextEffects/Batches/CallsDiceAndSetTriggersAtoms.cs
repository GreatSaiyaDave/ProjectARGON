using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CallsDiceAndSetTriggers" (v163): calls, declarations and dice made by either player, and the
    /// mandatory GY triggers of a Trap destroyed while Set. Each atom is the card's whole text (every sentence of it is
    /// honoured). The runtime is <see cref="CallsDiceAndSetTriggersRuntime"/>.
    /// </summary>
    public static class CallsDiceAndSetTriggersAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";
        const string Gy = @"(?:Graveyard|GY)";
        const string Apos = @"['’]";
        const string WhenDestroyedFaceDown = Begin + @"When this face-down card is destroyed and sent to the " + Gy + @",\s*";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Statue of the Wicked (c65810489): no EFFECT_TYPE_ACTIVATE (it cannot be activated); TRIGGER_F on
            // EVENT_TO_GRAVE with REASON_DESTROY, previous location on the field and previous position face-down (any
            // destroyer); CreateToken + SpecialSummon face-up for tp (the GY owner) when a zone is free.
            new AtomRegistration.Atom(
                WhenDestroyedFaceDown + @"Special Summon (\d+) ""([^""]+ Token)"" \(([A-Za-z]+)(?:-Type)?/" +
                @"(LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE)/Level (\d+)/ATK (\d+)/DEF (\d+)\) on your side of the field" +
                Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonToken,
                    Side = EffectSide.Controller,
                    TokenCount = Parse(m, 1, 1),
                    TokenName = m.Groups[2].Value.Trim(),
                    TokenRace = m.Groups[3].Value.Trim(),
                    TokenAttribute = m.Groups[4].Value.ToUpperInvariant(),
                    TokenLevel = Parse(m, 5, 1),
                    TokenAtk = Parse(m, 6, 0),
                    TokenDef = Parse(m, 7, 0),
                    RequiresDestroyedWhileFaceDown = true,
                    MakesChainLink = true
                }),

            // Dark Coffin (c1804528): no EFFECT_TYPE_ACTIVATE; TRIGGER_F on EVENT_TO_GRAVE (REASON_DESTROY, previously
            // face-down on the field). SelectOption(1-tp) among the options that are available: RandomSelect 1 card of
            // 1-tp's hand → SendtoGrave(REASON_EFFECT|REASON_DISCARD), or 1-tp Selects 1 monster in their Monster Zone →
            // Destroy (not a target).
            new AtomRegistration.Atom(
                WhenDestroyedFaceDown + @"your opponent selects and executes 1 of the following effects:\s*" +
                @"[●•]?\s*Discard 1 random card in their hand\.\s*" +
                @"[●•]?\s*Select 1 monster on their side of the field and destroy it" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster,
                    Side = EffectSide.Opponent,
                    RequiresDestroyedWhileFaceDown = true,
                    MakesChainLink = true
                }),

            // Cobra Jar (c86801871): FLIP; CreateToken + SpecialSummonStep face-up for tp, then a FIELD+CONTINUOUS
            // EVENT_BATTLE_DESTROYED effect registered to tp with the Token as its label: when that Token is destroyed by
            // battle, Damage(1-tp, 500). It lasts after Cobra Jar is gone and resets once the Token leaves otherwise.
            new AtomRegistration.Atom(
                Begin + @"FLIP:\s*Special Summon 1 ""([^""]+ Token)"" \(([A-Za-z]+)(?:-Type)?/" +
                @"(LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE)/(\d+) Stars?/ATK (\d+)/DEF (\d+)\)\.\s*" +
                @"When the ""\1"" is destroyed as a result of battle, inflict (\d+) (?:points of )?damage to your " +
                @"opponent" + Apos + @"s Life Points" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.SpecialSummonToken,
                    Side = EffectSide.Controller,
                    TokenCount = 1,
                    TokenName = m.Groups[1].Value.Trim(),
                    TokenRace = m.Groups[2].Value.Trim(),
                    TokenAttribute = m.Groups[3].Value.ToUpperInvariant(),
                    TokenLevel = Parse(m, 4, 1),
                    TokenAtk = Parse(m, 5, 0),
                    TokenDef = Parse(m, 6, 0),
                    TokenBattleDestroyedDamageToOpponent = Parse(m, 7, 500),
                    MakesChainLink = true
                }),

            // Dice Jar (c3549275): FLIP, CATEGORY_DICE; while d1 == d2: d1, d2 = TossDice(tp, 1, 1) (one die-roll event
            // for both players); the lower roller takes the other roll x 500, or 6000 when the winner rolled a 6.
            new AtomRegistration.Atom(
                Begin + @"FLIP:\s*Both players roll a six-sided die\.\s*" +
                @"The player with the lower result takes damage equal to their opponent" + Apos + @"s roll x (\d+)\.\s*" +
                @"However, if the winner rolled a 6, the loser takes (\d+) damage\.\s*" +
                @"If the rolls are the same, both players roll again" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 500),
                    MaxRollDamage = Parse(m, 2, 6000),
                    MakesChainLink = true
                }),

            // Ordeal of a Traveler (c39537362): Continuous Trap, free-chain activation; e2 FIELD+TRIGGER_O on
            // EVENT_ATTACK_ANNOUNCE in the SZONE, condition: the attacker is the opponent's; target chk: you have a card
            // in hand. Resolution: hand not empty and the attacker still related; RandomSelect 1 card of your hand,
            // 1-tp declares Monster / Spell / Trap, ConfirmCards; a wrong call returns the attacker to the hand.
            new AtomRegistration.Atom(
                Begin + @"When an opponent" + Apos + @"s monster declares an attack:\s*" +
                @"Your opponent chooses 1 random card in your hand and calls the type of the card " +
                @"\(Monster, Spell, or Trap\), and if they call it wrong, return the attacking monster to the hand" +
                Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker,
                    Side = EffectSide.Opponent,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Reversal Quiz (c5990062): cost — every other card in your hand and on your field to the GY (at least 1,
            // all able to go there as a cost); resolution: no top card → nothing; SelectOption(tp) Monster / Spell /
            // Trap, ConfirmDecktop(1); a right call: SetLP(tp, lp of 1-tp) and SetLP(1-tp, lp of tp); ShuffleDeck(tp).
            new AtomRegistration.Atom(
                Begin + @"Send all cards in your hand and on your side of the field to the " + Gy + @"\.\s*" +
                @"Call the type of card \(Spell, Trap, or Monster\) on top of your Deck\.\s*" +
                @"If you call it right, exchange your current Life Points with your opponent" + Apos +
                @"s current ones" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.CallTopDeckCardTypeRightExchangesLp,
                    Side = EffectSide.Both,
                    CostSendAllOtherHandAndFieldCardsToGy = true,
                    ActivationCondition = CallsDiceAndSetTriggersRuntime.OtherCardInHandOrOnField,
                    MakesChainLink = true
                }),

            // Reasoning (c58577036): target chk — a free Monster Zone, the player can Special Summon and mill, and a
            // monster that can be Normal Summoned/Set is in the Deck. AnnounceLevel(1-tp) (1-12); excavate down to the
            // nearest such monster; Special Summon it (POS_FACEUP) if a zone is free, its Level is not the declared one
            // and it can be Special Summoned, the cards above it to the GY; else every excavated card to the GY.
            new AtomRegistration.Atom(
                Begin + @"Your opponent declares a monster Level from 1 to 12\.\s*" +
                @"Excavate cards from the top of your Deck until you excavate a monster that can be Normal " +
                @"Summoned/Set, then, if that monster is the same Level as the one declared by your opponent, send " +
                @"all excavated cards to the " + Gy + @"\.\s*" +
                @"If not, Special Summon the excavated monster, also send the remaining cards to the " + Gy + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster,
                    Side = EffectSide.Controller,
                    ActivationCondition = CallsDiceAndSetTriggersRuntime.CanExcavateSummonableMonster,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
