namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Coverage family F2 (v180, FusionSubstitutes): the "You can substitute this card for any 1 Fusion Material
    /// Monster." ability (<see cref="FusionSubstitutesAtoms"/>). Read from the fusion material search in
    /// SpellTrapEffects.TryCollectMaterials: at most one named material may be satisfied by such a card, and every other
    /// material must be the correct printed one (the official "the other material(s) must be the correct one(s)" rule).
    /// </summary>
    public static class FusionSubstitutesRuntime
    {
        /// <summary>
        /// True when <paramref name="card"/> is a face-up substitute ability carrier — its text program is FullyCompiled
        /// and carries the <see cref="EffectActionKind.FusionSubstitute"/> clause. Only compiled programs count, so a
        /// substitute whose own text is not fully compiled does not enable a fusion.
        /// </summary>
        public static bool IsFusionSubstitute(CardInstance card)
        {
            if (card?.Def == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (prog == null || !prog.FullyCompiled) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.Action == EffectActionKind.FusionSubstitute)
                    return true;
            return false;
        }
    }
}
