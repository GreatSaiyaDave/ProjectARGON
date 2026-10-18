using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleSpellTraps" (v160 LOD) runtime. Reads compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>A Wingbeat of Giant Dragon</b> (<see cref="EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps"/>):
    /// at resolution the controller selects (not targets) 1 face-up Level N+ monster of the Type they control (a human
    /// through a held pick, <see cref="PendingActivation.HoldsChainResolution"/>); only if it reached the hand, every
    /// other Spell/Trap on the field is destroyed at once. Activation: <see cref="OwnRaceLevelMonsterAndOtherSpellTrap"/>.</item>
    /// <item><b>Burst Breath</b> (<see cref="EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk"/>): the Tribute
    /// cost may only be a monster of the Type for which another face-up monster has DEF &lt;= its ATK (Lua cfilter,
    /// <see cref="FilterTributeCostCandidates"/>); the cost numeric (its ATK on the field) is the DEF cap. A free-chain
    /// Trap: activated in a response window (<see cref="BeginSeatCost"/>) or chained (<see cref="BeginChainAnswerCost"/>)
    /// it Tributes as it is activated, and the Tributed ATK is kept for the resolution (<see cref="TakePaidTribute"/>).</item>
    /// <item><b>Dragon's Gunfire</b>: "Activate 1 of these effects" with a targeted bullet. The bullet is chosen as the
    /// card is activated (<see cref="OfferTargetedBullet"/>, the Poison of the Old Man tokens); a bullet that cannot be
    /// used is not offered, and only the chosen one goes on (<see cref="KeepChosenBullet"/>). The destroy bullet
    /// (<see cref="EffectActionKind.DestroyTargetWithDefAtMost"/>) re-checks face-up and DEF at resolution.</item>
    /// <item><b>Ready for Intercepting</b>: Book of Moon's action with a two-Type filter ("Warrior|Spellcaster"); the
    /// legacy target mirror filters it too (<see cref="FilterLegacyFaceDownTargets"/>).</item>
    /// <item><b>Fiend Comedian</b> (<see cref="EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy"/>): the coin is
    /// called at resolution (a human through <see cref="PendingActivation.AwaitingCoinCall"/>, holding the chain; the AI
    /// calls Heads). Activation: <see cref="OppGyCardsAndDeckCovers"/>.</item>
    /// <item><b>Emergency Provisions</b> (<see cref="EffectActionKind.GainLpPerSpellTrapSentAsCost"/>): the cost — 1 to 5
    /// other Spell/Trap Cards you control — is sent as the card is activated: on its own activation
    /// (<see cref="TryBeginSendCost"/>), as a chain answer (<see cref="BeginChainAnswerCost"/>) or in another response
    /// window (<see cref="BeginSeatCost"/>). A human taps cards one by one and CANCEL finishes (with none picked, CANCEL
    /// undoes the activation). The count is stored on the card and read when the effect resolves.</item>
    /// <item><b>The A. Forces</b>: a <see cref="EffectActionKind.ContinuousGainAtkDef"/> aura whose Amount is multiplied by
    /// the live count of <see cref="FaceUpRaceYouControl"/> (<see cref="AuraMultiplier"/>).</item>
    /// </list>
    /// </summary>
    public static class SimpleSpellTrapsRuntime
    {
        // ── ActivationCondition values ──
        public const string OwnRaceLevelMonsterAndOtherSpellTrap = "OwnRaceLevelMonsterAndOtherSpellTrap";
        public const string OppGyCardsAndDeckCovers = "OppGyCardsAndDeckCovers";
        public const string OtherSpellTrapYouControl = "OtherSpellTrapYouControl";

        // ── CountSource value ──
        /// <summary>Face-up monsters the aura's controller controls whose Type is in <see cref="EffectClause.CountRaceFilter"/>.</summary>
        public const string FaceUpRaceYouControl = "FaceUpRaceYouControl";

        /// <summary>Emergency Provisions: at most this many cards are sent (Lua SelectMatchingCard 1, 5).</summary>
        const int MaxProvisionsSends = 5;

        enum PickKind { WingbeatReturn, FiendComedianCall, ProvisionsCost, BurstTributeCost }

        sealed class Pick
        {
            public PickKind Kind;
            public EffectClause Clause;
            public readonly List<CardInstance> Chosen = new();
            /// <summary>Provisions answering a chain (ActivateFreeChainOnChain): the opponent's window opens after.</summary>
            public bool ChainAnswer;
            /// <summary>A cost paid as the card is seated in this response window (TextEffectRuntime.SeatResponseOnChain).</summary>
            public ResponseTiming? Seat;
        }

        static readonly ConditionalWeakTable<PendingActivation, Pick> s_picks = new();

        sealed class Paid
        {
            public int Count;
            /// <summary>The cost was just paid by a human pick: the next activation pass goes on to the effect.</summary>
            public bool JustPaid;
            /// <summary>Burst Breath chained / seated: its Tribute is paid and <see cref="TributeAtk"/> is the DEF cap.</summary>
            public bool TributePaid;
            public int TributeAtk;
        }

        static readonly ConditionalWeakTable<CardInstance, Paid> s_paid = new();

        // ───────────────────────────── activation conditions ─────────────────────────────

        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c, out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null) return false;
            switch (c.ActivationCondition)
            {
                case OwnRaceLevelMonsterAndOtherSpellTrap:
                    ok = WingbeatCandidates(engine, who, c).Count > 0 && OtherSpellTrapsOnField(engine, card).Count > 0;
                    reason = $"Needs a face-up Level {c.ConditionAmount} or higher {c.ConditionName} monster you control " +
                             "and another Spell/Trap Card on the field.";
                    return true;
                case OppGyCardsAndDeckCovers:
                {
                    var opp = engine?.OpponentOf(who);
                    var n = opp?.Graveyard?.Count ?? 0;
                    // ForcedActivationAndSubstitution batch (v163): a card it can banish (Spirit Elimination on the
                    // opponent: their GY Spells/Traps, or a monster they control).
                    var banishable = opp?.Graveyard?.ToList() ?? new List<CardInstance>();
                    SpiritEliminationRuntime.SubstituteInList(engine, banishable, null, false, null, false, opp);
                    ok = banishable.Count > 0 && (who?.Deck?.Count ?? 0) >= n;
                    reason = "Needs a card in your opponent's GY and at least that many cards in your Deck.";
                    return true;
                }
                case OtherSpellTrapYouControl:
                    ok = ProvisionsCandidates(who, card).Count > 0;
                    reason = "Needs another Spell/Trap Card you control to send to the GY.";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── The A. Forces ─────────────────────────────

        /// <summary>
        /// The factor of a <see cref="EffectActionKind.ContinuousGainAtkDef"/> aura: 1 without a
        /// <see cref="EffectClause.CountSource"/> this batch knows, else the live count.
        /// </summary>
        public static int AuraMultiplier(DuelEngine engine, DuelistState controller, EffectClause clause)
        {
            if (clause == null || clause.CountSource != FaceUpRaceYouControl) return 1;
            if (controller == null) return 0;
            var n = 0;
            foreach (var m in controller.MonstersOnField())
                if (m?.Def != null && m.FaceUp && ClassicEraTemplates.RaceMatches(m.Race, clause.CountRaceFilter))
                    n++;
            return n;
        }

        // ───────────────────────────── A Wingbeat of Giant Dragon ─────────────────────────────

        static List<CardInstance> WingbeatCandidates(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || c == null) return list;
            foreach (var m in who.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp || m.IsToken) continue; // a Token cannot be returned to the hand
                if (!ClassicEraTemplates.RaceMatches(m.Race, c.ConditionName)) continue;
                if (m.Level < c.ConditionAmount) continue;
                list.Add(m);
            }

            return list;
        }

        static List<CardInstance> OtherSpellTrapsOnField(DuelEngine engine, CardInstance except)
        {
            var list = new List<CardInstance>();
            if (engine == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    if (st != null && !ReferenceEquals(st, except))
                        list.Add(st);
            }

            return list;
        }

        internal static void ResolveWingbeat(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var candidates = WingbeatCandidates(engine, who, c);
            if (candidates.Count == 0)
            {
                engine.Log($"{source?.Name}: no Level {c.ConditionAmount} or higher {c.ConditionName} monster to return — " +
                           "nothing happens.");
                return;
            }

            if (candidates.Count > 1 && engine.IsHumanControlled(who) && engine.PendingActivation == null)
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    HoldsChainResolution = true,
                    TargetKind = EffectTargetKind.YourMonster,
                    TargetPicksRemaining = 1,
                    PromptOverride = $"return 1 Level {c.ConditionAmount} or higher {c.ConditionName} monster you " +
                                     "control to the hand (then every Spell/Trap Card on the field is destroyed)."
                };
                pending.LegalTargets.AddRange(candidates);
                s_picks.Add(pending, new Pick { Kind = PickKind.WingbeatReturn, Clause = c });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            // The AI (or a lone choice): the weakest one goes back.
            FinishWingbeat(engine, who, source, candidates.OrderBy(m => m.CurrentAtk).First());
        }

        static void FinishWingbeat(DuelEngine engine, DuelistState who, CardInstance source, CardInstance monster)
        {
            if (monster == null) return;
            if (ContinuousProtections.IsUnaffectedBy(engine, monster, source))
            {
                engine.Log($"{monster.Name} is unaffected by {source?.Name} — nothing is destroyed.");
                return;
            }

            engine.ReturnCardToHand(monster, by: who);
            var inHand = (engine.Player?.Hand?.Contains(monster) ?? false) ||
                         (engine.Opponent?.Hand?.Contains(monster) ?? false);
            if (!inHand)
            {
                engine.Log($"{source?.Name}: {monster.Name} did not reach the hand — nothing is destroyed.");
                return;
            }

            var sts = OtherSpellTrapsOnField(engine, source);
            // Destroyed together (FlipMassEffects: their GY triggers activate once all are in the GY).
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var st in sts)
                    TextEffectRuntime.DestroyCard(engine, st, source, false, who);
            });
        }

        // ───────────────────────────── Burst Breath ─────────────────────────────

        /// <summary>
        /// CollectTributeCost: Burst Breath's Tribute is a monster of exactly the Type (not a name containing it) for
        /// which another face-up monster on the field has DEF &lt;= its ATK (Lua s.cfilter). The AI's best pick first.
        /// </summary>
        public static void FilterTributeCostCandidates(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> list)
        {
            if (c == null || list == null || c.Action != EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk) return;
            list.RemoveAll(m => m == null || !ClassicEraTemplates.RaceMatches(m.Race, c.TributeRaceFilter));
            if (engine == null) return;
            list.RemoveAll(m => !BurstVictims(engine, m.CurrentAtk, except: m).Any());
            if (who == null || list.Count < 2) return;
            var opp = engine.OpponentOf(who);
            int Score(CardInstance m) =>
                BurstVictims(engine, m.CurrentAtk, except: m)
                    .Sum(v => opp != null && opp.TryFindMonster(v, out _) ? 1 : -1);
            var ordered = list.OrderByDescending(Score).ThenBy(m => m.CurrentAtk).ToList();
            list.Clear();
            list.AddRange(ordered);
        }

        static IEnumerable<CardInstance> BurstVictims(DuelEngine engine, int atk, CardInstance except)
        {
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (m?.Def != null && m.FaceUp && !ReferenceEquals(m, except) && m.CurrentDef <= atk)
                        yield return m;
            }
        }

        internal static void ResolveBurstBreath(DuelEngine engine, DuelistState who, CardInstance source, int tributedAtk)
        {
            var victims = BurstVictims(engine, tributedAtk, except: null).ToList();
            engine.Log($"{source?.Name}: destroy every face-up monster with {tributedAtk} or less DEF ({victims.Count}).");
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var m in victims)
                    TextEffectRuntime.DestroyCard(engine, m, source, false, who);
            });
        }

        // ───────────────────────────── Dragon's Gunfire ─────────────────────────────

        /// <summary>The end of TextEffectRuntime.CollectTargets: a "monster with N or less DEF" target is face-up.</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null) return;
            if (c.Action == EffectActionKind.DestroyTargetWithDefAtMost)
                list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster || !t.FaceUp || t.CurrentDef > c.Amount);
        }

        internal static void ResolveDestroyTargetWithDefAtMost(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause c, CardInstance target)
        {
            if (target == null) return;
            var onField = (engine.Player?.TryFindMonster(target, out _) ?? false) ||
                          (engine.Opponent?.TryFindMonster(target, out _) ?? false);
            if (!onField || !target.FaceUp || target.CurrentDef > c.Amount)
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer a face-up monster with {c.Amount} or less DEF — " +
                           "not destroyed.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log($"{target.Name} is protected at resolution.");
                return;
            }

            TextEffectRuntime.DestroyCard(engine, target, source, false, who);
        }

        static bool IsActivateBullet(EffectClause c) =>
            c != null && c.EffectChoiceIndex > 0 && c.Timing == EffectTiming.Activate;

        /// <summary>"Activate 1 of these effects" where a bullet takes a target (Dragon's Gunfire).</summary>
        public static bool HasTargetedBullet(IEnumerable<EffectClause> activate) =>
            activate != null && activate.Any(c => IsActivateBullet(c) && c.RequiresTargetChoice);

        /// <summary>
        /// CanActivate: a targeted bullet with no legal target does not block the card while another bullet without a
        /// target can be chosen instead.
        /// </summary>
        public static bool TargetedBulletHasAlternative(IEnumerable<EffectClause> activate, EffectClause c) =>
            IsActivateBullet(c) && c.RequiresTargetChoice && activate != null &&
            activate.Any(o => !ReferenceEquals(o, c) && IsActivateBullet(o) && !o.RequiresTargetChoice);

        /// <summary>
        /// TryResolveActivation, before the targets: choose the bullet. The AI (or a lone usable bullet) is remembered
        /// and the caller goes on; a human with two usable bullets gets the bullet tokens
        /// (<see cref="PoisonMeteorCestusRuntime.OfferInlineBullet"/>) and true is returned. A bullet whose target is
        /// missing is not offered.
        /// </summary>
        public static bool OfferTargetedBullet(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> activate, bool autoPick)
        {
            if (!HasTargetedBullet(activate) || PoisonMeteorCestusRuntime.HasChosenBullet(card)) return false;
            var usable = new List<(EffectClause c, List<CardInstance> targets)>();
            foreach (var c in activate.Where(IsActivateBullet).OrderBy(c => c.EffectChoiceIndex))
            {
                if (!c.RequiresTargetChoice)
                {
                    usable.Add((c, null));
                    continue;
                }

                var targets = TextEffectRuntime.CollectChoiceTargets(engine, who, c, card);
                if (targets.Count > 0) usable.Add((c, targets));
            }

            if (usable.Count == 0) return false;
            if (usable.Count == 1)
            {
                PoisonMeteorCestusRuntime.RememberBullet(card, usable[0].c.EffectChoiceIndex);
                return false;
            }

            if (autoPick || !engine.IsHumanControlled(who))
            {
                // The AI destroys an opponent's monster when it can, otherwise it takes the bullet without a target.
                var opp = engine.OpponentOf(who);
                var pickBullet = usable.FirstOrDefault(u =>
                    u.targets != null && opp != null &&
                    opp.TryFindMonster(TextEffectRuntime.AutoPickChoiceTarget(engine, who, u.c, u.targets), out _)).c;
                pickBullet ??= usable.FirstOrDefault(u => u.targets == null).c ?? usable[0].c;
                PoisonMeteorCestusRuntime.RememberBullet(card, pickBullet.EffectChoiceIndex);
                return false;
            }

            return PoisonMeteorCestusRuntime.OfferInlineBullet(engine, who, card, activate, fromHand, autoPick: false);
        }

        /// <summary>Only the chosen bullet (and the clauses outside the choice) go on.</summary>
        public static List<EffectClause> KeepChosenBullet(CardInstance card, List<EffectClause> activate)
        {
            if (!HasTargetedBullet(activate)) return activate;
            var chosen = PoisonMeteorCestusRuntime.ChosenBullet(card);
            return activate.Where(c => c != null && (!IsActivateBullet(c) || c.EffectChoiceIndex == chosen)).ToList();
        }

        // ───────────────────────────── Ready for Intercepting (legacy mirror) ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.CollectLegalTargets (AnyMonsterOnField): a Type-filtered "change that target to face-down
        /// Defense Position" (Ready for Intercepting) offers only face-up monsters of those Types that are not Tokens,
        /// like TextEffectRuntime.CollectTargets.
        /// </summary>
        public static void FilterLegacyFaceDownTargets(CardInstance card, List<CardInstance> list)
        {
            if (card?.Def == null || list == null) return;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return;
            var c = prog.ClausesFor(EffectTiming.Activate).FirstOrDefault(x =>
                x != null && x.RequiresTargetChoice && x.Action == EffectActionKind.SetTargetFaceDownDefense &&
                !string.IsNullOrEmpty(x.RaceFilter));
            if (c == null) return;
            list.RemoveAll(t => t == null || !t.FaceUp || t.IsToken || !ClassicEraTemplates.RaceMatches(t.Race, c.RaceFilter));
        }

        // ───────────────────────────── Fiend Comedian ─────────────────────────────

        internal static void ResolveFiendComedian(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine.IsHumanControlled(who) && engine.PendingActivation == null)
            {
                var pending = new PendingActivation
                {
                    Controller = who,
                    Card = source,
                    UsesTextProgram = true,
                    AwaitingCoinCall = true,
                    HoldsChainResolution = true,
                    PromptOverride = "call the coin toss — right: banish every card in your opponent's GY; wrong: send " +
                                     "that many cards from the top of your Deck to the GY."
                };
                s_picks.Add(pending, new Pick { Kind = PickKind.FiendComedianCall });
                engine.SetPendingActivation(pending);
                engine.Log(pending.Prompt);
                engine.NotifyPublic();
                return;
            }

            FinishFiendComedian(engine, who, source, callHeads: true);
        }

        static void FinishFiendComedian(DuelEngine engine, DuelistState who, CardInstance source, bool callHeads)
        {
            var opp = engine.OpponentOf(who);
            // A coin-toss event (CoinDiceInterrupts batch): Second Coin Toss may redo it before the call is judged.
            CoinDiceInterruptsRuntime.TossCoin(engine, who, source, t => t == callHeads, toss =>
            {
                engine.Log($"{who?.Name} calls {(callHeads ? "Heads" : "Tails")}; toss is {(toss ? "Heads" : "Tails")}.");
                if (opp == null || who == null) return;
                if (toss == callHeads)
                {
                    engine.Log($"{source?.Name}: right call — every card in {opp.Name}'s GY is banished.");
                    // ForcedActivationAndSubstitution batch (v163): Spirit Elimination on the opponent — their GY
                    // monsters stay and every monster they control is banished instead (Lua s.rmfilter, SpElimFilter).
                    var all = opp.Graveyard.ToList();
                    SpiritEliminationRuntime.SubstituteInList(engine, all, null, false, null, false, opp);
                    foreach (var g in all)
                        engine.BanishCard(engine.ControllerOf(g) ?? opp, g, by: who);
                    return;
                }

                var n = opp.Graveyard.Count;
                engine.Log($"{source?.Name}: wrong call — {who.Name} sends {n} card(s) from the top of the Deck to the GY.");
                if (n <= 0) return;
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    TextEffectRuntime.SendTopOfDeckToGy(engine, who, n, byEffectOf: who);
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
                }
            });
        }

        /// <summary>DuelEngine.TrySelectCoinCall: Fiend Comedian's call. False for any other coin call.</summary>
        public static bool TryFinishCoinCall(DuelEngine engine, bool callHeads)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick) || pick.Kind != PickKind.FiendComedianCall)
                return false;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            FinishFiendComedian(engine, p.Controller, p.Card, callHeads);
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Emergency Provisions ─────────────────────────────

        /// <summary>Other Spell/Trap Cards <paramref name="who"/> controls (Set ones and the Field Zone too).</summary>
        static List<CardInstance> ProvisionsCandidates(DuelistState who, CardInstance card)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            foreach (var st in who.SpellTrapsOnField())
                if (st != null && !ReferenceEquals(st, card))
                    list.Add(st);
            return list;
        }

        static EffectClause ProvisionsClause(IEnumerable<EffectClause> clauses) =>
            clauses?.FirstOrDefault(c => c != null && c.Action == EffectActionKind.GainLpPerSpellTrapSentAsCost);

        /// <summary>
        /// The clause of this batch whose cost is paid as the card is activated in a response window or chained
        /// (<see cref="BeginSeatCost"/>, <see cref="BeginChainAnswerCost"/>): Emergency Provisions' sends, Burst Breath's
        /// Tribute. TextEffectRuntime.CanActivate checks that cost (IgnitionCostLegal) before a window offers the card.
        /// </summary>
        public static EffectClause ResponseCostClause(IEnumerable<EffectClause> clauses) =>
            clauses?.FirstOrDefault(c => c != null &&
                (c.Action == EffectActionKind.GainLpPerSpellTrapSentAsCost || PaysTributeWhenChained(c)));

        /// <summary>
        /// TextEffectRuntime.CanActivateFreeChainOnChain: Burst Breath's Tribute is paid as it is chained
        /// (<see cref="BeginChainAnswerCost"/>), so a chain window does not refuse it as a cost needing a pick.
        /// </summary>
        public static bool PaysTributeWhenChained(EffectClause c) =>
            c != null && c.RequiresTributeCount == 1 &&
            (c.Action == EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk ||
             // DeckSummonsAndSarcophagi batch: Ninjitsu Art of Transformation (a free-chain Continuous Trap) records the
             // Tributed Level the same way (TributeSummonsRuntime.TributeCostNumeric).
             DeckSummonsAndSarcophagiRuntime.RecordsTributedLevel(c));

        /// <summary>
        /// TryResolveActivation, as the card is activated: pay Emergency Provisions' send cost. Returns true when a human
        /// is now picking (the activation goes on when the picks are done). The AI pays at once and false is returned, as
        /// it is once the cost of this activation has been paid. Never pays while a chain resolves.
        /// </summary>
        public static bool TryBeginSendCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> activate, bool autoPick)
        {
            var c = ProvisionsClause(activate);
            if (c == null || engine == null || card == null || engine.IsResolvingChain) return false;
            var paid = s_paid.GetOrCreateValue(card);
            if (paid.JustPaid)
            {
                paid.JustPaid = false;
                return false;
            }

            return BeginCost(engine, who, card, fromHand, c, autoPick, chainAnswer: false);
        }

        /// <summary>
        /// TextEffectRuntime.ActivateFreeChainOnChain, once the link is added: pay the send cost. True = a human is now
        /// picking (the opponent's window opens when the picks are done); false = paid (AI) or not this card.
        /// </summary>
        public static bool BeginChainAnswerCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            CompiledCardProgram prog, bool autoPick)
        {
            var c = ResponseCostClause(prog?.ClausesFor(EffectTiming.Activate));
            if (c == null || engine == null || card == null) return false;
            return PaysTributeWhenChained(c)
                ? BeginTributeCost(engine, who, card, fromHand, c, autoPick, chainAnswer: true, seat: null)
                : BeginCost(engine, who, card, fromHand, c, autoPick, chainAnswer: true);
        }

        /// <summary>
        /// TextEffectRuntime.SeatResponseOnChain (an attack declaration, the opponent's open game state …), once the link
        /// is added and the card is face-up: pay this batch's cost. True = a human is now picking (the opponent's window
        /// opens when the picks are done; CANCEL with nothing picked undoes the activation and re-offers the window);
        /// false = paid (AI) or not this card.
        /// </summary>
        public static bool BeginSeatCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand,
            List<EffectClause> clauses, bool human, ResponseTiming timing)
        {
            var c = ResponseCostClause(clauses);
            if (c == null || engine == null || card == null) return false;
            return PaysTributeWhenChained(c)
                ? BeginTributeCost(engine, who, card, fromHand, c, !human, chainAnswer: false, seat: timing)
                : BeginCost(engine, who, card, fromHand, c, !human, chainAnswer: false, seat: timing);
        }

        static bool BeginCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, EffectClause c,
            bool autoPick, bool chainAnswer, ResponseTiming? seat = null)
        {
            var candidates = ProvisionsCandidates(who, card);
            if (autoPick || !engine.IsHumanControlled(who))
            {
                PaySendCost(engine, who, card, AiProvisionsPicks(engine, who, candidates));
                return false;
            }

            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = !fromHand,
                UsesTextProgram = true,
                ChainActivation = chainAnswer || seat.HasValue,
                SeatTiming = seat ?? ResponseTiming.None,
                TargetKind = EffectTargetKind.SpellTrapOnField,
                TargetPicksRemaining = System.Math.Min(MaxProvisionsSends, candidates.Count),
                TargetPicksAreUpTo = true
            };
            pending.LegalTargets.AddRange(candidates);
            var pick = new Pick { Kind = PickKind.ProvisionsCost, Clause = c, ChainAnswer = chainAnswer, Seat = seat };
            pending.PromptOverride = ProvisionsPrompt(c, pick);
            s_picks.Add(pending, pick);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static string ProvisionsPrompt(EffectClause c, Pick pick) =>
            pick.Chosen.Count == 0
                ? $"send 1 or more other Spell/Trap Cards you control to the GY (cost; {c.Amount} LP each). Tap a card; " +
                  "CANCEL with none picked cancels the activation."
                : $"{pick.Chosen.Count} picked ({string.Join(", ", pick.Chosen.Select(x => x.Name))}). Tap another card " +
                  "to send, or CANCEL to send these.";

        /// <summary>
        /// The AI sends the cards about to be destroyed: every other Spell/Trap when an opposing Chain Link destroys
        /// Spell/Traps without a target, else the ones an opposing Chain Link targets, else one card that does nothing
        /// where it is (<see cref="AiSendOrder"/>): a spent face-up card first, then a Set card, an active Continuous /
        /// Field / Equip card last.
        /// </summary>
        static List<CardInstance> AiProvisionsPicks(DuelEngine engine, DuelistState who, List<CardInstance> candidates)
        {
            var picks = new List<CardInstance>();
            if (candidates.Count == 0) return picks;
            var threatened = new List<CardInstance>();
            var sweep = false;
            foreach (var link in engine.Chain.Links)
            {
                if (link == null || link.Resolved || link.Controller == who) continue;
                foreach (var t in link.Targets.Append(link.Target))
                    if (t != null && candidates.Contains(t) && !threatened.Contains(t))
                        threatened.Add(t);
                var prog = CompiledEffectCache.GetOrCompile(link.Card?.Def);
                if (prog != null && prog.ClausesFor(EffectTiming.Activate).Any(x =>
                        x != null && !x.RequiresTargetChoice && x.Action == EffectActionKind.Destroy &&
                        x.Zone == EffectZoneFilter.FieldSpellTraps && x.Side != EffectSide.Controller))
                    sweep = true;
            }

            if (sweep) picks.AddRange(candidates);
            else if (threatened.Count > 0) picks.AddRange(threatened);
            else picks.Add(candidates.OrderBy(AiSendOrder).First());
            if (picks.Count > MaxProvisionsSends) picks.RemoveRange(MaxProvisionsSends, picks.Count - MaxProvisionsSends);
            return picks;
        }

        /// <summary>0 = a face-up card whose activation is spent (a Normal Spell/Trap still on the field), 1 = a Set card,
        /// 2 = a face-up card that keeps working (Continuous, Field, Equip).</summary>
        static int AiSendOrder(CardInstance st)
        {
            if (st == null || !st.FaceUp) return 1;
            var def = st.Def;
            return def != null && (def.IsContinuousSpellOrTrap || def.IsFieldSpell || def.IsEquipSpell) ? 2 : 0;
        }

        static void PaySendCost(DuelEngine engine, DuelistState who, CardInstance card, List<CardInstance> picks)
        {
            var sent = 0;
            // Sent at the same time (their GY triggers activate once all are in the GY).
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var st in picks)
                {
                    if (st == null || !who.TryFindSpellTrap(st, out _) &&
                        (who.FieldSpellZone == null || !ReferenceEquals(who.FieldSpellZone.Occupant, st)))
                        continue;
                    engine.SendCardToGrave(who, st);
                    engine.Log($"Cost: send {st.Name} to the GY.");
                    sent++;
                }
            });
            s_paid.GetOrCreateValue(card).Count = sent;
            engine.Log($"{card.Name}: {sent} card(s) sent to the GY.");
        }

        /// <summary>The ApplyClause arm: gain Amount LP per card sent as this activation's cost.</summary>
        internal static void ResolveProvisions(DuelEngine engine, DuelistState who, CardInstance source, EffectClause c)
        {
            var n = 0;
            if (source != null && s_paid.TryGetValue(source, out var paid))
            {
                n = paid.Count;
                paid.Count = 0;
            }

            var gain = n * System.Math.Max(0, c.Amount);
            if (gain <= 0)
            {
                engine.Log($"{source?.Name}: no card was sent — no LP gained.");
                return;
            }

            TextEffectRuntime.ApplyLifePointGain(engine, who, gain, source?.Name);
        }

        static bool ContinueProvisionsPick(DuelEngine engine, PendingActivation p, Pick pick, CardInstance target)
        {
            if (!p.LegalTargets.Contains(target) || pick.Chosen.Contains(target))
            {
                engine.Log("Pick one of the highlighted Spell/Trap Cards.");
                return true;
            }

            pick.Chosen.Add(target);
            p.LegalTargets.Remove(target);
            p.TargetPicksRemaining = System.Math.Max(0, System.Math.Min(MaxProvisionsSends, pick.Chosen.Count +
                p.LegalTargets.Count) - pick.Chosen.Count);
            if (pick.Chosen.Count < MaxProvisionsSends && p.LegalTargets.Count > 0)
            {
                p.PromptOverride = ProvisionsPrompt(pick.Clause, pick);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return true;
            }

            return FinishProvisionsPick(engine, p, pick);
        }

        static bool FinishProvisionsPick(DuelEngine engine, PendingActivation p, Pick pick)
        {
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            var who = p.Controller;
            var card = p.Card;
            PaySendCost(engine, who, card, pick.Chosen);
            if (pick.ChainAnswer || pick.Seat.HasValue)
                return OpenWindowAfterCost(engine, who, pick);

            s_paid.GetOrCreateValue(card).JustPaid = true;
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            return TextEffectRuntime.TryResolveActivation(engine, who, card, p.FromHand, prog, false);
        }

        /// <summary>
        /// The link is on the chain and its cost paid: the opponent may respond to it (a seated response: the window it
        /// answered is answered, TextEffectRuntime.OpenSeatedResponseChainWindow; a chain answer: ActivateFreeChainOnChain's
        /// tail).
        /// </summary>
        static bool OpenWindowAfterCost(DuelEngine engine, DuelistState who, Pick pick)
        {
            if (pick.Seat.HasValue)
                return TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, pick.Seat.Value);
            engine.ClearPendingResponse();
            if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
            {
                engine.Chain.StartResolution();
                engine.ResolveChainStackForActivation();
            }
            else
                engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── Burst Breath: Tribute paid as it is chained / seated ─────────────────────────────

        /// <summary>
        /// Burst Breath activated as a chain answer or in a response window: Tribute now (Lua s.cost). The AI Tributes its
        /// best candidate (<see cref="FilterTributeCostCandidates"/> orders them); a human picks (CANCEL undoes the
        /// activation). True = a human is now picking.
        /// </summary>
        static bool BeginTributeCost(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, EffectClause c,
            bool autoPick, bool chainAnswer, ResponseTiming? seat)
        {
            var candidates = TextEffectRuntime.CollectTributeCost(who, card, c, engine)
                .FindAll(m => m != null && who.TryFindMonster(m, out _));
            if (candidates.Count == 0)
            {
                engine.Log($"{card.Name}: no monster to Tribute.");
                return false;
            }

            if (autoPick || !engine.IsHumanControlled(who))
            {
                PayTribute(engine, who, card, c, candidates[0]);
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
                SeatTiming = seat ?? ResponseTiming.None,
                TargetKind = EffectTargetKind.TributeMonsterYouControl,
                PromptOverride = $"Tribute 1 {(string.IsNullOrEmpty(c.TributeRaceFilter) ? (c.TributeFaceUpOnly ? "face-up " : "") + c.TributeSeriesName : c.TributeRaceFilter)} " +
                                 "monster (cost). CANCEL cancels the activation."
            };
            pending.LegalTargets.AddRange(candidates);
            s_picks.Add(pending, new Pick { Kind = PickKind.BurstTributeCost, Clause = c, ChainAnswer = chainAnswer, Seat = seat });
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        static void PayTribute(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c, CardInstance m)
        {
            var atk = TributeSummonsRuntime.TributeCostNumeric(c, m, m.CurrentAtk);
            // EngineHygiene: EVENT_RELEASE (Super Rejuvenation counts a Dragon Tributed for Burst Breath; v160 audit).
            ReleaseEvents.Record(engine, m, who);
            engine.SendCardToGrave(engine.ControllerOf(m) ?? who, m);
            engine.Log($"Cost: Tribute {m.Name}.");
            var paid = s_paid.GetOrCreateValue(card);
            paid.TributePaid = true;
            paid.TributeAtk = atk;
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveActivation / TryResolveSeatedResponse: the Tribute Burst Breath paid when it was
        /// chained or seated; its ATK is the DEF cap. Only a resolving Chain Link takes it: an activation of its own pays
        /// its Tribute itself, and a value left by a link that never resolved (negated) is forgotten there.
        /// </summary>
        public static bool TakePaidTribute(DuelEngine engine, CardInstance card, IEnumerable<EffectClause> clauses,
            out int tributedAtk)
        {
            tributedAtk = 0;
            if (card == null || clauses == null || !clauses.Any(PaysTributeWhenChained)) return false;
            if (!s_paid.TryGetValue(card, out var paid) || !paid.TributePaid) return false;
            paid.TributePaid = false;
            if (engine == null || !engine.IsResolvingChain) return false;
            tributedAtk = paid.TributeAtk;
            return true;
        }

        // ───────────────────────────── pick routing ─────────────────────────────

        /// <summary>SpellTrapEffects.TryResolveWithTarget: this batch's picks. False for any other pending activation.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var pick)) return false;
            if (pick.Kind == PickKind.FiendComedianCall) return false;
            var chosen = engine.ResolveLegalEffectTarget(target) ?? target;
            if (pick.Kind == PickKind.ProvisionsCost)
            {
                ok = ContinueProvisionsPick(engine, p, pick, chosen);
                return true;
            }

            if (pick.Kind == PickKind.BurstTributeCost)
            {
                if (!p.LegalTargets.Contains(chosen))
                {
                    engine.Log("Pick one of the highlighted monsters to Tribute.");
                    return true;
                }

                s_picks.Remove(p);
                engine.ClearPendingActivation();
                PayTribute(engine, p.Controller, p.Card, pick.Clause, chosen);
                ok = OpenWindowAfterCost(engine, p.Controller, pick);
                return true;
            }

            if (!p.LegalTargets.Contains(chosen))
            {
                engine.Log("Pick one of the highlighted monsters.");
                return true;
            }

            s_picks.Remove(p);
            engine.ClearPendingActivation();
            FinishWingbeat(engine, p.Controller, p.Card, chosen);
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            ok = true;
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.CancelPending. Wingbeat's selection and Fiend Comedian's call are part of the resolution and
        /// are made for you; Emergency Provisions' CANCEL sends the cards picked so far (with none, the activation is
        /// undone by the caller), and so is Burst Breath's chained / seated activation when its Tribute is cancelled. A Dragon's Gunfire bullet chosen before a cancelled target pick is forgotten.
        /// </summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null) return false;
            if (!s_picks.TryGetValue(p, out var pick))
            {
                var prog = CompiledEffectCache.GetOrCompile(p.Card?.Def);
                if (prog != null && HasTargetedBullet(prog.ClausesFor(EffectTiming.Activate)))
                    PoisonMeteorCestusRuntime.ForgetBullet(p.Card);
                return false;
            }

            switch (pick.Kind)
            {
                case PickKind.WingbeatReturn:
                {
                    s_picks.Remove(p);
                    engine.ClearPendingActivation();
                    var auto = p.LegalTargets.OrderBy(m => m.CurrentAtk).FirstOrDefault();
                    engine.Log($"{p.Card?.Name}: the selection is mandatory — {auto?.Name} is chosen.");
                    FinishWingbeat(engine, p.Controller, p.Card, auto);
                    engine.ResumeChainAfterChoice();
                    engine.NotifyPublic();
                    return true;
                }
                case PickKind.FiendComedianCall:
                    engine.Log($"{p.Card?.Name}: the call is mandatory — Heads is called.");
                    return TryFinishCoinCall(engine, true);
                case PickKind.ProvisionsCost:
                    if (pick.Chosen.Count > 0)
                        return FinishProvisionsPick(engine, p, pick);
                    s_picks.Remove(p);
                    return false; // nothing paid: the caller undoes the activation
                case PickKind.BurstTributeCost:
                    s_picks.Remove(p);
                    return false; // nothing Tributed: the caller undoes the activation and re-offers the window
                default:
                    return false;
            }
        }
    }
}
