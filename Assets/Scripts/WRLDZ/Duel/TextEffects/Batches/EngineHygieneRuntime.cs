using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════
    //  EngineHygiene batch (v160): engine subsystems shared by many cards.
    //  1. SpecialSummonLegality — one engine-aware "can this copy be Special Summoned from X" check (+ summon-cost hook).
    //  2. DiscardEvents — every discard from a hand raises one event (Forced Requisition, Super Rejuvenation).
    //  3. ReleaseEvents — every Tribute (release) is recorded (Super Rejuvenation).
    //  4. EngineHygieneRuntime — the banish-from-GY hand procedure, Ekibyo Drakmord's End Phase countdown, End Phase hooks.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Where a Special Summon takes the monster from.</summary>
    public enum SummonOrigin
    {
        Unknown = 0,
        Hand = 1,
        Deck = 2,
        Graveyard = 3,
        Banished = 4,
        ExtraDeck = 5,
        Field = 6
    }

    /// <summary>
    /// EngineHygiene batch (v160): the one engine-aware "can this copy be Special Summoned by an effect" check (Lua
    /// IsCanBeSpecialSummoned(e, 0, tp, false, false)). It knows every gate <see cref="DuelEngine.SpecialSummonToField(DuelistState, CardInstance, BattlePosition, bool)"/>
    /// applies, so a pick never offers a Summon the engine then refuses:
    /// <list type="bullet">
    /// <item>printed gates: hard Nomi, a monster summoned only by its own procedure ("can only be Special Summoned by",
    /// Gate Guardian), a "while X is face-up" gate and the named-card Semi-Nomi (Wall Shadow)
    /// (<see cref="FlipMassEffectsRuntime.CanBeSpecialSummonedByThis"/>);</item>
    /// <item>"Must first be Special Summoned …" (Semi-Nomi: the Toons, Gigantes, Spirit of Flames, Inferno), Ritual and
    /// Extra Deck monsters: never from the hand or Deck; from the GY / banishment only once properly summoned
    /// (<see cref="CardInstance.ProcedureCompleted"/>); an Extra Deck monster with "Must be Fusion Summoned" never from the
    /// Extra Deck;</item>
    /// <item>"cannot be Special Summoned from the GY", Necrovalley on GY cards;</item>
    /// <item>RuleLocks: Jowgen the Spiritualist / Jam Breeding Machine (<see cref="RuleLocksRuntime.SpecialSummonLocked(DuelEngine, DuelistState, CardInstance)"/>),
    /// Chain Energy's hand toll (from the hand, when asked);</item>
    /// <item>per-card summon costs (<see cref="RegisterSummonCost"/>; ygopro EFFECT_SPSUMMON_COST, e.g. Tyrant Dragon's
    /// "cannot be Special Summoned from the GY, unless you Tribute 1 Dragon monster"), paid by
    /// <see cref="PaySummonCosts"/> when the Summon happens (DuelEngine.SpecialSummonToField);</item>
    /// <item>optionally a free Monster Zone.</item>
    /// </list>
    /// </summary>
    public static class SpecialSummonLegality
    {
        static readonly Regex RxNomiOwnWayOnly = new(@"\bcan only be Special Summoned\b|\bMust be Special Summoned\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSemiNomi = new(@"\bMust first be Special Summoned\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Summoning conditions of an Extra Deck monster still in the Extra Deck: "Must be Fusion Summoned." (Lua
        /// aux.fuslimit), "Must first be Special Summoned (from your Extra Deck) by banishing …" and Nomi.
        /// </summary>
        static readonly Regex RxExtraDeckSummonCondition = new(
            @"\bMust (?:first )?be (?:Fusion|Special) Summoned\b|\bcan only be (?:Fusion |Special )?Summoned\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A per-card Special Summon cost (ygopro EFFECT_SPSUMMON_COST): <see cref="AppliesTo"/> picks the cards and
        /// origins it governs, <see cref="CanPay"/> is asked by <see cref="CanBeSpecialSummoned"/>, and <see cref="Pay"/>
        /// runs as the Summon happens (false refuses the Summon).
        /// </summary>
        public sealed class SummonCost
        {
            public string Name;
            public Func<CardInstance, SummonOrigin, bool> AppliesTo;
            public Func<DuelEngine, DuelistState, CardInstance, SummonOrigin, bool> CanPay;
            public Func<DuelEngine, DuelistState, CardInstance, SummonOrigin, bool> Pay;
        }

        static readonly List<SummonCost> s_costs = new();

        /// <summary>The summon costs printed on cards (SummonPromptsAndCosts batch, v163: Tyrant Dragon's GY Tribute).</summary>
        static SpecialSummonLegality() => RegisterSummonCost(SummonPromptsAndCostsRuntime.GyTributeSummonCost);

        /// <summary>Register a summon cost once (a second registration with the same <see cref="SummonCost.Name"/> is ignored).</summary>
        public static void RegisterSummonCost(SummonCost cost)
        {
            if (cost?.AppliesTo == null) return;
            lock (s_costs)
            {
                if (s_costs.Any(c => c.Name == cost.Name)) return;
                s_costs.Add(cost);
            }
        }

        static List<SummonCost> CostsFor(CardInstance card, SummonOrigin from)
        {
            lock (s_costs)
                return s_costs.Count == 0
                    ? null
                    : s_costs.Where(c => c.AppliesTo(card, from)).ToList();
        }

        /// <summary>Every applicable summon cost can be paid by <paramref name="who"/>.</summary>
        public static bool CanPaySummonCosts(DuelEngine engine, DuelistState who, CardInstance card, SummonOrigin from)
        {
            var costs = CostsFor(card, from);
            return costs == null || costs.All(c => c.CanPay == null || c.CanPay(engine, who, card, from));
        }

        /// <summary>Pay every applicable summon cost now (DuelEngine.SpecialSummonToField). False refuses the Summon.</summary>
        public static bool PaySummonCosts(DuelEngine engine, DuelistState who, CardInstance card, SummonOrigin from)
        {
            var costs = CostsFor(card, from);
            if (costs == null || costs.Count == 0) return true;
            if (!costs.All(c => c.CanPay == null || c.CanPay(engine, who, card, from))) return false;
            foreach (var c in costs)
                if (c.Pay != null && !c.Pay(engine, who, card, from))
                    return false;
            return true;
        }

        static bool IsRitualMonster(CardDef def) =>
            def?.type != null && def.type.IndexOf("Ritual", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// <paramref name="card"/> (in <paramref name="from"/>) can be Special Summoned to <paramref name="who"/>'s field
        /// by an effect of <paramref name="source"/>. <paramref name="checkHandToll"/>: also ask Chain Energy's toll for a
        /// hand card (callers that already charged it pass false). <paramref name="requireZone"/>: a Monster Zone is free.
        /// </summary>
        public static bool CanBeSpecialSummoned(DuelEngine engine, DuelistState who, CardInstance card,
            CardInstance source, SummonOrigin from, bool checkHandToll = true, bool requireZone = false)
        {
            if (card?.Def == null || !card.Def.IsMonster || who == null) return false;
            engine ??= who.Engine;
            var text = card.OfficialText ?? "";
            if (!FlipMassEffectsRuntime.CanBeSpecialSummonedByThis(who, card)) return false;
            // TargetNegationsAndHandQuickEffects batch (v163): a named-card procedure ("by Tributing 1 "Great Dezard"",
            // Fushioh Richie) is a Semi-Nomi: only that card summons it from the hand or Deck, a properly summoned copy
            // may come back (SemiNomiMayBeRevived below).
            var namedProcedure = TributeSummonsRuntime.ProcedureCardName(card.Def) != null;
            if (RxNomiOwnWayOnly.IsMatch(text) &&
                !(namedProcedure && card.ProcedureCompleted &&
                  (from == SummonOrigin.Graveyard || from == SummonOrigin.Banished)))
                return false;
            var ritual = IsRitualMonster(card.Def);
            switch (from)
            {
                case SummonOrigin.Hand:
                case SummonOrigin.Deck:
                    // A copy in the hand or Deck was never properly summoned.
                    if (ritual || card.Def.IsExtraDeck || RxSemiNomi.IsMatch(text) || namedProcedure ||
                        ToonsRuntime.DefNeedsOwnProcedure(card.Def))
                        return false;
                    break;
                case SummonOrigin.ExtraDeck:
                    if (RxExtraDeckSummonCondition.IsMatch(text)) return false;
                    break;
                case SummonOrigin.Graveyard:
                case SummonOrigin.Banished:
                    if ((card.Def.IsExtraDeck || ritual) && !card.ProcedureCompleted) return false;
                    if (from == SummonOrigin.Graveyard && PsctGrammar.CannotBeSpecialSummonedFromGraveyard(text))
                        return false;
                    if (!TributeSummonsRuntime.SemiNomiMayBeRevived(card)) return false;
                    if (ToonsRuntime.BlocksSpecialSummon(card)) return false;
                    if (from == SummonOrigin.Graveyard && engine != null &&
                        ContinuousProtections.GyMovementLocked(engine, source, card))
                        return false;
                    break;
            }

            if (engine != null)
            {
                if (RuleLocksRuntime.SpecialSummonLocked(engine, who, card)) return false;
                // SummonTriggersAndTributes batch (v163): "You can only control 1 face-up X" (a face-up Special Summon).
                if (SummonTriggersAndTributesRuntime.UniqueBlocksFaceUp(engine, who, card, out _)) return false;
                if (checkHandToll && from == SummonOrigin.Hand && RuleLocksRuntime.HandSpecialSummonUnaffordable(engine, who))
                    return false;
                if (requireZone && engine.FirstEmpty(who.MonsterZones) < 0) return false;
            }

            return CanPaySummonCosts(engine, who, card, from);
        }
    }

    // ─────────────────────────────────────────── Discards ───────────────────────────────────────────

    /// <summary>Why cards were discarded (informational; consumers read <see cref="DiscardEvent.Cards"/>).</summary>
    public enum DiscardCause
    {
        Unknown = 0,
        Cost = 1,
        Effect = 2,
        HandSizeRule = 3
    }

    /// <summary>One discard event (ygopro EVENT_DISCARD): the cards one player discarded from their hand together.</summary>
    public sealed class DiscardEvent
    {
        public DuelistState Player;
        public readonly List<CardInstance> Cards = new();
        public DiscardCause Cause;
        /// <summary>The player whose card effect made the discard, when known (null for costs and the hand-size rule).</summary>
        public DuelistState CausingController;
        public int Turn;
        public DuelPhase Phase;
    }

    /// <summary>
    /// EngineHygiene batch (v160): the central discard event. Every discard path — costs and effects, random and chosen,
    /// the hand-size rule, Card Destruction / Morphing Jar, Graceful Charity, the Kuriboh-type hand costs — calls
    /// <see cref="Record"/> (MonsterEffects.DiscardFromHandByInstance does it for most of them). Cards recorded while a
    /// scope is open (<see cref="Begin"/> / <see cref="End"/>, also opened by DiscardTriggersRuntime.BeginScope) form one
    /// event per player; outside a scope each record is its own event. A refunded cost (the activation was cancelled)
    /// is taken back with <see cref="Retract"/>.
    /// <para>Listeners: Super Rejuvenation counts Dragon monsters (<see cref="SuperRejuvenation"/>); Forced Requisition
    /// (<see cref="EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard"/>): (1) its activation window
    /// <see cref="ResponseTiming.YouDiscardedFromHand"/>, offered once play is idle to the player who discarded
    /// (DuelEngine.OfferDiscardedWindow; a discard of an earlier turn or phase was missed); (2) a face-up copy whose
    /// controller discarded queues its mandatory trigger: once play is idle the opponent discards that many cards of their
    /// choice (all of them when they hold fewer; a human chooses through the Graceful Charity discard pick), like the
    /// engine's other discard triggers (DiscardTriggersRuntime) — inline, not as a new Chain the opponent can answer.</para>
    /// </summary>
    public static class DiscardEvents
    {
        /// <summary>ActivationCondition (Forced Requisition): in the "you discarded from your hand" window.</summary>
        public const string YouDiscardedFromHand = "YouDiscardedFromHand";

        sealed class Rec
        {
            public DuelistState Owner;
            public CardInstance Card;
            public DiscardCause Cause;
            public DuelistState By;
        }

        sealed class Trigger
        {
            public CardInstance Source;
            public DuelistState Controller;
            public List<CardInstance> Cards;
        }

        sealed class State
        {
            public int Depth;
            public readonly List<Rec> Open = new();
            /// <summary>Events Forced Requisition's activation can still answer.</summary>
            public readonly List<DiscardEvent> Window = new();
            public readonly List<DiscardEvent> Offered = new();
            public readonly List<Trigger> Triggers = new();
            public bool Firing;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        public static void Begin(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).Depth++;
        }

        public static void End(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Depth > 0) st.Depth--;
            if (st.Depth == 0) Close(engine, st);
        }

        /// <summary>
        /// <paramref name="card"/> was just discarded from <paramref name="owner"/>'s hand (it is now in a GY, or banished
        /// by Banisher of the Light). <paramref name="engine"/> may be null (then <see cref="DuelistState.Engine"/>).
        /// </summary>
        public static void Record(DuelEngine engine, DuelistState owner, CardInstance card,
            DiscardCause cause = DiscardCause.Unknown, DuelistState by = null)
        {
            engine ??= owner?.Engine;
            if (engine == null || owner == null || card == null) return;
            // BattleDestroyedGyRecovery (v162): the GY-entry stamp of a discard (REASON_DISCARD, + COST / EFFECT).
            GyEntryStamps.AddReason(engine, card,
                GyEntryReason.Discard | (cause == DiscardCause.Cost ? GyEntryReason.Cost
                    : cause == DiscardCause.Effect ? GyEntryReason.Effect : GyEntryReason.None), by);
            var st = StateOf(engine);
            st.Open.Add(new Rec { Owner = owner, Card = card, Cause = cause, By = by });
            if (st.Depth == 0) Close(engine, st);
        }

        /// <summary>DiscardTriggersRuntime.NoteDiscard: the discard of <paramref name="card"/> was made by an effect of <paramref name="by"/>'s card.</summary>
        public static void NoteCause(DuelEngine engine, CardInstance card, DuelistState by)
        {
            // BattleDestroyedGyRecovery (v162): a discard made by an effect of by's card (its GY-entry reason player).
            GyEntryStamps.AddReason(engine, card, GyEntryReason.Effect, by);
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return;
            foreach (var r in st.Open)
                if (r.Card == card)
                {
                    r.Cause = DiscardCause.Effect;
                    r.By = by;
                }

            foreach (var ev in st.Window)
                if (ev.Cards.Contains(card))
                {
                    ev.Cause = DiscardCause.Effect;
                    ev.CausingController = by;
                }
        }

        static void Close(DuelEngine engine, State st)
        {
            if (st.Open.Count == 0) return;
            var recs = st.Open.ToList();
            st.Open.Clear();
            foreach (var group in recs.GroupBy(r => r.Owner))
            {
                var ev = new DiscardEvent
                {
                    Player = group.Key,
                    Cause = group.First().Cause,
                    CausingController = group.First().By,
                    Turn = engine.TurnNumber,
                    Phase = engine.Phase
                };
                ev.Cards.AddRange(group.Select(r => r.Card).Distinct());
                Raise(engine, st, ev);
            }
        }

        static void Raise(DuelEngine engine, State st, DiscardEvent ev)
        {
            SuperRejuvenation.OnDiscarded(engine, ev);
            // Forced Requisition's activation window (the player who discarded).
            st.Window.Add(ev);
            // Forced Requisition's trigger: every face-up copy the discarding player controls.
            foreach (var fr in ForcedRequisitionsOf(engine, ev.Player))
                st.Triggers.Add(new Trigger { Source = fr, Controller = ev.Player, Cards = ev.Cards.ToList() });
        }

        /// <summary>A refunded discard cost (the activation was cancelled): the discard never happened.</summary>
        public static void Retract(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Open.RemoveAll(r => r.Card == card);
            foreach (var ev in st.Window) ev.Cards.Remove(card);
            st.Window.RemoveAll(ev => ev.Cards.Count == 0);
            foreach (var ev in st.Offered) ev.Cards.Remove(card);
            st.Offered.RemoveAll(ev => ev.Cards.Count == 0);
            foreach (var t in st.Triggers) t.Cards.Remove(card);
            st.Triggers.RemoveAll(t => t.Cards.Count == 0);
            SuperRejuvenation.Retract(engine, card);
        }

        // ───────────────────── Forced Requisition ─────────────────────

        static bool IsForcedRequisition(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard);
        }

        /// <summary>Face-up copies <paramref name="who"/> controls whose effect applies (not negated).</summary>
        static IEnumerable<CardInstance> ForcedRequisitionsOf(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) yield break;
            foreach (var st in who.SpellTrapsOnField())
                if (st != null && st.FaceUp && !st.IsNegated && !engine.Chain.Links.Any(l => l.Card == st && !l.Resolved) &&
                    IsForcedRequisition(st))
                    yield return st;
        }

        /// <summary>
        /// Once play is idle (DuelEngine.Notify): each queued Forced Requisition trigger whose card is still face-up and
        /// applying makes its controller's opponent discard. A human victim's choice opens a pick and pauses the queue.
        /// </summary>
        public static void FireQueuedTriggers(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Firing || st.Triggers.Count == 0) return;
            st.Firing = true;
            try
            {
                while (st.Triggers.Count > 0)
                {
                    if (engine.GameOver)
                    {
                        st.Triggers.Clear();
                        return;
                    }

                    if (engine.PendingActivation != null || engine.PendingResponse != null) return;
                    var t = st.Triggers[0];
                    st.Triggers.RemoveAt(0);
                    var fr = t.Source;
                    if (!ReferenceEquals(engine.ControllerOf(fr), t.Controller) ||
                        !t.Controller.TryFindSpellTrap(fr, out _) || !fr.FaceUp || fr.IsNegated)
                        continue;
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, fr)) continue;
                    var victim = engine.OpponentOf(t.Controller);
                    var n = t.Cards.Count;
                    if (victim?.Hand == null || n <= 0) continue;
                    if (victim.Hand.Count == 0)
                    {
                        engine.Log($"{fr.Name}: {victim.Name} has no card in hand to discard.");
                        continue;
                    }

                    engine.Log($"{fr.Name}: {t.Controller.Name} discarded {n} card(s) — {victim.Name} must discard " +
                               $"{Math.Min(n, victim.Hand.Count)}.");
                    DrawThenDiscardRuntime.StartChosenDiscard(engine, victim, fr, n, t.Controller);
                }
            }
            finally
            {
                st.Firing = false;
            }
        }

        public static bool HasWindowRecords(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.Window.Count > 0;

        public static void DropWindowRecords(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Window.Clear();
            st.Offered.Clear();
        }

        /// <summary>
        /// The idle window for <paramref name="who"/> (DuelEngine.OfferDiscardedWindow): the fresh discards it answers.
        /// Discards of an earlier turn or phase were missed and are dropped. False when there is nothing to answer.
        /// </summary>
        public static bool BeginDiscardWindow(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            var st = StateOf(engine);
            st.Offered.Clear();
            st.Window.RemoveAll(ev => ev.Turn != engine.TurnNumber || ev.Phase != engine.Phase);
            st.Offered.AddRange(st.Window.Where(ev => ev.Player == who));
            return st.Offered.Count > 0;
        }

        /// <summary>The window closed (passed, answered or never opened): the discards it offered are gone.</summary>
        public static void EndDiscardWindow(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Window.RemoveAll(ev => st.Offered.Contains(ev));
            st.Offered.Clear();
        }

        /// <summary>Activation condition <see cref="YouDiscardedFromHand"/>: <paramref name="who"/>'s discard window is open.</summary>
        public static bool DiscardWindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only when you discard from your hand.";
            var pr = engine?.PendingResponse;
            if (pr == null || who == null || pr.Responder != who || pr.Timing != ResponseTiming.YouDiscardedFromHand ||
                !s_state.TryGetValue(engine, out var st))
                return false;
            if (!st.Offered.Exists(ev => ev.Player == who && ev.Cards.Count > 0)) return false;
            reason = "OK";
            return true;
        }

        /// <summary>The program answers the <see cref="ResponseTiming.YouDiscardedFromHand"/> window (Forced Requisition).</summary>
        public static bool AnswersDiscardWindow(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.Activate && c.ActivationCondition == YouDiscardedFromHand);
    }

    // ─────────────────────────────────────────── Tributes ───────────────────────────────────────────

    /// <summary>
    /// EngineHygiene batch (v160): every Tribute (ygopro EVENT_RELEASE) — Tribute Summons, Tribute costs, Ritual
    /// Tributes, procedures (the Toons, Gate Guardian, the Cocoon moths), effect Tributes — calls <see cref="Record"/>
    /// right before the card leaves, so its location and its Type on the field are still known. Super Rejuvenation
    /// counts the Dragons.
    /// </summary>
    public static class ReleaseEvents
    {
        /// <param name="tributer">The player who Tributes it (the reason player), when known.</param>
        public static void Record(DuelEngine engine, CardInstance card, DuelistState tributer)
        {
            if (engine == null || card?.Def == null) return;
            DuelistState holder = null;
            var fromField = false;
            var fromHand = false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindMonster(card, out _))
                {
                    holder = side;
                    fromField = true;
                    break;
                }

                if (side.Hand != null && side.Hand.Contains(card))
                {
                    holder = side;
                    fromHand = true;
                    break;
                }
            }

            if (holder == null) return;
            SuperRejuvenation.OnReleased(engine, card, fromField, fromHand, holder, tributer ?? holder);
        }
    }

    /// <summary>
    /// EngineHygiene batch (v160) — Super Rejuvenation (c27770341). Per-turn counts per player (Lua global s[0]/s[1], kept
    /// for the whole turn, also before the card is activated): a Dragon Tributed from a Monster Zone (its Type on the
    /// field) counts for the player who Tributed it; a Dragon monster discarded or Tributed from the hand (its original
    /// Type) counts for the hand's owner. The activation registers a draw for that turn's End Phase
    /// (<see cref="OnEndPhase"/>, from DuelEngine.EndTurn) of the count at that time; resolved during the End Phase itself,
    /// it draws once the Chain has resolved.
    /// </summary>
    public static class SuperRejuvenation
    {
        sealed class Reg
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Turn;
            public bool Done;
        }

        sealed class State
        {
            public int Turn = int.MinValue;
            public readonly Dictionary<DuelistState, int> Count = new();
            public readonly Dictionary<CardInstance, DuelistState> CountedFor = new();
            public readonly List<Reg> Regs = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine)
        {
            var st = s_state.GetValue(engine, _ => new State());
            if (st.Turn != engine.TurnNumber)
            {
                st.Turn = engine.TurnNumber;
                st.Count.Clear();
                st.CountedFor.Clear();
                st.Regs.RemoveAll(r => r.Turn != engine.TurnNumber);
            }

            return st;
        }

        static bool IsDragon(string race) => ClassicEraTemplates.RaceMatches(race, "Dragon");

        static void Add(DuelEngine engine, CardInstance card, DuelistState who)
        {
            if (who == null) return;
            var st = StateOf(engine);
            st.Count.TryGetValue(who, out var n);
            st.Count[who] = n + 1;
            st.CountedFor[card] = who;
        }

        /// <summary>The Dragons <paramref name="who"/> discarded or Tributed this turn.</summary>
        public static int CountFor(DuelEngine engine, DuelistState who) =>
            engine == null || who == null ? 0 : StateOf(engine).Count.TryGetValue(who, out var n) ? n : 0;

        internal static void OnDiscarded(DuelEngine engine, DiscardEvent ev)
        {
            if (engine == null || ev?.Player == null) return;
            foreach (var c in ev.Cards)
                if (c?.Def != null && c.Def.IsMonster && IsDragon(c.Def.race))
                    Add(engine, c, ev.Player);
        }

        internal static void OnReleased(DuelEngine engine, CardInstance card, bool fromField, bool fromHand,
            DuelistState holder, DuelistState tributer)
        {
            if (engine == null || card?.Def == null) return;
            if (fromField && IsDragon(TypeDeclarationRuntime.CurrentRace(card)))
                Add(engine, card, tributer);
            else if (fromHand && card.Def.IsMonster && IsDragon(card.Def.race))
                Add(engine, card, holder);
        }

        internal static void Retract(DuelEngine engine, CardInstance card)
        {
            var st = StateOf(engine);
            if (!st.CountedFor.TryGetValue(card, out var who)) return;
            st.CountedFor.Remove(card);
            if (st.Count.TryGetValue(who, out var n) && n > 0) st.Count[who] = n - 1;
        }

        /// <summary>ApplyClause arm: register this turn's End Phase draw for <paramref name="who"/>.</summary>
        public static void Register(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            StateOf(engine).Regs.Add(new Reg { Who = who, Source = source, Turn = engine.TurnNumber });
            engine.Log($"{source?.Name ?? "Super Rejuvenation"}: {who.Name} draws during this End Phase " +
                       "(1 per Dragon discarded or Tributed this turn).");
            // Already in the End Phase: the draw comes once the Chain has resolved (AfterChainResolved).
            if (engine.Phase == DuelPhase.End && !engine.IsResolvingChain && !engine.Chain.IsResolving)
                DrawDue(engine);
        }

        /// <summary>DuelEngine.EndTurn (End Phase) and after a Chain resolved during the End Phase.</summary>
        internal static void DrawDue(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out _)) return;
            var st = StateOf(engine);
            foreach (var r in st.Regs.Where(r => !r.Done && r.Turn == engine.TurnNumber).ToList())
            {
                r.Done = true;
                var n = CountFor(engine, r.Who);
                if (n <= 0)
                {
                    engine.Log($"{r.Source?.Name ?? "Super Rejuvenation"}: no Dragon was discarded or Tributed — no draw.");
                    continue;
                }

                if (ContinuousProtections.BlocksDrawOutsideDrawPhase(engine, r.Who))
                {
                    engine.Log($"{r.Source?.Name ?? "Super Rejuvenation"}: {r.Who.Name} cannot draw.");
                    continue;
                }

                engine.Log($"{r.Source?.Name ?? "Super Rejuvenation"}: {r.Who.Name} draws {n} card(s).");
                engine.Draw(r.Who, n);
                if (engine.GameOver) return;
            }
        }
    }

    // ─────────────────────────────────────────── Batch runtime ───────────────────────────────────────────

    /// <summary>
    /// EngineHygiene batch (v160) runtime: the "banish N ATTRIBUTE monster(s) from your GY" hand procedure (paid for
    /// <see cref="ToonsRuntime"/>), Ekibyo Drakmord's End Phase countdown and the End Phase / after-Chain hooks.
    /// </summary>
    public static class EngineHygieneRuntime
    {
        // ───────────────────── Hand procedure: banish N ATTRIBUTE monsters from your GY ─────────────────────

        sealed class ProcedurePick
        {
            public CardInstance Card;
            public EffectClause Proc;
            public readonly List<CardInstance> Chosen = new();
            /// <summary>The UI's chosen column / position (DuelUI clears it once the command returns).</summary>
            public int Zone = -1;
            public BattlePosition? Position;
        }

        static readonly ConditionalWeakTable<PendingActivation, ProcedurePick> s_picks = new();

        /// <summary>The GY monsters <paramref name="who"/> can banish for <paramref name="proc"/> (printed Attribute in the GY).</summary>
        public static List<CardInstance> ProcedureBanishCandidates(DuelEngine engine, DuelistState who,
            CardInstance card, EffectClause proc)
        {
            var list = new List<CardInstance>();
            if (engine == null || who?.Graveyard == null || proc == null) return list;
            // ForcedActivationAndSubstitution batch (v163): Spirit Elimination — face-up monsters of that Attribute
            // `who` controls stand in for the GY monsters (aux.SpElimFilter(c,true)).
            if (SpiritEliminationRuntime.Affects(engine, who))
                return SpiritEliminationRuntime.Substitutes(engine, who, card,
                    !string.IsNullOrEmpty(proc.AttributeFilter),
                    m => string.IsNullOrEmpty(proc.AttributeFilter) ||
                         string.Equals(EquipFamilyRuntime.CurrentAttribute(m), proc.AttributeFilter,
                             StringComparison.OrdinalIgnoreCase));
            if (ContinuousProtections.CannotBanishFromGraveyard(engine, who) || RuleLocksRuntime.BanishFromGyLocked(engine, who))
                return list;
            foreach (var g in who.Graveyard)
            {
                if (g?.Def == null || !g.Def.IsMonster) continue;
                if (!string.IsNullOrEmpty(proc.AttributeFilter) &&
                    !string.Equals(g.Def.attribute, proc.AttributeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ContinuousProtections.GyMovementLocked(engine, card, g)) continue;
                list.Add(g);
            }

            return list;
        }

        /// <summary>ToonsRuntime.CanSummonByHandProcedure: enough monsters to banish.</summary>
        public static bool CanPayProcedureBanish(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause proc, out string reason)
        {
            reason = "OK";
            if (proc == null || proc.BanishFromGyCount <= 0) return true;
            if (ProcedureBanishCandidates(engine, who, card, proc).Count >= proc.BanishFromGyCount) return true;
            reason = $"Need {proc.BanishFromGyCount} {proc.AttributeFilter} monster(s) in your GY to banish.";
            return false;
        }

        /// <summary>
        /// ToonsRuntime.TrySummonByHandProcedure for a banish procedure (legality already checked): the AI, or a player
        /// with no choice, banishes at once and Special Summons; a player with a choice picks the monsters first
        /// (PendingActivation, Cancel = nothing happens).
        /// </summary>
        public static bool SummonByBanishProcedure(DuelEngine engine, DuelistState who, CardInstance card, EffectClause proc)
        {
            var need = proc.BanishFromGyCount;
            var candidates = ProcedureBanishCandidates(engine, who, card, proc);
            if (candidates.Count < need) return false;
            if (candidates.Count == need || !engine.IsHumanControlled(who))
            {
                var picks = candidates.Count == need
                    ? candidates
                    : candidates.OrderBy(g => g.Def.atk).ThenBy(g => g.Level).Take(need).ToList();
                return BanishAndSummon(engine, who, card, picks);
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = true,
                TargetKind = EffectTargetKind.BanishFromYourGy,
                IsMonsterEffect = true,
                UsesTextProgram = true,
                TargetPicksRemaining = need,
                PromptOverride = $"banish {need} {proc.AttributeFilter} monster(s) from your GY to Special Summon {card.Name} " +
                                 "(Cancel to not Summon)."
            };
            pending.LegalTargets.AddRange(candidates);
            s_picks.Add(pending, new ProcedurePick
            {
                Card = card, Proc = proc, Zone = engine.PendingSpecialSummonZone,
                Position = engine.PendingSpecialSummonPosition
            });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static bool BanishAndSummon(DuelEngine engine, DuelistState who, CardInstance card, List<CardInstance> picks)
        {
            foreach (var g in picks)
            {
                engine.BanishCard(who, g, by: who);
                if (!who.Banished.Contains(g) && !engine.OpponentOf(who).Banished.Contains(g))
                {
                    engine.Log($"{card.Name}: {g.Name} could not be banished — no Special Summon.");
                    return false;
                }
            }

            engine.Log($"{who.Name} Special Summons {card.Name} from the hand by banishing " +
                       $"{string.Join(", ", picks.Select(p => p.Name))} from the GY.");
            return engine.SpecialSummonByOwnHandProcedure(who, card, null);
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook: a procedure banish pick.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            if (target == null || !p.LegalTargets.Contains(target)) return true;
            p.LegalTargets.Remove(target);
            pick.Chosen.Add(target);
            p.TargetPicksRemaining--;
            if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
            {
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                ok = true;
                return true;
            }

            s_picks.Remove(p);
            var who = p.Controller;
            engine.ClearPendingActivation();
            if (!ToonsRuntime.CanSummonByHandProcedure(engine, who, pick.Card, out var reason))
            {
                engine.Log($"Cannot Special Summon {pick.Card.Name}: {reason}");
                engine.NotifyPublic();
                return true;
            }

            if (pick.Zone >= 0 || pick.Position != null)
                engine.SetPendingSpecialSummonPlacement(pick.Zone, pick.Position);
            ok = BanishAndSummon(engine, who, pick.Card, pick.Chosen);
            engine.ClearPendingSpecialSummonPlacement();
            engine.NotifyPublic();
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the procedure is not performed; nothing was paid.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            engine.Log($"{pick.Card?.Name}: not Special Summoned.");
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────── Ekibyo Drakmord ─────────────────────

        sealed class Countdown
        {
            public int Stamp = int.MinValue;
            public CardInstance Host;
            public int Count;
        }

        static readonly ConditionalWeakTable<CardInstance, Countdown> s_countdowns = new();

        static EffectClause CountdownClause(CardInstance st)
        {
            if (st?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(st);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClauseList.FirstOrDefault(c =>
                c != null && c.Action == EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis);
        }

        /// <summary>The End Phases counted so far for <paramref name="equip"/> (tests).</summary>
        public static int CountdownOf(CardInstance equip) =>
            equip != null && s_countdowns.TryGetValue(equip, out var cd) ? cd.Count : 0;

        /// <summary>
        /// Each End Phase: every face-up equip with the countdown counts it when the equipped monster's controller is the
        /// turn player (Lua e3, EFFECT_FLAG_CANNOT_DISABLE: also while negated). The End Phase the count reaches N, the
        /// equipped monster is destroyed unless the equip is negated (e4), and if it was destroyed the equip returns to its
        /// owner's hand from the field or the GY (e5; Necrovalley keeps it in the GY).
        /// </summary>
        static void TickCountdowns(DuelEngine engine)
        {
            var due = new List<(DuelistState side, CardInstance equip, CardInstance host)>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st == null || !st.FaceUp || st.EquippedTo == null) continue;
                    var clause = CountdownClause(st);
                    if (clause == null) continue;
                    var host = st.EquippedTo;
                    var cd = s_countdowns.GetValue(st, _ => new Countdown());
                    // Reset only by a new activation (Lua eqpop1 SetTurnCounter(0)): an equip moved to another monster
                    // (Tailor of the Fickle, Collected Power: Duel.Equip) keeps its count (v160 integration audit).
                    if (cd.Stamp != st.FaceUpStamp)
                    {
                        cd.Stamp = st.FaceUpStamp;
                        cd.Host = host;
                        cd.Count = 0;
                    }

                    var hostController = engine.ControllerOf(host);
                    if (hostController == null || !ReferenceEquals(hostController, engine.TurnPlayer)) continue;
                    cd.Count++;
                    engine.Log($"{st.Name}: End Phase {cd.Count} of {Math.Max(1, clause.Amount)} for {host.Name}'s controller.");
                    if (cd.Count == Math.Max(1, clause.Amount) && !st.IsNegated)
                        due.Add((side, st, host));
                }
            }

            foreach (var (side, equip, host) in due)
            {
                if (engine.GameOver) return;
                var hostController = engine.ControllerOf(host);
                if (hostController == null || !hostController.TryFindMonster(host, out _)) continue;
                engine.Log($"{equip.Name}: {host.Name} is destroyed.");
                engine.DestroyMonsterPublic(hostController, host, equip);
                if (hostController.TryFindMonster(host, out _)) continue; // not destroyed
                ReturnToOwnersHand(engine, equip);
            }
        }

        static void ReturnToOwnersHand(DuelEngine engine, CardInstance card)
        {
            if (engine.ControllerOf(card) != null)
            {
                engine.ReturnCardToHand(card);
                return;
            }

            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.Graveyard == null || !side.Graveyard.Contains(card)) continue;
                if (ContinuousProtections.GyMovementLocked(engine, card, card))
                {
                    engine.Log($"{card.Name} cannot leave the GY (Necrovalley).");
                    return;
                }

                side.Graveyard.Remove(card);
                card.FaceUp = true;
                side.Hand.Add(card);
                engine.Log($"{card.Name} returns to {side.Name}'s hand.");
                return;
            }
        }

        // ───────────────────── Hooks ─────────────────────

        /// <summary>DuelEngine.EndTurn, after the End Phase triggers: Ekibyo Drakmord's countdowns, Super Rejuvenation's draws.</summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            TickCountdowns(engine);
            if (engine.GameOver) return;
            SuperRejuvenation.DrawDue(engine);
        }

        /// <summary>DuelEngine.ResolveChainStack, once the whole Chain has resolved.</summary>
        public static void AfterChainResolved(DuelEngine engine)
        {
            if (engine == null || engine.GameOver) return;
            if (engine.Phase == DuelPhase.End)
                SuperRejuvenation.DrawDue(engine);
        }
    }
}
