using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// FlipCostsAndFusion batch: whole-sentence atoms for FLIP effects with a cost (Fire Sorcerer, Lady Assailant of
    /// Flames), the Fusion recoveries (Supply, De-Fusion) and Summoner of Illusions. Each pattern is anchored to a
    /// sentence start and a sentence end, so a longer or different card never compiles part of its text here.
    /// Runtime: <see cref="FlipCostsAndFusionRuntime"/>.
    /// </summary>
    public static class FlipCostsAndFusionAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fire Sorcerer (c27132350): FLIP with SetCost — IsExistingMatchingCard(IsAbleToRemoveAsCost, hand, 2),
            // RandomSelect(tp, 2), Remove(POS_FACEUP, REASON_COST); effect Damage(1-tp, 800). With fewer than 2 cards in
            // hand the FLIP effect cannot be activated.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Randomly select (\d+) cards from your hand and remove them from play to inflict (\d+) " +
                @"points of damage to your opponent's Life Points" + End,
                m =>
                {
                    var n = Parse(m, 1, 0);
                    if (n < 1) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 2, 800),
                        CostBanishRandomHandCards = n,
                        MakesChainLink = true
                    };
                }),

            // Lady Assailant of Flames (c90147755): FLIP with SetCost — at least 3 cards in the Deck that can be
            // banished, GetDecktopGroup(tp, 3), Remove(POS_FACEUP, REASON_COST); effect Damage(1-tp, 800).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Banish the top (\d+) cards of your Deck;\s*inflict (\d+) damage to your opponent" + End,
                m =>
                {
                    var n = Parse(m, 1, 0);
                    if (n < 1) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 2, 800),
                        CostBanishTopDeckCards = n,
                        MakesChainLink = true
                    };
                }),

            // Supply (c44072894): FLIP, EFFECT_FLAG_CARD_TARGET — exactly 2 monsters in your GY with IsReason(REASON_FUSION)
            // (sent there as Fusion Material); with fewer than 2 it has no targets. At resolution both go to the hand
            // only while both still relate (#g == 2); an Extra Deck monster goes to the Extra Deck instead.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Return (\d+) Fusion-Material monsters that were sent to the Graveyard as a result of " +
                @"a Fusion Summon to your hand" + End,
                m =>
                {
                    var n = Parse(m, 1, 2);
                    if (n < 2) return null; // "monsters" is plural: a single target is another sentence
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.AddFromGyToHand,
                        Zone = EffectZoneFilter.ControllerGyMonsters,
                        Side = EffectSide.Controller,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = n,
                        TargetUpTo = false,
                        RequiresSentAsFusionMaterial = true,
                        MakesChainLink = true
                    };
                }),

            // Summoner of Illusions (c14644902): FLIP, no target. SelectReleaseGroup 1 other monster (an effect Tribute)
            // that leaves room for a Fusion Monster, Release(REASON_EFFECT) > 0, then SpecialSummon 1 Fusion Monster
            // from the Extra Deck (POS_FACEUP, not a Fusion Summon) and register on it an End Phase Destroy (reset when
            // it leaves the field).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Tribute 1 other monster, and if you do, Special Summon 1 Fusion Monster from your " +
                @"Extra Deck, but destroy it during the End Phase of this turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // De-Fusion (c95286165): Quick-Play, EFFECT_FLAG_CARD_TARGET — 1 face-up Fusion Monster in either Monster
            // Zone; SendtoDeck (to the Extra Deck), then, if it was Fusion Summoned and every material is in your GY as
            // its material, can be Special Summoned, and fits your free zones, SelectYesNo → SpecialSummon all of them.
            new AtomRegistration.Atom(
                Start + @"Target 1 Fusion Monster on the field;\s*return that target to the Extra Deck, then, if all " +
                @"the Fusion Materials that were used for its Fusion Summon are in your GY, you can Special Summon " +
                @"all of them" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials,
                    Zone = EffectZoneFilter.FieldFaceUpFusionMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
