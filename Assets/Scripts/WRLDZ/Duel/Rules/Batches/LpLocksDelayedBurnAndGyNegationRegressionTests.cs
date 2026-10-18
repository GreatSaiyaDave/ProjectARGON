using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// LpLocksDelayedBurnAndGyNegation batch (AST): King of the Swamp (Fusion substitute + Polymerization search), Wall of
    /// Revealing Light (chosen multiple of 1000 LP, ATK &lt;= paid attack lock), Weapon Change (Standby pay 700, switch the
    /// current ATK/DEF until the end of the opponent's next turn), Dora of Fate (delayed Normal Summon burn) and the
    /// fail-closed pin of The End of Anubis (<see cref="LpLocksDelayedBurnAndGyNegationAtoms"/>,
    /// <see cref="LpLocksDelayedBurnAndGyNegationRuntime"/>).
    /// </summary>
    public static class LpLocksDelayedBurnAndGyNegationRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int KingOfTheSwamp = 79109599;
        const int WallOfRevealingLight = 17078030;
        const int WeaponChange = 10035717;
        const int DoraOfFate = 67464807;
        const int EndOfAnubis = 65403020;

        const int Polymerization = 24094653;
        const int FlameSwordsman = 45231177;   // "Flame Manipulator" + "Masaki the Legendary Swordsman"
        const int FlameManipulator = 34460851; // Spellcaster Lv3 900/1000
        const int MysticalSheep = 30451366;    // v180 substitute
        const int BigShieldGardna = 65240384;  // Warrior Lv4 100/2600
        const int Celtic = 91152256;           // Warrior Lv4 1400/1200
        const int CannonSoldier = 11384280;    // Machine Lv4 1400/1300
        const int GiantSoldierOfStone = 13039848; // Rock Lv3 1300/2000
        const int DarkBlade = 11321183;        // Warrior Lv4 1800/1500
        const int Kuriboh = 40640057;          // Fiend Lv1
        const int SummonedSkull = 70781052;    // Fiend Lv6 2500/1200
        const int DarkMagician = 36996508;     // Spellcaster Lv7

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("LpLocksDelayedBurnAndGyNegation: " + name, ok, detail ?? "");
            CompileShapes(db, Check);
            KingDuels(db, pDeck, aDeck, Check);
            WallDuels(db, pDeck, aDeck, Check);
            WeaponChangeDuels(db, pDeck, aDeck, Check);
            DoraDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id)
        {
            var def = db.Get(id);
            return def != null ? CardTextEffectCompiler.Compile(def) : null;
        }

        static bool Implemented(CardDatabase db, int id)
        {
            var def = db.Get(id);
            return def != null && CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented;
        }

        static CompiledCardProgram Probe(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990000001, name = "LpLocks probe", type = type,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                race = race, attribute = "EARTH", level = 4, desc = text
            });

        static string Shape(CompiledCardProgram p) =>
            p == null
                ? "null"
                : $"FC={p.FullyCompiled} " + string.Join(" | ",
                    p.ClauseList.Where(c => c != null).Select(c => $"{c.Timing}/{c.Action}/{c.Zone}"));

        static void Settle(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static void EndTurn(DuelEngine engine)
        {
            if (engine.IsAwaitingResponse) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            if (engine.IsAwaitingResponse) engine.PassResponse();
        }

        /// <summary>The Player's Main Phase 1 of turn 3 or later, board and hands empty.</summary>
        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 &&
                              engine.Phase == DuelPhase.Main1); t++)
                EndTurn(engine);
            if (engine.IsAwaitingResponse) engine.PassResponse();
            reached = engine.TurnPlayer == engine.Player && engine.TurnNumber >= 3 && engine.Phase == DuelPhase.Main1;
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            engine.Player.LifePoints = 8000;
            engine.Opponent.LifePoints = 8000;
            return engine;
        }

        static CardInstance FaceUpSt(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        /// <summary>From the Opponent's turn to the Player's next Main Phase 1 (stops early on an open pick).</summary>
        static bool ToPlayerMain1(DuelEngine engine)
        {
            for (var i = 0; i < 6 && !(engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1); i++)
            {
                if (engine.IsAwaitingEffectTarget) return false;
                if (engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Standby)
                {
                    engine.ContinueAfterStandby();
                    continue;
                }

                EndTurn(engine);
            }

            return engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1;
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var p = Prog(db, KingOfTheSwamp);
                Check("King of the Swamp compiles to the FusionSubstitute ability + the discard-self Polymerization search",
                    Implemented(db, KingOfTheSwamp) && p != null && p.FullyCompiled && p.ClauseList.Count == 2 &&
                    p.ClauseList.Exists(c => c.Action == EffectActionKind.FusionSubstitute &&
                                             c.Timing == EffectTiming.ContinuousWhileFaceUp) &&
                    p.ClauseList.Exists(c => c.Action == EffectActionKind.AddNamedFromDeckToHand &&
                                             c.RequiresDiscardSelf && c.ActivatesFromHand && c.FromDeck &&
                                             c.NamedCard == "Polymerization"),
                    Shape(p));
            }
            {
                var p = Prog(db, WallOfRevealingLight);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Wall of Revealing Light compiles to a paid-LP (multiple of 1000) ATK <= paid attack lock",
                    Implemented(db, WallOfRevealingLight) && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == EffectActionKind.ContinuousCannotAttack &&
                    c.Side == EffectSide.Opponent && c.CannotAttackIfAtkAtMostPaidLp && c.RequiresLpCostMultiple == 1000 &&
                    c.Amount == 0 && c.PayLpAmount == 0 &&
                    c.ActivationCondition == LpLocksDelayedBurnAndGyNegationRuntime.PaidLpMultipleAffordable &&
                    TextEffectRuntime.IsContinuousTrapPlacementOnly(db.Get(WallOfRevealingLight), p),
                    Shape(p));
            }
            {
                var p = Prog(db, WeaponChange);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Weapon Change compiles to an optional Standby pay-700 Warrior/Machine target switch (no Activate cost)",
                    Implemented(db, WeaponChange) && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.StandbyPhase &&
                    c.Action == EffectActionKind.SwitchTargetCurrentAtkDefUntilOpponentsNextEndPhase &&
                    c.PayLpAmount == 700 && c.RaceFilter == "Warrior|Machine" && c.IsOptional &&
                    c.RequiresTargetChoice && c.RequiresTargetFaceUp && !c.OpponentTurnOnly &&
                    p.ClausesFor(EffectTiming.Activate).Count == 0,
                    Shape(p));
            }
            {
                var p = Prog(db, DoraOfFate);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Dora of Fate compiles to an opponent's-turn targeted delayed burn (Level > 1, 500 per Level)",
                    Implemented(db, DoraOfFate) && p.FullyCompiled && p.ClauseList.Count == 1 &&
                    c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.BurnWhenYouNormalSummonOneLevelLowerThanTargetNextTurn &&
                    c.Zone == EffectZoneFilter.OppFaceUpMonsters && c.RequiresTargetChoice && c.OpponentTurnOnly &&
                    c.MonsterLevelMin == 2 && c.Amount == 500 && TargetingTruthRuntime.ClauseTargets(c),
                    Shape(p));
            }
            {
                // The End of Anubis stays fail-closed: GY-activated effects and effects targeting GY cards resolve at many
                // sites outside ApplyClause and outside the Chain (inline batch triggers), so no partial negation ships.
                var def = db.Get(EndOfAnubis);
                var p = Prog(db, EndOfAnubis);
                Check("The End of Anubis stays fail-closed (not FullyCompiled, not Implemented, kept out of decks)",
                    def != null && p != null && !p.FullyCompiled &&
                    CardEffectStatus.Classify(def) != CardEffectStatusKind.Implemented &&
                    !CardEffectStatus.MayIncludeInDeck(def),
                    Shape(p));
            }

            // Near misses: look-alike sentences stay uncompiled.
            {
                var bare = Probe("Trap Card", "Continuous", "Activate by paying any multiple of 1000 Life Points.");
                var other = Probe("Trap Card", "Continuous",
                    "Activate by paying any multiple of 1000 Life Points. Monsters your opponent controls cannot attack " +
                    "if their ATK is greater than or equal to the amount you paid.");
                Check("Wall look-alikes (bare cost; ATK >= paid) are not FullyCompiled",
                    bare != null && !bare.FullyCompiled && other != null && !other.FullyCompiled,
                    $"bare: {Shape(bare)} / other: {Shape(other)}");
            }
            {
                var other = Probe("Spell Card", "Continuous",
                    "Once during each of your Standby Phases: You can pay 700 Life Points, then target 1 Warrior or " +
                    "Machine-Type monster you control; switch the current ATK and DEF of that target until the End Phase.");
                Check("Weapon Change look-alike (until the End Phase) gets no switch clause",
                    other != null && !other.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.SwitchTargetCurrentAtkDefUntilOpponentsNextEndPhase),
                    Shape(other));
            }
            {
                var other = Probe("Trap Card", "Normal",
                    "You can only activate this card during your opponent's turn. Select 1 face-up monster on your " +
                    "opponent's side of the field. When you Special Summon a monster that is 1 Level lower than the " +
                    "selected monster during your next turn, inflict damage to your opponent's Life Points equal to the " +
                    "Level of the selected monster x 500 points.");
                Check("Dora look-alike (Special Summon) gets no delayed-burn clause",
                    other != null && !other.ClauseList.Exists(c =>
                        c != null && c.Action == EffectActionKind.BurnWhenYouNormalSummonOneLevelLowerThanTargetNextTurn),
                    Shape(other));
            }
            {
                var extra = Probe("Effect Monster", "Aqua",
                    "This card can be used as a substitute for any 1 Fusion Material whose name is specifically listed " +
                    "on the Fusion Monster Card, but the other Fusion Material(s) must be correct. If this card is used " +
                    "as a Fusion Material, shuffle it into the Deck instead of sending it to the Graveyard.");
                Check("A substitute carrier with an uncompiled second sentence is not FullyCompiled (no partial card)",
                    extra != null && !extra.FullyCompiled, Shape(extra));
            }
        }

        // ───────────────────────────── King of the Swamp ─────────────────────────────

        static DuelEngine FusionBoard(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            e.Player.Hand.Clear();
            e.Player.Graveyard.Clear();
            e.Player.ExtraDeck.Clear();
            e.Player.ExtraDeck.Add(FlameSwordsman);
            return e;
        }

        static void KingDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FusionBoard(db, pDeck, aDeck);
                var p = e.Player;
                var flame = PutInHand(e, p, FlameManipulator);
                var king = PutInHand(e, p, KingOfTheSwamp);
                var poly = PutInHand(e, p, Polymerization);
                var can = e.CanActivateSpellTrap(p, poly, fromHand: true);
                var ok = e.TryActivateSpellTrap(p, poly, fromHand: true);
                Settle(e);
                Check("King of the Swamp substitutes for Masaki: Flame Manipulator + King fuse into Flame Swordsman",
                    can && ok && p.MonstersOnField().Any(m => m != null && m.CardId == FlameSwordsman) &&
                    p.Graveyard.Contains(flame) && p.Graveyard.Contains(king),
                    $"can={can} ok={ok} field={string.Join(",", p.MonstersOnField().Select(m => m.Name))}");
            }
            {
                var e = FusionBoard(db, pDeck, aDeck);
                var p = e.Player;
                PutInHand(e, p, KingOfTheSwamp);
                PutInHand(e, p, MysticalSheep);
                var poly = PutInHand(e, p, Polymerization);
                Check("King of the Swamp + another substitute cannot cover both named materials (one substitution only)",
                    !e.CanActivateSpellTrap(p, poly, fromHand: true) && !e.TryActivateSpellTrap(p, poly, fromHand: true));
            }
            {
                var e = Fresh(db, pDeck, aDeck);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                p.Deck.RemoveAll(id => id == Polymerization);
                p.Deck.Insert(0, Polymerization);
                var oppHand = o.Hand.Count;
                var oppDeck = o.Deck.Count;
                var king = PutInHand(e, p, KingOfTheSwamp);
                var can = e.CanActivateSpellTrap(p, king, fromHand: true);
                var ok = e.TryActivateSpellTrap(p, king, fromHand: true);
                Settle(e);
                Check("King of the Swamp: discard it; Polymerization goes from YOUR Deck to YOUR hand (not the opponent's)",
                    can && ok && p.Graveyard.Contains(king) && p.Hand.Exists(h => h.CardId == Polymerization) &&
                    !p.Deck.Contains(Polymerization) && o.Hand.Count == oppHand && o.Deck.Count == oppDeck,
                    $"can={can} ok={ok} hand={string.Join(",", p.Hand.Select(h => h.Name))} oppHand={oppHand}->{o.Hand.Count}");
            }
        }

        // ───────────────────────────── Wall of Revealing Light ─────────────────────────────

        static void WallDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
                var skull = PlaceMonster(e, o, SummonedSkull, 1, BattlePosition.Attack, true);
                var mine = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var wall = PlaceSetTrap(e, p, WallOfRevealingLight, 0);
                var tried = e.TryActivateSpellTrap(p, wall, fromHand: false);
                var pend = e.PendingActivation;
                var asked = pend != null && pend.AwaitingLpCost && pend.Card == wall &&
                            pend.LpCostChoices.SequenceEqual(Enumerable.Range(1, 8).Select(i => i * 1000)) &&
                            !wall.FaceUp && p.LifePoints == 8000 && !e.Chain.HasLinks;
                var paid = e.TrySelectLpCost(2000);
                Settle(e);
                Check("Wall of Revealing Light: a human chooses 1000..8000 before it is placed; paying 2000 activates it",
                    reached && tried && asked && paid && wall.FaceUp && p.TryFindSpellTrap(wall, out _) &&
                    p.LifePoints == 6000 && LpLocksDelayedBurnAndGyNegationRuntime.PaidAmount(wall) == 2000 &&
                    !e.IsAwaitingEffectTarget,
                    $"tried={tried} asked={asked} paid={paid} lp={p.LifePoints} faceUp={wall.FaceUp} " +
                    $"choices={string.Join(",", pend?.LpCostChoices ?? new List<int>())}");

                var lockedBlade = e.ContinuousCannotAttackBlocks(o, blade);
                var freeSkull = !e.ContinuousCannotAttackBlocks(o, skull);
                var ownFree = !e.ContinuousCannotAttackBlocks(p, mine);
                Check("Wall: the opponent's 1800 ATK monster cannot attack, its 2500 ATK one can; your own monsters can",
                    lockedBlade && freeSkull && ownFree, $"blade={lockedBlade} skull={freeSkull} own={ownFree}");

                blade.LingeringAtkModifier = 300;   // 2100
                skull.LingeringAtkModifier = -500;  // 2000
                Check("Wall: current ATK is read each time (2100 may attack, 2000 may not)",
                    !e.ContinuousCannotAttackBlocks(o, blade) && e.ContinuousCannotAttackBlocks(o, skull),
                    $"blade={blade.CurrentAtk} skull={skull.CurrentAtk}");
                blade.LingeringAtkModifier = 0;
                skull.LingeringAtkModifier = 0;

                var battle = ReachOpponentBattle(e);
                Check("Wall in the opponent's Battle Phase: Dark Blade cannot declare an attack, Summoned Skull can",
                    battle && !e.CanAttack(o, blade) && e.CanAttack(o, skull),
                    $"battle={battle} phase={e.Phase} blade={e.CanAttack(o, blade)} skull={e.CanAttack(o, skull)}");

                wall.IsNegated = true;
                var negatedFree = !e.ContinuousCannotAttackBlocks(o, blade);
                wall.IsNegated = false;
                wall.FaceUp = false;
                var faceDownFree = !e.ContinuousCannotAttackBlocks(o, blade);
                wall.FaceUp = true;
                Check("Wall: a negated or face-down Wall locks nothing", negatedFree && faceDownFree,
                    $"negated={negatedFree} faceDown={faceDownFree}");
            }
            {
                // CANCEL at the amount: not activated, nothing paid, still Set.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var wall = PlaceSetTrap(e, p, WallOfRevealingLight, 0);
                var tried = e.TryActivateSpellTrap(p, wall, fromHand: false);
                var asked = e.PendingActivation?.Card == wall;
                e.CancelEffectTargeting();
                Check("Wall: CANCEL at the amount leaves it Set and pays nothing",
                    tried && asked && e.PendingActivation == null && !wall.FaceUp && p.LifePoints == 8000 &&
                    !e.Chain.HasLinks && LpLocksDelayedBurnAndGyNegationRuntime.PaidAmount(wall) == 0,
                    $"asked={asked} faceUp={wall.FaceUp} lp={p.LifePoints}");
                p.LifePoints = 1000;
                Check("Wall: a human with exactly 1000 LP may activate it (Lua allows paying every LP)",
                    e.CanActivateSpellTrap(p, wall, fromHand: false));
                p.LifePoints = 999;
                Check("Wall: below 1000 LP it cannot be activated", !e.CanActivateSpellTrap(p, wall, fromHand: false));
            }
            {
                // The AI pays enough to lock the strongest monster, never its last LP.
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                o.LifePoints = 8000;
                var skull = PlaceMonster(e, p, SummonedSkull, 0, BattlePosition.Attack, true);
                var wall = PlaceSetTrap(e, o, WallOfRevealingLight, 0);
                var ok = e.TryActivateSpellTrap(o, wall, fromHand: false);
                Settle(e);
                Check("Wall (AI): pays 3000 for the opponent's 2500 ATK monster and locks it",
                    reached && ok && wall.FaceUp && o.LifePoints == 5000 &&
                    LpLocksDelayedBurnAndGyNegationRuntime.PaidAmount(wall) == 3000 &&
                    e.ContinuousCannotAttackBlocks(p, skull),
                    $"reached={reached} ok={ok} lp={o.LifePoints} paid={LpLocksDelayedBurnAndGyNegationRuntime.PaidAmount(wall)}");

                var wall2 = PlaceSetTrap(e, o, WallOfRevealingLight, 1);
                o.LifePoints = 2500;
                var ok2 = e.TryActivateSpellTrap(o, wall2, fromHand: false);
                Settle(e);
                Check("Wall (AI): with 2500 LP it pays 2000, never down to 0",
                    ok2 && o.LifePoints == 500 && LpLocksDelayedBurnAndGyNegationRuntime.PaidAmount(wall2) == 2000,
                    $"ok={ok2} lp={o.LifePoints}");

                var wall3 = PlaceSetTrap(e, o, WallOfRevealingLight, 2);
                o.LifePoints = 1000;
                Check("Wall (AI): with 1000 LP it does not activate (it would pay its last LP)",
                    !e.CanActivateSpellTrap(o, wall3, fromHand: false) && !e.TryActivateSpellTrap(o, wall3, false) &&
                    o.LifePoints == 1000 && !wall3.FaceUp);
            }
        }

        // ───────────────────────────── Weapon Change ─────────────────────────────

        static void WeaponChangeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = e.Player;
                var o = e.Opponent;
                var wc = PutInHand(e, p, WeaponChange);
                var can = e.CanActivateSpellTrap(p, wc, fromHand: true);
                var ok = e.TryActivateSpellTrap(p, wc, fromHand: true);
                Settle(e);
                Check("Weapon Change: activation from the hand places it face-up and costs no LP",
                    reached && can && ok && wc.FaceUp && p.TryFindSpellTrap(wc, out _) && p.LifePoints == 8000,
                    $"can={can} ok={ok} lp={p.LifePoints}");

                var gardna = PlaceMonster(e, p, BigShieldGardna, 0, BattlePosition.Attack, true);
                var cannon = PlaceMonster(e, p, CannonSoldier, 1, BattlePosition.Attack, true);
                var stone = PlaceMonster(e, p, GiantSoldierOfStone, 2, BattlePosition.Attack, true);
                EndTurn(e); // the opponent's turn: nothing
                var noneOnOpp = e.TurnPlayer == o && e.PendingActivation == null && p.LifePoints == 8000;
                EndTurn(e); // your Standby Phase
                var pend = e.PendingActivation;
                var asked = e.TurnPlayer == p && pend != null && pend.Card == wc &&
                            pend.LegalTargets.Contains(gardna) && pend.LegalTargets.Contains(cannon) &&
                            !pend.LegalTargets.Contains(stone);
                var picked = asked && e.TrySelectEffectTarget(gardna);
                Check("Weapon Change: nothing in the opponent's turn; in your Standby you may target a face-up Warrior / " +
                      "Machine (not the Rock)",
                    noneOnOpp && asked && picked,
                    $"noneOnOpp={noneOnOpp} asked={asked} prompt={pend?.Prompt} " +
                    $"targets={string.Join(",", pend?.LegalTargets.Select(t => t.Name) ?? new string[0])}");
                Check("Weapon Change: pay 700, Big Shield Gardna becomes 2600 ATK / 100 DEF",
                    p.LifePoints == 7300 && gardna.CurrentAtk == 2600 && gardna.CurrentDef == 100 &&
                    e.PendingActivation == null,
                    $"lp={p.LifePoints} {gardna.CurrentAtk}/{gardna.CurrentDef}");

                var main = ToPlayerMain1(e);
                gardna.UntilEndOfTurnAtk += 500; // a later boost adds on top (Rush Recklessly)
                var boosted = gardna.CurrentAtk == 3100;
                gardna.UntilEndOfTurnAtk -= 500;
                EndTurn(e); // the opponent's turn: the switch still applies
                var oppTurn = e.TurnPlayer == o && gardna.CurrentAtk == 2600 && gardna.CurrentDef == 100;
                EndTurn(e); // the opponent's End Phase ends it; your Standby asks again
                var ended = gardna.CurrentAtk == 100 && gardna.CurrentDef == 2600;
                var askedAgain = e.PendingActivation?.Card == wc;
                var lp = p.LifePoints;
                e.CancelEffectTargeting();
                Check("Weapon Change: the switch lasts through the opponent's next turn and ends at its End Phase",
                    main && boosted && oppTurn && ended,
                    $"main={main} boosted={boosted} oppTurn={oppTurn} now={gardna.CurrentAtk}/{gardna.CurrentDef}");
                Check("Weapon Change: CANCEL at the next Standby declines (no LP paid)",
                    askedAgain && e.PendingActivation == null && p.LifePoints == lp && gardna.CurrentAtk == 100,
                    $"askedAgain={askedAgain} lp={lp}->{p.LifePoints}");
            }
            {
                // Turned face-down: the switch ends (RESET_TURN_SET).
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var wc = FaceUpSt(e, p, WeaponChange, 0);
                var gardna = PlaceMonster(e, p, BigShieldGardna, 0, BattlePosition.Attack, true);
                EndTurn(e);
                EndTurn(e);
                var picked = e.PendingActivation?.Card == wc && e.TrySelectEffectTarget(gardna);
                var switched = gardna.CurrentAtk == 2600;
                gardna.FaceUp = false;
                gardna.Position = BattlePosition.Defense;
                FieldSpellEffects.RefreshBoard(e);
                gardna.FaceUp = true;
                gardna.Position = BattlePosition.Attack;
                FieldSpellEffects.RefreshBoard(e);
                Check("Weapon Change: a monster turned face-down loses the switch",
                    picked && switched && gardna.CurrentAtk == 100 && gardna.CurrentDef == 2600,
                    $"picked={picked} switched={switched} now={gardna.CurrentAtk}/{gardna.CurrentDef}");
            }
            {
                // Negated Weapon Change, too few LP: nothing is offered.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var p = e.Player;
                var wc = FaceUpSt(e, p, WeaponChange, 0);
                var gardna = PlaceMonster(e, p, BigShieldGardna, 0, BattlePosition.Attack, true);
                wc.IsNegated = true;
                EndTurn(e);
                EndTurn(e);
                var negatedQuiet = e.TurnPlayer == p && e.PendingActivation?.Card != wc && gardna.CurrentAtk == 100;
                if (e.PendingActivation != null) e.CancelEffectTargeting();
                wc.IsNegated = false;
                p.LifePoints = 600;
                ToPlayerMain1(e);
                EndTurn(e);
                EndTurn(e);
                var poorQuiet = e.TurnPlayer == p && e.PendingActivation?.Card != wc && p.LifePoints == 600;
                Check("Weapon Change: a negated copy, or a player with less than 700 LP, is offered nothing",
                    negatedQuiet && poorQuiet, $"negated={negatedQuiet} poor={poorQuiet} lp={p.LifePoints}");
            }
            {
                // The AI uses it only when the switch raises a monster's ATK.
                var e = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(e);
                ClearBoard(e);
                var o = e.Opponent;
                o.LifePoints = 8000;
                FaceUpSt(e, o, WeaponChange, 0);
                var gardna = PlaceMonster(e, o, BigShieldGardna, 0, BattlePosition.Attack, true);
                EndTurn(e); // your turn
                EndTurn(e); // the AI's Standby Phase
                var used = e.TurnPlayer == o && o.LifePoints == 7300 && gardna.CurrentAtk == 2600;

                var e2 = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(e2);
                ClearBoard(e2);
                var o2 = e2.Opponent;
                o2.LifePoints = 8000;
                FaceUpSt(e2, o2, WeaponChange, 0);
                var celtic = PlaceMonster(e2, o2, Celtic, 0, BattlePosition.Attack, true);
                EndTurn(e2);
                EndTurn(e2);
                var declined = e2.TurnPlayer == o2 && o2.LifePoints == 8000 && celtic.CurrentAtk == 1400;
                Check("Weapon Change (AI): switches Big Shield Gardna (100/2600), declines for Celtic Guardian (1400/1200)",
                    used && declined,
                    $"used={used} lp={o.LifePoints} atk={gardna.CurrentAtk}; declined={declined} lp2={o2.LifePoints}");
            }
        }

        // ───────────────────────────── Dora of Fate ─────────────────────────────

        static void DoraDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Your own turn: illegal.
                var e = PlayerMain1(db, pDeck, aDeck, out _);
                var dora = PlaceSetTrap(e, e.Player, DoraOfFate, 0);
                PlaceMonster(e, e.Opponent, DarkBlade, 0, BattlePosition.Attack, true);
                Check("Dora of Fate: cannot be activated during your own turn",
                    !e.CanActivateSpellTrap(e.Player, dora, fromHand: false));
            }
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.LifePoints = 8000;
                var kuriboh = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);
                var dora = PlaceSetTrap(e, p, DoraOfFate, 0);
                var onlyLevelOne = !e.CanActivateSpellTrap(p, dora, fromHand: false);
                var blade = PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
                var ok = e.TryActivateSpellTrap(p, dora, fromHand: false);
                var pend = e.PendingActivation;
                var legal = pend == null || (pend.LegalTargets.Contains(blade) && !pend.LegalTargets.Contains(kuriboh));
                if (pend != null) e.TrySelectEffectTarget(blade);
                Settle(e);
                Check("Dora of Fate: a Level 1 monster is not a legal target; it targets the Level 4 Dark Blade",
                    reached && onlyLevelOne && ok && legal && p.Graveyard.Contains(dora) && o.LifePoints == 8000,
                    $"reached={reached} onlyLv1Refused={onlyLevelOne} ok={ok} legal={legal} gy={p.Graveyard.Contains(dora)}");

                var main = ToPlayerMain1(e);
                var stone = PutInHand(e, p, GiantSoldierOfStone); // Level 3
                var summoned = e.TryNormalSummon(p, stone, false);
                Settle(e);
                Check("Dora of Fate: Normal Summoning a Level 3 monster in your next turn inflicts 2000 to the opponent",
                    main && summoned && o.LifePoints == 6000, $"main={main} summoned={summoned} oppLp={o.LifePoints}");

                p.NormalSummonUsed = false;
                var stone2 = PutInHand(e, p, GiantSoldierOfStone);
                e.TryNormalSummon(p, stone2, false);
                Settle(e);
                Check("Dora of Fate: it fires only once", o.LifePoints == 6000, $"oppLp={o.LifePoints}");
            }
            {
                // A Set, a wrong Level, and the turn after next do nothing; two Doras stack.
                var e = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.LifePoints = 8000;
                PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
                var d1 = PlaceSetTrap(e, p, DoraOfFate, 0);
                var d2 = PlaceSetTrap(e, p, DoraOfFate, 1);
                var ok1 = ActivateDora(e, p, d1, null);
                var ok2 = ActivateDora(e, p, d2, null);
                var main = ToPlayerMain1(e);
                var set = PutInHand(e, p, GiantSoldierOfStone);
                e.TryNormalSummon(p, set, true);
                Settle(e);
                var setQuiet = o.LifePoints == 8000;
                p.NormalSummonUsed = false;
                var lv4 = PutInHand(e, p, Celtic);
                e.TryNormalSummon(p, lv4, false);
                Settle(e);
                var wrongQuiet = o.LifePoints == 8000;
                p.NormalSummonUsed = false;
                var lv3 = PutInHand(e, p, FlameManipulator);
                e.TryNormalSummon(p, lv3, false);
                Settle(e);
                Check("Dora of Fate: a Set and a Level 4 Summon do nothing; two Doras on a Level 4 both burn (4000)",
                    ok1 && ok2 && main && setQuiet && wrongQuiet && o.LifePoints == 4000,
                    $"ok1={ok1} ok2={ok2} main={main} set={setQuiet} wrong={wrongQuiet} oppLp={o.LifePoints}");
            }
            {
                // Expires at the end of your next turn; a Tribute Summon counts.
                var e = Fresh(db, pDeck, aDeck);
                ReachOpponentMain(e);
                ClearBoard(e);
                var p = e.Player;
                var o = e.Opponent;
                p.Hand.Clear();
                o.LifePoints = 8000;
                PlaceMonster(e, o, DarkBlade, 0, BattlePosition.Attack, true);
                var magician = PlaceMonster(e, o, DarkMagician, 1, BattlePosition.Attack, true);
                var d1 = PlaceSetTrap(e, p, DoraOfFate, 0);
                var d2 = PlaceSetTrap(e, p, DoraOfFate, 1);
                var armed = ActivateDora(e, p, d1, magician);
                var main = ToPlayerMain1(e);
                var fodder = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var skull = PutInHand(e, p, SummonedSkull); // Level 6, 1 Tribute
                e.PendingTributes.Add(fodder);
                var tributed = e.TryNormalSummon(p, skull, false);
                Settle(e);
                var tributeBurn = o.LifePoints == 8000 - 3500;

                // Dora activated in the next opponent's turn on Dark Blade, then the Player skips a turn.
                ToOpponentMain(e);
                var lp = o.LifePoints;
                var blade = o.MonstersOnField().FirstOrDefault(m => m != null && m.CardId == DarkBlade);
                var armed2 = ActivateDora(e, p, d2, blade);
                var skipped = ToPlayerMain1(e);
                ToOpponentMain(e);
                var later = ToPlayerMain1(e);
                p.NormalSummonUsed = false;
                var late = PutInHand(e, p, FlameManipulator);
                e.TryNormalSummon(p, late, false);
                Settle(e);
                Check("Dora of Fate: a Tribute Summon of a Level 6 on a Level 7 burns 3500; a summon after your next turn " +
                      "does nothing",
                    armed && main && tributed && tributeBurn && armed2 && skipped && later && o.LifePoints == lp,
                    $"armed={armed} main={main} tributed={tributed} tributeBurn={tributeBurn} armed2={armed2} " +
                    $"skipped={skipped} later={later} oppLp={lp}->{o.LifePoints}");
            }
        }

        /// <summary>Activate a Set Dora of Fate in the opponent's turn; answer its target pick (or a lone target).</summary>
        static bool ActivateDora(DuelEngine e, DuelistState p, CardInstance dora, CardInstance target)
        {
            var ok = e.TryActivateSpellTrap(p, dora, false);
            if (e.PendingActivation != null)
            {
                var pick = target ?? e.PendingActivation.LegalTargets.FirstOrDefault();
                if (pick != null) e.TrySelectEffectTarget(pick);
            }

            Settle(e);
            return ok && p.Graveyard.Contains(dora) && e.PendingActivation == null;
        }

        static void ToOpponentMain(DuelEngine engine)
        {
            for (var i = 0; i < 6 && !(engine.TurnPlayer == engine.Opponent && engine.Phase == DuelPhase.Main1); i++)
            {
                if (engine.IsAwaitingEffectTarget) engine.CancelEffectTargeting();
                EndTurn(engine);
            }
        }
    }
}
