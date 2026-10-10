namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleRules" (v161): battle rules and locks — Cave Dragon (Normal Summon condition + attack condition),
    /// Thunder Nyan Nyan (self-destroy state), Array of Revealing Light (declared-Type summon-turn attack lock), After
    /// the Struggle (end-of-Battle-Phase sweep of the monsters that battled), The Secret of the Bandit (turn-scoped
    /// battle-damage discard watcher) and Patrician of Darkness (the defender chooses the attack targets). Every atom is
    /// one whole sentence (or the card's whole text), anchored at a sentence start and end, so a longer or reworded
    /// sentence stays uncompiled. Runtime: <see cref="BattleRulesRuntime"/>.
    /// </summary>
    public static class BattleRulesAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string End = @"(?:\.(?=\s|$)|$)";
        /// <summary>The card's whole text: start of text … final period and the end of the text.</summary>
        const string WholeStart = @"^\s*";
        const string WholeEnd = @"\.?\s*$";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cave Dragon (c93220472) e1: SINGLE EFFECT_CANNOT_SUMMON, condition GetFieldGroupCount(tp, MZONE, 0) > 0
            // (any monster, face-up or face-down). Only the Normal Summon is forbidden: it can still be Set.
            new AtomRegistration.Atom(
                Start + @"Cannot be Normal Summoned while you control a monster" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Cave Dragon e2: SINGLE EFFECT_CANNOT_ATTACK unless IsExistingMatchingCard(FaceupFilter(IsRace, DRAGON),
            // tp, MZONE, 0, 1, this card): another face-up monster of that Type you control.
            new AtomRegistration.Atom(
                Start + @"This card cannot declare an attack unless you control another " +
                @"([A-Za-z]+(?:[ -][A-Za-z]+)?)-Type monster" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace,
                    RaceFilter = m.Groups[1].Value,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Thunder Nyan Nyan (c70797118): SINGLE EFFECT_SELF_DESTROY (range MZONE) while its controller controls a
            // face-up monster that is not LIGHT (FaceupFilter(IsAttributeExcept, LIGHT), itself included).
            new AtomRegistration.Atom(
                Start + @"If there is a non-(LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE) monster on your side of the " +
                @"field, destroy this face-up card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute,
                    AttributeFilter = m.Groups[1].Value.ToUpperInvariant(),
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // Array of Revealing Light (c69296555): Field Spell. The Type is announced while it is activated
            // (AnnounceRace in the target step, not a target); FIELD EFFECT_CANNOT_ATTACK (MZONE both sides) on
            // monsters of that Type with STATUS_SUMMON_TURN / FLIP_SUMMON_TURN / SPSUMMON_TURN.
            AtomRegistration.Atom.Sequence(
                Start + @"Declare 1 Type of monster\.\s*Any monster of the declared Type cannot declare an attack " +
                @"during the turn it is Normal Summoned, Flip Summoned or Special Summoned" + End,
                _ => new[]
                {
                    TypeDeclarationAtoms.DeclareTypeOnActivation(),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousCannotAttack,
                        RaceFilterIsDeclared = true,
                        RequiresSummonedOrFlippedThisTurn = true,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // After the Struggle (c25345186): Normal Spell, condition IsPhase(MAIN1). The operation registers for the
            // turn an EVENT_BATTLED flag on the attacker and the attack target, and at EVENT_PHASE+PHASE_BATTLE (count
            // limit 1) destroys every flagged monster on the field (REASON_EFFECT). Printed "End Step" = the end of the
            // Battle Phase.
            new AtomRegistration.Atom(
                WholeStart + @"This card can only be activated during Main Phase 1\.\s*All monsters on both sides of " +
                @"the field that have been involved in damage calculation are destroyed during the End Step of the turn" +
                WholeEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase,
                    OnlyDuringMainPhase1 = true,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // The Secret of the Bandit (c99351431): Normal Spell, chk IsPhase(MAIN1); targets 1 face-up monster on
            // either field (EFFECT_FLAG_CARD_TARGET). If it is still face-up on the field at resolution, a TRIGGER_F on
            // EVENT_BATTLE_DAMAGE for the rest of the turn: each time it inflicts battle damage to the activator's
            // opponent, that opponent sends 1 random card from their hand to the GY.
            new AtomRegistration.Atom(
                WholeStart + @"This card can only be activated during Main Phase 1\.\s*Select 1 face-up monster on the " +
                @"field\.\s*Each time the selected monster inflicts Battle Damage to your opponent's Life Points this " +
                @"turn, your opponent discards 1 random card" + WholeEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    Amount = 1,
                    OnlyDuringMainPhase1 = true,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Patrician of Darkness (c19153634): FIELD EFFECT_PATRICIAN_OF_DARKNESS (PLAYER_TARGET, range 0,1) while it
            // is in the Monster Zone: when the opponent attacks a monster, this card's controller selects the target.
            new AtomRegistration.Atom(
                Start + @"You choose the attack targets for your opponent's attacks" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.YouChooseOpponentsAttackTargets,
                    Side = EffectSide.Opponent,
                    MakesChainLink = false
                }),
        };
    }
}
