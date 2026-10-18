using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ContinuousNegation batch (v141) runtime: continuous Trap / Spell activation locks and negation
    /// (Jinzo, Royal Decree, Imperial Order, Spell Canceller) and Amplifier's exemption. Reads compiled
    /// programs only — no card-id branches.
    ///
    /// A <b>source</b> is a face-up card on the field (a monster, or a Spell/Trap whose activation has
    /// resolved) whose FullyCompiled program has a ContinuousWhileFaceUp
    /// <see cref="EffectActionKind.ContinuousCannotActivateTraps"/> /
    /// <see cref="EffectActionKind.ContinuousNegateTrapEffectsOnField"/> /
    /// <see cref="EffectActionKind.ContinuousCannotActivateSpells"/> /
    /// <see cref="EffectActionKind.ContinuousNegateSpellEffectsOnField"/> clause. A source applies only
    /// while no other applying source negates it (Jinzo negates Royal Decree; Royal Decree negates Imperial
    /// Order). Two sources that negate each other (two Royal Decrees) resolve by timestamp: the one that was
    /// face-up first negates the later one.
    ///
    /// Effects of an applying source:
    /// · lock — a Spell/Trap of that kind cannot be activated from the hand or the field
    ///   (<see cref="ActivationLocked"/>, checked by every activation gate);
    /// · negate — face-up Spells/Traps of that kind on the field get <see cref="CardInstance.IsNegated"/>
    ///   on each board refresh (<see cref="Refresh"/>), and an effect of that kind resolving on the field
    ///   does nothing (<see cref="NegatesResolvingClause"/>, <see cref="ChainLinkEffectNegated"/>).
    /// Maintenance costs (PayLpOrDestroyThis) are costs, not effects: they still apply while negated, and so
    /// does setup the Lua marks EFFECT_FLAG_CANNOT_DISABLE (the Swords turn countdowns, TextEffectRuntime).
    /// A negated take-control Equip (Snatch Steal) hands its monster back until the negation ends
    /// (<see cref="Refresh"/>). Resolution paths outside ApplyClause (Bark of Dark Ruler's LP pick, Magical
    /// Hats, Standby/End Phase subject clauses) ask <see cref="NegatesOnFieldResolution"/> /
    /// <see cref="NegatesResolvingClause"/> themselves.
    /// A card whose effect "cannot be negated" (EffectNegatable = false) is never negated.
    /// Amplifier (<see cref="EffectClause.EquipSparesControllerTrapsFromHostNegation"/>): the equipped
    /// source's Trap lock / negation does not apply to Traps its controller controls on the field.
    /// </summary>
    public static class ContinuousNegationRuntime
    {
        sealed class State
        {
            /// <summary>Sources in the order they were first seen applying (timestamp tie-break).</summary>
            public readonly List<CardInstance> Order = new();
            /// <summary>Spell/Trap cards whose <see cref="CardInstance.IsNegated"/> this runtime set.</summary>
            public HashSet<CardInstance> Flagged = new();
            /// <summary>Equip Spells whose equip resolution was negated: they go to the GY, not stay.</summary>
            public readonly HashSet<CardInstance> NegatedEquips = new();
            /// <summary>
            /// Take-control Equips (Snatch Steal) this runtime negated while their monster was taken: the
            /// monster went back to the other player, and returns to the Equip's controller once the
            /// negation ends. Equip → monster.
            /// </summary>
            public readonly Dictionary<CardInstance, CardInstance> ControlSuspended = new();
            /// <summary>Re-entrancy guard: a control change notifies, which can refresh the board again.</summary>
            public bool SyncingControl;
            /// <summary>Gorgon's Eye traps that are applying this turn (face-up on the field, still armed).</summary>
            public readonly List<CardInstance> GorgonEyes = new();
        }

        sealed class Source
        {
            public CardInstance Card;
            public bool LocksTraps;
            public bool LocksSpells;
            public bool NegatesTraps;
            public bool NegatesSpells;
            public bool TrapNegationSparesThis;
            public bool SpellNegationSparesThis;
            /// <summary>MonsterNegationAndControlLocks batch: Skill Drain negates face-up Effect Monsters on the field.</summary>
            public bool NegatesMonsters;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// "Trap Cards / Spell Cards, and their effects on the field, cannot be activated": true when
        /// <paramref name="card"/> (in <paramref name="who"/>'s hand or on the field) may not be activated
        /// because an applying source locks its kind. GY effects are never locked.
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card,
            out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster) return false;
            var trap = card.Def.IsTrap;
            var spell = card.Def.IsSpell;
            if (!trap && !spell) return false;
            // LocksAndZones batch (v142), Cold Wave: until its lock ends no Spell/Trap Card or Spell/Trap effect can
            // be activated at all (ygopro EFFECT_CANNOT_ACTIVATE, re:IsSpellTrapEffect — wherever the card is).
            if (LocksAndZonesRuntime.SpellTrapActivationLocked(engine, card, out reason)) return true;
            // v208 QC (QcHollowActionsRuntime): Xing Zhen Hu — its Set card targets cannot be activated.
            if (QcHollowActionsRuntime.SetCardActivationLocked(engine, card, out reason)) return true;
            // SpellTrapAnswersAndActivationLocks batch: Cursed Seal of the Forbidden Spell's name lock, Invader of Darkness's
            // Quick-Play lock (the card's activation, from the hand or Set).
            if (SpellTrapAnswersAndActivationLocksRuntime.ActivationLocked(engine, who, card, out reason)) return true;
            // SpellTrapSealsAndImmunity batch: Sonic Jammer's Spell Card lock (the card's activation, from the hand or Set).
            if (SpellTrapSealsAndImmunityRuntime.ActivationLocked(engine, who, card, out reason)) return true;
            // LocksRedirectsAndActivity batch: "you cannot activate cards or effects" — the player-wide
            // activation lock (Chaos Emperor Dragon - Envoy of the End), the same hub every activation site asks.
            if (LocksRedirectsAndActivityRuntime.ActivationLocked(engine, who, card))
            {
                reason = "You cannot activate cards or effects.";
                return true;
            }
            var onField = OnSpellTrapField(engine, card);
            var inHand = who.Hand != null && who.Hand.Contains(card);
            if (!onField && !inHand) return false;
            foreach (var src in ActiveSources(engine))
            {
                if (trap && src.LocksTraps)
                {
                    if (onField && SparesControllerTraps(engine, src.Card, card)) continue;
                    reason = $"{src.Card.Name}: Trap Cards cannot be activated.";
                    return true;
                }

                if (spell && src.LocksSpells)
                {
                    reason = $"{src.Card.Name}: Spell Cards cannot be activated.";
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The applying source that negates <paramref name="card"/>'s effects right now, or null. Location
        /// is the caller's business (a card being activated is treated as on the field).
        /// </summary>
        public static CardInstance NegatedBy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null) return null;
            // MonsterNegationAndControlLocks batch: Skill Drain covers a face-up Effect Monster in a Monster Zone.
            if (card.Def.IsMonster) return MonsterNegatedBy(engine, card);
            if (!card.Def.IsSpell && !card.Def.IsTrap) return null;
            foreach (var src in ActiveSources(engine))
            {
                if (src.Card != card && Covers(engine, src, card))
                    return src.Card;
            }

            return null;
        }

        /// <summary>
        /// SpiritsAndMisc batch: the applying source that negates Spell effects on the field (Imperial Order,
        /// Spell Canceller), for a card that is a Spell Card only while it sits in a Spell &amp; Trap Zone — a
        /// monster that "becomes an Equip Spell Card" (Kiseitai), whose printed Monster type
        /// <see cref="NegatedBy"/> skips. Null when none applies or the card's effects cannot be negated.
        /// </summary>
        public static CardInstance SpellEffectsOnFieldNegatedBy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !OnSpellTrapField(engine, card)) return null;
            if (!EffectCanBeNegated(card)) return null;
            foreach (var src in ActiveSources(engine))
            {
                if (src.Card != card && src.NegatesSpells)
                    return src.Card;
            }

            return null;
        }

        /// <summary>
        /// Chain-solving negation for <see cref="TextEffectRuntime"/>.ApplyClause: a Spell/Trap effect
        /// that resolves on the field (or is being activated from the hand) while an applying source
        /// negates its kind has no effect. Logs, and remembers an Equip Spell whose equip was negated so
        /// <see cref="ConsumeNegatedEquip"/> sends it to the GY instead of leaving it unequipped.
        /// </summary>
        public static bool NegatesResolvingClause(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || source?.Def == null || clause == null) return false;
            // Upkeep is a cost (Lua EFFECT_FLAG_CANNOT_DISABLE), paid even while negated.
            if (clause.Action == EffectActionKind.PayLpOrDestroyThis ||
                clause.Action == EffectActionKind.StandbyMaintenancePayLp)
                return false;
            // MonsterNegationAndControlLocks batch: Skill Drain — a monster effect resolving while its monster is face-up on
            // the field does nothing (it could still be activated); one resolving in the GY / hand applies.
            if (source.Def.IsMonster) return NegatesResolvingMonsterEffect(engine, source);
            if (!source.Def.IsSpell && !source.Def.IsTrap) return false;
            var onField = OnSpellTrapField(engine, source);
            var activating = !onField && IsActivationTiming(clause.Timing) && !clause.ResolvesFromGy &&
                             !clause.ActivatesFromHand && InAnyHand(engine, source);
            if (!onField && !activating) return false;
            var by = NegatedBy(engine, source);
            if (by == null) return false;
            engine.Log($"{source.Name}: its effect is negated by {by.Name}.");
            // An Equip Spell whose activation effect is negated equips nothing (EquipThisToTarget, or
            // Premature Burial's summon-then-equip): it is sent to the GY instead of staying unequipped.
            if (source.Def.IsEquipSpell && source.EquippedTo == null && IsActivationTiming(clause.Timing))
                StateOf(engine).NegatedEquips.Add(source);
            return true;
        }

        /// <summary>
        /// Resolution paths that apply a Spell/Trap effect outside <see cref="TextEffectRuntime"/>.ApplyClause
        /// (Bark of Dark Ruler's LP pick, Magical Hats' picks): true, and logged, when the card resolves
        /// face-up on the field while an applying source negates its kind. Costs already paid stay paid.
        /// </summary>
        public static bool NegatesOnFieldResolution(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source?.Def == null || source.Def.IsMonster) return false;
            if (!source.Def.IsSpell && !source.Def.IsTrap) return false;
            if (!OnSpellTrapField(engine, source)) return false;
            var by = NegatedBy(engine, source);
            if (by == null) return false;
            engine.Log($"{source.Name}: its effect is negated by {by.Name}.");
            return true;
        }

        /// <summary>
        /// Activation gate (TextEffectRuntime.CanActivate): a face-up Spell/Trap on the field whose effects an
        /// applying source negates cannot activate its own effects either (Magical Labyrinth's Tribute ignition
        /// under Imperial Order; ygopro refuses the activation of a disabled card's effect). A Set card being
        /// activated, or a card in the hand, is not face-up on the field and stays legal.
        /// </summary>
        public static bool NegatesEffectActivationOnField(DuelEngine engine, CardInstance card) =>
            engine != null && card?.Def != null && card.FaceUp && OnSpellTrapField(engine, card) &&
            NegatedBy(engine, card) != null;

        /// <summary>
        /// A Counter-Trap-style link (<see cref="ChainLink.NegatesActivation"/>) resolves outside
        /// ApplyClause: true when its Spell/Trap effect is negated on the field right now.
        /// </summary>
        public static bool ChainLinkEffectNegated(DuelEngine engine, ChainLink link)
        {
            var card = link?.Card;
            if (engine == null || card?.Def == null || card.Def.IsMonster) return false;
            if (!OnSpellTrapField(engine, card)) return false;
            var by = NegatedBy(engine, card);
            if (by == null) return false;
            engine.Log($"{card.Name}: its effect is negated by {by.Name}.");
            return true;
        }

        /// <summary>
        /// FinishSpellTrap hook: an Equip Spell whose equip resolution was negated is sent to the GY
        /// (an Equip Spell that equips nothing does not stay on the field).
        /// </summary>
        public static bool ConsumeNegatedEquip(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return false;
            var st = StateOf(engine);
            if (!st.NegatedEquips.Remove(card)) return false;
            return card.EquippedTo == null;
        }

        /// <summary>
        /// Board refresh (FieldSpellEffects.RefreshBoard, before any aura applies): recompute
        /// <see cref="CardInstance.IsNegated"/> on face-up Spells/Traps from the applying sources. Only
        /// flags this runtime set are ever cleared.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var st = StateOf(engine);
            var next = new HashSet<CardInstance>();
            var active = ActiveSources(engine);
            if (active.Count > 0)
            {
                foreach (var side in new[] { engine.Player, engine.Opponent })
                {
                    // MonsterNegationAndControlLocks batch: Skill Drain's face-up Effect Monsters, then Spells/Traps.
                    foreach (var card in side.MonstersOnField().Concat(side.SpellTrapsOnField()))
                    {
                        if (card?.Def == null || !card.FaceUp) continue;
                        var covered = false;
                        foreach (var src in active)
                        {
                            if (src.Card == card || !Covers(engine, src, card)) continue;
                            covered = true;
                            break;
                        }

                        // Never take over a negation some other effect set.
                        if (covered && (st.Flagged.Contains(card) || !card.IsNegated))
                            next.Add(card);
                    }
                }
            }

            foreach (var card in st.Flagged)
                if (card != null && !next.Contains(card))
                    card.IsNegated = false;
            foreach (var card in next)
                card.IsNegated = true;
            st.Flagged = next;
            SyncEquipControl(engine, st);
        }

        /// <summary>
        /// "Take control of the equipped monster" is a continuous Equip effect (Snatch Steal, Lua
        /// EFFECT_SET_CONTROL). While this runtime negates such an Equip, control of its monster returns
        /// to the other player (sent to the GY if that player has no free Monster Zone, as
        /// <c>RevertEquipTakeControl</c> does); when the negation ends and the Equip is still equipped to
        /// it, its controller takes control again.
        /// </summary>
        static void SyncEquipControl(DuelEngine engine, State st)
        {
            if (st.SyncingControl) return;
            st.SyncingControl = true;
            try
            {
                foreach (var eq in st.Flagged.ToList())
                {
                    if (eq == null || st.ControlSuspended.ContainsKey(eq)) continue;
                    var host = eq.EquippedTo;
                    if (host == null || !host.TakenByEquipControl || !TakesControlOfHost(eq)) continue;
                    var eqController = engine.ControllerOf(eq);
                    if (eqController == null || engine.ControllerOf(host) != eqController) continue;
                    // Back to the monster's owner (remembered while someone else controls it).
                    var other = host.ControlOwner ?? engine.OpponentOf(eqController);
                    if (other == null || other == eqController) continue;
                    st.ControlSuspended[eq] = host;
                    if (engine.TryTakeControl(other, host))
                        engine.Log($"{eq.Name} is negated: control of {host.Name} returns to {other.Name}.");
                    else
                    {
                        engine.Log($"{eq.Name} is negated: {host.Name} cannot return (no Monster Zone) and is sent to the GY.");
                        engine.SendCardToGrave(eqController, host);
                    }
                }

                foreach (var kv in st.ControlSuspended.ToList())
                {
                    var eq = kv.Key;
                    var host = kv.Value;
                    if (st.Flagged.Contains(eq) && eq.EquippedTo == host) continue;
                    st.ControlSuspended.Remove(eq);
                    if (eq.EquippedTo != host || !eq.FaceUp || !OnSpellTrapField(engine, eq)) continue;
                    var eqController = engine.ControllerOf(eq);
                    var hostController = engine.ControllerOf(host);
                    if (eqController == null || hostController == null || hostController == eqController) continue;
                    if (engine.TryTakeControl(eqController, host))
                    {
                        host.TakenByEquipControl = true;
                        engine.Log($"{eq.Name} is no longer negated: {eqController.Name} takes control of {host.Name} again.");
                    }
                }
            }
            finally
            {
                st.SyncingControl = false;
            }
        }

        static bool TakesControlOfHost(CardInstance equip)
        {
            var prog = equip?.Def != null ? CompiledEffectCache.GetOrCompile(equip.Def) : null;
            return prog != null && prog.ClauseList.Exists(c => c != null && c.TakeControlOfTarget);
        }

        // ------------------------------------------------------------------ sources

        static bool IsSourceAction(EffectActionKind a) =>
            a == EffectActionKind.ContinuousCannotActivateTraps ||
            a == EffectActionKind.ContinuousNegateTrapEffectsOnField ||
            a == EffectActionKind.ContinuousCannotActivateSpells ||
            a == EffectActionKind.ContinuousNegateSpellEffectsOnField ||
            a == EffectActionKind.ContinuousNegateMonsterEffectsOnField;

        static Source ReadSource(CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            Source src = null;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || c.Timing != EffectTiming.ContinuousWhileFaceUp || !IsSourceAction(c.Action))
                    continue;
                src ??= new Source { Card = card };
                switch (c.Action)
                {
                    case EffectActionKind.ContinuousCannotActivateTraps:
                        src.LocksTraps = true;
                        break;
                    case EffectActionKind.ContinuousCannotActivateSpells:
                        src.LocksSpells = true;
                        break;
                    case EffectActionKind.ContinuousNegateTrapEffectsOnField:
                        src.NegatesTraps = true;
                        src.TrapNegationSparesThis |= c.ExceptThisCard;
                        break;
                    case EffectActionKind.ContinuousNegateSpellEffectsOnField:
                        src.NegatesSpells = true;
                        src.SpellNegationSparesThis |= c.ExceptThisCard;
                        break;
                    case EffectActionKind.ContinuousNegateMonsterEffectsOnField:
                        src.NegatesMonsters = true;
                        break;
                }
            }

            return src;
        }

        /// <summary>Face-up sources on the field, in timestamp order.</summary>
        static List<Source> Candidates(DuelEngine engine)
        {
            var list = new List<Source>();
            if (engine?.Player == null || engine.Opponent == null) return list;
            var st = StateOf(engine);

            void Consider(CardInstance card)
            {
                if (card?.Def == null || !card.FaceUp) return;
                // A monster whose Summon is still in its summon-negation window is not on the field yet
                // (Solemn Judgment / Horn of Heaven can answer Jinzo's own Summon).
                if (engine.IsSummonPending(card)) return;
                // Negated by something other than this runtime (a monster-negation effect).
                if (card.IsNegated && !st.Flagged.Contains(card)) return;
                // A Continuous Spell/Trap applies only once its activation has resolved.
                if (ActivationPending(engine, card)) return;
                var src = ReadSource(card);
                if (src != null) list.Add(src);
            }

            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                foreach (var m in side.MonstersOnField()) Consider(m);
                foreach (var s in side.SpellTrapsOnField()) Consider(s);
            }

            st.Order.RemoveAll(c => !list.Exists(s => s.Card == c));
            foreach (var s in list)
                if (!st.Order.Contains(s.Card))
                    st.Order.Add(s.Card);
            list.Sort((a, b) => st.Order.IndexOf(a.Card).CompareTo(st.Order.IndexOf(b.Card)));
            return list;
        }

        /// <summary>
        /// Sources that apply: not negated by another applying source. Negation between sources is
        /// acyclic once mutual pairs are broken by timestamp, so the fixpoint below settles.
        /// </summary>
        static List<Source> ActiveSources(DuelEngine engine)
        {
            var cands = Candidates(engine);
            var n = cands.Count;
            if (n <= 1) return cands;
            var edge = new bool[n, n];
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                edge[i, j] = i != j && Covers(engine, cands[i], cands[j].Card);
            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (edge[i, j] && edge[j, i])
                    edge[j, i] = false; // the earlier source negates the later one
            var active = new bool[n];
            for (var i = 0; i < n; i++) active[i] = true;
            for (var round = 0; round <= n + 1; round++)
            {
                var changed = false;
                var next = new bool[n];
                for (var j = 0; j < n; j++)
                {
                    next[j] = true;
                    for (var i = 0; i < n; i++)
                    {
                        if (!active[i] || !edge[i, j]) continue;
                        next[j] = false;
                        break;
                    }

                    if (next[j] != active[j]) changed = true;
                }

                active = next;
                if (!changed) break;
            }

            var result = new List<Source>();
            for (var i = 0; i < n; i++)
                if (active[i])
                    result.Add(cands[i]);
            return result;
        }

        /// <summary>Would <paramref name="src"/> negate <paramref name="card"/> (a Spell/Trap)?</summary>
        static bool Covers(DuelEngine engine, Source src, CardInstance card)
        {
            if (src?.Card == null || card?.Def == null) return false;
            if (card.Def.IsMonster) return src.NegatesMonsters && CoversMonster(engine, card);
            var trap = card.Def.IsTrap;
            var spell = card.Def.IsSpell;
            if (trap && src.NegatesTraps)
            {
                if (card == src.Card && src.TrapNegationSparesThis) return false;
                if (!EffectCanBeNegated(card)) return false;
                return !SparesControllerTraps(engine, src.Card, card);
            }

            if (spell && src.NegatesSpells)
            {
                if (card == src.Card && src.SpellNegationSparesThis) return false;
                return EffectCanBeNegated(card);
            }

            return false;
        }

        /// <summary>"This card's … effect cannot be negated" (Amplifier, Spiritualism).</summary>
        static bool EffectCanBeNegated(CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) return true;
            foreach (var c in prog.ClauseList)
                if (c != null && !c.EffectNegatable)
                    return false;
            return true;
        }

        /// <summary>
        /// Amplifier: while an Equip with <see cref="EffectClause.EquipSparesControllerTrapsFromHostNegation"/>
        /// is equipped to <paramref name="host"/>, the host's Trap lock / negation does not apply to Trap
        /// Cards on the field controlled by the host's controller. The Equip's own effect cannot be negated,
        /// so no negation of the Equip is consulted.
        /// </summary>
        static bool SparesControllerTraps(DuelEngine engine, CardInstance host, CardInstance trap)
        {
            if (engine == null || host?.Equips == null || host.Equips.Count == 0 || trap == null) return false;
            var hostController = engine.ControllerOf(host);
            if (hostController == null || engine.ControllerOf(trap) != hostController) return false;
            foreach (var eq in host.Equips)
            {
                if (eq?.Def == null || !eq.FaceUp || eq.EquippedTo != host) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClauseList)
                    if (c != null && c.EquipSparesControllerTrapsFromHostNegation)
                        return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ helpers

        static bool ActivationPending(DuelEngine engine, CardInstance card)
        {
            var links = engine.Chain?.Links;
            if (links == null) return false;
            foreach (var l in links)
                if (l != null && l.Card == card && !l.Resolved)
                    return true;
            return false;
        }

        static bool OnSpellTrapField(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindSpellTrap(card, out _)) return true;
                if (side.FieldSpellZone != null && side.FieldSpellZone.Occupant == card) return true;
            }

            return false;
        }

        static bool InAnyHand(DuelEngine engine, CardInstance card) =>
            (engine.Player?.Hand != null && engine.Player.Hand.Contains(card)) ||
            (engine.Opponent?.Hand != null && engine.Opponent.Hand.Contains(card));

        static bool IsActivationTiming(EffectTiming t) =>
            t == EffectTiming.Activate ||
            t == EffectTiming.ChainLinkActivated ||
            t == EffectTiming.AttackDeclared ||
            t == EffectTiming.OpponentNormalOrFlipSummon ||
            t == EffectTiming.DamageCalculation ||
            t == EffectTiming.YouTakeLifePointDamage;

        // ------------------------------------------------------------------ MonsterNegationAndControlLocks batch

        /// <summary>
        /// Skill Drain (Lua EFFECT_DISABLE, target range both Monster Zones, s.disable: an Effect Monster, or one that was
        /// originally): the applying source that negates <paramref name="monster"/>'s effects right now — it is face-up in a
        /// Monster Zone, has an effect, and its effects can be negated. Null otherwise.
        /// </summary>
        static CardInstance MonsterNegatedBy(DuelEngine engine, CardInstance monster)
        {
            if (!CoversMonster(engine, monster)) return null;
            foreach (var src in ActiveSources(engine))
                if (src.Card != monster && src.NegatesMonsters)
                    return src.Card;
            return null;
        }

        /// <summary>A face-up Effect Monster in a Monster Zone whose effects can be negated (Skill Drain's targets).</summary>
        static bool CoversMonster(DuelEngine engine, CardInstance card)
        {
            if (card?.Def == null || !card.FaceUp || !card.Def.IsMonster || card.IsToken) return false;
            if (!TextEffectRuntime.IsTrueEffectMonster(card.Def)) return false;
            if (!InMonsterZone(engine, card)) return false;
            return EffectCanBeNegated(card);
        }

        static bool InMonsterZone(DuelEngine engine, CardInstance card) =>
            (engine.Player != null && engine.Player.TryFindMonster(card, out _)) ||
            (engine.Opponent != null && engine.Opponent.TryFindMonster(card, out _));

        /// <summary>
        /// <see cref="NegatesResolvingClause"/> for a monster's effect: negated (and logged) while the monster is face-up in a
        /// Monster Zone and an applying Skill Drain covers it. A monster that left the field (Tributed as a cost, sent to the
        /// GY) or was turned face-down before resolution resolves its effect.
        /// </summary>
        static bool NegatesResolvingMonsterEffect(DuelEngine engine, CardInstance monster)
        {
            var by = MonsterNegatedBy(engine, monster);
            if (by == null) return false;
            engine.Log($"{monster.Name}: its effect is negated by {by.Name}.");
            return true;
        }

        /// <summary>
        /// Registered monster scripts that resolve outside ApplyClause (MonsterEffects: Man-Eater Bug, Magician of Faith,
        /// Abyss Soldier): true, and logged, when <paramref name="monster"/>'s effect resolves while Skill Drain negates it
        /// (face-up on the field). Its costs already paid stay paid.
        /// </summary>
        public static bool NegatesMonsterEffectOnField(DuelEngine engine, CardInstance monster) =>
            engine != null && monster?.Def != null && monster.Def.IsMonster &&
            NegatesResolvingMonsterEffect(engine, monster);

        // ───────────────────────── Gorgon's Eye ─────────────────────────

        /// <summary>
        /// Gorgon's Eye (c52648457) at resolution: arm this turn's negation of every face-up monster in face-up
        /// Defense Position on both Monster Zones. It is a field rule (ygopro EFFECT_DISABLE with
        /// RESET_PHASE|PHASE_END), so <see cref="Refresh"/> recomputes it on every board change and
        /// <see cref="EndTurn"/> clears it.
        /// </summary>
        public static void ArmGorgonEyes(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || source == null) return;
            var st = StateOf(engine);
            if (!st.GorgonEyes.Contains(source)) st.GorgonEyes.Add(source);
            engine.Log($"{source.Name}: until the end of this turn the effects of Defense Position monsters are negated.");
            Refresh(engine);
        }

        /// <summary>
        /// Gorgon's Eye: recompute <see cref="CardInstance.GorgonEyeNegated"/> on every monster both players
        /// control. The negation ends when no armed Trap is face-up on the field, when the monster is no longer
        /// face-up in Defense Position, and at the end of the turn (<see cref="EndTurn"/>).
        /// </summary>
        public static void RefreshGorgonEyes(DuelEngine engine)
        {
            if (engine == null) return;
            var st = StateOf(engine);
            // Gorgon's Eye is a Normal Trap and goes to the GY, but ygopro registers its field EFFECT_DISABLE on the
            // card itself, so the negation lasts the turn wherever the card ended up. Only the turn ends it
            // (EndTurn) — do not drop it while it is off the field or negated.
            if (st.GorgonEyes.Count == 0) return;
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                    if (m != null) m.GorgonEyeNegated = m.FaceUp && m.Position == BattlePosition.Defense;
            }
        }

        /// <summary>End Phase: Gorgon's Eye's turn has ended (RESET_PHASE|PHASE_END).</summary>
        public static void EndTurn(DuelEngine engine)
        {
            if (engine == null) return;
            if (!s_state.TryGetValue(engine, out var st)) return;
            st.GorgonEyes.Clear();
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                    if (m != null) m.GorgonEyeNegated = false;
            }
        }
    }
}
