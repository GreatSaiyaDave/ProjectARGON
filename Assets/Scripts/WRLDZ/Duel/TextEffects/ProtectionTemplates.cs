using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared continuous protections. Fisherman / Kay'est / Deepsea Warrior / Torpedo Fish
    /// are one kind (unaffected + cannot be attack-targeted), not unique scripts.
    /// New cards with the same PSCT compile here with no cardId branch.
    /// </summary>
    public static class ProtectionTemplates
    {
        /// <summary>
        /// Legendary Fisherman: While "Umi" is on the field, this card is unaffected by
        /// Spell effects and cannot be targeted for attacks, but does not prevent direct.
        /// </summary>
        static readonly Regex RxNamedUnaffectedAndNoAttack = new(
            @"(?:While|As long as) ""([^""]+)"" is(?: face-up)? on the field, this card is " +
            @"unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects) and cannot be " +
            @"targeted for attacks(?:, but does not prevent your opponent from attacking you directly)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Guardian Kay'est: same pair of protections with no named-field lock.
        /// </summary>
        static readonly Regex RxUnaffectedAndNoAttack = new(
            @"This card is unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects) and " +
            @"cannot be targeted for attacks(?:, but does not prevent your opponent from attacking you directly)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Deepsea Warrior / Torpedo Fish / Cannonball Spear Shellfish:
        /// While "Umi" is face-up on the field, this card is unaffected by any Spell Cards.
        /// </summary>
        static readonly Regex RxNamedUnaffected = new(
            @"(?:While|As long as) ""([^""]+)"" is(?: face-up)? on the field, this card is " +
            @"unaffected by (?:any )?(Spell|Trap|monster) (?:Cards|effects)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxHorusServant = new(
            @"Your opponent cannot target face-up ""([^""]+)"" monsters with Spells, Traps, or card effects\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Marauding Captain: Your opponent cannot target Race monsters for attacks, except this one.
        /// Extends CannotBeAttackTarget with RaceFilter + ExceptThisCard (scan other monsters).
        /// </summary>
        static readonly Regex RxCannotAttackTargetRaceExceptThis = new(
            @"Your opponent cannot target (\w+)(?:-Type)? monsters for attacks, except this one\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Fox Fire leftover: This face-up card cannot be Tributed for a Tribute Summon.
        /// Ignis c88753985: EFFECT_UNRELEASABLE_SUM.
        /// </summary>
        static readonly Regex RxCannotTributeForSummon = new(
            @"This face-up card cannot be Tributed for a Tribute Summon\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Spirit Reaper PSCT: After resolving a card effect that targets this face-up card, destroy this card.
        /// </summary>
        static readonly Regex RxDestroyAfterResolvingTarget = new(
            @"After resolving a card effect that targets this(?: face-up)? card,\s*destroy this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Older / close variants (Reaper on the Nightmare, Arcana Force 0 shape):
        /// Destroy this card when it is targeted by a card effect /
        /// If this card is targeted by an effect, destroy it.
        /// </summary>
        static readonly Regex RxDestroyWhenTargeted = new(
            @"(?:Destroy this card when it is targeted by (?:the effect of )?(?:a Spell, Trap, or Effect Monster|a card effect|an? effect)|If this card is targeted by an? (?:card )?effect,\s*destroy it)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Unconditional this-card battle destruction protection.
        /// Sentence-start only so "While ATK position…" / "with a monster that has 1900 ATK" stay refuse.
        /// </summary>
        static readonly Regex RxCannotBeDestroyedByBattle = new(
            @"(?:^|(?<=[.!?]\s))" +
            @"(?:Cannot be destroyed by battle(?! with| or)|" +
            @"This card (?:cannot be destroyed by battle(?! with| or)|is not destroyed as a result of battle))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPiercingThis = new(
            @"(?:If|When) this card attacks a Defense Position monster, inflict piercing battle damage(?: to your opponent)?\.?",
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

            var namedBoth = RxNamedUnaffectedAndNoAttack.Match(text);
            if (namedBoth.Success)
            {
                var allowsDirect = Regex.IsMatch(namedBoth.Value,
                    @"does not prevent your opponent from attacking you directly",
                    RegexOptions.IgnoreCase);
                var filter = namedBoth.Groups[2].Value;
                Add(namedBoth, UnaffectedClause(filter, namedBoth.Groups[1].Value));
                into.Add(NoAttackClause(namedBoth.Groups[1].Value, allowsDirect, namedBoth.Value));
            }

            var both = RxUnaffectedAndNoAttack.Match(text);
            if (both.Success)
            {
                var allowsDirect = Regex.IsMatch(both.Value,
                    @"does not prevent your opponent from attacking you directly",
                    RegexOptions.IgnoreCase);
                Add(both, UnaffectedClause(both.Groups[1].Value, null));
                into.Add(NoAttackClause(null, allowsDirect, both.Value));
            }

            Add(RxNamedUnaffected.Match(text),
                UnaffectedFromNamed(RxNamedUnaffected.Match(text)));

            var horus = RxHorusServant.Match(text);
            Add(horus, horus.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeTargetedByEffects,
                    NamedCard = horus.Groups[1].Value,
                    MakesChainLink = false
                }
                : null);

            var raceAtk = RxCannotAttackTargetRaceExceptThis.Match(text);
            Add(raceAtk, raceAtk.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeAttackTarget,
                    RaceFilter = raceAtk.Groups[1].Value,
                    ExceptThisCard = true,
                    MakesChainLink = false
                }
                : null);

            Add(RxCannotTributeForSummon.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBeTributedForSummon,
                MakesChainLink = false
            });

            Add(RxDestroyAfterResolvingTarget.Match(text), DestroyAfterTargetingClause());
            Add(RxDestroyWhenTargeted.Match(text), DestroyAfterTargetingClause());
            Add(RxCannotBeDestroyedByBattle.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBeDestroyedByBattle,
                MakesChainLink = false
            });
            Add(RxPiercingThis.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PiercingBattleDamage,
                MakesChainLink = false
            });
        }

        public static bool MatchesSharedKind(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return RxNamedUnaffectedAndNoAttack.IsMatch(text) ||
                   RxUnaffectedAndNoAttack.IsMatch(text) ||
                   RxNamedUnaffected.IsMatch(text) ||
                   RxHorusServant.IsMatch(text) ||
                   RxCannotAttackTargetRaceExceptThis.IsMatch(text) ||
                   RxCannotTributeForSummon.IsMatch(text) ||
                   RxDestroyAfterResolvingTarget.IsMatch(text) ||
                   RxDestroyWhenTargeted.IsMatch(text) ||
                   RxCannotBeDestroyedByBattle.IsMatch(text) ||
                   RxPiercingThis.IsMatch(text);
        }

        public static void ExpectedActions(string text, List<EffectActionKind> need)
        {
            if (string.IsNullOrEmpty(text) || need == null) return;
            if (RxNamedUnaffectedAndNoAttack.IsMatch(text) || RxUnaffectedAndNoAttack.IsMatch(text))
            {
                need.Add(EffectActionKind.UnaffectedByCardEffects);
                need.Add(EffectActionKind.CannotBeAttackTarget);
            }
            else if (RxNamedUnaffected.IsMatch(text))
                need.Add(EffectActionKind.UnaffectedByCardEffects);
            if (RxHorusServant.IsMatch(text))
                need.Add(EffectActionKind.CannotBeTargetedByEffects);
            if (RxCannotAttackTargetRaceExceptThis.IsMatch(text))
                need.Add(EffectActionKind.CannotBeAttackTarget);
            if (RxCannotTributeForSummon.IsMatch(text))
                need.Add(EffectActionKind.CannotBeTributedForSummon);
            if (RxDestroyAfterResolvingTarget.IsMatch(text) || RxDestroyWhenTargeted.IsMatch(text))
                need.Add(EffectActionKind.DestroyThisAfterResolvingTargetingEffect);
            if (RxCannotBeDestroyedByBattle.IsMatch(text))
                need.Add(EffectActionKind.CannotBeDestroyedByBattle);
            if (RxPiercingThis.IsMatch(text))
                need.Add(EffectActionKind.PiercingBattleDamage);
        }

        static EffectClause DestroyAfterTargetingClause()
        {
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.DestroyThisAfterResolvingTargetingEffect,
                MakesChainLink = false
            };
        }

        static EffectClause UnaffectedFromNamed(Match m)
        {
            if (m == null || !m.Success) return null;
            return UnaffectedClause(m.Groups[2].Value, m.Groups[1].Value);
        }

        static EffectClause UnaffectedClause(string filter, string named)
        {
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.UnaffectedByCardEffects,
                UnaffectedByFilter = string.IsNullOrEmpty(filter) ? "Spell" : filter,
                RequiresFaceUpName = named,
                MakesChainLink = false
            };
        }

        static EffectClause NoAttackClause(string named, bool allowsDirect, string snippet)
        {
            return new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CannotBeAttackTarget,
                RequiresFaceUpName = named,
                AllowsDirectAttackWhileProtected = allowsDirect,
                MakesChainLink = false,
                SourceSnippet = snippet?.Trim()
            };
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
