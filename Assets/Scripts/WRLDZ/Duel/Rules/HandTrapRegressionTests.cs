using System.Collections.Generic;
using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Hand traps — monster effects activatable from the hand that act like Trap Cards.
    /// Verifies the category is driven by the compiled program (a discard-from-hand
    /// "no battle damage" Quick Effect at Damage Calculation), not a Kuriboh id special-case:
    /// membership, timing/attacker legality, and generic collection.
    /// </summary>
    public static class HandTrapRegressionTests
    {
        const int Kuriboh = 40640057;
        const int Celtic = 91152256; // vanilla Warrior — not a hand trap
        const int Bewd = 89631139;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; sb.AppendLine("PASS  " + name); }
                else { fail++; sb.AppendLine("FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
            }

            var db = CardDatabase.Load();
            if (db == null || db.Count == 0)
            {
                sb.AppendLine("FAIL  CardDatabase load");
                sb.AppendLine("--- 0 passed, 1 failed ---");
                return sb.ToString();
            }

            var engine = new DuelEngine();
            engine.StartDuel(db, CardDatabase.LoadDeck("lab_rules_player.json"),
                CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);

            var kuriboh = engine.CreateCardInstance(Kuriboh);
            var vanilla = engine.CreateCardInstance(Celtic);
            var attacker = engine.CreateCardInstance(Bewd);

            // Category membership is program-driven, not an id list.
            Check("HandTrap: Kuriboh is recognized (compiled discard-from-hand Quick Effect)",
                MonsterEffects.HasDamageCalcHandTrap(kuriboh));
            Check("HandTrap: a vanilla monster is not a hand trap",
                !MonsterEffects.HasDamageCalcHandTrap(vanilla));

            var who = engine.Player;
            var opp = engine.Opponent;
            who.Hand.Clear();
            who.Hand.Add(kuriboh);

            Check("HandTrap: legal at Damage Calculation vs an opponent's attacker",
                MonsterEffects.IsLegalHandDamageCalculationEffect(
                    who, kuriboh, ResponseTiming.DamageCalculation, opp, attacker));
            Check("HandTrap: illegal if you are the attacker",
                !MonsterEffects.IsLegalHandDamageCalculationEffect(
                    who, kuriboh, ResponseTiming.DamageCalculation, who, attacker));
            Check("HandTrap: illegal outside the Damage Calculation window",
                !MonsterEffects.IsLegalHandDamageCalculationEffect(
                    who, kuriboh, ResponseTiming.AttackDeclared, opp, attacker));
            Check("HandTrap: illegal when not in hand",
                !MonsterEffects.IsLegalHandDamageCalculationEffect(
                    who, engine.CreateCardInstance(Kuriboh), ResponseTiming.DamageCalculation, opp, attacker));

            var legal = new List<CardInstance>();
            MonsterEffects.CollectLegalHandDamageCalculation(
                who, ResponseTiming.DamageCalculation, opp, attacker, legal);
            Check("HandTrap: collected generically as a legal DC response", legal.Contains(kuriboh));

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }
    }
}
