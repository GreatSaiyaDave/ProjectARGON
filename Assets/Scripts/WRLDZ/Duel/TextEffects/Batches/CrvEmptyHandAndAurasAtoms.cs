using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvEmptyHandAndAuras" (CRV / EEN): continuous self and field stats, empty-hand conditions and
    /// immunities. Whole sentences only, never a card-name list. Runtime: <see cref="CrvEmptyHandAndAurasRuntime"/>.
    /// <list type="bullet">
    /// <item>Cybernetic Cyclopean (c96428622): SINGLE UPDATE_ATTACK +N while its controller's hand is empty
    /// (ContinuousGainAtkDef + AffectsThisCardOnly + WhileCondition <see cref="CrvEmptyHandAndAurasRuntime.NoCardsInHand"/>,
    /// FieldSpellEffects.ApplyStatClause).</item>
    /// <item>Reborn Zombie (c23421244): SINGLE INDESTRUCTABLE_BATTLE while IsAttackPos and the hand is empty (The Unhappy
    /// Girl's <see cref="EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition"/> + the WhileCondition,
    /// BattleAndPositionRecordsRuntime.BattleIndestructible).</item>
    /// <item>Mechanical Hound (c22512237): FIELD CANNOT_ACTIVATE on the opponent, Spell Card activations only, while the
    /// hand is empty (<see cref="EffectActionKind.ContinuousOpponentCannotActivateSpellCards"/>).</item>
    /// <item>Elemental HERO Wildheart (c86188410): SINGLE IMMUNE_EFFECT te:IsTrapEffect() — every Trap, its controller's
    /// too (UnaffectedByCardEffects "Trap", ContinuousProtections.IsUnaffectedBy).</item>
    /// <item>Gyroid (c18325492): SINGLE INDESTRUCTABLE_COUNT, count limit 1, REASON_BATTLE
    /// (<see cref="EffectActionKind.CannotBeDestroyedByBattleOncePerTurn"/>).</item>
    /// <item>Tyranno Infinity (c83235263): SET_BASE_ATTACK = your face-up banished monsters of the Type x N (Lost
    /// Guardian's count source with ATK instead of DEF; printed ATK "?").</item>
    /// <item>Batteryman C (c19733961): FIELD UPDATE_ATTACK / UPDATE_DEFENSE on the monsters of the Type you control, N x the
    /// face-up copies you control, each live only while every face-up copy is in that battle position (each copy is its
    /// own aura).</item>
    /// </list>
    /// </summary>
    public static class CrvEmptyHandAndAurasAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis or quote.</summary>
        const string Start = @"(?:^|(?<=[.!?)""]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        const string Q = @"[""“”]";
        const string Name = Q + @"([^""“”]+)" + Q;

        const string Race =
            @"(Aqua|Beast-Warrior|Winged Beast|Beast|Creator God|Cyberse|Dinosaur|Divine-Beast|Dragon|Fairy|Fiend|Fish|" +
            @"Illusion|Insect|Machine|Plant|Psychic|Pyro|Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)";

        const string NoHand = @"While you have no cards in your hand";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cybernetic Cyclopean: +N ATK on itself while its controller's hand is empty.
            new AtomRegistration.Atom(
                Start + NoHand + @", this card gains (\d+) ATK" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1),
                    DefAmount = 0,
                    AffectsThisCardOnly = true,
                    WhileCondition = CrvEmptyHandAndAurasRuntime.NoCardsInHand,
                    MakesChainLink = false
                }),

            // Reborn Zombie: not destroyed by battle while in (face-up) Attack Position and the hand is empty.
            new AtomRegistration.Atom(
                Start + NoHand + @" and this card is in Attack Position, it cannot be destroyed by battle" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition,
                    Side = EffectSide.Controller,
                    WhileCondition = CrvEmptyHandAndAurasRuntime.NoCardsInHand,
                    MakesChainLink = false
                }),

            // Mechanical Hound: the opponent cannot activate Spell Cards while the hand is empty.
            new AtomRegistration.Atom(
                Start + NoHand + @", your opponent cannot activate Spell Cards" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousOpponentCannotActivateSpellCards,
                    Side = EffectSide.Opponent,
                    WhileCondition = CrvEmptyHandAndAurasRuntime.NoCardsInHand,
                    MakesChainLink = false
                }),

            // Elemental HERO Wildheart: unaffected by every Trap effect (its controller's too).
            new AtomRegistration.Atom(
                Start + @"This card is unaffected by Trap effects" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.UnaffectedByCardEffects,
                    UnaffectedByFilter = "Trap",
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // — 41589166 Tenkabito Shien: This card is unaffected by the effects of Trap Cards.
            new AtomRegistration.Atom(
                Start + @"This card is unaffected by the effects of Trap Cards" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.UnaffectedByCardEffects,
                    UnaffectedByFilter = "Trap",
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Gyroid: the first battle destruction each turn does not happen.
            new AtomRegistration.Atom(
                Start + @"Once per turn, this card cannot be destroyed by battle" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeDestroyedByBattleOncePerTurn,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    MakesChainLink = false
                }),

            // Tyranno Infinity: original ATK = your face-up banished monsters of the Type x N (DEF untouched).
            new AtomRegistration.Atom(
                Start + @"The original ATK of this card is the number of your banished " + Race + @"(?:-Type)? monsters " +
                @"(?:×|x) (\d+)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkPerCount,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 2),
                    DefAmount = 0,
                    RaceFilter = m.Groups[1].Value,
                    CountSource = TlmDerivedStatsAndProceduresRuntime.YourFaceUpBanishedRace,
                    SelfStatIsSet = true,
                    MakesChainLink = false
                }),

            // Batteryman C: one sentence per stat (an atom matches once per text) — the Type you control gains N ATK for
            // each face-up copy while all are in Attack Position, N DEF while all are in Defense Position.
            new AtomRegistration.Atom(
                Start + @"If all " + Name + @"\(s\) you control are in Attack Position, all " + Race + @"(?:-Type)? " +
                @"monsters you control gain (\d+) ATK for each " + Q + @"\1" + Q + @" you control" + End,
                m => PositionAura(m, attack: true)),
            new AtomRegistration.Atom(
                Start + @"If all " + Name + @"\(s\) you control are in Defense Position, all " + Race + @"(?:-Type)? " +
                @"monsters you control gain (\d+) DEF for each " + Q + @"\1" + Q + @" you control" + End,
                m => PositionAura(m, attack: false)),
        };

        static EffectClause PositionAura(Match m, bool attack)
        {
            var n = Parse(m, 3);
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Side = EffectSide.Controller,
                RaceFilter = m.Groups[2].Value,
                Amount = attack ? n : 0,
                DefAmount = attack ? 0 : n,
                // The counted copies' name (not a filter on the boosted monsters).
                ConditionName = m.Groups[1].Value.Trim(),
                CountSource = attack
                    ? CrvEmptyHandAndAurasRuntime.FaceUpNamedYouControlAllInAttack
                    : CrvEmptyHandAndAurasRuntime.FaceUpNamedYouControlAllInDefense,
                MakesChainLink = false
            };
        }

        static int Parse(Match m, int group) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n) ? n : 0;
    }
}
