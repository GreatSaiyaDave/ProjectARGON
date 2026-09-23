using System.Collections.Generic;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Runtime for trap-lock / trap-negate atoms. Reads compiled programs —
    /// no cardId branches. Amplifier exemption is a clause flag on the Equip.
    /// </summary>
    public static class ContinuousNegations
    {
        /// <summary>
        /// Recompute <see cref="CardInstance.IsNegated"/> on face-up Traps from
        /// face-up <see cref="EffectActionKind.ContinuousNegateFaceUpTraps"/> sources.
        /// </summary>
        public static void Refresh(DuelEngine engine)
        {
            if (engine?.Player == null || engine.Opponent == null) return;
            foreach (var c in AllFieldCards(engine))
            {
                if (c != null) c.IsNegated = false;
            }

            foreach (var src in FaceUpSources(engine))
            {
                foreach (var clause in ClausesOn(src, EffectActionKind.ContinuousNegateFaceUpTraps))
                {
                    var srcCtrl = engine.ControllerOf(src);
                    var exemptCtrl = HostExemptsControllerTraps(src);
                    foreach (var st in AllSpellTraps(engine))
                    {
                        if (st == null || !st.FaceUp || st.Def == null || !st.Def.IsTrap) continue;
                        if (clause.NegateOtherOnly && st == src) continue;
                        if (exemptCtrl && srcCtrl != null &&
                            engine.ControllerOf(st) == srcCtrl)
                            continue;
                        st.IsNegated = true;
                    }
                }
            }
        }

        /// <summary>
        /// Jinzo first sentence: this Trap cannot start a Chain.
        /// Royal Decree does not set this. Amplifier lets the host's controller activate.
        /// </summary>
        public static bool TrapsCannotActivate(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine == null || who == null || card?.Def == null || !card.Def.IsTrap)
                return false;

            foreach (var src in FaceUpSources(engine))
            {
                if (src == card) continue;
                if (!HasClause(src, EffectActionKind.ContinuousCannotActivateTraps))
                    continue;
                var srcCtrl = engine.ControllerOf(src);
                if (HostExemptsControllerTraps(src) && srcCtrl == who)
                    continue;
                return true;
            }

            return false;
        }

        static bool HostExemptsControllerTraps(CardInstance host)
        {
            if (host?.Equips == null) return false;
            foreach (var eq in host.Equips)
            {
                if (eq?.Def == null || !eq.FaceUp) continue;
                foreach (var c in ClausesOn(eq, EffectActionKind.EquipThisToTarget))
                    if (c.ExemptControllerTrapsFromHostNegation)
                        return true;
                var prog = CompiledEffectCache.GetOrCompile(eq.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClauseList)
                    if (c != null && c.ExemptControllerTrapsFromHostNegation)
                        return true;
            }

            return false;
        }

        static bool HasClause(CardInstance card, EffectActionKind action)
        {
            foreach (var _ in ClausesOn(card, action))
                return true;
            return false;
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null || !card.FaceUp || card.IsNegated) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) yield break;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == action &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
            }
        }

        static IEnumerable<CardInstance> FaceUpSources(DuelEngine engine)
        {
            foreach (var c in AllFieldCards(engine))
                if (c != null && c.FaceUp && c.Def != null)
                    yield return c;
        }

        static IEnumerable<CardInstance> AllSpellTraps(DuelEngine engine)
        {
            if (engine.Player != null)
                foreach (var st in engine.Player.SpellTrapsOnField())
                    if (st != null) yield return st;
            if (engine.Opponent != null)
                foreach (var st in engine.Opponent.SpellTrapsOnField())
                    if (st != null) yield return st;
        }

        static IEnumerable<CardInstance> AllFieldCards(DuelEngine engine)
        {
            foreach (var who in new[] { engine.Player, engine.Opponent })
            {
                if (who == null) continue;
                foreach (var m in who.MonstersOnField())
                    if (m != null) yield return m;
                foreach (var st in who.SpellTrapsOnField())
                    if (st != null) yield return st;
            }
        }
    }
}
