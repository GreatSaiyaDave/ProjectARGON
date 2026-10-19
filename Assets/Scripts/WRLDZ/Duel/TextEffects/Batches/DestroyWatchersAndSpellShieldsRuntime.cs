using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DestroyWatchersAndSpellShields" runtime (atoms: <see cref="DestroyWatchersAndSpellShieldsAtoms"/>). Reads
    /// compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Destroyed-monster events</b>: DuelEngine.DestroyMonster records each monster that was face-up in a Monster
    /// Zone, with its controller and its Type on the field (<see cref="NoteMonsterDestroyed"/>). At the next GY-send
    /// checkpoint (StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint, once no <see cref="FlipMassEffectsRuntime.Simultaneously"/>
    /// event is still happening) the records since the last checkpoint are ONE event (<see cref="FlushDestroyedEvents"/>):
    /// <list type="bullet">
    /// <item>Firebird / Maji-Gire Panda (<see cref="EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed"/>): each
    /// face-up, un-negated copy on the field that saw 1+ matching monster queues one mandatory trigger on the
    /// simultaneous-trigger Chain (<see cref="SimultaneousTriggersRuntime.QueueTrigger"/>); it resolves if the copy is still
    /// face-up on the same field presence and not negated: a lasting +N ATK (RESETS_STANDARD; not applied while negated,
    /// <see cref="SpecialSummonEffectsRuntime.AddLastingStatChange"/>). A copy destroyed in the same event is gone.</item>
    /// <item>Mokey Mokey Smackdown (<see cref="EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed"/>):
    /// a continuous effect (no Chain Link): if a matching monster its controller controlled was destroyed and that player
    /// now controls a face-up named monster, the card is flagged until the end of the turn (or until it leaves the field or
    /// is turned face-down). While flagged, face-up and not negated, each face-up named monster its controller controls has
    /// its ATK set to N (<see cref="ApplyBoard"/>, <see cref="CardInstance.SelfSetAtk"/>: the base every other modifier
    /// adds to), unless it is unaffected by the Spell (Non-Spellcasting Area, Pole Position).</item>
    /// </list></item>
    /// <item><b>Pole Position</b> (<see cref="EffectActionKind.HighestAtkMonstersUnaffectedBySpells"/>): after each board
    /// refresh (Lua EVENT_ADJUST) the face-up monster(s) with the highest ATK on the field are stored as the shielded group
    /// (<see cref="AdjustShieldedGroup"/>); a changed group refreshes the board again (at most 3 times in a row, a loop
    /// guard). While a Pole Position applies, a stored monster still face-up on the same field presence is unaffected by
    /// every Spell effect (<see cref="ShieldsFromSpell"/>, read by MonsterNegationAndControlLocksRuntime.ShieldsFromSpell:
    /// resolution guards, destruction, Spell auras, Equip Spell grants, Spell attack locks).
    /// <see cref="EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField"/>: each refresh records whether each
    /// Pole Position on the field applies (face-up, not negated, its activation resolved); once one is no longer on that
    /// field presence (DuelEngine.Notify, <see cref="OnBoardSettled"/>), the face-up monster(s) with the highest ATK then
    /// are destroyed together — not when it was negated or not yet applying at its last refresh (Lua checkop).</item>
    /// <item><b>Blast Magician</b> (<see cref="EffectClause.SpellCountersPerTargetAtk"/>): a legal target is a face-up
    /// monster whose ATK needs no more counters than this card holds (<see cref="FilterTargets"/>); the cost,
    /// ceil(ATK / N) and at least 1, is removed before the destruction (<see cref="PayCountersForTarget"/>, from the
    /// Destroy arm of TextEffectRuntime.ApplyClause: monster ignitions resolve as they are activated).</item>
    /// </list>
    /// </summary>
    public static class DestroyWatchersAndSpellShieldsRuntime
    {
        sealed class DestroyedNote
        {
            public CardInstance Card;
            public DuelistState Controller;
            public string Race;
        }

        sealed class Member
        {
            public CardInstance Card;
            public int Entry;
        }

        sealed class PoleRecord
        {
            public CardInstance Card;
            public DuelistState Holder;
            public int Entry;
            public bool Applying;
        }

        sealed class SmackdownFlag
        {
            public CardInstance Card;
            public int Entry;
            public int FaceDown;
            public int Turn;
        }

        sealed class State
        {
            public readonly List<DestroyedNote> Destroyed = new();
            public bool Flushing;
            public readonly List<Member> Shielded = new();
            public int AdjustDepth;
            public readonly List<PoleRecord> Poles = new();
            public bool Settling;
            public readonly List<SmackdownFlag> Flags = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── shared helpers ─────────────────────────────

        static EffectClause ProgramClause(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        /// <summary>A face-up, un-negated Spell/Trap of <paramref name="side"/> whose activation has resolved.</summary>
        static EffectClause LiveSpellTrapClause(DuelEngine engine, DuelistState side, CardInstance st,
            EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _)) return null;
            var clause = ProgramClause(st, action);
            if (clause == null) return null;
            if (ActivationUnresolved(engine, st)) return null;
            return clause;
        }

        static bool ActivationUnresolved(DuelEngine engine, CardInstance st) =>
            engine.Chain != null &&
            engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, st) && !l.Resolved);

        /// <summary>A face-up, un-negated monster of <paramref name="side"/> on the field (its Summon settled).</summary>
        static EffectClause LiveMonsterClause(DuelEngine engine, DuelistState side, CardInstance m, EffectActionKind action)
        {
            if (m?.Def == null || !m.FaceUp || m.IsNegated || side == null) return null;
            if (!side.TryFindMonster(m, out _) || engine.IsSummonPending(m)) return null;
            return ProgramClause(m, action);
        }

        static IEnumerable<DuelistState> Sides(DuelEngine engine)
        {
            if (engine?.Player != null) yield return engine.Player;
            if (engine?.Opponent != null) yield return engine.Opponent;
        }

        static List<CardInstance> FaceUpMonsters(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            foreach (var side in Sides(engine))
                list.AddRange(side.MonstersOnField().Where(m => m?.Def != null && m.FaceUp));
            return list;
        }

        // ───────────────────────────── destroyed-monster events ─────────────────────────────

        /// <summary>
        /// DuelEngine.DestroyMonster, after <paramref name="card"/> left <paramref name="controller"/>'s Monster Zone
        /// (destroyed by battle or by an effect, to any destination). <paramref name="raceOnField"/> is its Type while it
        /// was on the field. A monster that was face-down is no event for these cards.
        /// </summary>
        public static void NoteMonsterDestroyed(DuelEngine engine, DuelistState controller, CardInstance card,
            bool wasFaceUp, string raceOnField)
        {
            if (engine == null || controller == null || card?.Def == null || !wasFaceUp) return;
            StateOf(engine).Destroyed.Add(new DestroyedNote { Card = card, Controller = controller, Race = raceOnField });
        }

        /// <summary>
        /// StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: the destructions recorded since the last checkpoint were
        /// one event. Waits while a simultaneous event is still happening.
        /// </summary>
        public static void FlushDestroyedEvents(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Flushing || st.Destroyed.Count == 0)
                return;
            if (FlipMassEffectsRuntime.Holding(engine)) return;
            var events = st.Destroyed.ToList();
            st.Destroyed.Clear();
            if (engine.GameOver) return;
            st.Flushing = true;
            try
            {
                foreach (var side in Sides(engine).ToList())
                {
                    foreach (var m in side.MonstersOnField().ToList())
                    {
                        var clause = LiveMonsterClause(engine, side, m, EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed);
                        if (clause == null) continue;
                        var mineOnly = clause.Side == EffectSide.Controller;
                        if (!events.Any(n => !ReferenceEquals(n.Card, m) &&
                                             ClassicEraTemplates.RaceMatches(n.Race, clause.RaceFilter) &&
                                             (!mineOnly || ReferenceEquals(n.Controller, side))))
                            continue;
                        QueueSelfGain(engine, side, m, clause);
                    }

                    foreach (var card in side.SpellTrapsOnField().ToList())
                    {
                        var clause = LiveSpellTrapClause(engine, side, card,
                            EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed);
                        if (clause == null || FlagOf(engine, st, card) != null) continue;
                        if (!events.Any(n => ReferenceEquals(n.Controller, side) &&
                                             ClassicEraTemplates.RaceMatches(n.Race, clause.RaceFilter)))
                            continue;
                        if (!side.MonstersOnField().Any(x => x != null && x.FaceUp && x.IsNamed(clause.NamedCard)))
                            continue;
                        st.Flags.RemoveAll(f => ReferenceEquals(f.Card, card));
                        st.Flags.Add(new SmackdownFlag
                        {
                            Card = card, Entry = card.FieldEntryStamp, FaceDown = card.FaceDownStamp, Turn = engine.TurnNumber
                        });
                        engine.Log($"{card.Name}: a {clause.RaceFilter} monster {side.Name} controlled was destroyed — " +
                                   $"the ATK of each \"{clause.NamedCard}\" {side.Name} controls becomes {clause.Amount} " +
                                   "until the end of this turn.");
                        ApplySmackdown(engine, side, card, clause);
                    }
                }
            }
            finally
            {
                st.Flushing = false;
            }
        }

        static void QueueSelfGain(DuelEngine engine, DuelistState side, CardInstance card, EffectClause clause)
        {
            var entry = card.FieldEntryStamp;
            var faceDown = card.FaceDownStamp;
            var amount = clause.Amount;
            engine.Log($"{card.Name}: a face-up {clause.RaceFilter} monster was destroyed — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
            {
                if (engine.GameOver) return;
                var holder = engine.ControllerOf(card);
                if (holder == null || !holder.TryFindMonster(card, out _) || !card.FaceUp ||
                    card.FieldEntryStamp != entry || card.FaceDownStamp != faceDown)
                {
                    engine.Log($"{card.Name} is no longer face-up on the field — it gains no ATK.");
                    return;
                }

                if (card.IsNegated)
                {
                    engine.Log($"{card.Name}'s effect is negated — it gains no ATK.");
                    return;
                }

                SpecialSummonEffectsRuntime.AddLastingStatChange(engine, card, amount, 0, endsOnNegation: false,
                    suppressedWhileNegated: true);
                engine.NotifyPublic();
                engine.Log($"{card.Name} gains {amount} ATK (now {card.CurrentAtk}).");
            });
        }

        // ───────────────────────────── Mokey Mokey Smackdown ─────────────────────────────

        static SmackdownFlag FlagOf(DuelEngine engine, State st, CardInstance card)
        {
            var f = st.Flags.FirstOrDefault(x => ReferenceEquals(x.Card, card));
            if (f == null) return null;
            var holder = engine.ControllerOf(card);
            var live = holder != null && holder.TryFindSpellTrap(card, out _) && card.FaceUp &&
                       card.FieldEntryStamp == f.Entry && card.FaceDownStamp == f.FaceDown && f.Turn == engine.TurnNumber;
            if (live) return f;
            st.Flags.Remove(f);
            return null;
        }

        static void ApplySmackdown(DuelEngine engine, DuelistState side, CardInstance card, EffectClause clause)
        {
            foreach (var m in side.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp || !m.IsNamed(clause.NamedCard) || engine.IsSummonPending(m)) continue;
                if (ContinuousProtections.IsUnaffectedBy(engine, m, card)) continue;
                m.SelfSetAtk = Math.Max(0, clause.Amount);
            }
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (after the cleared modifiers and the lasting grants): every flagged, applying
        /// Mokey Mokey Smackdown sets the ATK of its controller's face-up named monsters.
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Flags.Count == 0) return;
            foreach (var f in st.Flags.ToList())
            {
                if (FlagOf(engine, st, f.Card) == null) continue;
                var side = engine.ControllerOf(f.Card);
                var clause = LiveSpellTrapClause(engine, side, f.Card,
                    EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed);
                if (clause == null) continue; // negated: the flag stays, the ATK change does not apply
                ApplySmackdown(engine, side, f.Card, clause);
            }
        }

        // ───────────────────────────── Pole Position ─────────────────────────────

        static bool AnyPoleApplies(DuelEngine engine)
        {
            foreach (var side in Sides(engine))
            foreach (var st in side.SpellTrapsOnField())
                if (LiveSpellTrapClause(engine, side, st, EffectActionKind.HighestAtkMonstersUnaffectedBySpells) != null)
                    return true;
            return false;
        }

        /// <summary>
        /// MonsterNegationAndControlLocksRuntime.ShieldsFromSpell (<paramref name="source"/> is a Spell): the monster is in
        /// the stored highest-ATK group while a Pole Position applies.
        /// </summary>
        public static bool ShieldsFromSpell(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (engine == null || victim?.Def == null || !victim.FaceUp || source?.Def == null || !source.Def.IsSpell)
                return false;
            if (!s_state.TryGetValue(engine, out var st) || st.Shielded.Count == 0) return false;
            if (!st.Shielded.Exists(s => ReferenceEquals(s.Card, victim) && victim.FieldEntryStamp == s.Entry)) return false;
            var holder = engine.ControllerOf(victim);
            if (holder == null || !holder.TryFindMonster(victim, out _)) return false;
            return AnyPoleApplies(engine);
        }

        /// <summary>
        /// End of FieldSpellEffects.RefreshBoard (Lua EVENT_ADJUST): record each Pole Position on the field (for its leave
        /// effect), then recompute the highest-ATK group. True when the group changed and the board should be refreshed
        /// again (the caller then calls <see cref="EndReadjust"/>); at most 3 nested re-refreshes.
        /// </summary>
        public static bool AdjustShieldedGroup(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return false;
            var hasState = s_state.TryGetValue(engine, out var st);
            List<(CardInstance card, DuelistState side, bool applying)> poles = null;
            foreach (var side in Sides(engine))
            foreach (var card in side.SpellTrapsOnField())
            {
                if (ProgramClause(card, EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField) == null &&
                    ProgramClause(card, EffectActionKind.HighestAtkMonstersUnaffectedBySpells) == null)
                    continue;
                var applying = LiveSpellTrapClause(engine, side, card,
                    EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField) != null;
                (poles ??= new List<(CardInstance, DuelistState, bool)>()).Add((card, side, applying));
            }

            if (poles == null && (!hasState || (st.Shielded.Count == 0 && st.Poles.Count == 0))) return false;
            st ??= StateOf(engine);
            if (poles != null)
                foreach (var (card, side, applying) in poles)
                {
                    var rec = st.Poles.FirstOrDefault(r => ReferenceEquals(r.Card, card));
                    if (rec != null && rec.Entry != card.FieldEntryStamp)
                    {
                        // Left and came back between two checks: the old presence's leave is handled first.
                        if (!st.Settling) SettleLeftPoles(engine, st, rec);
                        rec = null;
                    }

                    if (rec == null)
                    {
                        rec = new PoleRecord { Card = card, Entry = card.FieldEntryStamp };
                        st.Poles.Add(rec);
                    }

                    rec.Holder = side;
                    rec.Applying = applying;
                }

            if (!AnyPoleApplies(engine))
            {
                st.Shielded.Clear();
                return false;
            }

            var faceUp = FaceUpMonsters(engine);
            var group = new List<Member>();
            if (faceUp.Count > 0)
            {
                var top = faceUp.Max(m => m.CurrentAtk);
                group.AddRange(faceUp.Where(m => m.CurrentAtk == top)
                    .Select(m => new Member { Card = m, Entry = m.FieldEntryStamp }));
            }

            var same = group.Count == st.Shielded.Count &&
                       group.TrueForAll(g => st.Shielded.Exists(s => ReferenceEquals(s.Card, g.Card) && s.Entry == g.Entry));
            if (same) return false;
            st.Shielded.Clear();
            st.Shielded.AddRange(group);
            if (st.AdjustDepth >= 3) return false;
            st.AdjustDepth++;
            return true;
        }

        /// <summary>Paired with a true <see cref="AdjustShieldedGroup"/> once the extra refresh is done.</summary>
        public static void EndReadjust(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st) && st.AdjustDepth > 0) st.AdjustDepth--;
        }

        /// <summary>
        /// DuelEngine.Notify (after the board refresh): a recorded Pole Position that is no longer on its field presence
        /// destroys the face-up monster(s) with the highest ATK on the field now, if it applied when it left.
        /// </summary>
        public static void OnBoardSettled(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Settling || st.Poles.Count == 0) return;
            st.Settling = true;
            try
            {
                foreach (var rec in st.Poles.ToList())
                {
                    var holder = engine.ControllerOf(rec.Card);
                    if (holder != null && holder.TryFindSpellTrap(rec.Card, out _) &&
                        rec.Card.FieldEntryStamp == rec.Entry)
                        continue;
                    SettleLeftPoles(engine, st, rec);
                }
            }
            finally
            {
                st.Settling = false;
            }
        }

        static void SettleLeftPoles(DuelEngine engine, State st, PoleRecord rec)
        {
            st.Poles.Remove(rec);
            var pole = rec.Card;
            if (engine.GameOver) return;
            if (!rec.Applying)
            {
                engine.Log($"{pole.Name} left the field while its effect did not apply — nothing is destroyed.");
                return;
            }

            var faceUp = FaceUpMonsters(engine);
            if (faceUp.Count == 0)
            {
                engine.Log($"{pole.Name} left the field — no face-up monster on the field.");
                return;
            }

            var top = faceUp.Max(m => m.CurrentAtk);
            var victims = faceUp.Where(m => m.CurrentAtk == top).ToList();
            engine.Log($"{pole.Name} left the field — destroy the face-up monster(s) with the highest ATK ({top}): " +
                       string.Join(", ", victims.Select(v => v.Name)) + ".");
            var by = rec.Holder;
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var v in victims)
                    if (engine.ControllerOf(v) != null)
                        TextEffectRuntime.DestroyCard(engine, v, pole, byPlayer: by);
            });
            engine.NotifyPublic();
        }

        // ───────────────────────────── Blast Magician ─────────────────────────────

        /// <summary>The Spell Counters a target with <paramref name="atk"/> ATK costs: ceil(ATK / N), at least 1.</summary>
        public static int CountersFor(int atk, int per) =>
            per <= 0 ? 0 : Math.Max(1, (Math.Max(0, atk) + per - 1) / per);

        /// <summary>
        /// End of TextEffectRuntime.CollectTargets: a face-up monster is legal only when this card (<paramref name="source"/>)
        /// holds the counters its ATK costs.
        /// </summary>
        public static void FilterTargets(EffectClause c, CardInstance source, List<CardInstance> list)
        {
            if (c == null || c.SpellCountersPerTargetAtk <= 0 || list == null) return;
            var have = source?.Counters ?? 0;
            list.RemoveAll(t => t?.Def == null || !t.FaceUp || !t.Def.IsMonster ||
                                CountersFor(t.CurrentAtk, c.SpellCountersPerTargetAtk) > have);
        }

        /// <summary>
        /// SimpleAi (monster ignitions): the AI uses Blast Magician only while a monster its opponent controls is a legal
        /// target (otherwise <see cref="TextEffectRuntime.AutoPick"/> would fall back to one of its own).
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance m)
        {
            if (engine == null || ai == null || m?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(m);
            if (prog == null || !prog.FullyCompiled) return false;
            var clause = prog.ClausesFor(EffectTiming.Activate)
                .FirstOrDefault(c => c != null && c.SpellCountersPerTargetAtk > 0);
            if (clause == null) return false;
            var opp = engine.OpponentOf(ai);
            if (opp == null) return true;
            return !TextEffectRuntime.CollectTargets(engine, ai, clause, m)
                .Exists(t => opp.TryFindMonster(t, out _));
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause (Destroy), before the destruction: remove the counters the chosen target costs.
        /// False (nothing happens) when there is no target or this card no longer holds them.
        /// </summary>
        public static bool PayCountersForTarget(DuelEngine engine, CardInstance source, EffectClause c,
            CardInstance target)
        {
            if (engine == null || source == null || c == null || c.SpellCountersPerTargetAtk <= 0) return true;
            if (target == null)
            {
                engine.Log($"{source.Name}: no target — no Spell Counters are removed.");
                return false;
            }

            var need = CountersFor(target.CurrentAtk, c.SpellCountersPerTargetAtk);
            if (source.Counters < need)
            {
                engine.Log($"{source.Name}: needs {need} Spell Counter(s) to target {target.Name}.");
                return false;
            }

            source.Counters -= need;
            engine.Log($"Cost: remove {need} Spell Counter(s) from {source.Name} (now {source.Counters}).");
            return true;
        }
    }
}
