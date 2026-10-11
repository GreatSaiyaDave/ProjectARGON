using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// MonsterNegationAndControlLocks batch: Skill Drain, Non-Spellcasting Area, Blindly Loyal Goblin, Mataza the Zapper,
    /// Final Attack Orders, Stumbling. Compile shapes through the real compiler, near-miss texts that must not compile to
    /// these actions, and mini-duels through the engine entry points (TryActivateSpellTrap, TryNormalSummon,
    /// TryFlipSummon, TrySelectEffectTarget, CanAttack, CanChangePosition). The duels run in the Player's turn-3 Main
    /// Phase with empty hands. Runtime: <see cref="MonsterNegationAndControlLocksRuntime"/>,
    /// <see cref="ContinuousNegationRuntime"/>.
    /// </summary>
    public static class MonsterNegationAndControlLocksRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int SkillDrain = 82732705;          // Continuous Trap
        const int NonSpellcastingArea = 20065549; // Continuous Spell
        const int BlindlyLoyalGoblin = 35215622;  // Warrior 1800/1500
        const int Mataza = 22609617;              // Warrior 1300/800
        const int FinalAttackOrders = 52503575;   // Continuous Trap
        const int Stumbling = 34646691;           // Continuous Spell

        const int Celtic = 91152256;              // Normal Warrior 1400/1200
        const int GiantSoldier = 13039848;        // Normal Rock 1300/2000
        const int BlueEyes = 89631139;            // Normal Dragon 3000/2500
        const int ExiledForce = 74131780;         // Effect Warrior 1000/1000: Tribute this card, target 1 monster; destroy it
        const int GyakuGirePanda = 9817927;       // Effect Beast 800/1600: +500 ATK per opponent monster
        const int HaneHane = 7089711;             // FLIP: target 1 monster on the field; return it to the hand
        const int ManEaterBug = 54652250;         // FLIP (registered script): target 1 monster on the field; destroy it
        const int Jinzo = 77585513;               // Effect Machine 2400/1500: Trap lock + negation
        const int ChangeOfHeart = 4031928;
        const int SnatchSteal = 45986603;
        const int CreatureSwap = 31036355;
        const int Mst = 5318639;
        const int MonsterReborn = 83764718;
        const int DarkHole = 53129443;
        const int AxeOfDespair = 40619825;
        const int Sogen = 86318356;               // Field Spell: Warrior / Beast-Warrior +200 ATK/DEF
        const int MessengerOfPeace = 44656491;    // Continuous Spell: monsters with 1500+ ATK cannot declare an attack
        const int SwordsOfRevealingLight = 72302403;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("MonsterNegationAndControlLocks: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            SkillDrainDuels(db, pDeck, aDeck, Check);
            FinalAttackOrdersDuels(db, pDeck, aDeck, Check);
            ControlLockDuels(db, pDeck, aDeck, Check);
            StumblingDuels(db, pDeck, aDeck, Check);
            NonSpellcastingAreaDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            reached = false;
            for (var t = 0; t < 12; t++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                if (e.TurnPlayer == e.Player && e.Phase == DuelPhase.Main1 && e.TurnNumber >= 3)
                {
                    reached = true;
                    break;
                }

                e.TryEndTurnSafe(e.TurnPlayer);
            }

            ClearBoard(e);
            e.ClearPendingResponse();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
                who.Banished.Clear();
            }

            return e;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Pick the first legal card matching <paramref name="want"/> at every prompt; pass every window.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want = null)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want ?? (_ => true));
                    if (pick == null || !e.TrySelectEffectTarget(pick)) e.CancelEffectTargeting();
                    continue;
                }

                if (e.IsAwaitingResponse)
                {
                    e.PassResponse();
                    continue;
                }

                break;
            }

            e.NotifyPublic();
        }

        static CardInstance PlaceFaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static CompiledCardProgram CompileText(int id, string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "MonsterNegationAndControlLocks probe", type = type,
                frameType = type.StartsWith("Spell") || type.StartsWith("Trap") ? "spell" : "effect",
                race = race, attribute = type.StartsWith("Spell") || type.StartsWith("Trap") ? null : "EARTH",
                level = 4, atk = 1000, def = 1000, desc = text
            });

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static bool HasAction(CompiledCardProgram p, EffectActionKind a) => Has(p, c => c.Action == a);

        static string Describe(CompiledCardProgram p) =>
            p == null ? "null" : string.Join("; ", p.ClauseList.ConvertAll(EffectVocabulary.Describe));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static bool Controls(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var sd = Prog(db, SkillDrain);
            Check("Skill Drain FullyCompiled: 1000 LP activation cost + continuous monster negation",
                sd != null && sd.FullyCompiled && Implemented(db, SkillDrain) && sd.ClauseList.Count == 2 &&
                Has(sd, c => c.Timing == EffectTiming.Activate && c.PayLpAmount == 1000 && c.Action == EffectActionKind.None) &&
                Has(sd, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                             c.Action == EffectActionKind.ContinuousNegateMonsterEffectsOnField && c.Side == EffectSide.Both),
                Describe(sd));

            var nsa = Prog(db, NonSpellcastingArea);
            Check("Non-Spellcasting Area FullyCompiled: non-Effect monsters unaffected by other Spells",
                nsa != null && nsa.FullyCompiled && Implemented(db, NonSpellcastingArea) && nsa.ClauseList.Count == 1 &&
                Has(nsa, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                              c.Action == EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells),
                Describe(nsa));

            var gob = Prog(db, BlindlyLoyalGoblin);
            Check("Blindly Loyal Goblin FullyCompiled: control of this card cannot switch",
                gob != null && gob.FullyCompiled && Implemented(db, BlindlyLoyalGoblin) && gob.ClauseList.Count == 1 &&
                Has(gob, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                              c.Action == EffectActionKind.ControlOfThisCannotSwitch),
                Describe(gob));

            var mat = Prog(db, Mataza);
            Check("Mataza the Zapper FullyCompiled: second attack + control lock",
                mat != null && mat.FullyCompiled && Implemented(db, Mataza) && mat.ClauseList.Count == 2 &&
                Has(mat, c => c.Action == EffectActionKind.ControlOfThisCannotSwitch) &&
                Has(mat, c => c.Action == EffectActionKind.ExtraAttacks && c.Amount == 1),
                Describe(mat));

            var fao = Prog(db, FinalAttackOrders);
            Check("Final Attack Orders FullyCompiled: continuous Attack Position lock",
                fao != null && fao.FullyCompiled && Implemented(db, FinalAttackOrders) && fao.ClauseList.Count == 1 &&
                Has(fao, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                              c.Action == EffectActionKind.ContinuousForceAttackLockPosition),
                Describe(fao));

            var stu = Prog(db, Stumbling);
            Check("Stumbling FullyCompiled: summoned monsters change to Defense Position",
                stu != null && stu.FullyCompiled && Implemented(db, Stumbling) && stu.ClauseList.Count == 1 &&
                Has(stu, c => c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                              c.Action == EffectActionKind.ChangeSummonedMonstersToDefense),
                Describe(stu));

            // Near misses: look-alike sentences never reach these actions.
            var noCost = CompileText(990001, "Trap Card", "Continuous",
                "Negate the effects of all face-up monsters while they are face-up on the field.");
            Check("Near miss: monster negation without Skill Drain's cost / parenthetical is not compiled",
                !HasAction(noCost, EffectActionKind.ContinuousNegateMonsterEffectsOnField) && !noCost.FullyCompiled,
                Describe(noCost));
            var defLock = CompileText(990002, "Trap Card", "Continuous",
                "All face-up monsters on the field are changed to Defense Position and their battle positions cannot be changed.");
            Check("Near miss: a Defense Position sibling is not Final Attack Orders",
                !HasAction(defLock, EffectActionKind.ContinuousForceAttackLockPosition), Describe(defLock));
            var condLock = CompileText(990003, "Effect Monster", "Warrior",
                "Control of this card cannot switch while you control another Warrior-Type monster.");
            Check("Near miss: a conditional control lock is not compiled",
                !HasAction(condLock, EffectActionKind.ControlOfThisCannotSwitch) && !condLock.FullyCompiled,
                Describe(condLock));
            var nsOnly = CompileText(990004, "Spell Card", "Continuous",
                "Any monster that is Normal Summoned is changed to Defense Position.");
            Check("Near miss: a Normal-Summon-only Stumbling sibling is not compiled",
                !HasAction(nsOnly, EffectActionKind.ChangeSummonedMonstersToDefense), Describe(nsOnly));
            var trapImm = CompileText(990005, "Spell Card", "Continuous",
                "All face-up monsters, except for Effect Monsters, are unaffected by other Trap effects.");
            Check("Near miss: a Trap-immunity sibling is not Non-Spellcasting Area",
                !HasAction(trapImm, EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells), Describe(trapImm));
        }

        // ───────────────────────────── Skill Drain ─────────────────────────────

        static void SkillDrainDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Activation pays 1000 LP; Panda's continuous ATK gain stops, an Equip Spell's grant stays; destroying Skill
                // Drain ends the negation.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var panda = PlaceMonster(e, p, GyakuGirePanda, 0, BattlePosition.Attack, true);
                var force = PlaceMonster(e, p, ExiledForce, 1, BattlePosition.Attack, true);
                PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var axe = PutInHand(e, p, AxeOfDespair);
                e.NotifyPublic();
                var pandaBefore = panda.CurrentAtk;
                e.TryActivateSpellTrap(p, axe, fromHand: true);
                Drive(e, t => ReferenceEquals(t, force));
                var forceWithAxe = force.CurrentAtk;
                var sd = PlaceSetTrap(e, p, SkillDrain, 1);
                var lp = p.LifePoints;
                var used = e.TryActivateSpellTrap(p, sd, fromHand: false);
                Drive(e);
                Check("Skill Drain: activation pays 1000 LP and negates face-up Effect Monsters (both fields)",
                    reached && used && p.LifePoints == lp - 1000 && sd.FaceUp && panda.IsNegated && force.IsNegated,
                    $"used={used} lp={lp}->{p.LifePoints} pandaNeg={panda.IsNegated} forceNeg={force.IsNegated}");
                Check("Skill Drain: a continuous ATK gain stops; an Equip Spell's grant stays",
                    pandaBefore == 1300 && panda.CurrentAtk == 800 && forceWithAxe == 2000 && force.CurrentAtk == 2000,
                    $"panda {pandaBefore}->{panda.CurrentAtk} force axe={forceWithAxe} now={force.CurrentAtk}");

                var mst = PutInHand(e, p, Mst);
                e.TryActivateSpellTrap(p, mst, fromHand: true);
                Drive(e, t => ReferenceEquals(t, sd));
                Check("Skill Drain: once it leaves the field the negation ends",
                    p.Graveyard.Contains(sd) && !panda.IsNegated && panda.CurrentAtk == 1300,
                    $"sdGy={p.Graveyard.Contains(sd)} pandaNeg={panda.IsNegated} atk={panda.CurrentAtk}");
            }

            {
                // Effects can still be activated: Exiled Force Tributes itself as the cost, so it is no longer face-up on the
                // field when the effect resolves — the destruction applies.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, SkillDrain, 0);
                var force = PlaceMonster(e, p, ExiledForce, 0, BattlePosition.Attack, true);
                var bewd = PlaceMonster(e, opp, BlueEyes, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var negatedBefore = force.IsNegated;
                var used = e.TryActivateSpellTrap(p, force, fromHand: false);
                Drive(e, t => ReferenceEquals(t, bewd));
                Check("Skill Drain: Exiled Force can still activate and, Tributed as the cost, destroys its target",
                    reached && negatedBefore && used && p.Graveyard.Contains(force) && opp.Graveyard.Contains(bewd),
                    $"neg={negatedBefore} used={used} forceGy={p.Graveyard.Contains(force)} bewdGy={opp.Graveyard.Contains(bewd)}");
            }

            {
                // A FLIP effect activates as the monster is Flip Summoned and resolves while it is face-up on the field:
                // negated (compiled Hane-Hane, registered Man-Eater Bug).
                foreach (var flipId in new[] { HaneHane, ManEaterBug })
                {
                    var e = PlayerMain(db, pDeck, aDeck, out var reached);
                    var p = e.Player;
                    var opp = e.Opponent;
                    PlaceFaceUpSpellTrap(e, p, SkillDrain, 0);
                    var flip = PlaceMonster(e, p, flipId, 0, BattlePosition.Defense, false);
                    var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                    e.NotifyPublic();
                    var flipped = e.TryFlipSummon(p, flip);
                    Drive(e, t => ReferenceEquals(t, celtic));
                    Check($"Skill Drain: {db.Get(flipId)?.name}'s FLIP effect resolves negated while it is face-up",
                        reached && flipped && flip.FaceUp && Controls(p, flip) && Controls(opp, celtic),
                        $"flipped={flipped} faceUp={flip.FaceUp} celticOnField={Controls(opp, celtic)}");
                }
            }

            foreach (var flipId in new[] { HaneHane, ManEaterBug })
            {
                // Without Skill Drain the same Flip Summon bounces / destroys (control for the test above).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var flip = PlaceMonster(e, p, flipId, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                e.TryFlipSummon(p, flip);
                Drive(e, t => ReferenceEquals(t, celtic));
                Check($"Skill Drain (control): without it {db.Get(flipId)?.name}'s FLIP removes its target",
                    reached && !Controls(opp, celtic), $"celticOnField={Controls(opp, celtic)}");
            }

            {
                // Jinzo face-up first: Skill Drain cannot be activated. Skill Drain face-up first: Jinzo is negated and
                // Skill Drain applies (the earlier of two mutual negators wins).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, opp, Jinzo, 0, BattlePosition.Attack, true);
                var sd = PlaceSetTrap(e, p, SkillDrain, 0);
                e.NotifyPublic();
                Check("Skill Drain: Jinzo on the field first — Skill Drain cannot be activated",
                    reached && !e.CanActivateSpellTrap(p, sd, fromHand: false));

                var e2 = PlayerMain(db, pDeck, aDeck, out var reached2);
                var sd2 = PlaceFaceUpSpellTrap(e2, e2.Player, SkillDrain, 0);
                e2.NotifyPublic();
                var jinzo = PlaceMonster(e2, e2.Opponent, Jinzo, 0, BattlePosition.Attack, true);
                e2.NotifyPublic();
                Check("Skill Drain: face-up before Jinzo — Jinzo is negated, Skill Drain is not",
                    reached2 && jinzo.IsNegated && !sd2.IsNegated, $"jinzoNeg={jinzo.IsNegated} sdNeg={sd2.IsNegated}");
            }
        }

        // ───────────────────────────── Final Attack Orders ─────────────────────────────

        static void FinalAttackOrdersDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var opp = e.Opponent;
            var mine = PlaceMonster(e, p, GiantSoldier, 0, BattlePosition.Defense, true);
            var theirs = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Defense, true);
            var setOne = PlaceMonster(e, opp, GiantSoldier, 1, BattlePosition.Defense, false);
            var mySet = PlaceMonster(e, p, Celtic, 1, BattlePosition.Defense, false);
            var fao = PlaceSetTrap(e, p, FinalAttackOrders, 0);
            e.NotifyPublic();
            var canBefore = e.CanChangePosition(p, mine);
            var used = e.TryActivateSpellTrap(p, fao, fromHand: false);
            Drive(e);
            Check("Final Attack Orders: face-up monsters on both fields change to Attack Position; Sets stay face-down",
                reached && used && canBefore && fao.FaceUp && mine.Position == BattlePosition.Attack &&
                theirs.Position == BattlePosition.Attack && !setOne.FaceUp && setOne.Position == BattlePosition.Defense,
                $"used={used} mine={mine.Position} theirs={theirs.Position} set={setOne.FaceUp}/{setOne.Position}");
            Check("Final Attack Orders: their battle positions cannot be changed, and no Flip Summon (Lua e3 covers Sets)",
                !e.CanChangePosition(p, mine) && e.IsBattlePositionLocked(mine) && !e.CanFlipSummon(p, mySet));

            var mst = PutInHand(e, p, Mst);
            e.TryActivateSpellTrap(p, mst, fromHand: true);
            Drive(e, t => ReferenceEquals(t, fao));
            Check("Final Attack Orders: once it leaves the field the lock ends",
                p.Graveyard.Contains(fao) && e.CanChangePosition(p, mine) && e.CanFlipSummon(p, mySet),
                $"faoGy={p.Graveyard.Contains(fao)}");
        }

        // ───────────────────────────── Blindly Loyal Goblin / Mataza ─────────────────────────────

        static void ControlLockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var gob = PlaceMonster(e, opp, BlindlyLoyalGoblin, 0, BattlePosition.Attack, true);
                var coh = PutInHand(e, p, ChangeOfHeart);
                var snatch = PutInHand(e, p, SnatchSteal);
                e.NotifyPublic();
                Check("Blindly Loyal Goblin: Change of Heart / Snatch Steal cannot target it (no other target: no activation)",
                    reached && !e.CanActivateSpellTrap(p, coh, fromHand: true) &&
                    !e.CanActivateSpellTrap(p, snatch, fromHand: true));

                var celtic = PlaceMonster(e, opp, Celtic, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, coh, fromHand: true);
                var offeredGoblin = Legal(e).Contains(gob);
                Drive(e, t => ReferenceEquals(t, celtic));
                Check("Blindly Loyal Goblin: Change of Heart takes the other monster, never the Goblin",
                    used && !offeredGoblin && Controls(opp, gob) && Controls(p, celtic),
                    $"used={used} offeredGoblin={offeredGoblin} celticMine={Controls(p, celtic)}");
            }

            {
                // The lock applies only while it is face-up and its effect applies: a face-down Goblin, or one negated by
                // Skill Drain, can be taken.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var gob = PlaceMonster(e, opp, BlindlyLoyalGoblin, 0, BattlePosition.Defense, false);
                var coh = PutInHand(e, p, ChangeOfHeart);
                e.NotifyPublic();
                e.TryActivateSpellTrap(p, coh, fromHand: true);
                Drive(e, t => ReferenceEquals(t, gob));
                Check("Blindly Loyal Goblin: face-down, its control can be taken",
                    reached && Controls(p, gob), $"mine={Controls(p, gob)}");

                var e2 = PlayerMain(db, pDeck, aDeck, out var reached2);
                PlaceFaceUpSpellTrap(e2, e2.Opponent, SkillDrain, 0);
                var gob2 = PlaceMonster(e2, e2.Opponent, BlindlyLoyalGoblin, 0, BattlePosition.Attack, true);
                var coh2 = PutInHand(e2, e2.Player, ChangeOfHeart);
                e2.NotifyPublic();
                e2.TryActivateSpellTrap(e2.Player, coh2, fromHand: true);
                Drive(e2, t => ReferenceEquals(t, gob2));
                Check("Blindly Loyal Goblin: negated by Skill Drain, its control can be taken",
                    reached2 && gob2.IsNegated && Controls(e2.Player, gob2), $"neg={gob2.IsNegated} mine={Controls(e2.Player, gob2)}");
            }

            {
                // Creature Swap: a locked monster is not one its controller can give; with no other, no activation.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var mat = PlaceMonster(e, opp, Mataza, 0, BattlePosition.Attack, true);
                var swap = PutInHand(e, p, CreatureSwap);
                e.NotifyPublic();
                Check("Mataza the Zapper: Creature Swap cannot be activated when the opponent's only monster is control-locked",
                    reached && !e.CanActivateSpellTrap(p, swap, fromHand: true) && Controls(opp, mat));
                Check("Mataza the Zapper: it can attack twice in each Battle Phase",
                    e.ExtraAttacksAllowed(mat) == 1 && e.MaxAttacksThisTurn(mat) == 2,
                    $"extra={e.ExtraAttacksAllowed(mat)} max={e.MaxAttacksThisTurn(mat)}");
            }
        }

        // ───────────────────────────── Stumbling ─────────────────────────────

        static void StumblingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var stu = PutInHand(e, p, Stumbling);
                var used = e.TryActivateSpellTrap(p, stu, fromHand: true);
                Drive(e);
                var celtic = PutInHand(e, p, Celtic);
                var summoned = e.TryNormalSummon(p, celtic, asSet: false);
                Drive(e);
                Check("Stumbling: a Normal Summoned monster is changed to face-up Defense Position",
                    reached && used && stu.FaceUp && summoned && celtic.FaceUp && celtic.Position == BattlePosition.Defense,
                    $"used={used} summoned={summoned} pos={celtic.Position} faceUp={celtic.FaceUp}");

                var flipper = PlaceMonster(e, p, GiantSoldier, 1, BattlePosition.Defense, false);
                e.NotifyPublic();
                var flipped = e.TryFlipSummon(p, flipper);
                Drive(e);
                Check("Stumbling: a Flip Summoned monster is changed to face-up Defense Position",
                    flipped && flipper.FaceUp && flipper.Position == BattlePosition.Defense,
                    $"flipped={flipped} pos={flipper.Position} faceUp={flipper.FaceUp}");
            }

            foreach (var withStumbling in new[] { false, true })
            {
                // Monster Reborn Special Summons in Attack Position; Stumbling changes it to Defense.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                if (withStumbling) PlaceFaceUpSpellTrap(e, p, Stumbling, 0);
                var bewd = e.CreateCardInstance(BlueEyes);
                p.Graveyard.Add(bewd);
                var reborn = PutInHand(e, p, MonsterReborn);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, reborn, fromHand: true);
                Drive(e, t => t.CardId == BlueEyes);
                var back = p.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == BlueEyes);
                var want = withStumbling ? BattlePosition.Defense : BattlePosition.Attack;
                Check(withStumbling
                        ? "Stumbling: a Special Summoned monster is changed to Defense Position"
                        : "Stumbling (control): without it Monster Reborn's monster stays in Attack Position",
                    reached && used && back != null && back.FaceUp && back.Position == want,
                    $"used={used} back={back != null} pos={back?.Position}");
            }

            {
                // A FLIP monster whose FLIP pick holds the summon window is still changed (and its FLIP still resolves).
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, Stumbling, 0);
                var bug = PlaceMonster(e, p, ManEaterBug, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                e.NotifyPublic();
                var flipped = e.TryFlipSummon(p, bug);
                Drive(e, t => ReferenceEquals(t, celtic));
                Check("Stumbling: a Flip Summoned FLIP monster is changed to Defense Position; its FLIP effect resolves",
                    reached && flipped && bug.FaceUp && bug.Position == BattlePosition.Defense && !Controls(opp, celtic),
                    $"flipped={flipped} pos={bug.Position} celticOnField={Controls(opp, celtic)}");
            }

            {
                // A Normal Set is not a Summon: the Set monster stays face-down.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpellTrap(e, p, Stumbling, 0);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                var set = e.TryNormalSummon(p, celtic, asSet: true);
                Drive(e);
                Check("Stumbling: a Normal Set monster stays face-down Defense",
                    reached && set && !celtic.FaceUp && celtic.Position == BattlePosition.Defense);
            }
        }

        // ───────────────────────────── Non-Spellcasting Area ─────────────────────────────

        static void NonSpellcastingAreaDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Dark Hole: the Normal Monster is unaffected, the Effect Monster is destroyed.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceFaceUpSpellTrap(e, p, NonSpellcastingArea, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var force = PlaceMonster(e, opp, ExiledForce, 1, BattlePosition.Attack, true);
                var hole = PutInHand(e, p, DarkHole);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, hole, fromHand: true);
                Drive(e);
                Check("Non-Spellcasting Area: Dark Hole destroys the Effect Monster, not the Normal Monster",
                    reached && used && Controls(opp, celtic) && opp.Graveyard.Contains(force),
                    $"used={used} celtic={Controls(opp, celtic)} forceGy={opp.Graveyard.Contains(force)}");
            }

            {
                // A Field Spell's aura and an Equip Spell skip the Normal Monster; the Effect Monster still gets the aura.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                var sogen = e.CreateCardInstance(Sogen);
                sogen.FaceUp = true;
                p.FieldSpellZone.Occupant = sogen;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var force = PlaceMonster(e, p, ExiledForce, 1, BattlePosition.Attack, true);
                e.NotifyPublic();
                var boosted = celtic.CurrentAtk;
                PlaceFaceUpSpellTrap(e, opp, NonSpellcastingArea, 0);
                e.NotifyPublic();
                Check("Non-Spellcasting Area: Sogen's +200 stops on the Normal Monster, stays on the Effect Monster",
                    reached && boosted == 1600 && celtic.CurrentAtk == 1400 && force.CurrentAtk == 1200,
                    $"celtic {boosted}->{celtic.CurrentAtk} force={force.CurrentAtk}");

                var axe = PutInHand(e, p, AxeOfDespair);
                var used = e.TryActivateSpellTrap(p, axe, fromHand: true);
                Drive(e, t => ReferenceEquals(t, celtic));
                Check("Non-Spellcasting Area: Axe of Despair does not raise the Normal Monster's ATK",
                    used && celtic.CurrentAtk == 1400, $"used={used} atk={celtic.CurrentAtk}");
            }

            {
                // Change of Heart may target the Normal Monster, but it is unaffected at resolution.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var opp = e.Opponent;
                PlaceFaceUpSpellTrap(e, opp, NonSpellcastingArea, 0);
                var celtic = PlaceMonster(e, opp, Celtic, 0, BattlePosition.Attack, true);
                var coh = PutInHand(e, p, ChangeOfHeart);
                e.NotifyPublic();
                var used = e.TryActivateSpellTrap(p, coh, fromHand: true);
                Drive(e, t => ReferenceEquals(t, celtic));
                Check("Non-Spellcasting Area: Change of Heart does not take the Normal Monster",
                    reached && used && Controls(opp, celtic), $"used={used} theirs={Controls(opp, celtic)}");
            }

            {
                // Spell attack locks: Messenger of Peace and Swords of Revealing Light do not stop the Normal Monster.
                foreach (var lockId in new[] { MessengerOfPeace, SwordsOfRevealingLight })
                {
                    var e = PlayerMain(db, pDeck, aDeck, out var reached);
                    var p = e.Player;
                    var opp = e.Opponent;
                    PlaceFaceUpSpellTrap(e, opp, lockId, 0);
                    var bewd = PlaceMonster(e, p, BlueEyes, 0, BattlePosition.Attack, true);
                    var jinzo = PlaceMonster(e, p, Jinzo, 1, BattlePosition.Attack, true);
                    e.NotifyPublic();
                    var entered = e.TryEnterBattlePhase(p);
                    var lockedBefore = !e.CanAttack(p, bewd);
                    PlaceFaceUpSpellTrap(e, p, NonSpellcastingArea, 0);
                    e.NotifyPublic();
                    Check($"Non-Spellcasting Area: {db.Get(lockId)?.name} does not stop the Normal Monster's attack",
                        reached && entered && lockedBefore && e.CanAttack(p, bewd) && !e.CanAttack(p, jinzo),
                        $"entered={entered} lockedBefore={lockedBefore} bewd={e.CanAttack(p, bewd)} jinzo={e.CanAttack(p, jinzo)}");
                }
            }

            {
                // Stumbling is a Spell: the Normal Monster stays in Attack Position, an Effect Monster changes.
                var e = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                PlaceFaceUpSpellTrap(e, p, Stumbling, 0);
                PlaceFaceUpSpellTrap(e, p, NonSpellcastingArea, 1);
                var celtic = PutInHand(e, p, Celtic);
                e.NotifyPublic();
                e.TryNormalSummon(p, celtic, asSet: false);
                Drive(e);
                Check("Non-Spellcasting Area: Stumbling does not change the Normal Monster's position",
                    reached && celtic.FaceUp && celtic.Position == BattlePosition.Attack, $"pos={celtic.Position}");
            }
        }
    }
}
