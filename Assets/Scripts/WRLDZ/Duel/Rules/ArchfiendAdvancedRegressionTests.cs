using WRLDZ.Data;
using System.Text;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// The rest of the Archfiend toolkit: die-roll targeting protection (roll a d6 as an
    /// opponent's targeting effect resolves; on a negate face negate it and destroy the
    /// opponent's card), Pandemonium's search-on-destruction, Battle-Scarred's mirrored
    /// maintenance + destroy-links, Archfiend's Roar (temporary GY revive), and Falling
    /// Down's take-control + Standby burn. Deterministic die results are queued via
    /// <see cref="DuelRng.QueueDie"/>.
    /// </summary>
    public static class ArchfiendAdvancedRegressionTests
    {
        const int Vilepawn = 73219648;      // die faces {3}; Level 2
        const int Darkbishop = 35798491;    // die faces {1,3,6}; protects all Archfiends
        const int Terrorking = 35975813;    // die faces {2,5}; pays 800; Level 4
        const int SkullArchfiend = 61370518;// die faces {1,3,6}
        const int ArchfiendSoldier = 49881766; // vanilla Archfiend; Level 4
        const int SummonedSkull = 70781052; // treated-as Archfiend; Level 6
        const int Celtic = 91152256;        // vanilla, non-Archfiend
        const int Offerings = 19230407;     // Quick-Play: target 1 face-up monster; destroy it
        const int BattleScarred = 94463200;
        const int ArchfiendsRoar = 56246017;
        const int FallingDown = 32919136;
        const int Pandemonium = 94585852;

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

            // ─────────────────────────────────────────────────────────────────────
            // Die-roll targeting negation
            // ─────────────────────────────────────────────────────────────────────

            // Direct guard: self-protecting Vilepawn negates on face 3, not on face 4.
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var vilepawn = PlaceMonster(engine, me, Vilepawn, 0);
                var src = PlaceSpellTrap(engine, opp, Offerings, 0);
                engine.Rng.QueueDie(3);
                var negated = ArchfiendTargetNegation.TryNegate(engine, opp, src, vilepawn);
                Check("Die-roll: Vilepawn negates on face 3 and destroys the source",
                    negated && opp.Graveyard.Contains(src) && OnField(me, vilepawn));
            }
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var vilepawn = PlaceMonster(engine, me, Vilepawn, 0);
                var src = PlaceSpellTrap(engine, opp, Offerings, 0);
                engine.Rng.QueueDie(4);
                var negated = ArchfiendTargetNegation.TryNegate(engine, opp, src, vilepawn);
                Check("Die-roll: Vilepawn does not negate on face 4 (source survives)",
                    !negated && !opp.Graveyard.Contains(src));
            }

            // Skull Archfiend negates on any of {1,3,6}.
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var skull = PlaceMonster(engine, me, SkullArchfiend, 0);
                var src = PlaceSpellTrap(engine, opp, Offerings, 0);
                engine.Rng.QueueDie(6);
                Check("Die-roll: Skull Archfiend negates on face 6",
                    ArchfiendTargetNegation.TryNegate(engine, opp, src, skull));
            }

            // Darkbishop protects ANY Archfiend you control (not just itself)…
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                PlaceMonster(engine, me, Darkbishop, 0);
                var soldier = PlaceMonster(engine, me, ArchfiendSoldier, 1);
                var src = PlaceSpellTrap(engine, opp, Offerings, 0);
                engine.Rng.QueueDie(1);
                Check("Die-roll: Darkbishop protects another Archfiend (face 1)",
                    ArchfiendTargetNegation.TryNegate(engine, opp, src, soldier));
            }
            // …but not a non-Archfiend you control.
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                PlaceMonster(engine, me, Darkbishop, 0);
                var celtic = PlaceMonster(engine, me, Celtic, 1);
                var src = PlaceSpellTrap(engine, opp, Offerings, 0);
                engine.Rng.QueueDie(1);
                Check("Die-roll: Darkbishop does not protect a non-Archfiend",
                    !ArchfiendTargetNegation.TryNegate(engine, opp, src, celtic));
            }

            // Your own targeting of your Archfiend never triggers the protection.
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var vilepawn = PlaceMonster(engine, me, Vilepawn, 0);
                var src = PlaceSpellTrap(engine, me, Offerings, 0);
                engine.Rng.QueueDie(3);
                Check("Die-roll: your own effect does not trigger the protection",
                    !ArchfiendTargetNegation.TryNegate(engine, me, src, vilepawn));
            }

            // Integration: opponent's "Offerings to the Doomed" resolves through the real
            // activation pipeline; the die-roll guard fires inside ApplyClause.
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var vilepawn = PlaceMonster(engine, me, Vilepawn, 0);
                var offerings = engine.CreateCardInstance(Offerings);
                opp.Hand.Add(offerings);
                var prog = CompiledEffectCache.GetOrCompile(offerings.Def);
                engine.Rng.QueueDie(3);
                TextEffectRuntime.TryResolveActivation(engine, opp, offerings, true, prog, true);
                Check("Die-roll integration: Offerings negated (face 3) — Vilepawn survives, Offerings gone",
                    OnField(me, vilepawn) && !OnField(opp, offerings) && opp.Graveyard.Contains(offerings));
            }
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var vilepawn = PlaceMonster(engine, me, Vilepawn, 0);
                var offerings = engine.CreateCardInstance(Offerings);
                opp.Hand.Add(offerings);
                var prog = CompiledEffectCache.GetOrCompile(offerings.Def);
                engine.Rng.QueueDie(4);
                TextEffectRuntime.TryResolveActivation(engine, opp, offerings, true, prog, true);
                Check("Die-roll integration: Offerings resolves (face 4) — Vilepawn destroyed",
                    !OnField(me, vilepawn) && me.Graveyard.Contains(vilepawn));
            }

            // ─────────────────────────────────────────────────────────────────────
            // Battle-Scarred: opponent mirrors the maintenance; destroy-links both ways
            // ─────────────────────────────────────────────────────────────────────
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                me.LifePoints = 8000; opp.LifePoints = 8000;
                var terror = PlaceMonster(engine, me, Terrorking, 0);
                LinkBattleScarred(engine, me, terror, out _);
                TextEffectRuntime.FirePhaseTriggers(engine, me, EffectTiming.StandbyPhase);
                Check("Battle-Scarred: opponent mirrors Terrorking's 800 upkeep",
                    me.LifePoints == 7200 && opp.LifePoints == 7200);
            }
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var terror = PlaceMonster(engine, me, Terrorking, 0);
                LinkBattleScarred(engine, me, terror, out var scarred);
                engine.SendCardToGrave(me, scarred);
                Check("Battle-Scarred: removing the trap destroys the selected monster",
                    !OnField(me, terror) && me.Graveyard.Contains(terror));
            }
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                var terror = PlaceMonster(engine, me, Terrorking, 0);
                LinkBattleScarred(engine, me, terror, out var scarred);
                engine.DestroyMonsterPublic(me, terror);
                Check("Battle-Scarred: removing the monster destroys the trap",
                    me.Graveyard.Contains(scarred));
            }

            // ─────────────────────────────────────────────────────────────────────
            // Archfiend's Roar: pay 500, revive an Archfiend, cannot Tribute, End-Phase destroy
            // ─────────────────────────────────────────────────────────────────────
            {
                var engine = Fresh(db);
                var me = engine.Player;
                ClearField(me); ClearField(engine.Opponent);
                me.LifePoints = 8000;
                var soldier = engine.CreateCardInstance(ArchfiendSoldier);
                me.Graveyard.Add(soldier);
                var roar = engine.CreateCardInstance(ArchfiendsRoar);
                me.SpellTrapZones[0].Occupant = roar; roar.FaceUp = false; roar.SetThisTurn = false;
                var prog = CompiledEffectCache.GetOrCompile(roar.Def);
                var summonTurn = engine.TurnNumber;
                // Pay-cost then target: activate, then pick the GY target (player picks).
                TextEffectRuntime.TryResolveActivation(engine, me, roar, false, prog, true);
                if (engine.IsAwaitingEffectTarget)
                    TextEffectRuntime.TryResolveTextTarget(engine, soldier);
                Check("Archfiend's Roar: pays 500 and Special Summons the target from the GY",
                    me.LifePoints == 7500 && OnField(me, soldier) && !me.Graveyard.Contains(soldier));
                Check("Archfiend's Roar: the revived monster cannot be Tributed",
                    soldier.CannotBeTributedForSummon);
                Check("Archfiend's Roar: flagged for End-Phase destruction this turn",
                    soldier.TempDestroyOnEndOfTurn == summonTurn);
                if (engine.TurnPlayer == me && !engine.GameOver)
                    engine.EndTurn(me);
                Check("Archfiend's Roar: revived monster is destroyed in the End Phase",
                    !OnField(me, soldier) && me.Graveyard.Contains(soldier));
            }

            // ─────────────────────────────────────────────────────────────────────
            // Pandemonium: search a lower-Level Archfiend when one is destroyed (not by battle)
            // ─────────────────────────────────────────────────────────────────────
            {
                var engine = Fresh(db);
                var me = engine.Player;
                ClearField(me); ClearField(engine.Opponent);
                PlaceFieldSpell(engine, me, Pandemonium);
                var terror = PlaceMonster(engine, me, Terrorking, 0); // Level 4
                me.Deck.Insert(0, Vilepawn); // Level 2 Archfiend
                var handBefore = me.Hand.Count;
                engine.DestroyMonsterPublic(me, terror);
                Check("Pandemonium: destroying an Archfiend adds a lower-Level Archfiend from Deck",
                    me.Hand.Count == handBefore + 1 &&
                    me.Hand.Exists(c => c != null && c.CardId == Vilepawn) &&
                    !me.Deck.Contains(Vilepawn));
            }
            {
                // No Pandemonium → no search.
                var engine = Fresh(db);
                var me = engine.Player;
                ClearField(me); ClearField(engine.Opponent);
                me.Deck.Insert(0, Vilepawn);
                var handBefore = me.Hand.Count;
                FieldSpellEffects.TryPandemoniumSearchOnDestroy(engine, me,
                    engine.CreateCardInstance(Terrorking), byDestruction: true, destroyedByBattle: false);
                Check("Pandemonium: no search without a face-up Pandemonium", me.Hand.Count == handBefore);
            }
            {
                // Destroyed by battle → excluded.
                var engine = Fresh(db);
                var me = engine.Player;
                ClearField(me); ClearField(engine.Opponent);
                PlaceFieldSpell(engine, me, Pandemonium);
                me.Deck.Insert(0, Vilepawn);
                var handBefore = me.Hand.Count;
                FieldSpellEffects.TryPandemoniumSearchOnDestroy(engine, me,
                    engine.CreateCardInstance(Terrorking), byDestruction: true, destroyedByBattle: true);
                Check("Pandemonium: battle destruction does not trigger the search",
                    me.Hand.Count == handBefore);
            }

            // ─────────────────────────────────────────────────────────────────────
            // Falling Down: take control of the target + 800 burn each opponent Standby
            // ─────────────────────────────────────────────────────────────────────
            {
                var engine = Fresh(db);
                var me = engine.Player; var opp = engine.Opponent;
                ClearField(me); ClearField(opp);
                me.LifePoints = 8000;
                PlaceMonster(engine, me, ArchfiendSoldier, 0); // keeps Falling Down alive
                var victim = PlaceMonster(engine, opp, Celtic, 0);
                var falling = engine.CreateCardInstance(FallingDown);
                me.Hand.Add(falling);
                var prog = CompiledEffectCache.GetOrCompile(falling.Def);
                TextEffectRuntime.TryResolveActivation(engine, me, falling, true, prog, true);
                Check("Falling Down: takes control of the opponent's monster",
                    engine.ControllerOf(victim) == me && OnField(me, victim));
                TextEffectRuntime.FirePhaseTriggers(engine, opp, EffectTiming.StandbyPhase);
                Check("Falling Down: controller takes 800 during the opponent's Standby",
                    me.LifePoints == 7200);
            }

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

        static void ClearField(DuelistState who)
        {
            for (var i = 0; i < who.MonsterZones.Length; i++) who.MonsterZones[i].Occupant = null;
            for (var i = 0; i < who.SpellTrapZones.Length; i++) who.SpellTrapZones[i].Occupant = null;
            if (who.FieldSpellZone != null) who.FieldSpellZone.Occupant = null;
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
            if (who.FieldSpellZone != null) who.FieldSpellZone.Occupant = c;
            return c;
        }

        // Establish the Battle-Scarred link exactly as its EquipThisToTarget clause would.
        static void LinkBattleScarred(DuelEngine engine, DuelistState who, CardInstance monster,
            out CardInstance scarred)
        {
            scarred = engine.CreateCardInstance(BattleScarred);
            scarred.FaceUp = true;
            scarred.SetThisTurn = false;
            who.SpellTrapZones[0].Occupant = scarred;
            scarred.EquippedTo = monster;
            if (!monster.Equips.Contains(scarred)) monster.Equips.Add(scarred);
        }

        static bool OnField(DuelistState who, CardInstance card)
        {
            foreach (var z in who.MonsterZones) if (z.Occupant == card) return true;
            foreach (var z in who.SpellTrapZones) if (z.Occupant == card) return true;
            return false;
        }
    }
}
