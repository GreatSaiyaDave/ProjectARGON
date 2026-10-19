namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvIgnitionsAndUnions" (CRV): monster ignitions with a discard cost or a peek, written as whole-sentence
    /// atoms (the generic IgnitionTemplates parser drops costs and riders), and the riders of the CRV-print Union procedure.
    /// <list type="bullet">
    /// <item>D.D.M. - Different Dimension Master (c82112775): IGNITION, CARD_TARGET, SetCountLimit(1); cost discard 1 Spell;
    /// target 1 face-up monster in your banished pile that can be Special Summoned (free Monster Zone); Special Summon it
    /// face-up (Dimension Distortion's <see cref="EffectActionKind.SpecialSummonBanishedTarget"/>).</item>
    /// <item>Patroid (c71930383): IGNITION, CARD_TARGET, SetCountLimit(1); target 1 face-down card the opponent controls;
    /// ConfirmCards (A Man with Wdjat's <see cref="EffectActionKind.LookAtTargetSetCard"/>).</item>
    /// <item>Protective Soul Ailin (c11678191): the CRV wording of Second Goblin's Union-state ignition (IGNITION from the
    /// S/T Zone, SetCountLimit(1), aux.IsUnionState; ChangePosition of the equipped monster).</item>
    /// </list>
    /// The CRV Union procedure itself ("equip this card to a "X" you control, OR unequip it to Special Summon this card in
    /// face-up Attack Position", aux.AddUnionProcedure(c, f, true)) and Doitsu's "that monster gains 2500 ATK" are read by
    /// AdvancedEffectTemplates (RxUnionCrv, RxUnionBonusGains), the same Union shell as the classic print.
    /// Cybernetic Magician (c59023523, SINGLE EFFECT_SET_ATTACK_FINAL until the End Phase) is deferred: the engine has no
    /// timestamped "set final ATK" layer, and the until-end-of-turn delta model gives the wrong ATK when earlier modifiers sum
    /// below 0 or end later this turn.
    /// </summary>
    public static class CrvIgnitionsAndUnionsAtoms
    {
        /// <summary>A sentence start: the text start or after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>A sentence end: "." (or the text end) followed by a space or the text end.</summary>
        const string E = @"(?:\.|$)(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // D.D.M. - Different Dimension Master (c82112775): IGNITION, CARD_TARGET, SetCountLimit(1); cost DiscardHand 1 Spell
            // (REASON_COST|REASON_DISCARD); target s.filter (face-up, IsCanBeSpecialSummoned) in (REMOVED, 0) with
            // GetLocationCount(MZONE) > 0; SpecialSummon(tc, 0, tp, tp, false, false, POS_FACEUP) if it still relates.
            new AtomRegistration.Atom(
                S + @"Once per turn, you can discard 1 Spell Card to Special Summon 1 of your removed from play monsters" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonBanishedTarget,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerBanishedFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = FaceUpQuickEffectsRuntime.DiscardSpellCard,
                    DiscardCostCount = 1,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerInstance,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Patroid (c71930383): IGNITION, CARD_TARGET, SetCountLimit(1); target Card.IsFacedown in (0, ONFIELD) — a Set
            // monster or Spell/Trap the opponent controls; operation: still related and face-down → ConfirmCards(tp, tc).
            new AtomRegistration.Atom(
                S + @"Once per turn, during your Main Phase, you can look at 1 face-down card your opponent controls, then " +
                @"return it to its original position" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LookAtTargetSetCard,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAnyCardOnField,
                    RequiresTargetFaceDown = true,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerInstance,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Protective Soul Ailin (c11678191) e1: IGNITION, range SZONE, SetCountLimit(1), condition aux.IsUnionState;
            // ChangePosition(equip target, POS_FACEUP_DEFENSE, 0, POS_FACEUP_ATTACK, 0) — Second Goblin's rider, CRV wording.
            new AtomRegistration.Atom(
                S + @"When equipped to a monster by this card's effect, you can change that monster's battle position once " +
                @"per turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.UnionStateIgnition,
                    Action = EffectActionKind.ChangeEquippedMonsterBattlePosition,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };
    }
}
