namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "MfcSimpleSpells" (v168). The v168 atoms were written against invented card texts
    /// (and the regression test against placeholder ids that are not in cards_db at all), so none of
    /// them ever matched a real card; they were removed in the 2026-10-07 audit. The six cards' real
    /// printed texts are pinned fail-closed by
    /// <c>Rules/Batches/MfcSimpleSpellsRegressionTests.cs</c>:
    /// <list type="bullet">
    /// <item>Emblem of Dragon Destroyer (6390406): Add 1 "Buster Blader" from your Deck or Graveyard to your hand.</item>
    /// <item>Hidden Spellbook (21840375): During your turn: Target 2 Spell Cards in your Graveyard; shuffle those
    /// targets into the Deck.</item>
    /// <item>Dark Magician Girl (38033121): Gains 300 ATK for every "Dark Magician" or "Magician of Black Chaos"
    /// in the GY — compiled by <see cref="MfcLabCards3Atoms"/> (real-text atom).</item>
    /// <item>Mass Driver (34906152): Tribute 1 monster on your side of the field to inflict 400 points of damage
    /// to your opponent's Life Points — stays fail-closed (gated by WholeResolutionRegressionTests).</item>
    /// <item>Thunder of Ruler (91781589): During your opponent's Standby Phase: You can activate this card; there
    /// is no Battle Phase this turn.</item>
    /// <item>Anti-Spell (53112492): Remove 2 Spell Counters on your side of the field to negate the activation of
    /// a Spell Card and destroy it — stays fail-closed (gated by WholeResolutionRegressionTests and
    /// SpellCounterRegressionTests: a cost fragment must never compile alone).</item>
    /// </list>
    /// The enum members the removed atoms referenced (DestroyOneInHandOneOnFieldOneInDeck,
    /// GainAtkDefPerNamedInBothGyWhileFaceUp, InflictDamagePer1000TributedAtk, SkipControllerNextStandbyPhase,
    /// ContinuousCannotActivateSpells, ContinuousNegateSpellEffectsOnField) stay append-only and are kept even
    /// where no card compiles to them yet.
    /// </summary>
    public static class MfcSimpleSpellsAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
        };
    }
}
