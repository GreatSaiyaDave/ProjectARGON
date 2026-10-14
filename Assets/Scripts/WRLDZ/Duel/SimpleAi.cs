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
            var effDiff = (WRLDZ.Duel.AiDifficulty)Math.Max((int)engine.AiDifficulty,
                                                           (int)WRLDZ.Duel.AiDifficultyPrefs.Current);
            AiHeuristicPolicy.ApplyDifficulty(effDiff);

            if (engine.TurnNumber <= 1) WRLDZ.Duel.AiArchetypeDetector.Reset();
            foreach (var m in engine.Player.MonstersOnField())
                if (m != null && m.FaceUp) WRLDZ.Duel.AiArchetypeDetector.RevealCard(m.CardId);
            foreach (var m in engine.Opponent.MonstersOnField())
                if (m != null) WRLDZ.Duel.AiArchetypeDetector.RevealCard(m.CardId);
            foreach (var s in engine.Player.SpellTrapsOnField())
                if (s != null && s.FaceUp) WRLDZ.Duel.AiArchetypeDetector.RevealCard(s.CardId);
            // Phase banner already logged by BeginTurn — no ">>> takes their turn" / policy spam.
            engine.LogReview(DuelLogKind.Ai, "AI",
                $"Turn start — LP you={engine.Player.LifePoints} ai={ai.LifePoints} " +
                $"hand={ai.HandCount} monsters={ai.MonsterCount} NS_used={ai.NormalSummonUsed}");
            onStep?.Invoke();
            yield return Wait();
            // TriggerChainResponses batch (v163): a trigger Chain of this turn's Draw / Standby / End Phase (Freed's search,
            // Life Absorbing Machine, Spirit's Invitation) may wait on the player's Chain response; the turn goes on after it.
            while (engine.IsAwaitingPlayerResponse && engine.TurnPlayer == ai &&
                   (engine.Phase == DuelPhase.Draw || engine.Phase == DuelPhase.Standby || engine.Phase == DuelPhase.End))
                yield return null;
            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            // Timeater (BattleOutcomeTriggers batch, v162): Main Phase 1 was skipped — the turn starts in its Battle Phase.
            var mainPhase1Skipped = engine.Phase == DuelPhase.Battle;
            if (engine.Phase != DuelPhase.Main1 && !mainPhase1Skipped)
            {
                engine.Log($"[AI] Expected Main Phase 1, got {engine.Phase}. Forcing End Turn.");
                engine.EndTurn(ai);
                onStep?.Invoke();
                yield break;
            }

            // ── Main Phase 1 (engine already logged phase) ──
            if (!mainPhase1Skipped)
            {
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
            }
            else
            {
                // The skip entered the Battle Phase, which opened the player's Battle-Phase-start response window:
                // it is answered before the AI looks at its attackers or leaves the Battle Phase (v162 review fix).
                onStep?.Invoke();
                yield return Wait();
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                if (engine.GameOver || engine.TurnPlayer != ai) yield break;
            }

            // ── Battle ──
            if (mainPhase1Skipped && engine.Phase == DuelPhase.Battle ||
                !mainPhase1Skipped && engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase)
            {
                var canAttackSomeone = ai.MonstersOnField()
                    .Any(m => m.FaceUp && m.Position == BattlePosition.Attack);

                if (canAttackSomeone)
                {
                    if (!mainPhase1Skipped)
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
                foreach (var _ in EnterMainPhase2(engine, ai))
                    yield return _;
                if (engine.GameOver || engine.TurnPlayer != ai) yield break;

                if (engine.Phase == DuelPhase.Main2)
                {
                    onStep?.Invoke();
                    yield return Wait();

                    foreach (var _ in RunMainPhaseActions(engine, ai, "MP2"))
                    {
                        // The action's yield can land while the player's chain or Summon response is open.
                        // That window is answered before the next visible step.
                        while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                            yield return null;
                        onStep?.Invoke();
                        yield return Wait();
                        if (engine.GameOver || engine.TurnPlayer != ai) yield break;
                    }
                }
            }

            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            // ── Second Battle Phase (Weather Report: "perform your Battle Phase twice"), then Main Phase 2 ──
            if (ai.BattlePhaseTwiceTurns.Contains(engine.TurnNumber))
            {
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                if (engine.GameOver || engine.TurnPlayer != ai) yield break;
            }

            if (engine.Phase == DuelPhase.Main2 && engine.CanConductBattlePhase &&
                ai.MonstersOnField().Any(m => m.FaceUp && m.Position == BattlePosition.Attack))
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

                if (engine.GameOver || engine.TurnPlayer != ai) yield break;
                if (engine.Phase == DuelPhase.Battle)
                {
                    foreach (var _ in EnterMainPhase2(engine, ai))
                        yield return _;
                    if (engine.GameOver || engine.TurnPlayer != ai) yield break;
                    if (engine.Phase == DuelPhase.Main2)
                    {
                        onStep?.Invoke();
                        yield return Wait();
                    }
                }
            }

            if (engine.GameOver || engine.TurnPlayer != ai) yield break;

            // ── End ──
            onStep?.Invoke();
            yield return Wait();
            // A player's choice still open (an end-of-Battle-Phase Special Summon offer, SummonPromptsAndCosts v163) is
            // answered first: EndTurn refuses to end the turn while it is open.
            while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                yield return null;
            if (engine.GameOver || engine.TurnPlayer != ai) yield break;
            engine.EndTurn(ai);
            onStep?.Invoke();
        }

        /// <summary>
        /// Battle or Main Phase 1 → Main Phase 2. An open response is answered first
        /// (<see cref="DuelEngine.TryEnterMainPhase2"/> refuses while one is open).
        /// </summary>
        static IEnumerable<object> EnterMainPhase2(DuelEngine engine, DuelistState ai)
        {
            while (!engine.GameOver && engine.TurnPlayer == ai &&
                   (engine.Phase == DuelPhase.Battle || engine.Phase == DuelPhase.Main1))
            {
                if (engine.TryEnterMainPhase2(ai))
                    yield break;
                if (!engine.IsAwaitingPlayerResponse && !engine.IsAwaitingEffectTarget)
                    yield break;
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
            }
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
                // A response opened by the previous action (a search, a Normal Summon) is answered
                // before this step activates or summons again.
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                if (engine.GameOver || engine.TurnPlayer != ai || !engine.InMainPhase)
                    yield break;

                // Advantage spells first (policy table)
                if (AiHeuristicPolicy.PreferPotOfGreed &&
                    TryActivateById(engine, ai, SpellTrapEffects.PotOfGreed, phaseLabel))
                {
                    yield return null;
                    continue;
                }

                // Search cards (Normal+): fetch a key piece from the Deck
                if (AiHeuristicPolicy.UseSearchCards)
                {
                    var searchCard = ai.Hand.FirstOrDefault(c =>
                        c?.Def != null && HasSearchEffect(c) && engine.CanActivateSpellTrap(ai, c, true));
                    if (searchCard != null && engine.TryActivateSpellTrap(ai, searchCard, true))
                    {
                        yield return null;
                        continue;
                    }
                }

                // Graveyard recursion (Normal+): bring back a fallen monster or recycle a key spell
                if (AiHeuristicPolicy.UseGraveyardRecursion)
                {
                    var gyCard = ai.Hand.FirstOrDefault(c =>
                        c?.Def != null && HasGyRecursion(c) && engine.CanActivateSpellTrap(ai, c, true));
                    if (gyCard != null && engine.TryActivateSpellTrap(ai, gyCard, true))
                    {
                        yield return null;
                        continue;
                    }
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

                // Fusion / Ritual (Hard+): summon a boss monster from the Extra / Deck
                if (AiHeuristicPolicy.UseExtraDeck && AiHeuristicPolicy.PreferSynchroFusion)
                {
                    var extraCard = ai.Hand.FirstOrDefault(c =>
                        c?.Def != null && HasFusionOrRitual(c) && engine.CanActivateSpellTrap(ai, c, true));
                    if (extraCard != null && engine.TryActivateSpellTrap(ai, extraCard, true))
                    {
                        yield return null;
                        continue;
                    }
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
                    if (TributesForTempAtkOnly(m)) continue;
                    // Winged Minion: no own monster to boost (SpecialSummonEffects batch, v161).
                    if (TextEffects.SpecialSummonEffectsRuntime.AiSkipsIgnition(engine, ai, m)) continue;
                    // BattleBoosts batch (v161): Gray Wing's discard only when a second attack can matter.
                    if (TextEffects.BattleBoostsRuntime.AiSkipsIgnition(engine, ai, m)) continue;
                    // BattleDestroyedGyRecovery batch (v162): Crimson Sentry family — only for a better monster back.
                    if (TextEffects.BattleDestroyedGyRecoveryRuntime.AiSkipsIgnition(engine, ai, m)) continue;
                    // ChaosBanishSummons batch: Chaos Sorcerer / BLS only on an opponent's monster; Strike Ninja never here.
                    if (TextEffects.ChaosBanishSummonsRuntime.AiSkipsIgnition(engine, ai, m)) continue;
                    // IgnitionCostsAndSimpleSpells batch: Sand Gambler only while the opponent controls a monster.
                    if (TextEffects.IgnitionCostsAndSimpleSpellsRuntime.AiSkipsIgnition(engine, ai, m)) continue;
                    if (!engine.TryActivateSpellTrap(ai, m, fromHand: false)) continue;
                    // Only the AI's own pick: a choice the effect gives the player (Spear Cretin's GY
                    // selection, FlipMassEffects v142) waits for the player below.
                    while (engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                           engine.PendingActivation.Controller != engine.Player)
                    {
                        var t = engine.PendingActivation.LegalTargets
                            .OrderByDescending(x => x.CurrentAtk).FirstOrDefault();
                        if (t == null || !engine.TrySelectEffectTarget(t))
                            engine.CancelEffectTargeting();
                    }

                    yield return null;
                    continue;
                }

                // Face-up Spell ignition burns (Wave-Motion Cannon, PerCountBurns v141): lethal, or 3+ counted.
                var fieldIgnition = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    TextEffects.PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition(engine, ai, st) &&
                    engine.CanActivateSpellTrap(ai, st, fromHand: false));
                if (fieldIgnition != null && engine.TryActivateSpellTrap(ai, fieldIgnition, fromHand: false))
                {
                    yield return null;
                    continue;
                }

                // Face-up Quick Effects (FaceUpQuickEffects v160): Type Zero Magic Crusher, Skull Lair.
                var quickEffect = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    TextEffects.FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, ai, st) &&
                    engine.CanActivateSpellTrap(ai, st, fromHand: false));
                if (quickEffect != null && engine.TryActivateSpellTrap(ai, quickEffect, fromHand: false))
                {
                    yield return null;
                    continue;
                }

                // Their Set copies are activated (placed face-up) once the Quick Effect has a use.
                var quickSet = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    TextEffects.FaceUpQuickEffectsRuntime.AiWantsToActivateSetCopy(engine, ai, st) &&
                    engine.CanActivateSpellTrap(ai, st, fromHand: false));
                if (quickSet != null && engine.TryActivateSpellTrap(ai, quickSet, fromHand: false))
                {
                    yield return null;
                    continue;
                }

                // FlipMassAndOpponentPicks batch: a Set Huge Revolution once its Main Phase condition holds.
                var revolution = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    TextEffects.FlipMassAndOpponentPicksRuntime.AiWantsToActivateSetTrap(engine, ai, st) &&
                    engine.CanActivateSpellTrap(ai, st, fromHand: false));
                if (revolution != null && engine.TryActivateSpellTrap(ai, revolution, fromHand: false))
                {
                    yield return null;
                    continue;
                }

                // Delinquent Duo (DiscardTriggers v142): 2 of the player's cards for 1 while the AI's LP allow it.
                // Main Phase 1 only: there RunTurn waits (IsAwaitingEffectTarget) for the human victim's forced
                // choice; its Main Phase 2 loop does not, and would try to end the turn with the choice open.
                var discardSpell = engine.Phase != DuelPhase.Main1
                    ? null
                    : ai.Hand.FirstOrDefault(c =>
                        TextEffects.DiscardTriggersRuntime.AiWantsToActivateFromHand(engine, ai, c) &&
                        engine.CanActivateSpellTrap(ai, c, true));
                if (discardSpell != null && engine.TryActivateSpellTrap(ai, discardSpell, true))
                {
                    yield return null;
                    continue;
                }

                // Reasoning / Reversal Quiz (CallsDiceAndSetTriggers v163). Main Phase 1 only, like Delinquent Duo: RunTurn
                // waits there for the human opponent's Level declaration.
                var callSpell = engine.Phase != DuelPhase.Main1
                    ? null
                    : ai.Hand.FirstOrDefault(c =>
                        TextEffects.CallsDiceAndSetTriggersRuntime.AiWantsToActivateFromHand(engine, ai, c) &&
                        engine.CanActivateSpellTrap(ai, c, true));
                if (callSpell != null && engine.TryActivateSpellTrap(ai, callSpell, true))
                {
                    yield return null;
                    continue;
                }

                // Painful Choice (HandDisruption v142): show 5 Deck cards, the opponent gives one to the hand.
                var handDisruption = ai.Hand.FirstOrDefault(c =>
                    TextEffects.HandDisruptionRuntime.AiWantsToActivateFromHand(engine, ai, c) &&
                    engine.CanActivateSpellTrap(ai, c, true));
                if (handDisruption != null && engine.TryActivateSpellTrap(ai, handDisruption, true))
                {
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
                            // Resolve pending monster targets (AI auto-picks inside MonsterEffects). Not the
                            // player's own choice (Cyber Jar / Morphing Jar #2 summons, FlipMassEffects v142).
                            while (engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                                   engine.PendingActivation.IsMonsterEffect &&
                                   engine.PendingActivation.Controller != engine.Player)
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

                // A printed Special Summon from the hand (Toon monsters while it controls "Toon World", Toons
                // batch): does not use the Normal Summon; only when the Tributes are worth less ATK than the Toon.
                var ownProcedure = ai.Hand.FirstOrDefault(c =>
                    c != null && (TextEffects.ToonsRuntime.AiWantsHandProcedure(engine, ai, c) ||
                                  TextEffects.NegationsAndGySummonsRuntime.AiWantsOwnHandProcedure(engine, ai, c)));
                if (ownProcedure != null)
                {
                    engine.ClearTributes();
                    if (engine.TrySpecialSummonProcedure(ai, ownProcedure))
                    {
                        engine.LogReview(DuelLogKind.Ai, "AI",
                            $"{phaseLabel}: SS {ownProcedure.Name} by its own procedure (ATK {ownProcedure.CurrentAtk})");
                        // The player may answer the Summon (Solemn Judgment) before the AI goes on.
                        while (engine.IsAwaitingPlayerResponse)
                            yield return null;
                        yield return null;
                        continue;
                    }
                }

                // Normal Summon / Set best legal monster
                if (!ai.NormalSummonUsed)
                {
                    var summonable = ai.Hand
                        .Where(c => engine.CanNormalSummonOrSet(ai, c))
                        .ToList();

                    // Prefer Level ≤4 non-Flip for face-up Attack; Set Flip Effect monsters (and Kiseitai-style
                    // "attacked while face-down" monsters, SpiritsAndMisc batch)
                    var freeAtk = summonable
                        .Where(c => c.Level <= 4 && !IsFlipEffectMonster(c) &&
                                    !TextEffects.SpiritsAndMiscRuntime.AiSetsFaceDown(c))
                        .OrderByDescending(c => c.CurrentAtk)
                        .ToList();
                    var freeFlip = summonable
                        .Where(c => c.Level <= 4 && (IsFlipEffectMonster(c) ||
                                                     TextEffects.SpiritsAndMiscRuntime.AiSetsFaceDown(c)))
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
                        pick = freeFlip[0];
                        asSet = true;
                    }
                    else if (freeFlip.Count > 0)
                    {
                        pick = freeFlip[0];
                        asSet = true;
                    }
                    else if (tribute.Count > 0 && !(AiHeuristicPolicy.AvoidSacrifice && ai.MonsterCount < engine.Player.MonsterCount))
                    {
                        pick = tribute[0];
                        asSet = false;
                    }

                    if (pick != null)
                    {
                        engine.ClearTributes();
                        var ok = engine.TryNormalSummon(ai, pick, asSet);
                        // RuleLocks batch: under a Summon lock (Jam Breeding Machine) the monster can still be Set.
                        if (!ok && !asSet &&
                            Rules.SummonProcedures.CheckNormalOrTribute(engine, ai, pick, asSet: true).Legal)
                        {
                            engine.ClearTributes();
                            asSet = true;
                            ok = engine.TryNormalSummon(ai, pick, asSet: true);
                        }
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
                var trapSlots = AiHeuristicPolicy.DefensivePlay ? 3 : AiHeuristicPolicy.AggressivePlay ? 1 : 2;
                if (AiHeuristicPolicy.PreferSetTrapsBeforeEnd && ai.SpellTrapsOnField().Count() < trapSlots)
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

        /// <summary>
        /// "Tribute another monster → this card gains ATK until the end of the turn"
        /// (The Little Swordsman of Aile): a card-for-a-turn trade the simple AI must not
        /// spend every Main Phase.
        /// </summary>
        /// <summary>Dark Elf / Toll: never pay the last Life Points just to attack.</summary>
        static bool AttackCostIsLethal(DuelEngine engine, DuelistState ai, CardInstance m)
        {
            TextEffects.TextEffectRuntime.AttackCosts(engine, ai, m, out var lp, out _);
            // The monster's own local-lineage attack cost (Dark Elf / Toon "You must pay 500 LP to declare an
            // attack with this monster", Toons batch).
            if (!m.AttackLpCostPaidThisDeclaration)
                lp += TextEffects.TextEffectRuntime.DeclareAttackLpCost(m);
            return lp > 0 && lp >= ai.LifePoints;
        }

        static bool TributesForTempAtkOnly(CardInstance m)
        {
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(m);
            if (prog == null) return false;
            var act = prog.ClausesFor(TextEffects.EffectTiming.Activate);
            return act.Count > 0 && act.TrueForAll(c =>
                c != null && c.Action == TextEffects.EffectActionKind.GainThisAtkUntilEnd &&
                c.RequiresTributeCount > 0);
        }

        static bool IsFlipEffectMonster(CardInstance c) =>
            c?.Def?.type != null &&
            c.Def.type.IndexOf("Flip", System.StringComparison.OrdinalIgnoreCase) >= 0;

        static bool CardHasAction(CardInstance card, TextEffects.EffectActionKind action)
        {
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(card);
            if (prog == null) return false;
            return prog.ClausesFor(TextEffects.EffectTiming.Activate).Any(c => c != null && c.Action == action);
        }

        static bool HasGyRecursion(CardInstance card) =>
            CardHasAction(card, TextEffects.EffectActionKind.AddFromGyToHand) ||
            CardHasAction(card, TextEffects.EffectActionKind.SpecialSummonFromGy);

        static bool HasSearchEffect(CardInstance card) =>
            CardHasAction(card, TextEffects.EffectActionKind.AddFromDeckToHand);

        static bool HasFusionOrRitual(CardInstance card) =>
            CardHasAction(card, TextEffects.EffectActionKind.FusionSummonRegistered) ||
            CardHasAction(card, TextEffects.EffectActionKind.RitualSummon);

        /// <summary>Yields once per attack declaration.</summary>
        static IEnumerable<object> RunBattlePhase(DuelEngine engine, DuelistState ai)
        {
            // Monsters whose declaration the engine refused this Battle Phase (Ring of Magnetism,
            // "cannot be targeted for attacks", Duelist Kingdom no-direct …). Without this the loop
            // retried the same illegal attack forever and the AI turn never ended (DR-03).
            var refused = new HashSet<CardInstance>();
            // Hard cap as a second guard: no field allows this many declarations in one Battle Phase.
            const int maxIterations = 64;
            for (var iteration = 0;
                 iteration < maxIterations &&
                 !engine.GameOver && engine.Phase == DuelPhase.Battle && engine.TurnPlayer == ai;
                 iteration++)
            {
                // Refresh attacker list each step (board changes)
                var attackers = ai.MonstersOnField()
                    .Where(m => !refused.Contains(m) && engine.CanAttack(ai, m) && !AttackCostIsLethal(engine, ai, m))
                    .ToList();

                // AttackObligations batch: a monster that "must attack if able" (Berserk Gorilla, Amazoness Archers,
                // Diffusion Wave-Motion, Staunch Defender's lock) is met before the AI's own preference, so the duty is
                // satisfied and the Battle Phase / turn can end (ygopro is_capable_attack).
                if (TextEffects.AttackObligationsRuntime.TryChooseObligedAttack(engine, ai, out var obligedAttacker,
                        out var obligedTarget))
                {
                    if (obligedTarget == null)
                    {
                        if (!engine.TryAttack(ai, obligedAttacker, null))
                            TextEffects.AttackObligationsRuntime.NoteRefused(engine, obligedAttacker, null);
                    }
                    else if (!engine.TryAttack(ai, obligedAttacker, obligedTarget))
                    {
                        TextEffects.AttackObligationsRuntime.NoteRefused(engine, obligedAttacker, obligedTarget);
                    }

                    while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                        yield return null;
                    yield return null;
                    continue;
                }

                CardInstance attacker = null;

                // BaitTraps (Hard+): lead with the weakest attacker to soak Mirror Force / Waboku
                if (AiHeuristicPolicy.BaitTraps && attackers.Count > 1)
                {
                    var weakest = attackers.OrderBy(m => m.CurrentAtk).FirstOrDefault();
                    var others = attackers.Where(m => m != weakest).ToList();
                    if (weakest != null && others.Any(o => o.CurrentAtk >= weakest.CurrentAtk * 2))
                    {
                        attacker = weakest;
                        refused.Add(weakest);
                    }
                }

                if (attacker == null)
                {
                    // CalculateLethal: if any attacker can kill, lead with it
                    if (AiHeuristicPolicy.CalculateLethal)
                    {
                        var lethal = attackers
                            .Where(m => m.CurrentAtk >= engine.Player.LifePoints)
                            .OrderByDescending(m => m.CurrentAtk)
                            .FirstOrDefault();
                        if (lethal != null)
                            attacker = lethal;
                    }

                    if (attacker == null)
                        attacker = attackers.OrderByDescending(m => m.CurrentAtk).FirstOrDefault();
                }

                if (attacker == null)
                    yield break;

                var target = ChooseAttackTarget(engine, ai, attacker);

                if (target == null &&
                    (engine.Player.MonsterCount == 0 || engine.CanAttackDirectly(ai, attacker)))
                {
                    // Engine logs the attack declaration.
                    if (!engine.TryAttack(ai, attacker, null))
                    {
                        refused.Add(attacker);
                        continue;
                    }

                    while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                        yield return null;
                    yield return null;
                    continue;
                }

                if (target == null)
                {
                    // A Toon limited to face-up Toon targets (Toons batch) may find none while a weaker attacker still
                    // can: skip only this one.
                    if (TextEffects.ToonsRuntime.ForbidsDirectAttack(engine, attacker) ||
                        TextEffects.SelfStatMonstersRuntime.ForbidsDirectAttack(engine, attacker) ||
                        // Asura Priest (SpiritMonsters batch): no worthwhile monster left that it has not attacked.
                        TextEffects.SpiritMonstersRuntime.IsAttackAllFollowUp(engine, attacker))
                    {
                        refused.Add(attacker);
                        continue;
                    }

                    // No beneficial target for the strongest attacker: weaker ones will not find one either.
                    yield break;
                }

                engine.LogReview(DuelLogKind.Ai, "AI",
                    $"Battle: {attacker.Name}({attacker.CurrentAtk}) → " +
                    (target.FaceUp
                        ? $"{target.Name}({target.CurrentAtk}/{target.CurrentDef})"
                        : "face-down"));
                // Engine logs the attack declaration.
                if (!engine.TryAttack(ai, attacker, target))
                {
                    refused.Add(attacker);
                    continue;
                }

                // Player may activate Mirror Force / Negate Attack / Waboku — do not continue until resolved
                while (engine.IsAwaitingPlayerResponse || engine.IsAwaitingEffectTarget)
                    yield return null;
                yield return null;
            }
        }

        /// <summary>
        /// Legal target selection using official battle math. Only targets the engine will accept are
        /// considered (Ring of Magnetism host, "cannot be targeted for attacks", attacks-must-be-direct).
        /// Face-down monsters are judged by <see cref="AiHeuristicPolicy.FaceDownDefEstimate"/>, never by
        /// their hidden DEF (GDD 3.2: the AI does not look at face-down cards).
        /// </summary>
        static CardInstance ChooseAttackTarget(DuelEngine engine, DuelistState ai, CardInstance attacker)
        {
            if (ai.MustAttackDirectlyThisTurn) return null;
            // A Toon that may attack directly (the player controls no face-up Toon) goes for the LP (Toons batch).
            if (TextEffects.ToonsRuntime.GrantsDirectAttack(engine, attacker) &&
                engine.CanAttackDirectly(ai, attacker))
                return null;
            var playerMonsters = engine.Player.MonstersOnField()
                .Where(m => m != null && !TextEffects.ContinuousProtections.CannotBeAttackTarget(engine, m) &&
                            // Toon rule: only face-up Toon monsters while the player controls one.
                            !TextEffects.ToonsRuntime.ForbidsAttackTarget(engine, attacker, m) &&
                            // SpiritMonsters batch: Inaba White Rabbit attacks directly only; Asura Priest attacks
                            // each monster once per Battle Phase.
                            !TextEffects.SpiritMonstersRuntime.ForbidsAttackTarget(engine, attacker, m) &&
                            // Taunt (BattleRules batch): only the selected monster this turn.
                            !TextEffects.BattleRulesRuntime.ForbidsAttackTarget(engine, attacker, m))
                .ToList();
            var magnetHosts = engine.MagnetismHosts(ai);
            if (magnetHosts.Count == 1)
                playerMonsters = playerMonsters.Where(m => m == magnetHosts[0]).ToList();
            if (playerMonsters.Count == 0) return null;

            bool IsDefense(CardInstance m) =>
                !m.FaceUp || m.Position == BattlePosition.Defense;

            int KnownDef(CardInstance m) =>
                m.FaceUp ? m.CurrentDef : (AiHeuristicPolicy.RespectBackrow ? 2000 : AiHeuristicPolicy.FaceDownDefEstimate);

            var atk = attacker.CurrentAtk;

            // 1) Destroy weakest face-up ATK we can beat (ATK > their ATK)
            foreach (var t in playerMonsters
                         .Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                         .OrderBy(m => m.CurrentAtk))
            {
                if (atk > t.CurrentAtk)
                    return t;
            }

            // 2) Destroy face-down / DEF only if ATK > (known or estimated) DEF — otherwise we take
            //    damage and they live.
            foreach (var t in playerMonsters
                         .Where(IsDefense)
                         .OrderBy(KnownDef))
            {
                if (atk > KnownDef(t))
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
