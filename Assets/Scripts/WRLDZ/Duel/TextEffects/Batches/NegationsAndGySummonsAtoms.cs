namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NegationsAndGySummons" (v159): Quick negations and Graveyard Special Summon events. Each atom is
    /// one whole sentence or a whole card text (start of text or after a sentence end, through its final period),
    /// so a longer card that only contains part of it stays uncompiled. Runtime: <see cref="NegationsAndGySummonsRuntime"/>.
    /// </summary>
    public static class NegationsAndGySummonsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Maryokutai (c71466592): QUICK_O on EVENT_CHAINING from the Monster Zone; rp~=tp, re:IsSpellEffect() and
            // re:IsHasType(EFFECT_TYPE_ACTIVATE) (a Spell Card's activation, not an effect of a face-up Spell),
            // Duel.GetTurnPlayer()~=tp. Cost: Cost.SelfTribute. NegateActivation, then destroy it if it is still
            // related. Same answer as Horus the Black Flame Dragon LV8, plus the turn, cost and opponent-only gates.
            new AtomRegistration.Atom(
                S + @"During your opponent's turn, when your opponent activates a Spell Card \(Quick Effect\): " +
                @"You can Tribute this card; negate the activation, and if you do, destroy it" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    IsOptional = true,
                    RequiresTributeThis = true,
                    OpponentTurnOnly = true,
                    NegateOpponentActivationOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // A-Team: Trap Disposal Unit (c13026402): QUICK_O on EVENT_CHAINING from the Monster Zone
            // (aux.DoubleSnareValidity — the answer is still a monster's own effect); rp~=tp, re:IsTrapEffect() with
            // EFFECT_TYPE_ACTIVATE, and not battle-destroyed. Cost: Cost.SelfTribute (the tribute is the track's own
            // "Tribute this face-up card"). NegateActivation, then destroy it if it is still related. Every clause
            // field this needs already exists; the monster answers through SeatChainAnswer's monster branch.
            new AtomRegistration.Atom(
                S + @"(?:This effect can be used during either player's turn\.\s*)?" +
                @"When your opponent activates a Trap Card, Tribute this face-up card to negate the " +
                @"activation of the Trap Card and destroy it" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    IsOptional = true,
                    RequiresTributeThis = true,
                    NegateOpponentActivationOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToTrap = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // Riryoku Field (c70344351): Counter Trap on EVENT_CHAINING, either player's Spell Card activation
            // (re:IsHasType(EFFECT_TYPE_ACTIVATE)) with EFFECT_FLAG_CARD_TARGET whose CHAININFO_TARGET_CARDS is exactly
            // 1 card in LOCATION_MZONE. NegateActivation, then destroy it if it is still related (Magic Jammer's answer).
            new AtomRegistration.Atom(
                S + @"When a Spell Card that targets exactly 1 monster on the field \(and no other cards\) is " +
                @"activated: Negate the activation, and if you do, destroy it" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRequiresSingleMonsterTarget = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // Gilasaurus (c45894482): e1 EFFECT_SPSUMMON_PROC from LOCATION_HAND (s.spcon: a free Monster Zone,
            // SetValue(1) = SUMMON_TYPE_SPECIAL+1); e2 SINGLE+TRIGGER_F on EVENT_SPSUMMON_SUCCESS with
            // GetSummonType()==SUMMON_TYPE_SPECIAL+1: the opponent may (SelectYesNo by 1-tp) Special Summon 1 monster
            // from their GY face-up (NecroValleyFilter, POS_FACEUP). The two sentences only as the whole text.
            AtomRegistration.Atom.Sequence(
                @"^You can Special Summon this card \(from your hand\)\. If Summoned this way: Activate this effect; " +
                @"your opponent can Special Summon 1 monster from their (?:GY|Graveyard)\.$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SpecialSummonThisFromHandByOwnProcedure,
                        FromHand = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ThisCardSummoned,
                        Action = EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy,
                        Side = EffectSide.Opponent,
                        Amount = 1,
                        RequiresThisSpecialSummoned = true,
                        RequiresThisSummonedByOwnProcedure = true,
                        MakesChainLink = true
                    }
                }),
            // Card of Safe Return (c57953380): FIELD+TRIGGER_O on EVENT_SPSUMMON_SUCCESS from LOCATION_SZONE when a
            // Special Summoned monster's previous location is its controller's GY (IsPreviousControler(tp)); draw 1.
            new AtomRegistration.Atom(
                S + @"When a monster is Special Summoned from your (?:Graveyard|GY), you can draw 1 card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Chosen One (c21888494): s.target chk (free Monster Zone, IsSpellTrap ×2 other than itself, 1 monster
            // that can be Special Summoned); s.activate selects g1 (1 monster) then g2 (2 Spell/Traps), the opponent
            // selects 1 of them blind, a monster is Special Summoned face-up and the 2 others sent to the GY, else all 3.
            new AtomRegistration.Atom(
                S + @"Select 1 Monster Card and 2 non-Monster Cards from your hand\. Your opponent randomly selects 1 " +
                @"card among them\. If it is a Monster Card, it is Special Summoned and send the remaining 2 cards to " +
                @"the (?:Graveyard|GY)\. If not, send all the cards to the (?:Graveyard|GY)" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom,
                    ActivationCondition = NegationsAndGySummonsRuntime.ChosenOneHand,
                    MakesChainLink = true
                }),
        };
    }
}
