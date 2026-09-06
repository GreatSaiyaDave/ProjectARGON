using System;
using System.Collections.Generic;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Official Ritual Summon procedure (Ignis/EDOPro <c>Ritual.AddProcGreaterCode</c> /
    /// <c>AddProcEqual</c>). Tributes from hand or field; destination GY unless the
    /// spell text says otherwise. Special Summons the Ritual Monster from the hand.
    /// </summary>
    public static class RitualProcedures
    {
        public const int BlackLusterRitual = 55761792;
        public const int BlackLusterSoldier = 5405694;
        public const int BlackIllusionRitual = 41426869;
        public const int Relinquished = 64631466;
        public const int EarthChant = 59820352;
        public const int ContractWithTheAbyss = 69035382;

        public struct Spec
        {
            public bool ExactLevel;
            public int FixedLevel;
            public string NamedMonster;
            public string AttributeFilter;
            public bool FromHand;
        }

        public static bool IsRitualSpell(CardDef def) => def != null && def.IsRitualSpell;

        public static bool MonsterHasOfficialSpell(int cardId)
        {
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            var def = db != null ? db.Get(cardId) : null;
            return def != null && def.IsRitualMonster;
        }

        public static bool TryGetSpec(CardDef spell, out Spec spec)
        {
            spec = default;
            if (spell == null || !IsRitualSpell(spell)) return false;
            var prog = CardTextEffectCompiler.Compile(spell);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c != null && c.Action == EffectActionKind.RitualSummon)
                    return TryGetSpec(c, out spec);
            }

            return false;
        }

        public static bool TryGetSpec(EffectClause clause, out Spec spec)
        {
            spec = default;
            if (clause == null || clause.Action != EffectActionKind.RitualSummon)
                return false;
            spec = new Spec
            {
                ExactLevel = clause.RitualExactLevel,
                FixedLevel = clause.Amount,
                NamedMonster = clause.NamedCard,
                AttributeFilter = clause.AttributeFilter,
                FromHand = clause.FromHand || !clause.FromDeck && !clause.FromGrave
            };
            if (!spec.FromHand && !clause.FromDeck && !clause.FromGrave)
                spec.FromHand = true;
            return !string.IsNullOrEmpty(spec.NamedMonster) ||
                   !string.IsNullOrEmpty(spec.AttributeFilter);
        }

        public static bool MonsterMatches(CardDef monster, Spec spec)
        {
            if (monster == null || !monster.IsRitualMonster) return false;
            if (!string.IsNullOrEmpty(spec.NamedMonster))
            {
                return string.Equals(monster.name, spec.NamedMonster, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrEmpty(spec.AttributeFilter))
            {
                return monster.attribute != null &&
                       string.Equals(monster.attribute, spec.AttributeFilter,
                           StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static int RequiredLevel(CardDef monster, Spec spec)
        {
            if (spec.FixedLevel > 0) return spec.FixedLevel;
            return monster != null && monster.level > 0 ? monster.level : 1;
        }

        public static List<CardInstance> CollectTributePool(DuelistState who, CardInstance ritualMonster)
        {
            var list = new List<CardInstance>();
            if (who == null) return list;
            if (who.Hand != null)
            {
                foreach (var c in who.Hand)
                {
                    if (c == null || c == ritualMonster) continue;
                    if (c.Def == null || !c.Def.IsMonster || c.Level < 1) continue;
                    list.Add(c);
                }
            }

            foreach (var m in who.MonstersOnField())
            {
                if (m == null || m == ritualMonster) continue;
                if (m.Def == null || !m.Def.IsMonster || m.Level < 1) continue;
                list.Add(m);
            }

            return list;
        }

        /// <summary>
        /// Lowest-level legal tribute set: minimum total Level that satisfies Greater/Equal,
        /// then fewest cards. Deterministic for tests (no UI).
        /// </summary>
        public static List<CardInstance> AutoPickTributes(IList<CardInstance> pool, int required,
            bool exact)
        {
            if (pool == null || required < 1) return null;
            var cards = pool
                .Where(c => c?.Def != null && c.Def.IsMonster && c.Level >= 1)
                .OrderBy(c => c.Level)
                .ThenBy(c => c.InstanceId)
                .ToList();
            if (cards.Count == 0) return null;

            List<CardInstance> best = null;
            var bestSum = int.MaxValue;
            var bestCount = int.MaxValue;
            var chosen = new List<CardInstance>(cards.Count);

            void Search(int i, int sum)
            {
                if (exact)
                {
                    if (sum == required && chosen.Count > 0)
                    {
                        if (sum < bestSum || (sum == bestSum && chosen.Count < bestCount))
                        {
                            best = chosen.ToList();
                            bestSum = sum;
                            bestCount = chosen.Count;
                        }

                        return;
                    }

                    if (sum >= required || i >= cards.Count) return;
                }
                else
                {
                    if (sum >= required && chosen.Count > 0)
                    {
                        if (sum < bestSum || (sum == bestSum && chosen.Count < bestCount))
                        {
                            best = chosen.ToList();
                            bestSum = sum;
                            bestCount = chosen.Count;
                        }

                        return;
                    }

                    if (i >= cards.Count) return;
                }

                Search(i + 1, sum);
                chosen.Add(cards[i]);
                Search(i + 1, sum + cards[i].Level);
                chosen.RemoveAt(chosen.Count - 1);
            }

            Search(0, 0);
            return best;
        }

        public static bool LevelsLegal(IList<CardInstance> tributes, int required, bool exact)
        {
            if (tributes == null || tributes.Count == 0 || required < 1) return false;
            var sum = 0;
            foreach (var t in tributes)
            {
                if (t?.Def == null || !t.Def.IsMonster || t.Level < 1) return false;
                sum += t.Level;
            }

            return exact ? sum == required : sum >= required;
        }

        public static List<CardInstance> ResolveTributeSelection(DuelEngine engine, DuelistState who,
            CardInstance ritualMonster, IList<CardInstance> requested, int required, bool exact)
        {
            var pool = CollectTributePool(who, ritualMonster);
            if (requested != null && requested.Count > 0)
            {
                var chosen = new List<CardInstance>();
                foreach (var t in requested)
                {
                    if (t == null || !pool.Contains(t)) continue;
                    if (!chosen.Contains(t)) chosen.Add(t);
                }

                if (LevelsLegal(chosen, required, exact))
                    return chosen;
                if (!exact && chosen.Count > 0)
                {
                    var remaining = pool.Where(c => !chosen.Contains(c)).ToList();
                    var extra = AutoPickTributes(remaining, required - chosen.Sum(c => c.Level),
                        exact: false);
                    if (extra != null)
                    {
                        chosen.AddRange(extra);
                        if (LevelsLegal(chosen, required, exact: false))
                            return chosen;
                    }
                }

                if (exact && chosen.Count > 0)
                {
                    var remaining = pool.Where(c => !chosen.Contains(c)).ToList();
                    var extra = AutoPickTributes(remaining, required - chosen.Sum(c => c.Level),
                        exact: true);
                    if (extra != null)
                    {
                        chosen.AddRange(extra);
                        if (LevelsLegal(chosen, required, exact: true))
                            return chosen;
                    }
                }

                return null;
            }

            if (engine?.PendingTributes != null && engine.PendingTributes.Count > 0)
            {
                var pending = engine.PendingTributes
                    .Where(t => t != null && pool.Contains(t))
                    .Distinct()
                    .ToList();
                if (pending.Count > 0)
                {
                    var filled = ResolveTributeSelection(engine, who, ritualMonster, pending,
                        required, exact);
                    if (filled != null) return filled;
                }
            }

            return AutoPickTributes(pool, required, exact);
        }

        public static CardInstance FindRitualMonsterInHand(DuelistState who, Spec spec)
        {
            if (who?.Hand == null) return null;
            foreach (var c in who.Hand)
            {
                if (c?.Def != null && MonsterMatches(c.Def, spec))
                    return c;
            }

            return null;
        }

        public static bool CanPerform(DuelEngine engine, DuelistState who, CardInstance spell,
            CardInstance ritualMonster, IList<CardInstance> tributes, out string reason)
        {
            reason = "Ritual procedure not registered.";
            if (engine == null || who == null || spell?.Def == null)
            {
                reason = "Invalid ritual context.";
                return false;
            }

            if (!TryGetSpec(spell.Def, out var spec))
            {
                reason = "Ritual procedure not registered.";
                return false;
            }

            if (ritualMonster == null)
                ritualMonster = FindRitualMonsterInHand(who, spec);
            if (ritualMonster?.Def == null || !MonsterMatches(ritualMonster.Def, spec))
            {
                reason = "No matching Ritual Monster in hand.";
                return false;
            }

            if (!who.Hand.Contains(ritualMonster))
            {
                reason = "Ritual Monster must be in hand.";
                return false;
            }

            var required = RequiredLevel(ritualMonster.Def, spec);
            var chosen = ResolveTributeSelection(engine, who, ritualMonster, tributes, required,
                spec.ExactLevel);
            if (chosen == null || !LevelsLegal(chosen, required, spec.ExactLevel))
            {
                reason = spec.ExactLevel
                    ? $"Need tributes whose Levels exactly equal {required}."
                    : $"Need tributes whose Levels sum to {required} or more.";
                return false;
            }

            var fieldTributes = 0;
            foreach (var t in chosen)
                if (who.TryFindMonster(t, out _))
                    fieldTributes++;
            if (engine.FirstEmpty(who.MonsterZones) < 0 && fieldTributes == 0)
            {
                reason = "No free Monster Zone.";
                return false;
            }

            reason = "OK";
            return true;
        }

        public static bool Validate(DuelEngine engine, DuelistState who, CardInstance ritualMonster,
            CardInstance spell, List<CardInstance> tributes, out string reason) =>
            CanPerform(engine, who, spell, ritualMonster, tributes, out reason);

        public static bool TryResolve(DuelEngine engine, DuelistState who, CardInstance spell)
        {
            if (!CanPerform(engine, who, spell, null, null, out var reason))
            {
                engine?.Log($"Ritual Summon failed: {reason}");
                return false;
            }

            if (!TryGetSpec(spell.Def, out var spec)) return false;
            var monster = FindRitualMonsterInHand(who, spec);
            if (monster == null) return false;
            var required = RequiredLevel(monster.Def, spec);
            var tributes = ResolveTributeSelection(engine, who, monster, null, required,
                spec.ExactLevel);
            if (tributes == null || !LevelsLegal(tributes, required, spec.ExactLevel))
                return false;

            // Fail-closed: do not move tributes unless a zone will exist after field tributes.
            var fieldTributes = 0;
            foreach (var t in tributes)
                if (who.TryFindMonster(t, out _))
                    fieldTributes++;
            if (engine.FirstEmpty(who.MonsterZones) < 0 && fieldTributes == 0)
            {
                engine.Log("Ritual Summon failed — no Monster Zone.");
                return false;
            }

            foreach (var t in tributes.ToList())
            {
                var fromHand = who.Hand.Contains(t);
                engine.SendCardToGrave(who, t);
                engine.Log(fromHand
                    ? $"Ritual Tribute: {t.Name} (from hand) → GY"
                    : $"Ritual Tribute: {t.Name} (from field) → GY");
            }

            engine.PendingTributes.Clear();
            who.Hand.Remove(monster);
            if (!engine.SpecialSummonToField(who, monster, BattlePosition.Attack, faceUp: true, summonKind: SummonKind.RitualSummon))
            {
                who.Hand.Add(monster);
                engine.Log("Ritual Summon failed — no Monster Zone.");
                return false;
            }

            engine.Log($"Ritual Summon! {monster.Name}!");
            return true;
        }
    }
}
