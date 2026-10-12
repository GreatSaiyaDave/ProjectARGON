using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PositionLocks" runtime: battle-position changes with rest-of-turn locks. Reads compiled clauses
    /// only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Aqua Spirit / Garuda the Wind Spirit</b>
    /// (<see cref="EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase"/>, an optional Standby / End Phase trigger
    /// in the opponent's turn). TextEffectRuntime.FirePhaseTriggersFor applies the clause with no target, which lands
    /// in <see cref="OnPhaseTrigger"/>: the AI picks at once (<see cref="AiPick"/>: the strongest face-up Attack
    /// Position monster, turned to Defense; nothing else is worth it); a human's choice is queued and asked once the
    /// phase's other triggers and upkeep choices are done (<see cref="OfferQueuedPick"/>, called by DuelEngine.BeginTurn /
    /// ResumeStandbyAfterUpkeep / ContinueStandbyAfterUpkeepChoices for the Standby Phase and by DuelEngine.EndTurn /
    /// ResumeEndTurnAfterTrigger for the End Phase). The phase waits for the answer: the Standby Phase through the
    /// turn player's Standby pause, the End Phase through its trigger pause (SimpleAi waits on both). The pick is an
    /// ordinary text-program target (<see cref="EffectTargetKind.OppFaceUpMonster"/>, <see cref="TryResolvePick"/> /
    /// <see cref="TryCancelPick"/>; Cancel = the effect is not used); then the phase goes on. The change resolves in
    /// <see cref="ChangeTargetPosition"/>; Aqua Spirit's <see cref="EffectClause.LocksPositionRestOfTurn"/> locks that
    /// monster for the rest of the turn.</item>
    /// <item><b>Curse of Fiend</b> (<see cref="EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn"/>). Activated
    /// only while Set, during its controller's Standby Phase (<see cref="SetInYourStandbyPhase"/> +
    /// <see cref="EffectClause.OnlyDuringOwnStandby"/>); a human's Standby Phase holds for it
    /// (<see cref="HasStandbyOnlySetCard"/>, read by TextEffectRuntime.HasStandbyOnlyIgnition). It can never be answered
    /// onto a Chain (the Standby-only gate refuses a chain response), so it is always Chain Link 1.
    /// <see cref="ChangeAllPositionsAndLock"/> changes every monster at once, queues the FLIP effects of the ones it
    /// flipped and the "position changed" triggers it caused (DuelEngine.QueueFlipEffectAfterResolution,
    /// <see cref="SimultaneousTriggersRuntime"/>: once this card has resolved they go on one Chain, SEGOC, in the
    /// Standby hold) and locks the whole field for the rest of the turn.</item>
    /// <item><b>The locks</b> (<see cref="ManualPositionChangeLocked"/>, read by DuelEngine.CanChangePosition /
    /// CanFlipSummon / IsBattlePositionLocked): ygopro EFFECT_CANNOT_CHANGE_POSITION forbids manual changes and Flip
    /// Summons, never a card effect's change. Aqua Spirit's lock follows one monster (it ends when that monster leaves
    /// the field, not when it is turned face-down); Curse of Fiend's covers every monster on the field this turn,
    /// including monsters that arrive later.</item>
    /// </list>
    /// State is kept per engine and tied to this Duel's duelists, so a restarted Duel starts clean.
    /// </summary>
    public static class PositionLocksRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Curse of Fiend): the card is Set in its controller's Spell
        /// &amp; Trap Zone, it is that player's Standby Phase, and a monster is on the field.
        /// </summary>
        public const string SetInYourStandbyPhase = "SetInYourStandbyPhase";

        /// <summary>Aqua Spirit's lock on one monster: this field presence, this turn.</summary>
        sealed class CardLock
        {
            public int Turn;
            public int EntryStamp;
        }

        /// <summary>A human's Aqua Spirit / Garuda trigger waiting to be asked in this phase.</summary>
        sealed class QueuedChange
        {
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public int Turn;
            public DuelPhase Phase;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, CardLock> CardLocks = new();
            /// <summary>Curse of Fiend: the player whose activation locked the field (null = none) …</summary>
            public DuelistState FieldLockBy;
            /// <summary>… and the turn it lasts through.</summary>
            public int FieldLockTurn;
            public readonly List<QueuedChange> Queue = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        /// <summary>The open target picks this batch asked, with the trigger each one answers.</summary>
        static readonly ConditionalWeakTable<PendingActivation, QueuedChange> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary><paramref name="who"/> is a duelist of the Duel <paramref name="engine"/> is running now.</summary>
        static bool InThisDuel(DuelEngine engine, DuelistState who) =>
            who != null && (ReferenceEquals(who, engine.Player) || ReferenceEquals(who, engine.Opponent));

        // ───────────────────────── Aqua Spirit / Garuda: the trigger ─────────────────────────

        /// <summary>
        /// TextEffectRuntime.ApplyClause arm with no target: the Standby / End Phase trigger fired for
        /// <paramref name="who"/> (TextEffectRuntime.FirePhaseTriggersFor, the opponent's turn only). The AI decides at
        /// once; a human is asked by <see cref="OfferQueuedPick"/> once the phase's other triggers are done.
        /// </summary>
        public static void OnPhaseTrigger(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            var phase = PhaseOf(clause);
            if (phase == DuelPhase.Draw || engine.Phase != phase || engine.TurnPlayer == who) return;
            if (!SourceActive(engine, who, source)) return;
            var targets = Targets(engine, who, source, clause);
            if (targets.Count == 0)
            {
                engine.Log($"{source.Name}: no face-up monster your opponent controls — the effect is not used.");
                return;
            }

            if (!engine.IsHumanControlled(who))
            {
                var pick = AiPick(targets);
                if (pick == null)
                {
                    engine.Log($"{source.Name}: {who.Name} does not use its effect.");
                    return;
                }

                engine.Log($"{source.Name}: {who.Name} activates its effect, targeting {pick.Name}.");
                TextEffectRuntime.ApplyChosenClause(engine, who, source, clause, pick);
                return;
            }

            var st = StateOf(engine);
            // Entries left from an earlier phase or Duel are never asked.
            st.Queue.RemoveAll(q => q.Turn != engine.TurnNumber || q.Phase != engine.Phase || !InThisDuel(engine, q.Who));
            if (st.Queue.Exists(q => ReferenceEquals(q.Source, source) && ReferenceEquals(q.Clause, clause))) return;
            st.Queue.Add(new QueuedChange
            {
                Who = who,
                Source = source,
                Clause = clause,
                Turn = engine.TurnNumber,
                Phase = phase
            });
        }

        /// <summary>
        /// The AI's use: turn the opponent's strongest face-up Attack Position monster to Defense Position (it cannot
        /// attack this turn, or is left in Defense for the AI's own turn). No Attack Position target: not used.
        /// </summary>
        public static CardInstance AiPick(List<CardInstance> targets) =>
            targets?
                .Where(t => t != null && t.FaceUp && t.Position == BattlePosition.Attack)
                .OrderByDescending(t => t.CurrentAtk)
                .ThenBy(t => t.Name)
                .FirstOrDefault();

        static DuelPhase PhaseOf(EffectClause clause) =>
            clause.Timing == EffectTiming.StandbyPhase ? DuelPhase.Standby
            : clause.Timing == EffectTiming.EndPhase ? DuelPhase.End
            : DuelPhase.Draw; // not a phase trigger

        /// <summary>The trigger's card is still a face-up, not negated monster its controller controls.</summary>
        static bool SourceActive(DuelEngine engine, DuelistState who, CardInstance source) =>
            source != null && source.FaceUp && !source.IsNegated && who != null &&
            who.TryFindMonster(source, out _) &&
            !TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, source);

        /// <summary>Legal targets: face-up monsters the opponent controls that its card effects may target.</summary>
        static List<CardInstance> Targets(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause) =>
            TextEffectRuntime.CollectChoiceTargets(engine, who, clause, source)
                .Where(t => t != null && t.FaceUp && ReferenceEquals(engine.ControllerOf(t), engine.OpponentOf(who)))
                .Distinct()
                .ToList();

        // ───────────────────────── Aqua Spirit / Garuda: the human's choice ─────────────────────────

        /// <summary>
        /// Ask the next queued human trigger of this phase (the target pick; Cancel declines). False when nothing is
        /// asked: the queue is empty, another choice or window is open, or no queued trigger can still be used.
        /// </summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Queue.Count == 0) return false;
            if (engine.GameOver)
            {
                st.Queue.Clear();
                return false;
            }

            if (engine.PendingActivation != null || engine.IsAwaitingResponse) return false;
            while (st.Queue.Count > 0)
            {
                var q = st.Queue[0];
                st.Queue.RemoveAt(0);
                if (!InThisDuel(engine, q.Who) || q.Turn != engine.TurnNumber || engine.Phase != q.Phase ||
                    engine.TurnPlayer == q.Who)
                    continue;
                if (!SourceActive(engine, q.Who, q.Source))
                {
                    engine.Log($"{q.Source?.Name}: no longer face-up on the field — the effect is not used.");
                    continue;
                }

                var targets = Targets(engine, q.Who, q.Source, q.Clause);
                if (targets.Count == 0)
                {
                    engine.Log($"{q.Source.Name}: no face-up monster your opponent controls — the effect is not used.");
                    continue;
                }

                var pending = new PendingActivation
                {
                    Controller = q.Who,
                    Card = q.Source,
                    TargetKind = EffectTargetKind.OppFaceUpMonster,
                    IsMonsterEffect = true,
                    UsesTextProgram = true,
                    TargetPicksRemaining = 1,
                    PromptOverride = q.Clause.LocksPositionRestOfTurn
                        ? "you can change the battle position of 1 face-up monster your opponent controls (it must " +
                          "stay in that position for the rest of the turn) — tap it, or CANCEL."
                        : "you can change the battle position of 1 face-up monster your opponent controls — tap it, " +
                          "or CANCEL."
                };
                pending.LegalTargets.AddRange(targets);
                s_picks.Add(pending, q);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveMonsterTextTarget hook: the human picked the monster for their queued trigger.
        /// False when the open pick is not this batch's.
        /// </summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var q)) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            if (pick == null)
            {
                engine.Log("Choose one of the highlighted monsters, or CANCEL.");
                return true;
            }

            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            engine.Log($"{q.Source?.Name}: {q.Who?.Name} activates its effect, targeting {pick.Name}.");
            TextEffectRuntime.ApplyChosenClause(engine, q.Who, q.Source, q.Clause, pick);
            ok = true;
            GoOn(engine, q);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the human does not use their queued trigger.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_picks.TryGetValue(pending, out var q)) return false;
            s_picks.Remove(pending);
            engine.ClearPendingActivation();
            engine.Log($"{q.Source?.Name}: {q.Who?.Name} does not use its effect.");
            GoOn(engine, q);
            return true;
        }

        /// <summary>After an answer: ask the next queued trigger, or let the waiting phase go on.</summary>
        static void GoOn(DuelEngine engine, QueuedChange answered)
        {
            if (engine.GameOver) return;
            if (OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            if (answered.Phase == DuelPhase.Standby)
                engine.ContinueStandbyAfterUpkeepChoices();
            else if (answered.Phase == DuelPhase.End)
                engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
        }

        // ───────────────────────── Aqua Spirit / Garuda: the resolution ─────────────────────────

        /// <summary>
        /// TextEffectRuntime.ApplyClause arm with the chosen target: change its battle position (face-up Attack ↔
        /// face-up Defense) if it is still a face-up monster on the field (Lua: IsRelateToEffect and IsFaceup), then
        /// lock it for the rest of the turn when the clause says so.
        /// </summary>
        public static void ChangeTargetPosition(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || clause == null || target == null) return;
            if (engine.ControllerOf(target) == null || !target.FaceUp)
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer a face-up monster on the field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var to = target.Position == BattlePosition.Attack ? BattlePosition.Defense : BattlePosition.Attack;
            engine.ApplyEffectPositionChange(target, to, faceUp: true);
            engine.Log($"{source?.Name}: {target.Name} → {to} Position.");
            if (clause.LocksPositionRestOfTurn && engine.ControllerOf(target) != null)
            {
                StateOf(engine).CardLocks[target] = new CardLock
                {
                    Turn = engine.TurnNumber,
                    EntryStamp = target.FieldEntryStamp
                };
                engine.Log($"{target.Name} must remain in this position for the rest of the turn.");
            }

            // "This card's position changed" effects (Dream Clown / Crass Clown) of the changed monster, afterwards.
            TextEffectRuntime.NotifyPositionChanged(engine, target, toDefense: to == BattlePosition.Defense);
        }

        // ───────────────────────── Curse of Fiend ─────────────────────────

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet hook for <see cref="SetInYourStandbyPhase"/>. False when
        /// <paramref name="c"/> does not carry it.
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || c.ActivationCondition != SetInYourStandbyPhase) return false;
            reason = "Only while Set in your Spell & Trap Zone during your Standby Phase, with a monster on the field.";
            ok = engine != null && who != null && card != null &&
                 engine.Phase == DuelPhase.Standby && engine.TurnPlayer == who &&
                 who.TryFindSpellTrap(card, out _) && !card.FaceUp &&
                 AnyMonsterOnField(engine);
            return true;
        }

        static bool AnyMonsterOnField(DuelEngine engine) =>
            (engine.Player?.MonsterCount ?? 0) + (engine.Opponent?.MonsterCount ?? 0) > 0;

        /// <summary>
        /// TextEffectRuntime.HasStandbyOnlyIgnition: <paramref name="who"/> has a Set card whose activation is legal
        /// only during their Standby Phase (Curse of Fiend) and it can be activated now, so a human's Standby Phase holds
        /// for it (DuelEngine.BeginTurn, left with MAIN 1).
        /// </summary>
        public static bool HasStandbyOnlySetCard(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var st in who.SpellTrapsOnField().ToList())
            {
                if (st?.Def == null || st.FaceUp) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                if (!prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && c.OnlyDuringOwnStandby)) continue;
                if (engine.CanActivateSpellTrap(who, st, fromHand: false)) return true;
            }

            return false;
        }

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn"/>: every monster on the
        /// field changes position at the same time — face-up Attack → face-up Defense, face-up or face-down Defense →
        /// face-up Attack. The FLIP effects of the monsters it flipped face-up activate once this resolution (and its
        /// Chain) is over. Then no monster on the field can change its battle position for the rest of the turn except
        /// by a card effect (registered even when no monster changed, as the Lua operation does).
        /// </summary>
        public static void ChangeAllPositionsAndLock(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null) return;
            // Every monster's new position is decided before any of them changes (one simultaneous change).
            var plan = new List<(DuelistState Side, CardInstance Monster, BattlePosition To, bool WasFaceDown)>();
            foreach (var side in new[] { who, engine.OpponentOf(who) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField().ToList())
                {
                    if (m == null) continue;
                    var wasFaceDown = !m.FaceUp;
                    var to = !wasFaceDown && m.Position == BattlePosition.Attack
                        ? BattlePosition.Defense
                        : BattlePosition.Attack;
                    plan.Add((side, m, to, wasFaceDown));
                }
            }

            // One simultaneous change: the "position changed" triggers it causes (Dream Clown) and the FLIP effects
            // of the monsters it flipped face-up all wait, then go on one Chain together once this card has resolved
            // (SimultaneousTriggers batch, SEGOC).
            var changed = new List<(CardInstance Monster, bool ToDefense)>();
            SimultaneousTriggersRuntime.Capture(engine, () =>
            {
                foreach (var (side, m, to, wasFaceDown) in plan)
                {
                    if (engine.ControllerOf(m) != side) continue;
                    if (!engine.ApplyEffectPositionChange(m, to, faceUp: true)) continue;
                    changed.Add((m, to == BattlePosition.Defense));
                    if (wasFaceDown)
                        engine.QueueFlipEffectAfterResolution(side, m);
                }
            });

            engine.Log(changed.Count > 0
                ? $"{source?.Name}: " + string.Join(", ", changed.Select(x =>
                    $"{x.Monster.Name} → {(x.ToDefense ? "Defense" : "Attack")}")) + "."
                : $"{source?.Name}: no monster changed its battle position.");
            var st = StateOf(engine);
            st.FieldLockBy = who;
            st.FieldLockTurn = engine.TurnNumber;
            engine.Log("Battle positions cannot be changed for the rest of this turn, except by card effects.");

            // "This card's position changed" effects of the changed monsters join the same Chain.
            foreach (var (m, toDefense) in changed)
                if (m.FaceUp && engine.ControllerOf(m) != null)
                    SimultaneousTriggersRuntime.QueueThisCardPositionChanged(engine, m, toDefense);
        }

        // ───────────────────────── The locks ─────────────────────────

        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161), Creature Swap: <paramref name="card"/> cannot change its battle
        /// position (nor be Flip Summoned) for the rest of this turn — ygopro EFFECT_CANNOT_CHANGE_POSITION reset at the
        /// End Phase; Aqua Spirit's per-card lock (this field presence, this turn).
        /// </summary>
        public static void LockRestOfTurn(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || engine.ControllerOf(card) == null) return;
            StateOf(engine).CardLocks[card] = new CardLock
            {
                Turn = engine.TurnNumber,
                EntryStamp = card.FieldEntryStamp
            };
        }

        /// <summary>
        /// DuelEngine.CanChangePosition / CanFlipSummon / IsBattlePositionLocked: this batch forbids changing
        /// <paramref name="card"/>'s battle position by hand (manual change or Flip Summon) right now — Curse of Fiend's
        /// field-wide lock this turn, or Aqua Spirit's lock on this monster.
        /// </summary>
        public static bool ManualPositionChangeLocked(DuelEngine engine, CardInstance card)
        {
            // BattleAndPositionRecords batch: The Unhappy Girl's battle partners, Judgment of the Desert's flipped monsters.
            if (BattleAndPositionRecordsRuntime.ManualPositionChangeLocked(engine, card)) return true;
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return false;
            if (engine.ControllerOf(card) == null) return false;
            if (st.FieldLockBy != null && InThisDuel(engine, st.FieldLockBy) && st.FieldLockTurn == engine.TurnNumber)
                return true;
            if (!st.CardLocks.TryGetValue(card, out var l)) return false;
            if (l.Turn != engine.TurnNumber || l.EntryStamp != card.FieldEntryStamp)
            {
                st.CardLocks.Remove(card);
                return false;
            }

            return true;
        }
    }
}
