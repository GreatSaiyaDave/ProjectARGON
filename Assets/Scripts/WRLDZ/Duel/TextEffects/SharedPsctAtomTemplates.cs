using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared modern-PSCT atoms used by more than one card. Parameterized by printed
    /// type/attribute lists — never by passcode allowlists.
    /// </summary>
    public static class SharedPsctAtomTemplates
    {
        /// <summary>Delimiter for multi-type <see cref="EffectClause.RaceFilter"/> lists.</summary>
        public const char RaceListSep = '|';

        /// <summary>
        /// LOB Field family + Sogen:
        /// "All Insect, Beast, Plant, and Beast-Warrior monsters on the field gain 200 ATK/DEF."
        /// Does not consume the Umi/Yami "gain …, also … lose" sentence (fail-closed).
        /// </summary>
        static readonly Regex RxFieldTypesGainAtkDef = new(
            @"All (.+?) monsters on the field gain (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Official Type words, longest first so "Beast-Warrior" / "Winged Beast"
        /// win over "Beast" / "Warrior".
        /// </summary>
        static readonly string[] MonsterTypesLongestFirst =
        {
            "Beast-Warrior", "Divine-Beast", "Winged Beast", "Sea Serpent", "Creator-God",
            "Spellcaster", "Dinosaur", "Thunder", "Warrior", "Cyberse", "Illusion",
            "Reptile", "Machine", "Psychic", "Dragon", "Zombie", "Insect", "Fairy",
            "Fiend", "Plant", "Beast", "Wyrm", "Aqua", "Rock", "Fish", "Pyro"
        };

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;
            if (!TryMatch(text, out var clause, out var index, out var length) || clause == null)
                return;
            into.Add(clause);
            spans?.Add((index, length));
        }

        /// <summary>Compile one sentence/body. Empty when the atom does not apply.</summary>
        public static List<EffectClause> TryCompile(string text)
        {
            var list = new List<EffectClause>();
            if (TryMatch(text, out var clause, out _, out _) && clause != null)
                list.Add(clause);
            return list;
        }

        public static bool TryMatch(string text, out EffectClause clause, out int index, out int length)
        {
            clause = null;
            index = 0;
            length = 0;
            if (string.IsNullOrEmpty(text)) return false;

            var m = RxFieldTypesGainAtkDef.Match(text);
            if (!m.Success) return false;
            if (FollowedByAlsoLose(text, m)) return false;
            if (!TryParseTypeList(m.Groups[1].Value, out var types) || types.Count == 0)
                return false;

            var n = 0;
            int.TryParse(m.Groups[2].Value, out n);
            if (n <= 0) return false;

            clause = new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = n,
                DefAmount = n,
                Side = EffectSide.Both,
                RaceFilter = string.Join(RaceListSep.ToString(), types),
                StaysOnField = true,
                MakesChainLink = false,
                SourceSnippet = m.Value.Trim()
            };
            index = m.Index;
            length = m.Length;
            return true;
        }

        /// <summary>
        /// True when <paramref name="race"/> is one of the types in a pipe-separated
        /// filter, or (single filter) the legacy substring match.
        /// </summary>
        public static bool RaceMatchesFilter(string race, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(race)) return false;
            if (filter.IndexOf(RaceListSep) >= 0)
            {
                var parts = filter.Split(RaceListSep);
                for (var i = 0; i < parts.Length; i++)
                {
                    var p = parts[i] != null ? parts[i].Trim() : "";
                    if (p.Length == 0) continue;
                    if (race.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
                }

                return false;
            }

            return race.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool FollowedByAlsoLose(string text, Match m)
        {
            var end = m.Index + m.Length;
            if (end >= text.Length) return false;
            var rest = text.Substring(end).TrimStart();
            if (rest.StartsWith(",", StringComparison.Ordinal))
                rest = rest.Substring(1).TrimStart();
            return rest.StartsWith("also", StringComparison.OrdinalIgnoreCase);
        }

        static bool TryParseTypeList(string raw, out List<string> types)
        {
            types = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var i = 0;
            var s = raw.Trim();
            while (i < s.Length)
            {
                while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
                if (i >= s.Length) break;
                if (StartsAt(s, i, "and") &&
                    (i + 3 >= s.Length || char.IsWhiteSpace(s[i + 3]) || s[i + 3] == ','))
                {
                    i += 3;
                    continue;
                }

                var hit = TypeAt(s, i);
                if (hit == null) return false;
                i += hit.Length;
                if (StartsAt(s, i, "-Type")) i += 5;
                else if (StartsAt(s, i, " Type")) i += 5;
                types.Add(hit);
            }

            return types.Count > 0;
        }

        static string TypeAt(string s, int i)
        {
            for (var t = 0; t < MonsterTypesLongestFirst.Length; t++)
            {
                var name = MonsterTypesLongestFirst[t];
                if (StartsAt(s, i, name))
                {
                    var end = i + name.Length;
                    if (end < s.Length && char.IsLetter(s[end])) continue;
                    return name;
                }
            }

            return null;
        }

        static bool StartsAt(string s, int i, string token)
        {
            if (i < 0 || token == null || i + token.Length > s.Length) return false;
            return string.Compare(s, i, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
