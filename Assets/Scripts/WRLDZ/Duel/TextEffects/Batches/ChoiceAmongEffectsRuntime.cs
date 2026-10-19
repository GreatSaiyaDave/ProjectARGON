using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChoiceAmongEffects batch (<see cref="ChoiceAmongEffectsAtoms"/>):
    /// <list type="bullet">
    /// <item>"Apply 1 of these effects" — the choice is made as the card resolves (Fuh-Rin-Ka-Zan c1781310: s.activate lists
    /// the bullets that can apply now, then Duel.SelectOption). The chooser clause
    /// (<see cref="EffectActionKind.ApplyOneOfTheseEffectsAtResolution"/>) asks through the generic option pick
    /// (<see cref="ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(DuelEngine, DuelistState, CardInstance, CardPickRequest)"/>,
    /// option tokens, the Chain held while it resolves); the AI picks the bullet worth most. A bullet clause
    /// (<see cref="EffectClause.EffectChoiceAppliedAtResolution"/>) applies only through this pick
    /// (<see cref="IsApplyingChosen"/>, asked at the top of <c>TextEffectRuntime.ApplyClause</c>).</item>
    /// <item>The activation conditions <see cref="FaceUpAttributesOnField"/>, <see cref="OpponentHandExactly"/> and
    /// <see cref="AnyBulletApplicable"/> (Lua s.target chk), checked when the card is activated.</item>
    /// <item>Penalty Game! c967928 bullets: <see cref="EffectActionKind.OpponentCannotDrawDuringNextDrawPhase"/>
    /// (<see cref="CannotDrawNow"/>, asked by <c>DuelEngine.Draw</c>; ends with that player's next Draw Phase,
    /// <see cref="OnDrawPhaseEnd"/>) and <see cref="EffectActionKind.OpponentCannotActivateSpellTrapCardsThisTurn"/>
    /// (the shared card activation lock <c>SpellTrapSealsAndImmunityRuntime.RegisterCardActivationLock</c>, Spells + Traps, this turn).</item>
    /// </list>
    /// </summary>
    public static class ChoiceAmongEffectsRuntime
    {
        public const string FaceUpAttributesOnField = "ChoiceFaceUpAttributesOnField";
        public const string OpponentHandExactly = "ChoiceOpponentHandExactly";
        public const string AnyBulletApplicable = "ChoiceAnyBulletApplicable";

        sealed class DrawLock
        {
            public DuelistState Who;
            /// <summary>The turn whose Draw Phase does not count (activated during that player's own Draw Phase), or 0.</summary>
            public int ExcludeTurn;
            public string Source;
        }

        sealed class State
        {
            public readonly List<DrawLock> DrawLocks = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>The bullet <see cref="ResolveChoice"/> is applying now (null: none).</summary>
        [ThreadStatic] static EffectClause s_applying;

        // ───────────────────────────── activation conditions ─────────────────────────────

        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null) return false;
            switch (c.ActivationCondition)
            {
                case FaceUpAttributesOnField:
                {
                    var need = (c.ConditionName ?? "").Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                    var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var side in new[] { engine.Player, engine.Opponent })
                    foreach (var m in side?.MonstersOnField() ?? Enumerable.Empty<CardInstance>())
                    {
                        if (m == null || !m.FaceUp) continue;
                        var a = EquipFamilyRuntime.CurrentAttribute(m);
                        if (!string.IsNullOrEmpty(a)) shown.Add(a);
                    }

                    ok = need.Count > 0 && need.All(shown.Contains);
                    reason = $"Needs face-up {string.Join(", ", need)} monster(s) on the field.";
                    return true;
                }
                case OpponentHandExactly:
                {
                    var opp = engine.OpponentOf(who);
                    ok = opp != null && opp.HandCount == c.ConditionAmount;
                    reason = $"Only while your opponent has exactly {c.ConditionAmount} cards in their hand.";
                    return true;
                }
                case AnyBulletApplicable:
                    ok = ApplicableBullets(engine, who, card).Count > 0;
                    reason = "None of its effects can be applied now.";
                    return true;
            }

            return false;
        }

        // ───────────────────────────── "Apply 1 of these effects" ─────────────────────────────

        public static bool IsAppliedBullet(EffectClause c) =>
            c != null && c.EffectChoiceAppliedAtResolution && c.EffectChoiceIndex > 0;

        /// <summary>TextEffectRuntime.ApplyClause: true while the chooser applies <paramref name="c"/> as the chosen bullet.</summary>
        public static bool IsApplyingChosen(EffectClause c) => c != null && ReferenceEquals(s_applying, c);

        /// <summary>
        /// The bullet shapes whose "can this apply now" the runtime can judge (Lua s.activate's option filters); the atom
        /// compiles no other bullet for an "Apply" choice.
        /// </summary>
        public static bool CanJudge(EffectClause c)
        {
            if (c == null || c.RequiresTargetChoice) return false;
            switch (c.Action)
            {
                case EffectActionKind.Destroy:
                    return c.Side == EffectSide.Opponent &&
                           (c.Zone == EffectZoneFilter.FieldMonsters || c.Zone == EffectZoneFilter.FieldSpellTraps);
                case EffectActionKind.DiscardRandomFromOpponentHand:
                    return c.Amount > 0;
                case EffectActionKind.Draw:
                    return c.Side == EffectSide.Controller && c.Amount > 0;
                default:
                    return false;
            }
        }

        /// <summary>Lua s.activate's option filters: the bullet would do something now.</summary>
        static bool CanApply(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null || !CanJudge(c)) return false;
            switch (c.Action)
            {
                case EffectActionKind.Destroy when c.Zone == EffectZoneFilter.FieldMonsters:
                    return opp.MonstersOnField().Any(m => m != null);
                case EffectActionKind.Destroy:
                    return opp.SpellTrapsOnField().Any(s => s != null);
                case EffectActionKind.DiscardRandomFromOpponentHand:
                    return opp.HandCount >= c.Amount;
                case EffectActionKind.Draw:
                    return DrawThenDiscardRuntime.CanDraw(engine, who, c.Amount);
                default:
                    return false;
            }
        }

        static List<EffectClause> Bullets(CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            return prog == null
                ? new List<EffectClause>()
                : prog.ClausesFor(EffectTiming.Activate).Where(IsAppliedBullet).OrderBy(c => c.EffectChoiceIndex).ToList();
        }

        static List<EffectClause> ApplicableBullets(DuelEngine engine, DuelistState who, CardInstance card) =>
            engine == null || who == null ? new List<EffectClause>() : Bullets(card).Where(c => CanApply(engine, who, c)).ToList();

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.ApplyOneOfTheseEffectsAtResolution"/>: offer the bullets that can
        /// apply now; the chosen one applies. A lone bullet applies with no prompt; none: nothing happens.
        /// </summary>
        public static void ResolveChoice(DuelEngine engine, DuelistState who, CardInstance source, EffectClause chooser)
        {
            if (engine == null || who == null || source == null || engine.GameOver) return;
            var open = ApplicableBullets(engine, who, source);
            if (open.Count == 0)
            {
                engine.Log($"{source.Name}: none of its effects can be applied.");
                return;
            }

            var tokens = open.Select(b => OptionToken(DescribeBullet(b) ?? "Apply that effect", b.EffectChoiceIndex)).ToList();
            ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
            {
                Candidates = () => tokens,
                Kind = EffectTargetKind.None,
                Prompt = $"{source.Name}: apply 1 of these effects.",
                AiChoose = list => AiChoose(engine, who, open, list),
                HoldsChain = engine.IsResolvingChain,
                Finish = chosen =>
                {
                    var idx = chosen?.Def?.level ?? 0;
                    var bullet = open.FirstOrDefault(b => b.EffectChoiceIndex == idx) ?? open[0];
                    Apply(engine, who, source, bullet);
                }
            });
        }

        static void Apply(DuelEngine engine, DuelistState who, CardInstance source, EffectClause bullet)
        {
            if (engine.GameOver) return;
            engine.Log($"{source.Name}: {who.Name} applies \"{DescribeBullet(bullet) ?? "that effect"}\".");
            var prev = s_applying;
            s_applying = bullet;
            try
            {
                var d = false;
                TextEffectRuntime.ApplyClause(engine, who, source, bullet, null, ref d, ref d);
            }
            finally
            {
                s_applying = prev;
            }

            engine.NotifyPublic();
        }

        /// <summary>The AI applies the bullet worth most to it now.</summary>
        static CardInstance AiChoose(DuelEngine engine, DuelistState who, List<EffectClause> open, List<CardInstance> tokens)
        {
            EffectClause best = null;
            var bestScore = double.MinValue;
            foreach (var b in open)
            {
                var s = Score(engine, who, b);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = b;
                }
            }

            return tokens.FirstOrDefault(t => t?.Def != null && best != null && t.Def.level == best.EffectChoiceIndex)
                   ?? tokens.FirstOrDefault();
        }

        static double Score(DuelEngine engine, DuelistState who, EffectClause b)
        {
            var opp = engine.OpponentOf(who);
            switch (b.Action)
            {
                case EffectActionKind.Destroy when b.Zone == EffectZoneFilter.FieldMonsters:
                    return opp.MonstersOnField().Where(m => m != null)
                        .Sum(m => 2.0 + Math.Max(0, m.Def?.atk ?? 0) / 1000.0);
                case EffectActionKind.Destroy:
                    return opp.SpellTrapsOnField().Count(s => s != null) * 1.5;
                case EffectActionKind.DiscardRandomFromOpponentHand:
                    return b.Amount * 1.2;
                case EffectActionKind.Draw:
                    return who.Deck.Count - b.Amount < 3 ? 0.1 : b.Amount;
                default:
                    return 0;
            }
        }

        /// <summary>The picker / log text of a bullet of this batch, or null (PoisonMeteorCestusRuntime.Describe asks it).</summary>
        public static string DescribeBullet(EffectClause c)
        {
            if (c == null) return null;
            switch (c.Action)
            {
                case EffectActionKind.Destroy when c.Side == EffectSide.Opponent && c.Zone == EffectZoneFilter.FieldMonsters &&
                                                   !c.RequiresTargetChoice:
                    return "Destroy all monsters your opponent controls";
                case EffectActionKind.Destroy when c.Side == EffectSide.Opponent && c.Zone == EffectZoneFilter.FieldSpellTraps &&
                                                   !c.RequiresTargetChoice:
                    return "Destroy all Spell and Trap Cards your opponent controls";
                case EffectActionKind.DiscardRandomFromOpponentHand when IsAppliedBullet(c):
                    return $"Discard {Math.Max(1, c.Amount)} random card{(c.Amount == 1 ? "" : "s")} from your opponent's hand";
                case EffectActionKind.Draw when IsAppliedBullet(c):
                    return $"Draw {Math.Max(1, c.Amount)} card{(c.Amount == 1 ? "" : "s")}";
                case EffectActionKind.OpponentCannotDrawDuringNextDrawPhase:
                    return "Your opponent cannot draw during their next Draw Phase";
                case EffectActionKind.OpponentCannotActivateSpellTrapCardsThisTurn:
                    return "Your opponent cannot activate Spell/Trap Cards this turn";
                default:
                    return null;
            }
        }

        static CardInstance OptionToken(string name, int index) => new()
        {
            InstanceId = 0,
            CardId = 0,
            FaceUp = true,
            Def = new CardDef
            {
                id = 0,
                name = name,
                type = CallsDiceAndSetTriggersRuntime.OptionTokenKind,
                frameType = "token",
                desc = "",
                atk = -1,
                def = -1,
                level = index,
                race = "",
                attribute = "",
                archetype = ""
            }
        };

        // ───────────────────────────── Penalty Game!: no draw in the next Draw Phase ─────────────────────────────

        /// <summary>ApplyClause arm of <see cref="EffectActionKind.OpponentCannotDrawDuringNextDrawPhase"/>.</summary>
        public static void RegisterNoDrawNextDrawPhase(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            var st = StateOf(engine);
            st.DrawLocks.Add(new DrawLock
            {
                Who = opp,
                // Lua: activated during that player's own Draw Phase, the lock skips this one (label = turn count).
                ExcludeTurn = ReferenceEquals(engine.TurnPlayer, opp) && engine.Phase == DuelPhase.Draw ? engine.TurnNumber : 0,
                Source = source?.Name ?? "Effect"
            });
            engine.Log($"{source?.Name ?? "Effect"}: {opp.Name} cannot draw during their next Draw Phase.");
        }

        /// <summary>
        /// DuelEngine.Draw: <paramref name="who"/> cannot draw now — a Draw Phase while a lock on them lasts (Lua skipcon:
        /// any Draw Phase but the excluded one, until the lock ends with their own next Draw Phase).
        /// </summary>
        public static bool CannotDrawNow(DuelEngine engine, DuelistState who, out string source)
        {
            source = null;
            if (engine == null || who == null || engine.Phase != DuelPhase.Draw) return false;
            if (!s_state.TryGetValue(engine, out var st) || st.DrawLocks.Count == 0) return false;
            foreach (var l in st.DrawLocks)
            {
                if (!ReferenceEquals(l.Who, who) || l.ExcludeTurn == engine.TurnNumber) continue;
                source = l.Source;
                return true;
            }

            return false;
        }

        public static bool CannotDrawNow(DuelEngine engine, DuelistState who) => CannotDrawNow(engine, who, out _);

        /// <summary>DuelEngine.ContinueDrawPhase, as <paramref name="who"/>'s Draw Phase ends: their locks end with it.</summary>
        public static void OnDrawPhaseEnd(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !s_state.TryGetValue(engine, out var st)) return;
            st.DrawLocks.RemoveAll(l => ReferenceEquals(l.Who, who) && l.ExcludeTurn != engine.TurnNumber);
        }

        // ───────────────────────────── Penalty Game!: no Spell/Trap Card activations this turn ─────────────────────────────

        /// <summary>
        /// ApplyClause arm of <see cref="EffectActionKind.OpponentCannotActivateSpellTrapCardsThisTurn"/>: the shared card
        /// activation lock (<c>SpellTrapSealsAndImmunityRuntime.RegisterCardActivationLock</c>, Spells + Traps, this turn;
        /// card activations from the hand or Set only, read by its <c>ActivationLocked</c> through the hub).
        /// </summary>
        public static void RegisterSpellTrapCardLock(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            SpellTrapSealsAndImmunityRuntime.RegisterCardActivationLock(engine, opp, spells: true, traps: true,
                engine.TurnNumber, source);
            engine.Log($"{source?.Name ?? "Effect"}: {opp.Name} cannot activate Spell/Trap Cards this turn.");
        }

        /// <summary>Whether <paramref name="who"/> may not activate the Spell/Trap Card <paramref name="card"/> now (the shared lock).</summary>
        public static bool ActivationLocked(DuelEngine engine, DuelistState who, CardInstance card, out string reason) =>
            SpellTrapSealsAndImmunityRuntime.ActivationLocked(engine, who, card, out reason);
    }
}
