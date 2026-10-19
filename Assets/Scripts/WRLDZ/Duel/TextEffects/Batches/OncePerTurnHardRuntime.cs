using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// OncePerTurnHard batch: enforcement of <see cref="OncePerTurnScope.PerCardName"/> ("You can only use this effect of
    /// "X" once per turn", ygopro <c>SetCountLimit(1, id)</c>). The count is per player and per card name (every copy,
    /// on any zone, shares it) and per effect (the clause's SourceSnippet), for the game turn it was used in. It is used
    /// when the effect activates:
    /// <list type="bullet">
    /// <item>a monster ignition (Activate): SpellTrapEffects records it once TextEffectRuntime.TryResolveActivation
    /// accepted the activation (<see cref="NoteActivated"/>); a cancelled pick that undid the activation gives it back
    /// (<see cref="NoteCancelled"/>); TextEffectRuntime.CanActivate refuses a spent one (<see cref="BlocksActivation"/>);</item>
    /// <item>a "sent from the field to the GY" trigger: queued on a Chain (SimultaneousTriggersRuntime, mandatory ones) or
    /// resolved (TextEffectRuntime.TryResolveSentToGy); TextEffectRuntime.SentToGyCanActivate gives a spent one no Chain
    /// Link (the copy that used it keeps its own link for that trip to the GY);</item>
    /// <item>an "If this card is Summoned" trigger: TextEffectRuntime.TryResolveThisCardSummoned.</item>
    /// </list>
    /// </summary>
    public static class OncePerTurnHardRuntime
    {
        sealed class Use
        {
            public int Turn;
            /// <summary>The player who used it. A new duel (StartDuel on the same engine) has new DuelistStates, so an
            /// old record never counts against it, even on the same turn number.</summary>
            public DuelistState Owner;
            public CardInstance Consumer;
            /// <summary>The consumer's GY trip (CardInstance.GyEntryStamp) for a "sent to the GY" trigger; -1 otherwise.</summary>
            public int Trip = -1;
            public bool Ignition;
        }

        sealed class State
        {
            public readonly Dictionary<string, Use> Used = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A hard once per turn by card name.</summary>
        public static bool IsHardNameOpt(EffectClause c) =>
            c != null && c.OncePerTurn && c.OptScope == OncePerTurnScope.PerCardName;

        static string Key(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (engine == null || who == null || card?.Def == null || c == null) return null;
            string side = ReferenceEquals(who, engine.Player) ? "P" : ReferenceEquals(who, engine.Opponent) ? "O" : null;
            if (side == null || string.IsNullOrEmpty(card.Def.name)) return null;
            return side + "|" + card.Def.name + "|" + (c.SourceSnippet ?? "");
        }

        /// <summary>
        /// The hard once per turn of <paramref name="c"/> was already used this turn by <paramref name="who"/> (any copy).
        /// <paramref name="trip"/> &gt;= 0: the copy that used it on that same trip to the GY is still allowed (its own
        /// queued trigger resolving).
        /// </summary>
        public static bool Spent(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c, int trip = -1)
        {
            if (!IsHardNameOpt(c)) return false;
            var key = Key(engine, who, card, c);
            if (key == null) return false;
            if (!StateOf(engine).Used.TryGetValue(key, out var u) || u.Turn != engine.TurnNumber ||
                !ReferenceEquals(u.Owner, who)) return false;
            if (trip >= 0 && ReferenceEquals(u.Consumer, card) && u.Trip == trip) return false;
            return true;
        }

        /// <summary>Record that <paramref name="who"/> used the hard once per turn of <paramref name="c"/> now.</summary>
        public static void Mark(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c, int trip = -1,
            bool ignition = false)
        {
            if (!IsHardNameOpt(c)) return;
            var key = Key(engine, who, card, c);
            if (key == null) return;
            var st = StateOf(engine);
            if (st.Used.TryGetValue(key, out var u) && u.Turn == engine.TurnNumber && ReferenceEquals(u.Owner, who))
                return; // the first use keeps the record
            st.Used[key] = new Use
                { Turn = engine.TurnNumber, Owner = who, Consumer = card, Trip = trip, Ignition = ignition };
            engine.Log($"{card.Name}: its effect is used for this turn (once per turn, by name).");
        }

        // ───────────────────────────── ignitions ─────────────────────────────

        /// <summary>TextEffectRuntime.CanActivate: a hard once-per-turn Activate clause of this name was used this turn.</summary>
        public static bool BlocksActivation(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> activate)
        {
            if (activate == null) return false;
            foreach (var c in activate)
                if (Spent(engine, who, card, c))
                    return true;
            return false;
        }

        /// <summary>
        /// SpellTrapEffects: TextEffectRuntime.TryResolveActivation accepted <paramref name="card"/>'s Activate program (it
        /// resolved, or its cost / target pick is open): its hard once per turn is used.
        /// </summary>
        public static void NoteActivated(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (engine == null || prog == null || card == null) return;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                Mark(engine, who, card, c, -1, ignition: true);
        }

        /// <summary>
        /// DuelEngine.CancelEffectTargeting: the pick of <paramref name="card"/>'s activation was cancelled. When that undid the
        /// activation (no pick of it is open any more and it did not resolve), its once per turn is given back.
        /// </summary>
        public static void NoteCancelled(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return;
            if (ReferenceEquals(engine.PendingActivation?.Card, card) || card.EffectUsedThisTurn) return;
            var st = StateOf(engine);
            var drop = new List<string>();
            foreach (var kv in st.Used)
                if (kv.Value.Ignition && ReferenceEquals(kv.Value.Consumer, card) && kv.Value.Turn == engine.TurnNumber)
                    drop.Add(kv.Key);
            foreach (var k in drop)
                st.Used.Remove(k);
        }

        // ───────────────────────────── "sent from the field to the GY" ─────────────────────────────

        static DuelistState GyHolder(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null) return null;
            if (engine.Player?.Graveyard != null && engine.Player.Graveyard.Contains(card)) return engine.Player;
            if (engine.Opponent?.Graveyard != null && engine.Opponent.Graveyard.Contains(card)) return engine.Opponent;
            return null;
        }

        /// <summary>
        /// The "sent from the field to the GY" clause <paramref name="c"/> of <paramref name="card"/> cannot activate: its
        /// hard once per turn was used this turn by another copy (or by this one on an earlier trip to the GY).
        /// </summary>
        public static bool SentToGySpent(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c) =>
            IsHardNameOpt(c) && Spent(engine, who ?? GyHolder(engine, card), card, c, card?.GyEntryStamp ?? -1);

        /// <summary>The "sent from the field to the GY" clause <paramref name="c"/> activates now.</summary>
        public static void MarkSentToGy(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            if (!IsHardNameOpt(c)) return;
            Mark(engine, who ?? GyHolder(engine, card), card, c, card?.GyEntryStamp ?? -1);
        }

        /// <summary>
        /// SimultaneousTriggersRuntime: <paramref name="card"/>'s "sent from the field to the GY" trigger got a Chain Link (a
        /// mandatory one activates with it), so a second copy sent by the same event gets none.
        /// </summary>
        public static void NoteSentToGyQueued(DuelEngine engine, DuelistState owner, CardInstance card,
            CompiledCardProgram prog)
        {
            if (engine == null || card == null || prog == null || !prog.FullyCompiled) return;
            foreach (var c in prog.ClausesFor(EffectTiming.SentFromFieldToGy))
                if (c != null && !c.IsOptional)
                    MarkSentToGy(engine, owner, card, c);
        }
    }
}
