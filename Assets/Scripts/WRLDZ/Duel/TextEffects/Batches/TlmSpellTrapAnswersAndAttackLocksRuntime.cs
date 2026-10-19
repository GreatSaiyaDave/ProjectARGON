using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmSpellTrapAnswersAndAttackLocks" runtime (atoms: <see cref="TlmSpellTrapAnswersAndAttackLocksAtoms"/>).
    /// Reads compiled programs only — no card-id branches; only a FullyCompiled program's clauses apply.
    /// <list type="bullet">
    /// <item><b>Continuous-only answers</b> (<see cref="ChainAnswerAllowed"/>, from TextEffectRuntime.CanAnswerChainLinkWithClause):
    /// Royal Surrender / Spell-Stopping Statute answer only the activation of a Continuous Trap (Spell) Card.</item>
    /// <item><b>Mid Shield Gardna</b> (<see cref="NegatesBeforeFlip"/> / <see cref="SendNegatedCardToGy"/> /
    /// <see cref="FlipSelfFaceUpDefense"/>, from DuelEngine.ApplyActivationNegation): negate, send the Spell to the GY, then
    /// change this card to face-up Defense Position. <see cref="MonsterChainAnswerSpeed"/> (from
    /// DuelEngine.TryActivateChainResponse): a monster's chain answer is Spell Speed 2 whatever its old wording.</item>
    /// <item><b>Ancient Gear attack lock</b> (<see cref="ActivationLocked"/>, from ContinuousNegationRuntime.ActivationLocked, so
    /// every activation gate, every response window and SpellTrapEffects.AiAutoRespond ask it).</item>
    /// </list>
    /// </summary>
    public static class TlmSpellTrapAnswersAndAttackLocksRuntime
    {
        // ───────────────────────────── Royal Surrender / Spell-Stopping Statute ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CanAnswerChainLinkWithClause: an answer with <see cref="EffectClause.NegateRespondsToContinuousOnly"/>
        /// answers only the activation of a Continuous Spell/Trap Card (from the hand or Set), never a later effect of a
        /// face-up one. Spell vs Trap and "your opponent" are the shared NegateRespondsTo* / NegateOpponentActivationOnly checks.
        /// </summary>
        public static bool ChainAnswerAllowed(EffectClause clause, ChainLink target, out string reason)
        {
            reason = "OK";
            if (clause == null || !clause.NegateRespondsToContinuousOnly) return true;
            var def = target?.Card?.Def;
            if (def != null && def.IsContinuousSpellOrTrap && !def.IsFieldSpell &&
                ChainLinkAnswersRuntime.IsCardActivation(target))
                return true;
            reason = "Only answers the activation of a Continuous Spell/Trap Card.";
            return false;
        }

        // ───────────────────────────── Mid Shield Gardna ─────────────────────────────

        /// <summary>
        /// DuelEngine.TryActivateChainResponse: a monster's compiled answer to the last Chain Link (a ChainLinkActivated
        /// clause) is a Quick Effect — Spell Speed 2 — whatever its old wording (Mid Shield Gardna's e2 is EFFECT_TYPE_QUICK_F
        /// on EVENT_CHAINING, yet its text prints neither "Quick Effect" nor "during either player's turn", so
        /// OfficialEffectRegistry.SpeedOf reads Spell Speed 1, which cannot chain).
        /// </summary>
        public static void MonsterChainAnswerSpeed(CardInstance card, ref SpellSpeed speed, ref EffectClass cls)
        {
            if (card?.Def == null || !card.Def.IsMonster || speed != SpellSpeed.Speed1) return;
            speed = SpellSpeed.Speed2;
            cls = EffectClass.Quick;
        }

        /// <summary>The resolving answer <paramref name="link"/> negates first, then sends and flips (Mid Shield Gardna).</summary>
        public static bool NegatesBeforeFlip(ChainLink link)
        {
            var def = link?.Card?.Def;
            if (def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClausesFor(EffectTiming.ChainLinkActivated)
                .Any(c => c != null && c.Action == EffectActionKind.NegateActivation && c.SendNegatedCardToGyThenFlipSelf);
        }

        /// <summary>
        /// DuelEngine.ApplyActivationNegation, after the negation: the negated Spell Card, still on the field, is sent to the
        /// GY by this effect (Lua SendtoGrave(eg, REASON_EFFECT) while re:GetHandler():IsRelateToEffect(re)).
        /// </summary>
        public static void SendNegatedCardToGy(DuelEngine engine, ChainLink link)
        {
            var answered = link?.TargetLink;
            var card = answered?.Card;
            var owner = answered?.Controller;
            if (engine == null || card?.Def == null || owner == null) return;
            var onField = owner.TryFindSpellTrap(card, out _) ||
                          (owner.FieldSpellZone != null && ReferenceEquals(owner.FieldSpellZone.Occupant, card));
            if (!onField) return;
            engine.SendCardToGrave(owner, card, sentBy: link.Card);
            engine.Log($"{link.Card?.Name}: {card.Name} is sent to the GY.");
        }

        /// <summary>
        /// DuelEngine.ApplyActivationNegation, last: this card, if it is still in its controller's Monster Zone, is changed to
        /// face-up Defense Position (Lua ChangePosition(e:GetHandler(), POS_FACEUP_DEFENSE), applied whether or not the
        /// negation took).
        /// </summary>
        public static void FlipSelfFaceUpDefense(DuelEngine engine, ChainLink link)
        {
            var self = link?.Card;
            var who = link?.Controller;
            if (engine == null || self == null || who == null || !who.TryFindMonster(self, out _)) return;
            self.FaceUp = true;
            self.Position = BattlePosition.Defense;
            engine.Log($"{self.Name} is changed to face-up Defense Position.");
        }

        // ───────────────────────────── Ancient Gear attack lock ─────────────────────────────

        /// <summary>
        /// ContinuousNegationRuntime.ActivationLocked: <paramref name="who"/> may not activate the Spell/Trap Card
        /// <paramref name="card"/> (in their hand, or Set on their field) while a face-up, un-negated monster their opponent
        /// controls with <see cref="EffectActionKind.ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks"/> is the
        /// attacker — from its attack declaration through the end of the Damage Step. The effects of face-up cards are not
        /// card activations and are not locked.
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster) return false;
            if (!card.Def.IsSpell && !card.Def.IsTrap) return false;
            if (!OwnSetOrHand(who, card)) return false;
            var src = AttackLockSource(engine, who);
            if (src == null) return false;
            reason = $"{src.Name} is attacking: you cannot activate Spell/Trap Cards until the end of the Damage Step.";
            return true;
        }

        /// <summary>The attacking monster of the battle under way (Lua Duel.GetAttacker()), or null outside one.</summary>
        static CardInstance CurrentAttacker(DuelEngine engine)
        {
            if (engine.Phase != DuelPhase.Battle) return null;
            // The Battle Step: from the attack declaration until the Damage Step begins.
            if (engine.DeclaredAttacker != null) return engine.DeclaredAttacker;
            // The Damage Step, through its end (the end-of-Damage-Step window included).
            if (engine.BattleStep != BattleStep.DamageStep || engine.DamageSubStep == DamageSubStep.None) return null;
            var attacker = DamageStepRulesRuntime.BattleAttacker(engine);
            if (attacker != null) return attacker;
            return engine.TryGetBattlingPair(out var a, out _) ? a : null;
        }

        /// <summary>A face-up, un-negated attacker <paramref name="activator"/>'s opponent controls whose lock applies.</summary>
        static CardInstance AttackLockSource(DuelEngine engine, DuelistState activator)
        {
            var attacker = CurrentAttacker(engine);
            var opp = engine.OpponentOf(activator);
            if (attacker?.Def == null || opp == null || !attacker.FaceUp || attacker.IsNegated) return null;
            if (!opp.TryFindMonster(attacker, out _)) return null;
            if (ContinuousNegationRuntime.NegatedBy(engine, attacker) != null) return null;
            var prog = CompiledEffectCache.GetOrCompile(attacker.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Any(c =>
                c != null && c.Action == EffectActionKind.ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks)
                ? attacker
                : null;
        }

        /// <summary>The card's activation: from the hand, or a Set card on the field (a face-up card's effect is not one).</summary>
        static bool OwnSetOrHand(DuelistState who, CardInstance card) =>
            (who.Hand != null && who.Hand.Contains(card)) ||
            ((who.TryFindSpellTrap(card, out _) ||
              (who.FieldSpellZone != null && ReferenceEquals(who.FieldSpellZone.Occupant, card))) && !card.FaceUp);
    }
}
