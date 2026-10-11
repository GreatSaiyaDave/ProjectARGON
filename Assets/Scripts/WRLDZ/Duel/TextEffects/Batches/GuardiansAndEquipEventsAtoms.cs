using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// GuardiansAndEquipEvents batch: Guardian riders, equipping an Equip Spell from the GY, and the Equip Spell equip /
    /// leave-field events. Whole sentences only (each pattern runs from a sentence start to its period). The Guardians'
    /// "Cannot be Summoned unless you control a face-up "X"" gate is structural (PsctGrammar.SummonGateNamed).
    /// <list type="bullet">
    /// <item>Guardian Elma (c74367458): e4/e5 SINGLE TRIGGER_O on EVENT_SUMMON_SUCCESS + EVENT_SPSUMMON_SUCCESS (not a Flip
    /// Summon), EFFECT_FLAG_CARD_TARGET: 1 Equip Spell in your GY that CheckEquipTarget(this card), with a free Spell &amp;
    /// Trap Zone; Duel.Equip to this card (<see cref="EffectActionKind.EquipTargetedGyEquipSpellToThis"/>).</item>
    /// <item>Chopman the Desperate Outlaw (c40884383): the same, on EVENT_FLIP_SUMMON_SUCCESS only (it targets in Lua).</item>
    /// <item>Guardian Grarl (c47150851): EFFECT_SPSUMMON_PROC from the hand, s.spcon: a free Monster Zone, the only card in
    /// the hand, and the face-up "Gravity Axe - Grarl" (<see cref="EffectClause.RequiresOnlyCardInHand"/>).</item>
    /// <item>Guardian Baou (c73544866): e5 SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING (the battle target is in the GY by battle):
    /// EFFECT_UPDATE_ATTACK +N, RESETS_STANDARD_DISABLE (Zombyra's <see cref="EffectActionKind.ChangeThisCardAtkWhileFaceUp"/>);
    /// e4 SINGLE+CONTINUOUS EVENT_BATTLED: the battle target, an Effect Monster (IsType(TYPE_EFFECT)) destroyed by that
    /// battle, has its effects negated (the Twin-Headed Wolf / Ultimate Obedient Fiend runtime, card kind "Effect").</item>
    /// <item>Fuhma Shuriken (c9373534): aux.AddEquipProcedure(c, nil, IsSetCard SET_NINJA) — a face-up "Ninja" monster on
    /// either field (<see cref="EffectClause.TargetSeriesName"/>), +N ATK. Its "sent from the field to the GY" burn compiles
    /// through the existing Black Pendant sentence.</item>
    /// <item>Morale Boost (c93671934): FIELD TRIGGER_F EVENT_EQUIP (gain LP) and EVENT_LEAVE_FIELD (damage) markers, run by
    /// <see cref="GuardiansAndEquipEventsRuntime"/>.</item>
    /// </list>
    /// </summary>
    public static class GuardiansAndEquipEventsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.?(?=\s|$)";
        const string Gy = @"(?:GY|Graveyard)";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Guardian Elma.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal or Special Summoned: You can target 1 appropriate Equip Spell Card in your " +
                Gy + @"; equip that target to this card" + End,
                _ => EquipFromGy(normalOrSpecial: true)),

            // Chopman the Desperate Outlaw.
            new AtomRegistration.Atom(
                Start + @"When this card is Flip Summoned, you can equip 1 Equip Spell Card in your " + Gy +
                @" to this card" + End,
                _ => EquipFromGy(normalOrSpecial: false)),

            // Guardian Grarl.
            new AtomRegistration.Atom(
                Start + @"If this is the only card in your hand, you can Special Summon it \(from your hand\)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonThisFromHandByOwnProcedure,
                    FromHand = true,
                    RequiresOnlyCardInHand = true,
                    MakesChainLink = false
                }),

            // Guardian Baou, sentence 2: a permanent ATK gain after it destroys an opponent's monster by battle.
            new AtomRegistration.Atom(
                Start + @"When this card destroys an opponent" + Apos + @"s monster by battle and sends it to the " + Gy +
                @": It gains (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1),
                    MakesChainLink = true
                }),

            // Guardian Baou, sentence 3.
            new AtomRegistration.Atom(
                Start + @"Negate the effects of monsters destroyed by battle with this card" + End,
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

            // Fuhma Shuriken: the series equip limit and its ATK change (two sentences, nothing after "ATK").
            new AtomRegistration.Atom(
                Start + @"Equip only to an? ""([^""]+)"" monster\.\s*It gains (\d+) ATK\.(?=\s|$)",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    TargetSeriesName = m.Groups[1].Value.Trim(),
                    EquipAtkBonus = Parse(m, 2),
                    AiEquipsOwnMonster = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Morale Boost, sentence 1.
            new AtomRegistration.Atom(
                Start + @"Each time a player equips an Equip Spell Card, increase the Life Points of the controller of " +
                @"that Equip Spell Card by (\d+) points" + End,
                m => Marker(EffectActionKind.GainLpWhenEquipSpellEquipped, Parse(m, 1))),

            // Morale Boost, sentence 2.
            new AtomRegistration.Atom(
                Start + @"Each time an Equip Spell Card is removed from the field, the controller of that Equip Spell Card " +
                @"takes (\d+) points of damage" + End,
                m => Marker(EffectActionKind.DamageWhenEquipSpellLeavesField, Parse(m, 1))),
        };

        /// <summary>The summon trigger: target 1 appropriate Equip Spell in your GY and equip it to this card.</summary>
        static EffectClause EquipFromGy(bool normalOrSpecial) => new EffectClause
        {
            Timing = EffectTiming.ThisCardSummoned,
            Action = EffectActionKind.EquipTargetedGyEquipSpellToThis,
            Side = EffectSide.Controller,
            Zone = EffectZoneFilter.ControllerGyEquipSpells,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            TargetCount = 1,
            RequiresThisNormalOrSpecialSummoned = normalOrSpecial,
            RequiresThisFlipSummoned = !normalOrSpecial,
            IsOptional = true,
            CheckedAt = ConditionCheckedAt.Activation,
            MakesChainLink = true
        };

        /// <summary>A FIELD trigger run by the runtime's equip events: no Chain Link of its own clause.</summary>
        static EffectClause Marker(EffectActionKind action, int amount) => new EffectClause
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = action,
            Side = EffectSide.Both,
            Amount = amount,
            StaysOnField = true,
            MakesChainLink = false
        };

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;
    }
}
