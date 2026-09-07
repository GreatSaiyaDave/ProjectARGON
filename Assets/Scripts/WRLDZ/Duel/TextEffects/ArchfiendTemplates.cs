using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Archfiend archetype text that maps onto shared kinds: the die-roll targeting
    /// protection (roll a d6 as an opponent's targeting effect resolves; on certain
    /// faces negate it and destroy the opponent's card), Battle-Scarred (mirror the
    /// Standby maintenance payment to the opponent and destroy-link to the chosen
    /// Archfiend), and Archfiend's Roar (pay 500, revive an "Archfiend" from the GY that
    /// cannot be Tributed and is destroyed in the End Phase).
    /// </summary>
    public static class ArchfiendTemplates
    {
        // "When this card is targeted …" / "When an Archfiend Monster Card on your side
        // of the field is targeted …, when resolving the effect, roll a six-sided die.
        // If the result is X, negate the effect and destroy the opponent's card."
        static readonly Regex RxWhenTargetedDie = new(
            @"When (this card|an Archfiend Monster Card on your side of the field) is targeted " +
            @"by the effect of a card controlled by your opponent, when resolving the effect, " +
            @"roll a six-sided die\.\s*If the result is (.+?), negate the effect and destroy " +
            @"the opponent's card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Skull Archfiend of Lightning wording.
        static readonly Regex RxBeforeResolvingDie = new(
            @"Before resolving an opponent's card effect that targets this card, roll a " +
            @"six-sided die, negate the effect if you roll (.+?), and if you do, destroy that card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleScarred = new(
            @"Select 1 Archfiend monster on your side of the field to activate this card\.\s*" +
            @"Your opponent also pays the same Life Points that you pay for the selected monster " +
            @"during the Standby Phase\.\s*" +
            @"If this card is removed from the field, destroy the selected monster\.\s*" +
            @"When the selected monster is removed from the field, destroy this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxArchfiendsRoar = new(
            @"Pay (\d+) Life Points, then target 1 ""Archfiend"" monster in your (?:GY|Graveyard);\s*" +
            @"Special Summon that target\.\s*It cannot be Tributed\.\s*" +
            @"Destroy it during the End Phase of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            var dieTargeted = RxWhenTargetedDie.Match(text);
            Add(dieTargeted, dieTargeted.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DieRollNegateWhenTargeted,
                    DieNegateFacesMask = ParseDieFacesMask(dieTargeted.Groups[2].Value),
                    ProtectsAllArchfiendsOnField =
                        dieTargeted.Groups[1].Value.IndexOf("Archfiend",
                            System.StringComparison.OrdinalIgnoreCase) >= 0,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var dieBefore = RxBeforeResolvingDie.Match(text);
            Add(dieBefore, dieBefore.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DieRollNegateWhenTargeted,
                    DieNegateFacesMask = ParseDieFacesMask(dieBefore.Groups[1].Value),
                    ProtectsAllArchfiendsOnField = false,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var scarred = RxBattleScarred.Match(text);
            Add(scarred, scarred.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    TargetSeriesName = "Archfiend",
                    RequiresTargetChoice = true,
                    MirrorStandbyPaymentToOpponent = true,
                    DestroyHostWhenThisLeaves = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var roar = RxArchfiendsRoar.Match(text);
            Add(roar, roar.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    TargetSeriesName = "Archfiend",
                    RequiresTargetChoice = true,
                    PayLpAmount = Parse(roar, 1, 500),
                    SummonCannotBeTributed = true,
                    SummonDestroyAtEndPhase = true,
                    MakesChainLink = true
                }
                : null);
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxWhenTargetedDie.IsMatch(text) || RxBeforeResolvingDie.IsMatch(text))
                need.Add(EffectActionKind.DieRollNegateWhenTargeted);
            if (RxBattleScarred.IsMatch(text))
                need.Add(EffectActionKind.EquipThisToTarget);
            if (RxArchfiendsRoar.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
        }

        /// <summary>Bitmask of six-sided faces (1–6) named in "1, 3, or 6" / "2 or 5" / "3".</summary>
        static int ParseDieFacesMask(string s)
        {
            var mask = 0;
            if (string.IsNullOrEmpty(s)) return mask;
            foreach (Match d in Regex.Matches(s, @"\d"))
            {
                if (int.TryParse(d.Value, out var face) && face >= 1 && face <= 6)
                    mask |= 1 << (face - 1);
            }
            return mask;
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }
    }
}
