using System;
using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ConditionalSpellsAndTraps" runtime. Reads compiled clauses only — no card-id branches.
    /// <list type="bullet">
    /// <item>Activation conditions (<see cref="TryActivationCondition"/>, TextEffectRuntime.ActivationConditionsMet default
    /// chain): <see cref="YourGyAtLeast"/> (Blasting the Ruins), <see cref="YourBanishedAtLeastAndMonsterOnField"/> (Chaos
    /// End), <see cref="YourBanishedAtLeastNoGyCanDraw"/> (Chaos Greed), <see cref="NamedFaceUpAndOpponentControlsACard"/>
    /// (Ojama Delta Hurricane!!), <see cref="YouControlAnotherSpellTrap"/> (Gryphon's Feather Duster). Self-Destruct Button
    /// reuses <c>DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent</c>.</item>
    /// <item><b>Self-Destruct Button</b>: <see cref="ApplyBothLifePointsBecomeZero"/> (Lua SetLP, not damage) — the Duel
    /// ends at once as a DRAW.</item>
    /// <item><b>Destruction Ring</b>: <see cref="ApplyDestroyTargetThenDamageBoth"/> — the target, still face-up and
    /// related, is destroyed; only then both players take the damage at once (ApplyEffectDamageBoth).</item>
    /// <item><b>D.D. Borderline</b>: <see cref="BattlePhaseLocked"/>, read by TributeSummonsRuntime.BattlePhaseForbidden
    /// (DuelEngine.CanConductBattlePhase / TryEnterBattlePhase, which SimpleAi checks before entering).</item>
    /// </list>
    /// Chaos End, Ojama Delta Hurricane!! and Gryphon's Feather Duster resolve through TextEffectRuntime's mass Destroy arm
    /// (one event, FlipMassEffectsRuntime.Simultaneously); Gryphon's gain reads the destroyed count (CountSource
    /// PreviousClauseHits in the GainLifePoints arm).
    /// </summary>
    public static class ConditionalSpellsAndTrapsRuntime
    {
        /// <summary>Blasting the Ruins: GetFieldGroupCount(tp, LOCATION_GRAVE, 0) &gt;= ConditionAmount.</summary>
        public const string YourGyAtLeast = "ConditionalSpellsAndTraps.YourGyAtLeast";

        /// <summary>
        /// Chaos End: GetFieldGroupCount(tp, LOCATION_REMOVED, 0) &gt;= ConditionAmount (face-down banished cards count),
        /// and a monster in either Monster Zone (Lua s.target).
        /// </summary>
        public const string YourBanishedAtLeastAndMonsterOnField =
            "ConditionalSpellsAndTraps.YourBanishedAtLeastAndMonsterOnField";

        /// <summary>
        /// Chaos Greed: REMOVED &gt;= ConditionAmount and no card in your GY; Duel.IsPlayerCanDraw(tp, Amount).
        /// </summary>
        public const string YourBanishedAtLeastNoGyCanDraw = "ConditionalSpellsAndTraps.YourBanishedAtLeastNoGyCanDraw";

        /// <summary>
        /// Ojama Delta Hurricane!!: every name of ConditionName ('|'-separated) face-up on your field, and the opponent
        /// controls a card (Lua s.target).
        /// </summary>
        public const string NamedFaceUpAndOpponentControlsACard =
            "ConditionalSpellsAndTraps.NamedFaceUpAndOpponentControlsACard";

        /// <summary>Gryphon's Feather Duster: a Spell/Trap you control other than this card (Lua s.target).</summary>
        public const string YouControlAnotherSpellTrap = "ConditionalSpellsAndTraps.YouControlAnotherSpellTrap";

        /// <summary>CorpusTriggerStressTests.NeedsHardBoard: this batch's regression tests drive these boards.</summary>
        public static bool NeedsHardBoard(EffectClause c) =>
            c != null &&
            (c.ActivationCondition == YourGyAtLeast ||
             c.ActivationCondition == YourBanishedAtLeastAndMonsterOnField ||
             c.ActivationCondition == YourBanishedAtLeastNoGyCanDraw ||
             c.ActivationCondition == NamedFaceUpAndOpponentControlsACard ||
             c.ActivationCondition == YouControlAnotherSpellTrap);

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False when the condition is not this batch's.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case YourGyAtLeast:
                    ok = Count(who.Graveyard) >= c.ConditionAmount;
                    reason = $"Needs {c.ConditionAmount} or more cards in your GY.";
                    return true;
                case YourBanishedAtLeastAndMonsterOnField:
                    ok = Count(who.Banished) >= c.ConditionAmount &&
                         who.MonsterCount + (opp?.MonsterCount ?? 0) > 0;
                    reason = $"Needs {c.ConditionAmount} or more of your cards banished and a monster on the field.";
                    return true;
                case YourBanishedAtLeastNoGyCanDraw:
                    ok = Count(who.Banished) >= c.ConditionAmount && Count(who.Graveyard) == 0 &&
                         DrawThenDiscardRuntime.CanDraw(engine, who, Math.Max(1, c.Amount));
                    reason = $"Needs {c.ConditionAmount} or more of your cards banished, no card in your GY, and " +
                             $"{Math.Max(1, c.Amount)} cards to draw.";
                    return true;
                case NamedFaceUpAndOpponentControlsACard:
                {
                    var names = NamesOf(c.ConditionName);
                    ok = opp != null && names.Count > 0 &&
                         names.All(n => FieldSpellEffects.ControlsFaceUpNamed(who, n)) &&
                         CardsOnField(opp).Any();
                    reason = "Only while " + string.Join(", ", names.Select(n => "\"" + n + "\"")) +
                             " are face-up on your field and your opponent controls a card.";
                    return true;
                }
                case YouControlAnotherSpellTrap:
                    ok = who.SpellTrapsOnField().Any(st => st != null && !ReferenceEquals(st, card));
                    reason = "You control no other Spell or Trap Card.";
                    return true;
                default:
                    return false;
            }
        }

        static int Count(List<CardInstance> pile) => pile?.Count(x => x != null) ?? 0;

        static List<string> NamesOf(string list) =>
            (list ?? string.Empty).Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>Every card <paramref name="side"/> controls (ygopro LOCATION_ONFIELD).</summary>
        static IEnumerable<CardInstance> CardsOnField(DuelistState side)
        {
            if (side == null) yield break;
            foreach (var m in side.MonstersOnField())
                if (m != null) yield return m;
            foreach (var st in side.SpellTrapsOnField())
                if (st != null) yield return st;
            if (side.PendulumZones != null)
                foreach (var z in side.PendulumZones)
                    if (z?.Occupant != null) yield return z.Occupant;
        }

        // ───────────────────────────── Self-Destruct Button ─────────────────────────────

        /// <summary>
        /// Both players' LP become 0 (Lua Duel.SetLP — an LP change, not damage or a cost): no damage event, no LP-cost
        /// record. Both at 0 at once is a DRAW, declared at once.
        /// </summary>
        public static void ApplyBothLifePointsBecomeZero(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            who.LifePoints = 0;
            if (opp != null) opp.LifePoints = 0;
            var name = source?.Name ?? "Effect";
            engine.Log($"{name}: both players' Life Points become 0.");
            engine.EndGameByCardEffect(null, $"{name}: both players' Life Points became 0 — the Duel is a DRAW");
        }

        // ───────────────────────────── Destruction Ring ─────────────────────────────

        /// <summary>
        /// Lua s.activate: the target must still be face-up (and related: the chain step drops a target that left or was
        /// turned face-down); when Duel.Destroy destroys it, the opponent and the activating player each take
        /// <see cref="EffectClause.Amount"/> damage at once (Damage(…, true) x2 + RDComplete).
        /// </summary>
        public static void ApplyDestroyTargetThenDamageBoth(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, CardInstance target, Action<CardInstance> destroy)
        {
            if (engine == null || who == null || c == null || target == null || destroy == null) return;
            var name = source?.Name ?? "Effect";
            if (engine.ControllerOf(target) == null || !target.FaceUp)
            {
                engine.Log($"{name}: {target.Name} is no longer a face-up monster — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            destroy(target);
            if (engine.ControllerOf(target) != null)
            {
                engine.Log($"{name}: {target.Name} was not destroyed — no damage.");
                return;
            }

            var amount = Math.Max(0, c.Amount);
            engine.ApplyEffectDamageBoth(engine.OpponentOf(who), amount, who, amount, source?.Name, source);
        }

        // ───────────────────────────── D.D. Borderline ─────────────────────────────

        /// <summary>
        /// EFFECT_CANNOT_BP (target range 1,1): <paramref name="who"/> cannot conduct a Battle Phase while a face-up,
        /// un-negated NoBattlePhaseWhileNoSpellInControllerGy card is on either field and its controller's GY holds no
        /// Spell Card.
        /// </summary>
        public static bool BattlePhaseLocked(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    if (!HasLockClause(st)) continue;
                    if (side.Graveyard == null || !side.Graveyard.Any(g => g?.Def != null && g.Def.IsSpell))
                        return true;
                }
            }

            return false;
        }

        static bool HasLockClause(CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.NoBattlePhaseWhileNoSpellInControllerGy)
                    return true;
            return false;
        }
    }
}
