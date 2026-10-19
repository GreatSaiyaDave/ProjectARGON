using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CrvCountersAndPhaseTriggers" (CRV / EEN): the old print of the B.E.S. counter cores, a counter-scaled
    /// banish cost, Standby / Draw / End Phase triggers and a Spell-resolution burn. Every atom is one whole sentence (or the
    /// whole text), anchored at both ends, so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="CrvCountersAndPhaseTriggersRuntime"/>.
    /// <list type="bullet">
    /// <item>B.E.S. Crystal Core (c22790789) / B.E.S. Tetran (c44954628): B.E.S. Big Core's counter shapes in the old
    /// wording (<see cref="EffectActionKind.PlaceCounterOnThisCard"/> on a Normal Summon,
    /// <see cref="EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep"/>); Crystal Core's targeted position
    /// change (<see cref="EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense"/>); Tetran's counter-cost Spell/Trap
    /// destruction (<see cref="EffectClause.CounterCostIsNotSpellCounter"/>).</item>
    /// <item>Dark Catapulter (c33875961): a Standby counter in Defense Position and the counter-scaled ignition
    /// (<see cref="EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps"/>).</item>
    /// <item>Ebon Magician Curran (c46128076): a Standby burn per monster the opponent controls.</item>
    /// <item>Cyber Archfiend (c59907935): the pre-draw extra draw and the End Phase self-destruction.</item>
    /// <item>Rapid-Fire Magician (c6337436): 400 damage each time its controller's Normal Spell Card resolves.</item>
    /// </list>
    /// </summary>
    public static class CrvCountersAndPhaseTriggersAtoms
    {
        /// <summary>A sentence start: the text start or after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>A sentence end: "." (or the text end) followed by a space or the text end.</summary>
        const string E = @"(?:\.|$)(?=\s|$)";

        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";

        static int Pos(Match m, int group) =>
            m != null && m.Success && int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // B.E.S. Crystal Core / B.E.S. Tetran e1: SINGLE TRIGGER_F EVENT_SUMMON_SUCCESS (a Normal Summon only);
            // AddCounter(0x1f, 3) while still related. B.E.S. Big Core's clause (SummonTriggersAndTributesAtoms), old wording.
            new AtomRegistration.Atom(
                S + @"When this card is Normal Summoned, put (\d+) counters? on it" + E,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.ThisCardSummoned,
                        Action = EffectActionKind.PlaceCounterOnThisCard,
                        Side = EffectSide.Controller,
                        Amount = Pos(m, 1),
                        RequiresThisNormalSummoned = true,
                        MakesChainLink = true
                    }),

            // B.E.S. Crystal Core / B.E.S. Tetran e3 + e4: SINGLE TRIGGER_F EVENT_DAMAGE_STEP_END (this card battled);
            // counters ~= 0 → remove 1 (face-up and related), counters == 0 → destroy it. The two printed sentences are one
            // effect: B.E.S. Big Core's (BattleRidersRuntime.RemoveCounterOrDestroy), old wording.
            new AtomRegistration.Atom(
                S + @"If this card attacks or is attacked, remove (\d+) counters? from this card at the end of the " +
                @"Damage Step\. If you cannot, destroy it" + E,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.EndOfDamageStep,
                        Action = EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep,
                        Side = EffectSide.Controller,
                        Amount = Pos(m, 1),
                        MakesChainLink = true
                    }),

            // B.E.S. Crystal Core e5: IGNITION, CARD_TARGET, SetCountLimit(1); target 1 face-up Attack Position monster the
            // opponent controls (IsCanChangePosition); operation: still face-up Attack Position and related →
            // ChangePosition(POS_FACEUP_DEFENSE).
            new AtomRegistration.Atom(
                S + @"Once per turn, during your Main Phase, you can change 1 face-up Attack Position monster your " +
                @"opponent controls to face-up Defense Position" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAttackPositionMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    OncePerTurn = true,
                    OptScope = OncePerTurnScope.PerInstance,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // B.E.S. Tetran e5: IGNITION, CARD_TARGET, SetCountLimit(1); cost RemoveCounter(tp,0x1f,1,REASON_COST) (its own
            // counters, not Spell Counters); target 1 Spell/Trap Card on the field (either side); destroy it if related.
            new AtomRegistration.Atom(
                S + @"Once per turn, you can remove (\d+) counters? (?:on|from) this card to destroy 1 Spell or Trap Card " +
                @"on the field" + E,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.FieldSpellTraps,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = 1,
                        RequiresRemoveSpellCounters = Pos(m, 1),
                        CounterCostIsNotSpellCounter = true,
                        OncePerTurn = true,
                        OptScope = OncePerTurnScope.PerInstance,
                        IsOptional = true,
                        MakesChainLink = true
                    }),

            // Dark Catapulter e1: FIELD TRIGGER_F EVENT_PHASE+PHASE_STANDBY, count limit 1; condition: its controller's turn
            // and IsDefensePos (face-up: a face-down card's effects are inactive); AddCounter(0x28, 1) while related.
            new AtomRegistration.Atom(
                S + @"During your Standby Phase, if this card is in Defense Position, put (\d+) counters? on this card" + E,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.PlaceCounterOnThisCard,
                        Side = EffectSide.Controller,
                        Amount = Pos(m, 1),
                        RequiresThisDefensePosition = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    }),

            // Dark Catapulter e2: IGNITION, CARD_TARGET, no count limit. ct = its counters (ct > 0). Cost: banish exactly ct
            // cards from your GY. Target exactly ct Spell/Trap Cards on the field. Operation: destroy them only if every
            // target is still related; then remove all its counters (always).
            new AtomRegistration.Atom(
                S + @"You can remove from play a number of card\(s\) in your Graveyard equal to the number of counters on " +
                @"this card to destroy that many Spell or Trap Card\(s\) on the field\. Then remove all counter\(s\) from " +
                @"this card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishGyCardsPerCounterDestroyThatManySpellTraps,
                    Side = EffectSide.Both,
                    ActivationCondition = CrvCountersAndPhaseTriggersRuntime.CatapulterReady,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Ebon Magician Curran: FIELD TRIGGER_F EVENT_PHASE+PHASE_STANDBY, count limit 1, the turn player only;
            // Damage(1-tp, 300 x GetFieldGroupCount(tp,0,LOCATION_MZONE)) counted at resolution. The whole text: Princess
            // Curran's same sentence follows "This card cannot be Special Summoned except with ..." — a lock the engine does
            // not enforce (PsctGrammar.BlocksSpecialSummon needs the bare sentence) — so she stays a Stub. The shared Just
            // Desserts pass (CardTextEffectCompiler.RxJustDesserts) leaves this whole text alone.
            new AtomRegistration.Atom(
                Begin + @"During your Standby Phase, inflict (\d+) damage to your opponent for each monster they control" +
                Finish,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Pos(m, 1),
                        ScaleAmountByOpponentMonsterCount = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    }),

            // Cyber Archfiend e1: FIELD TRIGGER_O EVENT_PREDRAW; condition ep == tp and an empty hand; target
            // IsPlayerCanDraw(tp,1); Draw(tp,1). A marker: DuelEngine.ContinueDrawPhase offers it before the normal draw.
            new AtomRegistration.Atom(
                S + @"At the beginning of your Draw Phase, if you have no cards in your hand, draw 1 more card in addition " +
                @"to your normal draw" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DrawOneAtDrawPhaseStartIfHandEmpty,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Cyber Archfiend e2: FIELD TRIGGER_F EVENT_PHASE+PHASE_END, count limit 1; condition: the turn player is its
            // controller and holds 1 or more cards; destroy this card if it is related and face-up.
            new AtomRegistration.Atom(
                S + @"During your End Phase, if you have 1 or more card\(s\) in your hand, destroy this card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.DestroyThisAtYourEndPhaseIfHandNotEmpty,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Rapid-Fire Magician: FIELD CONTINUOUS EVENT_CHAIN_SOLVED in the Monster Zone (no Chain Link); re is the
            // activation (EFFECT_TYPE_ACTIVATE) of a Normal Spell Card (GetActiveType()==TYPE_SPELL) and rp == tp →
            // Damage(1-tp, 400).
            new AtomRegistration.Atom(
                Begin + @"Inflict (\d+) damage to your opponent each time you activate a Normal Spell Card" + Finish,
                m => Pos(m, 1) <= 0
                    ? null
                    : new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.InflictDamageToOpponentWhenYourNormalSpellResolves,
                        Side = EffectSide.Opponent,
                        Amount = Pos(m, 1),
                        StaysOnField = true,
                        MakesChainLink = false
                    }),
        };
    }
}
