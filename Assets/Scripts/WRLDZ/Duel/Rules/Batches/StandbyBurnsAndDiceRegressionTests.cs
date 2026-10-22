using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// StandbyBurnsAndDice batch (v162): Blind Destruction, Needle Wall, Graverobber's Retribution, Dark Snake Syndrome,
    /// Mask of Dispel, Nightmare Wheel, The Unfriendly Amazon and Inspection. Compile shapes, mini-duels through the real
    /// engine entry points (TryActivateSpellTrap, EndTurn through the Draw / Standby Phases, TrySelectEffectTarget /
    /// CancelEffectTargeting / TrySelectLpCost, ReturnCardToHand, SendCardToGrave) with queued dice, and near misses.
    /// Runtime: <see cref="StandbyBurnsAndDiceRuntime"/>.
    /// </summary>
    public static class StandbyBurnsAndDiceRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BlindDestruction = 32015116;
        const int NeedleWall = 38299233;
        const int Graverobber = 33737664;
        const int DarkSnake = 47233801;
        const int MaskOfDispel = 20765952;
        const int NightmareWheel = 54704216;
        const int UnfriendlyAmazon = 65475294;
        const int Inspection = 16227556;

        const int RegulationOfTribe = 296499;
        const int SpellbindingCircle = 18807108;
        const int ShadowSpell = 29267084;
        const int DarkHole = 53129443;

        const int Celtic = 91152256;        // Lv4 1400/1200
        const int DarkBlade = 11321183;     // Lv4 1800/1500
        const int FeralImp = 41392891;      // Lv4 1300/1400
        const int GiantSoldier = 13039848;  // Lv3 1300/2000
        const int RudeKaiser = 26378150;    // Lv5 1800/1600
        const int SummonedSkull = 70781052; // Lv6 2500/1200
        const int Gaia = 6368038;           // Lv7 2300/2100
        const int Banner = 10012614;        // Continuous Spell (Banner of Courage)
        const int Yami = 59197169;          // Field Spell
        const int Mst = 5318639;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("StandbyBurnsAndDice: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(db, Check);
            BlindDestructionDuels(db, pDeck, aDeck, Check);
            NeedleWallDuels(db, pDeck, aDeck, Check);
            GraverobberDuels(db, pDeck, aDeck, Check);
            DarkSnakeDuels(db, pDeck, aDeck, Check);
            MaskDuels(db, pDeck, aDeck, Check);
            WheelDuels(db, pDeck, aDeck, Check);
            AmazonDuels(db, pDeck, aDeck, Check);
            InspectionDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [{string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef { id = 990000162, name = "Probe", type = type, race = race, desc = text });

        static CardEffectStatusKind Status(CardDatabase db, int id) => CardEffectStatus.Classify(db.Get(id));

        /// <summary>The player's Main Phase 1 on turn 3+, boards and hands empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        /// <summary>From the player's turn to the player's next Standby Phase (and Main Phase 1 unless it holds).</summary>
        static void ToPlayersNextTurn(DuelEngine engine)
        {
            EndTurn(engine);
            EndTurn(engine);
        }

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static CardInstance FaceUpSt(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CardInstance Banished(DuelEngine engine, DuelistState pile, int id, bool faceUp)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = faceUp;
            pile.Banished.Add(c);
            return c;
        }

        static bool OnField(DuelistState who, CardInstance c) =>
            who.TryFindMonster(c, out _) || who.TryFindSpellTrap(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            foreach (var (id, label, action, timing) in new[]
                     {
                         (BlindDestruction, "Blind Destruction", EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel,
                             EffectTiming.StandbyPhase),
                         (NeedleWall, "Needle Wall", EffectActionKind.RollDieDestroyOppMonsterInRolledZone,
                             EffectTiming.StandbyPhase),
                         (DarkSnake, "Dark Snake Syndrome", EffectActionKind.InflictDoublingDamageToBothPlayers,
                             EffectTiming.StandbyPhase),
                     })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                var c = prog?.ClauseList.FirstOrDefault();
                Check($"{label}: one mandatory Standby clause ({action}), Implemented",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && c.Timing == timing &&
                    c.Action == action && c.MakesChainLink && !c.OpponentTurnOnly && !c.IsOptional &&
                    Status(db, id) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(DarkSnake));
                Check("Dark Snake Syndrome: starts at 200", prog?.ClauseList.FirstOrDefault()?.Amount == 200, Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Graverobber));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Graverobber's Retribution: Standby burn 100 x the opponent's face-up banished monsters, Implemented",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.InflictDamageToOpponent &&
                    c.Amount == 100 && c.CountSource == PerCountBurnsRuntime.OppBanishedFaceUpMonsters &&
                    Status(db, Graverobber) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(MaskOfDispel));
                var act = prog?.ClauseList.Find(c => c.Timing == EffectTiming.Activate);
                var burn = prog?.ClauseList.Find(c => c.Timing == EffectTiming.StandbyPhase);
                var leave = prog?.ClauseList.Find(c => c.Timing == EffectTiming.ContinuousWhileFaceUp);
                Check("Mask of Dispel: target a face-up Spell (link) + Standby 500 to its controller + leave-field destroy",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 3 &&
                    act?.Action == EffectActionKind.LinkThisToTargetSpell && act.Zone == EffectZoneFilter.FieldFaceUpSpells &&
                    act.RequiresTargetChoice && act.IsPsctTarget && act.RequiresTargetFaceUp &&
                    burn?.Action == EffectActionKind.InflictDamageToLinkedSpellController && burn.Amount == 500 &&
                    !burn.OpponentTurnOnly &&
                    leave?.Action == EffectActionKind.DestroyThisWhenLinkedSpellLeavesField && !leave.MakesChainLink &&
                    Status(db, MaskOfDispel) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(NightmareWheel));
                var act = prog?.ClauseList.Find(c => c.Timing == EffectTiming.Activate);
                var burns = prog?.ClauseList.FindAll(c => c.Timing == EffectTiming.StandbyPhase);
                Check("Nightmare Wheel: bind 1 opponent's monster (any position), destroyed when it leaves, Standby 500 " +
                      "while it is on the field — no unconditional burn",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 3 &&
                    act?.Action == EffectActionKind.ContinuousCannotAttack && act.Zone == EffectZoneFilter.OppMonsters &&
                    act.RequiresTargetChoice && act.DestroyThisWhenBoundHostLeaves && act.AlsoCannotChangeBattlePosition &&
                    !act.DestroyThisWhenBoundHostDestroyed && !act.RequiresTargetFaceUp &&
                    burns.Count == 1 &&
                    burns[0].Action == EffectActionKind.InflictDamageToOpponentWhileBoundMonsterOnField &&
                    burns[0].Amount == 500 &&
                    Status(db, NightmareWheel) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(UnfriendlyAmazon));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("The Unfriendly Amazon: mandatory Standby Tribute of another monster, else destroyed (no Chain Link)",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.StandbyTributeOrDestroyThis &&
                    c.TributeExceptThis && c.UpkeepTributeMandatory && !c.MakesChainLink &&
                    Status(db, UnfriendlyAmazon) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }

            {
                var prog = CardTextEffectCompiler.Compile(db.Get(Inspection));
                var c = prog?.ClauseList.FirstOrDefault();
                Check("Inspection: optional trigger in the opponent's Standby, 500 LP cost, random look; no LP paid on activation",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.PayLpLookAtRandomOppHandCard &&
                    c.OpponentTurnOnly && c.IsOptional && c.Amount == 500 && c.PayLpAmount == 0 &&
                    Status(db, Inspection) == CardEffectStatusKind.Implemented,
                    Describe(prog));
            }
        }

        // ───────────────────────────── near misses ─────────────────────────────

        static void NearMisses(CardDatabase db, Chk Check)
        {
            const string trap = "Trap Card";
            const string spell = "Spell Card";
            const string cont = "Continuous";

            Check("Near miss: a die roll without the Level 6+ sentence is not Blind Destruction",
                !CompileText(trap, cont, "Once per turn, during your Standby Phase: Roll a six-sided die. If the result " +
                                         "is 1-5, destroy all monsters on the field with the same Level as the number " +
                                         "rolled.").FullyCompiled);
            Check("Near miss: Needle Wall without its \"roll again\" sentence is not compiled",
                !CompileText(trap, cont, "Once per turn, during your Standby Phase: Roll a six-sided die. Treat your " +
                                         "opponent's Main Monster Zones as numbers 1-5, counting from your right, and " +
                                         "destroy the monster that is in the same Main Monster Zone as the result.")
                    .FullyCompiled);
            {
                var dd = CompileText(trap, cont, "During each of your Standby Phases, inflict 100 points of damage to " +
                                                 "your opponent's Life Points for each card your opponent has removed " +
                                                 "from play.");
                Check("Near miss: a Standby burn per banished CARD is not the face-up monster count",
                    !dd.ClauseList.Exists(c => c.CountSource == PerCountBurnsRuntime.OppBanishedFaceUpMonsters),
                    Describe(dd));
            }
            Check("Near miss: a both-players Standby burn that does not double is not Dark Snake Syndrome",
                !CompileText(spell, cont, "During each of your Standby Phases: Inflict 200 damage to both players.")
                    .ClauseList.Exists(c => c.Action == EffectActionKind.InflictDoublingDamageToBothPlayers));
            Check("Near miss: Mask of Dispel's text without its leave-field sentence is not FullyCompiled",
                !CompileText(spell, cont, "Select 1 face-up Spell Card on the field. The controller of the Spell Card " +
                                          "takes 500 points of damage during each of your Standby Phases.").FullyCompiled);
            {
                var miss = CompileText(trap, cont, "Activate this card by targeting 1 monster your opponent controls; it " +
                                                   "cannot attack or change its battle position. When it leaves the field, " +
                                                   "destroy this card. Once per turn, during your Standby Phase: Inflict " +
                                                   "500 damage to your opponent.");
                Check("Near miss: Nightmare Wheel without \"That monster must be on the field\" is not compiled",
                    !miss.FullyCompiled &&
                    !miss.ClauseList.Exists(c => c.Action == EffectActionKind.InflictDamageToOpponentWhileBoundMonsterOnField),
                    Describe(miss));
            }
            Check("Near miss: Inspection in your own Standby Phase is not this card",
                !CompileText(spell, cont, "During your Standby Phase, you can pay 500 Life Points to randomly select 1 " +
                                          "card in your opponent's hand and look at it.")
                    .ClauseList.Exists(c => c.Action == EffectActionKind.PayLpLookAtRandomOppHandCard));

            {
                var reg = CardTextEffectCompiler.Compile(db.Get(RegulationOfTribe));
                var up = reg?.ClauseList.Find(c => c.Action == EffectActionKind.StandbyTributeOrDestroyThis);
                Check("Near miss: The Regulation of Tribe's upkeep stays optional and may Tribute any monster",
                    reg != null && reg.FullyCompiled && up != null && !up.TributeExceptThis && !up.UpkeepTributeMandatory,
                    Describe(reg));
            }
            {
                var sb = CardTextEffectCompiler.Compile(db.Get(SpellbindingCircle));
                Check("Near miss: Spellbinding Circle still leaves only when its monster is destroyed",
                    sb != null && sb.FullyCompiled &&
                    sb.ClauseList.Exists(c => c.DestroyThisWhenBoundHostDestroyed) &&
                    !sb.ClauseList.Exists(c => c.DestroyThisWhenBoundHostLeaves),
                    Describe(sb));
                var shadow = CardTextEffectCompiler.Compile(db.Get(ShadowSpell));
                Check("Shadow Spell is now compiled (loses 700 ATK, cannot attack or change position)",
                    shadow != null && shadow.FullyCompiled,
                    Describe(shadow));
            }
            {
                var hole = CardTextEffectCompiler.Compile(db.Get(DarkHole));
                Check("Near miss: Dark Hole still destroys all monsters on the field",
                    hole != null && hole.FullyCompiled &&
                    hole.ClauseList.Exists(c => c.Action == EffectActionKind.Destroy && c.Zone == EffectZoneFilter.FieldMonsters),
                    Describe(hole));
                var bd = CardTextEffectCompiler.Compile(db.Get(BlindDestruction));
                Check("Near miss: Blind Destruction has no Dark Hole clause",
                    bd != null && !bd.ClauseList.Exists(c => c.Action == EffectActionKind.Destroy), Describe(bd));
            }
        }

        // ───────────────────────────── Blind Destruction ─────────────────────────────

        static void BlindDestructionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bd = PlaceSetTrap(engine, p, BlindDestruction, 0);
                var activated = engine.TryActivateSpellTrap(p, bd, fromHand: false);
                Settle(engine);
                var placed = bd.FaceUp && p.TryFindSpellTrap(bd, out _);
                var mine = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(engine, o, DarkBlade, 0, BattlePosition.Attack, true);
                var set4 = PlaceMonster(engine, o, FeralImp, 1, BattlePosition.Defense, false);
                var lv3 = PlaceMonster(engine, o, GiantSoldier, 2, BattlePosition.Attack, true);
                EndTurn(engine);
                var oppTurnKept = OnField(p, mine) && OnField(o, theirs);
                engine.Rng.QueueDie(4);
                EndTurn(engine);
                Check("Blind Destruction: activated from the Set; a 4 in your Standby destroys every face-up Level 4 " +
                      "(both sides), not the face-down one or the Level 3",
                    reached && activated && placed && oppTurnKept && engine.TurnPlayer == p &&
                    !OnField(p, mine) && p.Graveyard.Contains(mine) && !OnField(o, theirs) && o.Graveyard.Contains(theirs) &&
                    OnField(o, set4) && OnField(o, lv3) && OnField(p, bd),
                    $"reached={reached} act={activated} placed={placed} oppTurnKept={oppTurnKept} mine={OnField(p, mine)} " +
                    $"theirs={OnField(o, theirs)} set4={OnField(o, set4)} lv3={OnField(o, lv3)}");

                var gaia = PlaceMonster(engine, p, Gaia, 1, BattlePosition.Attack, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 3, BattlePosition.Attack, true);
                var kaiser = PlaceMonster(engine, o, RudeKaiser, 4, BattlePosition.Attack, true);
                engine.Rng.QueueDie(6);
                ToPlayersNextTurn(engine);
                Check("Blind Destruction: a 6 destroys every face-up Level 6 or higher monster, not a Level 5",
                    engine.TurnPlayer == p && !OnField(p, gaia) && !OnField(o, skull) && OnField(o, kaiser),
                    $"gaia={OnField(p, gaia)} skull={OnField(o, skull)} kaiser={OnField(o, kaiser)}");
            }
        }

        // ───────────────────────────── Needle Wall ─────────────────────────────

        static void NeedleWallDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var wall = PlaceSetTrap(engine, p, NeedleWall, 0);
            var activated = engine.TryActivateSpellTrap(p, wall, fromHand: false);
            Settle(engine);
            var zone1 = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
            var zone2 = PlaceMonster(engine, o, FeralImp, 1, BattlePosition.Defense, false);
            var mine = PlaceMonster(engine, p, DarkBlade, 1, BattlePosition.Attack, true);
            EndTurn(engine);
            engine.Rng.QueueDie(6);
            engine.Rng.QueueDie(2);
            EndTurn(engine);
            Check("Needle Wall: a 6 is rolled again; a 2 destroys the (face-down) monster in the opponent's zone 2 " +
                  "(sequence 1), not zone 1 or your own",
                reached && activated && engine.TurnPlayer == p && OnField(o, zone1) && !OnField(o, zone2) &&
                o.Graveyard.Contains(zone2) && OnField(p, mine),
                $"act={activated} zone1={OnField(o, zone1)} zone2={OnField(o, zone2)} mine={OnField(p, mine)}");

            var gyBefore = o.Graveyard.Count;
            engine.Rng.QueueDie(4);
            ToPlayersNextTurn(engine);
            Check("Needle Wall: an empty zone (4) destroys nothing",
                engine.TurnPlayer == p && OnField(o, zone1) && o.Graveyard.Count == gyBefore,
                $"zone1={OnField(o, zone1)} gy {gyBefore}->{o.Graveyard.Count}");
        }

        // ───────────────────────────── Graverobber's Retribution ─────────────────────────────

        static void GraverobberDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var grave = PlaceSetTrap(engine, p, Graverobber, 0);
            var activated = engine.TryActivateSpellTrap(p, grave, fromHand: false);
            Settle(engine);
            Banished(engine, o, Celtic, true);
            Banished(engine, o, DarkBlade, true);
            Banished(engine, o, FeralImp, false); // face-down: not counted
            Banished(engine, o, Mst, true);       // a Spell: not counted
            Banished(engine, p, Celtic, true);    // yours: not counted
            EndTurn(engine);
            var oppLp = o.LifePoints;
            var myLp = p.LifePoints;
            EndTurn(engine);
            Check("Graverobber's Retribution: 100 x 2 face-up monsters in the opponent's banished pile (face-down, Spells " +
                  "and yours do not count)",
                reached && activated && engine.TurnPlayer == p && oppLp - o.LifePoints == 200 && p.LifePoints == myLp,
                $"act={activated} dealt={oppLp - o.LifePoints} mine={myLp - p.LifePoints}");

            o.Banished.Clear();
            var lp2 = o.LifePoints;
            ToPlayersNextTurn(engine);
            Check("Graverobber's Retribution: no banished monster → no damage",
                engine.TurnPlayer == p && o.LifePoints == lp2, $"lp {lp2}->{o.LifePoints}");
        }

        // ───────────────────────────── Dark Snake Syndrome ─────────────────────────────

        static void DarkSnakeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var snake = PutInHand(engine, p, DarkSnake);
            var activated = engine.TryActivateSpellTrap(p, snake, true);
            Settle(engine);
            var dealt = new List<(int mine, int theirs)>();
            for (var i = 0; i < 3; i++)
            {
                EndTurn(engine);
                var (a, b) = (p.LifePoints, o.LifePoints);
                EndTurn(engine);
                dealt.Add((a - p.LifePoints, b - o.LifePoints));
            }

            Check("Dark Snake Syndrome: both players take 200, then 400, then 800 in your Standby Phases (not the opponent's)",
                reached && activated && dealt.Count == 3 && dealt[0] == (200, 200) && dealt[1] == (400, 400) &&
                dealt[2] == (800, 800),
                $"act={activated} dealt={string.Join(",", dealt)}");

            // It leaves the field and comes back: the amount starts over (Lua flag RESETS_STANDARD).
            engine.SendCardToGrave(p, snake);
            engine.NotifyPublic();
            var reset = StandbyBurnsAndDiceRuntime.DarkSnakeLastDamage(engine, snake) == 0;
            p.Graveyard.Remove(snake);
            snake.FaceUp = true;
            p.SpellTrapZones[1].Occupant = snake;
            EndTurn(engine);
            var lp = p.LifePoints;
            EndTurn(engine);
            Check("Dark Snake Syndrome: after it left the field the damage starts at 200 again",
                reset && lp - p.LifePoints == 200, $"reset={reset} dealt={lp - p.LifePoints}");
        }

        // ───────────────────────────── Mask of Dispel ─────────────────────────────

        static void MaskDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var mask = PutInHand(engine, p, MaskOfDispel);
                Check("Mask of Dispel: cannot be activated with no other face-up Spell on the field",
                    reached && !engine.CanActivateSpellTrap(p, mask, true));
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var banner = FaceUpSt(engine, o, Banner, 0);
                var yami = engine.CreateCardInstance(Yami);
                yami.FaceUp = true;
                p.FieldSpellZone.Occupant = yami;
                var setSpell = PlaceSetTrap(engine, o, Mst, 1); // face-down: not a legal target
                var mask = PutInHand(engine, p, MaskOfDispel);
                var can = engine.CanActivateSpellTrap(p, mask, true);
                var used = engine.TryActivateSpellTrap(p, mask, true);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(banner);
                Settle(engine);
                var linked = StandbyBurnsAndDiceRuntime.LinkedSpellOf(engine, mask);
                Check("Mask of Dispel: targets a face-up Spell on either field (Field Zone included), not itself or a Set card",
                    reached && can && used && legal.Contains(banner) && legal.Contains(yami) && !legal.Contains(mask) &&
                    !legal.Contains(setSpell) && picked && linked == banner && mask.FaceUp && p.TryFindSpellTrap(mask, out _),
                    $"can={can} used={used} legal=[{string.Join(",", legal.Select(l => l.Name))}] picked={picked} " +
                    $"linked={linked?.Name}");

                EndTurn(engine);
                var (myLp, oppLp) = (p.LifePoints, o.LifePoints);
                EndTurn(engine);
                Check("Mask of Dispel: in your Standby Phase the linked Spell's controller takes 500",
                    engine.TurnPlayer == p && oppLp - o.LifePoints == 500 && myLp == p.LifePoints,
                    $"opp={oppLp - o.LifePoints} me={myLp - p.LifePoints}");

                engine.SendCardToGrave(o, banner);
                engine.NotifyPublic();
                Check("Mask of Dispel: destroyed when the selected Spell leaves the field",
                    !p.TryFindSpellTrap(mask, out _) && p.Graveyard.Contains(mask) &&
                    StandbyBurnsAndDiceRuntime.LinkedSpellOf(engine, mask) == null,
                    $"onField={p.TryFindSpellTrap(mask, out _)} inGy={p.Graveyard.Contains(mask)}");
            }

            {
                // Your own linked Spell burns you; the AI takes the opponent's Spell when both exist.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var mine = FaceUpSt(engine, p, Banner, 1);
                var mask = PutInHand(engine, p, MaskOfDispel);
                var used = engine.TryActivateSpellTrap(p, mask, true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(mine);
                Settle(engine);
                EndTurn(engine);
                var (myLp, oppLp) = (p.LifePoints, o.LifePoints);
                EndTurn(engine);
                Check("Mask of Dispel: a Spell you control → you take the 500",
                    reached && used && StandbyBurnsAndDiceRuntime.LinkedSpellOf(engine, mask) == mine &&
                    myLp - p.LifePoints == 500 && oppLp == o.LifePoints,
                    $"used={used} me={myLp - p.LifePoints} opp={oppLp - o.LifePoints}");

                var theirs = FaceUpSt(engine, o, Banner, 2);
                var pick = StandbyBurnsAndDiceRuntime.AutoPickSpell(engine, o, new List<CardInstance> { theirs, mine });
                Check("Mask of Dispel AI: picks the other player's Spell first", pick == mine, pick?.Name);
            }
        }

        // ───────────────────────────── Nightmare Wheel ─────────────────────────────

        static void WheelDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(engine, o, FeralImp, 1, BattlePosition.Defense, false);
                var wheel = PlaceSetTrap(engine, p, NightmareWheel, 0);
                var used = engine.TryActivateSpellTrap(p, wheel, fromHand: false);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                Settle(engine);
                Check("Nightmare Wheel: targets a monster the opponent controls (face-down included) and binds it",
                    reached && used && legal.Contains(celtic) && legal.Contains(set) && wheel.EquippedTo == celtic &&
                    wheel.FaceUp,
                    $"used={used} legal=[{string.Join(",", legal.Select(l => l.Name))}] bound={wheel.EquippedTo?.Name}");

                EndTurn(engine);
                var lockedAtk = engine.ContinuousCannotAttackBlocks(o, celtic);
                var lockedPos = engine.IsBattlePositionLocked(celtic);
                var freeOther = !engine.ContinuousCannotAttackBlocks(o, set);
                var oppLp = o.LifePoints;
                EndTurn(engine);
                Check("Nightmare Wheel: the bound monster cannot attack or change position; 500 to the opponent in " +
                      "your Standby Phase",
                    lockedAtk && lockedPos && freeOther && engine.TurnPlayer == p && oppLp - o.LifePoints == 500,
                    $"atkLock={lockedAtk} posLock={lockedPos} freeOther={freeOther} dealt={oppLp - o.LifePoints}");

                var bounced = engine.ReturnCardToHand(celtic);
                engine.NotifyPublic();
                Check("Nightmare Wheel: the bound monster returned to the hand (not destroyed) → this card is sent away",
                    bounced && o.Hand.Contains(celtic) && !p.TryFindSpellTrap(wheel, out _) && p.Graveyard.Contains(wheel),
                    $"bounced={bounced} wheelOnField={p.TryFindSpellTrap(wheel, out _)}");
            }

            {
                // Spellbinding Circle (destroyed-only) stays when its monster is returned to the hand.
                var engine = PlayerMain1(db, pDeck, aDeck, out _);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                var circle = PlaceSetTrap(engine, p, SpellbindingCircle, 0);
                engine.TryActivateSpellTrap(p, circle, fromHand: false);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(celtic);
                Settle(engine);
                var bound = circle.EquippedTo == celtic;
                engine.ReturnCardToHand(celtic);
                engine.NotifyPublic();
                Check("Near miss: Spellbinding Circle stays face-up when its monster is returned to the hand",
                    bound && p.TryFindSpellTrap(circle, out _), $"bound={bound}");
            }
        }

        // ───────────────────────────── The Unfriendly Amazon ─────────────────────────────

        static void AmazonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var amazon = PlaceMonster(engine, p, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                ToPlayersNextTurn(engine);
                Check("The Unfriendly Amazon: the lone other monster is Tributed with no prompt; she stays",
                    reached && engine.TurnPlayer == p && !engine.IsAwaitingEffectTarget && OnField(p, amazon) &&
                    !OnField(p, celtic) && p.Graveyard.Contains(celtic) && engine.Phase == DuelPhase.Main1,
                    $"amazon={OnField(p, amazon)} celtic={OnField(p, celtic)} phase={engine.Phase} " +
                    $"pending={engine.PendingActivation?.Prompt}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var amazon = PlaceMonster(engine, p, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var blade = PlaceMonster(engine, p, DarkBlade, 2, BattlePosition.Defense, false);
                ToPlayersNextTurn(engine);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.AwaitingUpkeepTribute && pend.LegalTargets.Contains(celtic) &&
                            pend.LegalTargets.Contains(blade) && !pend.LegalTargets.Contains(amazon);
                engine.CancelEffectTargeting();
                var stillAsked = engine.PendingActivation != null && engine.PendingActivation.AwaitingUpkeepTribute &&
                                 OnField(p, amazon);
                var picked = engine.TrySelectEffectTarget(blade);
                Check("The Unfriendly Amazon: 2+ other monsters → the human picks (never herself); Cancel cannot decline",
                    reached && asked && stillAsked && picked && OnField(p, amazon) && OnField(p, celtic) &&
                    !OnField(p, blade) && engine.PendingActivation == null && engine.Phase == DuelPhase.Main1,
                    $"asked={asked} stillAsked={stillAsked} picked={picked} blade={OnField(p, blade)} phase={engine.Phase}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var amazon = PlaceMonster(engine, p, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
                ToPlayersNextTurn(engine);
                Check("The Unfriendly Amazon: no other monster to Tribute → she is destroyed",
                    reached && engine.TurnPlayer == p && !OnField(p, amazon) && p.Graveyard.Contains(amazon),
                    $"amazon={OnField(p, amazon)}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var amazon = PlaceMonster(engine, o, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var imp = PlaceMonster(engine, o, FeralImp, 2, BattlePosition.Attack, true);
                EndTurn(engine); // the AI's Standby Phase
                Check("The Unfriendly Amazon (AI): Tributes its cheapest other monster at once",
                    reached && engine.TurnPlayer == o && OnField(o, amazon) && OnField(o, celtic) && !OnField(o, imp) &&
                    !engine.IsAwaitingEffectTarget,
                    $"amazon={OnField(o, amazon)} celtic={OnField(o, celtic)} imp={OnField(o, imp)}");
            }

            {
                // v162 cross-group audit: A Man with Wdjat's Standby pick (FlipDownAndPeek) opens before the upkeep runs;
                // once it is answered the Amazon's upkeep is still asked (it was skipped: the phase went on to Main Phase 1).
                const int wdjatId = 51351302;
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var amazon = PlaceMonster(engine, p, UnfriendlyAmazon, 0, BattlePosition.Attack, true);
                var wdjat = PlaceMonster(engine, p, wdjatId, 1, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var setMon = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Defense, false);
                PlaceSetTrap(engine, o, 55144522, 0); // a Set Pot of Greed: 2 face-down cards to look at
                ToPlayersNextTurn(engine);
                var look = engine.PendingActivation;
                var lookAsked = look != null && !look.AwaitingUpkeepTribute && look.LegalTargets.Contains(setMon);
                var looked = lookAsked && engine.TrySelectEffectTarget(setMon);
                var up = engine.PendingActivation;
                var upkeepAsked = up != null && up.AwaitingUpkeepTribute && up.LegalTargets.Contains(celtic) &&
                                  up.LegalTargets.Contains(wdjat) && !up.LegalTargets.Contains(amazon);
                var tributed = upkeepAsked && engine.TrySelectEffectTarget(celtic);
                Check("The Unfriendly Amazon + A Man with Wdjat: after the Standby look, the upkeep Tribute is still asked",
                    reached && engine.TurnPlayer == p && lookAsked && looked && upkeepAsked && tributed &&
                    OnField(p, amazon) && OnField(p, wdjat) && !OnField(p, celtic) && engine.PendingActivation == null &&
                    engine.Phase == DuelPhase.Main1,
                    $"lookAsked={lookAsked} looked={looked} upkeepAsked={upkeepAsked} tributed={tributed} " +
                    $"celtic={OnField(p, celtic)} phase={engine.Phase} pending={engine.PendingActivation?.Prompt}");
            }
        }

        // ───────────────────────────── Inspection ─────────────────────────────

        static void InspectionDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var insp = PutInHand(engine, p, Inspection);
                var lp0 = p.LifePoints;
                var used = engine.TryActivateSpellTrap(p, insp, true);
                Settle(engine);
                var noCostOnActivate = p.LifePoints == lp0 && insp.FaceUp && p.TryFindSpellTrap(insp, out _);
                PutInHand(engine, o, Celtic);
                PutInHand(engine, o, DarkBlade);
                var log = new List<string>();
                engine.OnLog += log.Add;
                EndTurn(engine); // the opponent's Standby Phase
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.AwaitingLpCost && pend.Controller == p && pend.Card == insp &&
                            pend.LpCostChoices.SequenceEqual(new[] { 500 });
                var handBefore = o.Hand.Count;
                var lp1 = p.LifePoints;
                var paid = engine.TrySelectLpCost(500);
                engine.OnLog -= log.Add;
                Check("Inspection: activation costs no LP; in the opponent's Standby you are asked PAY 500 / CANCEL",
                    reached && used && noCostOnActivate && engine.TurnPlayer == o && asked,
                    $"used={used} noCost={noCostOnActivate} asked={asked} prompt={pend?.Prompt}");
                Check("Inspection: paying 500 looks at 1 random card of their hand; their hand stays (shuffled)",
                    paid && lp1 - p.LifePoints == 500 && o.Hand.Count == handBefore && engine.PendingActivation == null &&
                    log.Exists(l => l.Contains("looks at 1 random card")),
                    $"paid={paid} lp={lp1}->{p.LifePoints} hand={handBefore}->{o.Hand.Count}");

                // Back to your turn: Inspection does nothing in your own Standby Phase.
                var lp2 = p.LifePoints;
                EndTurn(engine);
                Check("Inspection: nothing in your own Standby Phase",
                    engine.TurnPlayer == p && !engine.IsAwaitingEffectTarget && p.LifePoints == lp2,
                    $"pending={engine.PendingActivation?.Prompt} lp={lp2}->{p.LifePoints}");

                // The next opponent's Standby: CANCEL pays nothing.
                EndTurn(engine);
                var lp3 = p.LifePoints;
                var askedAgain = engine.PendingActivation?.Card == insp;
                engine.CancelEffectTargeting();
                Check("Inspection: CANCEL declines (no LP paid)",
                    askedAgain && engine.PendingActivation == null && p.LifePoints == lp3,
                    $"askedAgain={askedAgain} lp={lp3}->{p.LifePoints}");
            }

            {
                // v162 cross-group audit: the AI skips its Main Phase 1 (Timeater, BattleOutcomeTriggers) while the human's
                // Inspection choice is open (asked as its Standby Phase ends, the Aqua Spirit precedent: the turn goes on and
                // SimpleAi waits): answered, the turn is in the Battle Phase with nothing left open.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var insp = FaceUpSt(engine, p, Inspection, 0);
                PutInHand(engine, o, Celtic);
                BattleOutcomeTriggersRuntime.ScheduleMainPhase1Skip(engine, o, null);
                EndTurn(engine); // the AI's Standby Phase
                var asked = engine.PendingActivation?.Card == insp && engine.TurnPlayer == o;
                var lp = p.LifePoints;
                var paid = asked && engine.TrySelectLpCost(500);
                Check("Inspection + Timeater: PAY on the AI's turn; its Main Phase 1 is skipped to the Battle Phase, nothing open",
                    reached && asked && paid && engine.TurnPlayer == o && lp - p.LifePoints == 500 &&
                    engine.PendingActivation == null && engine.Phase == DuelPhase.Battle &&
                    !BattleOutcomeTriggersRuntime.MainPhase1SkipPending(engine, o),
                    $"asked={asked} paid={paid} phase={engine.Phase} turn={engine.TurnPlayer?.Name} " +
                    $"pending={engine.PendingActivation?.Prompt}");
            }

            {
                // The opponent's hand is empty during their Standby Phase (no draw): not asked.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSt(engine, p, Inspection, 0);
                o.SkipNextDrawPhase = true;
                var lp = p.LifePoints;
                EndTurn(engine);
                Check("Inspection: not usable while the opponent's hand is empty",
                    reached && engine.TurnPlayer == o && o.Hand.Count == 0 && !engine.IsAwaitingEffectTarget &&
                    p.LifePoints == lp,
                    $"hand={o.Hand.Count} pending={engine.PendingActivation?.Prompt}");
            }

            {
                // The AI's Inspection during your Standby Phase: it pays while it keeps 4000+ LP.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FaceUpSt(engine, o, Inspection, 0);
                PutInHand(engine, p, Celtic);
                EndTurn(engine);
                var lp = o.LifePoints;
                var hand = p.Hand.Count;
                EndTurn(engine); // your Standby Phase (after your draw)
                Check("Inspection (AI): pays 500 in your Standby Phase and looks; your hand is unchanged",
                    reached && engine.TurnPlayer == p && lp - o.LifePoints == 500 && p.Hand.Count == hand + 1 &&
                    !engine.IsAwaitingEffectTarget,
                    $"paid={lp - o.LifePoints} hand={hand}->{p.Hand.Count}");

                o.LifePoints = 4200;
                ToPlayersNextTurn(engine);
                Check("Inspection (AI): declines when it would drop under 4000 LP",
                    engine.TurnPlayer == p && o.LifePoints == 4200, $"lp={o.LifePoints}");
            }
        }
    }
}
