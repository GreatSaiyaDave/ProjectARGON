using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Generalized Ritual Summon: a Ritual Spell (race "Ritual") compiles to a
    /// RitualSummon clause that Special Summons the named Ritual Monster from hand by
    /// Tributing monsters whose total Level ≥ the required amount. Verifies compile,
    /// activation legality (monster in hand + enough Tribute Levels), and the
    /// end-to-end summon (monster to field, Tributes + spell to GY).
    /// </summary>
    public static class RitualRegressionTests
    {
        const int HamburgerRecipe = 80811661; // Ritual Spell → Hungry Burger, needs Level 6+
        const int HungryBurger = 30243636;    // Ritual Monster, Level 6
        const int BlackLusterRitual = 55761792; // → Black Luster Soldier, needs Level 8+
        const int BlackLusterSoldier = 5405694;  // Ritual Monster, Level 8
        const int Celtic = 91152256;  // Warrior, Level 4
        const int Beaver = 32452818;  // Warrior, Level 4
        const int Bewd = 89631139;    // Level 8

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

            // ── Compile: Ritual Spell → RitualSummon clause ──
            var recipe = db.Get(HamburgerRecipe);
            var prog = recipe != null ? CardTextEffectCompiler.Compile(recipe) : null;
            Check("Ritual: Hamburger Recipe FullyCompiled", prog != null && prog.FullyCompiled);
            var rc = prog?.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.RitualSummon);
            Check("Ritual: clause is RitualSummon, names Hungry Burger, needs Level 6",
                rc != null && rc.NamedCard == "Hungry Burger" && rc.Amount == 6,
                rc == null ? "no clause" : $"name={rc.NamedCard} amt={rc.Amount}");

            // ── End-to-end summon: Recipe + Burger + two Lv4 fodder in hand ──
            var engine = Fresh(db);
            var p = engine.Player;
            ClearSide(p);
            var burger = InHand(engine, p, HungryBurger);
            var recipeCard = InHand(engine, p, HamburgerRecipe);
            InHand(engine, p, Celtic);
            InHand(engine, p, Beaver);

            Check("Ritual: Recipe is activatable with monster + Tributes in hand",
                engine.CanActivateSpellTrap(p, recipeCard, fromHand: true));
            var summoned = engine.TryActivateSpellTrap(p, recipeCard, fromHand: true);
            Check("Ritual: activation resolves", summoned);
            Check("Ritual: Hungry Burger is on the field face-up Attack",
                p.MonstersOnField().Any(m => m != null && m.CardId == HungryBurger &&
                    m.FaceUp && m.Position == BattlePosition.Attack));
            Check("Ritual: Hungry Burger left the hand", !p.Hand.Contains(burger));
            Check("Ritual: two Tributes are in the GY",
                p.Graveyard.Count(c => c != null && (c.CardId == Celtic || c.CardId == Beaver)) == 2);
            Check("Ritual: the Ritual Spell is in the GY", p.Graveyard.Contains(recipeCard));

            // ── Legality: no Ritual Monster in hand → illegal ──
            var e2 = Fresh(db);
            var p2 = e2.Player;
            ClearSide(p2);
            var recipe2 = InHand(e2, p2, HamburgerRecipe);
            InHand(e2, p2, Celtic);
            InHand(e2, p2, Beaver);
            Check("Ritual: illegal without the Ritual Monster in hand",
                !e2.CanActivateSpellTrap(p2, recipe2, fromHand: true));

            // ── Legality: monster present but not enough Tribute Levels ──
            var e3 = Fresh(db);
            var p3 = e3.Player;
            ClearSide(p3);
            InHand(e3, p3, HungryBurger);         // needs 6
            var recipe3 = InHand(e3, p3, HamburgerRecipe);
            InHand(e3, p3, Celtic);                // only Level 4 available
            Check("Ritual: illegal with insufficient Tribute Levels (4 < 6)",
                !e3.CanActivateSpellTrap(p3, recipe3, fromHand: true));

            // ── Field monsters can be Tributed (Black Luster Soldier needs 8) ──
            var e4 = Fresh(db);
            var p4 = e4.Player;
            ClearSide(p4);
            InHand(e4, p4, BlackLusterSoldier);
            var ritual4 = InHand(e4, p4, BlackLusterRitual);
            PlaceMonster(e4, p4, Bewd, 0);          // Level 8 on the field
            Check("Ritual: Black Luster Ritual activatable Tributing a field Lv8",
                e4.CanActivateSpellTrap(p4, ritual4, fromHand: true));
            Check("Ritual: Black Luster Ritual resolves",
                e4.TryActivateSpellTrap(p4, ritual4, fromHand: true));
            Check("Ritual: Black Luster Soldier summoned, field Lv8 Tributed to GY",
                p4.MonstersOnField().Any(m => m != null && m.CardId == BlackLusterSoldier) &&
                p4.Graveyard.Any(c => c != null && c.CardId == Bewd));

            // ── "Exactly equal" (Contract with the Abyss → any DARK Ritual Monster) ──
            // Target Hungry Burger (DARK, Level 6): Tributes must total EXACTLY 6.
            const int Contract = 69035382, GiantSoldier = 13039848; // GiantSoldier = Level 3
            if (db.Get(Contract) != null)
            {
                // Exact subset {3,3}=6 exists (even though a Lv8 is also in hand) → legal,
                // and the exact selection must Tribute the two Lv3s, NOT the Lv8.
                var eOk = Fresh(db);
                var pO = eOk.Player;
                ClearSide(pO);
                InHand(eOk, pO, HungryBurger);           // DARK Level 6 target
                var contractO = InHand(eOk, pO, Contract);
                var keep = InHand(eOk, pO, Bewd);        // Level 8 — must NOT be Tributed
                InHand(eOk, pO, GiantSoldier);           // Level 3
                InHand(eOk, pO, GiantSoldier);           // Level 3  → {3,3} == 6
                Check("Ritual(exact): Contract legal when Tributes can total exactly 6",
                    eOk.CanActivateSpellTrap(pO, contractO, fromHand: true));
                Check("Ritual(exact): resolves and summons Hungry Burger",
                    eOk.TryActivateSpellTrap(pO, contractO, fromHand: true) &&
                    pO.MonstersOnField().Any(m => m != null && m.CardId == HungryBurger));
                Check("Ritual(exact): chose the exact {3,3} Tribute, kept the Lv8",
                    pO.Hand.Contains(keep) &&
                    pO.Graveyard.Count(c => c != null && c.CardId == GiantSoldier) == 2);

                // Overpay must be illegal: only {4,4}=8 available, no subset equals 6
                // (a "Level 6 or more" spell WOULD allow this — proves exact ≠ ≥).
                var eBad = Fresh(db);
                var pB = eBad.Player;
                ClearSide(pB);
                InHand(eBad, pB, HungryBurger);          // DARK Level 6 target
                var contractB = InHand(eBad, pB, Contract);
                InHand(eBad, pB, Celtic);                // Level 4
                InHand(eBad, pB, Beaver);                // Level 4  → no subset == 6
                Check("Ritual(exact): illegal when no Tribute subset equals 6 (overpay rejected)",
                    !eBad.CanActivateSpellTrap(pB, contractB, fromHand: true));
            }

            // ── Sweep EVERY Ritual Spell end-to-end (not just the anchors) ──
            var summonFail = new System.Collections.Generic.List<string>();
            var ritualSpells = db.GetAllCards().Where(c =>
                c != null && string.Equals(c.race, "Ritual", StringComparison.OrdinalIgnoreCase) &&
                (c.type?.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            var tested = 0;
            foreach (var spell in ritualSpells)
            {
                var sp = CardTextEffectCompiler.Compile(spell);
                var clause = sp?.ClauseList.FirstOrDefault(c => c != null && c.Action == EffectActionKind.RitualSummon);
                if (clause == null)
                {
                    summonFail.Add($"{spell.name}(no RitualSummon clause)");
                    continue;
                }

                // Resolve which monster this spell summons.
                var target = !string.IsNullOrEmpty(clause.NamedCard)
                    ? db.GetAllCards().FirstOrDefault(c => c != null && c.IsRitualMonster &&
                        string.Equals(c.name, clause.NamedCard, StringComparison.OrdinalIgnoreCase))
                    : db.GetAllCards().FirstOrDefault(c => c != null && c.IsRitualMonster &&
                        string.Equals(c.attribute, clause.AttributeFilter, StringComparison.OrdinalIgnoreCase));
                if (target == null) continue; // target not in this pool — compile-only
                var need = target.level > 0 ? target.level : Math.Max(1, clause.Amount);

                var e = Fresh(db);
                var pl = e.Player;
                ClearSide(pl);
                InHand(e, pl, target.id);
                var spellCard = InHand(e, pl, spell.id);
                for (var i = 0; i < need; i++) InHand(e, pl, Kuriboh); // need × Level 1 = exactly `need`

                if (!e.CanActivateSpellTrap(pl, spellCard, fromHand: true) ||
                    !e.TryActivateSpellTrap(pl, spellCard, fromHand: true) ||
                    !pl.MonstersOnField().Any(m => m != null && m.CardId == target.id) ||
                    !pl.Graveyard.Contains(spellCard))
                {
                    summonFail.Add($"{spell.name}->{target.name}(Lv{need})");
                    continue;
                }

                tested++;
            }

            Check($"Ritual: every Ritual Spell summons its monster (tested {tested}/{ritualSpells.Count})",
                summonFail.Count == 0, string.Join(", ", summonFail));

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }

        const int Kuriboh = 40640057; // Level 1 fodder

        static DuelEngine Fresh(CardDatabase db)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, CardDatabase.LoadDeck("lab_rules_player.json"),
                CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);
            return engine;
        }

        static void ClearSide(DuelistState who)
        {
            who.Hand.Clear();
            for (var i = 0; i < who.MonsterZones.Length; i++) who.MonsterZones[i].Occupant = null;
            for (var i = 0; i < who.SpellTrapZones.Length; i++) who.SpellTrapZones[i].Occupant = null;
            who.Graveyard.Clear();
        }

        static CardInstance InHand(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Hand.Add(c);
            return c;
        }

        static CardInstance PlaceMonster(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.Position = BattlePosition.Attack;
            c.SetThisTurn = false;
            c.SummonedThisTurn = false;
            who.MonsterZones[zone].Occupant = c;
            return c;
        }
    }
}
