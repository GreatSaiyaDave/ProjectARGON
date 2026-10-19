using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TlmBattleShieldsAndBurnZero batch (atoms: <see cref="TlmBattleShieldsAndBurnZeroAtoms"/>). Reads compiled programs only,
    /// no card-id branches:
    /// <list type="bullet">
    /// <item><b>Face-down Defense shield</b> (Hieracosphinx, <see cref="FaceDownDefenseUnselectable"/>, read by
    /// ContinuousProtections.CannotBeAttackTarget): while a face-up, unnegated source is on its controller's field, that
    /// player's face-down Defense Position monsters cannot be selected as attack targets. They still count as monsters, so
    /// the opponent cannot attack directly because of them.</item>
    /// <item><b>"While you control a face-down Defense Position monster"</b> (Grave Ohja's own shield,
    /// <see cref="YouControlFaceDownDefense"/>; evaluated by AttackShieldsAndDamageZeroRuntime.WhileConditionMet).</item>
    /// <item><b>Flip Summon burn</b> (Grave Ohja, <see cref="OnFlipSummoned"/> from DuelEngine.CompleteFlipSummon): each
    /// Flip Summon of another monster by the source's controller queues one mandatory trigger on the simultaneous-trigger
    /// Chain (a real Chain Link when the opponent may respond). Once activated, it resolves even if the source left the field
    /// (the script has no relate check); a source still on the field but negated by then does nothing.
    /// EffectDamageEventsRuntime knows the link would damage the opponent (Barrel Behind the Door can answer it).</item>
    /// <item><b>Effect damage becomes 0</b> (Des Wombat, <see cref="EffectDamageBecomesZero"/>): DuelEngine.ApplyEffectDamage
    /// and its simultaneous twin, after the reflect modifiers, before the LP change.</item>
    /// <item><b>Destroyed while Set</b> (Kozaky's Self-Destruct Button, <see cref="BurnDestroyer"/>): the "sent from the field
    /// to the GY" trigger gated by RequiresDestroyedWhileFaceDown (CallsDiceAndSetTriggersRuntime); the damage goes to the
    /// player recorded as the destroyer (<see cref="CallsDiceAndSetTriggersRuntime.DestroyedFaceDownBy"/>).</item>
    /// </list>
    /// </summary>
    public static class TlmBattleShieldsAndBurnZeroRuntime
    {
        /// <summary>WhileCondition: the source's controller has a face-down Defense Position monster (Grave Ohja).</summary>
        public const string YouControlFaceDownDefense = "YouControlFaceDownDefense";

        static bool IsFaceDownDefense(CardInstance m) =>
            m != null && !m.FaceUp && m.Position == BattlePosition.Defense;

        /// <summary><paramref name="who"/> has a face-down Defense Position monster in a Monster Zone (Lua POS_FACEDOWN_DEFENSE).</summary>
        public static bool ControlsFaceDownDefense(DuelistState who) =>
            who != null && who.MonstersOnField().Any(IsFaceDownDefense);

        /// <summary>The ContinuousWhileFaceUp clauses of <paramref name="action"/> of a face-up, unnegated, settled card.</summary>
        static IEnumerable<EffectClause> Live(DuelEngine engine, CardInstance card, EffectActionKind action)
        {
            if (engine == null || card?.Def == null || !card.FaceUp || card.IsNegated || engine.IsSummonPending(card))
                yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == action)
                    yield return c;
        }

        // ───────────────────────────── attack targets ─────────────────────────────

        /// <summary>
        /// ContinuousProtections.CannotBeAttackTarget: <paramref name="monster"/> is a face-down Defense Position monster
        /// whose controller controls a live
        /// <see cref="EffectActionKind.OpponentCannotSelectYourFaceDownDefenseMonstersAsAttackTarget"/> source (Hieracosphinx).
        /// </summary>
        public static bool FaceDownDefenseUnselectable(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || !IsFaceDownDefense(monster)) return false;
            var ctrl = engine.ControllerOf(monster);
            if (ctrl == null) return false;
            return ctrl.MonstersOnField().Any(src =>
                Live(engine, src, EffectActionKind.OpponentCannotSelectYourFaceDownDefenseMonstersAsAttackTarget).Any());
        }

        // ───────────────────────────── effect damage ─────────────────────────────

        /// <summary>
        /// DuelEngine.ApplyEffectDamage: <paramref name="target"/> controls a live
        /// <see cref="EffectActionKind.EffectDamageToControllerBecomesZero"/> monster (Des Wombat), so effect damage to them
        /// becomes 0. <paramref name="source"/> is that monster.
        /// </summary>
        public static bool EffectDamageBecomesZero(DuelEngine engine, DuelistState target, out CardInstance source)
        {
            source = null;
            if (engine == null || target == null) return false;
            foreach (var m in target.MonstersOnField())
            {
                if (!Live(engine, m, EffectActionKind.EffectDamageToControllerBecomesZero).Any()) continue;
                source = m;
                return true;
            }

            return false;
        }

        // ───────────────────────────── Flip Summon burn ─────────────────────────────

        /// <summary>
        /// DuelEngine.CompleteFlipSummon: <paramref name="who"/> Flip Summoned <paramref name="flipped"/>. Each live
        /// <see cref="EffectActionKind.InflictDamageToOpponentEachTimeYourMonsterFlipSummoned"/> monster <paramref name="who"/>
        /// controls, other than <paramref name="flipped"/>, queues one mandatory trigger.
        /// </summary>
        public static void OnFlipSummoned(DuelEngine engine, DuelistState who, CardInstance flipped)
        {
            if (engine == null || who == null || flipped == null || engine.GameOver) return;
            var victim = engine.OpponentOf(who);
            if (victim == null) return;
            foreach (var src in who.MonstersOnField().ToList())
            {
                if (src == null || ReferenceEquals(src, flipped)) continue;
                foreach (var c in Live(engine, src, EffectActionKind.InflictDamageToOpponentEachTimeYourMonsterFlipSummoned)
                             .ToList())
                {
                    var card = src;
                    var amount = c.Amount > 0 ? c.Amount : 300;
                    var stamp = card.FieldEntryStamp;
                    var flippedName = flipped.Name;
                    SimultaneousTriggersRuntime.QueueTrigger(engine, who, card, optional: false, () =>
                    {
                        if (engine.GameOver) return;
                        // Activated: it resolves even if the card left the field; negated on the field, it does nothing.
                        var stillThere = who.TryFindMonster(card, out _) && card.FieldEntryStamp == stamp;
                        if (stillThere && card.FaceUp && card.IsNegated)
                        {
                            engine.Log($"{card.Name} is negated — its effect does not resolve.");
                            return;
                        }

                        engine.Log($"{card.Name}: {flippedName} was Flip Summoned — {amount} damage to {victim.Name}.");
                        engine.ApplyEffectDamage(victim, amount, card.Name, card);
                    });
                }
            }
        }

        /// <summary>
        /// EffectDamageEventsRuntime.ActivationClauses: the clauses a trigger Chain Link of this card resolves that the
        /// would-damage knowledge reads (Grave Ohja's Flip Summon burn).
        /// </summary>
        public static IEnumerable<EffectClause> TriggerLinkDamageClauses(CompiledCardProgram prog)
        {
            if (prog == null) yield break;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == EffectActionKind.InflictDamageToOpponentEachTimeYourMonsterFlipSummoned)
                    yield return c;
        }

        // ───────────────────────────── destroyed while Set ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.InflictDamageToPlayerWhoDestroyedThisSetCard"/> (Kozaky's Self-Destruct Button): the
        /// player whose card destroyed <paramref name="source"/> while it was Set takes the damage (its controller too).
        /// </summary>
        public static void BurnDestroyer(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || source == null || clause == null || engine.GameOver) return;
            var destroyer = CallsDiceAndSetTriggersRuntime.DestroyedFaceDownBy(engine, source);
            if (destroyer == null)
            {
                engine.Log($"{source.Name}: the player who destroyed it is unknown — no damage.");
                return;
            }

            var amount = clause.Amount > 0 ? clause.Amount : 1000;
            engine.Log($"{source.Name}: {amount} damage to {destroyer.Name}, who destroyed it.");
            engine.ApplyEffectDamage(destroyer, amount, source.Name, source);
        }
    }
}
