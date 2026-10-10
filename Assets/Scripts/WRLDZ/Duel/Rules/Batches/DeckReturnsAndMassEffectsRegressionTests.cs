using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "DeckReturnsAndMassEffects" (v161 LOD), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item>Fiber Jar c78706415: FLIP; hand, field and GY of both players to the Deck (g:Remove STATUS_BATTLE_DESTROYED;
    /// Extra Deck monsters to the Extra Deck; banished cards stay), shuffle, then the controller draws 5, then the
    /// opponent.</item>
    /// <item>Mysterious Guard c1347977: FLIP, targets; the first to the top of the Deck, then the additional one (only
    /// with a face-up Warrior) to the hand if a face-up Warrior is still controlled.</item>
    /// <item>Serpentine Princess c71829750: EVENT_TO_DECK, previously face-up on the field; mandatory; Special Summon a
    /// Level 3 or lower monster from the Deck.</item>
    /// <item>Creature Swap c31036355: each player needs a monster; each chooses 1 they control (activator first);
    /// SwapControl; EFFECT_CANNOT_CHANGE_POSITION until the End Phase.</item>
    /// <item>Bubble Crash c61622107: a player with 6+ cards in hand + field; each such player sends cards (not Bubble
    /// Crash) until 5 remain (Bubble Crash counts while on the field).</item>
    /// </list>
    /// </summary>
    public static class DeckReturnsAndMassEffectsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int FiberJar = 78706415;
        const int MysteriousGuard = 1347977;
        const int Serpentine = 71829750;
        const int CreatureSwap = 31036355;
        const int BubbleCrash = 61622107;

        const int Celtic = 91152256;          // Warrior Lv4 1400/1200
        const int Gaia = 6368038;             // Warrior Lv7 2300/2100
        const int MysticalElf = 15025844;     // Spellcaster Lv4 800/2000
        const int Bewd = 89631139;            // Dragon Lv8 3000/2500
        const int GiantSoldier = 13039848;    // Rock Lv3 1300/2000
        const int SilverFang = 90357090;      // Beast Lv3 1200/800
        const int PetitDragon = 75356564;     // Dragon Lv2 600/700
        const int Kuriboh = 40640057;         // Fiend Lv1 300/200
        const int FlameSwordsman = 45231177;  // Fusion
        const int TrapHole = 4206964;
        const int PotOfGreed = 55144522;
        const int Mountain = 50913601;
        const int CurseOfFiend = 12470447;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DeckReturnsAndMassEffects: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);

            FiberJarShufflesEverythingThenDraws(db, pDeck, aDeck, Check);
            FiberJarDestroyedByBattleStaysInGy(db, pDeck, aDeck, Check);
            FiberJarNecrovalleyKeepsGraveyards(db, pDeck, aDeck, Check);

            GuardTwoTargetsWithWarrior(db, pDeck, aDeck, Check);
            GuardOneTargetWithoutWarrior(db, pDeck, aDeck, Check);
            GuardWarriorReturnedFirstKeepsSecond(db, pDeck, aDeck, Check);
            GuardAi(db, pDeck, aDeck, Check);
            GuardOnASegocChain(db, pDeck, aDeck, Check);
            GuardLoneFaceUpTargetGoesToDeck(db, pDeck, aDeck, Check);

            SerpentineReturnedByAiGuard(db, pDeck, aDeck, Check);
            SerpentineReturnedByFiberJar(db, pDeck, aDeck, Check);
            SerpentineAiReturnedByPlayerGuard(db, pDeck, aDeck, Check);
            SerpentineSetIsNoTrigger(db, pDeck, aDeck, Check);

            CreatureSwapPlayer(db, pDeck, aDeck, Check);
            CreatureSwapAiActivates(db, pDeck, aDeck, Check);
            CreatureSwapNeedsAMonsterEachSide(db, pDeck, aDeck, Check);

            BubbleCrashPlayer(db, pDeck, aDeck, Check);
            BubbleCrashCondition(db, pDeck, aDeck, Check);
            BubbleCrashAiActivates(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static (CompiledCardProgram prog, CardEffectStatusKind st) Of(CardDatabase db, int id)
        {
            var def = db.Get(id);
            var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
            return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
        }

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var (p, st) = Of(db, FiberJar);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Fiber Jar: one FLIP clause, everything to the Deck then each player draws 5",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws &&
                    c.Amount == 5 && c.Side == EffectSide.Both && !c.RequiresTargetChoice,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, MysteriousGuard);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Mysterious Guard: one FLIP clause, ordered targets (Warrior condition), picked by its runtime",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.Flip &&
                    c.Action == EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace &&
                    c.ConditionName == "Warrior" && c.RequiresTargetFaceUp && c.IsPsctTarget && !c.RequiresTargetChoice &&
                    string.IsNullOrEmpty(c.RaceFilter),
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, Serpentine);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Serpentine Princess: mandatory 'returned from the field to the Deck' trigger, SS Lv3 or lower from Deck",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.ReturnedFromFieldToDeck && c.Action == EffectActionKind.SpecialSummonFromDeck &&
                    c.Zone == EffectZoneFilter.ControllerDeckMonsters && c.Amount == 3 && c.AmountIsLevel &&
                    !c.IsOptional && !c.IsPsctTarget,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, CreatureSwap);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Creature Swap: one untargeted switch clause, position lock, each-side-monster condition",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.EachPlayerChoosesOwnMonsterSwitchControl &&
                    c.LocksPositionRestOfTurn && !c.RequiresTargetChoice &&
                    c.ActivationCondition == DeckReturnsAndMassEffectsRuntime.EachPlayerControlsMonster &&
                    !p.ClauseList.Exists(x => x != null && (x.Action == EffectActionKind.SwitchControlWithThis ||
                                                            x.Action == EffectActionKind.ReturnControlToOwners)),
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, BubbleCrash);
                var c = p?.ClauseList.Count == 1 ? p.ClauseList[0] : null;
                Check("Bubble Crash: one clause, 6+ cards condition, send until 5 remain",
                    p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && c != null &&
                    c.Timing == EffectTiming.Activate && c.Action == EffectActionKind.EachPlayerSendsHandAndFieldToGyUntilAmount &&
                    c.Amount == 5 && c.ConditionAmount == 6 &&
                    c.ActivationCondition == DeckReturnsAndMassEffectsRuntime.AnyPlayerHandAndFieldAtLeast,
                    $"status={st} {Describe(p)}");
            }
        }

        static CompiledCardProgram Probe(string desc, string type, string race = "Normal") =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990161000 + Math.Abs(desc.GetHashCode() % 9999), name = "DeckReturns Probe", type = type,
                race = race, level = type.Contains("Monster") ? 3 : 0, atk = 500, def = 500,
                frameType = type.StartsWith("Trap") ? "trap" : type.StartsWith("Spell") ? "spell" : "effect",
                desc = desc
            });

        static bool Has(CompiledCardProgram p, EffectActionKind a) =>
            p != null && p.ClauseList.Any(c => c != null && c.Action == a);

        /// <summary>Look-alike texts do not take these atoms (anchored whole-text shapes).</summary>
        static void NearMisses(CardDatabase db, Chk Check)
        {
            var noField = Probe("FLIP: Each player shuffles all cards from their hand and Graveyard into the Deck, then " +
                                "draws 5 cards.", "Flip Effect Monster", "Plant");
            Check("Near miss: 'hand and Graveyard' (no field) is not Fiber Jar",
                noField == null || (!noField.FullyCompiled && !Has(noField, EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws)),
                Describe(noField));
            var noDraw = Probe("FLIP: Each player shuffles all cards from their hand, field, and Graveyard into the Deck.",
                "Flip Effect Monster", "Plant");
            Check("Near miss: Fiber Jar's shuffle without the draw is not FullyCompiled",
                noDraw == null || (!noDraw.FullyCompiled && !Has(noDraw, EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws)),
                Describe(noDraw));

            var mixed = Probe("FLIP: Target 1 face-up monster on the field, and if you control a face-up Warrior-Type " +
                              "monster, you can target 1 additional face-up monster on the field; return the first target " +
                              "to the top of the Deck, then return the additional target (if any) to the hand if you still " +
                              "control a face-up Spellcaster-Type monster.", "Flip Effect Monster", "Spellcaster");
            Check("Near miss: Mysterious Guard's text with two different Types is not compiled",
                mixed == null || (!mixed.FullyCompiled &&
                                  !Has(mixed, EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace)),
                Describe(mixed));
            var guardNoStill = Probe("FLIP: Target 1 face-up monster on the field, and if you control a face-up " +
                                     "Warrior-Type monster, you can target 1 additional face-up monster on the field; " +
                                     "return the first target to the top of the Deck, then return the additional target " +
                                     "(if any) to the hand.", "Flip Effect Monster", "Spellcaster");
            Check("Near miss: Mysterious Guard without 'if you still control' is not compiled by this atom",
                guardNoStill == null || !Has(guardNoStill, EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace),
                Describe(guardNoStill));

            var toHand = Probe("If this face-up card is returned from the field to your hand, select and Special Summon 1 " +
                               "Level 3 or lower monster from your Deck to your side of the field. Then shuffle your Deck.",
                "Effect Monster", "Reptile");
            Check("Near miss: 'returned from the field to your hand' is not the Deck trigger",
                toHand == null || !toHand.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ReturnedFromFieldToDeck),
                Describe(toHand));
            var noShuffle = Probe("If this face-up card is returned from the field to your Deck, select and Special Summon " +
                                  "1 Level 3 or lower monster from your Deck to your side of the field.",
                "Effect Monster", "Reptile");
            Check("Near miss: Serpentine Princess's trigger without 'Then shuffle your Deck.' is not this atom",
                noShuffle == null || !noShuffle.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ReturnedFromFieldToDeck),
                Describe(noShuffle));

            var swapOnly = Probe("Each player chooses 1 monster they control and switches control of those monsters with " +
                                 "each other.", "Spell Card");
            Check("Near miss: Creature Swap's first sentence alone (no position lock) is not FullyCompiled",
                swapOnly == null || (!swapOnly.FullyCompiled &&
                                     !Has(swapOnly, EffectActionKind.EachPlayerChoosesOwnMonsterSwitchControl)),
                Describe(swapOnly));

            var bubbleNoCondition = Probe("The player(s) then select and send cards to their respective Graveyards until " +
                                          "the amount remaining on the field and in their hand is 5.", "Trap Card");
            Check("Near miss: Bubble Crash's resolution without its activation sentence is not compiled",
                bubbleNoCondition == null ||
                (!bubbleNoCondition.FullyCompiled &&
                 !Has(bubbleNoCondition, EffectActionKind.EachPlayerSendsHandAndFieldToGyUntilAmount)),
                Describe(bubbleNoCondition));
        }

        // ───────────────────────────── Fiber Jar ─────────────────────────────

        static void FiberJarShufflesEverythingThenDraws(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            o.Deck.Clear();
            p.Deck.AddRange(Enumerable.Repeat(Celtic, 6));
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 6));
            var pExtra = p.ExtraDeck.Count;
            var fj = PlaceMonster(e, p, FiberJar, 0, BattlePosition.Defense, false);
            var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
            var stolen = PlaceMonster(e, p, Gaia, 2, BattlePosition.Attack, true);
            stolen.ControlOwner = o; // the opponent's monster, controlled by the player
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var fang = PlaceMonster(e, o, SilverFang, 0, BattlePosition.Attack, true);
            var mountain = e.CreateCardInstance(Mountain);
            mountain.FaceUp = true;
            o.FieldSpellZone.Occupant = mountain;
            PutInHand(e, p, PotOfGreed);
            PutInHand(e, p, Kuriboh);
            PutInHand(e, o, Celtic);
            PutInHand(e, o, Celtic);
            p.Graveyard.Add(e.CreateCardInstance(PetitDragon));
            p.Graveyard.Add(e.CreateCardInstance(FlameSwordsman));
            o.Graveyard.Add(e.CreateCardInstance(Bewd));
            var banished = e.CreateCardInstance(Kuriboh);
            p.Banished.Add(banished);

            var flipped = e.TryFlipSummon(p, fj);
            Drive(e, _ => false);
            // Player owns: 6 Deck + Fiber Jar + Elf + Trap Hole + 2 hand + Petit Dragon = 12 Main Deck cards (Flame
            // Swordsman to the Extra Deck); the opponent: 6 + Gaia + Silver Fang + Mountain + 2 hand + B.E.W.D. = 12.
            var fieldsEmpty = !p.MonstersOnField().Any() && !p.SpellTrapsOnField().Any() &&
                              !o.MonstersOnField().Any() && !o.SpellTrapsOnField().Any();
            Check("Fiber Jar: both hands, fields and GYs go to their owners' Decks (stolen Gaia to its owner, Fusion to " +
                  "the Extra Deck, banished stays), then each player draws 5",
                flipped && fieldsEmpty && p.Graveyard.Count == 0 && o.Graveyard.Count == 0 &&
                p.Hand.Count == 5 && o.Hand.Count == 5 && p.Deck.Count == 7 && o.Deck.Count == 7 &&
                p.ExtraDeck.Count == pExtra + 1 && p.ExtraDeck.Contains(FlameSwordsman) && p.Banished.Contains(banished) &&
                !e.IsAwaitingEffectTarget,
                $"flipped={flipped} fieldsEmpty={fieldsEmpty} pGy={p.Graveyard.Count} oGy={o.Graveyard.Count} " +
                $"pHand={p.Hand.Count} oHand={o.Hand.Count} pDeck={p.Deck.Count} oDeck={o.Deck.Count} " +
                $"pExtra={p.ExtraDeck.Count}/{pExtra} banished={p.Banished.Contains(banished)} {State(e)}");
        }

        static void FiberJarDestroyedByBattleStaysInGy(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            p.Graveyard.Clear();
            o.Graveyard.Clear();
            var fj = PlaceMonster(e, p, FiberJar, 0, BattlePosition.Defense, false);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var attacked = reached && e.TryAttack(o, gaia, fj);
            DrainCombat(e);
            Drive(e, _ => false);
            Check("Fiber Jar destroyed by the battle that flipped it: it stays out of the shuffle (GY); the attacker goes " +
                  "to the Deck; each player draws 5",
                reached && attacked && p.Graveyard.Contains(fj) && !o.TryFindMonster(gaia, out _) &&
                !o.Graveyard.Contains(gaia) && p.Hand.Count == 5 && o.Hand.Count == 5,
                $"reached={reached} attacked={attacked} fjGy={p.Graveyard.Contains(fj)} gaia={Where(e, gaia)} " +
                $"pHand={p.Hand.Count} oHand={o.Hand.Count} {State(e)}");
        }

        /// <summary>
        /// Review (rv-combined): Fiber Jar is one simultaneous move (Lua: one Duel.SendtoDeck), so Necrovalley — itself
        /// shuffled into the Deck — still keeps both GYs where they are.
        /// </summary>
        static void FiberJarNecrovalleyKeepsGraveyards(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            const int Necrovalley = 47355498;
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            o.Deck.Clear();
            p.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            var fj = PlaceMonster(e, p, FiberJar, 0, BattlePosition.Defense, false);
            var nv = e.CreateCardInstance(Necrovalley);
            nv.FaceUp = true;
            p.FieldSpellZone.Occupant = nv;
            var oGy = e.CreateCardInstance(PetitDragon);
            o.Graveyard.Add(oGy);
            var pGy = e.CreateCardInstance(Kuriboh);
            p.Graveyard.Add(pGy);
            var lockedBefore = ContinuousProtections.GyMovementLocked(e, fj, oGy);
            var flipped = e.TryFlipSummon(p, fj);
            Drive(e, _ => false);
            Check("Fiber Jar with Necrovalley face-up: both GYs stay (one simultaneous move); Necrovalley itself goes to " +
                  "the Deck",
                flipped && lockedBefore && o.Graveyard.Contains(oGy) && p.Graveyard.Contains(pGy) &&
                p.FieldSpellZone.Occupant == null && p.Hand.Count == 5 && o.Hand.Count == 5,
                $"lockedBefore={lockedBefore} flipped={flipped} oGy={Names(o.Graveyard)} pGy={Names(p.Graveyard)} " +
                $"field={p.FieldSpellZone.Occupant?.Name ?? "-"} pHand={p.Hand.Count} oHand={o.Hand.Count} {State(e)}");
        }

        // ───────────────────────────── Mysterious Guard ─────────────────────────────

        static void GuardTwoTargetsWithWarrior(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
            var setOpp = PlaceMonster(e, o, Celtic, 2, BattlePosition.Defense, false);
            var flipped = e.TryFlipSummon(p, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var first = Legal(e);
            var firstOk = e.IsAwaitingEffectTarget && first.Count == 4 && first.Contains(mg) && first.Contains(celtic) &&
                          first.Contains(gaia) && first.Contains(bewd) && !first.Contains(setOpp);
            var pick1 = firstOk && e.TrySelectEffectTarget(bewd);
            var second = Legal(e);
            var secondOk = pick1 && e.IsAwaitingEffectTarget && second.Count == 3 && !second.Contains(bewd) &&
                           second.Contains(gaia);
            var pick2 = secondOk && e.TrySelectEffectTarget(gaia);
            // Lua s.activate: the two targets are a pair; at resolution the activator chooses which goes to the Deck.
            var third = Legal(e);
            var thirdOk = pick2 && e.IsAwaitingEffectTarget && third.Count == 2 && third.Contains(bewd) &&
                          third.Contains(gaia);
            var pick3 = thirdOk && e.TrySelectEffectTarget(gaia);
            Drive(e, _ => false);
            Check("Mysterious Guard (Warrior controlled): two face-up targets; at resolution the activator chooses the one " +
                  "for the top of its owner's Deck (here the one targeted second), the other returns to the hand",
                flipped && firstOk && secondOk && pick3 && o.Deck.Count > 0 && o.Deck[0] == Gaia && o.Hand.Contains(bewd) &&
                !o.TryFindMonster(gaia, out _) && !e.IsAwaitingEffectTarget,
                $"flipped={flipped} first={Names(first)} second={Names(second)} third={Names(third)} pick3={pick3} " +
                $"top={(o.Deck.Count > 0 ? o.Deck[0] : 0)} bewd={Where(e, bewd)} {State(e)}");
        }

        static void GuardOneTargetWithoutWarrior(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
            var flipped = e.TryFlipSummon(p, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = e.IsAwaitingEffectTarget && Legal(e).Contains(gaia);
            var picked = asked && e.TrySelectEffectTarget(gaia);
            var noSecond = picked && !e.IsAwaitingEffectTarget;
            Check("Mysterious Guard (no face-up Warrior): one target only, on top of its owner's Deck",
                flipped && noSecond && o.Deck.Count > 0 && o.Deck[0] == Gaia && o.TryFindMonster(bewd, out _) &&
                p.TryFindMonster(elf, out _),
                $"flipped={flipped} asked={asked} picked={picked} noSecond={noSecond} gaia={Where(e, gaia)} {State(e)}");
        }

        static void GuardWarriorReturnedFirstKeepsSecond(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var flipped = e.TryFlipSummon(p, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var pick1 = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(celtic);
            var pick2 = pick1 && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(gaia);
            var pick3 = pick2 && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(celtic);
            Drive(e, _ => false);
            Check("Mysterious Guard: the only Warrior chosen for the Deck — 'if you still control' fails, the other " +
                  "target stays",
                flipped && pick3 && p.Deck.Count > 0 && p.Deck[0] == Celtic && o.TryFindMonster(gaia, out _) &&
                !o.Hand.Contains(gaia),
                $"flipped={flipped} pick1={pick1} pick2={pick2} pick3={pick3} celtic={Where(e, celtic)} gaia={Where(e, gaia)} {State(e)}");

            // Cancel at the additional target: none is chosen; the first still goes to the Deck.
            var e2 = FreshCleared(db, pDeck, aDeck);
            var p2 = e2.Player;
            var o2 = e2.Opponent;
            var mg2 = PlaceMonster(e2, p2, MysteriousGuard, 0, BattlePosition.Defense, false);
            PlaceMonster(e2, p2, Celtic, 1, BattlePosition.Attack, true);
            var gaia2 = PlaceMonster(e2, o2, Gaia, 0, BattlePosition.Attack, true);
            var bewd2 = PlaceMonster(e2, o2, Bewd, 1, BattlePosition.Attack, true);
            e2.TryFlipSummon(p2, mg2);
            for (var i = 0; i < 4 && e2.IsAwaitingResponse; i++) e2.PassResponse();
            var p1 = e2.IsAwaitingEffectTarget && e2.TrySelectEffectTarget(bewd2);
            var offered = p1 && e2.IsAwaitingEffectTarget;
            if (offered) e2.CancelEffectTargeting();
            Check("Mysterious Guard: Cancel at the additional target — only the first target is returned",
                offered && !e2.IsAwaitingEffectTarget && o2.Deck.Count > 0 && o2.Deck[0] == Bewd &&
                o2.TryFindMonster(gaia2, out _),
                $"p1={p1} offered={offered} bewd={Where(e2, bewd2)} gaia={Where(e2, gaia2)} {State(e2)}");
        }

        static void GuardAi(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var mg = PlaceMonster(e, o, MysteriousGuard, 0, BattlePosition.Defense, false);
            PlaceMonster(e, o, Celtic, 1, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, p, Gaia, 1, BattlePosition.Attack, true);
            var flipped = main && e.TryFlipSummon(o, mg);
            Drive(e, _ => false);
            Check("Mysterious Guard (AI): the player's strongest face-up monster on top of the Deck, the next to the hand",
                flipped && p.Deck.Count > 0 && p.Deck[0] == Bewd && p.Hand.Contains(gaia) && !e.IsAwaitingEffectTarget,
                $"main={main} flipped={flipped} bewd={Where(e, bewd)} gaia={Where(e, gaia)} {State(e)}");
        }

        /// <summary>
        /// Flipped face-up by Curse of Fiend, Mysterious Guard's FLIP goes on the simultaneous-trigger Chain: its targets
        /// are chosen as it activates, then it resolves on them.
        /// </summary>
        static void GuardOnASegocChain(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = new DuelEngine();
            e.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var curse = PlaceSetTrap(e, p, CurseOfFiend, 0);
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, p, Celtic, 1, BattlePosition.Defense, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
            e.TryEndTurnSafe(p);
            if (e.IsAwaitingResponse) e.PassResponse();
            e.TryEndTurnSafe(o);
            if (e.IsAwaitingResponse) e.PassResponse();
            var paused = e.Phase == DuelPhase.Standby && e.TurnPlayer == p;
            var act = paused && e.TryActivateSpellTrap(p, curse, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = act && e.IsAwaitingEffectTarget && e.PendingActivation.Card == mg && mg.FaceUp &&
                        Legal(e).Contains(bewd);
            var pick1 = asked && e.TrySelectEffectTarget(bewd);
            var pick2 = pick1 && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(gaia);
            Drive(e, c => c == bewd);
            Check("Mysterious Guard flipped by Curse of Fiend: targets chosen on the Chain, then resolved on them",
                pick2 && o.Deck.Count > 0 && o.Deck[0] == Bewd && o.Hand.Contains(gaia) && !SimultaneousTriggersRuntime.Busy(e),
                $"paused={paused} act={act} asked={asked} pick1={pick1} pick2={pick2} bewd={Where(e, bewd)} " +
                $"gaia={Where(e, gaia)} celtic={Where(e, celtic)} {State(e)}");
        }

        /// <summary>
        /// Review (rv-combined), Lua c1347977 op 2: at resolution only the targets still face-up count, and a lone one goes
        /// on top of the Deck whichever was targeted first. The first target is turned face-down after it was targeted (a
        /// stand-in for Book of Moon chained to the effect; the test sets it between the two target picks).
        /// </summary>
        static void GuardLoneFaceUpTargetGoesToDeck(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, o, Bewd, 1, BattlePosition.Attack, true);
            var flipped = e.TryFlipSummon(p, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var pick1 = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(bewd);
            if (pick1) bewd.FaceUp = false;
            var pick2 = pick1 && e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(gaia);
            var choiceAsked = pick2 && e.IsAwaitingEffectTarget;
            Drive(e, _ => false);
            Check("Mysterious Guard: the first target is face-down at resolution — the lone face-up target goes on top of " +
                  "its owner's Deck (no choice, nothing to the hand)",
                flipped && pick2 && !choiceAsked && o.Deck.Count > 0 && o.Deck[0] == Gaia && !o.Hand.Contains(gaia) &&
                o.TryFindMonster(bewd, out _) && !bewd.FaceUp && !e.IsAwaitingEffectTarget,
                $"flipped={flipped} pick1={pick1} pick2={pick2} choiceAsked={choiceAsked} " +
                $"top={(o.Deck.Count > 0 ? o.Deck[0] : 0)} gaia={Where(e, gaia)} bewd={Where(e, bewd)} {State(e)}");
        }

        // ───────────────────────────── Serpentine Princess ─────────────────────────────

        static void SerpentineReturnedByAiGuard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, SilverFang, Gaia, GiantSoldier });
            var mg = PlaceMonster(e, o, MysteriousGuard, 0, BattlePosition.Defense, false);
            var princess = PlaceMonster(e, p, Serpentine, 0, BattlePosition.Attack, true);
            var flipped = main && e.TryFlipSummon(o, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var legal = Legal(e);
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p &&
                        e.PendingActivation.TargetKind == EffectTargetKind.MonsterInYourDeckToSummon &&
                        SpellTrapEffects.PendingTargetIsSpecialSummon(e.PendingActivation) &&
                        legal.Count == 2 && legal.All(c => c.CardId == SilverFang || c.CardId == GiantSoldier);
            var fang = legal.FirstOrDefault(c => c.CardId == SilverFang);
            var picked = asked && e.TrySelectEffectTarget(fang);
            Drive(e, _ => false);
            Check("Serpentine Princess returned to the Deck by the opponent's Mysterious Guard: its owner selects a Level 3 " +
                  "or lower monster from the Deck and Special Summons it",
                flipped && asked && picked && p.TryFindMonster(fang, out _) && fang.FaceUp && p.Deck.Contains(Serpentine) &&
                !p.Deck.Contains(SilverFang) && p.Deck.Count == 4 && !e.IsAwaitingEffectTarget,
                $"main={main} flipped={flipped} legal={Names(legal)} picked={picked} fang={Where(e, fang)} " +
                $"princess={Where(e, princess)} deck={string.Join(",", p.Deck)} {State(e)}");
        }

        static void SerpentineReturnedByFiberJar(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            o.Deck.Clear();
            p.Deck.AddRange(Enumerable.Repeat(SilverFang, 10));
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            var fj = PlaceMonster(e, p, FiberJar, 0, BattlePosition.Defense, false);
            var princess = PlaceMonster(e, p, Serpentine, 1, BattlePosition.Attack, true);
            var flipped = e.TryFlipSummon(p, fj);
            Drive(e, c => c.CardId == SilverFang);
            var summoned = p.MonstersOnField().ToList();
            Check("Fiber Jar shuffles a face-up Serpentine Princess into the Deck: after the draws its trigger Special " +
                  "Summons a Level 3 or lower monster from the Deck",
                flipped && summoned.Count == 1 && summoned[0].Level <= 3 && summoned[0].FaceUp && p.Hand.Count == 5 &&
                o.Hand.Count == 5 && !e.IsAwaitingEffectTarget && !SimultaneousTriggersRuntime.Busy(e),
                $"flipped={flipped} field={Names(summoned)} pHand={p.Hand.Count} princess={Where(e, princess)} {State(e)}");
        }

        static void SerpentineAiReturnedByPlayerGuard(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            o.Deck.Clear();
            o.Deck.AddRange(new[] { Celtic, PetitDragon, Gaia, GiantSoldier, Bewd });
            var mg = PlaceMonster(e, p, MysteriousGuard, 0, BattlePosition.Defense, false);
            var princess = PlaceMonster(e, o, Serpentine, 0, BattlePosition.Attack, true);
            var flipped = e.TryFlipSummon(p, mg);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var picked = e.IsAwaitingEffectTarget && e.TrySelectEffectTarget(princess);
            Drive(e, _ => false);
            var oField = o.MonstersOnField().ToList();
            Check("Serpentine Princess (AI) returned by the player's Mysterious Guard: the AI Special Summons its best " +
                  "Level 3 or lower monster (Giant Soldier of Stone) and shuffles",
                flipped && picked && oField.Count == 1 && oField[0].CardId == GiantSoldier && o.Deck.Contains(Serpentine) &&
                o.Deck.Count == 5 && !e.IsAwaitingEffectTarget,
                $"flipped={flipped} picked={picked} oField={Names(oField)} deck={string.Join(",", o.Deck)} {State(e)}");
        }

        static void SerpentineSetIsNoTrigger(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            o.Deck.Clear();
            p.Deck.AddRange(Enumerable.Repeat(SilverFang, 10));
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            var fj = PlaceMonster(e, p, FiberJar, 0, BattlePosition.Defense, false);
            PlaceMonster(e, p, Serpentine, 1, BattlePosition.Defense, false);
            var flipped = e.TryFlipSummon(p, fj);
            var askedAny = false;
            for (var i = 0; i < 6; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    askedAny = true;
                    e.CancelEffectTargeting();
                }
                else if (e.IsAwaitingResponse) e.PassResponse();
            }

            Check("Serpentine Princess shuffled in while Set (not face-up): no trigger",
                flipped && !askedAny && !p.MonstersOnField().Any() && p.Hand.Count == 5,
                $"flipped={flipped} askedAny={askedAny} field={Names(p.MonstersOnField())} {State(e)}");
        }

        // ───────────────────────────── Creature Swap ─────────────────────────────

        static void CreatureSwapPlayer(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, o, Gaia, 2, BattlePosition.Attack, true);
            var fang = PlaceMonster(e, o, SilverFang, 3, BattlePosition.Defense, false);
            var swap = PutInHand(e, p, CreatureSwap);
            var act = e.TryActivateSpellTrap(p, swap, fromHand: true);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var legal = Legal(e);
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p && legal.Count == 2 &&
                        legal.Contains(celtic) && legal.Contains(elf) && e.PendingActivation.HoldsChainResolution;
            var picked = asked && e.TrySelectEffectTarget(elf);
            Drive(e, _ => false);
            var elfZone = o.TryFindMonster(elf, out var ez) ? ez : -1;
            var fangZone = p.TryFindMonster(fang, out var fz) ? fz : -1;
            Check("Creature Swap: the player chooses as it resolves (the pick holds the Chain), the AI gives its weakest (the Set Silver Fang); they " +
                  "switch into each other's zones",
                act && asked && picked && elfZone == 3 && fangZone == 1 && o.TryFindMonster(gaia, out _) &&
                p.TryFindMonster(celtic, out _) && p.Graveyard.Contains(swap) && !e.Chain.HasLinks &&
                !e.IsAwaitingEffectTarget,
                $"act={act} legal={Names(legal)} picked={picked} elfZone={elfZone} " +
                $"fangZone={fangZone} {State(e)}");
            Check("Creature Swap: neither switched monster can change its battle position this turn (nor be Flip Summoned)",
                PositionLocksRuntime.ManualPositionChangeLocked(e, elf) &&
                PositionLocksRuntime.ManualPositionChangeLocked(e, fang) &&
                !PositionLocksRuntime.ManualPositionChangeLocked(e, celtic) && !e.CanFlipSummon(p, fang),
                $"elf={PositionLocksRuntime.ManualPositionChangeLocked(e, elf)} fang={PositionLocksRuntime.ManualPositionChangeLocked(e, fang)} " +
                $"celtic={PositionLocksRuntime.ManualPositionChangeLocked(e, celtic)}");
        }

        static void CreatureSwapAiActivates(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, p, Bewd, 1, BattlePosition.Attack, true);
            var kuriboh = PlaceMonster(e, o, Kuriboh, 0, BattlePosition.Attack, true);
            var swap = PutInHand(e, o, CreatureSwap);
            var act = main && e.TryActivateSpellTrap(o, swap, fromHand: true);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p && Legal(e).Count == 2;
            var picked = asked && e.TrySelectEffectTarget(celtic);
            Drive(e, _ => false);
            Check("Creature Swap (AI): the player chooses their own monster during the AI's resolution; then the switch",
                act && asked && picked && o.TryFindMonster(celtic, out _) && p.TryFindMonster(kuriboh, out _) &&
                p.TryFindMonster(bewd, out _) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"main={main} act={act} asked={asked} picked={picked} celtic={Where(e, celtic)} kuriboh={Where(e, kuriboh)} {State(e)}");
        }

        static void CreatureSwapNeedsAMonsterEachSide(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var swap = PutInHand(e, p, CreatureSwap);
            var noneOwn = !e.CanActivateSpellTrap(p, swap, fromHand: true);
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var both = e.CanActivateSpellTrap(p, swap, fromHand: true);
            ClearBoard(e);
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var noneOpp = !e.CanActivateSpellTrap(p, swap, fromHand: true);
            Check("Creature Swap: cannot be activated unless each player controls a monster",
                noneOwn && both && noneOpp, $"noneOwn={noneOwn} both={both} noneOpp={noneOpp}");
        }

        // ───────────────────────────── Bubble Crash ─────────────────────────────

        static void BubbleCrashPlayer(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var bubble = PlaceSetTrap(e, p, BubbleCrash, 0);
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, Gaia, 1, BattlePosition.Attack, true);
            var pot = PutInHand(e, p, PotOfGreed);
            PutInHand(e, p, Kuriboh);
            PutInHand(e, p, SilverFang);
            // Player: 3 hand + 2 monsters + Bubble Crash = 6; opponent: 4 hand + 3 field = 7.
            PlaceMonster(e, o, Bewd, 0, BattlePosition.Attack, true);
            PlaceMonster(e, o, PetitDragon, 1, BattlePosition.Attack, true);
            PlaceSetTrap(e, o, TrapHole, 0);
            for (var i = 0; i < 4; i++) PutInHand(e, o, Celtic);
            var act = e.TryActivateSpellTrap(p, bubble, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var legal = Legal(e);
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p &&
                        e.PendingActivation.TargetPicksRemaining == 1 && legal.Count == 5 && !legal.Contains(bubble);
            var picked = asked && e.TrySelectEffectTarget(pot);
            Drive(e, _ => false);
            int Total(DuelistState s) => s.Hand.Count + s.MonstersOnField().Count() + s.SpellTrapsOnField().Count();
            Check("Bubble Crash: the activator selects 1 (not Bubble Crash, which counts), the AI sends 2; all to the GY",
                act && asked && picked && p.Graveyard.Contains(pot) && p.Graveyard.Contains(bubble) && Total(p) == 4 &&
                Total(o) == 5 && o.Graveyard.Count == 2 && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"act={act} asked={asked} legal={Names(legal)} picked={picked} pTotal={Total(p)} oTotal={Total(o)} " +
                $"oGy={Names(o.Graveyard)} {State(e)}");
        }

        static void BubbleCrashCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var bubble = PlaceSetTrap(e, p, BubbleCrash, 0);
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            for (var i = 0; i < 3; i++) PutInHand(e, p, Kuriboh);
            for (var i = 0; i < 5; i++) PutInHand(e, o, Celtic);
            var five = !e.CanActivateSpellTrap(p, bubble, fromHand: false);
            PutInHand(e, o, Celtic);
            var six = e.CanActivateSpellTrap(p, bubble, fromHand: false);
            var act = six && e.TryActivateSpellTrap(p, bubble, fromHand: false);
            Drive(e, _ => false);
            Check("Bubble Crash: needs a player with 6+ cards in hand and on the field; only that player sends cards",
                five && six && act && p.Hand.Count == 3 && p.MonstersOnField().Count() == 1 &&
                o.Hand.Count == 5 && o.Graveyard.Count == 1,
                $"five={five} six={six} act={act} pHand={p.Hand.Count} oHand={o.Hand.Count} oGy={o.Graveyard.Count} {State(e)}");
        }

        static void BubbleCrashAiActivates(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var main = ReachOpponentMain(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            p.Graveyard.Clear();
            var bubble = PlaceSetTrap(e, o, BubbleCrash, 0);
            PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var bewd = PlaceMonster(e, p, Bewd, 0, BattlePosition.Attack, true);
            var gaia = PlaceMonster(e, p, Gaia, 1, BattlePosition.Attack, true);
            var hole = PlaceSetTrap(e, p, TrapHole, 0);
            var k1 = PutInHand(e, p, Kuriboh);
            var k2 = PutInHand(e, p, Kuriboh);
            PutInHand(e, p, SilverFang);
            PutInHand(e, p, PotOfGreed);
            var act = main && e.TryActivateSpellTrap(o, bubble, fromHand: false);
            for (var i = 0; i < 4 && e.IsAwaitingResponse; i++) e.PassResponse();
            var asked = e.IsAwaitingEffectTarget && e.PendingActivation.Controller == p &&
                        e.PendingActivation.TargetPicksRemaining == 2 && Legal(e).Count == 7;
            var pick1 = asked && e.TrySelectEffectTarget(k1);
            var stillAsking = pick1 && e.IsAwaitingEffectTarget;
            var pick2 = stillAsking && e.TrySelectEffectTarget(k2);
            Drive(e, _ => false);
            Check("Bubble Crash (AI): the player selects 2 of their 7 cards during the AI's resolution; down to 5",
                act && asked && stillAsking && pick2 && p.Graveyard.Contains(k1) && p.Graveyard.Contains(k2) &&
                p.Hand.Count == 2 && p.TryFindMonster(bewd, out _) && p.TryFindMonster(gaia, out _) &&
                p.TryFindSpellTrap(hole, out _) && o.Graveyard.Contains(bubble) && o.MonstersOnField().Count() == 1 &&
                !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                $"main={main} act={act} asked={asked} pick1={pick1} pick2={pick2} pGy={Names(p.Graveyard)} " +
                $"pHand={p.Hand.Count} {State(e)}");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine FreshCleared(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
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

        static IEnumerable<string> Places(DuelEngine e, CardInstance c)
        {
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) yield return tag + ".hand";
                if (who.Graveyard.Contains(c)) yield return tag + ".gy";
                if (who.Banished.Contains(c)) yield return tag + ".ban";
                if (who.TryFindMonster(c, out _)) yield return tag + ".mz";
                if (who.TryFindSpellTrap(c, out _)) yield return tag + ".stz";
            }
        }

        static string Where(DuelEngine e, CardInstance c) => c == null ? "-" : string.Join("+", Places(e, c));

        static string Names(IEnumerable<CardInstance> cards) =>
            string.Join(",", (cards ?? Enumerable.Empty<CardInstance>())
                .Select(t => t == null ? "null" : t.Name + (t.FaceUp ? "(up)" : "(set)")));

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"}[{Names(e.PendingActivation?.LegalTargets)}] " +
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name} " +
            $"over={e.GameOver}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/amt={c.Amount}/cond={c.ActivationCondition}:" +
                             $"{c.ConditionName}:{c.ConditionAmount}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
