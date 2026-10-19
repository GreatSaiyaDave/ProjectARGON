using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// BattleRiders batch runtime (atoms: <see cref="BattleRidersAtoms"/>).
    /// <list type="bullet">
    /// <item>B.E.S. Big Core: <see cref="RemoveCounterOrDestroy"/> (TextEffectRuntime.FireEndOfDamageFor → ApplyClause).
    /// Kangaroo Champ reuses <see cref="EndOfDamageStepRidersRuntime.ChangeBattledMonsterToDefense"/>.</item>
    /// <item>Poison Fangs / Spiral Spear Strike: <see cref="DealerTriggerApplies"/>, the gate of the Robbin' Goblin loop in
    /// TextEffectRuntime.FireInflictedBattleDamage (the controller of the damage dealer's face-up Spell/Trap Cards).
    /// Spiral Spear Strike's piercing: <see cref="NamedGrantsPiercing"/> (BattleBoostsRuntime.FieldGrantsPiercing).</item>
    /// <item>Catnipped Kitty: <see cref="FilterTargets"/> / <see cref="FilterLegacyTargets"/> (both target mirrors) and
    /// <see cref="SetDefZeroUntilEndOfTurn"/>; its shield is AttackShieldsAndDamageZero's.</item>
    /// <item>Armed Samurai - Ben Kei: <see cref="ExtraAttacksPerEquip"/> (DuelEngine.ExtraAttacksAllowed).</item>
    /// </list>
    /// Like every end-of-Damage-Step and battle-damage trigger in the engine these resolve in the Damage Step, inline (no
    /// response window; known residual).
    /// </summary>
    public static class BattleRidersRuntime
    {
        static bool OnFieldMonster(DuelEngine engine, CardInstance card)
        {
            var ctrl = engine?.ControllerOf(card);
            return ctrl != null && ctrl.TryFindMonster(card, out _);
        }

        static IEnumerable<string> SplitNames(string joined) =>
            (joined ?? "").Split('|').Select(n => n.Trim()).Where(n => n.Length > 0);

        /// <summary><paramref name="card"/> has one of the '|'-separated printed names (its rules name included).</summary>
        static bool HasAnyName(CardInstance card, string joined) =>
            card != null && SplitNames(joined).Any(card.IsNamed);

        // ───────────────────────────── B.E.S. Big Core ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.RemoveCounterFromThisOrDestroyAtEndOfDamageStep"/> (aux.EnableBESRemove): this card,
        /// face-up, unnegated and still related to the battle, loses <see cref="EffectClause.Amount"/> counter(s); with
        /// fewer counters it is destroyed. Counters of an earlier field presence do not count
        /// (<see cref="TokensDeclarationsAndPicksRuntime.CountersOfLastPresence"/>). A direct attack is a battle too.
        /// </summary>
        public static void RemoveCounterOrDestroy(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            // A SINGLE trigger of a face-up monster: no activation while negated or face-down, or once it left the battle.
            if (source.IsNegated || !source.FaceUp || !who.TryFindMonster(source, out _)) return;
            if (!EndOfDamageStepRidersRuntime.IsRelatedToBattle(engine, source)) return;
            var need = Math.Max(1, clause.Amount);
            var have = TokensDeclarationsAndPicksRuntime.CountersOfLastPresence(source);
            if (have >= need)
            {
                source.Counters = have - need;
                engine.Log($"{source.Name}: {need} counter(s) removed at the end of the Damage Step " +
                           $"({source.Counters} left).");
                return;
            }

            engine.Log($"{source.Name}: no counter to remove at the end of the Damage Step — it is destroyed.");
            TextEffectRuntime.DestroyCard(engine, source, source, byPlayer: who);
        }

        // ───────────────────────────── Poison Fangs / Spiral Spear Strike ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.FireInflictedBattleDamage (an <see cref="EffectClause.AnyControllerMonsterBattleDamage"/>
        /// clause of <paramref name="card"/>, a face-up Spell/Trap Card of <paramref name="who"/>, whose monster
        /// <paramref name="dealer"/> just inflicted battle damage to the opponent): the clause's dealer filters hold —
        /// <see cref="EffectClause.RaceFilter"/> (its live Type, Poison Fangs), <see cref="EffectClause.NamedCard"/>
        /// (Spiral Spear Strike: "Gaia the Dragon Champion") and <see cref="EffectClause.RequiresDealerBattledDefensePosition"/>
        /// (its battle target is in Defense Position). Clauses with none of them (Robbin' Goblin, Robbin' Zombie) always
        /// apply. Spiral Spear Strike's draw does nothing with an empty Deck (Lua returns before Duel.Draw).
        /// </summary>
        public static bool DealerTriggerApplies(DuelEngine engine, DuelistState who, CardInstance card,
            CardInstance dealer, EffectClause c)
        {
            if (c == null || !c.AnyControllerMonsterBattleDamage) return true;
            if (!string.IsNullOrEmpty(c.RaceFilter) &&
                (dealer == null || !ClassicEraTemplates.RaceMatches(dealer.Race, c.RaceFilter)))
                return false;
            if (!string.IsNullOrEmpty(c.NamedCard) && !HasAnyName(dealer, c.NamedCard)) return false;
            if (c.RequiresDealerBattledDefensePosition)
            {
                var bc = EndOfDamageStepRidersRuntime.BattleTargetOf(engine, dealer);
                if (bc == null || bc.Position != BattlePosition.Defense) return false;
            }

            if (c.Action == EffectActionKind.DrawThenDiscard && (who?.Deck == null || who.Deck.Count == 0))
            {
                engine?.Log($"{card?.Name}: {who?.Name} has no cards in the Deck — no effect.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// BattleBoostsRuntime.FieldGrantsPiercing: a face-up <paramref name="attacker"/> with one of the names of the
        /// <see cref="EffectActionKind.GrantPiercingToYourNamedMonsters"/> clause (the caller only offers the attacker
        /// controller's own face-up, unnegated Spell/Trap Cards).
        /// </summary>
        public static bool NamedGrantsPiercing(CardInstance attacker, EffectClause c) =>
            attacker != null && attacker.FaceUp && c != null &&
            c.Action == EffectActionKind.GrantPiercingToYourNamedMonsters && HasAnyName(attacker, c.NamedCard);

        // ───────────────────────────── Catnipped Kitty ─────────────────────────────

        /// <summary>TextEffectRuntime.CollectTargets: Catnipped Kitty's target has DEF above 0 (Card.HasNonZeroDefense).</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || c.Action != EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn) return;
            list.RemoveAll(t => t == null || !t.FaceUp || t.CurrentDef <= 0);
        }

        /// <summary>SpellTrapEffects.CollectLegalTargets (legacy mirror): the same filter for the card being activated.</summary>
        public static void FilterLegacyTargets(CardInstance activating, List<CardInstance> list)
        {
            if (activating?.Def == null || list == null) return;
            var prog = CompiledEffectCache.GetOrCompile(activating.Def);
            if (prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                FilterTargets(c, list);
        }

        /// <summary>
        /// <see cref="EffectActionKind.TargetDefBecomesZeroUntilEndOfTurn"/>: the target, still a face-up monster on the
        /// field and not unaffected, has its DEF set to 0 until the end of this turn (EFFECT_SET_DEFENSE_FINAL; later DEF
        /// changes do not raise it; it ends at the End Phase, face-down or on leaving the field).
        /// </summary>
        public static void SetDefZeroUntilEndOfTurn(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null || clause == null) return;
            var name = source?.Name ?? "Effect";
            if (target == null || !OnFieldMonster(engine, target) || !target.FaceUp)
            {
                engine.Log($"{name}: the target is not a face-up monster on the field — no effect.");
                return;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, target, source))
            {
                engine.Log($"{target.Name} is unaffected by {name}.");
                return;
            }

            target.DefZeroUntilEndOfTurn = true;
            s_defZero.GetOrCreateValue(engine).Add((target, target.FieldEntryStamp, target.FaceUpStamp));
            engine.Log($"{name}: the DEF of {target.Name} becomes 0 until the end of this turn.");
        }

        /// <summary>The monsters <see cref="SetDefZeroUntilEndOfTurn"/> marked, with the field presence the mark belongs to.</summary>
        static readonly ConditionalWeakTable<DuelEngine, List<(CardInstance Card, int Entry, int FaceUp)>> s_defZero = new();

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: RESETS_STANDARD — the DEF 0 ends once its monster left the field, was turned
        /// face-down or is on a new presence (the End Phase reset is DuelEngine.ClearUntilEndOfTurnStatMods).
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine == null || !s_defZero.TryGetValue(engine, out var marks) || marks.Count == 0) return;
            for (var i = marks.Count - 1; i >= 0; i--)
            {
                var (card, entry, faceUp) = marks[i];
                if (card == null || !card.DefZeroUntilEndOfTurn)
                {
                    marks.RemoveAt(i);
                    continue;
                }

                if (OnFieldMonster(engine, card) && card.FaceUp && card.FieldEntryStamp == entry &&
                    card.FaceUpStamp == faceUp)
                    continue;
                card.DefZeroUntilEndOfTurn = false;
                marks.RemoveAt(i);
            }
        }

        // ───────────────────────────── Armed Samurai - Ben Kei ─────────────────────────────

        /// <summary>
        /// DuelEngine.ExtraAttacksAllowed (<see cref="EffectClause.ExtraAttacksPerEquippedCard"/>): Amount additional
        /// attacks per Equip Card equipped to <paramref name="attacker"/> now (Lua c:GetEquipCount(): every card equipped
        /// to it in a Spell &amp; Trap Zone — Equip Spells, monsters equipped as Equip Cards, Unions).
        /// </summary>
        public static int ExtraAttacksPerEquip(DuelEngine engine, CardInstance attacker, EffectClause clause)
        {
            if (engine == null || attacker?.Equips == null || clause == null) return 0;
            var n = attacker.Equips.Distinct().Count(e =>
            {
                if (e == null || !ReferenceEquals(e.EquippedTo, attacker)) return false;
                // v210 cross-group audit: a link recorded in Equips that is not an Equip Card (Call of the Haunted,
                // Rare Metalmorph, Spellbinding Circle: Lua SetCardTarget) is not counted by GetEquipCount.
                if (!TextEffectRuntime.IsRealEquipCard(e)) return false;
                var holder = engine.ControllerOf(e);
                return holder != null && holder.TryFindSpellTrap(e, out _);
            });
            return Math.Max(0, clause.Amount) * n;
        }
    }
}
