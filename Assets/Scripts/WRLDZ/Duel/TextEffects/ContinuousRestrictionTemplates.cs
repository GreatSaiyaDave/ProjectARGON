using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Continuous S/T restrictions and GY-revive Continuous Traps that fully map
    /// onto shared kinds (cannot attack, pay-or-destroy, Call of the Haunted).
    /// </summary>
    public static class ContinuousRestrictionTemplates
    {
        static readonly Regex RxGravityBind = new(
            @"Level (\d+) or higher monsters cannot attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxInsectBarrier = new(
            @"(\w+)(?:-Type)? monsters your opponent controls cannot declare an attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCannotAttackAtk = new(
            @"Monsters with (\d+) or more ATK cannot declare an attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivateByPayingLp = new(
            @"Activate this card by paying (\d+) (?:LP|Life Points)\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Standby-Phase "pay N or destroy this card" upkeep across its phrasing variants:
        /// "During your Standby Phase, pay 500 LP or destroy this card" (Messenger of Peace),
        /// "During each of your Standby Phases, pay 2000 LP or destroy this card"
        /// (Fairy Box / Mirror Wall / Mask of Brutality), and the "you must pay N
        /// (this is not optional), or this card is destroyed" form (Skull Archfiend of
        /// Lightning / Imperial Order).
        /// </summary>
        static readonly Regex RxPayOrDestroyStandby = new(
            @"(?:Once per turn,?\s*)?during (?:each of your |the |your )Standby Phases?,?\s*" +
            @"(?:you must )?pay (\d+) (?:LP|Life Points)(?: \(this is not optional\))?,?\s*" +
            @"(?:or destroy this card|or this card is destroyed)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Spirit's Invitation phrasing: "Pay N Life Points during each of your Standby
        /// Phases. If you do not, destroy this card." Same pay-or-destroy upkeep.
        /// </summary>
        static readonly Regex RxPayThenDestroyStandby = new(
            @"Pay (\d+) (?:LP|Life Points) during each of your Standby Phases\.\s*" +
            @"If you do not, destroy this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCallOfTheHaunted = new(
            @"Activate this card by targeting 1 monster in your (?:GY|Graveyard);\s*" +
            @"Special Summon that target in Attack Position\.\s*" +
            @"When this card leaves the field, destroy that monster\.\s*" +
            @"When that monster is destroyed, destroy this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSoulResurrection = new(
            @"Activate this card by targeting 1 Normal Monster in your (?:GY|Graveyard);\s*" +
            @"Special Summon it in Defense Position\.\s*" +
            @"When this card leaves the field, destroy that monster\.\s*" +
            @"When that monster is destroyed, destroy this card\.?",
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

            var gb = RxGravityBind.Match(text);
            Add(gb, gb.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Amount = Parse(gb, 1, 4),
                    AmountIsLevel = true,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var ib = RxInsectBarrier.Match(text);
            Add(ib, ib.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    RaceFilter = ib.Groups[1].Value,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var atk = RxCannotAttackAtk.Match(text);
            Add(atk, atk.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Amount = Parse(atk, 1, 1500),
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var playPay = RxActivateByPayingLp.Match(text);
            Add(playPay, playPay.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    PayLpAmount = Parse(playPay, 1, 1000),
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var pay = RxPayOrDestroyStandby.Match(text);
            if (!pay.Success) pay = RxPayThenDestroyStandby.Match(text);
            Add(pay, pay.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.PayLpOrDestroyThis,
                    PayLpAmount = Parse(pay, 1, 100),
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);

            var call = RxCallOfTheHaunted.Match(text);
            Add(call, call.Success
                ? GyReviveClause(defense: false, normalOnly: false)
                : null);

            var soul = RxSoulResurrection.Match(text);
            Add(soul, soul.Success
                ? GyReviveClause(defense: true, normalOnly: true)
                : null);
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxGravityBind.IsMatch(text) || RxInsectBarrier.IsMatch(text) ||
                RxCannotAttackAtk.IsMatch(text))
                need.Add(EffectActionKind.ContinuousCannotAttack);
            if (RxPayOrDestroyStandby.IsMatch(text) || RxPayThenDestroyStandby.IsMatch(text))
                need.Add(EffectActionKind.PayLpOrDestroyThis);
            if (RxCallOfTheHaunted.IsMatch(text) || RxSoulResurrection.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
        }

        static EffectClause GyReviveClause(bool defense, bool normalOnly)
        {
            return new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromGy,
                Zone = EffectZoneFilter.ControllerGyMonsters,
                RequiresTargetChoice = true,
                StaysOnField = true,
                DestroyHostWhenThisLeaves = true,
                SummonInDefense = defense,
                RequiresNormalMonster = normalOnly,
                MakesChainLink = true
            };
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }
    }
}
