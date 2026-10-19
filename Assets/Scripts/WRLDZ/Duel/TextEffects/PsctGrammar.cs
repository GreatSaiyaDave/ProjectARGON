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
        static readonly Regex RxEvenDuringDamageStep = new(
            @",?\s*even during the Damage Step\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxSummonRestrict = new(
            @"cannot be normal summoned(?:/set)?|must (?:first )?be special summoned|cannot be special summoned(?: except| from the (?:GY|Graveyard))?|cannot be summoned unless you control a face-up ""[^""]+""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxSummonGateNamed = new(
            @"cannot be summoned unless you control a face-up ""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCannotBeSpecialSummoned = new(
            @"cannot be special summoned(?=\s*(?:\.|$))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex RxCannotBeSpecialSummonedFromGy = new(
            @"cannot be special summoned from (?:the )?(?:GY|Graveyard)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGySummonUnlessTributeCost = new(
            @"cannot be special summoned from (?:the )?(?:GY|Graveyard),? unless you Tribute\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCannotBeSpecialSummonedByOtherWays = new(
            @"cannot be special summoned by other ways\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Printed Normal Summon/Set lock (nomi / semi-nomi). Inherent SS-from-hand
        /// (Cyber Dragon) does not match and stays Normal Summonable. A conditional "Cannot be Normal Summoned while
        /// you control a monster" (Cave Dragon, BattleRules batch) blocks only that Summon, never the Set
        /// (BattleRulesRuntime.NormalSummonForbiddenByOwnText). Terrorking Archfiend's "cannot be Normal Summoned or Flip
        /// Summoned unless …" is conditional too (ArchfiendRidersRuntime.NormalOrFlipSummonForbidden, Set stays legal).
        /// </summary>
        static readonly Regex RxBlocksNormalSummon = new(
            @"cannot be normal summoned(?:/set)?(?!\s+while\b)(?!\s+or Flip Summoned unless\b)(?!\s*\(but can be Normal Set\))|" +
            @"must (?:first )?be special summoned",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool BlocksNormalSummonOrSet(string officialText) =>
            !string.IsNullOrWhiteSpace(officialText) && RxBlocksNormalSummon.IsMatch(officialText);

        /// <summary>
        /// RdsRedoBattle batch: "Cannot be Normal Summoned (but can be Normal Set)." (Mystic Swordsman LV4 / LV6; ygopro
        /// EFFECT_LIMIT_SUMMON_PROC with no procedure): the Normal (Tribute) Summon is forbidden, the Normal (Tribute) Set is
        /// legal. Before, <see cref="BlocksNormalSummonOrSet"/> matched it and forbade the Set too.
        /// </summary>
        static readonly Regex RxNormalSetOnly = new(@"cannot be normal summoned\s*\(but can be Normal Set\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool BlocksNormalSummonButNotSet(string officialText) =>
            !string.IsNullOrWhiteSpace(officialText) && RxNormalSetOnly.IsMatch(officialText);

        /// <summary>Returns the exact face-up card required by Guardian-style summon text.</summary>
        public static string SummonGateNamed(string officialText)
        {
            if (string.IsNullOrWhiteSpace(officialText)) return null;
            var m = RxSummonGateNamed.Match(officialText);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>True for a hard Nomi sentence with no printed exception.</summary>
        public static bool BlocksSpecialSummon(string officialText) =>
            !string.IsNullOrWhiteSpace(officialText) &&
            (RxCannotBeSpecialSummoned.IsMatch(officialText) ||
             RxCannotBeSpecialSummonedByOtherWays.IsMatch(officialText));

        /// <summary>
        /// True when printed text explicitly forbids Special Summoning from the GY.
        /// Conditional text with an "unless" exception remains open for its
        /// procedure-specific implementation. Full Semi-Nomi provenance (properly
        /// summoned once before a later GY revive) is parked until a
        /// WasProperlySpecialSummoned flag exists; do not infer it from WasSpecialSummoned.
        /// </summary>
        public static bool CannotBeSpecialSummonedFromGraveyard(string officialText)
        {
            if (string.IsNullOrWhiteSpace(officialText) ||
                !RxCannotBeSpecialSummonedFromGy.IsMatch(officialText))
                return false;

            var sentence = Regex.Match(officialText,
                @"[^.]*cannot be special summoned from (?:the )?(?:GY|Graveyard)[^.]*\.?",
                RegexOptions.IgnoreCase);
            return !sentence.Success ||
                   !Regex.IsMatch(sentence.Value, @"\bunless\b", RegexOptions.IgnoreCase);
        }

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
            // NegationMonsters batch (v161): "cannot be Special Summoned from the GY, unless you Tribute …" (Tyrant Dragon,
            // ygopro EFFECT_SPSUMMON_COST) is a summon cost, not a restriction to skip: it stays unparsed until the cost
            // is implemented, so the card is not FullyCompiled with its cost silently dropped.
            s.IsSummonRestriction = RxSummonRestrict.IsMatch(body) && !RxGySummonUnlessTributeCost.IsMatch(body);
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
            // "…, even during the Damage Step" only permits activation there; it is not the trigger. Read as one,
            // it filed Michizure ("When a monster is sent from the field to your GY, even during the Damage Step")
            // onto the damage-calculation window.
            cond = RxEvenDuringDamageStep.Replace(cond, "");
            if (s.IsFlip) return EffectTiming.Flip;
            if (Contains(cond, "end of the damage step"))
                return EffectTiming.EndOfDamageStep;
            if (Contains(cond, "after damage calculation"))
                return EffectTiming.AfterDamageCalculation;
            // Must precede the generic "damage step" test below, which would otherwise
            // file Sasuke / Mystic Swordsman onto the damage-calculation window.
            if (Contains(cond, "start of the damage step"))
                return EffectTiming.StartOfDamageStep;
            if (Contains(cond, "damage calculation") || Contains(cond, "damage step"))
                return EffectTiming.DamageCalculation;
            if (Contains(cond, "declares an attack") || Contains(cond, "declare an attack"))
                return EffectTiming.AttackDeclared;
            // Heart of the Underdog: "During your Draw Phase, when you draw a Normal Monster(s)" —
            // must precede Drop Off's "normal draw" + "draw phase" substring test.
            if (Contains(cond, "normal monster") && Contains(cond, "draw phase"))
                return EffectTiming.ControllerNormalDrawInDrawPhase;
            // Drop Off: "draws for their normal draw in their Draw Phase" — not Solemn Wishes / any-draw.
            if (Contains(cond, "normal draw") && Contains(cond, "draw phase"))
                return EffectTiming.OpponentNormalDrawInDrawPhase;
            // Spring of Rebirth: each/every time monster(s) return field→owner's hand (continuous).
            if ((Contains(cond, "return") || Contains(cond, "returns")) &&
                Contains(cond, "owner") && Contains(cond, "hand") &&
                (Contains(cond, "every time") || Contains(cond, "each time")))
                return EffectTiming.ContinuousWhileFaceUp;
            // Mysterious Puppeteer: each time you or your opponent / either player
            // Normal or Flip Summons → Continuous LP gain (not Trap Hole window).
            if ((Contains(cond, "you or your opponent") || Contains(cond, "either player") ||
                 Contains(cond, "a player")) &&
                (Contains(cond, "normal summon") || Contains(cond, "flip summon")) &&
                (Contains(cond, "life points") || Contains(cond, "gain")))
                return EffectTiming.ContinuousWhileFaceUp;
            // "When THIS CARD is Normal or Flip Summoned" is a self-summon trigger
            // (Senju / Sonic Bird / Manju). Only the opponent's phrasing — "your
            // opponent Normal or Flip Summons" — belongs to the Trap Hole window.
            // Matching the shared substring first mis-filed those onto the opponent.
            var aboutThisCard = Contains(cond, "this card is") ||
                                Contains(cond, "this monster is");
            if (!aboutThisCard &&
                (Contains(cond, "normal or flip summon") || Contains(cond, "normal summons") ||
                 Contains(cond, "flip summons")))
                return EffectTiming.OpponentNormalOrFlipSummon;
            if (Contains(cond, "flip summoned"))
                return EffectTiming.ThisCardSummoned;
            if (Contains(cond, "this card is tribute summoned") ||
                Contains(cond, "this monster is tribute summoned"))
                return EffectTiming.ThisCardSummoned;
            if (Contains(cond, "this card is summoned") ||
                Contains(cond, "this card is normal summoned") ||
                Contains(cond, "this monster is summoned") ||
                Contains(cond, "this monster is normal summoned"))
                return EffectTiming.ThisCardSummoned;
            if (Contains(cond, "inflicts battle damage"))
                return EffectTiming.ThisCardInflictsBattleDamage;
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
                   Contains(body, "neither player can target") ||
                   Contains(body, "negate all equip card effects") ||
                   Contains(body, "cannot draw cards except during");
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
