using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Runtime for shared protection kinds: unaffected-by, cannot-be-attack-target,
    /// cannot-be-targeted-by-effects. Reads compiled programs — no cardId branches.
    /// </summary>
    public static class ContinuousProtections
    {
        public const int LordOfD = 17985575;

        public static bool CannotBeAttackTarget(DuelEngine engine, CardInstance monster)
        {
            if (monster == null || !monster.FaceUp || monster.IsNegated) return false;
            foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeAttackTarget))
                if (ConditionMet(engine, c))
                    return true;
            return false;
        }

        /// <summary>
        /// True when every monster the opponent controls cannot be attack-targeted
        /// and at least one of them allows a direct attack (Fisherman / Kay'est).
        /// </summary>
        public static bool AllOpponentMonstersAllowDirect(DuelEngine engine, DuelistState opp)
        {
            if (opp == null) return false;
            var any = false;
            var allows = false;
            foreach (var m in opp.MonstersOnField())
            {
                if (m == null) continue;
                any = true;
                if (!CannotBeAttackTarget(engine, m))
                    return false;
                if (AllowsDirectWhileProtected(engine, m))
                    allows = true;
            }

            return any && allows;
        }

        public static bool AllowsDirectWhileProtected(DuelEngine engine, CardInstance monster)
        {
            if (monster == null || !monster.FaceUp) return false;
            foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeAttackTarget))
                if (ConditionMet(engine, c) && c.AllowsDirectAttackWhileProtected)
                    return true;
            return false;
        }

        public static bool IsUnaffectedBy(DuelEngine engine, CardInstance victim, CardInstance source)
        {
            if (victim == null || source?.Def == null) return false;
            if (!victim.FaceUp || victim.IsNegated) return false;
            foreach (var c in ClausesOn(victim, EffectActionKind.UnaffectedByCardEffects))
            {
                if (!ConditionMet(engine, c)) continue;
                if (FilterMatches(c.UnaffectedByFilter, source.Def))
                    return true;
            }

            return false;
        }

        public static bool CannotBeTargetedByEffects(DuelEngine engine, CardInstance monster)
        {
            if (engine == null || monster?.Def == null || !monster.FaceUp) return false;

            foreach (var c in ClausesOn(monster, EffectActionKind.CannotBeTargetedByEffects))
            {
                if (!ConditionMet(engine, c)) continue;
                if (string.IsNullOrEmpty(c.NamedCard) && string.IsNullOrEmpty(c.RaceFilter))
                    return true;
            }

            if (IsDragon(monster) && LordOfDFaceUp(engine))
                return true;

            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBeTargetedByEffects))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (!string.IsNullOrEmpty(c.NamedCard) && monster.IsNamed(c.NamedCard))
                        return true;
                    if (!string.IsNullOrEmpty(c.RaceFilter) && MatchesRace(monster, c.RaceFilter))
                        return true;
                }

                foreach (var c in ClausesOn(src, EffectActionKind.ContinuousCannotTargetDragons))
                {
                    if (ConditionMet(engine, c) && IsDragon(monster))
                        return true;
                }
            }

            return false;
        }

        static bool ConditionMet(DuelEngine engine, EffectClause c)
        {
            if (c == null) return false;
            if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                return false;
            return true;
        }

        static bool FilterMatches(string filter, Data.CardDef def)
        {
            if (def == null) return false;
            if (string.IsNullOrEmpty(filter) ||
                filter.Equals("all", StringComparison.OrdinalIgnoreCase))
                return true;
            if (filter.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                return def.IsSpell && !def.IsTrap;
            if (filter.Equals("Trap", StringComparison.OrdinalIgnoreCase))
                return def.IsTrap;
            if (filter.Equals("Monster", StringComparison.OrdinalIgnoreCase))
                return def.IsMonster;
            if (filter.Equals("SpellTrap", StringComparison.OrdinalIgnoreCase))
                return def.IsSpell || def.IsTrap;
            return false;
        }

        static IEnumerable<EffectClause> ClausesOn(CardInstance card, EffectActionKind action)
        {
            if (card?.Def == null) yield break;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null) yield break;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == action &&
                    c.Timing == EffectTiming.ContinuousWhileFaceUp)
                    yield return c;
            }
        }

        static IEnumerable<CardInstance> FaceUpMonsters(DuelEngine engine)
        {
            if (engine?.Player != null)
                foreach (var m in engine.Player.MonstersOnField())
                    if (m != null && m.FaceUp) yield return m;
            if (engine?.Opponent != null)
                foreach (var m in engine.Opponent.MonstersOnField())
                    if (m != null && m.FaceUp) yield return m;
        }

        static bool LordOfDFaceUp(DuelEngine engine)
        {
            foreach (var m in FaceUpMonsters(engine))
                if (m.CardId == LordOfD && !m.IsNegated) return true;
            return false;
        }

        static bool IsDragon(CardInstance m) =>
            m?.Def?.race != null &&
            m.Def.race.IndexOf("Dragon", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool MatchesRace(CardInstance m, string race) =>
            m?.Def?.race != null &&
            !string.IsNullOrEmpty(race) &&
            m.Def.race.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
