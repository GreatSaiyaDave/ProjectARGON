using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleDamageEvents" (IOC / AST) runtime (atoms: <see cref="BattleDamageEventsAtoms"/>):
    /// <list type="bullet">
    /// <item>Piranha Army / Stone Statue of the Aztecs: <see cref="DoubleBattleDamage"/>, called by DuelEngine's damage
    /// calculation after the other battle-damage changes (Rod of the Mind's Eye, Dark Scorpion Combination), never on
    /// damage converted to effect damage (Gravekeeper's Vassal).</item>
    /// <item>Aswan Apparition / Nubian Guard: <see cref="TryOfferOptionalTarget"/>, the optional targeted "inflicts battle
    /// damage" trigger (a human gets a yes/no pick through <see cref="ContinuousTrapsAndSpiritSupportRuntime.AskOptionalCardPick"/>).</item>
    /// <item>Begone, Knave! / Des Counterblow: <see cref="FireAnyMonsterInflicted"/>, any monster's battle damage to either
    /// player (<see cref="EffectClause.AnyMonsterBattleDamageToAnyPlayer"/>), from TextEffectRuntime.FireInflictedBattleDamage
    /// after the monster's own triggers and Robbin' Goblin / Robbin' Zombie; they resolve in <see cref="ResolvePending"/>
    /// (DuelEngine.ApplyBattleDestructions), after battle destruction and the destroys-by-battle triggers.</item>
    /// </list>
    /// Like every battle-damage trigger in the engine these resolve in the Damage Step (no response window).
    /// Battle damage of 0 (Kuriboh, Waboku, Susa Soldier halving 1) is no battle damage: nothing fires or doubles.
    /// </summary>
    public static class BattleDamageEventsRuntime
    {
        /// <summary><see cref="EffectClause.TargetCardKind"/> of Nubian Guard's "1 Continuous Spell Card".</summary>
        public const string ContinuousSpellKind = "ContinuousSpell";

        /// <summary>A Continuous Spell Card (not a Continuous Trap).</summary>
        public static bool IsContinuousSpell(CardDef def) => def != null && def.IsSpell && def.IsContinuousSpellOrTrap;

        // ───────────────────────────── Piranha Army / Stone Statue of the Aztecs ─────────────────────────────

        static bool HasDoubler(CardInstance card, Func<EffectClause, bool> shape)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp).Exists(c =>
                c != null && c.Action == EffectActionKind.DoubleBattleDamageToOpponent && shape(c));
        }

        static bool VictimIsOpponentOf(DuelEngine engine, CardInstance card, DuelistState victim)
        {
            var controller = engine.ControllerOf(card);
            return controller != null && controller != victim && ReferenceEquals(engine.OpponentOf(controller), victim);
        }

        /// <summary>
        /// The battle damage <paramref name="victim"/> takes from the battle of <paramref name="attacker"/> (with
        /// <paramref name="targetOrNull"/>, null for a direct attack), doubled when Piranha Army attacked directly or Stone
        /// Statue of the Aztecs is the attack target and the victim is that monster's controller's opponent (Lua
        /// aux.ChangeBattleDamage(1, DOUBLE_DAMAGE): one doubling however many doublers apply).
        /// </summary>
        public static int DoubleBattleDamage(DuelEngine engine, CardInstance attacker, CardInstance targetOrNull,
            DuelistState victim, int damage)
        {
            if (engine == null || victim == null || damage <= 0) return damage;
            CardInstance doubler = null;
            if (targetOrNull == null && attacker != null &&
                HasDoubler(attacker, c => c.RequiresDirectAttack && !c.RequiresThisIsAttackTarget) &&
                VictimIsOpponentOf(engine, attacker, victim))
                doubler = attacker;
            else if (targetOrNull != null &&
                     HasDoubler(targetOrNull, c => c.RequiresThisIsAttackTarget && !c.RequiresDirectAttack) &&
                     VictimIsOpponentOf(engine, targetOrNull, victim))
                doubler = targetOrNull;
            if (doubler == null) return damage;
            var doubled = damage > int.MaxValue / 2 ? int.MaxValue : damage * 2;
            engine.Log($"{doubler.Name}: the battle damage {victim.Name} takes is doubled ({damage} → {doubled}).");
            return doubled;
        }

        // ───────────────────────────── Aswan Apparition / Nubian Guard ─────────────────────────────

        static bool IsOptionalGyToDeckTop(EffectClause c) =>
            c != null && c.Timing == EffectTiming.ThisCardInflictsBattleDamage && c.IsOptional &&
            c.RequiresTargetChoice && c.Action == EffectActionKind.PlaceTargetOnTopOfDeck &&
            (c.Zone == EffectZoneFilter.ControllerGyTraps || c.Zone == EffectZoneFilter.ControllerGySpells) &&
            c.EffectChoiceIndex == 0;

        static bool MatchesKind(EffectClause c, CardDef def)
        {
            if (def == null) return false;
            if (string.Equals(c.TargetCardKind, ContinuousSpellKind, StringComparison.OrdinalIgnoreCase))
                return IsContinuousSpell(def);
            if (string.Equals(c.TargetCardKind, "Trap", StringComparison.OrdinalIgnoreCase))
                return def.IsTrap;
            return false;
        }

        static List<CardInstance> Candidates(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c) =>
            TextEffectRuntime.CollectTriggerTargets(engine, who, c, source)
                .Where(t => t?.Def != null && who.Graveyard.Contains(t) && MatchesKind(c, t.Def))
                .ToList();

        /// <summary>
        /// "If this card inflicts Battle Damage to your opponent's Life Points, you can return 1 Trap / Continuous Spell
        /// Card from your Graveyard to the top of your Deck." (Lua TRIGGER_O + CARD_TARGET): with no such card it cannot
        /// be activated; the AI always returns one; a human taps the card (Yes, even a lone one) or Cancels (not
        /// activated). Resolution: the PlaceTargetOnTopOfDeck arm (Necrovalley negates it), only if the target is still
        /// in the GY. Returns true when the clause was handled here.
        /// </summary>
        public static bool TryOfferOptionalTarget(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || !IsOptionalGyToDeckTop(clause)) return false;
            var now = Candidates(engine, who, source, clause);
            if (now.Count == 0)
            {
                engine.Log($"{source.Name}: no {(clause.Zone == EffectZoneFilter.ControllerGyTraps ? "Trap" : "Continuous Spell")} " +
                           "in your GY — the effect cannot be activated.");
                return true;
            }

            var what = clause.Zone == EffectZoneFilter.ControllerGyTraps ? "Trap Card" : "Continuous Spell Card";
            ContinuousTrapsAndSpiritSupportRuntime.AskOptionalCardPick(engine, who, source,
                () => Candidates(engine, who, source, clause),
                TextEffectRuntime.TargetKindOf(clause),
                $"{source.Name}: you can return 1 {what} from your GY to the top of your Deck (Cancel: do not activate).",
                list => list == null || list.Count == 0
                    ? null
                    : TextEffectRuntime.AutoPickTriggerTargets(engine, who, clause, list, 1).FirstOrDefault() ?? list[0],
                pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{source.Name}: the effect is not activated.");
                        return;
                    }

                    if (!who.Graveyard.Contains(pick))
                    {
                        engine.Log($"{source.Name}: {pick.Name} is no longer in the GY — no effect.");
                        return;
                    }

                    engine.Log($"{source.Name}: {pick.Name} is returned to the top of the Deck.");
                    TextEffectRuntime.ApplyToTriggerTargets(engine, who, source, clause, new List<CardInstance> { pick });
                    engine.NotifyPublic();
                });
            return true;
        }

        // ───────────────────────────── Begone, Knave! / Des Counterblow ─────────────────────────────

        sealed class PendingLink
        {
            public DuelistState Player;
            public CardInstance Source;
            public EffectClause Clause;
            public CardInstance Monster;
        }

        sealed class PendingChain
        {
            public readonly List<PendingLink> Links = new List<PendingLink>();
        }

        static readonly ConditionalWeakTable<DuelEngine, PendingChain> s_pending =
            new ConditionalWeakTable<DuelEngine, PendingChain>();

        /// <summary>
        /// TextEffectRuntime.FireInflictedBattleDamage (ygopro EVENT_BATTLE_DAMAGE is raised): <paramref name="monster"/>
        /// inflicted battle damage to a player (<paramref name="wasDirect"/>: by a direct attack). Each face-up, not negated
        /// Spell/Trap on either field (the turn player's first) whose FullyCompiled
        /// <see cref="EffectClause.AnyMonsterBattleDamageToAnyPlayer"/> clause applies activates (Begone, Knave!; Des
        /// Counterblow only for a direct attack; Cold Wave stops these activated Spell/Trap triggers). They resolve in
        /// <see cref="ResolvePending"/>, after battle destruction, on the same chain as the destroys-by-battle triggers
        /// (aux.bdocon), so a monster's own "destroys by battle" trigger still activates.
        /// </summary>
        public static void FireAnyMonsterInflicted(DuelEngine engine, CardInstance monster, bool wasDirect)
        {
            if (engine == null || monster?.Def == null || engine.GameOver) return;
            var first = engine.TurnPlayer;
            var players = new[] { first, first != null ? engine.OpponentOf(first) : null };
            foreach (var player in players)
            {
                if (player == null) continue;
                foreach (var st in player.SpellTrapsOnField().ToList())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(st);
                    if (prog == null || !prog.FullyCompiled) continue;
                    foreach (var c in prog.ClausesFor(EffectTiming.ThisCardInflictsBattleDamage))
                    {
                        if (c == null || !c.AnyMonsterBattleDamageToAnyPlayer) continue;
                        if (c.Action != EffectActionKind.ReturnToHand && c.Action != EffectActionKind.Destroy) continue;
                        if (c.RequiresDirectAttack && !wasDirect) continue;
                        if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, st)) continue;
                        engine.Log($"{st.Name} activates: {monster.Name} inflicted battle damage.");
                        s_pending.GetOrCreateValue(engine).Links.Add(new PendingLink
                            { Player = player, Source = st, Clause = c, Monster = monster });
                    }
                }
            }
        }

        /// <summary>
        /// DuelEngine.ApplyBattleDestructions, after battle destruction and the destroys-by-battle triggers: resolve the
        /// Begone, Knave! / Des Counterblow links <see cref="FireAnyMonsterInflicted"/> activated for this battle
        /// (<paramref name="attacker"/> vs <paramref name="targetOrNull"/>), last link first (SEGOC: the turn player's
        /// mandatory triggers are chained first, so the opponent's resolve first). Lua s.operation: only while the monster
        /// is still on the field (tc:IsRelateToEffect); the Spell/Trap may have left the field (the activated effect still
        /// resolves), but one still face-up on the field and negated now does nothing. Links of another battle are dropped.
        /// </summary>
        public static void ResolvePending(DuelEngine engine, CardInstance attacker, CardInstance targetOrNull)
        {
            if (engine == null || !s_pending.TryGetValue(engine, out var chain) || chain.Links.Count == 0) return;
            var links = chain.Links.ToList();
            chain.Links.Clear();
            for (var i = links.Count - 1; i >= 0; i--)
            {
                if (engine.GameOver) return;
                var link = links[i];
                var monster = link.Monster;
                var st = link.Source;
                if (monster == null || (!ReferenceEquals(monster, attacker) && !ReferenceEquals(monster, targetOrNull)))
                    continue;
                if (link.Player.TryFindSpellTrap(st, out _) && st.FaceUp && st.IsNegated)
                {
                    engine.Log($"{st.Name} is negated — no effect.");
                    continue;
                }

                var holder = engine.ControllerOf(monster);
                if (holder == null || !holder.TryFindMonster(monster, out _))
                {
                    engine.Log($"{st.Name}: {monster.Name} is no longer on the field — no effect.");
                    continue;
                }

                switch (link.Clause.Action)
                {
                    case EffectActionKind.ReturnToHand:
                        if (ContinuousProtections.IsUnaffectedBy(engine, monster, st))
                        {
                            engine.Log($"{monster.Name} is unaffected by {st.Name}.");
                            break;
                        }

                        engine.Log($"{st.Name}: {monster.Name} inflicted battle damage — returned to the hand.");
                        engine.ReturnCardToHand(monster, by: link.Player);
                        break;
                    case EffectActionKind.Destroy:
                        engine.Log($"{st.Name}: {monster.Name} inflicted battle damage by a direct attack — destroyed.");
                        TextEffectRuntime.DestroyCard(engine, monster, st, byPlayer: link.Player);
                        break;
                }
            }

            engine.NotifyPublic();
        }
    }
}
