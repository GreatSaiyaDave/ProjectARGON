using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Official Problem-Solving Card Text (Konami, 2011+).
    /// Source: Kevin Tewart, "PSCT Part 3: Conditions, Activations, and Effects"
    /// (yugioh-card.com/en/play/psct/psct-3) and Yugipedia "Problem-Solving Card Text".
    ///
    /// Structure: CONDITIONS : ACTIVATION ; RESOLUTION
    /// · Text before ":" = when / how often (activation conditions).
    /// · Text before ";" (after ":" if any) = costs and targeting at activation.
    /// · Text after ";" (or after ":" if no semicolon) = what happens at resolution.
    /// · Colon or semicolon ⇒ the effect makes a Chain Link.
    /// · No colon/semicolon on a monster ⇒ does NOT start a chain (continuous / inherent SS).
    /// · Spell/Trap card activation always starts a chain even without those marks.
    /// </summary>
    public static class PsctGrammar
    {
        public sealed class Sentence
        {
            public string Raw = "";
            /// <summary>Green text (Konami): before the colon.</summary>
            public string Condition = "";
            /// <summary>Red text: costs / targeting at activation (before semicolon).</summary>
            public string Activation = "";
            /// <summary>Blue text: resolution (after semicolon, or after colon if none).</summary>
            public string Resolution = "";
            public bool HasColon;
            public bool HasSemicolon;
            /// <summary>True if this sentence makes a Chain Link (official punctuation rule).</summary>
            public bool MakesChainLink;
            public bool IsFlip;
            public bool IsQuickEffect;
            public bool OncePerTurn;
            /// <summary>"You can" — optional activation.</summary>
            public bool IsOptional;
            public bool IsParenthetical;
            /// <summary>Unclassified monster text with no : or ; (continuous / inherent SS / restriction).</summary>
            public bool IsUnchainedMonsterText;
            public bool IsSummonRestriction;
            /// <summary>PSCT "when" vs "if" — stored on compiled clauses as ConditionCheckedAt.</summary>
            public ConditionCheckedAt CheckedAt = ConditionCheckedAt.Both;
            public EffectTiming SuggestedTiming;
            public int IndexInText;
            public int Length;
        }

        static readonly Regex RxFlipLead = new(@"^FLIP:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxOncePerTurn = new(@"\bonce per turn\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxQuick = new(
            @"\(Quick Effect\)|\(this is a Quick Effect\)|\bduring either player's\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxYouCan = new(@"\byou can\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxSummonRestrict = new(
            @"cannot be normal summoned(?:/set)?|must (?:first )?be special summoned|" +
            @"cannot be special summoned except|cannot be special summoned\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Printed Normal Summon/Set lock (nomi / semi-nomi). Inherent SS-from-hand
        /// (Cyber Dragon) does not match and stays Normal Summonable.
        /// </summary>
        static readonly Regex RxBlocksNormalSummon = new(
            @"cannot be normal summoned(?:/set)?|must (?:first )?be special summoned",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool BlocksNormalSummonOrSet(string officialText) =>
            !string.IsNullOrWhiteSpace(officialText) && RxBlocksNormalSummon.IsMatch(officialText);

        /// <summary>
        /// Printed full Special Summon lock (Spirit family). Does not match
        /// "Cannot be Special Summoned from the GY" or "...except by…".
        /// </summary>
        static readonly Regex RxBlocksSpecialSummon = new(
            @"cannot be special summoned\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool BlocksSpecialSummon(string officialText) =>
            !string.IsNullOrWhiteSpace(officialText) && RxBlocksSpecialSummon.IsMatch(officialText);

        public static List<Sentence> Parse(string officialText, CardDef def)
        {
            var list = new List<Sentence>();
            if (string.IsNullOrWhiteSpace(officialText)) return list;
            var text = Normalize(officialText);
            var isMonster = def != null && def.IsMonster;
            var isSpellTrap = def != null && (def.IsSpell || def.IsTrap);

            foreach (var (raw, index, length) in SplitIndexed(text))
            {
                var s = ParseOne(raw, isMonster, isSpellTrap);
                s.IndexInText = index;
                s.Length = length;
                list.Add(s);
            }

            return list;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        static Sentence ParseOne(string raw, bool isMonster, bool isSpellTrap)
        {
            var s = new Sentence { Raw = raw };
            var body = raw.Trim();
            s.IsParenthetical = body.StartsWith("(") && body.EndsWith(")");
            s.IsSummonRestriction = RxSummonRestrict.IsMatch(body);
            s.IsFlip = RxFlipLead.IsMatch(body);
            s.OncePerTurn = RxOncePerTurn.IsMatch(body);
            s.IsQuickEffect = RxQuick.IsMatch(body);
            s.IsOptional = RxYouCan.IsMatch(body);

            if (s.IsFlip)
                body = RxFlipLead.Replace(body, "").Trim();

            var colon = IndexOfPunctOutsideParens(body, ':');
            var semi = IndexOfPunctOutsideParens(body, ';');
            s.HasColon = colon >= 0;
            s.HasSemicolon = semi >= 0;

            if (s.IsFlip && !s.HasColon)
            {
                s.Condition = "FLIP";
                s.HasColon = true;
            }

            if (colon >= 0)
            {
                s.Condition = (s.IsFlip ? "FLIP" : body.Substring(0, colon).Trim());
                var after = body.Substring(colon + 1).Trim();
                var semi2 = IndexOfPunctOutsideParens(after, ';');
                if (semi2 >= 0)
                {
                    s.Activation = after.Substring(0, semi2).Trim();
                    s.Resolution = after.Substring(semi2 + 1).Trim();
                    s.HasSemicolon = true;
                }
                else
                {
                    s.Resolution = after;
                }
            }
            else if (semi >= 0)
            {
                s.Activation = body.Substring(0, semi).Trim();
                s.Resolution = body.Substring(semi + 1).Trim();
            }
            else
            {
                s.Resolution = body;
            }

            if (s.IsFlip && string.IsNullOrEmpty(s.Condition))
                s.Condition = "FLIP";

            // Konami: colon or semicolon ⇒ chain. Spell/Trap card activation always chains.
            // Monster text with neither mark does not start a chain.
            if (s.IsParenthetical || s.IsSummonRestriction)
                s.MakesChainLink = false;
            else if (s.HasColon || s.HasSemicolon || s.IsFlip)
                s.MakesChainLink = true;
            else if (isSpellTrap)
            {
                // Spell/Trap card activation always chains, but leftover sentences that
                // describe while-on-field continuous stats do not make extra Chain Links.
                s.MakesChainLink = !LooksContinuous(body);
            }
            else
            {
                s.MakesChainLink = false;
                s.IsUnchainedMonsterText = isMonster;
            }

            s.SuggestedTiming = SuggestTiming(s);
            var lead = (s.Condition ?? "").TrimStart();
            if (lead.StartsWith("When ", StringComparison.OrdinalIgnoreCase) ||
                lead.StartsWith("When\t", StringComparison.OrdinalIgnoreCase))
                s.CheckedAt = ConditionCheckedAt.Activation;
            else if (lead.StartsWith("If ", StringComparison.OrdinalIgnoreCase))
                s.CheckedAt = ConditionCheckedAt.Both;
            return s;
        }

        static EffectTiming SuggestTiming(Sentence s)
        {
            var cond = ((s.Condition ?? "") + " " + (s.Raw ?? "")).Trim();
            if (s.IsFlip) return EffectTiming.Flip;
            if (Contains(cond, "damage calculation") || Contains(cond, "damage step"))
                return EffectTiming.DamageCalculation;
            if (Contains(cond, "declares an attack") || Contains(cond, "declare an attack"))
                return EffectTiming.AttackDeclared;
            if (Contains(cond, "normal or flip summon") || Contains(cond, "normal summons") ||
                Contains(cond, "flip summons"))
                return EffectTiming.OpponentNormalOrFlipSummon;
            if (Contains(cond, "flip summoned"))
                return EffectTiming.ThisCardSummoned;
            if (Contains(cond, "this card is summoned") ||
                Contains(cond, "this card is normal summoned") ||
                Contains(cond, "this monster is summoned") ||
                Contains(cond, "this monster is normal summoned"))
                return EffectTiming.ThisCardSummoned;
            if (Contains(cond, "destroyed by battle"))
                return EffectTiming.SentFromFieldToGy;
            if (Contains(cond, "sent from the field to the gy") ||
                Contains(cond, "sent from the field to the graveyard"))
                return EffectTiming.SentFromFieldToGy;
            if (Contains(cond, "standby phase"))
                return EffectTiming.StandbyPhase;
            if (Contains(cond, "end phase"))
                return EffectTiming.EndPhase;
            if (!s.MakesChainLink) return EffectTiming.ContinuousWhileFaceUp;
            if (s.OncePerTurn && !LooksLikeTrigger(s.Condition))
                return EffectTiming.Activate;
            return EffectTiming.Activate;
        }

        static bool LooksContinuous(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return (Contains(body, "gain ") && Contains(body, "ATK")) ||
                   (Contains(body, "lose ") && Contains(body, "ATK")) ||
                   Contains(body, "while this card") ||
                   Contains(body, "as long as this card") ||
                   Contains(body, "reduce the level") ||
                   Contains(body, "this card remains on the field") ||
                   Contains(body, "cannot declare an attack") ||
                   Contains(body, "neither player can target");
        }

        static bool LooksLikeTrigger(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return false;
            return Starts(condition, "when ") || Starts(condition, "if ") ||
                   Starts(condition, "during ") || Starts(condition, "at ");
        }

        static bool Starts(string s, string p) =>
            s != null && s.StartsWith(p, StringComparison.OrdinalIgnoreCase);

        static bool Contains(string s, string p) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0;

        static int IndexOfPunctOutsideParens(string s, char punct)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var depth = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '(') depth++;
                else if (c == ')' && depth > 0) depth--;
                else if (c == punct && depth == 0) return i;
            }

            return -1;
        }

        /// <summary>
        /// Split on sentence-ending punctuation. A leading parenthetical
        /// "(This card is always treated as ….)" is its own sentence even when
        /// the period sits inside the parentheses (Konami name conditions).
        /// </summary>
        public static List<(string raw, int index, int length)> SplitIndexed(string text)
        {
            var list = new List<(string, int, int)>();
            if (string.IsNullOrEmpty(text)) return list;
            var i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                var start = i;
                if (text[i] == '(')
                {
                    var close = FindMatchingParen(text, i);
                    if (close < 0) break;
                    var end = close + 1;
                    if (end < text.Length && text[end] == '.') end++;
                    var raw = text.Substring(start, end - start).Trim();
                    if (raw.Length > 2)
                        list.Add((raw, start, end - start));
                    i = end;
                    continue;
                }

                var depth = 0;
                var inQuote = false;
                var endSent = -1;
                for (var j = i; j < text.Length; j++)
                {
                    var c = text[j];
                    if (c == '"') inQuote = !inQuote;
                    else if (c == '(' && !inQuote) depth++;
                    else if (c == ')' && depth > 0 && !inQuote) depth--;
                    else if (!inQuote && depth == 0 && (c == '.' || c == '!' || c == '?'))
                    {
                        endSent = j + 1;
                        break;
                    }
                }

                if (endSent < 0)
                {
                    var raw = text.Substring(start).Trim();
                    if (raw.Length > 2)
                        list.Add((raw, start, text.Length - start));
                    break;
                }

                {
                    var raw = text.Substring(start, endSent - start).Trim();
                    if (raw.Length > 2)
                        list.Add((raw, start, endSent - start));
                    i = endSent;
                }
            }

            return list;
        }

        static int FindMatchingParen(string text, int open)
        {
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }
    }
}
