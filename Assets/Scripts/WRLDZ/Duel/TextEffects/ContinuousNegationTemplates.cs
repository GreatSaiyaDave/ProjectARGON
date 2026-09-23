using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared trap-lock / trap-negate atoms (Jinzo, Royal Decree) plus the
    /// Equip-only-to-named + controller-trap exemption (Amplifier).
    /// No cardId branches.
    /// </summary>
    public static class ContinuousNegationTemplates
    {
        /// <summary>Jinzo first sentence — Traps cannot be activated.</summary>
        static readonly Regex RxTrapsCannotActivate = new(
            @"Trap Cards, and their effects on the field, cannot be activated\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Jinzo second sentence — negate face-up Trap effects (not "other").</summary>
        static readonly Regex RxNegateAllTrapEffects = new(
            @"Negate all Trap effects on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Royal Decree — negate other Trap effects; Traps may still activate.</summary>
        static readonly Regex RxNegateOtherTrapEffects = new(
            @"Negate all other Trap effects on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Amplifier family: Equip only to "X"; host does not negate controller Traps;
        /// leave destroys the equipped; activation/effect cannot be negated.
        /// </summary>
        static readonly Regex RxEquipNamedTrapExemption = new(
            @"Equip only to ""([^""]+)""\.\s*" +
            @"While this card is equipped, the equipped monster's effect does not negate " +
            @"the effects of its controller's Trap Cards\.\s*" +
            @"When this card is removed from the field, destroy the equipped monster\.\s*" +
            @"This card's activation and effect cannot be negated\.?",
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

            var lockTraps = RxTrapsCannotActivate.Match(text);
            Add(lockTraps, lockTraps.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousCannotActivateTraps,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var other = RxNegateOtherTrapEffects.Match(text);
            if (other.Success)
            {
                Add(other, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousNegateFaceUpTraps,
                    Side = EffectSide.Both,
                    NegateOtherOnly = true,
                    StaysOnField = true,
                    MakesChainLink = false
                });
            }
            else
            {
                var all = RxNegateAllTrapEffects.Match(text);
                Add(all, all.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousNegateFaceUpTraps,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                    : null);
            }

            var amp = RxEquipNamedTrapExemption.Match(text);
            Add(amp, amp.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    EquipHostName = amp.Groups[1].Value,
                    StaysOnField = true,
                    MakesChainLink = true,
                    DestroyHostWhenThisLeaves = true,
                    ExemptControllerTrapsFromHostNegation = true,
                    ActivationNegatable = false,
                    EffectNegatable = false
                }
                : null);
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
