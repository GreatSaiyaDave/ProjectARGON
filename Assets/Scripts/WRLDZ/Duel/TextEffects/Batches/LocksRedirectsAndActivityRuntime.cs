using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LocksRedirectsAndActivity batch — three ENGINE_MAP §4 gap rows, each resolved as ONE helper that every
    /// site calls, so a later card with the same lock only flips a compiled flag instead of adding another
    /// scattered check:
    ///
    /// <item><b>Cannot-discard-as-a-cost lock</b> (per player, per effect; Goblin of Greed,
    /// ygopro EFFECT_CANNOT_DISCARD_AS_COST) — <see cref="DiscardAsCostLocked"/>. Every discard-cost site asks it.
    /// Sending from the hand to the GY is a send, not a discard, and is never locked.</item>
    ///
    /// <item><b>Player-wide activation lock + activity record</b> ("you cannot activate cards or effects";
    /// Chaos Emperor Dragon - Envoy of the End) — <see cref="ActivationLocked"/>, <see cref="NoteActivation"/>,
    /// <see cref="ActivityCostLegal"/>. The lock is checked at every activation site (S/T, monster
    /// ignition / trigger / quick, hand traps) by chaining into
    /// <see cref="ContinuousNegationRuntime.ActivationLocked"/>'s hub; the counter records the Lua ACTIVITY_CHAIN
    /// a cost can require.</item>
    ///
    /// <item><b>Leave-field redirect</b> ("if this card would leave the field, return it to its owner's hand
    /// instead"; Dark Magician of Chaos, ygopro EFFECT_LEAVE_FIELD_REDIRECT) — <see cref="RedirectFieldExit"/> /
    /// <see cref="CardIsRedirected"/>. One funnel every field exit goes through, unlike the GY-send hub
    /// (<c>SendsToBanishedInstead</c>), which only covers GY sends.</item>
    /// </summary>
    public static class LocksRedirectsAndActivityRuntime
    {
        // ───────────────────── Per-duel activity record ─────────────────────

        /// <summary>
        /// Per-player ACTIVITY_CHAIN record: how many cards and effects that player activated this turn, and the
        /// one activation in progress (the cost check of a card being activated excludes it).
        /// Reset at the end of each turn (the engine's turn switch).
        /// </summary>
        public sealed class ActivityRecord
        {
            public int PlayerActivations;
            public int OpponentActivations;
            /// <summary>The card currently being activated — excluded from its player's own count.</summary>
            public CardInstance InProgress;
        }

        static readonly ConditionalWeakTable<DuelEngine, ActivityRecord> s_activity =
            new ConditionalWeakTable<DuelEngine, ActivityRecord>();

        static ActivityRecord State(DuelEngine engine) => s_activity.GetOrCreateValue(engine);

        /// <summary>
        /// How many cards and effects <paramref name="who"/> has activated this turn, excluding the activation
        /// currently in progress (a cost that requires "no other activation this turn" still allows itself).
        /// </summary>
        public static int ActivationCount(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return 0;
            var s = State(engine);
            var n = who == engine.Player ? s.PlayerActivations : s.OpponentActivations;
            if (s.InProgress != null && ControllerOf(engine, s.InProgress) == who)
                return Math.Max(0, n - 1);
            return n;
        }

        /// <summary>
        /// Record that <paramref name="who"/> activated <paramref name="card"/>. Called once per activation as it
        /// begins (the cost check of that same activation reads the count through
        /// <see cref="ActivationCount"/>, which excludes <paramref name="card"/>).
        /// </summary>
        public static void NoteActivation(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null) return;
            var s = State(engine);
            if (who == engine.Player) s.PlayerActivations++;
            else s.OpponentActivations++;
            s.InProgress = card;
        }

        /// <summary>
        /// The activation in progress has resolved or been refused; the record keeps the count, drops the exclusion.
        /// </summary>
        public static void EndActivation(DuelEngine engine)
        {
            if (engine == null) return;
            State(engine).InProgress = null;
        }

        /// <summary>New turn: the ACTIVITY_CHAIN counters restart.</summary>
        public static void ResetTurn(DuelEngine engine) => State(engine).PlayerActivations = State(engine).OpponentActivations = 0;

        /// <summary>
        /// Cost legality for a clause that requires "you have not activated any other cards or effects this turn"
        /// (Lua ACTIVITY_CHAIN check). True when <paramref name="card"/> itself is the only activation this turn.
        /// </summary>
        public static bool ActivityCostLegal(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null) return true;
            if (ActivationCount(engine, who) > 1) return false;
            // The only recorded activation is this card's own cost check (NoteActivation ran first).
            var s = State(engine);
            if (s.InProgress != card && ActivationCount(engine, who) > 0) return false;
            return true;
        }

        static DuelistState ControllerOf(DuelEngine engine, CardInstance c)
        {
            if (engine?.Player != null && engine.Player.TryFindMonster(c, out _)) return engine.Player;
            if (engine?.Player != null && engine.Player.TryFindSpellTrap(c, out _)) return engine.Player;
            if (engine?.Opponent != null && engine.Opponent.TryFindMonster(c, out _)) return engine.Opponent;
            if (engine?.Opponent != null && engine.Opponent.TryFindSpellTrap(c, out _)) return engine.Opponent;
            return null;
        }

        // ───────────────────── Cannot discard as a cost ─────────────────────

        /// <summary>
        /// True when <paramref name="who"/> may not discard cards as a cost because a live
        /// <see cref="EffectActionKind.ContinuousCannotDiscardAsCost"/> source covers them
        /// (ygopro EFFECT_CANNOT_DISCARD_AS_COST, per player, per effect).
        /// Sending from the hand to the GY is a send, not a discard, and never trips this.
        /// </summary>
        public static bool DiscardAsCostLocked(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null) return false;
            foreach (var src in LockSources(engine))
                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousCannotDiscardAsCost))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (CoversSide(c.Side, engine, src, who))
                    {
                        engine.Log($"{who.Name} cannot discard cards as a cost ({src.Name}).");
                        return true;
                    }
                }
            return false;
        }

        /// <summary>
        /// A discard cost <paramref name="c"/> on <paramref name="card"/> is payable: enough cards are eligible
        /// AND no live cannot-discard-as-a-cost lock covers <paramref name="who"/>. The single check every
        /// discard-cost site calls.
        /// </summary>
        public static bool DiscardCostPayable(DuelEngine engine, DuelistState who, CardInstance card,
            EffectClause c)
        {
            if (c == null) return true;
            // Sends are never discards (DcrEquipSpells: "send 1 card from your hand to the GY").
            if (c.DiscardCostIsSend) return true;
            if (!c.RequiresDiscardCost && !c.RequiresDiscardSelf) return true;
            if (DiscardAsCostLocked(engine, who)) return false;
            return true;
        }

        // ───────────────────── Player-wide activation lock ─────────────────────

        /// <summary>
        /// True when a live <see cref="EffectActionKind.ContinuousThisPlayerCannotActivate"/> or
        /// <see cref="EffectActionKind.ContinuousNeitherPlayerCanActivate"/> source forbids
        /// <paramref name="who"/> from activating cards or effects at all — the player-wide half of the
        /// activation-lock row (Chaos Emperor Dragon - Envoy of the End).
        /// Checked at every activation site; GY effects of another card are untouched (a lock bars
        /// activating, not applying).
        /// </summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null) return false;
            foreach (var src in LockSources(engine))
            {
                if (src == card) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousThisPlayerCannotActivate))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (CoversSide(c.Side, engine, src, who))
                    {
                        engine.Log($"{who.Name} cannot activate cards or effects ({src.Name}).");
                        return true;
                    }
                }
                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousNeitherPlayerCanActivate))
                {
                    if (!ConditionMet(engine, c)) continue;
                    // "Neither player can activate cards or effects" — every activation site, both sides.
                    engine.Log($"{who.Name} cannot activate cards or effects ({src.Name}).");
                    return true;
                }
            }
            return false;
        }

        // ───────────────────── Leave-field redirect ─────────────────────

        /// <summary>
        /// The single funnel every field exit goes through (ygopro EFFECT_LEAVE_FIELD_REDIRECT): when
        /// <paramref name="card"/> would leave the field, return it to its owner's hand instead of going to the
        /// GY / being banished / being shuffled into the Deck. Unlike the GY-send hub
        /// (<c>RedirectAndReplaceRuntime.SendsToBanishedInstead</c>) it covers every destination.
        /// Returns true when the exit was redirected (the caller must stop its own send).
        /// </summary>
        public static bool RedirectFieldExit(DuelEngine engine, CardInstance card, string viaReason)
        {
            if (engine == null || card == null || !CardIsRedirected(engine, card)) return false;
            var owner = OwnerOf(engine, card) ?? ControllerOf(engine, card);
            if (owner == null) return false;
            engine.Log($"{card.Name} returns to its owner's hand instead of leaving the field ({viaReason ?? "effect"}).");
            // A card that never left keeps its field presence: no "sent to the GY" trigger fires.
            if (owner.TryFindSpellTrap(card, out var si))
                owner.SpellTrapZones[si].Occupant = null;
            if (owner.TryFindMonster(card, out var mi))
                owner.MonsterZones[mi].Occupant = null;
            if (owner.FieldSpellZone != null && owner.FieldSpellZone.Occupant == card)
                owner.FieldSpellZone.Occupant = null;
            owner.Hand.Add(card);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// True when <paramref name="card"/> carries a live leave-field-redirect clause.
        /// </summary>
        public static bool CardIsRedirected(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null) return false;
            if (!card.FaceUp || card.IsNegated) return false;
            foreach (var c in ClausesOn(card, EffectActionKind.LeaveFieldReturnsToOwnerHandInstead))
                if (ConditionMet(engine, c))
                    return true;
            return false;
        }

        /// <summary>The owner of <paramref name="card"/> (its ControlOwner), or its controller.</summary>
        public static DuelistState OwnerOf(DuelEngine engine, CardInstance card)
        {
            if (card?.ControlOwner != null) return card.ControlOwner;
            return ControllerOf(engine, card);
        }

        // ───────────────────── Shared source scan ─────────────────────

        /// <summary>
        /// Every face-up, non-negated monster or Spell/Trap on either field that can carry a lock clause. A monster
        /// whose Summon is still in its summon-negation window is not on the field yet.
        /// </summary>
        static IEnumerable<CardInstance> LockSources(DuelEngine engine)
        {
            if (engine == null) yield break;
            foreach (var p in new[] { engine.Player, engine.Opponent })
            {
                if (p == null) continue;
                foreach (var m in p.MonstersOnField())
                    if (m != null && m.FaceUp && !m.IsNegated && !engine.IsSummonPending(m))
                        yield return m;
                foreach (var st in p.SpellTrapsOnField())
                    if (st != null && st.FaceUp && !st.IsNegated)
                        yield return st;
            }
        }

        /// <summary>
        /// Does a clause on <paramref name="src"/> (controlled by one duelist) apply to
        /// <paramref name="target"/>? Side Controller = the source's controller only, Opponent = the other
        /// duelist, Both / Either = either. Blank Side is treated as Controller.
        /// </summary>
        static bool CoversSide(EffectSide side, DuelEngine engine, CardInstance src, DuelistState target)
        {
            var controller = ControllerOf(engine, src);
            switch (side)
            {
                case EffectSide.Opponent:
                    return target != controller;
                case EffectSide.Both:
                case EffectSide.Either:
                    return true;
                case EffectSide.Controller:
                default:
                    return target == controller;
            }
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) yield break;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == action &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
            }
        }

        static bool ConditionMet(DuelEngine engine, EffectClause c) =>
            ContinuousProtections.ConditionMet(engine, c);
    }
}
