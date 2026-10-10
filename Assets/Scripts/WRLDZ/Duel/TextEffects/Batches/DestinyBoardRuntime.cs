using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DestinyBoard" runtime (Destiny Board, Spirit Message "I" / "N" / "A" / "L"; ygopro c94212438.lua).
    /// Reads compiled clauses only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>The placement</b> (<see cref="EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck"/>, a mandatory
    /// trigger during the opponent's End Phase, once per turn, while this copy has placed fewer than all of its named
    /// cards): TextEffectRuntime.FirePhaseTriggersFor applies it through <see cref="OnPlacementTrigger"/>. The next name
    /// in printed order after the ones this copy already placed is taken from the hand or the Deck and put face-up in a
    /// free Spell &amp; Trap Zone (Lua MoveToField, no activation, no Chain Link). The count belongs to this copy's field
    /// presence (Lua flag RESETS_STANDARD): it is forgotten when the card leaves the field. A face-down or negated Board
    /// does nothing (FirePhaseTriggersFor / ApplyClause gates); no free zone or no copy in the hand or Deck places
    /// nothing. The AI takes the copy in its hand first. A human with a copy in both places is asked (an ordinary
    /// text-program pick over <see cref="EffectTargetKind.MonsterInHandOrDeck"/>, whose picker tags HAND / DECK) once
    /// the phase's other triggers are done (<see cref="OfferQueuedPick"/>, called by FirePhaseTriggers and
    /// DuelEngine.ResumeEndTurnAfterTrigger); the End Phase waits for the answer, and CANCEL takes the hand copy (the
    /// placement is mandatory). One copy in one place is placed with no prompt.</item>
    /// <item><b>The leave-field send</b> (<see cref="EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField"/>, Lua
    /// EVENT_LEAVE_FIELD continuous effects e3 / e4): on every board refresh (<see cref="OnBoardSettled"/>, called by
    /// DuelEngine.Notify) each side's series / named cards are compared with the last refresh; when one of them left
    /// that player's field while a face-up, non-negated Board was applying there, every face-up series / named card
    /// that player still controls is sent to the GY.</item>
    /// <item><b>The win</b> (<see cref="EffectActionKind.WinWithThisAndNamedSetFaceUpOnField"/>, Lua EVENT_ADJUST,
    /// EFFECT_FLAG_CANNOT_DISABLE): after the send, a player with a face-up Board in their Spell &amp; Trap Zone and
    /// every named card face-up on their field wins (both at once: a Draw).</item>
    /// <item><b>The placed cards</b> ("This card can only be placed on the field by the effect of "X"."): no
    /// activation and no Set (<see cref="PlacerOf"/>, read by OfficialEffectRegistry.CanActivateOfficial and
    /// DuelEngine.CanSetSpellTrap, which the UI and AI read); Structural only while X compiles to a placement that
    /// names the card (<see cref="HasLivePlacementPath"/>, read by CardEffectStatus).</item>
    /// </list>
    /// State is kept per engine, so a restarted Duel starts clean.
    /// </summary>
    public static class DestinyBoardRuntime
    {
        static readonly Regex RxPlacementLock = new Regex(
            @"This card can only be placed on the field by the effect of ""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        sealed class BoardRecord
        {
            public int Placed;
            public int LastTriggerTurn = -1;
        }

        sealed class QueuedPlacement
        {
            public DuelistState Who;
            public CardInstance Board;
            public EffectClause Clause;
            public int Turn;
        }

        sealed class SideSnapshot
        {
            public readonly List<CardInstance> Members = new();
            public CardInstance ApplyingBoard;
            public EffectClause SendClause;
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, BoardRecord> Boards = new();
            public readonly List<QueuedPlacement> Queue = new();
            public readonly Dictionary<DuelistState, SideSnapshot> Last = new();
            public PendingActivation OpenPick;
            public QueuedPlacement OpenFor;
            public bool Settling;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────── the placed cards ─────────────────────────

        /// <summary>
        /// "This card can only be placed on the field by the effect of "X"." (Spirit Message): X, else null. Such a card
        /// has no activation (Lua: no EFFECT_TYPE_ACTIVATE) and cannot be Set (EFFECT_CANNOT_SSET).
        /// </summary>
        public static string PlacerOf(CardDef def)
        {
            if (def?.desc == null || def.IsMonster) return null;
            var m = RxPlacementLock.Match(def.desc);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// True when the card's placer exists and compiles in full to a placement that names this card — the
        /// live-placement counterpart of CardEffectStatus.HasLiveSummonPath.
        /// </summary>
        public static bool HasLivePlacementPath(CardDef def)
        {
            var placer = PlacerOf(def);
            if (placer == null || string.IsNullOrEmpty(def.name)) return false;
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            if (db == null) return false;
            foreach (var c in db.GetAllCards())
            {
                if (c == null || !string.Equals(c.name, placer, StringComparison.OrdinalIgnoreCase)) continue;
                var prog = CompiledEffectCache.GetOrCompile(c);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var clause in prog.ClauseList)
                {
                    if (clause == null || clause.Action != EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck) continue;
                    foreach (var n in Names(clause.NamedCard))
                        if (string.Equals(n, def.name, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }

            return false;
        }

        static List<string> Names(string named)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(named)) return list;
            foreach (var n in named.Split('|'))
                if (!string.IsNullOrWhiteSpace(n))
                    list.Add(n.Trim());
            return list;
        }

        // ───────────────────────── the placement ─────────────────────────

        static BoardRecord RecordOf(DuelEngine engine, CardInstance board)
        {
            var st = StateOf(engine);
            if (!st.Boards.TryGetValue(board, out var rec))
                st.Boards[board] = rec = new BoardRecord();
            return rec;
        }

        /// <summary>How many named cards this copy has placed during its current field presence (tests, UI).</summary>
        public static int PlacedCount(DuelEngine engine, CardInstance board) =>
            engine != null && board != null && StateOf(engine).Boards.TryGetValue(board, out var rec) ? rec.Placed : 0;

        /// <summary>
        /// TextEffectRuntime.ApplyClause arm (from FirePhaseTriggersFor, the opponent's End Phase): Lua s.plcon / s.plop.
        /// </summary>
        public static void OnPlacementTrigger(DuelEngine engine, DuelistState who, CardInstance board, EffectClause clause)
        {
            if (engine == null || who == null || board == null || clause == null) return;
            if (!board.FaceUp || !who.TryFindSpellTrap(board, out _)) return;
            var names = Names(clause.NamedCard);
            if (names.Count == 0) return;
            var rec = RecordOf(engine, board);
            if (rec.Placed >= names.Count) return;
            if (clause.OncePerTurn && rec.LastTriggerTurn == engine.TurnNumber) return;
            rec.LastTriggerTurn = engine.TurnNumber;
            var q = new QueuedPlacement { Who = who, Board = board, Clause = clause, Turn = engine.TurnNumber };
            if (NeedsChoice(engine, q))
            {
                StateOf(engine).Queue.Add(q);
                return;
            }

            Place(engine, q, null);
        }

        static string NextName(DuelEngine engine, QueuedPlacement q)
        {
            var names = Names(q.Clause?.NamedCard);
            var rec = RecordOf(engine, q.Board);
            return rec.Placed < names.Count ? names[rec.Placed] : null;
        }

        static void Candidates(DuelEngine engine, DuelistState who, string name, out CardInstance inHand,
            out int deckId)
        {
            inHand = null;
            deckId = -1;
            foreach (var h in who.Hand)
            {
                if (h?.Def == null || !h.IsNamed(name)) continue;
                inHand = h;
                break;
            }

            if (who.Deck == null || engine.Database == null) return;
            foreach (var id in who.Deck)
            {
                if (!engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                deckId = id;
                break;
            }
        }

        /// <summary>A human with the next card in both the hand and the Deck, and a free zone, chooses.</summary>
        static bool NeedsChoice(DuelEngine engine, QueuedPlacement q)
        {
            if (q?.Who == null || !engine.IsHumanControlled(q.Who)) return false;
            var name = NextName(engine, q);
            if (name == null || engine.FirstEmptySpellTrap(q.Who) < 0) return false;
            Candidates(engine, q.Who, name, out var inHand, out var deckId);
            return inHand != null && deckId >= 0;
        }

        /// <summary>
        /// Place the next named card face-up. <paramref name="pick"/>: the chosen copy (a hand card, or a Deck copy
        /// materialized for the picker); null = automatic (the hand copy first, else the Deck).
        /// </summary>
        static void Place(DuelEngine engine, QueuedPlacement q, CardInstance pick)
        {
            var who = q.Who;
            var board = q.Board;
            if (!board.FaceUp || !who.TryFindSpellTrap(board, out _))
            {
                engine.Log($"{board.Name} is no longer face-up on the field — nothing is placed.");
                return;
            }

            var name = NextName(engine, q);
            if (name == null) return;
            var zi = engine.FirstEmptySpellTrap(who);
            if (zi < 0)
            {
                engine.Log($"{board.Name}: no free Spell & Trap Zone — {name} is not placed.");
                return;
            }

            Candidates(engine, who, name, out var inHand, out var deckId);
            CardInstance card;
            string from;
            var fromHand = pick != null ? who.Hand.Contains(pick) : inHand != null;
            if (fromHand)
            {
                card = pick ?? inHand;
                who.Hand.Remove(card);
                from = "hand";
            }
            else if (deckId >= 0)
            {
                var at = who.Deck.IndexOf(pick != null ? pick.CardId : deckId);
                if (at < 0) at = who.Deck.IndexOf(deckId);
                var id = who.Deck[at];
                card = pick != null && pick.CardId == id ? pick : engine.CreateCardInstance(id);
                // PerCopyDeck batch (v163): the instance takes the copy's owner.
                PerCopyDeckRuntime.TakeFromDeck(who, at, card);
                engine.Rng.Shuffle(who.Deck);
                from = "Deck";
            }
            else
            {
                engine.Log($"{board.Name}: {who.Name} has no {name} in the hand or Deck — nothing is placed.");
                return;
            }

            card.FaceUp = true;
            card.SetThisTurn = false;
            who.SpellTrapZones[zi].Occupant = card;
            var rec = RecordOf(engine, board);
            rec.Placed++;
            engine.Log($"{board.Name}: {who.Name} places {card.Name} face-up from the {from} " +
                       $"({rec.Placed}/{Names(q.Clause.NamedCard).Count}).");
            engine.NotifyPublic();
        }

        /// <summary>
        /// Ask a human's queued placement (copies in both the hand and the Deck). Called once the End Phase triggers
        /// have fired (TextEffectRuntime.FirePhaseTriggers) and when the paused End Phase resumes
        /// (DuelEngine.ResumeEndTurnAfterTrigger). A queued placement with only one choice left is made at once.
        /// True while a pick is open.
        /// </summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || engine.PendingActivation != null) return false;
            var st = StateOf(engine);
            while (st.Queue.Count > 0)
            {
                var q = st.Queue[0];
                st.Queue.RemoveAt(0);
                // A placement belongs to the End Phase that triggered it; a leftover from an earlier turn is dropped.
                if (q.Turn != engine.TurnNumber) continue;
                if (!NeedsChoice(engine, q))
                {
                    Place(engine, q, null);
                    if (engine.GameOver) return false;
                    continue;
                }

                var name = NextName(engine, q);
                Candidates(engine, q.Who, name, out var inHand, out var deckId);
                var pending = new PendingActivation
                {
                    Controller = q.Who,
                    Card = q.Board,
                    TargetKind = EffectTargetKind.MonsterInHandOrDeck,
                    UsesTextProgram = true,
                    IsMonsterEffect = false,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"place {name} face-up in your Spell & Trap Zone — choose the copy in your hand " +
                                     "or the one in your Deck (CANCEL takes the one in your hand)."
                };
                pending.LegalTargets.Add(inHand);
                pending.LegalTargets.Add(engine.CreateCardInstance(deckId));
                st.OpenPick = pending;
                st.OpenFor = q;
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        /// <summary>TextEffectRuntime.TryResolveTextTarget hook: the human chose the copy to place.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null) return false;
            var st = StateOf(engine);
            if (!ReferenceEquals(st.OpenPick, pending)) return false;
            var pick = engine.ResolveLegalEffectTarget(target);
            if (pick == null)
            {
                engine.Log("Choose one of the highlighted cards.");
                return true;
            }

            var q = st.OpenFor;
            st.OpenPick = null;
            st.OpenFor = null;
            engine.ClearPendingActivation();
            Place(engine, q, pick);
            ok = true;
            GoOn(engine);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the placement is mandatory, so CANCEL takes the hand copy.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null) return false;
            var st = StateOf(engine);
            if (!ReferenceEquals(st.OpenPick, pending)) return false;
            var q = st.OpenFor;
            st.OpenPick = null;
            st.OpenFor = null;
            engine.ClearPendingActivation();
            Place(engine, q, null);
            GoOn(engine);
            return true;
        }

        static void GoOn(DuelEngine engine)
        {
            if (engine.GameOver) return;
            if (OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            engine.ResumeEndTurnAfterTrigger();
            engine.NotifyPublic();
        }

        // ───────────────────────── leave-field send + win (every board refresh) ─────────────────────────

        /// <summary>
        /// DuelEngine.Notify, after FieldSpellEffects.RefreshBoard: the leave-field send, then the field win. True when
        /// the Duel ended.
        /// </summary>
        public static bool OnBoardSettled(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return false;
            var st = StateOf(engine);
            if (st.Settling) return false;
            st.Settling = true;
            try
            {
                foreach (var side in new[] { engine.Player, engine.Opponent })
                    SweepLeaveField(engine, st, side);
                // A Board's placement count belongs to its field presence (Lua RESETS_STANDARD).
                List<CardInstance> gone = null;
                foreach (var kv in st.Boards)
                    if (engine.ControllerOf(kv.Key) == null || !kv.Key.FaceUp)
                        (gone ??= new List<CardInstance>()).Add(kv.Key);
                if (gone != null)
                    foreach (var b in gone)
                        st.Boards.Remove(b);
                return !engine.GameOver && CheckFieldWin(engine);
            }
            finally
            {
                st.Settling = false;
            }
        }

        static IEnumerable<CardInstance> FieldCards(DuelistState side)
        {
            foreach (var m in side.MonstersOnField())
                if (m != null) yield return m;
            foreach (var s in side.SpellTrapsOnField())
                if (s != null) yield return s;
            if (side.FieldSpellZone?.Occupant != null)
                yield return side.FieldSpellZone.Occupant;
        }

        static bool OnSideField(DuelistState side, CardInstance card)
        {
            foreach (var c in FieldCards(side))
                if (ReferenceEquals(c, card)) return true;
            return false;
        }

        static EffectClause ClauseOf(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == action)
                    return c;
            return null;
        }

        static bool IsMember(CardInstance card, EffectClause send) =>
            card?.Def != null && send != null &&
            (TextEffectRuntime.CardMatchesSeries(card.Def, send.TargetSeriesName) ||
             (!string.IsNullOrEmpty(send.NamedCard) && card.IsNamed(send.NamedCard)));

        static SideSnapshot Snapshot(DuelEngine engine, DuelistState side)
        {
            var snap = new SideSnapshot();
            foreach (var st in side.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.IsNegated) continue;
                var send = ClauseOf(st, EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField);
                if (send == null || ContinuousNegationRuntime.NegatedBy(engine, st) != null) continue;
                snap.ApplyingBoard = st;
                snap.SendClause = send;
                break;
            }

            if (snap.SendClause == null) return snap;
            foreach (var c in FieldCards(side))
                if (IsMember(c, snap.SendClause))
                    snap.Members.Add(c);
            return snap;
        }

        static void SweepLeaveField(DuelEngine engine, State st, DuelistState side)
        {
            if (st.Last.TryGetValue(side, out var last) && last.SendClause != null)
            {
                CardInstance left = null;
                foreach (var m in last.Members)
                {
                    if (OnSideField(side, m)) continue;
                    left = m;
                    break;
                }

                if (left != null)
                {
                    var send = new List<CardInstance>();
                    foreach (var c in FieldCards(side))
                        if (c.FaceUp && IsMember(c, last.SendClause))
                            send.Add(c);
                    if (send.Count > 0)
                    {
                        engine.Log($"{last.ApplyingBoard.Name}: {left.Name} left the field — {side.Name} sends " +
                                   $"{send.Count} card(s) to the GY.");
                        foreach (var c in send)
                            if (OnSideField(side, c))
                                engine.SendCardToGrave(side, c, sentBy: last.ApplyingBoard);
                    }
                }
            }

            st.Last[side] = Snapshot(engine, side);
        }

        static bool CheckFieldWin(DuelEngine engine)
        {
            var p = FieldWinOf(engine.Player, out var pBoard);
            var o = FieldWinOf(engine.Opponent, out var oBoard);
            if (p == null && o == null) return false;
            if (p != null && o != null)
            {
                engine.EndGameByCardEffect(null, "Both players completed their Destiny Board — the Duel is a draw.");
                return true;
            }

            var board = p != null ? pBoard : oBoard;
            engine.EndGameByCardEffect(p != null ? engine.Player : engine.Opponent,
                $"{board.Name} and every \"{(p ?? o).TargetSeriesName}\" card it names are face-up on the field");
            return true;
        }

        /// <summary>The win clause of a face-up Board on <paramref name="side"/> whose named cards are all face-up there.</summary>
        static EffectClause FieldWinOf(DuelistState side, out CardInstance board)
        {
            board = null;
            if (side == null) return null;
            foreach (var st in side.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp) continue;
                var win = ClauseOf(st, EffectActionKind.WinWithThisAndNamedSetFaceUpOnField);
                if (win == null) continue;
                var names = Names(win.NamedCard);
                if (names.Count == 0) continue;
                var all = true;
                foreach (var n in names)
                {
                    var found = false;
                    foreach (var c in FieldCards(side))
                    {
                        if (ReferenceEquals(c, st) || !c.FaceUp || !c.IsNamed(n)) continue;
                        found = true;
                        break;
                    }

                    if (found) continue;
                    all = false;
                    break;
                }

                if (!all) continue;
                board = st;
                return win;
            }

            return null;
        }
    }
}
