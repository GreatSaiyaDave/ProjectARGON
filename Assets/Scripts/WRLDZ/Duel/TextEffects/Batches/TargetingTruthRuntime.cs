using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TargetingTruth batch (v177): the ONE answer to "does this effect TARGET?" (ygopro EFFECT_FLAG_CARD_TARGET,
    /// CHAININFO_TARGET_CARDS). Every rule that depends on targeting reads it: the Archfiend die roll (TextEffectRuntime
    /// ApplyClause), Different Dimension Dragon / Heart of Clear Water (EquipAndSpellProtectionRuntime), Metalsilver Armor
    /// (EquipLocksAndLeaveFieldRuntime), Lord of D. in the target lists, Freed / Tyrant / Gora Turtle
    /// (NegationMonstersRuntime), Chaos Command Magician / Dramatic Rescue / Big Burn (ChainLinkAnswersRuntime), Riryoku
    /// Field, Curse of Royal, The Dragon's Bead, Fairy's Hand Mirror / Shift and the compiled Chain Link target list.
    /// <list type="bullet">
    /// <item><b>Clause</b> (<see cref="ClauseTargets"/>): a choice that does not target (<see cref="EffectClause.ChoiceDoesNotTarget"/>,
    /// the "lowest / highest ATK / DEF" picks — Fissure, Hammer Shot, Smashing Ground, Widespread Ruin, all Lua
    /// <c>Select</c> at resolution) never targets, nor does a pick from the Deck or the hand (those cards are never
    /// targets). Otherwise a PSCT "target" (<see cref="EffectClause.IsPsctTarget"/>) targets, and so does an old-text
    /// "select" / "choose" pick of a card on the field or in a GY (<see cref="EffectClause.RequiresTargetChoice"/>): every
    /// such Implemented card's Lua uses <c>SelectTarget</c> (audited v177; Throwstone Unit, whose Lua selects at
    /// resolution, now sets ChoiceDoesNotTarget). A NEW non-targeting pick on the field or in a GY must set
    /// ChoiceDoesNotTarget.</item>
    /// <item><b>Pick</b> (<see cref="PicksTarget"/>): the clause takes its target with a pick — the clause that rolls the
    /// Archfiend die once per effect and records a Chain Link target. An implied target ("Target the attacking monster",
    /// IsPsctTarget without a pick) targets but is not a pick.</item>
    /// <item><b>Chain Link</b> (<see cref="LinkEffectTargets"/>, <see cref="LinkTargets"/>): a recorded compiled
    /// activation lists only its targets; otherwise a registered staple that targets, or a FullyCompiled program with an
    /// activated (not continuous) clause that targets.</item>
    /// </list>
    /// </summary>
    public static class TargetingTruthRuntime
    {
        // ───────────────────────────── clause ─────────────────────────────

        /// <summary>
        /// A choice made among the cards that qualify, without targeting them (Lua <c>Select</c> + HintSelection):
        /// "the 1 face-up monster with the lowest ATK" (Fissure), the highest ATK / DEF, or a choice flagged
        /// <see cref="EffectClause.ChoiceDoesNotTarget"/> (Throwstone Unit, Confiscation's look, a declared Type).
        /// </summary>
        public static bool IsNonTargetingChoice(EffectClause c) =>
            c != null && (c.ChoiceDoesNotTarget || c.PickLowestAtk || c.PickHighestAtk || c.PickHighestDef);

        /// <summary>THE answer: applying <paramref name="c"/> targets the card it applies to.</summary>
        public static bool ClauseTargets(EffectClause c)
        {
            if (c == null || IsNonTargetingChoice(c) || NeverTargetedZone(c.Zone)) return false;
            if (c.IsPsctTarget) return true;
            return c.RequiresTargetChoice;
        }

        /// <summary>The clause takes its target with a pick (<see cref="EffectClause.RequiresTargetChoice"/>) and targets it.</summary>
        public static bool PicksTarget(EffectClause c) => c != null && c.RequiresTargetChoice && ClauseTargets(c);

        /// <summary>The clause picks a card without targeting it (a target protection does not filter its list).</summary>
        public static bool PicksWithoutTargeting(EffectClause c) => c != null && c.RequiresTargetChoice && !ClauseTargets(c);

        /// <summary>
        /// The clause is part of an activated effect (not a continuous one: "cannot be targeted", a protection's own marker)
        /// and targets.
        /// </summary>
        public static bool ActivatedClauseTargets(EffectClause c) =>
            c != null && c.Timing != EffectTiming.ContinuousWhileFaceUp && ClauseTargets(c);

        static readonly Dictionary<EffectZoneFilter, bool> s_hiddenZones = new();

        /// <summary>A pick from the Deck or the hand (any zone filter that reads one): those cards are never targets.</summary>
        public static bool NeverTargetedZone(EffectZoneFilter zone)
        {
            if (zone == EffectZoneFilter.None) return false;
            lock (s_hiddenZones)
            {
                if (s_hiddenZones.TryGetValue(zone, out var hidden)) return hidden;
                var n = zone.ToString();
                hidden = n.IndexOf("Deck", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("Hand", StringComparison.Ordinal) >= 0;
                s_hiddenZones[zone] = hidden;
                return hidden;
            }
        }

        /// <summary>
        /// The resolution re-check of a picked card: a target is stopped by "cannot be targeted" (Lord of D.) or by
        /// "unaffected"; a choice that does not target (<see cref="PicksWithoutTargeting"/>, Fissure) only by "unaffected".
        /// </summary>
        public static bool ProtectedAtResolution(DuelEngine engine, EffectClause clause, CardInstance target,
            CardInstance source) =>
            PicksWithoutTargeting(clause)
                ? target == null || ContinuousProtections.IsUnaffectedBy(engine, target, source)
                : ContinuousProtections.TargetedEffectBlocked(engine, target, source);

        // ───────────────────────────── program / card ─────────────────────────────

        /// <summary>A FullyCompiled program whose <paramref name="timing"/> effect targets (any activated effect when null).</summary>
        public static bool ProgramTargets(CompiledCardProgram prog, EffectTiming? timing = null)
        {
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = timing.HasValue ? prog.ClausesFor(timing.Value) : prog.ClauseList;
            return clauses != null && clauses.Exists(c => c != null && (timing.HasValue ? ClauseTargets(c) : ActivatedClauseTargets(c)));
        }

        /// <summary>The activation of <paramref name="card"/> (a Spell/Trap Card's Activate effect) targets.</summary>
        public static bool ActivationTargets(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog != null && prog.FullyCompiled) return ProgramTargets(prog, EffectTiming.Activate);
            return SpellTrapEffects.GetTargetKind(card.Def.id) != EffectTargetKind.None;
        }

        /// <summary>
        /// The activation of <paramref name="card"/> picks without targeting (Fissure): a target protection (Lord of D.) does
        /// not filter the legacy Spell/Trap target list.
        /// </summary>
        public static bool ActivationPicksWithoutTargeting(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var activate = prog.ClausesFor(EffectTiming.Activate);
            if (activate == null) return false;
            var pick = activate.FirstOrDefault(c => c != null && c.RequiresTargetChoice);
            return pick != null && !ClauseTargets(pick);
        }

        // ───────────────────────────── chain link ─────────────────────────────

        /// <summary>The cards <paramref name="link"/> records (its target and target list), whether or not they are targets.</summary>
        public static List<CardInstance> LinkCards(ChainLink link)
        {
            var list = new List<CardInstance>();
            if (link == null) return list;
            if (link.Target != null) list.Add(link.Target);
            foreach (var t in link.Targets)
                if (t != null && !list.Contains(t))
                    list.Add(t);
            return list;
        }

        /// <summary>
        /// The link's effect targets (ygopro EFFECT_FLAG_CARD_TARGET): a recorded compiled activation lists only its
        /// targets (CompiledActivationChainRuntime records a step as a target by <see cref="PicksTarget"/>); otherwise a
        /// registered Spell/Trap staple that targets (Monster Reborn, MST, Enemy Controller, Ring of Destruction), or a
        /// FullyCompiled program with an activated clause that targets.
        /// </summary>
        public static bool LinkEffectTargets(ChainLink link)
        {
            if (link == null) return false;
            if (link.DeferredEffect != null) return true;
            var def = link.Card?.Def;
            if (def == null) return false;
            if (!def.IsMonster && SpellTrapEffects.GetTargetKind(def.id) != EffectTargetKind.None) return true;
            return ProgramTargets(CompiledEffectCache.GetOrCompile(def));
        }

        /// <summary>The cards <paramref name="link"/>'s effect targets (CHAININFO_TARGET_CARDS); empty when it does not target.</summary>
        public static List<CardInstance> LinkTargets(ChainLink link) =>
            LinkEffectTargets(link) ? LinkCards(link) : new List<CardInstance>();

        /// <summary><paramref name="link"/>'s effect targets <paramref name="card"/>.</summary>
        public static bool LinkTargetsCard(ChainLink link, CardInstance card) =>
            card != null && LinkTargets(link).Contains(card);

        // ───────────────────────────── Big Burn ─────────────────────────────

        /// <summary>
        /// Big Burn's condition (Lua s.condition, EVENT_BECOME_TARGET): <paramref name="link"/> is an effect of
        /// <paramref name="who"/>'s opponent (rp~=tp) whose targets include a monster in either GY.
        /// </summary>
        public static bool OpponentTargetsGyMonster(DuelEngine engine, DuelistState who, ChainLink link)
        {
            if (engine?.Player == null || engine.Opponent == null || who == null || link == null) return false;
            if (link.Negated || link.Resolved || ReferenceEquals(link.Controller, who)) return false;
            return LinkTargets(link).Exists(t => t?.Def != null && t.Def.IsMonster &&
                                                 (engine.Player.Graveyard.Contains(t) ||
                                                  engine.Opponent.Graveyard.Contains(t)));
        }

        /// <summary>
        /// Big Burn's banished cards (Lua s.filter: IsMonster, IsAbleToRemove, aux.SpElimFilter over both Monster Zones and
        /// GYs): each player's GY monsters, or — under Spirit Elimination — every monster that player controls instead.
        /// A GY monster a banish lock keeps there (Necrovalley, Kycoo against <paramref name="who"/>) is left out.
        /// </summary>
        public static List<CardInstance> AllGyMonstersToBanish(DuelEngine engine, DuelistState who)
        {
            var list = new List<CardInstance>();
            if (engine?.Player == null || engine.Opponent == null || who == null) return list;
            foreach (var owner in new[] { engine.Player, engine.Opponent })
            foreach (var g in owner.Graveyard)
                if (g?.Def != null && g.Def.IsMonster && !list.Contains(g))
                    list.Add(g);
            SpiritEliminationRuntime.SubstituteInList(engine, list, null, false, null, false, engine.Player,
                engine.Opponent);
            list.RemoveAll(c =>
            {
                var gyOwner = engine.Player.Graveyard.Contains(c) ? engine.Player
                    : engine.Opponent.Graveyard.Contains(c) ? engine.Opponent : null;
                return gyOwner != null && (ContinuousProtections.CannotBanishFromGraveyard(engine, gyOwner) ||
                                           RuleLocksRuntime.BanishFromGyLocked(engine, who));
            });
            return list;
        }

        /// <summary>Big Burn's resolution: banish every card of <see cref="AllGyMonstersToBanish"/>.</summary>
        public static void BanishAllGyMonsters(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var all = AllGyMonstersToBanish(engine, who);
            engine.Log($"{source?.Name ?? "Big Burn"}: every monster in both GYs is banished ({all.Count}).");
            // Lua Duel.Remove(g): one EVENT_REMOVE for the whole group (Soul Absorption triggers once, ChaosBanishSummons).
            using var banishGroup = BanishEvents.Group(engine);
            foreach (var c in all)
            {
                var owner = engine.Player.Graveyard.Contains(c) ? engine.Player
                    : engine.Opponent.Graveyard.Contains(c) ? engine.Opponent
                    : engine.ControllerOf(c);
                if (owner != null) engine.BanishCard(owner, c, by: who);
            }
        }
    }
}
