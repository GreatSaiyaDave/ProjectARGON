using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SimultaneousTriggers batch (v160): the simultaneous trigger queue — SEGOC, "Simultaneous Effects Go On Chain".
    /// Trigger effects whose conditions are met by the same event go on ONE Chain: the turn player's mandatory
    /// triggers first, then the turn player's optional ones, then the non-turn player's mandatory, then optional
    /// (so the non-turn player's resolve first). Each player orders their own: by default the effects of one group
    /// resolve in the order their events happened (the AI always keeps it); in a FLIP Chain a human with two or more
    /// effects in one group picks which goes on next (a <see cref="PendingActivation"/> over those face-up monsters;
    /// Cancel keeps the default order).
    /// <list type="bullet">
    /// <item><b>FLIP effects</b> of monsters a resolving effect flipped face-up (Swords of Revealing Light, Curse of
    /// Fiend; <see cref="QueueFlip"/>, from DuelEngine.QueueFlipEffectAfterResolution) wait until that effect and its
    /// Chain are over and play is idle (<see cref="Flush"/>, from DuelEngine.Notify; the turn player's Standby pause
    /// counts as idle). They activate in Chain order — costs paid and targets chosen then (AI: at once; a human: the
    /// usual target pick, which holds the Chain) — and resolve in reverse order on the targets chosen. A FLIP effect
    /// still resolves after its monster has left the field (or an earlier link destroyed it); only the parts that act on
    /// "this card" (<see cref="NeedsSourceFaceUp"/>) need it still face-up. A target that has left its zone is not
    /// affected. The registered scripts (Man-Eater Bug, Magician of Faith) are split the same way
    /// (MonsterEffects.TryRegisteredFlipTargets / ResolveRegisteredFlip).</item>
    /// <item><b>"Position changed" triggers</b> of a mass position change (Curse of Fiend; <see cref="Capture"/> /
    /// <see cref="QueueThisCardPositionChanged"/>) join the FLIP effects' Chain; they activate as they resolve.</item>
    /// <item><b>"Sent to the GY" triggers</b> held by FlipMassEffectsRuntime.Simultaneously (Dark Hole, both battlers
    /// destroyed, a Summon's Tributes; <see cref="RunHeld"/>) go on one Chain at once, in the same SEGOC order (each
    /// player's own in the default order, no order pick); each activates as it resolves. Sent by a Chain Link that was not the last to resolve, an optional "When … you can"
    /// effect misses the timing (<see cref="MissesWhenTiming"/>, read by TextEffectRuntime.TryResolveSentToGy).</item>
    /// </list>
    /// Nobody can chain to these Chains (as with every FLIP effect in this engine). Only one prompt is open at a time:
    /// a Chain waits while any pick is open and goes on when it is answered. State is kept per engine.
    /// </summary>
    public static class SimultaneousTriggersRuntime
    {
        enum EntryKind
        {
            /// <summary>A FLIP effect: activates (cost, targets) in Chain order, resolves later.</summary>
            Flip,
            /// <summary>A trigger that activates as it resolves (its existing one-step resolution).</summary>
            Fused
        }

        enum Where
        {
            Other,
            Field,
            Graveyard
        }

        /// <summary>A chosen target and where it was when it was chosen.</summary>
        sealed class Snap
        {
            public CardInstance Card;
            public Where Where;
            public DuelistState Owner;
            public int Stamp;
        }

        sealed class Entry
        {
            public EntryKind Kind;
            public DuelistState Controller;
            public CardInstance Card;
            public int Turn;
            public bool Optional;
            public Action Run;
            public bool MissesWhenTiming;
            public int Link;
            /// <summary>A "flipped face-up" entry (a FLIP effect, or Otohime / Maharaghi's summon-or-flip trigger).</summary>
            public bool FlipEvent;

            // Flip
            public bool NoEffect;
            public List<EffectClause> Clauses;
            public EffectClause Targeted;
            public bool Registered;
            public EffectTargetKind RegisteredKind;
            public readonly List<Snap> Picks = new();
        }

        enum Stage
        {
            Ordering,
            Activating,
            Resolving,
            /// <summary>
            /// TriggerChainResponses batch (v163): the links sit on the engine's Chain (a player could respond); each
            /// resolves there through TriggerChainResponsesRuntime.TryResolveLink. Done once that Chain is over.
            /// </summary>
            EngineChain
        }

        sealed class SegocChain
        {
            /// <summary>FLIP Chains wait for idle play; held "sent to the GY" Chains run at once.</summary>
            public bool WaitIdle;
            public Stage Stage;
            /// <summary>Placement groups: turn player's mandatory, optional, then the non-turn player's.</summary>
            public readonly List<List<Entry>> Groups = new();
            public int Group;
            public readonly List<Entry> Links = new();
            public int Next;
            /// <summary>The pick this Chain opened and waits on (null = none).</summary>
            public PendingActivation Pick;
        }

        sealed class PickContext
        {
            public SegocChain Chain;
            /// <summary>Target pick: the FLIP effect activating.</summary>
            public Entry Entry;
            /// <summary>Order pick: the effects of one group still to be placed.</summary>
            public List<Entry> Candidates;
        }

        sealed class State
        {
            public readonly List<Entry> Collected = new();
            public readonly List<SegocChain> Stack = new();
            public bool Running;
            public int CaptureDepth;
            public CardInstance MissingTiming;
            /// <summary>TriggerChainResponses batch (v163): a link of a simultaneous-trigger Chain is resolving.</summary>
            public int ResolvingDepth;
            /// <summary>TriggerChainResponses batch (v163): the card whose queued trigger is running now (not deferred again).</summary>
            public CardInstance Replaying;
            /// <summary>TriggerChainResponses batch (v163): the monster whose activated FLIP effect is resolving now.</summary>
            public CardInstance ResolvingFlip;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<PendingActivation, PickContext> s_picks = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        static bool InThisDuel(DuelEngine engine, DuelistState who) =>
            who != null && (ReferenceEquals(who, engine.Player) || ReferenceEquals(who, engine.Opponent));

        // ───────────────────────── queueing ─────────────────────────

        /// <summary>DuelEngine.QueueFlipEffectAfterResolution: a monster a resolving effect flipped face-up.</summary>
        public static void QueueFlip(DuelEngine engine, DuelistState controller, CardInstance monster)
        {
            if (engine == null || controller == null || monster == null) return;
            // One flip, one entry: a mass change (Curse of Fiend, PositionLocksRuntime) reports a flip both through
            // DuelEngine.ApplyEffectPositionChange (SpiritMonstersRuntime.NoteFlippedFaceUpByEffect) and its own queue
            // call, so Otohime's trigger was queued twice (v160 integration audit; SpiritMonsters' dedupe).
            if (StateOf(engine).Collected.Exists(x =>
                    x.FlipEvent && ReferenceEquals(x.Card, monster) && x.Turn == engine.TurnNumber))
                return;
            var prog = CompiledEffectCache.GetOrCompile(monster);
            var flip = prog?.ClausesFor(EffectTiming.Flip);
            // SpiritMonsters batch: a "Normal Summoned or flipped face-up" trigger (Otohime, Maharaghi) with no FLIP clause.
            if ((flip == null || flip.Count == 0) && SpiritMonstersRuntime.HasFlippedFaceUpTrigger(monster))
            {
                var sc = prog.ClausesFor(EffectTiming.ThisCardSummoned)
                    .Where(c => c != null && c.AlsoWhenFlippedFaceUp).ToList();
                StateOf(engine).Collected.Add(new Entry
                {
                    Kind = EntryKind.Fused,
                    FlipEvent = true,
                    Controller = controller,
                    Card = monster,
                    Turn = engine.TurnNumber,
                    Optional = sc.TrueForAll(c => c.IsOptional),
                    Run = () => SpiritMonstersRuntime.NotifyFlippedFaceUp(engine, engine.ControllerOf(monster) ?? controller, monster)
                });
                return;
            }

            // No FLIP effect (compiled or a registered script): nothing activates, so no Chain Link and no order pick for
            // it (a vanilla flipped by Swords of Revealing Light; 2026-10-18 fix, the twin of RunHeld's check).
            if (!MonsterEffects.IsRegisteredFlip(monster) && (flip == null || flip.Count == 0)) return;
            StateOf(engine).Collected.Add(new Entry
            {
                Kind = EntryKind.Flip,
                FlipEvent = true,
                Controller = controller,
                Card = monster,
                Turn = engine.TurnNumber,
                Optional = flip != null && flip.Count > 0 && flip.TrueForAll(c => c == null || c.IsOptional)
            });
        }

        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161): a trigger of <paramref name="card"/> that activates as it resolves
        /// (Serpentine Princess returned from the field to the Deck), queued with the other simultaneous triggers: it
        /// goes on the next simultaneous-trigger Chain once play is idle (<see cref="Flush"/>).
        /// </summary>
        public static void QueueTrigger(DuelEngine engine, DuelistState controller, CardInstance card, bool optional,
            Action run)
        {
            if (engine == null || controller == null || card == null || run == null) return;
            StateOf(engine).Collected.Add(new Entry
            {
                Kind = EntryKind.Fused,
                Controller = controller,
                Card = card,
                Turn = engine.TurnNumber,
                Optional = optional,
                Run = run
            });
        }

        /// <summary>True while anything is queued, ordering, activating or resolving.</summary>
        public static bool Busy(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) &&
            (st.Running || st.Stack.Count > 0 || st.Collected.Count > 0);

        /// <summary>
        /// Runs <paramref name="body"/> (one simultaneous mass position change): the "position changed" triggers it
        /// causes are queued with the FLIP effects instead of resolving in the middle of it.
        /// </summary>
        public static void Capture(DuelEngine engine, Action body)
        {
            if (body == null) return;
            if (engine == null)
            {
                body();
                return;
            }

            var st = StateOf(engine);
            st.CaptureDepth++;
            try
            {
                body();
            }
            finally
            {
                st.CaptureDepth--;
            }
        }

        /// <summary>
        /// TextEffectRuntime.NotifyPositionChanged (the compiled "position changed" triggers): inside
        /// <see cref="Capture"/>, a card with such a trigger is queued and resolves later on the Chain.
        /// </summary>
        public static bool TryCapturePositionChange(DuelEngine engine, DuelistState who, CardInstance card,
            BattlePosition from, BattlePosition to, bool destinationFaceUp, bool wasFlipSummon)
        {
            if (engine == null || card?.Def == null || !s_state.TryGetValue(engine, out var st) ||
                st.CaptureDepth <= 0)
                return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            var clauses = prog.ClausesFor(EffectTiming.PositionChanged);
            if (clauses == null || clauses.Count == 0) return false;
            st.Collected.Add(new Entry
            {
                Kind = EntryKind.Fused,
                Controller = who ?? engine.ControllerOf(card),
                Card = card,
                Turn = engine.TurnNumber,
                Optional = clauses.TrueForAll(c => c == null || c.IsOptional),
                Run = () =>
                {
                    if (engine.ControllerOf(card) == null)
                    {
                        engine.Log($"{card.Name} is no longer on the field — its effect does not resolve.");
                        return;
                    }

                    TextEffectRuntime.NotifyPositionChanged(engine, engine.ControllerOf(card), card, from, to,
                        destinationFaceUp, wasFlipSummon);
                }
            });
            return true;
        }

        /// <summary>
        /// A mass change's "this card's battle position changed" trigger (TextEffectRuntime.NotifyPositionChanged,
        /// <see cref="EffectTiming.ThisCardPositionChanged"/>) of <paramref name="card"/>, queued with the FLIP effects.
        /// </summary>
        public static void QueueThisCardPositionChanged(DuelEngine engine, CardInstance card, bool toDefense)
        {
            if (engine == null || card?.Def == null) return;
            var prog = CompiledEffectCache.GetOrCompile(card);
            var clauses = prog?.ClausesFor(EffectTiming.ThisCardPositionChanged)
                .Where(c => c != null && c.PositionChangeToDefense == toDefense).ToList();
            if (clauses == null || clauses.Count == 0) return;
            StateOf(engine).Collected.Add(new Entry
            {
                Kind = EntryKind.Fused,
                Controller = engine.ControllerOf(card),
                Card = card,
                Turn = engine.TurnNumber,
                Optional = clauses.TrueForAll(c => c.IsOptional),
                Run = () => TextEffectRuntime.NotifyPositionChanged(engine, card, toDefense)
            });
        }

        /// <summary>
        /// FlipMassEffectsRuntime.Simultaneously: the "sent to the GY" triggers one event held, as one Chain, at once.
        /// </summary>
        public static void RunHeld(DuelEngine engine,
            List<(DuelistState Owner, CardInstance Card, bool Destroyed, bool DestroyedByBattle, Action Run)> held)
        {
            if (engine == null || held == null || held.Count == 0) return;
            // Sent while more Chain Links are still to resolve: the send is not the last thing to happen.
            var misses = engine.IsResolvingChain && engine.Chain.Count > 0;
            var entries = new List<Entry>();
            foreach (var (owner, card, destroyed, destroyedByBattle, run) in held)
            {
                // Only a card with a "sent from the field to the GY" effect that this send can activate has a trigger here.
                // Every card the event sent is held, so without this check a vanilla Tribute (Blue-Eyes for Judge Man), a
                // vanilla destroyed by Dark Hole, or a Tributed Mystic Tomato (only "destroyed by battle") became a Chain
                // Link of an effect that does not activate, with the opponent's response window (2026-10-18 fix).
                if (!MonsterEffects.HasSentFromFieldToGyEffect(engine, card, destroyed, destroyedByBattle)) continue;
                var prog = card != null ? CompiledEffectCache.GetOrCompile(card) : null;
                // OncePerTurnHard batch: this copy's mandatory trigger activates with its link; a second copy sent by the same
                // event has no trigger left (HasSentFromFieldToGyEffect above is false for it).
                OncePerTurnHardRuntime.NoteSentToGyQueued(engine, owner, card, prog);
                var gy = prog != null && prog.FullyCompiled ? prog.ClausesFor(EffectTiming.SentFromFieldToGy) : null;
                entries.Add(new Entry
                {
                    Kind = EntryKind.Fused,
                    Controller = owner,
                    Card = card,
                    Turn = engine.TurnNumber,
                    Optional = gy != null && gy.Count > 0 && gy.TrueForAll(c => c == null || c.IsOptional),
                    MissesWhenTiming = misses,
                    Run = run
                });
            }

            // ArchfiendRiders batch: Pandemonium's search is an optional trigger of this same event (Lua s.regop raises one
            // event per player for the whole group): it goes on this Chain with the held triggers.
            foreach (var (who, card, run) in ArchfiendRidersRuntime.TakeHeldSearches(engine))
                entries.Add(new Entry
                {
                    Kind = EntryKind.Fused,
                    Controller = who,
                    Card = card,
                    Turn = engine.TurnNumber,
                    Optional = true,
                    Run = run
                });

            // DeckSwarmsAndFusionMaterials batch: Centrifugal Field's mandatory trigger for each Fusion Monster the held
            // event destroyed (Lua raises one event per Fusion; its owner activates it) goes on this Chain too.
            foreach (var (who, card, run) in DeckSwarmsAndFusionMaterialsRuntime.TakeHeldTriggers(engine))
                entries.Add(new Entry
                {
                    Kind = EntryKind.Fused,
                    Controller = who,
                    Card = card,
                    Turn = engine.TurnNumber,
                    Optional = false,
                    Run = run
                });

            // TriggerChainResponses batch (v163): sent by a resolving Chain (Dark Hole) while play opens again after it,
            // and a player may respond — the held triggers wait for that Chain to finish and go on a new Chain then.
            if (ShouldDeferPastChain(engine, entries.Select(e => e.Controller)))
            {
                engine.Log("Simultaneous effects: the \"sent to the GY\" effects activate once the current Chain has resolved.");
                StateOf(engine).Collected.AddRange(entries);
                return;
            }

            var chain = Build(engine, entries, waitIdle: false);
            if (chain == null) return;
            StateOf(engine).Stack.Add(chain);
            Drive(engine);
        }

        // ───────────────────────── TriggerChainResponses batch (v163) ─────────────────────────

        /// <summary>
        /// The triggers of <paramref name="controllers"/> happen while a Chain (or a simultaneous-trigger Chain) is
        /// resolving, play opens again once it is over, and the opponent of one of them may respond: they wait for that
        /// Chain to finish and then go on a new Chain (official timing; a response window is possible there).
        /// </summary>
        static bool ShouldDeferPastChain(DuelEngine engine, IEnumerable<DuelistState> controllers)
        {
            if (engine == null || engine.GameOver) return false;
            if (!engine.IsResolvingChain && StateOf(engine).ResolvingDepth == 0) return false;
            if (!engine.OpenPlayAfterChain()) return false;
            return controllers.Any(c => InThisDuel(engine, c) && TriggerChainResponsesRuntime.OpponentMayRespond(engine, c));
        }

        /// <summary>
        /// MonsterEffects.OnSentFromFieldToGy: <paramref name="card"/>'s "If this card is sent from the field to the GY"
        /// trigger (an activated effect that cannot miss the timing) happened while a Chain resolves. When play opens
        /// after that Chain and the opponent may respond, it is queued (<paramref name="run"/> = its resolution, as
        /// before) and goes on the next simultaneous-trigger Chain. False: resolve it now, as before.
        /// </summary>
        public static bool TryDeferSentToGy(DuelEngine engine, DuelistState owner, CardInstance card,
            CompiledCardProgram prog, bool destroyed, bool destroyedByBattle, Action run)
        {
            if (engine == null || owner == null || card?.Def == null || !card.Def.IsMonster || run == null) return false;
            var gy = prog != null && prog.FullyCompiled ? prog.ClausesFor(EffectTiming.SentFromFieldToGy) : null;
            if (gy == null || gy.Count == 0) return false;
            // This send does not meet the trigger's condition (sent but not destroyed, …): nothing activates, so nothing is
            // queued for a later Chain (2026-10-18 fix, the twin of RunHeld's check).
            if (!TextEffectRuntime.SentToGyCanActivate(engine, card, prog, destroyed, destroyedByBattle)) return false;
            // Only activated "If" triggers: a "When … you can" one can miss the timing; a non-chain clause is continuous.
            if (!gy.TrueForAll(c => c != null && c.MakesChainLink &&
                                    !(c.IsOptional && c.CheckedAt == ConditionCheckedAt.Activation)))
                return false;
            // Its own queued trigger running now (a deferred or held entry replaying the send) resolves; it is not queued again.
            if (ReferenceEquals(StateOf(engine).Replaying, card)) return false;
            if (!ShouldDeferPastChain(engine, new[] { owner })) return false;
            // OncePerTurnHard batch: queued now, so its once per turn by name is used now.
            OncePerTurnHardRuntime.NoteSentToGyQueued(engine, owner, card, prog);
            engine.Log($"{card.Name}: its effect activates once the current Chain has resolved.");
            StateOf(engine).Collected.Add(new Entry
            {
                Kind = EntryKind.Fused,
                Controller = owner,
                Card = card,
                Turn = engine.TurnNumber,
                Optional = gy.TrueForAll(c => c.IsOptional),
                Run = run
            });
            return true;
        }

        /// <summary>
        /// TlmFlipsControlAndZoneEvents batch: a mandatory trigger of <paramref name="card"/> (controlled by
        /// <paramref name="controller"/>; Criosphinx) that happened while a Chain resolves. When play opens after that
        /// Chain and the opponent may respond, it is queued (<paramref name="run"/> = its resolution) and goes on the next
        /// simultaneous-trigger Chain, as <see cref="TryDeferSentToGy"/>. False: resolve it as before.
        /// </summary>
        public static bool TryDeferTriggerPastChain(DuelEngine engine, DuelistState controller, CardInstance card,
            Action run)
        {
            if (engine == null || controller == null || card?.Def == null || run == null) return false;
            if (ReferenceEquals(StateOf(engine).Replaying, card)) return false;
            if (!ShouldDeferPastChain(engine, new[] { controller })) return false;
            engine.Log($"{card.Name}: its effect activates once the current Chain has resolved.");
            StateOf(engine).Collected.Add(new Entry
            {
                Kind = EntryKind.Fused,
                Controller = controller,
                Card = card,
                Turn = engine.TurnNumber,
                Optional = false,
                Run = run
            });
            return true;
        }

        /// <summary>
        /// DuelEngine.CompleteFlipSummon: the Flip Summoned monster's FLIP effect goes on a Chain of its own (Chain Link
        /// 1: costs paid and targets chosen now) when the opponent may respond to it; the summon's response window opens
        /// after that Chain. False (nobody could respond, or play is not open): resolve it as before.
        /// </summary>
        public static bool TryChainFlipSummoned(DuelEngine engine, DuelistState controller, CardInstance monster)
        {
            if (engine == null || controller == null || monster?.Def == null) return false;
            var st = StateOf(engine);
            if (st.Running || st.Stack.Count > 0 || st.Collected.Count > 0) return false;
            if (!TriggerChainResponsesRuntime.CanSeatNow(engine) ||
                !TriggerChainResponsesRuntime.OpponentMayRespond(engine, controller))
                return false;
            var flip = CompiledEffectCache.GetOrCompile(monster)?.ClausesFor(EffectTiming.Flip);
            if (!MonsterEffects.IsRegisteredFlip(monster) && (flip == null || flip.Count == 0)) return false;
            var chain = Build(engine, new List<Entry>
            {
                new()
                {
                    Kind = EntryKind.Flip,
                    FlipEvent = true,
                    Controller = controller,
                    Card = monster,
                    Turn = engine.TurnNumber,
                    Optional = flip != null && flip.Count > 0 && flip.TrueForAll(c => c == null || c.IsOptional)
                }
            }, waitIdle: false);
            if (chain == null) return false;
            st.Stack.Add(chain);
            Drive(engine);
            return true;
        }

        /// <summary>
        /// Every link of <paramref name="chain"/> has activated: seat them on the engine's Chain when this is the only
        /// simultaneous-trigger Chain, play is open and the opponent has a legal response (TriggerChainResponsesRuntime).
        /// </summary>
        static bool TrySeatOnEngineChain(DuelEngine engine, SegocChain chain)
        {
            var st = StateOf(engine);
            if (st.Stack.Count != 1 || !ReferenceEquals(st.Stack[0], chain) || st.ResolvingDepth > 0) return false;
            if (!TriggerChainResponsesRuntime.CanSeatNow(engine)) return false;
            var seats = new List<TriggerChainResponsesRuntime.Seat>();
            foreach (var e in chain.Links)
            {
                if (e.Kind == EntryKind.Flip && e.NoEffect) continue; // never activated (no target, Royal Command)
                var entry = e;
                var seat = new TriggerChainResponsesRuntime.Seat
                {
                    Controller = e.Controller,
                    Card = e.Card,
                    From = TriggerChainResponsesRuntime.LocationOf(engine, e.Card),
                    Resolve = () => Resolve(engine, chain, entry)
                };
                foreach (var p in e.Picks)
                    if (p?.Card != null) seat.Targets.Add(p.Card);
                // DeckSwarmsAndFusionMaterials batch: the target a Centrifugal Field trigger chose as it activated.
                seat.Targets.AddRange(DeckSwarmsAndFusionMaterialsRuntime.ActivationTargets(e.Run));
                seats.Add(seat);
            }

            return seats.Count > 0 && TriggerChainResponsesRuntime.TrySeat(engine, seats);
        }

        /// <summary>
        /// ArchfiendRiders batch: a queued or held entry of <paramref name="card"/> is running now (a held send replaying
        /// MonsterEffects.OnSentFromFieldToGy), not a new send of it.
        /// </summary>
        public static bool IsReplaying(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_state.TryGetValue(engine, out var st) &&
            ReferenceEquals(st.Replaying, card);

        /// <summary>TextEffectRuntime.TryResolveSentToGy: <paramref name="card"/>'s optional "When" effects miss the timing.</summary>
        public static bool MissesWhenTiming(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_state.TryGetValue(engine, out var st) &&
            ReferenceEquals(st.MissingTiming, card);

        /// <summary>DuelEngine.Notify: start or continue the queued Chains when play allows.</summary>
        public static void Flush(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            if (st.Collected.Count == 0 && st.Stack.Count == 0) return;
            Drive(engine);
        }

        // ───────────────────────── building and driving a Chain ─────────────────────────

        static SegocChain Build(DuelEngine engine, List<Entry> entries, bool waitIdle)
        {
            var tp = engine.TurnPlayer;
            var ntp = engine.OpponentOf(tp);
            var live = new List<Entry>();
            foreach (var e in entries)
            {
                if (e?.Card == null || !InThisDuel(engine, e.Controller)) continue;
                if (e.Turn != engine.TurnNumber)
                {
                    engine.Log($"{e.Card.Name}: its trigger was left from an earlier turn — it does not activate.");
                    continue;
                }

                live.Add(e);
            }

            if (live.Count == 0) return null;
            var chain = new SegocChain { WaitIdle = waitIdle, Stage = Stage.Ordering };
            // Default order inside a group: placed last-happened first, so the first to happen resolves first.
            foreach (var side in new[] { tp, ntp })
            foreach (var optional in new[] { false, true })
                chain.Groups.Add(live.Where(e => ReferenceEquals(e.Controller, side) && e.Optional == optional)
                    .Reverse().ToList());
            if (waitIdle && live.Count > 1)
                engine.Log($"Simultaneous effects: {live.Count} effects go on one Chain " +
                           $"({tp?.Name}'s first, so {ntp?.Name}'s resolve first).");
            return chain;
        }

        static void Drive(DuelEngine engine)
        {
            var st = StateOf(engine);
            if (st.Running) return;
            st.Running = true;
            var finished = false;
            try
            {
                for (var guard = 0; guard < 1024; guard++)
                {
                    if (engine.GameOver)
                    {
                        st.Collected.Clear();
                        st.Stack.Clear();
                        break;
                    }

                    if (st.Stack.Count == 0)
                    {
                        if (st.Collected.Count == 0 || !engine.PlayIdleForTriggerChain()) break;
                        var built = Build(engine, st.Collected.ToList(), waitIdle: true);
                        st.Collected.Clear();
                        if (built != null) st.Stack.Add(built);
                        continue;
                    }

                    var top = st.Stack[st.Stack.Count - 1];
                    if (!InThisDuel(engine, top.Links.Concat(top.Groups.SelectMany(g => g))
                            .Select(e => e.Controller).FirstOrDefault()))
                    {
                        st.Stack.Remove(top); // a restarted Duel
                        continue;
                    }

                    if (!CanStep(engine, top)) break;
                    if (Step(engine, top))
                    {
                        st.Stack.Remove(top);
                        finished = true;
                    }
                }
            }
            finally
            {
                st.Running = false;
            }

            if (finished && !engine.GameOver) engine.NotifyPublic();
        }

        static bool CanStep(DuelEngine engine, SegocChain chain)
        {
            if (chain.Pick != null)
            {
                if (engine.PendingActivation != null) return false; // the player's pick (or another) is open
                // Our pick was cleared without an answer (replaced by another prompt): make it automatically.
                if (s_picks.TryGetValue(chain.Pick, out var ctx))
                {
                    s_picks.Remove(chain.Pick);
                    chain.Pick = null;
                    CompleteAutomatically(engine, ctx);
                }
                else
                    chain.Pick = null;
            }

            if (engine.PendingActivation != null || engine.IsAwaitingResponse) return false;
            // TriggerChainResponses batch (v163): seated on the engine's Chain — done once that Chain is over.
            if (chain.Stage == Stage.EngineChain)
                return !engine.Chain.HasLinks && !engine.Chain.IsResolving && !engine.IsResolvingChain;
            return !chain.WaitIdle || engine.PlayIdleForTriggerChain();
        }

        /// <summary>One step of <paramref name="chain"/>. True when the whole Chain has resolved.</summary>
        static bool Step(DuelEngine engine, SegocChain chain)
        {
            switch (chain.Stage)
            {
                case Stage.Ordering:
                    while (chain.Group < chain.Groups.Count)
                    {
                        var g = chain.Groups[chain.Group];
                        if (g.Count == 0)
                        {
                            chain.Group++;
                            continue;
                        }

                        if (chain.WaitIdle && g.Count >= 2 && g[0].Controller.IsPlayer &&
                            TryOpenOrderPick(engine, chain, g.ToList()))
                            return false;
                        Place(chain, g, g[0]);
                    }

                    chain.Stage = Stage.Activating;
                    chain.Next = 0;
                    return false;

                case Stage.Activating:
                    if (chain.Next < chain.Links.Count)
                    {
                        var e = chain.Links[chain.Next++];
                        if (e.Kind == EntryKind.Flip)
                            ActivateFlip(engine, chain, e);
                        else if (e.Kind == EntryKind.Fused)
                        {
                            // FlipMassAndOpponentPicks batch: Pixie Knight's opponent targets its Spell as it activates.
                            FlipMassAndOpponentPicksRuntime.ActivateSentToGyTargets(engine, e.Controller, e.Card);
                            // DeckSwarmsAndFusionMaterials batch: Centrifugal Field's owner targets the listed material.
                            DeckSwarmsAndFusionMaterialsRuntime.ActivateQueuedTrigger(engine, e.Run);
                        }
                        return false;
                    }

                    chain.Stage = Stage.Resolving;
                    chain.Next = chain.Links.Count - 1;
                    // TriggerChainResponses batch (v163): a player who can respond gets the Chain response window; the
                    // links then resolve on the engine's Chain.
                    if (TrySeatOnEngineChain(engine, chain))
                    {
                        chain.Stage = Stage.EngineChain;
                        return false;
                    }

                    if (chain.WaitIdle && chain.Links.Count > 1)
                        engine.Log("Chain: " + string.Join(", ", chain.Links.Select(x =>
                            $"CL{x.Link} {x.Card.Name} ({x.Controller.Name})")) + " — resolving from the last link.");
                    return false;

                case Stage.EngineChain:
                    return true;

                default:
                    if (chain.Next < 0) return true;
                    Resolve(engine, chain, chain.Links[chain.Next--]);
                    return false;
            }
        }

        static void Place(SegocChain chain, List<Entry> group, Entry e)
        {
            group.Remove(e);
            chain.Links.Add(e);
            e.Link = chain.Links.Count;
        }

        // ───────────────────────── player picks ─────────────────────────

        static bool TryOpenOrderPick(DuelEngine engine, SegocChain chain, List<Entry> candidates)
        {
            var cards = candidates.Select(e => e.Card).ToList();
            if (cards.Distinct().Count() != cards.Count) return false;
            // Only face-up monsters on the field can be offered; otherwise the default order stands.
            if (!cards.TrueForAll(c => c.FaceUp && engine.ControllerOf(c) != null && c.Def != null && c.Def.IsMonster))
                return false;

            var who = candidates[0].Controller;
            var p = new PendingActivation
            {
                Controller = who,
                Card = cards[0],
                TargetKind = EffectTargetKind.FaceUpMonsterOnField,
                IsMonsterEffect = true,
                TargetPicksRemaining = 1,
                PromptOverride = $"{cards.Count} of your effects activate at the same time — choose the one to put " +
                                 $"on the Chain next (Chain Link {chain.Links.Count + 1}; the last one resolves first). " +
                                 "Cancel keeps the default order (the first flipped resolves first)."
            };
            p.LegalTargets.AddRange(cards);
            s_picks.Add(p, new PickContext { Chain = chain, Candidates = candidates });
            chain.Pick = p;
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
            return true;
        }

        static void OpenTargetPick(DuelEngine engine, SegocChain chain, Entry e, EffectTargetKind kind,
            List<CardInstance> legal, int picks, bool upTo)
        {
            var p = new PendingActivation
            {
                Controller = e.Controller,
                Card = e.Card,
                FromHand = false,
                WasSetOnField = false,
                TargetKind = kind,
                IsMonsterEffect = true,
                UsesTextProgram = !e.Registered,
                TargetPicksRemaining = Math.Max(1, picks),
                TargetPicksAreUpTo = upTo
            };
            p.LegalTargets.AddRange(legal);
            s_picks.Add(p, new PickContext { Chain = chain, Entry = e });
            chain.Pick = p;
            engine.SetPendingActivation(p);
            engine.Log(p.Prompt);
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget: a pick this batch asked.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || target == null || !s_picks.TryGetValue(p, out var ctx)) return false;
            if (ctx.Candidates != null)
            {
                var chosen = ctx.Candidates.FirstOrDefault(x => ReferenceEquals(x.Card, target));
                if (chosen == null) return true;
                Close(engine, p, ctx);
                Place(ctx.Chain, ctx.Chain.Groups[ctx.Chain.Group], chosen);
                engine.Log($"{chosen.Controller.Name} puts {chosen.Card.Name}'s effect on the Chain as Chain Link " +
                           $"{chosen.Link}.");
            }
            else
            {
                var e = ctx.Entry;
                Record(engine, e, target);
                if (!p.ChosenTargets.Contains(target)) p.ChosenTargets.Add(target);
                p.LegalTargets.Remove(target);
                if (p.TargetPicksRemaining > 0) p.TargetPicksRemaining--;
                engine.Log($"{e.Card.Name} targets {target.Name}.");
                if (p.TargetPicksRemaining > 0 && p.LegalTargets.Count > 0)
                {
                    ok = true;
                    engine.Log($"{e.Card.Name}: choose another target ({p.TargetPicksRemaining} remaining).");
                    engine.NotifyPublic();
                    return true;
                }

                Close(engine, p, ctx);
            }

            ok = true;
            Drive(engine);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending: Cancel on a pick this batch asked.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var ctx)) return false;
            Close(engine, p, ctx);
            CompleteAutomatically(engine, ctx);
            Drive(engine);
            engine.NotifyPublic();
            return true;
        }

        static void Close(DuelEngine engine, PendingActivation p, PickContext ctx)
        {
            s_picks.Remove(p);
            ctx.Chain.Pick = null;
            if (ReferenceEquals(engine.PendingActivation, p))
                engine.ClearPendingActivation();
        }

        /// <summary>
        /// An unanswered pick: an order keeps the listed order; an optional FLIP effect with no target chosen is not
        /// activated; a mandatory one takes the automatic targets (those still missing for an exact count).
        /// </summary>
        static void CompleteAutomatically(DuelEngine engine, PickContext ctx)
        {
            if (ctx.Candidates != null)
            {
                var g = ctx.Chain.Groups[ctx.Chain.Group];
                foreach (var e in ctx.Candidates.Where(g.Contains).ToList())
                    Place(ctx.Chain, g, e);
                engine.Log("The remaining effects go on the Chain in the default order.");
                return;
            }

            var entry = ctx.Entry;
            var exact = entry.Targeted != null ? TargetedClassicsRuntime.ExactTargetCount(entry.Targeted) : 0;
            if (entry.Picks.Count > 0 && entry.Picks.Count >= exact) return; // "up to": the targets chosen so far
            if (entry.Picks.Count == 0 && entry.Optional)
            {
                entry.NoEffect = true;
                engine.Log($"{entry.Card.Name}: its effect is not activated.");
                return;
            }

            AutoTargets(engine, entry);
        }

        // ───────────────────────── FLIP activation and resolution ─────────────────────────

        static void ActivateFlip(DuelEngine engine, SegocChain chain, Entry e)
        {
            var who = e.Controller;
            var m = e.Card;
            engine.Log($"Chain Link {e.Link}: {m.Name}'s FLIP effect ({who.Name}).");
            // RuleLocks batch: Royal Command — the activation is negated; its cost is still paid.
            if (RuleLocksRuntime.NegatesFlipEffect(engine, m))
            {
                var negated = CompiledEffectCache.GetOrCompile(m)?.ClausesFor(EffectTiming.Flip);
                if (negated != null && negated.Count > 0)
                    FlipCostsAndFusionRuntime.TryPayFlipCost(engine, who, m, negated);
                e.NoEffect = true;
                return;
            }

            if (MonsterEffects.TryRegisteredFlipTargets(engine, who, m, out var kind, out var legal))
            {
                e.Registered = true;
                e.RegisteredKind = kind;
                if (legal == null || legal.Count == 0)
                {
                    engine.Log($"{m.Name} FLIP: no legal targets.");
                    e.NoEffect = true;
                    return;
                }

                if (!who.IsPlayer)
                {
                    Record(engine, e, MonsterEffects.RegisteredFlipAiPick(engine, who, kind, legal));
                    engine.Log($"{m.Name} targets {e.Picks[0].Card.Name}.");
                    return;
                }

                OpenTargetPick(engine, chain, e, kind, legal, 1, false);
                return;
            }

            var flip = CompiledEffectCache.GetOrCompile(m)?.ClausesFor(EffectTiming.Flip);
            if (flip == null || flip.Count == 0 ||
                TakeControlRuntime.FlipBlockedInBattlePhase(engine, m, flip) ||
                !FlipCostsAndFusionRuntime.TryPayFlipCost(engine, who, m, flip))
            {
                e.NoEffect = true;
                return;
            }

            foreach (var c in flip)
                if (c != null && TextEffectRuntime.NeedsTargetChoice(c))
                    c.RequiresTargetChoice = true;
            e.Clauses = flip;
            e.Targeted = flip.FirstOrDefault(c => c != null && c.RequiresTargetChoice);
            if (e.Targeted == null)
            {
                // DeckReturnsAndMassEffects batch (v161): Mysterious Guard chooses its ordered targets now, as it activates.
                DeckReturnsAndMassEffectsRuntime.ActivateFlipTargets(engine, who, m, flip);
                // FlipMassAndOpponentPicks batch: Dark Cat with White Tail chooses its two groups of targets now.
                FlipMassAndOpponentPicksRuntime.ActivateFlipTargets(engine, who, m, flip);
                return;
            }

            var targets = TextEffectRuntime.CollectTargets(engine, who, e.Targeted, m);
            var exact = TargetedClassicsRuntime.ExactTargetCount(e.Targeted);
            if (targets.Count == 0 || (exact > 0 && targets.Count < exact))
            {
                engine.Log($"{m.Name} FLIP: not enough legal targets — no effect.");
                e.NoEffect = true;
                return;
            }

            if (!who.IsPlayer)
            {
                AutoTargets(engine, e);
                return;
            }

            var picks = TextEffectRuntime.TargetPicks(e.Targeted, out var upTo);
            OpenTargetPick(engine, chain, e, TextEffectRuntime.MapTargetKind(e.Targeted), targets, picks, upTo);
        }

        /// <summary>The automatic targets of <paramref name="e"/> (the AI's choice), added to those already chosen.</summary>
        static void AutoTargets(DuelEngine engine, Entry e)
        {
            var who = e.Controller;
            if (e.Registered)
            {
                if (e.Picks.Count > 0) return;
                MonsterEffects.TryRegisteredFlipTargets(engine, who, e.Card, out var kind, out var legal);
                var pick = MonsterEffects.RegisteredFlipAiPick(engine, who, kind, legal);
                if (pick == null)
                {
                    e.NoEffect = true;
                    return;
                }

                Record(engine, e, pick);
                engine.Log($"{e.Card.Name} targets {pick.Name}.");
                return;
            }

            if (e.Targeted == null) return;
            var remaining = TextEffectRuntime.CollectTargets(engine, who, e.Targeted, e.Card)
                .Where(t => !e.Picks.Exists(s => ReferenceEquals(s.Card, t))).ToList();
            var max = TextEffectRuntime.TargetPicks(e.Targeted, out _);
            for (var i = e.Picks.Count; i < max && remaining.Count > 0; i++)
            {
                var pick = TextEffectRuntime.AutoPickChoiceTarget(engine, who, e.Targeted, remaining);
                if (pick == null) break;
                remaining.Remove(pick);
                Record(engine, e, pick);
                engine.Log($"{e.Card.Name} targets {pick.Name}.");
            }

            if (e.Picks.Count == 0) e.NoEffect = true;
        }

        static void Record(DuelEngine engine, Entry e, CardInstance card)
        {
            if (card == null) return;
            var snap = new Snap { Card = card, Where = Where.Other };
            if (engine.ControllerOf(card) != null)
            {
                snap.Where = Where.Field;
                snap.Stamp = card.FieldEntryStamp;
            }
            else
            {
                foreach (var d in new[] { engine.Player, engine.Opponent })
                {
                    if (d?.Graveyard == null || !d.Graveyard.Contains(card)) continue;
                    snap.Where = Where.Graveyard;
                    snap.Owner = d;
                    break;
                }
            }

            e.Picks.Add(snap);
        }

        static bool StillThere(DuelEngine engine, Snap s) => s.Where switch
        {
            Where.Field => engine.ControllerOf(s.Card) != null && s.Card.FieldEntryStamp == s.Stamp,
            Where.Graveyard => s.Owner != null && s.Owner.Graveyard.Contains(s.Card),
            _ => true
        };

        /// <summary>
        /// A FLIP clause that acts on "this card" itself: it needs the monster still face-up on the field when the
        /// effect resolves (the rest of the effect resolves anyway).
        /// </summary>
        internal static bool NeedsSourceFaceUp(EffectClause c)
        {
            switch (c.Action)
            {
                case EffectActionKind.TakeControlWhileSourceFaceUp:
                case EffectActionKind.SwitchControlWithThis:
                case EffectActionKind.RecordThisCardFlipped:
                case EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp:
                case EffectActionKind.DestroyThisCard:
                case EffectActionKind.PlaceThisOnTopOfDeck:
                case EffectActionKind.SetThisFaceDownDefense:
                case EffectActionKind.ChangeThisBattlePosition:
                case EffectActionKind.GainThisAtkUntilEnd:
                case EffectActionKind.GainSelfAtkPerCount:
                case EffectActionKind.EquipThisToTarget:
                case EffectActionKind.EquipTargetToThis:
                case EffectActionKind.LinkThisToTarget:
                case EffectActionKind.LinkThisToTargetSpellTrap:
                    return true;
                case EffectActionKind.ApplyLingeringAtkDef:
                    return !c.RequiresTargetChoice && !c.AppliesToEquippedHost;
                default:
                    return false;
            }
        }

        static void Resolve(DuelEngine engine, SegocChain chain, Entry e)
        {
            var st = StateOf(engine);
            st.ResolvingDepth++;
            try
            {
                ResolveEntry(engine, chain, e);
            }
            finally
            {
                st.ResolvingDepth--;
            }
        }

        static void ResolveEntry(DuelEngine engine, SegocChain chain, Entry e)
        {
            if (chain.WaitIdle)
            {
                // Each link is a new last thing to happen (Gust / Michizure answer only the last one).
                SpellTrapDestroyedResponseRuntime.DropPending(engine);
                BattleWindowsRuntime.DropSentRecords(engine);
            }

            if (e.Kind == EntryKind.Fused)
            {
                var st = StateOf(engine);
                var outer = st.MissingTiming;
                var outerReplay = st.Replaying;
                st.MissingTiming = e.MissesWhenTiming ? e.Card : null;
                st.Replaying = e.Card;
                try
                {
                    e.Run?.Invoke();
                }
                finally
                {
                    st.MissingTiming = outer;
                    st.Replaying = outerReplay;
                }

                return;
            }

            if (e.NoEffect) return;
            var state = StateOf(engine);
            var outerFlip = state.ResolvingFlip;
            state.ResolvingFlip = e.Card;
            try
            {
                ResolveFlip(engine, e);
            }
            finally
            {
                state.ResolvingFlip = outerFlip;
            }
        }

        /// <summary>
        /// TriggerChainResponses batch (v163), RuleLocksRuntime.SkipsFlipMonsterClause: <paramref name="monster"/>'s FLIP
        /// effect activated (Royal Command was not face-up then: it negates activations only, Lua EVENT_CHAIN_ACTIVATING)
        /// and is resolving now — a Royal Command chained to it does not stop it.
        /// </summary>
        public static bool ResolvesActivatedFlip(DuelEngine engine, CardInstance monster) =>
            engine != null && monster != null && s_state.TryGetValue(engine, out var st) &&
            ReferenceEquals(st.ResolvingFlip, monster);

        static void ResolveFlip(DuelEngine engine, Entry e)
        {
            var who = e.Controller;
            var m = e.Card;
            engine.Log($"Chain Link {e.Link}: {m.Name}'s FLIP effect resolves.");
            var sourceUp = m.FaceUp && engine.ControllerOf(m) != null;
            if (e.Registered)
            {
                var s = e.Picks.FirstOrDefault();
                if (s == null || !StillThere(engine, s))
                {
                    engine.Log($"{m.Name}: its target is no longer there — no effect.");
                    return;
                }

                MonsterEffects.ResolveRegisteredFlip(engine, who, m, e.RegisteredKind, s.Card);
                return;
            }

            var attackNegated = false;
            var battleEnded = false;
            if (e.Targeted == null)
            {
                foreach (var c in e.Clauses)
                {
                    if (c == null) continue;
                    if (!sourceUp && NeedsSourceFaceUp(c))
                    {
                        engine.Log($"{m.Name} is no longer face-up on the field — that part of its effect does not apply.");
                        continue;
                    }

                    TextEffectRuntime.ApplyClause(engine, who, m, c, null, ref attackNegated, ref battleEnded);
                }

                engine.NotifyPublic();
                return;
            }

            var live = e.Picks.Where(s => StillThere(engine, s)).Select(s => s.Card).ToList();
            var exact = TargetedClassicsRuntime.ExactTargetCount(e.Targeted);
            if (live.Count == 0 ||
                (exact > 0 && (live.Count != exact ||
                               !FlipCostsAndFusionRuntime.AllExactTargetsStillThere(engine, e.Targeted, live))))
            {
                engine.Log($"{m.Name}: not all of its targets are still there — no effect.");
                return;
            }

            for (var i = 0; i < live.Count; i++)
            {
                foreach (var c in e.Clauses)
                {
                    if (c == null || (!c.RequiresTargetChoice && i > 0)) continue;
                    if (!sourceUp && NeedsSourceFaceUp(c))
                    {
                        engine.Log($"{m.Name} is no longer face-up on the field — that part of its effect does not apply.");
                        continue;
                    }

                    TextEffectRuntime.ApplyClause(engine, who, m, c, c.RequiresTargetChoice ? live[i] : null,
                        ref attackNegated, ref battleEnded);
                }
            }

            TextEffectRuntime.NotifyTargetingEffectsResolved(engine, live, TargetingTruthRuntime.ClauseTargets(e.Targeted));
            engine.NotifyPublic();
        }
    }
}
