namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ContinuousLocksAndIgnitions batch (v163 PGD / MFC): continuous rules, lingering summon locks, Set-card protection and
    /// once-per-turn Continuous Spell summons, checked against the ygopro scripts. Every pattern is anchored to the start
    /// and the end of the card text, so a longer or different card never compiles part of its text here. Runtime:
    /// <see cref="ContinuousLocksAndIgnitionsRuntime"/>.
    /// </summary>
    public static class ContinuousLocksAndIgnitionsAtoms
    {
        /// <summary>A Monster Type word ("Insect", "Winged Beast", "Beast-Warrior") before "-Type" or "monster".</summary>
        const string TypeWord = @"((?:[A-Za-z]+[ -])*?[A-Za-z]+)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gora Turtle (c80233946): EFFECT_CANNOT_ATTACK_ANNOUNCE, range MZONE, both Monster Zones, target
            // IsAttackAbove(1900) — this card itself included. Read by DuelEngine.ContinuousCannotAttackBlocks (monster host).
            new AtomRegistration.Atom(
                @"^\s*As long as this card remains face-up on the field, monsters with an ATK equal to (\d+) or more " +
                @"cannot declare an attack\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Amount = int.Parse(m.Groups[1].Value),
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Arsenal Bug (c42364374): EFFECT_SET_ATTACK / EFFECT_SET_DEFENSE 1000 (SINGLE_RANGE, MZONE) while its controller
            // controls no other face-up Insect monster (aux.FaceupFilter, excluding itself).
            new AtomRegistration.Atom(
                @"^\s*If you control no other " + TypeWord + @"-Type monsters, this card['’]s ATK and DEF become (\d+)\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace,
                    RaceFilter = m.Groups[1].Value,
                    Amount = int.Parse(m.Groups[2].Value),
                    DefAmount = int.Parse(m.Groups[2].Value),
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Timidity (c40350910): Normal Spell, free chain; at resolution a player-registered EFFECT_INDESTRUCTABLE_EFFECT
            // on every face-down card in both Spell & Trap Zones (EFFECT_FLAG_SET_AVAILABLE: Sets made later too), reset at
            // the 2nd End Phase (the opponent's next End Phase).
            new AtomRegistration.Atom(
                @"^\s*Neither player can destroy Set Spell and Trap Cards on the field until your opponent['’]s next " +
                @"End Phase\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase,
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),

            // Non Aggression Area (c76848240): Normal Trap, condition your Standby Phase (IsPhase(STANDBY), IsTurnPlayer);
            // cost discard 1 card (REASON_COST|REASON_DISCARD); EFFECT_CANNOT_SUMMON / _MSET / _SPECIAL_SUMMON on the
            // opponent with condition TurnCount ~= activation turn, reset at the 2nd End Phase = their next turn only.
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card during your Standby Phase\.\s*Discard 1 card from your hand\.\s*" +
                @"Your opponent cannot Normal Summon, Set and/or Special Summon during (?:his/her|their) next turn\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentCannotSummonOrSetDuringNextTurn,
                    Side = EffectSide.Opponent,
                    OnlyDuringOwnStandby = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    MakesChainLink = true
                }),

            // Call of the Mummy (c4861205): Continuous Spell whose activation does nothing; EFFECT_TYPE_IGNITION in the
            // Spell & Trap Zone, SetCountLimit(1), condition: you control no monsters; at resolution: still related, a free
            // Monster Zone, still no monsters; Special Summon 1 Zombie from your hand in a face-up position.
            new AtomRegistration.Atom(
                @"^\s*Once per turn: You can Special Summon 1 " + TypeWord + @"(?:-Type)? monster from your hand\.\s*" +
                @"You must control no monsters to activate and to resolve this effect\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.SpecialSummonFromHandByFaceUpIgnition,
                    RaceFilter = m.Groups[1].Value,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    IsOptional = true,
                    RequiresControllerControlsNoMonsters = true,
                    RequiresThisRelatedAtResolution = true,
                    MakesChainLink = true
                }),

            // Frontline Base (c46181000): EFFECT_TYPE_IGNITION in the Spell & Trap Zone, SetCountLimit(1); Special Summon 1
            // Level 4 or lower Union monster (TYPE_UNION) from your hand in a face-up position; no relation check.
            new AtomRegistration.Atom(
                @"^\s*Once per turn: You can Special Summon 1 Level (\d+) or lower Union monster from your hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.SpecialSummonFromHandByFaceUpIgnition,
                    TargetCardKind = ContinuousLocksAndIgnitionsRuntime.UnionKind,
                    Amount = int.Parse(m.Groups[1].Value),
                    AmountIsLevel = true,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Buster Rancher (c84740193): aux.AddEquipProcedure with Card.IsAttackBelow(1000) (a face-up monster on either
            // field) and s.eqlimit (ATK 1000 or less, except during damage calculation); EFFECT_UPDATE_ATTACK 2500 on the
            // equipped monster with s.atkcon: damage calculation, its battle target in Attack Position with 2500+ ATK or
            // in Defense Position with 2500+ DEF.
            AtomRegistration.Atom.Sequence(
                @"^\s*Only a monster with an ATK of (\d+) points or less can be equipped with this card\.\s*" +
                @"During damage calculation, increase the ATK of the monster equipped with this card by (\d+) points if " +
                @"the opponent['’]s monster that battles it is in Attack Position and its ATK is (\d+) or more, OR if " +
                @"the opponent['’]s monster that battles it is in Defense Position and its DEF is \3 or more\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        Side = EffectSide.Both,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        EquipLimitAtkMax = int.Parse(m.Groups[1].Value),
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedGainsAtkInDamageCalcVsStrongBattler,
                        Amount = int.Parse(m.Groups[2].Value),
                        ConditionAmount = int.Parse(m.Groups[3].Value),
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };
    }
}
