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
        /// <returns>
        /// True if the targeting effect was negated, or if its rolls wait on a player's re-roll choice (Dice Re-Roll,
        /// <see cref="CoinDiceInterruptsRuntime"/>): the caller must skip it now. In the second case
        /// <paramref name="resumeIfNotNegated"/> applies the effect once every roll is settled without a negation.
        /// </returns>
        public static bool TryNegate(DuelEngine engine, DuelistState activator,
            CardInstance source, CardInstance target, System.Action resumeIfNotNegated = null)
        {
            if (engine == null || activator == null || target?.Def == null) return false;
            if (!target.Def.IsMonster) return false;

            var controller = engine.ControllerOf(target);
            if (controller == null || controller == activator) return false; // not an opponent's effect
            if (!target.FaceUp) return false;

            var protectors = new System.Collections.Generic.List<CardInstance>(controller.MonstersOnField());
            bool? settled = null;
            var waiting = false;

            void Done(bool negated)
            {
                if (!waiting) settled = negated;
                else if (!negated) resumeIfNotNegated?.Invoke();
            }

            void From(int start)
            {
                for (var i = start; i < protectors.Count; i++)
                {
                    var protector = protectors[i];
                    if (protector == null || !protector.FaceUp || protector.IsNegated) continue;
                    if (!ReferenceEquals(engine.ControllerOf(protector), controller)) continue;
                    var clause = FindProtection(protector, target);
                    if (clause == null) continue;

                    var next = i + 1;
                    // A die-roll event: Dice Re-Roll may have the roll re-rolled before it applies.
                    CoinDiceInterruptsRuntime.RollDie(engine, controller, protector, roll => Negates(clause, roll),
                        roll =>
                        {
                            var negates = Negates(clause, roll);
                            engine.Log($"{protector.Name}: rolls a {roll} vs {target.Name} being targeted" +
                                       (negates ? " — negate + destroy the opponent's card." : " — no negate."));
                            if (!negates)
                            {
                                From(next);
                                return;
                            }

                            if (source != null)
                            {
                                var srcOwner = engine.ControllerOf(source) ?? activator;
                                engine.SendCardToGrave(srcOwner, source);
                                engine.Log($"{protector.Name}: {source.Name} is destroyed.");
                            }

                            Done(true);
                        });
                    return;
                }

                Done(false);
            }

            From(0);
            if (settled.HasValue) return settled.Value;
            waiting = true;
            return true;
        }

        static bool Negates(EffectClause clause, int roll) =>
            roll >= 1 && roll <= 6 && (clause.DieNegateFacesMask & (1 << (roll - 1))) != 0;

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
