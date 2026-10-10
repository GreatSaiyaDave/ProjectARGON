using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card fix "EmptyHandStandbyDraw": The Agent of Wisdom - Mercury draws during its controller's Standby Phase
    /// only if they controlled it face-up and had no cards in hand at the end of the opponent's last End Phase.
    /// It used to compile to an unconditional Standby "Draw 1" (the condition before the colon was dropped), so
    /// an AI holding 6 cards still drew. Turn 1 is always the Player's, turn 2 the AI's, turn 3 the Player's.
    /// </summary>
    public static class EmptyHandStandbyDrawRegressionTests
    {
        const int Mercury = 38730226; // Fairy Lv4 0/1700
        const int Celtic = 91152256;  // filler hand card

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") =>
                check("EmptyHandStandbyDraw: " + name, ok, detail ?? "");

            CompileShape(db, Check);
            AiMercuryDraws(db, pDeck, aDeck, Check);
            PlayerMercuryDraws(db, pDeck, aDeck, Check);
            OwnEndPhaseDoesNotCount(db, pDeck, aDeck, Check);
            FieldPresenceMustLast(db, pDeck, aDeck, Check);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static DuelEngine Start(CardDatabase db, DeckFile pDeck, DeckFile aDeck)
        {
            var e = Fresh(db, pDeck, aDeck);
            if (e.IsAwaitingResponse) e.PassResponse();
            ClearBoard(e);
            return e;
        }

        static void EndTurn(DuelEngine e)
        {
            e.TryEndTurnSafe(e.TurnPlayer);
            if (e.IsAwaitingResponse) e.PassResponse();
        }

        static void SetHand(DuelEngine e, DuelistState who, int cards)
        {
            who.Hand.Clear();
            for (var i = 0; i < cards; i++)
                PutInHand(e, who, Celtic);
        }

        static CompiledCardProgram CompileMonsterText(int id, string text) =>
            CardTextEffectCompiler.Compile(new CardDef
            {
                id = id, name = "Near-miss Mercury", type = "Effect Monster", frameType = "effect",
                race = "Fairy", attribute = "LIGHT", level = 4, desc = text
            });

        static string State(DuelEngine e, DuelistState who) =>
            $"turn={e.TurnNumber} tp={e.TurnPlayer?.Name} phase={e.Phase} hand={who.Hand.Count} deck={who.DeckCount}";

        // ───────────────────────────── checks ─────────────────────────────

        static void CompileShape(CardDatabase db, Action<string, bool, string> Check)
        {
            var def = db.Get(Mercury);
            var prog = CardTextEffectCompiler.Compile(def);
            var c = prog != null && prog.ClauseList.Count == 1 ? prog.ClauseList[0] : null;
            Check("Mercury: FullyCompiled, one mandatory StandbyPhase Draw 1 gated on the opponent's last End Phase",
                prog != null && prog.FullyCompiled && prog.CanResolveAny && c != null &&
                c.Timing == EffectTiming.StandbyPhase && c.Action == EffectActionKind.Draw &&
                c.Side == EffectSide.Controller && c.Amount == 1 && c.RequiresEmptyHandAtOpponentsLastEndPhase &&
                !c.IsOptional && !c.OpponentTurnOnly && !c.EachStandbyPhase && c.MakesChainLink,
                c == null ? $"clauses={prog?.ClauseList.Count}" : EffectVocabulary.Describe(c));
            Check("Mercury: classified Implemented",
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                CardEffectStatus.Classify(def).ToString());

            // The current errata wording compiles to the same gated draw, so a cards_db text refresh cannot
            // bring back the unconditional draw.
            var errata = CompileMonsterText(990003,
                "Once per turn, during your Standby Phase, if you controlled this face-up card in your Monster Zone " +
                "and had no cards in your hand at the end of the opponent's last turn: Draw 1 card.");
            var ec = errata != null && errata.ClauseList.Count == 1 ? errata.ClauseList[0] : null;
            Check("errata wording: FullyCompiled to the same gated StandbyPhase Draw 1",
                errata != null && errata.FullyCompiled && ec != null && ec.Timing == EffectTiming.StandbyPhase &&
                ec.Action == EffectActionKind.Draw && ec.Side == EffectSide.Controller && ec.Amount == 1 &&
                ec.RequiresEmptyHandAtOpponentsLastEndPhase,
                ec == null ? $"clauses={errata?.ClauseList.Count}" : EffectVocabulary.Describe(ec));

            // Near miss: the atom takes only the whole sentence, never the head of a longer one.
            var longer = CompileMonsterText(990002,
                "During your Standby Phase, if you controlled this face-up card and had no cards in your " +
                "hand at the end of the opponent's last End Phase: Draw 1 card, then discard 1 card.");
            Check("a longer sentence is not taken by the Mercury atom",
                longer == null || !longer.ClauseList.Exists(x => x != null && x.RequiresEmptyHandAtOpponentsLastEndPhase),
                longer == null ? "null" : string.Join("; ", longer.ClauseList.ConvertAll(EffectVocabulary.Describe)));
        }

        /// <summary>
        /// The reported bug: the AI's face-up Mercury. Hand 0 at the end of the Player's End Phase → Draw Phase
        /// draw + Mercury draw; hand 1 or 6 → the Draw Phase draw only.
        /// </summary>
        static void AiMercuryDraws(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> Check)
        {
            foreach (var handAtEnd in new[] { 0, 1, 6 })
            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                PlaceMonster(e, o, Mercury, 0, BattlePosition.Defense, true);
                SetHand(e, o, handAtEnd);
                var deckBefore = o.DeckCount;
                EndTurn(e);
                var drawn = deckBefore - o.DeckCount;
                var expected = handAtEnd == 0 ? 2 : 1;
                Check($"AI Mercury, AI hand {handAtEnd} at the end of the Player's End Phase → draws {expected} on turn 2",
                    e.TurnPlayer == o && e.TurnNumber == 2 && drawn == expected &&
                    o.Hand.Count == handAtEnd + expected,
                    $"drawn={drawn} {State(e, o)}");
            }
        }

        /// <summary>The human side: the AI ends turn 2, the Player's Mercury draws on turn 3 only from an empty hand.</summary>
        static void PlayerMercuryDraws(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            foreach (var handAtEnd in new[] { 0, 1 })
            {
                var e = Start(db, pDeck, aDeck);
                var p = e.Player;
                PlaceMonster(e, p, Mercury, 0, BattlePosition.Defense, true);
                EndTurn(e);
                SetHand(e, p, handAtEnd);
                var deckBefore = p.DeckCount;
                EndTurn(e);
                var drawn = deckBefore - p.DeckCount;
                var expected = handAtEnd == 0 ? 2 : 1;
                Check($"Player Mercury, Player hand {handAtEnd} at the end of the AI's End Phase → draws {expected} on turn 3",
                    e.TurnPlayer == p && e.TurnNumber == 3 && drawn == expected,
                    $"drawn={drawn} {State(e, p)}");
            }
        }

        /// <summary>
        /// Only the opponent's End Phase counts, and only for your own Standby Phase: the Player ends turn 1 with
        /// an empty hand and a face-up Mercury; nothing is drawn for the Player during the AI's turn 2.
        /// </summary>
        static void OwnEndPhaseDoesNotCount(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            var e = Start(db, pDeck, aDeck);
            var p = e.Player;
            PlaceMonster(e, p, Mercury, 0, BattlePosition.Defense, true);
            SetHand(e, p, 0);
            var deckBefore = p.DeckCount;
            EndTurn(e);
            Check("your own End Phase with an empty hand draws nothing in the opponent's Standby Phase",
                e.TurnPlayer == e.Opponent && p.DeckCount == deckBefore && p.Hand.Count == 0,
                State(e, p));
        }

        /// <summary>
        /// "you controlled this face-up card … at the end of the opponent's last End Phase": a Mercury that was not
        /// on the field then, or was turned face-down since, draws nothing.
        /// </summary>
        static void FieldPresenceMustLast(CardDatabase db, DeckFile pDeck, DeckFile aDeck,
            Action<string, bool, string> Check)
        {
            // Not on the field at the end of the Player's End Phase: placed afterwards, Standby re-run by hand.
            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                SetHand(e, o, 0);
                EndTurn(e);
                var late = PlaceMonster(e, o, Mercury, 0, BattlePosition.Defense, true);
                SetHand(e, o, 0);
                var deckBefore = o.DeckCount;
                TextEffectRuntime.FirePhaseTriggers(e, o, EffectTiming.StandbyPhase);
                Check("a Mercury that was not face-up on the field at the end of the opponent's End Phase draws nothing",
                    e.TurnPlayer == o && o.EndOfOpponentsLastEndPhase != null &&
                    o.EndOfOpponentsLastEndPhase.HandEmpty &&
                    !EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand(e, o, late) &&
                    o.DeckCount == deckBefore,
                    State(e, o));
            }

            // Face-up then, turned face-down and back up since: a new face-up presence no longer qualifies.
            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                var mercury = PlaceMonster(e, o, Mercury, 0, BattlePosition.Defense, true);
                SetHand(e, o, 0);
                EndTurn(e);
                var qualified = EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand(e, o, mercury);
                mercury.FaceUp = false;
                mercury.FaceUp = true;
                var afterFlip = EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand(e, o, mercury);
                Check("a Mercury turned face-down since the opponent's End Phase no longer qualifies",
                    qualified && !afterFlip, $"qualified={qualified} afterFlip={afterFlip} {State(e, o)}");
            }

            // Face-down at the end of the Player's End Phase: not recorded, even if it is face-up later.
            {
                var e = Start(db, pDeck, aDeck);
                var o = e.Opponent;
                var mercury = PlaceMonster(e, o, Mercury, 0, BattlePosition.Defense, false);
                SetHand(e, o, 0);
                EndTurn(e);
                mercury.FaceUp = true;
                Check("a Mercury that was face-down at the end of the opponent's End Phase does not qualify",
                    !EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand(e, o, mercury), State(e, o));
            }
        }
    }
}
