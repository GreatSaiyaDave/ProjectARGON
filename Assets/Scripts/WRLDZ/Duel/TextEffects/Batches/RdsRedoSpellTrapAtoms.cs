using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RdsRedoSpellTrap" whole-card atoms (RDS / FET Spells and Traps the v208 QC failed closed), each anchored
    /// at both ends so a longer or reworded card stays uncompiled:
    /// <list type="bullet">
    /// <item>Chain Burst (c48276469): Continuous Trap; each time a Trap Card's activation resolves, the player who activated
    /// it takes 1000 damage (<see cref="EffectActionKind.InflictDamageToTrapActivatorAfterResolve"/>).</item>
    /// <item>Spell Absorption (c51481927): Continuous Spell; each time a Spell Card's activation resolves, its controller
    /// gains 500 LP (<see cref="EffectActionKind.GainLpWhenSpellCardActivationResolves"/>).</item>
    /// <item>Forced Ceasefire (c97806240): Normal Trap; cost: discard 1 card; neither player can activate Trap Cards for the
    /// rest of the turn (<see cref="EffectActionKind.TrapCardActivationsLockedThisTurn"/>, Side Both).</item>
    /// <item>Necklace of Command (c48576971): Equip Spell; its monster destroyed by battle and this card sent to the GY: draw
    /// 1 or discard 1 random card from the opponent's hand (<see cref="EffectActionKind.DrawOrDiscardRandomFromOpponentHand"/>).</item>
    /// <item>Flint (c75560629): Equip Spell; −300 ATK, cannot change its battle position or declare an attack; sent to the
    /// GY because its monster was destroyed: equip it to a targeted face-up monster
    /// (<see cref="EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed"/>).</item>
    /// <item>Triangle Ecstasy Spark (c12181376): Normal Spell; needs a face-up "Harpie Lady Sisters"; their ATK becomes 2700,
    /// the opponent cannot activate Trap Cards and their Trap effects on the field are negated, all for the rest of the turn
    /// (<see cref="EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn"/>, the opponent's
    /// <see cref="EffectActionKind.TrapCardActivationsLockedThisTurn"/>, <see cref="EffectActionKind.NegateCardFamilyOnFieldThisTurn"/>
    /// with <see cref="TurnNegationTrapsRuntime.OpponentTrap"/>).</item>
    /// </list>
    /// Registered with one line in <see cref="AtomRegistration"/>'s BatchAtomSets. Runtime: <see cref="RdsRedoSpellTrapRuntime"/>.
    /// </summary>
    public static class RdsRedoSpellTrapAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Chain Burst. Not a Chain Link (Lua CONTINUOUS EVENT_CHAINING aux.chainreg + EVENT_CHAIN_SOLVED, Duel.Damage(rp)).
            new AtomRegistration.Atom(
                @"^\s*Each time a player activates a Trap Card, they take (\d+) damage immediately after it resolves\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageToTrapActivatorAfterResolve,
                    Side = EffectSide.Both,
                    Amount = Parse(m, 1, 1000),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Spell Absorption. Not a Chain Link (the same listener; Duel.Recover(e:GetHandlerPlayer())).
            new AtomRegistration.Atom(
                @"^\s*Each time a Spell Card is activated, gain (\d+) (?:LP|Life Points) immediately after it resolves\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainLpWhenSpellCardActivationResolves,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 500),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Forced Ceasefire. Lua: cost DiscardHand(1, other than this card); EFFECT_CANNOT_ACTIVATE target range (1,1),
            // aclimit EFFECT_TYPE_ACTIVATE + IsTrapEffect, RESET_PHASE|PHASE_END.
            new AtomRegistration.Atom(
                @"^\s*Discard 1 card from your hand\. No Trap Cards can be activated until the end of this turn\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TrapCardActivationsLockedThisTurn,
                    Side = EffectSide.Both,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    MakesChainLink = true
                }),
            // Necklace of Command. Lua: aux.AddEquipProcedure(c) (a face-up monster on either field); SINGLE TRIGGER_F on
            // EVENT_TO_GRAVE, REASON_LOST_TARGET, ec destroyed by battle and previously controlled by tp; SelectOption draw 1
            // / discard 1 random card from the opponent's hand ("you can either" is the choice, the trigger is mandatory).
            AtomRegistration.Atom.Sequence(
                @"^\s*When a monster you control that's equipped with this card is destroyed by battle, and this card is " +
                @"sent to the (?:Graveyard|GY), you can either:\s*●\s*Draw 1 card\.\s*OR\s*●\s*Discard 1 random card " +
                @"from your opponent's hand\.?\s*$",
                _ => new[]
                {
                    Equip(atkBonus: 0, aiEquipsOwn: true),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DrawOrDiscardRandomFromOpponentHand,
                        Side = EffectSide.Controller,
                        Amount = 1,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // Flint. Lua: aux.AddEquipProcedure(c, nil, …) (either field); EQUIP UPDATE_ATTACK -300, CANNOT_ATTACK_ANNOUNCE,
            // CANNOT_CHANGE_POSITION; SINGLE TRIGGER_F on EVENT_TO_GRAVE, CARD_TARGET, REASON_LOST_TARGET and ec destroyed:
            // target 1 face-up monster on the field; equip this card to it.
            AtomRegistration.Atom.Sequence(
                @"^\s*The equipped monster loses (\d+) ATK and cannot change its battle position or declare an attack\. " +
                @"If this card is destroyed because the equipped monster is destroyed: Target 1 monster on the field; " +
                @"equip this card to that monster\.?\s*$",
                m => new[]
                {
                    Equip(atkBonus: -Parse(m, 1, 300), aiEquipsOwn: false),
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedCannotChangePosition,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquippedCannotAttack,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ReEquipThisFromGyWhenEquippedMonsterDestroyed,
                        Side = EffectSide.Controller,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
            // Triangle Ecstasy Spark. Lua: target chk = a face-up "Harpie Lady Sisters" in either Monster Zone; each one there
            // at resolution gets SET_ATTACK_FINAL 2700 (RESETS_STANDARD_PHASE_END); EFFECT_CANNOT_ACTIVATE (0,1) for Trap Card
            // activations and EFFECT_DISABLE / DISABLE_TRAPMONSTER on the opponent's Traps, all until the End Phase.
            AtomRegistration.Atom.Sequence(
                @"^\s*Until the end of this turn, the ATK of all ""([^""]+)"" currently on the field becomes (\d+), your " +
                @"opponent cannot activate any Trap Cards, also negate all your opponent's Trap effects on the field\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.NamedFaceUpMonstersAtkBecomesUntilEndOfTurn,
                        Side = EffectSide.Both,
                        ConditionName = m.Groups[1].Value.Trim(),
                        Amount = Parse(m, 2, 2700),
                        ActivationCondition = ChaosBanishSummonsRuntime.NamedFaceUpOnEitherField,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.TrapCardActivationsLockedThisTurn,
                        Side = EffectSide.Opponent,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.NegateCardFamilyOnFieldThisTurn,
                        Side = EffectSide.Opponent,
                        NegatedCardFamily = TurnNegationTrapsRuntime.OpponentTrap,
                        MakesChainLink = true
                    }
                }),
        };

        /// <summary>The Equip Spell's own equip (aux.AddEquipProcedure with no player filter): a face-up monster on either field.</summary>
        static EffectClause Equip(int atkBonus, bool aiEquipsOwn) => new EffectClause
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.EquipThisToTarget,
            Zone = EffectZoneFilter.FieldAnyMonster,
            Side = EffectSide.Both,
            RequiresTargetChoice = true,
            IsPsctTarget = true,
            EquipAtkBonus = atkBonus,
            AiEquipsOwnMonster = aiEquipsOwn,
            StaysOnField = true,
            MakesChainLink = true
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
