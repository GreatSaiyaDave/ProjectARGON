using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// GuardiansAndEquipEvents batch (<see cref="GuardiansAndEquipEventsAtoms"/>).
    /// <list type="bullet">
    /// <item>Guardian Elma (c74367458) / Chopman the Desperate Outlaw (c40884383): the summon trigger targets 1 Equip Spell in
    /// its controller's GY that could be equipped to this card (Lua s.filter: IsType(TYPE_EQUIP) and CheckEquipTarget, with a
    /// free Spell &amp; Trap Zone) — <see cref="FilterTargets"/> — and equips it to this card from the GY (Duel.Equip) —
    /// <see cref="ApplyEquipFromGy"/>.</item>
    /// <item>Guardian Grarl (c47150851): its hand procedure (EFFECT_SPSUMMON_PROC, s.spcon) only while it is the only card in
    /// its controller's hand, and only while its summon gate is open — <see cref="OwnHandProcedureAllows"/>.</item>
    /// <item>Morale Boost (c93671934): two FIELD TRIGGER_F effects in the Spell &amp; Trap Zone. EVENT_EQUIP: the controller of
    /// the Equip Spell Card that was equipped gains 1000 LP (<see cref="NoteEquipped"/>, called from the shared equip event
    /// <see cref="EquipModelRuntime.NoteEquipped"/> and Premature Burial's link). EVENT_LEAVE_FIELD of Equip Spell Cards that
    /// were equipped (GetEquipTarget, or sent because their monster left: REASON_LOST_TARGET): each controller of one of them
    /// takes 1000 damage, once per event (<see cref="NoteEquipLeftField"/>; one event = one
    /// <see cref="EnterEquipLeaveEvent"/> scope: a monster's Equip Cards leaving with it, a mass destruction, or one clause /
    /// one set of Tributes, RedirectAndReplaceRuntime.EnterEffectApplication). Each trigger
    /// goes on the SEGOC queue (<see cref="SimultaneousTriggersRuntime.QueueTrigger"/>) and re-checks Morale Boost there
    /// (same field presence, face-up, not negated), as the EffectDamageEvents burns do. Only Equip Spell Cards count: a Union
    /// monster or a monster equipped by an effect is not one (the printed text).</item>
    /// </list>
    /// </summary>
    public static class GuardiansAndEquipEventsRuntime
    {
        sealed class State
        {
            public int Depth;
            public readonly List<(CardInstance card, DuelistState controller)> Left = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A real Equip Spell Card (not a monster equipped as one, not a Continuous Trap link).</summary>
        static bool IsEquipSpellCard(CardInstance card) =>
            card?.Def != null && card.Def.IsEquipSpell && !card.Def.IsMonster;

        // ───────────────────────────── Guardian Elma / Chopman ─────────────────────────────

        /// <summary>
        /// CollectTargets (<see cref="EffectActionKind.EquipTargetedGyEquipSpellToThis"/> only): keep the Equip Spells in
        /// <paramref name="who"/>'s GY that could be equipped to <paramref name="source"/> (this card, face-up on the field),
        /// none while <paramref name="who"/> has no free Spell &amp; Trap Zone; the highest ATK change first (the AI's pick).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, CardInstance source,
            List<CardInstance> list)
        {
            if (engine == null || c == null || list == null || c.Action != EffectActionKind.EquipTargetedGyEquipSpellToThis)
                return;
            if (who == null || source == null || engine.FirstEmptySpellTrap(who) < 0)
            {
                list.Clear();
                return;
            }

            list.RemoveAll(t => t == null || !who.Graveyard.Contains(t) ||
                                !EquipFamilyRuntime.IsCorrectEquipTargetFromGy(engine, who, t, source));
            if (list.Count < 2) return;
            var ordered = list.OrderByDescending(EquipAtkOf).ToList();
            list.Clear();
            list.AddRange(ordered);
        }

        static int EquipAtkOf(CardInstance equip)
        {
            var prog = equip?.Def != null ? CompiledEffectCache.GetOrCompile(equip.Def) : null;
            var clause = prog?.ClauseList?.FirstOrDefault(x => x != null && x.Action == EffectActionKind.EquipThisToTarget);
            return clause?.EquipAtkBonus ?? 0;
        }

        /// <summary>
        /// ApplyClause arm: equip <paramref name="target"/> (the targeted Equip Spell, still in <paramref name="who"/>'s GY) to
        /// <paramref name="source"/> (Lua Duel.Equip(tp, tc, c)): nothing happens when the target left the GY, this card is not
        /// face-up on the field, no Spell &amp; Trap Zone is free, or the target can no longer be equipped to it.
        /// </summary>
        public static void ApplyEquipFromGy(DuelEngine engine, DuelistState who, CardInstance source, CardInstance target)
        {
            if (engine == null || who == null || source == null) return;
            if (target == null || !who.Graveyard.Contains(target))
            {
                engine.Log($"{source.Name}: the targeted Equip Spell is no longer in the GY — nothing is equipped.");
                return;
            }

            var host = source;
            var hostSide = engine.ControllerOf(host);
            if (hostSide == null || !hostSide.TryFindMonster(host, out _) || !host.FaceUp)
            {
                engine.Log($"{source.Name} is no longer face-up on the field — {target.Name} is not equipped.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, source, target))
            {
                engine.Log($"{source.Name}: {target.Name} cannot leave the GY — nothing is equipped.");
                return;
            }

            var zi = engine.FirstEmptySpellTrap(who);
            if (zi < 0)
            {
                engine.Log($"{source.Name}: no free Spell & Trap Zone — {target.Name} stays in the GY.");
                return;
            }

            if (!EquipFamilyRuntime.IsCorrectEquipTargetFromGy(engine, who, target, host))
            {
                engine.Log($"{source.Name}: {target.Name} cannot be equipped to {host.Name} — it stays in the GY.");
                return;
            }

            who.Graveyard.Remove(target);
            EquipFamilyRuntime.ForgetDestroyRecord(target);
            GyEntryStamps.Left(target);
            DuelEngine.RecordOwnerOnFieldEntry(target, who, who);
            who.SpellTrapZones[zi].Occupant = target;
            target.FaceUp = true;
            target.SetThisTurn = false;
            target.SentFromFieldTurnNumber = 0;
            target.Counters = 0;
            target.EquipTurnCounter = 0;
            target.AbsorbedBy = null;
            target.GrantedEquipHost = null;
            target.EquippedTo = host;
            if (!host.Equips.Contains(target))
                host.Equips.Add(target);
            var prog = CompiledEffectCache.GetOrCompile(target.Def);
            var equipClause = prog?.ClauseList?.FirstOrDefault(x => x != null && x.Action == EffectActionKind.EquipThisToTarget);
            if (equipClause != null && equipClause.ReplaceHostOriginalAtkDef && target.Def != null)
            {
                host.OriginalAtkOverride = target.Def.atk;
                host.OriginalDefOverride = target.Def.def;
            }

            engine.Log($"{source.Name}: {target.Name} is equipped from the GY to {host.Name}.");
            // ygopro Duel.Equip: EVENT_EQUIP (Armored Glass's window, Morale Boost).
            EquipFamilyRuntime.NoteEquipped(engine, target, host);
            engine.NotifyPublic();
        }

        // ───────────────────────────── Guardian Grarl ─────────────────────────────

        /// <summary>
        /// NegationsAndGySummonsRuntime.CanSummonByOwnHandProcedure: the extra conditions of a card's own hand procedure — "If
        /// this is the only card in your hand" (<see cref="EffectClause.RequiresOnlyCardInHand"/>), and the card's
        /// "Cannot be Summoned unless you control a face-up "X"" gate (Lua EFFECT_SPSUMMON_CONDITION; checked again by
        /// DuelEngine.SpecialSummonToField).
        /// </summary>
        public static bool OwnHandProcedureAllows(DuelistState who, CardInstance card, EffectClause proc, out string reason)
        {
            reason = "OK";
            if (who == null || card?.Def == null || proc == null) return true;
            if (proc.RequiresOnlyCardInHand && (who.Hand == null || who.Hand.Count != 1 || !who.Hand.Contains(card)))
            {
                reason = "Only while it is the only card in your hand.";
                return false;
            }

            var gate = PsctGrammar.SummonGateNamed(Rules.OfficialCardAuthority.OfficialText(card));
            if (!string.IsNullOrEmpty(gate) && !FieldSpellEffects.ControlsFaceUpNamed(who, gate))
            {
                reason = $"Requires a face-up \"{gate}\" you control.";
                return false;
            }

            return true;
        }

        // ───────────────────────────── Morale Boost ─────────────────────────────

        /// <summary>A face-up, not negated card in a Spell &amp; Trap Zone whose program carries <paramref name="action"/>.</summary>
        static IEnumerable<(DuelistState side, CardInstance card, EffectClause clause)> LiveCards(DuelEngine engine,
            EffectActionKind action)
        {
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField().ToList())
                {
                    var clause = LiveClause(engine, side, st, action);
                    if (clause != null) yield return (side, st, clause);
                }
            }
        }

        static EffectClause LiveClause(DuelEngine engine, DuelistState side, CardInstance st, EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _)) return null;
            var prog = CompiledEffectCache.GetOrCompile(st);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
            if (clause == null) return null;
            // Its own activation is still an unresolved Chain Link: its effects do not apply yet.
            if (engine.Chain != null &&
                engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, st) && !l.Resolved))
                return null;
            return clause;
        }

        /// <summary>
        /// The shared EVENT_EQUIP (<see cref="EquipModelRuntime.NoteEquipped"/>; Premature Burial's link): an Equip Spell Card
        /// was equipped to <paramref name="host"/> — each live Morale Boost triggers (its controller of that Equip Spell gains
        /// the amount).
        /// </summary>
        public static void NoteEquipped(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            // RevivalEquipsAndGearfried batch: Gearfried the Swordmaster's "each time an Equip Card(s) is equipped to this
            // card" (any Equip Card, not only Equip Spells).
            RevivalEquipsAndGearfriedRuntime.NoteEquipped(engine, equip, host);
            if (engine == null || engine.GameOver || host == null || !IsEquipSpellCard(equip) || equip.EquippedTo != host)
                return;
            var gainer = engine.ControllerOf(equip);
            if (gainer == null) return;
            foreach (var (side, card, clause) in LiveCards(engine, EffectActionKind.GainLpWhenEquipSpellEquipped).ToList())
            {
                // Its TRIGGER_F cannot be activated under Cold Wave or Sasuke Samurai #2 (re:IsSpellTrapEffect()).
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) continue;
                var amount = clause.Amount > 0 ? clause.Amount : 1000;
                var stamp = card.FieldEntryStamp;
                var equipName = equip.Name;
                SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
                {
                    if (engine.GameOver) return;
                    if (card.FieldEntryStamp != stamp ||
                        LiveClause(engine, side, card, EffectActionKind.GainLpWhenEquipSpellEquipped) == null)
                    {
                        engine.Log($"{card.Name} no longer applies on the field — its effect does not resolve.");
                        return;
                    }

                    engine.Log($"{card.Name}: {equipName} was equipped — {gainer.Name} gains {amount} LP.");
                    TextEffectRuntime.ApplyLifePointGain(engine, gainer, amount, card.Name);
                });
            }
        }

        /// <summary>
        /// One leave-field event for Equip Spell Cards: every <see cref="NoteEquipLeftField"/> until the outermost scope is
        /// disposed belongs to it (each controller is hit once). Nested scopes join the outer one.
        /// </summary>
        public static IDisposable EnterEquipLeaveEvent(DuelEngine engine)
        {
            if (engine == null) return Scope.Empty;
            StateOf(engine).Depth++;
            return new Scope(engine);
        }

        sealed class Scope : IDisposable
        {
            public static readonly Scope Empty = new(null);
            DuelEngine _engine;

            public Scope(DuelEngine engine) => _engine = engine;

            public void Dispose()
            {
                var engine = _engine;
                _engine = null;
                if (engine == null) return;
                var st = StateOf(engine);
                if (st.Depth > 0) st.Depth--;
                if (st.Depth == 0) CloseEvent(engine, st);
            }
        }

        /// <summary>
        /// <paramref name="equip"/> leaves the field while equipped (Lua GetEquipTarget()~=nil), or because its monster left
        /// (REASON_LOST_TARGET); <paramref name="controller"/> is its controller as it leaves. Only Equip Spell Cards count.
        /// </summary>
        public static void NoteEquipLeftField(DuelEngine engine, CardInstance equip, DuelistState controller)
        {
            if (engine == null || !IsEquipSpellCard(equip)) return;
            var ctrl = engine.ControllerOf(equip) ?? controller;
            if (ctrl == null) return;
            var st = StateOf(engine);
            if (!st.Left.Exists(x => ReferenceEquals(x.card, equip)))
                st.Left.Add((equip, ctrl));
            if (st.Depth == 0) CloseEvent(engine, st);
        }

        static void CloseEvent(DuelEngine engine, State st)
        {
            if (st.Left.Count == 0) return;
            var victims = st.Left.Select(x => x.controller).Where(x => x != null).Distinct().ToList();
            st.Left.Clear();
            if (engine.GameOver || victims.Count == 0) return;
            foreach (var (side, card, clause) in LiveCards(engine, EffectActionKind.DamageWhenEquipSpellLeavesField).ToList())
            {
                // Its TRIGGER_F cannot be activated under Cold Wave or Sasuke Samurai #2 (re:IsSpellTrapEffect()).
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) continue;
                var amount = clause.Amount > 0 ? clause.Amount : 1000;
                var stamp = card.FieldEntryStamp;
                var hit = victims.ToList();
                SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
                {
                    if (engine.GameOver) return;
                    if (card.FieldEntryStamp != stamp ||
                        LiveClause(engine, side, card, EffectActionKind.DamageWhenEquipSpellLeavesField) == null)
                    {
                        engine.Log($"{card.Name} no longer applies on the field — its effect does not resolve.");
                        return;
                    }

                    engine.Log($"{card.Name}: an Equip Spell Card left the field — " +
                               $"{string.Join(" and ", hit.Select(v => v.Name))} take{(hit.Count == 1 ? "s" : "")} {amount} damage.");
                    // Lua s.damop: the Morale Boost controller first (Damage step), then the other player; RDComplete.
                    if (hit.Count >= 2)
                    {
                        var first = hit.Contains(side) ? side : hit[0];
                        var second = hit.First(v => v != first);
                        engine.ApplyEffectDamageBoth(first, amount, second, amount, card.Name, card);
                    }
                    else
                        engine.ApplyEffectDamage(hit[0], amount, card.Name, card);
                });
            }
        }
    }
}
