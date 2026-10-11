using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellCounterSpenders" runtime (atoms: <see cref="SpellCounterSpendersAtoms"/>).
    /// <list type="bullet">
    /// <item><b>Spell Counter holders.</b> ygopro gives a card a counter permit (EnableCounterPermit / IsCanAddCounter).
    /// There is no permit table here, so it is read from the compiled program: a card holds Spell Counters when its own
    /// text places, spends, counts or moves Spell Counters on itself (<see cref="IsSpellCounterHolder"/>); a printed
    /// "(max. N)" is its cap (<see cref="SpellCounterCap"/>). The permit cannot be negated (it is a property of the card).
    /// Other counters stored in <see cref="CardInstance.Counters"/> (an Equip Spell's Standby decay count) never count:
    /// those cards are not holders.</item>
    /// <item><b>"Remove N Spell Counters on/from your side of the field"</b> (<see cref="EffectClause.RemoveSpellCountersOnYourSide"/>;
    /// Lua Duel.RemoveCounter(tp,1,0,COUNTER_SPELL,N,REASON_COST)): N counters from face-up holders its controller
    /// controls. A Spell/Trap activation pays it through the compiled cost flow (TextEffectRuntime.StartIgnition: a human
    /// with a real choice taps the cards one counter at a time; a forced distribution and the AI pay at once). A Counter
    /// Trap pays it in DuelEngine.TryActivateChainResponse (<see cref="AskChainAnswerCost"/>). A response-window seat
    /// (TextEffectRuntime.SeatResponseOnChain, <see cref="BeginSeatCost"/>) pays a forced distribution at once; a human
    /// with a real choice taps the cards one counter at a time (<see cref="TryPaySeatPick"/>) before the target pick.
    /// Cancel before the activation is complete puts the counters back (<see cref="RefundOnCancel"/>).</item>
    /// <item>Mandragola's FLIP (<see cref="PlaceOnEachHolder"/>), Apprentice Magician's Summon trigger
    /// (<see cref="PlaceOnTarget"/>) and battle-destroyed face-down Deck summon (<see cref="QueueDeckFaceDownSummon"/>),
    /// Armor Exe's Standby upkeep (<see cref="ResolveStandbyUpkeep"/>) and Normal / Flip Summon attack lock
    /// (<see cref="OnNormalOrFlipSummoned"/>), Pitch-Black Power Stone's counter move (FaceUpQuickEffectsRuntime with
    /// <see cref="MoveReady"/> / <see cref="ResolveMove"/>) and self-destruction (<see cref="SweepSelfDestroy"/>).</item>
    /// </list>
    /// </summary>
    public static class SpellCounterSpendersRuntime
    {
        /// <summary>Mega Ton Magical Cannon's target chk: the opponent controls at least 1 card.</summary>
        public const string OpponentControlsACard = "SpellCounterSpenders.OpponentControlsACard";

        /// <summary>Legendary Flame Lord's target chk: a monster other than this card is on the field.</summary>
        public const string AnotherMonsterOnField = "SpellCounterSpenders.AnotherMonsterOnField";

        // ───────────────────────────── holders ─────────────────────────────

        /// <summary>
        /// The card's own text places, spends, counts or moves Spell Counters on itself (the ygopro counter permit).
        /// </summary>
        public static bool IsSpellCounterHolder(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                if (c.Action == EffectActionKind.PlaceSpellCounters ||
                    c.Action == EffectActionKind.GainAtkPerSpellCounter ||
                    c.Action == EffectActionKind.MoveSpellCounterFromThisToTarget ||
                    c.Action == EffectActionKind.DestroyThisWhenLastSpellCounterRemoved ||
                    c.PlaceCounterOnSpellActivate || c.RequiresRemoveSpellCounters > 0 || c.RequiresSpellCounters > 0)
                    return true;
            }

            return false;
        }

        /// <summary>The printed "(max. N)" of the card's own counter placement; 0 = no cap.</summary>
        public static int SpellCounterCap(CardInstance card)
        {
            if (card?.Def == null) return 0;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            var cap = 0;
            if (prog == null) return 0;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == EffectActionKind.PlaceSpellCounters && c.CounterMax > 0)
                    cap = Math.Max(cap, c.CounterMax);
            return cap;
        }

        /// <summary>Every card on <paramref name="side"/>'s field: Monster Zones, Spell &amp; Trap Zones, Field Zone.</summary>
        static IEnumerable<CardInstance> CardsOnField(DuelistState side)
        {
            if (side == null) yield break;
            foreach (var m in side.MonstersOnField())
                if (m != null) yield return m;
            foreach (var st in side.SpellTrapsOnField())
                if (st != null) yield return st;
            var field = side.FieldSpellZone?.Occupant;
            if (field != null) yield return field;
        }

        static bool OnField(DuelEngine engine, CardInstance card) =>
            card != null && engine != null && CardsOnField(engine.Player).Concat(CardsOnField(engine.Opponent))
                .Any(c => ReferenceEquals(c, card));

        /// <summary>Lua IsFaceup() + IsCanAddCounter(COUNTER_SPELL,<paramref name="n"/>) for a card on the field.</summary>
        public static bool CanAddSpellCounter(DuelEngine engine, CardInstance card, int n = 1)
        {
            if (card == null || !card.FaceUp || !OnField(engine, card) || !IsSpellCounterHolder(card)) return false;
            var cap = SpellCounterCap(card);
            return cap <= 0 || card.Counters + n <= cap;
        }

        /// <summary>Adds up to <paramref name="n"/> Spell Counters (never past the card's cap).</summary>
        static void AddSpellCounters(DuelEngine engine, CardInstance card, int n, CardInstance source)
        {
            if (card == null || n <= 0) return;
            var cap = SpellCounterCap(card);
            card.Counters += n;
            if (cap > 0)
            {
                card.SpellCounterMax = cap;
                if (card.Counters > cap) card.Counters = cap;
            }

            Arm(engine, card);
            engine?.Log($"{source?.Name ?? "Effect"}: Spell Counter on {card.Name} → {card.Counters}.");
        }

        /// <summary>Face-up holders on the whole field that can take 1 more Spell Counter.</summary>
        static List<CardInstance> FaceUpHolders(DuelEngine engine, CardInstance except = null)
        {
            var list = new List<CardInstance>();
            if (engine == null) return list;
            foreach (var c in CardsOnField(engine.Player).Concat(CardsOnField(engine.Opponent)))
                if (!ReferenceEquals(c, except) && CanAddSpellCounter(engine, c) && !list.Contains(c))
                    list.Add(c);
            return list;
        }

        // ───────────────────────────── side-wide cost ─────────────────────────────

        /// <summary>Face-up holders <paramref name="who"/> controls that carry at least 1 Spell Counter.</summary>
        public static List<CardInstance> SideCounterSources(DuelEngine engine, DuelistState who) =>
            CardsOnField(who).Where(c => c.FaceUp && c.Counters > 0 && IsSpellCounterHolder(c)).Distinct().ToList();

        public static int SideCounterTotal(DuelEngine engine, DuelistState who) =>
            SideCounterSources(engine, who).Sum(c => c.Counters);

        /// <summary>Lua Duel.IsCanRemoveCounter(tp,1,0,COUNTER_SPELL,N,REASON_COST).</summary>
        public static bool SideCostPayable(DuelEngine engine, DuelistState who, int n) =>
            n <= 0 || SideCounterTotal(engine, who) >= n;

        /// <summary>The player has a real choice: 2+ cards carry counters and they hold more than N between them.</summary>
        public static bool SideChoiceMatters(DuelEngine engine, DuelistState who, int n)
        {
            var sources = SideCounterSources(engine, who);
            return sources.Count > 1 && sources.Sum(c => c.Counters) > n;
        }

        /// <summary>The first clause of <paramref name="card"/>'s program with the side-wide counter cost.</summary>
        public static EffectClause SideCostClause(CardInstance card)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            return prog?.ClauseList.FirstOrDefault(c => c != null && c.RemoveSpellCountersOnYourSide > 0);
        }

        /// <summary>
        /// The AI's (or a forced) payment: the card holding the most counters gives first (a card's only counters,
        /// e.g. Pitch-Black Power Stone's last one, are spent last).
        /// </summary>
        public static bool PayAuto(DuelEngine engine, DuelistState who, CardInstance source, int n)
        {
            if (engine == null || who == null || n <= 0) return true;
            if (!SideCostPayable(engine, who, n)) return false;
            for (var i = 0; i < n; i++)
            {
                var from = SideCounterSources(engine, who).OrderByDescending(c => c.Counters)
                    .ThenBy(c => c.Def != null && c.Def.IsMonster ? 1 : 0).FirstOrDefault();
                if (from == null) return false;
                RemoveOne(engine, from, source);
            }

            return true;
        }

        static void RemoveOne(DuelEngine engine, CardInstance from, CardInstance source)
        {
            Arm(engine, from);
            from.Counters--;
            Record(engine, source, from);
            engine.Log($"Cost: remove 1 Spell Counter from {from.Name} (now {from.Counters}) for {source?.Name}.");
        }

        // ── payments made for an activation that can still be cancelled ──

        sealed class Paid
        {
            public ChainLink Link;
            public readonly List<CardInstance> From = new();
        }

        sealed class State
        {
            public readonly Dictionary<CardInstance, Paid> Paid = new();
            public readonly Dictionary<CardInstance, (int stamp, int turn)> Armed = new();
            public readonly Dictionary<CardInstance, int> MoveUsedTurn = new();
            /// <summary>Pitch-Black Power Stone: counters placed as it was activated (its link, its field stay).</summary>
            public readonly Dictionary<CardInstance, (ChainLink link, int stamp, int n)> PlacedOnActivation = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>
        /// TextEffectRuntime.PayAutoIgnitionCostsCore (the start of every payment of this cost): forget an older record
        /// and tie the next payments to <paramref name="card"/>'s unresolved Chain Link.
        /// </summary>
        public static void BeginPayment(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            var st = StateOf(engine);
            var link = engine.Chain?.Links?.LastOrDefault(l => l != null && ReferenceEquals(l.Card, card) && !l.Resolved);
            st.Paid[card] = new Paid { Link = link };
        }

        static void Record(DuelEngine engine, CardInstance source, CardInstance from)
        {
            if (engine == null || source == null) return;
            var st = StateOf(engine);
            if (!st.Paid.TryGetValue(source, out var paid))
                st.Paid[source] = paid = new Paid();
            paid.From.Add(from);
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: the activation of <paramref name="p"/>'s card is undone, so the counters it
        /// removed (whose cards are still face-up on the field) go back.
        /// </summary>
        public static void RefundOnCancel(DuelEngine engine, PendingActivation p)
        {
            var card = p?.Card;
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return;
            TakeBackActivationCounters(engine, card, st);
            if (!st.Paid.TryGetValue(card, out var paid)) return;
            st.Paid.Remove(card);
            // Only an activation still being made: its own Chain Link is still on the Chain, unresolved.
            if (paid.Link == null || paid.Link.Resolved || engine.Chain?.Links == null ||
                !engine.Chain.Links.Contains(paid.Link))
                return;
            Refund(engine, card, paid);
        }

        static void Refund(DuelEngine engine, CardInstance card, Paid paid)
        {
            var back = 0;
            foreach (var from in paid.From)
            {
                if (from == null || !from.FaceUp || !OnField(engine, from)) continue;
                from.Counters++;
                back++;
            }

            if (back > 0)
                engine.Log($"Cancelled {card.Name} — {back} Spell Counter(s) put back.");
        }

        // ── the compiled cost flow (TextEffectRuntime.StartIgnition) ──

        /// <summary>
        /// TextEffectRuntime.PayOneIgnitionCostCore: the player tapped <paramref name="pick"/> to remove 1 Spell Counter
        /// for <paramref name="source"/>'s cost. When what is left can only be paid one way it is paid at once (the
        /// remaining picks end). False for any other cost pick.
        /// </summary>
        public static bool TryPayOneIgnitionPick(DuelEngine engine, DuelistState who, CardInstance source,
            PendingActivation p, CardInstance pick, out bool ok)
        {
            ok = false;
            if (engine == null || p == null || !p.AwaitingIgnitionCost || p.TargetKind != EffectTargetKind.AnyCardOnField)
                return false;
            var clause = SideCostClause(source);
            if (clause == null) return false;
            if (pick == null || !SideCounterSources(engine, who).Contains(pick)) return true;
            RemoveOne(engine, pick, source);
            var left = p.CostPicksRemaining - 1;
            if (left > 0 && !SideChoiceMatters(engine, who, left))
            {
                if (!PayAuto(engine, who, source, left)) return true;
                p.CostPicksRemaining = 1; // the caller's decrement ends the picks
            }

            p.PromptOverride = SidePickPrompt(Math.Max(0, p.CostPicksRemaining - 1));
            ok = true;
            return true;
        }

        public static string SidePickPrompt(int left) =>
            $"remove 1 Spell Counter from a card you control (cost) — tap the card ({left} more after this one), " +
            "or CANCEL to not activate it.";

        // ── a response-window seat (TextEffectRuntime.SeatResponseOnChain) ──

        /// <summary>
        /// TextEffectRuntime.CanActivate, response windows: a clause with the side-wide counter cost is offered only when
        /// it can be paid. A human's counter pick (<see cref="BeginSeatCost"/>) cannot be combined with another seat cost
        /// pick (a discard, a Deck send, SimpleSpellTraps' sends / Tribute): such an answer is refused (no card has both).
        /// </summary>
        public static bool ResponseSeatCostOk(DuelEngine engine, DuelistState who, IEnumerable<EffectClause> clauses,
            out string reason)
        {
            reason = "OK";
            if (clauses == null) return true;
            var list = clauses.Where(c => c != null).ToList();
            foreach (var c in list)
            {
                if (c.RemoveSpellCountersOnYourSide <= 0) continue;
                if (!SideCostPayable(engine, who, c.RemoveSpellCountersOnYourSide))
                {
                    reason = $"Needs {c.RemoveSpellCountersOnYourSide} Spell Counter(s) on your side of the field.";
                    return false;
                }

                if (SimpleSpellTrapsRuntime.ResponseCostClause(list) != null ||
                    (engine.IsHumanControlled(who) &&
                     SideChoiceMatters(engine, who, c.RemoveSpellCountersOnYourSide) &&
                     list.Any(x => TextEffectRuntime.HasDiscardCost(x) || x.RequiresSendRaceFromDeck)))
                {
                    reason = "Its costs need more than one pick — not available in this window.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// TextEffectRuntime.SeatResponseOnChain, once the link is seated: pay the side-wide counter cost of the seated
        /// clauses. The AI, a forced distribution, or a seat with another cost pick pays at once (false). A human with a
        /// real choice gets a cost pick (true): <see cref="TryPaySeatPick"/> removes 1 counter per tap, then the target
        /// pick (if any) follows (TextEffectRuntime.ContinueResponseDiscardPick); Cancel undoes the activation and puts the
        /// counters back (SpellTrapEffects.CancelPending, <see cref="RefundOnCancel"/>).
        /// </summary>
        public static bool BeginSeatCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            IEnumerable<EffectClause> clauses, bool human, ResponseTiming timing, CardInstance summoned,
            EffectClause targeted, int paidLp)
        {
            var c = clauses?.FirstOrDefault(x => x != null && x.RemoveSpellCountersOnYourSide > 0);
            if (c == null || engine == null || who == null || card == null) return false;
            var n = c.RemoveSpellCountersOnYourSide;
            BeginPayment(engine, card);
            if (!human || !SideChoiceMatters(engine, who, n))
            {
                PayAuto(engine, who, card, n);
                return false;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                TargetKind = EffectTargetKind.AnyCardOnField,
                UsesTextProgram = true,
                AwaitingIgnitionCost = true,
                CostPicksRemaining = n,
                ChainActivation = true,
                SeatTiming = timing,
                SeatSummoned = summoned,
                SeatTargetClause = targeted,
                PaidLpCost = paidLp,
                PromptOverride = SidePickPrompt(n - 1)
            };
            pending.LegalTargets.AddRange(SideCounterSources(engine, who));
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.ContinueResponseDiscardPick: the player tapped <paramref name="pick"/> to remove 1 Spell
        /// Counter for a seated response's cost (<see cref="BeginSeatCost"/>). Once what is left can only be paid one way it
        /// is paid at once. False when <paramref name="p"/> is not this cost's pick.
        /// </summary>
        public static bool TryPaySeatPick(DuelEngine engine, PendingActivation p, CardInstance pick, out bool ok)
        {
            ok = false;
            if (engine == null || p == null || !p.AwaitingIgnitionCost || p.SeatTiming == ResponseTiming.None ||
                p.TargetKind != EffectTargetKind.AnyCardOnField || SideCostClause(p.Card) == null)
                return false;
            var who = p.Controller;
            if (pick == null || pick.Counters <= 0 || !SideCounterSources(engine, who).Contains(pick)) return true;
            RemoveOne(engine, pick, p.Card);
            p.CostPicksRemaining--;
            if (p.CostPicksRemaining > 0 && !SideChoiceMatters(engine, who, p.CostPicksRemaining))
            {
                if (!PayAuto(engine, who, p.Card, p.CostPicksRemaining)) return true;
                p.CostPicksRemaining = 0;
            }

            p.LegalTargets.Clear();
            if (p.CostPicksRemaining > 0)
                p.LegalTargets.AddRange(SideCounterSources(engine, who));
            p.PromptOverride = SidePickPrompt(Math.Max(0, p.CostPicksRemaining - 1));
            ok = true;
            return true;
        }

        // ── a Counter Trap's chain answer (DuelEngine.TryActivateChainResponse) ──

        /// <summary>
        /// Anti-Spell: remove N Spell Counters from your side, then <paramref name="seat"/> seats the answer. A human with
        /// a real choice taps the cards one counter at a time (Cancel puts them back: not activated, the window stays
        /// open); otherwise the counters go at once. True while a pick is open or once the link is seated.
        /// </summary>
        internal static bool AskChainAnswerCost(DuelEngine engine, DuelistState who, CardInstance card, int n,
            bool human, Func<bool> stillLegal, Func<bool> seat)
        {
            if (engine == null || who == null || card == null || seat == null) return false;
            if (!SideCostPayable(engine, who, n)) return false;
            BeginPayment(engine, card);
            if (!human || !SideChoiceMatters(engine, who, n))
            {
                PayAuto(engine, who, card, n);
                return SeatOrRefund(engine, card, seat);
            }

            var answering = engine.PendingResponse;
            var activated = true;
            void Next(int left)
            {
                if (left <= 0 || !SideChoiceMatters(engine, who, left))
                {
                    if (left > 0) PayAuto(engine, who, card, left);
                    activated = SeatOrRefund(engine, card, seat);
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
                {
                    Candidates = () => SideCounterSources(engine, who),
                    Kind = EffectTargetKind.AnyCardOnField,
                    Prompt = $"remove 1 Spell Counter from a card you control to activate {card.Name} (cost; {left} " +
                             "left), or CANCEL to not activate it.",
                    AiChoose = list => list.OrderByDescending(x => x.Counters).FirstOrDefault(),
                    DuringActivation = true,
                    Finish = chosen =>
                    {
                        if (chosen == null || chosen.Counters <= 0 || !ReferenceEquals(engine.PendingResponse, answering) ||
                            (stillLegal != null && !stillLegal()))
                        {
                            CancelChainAnswer(engine, card);
                            activated = false;
                            return;
                        }

                        RemoveOne(engine, chosen, card);
                        Next(left - 1);
                    },
                    Cancel = () => CancelChainAnswer(engine, card)
                });
            }

            Next(n);
            return activated;
        }

        static bool SeatOrRefund(DuelEngine engine, CardInstance card, Func<bool> seat)
        {
            var ok = seat();
            var st = StateOf(engine);
            if (st.Paid.TryGetValue(card, out var paid))
            {
                st.Paid.Remove(card);
                if (!ok) Refund(engine, card, paid);
            }

            if (!ok) engine.NotifyPublic();
            return ok;
        }

        static void CancelChainAnswer(DuelEngine engine, CardInstance card)
        {
            var st = StateOf(engine);
            if (st.Paid.TryGetValue(card, out var paid))
            {
                st.Paid.Remove(card);
                Refund(engine, card, paid);
            }

            engine.Log($"{card.Name} is not activated.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain: this batch's conditions.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            switch (c?.ActivationCondition)
            {
                case OpponentControlsACard:
                    ok = CardsOnField(engine.OpponentOf(who)).Any();
                    reason = "Your opponent controls no card.";
                    return true;
                case AnotherMonsterOnField:
                    ok = engine.Player.MonstersOnField().Concat(engine.Opponent.MonstersOnField())
                        .Any(m => m != null && !ReferenceEquals(m, card));
                    reason = "Needs another monster on the field.";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── effects ─────────────────────────────

        /// <summary>Magical Plant Mandragola: 1 Spell Counter on each face-up holder on the field that can take it.</summary>
        public static void PlaceOnEachHolder(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 1;
            var holders = FaceUpHolders(engine).Where(c => CanAddSpellCounter(engine, c, n)).ToList();
            if (holders.Count == 0)
            {
                engine.Log($"{source?.Name}: no face-up card on the field can take a Spell Counter.");
                return;
            }

            foreach (var c in holders)
                AddSpellCounters(engine, c, n, source);
        }

        /// <summary>The face-up holders a "target 1 [other] face-up card … you can place a Spell Counter on" may pick.</summary>
        public static List<CardInstance> CollectHolderTargets(DuelEngine engine, EffectClause clause, CardInstance source)
        {
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 1;
            return FaceUpHolders(engine, clause != null && clause.ExceptThisCard ? source : null)
                .Where(c => CanAddSpellCounter(engine, c, n))
                // EquipLocksAndLeaveField batch: Metalsilver Armor (Pitch-Black's pick does not go through CollectTargets).
                .Where(c => !EquipLocksAndLeaveFieldRuntime.ShieldedFromSource(engine, c, source)).ToList();
        }

        /// <summary>
        /// AI: its own holder first (the one that gains ATK per counter, then the one closest to its next spend), never
        /// the opponent's unless nothing else is left (a mandatory trigger).
        /// </summary>
        public static CardInstance AiPickHolder(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var own = targets.Where(t => ReferenceEquals(engine.ControllerOf(t), who)).ToList();
            var pool = own.Count > 0 ? own : targets;
            return pool.OrderByDescending(t => HasAction(t, EffectActionKind.GainAtkPerSpellCounter) ? 1 : 0)
                .ThenByDescending(t => t.Counters).First();
        }

        static bool HasAction(CardInstance card, EffectActionKind action)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog != null && prog.ClauseList.Exists(c => c != null && c.Action == action);
        }

        /// <summary>Apprentice Magician: 1 Spell Counter on the target if it is still face-up on the field and can take it.</summary>
        public static void PlaceOnTarget(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            var n = clause != null && clause.Amount > 0 ? clause.Amount : 1;
            if (target == null)
            {
                engine.Log($"{source?.Name}: no target — no Spell Counter is placed.");
                return;
            }

            if (!CanAddSpellCounter(engine, target, n))
            {
                engine.Log($"{source?.Name}: {target.Name} can no longer take a Spell Counter.");
                return;
            }

            AddSpellCounters(engine, target, n, source);
        }

        /// <summary>
        /// Apprentice Magician destroyed by battle (TextEffectRuntime.TryResolveSentToGy): its optional trigger goes on
        /// the next simultaneous-trigger Chain (SimultaneousTriggersRuntime.QueueTrigger). It activates only with a free
        /// Monster Zone and a Level N or lower monster of the Type in the Deck (Lua s.target chk).
        /// </summary>
        public static void QueueDeckFaceDownSummon(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0 || DeckCandidates(engine, who, source, clause).Count == 0)
            {
                engine.Log($"{source?.Name}: no Level {clause.Amount} or lower {clause.RaceFilter} monster can be Special " +
                           "Summoned from the Deck — the effect does not activate.");
                return;
            }

            SimultaneousTriggersRuntime.QueueTrigger(engine, who, source, optional: true,
                () => ResolveDeckFaceDownSummon(engine, who, source, clause));
        }

        static List<CardInstance> DeckCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || who?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in who.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null) continue;
                if (!def.IsMonster || def.IsExtraDeck || def.level > clause.Amount) continue;
                if (!string.IsNullOrEmpty(clause.RaceFilter) && !ClassicEraTemplates.RaceMatches(def.race, clause.RaceFilter))
                    continue;
                var inst = engine.CreateCardInstance(id);
                // Face-down Defense: the face-up-only rules (Rivalry of Warlords, the unique rule) do not apply.
                if (!SpecialSummonLegality.CanBeSpecialSummoned(engine, who, inst, source, SummonOrigin.Deck,
                        faceUp: false)) continue;
                list.Add(inst);
            }

            return list;
        }

        static void ResolveDeckFaceDownSummon(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || engine.GameOver) return;
            if (engine.FirstEmpty(who.MonsterZones) < 0)
            {
                engine.Log($"{source?.Name}: no free Monster Zone — nothing is Special Summoned.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => DeckCandidates(engine, who, source, clause),
                Kind = EffectTargetKind.MonsterInYourDeckToSummon,
                Prompt = $"you can Special Summon 1 Level {clause.Amount} or lower {clause.RaceFilter} monster from your " +
                         "Deck in face-down Defense Position — tap it, or CANCEL to not Summon.",
                AiChoose = list => list.OrderByDescending(c => c.Def?.def ?? 0).ThenByDescending(c => c.Def?.atk ?? 0)
                    .FirstOrDefault(),
                AskWithOneCandidate = true,
                Finish = pick => SummonFaceDown(engine, who, source, pick),
                Cancel = () => engine.Log($"{source?.Name}: {who.Name} does not Special Summon.")
            });
        }

        static void SummonFaceDown(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            if (pick == null)
            {
                engine.Log($"{source?.Name}: nothing is Special Summoned.");
                return;
            }

            var index = who.Deck.IndexOf(pick.CardId);
            if (index < 0)
            {
                engine.Log($"{source?.Name}: {pick.Name} is no longer in the Deck.");
                return;
            }

            var copy = PerCopyDeckRuntime.TakeFromDeck(who, index, pick);
            if (!engine.SpecialSummonToField(who, pick, BattlePosition.Defense, false))
            {
                who.Deck.Insert(index, pick.CardId, copy);
                engine.Log($"{source?.Name}: the Special Summon of {pick.Name} failed.");
            }
            else
                // Lua Duel.ConfirmCards(1-tp,g): the opponent is shown the face-down monster.
                engine.Log($"{source?.Name}: {who.Name} Special Summons {pick.Name} from the Deck in face-down Defense " +
                           $"Position (shown to {engine.OpponentOf(who)?.Name}).");

            engine.Rng.Shuffle(who.Deck);
            engine.NotifyPublic();
        }

        // ── Armor Exe ──

        /// <summary>
        /// DuelEngine Normal Summon / Flip Summon: a face-up card whose "cannot attack the turn it is Special Summoned"
        /// clause also names Normal and Flip Summons cannot attack this turn (ToonsRuntime.OnSpecialSummoned does the
        /// Special Summon).
        /// </summary>
        public static void OnNormalOrFlipSummoned(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || !card.FaceUp) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.ClauseList.Exists(c => c != null &&
                    c.Action == EffectActionKind.CannotAttackTurnSpecialSummoned &&
                    c.CannotAttackAlsoTurnNormalOrFlipSummoned))
                return;
            card.CannotAttackDuringTurnNumber = engine.TurnNumber;
        }

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor, each Standby Phase: remove 1 Spell Counter from your side or destroy
        /// this card. A human with a counter to remove is asked (tap a card = remove from it, Cancel = destroy this card;
        /// the Standby Phase waits); the AI keeps it while it can pay.
        /// </summary>
        public static void ResolveStandbyUpkeep(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause)
        {
            if (engine == null || who == null || card == null) return;
            var stamp = card.FieldEntryStamp;
            bool StillThere() => card.FaceUp && card.FieldEntryStamp == stamp && ReferenceEquals(engine.ControllerOf(card), who);
            if (!SideCostPayable(engine, who, 1))
            {
                engine.Log($"{card.Name}: no Spell Counter on {who.Name}'s side of the field — destroy this card.");
                DestroyForUpkeep(engine, who, card);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
            {
                Candidates = () => SideCounterSources(engine, who),
                Kind = EffectTargetKind.AnyCardOnField,
                Prompt = $"{card.Name}: remove 1 Spell Counter from a card you control to keep it — tap the card, or " +
                         "CANCEL to destroy it.",
                AiChoose = list => list.OrderByDescending(x => x.Counters).FirstOrDefault(),
                AskWithOneCandidate = true,
                HoldsStandby = true,
                Finish = chosen =>
                {
                    if (!StillThere()) return;
                    if (chosen == null || chosen.Counters <= 0 || !SideCounterSources(engine, who).Contains(chosen))
                    {
                        engine.Log($"{card.Name}: {who.Name} does not remove a Spell Counter — destroy this card.");
                        DestroyForUpkeep(engine, who, card);
                        return;
                    }

                    Arm(engine, chosen);
                    chosen.Counters--;
                    engine.Log($"Cost: {who.Name} removes 1 Spell Counter from {chosen.Name} (now {chosen.Counters}) " +
                               $"to keep {card.Name}.");
                    engine.NotifyPublic();
                },
                Cancel = () =>
                {
                    if (!StillThere()) return;
                    engine.Log($"{card.Name}: {who.Name} does not remove a Spell Counter — destroy this card.");
                    DestroyForUpkeep(engine, who, card);
                }
            });
        }

        static void DestroyForUpkeep(DuelEngine engine, DuelistState who, CardInstance card)
        {
            // Lua Duel.Destroy(c, REASON_COST): destroyed by its own maintenance.
            TextEffectRuntime.DestroyCard(engine, card, card, false, who);
            engine.NotifyPublic();
        }

        // ── Pitch-Black Power Stone ──

        /// <summary>
        /// Every Spell/Trap activation site (SpellTrapEffects' own activation, TextEffectRuntime.SeatResponseOnChain and
        /// ChainFreeChainLink), once <paramref name="card"/> is face-up with its Chain Link <paramref name="link"/>: "Activate
        /// this card by placing N Spell Counters on it" (<see cref="EffectClause.PlaceSpellCountersAsActivated"/>; Lua
        /// AddCounter in the target step) places them now, before anyone can respond. It is not an effect, so a negation
        /// (Royal Decree) does not stop it. The link's resolution does not place them again (<see cref="PlacedOnActivation"/>).
        /// </summary>
        public static void PlaceOnActivation(DuelEngine engine, CardInstance card, ChainLink link)
        {
            if (engine == null || card?.Def == null || link == null || !ReferenceEquals(link.Card, card)) return;
            var c = CompiledEffectCache.GetOrCompile(card.Def)?.ClausesFor(EffectTiming.Activate)?.FirstOrDefault(x =>
                x != null && x.PlaceSpellCountersAsActivated && x.Action == EffectActionKind.PlaceSpellCounters);
            if (c == null) return;
            var st = StateOf(engine);
            if (st.PlacedOnActivation.TryGetValue(card, out var done) && ReferenceEquals(done.link, link)) return;
            var before = card.Counters;
            AddSpellCounters(engine, card, c.Amount > 0 ? c.Amount : 1, card);
            st.PlacedOnActivation[card] = (link, card.FieldEntryStamp, card.Counters - before);
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause (PlaceSpellCounters): <paramref name="card"/>'s counters were placed as it was
        /// activated during this stay on the field, so its resolving activation places none (the record is used up).
        /// </summary>
        public static bool PlacedOnActivation(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st) ||
                !st.PlacedOnActivation.TryGetValue(card, out var done))
                return false;
            st.PlacedOnActivation.Remove(card);
            return done.stamp == card.FieldEntryStamp;
        }

        /// <summary>The activation is undone (<see cref="RefundOnCancel"/>): the counters it placed are taken back.</summary>
        static void TakeBackActivationCounters(DuelEngine engine, CardInstance card, State st)
        {
            if (!st.PlacedOnActivation.TryGetValue(card, out var done)) return;
            if (done.link == null || done.link.Resolved || engine.Chain?.Links == null ||
                !engine.Chain.Links.Contains(done.link))
                return;
            st.PlacedOnActivation.Remove(card);
            st.Armed.Remove(card);
            card.Counters = Math.Max(0, card.Counters - done.n);
            engine.Log($"Cancelled {card.Name} — the {done.n} Spell Counter(s) placed to activate it are removed.");
        }

        /// <summary>The card had a Spell Counter during this stay on the field (its "last counter" can be removed).</summary>
        static void Arm(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            StateOf(engine).Armed[card] = (card.FieldEntryStamp, 0);
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: a face-up, non-negated card with "When the last Spell Counter(s) is removed
        /// from this card, destroy it" that had counters during this stay on the field and has none left is destroyed
        /// (its own effect; ygopro EFFECT_SELF_DESTROY). Its activation still on the Chain does not count
        /// (<see cref="CardInstance.IsNegated"/> covers <see cref="CardInstance.ActivationUnresolvedOnChain"/>).
        /// </summary>
        public static void SweepSelfDestroy(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var st = StateOf(engine);
            var doomed = new List<(DuelistState side, CardInstance card)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            foreach (var c in CardsOnField(side))
            {
                if (c?.Def == null || !c.FaceUp || !HasAction(c, EffectActionKind.DestroyThisWhenLastSpellCounterRemoved))
                    continue;
                if (c.Counters > 0)
                {
                    st.Armed[c] = (c.FieldEntryStamp, 0);
                    continue;
                }

                if (c.IsNegated || !st.Armed.TryGetValue(c, out var armed) || armed.stamp != c.FieldEntryStamp) continue;
                doomed.Add((side, c));
            }

            foreach (var (side, c) in doomed)
            {
                st.Armed.Remove(c);
                if (engine.ControllerOf(c) == null) continue;
                engine.Log($"{c.Name}: its last Spell Counter was removed — it is destroyed.");
                TextEffectRuntime.DestroyCard(engine, c, c, false, side);
            }
        }

        /// <summary>FaceUpQuickEffectsRuntime.ClauseReady: Pitch-Black Power Stone's move can be activated now.</summary>
        public static bool MoveReady(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            out string reason)
        {
            if (clause.ControllerTurnOnly && engine.TurnPlayer != who)
            {
                reason = "Only during your turn.";
                return false;
            }

            if (clause.OncePerTurn && MoveUsedThisTurn(engine, card))
            {
                reason = "Once per turn.";
                return false;
            }

            if (card.Counters < 1)
            {
                reason = "No Spell Counter on this card.";
                return false;
            }

            if (CollectHolderTargets(engine, clause, card).Count == 0)
            {
                reason = "No other face-up card on the field can take a Spell Counter.";
                return false;
            }

            reason = "OK";
            return true;
        }

        static bool MoveUsedThisTurn(DuelEngine engine, CardInstance card) =>
            card != null && s_state.TryGetValue(engine, out var st) && st.MoveUsedTurn.TryGetValue(card, out var t) &&
            t == engine.TurnNumber;

        /// <summary>The once-per-turn use is spent as the effect is activated (Lua SetCountLimit).</summary>
        public static void NoteMoveUsed(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            StateOf(engine).MoveUsedTurn[card] = engine.TurnNumber;
        }

        /// <summary>
        /// Pitch-Black Power Stone resolves (Lua s.operation): this card still on the field, the target still face-up on
        /// the field and able to take a counter, and this card still holding one: move 1 Spell Counter.
        /// </summary>
        public static void ResolveMove(DuelEngine engine, DuelistState who, CardInstance card, CardInstance target)
        {
            var name = card?.Name ?? "Effect";
            if (card == null || !card.FaceUp || !OnField(engine, card))
            {
                engine.Log($"{name} is no longer on the field — its effect does nothing.");
                return;
            }

            if (target == null || !CanAddSpellCounter(engine, target))
            {
                engine.Log($"{name}: the target can no longer take a Spell Counter.");
                return;
            }

            if (card.Counters < 1)
            {
                engine.Log($"{name}: no Spell Counter left to remove.");
                return;
            }

            Arm(engine, card);
            card.Counters--;
            engine.Log($"{name}: removes 1 Spell Counter from itself (now {card.Counters}).");
            AddSpellCounters(engine, target, 1, card);
        }

        /// <summary>AI: move a counter in its own Main Phase onto its own holder (one that uses counters).</summary>
        public static bool AiWantsMove(DuelEngine engine, DuelistState ai, CardInstance card, EffectClause clause) =>
            engine.TurnPlayer == ai && engine.InMainPhase && engine.PendingResponse == null &&
            CollectHolderTargets(engine, clause, card).Any(t => ReferenceEquals(engine.ControllerOf(t), ai));

        /// <summary>AI: activate a Set Pitch-Black Power Stone when it has a holder of its own to feed.</summary>
        public static bool AiWantsActivateSetStone(DuelEngine engine, DuelistState ai, CardInstance card) =>
            CardsOnField(ai).Any(c => !ReferenceEquals(c, card) && c.FaceUp && IsSpellCounterHolder(c));
    }
}
