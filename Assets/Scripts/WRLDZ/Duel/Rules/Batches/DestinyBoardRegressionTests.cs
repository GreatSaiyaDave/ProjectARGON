using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Card batch "DestinyBoard": Destiny Board (ygopro c94212438.lua) and Spirit Message "I" / "N" / "A" / "L".
    /// The Board places the next Spirit Message in the order I, N, A, L during each of the opponent's End Phases
    /// (mandatory, once per turn, from the hand or Deck, face-up, no activation), its controller wins with the Board and
    /// all 4 messages face-up, and when any of them leaves the field the rest are sent to the GY. The messages have no
    /// activation and cannot be Set; they are Structural while the Board compiles to a placement that names them.
    /// </summary>
    public static class DestinyBoardRegressionTests
    {
        const int Board = 94212438;
        const int MsgI = 31893528;
        const int MsgN = 67287533;
        const int MsgA = 94772232;
        const int MsgL = 30170981;
        const int Mst = 5318639;              // Target 1 Spell/Trap on the field; destroy that target.
        const int SecondSarcophagus = 4081094; // placed only by "The First Sarcophagus" (live since DeckSummonsAndSarcophagi)
        const int Celtic = 91152256;          // filler

        static readonly string Named =
            "Spirit Message \"I\"|Spirit Message \"N\"|Spirit Message \"A\"|Spirit Message \"L\"";

        delegate void Chk(string name, bool ok, string detail = "");

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            Chk Check = (name, ok, detail) => check("DestinyBoard: " + name, ok, detail ?? "");

            CompileShapes(db, Check);
            NearMissesFailClosed(db, Check);
            MessagesCannotBeActivatedOrSet(db, pDeck, aDeck, Check);
            FullSequenceWins(db, pDeck, aDeck, Check);
            HumanChoosesHandOrDeck(db, pDeck, aDeck, Check, cancel: false);
            HumanChoosesHandOrDeck(db, pDeck, aDeck, Check, cancel: true);
            AiPlacesWithoutPrompt(db, pDeck, aDeck, Check);
            NoFreeZonePlacesNothing(db, pDeck, aDeck, Check);
            MessageDestroyedSendsAll(db, pDeck, aDeck, Check, destroyBoard: false);
            MessageDestroyedSendsAll(db, pDeck, aDeck, Check, destroyBoard: true);
        }

        // ───────────────────────────── compile ─────────────────────────────

        static void CompileShapes(CardDatabase db, Chk Check)
        {
            var def = db.Get(Board);
            var prog = def != null ? CardTextEffectCompiler.Compile(def) : null;
            var win = prog?.ClauseList.FirstOrDefault(c =>
                c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                c.Action == EffectActionKind.WinWithThisAndNamedSetFaceUpOnField);
            var place = prog?.ClauseList.FirstOrDefault(c =>
                c != null && c.Timing == EffectTiming.EndPhase &&
                c.Action == EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck);
            var send = prog?.ClauseList.FirstOrDefault(c =>
                c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                c.Action == EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField);
            Check("Destiny Board compiles to the win, the in-order End Phase placement and the leave-field send",
                prog != null && prog.FullyCompiled && prog.ClauseList.Count == 3 &&
                win != null && win.NamedCard == Named && !win.EffectNegatable &&
                place != null && place.NamedCard == Named && place.OpponentTurnOnly && place.OncePerTurn &&
                send != null && send.NamedCard == "Destiny Board" && send.TargetSeriesName == "Spirit Message" &&
                CardEffectStatus.Classify(def) == CardEffectStatusKind.Implemented,
                Describe(prog));

            foreach (var id in new[] { MsgI, MsgN, MsgA, MsgL })
            {
                var m = db.Get(id);
                var mp = m != null ? CardTextEffectCompiler.Compile(m) : null;
                Check($"{m?.name}: the placement lock is a rule (no clause), Structural and deck-legal",
                    mp != null && mp.FullyCompiled && mp.ClauseList.Count == 0 &&
                    DestinyBoardRuntime.PlacerOf(m) == "Destiny Board" &&
                    DestinyBoardRuntime.HasLivePlacementPath(m) &&
                    CardEffectStatus.Classify(m) == CardEffectStatusKind.Structural &&
                    CardEffectStatus.MayIncludeInDeck(m),
                    Describe(mp) + $" status={CardEffectStatus.Classify(m)}");
            }
        }

        static CardDef BoardLike(string desc) => new CardDef
        {
            id = 990159401, name = "ARGON DestinyBoard synthetic", type = "Trap Card", frameType = "trap",
            race = "Continuous", desc = desc
        };

        static void NearMissesFailClosed(CardDatabase db, Chk Check)
        {
            var printed = db.Get(Board)?.desc ?? "";
            var variants = new[]
            {
                ("a placement during your own End Phase",
                    printed.Replace("during your opponent's End Phase", "during your End Phase")),
                ("a printed count that is not the listed order",
                    printed.Replace("all 4 \"Spirit Message\"", "all 3 \"Spirit Message\"")),
                ("a leave-field rule that destroys instead of sending",
                    printed.Replace("send all \"Spirit Message\" cards and \"Destiny Board\" you control to the Graveyard",
                        "destroy all \"Spirit Message\" cards and \"Destiny Board\" you control")),
                ("a placement in any order",
                    printed.Replace(", in the proper order of \"I\", \"N\", \"A\", and \"L\"", ""))
            };
            foreach (var (what, desc) in variants)
            {
                var prog = CardTextEffectCompiler.Compile(BoardLike(desc));
                var hit = prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                    c != null && (c.Action == EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck ||
                                  c.Action == EffectActionKind.WinWithThisAndNamedSetFaceUpOnField ||
                                  c.Action == EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField));
                Check($"near miss — {what}: not compiled", desc != printed && !hit, Describe(prog));
            }

            // The same placement lock: Structural while its placer compiles to a placement naming it (The Second
            // Sarcophagus since the DeckSummonsAndSarcophagi batch), out of decks while it does not (a placer that places
            // nothing: Pot of Greed).
            var sarc = db.Get(SecondSarcophagus);
            Check("The Second Sarcophagus: placer The First Sarcophagus is a live placement — Structural",
                sarc != null && DestinyBoardRuntime.PlacerOf(sarc) == "The First Sarcophagus" &&
                DestinyBoardRuntime.HasLivePlacementPath(sarc) &&
                CardEffectStatus.Classify(sarc) == CardEffectStatusKind.Structural,
                $"status={CardEffectStatus.Classify(sarc)}");
            var dead = new CardDef
            {
                id = 990000311, name = "Near-miss placed card 990000311", type = sarc?.type ?? "Spell Card",
                race = sarc?.race ?? "Continuous",
                desc = "This card can only be placed on the field by the effect of \"Pot of Greed\"."
            };
            Check("A placement lock whose placer does not compile to a placement naming it stays Unimplemented",
                DestinyBoardRuntime.PlacerOf(dead) == "Pot of Greed" && !DestinyBoardRuntime.HasLivePlacementPath(dead) &&
                CardEffectStatus.Classify(dead) == CardEffectStatusKind.Unimplemented &&
                !CardEffectStatus.MayIncludeInDeck(dead),
                $"status={CardEffectStatus.Classify(dead)}");
        }

        // ───────────────────────────── the Spirit Messages ─────────────────────────────

        static void MessagesCannotBeActivatedOrSet(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            PlaceFaceUp(e, p, Board, 0);
            e.NotifyPublic();
            foreach (var id in new[] { MsgI, MsgN, MsgA, MsgL })
            {
                var msg = PutInHand(e, p, id);
                var canAct = e.CanActivateSpellTrap(p, msg, fromHand: true);
                var act = e.TryActivateSpellTrap(p, msg, fromHand: true);
                Drive(e, _ => true);
                var canSet = e.CanSetSpellTrap(p, msg);
                var set = e.TrySetSpellTrap(p, msg);
                Check($"{msg.Name} cannot be activated or Set from the hand (even with a face-up Destiny Board)",
                    !canAct && !act && !canSet && !set && p.Hand.Contains(msg) && !p.TryFindSpellTrap(msg, out _),
                    $"canAct={canAct} act={act} canSet={canSet} set={set} at={Where(e, msg)}");
                p.Hand.Remove(msg);
            }
        }

        // ───────────────────────────── the placement + the win ─────────────────────────────

        /// <summary>Activated from the field; I, N, A, L arrive over four opponent End Phases; the 4th wins.</summary>
        static void FullSequenceWins(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, Celtic, Celtic, Celtic, MsgL, MsgA, MsgN, MsgI, Celtic, Celtic });
            o.Deck.Clear();
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 12));
            var board = PlaceSetTrap(e, p, Board, 0);
            var act = e.TryActivateSpellTrap(p, board, fromHand: false);
            Drive(e, _ => true);
            var active = act && board.FaceUp && p.TryFindSpellTrap(board, out _);

            var trail = new List<string>();
            var arrived = new List<string>(); // each new message, in the order it was placed
            var seen = new HashSet<CardInstance>();
            var ok = active;
            var expected = new[] { 0, 1, 1, 2, 2, 3, 3 }; // messages after the End Phase of turns 1..7
            for (var i = 0; i < 8 && !e.GameOver; i++)
            {
                var tp = e.TurnPlayer;
                var ended = e.TryEndTurnSafe(tp);
                Drive(e, _ => true);
                var on = p.SpellTrapsOnField().Where(c => c != null && c != board && c.FaceUp).ToList();
                foreach (var c in on)
                    if (seen.Add(c)) arrived.Add(c.Name);
                var msgs = on.Select(c => c.Name).ToList();
                trail.Add($"t{i + 1}:{(tp == p ? "P" : "O")}:{string.Join(",", msgs)}");
                if (i < expected.Length)
                    ok &= ended && !e.GameOver && msgs.Count == expected[i] &&
                          DestinyBoardRuntime.PlacedCount(e, board) == expected[i];
            }

            var order = string.Join(",", arrived);
            Check("Destiny Board places I, N, A, L in the opponent's End Phases only, then its controller wins",
                ok && e.GameOver && e.Winner == p &&
                order == "Spirit Message \"I\",Spirit Message \"N\",Spirit Message \"A\",Spirit Message \"L\"" &&
                !p.Deck.Contains(MsgI) && !p.Deck.Contains(MsgL),
                $"active={active} trail=[{string.Join(" ", trail)}] order={order} over={e.GameOver} " +
                $"winner={e.Winner?.Name ?? "-"}");
        }

        /// <summary>The next message is in both the hand and the Deck: the human chooses, the End Phase waits.</summary>
        static void HumanChoosesHandOrDeck(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check, bool cancel)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, Celtic, MsgI, Celtic, Celtic, Celtic });
            o.Deck.Clear();
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            var board = PlaceFaceUp(e, p, Board, 0);
            var inHand = PutInHand(e, p, MsgI);
            e.NotifyPublic();
            e.TryEndTurnSafe(p);
            Drive(e, _ => true);
            var noOwnPhase = DestinyBoardRuntime.PlacedCount(e, board) == 0 && e.TurnPlayer == o;
            e.TryEndTurnSafe(o);
            var legal = Legal(e);
            var asked = e.IsAwaitingEffectTarget && e.Phase == DuelPhase.End && e.TurnPlayer == o &&
                        legal.Count == 2 && legal.Contains(inHand);
            var deckCopy = legal.FirstOrDefault(c => c != inHand);
            var answered = cancel ? e.CancelEffectTargeting() : deckCopy != null && e.TrySelectEffectTarget(deckCopy);
            Drive(e, _ => true);
            var placed = p.SpellTrapsOnField().FirstOrDefault(c => c != null && c != board && c.FaceUp);
            var right = cancel
                ? placed == inHand && !p.Hand.Contains(inHand) && p.Deck.Contains(MsgI)
                : placed == deckCopy && p.Hand.Contains(inHand) && !p.Deck.Contains(MsgI);
            Check(cancel
                    ? "Human with the next message in hand and Deck: CANCEL places the hand copy (mandatory)"
                    : "Human with the next message in hand and Deck: the End Phase waits, the Deck copy is placed",
                noOwnPhase && asked && answered && right && placed != null && placed.FaceUp &&
                DestinyBoardRuntime.PlacedCount(e, board) == 1 && e.TurnPlayer == p && !e.IsAwaitingEffectTarget,
                $"noOwnPhase={noOwnPhase} asked={asked} answered={answered} right={right} " +
                $"placed={placed?.Name ?? "-"} hand={inHand.Name}@{Where(e, inHand)} {State(e)}");
        }

        /// <summary>The AI's Board places with no prompt (its hand copy first); the human's End Phase never pauses.</summary>
        static void AiPlacesWithoutPrompt(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            o.Deck.Clear();
            o.Deck.AddRange(new[] { Celtic, MsgI, Celtic, Celtic });
            var board = PlaceFaceUp(e, o, Board, 0);
            var inHand = PutInHand(e, o, MsgI);
            e.NotifyPublic();
            var ended = e.TryEndTurnSafe(p);
            var noPause = e.TurnPlayer == o && !e.IsAwaitingEffectTarget;
            Drive(e, _ => true);
            Check("AI's Destiny Board places its hand copy in the human's End Phase with no prompt",
                ended && noPause && o.TryFindSpellTrap(inHand, out _) && inHand.FaceUp && o.Deck.Contains(MsgI) &&
                DestinyBoardRuntime.PlacedCount(e, board) == 1,
                $"ended={ended} noPause={noPause} msg={Where(e, inHand)} {State(e)}");
        }

        /// <summary>No free Spell &amp; Trap Zone: nothing is placed and nothing is counted; the next time, "I" comes first.</summary>
        static void NoFreeZonePlacesNothing(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            p.Deck.Clear();
            p.Deck.AddRange(new[] { Celtic, Celtic, Celtic, MsgN, MsgI, Celtic, Celtic });
            o.Deck.Clear();
            o.Deck.AddRange(Enumerable.Repeat(Celtic, 8));
            var board = PlaceFaceUp(e, p, Board, 0);
            for (var z = 1; z < p.SpellTrapZones.Length; z++)
                PlaceSetTrap(e, p, Mst, z);
            e.NotifyPublic();
            e.TryEndTurnSafe(p);
            Drive(e, _ => true);
            e.TryEndTurnSafe(o);
            Drive(e, _ => true);
            var none = DestinyBoardRuntime.PlacedCount(e, board) == 0 && p.Deck.Contains(MsgI);
            p.SpellTrapZones[1].Occupant = null;
            e.NotifyPublic();
            e.TryEndTurnSafe(p);
            Drive(e, _ => true);
            e.TryEndTurnSafe(o);
            Drive(e, _ => true);
            var first = p.SpellTrapZones[1].Occupant;
            Check("No free zone: nothing is placed or counted; the next opponent End Phase places \"I\"",
                none && first != null && first.CardId == MsgI && first.FaceUp &&
                DestinyBoardRuntime.PlacedCount(e, board) == 1 &&
                (p.Deck.Contains(MsgN) || p.Hand.Exists(c => c?.CardId == MsgN)),
                $"none={none} first={first?.Name ?? "-"} count={DestinyBoardRuntime.PlacedCount(e, board)} {State(e)}");
        }

        // ───────────────────────────── the leave-field send ─────────────────────────────

        /// <summary>Mystical Space Typhoon destroys a Spirit Message (or the Board): every other one goes to the GY.</summary>
        static void MessageDestroyedSendsAll(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check,
            bool destroyBoard)
        {
            var e = FreshCleared(db, pDeck, aDeck);
            var p = e.Player;
            var o = e.Opponent;
            var board = PlaceFaceUp(e, p, Board, 0);
            var msgI = PlaceFaceUp(e, p, MsgI, 1);
            var msgN = PlaceFaceUp(e, p, MsgN, 2);
            var other = PlaceSetTrap(e, p, Mst, 3);
            e.NotifyPublic();
            var victim = destroyBoard ? board : msgN;
            var mst = PutInHand(e, o, Mst);
            var reached = ReachOpponentMain(e);
            var act = reached && e.TryActivateSpellTrap(o, mst, fromHand: true);
            Drive(e, c => c == victim);
            var allGone = p.Graveyard.Contains(board) && p.Graveyard.Contains(msgI) && p.Graveyard.Contains(msgN);
            Check(destroyBoard
                    ? "Mystical Space Typhoon on Destiny Board: the Spirit Messages go to the GY too"
                    : "Mystical Space Typhoon on Spirit Message \"N\": Destiny Board and \"I\" go to the GY too",
                act && allGone && p.TryFindSpellTrap(other, out _) && !e.GameOver,
                $"reached={reached} act={act} board={Where(e, board)} I={Where(e, msgI)} N={Where(e, msgN)} " +
                $"other={Where(e, other)} mst={Where(e, mst)} {State(e)}");
        }

        // ───────────────────────────── helpers ─────────────────────────────

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

        static CardInstance PlaceFaceUp(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = e.CreateCardInstance(id);
            c.FaceUp = true;
            c.SetThisTurn = false;
            who.SpellTrapZones[zone].Occupant = c;
            return c;
        }

        static List<CardInstance> Legal(DuelEngine e) =>
            e.PendingActivation?.LegalTargets?.ToList() ?? new List<CardInstance>();

        /// <summary>Every prompt picks the first legal card matching <paramref name="want"/>; every chain window passes.</summary>
        static void Drive(DuelEngine e, Func<CardInstance, bool> want)
        {
            for (var i = 0; i < 20; i++)
            {
                if (e.IsAwaitingEffectTarget)
                {
                    var pick = Legal(e).FirstOrDefault(want);
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
        }

        static string Where(DuelEngine e, CardInstance c)
        {
            if (c == null) return "-";
            var at = new List<string>();
            foreach (var who in new[] { e.Player, e.Opponent })
            {
                var tag = who == e.Player ? "P" : "O";
                if (who.Hand.Contains(c)) at.Add(tag + ".hand");
                if (who.Graveyard.Contains(c)) at.Add(tag + ".gy");
                if (who.TryFindSpellTrap(c, out _)) at.Add(tag + ".stz" + (c.FaceUp ? "(up)" : "(set)"));
            }

            return string.Join("+", at);
        }

        static string State(DuelEngine e) =>
            $"pending={e.PendingActivation?.TargetKind.ToString() ?? "none"} response={e.IsAwaitingResponse} " +
            $"phase={e.Phase} turn={e.TurnNumber}/{e.TurnPlayer?.Name} over={e.GameOver}";

        static string Describe(CompiledCardProgram prog)
        {
            if (prog == null) return "null";
            var clauses = string.Join(" | ", prog.ClauseList.Where(c => c != null)
                .Select(c => $"{c.Timing}/{c.Action}/named={c.NamedCard}/series={c.TargetSeriesName}"));
            return $"full={prog.FullyCompiled} clauses=[{clauses}]";
        }
    }
}
