using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Flip / battle-destroyed / delayed-GY Standby templates from official text.
    /// Shared families only — no cardId branches. Unique leftover (Charmers,
    /// Dice Jar, Penguin Soldier up-to-N, Revival Jam optional delayed pay) stays refuse.
    /// </summary>
    public static class MonsterTriggerTemplates
    {
        static readonly string[] Attributes =
            { "DARK", "LIGHT", "WATER", "FIRE", "EARTH", "WIND", "DIVINE" };

        static readonly Regex RxFlipGainAtkDef = new(
            @"FLIP:\s*This card gains (\d+) ATK and DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipInflict = new(
            @"FLIP:\s*Inflict (\d+) (?:points of )?damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipMillOpp = new(
            @"FLIP:\s*Send the top (\d+) cards of your opponent's Deck to the Graveyard\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipDestroyOppLevel = new(
            @"FLIP:\s*Destroy all Level (\d+) monsters your opponent controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipChangePos = new(
            @"FLIP:\s*Change the battle position of 1 face-up monster on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipBounceField = new(
            @"FLIP:\s*(?:Select|Target) 1 monster on the field(?: and return it to its owner's hand|; return (?:it|that target) to (?:its owner's |the owner's )?hand)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipBounceOpp = new(
            @"FLIP:\s*(?:Select|Target) 1 (?:Attack Position )?monster your opponent controls(?: and return it to its owner's hand|; return (?:it|that target) to (?:its owner's |the owner's |the )?hand)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFlipSummonInflict = new(
            @"When this card is Flip Summoned:\s*Inflict (\d+) (?:points of )?damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleDestroyerLosesAtkDef = new(
            @"If this card is destroyed by battle:\s*The monster that destroyed it loses (\d+) ATK and DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleDestroyTheDestroyer = new(
            @"If this card is destroyed by battle and sent to the GY:\s*Destroy the monster that destroyed this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyTargetDestroy = new(
            @"If this card is destroyed by battle and sent to the GY:\s*Target 1 monster on the field;\s*destroy that target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyDestroyAllSt = new(
            @"If this card is destroyed by battle and sent to the GY:\s*Destroy all Spells and Traps on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyInflict = new(
            @"(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard)[:,]\s*" +
            @"Inflict (\d+) (?:points of )?damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyNamedSsDeck = new(
            @"(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*" +
            @"(?:You can )?Special Summon 1 ""([^""]+)"" from your Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyFilterSsDeck = new(
            @"(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*" +
            @"(?:You can )?Special Summon 1 (\w+)(?:-Type)? monster with (\d+) or less (ATK|DEF) from your Deck" +
            @"(?:, in (?:face-up )?Attack Position)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBattleGyAddNamed = new(
            @"(?:If|When) this card is destroyed by battle and sent to the (?:GY|Graveyard):\s*" +
            @"(?:You can )?add 1 ""([^""]+)"" from your Deck to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNextStandbySsAfterContinuousSpell = new(
            @"During your next Standby Phase after this card was sent from the field to the Graveyard " +
            @"by the effect of a Continuous Spell Card:\s*Special Summon this card from the Graveyard\.?",
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

            var gain = RxFlipGainAtkDef.Match(text);
            Add(gain, gain.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ApplyLingeringAtkDef,
                    Amount = Parse(gain, 1, 500),
                    DefAmount = Parse(gain, 1, 500),
                    MakesChainLink = true
                }
                : null);

            var flipDmg = RxFlipInflict.Match(text);
            Add(flipDmg, flipDmg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(flipDmg, 1, 500),
                    MakesChainLink = true
                }
                : null);

            var mill = RxFlipMillOpp.Match(text);
            Add(mill, mill.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.SendFromTopOfDeckToGy,
                    Side = EffectSide.Opponent,
                    Amount = Parse(mill, 1, 5),
                    MakesChainLink = true
                }
                : null);

            var lv = RxFlipDestroyOppLevel.Match(text);
            Add(lv, lv.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Amount = Parse(lv, 1, 4),
                    AmountIsLevel = true,
                    RequiresTargetChoice = false,
                    MakesChainLink = true
                }
                : null);

            Add(RxFlipChangePos.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.ChangeBattlePosition,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            Add(RxFlipBounceField.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.ReturnToHand,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            Add(RxFlipBounceOpp.Match(text), new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.ReturnToHand,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            var fsDmg = RxFlipSummonInflict.Match(text);
            Add(fsDmg, fsDmg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(fsDmg, 1, 1000),
                    RequiresThisFlipSummoned = true,
                    MakesChainLink = true
                }
                : null);

            var lose = RxBattleDestroyerLosesAtkDef.Match(text);
            Add(lose, lose.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.ApplyLingeringAtkDef,
                    Amount = -Parse(lose, 1, 500),
                    DefAmount = -Parse(lose, 1, 500),
                    RequiresThisDestroyedByBattle = true,
                    ImplicitTargetIsBattleDestroyer = true,
                    RequiresTargetChoice = false,
                    MakesChainLink = true
                }
                : null);

            Add(RxBattleDestroyTheDestroyer.Match(text), new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.Destroy,
                RequiresThisDestroyedByBattle = true,
                ImplicitTargetIsBattleDestroyer = true,
                RequiresTargetChoice = false,
                MakesChainLink = true
            });

            Add(RxBattleGyTargetDestroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                RequiresThisDestroyedByBattle = true,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            Add(RxBattleGyDestroyAllSt.Match(text), new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Both,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresThisDestroyedByBattle = true,
                RequiresTargetChoice = false,
                MakesChainLink = true
            });

            var gyDmg = RxBattleGyInflict.Match(text);
            Add(gyDmg, gyDmg.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(gyDmg, 1, 500),
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }
                : null);

            var named = RxBattleGyNamedSsDeck.Match(text);
            Add(named, named.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonNamed,
                    NamedCard = named.Groups[1].Value,
                    FromDeck = true,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = Regex.IsMatch(named.Value, @"you can", RegexOptions.IgnoreCase),
                    MakesChainLink = true
                }
                : null);

            var filt = RxBattleGyFilterSsDeck.Match(text);
            if (filt.Success)
            {
                var kind = filt.Groups[1].Value;
                var cap = Parse(filt, 2, 1500);
                var atk = filt.Groups[3].Value.IndexOf("ATK", StringComparison.OrdinalIgnoreCase) >= 0;
                Add(filt, new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonNamed,
                    RaceFilter = IsAttribute(kind) ? null : kind,
                    AttributeFilter = IsAttribute(kind) ? kind : null,
                    Amount = cap,
                    AmountIsAtkMax = atk,
                    AmountIsDefMax = !atk,
                    FromDeck = true,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = Regex.IsMatch(filt.Value, @"you can", RegexOptions.IgnoreCase),
                    MakesChainLink = true
                });
            }

            var add = RxBattleGyAddNamed.Match(text);
            Add(add, add.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.AddNamedFromDeckToHand,
                    NamedCard = add.Groups[1].Value,
                    FromDeck = true,
                    Amount = 1,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = Regex.IsMatch(add.Value, @"you can", RegexOptions.IgnoreCase),
                    MakesChainLink = true
                }
                : null);

            Add(RxNextStandbySsAfterContinuousSpell.Match(text), new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.SpecialSummonFromGy,
                ResolvesFromGy = true,
                RequiresSentByContinuousSpell = true,
                RequiresNextControllerStandby = true,
                MakesChainLink = true
            });
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxFlipGainAtkDef.IsMatch(text) || RxBattleDestroyerLosesAtkDef.IsMatch(text))
                need.Add(EffectActionKind.ApplyLingeringAtkDef);
            if (RxFlipInflict.IsMatch(text) || RxFlipSummonInflict.IsMatch(text) ||
                RxBattleGyInflict.IsMatch(text))
                need.Add(EffectActionKind.InflictDamageToOpponent);
            if (RxFlipMillOpp.IsMatch(text))
                need.Add(EffectActionKind.SendFromTopOfDeckToGy);
            if (RxNextStandbySsAfterContinuousSpell.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
            if (RxFlipBounceField.IsMatch(text) || RxFlipBounceOpp.IsMatch(text))
                need.Add(EffectActionKind.ReturnToHand);
            if (RxBattleDestroyTheDestroyer.IsMatch(text) || RxBattleGyTargetDestroy.IsMatch(text) ||
                RxBattleGyDestroyAllSt.IsMatch(text) || RxFlipDestroyOppLevel.IsMatch(text))
                need.Add(EffectActionKind.Destroy);
        }

        static bool IsAttribute(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var a in Attributes)
                if (string.Equals(a, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
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
