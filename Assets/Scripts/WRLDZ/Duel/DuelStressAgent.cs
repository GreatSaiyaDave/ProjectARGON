using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Side-agnostic autopilot for stress tests (either player or opponent).
    /// Mirrors SimpleAi priorities without UI delays or human-only response skips.
    /// </summary>
    public static class DuelStressAgent
    {
        /// <summary>Clear response / targeting / stuck attack windows (both sides).</summary>
        public static int DrainWindows(DuelEngine engine, int maxSteps = 32)
        {
            var n = 0;
            for (var i = 0; i < maxSteps; i++)
            {
                if (engine == null || engine.GameOver) break;

                if (engine.IsAwaitingEffectTarget)
                {
                    if (!AutoPickTarget(engine))
                        engine.CancelEffectTargeting();
                    else
                        engine.TryCompleteDeferredBattleDestruction();
                    n++;
                    continue;
                }

                // Finish Damage Step after Flip resolved without pending
                engine.TryCompleteDeferredBattleDestruction();

                if (engine.IsAwaitingResponse)
                {
                    AutoRespond(engine);
                    n++;
                    continue;
                }

                if (engine.HasDeclaredAttack && !engine.IsAwaitingResponse && !engine.IsAwaitingEffectTarget)
                {
                    engine.ResolveDeclaredAttack();
                    n++;
                    continue;
                }

                if (engine.RecoverStuckCombat())
                {
                    n++;
                    continue;
                }

                break;
            }

            return n;
        }

        /// <summary>Play a full turn for <paramref name="me"/> (must be TurnPlayer).</summary>
        public static void PlayTurn(DuelEngine engine, DuelistState me)
        {
            if (engine == null || me == null || engine.GameOver) return;
            if (engine.TurnPlayer != me) return;

            DrainWindows(engine);

            if (engine.Phase != DuelPhase.Main1)
            {
                engine.TryEndTurnSafe(me);
                DrainWindows(engine);
                return;
            }

            RunMainPhase(engine, me, "MP1");
            DrainWindows(engine);
            if (engine.GameOver || engine.TurnPlayer != me) return;

            // Battle
            if (engine.Phase == DuelPhase.Main1 && engine.CanConductBattlePhase)
            {
                var canAtk = me.MonstersOnField()
                    .Any(m => m.FaceUp && m.Position == BattlePosition.Attack && engine.CanAttack(me, m));
                if (canAtk)
                {
                    engine.TryEnterBattlePhase(me);
                    RunBattle(engine, me);
                    DrainWindows(engine);
                }
            }

            if (engine.GameOver || engine.TurnPlayer != me) return;

            if (engine.Phase == DuelPhase.Battle || engine.Phase == DuelPhase.Main1)
            {
                engine.TryEnterMainPhase2(me);
                RunMainPhase(engine, me, "MP2");
                DrainWindows(engine);
            }

            if (engine.GameOver || engine.TurnPlayer != me) return;

            if (!engine.TryEndTurnSafe(me))
            {
                DrainWindows(engine);
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(me);
            }

            DrainWindows(engine);
        }

        static void RunMainPhase(DuelEngine engine, DuelistState me, string label)
        {
            var opp = engine.OpponentOf(me);
            const int maxSteps = 12;
            for (var step = 0; step < maxSteps; step++)
            {
                if (engine.GameOver || engine.TurnPlayer != me || !engine.InMainPhase)
                    return;

                DrainWindows(engine);

                // Advantage / removal spells
                if (TryActivateId(engine, me, SpellTrapEffects.PotOfGreed)) continue;
                if ((opp.MonstersOnField().Count(m => m.FaceUp) > 0 || opp.MonsterCount >= 2) &&
                    TryActivateId(engine, me, SpellTrapEffects.Raigeki))
                    continue;
                if (opp.MonsterCount > me.MonsterCount &&
                    opp.MonstersOnField().Any(m => m.FaceUp) &&
                    TryActivateId(engine, me, SpellTrapEffects.DarkHole))
                    continue;

                // Other common lab spells
                if (TryActivateId(engine, me, SpellTrapEffects.HeavyStorm)) continue;
                if (TryActivateId(engine, me, SpellTrapEffects.MonsterReborn)) continue;
                if (TryActivateId(engine, me, SpellTrapEffects.Polymerization)) continue;
                if (TryActivateId(engine, me, SpellTrapEffects.CardDestruction)) continue;
                if (TryActivateId(engine, me, SpellTrapEffects.SwordsOfRevealingLight)) continue;
                if (TryActivateId(engine, me, SpellTrapEffects.FluteOfSummoningDragon)) continue;

                var fieldSpell = me.Hand.FirstOrDefault(c =>
                    c?.Def != null && c.Def.IsFieldSpell &&
                    engine.CanActivateSpellTrap(me, c, true));
                if (fieldSpell != null && engine.TryActivateSpellTrap(me, fieldSpell, true))
                {
                    DrainWindows(engine);
                    continue;
                }

                var ign = me.MonstersOnField().FirstOrDefault(m =>
                    m != null && engine.CanActivateSpellTrap(me, m, false));
                if (ign != null && engine.TryActivateSpellTrap(me, ign, false))
                {
                    DrainWindows(engine);
                    continue;
                }

                // Flip Summon
                foreach (var m in me.MonstersOnField().ToList())
                {
                    if (!engine.CanFlipSummon(me, m)) continue;
                    if (!IsFlip(m) && m.CurrentAtk < 1400) continue;
                    if (engine.TryFlipSummon(me, m))
                    {
                        DrainWindows(engine);
                        goto ContinueOuter;
                    }
                }

                // Normal Summon / Set
                if (!me.NormalSummonUsed)
                {
                    var summonable = me.Hand.Where(c => engine.CanNormalSummonOrSet(me, c)).ToList();
                    var freeAtk = summonable.Where(c => c.Level <= 4 && !IsFlip(c))
                        .OrderByDescending(c => c.CurrentAtk).ToList();
                    var freeFlip = summonable.Where(c => c.Level <= 4 && IsFlip(c))
                        .OrderByDescending(c => c.CurrentDef).ToList();
                    var tribute = summonable.Where(c => c.Level >= 5)
                        .OrderByDescending(c => c.CurrentAtk).ToList();

                    CardInstance pick = null;
                    var asSet = false;
                    if (freeAtk.Count > 0) { pick = freeAtk[0]; asSet = false; }
                    else if (freeFlip.Count > 0) { pick = freeFlip[0]; asSet = true; }
                    else if (tribute.Count > 0) { pick = tribute[0]; asSet = false; }

                    if (pick != null)
                    {
                        engine.ClearTributes();
                        // Auto-pick tributes if needed
                        var need = TcgRules.TributesRequired(pick.Level);
                        if (need > 0)
                        {
                            var tributes = me.MonstersOnField()
                                .OrderBy(m => m.CurrentAtk)
                                .Take(need)
                                .ToList();
                            if (tributes.Count < need)
                            {
                                // can't tribute summon
                            }
                            else
                            {
                                foreach (var t in tributes)
                                    engine.ToggleTribute(me, t);
                            }
                        }

                        if (engine.TryNormalSummon(me, pick, asSet))
                        {
                            DrainWindows(engine);
                            continue;
                        }
                    }
                }

                // Set traps / continuous
                if (me.SpellTrapsOnField().Count() < 3)
                {
                    var trap = me.Hand.FirstOrDefault(c =>
                        c.Def != null && c.Def.IsTrap && engine.CanSetSpellTrap(me, c));
                    if (trap != null && engine.TrySetSpellTrap(me, trap))
                    {
                        DrainWindows(engine);
                        continue;
                    }

                    var setSpell = me.Hand.FirstOrDefault(c =>
                        c.Def != null && c.Def.IsSpell &&
                        c.CardId != SpellTrapEffects.PotOfGreed &&
                        engine.CanSetSpellTrap(me, c));
                    if (setSpell != null && engine.TrySetSpellTrap(me, setSpell))
                    {
                        DrainWindows(engine);
                        continue;
                    }
                }

                return; // nothing left

                ContinueOuter: ;
            }
        }

        static void RunBattle(DuelEngine engine, DuelistState me)
        {
            var opp = engine.OpponentOf(me);
            var safety = 0;
            while (!engine.GameOver && engine.Phase == DuelPhase.Battle && engine.TurnPlayer == me &&
                   safety++ < 12)
            {
                DrainWindows(engine);
                var attacker = me.MonstersOnField()
                    .Where(m => engine.CanAttack(me, m))
                    .OrderByDescending(m => m.CurrentAtk)
                    .FirstOrDefault();
                if (attacker == null) break;

                var target = ChooseTarget(opp, attacker);
                if (target == null && opp.MonsterCount > 0 && !engine.CanAttackDirectly(me, attacker))
                    break; // no beneficial attack

                engine.TryAttack(me, attacker, target);
                DrainWindows(engine);
            }
        }

        static CardInstance ChooseTarget(DuelistState opp, CardInstance attacker)
        {
            var monsters = opp.MonstersOnField().ToList();
            if (monsters.Count == 0) return null;
            var atk = attacker.CurrentAtk;

            foreach (var t in monsters.Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                         .OrderBy(m => m.CurrentAtk))
            {
                if (atk > t.CurrentAtk) return t;
            }

            foreach (var t in monsters.Where(m => !m.FaceUp || m.Position == BattlePosition.Defense)
                         .OrderBy(m => m.CurrentDef))
            {
                if (atk > t.CurrentDef) return t;
            }

            foreach (var t in monsters.Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                         .OrderBy(m => m.CurrentAtk))
            {
                if (atk == t.CurrentAtk) return t;
            }

            return null;
        }

        static bool TryActivateId(DuelEngine engine, DuelistState me, int id)
        {
            var card = me.Hand.FirstOrDefault(c =>
                c.CardId == id && engine.CanActivateSpellTrap(me, c, true));
            if (card == null)
            {
                // field set activation
                card = me.SpellTrapsOnField().FirstOrDefault(c =>
                    c.CardId == id && engine.CanActivateSpellTrap(me, c, false));
                if (card == null) return false;
                if (!engine.TryActivateSpellTrap(me, card, false)) return false;
            }
            else if (!engine.TryActivateSpellTrap(me, card, true))
                return false;

            DrainWindows(engine);
            return true;
        }

        static void AutoRespond(DuelEngine engine)
        {
            if (engine.PendingResponse == null) return;
            var who = engine.PendingResponse.Responder;
            var legal = engine.PendingResponse.LegalCards;
            if (who == null || legal == null || legal.Count == 0)
            {
                engine.PassResponse();
                return;
            }

            CardInstance pick = null;
            if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
            {
                pick = legal.FirstOrDefault(c => c.CardId == SpellTrapEffects.MirrorForce)
                       ?? legal.FirstOrDefault(c => c.CardId == SpellTrapEffects.NegateAttack)
                       ?? legal.FirstOrDefault(c => c.CardId == SpellTrapEffects.Waboku);
                var atk = engine.PendingResponse.Attacker;
                if (pick != null && atk != null && atk.CurrentAtk < 1200 &&
                    pick.CardId == SpellTrapEffects.Waboku)
                    pick = null;
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
            {
                pick = legal.FirstOrDefault(c => c.CardId == SpellTrapEffects.TrapHole);
            }

            if (pick == null)
            {
                engine.PassResponse();
                return;
            }

            // Activate set trap (from field)
            engine.TryActivateSpellTrap(who, pick, fromHand: false);
        }

        static bool AutoPickTarget(DuelEngine engine)
        {
            var p = engine.PendingActivation;
            if (p?.LegalTargets == null || p.LegalTargets.Count == 0)
                return false;

            CardInstance t;
            // Sangan / deck search: prefer lowest ATK (sensible mandatory auto)
            if (p.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq ||
                p.TargetKind == EffectTargetKind.FieldSpellInYourDeck ||
                p.TargetKind == EffectTargetKind.EquipSpellInYourDeck ||
                p.TargetKind == EffectTargetKind.MonsterInYourDeckFiltered ||
                p.TargetKind == EffectTargetKind.MonsterInYourDeckToSummon)
                t = p.LegalTargets
                    .OrderBy(x => x.Def != null ? x.Def.atk : 9999)
                    .ThenBy(x => x.Name)
                    .FirstOrDefault();
            else
                t = p.LegalTargets.OrderByDescending(x => x.CurrentAtk).FirstOrDefault();

            if (t == null) return false;
            return engine.TrySelectEffectTarget(t);
        }

        static bool IsFlip(CardInstance c) =>
            c?.Def?.type != null &&
            c.Def.type.IndexOf("Flip", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
