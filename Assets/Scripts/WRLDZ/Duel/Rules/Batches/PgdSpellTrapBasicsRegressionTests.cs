using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "PgdSpellTrapBasics" (v162 PGD), checked against the ygopro scripts:
    /// <list type="bullet">
    /// <item>Book of Taiyou c38699854: target a face-down monster on either field; if still face-down, face-up Attack
    /// (its FLIP effect activates).</item>
    /// <item>Curse of Aging c41398771: cost discard 1; every face-up monster the opponent controls -500 ATK/DEF until the
    /// End Phase.</item>
    /// <item>D. Tribe c2833249: every face-up monster you control becomes Dragon until the End Phase.</item>
    /// <item>Pyramid Energy c76754619: option chosen at activation: +200 ATK or +500 DEF to your face-up monsters.</item>
    /// <item>Secret Pass to the Treasures c77876207: target a face-up monster you control with 1000 or less ATK; it can
    /// attack directly this turn.</item>
    /// <item>Needle Ceiling c38411870: 4+ monsters on the field (face-down count); destroy every face-up monster.</item>
    /// <item>Hieroglyph Lithograph c10248192: pay 1000 LP; your hand size limit becomes 7 for the rest of the Duel.</item>
    /// <item>Card Shuffle c12183332: face-up ignition, once per turn, pay 300 LP; shuffle your Deck or the opponent's,
    /// chosen at resolution.</item>
    /// </list>
    /// </summary>
    public static class PgdSpellTrapBasicsRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int BookOfTaiyou = 38699854;
        const int CurseOfAging = 41398771;
        const int DTribe = 2833249;
        const int PyramidEnergy = 76754619;
        const int SecretPass = 77876207;
        const int NeedleCeiling = 38411870;
        const int Hieroglyph = 10248192;
        const int CardShuffle = 12183332;

        const int Celtic = 91152256;          // Warrior 1400/1200
        const int MysticalElf = 15025844;     // Spellcaster 800/2000
        const int Gaia = 6368038;             // Warrior 2300/2100
        const int Koumori = 67724379;         // Dragon 1500/1200
        const int SilverFang = 90357090;      // Beast 1200/800
        const int GiantSoldier = 13039848;    // Rock 1300/2000
        const int PoisonMummy = 43716289;     // FLIP: Inflict 500 damage to your opponent.
        const int PotOfGreed = 55144522;
        const int HeavyStorm = 19613556;
        const int InfiniteCards = 94163677;
        const int EnervatingMist = 26022485;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("PgdSpellTrapBasics: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMisses(db, Check);

            BookFlipsOpponentsSet(db, pDeck, aDeck, Check);
            BookFlipEffectActivatesAfterwards(db, pDeck, aDeck, Check);
            BookNeedsAFaceDownMonster(db, pDeck, aDeck, Check);

            CurseOfAgingLowersFaceUpOpponents(db, pDeck, aDeck, Check);
            CurseOfAgingLegality(db, pDeck, aDeck, Check);

            DTribeTurnsYourFaceUpsIntoDragons(db, pDeck, aDeck, Check);
            DTribeNeedsAFaceUpMonster(db, pDeck, aDeck, Check);
            DTribeEndsWhenTheMonsterLeavesTheField(db, pDeck, aDeck, Check);

            PyramidEnergyPlayerOptions(db, pDeck, aDeck, Check);
            PyramidEnergyAiAndLegality(db, pDeck, aDeck, Check);

            SecretPassAttacksDirectly(db, pDeck, aDeck, Check);
            SecretPassTargets(db, pDeck, aDeck, Check);

            NeedleCeilingDestroysFaceUps(db, pDeck, aDeck, Check);
            NeedleCeilingCondition(db, pDeck, aDeck, Check);
            NeedleCeilingAnswersAnAttack(db, pDeck, aDeck, Check);
            NeedleCeilingAiUseItOrLoseIt(db, pDeck, aDeck, Check);

            HieroglyphRaisesHandLimit(db, pDeck, aDeck, Check);
            HieroglyphVersusMistAndInfiniteCards(db, pDeck, aDeck, Check);

            CardShufflePlayerPicksADeck(db, pDeck, aDeck, Check);
            CardShuffleLimits(db, pDeck, aDeck, Check);
            CardShuffleIsANewCardAfterLeavingTheField(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static (CompiledCardProgram prog, CardEffectStatusKind st) Of(CardDatabase db, int id)
        {
            var def = db.Get(id);
            var p = def != null ? CardTextEffectCompiler.Compile(def) : null;
            return (p, def != null ? CardEffectStatus.Classify(def) : CardEffectStatusKind.Unimplemented);
        }

        static bool Whole(CompiledCardProgram p, CardEffectStatusKind st, int clauses) =>
            p != null && p.FullyCompiled && st == CardEffectStatusKind.Implemented && p.ClauseList.Count == clauses &&
            (p.UnparsedFragments == null || p.UnparsedFragments.Length == 0);

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            {
                var (p, st) = Of(db, BookOfTaiyou);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Book of Taiyou: one targeted flip of a face-down monster on either field",
                    Whole(p, st, 1) && c.Timing == EffectTiming.Activate &&
                    c.Action == EffectActionKind.FlipFaceDownTargetToFaceUpAttack &&
                    c.Zone == EffectZoneFilter.FieldFaceDownMonsters && c.RequiresTargetChoice && c.IsPsctTarget,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, CurseOfAging);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Curse of Aging: discard 1 (cost), opponent's face-up monsters -500 ATK/DEF until the End Phase",
                    Whole(p, st, 1) && c.Action == EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase &&
                    c.Side == EffectSide.Opponent && c.Amount == -500 && c.DefAmount == -500 && c.RequiresDiscardCost &&
                    c.DiscardCostCount == 1 && c.DiscardCostAttribute == "*" && !c.RequiresTargetChoice &&
                    c.ActivationCondition == PgdSpellTrapBasicsRuntime.OpponentControlsFaceUpMonster,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, DTribe);
                var c = p?.ClauseList.FirstOrDefault();
                Check("D. Tribe: your face-up monsters become Dragon until the End Phase",
                    Whole(p, st, 1) && c.Action == EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase &&
                    c.BecomesRace == "Dragon" && c.Side == EffectSide.Controller && !c.RequiresTargetChoice &&
                    c.ActivationCondition == PgdSpellTrapBasicsRuntime.YouControlFaceUpMonster,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, PyramidEnergy);
                var a = p?.ClauseList;
                Check("Pyramid Energy: two options (+200 ATK / +500 DEF to your face-up monsters)",
                    Whole(p, st, 2) &&
                    a[0].Action == EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase && a[0].EffectChoiceIndex == 1 &&
                    a[0].Amount == 200 && a[0].DefAmount == 0 && a[0].Side == EffectSide.Controller &&
                    a[1].Action == EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase && a[1].EffectChoiceIndex == 2 &&
                    a[1].Amount == 0 && a[1].DefAmount == 500 && a[1].Side == EffectSide.Controller,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, SecretPass);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Secret Pass to the Treasures: target your face-up monster with 1000 or less ATK; direct attack",
                    Whole(p, st, 1) && c.Action == EffectActionKind.TargetCanAttackDirectlyThisTurn &&
                    c.Zone == EffectZoneFilter.ControllerMonsters && c.RequiresTargetChoice && c.IsPsctTarget &&
                    c.RequiresTargetFaceUp && c.ConditionAmount == 1000 && c.Amount == 0,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, NeedleCeiling);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Needle Ceiling: 4+ monsters condition, destroy all face-up monsters",
                    Whole(p, st, 1) && c.Action == EffectActionKind.DestroyAllFaceUpMonstersOnField &&
                    c.ActivationCondition == PgdSpellTrapBasicsRuntime.FieldMonstersAtLeastWithFaceUp &&
                    c.ConditionAmount == 4 && !c.RequiresTargetChoice,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, Hieroglyph);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Hieroglyph Lithograph: pay 1000 LP (cost); your hand size limit becomes 7 for the Duel",
                    Whole(p, st, 1) && c.Action == EffectActionKind.ControllerHandSizeLimitBecomesForDuel &&
                    c.Amount == 7 && c.PayLpAmount == 1000 && c.Side == EffectSide.Controller,
                    $"status={st} {Describe(p)}");
            }
            {
                var (p, st) = Of(db, CardShuffle);
                var c = p?.ClauseList.FirstOrDefault();
                Check("Card Shuffle: face-up ignition, pay 300 LP, once per turn, shuffle a chosen Deck",
                    Whole(p, st, 1) && c.Timing == EffectTiming.FaceUpFieldIgnition &&
                    c.Action == EffectActionKind.ShuffleYourOrOpponentsDeck && c.PayLpAmount == 300 && c.OncePerTurn &&
                    !p.HasTiming(EffectTiming.Activate),
                    $"status={st} {Describe(p)}");
            }
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            CompiledCardProgram Text(int id, string type, string race, string desc) =>
                CardTextEffectCompiler.Compile(new CardDef
                {
                    id = id, name = "Near-miss " + id, type = type, race = race,
                    frameType = type.StartsWith("Trap", StringComparison.Ordinal) ? "trap" : "spell", desc = desc
                });

            bool Lacks(CompiledCardProgram p, EffectActionKind a) =>
                p == null || !p.ClauseList.Exists(c => c != null && c.Action == a);

            var misses = new (int id, string type, string race, string desc, EffectActionKind action)[]
            {
                (90062001, "Spell Card", "Normal",
                    "Flip 1 face-down monster on the field into face-up Defense Position.",
                    EffectActionKind.FlipFaceDownTargetToFaceUpAttack),
                (90062002, "Spell Card", "Normal",
                    "Flip 1 face-down monster on the field into face-up Attack Position. Draw 1 card.",
                    EffectActionKind.FlipFaceDownTargetToFaceUpAttack),
                (90062003, "Trap Card", "Normal",
                    "Decrease the ATK and DEF of all monsters on your opponent's side of the field by 500 points until " +
                    "the end of the turn this card is activated.",
                    EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase),
                (90062004, "Trap Card", "Normal",
                    "All monsters on your side of the field are treated as Dragon-Type.",
                    EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase),
                (90062005, "Spell Card", "Quick-Play",
                    "Increase the ATK of all face-up monsters on your side of the field by 200 points until the End " +
                    "Phase of the turn this card is activated.",
                    EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase),
                (90062006, "Spell Card", "Normal",
                    "Select 1 face-up monster with an ATK equal to 1000 points or less on your side of the field. During " +
                    "the turn this card is activated, the selected monster can attack twice.",
                    EffectActionKind.TargetCanAttackDirectlyThisTurn),
                (90062007, "Trap Card", "Normal",
                    "When there are 4 or more monsters on the field: Destroy all monsters.",
                    EffectActionKind.DestroyAllFaceUpMonstersOnField),
                (90062008, "Spell Card", "Normal",
                    "During the current Duel, your hand size limit becomes 7.",
                    EffectActionKind.ControllerHandSizeLimitBecomesForDuel),
                (90062009, "Spell Card", "Continuous",
                    "Pay 300 Life Points to use this card's effect. Shuffle your Deck. You can only use this effect " +
                    "during your turn, once per turn.",
                    EffectActionKind.ShuffleYourOrOpponentsDeck),
            };
            foreach (var (id, type, race, desc, action) in misses)
            {
                var p = Text(id, type, race, desc);
                Check($"near-miss is not {action}: \"{desc}\"", Lacks(p, action), Describe(p));
            }

            // The Enervating Mist / Infinite Cards guards in InteractionRegressionTests stay; Hieroglyph has its own shape.
            var hiero = Of(db, Hieroglyph).prog;
            Check("Hieroglyph Lithograph is neither NoHandSizeLimit nor OpponentHandSizeLimitBecomes",
                Lacks(hiero, EffectActionKind.NoHandSizeLimit) &&
                Lacks(hiero, EffectActionKind.OpponentHandSizeLimitBecomes), Describe(hiero));
        }

        // ───────────────────────────── Book of Taiyou ─────────────────────────────

        static void BookFlipsOpponentsSet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var set = PlaceMonster(e, o, GiantSoldier, 0, BattlePosition.Defense, false);
            var faceUpDef = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
            var book = PutInHand(e, p, BookOfTaiyou);
            var targets = new List<CardInstance>();
            var act = e.TryActivateSpellTrap(p, book, fromHand: true);
            if (e.IsAwaitingEffectTarget) targets.AddRange(Legal(e));
            Drive(e, c => c == set);
            Check("Book of Taiyou: the face-down monster (not a face-up Defense one) is flipped to face-up Attack",
                act && set.FaceUp && set.Position == BattlePosition.Attack && faceUp(faceUpDef, BattlePosition.Defense) &&
                !targets.Contains(faceUpDef) && Where(e, book) == "P.gy",
                $"act={act} targets={Names(targets)} set={set.FaceUp}/{set.Position} {State(e)}");
        }

        static bool faceUp(CardInstance c, BattlePosition pos) => c != null && c.FaceUp && c.Position == pos;

        static void BookFlipEffectActivatesAfterwards(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mummy = PlaceMonster(e, p, PoisonMummy, 0, BattlePosition.Defense, false);
            var oLp = o.LifePoints;
            var book = PutInHand(e, p, BookOfTaiyou);
            var act = e.TryActivateSpellTrap(p, book, fromHand: true);
            Drive(e, c => c == mummy);
            Check("Book of Taiyou: a flipped FLIP monster activates its effect (Poison Mummy: 500 damage)",
                act && faceUp(mummy, BattlePosition.Attack) && o.LifePoints == oLp - 500 && Where(e, book) == "P.gy",
                $"act={act} mummy={mummy.FaceUp}/{mummy.Position} o={o.LifePoints} {State(e)}");
        }

        static void BookNeedsAFaceDownMonster(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            PlaceMonster(e, e.Player, Celtic, 0, BattlePosition.Attack, true);
            PlaceMonster(e, e.Opponent, MysticalElf, 0, BattlePosition.Defense, true);
            var book = PutInHand(e, e.Player, BookOfTaiyou);
            Check("Book of Taiyou: no face-down monster — cannot activate",
                !e.CanActivateSpellTrap(e.Player, book, fromHand: true), State(e));
        }

        // ───────────────────────────── Curse of Aging ─────────────────────────────

        static void CurseOfAgingLowersFaceUpOpponents(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var mine = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var elf = PlaceMonster(e, o, MysticalElf, 1, BattlePosition.Defense, true);
            var set = PlaceMonster(e, o, GiantSoldier, 2, BattlePosition.Defense, false);
            var fodder = PutInHand(e, p, PotOfGreed);
            var curse = PlaceSetTrap(e, p, CurseOfAging, 0);
            var act = e.TryActivateSpellTrap(p, curse, fromHand: false);
            Drive(e, c => c == fodder);
            var during = celtic.CurrentAtk == 900 && celtic.CurrentDef == 700 && elf.CurrentAtk == 300 &&
                         elf.CurrentDef == 1500 && set.UntilEndOfTurnAtk == 0 && set.UntilEndOfTurnDef == 0 &&
                         mine.CurrentAtk == 2300 && Where(e, fodder) == "P.gy" && Where(e, curse) == "P.gy";
            var detail = $"act={act} celtic={celtic.CurrentAtk}/{celtic.CurrentDef} elf={elf.CurrentAtk}/{elf.CurrentDef} " +
                         $"set={set.UntilEndOfTurnAtk}/{set.UntilEndOfTurnDef} fodder={Where(e, fodder)} {State(e)}";
            e.TryEndTurnSafe(p);
            Check("Curse of Aging: discard 1 as the cost; only the opponent's face-up monsters lose 500 ATK/DEF, until the End Phase",
                act && during && celtic.CurrentAtk == 1400 && celtic.CurrentDef == 1200,
                detail + $" after={celtic.CurrentAtk}/{celtic.CurrentDef}");
        }

        static void CurseOfAgingLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                var curse = PlaceSetTrap(e, e.Player, CurseOfAging, 0);
                Check("Curse of Aging: no card to discard — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, curse, fromHand: false), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Player, Gaia, 0, BattlePosition.Attack, true);
                PutInHand(e, e.Player, PotOfGreed);
                var curse = PlaceSetTrap(e, e.Player, CurseOfAging, 0);
                Check("Curse of Aging: the opponent controls no face-up monster — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, curse, fromHand: false), State(e));
            }
        }

        // ───────────────────────────── D. Tribe ─────────────────────────────

        static void DTribeTurnsYourFaceUpsIntoDragons(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var set = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
            var theirs = PlaceMonster(e, o, SilverFang, 0, BattlePosition.Attack, true);
            var tribe = PlaceSetTrap(e, p, DTribe, 0);
            var act = e.TryActivateSpellTrap(p, tribe, fromHand: false);
            Drive(e, _ => false);
            var during = celtic.Race == "Dragon" && theirs.Race == "Beast";
            set.FaceUp = true; // flipped later this turn: it was face-down when D. Tribe resolved
            var later = PlaceMonster(e, p, Gaia, 2, BattlePosition.Attack, true);
            var unaffected = set.Race == "Spellcaster" && later.Race == "Warrior";
            var detail = $"act={act} celtic={celtic.Race} set={set.Race} later={later.Race} theirs={theirs.Race} {State(e)}";
            e.TryEndTurnSafe(p);
            Check("D. Tribe: your face-up monsters at resolution are Dragon until the End Phase; Sets / later monsters are not",
                act && during && unaffected && celtic.Race == "Warrior" && Where(e, tribe) == "P.gy",
                detail + $" afterEnd={celtic.Race}");
        }

        static void DTribeNeedsAFaceUpMonster(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            PlaceMonster(e, e.Player, Celtic, 0, BattlePosition.Defense, false);
            PlaceMonster(e, e.Opponent, Gaia, 0, BattlePosition.Attack, true);
            var tribe = PlaceSetTrap(e, e.Player, DTribe, 0);
            Check("D. Tribe: you control no face-up monster — cannot activate",
                !e.CanActivateSpellTrap(e.Player, tribe, fromHand: false), State(e));
        }

        /// <summary>
        /// RESETS_STANDARD_PHASE_END includes RESET_LEAVE / RESET_TOFIELD: a monster that leaves the field and comes back
        /// the same turn is a new card with its own Type (review finding, v162).
        /// </summary>
        static void DTribeEndsWhenTheMonsterLeavesTheField(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            PlaceMonster(e, e.Opponent, SilverFang, 0, BattlePosition.Attack, true);
            var tribe = PlaceSetTrap(e, p, DTribe, 0);
            var act = e.TryActivateSpellTrap(p, tribe, fromHand: false);
            Drive(e, _ => false);
            var during = celtic.Race;
            TextEffects.TextEffectRuntime.DestroyCard(e, celtic, null, false, e.Opponent);
            var inGy = p.Graveyard.Contains(celtic);
            var gyRace = celtic.Race;
            var ss = e.SpecialSummonToField(p, celtic, BattlePosition.Attack, true);
            Check("D. Tribe: a monster that leaves the field and comes back the same turn is no longer Dragon",
                act && during == "Dragon" && inGy && gyRace == "Warrior" && ss && celtic.Race == "Warrior",
                $"act={act} during={during} inGy={inGy} gyRace={gyRace} ss={ss} after={celtic.Race} {State(e)}");
        }

        // ───────────────────────────── Pyramid Energy ─────────────────────────────

        static void PyramidEnergyPlayerOptions(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var atk in new[] { true, false })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
                var set = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
                var theirs = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
                var pe = PutInHand(e, p, PyramidEnergy);
                var act = e.TryActivateSpellTrap(p, pe, fromHand: true);
                var tokens = Legal(e);
                var two = tokens.Count == 2 && tokens.All(t => t.CardId == 0);
                var picked = Drive(e, c => c?.Def != null && c.CardId == 0 &&
                                           c.Def.name.StartsWith(atk ? "+200 ATK" : "+500 DEF", StringComparison.Ordinal));
                var ok = atk
                    ? celtic.CurrentAtk == 1600 && celtic.CurrentDef == 1200
                    : celtic.CurrentAtk == 1400 && celtic.CurrentDef == 1700;
                var detail = $"act={act} tokens={Names(tokens)} picked={picked} celtic={celtic.CurrentAtk}/{celtic.CurrentDef} " +
                             $"set={set.UntilEndOfTurnAtk}/{set.UntilEndOfTurnDef} theirs={theirs.CurrentAtk}/{theirs.CurrentDef} {State(e)}";
                var during = act && two && picked && ok && set.UntilEndOfTurnAtk == 0 && set.UntilEndOfTurnDef == 0 &&
                             theirs.CurrentAtk == 2300 && theirs.CurrentDef == 2100 && Where(e, pe) == "P.gy";
                e.TryEndTurnSafe(p);
                Check($"Pyramid Energy: the player picks {(atk ? "+200 ATK" : "+500 DEF")} — only that option, only your face-up monsters, until the End Phase",
                    during && celtic.CurrentAtk == 1400 && celtic.CurrentDef == 1200,
                    detail + $" after={celtic.CurrentAtk}/{celtic.CurrentDef}");
            }
        }

        static void PyramidEnergyAiAndLegality(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var o = e.Opponent;
                var silver = PlaceMonster(e, o, SilverFang, 0, BattlePosition.Attack, true);
                var pe = PutInHand(e, o, PyramidEnergy);
                var act = e.TryActivateSpellTrap(o, pe, fromHand: true);
                Drive(e, _ => false);
                Check("Pyramid Energy: the AI picks an option by itself and resolves it (no prompt left open)",
                    reached && act && silver.CurrentAtk == 1400 && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"reached={reached} act={act} silver={silver.CurrentAtk}/{silver.CurrentDef} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Celtic, 0, BattlePosition.Defense, false);
                var pe = PutInHand(e, e.Player, PyramidEnergy);
                Check("Pyramid Energy: you control no face-up monster — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, pe, fromHand: true), State(e));
            }
        }

        // ───────────────────────────── Secret Pass to the Treasures ─────────────────────────────

        static DuelEngine PlayerBattleReady(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var i = 0; i < 8 && !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.InMainPhase); i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            if (e.IsAwaitingResponse) e.PassResponse();
            reached = e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1;
            ClearBoard(e);
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                who.Hand.Clear();
                who.Graveyard.Clear();
            }

            return e;
        }

        static void SecretPassAttacksDirectly(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = PlayerBattleReady(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
            PlaceMonster(e, p, Celtic, 1, BattlePosition.Attack, true);
            PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var pass = PutInHand(e, p, SecretPass);
            var act = e.TryActivateSpellTrap(p, pass, fromHand: true);
            var targets = Legal(e).ToList();
            Drive(e, c => c == elf);
            var battle = e.TryEnterBattlePhase(p);
            if (e.IsAwaitingResponse) e.PassResponse();
            var oLp = o.LifePoints;
            var attack = e.TryAttack(p, elf, null);
            DrainCombat(e);
            Check("Secret Pass to the Treasures: the 800-ATK monster attacks directly although the opponent has a monster",
                reached && act && battle && attack && o.LifePoints == oLp - 800 && Where(e, pass) == "P.gy" &&
                (targets.Count == 0 || !targets.Any(t => t != elf)),
                $"reached={reached} act={act} targets={Names(targets)} battle={battle} attack={attack} o {oLp}->{o.LifePoints} {State(e)}");
        }

        static void SecretPassTargets(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Player, MysticalElf, 1, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, MysticalElf, 0, BattlePosition.Attack, true);
                var pass = PutInHand(e, e.Player, SecretPass);
                Check("Secret Pass to the Treasures: no face-up monster of yours with 1000 or less ATK — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, pass, fromHand: true), State(e));
            }
            {
                // The grant ends with the turn.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Attack, true);
                var pass = PutInHand(e, p, SecretPass);
                var act = e.TryActivateSpellTrap(p, pass, fromHand: true);
                Drive(e, c => c == elf);
                PlaceMonster(e, e.Opponent, Gaia, 0, BattlePosition.Attack, true);
                var now = e.GrantsDirectAttack(elf);
                e.TryEndTurnSafe(p);
                var next = e.GrantsDirectAttack(elf);
                Check("Secret Pass to the Treasures: the direct attack is granted this turn only",
                    act && now && !next, $"act={act} now={now} next={next} {State(e)}");
            }
        }

        // ───────────────────────────── Needle Ceiling ─────────────────────────────

        static void NeedleCeilingDestroysFaceUps(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var gaia = PlaceMonster(e, p, Gaia, 0, BattlePosition.Attack, true);
            var mySet = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var fang = PlaceMonster(e, o, SilverFang, 1, BattlePosition.Defense, true);
            var needle = PlaceSetTrap(e, p, NeedleCeiling, 0);
            var act = e.TryActivateSpellTrap(p, needle, fromHand: false);
            Drive(e, _ => false);
            Check("Needle Ceiling: 4 monsters on the field — every face-up monster is destroyed, the Set one stays",
                act && Where(e, gaia) == "P.gy" && Where(e, celtic) == "O.gy" && Where(e, fang) == "O.gy" &&
                Where(e, mySet) == "P.mz" && Where(e, needle) == "P.gy",
                $"act={act} gaia={Where(e, gaia)} celtic={Where(e, celtic)} fang={Where(e, fang)} set={Where(e, mySet)} {State(e)}");
        }

        static void NeedleCeilingCondition(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Gaia, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Opponent, SilverFang, 1, BattlePosition.Attack, true);
                var needle = PlaceSetTrap(e, e.Player, NeedleCeiling, 0);
                Check("Needle Ceiling: 3 monsters on the field — cannot activate",
                    !e.CanActivateSpellTrap(e.Player, needle, fromHand: false), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Gaia, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Player, Celtic, 1, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, SilverFang, 1, BattlePosition.Defense, false);
                var needle = PlaceSetTrap(e, e.Player, NeedleCeiling, 0);
                Check("Needle Ceiling: 4 monsters, all face-down — nothing to destroy, cannot activate",
                    !e.CanActivateSpellTrap(e.Player, needle, fromHand: false), State(e));
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                PlaceMonster(e, e.Player, Gaia, 0, BattlePosition.Attack, true);
                PlaceMonster(e, e.Player, Celtic, 1, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, Celtic, 0, BattlePosition.Defense, false);
                PlaceMonster(e, e.Opponent, SilverFang, 1, BattlePosition.Defense, false);
                var needle = PlaceSetTrap(e, e.Player, NeedleCeiling, 0);
                Check("Needle Ceiling: face-down monsters count toward the 4",
                    e.CanActivateSpellTrap(e.Player, needle, fromHand: false), State(e));
            }
        }

        static void NeedleCeilingAnswersAnAttack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var battle = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var gaia = PlaceMonster(e, o, Gaia, 0, BattlePosition.Attack, true);
            var fang = PlaceMonster(e, o, SilverFang, 1, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var set = PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
            var needle = PlaceSetTrap(e, p, NeedleCeiling, 0);
            e.NotifyPublic();
            var pLp = p.LifePoints;
            var atk = e.TryAttack(o, gaia, celtic);
            var window = e.IsAwaitingResponse && e.PendingResponse?.Responder == p;
            var act = window && e.TryActivateSpellTrap(p, needle, fromHand: false);
            Drive(e, _ => false);
            DrainCombat(e);
            Check("Needle Ceiling: answers an attack — the attacker and every other face-up monster are destroyed",
                battle && atk && window && act && Where(e, gaia) == "O.gy" && Where(e, fang) == "O.gy" &&
                Where(e, celtic) == "P.gy" && Where(e, set) == "P.mz" && p.LifePoints == pLp,
                $"battle={battle} atk={atk} window={window} act={act} gaia={Where(e, gaia)} p={p.LifePoints} {State(e)}");
        }

        static void NeedleCeilingAiUseItOrLoseIt(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var aiAhead in new[] { true, false })
            {
                // The player's Heavy Storm would destroy the AI's Set Needle Ceiling: it chains it only when the
                // opponent loses more face-up ATK.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                var mine = PlaceMonster(e, p, aiAhead ? Celtic : Gaia, 0, BattlePosition.Attack, true);
                PlaceMonster(e, p, MysticalElf, 1, BattlePosition.Defense, false);
                var theirs = PlaceMonster(e, o, aiAhead ? Gaia : SilverFang, 0, BattlePosition.Attack, true);
                PlaceMonster(e, o, GiantSoldier, 1, BattlePosition.Defense, false);
                var needle = PlaceSetTrap(e, o, NeedleCeiling, 0);
                var storm = PutInHand(e, p, HeavyStorm);
                var act = e.TryActivateSpellTrap(p, storm, fromHand: true);
                Drive(e, _ => false);
                var ok = aiAhead
                    ? Where(e, mine) == "P.mz" && Where(e, theirs) == "O.mz"
                    : Where(e, mine) == "P.gy" && Where(e, theirs) == "O.gy";
                Check($"Needle Ceiling (AI): {(aiAhead ? "kept back when it would cost the AI more" : "chained before a Heavy Storm when it hurts the opponent more")}",
                    act && ok && Where(e, needle) == "O.gy" && !e.Chain.HasLinks,
                    $"act={act} mine={Where(e, mine)} theirs={Where(e, theirs)} needle={Where(e, needle)} {State(e)}");
            }
        }

        // ───────────────────────────── Hieroglyph Lithograph ─────────────────────────────

        static void HieroglyphRaisesHandLimit(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            for (var i = 0; i < 8; i++) PutInHand(e, p, Celtic);
            var lith = PutInHand(e, p, Hieroglyph);
            var lp = p.LifePoints;
            var act = e.TryActivateSpellTrap(p, lith, fromHand: true);
            Drive(e, _ => false);
            var paid = p.LifePoints == lp - 1000 && p.HandSizeLimitByEffect == 7 && Where(e, lith) == "P.gy";
            e.TryEndTurnSafe(p);
            var kept = p.Hand.Count;
            Check("Hieroglyph Lithograph: pay 1000 LP; at the End Phase you discard down to 7, not 6",
                act && paid && kept == 7, $"act={act} lp {lp}->{p.LifePoints} limit={p.HandSizeLimitByEffect} hand={kept} {State(e)}");
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        static void HieroglyphVersusMistAndInfiniteCards(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // Enervating Mist already applying when the Lithograph resolves: the later "becomes 7" wins.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                FaceUpSpellTrap(e, e.Opponent, EnervatingMist, 0);
                var before = ContinuousProtections.HandSizeLimitEndPhaseFor(e, p);
                var lith = PutInHand(e, p, Hieroglyph);
                var act = e.TryActivateSpellTrap(p, lith, fromHand: true);
                Drive(e, _ => false);
                var after = ContinuousProtections.HandSizeLimitEndPhaseFor(e, p);
                Check("Hieroglyph Lithograph after Enervating Mist: your limit is 7 (applied later)",
                    act && before == 5 && after == 7, $"before={before} after={after}");
            }
            {
                // Mist activated after the Lithograph: its "becomes 5" is the later one; when it leaves, 7 again.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var lith = PutInHand(e, p, Hieroglyph);
                var act = e.TryActivateSpellTrap(p, lith, fromHand: true);
                Drive(e, _ => false);
                var mist = FaceUpSpellTrap(e, e.Opponent, EnervatingMist, 0);
                var withMist = ContinuousProtections.HandSizeLimitEndPhaseFor(e, p);
                e.Opponent.SpellTrapZones[0].Occupant = null;
                var gone = ContinuousProtections.HandSizeLimitEndPhaseFor(e, p);
                var oppOwn = ContinuousProtections.HandSizeLimitEndPhaseFor(e, e.Opponent);
                Check("Enervating Mist after Hieroglyph Lithograph: 5 while it applies, 7 once it is gone; the opponent stays at 6",
                    act && mist != null && withMist == 5 && gone == 7 && oppOwn == 6,
                    $"withMist={withMist} gone={gone} opp={oppOwn}");
            }
            {
                // Infinite Cards: no hand size limit at all, Lithograph or not.
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var lith = PutInHand(e, p, Hieroglyph);
                var act = e.TryActivateSpellTrap(p, lith, fromHand: true);
                Drive(e, _ => false);
                FaceUpSpellTrap(e, p, InfiniteCards, 1);
                for (var i = 0; i < 9; i++) PutInHand(e, p, Celtic);
                var held = p.Hand.Count;
                e.TryEndTurnSafe(p);
                Check("Hieroglyph Lithograph + Infinite Cards: no discard at the End Phase",
                    act && p.Hand.Count == held, $"held={held} after={p.Hand.Count}");
            }
        }

        // ───────────────────────────── Card Shuffle ─────────────────────────────

        static void CardShufflePlayerPicksADeck(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            foreach (var mode in new[] { "Opponent's Deck", "Your Deck", "cancel" })
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var o = e.Opponent;
                SeedDeck(p);
                SeedDeck(o);
                var mine = p.Deck.ToList();
                var theirs = o.Deck.ToList();
                var cs = PutInHand(e, p, CardShuffle);
                var placed = e.TryActivateSpellTrap(p, cs, fromHand: true);
                Drive(e, _ => false);
                var onField = cs.FaceUp && p.TryFindSpellTrap(cs, out _);
                var lp = p.LifePoints;
                var fired = e.TryActivateSpellTrap(p, cs, fromHand: false);
                var tokens = Legal(e);
                var asked = tokens.Count == 2 && tokens.All(t => t.CardId == 0) && e.PendingActivation.HoldsChainResolution;
                if (mode == "cancel")
                    e.CancelEffectTargeting();
                else
                    Drive(e, c => c?.Def != null && c.CardId == 0 && c.Def.name == mode);
                var oppShuffled = !o.Deck.SequenceEqual(theirs);
                var ownShuffled = !p.Deck.SequenceEqual(mine);
                var wantOpp = mode == "Opponent's Deck";
                Check($"Card Shuffle: pay 300 LP, the player picks at resolution ({mode}) — only that Deck is shuffled",
                    placed && onField && fired && asked && p.LifePoints == lp - 300 && oppShuffled == wantOpp &&
                    ownShuffled == !wantOpp && p.TryFindSpellTrap(cs, out _) && !e.Chain.HasLinks && !e.IsAwaitingEffectTarget,
                    $"placed={placed} onField={onField} fired={fired} tokens={Names(tokens)} lp {lp}->{p.LifePoints} " +
                    $"opp={oppShuffled} own={ownShuffled} {State(e)}");
            }
        }

        static void SeedDeck(DuelistState who)
        {
            who.Deck.Clear();
            who.Deck.AddRange(new[]
            {
                Celtic, MysticalElf, Gaia, Koumori, SilverFang, GiantSoldier, PoisonMummy, PotOfGreed, 4206964, 44095762,
                50913601, 19613556, 14087893, 12607053, 5053103, 89631139
            });
        }

        static void CardShuffleLimits(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var cs = FaceUpSpellTrap(e, p, CardShuffle, 0);
                var first = e.TryActivateSpellTrap(p, cs, fromHand: false);
                Drive(e, c => c?.CardId == 0);
                var lp = p.LifePoints;
                var again = e.CanActivateSpellTrap(p, cs, fromHand: false);
                Check("Card Shuffle: once per turn", first && !again && p.LifePoints == lp, $"first={first} again={again} {State(e)}");
            }
            {
                var e = FreshCleared(db, pDeck, aDeck);
                var p = e.Player;
                var cs = FaceUpSpellTrap(e, p, CardShuffle, 0);
                p.LifePoints = 200;
                Check("Card Shuffle: fewer than 300 LP — cannot use", !e.CanActivateSpellTrap(p, cs, fromHand: false),
                    State(e));
            }
            {
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var cs = FaceUpSpellTrap(e, e.Player, CardShuffle, 0);
                Check("Card Shuffle: not during the opponent's turn",
                    reached && !e.CanActivateSpellTrap(e.Player, cs, fromHand: false), State(e));
            }
            {
                // The AI resolves its own use without a prompt (shuffles its own Deck).
                var e = Fresh(db, pDeck, aDeck);
                var reached = ReachOpponentMain(e);
                ClearBoard(e);
                var o = e.Opponent;
                SeedDeck(o);
                var before = o.Deck.ToList();
                var cs = FaceUpSpellTrap(e, o, CardShuffle, 0);
                var lp = o.LifePoints;
                var fired = e.TryActivateSpellTrap(o, cs, fromHand: false);
                Drive(e, _ => false);
                Check("Card Shuffle: the AI's use resolves without a prompt",
                    reached && fired && o.LifePoints == lp - 300 && !o.Deck.SequenceEqual(before) &&
                    !e.IsAwaitingEffectTarget && !e.Chain.HasLinks,
                    $"reached={reached} fired={fired} lp {lp}->{o.LifePoints} {State(e)}");
            }
        }

        /// <summary>
        /// SetCountLimit(1) without a code: a Card Shuffle that left the field and was activated again the same turn is
        /// a new card, so its effect can be used again (review finding, v162).
        /// </summary>
        static void CardShuffleIsANewCardAfterLeavingTheField(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            SeedDeck(p);
            SeedDeck(e.Opponent);
            var cs = PutInHand(e, p, CardShuffle);
            var placed = e.TryActivateSpellTrap(p, cs, fromHand: true);
            Drive(e, _ => false);
            var first = e.TryActivateSpellTrap(p, cs, fromHand: false);
            Drive(e, c => c?.CardId == 0);
            var usedUp = !e.CanActivateSpellTrap(p, cs, fromHand: false);
            var bounced = e.ReturnCardToHand(cs) && p.Hand.Contains(cs);
            var again = e.TryActivateSpellTrap(p, cs, fromHand: true);
            Drive(e, _ => false);
            var onField = cs.FaceUp && p.TryFindSpellTrap(cs, out _);
            var fresh = e.CanActivateSpellTrap(p, cs, fromHand: false);
            Check("Card Shuffle: once per turn per copy on the field — returned to the hand and activated again, it can be used",
                placed && first && usedUp && bounced && again && onField && fresh,
                $"placed={placed} first={first} usedUp={usedUp} bounced={bounced} again={again} onField={onField} " +
                $"fresh={fresh} {State(e)}");
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
            $"response={e.IsAwaitingResponse} links={e.Chain.HasLinks} phase={e.Phase} turn={e.TurnPlayer?.Name}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/{c.Zone}/side={c.Side}/amt={c.Amount}/def={c.DefAmount}/" +
                             $"cond={c.ActivationCondition}:{c.ConditionAmount}/choice={c.EffectChoiceIndex}/lp={c.PayLpAmount}/" +
                             $"race={c.BecomesRace}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}] unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";
        }
    }
}
