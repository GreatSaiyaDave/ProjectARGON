using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "HandDisruption" (v142): Gamble (called coin: draw until 5, or skip your next turn), Appropriate
    /// (Continuous Trap: activated only after the opponent draws outside either Draw Phase; then each such draw
    /// makes you draw 2) and Painful Choice (select 5 from your Deck, the opponent picks the 1 added to your hand,
    /// the rest go to the GY). Graverobber is implemented by the PerCopyDeck batch (v163) and its shape asserted here; Forced
    /// Requisition is implemented by the EngineHygiene batch (v160) and asserted here.
    /// </summary>
    public static class HandDisruptionRegressionTests
    {
        const int Gamble = 37313786;
        const int Appropriate = 48539234;
        const int PainfulChoice = 74191942;
        const int ForcedRequisition = 74923978;
        const int Graverobber = 61705417;

        const int PotOfGreed = 55144522;
        const int CardDestruction = 72892473;
        const int HirosShadowScout = 81863068; // FLIP: the opponent draws 3, then discards the drawn Spells
        const int Mercury = 38730226;          // The Agent of Wisdom - Mercury: Standby Phase draw
        const int MorphingJar = 33508719;      // FLIP: both players discard their hands, then each draws 5
        const int Celtic = 91152256;       // Normal Lv4 1400/1200
        const int Bewd = 89631139;         // Normal Lv8 3000/2500
        const int SummonedSkull = 70781052; // Normal Lv6 2500/1200
        const int SkullServant = 32274490; // Normal Lv1 300/200
        const int ProtectorOfTheSanctuary = 24221739;
        const int Mst = 5318639;            // Mystical Space Typhoon (Quick-Play)
        const int JarOfGreed = 83968380;    // Normal Trap: draw 1

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) => check("HandDisruption: " + name, ok, detail);

            CompileShapes(db, Check);
            NearMisses(db, Check);
            GambleDuels(db, pDeck, aDeck, Check);
            AppropriateDuels(db, pDeck, aDeck, Check);
            PainfulChoiceDuels(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── compile shape ─────────────────────────

        static EffectClause OnlyClause(CardDatabase db, int id, out CompiledCardProgram prog)
        {
            prog = CardTextEffectCompiler.Compile(db.Get(id));
            return prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
        }

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var g = OnlyClause(db, Gamble, out var gp);
            Check("Gamble: FullyCompiled — one Activate CoinCallDrawUntilOrSkipNextTurn (until 5; opp 6+ / you 2-)",
                gp != null && gp.FullyCompiled && gp.CanResolveAny && g != null &&
                g.Timing == EffectTiming.Activate && g.Action == EffectActionKind.CoinCallDrawUntilOrSkipNextTurn &&
                g.Amount == 5 && g.RequiresOpponentHandAtLeast == 6 && g.ConditionAmount == 2 &&
                g.ActivationCondition == HandDisruptionRuntime.HandSizes && g.MakesChainLink &&
                !g.RequiresTargetChoice && !g.StaysOnField,
                g == null ? $"clauses={gp?.ClauseList.Count}" : EffectVocabulary.Describe(g));

            var a = OnlyClause(db, Appropriate, out var ap);
            Check("Appropriate: FullyCompiled — one ContinuousWhileFaceUp DrawWhenOpponentDrawsOutsideDrawPhase (2)",
                ap != null && ap.FullyCompiled && ap.CanResolveAny && a != null &&
                a.Timing == EffectTiming.ContinuousWhileFaceUp &&
                a.Action == EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase && a.Amount == 2 &&
                a.ActivationCondition == HandDisruptionRuntime.OpponentDrewOutsideDrawPhase && !a.MakesChainLink &&
                HandDisruptionRuntime.AnswersDrawWindow(ap) && !ap.HasTiming(EffectTiming.Activate),
                a == null ? $"clauses={ap?.ClauseList.Count}" : EffectVocabulary.Describe(a));

            var pc = OnlyClause(db, PainfulChoice, out var pcp);
            Check("Painful Choice: FullyCompiled — one Activate RevealDeckCardsOpponentPicksOneToHand (5; Deck 5+)",
                pcp != null && pcp.FullyCompiled && pcp.CanResolveAny && pc != null &&
                pc.Timing == EffectTiming.Activate &&
                pc.Action == EffectActionKind.RevealDeckCardsOpponentPicksOneToHand && pc.Amount == 5 &&
                pc.ActivationCondition == HandDisruptionRuntime.ControllerDeckAtLeast && pc.ConditionAmount == 5 &&
                pc.MakesChainLink && !pc.RequiresTargetChoice,
                pc == null ? $"clauses={pcp?.ClauseList.Count}" : EffectVocabulary.Describe(pc));

            foreach (var id in new[] { Gamble, Appropriate, PainfulChoice })
                Check($"{db.Get(id)?.name}: classified Implemented",
                    CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(db.Get(id)).ToString());

            // Forced Requisition: implemented by the EngineHygiene batch (v160) on the central discard event.
            {
                var fr = CardTextEffectCompiler.Compile(db.Get(ForcedRequisition));
                Check("Forced Requisition: Implemented on the discard event (EngineHygiene) — window-only activation",
                    fr != null && fr.FullyCompiled &&
                    CardEffectStatus.Classify(db.Get(ForcedRequisition)) == CardEffectStatusKind.Implemented &&
                    fr.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.Activate &&
                        c.Action == EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard &&
                        c.ActivationCondition == DiscardEvents.YouDiscardedFromHand),
                    CardEffectStatus.Classify(db.Get(ForcedRequisition)).ToString());
            }

            {
                // Graverobber was deferred here (no opponent-owned card in your hand). PerCopyDeck batch (v163): the
                // Spell keeps its owner in your hand (PerCopyDeckRegressionTests drive it).
                var prog = CardTextEffectCompiler.Compile(db.Get(Graverobber));
                Check("Graverobber: compiles to the opponent's-GY Spell taken into your hand for the turn",
                    prog != null && prog.FullyCompiled && prog.ClauseList.Count == 1 &&
                    prog.ClauseList[0].Action == EffectActionKind.AddOpponentGySpellToHandUsableThisTurn &&
                    prog.ClauseList[0].Zone == EffectZoneFilter.OppGySpells &&
                    CardEffectStatus.Classify(db.Get(Graverobber)) == CardEffectStatusKind.Implemented,
                    CardEffectStatus.Classify(db.Get(Graverobber)).ToString());
            }

            Check("Vocabulary: Gamble Randomize, Appropriate Draw, Painful Choice Search (shared kinds)",
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.CoinCallDrawUntilOrSkipNextTurn }) ==
                EffectResolutionKind.Randomize &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase }) ==
                EffectResolutionKind.Draw &&
                EffectVocabulary.ResolutionOf(new EffectClause { Action = EffectActionKind.RevealDeckCardsOpponentPicksOneToHand }) ==
                EffectResolutionKind.Search);
        }

        static CompiledCardProgram CompileText(string text, string race, string type, string frame) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = 990142, name = "Near-miss Card", type = type, frameType = frame, race = race, desc = text
            });

        static bool CompilesTo(string text, EffectActionKind action, string race = "Normal", string type = "Trap Card",
            string frame = "trap")
        {
            var prog = CompileText(text, race, type, frame);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        static void NearMisses(CardDatabase db, CheckFn Check)
        {
            const string gamble =
                "You can only activate this card when your opponent's hand is 6 or more cards and your hand is 2 or " +
                "less. Toss a coin and call heads or tails. If you call it right, draw until your hand has 5 cards. " +
                "If you call it wrong, skip your next turn.";
            Check("Near miss: Gamble's text compiles (control)",
                CompilesTo(gamble, EffectActionKind.CoinCallDrawUntilOrSkipNextTurn));
            Check("Near miss: an extra sentence after Gamble's text is not compiled",
                !CompilesTo(gamble + " Then take 1000 damage.", EffectActionKind.CoinCallDrawUntilOrSkipNextTurn));
            Check("Near miss: skipping only the next Draw Phase is not this template",
                !CompilesTo(gamble.Replace("skip your next turn.", "skip your next Draw Phase."),
                    EffectActionKind.CoinCallDrawUntilOrSkipNextTurn));
            Check("Near miss: an uncalled coin toss is not this template",
                !CompilesTo(gamble.Replace("Toss a coin and call heads or tails. If you call it right",
                        "Toss a coin. If the result is heads").Replace("If you call it wrong", "If the result is tails"),
                    EffectActionKind.CoinCallDrawUntilOrSkipNextTurn));

            const string appropriate =
                "Activate only when your opponent draws a card(s) outside of either Draw Phase. After that, each time " +
                "your opponent draws a card(s) outside of either Draw Phase, immediately draw 2 cards.";
            Check("Near miss: Appropriate's text compiles (control)",
                CompilesTo(appropriate, EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase, "Continuous"));
            Check("Near miss: every opponent draw (Draw Phase included) is not this template",
                !CompilesTo("Activate only when your opponent draws a card(s). After that, each time your opponent " +
                            "draws a card(s), immediately draw 2 cards.",
                    EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase, "Continuous"));
            Check("Near miss: a one-shot draw answer (no \"each time\") is not this template",
                !CompilesTo("Activate only when your opponent draws a card(s) outside of either Draw Phase. Draw 2 cards.",
                    EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase));

            const string painful =
                "Select 5 cards from your Deck and show them to your opponent. Your opponent selects 1 card among " +
                "them. Add that card to your hand and discard the remaining cards to the Graveyard.";
            Check("Near miss: Painful Choice's text compiles (control)",
                CompilesTo(painful, EffectActionKind.RevealDeckCardsOpponentPicksOneToHand, "Normal", "Spell Card",
                    "spell"));
            Check("Near miss: the rest shuffled back into the Deck is not this template",
                !CompilesTo(painful.Replace("discard the remaining cards to the Graveyard",
                        "shuffle the remaining cards into the Deck"),
                    EffectActionKind.RevealDeckCardsOpponentPicksOneToHand, "Normal", "Spell Card", "spell"));
            Check("Near miss: you selecting the card yourself is not this template",
                !CompilesTo(painful.Replace("Your opponent selects 1 card among them.", "Select 1 card among them."),
                    EffectActionKind.RevealDeckCardsOpponentPicksOneToHand, "Normal", "Spell Card", "spell"));
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

        static void EndTurn(DuelEngine engine)
        {
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
            engine.RecoverStuckCombat();
            engine.TryEndTurnSafe(engine.TurnPlayer);
            for (var i = 0; i < 4 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++) engine.PassResponse();
        }

        static void SetHand(DuelEngine engine, DuelistState who, int count, int id = Celtic)
        {
            who.Hand.Clear();
            for (var i = 0; i < count; i++) PutInHand(engine, who, id);
        }

        static CardInstance PlaceFaceUpTrap(DuelEngine engine, DuelistState who, int id, int zone)
        {
            var c = engine.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static string Window(DuelEngine engine) =>
            engine.PendingResponse == null
                ? "no window"
                : $"timing={engine.PendingResponse.Timing} responder={engine.PendingResponse.Responder?.Name} " +
                  $"legal=[{string.Join(",", engine.PendingResponse.LegalCards.Select(c => c?.Name))}]";

        static bool DrawWindowOffers(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine.PendingResponse != null &&
            engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase &&
            engine.PendingResponse.Responder == who && engine.PendingResponse.LegalCards.Contains(card);

        // ───────────────────────── Gamble ─────────────────────────

        static void GambleDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Activation condition: your hand 2 or less AND the opponent's hand 6 or more.
                var engine = Fresh(db, pDeck, aDeck);
                var reached = ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, p, Gamble, 0);
                SetHand(engine, p, 3);
                SetHand(engine, opp, 6);
                var threeVsSix = engine.CanActivateSpellTrap(p, g, fromHand: false);
                SetHand(engine, p, 2);
                SetHand(engine, opp, 5);
                var twoVsFive = engine.CanActivateSpellTrap(p, g, fromHand: false);
                SetHand(engine, opp, 6);
                var twoVsSix = engine.CanActivateSpellTrap(p, g, fromHand: false);
                SetHand(engine, p, 0);
                SetHand(engine, opp, 9);
                var zeroVsNine = engine.CanActivateSpellTrap(p, g, fromHand: false);
                Check("Gamble: legal only while your hand is 2 or less and your opponent's is 6 or more",
                    reached && !threeVsSix && !twoVsFive && twoVsSix && zeroVsNine,
                    $"3v6={threeVsSix} 2v5={twoVsFive} 2v6={twoVsSix} 0v9={zeroVsNine}");
            }

            {
                // Called right: draw until your hand has 5 cards (2 → 5); Gamble goes to the GY.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, p, Gamble, 0);
                SetHand(engine, p, 2);
                SetHand(engine, opp, 6);
                var deck = p.Deck.Count;
                engine.Rng.ClearQueues();
                engine.Rng.QueueCoin(true);
                var ok = engine.TryActivateSpellTrap(p, g, fromHand: false);
                PassAll(engine);
                Check("Gamble: called right — you draw until your hand has 5 cards",
                    ok && p.HandCount == 5 && p.Deck.Count == deck - 3 && p.Graveyard.Contains(g) &&
                    !p.SkipNextTurn && engine.Chain.Count == 0 && !engine.IsAwaitingEffectTarget,
                    $"ok={ok} hand={p.HandCount} deck {deck}->{p.Deck.Count} gy={p.Graveyard.Contains(g)} skip={p.SkipNextTurn}");
            }

            {
                // Called wrong: nothing drawn; your next turn is skipped — the opponent takes two turns in a row.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, p, Gamble, 0);
                SetHand(engine, p, 2);
                SetHand(engine, opp, 6);
                engine.Rng.ClearQueues();
                engine.Rng.QueueCoin(false);
                var ok = engine.TryActivateSpellTrap(p, g, fromHand: false);
                PassAll(engine);
                Check("Gamble: called wrong — no draw, your next turn is marked to be skipped",
                    ok && p.HandCount == 2 && p.SkipNextTurn && p.Graveyard.Contains(g),
                    $"ok={ok} hand={p.HandCount} skip={p.SkipNextTurn}");
                var turn = engine.TurnNumber;
                EndTurn(engine); // → opponent's turn
                var oppTurn = engine.TurnPlayer == opp && engine.TurnNumber == turn + 1;
                var pDeck1 = p.Deck.Count;
                var oppDeck1 = opp.Deck.Count;
                EndTurn(engine); // your turn is skipped → the opponent's turn again
                Check("Gamble: your next turn is skipped — the opponent takes the following turn",
                    oppTurn && engine.TurnPlayer == opp && engine.TurnNumber == turn + 3 &&
                    engine.Phase == DuelPhase.Main1 && p.Deck.Count == pDeck1 && opp.Deck.Count == oppDeck1 - 1 &&
                    !p.SkipNextTurn,
                    $"oppTurn={oppTurn} turnPlayer={engine.TurnPlayer?.Name} turn {turn}->{engine.TurnNumber} " +
                    $"phase={engine.Phase} pDeck {pDeck1}->{p.Deck.Count} oppDeck {oppDeck1}->{opp.Deck.Count}");
                EndTurn(engine);
                Check("Gamble: only one turn is skipped — your turn comes back after that",
                    engine.TurnPlayer == p && engine.TurnNumber == turn + 4 && engine.Phase == DuelPhase.Main1,
                    $"turnPlayer={engine.TurnPlayer?.Name} turn={engine.TurnNumber} phase={engine.Phase}");
            }

            {
                // A skipped turn uses up a pending "skip your next Draw Phase" (ygopro resets Draw Phase effects in
                // the skipped turn): the turn after it draws normally.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                var p = engine.Player;
                p.SkipNextTurn = true;
                p.SkipNextDrawPhase = true;
                var turn = engine.TurnNumber;
                EndTurn(engine); // → opponent
                EndTurn(engine); // player's turn skipped → opponent
                var deck = p.Deck.Count;
                EndTurn(engine); // → player's next real turn: normal draw
                Check("Gamble: the skipped turn uses up a pending Draw Phase skip — the next turn draws",
                    engine.TurnPlayer == p && engine.TurnNumber == turn + 4 && p.Deck.Count == deck - 1 &&
                    !p.SkipNextDrawPhase,
                    $"turnPlayer={engine.TurnPlayer?.Name} turn {turn}->{engine.TurnNumber} deck {deck}->{p.Deck.Count}");
            }

            {
                // The player answers the opponent's-turn window (Main Phase 1 start) with Gamble: the opponent drew
                // to 6; the player (2 cards) calls right and draws to 5 during the opponent's turn.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, p, Gamble, 0);
                SetHand(engine, p, 2);
                SetHand(engine, opp, 5);
                engine.Rng.ClearQueues();
                engine.Rng.QueueCoin(true);
                engine.TryEndTurnSafe(p);
                var offered = engine.PendingResponse != null &&
                              engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                              engine.PendingResponse.Responder == p && engine.PendingResponse.LegalCards.Contains(g);
                var window = Window(engine);
                var ok = offered && engine.TryActivateSpellTrap(p, g, fromHand: false);
                PassAll(engine);
                Check("Gamble: activated in the opponent's-turn window after they drew to 6 — you draw to 5",
                    offered && ok && engine.TurnPlayer == opp && p.HandCount == 5 && p.Graveyard.Contains(g),
                    $"offered={offered} ok={ok} {window} hand={p.HandCount}");
            }

            {
                // The AI takes the gamble in its opponent's-turn window; called wrong, it skips its next turn.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, opp, Gamble, 0);
                SetHand(engine, opp, 2);
                SetHand(engine, p, 5);
                engine.Rng.ClearQueues();
                engine.Rng.QueueCoin(false);
                engine.TryEndTurnSafe(opp); // → the player's turn; the player draws to 6; the AI answers at MP1
                PassAll(engine);
                var used = opp.Graveyard.Contains(g) && opp.SkipNextTurn;
                var turn = engine.TurnNumber;
                EndTurn(engine); // the AI's turn is skipped → back to the player
                Check("Gamble (AI): used when legal; called wrong, the AI's next turn is skipped",
                    used && engine.TurnPlayer == p && engine.TurnNumber == turn + 2 && engine.Phase == DuelPhase.Main1,
                    $"used={used} gy={opp.Graveyard.Contains(g)} turnPlayer={engine.TurnPlayer?.Name} turn {turn}->{engine.TurnNumber}");
            }
        }

        // ───────────────────────── Appropriate ─────────────────────────

        static void AppropriateDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Never at open game state, never as a chain answer: only in its draw window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                var openState = engine.CanActivateSpellTrap(p, ap, fromHand: false);
                engine.TryEndTurnSafe(p); // the opponent's Draw Phase draw opens no window
                var drawPhaseWindow = engine.PendingResponse != null &&
                                      engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase;
                Check("Appropriate: the opponent's Draw Phase draw opens no activation window",
                    engine.TurnPlayer == opp && !drawPhaseWindow, Window(engine));
                PassAll(engine);
                var pog = PutInHand(engine, opp, PotOfGreed);
                var chainOffered = false;
                // Watch the chain window of the draw card (before it resolves).
                engine.OnStateChanged += Watch;
                void Watch()
                {
                    if (engine.PendingResponse != null &&
                        engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                        engine.PendingResponse.LegalCards.Contains(ap))
                        chainOffered = true;
                }

                var ok = engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                engine.OnStateChanged -= Watch;
                Check("Appropriate: not activatable at open game state nor as a chain answer to the draw card",
                    !openState && ok && !chainOffered, $"open={openState} ok={ok} chain={chainOffered} {Window(engine)}");
            }

            {
                // The opponent draws outside the Draw Phase (Pot of Greed): once it resolves, the player gets the
                // window, activates Appropriate (it stays face-up); the next outside draw makes the player draw 2.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                var pog = PutInHand(engine, opp, PotOfGreed);
                var oppHand = opp.HandCount;
                var ok = engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                var offered = DrawWindowOffers(engine, p, ap);
                var window = Window(engine);
                Check("Appropriate: offered right after the opponent's Pot of Greed resolves (drew outside the Draw Phase)",
                    ok && offered && opp.HandCount == oppHand + 1 && engine.Chain.Count == 0,
                    $"ok={ok} {window} oppHand {oppHand}->{opp.HandCount}");
                var act = offered && engine.TryActivateSpellTrap(p, ap, fromHand: false);
                PassAll(engine);
                Check("Appropriate: activation resolves — face-up, stays on the field, no draw yet",
                    act && ap.FaceUp && p.TryFindSpellTrap(ap, out _) && !p.Graveyard.Contains(ap) &&
                    !engine.IsAwaitingResponse && engine.Chain.Count == 0,
                    $"act={act} up={ap.FaceUp} field={p.TryFindSpellTrap(ap, out _)} {Window(engine)}");
                var hand = p.HandCount;
                var deck = p.Deck.Count;
                var pog2 = PutInHand(engine, opp, PotOfGreed);
                var ok2 = engine.TryActivateSpellTrap(opp, pog2, fromHand: true);
                PassAll(engine);
                Check("Appropriate: each later outside draw by the opponent — you draw 2 at once",
                    ok2 && p.HandCount == hand + 2 && p.Deck.Count == deck - 2 && !engine.IsAwaitingResponse,
                    $"ok2={ok2} hand {hand}->{p.HandCount} deck {deck}->{p.Deck.Count} {Window(engine)}");

                // Card Destruction (the opponent discards its hand, then draws that many): one draw event of
                // several cards, still 2. The player also discards and redraws their own hand (their own draw does
                // not count), so the player ends with their hand size + 2.
                hand = p.HandCount;
                var oppRedraw = opp.HandCount;
                var cd = PutInHand(engine, opp, CardDestruction);
                var ok3 = engine.TryActivateSpellTrap(opp, cd, fromHand: true);
                PassAll(engine);
                Check("Appropriate: a multi-card draw is one draw — you draw 2, not 2 per card",
                    ok3 && oppRedraw >= 2 && p.HandCount == hand + 2,
                    $"ok3={ok3} oppRedraw={oppRedraw} hand {hand}->{p.HandCount}");

                // The opponent's Draw Phase draw does not count (your Deck is untouched through their Draw Phase).
                EndTurn(engine); // → player
                deck = p.Deck.Count;
                var oppDeck = opp.Deck.Count;
                EndTurn(engine); // → opponent's turn (Draw Phase draw)
                var turnOk = engine.TurnPlayer == opp && opp.Deck.Count == oppDeck - 1;
                Check("Appropriate: the opponent's normal Draw Phase draw does not make you draw",
                    turnOk && p.Deck.Count == deck, $"turnOpp={turnOk} deck {deck}->{p.Deck.Count}");
            }

            {
                // Your own draws outside the Draw Phase do not trigger your own Appropriate.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpTrap(engine, p, Appropriate, 0);
                p.Hand.Clear();
                var pog = PutInHand(engine, p, PotOfGreed);
                var oppHand = opp.HandCount;
                var ok = engine.TryActivateSpellTrap(p, pog, fromHand: true);
                PassAll(engine);
                Check("Appropriate: your own Pot of Greed draws only its 2",
                    ok && p.HandCount == 2 && opp.HandCount == oppHand, $"ok={ok} hand={p.HandCount}");
            }

            {
                // Negated (Metal Detector's Continuous Trap negation this turn): no extra draw.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpTrap(engine, p, Appropriate, 0);
                TurnNegationTrapsRuntime.Register(engine, opp, null, TurnNegationTrapsRuntime.ContinuousTrap);
                var hand = p.HandCount;
                var pog = PutInHand(engine, opp, PotOfGreed);
                var ok = engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                PassAll(engine);
                Check("Appropriate: while its effects are negated, the opponent's outside draw gives nothing",
                    ok && p.HandCount == hand, $"ok={ok} hand {hand}->{p.HandCount}");
            }

            {
                // The opponent's Protector of the Sanctuary: you cannot draw outside your Draw Phase.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpTrap(engine, p, Appropriate, 0);
                PlaceMonster(engine, opp, ProtectorOfTheSanctuary, 0, BattlePosition.Attack, true);
                var hand = p.HandCount;
                var deck = p.Deck.Count;
                var pog = PutInHand(engine, opp, PotOfGreed);
                var ok = engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                PassAll(engine);
                Check("Appropriate: the opponent's Protector of the Sanctuary blocks the extra draw",
                    ok && p.HandCount == hand && p.Deck.Count == deck, $"ok={ok} hand {hand}->{p.HandCount}");
            }

            {
                // The AI activates its Set Appropriate in the window the player's Pot of Greed opens, and later draws 2.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, opp, Appropriate, 0);
                var pog = PutInHand(engine, p, PotOfGreed);
                var ok = engine.TryActivateSpellTrap(p, pog, fromHand: true);
                PassAll(engine);
                var active = ap.FaceUp && opp.TryFindSpellTrap(ap, out _);
                var oppHand = opp.HandCount;
                var pog2 = PutInHand(engine, p, PotOfGreed);
                var ok2 = engine.TryActivateSpellTrap(p, pog2, fromHand: true);
                PassAll(engine);
                Check("Appropriate (AI): activated in its draw window; then the player's outside draw gives the AI 2",
                    ok && active && ok2 && opp.HandCount == oppHand + 2 && !engine.IsAwaitingResponse,
                    $"ok={ok} active={active} ok2={ok2} oppHand {oppHand}->{opp.HandCount} {Window(engine)}");
            }

            {
                // The AI's Appropriate is destroyed in response to its activation (the player chains Mystical Space
                // Typhoon): its link resolves with no effect and it is not put back on the field.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, opp, Appropriate, 0);
                var mst = PutInHand(engine, p, Mst);
                var pog = PutInHand(engine, p, PotOfGreed);
                engine.TryActivateSpellTrap(p, pog, fromHand: true);
                var chainOffered = engine.PendingResponse != null &&
                                   engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                                   engine.PendingResponse.Responder == p &&
                                   engine.PendingResponse.LegalCards.Contains(mst) &&
                                   engine.Chain.Last?.Card == ap;
                var window = Window(engine);
                var chained = chainOffered && engine.TryActivateSpellTrap(p, mst, fromHand: true);
                if (engine.IsAwaitingEffectTarget) engine.TrySelectEffectTarget(ap);
                PassAll(engine);
                Check("Appropriate: destroyed in response to its activation — it resolves with no effect, stays gone",
                    chainOffered && chained && opp.Graveyard.Contains(ap) && !opp.TryFindSpellTrap(ap, out _) &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"offered={chainOffered} {window} chained={chained} gy={opp.Graveyard.Contains(ap)} " +
                    $"field={opp.TryFindSpellTrap(ap, out _)} chain={engine.Chain.Count}");
            }

            {
                // Missed timing: the player's draw comes from Chain Link 2 (Jar of Greed chained to the AI's Gamble),
                // and Gamble (CL1) resolves after it — the AI's Set Appropriate gets no window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var g = PlaceSetTrap(engine, opp, Gamble, 0);
                var ap = PlaceSetTrap(engine, opp, Appropriate, 1);
                var jar = PlaceSetTrap(engine, p, JarOfGreed, 0);
                SetHand(engine, opp, 2);
                SetHand(engine, p, 5);
                engine.Rng.ClearQueues();
                engine.Rng.QueueCoin(true);
                engine.TryEndTurnSafe(opp); // player's MP1: the AI activates Gamble; the player may chain
                var chainWindow = engine.PendingResponse != null &&
                                  engine.PendingResponse.Timing == ResponseTiming.ChainResponse &&
                                  engine.PendingResponse.Responder == p &&
                                  engine.PendingResponse.LegalCards.Contains(jar) && engine.Chain.Last?.Card == g;
                var window = Window(engine);
                var chained = chainWindow && engine.TryActivateSpellTrap(p, jar, fromHand: false);
                var drawWindow = engine.PendingResponse != null &&
                                 engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase;
                PassAll(engine);
                Check("Appropriate: a draw by a Chain Link that resolves before another link misses the timing",
                    chainWindow && chained && !drawWindow && !ap.FaceUp && opp.TryFindSpellTrap(ap, out _) &&
                    p.HandCount == 7 && opp.HandCount == 5 && opp.Graveyard.Contains(g),
                    $"chainWindow={chainWindow} {window} chained={chained} drawWindow={drawWindow} apUp={ap.FaceUp} " +
                    $"pHand={p.HandCount} oppHand={opp.HandCount}");
            }

            {
                // "When" timing: Hiro's Shadow Scout makes the opponent draw 3, then the drawn Spell is discarded —
                // the draw is not the last thing that happened, so the player's Set Appropriate misses the timing.
                // (The OCG ruling's example is Graceful Charity's draw-then-discard, but that card compiles today as
                // a plain "Draw 3" — its discard is not implemented, a pre-existing gap outside this batch — so Hiro
                // stands in as the draw-then-discard case.)
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                var hiro = PlaceMonster(engine, p, HirosShadowScout, 0, BattlePosition.Defense, false);
                opp.Deck.Insert(0, PotOfGreed);
                opp.Deck.Insert(0, Celtic);
                opp.Deck.Insert(0, Celtic);
                var oppHand = opp.HandCount;
                var oppGy = opp.Graveyard.Count;
                var offered = false;
                engine.OnStateChanged += Watch;
                void Watch()
                {
                    if (DrawWindowOffers(engine, p, ap)) offered = true;
                }

                var ok = engine.TryFlipSummon(p, hiro);
                for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++)
                {
                    if (DrawWindowOffers(engine, p, ap)) offered = true;
                    engine.PassResponse();
                }

                engine.OnStateChanged -= Watch;
                Check("Appropriate: Hiro's Shadow Scout draws, then discards a drawn Spell — no window (timing missed)",
                    ok && !offered && !ap.FaceUp && opp.HandCount == oppHand + 2 && opp.Graveyard.Count == oppGy + 1,
                    $"ok={ok} offered={offered} apUp={ap.FaceUp} oppHand {oppHand}->{opp.HandCount} " +
                    $"oppGy {oppGy}->{opp.Graveyard.Count} {Window(engine)}");
            }

            {
                // Same with no Spell drawn: Hiro's effect still goes on after the draw (both players look at the
                // cards — ygopro Duel.BreakEffect after the draw), so there is still no window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                var hiro = PlaceMonster(engine, p, HirosShadowScout, 0, BattlePosition.Defense, false);
                for (var i = 0; i < 3; i++) opp.Deck.Insert(0, Celtic);
                var oppHand = opp.HandCount;
                var offered = false;
                engine.OnStateChanged += Watch;
                void Watch()
                {
                    if (DrawWindowOffers(engine, p, ap)) offered = true;
                }

                var ok = engine.TryFlipSummon(p, hiro);
                for (var i = 0; i < 6 && engine.IsAwaitingResponse; i++)
                {
                    if (DrawWindowOffers(engine, p, ap)) offered = true;
                    engine.PassResponse();
                }

                engine.OnStateChanged -= Watch;
                Check("Appropriate: Hiro's Shadow Scout with no Spell drawn — still no window (its effect continues after the draw)",
                    ok && !offered && !ap.FaceUp && opp.HandCount == oppHand + 3,
                    $"ok={ok} offered={offered} apUp={ap.FaceUp} oppHand {oppHand}->{opp.HandCount} {Window(engine)}");
            }

            {
                // Card Destruction (discard, THEN draw): the draw is the last thing that happened, so the window
                // opens once the Spell has resolved (it going to the GY afterwards does not count).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                SetHand(engine, opp, 3);
                SetHand(engine, p, 2);
                var cd = PutInHand(engine, opp, CardDestruction);
                var ok = engine.TryActivateSpellTrap(opp, cd, fromHand: true);
                for (var i = 0; i < 4 && engine.IsAwaitingResponse && !DrawWindowOffers(engine, p, ap); i++)
                    engine.PassResponse();
                var offered = DrawWindowOffers(engine, p, ap);
                var window = Window(engine);
                var act = offered && engine.TryActivateSpellTrap(p, ap, fromHand: false);
                PassAll(engine);
                Check("Appropriate: offered after the opponent's Card Destruction (the draw is the last thing that happened)",
                    ok && offered && act && ap.FaceUp && opp.HandCount == 3 && opp.Graveyard.Contains(cd),
                    $"ok={ok} offered={offered} {window} act={act} apUp={ap.FaceUp} oppHand={opp.HandCount}");
            }

            {
                // Both players drew at the same time (Morphing Jar): each Set Appropriate gets its window, the turn
                // player's first, and the first activation does not make the other miss the timing (in a real
                // chain the second would be chained to the first).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var jarM = PlaceMonster(engine, p, MorphingJar, 0, BattlePosition.Defense, false);
                var apP = PlaceSetTrap(engine, p, Appropriate, 0);
                var apO = PlaceSetTrap(engine, opp, Appropriate, 0);
                var order = new System.Collections.Generic.List<string>();
                engine.OnStateChanged += Watch;
                void Watch()
                {
                    var pr = engine.PendingResponse;
                    if (pr?.Timing != ResponseTiming.OpponentDrewOutsideDrawPhase) return;
                    var who = pr.Responder == p ? "player" : "AI";
                    if (order.Count == 0 || order[order.Count - 1] != who) order.Add(who);
                }

                var ok = engine.TryFlipSummon(p, jarM);
                for (var i = 0; i < 10 && (engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget); i++)
                {
                    if (DrawWindowOffers(engine, p, apP)) engine.TryActivateSpellTrap(p, apP, fromHand: false);
                    else if (engine.IsAwaitingResponse) engine.PassResponse();
                    else engine.CancelEffectTargeting();
                }

                engine.OnStateChanged -= Watch;
                Check("Appropriate x2 (Morphing Jar): both players' Set copies answer the simultaneous draws, turn player first",
                    ok && order.Count >= 1 && order[0] == "player" && apP.FaceUp && apO.FaceUp &&
                    p.HandCount == 5 && opp.HandCount == 5 && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"ok={ok} order=[{string.Join(",", order)}] apP={apP.FaceUp} apO={apO.FaceUp} " +
                    $"hands p={p.HandCount} o={opp.HandCount} chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // A draw during the Standby Phase (the AI's Mercury): BeginTurn runs that phase straight through, so
                // no window opens mid-sequence; the player's Main Phase 1 opponent's-turn window (a Set Jar of Greed
                // is legal there) is still offered, and nothing is left open. Mercury draws only when its controller
                // had no cards in hand at the end of the opponent's last End Phase, so the AI's hand is emptied
                // before the player's turn ends (Draw Phase 1 + Mercury 1 = 2).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, opp, Mercury, 0, BattlePosition.Defense, true);
                opp.Hand.Clear();
                p.Hand.Clear();
                var ap = PlaceSetTrap(engine, p, Appropriate, 0);
                var g = PlaceSetTrap(engine, p, JarOfGreed, 1);
                var drawWindow = false;
                engine.OnStateChanged += Watch;
                void Watch()
                {
                    if (engine.PendingResponse?.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
                        drawWindow = true;
                }

                engine.TryEndTurnSafe(p);
                engine.OnStateChanged -= Watch;
                var mp1Offered = engine.PendingResponse != null &&
                                    engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState &&
                                    engine.PendingResponse.Responder == p &&
                                    engine.PendingResponse.LegalCards.Contains(g);
                Check("Appropriate: no window for a Standby Phase draw; the opponent's-turn Main Phase 1 window still opens",
                    engine.TurnPlayer == opp && engine.Phase == DuelPhase.Main1 && opp.HandCount == 2 && !drawWindow &&
                    mp1Offered && engine.Chain.Count == 0 && !ap.FaceUp,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} oppHand={opp.HandCount} drawWindow={drawWindow} " +
                    $"chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // The mirror: the player's Mercury draws in the player's Standby Phase; the AI's Set Appropriate gets
                // no window there, and the player's turn reaches a clean Main Phase 1 (no chain carried over).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, p, Mercury, 0, BattlePosition.Defense, true);
                p.Hand.Clear();
                PlaceSetTrap(engine, p, Mst, 0);
                var ap = PlaceSetTrap(engine, opp, Appropriate, 0);
                engine.TryEndTurnSafe(opp);
                PassAll(engine);
                Check("Appropriate (AI): no window for the player's Standby Phase draw; the turn reaches a clean Main Phase 1",
                    engine.TurnPlayer == p && engine.Phase == DuelPhase.Main1 && p.HandCount == 2 && !ap.FaceUp &&
                    engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"turn={engine.TurnPlayer?.Name} phase={engine.Phase} hand={p.HandCount} apUp={ap.FaceUp} " +
                    $"chain={engine.Chain.Count} {Window(engine)}");
            }

            {
                // A draw while an attack is under way (Jar of Greed answering the attack declaration): the engine
                // carries the attack on into damage straight after the chain, so the draw is never the last thing
                // that happened at a point where the AI's Set Appropriate could be activated — no window.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                var atk = PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, p, JarOfGreed, 0);
                var ap = PlaceSetTrap(engine, opp, Appropriate, 0);
                engine.TryEnterBattlePhase(opp);
                PassAll(engine);
                var lp = p.LifePoints;
                var hand = p.HandCount;
                var att = engine.TryAttack(opp, atk, null);
                var offered = engine.PendingResponse?.Timing == ResponseTiming.AttackDeclared &&
                              engine.PendingResponse.LegalCards.Contains(jar);
                var act = offered && engine.TryActivateSpellTrap(p, jar, fromHand: false);
                for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++) engine.PassResponse();
                engine.RecoverStuckCombat();
                PassAll(engine);
                Check("Appropriate (AI): no window for a draw during an attack; the attack still resolves",
                    att && act && p.HandCount == hand + 1 && !ap.FaceUp && p.LifePoints == lp - 3000 &&
                    !engine.HasDeclaredAttack && engine.Chain.Count == 0 && !engine.IsAwaitingResponse,
                    $"att={att} offered={offered} act={act} hand {hand}->{p.HandCount} apUp={ap.FaceUp} " +
                    $"LP {lp}->{p.LifePoints} atk={engine.HasDeclaredAttack} {Window(engine)}");
            }

            {
                // Control: a draw at the start of the Battle Phase (no attack declared) is answered once it resolves.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceMonster(engine, opp, Bewd, 0, BattlePosition.Attack, true);
                var jar = PlaceSetTrap(engine, p, JarOfGreed, 0);
                var ap = PlaceSetTrap(engine, opp, Appropriate, 0);
                engine.TryEnterBattlePhase(opp);
                var offered = engine.PendingResponse != null && engine.PendingResponse.Responder == p &&
                              engine.PendingResponse.LegalCards.Contains(jar);
                var act = offered && engine.TryActivateSpellTrap(p, jar, fromHand: false);
                PassAll(engine);
                Check("Appropriate (AI): a draw in the Battle Phase before any attack — the AI activates it once the draw resolves",
                    offered && act && ap.FaceUp && engine.Phase == DuelPhase.Battle && engine.Chain.Count == 0 &&
                    !engine.IsAwaitingResponse,
                    $"offered={offered} act={act} apUp={ap.FaceUp} phase={engine.Phase} {Window(engine)}");
            }

            {
                // Both players' Appropriates answer each other's draws until a Deck runs out (the player's first).
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                PlaceFaceUpTrap(engine, p, Appropriate, 0);
                PlaceFaceUpTrap(engine, opp, Appropriate, 0);
                while (p.Deck.Count > 3) p.Deck.RemoveAt(p.Deck.Count - 1);
                while (opp.Deck.Count > 20) opp.Deck.RemoveAt(opp.Deck.Count - 1);
                var pog = PutInHand(engine, opp, PotOfGreed);
                engine.TryActivateSpellTrap(opp, pog, fromHand: true);
                PassAll(engine);
                Check("Appropriate x2: the draws answer each other until the player decks out (no hang)",
                    engine.GameOver && engine.Winner == opp,
                    $"over={engine.GameOver} winner={engine.Winner?.Name} pDeck={p.Deck.Count} oppDeck={opp.Deck.Count}");
            }
        }

        // ───────────────────────── Painful Choice ─────────────────────────

        static void StackDeck(DuelistState who, params int[] ids)
        {
            who.Deck.Clear();
            who.Deck.AddRange(ids);
        }

        static void PainfulChoiceDuels(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            {
                // Needs 5 cards in the Deck.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                var pc = PutInHand(engine, p, PainfulChoice);
                StackDeck(p, Celtic, Celtic, Bewd, SkullServant);
                var four = engine.CanActivateSpellTrap(p, pc, fromHand: true);
                p.Deck.Add(SummonedSkull);
                var five = engine.CanActivateSpellTrap(p, pc, fromHand: true);
                Check("Painful Choice: needs 5 cards in your Deck to activate", !four && five,
                    $"4={four} 5={five}");
            }

            {
                // The player selects 5 (a copy each pick); the AI opponent gives the least valuable (Skull Servant);
                // the other 4 go to the GY; the rest of the Deck stays.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var pc = PutInHand(engine, p, PainfulChoice);
                StackDeck(p, Celtic, Bewd, SummonedSkull, SkullServant, PotOfGreed, Celtic, Celtic, Bewd);
                var gyBefore = p.Graveyard.Count;
                var ok = engine.TryActivateSpellTrap(p, pc, fromHand: true);
                PassAll(engine);
                var pend = engine.PendingActivation;
                var selecting = pend != null && pend.AwaitingDeckRevealPicks && pend.Controller == p &&
                                pend.TargetPicksRemaining == 5 && pend.LegalTargets.Count == 8;
                Check("Painful Choice: at resolution you select 5 cards from your Deck (every Deck card listed)",
                    ok && selecting,
                    $"ok={ok} pending={(pend == null ? "none" : $"deck={pend.AwaitingDeckRevealPicks} n={pend.LegalTargets.Count} left={pend.TargetPicksRemaining}")}");
                foreach (var id in new[] { Bewd, SummonedSkull, SkullServant, Celtic, Celtic })
                {
                    var t = engine.PendingActivation?.LegalTargets.FirstOrDefault(c => c.CardId == id);
                    if (t != null) engine.TrySelectEffectTarget(t);
                }

                var handIds = p.Hand.Select(c => c.CardId).ToList();
                Check("Painful Choice: the AI opponent gives the least valuable shown card (Skull Servant) to your hand",
                    !engine.IsAwaitingEffectTarget && handIds.Count == 1 && handIds[0] == SkullServant,
                    $"hand=[{string.Join(",", p.Hand.Select(c => c.Name))}] pending={engine.IsAwaitingEffectTarget}");
                var gyIds = p.Graveyard.Skip(gyBefore).Select(c => c.CardId).ToList();
                Check("Painful Choice: the other 4 go to the GY; the Deck keeps the 3 not shown",
                    gyIds.Count(x => x == Bewd) == 1 && gyIds.Count(x => x == SummonedSkull) == 1 &&
                    gyIds.Count(x => x == Celtic) == 2 && gyIds.Contains(PainfulChoice) && p.Deck.Count == 3 &&
                    p.Deck.Count(x => x == Celtic) == 1 && p.Deck.Count(x => x == Bewd) == 1 &&
                    p.Deck.Contains(PotOfGreed),
                    $"gy=[{string.Join(",", p.Graveyard.Skip(gyBefore).Select(c => c.Name))}] " +
                    $"deck=[{string.Join(",", p.Deck)}]");
            }

            {
                // Cancel cannot skip the resolving selection: the remaining picks are made automatically.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Player, 3);
                ClearBoard(engine);
                var p = engine.Player;
                p.Hand.Clear();
                var pc = PutInHand(engine, p, PainfulChoice);
                StackDeck(p, Celtic, Bewd, SummonedSkull, SkullServant, PotOfGreed, Celtic, Celtic);
                engine.TryActivateSpellTrap(p, pc, fromHand: true);
                PassAll(engine);
                var t = engine.PendingActivation?.LegalTargets.FirstOrDefault(c => c.CardId == Bewd);
                if (t != null) engine.TrySelectEffectTarget(t);
                engine.CancelEffectTargeting();
                Check("Painful Choice: Cancel completes the selection — 1 card to hand, 4 to the GY, Deck -5",
                    !engine.IsAwaitingEffectTarget && p.HandCount == 1 && p.Deck.Count == 2 &&
                    p.Graveyard.Contains(pc),
                    $"pending={engine.IsAwaitingEffectTarget} hand={p.HandCount} deck={p.Deck.Count}");
            }

            {
                // The AI activates it: the player (its opponent) picks which shown card the AI adds to its hand.
                var engine = Fresh(db, pDeck, aDeck);
                ReachMain(engine, engine.Opponent, 2);
                ClearBoard(engine);
                var p = engine.Player;
                var opp = engine.Opponent;
                opp.Hand.Clear();
                var pc = PutInHand(engine, opp, PainfulChoice);
                StackDeck(opp, Celtic, Bewd, SummonedSkull, SkullServant, PotOfGreed, Celtic, Celtic, Bewd, Celtic,
                    Celtic, Celtic);
                var wants = HandDisruptionRuntime.AiWantsToActivateFromHand(engine, opp, pc);
                var ok = engine.TryActivateSpellTrap(opp, pc, fromHand: true);
                PassAll(engine);
                var pend = engine.PendingActivation;
                var asked = pend != null && pend.AwaitingOpponentPickAmongRevealed && pend.Controller == p &&
                            pend.LegalTargets.Count == 5;
                Check("Painful Choice (AI): the player is asked to pick 1 of the 5 cards the AI shows",
                    wants && ok && asked,
                    $"wants={wants} ok={ok} pending={(pend == null ? "none" : $"opp={pend.AwaitingOpponentPickAmongRevealed} ctrl={pend.Controller?.Name} n={pend.LegalTargets.Count}")}");
                var give = pend?.LegalTargets.OrderBy(c => c.Def?.atk ?? 0).FirstOrDefault();
                var gyBefore = opp.Graveyard.Count;
                var deckBefore = opp.Deck.Count;
                var picked = give != null && engine.TrySelectEffectTarget(give);
                Check("Painful Choice (AI): the player's pick goes to the AI's hand; the other 4 to the AI's GY",
                    picked && opp.Hand.Contains(give) && opp.HandCount == 1 &&
                    opp.Graveyard.Count == gyBefore + 4 && opp.Deck.Count == deckBefore - 5 &&
                    !engine.IsAwaitingEffectTarget,
                    $"picked={picked} give={give?.Name} hand={opp.HandCount} gy {gyBefore}->{opp.Graveyard.Count} " +
                    $"deck {deckBefore}->{opp.Deck.Count}");
                StackDeck(opp, Celtic, Celtic, Celtic, Celtic, Celtic, Celtic, Celtic, Celtic, Celtic);
                var pc2 = PutInHand(engine, opp, PainfulChoice);
                Check("Painful Choice (AI): not used when the Deck has fewer than 10 cards",
                    !HandDisruptionRuntime.AiWantsToActivateFromHand(engine, opp, pc2));
            }
        }
    }
}
