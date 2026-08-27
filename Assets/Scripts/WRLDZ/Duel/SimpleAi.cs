using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Opponent AI — stepped coroutine so the board/UI can refresh each phase.
    /// Phase order: (Draw/Standby/MP1 already started by BeginTurn) → MP1 acts → Battle → MP2 → End.
    /// </summary>
    public static class SimpleAi
    {
        /// <summary>Base gap between AI actions (plus any <see cref="DuelPresentationPacer"/> hold).</summary>
        public static float StepDelay = DuelPresentationPacer.DefaultAiStep;

        /// <summary>Legacy one-shot (no delay). Prefer TakeTurnRoutine.</summary>
        public static void TakeTurn(DuelEngine engine)
        {
            var runner = RunTurn(engine, null);
            while (runner.MoveNext()) { }
        }

        public static IEnumerator TakeTurnRoutine(DuelEngine engine, Action onStep)
        {
            return RunTurn(engine, onStep);
        }

        static IEnumerator RunTurn(DuelEngine engine, Action onStep)
        {
            var ai = engine.Opponent;
            if (engine.GameOver || engine.TurnPlayer != ai)
            {
                engine.Log("[AI] TakeTurn aborted — not opponent's turn or game over.");
                yield break;
            }

            AiHeuristicPolicy.EnsureLoaded();
            // Phase banner already logged by BeginTurn — no ">>> takes their turn" / policy spam.
            engine.LogReview(DuelLogKind.Ai, "AI",
                $"Turn start — LP you={engine.Player.LifePoints} ai={ai.LifePoints} " +
                $"hand={ai.HandCount} monsters={ai.MonsterCount} NS_used={ai.NormalSummonUsed}");
            onStep?.Invoke();
            yield return Wait();

            if (engine.Phase != DuelPhase.Main1)
            {
                engine.Log($"[AI] Expected Main Phase 1, got {engine.Phase}. Forcing End Turn.");
                engine.EndTurn(ai);
                onStep?.Invoke();
                yield break;
            }

            // ── Main Phase 1 (engine already logged phase) ──
            onStep?.Invoke();
            yield return Wait();
            while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                yield return null;
            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            foreach (var _ in RunMainPhaseActions(engine, ai, "MP1"))
            {
                onStep?.Invoke();
                yield return Wait();
                // Summon response window (Trap Hole) for the player
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                if (engine.GameOver || engine.TurnPlayer != ai) yield break;
            }

            // ── Battle ──
            if (engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase)
            {
                var canAttackSomeone = ai.MonstersOnField()
                    .Any(m => m.FaceUp && m.Position == BattlePosition.Attack);

                if (canAttackSomeone)
                {
                    engine.TryEnterBattlePhase(ai);
                    onStep?.Invoke();
                    yield return Wait();
                    while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                        yield return null;
                    if (engine.GameOver || engine.TurnPlayer != ai) yield break;

                    foreach (var _ in RunBattlePhase(engine, ai))
                    {
                        onStep?.Invoke();
                        yield return Wait();
                        if (engine.GameOver || engine.Phase != DuelPhase.Battle) break;
                    }
                }
                // No log when skipping Battle — Main Phase 2 line covers it.
            }

            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            // ── Main Phase 2 ──
            if (engine.Phase == DuelPhase.Battle || engine.Phase == DuelPhase.Main1)
            {
                engine.TryEnterMainPhase2(ai);
                onStep?.Invoke();
                yield return Wait();

                foreach (var _ in RunMainPhaseActions(engine, ai, "MP2"))
                {
                    onStep?.Invoke();
                    yield return Wait();
                    if (engine.GameOver || engine.TurnPlayer != ai) yield break;
                }
            }

            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            // ── End ──
            onStep?.Invoke();
            yield return Wait();
            engine.EndTurn(ai);
            onStep?.Invoke();
        }

        static IEnumerator Wait()
        {
            // Let flip animations + read holds finish so the player can follow the board
            while (DuelPresentationPacer.IsHolding)
                yield return null;
            if (StepDelay > 0f)
                yield return new WaitForSecondsRealtime(StepDelay);
        }

        /// <summary>Yields once per successful action.</summary>
        static IEnumerable<object> RunMainPhaseActions(DuelEngine engine, DuelistState ai, string phaseLabel)
        {
            const int maxSteps = 10;
            for (var step = 0; step < maxSteps; step++)
            {
                if (engine.GameOver || engine.TurnPlayer != ai || !engine.InMainPhase)
                    yield break;

                // Advantage spells first (policy table)
                if (AiHeuristicPolicy.PreferPotOfGreed &&
                    TryActivateById(engine, ai, SpellTrapEffects.PotOfGreed, phaseLabel))
                {
                    yield return null;
                    continue;
                }

                // Raigeki: only when player has a real board threat (face-up monster or 2+ monsters).
                // Wiping a lone SET with Raigeki is legal but makes the demo feel broken.
                var playerFaceUpMonsters = engine.Player.MonstersOnField().Count(m => m.FaceUp);
                if ((playerFaceUpMonsters >= AiHeuristicPolicy.RaigekiMinFaceUpThreat ||
                     engine.Player.MonsterCount >= AiHeuristicPolicy.RaigekiMinMonsterCount) &&
                    TryActivateById(engine, ai, SpellTrapEffects.Raigeki, phaseLabel))
                {
                    yield return null;
                    continue;
                }

                if (engine.Player.MonsterCount > ai.MonsterCount && playerFaceUpMonsters > 0 &&
                    TryActivateById(engine, ai, SpellTrapEffects.DarkHole, phaseLabel))
                {
                    yield return null;
                    continue;
                }

                // Swords when behind (policy)
                if (AiHeuristicPolicy.UseSwordsWhenBehind &&
                    engine.Player.MonsterCount > ai.MonsterCount &&
                    TryActivateById(engine, ai, SpellTrapEffects.SwordsOfRevealingLight, phaseLabel))
                {
                    yield return null;
                    continue;
                }

                // Field Spells first so WATER Level 5 can Normal Summon without Tribute the same turn.
                var fieldSpell = ai.Hand.FirstOrDefault(c =>
                    c?.Def != null && c.Def.IsFieldSpell &&
                    engine.CanActivateSpellTrap(ai, c, true));
                if (fieldSpell != null && engine.TryActivateSpellTrap(ai, fieldSpell, true))
                {
                    yield return null;
                    continue;
                }

                // Ignition monster effects (Abyss Soldier bounce, …)
                foreach (var m in ai.MonstersOnField().ToList())
                {
                    if (m == null || !engine.CanActivateSpellTrap(ai, m, fromHand: false)) continue;
                    if (!engine.TryActivateSpellTrap(ai, m, fromHand: false)) continue;
                    while (engine.IsAwaitingEffectTarget && engine.PendingActivation != null)
                    {
                        var t = engine.PendingActivation.LegalTargets
                            .OrderByDescending(x => x.CurrentAtk).FirstOrDefault();
                        if (t == null || !engine.TrySelectEffectTarget(t))
                            engine.CancelEffectTargeting();
                    }

                    yield return null;
                    continue;
                }

                // Flip Summon face-down Flip monsters when useful (Man-Eater Bug, etc.)
                foreach (var m in ai.MonstersOnField().ToList())
                {
                    if (!engine.CanFlipSummon(ai, m)) continue;
                    if (!IsFlipEffectMonster(m) && m.CurrentAtk < 1400) continue;
                    if (AiHeuristicPolicy.PreferFlipBugs && IsFlipEffectMonster(m) ||
                        !IsFlipEffectMonster(m))
                    {
                        if (engine.TryFlipSummon(ai, m))
                        {
                            // Resolve pending monster targets (AI auto-picks inside MonsterEffects)
                            while (engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                                   engine.PendingActivation.IsMonsterEffect)
                            {
                                var t = engine.PendingActivation.LegalTargets
                                    .OrderByDescending(x => x.CurrentAtk).FirstOrDefault();
                                if (t == null || !engine.TrySelectEffectTarget(t))
                                    engine.CancelEffectTargeting();
                                break;
                            }

                            engine.TryCompleteDeferredBattleDestruction();
                            yield return null;
                            continue;
                        }
                    }
                }

                // Normal Summon / Set best legal monster
                if (!ai.NormalSummonUsed)
                {
                    var summonable = ai.Hand
                        .Where(c => engine.CanNormalSummonOrSet(ai, c))
                        .ToList();

                    // Prefer Level ≤4 non-Flip for face-up Attack; Set Flip Effect monsters
                    var freeAtk = summonable
                        .Where(c => c.Level <= 4 && !IsFlipEffectMonster(c))
                        .OrderByDescending(c => c.CurrentAtk)
                        .ToList();
                    var freeFlip = summonable
                        .Where(c => c.Level <= 4 && IsFlipEffectMonster(c))
                        .OrderByDescending(c => c.CurrentDef)
                        .ToList();
                    var tribute = summonable
                        .Where(c => c.Level >= 5)
                        .OrderByDescending(c => c.CurrentAtk)
                        .ToList();

                    CardInstance pick = null;
                    var asSet = false;
                    if (freeAtk.Count > 0)
                    {
                        pick = freeAtk[0];
                        asSet = false;
                    }
                    else if (freeFlip.Count > 0 && AiHeuristicPolicy.PreferSetFlipMonsters)
                    {
                        // Official: Flip monsters are usually Set face-down Defense
                        pick = freeFlip[0];
                        asSet = true;
                    }
                    else if (freeFlip.Count > 0)
                    {
                        pick = freeFlip[0];
                        asSet = true;
                    }
                    else if (tribute.Count > 0)
                    {
                        pick = tribute[0];
                        asSet = false;
                    }

                    if (pick != null)
                    {
                        engine.ClearTributes();
                        var ok = engine.TryNormalSummon(ai, pick, asSet);
                        // Engine logs the summon/set itself.
                        if (ok)
                        {
                            engine.LogReview(DuelLogKind.Ai, "AI",
                                $"{phaseLabel}: {(asSet ? "Set" : "NS")} {pick.Name} " +
                                $"(ATK {pick.CurrentAtk} DEF {pick.CurrentDef} Lv{pick.Level})");
                            yield return null;
                            continue;
                        }
                    }
                }

                // Set a Trap (max 2 on field) — policy prefers Mirror Force / Trap Hole / Waboku
                if (AiHeuristicPolicy.PreferSetTrapsBeforeEnd && ai.SpellTrapsOnField().Count() < 2)
                {
                    var trap = ai.Hand
                        .Where(c => c.Def != null && c.Def.IsTrap && engine.CanSetSpellTrap(ai, c))
                        .OrderByDescending(c =>
                            c.CardId == SpellTrapEffects.MirrorForce ? 3 :
                            c.CardId == SpellTrapEffects.TrapHole ? 2 :
                            c.CardId == SpellTrapEffects.Waboku ? 1 : 0)
                        .FirstOrDefault();
                    if (trap != null)
                    {
                        engine.TrySetSpellTrap(ai, trap);
                        // Engine logs Set Spell/Trap.
                        yield return null;
                        continue;
                    }
                }

                // Nothing else useful — silent end of Main Phase actions
                yield break;
            }
        }

        static bool TryActivateById(DuelEngine engine, DuelistState ai, int id, string phaseLabel)
        {
            var card = ai.Hand.FirstOrDefault(c =>
                c.CardId == id && engine.CanActivateSpellTrap(ai, c, true));
            if (card == null) return false;
            // Engine logs activation.
            return engine.TryActivateSpellTrap(ai, card, true);
        }

        static bool IsFlipEffectMonster(CardInstance c) =>
            c?.Def?.type != null &&
            c.Def.type.IndexOf("Flip", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Yields once per attack declaration.</summary>
        static IEnumerable<object> RunBattlePhase(DuelEngine engine, DuelistState ai)
        {
            // Refresh attacker list each step (board changes)
            while (!engine.GameOver && engine.Phase == DuelPhase.Battle && engine.TurnPlayer == ai)
            {
                var attacker = ai.MonstersOnField()
                    .Where(m => engine.CanAttack(ai, m))
                    .OrderByDescending(m => m.CurrentAtk)
                    .FirstOrDefault();

                if (attacker == null)
                    yield break;

                var target = ChooseAttackTarget(engine, ai, attacker);

                if (target == null &&
                    (engine.Player.MonsterCount == 0 || engine.CanAttackDirectly(ai, attacker)))
                {
                    // Engine logs the attack declaration.
                    engine.TryAttack(ai, attacker, null);
                    while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                        yield return null;
                    yield return null;
                    continue;
                }

                if (target == null)
                {
                    // Prefer skip further attacks if no beneficial target (no log spam).
                    yield break;
                }

                engine.LogReview(DuelLogKind.Ai, "AI",
                    $"Battle: {attacker.Name}({attacker.CurrentAtk}) → " +
                    (target.FaceUp
                        ? $"{target.Name}({target.CurrentAtk}/{target.CurrentDef})"
                        : "face-down"));
                // Engine logs the attack declaration.
                engine.TryAttack(ai, attacker, target);
                // Player may activate Mirror Force / Negate Attack / Waboku — do not continue until resolved
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                yield return null;
            }
        }

        /// <summary>
        /// Legal target selection using official battle math (never treats face-down as 0 DEF).
        /// Face-down monsters use printed DEF for evaluation (revealed at damage calculation).
        /// </summary>
        static CardInstance ChooseAttackTarget(DuelEngine engine, DuelistState ai, CardInstance attacker)
        {
            var playerMonsters = engine.Player.MonstersOnField().ToList();
            if (playerMonsters.Count == 0) return null;

            bool IsDefense(CardInstance m) =>
                !m.FaceUp || m.Position == BattlePosition.Defense;

            var atk = attacker.CurrentAtk;

            // 1) Destroy weakest face-up ATK we can beat (ATK > their ATK)
            foreach (var t in playerMonsters
                         .Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                         .OrderBy(m => m.CurrentAtk))
            {
                if (atk > t.CurrentAtk)
                    return t;
            }

            // 2) Destroy face-down / DEF only if ATK > DEF (official — otherwise we take damage & they live)
            foreach (var t in playerMonsters
                         .Where(IsDefense)
                         .OrderBy(m => m.CurrentDef))
            {
                if (atk > t.CurrentDef)
                    return t;
            }

            // 3) Equal ATK trade only with board advantage (policy)
            foreach (var t in playerMonsters
                         .Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                         .OrderBy(m => m.CurrentAtk))
            {
                if (atk == t.CurrentAtk &&
                    ai.MonsterCount >= AiHeuristicPolicy.EqualTradeMinOwnMonsters)
                    return t;
            }

            // 4) No beneficial attack — return null (do not suicide into higher DEF for "demo")
            return null;
        }
    }
}
