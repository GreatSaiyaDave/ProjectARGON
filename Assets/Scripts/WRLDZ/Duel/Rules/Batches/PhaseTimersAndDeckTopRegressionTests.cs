using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// PhaseTimersAndDeckTop batch (v163): Bottomless Shifting Sand, Mirage of Nightmare, Different Dimension Capsule,
    /// Pyro Clock of Destiny, A Cat of Ill Omen, An Owl of Luck and Royal Tribute. Compile shapes, mini-duels through the
    /// real engine entry points (TryActivateSpellTrap, TryFlipSummon, MonsterEffects.OnFlipSummoned, EndTurn through the
    /// End / Draw / Standby Phases, TrySelectEffectTarget / CancelEffectTargeting / TrySelectLpCost) and near misses.
    /// Runtime: <see cref="PhaseTimersAndDeckTopRuntime"/>.
    /// </summary>
    public static class PhaseTimersAndDeckTopRegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int ShiftingSand = 76532077;
        const int Mirage = 41482598;
        const int Capsule = 11961740;
        const int PyroClock = 1082946;
        const int Cat = 24140059;
        const int Owl = 23927567;
        const int RoyalTribute = 72405967;

        const int Necrovalley = 47355498;
        const int Swords = 72302403;
        const int Lightforce = 49587034;
        const int SteelScorpion = 13599884;
        const int Ddv = 35027493;
        const int DrillBug = 88733579;
        const int Messenger = 44656491;
        const int RoyalDecree = 51452091;

        const int Minar = 32539892;
        const int Celtic = 91152256;       // Lv4 1400/1200
        const int DarkBlade = 11321183;    // Lv4 1800/1500
        const int RudeKaiser = 26378150;   // Lv5 1800/1600
        const int SummonedSkull = 70781052; // Lv6 2500/1200
        const int TrapHole = 4206964;
        const int MirrorForce = 44095762;
        const int Mst = 5318639;
        const int Yami = 59197169;
        const int Sogen = 86318356;

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("PhaseTimersAndDeckTop: " + name, ok, detail ?? "");
            Shapes(db, Check);
            NearMisses(db, Check);
            ShiftingSandDuels(db, pDeck, aDeck, Check);
            MirageDuels(db, pDeck, aDeck, Check);
            CapsuleDuels(db, pDeck, aDeck, Check);
            PyroClockDuels(db, pDeck, aDeck, Check);
            DeckTopFlipDuels(db, pDeck, aDeck, Check);
            RoyalTributeDuels(db, pDeck, aDeck, Check);
            StandbyHoldDuel(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string Describe(CompiledCardProgram prog) =>
            prog == null
                ? "null"
                : $"full={prog.FullyCompiled} [{string.Join(" | ", prog.ClauseList.Select(EffectVocabulary.Describe))}] " +
                  $"unparsed={string.Join("|", prog.UnparsedFragments ?? Array.Empty<string>())}";

        static CompiledCardProgram CompileText(string type, string race, string text) =>
            CardTextEffectCompiler.Compile(new CardDef { id = 990000163, name = "Probe", type = type, race = race, desc = text });

        static bool Has(CompiledCardProgram prog, EffectActionKind action) =>
            prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);

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

        static CardInstance FieldSpell(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            who.FieldSpellZone.Occupant = c;
            return c;
        }

        static void Fill(DuelEngine engine, DuelistState who, int id, int n)
        {
            for (var i = 0; i < n; i++)
                PutInHand(engine, who, id);
        }

        static bool OnField(DuelistState who, CardInstance c) =>
            who.TryFindMonster(c, out _) || who.TryFindSpellTrap(c, out _);

        static CardInstance LegalWithId(DuelEngine engine, int id) =>
            engine.PendingActivation?.LegalTargets.FirstOrDefault(t => t?.CardId == id);

        /// <summary>Only <paramref name="keep"/> of this kind in <paramref name="who"/>'s Deck (other cards of that kind removed).</summary>
        static void DeckWith(CardDatabase db, DuelistState who, Func<CardDef, bool> kind, params int[] keep)
        {
            who.Deck.RemoveAll(id => db.Get(id) is { } d && kind(d));
            for (var i = 0; i < keep.Length; i++)
                who.Deck.Insert(Math.Min(who.Deck.Count, 3 + 4 * i), keep[i]);
        }

        // ───────────────────────────── compile shapes ─────────────────────────────

        static void Shapes(CardDatabase db, Chk Check)
        {
            var sand = CardTextEffectCompiler.Compile(db.Get(ShiftingSand));
            var sandEnd = sand?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk);
            var sandStandby = sand?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost);
            Check("Bottomless Shifting Sand compiles: opponent's End Phase highest-ATK wipe + own Standby hand check (4)",
                sand != null && sand.FullyCompiled && sand.ClauseList.Count == 2 &&
                sandEnd != null && sandEnd.Timing == EffectTiming.EndPhase && sandEnd.OpponentTurnOnly && sandEnd.MakesChainLink &&
                sandStandby != null && sandStandby.Timing == EffectTiming.StandbyPhase && !sandStandby.OpponentTurnOnly &&
                sandStandby.Amount == 4 && !sandStandby.MakesChainLink &&
                TextEffectRuntime.IsContinuousTrapPlacementOnly(db.Get(ShiftingSand), sand) &&
                Status(db, ShiftingSand) == CardEffectStatusKind.Implemented, Describe(sand));

            var mirage = CardTextEffectCompiler.Compile(db.Get(Mirage));
            var draw = mirage?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard);
            var discard = mirage?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.DiscardRandomRecordedDrawCountAtStandby);
            Check("Mirage of Nightmare compiles: opponent's Standby draw-to-4 with a record, own Standby random discard",
                mirage != null && mirage.FullyCompiled && mirage.ClauseList.Count == 2 &&
                draw != null && draw.Timing == EffectTiming.StandbyPhase && draw.OpponentTurnOnly && draw.Amount == 4 &&
                discard != null && discard.Timing == EffectTiming.StandbyPhase && !discard.OpponentTurnOnly &&
                !Has(mirage, EffectActionKind.DrawUntilHandSize) &&
                Status(db, Mirage) == CardEffectStatusKind.Implemented, Describe(mirage));

            var cap = CardTextEffectCompiler.Compile(db.Get(Capsule));
            var capC = cap?.ClauseList.FirstOrDefault();
            Check("Different Dimension Capsule compiles: Activate, stays on the field, banish face-down, needs a Deck card",
                cap != null && cap.FullyCompiled && cap.ClauseList.Count == 1 && capC != null &&
                capC.Timing == EffectTiming.Activate && capC.Action == EffectActionKind.BanishDeckCardFaceDownAddAtSecondStandby &&
                capC.StaysOnField && capC.ActivationCondition == HandDisruptionRuntime.ControllerDeckAtLeast &&
                Status(db, Capsule) == CardEffectStatusKind.Implemented, Describe(cap));

            var clock = CardTextEffectCompiler.Compile(db.Get(PyroClock));
            var clockC = clock?.ClauseList.FirstOrDefault();
            Check("Pyro Clock of Destiny compiles: Activate AdvanceTurnCountByOne gated on a running turn count (unique)",
                clock != null && clock.FullyCompiled && clock.ClauseList.Count == 1 && clockC != null &&
                clockC.Action == EffectActionKind.AdvanceTurnCountByOne &&
                clockC.ActivationCondition == PhaseTimersAndDeckTopRuntime.TurnCountExists &&
                EffectVocabulary.IsUniqueException(EffectActionKind.AdvanceTurnCountByOne) &&
                Status(db, PyroClock) == CardEffectStatusKind.Implemented, Describe(clock));

            foreach (var (id, label, kind) in new[] { (Cat, "A Cat of Ill Omen", "Trap"), (Owl, "An Owl of Luck", "FieldSpell") })
            {
                var prog = CardTextEffectCompiler.Compile(db.Get(id));
                var c = prog?.ClauseList.FirstOrDefault();
                Check($"{label} compiles: FLIP PlaceNamedFromDeckOnTop by kind {kind}, Necrovalley hand alternative",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 && c != null &&
                    c.Timing == EffectTiming.Flip && c.Action == EffectActionKind.PlaceNamedFromDeckOnTop &&
                    string.IsNullOrEmpty(c.NamedCard) && c.DeckPickCardKind == kind &&
                    c.AddToHandInsteadIfNamedOnField == "Necrovalley" && !c.RequiresTargetChoice &&
                    Status(db, id) == CardEffectStatusKind.Implemented, Describe(prog));
            }

            var drill = CardTextEffectCompiler.Compile(db.Get(DrillBug));
            var drillC = drill?.ClauseList.FirstOrDefault(c => c.Action == EffectActionKind.PlaceNamedFromDeckOnTop);
            Check("Drill Bug keeps its named Deck-top clause (no card kind, no Necrovalley alternative)",
                drillC != null && !string.IsNullOrEmpty(drillC.NamedCard) && string.IsNullOrEmpty(drillC.DeckPickCardKind) &&
                string.IsNullOrEmpty(drillC.AddToHandInsteadIfNamedOnField), Describe(drill));

            var tribute = CardTextEffectCompiler.Compile(db.Get(RoyalTribute));
            var tc = tribute?.ClauseList.FirstOrDefault();
            Check("Royal Tribute compiles: you control Necrovalley; both players discard every hand monster",
                tribute != null && tribute.FullyCompiled && tribute.ClauseList.Count == 1 && tc != null &&
                tc.Action == EffectActionKind.BothPlayersDiscardAllMonstersInHand && tc.RequiresFaceUpName == "Necrovalley" &&
                tc.RequiresControllerNamedCard &&
                tc.ActivationCondition == PhaseTimersAndDeckTopRuntime.CardOtherThanThisInEitherHand &&
                Status(db, RoyalTribute) == CardEffectStatusKind.Implemented, Describe(tribute));
        }

        static void NearMisses(CardDatabase db, Chk Check)
        {
            var ownEnd = CompileText("Trap Card", "Continuous",
                "At the end of your turn, destroy the face-up monster(s) with the highest ATK on the field. During your own " +
                "Standby Phase, if you have 4 or less cards in your hand, this card is destroyed.");
            Check("Near miss: an own-turn highest-ATK wipe is not the Shifting Sand atom",
                ownEnd != null && !ownEnd.FullyCompiled && !Has(ownEnd, EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk),
                Describe(ownEnd));

            var noDiscard = CompileText("Spell Card", "Continuous",
                "Once per turn, during your opponent's Standby Phase: Draw cards until you have 4 cards in your hand.");
            Check("Near miss: a draw-to-4 with no discard rider is not Mirage of Nightmare",
                noDiscard != null && !Has(noDiscard, EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard),
                Describe(noDiscard));

            var third = CompileText("Spell Card", "Normal",
                "Select 1 card from your Deck and remove it from play face-down. During your 3rd Standby Phase after " +
                "activation, destroy this card and add the removed card to your hand.");
            Check("Near miss: a 3rd-Standby capsule is not the Different Dimension Capsule atom",
                third != null && !third.FullyCompiled && !Has(third, EffectActionKind.BanishDeckCardFaceDownAddAtSecondStandby),
                Describe(third));

            var twoTurns = CompileText("Trap Card", "Normal", "Move the turn count forward by 2 turns.");
            Check("Near miss: 'forward by 2 turns' is not Pyro Clock of Destiny",
                twoTurns != null && !twoTurns.FullyCompiled && !Has(twoTurns, EffectActionKind.AdvanceTurnCountByOne),
                Describe(twoTurns));

            var plainCat = CompileText("Flip Effect Monster", "Beast",
                "FLIP: Choose 1 Trap from your Deck and place it on top of your Deck.");
            Check("Near miss: a Deck-top FLIP without the Necrovalley alternative stays fail-closed",
                plainCat != null && !plainCat.FullyCompiled &&
                !plainCat.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.PlaceNamedFromDeckOnTop),
                Describe(plainCat));

            var wholeHand = CompileText("Spell Card", "Normal",
                "If you control \"Necrovalley\": Both players discard their entire hands.");
            Check("Near miss: 'discard their entire hands' is not Royal Tribute",
                wholeHand != null && !Has(wholeHand, EffectActionKind.BothPlayersDiscardAllMonstersInHand),
                Describe(wholeHand));
        }

        // ───────────────────────────── Bottomless Shifting Sand ─────────────────────────────

        static void ShiftingSandDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var sand = FaceUpSt(engine, p, ShiftingSand, 0);
                var mine = PlaceMonster(engine, p, DarkBlade, 0, BattlePosition.Attack, true);
                var theirs = PlaceMonster(engine, o, RudeKaiser, 0, BattlePosition.Attack, true);
                var low = PlaceMonster(engine, o, Celtic, 1, BattlePosition.Attack, true);
                var setSkull = PlaceMonster(engine, o, SummonedSkull, 2, BattlePosition.Defense, false);
                Fill(engine, p, Celtic, 6);
                EndTurn(engine); // the player's own End Phase: nothing
                var afterOwn = OnField(p, mine) && OnField(o, theirs);
                EndTurn(engine); // the opponent's End Phase: both 1800s at once
                Check("Bottomless Shifting Sand: the opponent's End Phase destroys every tied highest-ATK face-up monster " +
                      "(both sides), not a face-down one; nothing at the end of your own turn",
                    reached && afterOwn && !OnField(p, mine) && !OnField(o, theirs) && OnField(o, low) &&
                    OnField(o, setSkull) && OnField(p, sand) && p.Graveyard.Contains(mine) && o.Graveyard.Contains(theirs),
                    $"afterOwn={afterOwn} mine={OnField(p, mine)} theirs={OnField(o, theirs)} low={OnField(o, low)} " +
                    $"skull={OnField(o, setSkull)} sand={OnField(p, sand)}");
            }

            {
                // Own Standby with 4 cards (3 + the draw): destroyed. The opponent's Standby never checks.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var sand = FaceUpSt(engine, p, ShiftingSand, 0);
                Fill(engine, p, Celtic, 1);
                EndTurn(engine); // opponent's turn: its Standby Phase does not check the player's hand
                var survivedOpp = OnField(p, sand);
                p.Hand.Clear();
                Fill(engine, p, Celtic, 3);
                EndTurn(engine); // player's Draw (4 cards) then Standby
                Check("Bottomless Shifting Sand: destroyed in your Standby Phase with 4 cards in hand; the opponent's " +
                      "Standby Phase does not check",
                    reached && survivedOpp && !OnField(p, sand) && p.Graveyard.Contains(sand),
                    $"survivedOpp={survivedOpp} onField={OnField(p, sand)} hand={p.Hand.Count}");
            }

            {
                // 5 cards (4 + the draw): stays. Negated (Royal Decree): stays with 1 card.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var sand = FaceUpSt(engine, p, ShiftingSand, 0);
                EndTurn(engine);
                p.Hand.Clear();
                Fill(engine, p, Celtic, 4);
                EndTurn(engine);
                var stayed = OnField(p, sand);
                FaceUpSt(engine, o, RoyalDecree, 0);
                engine.NotifyPublic();
                EndTurn(engine);
                p.Hand.Clear();
                EndTurn(engine);
                Check("Bottomless Shifting Sand: stays with 5 cards in hand; a negated copy (Royal Decree) is not destroyed",
                    reached && stayed && OnField(p, sand), $"stayed={stayed} negatedStays={OnField(p, sand)} hand={p.Hand.Count}");
            }
        }

        // ───────────────────────────── Mirage of Nightmare ─────────────────────────────

        static void MirageDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var mirage = PutInHand(engine, p, Mirage);
                var activated = engine.TryActivateSpellTrap(p, mirage, true);
                Settle(engine);
                Fill(engine, p, Celtic, 1);
                var deck0 = p.Deck.Count;
                EndTurn(engine); // opponent's Standby: 1 → 4
                var drew = deck0 - p.Deck.Count;
                var handAfterDraw = p.Hand.Count;
                var recorded = PhaseTimersAndDeckTopRuntime.MirageRecordedCount(engine, mirage);
                var gy0 = p.Graveyard.Count;
                EndTurn(engine); // player's Draw (5) then Standby: discard 3 at random
                Check("Mirage of Nightmare: draw until 4 in the opponent's Standby Phase, discard that many at random in yours",
                    reached && activated && drew == 3 && handAfterDraw == 4 && recorded == 3 && p.Hand.Count == 2 &&
                    p.Graveyard.Count - gy0 == 3 && PhaseTimersAndDeckTopRuntime.MirageRecordedCount(engine, mirage) == 0,
                    $"act={activated} drew={drew} hand={handAfterDraw} rec={recorded} handNow={p.Hand.Count} " +
                    $"gy+={p.Graveyard.Count - gy0}");

                // The next own Standby: no record, no discard; the opponent's Standby with 4+ cards: no draw.
                Fill(engine, p, Celtic, 3);
                var deck1 = p.Deck.Count;
                EndTurn(engine);
                var noDraw = p.Deck.Count == deck1 && PhaseTimersAndDeckTopRuntime.MirageRecordedCount(engine, mirage) == 0;
                var hand1 = p.Hand.Count;
                EndTurn(engine);
                Check("Mirage of Nightmare: no draw with 4+ cards in hand, and then no discard",
                    noDraw && p.Hand.Count == hand1 + 1, $"noDraw={noDraw} hand {hand1}->{p.Hand.Count}");
            }

            {
                // More to discard than cards in hand: the whole hand. It left the field: no discard.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var mirage = FaceUpSt(engine, p, Mirage, 0);
                EndTurn(engine); // 0 → 4 (record 4)
                var rec = PhaseTimersAndDeckTopRuntime.MirageRecordedCount(engine, mirage);
                p.Hand.Clear();
                Fill(engine, p, Celtic, 1);
                EndTurn(engine); // draw (2), discard 4 → whole hand
                Check("Mirage of Nightmare: with fewer cards than the record, the whole hand is discarded",
                    reached && rec == 4 && p.Hand.Count == 0, $"rec={rec} hand={p.Hand.Count}");

                p.Hand.Clear();
                EndTurn(engine); // opponent's Standby: record 4 again
                engine.SendCardToGrave(p, mirage);
                EndTurn(engine);
                Check("Mirage of Nightmare: after it left the field, its controller's Standby Phase discards nothing",
                    p.Hand.Count == 5, $"hand={p.Hand.Count}");
            }
        }

        // ───────────────────────────── Different Dimension Capsule ─────────────────────────────

        static void CapsuleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                p.Deck.Insert(5, SummonedSkull);
                var skulls = p.Deck.Count(id => id == SummonedSkull);
                var cap = PutInHand(engine, p, Capsule);
                var activated = engine.TryActivateSpellTrap(p, cap, true);
                Settle(engine);
                var pick = LegalWithId(engine, SummonedSkull);
                var asked = pick != null && engine.PendingActivation.LegalTargets.Count > 1;
                var picked = pick != null && engine.TrySelectEffectTarget(pick);
                Settle(engine);
                var held = PhaseTimersAndDeckTopRuntime.CapsuleBanishedCard(engine, cap);
                var banishedOk = held != null && !held.FaceUp && held.CardId == SummonedSkull && p.Banished.Contains(held) &&
                                 p.Deck.Count(id => id == SummonedSkull) == skulls - 1 && OnField(p, cap) && cap.FaceUp;
                EndTurn(engine);
                EndTurn(engine); // 1st Standby
                var firstStays = OnField(p, cap) && p.Banished.Contains(held);
                EndTurn(engine);
                EndTurn(engine); // 2nd Standby
                Check("Different Dimension Capsule: the chosen Deck card is banished face-down and the Capsule stays",
                    reached && activated && asked && picked && banishedOk,
                    $"act={activated} asked={asked} picked={picked} held={held?.Name} faceUp={held?.FaceUp} " +
                    $"onField={OnField(p, cap)}");
                Check("Different Dimension Capsule: 1st Standby nothing; 2nd Standby the Capsule is destroyed and the card " +
                      "is added to the hand",
                    firstStays && !OnField(p, cap) && p.Graveyard.Contains(cap) && held != null && p.Hand.Contains(held) &&
                    held.FaceUp && !p.Banished.Contains(held),
                    $"first={firstStays} capOnField={OnField(p, cap)} inHand={held != null && p.Hand.Contains(held)}");
            }

            {
                // The Capsule leaves the field first: the card stays banished.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var cap = PutInHand(engine, p, Capsule);
                engine.TryActivateSpellTrap(p, cap, true);
                Settle(engine);
                if (engine.PendingActivation != null)
                    engine.CancelEffectTargeting(); // the choice is mandatory: Cancel takes the AI's pick
                Settle(engine);
                var held = PhaseTimersAndDeckTopRuntime.CapsuleBanishedCard(engine, cap);
                engine.SendCardToGrave(p, cap);
                for (var i = 0; i < 4; i++) EndTurn(engine);
                Check("Different Dimension Capsule: once it has left the field the banished card stays banished",
                    reached && held != null && p.Banished.Contains(held) && !held.FaceUp && !p.Hand.Contains(held),
                    $"held={held?.Name} banished={held != null && p.Banished.Contains(held)}");
            }
        }

        // ───────────────────────────── Pyro Clock of Destiny ─────────────────────────────

        static void PyroClockDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var clock = PlaceSetTrap(engine, p, PyroClock, 1);
                var none = !engine.CanActivateSpellTrap(p, clock, false);
                // A Different Dimension Capsule count is not a 1082946 turn count.
                var cap = PutInHand(engine, p, Capsule);
                engine.TryActivateSpellTrap(p, cap, true);
                Settle(engine);
                if (engine.PendingActivation != null) engine.CancelEffectTargeting();
                Settle(engine);
                Check("Pyro Clock of Destiny: cannot be activated with no turn count (a Capsule is not one)",
                    reached && none && OnField(p, cap) && !engine.CanActivateSpellTrap(p, clock, false));
            }

            {
                // Two counts: Swords of Revealing Light and Lightforce Sword. The player picks Lightforce; Swords keeps 3.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var swords = FaceUpSt(engine, p, Swords, 0);
                swords.ContinuousTurnsRemaining = 3;
                Fill(engine, o, Celtic, 2);
                var sword = PlaceSetTrap(engine, p, Lightforce, 2);
                var lfOk = engine.TryActivateSpellTrap(p, sword, false);
                Settle(engine);
                var banished = o.Banished.FirstOrDefault(c => !c.FaceUp && c.ContinuousTurnsRemaining == 4);
                var clock = PlaceSetTrap(engine, p, PyroClock, 1);
                var can = engine.CanActivateSpellTrap(p, clock, false);
                var act = engine.TryActivateSpellTrap(p, clock, false);
                Settle(engine);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var asked = legal.Count == 2 && legal.Contains(swords) && legal.Any(c => c.CardId == Lightforce);
                var lfHolder = legal.FirstOrDefault(c => c.CardId == Lightforce);
                var picked = lfHolder != null && engine.TrySelectEffectTarget(lfHolder);
                Settle(engine);
                Check("Pyro Clock of Destiny: picks among 2 turn counts (Swords, Lightforce Sword) and moves only the chosen one",
                    reached && lfOk && banished != null && can && act && asked && picked &&
                    banished.ContinuousTurnsRemaining == 3 && swords.ContinuousTurnsRemaining == 3 &&
                    engine.PendingActivation == null,
                    $"lf={lfOk} banished={banished?.Name} can={can} act={act} legal=[{string.Join(",", legal.Select(c => c.Name))}] " +
                    $"picked={picked} lfCount={banished?.ContinuousTurnsRemaining} swords={swords.ContinuousTurnsRemaining}");

                // A lone count resolves with no prompt; Swords at 1 is destroyed by the step.
                swords.ContinuousTurnsRemaining = 1;
                banished.ContinuousTurnsRemaining = 1;
                var clock2 = PlaceSetTrap(engine, p, PyroClock, 3);
                engine.TryActivateSpellTrap(p, clock2, false);
                Settle(engine);
                var sw = engine.PendingActivation?.LegalTargets.FirstOrDefault(c => ReferenceEquals(c, swords));
                if (sw != null) engine.TrySelectEffectTarget(sw);
                Settle(engine);
                var swordsGone = !OnField(p, swords) && p.Graveyard.Contains(swords);
                var clock3 = PlaceSetTrap(engine, p, PyroClock, 4);
                engine.TryActivateSpellTrap(p, clock3, false);
                Settle(engine);
                Check("Pyro Clock of Destiny: the last step of a count applies now — Swords is destroyed, the Lightforce " +
                      "card returns to its owner's hand (lone count: no prompt)",
                    swordsGone && o.Hand.Contains(banished) && banished.FaceUp && !o.Banished.Contains(banished) &&
                    engine.PendingActivation == null,
                    $"swordsGone={swordsGone} returned={o.Hand.Contains(banished)}");
            }

            {
                // Steel Scorpion: two marked attackers; its owner chooses which count moves. Deck Devastation Virus: one step.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var scorpion = PlaceMonster(engine, p, SteelScorpion, 0, BattlePosition.Defense, true);
                var a = PlaceMonster(engine, o, DarkBlade, 0, BattlePosition.Attack, true);
                var b = PlaceMonster(engine, o, RudeKaiser, 1, BattlePosition.Attack, true);
                var clause = CompiledEffectCache.GetOrCompile(scorpion.Def)?.ClauseList
                    .FirstOrDefault(c => c.Action == EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase);
                BattlePhaseEndRuntime.MarkAttackerForDelayedDestroy(engine, p, scorpion, clause, a);
                BattlePhaseEndRuntime.MarkAttackerForDelayedDestroy(engine, p, scorpion, clause, b);
                var counts = PhaseTimersAndDeckTopRuntime.TurnCounts(engine);
                var row = counts.FirstOrDefault(t => ReferenceEquals(t.Holder, scorpion));
                row?.Advance(b);
                row?.Advance(b); // b: 2 of 3
                var clock = PlaceSetTrap(engine, p, PyroClock, 1);
                engine.TryActivateSpellTrap(p, clock, false);
                Settle(engine);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var asked = legal.Count == 2 && legal.Contains(a) && legal.Contains(b);
                if (asked) engine.TrySelectEffectTarget(b);
                Settle(engine);
                Check("Pyro Clock of Destiny x Steel Scorpion: its owner picks the marked monster; its 3rd count destroys it now",
                    reached && clause != null && row != null && asked && !OnField(o, b) && o.Graveyard.Contains(b) && OnField(o, a),
                    $"row={row != null} legal=[{string.Join(",", legal.Select(c => c.Name))}] b={OnField(o, b)} a={OnField(o, a)}");

                var ddv = engine.CreateCardInstance(Ddv);
                p.Graveyard.Add(ddv);
                o.CrushCardLingerEndsRemaining = 3;
                PhaseTimersAndDeckTopRuntime.NoteTurnCountHolder(engine, o, ddv, p);
                var ddvRow = PhaseTimersAndDeckTopRuntime.TurnCounts(engine).FirstOrDefault(t => ReferenceEquals(t.Holder, ddv));
                ddvRow?.Advance(null);
                Check("Pyro Clock of Destiny x Deck Devastation Virus: one step of the draw check (3 → 2)",
                    ddvRow != null && o.CrushCardLingerEndsRemaining == 2 && ReferenceEquals(ddvRow.Owner, p),
                    $"row={ddvRow != null} left={o.CrushCardLingerEndsRemaining}");
            }
        }

        // ───────────────────────────── A Cat of Ill Omen / An Owl of Luck ─────────────────────────────

        static void FlipCat(DuelEngine engine, CardInstance cat)
        {
            engine.TryFlipSummon(engine.Player, cat);
            Settle(engine);
        }

        static void DeckTopFlipDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                // No Necrovalley: the chosen Trap goes on top of the shuffled Deck.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                DeckWith(db, p, d => d.IsTrap, TrapHole, MirrorForce);
                var deck0 = p.Deck.Count;
                var cat = PlaceMonster(engine, p, Cat, 0, BattlePosition.Defense, false);
                FlipCat(engine, cat);
                var legal = engine.PendingActivation?.LegalTargets.ToList() ?? new List<CardInstance>();
                var asked = legal.Count == 2 && legal.All(c => c.Def.IsTrap);
                var mf = LegalWithId(engine, MirrorForce);
                if (mf != null) engine.TrySelectEffectTarget(mf);
                Settle(engine);
                Check("A Cat of Ill Omen: choose 1 of 2 Traps in the Deck; with no Necrovalley it is placed on top",
                    reached && cat.FaceUp && asked && p.Deck.Count == deck0 && p.Deck[0] == MirrorForce &&
                    engine.PendingActivation == null && p.Hand.Count == 0,
                    $"asked={asked} top={p.Deck.FirstOrDefault()} deck {deck0}->{p.Deck.Count} pending={engine.PendingActivation != null}");
            }

            foreach (var (label, negated, yes) in new[] { ("present, Yes", false, true), ("negated, Yes", true, true), ("present, Cancel", false, false) })
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                var necro = FieldSpell(engine, o, Necrovalley);
                necro.IsNegated = negated;
                DeckWith(db, p, d => d.IsTrap, TrapHole);
                var deck0 = p.Deck.Count;
                var cat = PlaceMonster(engine, p, Cat, 0, BattlePosition.Defense, false);
                FlipCat(engine, cat);
                var pend = engine.PendingActivation;
                var offered = pend != null && pend.LegalTargets.Count == 1 && pend.LegalTargets[0].CardId == TrapHole;
                if (offered)
                {
                    if (yes) engine.TrySelectEffectTarget(pend.LegalTargets[0]);
                    else engine.CancelEffectTargeting();
                }

                Settle(engine);
                var ok = yes
                    ? p.Hand.Any(c => c.CardId == TrapHole) && p.Deck.Count == deck0 - 1
                    : p.Hand.Count == 0 && p.Deck.Count == deck0 && p.Deck[0] == TrapHole;
                Check($"A Cat of Ill Omen with Necrovalley ({label}): a lone Trap still asks; Yes adds it, Cancel puts it on top",
                    reached && offered && ok && engine.PendingActivation == null,
                    $"offered={offered} hand={p.Hand.Count} deck {deck0}->{p.Deck.Count} top={p.Deck.FirstOrDefault()}");
            }

            {
                // A Necrovalley whose activation has not resolved is not "on the field" (IsEnvironment): no Yes/No.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var necro = FieldSpell(engine, engine.Opponent, Necrovalley);
                necro.ActivationUnresolvedOnChain = true;
                DeckWith(db, p, d => d.IsTrap, TrapHole);
                var cat = PlaceMonster(engine, p, Cat, 0, BattlePosition.Defense, false);
                FlipCat(engine, cat);
                Check("A Cat of Ill Omen: a Necrovalley still activating does not count — the Trap goes on top, no prompt",
                    reached && engine.PendingActivation == null && p.Deck[0] == TrapHole && p.Hand.Count == 0,
                    $"pending={engine.PendingActivation != null} top={p.Deck.FirstOrDefault()}");
            }

            {
                // The AI's Owl with Necrovalley adds its Field Spell; with no Field Spell in the Deck nothing happens.
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var o = engine.Opponent;
                FieldSpell(engine, engine.Player, Necrovalley);
                DeckWith(db, o, d => d.IsFieldSpell, Yami, Sogen);
                var owl = PlaceMonster(engine, o, Owl, 0, BattlePosition.Defense, true);
                MonsterEffects.OnFlipSummoned(engine, o, owl);
                Settle(engine);
                var added = o.Hand.Count == 1 && o.Hand[0].Def.IsFieldSpell;

                engine.Player.FieldSpellZone.Occupant = null;
                o.Hand.Clear();
                var deck0 = o.Deck.Count;
                MonsterEffects.OnFlipSummoned(engine, o, owl);
                Settle(engine);
                var topped = o.Hand.Count == 0 && o.Deck.Count == deck0 &&
                             db.Get(o.Deck[0]) is { } top && top.IsFieldSpell;

                o.Deck.RemoveAll(id => db.Get(id) is { } d && d.IsFieldSpell);
                var deck1 = o.Deck.ToList();
                MonsterEffects.OnFlipSummoned(engine, o, owl);
                Settle(engine);
                Check("An Owl of Luck (AI): with Necrovalley it adds the Field Spell, without it the Field Spell goes on top; " +
                      "no Field Spell in the Deck: nothing",
                    reached && added && topped && o.Deck.SequenceEqual(deck1) && engine.PendingActivation == null,
                    $"added={added} topped={topped}");
            }
        }

        // ───────────────────────────── Royal Tribute ─────────────────────────────

        static void RoyalTributeDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FieldSpell(engine, p, Necrovalley);
                var tribute = PutInHand(engine, p, RoyalTribute);
                var myMonster = PutInHand(engine, p, Celtic);
                var mySpell = PutInHand(engine, p, Mst);
                var minar = PutInHand(engine, o, Minar);
                var theirs = PutInHand(engine, o, DarkBlade);
                var theirTrap = PutInHand(engine, o, TrapHole);
                var lp = p.LifePoints;
                var act = engine.TryActivateSpellTrap(p, tribute, true);
                Settle(engine);
                Check("Royal Tribute: with your Necrovalley both players discard every monster in hand (others stay); the " +
                      "opponent's Minar triggers (discarded by your effect)",
                    reached && act && p.Graveyard.Contains(myMonster) && o.Graveyard.Contains(minar) &&
                    o.Graveyard.Contains(theirs) && p.Hand.Contains(mySpell) && o.Hand.Contains(theirTrap) &&
                    p.Hand.Count == 1 && o.Hand.Count == 1 && p.LifePoints == lp - 1000,
                    $"act={act} pHand={p.Hand.Count} oHand={o.Hand.Count} lp {lp}->{p.LifePoints}");
            }

            {
                var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
                var p = engine.Player;
                var o = engine.Opponent;
                FieldSpell(engine, o, Necrovalley);
                var tribute = PutInHand(engine, p, RoyalTribute);
                PutInHand(engine, o, Celtic);
                var oppOnly = !engine.CanActivateSpellTrap(p, tribute, true);
                o.FieldSpellZone.Occupant = null;
                var necro = FieldSpell(engine, p, Necrovalley);
                necro.IsNegated = true;
                var negatedOk = engine.CanActivateSpellTrap(p, tribute, true);
                o.Hand.Clear();
                var emptyHands = !engine.CanActivateSpellTrap(p, tribute, true);
                Check("Royal Tribute: not with only the opponent's Necrovalley, nor with empty hands; a negated Necrovalley " +
                      "you control still counts",
                    reached && oppOnly && negatedOk && emptyHands,
                    $"oppOnly={oppOnly} negated={negatedOk} empty={emptyHands}");
            }
        }

        // ───────────────────────────── Standby hold ─────────────────────────────

        static void StandbyHoldDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            // The player's Standby Phase holds for Messenger of Peace's "pay or destroy" choice; Mirage of Nightmare's
            // discard and Bottomless Shifting Sand's check apply once before it, and not again when it is answered.
            var engine = PlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var mirage = FaceUpSt(engine, p, Mirage, 0);
            var sand = FaceUpSt(engine, p, ShiftingSand, 1);
            FaceUpSt(engine, p, Messenger, 2);
            Fill(engine, p, Celtic, 2);
            EndTurn(engine); // opponent's Standby: draw 2 → 4
            var rec = PhaseTimersAndDeckTopRuntime.MirageRecordedCount(engine, mirage);
            EndTurn(engine); // player's Draw (5): Sand stays; Mirage discards 2 → 3; Messenger asks
            var pend = engine.PendingActivation;
            var held = pend != null && pend.AwaitingUpkeepChoice && engine.Phase == DuelPhase.Standby;
            var handHeld = p.Hand.Count;
            var sandStayed = OnField(p, sand);
            if (held) engine.TrySelectLpCost(pend.CostNumeric);
            Settle(engine);
            Check("Standby hold: Mirage's discard and Shifting Sand's check apply once before Messenger of Peace's prompt, " +
                  "then Main Phase 1",
                reached && rec == 2 && held && handHeld == 3 && sandStayed && p.Hand.Count == 3 &&
                engine.Phase == DuelPhase.Main1 && engine.PendingActivation == null,
                $"rec={rec} held={held} hand {handHeld}->{p.Hand.Count} sand={sandStayed} phase={engine.Phase}");
        }
    }
}
