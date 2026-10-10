using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "HandDisruption" (v142) runtime. Reads compiled programs only — no card-id branches.
    ///
    /// · <b>Gamble</b> (<see cref="EffectActionKind.CoinCallDrawUntilOrSkipNextTurn"/>): activation condition
    ///   <see cref="HandSizes"/>; at resolution a called coin (Duel.CallCoin). The call is made automatically
    ///   (Heads) — a presentation residual: asking a human would need the chain to pause mid-resolution for the
    ///   AwaitingCoinCall prompt (DuelEngine.ResolveChainStack pauses only for Magic Drain's waiver today), and a
    ///   fair coin is called right half the time whichever face is called. Right: draw until the hand has Amount
    ///   cards. Wrong: <see cref="DuelistState.SkipNextTurn"/>, consumed by DuelEngine.BeginTurn through
    ///   <see cref="ConsumeSkippedTurn"/> (the turn begins and ends with no phases).
    /// · <b>Appropriate</b> (<see cref="EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase"/>): DuelEngine.Draw
    ///   calls <see cref="OnDrew"/> after every draw. A draw outside either Draw Phase makes each face-up,
    ///   non-negated copy the other player controls draw its Amount at once (EFFECT_TYPE_CONTINUOUS, no Chain
    ///   Link), and queues a <see cref="ResponseTiming.OpponentDrewOutsideDrawPhase"/> window for the other
    ///   player when they have a Set copy. "Activate only when" is when-timing: the draw must be the last thing
    ///   that happened. The window opens at the next stable point (the draw's chain has resolved, no window /
    ///   pick / attack is open: <see cref="FlushDrawWindow"/>, called from DuelEngine.Notify and after an idle
    ///   Chain Link is dropped) only if nothing happened after the draw (<see cref="DrawStillLast"/>: the
    ///   resolved Spell/Trap itself going to the GY does not count); it is dropped when the turn or phase changed
    ///   first, or when the drawing effect went on after the draw (<see cref="EffectContinuesAfterDraw"/>). No
    ///   window is queued for a draw by a Chain Link that is not the last to resolve (the timing is missed), in the
    ///   Standby or End Phase (DuelEngine.BeginTurn / EndTurn run those phases straight through, so no point in them
    ///   is stable), or while an attack is under way (the engine carries the attack on into damage right after
    ///   the chain; such a Trap cannot be activated in the Damage Step at all). Activating the Set copy in the
    ///   window is its only legal activation (<see cref="OpponentDrewOutsideDrawPhase"/> condition); the
    ///   activation seats a Chain Link that resolves by leaving the card face-up.
    /// · <b>Painful Choice</b> (<see cref="EffectActionKind.RevealDeckCardsOpponentPicksOneToHand"/>): at
    ///   resolution the activating player selects Amount cards from their Deck (a human through a
    ///   <see cref="PendingActivation.AwaitingDeckRevealPicks"/> pick; the AI shows its most valuable cards), the
    ///   opponent selects 1 of them (a human through <see cref="PendingActivation.AwaitingOpponentPickAmongRevealed"/>;
    ///   the AI gives the least valuable), that card is added to the activating player's hand and the others are
    ///   sent to the GY; the Deck is shuffled. A Normal Spell is always the last link of its chain to resolve, so
    ///   the picks never hold up another link.
    /// </summary>
    public static class HandDisruptionRuntime
    {
        // ── EffectClause.ActivationCondition values ──

        /// <summary>
        /// Gamble: the opponent has at least <see cref="EffectClause.RequiresOpponentHandAtLeast"/> cards in hand
        /// and the activating player at most <see cref="EffectClause.ConditionAmount"/>.
        /// </summary>
        public const string HandSizes = "HandSizesOppAtLeastYoursAtMost";

        /// <summary>
        /// Appropriate: only in the <see cref="ResponseTiming.OpponentDrewOutsideDrawPhase"/> window offered to the
        /// activating player (their opponent just drew outside either Draw Phase).
        /// </summary>
        public const string OpponentDrewOutsideDrawPhase = "OpponentDrewOutsideDrawPhase";

        /// <summary>Painful Choice: the activating player's Deck holds at least ConditionAmount cards.</summary>
        public const string ControllerDeckAtLeast = "ControllerDeckAtLeast";

        sealed class DrawWindow
        {
            public DuelistState Responder;
            public int Turn;
            public DuelPhase Phase;

            // "The draw is the last thing that happened" — the state right after the draw (see DrawStillLast).
            public int EventCount;
            public HashSet<CardInstance> Resolving;
            public List<object> Board;
            public List<CardInstance> PlayerHand;
            public List<CardInstance> OpponentHand;

            /// <summary>Found still valid at a stable point; it opens after the windows queued with it.</summary>
            public bool Checked;
        }

        sealed class State
        {
            public readonly List<DrawWindow> DrawWindows = new();
            public bool Flushing;

            // Painful Choice selection in progress.
            public DuelistState RevealActivator;
            public CardInstance RevealSource;
            public readonly List<CardInstance> Revealed = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ─────────────────────────── activation conditions ───────────────────────────

        /// <summary>
        /// Checks an activation condition this batch owns. Returns false when the clause's ActivationCondition is
        /// not one of them (the caller goes on to the next batch / reports it as unknown).
        /// </summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause clause,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (clause == null || engine == null || who == null) return false;
            switch (clause.ActivationCondition)
            {
                case HandSizes:
                {
                    var oppMin = clause.RequiresOpponentHandAtLeast > 0 ? clause.RequiresOpponentHandAtLeast : 1;
                    var oppHand = engine.OpponentOf(who)?.HandCount ?? 0;
                    ok = oppHand >= oppMin && who.HandCount <= clause.ConditionAmount;
                    reason = $"Only while your opponent has {oppMin} or more cards in hand and you have " +
                             $"{clause.ConditionAmount} or less.";
                    return true;
                }
                case OpponentDrewOutsideDrawPhase:
                    ok = InDrawWindow(engine, who);
                    reason = "Only right after your opponent draws a card(s) outside of either Draw Phase.";
                    return true;
                case ControllerDeckAtLeast:
                {
                    var min = clause.ConditionAmount > 0 ? clause.ConditionAmount : 1;
                    ok = (who.Deck?.Count ?? 0) >= min;
                    reason = $"Needs {min} or more cards in your Deck.";
                    return true;
                }
                default:
                    return false;
            }
        }

        // ─────────────────────────── Gamble ───────────────────────────

        /// <summary>Gamble's resolution: a called coin, then draw until Amount cards in hand, or skip the next turn.</summary>
        public static void ResolveCoinCallDrawUntilOrSkip(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            // Presentation residual: the call is made for the player (Heads) — the chain does not pause for a
            // call prompt while it resolves; with a fair coin the chance of calling right is the same.
            const bool callHeads = true;
            // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo it before the call is judged.
            CoinDiceInterruptsRuntime.TossCoin(engine, who, source, t => t == callHeads,
                heads => FinishCoinCallDrawUntilOrSkip(engine, who, source, clause, heads == callHeads, heads));
        }

        static void FinishCoinCallDrawUntilOrSkip(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, bool right, bool heads)
        {
            if (engine.GameOver) return;
            engine.Log($"{source?.Name}: the call is made for {who.Name} (Heads); the toss is " +
                       $"{(heads ? "Heads" : "Tails")} — {(right ? "called right" : "called wrong")}.");
            if (right)
            {
                var target = clause.Amount > 0 ? clause.Amount : 5;
                var n = target - who.HandCount;
                if (n <= 0)
                {
                    engine.Log($"{source?.Name}: {who.Name} already has {who.HandCount} cards in hand — no draw.");
                    return;
                }

                engine.Log($"{source?.Name}: {who.Name} draws until their hand has {target} cards ({n}).");
                engine.Draw(who, n);
                return;
            }

            who.SkipNextTurn = true;
            engine.Log($"{source?.Name}: {who.Name} will skip their next turn.");
        }

        /// <summary>
        /// DuelEngine.BeginTurn: <paramref name="who"/>'s turn has just begun (turn number counted). True when it is
        /// skipped — the flag is consumed and the caller hands the next turn to the opponent at once.
        /// </summary>
        public static bool ConsumeSkippedTurn(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !who.SkipNextTurn || engine.GameOver) return false;
            who.SkipNextTurn = false;
            // ygopro still resets Draw / Standby Phase effects in a skipped turn (reset_phase), so a pending
            // "skip your next Draw Phase" (Time Seal, Fenrir; one of Reckless Greed's) or "next Standby Phase"
            // (Solomon's Lawbook) is used up by it rather than carried to a later turn.
            if (who.SkipNextDrawPhaseCount > 0)
                who.SkipNextDrawPhaseCount--;
            // The opponent's skip (Yata-Garasu, Fenrir, Time Seal) covers this same Draw Phase and resets with it
            // (SpiritsAndMisc: count and flag never skip two Draw Phases).
            who.SkipNextDrawPhase = false;
            // Hino-Kagu-Tsuchi's pending discard (EVENT_PREDRAW, RESET_PHASE+PHASE_DRAW) ends with this Draw Phase reset.
            SpiritsAndMiscRuntime.OnDrawPhaseStart(engine, skippedByEffect: true);
            who.SkipNextStandbyPhase = false;
            // SpiritMonsters batch: Great Long Nose's Battle Phase skip and Maharaghi's look end with this turn's resets.
            SpiritMonstersRuntime.OnSkippedTurn(engine, who);
            var side = who.IsPlayer ? "YOUR" : "OPPONENT";
            engine.Log($"════ {side} TURN (game turn #{engine.TurnNumber}) — {who.Name}'s turn is skipped (card effect) ════");
            return true;
        }

        /// <summary>AI: take Gamble whenever it is legal (its own hand is small and the opponent's is big).</summary>
        public static bool AiWantsToGamble(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            return prog != null && prog.FullyCompiled &&
                   prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.CoinCallDrawUntilOrSkipNextTurn);
        }

        // ─────────────────────────── Appropriate ───────────────────────────

        /// <summary>A complete program whose activation is only legal in the draw window (Appropriate).</summary>
        public static bool AnswersDrawWindow(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled &&
            prog.ClauseList.Exists(c => c != null && c.ActivationCondition == OpponentDrewOutsideDrawPhase);

        static bool InDrawWindow(DuelEngine engine, DuelistState who)
        {
            var pr = engine?.PendingResponse;
            return pr != null && pr.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase && pr.Responder == who;
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveActivation in the draw window: the Continuous Trap is placed face-up as a
        /// Chain Link (the opponent may respond); it resolves by staying face-up.
        /// </summary>
        public static bool ActivateInDrawWindow(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, CompiledCardProgram prog, bool autoPickTarget)
        {
            if (engine == null || who == null || card == null || !AnswersDrawWindow(prog)) return false;
            return TextEffectRuntime.SeatResponseOnChain(engine, who, card, fromHand,
                ResponseTiming.OpponentDrewOutsideDrawPhase, null, new List<EffectClause>(), autoPickTarget);
        }

        /// <summary>
        /// DuelEngine.Draw, after <paramref name="drawer"/> drew <paramref name="drawn"/> card(s). Outside either
        /// Draw Phase: each face-up Appropriate the other player controls draws them its Amount now, and a Set
        /// copy gets its activation window queued.
        /// </summary>
        public static void OnDrew(DuelEngine engine, DuelistState drawer, int drawn)
        {
            if (engine == null || drawer == null || drawn <= 0 || engine.GameOver || engine.TurnNumber <= 0) return;
            if (engine.Phase == DuelPhase.Draw) return;
            var other = engine.OpponentOf(drawer);
            if (other == null) return;

            foreach (var st in other.SpellTrapsOnField().ToList())
            {
                if (engine.GameOver) return;
                var clause = FaceUpDrawClause(engine, other, st);
                if (clause == null) continue;
                var n = clause.Amount > 0 ? clause.Amount : 2;
                engine.Log($"{st.Name}: {drawer.Name} drew outside the Draw Phase — {other.Name} draws {n}.");
                engine.Draw(other, n);
            }

            // No window when the draw came from a Chain Link that is not the last to resolve (later links resolve
            // after it: the timing is missed); in the Standby or End Phase (BeginTurn / EndTurn run those phases
            // straight through, so there is no stable point before the phase moves on); or while an attack is under
            // way (the engine carries the attack on into damage right after the chain, and such a Trap cannot be
            // activated in the Damage Step): at no point there is the draw still the last thing that happened.
            if (engine.GameOver || engine.BattleStep == BattleStep.DamageStep || engine.HasDeclaredAttack) return;
            if (engine.Phase == DuelPhase.Standby || engine.Phase == DuelPhase.End) return;
            if ((engine.IsResolvingChain || engine.Chain.IsResolving) && engine.Chain.Count > 0) return;
            if (!other.SpellTrapsOnField().Any(st => st != null && !st.FaceUp &&
                                                      AnswersDrawWindow(CompiledEffectCache.GetOrCompile(st))))
                return;
            var state = StateOf(engine);
            state.DrawWindows.RemoveAll(w => w.Responder == other);
            var window = new DrawWindow { Responder = other, Turn = engine.TurnNumber, Phase = engine.Phase };
            RecordAfterDraw(engine, window);
            state.DrawWindows.Add(window);
        }

        /// <summary>
        /// The resolving effect goes on after its draw (ygopro Duel.BreakEffect after Duel.Draw — Hiro's Shadow
        /// Scout: both players look at the drawn cards, then the Spells are discarded): the draw is no longer the
        /// last thing that happened, so every queued "opponent drew" window misses the timing.
        /// </summary>
        public static void EffectContinuesAfterDraw(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var state) || state.DrawWindows.Count == 0) return;
            state.DrawWindows.Clear();
            engine.Log("The effect goes on after the draw — \"when your opponent draws\" cards miss the timing.");
        }

        /// <summary>
        /// Remember the state right after the draw: events logged so far, both hands, and a board signature (LP,
        /// GY, banished, every field zone) that leaves out the Spell/Trap cards that are resolving now or still on
        /// the chain (a face-up Normal / Quick-Play Spell or Normal / Counter Trap on the field, or a chain link's
        /// card) — those go to the GY once they resolve, which does not count as something happening after the draw.
        /// </summary>
        static void RecordAfterDraw(DuelEngine engine, DrawWindow w)
        {
            var resolving = new HashSet<CardInstance>();
            foreach (var d in new[] { engine.Player, engine.Opponent })
            {
                if (d == null) continue;
                foreach (var st in d.SpellTrapsOnField())
                    if (st?.Def != null && st.FaceUp && (st.Def.IsSpell || st.Def.IsTrap) &&
                        !st.Def.StaysFlatOnFieldWhenActivated)
                        resolving.Add(st);
            }

            foreach (var link in engine.Chain.Links)
                if (link?.Card != null)
                    resolving.Add(link.Card);
            w.Resolving = resolving;
            w.EventCount = engine.GameEvents.Events.Count;
            w.Board = BoardSignature(engine, resolving);
            w.PlayerHand = engine.Player?.Hand.ToList() ?? new List<CardInstance>();
            w.OpponentHand = engine.Opponent?.Hand.ToList() ?? new List<CardInstance>();
        }

        static List<object> BoardSignature(DuelEngine engine, HashSet<CardInstance> resolving)
        {
            var sig = new List<object>();
            foreach (var d in new[] { engine.Player, engine.Opponent })
            {
                if (d == null)
                {
                    sig.Add(null);
                    continue;
                }

                sig.Add(d.LifePoints);
                sig.Add("gy");
                foreach (var c in d.Graveyard)
                    if (c != null && !resolving.Contains(c))
                        sig.Add(c);
                sig.Add("banished");
                foreach (var c in d.Banished)
                    if (c != null)
                        sig.Add(c);
                sig.Add("monsters");
                foreach (var z in d.MonsterZones)
                {
                    var c = z?.Occupant;
                    sig.Add(c);
                    if (c == null) continue;
                    sig.Add(c.FaceUp);
                    sig.Add(c.Position);
                }

                sig.Add("spells-traps");
                foreach (var z in d.SpellTrapZones)
                {
                    var c = z?.Occupant;
                    if (c != null && resolving.Contains(c)) c = null;
                    sig.Add(c);
                    if (c != null) sig.Add(c.FaceUp);
                }

                sig.Add(d.FieldSpellZone?.Occupant);
            }

            return sig;
        }

        /// <summary>
        /// Nothing happened since the draw: no card left a hand (later draws only add), the board signature is
        /// the same, and every event logged since is a resolving Spell/Trap going to the GY.
        /// </summary>
        static bool DrawStillLast(DuelEngine engine, DrawWindow w)
        {
            if (w.Board == null) return false;
            if (engine.Player != null && w.PlayerHand.Exists(c => !engine.Player.Hand.Contains(c))) return false;
            if (engine.Opponent != null && w.OpponentHand.Exists(c => !engine.Opponent.Hand.Contains(c))) return false;
            var events = engine.GameEvents.Events;
            for (var i = w.EventCount; i < events.Count; i++)
                if (events[i].Kind != DuelGameEventKind.SentToGy || events[i].Card == null ||
                    !w.Resolving.Contains(events[i].Card))
                    return false;
            var now = BoardSignature(engine, w.Resolving);
            if (now.Count != w.Board.Count) return false;
            for (var i = 0; i < now.Count; i++)
                if (!Equals(now[i], w.Board[i]))
                    return false;
            return true;
        }

        /// <summary>The continuous draw clause of a face-up copy whose effects apply now, or null.</summary>
        static EffectClause FaceUpDrawClause(DuelEngine engine, DuelistState controller, CardInstance card)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) return null;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .Find(c => c != null && c.Action == EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase);
            if (clause == null || !controller.TryFindSpellTrap(card, out _)) return null;
            if (ContinuousNegationRuntime.NegatedBy(engine, card) != null) return null;
            if (TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, card)) return null;
            return clause;
        }

        /// <summary>
        /// DuelEngine.Notify / ClearIdleChain: open a queued draw window once play is at a stable point — no chain
        /// building or resolving, no window, pick or declared attack open, not while BeginTurn / EndTurn sequence
        /// the Draw, Standby or End Phase. A window queued in another turn or phase, or whose draw is no longer the
        /// last thing that happened, is dropped. The AI answers inside the call; a human's window stays open.
        /// </summary>
        public static void FlushDrawWindow(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var state)) return;
            if (state.DrawWindows.Count == 0 || state.Flushing) return;
            if (engine.GameOver)
            {
                state.DrawWindows.Clear();
                return;
            }

            state.Flushing = true;
            try
            {
                while (state.DrawWindows.Count > 0 && Stable(engine))
                {
                    // Every window not checked yet is checked now, before any of them opens: windows queued
                    // together (Card Destruction, Morphing Jar — both players drew) answer draws that happened at
                    // the same time, so one player's Appropriate activation does not make the other miss the
                    // timing (in a real chain it would be chained to the first). The turn player answers first.
                    for (var i = state.DrawWindows.Count - 1; i >= 0; i--)
                    {
                        var q = state.DrawWindows[i];
                        if (q.Checked) continue;
                        if (q.Turn != engine.TurnNumber || q.Phase != engine.Phase ||
                            (q.Responder != engine.Player && q.Responder != engine.Opponent))
                        {
                            state.DrawWindows.RemoveAt(i);
                            continue;
                        }

                        if (!DrawStillLast(engine, q))
                        {
                            state.DrawWindows.RemoveAt(i);
                            engine.Log($"{q.Responder.Name}: the draw was not the last thing to happen — " +
                                       "\"when your opponent draws\" cards miss the timing.");
                            continue;
                        }

                        q.Checked = true;
                    }

                    if (state.DrawWindows.Count == 0) break;
                    var at = state.DrawWindows.FindIndex(q => q.Responder == engine.TurnPlayer);
                    var w = state.DrawWindows[at < 0 ? 0 : at];
                    state.DrawWindows.Remove(w);
                    if (w.Turn != engine.TurnNumber || w.Phase != engine.Phase) continue;
                    if (!engine.OpenResponseWindow(w.Responder, ResponseTiming.OpponentDrewOutsideDrawPhase,
                            null, null, null, null, null, CombatAnimTimings.ForOpenGameState()))
                        continue;
                    if (engine.IsHumanControlled(w.Responder))
                    {
                        engine.NotifyPublic();
                        break;
                    }

                    SpellTrapEffects.AiAutoRespond(engine);
                }
            }
            finally
            {
                state.Flushing = false;
            }
        }

        static bool Stable(DuelEngine engine) =>
            !engine.GameOver && !engine.IsResolvingChain && !engine.Chain.IsResolving && engine.Chain.Count == 0 &&
            engine.PendingResponse == null && engine.PendingActivation == null && !engine.HasDeclaredAttack &&
            !engine.OpeningSequenceActive && engine.Phase != DuelPhase.Draw && engine.Phase != DuelPhase.Standby &&
            engine.Phase != DuelPhase.End;

        /// <summary>DuelEngine.BeginTurn: draw windows of the last turn are gone.</summary>
        public static void OnTurnStart(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var state)) return;
            state.DrawWindows.Clear();
        }

        // ─────────────────────────── Painful Choice ───────────────────────────

        /// <summary>
        /// Painful Choice's resolution: start the Deck selection (a human picks; the AI chooses at once), then the
        /// opponent's pick. Nothing happens when the Deck has fewer than Amount cards.
        /// </summary>
        public static void BeginRevealDeckPick(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            var count = clause.Amount > 0 ? clause.Amount : 5;
            if (who.Deck == null || who.Deck.Count < count)
            {
                engine.Log($"{source?.Name}: fewer than {count} cards in {who.Name}'s Deck — no effect.");
                return;
            }

            var state = StateOf(engine);
            state.RevealActivator = who;
            state.RevealSource = source;
            state.Revealed.Clear();
            // One card per Deck entry (copies stay separate), listed by name so the pick does not show Deck order.
            var proxies = who.Deck.Select(engine.CreateCardInstance)
                .OrderBy(c => c.Name).ThenBy(c => c.CardId).ToList();
            if (engine.IsHumanControlled(who))
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingDeckRevealPicks = true,
                    TargetKind = EffectTargetKind.CardInYourDeck,
                    TargetPicksRemaining = count
                };
                pending.LegalTargets.AddRange(proxies);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            state.Revealed.AddRange(proxies
                .OrderByDescending(AiHeuristicPolicy.DiscardPriority)
                .ThenBy(c => c.Name)
                .Take(count));
            OfferOpponentPick(engine);
        }

        /// <summary>TextEffectRuntime.TryResolveTextTarget: a pick in Painful Choice's Deck selection or the opponent's choice.</summary>
        public static bool ContinueRevealPick(DuelEngine engine, PendingActivation p, CardInstance target)
        {
            if (engine == null || p == null || target == null) return false;
            if (!p.LegalTargets.Contains(target)) return false;
            if (p.AwaitingDeckRevealPicks)
            {
                p.LegalTargets.Remove(target);
                p.ChosenTargets.Add(target);
                p.TargetPicksRemaining--;
                engine.Log($"{p.Card?.Name}: selected {target.Name}.");
                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    return true;
                }

                var state = StateOf(engine);
                state.Revealed.Clear();
                state.Revealed.AddRange(p.ChosenTargets);
                engine.ClearPendingActivation();
                OfferOpponentPick(engine);
                return true;
            }

            if (p.AwaitingOpponentPickAmongRevealed)
            {
                engine.ClearPendingActivation();
                FinishReveal(engine, target);
                return true;
            }

            return false;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: the selection belongs to a resolving effect and cannot be skipped — the
        /// remaining picks are made automatically (the first cards listed; the opponent gives the least valuable).
        /// </summary>
        public static bool AutoCompleteRevealPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !(p.AwaitingDeckRevealPicks || p.AwaitingOpponentPickAmongRevealed)) return false;
            if (p.AwaitingOpponentPickAmongRevealed)
            {
                var give = AiHeuristicPolicy.PickLowestValue(p.LegalTargets, null) ?? p.LegalTargets.FirstOrDefault();
                engine.Log($"{p.Card?.Name}: the choice cannot be skipped — {give?.Name} is chosen.");
                return give != null && ContinueRevealPick(engine, p, give);
            }

            engine.Log($"{p.Card?.Name}: the selection cannot be skipped — the remaining cards are chosen.");
            while (engine.PendingActivation == p && p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                if (!ContinueRevealPick(engine, p, p.LegalTargets[0]))
                    break;
            return true;
        }

        /// <summary>Show the selected cards; the opponent (human: a pick; AI: the least valuable) chooses 1.</summary>
        static void OfferOpponentPick(DuelEngine engine)
        {
            var state = StateOf(engine);
            var who = state.RevealActivator;
            var source = state.RevealSource;
            var opp = who != null ? engine.OpponentOf(who) : null;
            if (who == null || opp == null || state.Revealed.Count == 0)
            {
                ClearReveal(state);
                return;
            }

            engine.Log($"{source?.Name}: {who.Name} shows " +
                       $"{string.Join(", ", state.Revealed.Select(c => c.Name))}.");
            if (engine.IsHumanControlled(opp))
            {
                var pending = new PendingActivation
                {
                    Controller = opp,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingOpponentPickAmongRevealed = true,
                    TargetKind = EffectTargetKind.CardInYourDeck
                };
                pending.LegalTargets.AddRange(state.Revealed);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            var give = AiHeuristicPolicy.PickLowestValue(state.Revealed, null) ?? state.Revealed[0];
            FinishReveal(engine, give);
        }

        /// <summary>
        /// The chosen card goes from the Deck to the activating player's hand; if it did, the other shown cards go
        /// from the Deck to the GY (Lua: SendtoHand, then SendtoGrave only if it succeeded). The Deck is shuffled.
        /// </summary>
        static void FinishReveal(DuelEngine engine, CardInstance chosen)
        {
            var state = StateOf(engine);
            var who = state.RevealActivator;
            var source = state.RevealSource;
            var shown = state.Revealed.ToList();
            ClearReveal(state);
            if (who?.Deck == null || chosen == null || !shown.Contains(chosen)) return;

            engine.Log($"{source?.Name}: {engine.OpponentOf(who)?.Name} chooses {chosen.Name}.");
            if (!RemoveOneFromDeck(who, chosen))
            {
                engine.Log($"{source?.Name}: {chosen.Name} is no longer in the Deck — nothing is added.");
                engine.NotifyPublic();
                return;
            }

            who.Hand.Add(chosen);
            engine.Log($"{source?.Name}: {chosen.Name} is added to {who.Name}'s hand.");
            foreach (var c in shown)
            {
                if (c == chosen || engine.GameOver) continue;
                if (!RemoveOneFromDeck(who, c)) continue;
                engine.SendCardToGrave(who, c, sentBy: source);
                engine.Log($"{source?.Name}: {c.Name} is sent to the GY.");
            }

            engine.Rng.Shuffle(who.Deck);
            engine.NotifyPublic();
        }

        static bool RemoveOneFromDeck(DuelistState who, CardInstance card)
        {
            var idx = who.Deck.IndexOf(card.CardId);
            if (idx < 0) return false;
            // PerCopyDeck batch (v163): the shown instance takes the copy's owner (its owner's GY).
            PerCopyDeckRuntime.TakeFromDeck(who, idx, card);
            return true;
        }

        static void ClearReveal(State state)
        {
            state.RevealActivator = null;
            state.RevealSource = null;
            state.Revealed.Clear();
        }

        /// <summary>AI main phase: activate Painful Choice while the Deck has cards to spare.</summary>
        public static bool AiWantsToActivateFromHand(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai?.Deck == null || card?.Def == null || !card.Def.IsSpell) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            var c = prog.ClauseList.Find(x => x != null &&
                                              x.Action == EffectActionKind.RevealDeckCardsOpponentPicksOneToHand);
            return c != null && ai.Deck.Count >= (c.Amount > 0 ? c.Amount : 5) + 5;
        }
    }
}
