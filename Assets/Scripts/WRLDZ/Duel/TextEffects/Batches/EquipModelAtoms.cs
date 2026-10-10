namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipModel batch: cards that answer or move Equip Cards. Each atom is the card's whole text, anchored at both
    /// ends, so a longer or reworded card stays uncompiled; the runtime is <see cref="EquipModelRuntime"/>.
    /// <list type="bullet">
    /// <item>Armored Glass (c36868108): Normal Trap, EFFECT_TYPE_ACTIVATE on EVENT_EQUIP (activated right after a monster
    /// is equipped with an Equip Card, by either player); it registers EFFECT_DISABLE on every TYPE_EQUIP card in both
    /// Spell &amp; Trap Zones, reset at the End Phase. Nothing is targeted. It reuses the TurnNegationTraps family
    /// negation (<see cref="TurnNegationTrapsRuntime.EquipCard"/>).</item>
    /// <item>Tailor of the Fickle (c43641473): Quick-Play Spell, free chain; targets 1 Equip Card equipped to a monster
    /// (not a monster that became an Equip Card, except a Union) and another face-up monster that is a correct target
    /// for it; moves it there, or sends it to the GY if that monster target is gone.</item>
    /// </list>
    /// </summary>
    public static class EquipModelAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Armored Glass (36868108).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when a monster is equipped with an Equip Card\. " +
                @"Negate the effects of all Equip Cards on the field during the turn this card is activated\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.NegateCardFamilyOnFieldThisTurn,
                    Side = EffectSide.Both,
                    NegatedCardFamily = TurnNegationTrapsRuntime.EquipCard,
                    ActivationCondition = EquipModelRuntime.MonsterEquippedWithEquipCard,
                    ConditionName = TurnNegationTrapsRuntime.EquipCard,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Tailor of the Fickle (43641473).
            new AtomRegistration.Atom(
                @"^\s*Switch 1 Equip Card equipped to a monster to another correct target\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.MoveEquipCardToAnotherCorrectTarget,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),
        };
    }
}
