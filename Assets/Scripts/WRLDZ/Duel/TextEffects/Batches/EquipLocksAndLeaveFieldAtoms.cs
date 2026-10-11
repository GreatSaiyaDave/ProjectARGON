using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipLocksAndLeaveField batch (MFC Equip Spells): each card compiles from its WHOLE printed text (anchored to the
    /// start and the end of the text, EquipRiders pattern), so a longer or different card never compiles part of it.
    /// <list type="bullet">
    /// <item>Demotion (c72575145): aux.AddEquipProcedure (any face-up monster on either field); EFFECT_TYPE_EQUIP +
    /// EFFECT_UPDATE_LEVEL -2 — ContinuousReduceLevel scoped to the equipped monster (AppliesToEquippedHost). The
    /// Level never drops below 1 (<see cref="CardInstance.Level"/>).</item>
    /// <item>Big Bang Shot (c61127349): +400 ATK, piercing (EFFECT_PIERCE, the Fairy Meteor Crush clause), and
    /// "When this card leaves the field, banish the equipped monster" (e5 EVENT_LEAVE_FIELD:
    /// <see cref="EffectClause.BanishHostWhenThisLeaves"/>).</item>
    /// <item>Autonomous Action Unit (c71453557): Premature Burial's revive-and-equip with an LP cost, from the
    /// OPPONENT's GY to your field in face-up Attack Position; "When this card leaves the field" (any reason, Lua e2
    /// EVENT_LEAVE_FIELD) destroys the equipped monster (DestroyHostWhenThisLeaves without
    /// DestroyHostOnlyIfThisDestroyed). Its equip limit is its own monster only (no EquipThisToTarget clause).</item>
    /// <item>Raregold Armor (c7625614): while you control the equipped monster, the opponent's monsters cannot select
    /// any other monster as an attack target (<see cref="EffectActionKind.OpponentCannotAttackOtherThanEquippedHost"/>).</item>
    /// <item>Metalsilver Armor (c33114323): while you control the equipped monster, no other monster can be targeted by
    /// the opponent's card effects (<see cref="EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost"/>).</item>
    /// </list>
    /// Runtime: <see cref="EquipLocksAndLeaveFieldRuntime"/>.
    /// </summary>
    public static class EquipLocksAndLeaveFieldAtoms
    {
        /// <summary>Whole text: nothing before the first sentence.</summary>
        const string Begin = @"^\s*";

        /// <summary>Whole text: the last sentence's period (optional), then only whitespace.</summary>
        const string Finish = @"\.?\s*$";

        /// <summary>A straight or typographic apostrophe.</summary>
        const string Apos = @"['’]";

        /// <summary>"As long as you control the monster equipped with this card, " (Raregold / Metalsilver Armor).</summary>
        const string WhileYouControlHost = @"As long as you control the monster equipped with this card,\s*";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Demotion.
            AtomRegistration.Atom.Sequence(
                Begin + @"Downgrade the monster equipped with this card by (\d+) Levels?" + Finish,
                m => new[]
                {
                    AnyMonsterEquip(),
                    new EffectClause
                    {
                        // EFFECT_TYPE_EQUIP + EFFECT_UPDATE_LEVEL (value -N): the equipped monster only.
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousReduceLevel,
                        Amount = Parse(m, 1, 2),
                        AppliesToEquippedHost = true,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Big Bang Shot.
            AtomRegistration.Atom.Sequence(
                Begin + @"The equipped monster gains (\d+) ATK\.\s*" +
                @"If the equipped monster attacks a Defense Position monster, inflict piercing battle damage to your " +
                @"opponent\.\s*" +
                @"When this card leaves the field, banish the equipped monster" + Finish,
                m =>
                {
                    var equip = AnyMonsterEquip(aiOwn: true);
                    equip.EquipAtkBonus = Parse(m, 1, 400);
                    equip.BanishHostWhenThisLeaves = true;
                    return new[]
                    {
                        equip,
                        new EffectClause
                        {
                            // EFFECT_TYPE_EQUIP + EFFECT_PIERCE (Fairy Meteor Crush's clause; OfficialEffectRegistry.HasPiercing).
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.PiercingBattleDamage,
                            StaysOnField = true,
                            MakesChainLink = false
                        }
                    };
                }),

            // Autonomous Action Unit.
            new AtomRegistration.Atom(
                Begin + @"Pay (\d+) (?:LP|Life Points), then target 1 monster in your opponent" + Apos +
                @"s (?:GY|Graveyard);\s*" +
                @"Special Summon that target to your side of the field in face-up Attack Position, and equip it with " +
                @"this card\.\s*" +
                @"When this card leaves the field, destroy the equipped monster" + Finish,
                m => new EffectClause
                {
                    // Lua e1: Cost.PayLP(N); target 1 monster in the opponent's GY; Special Summon it to your field in
                    // face-up Attack Position, then Duel.Equip (eqlimit: that monster only).
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.OppGyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    PayLpAmount = Parse(m, 1, 1500),
                    SummonInAttack = true,
                    StaysOnField = true,
                    // Lua e2 EVENT_LEAVE_FIELD: any way this card leaves the field destroys its monster.
                    DestroyHostWhenThisLeaves = true,
                    MakesChainLink = true
                }),

            // Raregold Armor.
            AtomRegistration.Atom.Sequence(
                Begin + WhileYouControlHost +
                @"your opponent cannot attack (?:other )?monsters other than the equipped monster" + Finish,
                m => new[]
                {
                    AnyMonsterEquip(aiOwn: true),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.OpponentCannotAttackOtherThanEquippedHost,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Metalsilver Armor.
            AtomRegistration.Atom.Sequence(
                Begin + WhileYouControlHost +
                @"your opponent cannot use the effect of a Spell, Trap,? or Effect Monster that targets 1 monster, " +
                @"except the equipped monster" + Finish,
                m => new[]
                {
                    AnyMonsterEquip(aiOwn: true),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };

        /// <summary>
        /// aux.AddEquipProcedure(c): any face-up monster on either field. <paramref name="aiOwn"/>: the AI equips its own
        /// monster (<see cref="EffectClause.AiEquipsOwnMonster"/>).
        /// </summary>
        static EffectClause AnyMonsterEquip(bool aiOwn = false) => new()
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.EquipThisToTarget,
            Zone = EffectZoneFilter.FieldAnyMonster,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            StaysOnField = true,
            AiEquipsOwnMonster = aiOwn,
            MakesChainLink = true
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
