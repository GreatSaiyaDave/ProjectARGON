using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared modern-PSCT atoms used by more than one card. Parameterized by printed
    /// type/kind — never by passcode allowlists.
    /// </summary>
    public static class SharedPsctAtomTemplates
    {
        /// <summary>
        /// Armed Ninja / Crimson Ninja modern PSCT:
        /// FLIP: Target 1 Spell Card on the field; destroy that target.
        /// (If the target is Set, reveal it, and destroy it if it is a Spell Card.
        /// Otherwise, return it to its original position.)
        /// </summary>
        static readonly Regex RxFlipDestroyKindModern = new(
            @"FLIP:\s*(?:Target|Select) 1 (Spell|Trap)(?: Card)? on the field;\s*" +
            @"destroy that target\.?\s*" +
            @"\(If the target is Set,\s*reveal it,\s*and destroy it if it is a \1(?: Card)?\.?\s*" +
            @"Otherwise,\s*return it to its original position\.?\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Reaper of the Cards pre-PSCT:
        /// FLIP: Select 1 Trap Card on the field and destroy it.
        /// If the selected card is Set, pick up and see the card.
        /// If it is a Trap Card, it is destroyed.
        /// If it is a Spell Card, return it to its original position.
        /// </summary>
        static readonly Regex RxFlipDestroyKindLegacy = new(
            @"FLIP:\s*(?:Select|Target) 1 (Spell|Trap)(?: Card)? on the field and destroy it\.?\s*" +
            @"If the selected card is Set,\s*pick up and see the card\.?\s*" +
            @"If it is a \1(?: Card)?,\s*it is destroyed\.?\s*" +
            @"If it is a (?:Spell|Trap)(?: Card)?,\s*return it to its original position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;
            if (!TryMatch(text, out var clause, out var index, out var length) || clause == null)
                return;
            into.Add(clause);
            spans?.Add((index, length));
        }

        /// <summary>Compile one body. Empty when the atom does not apply.</summary>
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

            var m = RxFlipDestroyKindModern.Match(text);
            if (!m.Success) m = RxFlipDestroyKindLegacy.Match(text);
            if (!m.Success) return false;

            var kind = (m.Groups[1].Value ?? "").Trim();
            if (!IsSpellOrTrapKind(kind)) return false;

            clause = FlipDestroyKindClause(kind, m.Value);
            index = m.Index;
            length = m.Length;
            return true;
        }

        /// <summary>
        /// Face-up cards must match the printed Spell/Trap kind. Set cards are confirmed
        /// at resolution after reveal — do not use this for activation of face-up wrong type.
        /// </summary>
        public static bool MatchesCardKind(CardDef def, string kind)
        {
            if (def == null || string.IsNullOrEmpty(kind)) return false;
            if (kind.Equals("Spell", StringComparison.OrdinalIgnoreCase))
                return def.IsSpell && !def.IsTrap;
            if (kind.Equals("Trap", StringComparison.OrdinalIgnoreCase))
                return def.IsTrap;
            return false;
        }

        public static bool IsSpellOrTrapKind(string kind) =>
            !string.IsNullOrEmpty(kind) &&
            (kind.Equals("Spell", StringComparison.OrdinalIgnoreCase) ||
             kind.Equals("Trap", StringComparison.OrdinalIgnoreCase));

        static EffectClause FlipDestroyKindClause(string kind, string snippet)
        {
            var canonical = kind.Equals("Trap", StringComparison.OrdinalIgnoreCase) ? "Trap" : "Spell";
            return new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                CardKindFilter = canonical,
                MakesChainLink = true,
                SourceSnippet = (snippet ?? "").Trim()
            };
        }
    }
}
