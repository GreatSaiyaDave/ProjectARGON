using System;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Entry point for card-batch regressions (Tools/CardCompile/README.md). Each batch keeps its checks in
    /// its own file, Rules/Batches/&lt;Batch&gt;RegressionTests.cs, with a
    /// <c>public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action&lt;string, bool, string&gt; check)</c>
    /// and adds exactly one call line below, so parallel batches never edit the same lines. Runs inside
    /// <see cref="InteractionRegressionTests.RunAll"/>, so every runner (Editor menu, batch mode, the headless
    /// harness) covers it. Shared mini-duel helpers: InteractionRegressionTests.Fresh / ClearBoard /
    /// PlaceMonster / PlaceSetTrap / PutInHand / DrainCombat / ReachOpponentMain / ReachOpponentBattle /
    /// SentenceAtomMisses.
    /// </summary>
    public static class CardBatchRegressionTests
    {
        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> check)
        {
            // Batch test calls — one line per batch, append below.
            PerCountBurnsRegressionTests.Run(db, pDeck, aDeck, check);
            SummonNegationRegressionTests.Run(db, pDeck, aDeck, check);
            ContinuousNegationRegressionTests.Run(db, pDeck, aDeck, check);
            TurnNegationTrapsRegressionTests.Run(db, pDeck, aDeck, check);
            TakeControlRegressionTests.Run(db, pDeck, aDeck, check);
            TributeSummonsRegressionTests.Run(db, pDeck, aDeck, check);
            V141CrossGroupRegressionTests.Run(db, pDeck, aDeck, check);
            FuzzAuditRegressionTests.Run(db, pDeck, aDeck, check);
            StandbyPauseRegressionTests.Run(db, pDeck, aDeck, check);
            EmptyHandStandbyDrawRegressionTests.Run(db, pDeck, aDeck, check);
            DiscardTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            BattlePhaseEndRegressionTests.Run(db, pDeck, aDeck, check);
            SpellTrapDestroyedResponseRegressionTests.Run(db, pDeck, aDeck, check);
            TypeDeclarationRegressionTests.Run(db, pDeck, aDeck, check);
            LocksAndZonesRegressionTests.Run(db, pDeck, aDeck, check);
            HandDisruptionRegressionTests.Run(db, pDeck, aDeck, check);
            BanishedReturnRegressionTests.Run(db, pDeck, aDeck, check);
            FlipMassEffectsRegressionTests.Run(db, pDeck, aDeck, check);
            RedirectAndReplaceRegressionTests.Run(db, pDeck, aDeck, check);
            StandbyGrowthAndMaintenanceRegressionTests.Run(db, pDeck, aDeck, check);
            SpiritsAndMiscRegressionTests.Run(db, pDeck, aDeck, check);
            CrossGroupV142RegressionTests.Run(db, pDeck, aDeck, check);
            LpCostOnlyActivationRegressionTests.Run(db, pDeck, aDeck, check);
            PartialCompilesRegressionTests.Run(db, pDeck, aDeck, check);
            CompiledActivationChainRegressionTests.Run(db, pDeck, aDeck, check);
            ActivationLpCostRegressionTests.Run(db, pDeck, aDeck, check);
            TargetCountRegressionTests.Run(db, pDeck, aDeck, check);
            DiscardCostPickRegressionTests.Run(db, pDeck, aDeck, check);
            PsctConditionGateRegressionTests.Run(db, pDeck, aDeck, check);
            DrawThenDiscardRegressionTests.Run(db, pDeck, aDeck, check);
            UnparsedTriggerFallbacksRegressionTests.Run(db, pDeck, aDeck, check);
            UnparsedTriggerDrawRegressionTests.Run(db, pDeck, aDeck, check);
            SpiritReturnPsctRegressionTests.Run(db, pDeck, aDeck, check);
            FlipDefCheckRegressionTests.Run(db, pDeck, aDeck, check);
            ToonsRegressionTests.Run(db, pDeck, aDeck, check);
            ResponseDiscardCostRegressionTests.Run(db, pDeck, aDeck, check);
            WholeResolutionRegressionTests.Run(db, pDeck, aDeck, check);
            OppMonsterTargetsRegressionTests.Run(db, pDeck, aDeck, check);
            OppFaceDownSiblingsRegressionTests.Run(db, pDeck, aDeck, check);
            EquipSpellCostRegressionTests.Run(db, pDeck, aDeck, check);
            GrokTargetedHardeningRegressionTests.Run(db, pDeck, aDeck, check);
            AttackObligationsRegressionTests.Run(db, pDeck, aDeck, check);
            GrokSpiritsLocksHardeningRegressionTests.Run(db, pDeck, aDeck, check);
            GrokEquipStandbyHardeningRegressionTests.Run(db, pDeck, aDeck, check);
            GrokPoisonMeteorRockHardeningRegressionTests.Run(db, pDeck, aDeck, check);
            EquipFamilyRegressionTests.Run(db, pDeck, aDeck, check);
            FlipCostsAndFusionRegressionTests.Run(db, pDeck, aDeck, check);
            BattleWindowsRegressionTests.Run(db, pDeck, aDeck, check);
            RuleLocksRegressionTests.Run(db, pDeck, aDeck, check);
            DestinyBoardRegressionTests.Run(db, pDeck, aDeck, check);
            NegationsAndGySummonsRegressionTests.Run(db, pDeck, aDeck, check);
            EquipModelRegressionTests.Run(db, pDeck, aDeck, check);
            FaceUpQuickEffectsRegressionTests.Run(db, pDeck, aDeck, check);
            SimultaneousTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            SpellCounterRegressionTests.Run(db, check);
            SpellCounterDuelRegressionTests.Run(db, pDeck, aDeck, check);
            EngineHygieneRegressionTests.Run(db, pDeck, aDeck, check);
            SpiritMonstersRegressionTests.Run(db, pDeck, aDeck, check);
            SimpleSpellTrapsRegressionTests.Run(db, pDeck, aDeck, check);
            BothSideAndMonsterEquipRegressionTests.Run(db, pDeck, aDeck, check);
            CoinDiceInterruptsRegressionTests.Run(db, pDeck, aDeck, check);
            SpecialSummonEffectsRegressionTests.Run(db, pDeck, aDeck, check);
            ContinuousTrapsAndSpiritSupportRegressionTests.Run(db, pDeck, aDeck, check);
            BattleBoostsRegressionTests.Run(db, pDeck, aDeck, check);
            EquipAndSpellProtectionRegressionTests.Run(db, pDeck, aDeck, check);
            DeckReturnsAndMassEffectsRegressionTests.Run(db, pDeck, aDeck, check);
            BattleRulesRegressionTests.Run(db, pDeck, aDeck, check);
            NegationMonstersRegressionTests.Run(db, pDeck, aDeck, check);
            EffectDamageEventsRegressionTests.Run(db, pDeck, aDeck, check);
            PgdSpellTrapBasicsRegressionTests.Run(db, pDeck, aDeck, check);
            BattleOutcomeTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            FlipDownAndPeekRegressionTests.Run(db, pDeck, aDeck, check);
            StandbyBurnsAndDiceRegressionTests.Run(db, pDeck, aDeck, check);
            BattleDestroyedGyRecoveryRegressionTests.Run(db, pDeck, aDeck, check);
            ContinuousLocksAndIgnitionsRegressionTests.Run(db, pDeck, aDeck, check);
            PhaseTimersAndDeckTopRegressionTests.Run(db, pDeck, aDeck, check);
            CallsDiceAndSetTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            TargetNegationsAndHandQuickEffectsRegressionTests.Run(db, pDeck, aDeck, check);
            SummonTriggersAndTributesRegressionTests.Run(db, pDeck, aDeck, check);
            EngineResidualsRegressionTests.Run(db, pDeck, aDeck, check);
            LastTurnRegressionTests.Run(db, pDeck, aDeck, check);
            SummonPromptsAndCostsRegressionTests.Run(db, pDeck, aDeck, check);
            ForcedActivationAndSubstitutionRegressionTests.Run(db, pDeck, aDeck, check);
            TriggerChainResponsesRegressionTests.Run(db, pDeck, aDeck, check);
            PerCopyDeckRegressionTests.Run(db, pDeck, aDeck, check);
            UnionEquipRegressionTests.Run(db, pDeck, aDeck, check);
            MfcSimpleSpellsRegressionTests.Run(db, pDeck, aDeck, check);
            MfcSimpleSpellsBRegressionTests.Run(db, pDeck, aDeck, check);
            LpCachePoisoningRegressionTests.Run(db, pDeck, aDeck, check);
            SimpleTargetUtilityRegressionTests.Run(db, pDeck, aDeck, check);
            DamageStepRulesRegressionTests.Run(db, pDeck, aDeck, check);
            EquipLocksAndLeaveFieldRegressionTests.Run(db, pDeck, aDeck, check);
            DcrEquipSpellsRegressionTests.Run(db, pDeck, aDeck, check);
            SpellCounterSpendersRegressionTests.Run(db, pDeck, aDeck, check);
            ChainLinkAnswersRegressionTests.Run(db, pDeck, aDeck, check);
            FlipMassAndOpponentPicksRegressionTests.Run(db, pDeck, aDeck, check);
            PhaseTriggersAndIgnitionCostsRegressionTests.Run(db, pDeck, aDeck, check);
            UnionRidersRegressionTests.Run(db, pDeck, aDeck, check);
            SummonLocksAndStatRulesRegressionTests.Run(db, pDeck, aDeck, check);
            SimpleTriggersAndSwarmRegressionTests.Run(db, pDeck, aDeck, check);
            DcrSpellTrapUtilityRegressionTests.Run(db, pDeck, aDeck, check);
            BattleDestroyedTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            GuardiansAndEquipEventsRegressionTests.Run(db, pDeck, aDeck, check);
            DerivedAtkMonstersRegressionTests.Run(db, pDeck, aDeck, check);
            NamedSummonPairsRegressionTests.Run(db, pDeck, aDeck, check);
            ArchfiendRidersRegressionTests.Run(db, pDeck, aDeck, check);
            SpellTrapAnswersAndActivationLocksRegressionTests.Run(db, pDeck, aDeck, check);
            MonsterNegationAndControlLocksRegressionTests.Run(db, pDeck, aDeck, check);
            ConditionalSpellsAndTrapsRegressionTests.Run(db, pDeck, aDeck, check);
            EndOfDamageStepRidersRegressionTests.Run(db, pDeck, aDeck, check);
            BattleDamageEventsRegressionTests.Run(db, pDeck, aDeck, check);
            SpellEventsAndHandSendTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            RecoveryAndPhaseCostsRegressionTests.Run(db, pDeck, aDeck, check);
            TokensDeclarationsAndPicksRegressionTests.Run(db, pDeck, aDeck, check);
            ChaosBanishSummonsRegressionTests.Run(db, pDeck, aDeck, check);
            GyHandAndSummonEventTriggersRegressionTests.Run(db, pDeck, aDeck, check);
            TargetingTruthRegressionTests.Run(db, pDeck, aDeck, check);
            BattleAndPositionRecordsRegressionTests.Run(db, pDeck, aDeck, check);
            IgnitionCostsAndSimpleSpellsRegressionTests.Run(db, pDeck, aDeck, check);
            AttackShieldsAndDamageZeroRegressionTests.Run(db, pDeck, aDeck, check);
            SanctuaryAndZeraRegressionTests.Run(db, pDeck, aDeck, check);
            LvChainRegressionTests.Run(db, pDeck, aDeck, check);
            ElementMonstersRegressionTests.Run(db, pDeck, aDeck, check);
            FusionSubstitutesRegressionTests.Run(db, pDeck, aDeck, check);
            SodCompletionRegressionTests.Run(db, pDeck, aDeck, check);
            RdsRegressionTests.Run(db, pDeck, aDeck, check);
        }
    }
}
