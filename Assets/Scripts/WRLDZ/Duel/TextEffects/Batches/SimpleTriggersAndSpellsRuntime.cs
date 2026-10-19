using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleTriggersAndSpells" (v210 FET) runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Abare Ushioni</b> (<see cref="EffectActionKind.CallCoinRightBurnOpponentWrongBurnSelf"/>): the coin is called
    /// as the Chain Link resolves (Fiend Comedian's flow: a human through <see cref="PendingActivation.AwaitingCoinCall"/>,
    /// holding the chain; the AI and a CANCEL call Heads). Right: Amount damage to the opponent; wrong: to the controller.
    /// The toss is a CoinDiceInterrupts coin-toss event (Second Coin Toss may redo it).</item>
    /// <item><b>Granmarg the Rock Monarch</b> (<see cref="EffectZoneFilter.FieldSetCards"/>): the Set cards on both fields
    /// (<see cref="CollectSetCards"/>); the AI targets an opponent's Set card (<see cref="AutoPickSetCard"/>) and its own
    /// only when the mandatory trigger leaves no other; the Destroy arm re-checks face-down at resolution.</item>
    /// <item><b>Good Goblin Housekeeping</b> (<see cref="EffectActionKind.DrawNamedInGyPlusAmountThenHandCardToDeckBottom"/>):
    /// <see cref="ResolveGoblinHousekeeping"/>.</item>
    /// <item><b>The Big March of Animals</b> (<see cref="EffectActionKind.FaceUpRaceYouControlGainAtkPerFaceUpRaceUntilEndOfTurn"/>):
    /// <see cref="ResolveRaceMarch"/>.</item>
    /// <item><b>Elemental Burst</b> (<see cref="EffectClause.TributeOneEachAttribute"/>): the generic structured Tribute cost
    /// (RequiresTributeCount 4) with <see cref="FilterTributeCostCandidates"/>: only monsters of an Attribute still missing
    /// are offered, none when a full set cannot be made; a human's picks are recorded per activation
    /// (<see cref="NoteTributeCost"/>), the AI Tributes one of each (ordered first) together.</item>
    /// </list>
    /// </summary>
    public static class SimpleTriggersAndSpellsRuntime
    {
        // ───────────────────────────── Abare Ushioni ─────────────────────────────

        sealed class CoinCall
        {
            public DuelistState Who;
            public CardInstance Source;
            public int Amount;
        }

        static readonly ConditionalWeakTable<PendingActivation, CoinCall> s_calls = new();

        /// <summary>
        /// The ApplyClause arm: <paramref name="who"/> tosses a coin and calls it. A human is asked (the chain waits); the AI
        /// calls Heads (a fair coin is called right half the time whichever face is called).
        /// </summary>
        internal static void ResolveCoinCallBurn(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var amount = Math.Max(0, clause?.Amount ?? 0);
            if (engine.IsHumanControlled(who) && engine.PendingActivation == null)
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    IsMonsterEffect = source?.Def != null && source.Def.IsMonster,
                    UsesTextProgram = true,
                    AwaitingCoinCall = true,
                    // A Chain Link waits on the call; a monster ignition resolving inline has no Chain to hold.
                    HoldsChainResolution = engine.IsResolvingChain,
                    PromptOverride = $"call the coin toss — right: inflict {amount} damage to your opponent; wrong: " +
                                     $"you take {amount} damage."
                };
                s_calls.Add(pending, new CoinCall { Who = who, Source = source, Amount = amount });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            FinishCoinCallBurn(engine, who, source, amount, callHeads: true);
        }

        static void FinishCoinCallBurn(DuelEngine engine, DuelistState who, CardInstance source, int amount,
            bool callHeads)
        {
            var opp = engine.OpponentOf(who);
            // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo it before the call is judged.
            CoinDiceInterruptsRuntime.TossCoin(engine, who, source, t => t == callHeads, toss =>
            {
                engine.Log($"{who?.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(toss ? "Heads" : "Tails")}.");
                if (who == null || opp == null || amount <= 0 || engine.GameOver) return;
                if (toss == callHeads)
                {
                    engine.Log($"{source?.Name}: right call — {amount} damage to {opp.Name}.");
                    engine.ApplyEffectDamage(opp, amount, source?.Name, source);
                }
                else
                {
                    engine.Log($"{source?.Name}: wrong call — {who.Name} takes {amount} damage.");
                    engine.ApplyEffectDamage(who, amount, source?.Name, source);
                }
            });
        }

        /// <summary>DuelEngine.TrySelectCoinCall: Abare Ushioni's call. False for any other coin call.</summary>
        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !p.AwaitingCoinCall || !s_calls.TryGetValue(p, out var call)) return false;
            s_calls.Remove(p);
            engine.ClearPendingActivation();
            FinishCoinCallBurn(engine, call.Who, call.Source, call.Amount, callHeads);
            // A held Chain resolves on; a monster ignition that resolved inline drops its idle link (as a target pick
            // does in DuelEngine.TrySelectEffectTarget).
            engine.ResumeChainAfterChoice();
            engine.ClearIdleChain();
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending: Abare Ushioni's call is mandatory — CANCEL calls Heads. Elemental Burst: once a
        /// Tribute of its cost has been paid it cannot be taken back, so CANCEL pays the rest for the player (one monster
        /// of each missing Attribute, lowest ATK first) and the activation goes on; with nothing paid the generic CANCEL
        /// undoes the activation.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null) return false;
            if (s_calls.TryGetValue(p, out _))
            {
                engine.Log($"{p.Card?.Name}: the call is mandatory — Heads is called.");
                return TryFinishCoinCall(engine, true);
            }

            // A seated activation's picks: nothing is Tributed yet — the caller undoes the activation.
            if (s_seatPicks.TryGetValue(p, out _))
            {
                s_seatPicks.Remove(p);
                return false;
            }

            if (!p.AwaitingIgnitionCost || OneEachClause(p.Card) == null ||
                !s_paidAttributes.TryGetValue(p, out var paid) || paid.Count == 0)
                return false;
            engine.Log($"{p.Card?.Name}: the Tributes already paid cannot be taken back — the rest of the cost is paid.");
            for (var i = 0; i < 8 && ReferenceEquals(engine.PendingActivation, p) && p.AwaitingIgnitionCost; i++)
            {
                var next = p.LegalTargets.FirstOrDefault();
                if (next == null || !SpellTrapEffects.TryResolveWithTarget(engine, next)) break;
            }

            return true;
        }

        // ───────────────────────────── Granmarg the Rock Monarch ─────────────────────────────

        /// <summary>Targets for <see cref="EffectZoneFilter.FieldSetCards"/>: every face-down card on either field.</summary>
        public static void CollectSetCards(DuelistState who, DuelistState opp, CardInstance except,
            List<CardInstance> into)
        {
            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m != null && !ReferenceEquals(m, except) && !m.FaceUp && !into.Contains(m))
                        into.Add(m);
                // SpellTrapsOnField yields the Field Zone card too.
                foreach (var st in side.SpellTrapsOnField())
                    if (st != null && !ReferenceEquals(st, except) && !st.FaceUp && !into.Contains(st))
                        into.Add(st);
            }
        }

        /// <summary>
        /// AI pick for a Set card: an opponent's Set Spell/Trap first, then an opponent's face-down monster; its own Set
        /// card only when the mandatory trigger leaves nothing else.
        /// </summary>
        public static CardInstance AutoPickSetCard(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var opp = engine?.OpponentOf(who);
            if (opp != null)
            {
                var st = targets.FirstOrDefault(t => opp.TryFindSpellTrap(t, out _) ||
                                                     ReferenceEquals(opp.FieldSpellZone?.Occupant, t));
                if (st != null) return st;
                var mon = targets.Where(t => opp.TryFindMonster(t, out _))
                    .OrderByDescending(t => t.Def?.atk ?? 0).FirstOrDefault();
                if (mon != null) return mon;
            }

            return targets.OrderBy(t => t.Def?.atk ?? 0).First();
        }

        // ───────────────────────────── Good Goblin Housekeeping ─────────────────────────────

        /// <summary>
        /// Draw (copies of <see cref="EffectClause.NamedCard"/> in the controller's GY, counted now) +
        /// <see cref="EffectClause.Amount"/> cards; then (BreakEffect) the controller selects 1 card in the hand and places it
        /// on the bottom of the Deck. The selection is mandatory: a human picks among 2+ (CANCEL makes the AI's choice, the
        /// least useful card); a lone card goes with no prompt.
        /// </summary>
        internal static void ResolveGoblinHousekeeping(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            var name = clause.NamedCard;
            var named = string.IsNullOrEmpty(name)
                ? 0
                : who.Graveyard.Count(g => g != null &&
                                           (g.IsNamed(name) ||
                                            string.Equals(g.Def?.name, name, StringComparison.OrdinalIgnoreCase)));
            var n = named + Math.Max(0, clause.Amount);
            engine.Log($"{source?.Name}: {named} \"{name}\" in {who.Name}'s GY — {who.Name} draws {n} card(s).");
            engine.Draw(who, n);
            if (engine.GameOver) return;
            if (who.Hand.Count == 0)
            {
                engine.Log($"{source?.Name}: no card in the hand to return to the Deck.");
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => who.Hand.Where(h => h != null).ToList(),
                Kind = EffectTargetKind.DiscardFromHand,
                Prompt = $"{source?.Name}: choose 1 card in your hand to place on the bottom of your Deck.",
                AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, null) ?? list.FirstOrDefault(),
                Finish = c => PlaceHandCardOnBottom(engine, who, source, c)
            });
        }

        static void PlaceHandCardOnBottom(DuelEngine engine, DuelistState who, CardInstance source, CardInstance card)
        {
            if (card == null || !who.Hand.Contains(card))
            {
                engine.Log($"{source?.Name}: no card in the hand to return.");
                return;
            }

            who.Hand.Remove(card);
            // A card held by a player who does not own it goes to its owner's Deck (PerCopyDeck batch).
            var pile = PerCopyDeckRuntime.OwnersPile(who, card);
            pile.Deck.Add(card.CardId);
            engine.Log($"{source?.Name}: {who.Name} places a card from the hand on the bottom of the Deck.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── The Big March of Animals ─────────────────────────────

        /// <summary>
        /// Count the face-up <see cref="EffectClause.RaceFilter"/> monsters the controller controls now (live Type); each of
        /// them gains that count x <see cref="EffectClause.Amount"/> ATK until the end of the turn. The value is fixed: a
        /// monster that arrives or leaves later does not change it. A monster unaffected by the card is counted, not boosted.
        /// </summary>
        internal static void ResolveRaceMarch(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            if (engine == null || who == null || c == null) return;
            var group = who.MonstersOnField()
                .Where(m => m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, c.RaceFilter))
                .ToList();
            if (group.Count == 0)
            {
                engine.Log($"{source?.Name}: {who.Name} controls no face-up {c.RaceFilter} monster.");
                return;
            }

            var gain = group.Count * Math.Max(0, c.Amount);
            foreach (var m in group)
            {
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                {
                    engine.Log($"{m.Name} is unaffected.");
                    continue;
                }

                m.UntilEndOfTurnAtk += gain;
                engine.Log($"{source?.Name}: {m.Name} gains {gain} ATK until the end of this turn (now {m.CurrentAtk}).");
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Elemental Burst ─────────────────────────────

        static readonly ConditionalWeakTable<PendingActivation, List<string>> s_paidAttributes = new();

        static string AttributeOf(CardInstance m) => (m?.Attribute ?? "").Trim().ToUpperInvariant();

        /// <summary>
        /// TextEffectRuntime.PayOneIgnitionCostCore, before a human's Tribute pick leaves the field: remember its live
        /// Attribute for this activation (the pending cost pick), so the next pick offers only the Attributes still missing.
        /// </summary>
        public static void NoteTributeCost(DuelEngine engine, CardInstance source, CardInstance tributed,
            PendingActivation pending)
        {
            if (pending == null || tributed == null) return;
            var c = OneEachClause(source);
            if (c == null) return;
            s_paidAttributes.GetOrCreateValue(pending).Add(AttributeOf(tributed));
        }

        static EffectClause OneEachClause(CardInstance source) =>
            CompiledEffectCache.GetOrCompile(source?.Def)?.ClausesFor(EffectTiming.Activate)
                .FirstOrDefault(x => x != null && !string.IsNullOrEmpty(x.TributeOneEachAttribute));

        /// <summary>
        /// TextEffectRuntime.CollectTributeCost: for a "Tribute 1 each of A, B, C and D" cost keep only the monsters of an
        /// Attribute not yet Tributed for this activation, and none when one of the missing Attributes has no candidate (the
        /// set cannot be completed). One monster of each missing Attribute is ordered first (the AI's lowest-ATK pick), so
        /// the automatic payment of the first N candidates Tributes one of each.
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || string.IsNullOrEmpty(c.TributeOneEachAttribute)) return;
            var needed = c.TributeOneEachAttribute.Split('|').Select(a => a.Trim().ToUpperInvariant())
                .Where(a => a.Length > 0).ToList();
            var pending = engine?.PendingActivation;
            if (pending != null && pending.AwaitingIgnitionCost && ReferenceEquals(pending.Card, source) &&
                s_paidAttributes.TryGetValue(pending, out var paid))
                foreach (var a in paid)
                    needed.Remove(a);
            var keep = list.Where(m => m != null && needed.Contains(AttributeOf(m))).ToList();
            var first = new List<CardInstance>();
            foreach (var a in needed)
            {
                var best = keep.Where(m => AttributeOf(m) == a).OrderBy(m => m.CurrentAtk).FirstOrDefault();
                if (best == null)
                {
                    list.Clear();
                    return;
                }

                first.Add(best);
            }

            list.Clear();
            list.AddRange(first);
            list.AddRange(keep.Where(m => !first.Contains(m)));
        }

        // ── Elemental Burst seated in a response window (the opponent's attack, their open game state) ──

        sealed class SeatTribute
        {
            public EffectClause Clause;
            public ResponseTiming Timing;
            public readonly List<CardInstance> Chosen = new();
        }

        static readonly ConditionalWeakTable<PendingActivation, SeatTribute> s_seatPicks = new();

        /// <summary>The clause whose Tribute cost is one monster of each listed Attribute (Elemental Burst), or null.</summary>
        public static EffectClause OneEachTributeClause(IEnumerable<EffectClause> clauses) =>
            clauses?.FirstOrDefault(c => c != null && c.RequiresTributeCount > 0 &&
                                         !string.IsNullOrEmpty(c.TributeOneEachAttribute));

        /// <summary>The monsters still offered: no chosen one, none of an Attribute already chosen.</summary>
        static List<CardInstance> SeatCandidates(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            List<CardInstance> chosen)
        {
            var taken = chosen.Select(AttributeOf).ToList();
            return TextEffectRuntime.CollectTributeCost(who, card, c, engine)
                .Where(m => m != null && who.TryFindMonster(m, out _) && !chosen.Contains(m) &&
                            !taken.Contains(AttributeOf(m)))
                .ToList();
        }

        /// <summary>
        /// TextEffectRuntime.SeatResponseOnChain, once the link is added and the card is face-up: pay the Tributes now (the
        /// seated resolution pays no cost). The AI Tributes one of each together and false is returned (the window opens as
        /// usual); a human taps them one by one (true: the window opens once the last is picked). Nothing is Tributed
        /// until the set is complete, so CANCEL (<see cref="TryCancelPick"/>) leaves nothing to refund: the activation is
        /// undone and the window re-offered by SpellTrapEffects.CancelPending.
        /// </summary>
        internal static bool BeginSeatTributeCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> clauses, bool human, ResponseTiming timing, CardInstance summoned, int paidLp)
        {
            var c = OneEachTributeClause(clauses);
            if (c == null || engine == null || who == null || card == null) return false;
            var candidates = SeatCandidates(engine, who, card, c, new List<CardInstance>());
            if (!human || !engine.IsHumanControlled(who))
            {
                PayTributes(engine, who, card, candidates.Take(c.RequiresTributeCount).ToList());
                return false;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                UsesTextProgram = true,
                ChainActivation = true,
                SeatTiming = timing,
                SeatSummoned = summoned,
                PaidLpCost = paidLp,
                TargetKind = EffectTargetKind.TributeMonsterYouControl,
                PromptOverride = SeatPrompt(c, 0)
            };
            pending.LegalTargets.AddRange(candidates);
            s_seatPicks.Add(pending, new SeatTribute { Clause = c, Timing = timing });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static string SeatPrompt(EffectClause c, int picked) =>
            $"Tribute 1 each of {c.TributeOneEachAttribute.Replace("|", ", ")} monsters (cost; {picked}/" +
            $"{c.RequiresTributeCount} picked, Tributed together). CANCEL cancels the activation.";

        /// <summary>SpellTrapEffects.TryResolveWithTarget: a seated Elemental Burst's Tribute pick.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_seatPicks.TryGetValue(p, out var st)) return false;
            var chosen = engine.ResolveLegalEffectTarget(target) ?? target;
            if (!p.LegalTargets.Contains(chosen))
            {
                engine.Log("Pick one of the highlighted monsters to Tribute.");
                return true;
            }

            st.Chosen.Add(chosen);
            if (st.Chosen.Count < st.Clause.RequiresTributeCount)
            {
                var more = SeatCandidates(engine, p.Controller, p.Card, st.Clause, st.Chosen);
                if (more.Count > 0)
                {
                    p.LegalTargets.Clear();
                    p.LegalTargets.AddRange(more);
                    p.PromptOverride = SeatPrompt(st.Clause, st.Chosen.Count);
                    engine.Log(p.Prompt);
                    engine.NotifyPublic();
                    ok = true;
                    return true;
                }
            }

            s_seatPicks.Remove(p);
            engine.ClearPendingActivation();
            PayTributes(engine, p.Controller, p.Card, st.Chosen);
            ok = TextEffectRuntime.OpenSeatedResponseChainWindow(engine, p.Controller, st.Timing);
            return true;
        }

        /// <summary>The cost's Tributes, at the same time (their GY triggers activate once all are paid).</summary>
        static void PayTributes(DuelEngine engine, DuelistState who, CardInstance card, List<CardInstance> picks)
        {
            BattleWindowsRuntime.BeginCost(engine);
            try
            {
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    using var together = RedirectAndReplaceRuntime.EnterEffectApplication(engine);
                    foreach (var m in picks)
                    {
                        if (m == null || !who.TryFindMonster(m, out _)) continue;
                        ReleaseEvents.Record(engine, m, who); // EngineHygiene: EVENT_RELEASE
                        engine.SendCardToGrave(engine.ControllerOf(m) ?? who, m);
                        engine.Log($"Cost: Tribute {m.Name} ({card?.Name}).");
                    }
                });
            }
            finally
            {
                BattleWindowsRuntime.EndCost(engine);
            }
        }
    }
}
