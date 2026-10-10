namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "LocksAndZones" (v142): turn locks and unusable zones. Each atom is the card's whole text
    /// (every sentence, anchored at both ends), so a longer or reworded card stays uncompiled.
    /// Runtime: <see cref="LocksAndZonesRuntime"/>.
    /// <para>Deferred (stay unimplemented, see the batch notes): Ground Collapse (the player selects 2 empty
    /// Main Monster Zones — no engine prompt picks an empty zone) and Curse of Fiend (a Set Normal Spell
    /// activated during your Standby Phase — the engine opens no Standby Phase activation window the UI
    /// drives).</para>
    /// </summary>
    public static class LocksAndZonesAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cold Wave (60682203). ygopro: EFFECT_TYPE_ACTIVATE, condition IsPhase(PHASE_MAIN1) and not
            // Duel.CheckPhaseActivity() (nothing has happened yet this Main Phase 1); the operation registers
            // EFFECT_CANNOT_ACTIVATE (re:IsSpellTrapEffect) and EFFECT_CANNOT_SSET for both players, reset at the
            // 2nd End Phase (through the opponent's next turn). Nothing is targeted.
            new AtomRegistration.Atom(
                @"^\s*This card can only be activated at the start of Main Phase 1\.\s*" +
                @"Until your next turn, you and your opponent cannot play or Set any Spell or Trap Cards\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn,
                    Side = EffectSide.Both,
                    ActivationCondition = LocksAndZonesRuntime.StartOfMainPhase1,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Dimensionhole (22959079). ygopro: EFFECT_FLAG_CARD_TARGET, 1 monster you control
            // (LOCATION_MZONE, 0, IsAbleToRemove); Duel.Remove(REASON_TEMPORARY), then a continuous
            // EVENT_PHASE+PHASE_STANDBY return (Duel.ReturnToField) on its controller's next Standby Phase, and
            // EFFECT_DISABLE_FIELD on the zone it left while the monster stays in the banishment.
            new AtomRegistration.Atom(
                @"^\s*Select 1 monster on your side of the field and remove it from play until your next " +
                @"Standby Phase\.\s*While the monster is removed from play, the Monster Card Zone of the " +
                @"selected monster cannot be used\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishTargetUntilYourNextStandbyPhase,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Amount = 1,
                    MakesChainLink = true
                }),
            // Light of Intervention (62867251). ygopro: Continuous Trap (free-chain activation, no effect of its
            // own), then while face-up in the Spell & Trap Zone: EFFECT_CANNOT_MSET and
            // EFFECT_LIGHT_OF_INTERVENTION for both players (a Set is made as a Normal Summon in face-up Defense
            // Position; ocgcore also makes a face-down Special Summon face-up Defense), and EFFECT_CANNOT_TURN_SET
            // on every monster on the field.
            new AtomRegistration.Atom(
                @"^\s*While this card is on the field, Monster Cards cannot be Set or flipped face-down\.\s*" +
                @"If a Monster Card would be Set, it must be Normal Summoned in face-up Defense Position\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };
    }
}
