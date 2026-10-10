using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "StandbyBurnsAndDice" (v162): Continuous Spell/Trap and monster Standby Phase effects — burns (per
    /// banished face-up monster, doubling, linked Spell's controller, bound monster), die-roll destruction, a mandatory
    /// Tribute upkeep and an opponent's-Standby look at a random hand card. Each atom is the card's whole text (every
    /// sentence of it is honoured). The runtime is <see cref="StandbyBurnsAndDiceRuntime"/>; the Standby clauses fire from
    /// TextEffectRuntime.FirePhaseTriggersFor like every other Standby trigger.
    /// </summary>
    public static class StandbyBurnsAndDiceAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";
        const string Lp = @"(?:LP|Life Points)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Blind Destruction (c32015116): EVENT_PHASE+PHASE_STANDBY, TRIGGER_F, count limit 1, turn player only;
            // TossDice(tp,1); destroy every face-up monster on either field whose Level is the result (1-5) or 6+ (6).
            new AtomRegistration.Atom(
                Begin + @"Once per turn, during your Standby Phase: Roll a six-sided die\.\s*" +
                @"If the result is 1-5, destroy all monsters on the field with the same Level as the number rolled\.\s*" +
                @"If it is 6, destroy all Level 6 and higher monsters on the field" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Needle Wall (c38299233): TRIGGER_F, count limit 1, turn player only; while the die shows 6 roll again;
            // Destroy(GetFieldCard(1-tp, LOCATION_MZONE, d-1)).
            new AtomRegistration.Atom(
                Begin + @"Once per turn, during your Standby Phase: Roll a six-sided die\.\s*" +
                @"Treat your opponent's Main Monster Zones as numbers 1-5, counting from your right, and destroy the " +
                @"monster that is in the same Main Monster Zone as the result\.\s*" +
                @"If the result is 6, roll again" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.RollDieDestroyOppMonsterInRolledZone,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Graverobber's Retribution (c33737664): TRIGGER_F, count limit 1, turn player only; damage to 1-tp =
            // 100 x face-up monsters in the opponent's banished pile, counted at resolution.
            new AtomRegistration.Atom(
                Begin + @"During each of your Standby Phases, inflict (\d+) (?:points of )?damage to your opponent" +
                @"(?:'s Life Points)? for each of your opponent's monsters that (?:have been|are) " +
                @"(?:removed from play|banished)" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 100),
                    CountSource = PerCountBurnsRuntime.OppBanishedFaceUpMonsters,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Dark Snake Syndrome (c47233801): TRIGGER_F, turn player only; 200 the first time (flag label), then double the
            // label each resolution; Damage(tp) and Damage(1-tp) at once (RDComplete). The flag resets when it leaves.
            new AtomRegistration.Atom(
                Begin + @"During each of your Standby Phases: Inflict damage to both players\.\s*" +
                @"This damage begins at (\d+) and is doubled during each of your following Standby Phases" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.InflictDoublingDamageToBothPlayers,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 200),
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Mask of Dispel (c20765952): Activate targeting 1 face-up Spell in either Spell & Trap Zone (Field Zone
            // included), not itself, and SetCardTarget it. TRIGGER_F in your Standby: 500 damage to that card's controller.
            // EVENT_LEAVE_FIELD continuous: when that card leaves the field, destroy this card.
            AtomRegistration.Atom.Sequence(
                Begin + @"Select 1 face-up Spell Card on the field\.\s*" +
                @"The controller of the Spell Card takes (\d+) points of damage during each of your Standby Phases\.\s*" +
                @"When the selected card is removed from the field, destroy this card" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.LinkThisToTargetSpell,
                        Side = EffectSide.Both,
                        Zone = EffectZoneFilter.FieldFaceUpSpells,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.InflictDamageToLinkedSpellController,
                        Side = EffectSide.Both,
                        Amount = Parse(m, 1, 500),
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyThisWhenLinkedSpellLeavesField,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Nightmare Wheel (c54704216): aux.AddPersistentProcedure (target 1 monster the opponent controls, face-down
            // included); it cannot attack or change its battle position; EVENT_LEAVE_FIELD of the target destroys this
            // card; TRIGGER_F in your Standby (count limit 1) while the target is still this card's target: 500 to 1-tp.
            AtomRegistration.Atom.Sequence(
                Begin + @"Activate this card by targeting 1 monster your opponent controls;\s*" +
                @"it cannot attack or change its battle position\.\s*" +
                @"When it leaves the field, destroy this card\.\s*" +
                @"Once per turn, during your Standby Phase: Inflict (\d+) damage to your opponent\.\s*" +
                @"That monster must be on the field to activate and to resolve this effect" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.ContinuousCannotAttack,
                        Zone = EffectZoneFilter.OppMonsters,
                        RequiresTargetChoice = true,
                        StaysOnField = true,
                        DestroyThisWhenBoundHostLeaves = true,
                        AlsoCannotChangeBattlePosition = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousCannotAttack,
                        StaysOnField = true,
                        DestroyThisWhenBoundHostLeaves = true,
                        AlsoCannotChangeBattlePosition = true,
                        MakesChainLink = false,
                        SourceSnippet = "bound monster cannot attack or change battle position"
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.InflictDamageToOpponentWhileBoundMonsterOnField,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 1, 500),
                        StaysOnField = true,
                        MakesChainLink = true
                    }
                }),

            // The Unfriendly Amazon (c65475294): FIELD+CONTINUOUS in the Monster Zone, count limit 1, own Standby,
            // CANNOT_DISABLE: if CheckReleaseGroup(tp,nil,1,c) then SelectReleaseGroup(1,1, excluding c) + Release
            // (REASON_COST), else Destroy(c, REASON_COST). Not a Chain Link; the Tribute is not optional.
            new AtomRegistration.Atom(
                Begin + @"Tribute 1 of your monsters on the field \(excluding this monster\) during each of your " +
                @"Standby Phases\.\s*If you do not, this card is destroyed" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.StandbyTributeOrDestroyThis,
                    Side = EffectSide.Controller,
                    TributeExceptThis = true,
                    UpkeepTributeMandatory = true,
                    MakesChainLink = false
                }),

            // Inspection (c16227556): TRIGGER_O in the opponent's Standby (count limit 1), condition: the opponent's hand
            // is not empty; cost Cost.PayLP(500); RandomSelect 1 of their hand, ConfirmCards, ShuffleHand(1-tp).
            new AtomRegistration.Atom(
                Begin + @"During your opponent's Standby Phase, you can pay (\d+) " + Lp + @" to randomly select " +
                @"1 card in your opponent's hand and look at it" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.PayLpLookAtRandomOppHandCard,
                    Side = EffectSide.Opponent,
                    OpponentTurnOnly = true,
                    IsOptional = true,
                    Amount = Parse(m, 1, 500),
                    StaysOnField = true,
                    MakesChainLink = true
                }),
        };

        /// <summary>
        /// The whole text is one of this batch's atoms. The compiler's earlier fragment templates (the Dark Hole
        /// "destroy all monsters on the field" inside Blind Destruction's Level wipe) must leave it alone.
        /// </summary>
        public static bool IsWholeTextAtom(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var atom in Atoms)
                if (atom.Pattern.IsMatch(text))
                    return true;
            return false;
        }

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
