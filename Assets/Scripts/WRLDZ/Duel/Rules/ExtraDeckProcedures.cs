using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Generic pre-Link Extra Deck procedures and the Pendulum Summon.
    ///
    /// These are <b>structural</b> rules (Rulebook / Master Rule 3), not per-card scripts:
    /// <list type="bullet">
    /// <item>Synchro: exactly the printed Tuner count plus at least the printed non-Tuner count, whose
    /// Levels total the Synchro Monster's Level; every material is a face-up monster you control.</item>
    /// <item>Xyz: the printed number of monsters, all of one Level (the printed "Level N", else all equal),
    /// which become Xyz Material instead of going to the Graveyard.</item>
    /// <item>Pendulum: any number of monsters from the hand whose Level is strictly between the two Scales,
    /// plus face-up Pendulum Monsters from the Extra Deck, once per turn in your Main Phase; a Pendulum
    /// Monster that would be sent from the field to the Graveyard is placed face-up in the Extra Deck instead.</item>
    /// </list>
    ///
    /// Material <i>count</i> and <i>Level</i> constraints are read from the printed material line. Attribute /
    /// Type qualifiers and archetype-fragment names are not enforced yet (documented limitation); an exact
    /// printed card name in quotes is required when the database holds a card with that name.
    /// </summary>
    public static class ExtraDeckProcedures
    {
        public struct SynchroRequirement
        {
            public int Tuners;
            public int MinNonTuners;
        }

        public struct XyzRequirement
        {
            public int Count;
            public int Rank;
            /// <summary>Allowed material Levels when the text prints them ("Level 4", "Level 5 or 6"); empty = any shared Level.</summary>
            public List<int> AllowedLevels;
        }

        /// <summary>The first line of the official text: the material line of an Extra Deck Monster.</summary>
        public static string MaterialLine(CardDef def)
        {
            var text = OfficialCardAuthority.OfficialText(def);
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var nl = text.IndexOf('\n');
            var line = nl >= 0 ? text.Substring(0, nl) : text;
            return line.Trim();
        }

        // ───────────────────────────── Synchro ─────────────────────────────

        public static SynchroRequirement ParseSynchro(string materialLine)
        {
            var req = new SynchroRequirement { Tuners = 1, MinNonTuners = 1 };
            if (string.IsNullOrEmpty(materialLine)) return req;

            // "2 Tuners + …" / "1 Tuner Synchro Monster + …". The pattern cannot pick "non-Tuner"
            // (letters sit between the digit and "Tuner"), so any match is a real Tuner count.
            var t = Regex.Match(materialLine, @"(\d+)\s+Tuners?\b", RegexOptions.IgnoreCase);
            if (t.Success && int.TryParse(t.Groups[1].Value, out var tuners) && tuners > 0)
                req.Tuners = tuners;

            var n = Regex.Match(materialLine, @"(\d+)\s+or more non-Tuner", RegexOptions.IgnoreCase);
            if (n.Success && int.TryParse(n.Groups[1].Value, out var minNon) && minNon > 0)
                req.MinNonTuners = minNon;

            return req;
        }

        public static bool ValidateSynchro(DuelEngine engine, DuelistState who, CardInstance synchro,
            List<CardInstance> materials, out string reason)
        {
            reason = "OK";
            if (engine == null || who == null || synchro?.Def == null || materials == null)
            {
                reason = "Invalid Synchro Summon context.";
                return false;
            }

            if (!synchro.Def.IsSynchroMonster)
            {
                reason = "Not a Synchro Monster.";
                return false;
            }

            if (!SummonWindowOpen(engine, who, "Synchro Summon", out reason))
                return false;

            if (!who.ExtraDeck.Contains(synchro.CardId))
            {
                reason = "The Synchro Monster is not in your Extra Deck.";
                return false;
            }

            if (materials.Count == 0)
            {
                reason = "No Synchro Materials selected.";
                return false;
            }

            if (materials.Distinct().Count() != materials.Count)
            {
                reason = "A monster cannot be used twice as Synchro Material.";
                return false;
            }

            foreach (var m in materials)
            {
                if (m?.Def == null || !who.TryFindMonster(m, out _) || !m.FaceUp)
                {
                    reason = "Synchro Materials must be face-up monsters you control.";
                    return false;
                }
            }

            var req = ParseSynchro(MaterialLine(synchro.Def));
            int tuners = materials.Count(m => m.Def.IsTuner);
            int nonTuners = materials.Count - tuners;
            int sum = materials.Sum(m => m.Level);

            if (tuners != req.Tuners)
            {
                reason = $"Needs exactly {req.Tuners} Tuner Material(s).";
                return false;
            }

            if (nonTuners < req.MinNonTuners)
            {
                reason = $"Needs at least {req.MinNonTuners} non-Tuner Material(s).";
                return false;
            }

            if (sum != synchro.Level)
            {
                reason = $"Material Levels must total {synchro.Level} (they total {sum}).";
                return false;
            }

            if (!NamedRequirementsMet(engine, MaterialLine(synchro.Def), materials, out var namedReason))
            {
                reason = namedReason;
                return false;
            }

            return true;
        }

        // ─────────────────────────────── Xyz ───────────────────────────────

        public static XyzRequirement ParseXyz(string materialLine, int rank)
        {
            var req = new XyzRequirement { Count = 2, Rank = rank, AllowedLevels = new List<int>() };
            if (string.IsNullOrEmpty(materialLine)) return req;

            var c = Regex.Match(materialLine, @"^\s*(\d+)");
            if (c.Success && int.TryParse(c.Groups[1].Value, out var count) && count >= 1)
                req.Count = count;

            foreach (Match m in Regex.Matches(materialLine, @"Level\s+(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[1].Value, out var lvl) && lvl > 0 && !req.AllowedLevels.Contains(lvl))
                    req.AllowedLevels.Add(lvl);
            }

            // "or lower"/"or higher" makes the printed number a bound, not a set: leave the set empty
            // (all materials must still share one Level).
            if (Regex.IsMatch(materialLine, @"Level\s+\d+\s+or\s+(lower|higher)", RegexOptions.IgnoreCase))
                req.AllowedLevels.Clear();

            return req;
        }

        public static bool ValidateXyz(DuelEngine engine, DuelistState who, CardInstance xyz,
            List<CardInstance> materials, out string reason)
        {
            reason = "OK";
            if (engine == null || who == null || xyz?.Def == null || materials == null)
            {
                reason = "Invalid Xyz Summon context.";
                return false;
            }

            if (!xyz.Def.IsXyzMonster)
            {
                reason = "Not an Xyz Monster.";
                return false;
            }

            if (!SummonWindowOpen(engine, who, "Xyz Summon", out reason))
                return false;

            if (!who.ExtraDeck.Contains(xyz.CardId))
            {
                reason = "The Xyz Monster is not in your Extra Deck.";
                return false;
            }

            if (materials.Count == 0)
            {
                reason = "No Xyz Materials selected.";
                return false;
            }

            if (materials.Distinct().Count() != materials.Count)
            {
                reason = "A monster cannot be used twice as Xyz Material.";
                return false;
            }

            foreach (var m in materials)
            {
                if (m?.Def == null || !who.TryFindMonster(m, out _) || !m.FaceUp)
                {
                    reason = "Xyz Materials must be face-up monsters you control.";
                    return false;
                }
            }

            var req = ParseXyz(MaterialLine(xyz.Def), xyz.Level);
            if (materials.Count != req.Count)
            {
                reason = $"Needs exactly {req.Count} Xyz Material(s).";
                return false;
            }

            var levels = materials.Select(m => m.Level).ToList();
            if (levels.Distinct().Count() != 1)
            {
                reason = "Xyz Materials must all be the same Level.";
                return false;
            }

            var sharedLevel = levels[0];
            if (req.AllowedLevels.Count > 0 && !req.AllowedLevels.Contains(sharedLevel))
            {
                reason = $"Xyz Materials must be {string.Join(" or ", req.AllowedLevels.Select(l => "Level " + l))}.";
                return false;
            }

            if (!NamedRequirementsMet(engine, MaterialLine(xyz.Def), materials, out var namedReason))
            {
                reason = namedReason;
                return false;
            }

            return true;
        }

        // ───────────────────────────── Pendulum ─────────────────────────────

        /// <summary>Reads both Scales; false when either Pendulum Zone is empty.</summary>
        public static bool TryGetScales(DuelistState who, out int low, out int high, out string reason)
        {
            low = high = 0;
            reason = "Both Pendulum Scales must be set.";
            var zones = who?.PendulumZones;
            if (zones == null || zones.Length < 2 || zones[0]?.Occupant == null || zones[1]?.Occupant == null)
                return false;

            var left = zones[0].Occupant.Def?.PendulumScaleLeft ?? 0;
            var right = zones[1].Occupant.Def?.PendulumScaleRight ?? 0;
            if (left <= 0 || right <= 0)
            {
                reason = "A placed Pendulum Scale has no printed Scale value.";
                return false;
            }

            low = Math.Min(left, right);
            high = Math.Max(left, right);
            reason = "OK";
            return true;
        }

        public static bool ValidatePendulumSummon(DuelEngine engine, DuelistState who,
            List<CardInstance> toSummon, out string reason)
        {
            reason = "OK";
            if (engine == null || who == null)
            {
                reason = "Invalid Pendulum Summon context.";
                return false;
            }

            if (!SummonWindowOpen(engine, who, "Pendulum Summon", out reason))
                return false;

            if (who.PendulumSummonedThisTurn)
            {
                reason = "You already Pendulum Summoned this turn.";
                return false;
            }

            if (!TryGetScales(who, out var low, out var high, out reason))
                return false;

            if (toSummon == null || toSummon.Count == 0)
            {
                reason = "No monsters selected for the Pendulum Summon.";
                return false;
            }

            if (toSummon.Distinct().Count() != toSummon.Count)
            {
                reason = "A card cannot be Pendulum Summoned twice.";
                return false;
            }

            foreach (var c in toSummon)
            {
                if (c?.Def == null || !c.Def.IsMonster)
                {
                    reason = "Only monsters can be Pendulum Summoned.";
                    return false;
                }

                var fromHand = who.Hand.Contains(c);
                var fromExtra = who.ExtraDeckFaceUp.Contains(c);
                if (!fromHand && !fromExtra)
                {
                    reason = "Selected monsters must be in your hand or your face-up Extra Deck.";
                    return false;
                }

                // From the hand: any monster. From the Extra Deck: only a face-up Pendulum Monster.
                if (fromExtra && !c.Def.IsPendulumMonster)
                {
                    reason = "Only Pendulum Monsters can be Pendulum Summoned from the Extra Deck.";
                    return false;
                }

                if (c.Level <= low || c.Level >= high)
                {
                    reason = $"Level {c.Level} is not between the Pendulum Scales ({low} and {high}).";
                    return false;
                }
            }

            return true;
        }

        /// <summary>All candidate Levels for a Pendulum Summon right now (hand monsters + face-up Extra Deck Pendulum Monsters).</summary>
        public static List<CardInstance> PendulumSummonCandidates(DuelistState who)
        {
            var result = new List<CardInstance>();
            if (who == null) return result;
            if (!TryGetScales(who, out var low, out var high, out _)) return result;

            foreach (var c in who.Hand)
                if (c?.Def != null && c.Def.IsMonster && c.Level > low && c.Level < high)
                    result.Add(c);

            foreach (var c in who.ExtraDeckFaceUp)
                if (c?.Def != null && c.Def.IsPendulumMonster && c.Level > low && c.Level < high)
                    result.Add(c);

            return result;
        }

        // ───────────────────── legal material-set finders ─────────────────────

        /// <summary>
        /// A legal Synchro material set for <paramref name="synchro"/>, or null when none exists. Greedy:
        /// the printed Tuner count first, then as few non-Tuners as reach the printed Level total. Selection UI
        /// may pass any set it likes; this is the "one-tap" / AI answer.
        /// </summary>
        public static List<CardInstance> FindSynchroMaterials(DuelistState who, CardDef synchro)
        {
            if (who == null || synchro == null) return null;
            var pool = FieldFaceUpMonsters(who);
            if (pool.Count == 0) return null;

            var req = ParseSynchro(MaterialLine(synchro));
            var tuners = pool.Where(m => m.Def.IsTuner).ToList();
            if (tuners.Count < req.Tuners) return null;

            // Exactly the printed Tuner count (Synchro Monsters print an exact Tuner count).
            var chosen = tuners.GetRange(0, req.Tuners);
            var nonTuners = pool.Where(m => !m.Def.IsTuner).ToList();
            if (nonTuners.Count < req.MinNonTuners) return null;

            // Add the cheapest non-Tuners that reach the Level total (else fail the Level sum).
            var picked = new List<CardInstance>(nonTuners);
            while (picked.Count >= req.MinNonTuners)
            {
                var candidate = new List<CardInstance>(chosen);
                candidate.AddRange(picked);
                if (SumLevels(candidate) == synchro.level)
                    return candidate;
                // Drop the highest-Level non-Tuner and retry.
                var drop = picked.Aggregate((a, b) => a.Level >= b.Level ? a : b);
                picked.Remove(drop);
            }

            return null;
        }

        /// <summary>
        /// A legal Xyz material set for <paramref name="xyz"/>, or null when none exists. Greedy: the printed
        /// number of same-Level monsters (preferring the lowest Level present).
        /// </summary>
        public static List<CardInstance> FindXyzMaterials(DuelistState who, CardDef xyz)
        {
            if (who == null || xyz == null) return null;
            var pool = FieldFaceUpMonsters(who);
            if (pool.Count == 0) return null;

            var req = ParseXyz(MaterialLine(xyz), xyz.level);
            var byLevel = pool.GroupBy(m => m.Level);
            foreach (var group in byLevel.OrderBy(g => g.Key))
            {
                if (req.AllowedLevels.Count > 0 && !req.AllowedLevels.Contains(group.Key)) continue;
                var list = group.Take(req.Count).ToList();
                if (list.Count == req.Count)
                    return list;
            }

            return null;
        }

        static List<CardInstance> FieldFaceUpMonsters(DuelistState who) =>
            who.MonsterZones
                .Where(z => z.Occupant?.Def != null && z.Occupant.Def.IsMonster && z.Occupant.FaceUp)
                .Select(z => z.Occupant)
                .ToList();

        static int SumLevels(List<CardInstance> cards)
        {
            var sum = 0;
            foreach (var c in cards) sum += c.Level;
            return sum;
        }

        // ───────────────────────────── shared ─────────────────────────────

        /// <summary>
        /// Common gate: your turn, a Main Phase, and no open chain. Every Extra Deck summon
        /// (Synchro / Xyz / Pendulum) requires all three.
        /// </summary>
        public static bool SummonWindowOpen(DuelEngine engine, DuelistState who, string what, out string reason)
        {
            reason = "OK";
            if (engine.GameOver)
            {
                reason = "The Duel is over.";
                return false;
            }

            if (engine.TurnPlayer != who || !engine.InMainPhase)
            {
                reason = $"{what} can only be performed during your own Main Phase.";
                return false;
            }

            if (engine.Chain.HasLinks || engine.Chain.IsResolving)
            {
                reason = $"Cannot {what.ToLowerInvariant()} while a Chain is open.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// A quoted card name in the material line must be present among the materials — but only when the
        /// database actually holds a card of that name (so archetype fragments like "Blackwing" are not
        /// mistaken for a full card name).
        /// </summary>
        static bool NamedRequirementsMet(DuelEngine engine, string materialLine,
            List<CardInstance> materials, out string reason)
        {
            reason = "OK";
            if (string.IsNullOrEmpty(materialLine)) return true;

            var quoted = Regex.Matches(materialLine, "\"([^\"]+)\"")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (quoted.Count == 0) return true;

            var names = CardNames(engine);
            foreach (var name in quoted)
            {
                if (string.Equals(name, "Tuner", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "non-Tuner", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!names.Contains(name)) continue; // archetype fragment, not a whole card name

                if (!materials.Any(m => m.IsNamed(name)))
                {
                    reason = $"Needs a Synchro/Xyz Material named \"{name}\".";
                    return false;
                }
            }

            return true;
        }

        static Dictionary<CardDatabase, HashSet<string>> _nameCache;

        static HashSet<string> CardNames(DuelEngine engine)
        {
            var db = engine.Database;
            if (db == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _nameCache ??= new Dictionary<CardDatabase, HashSet<string>>();
            if (_nameCache.TryGetValue(db, out var cached) && cached != null)
                return cached;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in db.GetAllCards())
                if (c != null && !string.IsNullOrEmpty(c.name))
                    set.Add(c.name);
            _nameCache[db] = set;
            return set;
        }
    }
}
