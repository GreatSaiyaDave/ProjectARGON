using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>
    /// The Archfiend die-roll protection, resolved as an opponent's targeting effect
    /// would apply. When a monster is targeted by the effect of a card its controller's
    /// opponent controls, any face-up Archfiend the controller controls that grants the
    /// protection (itself, or — for Darkbishop — every Archfiend it controls) rolls a
    /// six-sided die; on a negate face the effect is negated and the opponent's card is
    /// destroyed. Program-driven via <see cref="EffectActionKind.DieRollNegateWhenTargeted"/>
    /// — no card-id special cases.
    /// </summary>
    public static class ArchfiendTargetNegation
    {
        /// <returns>True if the targeting effect was negated (caller must skip it).</returns>
        public static bool TryNegate(DuelEngine engine, DuelistState activator,
            CardInstance source, CardInstance target)
        {
            if (engine == null || activator == null || target?.Def == null) return false;
            if (!target.Def.IsMonster) return false;

            var controller = engine.ControllerOf(target);
            if (controller == null || controller == activator) return false; // not an opponent's effect
            if (!target.FaceUp) return false;

            foreach (var protector in controller.MonstersOnField())
            {
                if (protector == null || !protector.FaceUp || protector.IsNegated) continue;
                var clause = FindProtection(protector, target);
                if (clause == null) continue;

                engine.Rng.SetPresentationContext(controller.IsPlayer, protector.Name);
                var roll = engine.Rng.RollDie();
                var negates = (clause.DieNegateFacesMask & (1 << (roll - 1))) != 0;
                engine.Log($"{protector.Name}: rolls a {roll} vs {target.Name} being targeted" +
                           (negates ? " — negate + destroy the opponent's card." : " — no negate."));
                if (!negates) continue;

                if (source != null)
                {
                    var srcOwner = engine.ControllerOf(source) ?? activator;
                    engine.SendCardToGrave(srcOwner, source);
                    engine.Log($"{protector.Name}: {source.Name} is destroyed.");
                }
                return true;
            }

            return false;
        }

        static EffectClause FindProtection(CardInstance protector, CardInstance target)
        {
            var prog = CompiledEffectCache.GetOrCompile(protector.Def);
            if (prog == null) return null;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
            {
                if (c == null || c.Action != EffectActionKind.DieRollNegateWhenTargeted) continue;
                if (c.DieNegateFacesMask == 0) continue;
                var self = protector == target;
                var coversArchfiend = c.ProtectsAllArchfiendsOnField &&
                                      FieldSpellEffects.IsArchfiendMonster(target.Def);
                if (self || coversArchfiend) return c;
            }
            return null;
        }
    }
}
