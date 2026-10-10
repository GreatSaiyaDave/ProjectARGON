using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattlePhaseEnd" (v142) runtime: end-of-Battle-Phase and battle-aftermath effects. Reads
    /// compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><see cref="FireEndOfBattlePhase"/> (DuelEngine.EndBattlePhase, the End Step of every Battle Phase):
    /// the face-up monsters' <see cref="EffectTiming.EndOfBattlePhase"/> clauses, the turn player's first. Like the
    /// engine's other monster battle triggers they resolve inline. None of them opens a window or a choice
    /// (DuelEngine.EndTurn does not wait for one after the End Step).</item>
    /// <item>Goblin Attack Force / Giant Orc (<see cref="EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition"/>):
    /// the position lock is read by DuelEngine.CanChangePosition through <see cref="ManualPositionChangeLocked"/>.</item>
    /// <item>Steel Scorpion (<see cref="EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase"/>): a countdown on
    /// the attacker, ticked each End Phase by <see cref="TickDelayedDestroys"/>.</item>
    /// <item>Reflect Bounder (<see cref="EffectTiming.BeforeDamageCalculation"/>): DuelEngine's damage calculation
    /// calls <see cref="FireBeforeDamageCalculation"/> right before damage is calculated and
    /// <see cref="DestroyAfterDamageCalculation"/> after it.</item>
    /// </list>
    /// State that must outlive one call (locks, countdowns) is kept per engine.
    /// </summary>
    public static class BattlePhaseEndRuntime
    {
        /// <summary>A Goblin Attack Force lock: this field presence, not turned face-down since, through this turn.</summary>
        sealed class PositionLock
        {
            public int ThroughTurn;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        /// <summary>A Steel Scorpion countdown on one attacker (ygopro flag effect with a label counter).</summary>
        sealed class DelayedDestroy
        {
            public CardInstance Target;
            public CardInstance Source;
            /// <summary>The source's controller when it applied (ygopro owner player): only the other player's End Phases count.</summary>
            public DuelistState SourceController;
            public int Count;
            public int Needed;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, PositionLock> PositionLocks = new();
            public readonly List<DelayedDestroy> DelayedDestroys = new();
            /// <summary>Re-entrancy guard: an effect resolving in the End Step may end the Battle Phase again.</summary>
            public bool FiringEndOfBattlePhase;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────── At the end of the Battle Phase ─────────────────────

        /// <summary>
        /// The End Step of the Battle Phase (DuelEngine.EndBattlePhase, once per Battle Phase, however it ends):
        /// every face-up, non-negated monster with a FullyCompiled <see cref="EffectTiming.EndOfBattlePhase"/> clause,
        /// the turn player's monsters first.
        /// </summary>
        public static void FireEndOfBattlePhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            var turn = engine.TurnPlayer;
            if (turn == null) return;
            var st = StateOf(engine);
            if (st.FiringEndOfBattlePhase) return;
            st.FiringEndOfBattlePhase = true;
            var any = false;
            try
            {
                foreach (var who in new[] { turn, engine.OpponentOf(turn) })
                {
                    if (who == null) continue;
                    foreach (var card in who.MonstersOnField().ToList())
                    {
                        if (engine.GameOver) return;
                        if (card?.Def == null || !card.FaceUp || card.IsNegated) continue;
                        if (!who.TryFindMonster(card, out _)) continue; // left during an earlier resolution
                        var prog = CompiledEffectCache.GetOrCompile(card.Def);
                        if (prog == null || !prog.FullyCompiled) continue;
                        foreach (var c in prog.ClausesFor(EffectTiming.EndOfBattlePhase))
                        {
                            if (c == null) continue;
                            if (engine.GameOver || !card.FaceUp || !who.TryFindMonster(card, out _)) break;
                            any |= ResolveEndOfBattlePhase(engine, who, card, c);
                        }
                    }
                }

                // BattleDestroyedGyRecovery batch (v162): Helpoemer — at the end of the opponent's Battle Phase, from
                // the GY it was destroyed into by battle.
                if (!engine.GameOver)
                    any |= BattleDestroyedGyRecoveryRuntime.FireEndOfBattlePhaseFromGy(engine);
            }
            finally
            {
                st.FiringEndOfBattlePhase = false;
                // SummonPromptsAndCosts batch (v163): "destroyed a monster by battle" flags reset at the end of the Battle Phase.
                SummonPromptsAndCostsRuntime.ResetBattleDestroyingFlags(engine);
            }

            if (any && !engine.GameOver) engine.NotifyPublic();
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause arm for this batch's end-of-Battle-Phase actions: resolves one clause of
        /// <paramref name="source"/> exactly as <see cref="FireEndOfBattlePhase"/> does.
        /// </summary>
        public static void ApplyEndOfBattlePhaseClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source?.Def == null || clause == null) return;
            if (!source.FaceUp || source.IsNegated || !who.TryFindMonster(source, out _)) return;
            if (ResolveEndOfBattlePhase(engine, who, source, clause))
                engine.NotifyPublic();
        }

        static bool ResolveEndOfBattlePhase(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            switch (c.Action)
            {
                case EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition:
                    return ChangeThisToDefenseAfterAttacking(engine, who, card);
                // BothSideAndMonsterEquip batch (v161): Sword Hunter.
                case EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells:
                    return BothSideAndMonsterEquipRuntime.EquipBattleKills(engine, who, card, c);
                // SummonPromptsAndCosts batch (v163): Vampire Baby (optional; a human's offer opens once play is idle).
                case EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn:
                    return SummonPromptsAndCostsRuntime.OnEndOfBattlePhase(engine, who, card, c);
                default:
                    return false;
            }
        }

        /// <summary>
        /// "If this card attacks" (Lua GetAttackedCount() &gt; 0): it declared an attack this turn and the engine
        /// counts it as having attacked, or it is the attacker whose attack is being negated right now (Negate
        /// Attack ends the Battle Phase before the engine records the negated attack as resolved). A negated attack
        /// still counts; a replay that was cancelled does not.
        /// </summary>
        static bool AttackedThisTurn(DuelEngine engine, CardInstance card) =>
            card != null && (card.AttackedThisTurn || engine.DeclaredAttacker == card) &&
            engine.GameEvents.HappenedThisTurn(DuelGameEventKind.DeclaredAttack, engine.TurnNumber, card);

        /// <summary>Goblin Attack Force / Giant Orc. Returns true when the effect applied.</summary>
        public static bool ChangeThisToDefenseAfterAttacking(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card == null || !card.FaceUp) return false;
            if (!who.TryFindMonster(card, out _) || !AttackedThisTurn(engine, card)) return false;
            if (card.Position == BattlePosition.Attack)
            {
                engine.ApplyEffectPositionChange(card, BattlePosition.Defense, faceUp: true);
                engine.Log($"{card.Name} is changed to Defense Position at the end of the Battle Phase.");
                TextEffectRuntime.NotifyPositionChanged(engine, card, toDefense: true);
            }

            // Lua: EFFECT_CANNOT_CHANGE_POSITION is registered whatever the position was, and lasts until the
            // End Phase of the controller's next turn (the 3rd End Phase from this one).
            StateOf(engine).PositionLocks[card] = new PositionLock
            {
                ThroughTurn = engine.TurnNumber + 2,
                EntryStamp = card.FieldEntryStamp,
                FaceDownStamp = card.FaceDownStamp
            };
            engine.Log($"{card.Name}: its battle position cannot be changed until the End Phase of " +
                       $"{who.Name}'s next turn.");
            return true;
        }

        /// <summary>
        /// DuelEngine.CanChangePosition: a Goblin Attack Force lock forbids its controller to change this monster's
        /// battle position. Ends after the locked turn, or when the monster leaves the field or is turned face-down.
        /// </summary>
        public static bool ManualPositionChangeLocked(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return false;
            if (!s_state.TryGetValue(engine, out var st) || !st.PositionLocks.TryGetValue(card, out var l))
                return false;
            if (engine.TurnNumber > l.ThroughTurn || card.FieldEntryStamp != l.EntryStamp ||
                card.FaceDownStamp != l.FaceDownStamp || engine.ControllerOf(card) == null)
            {
                st.PositionLocks.Remove(card);
                return false;
            }

            return true;
        }

        // ───────────────────── Steel Scorpion ─────────────────────

        /// <summary>
        /// ApplyClause arm (AfterDamageCalculation, this card the attack target): start the countdown on the attacker,
        /// unless its Type is <see cref="EffectClause.ExcludeRaceFilter"/>. <paramref name="who"/> controls the source.
        /// </summary>
        public static void MarkAttackerForDelayedDestroy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance attacker)
        {
            if (engine == null || who == null || source == null || clause == null || attacker?.Def == null) return;
            if (!source.FaceUp || source.IsNegated) return;
            var atkSide = engine.ControllerOf(attacker);
            if (atkSide == null || atkSide == who || !atkSide.TryFindMonster(attacker, out _)) return;
            // The attacker's current Type (Lua IsRace): DNA Surgery's declared Type counts (v142 cross-group audit).
            if (!string.IsNullOrEmpty(clause.ExcludeRaceFilter) &&
                ClassicEraTemplates.RaceMatches(attacker.Race, clause.ExcludeRaceFilter))
                return;
            var turns = clause.Amount > 0 ? clause.Amount : 2;
            StateOf(engine).DelayedDestroys.Add(new DelayedDestroy
            {
                Target = attacker,
                Source = source,
                SourceController = who,
                Needed = turns + 1, // the attack turn's own End Phase counts first
                EntryStamp = attacker.FieldEntryStamp,
                FaceDownStamp = attacker.FaceDownStamp
            });
            engine.Log($"{source.Name}: {attacker.Name} will be destroyed at the End Phase of " +
                       $"{atkSide.Name}'s {Ordinal(turns)} turn after this attack.");
        }

        static string Ordinal(int n) =>
            n % 100 is 11 or 12 or 13 ? n + "th"
            : (n % 10) switch { 1 => n + "st", 2 => n + "nd", 3 => n + "rd", _ => n + "th" };

        /// <summary>
        /// Each End Phase (TextEffectRuntime.FirePhaseTriggers): count the End Phases of turns that are not the
        /// source controller's, and destroy the monster on the last one (not a chain link; cannot be negated).
        /// A countdown ends when its monster leaves the field or is turned face-down.
        /// </summary>
        public static void TickDelayedDestroys(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            if (!s_state.TryGetValue(engine, out var st) || st.DelayedDestroys.Count == 0) return;
            var due = new List<DelayedDestroy>();
            foreach (var d in st.DelayedDestroys.ToList())
            {
                if (!StillMarked(engine, d))
                {
                    st.DelayedDestroys.Remove(d);
                    continue;
                }

                if (engine.TurnPlayer == d.SourceController) continue;
                d.Count++;
                if (d.Count < d.Needed)
                {
                    engine.Log($"{d.Source?.Name}: {d.Target.Name} — End Phase {d.Count} of {d.Needed}.");
                    continue;
                }

                st.DelayedDestroys.Remove(d);
                due.Add(d);
            }

            foreach (var d in due)
            {
                if (engine.GameOver) return;
                if (engine.ControllerOf(d.Target) == null) continue;
                engine.Log($"{d.Source?.Name}: {d.Target.Name} is destroyed.");
                TextEffectRuntime.DestroyCard(engine, d.Target, d.Source);
            }
        }

        /// <summary>The countdown's monster is still on the field as the same face-up presence it was marked in.</summary>
        static bool StillMarked(DuelEngine engine, DelayedDestroy d)
        {
            var ctrl = engine.ControllerOf(d.Target);
            return ctrl != null && ctrl.TryFindMonster(d.Target, out _) &&
                   d.Target.FieldEntryStamp == d.EntryStamp && d.Target.FaceDownStamp == d.FaceDownStamp;
        }

        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163): the running Steel Scorpion countdowns, one row per source card with the
        /// monsters it marked that are still marked (ygopro c13599884 1082946 effect: its condition s.resetcon).
        /// </summary>
        public static List<(CardInstance source, DuelistState sourceController, List<CardInstance> targets)>
            LiveDelayedDestroys(DuelEngine engine)
        {
            var rows = new List<(CardInstance, DuelistState, List<CardInstance>)>();
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return rows;
            foreach (var d in st.DelayedDestroys)
            {
                if (d?.Source == null || !StillMarked(engine, d)) continue;
                var i = rows.FindIndex(r => ReferenceEquals(r.Item1, d.Source));
                if (i < 0)
                    rows.Add((d.Source, d.SourceController, new List<CardInstance> { d.Target }));
                else if (!rows[i].Item3.Contains(d.Target))
                    rows[i].Item3.Add(d.Target);
            }

            return rows;
        }

        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163), Pyro Clock of Destiny: one step of <paramref name="source"/>'s countdown on
        /// <paramref name="target"/> (Lua s.desop with check = false: whoever's turn it is). Each of its counts on that
        /// monster goes up by 1; on the last one the monster is destroyed now, as the End Phase step does.
        /// </summary>
        public static void AdvanceDelayedDestroy(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (engine == null || source == null || target == null || engine.GameOver) return;
            if (!s_state.TryGetValue(engine, out var st)) return;
            var due = false;
            foreach (var d in st.DelayedDestroys.ToList())
            {
                if (!ReferenceEquals(d.Source, source) || !ReferenceEquals(d.Target, target)) continue;
                if (!StillMarked(engine, d))
                {
                    st.DelayedDestroys.Remove(d);
                    continue;
                }

                d.Count++;
                if (d.Count < d.Needed)
                {
                    engine.Log($"{source.Name}: {target.Name} — End Phase {d.Count} of {d.Needed} (turn count moved forward).");
                    continue;
                }

                st.DelayedDestroys.Remove(d);
                due = true;
            }

            if (!due || engine.ControllerOf(target) == null) return;
            engine.Log($"{source.Name}: {target.Name} is destroyed.");
            TextEffectRuntime.DestroyCard(engine, target, source);
        }

        // ───────────────────── Reflect Bounder ─────────────────────

        /// <summary>
        /// Right before damage is calculated: the attack target's <see cref="EffectTiming.BeforeDamageCalculation"/>
        /// clauses (face-up, not negated, in face-up Attack Position when the clause needs it). Returns the cards to
        /// destroy after this damage calculation ("If you do": the opponent's LP went down), or null.
        /// </summary>
        public static List<CardInstance> FireBeforeDamageCalculation(DuelEngine engine, DuelistState atkPlayer,
            DuelistState defPlayer, CardInstance attacker, CardInstance defender)
        {
            if (engine == null || engine.GameOver || atkPlayer == null || defPlayer == null) return null;
            if (attacker == null || defender?.Def == null) return null;
            if (!defPlayer.TryFindMonster(defender, out _) || !atkPlayer.TryFindMonster(attacker, out _)) return null;
            if (!defender.FaceUp || defender.IsNegated) return null;
            var prog = CompiledEffectCache.GetOrCompile(defender.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            List<CardInstance> destroyAfter = null;
            foreach (var c in prog.ClausesFor(EffectTiming.BeforeDamageCalculation))
            {
                if (c == null || c.Action != EffectActionKind.InflictDamageEqualToAtk ||
                    c.Zone != EffectZoneFilter.AttackingMonster || !c.RequiresThisIsAttackTarget)
                    continue;
                if (c.RequiresThisAttackPosition && defender.Position != BattlePosition.Attack) continue;
                if (!atkPlayer.TryFindMonster(attacker, out _)) break;
                var lp = atkPlayer.LifePoints;
                var dmg = attacker.CurrentAtk;
                engine.Log($"{defender.Name}: inflicts damage equal to {attacker.Name}'s ATK ({dmg}).");
                engine.ApplyEffectDamage(atkPlayer, dmg, defender.Name);
                if (c.DestroyThisAfterDamageCalculation && atkPlayer.LifePoints < lp)
                    (destroyAfter ??= new List<CardInstance>()).Add(defender);
                if (engine.GameOver) break;
            }

            return destroyAfter;
        }

        /// <summary>After damage calculation: "If you do, … Destroy this card." (still on the field).</summary>
        public static void DestroyAfterDamageCalculation(DuelEngine engine, List<CardInstance> cards)
        {
            if (engine == null || cards == null) return;
            foreach (var card in cards)
            {
                if (engine.GameOver) return;
                if (card == null || engine.ControllerOf(card) == null) continue;
                engine.Log($"{card.Name} is destroyed after damage calculation.");
                TextEffectRuntime.DestroyCard(engine, card, card);
            }
        }
    }
}
