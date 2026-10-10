namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipAndSpellProtection batch: Equip Cards (an Equip Trap included), "destroyed by a card effect while equipped"
    /// triggers, and destruction protections of Equip Cards, equipped monsters and Spells. Each atom is the card's whole
    /// text (or, for Fusion Sword Murasame Blade, its last sentence after the existing "Equip only to … It gains …" equip
    /// template), anchored, so a longer or reworded card stays uncompiled. Runtime: <see cref="EquipAndSpellProtectionRuntime"/>.
    /// <list type="bullet">
    /// <item>Fusion Sword Murasame Blade (c37684215): EFFECT_INDESTRUCTABLE_EFFECT on itself (range SZONE).</item>
    /// <item>Heart of Clear Water (c64801562): aux.AddEquipProcedure(c) (any face-up monster on either field);
    /// EFFECT_INDESTRUCTABLE_BATTLE and a targeting-only EFFECT_INDESTRUCTABLE_EFFECT on the equipped monster;
    /// EFFECT_SELF_DESTROY while the equipped monster's ATK is 1300 or more.</item>
    /// <item>Smoke Grenade of the Thief (c63789924): aux.AddEquipProcedure(c); TRIGGER_F on EVENT_LEAVE_FIELD when destroyed
    /// by a card effect while equipped and now in the GY: look at the opponent's hand and discard 1 card of your choice
    /// (no target).</item>
    /// <item>Blast with Chain (c98239899): Normal Trap that targets 1 face-up monster its controller controls and equips
    /// itself to it (+500 ATK; equip limit: a monster its controller controls); TRIGGER_F on EVENT_LEAVE_FIELD when destroyed
    /// by a card effect while equipped: target 1 card on the field; destroy it.</item>
    /// <item>Woodland Sprite (c6979239): ignition, no once-per-turn; cost: send 1 Equip Card equipped to this card (either
    /// controller's) to the GY; 500 damage to the opponent.</item>
    /// <item>Magic Reflector (c61844784): Normal Spell; target 1 other face-up Spell you control; place 1 counter (0x102a) on
    /// it with a destroy replacement that removes the counter instead.</item>
    /// </list>
    /// </summary>
    public static class EquipAndSpellProtectionAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fusion Sword Murasame Blade (37684215): its last sentence. "Equip only to a Warrior-Type monster. It gains
            // 800 ATK." stays with the existing equip template.
            new AtomRegistration.Atom(
                @"(?<=^|\.\s)While equipped to a monster, this card cannot be destroyed by card effects\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Heart of Clear Water (64801562).
            AtomRegistration.Atom.Sequence(
                @"^\s*If the equipped monster['’]s ATK is (\d+) or higher, this card is destroyed\. " +
                @"The monster equipped with this card is not destroyed as a result of battle or by the effect of a card " +
                @"that targets the monster\.(?:\s*\(Damage calculation is applied normally\.\))?\s*$",
                m => new[]
                {
                    AnyMonsterEquip(0),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast,
                        Amount = int.Parse(m.Groups[1].Value),
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Smoke Grenade of the Thief (63789924).
            AtomRegistration.Atom.Sequence(
                @"^\s*When this card is destroyed by a card effect while equipped to a monster: " +
                @"Look at your opponent['’]s hand and discard 1 card from their hand\.?\s*$",
                _ => new[]
                {
                    AnyMonsterEquip(0),
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.DiscardChosenFromHand,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppHandCards,
                        RequiresTargetChoice = true,
                        ChoiceDoesNotTarget = true,
                        RequiresDestroyedByEffectWhileEquipped = true,
                        MakesChainLink = true
                    }
                }),

            // Blast with Chain (98239899).
            AtomRegistration.Atom.Sequence(
                @"^\s*Target 1 face-up monster you control; equip this card to that target\. It gains (\d+) ATK\. " +
                @"If this card is destroyed by a card effect while equipped: Target 1 card on the field; destroy that " +
                @"target\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Zone = EffectZoneFilter.ControllerMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        EquipAtkBonus = int.Parse(m.Groups[1].Value),
                        // Lua s.eqlimit: c:GetControler()==e:GetOwnerPlayer().
                        EquipLimitSameController = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Both,
                        Zone = EffectZoneFilter.AnyCardOnField,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = 1,
                        RequiresDestroyedByEffectWhileEquipped = true,
                        MakesChainLink = true
                    }
                }),

            // Woodland Sprite (6979239).
            new AtomRegistration.Atom(
                @"^\s*Send 1 Equip Card equipped to this card to the Graveyard\. " +
                @"Inflict (\d+) points of damage to your opponent['’]s Life Points\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = int.Parse(m.Groups[1].Value),
                    RequiresSendEquipSpellOnThisToGy = true,
                    EquipCostAnyEquipCard = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Magic Reflector (61844784).
            new AtomRegistration.Atom(
                @"^\s*Target 1 face-up Spell Card you control; place 1 counter on it\. " +
                @"If it would ever be destroyed, remove the counter instead\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.PlaceDestroyReplacementCounterOnTarget,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerFaceUpSpellsExceptThis,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),
        };

        /// <summary>
        /// aux.AddEquipProcedure(c) with no filter: target 1 face-up monster on either field and equip this card to it
        /// (the Cyclon Laser zone; picker kind EquipAnyMonster).
        /// </summary>
        static EffectClause AnyMonsterEquip(int atk) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.EquipThisToTarget,
            Zone = EffectZoneFilter.FieldAnyMonster,
            Side = EffectSide.Both,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            RequiresTargetFaceUp = true,
            TargetCount = 1,
            EquipAtkBonus = atk,
            StaysOnField = true,
            MakesChainLink = true
        };
    }
}
