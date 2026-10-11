namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DamageStepRules": Battle and Damage Step rules printed in old (MFC / DCR) wordings. Every atom is a
    /// whole sentence, anchored at both ends, so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="DamageStepRulesRuntime"/>.
    /// <list type="bullet">
    /// <item>D.D. Crazy Beast (c48148828): a monster this card destroys by battle is banished (the same clause as Lesser
    /// Fiend, <see cref="EffectActionKind.BanishMonstersDestroyedByBattleWithThis"/>).</item>
    /// <item>Thousand Needles (c33977496): TRIGGER_F at EVENT_DAMAGE_STEP_END — attacked in Defense Position by a
    /// monster whose ATK is lower than this card's DEF, the attacker is destroyed
    /// (<see cref="EffectActionKind.DestroyAttackerAtEndOfDamageStep"/> on <see cref="EffectTiming.EndOfDamageStep"/>).</item>
    /// <item>Continuous Destruction Punch (c68057622): the same destruction from a face-up Continuous Spell
    /// (EFFECT_TYPE_CONTINUOUS at EVENT_DAMAGE_STEP_END: no Chain Link).</item>
    /// <item>Ultimate Obedient Fiend (c32240937): attack condition, and the Effect Monsters it destroys by battle have
    /// their effects negated (<see cref="EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle"/>, this card,
    /// card kind "Effect").</item>
    /// <item>Meteorain (c64274292): your turn only; your monsters inflict piercing battle damage this turn.</item>
    /// <item>Kishido Spirit (c60519422): your monsters cannot be destroyed by battle with a monster of equal ATK.</item>
    /// <item>Cat's Ear Tribe (c95841282): attacked during the opponent's turn, the attacker's original ATK becomes 200
    /// during the Damage Step.</item>
    /// <item>Amazoness Swords Woman (c94004268): battle damage its controller would take from its battles is taken by the
    /// opponent instead.</item>
    /// </list>
    /// </summary>
    public static class DamageStepRulesAtoms
    {
        const string S = @"(?<=^|\.\s)";
        const string E = @"(?:\.|$)(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // D.D. Crazy Beast (c48148828): SINGLE TRIGGER_F at EVENT_BATTLED, the battle target is STATUS_BATTLE_DESTROYED
            // → Duel.Remove it (face-up). It never reaches the GY, as Lesser Fiend's redirect.
            new AtomRegistration.Atom(
                S + @"A monster that this card destroys as a result of battle is removed from play" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.BanishMonstersDestroyedByBattleWithThis,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Thousand Needles (c33977496): SINGLE TRIGGER_F at EVENT_DAMAGE_STEP_END; condition: this card is the attack
            // target, in Defense Position, and the attacker's ATK is lower than its DEF; destroy the attacker if it still
            // relates to the battle.
            new AtomRegistration.Atom(
                S + @"When this Defense Position card is attacked and if the DEF of this card is higher than the ATK of " +
                @"the attacking monster, destroy the attacking monster at the end of the Damage Step" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),
            // Continuous Destruction Punch (c68057622): e1 ACTIVATE (free chain, placement only); e2 FIELD+CONTINUOUS
            // EVENT_DAMAGE_STEP_END, LOCATION_SZONE: the attacker is the opponent's, the attack target is a Defense
            // Position monster, both still relate to the battle, the attacker's ATK is lower than its DEF → destroy the
            // attacker. "Damage calculation is applied normally" is the reminder that the battle itself is unchanged.
            new AtomRegistration.Atom(
                S + @"If the DEF of the attacked Defense Position monster on your side of the field is higher than the ATK " +
                @"of the attacking monster on your opponent['’]s side of the field, destroy the attacking monster\.\s*" +
                @"Damage calculation is applied normally" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DestroyAttackerAtEndOfDamageStep,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Ultimate Obedient Fiend (c32240937), first sentence: e1 SINGLE EFFECT_CANNOT_ATTACK while its controller has
            // more than 1 card on the field or any card in hand.
            new AtomRegistration.Atom(
                S + @"This card can only attack if this card is the only card on your side of the field and if you also " +
                @"have no cards in your hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Ultimate Obedient Fiend (c32240937), second sentence: e2 SINGLE+CONTINUOUS EVENT_BATTLED — the battle target is
            // an Effect Monster (TYPE_EFFECT) that is battle-destroyed → EFFECT_DISABLE + EFFECT_DISABLE_EFFECT,
            // RESETS_STANDARD_EXC_GRAVE (the Twin-Headed Wolf runtime, card kind "Effect").
            new AtomRegistration.Atom(
                S + @"The effects of Effect Monsters that this card destroys are negated" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                    Side = EffectSide.Controller,
                    AffectsThisCardOnly = true,
                    TargetCardKind = "Effect",
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Meteorain (c64274292): ACTIVATE, free chain, condition Duel.IsTurnPlayer(tp); a player EFFECT_PIERCE for
            // LOCATION_MZONE,0 until the End Phase.
            new AtomRegistration.Atom(
                S + @"During this turn, when your monsters attack with an ATK that is higher than the DEF of your " +
                @"opponent['’]s Defense Position monster, inflict the difference as Battle Damage to your opponent['’]s " +
                @"Life Points" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.YourMonstersGainPiercingThisTurn,
                    Side = EffectSide.Controller,
                    ActivationCondition = DamageStepRulesRuntime.YourTurn,
                    MakesChainLink = true
                }),
            // Kishido Spirit (c60519422): e1 ACTIVATE (placement only); e2 FIELD EFFECT_INDESTRUCTABLE_BATTLE,
            // LOCATION_SZONE, target range LOCATION_MZONE,0, value: the battling monster's ATK equals this monster's.
            new AtomRegistration.Atom(
                S + @"Monsters on your side of the field cannot be destroyed in a battle if the monsters['’] ATK are equal" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Cat's Ear Tribe (c95841282): FIELD EFFECT_SET_BASE_ATTACK 200, LOCATION_MZONE, target range 0,LOCATION_MZONE
            // (its battle target), condition: Damage Step or damage calculation, the turn player is the opponent, and
            // this card is the attack target.
            new AtomRegistration.Atom(
                S + @"The original ATK of your opponent['’]s monster\(s\) that battles with this card during his/her turn " +
                @"becomes (\d+) points during the Damage Step" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep,
                    Side = EffectSide.Opponent,
                    Amount = int.Parse(m.Groups[1].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Amazoness Swords Woman (c94004268): SINGLE EFFECT_REFLECT_BATTLE_DAMAGE — the battle damage its controller
            // would take from battles involving this card is taken by the opponent instead.
            new AtomRegistration.Atom(
                S + @"Your opponent takes any battle damage you would have taken from battles involving this card instead" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis,
                    Side = EffectSide.Controller,
                    OnlyBattlesInvolvingThis = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };
    }
}
