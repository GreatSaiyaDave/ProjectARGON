using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "BattleRules" (v161): Cave Dragon, Thunder Nyan Nyan, Array of Revealing Light, After the Struggle,
    /// The Secret of the Bandit, Patrician of Darkness — summon and attack conditions, a self-destroy state, a
    /// declared-Type summon-turn attack lock, an end-of-Battle-Phase sweep, a turn-scoped battle-damage watcher and the
    /// defender choosing the attack targets. Compile shapes, mini-duels through the engine entry points, near misses.
    /// </summary>
    public static class BattleRulesRegressionTests
    {
        const int CaveDragon = 93220472;
        const int ThunderNyanNyan = 70797118;
        const int ArrayOfRevealingLight = 69296555;
        const int AfterTheStruggle = 25345186;
        const int SecretOfTheBandit = 99351431;
        const int PatricianOfDarkness = 19153634;

        const int Bewd = 89631139;          // Dragon LIGHT 3000/2500
        const int WingedDragon = 87796900;  // Dragon WIND 1400/1200
        const int Celtic = 91152256;        // Warrior EARTH 1400/1200
        const int MysticalElf = 15025844;   // Spellcaster LIGHT 800/2000
        const int SummonedSkull = 70781052; // Fiend DARK 2500/1200

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("BattleRules: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(Check);
            CaveDragonDuels(db, pDeck, aDeck, Check);
            ThunderNyanNyanDuels(db, pDeck, aDeck, Check);
            ArrayDuels(db, pDeck, aDeck, Check);
            AfterTheStruggleDuels(db, pDeck, aDeck, Check);
            BanditDuels(db, pDeck, aDeck, Check);
            PatricianDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static string Dump(CompiledCardProgram prog) =>
            prog == null ? "null" : string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe));

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            {
                var def = db.Get(CaveDragon);
                var prog = CardTextEffectCompiler.Compile(def);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                Check("Cave Dragon: FullyCompiled — Normal Summon condition + attack condition (another face-up Dragon)",
                    prog != null && prog.FullyCompiled && cl.Count == 2 &&
                    cl[0].Action == EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster &&
                    !cl[0].MakesChainLink &&
                    cl[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                    cl[1].Action == EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace &&
                    cl[1].RaceFilter == "Dragon" && !cl[1].MakesChainLink,
                    Dump(prog));
                Check("Cave Dragon: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(ThunderNyanNyan);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Thunder Nyan Nyan: FullyCompiled — self-destroy state while you control a face-up non-LIGHT monster",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute &&
                    c.AttributeFilter == "LIGHT" && !c.MakesChainLink,
                    Dump(prog));
                Check("Thunder Nyan Nyan: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(ArrayOfRevealingLight);
                var prog = CardTextEffectCompiler.Compile(def);
                var cl = prog?.ClauseList ?? new List<EffectClause>();
                Check("Array of Revealing Light: FullyCompiled — declare a Type (no target) + summon-turn attack lock on that Type",
                    prog != null && prog.FullyCompiled && cl.Count == 2 &&
                    cl[0].Timing == EffectTiming.Activate && cl[0].Action == EffectActionKind.DeclareMonsterType &&
                    cl[0].ChoiceDoesNotTarget && !cl[0].IsPsctTarget &&
                    cl[0].Zone == EffectZoneFilter.DeclaredMonsterTypes &&
                    cl[1].Timing == EffectTiming.ContinuousWhileFaceUp &&
                    cl[1].Action == EffectActionKind.ContinuousCannotAttack && cl[1].RaceFilterIsDeclared &&
                    cl[1].RequiresSummonedOrFlippedThisTurn && cl[1].Side == EffectSide.Both &&
                    string.IsNullOrEmpty(cl[1].RaceFilter) && cl[1].Amount == 0,
                    Dump(prog));
                Check("Array of Revealing Light: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(AfterTheStruggle);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("After the Struggle: FullyCompiled — Main Phase 1 only, untargeted end-of-Battle-Phase sweep",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase &&
                    c.OnlyDuringMainPhase1 && !c.RequiresTargetChoice && c.MakesChainLink,
                    Dump(prog));
                Check("After the Struggle: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(SecretOfTheBandit);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("The Secret of the Bandit: FullyCompiled — Main Phase 1 only, targets 1 face-up monster on the field",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn &&
                    c.OnlyDuringMainPhase1 && c.RequiresTargetChoice && c.IsPsctTarget && c.RequiresTargetFaceUp &&
                    c.Zone == EffectZoneFilter.FieldAnyMonster && c.TargetCount == 1 && c.MakesChainLink,
                    Dump(prog));
                Check("The Secret of the Bandit: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            {
                var def = db.Get(PatricianOfDarkness);
                var prog = CardTextEffectCompiler.Compile(def);
                var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
                Check("Patrician of Darkness: FullyCompiled — the defender chooses the opponent's attack targets",
                    prog != null && prog.FullyCompiled && c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                    c.Action == EffectActionKind.YouChooseOpponentsAttackTargets && !c.MakesChainLink &&
                    !c.RequiresTargetChoice,
                    Dump(prog));
                Check("Patrician of Darkness: classified Implemented",
                    CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented, CardEffectStatus.Classify(def).ToString());
            }

            foreach (var (a, want) in new[]
                     {
                         (EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster, EffectResolutionKind.Protection),
                         (EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace, EffectResolutionKind.Protection),
                         (EffectActionKind.YouChooseOpponentsAttackTargets, EffectResolutionKind.Protection),
                         (EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute, EffectResolutionKind.Destroy),
                         (EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase, EffectResolutionKind.Destroy),
                         (EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn,
                             EffectResolutionKind.Discard)
                     })
            {
                var kind = EffectVocabulary.ResolutionOf(new EffectClause { Action = a });
                Check($"{a} maps to the shared resolution {want}", kind == want, kind.ToString());
            }
        }

        // ───────────────────────── near misses ─────────────────────────

        static CompiledCardProgram CompileMonster(string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990161, name = "Near-miss Monster", type = "Effect Monster", frameType = "effect", race = "Dragon",
                attribute = "WIND", level = 4, atk = 2000, def = 100, desc = text
            });

        static CompiledCardProgram CompileSpell(string text, string race = "Normal") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990162, name = "Near-miss Spell", type = "Spell Card", frameType = "spell", race = race, desc = text
            });

        static bool Has(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == a);

        static bool FullyWith(CompiledCardProgram prog, EffectActionKind a) =>
            prog != null && prog.FullyCompiled && Has(prog, a);

        static void NearMisses(CheckFn Check)
        {
            Check("Near miss: a Normal Summon condition naming a Type is not Cave Dragon's",
                !Has(CompileMonster("Cannot be Normal Summoned while you control a Dragon-Type monster."),
                    EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster));
            Check("Near miss: an attack condition with an added cost is not compiled",
                !FullyWith(CompileMonster("This card cannot declare an attack unless you control another Dragon-Type " +
                                          "monster and pay 500 Life Points."),
                    EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace));
            Check("Near miss: a self-destroy reading any non-LIGHT monster on the field (not only yours) is not compiled",
                !Has(CompileMonster("If there is a non-LIGHT monster on the field, destroy this face-up card."),
                    EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute));
            Check("Near miss: a declared-Type lock for Special Summons only is not Array of Revealing Light",
                !FullyWith(CompileSpell("Declare 1 Type of monster. Any monster of the declared Type cannot declare an " +
                                        "attack during the turn it is Special Summoned.", "Field"),
                    EffectActionKind.DeclareMonsterType));
            Check("Near miss: the After the Struggle sweep without its Main Phase 1 condition is not compiled",
                !Has(CompileSpell("All monsters on both sides of the field that have been involved in damage " +
                                  "calculation are destroyed during the End Step of the turn."),
                    EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase));
            Check("Near miss: a Bandit that discards 2 random cards is not compiled",
                !Has(CompileSpell("This card can only be activated during Main Phase 1. Select 1 face-up monster on " +
                                  "the field. Each time the selected monster inflicts Battle Damage to your opponent's " +
                                  "Life Points this turn, your opponent discards 2 random cards."),
                    EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn));
            Check("Near miss: a Patrician limited to this turn is not compiled",
                !Has(CompileMonster("You choose the attack targets for your opponent's attacks this turn."),
                    EffectActionKind.YouChooseOpponentsAttackTargets));
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>Advance to <paramref name="who"/>'s Main Phase 1 on game turn ≥ <paramref name="minTurn"/>.</summary>
        static bool ReachMain(DuelEngine engine, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                if (engine.IsAwaitingResponse) engine.PassResponse();
                engine.RecoverStuckCombat();
                if (engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn)
                    return true;
                engine.TryEndTurnSafe(engine.TurnPlayer);
            }

            if (engine.IsAwaitingResponse) engine.PassResponse();
            return engine.TurnPlayer == who && engine.Phase == DuelPhase.Main1 && engine.TurnNumber >= minTurn;
        }

        /// <summary>A fresh duel at the player's Main Phase 1 (turn 3+), empty board, both hands empty.</summary>
        static DuelEngine PlayerMain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            reached = ReachMain(engine, engine.Player, 3);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        /// <summary>A fresh duel at the opponent's Battle Phase, empty board, both hands empty.</summary>
        static DuelEngine OpponentBattle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = Fresh(db, pDeck, aDeck);
            ClearBoard(engine);
            reached = ReachOpponentBattle(engine);
            ClearBoard(engine);
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static bool EnterBattle(DuelEngine engine, DuelistState who)
        {
            PassAll(engine);
            engine.TryEnterBattlePhase(who);
            PassAll(engine);
            DrainCombat(engine);
            return engine.Phase == DuelPhase.Battle && engine.TurnPlayer == who;
        }

        static bool Attack(DuelEngine engine, DuelistState who, CardInstance attacker, CardInstance target)
        {
            PassAll(engine);
            var ok = engine.TryAttack(who, attacker, target);
            DrainCombat(engine);
            return ok;
        }

        static void ToMainPhase2(DuelEngine engine, DuelistState who)
        {
            engine.TryEnterMainPhase2(who);
            PassAll(engine);
        }

        static bool OnField(DuelistState who, CardInstance c) => who.TryFindMonster(c, out _);

        /// <summary>The target of <paramref name="attacker"/>'s latest attack declaration (the battle may already be over).</summary>
        static CardInstance LastAttackTarget(DuelEngine engine, CardInstance attacker) =>
            engine.GameEvents.Events.LastOrDefault(e => e.Kind == DuelGameEventKind.DeclaredAttack &&
                                                        ReferenceEquals(e.Card, attacker)).Related;

        // ───────────────────────── Cave Dragon ─────────────────────────

        static void CaveDragonDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var setElf = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Defense, false);
                var cave = PutInHand(engine, p, CaveDragon);
                var summon = SummonProcedures.CheckNormalOrTribute(engine, p, cave, asSet: false);
                var summoned = engine.TryNormalSummon(p, cave, asSet: false);
                Check("Cave Dragon: cannot be Normal Summoned while you control a monster (even a face-down one)",
                    reached && !summon.Legal && !summoned && p.Hand.Contains(cave) && !p.NormalSummonUsed,
                    $"reached={reached} legal={summon.Legal} reason={summon.Reason} summoned={summoned}");
                var set = engine.TryNormalSummon(p, cave, asSet: true);
                Check("Cave Dragon: it can still be Set while you control a monster",
                    set && OnField(p, cave) && !cave.FaceUp && OnField(p, setElf),
                    $"set={set} faceUp={cave.FaceUp}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, engine.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var cave = PutInHand(engine, p, CaveDragon);
                var summoned = engine.TryNormalSummon(p, cave, asSet: false);
                PassAll(engine);
                Check("Cave Dragon: with no monster of your own it can be Normal Summoned (the opponent's do not count)",
                    reached && summoned && OnField(p, cave) && cave.FaceUp,
                    $"reached={reached} summoned={summoned}");
            }

            {
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                var p = engine.Player;
                var cave = PlaceMonster(engine, o, CaveDragon, 0, BattlePosition.Attack, true);
                var prey = PlaceMonster(engine, p, MysticalElf, 0, BattlePosition.Attack, true);
                var alone = engine.CanAttack(o, cave);
                var setDragon = PlaceMonster(engine, o, WingedDragon, 1, BattlePosition.Defense, false);
                var withSet = engine.CanAttack(o, cave);
                var warrior = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var withWarrior = engine.CanAttack(o, cave);
                Check("Cave Dragon: cannot declare an attack without another face-up Dragon (a Set Dragon or a Warrior does not count)",
                    reached && !alone && !withSet && !withWarrior && !engine.TryAttack(o, cave, prey),
                    $"reached={reached} alone={alone} withSet={withSet} withWarrior={withWarrior}");
                setDragon.FaceUp = true;
                setDragon.Position = BattlePosition.Attack;
                var withDragon = engine.CanAttack(o, cave);
                var attacked = Attack(engine, o, cave, prey);
                Check("Cave Dragon: with another face-up Dragon it attacks",
                    withDragon && attacked && p.Graveyard.Contains(prey),
                    $"withDragon={withDragon} attacked={attacked} preyInGy={p.Graveyard.Contains(prey)}");
                Check("Cave Dragon: a face-up Warrior alone never unlocks it", OnField(o, warrior));
            }
        }

        // ───────────────────────── Thunder Nyan Nyan ─────────────────────────

        static void ThunderNyanNyanDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var engine = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var nyan = PlaceMonster(engine, p, ThunderNyanNyan, 0, BattlePosition.Attack, true);
            PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Defense, true);
            PlaceMonster(engine, p, Celtic, 2, BattlePosition.Defense, false);
            PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
            engine.NotifyPublic();
            Check("Thunder Nyan Nyan: stays with a LIGHT monster, a face-down non-LIGHT and the opponent's DARK monster",
                reached && OnField(p, nyan) && nyan.FaceUp, $"reached={reached} onField={OnField(p, nyan)}");
            var wind = PutInHand(engine, p, WingedDragon);
            var summoned = engine.TryNormalSummon(p, wind, asSet: false);
            PassAll(engine);
            engine.NotifyPublic();
            Check("Thunder Nyan Nyan: destroyed once you Normal Summon a face-up WIND monster",
                summoned && OnField(p, wind) && !OnField(p, nyan) && p.Graveyard.Contains(nyan) && !engine.IsAwaitingEffectTarget,
                $"summoned={summoned} nyanOnField={OnField(p, nyan)} inGy={p.Graveyard.Contains(nyan)}");

            var engine2 = PlayerMain(db, pDeck, aDeck, out var reached2);
            var p2 = engine2.Player;
            var nyan2 = PlaceMonster(engine2, p2, ThunderNyanNyan, 0, BattlePosition.Attack, true);
            var setCeltic = PlaceMonster(engine2, p2, Celtic, 1, BattlePosition.Defense, false);
            setCeltic.SetThisTurn = false;
            engine2.NotifyPublic();
            var before = OnField(p2, nyan2);
            var flipped = engine2.TryChangePosition(p2, setCeltic) || engine2.TryFlipSummon(p2, setCeltic);
            PassAll(engine2);
            engine2.NotifyPublic();
            Check("Thunder Nyan Nyan: destroyed when your Set EARTH monster is flipped face-up",
                reached2 && before && flipped && setCeltic.FaceUp && p2.Graveyard.Contains(nyan2),
                $"before={before} flipped={flipped} celticFaceUp={setCeltic.FaceUp} inGy={p2.Graveyard.Contains(nyan2)}");
        }

        // ───────────────────────── Array of Revealing Light ─────────────────────────

        static CardInstance PendingToken(DuelEngine engine, string name) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t =>
                t?.Def != null && string.Equals(t.Def.name, name, StringComparison.OrdinalIgnoreCase));

        static void ArrayDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var engine = PlayerMain(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var o = engine.Opponent;
            var oldDragon = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
            var setDragon = PlaceMonster(engine, p, Bewd, 1, BattlePosition.Defense, false);
            var prey = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
            PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, true);
            PlaceMonster(engine, o, MysticalElf, 2, BattlePosition.Defense, true);
            var array = PutInHand(engine, p, ArrayOfRevealingLight);
            var activated = engine.TryActivateSpellTrap(p, array, fromHand: true);
            var token = PendingToken(engine, "Dragon");
            var declared = token != null && engine.TrySelectEffectTarget(token);
            PassAll(engine);
            Check("Array of Revealing Light: activated from the hand, declares Dragon and stays face-up in the Field Zone",
                reached && activated && declared && array.FaceUp && p.FieldSpellZone?.Occupant == array &&
                TypeDeclarationRuntime.DeclaredTypeOf(array) == "Dragon" && !engine.IsAwaitingEffectTarget,
                $"reached={reached} activated={activated} declared={declared} zone={p.FieldSpellZone?.Occupant?.Name} " +
                $"type={TypeDeclarationRuntime.DeclaredTypeOf(array)}");

            var winged = PutInHand(engine, p, WingedDragon);
            var normal = engine.TryNormalSummon(p, winged, asSet: false);
            PassAll(engine);
            var flip = engine.TryFlipSummon(p, setDragon);
            PassAll(engine);
            var warrior = PutInHand(engine, p, Celtic);
            p.NormalSummonUsed = false;
            var normalWarrior = engine.TryNormalSummon(p, warrior, asSet: false);
            PassAll(engine);
            var inBattle = EnterBattle(engine, p);
            Check("Array of Revealing Light: a Dragon Normal or Flip Summoned this turn cannot declare an attack",
                normal && flip && inBattle && !engine.CanAttack(p, winged) && !engine.CanAttack(p, setDragon) &&
                !engine.TryAttack(p, winged, prey),
                $"normal={normal} flip={flip} battle={inBattle} wingedCan={engine.CanAttack(p, winged)} " +
                $"flipCan={engine.CanAttack(p, setDragon)}");
            Check("Array of Revealing Light: a Dragon already on the field and a Warrior Summoned this turn can attack",
                normalWarrior && engine.CanAttack(p, oldDragon) && engine.CanAttack(p, warrior),
                $"normalWarrior={normalWarrior} oldCan={engine.CanAttack(p, oldDragon)} warriorCan={engine.CanAttack(p, warrior)}");
            var hit = Attack(engine, p, oldDragon, prey);
            Check("Array of Revealing Light: the old Dragon's attack goes through", hit && o.Graveyard.Contains(prey),
                $"hit={hit}");

            // Next turn of the player: the Dragon Summoned two turns ago is free again.
            var next = ReachMain(engine, p, engine.TurnNumber + 1);
            ClearBoard(engine);
            p.FieldSpellZone.Occupant = array;
            var winged2 = PlaceMonster(engine, p, WingedDragon, 0, BattlePosition.Attack, true);
            var ss = PlaceMonster(engine, p, WingedDragon, 1, BattlePosition.Attack, true);
            ss.SummonedThisTurn = true; // a Special Summon this turn (DuelEngine.SpecialSummonToField sets it)
            var battle2 = EnterBattle(engine, p);
            Check("Array of Revealing Light: next turn a Dragon not Summoned that turn attacks; one Special Summoned that turn cannot",
                next && battle2 && engine.CanAttack(p, winged2) && !engine.CanAttack(p, ss),
                $"next={next} battle={battle2} can={engine.CanAttack(p, winged2)} ssCan={engine.CanAttack(p, ss)}");

            {
                // CANCEL on the declaration undoes the activation: nothing was placed.
                var e2 = PlayerMain(db, pDeck, aDeck, out var r2);
                var p2 = e2.Player;
                var arr = PutInHand(e2, p2, ArrayOfRevealingLight);
                var started = e2.TryActivateSpellTrap(p2, arr, fromHand: true);
                var pa = e2.PendingActivation;
                var asks = e2.IsAwaitingEffectTarget && pa != null && pa.Controller == p2 &&
                           pa.TargetKind == EffectTargetKind.DeclareMonsterType && pa.LegalTargets.Count >= 20 &&
                           pa.LegalTargets.All(TypeDeclarationRuntime.IsTypeToken) && p2.FieldSpellZone?.Occupant == null;
                var cancelled = e2.CancelEffectTargeting();
                Check("Array of Revealing Light: the Type is declared before the card is placed; CANCEL undoes the activation",
                    r2 && started && asks && cancelled && p2.Hand.Contains(arr) && p2.FieldSpellZone?.Occupant == null &&
                    !e2.IsAwaitingEffectTarget && e2.Chain.Count == 0 && TypeDeclarationRuntime.DeclaredTypeOf(arr) == null,
                    $"reached={r2} started={started} asks={asks} cancelled={cancelled} inHand={p2.Hand.Contains(arr)} " +
                    $"zone={p2.FieldSpellZone?.Occupant?.Name}");
            }

            {
                // v161 cross-group audit: Chain Energy's toll refunded by that CANCEL was never paid, so Life Absorbing
                // Machine (ContinuousTrapsAndSpiritSupport) must not count it as an LP cost paid this turn.
                var e2 = PlayerMain(db, pDeck, aDeck, out var r2);
                var p2 = e2.Player;
                var ce = PlaceSetTrap(e2, p2, 79323590, 0); // Chain Energy
                ce.FaceUp = true;
                var lp0 = p2.LifePoints;
                var arr = PutInHand(e2, p2, ArrayOfRevealingLight);
                var started = e2.TryActivateSpellTrap(p2, arr, fromHand: true);
                var paid = lp0 - p2.LifePoints;
                var cancelled = e2.CancelEffectTargeting();
                ContinuousTrapsAndSpiritSupportRuntime.OnTurnBegin(e2, p2); // this turn becomes "your last turn"
                var counted = ContinuousTrapsAndSpiritSupportRuntime.CostsPaidLastTurn(e2, p2).Count;
                Check("Array of Revealing Light + Chain Energy: the toll refunded on CANCEL is not an LP cost for Life Absorbing Machine",
                    r2 && started && paid == 500 && cancelled && p2.LifePoints == lp0 && counted == 0,
                    $"reached={r2} started={started} paid={paid} cancelled={cancelled} lp={p2.LifePoints}/{lp0} counted={counted}");
            }

            {
                // The AI activates it: it declares as the card is placed — here the Type of the player's face-up monster.
                var e3 = Fresh(db, pDeck, aDeck);
                var r3 = ReachMain(e3, e3.Opponent, 2);
                ClearBoard(e3);
                e3.Player.Hand.Clear();
                e3.Opponent.Hand.Clear();
                PlaceMonster(e3, e3.Player, Celtic, 0, BattlePosition.Attack, true);
                var arr = PutInHand(e3, e3.Opponent, ArrayOfRevealingLight);
                var act = e3.TryActivateSpellTrap(e3.Opponent, arr, fromHand: true);
                PassAll(e3);
                Check("Array of Revealing Light (AI): declares a Type as it is activated, no prompt left open",
                    r3 && act && e3.Opponent.FieldSpellZone?.Occupant == arr &&
                    TypeDeclarationRuntime.DeclaredTypeOf(arr) == "Warrior" && !e3.IsAwaitingEffectTarget,
                    $"reached={r3} act={act} zone={e3.Opponent.FieldSpellZone?.Occupant?.Name} " +
                    $"type={TypeDeclarationRuntime.DeclaredTypeOf(arr)} awaiting={e3.IsAwaitingEffectTarget}");
            }

            {
                // Placed straight in the Field Zone (the AR disk path): the player declares right after it is placed.
                var e4 = PlayerMain(db, pDeck, aDeck, out var r4);
                var p4 = e4.Player;
                var arr = PutInHand(e4, p4, ArrayOfRevealingLight);
                var placed = e4.TryPlaceFieldSpell(p4, arr);
                var tok = PendingToken(e4, "Fiend");
                var picked = tok != null && e4.TrySelectEffectTarget(tok);
                Check("Array of Revealing Light: placed directly in the Field Zone, the player declares the Type there",
                    r4 && placed && picked && p4.FieldSpellZone?.Occupant == arr &&
                    TypeDeclarationRuntime.DeclaredTypeOf(arr) == "Fiend" && !e4.IsAwaitingEffectTarget,
                    $"reached={r4} placed={placed} picked={picked} type={TypeDeclarationRuntime.DeclaredTypeOf(arr)}");
            }
        }

        // ───────────────────────── After the Struggle ─────────────────────────

        static void AfterTheStruggleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            foreach (var withCard in new[] { true, false })
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var idle = PlaceMonster(engine, p, WingedDragon, 1, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, true);
                var skull = PlaceMonster(engine, o, SummonedSkull, 1, BattlePosition.Defense, false);
                var activated = true;
                CardInstance card = null;
                if (withCard)
                {
                    card = PutInHand(engine, p, AfterTheStruggle);
                    activated = engine.TryActivateSpellTrap(p, card, fromHand: true);
                    PassAll(engine);
                }

                var battle = EnterBattle(engine, p);
                var attacked = Attack(engine, p, celtic, elf);
                var bothAfterCalc = OnField(p, celtic) && OnField(o, elf);
                ToMainPhase2(engine, p);
                if (withCard)
                    Check("After the Struggle: both battlers (still on the field) are destroyed at the end of the Battle Phase; " +
                          "monsters that did not battle stay",
                        reached && activated && p.Graveyard.Contains(card) && battle && attacked && bothAfterCalc &&
                        p.Graveyard.Contains(celtic) && o.Graveyard.Contains(elf) && OnField(p, idle) &&
                        OnField(o, skull) && engine.Phase == DuelPhase.Main2,
                        $"reached={reached} act={activated} battle={battle} attacked={attacked} afterCalc={bothAfterCalc} " +
                        $"celticGy={p.Graveyard.Contains(celtic)} elfGy={o.Graveyard.Contains(elf)} phase={engine.Phase}");
                else
                    Check("After the Struggle (control): without it the same battle destroys nothing",
                        battle && attacked && OnField(p, celtic) && OnField(o, elf),
                        $"battle={battle} attacked={attacked}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                ToMainPhase2(engine, p);
                var card = PutInHand(engine, p, AfterTheStruggle);
                Check("After the Struggle: cannot be activated in Main Phase 2",
                    reached && engine.Phase == DuelPhase.Main2 && !engine.CanActivateSpellTrap(p, card, fromHand: true),
                    $"phase={engine.Phase}");
            }

            {
                // Activated, but no Battle Phase battle: nothing is destroyed, and a later turn's battle is not swept.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var celtic = PlaceMonster(engine, p, Celtic, 0, BattlePosition.Attack, true);
                var card = PutInHand(engine, p, AfterTheStruggle);
                var activated = engine.TryActivateSpellTrap(p, card, fromHand: true);
                PassAll(engine);
                var next = ReachOpponentBattle(engine);
                var o = engine.Opponent;
                var atk = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Attack, true);
                celtic.Position = BattlePosition.Defense;
                var attacked = Attack(engine, o, atk, celtic);
                ToMainPhase2(engine, o);
                Check("After the Struggle: it lasts only for the turn it resolved",
                    reached && activated && next && attacked && OnField(p, celtic) && OnField(o, atk),
                    $"act={activated} next={next} attacked={attacked} celtic={OnField(p, celtic)} elf={OnField(o, atk)}");
            }
        }

        // ───────────────────────── The Secret of the Bandit ─────────────────────────

        static bool ActivateBandit(DuelEngine engine, DuelistState who, CardInstance card, CardInstance target)
        {
            if (!engine.TryActivateSpellTrap(who, card, fromHand: true)) return false;
            if (engine.IsAwaitingEffectTarget && !engine.TrySelectEffectTarget(target)) return false;
            PassAll(engine);
            return !engine.IsAwaitingEffectTarget && engine.Chain.Count == 0;
        }

        static void BanditDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var oppSet = PlaceMonster(engine, o, MysticalElf, 0, BattlePosition.Defense, false);
                var oppFaceUp = PlaceMonster(engine, o, SummonedSkull, 1, BattlePosition.Attack, true);
                for (var i = 0; i < 3; i++) PutInHand(engine, o, Celtic);
                var card = PutInHand(engine, p, SecretOfTheBandit);
                var activated = engine.TryActivateSpellTrap(p, card, fromHand: true);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                Check("The Secret of the Bandit: the targets are the face-up monsters on either field (no Set monster)",
                    reached && activated && legal.Contains(bewd) && legal.Contains(celtic) && legal.Contains(oppFaceUp) &&
                    !legal.Contains(oppSet),
                    $"reached={reached} act={activated} legal=[{string.Join(",", legal.Select(t => t?.Name))}]");
                var picked = engine.IsAwaitingEffectTarget && engine.TrySelectEffectTarget(bewd);
                PassAll(engine);
                Check("The Secret of the Bandit: resolves on the selected monster", picked && p.Graveyard.Contains(card),
                    $"picked={picked}");

                // Clear the opponent's monsters so both attacks are direct.
                o.MonsterZones[0].Occupant = null;
                o.MonsterZones[1].Occupant = null;
                var battle = EnterBattle(engine, p);
                var hand0 = o.Hand.Count;
                var gy0 = o.Graveyard.Count;
                var hit = Attack(engine, p, bewd, null);
                var hand1 = o.Hand.Count;
                Check("The Secret of the Bandit: the selected monster's battle damage makes the opponent discard 1 random card",
                    battle && hit && hand0 == 3 && hand1 == 2 && o.Graveyard.Count == gy0 + 1 && o.LifePoints <= 5000,
                    $"battle={battle} hit={hit} hand {hand0}->{hand1} lp={o.LifePoints}");
                var hit2 = Attack(engine, p, celtic, null);
                Check("The Secret of the Bandit: another monster's battle damage does not",
                    hit2 && o.Hand.Count == 2, $"hit2={hit2} hand={o.Hand.Count}");
            }

            {
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                ToMainPhase2(engine, p);
                var card = PutInHand(engine, p, SecretOfTheBandit);
                Check("The Secret of the Bandit: cannot be activated in Main Phase 2",
                    reached && engine.Phase == DuelPhase.Main2 && !engine.CanActivateSpellTrap(p, card, fromHand: true),
                    $"phase={engine.Phase}");
            }

            {
                // The selected monster is the opponent's: its battle damage hits the activator — nobody discards.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var skull = PlaceMonster(engine, o, SummonedSkull, 0, BattlePosition.Attack, true);
                for (var i = 0; i < 2; i++) PutInHand(engine, p, Celtic);
                for (var i = 0; i < 2; i++) PutInHand(engine, o, Celtic);
                BattleRulesRuntimeTestHook(engine, p, skull);
                var hit = Attack(engine, o, skull, null);
                Check("The Secret of the Bandit: damage dealt to the activator by a selected opponent's monster discards nothing",
                    reached && hit && p.Hand.Count == 2 && o.Hand.Count == 2,
                    $"reached={reached} hit={hit} pHand={p.Hand.Count} oHand={o.Hand.Count}");
            }
        }

        /// <summary>Resolve a Bandit for <paramref name="who"/> on <paramref name="target"/> mid-turn (the card itself is
        /// Main Phase 1 only; this covers the watcher's "your opponent" side on the opponent's turn).</summary>
        static void BattleRulesRuntimeTestHook(DuelEngine engine, DuelistState who, CardInstance target) =>
            BattleRulesRuntime.ArmBandit(engine, who, engine.CreateCardInstance(SecretOfTheBandit), target);

        // ───────────────────────── Patrician of Darkness ─────────────────────────

        static void PatricianDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // The AI controls Patrician: the player attacks a monster, the AI chooses which one is attacked.
                var engine = PlayerMain(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, p, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, o, PatricianOfDarkness, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, o, MysticalElf, 1, BattlePosition.Defense, true);
                var celtic = PlaceMonster(engine, o, Celtic, 2, BattlePosition.Attack, true);
                var lp = o.LifePoints;
                var battle = EnterBattle(engine, p);
                var ok = engine.TryAttack(p, bewd, celtic);
                var target = LastAttackTarget(engine, bewd);
                DrainCombat(engine);
                Check("Patrician of Darkness (AI): the defender redirects the attack to its Defense Position monster",
                    reached && battle && ok && target == elf && o.Graveyard.Contains(elf) && OnField(o, celtic) &&
                    o.LifePoints == lp && !engine.IsAwaitingEffectTarget,
                    $"reached={reached} battle={battle} ok={ok} target={target?.Name} elfGy={o.Graveyard.Contains(elf)} lp={lp}->{o.LifePoints}");
            }

            {
                // The player controls Patrician: the AI attacks, the player picks the target on the target sheet.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, o, Bewd, 0, BattlePosition.Attack, true);
                var pat = PlaceMonster(engine, p, PatricianOfDarkness, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 2, BattlePosition.Attack, true);
                var ok = engine.TryAttack(o, bewd, celtic);
                var pa = engine.PendingActivation;
                var waiting = engine.IsAwaitingEffectTarget && pa != null && pa.Controller == p &&
                              pa.LegalTargets.Contains(pat) && pa.LegalTargets.Contains(elf) &&
                              pa.LegalTargets.Contains(celtic) && pa.LegalTargets.Count == 3 && !engine.HasDeclaredAttack;
                Check("Patrician of Darkness (player): the AI's attack waits for the player to choose among every legal target",
                    reached && ok && waiting,
                    $"reached={reached} ok={ok} waiting={waiting} pending={pa?.Controller?.Name} " +
                    $"[{string.Join(",", pa?.LegalTargets.Select(t => t?.Name) ?? Array.Empty<string>())}] declared={engine.HasDeclaredAttack}");
                var picked = engine.TrySelectEffectTarget(elf);
                var target = LastAttackTarget(engine, bewd);
                DrainCombat(engine);
                Check("Patrician of Darkness (player): the attack is declared on the chosen Set monster",
                    picked && target == elf && p.Graveyard.Contains(elf) && OnField(p, celtic) && OnField(p, pat),
                    $"picked={picked} target={target?.Name} elfGy={p.Graveyard.Contains(elf)}");
            }

            {
                // Cancel: the choice is mandatory and made for the player (the monster that holds best).
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var atk = PlaceMonster(engine, o, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, PatricianOfDarkness, 0, BattlePosition.Attack, true);
                var elf = PlaceMonster(engine, p, MysticalElf, 1, BattlePosition.Attack, true);
                var ok = engine.TryAttack(o, atk, elf);
                var waiting = engine.IsAwaitingEffectTarget;
                var cancelled = engine.CancelEffectTargeting();
                var target = LastAttackTarget(engine, atk);
                DrainCombat(engine);
                Check("Patrician of Darkness (player): CANCEL makes the mandatory choice — the stronger Patrician meets the attack",
                    reached && ok && waiting && cancelled && target?.CardId == PatricianOfDarkness &&
                    o.Graveyard.Contains(atk) && OnField(p, elf),
                    $"reached={reached} ok={ok} waiting={waiting} cancel={cancelled} target={target?.Name}");
            }

            {
                // A face-down Patrician applies nothing: the attacker's choice stands.
                var engine = OpponentBattle(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var bewd = PlaceMonster(engine, o, Bewd, 0, BattlePosition.Attack, true);
                PlaceMonster(engine, p, PatricianOfDarkness, 0, BattlePosition.Defense, false);
                var celtic = PlaceMonster(engine, p, Celtic, 1, BattlePosition.Attack, true);
                var ok = engine.TryAttack(o, bewd, celtic);
                var waiting = engine.IsAwaitingEffectTarget;
                var target = LastAttackTarget(engine, bewd);
                DrainCombat(engine);
                Check("Patrician of Darkness: face-down, the attacker keeps its target",
                    reached && ok && !waiting && target == celtic && p.Graveyard.Contains(celtic),
                    $"reached={reached} ok={ok} waiting={waiting} target={target?.Name}");
            }
        }
    }
}
