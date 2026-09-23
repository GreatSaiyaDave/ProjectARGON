using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared multi-target atoms: opponent-GY banish (Gravedigger Ghoul / Soul Release
    /// is a different "any GY, cards" shape and stays fail-closed) and mixed-side
    /// destroy (Two-Pronged Attack). No cardId branches.
    /// </summary>
    public static class MultiTargetTemplates
    {
        /// <summary>
        /// Gravedigger Ghoul family (LOB pre-PSCT): select up to N monsters in the
        /// opponent's Graveyard and remove them from play.
        /// </summary>
        static readonly Regex RxOppGyBanishLegacy = new(
            @"Select up to (\d+) Monster Card\(s\) from your opponent's Graveyard\.\s*" +
            @"Remove the selected card\(s\) from play\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Modern PSCT sibling: Target up to N monsters in opponent's GY; banish them.
        /// Does not match Soul Release ("cards in any GY(s)").
        /// </summary>
        static readonly Regex RxOppGyBanishPsct = new(
            @"Target up to (\d+) monsters? in (?:your )?opponent's (?:GY|Graveyard);\s*" +
            @"banish them\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Two-Pronged Attack family (LOB pre-PSCT): select N of yours and M of theirs.
        /// </summary>
        static readonly Regex RxDestroyYoursAndOppLegacy = new(
            @"Select and destroy (\d+) of your monsters and (\d+) of your opponent's monsters\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Modern PSCT sibling. "them" / "those targets" (not "all those targets") —
        /// remaining legal targets still resolve if one leaves mid-chain.
        /// </summary>
        static readonly Regex RxDestroyYoursAndOppPsct = new(
            @"Target (\d+) monsters? you control and (\d+) monsters? your opponent controls;\s*" +
            @"destroy (?:them|those targets)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                if (SpanCovered(spans, m.Index, m.Length)) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            var gyBan = RxOppGyBanishLegacy.Match(text);
            if (!gyBan.Success) gyBan = RxOppGyBanishPsct.Match(text);
            Add(gyBan, gyBan.Success ? OppGyBanishClause(gyBan) : null);

            var mixed = RxDestroyYoursAndOppLegacy.Match(text);
            if (!mixed.Success) mixed = RxDestroyYoursAndOppPsct.Match(text);
            Add(mixed, mixed.Success ? MixedDestroyClause(mixed) : null);
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxOppGyBanishLegacy.IsMatch(text) || RxOppGyBanishPsct.IsMatch(text))
                need.Add(EffectActionKind.Banish);
            if (RxDestroyYoursAndOppLegacy.IsMatch(text) || RxDestroyYoursAndOppPsct.IsMatch(text))
                need.Add(EffectActionKind.Destroy);
        }

        static EffectClause OppGyBanishClause(Match m)
        {
            var n = 2;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsed) && parsed > 0)
                n = parsed;
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Banish,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OpponentGyMonsters,
                RequiresTargetChoice = true,
                TargetCount = n,
                TargetUpTo = true,
                MakesChainLink = true
            };
        }

        static EffectClause MixedDestroyClause(Match m)
        {
            var yours = 2;
            var theirs = 1;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var y) && y > 0)
                yours = y;
            if (m.Groups.Count > 2 && int.TryParse(m.Groups[2].Value, out var o) && o > 0)
                theirs = o;
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                ControllerTargetCount = yours,
                OpponentTargetCount = theirs,
                TargetCount = yours + theirs,
                MakesChainLink = true
            };
        }

        static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            foreach (var s in spans)
            {
                var sEnd = s.start + s.length;
                if (start < sEnd && end > s.start) return true;
            }

            return false;
        }
    }
}
