using System.Linq;
using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Data-driven Fusion: Polymerization fuses any Extra Deck Fusion Monster whose named
    /// materials (parsed from its own official text, "A" + "B" [+ "C"]) are present in the
    /// hand/field — not just a hardcoded pair. Verifies non-registered 2- and 3-material
    /// fusions, material→GY, and activation legality.
    /// </summary>
    public static class FusionRegressionTests
    {
        const int Polymerization = 24094653;
        // Non-registered fusions (not the old hardcoded Gaia/Black Skull pair):
        const int DarkFlareKnight = 13722870; // Dark Magician + Flame Swordsman
        const int DarkMagician = 46986414;
        const int FlameSwordsman = 45231177;
        const int MokeyMokeyKing = 13803864;  // Mokey Mokey x3
        const int MokeyMokey = 27288416;

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

            // ── 2-material non-registered fusion from the hand ──
            var e = Fresh(db);
            var p = e.Player;
            ClearSide(p);
            p.ExtraDeck.Clear();
            p.ExtraDeck.Add(DarkFlareKnight);
            InHand(e, p, DarkMagician);
            InHand(e, p, FlameSwordsman);
            var poly = InHand(e, p, Polymerization);
            Check("Fusion: Polymerization legal with materials for a non-registered fusion",
                e.CanActivateSpellTrap(p, poly, fromHand: true));
            Check("Fusion: Polymerization resolves",
                e.TryActivateSpellTrap(p, poly, fromHand: true));
            Check("Fusion: Dark Flare Knight is on the field",
                p.MonstersOnField().Any(m => m != null && m.CardId == DarkFlareKnight));
            Check("Fusion: both materials sent to GY",
                p.Graveyard.Count(c => c != null && (c.CardId == DarkMagician || c.CardId == FlameSwordsman)) == 2);
            Check("Fusion: fusion removed from Extra Deck", !p.ExtraDeck.Contains(DarkFlareKnight));
            Check("Fusion: Polymerization sent to GY", p.Graveyard.Contains(poly));

            // ── Illegal when a material is missing ──
            var e2 = Fresh(db);
            var p2 = e2.Player;
            ClearSide(p2);
            p2.ExtraDeck.Clear();
            p2.ExtraDeck.Add(DarkFlareKnight);
            InHand(e2, p2, DarkMagician); // only one of the two materials
            var poly2 = InHand(e2, p2, Polymerization);
            Check("Fusion: Polymerization illegal without all materials",
                !e2.CanActivateSpellTrap(p2, poly2, fromHand: true));

            // ── 3-material fusion (Mokey Mokey King = Mokey Mokey x3), materials on field ──
            var e3 = Fresh(db);
            var p3 = e3.Player;
            ClearSide(p3);
            p3.ExtraDeck.Clear();
            p3.ExtraDeck.Add(MokeyMokeyKing);
            PlaceMonster(e3, p3, MokeyMokey, 0);
            PlaceMonster(e3, p3, MokeyMokey, 1);
            InHand(e3, p3, MokeyMokey); // third from hand
            var poly3 = InHand(e3, p3, Polymerization);
            Check("Fusion: 3-material fusion legal (2 field + 1 hand)",
                e3.CanActivateSpellTrap(p3, poly3, fromHand: true));
            Check("Fusion: Mokey Mokey King resolves and is summoned",
                e3.TryActivateSpellTrap(p3, poly3, fromHand: true) &&
                p3.MonstersOnField().Any(m => m != null && m.CardId == MokeyMokeyKing));
            Check("Fusion: three Mokey Mokey materials in GY",
                p3.Graveyard.Count(c => c != null && c.CardId == MokeyMokey) == 3);

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
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
