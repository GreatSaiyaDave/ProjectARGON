using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipModel batch runtime: the "a monster became equipped" event and its idle response window (Armored Glass),
    /// the turn-long negation of every Equip Card on the field and what that negation turns off (control Equips give
    /// their monster back), and switching an Equip Card to another correct target (Tailor of the Fickle). Reads
    /// compiled programs only — no card-id branches.
    /// <list type="bullet">
    /// <item><b>Equip event</b> (ygopro EVENT_EQUIP). <see cref="NoteEquipped"/> runs at every equip path:
    /// <c>TextEffectRuntime.EquipCardTo</c> (Equip Spells, Unions, absorbs) and Dark Necrofear through
    /// <c>EquipFamilyRuntime.NoteEquipped</c>, an Equip Card moved by Collected Power / Tailor of the Fickle through
    /// <c>EquipFamilyRuntime.MoveEquip</c>, and Kiseitai becoming an Equip Card. Persistent Continuous Trap links
    /// (Call of the Haunted, Battle-Scarred) are not Equip Cards and record nothing. Once play is idle in a Main or
    /// Battle Phase, <c>DuelEngine.OfferMonsterEquippedWindow</c> offers each duelist (turn player first) the
    /// <see cref="ResponseTiming.MonsterEquipped"/> window; events from a Damage Step, an earlier phase or turn were
    /// missed and are dropped.</item>
    /// <item><b>Equip Card negation.</b> Armored Glass registers the <see cref="TurnNegationTrapsRuntime.EquipCard"/>
    /// family through the end of the turn (ygopro EFFECT_DISABLE on TYPE_EQUIP in both Spell &amp; Trap Zones,
    /// RESET_PHASE+PHASE_END). <see cref="IsEquipCard"/> decides membership per card (an Equip Spell / Equip Trap
    /// equipped to a monster, or a monster acting as an Equip Card), so Equip Cards equipped later this turn are negated
    /// too; <c>CardInstance.IsNegated</c> then turns off their stat changes, piercing, locks, Standby effects and
    /// Kiseitai's LP gain. <see cref="SyncNegatedControlEquips"/> gives a monster taken by a control Equip (Snatch
    /// Steal, Dark Necrofear) back while that Equip's effects are negated by Armored Glass or The Emperor's Holiday,
    /// and takes it again when the negation ends with the Equip still attached.</item>
    /// <item><b>Tailor of the Fickle</b> (<see cref="EffectActionKind.MoveEquipCardToAnotherCorrectTarget"/>): the
    /// clause targets the Equip Card (<see cref="SwitchCandidates"/>); the monster target is chosen with it, by the
    /// player right after the Equip Card pick (<see cref="TryInterceptSwitchPick"/>, the same pending, whatever path
    /// opened it: open state, a chain response or a response window) or by the AI's auto-pick
    /// (<see cref="AiPickSwitch"/>). Both are the Chain Link's targets; the pair is kept per activating card
    /// (<see cref="ClaimPlan"/>), so two links on one Equip Card keep their own monster targets. The AI chains it only
    /// when a switch helps it (<see cref="AiWorthSwitch"/>). <see cref="ResolveSwitch"/> applies Lua s.operation.</item>
    /// </list>
    /// </summary>
    public static class EquipModelRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Armored Glass, Lua EVENT_EQUIP): only in the
        /// <see cref="ResponseTiming.MonsterEquipped"/> window right after a monster was equipped with an Equip Card.
        /// </summary>
        public const string MonsterEquippedWithEquipCard = "MonsterEquippedWithEquipCard";

        sealed class EquipRecord
        {
            public CardInstance Equip;
            public CardInstance Host;
            public int Turn;
            public DuelPhase Phase;
            public bool InDamageStep;
        }

        /// <summary>The two targets of a Tailor of the Fickle activation: the Equip Card and the monster.</summary>
        sealed class SwitchPlan
        {
            public CardInstance Equip;
            public CardInstance Monster;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        sealed class State
        {
            public readonly List<EquipRecord> Records = new();
            public readonly HashSet<DuelistState> Offered = new();
            /// <summary>Control Equips whose monster went back while negated, with that monster.</summary>
            public readonly Dictionary<CardInstance, CardInstance> Suspended = new();
            /// <summary>
            /// Each Tailor of the Fickle activation's targets, keyed by the activating card (its own Chain Link), so two
            /// links on the same Equip Card each keep their own monster target (Lua CHAININFO_TARGET_CARDS).
            /// </summary>
            public readonly Dictionary<CardInstance, SwitchPlan> Plans = new();
            /// <summary>The pair just picked, before its activation is recorded or seated (<see cref="ClaimPlan"/>).</summary>
            public SwitchPlan Pick;
            public bool Syncing;
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        /// <summary>A human's Tailor pick in progress: 0 = the Equip Card, 1 = the monster, 2 = done.</summary>
        sealed class PickState
        {
            public int Stage;
            public CardInstance Equip;
            public CardInstance Monster;
            public EffectTargetKind Kind;
            public string Prompt;
        }

        static readonly ConditionalWeakTable<PendingActivation, PickState> s_picks = new();

        // ------------------------------------------------------------------ Equip Cards

        /// <summary>
        /// Lua TYPE_EQUIP for a card on the field: an Equip Spell, an Equip Trap (a Normal Trap that stays equipped to a
        /// monster) or a monster acting as an Equip Card, face-up and equipped to a monster. Persistent Continuous Trap
        /// links (Call of the Haunted, Spellbinding Circle, Battle-Scarred) are not Equip Cards. Its zone is not
        /// checked here.
        /// </summary>
        public static bool IsEquipCardShape(CardInstance card) =>
            card?.Def != null && card.FaceUp && card.EquippedTo != null &&
            (card.Def.IsEquipSpell || card.Def.IsMonster ||
             (card.Def.IsTrap && !card.Def.IsContinuousSpellOrTrap));

        /// <summary><see cref="IsEquipCardShape"/> and in a Spell &amp; Trap Zone (an absorbed monster sits in none).</summary>
        public static bool IsEquipCard(DuelEngine engine, CardInstance card)
        {
            if (engine == null || !IsEquipCardShape(card)) return false;
            foreach (var side in new[] { engine.Player, engine.Opponent })
                if (side != null && side.TryFindSpellTrap(card, out _))
                    return true;
            return false;
        }

        static List<CardInstance> EquipCardsOnField(DuelEngine engine)
        {
            var list = new List<CardInstance>();
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    if (IsEquipCardShape(st))
                        list.Add(st);
            }

            return list;
        }

        static bool TakesControlOfHost(CardInstance equip)
        {
            var prog = equip?.Def != null ? CompiledEffectCache.GetOrCompile(equip.Def) : null;
            return prog != null && prog.ClauseList.Exists(c => c != null && c.TakeControlOfTarget);
        }

        // ------------------------------------------------------------------ equip event + window

        /// <summary>
        /// A monster was just equipped with <paramref name="equip"/> (ygopro EVENT_EQUIP): remember it for the idle
        /// <see cref="ResponseTiming.MonsterEquipped"/> window. Links that are not Equip Cards record nothing.
        /// </summary>
        public static void NoteEquipped(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            if (engine == null || host == null || equip?.Def == null || equip.EquippedTo != host) return;
            if (!(equip.Def.IsEquipSpell || equip.Def.IsMonster ||
                  (equip.Def.IsTrap && !equip.Def.IsContinuousSpellOrTrap)))
                return;
            StateOf(engine).Records.Add(new EquipRecord
            {
                Equip = equip,
                Host = host,
                Turn = engine.TurnNumber,
                Phase = engine.Phase,
                InDamageStep = engine.BattleStep == BattleStep.DamageStep
            });
        }

        public static bool HasPending(DuelEngine engine) =>
            engine != null && s_state.TryGetValue(engine, out var st) && st.Records.Count > 0;

        /// <summary>The window round is over (or the duel ended): every recorded equip is gone.</summary>
        public static void ClearAll(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.Records.Clear();
            st.Offered.Clear();
        }

        /// <summary>
        /// DuelEngine.OfferMonsterEquippedWindow: <paramref name="victim"/> may now get the window once for the equips
        /// recorded since the last round. Equips of an earlier turn or phase, or made in a Damage Step (a Normal Trap
        /// cannot be activated there), were missed and are dropped.
        /// </summary>
        public static bool BeginWindow(DuelEngine engine, DuelistState victim)
        {
            if (engine == null || victim == null || !s_state.TryGetValue(engine, out var st)) return false;
            st.Records.RemoveAll(r =>
                r.Turn != engine.TurnNumber || r.Phase != engine.Phase || r.InDamageStep);
            if (st.Records.Count == 0 || st.Offered.Contains(victim)) return false;
            st.Offered.Add(victim);
            return true;
        }

        /// <summary>Activation condition <see cref="MonsterEquippedWithEquipCard"/>: <paramref name="who"/>'s window is open.</summary>
        public static bool EquipWindowOpen(DuelEngine engine, DuelistState who, out string reason)
        {
            reason = "Only when a monster is equipped with an Equip Card.";
            var pr = engine?.PendingResponse;
            return pr != null && who != null && pr.Timing == ResponseTiming.MonsterEquipped && pr.Responder == who &&
                   s_state.TryGetValue(engine, out var st) && st.Records.Count > 0;
        }

        /// <summary>The program is a Trap whose activation condition is this batch's equip event (Armored Glass).</summary>
        public static bool AnswersEquipWindow(CompiledCardProgram prog) =>
            prog != null && prog.FullyCompiled && prog.ClauseList.Exists(c =>
                c != null && c.Timing == EffectTiming.Activate &&
                c.ActivationCondition == MonsterEquippedWithEquipCard);

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond): Armored Glass when the opponent just equipped a monster and the
        /// opponent has more Equip Cards on the field whose effects still apply than the AI does.
        /// </summary>
        public static bool AiWantsToAnswer(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || card?.Def == null) return false;
            if (!AnswersEquipWindow(CompiledEffectCache.GetOrCompile(card.Def))) return false;
            if (!EquipWindowOpen(engine, ai, out _) || !s_state.TryGetValue(engine, out var st)) return false;
            var opp = engine.OpponentOf(ai);
            if (opp == null || !st.Records.Exists(r => engine.ControllerOf(r.Equip) == opp)) return false;
            int Live(DuelistState who) => EquipCardsOnField(engine).Count(e =>
                engine.ControllerOf(e) == who && !ContinuousProtections.EquipCardEffectsAreNegated(engine, e));
            return Live(opp) > Live(ai);
        }

        // ------------------------------------------------------------------ control Equips while negated

        /// <summary>
        /// Negation by Armored Glass (the Equip Card family, this turn) or The Emperor's Holiday. Imperial Order / Spell
        /// Canceller are <c>ContinuousNegationRuntime</c>'s own (it moves control for those).
        /// </summary>
        static bool NegatedByEquipNegation(DuelEngine engine, CardInstance eq) =>
            TurnNegationTrapsRuntime.IsNegatedByFamilyNow(engine, eq) ||
            ContinuousProtections.NegateAllEquipCardEffectsOnField(engine);

        /// <summary>
        /// Board refresh (FieldSpellEffects.RefreshBoard), Armored Glass's resolution and the start of each turn:
        /// "take control of the equipped monster" is a continuous Equip effect (Lua EFFECT_SET_CONTROL). While every
        /// control Equip attached to a monster its controller holds is negated (Armored Glass, The Emperor's Holiday),
        /// control returns to the other player (to the GY when they have no free Monster Zone); once the negation
        /// ends and the Equip is still equipped to that monster, its controller takes control again (unless Remove
        /// Brainwashing applies).
        /// </summary>
        public static void SyncNegatedControlEquips(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return;
            var st = StateOf(engine);
            if (st.Syncing) return;
            st.Syncing = true;
            try
            {
                foreach (var side in new[] { engine.Player, engine.Opponent })
                {
                    foreach (var m in side.MonstersOnField().ToList())
                    {
                        if (m == null || !m.TakenByEquipControl || engine.ControllerOf(m) != side) continue;
                        CardInstance negated = null;
                        var applies = false;
                        foreach (var eq in m.Equips)
                        {
                            if (eq?.Def == null || eq.EquippedTo != m || !eq.FaceUp || !TakesControlOfHost(eq)) continue;
                            if (engine.ControllerOf(eq) != side || !side.TryFindSpellTrap(eq, out _)) continue;
                            if (NegatedByEquipNegation(engine, eq)) negated ??= eq;
                            else applies = true;
                        }

                        if (applies || negated == null) continue;
                        var other = m.ControlOwner != null && m.ControlOwner != side ? m.ControlOwner : engine.OpponentOf(side);
                        if (other == null) continue;
                        st.Suspended[negated] = m;
                        m.TakenByEquipControl = false;
                        if (engine.TryTakeControl(other, m))
                            engine.Log($"{negated.Name} is negated: control of {m.Name} returns to {other.Name}.");
                        else
                        {
                            engine.Log($"{negated.Name} is negated: {m.Name} cannot return (no Monster Zone) and is sent to the GY.");
                            engine.SendCardToGrave(side, m);
                        }
                    }
                }

                foreach (var kv in st.Suspended.ToList())
                {
                    var eq = kv.Key;
                    var host = kv.Value;
                    var eqController = engine.ControllerOf(eq);
                    var hostController = engine.ControllerOf(host);
                    if (eq.EquippedTo != host || !eq.FaceUp || eqController == null ||
                        !eqController.TryFindSpellTrap(eq, out _) || hostController == null ||
                        !hostController.TryFindMonster(host, out _) || !host.FaceUp)
                    {
                        st.Suspended.Remove(eq);
                        continue;
                    }

                    if (eq.IsNegated || NegatedByEquipNegation(engine, eq) ||
                        TextEffectRuntime.ReturnControlToOwnersApplies(engine))
                        continue;
                    st.Suspended.Remove(eq);
                    host.TakenByEquipControl = true;
                    if (hostController == eqController) continue;
                    if (engine.TryTakeControl(eqController, host))
                        engine.Log($"{eq.Name} is no longer negated: {eqController.Name} takes control of {host.Name} again.");
                    else
                    {
                        host.TakenByEquipControl = false;
                        engine.Log($"{eq.Name}: take control failed — no Monster Zone.");
                    }
                }
            }
            finally
            {
                st.Syncing = false;
            }
        }

        /// <summary>
        /// A control Equip was just equipped while its effects are negated (Dark Necrofear under Armored Glass): it takes
        /// control once the negation ends, as <see cref="SyncNegatedControlEquips"/> does for a monster it gave back.
        /// </summary>
        public static void SuspendControl(DuelEngine engine, CardInstance equip, CardInstance host)
        {
            if (engine == null || equip == null || host == null) return;
            StateOf(engine).Suspended[equip] = host;
        }

        // ------------------------------------------------------------------ Tailor of the Fickle

        /// <summary>
        /// Lua s.ecfilter's card part: a face-up Equip Card equipped to a monster in a Spell &amp; Trap Zone that is not
        /// a monster card, unless it is a Union (a monster that became an Equip Card by another effect — Kiseitai, Dark
        /// Necrofear — is not switched).
        /// </summary>
        static bool IsSwitchableEquip(DuelEngine engine, CardInstance ec)
        {
            if (!IsEquipCard(engine, ec)) return false;
            if (!ec.Def.IsMonster) return true;
            var prog = CompiledEffectCache.GetOrCompile(ec.Def);
            return ec.AbsorbedBy == null && prog != null && prog.ClauseList.Exists(c =>
                c != null && c.Action == EffectActionKind.UnequipThisSpecialSummon);
        }

        /// <summary>
        /// Lua s.tcfilter: face-up monsters on either field, other than <paramref name="ec"/>'s current monster, that
        /// are a correct target for it and can be targeted by <paramref name="source"/>.
        /// </summary>
        public static List<CardInstance> CorrectOtherTargets(DuelEngine engine, CardInstance ec, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || ec == null) return list;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || !m.FaceUp || ReferenceEquals(m, ec.EquippedTo)) continue;
                    if (engine.IsDragonTargetProtected(m)) continue;
                    if (source != null && ContinuousProtections.TargetedEffectBlocked(engine, m, source)) continue;
                    if (EquipFamilyRuntime.IsCorrectEquipTarget(engine, ec, m))
                        list.Add(m);
                }
            }

            return list;
        }

        /// <summary>
        /// CollectTargets for <see cref="EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget"/>: the Equip Cards
        /// Tailor of the Fickle can target (Lua s.ecfilter — another correct face-up target exists).
        /// </summary>
        public static void SwitchCandidates(DuelEngine engine, CardInstance source, List<CardInstance> into)
        {
            if (engine == null || into == null) return;
            foreach (var side in new[] { engine.Player, engine.Opponent })
            {
                if (side == null) continue;
                foreach (var st in side.SpellTrapsOnField())
                    if (IsSwitchableEquip(engine, st) && CorrectOtherTargets(engine, st, source).Count > 0)
                        into.Add(st);
            }
        }

        static bool IsSwitchClause(EffectClause c) =>
            c != null && c.Action == EffectActionKind.MoveEquipCardToAnotherCorrectTarget;

        static bool IsSwitchCard(CardInstance card)
        {
            var prog = card?.Def != null ? CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog != null && prog.FullyCompiled && prog.ClauseList.Exists(IsSwitchClause);
        }

        /// <summary>The pair just picked; the activation that made it claims it next (<see cref="ClaimPlan"/>).</summary>
        static void SetPick(DuelEngine engine, CardInstance ec, CardInstance monster)
        {
            if (engine == null) return;
            StateOf(engine).Pick = ec == null || monster == null
                ? null
                : new SwitchPlan
                {
                    Equip = ec,
                    Monster = monster,
                    EntryStamp = monster.FieldEntryStamp,
                    FaceDownStamp = monster.FaceDownStamp
                };
        }

        /// <summary>
        /// The activation of <paramref name="source"/> got its Equip Card target <paramref name="ec"/> (its Chain Link or
        /// recorded activation exists now): the pair just picked becomes that activation's own plan. Returns its monster
        /// target, or null.
        /// </summary>
        public static CardInstance ClaimPlan(DuelEngine engine, CardInstance source, CardInstance ec)
        {
            if (engine == null || source == null || ec == null || !s_state.TryGetValue(engine, out var st)) return null;
            if (st.Pick != null && ReferenceEquals(st.Pick.Equip, ec))
            {
                st.Plans[source] = st.Pick;
                st.Pick = null;
            }

            return st.Plans.TryGetValue(source, out var plan) && ReferenceEquals(plan.Equip, ec) ? plan.Monster : null;
        }

        /// <summary>The monster target of <paramref name="source"/>'s Tailor of the Fickle activation, or null.</summary>
        public static CardInstance PlannedMonster(DuelEngine engine, CardInstance source) =>
            engine != null && source != null && s_state.TryGetValue(engine, out var st) &&
            st.Plans.TryGetValue(source, out var plan)
                ? plan.Monster
                : null;

        /// <summary>
        /// The AI's score for moving <paramref name="ec"/> to <paramref name="tc"/>: a control Equip (Snatch Steal) the AI
        /// holds should take the opponent's strongest monster; one the opponent holds should move to the AI's weakest
        /// monster (the stronger one comes back). Any other Equip Card helps the monster it is on: moving it from an
        /// opponent's monster to the AI's strongest one scores highest. Positive = the switch helps the AI.
        /// </summary>
        static long SwitchScore(DuelEngine engine, DuelistState who, CardInstance ec, CardInstance tc)
        {
            var host = ec.EquippedTo;
            var hostAtk = host?.CurrentAtk ?? 0;
            var hostMine = host != null && engine.ControllerOf(host) == who;
            var tcMine = engine.ControllerOf(tc) == who;
            if (TakesControlOfHost(ec))
                return engine.ControllerOf(ec) == who ? tc.CurrentAtk - hostAtk : hostAtk - tc.CurrentAtk;
            return ((tcMine ? 1 : -1) - (hostMine ? 1 : -1)) * 100000L + (tcMine ? tc.CurrentAtk : -tc.CurrentAtk);
        }

        static long BestSwitch(DuelEngine engine, DuelistState who, IEnumerable<CardInstance> equips,
            CardInstance source, out CardInstance bestEc, out CardInstance bestTc)
        {
            bestEc = null;
            bestTc = null;
            var best = long.MinValue;
            foreach (var ec in equips)
            {
                if (ec == null) continue;
                foreach (var tc in CorrectOtherTargets(engine, ec, source))
                {
                    var score = SwitchScore(engine, who, ec, tc);
                    if (score <= best) continue;
                    best = score;
                    bestEc = ec;
                    bestTc = tc;
                }
            }

            return best;
        }

        /// <summary>
        /// AI (SpellTrapEffects.AiAutoRespond, "use it or lose it"): true unless <paramref name="card"/> is a Tailor of
        /// the Fickle whose every switch would hurt the AI or do nothing for it (no pair scores above 0).
        /// </summary>
        public static bool AiWorthSwitch(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            if (engine == null || ai == null || !IsSwitchCard(card)) return true;
            var equips = new List<CardInstance>();
            SwitchCandidates(engine, card, equips);
            return BestSwitch(engine, ai, equips, card, out _, out _) > 0;
        }

        /// <summary>
        /// AI auto-pick (TextEffectRuntime.AutoPick): the Equip Card and monster pair that helps the AI most. A control
        /// Equip (Snatch Steal) the AI holds goes to the opponent's strongest monster; one the opponent holds goes to the
        /// AI's weakest monster (the stronger one comes back). Any other Equip Card is treated as helping the monster it
        /// is on: moved from an opponent's monster to the AI's strongest one. The monster is remembered for the Equip
        /// Card (<see cref="PlannedMonster"/>).
        /// </summary>
        public static CardInstance AiPickSwitch(DuelEngine engine, DuelistState who, List<CardInstance> equips)
        {
            if (engine == null || equips == null || equips.Count == 0) return null;
            BestSwitch(engine, who, equips, null, out var bestEc, out var bestTc);
            bestEc ??= equips[0];
            bestTc ??= CorrectOtherTargets(engine, bestEc, null).FirstOrDefault();
            SetPick(engine, bestEc, bestTc);
            return bestEc;
        }

        /// <summary>
        /// SpellTrapEffects.TryResolveWithTarget (first, for every pick): a player's Tailor of the Fickle pick. The
        /// first pick is the Equip Card: the same pending then offers the monsters that are a correct target for it.
        /// The second pick is the monster: it is remembered, and the ordinary flow continues with the Equip Card as the
        /// clause's target (the activation is recorded or seated exactly as for any single-target pick).
        /// </summary>
        public static bool TryInterceptSwitchPick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p?.Card == null || target == null || !IsSwitchCard(p.Card)) return false;
            if (p.AwaitingIgnitionCost || p.AwaitingDeclaration || p.AwaitingMultiDiscard) return false;
            var st = s_picks.GetValue(p, _ => new PickState());
            if (st.Stage == 0)
            {
                var monsters = CorrectOtherTargets(engine, target, p.Card);
                if (monsters.Count == 0)
                {
                    engine.Log($"{target.Name} has no other correct target.");
                    return true;
                }

                st.Stage = 1;
                st.Equip = target;
                st.Kind = p.TargetKind;
                st.Prompt = p.PromptOverride;
                p.LegalTargets.Clear();
                p.LegalTargets.AddRange(monsters);
                p.TargetKind = EffectTargetKind.FaceUpMonsterOnField;
                p.PromptOverride = $"choose the monster to equip {target.Name} to.";
                engine.Log($"{p.Card.Name}: {target.Name} targeted — {p.Prompt}");
                engine.NotifyPublic();
                ok = true;
                return true;
            }

            if (st.Stage != 1) return false;
            st.Stage = 2;
            st.Monster = target;
            SetPick(engine, st.Equip, target);
            p.LegalTargets.Clear();
            p.LegalTargets.Add(st.Equip);
            p.TargetKind = st.Kind;
            p.PromptOverride = st.Prompt;
            ok = SpellTrapEffects.TryResolveWithTarget(engine, st.Equip);
            return true;
        }

        /// <summary>
        /// A Tailor of the Fickle Chain Link got its Equip Card target (a chain response or a response seat): its
        /// monster target is a target of the link too.
        /// </summary>
        public static void AddMonsterTargetToLink(DuelEngine engine, ChainLink link)
        {
            if (link?.Card == null || link.Target == null || !IsSwitchCard(link.Card)) return;
            var tc = ClaimPlan(engine, link.Card, link.Target);
            if (tc != null && !link.Targets.Contains(tc))
                link.Targets.Add(tc);
        }

        /// <summary>
        /// Lua s.operation: if the Equip Card target is still face-up on the field — and the monster target still a
        /// face-up monster on the field (same stay, not turned face-down) — the Equip Card is equipped to it when it is
        /// a correct target (a control Equip moves its control with it); if the monster target is gone, the Equip Card
        /// is sent to the GY. An Equip Card that left the field is not affected.
        /// </summary>
        public static void ResolveSwitch(DuelEngine engine, DuelistState who, CardInstance source, CardInstance ec)
        {
            if (engine == null || ec == null) return;
            var st = StateOf(engine);
            // This activation's own pair (keyed by the activating card): another Tailor link on the same Equip Card
            // keeps its own monster target.
            SwitchPlan plan = null;
            if (source != null && st.Plans.TryGetValue(source, out var own))
            {
                st.Plans.Remove(source);
                if (ReferenceEquals(own.Equip, ec)) plan = own;
            }

            var holder = engine.ControllerOf(ec);
            if (holder == null || !holder.TryFindSpellTrap(ec, out _) || !ec.FaceUp)
            {
                engine.Log($"{source?.Name ?? "Effect"}: {ec.Name} is no longer on the field — no effect.");
                return;
            }

            var tc = plan?.Monster;
            var tcController = tc != null ? engine.ControllerOf(tc) : null;
            var related = tcController != null && tcController.TryFindMonster(tc, out _) && tc.FaceUp &&
                          tc.FieldEntryStamp == plan.EntryStamp && tc.FaceDownStamp == plan.FaceDownStamp;
            if (!related)
            {
                engine.Log($"{source?.Name ?? "Effect"}: the monster target is gone — {ec.Name} is sent to the GY.");
                engine.SendCardToGrave(holder, ec);
                engine.NotifyPublic();
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, tc, source) ||
                !EquipFamilyRuntime.IsCorrectEquipTarget(engine, ec, tc))
            {
                engine.Log($"{source?.Name ?? "Effect"}: {ec.Name} cannot be equipped to {tc.Name} — it stays.");
                return;
            }

            EquipFamilyRuntime.MoveEquip(engine, ec, tc);
            engine.NotifyPublic();
        }
    }
}
