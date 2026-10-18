using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ZoneCapsAndPhaseWindows batch: the opponent's Standby Phase window (Thunder of Ruler).
    /// <list type="bullet">
    /// <item><b>The window.</b> When the turn player leaves their Standby Phase for Main Phase 1 (DuelEngine
    /// <c>LeaveStandbyForMain1</c>: after the Standby triggers, upkeeps and any standby-only ignition hold), the other duelist
    /// gets priority in that Standby Phase: <see cref="ResponseTiming.StandbyResponse"/> opens once per turn, and only
    /// when they have a legal answer (<see cref="BeginWindow"/>). A pass or a resolved answer moves the turn on to Main
    /// Phase 1. A skipped Standby Phase (Solomon's Lawbook) has no window.</item>
    /// <item><b>The answer.</b> A Trap whose Activate clause carries the activation condition <see cref="OpponentStandbyPhase"/>
    /// (Lua condition IsTurnPlayer(1-tp) and IsPhase(PHASE_STANDBY)) is seated on a Chain as a response
    /// (TextEffectRuntime.SeatResponseOnChain); the turn player may chain to it.</item>
    /// </list>
    /// </summary>
    public static class StandbyWindowRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/>: only during the opponent's Standby Phase (offered in the
        /// <see cref="ResponseTiming.StandbyResponse"/> window, or chained to a link in that phase).
        /// </summary>
        public const string OpponentStandbyPhase = "OpponentStandbyPhase";

        sealed class State
        {
            public int OfferedTurn;
            public DuelistState OfferedFor;
            public int HeldTurn;
            public DuelistState HeldFor;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        /// <summary>
        /// DuelEngine.OfferStandbyWindow: true the first time <paramref name="turnPlayer"/> leaves this turn's Standby
        /// Phase (a Main Phase 1 skip that holds the Standby Phase again does not offer it twice).
        /// </summary>
        public static bool BeginWindow(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null || engine.GameOver || engine.Phase != DuelPhase.Standby ||
                !ReferenceEquals(engine.TurnPlayer, turnPlayer))
                return false;
            var st = s_state.GetValue(engine, _ => new State());
            // The turn player is part of the key: a new duel (new DuelistState objects) offers turn N again.
            if (st.OfferedTurn == engine.TurnNumber && ReferenceEquals(st.OfferedFor, turnPlayer)) return false;
            st.OfferedTurn = engine.TurnNumber;
            st.OfferedFor = turnPlayer;
            return true;
        }

        /// <summary>
        /// DuelEngine.FinishStandbyWindow (v178 review fix): the window closed while a prompt, a response window or a Chain
        /// was still open (a trigger of the Chain that answered it). The Standby Phase holds (DuelEngine.StandbyPausedFor) and
        /// DuelEngine.Notify moves the turn on to Main Phase 1 once nothing is open (<see cref="FinishHeld"/>), on either
        /// player's turn; SimpleAi waits for it.
        /// </summary>
        public static void HoldFinish(DuelEngine engine, DuelistState turnPlayer)
        {
            if (engine == null || turnPlayer == null) return;
            var st = s_state.GetValue(engine, _ => new State());
            st.HeldTurn = engine.TurnNumber;
            st.HeldFor = turnPlayer;
        }

        /// <summary>A held finish (<see cref="HoldFinish"/>) is waiting in this turn's Standby Phase.</summary>
        public static bool FinishHeld(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.HeldFor != null &&
            st.HeldTurn == engine.TurnNumber && ReferenceEquals(st.HeldFor, engine.TurnPlayer) &&
            engine.Phase == DuelPhase.Standby && !engine.GameOver;

        /// <summary>DuelEngine.ContinueAfterStandby: the Standby Phase moves on, so a held finish is spent.</summary>
        public static void ReleaseFinish(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.HeldTurn = 0;
            st.HeldFor = null;
        }

        /// <summary>Activation condition <see cref="OpponentStandbyPhase"/>.</summary>
        public static bool WindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only during your opponent's Standby Phase.";
            if (engine == null || who == null || engine.Phase != DuelPhase.Standby) return false;
            var tp = engine.TurnPlayer;
            if (tp == null || ReferenceEquals(tp, who) || !ReferenceEquals(engine.OpponentOf(who), tp)) return false;
            reason = "OK";
            return true;
        }

        /// <summary>The program is a Trap whose activation condition is the opponent's Standby Phase (Thunder of Ruler).</summary>
        public static bool AnswersWindow(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.Activate && c.ActivationCondition == OpponentStandbyPhase);

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond): a "no Battle Phase this turn" answer when the turn player could still conduct
        /// a Battle Phase this turn and has something to attack with — a monster on their field, or one in their hand.
        /// </summary>
        public static bool AiWantsToAnswer(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!AnswersWindow(prog) || !WindowOpen(engine, ai, out _)) return false;
            if (!prog.ClauseList.Exists(c => c != null && c.Action == EffectActionKind.ForbidBattlePhaseThisTurn))
                return false;
            var tp = engine.TurnPlayer;
            if (tp == null || TributeSummonsRuntime.BattlePhaseForbidden(engine, tp)) return false;
            if (engine.TurnNumber == 1 && ReferenceEquals(engine.FirstPlayer, tp)) return false;
            return tp.MonstersOnField().Any(m => m != null) ||
                   tp.Hand.Any(h => h?.Def != null && h.Def.IsMonster);
        }

        /// <summary>
        /// The <see cref="EffectActionKind.ForbidBattlePhaseThisTurn"/> arm (Lua EFFECT_CANNOT_BP, target range (0,1), reset
        /// at the End Phase): the activator's opponent cannot conduct a Battle Phase this turn. A stamp of the turn
        /// (<see cref="DuelistState.BattlePhaseForbiddenTurn"/>), so it lasts after the card leaves the field.
        /// </summary>
        public static void ForbidOpponentBattlePhase(DuelEngine engine, DuelistState who, CardInstance source)
        {
            var opp = engine?.OpponentOf(who);
            if (opp == null) return;
            TributeSummonsRuntime.ApplyBattlePhaseOath(engine, opp, source);
        }
    }

    /// <summary>
    /// ZoneCapsAndPhaseWindows batch: "It becomes the End Phase of this turn" (Neko Mane King) — a safe jump to the End
    /// Phase. <see cref="Request"/> (the <see cref="EffectActionKind.PhaseJumpToEndPhase"/> arm) forbids the turn player's
    /// Battle Phase (EFFECT_CANNOT_BP) and records the jump; the turn then goes to its End Phase at the next safe point:
    /// <list type="bullet">
    /// <item>once play is idle (DuelEngine Notify → <see cref="OnPlayIdle"/>) in Main Phase 1, the Battle Phase (no attack
    /// declared, outside the Damage Step: the battle in progress finishes first) or Main Phase 2 — DuelEngine.EndTurn, which
    /// ends a Battle Phase through its End Step;</item>
    /// <item>as the Draw Phase moves on to the Standby Phase, or the Standby Phase to Main Phase 1 (DuelEngine checks
    /// <see cref="Take"/> there), so the skipped phases' effects never happen.</item>
    /// </list>
    /// In the End Phase itself the request does nothing.
    /// </summary>
    public static class PhaseJumpRuntime
    {
        sealed class State
        {
            public int JumpTurn;
            public DuelistState JumpFor;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        /// <summary>
        /// <see cref="EffectClause.RequiresSentByOpponentEffectOnTheirTurn"/>: <paramref name="card"/> entered
        /// <paramref name="owner"/>'s GY this turn, during the opponent's turn, by an effect (not a cost) of that opponent,
        /// from <paramref name="owner"/>'s possession (field, hand or Deck — the GY-entry stamp's previous holder).
        /// </summary>
        public static bool SentByOpponentEffectOnTheirTurn(DuelEngine engine, DuelistState owner, CardInstance card)
        {
            if (engine == null || owner == null || card == null) return false;
            var opp = engine.OpponentOf(owner);
            return opp != null && ReferenceEquals(engine.TurnPlayer, opp) &&
                   card.GyEntryTurn != 0 && card.GyEntryTurn == engine.TurnNumber &&
                   (card.GyEntryReason & GyEntryReason.Effect) != 0 &&
                   (card.GyEntryReason & GyEntryReason.Cost) == 0 &&
                   ReferenceEquals(card.GyEntryReasonPlayer, opp) &&
                   ReferenceEquals(card.GyEntryPreviousHolder, owner);
        }

        /// <summary>
        /// The <see cref="EffectActionKind.PhaseJumpToEndPhase"/> arm (Lua s.operation: SkipPhase of 1-tp's Draw … Main Phase
        /// 2, EFFECT_CANNOT_BP for 1-tp): the opponent of <paramref name="who"/>, when it is their turn.
        /// </summary>
        public static void Request(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || engine.GameOver) return;
            var tp = engine.TurnPlayer;
            if (tp == null || ReferenceEquals(tp, who) || !ReferenceEquals(engine.OpponentOf(who), tp))
            {
                engine.Log($"{source?.Name}: it is not the opponent's turn — no effect.");
                return;
            }

            if (engine.Phase == DuelPhase.End)
            {
                engine.Log($"{source?.Name}: it is already the End Phase.");
                return;
            }

            tp.BattlePhaseForbiddenTurn = engine.TurnNumber;
            var st = s_state.GetValue(engine, _ => new State());
            st.JumpTurn = engine.TurnNumber;
            st.JumpFor = tp;
            engine.Log($"{source?.Name}: it becomes the End Phase of this turn.");
        }

        /// <summary>A jump to the End Phase is waiting for <paramref name="turnPlayer"/>'s turn.</summary>
        public static bool Pending(DuelEngine engine, DuelistState turnPlayer) =>
            engine != null && turnPlayer != null && s_state.TryGetValue(engine, out var st) && st.JumpTurn != 0 &&
            st.JumpTurn == engine.TurnNumber && ReferenceEquals(st.JumpFor, turnPlayer) &&
            ReferenceEquals(engine.TurnPlayer, turnPlayer);

        /// <summary>Consume the pending jump (true when there was one): the caller moves the turn to its End Phase.</summary>
        public static bool Take(DuelEngine engine, DuelistState turnPlayer)
        {
            if (!Pending(engine, turnPlayer) || !s_state.TryGetValue(engine, out var st)) return false;
            st.JumpTurn = 0;
            st.JumpFor = null;
            engine.Log($"[{turnPlayer.Name}] The turn moves on to the End Phase.");
            return true;
        }

        /// <summary>
        /// DuelEngine.Notify, once play is idle: a pending jump in Main Phase 1, the Battle Phase or Main Phase 2 ends the
        /// turn's open phase now (DuelEngine.EndTurn).
        /// </summary>
        public static void OnPlayIdle(DuelEngine engine)
        {
            var tp = engine?.TurnPlayer;
            if (tp == null || engine.GameOver || !Pending(engine, tp)) return;
            if (engine.Phase != DuelPhase.Main1 && engine.Phase != DuelPhase.Battle && engine.Phase != DuelPhase.Main2)
                return;
            if (engine.IsAwaitingResponse || engine.IsAwaitingEffectTarget || engine.HasDeclaredAttack ||
                engine.BattleStep == BattleStep.DamageStep)
                return;
            if (!Take(engine, tp)) return;
            engine.EndTurn(tp);
        }
    }

    /// <summary>
    /// ZoneCapsAndPhaseWindows batch: usable-zone caps (ygopro EFFECT_MAX_MZONE / EFFECT_MAX_SZONE). One count per player and
    /// zone kind that every placement and legality path reads:
    /// <list type="bullet">
    /// <item><see cref="DuelEngine.FirstEmpty"/> (and the preferred-zone paths: Normal Summon / Set, Special Summon, S/T Set,
    /// a monster returned to the field) finds no zone once the cap is reached; <see cref="DuelEngine.UsableMonsterZoneCount"/>
    /// / <see cref="UsableMonsterZones"/> are the multi-placement counts (Scapegoat, swarms, Magical Hats);
    /// <see cref="DuelEngine.MonsterZoneAfterLeaving"/> is the Tribute / material check (SummonProcedures, Ritual, Fusion:
    /// the cap must still leave room once the field Tributes are gone — Kaiser Colosseum's EFFECT_UNRELEASABLE_SUM).</item>
    /// <item>Placement context: by default the zone's owner places the card. <see cref="PlacedBy"/> names another placing
    /// player (Tokens an effect puts on the opponent's field: Ojama Trio); <see cref="ControlChange"/> marks a control change
    /// (ygopro LOCATION_REASON_CONTROL: Change of Heart, Snatch Steal, a Charmer).</item>
    /// <item>Kaiser Colosseum (<see cref="EffectActionKind.OpponentMonstersCappedByYourMonsterCount"/>): while its controller
    /// controls N ≥ 1 monsters, their opponent's monster cap is N — not for a control change nor for a monster the Kaiser
    /// controller's own effect places there (Lua s.value).</item>
    /// <item>Spatial Collapse (<see cref="EffectActionKind.EachPlayerFieldCardsCappedAtFive"/>): each player's monster cap is
    /// 5 minus their Spell &amp; Trap / Field cards, their Spell &amp; Trap cap 5 minus their monsters and Field card; no Field
    /// Spell is activated into an empty Field Zone with 5+ cards (Lua e5 / e6); a player above 5 sends the excess to the GY
    /// (<see cref="Adjust"/>, Lua EVENT_ADJUST; not during the Damage Step before damage calculation).</item>
    /// </list>
    /// A cap card applies while it is face-up, its activation has resolved and it is not negated.
    /// </summary>
    public static class ZoneCapsRuntime
    {
        /// <summary>Spatial Collapse's activation condition: both players have 5 or fewer cards on the field.</summary>
        public const string BothPlayersFiveOrFewerFieldCards = "BothPlayersFiveOrFewerFieldCards";

        const int NoCap = 99;

        sealed class Ctx
        {
            public DuelistState ReasonPlayer;
            public bool Control;
        }

        sealed class State
        {
            public readonly List<Ctx> Stack = new();
            public bool Adjusting;
            /// <summary>The duel the open adjust belongs to (a new duel has new DuelistState objects).</summary>
            public DuelistState AdjustingIn;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        sealed class Pop : IDisposable
        {
            readonly State _st;
            readonly Ctx _ctx;

            public Pop(State st, Ctx ctx)
            {
                _st = st;
                _ctx = ctx;
            }

            public void Dispose() => _st?.Stack.Remove(_ctx);
        }

        static IDisposable Push(DuelEngine engine, Ctx ctx)
        {
            if (engine == null) return new Pop(null, null);
            var st = s_state.GetValue(engine, _ => new State());
            st.Stack.Add(ctx);
            return new Pop(st, ctx);
        }

        /// <summary>The cards placed inside this scope are placed by <paramref name="reasonPlayer"/>'s effect.</summary>
        public static IDisposable PlacedBy(DuelEngine engine, DuelistState reasonPlayer) =>
            Push(engine, new Ctx { ReasonPlayer = reasonPlayer });

        /// <summary>The monsters placed inside this scope move by a control change.</summary>
        public static IDisposable ControlChange(DuelEngine engine) => Push(engine, new Ctx { Control = true });

        static Ctx Current(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.Stack.Count > 0
                ? st.Stack[st.Stack.Count - 1]
                : null;

        // ── counts ──

        static int Monsters(DuelistState who, ICollection<CardInstance> leaving = null) =>
            who?.MonsterZones?.Count(z => z?.Occupant != null && (leaving == null || !leaving.Contains(z.Occupant))) ?? 0;

        static int SpellTraps(DuelistState who) =>
            (who?.SpellTrapZones?.Count(z => z?.Occupant != null) ?? 0) +
            (who?.PendulumZones?.Count(z => z?.Occupant != null) ?? 0);

        static int FieldCard(DuelistState who) => who?.FieldSpellZone?.Occupant != null ? 1 : 0;

        /// <summary>Every card <paramref name="who"/> has on the field (ygopro LOCATION_ONFIELD).</summary>
        public static int FieldCards(DuelistState who) => Monsters(who) + SpellTraps(who) + FieldCard(who);

        static bool Live(CardInstance st, EffectActionKind action)
        {
            if (st?.Def == null || !st.FaceUp || st.IsNegated || st.ActivationUnresolvedOnChain) return false;
            var prog = CompiledEffectCache.GetOrCompile(st.Def);
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.ContinuousWhileFaceUp && c.Action == action);
        }

        /// <summary>Each live cap card on the field with its controller.</summary>
        static IEnumerable<(DuelistState holder, CardInstance card, EffectActionKind action)> CapCards(DuelEngine engine)
        {
            if (engine == null) yield break;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side?.SpellTrapZones == null) continue;
                foreach (var z in side.SpellTrapZones)
                {
                    var c = z?.Occupant;
                    if (c == null) continue;
                    if (Live(c, EffectActionKind.OpponentMonstersCappedByYourMonsterCount))
                        yield return (side, c, EffectActionKind.OpponentMonstersCappedByYourMonsterCount);
                    if (Live(c, EffectActionKind.EachPlayerFieldCardsCappedAtFive))
                        yield return (side, c, EffectActionKind.EachPlayerFieldCardsCappedAtFive);
                }
            }
        }

        static bool SpatialCollapseLive(DuelEngine engine) =>
            CapCards(engine).Any(x => x.action == EffectActionKind.EachPlayerFieldCardsCappedAtFive);

        /// <summary>
        /// The most monsters <paramref name="who"/> may control after placing one now (99 = no cap); with
        /// <paramref name="leaving"/>, once those monsters (Tributes from either field) have left.
        /// </summary>
        public static int MonsterCap(DuelEngine engine, DuelistState who, ICollection<CardInstance> leaving = null)
        {
            var cap = NoCap;
            if (engine == null || who == null) return cap;
            var ctx = Current(engine);
            foreach (var (holder, _, action) in CapCards(engine))
            {
                if (action == EffectActionKind.OpponentMonstersCappedByYourMonsterCount)
                {
                    if (!ReferenceEquals(engine.OpponentOf(holder), who)) continue;
                    var limit = Monsters(holder, leaving);
                    if (limit == 0 || (ctx != null && (ctx.Control || ReferenceEquals(ctx.ReasonPlayer, holder))))
                        continue;
                    cap = Math.Min(cap, limit);
                }
                else
                    cap = Math.Min(cap, 5 - SpellTraps(who) - FieldCard(who));
            }

            return cap;
        }

        /// <summary>The most Spell &amp; Trap Cards <paramref name="who"/> may have after placing one now (99 = no cap).</summary>
        public static int SpellTrapCap(DuelEngine engine, DuelistState who)
        {
            if (engine == null || who == null || !SpatialCollapseLive(engine)) return NoCap;
            return 5 - Monsters(who) - FieldCard(who);
        }

        /// <summary>How many more monsters the caps let <paramref name="who"/> place now (≤ 0: none).</summary>
        public static int MonsterRoom(DuelEngine engine, DuelistState who, ICollection<CardInstance> leaving = null) =>
            MonsterCap(engine, who, leaving) - Monsters(who, leaving);

        /// <summary>How many more Spell &amp; Trap Cards the caps let <paramref name="who"/> place now (≤ 0: none).</summary>
        public static int SpellTrapRoom(DuelEngine engine, DuelistState who) => SpellTrapCap(engine, who) - SpellTraps(who);

        /// <summary>DuelEngine.FirstEmpty: <paramref name="zones"/> is a player's Monster or Spell &amp; Trap row whose cap is reached.</summary>
        public static bool CapReached(DuelEngine engine, FieldZone[] zones)
        {
            if (engine == null || zones == null) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                if (ReferenceEquals(zones, side.MonsterZones)) return MonsterRoom(engine, side) <= 0;
                if (ReferenceEquals(zones, side.SpellTrapZones)) return SpellTrapRoom(engine, side) <= 0;
            }

            return false;
        }

        /// <summary>Usable Monster Zones of <paramref name="who"/>: free zones, at most the caps' room.</summary>
        public static int UsableMonsterZones(DuelistState who)
        {
            var free = who?.MonsterZones?.Count(z => z != null && z.IsEmpty) ?? 0;
            var engine = who?.Engine;
            return engine == null ? free : Math.Max(0, Math.Min(free, MonsterRoom(engine, who)));
        }

        /// <summary>Usable Spell &amp; Trap Zones of <paramref name="who"/>: free zones, at most the caps' room.</summary>
        public static int UsableSpellTrapZones(DuelistState who)
        {
            var free = who?.SpellTrapZones?.Count(z => z != null && z.IsEmpty) ?? 0;
            var engine = who?.Engine;
            return engine == null ? free : Math.Max(0, Math.Min(free, SpellTrapRoom(engine, who)));
        }

        /// <summary>
        /// Spatial Collapse (Lua e5 / e6): no Field Spell is activated into an empty Field Zone while its player already has 5
        /// or more cards on the field (replacing a Field Spell keeps the count).
        /// </summary>
        public static bool FieldSpellBlocked(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = null;
            if (engine == null || who == null || FieldCard(who) > 0 || !SpatialCollapseLive(engine)) return false;
            if (FieldCards(who) <= 4) return false;
            reason = "You cannot have more than 5 cards on the field (Spatial Collapse).";
            return true;
        }

        /// <summary>
        /// Spatial Collapse (Lua e5): no Pendulum Scale is activated while its player already has 5 or more cards on the
        /// field (DuelEngine.TryPlacePendulum).
        /// </summary>
        public static bool PendulumScaleBlocked(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = null;
            if (engine == null || who == null || !SpatialCollapseLive(engine) || FieldCards(who) <= 4) return false;
            reason = "You cannot have more than 5 cards on the field (Spatial Collapse).";
            return true;
        }

        /// <summary>Activation condition <see cref="BothPlayersFiveOrFewerFieldCards"/>.</summary>
        public static bool BothFiveOrFewer(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only if both players each have 5 or fewer cards on the field.";
            var opp = engine?.OpponentOf(who);
            if (who == null || opp == null || FieldCards(who) > 5 || FieldCards(opp) > 5) return false;
            reason = "OK";
            return true;
        }

        /// <summary>
        /// Spatial Collapse's adjust (Lua EVENT_ADJUST, s.adjustop), from DuelEngine.Notify once nothing resolves: a player
        /// with more than 5 cards on the field sends the excess to the GY (the controller of the Spatial Collapse chooses
        /// first, then the opponent; a human picks through the card sheet, the AI keeps its strongest cards); the chosen
        /// cards go together (one event). Not in the Damage Step before damage calculation.
        /// </summary>
        public static void Adjust(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || engine.Player == null || engine.Opponent == null) return;
            var st = s_state.GetValue(engine, _ => new State());
            if (st.Adjusting && ReferenceEquals(st.AdjustingIn, engine.Player)) return;
            var spatial = CapCards(engine).FirstOrDefault(x => x.action == EffectActionKind.EachPlayerFieldCardsCappedAtFive);
            if (spatial.card == null) return;
            // Lua: not in the Damage Step before damage calculation, nor during it.
            if (engine.BattleStep == BattleStep.DamageStep && engine.DamageSubStep != DamageSubStep.AfterDamageCalculation &&
                engine.DamageSubStep != DamageSubStep.End)
                return;
            var first = spatial.holder;
            var order = new[] { first, engine.OpponentOf(first) };
            if (order.All(p => FieldCards(p) <= 5)) return;
            st.Adjusting = true;
            st.AdjustingIn = engine.Player;
            var chosen = new List<(DuelistState who, CardInstance card)>();
            PickExcess(engine, spatial.card, order, 0, chosen, () =>
            {
                st.Adjusting = false;
                if (chosen.Count == 0) return;
                engine.Log($"{spatial.card.Name}: the cards above 5 are sent to the GY.");
                FlipMassEffectsRuntime.Simultaneously(engine, () =>
                {
                    foreach (var (who, card) in chosen)
                        if (engine.ControllerOf(card) == who)
                            engine.SendCardToGrave(who, card, spatial.card);
                });
                engine.NotifyPublic();
            });
        }

        static List<CardInstance> OnField(DuelistState who)
        {
            var list = new List<CardInstance>();
            list.AddRange(who.MonstersOnField().Where(m => m != null));
            list.AddRange(who.SpellTrapZones.Where(z => z?.Occupant != null).Select(z => z.Occupant));
            if (who.PendulumZones != null)
                list.AddRange(who.PendulumZones.Where(z => z?.Occupant != null).Select(z => z.Occupant));
            if (who.FieldSpellZone?.Occupant != null) list.Add(who.FieldSpellZone.Occupant);
            return list;
        }

        /// <summary>
        /// The AI sends its least useful card: never the Spatial Collapse itself while another card can go (v178 review),
        /// then Spell / Trap Cards before monsters, then the lowest ATK.
        /// </summary>
        static CardInstance AiExcess(List<CardInstance> list, CardInstance source) =>
            list.OrderBy(c => ReferenceEquals(c, source) ? 1 : 0)
                .ThenBy(c => c.Def != null && c.Def.IsMonster ? 1 : 0)
                .ThenBy(c => c.Def != null && c.Def.IsMonster ? c.CurrentAtk : 0)
                .FirstOrDefault();

        static void PickExcess(DuelEngine engine, CardInstance source, DuelistState[] order, int i,
            List<(DuelistState who, CardInstance card)> chosen, Action done)
        {
            while (i < order.Length)
            {
                var who = order[i];
                var left = OnField(who).Where(c => !chosen.Exists(x => ReferenceEquals(x.card, c))).ToList();
                if (who != null && left.Count > 5)
                {
                    var whoNow = who;
                    var iNow = i;
                    ContinuousTrapsAndSpiritSupportRuntime.AskCardPick(engine, who, source, new CardPickRequest
                    {
                        Candidates = () => OnField(whoNow).Where(c => !chosen.Exists(x => ReferenceEquals(x.card, c))).ToList(),
                        Kind = EffectTargetKind.AnyCardOnField,
                        Prompt = $"{source.Name}: choose a card you control to send to the GY " +
                                 $"({left.Count - 5} more above 5).",
                        AiChoose = list => AiExcess(list, source),
                        Finish = picked =>
                        {
                            if (picked != null) chosen.Add((whoNow, picked));
                            else
                            {
                                // Nothing to choose any more: go on with the next player.
                                PickExcess(engine, source, order, iNow + 1, chosen, done);
                                return;
                            }

                            PickExcess(engine, source, order, iNow, chosen, done);
                        },
                        HoldsChain = false
                    });
                    return;
                }

                i++;
            }

            done();
        }
    }
}
