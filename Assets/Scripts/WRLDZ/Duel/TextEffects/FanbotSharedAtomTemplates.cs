using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared atoms for Soul Exchange, The Shallow Grave, and die-scale until-end
    /// ATK/DEF (Graceful Dice / Skull Dice). No cardId branches.
    /// </summary>
    public static class FanbotSharedAtomTemplates
    {
        /// <summary>
        /// Soul Exchange: tribute the targeted opponent monster as if you controlled it
        /// this turn (not a take-control); skip Battle Phase if printed.
        /// </summary>
        static readonly Regex RxSoulExchange = new(
            @"Target 1 monster your opponent controls;\s*" +
            @"this turn, if you Tribute a monster, you must Tribute that target, " +
            @"as if you controlled it\.\s*" +
            @"You cannot conduct your Battle Phase the turn you activate this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The Shallow Grave family — each player SS 1 from their GY face-down DEF.</summary>
        static readonly Regex RxShallowGrave = new(
            @"Each player targets 1 monster in their own (?:GY|Graveyard);\s*" +
            @"each player Special Summons the target from their (?:GY|Graveyard) " +
            @"in face-down Defense Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Graceful Dice — your monsters gain ATK/DEF × die until end of turn.</summary>
        static readonly Regex RxDieGainYou = new(
            @"Roll a six-sided die\.\s*" +
            @"All monsters you currently control gain ATK(?:/DEF| and DEF) " +
            @"equal to the result x (\d+), until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Skull Dice — opponent monsters lose ATK/DEF × die until end of turn.</summary>
        static readonly Regex RxDieLoseOpp = new(
            @"Roll a six-sided die\.\s*" +
            @"All monsters your opponent currently controls lose ATK(?:/DEF| and DEF) " +
            @"equal to the result x (\d+), until the end of this turn\.?",
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

            var se = RxSoulExchange.Match(text);
            Add(se, se.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GrantTributeAsIfControlledUntilEnd,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    SkipBattlePhaseThisTurn = true,
                    MakesChainLink = true
                }
                : null);

            var sg = RxShallowGrave.Match(text);
            Add(sg, sg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    EachPlayerTargetsOwnGy = true,
                    SummonInDefense = true,
                    SummonFaceDown = true,
                    MakesChainLink = true
                }
                : null);

            var gain = RxDieGainYou.Match(text);
            if (gain.Success)
            {
                var n = Parse(gain, 1, 100);
                Add(gain, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                    Amount = -n,
                    DefAmount = -n,
                    ScaleByDieRoll = true,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.FieldMonsters,
                    MakesChainLink = true
                });
            }

            var lose = RxDieLoseOpp.Match(text);
            if (lose.Success)
            {
                var n = Parse(lose, 1, 100);
                Add(lose, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LoseAtkDefUntilEndOfTurn,
                    Amount = n,
                    DefAmount = n,
                    ScaleByDieRoll = true,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldMonsters,
                    MakesChainLink = true
                });
            }
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
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
