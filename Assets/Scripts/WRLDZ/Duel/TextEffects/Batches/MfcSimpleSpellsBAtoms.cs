namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "MfcSimpleSpellsB" (v174): two of the MFC spells left over from the v168
    /// MfcSimpleSpells batch whose atoms were written against invented card texts and were removed in
    /// the 2026-10-07 audit. Each atom below is anchored to the real printed text, and the v168
    /// regression test's honest fail-closed pins for these two are replaced by real implementations.
    /// <list type="bullet">
    /// <item>Emblem of Dragon Destroyer (6390406): add 1 "Buster Blader" from the Deck or the GY to the hand.</item>
    /// <item>Hidden Spellbook (21840375): target 2 Spell Cards in the GY, shuffle those targets into the Deck.</item>
    /// </list>
    /// Thunder of Ruler (91781589) stays fail-closed on purpose: its only timing is the opponent's
    /// Standby Phase, and the engine has no activation path for a Set Normal Trap on that timing —
    /// FirePhaseTriggersFor skips face-down cards, and CanActivateOfficial refuses manual activation
    /// of a program with no Activate timing. Compiling it Implemented would put a dead card into
    /// decks. Queued until a Standby priority window for the non-turn player exists.
    /// Each atom consumes the whole sentence (or the whole card, for the two-sentence ones) so the
    /// generic sentence loop never re-parses a fragment. Runtime: <see cref="MfcSimpleSpellsBRuntime"/>.
    /// </summary>
    public static class MfcSimpleSpellsBAtoms
    {
        const string SentenceStart = @"(?<=^|\.\s)";
        const string SentenceEnd = @"(?:\.|$)(?=\s|$)";

        /// <summary>
        /// "Buster Blader" — the card is named with the double quotes in the printed text, so the
        /// quoted-name matcher (<see cref="TextEffectRuntime.NameMatchesQuoted"/>) is used rather than
        /// a bare string compare; "Buster Blader" also matches the series "Buster Blader" cards.
        /// </summary>
        const string BusterBlader = "\"Buster Blader\"";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Emblem of Dragon Destroyer (6390406): add 1 "Buster Blader" from the Deck or the GY to the hand.
            // Deck first, then GY (ygopro s.target LOCATION_DECK + LOCATION_GRAVE, resolved in that order).
            new AtomRegistration.Atom(
                SentenceStart + @"Add 1 " + BusterBlader + @" from your Deck or Graveyard to your hand" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddNamedFromDeckOrGyToHand,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerDeckOrGyNamed,
                    NamedCard = "Buster Blader",
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    TargetUpTo = false,
                    MakesChainLink = true
                }),

            // Hidden Spellbook (21840375): during your turn, target 2 Spell Cards in your GY; shuffle
            // those targets into the Deck (ygopro s.target LOCATION_GRAVE + s.target LOCATION_DECK + s.shuffle).
            new AtomRegistration.Atom(
                SentenceStart + @"During your turn:\s*Target 2 Spell Cards in your Graveyard;\s*" +
                @"shuffle those targets into the Deck" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ShuffleGySpellsIntoDeck,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGySpells,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 2,
                    TargetUpTo = false,
                    OpponentTurnOnly = false,
                    MakesChainLink = true
                }),
        };
    }
}