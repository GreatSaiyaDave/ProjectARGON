using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DcrSpellTrapUtility" runtime. Reads compiled clauses only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Ray of Hope</b>: <see cref="FilterTargets"/> keeps the monsters of the clause's Attribute among the GY
    /// targets of <see cref="EffectActionKind.ReturnChosenToDeckShuffle"/> (TextEffectRuntime.CollectTargets).</item>
    /// <item><b>Spell Reproduction</b>: the "send N Spells from your hand" activation cost rides the N-card hand cost of
    /// TextEffectRuntime (<see cref="EffectClause.DiscardCostCount"/>) with <see cref="EffectClause.CostSendSpellsFromHandCount"/>:
    /// only Spells are offered (<see cref="SpellsForHandCost"/>) and they are sent, not discarded (<see cref="IsSpellSendCost"/>).</item>
    /// <item><b>Frozen Soul</b>: activation condition <see cref="YourLpAtLeastLowerThanOpponent"/>; the skip is Great Long
    /// Nose's <c>SpiritMonstersRuntime.ScheduleBattlePhaseSkip</c>.</item>
    /// <item><b>Arsenal Robber</b>: activation condition <see cref="OpponentDeckNotEmpty"/>; <see cref="StartOpponentEquipSend"/>
    /// asks the opponent (AskCardPick, the Chain waits) for 1 Equip Spell of their Deck and sends it with Dark Scorpion
    /// Burglars' send (<c>BattleOutcomeTriggersRuntime.SendSpell</c>).</item>
    /// <item><b>Interdimensional Matter Transporter</b>: <see cref="BanishUntilEndPhase"/> + <see cref="OnEndPhase"/>
    /// (DuelEngine.EndTurn at the start of the End Phase and again before it ends), returning through LocksAndZones'
    /// <c>ReturnToField</c> (Duel.ReturnToField: the zone it left, else another; no zone: the GY).</item>
    /// <item><b>Different Dimension Gate</b>: <see cref="BanishLinkedToGate"/> (each target, at resolution, while the gate is
    /// still face-up on the field) + <see cref="OnCardDestroyed"/> (ToonsRuntime.NotifyFaceUpCardDestroyed — every Spell/Trap
    /// destroy path calls it after the card was sent): the gate in a GY on that same field presence returns the monsters.</item>
    /// <item><b>Final Countdown</b>: <see cref="StartCountdown"/> + <see cref="OnTurnEnd"/> (DuelEngine, at the very end of a
    /// turn) + <see cref="TurnCounts"/> (Pyro Clock of Destiny moves a countdown forward, Lua code 1082946).</item>
    /// <item><b>Dragged Down into the Grave</b>: <see cref="ResolveDraggedDown"/> — two AskCardPick picks (Kind
    /// CardInOppHand: the activating player's, then the opponent's), one discard event, then both draw.</item>
    /// </list>
    /// </summary>
    public static class DcrSpellTrapUtilityRuntime
    {
        /// <summary>Frozen Soul: Duel.GetLP(tp) + ConditionAmount &lt;= Duel.GetLP(1-tp).</summary>
        public const string YourLpAtLeastLowerThanOpponent = "DcrYourLpAtLeastLowerThanOpponent";

        /// <summary>Arsenal Robber: Duel.GetFieldGroupCount(tp, 0, LOCATION_DECK) &gt; 0.</summary>
        public const string OpponentDeckNotEmpty = "DcrOpponentDeckNotEmpty";

        /// <summary>
        /// Dragged Down into the Grave: the opponent has a card in hand and the activating player has one besides this
        /// card (Lua s.condition), and both players can draw 1 (Lua s.target).
        /// </summary>
        public const string BothHandsHaveACard = "DcrBothHandsHaveACard";

        // ───────────────────────────── state ─────────────────────────────

        sealed class GateLink
        {
            public CardInstance Gate;
            public int GateEntry;
            public int GateFaceDown;
            public TemporaryBanishment Record;
            public int MonsterEntry;
        }

        sealed class Countdown
        {
            public DuelistState Who;
            public CardInstance Card;
            public int Count;
            public int Goal;
        }

        sealed class State
        {
            public readonly List<TemporaryBanishment> EndPhaseReturns = new();
            public readonly List<GateLink> Gates = new();
            public readonly List<Countdown> Countdowns = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── activation conditions ─────────────────────────────

        /// <summary>TextEffectRuntime.ActivationConditionsMet default chain. False when the condition is not this batch's.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (engine == null || who == null || c == null) return false;
            var opp = engine.OpponentOf(who);
            switch (c.ActivationCondition)
            {
                case YourLpAtLeastLowerThanOpponent:
                    ok = opp != null && who.LifePoints + c.ConditionAmount <= opp.LifePoints;
                    reason = $"Only while your LP is at least {c.ConditionAmount} lower than your opponent's.";
                    return true;
                case OpponentDeckNotEmpty:
                    ok = opp?.Deck != null && opp.Deck.Count > 0;
                    reason = "Your opponent has no card in their Deck.";
                    return true;
                case BothHandsHaveACard:
                    // Lua s.condition (both hands) + s.target (Duel.IsPlayerCanDraw for both players: a card in each Deck).
                    ok = opp?.Hand != null && opp.Hand.Any(h => h != null) &&
                         who.Hand != null && who.Hand.Any(h => h != null && !ReferenceEquals(h, card)) &&
                         who.Deck != null && who.Deck.Count > 0 && opp.Deck != null && opp.Deck.Count > 0;
                    reason = "Both players need a card in hand (yours besides this card) and a card in their Deck.";
                    return true;
                default:
                    return false;
            }
        }

        // ───────────────────────────── Ray of Hope ─────────────────────────────

        /// <summary>TextEffectRuntime.CollectTargets: GY targets of a Deck shuffle restricted to an Attribute (Ray of Hope).</summary>
        public static void FilterTargets(EffectClause c, List<CardInstance> list)
        {
            if (c == null || list == null || string.IsNullOrEmpty(c.AttributeFilter)) return;
            if (c.Action != EffectActionKind.ReturnChosenToDeckShuffle || c.Zone != EffectZoneFilter.ControllerGyCards)
                return;
            list.RemoveAll(t => t?.Def == null || !t.Def.IsMonster ||
                                !string.Equals(t.Def.attribute, c.AttributeFilter, StringComparison.OrdinalIgnoreCase));
        }

        // ───────────────────────────── Spell Reproduction ─────────────────────────────

        /// <summary>The N-card hand cost of <paramref name="c"/> sends Spells (not a discard).</summary>
        public static bool IsSpellSendCost(EffectClause c) =>
            c != null && c.DiscardCostCount > 0 && c.CostSendSpellsFromHandCount > 0;

        /// <summary>The compiled Activate clause of <paramref name="card"/> whose hand cost sends Spells, or null.</summary>
        public static EffectClause SpellSendCostClause(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog?.ClausesFor(EffectTiming.Activate)?.FirstOrDefault(IsSpellSendCost);
        }

        /// <summary>The Spells in <paramref name="who"/>'s hand that can pay the cost (never the card being activated).</summary>
        public static List<CardInstance> SpellsForHandCost(DuelistState who, CardInstance except) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(h => h?.Def != null && h.Def.IsSpell && !ReferenceEquals(h, except)).ToList();

        // ───────────────────────────── Arsenal Robber ─────────────────────────────

        /// <summary>
        /// The OpponentSendsSpellFromDeckToGy arm with the Equip filter (<see cref="EffectZoneFilter.DeckEquipSpells"/>):
        /// the opponent selects 1 Equip Spell in their Deck (a human through AskCardPick, which holds the Chain) and sends it
        /// to the GY; none: nothing happens.
        /// </summary>
        public static void StartOpponentEquipSend(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var victim = engine.OpponentOf(who);
            if (victim == null) return;
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, victim, source, new CardPickRequest
            {
                Candidates = () => EquipSpellsInDeck(engine, victim),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"{source?.Name}: select 1 Equip Spell Card in your Deck to send to the GY.",
                AiChoose = list => BattleOutcomeTriggersRuntime.AiSpellToSend(victim, list),
                Finish = pick =>
                {
                    if (pick == null)
                    {
                        engine.Log($"{source?.Name}: {victim.Name} has no Equip Spell Card in their Deck.");
                        return;
                    }

                    BattleOutcomeTriggersRuntime.SendSpell(engine, who, victim, source, pick);
                }
            });
        }

        /// <summary>One card per distinct Equip Spell in <paramref name="owner"/>'s Deck.</summary>
        static List<CardInstance> EquipSpellsInDeck(DuelEngine engine, DuelistState owner)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null || !def.IsSpell ||
                    !def.IsEquipSpell)
                    continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        // ───────────────────────────── temporary banishment helpers ─────────────────────────────

        static DuelistState PileHolding(DuelEngine engine, CardInstance card)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
                if (who?.Banished != null && who.Banished.Contains(card))
                    return who;
            return null;
        }

        /// <summary>
        /// Banish <paramref name="target"/> (a monster <paramref name="controller"/> controls, in zone <paramref name="zone"/>)
        /// temporarily: the record to return it later, or null when it was not banished or was a Token (it ceases to exist).
        /// </summary>
        static TemporaryBanishment BanishTemporarily(DuelEngine engine, DuelistState who, CardInstance source,
            DuelistState controller, CardInstance target, int zone)
        {
            var faceUp = target.FaceUp;
            var position = target.Position;
            var token = target.IsToken;
            engine.BanishCard(controller, target, by: who);
            var pile = PileHolding(engine, target);
            if (pile == null) return null; // not banished (an effect stopped it)
            if (token)
            {
                pile.Banished.Remove(target);
                engine.Log($"{target.Name} (Token) ceases to exist.");
                return null;
            }

            return new TemporaryBanishment
            {
                Card = target,
                Source = source,
                Pile = pile,
                ReturnTo = controller,
                ZoneIndex = zone,
                Position = position,
                FaceUp = faceUp,
                WaitsFor = who,
                ReturnTurn = engine.TurnNumber
            };
        }

        // ───────────────────────────── Interdimensional Matter Transporter ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.BanishTargetUntilEndPhase"/>: the target, still a face-up monster
        /// <paramref name="who"/> controls (Lua IsFaceup + IsControler(tp)), is banished until this turn's End Phase.
        /// </summary>
        public static void BanishUntilEndPhase(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || who == null) return;
            if (target == null || !ReferenceEquals(engine.ControllerOf(target), who) ||
                !who.TryFindMonster(target, out var zone) || !target.FaceUp)
            {
                engine.Log($"{source?.Name ?? "Card"}: the target is no longer a face-up monster you control — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var record = BanishTemporarily(engine, who, source, who, target, zone);
            if (record != null)
            {
                StateOf(engine).EndPhaseReturns.Add(record);
                engine.Log($"{target.Name} is banished until the End Phase.");
            }

            engine.NotifyPublic();
        }

        /// <summary>
        /// DuelEngine.EndTurn (start of the End Phase) and FinishEndTurnAfterTriggers (a banishment resolved during the
        /// End Phase): every monster banished until this End Phase returns to the field (Lua count limit 1 per banishment).
        /// </summary>
        public static void OnEndPhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.EndPhaseReturns.Count == 0) return;
            var due = st.EndPhaseReturns.Where(r => r != null && r.ReturnTurn <= engine.TurnNumber).ToList();
            if (due.Count == 0) return;
            foreach (var r in due)
                st.EndPhaseReturns.Remove(r);
            foreach (var r in due)
            {
                if (engine.GameOver) return;
                if (r.ReturnTurn != engine.TurnNumber) continue; // RESET_PHASE+PHASE_END: an End Phase went by
                if (r.Pile?.Banished == null || !r.Pile.Banished.Contains(r.Card)) continue;
                engine.Log($"{r.Source?.Name ?? "Effect"}: {r.Card.Name} returns to the field.");
                LocksAndZonesRuntime.ReturnToField(engine, r);
            }

            engine.NotifyPublic();
        }

        // ───────────────────────────── Different Dimension Gate ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy"/> for one target (the first, then the second
        /// through ApplySecondTarget): only while the gate is still face-up in its controller's Spell &amp; Trap Zone (Lua
        /// c:IsRelateToEffect), the target still on the field.
        /// </summary>
        public static void BanishLinkedToGate(DuelEngine engine, DuelistState who, CardInstance gate, CardInstance target)
        {
            if (engine == null || who == null || gate == null) return;
            if (!who.TryFindSpellTrap(gate, out _) || !gate.FaceUp)
            {
                engine.Log($"{gate.Name} is no longer on the field — nothing is banished.");
                return;
            }

            var controller = target != null ? engine.ControllerOf(target) : null;
            if (controller == null || !controller.TryFindMonster(target, out var zone))
            {
                engine.Log($"{gate.Name}: the target is no longer on the field — it is not banished.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, gate))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var st = StateOf(engine);
            // Links of a gate that left the field another way end (they stay banished).
            st.Gates.RemoveAll(l => l?.Gate == null ||
                                    (!ReferenceEquals(l.Gate, gate) && engine.ControllerOf(l.Gate) == null));
            var record = BanishTemporarily(engine, who, gate, controller, target, zone);
            if (record != null)
            {
                st.Gates.Add(new GateLink
                {
                    Gate = gate,
                    GateEntry = gate.FieldEntryStamp,
                    GateFaceDown = gate.FaceDownStamp,
                    Record = record,
                    MonsterEntry = target.FieldEntryStamp
                });
                engine.Log($"{gate.Name} banishes {target.Name}.");
            }

            engine.NotifyPublic();
        }

        /// <summary>
        /// ToonsRuntime.NotifyFaceUpCardDestroyed (every Spell/Trap destroy path, after the card was sent): a destroyed gate
        /// that landed in a GY from the field presence that banished its monsters returns them (Lua REASON_DESTROY on
        /// EVENT_TO_GRAVE); a gate destroyed and banished instead returns nothing.
        /// </summary>
        public static void OnCardDestroyed(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st) || st.Gates.Count == 0) return;
            var links = st.Gates.Where(l => ReferenceEquals(l?.Gate, card)).ToList();
            if (links.Count == 0) return;
            foreach (var l in links)
                st.Gates.Remove(l);
            var inGy = (engine.Player?.Graveyard?.Contains(card) ?? false) ||
                       (engine.Opponent?.Graveyard?.Contains(card) ?? false);
            if (!inGy) return;
            var returned = false;
            foreach (var l in links)
            {
                if (engine.GameOver) return;
                if (l.GateEntry != card.FieldEntryStamp || l.GateFaceDown != card.FaceDownStamp) continue;
                var r = l.Record;
                if (r?.Pile?.Banished == null || !r.Pile.Banished.Contains(r.Card) ||
                    r.Card.FieldEntryStamp != l.MonsterEntry)
                    continue;
                engine.Log($"{card.Name}: {r.Card.Name} returns to the field.");
                LocksAndZonesRuntime.ReturnToField(engine, r);
                returned = true;
            }

            if (returned) engine.NotifyPublic();
        }

        // ───────────────────────────── Final Countdown ─────────────────────────────

        /// <summary><see cref="EffectActionKind.WinAfterTurnCountdown"/>: <paramref name="who"/>'s countdown starts (this turn is turn 1).</summary>
        public static void StartCountdown(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null) return;
            var goal = clause != null && clause.Amount > 0 ? clause.Amount : 20;
            StateOf(engine).Countdowns.Add(new Countdown { Who = who, Card = source, Count = 0, Goal = goal });
            engine.Log($"{source?.Name ?? "Effect"}: {who.Name} wins at the end of the {goal}th turn from now " +
                       "(this turn is the 1st).");
        }

        /// <summary>DuelEngine.FinishEndTurnAfterTriggers, as the turn ends (Lua EVENT_TURN_END): each countdown counts 1 turn.</summary>
        public static void OnTurnEnd(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st) ||
                st.Countdowns.Count == 0)
                return;
            foreach (var c in st.Countdowns)
            {
                c.Count++;
                engine.Log($"{c.Card?.Name ?? "Countdown"}: {c.Who?.Name} — turn {c.Count} of {c.Goal}.");
            }

            CheckCountdownWin(engine);
        }

        /// <summary>Lua s.winop: a player whose countdown reached its goal wins; both players at once: a DRAW.</summary>
        static void CheckCountdownWin(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !s_state.TryGetValue(engine, out var st)) return;
            var done = st.Countdowns.Where(c => c.Count >= c.Goal).ToList();
            if (done.Count == 0) return;
            foreach (var c in done)
                st.Countdowns.Remove(c);
            var forPlayer = done.Count(c => ReferenceEquals(c.Who, engine.Player));
            var forOpponent = done.Count(c => ReferenceEquals(c.Who, engine.Opponent));
            var name = done[0].Card?.Name ?? "Final Countdown";
            if (forPlayer == forOpponent)
                engine.EndGameByCardEffect(null, $"{name}: both countdowns end on the same turn — the Duel is a DRAW");
            else
                engine.EndGameByCardEffect(forPlayer > forOpponent ? engine.Player : engine.Opponent,
                    $"{name} — the countdown has ended");
        }

        /// <summary>PhaseTimersAndDeckTopRuntime.TurnCounts: each running countdown (Pyro Clock of Destiny moves it 1 turn).</summary>
        public static IEnumerable<PhaseTimersAndDeckTopRuntime.TurnCount> TurnCounts(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) yield break;
            foreach (var c in st.Countdowns.ToList())
            {
                if (c.Card == null) continue;
                var cd = c;
                yield return new PhaseTimersAndDeckTopRuntime.TurnCount
                {
                    Holder = cd.Card,
                    Owner = cd.Who,
                    EndingHelps = cd.Who,
                    Advance = _ =>
                    {
                        if (!StateOf(engine).Countdowns.Contains(cd)) return;
                        cd.Count++;
                        engine.Log($"{cd.Card.Name}: {cd.Who?.Name} — turn {cd.Count} of {cd.Goal}.");
                        CheckCountdownWin(engine);
                    }
                };
            }
        }

        // ───────────────────────────── Dragged Down into the Grave ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw"/> (Lua s.activate): nothing if either
        /// hand is empty; otherwise each player looks at the other's hand, <paramref name="who"/> picks 1 card of the
        /// opponent's hand, the opponent picks 1 of <paramref name="who"/>'s, both are discarded in one event, then
        /// <paramref name="who"/> draws 1 and the opponent draws 1.
        /// </summary>
        public static void ResolveDraggedDown(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            if (opp == null) return;
            if (!opp.Hand.Any(h => h != null) || !who.Hand.Any(h => h != null))
            {
                engine.Log($"{source?.Name ?? "Effect"}: a hand is empty — nothing happens.");
                return;
            }

            FlipDownAndPeekRuntime.LogLook(engine, who, source, $"{opp.Name}'s hand", opp.Hand);
            FlipDownAndPeekRuntime.LogLook(engine, opp, source, $"{who.Name}'s hand", who.Hand);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => opp.Hand.Where(h => h != null).ToList(),
                Kind = EffectTargetKind.CardInOppHand,
                Prompt = $"{source?.Name}: choose 1 card in your opponent's hand to discard.",
                AiChoose = BestCard,
                Finish = fromOpp =>
                    ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, opp, source, new CardPickRequest
                    {
                        Candidates = () => who.Hand.Where(h => h != null).ToList(),
                        Kind = EffectTargetKind.CardInOppHand,
                        Prompt = $"{source?.Name}: choose 1 card in your opponent's hand to discard.",
                        AiChoose = BestCard,
                        Finish = fromWho => DiscardBothThenDraw(engine, who, opp, source, fromOpp, fromWho)
                    })
            });
        }

        /// <summary>The AI takes the opponent's most valuable card.</summary>
        static CardInstance BestCard(List<CardInstance> list) =>
            list?.Where(c => c != null).OrderByDescending(AiHeuristicPolicy.DiscardPriority).FirstOrDefault();

        static void DiscardBothThenDraw(DuelEngine engine, DuelistState who, DuelistState opp, CardInstance source,
            CardInstance fromOpp, CardInstance fromWho)
        {
            if (engine.GameOver) return;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                foreach (var (owner, card) in new[] { (opp, fromOpp), (who, fromWho) })
                {
                    if (card == null || !owner.Hand.Contains(card)) continue;
                    var gone = MonsterEffects.DiscardFromHandByInstance(owner, card, DiscardCause.Effect, who);
                    if (gone == null) continue;
                    engine.Log($"{owner.Name} discards {gone.Name} ({source?.Name}).");
                    DiscardTriggersRuntime.NoteDiscard(engine, owner, gone, who);
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            if (engine.GameOver) return;
            engine.Draw(who, 1);
            if (engine.GameOver) return;
            engine.Draw(opp, 1);
            engine.NotifyPublic();
        }
    }
}
