using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TakeControl batch runtime (Tools/CardCompile/README.md):
    /// <list type="bullet">
    /// <item>Charmers (<see cref="EffectActionKind.TakeControlWhileSourceFaceUp"/>): FLIP, take control of
    /// the targeted face-up Attribute monster while the Charmer stays face-up on the field. The stolen
    /// monster is bound to the Charmer (<see cref="CardInstance.ControlBoundTo"/>, with a
    /// <see cref="ControlBinding"/> record of the control effect it was layered over);
    /// <see cref="SweepBoundControl"/> hands it back to that effect (usually its owner) on the first board
    /// refresh after the Charmer leaves the field or is turned face-down (ygopro c:SetCardTarget +
    /// EFFECT_SET_CONTROL with an IsHasCardTarget condition). The stolen monster is not destroyed. If the
    /// stolen monster itself is turned face-down, the binding ends and its current controller keeps it
    /// (ygopro card::reset RESET_TURN_SET turns a conditional control effect into a permanent one).</item>
    /// <item>Invader of the Throne (<see cref="EffectActionKind.SwitchControlWithThis"/>): FLIP, switch
    /// control of the targeted opponent's monster with this card (ygopro Duel.SwapControl, permanent);
    /// not during the Battle Phase (<see cref="EffectClause.CannotActivateInBattlePhase"/>).</item>
    /// <item>Brain Control (<see cref="EffectClause.TargetMustBeNormalSummonable"/>): the target must be
    /// a monster that can be Normal Summoned/Set (ygopro IsSummonableCard).</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// A Charmer's control of one monster. ygopro stacks control effects (EFFECT_SET_CONTROL): the most
    /// recent one that still applies decides the controller, and with none left the owner controls it.
    /// The engine keeps one controller per card, so this record remembers which older control effect
    /// the Charmer's control was layered over; when the Charmer's control ends, that effect (or the
    /// owner) takes over again (<see cref="TakeControlRuntime.SweepBoundControl"/>).
    /// </summary>
    public sealed class ControlBinding
    {
        /// <summary>The Charmer.</summary>
        public CardInstance Source;

        /// <summary>The player who took control with the Charmer.</summary>
        public DuelistState Taker;

        /// <summary>The player who controlled the monster just before the Charmer took it.</summary>
        public DuelistState Prior;

        /// <summary><see cref="Prior"/> is the owner: no control effect was under the Charmer's.</summary>
        public bool PriorIsOwner;

        /// <summary><see cref="Prior"/> held it by this take-control Equip Card (Snatch Steal).</summary>
        public CardInstance PriorEquip;

        /// <summary><see cref="Prior"/> held it until this turn's End Phase (Change of Heart family); -1 = no.</summary>
        public int PriorUntilTurn = -1;

        /// <summary><see cref="Prior"/> held it by an earlier Charmer.</summary>
        public ControlBinding PriorBinding;

        /// <summary>
        /// Remove Brainwashing kept the monster with its owner when the Charmer resolved. The Charmer's
        /// control starts to apply once Remove Brainwashing stops applying while the Charmer is face-up.
        /// </summary>
        public bool Suspended;

        /// <summary>None of the above: <see cref="Prior"/> held it by a permanent control effect (Invader of the Throne).</summary>
        public bool PriorIsPermanent =>
            !PriorIsOwner && PriorEquip == null && PriorUntilTurn < 0 && PriorBinding == null;
    }

    public static class TakeControlRuntime
    {
        /// <summary>Re-entrancy guard: a return that fails sends the monster to the GY, which refreshes the board.</summary>
        [ThreadStatic] static int s_sweepDepth;

        /// <summary>
        /// A printed, unconditional "Cannot be Normal Summoned/Set." / "cannot be Normal Summoned or Set."
        /// sentence (ygopro TYPE_SPSUMMON / EFFECT_UNSUMMONABLE_CARD). A conditional or Summon-only
        /// restriction — "Cannot be Normal Summoned while you control a monster" (Cave Dragon), "Cannot be
        /// Normal Summoned (but can be Normal Set)" (Mystic Swordsman LV4), "cannot be Normal Summoned or
        /// Flip Summoned unless …" (Terrorking Archfiend) — still leaves a card that can be Normal Summoned/Set.
        /// </summary>
        static readonly Regex RxUnsummonableCard = new(
            @"\bcannot be Normal Summoned(?:/Set| or Set)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// ygopro Card.IsSummonableCard: a main-deck monster card that is not a Token, not a Ritual Monster
        /// and not printed with an unconditional "Cannot be Normal Summoned/Set" (<see cref="RxUnsummonableCard"/>).
        /// A Trap Monster's card is a Trap, so it is excluded too.
        /// </summary>
        public static bool IsNormalSummonableCard(CardInstance m)
        {
            if (m?.Def == null || m.IsToken) return false;
            var def = m.Def;
            if (!def.IsMonster || def.IsExtraDeck || def.IsRitualMonster) return false;
            return !RxUnsummonableCard.IsMatch(OfficialCardAuthority.OfficialText(m));
        }

        /// <summary>
        /// Batch target filters, applied at the end of <see cref="TextEffectRuntime"/>.CollectTargets
        /// (activation legality, the target prompt and the AI pick all read that list).
        /// </summary>
        public static void FilterTargets(DuelEngine engine, DuelistState who, EffectClause c,
            CardInstance source, List<CardInstance> list)
        {
            if (c == null || list == null || list.Count == 0) return;
            if (c.TargetMustBeNormalSummonable)
                list.RemoveAll(t => !IsNormalSummonableCard(t));
            if (c.Action != EffectActionKind.TakeControlWhileSourceFaceUp) return;

            if (!string.IsNullOrEmpty(c.AttributeFilter))
                list.RemoveAll(t => t == null || !t.FaceUp || !HasAttribute(t, c.AttributeFilter));
            // IsControlerCanBeChanged: the Charmer's controller needs a free Monster Zone.
            if (engine != null && who != null && engine.FirstEmpty(who.MonsterZones) < 0)
                list.Clear();
            // STATUS_BATTLE_DESTROYED: a Charmer flipped by an attack that destroys it selects no target.
            if (engine != null && source != null && engine.FlipSourceBattleDestroyPending == source)
                list.Clear();
        }

        /// <summary>
        /// Legacy Spell/Trap target mirror (<see cref="SpellTrapEffects"/>.CollectLegalTargets): true when the
        /// activating card's compiled Activate target must be a monster that can be Normal Summoned/Set.
        /// </summary>
        public static bool ActivationRequiresNormalSummonableTarget(CardInstance activating)
        {
            if (activating?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(activating.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && c.RequiresTargetChoice && c.TargetMustBeNormalSummonable)
                    return true;
            return false;
        }

        /// <summary>"This effect cannot be activated during the Battle Phase." (Invader of the Throne).</summary>
        public static bool FlipBlockedInBattlePhase(DuelEngine engine, CardInstance monster,
            List<EffectClause> flip)
        {
            if (engine == null || flip == null || engine.Phase != DuelPhase.Battle) return false;
            if (!flip.Exists(c => c != null && c.CannotActivateInBattlePhase)) return false;
            engine.Log($"{monster?.Name}: this effect cannot be activated during the Battle Phase.");
            return true;
        }

        /// <summary>Charmer FLIP resolution: take control of the target while this card stays face-up.</summary>
        public static void ApplyTakeControlWhileSourceFaceUp(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null || target == null) return;
            if (!IsFaceUpMonsterOnField(engine, source))
            {
                engine.Log($"{source?.Name}: no longer face-up on the field — take control does not apply.");
                return;
            }

            var old = engine.ControllerOf(target);
            if (old == null || old == who)
            {
                engine.Log($"{source.Name}: take control failed — target is not opponent-controlled.");
                return;
            }

            // "Target 1 face-up <Attribute> monster": a target turned face-down (or no longer of that
            // Attribute) before resolution no longer meets the targeting requirement.
            if (!target.FaceUp ||
                (!string.IsNullOrEmpty(clause?.AttributeFilter) && !HasAttribute(target, clause.AttributeFilter)))
            {
                engine.Log($"{source.Name}: {target.Name} is no longer a legal target — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // MonsterNegationAndControlLocks batch: Blindly Loyal Goblin — its control cannot switch.
            if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, target)) return;

            var binding = NewBinding(engine, who, source, target, old);
            if (!engine.TryTakeControl(who, target))
            {
                engine.Log($"{source.Name}: take control failed — no zone.");
                return;
            }

            // Remove Brainwashing may have snapped it straight back to its owner. The Charmer's effect
            // still lingers and applies once Remove Brainwashing stops applying.
            binding.Suspended = engine.ControllerOf(target) != who;
            target.ControlBoundTo = source;
            target.ControlBinding = binding;
            engine.Log(binding.Suspended
                ? $"Remove Brainwashing keeps {target.Name} with its owner; {who.Name} takes control of it " +
                  $"if Remove Brainwashing stops applying while {source.Name} is face-up on the field."
                : $"{who.Name} controls {target.Name} while {source.Name} is face-up on the field.");
        }

        /// <summary>What the target's current controller <paramref name="prior"/> holds it by, just before the take.</summary>
        static ControlBinding NewBinding(DuelEngine engine, DuelistState taker, CardInstance source,
            CardInstance target, DuelistState prior)
        {
            var b = new ControlBinding { Source = source, Taker = taker, Prior = prior };
            var earlier = CurrentBinding(target);
            CardInstance equip;
            if (earlier != null && earlier.Taker == prior && !earlier.Suspended)
                b.PriorBinding = earlier;
            else if (target.ControlOwner == null)
                b.PriorIsOwner = true;
            else if ((equip = ControllingEquip(engine, target, prior)) != null)
                b.PriorEquip = equip;
            else if (target.ControlRevertsAtEndPhase)
                b.PriorUntilTurn = engine.TurnNumber;
            return b;
        }

        /// <summary>The live Charmer record of <paramref name="m"/>, or null.</summary>
        static ControlBinding CurrentBinding(CardInstance m) =>
            m?.ControlBoundTo != null && m.ControlBinding != null && m.ControlBinding.Source == m.ControlBoundTo
                ? m.ControlBinding
                : null;

        /// <summary>
        /// A take-control Equip Card (Snatch Steal) attached to <paramref name="m"/> whose control still
        /// applies, controlled by <paramref name="controller"/> (any controller when null).
        /// </summary>
        static CardInstance ControllingEquip(DuelEngine engine, CardInstance m, DuelistState controller)
        {
            if (engine == null || m == null || !m.TakenByEquipControl) return null;
            foreach (var e in m.Equips)
                if (EquipStillTakesControl(engine, m, e) &&
                    (controller == null || engine.ControllerOf(e) == controller))
                    return e;
            return null;
        }

        static bool EquipStillTakesControl(DuelEngine engine, CardInstance m, CardInstance equip)
        {
            if (equip?.Def == null || equip.EquippedTo != m || !m.TakenByEquipControl) return false;
            var holder = engine.ControllerOf(equip);
            if (holder == null || !holder.TryFindSpellTrap(equip, out _)) return false;
            if (ContinuousProtections.EquipCardEffectsAreNegated(engine, equip)) return false;
            var prog = CompiledEffectCache.GetOrCompile(equip.Def);
            return prog != null && prog.ClauseList.Exists(c => c != null && c.TakeControlOfTarget);
        }

        /// <summary>Invader of the Throne FLIP resolution: switch control of the target and this card.</summary>
        public static void ApplySwitchControlWithThis(DuelEngine engine, DuelistState who,
            CardInstance source, EffectClause clause, CardInstance target)
        {
            if (engine == null || who == null || source == null || target == null) return;
            if (!who.TryFindMonster(source, out _))
            {
                engine.Log($"{source.Name}: no longer on the field — switch control does not apply.");
                return;
            }

            var opp = engine.OpponentOf(who);
            if (opp == null || !opp.TryFindMonster(target, out _))
            {
                engine.Log($"{source.Name}: switch control failed — target is not opponent-controlled.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            // MonsterNegationAndControlLocks batch: neither monster's control may be locked (ygopro Duel.SwapControl).
            if (MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, target) ||
                MonsterNegationAndControlLocksRuntime.BlocksControlChange(engine, source, source))
                return;

            if (!engine.TrySwapControl(source, target))
                engine.Log($"{source.Name}: switch control failed.");
        }

        /// <summary>
        /// Charmers, on every board refresh:
        /// <list type="bullet">
        /// <item>a bound monster that is now face-down loses the binding and stays with its current
        /// controller (ygopro card::reset RESET_TURN_SET makes the Charmer's control permanent when it is
        /// the effect in charge; when a newer control effect is in charge, that one keeps it);</item>
        /// <item>a bound monster whose Charmer is no longer face-up on the field goes back to whatever
        /// the Charmer's control was layered over (<see cref="ResolveUnder"/>): the owner, a still-equipped
        /// Snatch Steal, this turn's Change of Heart, an earlier Charmer or a permanent switch. With no
        /// free zone on that side it is sent to the GY, as the End Phase return of Change of Heart does.
        /// When a newer control effect holds it (the owner's Brain Control), that effect's own return
        /// is pointed at the same place instead;</item>
        /// <item>a Charmer take suspended by Remove Brainwashing applies once Remove Brainwashing no
        /// longer does.</item>
        /// </list>
        /// </summary>
        public static void SweepBoundControl(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null || engine.GameOver) return;
            if (s_sweepDepth > 0) return;
            s_sweepDepth++;
            try
            {
                List<CardInstance> expired = null, resumed = null;
                foreach (var side in new[] { engine.Player, engine.Opponent })
                    foreach (var m in side.MonstersOnField())
                    {
                        if (m?.ControlBoundTo == null) continue;
                        var b = CurrentBinding(m);
                        if (!m.FaceUp)
                        {
                            engine.Log($"{m.Name} was turned face-down — {side.Name} keeps control of it.");
                            m.ControlBoundTo = null;
                            m.ControlBinding = null;
                            // A newer control effect was in charge: it now falls back to what the Charmer covered.
                            if (b != null && (b.Suspended || side != b.Taker))
                                PointNewerControlUnder(engine, m, side, b);
                            continue;
                        }

                        if (IsFaceUpMonsterOnField(engine, m.ControlBoundTo))
                        {
                            if (b != null && b.Suspended && !TextEffectRuntime.AnyFaceUpReturnControlToOwners(engine))
                                (resumed ??= new List<CardInstance>()).Add(m);
                            continue;
                        }

                        (expired ??= new List<CardInstance>()).Add(m);
                    }

                if (resumed != null)
                    foreach (var m in resumed)
                        Resume(engine, m);

                if (expired == null) return;
                foreach (var m in expired)
                    Expire(engine, m);
            }
            finally
            {
                s_sweepDepth--;
            }
        }

        /// <summary>Remove Brainwashing stopped applying: the suspended Charmer take applies now.</summary>
        static void Resume(DuelEngine engine, CardInstance m)
        {
            var b = CurrentBinding(m);
            var holder = engine.ControllerOf(m);
            if (b == null || holder == null) return;
            b.Suspended = false;
            if (holder == b.Taker) return;
            if (engine.TryTakeControl(b.Taker, m))
            {
                engine.Log($"Remove Brainwashing no longer applies — {b.Taker.Name} takes control of {m.Name} " +
                           $"while {b.Source?.Name} is face-up on the field.");
                return;
            }

            engine.Log($"{m.Name}: no Monster Zone to take control into — sent to the GY.");
            m.ControlBoundTo = null;
            m.ControlBinding = null;
            engine.SendCardToGrave(holder, m);
        }

        /// <summary>The Charmer left the field or was turned face-down.</summary>
        static void Expire(DuelEngine engine, CardInstance m)
        {
            var source = m.ControlBoundTo;
            var b = CurrentBinding(m);
            m.ControlBoundTo = null;
            m.ControlBinding = null;
            var holder = engine.ControllerOf(m);
            if (holder == null) return;

            if (b == null)
            {
                // No record (should not happen): plain return to the owner.
                var owner = m.ControlOwner;
                if (owner == null || owner == holder) return;
                if (!engine.TryTakeControl(owner, m))
                {
                    engine.Log($"{m.Name}: no Monster Zone to return control to — sent to the GY.");
                    engine.SendCardToGrave(holder, m);
                }
                return;
            }

            var under = ResolveUnder(engine, m, b, afterEndPhase: false, out var restore);
            if (restore != null)
            {
                m.ControlBoundTo = restore.Source;
                m.ControlBinding = restore;
            }

            if (b.Suspended || holder != b.Taker)
            {
                // A newer control effect (or Remove Brainwashing) holds it: nothing moves now.
                PointNewerControlUnder(engine, m, holder, b);
                return;
            }

            if (under == null || under == holder) return;
            if (engine.TryTakeControl(under, m))
            {
                engine.Log($"{source?.Name} is no longer face-up on the field — control of {m.Name} " +
                           $"goes to {under.Name}.");
                return;
            }

            engine.Log($"{m.Name}: no Monster Zone to return control to — sent to the GY.");
            m.ControlBoundTo = null;
            m.ControlBinding = null;
            engine.SendCardToGrave(holder, m);
        }

        /// <summary>
        /// Who controls <paramref name="m"/> once the Charmer's control <paramref name="b"/> is gone: the
        /// control effect it was layered over while that one still applies (an earlier Charmer is handed
        /// back as <paramref name="restore"/>), else the owner. With <paramref name="afterEndPhase"/>, a
        /// Change of Heart under it counts as ended.
        /// </summary>
        static DuelistState ResolveUnder(DuelEngine engine, CardInstance m, ControlBinding b, bool afterEndPhase,
            out ControlBinding restore)
        {
            restore = null;
            for (var cur = b; cur != null; cur = cur.PriorBinding)
            {
                if (cur.PriorBinding != null)
                {
                    if (IsFaceUpMonsterOnField(engine, cur.PriorBinding.Source))
                    {
                        restore = cur.PriorBinding;
                        return restore.Taker;
                    }

                    continue; // that Charmer is gone too: look under it
                }

                if (cur.PriorIsOwner) return cur.Prior;
                if (cur.PriorEquip != null && EquipStillTakesControl(engine, m, cur.PriorEquip))
                    return engine.ControllerOf(cur.PriorEquip) ?? cur.Prior;
                if (!afterEndPhase && cur.PriorUntilTurn == engine.TurnNumber && m.ControlRevertsAtEndPhase)
                    return cur.Prior;
                if (cur.PriorIsPermanent) return cur.Prior;
                break; // the effect under it has ended too
            }

            return m.ControlOwner ?? engine.ControllerOf(m);
        }

        /// <summary>
        /// A control effect newer than the Charmer's holds <paramref name="m"/> for <paramref name="holder"/>
        /// (the owner's Brain Control or Snatch Steal). The Charmer's control is gone, so when that effect
        /// ends the monster must go to what the Charmer covered, not back to the Charmer's controller.
        /// </summary>
        static void PointNewerControlUnder(DuelEngine engine, CardInstance m, DuelistState holder, ControlBinding b)
        {
            var after = ResolveUnder(engine, m, b, afterEndPhase: true, out _);
            if (after == null) return;
            // Change of Heart family: returns at this End Phase to OriginalControllerIsPlayer.
            if (m.ControlRevertsAtEndPhase)
                m.OriginalControllerIsPlayer = after.IsPlayer;
            // Snatch Steal family (RevertEquipTakeControl): when the holder's Equip leaves, the monster
            // goes to the Equip controller's opponent. When what lies under is the holder itself, it stays.
            if (m.TakenByEquipControl && after == holder && !ChainHoldsEquip(b, m))
                m.TakenByEquipControl = false;
        }

        /// <summary>True when a take-control Equip under the Charmer (not a newer one) is still attached.</summary>
        static bool ChainHoldsEquip(ControlBinding b, CardInstance m)
        {
            for (var cur = b; cur != null; cur = cur.PriorBinding)
                if (cur.PriorEquip != null && cur.PriorEquip.EquippedTo == m)
                    return true;
            return false;
        }

        static bool HasAttribute(CardInstance card, string attribute) =>
            card?.Def != null && card.Attribute != null && // live Attribute (Scroll of Bewitchment, EquipFamily)
            card.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase);

        static bool IsFaceUpMonsterOnField(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !card.FaceUp) return false;
            return engine.Player.TryFindMonster(card, out _) || engine.Opponent.TryFindMonster(card, out _);
        }
    }
}
