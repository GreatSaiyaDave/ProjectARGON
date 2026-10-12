using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipLocksAndLeaveField batch runtime (see <see cref="EquipLocksAndLeaveFieldAtoms"/>):
    /// <list type="bullet">
    /// <item>Demotion: <see cref="ApplyEquippedLevelChange"/> (called by FieldSpellEffects on each board refresh).</item>
    /// <item>Raregold Armor: <see cref="OnlyEquippedHostMayBeAttacked"/> (ContinuousProtections.CannotBeAttackTarget, which
    /// DuelEngine.CanBeAttacked / TryAttack, LegalIntentService and SimpleAi already ask).</item>
    /// <item>Metalsilver Armor: <see cref="ShieldedFromTargeting"/> when a target is chosen —
    /// TextEffectRuntime.CollectTargets (text path), SpellTrapEffects.CollectLegalTargets (legacy mirror), the
    /// MonsterEffects target lists and the batch-owned target lists that already honour Lord of D (Dark Spirit of the
    /// Silent, Soul Demolition, Tailor of the Fickle, DeckReturns guard). Not re-checked at resolution: like Lua
    /// EFFECT_CANNOT_BE_EFFECT_TARGET, a target chosen before the lock applied stays a legal target.</item>
    /// <item>Big Bang Shot's leave-field banish: DuelEngine.UnlinkCardFromHostOnLeave
    /// (<see cref="EffectClause.BanishHostWhenThisLeaves"/>).</item>
    /// </list>
    /// No card-id branches: every check reads the compiled clauses.
    /// </summary>
    public static class EquipLocksAndLeaveFieldRuntime
    {
        // ───────────────────────── Demotion ─────────────────────────

        /// <summary>
        /// A ContinuousReduceLevel clause with <see cref="EffectClause.AppliesToEquippedHost"/> on the face-up Equip Card
        /// <paramref name="equip"/>: its monster's Level changes by -Amount (EFFECT_TYPE_EQUIP + EFFECT_UPDATE_LEVEL). A
        /// monster with no Level (Xyz / Link) is not changed. Returns false when the clause is not host-scoped.
        /// </summary>
        public static bool ApplyEquippedLevelChange(DuelEngine engine, CardInstance equip, EffectClause clause)
        {
            if (clause == null || !clause.AppliesToEquippedHost ||
                clause.Action != EffectActionKind.ContinuousReduceLevel)
                return false;
            var host = equip?.EquippedTo;
            if (engine == null || host?.Def == null || !host.FaceUp) return true;
            var ctrl = engine.ControllerOf(host);
            if (ctrl == null || !ctrl.TryFindMonster(host, out _)) return true;
            if (HasNoLevel(host)) return true;
            host.LevelModifier -= Math.Abs(clause.Amount);
            return true;
        }

        static bool HasNoLevel(CardInstance m)
        {
            var type = m?.Def?.type;
            if (string.IsNullOrEmpty(type)) return false;
            return type.IndexOf("XYZ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ───────────────────────── Shared: an active Armor lock ─────────────────────────

        /// <summary>
        /// Face-up Equip Cards carrying <paramref name="action"/> whose effects apply and whose controller controls the
        /// equipped monster (Lua s.atkcon / s.cond: ec:GetControler()==e:GetHandlerPlayer()). Yields (controller, Equip
        /// Card, equipped monster).
        /// </summary>
        static IEnumerable<(DuelistState holder, CardInstance equip, CardInstance host)> ActiveLocks(DuelEngine engine,
            EffectActionKind action)
        {
            if (engine == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated || st.EquippedTo == null) continue;
                    if (ContinuousProtections.EquipCardEffectsAreNegated(engine, st)) continue;
                    if (!HasContinuous(st, action)) continue;
                    var host = st.EquippedTo;
                    if (!side.TryFindMonster(host, out _)) continue;
                    yield return (side, st, host);
                }
            }
        }

        static bool HasContinuous(CardInstance card, EffectActionKind action)
        {
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action && c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    return true;
            return false;
        }

        // ───────────────────────── Raregold Armor ─────────────────────────

        /// <summary>
        /// Raregold Armor: <paramref name="monster"/> cannot be selected as an attack target because its controller also
        /// controls an active Raregold-style Equip Card equipped to ANOTHER monster they control (Lua
        /// EFFECT_CANNOT_SELECT_BATTLE_TARGET, value c ~= GetEquipTarget()). The opponent's direct attacks are not
        /// restricted by it.
        /// </summary>
        public static bool OnlyEquippedHostMayBeAttacked(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster == null) return false;
            var defender = engine.ControllerOf(monster);
            if (defender == null || !defender.TryFindMonster(monster, out _)) return false;
            foreach (var (holder, _, host) in ActiveLocks(engine, EffectActionKind.OpponentCannotAttackOtherThanEquippedHost))
                if (holder == defender && !ReferenceEquals(host, monster))
                    return true;
            return false;
        }

        // ───────────────────────── Metalsilver Armor ─────────────────────────

        /// <summary>
        /// Metalsilver Armor: <paramref name="card"/> cannot be targeted by a card effect of <paramref name="targeter"/>.
        /// Lua: EFFECT_CANNOT_BE_EFFECT_TARGET on every monster in a Monster Zone (face-down too), GY or banished
        /// (face-up only) on either side, except the equipped monster, against the opponent's effects (aux.tgoval);
        /// only while the Equip Card's controller controls the equipped monster.
        /// </summary>
        public static bool ShieldedFromTargeting(DuelEngine engine, DuelistState targeter, CardInstance card)
        {
            if (engine == null || targeter == null || card?.Def == null) return false;
            if (!InShieldedLocation(engine, card)) return false;
            foreach (var (holder, _, host) in ActiveLocks(engine,
                         EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost))
            {
                if (holder == targeter) continue;
                if (ReferenceEquals(host, card)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Lua efftg + target range 0x34: a monster in a Monster Zone, a monster in a GY, a face-up banished monster.</summary>
        static bool InShieldedLocation(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindMonster(card, out _)) return true;
                if (side.Graveyard != null && side.Graveyard.Contains(card)) return card.Def.IsMonster;
                if (side.Banished != null && side.Banished.Contains(card)) return card.Def.IsMonster && card.FaceUp;
            }
            return false;
        }

        /// <summary>
        /// The player whose card effect this is: its controller on the field, else the player whose hand, GY or banished
        /// cards hold it (a hand or GY effect). Null when unknown.
        /// </summary>
        public static DuelistState EffectPlayerOf(DuelEngine engine, CardInstance source)
        {
            if (engine == null || source == null) return null;
            var ctrl = engine.ControllerOf(source);
            if (ctrl != null) return ctrl;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if ((side.Hand != null && side.Hand.Contains(source)) ||
                    (side.Graveyard != null && side.Graveyard.Contains(source)) ||
                    (side.Banished != null && side.Banished.Contains(source)))
                    return side;
            }
            return null;
        }

        /// <summary>
        /// Target-list guard by source card (MonsterEffects / EquipModel lists): the effect of <paramref name="source"/>
        /// cannot target <paramref name="card"/>. False when the source is unknown.
        /// </summary>
        public static bool ShieldedFromSource(DuelEngine engine, CardInstance card, CardInstance source) =>
            source != null && ShieldedFromTargeting(engine, EffectPlayerOf(engine, source), card);

        /// <summary>
        /// Text path (TextEffectRuntime.CollectTargets): drop the shielded cards from a TARGETING clause's list. A
        /// choice that does not target (ChoiceDoesNotTarget, lowest / highest ATK picks) is not filtered (the same rule
        /// CompiledActivationChainRuntime.Record uses for isTarget).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> list)
        {
            if (engine == null || who == null || c == null || list == null || list.Count == 0) return;
            if (!TargetingTruthRuntime.PicksTarget(c)) return;
            list.RemoveAll(t => ShieldedFromTargeting(engine, who, t));
        }

        /// <summary>
        /// Legacy Spell/Trap mirror (SpellTrapEffects.CollectLegalTargets): drop the shielded cards for the target kinds
        /// that pick monsters (on the field, in a GY, banished). Cost picks (Tribute, send, discard) are not targets.
        /// </summary>
        public static void FilterLegacyTargets(DuelEngine engine, DuelistState who, EffectTargetKind kind,
            List<CardInstance> list)
        {
            if (engine == null || who == null || list == null || list.Count == 0) return;
            switch (kind)
            {
                case EffectTargetKind.MonsterInEitherGy:
                case EffectTargetKind.OppFaceUpMonster:
                case EffectTargetKind.OppFaceUpMonsterAtkLeqLp:
                case EffectTargetKind.AnyMonsterOnField:
                case EffectTargetKind.AnyCardOnField:
                case EffectTargetKind.EquipMonsterYouControl:
                case EffectTargetKind.CardInEitherGy:
                case EffectTargetKind.OppMonster:
                case EffectTargetKind.YourMonster:
                case EffectTargetKind.EquipAnyMonster:
                case EffectTargetKind.FaceUpMonsterOnField:
                case EffectTargetKind.MonsterInYourBanished:
                case EffectTargetKind.FaceDownMonsterOnField:
                    list.RemoveAll(t => ShieldedFromTargeting(engine, who, t));
                    break;
            }
        }
    }
}
