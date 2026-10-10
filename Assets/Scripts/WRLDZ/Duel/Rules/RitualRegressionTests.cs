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

            RunPlayerChoice(db, Check);

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }

        const int Kuriboh = 40640057; // Level 1 fodder
        const int GiantSoldier = 13039848; // Level 3
        const int MaskedBeast = 49064413;  // DARK Ritual Monster, Level 8

        /// <summary>
        /// The player picks the Ritual Monster and the Tributes (hand or field) whenever there is a real choice;
        /// one legal option is used as is; the AI always auto-picks. "Equal or greater" refuses an unnecessary
        /// Tribute, "exactly" refuses an overshoot, CANCEL restarts the Tributes then undoes the activation.
        /// </summary>
        static void RunPlayerChoice(CardDatabase db, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, detail);

            // ── Real choice: the prompt opens, the picks decide, a field monster counts ──
            {
                var e = Fresh(db);
                var p = e.Player;
                ClearSide(p);
                InHand(e, p, HungryBurger);
                var recipe = InHand(e, p, HamburgerRecipe);
                var celtic = InHand(e, p, Celtic);
                var k1 = InHand(e, p, Kuriboh);
                var k2 = InHand(e, p, Kuriboh);
                var beaver = PlaceMonster(e, p, Beaver, 2);
                var opened = e.TryActivateSpellTrap(p, recipe, fromHand: true);
                var pend = e.PendingActivation;
                Check("Ritual choice: a real choice opens the Tribute picker",
                    opened && pend != null && pend.AwaitingRitualTributes &&
                    pend.TargetKind == EffectTargetKind.RitualTribute && p.Hand.Contains(recipe),
                    $"opened={opened} pending={pend?.TargetKind}");
                Check("Ritual choice: hand and field monsters are both offered",
                    e.IsLegalEffectTarget(celtic) && e.IsLegalEffectTarget(k1) && e.IsLegalEffectTarget(beaver));
                var first = e.TrySelectEffectTarget(beaver);
                var stillPicking = e.PendingActivation != null && e.PendingActivation.AwaitingRitualTributes;
                var second = e.TrySelectEffectTarget(celtic);
                Check("Ritual choice: the picked Tributes (field Beaver + hand Celtic) are the ones sent",
                    first && stillPicking && second && e.PendingActivation == null &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == HungryBurger) &&
                    p.Graveyard.Contains(beaver) && p.Graveyard.Contains(celtic) &&
                    p.Hand.Contains(k1) && p.Hand.Contains(k2) && p.Graveyard.Contains(recipe),
                    $"first={first} picking={stillPicking} second={second}");
            }

            // ── "Equal or greater": no unnecessary Tribute; CANCEL restarts, then undoes ──
            {
                var e = Fresh(db);
                var p = e.Player;
                ClearSide(p);
                var burger = InHand(e, p, HungryBurger);
                var recipe = InHand(e, p, HamburgerRecipe);
                var celtic = InHand(e, p, Celtic);
                var beaver = InHand(e, p, Beaver);
                var k1 = InHand(e, p, Kuriboh);
                var k2 = InHand(e, p, Kuriboh);
                e.TryActivateSpellTrap(p, recipe, fromHand: true);
                e.TrySelectEffectTarget(k1);
                e.TrySelectEffectTarget(celtic);
                // {Kuriboh, Celtic} = 5: Beaver would make 9 with Kuriboh unnecessary (9 − 1 ≥ 6).
                Check("Ritual choice: an unnecessary Tribute is refused (Beaver after Kuriboh + Celtic)",
                    e.PendingActivation != null && !e.IsLegalEffectTarget(beaver) && e.IsLegalEffectTarget(k2));
                var restarted = e.CancelEffectTargeting();
                var p2 = e.PendingActivation;
                Check("Ritual choice: CANCEL after picks starts the Tributes over",
                    restarted && p2 != null && p2.AwaitingRitualTributes && p2.RitualPicks.Count == 0 &&
                    e.IsLegalEffectTarget(k1) && e.IsLegalEffectTarget(celtic));
                var undone = e.CancelEffectTargeting();
                Check("Ritual choice: CANCEL with no picks undoes the activation (all cards stay in hand)",
                    undone && e.PendingActivation == null && p.Hand.Contains(recipe) && p.Hand.Contains(burger) &&
                    p.Hand.Contains(k1) && p.Hand.Contains(celtic) && p.Graveyard.Count == 0 &&
                    !p.MonstersOnField().Any());
            }

            // ── "Exactly equal" (Contract with the Abyss): an overshoot is never offered ──
            if (db.Get(RitualProcedures.ContractWithTheAbyss) != null)
            {
                var e = Fresh(db);
                var p = e.Player;
                ClearSide(p);
                InHand(e, p, HungryBurger);
                var contract = InHand(e, p, RitualProcedures.ContractWithTheAbyss);
                var gs1 = InHand(e, p, GiantSoldier);
                var gs2 = InHand(e, p, GiantSoldier);
                var celtic = InHand(e, p, Celtic);
                var k1 = InHand(e, p, Kuriboh);
                var k2 = InHand(e, p, Kuriboh);
                var opened = e.TryActivateSpellTrap(p, contract, fromHand: true);
                e.TrySelectEffectTarget(celtic);
                Check("Ritual choice (exact 6): after Celtic (4) only the Level 1s complete it — Giant Soldier (3) would overshoot",
                    opened && e.PendingActivation != null && !e.IsLegalEffectTarget(gs1) && e.IsLegalEffectTarget(k1));
                e.TrySelectEffectTarget(k1);
                e.TrySelectEffectTarget(k2);
                Check("Ritual choice (exact 6): Celtic + 2 Kuriboh Tributed, both Giant Soldiers kept",
                    e.PendingActivation == null &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == HungryBurger) &&
                    p.Graveyard.Contains(celtic) && p.Graveyard.Contains(k1) && p.Graveyard.Contains(k2) &&
                    p.Hand.Contains(gs1) && p.Hand.Contains(gs2));
            }

            // ── Two different Ritual Monsters: the player picks which; one legal Tribute set is used as is ──
            if (db.Get(RitualProcedures.ContractWithTheAbyss) != null && db.Get(MaskedBeast) != null)
            {
                var e = Fresh(db);
                var p = e.Player;
                ClearSide(p);
                var burger = InHand(e, p, HungryBurger);   // DARK Lv6: {3,3}
                var beast = InHand(e, p, MaskedBeast);     // DARK Lv8: {4,4} only (Burger 6 has no Level 2 partner)
                var contract = InHand(e, p, RitualProcedures.ContractWithTheAbyss);
                var celtic = InHand(e, p, Celtic);
                var beaver = InHand(e, p, Beaver);
                InHand(e, p, GiantSoldier);
                InHand(e, p, GiantSoldier);
                e.TryActivateSpellTrap(p, contract, fromHand: true);
                var pend = e.PendingActivation;
                Check("Ritual choice: two different Ritual Monsters open the monster picker",
                    pend != null && pend.AwaitingRitualMonster &&
                    e.IsLegalEffectTarget(burger) && e.IsLegalEffectTarget(beast));
                e.TrySelectEffectTarget(beast);
                Check("Ritual choice: The Masked Beast summoned with its only legal set (Celtic + Beaver), no second prompt",
                    e.PendingActivation == null &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == MaskedBeast) &&
                    p.Graveyard.Contains(celtic) && p.Graveyard.Contains(beaver) && p.Hand.Contains(burger));
            }

            // ── Another Ritual Monster in hand is itself a legal Tribute: Burger (6) + 2 Kuriboh = 8 for Beast ──
            if (db.Get(RitualProcedures.ContractWithTheAbyss) != null && db.Get(MaskedBeast) != null)
            {
                var e = Fresh(db);
                var p = e.Player;
                ClearSide(p);
                var burger = InHand(e, p, HungryBurger);
                var beast = InHand(e, p, MaskedBeast);
                var contract = InHand(e, p, RitualProcedures.ContractWithTheAbyss);
                var celtic = InHand(e, p, Celtic);
                var beaver = InHand(e, p, Beaver);
                var k1 = InHand(e, p, Kuriboh);
                var k2 = InHand(e, p, Kuriboh);
                e.TryActivateSpellTrap(p, contract, fromHand: true);
                e.TrySelectEffectTarget(beast);
                var offered = e.PendingActivation != null && e.PendingActivation.AwaitingRitualTributes &&
                              e.IsLegalEffectTarget(burger) && e.IsLegalEffectTarget(celtic);
                e.TrySelectEffectTarget(burger);
                e.TrySelectEffectTarget(k1);
                e.TrySelectEffectTarget(k2);
                Check("Ritual choice: a Ritual Monster in hand is offered as a Tribute (Burger + 2 Kuriboh for Beast)",
                    offered && e.PendingActivation == null &&
                    p.MonstersOnField().Any(m => m != null && m.CardId == MaskedBeast) &&
                    p.Graveyard.Contains(burger) && p.Hand.Contains(celtic) && p.Hand.Contains(beaver),
                    $"offered={offered} pending={e.PendingActivation?.TargetKind}");
            }

            // ── The AI never gets a prompt ──
            {
                var e = Fresh(db);
                if (InteractionRegressionTests.ReachOpponentMain(e))
                {
                    var o = e.Opponent;
                    ClearSide(o);
                    InHand(e, o, HungryBurger);
                    var recipe = InHand(e, o, HamburgerRecipe);
                    InHand(e, o, Celtic);
                    InHand(e, o, Kuriboh);
                    InHand(e, o, Kuriboh);
                    PlaceMonster(e, o, Beaver, 2);
                    var ok = e.TryActivateSpellTrap(o, recipe, fromHand: true);
                    if (e.IsAwaitingResponse) e.PassResponse();
                    Check("Ritual choice: the AI auto-picks (no prompt) and summons",
                        ok && e.PendingActivation == null &&
                        o.MonstersOnField().Any(m => m != null && m.CardId == HungryBurger),
                        $"ok={ok} pending={e.PendingActivation?.TargetKind}");
                }
                else
                    Check("Ritual choice: reached the opponent's Main Phase for the AI check", false, "");
            }
        }

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
