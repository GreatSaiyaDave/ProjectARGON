using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "CoinDiceInterrupts" (v161 LOD-b): the coin-toss / die-roll result event. Every coin toss and die roll a
    /// card effect makes goes through <see cref="TossCoins"/> / <see cref="RollDice"/>: the coins / dice are thrown
    /// (<see cref="DuelRng"/>, so <see cref="DuelRng.QueueCoin"/> / <see cref="DuelRng.QueueDie"/> stay deterministic and
    /// <see cref="CoinDicePresentation"/> sees every throw and re-throw), then, BEFORE the result applies, a player who
    /// may redo it chooses (ygopro EVENT_TOSS_COIN_NEGATE / EVENT_TOSS_DICE_NEGATE), and only then does the effect apply
    /// the final result (the caller's continuation).
    /// <list type="bullet">
    /// <item>Second Coin Toss (c36562627, Continuous Spell): while it is face-up and its effects apply, once per turn,
    /// when its controller tosses coins they may toss them all again (Lua <c>ep==tp</c>, flag per player until the End
    /// Phase); the new result replaces the old.</item>
    /// <item>Dice Re-Roll (c83241722, Normal Trap): registers for its controller, for the rest of the turn, one re-roll of
    /// a die roll by either player (all its dice; the same player rolls again). The flag is set when it is used.</item>
    /// </list>
    /// A redo throws a new toss, so another player's redo can still apply to it (the turn player is asked first). The AI
    /// redoes only a result that is bad for it (each call site says what is good for the player who threw). A human
    /// answers through the card picker (<see cref="PendingActivation.LegalTargets"/> = the redo card: tap it to redo,
    /// CANCEL keeps the result), holding the Chain while it resolves (<see cref="PendingActivation.HoldsChainResolution"/>);
    /// the continuation then applies and the Chain resumes. Call sites call these in tail position: whatever must follow
    /// the outcome (an attack continuing, a coin call finishing) goes in the continuation.
    /// Residual: a human's redo cannot be offered while another, unrelated choice is already open (it is declined, logged).
    /// </summary>
    public static class CoinDiceInterruptsRuntime
    {
        sealed class Flags
        {
            /// <summary>Turn in which each player used Second Coin Toss's redo.</summary>
            public readonly Dictionary<DuelistState, int> CoinRedoUsedTurn = new();
            /// <summary>Turn in which each player gained Dice Re-Roll's effect, and the card that gave it.</summary>
            public readonly Dictionary<DuelistState, (int turn, CardInstance card)> DiceReRollGranted = new();
            /// <summary>Turn in which each player used a Dice Re-Roll.</summary>
            public readonly Dictionary<DuelistState, int> DiceReRollUsedTurn = new();
        }

        sealed class TossEvent
        {
            public bool IsDice;
            public DuelistState Performer;
            public CardInstance Source;
            public bool[] Coins;
            public int[] Dice;
            public Func<bool> GoodForPerformer;
            public Action Apply;
            /// <summary>Players who declined to redo this throw (a new throw asks them again).</summary>
            public readonly HashSet<DuelistState> Declined = new();
            public DuelistState Decider;
            public CardInstance RedoCard;
        }

        static readonly ConditionalWeakTable<DuelEngine, Flags> s_flags = new();

        /// <summary>The targeting effect whose Archfiend die rolls are settled (<see cref="ReapplyAfterArchfiendRolls"/>).</summary>
        [ThreadStatic] static EffectClause s_rollsSettledClause;
        [ThreadStatic] static CardInstance s_rollsSettledTarget;

        static Flags FlagsOf(DuelEngine engine) => s_flags.GetValue(engine, _ => new Flags());

        // ───────────────────────────── the event ─────────────────────────────

        /// <summary>
        /// <paramref name="tosser"/> tosses <paramref name="count"/> coin(s) for <paramref name="source"/>.
        /// <paramref name="apply"/> gets the final results (true = Heads). <paramref name="goodForTosser"/> tells the AI
        /// whether a result is good for the tosser. True when the result waits on a player's redo choice.
        /// </summary>
        public static bool TossCoins(DuelEngine engine, DuelistState tosser, CardInstance source, int count,
            Func<bool[], bool> goodForTosser, Action<bool[]> apply)
        {
            var ev = new TossEvent { Performer = tosser, Source = source, Coins = new bool[Math.Max(1, count)] };
            ev.GoodForPerformer = () => goodForTosser == null || goodForTosser(ev.Coins);
            ev.Apply = () => apply?.Invoke(ev.Coins);
            return Run(engine, ev);
        }

        /// <summary>One coin: <see cref="TossCoins"/>.</summary>
        public static bool TossCoin(DuelEngine engine, DuelistState tosser, CardInstance source,
            Func<bool, bool> goodForTosser, Action<bool> apply) =>
            TossCoins(engine, tosser, source, 1,
                goodForTosser == null ? null : r => goodForTosser(r[0]),
                r => apply?.Invoke(r[0]));

        /// <summary>
        /// <paramref name="roller"/> rolls <paramref name="count"/> six-sided dice for <paramref name="source"/>;
        /// see <see cref="TossCoins"/>.
        /// </summary>
        public static bool RollDice(DuelEngine engine, DuelistState roller, CardInstance source, int count,
            Func<int[], bool> goodForRoller, Action<int[]> apply)
        {
            var ev = new TossEvent { IsDice = true, Performer = roller, Source = source, Dice = new int[Math.Max(1, count)] };
            ev.GoodForPerformer = () => goodForRoller == null || goodForRoller(ev.Dice);
            ev.Apply = () => apply?.Invoke(ev.Dice);
            return Run(engine, ev);
        }

        /// <summary>One die: <see cref="RollDice"/>.</summary>
        public static bool RollDie(DuelEngine engine, DuelistState roller, CardInstance source,
            Func<int, bool> goodForRoller, Action<int> apply) =>
            RollDice(engine, roller, source, 1,
                goodForRoller == null ? null : r => goodForRoller(r[0]),
                r => apply?.Invoke(r[0]));

        static bool Run(DuelEngine engine, TossEvent ev)
        {
            if (engine == null) return false;
            Throw(engine, ev);
            return Settle(engine, ev);
        }

        static void Throw(DuelEngine engine, TossEvent ev)
        {
            engine.Rng.SetPresentationContext(ev.Performer != null && ev.Performer.IsPlayer, ev.Source?.Name);
            if (ev.IsDice)
            {
                for (var i = 0; i < ev.Dice.Length; i++)
                    ev.Dice[i] = engine.Rng.RollDie();
            }
            else
            {
                for (var i = 0; i < ev.Coins.Length; i++)
                    ev.Coins[i] = engine.Rng.TossCoin();
            }
        }

        /// <summary>Offers every applicable redo, then applies the result. True when a human's choice is open.</summary>
        static bool Settle(DuelEngine engine, TossEvent ev)
        {
            while (true)
            {
                var redoCard = NextOffer(engine, ev, out var decider);
                if (redoCard == null)
                {
                    ev.Apply();
                    return false;
                }

                if (!engine.IsHumanControlled(decider))
                {
                    var good = ev.GoodForPerformer();
                    var redo = ReferenceEquals(decider, ev.Performer) ? !good : good;
                    if (redo) Redo(engine, ev, decider, redoCard);
                    else ev.Declined.Add(decider);
                    continue;
                }

                if (engine.PendingActivation != null)
                {
                    engine.Log($"{redoCard.Name}: another choice is open — {decider.Name} keeps the result.");
                    ev.Declined.Add(decider);
                    continue;
                }

                Hold(engine, ev, decider, redoCard);
                return true;
            }
        }

        static CardInstance NextOffer(DuelEngine engine, TossEvent ev, out DuelistState decider)
        {
            decider = null;
            if (engine.GameOver) return null;
            var first = engine.TurnPlayer ?? ev.Performer;
            foreach (var who in new[] { first, engine.OpponentOf(first) })
            {
                if (who == null || ev.Declined.Contains(who)) continue;
                var card = ev.IsDice
                    ? DiceReRollFor(engine, who)
                    : ReferenceEquals(who, ev.Performer) ? SecondCoinTossFor(engine, who) : null;
                if (card == null) continue;
                decider = who;
                return card;
            }

            return null;
        }

        static void Redo(DuelEngine engine, TossEvent ev, DuelistState decider, CardInstance redoCard)
        {
            var f = FlagsOf(engine);
            if (ev.IsDice) f.DiceReRollUsedTurn[decider] = engine.TurnNumber;
            else f.CoinRedoUsedTurn[decider] = engine.TurnNumber;
            var before = Describe(ev);
            Throw(engine, ev);
            ev.Declined.Clear();
            engine.Log(ev.IsDice
                ? $"{redoCard.Name}: {decider.Name} has {ev.Performer?.Name} re-roll ({before} → {Describe(ev)})."
                : $"{redoCard.Name}: {decider.Name} tosses again ({before} → {Describe(ev)}).");
        }

        static string Describe(TossEvent ev) =>
            ev.IsDice
                ? string.Join(", ", ev.Dice)
                : string.Join(", ", ev.Coins.Select(h => h ? "Heads" : "Tails"));

        static void Hold(DuelEngine engine, TossEvent ev, DuelistState decider, CardInstance redoCard)
        {
            ev.Decider = decider;
            ev.RedoCard = redoCard;
            var what = ev.IsDice ? (ev.Dice.Length > 1 ? "dice" : "die") : (ev.Coins.Length > 1 ? "coins" : "coin");
            var whose = ReferenceEquals(decider, ev.Performer) ? "your" : $"{ev.Performer?.Name}'s";
            var redoWords = ev.IsDice
                ? (ReferenceEquals(decider, ev.Performer) ? "re-roll" : "have them re-roll")
                : "toss again";
            // EngineResiduals (v163): the generic card pick (ContinuousTrapsAndSpiritSupportRuntime.AskCardPick) — tap the
            // redo card or CANCEL. It holds the Chain while it resolves and the live Standby Phase (Blind Destruction's
            // roll), so the Standby upkeeps queued behind it (The Unfriendly Amazon's Tribute) still run afterwards.
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, decider, redoCard, new CardPickRequest
            {
                Candidates = () => new List<CardInstance> { redoCard },
                Kind = EffectTargetKind.AnyCardOnField,
                Prompt = $"{whose} {what} for {ev.Source?.Name ?? "the effect"}: {Describe(ev)}. Tap " +
                         $"{redoCard.Name} to {redoWords} (the new result replaces it), or CANCEL to keep it.",
                AskWithOneCandidate = true,
                AiChoose = _ => null, // not reached: Settle asks a human only while no other prompt is open
                HoldsChain = engine.IsResolvingChain,
                HoldsStandby = engine.Phase == DuelPhase.Standby,
                Finish = chosen => Answer(engine, ev, redo: chosen != null),
                Cancel = () => Answer(engine, ev, redo: false)
            });
        }

        /// <summary>The human tapped the redo card (redo) or CANCEL (keep the result); the next redo is offered, or the
        /// result applies. AskCardPick then resumes the held Chain / Standby Phase.</summary>
        static void Answer(DuelEngine engine, TossEvent ev, bool redo)
        {
            if (redo)
                Redo(engine, ev, ev.Decider, ev.RedoCard);
            else
            {
                ev.Declined.Add(ev.Decider);
                engine.Log($"{ev.RedoCard?.Name}: {ev.Decider?.Name} keeps the result.");
            }

            Settle(engine, ev);
        }

        // ───────────────────────────── the redo cards ─────────────────────────────

        /// <summary>Second Coin Toss: a face-up card of <paramref name="who"/> whose redo is available now, or null.</summary>
        static CardInstance SecondCoinTossFor(DuelEngine engine, DuelistState who)
        {
            if (FlagsOf(engine).CoinRedoUsedTurn.TryGetValue(who, out var t) && t == engine.TurnNumber) return null;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (st == null || !st.FaceUp || st.ActivationUnresolvedOnChain || st.IsNegated) continue;
                if (!HasClause(st, EffectTiming.ContinuousWhileFaceUp, EffectActionKind.RedoOwnCoinTossOncePerTurn))
                    continue;
                if (ContinuousNegationRuntime.NegatesEffectActivationOnField(engine, st)) continue;
                if (TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, st)) continue;
                return st;
            }

            return null;
        }

        /// <summary>Dice Re-Roll: the card whose effect <paramref name="who"/> gained this turn and has not used, or null.</summary>
        static CardInstance DiceReRollFor(DuelEngine engine, DuelistState who)
        {
            var f = FlagsOf(engine);
            if (!f.DiceReRollGranted.TryGetValue(who, out var g) || g.turn != engine.TurnNumber) return null;
            if (f.DiceReRollUsedTurn.TryGetValue(who, out var used) && used == engine.TurnNumber) return null;
            return g.card;
        }

        static bool HasClause(CardInstance card, EffectTiming timing, EffectActionKind action)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClausesFor(timing))
                if (c != null && c.Action == action) return true;
            return false;
        }

        /// <summary>The GrantDiceReRollThisTurn arm: <paramref name="who"/> may have one die roll re-rolled this turn.</summary>
        internal static void GrantDiceReRoll(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null) return;
            FlagsOf(engine).DiceReRollGranted[who] = (engine.TurnNumber, source);
            var used = FlagsOf(engine).DiceReRollUsedTurn.TryGetValue(who, out var t) && t == engine.TurnNumber;
            engine.Log(used
                ? $"{source?.Name}: {who.Name} already used this effect this turn."
                : $"{source?.Name}: once this turn, when either player rolls a die, {who.Name} may have it re-rolled.");
        }

        /// <summary>True while <paramref name="who"/> holds an unused Dice Re-Roll this turn (tests, AI).</summary>
        public static bool HasDiceReRoll(DuelEngine engine, DuelistState who) =>
            engine != null && who != null && DiceReRollFor(engine, who) != null;

        // ───────────────────────────── Archfiend rolls (ApplyClause) ─────────────────────────────

        /// <summary>
        /// The targeting effect waited on an Archfiend's re-roll choice and was not negated: apply it now, without
        /// rolling again (the rolls are settled), outside the original ApplyClause call.
        /// </summary>
        internal static void ReapplyAfterArchfiendRolls(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target, int costNumeric)
        {
            var neg = false;
            var ended = false;
            s_rollsSettledClause = clause;
            s_rollsSettledTarget = target;
            try
            {
                TextEffectRuntime.ApplyClause(engine, who, source, clause, target, ref neg, ref ended, costNumeric);
            }
            finally
            {
                s_rollsSettledClause = null;
                s_rollsSettledTarget = null;
            }

            if (engine.IsResolvingChain)
            {
                engine.ChainNegatedAttack |= neg;
                engine.ChainEndedBattle |= ended;
            }
        }

        /// <summary>ApplyClause: the Archfiend rolls for this clause and target are already settled (consumed once).</summary>
        internal static bool ArchfiendRollsSettled(EffectClause clause, CardInstance target)
        {
            if (s_rollsSettledClause == null || !ReferenceEquals(s_rollsSettledClause, clause) ||
                !ReferenceEquals(s_rollsSettledTarget, target))
                return false;
            s_rollsSettledClause = null;
            s_rollsSettledTarget = null;
            return true;
        }
    }
}
