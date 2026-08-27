using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>Standby / End Phase trigger templates from official text.</summary>
    public static class PhaseTriggerTemplates
    {
        static readonly Regex RxStandbyDmgSelf = new(
            @"during your standby phase:\s*take (\d+) damage",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStandbyDmgOpp = new(
            @"during your standby phase:\s*inflict (\d+) damage to your opponent",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxStandbyGainLp = new(
            @"during your standby phase:\s*(?:you gain|increase your life points by) (\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Pre-PSCT: "increase your Life Points by 800 points during each of your Standby Phases."
        /// Cure Mermaid / Spirit of the Breeze / Dancing Fairy.
        /// </summary>
        static readonly Regex RxStandbyGainLpEach = new(
            @"(?:As long as this card remains (?:in )?face-up(?: Attack Position| Defense Position)?" +
            @"(?: on your side of the field)?,?\s*)?" +
            @"increase your Life Points by (\d+) points during each of your Standby Phases\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAsLongAsAtk = new(
            @"As long as this card remains in face-up Attack Position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAsLongAsDef = new(
            @"As long as this card remains in face-up Defense Position",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPikeru = new(
            @"During your Standby Phase, increase your Life Points by (\d+) points " +
            @"for each monster on your side of the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxLvSendSs = new(
            @"during (?:your standby phase|the end phase)[^.]{0,80}?:\s*you can send this (?:face-up )?card to the (?:GY|Graveyard);\s*" +
            @"special summon 1 ""([^""]+)"" from your hand or deck",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSpiritBounce = new(
            @"during the end phase of the turn (?:this card (?:is|was) |that this card (?:is|was) )?" +
            @"(?:normal summoned or flipped face-up|this card is normal summoned or flipped face-up)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxReturnHand = new(
            @"return (?:it|this card) to (?:the owner's|its owner's|the) hand",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def,
            System.Collections.Generic.List<EffectClause> into,
            System.Collections.Generic.List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            Add(RxStandbyDmgSelf.Match(text), new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = Parse(RxStandbyDmgSelf.Match(text), 1, 1000),
                MakesChainLink = true
            });
            Add(RxStandbyDmgOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.InflictDamageToOpponent,
                Amount = Parse(RxStandbyDmgOpp.Match(text), 1, 600),
                MakesChainLink = true
            });
            var lp = RxStandbyGainLp.Match(text);
            Add(lp, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.GainLifePoints,
                Amount = lp.Success ? Parse(lp, 1, 200) : 200,
                ResolvesFromGy = Regex.IsMatch(text, @"must be in the graveyard", RegexOptions.IgnoreCase),
                MakesChainLink = true
            });

            var each = RxStandbyGainLpEach.Match(text);
            if (each.Success && !lp.Success)
            {
                Add(each, new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainLifePoints,
                    Amount = Parse(each, 1, 800),
                    RequiresThisAttackPosition = RxAsLongAsAtk.IsMatch(text),
                    RequiresThisDefensePosition = RxAsLongAsDef.IsMatch(text),
                    MakesChainLink = true
                });
            }

            var pik = RxPikeru.Match(text);
            Add(pik, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.GainLifePoints,
                Amount = pik.Success ? Parse(pik, 1, 400) : 400,
                ScaleAmountByControllerMonsters = true,
                MakesChainLink = true
            });

            var lv = RxLvSendSs.Match(text);
            if (lv.Success)
            {
                var end = Regex.IsMatch(lv.Value, @"end phase", RegexOptions.IgnoreCase);
                Add(lv, new EffectClause
                {
                    Timing = end ? EffectTiming.EndPhase : EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonNamed,
                    NamedCard = lv.Groups[1].Value,
                    RequiresSendThisToGy = true,
                    FromHand = true,
                    FromDeck = true,
                    IsOptional = true,
                    RequiresDestroyedByBattleThisTurn = end &&
                        Regex.IsMatch(lv.Value, @"destroyed a monster by battle", RegexOptions.IgnoreCase),
                    MakesChainLink = true
                });
            }

            if (RxSpiritBounce.IsMatch(text) && RxReturnHand.IsMatch(text))
            {
                var m = RxSpiritBounce.Match(text);
                Add(m, new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ReturnToHand,
                    RequiresSummonedOrFlippedThisTurn = true,
                    MakesChainLink = true
                });
            }
        }

        public static void ExpectedActions(CardDef def, System.Collections.Generic.List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxStandbyGainLp.IsMatch(text) || RxStandbyGainLpEach.IsMatch(text) ||
                RxPikeru.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }
    }
}
