using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// MonsterNegationAndControlLocks batch runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Skill Drain</b> (<see cref="EffectActionKind.ContinuousNegateMonsterEffectsOnField"/>) lives in
    /// <see cref="ContinuousNegationRuntime"/> as the monster sibling of Royal Decree / Imperial Order: it flags face-up
    /// Effect Monsters <see cref="CardInstance.IsNegated"/> on each board refresh and negates a monster effect resolving
    /// while its monster is face-up on the field.</item>
    /// <item><b>Control lock</b> (<see cref="EffectActionKind.ControlOfThisCannotSwitch"/>; Blindly Loyal Goblin, Mataza the
    /// Zapper; Lua EFFECT_CANNOT_CHANGE_CONTROL): <see cref="ControlLocked"/>. A locked monster is not a legal target of a
    /// take-control effect (<see cref="FilterTargets"/> in TextEffectRuntime.CollectTargets, <see cref="FilterLegacyTargets"/>
    /// in SpellTrapEffects.CollectLegalTargets; ygopro IsControlerCanBeChanged), cannot be chosen for Creature Swap, and a
    /// take-control or switch resolution leaves it where it is (<see cref="BlocksControlChange"/>). Control returns of
    /// monsters taken earlier are not blocked (residual).</item>
    /// <item><b>Final Attack Orders</b> (<see cref="EffectActionKind.ContinuousForceAttackLockPosition"/>): on each board
    /// refresh, once its activation has resolved, every face-up monster on the field is changed to Attack Position and
    /// <see cref="CardInstance.PositionLockedByEffect"/> (reset by FieldSpellEffects.ClearOne) forbids manual changes
    /// (<see cref="ApplyContinuous"/>).</item>
    /// <item><b>Stumbling</b> (<see cref="EffectActionKind.ChangeSummonedMonstersToDefense"/>): after a Normal / Flip /
    /// Special Summon settles (TextEffectRuntime.TryTriggerFaceUpFieldSummon and DuelEngine's queued-summon path, the same
    /// points as King Tiger Wanghu), each face-up Stumbling changes the summoned monster from face-up Attack to face-up
    /// Defense Position (<see cref="OnSummonSettled"/>; resolved inline like the other summon-success triggers).</item>
    /// <item><b>Non-Spellcasting Area</b> (<see cref="EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells"/>):
    /// <see cref="ShieldsFromSpell"/>, read by ContinuousProtections.IsUnaffectedBy (every resolution guard), the
    /// Spell attack locks (DuelEngine.ContinuousCannotAttackBlocks, Swords of Revealing Light), Equip Spell grants and
    /// Level Limit - Area B (FieldSpellEffects); <see cref="ShieldStats"/> undoes a Field / Continuous Spell aura on
    /// shielded monsters.</item>
    /// </list>
    /// </summary>
    public static class MonsterNegationAndControlLocksRuntime
    {
        sealed class Stamp
        {
            public int Turn;
            public int Entry;
        }

        /// <summary>Stumbling: the summoned monsters already answered (once per field presence and turn).</summary>
        static readonly ConditionalWeakTable<CardInstance, Stamp> s_stumbled = new();

        // ───────────────────────────── sources ─────────────────────────────

        /// <summary>The ContinuousWhileFaceUp clause of <paramref name="action"/> on a FullyCompiled program, or null.</summary>
        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        /// <summary>An unresolved Chain Link of this card's own activation: a Continuous Spell/Trap applies once it resolved.</summary>
        static bool ActivationPending(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && ReferenceEquals(l.Card, card) && !l.Resolved)
                    return true;
            return false;
        }

        /// <summary>Face-up, un-negated Spells/Traps on either field whose resolved program has <paramref name="action"/>.</summary>
        static IEnumerable<CardInstance> ActiveSpellTraps(DuelEngine engine, EffectActionKind action)
        {
            if (engine?.Player == null || engine.Opponent == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var st in side.SpellTrapsOnField())
            {
                if (st?.Def == null || !st.FaceUp || st.IsNegated || st.EquippedTo != null) continue;
                if (ClauseOf(st, action) == null || ActivationPending(engine, st)) continue;
                yield return st;
            }
        }

        static bool InMonsterZone(DuelEngine engine, CardInstance card) =>
            card != null &&
            ((engine.Player != null && engine.Player.TryFindMonster(card, out _)) ||
             (engine.Opponent != null && engine.Opponent.TryFindMonster(card, out _)));

        // ───────────────────────────── control lock ─────────────────────────────

        /// <summary>
        /// Blindly Loyal Goblin / Mataza the Zapper: <paramref name="card"/> is face-up in a Monster Zone, not negated, and
        /// its FullyCompiled program says control of it cannot switch.
        /// </summary>
        public static bool ControlLocked(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            if (!InMonsterZone(engine, card) || engine.IsSummonPending(card)) return false;
            var clause = ClauseOf(card, EffectActionKind.ControlOfThisCannotSwitch);
            // Coverage family F1 (v180, AttributeBullets): the Element WATER bullet's lock is live only while a monster
            // with its Attribute is face-up on the field.
            return clause != null && AttributeBulletsRuntime.FieldAttributePresent(engine, clause.RequiresFieldAttribute);
        }

        /// <summary>A clause whose resolution takes or switches control of its target(s).</summary>
        public static bool IsTakeControlClause(EffectClause c) =>
            c != null &&
            (c.TakeControlOfTarget ||
             c.Action == EffectActionKind.TakeControlUntilEndPhase ||
             c.Action == EffectActionKind.TakeControlWhileSourceFaceUp ||
             c.Action == EffectActionKind.SwitchControlWithThis ||
             c.Action == EffectActionKind.TakeControlLevelLeq ||
             c.Action == EffectActionKind.EquipThisFromGyToTargetTakeControl ||
             c.Action == EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis);

        /// <summary>TextEffectRuntime.CollectTargets tail: a take-control clause never targets a control-locked monster.</summary>
        public static void FilterTargets(DuelEngine engine, EffectClause c, List<CardInstance> list)
        {
            if (engine == null || list == null || list.Count == 0 || !IsTakeControlClause(c)) return;
            list.RemoveAll(t => ControlLocked(engine, t));
        }

        /// <summary>
        /// Legacy Spell/Trap mirror (SpellTrapEffects.CollectLegalTargets): when <paramref name="activating"/>'s
        /// FullyCompiled Activate clause takes control of its target, a control-locked monster is not a legal target.
        /// </summary>
        public static void FilterLegacyTargets(DuelEngine engine, CardInstance activating, List<CardInstance> list)
        {
            if (engine == null || activating?.Def == null || list == null || list.Count == 0) return;
            var prog = CompiledEffectCache.GetOrCompile(activating.Def);
            if (prog == null || !prog.FullyCompiled) return;
            var takes = false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && c.RequiresTargetChoice && IsTakeControlClause(c))
                    takes = true;
            if (takes) list.RemoveAll(t => ControlLocked(engine, t));
        }

        /// <summary>
        /// Take-control / switch resolutions: true (logged) when <paramref name="target"/>'s control cannot switch — the
        /// effect leaves it with its controller.
        /// </summary>
        public static bool BlocksControlChange(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (!ControlLocked(engine, target)) return false;
            engine.Log($"{source?.Name ?? "Effect"}: control of {target.Name} cannot switch.");
            return true;
        }

        // ───────────────────────────── Final Attack Orders ─────────────────────────────

        /// <summary>
        /// FieldSpellEffects.ApplyCompiledContinuous: <see cref="EffectActionKind.ContinuousForceAttackLockPosition"/> —
        /// every face-up monster on the field is changed to Attack Position and cannot change its battle position (Lua
        /// EFFECT_SET_POSITION + EFFECT_CANNOT_CHANGE_POSITION). Face-down monsters are not affected; a monster flipped
        /// face-up later is changed on the next refresh. False for any other clause.
        /// </summary>
        public static bool ApplyContinuous(DuelEngine engine, CardInstance source, EffectClause clause)
        {
            if (clause == null || clause.Action != EffectActionKind.ContinuousForceAttackLockPosition) return false;
            if (engine?.Player == null || engine.Opponent == null || source == null) return true;
            if (ActivationPending(engine, source)) return true;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in new List<CardInstance>(side.MonstersOnField()))
            {
                if (m?.Def == null || !m.FaceUp || engine.IsSummonPending(m)) continue;
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source)) continue;
                if (m.Position != BattlePosition.Attack)
                {
                    engine.Log($"{source.Name}: {m.Name} is changed to Attack Position.");
                    engine.ApplyEffectPositionChange(m, BattlePosition.Attack, true);
                }

                m.PositionLockedByEffect = true;
            }

            return true;
        }

        /// <summary>
        /// DuelEngine.CanFlipSummon: Final Attack Orders' EFFECT_CANNOT_CHANGE_POSITION has no face-up filter (Lua e3), so
        /// while it applies no face-down monster can be Flip Summoned (ygopro is_can_be_flip_summoned).
        /// </summary>
        public static bool FlipSummonLocked(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null) return false;
            foreach (var _ in ActiveSpellTraps(engine, EffectActionKind.ContinuousForceAttackLockPosition))
                return true;
            return false;
        }

        // ───────────────────────────── Stumbling ─────────────────────────────

        /// <summary>
        /// The summon-success point (TextEffectRuntime.TryTriggerFaceUpFieldSummon; DuelEngine's queued-summon branch):
        /// <paramref name="summoned"/> was Normal, Flip or Special Summoned. Each face-up Stumbling (activation resolved, not
        /// negated, not locked by Cold Wave) changes it to face-up Defense Position while it is still a face-up Attack
        /// Position monster on the field (Lua s.filter). A Set is not a Summon (the monster is face-down). Answered once
        /// per field presence and turn.
        /// </summary>
        public static void OnSummonSettled(DuelEngine engine, DuelistState summoner, CardInstance summoned)
        {
            if (engine == null || summoned?.Def == null || engine.GameOver) return;
            if (!summoned.FaceUp || !InMonsterZone(engine, summoned) || engine.IsSummonPending(summoned)) return;
            if (s_stumbled.TryGetValue(summoned, out var done) && done.Entry == summoned.FieldEntryStamp &&
                done.Turn == engine.TurnNumber)
                return;
            var sources = new List<CardInstance>(ActiveSpellTraps(engine, EffectActionKind.ChangeSummonedMonstersToDefense));
            if (sources.Count == 0) return;
            s_stumbled.Remove(summoned);
            s_stumbled.Add(summoned, new Stamp { Turn = engine.TurnNumber, Entry = summoned.FieldEntryStamp });
            foreach (var st in sources)
            {
                if (engine.GameOver) return;
                if (!summoned.FaceUp || summoned.Position != BattlePosition.Attack || !InMonsterZone(engine, summoned))
                    return;
                // Still face-up, un-negated and on the field (Lua IsRelateToEffect); Cold Wave locks the trigger.
                var holder = engine.ControllerOf(st);
                if (holder == null || !holder.TryFindSpellTrap(st, out _) || !st.FaceUp || st.IsNegated) continue;
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                if (ContinuousProtections.IsUnaffectedBy(engine, summoned, st))
                {
                    engine.Log($"{st.Name}: {summoned.Name} is unaffected.");
                    continue;
                }

                engine.Log($"{st.Name}: {summoned.Name} is changed to Defense Position.");
                engine.ApplyEffectPositionChange(summoned, BattlePosition.Defense, true);
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Non-Spellcasting Area ─────────────────────────────

        /// <summary>
        /// Non-Spellcasting Area: <paramref name="victim"/> — a face-up monster in a Monster Zone that is not an Effect
        /// Monster (a Normal Monster, an effectless Fusion / Ritual, a Token) — is unaffected by the effect of the Spell
        /// <paramref name="source"/> while a face-up Non-Spellcasting Area other than <paramref name="source"/> applies.
        /// </summary>
        public static bool ShieldsFromSpell(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (source?.Def == null || !source.Def.IsSpell) return false;
            if (!Shieldable(engine, victim)) return false;
            foreach (var nsa in ActiveSpellTraps(engine, EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells))
                if (!ReferenceEquals(nsa, source))
                    return true;
            return false;
        }

        static bool Shieldable(DuelEngine engine, CardInstance victim) =>
            engine != null && victim?.Def != null && victim.FaceUp && victim.Def.IsMonster &&
            (victim.IsToken || !TextEffectRuntime.IsTrueEffectMonster(victim.Def)) && InMonsterZone(engine, victim);

        /// <summary>
        /// Swords of Revealing Light (DuelEngine.CanAttack / TryAttack): its "cannot declare an attack" is a Spell effect on
        /// the opponent's monsters, so a monster <see cref="ShieldsFromSpell"/> shields may still attack.
        /// </summary>
        public static bool IgnoresSpellAttackLock(DuelEngine engine, DuelistState attackerController, CardInstance attacker)
        {
            if (engine == null || attackerController == null || !Shieldable(engine, attacker)) return false;
            var opp = engine.OpponentOf(attackerController);
            if (opp == null) return false;
            foreach (var st in opp.SpellTrapsOnField())
                if (st?.Def != null && st.FaceUp && st.CardId == SpellTrapEffects.SwordsOfRevealingLight &&
                    ShieldsFromSpell(engine, attacker, st))
                    return true;
            return false;
        }

        /// <summary>
        /// Snapshot of the shielded monsters' modifiers taken before a Spell's continuous aura applies (FieldSpellEffects
        /// RefreshBoard: Field Spells, Continuous Spells); disposing it puts them back, so the aura does not affect them.
        /// </summary>
        public readonly struct StatShield : IDisposable
        {
            readonly List<(CardInstance m, int atk, int def, int lvl, bool locked, BattlePosition pos)> _saved;

            internal StatShield(List<(CardInstance, int, int, int, bool, BattlePosition)> saved) => _saved = saved;

            public void Dispose()
            {
                if (_saved == null) return;
                foreach (var (m, atk, def, lvl, locked, pos) in _saved)
                {
                    m.AtkModifier = atk;
                    m.DefModifier = def;
                    m.LevelModifier = lvl;
                    m.PositionLockedByEffect = locked;
                    m.Position = pos;
                }
            }
        }

        /// <summary><see cref="StatShield"/> for the Spell <paramref name="source"/>; empty when nothing is shielded.</summary>
        public static StatShield ShieldStats(DuelEngine engine, CardInstance source)
        {
            if (engine?.Player == null || engine.Opponent == null || source?.Def == null || !source.Def.IsSpell)
                return default;
            List<(CardInstance, int, int, int, bool, BattlePosition)> saved = null;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var m in side.MonstersOnField())
            {
                if (!ShieldsFromSpell(engine, m, source)) continue;
                saved ??= new List<(CardInstance, int, int, int, bool, BattlePosition)>();
                saved.Add((m, m.AtkModifier, m.DefModifier, m.LevelModifier, m.PositionLockedByEffect, m.Position));
            }

            return new StatShield(saved);
        }
    }
}
