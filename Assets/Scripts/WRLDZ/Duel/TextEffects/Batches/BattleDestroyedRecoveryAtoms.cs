using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// BattleDestroyedRecovery (v153). Since v162 the cards it parked compile through
    /// <see cref="BattleDestroyedGyRecoveryAtoms"/> (GY-entry stamp, <see cref="BattleDestroyedGyRecoveryRuntime"/>);
    /// <see cref="IsFailClosedPark"/> now parks only a text none of those whole-text atoms matches. Original note: there
    /// was no runtime for: A recovery that looks a monster up from
    /// a battle destruction outside the resolving chain has nothing to dispatch:
    /// a monster "destroyed as a result of battle during this turn" (this turn, not "that turn"), or this
    /// card coming back "during your next turn's Standby Phase" after it was destroyed by battle.
    /// <see cref="IsFailClosedPark"/> is that shared rule — not a card-name list and not a list of whole
    /// prints — so the next card with the same sentence parks the same way. The compiler then drops every
    /// clause and leaves the whole text unparsed, so the card is not FullyCompiled and a fragment cannot
    /// fall through to a legacy activation. Fairy Guardian's "sent by your opponent's card effect" sentence
    /// is a different stamp and does not match.
    /// </summary>
    public static class BattleDestroyedRecoveryAtoms
    {
        /// <summary>No live atoms. Compiling one would be FullyCompiled with no resolution.</summary>
        public static readonly AtomRegistration.Atom[] Atoms = System.Array.Empty<AtomRegistration.Atom>();

        /// <summary>
        /// True when the text asks for a battle-destroyed recovery this engine cannot stamp.
        /// A straight or typographic apostrophe both count. Matching parks the whole card.
        /// </summary>
        public static bool IsFailClosedPark(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!RxDestroyedByBattleThisTurn.IsMatch(text) && !RxNextStandbySelfRevival.IsMatch(text)) return false;
            // BattleDestroyedGyRecovery (v162): the GY-entry stamp now exists; a text one of its whole-text atoms
            // compiles is not parked. A reworded or longer text still is.
            foreach (var atom in BattleDestroyedGyRecoveryAtoms.Atoms)
                if (atom.Pattern.IsMatch(text))
                    return false;
            return true;
        }

        /// <summary>
        /// "1 of your monsters destroyed as a result of battle during this turn" and Return of the Doomed's
        /// "destroyed and sent to your Graveyard as a result of battle during this turn". "during that turn"
        /// (Human-Wave Tactics) does not match.
        /// </summary>
        static readonly Regex RxDestroyedByBattleThisTurn = new(
            @"destroyed (?:and sent to (?:your|the) (?:GY|Graveyard) )?as a result of battle during this turn",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Revival Jam's family: optional pay, then Special Summon the destroyed card itself on your next
        /// Standby. An immediate "destroyed by battle" Special Summon from the Deck does not match.
        /// </summary>
        static readonly Regex RxNextStandbySelfRevival = new(
            @"destroyed by battle and sent to the (?:GY|Graveyard): You can pay \d+ (?:Life Points|LP); " +
            @"Special Summon it\b.*\bduring your next turn['’]s Standby Phase",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
