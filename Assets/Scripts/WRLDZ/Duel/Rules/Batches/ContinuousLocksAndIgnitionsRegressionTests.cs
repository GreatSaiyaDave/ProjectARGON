using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "ContinuousLocksAndIgnitions" (v163 PGD / MFC), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item>Gora Turtle c80233946: monsters with 1900 or more ATK (both sides, itself included) cannot declare an attack.</item>
    /// <item>Arsenal Bug c42364374: ATK/DEF become 1000 while you control no other face-up Insect (other changes on top).</item>
    /// <item>Timidity c40350910: face-down cards in either Spell &amp; Trap Zone cannot be destroyed by card effects until
    /// the opponent's next End Phase (Sets made later included).</item>
    /// <item>Non Aggression Area c76848240: your Standby Phase only; discard 1; the opponent cannot Normal Summon, Set or
    /// Special Summon during their next turn.</item>
    /// <item>Call of the Mummy c4861205: once per turn, control no monsters (activation and resolution), still on the field:
    /// Special Summon 1 Zombie from the hand.</item>
    /// <item>Frontline Base c46181000: once per turn, Special Summon 1 Level 4 or lower Union from the hand.</item>
    /// <item>Buster Rancher c84740193: equip only to ATK 1000 or less (re-checked except in damage calculation); +2500 ATK in
    /// damage calculation against a 2500+ ATK (Attack) / DEF (Defense) opponent's monster.</item>
    /// <item>Narrow Pass c40172183 stays Unimplemented (printed text and script disagree).</item>
    /// </list>
    /// </summary>
    public static class ContinuousLocksAndIgnitionsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int GoraTurtle = 80233946;
        const int ArsenalBug = 42364374;
        const int Timidity = 40350910;
        const int NarrowPass = 40172183;
        const int NonAggressionArea = 76848240;
        const int CallOfTheMummy = 4861205;
        const int FrontlineBase = 46181000;
        const int BusterRancher = 84740193;

        const int MessengerOfPeace = 44656491;
        const int Celtic = 91152256;        // Warrior 1400/1200
        const int Gaia = 6368038;           // Warrior 2300/2100
        const int MysticalElf = 15025844;   // Spellcaster 800/2000
        const int BasicInsect = 89091579;   // Insect 500/700
        const int ArmoredZombie = 20277860; // Zombie 1500/0
        const int DragonZombie = 66672569;  // Zombie 1600/0
        const int ZMetalTank = 64500000;    // Union Lv4 1500/1300
        const int BurningBeast = 59364406;  // Union Lv4 1500/1000
        const int Lunatite = 7369217;       // Union Lv7
        const int SummonedSkull = 70781052; // Fiend 2500/1200
        const int LabyrinthWall = 67284908; // Rock 0/3000
        const int HeavyStorm = 19613556;
        const int HarpiesFeatherDuster = 18144507;
        const int Mst = 5318639;
        const int DustTornado = 60082869;
        const int TrapHole = 4206964;
        const int PotOfGreed = 55144522;
        const int AxeOfDespair = 40619825;
        const int Waboku = 12607053;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("ContinuousLocksAndIgnitions: " + name, ok, detail ?? "");

            CompileShapes(db, Check);

            GoraTurtleLocksBothSides(db, pDeck, aDeck, Check);
            GoraTurtleOnlyWhileFaceUp(db, pDeck, aDeck, Check);

            ArsenalBugStats(db, pDeck, aDeck, Check);
            ArsenalBugLayering(db, pDeck, aDeck, Check);

            TimidityMassAndTargetedDestruction(db, pDeck, aDeck, Check);
            TimidityLastsUntilOpponentsEndPhase(db, pDeck, aDeck, Check);

            NonAggressionAreaLocksNextTurn(db, pDeck, aDeck, Check);
            NonAggressionAreaLegality(db, pDeck, aDeck, Check);

            CallOfTheMummySummons(db, pDeck, aDeck, Check);
            CallOfTheMummyConditions(db, pDeck, aDeck, Check);
            CallOfTheMummyAi(db, pDeck, aDeck, Check);

            FrontlineBaseSummons(db, pDeck, aDeck, Check);

            BusterRancherTargets(db, pDeck, aDeck, Check);
            BusterRancherDamageCalculation(db, pDeck, aDeck, Check);
            BusterRancherEquipLimit(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static (CompiledCardProgram prog, CardEffectStatusKind st) Of(CardDatabase db, int id)
        {
            var def = db.Get(id);
            var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
            return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
        }

        static bool Whole(CompiledCardProgram p, CardEffectStatusKind st, int clauses) =>
            p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == clauses &&
            (p.UnparsedFragments == null || p.UnparsedFragments.Length == 0);

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }
            return e;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/>; every chain window passes.</summary>
        static bool Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            var picked = false;
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
                    var ok = pick != null && e.TrySelectEffectTarget(pick);
                    picked |= ok;
                    if (!ok) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            return picked;
        }

        /// <summary>Passes every open chain window; an open pick stays open.</summary>
        static void PassWindows(DuelEngine e)
        {
            for (var i = 0; i < 10 && e.IsAwaitingResponse && !e.IsAwaitingEffectTarget; i++)
                e.PassResponse();
        }

        /// <summary>A Duel whose opponent is human too, with a Set Waboku: their chain window waits for an answer.</summary>
        static DuelEngine HeldChainDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = new DuelEngine { HumanVsHuman = true };
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }
            PlaceSetTrap(e, e.Opponent, Waboku, 4);
            return e;
        }

        /// <summary>Ends the current turn (passing open windows) and passes the next turn's opening windows.</summary>
        static void EndTurn(DuelEngine e)
        {
            Drive(e, _ => false);
            if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
            e.TryEndTurnSafe(e.TurnPlayer);
            Drive(e, _ => false);
        }

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnNumber}:{e.TurnPlayer?.Name} " +
            $"paused={e.StandbyPausedFor?.Name ?? "-"}";

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.TryFindMonster(c, out _)) at.Add(tag + ".mz");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz");
            }
            return string.Join("+", at);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var (p, st) = Of(db, GoraTurtle);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Gora Turtle: one continuous attack lock, ATK 1900 or more, both sides",
                    Whole(p, st, 1) && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.ContinuousCannotAttack && c.Amount == 1900 && c.Side == EffectSide.Both &&
                    !c.AmountIsLevel && string.IsNullOrEmpty(c.RaceFilter) && !c.RequiresSummonedOrFlippedThisTurn,
                    p?.ToString());
            }
            {
                var (p, st) = Of(db, ArsenalBug);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Arsenal Bug: ATK/DEF become 1000 unless another face-up Insect",
                    Whole(p, st, 1) && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace &&
                    c.Amount == 1000 && c.DefAmount == 1000 && c.RaceFilter == "Insect", p?.ToString());
            }
            {
                var (p, st) = Of(db, Timidity);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Timidity: one resolution registering the Set-card protection",
                    Whole(p, st, 1) && c.Timing == EffectTiming.Activate && !c.RequiresTargetChoice &&
                    c.Action == EffectActionKind.SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase, p?.ToString());
            }
            {
                var (p, st) = Of(db, NonAggressionArea);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Non Aggression Area: your Standby Phase only, discard 1 card, opponent's next-turn summon lock",
                    Whole(p, st, 1) && c.Timing == EffectTiming.Activate && c.OnlyDuringOwnStandby &&
                    c.RequiresDiscardCost && c.DiscardCostCount == 1 && c.DiscardCostAttribute == "*" &&
                    c.Action == EffectActionKind.OpponentCannotSummonOrSetDuringNextTurn && c.Side == EffectSide.Opponent,
                    p?.ToString());
            }
            {
                var (p, st) = Of(db, CallOfTheMummy);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Call of the Mummy: once-per-turn face-up ignition, a Zombie, no monsters, related at resolution",
                    Whole(p, st, 1) && c.Timing == EffectTiming.FaceUpFieldIgnition && c.OncePerTurn &&
                    c.Action == EffectActionKind.SpecialSummonFromHandByFaceUpIgnition && c.RaceFilter == "Zombie" &&
                    c.RequiresControllerControlsNoMonsters && c.RequiresThisRelatedAtResolution, p?.ToString());
            }
            {
                var (p, st) = Of(db, FrontlineBase);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Frontline Base: once-per-turn face-up ignition, a Level 4 or lower Union, no relation check",
                    Whole(p, st, 1) && c.Timing == EffectTiming.FaceUpFieldIgnition && c.OncePerTurn &&
                    c.Action == EffectActionKind.SpecialSummonFromHandByFaceUpIgnition &&
                    c.TargetCardKind == ContinuousLocksAndIgnitionsRuntime.UnionKind && c.AmountIsLevel && c.Amount == 4 &&
                    string.IsNullOrEmpty(c.RaceFilter) && !c.RequiresControllerControlsNoMonsters &&
                    !c.RequiresThisRelatedAtResolution, p?.ToString());
            }
            {
                var (p, st) = Of(db, BusterRancher);
                var eq = p?.ClauseList.FirstOrDefault(c => c?.Timing == EffectTiming.Activate);
                var cal = p?.ClauseList.FirstOrDefault(c => c?.Timing == EffectTiming.ContinuousWhileFaceUp);
                Check("Buster Rancher: equip with an ATK 1000 limit + damage-calculation +2500 vs 2500",
                    Whole(p, st, 2) && eq?.Action == EffectActionKind.EquipThisToTarget && eq.EquipLimitAtkMax == 1000 &&
                    eq.Zone == EffectZoneFilter.FieldAnyMonster && eq.RequiresTargetFaceUp &&
                    cal?.Action == EffectActionKind.EquippedGainsAtkInDamageCalcVsStrongBattler && cal.Amount == 2500 &&
                    cal.ConditionAmount == 2500, p?.ToString());
            }
            {
                // Narrow Pass: the script's EFFECT_CANNOT_SUMMON for a player with 2+ monsters is not the printed "up to 2
                // additional" count — fail closed.
                var (p, st) = Of(db, NarrowPass);
                Check("Narrow Pass: stays not FullyCompiled / not Implemented (text and script disagree)",
                    p != null && !p.FullyCompiled && st != CardEffectStatusKind.Implemented, $"{st} {p}");
            }
            {
                // Near miss: Messenger of Peace keeps its own Spell-borne ATK lock (1500) and upkeep.
                var (p, st) = Of(db, MessengerOfPeace);
                Check("Messenger of Peace: unchanged (ContinuousCannotAttack 1500 + Standby upkeep)",
                    st == CardEffectStatusKind.Implemented && p.ClauseList.Count == 2 &&
                    p.ClauseList.Exists(c => c.Action == EffectActionKind.ContinuousCannotAttack && c.Amount == 1500),
                    p?.ToString());
            }
        }

        // ───────────────────────────── Gora Turtle ─────────────────────────────

        static void GoraTurtleLocksBothSides(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, GoraTurtle, 0, BattlePosition.Defense, true);
                var gaia = PlaceMonster(e, e.Opponent, Gaia, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(e, e.Opponent, Celtic, 1, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var gaiaLocked = !e.CanAttack(e.Opponent, gaia) && !e.TryAttack(e.Opponent, gaia, null);
                var celticFree = e.CanAttack(e.Opponent, celtic);
                Check("Gora Turtle (yours): the opponent's 2300 ATK monster cannot attack, a 1400 ATK one can",
                    reached && gaiaLocked && celticFree, State(e));
            }
            {
                // Both Monster Zones: the turtle's own controller's 1900+ monster is locked too.
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Opponent, GoraTurtle, 0, BattlePosition.Defense, true);
                var gaia = PlaceMonster(e, e.Opponent, Gaia, 1, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                Check("Gora Turtle (theirs): its own controller's 2300 ATK monster cannot attack either",
                    reached && !e.CanAttack(e.Opponent, gaia), State(e));
            }
        }

        static void GoraTurtleOnlyWhileFaceUp(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, GoraTurtle, 0, BattlePosition.Defense, false);
                var gaia = PlaceMonster(e, e.Opponent, Gaia, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                Check("Gora Turtle: face-down — no lock", reached && e.CanAttack(e.Opponent, gaia), State(e));
            }
            {
                // Exactly 1900 ATK is locked ("1900 or more"): Celtic Guardian +500 = 1900.
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, GoraTurtle, 0, BattlePosition.Defense, true);
                var celtic = PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                celtic.UntilEndOfTurnAtk += 500;
                var locked = !e.CanAttack(e.Opponent, celtic);
                celtic.UntilEndOfTurnAtk -= 1;
                var free = e.CanAttack(e.Opponent, celtic);
                Check("Gora Turtle: 1900 ATK cannot attack, 1899 can",
                    reached && locked && free, $"atk={celtic.CurrentAtk} {State(e)}");
            }
        }

        // ───────────────────────────── Arsenal Bug ─────────────────────────────

        static void ArsenalBugStats(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var bug = PlaceMonster(e, p, ArsenalBug, 0, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var alone = bug.CurrentAtk == 1000 && bug.CurrentDef == 1000;
            var insect = PlaceMonster(e, p, BasicInsect, 1, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var withInsect = bug.CurrentAtk == 2000 && bug.CurrentDef == 2000;
            insect.FaceUp = false;
            insect.Position = BattlePosition.Defense;
            FieldSpellEffects.RefreshBoard(e);
            var faceDownInsect = bug.CurrentAtk == 1000 && bug.CurrentDef == 1000;
            p.MonsterZones[1].Occupant = null;
            PlaceMonster(e, e.Opponent, BasicInsect, 0, BattlePosition.Attack, true);
            FieldSpellEffects.RefreshBoard(e);
            var oppInsect = bug.CurrentAtk == 1000 && bug.CurrentDef == 1000;
            Check("Arsenal Bug: 1000/1000 alone, 2000/2000 with another face-up Insect, a Set or opponent's Insect does " +
                  "not count", alone && withInsect && faceDownInsect && oppInsect,
                $"alone={alone} with={withInsect} set={faceDownInsect} opp={oppInsect} now={bug.CurrentAtk}/{bug.CurrentDef}");
        }

        static void ArsenalBugLayering(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // ygopro SINGLE SET_ATTACK sets the base; the Equip's +1000 still applies on top.
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var bug = PlaceMonster(e, p, ArsenalBug, 0, BattlePosition.Attack, true);
            var axe = PutInHand(e, p, AxeOfDespair);
            var act = e.TryActivateSpellTrap(p, axe, fromHand: true);
            Drive(e, c => c == bug);
            FieldSpellEffects.RefreshBoard(e);
            Check("Arsenal Bug: alone with Axe of Despair — ATK 1000 + 1000 = 2000, DEF 1000",
                act && axe.EquippedTo == bug && bug.CurrentAtk == 2000 && bug.CurrentDef == 1000,
                $"act={act} eq={axe.EquippedTo?.Name} {bug.CurrentAtk}/{bug.CurrentDef} {State(e)}");
        }

        // ───────────────────────────── Timidity ─────────────────────────────

        static bool CastFromHand(DuelEngine e, DuelistState who, int id, CardInstance target = null)
        {
            var card = PutInHand(e, who, id);
            var ok = e.TryActivateSpellTrap(who, card, fromHand: true);
            Drive(e, c => target == null || c == target);
            return ok;
        }

        static void TimidityMassAndTargetedDestruction(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mySet = PlaceSetTrap(e, p, TrapHole, 0);
                var myFaceUp = FaceUpSpellTrap(e, p, MessengerOfPeace, 1);
                var theirSet = PlaceSetTrap(e, o, TrapHole, 0);
                var tim = CastFromHand(e, p, Timidity);
                var later = PlaceSetTrap(e, o, TrapHole, 1);
                var storm = CastFromHand(e, p, HeavyStorm);
                Check("Timidity: Heavy Storm — every Set card survives (one Set later too), the face-up one is destroyed",
                    tim && storm && p.TryFindSpellTrap(mySet, out _) && o.TryFindSpellTrap(theirSet, out _) &&
                    o.TryFindSpellTrap(later, out _) && p.Graveyard.Contains(myFaceUp),
                    $"mySet={Where(e, mySet)} faceUp={Where(e, myFaceUp)} theirs={Where(e, theirSet)} later={Where(e, later)} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var theirSet = PlaceSetTrap(e, o, TrapHole, 0);
                var theirSet2 = PlaceSetTrap(e, o, TrapHole, 1);
                var tim = CastFromHand(e, p, Timidity);
                var duster = CastFromHand(e, p, HarpiesFeatherDuster);
                var mst = CastFromHand(e, p, Mst, theirSet);
                var tornado = PlaceSetTrap(e, p, DustTornado, 2);
                var dt = e.TryActivateSpellTrap(p, tornado, fromHand: false);
                Drive(e, c => c == theirSet2);
                Check("Timidity: Harpie's Feather Duster, Mystical Space Typhoon and Dust Tornado cannot destroy Set cards",
                    tim && duster && mst && dt && o.TryFindSpellTrap(theirSet, out _) && o.TryFindSpellTrap(theirSet2, out _),
                    $"tim={tim} duster={duster} mst={mst} dt={dt} s1={Where(e, theirSet)} s2={Where(e, theirSet2)} {State(e)}");
            }
        }

        static void TimidityLastsUntilOpponentsEndPhase(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mySet = PlaceSetTrap(e, p, TrapHole, 0);
            var tim = CastFromHand(e, p, Timidity);
            EndTurn(e); // → the opponent's turn 2
            var oppTurn = e.TurnPlayer == o && e.InMainPhase;
            var storm2 = CastFromHand(e, o, HeavyStorm);
            var survivedTurn2 = p.TryFindSpellTrap(mySet, out _);
            EndTurn(e); // → the player's turn 3: the protection ended with the opponent's End Phase
            var mySet3 = PlaceSetTrap(e, p, TrapHole, 1);
            if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
            var storm3 = CastFromHand(e, p, HeavyStorm);
            Check("Timidity: still applies in the opponent's next turn, gone on your following turn",
                tim && oppTurn && storm2 && survivedTurn2 && storm3 && !p.TryFindSpellTrap(mySet, out _) &&
                !p.TryFindSpellTrap(mySet3, out _),
                $"oppTurn={oppTurn} s2={storm2} kept={survivedTurn2} s3={storm3} set={Where(e, mySet)} set3={Where(e, mySet3)} {State(e)}");
        }

        // ───────────────────────────── Non Aggression Area ─────────────────────────────

        static void NonAggressionAreaLocksNextTurn(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var naa = PlaceSetTrap(e, p, NonAggressionArea, 0);
            EndTurn(e); // player's turn 1 → opponent's turn 2
            p.Hand.Clear();
            var discard = PutInHand(e, p, Celtic);
            var keep = PutInHand(e, p, PotOfGreed);
            e.TryEndTurnSafe(o);
            Drive(e, _ => false);
            var paused = e.TurnPlayer == p && e.Phase == DuelPhase.Standby && e.StandbyPausedFor == p;
            var act = paused && e.TryActivateSpellTrap(p, naa, fromHand: false);
            Drive(e, c => c == discard);
            var resolved = act && p.Graveyard.Contains(discard) && p.Hand.Contains(keep) && p.Graveyard.Contains(naa);
            Check("Non Aggression Area: your Standby Phase holds for it; discard 1 card (your pick) and it resolves",
                paused && resolved, $"paused={paused} act={act} discard={Where(e, discard)} naa={Where(e, naa)} {State(e)}");

            var main = e.ContinueAfterStandby() && e.Phase == DuelPhase.Main1;
            var mine = PutInHand(e, p, MysticalElf);
            var ownFree = SummonProcedures.CheckNormalOrTribute(e, p, mine, asSet: false).Legal;
            EndTurn(e); // → opponent's turn 4: locked
            var oppHand = PutInHand(e, o, Celtic);
            var oppTurn = e.TurnPlayer == o && e.InMainPhase;
            var noNs = !SummonProcedures.CheckNormalOrTribute(e, o, oppHand, asSet: false).Legal;
            var noSet = !SummonProcedures.CheckNormalOrTribute(e, o, oppHand, asSet: true).Legal &&
                        !e.TryNormalSummon(o, oppHand, asSet: true);
            var ssCard = PutInHand(e, o, Gaia);
            o.Hand.Remove(ssCard);
            var noSs = RuleLocksRuntime.SpecialSummonLocked(e, o, ssCard) &&
                       !e.SpecialSummonToField(o, ssCard, BattlePosition.Attack, true) && !o.TryFindMonster(ssCard, out _);
            Check("Non Aggression Area: during the opponent's next turn they cannot Normal Summon, Set or Special Summon",
                main && ownFree && oppTurn && noNs && noSet && noSs,
                $"main={main} own={ownFree} oppTurn={oppTurn} ns={noNs} set={noSet} ss={noSs} {State(e)}");

            EndTurn(e); // → player's turn 5
            EndTurn(e); // → opponent's turn 6: free again
            var free = e.TurnPlayer == o && SummonProcedures.CheckNormalOrTribute(e, o, oppHand, asSet: false).Legal &&
                       !RuleLocksRuntime.SpecialSummonLocked(e, o, ssCard);
            Check("Non Aggression Area: the lock ends with that turn", free, State(e));
        }

        static void NonAggressionAreaLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Not in your Main Phase.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var naa = PlaceSetTrap(e, p, NonAggressionArea, 0);
                PutInHand(e, p, Celtic);
                Check("Non Aggression Area: not in your Main Phase",
                    e.InMainPhase && !e.CanActivateSpellTrap(p, naa, fromHand: false), State(e));
            }
            {
                // Not in the opponent's Standby Phase, and not without a card to discard.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var naa = PlaceSetTrap(e, p, NonAggressionArea, 0);
                var mine = PlaceSetTrap(e, o, NonAggressionArea, 0);
                EndTurn(e); // → opponent's turn 2 (main)
                EndTurn(e); // → player's turn 3: empty hand
                p.Hand.Clear();
                var emptyHand = e.TurnPlayer == p && !e.CanActivateSpellTrap(p, naa, fromHand: false);
                var oppNotInYourStandby = !e.CanActivateSpellTrap(o, mine, fromHand: false);
                Check("Non Aggression Area: needs a card to discard; not in the opponent's Standby Phase",
                    emptyHand && oppNotInYourStandby, $"empty={emptyHand} opp={oppNotInYourStandby} {State(e)}");
            }
        }

        // ───────────────────────────── Call of the Mummy ─────────────────────────────

        static void CallOfTheMummySummons(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var armored = PutInHand(e, p, ArmoredZombie);
            var dragon = PutInHand(e, p, DragonZombie);
            var celtic = PutInHand(e, p, Celtic);
            var cotm = PutInHand(e, p, CallOfTheMummy);
            var placed = e.TryActivateSpellTrap(p, cotm, fromHand: true);
            Drive(e, _ => false);
            var onField = cotm.FaceUp && p.TryFindSpellTrap(cotm, out _) && p.MonsterCount == 0;
            var fired = e.TryActivateSpellTrap(p, cotm, fromHand: false);
            PassWindows(e);
            var offered = Legal(e);
            var asked = offered.Count == 2 && offered.Contains(armored) && offered.Contains(dragon) && !offered.Contains(celtic);
            Drive(e, c => c == dragon);
            var summoned = p.TryFindMonster(dragon, out _) && dragon.FaceUp && p.Hand.Contains(armored) &&
                           p.TryFindSpellTrap(cotm, out _) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget;
            Check("Call of the Mummy: activation does nothing; its effect Special Summons the Zombie you pick",
                placed && onField && fired && asked && summoned,
                $"placed={placed} onField={onField} fired={fired} offered={Names(offered)} dragon={Where(e, dragon)} {State(e)}");
            for (var i = 0; i < p.MonsterZones.Length; i++) p.MonsterZones[i].Occupant = null;
            Check("Call of the Mummy: once per turn", !e.CanActivateSpellTrap(p, cotm, fromHand: false), State(e));
        }

        static void CallOfTheMummyConditions(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, ArmoredZombie);
                var cotm = FaceUpSpellTrap(e, p, CallOfTheMummy, 0);
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var withMonster = e.CanActivateSpellTrap(p, cotm, fromHand: false);
                p.MonsterZones[0].Occupant = null;
                p.Hand.Clear();
                PutInHand(e, p, Celtic);
                var noZombie = e.CanActivateSpellTrap(p, cotm, fromHand: false);
                Check("Call of the Mummy: not while you control a monster; not without a Zombie in hand",
                    !withMonster && !noZombie, State(e));
            }
            {
                // A monster you control at resolution: nothing happens (the use is spent).
                var e = HeldChainDuel(db, pDeck, aDeck);
                var p = e.Player;
                var zombie = PutInHand(e, p, ArmoredZombie);
                var cotm = FaceUpSpellTrap(e, p, CallOfTheMummy, 0);
                var fired = e.TryActivateSpellTrap(p, cotm, fromHand: false);
                var link = e.Chain.HasLinks && e.IsAwaitingResponse;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                Drive(e, _ => true);
                Check("Call of the Mummy: a monster you control at resolution — nothing is Special Summoned",
                    fired && link && p.Hand.Contains(zombie) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"fired={fired} link={link} zombie={Where(e, zombie)} {State(e)}");
            }
            {
                // Not related any more (destroyed in response): nothing happens.
                var e = HeldChainDuel(db, pDeck, aDeck);
                var p = e.Player;
                var zombie = PutInHand(e, p, ArmoredZombie);
                var cotm = FaceUpSpellTrap(e, p, CallOfTheMummy, 0);
                var fired = e.TryActivateSpellTrap(p, cotm, fromHand: false) && e.IsAwaitingResponse;
                e.SendCardToGrave(p, cotm);
                Drive(e, _ => true);
                Check("Call of the Mummy: left the field before resolution — nothing is Special Summoned",
                    fired && p.Hand.Contains(zombie) && p.MonsterCount == 0 && !e.Chain.HasLinks,
                    $"fired={fired} zombie={Where(e, zombie)} {State(e)}");
            }
        }

        static void CallOfTheMummyAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentMain(e);
            ClearBoard(e);
            var o = e.Opponent;
            o.Hand.Clear();
            var a = PutInHand(e, o, ArmoredZombie);
            var d = PutInHand(e, o, DragonZombie);
            var cotm = FaceUpSpellTrap(e, o, CallOfTheMummy, 0);
            var wants = PerCountBurnsRuntime.AiWantsFaceUpFieldIgnition(e, o, cotm);
            var fired = e.TryActivateSpellTrap(o, cotm, fromHand: false);
            Drive(e, _ => false);
            Check("Call of the Mummy: the AI uses it and takes its highest-ATK Zombie without a prompt",
                reached && wants && fired && o.TryFindMonster(d, out _) && o.Hand.Contains(a) && !e.IsAwaitingEffectTarget &&
                !e.Chain.HasLinks, $"wants={wants} fired={fired} d={Where(e, d)} {State(e)}");
        }

        // ───────────────────────────── Frontline Base ─────────────────────────────

        static void FrontlineBaseSummons(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // A lone legal Union: no prompt. A Level 7 Union and a non-Union are not offered. Monsters you control and
                // leaving the field before resolution do not matter.
                var e = HeldChainDuel(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var tank = PutInHand(e, p, ZMetalTank);
                var luna = PutInHand(e, p, Lunatite);
                var elf = PutInHand(e, p, MysticalElf);
                var fb = FaceUpSpellTrap(e, p, FrontlineBase, 0);
                var fired = e.TryActivateSpellTrap(p, fb, fromHand: false) && e.IsAwaitingResponse;
                e.SendCardToGrave(p, fb);
                Drive(e, _ => false);
                Check("Frontline Base: Special Summons its lone Level 4 or lower Union (even after leaving the field)",
                    fired && p.TryFindMonster(tank, out _) && tank.FaceUp && p.Hand.Contains(luna) && p.Hand.Contains(elf) &&
                    !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"fired={fired} tank={Where(e, tank)} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var tank = PutInHand(e, p, ZMetalTank);
                var beast = PutInHand(e, p, BurningBeast);
                var fb = FaceUpSpellTrap(e, p, FrontlineBase, 0);
                var fired = e.TryActivateSpellTrap(p, fb, fromHand: false);
                PassWindows(e);
                var offered = Legal(e);
                Drive(e, c => c == beast);
                var again = e.CanActivateSpellTrap(p, fb, fromHand: false);
                Check("Frontline Base: you pick 1 of 2 Unions; once per turn",
                    fired && offered.Count == 2 && p.TryFindMonster(beast, out _) && p.Hand.Contains(tank) && !again,
                    $"offered={Names(offered)} beast={Where(e, beast)} again={again} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, Lunatite);
                PutInHand(e, p, Celtic);
                var fb = FaceUpSpellTrap(e, p, FrontlineBase, 0);
                Check("Frontline Base: no Level 4 or lower Union in hand — cannot use",
                    !e.CanActivateSpellTrap(p, fb, fromHand: false), State(e));
            }
        }

        // ───────────────────────────── Buster Rancher ─────────────────────────────

        static void BusterRancherTargets(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var br = PutInHand(e, p, BusterRancher);
            var act = e.TryActivateSpellTrap(p, br, fromHand: true);
            var offered = Legal(e);
            var lone = !e.IsAwaitingEffectTarget;
            Drive(e, c => c == elf);
            Check("Buster Rancher: only a monster with 1000 or less ATK can be its target",
                act && !offered.Contains(celtic) && br.EquippedTo == elf && (lone || offered.Contains(elf)),
                $"offered={Names(offered)} eq={br.EquippedTo?.Name} {State(e)}");
        }

        static CardInstance EquipRancher(DuelEngine e, DuelistState who, CardInstance host)
        {
            var br = PutInHand(e, who, BusterRancher);
            e.TryActivateSpellTrap(who, br, fromHand: true);
            Drive(e, c => c == host);
            return br;
        }

        static void BusterRancherDamageCalculation(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Attacked by a 2500 ATK monster: 800 + 2500 = 3300 during damage calculation.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var br = EquipRancher(e, p, elf);
                var skull = PlaceMonster(e, o, SummonedSkull, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var oLp = o.LifePoints;
                var attacked = e.TryAttack(o, skull, elf);
                DrainCombat(e);
                Check("Buster Rancher: attacked by a 2500 ATK monster — 3300 ATK in damage calculation, the attacker loses",
                    reached && attacked && o.Graveyard.Contains(skull) && p.TryFindMonster(elf, out _) &&
                    o.LifePoints == oLp - 800 && elf.CurrentAtk == 800 && br.EquippedTo == elf,
                    $"eq={br.EquippedTo?.Name} skull={Where(e, skull)} lp {oLp}->{o.LifePoints} elf={elf.CurrentAtk} {State(e)}");
            }
            {
                // A 1400 ATK attacker is not strong enough: no gain.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                EquipRancher(e, p, elf);
                var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
                var reached = ReachOpponentBattle(e);
                var attacked = e.TryAttack(o, celtic, elf);
                DrainCombat(e);
                Check("Buster Rancher: attacked by a 1400 ATK monster — no gain, the equipped monster is destroyed",
                    reached && attacked && p.Graveyard.Contains(elf) && o.TryFindMonster(celtic, out _), State(e));
            }
            {
                // Attacking a Defense Position monster with 3000 DEF: 3300 destroys it.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                EquipRancher(e, p, elf);
                var wall = PlaceMonster(e, o, LabyrinthWall, 0, BattlePosition.Defense, true);
                EndTurn(e);
                EndTurn(e); // → the player's turn 3
                if (e.StandbyPausedFor != null) e.ContinueAfterStandby();
                var bp = e.TryEnterBattlePhase(p);
                Drive(e, _ => false);
                var attacked = e.TryAttack(p, elf, wall);
                DrainCombat(e);
                Check("Buster Rancher: attacking a 3000 DEF Defense Position monster — 3300 ATK destroys it",
                    bp && attacked && o.Graveyard.Contains(wall) && p.TryFindMonster(elf, out _), $"wall={Where(e, wall)} {State(e)}");
            }
        }

        static void BusterRancherEquipLimit(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // The target has more than 1000 ATK when the activation resolves: it cannot be equipped (Duel.Equip fails).
                var e = HeldChainDuel(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var br = PutInHand(e, p, BusterRancher);
                var act = e.TryActivateSpellTrap(p, br, fromHand: true);
                if (e.IsAwaitingEffectTarget) e.TrySelectEffectTarget(elf);
                var held = e.IsAwaitingResponse;
                elf.UntilEndOfTurnAtk += 500;
                Drive(e, _ => false);
                FieldSpellEffects.RefreshBoard(e);
                Check("Buster Rancher: its target has 1300 ATK at resolution — not equipped, the card leaves the field",
                    act && held && br.EquippedTo == null && !elf.Equips.Contains(br) && !p.TryFindSpellTrap(br, out _),
                    $"act={act} held={held} br={Where(e, br)} eq={br.EquippedTo?.Name} {State(e)}");
            }

            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var br = EquipRancher(e, p, elf);
                var equipped = br.EquippedTo == elf;
                var axe = PutInHand(e, p, AxeOfDespair);
                e.TryActivateSpellTrap(p, axe, fromHand: true);
                Drive(e, c => c == elf);
                FieldSpellEffects.RefreshBoard(e);
                Check("Buster Rancher: the equipped monster rises above 1000 ATK — Buster Rancher is destroyed, the Axe stays",
                    equipped && p.Graveyard.Contains(br) && axe.EquippedTo == elf && elf.CurrentAtk == 1800,
                    $"br={Where(e, br)} axe={axe.EquippedTo?.Name} atk={elf.CurrentAtk} {State(e)}");
            }
        }
    }
}
