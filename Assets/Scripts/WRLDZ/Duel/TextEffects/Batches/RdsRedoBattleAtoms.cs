namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// RdsRedoBattle batch — the RDS / FET battle cards the v208 QC failed closed, redone from their printed text and Lua:
    /// <list type="bullet">
    /// <item>Silent Swordsman LV3 (c1995985): "Negate your opponent's Spell effects that target this card." — a marker read
    /// at chain resolution (EVENT_CHAIN_SOLVING + Duel.NegateEffect: negate only, the Spell is not destroyed).</item>
    /// <item>Silent Swordsman LV5 (c74388798): immune to the opponent's Spell effects (EFFECT_IMMUNE_EFFECT,
    /// te:IsSpellEffect() and te:GetOwnerPlayer()~=handler) and its LV7 level-up in its next Standby Phase after a direct
    /// attack's battle damage (EVENT_BATTLE_DAMAGE flag, RESET_SELF_TURN 2).</item>
    /// <item>Mystic Swordsman LV6 (c60482781): EVENT_BATTLE_START destruction of the attacked face-down monster, which its
    /// controller may place on top of the opponent's Deck instead (only when the opponent owns it).</item>
    /// <item>Sasuke Samurai #4 (c64538655): EVENT_BATTLE_START coin call, attacking or attacked; a right call destroys the
    /// battle partner.</item>
    /// <item>Covering Fire (c74458486) and Cross Counter (c37083210): Normal Traps of the Damage Calculation window.</item>
    /// <item>Rare Metalmorph (c12503902): a persistent Continuous Trap linked to a face-up Machine on either field.</item>
    /// </list>
    /// Runtime: <see cref="RdsRedoBattleRuntime"/>.
    /// </summary>
    public static class RdsRedoBattleAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Silent Swordsman LV3 (c1995985) e1: FIELD+CONTINUOUS EVENT_CHAIN_SOLVING — rp~=tp, re:GetHandler():IsSpell(),
            // EFFECT_FLAG_CARD_TARGET, the target cards contain this card → Duel.NegateEffect(ev). Negate only.
            new AtomRegistration.Atom(
                Start + @"Negate your opponent" + Apos + @"s Spell effects that target this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateOpponentSpellEffectsThatTargetThis,
                    Side = EffectSide.Opponent,
                    AffectsThisCardOnly = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Silent Swordsman LV5 (c74388798) e1: EFFECT_IMMUNE_EFFECT, s.efilter: te:IsSpellEffect() and
            // te:GetOwnerPlayer()~=e:GetHandlerPlayer(). The LvChain Spell immunity, for the opponent's effects only.
            new AtomRegistration.Atom(
                Start + @"Unaffected by your opponent" + Apos + @"s Spell effects" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.UnaffectedByCardEffects,
                    UnaffectedByFilter = "Spell",
                    UnaffectedOnlyByOpponentEffects = true,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Silent Swordsman LV5 (c74388798) e2/e3: EVENT_BATTLE_DAMAGE (ep~=tp, GetAttackTarget()==nil) sets a flag that
            // lasts to the End Phase of its controller's next turn (RESETS_STANDARD_PHASE_END|RESET_SELF_TURN, count 2);
            // TRIGGER_O in its controller's Standby Phase while flagged, cost Cost.SelfToGrave, Special Summon the named
            // card from the hand or Deck ignoring its conditions (SpecialSummon(...,true,true) + CompleteProcedure).
            new AtomRegistration.Atom(
                Start + @"During your next Standby Phase after this card inflicts battle damage to your opponent by a " +
                @"direct attack: You can send this face-up card to the (?:GY|Graveyard); Special Summon 1 ""([^""]+)"" " +
                @"from your hand or Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    RequiresSendThisToGy = true,
                    FromHand = true,
                    FromDeck = true,
                    IsOptional = true,
                    OnlyDuringOwnStandby = true,
                    ActivationCondition = RdsRedoBattleRuntime.NextStandbyAfterDirectBattleDamage,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Mystic Swordsman LV6 (c60482781) e1: TRIGGER_F EVENT_BATTLE_START, this card attacked a face-down Defense
            // Position monster; d:IsRelateToBattle() → if d:GetOwner()~=tp and SelectYesNo: Destroy(d, REASON_EFFECT,
            // LOCATION_DECK) (top of its owner's Deck), else Destroy(d, REASON_EFFECT).
            new AtomRegistration.Atom(
                Start + @"At the start of the Damage Step, if this card attacked a face-down Defense Position monster: " +
                @"Destroy that monster, and if you do, you can place it on top of your opponent" + Apos + @"s Deck " +
                @"instead of sending it to the (?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StartOfDamageStep,
                    Action = EffectActionKind.DestroyAttackedFaceDownDefenseReturnToOwnerDeckTopOrGy,
                    Side = EffectSide.Opponent,
                    RequiresTargetFaceDownDefense = true
                }),

            // Sasuke Samurai #4 (c64538655) e1: TRIGGER_F EVENT_BATTLE_START with an attack target (it attacks or is
            // attacked); the battle partner still relates to the battle → Duel.CallCoin(tp) → right: Destroy (REASON_EFFECT).
            new AtomRegistration.Atom(
                Start + @"If this card battles a monster, toss a coin and call heads or tails before damage calculation\.\s*" +
                @"If you call it right, destroy the opponent" + Apos + @"s monster with this effect" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StartOfDamageStep,
                    Action = EffectActionKind.CoinCallDestroyBattledMonsterOnCorrect,
                    Side = EffectSide.Opponent
                }),

            // Covering Fire (c74458486): ACTIVATE EVENT_FREE_CHAIN, EFFECT_FLAG_DAMAGE_STEP + CARD_TARGET; condition: the
            // Damage Step before damage calculation, the attack target is your face-up monster; target 1 other face-up
            // monster you control with ATK above 0; the attacked monster gains that target's ATK (EFFECT_UPDATE_ATTACK,
            // RESET_PHASE|PHASE_DAMAGE_CAL). The engine's Damage Step answer window is the Damage Calculation window.
            new AtomRegistration.Atom(
                Start + @"During the Damage Step, while an opponent" + Apos + @"s monster is attacking a monster you " +
                @"control: Target 1 other face-up monster you control; your attacked monster gains ATK equal to that " +
                @"target" + Apos + @"s ATK during the Damage Step only" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.DamageCalculation,
                    Action = EffectActionKind.TargetGainsAtkEqualToAnotherTargetDuringDamageStep,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),

            // Cross Counter (c37083210): ACTIVATE EVENT_PRE_DAMAGE_CALCULATE; the attack target is your face-up Defense
            // Position monster whose DEF is higher than the attacker's ATK; that battle's damage to the opponent is doubled
            // and the attacker is destroyed at EVENT_DAMAGE_STEP_END if it still relates to the battle.
            new AtomRegistration.Atom(
                Start + @"If the DEF of an attacked Defense Position monster is higher than the ATK of your opponent" + Apos +
                @"s attacking monster, the Battle Damage inflicted to your opponent is doubled\.\s*" +
                @"Destroy the attacking monster after damage calculation" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.DamageCalculation,
                    Action = EffectActionKind.DoubleBattleDamageToAttackerThenDestroyAttacker,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.AttackingMonster,
                    MakesChainLink = true
                }),

            // Rare Metalmorph (c12503902): aux.AddPersistentProcedure on 1 face-up Machine on either field (s.filter); e1
            // EFFECT_UPDATE_ATTACK 500 on its persistent target; at resolution an EVENT_CHAIN_SOLVING negation of the first
            // Spell effect that targets that monster (either player's; e:Reset()); e2 EVENT_LEAVE_FIELD of the target →
            // destroy this card.
            AtomRegistration.Atom.Sequence(
                Start + @"Activate this card by targeting 1 Machine(?:-Type)? monster on the field; it gains (\d+) ATK\.\s*" +
                @"Once, negate a Spell effect that targets that monster\.\s*" +
                @"When that monster leaves the field, destroy this card" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.LinkThisToTarget,
                        Side = EffectSide.Both,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        RaceFilter = "Machine",
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        RequiresTargetFaceUp = true,
                        TargetCount = 1,
                        // The linked monster's ATK (FieldSpellEffects.ApplyEquipStats reads a linked card's EquipAtkBonus).
                        EquipAtkBonus = int.Parse(m.Groups[1].Value),
                        AiEquipsOwnMonster = true,
                        DestroyThisWhenBoundHostLeaves = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.NegateOnceSpellEffectTargetingBoundMonster,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };
    }
}
