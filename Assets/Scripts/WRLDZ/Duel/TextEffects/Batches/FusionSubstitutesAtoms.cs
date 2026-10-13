using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Coverage family F2 (v180, FusionSubstitutes): the "You can substitute this card for any 1 Fusion Material
    /// Monster." ability — Mystical Sheep #1, Versago the Destroyer, Goddess with the Third Eye, Beastking of the
    /// Swamps, and The Light / Dark / Earth - Hex-Sealed Fusion. It is a static (CONTINUOUS) ability on the monster
    /// itself; <see cref="FusionSubstitutesRuntime.IsFusionSubstitute"/> reads it from the fusion material search
    /// (SpellTrapEffects.TryCollectMaterials), where such a card may satisfy at most one named material.
    /// </summary>
    public static class FusionSubstitutesAtoms
    {
        const string SentenceStart = @"(?:^|(?<=[.!?)]\s))";
        const string SentenceEnd = @"\.?(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Mystical Sheep #1 (c30451366, "Fusion-Material") and Versago the Destroyer (c50259460) / Goddess with the
            // Third Eye (c53493204) / Beastking of the Swamps (c99426834, "Fusion Material"): the ability and its
            // "the other material(s) must be the correct one(s)" rider.
            new AtomRegistration.Atom(
                SentenceStart + @"You can substitute this card for any 1 Fusion[- ]Material Monster\.\s*" +
                @"When you do this, the other Fusion[- ]Material Monster\(s\) must be the correct one\(s\)\." + SentenceEnd,
                _ => Substitute()),

            // The Light / Dark / Earth - Hex-Sealed Fusion (c15717011 / c52101615 / c88696724): the same ability,
            // worded as a single sentence. (Their second sentence — tribute materials on the field to Special Summon a
            // corresponding Attribute Fusion — is not compiled yet, so those three stay fail-closed.)
            new AtomRegistration.Atom(
                SentenceStart + @"You can substitute this card for any 1 Fusion[- ]Material Monster, " +
                @"but the other Fusion[- ]Material Monster\(s\) must be (?:the )?correct\." + SentenceEnd,
                _ => Substitute()),
        };

        static EffectClause Substitute() => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.FusionSubstitute,
            Side = EffectSide.Controller,
            StaysOnField = true,
            MakesChainLink = false
        };
    }
}
