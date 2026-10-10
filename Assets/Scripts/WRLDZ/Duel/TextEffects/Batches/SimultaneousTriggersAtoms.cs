namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimultaneousTriggers" (v160): Curse of Fiend, whose mass position change flips face-down monsters
    /// of both players face-up at once; their FLIP effects go on one Chain through the SEGOC queue
    /// (<see cref="SimultaneousTriggersRuntime"/>). The change and the rest-of-turn lock are the PositionLocks runtime
    /// (<see cref="PositionLocksRuntime.ChangeAllPositionsAndLock"/>, <see cref="PositionLocksRuntime.SetInYourStandbyPhase"/>).
    /// The atom is the whole text; a reworded or longer card (Curse of Anubis, Final Attack Orders) stays uncompiled.
    /// </summary>
    public static class SimultaneousTriggersAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Curse of Fiend (12470447), whole text. ygopro c12470447: a Normal Spell (EFFECT_TYPE_ACTIVATE, condition
            // IsPhase(PHASE_STANDBY)) that only EFFECT_BECOME_QUICK makes activatable: while it is Set in the Spell &
            // Trap Zone, during its controller's Standby Phase, at the start of a Chain. Target step: a monster on the
            // field that can change position must exist (no target). Operation: ChangePosition(every such monster,
            // face-up Attack -> face-up Defense, face-up / face-down Defense -> face-up Attack; FLIP effects are not
            // suppressed), then a field EFFECT_CANNOT_CHANGE_POSITION on both Monster Zones until the End Phase
            // (manual changes and Flip Summons; card effects still change positions).
            new AtomRegistration.Atom(
                @"^\s*Change the battle positions of all Attack Position monsters on the field to Defense Position " +
                @"and vice[- ]versa\. These positions cannot be changed during the turn this card is activated except " +
                @"by the effect of a Spell, Trap or Effect Monster Card\. You can only activate this card during your " +
                @"Standby Phase\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    OnlyDuringOwnStandby = true,
                    ActivationCondition = PositionLocksRuntime.SetInYourStandbyPhase,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };
    }
}
