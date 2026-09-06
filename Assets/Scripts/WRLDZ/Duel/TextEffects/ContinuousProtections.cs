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
            {
                if (c.ExceptThisCard) continue;
                if (ConditionMet(engine, c))
                    return true;
            }

            foreach (var src in FaceUpMonsters(engine))
            {
                if (src == null || src.IsNegated || src == monster) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBeAttackTarget))
                {
                    if (!c.ExceptThisCard) continue;
                    // "Except this one" protects the controller's matching
                    // monsters (Marauding Captain), never the other side.
                    if (!SameMonsterController(engine, src, monster)) continue;
                    if (!ConditionMet(engine, c)) continue;
                    if (!string.IsNullOrEmpty(c.RaceFilter))
                    {
                        if (monster.Def?.race == null ||
                            !string.Equals(monster.Def.race, c.RaceFilter,
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    return true;
                }
            }
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

        /// <summary>
        /// Resolution-time guard for an effect that selected a field monster.
        /// </summary>
        public static bool TargetedEffectBlocked(DuelEngine engine, CardInstance target,
            CardInstance source)
        {
            if (target == null) return true;
            return CannotBeTargetedByEffects(engine, target) ||
                   IsUnaffectedBy(engine, target, source);
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

        static bool SameMonsterController(DuelEngine engine, CardInstance first,
            CardInstance second)
        {
            if (engine?.Player == null || engine.Opponent == null ||
                first == null || second == null)
                return false;

            return (engine.Player.TryFindMonster(first, out _) &&
                    engine.Player.TryFindMonster(second, out _)) ||
                   (engine.Opponent.TryFindMonster(first, out _) &&
                    engine.Opponent.TryFindMonster(second, out _));
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
            if (prog == null || !prog.FullyCompiled) yield break;
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

        /// <summary>Necrovalley: neither player can banish cards currently in a GY.</summary>
        public static bool CannotBanishFromGraveyard(DuelEngine engine)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBanishFromGraveyard))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Necrovalley: cards in the GY cannot be targeted, except by ExceptNamedCard
        /// (the effect of "Necrovalley") or a source whose program is unaffected by that name
        /// (Rite of Spirit).
        /// </summary>
        public static bool CannotTargetCardInGy(DuelEngine engine, CardInstance source,
            CardInstance gyCard)
        {
            if (engine == null || gyCard == null) return false;
            foreach (var lockCard in FaceUpSpellTraps(engine))
            {
                if (lockCard == null || lockCard.IsNegated) continue;
                foreach (var c in ClausesOn(lockCard, EffectActionKind.CannotTargetCardsInGraveyard))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (source != null && source == lockCard) continue;
                    if (source != null && !string.IsNullOrEmpty(c.ExceptNamedCard) &&
                        source.IsNamed(c.ExceptNamedCard))
                        continue;
                    if (SourceIgnoresNamedLock(source, lockCard))
                        continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shared Necrovalley leave-GY guard. The current runtime models the official
        /// GY lock through CannotTargetCardInGy, including named exceptions such as Rite
        /// of Spirit; callers use this name for every GY-origin movement resolution.
        /// </summary>
        public static bool GyMovementLocked(DuelEngine engine, CardInstance source,
            CardInstance gyCard) => CannotTargetCardInGy(engine, source, gyCard);


        static bool SourceIgnoresNamedLock(CardInstance source, CardInstance lockCard)
        {
            if (source?.Def == null || lockCard == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || string.IsNullOrEmpty(c.UnaffectedByNamedCard)) continue;
                if (lockCard.IsNamed(c.UnaffectedByNamedCard))
                    return true;
            }
            return false;
        }

        static System.Collections.Generic.IEnumerable<CardInstance> FaceUpSpellTraps(DuelEngine engine)
        {
            var player = engine?.Player;
            if (player != null)
                foreach (var st in player.SpellTrapsOnField())
                    if (st != null && st.FaceUp) yield return st;

            var opponent = engine?.Opponent;
            if (opponent != null)
                foreach (var st in opponent.SpellTrapsOnField())
                    if (st != null && st.FaceUp) yield return st;
        }
    }
}
