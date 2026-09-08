using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Anime-style verbal / typed announcements → <see cref="DuelIntent"/>.
    /// Examples: "I summon Dark Magician!", "Activate Mirror Force", "I end my turn".
    /// Does not check legality — <see cref="DuelCommandService"/> does.
    /// </summary>
    public static class VerbalMoveParser
    {
        static readonly Regex RxSummon = new(
            @"\b(?:i\s+)?(?:will\s+)?(?:normal\s+)?summon\s+(?:the\s+)?(?<name>.+?)(?:\s+in\s+(?<pos>attack|atk|defense|def)(?:\s+position)?)?[!?.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSet = new(
            @"\b(?:i\s+)?(?:will\s+)?set\s+(?:the\s+)?(?<name>.+?)(?:\s+in\s+(?<pos>defense|def|attack|atk)(?:\s+position)?)?[!?.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivate = new(
            @"\b(?:i\s+)?(?:will\s+)?activat(?:e|ing)\s+(?:the\s+)?(?<name>.+?)[!?.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAttackNamed = new(
            @"\b(?<name>.+?)\s*,?\s*attack(?:s)?(?:\s+(?:the\s+)?(?<target>.+))?[!?.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAttackWith = new(
            @"\battack\s+(?:with\s+)?(?:the\s+)?(?<name>.+?)(?:\s+(?:on|at|into)\s+(?:the\s+)?(?<target>.+))?[!?.]*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Parse player speech/type into an intent. Returns null if nothing recognized.
        /// </summary>
        public static DuelIntent Parse(string utterance, DuelEngine engine, DuelistState who)
        {
            if (string.IsNullOrWhiteSpace(utterance) || engine == null || who == null)
                return null;

            var text = Normalize(utterance);
            if (text.Length == 0) return null;

            if (engine.PendingActivation != null && engine.PendingActivation.AwaitingLpZeroConfirm)
            {
                if (text is "yes" or "sure" or "confirm" or "ok" or "okay" or "do it" or
                    "i'm sure" or "im sure" or "pay" or "pay lp")
                    return Intent(DuelIntentKind.ConfirmLpZero, text);
                if (IsPass(text) || text is "cancel" or "cancel target" or "never mind" or "nevermind")
                    return Intent(DuelIntentKind.CancelTarget, text);
            }

            // —— Global phrases (no card) ——
            if (IsPass(text))
                return Intent(DuelIntentKind.PassResponse, text);
            if (IsEndTurn(text))
                return Intent(DuelIntentKind.EndTurn, text);
            if (IsBattle(text))
                return Intent(DuelIntentKind.EnterBattlePhase, text);
            if (IsMain2(text))
                return Intent(DuelIntentKind.EnterMainPhase2, text);
            if (IsDirectOnly(text))
                return Intent(DuelIntentKind.DirectAttack, text);
            if (text is "cancel" or "cancel target" or "never mind" or "nevermind")
                return Intent(DuelIntentKind.CancelTarget, text);
            if (text.Contains("clear tribute") || text == "clear tributes")
                return Intent(DuelIntentKind.ClearTributes, text);

            // —— Activate ——
            var m = RxActivate.Match(text);
            if (m.Success)
            {
                var name = CleanName(m.Groups["name"].Value);
                var card = FindCard(engine, who, name, preferHand: true, preferFieldSt: true, preferFieldMon: true);
                return new DuelIntent
                {
                    Kind = DuelIntentKind.Activate,
                    Card = card,
                    CardNameHint = name,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            // —— Summon ——
            m = RxSummon.Match(text);
            if (m.Success)
            {
                var name = CleanName(m.Groups["name"].Value);
                var pos = m.Groups["pos"].Success ? m.Groups["pos"].Value.ToLowerInvariant() : "attack";
                var setDef = pos.StartsWith("def");
                var card = FindCard(engine, who, name, preferHand: true, preferFieldSt: false, preferFieldMon: false);
                return new DuelIntent
                {
                    Kind = setDef ? DuelIntentKind.SetMonsterDef : DuelIntentKind.NormalSummonAtk,
                    Card = card,
                    CardNameHint = name,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            // —— Set ——
            m = RxSet.Match(text);
            if (m.Success)
            {
                var name = CleanName(m.Groups["name"].Value);
                var card = FindCard(engine, who, name, preferHand: true, preferFieldSt: false, preferFieldMon: false);
                if (card?.Def != null && (card.Def.IsSpell || card.Def.IsTrap))
                {
                    return new DuelIntent
                    {
                        Kind = DuelIntentKind.SetSpellTrap,
                        Card = card,
                        CardNameHint = name,
                        RawText = utterance,
                        Source = "voice"
                    };
                }

                return new DuelIntent
                {
                    Kind = DuelIntentKind.SetMonsterDef,
                    Card = card,
                    CardNameHint = name,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            // —— Flip / change pos ——
            if (text.StartsWith("flip ") || text.Contains("flip summon"))
            {
                var name = CleanName(Regex.Replace(text, @"\b(?:i\s+)?flip(?:\s+summon)?\s+", "", RegexOptions.IgnoreCase));
                var card = FindCard(engine, who, name, preferHand: false, preferFieldSt: false, preferFieldMon: true);
                return new DuelIntent
                {
                    Kind = DuelIntentKind.FlipSummon,
                    Card = card,
                    CardNameHint = name,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            if (text.Contains("change position") || text.Contains("switch position") || text.Contains("to defense") ||
                text.Contains("to attack"))
            {
                var name = CleanName(Regex.Replace(text,
                    @"\b(?:change|switch)\s+position\s+(?:of\s+)?|\bto\s+(?:defense|attack)(?:\s+position)?\b",
                    " ", RegexOptions.IgnoreCase));
                name = name.Trim();
                var card = string.IsNullOrEmpty(name)
                    ? null
                    : FindCard(engine, who, name, preferHand: false, preferFieldSt: false, preferFieldMon: true);
                return new DuelIntent
                {
                    Kind = DuelIntentKind.ChangePosition,
                    Card = card,
                    CardNameHint = name,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            // —— Attack with X ——
            m = RxAttackWith.Match(text);
            if (m.Success && !text.StartsWith("i summon") && !text.Contains("summon"))
            {
                var name = CleanName(m.Groups["name"].Value);
                var tName = m.Groups["target"].Success ? CleanName(m.Groups["target"].Value) : null;
                if (tName != null && (tName.Contains("direct") || tName == "you" || tName == "me"))
                    tName = null;
                var atk = FindCard(engine, who, name, preferHand: false, preferFieldSt: false, preferFieldMon: true);
                CardInstance tgt = null;
                if (!string.IsNullOrEmpty(tName) && !IsDirectWord(tName))
                    tgt = FindOpponentMonster(engine, who, tName);
                var direct = string.IsNullOrEmpty(tName) || IsDirectWord(tName);
                return new DuelIntent
                {
                    Kind = direct && tgt == null ? DuelIntentKind.DirectAttack : DuelIntentKind.Attack,
                    Card = atk,
                    Target = tgt,
                    CardNameHint = name,
                    TargetNameHint = tName,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            // —— "X, attack!" / "X attacks Y" ——
            m = RxAttackNamed.Match(text);
            if (m.Success && text.Contains("attack"))
            {
                var name = CleanName(m.Groups["name"].Value);
                // Avoid false positives like "I activate..."
                if (name.StartsWith("i ") || name.Contains("activate") || name.Contains("summon"))
                    return null;
                var tName = m.Groups["target"].Success ? CleanName(m.Groups["target"].Value) : null;
                var atk = FindCard(engine, who, name, preferHand: false, preferFieldSt: false, preferFieldMon: true);
                CardInstance tgt = null;
                var direct = tName == null || IsDirectWord(tName);
                if (!direct)
                    tgt = FindOpponentMonster(engine, who, tName);
                return new DuelIntent
                {
                    Kind = direct ? DuelIntentKind.DirectAttack : DuelIntentKind.Attack,
                    Card = atk,
                    Target = tgt,
                    CardNameHint = name,
                    TargetNameHint = tName,
                    RawText = utterance,
                    Source = "voice"
                };
            }

            return null;
        }

        static DuelIntent Intent(DuelIntentKind kind, string text) =>
            new() { Kind = kind, RawText = text, Source = "voice" };

        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().ToLowerInvariant();
            // Curly/smart quotes → ASCII (string Replace avoids fragile char-literal escaping)
            s = s.Replace("\u2018", "'").Replace("\u2019", "'")
                .Replace("\u201C", "\"").Replace("\u201D", "\"");
            s = Regex.Replace(s, @"\s+", " ");
            // Strip common anime flourishes
            s = Regex.Replace(s, @"\b(?:my\s+)?(?:turn|move)\b[:!]?\s*", "");
            return s.Trim();
        }

        static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().Trim('!', '?', '.', ',', '"', '\'');
            s = s.Replace("\u2018", "").Replace("\u2019", "")
                .Replace("\u201C", "").Replace("\u201D", "");
            s = Regex.Replace(s, @"\s+in\s+(attack|defense|atk|def)(\s+position)?$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^(the|a|an)\s+", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        static bool IsPass(string t) =>
            t is "pass" or "no" or "nope" or "skip" or "go ahead" or "go on" or
                "no response" or "no traps" or "nothing" or "i pass" or "pass response";

        static bool IsEndTurn(string t) =>
            t.Contains("end my turn") || t.Contains("end the turn") || t == "end turn" ||
            t == "i end my turn" || t.Contains("turn end");

        static bool IsBattle(string t) =>
            t.Contains("battle phase") || t == "battle" || t.Contains("enter battle") ||
            t.Contains("go to battle");

        static bool IsMain2(string t) =>
            t.Contains("main phase 2") || t.Contains("main 2") || t == "mp2";

        static bool IsDirectOnly(string t) =>
            t is "direct attack" or "attack directly" or "i attack directly";

        static bool IsDirectWord(string t) =>
            t != null && (t.Contains("direct") || t is "you" or "life points" or "lp");

        /// <summary>Fuzzy match a spoken name against visible/legal cards.</summary>
        public static CardInstance FindCard(
            DuelEngine engine,
            DuelistState who,
            string spokenName,
            bool preferHand,
            bool preferFieldSt,
            bool preferFieldMon)
        {
            if (string.IsNullOrEmpty(spokenName)) return null;
            var candidates = new List<CardInstance>();
            if (preferHand)
                candidates.AddRange(who.Hand);
            if (preferFieldMon)
                candidates.AddRange(who.MonstersOnField());
            if (preferFieldSt)
                candidates.AddRange(who.SpellTrapsOnField());
            // Fallback scan
            if (candidates.Count == 0)
            {
                candidates.AddRange(who.Hand);
                candidates.AddRange(who.MonstersOnField());
                candidates.AddRange(who.SpellTrapsOnField());
            }

            return BestMatch(candidates, spokenName);
        }

        public static CardInstance FindOpponentMonster(DuelEngine engine, DuelistState who, string spokenName)
        {
            var opp = engine.OpponentOf(who);
            return BestMatch(opp.MonstersOnField().ToList(), spokenName);
        }

        static CardInstance BestMatch(List<CardInstance> cards, string spoken)
        {
            if (cards == null || cards.Count == 0 || string.IsNullOrEmpty(spoken)) return null;
            spoken = spoken.ToLowerInvariant();
            CardInstance best = null;
            var bestScore = 0;
            foreach (var c in cards)
            {
                if (c?.Name == null) continue;
                var n = c.Name.ToLowerInvariant();
                var score = NameScore(n, spoken);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            // Require a minimum confidence
            return bestScore >= 40 ? best : null;
        }

        static int NameScore(string cardName, string spoken)
        {
            if (cardName == spoken) return 100;
            if (cardName.Contains(spoken) || spoken.Contains(cardName)) return 90;
            // Token overlap
            var ct = cardName.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var st = spoken.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (st.Length == 0) return 0;
            var hits = st.Count(t => t.Length > 2 && ct.Any(c => c.StartsWith(t) || t.StartsWith(c) || c.Contains(t)));
            if (hits == 0) return 0;
            return 30 + (hits * 50 / st.Length);
        }
    }
}
