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

            // Soul Exchange: an opponent's face-up target is a Tribute as if you controlled it (and a must);
            // ygopro GetRitualMaterial only lists an opponent's EXTRA_RELEASE monster while it is face-up.
            TributeSummonsRuntime.IncludeForced(who, list,
                m => m != ritualMonster && m.FaceUp && m.Def != null && m.Def.IsMonster && m.Level >= 1);
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

        /// <summary>
        /// A legal Ritual Tribute set chosen by a player: Levels total exactly <paramref name="required"/> (exact),
        /// or at least it with no unnecessary Tribute — removing any one card would drop below the requirement
        /// (TCG: "you cannot Tribute more monsters than needed").
        /// </summary>
        public static bool IsLegalTributeSet(IList<CardInstance> set, int required, bool exact)
        {
            if (set == null || set.Count == 0 || required < 1) return false;
            var sum = 0;
            var min = int.MaxValue;
            foreach (var t in set)
            {
                if (t?.Def == null || !t.Def.IsMonster || t.Level < 1) return false;
                sum += t.Level;
                min = Math.Min(min, t.Level);
            }

            return exact ? sum == required : sum >= required && sum - min < required;
        }

        /// <summary>Can <paramref name="picks"/> still grow, from <paramref name="pool"/>, into a legal set?</summary>
        public static bool CanComplete(IList<CardInstance> picks, IList<CardInstance> pool, int required, bool exact)
        {
            if (pool == null || required < 1) return false;
            var chosen = picks?.Where(c => c != null).ToList() ?? new List<CardInstance>();
            if (chosen.Count > 0 && IsLegalTributeSet(chosen, required, exact)) return true;
            var sum = 0;
            var min = int.MaxValue;
            foreach (var c in chosen)
            {
                if (c?.Def == null || !c.Def.IsMonster || c.Level < 1) return false;
                sum += c.Level;
                min = Math.Min(min, c.Level);
            }

            // At or over the requirement without being legal: over (exact) or an unnecessary Tribute
            // (greater) — adding cards cannot fix either.
            if (sum >= required) return false;
            var rest = pool
                .Where(c => c?.Def != null && c.Def.IsMonster && c.Level >= 1 && !chosen.Contains(c))
                .OrderBy(c => c.Level)
                .ToList();

            bool Dfs(int from, int s, int lo)
            {
                for (var k = from; k < rest.Count; k++)
                {
                    var lvl = rest[k].Level;
                    var s2 = s + lvl;
                    var lo2 = Math.Min(lo, lvl);
                    if (exact)
                    {
                        if (s2 == required) return true;
                        if (s2 > required) return false; // ascending: every later card overshoots too
                        if (Dfs(k + 1, s2, lo2)) return true;
                    }
                    else if (s2 >= required)
                    {
                        if (s2 - lo2 < required) return true;
                    }
                    else if (Dfs(k + 1, s2, lo2))
                        return true;
                }

                return false;
            }

            return Dfs(0, sum, min);
        }

        /// <summary>Pool cards the player may pick next: each keeps a legal completion possible.</summary>
        public static List<CardInstance> SelectableTributes(IList<CardInstance> picks, IList<CardInstance> pool,
            int required, bool exact)
        {
            var list = new List<CardInstance>();
            if (pool == null) return list;
            var chosen = picks?.Where(c => c != null).ToList() ?? new List<CardInstance>();
            foreach (var c in pool)
            {
                if (c == null || chosen.Contains(c)) continue;
                chosen.Add(c);
                if (CanComplete(chosen, pool, required, exact)) list.Add(c);
                chosen.RemoveAt(chosen.Count - 1);
            }

            return list;
        }

        /// <summary>
        /// The distinct legal Tribute sets (identical copies in the same place count once), up to
        /// <paramref name="max"/>. Two or more means the player has a real choice.
        /// </summary>
        public static List<List<CardInstance>> DistinctLegalSets(DuelistState who, IList<CardInstance> pool,
            int required, bool exact, int max = 2)
        {
            var found = new List<List<CardInstance>>();
            var seen = new HashSet<string>();
            if (pool == null || required < 1) return found;
            var cards = pool.Where(c => c?.Def != null && c.Def.IsMonster && c.Level >= 1)
                .OrderBy(c => c.Level).ThenBy(c => c.InstanceId).ToList();
            var acc = new List<CardInstance>();

            string Signature(List<CardInstance> set) => string.Join("|", set
                .Select(c => $"{c.CardId}@{(who != null && who.Hand.Contains(c) ? "H" : "F")}")
                .OrderBy(s => s, StringComparer.Ordinal));

            void Dfs(int from, int s, int lo)
            {
                if (found.Count >= max) return;
                for (var k = from; k < cards.Count && found.Count < max; k++)
                {
                    var lvl = cards[k].Level;
                    var s2 = s + lvl;
                    var lo2 = Math.Min(lo, lvl);
                    acc.Add(cards[k]);
                    if (s2 >= required)
                    {
                        if (IsLegalTributeSet(acc, required, exact) && seen.Add(Signature(acc)))
                            found.Add(acc.ToList());
                    }
                    else
                        Dfs(k + 1, s2, lo2);
                    acc.RemoveAt(acc.Count - 1);
                    if (exact && s2 > required) break; // ascending
                }
            }

            Dfs(0, 0, int.MaxValue);
            return found;
        }

        /// <summary>Distinct matching Ritual Monsters in hand (one per card name) that can be Ritual Summoned now.</summary>
        public static List<CardInstance> PerformableRitualMonsters(DuelEngine engine, DuelistState who, Spec spec)
        {
            var list = new List<CardInstance>();
            if (who?.Hand == null) return list;
            var names = new HashSet<int>();
            foreach (var c in who.Hand)
            {
                if (c?.Def == null || !MonsterMatches(c.Def, spec) || !names.Add(c.CardId)) continue;
                var pool = CollectTributePool(who, c);
                if (DistinctLegalSets(who, pool, RequiredLevel(c.Def, spec), spec.ExactLevel, 1).Count > 0)
                    list.Add(c);
            }

            return list;
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
            // Soul Exchange: with forced Tributes in the pool, they are chosen first.
            var forced = TributeSummonsRuntime.ForcedTributes(who).Where(pool.Contains).ToList();
            if (forced.Count > 0)
                return TributeSummonsRuntime.RitualTributesWithForced(engine, pool, forced, requested,
                    required, exact);
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
            {
                // Any matching Ritual Monster in hand that can be Ritual Summoned makes it legal (the player
                // picks which one when there are several).
                var performable = PerformableRitualMonsters(engine, who, spec);
                ritualMonster = performable.Count > 0 ? performable[0] : FindRitualMonsterInHand(who, spec);
            }
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

            // Mask of Restrict Soft ContinuousProtections: neither player can Tribute (Ritual).
            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                reason = "Neither player can Tribute cards (Mask of Restrict).";
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
            // ZoneCapsAndPhaseWindows batch: the usable-zone caps count too (MonsterZoneAfterLeaving).
            if (!engine.MonsterZoneAfterLeaving(who, fieldTributes))
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
            // What the player chose while activating (TextEffectRuntime ritual choice), used once.
            var chosenMonster = spell?.ChosenRitualMonster;
            var chosenTributes = spell?.ChosenRitualTributes;
            if (spell != null)
            {
                spell.ChosenRitualMonster = null;
                spell.ChosenRitualTributes = null;
            }

            if (!TryGetSpec(spell?.Def, out var spec))
            {
                engine?.Log("Ritual Summon failed: Ritual procedure not registered.");
                return false;
            }

            if (chosenMonster != null &&
                (who?.Hand == null || !who.Hand.Contains(chosenMonster) || !MonsterMatches(chosenMonster.Def, spec)))
            {
                engine?.Log($"Ritual Summon: {chosenMonster.Name} is no longer in the hand.");
                chosenMonster = null;
                chosenTributes = null;
            }

            if (!CanPerform(engine, who, spell, chosenMonster, null, out var reason))
            {
                engine?.Log($"Ritual Summon failed: {reason}");
                return false;
            }

            var monster = chosenMonster;
            if (monster == null)
            {
                var performable = PerformableRitualMonsters(engine, who, spec);
                monster = performable.Count > 0 ? performable[0] : FindRitualMonsterInHand(who, spec);
            }
            if (monster == null) return false;
            // Mask of Restrict Soft ContinuousProtections: neither player can Tribute (Ritual).
            if (ContinuousProtections.NeitherPlayerCanTribute(engine))
            {
                engine?.Log("Ritual Summon failed: Neither player can Tribute cards (Mask of Restrict).");
                return false;
            }
            var required = RequiredLevel(monster.Def, spec);
            List<CardInstance> tributes = null;
            if (chosenTributes != null && chosenTributes.Count > 0)
            {
                var pool = CollectTributePool(who, monster);
                if (chosenTributes.All(pool.Contains) &&
                    IsLegalTributeSet(chosenTributes, required, spec.ExactLevel))
                    tributes = chosenTributes.ToList();
                else
                    engine.Log("Ritual Summon: the chosen Tributes are no longer all available — " +
                               "using the best legal Tributes instead.");
            }

            tributes ??= ResolveTributeSelection(engine, who, monster, null, required, spec.ExactLevel);
            if (tributes == null || !LevelsLegal(tributes, required, spec.ExactLevel))
                return false;

            // Fail-closed: do not move tributes unless a zone will exist after field tributes.
            var fieldTributes = 0;
            foreach (var t in tributes)
                if (who.TryFindMonster(t, out _))
                    fieldTributes++;
            // ZoneCapsAndPhaseWindows batch: the usable-zone caps count too (MonsterZoneAfterLeaving).
            if (!engine.MonsterZoneAfterLeaving(who, fieldTributes))
            {
                engine.Log("Ritual Summon failed — no Monster Zone.");
                return false;
            }
            // RuleLocks batch: Chain Energy — the Special Summon toll of a Ritual Monster in the hand, checked before
            // any Tribute is sent.
            var ritualFromHand = who.Hand.Contains(monster);
            if (ritualFromHand && TextEffects.RuleLocksRuntime.HandSpecialSummonUnaffordable(engine, who))
            {
                engine.Log($"Ritual Summon failed — {TextEffects.RuleLocksRuntime.HandActionLpCost(engine, who)} LP " +
                           "must be paid to Special Summon from the hand.");
                return false;
            }

            // The Tributes' "sent to the GY" triggers activate after the Ritual Summon (FlipMassEffects v142).
            var summoned = false;
            TextEffects.FlipMassEffectsRuntime.Simultaneously(engine, () =>
            {
                foreach (var t in tributes.ToList())
                {
                    var fromHand = who.Hand.Contains(t);
                    TextEffects.ReleaseEvents.Record(engine, t, who); // EngineHygiene: EVENT_RELEASE
                    // A Soul Exchange target leaves the opponent's field.
                    engine.SendCardToGrave(engine.ControllerOf(t) ?? who, t);
                    engine.Log(fromHand
                        ? $"Ritual Tribute: {t.Name} (from hand) → GY"
                        : $"Ritual Tribute: {t.Name} (from field) → GY");
                }

                engine.PendingTributes.Clear();
                who.Hand.Remove(monster);
                if (ritualFromHand) engine.MarkSpecialSummonFromHand(monster); // Chain Energy toll (RuleLocks batch)
                summoned = engine.SpecialSummonToField(who, monster, BattlePosition.Attack, faceUp: true,
                    summonKind: SummonKind.RitualSummon);
                if (!summoned)
                    who.Hand.Add(monster);
            });
            if (!summoned)
            {
                engine.Log("Ritual Summon failed — no Monster Zone.");
                return false;
            }

            engine.Log($"Ritual Summon! {monster.Name}!");
            return true;
        }
    }
}
