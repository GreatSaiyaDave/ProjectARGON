namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PositionLocks": battle-position changes with rest-of-turn locks. Aqua Spirit and Garuda the Wind
    /// Spirit keep their summon sentences in the shared templates (the NS/Set lock and the "Special Summoned by
    /// removing from play 1 ATTRIBUTE monster in your Graveyard" procedure); the atoms here are their phase-trigger
    /// sentences, anchored at a sentence start and end. Curse of Fiend's atom is in SimultaneousTriggersAtoms. A longer or reworded
    /// card (Final Attack Orders, Curse of Anubis, a "your End Phase" or "monster you control" variant) stays
    /// uncompiled. Runtime: <see cref="PositionLocksRuntime"/>.
    /// </summary>
    public static class PositionLocksAtoms
    {
        /// <summary>Start of a sentence: the text start, or right after ". ".</summary>
        const string SentenceStart = @"(?<=^|\.\s)";

        /// <summary>End of a sentence: its period, then whitespace or the end of the text.</summary>
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Aqua Spirit (40916023). ygopro: FIELD+TRIGGER_O on EVENT_PHASE+PHASE_STANDBY in the Monster Zone,
            // condition Duel.IsTurnPlayer(1-tp), count limit 1, EFFECT_FLAG_CARD_TARGET: 1 face-up monster the
            // opponent controls; ChangePosition(face-up Defense <-> face-up Attack), then EFFECT_CANNOT_CHANGE_POSITION on
            // it with RESETS_STANDARD_PHASE_END & ~RESET_TURN_SET (it stays locked if turned face-down).
            new AtomRegistration.Atom(
                SentenceStart +
                @"During each of your opponent['’]s Standby Phases, you can change the battle position of 1 of your " +
                @"opponent['’]s face-up Monster Cards\. Once changed, the monster must remain in this position for " +
                @"the rest of the turn" +
                SentenceEnd,
                _ => PhaseChange(EffectTiming.StandbyPhase, lockRestOfTurn: true)),
            // Garuda the Wind Spirit (12800777). ygopro: FIELD+TRIGGER_O on EVENT_PHASE+PHASE_END in the Monster Zone,
            // condition Duel.IsTurnPlayer(1-tp), count limit 1, EFFECT_FLAG_CARD_TARGET: 1 face-up monster the
            // opponent controls that can change its position; ChangePosition. No lock.
            new AtomRegistration.Atom(
                SentenceStart +
                @"During your opponent['’]s End Phase, you can select 1 face-up monster your opponent controls, and " +
                @"change its battle position" +
                SentenceEnd,
                _ => PhaseChange(EffectTiming.EndPhase, lockRestOfTurn: false)),
            // Curse of Fiend (12470447): its atom is SimultaneousTriggersAtoms (v160): the FLIP effects its swap
            // causes go on one Chain through the SEGOC queue (SimultaneousTriggersRuntime).
        };

        /// <summary>
        /// The Aqua Spirit / Garuda clause: an optional trigger in the opponent's <paramref name="timing"/> phase that
        /// targets 1 face-up monster the opponent controls.
        /// </summary>
        static EffectClause PhaseChange(EffectTiming timing, bool lockRestOfTurn) => new EffectClause
        {
            Timing = timing,
            Action = EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase,
            Zone = EffectZoneFilter.OppFaceUpMonsters,
            Side = EffectSide.Opponent,
            OpponentTurnOnly = true,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            IsOptional = true,
            LocksPositionRestOfTurn = lockRestOfTurn,
            MakesChainLink = true
        };
    }
}
