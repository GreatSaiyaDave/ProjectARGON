using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared continuous protections. Fisherman / Kay'est / Deepsea Warrior / Torpedo Fish
    /// are one kind (unaffected + cannot be attack-targeted), not unique scripts.
    /// New cards with the same PSCT compile here with no cardId branch.
    /// </summary>
    public static class ProtectionTemplates
    {
        /// <summary>
        /// Legendary Fisherman: While "Umi" is on the field, this card is unaffected by
        /// Spell effects and cannot be targeted for attacks, but does not prevent direct.
        /// </summary>
        static readonly Regex RxNamedUnaffectedAndNoAttack = new(
            @"(?:While|As long as) ""([^""]+)"" is(?: face-up)? on the field, this card is " +
            @"unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects) and cannot be " +
            @"targeted for attacks(?:, but does not prevent your opponent from attacking you directly)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Guardian Kay'est: same pair of protections with no named-field lock.
        /// </summary>
        static readonly Regex RxUnaffectedAndNoAttack = new(
            @"This card is unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects) and " +
            @"cannot be targeted for attacks(?:, but does not prevent your opponent from attacking you directly)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Deepsea Warrior / Torpedo Fish / Cannonball Spear Shellfish:
        /// While "Umi" is face-up on the field, this card is unaffected by any Spell Cards.
        /// </summary>
        static readonly Regex RxNamedUnaffected = new(
            @"(?:While|As long as) ""([^""]+)"" is(?: face-up)? on the field, this card is " +
            @"unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxHorusServant = new(
            @"Your opponent cannot target face-up ""([^""]+)"" monsters with Spells, Traps, or card effects\.?",
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

            var namedBoth = RxNamedUnaffectedAndNoAttack.Match(text);
            if (namedBoth.Success)
            {
                var allowsDirect = Regex.IsMatch(namedBoth.Value,
                    @"does not prevent your opponent from attacking you directly",
                    RegexOptions.IgnoreCase);
                var filter = namedBoth.Groups[2].Value;
                Add(namedBoth, UnaffectedClause(filter, namedBoth.Groups[1].Value));
                into.Add(NoAttackClause(namedBoth.Groups[1].Value, allowsDirect, namedBoth.Value));
            }

            var both = RxUnaffectedAndNoAttack.Match(text);
            if (both.Success)
            {
                var allowsDirect = Regex.IsMatch(both.Value,
                    @"does not prevent your opponent from attacking you directly",
                    RegexOptions.IgnoreCase);
                Add(both, UnaffectedClause(both.Groups[1].Value, null));
                into.Add(NoAttackClause(null, allowsDirect, both.Value));
            }

            Add(RxNamedUnaffected.Match(text),
                UnaffectedFromNamed(RxNamedUnaffected.Match(text)));

            var horus = RxHorusServant.Match(text);
            Add(horus, horus.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeTargetedByEffects,
                    NamedCard = horus.Groups[1].Value,
                    MakesChainLink = false
                }
                : null);
        }

        public static bool MatchesSharedKind(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return RxNamedUnaffectedAndNoAttack.IsMatch(text) ||
                   RxUnaffectedAndNoAttack.IsMatch(text) ||
                   RxNamedUnaffected.IsMatch(text) ||
                   RxHorusServant.IsMatch(text);
        }

        public static void ExpectedActions(string text, List<EffectActionKind> need)
        {
            if (string.IsNullOrEmpty(text) || need == null) return;
            if (RxNamedUnaffectedAndNoAttack.IsMatch(text) || RxUnaffectedAndNoAttack.IsMatch(text))
            {
                need.Add(EffectActionKind.UnaffectedByCardEffects);
                need.Add(EffectActionKind.CannotBeAttackTarget);
            }
            else if (RxNamedUnaffected.IsMatch(text))
                need.Add(EffectActionKind.UnaffectedByCardEffects);
            if (RxHorusServant.IsMatch(text))
                need.Add(EffectActionKind.CannotBeTargetedByEffects);
        }

        static EffectClause UnaffectedFromNamed(Match m)
        {
            if (m == null || !m.Success) return null;
            return UnaffectedClause(m.Groups[2].Value, m.Groups[1].Value);
        }

        static EffectClause UnaffectedClause(string filter, string named)
        {
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.UnaffectedByCardEffects,
                UnaffectedByFilter = string.IsNullOrEmpty(filter) ? "Spell" : filter,
                RequiresFaceUpName = named,
                MakesChainLink = false
            };
        }

        static EffectClause NoAttackClause(string named, bool allowsDirect, string snippet)
        {
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBeAttackTarget,
                RequiresFaceUpName = named,
                AllowsDirectAttackWhileProtected = allowsDirect,
                MakesChainLink = false,
                SourceSnippet = snippet?.Trim()
            };
        }

        static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            var covered = 0;
            foreach (var (s, n) in spans)
            {
                var a = start > s ? start : s;
                var b = end < s + n ? end : s + n;
                if (b > a) covered += b - a;
            }

            return covered * 2 >= length;
        }
    }
}
