using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TurnNegationTraps" (v141) runtime: World Suppression / Mystic Probe / Metal Detector.
    /// Each is a Normal Trap chained to the activation of a Field Spell / Continuous Spell / Continuous
    /// Trap Card; it registers a field-wide negation of that card family for the rest of the turn
    /// (ygopro: EFFECT_TYPE_FIELD EFFECT_DISABLE, target range LOCATION_SZONE on both sides — the Field
    /// Zone included — reset at the End Phase). Cards of the family activated later in the same turn are
    /// negated too; the negation is not targeted and applies to both players' cards.
    /// <para>How the negation is honoured:</para>
    /// <list type="bullet">
    /// <item><see cref="Refresh"/> sets <see cref="CardInstance.NegatedByTurnFamily"/> on every face-up
    /// card of a negated family in a Spell &amp; Trap Zone or Field Zone and clears it everywhere else,
    /// so every <see cref="CardInstance.IsNegated"/> reader (FieldSpellEffects auras, ContinuousProtections,
    /// trigger and phase sites, upkeep costs, monster links) skips the card. It runs on every board
    /// refresh and at the start of each turn (<see cref="OnTurnStart"/>), so a negation never outlives
    /// its turn.</item>
    /// <item><see cref="SkipsNegatedEffect"/> (TextEffectRuntime.ApplyClause): any clause of a negated
    /// card does nothing — the activation Mystic Probe / Metal Detector / World Suppression was chained
    /// to resolves without effect and the card stays (ygopro: STATUS_DISABLED on a card related to its
    /// chain link negates the effect at resolution). Costs are paid outside ApplyClause and still paid.</item>
    /// <item>TextEffectRuntime.CanActivate refuses the effects of a negated face-up card; activating a
    /// Set card or one from the hand stays legal (it resolves without effect).</item>
    /// </list>
    /// </summary>
    public static class TurnNegationTrapsRuntime
    {
        /// <summary>Family name: Field Spell Cards (World Suppression).</summary>
        public const string FieldSpell = "FieldSpell";
        /// <summary>Family name: Continuous Spell Cards (Mystic Probe).</summary>
        public const string ContinuousSpell = "ContinuousSpell";
        /// <summary>Family name: Continuous Trap Cards (Metal Detector).</summary>
        public const string ContinuousTrap = "ContinuousTrap";
        /// <summary>
        /// Family name: Equip Cards (Armored Glass, EquipModel batch). Membership is per card on the field, not per
        /// card type (<see cref="EquipModelRuntime.IsEquipCard"/>: an Equip Spell / Equip Trap equipped to a monster, or
        /// a monster acting as an Equip Card), so <see cref="IsOfFamily"/> never matches it.
        /// </summary>
        public const string EquipCard = "EquipCard";
        /// <summary>
        /// RdsRedoSpellTrap batch — family name: the Trap Cards the registrant's OPPONENT controls (Triangle Ecstasy Spark: Lua
        /// EFFECT_DISABLE target range (0, LOCATION_SZONE) on Traps + EFFECT_DISABLE_TRAPMONSTER (0, LOCATION_MZONE), until the
        /// End Phase). One-sided, so <see cref="IsOfFamily"/> never matches it: <see cref="OpponentTrapsNegatedFor"/> decides.
        /// </summary>
        public const string OpponentTrap = "OpponentTrap";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: the Chain Link being answered is the
        /// activation of a card of the family named by <see cref="EffectClause.ConditionName"/>
        /// (ygopro EVENT_CHAINING + <c>re:IsHasType(EFFECT_TYPE_ACTIVATE)</c>).
        /// </summary>
        public const string ChainLinkActivatesCardFamily = "ChainLinkActivatesCardFamily";

        static readonly string[] Families = { FieldSpell, ContinuousSpell, ContinuousTrap, EquipCard, OpponentTrap };

        /// <summary>True when <paramref name="def"/> is a card of <paramref name="family"/>.</summary>
        public static bool IsOfFamily(CardDef def, string family)
        {
            if (def == null || string.IsNullOrEmpty(family)) return false;
            switch (family)
            {
                case FieldSpell:
                    return def.IsFieldSpell;
                case ContinuousSpell:
                    return def.IsSpell && !def.IsTrap && def.IsContinuousSpellOrTrap;
                case ContinuousTrap:
                    return def.IsTrap && !def.IsSpell && def.IsContinuousSpellOrTrap;
                default:
                    return false;
            }
        }

        /// <summary>A family name this runtime knows (compile-time guard for the atoms).</summary>
        public static bool IsKnownFamily(string family) =>
            family == FieldSpell || family == ContinuousSpell || family == ContinuousTrap || family == EquipCard ||
            family == OpponentTrap;

        /// <summary>
        /// Activation condition: <paramref name="who"/> is answering a Chain Link (a chain response
        /// window is open) and that link is the activation of a card of <paramref name="family"/> —
        /// played from the hand or flipped from a Set, not an effect of a card that was already
        /// face-up. Checked when the Trap is activated only; the resolution applies regardless.
        /// </summary>
        public static bool AnsweredLinkActivatesFamily(DuelEngine engine, DuelistState who, string family)
        {
            var pr = engine?.PendingResponse;
            if (pr == null || pr.Timing != ResponseTiming.ChainResponse || pr.Responder != who) return false;
            var link = pr.ChainTarget;
            if (link == null || !ReferenceEquals(link, engine.Chain?.Last)) return false;
            return IsCardActivationOfFamily(link, family);
        }

        /// <summary>The link is the activation of a card of <paramref name="family"/> (not an effect).</summary>
        public static bool IsCardActivationOfFamily(ChainLink link, string family)
        {
            if (link?.Card?.Def == null || link.Resolved) return false;
            if (!link.FromHand && !link.WasSetOnField) return false;
            return IsOfFamily(link.Card.Def, family);
        }

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.NegateCardFamilyOnFieldThisTurn"/>: every card of
        /// <paramref name="family"/> on the field is negated for the rest of this turn. Not targeted;
        /// it does not need the answered card to still be on the field.
        /// <para>If that negation stops Remove Brainwashing from applying, a control Equip Card that is
        /// still equipped (Snatch Steal) takes control of its monster again for the turn (ygopro: the
        /// EFFECT_REMOVE_BRAINWASHING override ends and the Equip's EFFECT_SET_CONTROL applies); the
        /// snap-back returns in <see cref="OnTurnStart"/> once the negation ends.</para>
        /// </summary>
        public static void Register(DuelEngine engine, DuelistState who, CardInstance source, string family)
        {
            if (engine == null || who == null || !IsKnownFamily(family)) return;
            var brainwashingApplied = TextEffectRuntime.ReturnControlToOwnersApplies(engine);
            who.CardFamiliesNegatedThroughTurn ??= new Dictionary<string, int>();
            who.CardFamiliesNegatedThroughTurn[family] = engine.TurnNumber;
            engine.Log($"{source?.Name ?? "Card"}: {Describe(family)} on the field are negated this turn.");
            Refresh(engine);
            if (brainwashingApplied && !TextEffectRuntime.ReturnControlToOwnersApplies(engine))
                TextEffectRuntime.ReapplyEquipTakeControl(engine);
            // EquipModel batch: a negated control Equip (Snatch Steal) gives its monster back for the turn.
            if (family == EquipCard)
                EquipModelRuntime.SyncNegatedControlEquips(engine);
        }

        static string Describe(string family) => family switch
        {
            FieldSpell => "Field Spell Cards",
            ContinuousSpell => "Continuous Spell Cards",
            ContinuousTrap => "Continuous Trap Cards",
            EquipCard => "Equip Cards",
            OpponentTrap => "the opponent's Trap effects",
            _ => family
        };

        /// <summary>True while a negation of <paramref name="family"/> registered by either player lasts.</summary>
        public static bool FamilyNegatedThisTurn(DuelEngine engine, string family)
        {
            if (engine == null) return false;
            return NegatedBy(engine.Player, family, engine.TurnNumber) ||
                   NegatedBy(engine.Opponent, family, engine.TurnNumber);
        }

        static bool NegatedBy(DuelistState who, string family, int turn) =>
            who?.CardFamiliesNegatedThroughTurn != null &&
            who.CardFamiliesNegatedThroughTurn.TryGetValue(family, out var through) &&
            through >= turn && turn > 0;

        /// <summary>
        /// RdsRedoSpellTrap batch: the opponent of <paramref name="controller"/> registered <see cref="OpponentTrap"/> this turn,
        /// so <paramref name="controller"/>'s Trap effects on the field are negated (Triangle Ecstasy Spark).
        /// </summary>
        public static bool OpponentTrapsNegatedFor(DuelEngine engine, DuelistState controller)
        {
            if (engine == null || controller == null) return false;
            return NegatedBy(engine.OpponentOf(controller), OpponentTrap, engine.TurnNumber);
        }

        static bool AnyFamilyNegated(DuelEngine engine)
        {
            foreach (var f in Families)
                if (FamilyNegatedThisTurn(engine, f)) return true;
            return false;
        }

        /// <summary>
        /// Live check (does not rely on the last refresh): <paramref name="card"/> is face-up in a
        /// Spell &amp; Trap Zone or Field Zone and its family is negated this turn.
        /// </summary>
        public static bool IsNegatedByFamilyNow(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !card.FaceUp) return false;
            if (!IsInSpellTrapOrFieldZone(engine, card)) return false;
            // EquipModel batch (Armored Glass): an Equip Card is one by its state on the field, not its card type.
            if (FamilyNegatedThisTurn(engine, EquipCard) && EquipModelRuntime.IsEquipCardShape(card))
                return true;
            // RdsRedoSpellTrap batch (Triangle Ecstasy Spark): a Trap Card whose controller's opponent negated theirs.
            if (card.Def.IsTrap && OpponentTrapsNegatedFor(engine, engine.ControllerOf(card)))
                return true;
            foreach (var f in Families)
                if (IsOfFamily(card.Def, f) && FamilyNegatedThisTurn(engine, f))
                    return true;
            return false;
        }

        static bool IsInSpellTrapOrFieldZone(DuelEngine engine, CardInstance card)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                if (who.TryFindSpellTrap(card, out _)) return true;
                if (who.FieldSpellZone != null && ReferenceEquals(who.FieldSpellZone.Occupant, card)) return true;
            }

            return false;
        }

        /// <summary>
        /// Recompute <see cref="CardInstance.NegatedByTurnFamily"/>: true on face-up cards of a negated
        /// family in a Spell &amp; Trap Zone or Field Zone, false on every other card either player has
        /// (so a card that left the field, or a negation whose turn is over, never stays negated).
        /// Also drops a stale <see cref="CardInstance.ActivationUnresolvedOnChain"/>: it holds only while
        /// a Chain Link of that card is still on the chain.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine == null) return;
            var any = AnyFamilyNegated(engine);
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st == null) continue;
                    st.NegatedByTurnFamily = any && IsNegatedByFamilyNow(engine, st);
                    if (st.ActivationUnresolvedOnChain && !HasLinkOnChain(engine, st))
                        st.ActivationUnresolvedOnChain = false;
                }

                // RdsRedoSpellTrap batch (Triangle Ecstasy Spark): a face-up Trap Monster of a player whose opponent negated
                // their Trap effects this turn (EFFECT_DISABLE_TRAPMONSTER); every other monster is cleared.
                var trapMonstersNegated = any && OpponentTrapsNegatedFor(engine, who);
                foreach (var m in who.MonstersOnField())
                    if (m != null)
                        m.NegatedByTurnFamily = trapMonstersNegated && m.FaceUp && m.Def != null && m.Def.IsTrap;
                ClearAll(who.Hand);
                ClearAll(who.Graveyard);
                ClearAll(who.Banished);
            }
        }

        /// <summary>
        /// A Continuous Spell/Trap is placed face-up for an activation that stays on the chain (the
        /// opponent may respond before it resolves): mark it so its continuous effects do not apply
        /// until that activation resolves (ygopro STATUS_EFFECT_ENABLED). Dragon Capture Jar answered by
        /// Metal Detector never turns a Dragon to Defense Position; Level Limit - Area B answered by
        /// Mystic Probe never does either. DuelEngine clears it when the link resolves; <see cref="Refresh"/>
        /// clears it once no link of the card is left on the chain.
        /// </summary>
        public static void MarkActivationOnChain(CardInstance card, bool fromHand)
        {
            // Only the card's own activation (from the hand or a Set card being flipped), never an
            // effect of a Continuous card that is already face-up and applying.
            if (card?.Def != null && card.Def.IsContinuousSpellOrTrap && (fromHand || !card.FaceUp))
                card.ActivationUnresolvedOnChain = true;
        }

        static bool HasLinkOnChain(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && !l.Resolved && ReferenceEquals(l.Card, card)) return true;
            return false;
        }

        static void ClearAll(List<CardInstance> cards)
        {
            if (cards == null) return;
            foreach (var c in cards)
            {
                if (c == null) continue;
                c.NegatedByTurnFamily = false;
                c.ActivationUnresolvedOnChain = false;
            }
        }

        /// <summary>
        /// Start of a new turn (DuelEngine.BeginTurn, right after the turn number moves on): last
        /// turn's negations end. A continuous effect that only acts when something happens (Remove
        /// Brainwashing's control snap-back) is applied again now that it is no longer negated.
        /// </summary>
        public static void OnTurnStart(DuelEngine engine)
        {
            if (engine == null) return;
            var lifted = false;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                    if (st != null && st.NegatedByTurnFamily) lifted = true;
            }

            Refresh(engine);
            if (lifted)
                TextEffectRuntime.ReapplyReturnControlToOwners(engine);
            // EquipModel batch: Armored Glass ended — a control Equip it negated takes its monster again.
            if (lifted)
                EquipModelRuntime.SyncNegatedControlEquips(engine);
        }

        /// <summary>
        /// <c>TextEffectRuntime.ApplyClause</c> gate: <paramref name="source"/> is a face-up card of a
        /// family negated this turn, so this clause of its effect does nothing — at the resolution of
        /// its activation (the link Mystic Probe / Metal Detector / World Suppression was chained to,
        /// or a copy activated later this turn) and for any trigger or continuous clause it would
        /// apply. Costs are paid outside ApplyClause and are still paid; the card stays on the field.
        /// </summary>
        public static bool SkipsNegatedEffect(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (engine == null || source?.Def == null || clause == null) return false;
            if (!source.Def.IsSpell && !source.Def.IsTrap) return false;
            if (!IsNegatedByFamilyNow(engine, source)) return false;
            // Swords of Concealing Light: "Destroy this card during your 2nd Standby Phase after
            // activation" is set up when the card is activated and cannot be negated (ygopro
            // EFFECT_FLAG_CANNOT_DISABLE); the engine seeds that timer in this clause.
            if (clause.Action == EffectActionKind.SetAllOppMonstersFaceDownDefense &&
                source.ContinuousTurnsRemaining <= 0)
                source.ContinuousTurnsRemaining = clause.Amount > 0 ? clause.Amount : 2;
            source.NegatedByTurnFamily = true;
            engine.Log($"{source.Name}: its effects are negated this turn — no effect.");
            return true;
        }

        /// <summary>
        /// AI chain answer (SpellTrapEffects.AiAutoRespond): <paramref name="card"/> negates a card
        /// family, the link being answered is the opponent's activation of a card of that family, and
        /// the opponent has more face-up cards of that family on the field than the AI (the answered
        /// card included), so the negation costs them more than it costs the AI.
        /// </summary>
        public static bool AiWantsToAnswer(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            var clause = prog?.ClausesFor(EffectTiming.Activate)?.Find(c =>
                c != null && c.Action == EffectActionKind.NegateCardFamilyOnFieldThisTurn);
            if (clause == null) return false;
            var link = engine.PendingResponse?.ChainTarget;
            if (link == null || link.Controller == null || link.Controller == ai) return false;
            if (!IsCardActivationOfFamily(link, clause.NegatedCardFamily)) return false;
            return CountFaceUp(link.Controller, clause.NegatedCardFamily) > CountFaceUp(ai, clause.NegatedCardFamily);
        }

        static int CountFaceUp(DuelistState who, string family)
        {
            var n = 0;
            if (who == null) return 0;
            foreach (var st in who.SpellTrapsOnField())
                if (st != null && st.FaceUp && IsOfFamily(st.Def, family)) n++;
            return n;
        }

        /// <summary>
        /// A card linked to a monster (Call of the Haunted, Spellbinding Circle, Battle-Scarred, Soul
        /// Resurrection) whose "when that monster is destroyed / removed, destroy this card" is an
        /// effect of a Continuous Spell/Trap: while that card's effects are negated it stays on the
        /// field when the monster leaves. Equip Spells (and monsters equipped as Equip Cards) still go
        /// to the GY — that is a game rule, not an effect.
        /// <para>The test is on the card's type, not <c>TextEffectRuntime.IsRealEquipCard</c>: the engine
        /// compiles Battle-Scarred as EquipThisToTarget, but it is a persistent Continuous Trap, not an
        /// Equip Card (ygopro c94463200: aux.AddPersistentProcedure; the self-destroy is effect e3,
        /// Duel.Destroy REASON_EFFECT, not CANNOT_DISABLE), so while negated it stays too.</para>
        /// </summary>
        public static bool NegatedLinkStaysWhenHostLeaves(CardInstance linkCard) =>
            linkCard?.Def != null && linkCard.IsNegated && linkCard.Def.IsContinuousSpellOrTrap &&
            !linkCard.Def.IsEquipSpell && !linkCard.Def.IsMonster;
    }
}
