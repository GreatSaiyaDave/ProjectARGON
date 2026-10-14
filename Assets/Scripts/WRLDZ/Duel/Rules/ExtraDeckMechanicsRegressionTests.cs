using System.Collections.Generic;
using System.Linq;
using System.Text;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Data-driven pre-Link Extra Deck mechanics (Synchro Summon, Xyz Summon, Pendulum Summon).
    /// The production cards_db still has no Synchro / Xyz / Pendulum printing, so each test registers
    /// synthetic cards (via <see cref="CardDatabase.Register"/>) and then runs the structural rules
    /// — tuner math, same-Level math, Scales — plus the Pendulum "field → face-up Extra Deck" rule.
    /// </summary>
    public static class ExtraDeckMechanicsRegressionTests
    {
        const int FieldSpellId = 50954681;

        static CardDef Def(int id, string name, string type, string race, int level, int atk, int def,
            string desc = "", int scaleLeft = 0, int scaleRight = 0)
        {
            return new CardDef
            {
                id = id,
                name = name,
                type = type,
                frameType = type.IndexOf("Pendulum", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "pendulum" : "normal",
                race = race,
                attribute = "DARK",
                level = level,
                atk = atk,
                def = def,
                desc = desc,
                scaleLeft = scaleLeft,
                scaleRight = scaleRight,
                archetype = string.Empty
            };
        }

        // Stardust's 1900/800 Level 4 "Tuner" companion (a Wind Warrior)
        static CardDef Tuner() =>
            Def(90100001, "Test Wind Warrior", "Tuner Effect Monster", "Warrior", 4, 1900, 800);

        static CardDef Level4() =>
            Def(90100002, "Test Earth Golem", "Effect Monster", "Rock", 4, 1400, 1400);

        static CardDef Level2() =>
            Def(90100012, "Test Level 2 Minor", "Effect Monster", "Rock", 2, 800, 700);

        static CardDef Level8() =>
            Def(90100009, "Test Level 8 Wall", "Effect Monster", "Rock", 8, 2000, 2000);

        static CardDef Synchro6() =>
            Def(90100003, "Test Stardust Clone", "Synchro Effect Monster", "Warrior", 6, 2500, 2000,
                "1 Tuner + 1 or more non-Tuner monsters\nWhen this card is destroyed: you can target 1 card on the field; destroy it.");

        static CardDef Xyz4() =>
            Def(90100004, "Test Number Clone", "XYZ Effect Monster", "Warrior", 4, 2100, 1600,
                "2 Level 4 monsters\nOnce per turn: You can detach 1 Xyz Material from this card; draw 1 card.");

        static CardDef Scale2() =>
            Def(90100005, "Test Pendulum Scale 2", "Pendulum Effect Monster", "Spellcaster", 3, 1000, 1000,
                "2\n3 1000 1000\nDuring your Main Phase: You can destroy this card, and if you do, draw 1 card.",
                scaleLeft: 2, scaleRight: 2);

        static CardDef Scale8() =>
            Def(90100006, "Test Pendulum Scale 8", "Pendulum Effect Monster", "Spellcaster", 6, 1600, 1200,
                "8\n6 1600 1200\nDuring your Main Phase: You can destroy this card, and if you do, draw 1 card.",
                scaleLeft: 8, scaleRight: 8);

        static CardDef Hand4() =>
            Def(90100007, "Test Hand Monster 4", "Effect Monster", "Dinosaur", 4, 1700, 1200);

        static CardDef Hand3() =>
            Def(90100008, "Test Hand Monster 3", "Effect Monster", "Dinosaur", 3, 1500, 1000);

        static CardInstance Instance(int id, CardDef def, bool faceUp = true) =>
            new CardInstance { CardId = id, Def = def, InstanceId = nextId++, FaceUp = faceUp };

        static int nextId = 700000;

        static void Register(params CardDef[] defs)
        {
            var db = CardDatabase.Load();
            foreach (var d in defs) db.Register(d);
        }

        /// <summary>Places a card into a free Monster Zone, face-up, as if it were a summon.</summary>
        static CardInstance PutOnField(DuelistState who, CardDef def)
        {
            var inst = Instance(def.id, def);
            var free = who.MonsterZones.ToList().FindIndex(z => z.Occupant == null);
            if (free < 0) return null;
            who.MonsterZones[free].Occupant = inst;
            return inst;
        }

        static CardInstance PutInHand(DuelistState who, CardDef def)
        {
            var inst = Instance(def.id, def);
            who.Hand.Add(inst);
            return inst;
        }

        /// <summary>A duel in the turn player's own Main Phase 1.</summary>
        static DuelEngine NewDuel()
        {
            var db = CardDatabase.Load();
            var engine = new DuelEngine();
            engine.StartDuel(db,
                CardDatabase.LoadDeck("lab_rules_player.json"),
                CardDatabase.LoadDeck("lab_rules_ai.json"),
                cinematicOpening: false);
            engine.Chain.Clear();
            engine.Test_SetMainPhase(engine.Player);
            // The production pool has no Synchro / Xyz printing: put the synthetic monsters in the Extra Deck.
            engine.Player.ExtraDeck.Add(Synchro6().id);
            engine.Player.ExtraDeck.Add(Xyz4().id);
            return engine;
        }

        public static string RunAll()
        {
            Register(Tuner(), Level4(), Level2(), Level8(), Synchro6(), Xyz4(), Scale2(), Scale8(), Hand4(), Hand3());

            var sb = new StringBuilder();

            // ─── Synchro ───────────────────────────────────────────────
            sb.AppendLine(SynchroLegal());
            sb.AppendLine(SynchroRejectsNoTuner());
            sb.AppendLine(SynchroRejectsWrongTotal());
            sb.AppendLine(SynchroMaterialsGoToGy());
            sb.AppendLine(SynchroNeedsFaceUpMaterials());
            sb.AppendLine(SynchroRequiresMainPhase());
            sb.AppendLine(SynchroSecondSummonRefusesDuplicateMaterial());

            // ─── Xyz ───────────────────────────────────────────────────
            sb.AppendLine(XyzLegal());
            sb.AppendLine(XyzMaterialsBecomeOverlay());
            sb.AppendLine(XyzNeedsSameLevel());
            sb.AppendLine(XyzNeedsTwoMaterials());
            sb.AppendLine(XyzMaterialNotSentToGy());

            // ─── Pendulum ──────────────────────────────────────────────
            sb.AppendLine(PendulumScalesRead());
            sb.AppendLine(PendulumSummonFromHand());
            sb.AppendLine(PendulumSummonOncePerTurn());
            sb.AppendLine(PendulumScaleRangeExclusive());
            sb.AppendLine(PendulumFieldMonsterGoesToExtraFaceUp());
            sb.AppendLine(PendulumScaleZoneOccupantGoesToExtraFaceUp());
            sb.AppendLine(PendulumRejectsNoScales());
            sb.AppendLine(PendulumNonPendulumCannotComeFromExtraDeck());

            return sb.ToString();
        }

        // ─────────────────────────── Synchro ───────────────────────────

        static string SynchroLegal()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var nonT = PutOnField(who, Level2());
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId, new List<CardInstance> { tuner, nonT });
            var inField = who.MonsterZones.Any(z => z.Occupant != null &&
                                                    z.Occupant.CardId == Synchro6().id);
            var extraRemoved = !who.ExtraDeck.Contains(Synchro6().id);
            return Check(ok && inField && extraRemoved, "Synchro: 1 Tuner + 1 Level 4 = Level 6 Synchro summons");
        }

        static string SynchroRejectsNoTuner()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level2());
            var b = PutOnField(who, Tuner());
            b.Def = Level2(); // secretly not a Tuner any more (no Tuner Type line)
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId, new List<CardInstance> { a, b });
            return Check(!ok, "Synchro: no Tuner Material is rejected");
        }

        static string SynchroRejectsWrongTotal()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var higher = PutOnField(who, Level8());
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId, new List<CardInstance> { tuner, higher });
            return Check(!ok, "Synchro: Level 4 + Level 8 ≠ Level 6 is rejected");
        }

        static string SynchroMaterialsGoToGy()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var nonT = PutOnField(who, Level2());
            var synchroId = Synchro6().id;
            engine.TrySynchroSummon(who, synchroId, new List<CardInstance> { tuner, nonT });

            var tunerInGy = who.Graveyard.Cast<CardInstance>().Count(c => c.CardId == Tuner().id);
            var nonTInGy = who.Graveyard.Cast<CardInstance>().Count(c => c.CardId == Level2().id);
            var tunerOnField = who.MonsterZones.Any(z => z.Occupant?.CardId == Tuner().id);
            return Check(tunerInGy == 1 && nonTInGy == 1 && !tunerOnField,
                "Synchro: both materials go to the Graveyard and leave the field");
        }

        static string SynchroNeedsFaceUpMaterials()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var nonT = PutOnField(who, Level2());
            who.TryFindMonster(nonT, out var zi);
            nonT.FaceUp = false; // a Set monster is not legal material
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId, new List<CardInstance> { tuner, nonT });
            return Check(!ok, "Synchro: a Set (face-down) monster cannot be Material");
        }

        static string SynchroRequiresMainPhase()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var nonT = PutOnField(who, Level2());
            engine.Test_SetMainPhase(engine.Opponent); // not our turn
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId, new List<CardInstance> { tuner, nonT });
            return Check(!ok, "Synchro: not legal on the opponent's turn");
        }

        static string SynchroSecondSummonRefusesDuplicateMaterial()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var tuner = PutOnField(who, Tuner());
            var nonT = PutOnField(who, Level2());
            var synchro = Instance(Synchro6().id, Synchro6());
            var ok = engine.TrySynchroSummon(who, synchro.CardId,
                new List<CardInstance> { tuner, nonT, nonT }); // re-used
            return Check(!ok, "Synchro: the same monster cannot be used twice");
        }

        // ─────────────────────────── Xyz ───────────────────────────

        static string XyzLegal()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level4());
            var b = PutOnField(who, Level4());
            var ok = engine.TryXyzSummon(who, Xyz4().id, new List<CardInstance> { a, b });
            var inField = who.MonsterZones.Any(z => z.Occupant?.CardId == Xyz4().id);
            return Check(ok && inField, "Xyz: two Level 4 monsters = Level 4 Rank Xyz summons");
        }

        static string XyzMaterialsBecomeOverlay()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level4());
            var b = PutOnField(who, Level4());
            engine.TryXyzSummon(who, Xyz4().id, new List<CardInstance> { a, b });

            var xyz = who.MonsterZones.Select(z => z.Occupant).First(c => c != null && c.CardId == Xyz4().id);
            var count = xyz.OverlayMaterials?.Count ?? 0;
            var anyInGy = who.Graveyard.Cast<CardInstance>().Any(c => c.CardId == Level4().id);
            var stillOnField = who.MonsterZones.Count(z => z.Occupant?.CardId == Level4().id);
            return Check(count == 2 && !anyInGy && stillOnField == 0,
                "Xyz: materials are not sent to the GY — they become Xyz Material");
        }

        static string XyzNeedsSameLevel()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level4());
            var b = PutOnField(who, Def(90100010, "Test Level 3 Minor", "Effect Monster", "Rock", 3, 800, 700));
            var ok = engine.TryXyzSummon(who, Xyz4().id, new List<CardInstance> { a, b });
            return Check(!ok, "Xyz: mixed Levels are rejected");
        }

        static string XyzNeedsTwoMaterials()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level4());
            var ok = engine.TryXyzSummon(who, Xyz4().id, new List<CardInstance> { a });
            return Check(!ok, "Xyz: one monster is not enough");
        }

        static string XyzMaterialNotSentToGy()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var a = PutOnField(who, Level4());
            var b = PutOnField(who, Level4());
            var gyBefore = who.Graveyard.Count;
            engine.TryXyzSummon(who, Xyz4().id, new List<CardInstance> { a, b });
            return Check(who.Graveyard.Count == gyBefore,
                "Xyz: the Summon adds nothing to the Graveyard");
        }

        // ─────────────────────────── Pendulum ───────────────────────────

        static bool TryPlaceBothScales(DuelEngine engine, DuelistState who, out CardInstance left, out CardInstance right)
        {
            left = PutInHand(who, Scale2());
            right = PutInHand(who, Scale8());
            var okL = engine.TryPlacePendulum(who, left, true);
            var okR = engine.TryPlacePendulum(who, right, false);
            return okL && okR;
        }

        static string PendulumScalesRead()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var placed = TryPlaceBothScales(engine, who, out _, out _);
            var lowOk = Rules.ExtraDeckProcedures.TryGetScales(who, out var low, out var high, out _);
            var read = lowOk && low == 2 && high == 8;
            return Check(placed && read, "Pendulum: both Scales are placed and read back (2 … 8)");
        }

        static string PendulumSummonFromHand()
        {
            var engine = NewDuel();
            var who = engine.Player;
            if (!TryPlaceBothScales(engine, who, out _, out _)) return Check(false, "Pendulum: scales placed");
            var hand = PutInHand(who, Hand4());

            var ok = engine.TryPendulumSummon(who, new List<CardInstance> { hand });
            var onField = who.MonsterZones.Any(z => z.Occupant?.CardId == Hand4().id);
            var outOfHand = !who.Hand.Contains(hand);
            return Check(ok && onField && outOfHand,
                "Pendulum: a Level 4 hand monster between Scales 2 and 8 is Special Summoned");
        }

        static string PendulumSummonOncePerTurn()
        {
            var engine = NewDuel();
            var who = engine.Player;
            if (!TryPlaceBothScales(engine, who, out _, out _)) return Check(false, "Pendulum: scales placed");
            var first = PutInHand(who, Hand4());
            var firstOk = engine.TryPendulumSummon(who, new List<CardInstance> { first });

            // return the first card to the hand and try again in the same turn
            var zone = who.MonsterZones.First(z => z.Occupant == first);
            zone.Occupant = null;
            who.Hand.Add(first);
            var secondOk = engine.TryPendulumSummon(who, new List<CardInstance> { first });
            return Check(firstOk && !secondOk, "Pendulum: only one Pendulum Summon per turn");
        }

        static string PendulumScaleRangeExclusive()
        {
            var engine = NewDuel();
            var who = engine.Player;
            if (!TryPlaceBothScales(engine, who, out _, out _)) return Check(false, "Pendulum: scales placed");
            var onLow = PutInHand(who, Scale2());   // prints scale 2, is Level 3 → hmm; use a dedicated low-level card
            var scaleLow = Def(90100011, "Test Level 2 Outcast", "Effect Monster", "Beast-Warrior", 2, 600, 600);
            Register(scaleLow);
            var outOfRange = PutInHand(who, scaleLow);

            var okLow = engine.TryPendulumSummon(who, new List<CardInstance> { outOfRange });
            return Check(!okLow, "Pendulum: a Level 2 monster is outside Scales 2 and 8");
        }

        static string PendulumFieldMonsterGoesToExtraFaceUp()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var pend = PutOnField(who, Scale2());
            engine.SendCardToGrave(who, pend);
            var faceUp = who.ExtraDeckFaceUp.Count(c => c.CardId == Scale2().id);
            var inGy = who.Graveyard.Cast<CardInstance>().Any(c => c.CardId == Scale2().id);
            return Check(faceUp == 1 && !inGy,
                "Pendulum: a Pendulum Monster sent from the field to the GY goes face-up to the Extra Deck");
        }

        static string PendulumScaleZoneOccupantGoesToExtraFaceUp()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var scale = PutInHand(who, Scale2());
            if (!engine.TryPlacePendulum(who, scale, true))
                return Check(false, "Pendulum: scale placement");
            engine.SendCardToGrave(who, scale);
            var faceUp = who.ExtraDeckFaceUp.Count(c => c.CardId == Scale2().id);
            var zoneFree = who.PendulumZones[0].Occupant == null;
            return Check(faceUp == 1 && zoneFree,
                "Pendulum: a Pendulum Monster destroyed in the Pendulum Zone goes face-up to the Extra Deck");
        }

        static string PendulumRejectsNoScales()
        {
            var engine = NewDuel();
            var who = engine.Player;
            var hand = PutInHand(who, Hand4());
            var ok = engine.TryPendulumSummon(who, new List<CardInstance> { hand });
            return Check(!ok, "Pendulum: no Scales means no Pendulum Summon");
        }

        static string PendulumNonPendulumCannotComeFromExtraDeck()
        {
            var engine = NewDuel();
            var who = engine.Player;
            if (!TryPlaceBothScales(engine, who, out _, out _)) return Check(false, "Pendulum: scales placed");

            // A Synchro that was never properly summoned sits face-up in the Extra Deck:
            // only Pendulum Monsters may come from there.
            var synchroFaceUp = Instance(Synchro6().id, Synchro6());
            who.ExtraDeckFaceUp.Add(synchroFaceUp);
            var ok = engine.TryPendulumSummon(who, new List<CardInstance> { synchroFaceUp });
            return Check(!ok, "Pendulum: a non-Pendulum face-up Extra Deck card cannot be Pendulum Summoned");
        }

        static string Check(bool cond, string label)
        {
            return (cond ? "PASS  " : "FAIL  ") + label + "\n";
        }
    }
}
