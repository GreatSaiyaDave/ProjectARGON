using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// EquipAndSpellProtection batch (v161): destruction protections of Equip Cards, equipped monsters and Spells, and the
    /// "destroyed by a card effect while equipped" triggers of Equip Cards. Atoms: <see cref="EquipAndSpellProtectionAtoms"/>.
    /// <list type="bullet">
    /// <item>Effect destruction of a Spell/Trap (<see cref="TextEffectRuntime.DestroyCard"/> and the legacy MST / Heavy Storm
    /// paths) asks <see cref="PreventsSpellTrapEffectDestruction"/>: Fusion Sword Murasame Blade cannot be destroyed by
    /// card effects while equipped; a Magic Reflector counter is removed instead (<see cref="ReplacesDestruction"/>,
    /// also asked by the "pay or destroy this card" upkeeps and the self-destructions that are not by rule).</item>
    /// <item>Heart of Clear Water: the equipped monster is not destroyed by battle
    /// (<see cref="EquipPreventsBattleDestruction"/>) or by an effect that targets it — the resolving clause's target, kept
    /// by <see cref="EnterTargetedClause"/> while it applies (Lua CHAININFO_TARGET_CARDS of the resolving link), and the
    /// monster of a Call of the Haunted-style link card (Lua rc:IsHasCardTarget(tc)). It destroys itself while that
    /// monster's ATK is 1300 or more (<see cref="Refresh"/>, EFFECT_SELF_DESTROY).</item>
    /// <item>Smoke Grenade of the Thief / Blast with Chain: a Spell/Trap destroyed by a card effect while equipped is
    /// recorded (<see cref="NoteEffectDestroy"/>); its mandatory trigger resolves when it reaches the GY
    /// (<see cref="TryResolveDestroyedWhileEquipped"/>). A human controller picks through a
    /// <see cref="PendingActivation"/> (<see cref="TryResolvePick"/>; Cancel picks automatically). An Equip Card sent to
    /// the GY because its monster left the field is sent by rule and records nothing.</item>
    /// </list>
    /// </summary>
    public static class EquipAndSpellProtectionRuntime
    {
        sealed class State
        {
            /// <summary>Magic Reflector counters on face-up Spells (Lua counter 0x102a).</summary>
            public readonly Dictionary<CardInstance, int> Counters = new();
            /// <summary>Spells/Traps destroyed by a card effect while equipped: the turn of the destruction.</summary>
            public readonly Dictionary<CardInstance, int> DestroyedWhileEquipped = new();
        }

        static readonly ConditionalWeakTable<DuelEngine, State> s_state = new();

        static State Of(DuelEngine engine) => s_state.GetValue(engine, _ => new State());

        sealed class PickContext
        {
            public DuelistState Who;
            public CardInstance Card;
            public EffectClause Clause;
        }

        static readonly ConditionalWeakTable<PendingActivation, PickContext> s_picks = new();

        // ------------------------------------------------------------------ targeting scope (Heart of Clear Water)

        sealed class Scope : IDisposable
        {
            public CardInstance Source;
            public CardInstance Target;

            public void Dispose() => s_scopes?.Remove(this);
        }

        sealed class NoScope : IDisposable
        {
            public void Dispose() { }
        }

        static readonly IDisposable s_none = new NoScope();

        [ThreadStatic] static List<Scope> s_scopes;

        /// <summary>
        /// While the returned scope is open, <paramref name="target"/> is a target of the effect of
        /// <paramref name="source"/> that is applying (SpellTrapEffects.ResolveTargeted, a link card destroying its monster).
        /// </summary>
        public static IDisposable EnterTargetedResolution(CardInstance source, CardInstance target)
        {
            if (target == null) return s_none;
            var s = new Scope { Source = source, Target = target };
            (s_scopes ??= new List<Scope>()).Add(s);
            return s;
        }

        /// <summary>
        /// TextEffectRuntime.ApplyClause: a clause that targets its chosen card (<see cref="EffectClause.IsPsctTarget"/>,
        /// the engine's targeting flag; a choice that does not target — Fissure, Smashing Ground — is not one). The target
        /// need not be picked: an implied target the runtime passes in ("Target the attacking monster" — Sakuretsu Armor;
        /// "Target that monster") is in CHAININFO_TARGET_CARDS as well.
        /// </summary>
        internal static IDisposable EnterTargetedClause(EffectClause clause, CardInstance source, CardInstance chosenTarget) =>
            clause != null && chosenTarget != null && clause.IsPsctTarget && !clause.ChoiceDoesNotTarget
                ? EnterTargetedResolution(source, chosenTarget)
                : s_none;

        /// <summary>SpellTrapEffects.ResolveTargeted: the legacy pick is a target when the card's activation targets.</summary>
        public static IDisposable EnterLegacyTargetedResolution(CardInstance source, CardInstance target)
        {
            if (source?.Def == null || target == null) return s_none;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            var targets = prog?.ClausesFor(EffectTiming.Activate)
                .Exists(c => c != null && c.IsPsctTarget && !c.ChoiceDoesNotTarget) == true;
            return targets ? EnterTargetedResolution(source, target) : s_none;
        }

        static bool TargetedNow(CardInstance card, CardInstance byEffect)
        {
            if (s_scopes == null || card == null) return false;
            for (var i = s_scopes.Count - 1; i >= 0; i--)
            {
                var s = s_scopes[i];
                if (!ReferenceEquals(s.Target, card)) continue;
                if (byEffect == null || s.Source == null || ReferenceEquals(byEffect, s.Source)) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ helpers

        static EffectClause ContinuousClause(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) return null;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return null;
            return prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp)
                .FirstOrDefault(c => c != null && c.Action == action);
        }

        /// <summary>A face-up Equip Card on the field, equipped to <paramref name="host"/>, whose effects apply.</summary>
        static bool EquipEffectsLive(DuelEngine engine, CardInstance eq, CardInstance host)
        {
            if (engine == null || eq == null || !eq.FaceUp || eq.IsNegated) return false;
            if (host != null && !ReferenceEquals(eq.EquippedTo, host)) return false;
            if (eq.EquippedTo == null || engine.ControllerOf(eq) == null) return false;
            return !ContinuousProtections.EquipCardEffectsAreNegated(engine, eq);
        }

        static CardInstance HeartOn(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster?.Equips == null || !monster.FaceUp) return null;
            foreach (var eq in monster.Equips)
                if (EquipEffectsLive(engine, eq, monster) &&
                    ContinuousClause(eq, EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects) != null)
                    return eq;
            return null;
        }

        static bool OnFieldFaceUp(DuelEngine engine, CardInstance card) =>
            card != null && card.FaceUp && engine.ControllerOf(card) != null;

        // ------------------------------------------------------------------ Heart of Clear Water

        /// <summary>Damage calculation: the equipped monster is not destroyed by battle (battle damage still applies).</summary>
        public static bool EquipPreventsBattleDestruction(DuelEngine engine, CardInstance monster) =>
            HeartOn(engine, monster) != null;

        /// <summary>
        /// DuelEngine.DestroyMonster (not by battle): the monster is a target of the effect destroying it and is equipped
        /// with Heart of Clear Water — it is not destroyed.
        /// </summary>
        public static bool PreventsTargetedMonsterDestruction(DuelEngine engine, CardInstance monster, CardInstance byEffect)
        {
            if (!TargetedNow(monster, byEffect)) return false;
            var heart = HeartOn(engine, monster);
            if (heart == null) return false;
            engine.Log($"{monster.Name} is not destroyed by an effect that targets it ({heart.Name}).");
            return true;
        }

        // ------------------------------------------------------------------ Murasame Blade / Magic Reflector

        /// <summary>
        /// A card effect would destroy this Spell/Trap: Fusion Sword Murasame Blade (equipped, its effects not negated)
        /// is not destroyed; otherwise a Magic Reflector counter on it is removed instead.
        /// </summary>
        public static bool PreventsSpellTrapEffectDestruction(DuelEngine engine, CardInstance card, CardInstance source)
        {
            if (engine == null || card?.Def == null || card.Def.IsMonster) return false;
            // ContinuousLocksAndIgnitions batch (v163): Timidity — Set Spell and Trap Cards cannot be destroyed by card effects.
            if (ContinuousLocksAndIgnitionsRuntime.SetCardProtected(engine, card)) return true;
            if (card.EquippedTo != null && EquipEffectsLive(engine, card, null) &&
                ContinuousClause(card, EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped) != null)
            {
                engine.Log($"{card.Name} cannot be destroyed by card effects.");
                return true;
            }

            return ReplacesDestruction(engine, card);
        }

        /// <summary>
        /// Magic Reflector's EFFECT_DESTROY_REPLACE: this face-up Spell would be destroyed (not by rule) — remove 1 of its
        /// counters instead. Asked by every destruction that is not by rule: card effects, "pay or destroy this card"
        /// upkeeps (REASON_COST), self-destructions (EFFECT_SELF_DESTROY).
        /// </summary>
        public static bool ReplacesDestruction(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return false;
            if (!st.Counters.TryGetValue(card, out var n) || n <= 0) return false;
            if (!OnFieldFaceUp(engine, card))
            {
                st.Counters.Remove(card);
                return false;
            }

            if (n <= 1) st.Counters.Remove(card);
            else st.Counters[card] = n - 1;
            engine.Log($"{card.Name} is not destroyed: its Magic Reflector counter is removed instead.");
            return true;
        }

        /// <summary>Magic Reflector counters on <paramref name="card"/> (0 when it has none or left the field).</summary>
        public static int ReflectorCounters(DuelEngine engine, CardInstance card) =>
            engine != null && card != null && s_state.TryGetValue(engine, out var st) &&
            st.Counters.TryGetValue(card, out var n) && OnFieldFaceUp(engine, card)
                ? n
                : 0;

        /// <summary>Magic Reflector's targets: face-up Spells <paramref name="who"/> controls, except the activating card.</summary>
        public static void CollectControllerFaceUpSpells(DuelistState who, CardInstance except, List<CardInstance> into)
        {
            if (who == null || into == null) return;
            foreach (var st in who.SpellTrapsOnField())
                if (st?.Def != null && st.FaceUp && st.Def.IsSpell && !ReferenceEquals(st, except))
                    into.Add(st);
            var field = who.FieldSpellZone?.Occupant;
            if (field?.Def != null && field.FaceUp && field.Def.IsSpell && !ReferenceEquals(field, except) &&
                !into.Contains(field))
                into.Add(field);
        }

        /// <summary>Magic Reflector resolves: the target, still a face-up Spell on the field, gets 1 counter.</summary>
        public static void PlaceReflectorCounter(DuelEngine engine, DuelistState who, CardInstance source,
            CardInstance target)
        {
            if (engine == null || target?.Def == null) return;
            if (!OnFieldFaceUp(engine, target) || !target.Def.IsSpell)
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up Spell on the field — no counter.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log($"{target.Name} is protected at resolution.");
                return;
            }

            var st = Of(engine);
            st.Counters.TryGetValue(target, out var n);
            st.Counters[target] = n + 1;
            engine.Log($"{source?.Name}: 1 counter is placed on {target.Name} ({n + 1}).");
        }

        // ------------------------------------------------------------------ destroyed while equipped

        /// <summary>
        /// TextEffectRuntime.DestroyCard (and the legacy MST / Heavy Storm paths): a Spell/Trap is about to be destroyed by a
        /// card effect. When it is equipped to a monster, its "destroyed by a card effect while equipped" trigger may fire
        /// once it reaches the GY.
        /// </summary>
        public static void NoteEffectDestroy(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card?.Def == null || card.Def.IsMonster) return;
            var st = Of(engine);
            st.DestroyedWhileEquipped.Remove(card);
            if (card.EquippedTo != null && card.FaceUp)
                st.DestroyedWhileEquipped[card] = engine.TurnNumber;
        }

        static bool ConsumeDestroyedWhileEquipped(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !s_state.TryGetValue(engine, out var st)) return false;
            if (!st.DestroyedWhileEquipped.TryGetValue(card, out var turn)) return false;
            st.DestroyedWhileEquipped.Remove(card);
            return turn == engine.TurnNumber;
        }

        /// <summary>
        /// TextEffectRuntime.TryResolveSentToGy: a card whose field → GY trigger needs "destroyed by a card effect while
        /// equipped". Returns false when the card has no such trigger (the generic path handles it); otherwise it handles
        /// the card: nothing happens unless that destruction was recorded; the mandatory trigger then resolves.
        /// </summary>
        public static bool TryResolveDestroyedWhileEquipped(DuelEngine engine, DuelistState who, CardInstance card,
            List<EffectClause> clauses)
        {
            if (engine == null || who == null || card == null || clauses == null) return false;
            var mine = clauses.Where(c => c != null && c.RequiresDestroyedByEffectWhileEquipped).ToList();
            if (mine.Count == 0) return false;
            if (!ConsumeDestroyedWhileEquipped(engine, card)) return true;
            // Cold Wave: a Spell/Trap Card's trigger cannot be activated while the lock lasts.
            if (card.Def != null && !card.Def.IsMonster && LocksAndZonesRuntime.SpellTrapTriggerLocked(engine, card))
                return true;
            foreach (var c in mine)
                Fire(engine, who, card, c);
            return true;
        }

        static void Fire(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c)
        {
            List<CardInstance> candidates;
            string prompt;
            if (c.Action == EffectActionKind.DiscardChosenFromHand)
            {
                var opp = engine.OpponentOf(who);
                candidates = opp?.Hand?.Where(h => h?.Def != null).ToList() ?? new List<CardInstance>();
                engine.Log($"{card.Name}: {who.Name} looks at {opp?.Name}'s hand " +
                           $"({(candidates.Count == 0 ? "empty" : string.Join(", ", candidates.Select(h => h.Name)))}).");
                if (candidates.Count == 0) return;
                prompt = $"{card.Name}: discard 1 card from your opponent's hand.";
            }
            else
            {
                candidates = TextEffectRuntime.CollectTargets(engine, who, c, card);
                if (candidates.Count == 0)
                {
                    engine.Log($"{card.Name}: no card on the field to target.");
                    return;
                }

                prompt = $"{card.Name}: target 1 card on the field to destroy.";
            }

            if (who.IsPlayer && engine.PendingActivation == null && candidates.Count > 1)
            {
                var p = new PendingActivation
                {
                    Controller = who,
                    Card = card,
                    TargetKind = c.Action == EffectActionKind.DiscardChosenFromHand
                        ? EffectTargetKind.CardInOppHand
                        : TextEffectRuntime.MapTargetKind(c),
                    UsesTextProgram = true,
                    IsMonsterEffect = false,
                    PromptOverride = prompt,
                    // A destruction inside a resolving Chain: the rest of the Chain waits for this pick.
                    HoldsChainResolution = engine.Chain.IsResolving
                };
                p.LegalTargets.AddRange(candidates);
                s_picks.Add(p, new PickContext { Who = who, Card = card, Clause = c });
                engine.SetPendingActivation(p);
                engine.Log(p.Prompt);
                engine.NotifyPublic();
                return;
            }

            var pick = candidates.Count == 1 ? candidates[0] : AiPick(engine, who, c, candidates);
            Apply(engine, who, card, c, pick);
        }

        static CardInstance AiPick(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> candidates)
        {
            var opp = engine.OpponentOf(who);
            if (c.Action == EffectActionKind.DiscardChosenFromHand)
                // The strongest monster, else the first Spell/Trap.
                return candidates.Where(h => h.Def.IsMonster).OrderByDescending(h => h.Def.atk).FirstOrDefault() ??
                       candidates[0];
            // Blast with Chain: the opponent's strongest monster, then a Spell/Trap of theirs; its own card only if no
            // other card is left (the target is mandatory).
            return candidates.Where(t => opp != null && opp.TryFindMonster(t, out _))
                       .OrderByDescending(t => t.CurrentAtk).FirstOrDefault() ??
                   candidates.FirstOrDefault(t => opp != null && engine.ControllerOf(t) == opp) ??
                   candidates[0];
        }

        static void Apply(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c, CardInstance pick)
        {
            if (pick == null) return;
            var d = false;
            TextEffectRuntime.ApplyClause(engine, who, card, c, pick, ref d, ref d);
            if (c.IsPsctTarget)
                TextEffectRuntime.NotifyTargetingEffectResolved(engine, pick, true);
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget: the human's pick for a destroyed-while-equipped trigger.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || target == null || !s_picks.TryGetValue(p, out var ctx)) return false;
            if (!p.LegalTargets.Contains(target)) return true;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            Apply(engine, ctx.Who, ctx.Card, ctx.Clause, target);
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            ok = true;
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending: the trigger is mandatory — Cancel makes the pick automatically.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var ctx)) return false;
            s_picks.Remove(p);
            var legal = p.LegalTargets.Where(t => t != null).ToList();
            engine.ClearPendingActivation();
            if (legal.Count > 0)
                Apply(engine, ctx.Who, ctx.Card, ctx.Clause, AiPick(engine, ctx.Who, ctx.Clause, legal));
            engine.ResumeChainAfterChoice();
            engine.NotifyPublic();
            return true;
        }

        /// <summary>TextEffectRuntime.AutoPick: the AI's equip target for Heart of Clear Water / Smoke Grenade, its Magic
        /// Reflector target.</summary>
        internal static bool TryAutoPick(DuelEngine engine, DuelistState who, EffectClause c, List<CardInstance> targets,
            out CardInstance pick)
        {
            pick = null;
            if (engine == null || who == null || c == null || targets == null || targets.Count == 0) return false;
            if (c.Zone == EffectZoneFilter.ControllerFaceUpSpellsExceptThis)
            {
                // The Spell most likely to be destroyed by an effect: an Equip Spell, then a Continuous / Field Spell.
                pick = targets.FirstOrDefault(t => t.Def.IsEquipSpell) ??
                       targets.FirstOrDefault(t => t.Def.IsContinuousSpellOrTrap) ?? targets[0];
                return true;
            }

            // An Equip Card with no stat change that may go on any face-up monster (Heart of Clear Water, Smoke Grenade
            // of the Thief): the AI equips its own monster — Heart preferably one under 1300 ATK.
            if (c.Action != EffectActionKind.EquipThisToTarget || c.Zone != EffectZoneFilter.FieldAnyMonster ||
                c.EquipAtkBonus != 0 || c.EquipDefBonus != 0 || c.TakeControlOfTarget ||
                !string.IsNullOrEmpty(c.EquipHostName) || EquipFamilyRuntime.AiPrefersOwnMonster(c))
                return false;
            var own = targets.Where(t => who.TryFindMonster(t, out _)).ToList();
            if (own.Count == 0) return false;
            pick = own.Where(t => t.CurrentAtk < 1300).OrderByDescending(t => t.CurrentAtk).FirstOrDefault() ??
                   own.OrderByDescending(t => t.CurrentAtk).First();
            return true;
        }

        // ------------------------------------------------------------------ board refresh

        [ThreadStatic] static bool s_refreshing;

        /// <summary>
        /// FieldSpellEffects.RefreshBoard: Heart of Clear Water destroys itself while its monster's ATK is 1300 or more;
        /// counters on cards that left the field or were turned face-down and stale destruction records are dropped.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || s_refreshing) return;
            s_refreshing = true;
            try
            {
                if (s_state.TryGetValue(engine, out var st))
                {
                    foreach (var card in st.Counters.Keys.ToList())
                        if (!OnFieldFaceUp(engine, card))
                            st.Counters.Remove(card);
                    foreach (var card in st.DestroyedWhileEquipped.Keys.ToList())
                        if (!engine.Player.Graveyard.Contains(card) && !engine.Opponent.Graveyard.Contains(card))
                            st.DestroyedWhileEquipped.Remove(card);
                }

                var doomed = new List<(DuelistState side, CardInstance card, int atk)>();
                foreach (var side in new[] { engine.Player, engine.Opponent })
                foreach (var eq in side.SpellTrapsOnField())
                {
                    if (eq?.EquippedTo == null || !EquipEffectsLive(engine, eq, eq.EquippedTo)) continue;
                    var c = ContinuousClause(eq, EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast);
                    if (c == null || c.Amount <= 0) continue;
                    if (eq.EquippedTo.CurrentAtk >= c.Amount)
                        doomed.Add((side, eq, eq.EquippedTo.CurrentAtk));
                }

                foreach (var (side, eq, atk) in doomed)
                {
                    if (engine.ControllerOf(eq) == null) continue;
                    engine.Log($"{eq.Name}: the equipped monster has {atk} ATK — this card is destroyed.");
                    TextEffectRuntime.DestroyCard(engine, eq, eq, false, side);
                }
            }
            finally
            {
                s_refreshing = false;
            }
        }

        // ------------------------------------------------------------------ Woodland Sprite

        /// <summary>The cost of <paramref name="source"/> takes any Equip Card equipped to it (Woodland Sprite).</summary>
        public static bool CostTakesAnyEquipCard(CardInstance source)
        {
            if (source?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            return prog != null && prog.ClauseList.Exists(c =>
                c != null && c.RequiresSendEquipSpellOnThisToGy && c.EquipCostAnyEquipCard);
        }

        /// <summary>Face-up Equip Cards equipped to <paramref name="source"/> in either player's Spell &amp; Trap Zones.</summary>
        public static List<CardInstance> CollectAnyEquipCardsOn(DuelEngine engine, CardInstance source)
        {
            var list = new List<CardInstance>();
            if (engine == null || source?.Equips == null) return list;
            foreach (var eq in source.Equips)
                if (eq != null && eq.FaceUp && ReferenceEquals(eq.EquippedTo, source) &&
                    EquipModelRuntime.IsEquipCard(engine, eq) && !list.Contains(eq))
                    list.Add(eq);
            return list;
        }
    }
}
