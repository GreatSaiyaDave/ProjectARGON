using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellTrapDestroyedResponse" (v142) runtime.
    /// <para><b>Gust / Driving Snow</b> (ygopro c73079365 / c473469): Normal Traps activated on EVENT_TO_GRAVE when
    /// 1 or more of the activating player's Spell (Gust) / Trap (Driving Snow) Cards were destroyed by a card
    /// effect (REASON_DESTROY + REASON_EFFECT) the opponent controls (<c>rp ~= tp</c>) and sent from the field to
    /// the GY. How the engine models that event:</para>
    /// <list type="bullet">
    /// <item><see cref="NoteDestroyedByEffect"/> records the card on its controller
    /// (<see cref="DuelistState.SpellTrapsDestroyedByOpponent"/>) when a card effect destroys it
    /// (TextEffectRuntime.DestroyCard — every compiled Destroy clause — and a Counter Trap's "negate … and destroy
    /// it", DuelEngine.ApplyActivationNegation). Only Spells / Traps that were on the field, ended in the GY (not
    /// banished) and were destroyed by the other player's effect are recorded.</item>
    /// <item>Once nothing is resolving or open (DuelEngine.OfferSpellTrapDestroyedWindow, from Notify), each such
    /// player — the turn player first — gets a <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window
    /// (<see cref="BeginWindow"/>). Only cards whose Activate clause carries the
    /// <see cref="YourCardsDestroyedByOpponentEffect"/> activation condition are legal there, and that condition
    /// (<see cref="WindowAnswers"/>) holds only inside that window, for the matching card kind. Only the last
    /// thing to happen can be answered, so missed = gone: the records are dropped when the window closes, when
    /// nobody has a legal answer, when another Chain Link resolves after the destruction, when a Summon succeeds
    /// after its negation chain, when a queued FLIP effect resolves (Swords of Revealing Light), when a declared
    /// attack moves on to the Damage Step, and when the phase ends before the window could open; a destruction in
    /// the Damage Step is not recorded (neither Trap can be activated there).</item>
    /// <item>The Trap then resolves like any response seated on the chain (TextEffectRuntime.SeatResponseOnChain):
    /// "Destroy 1 Spell or Trap Card on the field" targets (EFFECT_FLAG_CARD_TARGET) a Spell/Trap other than itself
    /// (<see cref="EffectClause.ExceptThisCard"/>), chosen at activation; the opponent may chain; a target that left
    /// the field is not destroyed.</item>
    /// </list>
    /// <para><b>Weather Report</b> (c72053645): see <see cref="GrantBattlePhaseTwice"/> and
    /// <see cref="SecondBattlePhaseAvailable"/> (DuelEngine.CanConductBattlePhase / TryEnterBattlePhase).</para>
    /// </summary>
    public static class SpellTrapDestroyedResponseRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: a <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/>
        /// window is open for the activating player and 1 or more of the destroyed cards it answers are of the kind
        /// named by <see cref="EffectClause.ConditionName"/> (<see cref="SpellCards"/> / <see cref="TrapCards"/>).
        /// </summary>
        public const string YourCardsDestroyedByOpponentEffect = "YourCardsDestroyedByOpponentEffect";

        /// <summary>Card kind for <see cref="EffectClause.ConditionName"/>: Spell Cards (Gust).</summary>
        public const string SpellCards = "Spell";

        /// <summary>Card kind for <see cref="EffectClause.ConditionName"/>: Trap Cards (Driving Snow).</summary>
        public const string TrapCards = "Trap";

        static bool IsOfKind(CardDef def, string kind) =>
            def != null && ((kind == SpellCards && def.IsSpell) || (kind == TrapCards && def.IsTrap));

        // ───────────────────────── the event ─────────────────────────

        /// <summary>
        /// Records <paramref name="card"/> for its controller's Gust / Driving Snow window after a card effect of
        /// <paramref name="byPlayer"/> destroyed it. Call right after the destruction, only for a card that was on
        /// the field. Nothing is recorded when the destroying effect is the controller's own, when the card is not
        /// a Spell/Trap, when it is still on the field (not destroyed) or when it did not reach a GY (banished).
        /// </summary>
        public static void NoteDestroyedByEffect(DuelEngine engine, DuelistState victim, CardInstance card,
            DuelistState byPlayer)
        {
            if (engine == null || victim == null || byPlayer == null || card?.Def == null) return;
            if (ReferenceEquals(byPlayer, victim)) return;
            if (!card.Def.IsSpell && !card.Def.IsTrap) return;
            if (engine.ControllerOf(card) != null) return;
            if (!InAnyGraveyard(engine, card)) return;
            // Neither Trap can be activated in the Damage Step (no EFFECT_FLAG_DAMAGE_STEP), and the moment is gone
            // once it ends: a destruction there (a Flip effect by battle) is never answered.
            if (engine.BattleStep == BattleStep.DamageStep) return;
            if (victim.SpellTrapsDestroyedByOpponentTurn != engine.TurnNumber ||
                victim.SpellTrapsDestroyedByOpponentPhase != engine.Phase)
            {
                victim.SpellTrapsDestroyedByOpponent.Clear();
                victim.SpellTrapsDestroyedByOpponentTurn = engine.TurnNumber;
                victim.SpellTrapsDestroyedByOpponentPhase = engine.Phase;
            }

            if (!victim.SpellTrapsDestroyedByOpponent.Contains(card))
                victim.SpellTrapsDestroyedByOpponent.Add(card);
        }

        static bool InAnyGraveyard(DuelEngine engine, CardInstance card) =>
            (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) ||
            (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card));

        /// <summary>True when a destruction is waiting for its window (either player).</summary>
        public static bool HasPending(DuelEngine engine) =>
            engine != null &&
            ((engine.Player != null && engine.Player.SpellTrapsDestroyedByOpponent.Count > 0) ||
             (engine.Opponent != null && engine.Opponent.SpellTrapsDestroyedByOpponent.Count > 0));

        /// <summary>
        /// Moves <paramref name="victim"/>'s recorded destructions into the window list the open window answers —
        /// only those of this turn and phase (a destruction whose window could not open before the phase ended is
        /// missed). Returns false when there is nothing (left) to answer.
        /// </summary>
        public static bool BeginWindow(DuelEngine engine, DuelistState victim)
        {
            if (engine == null || victim == null) return false;
            victim.SpellTrapsDestroyedWindow.Clear();
            if (victim.SpellTrapsDestroyedByOpponent.Count == 0) return false;
            if (victim.SpellTrapsDestroyedByOpponentTurn == engine.TurnNumber &&
                victim.SpellTrapsDestroyedByOpponentPhase == engine.Phase)
                victim.SpellTrapsDestroyedWindow.AddRange(victim.SpellTrapsDestroyedByOpponent);
            victim.SpellTrapsDestroyedByOpponent.Clear();
            return victim.SpellTrapsDestroyedWindow.Count > 0;
        }

        /// <summary>
        /// The recorded destructions can no longer be answered: another Chain Link resolves after them (DuelEngine
        /// ResolveChainStack — only the last thing to happen can be answered), a Summon succeeds after its negation
        /// chain (DuelEngine FinishSummonNegationWindow), a queued FLIP effect resolves (DuelEngine
        /// FlushFlipEffectsAfterResolution), or a declared attack moved on to the Damage Step before the window could
        /// open.
        /// </summary>
        public static void DropPending(DuelEngine engine)
        {
            engine?.Player?.SpellTrapsDestroyedByOpponent.Clear();
            engine?.Opponent?.SpellTrapsDestroyedByOpponent.Clear();
        }

        /// <summary>The window closed (passed, answered, or never opened): its destructions can no longer be answered.</summary>
        public static void EndWindows(DuelEngine engine)
        {
            engine?.Player?.SpellTrapsDestroyedWindow.Clear();
            engine?.Opponent?.SpellTrapsDestroyedWindow.Clear();
        }

        /// <summary>Drops every record (the duel ended).</summary>
        public static void ClearAll(DuelEngine engine)
        {
            foreach (var d in new[] { engine?.Player, engine?.Opponent })
            {
                if (d == null) continue;
                d.SpellTrapsDestroyedByOpponent.Clear();
                d.SpellTrapsDestroyedWindow.Clear();
            }
        }

        // ───────────────────────── activation condition ─────────────────────────

        /// <summary>
        /// Activation condition <see cref="YourCardsDestroyedByOpponentEffect"/>: <paramref name="who"/>'s
        /// <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window is open and 1 or more of the cards it
        /// answers are <paramref name="kind"/> Cards. Checked when the Trap is activated only.
        /// </summary>
        public static bool WindowAnswers(DuelEngine engine, DuelistState who, string kind)
        {
            var pr = engine?.PendingResponse;
            if (who == null || pr == null || pr.Timing != ResponseTiming.SpellTrapDestroyedByOpponent ||
                pr.Responder != who)
                return false;
            foreach (var c in who.SpellTrapsDestroyedWindow)
                if (c?.Def != null && IsOfKind(c.Def, kind))
                    return true;
            return false;
        }

        /// <summary>The program answers a <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window.</summary>
        public static bool AnswersDestroyedWindow(CompiledCardProgram prog)
        {
            if (prog == null) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && c.ActivationCondition == YourCardsDestroyedByOpponentEffect)
                    return true;
            return false;
        }

        /// <summary>
        /// AI: answer with Gust / Driving Snow only when the opponent has a card in their Spell &amp; Trap Zones to
        /// destroy, never to destroy one of its own cards. The target auto-pick (TextEffectRuntime.AutoPick,
        /// FieldSpellTraps) prefers exactly those cards (<see cref="DuelistState.TryFindSpellTrap"/>) and otherwise
        /// falls back to the first legal target, which is one of the AI's own cards — so an opponent whose only
        /// Spell/Trap is a Field Spell is not a reason to answer (the AI passes).
        /// </summary>
        public static bool AiWantsToAnswer(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            if (!AnswersDestroyedWindow(CompiledEffectCache.GetOrCompile(card.Def))) return false;
            var opp = engine.OpponentOf(who);
            if (opp?.SpellTrapZones == null) return false;
            foreach (var zone in opp.SpellTrapZones)
                if (zone?.Occupant != null)
                    return true;
            return false;
        }

        // ───────────────────────── Weather Report: Battle Phase twice ─────────────────────────

        /// <summary>
        /// "you can perform your Battle Phase twice this turn (or your next turn, if activated during your
        /// opponent's turn)". ygopro c72053645: EFFECT_BP_TWICE on the controller; resolved during their own turn
        /// it applies this turn, except during their own Battle Phase, where it applies to their next turn (Lua
        /// <c>bpcon</c>: turn count ≠ this turn); resolved during the opponent's turn it applies to the
        /// controller's next turn.
        /// </summary>
        public static void GrantBattlePhaseTwice(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            int turn;
            if (engine.TurnPlayer == who)
                turn = engine.Phase == DuelPhase.Battle ? engine.TurnNumber + 2 : engine.TurnNumber;
            else
                turn = engine.TurnNumber + 1;
            who.BattlePhaseTwiceTurns.RemoveAll(t => t < engine.TurnNumber);
            if (!who.BattlePhaseTwiceTurns.Contains(turn))
                who.BattlePhaseTwiceTurns.Add(turn);
            engine.Log(turn == engine.TurnNumber
                ? $"{source?.Name ?? "Effect"}: {who.Name} can conduct their Battle Phase twice this turn."
                : $"{source?.Name ?? "Effect"}: {who.Name} can conduct their Battle Phase twice during their next turn.");
        }

        /// <summary>
        /// <paramref name="who"/> is the turn player, in Main Phase 2 after their first Battle Phase of a turn in
        /// which they may conduct it twice, and has not entered the second one yet.
        /// </summary>
        public static bool SecondBattlePhaseAvailable(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && engine.TurnPlayer == who && engine.Phase == DuelPhase.Main2 &&
            who.BattlePhaseTwiceTurns.Contains(engine.TurnNumber) &&
            who.SecondBattlePhaseTurn != engine.TurnNumber;

        /// <summary>
        /// Entering the second Battle Phase: every monster of the turn player may attack again (its attack count
        /// restarts), but it still attacked this turn (<see cref="CardInstance.AttackedThisTurn"/> stays, so it
        /// cannot change its battle position).
        /// </summary>
        public static void BeginSecondBattlePhase(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            who.SecondBattlePhaseTurn = engine.TurnNumber;
            foreach (var m in who.MonstersOnField())
                if (m != null)
                    m.AttacksDeclaredThisTurn = 0;
        }
    }
}
