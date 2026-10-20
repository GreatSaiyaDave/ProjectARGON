using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ContinuousTrapsAndSpiritSupport" (v161) runtime. Reads compiled programs only — no card-id branches.
    /// A card's effect applies only while it is face-up in a Spell &amp; Trap Zone, not negated, fully compiled, and its
    /// own activation has resolved (ygopro STATUS_EFFECT_ENABLED: no unresolved Chain Link of it).
    /// <list type="bullet">
    /// <item><b>Fatal Abacus</b> (<see cref="EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy"/>): every
    /// monster that leaves a Monster Zone for a GY while an Abacus is active is recorded
    /// (<see cref="NoteMonsterSentFromField"/>, the Michizure hook of DuelEngine.SendCardToGrave / DestroyMonster / a
    /// negated Summon). The next GY checkpoint (StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: every board refresh,
    /// before each Chain Link resolves and before each clause applies) inflicts Amount per monster to each GY's owner,
    /// both players at once, for each Abacus still active (one that left the field together with them does not apply).
    /// Tokens never reach the GY.</item>
    /// <item><b>Life Absorbing Machine</b> (<see cref="EffectActionKind.GainHalfLpCostsPaidLastTurn"/>): every LP cost
    /// the turn player pays during their own turn (DuelEngine.PayLifePoints / PayLifePointCost, Chain Energy's toll) is
    /// recorded (<see cref="NoteLpCostPaid"/>); each turn start keeps the turn player's previous turn
    /// (<see cref="OnTurnBegin"/>). In the controller's Standby Phase it gains half of each of those payments, rounded
    /// up (Lua math.ceil(ev/2) per EVENT_PAY_LPCOST). Damage and "lose LP" are not costs.</item>
    /// <item><b>Ominous Fortunetelling</b> (<see cref="EffectActionKind.CallTypeOfRandomOpponentHandCardBurn"/>): in the
    /// controller's Standby Phase a human calls Monster / Spell / Trap through 3 declaration tokens in the ordinary card
    /// picker (the Standby Phase waits: <see cref="HoldsStandby"/>); the AI calls the type most common among the
    /// opponent's cards. A card is then picked at random (engine.Rng.PickIndex) from the opponent's hand, revealed in the
    /// log, and a right call inflicts Amount damage.</item>
    /// <item><b>Fengsheng Mirror</b> (<see cref="EffectActionKind.LookAtOpponentHandDiscardChosenSpirit"/>): at
    /// resolution the opponent's hand is listed in the log (the look); a Spirit monster there is discarded, the
    /// activator's choice among several (CardInOppHand pick; the AI takes the highest Level).</item>
    /// <item><b>Spirit's Invitation</b> (<see cref="EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand"/>):
    /// DuelEngine.ReturnCardToHand records a face-up Spirit that left a Monster Zone for its owner's hand
    /// (<see cref="NoteMonsterReturnedToHand"/>); TextEffectRuntime.NotifyMonstersReturnedToOwnerHand (after a batched
    /// mass return, once) fires <see cref="FireSpiritReturnTriggers"/>: for each active Invitation whose controller got
    /// a Spirit back, the opponent selects 1 monster they control (a human through a YourMonster pick, the AI its least
    /// valuable one) and it returns to its owner's hand. Its 500 LP upkeep is the shared pay-or-destroy clause.</item>
    /// <item><b>Spiritual Energy Settle Machine</b>: <see cref="SpiritReturnPrevented"/> gates the Spirit End Phase
    /// return (SpiritsAndMiscRuntime.SpiritReturnDue); <see cref="ResolveEndPhaseUpkeep"/> is its maintenance (paid even
    /// while negated): discard 1 card of the controller's choice (a human picks, Cancel = do not discard) or the card is
    /// destroyed; <see cref="SweepLeftMachines"/> (each board refresh, and right after the upkeep) returns every face-up
    /// Spirit monster to its owner's hand once an active Machine has left the field.</item>
    /// </list>
    /// A human's choice is offered one at a time (<see cref="OfferQueuedPick"/>): a second choice of this batch waits
    /// behind the open one; while another batch's prompt is open the choice is made the way the AI would make it.
    /// </summary>
    public static class ContinuousTrapsAndSpiritSupportRuntime
    {
        /// <summary><see cref="CardDef.type"/> of a Monster / Spell / Trap call token (Ominous Fortunetelling).</summary>
        public const string CardTypeTokenKind = "Declaration (Card Type)";

        internal static readonly string[] TypeNames = { "Monster", "Spell", "Trap" };

        enum PickKind
        {
            FortuneCall,
            InvitationReturn,
            SettleDiscard,
            FengshengDiscard,
            /// <summary>FlipDownAndPeek batch (v162): a generic "choose 1 of these cards" (<see cref="AskCardPick"/>).</summary>
            CardPick
        }

        sealed class Pick
        {
            public PickKind Kind;
            /// <summary>The player who chooses.</summary>
            public DuelistState Who;
            public CardInstance Source;
            public EffectClause Clause;
            public int EntryStamp;
            /// <summary>Opened in the chooser's own live Standby Phase, which waits for the answer.</summary>
            public bool StandbyHold;
            // FlipDownAndPeek batch (v162), PickKind.CardPick: the cards to choose from (read again as the prompt opens),
            // the picker kind and prompt, the AI's choice (also a Cancel's: the choice is mandatory) and what follows.
            public Func<List<CardInstance>> Candidates;
            public EffectTargetKind TargetKind;
            public string PromptText;
            public Func<List<CardInstance>, CardInstance> AiChoose;
            public Action<CardInstance> Finish;
            /// <summary>
            /// PhaseTimersAndDeckTop batch (v163), <see cref="AskOptionalCardPick"/>: a "you can" choice — a lone card is
            /// still asked, Cancel declines (Finish gets null) and the AI's choice may be null.
            /// </summary>
            public bool Optional;
            // EngineResiduals (v163), CardPickRequest: Cancel's own answer (null: mandatory), a lone card still asked,
            // the chain hold, and a pick made while an activation is being made (no Chain / phase resume afterwards).
            public Action Cancel;
            public bool AskWithOneCandidate;
            public bool HoldsChain = true;
            public bool DuringActivation;
            /// <summary>BattleDestroyedTriggers batch: <see cref="CardPickRequest.AsksSpecialSummonPlacement"/>.</summary>
            public bool AsksPlacement;
        }

        sealed class AbacusSend
        {
            public DuelistState GyOwner;
            public readonly List<(CardInstance abacus, int entry)> Watchers = new();
        }

        sealed class EngineState
        {
            public readonly List<AbacusSend> AbacusSends = new();
            public bool AbacusBusy;
            public readonly Dictionary<DuelistState, List<int>> CostsThisTurn = new();
            public readonly Dictionary<DuelistState, List<int>> CostsLastTurn = new();
            public readonly List<(DuelistState handOwner, CardInstance spirit)> SpiritReturns = new();
            public List<(CardInstance machine, int entry, DuelistState side)> LiveMachines = new();
            public bool SweepBusy;
            public readonly List<Pick> Queue = new();
            /// <summary>Greed: the cards each player drew by a card effect this turn, per source Trap.</summary>
            public readonly Dictionary<CardInstance, Dictionary<DuelistState, int>> GreedDrawn = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, Pick> s_open = new();

        static EngineState State(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        // ───────────────────────────── Greed ─────────────────────────────

        /// <summary>
        /// DuelEngine.Draw, for a draw by a card effect (Greed's EVENT_DRAW with REASON_EFFECT). Every face-up,
        /// non-negated Greed on the field records the count for the player who drew.
        /// </summary>
        public static void NoteEffectDraw(DuelEngine engine, DuelistState who, int drawn)
        {
            if (engine == null || who == null || drawn <= 0 || engine.GameOver) return;
            var st = State(engine);
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var greedy in side.SpellTrapsOnField().ToList())
                {
                    if (greedy?.Def == null || !greedy.FaceUp || greedy.IsNegated) continue;
                    var prog = CompiledEffectCache.GetOrCompile(greedy);
                    if (prog == null || !prog.FullyCompiled) continue;
                    if (!prog.ClauseList.Exists(c => c != null &&
                            c.Action == EffectActionKind.TrackEffectDrawForEndPhaseDamage))
                        continue;
                    if (!st.GreedDrawn.TryGetValue(greedy, out var per))
                        st.GreedDrawn[greedy] = per = new Dictionary<DuelistState, int>();
                    per.TryGetValue(who, out var had);
                    per[who] = had + drawn;
                }
            }
        }

        /// <summary>Greed's recorded count for <paramref name="who"/> this turn, or 0.</summary>
        public static int EffectDrawsThisTurn(DuelEngine engine, CardInstance source, DuelistState who)
        {
            if (engine == null || who == null ||
                !s_state.TryGetValue(engine, out var st) || !st.GreedDrawn.TryGetValue(source, out var per))
                return 0;
            return per.TryGetValue(who, out var n) ? n : 0;
        }

        /// <summary>
        /// Greed's tracker for the End Phase trigger: the player whose draws the clause is being read for. The
        /// source stays on the field, so the record is cleared when it leaves or the turn ends
        /// (<see cref="ClearTurnScopedState"/>) — the Lua flags reset with RESETS_STANDARD_PHASE_END.
        /// </summary>
        public static DuelistState GreedWhoseDraws(EffectClause clause, DuelistState controller) =>
            clause?.Side == EffectSide.Opponent ? controller?.Engine?.OpponentOf(controller) : controller;

        /// <summary>End Phase: Greed's turn-scoped draw records end (RESETS_STANDARD_PHASE_END).</summary>
        public static void ClearTurnScopedState(DuelEngine engine)
        {
            if (engine == null) return;
            if (s_state.TryGetValue(engine, out var st)) st.GreedDrawn.Clear();
        }

        // ───────────────────────────── helpers ─────────────────────────────

        /// <summary>A Spirit monster card (printed type).</summary>
        public static bool IsSpiritMonster(CardDef def) =>
            def != null && def.IsMonster && def.type != null &&
            def.type.IndexOf("Spirit", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool InAnyPile(DuelEngine engine, CardInstance card)
        {
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                if (side.Graveyard.Contains(card) || side.Banished.Contains(card)) return true;
            }

            return false;
        }

        static bool ActivationResolved(DuelEngine engine, CardInstance card) =>
            engine?.Chain == null ||
            !engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, card) && !l.Resolved);

        /// <summary>
        /// The clause of <paramref name="st"/> when its effect applies now: face-up in <paramref name="side"/>'s Spell &amp;
        /// Trap Zone (in no GY or banished pile), not negated, fully compiled, its activation resolved.
        /// </summary>
        static EffectClause LiveClause(DuelEngine engine, DuelistState side, CardInstance st, EffectTiming timing,
            EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _) || InAnyPile(engine, st)) return null;
            var prog = CompiledEffectCache.GetOrCompile(st.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(timing)?.FirstOrDefault(c => c != null && c.Action == action);
            if (clause == null || !ActivationResolved(engine, st)) return null;
            return clause;
        }

        static List<(DuelistState side, CardInstance card, EffectClause clause)> LiveCards(DuelEngine engine,
            EffectTiming timing, EffectActionKind action)
        {
            var list = new List<(DuelistState, CardInstance, EffectClause)>();
            foreach (var side in new[] { engine?.Player, engine?.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField().ToList())
                {
                    var c = LiveClause(engine, side, st, timing, action);
                    if (c != null) list.Add((side, st, c));
                }
            }

            return list;
        }

        /// <summary>
        /// TextEffectRuntime.IsContinuousTrapPlacementOnly: a trigger of the face-up card (not part of its activation),
        /// so a Continuous Trap with only these clauses is a free-chain placement like Royal Decree.
        /// </summary>
        public static bool IsFaceUpTriggerOnly(EffectClause c) =>
            c != null && c.Timing == EffectTiming.StandbyPhase &&
            (c.Action == EffectActionKind.GainHalfLpCostsPaidLastTurn ||
             c.Action == EffectActionKind.CallTypeOfRandomOpponentHandCardBurn);

        // ───────────────────────────── Fatal Abacus ─────────────────────────────

        /// <summary>
        /// BattleWindowsRuntime.NoteMonsterSentFromField: <paramref name="card"/> left a Monster Zone for
        /// <paramref name="gyOwner"/>'s GY. Recorded for every Fatal Abacus active now.
        /// </summary>
        public static void NoteMonsterSentFromField(DuelEngine engine, DuelistState gyOwner, CardInstance card)
        {
            if (engine == null || gyOwner == null || card?.Def == null || card.IsToken) return;
            if (!gyOwner.Graveyard.Contains(card)) return;
            var live = LiveCards(engine, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy);
            if (live.Count == 0) return;
            var send = new AbacusSend { GyOwner = gyOwner };
            foreach (var (_, abacus, _) in live)
                send.Watchers.Add((abacus, abacus.FieldEntryStamp));
            State(engine).AbacusSends.Add(send);
        }

        /// <summary>
        /// StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: the recorded monsters cost their owners Amount each, for
        /// every Fatal Abacus that watched them and is still active (both players' damage together).
        /// </summary>
        public static void FlushAbacus(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            if (!s_state.TryGetValue(engine, out var st) || st.AbacusBusy || st.AbacusSends.Count == 0) return;
            var sends = st.AbacusSends.ToList();
            st.AbacusSends.Clear();
            st.AbacusBusy = true;
            try
            {
                foreach (var (side, abacus, clause) in LiveCards(engine, EffectTiming.ContinuousWhileFaceUp,
                             EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy))
                {
                    if (engine.GameOver) break;
                    var seen = sends.Where(s => s.Watchers.Exists(w =>
                        ReferenceEquals(w.abacus, abacus) && w.entry == abacus.FieldEntryStamp)).ToList();
                    if (seen.Count == 0) continue;
                    var opp = engine.OpponentOf(side);
                    var toSide = seen.Count(s => ReferenceEquals(s.GyOwner, side));
                    var toOpp = seen.Count(s => ReferenceEquals(s.GyOwner, opp));
                    var amount = clause.Amount > 0 ? clause.Amount : 500;
                    engine.Log($"{abacus.Name}: {seen.Count} monster(s) sent from the field to the GY — " +
                               $"{amount} damage to the owner for each.");
                    engine.ApplyEffectDamageBoth(side, amount * toSide, opp, amount * toOpp, abacus.Name);
                }
            }
            finally
            {
                st.AbacusBusy = false;
            }
        }

        // ───────────────────────────── Life Absorbing Machine ─────────────────────────────

        /// <summary>DuelEngine.BeginTurn: the new turn player's previous turn is now "your last turn".</summary>
        public static void OnTurnBegin(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return;
            var st = State(engine);
            st.CostsLastTurn[who] = st.CostsThisTurn.TryGetValue(who, out var paid) ? paid : new List<int>();
            st.CostsThisTurn[who] = new List<int>();
        }

        /// <summary>
        /// An LP cost of <paramref name="paid"/> was paid (DuelEngine.PayLifePoints / PayLifePointCost). Counted only for
        /// the turn player (Lua checkop: ep == Duel.GetTurnPlayer()).
        /// </summary>
        public static void NoteLpCostPaid(DuelEngine engine, DuelistState who, int paid)
        {
            if (engine == null || who == null || paid <= 0 || !ReferenceEquals(engine.TurnPlayer, who)) return;
            var st = State(engine);
            if (!st.CostsThisTurn.TryGetValue(who, out var list))
                st.CostsThisTurn[who] = list = new List<int>();
            list.Add(paid);
        }

        /// <summary>A Cancel refunded an LP cost (the activation never happened): it no longer counts as paid.</summary>
        public static void RefundLpCost(DuelEngine engine, DuelistState who, int amount)
        {
            if (engine == null || who == null || amount <= 0 || !s_state.TryGetValue(engine, out var st)) return;
            if (!st.CostsThisTurn.TryGetValue(who, out var list)) return;
            var i = list.LastIndexOf(amount);
            if (i >= 0) list.RemoveAt(i);
        }

        /// <summary>The LP costs <paramref name="who"/> paid during their last turn, in order.</summary>
        public static IReadOnlyList<int> CostsPaidLastTurn(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && s_state.TryGetValue(engine, out var st) &&
            st.CostsLastTurn.TryGetValue(who, out var list)
                ? list
                : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>
        /// TriggerChainResponses batch (v163), TextEffectRuntime.FirePhaseTriggersFor: Life Absorbing Machine's Standby
        /// effect (ygopro EFFECT_TYPE_TRIGGER_F) goes on a Chain the opponent may respond to (an MST on this card, Royal
        /// Decree). It activates only when LP were paid during the controller's last turn (Lua s.rectg chk 0) and resolves
        /// only while this card is still on the field (Lua s.recop IsRelateToEffect). False: resolve it now, as before.
        /// </summary>
        public static bool TrySeatStandbyGain(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null ||
                clause.Action != EffectActionKind.GainHalfLpCostsPaidLastTurn || clause.Timing != EffectTiming.StandbyPhase)
                return false;
            if (engine.TurnPlayer != who || !source.FaceUp || !who.TryFindSpellTrap(source, out _)) return false;
            if (CostsPaidLastTurn(engine, who).Count == 0) return false;
            var entry = source.FieldEntryStamp;
            var seat = new TriggerChainResponsesRuntime.Seat
            {
                Controller = who,
                Card = source,
                From = Rules.CardLocation.SpellTrapZone,
                Resolve = () =>
                {
                    if (!StillSeated(engine, who, source, entry))
                    {
                        engine.Log($"{source.Name} is no longer on the field — its effect does not resolve.");
                        return;
                    }

                    GainHalfLpCostsPaidLastTurn(engine, who, source);
                }
            };
            // Held for the Standby Phase's Chain (TextEffectRuntime.FirePhaseTriggers collects its triggers): two
            // Machines are Chain Links 1 and 2 of one Chain (review, v163).
            return TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, seat.Resolve);
        }

        public static void GainHalfLpCostsPaidLastTurn(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var paid = CostsPaidLastTurn(engine, who).ToList();
            if (paid.Count == 0)
            {
                engine.Log($"{source?.Name}: {who.Name} paid no Life Points during their last turn — nothing to gain.");
                return;
            }

            foreach (var p in paid)
            {
                if (engine.GameOver) return;
                var gain = (p + 1) / 2;
                engine.Log($"{source?.Name}: half of the {p} LP paid during {who.Name}'s last turn.");
                TextEffectRuntime.ApplyLifePointGain(engine, who, gain, source?.Name);
            }
        }

        // ───────────────────────────── Ominous Fortunetelling ─────────────────────────────

        internal static CardInstance TypeToken(string value) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = value,
                type = CardTypeTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = 0,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        internal static int CardTypeIndex(CardDef def) =>
            def == null ? -1 : def.IsMonster ? 0 : def.IsSpell ? 1 : def.IsTrap ? 2 : -1;

        /// <summary>AI call: the type most common among every card the opponent owns (Monster on a tie).</summary>
        internal static int AiCall(DuelEngine engine, DuelistState opp)
        {
            var counts = new int[3];
            void Count(CardDef d)
            {
                var i = CardTypeIndex(d);
                if (i >= 0) counts[i]++;
            }

            if (opp != null)
            {
                foreach (var id in opp.Deck) Count(engine.Database?.Get(id));
                foreach (var c in opp.Hand.Concat(opp.Graveyard).Concat(opp.Banished)
                             .Concat(opp.MonstersOnField()).Concat(opp.SpellTrapsOnField()))
                    Count(c?.Def);
            }

            var best = 0;
            for (var i = 1; i < 3; i++)
                if (counts[i] > counts[best]) best = i;
            return best;
        }

        public static void ResolveFortunetelling(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            var opp = engine?.OpponentOf(who);
            if (opp?.Hand == null || opp.Hand.Count == 0)
            {
                engine?.Log($"{source?.Name}: {opp?.Name} has no card in hand — nothing happens.");
                return;
            }

            if (CanAsk(engine, who))
            {
                Ask(engine, new Pick
                {
                    Kind = PickKind.FortuneCall,
                    Who = who,
                    Source = source,
                    Clause = clause,
                    StandbyHold = engine.Phase == DuelPhase.Standby && ReferenceEquals(engine.TurnPlayer, who)
                });
                return;
            }

            FinishFortunetelling(engine, who, source, clause, AiCall(engine, opp));
        }

        static void FinishFortunetelling(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause,
            int call)
        {
            var opp = engine.OpponentOf(who);
            if (opp?.Hand == null || opp.Hand.Count == 0) return;
            var i = engine.Rng.PickIndex(opp.Hand.Count);
            if (i < 0 || i >= opp.Hand.Count) i = 0;
            var card = opp.Hand[i];
            var kind = CardTypeIndex(card?.Def);
            call = Math.Max(0, Math.Min(2, call));
            engine.Log($"{source?.Name}: {who.Name} calls {TypeNames[call]}; the card picked at random from " +
                       $"{opp.Name}'s hand is {card?.Name} ({(kind >= 0 ? TypeNames[kind] : "?")}).");
            if (kind != call)
            {
                engine.Log($"{source?.Name}: wrong call — no damage.");
                return;
            }

            var amount = clause != null && clause.Amount > 0 ? clause.Amount : 700;
            engine.ApplyEffectDamage(opp, amount, source?.Name);
        }

        // ───────────────────────────── Fengsheng Mirror ─────────────────────────────

        static List<CardInstance> SpiritsInHand(DuelistState side) =>
            side?.Hand?.Where(h => h != null && IsSpiritMonster(h.Def)).ToList() ?? new List<CardInstance>();

        static CardInstance AiSpirit(List<CardInstance> spirits) =>
            spirits.OrderByDescending(s => s.Def?.level ?? 0).ThenByDescending(s => s.Def?.atk ?? 0).FirstOrDefault();

        public static void ResolveFengsheng(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp?.Hand == null || opp.Hand.Count == 0)
            {
                engine?.Log($"{source?.Name}: {opp?.Name} has no card in hand.");
                return;
            }

            engine.Log($"{source?.Name}: {who.Name} looks at {opp.Name}'s hand — " +
                       string.Join(", ", opp.Hand.Where(h => h != null).Select(h => h.Name)) + ".");
            var spirits = SpiritsInHand(opp);
            if (spirits.Count == 0)
            {
                engine.Log($"{source?.Name}: no Spirit monster in {opp.Name}'s hand — nothing is discarded.");
                return;
            }

            if (spirits.Count > 1 && CanAsk(engine, who))
            {
                Ask(engine, new Pick { Kind = PickKind.FengshengDiscard, Who = who, Source = source });
                return;
            }

            FinishFengsheng(engine, who, source, spirits.Count == 1 ? spirits[0] : AiSpirit(spirits));
        }

        static void FinishFengsheng(DuelEngine engine, DuelistState who, CardInstance source, CardInstance spirit)
        {
            var opp = engine.OpponentOf(who);
            if (spirit == null || opp?.Hand == null || !opp.Hand.Contains(spirit)) return;
            var gone = MonsterEffects.DiscardFromHandByInstance(opp, spirit, DiscardCause.Effect, who);
            if (gone == null) return;
            engine.Log($"{source?.Name}: {opp.Name} discards {gone.Name}.");
            DiscardTriggersRuntime.NoteDiscard(engine, opp, gone, who);
        }

        // ───────────────────────────── Spirit's Invitation ─────────────────────────────

        /// <summary>
        /// DuelEngine.ReturnCardToHand: <paramref name="card"/> left a Monster Zone for <paramref name="handOwner"/>'s
        /// hand; a Spirit that was face-up there is recorded for <see cref="FireSpiritReturnTriggers"/>.
        /// </summary>
        public static void NoteMonsterReturnedToHand(DuelEngine engine, DuelistState handOwner, CardInstance card,
            bool wasFaceUp)
        {
            if (engine == null || handOwner == null || !wasFaceUp || !IsSpiritMonster(card?.Def)) return;
            State(engine).SpiritReturns.Add((handOwner, card));
        }

        /// <summary>
        /// TextEffectRuntime.NotifyMonstersReturnedToOwnerHand (once per return event): each active Spirit's Invitation
        /// whose controller got a face-up Spirit back in their hand makes the opponent return 1 monster they control.
        /// </summary>
        public static void FireSpiritReturnTriggers(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.SpiritReturns.Count == 0) return;
            var returns = st.SpiritReturns.ToList();
            st.SpiritReturns.Clear();
            // Every Invitation that triggers on this return goes on one Chain (review, v163): collected here, seated by
            // the outermost EndCollect (an End Phase collects all its triggers).
            TriggerChainResponsesRuntime.BeginCollect(engine);
            try
            {
                FireInvitations(engine, returns);
            }
            finally
            {
                TriggerChainResponsesRuntime.EndCollect(engine);
            }
        }

        static void FireInvitations(DuelEngine engine, List<(DuelistState handOwner, CardInstance spirit)> returns)
        {
            foreach (var (side, invitation, _) in LiveCards(engine, EffectTiming.ContinuousWhileFaceUp,
                         EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand))
            {
                if (engine.GameOver) return;
                if (!returns.Exists(r => ReferenceEquals(r.handOwner, side))) continue;
                // Cold Wave: an activated Spell/Trap trigger (TRIGGER_F) cannot be activated under the lock.
                if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, invitation)) continue;
                var picker = engine.OpponentOf(side);
                if (picker == null) continue;
                // TriggerChainResponses batch (v163): the trigger (ygopro EFFECT_TYPE_TRIGGER_F) goes on a Chain the
                // opponent may respond to; the opponent selects at resolution (Lua s.operation, no IsRelateToEffect: it
                // resolves even if this card left the field).
                var card = invitation;
                var seat = new TriggerChainResponsesRuntime.Seat
                {
                    Controller = side,
                    Card = card,
                    From = Rules.CardLocation.SpellTrapZone,
                    Resolve = () => ResolveInvitationReturn(engine, side, picker, card)
                };
                if (TriggerChainResponsesRuntime.TrySeatOrCollect(engine, seat, seat.Resolve))
                    continue;
                ResolveInvitationReturn(engine, side, picker, invitation);
            }
        }

        /// <summary>Spirit's Invitation resolves: <paramref name="picker"/> returns 1 monster they control to the hand.</summary>
        static void ResolveInvitationReturn(DuelEngine engine, DuelistState side, DuelistState picker,
            CardInstance invitation)
        {
            if (engine.GameOver) return;
            engine.Log($"{invitation.Name}: {side.Name}'s Spirit monster returned to the hand — " +
                       $"{picker.Name} returns 1 monster they control to the hand.");
            var choices = picker.MonstersOnField().Where(m => m != null).ToList();
            if (choices.Count == 0)
            {
                engine.Log($"{invitation.Name}: {picker.Name} controls no monster.");
                return;
            }

            if (choices.Count > 1 && CanAsk(engine, picker))
            {
                Ask(engine, new Pick { Kind = PickKind.InvitationReturn, Who = picker, Source = invitation });
                return;
            }

            FinishInvitationReturn(engine, picker, invitation, choices.Count == 1 ? choices[0] : AiOwnReturn(choices));
        }

        /// <summary>The AI returns its least valuable monster (a face-up Spirit first: it would return anyway).</summary>
        static CardInstance AiOwnReturn(List<CardInstance> choices) =>
            choices.OrderBy(m => m.FaceUp && IsSpiritMonster(m.Def) ? 0 : 1)
                .ThenBy(m => m.FaceUp ? m.CurrentAtk : m.Def?.atk ?? 0)
                .FirstOrDefault();

        static void FinishInvitationReturn(DuelEngine engine, DuelistState picker, CardInstance invitation,
            CardInstance monster)
        {
            if (monster == null || picker == null || !picker.TryFindMonster(monster, out _)) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, monster, invitation))
            {
                engine.Log($"{monster.Name} is unaffected by {invitation?.Name}.");
                return;
            }

            engine.Log($"{invitation?.Name}: {picker.Name} returns {monster.Name} to its owner's hand.");
            // Spirit's Invitation's effect: its controller (the picker's opponent) is the reason player (v163).
            engine.ReturnCardToHand(monster, by: engine.OpponentOf(picker));
        }

        // ───────────────────────────── Spiritual Energy Settle Machine ─────────────────────────────

        /// <summary>SpiritsAndMiscRuntime.SpiritReturnDue: an active Settle Machine keeps every Spirit on the field.</summary>
        public static bool SpiritReturnPrevented(DuelEngine engine) =>
            engine != null && LiveCards(engine, EffectTiming.ContinuousWhileFaceUp,
                EffectActionKind.SpiritsDoNotReturnWhileThisFaceUp).Count > 0;

        static bool StillSeated(DuelEngine engine, DuelistState who, CardInstance card, int entry) =>
            card != null && who != null && card.FaceUp && card.FieldEntryStamp == entry &&
            who.TryFindSpellTrap(card, out _) && !InAnyPile(engine, card);

        /// <summary>
        /// TextEffectRuntime.FirePhaseTriggersFor, the controller's End Phase: discard 1 card or destroy this card. A
        /// maintenance cost (EFFECT_FLAG_CANNOT_DISABLE): it applies while the card is negated too.
        /// </summary>
        public static void ResolveEndPhaseUpkeep(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || source == null) return;
            if (!StillSeated(engine, who, source, source.FieldEntryStamp)) return;
            if (who.Hand == null || who.Hand.Count == 0)
            {
                engine.Log($"{source.Name}: {who.Name} has no card to discard — destroy this card.");
                DestroyForUpkeep(engine, who, source);
                return;
            }

            if (CanAsk(engine, who))
            {
                Ask(engine, new Pick
                {
                    Kind = PickKind.SettleDiscard,
                    Who = who,
                    Source = source,
                    Clause = clause,
                    EntryStamp = source.FieldEntryStamp
                });
                return;
            }

            var keep = AiKeepsSettleMachine(who);
            FinishSettleUpkeep(engine, who, source, source.FieldEntryStamp,
                keep ? AiHeuristicPolicy.PickLowestValue(who.Hand, null) : null);
        }

        /// <summary>AI: keep the Machine (discard its least valuable card) only while it holds a face-up Spirit.</summary>
        static bool AiKeepsSettleMachine(DuelistState who) =>
            who.Hand.Count > 0 && who.MonstersOnField().Any(m => m != null && m.FaceUp && IsSpiritMonster(m.Def));

        static void FinishSettleUpkeep(DuelEngine engine, DuelistState who, CardInstance source, int entry,
            CardInstance discard)
        {
            if (!StillSeated(engine, who, source, entry)) return;
            if (discard != null && who.Hand.Contains(discard))
            {
                var gone = MonsterEffects.DiscardFromHandByInstance(who, discard, DiscardCause.Cost, who);
                if (gone != null)
                {
                    engine.Log($"Cost: {who.Name} discards {gone.Name} to keep {source.Name}.");
                    return;
                }
            }

            engine.Log($"{source.Name}: {who.Name} does not discard — destroy this card.");
            DestroyForUpkeep(engine, who, source);
        }

        static void DestroyForUpkeep(DuelEngine engine, DuelistState who, CardInstance source)
        {
            TextEffectRuntime.DestroyCard(engine, source, source, false, who);
            // Its leave-field effect applies at once (continuous, no Chain Link).
            SweepLeftMachines(engine);
        }

        /// <summary>
        /// FieldSpellEffects.RefreshBoard (before the auras): an active Settle Machine seen at the last sweep that has
        /// since left the field (not merely turned face-down or negated) returns every face-up Spirit monster on the
        /// field to its owner's hand, as one event.
        /// </summary>
        public static void SweepLeftMachines(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            var st = State(engine);
            if (st.SweepBusy) return;
            var left = st.LiveMachines.Where(t => !OnFieldSameEntry(engine, t.machine, t.entry)).ToList();
            st.LiveMachines = LiveCards(engine, EffectTiming.ContinuousWhileFaceUp,
                    EffectActionKind.ReturnFaceUpSpiritsWhenThisLeavesField)
                .Select(x => (x.card, x.card.FieldEntryStamp, x.side)).ToList();
            if (left.Count == 0) return;
            st.SweepBusy = true;
            try
            {
                foreach (var (machine, _, side) in left)
                {
                    if (engine.GameOver) break;
                    ReturnAllFaceUpSpirits(engine, machine, side);
                }
            }
            finally
            {
                st.SweepBusy = false;
            }
        }

        static bool OnFieldSameEntry(DuelEngine engine, CardInstance card, int entry)
        {
            if (card == null || card.FieldEntryStamp != entry || InAnyPile(engine, card)) return false;
            return (engine.Player?.TryFindSpellTrap(card, out _) ?? false) ||
                   (engine.Opponent?.TryFindSpellTrap(card, out _) ?? false);
        }

        static void ReturnAllFaceUpSpirits(DuelEngine engine, CardInstance machine, DuelistState machineSide)
        {
            var spirits = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
                spirits.AddRange(side.MonstersOnField().Where(m => m != null && m.FaceUp && IsSpiritMonster(m.Def)));
            if (spirits.Count == 0) return;
            engine.Log($"{machine?.Name} left the field — every face-up Spirit monster returns to its owner's hand.");
            TextEffectRuntime.BeginMonsterReturnToOwnerHandBatch();
            try
            {
                foreach (var m in spirits)
                {
                    if (engine.ControllerOf(m) == null) continue;
                    if (ContinuousProtections.IsUnaffectedBy(engine, m, machine)) continue;
                    engine.ReturnCardToHand(m, by: machineSide); // the machine's controller (v163 reason player)
                }
            }
            finally
            {
                TextEffectRuntime.EndMonsterReturnToOwnerHandBatch(engine);
            }
        }

        // ───────────────────────────── human choices ─────────────────────────────

        static bool IsMine(DuelEngine engine) =>
            engine?.PendingActivation != null && s_open.TryGetValue(engine.PendingActivation, out _);

        /// <summary>A human chooses through a prompt: nothing else is open, or this batch's own prompt (queued behind).</summary>
        static bool CanAsk(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && engine.IsHumanControlled(who) && !engine.GameOver &&
            (engine.PendingActivation == null || IsMine(engine));

        static void Ask(DuelEngine engine, Pick pick)
        {
            State(engine).Queue.Add(pick);
            OfferQueuedPick(engine);
        }

        /// <summary>
        /// FlipDownAndPeek batch (v162): <paramref name="who"/> chooses 1 of the cards <paramref name="candidates"/> returns,
        /// at resolution, through this batch's prompt (Trap Dustshoot's monster in the opponent's hand; A Man with Wdjat's
        /// Standby Phase Set card, which holds the Standby Phase). The AI, a lone candidate, or a prompt that cannot open
        /// now take <paramref name="aiChoose"/>'s card at once; no candidate passes null. <paramref name="finish"/> runs
        /// with the chosen card (null: nothing to choose).
        /// </summary>
        public static void AskCardPick(DuelEngine engine, DuelistState who, CardInstance source,
            Func<List<CardInstance>> candidates, EffectTargetKind kind, string prompt,
            Func<List<CardInstance>, CardInstance> aiChoose, Action<CardInstance> finish) =>
            AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = candidates,
                Kind = kind,
                Prompt = prompt,
                AiChoose = aiChoose,
                Finish = finish
            });

        /// <summary>
        /// EngineResiduals (v163): THE generic human card pick (see <see cref="CardPickRequest"/>). <paramref name="who"/>
        /// chooses 1 of <see cref="CardPickRequest.Candidates"/> through the target sheet (<see cref="PendingActivation"/>,
        /// <see cref="PendingActivation.LegalTargets"/>); the answer runs <see cref="CardPickRequest.Finish"/>, Cancel runs
        /// <see cref="CardPickRequest.Cancel"/> (or makes the AI's choice when the pick is mandatory). The AI, a lone
        /// candidate (unless <see cref="CardPickRequest.AskWithOneCandidate"/>) or a prompt that cannot open now take the
        /// automatic answer at once. Picks queue behind each other, hold the Chain while it resolves, hold the live
        /// Standby Phase (the upkeeps still run afterwards: DuelEngine.ResumeStandbyAfterUpkeep) and resume the paused
        /// End Phase. Routing is done here (<see cref="TryResolvePick"/> / <see cref="TryCancelPick"/>): new code needs no
        /// PendingActivation dictionary and no new hook.
        /// </summary>
        public static void AskCardPick(DuelEngine engine, DuelistState who, CardInstance source, CardPickRequest req)
        {
            if (engine == null || who == null || req?.Candidates == null || req.Finish == null) return;
            var now = req.Candidates() ?? new List<CardInstance>();
            if (now.Count == 0 || (now.Count == 1 && !req.AskWithOneCandidate) || !CanAsk(engine, who))
            {
                req.Finish(AutoAnswer(now, req.AiChoose, req.AskWithOneCandidate));
                return;
            }

            Ask(engine, new Pick
            {
                Kind = PickKind.CardPick,
                Who = who,
                Source = source,
                Candidates = req.Candidates,
                TargetKind = req.Kind,
                PromptText = req.Prompt,
                AiChoose = req.AiChoose,
                Finish = req.Finish,
                Cancel = req.Cancel,
                AskWithOneCandidate = req.AskWithOneCandidate,
                HoldsChain = req.HoldsChain && !req.DuringActivation,
                DuringActivation = req.DuringActivation,
                AsksPlacement = req.AsksSpecialSummonPlacement,
                StandbyHold = !req.DuringActivation &&
                              (req.HoldsStandby ?? (engine.Phase == DuelPhase.Standby &&
                                                    ReferenceEquals(engine.TurnPlayer, who))) &&
                              engine.Phase == DuelPhase.Standby
            });
        }

        /// <summary>The answer made without a prompt: none, the lone card, or the AI's choice.</summary>
        static CardInstance AutoAnswer(List<CardInstance> list, Func<List<CardInstance>, CardInstance> aiChoose,
            bool askWithOne)
        {
            if (list == null || list.Count == 0) return null;
            if (list.Count == 1 && !askWithOne) return list[0];
            // A yes/no tap (askWithOne) may be declined: the AI's null answer stands.
            return askWithOne ? aiChoose?.Invoke(list) : aiChoose?.Invoke(list) ?? list[0];
        }

        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163): an optional "you can" choice among the cards <paramref name="candidates"/>
        /// returns (A Cat of Ill Omen's "you can add that Trap to your hand instead": its one card, tap it = yes, Cancel =
        /// no). Unlike <see cref="AskCardPick"/> a lone card is still asked and Cancel declines: <paramref name="finish"/>
        /// gets null. The AI, or a prompt that cannot open now, takes <paramref name="aiChoose"/>'s answer (null declines).
        /// </summary>
        public static void AskOptionalCardPick(DuelEngine engine, DuelistState who, CardInstance source,
            Func<List<CardInstance>> candidates, EffectTargetKind kind, string prompt,
            Func<List<CardInstance>, CardInstance> aiChoose, Action<CardInstance> finish)
        {
            if (engine == null || who == null || candidates == null || finish == null) return;
            var now = candidates() ?? new List<CardInstance>();
            if (now.Count == 0 || !CanAsk(engine, who))
            {
                finish(now.Count == 0 ? null : aiChoose?.Invoke(now));
                return;
            }

            Ask(engine, new Pick
            {
                Kind = PickKind.CardPick,
                Who = who,
                Source = source,
                Candidates = candidates,
                TargetKind = kind,
                PromptText = prompt,
                AiChoose = aiChoose,
                Finish = finish,
                Optional = true,
                StandbyHold = engine.Phase == DuelPhase.Standby && ReferenceEquals(engine.TurnPlayer, who)
            });
        }

        /// <summary>
        /// Opens the next queued choice when no prompt is open. True while one of this batch's choices is open.
        /// </summary>
        public static bool OfferQueuedPick(DuelEngine engine)
        {
            if (engine == null) return false;
            if (engine.PendingActivation != null) return IsMine(engine);
            if (!s_state.TryGetValue(engine, out var st)) return false;
            while (st.Queue.Count > 0 && !engine.GameOver)
            {
                var pick = st.Queue[0];
                st.Queue.RemoveAt(0);
                var pending = BuildPending(engine, pick);
                if (pending == null) continue;
                s_open.Add(pending, pick);
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                return true;
            }

            return false;
        }

        /// <summary>The prompt for <paramref name="pick"/>, or null when it was settled without one (no or one choice).</summary>
        static PendingActivation BuildPending(DuelEngine engine, Pick pick)
        {
            var who = pick.Who;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = pick.Source,
                UsesTextProgram = true,
                HoldsChainResolution = pick.HoldsChain,
                TargetPicksRemaining = 1
            };
            switch (pick.Kind)
            {
                case PickKind.FortuneCall:
                {
                    var opp = engine.OpponentOf(who);
                    if (opp?.Hand == null || opp.Hand.Count == 0) return null;
                    pending.PromptOverride = "call the type of the card picked at random from your opponent's hand " +
                                             "(Monster, Spell or Trap). A right call inflicts " +
                                             $"{(pick.Clause?.Amount > 0 ? pick.Clause.Amount : 700)} damage.";
                    foreach (var n in TypeNames)
                        pending.LegalTargets.Add(TypeToken(n));
                    return pending;
                }
                case PickKind.InvitationReturn:
                {
                    var choices = who.MonstersOnField().Where(m => m != null).ToList();
                    if (choices.Count <= 1)
                    {
                        FinishInvitationReturn(engine, who, pick.Source, choices.FirstOrDefault());
                        return null;
                    }

                    pending.TargetKind = EffectTargetKind.YourMonster;
                    pending.PromptOverride = "your opponent's card effect — return 1 monster you control to its " +
                                             "owner's hand.";
                    pending.LegalTargets.AddRange(choices);
                    return pending;
                }
                case PickKind.SettleDiscard:
                {
                    if (!StillSeated(engine, who, pick.Source, pick.EntryStamp)) return null;
                    var hand = who.Hand.Where(h => h != null).ToList();
                    if (hand.Count == 0)
                    {
                        FinishSettleUpkeep(engine, who, pick.Source, pick.EntryStamp, null);
                        return null;
                    }

                    pending.TargetKind = EffectTargetKind.DiscardFromHand;
                    pending.PromptOverride = "discard 1 card from your hand to keep this card, or Cancel to destroy it.";
                    pending.LegalTargets.AddRange(hand);
                    return pending;
                }
                case PickKind.FengshengDiscard:
                {
                    var spirits = SpiritsInHand(engine.OpponentOf(who));
                    if (spirits.Count <= 1)
                    {
                        FinishFengsheng(engine, who, pick.Source, spirits.FirstOrDefault());
                        return null;
                    }

                    pending.TargetKind = EffectTargetKind.CardInOppHand;
                    pending.PromptOverride = "select 1 Spirit monster in your opponent's hand to discard.";
                    pending.LegalTargets.AddRange(spirits);
                    return pending;
                }
                case PickKind.CardPick:
                {
                    var list = pick.Candidates?.Invoke() ?? new List<CardInstance>();
                    if (list.Count == 0 || (list.Count == 1 && !pick.AskWithOneCandidate && !pick.Optional))
                    {
                        pick.Finish?.Invoke(list.FirstOrDefault());
                        return null;
                    }

                    pending.TargetKind = pick.TargetKind;
                    pending.PromptOverride = pick.PromptText;
                    pending.LegalTargets.AddRange(list);
                    return pending;
                }
            }

            return null;
        }

        /// <summary>
        /// SpellTrapEffects.PendingTargetIsSpecialSummon (BattleDestroyedTriggers batch): an open card pick whose card is
        /// Special Summoned (<see cref="CardPickRequest.AsksSpecialSummonPlacement"/>) asks a face-up position and a zone.
        /// False (not handled) for every other pending, this batch's other picks included.
        /// </summary>
        public static bool TryPickAsksPlacement(PendingActivation pending, out bool asksPlacement)
        {
            asksPlacement = false;
            if (pending == null || !s_open.TryGetValue(pending, out var pick) || !pick.AsksPlacement) return false;
            asksPlacement = true;
            return true;
        }

        /// <summary>DuelEngine.BeginTurn / ResumeStandbyAfterUpkeep: the Standby Phase waits for this batch's open call.</summary>
        public static bool HoldsStandby(DuelEngine engine) =>
            engine?.PendingActivation != null && s_open.TryGetValue(engine.PendingActivation, out var pick) &&
            pick.StandbyHold;

        /// <summary>SpellTrapEffects.TryResolveWithTarget: a choice of this batch was made.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_open.TryGetValue(p, out var pick)) return false;
            var chosen = engine.ResolveLegalEffectTarget(target) ?? target;
            if (chosen == null || !p.LegalTargets.Contains(chosen))
            {
                engine.Log("Pick one of the highlighted cards.");
                return true;
            }

            s_open.Remove(p);
            engine.ClearPendingActivation();
            Complete(engine, pick, p, chosen);
            AfterPick(engine, pick);
            ok = true;
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: the Settle Machine upkeep's Cancel = do not discard (the card is destroyed);
        /// the other choices are mandatory — Cancel makes the AI's choice.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_open.TryGetValue(p, out var pick)) return false;
            s_open.Remove(p);
            engine.ClearPendingActivation();
            CardInstance auto = null;
            switch (pick.Kind)
            {
                case PickKind.FortuneCall:
                    auto = p.LegalTargets.ElementAtOrDefault(AiCall(engine, engine.OpponentOf(pick.Who)));
                    engine.Log($"{pick.Source?.Name}: the call is mandatory — {auto?.Name} is called.");
                    break;
                case PickKind.InvitationReturn:
                    auto = AiOwnReturn(p.LegalTargets.Where(m => pick.Who.TryFindMonster(m, out _)).ToList());
                    engine.Log($"{pick.Source?.Name}: the return is mandatory — {auto?.Name} is chosen.");
                    break;
                case PickKind.FengshengDiscard:
                    auto = AiSpirit(p.LegalTargets.ToList());
                    engine.Log($"{pick.Source?.Name}: the discard is mandatory — {auto?.Name} is chosen.");
                    break;
                case PickKind.CardPick when pick.Optional:
                    auto = null;
                    engine.Log($"{pick.Source?.Name}: declined.");
                    break;
                case PickKind.CardPick:
                {
                    // EngineResiduals (v163): a pick with its own Cancel answer (decline, or undo an activation).
                    if (pick.Cancel != null)
                    {
                        pick.Cancel();
                        AfterPick(engine, pick);
                        return true;
                    }

                    var list = p.LegalTargets.ToList();
                    auto = pick.AiChoose?.Invoke(list) ?? list.FirstOrDefault();
                    engine.Log($"{pick.Source?.Name}: the choice is mandatory — {auto?.Name} is chosen.");
                    break;
                }
                case PickKind.SettleDiscard:
                    auto = null;
                    break;
            }

            Complete(engine, pick, p, auto);
            AfterPick(engine, pick);
            return true;
        }

        static void Complete(DuelEngine engine, Pick pick, PendingActivation p, CardInstance chosen)
        {
            switch (pick.Kind)
            {
                case PickKind.FortuneCall:
                {
                    var call = Array.IndexOf(TypeNames, chosen?.Def?.name ?? "");
                    FinishFortunetelling(engine, pick.Who, pick.Source, pick.Clause,
                        call >= 0 ? call : AiCall(engine, engine.OpponentOf(pick.Who)));
                    break;
                }
                case PickKind.InvitationReturn:
                    FinishInvitationReturn(engine, pick.Who, pick.Source, chosen);
                    break;
                case PickKind.SettleDiscard:
                    FinishSettleUpkeep(engine, pick.Who, pick.Source, pick.EntryStamp, chosen);
                    break;
                case PickKind.FengshengDiscard:
                    FinishFengsheng(engine, pick.Who, pick.Source, chosen);
                    break;
                case PickKind.CardPick:
                    pick.Finish?.Invoke(chosen);
                    break;
            }
        }

        /// <summary>The next queued choice, or the paused Chain / End Phase / Standby Phase goes on.</summary>
        static void AfterPick(DuelEngine engine, Pick pick)
        {
            if (!engine.GameOver && OfferQueuedPick(engine))
            {
                engine.NotifyPublic();
                return;
            }

            // EngineResiduals (v163): a cost pick made while activating a card is part of that activation, which goes
            // on by itself (its chain window): nothing paused waits on it.
            if (pick.DuringActivation)
            {
                engine.NotifyPublic();
                return;
            }

            engine.ResumeChainAfterChoice();
            engine.ResumeEndTurnAfterTrigger();
            if (pick.StandbyHold)
                engine.ResumeStandbyAfterUpkeep();
            engine.NotifyPublic();
        }

        // ───────────────────────────── Mind on Air: hand reveal ─────────────────────────────

        /// <summary>
        /// Mind on Air (c66690411) at resolution: register this Trap as a hand revealer. It is a continuous field
        /// rule (ygopro EFFECT_FLAG_PLAYER_TARGET with player 1-tp, RESETS_STANDARD), so nothing here is one-shot —
        /// <see cref="RefreshHandReveal"/> recomputes it from the live sources on every board change.
        /// </summary>
        public static void ArmHandReveal(DuelEngine engine, DuelistState who, CardInstance source)
        {
            // Nothing to arm: RefreshHandReveal scans the board, so a card placed directly on the field applies its
            // rule at once and one that leaves the field stops applying.
            RefreshHandReveal(engine);
            engine?.NotifyPublic();
        }

        /// <summary>
        /// Recompute <see cref="DuelistState.HandRevealed"/> from the Mind on Air sources that are applying right now.
        /// A source applies while it is face-up in a Spell &amp; Trap Zone, its activation has resolved and its own
        /// effects are not negated (a negated Mind on Air reveals nothing). The player whose hand is revealed is the
        /// source's controller's opponent.
        /// </summary>
        public static void RefreshHandReveal(DuelEngine engine)
        {
            if (engine == null) return;
            if (engine.Player != null) engine.Player.HandRevealed = false;
            if (engine.Opponent != null) engine.Opponent.HandRevealed = false;

            // Scanned from the board, never from an armed list: a card placed directly on the field (a test, the
            // corpus, the Editor) applies its rule at once, and a card that leaves the field stops applying.
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                {
                    if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                    // A card whose activation is still on the Chain does not apply its continuous effects yet.
                    if (!ActivationResolved(engine, st)) continue;
                    if (!AppliesHandReveal(st)) continue;
                    var opp = engine.OpponentOf(side);
                    if (opp != null) opp.HandRevealed = true;
                    // Ceremonial Bell reveals both players' hands, not just the opponent's.
                    if (AppliesBothPlayersHandReveal(st)) side.HandRevealed = true;
                }
            }
        }

        /// <summary>True when <paramref name="card"/>'s compiled program has the hand-reveal rule.</summary>
        static bool AppliesHandReveal(CardInstance card)
        {
            var prog = WRLDZ.Duel.TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null &&
                c.Action == EffectActionKind.OpponentMustPlayWithHandRevealed ||
                c.Action == EffectActionKind.BothPlayersMustPlayWithHandRevealed);
        }

        /// <summary>True when <paramref name="card"/>'s program reveals both players' hands (Ceremonial Bell).</summary>
        static bool AppliesBothPlayersHandReveal(CardInstance card)
        {
            var prog = WRLDZ.Duel.TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c => c != null &&
                c.Action == EffectActionKind.BothPlayersMustPlayWithHandRevealed);
        }

        /// <summary>
        /// True when <paramref name="who"/> currently plays with their hand revealed — the player whose hand the UI
        /// and the AR hand volume may show with faces. A player's own hand is always visible to itself; this is the
        /// only thing that ever makes an <b>opponent's</b> hand public.
        /// </summary>
        public static bool HandRevealed(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && who.HandRevealed;
    }
}
