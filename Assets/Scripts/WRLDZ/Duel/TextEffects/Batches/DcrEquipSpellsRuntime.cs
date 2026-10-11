using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// DcrEquipSpells batch runtime (<see cref="DcrEquipSpellsAtoms"/>). Reads compiled programs only — no card-id branches.
    /// Every grant here is an Equip Card's own effect (ygopro EFFECT_TYPE_EQUIP or an SZONE field effect): it applies while
    /// the Equip Card is face-up in a Spell &amp; Trap Zone, equipped to that monster, not negated (The Emperor's Holiday
    /// included) and FullyCompiled — whether or not the monster's own effects are negated.
    /// <list type="bullet">
    /// <item>Direct attack (<see cref="GrantsDirectAttack"/>, DuelEngine.GrantsDirectAttack) — Shooting Star Bow - Ceal.</item>
    /// <item>Extra attacks (<see cref="ExtraAttacks"/>, DuelEngine.ExtraAttacksAllowed) — Twin Swords of Flashing Light -
    /// Tryce.</item>
    /// <item>Fixed battle damage (<see cref="ChangeBattleDamage"/>, the battle-damage site of DuelEngine, next to Susa
    /// Soldier's halving) — Rod of the Mind's Eye.</item>
    /// <item>Battle negation (<see cref="EquipBattleNegator"/>, NegationMonstersRuntime.BattleNegator) — Wicked-Breaking
    /// Flamberge - Baou.</item>
    /// <item>Spell target negation (<see cref="EquipTargetNegators"/>, NegationMonstersRuntime.TryNegateTargetingLink and
    /// SweepPersistentTargets) — Rod of Silence - Kay'est.</item>
    /// <item>Return to the hand (<see cref="FireReturnThisToHand"/>, EquipAndSpellProtectionRuntime's destroyed-while-
    /// equipped trigger) — Butterfly Dagger - Elma.</item>
    /// <item>The AI's equip target (<see cref="TryAutoPick"/>) — its own monster.</item>
    /// </list>
    /// The "Send 1 card from your hand to the GY" cost (Tryce, Flamberge) is the discard-cost pick with
    /// <see cref="EffectClause.DiscardCostIsSend"/> (TextEffectRuntime.StartOrPayDiscardThenTarget and its human
    /// continuation).
    /// </summary>
    public static class DcrEquipSpellsRuntime
    {
        // ───────────────────────────── shared: live Equip Card markers ─────────────────────────────

        /// <summary>The controller of a card in a Spell &amp; Trap Zone, or null.</summary>
        static DuelistState SpellTrapHolder(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            if (engine.Player != null && engine.Player.TryFindSpellTrap(card, out _)) return engine.Player;
            if (engine.Opponent != null && engine.Opponent.TryFindSpellTrap(card, out _)) return engine.Opponent;
            return null;
        }

        /// <summary>An Equip Card whose continuous effects apply now: face-up in a S/T Zone, not negated, FullyCompiled.</summary>
        static bool EquipEffectsApply(DuelEngine engine, CardInstance eq, out CompiledCardProgram prog)
        {
            prog = null;
            if (eq?.Def == null || !eq.FaceUp || eq.IsNegated || eq.IsEquipSpellWithGrantedEffectOnly) return false;
            if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq)) return false;
            if (SpellTrapHolder(engine, eq) == null) return false;
            prog = CompiledEffectCache.GetOrCompile(eq);
            // FullyCompiled only: a Stub's fragment never grants anything.
            return prog != null && prog.FullyCompiled;
        }

        /// <summary>The live <paramref name="action"/> markers of the Equip Cards equipped to <paramref name="host"/>.</summary>
        internal static IEnumerable<(CardInstance Equip, EffectClause Clause)> LiveEquipMarkers(DuelEngine engine,
            CardInstance host, EffectActionKind action)
        {
            if (engine == null || host?.Equips == null || host.Equips.Count == 0) yield break;
            foreach (var eq in host.Equips.ToList())
            {
                if (eq == null || !ReferenceEquals(eq.EquippedTo, host)) continue;
                if (!EquipEffectsApply(engine, eq, out var prog)) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    if (c != null && c.Action == action)
                        yield return (eq, c);
            }
        }

        // ───────────────────────────── Shooting Star Bow / Tryce ─────────────────────────────

        /// <summary>DuelEngine.GrantsDirectAttack: an Equip Card lets the monster attack directly (EFFECT_DIRECT_ATTACK).</summary>
        public static bool GrantsDirectAttack(DuelEngine engine, CardInstance attacker) =>
            attacker != null && attacker.FaceUp &&
            LiveEquipMarkers(engine, attacker, EffectActionKind.EquippedCanAttackDirectly).Any();

        /// <summary>DuelEngine.ExtraAttacksAllowed: the extra attacks an Equip Card grants (EFFECT_EXTRA_ATTACK value).</summary>
        public static int ExtraAttacks(DuelEngine engine, CardInstance attacker)
        {
            if (attacker == null || !attacker.FaceUp) return 0;
            var extra = 0;
            foreach (var (_, c) in LiveEquipMarkers(engine, attacker, EffectActionKind.EquippedGainsExtraAttacks))
                extra = System.Math.Max(extra, System.Math.Max(0, c.Amount));
            return extra;
        }

        // ───────────────────────────── Rod of the Mind's Eye ─────────────────────────────

        /// <summary>
        /// DuelEngine damage calculation: battle damage <paramref name="inflictor"/> inflicts to <paramref name="victim"/>
        /// becomes the Equip's amount when <paramref name="victim"/> is the opponent of that Equip Card's controller
        /// (aux.ChangeBattleDamage(1, N): e:GetOwnerPlayer() == 1-damp). No damage stays no damage.
        /// </summary>
        public static int ChangeBattleDamage(DuelEngine engine, CardInstance inflictor, DuelistState victim, int damage)
        {
            if (engine == null || inflictor == null || victim == null || damage <= 0) return damage;
            foreach (var (eq, c) in LiveEquipMarkers(engine, inflictor, EffectActionKind.EquippedBattleDamageToOpponentBecomes))
            {
                var holder = SpellTrapHolder(engine, eq);
                if (holder == null || !ReferenceEquals(engine.OpponentOf(holder), victim) || c.Amount <= 0) continue;
                if (c.Amount != damage)
                    engine.Log($"{eq.Name}: the battle damage {inflictor.Name} inflicts becomes {c.Amount} ({damage} → {c.Amount}).");
                return c.Amount;
            }

            return damage;
        }

        // ───────────────────────────── Wicked-Breaking Flamberge - Baou ─────────────────────────────

        /// <summary>
        /// NegationMonstersRuntime.BattleNegator: the face-up Equip Card on <paramref name="battler"/> whose EVENT_BATTLED
        /// effect negates <paramref name="other"/> (its battle target, destroyed by that battle; an Effect Monster), or null.
        /// </summary>
        public static CardInstance EquipBattleNegator(DuelEngine engine, CardInstance battler, CardInstance other)
        {
            if (other?.Def == null) return null;
            foreach (var (eq, c) in LiveEquipMarkers(engine, battler, EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle))
            {
                if (!c.AppliesToEquippedHost) continue;
                if (c.TargetCardKind == "Effect" && !other.Def.IsEffectMonster) continue;
                return eq;
            }

            return null;
        }

        // ───────────────────────────── Rod of Silence - Kay'est ─────────────────────────────

        /// <summary>
        /// Every face-up Equip Spell whose "negate Spell effects that target the equipped monster" applies now, with the
        /// monster it covers and its controller (NegationMonstersRuntime: EVENT_CHAIN_SOLVING and the SZONE
        /// EFFECT_DISABLE / SELF_DESTROY sweep).
        /// </summary>
        public static List<(CardInstance Protector, EffectClause Clause, CardInstance Host, DuelistState Holder)>
            EquipTargetNegators(DuelEngine engine)
        {
            var list = new List<(CardInstance, EffectClause, CardInstance, DuelistState)>();
            if (engine?.Player == null || engine.Opponent == null) return list;
            foreach (var holder in new[] { engine.Player, engine.Opponent })
            foreach (var st in holder.SpellTrapsOnField().ToList())
            {
                var host = st?.EquippedTo;
                if (host?.Def == null || !host.FaceUp || engine.ControllerOf(host) == null) continue;
                if (!EquipEffectsApply(engine, st, out var prog)) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                    if (c != null && c.Action == EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy &&
                        c.AppliesToEquippedHost)
                        list.Add((st, c, host, holder));
            }

            return list;
        }

        // ───────────────────────────── Butterfly Dagger - Elma ─────────────────────────────

        /// <summary>
        /// EquipAndSpellProtectionRuntime.Fire: this card was destroyed while equipped and is in the GY — "You can return
        /// this card to the hand". A human answers yes / no (tap the card or Cancel); the AI always returns it. It misses
        /// the timing like any optional "When" trigger; Necrovalley stops it.
        /// </summary>
        public static void FireReturnThisToHand(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            var owner = GyOwner(engine, card);
            if (owner == null) return; // s.retcon: c:IsLocation(LOCATION_GRAVE)
            if (c != null && c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation &&
                SimultaneousTriggersRuntime.MissesWhenTiming(engine, card))
            {
                engine.Log($"{card.Name}: more of the Chain resolved after it was destroyed — its \"When … you can\" " +
                           "effect misses the timing.");
                return;
            }

            if (ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log($"{card.Name}: it cannot leave the GY — its effect does not apply.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, owner, card, new CardPickRequest
            {
                Candidates = () => owner.Graveyard.Contains(card) ? new List<CardInstance> { card } : new List<CardInstance>(),
                Kind = EffectTargetKind.SpellInYourGy,
                Prompt = $"{card.Name}: return this card to your hand? Tap it, or CANCEL to leave it in the GY.",
                AskWithOneCandidate = true,
                AiChoose = l => l.FirstOrDefault(),
                Finish = pick => ReturnToHand(engine, owner, card),
                Cancel = () => engine.Log($"{card.Name} stays in the GY.")
            });
        }

        /// <summary>The <see cref="EffectActionKind.ReturnThisFromGyToHand"/> arm: this card, still in a GY, goes to its hand.</summary>
        public static void ReturnToHand(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var owner = GyOwner(engine, card) ?? who;
            if (engine == null || owner == null || card == null || !owner.Graveyard.Contains(card)) return;
            if (ContinuousProtections.GyMovementLocked(engine, card, card))
            {
                engine.Log($"{card.Name}: it cannot leave the GY.");
                return;
            }

            owner.Graveyard.Remove(card);
            GyEntryStamps.Left(card);
            owner.Hand.Add(card);
            engine.Log($"{card.Name} returns from the GY to {owner.Name}'s hand.");
            engine.NotifyPublic();
        }

        static DuelistState GyOwner(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            if (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        // ───────────────────────────── AI ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.AutoPick: an Equip that helps its monster goes on the AI's strongest face-up monster; with none,
        /// on the weakest monster it may equip (the target is mandatory once the card is activated).
        /// </summary>
        internal static bool TryAutoPick(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> targets,
            out CardInstance pick)
        {
            pick = null;
            if (engine == null || who == null || c == null || !c.AiEquipsOwnMonster ||
                c.Action != EffectActionKind.EquipThisToTarget || targets == null || targets.Count == 0)
                return false;
            pick = targets.Where(t => t != null && who.TryFindMonster(t, out _))
                       .OrderByDescending(t => t.CurrentAtk).FirstOrDefault() ??
                   targets.Where(t => t != null).OrderBy(t => t.CurrentAtk).FirstOrDefault();
            return pick != null;
        }
    }
}
