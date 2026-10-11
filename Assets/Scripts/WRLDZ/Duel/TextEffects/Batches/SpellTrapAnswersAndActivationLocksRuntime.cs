using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SpellTrapAnswersAndActivationLocks batch runtime (<see cref="SpellTrapAnswersAndActivationLocksAtoms"/>): Counter Traps
    /// answering Spell/Trap Card activations, and activation locks.
    /// <list type="bullet">
    /// <item><b>Answer legality</b> (<see cref="ChainAnswerAllowed"/>, from TextEffectRuntime.CanAnswerChainLinkWithClause):
    /// Trap Jammer only during the Battle Phase (<see cref="EffectClause.RequiresBattlePhase"/>); Judgment of Anubis only to a
    /// Spell Card activation that would destroy a Spell/Trap Card on the field (<see cref="LinkWouldDestroySpellTrapOnField"/>);
    /// Spell Vanishing only while the opponent has a card in their hand or Deck (Lua s.target chk).</item>
    /// <item><b>Costs</b> (<see cref="TryPayAnswerDiscards"/>, from DuelEngine.TryActivateChainResponse): a discard of N cards
    /// (Spell Vanishing) or of a Spell (Cursed Seal), which the generic one-card path cannot pay. The AI discards its least
    /// useful cards; a human picks them one by one (Cancel: not activated, nothing is discarded).</item>
    /// <item><b>Resolution</b> (<see cref="TryResolveLink"/>, from DuelEngine.ResolveChainStack in place of the generic
    /// negation): the generic negation and destruction, then the answer's <see cref="EffectClause.AfterNegationAction"/>.</item>
    /// <item><b>Locks</b> (<see cref="ActivationLocked"/>, from ContinuousNegationRuntime.ActivationLocked, so every activation
    /// gate asks it): Cursed Seal's rest-of-the-Duel name lock and Invader of Darkness's Quick-Play lock, both on the
    /// activation of the card (from the hand or Set), not on the effects of a face-up card. Sasuke Samurai #2's turn lock is
    /// read by LocksAndZonesRuntime.SpellTrapActivationLocked (<see cref="TurnSpellTrapLockActive"/>), which also stops
    /// Spell/Trap effects and triggers, as ygopro re:IsSpellTrapEffect() does.</item>
    /// </list>
    /// </summary>
    public static class SpellTrapAnswersAndActivationLocksRuntime
    {
        /// <summary>
        /// ActivationCondition of Sasuke Samurai #2 (ygopro GetFlagEffect(tp, id) == 0): its controller has not applied this
        /// card's lock this turn (a player flag shared by every copy, registered as the effect resolves).
        /// </summary>
        public const string TurnLockNotAppliedByYouThisTurn = "SpellTrapTurnLockNotAppliedByYouThisTurn";

        sealed class NameLock
        {
            public DuelistState Who;
            public int CardId;
            public string Name;
            public string Source;
        }

        sealed class State
        {
            /// <summary>Cursed Seal: (player, card code) pairs that player cannot activate for the rest of the Duel.</summary>
            public readonly List<NameLock> NameLocks = new();
            /// <summary>Sasuke Samurai #2: the turn whose Spell/Trap activations are locked (0: none).</summary>
            public int SpellTrapLockTurn;
            public string SpellTrapLockSource;
            /// <summary>Sasuke Samurai #2's player flag: (player, card code) → the turn it was applied.</summary>
            public readonly Dictionary<(DuelistState, int), int> TurnLockApplied = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── helpers ─────────────────────────────

        static bool SpellTrapOnField(DuelEngine engine, CardInstance c)
        {
            if (engine == null || c == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (side.TryFindSpellTrap(c, out _)) return true;
                if (side.FieldSpellZone != null && ReferenceEquals(side.FieldSpellZone.Occupant, c)) return true;
            }

            return false;
        }

        static bool OwnSetOrHand(DuelistState who, CardInstance card) =>
            who.Hand != null && who.Hand.Contains(card) ||
            (who.TryFindSpellTrap(card, out _) ||
             (who.FieldSpellZone != null && ReferenceEquals(who.FieldSpellZone.Occupant, card))) && !card.FaceUp;

        /// <summary>The ChainLinkActivated clause an answer link was seated with (DuelEngine.SeatChainAnswer: the first).</summary>
        static EffectClause AnswerClauseOf(ChainLink link)
        {
            if (link?.Card?.Def == null || link.TargetLink == null || link.EffectKey != "chain-response") return null;
            var prog = CompiledEffectCache.GetOrCompile(link.Card);
            if (prog == null || !prog.FullyCompiled) return null;
            var clauses = prog.ClausesFor(EffectTiming.ChainLinkActivated);
            return clauses != null && clauses.Count > 0 ? clauses[0] : null;
        }

        // ───────────────────────────── Judgment of Anubis: "would destroy a Spell/Trap Card on the field" ─────────────────────────────

        /// <summary>
        /// ygopro GetOperationInfo(ev, CATEGORY_DESTROY) holds a Spell/Trap Card on the field: resolving <paramref name="link"/>
        /// would destroy at least one card in a Spell &amp; Trap Zone or Field Zone (either side) other than the activated card
        /// itself. Fail-closed, as ChainLinkAnswersRuntime.LinkWouldDestroyMonsterOnField: only the plain Destroy action of a
        /// FullyCompiled program; a targeted clause reads the targets recorded on the link; a link that could be one of several
        /// effects of its card must destroy with every one of them.
        /// </summary>
        public static bool LinkWouldDestroySpellTrapOnField(DuelEngine engine, ChainLink link)
        {
            var card = link?.Card;
            if (engine == null || card?.Def == null || link.Negated) return false;
            var prog = CompiledEffectCache.GetOrCompile(card);
            if (prog == null || !prog.FullyCompiled) return false;
            var controller = link.Controller ?? engine.ControllerOf(card);
            if (controller == null) return false;
            var groups = ChainLinkAnswersRuntime.ResolvingEffects(prog, link);
            if (groups.Count == 0) return false;
            foreach (var group in groups)
                if (!group.Exists(c => DestroysSpellTrapOnField(engine, controller, link, c)))
                    return false;
            return true;
        }

        static bool ZoneCanHoldSpellTrap(EffectZoneFilter zone)
        {
            if (zone == EffectZoneFilter.None) return false;
            var n = zone.ToString();
            return (n.IndexOf("SpellTrap", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("AnyCard", StringComparison.Ordinal) >= 0 ||
                    zone == EffectZoneFilter.AllOtherCardsOnField) &&
                   n.IndexOf("Gy", StringComparison.Ordinal) < 0 && n.IndexOf("Hand", StringComparison.Ordinal) < 0 &&
                   n.IndexOf("Deck", StringComparison.Ordinal) < 0;
        }

        static bool DestroysSpellTrapOnField(DuelEngine engine, DuelistState controller, ChainLink link, EffectClause c)
        {
            if (c == null || c.Action != EffectActionKind.Destroy || !ZoneCanHoldSpellTrap(c.Zone)) return false;
            if (c.TributeChosenTarget) return false;
            if (ChainLinkAnswersRuntime.IsTargetingClause(c))
                // Targets are chosen as the card activates: only what the link recorded counts.
                return ChainLinkAnswersRuntime.TargetsOf(link)
                    .Exists(t => t != null && !ReferenceEquals(t, link.Card) && SpellTrapOnField(engine, t));
            // A destruction with no target (Heavy Storm, Harpie's Feather Duster): what it would destroy now. Filters this read
            // cannot decide are not counted (fail-closed).
            if (c.RaceFilterIsDeclared || c.FaceUpContinuousSpellsOnly || c.FaceUpContinuousTrapsOnly) return false;
            IEnumerable<CardInstance> mass = c.Zone == EffectZoneFilter.AllOtherCardsOnField
                ? engine.Player.SpellTrapsOnField().Concat(engine.Opponent.SpellTrapsOnField())
                : TextEffectRuntime.CollectAllMatching(engine, controller, c);
            return mass.Any(m => m != null && !ReferenceEquals(m, link.Card) && SpellTrapOnField(engine, m));
        }

        // ───────────────────────────── answer legality ─────────────────────────────

        /// <summary>
        /// TextEffectRuntime.CanAnswerChainLinkWithClause: this batch's conditions on a ChainLinkActivated answer. True when
        /// none applies or all hold.
        /// </summary>
        public static bool ChainAnswerAllowed(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            ChainLink target, out string reason)
        {
            reason = "OK";
            if (engine == null || clause == null || clause.Timing != EffectTiming.ChainLinkActivated) return true;
            // Trap Jammer (Lua Duel.IsBattlePhase()).
            if (clause.RequiresBattlePhase && engine.Phase != DuelPhase.Battle)
            {
                reason = "Only during the Battle Phase.";
                return false;
            }

            // Judgment of Anubis (Lua s.condition): a Spell Card's activation that would destroy a Spell/Trap Card on the field.
            if (clause.NegateRequiresWouldDestroySpellTrapOnField)
            {
                if (target?.Card?.Def == null || !target.Card.Def.IsSpell || !ChainLinkAnswersRuntime.IsCardActivation(target))
                {
                    reason = "Only answers the activation of a Spell Card.";
                    return false;
                }

                if (!LinkWouldDestroySpellTrapOnField(engine, target))
                {
                    reason = "It would not destroy a Spell/Trap Card on the field.";
                    return false;
                }
            }

            // Spell Vanishing (Lua s.target chk: GetFieldGroupCount(tp, 0, LOCATION_HAND|LOCATION_DECK) > 0).
            if (clause.AfterNegationAction == EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy)
            {
                var opp = who != null ? engine.OpponentOf(who) : null;
                if (opp == null || (opp.Hand?.Count ?? 0) + opp.DeckCount == 0)
                {
                    reason = "Your opponent has no card in their hand or Deck.";
                    return false;
                }
            }

            return true;
        }

        // ───────────────────────────── costs ─────────────────────────────

        /// <summary>
        /// DuelEngine.TryActivateChainResponse, before its one-card discard: a Counter Trap whose discard cost is N &gt; 1 cards
        /// (Spell Vanishing) or a Spell (Cursed Seal, <see cref="FaceUpQuickEffectsRuntime.DiscardSpellCard"/>). False: not
        /// this batch's cost (the generic path pays it). True: handled; <paramref name="seated"/> is the activation's result
        /// (true while a human's pick is open).
        /// </summary>
        internal static bool TryPayAnswerDiscards(DuelEngine engine, DuelistState who, CardInstance card, EffectClause clause,
            bool human, Func<bool> seat, out bool seated)
        {
            seated = false;
            if (engine == null || who == null || card?.Def == null || clause == null || seat == null ||
                !clause.RequiresDiscardCost)
                return false;
            var need = TextEffectRuntime.DiscardNeed(clause);
            var attribute = TextEffectRuntime.DiscardCostAttributeOf(clause);
            if (need <= 1 && attribute != FaceUpQuickEffectsRuntime.DiscardSpellCard) return false;
            List<CardInstance> Fodder() => TextEffectRuntime.CollectDiscardCost(who, attribute, card);
            if (Fodder().Count < need) return true;
            if (!human)
            {
                var picks = TextEffectRuntime.PickAiDiscards(Fodder(), need).ToList();
                if (picks.Count < need) return true;
                Pay(engine, who, card, picks);
                seated = seat();
                return true;
            }

            var answering = engine.PendingResponse;
            var speed = OfficialEffectRegistry.SpeedOf(card.Def);
            var cls = OfficialEffectRegistry.ClassOfActivation(card.Def, false);
            bool StillAnswering() =>
                ReferenceEquals(engine.PendingResponse, answering) && engine.PendingResponse?.Responder == who &&
                who.TryFindSpellTrap(card, out _) && !card.FaceUp && engine.Chain.CanAddLink(speed, cls);
            var chosen = new List<CardInstance>();
            var result = true;
            var what = attribute == FaceUpQuickEffectsRuntime.DiscardSpellCard ? "Spell Card" : "card";

            void AskNext()
            {
                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, card, new CardPickRequest
                {
                    Candidates = () => Fodder().Where(c => !chosen.Contains(c)).ToList(),
                    Kind = EffectTargetKind.DiscardFromHand,
                    Prompt = $"discard {(need > 1 ? $"{need - chosen.Count} more {what}(s)" : $"1 {what}")} from your hand " +
                             $"to activate {card.Name} (cost), or Cancel to not activate it.",
                    AiChoose = list => AiHeuristicPolicy.PickLowestValue(list, card) ?? list.FirstOrDefault(),
                    DuringActivation = true,
                    Finish = c =>
                    {
                        if (c == null || !who.Hand.Contains(c))
                        {
                            result = false;
                            engine.Log($"{card.Name} is not activated — nothing is discarded.");
                            engine.NotifyPublic();
                            return;
                        }

                        chosen.Add(c);
                        if (chosen.Count < need)
                        {
                            AskNext();
                            return;
                        }

                        if (!StillAnswering() || chosen.Exists(x => !who.Hand.Contains(x)))
                        {
                            result = false;
                            engine.Log($"{card.Name}: the window it answered has passed — not activated.");
                            engine.NotifyPublic();
                            return;
                        }

                        Pay(engine, who, card, chosen);
                        result = seat();
                        if (!result) engine.NotifyPublic();
                    },
                    Cancel = () =>
                    {
                        result = false;
                        engine.Log($"{card.Name} is not activated — nothing is discarded.");
                    }
                });
            }

            AskNext();
            seated = result;
            return true;
        }

        static void Pay(DuelEngine engine, DuelistState who, CardInstance card, List<CardInstance> discards)
        {
            foreach (var d in discards)
            {
                if (d == null || !who.Hand.Contains(d)) continue;
                engine.SendCardToGrave(who, d, sentBy: card);
                engine.Log($"Cost: discard {d.Name}.");
                DiscardEvents.Record(engine, who, d, DiscardCause.Cost);
            }
        }

        // ───────────────────────────── resolution ─────────────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack, in place of the generic negation of a NegateActivation link: an answer with an
        /// <see cref="EffectClause.AfterNegationAction"/> applies <paramref name="applyNegation"/> (the generic negation and
        /// destruction), then its rider. True when handled (a pick it opens may hold the Chain).
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link, Action<ChainLink> applyNegation)
        {
            if (engine == null || link == null || applyNegation == null || !link.NegatesActivation) return false;
            var clause = AnswerClauseOf(link);
            if (clause == null || clause.AfterNegationAction == EffectActionKind.None) return false;
            var who = link.Controller ?? engine.ControllerOf(link.Card);
            if (who == null) return false;
            var answered = link.TargetLink;
            var negated = answered?.Card;
            var negatedId = negated?.CardId ?? 0;
            var negatedName = negated?.Name;
            var wasOnField = negated != null && SpellTrapOnField(engine, negated);
            applyNegation(link);
            // "negate the activation, and if you do, destroy it": the card left the field by that destruction.
            var destroyed = wasOnField && link.DestroyNegatedCard && answered.Negated && !SpellTrapOnField(engine, negated);
            switch (clause.AfterNegationAction)
            {
                case EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel:
                    // Lua: only if the activation was negated and the card destroyed (Duel.Destroy(eg) ~= 0).
                    if (destroyed && negatedId > 0)
                        RegisterNameLock(engine, engine.OpponentOf(who), negatedId, negatedName, link.Card);
                    break;
                case EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy:
                    // Lua: whatever the negation did, the opponent's hand and Deck are confirmed and rc's code is sent.
                    SendSameNameFromOpponentHandAndDeck(engine, who, link.Card, negatedId, negatedName);
                    break;
                case EffectActionKind.MayDestroyOpponentFaceUpMonsterInflictItsAtk:
                    OfferDestroyAndBurn(engine, who, link.Card);
                    break;
            }

            return true;
        }

        static void RegisterNameLock(DuelEngine engine, DuelistState locked, int cardId, string name, CardInstance source)
        {
            if (locked == null) return;
            var st = StateOf(engine);
            if (!st.NameLocks.Exists(l => l.Who == locked && l.CardId == cardId))
                st.NameLocks.Add(new NameLock { Who = locked, CardId = cardId, Name = name, Source = source?.Name });
            engine.Log($"{source?.Name}: {locked.Name} cannot activate \"{name}\" for the rest of this Duel.");
        }

        /// <summary>
        /// Spell Vanishing (Lua s.activate): look at the opponent's hand and Deck (Duel.ConfirmCards) and send every card with
        /// the answered Spell's code to the GY (REASON_EFFECT; a send, not a discard or a destruction), then shuffle that Deck.
        /// </summary>
        static void SendSameNameFromOpponentHandAndDeck(DuelEngine engine, DuelistState who, CardInstance source,
            int cardId, string name)
        {
            var opp = engine.OpponentOf(who);
            if (opp == null || (opp.Hand?.Count ?? 0) + opp.DeckCount == 0) return;
            FlipDownAndPeekRuntime.LogLook(engine, who, source, $"{opp.Name}'s hand", opp.Hand);
            engine.Log($"{source?.Name}: {who.Name} looks at {opp.Name}'s Deck ({opp.DeckCount} cards).");
            var sent = 0;
            if (cardId > 0)
            {
                var gyBefore = new HashSet<CardInstance>(opp.Graveyard.Where(g => g != null));
                DiscardTriggersRuntime.BeginScope(engine);
                try
                {
                    for (var i = opp.Hand.Count - 1; i >= 0; i--)
                    {
                        var c = opp.Hand[i];
                        if (c == null || c.CardId != cardId) continue;
                        opp.Hand.RemoveAt(i);
                        RedirectAndReplaceRuntime.PutInGraveyard(engine, opp, c);
                        sent++;
                    }

                    var fromDeck = new List<CardInstance>();
                    for (var i = opp.Deck.Count - 1; i >= 0; i--)
                    {
                        if (opp.Deck[i] != cardId) continue;
                        opp.Deck.RemoveAt(i);
                        var inst = engine.CreateCardInstance(cardId);
                        RedirectAndReplaceRuntime.PutInGraveyard(engine, opp, inst);
                        fromDeck.Add(inst);
                        sent++;
                    }

                    // The copies sent from the Deck by this card's effect (DiscardTriggers: "sent from your Deck to the GY").
                    foreach (var g in fromDeck)
                        if (opp.Graveyard.Contains(g) && !gyBefore.Contains(g))
                            DiscardTriggersRuntime.NoteSentFromDeck(engine, opp, g, who);
                }
                finally
                {
                    DiscardTriggersRuntime.EndScope(engine);
                }
            }

            engine.Log(sent > 0
                ? $"{source?.Name}: {sent} \"{name}\" from {opp.Name}'s hand and Deck are sent to the GY."
                : $"{source?.Name}: {opp.Name} has no \"{name}\" in their hand or Deck.");
            engine.Rng.Shuffle(opp.Deck);
            engine.NotifyPublic();
        }

        /// <summary>
        /// Judgment of Anubis (Lua s.operation, after the negation): if the opponent controls a face-up monster, its
        /// controller may (SelectYesNo) select 1 of them (not a target) and destroy it; if it was destroyed, the opponent takes
        /// damage equal to its ATK on the field. A human taps the monster or Cancels (the Chain waits); the AI takes the
        /// highest ATK.
        /// </summary>
        static void OfferDestroyAndBurn(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            List<CardInstance> Candidates() => opp.MonstersOnField().Where(m => m?.Def != null && m.FaceUp).ToList();
            if (Candidates().Count == 0) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = EffectTargetKind.OppFaceUpMonster,
                Prompt = $"{source?.Name}: you can destroy 1 face-up monster your opponent controls and inflict damage equal " +
                         "to its ATK — tap it, or Cancel to not.",
                AiChoose = list => list.OrderByDescending(m => m.CurrentAtk).FirstOrDefault(),
                AskWithOneCandidate = true,
                Finish = chosen => DestroyAndBurn(engine, who, source, chosen),
                Cancel = () => engine.Log($"{source?.Name}: {who.Name} does not destroy a monster.")
            });
        }

        static void DestroyAndBurn(DuelEngine engine, DuelistState who, CardInstance source, CardInstance chosen)
        {
            var opp = engine.OpponentOf(who);
            if (chosen == null || opp == null)
            {
                engine.Log($"{source?.Name}: {who?.Name} does not destroy a monster.");
                return;
            }

            if (!opp.TryFindMonster(chosen, out _) || !chosen.FaceUp) return;
            // Lua: atk = GetAttack() before the destruction (its ATK on the field).
            var atk = Math.Max(0, chosen.CurrentAtk);
            TextEffectRuntime.DestroyCard(engine, chosen, source, false, who);
            if (opp.TryFindMonster(chosen, out _)) return; // not destroyed: no damage
            engine.ApplyEffectDamage(opp, atk, source?.Name, source);
        }

        // ───────────────────────────── Sasuke Samurai #2 ─────────────────────────────

        /// <summary>
        /// Resolution of <see cref="EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn"/>: until the End Phase of this
        /// turn neither player can activate Spell/Trap Cards or their effects; the controller's once-per-turn flag is set.
        /// </summary>
        public static void RegisterTurnSpellTrapLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            var st = StateOf(engine);
            st.SpellTrapLockTurn = engine.TurnNumber;
            st.SpellTrapLockSource = source?.Name;
            st.TurnLockApplied[(who, source?.CardId ?? 0)] = engine.TurnNumber;
            engine.Log($"{source?.Name ?? "Effect"}: until the End Phase, Spell and Trap Cards cannot be activated.");
        }

        /// <summary>True while a Sasuke Samurai #2 lock lasts (this turn, through its End Phase).</summary>
        public static bool TurnSpellTrapLockActive(DuelEngine engine, out string reason)
        {
            reason = null;
            if (engine == null || engine.TurnNumber <= 0 || !s_state.TryGetValue(engine, out var st) ||
                st.SpellTrapLockTurn != engine.TurnNumber)
                return false;
            reason = $"{st.SpellTrapLockSource ?? "Effect"}: Spell and Trap Cards cannot be activated until the End Phase.";
            return true;
        }

        /// <summary>TextEffectRuntime.ActivationConditionsMet: <see cref="TurnLockNotAppliedByYouThisTurn"/>.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c?.ActivationCondition != TurnLockNotAppliedByYouThisTurn) return false;
            ok = !(engine != null && who != null && s_state.TryGetValue(engine, out var st) &&
                   st.TurnLockApplied.TryGetValue((who, card?.CardId ?? 0), out var turn) && turn == engine.TurnNumber);
            reason = "Once per turn: you already applied this effect this turn.";
            return true;
        }

        // ───────────────────────────── activation locks ─────────────────────────────

        /// <summary>
        /// ContinuousNegationRuntime.ActivationLocked: <paramref name="who"/> may not activate <paramref name="card"/> (a Spell in
        /// their hand or Set) — Cursed Seal's name lock, or a face-up Invader of Darkness their opponent controls (a Quick-Play
        /// Spell). The effects of a face-up card are not card activations and are not locked.
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card, out string reason)
        {
            reason = null;
            if (engine == null || who == null || card?.Def == null || card.Def.IsMonster || !card.Def.IsSpell) return false;
            if (!OwnSetOrHand(who, card)) return false;
            if (s_state.TryGetValue(engine, out var st))
            {
                var nl = st.NameLocks.FirstOrDefault(l => l.Who == who && l.CardId == card.CardId);
                if (nl != null)
                {
                    reason = $"{nl.Source}: you cannot activate \"{nl.Name}\" for the rest of this Duel.";
                    return true;
                }
            }

            if (SpellTrapEffects.IsQuickPlay(card.Def))
            {
                var src = QuickPlayLockSource(engine, who);
                if (src != null)
                {
                    reason = $"{src.Name}: you cannot activate Quick-Play Spell Cards.";
                    return true;
                }
            }

            return false;
        }

        /// <summary>A face-up, un-negated monster <paramref name="activator"/>'s opponent controls that locks Quick-Play Spells.</summary>
        static CardInstance QuickPlayLockSource(DuelEngine engine, DuelistState activator)
        {
            var opp = engine.OpponentOf(activator);
            if (opp == null) return null;
            foreach (var m in opp.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp || m.IsNegated || engine.IsSummonPending(m)) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                if (prog.ClauseList.Exists(c => c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp &&
                                                c.Action == EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells))
                    return m;
            }

            return null;
        }
    }
}
