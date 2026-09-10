using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared continuous "named pieces in your hand → you win the Duel" family
    /// (Exodia the Forbidden One). Not an activation — no colon/semicolon, no Chain.
    /// GY/field copies do not count; only the controller's hand is scanned.
    /// </summary>
    public static class HandWinconTemplates
    {
        /// <summary>
        /// Modern PSCT: If you have "A", "B", "C" and "D" in addition to this card
        /// in your hand, you win the Duel.
        /// </summary>
        static readonly Regex RxWinIfNamedInHand = new(
            @"If you have ((?:""[^""]+""(?:\s*,\s*|\s+and\s+)?)+)\s*" +
            @"in addition to this card in your hand,\s*you win the Duel\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxQuotedName = new(
            @"""([^""]+)""",
            RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            var m = RxWinIfNamedInHand.Match(text);
            if (!m.Success) return;

            var names = new List<string>();
            foreach (Match q in RxQuotedName.Matches(m.Groups[1].Value))
            {
                var n = q.Groups[1].Value.Trim();
                if (n.Length == 0) continue;
                names.Add(n);
            }

            if (names.Count == 0) return;

            into.Add(new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileInHand,
                Action = EffectActionKind.WinIfNamedCardsInHand,
                Side = EffectSide.Controller,
                NamedCards = names.ToArray(),
                MakesChainLink = false,
                IsOptional = false,
                SourceSnippet = m.Value.Trim()
            });
            spans?.Add((m.Index, m.Length));
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || string.IsNullOrEmpty(def.desc) || need == null) return;
            if (RxWinIfNamedInHand.IsMatch(def.desc))
                need.Add(EffectActionKind.WinIfNamedCardsInHand);
        }
    }
}
