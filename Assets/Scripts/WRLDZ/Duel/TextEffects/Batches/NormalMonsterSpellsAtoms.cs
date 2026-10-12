using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "NormalMonsterSpells": Ancient Sanctuary Spells/Traps built around Normal Monsters, and two Equip Spells
    /// with an equip limit. Every atom owns the card's WHOLE printed text (anchored ^…$), so a longer or different card
    /// never compiles part of it here and no "You can only activate / equip …" sentence is dropped.
    /// "Normal Monster" is ygopro TYPE_NORMAL (a Token is TYPE_NORMAL too); Tokens are left out only where the Lua filter
    /// leaves them out. Runtime: <see cref="NormalMonsterSpellsRuntime"/>.
    /// <list type="bullet">
    /// <item>Delta Attacker (c39719977): condition — 3 face-up non-Token Normal Monsters of one name you control; each
    /// face-up non-Token Normal Monster you control with 2 others of its name can attack directly this turn.</item>
    /// <item>Thousand Energy (c5703682) / Triangle Power (c32298781): face-up non-Token Normal Monsters of exactly Level 2
    /// (1) you control get original ATK/DEF + 1000 (2000) while they stay face-up on the field; in this turn's End Phase
    /// every face-up Level 2 (1) Normal Monster you control (Tokens and later arrivals too) is destroyed.</item>
    /// <item>The Law of the Normal (c66926224): condition — 5 face-up Level 2 or lower Normal Monsters you control,
    /// another card in your hand, a card in the opponent's hand and something to destroy; both hands are discarded, then
    /// every card on the field except face-up Level 2 or lower Normal Monsters is destroyed.</item>
    /// <item>Order to Smash (c39019325): Normal Trap; target 1 face-up Level 2 or lower non-Token Normal Monster you
    /// control (activation needs a Spell/Trap the opponent controls); Tribute it, and if you do, destroy 1 or 2
    /// Spells/Traps the opponent controls (chosen at resolution, not targeted).</item>
    /// <item>Sword of the Soul-Eater (c5371656): equip only to a Level 3 or lower Normal Monster you control (the equip
    /// limit itself has no controller rule: aux.AddEquipProcedure p = 0 checks the activation only); activating it
    /// Tributes every other non-Token Normal Monster you control (cost); +1000 ATK per monster Tributed.</item>
    /// <item>Opti-Camouflage Armor (c44762290): equip only to a Level 1 monster on either field (p = nil); the equipped
    /// monster can attack directly (EFFECT_TYPE_EQUIP EFFECT_DIRECT_ATTACK).</item>
    /// </list>
    /// </summary>
    public static class NormalMonsterSpellsAtoms
    {
        const string S = @"^\s*";
        const string E = @"\.?\s*$";
        const string Apos = @"['’]";
        const string YourSide = @"on your side of the field";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Delta Attacker: Lua s.target chk — 3 face-up non-Token Normal Monsters, each with 2 others of its code;
            // s.activate grants EFFECT_DIRECT_ATTACK (RESETS_STANDARD_PHASE_END) to every such monster.
            new AtomRegistration.Atom(
                S + @"You can only activate this card while there are (\d+) Normal Monsters of the same name " +
                @"\(except Tokens\) " + YourSide + @"\.\s*The \1 Normal Monsters of the same name can attack your " +
                @"opponent" + Apos + @"s Life Points directly during the turn this card is activated" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.NormalsSharingNameAttackDirectlyThisTurn,
                    Side = EffectSide.Controller,
                    RequiresNormalType = true,
                    ActivationCondition = NormalMonsterSpellsRuntime.NormalsSharingNameFaceUp,
                    ConditionAmount = Parse(m, 1, 3),
                    MakesChainLink = true
                }),

            // Thousand Energy / Triangle Power: s.filter (face-up, TYPE_NORMAL, not TYPE_TOKEN, GetLevel()==N) for the
            // activation chk and the boost; the delayed End Phase effect's s.dfilter has no Token exclusion.
            new AtomRegistration.Atom(
                S + @"Increase the original ATK and DEF of all face-up Level (\d+) Normal Monsters \(except Tokens\) " +
                YourSide + @" by (\d+) points until the End Phase\.\s*Destroy all Level \1 Normal Monsters " + YourSide +
                @" during the End Phase" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RaiseOriginalAtkDefOfLevelNormalsDestroyThemAtEndPhase,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2, 0),
                    MonsterLevelMin = Parse(m, 1, 0),
                    MonsterLevelMax = Parse(m, 1, 0),
                    RequiresNormalType = true,
                    ActivationCondition = NormalMonsterSpellsRuntime.FaceUpNonTokenNormalOfLevel,
                    ConditionAmount = 1,
                    MakesChainLink = true
                }),

            // The Law of the Normal: s.condition (5 face-up TYPE_NORMAL Level <= 2) + s.target chk (another card in your
            // hand, a card in the opponent's hand, a card on the field to destroy); s.activate discards both hands, then
            // destroys every card on the field that is not a face-up TYPE_NORMAL Level <= 2 monster.
            new AtomRegistration.Atom(
                S + @"You can only activate this card while there are (\d+) face-up Level (\d+) or lower Normal " +
                @"Monsters " + YourSide + @"\.\s*Both players discard all cards in their hands, and destroy all cards " +
                @"on the field except Level \2 or lower Normal Monsters" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothDiscardHandsDestroyAllButLowLevelNormals,
                    Side = EffectSide.Both,
                    MonsterLevelMax = Parse(m, 2, 2),
                    RequiresNormalType = true,
                    ActivationCondition = NormalMonsterSpellsRuntime.LowLevelNormalsAndCardsToDiscardAndDestroy,
                    ConditionAmount = Parse(m, 1, 5),
                    MakesChainLink = true
                }),

            // Order to Smash: Normal Trap, EFFECT_FLAG_CARD_TARGET; s.rfilter (TYPE_NORMAL, not TYPE_TOKEN, face-up,
            // Level <= 2, releasable by effect) on your field; chk also needs a Spell/Trap the opponent controls; s.activate
            // Release(tc, REASON_EFFECT) > 0, then SelectMatchingCard(1, 2) of the opponent's Spells/Traps (no target).
            new AtomRegistration.Atom(
                S + @"Target 1 Level (\d+) or lower non-Token Normal Monster you control; Tribute that Normal Monster, " +
                @"and if you do, destroy up to (\d+) Spells/Traps your opponent controls" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TributeTargetedNormalThenDestroyUpToOppSpellTraps,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MonsterLevelMax = Parse(m, 1, 2),
                    RequiresNormalType = true,
                    TributeChosenTarget = true,
                    PickUpTo = Parse(m, 2, 2),
                    ActivationCondition = NormalMonsterSpellsRuntime.OpponentControlsSpellTrap,
                    MakesChainLink = true
                }),

            // Sword of the Soul-Eater: aux.AddEquipProcedure(c, 0, s.filter) — the target is a face-up TYPE_NORMAL monster
            // of Level <= 3 you control; the equip limit is s.filter alone.
            new AtomRegistration.Atom(
                S + @"You can only equip this card to a Level (\d+) or lower Normal Monster " + YourSide + @"\.\s*" +
                @"When you activate this card, Tribute all Normal Monsters \(except Tokens\) " + YourSide +
                @", except the equipped monster\.\s*Increase the ATK of the equipped monster by (\d+) points for each " +
                @"Normal Monster that you Tributed" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MonsterLevelMax = Parse(m, 1, 3),
                    RequiresNormalType = true,
                    EquipAtkPerTributedOnActivation = Parse(m, 2, 1000),
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Opti-Camouflage Armor: aux.AddEquipProcedure(c, nil, GetLevel()==1) — either field; EFFECT_TYPE_EQUIP
            // EFFECT_DIRECT_ATTACK (the DcrEquipSpells marker).
            AtomRegistration.Atom.Sequence(
                S + @"You can only equip this card to a Level (\d+) monster\.\s*A monster equipped with this card can " +
                @"attack your opponent" + Apos + @"s Life Points directly" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Side = EffectSide.Both,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        MonsterLevelMin = Parse(m, 1, 1),
                        MonsterLevelMax = Parse(m, 1, 1),
                        AiEquipsOwnMonster = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedCanAttackDirectly,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };
    }
}
