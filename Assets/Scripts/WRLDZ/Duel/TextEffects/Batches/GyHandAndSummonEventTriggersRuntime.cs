using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "GyHandAndSummonEventTriggers" runtime (atoms: <see cref="GyHandAndSummonEventTriggersAtoms"/>). Reads
    /// compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>"When this card is sent from the hand to the GY"</b> (<see cref="EffectTiming.SentFromHandToGy"/>, Night
    /// Assailant: Lua SINGLE TRIGGER_F EVENT_TO_GRAVE, IsPreviousLocation(LOCATION_HAND), any reason). Every hand → GY move
    /// passes <c>fromHand</c> to <see cref="GyEntryStamps.Entered"/>, which records the trip here
    /// (<see cref="NoteEnteredGy"/>) and queues the trigger; once play is idle (DuelEngine.Notify,
    /// <see cref="FireQueuedHandSends"/>) each card still in that GY on that trip activates: its target is chosen (the AI's
    /// <see cref="TextEffectRuntime.AutoPick"/>, a human through AskCardPick; none: it still activates and does nothing),
    /// then the triggers go on one Chain (<see cref="TriggerChainResponsesRuntime.TrySeat"/>) or resolve at once.</item>
    /// <item><b>Flip monster targets</b> (<see cref="FlipKind"/> on <see cref="EffectClause.TargetCardKind"/>, Lua
    /// IsType(TYPE_FLIP)): <see cref="FilterTargets"/> at the end of TextEffectRuntime.CollectTargets; the legacy Spell/Trap
    /// mirror never sees them (SpellTrapEffects.UsesCompiledActivationPending routes the card through the compiled flow).
    /// "except this card" on such a pick excludes every card of its name (Night Assailant's Lua: not IsCode(id)).</item>
    /// <item><b>Emissary of the Afterlife</b> (<see cref="EachPlayerAddsFromDeck"/>): the activator, then the opponent, each
    /// select 1 Level N or lower Normal Monster in their own Deck (AskCardPick; the AI takes the highest ATK); both are added
    /// together.</item>
    /// <item><b>Backfire</b> / <b>Blessings of the Nile</b>: FIELD TRIGGER_F markers. A send that qualifies (DuelEngine.
    /// DestroyMonster: a face-up monster destroyed and sent to its controller's GY, with the Attribute it had on the field;
    /// DiscardTriggersRuntime.NoteDiscard: a discard to the GY by an opponent's card effect) is recorded for each face-up
    /// copy active now; at the next GY-send checkpoint (StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: before each
    /// clause applies, before each Chain Link resolves, after each board refresh — one event) each copy that saw 1+ of
    /// them queues ONE mandatory trigger on the simultaneous-trigger Chain (Cold Wave stops it).</item>
    /// <item><b>"7"</b> (<see cref="OnSevenActivated"/>): as its activation resolves with exactly N face-up copies in your
    /// Spell &amp; Trap Zones, a mandatory trigger (Lua RaiseSingleEvent from s.operation only): draw (if still N), then
    /// destroy every face-up copy on the field together.</item>
    /// <item><b>Beckoning Light</b> (<see cref="DiscardHandThenAdd"/>, condition <see cref="HandAndAttributeMonstersInGy"/>):
    /// the whole hand is discarded as one event; if the GY then holds at least as many monsters of the Attribute as cards
    /// reached the GY, exactly that many are chosen (not targeted) and added; otherwise none.</item>
    /// </list>
    /// </summary>
    public static class GyHandAndSummonEventTriggersRuntime
    {
        /// <summary><see cref="EffectClause.TargetCardKind"/>: a Flip monster (Lua IsType(TYPE_FLIP)).</summary>
        public const string FlipKind = "Flip";

        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Beckoning Light, Lua s.target chk): 1+ card in your hand (not this
        /// card) and at least that many <see cref="EffectClause.AttributeFilter"/> monsters in your GY that can be added.
        /// </summary>
        public const string HandAndAttributeMonstersInGy = "GyHandHandAndAttributeMonstersInGy";

        sealed class HandSend
        {
            public CardInstance Card;
            public DuelistState Owner;
            public int Stamp;
        }

        sealed class EventNote
        {
            public EffectActionKind Action;
            public DuelistState Side;
            public readonly List<(CardInstance card, int stamp)> Watchers = new();
        }

        sealed class State
        {
            public readonly List<HandSend> HandSends = new();
            public bool Firing;
            public readonly List<EventNote> Events = new();
            public bool Flushing;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        /// <summary>The GY-entry stamp of the trip a card made from a hand (ygopro IsPreviousLocation(LOCATION_HAND)).</summary>
        static readonly ConditionalWeakTable<CardInstance, StrongBox<int>> s_fromHandTrip = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── Flip monster targets ─────────────────────────────

        /// <summary>A targeted clause whose target must be a Flip monster (the compiled activation flow, both mirrors).</summary>
        public static bool IsFlipKind(EffectClause c) =>
            c != null && c.RequiresTargetChoice && string.Equals(c.TargetCardKind, FlipKind, StringComparison.Ordinal);

        static bool IsFlipMonster(CardInstance card) =>
            card?.Def != null && card.Def.IsMonster &&
            (card.Def.type ?? "").IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// TextEffectRuntime.CollectTargets: a <see cref="FlipKind"/> pick keeps Flip monsters only (in the GY too, where the
        /// generic face-up kind filter does not look); with <see cref="EffectClause.ExceptThisCard"/> no card of the source's
        /// name either.
        /// </summary>
        public static void FilterTargets(EffectClause c, CardInstance except, List<CardInstance> list)
        {
            if (c == null || list == null || !string.Equals(c.TargetCardKind, FlipKind, StringComparison.Ordinal)) return;
            list.RemoveAll(t => !IsFlipMonster(t));
            if (c.ExceptThisCard && except?.Def != null)
                list.RemoveAll(t => ReferenceEquals(t, except) ||
                                    (t?.Def != null && string.Equals(t.Def.name, except.Def.name, StringComparison.Ordinal)));
        }

        // ───────────────────────────── sent from the hand to the GY ─────────────────────────────

        static EffectClause HandSendClause(CardInstance card)
        {
            if (card?.Def == null || card.IsToken) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.SentFromHandToGy).FirstOrDefault(c => c != null);
        }

        static DuelistState GyHolder(DuelEngine engine, CardInstance card)
        {
            if (engine?.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine?.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        /// <summary>
        /// <see cref="GyEntryStamps.Entered"/>: <paramref name="card"/> just entered a GY; <paramref name="fromHand"/> = it came
        /// from a hand. A card with a compiled "When this card is sent from the hand to the GY" trigger is queued for the
        /// player whose GY holds it.
        /// </summary>
        public static void NoteEnteredGy(DuelEngine engine, CardInstance card, bool fromHand)
        {
            if (card == null) return;
            s_fromHandTrip.Remove(card);
            if (!fromHand) return;
            s_fromHandTrip.Add(card, new StrongBox<int>(card.GyEntryStamp));
            if (engine == null || engine.GameOver || HandSendClause(card) == null) return;
            var holder = GyHolder(engine, card);
            if (holder == null) return;
            var st = StateOf(engine);
            st.HandSends.RemoveAll(q => ReferenceEquals(q.Card, card));
            st.HandSends.Add(new HandSend { Card = card, Owner = holder, Stamp = card.GyEntryStamp });
            Console.WriteLine($"[GyHandAndSummonEventTriggers] NoteEnteredGy: {card.Name} queued, HandSends={st.HandSends.Count}, holder={holder.Name}, stamp={card.GyEntryStamp}, clause={HandSendClause(card)?.Action}");
        }

        /// <summary>The card is in a GY on a trip that came from a hand.</summary>
        public static bool CameFromHand(CardInstance card) =>
            card != null && s_fromHandTrip.TryGetValue(card, out var trip) && trip.Value == card.GyEntryStamp;

        /// <summary>
        /// DuelEngine.Notify, once play is idle: the queued "sent from the hand to the GY" triggers activate (mandatory). A card
        /// that left that GY (or made a new trip) before then has none. The turn player's are Chain Link 1 ….
        /// </summary>
        public static void FireQueuedHandSends(DuelEngine engine)
        {
            if (engine == null) return;
            Console.WriteLine("[GyHandAndSummonEventTriggers] FireQueuedHandSends called");
            if (engine.GameOver || !s_state.TryGetValue(engine, out var st) || st.Firing ||
                st.HandSends.Count == 0)
            {
                var hasState = s_state.TryGetValue(engine, out var st2);
                var firing = st2?.Firing ?? false;
                var count = st2?.HandSends.Count ?? -1;
                Console.WriteLine("[GyHandAndSummonEventTriggers] FireQueuedHandSends early return: GameOver=" + engine.GameOver + " HasState=" + hasState + " Firing=" + firing + " Count=" + count);
                return;
            }
            if (engine.PendingActivation != null || engine.PendingResponse != null)
            {
                Console.WriteLine("[GyHandAndSummonEventTriggers] FireQueuedHandSends early return: PendingActivation=" + (engine.PendingActivation != null) + " PendingResponse=" + (engine.PendingResponse != null));
                return;
            }
            var due = st.HandSends.ToList();
            st.HandSends.Clear();
            var live = due.Where(q => (ReferenceEquals(q.Owner, engine.Player) || ReferenceEquals(q.Owner, engine.Opponent)) &&
                                      q.Owner.Graveyard.Contains(q.Card) && q.Card.GyEntryStamp == q.Stamp &&
                                      HandSendClause(q.Card) != null &&
                                      !NegationMonstersRuntime.NegatedInGy(engine, q.Card))
                .OrderBy(q => ReferenceEquals(q.Owner, engine.TurnPlayer) ? 0 : 1)
                .ToList();
            Console.WriteLine("[GyHandAndSummonEventTriggers] FireQueuedHandSends: due=" + due.Count + " live=" + live.Count);
            if (live.Count == 0) return;
            st.Firing = true;
            ActivateHandSends(engine, st, live, 0, new List<TriggerChainResponsesRuntime.Seat>());
        }

        /// <summary>Activate the triggers one by one (each chooses its target), then put them on one Chain.</summary>
        static void ActivateHandSends(DuelEngine engine, State st, List<HandSend> live, int index,
            List<TriggerChainResponsesRuntime.Seat> seats)
        {
            if (engine.GameOver)
            {
                st.Firing = false;
                return;
            }

            if (index >= live.Count)
            {
                st.Firing = false;
                if (TriggerChainResponsesRuntime.TrySeat(engine, seats)) return;
                foreach (var s in seats)
                {
                    if (engine.GameOver) return;
                    s.Resolve?.Invoke();
                }

                engine.NotifyPublic();
                return;
            }

            var q = live[index];
            var clause = HandSendClause(q.Card);
            engine.Log($"{q.Card.Name} was sent from the hand to the GY — its effect activates. RequiresTargetChoice={clause?.RequiresTargetChoice} TargetCardKind={clause?.TargetCardKind}");

            void Seat(CardInstance target)
            {
                var seat = new TriggerChainResponsesRuntime.Seat
                {
                    Controller = q.Owner,
                    Card = q.Card,
                    From = CardLocation.Graveyard,
                    Resolve = () => ResolveHandSend(engine, q.Owner, q.Card, clause, target)
                };
                if (target != null) seat.Targets.Add(target);
                seats.Add(seat);
                ActivateHandSends(engine, st, live, index + 1, seats);
            }

            if (clause == null || !clause.RequiresTargetChoice)
            {
                Seat(null);
                return;
            }

            List<CardInstance> Candidates() => TextEffectRuntime.CollectTargets(engine, q.Owner, clause, q.Card);
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, q.Owner, q.Card, new CardPickRequest
            {
                Candidates = Candidates,
                Kind = TextEffectRuntime.MapTargetKind(clause),
                Prompt = $"{q.Card.Name}: target 1 Flip monster in your GY to return to your hand.",
                AiChoose = list => TextEffectRuntime.AutoPick(clause, list, q.Owner, engine),
                HoldsChain = false,
                Finish = target =>
                {
                    if (target == null)
                        engine.Log($"{q.Card.Name}: no legal target — the effect resolves with nothing to do.");
                    Seat(target);
                }
            });
        }

        /// <summary>The link resolves: the target, still in the GY and still legal, goes to the hand (Lua IsRelateToEffect).</summary>
        static void ResolveHandSend(DuelEngine engine, DuelistState owner, CardInstance source, EffectClause clause,
            CardInstance target)
        {
            if (engine == null || owner == null || clause == null || engine.GameOver) return;
            if (clause.RequiresTargetChoice)
            {
                if (target == null) return;
                if (!owner.Graveyard.Contains(target) ||
                    !TextEffectRuntime.CollectTargets(engine, owner, clause, source).Contains(target))
                {
                    engine.Log($"{source.Name}: {target.Name} is no longer a legal target — nothing is returned.");
                    return;
                }
            }

            var d = false;
            TextEffectRuntime.ApplyClause(engine, owner, source, clause, target, ref d, ref d);
            if (target != null && owner.Hand.Contains(target))
                engine.Log($"{source.Name}: {owner.Name} reveals {target.Name}.");
            engine.NotifyPublic();
        }

        // ───────────────────────────── Emissary of the Afterlife ─────────────────────────────

        static List<CardInstance> NormalsInDeck(DuelEngine engine, DuelistState owner, int levelCap)
        {
            var list = new List<CardInstance>();
            if (engine?.Database == null || owner?.Deck == null) return list;
            var seen = new HashSet<int>();
            foreach (var id in owner.Deck)
            {
                if (!seen.Add(id) || !engine.Database.TryGet(id, out var def) || def == null || !def.IsNormalMonster ||
                    def.level > levelCap)
                    continue;
                list.Add(engine.CreateCardInstance(id));
            }

            return list;
        }

        static void PickFromOwnDeck(DuelEngine engine, DuelistState chooser, CardInstance source, int levelCap,
            Action<CardInstance> then)
        {
            if (chooser == null)
            {
                then(null);
                return;
            }

            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, chooser, source, new CardPickRequest
            {
                Candidates = () => NormalsInDeck(engine, chooser, levelCap),
                Kind = EffectTargetKind.CardInYourDeck,
                Prompt = $"{source?.Name}: select 1 Level {levelCap} or lower Normal Monster in your Deck to add to your hand.",
                AiChoose = list => list.OrderByDescending(c => c?.Def?.atk ?? 0).FirstOrDefault(),
                Finish = then
            });
        }

        static void AddPicked(DuelEngine engine, DuelistState who, CardInstance source, CardInstance pick)
        {
            if (who == null) return;
            if (pick == null)
            {
                engine.Log($"{source?.Name}: {who.Name} has no Normal Monster in the Deck to add.");
                return;
            }

            var idx = who.Deck.IndexOf(pick.CardId);
            if (idx < 0)
            {
                engine.Log($"{source?.Name}: {pick.Name} is no longer in {who.Name}'s Deck.");
                return;
            }

            // PerCopyDeck batch (v163): the pick was made before the removal; it takes the copy's owner.
            PerCopyDeckRuntime.TakeFromDeck(who, idx, pick);
            who.Hand.Add(pick);
            engine.Log($"{source?.Name}: {who.Name} adds {pick.Name} from the Deck to the hand (revealed).");
        }

        /// <summary>
        /// ApplyClause arm (<see cref="EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck"/>): Lua s.operation —
        /// tp selects from its Deck, then 1-tp from its own, then both cards go to the hands together and are revealed.
        /// </summary>
        public static void EachPlayerAddsFromDeck(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || clause == null || engine.GameOver) return;
            var opp = engine.OpponentOf(who);
            var cap = clause.Amount > 0 ? clause.Amount : 3;
            PickFromOwnDeck(engine, who, source, cap, first =>
                PickFromOwnDeck(engine, opp, source, cap, second =>
                {
                    AddPicked(engine, who, source, first);
                    AddPicked(engine, opp, source, second);
                    engine.NotifyPublic();
                }));
        }

        // ───────────────────────────── Backfire / Blessings of the Nile ─────────────────────────────

        static EffectClause LiveClause(DuelEngine engine, DuelistState side, CardInstance st, EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || side == null) return null;
            if (!side.TryFindSpellTrap(st, out _)) return null;
            var prog = CompiledEffectCache.GetOrCompile(st);
            if (prog == null || !prog.FullyCompiled) return null;
            var clause = prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
            if (clause == null) return null;
            // Its own activation is still an unresolved Chain Link: it is not active yet.
            if (engine.Chain != null &&
                engine.Chain.Links.Any(l => l != null && ReferenceEquals(l.Card, st) && !l.Resolved))
                return null;
            return clause;
        }

        static List<(CardInstance card, EffectClause clause)> LiveCopies(DuelEngine engine, DuelistState side,
            EffectActionKind action)
        {
            var list = new List<(CardInstance, EffectClause)>();
            if (engine == null || side == null) return list;
            foreach (var st in side.SpellTrapsOnField().ToList())
            {
                var c = LiveClause(engine, side, st, action);
                if (c != null) list.Add((st, c));
            }

            return list;
        }

        static void Note(DuelEngine engine, DuelistState side, EffectActionKind action,
            Func<EffectClause, bool> qualifies)
        {
            var live = LiveCopies(engine, side, action);
            if (live.Count == 0) return;
            var note = new EventNote { Action = action, Side = side };
            foreach (var (card, clause) in live)
                if (qualifies(clause))
                    note.Watchers.Add((card, card.FieldEntryStamp));
            if (note.Watchers.Count > 0) StateOf(engine).Events.Add(note);
        }

        /// <summary>
        /// DuelEngine.DestroyMonster: <paramref name="card"/>, face-up in <paramref name="controller"/>'s Monster Zone with
        /// Attribute <paramref name="attributeOnField"/>, was destroyed and sent to <paramref name="gyOwner"/>'s GY. Backfire
        /// needs its own controller's monster in its own GY (Lua IsPreviousControler(tp) and IsControler(tp)).
        /// </summary>
        public static void NoteDestroyedToGy(DuelEngine engine, DuelistState controller, DuelistState gyOwner,
            CardInstance card, string attributeOnField)
        {
            if (engine == null || controller == null || card?.Def == null || card.IsToken) return;
            if (!ReferenceEquals(controller, gyOwner) || gyOwner.Graveyard == null || !gyOwner.Graveyard.Contains(card)) return;
            Note(engine, controller, EffectActionKind.InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy,
                clause => !string.IsNullOrEmpty(clause.AttributeFilter) &&
                          string.Equals(attributeOnField, clause.AttributeFilter, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// DiscardTriggersRuntime.NoteDiscard: <paramref name="card"/> was discarded from <paramref name="owner"/>'s hand by an
        /// effect of a card <paramref name="byEffectOf"/> controls (never a cost). Blessings of the Nile: by the opponent's
        /// card, and in its controller's GY (Lua rp~=tp, IsControler(tp), REASON_EFFECT + REASON_DISCARD).
        /// </summary>
        public static void NoteDiscardedByEffect(DuelEngine engine, DuelistState owner, CardInstance card,
            DuelistState byEffectOf)
        {
            if (engine == null || owner == null || card == null || byEffectOf == null || ReferenceEquals(owner, byEffectOf))
                return;
            if (owner.Graveyard == null || !owner.Graveyard.Contains(card)) return;
            Note(engine, owner, EffectActionKind.GainLpEachTimeDiscardedByOpponentEffect, _ => true);
        }

        /// <summary>
        /// StandbyGrowthAndMaintenanceRuntime.GySendCheckpoint: the sends recorded since the last checkpoint were one event;
        /// each copy that saw 1+ of them and is still active (same field presence) queues one mandatory trigger.
        /// </summary>
        public static void FlushEventTriggers(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.Flushing || st.Events.Count == 0) return;
            var events = st.Events.ToList();
            st.Events.Clear();
            st.Flushing = true;
            try
            {
                var fired = new HashSet<CardInstance>();
                foreach (var ev in events)
                foreach (var (card, stamp) in ev.Watchers)
                {
                    if (engine.GameOver) return;
                    if (card.FieldEntryStamp != stamp || !fired.Add(card)) continue;
                    var clause = LiveClause(engine, ev.Side, card, ev.Action);
                    if (clause == null) continue;
                    // Cold Wave (LocksAndZones batch): a Spell/Trap Card's trigger cannot be activated.
                    if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card)) continue;
                    QueueEventTrigger(engine, ev.Side, card, clause, ev.Action, stamp);
                }
            }
            finally
            {
                st.Flushing = false;
            }
        }

        static void QueueEventTrigger(DuelEngine engine, DuelistState side, CardInstance card, EffectClause clause,
            EffectActionKind action, int stamp)
        {
            var amount = clause.Amount;
            if (action == EffectActionKind.InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy)
            {
                engine.Log($"{card.Name}: a face-up {clause.AttributeFilter} monster {side.Name} controlled was destroyed " +
                           "and sent to the GY — its effect activates.");
                // Lua s.operation has no IsRelateToEffect: the damage is inflicted even if Backfire left the field.
                SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
                {
                    if (engine.GameOver) return;
                    var victim = engine.OpponentOf(side);
                    if (victim == null) return;
                    engine.Log($"{card.Name}: {amount} damage to {victim.Name}.");
                    engine.ApplyEffectDamage(victim, amount, card.Name, card);
                });
                return;
            }

            engine.Log($"{card.Name}: {side.Name} discarded by the opponent's card effect — its effect activates.");
            // Lua s.operation: IsRelateToEffect — Blessings must still be on the field.
            SimultaneousTriggersRuntime.QueueTrigger(engine, side, card, optional: false, () =>
            {
                if (engine.GameOver) return;
                if (card.FieldEntryStamp != stamp || !side.TryFindSpellTrap(card, out _) || !card.FaceUp)
                {
                    engine.Log($"{card.Name} is no longer on the field — no LP are gained.");
                    return;
                }

                TextEffectRuntime.ApplyLifePointGain(engine, side, amount, card.Name);
            });
        }

        // ───────────────────────────── "7" ─────────────────────────────

        static bool IsFaceUpNamed(CardInstance c, string name) =>
            c?.Def != null && c.FaceUp && !string.IsNullOrEmpty(name) && c.IsNamed(name);

        static int FaceUpNamedInSpellTrapZones(DuelistState who, string name) =>
            who?.SpellTrapsOnField().Count(c => IsFaceUpNamed(c, name)) ?? 0;

        static IEnumerable<CardInstance> FaceUpNamedOnField(DuelEngine engine, string name)
        {
            foreach (var side in new[] { engine.TurnPlayer, engine.OpponentOf(engine.TurnPlayer) })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                    if (IsFaceUpNamed(m, name)) yield return m;
                foreach (var st in side.SpellTrapsOnField())
                    if (IsFaceUpNamed(st, name)) yield return st;
                if (IsFaceUpNamed(side.FieldSpellZone?.Occupant, name)) yield return side.FieldSpellZone.Occupant;
            }
        }

        static bool StillOnField(DuelistState who, CardInstance source, int stamp) =>
            source != null && source.FieldEntryStamp == stamp && who.TryFindSpellTrap(source, out _) && source.FaceUp;

        /// <summary>
        /// ApplyClause arm (<see cref="EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed"/>), as the card's
        /// activation resolves (Lua s.operation): still on the field, and exactly <see cref="EffectClause.ConditionAmount"/>
        /// face-up copies in <paramref name="who"/>'s Spell &amp; Trap Zones — its mandatory trigger goes on the next
        /// simultaneous-trigger Chain. A copy that arrives any other way raises nothing.
        /// </summary>
        public static void OnSevenActivated(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null || engine.GameOver) return;
            var name = clause.NamedCard;
            var need = clause.ConditionAmount > 0 ? clause.ConditionAmount : 3;
            if (!StillOnField(who, source, source.FieldEntryStamp))
            {
                engine.Log($"{source.Name} is no longer on the field — nothing happens.");
                return;
            }

            var count = FaceUpNamedInSpellTrapZones(who, name);
            if (count != need)
            {
                engine.Log($"{source.Name}: {count} face-up \"{name}\" in {who.Name}'s Spell & Trap Zones — nothing happens.");
                return;
            }

            // Cold Wave (LocksAndZones batch): a Spell/Trap Card's trigger cannot be activated.
            if (LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, source))
            {
                engine.Log($"{source.Name}: Spell/Trap effects cannot be activated — its trigger does not activate.");
                return;
            }

            var stamp = source.FieldEntryStamp;
            engine.Log($"{source.Name}: {need} face-up \"{name}\" on {who.Name}'s side — its effect activates.");
            SimultaneousTriggersRuntime.QueueTrigger(engine, who, source, optional: false,
                () => ResolveSeven(engine, who, source, clause, stamp));
        }

        static void ResolveSeven(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause, int stamp)
        {
            if (engine.GameOver) return;
            // Lua s.drop: IsRelateToEffect — nothing at all once this card left the field.
            if (!StillOnField(who, source, stamp))
            {
                engine.Log($"{source.Name} is no longer on the field — its effect does not resolve.");
                return;
            }

            var name = clause.NamedCard;
            var need = clause.ConditionAmount > 0 ? clause.ConditionAmount : 3;
            if (FaceUpNamedInSpellTrapZones(who, name) == need)
                engine.Draw(who, clause.Amount > 0 ? clause.Amount : 3, silent: false);
            else
                engine.Log($"{source.Name}: no longer {need} face-up \"{name}\" — no draw.");
            if (engine.GameOver) return;
            var all = FaceUpNamedOnField(engine, name).ToList();
            if (all.Count == 0) return;
            engine.Log($"{source.Name}: every face-up \"{name}\" on the field is destroyed.");
            FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var c in all)
                {
                    if (engine.GameOver) return;
                    if (engine.ControllerOf(c) == null) continue;
                    TextEffectRuntime.DestroyCard(engine, c, source, false, who);
                }
            });
        }

        // ───────────────────────────── Beckoning Light ─────────────────────────────

        static bool IsAttributeMonster(CardInstance c, string attribute) =>
            c?.Def != null && c.Def.IsMonster && !string.IsNullOrEmpty(attribute) &&
            string.Equals(c.Def.attribute, attribute, StringComparison.OrdinalIgnoreCase);

        /// <summary>The <paramref name="attribute"/> monsters in <paramref name="who"/>'s GY that can be added to the hand.</summary>
        static List<CardInstance> AddableFromGy(DuelEngine engine, DuelistState who, CardInstance source, string attribute) =>
            who?.Graveyard == null
                ? new List<CardInstance>()
                : who.Graveyard.Where(g => IsAttributeMonster(g, attribute) &&
                                           !ContinuousProtections.GyMovementLocked(engine, source, g)).ToList();

        /// <summary>TextEffectRuntime.ActivationConditionsMet: <see cref="HandAndAttributeMonstersInGy"/>.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = true;
            reason = null;
            if (c == null || c.ActivationCondition != HandAndAttributeMonstersInGy) return false;
            var hand = who?.Hand?.Count(h => h != null && !ReferenceEquals(h, card)) ?? 0;
            var gy = AddableFromGy(engine, who, card, c.AttributeFilter).Count;
            ok = hand > 0 && gy >= hand;
            if (!ok)
                reason = hand == 0
                    ? "You have no card in your hand to discard."
                    : $"You need at least {hand} {c.AttributeFilter} monster(s) in your GY.";
            return true;
        }

        /// <summary>
        /// ApplyClause arm (<see cref="EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard"/>), Lua
        /// s.operation: the whole hand is discarded (REASON_EFFECT + REASON_DISCARD, one event); ct = the cards that reached a
        /// GY. If ct &gt; 0 and the GY then holds at least ct monsters of the Attribute, exactly ct of them are chosen (a
        /// selection, not a target) and added to the hand; otherwise none.
        /// </summary>
        public static void DiscardHandThenAdd(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who?.Hand == null || clause == null || engine.GameOver) return;
            var hand = who.Hand.Where(h => h != null && !ReferenceEquals(h, source)).ToList();
            var discarded = 0;
            DiscardTriggersRuntime.BeginScope(engine);
            try
            {
                foreach (var h in hand)
                {
                    var gone = MonsterEffects.DiscardFromHandByInstance(who, h, DiscardCause.Effect, who);
                    if (gone == null) continue;
                    DiscardTriggersRuntime.NoteDiscard(engine, who, gone, who);
                    if (GyHolder(engine, gone) != null) discarded++;
                }
            }
            finally
            {
                DiscardTriggersRuntime.EndScope(engine);
            }

            engine.Log($"{source?.Name}: {who.Name} discards {hand.Count} card(s); {discarded} reached the GY.");
            if (discarded == 0 || engine.GameOver) return;
            var attribute = clause.AttributeFilter;
            if (AddableFromGy(engine, who, source, attribute).Count < discarded)
            {
                engine.Log($"{source?.Name}: fewer than {discarded} {attribute} monster(s) in the GY — none are added.");
                return;
            }

            var picked = new List<CardInstance>();
            List<CardInstance> Pool() => AddableFromGy(engine, who, source, attribute).Where(g => !picked.Contains(g)).ToList();

            void AddAll()
            {
                var added = new List<string>();
                foreach (var p in picked)
                {
                    if (!who.Graveyard.Contains(p) || ContinuousProtections.GyMovementLocked(engine, source, p)) continue;
                    who.Graveyard.Remove(p);
                    who.Hand.Add(p);
                    added.Add(p.Name);
                }

                engine.Log($"{source?.Name}: {who.Name} adds {string.Join(", ", added)} from the GY to the hand (revealed).");
                engine.NotifyPublic();
            }

            void PickNext()
            {
                if (picked.Count >= discarded || engine.GameOver)
                {
                    AddAll();
                    return;
                }

                ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                {
                    Candidates = Pool,
                    Kind = EffectTargetKind.MonsterInEitherGy,
                    Prompt = $"{source?.Name}: select a {attribute} monster in your GY to add to your hand " +
                             $"({picked.Count + 1} of {discarded}).",
                    AiChoose = list => list.OrderByDescending(c => c?.Def?.atk ?? 0).FirstOrDefault(),
                    Finish = p =>
                    {
                        if (p == null)
                        {
                            AddAll();
                            return;
                        }

                        picked.Add(p);
                        PickNext();
                    }
                });
            }

            PickNext();
        }
    }
}
