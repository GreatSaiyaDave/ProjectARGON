namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpiritMonsters": the one sentence each of these LOD Spirits adds to the shared Spirit text (which
    /// compiles through the structural "cannot be Special Summoned" lock and the Spirit return of
    /// PhaseTriggerTemplates / <see cref="SpiritReturnPsctAtoms"/>). Each atom is one whole sentence, anchored at a
    /// sentence start and end, so a longer or reworded sentence stays uncompiled.
    /// Runtime: <see cref="SpiritMonstersRuntime"/>.
    /// </summary>
    public static class SpiritMonstersAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string SentenceStart = @"(?:^|(?<=[.!?]\s))";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Asura Priest (2134346); the same sentence on Berserk Dragon. ygopro c2134346: SINGLE EFFECT_ATTACK_ALL,
            // value 1 (each opponent's monster once per Battle Phase).
            new AtomRegistration.Atom(
                SentenceStart + @"This card can attack all monsters your opponent controls once each" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AttackAllOpponentMonstersOnceEach,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Inaba White Rabbit (77084837). ygopro c77084837: SINGLE EFFECT_DIRECT_ATTACK + SINGLE
            // EFFECT_CANNOT_SELECT_BATTLE_TARGET (value 1 = every monster): it attacks directly and never a monster.
            AtomRegistration.Atom.Sequence(
                SentenceStart + @"This monster attacks your opponent's Life Points directly" + SentenceEnd,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.CanAttackDirectly,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.CannotSelectMonsterAsAttackTarget,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    }
                }),
            // Fushi No Tori (38538445). ygopro c38538445: SINGLE+TRIGGER_F on EVENT_BATTLE_DAMAGE, ep == 1-tp;
            // Duel.Recover(tp, ev) — the battle damage just inflicted.
            new AtomRegistration.Atom(
                SentenceStart + @"When this card inflicts Battle Damage to your opponent's Life Points, increase your " +
                @"Life Points by an amount equal to the Battle Damage" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.GainLifePoints,
                    Side = EffectSide.Controller,
                    AmountIsEquippedBattleDamage = true,
                    MakesChainLink = true
                }),
            // Great Long Nose (2356994). ygopro c2356994: SINGLE+TRIGGER_F on EVENT_BATTLE_DAMAGE, ep == 1-tp;
            // registers EFFECT_SKIP_BP on the opponent for their next turn. Not Yata-Garasu's skip-Draw sentence.
            new AtomRegistration.Atom(
                SentenceStart + @"If this card inflicts Battle Damage to your opponent's Life Points, your opponent " +
                @"skips their next Battle Phase" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.OpponentSkipsNextBattlePhase,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Susa Soldier (40473581). ygopro c40473581: SINGLE EFFECT_CHANGE_BATTLE_DAMAGE,
            // aux.ChangeBattleDamage(1, HALF_DAMAGE) — only the damage it inflicts to its controller's opponent.
            new AtomRegistration.Atom(
                SentenceStart + @"The Battle Damage this card inflicts to your opponent's Life Points is halved" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.HalveBattleDamageThisInflictsToOpponent,
                    Side = EffectSide.Opponent,
                    MakesChainLink = false
                }),
            // Otohime (39751093 / 39751094). ygopro c39751093: SINGLE+TRIGGER_O on EVENT_SUMMON_SUCCESS and EVENT_FLIP,
            // EFFECT_FLAG_CARD_TARGET + DAMAGE_STEP; target 1 face-up monster the opponent controls; face-up Attack ↔
            // face-up Defense.
            new AtomRegistration.Atom(
                SentenceStart + @"When this card is Normal Summoned or flipped face-up: You can target 1 face-up " +
                @"monster your opponent controls; change that target's battle position" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.ChangeBattlePosition,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    RequiresThisNormalOrFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
            // Maharaghi (40695128). ygopro c40695128: SINGLE+TRIGGER_F on EVENT_SUMMON_SUCCESS and EVENT_FLIP registers
            // an EVENT_PREDRAW effect for its controller's next Draw Phase: look at the top card, leave it or move it to
            // the bottom.
            new AtomRegistration.Atom(
                SentenceStart + @"If this card is Normal Summoned or flipped face-up, look at 1 card on the top of " +
                @"your Deck during your next Draw Phase \(before you draw\), and return the card to the top or the " +
                @"bottom of your Deck" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.LookAtTopCardAtNextDrawPhaseTopOrBottom,
                    Side = EffectSide.Controller,
                    RequiresThisNormalOrFlipSummoned = true,
                    AlsoWhenFlippedFaceUp = true,
                    MakesChainLink = true
                }),
        };
    }
}
