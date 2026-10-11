using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// DcrEquipSpells batch: six Dark Crisis Equip Spells, each compiled from its WHOLE printed text (anchored to the start
    /// and the end of the text, the EquipRiders pattern), so a longer or different card never compiles part of it here.
    /// Every equip is aux.AddEquipProcedure (any face-up monster on either field, the equip targets it).
    /// <list type="bullet">
    /// <item>Shooting Star Bow - Ceal (c95638658): EFFECT_UPDATE_ATTACK -N and EFFECT_DIRECT_ATTACK, both EFFECT_TYPE_EQUIP
    /// (<see cref="EffectActionKind.EquippedCanAttackDirectly"/>).</item>
    /// <item>Twin Swords of Flashing Light - Tryce (c21900719): cost Duel.DiscardHand(IsAbleToGraveAsCost, 1, REASON_COST)
    /// excluding itself — a SEND of 1 other hand card, not a discard (<see cref="EffectClause.DiscardCostIsSend"/>);
    /// -N ATK and EFFECT_EXTRA_ATTACK 1 (<see cref="EffectActionKind.EquippedGainsExtraAttacks"/>).</item>
    /// <item>Wicked-Breaking Flamberge - Baou (c68427465): the same send cost, then the equip target; +N ATK; EVENT_BATTLED
    /// (SZONE): the battle target of the equipped monster, an Effect Monster destroyed by that battle, has its effects
    /// negated (EFFECT_DISABLE + DISABLE_EFFECT, RESETS_STANDARD_EXC_GRAVE) — NegationMonsters' battle negation with an
    /// equip-host scope (<see cref="EffectClause.AppliesToEquippedHost"/>, TargetCardKind "Effect").</item>
    /// <item>Rod of the Mind's Eye (c94793422): EFFECT_CHANGE_BATTLE_DAMAGE aux.ChangeBattleDamage(1, N)
    /// (<see cref="EffectActionKind.EquippedBattleDamageToOpponentBecomes"/>).</item>
    /// <item>Rod of Silence - Kay'est (c95515060): EFFECT_UPDATE_DEFENSE +N; EVENT_CHAIN_SOLVING negate + destroy of a
    /// Spell effect that targets the equipped monster, and EFFECT_DISABLE + SELF_DESTROY of a Spell that keeps it as its
    /// card target — NegationMonsters' target negation with an equip-host scope (Spells only, never this card).</item>
    /// <item>Butterfly Dagger - Elma (c69243953): +N ATK; SINGLE+TRIGGER_O EVENT_LEAVE_FIELD, retcon: in the GY,
    /// REASON_DESTROY, still equipped as it was destroyed (GetEquipTarget, so not sent because its monster left) — the
    /// EquipAndSpellProtection destroyed-while-equipped stamp; "You can" return this card to the hand
    /// (<see cref="EffectActionKind.ReturnThisFromGyToHand"/>).</item>
    /// </list>
    /// Runtime: <see cref="DcrEquipSpellsRuntime"/>.
    /// </summary>
    public static class DcrEquipSpellsAtoms
    {
        /// <summary>Whole text: nothing before the first sentence.</summary>
        const string Begin = @"^\s*";

        /// <summary>Whole text: the last sentence's period (optional), then only whitespace.</summary>
        const string Finish = @"\.?\s*$";

        const string Gy = @"(?:Graveyard|GY)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Shooting Star Bow - Ceal.
            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster loses (\d+) ATK, but it can attack your opponent directly" + Finish,
                m => new[]
                {
                    OwnHelperEquip(-Parse(m, 1)),
                    Marker(EffectActionKind.EquippedCanAttackDirectly)
                }),

            // Twin Swords of Flashing Light - Tryce.
            AtomRegistration.Atom.Sequence(
                Begin + @"Send 1 card from your hand to the " + Gy + @"; equip this card to a monster\.\s*" +
                @"It loses (\d+) ATK, but it can make a second attack during each Battle Phase" + Finish,
                m =>
                {
                    var equip = OwnHelperEquip(-Parse(m, 1));
                    SendFromHandCost(equip);
                    var extra = Marker(EffectActionKind.EquippedGainsExtraAttacks);
                    extra.Amount = 1;
                    return new[] { equip, extra };
                }),

            // Wicked-Breaking Flamberge - Baou.
            AtomRegistration.Atom.Sequence(
                Begin + @"Send 1 card from your hand to the " + Gy + @", then target 1 monster on the field; equip this " +
                @"card to that target\.\s*It gains (\d+) ATK\.\s*" +
                @"Negate the effects of opponent['’]s monsters destroyed by battle with the equipped monster" + Finish,
                m =>
                {
                    var equip = OwnHelperEquip(Parse(m, 1));
                    SendFromHandCost(equip);
                    var negate = Marker(EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle);
                    negate.AppliesToEquippedHost = true;
                    // Lua s.negop: bc:IsType(TYPE_EFFECT).
                    negate.TargetCardKind = "Effect";
                    return new[] { equip, negate };
                }),

            // Rod of the Mind's Eye.
            AtomRegistration.Atom.Sequence(
                Begin + @"If a monster equipped with this card inflicts Battle Damage to your opponent, the damage " +
                @"becomes (\d+) points?" + Finish,
                m =>
                {
                    var change = Marker(EffectActionKind.EquippedBattleDamageToOpponentBecomes);
                    change.Amount = Parse(m, 1);
                    return new[] { OwnHelperEquip(0), change };
                }),

            // Rod of Silence - Kay'est.
            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster gains (\d+) DEF\.\s*" +
                @"Negate other Spell effects that target the equipped monster, and if you do, destroy that Spell Card" +
                Finish,
                m =>
                {
                    var equip = OwnHelperEquip(0);
                    equip.EquipDefBonus = Parse(m, 1);
                    var negate = Marker(EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy);
                    negate.NegateRespondsToSpell = true;
                    negate.DestroyNegatedCard = true;
                    negate.AppliesToEquippedHost = true;
                    negate.ExceptThisCard = true;
                    return new[] { equip, negate };
                }),

            // Butterfly Dagger - Elma.
            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster gains (\d+) ATK\.\s*" +
                @"When this card is destroyed and sent to the " + Gy + @" while equipped: You can return this card to " +
                @"the hand" + Finish,
                m => new[]
                {
                    OwnHelperEquip(Parse(m, 1)),
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.ReturnThisFromGyToHand,
                        RequiresDestroyedByEffectWhileEquipped = true,
                        IsOptional = true,
                        // "When … : You can" — it misses the timing like every optional When trigger.
                        CheckedAt = ConditionCheckedAt.Activation,
                        MakesChainLink = true
                    }
                }),
        };

        /// <summary>
        /// aux.AddEquipProcedure(c): target 1 face-up monster on either field and equip this card to it, with the Equip's
        /// ATK change; the AI equips its own monster (<see cref="EffectClause.AiEquipsOwnMonster"/>).
        /// </summary>
        static EffectClause OwnHelperEquip(int atk) => new EffectClause
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
            AiEquipsOwnMonster = true,
            StaysOnField = true,
            MakesChainLink = true
        };

        /// <summary>"Send 1 card from your hand to the GY": the discard-cost pick of any 1 other hand card, as a send.</summary>
        static void SendFromHandCost(EffectClause c)
        {
            c.RequiresDiscardCost = true;
            c.DiscardCostAttribute = "*";
            c.DiscardCostIsSend = true;
        }

        /// <summary>An EFFECT_TYPE_EQUIP / SZONE continuous marker: no Chain Link of its own.</summary>
        static EffectClause Marker(EffectActionKind action) => new EffectClause
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = action,
            StaysOnField = true,
            MakesChainLink = false
        };

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;
    }
}
