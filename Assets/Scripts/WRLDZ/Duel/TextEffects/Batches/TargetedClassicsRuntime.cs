using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TargetedClassics batch runtime (<see cref="TargetedClassicsAtoms"/>): targeted one-shot removals and boosts.
    /// <list type="bullet">
    /// <item>Remove Trap: a Destroy on <see cref="EffectZoneFilter.FieldSpellTraps"/> with
    /// <see cref="EffectClause.TargetCardKind"/> "Trap" and <see cref="EffectClause.RequiresTargetFaceUp"/>;
    /// TextEffectRuntime.CollectTargets applies both filters, and <see cref="FilterSpellTrapOffer"/> mirrors them in
    /// the legacy SpellTrapEffects.CollectLegalTargets picker.</item>
    /// <item>Nobleman of Crossout: <see cref="DestroyBanishThenPurgeIfFlip"/>
    /// (<see cref="EffectClause.PurgeDecksIfFlipMonster"/>).</item>
    /// <item>Deal of Phantom: <see cref="ApplyPerCountBoost"/> (ModifyTargetUntilEndOfTurn with a
    /// <see cref="EffectClause.CountSource"/>).</item>
    /// <item>Miracle Dig: <see cref="EffectZoneFilter.ControllerBanishedFaceUpMonsters"/>, the
    /// <see cref="YourFaceUpBanishedMonstersAtLeast"/> activation condition and
    /// <see cref="ReturnBanishedToGraveyard"/>.</item>
    /// <item>Tornado Bird: an exact <see cref="EffectClause.TargetCount"/> Flip target (<see cref="ExactTargetCount"/>);
    /// fewer legal targets than that and the effect resolves with no target.</item>
    /// </list>
    /// </summary>
    public static class TargetedClassicsRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Miracle Dig): the controller has at least
        /// <see cref="EffectClause.ConditionAmount"/> face-up monsters in their banished pile.
        /// </summary>
        public const string YourFaceUpBanishedMonstersAtLeast = "YourFaceUpBanishedMonstersAtLeast";

        // ───────────────────────────── Miracle Dig ─────────────────────────────

        /// <summary>A face-up monster card in <paramref name="who"/>'s banished pile (never a face-down banish).</summary>
        public static bool IsFaceUpBanishedMonster(DuelistState who, CardInstance card) =>
            who?.Banished != null && card?.Def != null && card.FaceUp && card.Def.IsMonster &&
            who.Banished.Contains(card);

        /// <summary>Targets for <see cref="EffectZoneFilter.ControllerBanishedFaceUpMonsters"/> (both target mirrors).</summary>
        public static void CollectFaceUpBanishedMonsters(DuelistState who, List<CardInstance> into)
        {
            if (who?.Banished == null || into == null) return;
            foreach (var c in who.Banished)
                if (IsFaceUpBanishedMonster(who, c) && !into.Contains(c))
                    into.Add(c);
        }

        public static int CountFaceUpBanishedMonsters(DuelistState who) =>
            who?.Banished?.Count(c => IsFaceUpBanishedMonster(who, c)) ?? 0;

        /// <summary>TextEffectRuntime.ActivationConditionsMet hook for this batch's conditions.</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, EffectClause clause,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (clause == null || who == null) return false;
            if (clause.ActivationCondition != YourFaceUpBanishedMonstersAtLeast) return false;
            var need = Math.Max(1, clause.ConditionAmount);
            ok = CountFaceUpBanishedMonsters(who) >= need;
            reason = $"Needs {need} or more of your monsters banished face-up.";
            return true;
        }

        /// <summary>
        /// <see cref="EffectActionKind.ReturnBanishedTargetToGy"/>: <paramref name="target"/> leaves its owner's
        /// banished pile for that player's GY, if it is still there face-up. While "any card sent to the GY is
        /// banished instead" applies (Banisher of the Light), it cannot go to the GY and stays banished. Returns true
        /// when the card reached the GY.
        /// </summary>
        public static bool ReturnBanishedToGraveyard(DuelEngine engine, CardInstance source, CardInstance target)
        {
            if (engine == null || target == null) return false;
            var owner = new[] { engine.Player, engine.Opponent }
                .FirstOrDefault(side => side?.Banished != null && side.Banished.Contains(target));
            if (owner == null || !IsFaceUpBanishedMonster(owner, target))
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer banished face-up — not affected.");
                return false;
            }

            if (ContinuousProtections.IsUnaffectedBy(engine, target, source))
            {
                engine.Log($"{target.Name} is unaffected by {source?.Name ?? "this effect"}.");
                return false;
            }

            if (RedirectAndReplaceRuntime.SendsToBanishedInstead(engine, target))
            {
                engine.Log($"{target.Name} would be banished instead of going to the GY — it stays banished.");
                return false;
            }

            owner.Banished.Remove(target);
            target.FaceUp = true;
            if (!RedirectAndReplaceRuntime.PutInGraveyard(engine, owner, target))
                return false;
            // RecoveryAndPhaseCosts batch: a REASON_RETURN trip (Manticore of Darkness ignores it).
            GyEntryStamps.AddReason(engine, target, GyEntryReason.Return, null);
            engine.GameEvents.Add(DuelGameEventKind.SentToGy, engine.TurnNumber, owner, target, source);
            engine.Log($"{source?.Name}: {target.Name} returns from the banished pile to {owner.Name}'s GY.");
            return true;
        }

        /// <summary>AI pick among your face-up banished monsters: the strongest first (the best GY resources).</summary>
        public static CardInstance AutoPickBanished(List<CardInstance> targets) =>
            targets == null || targets.Count == 0
                ? null
                : targets.OrderByDescending(t => t.Def?.atk ?? 0).First();

        // ───────────────────────────── Nobleman of Crossout ─────────────────────────────

        /// <summary>A Flip monster (ygopro TYPE_FLIP): "Flip" in the printed monster type.</summary>
        public static bool IsFlipMonster(CardInstance card) =>
            card?.Def?.type != null && card.Def.IsMonster &&
            card.Def.type.IndexOf("Flip", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Resolve Nobleman of Crossout on <paramref name="target"/> (c71044499): only a monster still face-down on the
        /// field is affected; it is destroyed and banished (<paramref name="destroy"/> is the ApplyClause destroy,
        /// with <see cref="EffectClause.BanishIfDestroyed"/>). Only if that destroyed it and it is a Flip monster,
        /// both Main Decks are revealed and every card with its name is banished from them, then both are shuffled
        /// (<paramref name="purgeName"/>: TextEffectRuntime.PurgeNameFromBothDecks).
        /// </summary>
        public static void DestroyBanishThenPurgeIfFlip(DuelEngine engine, CardInstance source, CardInstance target,
            Action<CardInstance> destroy, Action<string> purgeName)
        {
            if (engine == null || target == null) return;
            var controller = engine.ControllerOf(target);
            if (controller == null || !controller.TryFindMonster(target, out _))
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer on the field — no effect.");
                return;
            }

            if (target.FaceUp)
            {
                engine.Log($"{source?.Name}: {target.Name} is no longer face-down — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var flip = IsFlipMonster(target);
            var name = target.Def?.name ?? target.Name;
            destroy?.Invoke(target);
            if (engine.ControllerOf(target) != null) return; // not destroyed: nothing else happens
            engine.Log(flip
                ? $"{source?.Name}: {name} was a Flip monster — each player reveals their Main Deck."
                : $"{source?.Name}: {name} was not a Flip monster.");
            if (flip)
                purgeName?.Invoke(name);
        }

        /// <summary>
        /// AI pick for Nobleman of Crossout: an opponent's face-down monster (the highest printed ATK); only when it
        /// must, one of its own (the weakest).
        /// </summary>
        public static CardInstance AutoPickFaceDownToDestroy(DuelEngine engine, DuelistState who,
            List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var opp = engine?.OpponentOf(who);
            return targets.Where(t => opp != null && opp.TryFindMonster(t, out _))
                       .OrderByDescending(t => t.Def?.atk ?? 0).FirstOrDefault()
                   ?? targets.OrderBy(t => t.Def?.atk ?? 0).First();
        }

        // ───────────────────────────── Deal of Phantom ─────────────────────────────

        /// <summary>
        /// A ModifyTargetUntilEndOfTurn whose <see cref="EffectClause.Amount"/> is per counted thing
        /// (<see cref="EffectClause.CountSource"/>), not a flat boost (Rush Recklessly).
        /// </summary>
        public static bool IsPerCountBoost(EffectClause c) =>
            c != null && c.Action == EffectActionKind.ModifyTargetUntilEndOfTurn &&
            c.CountSource == TargetedClassicsAtoms.MonstersInYourGy;

        /// <summary>The count for <see cref="IsPerCountBoost"/>, taken now: monster cards in <paramref name="who"/>'s GY.</summary>
        public static int PerCountBoostCount(DuelistState who, EffectClause c) =>
            c?.CountSource == TargetedClassicsAtoms.MonstersInYourGy
                ? who?.Graveyard?.Count(g => g?.Def != null && g.Def.IsMonster) ?? 0
                : 0;

        /// <summary>
        /// Deal of Phantom at resolution (c69122763): if the target is still a face-up monster on the field, it gains
        /// <see cref="EffectClause.Amount"/> ATK for each monster in the controller's GY right now, until the end of
        /// this turn. The value is fixed here; later changes to the GY do not change it.
        /// </summary>
        public static void ApplyPerCountBoost(DuelEngine engine, DuelistState who, CardInstance source,
            EffectClause clause, CardInstance target)
        {
            if (engine == null || clause == null) return;
            var controller = target != null ? engine.ControllerOf(target) : null;
            if (controller == null || !controller.TryFindMonster(target, out _) || !target.FaceUp)
            {
                engine.Log($"{source?.Name}: the target is no longer a face-up monster on the field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            var n = PerCountBoostCount(who, clause);
            var gain = Math.Max(0, clause.Amount) * n;
            target.UntilEndOfTurnAtk += gain;
            engine.Log($"{source?.Name}: {n} monster(s) in {who?.Name}'s GY — {target.Name} gains {gain} ATK " +
                       $"until the end of this turn ({target.CurrentAtk} ATK).");
        }

        /// <summary>
        /// AI pick for a per-count boost: its own strongest face-up monster; only when it must, the opponent's
        /// weakest.
        /// </summary>
        public static CardInstance AutoPickBoostTarget(DuelEngine engine, DuelistState who, List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            return targets.Where(t => who != null && who.TryFindMonster(t, out _))
                       .OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
                   ?? targets.OrderBy(t => t.CurrentAtk).First();
        }

        // ───────────────────────────── Tornado Bird ─────────────────────────────

        /// <summary>
        /// The number of targets an exact multi-target clause takes (Tornado Bird: "Return 2 Spell or Trap Cards"),
        /// or 0 when the clause is not one: 1 target, "up to N" (<see cref="EffectClause.TargetUpTo"/>) or separate
        /// target groups (<see cref="EffectClause.DistinctTargetGroup"/>).
        /// </summary>
        public static int ExactTargetCount(EffectClause c) =>
            c != null && c.RequiresTargetChoice && c.TargetCount > 1 && !c.TargetUpTo && !c.DistinctTargetGroup
                ? c.TargetCount
                : 0;

        /// <summary>Every pick is still on the field (ygopro: all targets still relate to the effect, #dg == N).</summary>
        public static bool AllStillOnField(DuelEngine engine, IEnumerable<CardInstance> picks) =>
            engine != null && picks != null && picks.All(t => t != null && engine.ControllerOf(t) != null);

        // ───────────────────────────── Remove Trap: legacy picker mirror ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.CollectLegalTargets (SpellTrapOnField): the activating card's compiled Spell/Trap target
        /// may be face-up only (<see cref="EffectClause.RequiresTargetFaceUp"/>) and of one card kind
        /// (<see cref="EffectClause.TargetCardKind"/>) — Remove Trap's "face-up Trap Card". Removes the cards those
        /// filters refuse, exactly as TextEffectRuntime.CollectTargets does (a Set card of the wrong kind is only
        /// refused when the target must be face-up).
        /// </summary>
        public static void FilterSpellTrapOffer(CardInstance activating, List<CardInstance> offer)
        {
            if (activating?.Def == null || offer == null || offer.Count == 0) return;
            var prog = CompiledEffectCache.GetOrCompile(activating.Def);
            if (prog == null || !prog.FullyCompiled) return;
            var clause = prog.ClausesFor(EffectTiming.Activate)?.Find(c =>
                c != null && c.RequiresTargetChoice && c.Zone == EffectZoneFilter.FieldSpellTraps &&
                (c.RequiresTargetFaceUp || !string.IsNullOrEmpty(c.TargetCardKind)));
            if (clause == null) return;
            if (clause.RequiresTargetFaceUp)
                offer.RemoveAll(t => t == null || !t.FaceUp);
            if (!string.IsNullOrEmpty(clause.TargetCardKind))
                offer.RemoveAll(t => t?.Def == null || (t.FaceUp && !IsCardKind(t, clause.TargetCardKind)));
        }

        /// <summary>Mirror of TextEffectRuntime.CardIsKind for "Spell" / "Trap" (a monster equipped as a Spell is a Spell).</summary>
        static bool IsCardKind(CardInstance card, string kind)
        {
            if (card?.Def == null || string.IsNullOrEmpty(kind)) return true;
            if (SpiritsAndMiscRuntime.IsMonsterTreatedAsEquipSpell(card))
                return string.Equals(kind, "Spell", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(kind, "Spell", StringComparison.OrdinalIgnoreCase)) return card.Def.IsSpell;
            if (string.Equals(kind, "Trap", StringComparison.OrdinalIgnoreCase)) return card.Def.IsTrap;
            if (string.Equals(kind, "EffectMonster", StringComparison.OrdinalIgnoreCase))
                return TextEffectRuntime.IsTrueEffectMonster(card.Def);
            return true;
        }
    }
}
