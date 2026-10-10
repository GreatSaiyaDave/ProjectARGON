using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TriggerChainResponses batch (v163): trigger effects as real Chain Links that players can respond to.
    /// <para>
    /// An activated trigger effect (FLIP, "sent to the GY", a Standby / Draw / End Phase trigger) is seated on
    /// <see cref="DuelEngine.Chain"/> as a Spell Speed 1 <see cref="EffectClass.Trigger"/> link
    /// (<see cref="ChainLink.EffectKey"/> = <see cref="TriggerLinkKey"/>) once its costs are paid and its targets chosen
    /// (by the caller, as before), and the opponent of the last link's controller gets the ordinary Chain response window
    /// (<see cref="DuelEngine.OpenChainResponseWindow"/>: Set Quick-Play Spells and Traps, face-up Quick Effects). Each
    /// link resolves in Chain order through <see cref="TryResolveLink"/> (DuelEngine.ResolveChainStack, before the
    /// negation check): a negated link does nothing (its card stays where it is), otherwise it runs the resolution its
    /// caller recorded. A link whose resolution opens a player's pick holds the Chain until the pick is answered
    /// (<see cref="Flush"/>, from DuelEngine.Notify).
    /// </para>
    /// <para>
    /// The window opens only when the responding player has a legal response (<see cref="TrySeat"/> probes it with the
    /// links in place, then takes them back): otherwise the caller resolves the trigger exactly as before, so play with
    /// nothing to chain is unchanged. Seating needs open play (<see cref="CanSeatNow"/>): no Chain, response window,
    /// pick, declared attack or Damage Step. Triggers that happen elsewhere keep resolving where they happen.
    /// </para>
    /// Callers: SimultaneousTriggersRuntime (SEGOC Chains: FLIP effects of monsters flipped by an effect, queued triggers,
    /// Flip Summons, "sent to the GY" triggers deferred past a resolving Chain), NegationMonstersRuntime (Freed's Draw
    /// Phase search), Life Absorbing Machine's Standby trigger and Spirit's Invitation's return trigger
    /// (ContinuousTrapsAndSpiritSupportRuntime).
    /// </summary>
    public static class TriggerChainResponsesRuntime
    {
        /// <summary><see cref="ChainLink.EffectKey"/> of a trigger effect's link.</summary>
        public const string TriggerLinkKey = "trigger-effect";

        /// <summary>One trigger effect to seat as a Chain Link.</summary>
        public sealed class Seat
        {
            public DuelistState Controller;
            public CardInstance Card;
            public CardLocation From;
            /// <summary>The targets chosen as it activated (shown to responders: Chain threats, redirects).</summary>
            public readonly List<CardInstance> Targets = new();
            /// <summary>What the link does when it resolves (not negated).</summary>
            public Action Resolve;
        }

        sealed class State
        {
            public bool HeldForChoice;
            public readonly List<Action> AfterChain = new();
            public bool SeatedThisChain;
            public int CollectDepth;
            public readonly List<(Seat seat, Action inline)> Collected = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();
        static readonly ConditionalWeakTable<ChainLink, Seat> s_links = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        public static bool IsTriggerLink(ChainLink link) => link != null && link.EffectKey == TriggerLinkKey;

        /// <summary>Play is open: a new Chain may start here (nothing resolving, pending, attacking or in the Damage Step).</summary>
        public static bool CanSeatNow(DuelEngine engine) =>
            engine != null && !engine.GameOver && engine.PlayIdleForTriggerChain() && !engine.Chain.HasLinks &&
            !engine.IsResolvingChain && engine.PendingResponse == null && engine.PendingActivation == null;

        /// <summary>
        /// Cheap structural gate (a superset of the legal responses): the opponent of <paramref name="controller"/>
        /// has a Set Trap / Quick-Play Spell it could activate, a face-up card with a Quick Effect, or a Quick-Play Spell
        /// in the hand during its own turn. False means no response window can open, so the caller keeps today's path.
        /// </summary>
        public static bool OpponentMayRespond(DuelEngine engine, DuelistState controller)
        {
            var opp = controller != null ? engine?.OpponentOf(controller) : null;
            if (opp == null) return false;
            foreach (var st in opp.SpellTrapsOnField())
            {
                if (st?.Def == null) continue;
                if (!st.FaceUp)
                {
                    if (!st.SetThisTurn && (st.Def.IsTrap || SpellTrapEffects.IsQuickPlay(st.Def))) return true;
                    continue;
                }

                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (FaceUpQuickEffectsRuntime.IsFaceUpQuickCard(opp, st, prog)) return true;
            }

            foreach (var m in opp.MonstersOnField())
            {
                if (m?.Def == null || !m.FaceUp) continue;
                var prog = CompiledEffectCache.GetOrCompile(m.Def);
                if (FaceUpQuickEffectsRuntime.IsFaceUpQuickCard(opp, m, prog) ||
                    BattleBoostsRuntime.IsMonsterQuickCard(opp, m, prog))
                    return true;
            }

            return engine.TurnPlayer == opp && opp.Hand.Exists(h => h?.Def != null && SpellTrapEffects.IsQuickPlay(h.Def));
        }

        /// <summary>Where <paramref name="card"/> is now, for the link's <see cref="ChainLink.FromLocation"/>.</summary>
        public static CardLocation LocationOf(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return CardLocation.Unknown;
            foreach (var d in new[] { engine.Player, engine.Opponent })
            {
                if (d == null) continue;
                if (d.TryFindMonster(card, out _)) return CardLocation.MonsterZone;
                if (d.TryFindSpellTrap(card, out _)) return CardLocation.SpellTrapZone;
                if (ReferenceEquals(d.FieldSpellZone?.Occupant, card)) return CardLocation.FieldZone;
                if (d.Graveyard != null && d.Graveyard.Contains(card)) return CardLocation.Graveyard;
                if (d.Banished != null && d.Banished.Contains(card)) return CardLocation.Banished;
                if (d.Hand != null && d.Hand.Contains(card)) return CardLocation.Hand;
            }

            return CardLocation.Unknown;
        }

        /// <summary>
        /// Seats <paramref name="seats"/> (Chain Link 1 first) on the Chain and opens the response window of the opponent
        /// of the last link's controller. Returns false — with nothing changed — when play is not open or that player has
        /// no legal response: the caller then resolves the triggers as before. When true, every link resolves through
        /// <see cref="TryResolveLink"/> (possibly before this call returns, when an AI answers at once) and
        /// <paramref name="afterChain"/> runs once the whole Chain has resolved.
        /// </summary>
        public static bool TrySeat(DuelEngine engine, IReadOnlyList<Seat> seats, Action afterChain = null)
        {
            if (engine == null || seats == null || seats.Count == 0 || !CanSeatNow(engine)) return false;
            var last = seats[seats.Count - 1];
            if (last?.Controller == null || !OpponentMayRespond(engine, last.Controller)) return false;
            var responder = engine.OpponentOf(last.Controller);
            var added = new List<ChainLink>();
            foreach (var s in seats)
            {
                if (s?.Controller == null || s.Card == null || s.Resolve == null)
                {
                    Undo(engine, added);
                    return false;
                }

                var link = engine.Chain.AddLink(s.Controller, s.Card, SpellSpeed.Speed1, EffectClass.Trigger,
                    s.From, fromHand: false, wasSet: false, effectKey: TriggerLinkKey,
                    target: s.Targets.FirstOrDefault());
                if (link == null)
                {
                    Undo(engine, added);
                    return false;
                }

                for (var i = 1; i < s.Targets.Count; i++)
                    if (s.Targets[i] != null) link.Targets.Add(s.Targets[i]);
                s_links.Remove(link);
                s_links.Add(link, s);
                added.Add(link);
            }

            // The response window opens only when the responder has something legal to chain to the last link.
            engine.SetPendingResponse(new PendingResponse
            {
                Timing = ResponseTiming.ChainResponse,
                Responder = responder,
                ChainTarget = engine.Chain.Last
            });
            var legal = SpellTrapEffects.CollectLegalResponseCards(engine, responder, ResponseTiming.ChainResponse, null);
            engine.ClearPendingResponse();
            if (legal.Count == 0)
            {
                Undo(engine, added);
                return false;
            }

            var st = StateOf(engine);
            st.SeatedThisChain = true;
            if (afterChain != null) st.AfterChain.Add(afterChain);
            engine.Log("Chain: " + string.Join(", ", added.Select(l =>
                $"CL{l.LinkNumber} {l.Card.Name}'s effect ({l.Controller.Name})")) +
                       $" — {responder.Name} may respond.");
            if (!engine.OpenChainResponseWindow(responder))
            {
                // Nothing to answer after all: the Chain resolves now.
                engine.Chain.StartResolution();
                engine.ResolveChainStackForActivation();
            }

            return true;
        }

        /// <summary>
        /// Opens a pass in which triggers that happen at the same time are collected (a Standby / End Phase's triggers,
        /// several Spirit's Invitations on one return): <see cref="TrySeatOrCollect"/> holds each one, and the outermost
        /// <see cref="EndCollect"/> seats all of them on one Chain (Chain Link 1 … n), so no trigger resolves, or opens a
        /// pick, while the response window of an earlier one is open. Calls nest; pair each with <see cref="EndCollect"/>.
        /// </summary>
        public static void BeginCollect(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).CollectDepth++;
        }

        /// <summary>
        /// Closes a <see cref="BeginCollect"/> pass. The outermost one seats every collected trigger on one Chain (the turn
        /// player's first, then the other player's, each in the order they triggered); when no response window can open
        /// then, each runs its inline resolution in that order instead.
        /// </summary>
        public static void EndCollect(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.CollectDepth == 0) return;
            if (--st.CollectDepth > 0 || st.Collected.Count == 0) return;
            var items = st.Collected.ToList();
            st.Collected.Clear();
            if (engine.GameOver) return;
            var tp = engine.TurnPlayer;
            items = items.Where(i => i.seat.Controller == tp).Concat(items.Where(i => i.seat.Controller != tp)).ToList();
            if (TrySeat(engine, items.Select(i => i.seat).ToList())) return;
            foreach (var (_, inline) in items)
            {
                if (engine.GameOver) return;
                inline?.Invoke();
            }
        }

        /// <summary>
        /// One trigger: inside a <see cref="BeginCollect"/> pass where a Chain could be seated now, it is held for the
        /// pass's Chain (and <paramref name="inlineLater"/> runs at <see cref="EndCollect"/> if that Chain cannot open);
        /// outside one it is seated at once. False — nothing held or seated — means: resolve it now, as before.
        /// </summary>
        public static bool TrySeatOrCollect(DuelEngine engine, Seat seat, Action inlineLater)
        {
            if (engine == null || seat?.Controller == null || seat.Card == null || seat.Resolve == null) return false;
            var st = StateOf(engine);
            if (st.CollectDepth > 0)
            {
                if (!CanSeatNow(engine) || !OpponentMayRespond(engine, seat.Controller)) return false;
                st.Collected.Add((seat, inlineLater ?? seat.Resolve));
                return true;
            }

            return TrySeat(engine, new[] { seat });
        }

        static void Undo(DuelEngine engine, List<ChainLink> added)
        {
            for (var i = added.Count - 1; i >= 0; i--)
            {
                engine.Chain.RemoveUnresolved(added[i]);
                s_links.Remove(added[i]);
            }
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack, before the negation check: a trigger link applies the resolution recorded when
        /// it was seated; negated, it does nothing (and its card is not sent anywhere). False for any other link.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsTriggerLink(link)) return false;
            var name = link.Card?.Name ?? "Effect";
            if (!s_links.TryGetValue(link, out var seat))
            {
                engine.Log($"Chain Link {link.LinkNumber}: {name}'s effect has nothing to resolve.");
                return true;
            }

            s_links.Remove(link);
            if (link.Negated)
            {
                engine.Log($"Chain Link {link.LinkNumber}: {name}'s effect is negated.");
                return true;
            }

            // A Spell/Trap effect that triggered in the Spell & Trap Zone is negated as it resolves while a face-up
            // negator covers that card (Royal Decree, Lua EVENT_CHAIN_SOLVING + IsTrapEffect; Imperial Order for Spells).
            if (seat.From == CardLocation.SpellTrapZone && link.Card?.Def != null && !link.Card.Def.IsMonster &&
                ContinuousNegationRuntime.NegatedBy(engine, link.Card) is { } negator)
            {
                engine.Log($"Chain Link {link.LinkNumber}: {name}'s effect is negated by {negator.Name}.");
                return true;
            }

            seat.Resolve();
            return true;
        }

        /// <summary>
        /// SpellTrapEffects.ChainThreatensCard (the AI's "use it or lose it" answer): <paramref name="link"/> targets
        /// <paramref name="card"/> with an effect that would destroy, return or banish a card of its kind (a targeted
        /// clause's DestroyIfType: Armed Ninja destroys only a Spell). A registered script with no such clause counts as a
        /// threat to what it targets.
        /// </summary>
        public static bool LinkThreatensCard(ChainLink link, CardInstance card)
        {
            if (link == null || card?.Def == null || (link.Target != card && !link.Targets.Contains(card))) return false;
            var prog = CompiledEffectCache.GetOrCompile(link.Card?.Def);
            var targeted = prog?.ClauseList?.Where(c => c != null && (c.RequiresTargetChoice || c.IsPsctTarget)).ToList();
            if (targeted == null || targeted.Count == 0) return true;
            foreach (var c in targeted)
            {
                if (c.Action != EffectActionKind.Destroy && c.Action != EffectActionKind.ReturnToHand &&
                    c.Action != EffectActionKind.Banish)
                    continue;
                if (c.Action == EffectActionKind.Destroy && !string.IsNullOrEmpty(c.DestroyIfType) &&
                    !(c.DestroyIfType == "Spell" ? card.Def.IsSpell : c.DestroyIfType == "Trap" ? card.Def.IsTrap : true))
                    continue;
                return true;
            }

            return false;
        }

        /// <summary>DuelEngine.ResolveChainStack: a trigger link's resolution opened a pick; the Chain waits for it.</summary>
        public static void NoteHeldForChoice(DuelEngine engine)
        {
            if (engine != null) StateOf(engine).HeldForChoice = true;
        }

        /// <summary>DuelEngine.Notify: the pick a trigger link opened is answered — the rest of the Chain resolves.</summary>
        public static void Flush(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || !st.HeldForChoice) return;
            if (engine.GameOver)
            {
                st.HeldForChoice = false;
                return;
            }

            if (engine.PendingActivation != null || engine.PendingResponse != null) return;
            st.HeldForChoice = false;
            engine.ResumeChainAfterChoice();
        }

        /// <summary>
        /// True while a trigger Chain seated by this runtime is still open (a response window, a pick or its resolution):
        /// a phase that seated one waits for it (<see cref="RunAfterChain"/>).
        /// </summary>
        public static bool ChainOpen(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.SeatedThisChain &&
            (engine.Chain.HasLinks || engine.IsResolvingChain || engine.PendingResponse != null);

        /// <summary>Runs <paramref name="next"/> once the open trigger Chain has resolved (at once when none is open).</summary>
        public static void RunAfterChain(DuelEngine engine, Action next)
        {
            if (engine == null || next == null) return;
            if (!ChainOpen(engine))
            {
                next();
                return;
            }

            StateOf(engine).AfterChain.Add(next);
        }

        /// <summary>
        /// DuelEngine.ResolveChainStack, once the whole Chain has resolved: the phase or Summon that seated a trigger Chain
        /// goes on (Freed's Draw Phase, a Standby / End Phase, a Flip Summon's summon window).
        /// </summary>
        public static void AfterChainResolved(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || !st.SeatedThisChain) return;
            st.SeatedThisChain = false;
            st.HeldForChoice = false;
            var next = st.AfterChain.ToList();
            st.AfterChain.Clear();
            foreach (var a in next)
            {
                if (engine.GameOver) return;
                a();
            }

            if (!engine.GameOver)
                engine.FlushQueuedSummonResponses();
        }
    }
}
