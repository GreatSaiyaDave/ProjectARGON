namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TargetNegationsAndHandQuickEffects" (v163): negations of a card's activation that answer what it targets
    /// or would do (Curse of Royal, Gravekeeper's Watcher, the Great Dezard → Fushioh Richie line) and a free-chain Quick
    /// Effect activated from the hand (Charm of Shabti). Each atom is a card's whole text or one whole sentence, anchored
    /// at a sentence start and end, so a longer or reworded text stays uncompiled. Runtime:
    /// <see cref="TargetNegationsAndHandQuickEffectsRuntime"/>.
    /// </summary>
    public static class TargetNegationsAndHandQuickEffectsAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after a sentence end.</summary>
        const string S = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        /// <summary>Great Dezard, sentence 1 (from the start of its text).</summary>
        const string DezardOne =
            @"^When this monster destroys the following number of monsters as a result of battle, the following effects " +
            @"are activated in order:\s*One: As long as this card remains face-up on the field, negate the activation and " +
            @"effects of all Spell and Trap Cards that target this card and destroy them\.";

        /// <summary>Great Dezard, sentence 2 (to the end of its text); group 1 = the monster it Special Summons.</summary>
        const string DezardTwo =
            @"Two: You can Special Summon 1 ""([^""]+)"" from your hand or your Deck by Tributing this card during your " +
            @"Main Phase\.";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Curse of Royal (c2926176), its whole text. Counter Trap on EVENT_CHAINING: re is an EFFECT_TYPE_ACTIVATE (a
            // Spell/Trap Card's activation) with EFFECT_FLAG_CARD_TARGET, CHAININFO_TARGET_CARDS is exactly 1 card, and its
            // CATEGORY_DESTROY operation info is 1 card that is a Spell/Trap on the field (MST, Dust Tornado, Raigeki Break
            // on a Set card); Duel.IsChainNegatable. NegateActivation, then destroy it if it is still related.
            new AtomRegistration.Atom(
                @"^Negate the activation and the effect of a Spell or Trap Card that includes the effect of destroying " +
                @"1 Spell or Trap Card and destroy it\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRespondsToTrap = true,
                    DestroyNegatedCard = true,
                    ActivationCondition = TargetNegationsAndHandQuickEffectsRuntime.LinkTargetsOneSpellTrapToDestroy,
                    MakesChainLink = true
                }),
            // Gravekeeper's Watcher (c26084285), its whole text. QUICK_O on EVENT_CHAINING from LOCATION_HAND
            // (EFFECT_FLAG_DAMAGE_STEP): ep~=tp, re is a Spell/Trap Card's activation or a monster effect, the chain is
            // negatable, and its CATEGORY_HANDES operation info hits the activating player or both players. Cost:
            // Cost.SelfToGrave (send, not discard). NegateActivation, then destroy it if it is still related.
            new AtomRegistration.Atom(
                @"^During either player's turn, when your opponent activates a Spell/Trap Card or monster effect that " +
                @"could make them discard when it resolves: You can send this card from your hand to the " +
                @"(?:GY|Graveyard); negate the activation, and if you do, destroy it\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    IsOptional = true,
                    IsQuickEffect = true,
                    ActivatesFromHand = true,
                    RequiresSendThisToGy = true,
                    NegateOpponentActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRespondsToTrap = true,
                    DestroyNegatedCard = true,
                    ActivationCondition = TargetNegationsAndHandQuickEffectsRuntime.OpponentLinkMakesActivatorDiscard,
                    MakesChainLink = true
                }),
            // Charm of Shabti (c50412166), its whole text. QUICK_O on EVENT_FREE_CHAIN from LOCATION_HAND, condition: not
            // Main Phase 2 or the End Phase (no EFFECT_FLAG_DAMAGE_STEP). Cost: Cost.SelfDiscard. Operation: a FIELD effect
            // registered to the player (Duel.RegisterEffect(e1,tp)), EFFECT_INDESTRUCTABLE_BATTLE on their LOCATION_MZONE for
            // IsSetCard(SET_GRAVEKEEPERS), RESET_PHASE|PHASE_END.
            new AtomRegistration.Atom(
                @"^During either player's turn: You can discard this card; until the End Phase, ""([^""]+)"" monsters " +
                @"you control cannot be destroyed by battle\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.HandQuickEffect,
                    Action = EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    RequiresDiscardSelf = true,
                    ActivatesFromHand = true,
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Great Dezard (c88989706), its whole text, as two atoms (one per sentence, each anchored to the whole text by a
            // lookaround, so each clause keeps its own sentence as its snippet). e1 SINGLE+CONTINUOUS on
            // EVENT_BATTLE_DESTROYING: a flag per monster destroyed by battle (RESETS_STANDARD: lost when it leaves the field
            // or is turned face-down). e2 QUICK_F on EVENT_CHAINING in LOCATION_MZONE (DAMAGE_STEP, DAMAGE_CAL), flag >= 1: a
            // Spell/Trap Card's activation (EFFECT_TYPE_ACTIVATE) with EFFECT_FLAG_CARD_TARGET whose targets include this
            // card; NegateActivation then destroy, only while it is Chain Link ev+1.
            new AtomRegistration.Atom(
                DezardOne + @"(?=\s+" + DezardTwo + @"$)",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy,
                    Side = EffectSide.Controller,
                    ConditionAmount = 1,
                    DestroyNegatedCard = true,
                    MakesChainLink = false
                }),
            // Great Dezard, sentence 2. e3 IGNITION, flag >= 2, Cost.SelfTribute: Special Summon 1 "Fushioh Richie" from the
            // hand or Deck (the player selects which copy; IsCanBeSpecialSummoned(e,0,tp,true,false): its summoning condition
            // is ignored), then CompleteProcedure (TributeSummonsRuntime: the named card completes the procedure).
            new AtomRegistration.Atom(
                @"(?<=" + DezardOne + @"\s+)" + DezardTwo + @"$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonChosenFromHandOrDeck,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.HandOrDeckNamed,
                    NamedCard = m.Groups[1].Value,
                    RequiresTargetChoice = true,
                    ChoiceDoesNotTarget = true,
                    RequiresTributeThis = true,
                    ActivationCondition = TargetNegationsAndHandQuickEffectsRuntime.ThisDestroyedMonstersByBattle,
                    ConditionAmount = 2,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Fushioh Richie (c39711336), sentence 4. e3 QUICK_F on EVENT_CHAINING in LOCATION_MZONE (no battle count): the
            // same negation as Great Dezard's. Its sentences 1-2 are the structural summon condition
            // (TributeSummonsRuntime.ProcedureCardName: only Great Dezard's effect summons it, and once it has, it may be
            // revived); sentence 3, the flip-face-down ignition, compiles in FlipDownAndPeekAtoms.
            new AtomRegistration.Atom(
                S + @"As long as this monster remains face-up on the field, negate the activation and effects of all " +
                @"Spell and Trap Cards that target this card and destroy them" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy,
                    Side = EffectSide.Controller,
                    ConditionAmount = 0,
                    DestroyNegatedCard = true,
                    MakesChainLink = false
                }),
            // Fushioh Richie (c39711336), sentence 5. e2 SINGLE+TRIGGER_O on EVENT_FLIP (DAMAGE_STEP, CARD_TARGET): target
            // 1 Zombie monster in your GY that can be Special Summoned (a free Monster Zone); Special Summon it if it is still
            // related. "When this card is flipped face-up" fires on a Flip Summon and when flipped by battle or an effect
            // (the Royal Keeper shape, FlipDownAndPeekAtoms).
            new AtomRegistration.Atom(
                S + @"When this card is flipped face-up, you can Special Summon 1 (\w+)-Type monster from your " +
                @"(?:GY|Graveyard)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    RaceFilter = m.Groups[1].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    RequiresThisFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
        };
    }
}
