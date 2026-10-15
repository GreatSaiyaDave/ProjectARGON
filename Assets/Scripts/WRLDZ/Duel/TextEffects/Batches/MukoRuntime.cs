using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Muko (c80863132): a chain answer that REPLACES the operation of the link it answers.
    /// <para>
    /// The Lua answers <c>EVENT_CHAINING</c> whose link has <c>EFFECT_FLAG_PLAYER_TARGET</c> and exactly one operation
    /// of <c>CATEGORY_DRAW</c> with a count above 0, then calls <c>Duel.ChangeChainOperation</c> so that link discards
    /// the top of its drawer instead of drawing them. The engine has no generic "replace an operation", so the shape
    /// here is the same one ChainLinkAnswersRuntime uses for Jar Robber's negation: record the answered link, and when
    /// that link comes up to resolve, claim it and do something else instead. The one difference is what "instead"
    /// means — a mill of the drawn count rather than a no-op.
    /// </para>
    /// </summary>
    public static class MukoRuntime
    {
        sealed class State
        {
            /// <summary>Links whose draw operation Muko replaced with a discard (see the class doc).</summary>
            public readonly HashSet<ChainLink> Replaced = new();
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        // ───────────────────────────── answering ─────────────────────────────

        /// <summary>The <see cref="EffectActionKind.Draw"/> clause of the link being answered, with its count.</summary>
        static EffectClause DrawClauseOf(ChainLink link)
        {
            if (link?.Card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(link.Card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            // "an effect of drawing card(s)" and one operation only: the answered card must be exactly a draw.
            if (prog.ClauseList.Count(c => c != null && c.Timing != EffectTiming.ContinuousWhileFaceUp) != 1)
                return null;
            return prog.ClauseList.Find(c => c != null &&
                c.Action == EffectActionKind.Draw && System.Math.Max(1, c.Amount) > 0);
        }

        /// <summary>
        /// TextEffectRuntime.ActivationConditionsMet: the link being answered is a draw of N cards by a card effect
        /// (Lua s.condition). The count is kept on the answered link by <see cref="NoteReplaced"/>.
        /// </summary>
        public static bool CanAnswerDrawLink(ChainLink link, out int count)
        {
            count = 0;
            var draw = DrawClauseOf(link);
            if (draw == null) return false;
            count = System.Math.Max(1, draw.Amount);
            return true;
        }

        /// <summary>
        /// TextEffectRuntime.CanActivateChainResponse / DuelEngine.TryActivateChainResponse: Muko answers only a
        /// link whose whole operation is a draw (Lua s.condition), so it is never even offered as a legal
        /// response against anything else. Returns whether this batch decided the answer.
        /// </summary>
        public static bool TryCanAnswer(ChainLink answered, out bool ok, out string reason)
        {
            ok = CanAnswerDrawLink(answered, out var count);
            reason = ok
                ? "OK"
                : $"{answered?.Card?.Name ?? "that effect"} does not just draw cards, so Muko cannot answer it.";
            return true;
        }

        /// <summary>Muko resolved: <paramref name="answered"/> will discard instead of draw.</summary>
        public static void NoteReplaced(DuelEngine engine, ChainLink answered)
        {
            if (engine == null || answered == null) return;
            StateOf(engine).Replaced.Add(answered);
            engine.NotifyPublic();
        }

        // ───────────────────────────── resolving ─────────────────────────────

        /// <summary>
        /// DuelEngine.ResolveChainStack: when <paramref name="link"/> is one Muko replaced, its operation does not
        /// run — the top of its drawer that it would have drawn is discarded to the GY instead, and the chain moves
        /// on. Returns whether this runtime handled the link.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || link == null || engine.GameOver) return false;
            if (!s_state.TryGetValue(engine, out var st) || !st.Replaced.Contains(link)) return false;
            link.Resolved = true;

            var drawer = link.Controller;
            if (drawer == null) return true;
            var draw = DrawClauseOf(link);
            var n = draw != null ? System.Math.Max(1, draw.Amount) : 1;
            var discarded = new List<CardInstance>();
            for (var i = 0; i < n && drawer.Deck != null && drawer.Deck.Count > 0; i++)
            {
                var id = drawer.Deck[0];
                if (id <= 0) break;
                var inst = engine.CreateCardInstance(id);
                drawer.Deck.RemoveAt(0);
                discarded.Add(inst);
            }

            foreach (var c in discarded)
                engine.SendCardToGrave(drawer, c);

            engine.Log($"Muko: {link.Card?.Name ?? "the answered effect"}'s draw is replaced — " +
                       $"{discarded.Count} card(s) are discarded to the GY instead of drawn.");
            engine.NotifyPublic();
            return true;
        }

        /// <summary>The Chain ends: Muko's records die with it (the answered link is resolved or gone).</summary>
        public static void OnChainEnded(DuelEngine engine)
        {
            if (engine != null && s_state.TryGetValue(engine, out var st)) st.Replaced.Clear();
        }
    }
}
