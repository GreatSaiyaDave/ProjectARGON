using System.Collections;
using System.Linq;
using UnityEngine;
using WRLDZ.Duel;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Handles AI responses during the player's turn (Hard+ difficulty).
    /// Activates traps, quick-play spells, hand traps, and negation.
    /// </summary>
    public static class AiResponseHandler
    {
        /// <summary>
        /// Attempt to respond to a pending response window offered to the AI.
        /// Returns true if a response was made, false if the AI should pass.
        /// </summary>
        public static bool TryRespond(DuelEngine engine, DuelistState ai)
        {
            if (!AiHeuristicPolicy.UseChainResponse)
                return false;

            if (engine.GameOver || engine.TurnPlayer == ai)
                return false;

            if (engine.IsAwaitingPlayerResponse && engine.PendingResponse != null)
            {
                var response = engine.PendingResponse;

                if (response.Timing == ResponseTiming.SummonNegation)
                    return TrySummonNegation(engine, ai);

                if (response.Timing == ResponseTiming.AttackDeclared)
                    return TryAttackResponse(engine, ai);

                if (response.Timing == ResponseTiming.ChainResponse)
                    return TryChainResponse(engine, ai);

                if (response.Timing == ResponseTiming.DamageCalculation)
                    return TryDamageStepResponse(engine, ai);
            }

            return false;
        }

        static bool TrySummonNegation(DuelEngine engine, DuelistState ai)
        {
            var summonedMonster = engine.SummonAwaitingSuccess;
            if (summonedMonster == null)
                return false;

            var atk = summonedMonster.CurrentAtk;
            var level = summonedMonster.Level;

            // Expert: if we detected an archetype, be more proactive with negation
            var archetypeBias = AiHeuristicPolicy.DetectArchetype && !string.IsNullOrEmpty(AiArchetypeDetector.DetectedArchetype) ? 200 : 0;

            if (atk < 1500 + archetypeBias && level < 5)
                return false;

            if (atk >= 1000)
            {
                var trapHole = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    !st.FaceUp && st.CardId == SpellTrapEffects.TrapHole);
                if (trapHole != null && engine.TryActivateSpellTrap(ai, trapHole, fromHand: false))
                {
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Negated summon with Trap Hole ({atk} ATK)");
                    return true;
                }
            }

            if (atk >= 2000 || level >= 7)
            {
                var solemnJudgment = ai.Hand.FirstOrDefault(c =>
                    c.CardId == SpellTrapEffects.SolemnJudgment && engine.CanActivateSpellTrap(ai, c, true));
                if (solemnJudgment != null && ai.LifePoints > 4000)
                {
                    engine.TryActivateSpellTrap(ai, solemnJudgment, true);
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Negated summon with Solemn Judgment ({atk} ATK)");
                    return true;
                }
            }

            if (atk >= 1500 && ai.MonsterCount > 0)
            {
                var hornOfHeaven = ai.Hand.FirstOrDefault(c =>
                    c.CardId == SpellTrapEffects.HornOfHeaven && engine.CanActivateSpellTrap(ai, c, true));
                if (hornOfHeaven != null)
                {
                    engine.TryActivateSpellTrap(ai, hornOfHeaven, true);
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Negated summon with Horn of Heaven ({atk} ATK)");
                    return true;
                }
            }

            return false;
        }

        static bool TryAttackResponse(DuelEngine engine, DuelistState ai)
        {
            var attacker = engine.DeclaredAttacker;
            if (attacker == null)
                return false;

            var atk = attacker.CurrentAtk;

            var archetypeBias = AiHeuristicPolicy.DetectArchetype && !string.IsNullOrEmpty(AiArchetypeDetector.DetectedArchetype) ? 200 : 0;

            if (ai.MonsterCount >= 2 || atk >= 2000 - archetypeBias)
            {
                var mirrorForce = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    !st.FaceUp && st.CardId == SpellTrapEffects.MirrorForce);
                if (mirrorForce != null && engine.TryActivateSpellTrap(ai, mirrorForce, fromHand: false))
                {
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Activated Mirror Force vs {attacker.Name} ({atk} ATK)");
                    return true;
                }
            }

            if (atk >= ai.LifePoints && ai.LifePoints < 4000)
            {
                var waboku = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    !st.FaceUp && st.CardId == SpellTrapEffects.Waboku);
                if (waboku != null && engine.TryActivateSpellTrap(ai, waboku, fromHand: false))
                {
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Activated Waboku vs {attacker.Name} ({atk} ATK)");
                    return true;
                }
            }

            if (atk >= 2000 && ai.MonsterCount == 0)
            {
                var negateAttack = ai.SpellTrapsOnField().FirstOrDefault(st =>
                    !st.FaceUp && st.CardId == SpellTrapEffects.NegateAttack);
                if (negateAttack != null && engine.TryActivateSpellTrap(ai, negateAttack, fromHand: false))
                {
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Negated attack with Negate Attack vs {attacker.Name}");
                    return true;
                }
            }

            return false;
        }

        static bool TryChainResponse(DuelEngine engine, DuelistState ai)
        {
            var chain = engine.Chain;
            if (chain == null || chain.Count == 0)
                return false;

            var lastLink = chain.Last;
            var cardName = lastLink.Card?.Name ?? "";

            if (lastLink.Card != null && lastLink.Card.Def != null && lastLink.Card.Def.IsSpell)
            {
                var mst = ai.Hand.FirstOrDefault(c =>
                    c.CardId == SpellTrapEffects.Mst && engine.CanActivateSpellTrap(ai, c, true));
                if (mst != null)
                {
                    engine.TryActivateSpellTrap(ai, mst, true);
                    engine.LogReview(DuelLogKind.Ai, "AI", $"Chained Mystical Space Typhoon to {cardName}");
                    return true;
                }
            }

            return false;
        }

        static bool TryDamageStepResponse(DuelEngine engine, DuelistState ai)
        {
            var kuriboh = ai.Hand.FirstOrDefault(c =>
                c.CardId == SpellTrapEffects.Kuriboh && engine.CanActivateSpellTrap(ai, c, true));
            if (kuriboh != null)
            {
                engine.TryActivateSpellTrap(ai, kuriboh, true);
                engine.LogReview(DuelLogKind.Ai, "AI", "Discarded Kuriboh to reduce battle damage");
                return true;
            }

            var wingedKuriboh = ai.Hand.FirstOrDefault(c =>
                c.CardId == SpellTrapEffects.WingedKuriboh && engine.CanActivateSpellTrap(ai, c, true));
            if (wingedKuriboh != null)
            {
                engine.TryActivateSpellTrap(ai, wingedKuriboh, true);
                engine.LogReview(DuelLogKind.Ai, "AI", "Discarded Winged Kuriboh to negate battle damage");
                return true;
            }

            return false;
        }
    }
}
