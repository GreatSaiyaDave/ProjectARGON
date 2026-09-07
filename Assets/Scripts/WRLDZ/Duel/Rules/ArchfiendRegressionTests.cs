using System.Text;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Archfiends — the archetype's signature Standby-Phase maintenance cost and the
    /// "Pandemonium" waiver. Verifies the cost is compiled from card text (not an id
    /// special-case), that the controller pays it each of their own Standby Phases,
    /// that the "pay or destroy" variant destroys when unpaid, that Pandemonium waives
    /// it for Archfiend monsters (either player's copy) but not for non-Archfiend
    /// upkeep (Messenger of Peace), and that an unpayable mandatory cost loses the duel.
    /// </summary>
    public static class ArchfiendRegressionTests
    {
        const int Vilepawn = 73219648;      // pays 500 (mandatory)
        const int Desrook = 72192100;       // pays 500 (mandatory)
        const int Darkbishop = 35798491;    // pays 500 (mandatory)
        const int Infernalqueen = 8581705;  // pays 500 (mandatory)
        const int Shadowknight = 9603356;   // pays 900 (mandatory)
        const int Terrorking = 35975813;    // pays 800 (mandatory)
        const int SkullArchfiend = 61370518;// pay 500 or destroy this card
        const int Pandemonium = 94585852;   // waives the Archfiend maintenance cost
        const int Messenger = 44656491;     // pay 100 or destroy — NOT an Archfiend
        const int ArchfiendSoldier = 49881766; // Archfiend vanilla — no maintenance cost

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

            // ── Compile shape: the cost comes from text, with the printed amount ──
            bool HasMaintenance(int id, int amount)
            {
                var def = db.Get(id);
                if (def == null) return false;
                var prog = CardTextEffectCompiler.Compile(def);
                return prog != null && prog.ClauseList.Exists(c =>
                    c != null &&
                    c.Timing == EffectTiming.StandbyPhase &&
                    c.Action == EffectActionKind.StandbyMaintenancePayLp &&
                    c.PayLpAmount == amount);
            }

            Check("Compile: Vilepawn upkeep = pay 500 (mandatory)", HasMaintenance(Vilepawn, 500));
            Check("Compile: Desrook upkeep = pay 500 (mandatory)", HasMaintenance(Desrook, 500));
            Check("Compile: Darkbishop upkeep = pay 500 (mandatory)", HasMaintenance(Darkbishop, 500));
            Check("Compile: Infernalqueen upkeep = pay 500 (mandatory)", HasMaintenance(Infernalqueen, 500));
            Check("Compile: Terrorking upkeep = pay 800 (mandatory)", HasMaintenance(Terrorking, 800));
            Check("Compile: Shadowknight upkeep = pay 900 (mandatory)", HasMaintenance(Shadowknight, 900));

            {
                var def = db.Get(SkullArchfiend);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Compile: Skull Archfiend = pay 500 or destroy this card",
                    prog != null && prog.ClauseList.Exists(c =>
                        c != null &&
                        c.Timing == EffectTiming.StandbyPhase &&
                        c.Action == EffectActionKind.PayLpOrDestroyThis &&
                        c.PayLpAmount == 500));
            }

            {
                var def = db.Get(ArchfiendSoldier);
                var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
                Check("Compile: Archfiend Soldier (vanilla) has no maintenance cost",
                    prog == null || !prog.ClauseList.Exists(c =>
                        c != null && (c.Action == EffectActionKind.StandbyMaintenancePayLp ||
                                      c.Action == EffectActionKind.PayLpOrDestroyThis)));
            }

            // ── End-to-end: the controller pays each of their own Standby Phases ──
            int StandbyLpDelta(int monsterId, int startLp, int pandemoniumSide)
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = startLp;
                PlaceMonster(engine, who, monsterId, 0);
                if (pandemoniumSide == 1) PlaceFieldSpell(engine, who, Pandemonium);
                if (pandemoniumSide == 2) PlaceFieldSpell(engine, engine.Opponent, Pandemonium);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                return startLp - who.LifePoints;
            }

            Check("Standby: Vilepawn controller pays 500", StandbyLpDelta(Vilepawn, 8000, 0) == 500);
            Check("Standby: Terrorking controller pays 800", StandbyLpDelta(Terrorking, 8000, 0) == 800);
            Check("Standby: Shadowknight controller pays 900", StandbyLpDelta(Shadowknight, 8000, 0) == 900);

            // Two Archfiends → both upkeep costs are paid the same Standby Phase.
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 8000;
                PlaceMonster(engine, who, Vilepawn, 0);
                PlaceMonster(engine, who, Terrorking, 1);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Standby: two Archfiends pay both costs (500+800)", 8000 - who.LifePoints == 1300);
            }

            // The opponent does NOT pay the controller's upkeep during the controller's Standby.
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                var opp = engine.Opponent;
                ClearField(who);
                ClearField(opp);
                who.LifePoints = 8000;
                opp.LifePoints = 8000;
                PlaceMonster(engine, opp, Vilepawn, 0);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Standby: opponent's Archfiend not charged on your Standby",
                    who.LifePoints == 8000 && opp.LifePoints == 8000);
            }

            // ── Pandemonium waives the Archfiend maintenance cost (either player's copy) ──
            Check("Pandemonium: your copy waives Vilepawn's cost", StandbyLpDelta(Vilepawn, 8000, 1) == 0);
            Check("Pandemonium: opponent's copy also waives it", StandbyLpDelta(Vilepawn, 8000, 2) == 0);

            // ── Skull Archfiend: pay-or-destroy variant ──
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 8000;
                var skull = PlaceMonster(engine, who, SkullArchfiend, 0);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Skull Archfiend: pays 500 when able and stays",
                    who.LifePoints == 7500 && OnField(who, skull));
            }
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 300; // cannot pay 500
                var skull = PlaceMonster(engine, who, SkullArchfiend, 0);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Skull Archfiend: destroyed when it cannot pay",
                    !OnField(who, skull) && who.Graveyard.Contains(skull) && who.LifePoints == 300);
            }
            {
                // Pandemonium waives it — an underfunded Skull Archfiend survives untouched.
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 300;
                var skull = PlaceMonster(engine, who, SkullArchfiend, 0);
                PlaceFieldSpell(engine, who, Pandemonium);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Pandemonium: underfunded Skull Archfiend is not destroyed",
                    OnField(who, skull) && who.LifePoints == 300);
            }

            // ── Waiver is Archfiend-scoped: Messenger of Peace still pays under Pandemonium ──
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 8000;
                PlaceSpellTrap(engine, who, Messenger, 0);
                PlaceFieldSpell(engine, who, Pandemonium);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Pandemonium: non-Archfiend Messenger of Peace still pays 100",
                    8000 - who.LifePoints == 100);
            }

            // ── Mandatory cost you cannot afford loses the duel ──
            {
                var engine = FreshEngine(db);
                var who = engine.Player;
                ClearField(who);
                ClearField(engine.Opponent);
                who.LifePoints = 500; // Terrorking demands 800
                PlaceMonster(engine, who, Terrorking, 0);
                TextEffectRuntime.FirePhaseTriggers(engine, who, EffectTiming.StandbyPhase);
                Check("Standby: unpayable mandatory upkeep drops to 0 and ends the duel",
                    who.LifePoints == 0 && engine.GameOver);
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }

        static DuelEngine FreshEngine(CardDatabase db)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, CardDatabase.LoadDeck("lab_rules_player.json"),
                CardDatabase.LoadDeck("lab_rules_ai.json"), cinematicOpening: false);
            return engine;
        }

        static void ClearField(DuelistState who)
        {
            for (var i = 0; i < who.MonsterZones.Length; i++)
                who.MonsterZones[i].Occupant = null;
            for (var i = 0; i < who.SpellTrapZones.Length; i++)
                who.SpellTrapZones[i].Occupant = null;
            if (who.FieldSpellZone != null)
                who.FieldSpellZone.Occupant = null;
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

        static CardInstance PlaceSpellTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance PlaceFieldSpell(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            if (who.FieldSpellZone != null)
                who.FieldSpellZone.Occupant = c;
            return c;
        }

        static bool OnField(DuelistState who, CardInstance card)
        {
            foreach (var z in who.MonsterZones)
                if (z.Occupant == card) return true;
            return false;
        }
    }
}
