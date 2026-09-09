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

        static readonly Regex RxPayOrDestroyStandby = new(
            @"(?:Once per turn, )?during (?:each of )?your Standby Phase(?:s)?[,]? pay (\d+) (?:LP|Life Points) or destroy this card\.?",
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

        /// <summary>
        /// Premature Burial: pay LP, SS from your GY, equip this, destroy equipped when this is destroyed.
        /// Call of the Haunted sibling (same SS + DestroyHostWhenThisLeaves hook). Does not match
        /// Fulfillment / Re-Fusion (banish equipped) or Autonomous Action Unit (opponent GY).
        /// </summary>
        static readonly Regex RxPrematureBurial = new(
            @"Activate this card by paying (\d+) (?:LP|Life Points), then target 1 monster in your (?:GY|Graveyard);\s*" +
            @"Special Summon that target in Attack Position and equip it with this card\.\s*" +
            @"When this card is destroyed, destroy the equipped monster\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Necrovalley / Imperial Iron Wall-shape: Cards in the GY cannot be banished,
        /// or Neither player can banish cards from the GYs.
        /// </summary>
        static readonly Regex RxGyCannotBanish = new(
            @"(?:Cards in the (?:GY|Graveyard)s? cannot be banished|" +
            @"Neither player can banish (?:cards? )?from (?:the )?(?:GYs?|Graveyards?))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Official Necrovalley: Cards in the GY cannot be targeted, except by the
        /// effect of "Necrovalley". Except group is optional (hard lock).
        /// </summary>
        static readonly Regex RxGyCannotTarget = new(
            @"Cards in the (?:GY|Graveyard)s? cannot be targeted(?:, except by (?:the effect of )?""([^""]+)"")?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Older Necrovalley leftover: Negate any card effect that would move a
        /// card in the GY to a different place. Shared CannotTargetCardsInGraveyard
        /// (except this card / Necrovalley). Fail-loud if only the ATK aura compiles.
        /// </summary>
        static readonly Regex RxGyNegateMove = new(
            @"Negate any card effect that would move a card in the (?:GY|Graveyard) to a different place\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Older Necrovalley leftover: Types/Attributes in the GY.</summary>
        static readonly Regex RxGyNegateTypeAttr = new(
            @"Negate any card effect that changes Types or Attributes in the (?:GY|Graveyard)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Spellbinding Circle family: target 1 opp monster; it cannot attack or change
        /// battle position while this stays face-up. When that monster is destroyed,
        /// this is destroyed (equip-link without DestroyHostWhenThisLeaves).
        /// Shadow Spell / Nightmare Wheel share the lock but carry extra riders.
        /// </summary>
        static readonly Regex RxSpellbindingCircle = new(
            @"Activate this card by targeting 1 monster your opponent controls;\s*" +
            @"it cannot attack or change its battle position\.\s*" +
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

            var premature = RxPrematureBurial.Match(text);
            Add(premature, premature.Success
                ? GyReviveClause(defense: false, normalOnly: false, payLp: Parse(premature, 1, 800))
                : null);

            Add(RxGyCannotBanish.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBanishFromGraveyard,
                StaysOnField = true,
                MakesChainLink = false
            });

            var gyT = RxGyCannotTarget.Match(text);
            Add(gyT, gyT.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotTargetCardsInGraveyard,
                    ExceptNamedCard = gyT.Groups[1].Success ? gyT.Groups[1].Value : def?.name,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var gyMove = RxGyNegateMove.Match(text);
            var gyType = RxGyNegateTypeAttr.Match(text);
            if (!gyT.Success && (gyMove.Success || gyType.Success))
            {
                var first = gyMove.Success ? gyMove : gyType;
                Add(first, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotTargetCardsInGraveyard,
                    ExceptNamedCard = def?.name,
                    StaysOnField = true,
                    MakesChainLink = false
                });
                if (gyMove.Success && gyType.Success)
                    spans?.Add((gyType.Index, gyType.Length));
            }
            else
            {
                if (gyMove.Success) spans?.Add((gyMove.Index, gyMove.Length));
                if (gyType.Success) spans?.Add((gyType.Index, gyType.Length));
            }

            var bind = RxSpellbindingCircle.Match(text);
            if (bind.Success)
            {
                Add(bind, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    Zone = EffectZoneFilter.OppMonsters,
                    RequiresTargetChoice = true,
                    StaysOnField = true,
                    DestroyThisWhenBoundHostDestroyed = true,
                    AlsoCannotChangeBattlePosition = true,
                    MakesChainLink = true
                });
                into.Add(new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotAttack,
                    StaysOnField = true,
                    DestroyThisWhenBoundHostDestroyed = true,
                    AlsoCannotChangeBattlePosition = true,
                    MakesChainLink = false,
                    SourceSnippet = "bound monster cannot attack or change battle position"
                });
            }
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (RxGravityBind.IsMatch(text) || RxInsectBarrier.IsMatch(text) ||
                RxCannotAttackAtk.IsMatch(text) || RxSpellbindingCircle.IsMatch(text))
                need.Add(EffectActionKind.ContinuousCannotAttack);
            if (RxPayOrDestroyStandby.IsMatch(text))
                need.Add(EffectActionKind.PayLpOrDestroyThis);
            if (RxCallOfTheHaunted.IsMatch(text) || RxSoulResurrection.IsMatch(text) ||
                RxPrematureBurial.IsMatch(text))
                need.Add(EffectActionKind.SpecialSummonFromGy);
            if (RxGyCannotBanish.IsMatch(text))
                need.Add(EffectActionKind.CannotBanishFromGraveyard);
            if (RxGyCannotTarget.IsMatch(text) || RxGyNegateMove.IsMatch(text) ||
                RxGyNegateTypeAttr.IsMatch(text))
                need.Add(EffectActionKind.CannotTargetCardsInGraveyard);
        }

        static EffectClause GyReviveClause(bool defense, bool normalOnly, int payLp = 0)
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
                SummonInAttack = !defense,
                RequiresNormalMonster = normalOnly,
                PayLpAmount = payLp,
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
