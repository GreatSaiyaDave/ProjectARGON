using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LpLocksDelayedBurnAndGyNegation batch (AST): whole-sentence atoms for King of the Swamp's Fusion-substitute wording,
    /// Wall of Revealing Light's paid-LP attack lock, Weapon Change's Standby ATK/DEF switch and Dora of Fate's delayed
    /// Normal Summon burn. The runtime is <see cref="LpLocksDelayedBurnAndGyNegationRuntime"/>.
    /// </summary>
    public static class LpLocksDelayedBurnAndGyNegationAtoms
    {
        const string SentenceStart = @"(?:^|(?<=[.!?)]\s))";
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // King of the Swamp (c79109599, EFFECT_FUSION_SUBSTITUTE): the newer wording of the v180 FusionSubstitutes
            // ability ("You can substitute this card for any 1 Fusion Material Monster ..."). It emits the same clause, read by
            // FusionSubstitutesRuntime.IsFusionSubstitute from the fusion material search (at most one named material).
            new AtomRegistration.Atom(
                SentenceStart + @"This card can be used as a substitute for any 1 Fusion[- ]Material whose name is " +
                @"specifically listed on the Fusion Monster Card, but the other Fusion[- ]Material\(s\) must be correct\." +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.FusionSubstitute,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Wall of Revealing Light (c17078030): the whole text. Activation cost = Duel.AnnounceNumber of a multiple of N
            // up to the LP (paid as the card is activated, LpLocksDelayedBurnAndGyNegationRuntime.PayOnActivation); the
            // field EFFECT_CANNOT_ATTACK on the opponent's Monster Zone (c:GetAttack() <= paid) is read by
            // DuelEngine.ContinuousCannotAttackBlocks (CannotAttackIfAtkAtMostPaidLp). Placement-only Continuous Trap.
            new AtomRegistration.Atom(
                @"^Activate by paying any multiple of (\d+) Life Points\.\s*" +
                @"Monsters your opponent controls cannot attack if their ATK is less than or equal to the amount you paid\.?$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    RequiresLpCostMultiple = int.Parse(m.Groups[1].Value),
                    CannotAttackIfAtkAtMostPaidLp = true,
                    ActivationCondition = LpLocksDelayedBurnAndGyNegationRuntime.PaidLpMultipleAffordable
                }),

            // Weapon Change (c10035717): TRIGGER_O at your Standby Phase (IsTurnPlayer), range SZONE, count limit 1, cost
            // PayLP(N), target 1 face-up monster of the listed Types you control; SET_ATTACK_FINAL = its DEF and
            // SET_DEFENSE_FINAL = its ATK (values at resolution), RESETS_STANDARD_PHASE_END,2. Fired by
            // LpLocksDelayedBurnAndGyNegationRuntime.TryFirePhaseClause; the activation only places the card.
            new AtomRegistration.Atom(
                @"^Once during each of your Standby Phases: You can pay (\d+) Life Points, then target 1 " +
                @"([A-Z][A-Za-z]+(?: or [A-Z][A-Za-z]+)*)-Type monster you control; switch the current ATK and DEF of " +
                @"that target until the end of your opponent's next turn\.?$",
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SwitchTargetCurrentAtkDefUntilOpponentsNextEndPhase,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RaceFilter = Regex.Replace(m.Groups[2].Value, @"\s+or\s+", "|"),
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    PayLpAmount = int.Parse(m.Groups[1].Value),
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Dora of Fate (c67464807): the whole text. Condition IsTurnPlayer(1-tp); target 1 face-up monster the opponent
            // controls with Level > 1 (s.filter; LpLocksDelayedBurnAndGyNegationRuntime.FilterTargets in both target
            // mirrors); resolution registers the player effect (Level L recorded) that inflicts L x N on your next Normal
            // Summon of a Level L-1 monster during your turn (EVENT_SUMMON_SUCCESS, count 1, RESET_PHASE|PHASE_END,2).
            new AtomRegistration.Atom(
                @"^You can only activate this card during your opponent's turn\.\s*" +
                @"Select 1 face-up monster on your opponent's side of the field\.\s*" +
                @"When you Normal Summon a monster that is 1 Level lower than the selected monster during your next turn, " +
                @"inflict damage to your opponent's Life Points equal to the Level of the selected monster x (\d+) points\.?$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BurnWhenYouNormalSummonOneLevelLowerThanTargetNextTurn,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    OpponentTurnOnly = true,
                    MonsterLevelMin = 2,
                    Amount = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),
        };
    }
}
