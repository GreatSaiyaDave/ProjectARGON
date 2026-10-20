using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleBoosts" (v161): battle boosts — piercing (Spear Dragon's old wording, Dragon's Rage's field
    /// grant), an extra attack this turn (Gray Wing), damage-calculation ATK (The Hunter with 7 Weapons, Injection
    /// Fairy Lily) and a monster Quick Effect in the Battle Step (Spirit Ryu). Each atom is one whole sentence (start of
    /// text or after a sentence end, through its final period), so a longer card that only contains part of it stays
    /// uncompiled. Runtime: <see cref="BattleBoostsRuntime"/>.
    /// </summary>
    public static class BattleBoostsAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        /// <summary>A printed Monster Type ("Dragon", "Beast-Warrior", "Winged Beast").</summary>
        const string Type = @"([A-Z][a-z]+(?:[- ][A-Z][a-z]+)?)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Spear Dragon (c31553716) sentence 1: the old wording of piercing (EFFECT_PIERCE on itself).
            new AtomRegistration.Atom(
                S + @"During battle between this attacking card and a Defense Position monster whose DEF is lower " +
                @"than the ATK of this card, inflict the difference as Battle Damage to your opponent" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousPiercing,
                    MakesChainLink = false
                }),
            // Spear Dragon sentence 2: EVENT_DAMAGE_STEP_END continuous effect (no Chain Link, no position lock), only
            // while it is the attacker and still related to the battle; to face-up Defense if in Attack Position.
            new AtomRegistration.Atom(
                S + @"If this card attacks, it is changed to Defense Position at the end of the Damage Step" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndOfDamageStep,
                    Action = EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep,
                    DamageStepOnlyWhenAttacking = true,
                    MakesChainLink = false
                }),
            // Dragon's Rage (c54178050): EFFECT_PIERCE, EFFECT_TYPE_FIELD, LOCATION_MZONE of its controller,
            // target Card.IsRace(RACE_DRAGON) (the live Type).
            new AtomRegistration.Atom(
                S + Type + @"-Type monsters you control inflict piercing Battle Damage when they attack a Defense " +
                @"Position monster" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GrantPiercingToYourRaceMonsters,
                    RaceFilter = m.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Gray Wing (c29618570): an ignition (condition Duel.IsAbleToEnterBP, i.e. your Main Phase 1 of a turn with
            // a Battle Phase; cost: discard 1 card; target check: no extra attack yet) → EFFECT_EXTRA_ATTACK 1 until
            // the End Phase.
            new AtomRegistration.Atom(
                S + @"Discard 1 card from your hand during your Main Phase 1\.\s*" +
                @"This monster can attack twice during the Battle Phase of this turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GrantThisExtraAttackThisTurn,
                    Amount = 1,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    OnlyDuringMainPhase1 = true,
                    ActivationCondition = BattleBoostsRuntime.CanEnterBattlePhaseWithoutExtraAttack,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // The Hunter with 7 Weapons (c1525329): a mandatory trigger on its Normal Summon announces a Type
            // (Duel.AnnounceRace, not a target); at resolution, while face-up, it gets a mandatory
            // EVENT_PRE_DAMAGE_CALCULATE trigger: battling a monster of that Type, +1000 ATK until the end of that
            // damage calculation (attacking or attacked).
            AtomRegistration.Atom.Sequence(
                S + @"When this card is Normal Summoned, declare 1 Type of monster\.\s*" +
                @"When battling that Type of monster, increase the ATK of this card by (\d+) points during Damage " +
                @"Calculation" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ThisCardSummoned,
                        Action = EffectActionKind.DeclareTypeForThisCardBattles,
                        Zone = EffectZoneFilter.DeclaredMonsterTypes,
                        Side = EffectSide.Both,
                        RequiresTargetChoice = true,
                        ChoiceDoesNotTarget = true,
                        RequiresThisNormalSummoned = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType,
                        Amount = Parse(m, 1, 1000),
                        MakesChainLink = false
                    }
                }),
            // Injection Fairy Lily (c79575620): EFFECT_TYPE_QUICK_O at EVENT_PRE_DAMAGE_CALCULATE, condition attacker or
            // attack target, cost pay 2000 LP (flag: once per battle), +3000 ATK reset at the end of damage calculation.
            new AtomRegistration.Atom(
                S + @"If this card attacks or is attacked, during damage calculation \(in either player's turn\): " +
                @"You can pay (\d+) LP once per battle; this card gains (\d+) ATK during that damage calculation only" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.MonsterQuickEffect,
                    Action = EffectActionKind.GainAtkThisDamageCalculation,
                    PayLpAmount = Parse(m, 1, 2000),
                    Amount = Parse(m, 2, 3000),
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 79853073: If this card battles a Warrior-Type monster, during damage calculation:
            // This card gains 2000 ATK and DEF during that damage calculation only.
            new AtomRegistration.Atom(
                S + @"If this card battles a Warrior-Type monster, during damage calculation:" +
                @" This card gains (\d+) ATK and DEF during that damage calculation only" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainAtkDefThisDamageCalculationVsRace,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2000),
                    DefAmount = Parse(m, 1, 2000),
                    RaceFilter = "Warrior",
                    MakesChainLink = false
                }),
            // Spirit Ryu (c67957315): EFFECT_TYPE_QUICK_O, condition: turn player, Battle Phase, this card is the
            // attacker or the attack target; cost: discard 1 Dragon; +1000 ATK and DEF until the end of the Battle Phase.
            new AtomRegistration.Atom(
                S + @"During your Battle Step, if this card battles \(Quick Effect\): You can discard 1 " + Type +
                @" monster to the GY; this card gains (\d+) ATK/DEF until the end of the Battle Phase" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.MonsterQuickEffect,
                    Action = EffectActionKind.GainAtkDefUntilEndOfBattlePhase,
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostRace = m.Groups[1].Value,
                    Amount = Parse(m, 2, 1000),
                    DefAmount = Parse(m, 2, 1000),
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : fallback;
    }
}
