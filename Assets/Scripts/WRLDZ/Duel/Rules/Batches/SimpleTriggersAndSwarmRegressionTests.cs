using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// SimpleTriggersAndSwarm batch: Tsukuyomi's summon / flip trigger, the "When this card is sent to the GY" (from
    /// anywhere) triggers of Burning Algae and Outstanding Dog Marron, Little-Winguard's optional End Phase position
    /// change, the "Special Summon any number of X from your hand" of Don Turtle and Mustering of the Dark Scorpions,
    /// and Dark Scorpion Combination's direct attacks for 400.
    /// </summary>
    public static class SimpleTriggersAndSwarmRegressionTests
    {
        const int Tsukuyomi = 34853266;
        const int Marron = 11548522;
        const int Winguard = 90790253;
        const int Algae = 41859700;
        const int DonTurtle = 3493978;
        const int Mustering = 68191243;
        const int Combination = 20858318;
        const int DonZaloog = 76922029;
        const int Cliff = 6967870;
        const int Chick = 61587183;
        const int Gorg = 48768179;
        const int Meanae = 74153887;
        const int Celtic = 91152256;
        const int Kuriboh = 40640057;
        const int Necrovalley = 47355498;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            if (db == null) return;
            Action<string, bool, string> Check = (n, ok, d) => check("SimpleTriggersAndSwarm: " + n, ok, d ?? "");
            CompileShapes(db, Check);
            NearMisses(Check);
            TsukuyomiSetsATarget(db, pDeck, aDeck, Check);
            BurningAlgaeFromAnywhere(db, pDeck, aDeck, Check);
            MarronShufflesBack(db, pDeck, aDeck, Check);
            WinguardEndPhase(db, pDeck, aDeck, Check);
            DonTurtleSwarm(db, pDeck, aDeck, Check);
            MusteringOneOfEach(db, pDeck, aDeck, Check);
            CombinationDirectFor400(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── helpers ─────────────────────────

        static string Frags(CompiledCardProgram p) =>
            p == null ? "null" : $"full={p.FullyCompiled} | {string.Join(" | ", p.UnparsedFragments ?? Array.Empty<string>())}";

        static bool Has(CompiledCardProgram p, Func<EffectClause, bool> f) =>
            p != null && p.ClauseList.Exists(c => c != null && f(c));

        static CompiledCardProgram CompileText(string text, string type = "Effect Monster")
        {
            var spellTrap = type.Contains("Spell") || type.Contains("Trap");
            return CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990177, name = "Near-miss Swarm", type = type,
                frameType = type.Contains("Spell") ? "spell" : type.Contains("Trap") ? "trap" : "effect",
                race = spellTrap ? "Normal" : "Warrior", attribute = spellTrap ? null : "DARK",
                level = spellTrap ? 0 : 3, atk = 1000, def = 1000, desc = text
            });
        }

        static DuelEngine PlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var e = Fresh(db, pDeck, aDeck);
            for (var t = 0; t < 10 &&
                            !(e.TurnPlayer == e.Player && e.TurnNumber >= 3 && e.Phase == DuelPhase.Main1); t++)
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

        static void Settle(DuelEngine e)
        {
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
            e.NotifyPublic();
            for (var i = 0; i < 8 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static void EndTurn(DuelEngine e)
        {
            if (e.IsAwaitingResponse) e.PassResponse();
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static bool InMz(DuelistState who, CardInstance c) => who.MonsterZones.Any(z => z.Occupant == c);

        static int DeckCount(DuelistState who, int id)
        {
            var n = 0;
            for (var i = 0; i < who.Deck.Count; i++)
                if (who.Deck[i] == id) n++;
            return n;
        }

        // ───────────────────────── compile shapes ─────────────────────────

        static void CompileShapes(CardDatabase db, Action<string, bool, string> Check)
        {
            bool Implemented(int id) => CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

            var tsu = CardTextEffectCompiler.Compile(db.Get(Tsukuyomi));
            Check("Tsukuyomi compiles: Normal Summoned / flipped face-up → set 1 face-up monster face-down (mandatory), " +
                  "plus its Spirit return",
                tsu != null && tsu.FullyCompiled && Implemented(Tsukuyomi) &&
                Has(tsu, c => c.Timing == EffectTiming.ThisCardSummoned &&
                              c.Action == EffectActionKind.SetTargetFaceDownDefense &&
                              c.Zone == EffectZoneFilter.FieldAnyMonster && c.RequiresTargetFaceUp && c.TargetMayBeThisCard &&
                              c.RequiresThisNormalOrFlipSummoned && c.AlsoWhenFlippedFaceUp && !c.IsOptional) &&
                Has(tsu, c => c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ReturnToHand) &&
                !Has(tsu, c => c.Timing == EffectTiming.Activate),
                Frags(tsu));

            var algae = CardTextEffectCompiler.Compile(db.Get(Algae));
            Check("Burning Algae compiles: sent to the GY (anywhere) → the opponent gains 1000",
                algae != null && algae.FullyCompiled && Implemented(Algae) && algae.ClauseList.Count == 1 &&
                Has(algae, c => c.Timing == EffectTiming.SentToGyFromAnywhere &&
                                c.Action == EffectActionKind.GainLifePoints && c.Side == EffectSide.Opponent &&
                                c.Amount == 1000 && !c.IsOptional),
                Frags(algae));

            var marron = CardTextEffectCompiler.Compile(db.Get(Marron));
            Check("Outstanding Dog Marron compiles: sent to your GY (anywhere) → shuffle it into the Deck",
                marron != null && marron.FullyCompiled && Implemented(Marron) && marron.ClauseList.Count == 1 &&
                Has(marron, c => c.Timing == EffectTiming.SentToGyFromAnywhere &&
                                 c.Action == EffectActionKind.ShuffleThisFromGyIntoDeck && !c.IsOptional),
                Frags(marron));

            var wing = CardTextEffectCompiler.Compile(db.Get(Winguard));
            Check("Little-Winguard compiles: optional, once per turn, your End Phase position change",
                wing != null && wing.FullyCompiled && Implemented(Winguard) && wing.ClauseList.Count == 1 &&
                Has(wing, c => c.Timing == EffectTiming.EndPhase && c.Action == EffectActionKind.ChangeThisBattlePosition &&
                               c.IsOptional && c.OncePerTurn && !c.OpponentTurnOnly),
                Frags(wing));

            var turtle = CardTextEffectCompiler.Compile(db.Get(DonTurtle));
            Check("Don Turtle compiles: Normal / Flip Summoned → optionally Special Summon any number of copies from hand",
                turtle != null && turtle.FullyCompiled && Implemented(DonTurtle) && turtle.ClauseList.Count == 1 &&
                Has(turtle, c => c.Timing == EffectTiming.ThisCardSummoned &&
                                 c.Action == EffectActionKind.SpecialSummonAnyNumberFromHand &&
                                 c.NamedCard == "Don Turtle" && !c.NamedCardIsSeries && c.RequiresNormalOrFlipSummon &&
                                 c.IsOptional && !c.SummonOnlyOneOfEachName),
                Frags(turtle));

            var muster = CardTextEffectCompiler.Compile(db.Get(Mustering));
            Check("Mustering of the Dark Scorpions compiles: face-up Don Zaloog → any number of different Dark Scorpions",
                muster != null && muster.FullyCompiled && Implemented(Mustering) && muster.ClauseList.Count == 1 &&
                Has(muster, c => c.Timing == EffectTiming.Activate &&
                                 c.Action == EffectActionKind.SpecialSummonAnyNumberFromHand &&
                                 c.NamedCard == "Dark Scorpion" && c.NamedCardIsSeries && c.SummonOnlyOneOfEachName &&
                                 c.ConditionName == "Don Zaloog" &&
                                 c.ActivationCondition == SimpleTriggersAndSwarmRuntime.FaceUpNamedAndHandSummonable),
                Frags(muster));

            var combo = CardTextEffectCompiler.Compile(db.Get(Combination));
            Check("Dark Scorpion Combination compiles: the 5 named face-up → direct attacks, battle damage becomes 400",
                combo != null && combo.FullyCompiled && Implemented(Combination) && combo.ClauseList.Count == 1 &&
                Has(combo, c => c.Timing == EffectTiming.Activate &&
                                c.Action == EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn &&
                                c.Amount == 400 && (c.ConditionName ?? "").Split('|').Length == 5 &&
                                c.ActivationCondition == SimpleTriggersAndSwarmRuntime.AllNamedFaceUpInYourMonsterZones),
                Frags(combo));
        }

        static void NearMisses(Action<string, bool, string> Check)
        {
            var ownGain = CompileText("When this card is sent to the Graveyard, increase your Life Points by 1000 points.");
            Check("near miss: \"increase your Life Points\" is not Burning Algae's opponent gain",
                !Has(ownGain, c => c.Timing == EffectTiming.SentToGyFromAnywhere), Frags(ownGain));
            var fromField = CompileText("When this card is sent from the field to the Graveyard, add it to your Deck " +
                                        "and shuffle it.");
            Check("near miss: a field-only send is not the from-anywhere Marron trigger",
                !Has(fromField, c => c.Timing == EffectTiming.SentToGyFromAnywhere), Frags(fromField));
            var special = CompileText("When this card is Special Summoned, you can Special Summon any number of " +
                                      "\"Near-miss Swarm\"(s) from your hand.");
            Check("near miss: a Special Summon trigger is not Don Turtle's Normal / Flip Summon swarm",
                !Has(special, c => c.Action == EffectActionKind.SpecialSummonAnyNumberFromHand), Frags(special));
            var noOnly = CompileText("If you control a face-up \"Don Zaloog\": You can Special Summon any number of " +
                                     "\"Dark Scorpion\" monsters from your hand.", "Spell Card");
            Check("near miss: Mustering without \"only 1 copy of each\" does not compile to the swarm",
                !Has(noOnly, c => c.Action == EffectActionKind.SpecialSummonAnyNumberFromHand), Frags(noOnly));
            var four = CompileText("You can only activate this card when there are \"Don Zaloog\", \"Cliff the Trap " +
                                   "Remover\", \"Dark Scorpion - Chick the Yellow\", \"Dark Scorpion - Gorg the Strong\", " +
                                   "and \"Dark Scorpion - Meanae the Thorn\" face-up on your side of the field. During " +
                                   "this turn, any of these 4 cards can attack your opponent's Life Points directly. In " +
                                   "that case, the Battle Damage inflicted to your opponent by each of those cards " +
                                   "becomes 400 points.", "Trap Card");
            Check("near miss: a name count that disagrees with \"these N cards\" fails closed",
                four != null && !four.FullyCompiled &&
                !Has(four, c => c.Action == EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn),
                Frags(four));
            var eachEnd = CompileText("Once per turn, during each End Phase: You can change the battle position of this card.");
            Check("near miss: \"each End Phase\" is not Little-Winguard's \"your End Phase\" atom",
                !Has(eachEnd, c => c.Timing == EffectTiming.EndPhase && c.IsOptional && c.OncePerTurn &&
                                   c.Action == EffectActionKind.ChangeThisBattlePosition),
                Frags(eachEnd));
            var nsOnly = CompileText("If this card is Normal Summoned: Target 1 face-up monster on the field; change " +
                                     "that target to face-down Defense Position.");
            Check("near miss: \"Normal Summoned\" alone is not Tsukuyomi's \"or flipped face-up\" trigger",
                !Has(nsOnly, c => c.AlsoWhenFlippedFaceUp && c.Action == EffectActionKind.SetTargetFaceDownDefense),
                Frags(nsOnly));
        }

        // ───────────────────────── Tsukuyomi ─────────────────────────

        static void TsukuyomiSetsATarget(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var setMon = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Defense, false);
            var tsu = PutInHand(e, p, Tsukuyomi);
            var ns = e.TryNormalSummon(p, tsu, false);
            if (e.IsAwaitingResponse) e.PassResponse();
            var pending = e.PendingActivation;
            var offered = e.IsAwaitingEffectTarget && pending != null && pending.LegalTargets.Contains(celtic) &&
                          pending.LegalTargets.Contains(tsu) && !pending.LegalTargets.Contains(setMon);
            var picked = offered && e.TrySelectEffectTarget(celtic);
            Settle(e);
            Check("Tsukuyomi: Normal Summoned → a face-up monster (itself included, not a Set one) is changed to " +
                  "face-down Defense Position",
                reached && ns && offered && picked && !celtic.FaceUp && celtic.Position == BattlePosition.Defense &&
                tsu.FaceUp && !e.IsAwaitingEffectTarget,
                $"reached={reached} ns={ns} offered={offered} picked={picked} celtic={celtic.FaceUp}/{celtic.Position}");

            // Flip Summoned: the trigger again (the only face-up monster is Tsukuyomi itself → it is turned face-down).
            var e2 = PlayerMain1(db, pDeck, aDeck, out var reached2);
            var p2 = e2.Player;
            var setTsu = PlaceMonster(e2, p2, Tsukuyomi, 0, BattlePosition.Defense, false);
            var flipped = e2.TryFlipSummon(p2, setTsu);
            Settle(e2);
            if (e2.IsAwaitingEffectTarget && e2.IsLegalEffectTarget(setTsu)) e2.TrySelectEffectTarget(setTsu);
            Settle(e2);
            Check("Tsukuyomi: Flip Summoned with no other face-up monster → it must turn itself face-down",
                reached2 && flipped && !setTsu.FaceUp && setTsu.Position == BattlePosition.Defense &&
                !e2.IsAwaitingEffectTarget,
                $"reached={reached2} flipped={flipped} face={setTsu.FaceUp}/{setTsu.Position}");
        }

        // ───────────────────────── Burning Algae ─────────────────────────

        static void BurningAlgaeFromAnywhere(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var inHand = PutInHand(e, p, Algae);
            var lp = o.LifePoints;
            var myLp = p.LifePoints;
            e.SendCardToGrave(p, inHand);
            Settle(e);
            Check("Burning Algae: sent from the hand to the GY → the opponent gains 1000 LP",
                reached && p.Graveyard.Contains(inHand) && o.LifePoints == lp + 1000 && p.LifePoints == myLp,
                $"reached={reached} opp {lp}->{o.LifePoints} me {myLp}->{p.LifePoints}");

            var onField = PlaceMonster(e, p, Algae, 0, BattlePosition.Attack, true);
            lp = o.LifePoints;
            e.DestroyMonsterPublic(p, onField);
            Settle(e);
            Check("Burning Algae: destroyed on the field and sent to the GY → the opponent gains 1000 LP again",
                p.Graveyard.Contains(onField) && o.LifePoints == lp + 1000, $"opp {lp}->{o.LifePoints}");

            // The opponent's own copy: its owner's opponent (the player) gains.
            var theirs = PutInHand(e, o, Algae);
            var pLp = p.LifePoints;
            var oLp = o.LifePoints;
            e.SendCardToGrave(o, theirs);
            Settle(e);
            Check("Burning Algae: the opponent's copy makes ITS opponent (the player) gain 1000 LP",
                o.Graveyard.Contains(theirs) && p.LifePoints == pLp + 1000 && o.LifePoints == oLp,
                $"player {pLp}->{p.LifePoints} opp {oLp}->{o.LifePoints}");

            // It left the GY before its trigger could activate: its single effect was reset, no LP gain.
            var gone = PutInHand(e, p, Algae);
            lp = o.LifePoints;
            e.SendCardToGrave(p, gone);
            p.Graveyard.Remove(gone);
            p.Banished.Add(gone);
            Settle(e);
            Check("Burning Algae: no longer in the GY when its trigger would activate → no LP gain",
                o.LifePoints == lp, $"opp {lp}->{o.LifePoints}");
        }

        // ───────────────────────── Outstanding Dog Marron ─────────────────────────

        static void MarronShufflesBack(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var before = DeckCount(p, Marron);
            var deckSize = p.Deck.Count;
            var dog = PutInHand(e, p, Marron);
            e.SendCardToGrave(p, dog);
            Settle(e);
            Check("Outstanding Dog Marron: sent from the hand to the GY → it is shuffled into the Deck",
                reached && !p.Graveyard.Contains(dog) && DeckCount(p, Marron) == before + 1 &&
                p.Deck.Count == deckSize + 1,
                $"reached={reached} gy={p.Graveyard.Contains(dog)} copies {before}->{DeckCount(p, Marron)}");

            var field = PlaceMonster(e, p, Marron, 0, BattlePosition.Attack, true);
            before = DeckCount(p, Marron);
            e.DestroyMonsterPublic(p, field);
            Settle(e);
            Check("Outstanding Dog Marron: destroyed on the field → back into the Deck",
                !p.Graveyard.Contains(field) && !InMz(p, field) && DeckCount(p, Marron) == before + 1,
                $"gy={p.Graveyard.Contains(field)} copies {before}->{DeckCount(p, Marron)}");

            // Necrovalley: nothing in the GY can be moved — Marron stays.
            var vally = e.CreateCardInstance(Necrovalley);
            vally.FaceUp = true;
            e.Opponent.FieldSpellZone.Occupant = vally;
            FieldSpellEffects.RefreshBoard(e);
            var held = PutInHand(e, p, Marron);
            before = DeckCount(p, Marron);
            e.SendCardToGrave(p, held);
            Settle(e);
            Check("Outstanding Dog Marron: Necrovalley keeps it in the GY",
                p.Graveyard.Contains(held) && DeckCount(p, Marron) == before,
                $"gy={p.Graveyard.Contains(held)} copies {before}->{DeckCount(p, Marron)}");
        }

        // ───────────────────────── Little-Winguard ─────────────────────────

        static void WinguardEndPhase(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var mine = PlaceMonster(e, p, Winguard, 0, BattlePosition.Attack, true);
            var theirs = PlaceMonster(e, o, Winguard, 0, BattlePosition.Attack, true);
            e.TryEndTurnSafe(p);
            var asked = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(mine) && !e.IsLegalEffectTarget(theirs);
            var tapped = asked && e.TrySelectEffectTarget(mine);
            Settle(e);
            Check("Little-Winguard: your End Phase asks; tapping it changes it to face-up Defense Position",
                reached && asked && tapped && mine.Position == BattlePosition.Defense && mine.FaceUp &&
                theirs.Position == BattlePosition.Attack && e.TurnPlayer == o,
                $"reached={reached} asked={asked} tapped={tapped} mine={mine.Position} theirs={theirs.Position} " +
                $"turn={(e.TurnPlayer == o ? "opp" : "player")}");

            // The opponent's End Phase: not the player's ("your End Phase"); the AI moves its Attack Position copy to Defense.
            EndTurn(e);
            Check("Little-Winguard: the AI changes its own copy to Defense in its End Phase; the player's copy waits",
                theirs.Position == BattlePosition.Defense && mine.Position == BattlePosition.Defense &&
                e.TurnPlayer == p && !e.IsAwaitingEffectTarget,
                $"theirs={theirs.Position} mine={mine.Position} awaiting={e.IsAwaitingEffectTarget}");

            // Optional: CANCEL keeps the position.
            for (var i = 0; i < 4 && e.TurnPlayer == p && !e.IsAwaitingEffectTarget; i++)
            {
                if (e.IsAwaitingResponse) e.PassResponse();
                e.RecoverStuckCombat();
                e.TryEndTurnSafe(p);
            }

            var askedAgain = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(mine);
            if (askedAgain) e.CancelEffectTargeting();
            Settle(e);
            Check("Little-Winguard: \"You can\" — CANCEL keeps it in Defense Position and the turn ends",
                askedAgain && mine.Position == BattlePosition.Defense && e.TurnPlayer == o && !e.IsAwaitingEffectTarget,
                $"asked={askedAgain} mine={mine.Position}");
        }

        // ───────────────────────── Don Turtle ─────────────────────────

        static void DonTurtleSwarm(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var t1 = PutInHand(e, p, DonTurtle);
            var t2 = PutInHand(e, p, DonTurtle);
            var t3 = PutInHand(e, p, DonTurtle);
            var other = PutInHand(e, p, Celtic);
            var ns = e.TryNormalSummon(p, t1, false);
            Settle(e);
            var asked = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(t2) && e.IsLegalEffectTarget(t3) &&
                        !e.IsLegalEffectTarget(other);
            // The UI's placement: face-up Defense in Monster Zone 4 for the first pick.
            e.SetPendingSpecialSummonPlacement(4, BattlePosition.Defense);
            var first = asked && e.TrySelectEffectTarget(t2);
            var askedMore = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(t3) && !e.IsLegalEffectTarget(t2);
            e.SetPendingSpecialSummonPlacement(-1, BattlePosition.Attack);
            var second = askedMore && e.TrySelectEffectTarget(t3);
            Settle(e);
            Check("Don Turtle: Normal Summoned → the player picks the copies in hand one at a time; they are Special " +
                  "Summoned together in the chosen positions",
                reached && ns && asked && first && askedMore && second && InMz(p, t2) && InMz(p, t3) &&
                t2.FaceUp && t3.FaceUp && t2.Position == BattlePosition.Defense &&
                p.MonsterZones[4].Occupant == t2 && t3.Position == BattlePosition.Attack &&
                p.Hand.Contains(other) && !e.IsAwaitingEffectTarget,
                $"reached={reached} ns={ns} asked={asked} first={first} more={askedMore} second={second} " +
                $"t2={InMz(p, t2)}/{t2.Position} t3={InMz(p, t3)}/{t3.Position}");

            // CANCEL after the first pick = done.
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            var p2 = e2.Player;
            var a1 = PutInHand(e2, p2, DonTurtle);
            var a2 = PutInHand(e2, p2, DonTurtle);
            var a3 = PutInHand(e2, p2, DonTurtle);
            e2.TryNormalSummon(p2, a1, false);
            Settle(e2);
            var pickedOne = e2.IsAwaitingEffectTarget && e2.TrySelectEffectTarget(a2);
            var stillAsks = e2.IsAwaitingEffectTarget;
            if (stillAsks) e2.CancelEffectTargeting();
            Settle(e2);
            Check("Don Turtle: CANCEL after the first pick Special Summons only the copies chosen",
                pickedOne && stillAsks && InMz(p2, a2) && p2.Hand.Contains(a3) && !e2.IsAwaitingEffectTarget,
                $"picked={pickedOne} asks={stillAsks} a2={InMz(p2, a2)} a3hand={p2.Hand.Contains(a3)}");

            // "you can": a lone copy is still asked; CANCEL at the first pick Special Summons nothing.
            var e3 = PlayerMain1(db, pDeck, aDeck, out _);
            var p3 = e3.Player;
            var b1 = PutInHand(e3, p3, DonTurtle);
            var b2 = PutInHand(e3, p3, DonTurtle);
            e3.TryNormalSummon(p3, b1, false);
            Settle(e3);
            var yesNo = e3.IsAwaitingEffectTarget && e3.IsLegalEffectTarget(b2);
            if (yesNo) e3.CancelEffectTargeting();
            Settle(e3);
            Check("Don Turtle: \"you can\" — a lone copy is still asked, and CANCEL Special Summons nothing",
                yesNo && p3.Hand.Contains(b2) && !InMz(p3, b2) && !e3.IsAwaitingEffectTarget,
                $"asked={yesNo} b2hand={p3.Hand.Contains(b2)}");

            // A Special Summon does not trigger it; the AI's Normal Summon summons every copy it holds.
            var e4 = PlayerMain1(db, pDeck, aDeck, out _);
            var p4 = e4.Player;
            var o4 = e4.Opponent;
            var c1 = PutInHand(e4, p4, DonTurtle);
            var c2 = PutInHand(e4, p4, DonTurtle);
            p4.Hand.Remove(c1);
            e4.SpecialSummonToField(p4, c1, BattlePosition.Attack, true);
            Settle(e4);
            Check("Don Turtle: Special Summoned → no trigger (Normal or Flip Summon only)",
                InMz(p4, c1) && p4.Hand.Contains(c2) && !e4.IsAwaitingEffectTarget,
                $"awaiting={e4.IsAwaitingEffectTarget}");
            var reachedOpp = ReachOpponentMain(e4);
            ClearBoard(e4);
            o4.Hand.Clear();
            var d1 = PutInHand(e4, o4, DonTurtle);
            var d2 = PutInHand(e4, o4, DonTurtle);
            var d3 = PutInHand(e4, o4, DonTurtle);
            var aiNs = e4.TryNormalSummon(o4, d1, false);
            Settle(e4);
            Check("Don Turtle: the AI's Normal Summon Special Summons every copy in its hand",
                reachedOpp && aiNs && InMz(o4, d1) && InMz(o4, d2) && InMz(o4, d3) && d2.FaceUp && d3.FaceUp &&
                !e4.IsAwaitingEffectTarget,
                $"reached={reachedOpp} ns={aiNs} d2={InMz(o4, d2)} d3={InMz(o4, d3)}");
        }

        // ───────────────────────── Mustering of the Dark Scorpions ─────────────────────────

        static void MusteringOneOfEach(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var muster = PutInHand(e, p, Mustering);
            var cliff = PutInHand(e, p, Cliff);
            var chickA = PutInHand(e, p, Chick);
            var chickB = PutInHand(e, p, Chick);
            var turtle = PutInHand(e, p, DonTurtle);
            var without = e.CanActivateSpellTrap(p, muster, fromHand: true);
            var don = PlaceMonster(e, p, DonZaloog, 0, BattlePosition.Attack, true);
            var with = e.CanActivateSpellTrap(p, muster, fromHand: true);
            var used = e.TryActivateSpellTrap(p, muster, fromHand: true);
            Settle(e);
            var asked = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(cliff) && e.IsLegalEffectTarget(chickA) &&
                        e.IsLegalEffectTarget(chickB) && !e.IsLegalEffectTarget(turtle);
            var first = asked && e.TrySelectEffectTarget(chickA);
            var noDupe = e.IsAwaitingEffectTarget && e.IsLegalEffectTarget(cliff) && !e.IsLegalEffectTarget(chickB);
            var second = noDupe && e.TrySelectEffectTarget(cliff);
            Settle(e);
            Check("Mustering of the Dark Scorpions: needs a face-up Don Zaloog; summons the chosen Dark Scorpions, only " +
                  "1 copy of each name",
                reached && !without && with && used && asked && first && noDupe && second && InMz(p, chickA) &&
                InMz(p, cliff) && chickA.FaceUp && cliff.FaceUp && p.Hand.Contains(chickB) && p.Hand.Contains(turtle) &&
                InMz(p, don) && p.Graveyard.Contains(muster) && !e.IsAwaitingEffectTarget,
                $"reached={reached} without={without} with={with} used={used} asked={asked} first={first} " +
                $"noDupe={noDupe} second={second} chickA={InMz(p, chickA)} cliff={InMz(p, cliff)}");

            // No Dark Scorpion monster in the hand: it cannot be activated (Lua s.tg chk).
            var e2 = PlayerMain1(db, pDeck, aDeck, out _);
            var p2 = e2.Player;
            PlaceMonster(e2, p2, DonZaloog, 0, BattlePosition.Attack, true);
            var m2 = PutInHand(e2, p2, Mustering);
            PutInHand(e2, p2, DonTurtle);
            Check("Mustering of the Dark Scorpions: no Dark Scorpion monster in the hand → cannot be activated",
                !e2.CanActivateSpellTrap(p2, m2, fromHand: true), "");
        }

        // ───────────────────────── Dark Scorpion Combination ─────────────────────────

        static void ClearPicks(DuelEngine e)
        {
            for (var i = 0; i < 4 && e.IsAwaitingEffectTarget; i++) e.CancelEffectTargeting();
            DrainCombat(e);
        }

        static void CombinationDirectFor400(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = e.Player;
            var o = e.Opponent;
            var don = PlaceMonster(e, p, DonZaloog, 0, BattlePosition.Attack, true);
            var cliff = PlaceMonster(e, p, Cliff, 1, BattlePosition.Attack, true);
            var chick = PlaceMonster(e, p, Chick, 2, BattlePosition.Attack, true);
            var gorg = PlaceMonster(e, p, Gorg, 3, BattlePosition.Attack, true);
            var meanae = PlaceMonster(e, p, Meanae, 4, BattlePosition.Attack, false);
            var wall = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            var kuri = PlaceMonster(e, o, Kuriboh, 1, BattlePosition.Attack, true);
            var trap = PlaceSetTrap(e, p, Combination, 0);
            var fourOnly = e.CanActivateSpellTrap(p, trap, fromHand: false);
            meanae.FaceUp = true;
            var five = e.CanActivateSpellTrap(p, trap, fromHand: false);
            var grantedBefore = SimpleTriggersAndSwarmRuntime.GrantsDirectAttack(e, gorg);
            var used = e.TryActivateSpellTrap(p, trap, fromHand: false);
            Settle(e);
            e.TryEnterBattlePhase(p);
            Settle(e);
            var directAfter = e.CanAttackDirectly(p, gorg) && e.CanAttackDirectly(p, don) &&
                              e.CanAttackDirectly(p, cliff);
            Check("Dark Scorpion Combination: only with all 5 face-up (\"Cliff the Trap Remover\" = Dark Scorpion - " +
                  "Cliff); then the named monsters can attack directly",
                reached && !fourOnly && five && used && !grantedBefore && directAfter,
                $"reached={reached} four={fourOnly} five={five} used={used} before={grantedBefore} after={directAfter}");

            // Lua e1 is a FIELD EFFECT_DIRECT_ATTACK registered to tp with SetTargetRange(LOCATION_MZONE,0): only the
            // activator's monsters. The flag (RESETS_STANDARD, no RESET_CONTROL) survives a control change, so the
            // grant comes back when the activator regains the monster.
            var taken = e.TryTakeControl(o, chick);
            var grantUnderOpp = e.GrantsDirectAttack(chick);
            var back = e.TryTakeControl(p, chick);
            var grantBack = e.GrantsDirectAttack(chick);
            Check("Dark Scorpion Combination: the direct-attack grant covers only monsters the activator controls " +
                  "(none while the opponent controls a flagged monster; back once the activator regains it)",
                taken && !grantUnderOpp && back && grantBack,
                $"taken={taken} grantUnderOpp={grantUnderOpp} back={back} grantBack={grantBack}");

            var lp = o.LifePoints;
            var attacked = e.TryAttack(p, gorg, null);
            DrainCombat(e);
            var afterDirect = o.LifePoints;
            ClearPicks(e);
            Check("Dark Scorpion Combination: Gorg the Strong's direct attack inflicts 400 (not 1800)",
                attacked && afterDirect == lp - 400, $"attacked={attacked} lp {lp}->{afterDirect}");

            lp = o.LifePoints;
            var hit = e.TryAttack(p, don, kuri);
            DrainCombat(e);
            var afterHit = o.LifePoints;
            ClearPicks(e);
            Check("Dark Scorpion Combination: an attack on a monster inflicts its normal battle damage",
                hit && afterHit == lp - 1100, $"hit={hit} lp {lp}->{afterHit} wall={InMz(o, wall)}");
        }
    }
}
