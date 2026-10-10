using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// FlipDefCheck batch runtime (Acid Trap Hole, <see cref="EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown"/>).
    /// Follows c41356845.lua: the target goes to face-up Defense Position; if that changed nothing (it is already
    /// face-up Defense) the effect ends. Otherwise its DEF is checked right after the flip: DEF &lt;=
    /// <see cref="EffectClause.ConditionAmount"/> is destroyed, anything higher goes back to face-down Defense
    /// Position. A monster that was face-down activates its FLIP effect only after that check (the FLIP trigger
    /// follows the resolving card), so the check never sees a DEF its own FLIP effect changed, and the FLIP effect
    /// still activates when the monster was destroyed or turned face-down again.
    /// </summary>
    public static class FlipDefCheckRuntime
    {
        /// <summary>Targets for <see cref="EffectZoneFilter.FieldFaceDownMonsters"/>: face-down monsters on either field.</summary>
        public static void CollectFaceDownMonsters(DuelEngine engine, DuelistState who, DuelistState opp,
            CardInstance except, List<CardInstance> into)
        {
            foreach (var side in new[] { who, opp })
            {
                if (side == null) continue;
                foreach (var m in side.MonstersOnField())
                {
                    if (m == null || m == except || m.FaceUp) continue;
                    if (engine.IsDragonTargetProtected(m)) continue;
                    into.Add(m);
                }
            }
        }

        /// <summary>
        /// AI pick: an opponent's face-down monster this destroys (DEF &lt;= ConditionAmount, highest ATK first),
        /// then any opponent's face-down monster, then one of its own that survives the check.
        /// </summary>
        public static CardInstance AutoPick(DuelEngine engine, DuelistState who, EffectClause c,
            List<CardInstance> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            var opp = engine.OpponentOf(who);
            var threshold = c?.ConditionAmount ?? 0;
            var theirs = targets.Where(t => opp != null && opp.TryFindMonster(t, out _)).ToList();
            return theirs.Where(t => t.CurrentDef <= threshold).OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
                   ?? theirs.OrderByDescending(t => t.CurrentAtk).FirstOrDefault()
                   ?? targets.Where(t => t.CurrentDef > threshold).OrderBy(t => t.CurrentAtk).FirstOrDefault()
                   ?? targets[0];
        }

        /// <summary>
        /// Resolve on <paramref name="target"/>. <paramref name="destroy"/> is the ApplyClause destroy
        /// (DestroyCard with this card as the source).
        /// </summary>
        public static void Apply(DuelEngine engine, CardInstance source, EffectClause clause, CardInstance target,
            Action<CardInstance> destroy)
        {
            var controller = target != null ? engine.ControllerOf(target) : null;
            if (controller == null)
            {
                engine.Log($"{source?.Name}: the target is no longer on the field — no effect.");
                return;
            }

            if (ContinuousProtections.TargetedEffectBlocked(engine, target, source))
            {
                engine.Log("Target is protected at resolution.");
                return;
            }

            if (target.FaceUp && target.Position == BattlePosition.Defense)
            {
                engine.Log($"{source?.Name}: {target.Name} is already face-up Defense Position — no effect.");
                return;
            }

            var wasFaceDown = !target.FaceUp;
            var before = target.Position;
            target.FaceUp = true;
            target.Position = BattlePosition.Defense;
            engine.Log($"{source?.Name}: {target.Name} → face-up Defense Position.");
            if (before != BattlePosition.Defense)
                TextEffectRuntime.NotifyPositionChanged(engine, target, toDefense: true);

            var threshold = clause?.ConditionAmount ?? 0;
            var def = target.CurrentDef;
            if (def <= threshold)
            {
                engine.Log($"{source?.Name}: {target.Name} has {def} DEF (≤ {threshold}) — destroyed.");
                destroy?.Invoke(target);
            }
            else
            {
                target.FaceUp = false;
                target.Position = BattlePosition.Defense;
                TextEffectRuntime.ClearFaceUpMarks(target);
                engine.Log($"{source?.Name}: {target.Name} has {def} DEF (> {threshold}) — returned face-down.");
            }

            if (wasFaceDown)
                MonsterEffects.OnFlipSummoned(engine, controller, target);
        }
    }
}
