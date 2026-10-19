using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TlmEquipsAndLinkedContinuous batch runtime (<see cref="TlmEquipsAndLinkedContinuousAtoms"/>):
    /// <list type="bullet">
    /// <item>Name lists (<see cref="EffectClause.AnyOfNamedCards"/>, Lua IsCode with several codes): <see cref="NamedInList"/>,
    /// read by the text-path target filter (<see cref="FilterTargets"/>), the equip limit (<see cref="EquipLimitAllows"/>,
    /// EquipFamilyRuntime) and the activation conditions (<see cref="TryActivationCondition"/>).</item>
    /// <item>Legendary Black Belt: <see cref="FireEquipSpellDestroysByBattle"/> from
    /// TextEffectRuntime.NotifyDestroyedOpponentByBattle; <see cref="InflictDestroyedDef"/> is its ApplyClause arm.</item>
    /// <item>Nitro Unit: <see cref="NoteHostDestroyed"/> / <see cref="AfterHostDestroyed"/> from DuelEngine.DestroyMonster
    /// (the Necklace of Command shape, RdsRedoSpellTrapRuntime): the trigger is queued with the simultaneous triggers.</item>
    /// <item>Lone Wolf: <see cref="ShieldsFromOpponentMonsterEffect"/> (ContinuousProtections.IsUnaffectedBy), its battle
    /// immunity through OfficialEffectRegistry.BoundCannotBeDestroyedByBattle, and <see cref="StaysWhenLinkedMonsterLeaves"/>
    /// (DuelEngine.UnlinkEquips).</item>
    /// <item>Impenetrable Formation: <see cref="SuppressesEquipBonus"/> (FieldSpellEffects.ApplyEquipStats).</item>
    /// <item>Lone Wolf / Impenetrable Formation register their effects on the monster (EFFECT_FLAG_OWNER_RELATE,
    /// RESETS_STANDARD): turned face-down, it loses them for good while the link stays (<see cref="NoteLinked"/>,
    /// <see cref="LinkEffectsLive"/>).</item>
    /// <item>Kaminote Blow: <see cref="RegisterEndOfDamageStepDestroy"/> and <see cref="FireEndOfDamageStep"/> from
    /// TextEffectRuntime.NotifyEndOfDamageStep.</item>
    /// </list>
    /// </summary>
    public static class TlmEquipsAndLinkedContinuousRuntime
    {
        /// <summary>Lone Wolf: you control exactly 1 monster, and it is a face-up monster named in the list.</summary>
        public const string OnlyMonsterIsFaceUpNamed = "TlmOnlyMonsterIsFaceUpNamed";

        /// <summary>Kaminote Blow: a face-up monster named in the list is in your Monster Zone.</summary>
        public const string NamedFaceUpInYourMonsterZone = "TlmNamedFaceUpInYourMonsterZone";

        // ───────────────────────────── name lists ─────────────────────────────

        /// <summary><paramref name="card"/> has one of the '|'-separated names of <paramref name="list"/>.</summary>
        public static bool NamedInList(CardInstance card, string list)
        {
            if (card == null || string.IsNullOrEmpty(list)) return false;
            foreach (var n in list.Split('|'))
            {
                var name = n.Trim();
                if (name.Length > 0 && card.IsNamed(name)) return true;
            }

            return false;
        }

        /// <summary>TextEffectRuntime.CollectTargets (batch filter tail): a named-list target is one of those names.</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || string.IsNullOrEmpty(c.AnyOfNamedCards)) return;
            list.RemoveAll(t => !NamedInList(t, c.AnyOfNamedCards));
        }

        /// <summary>EquipFamilyRuntime.ClauseLimitAllows: Legendary Black Belt's equip limit (IsCode(list)).</summary>
        public static bool EquipLimitAllows(EffectClause c, CardInstance monster) =>
            c == null || string.IsNullOrEmpty(c.AnyOfNamedCards) || NamedInList(monster, c.AnyOfNamedCards);

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>Activation-condition hook (TextEffectRuntime.ActivationConditionsMet default chain).</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null) return false;
            if (c.ActivationCondition == OnlyMonsterIsFaceUpNamed)
            {
                // Lua s.target chk==0: #Duel.GetFieldGroup(tp, LOCATION_MZONE, 0) == 1 and that card is face-up and listed.
                var mine = who?.MonstersOnField().Where(m => m != null).ToList();
                ok = mine != null && mine.Count == 1 && mine[0].FaceUp && NamedInList(mine[0], c.AnyOfNamedCards);
                reason = ok ? null : "Activate only if the 1 and only monster you control is a face-up " + Listed(c) + ".";
                return true;
            }

            if (c.ActivationCondition == NamedFaceUpInYourMonsterZone)
            {
                ok = who != null && who.MonstersOnField().Any(m => m != null && m.FaceUp &&
                                                                   NamedInList(m, c.AnyOfNamedCards));
                reason = ok ? null : "Activate only if you control a face-up " + Listed(c) + ".";
                return true;
            }

            return false;
        }

        static string Listed(EffectClause c) =>
            string.Join(", ", (c?.AnyOfNamedCards ?? "").Split('|').Select(n => "\"" + n.Trim() + "\""));

        // ───────────────────────────── Legendary Black Belt ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.NotifyDestroyedOpponentByBattle: <paramref name="destroyer"/> (still on the field, controlled by
        /// <paramref name="who"/>) destroyed <paramref name="destroyed"/> by battle. Each face-up Equip Spell Card equipped to
        /// it whose effects apply and whose program has an <see cref="EffectTiming.EquippedMonsterDestroysByBattle"/>
        /// <see cref="EffectActionKind.InflictDestroyedMonsterDefToOpponent"/> clause activates (TRIGGER_F; Lua s.damcon: the
        /// destroyed monster is in the GY by battle). It resolves inline, as every battle-destroying trigger of this engine.
        /// </summary>
        public static void FireEquipSpellDestroysByBattle(DuelEngine engine, DuelistState who, CardInstance destroyer,
            CardInstance destroyed)
        {
            if (engine == null || destroyer?.Equips == null || destroyer.Equips.Count == 0 || destroyed?.Def == null) return;
            if (!InGyByBattle(engine, destroyed)) return;
            foreach (var eq in destroyer.Equips.ToList())
            {
                if (engine.GameOver) return;
                if (eq?.Def == null || eq.Def.IsMonster || !eq.Def.IsEquipSpell || !eq.FaceUp) continue;
                if (!ReferenceEquals(eq.EquippedTo, destroyer) || eq.IsNegated) continue;
                if (ContinuousProtections.EquipCardEffectsAreNegated(engine, eq)) continue;
                var ctrl = engine.ControllerOf(eq);
                if (ctrl == null || !ctrl.TryFindSpellTrap(eq, out _)) continue;
                // Its TRIGGER_F cannot be activated under Cold Wave (a Spell/Trap effect).
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, eq)) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.EquippedMonsterDestroysByBattle))
                {
                    if (c == null || c.Action != EffectActionKind.InflictDestroyedMonsterDefToOpponent) continue;
                    engine.Log($"{eq.Name}: the equipped {destroyer.Name} destroyed {destroyed.Name} by battle — its " +
                               "effect activates.");
                    var dummy = false;
                    TextEffectRuntime.ApplyClause(engine, ctrl, eq, c, destroyed, ref dummy, ref dummy);
                }
            }
        }

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.InflictDestroyedMonsterDefToOpponent"/>: <paramref name="who"/>'s
        /// opponent takes damage equal to the DEF <paramref name="destroyed"/> has in the GY (its printed DEF; Lua
        /// bc:GetDefense()), while <paramref name="source"/> is still on the field (c:IsRelateToEffect).
        /// </summary>
        public static void InflictDestroyedDef(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance destroyed)
        {
            if (engine == null || who == null || source == null || destroyed?.Def == null) return;
            var ctrl = engine.ControllerOf(source);
            if (ctrl == null || !ctrl.TryFindSpellTrap(source, out _)) return;
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            var dmg = Math.Max(0, destroyed.Def.def);
            engine.Log($"{source.Name}: {dmg} damage to {opp.Name} (the DEF of {destroyed.Name}).");
            if (dmg > 0) engine.ApplyEffectDamage(opp, dmg, source.Name, source);
        }

        static bool InGyByBattle(DuelEngine engine, CardInstance card) =>
            InGraveyard(engine, card) && (card.GyEntryReason & GyEntryReason.Battle) != 0;

        static bool InGraveyard(DuelEngine engine, CardInstance card) =>
            card != null && ((engine.Player?.Graveyard?.Contains(card) ?? false) ||
                             (engine.Opponent?.Graveyard?.Contains(card) ?? false));

        // ───────────────────────────── Nitro Unit ─────────────────────────────

        /// <summary>The Nitro Unit-style Equip Spell Cards of a monster being destroyed by battle.</summary>
        public sealed class LostHosts
        {
            internal readonly List<(CardInstance Equip, CardInstance Host)> Items = new();
        }

        /// <summary>
        /// DuelEngine.DestroyMonster, before <paramref name="host"/>'s Equip Cards are unlinked: each face-up Equip Spell Card
        /// equipped to it whose program carries <see cref="EffectActionKind.InflictLostHostAtkToOpponentWhenHostDestroyedByBattle"/>,
        /// when the host is destroyed by battle. Null: none.
        /// </summary>
        public static LostHosts NoteHostDestroyed(DuelEngine engine, CardInstance host, bool byBattle)
        {
            if (engine == null || !byBattle || host?.Equips == null || host.Equips.Count == 0) return null;
            LostHosts found = null;
            foreach (var eq in host.Equips)
            {
                if (eq?.Def == null || eq.Def.IsMonster || !eq.Def.IsEquipSpell || !eq.FaceUp) continue;
                if (!ReferenceEquals(eq.EquippedTo, host)) continue;
                var ctrl = engine.ControllerOf(eq);
                if (ctrl == null || !ctrl.TryFindSpellTrap(eq, out _)) continue;
                var prog = CompiledEffectCache.GetOrCompile(eq);
                if (prog == null || !prog.FullyCompiled) continue;
                if (!prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                        c != null && c.Action == EffectActionKind.InflictLostHostAtkToOpponentWhenHostDestroyedByBattle))
                    continue;
                found ??= new LostHosts();
                found.Items.Add((eq, host));
            }

            return found;
        }

        /// <summary>
        /// DuelEngine.DestroyMonster, after the destroyed monster's Equip Cards were unlinked: each noted Equip Spell Card
        /// that the rule took off the field (REASON_LOST_TARGET; a GY or, under Banisher of the Light, banished) queues its
        /// mandatory trigger with the other simultaneous triggers. As it resolves (Lua s.damcon / s.damtg): the former host is
        /// in the GY, destroyed by battle, and the opponent of the player who holds this card takes damage equal to that
        /// monster's ATK in the GY (its printed ATK).
        /// </summary>
        public static void AfterHostDestroyed(DuelEngine engine, LostHosts lost)
        {
            if (engine == null || lost == null || engine.GameOver) return;
            foreach (var (eq, host) in lost.Items)
            {
                var card = eq;
                var former = host;
                // Still equipped somewhere or still on the field (kept by a replacement): no REASON_LOST_TARGET.
                if (card.EquippedTo != null) continue;
                var holder = engine.ControllerOf(card) ?? EndOfDamageStepRidersRuntime.HolderOffField(engine, card);
                if (holder == null) continue;
                SimultaneousTriggersRuntime.QueueTrigger(engine, holder, card, optional: false, () =>
                {
                    if (engine.GameOver) return;
                    // Left the field by now (a deferred rule send is over), and its monster is in the GY by battle.
                    if (engine.ControllerOf(card) != null) return;
                    var who = EndOfDamageStepRidersRuntime.HolderOffField(engine, card);
                    if (who == null || !InGyByBattle(engine, former)) return;
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) return;
                    var opp = engine.OpponentOf(who);
                    if (opp == null) return;
                    var dmg = Math.Max(0, former.Def?.atk ?? 0);
                    engine.Log($"{card.Name}: {dmg} damage to {opp.Name} (the ATK of {former.Name}).");
                    if (dmg > 0) engine.ApplyEffectDamage(opp, dmg, card.Name, card);
                });
                engine.Log($"{card.Name} left the field because its monster was destroyed by battle — its effect activates.");
            }
        }

        // ───────────────────────────── linked effects (Lone Wolf / Impenetrable Formation) ─────────────────────────────

        sealed class LinkRecord
        {
            public CardInstance Host;
            public int FaceDownStamp;
            public int FieldEntryStamp;
        }

        static readonly ConditionalWeakTable<CardInstance, LinkRecord> s_links = new();

        /// <summary>
        /// TextEffectRuntime.ApplyClause (<see cref="EffectActionKind.LinkThisToTarget"/>): a link of this batch registers its
        /// effects on <paramref name="host"/> now (Lua tc:RegisterEffect, RESET_EVENT|RESETS_STANDARD).
        /// </summary>
        public static void NoteLinked(CardInstance link, CardInstance host, EffectClause clause)
        {
            if (link == null || host == null || clause == null) return;
            if (!clause.LinkStaysWhenHostLeaves && !clause.EquipBonusRequiresTwoMonstersNoneInAttack) return;
            s_links.Remove(link);
            s_links.Add(link, new LinkRecord
            {
                Host = host,
                FaceDownStamp = host.FaceDownStamp,
                FieldEntryStamp = host.FieldEntryStamp
            });
        }

        /// <summary>
        /// The effects <paramref name="link"/> registered on <paramref name="host"/> still apply: the same monster, not turned
        /// face-down since (RESET_TURN_SET). True for every link this batch did not record.
        /// </summary>
        public static bool LinkEffectsLive(CardInstance link, CardInstance host)
        {
            if (link == null || host == null) return false;
            if (!s_links.TryGetValue(link, out var r)) return true;
            return ReferenceEquals(r.Host, host) && host.FaceDownStamp == r.FaceDownStamp &&
                   host.FieldEntryStamp == r.FieldEntryStamp;
        }

        static bool LiveLinkClause(CardInstance link, CardInstance host, EffectActionKind action)
        {
            if (link?.Def == null || !link.FaceUp || link.IsNegated || !ReferenceEquals(link.EquippedTo, host)) return false;
            if (!LinkEffectsLive(link, host)) return false;
            var prog = CompiledEffectCache.GetOrCompile(link);
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c => c != null && c.Action == action);
        }

        /// <summary>
        /// ContinuousProtections.IsUnaffectedBy — Lone Wolf (EFFECT_IMMUNE_EFFECT, s.efilter): <paramref name="victim"/>, a
        /// face-up monster linked to a face-up card with <see cref="EffectActionKind.LinkedMonsterUnaffectedByOpponentMonsterEffects"/>,
        /// is unaffected by a monster effect (<paramref name="source"/> is a monster Card that is not acting as an Equip Card)
        /// of the opponent of that card's controller.
        /// </summary>
        public static bool ShieldsFromOpponentMonsterEffect(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (engine == null || victim?.Equips == null || victim.Equips.Count == 0 || source?.Def == null) return false;
            if (!victim.FaceUp || !source.Def.IsMonster || source.EquippedTo != null) return false;
            var vc = engine.ControllerOf(victim);
            if (vc == null || !vc.TryFindMonster(victim, out _)) return false;
            var sourceSide = HolderOf(engine, source);
            if (sourceSide == null) return false;
            foreach (var link in victim.Equips)
            {
                if (!LiveLinkClause(link, victim, EffectActionKind.LinkedMonsterUnaffectedByOpponentMonsterEffects))
                    continue;
                var lc = engine.ControllerOf(link);
                if (lc != null && !ReferenceEquals(lc, sourceSide)) return true;
            }

            return false;
        }

        /// <summary>
        /// OfficialEffectRegistry.BoundCannotBeDestroyedByBattle: a linked card of this batch whose effects on
        /// <paramref name="host"/> ended (turned face-down since it was linked) gives no battle immunity.
        /// </summary>
        public static bool BoundEffectsEnded(CardInstance link, CardInstance host) => !LinkEffectsLive(link, host);

        /// <summary>The player who controls <paramref name="card"/>, or holds it in the hand, GY or banished pile.</summary>
        static DuelistState HolderOf(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine.ControllerOf(card);
            if (ctrl != null) return ctrl;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if ((side.Hand?.Contains(card) ?? false) || (side.Graveyard?.Contains(card) ?? false) ||
                    (side.Banished?.Contains(card) ?? false))
                    return side;
            }

            return null;
        }

        /// <summary>
        /// DuelEngine.UnlinkEquips: Lone Wolf (<see cref="EffectClause.LinkStaysWhenHostLeaves"/>) only loses the link when its
        /// monster leaves the field; it is not sent anywhere.
        /// </summary>
        public static bool StaysWhenLinkedMonsterLeaves(CardInstance linkCard)
        {
            if (linkCard?.Def == null || linkCard.Def.IsMonster || linkCard.Def.IsEquipSpell) return false;
            var prog = CompiledEffectCache.GetOrCompile(linkCard.Def);
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(EffectTiming.Activate).Exists(c => c != null && c.LinkStaysWhenHostLeaves);
        }

        /// <summary>
        /// FieldSpellEffects.ApplyEquipStats — Impenetrable Formation (s.rcon): the bonus of <paramref name="clause"/> on
        /// <paramref name="link"/> does not apply to <paramref name="host"/> unless the host's controller controls 2 or more
        /// monsters (face-down ones count) and none of them is in Attack Position, and the effect on the host is still live.
        /// </summary>
        public static bool SuppressesEquipBonus(DuelEngine engine, CardInstance link, EffectClause clause, CardInstance host)
        {
            if (clause == null || !clause.EquipBonusRequiresTwoMonstersNoneInAttack) return false;
            if (engine == null || host == null || !LinkEffectsLive(link, host)) return true;
            var ctrl = engine.ControllerOf(host);
            if (ctrl == null) return true;
            var mons = ctrl.MonstersOnField().Where(m => m != null).ToList();
            return mons.Count < 2 || mons.Exists(m => m.Position == BattlePosition.Attack);
        }

        // ───────────────────────────── Kaminote Blow ─────────────────────────────

        sealed class Grant
        {
            public DuelistState Who;
            public int Turn;
            public string Names;
            public CardInstance Source;
        }

        static readonly ConditionalWeakTable<DuelEngine, List<Grant>> s_grants = new();

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.DestroyMonstersBattlingNamedAtEndOfDamageStepThisTurn"/>: a player
        /// effect of <paramref name="who"/> until the End Phase (Lua Duel.RegisterEffect, RESET_PHASE|PHASE_END).
        /// </summary>
        public static void RegisterEndOfDamageStepDestroy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null || string.IsNullOrEmpty(clause.AnyOfNamedCards)) return;
            var list = s_grants.GetOrCreateValue(engine);
            list.RemoveAll(g => g.Turn != engine.TurnNumber);
            list.Add(new Grant { Who = who, Turn = engine.TurnNumber, Names = clause.AnyOfNamedCards, Source = source });
            engine.Log($"{source?.Name ?? "Effect"}: this turn, the monsters that battle {Listed(clause)} controlled by " +
                       $"{who.Name} are destroyed at the end of the Damage Step.");
        }

        /// <summary>
        /// TextEffectRuntime.NotifyEndOfDamageStep (EVENT_DAMAGE_STEP_END, a continuous effect: before the triggers there).
        /// For each Kaminote Blow grant of this turn (Lua s.desop): with an attack target, when the attacker is a listed
        /// monster its player controls (on the field, or its owner's when it left) and the attack target is still in a
        /// Monster Zone, the attack target is destroyed; or the other way round.
        /// </summary>
        public static void FireEndOfDamageStep(DuelEngine engine, CardInstance attacker, CardInstance defender)
        {
            if (engine == null || attacker == null || defender == null || engine.GameOver) return;
            if (!s_grants.TryGetValue(engine, out var list) || list.Count == 0) return;
            foreach (var g in list.ToList())
            {
                if (engine.GameOver) return;
                if (g.Turn != engine.TurnNumber) continue;
                CardInstance victim = null;
                if (ListedYours(engine, attacker, g) && InMonsterZone(engine, defender)) victim = defender;
                else if (ListedYours(engine, defender, g) && InMonsterZone(engine, attacker)) victim = attacker;
                if (victim == null) continue;
                engine.Log($"{g.Source?.Name ?? "Effect"}: {victim.Name} battled {Listed(g.Names)} — it is destroyed at the " +
                           "end of the Damage Step.");
                TextEffectRuntime.DestroyCard(engine, victim, g.Source, byPlayer: g.Who);
            }
        }

        static string Listed(string names) =>
            string.Join(" / ", (names ?? "").Split('|').Select(n => "\"" + n.Trim() + "\""));

        /// <summary>Lua s.cfilter: a listed monster whose controller is the grant's player (off the field: its holder).</summary>
        static bool ListedYours(DuelEngine engine, CardInstance card, Grant g)
        {
            if (!NamedInList(card, g.Names)) return false;
            var side = engine.ControllerOf(card) ?? EndOfDamageStepRidersRuntime.HolderOffField(engine, card);
            return ReferenceEquals(side, g.Who);
        }

        static bool InMonsterZone(DuelEngine engine, CardInstance card)
        {
            var side = engine.ControllerOf(card);
            return side != null && side.TryFindMonster(card, out _);
        }
    }
}
