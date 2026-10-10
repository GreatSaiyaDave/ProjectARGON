using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DiscardTriggers" (v142) whole-card atoms, each anchored at both ends so a longer or reworded
    /// card stays uncompiled:
    /// <list type="bullet">
    /// <item>Electric Snake (c11324436) / Minar (c32539892): a mandatory trigger when an effect of a card the
    /// opponent controls discards this card from its owner's hand (ygopro EVENT_TO_GRAVE, previous location hand,
    /// REASON_DISCARD|REASON_EFFECT, rp = 1-tp): draw 2 / inflict 1000 damage.</item>
    /// <item>Penguin Knight (c36039163): the same when it is sent directly from its owner's Deck to the GY by
    /// such an effect (milled or destroyed there; REASON_EFFECT, rp = 1-tp): its whole GY goes back to the Deck,
    /// which is shuffled.</item>
    /// <item>Delinquent Duo (c44763025): Normal Spell. Cost: pay 1000 LP. Legal only while the opponent holds a
    /// card. The opponent discards 1 random card, then, if they still hold one, 1 more of their own choice.</item>
    /// </list>
    /// Registered with one line in <see cref="AtomRegistration"/>'s BatchAtomSets.
    /// Runtime: <see cref="DiscardTriggersRuntime"/>.
    /// </summary>
    public static class DiscardTriggersAtoms
    {
        /// <summary>Either PSCT wording of "an effect of a card controlled by your opponent".</summary>
        const string ByOpponentEffect =
            @"by (?:an effect of a card controlled by your opponent|(?:an|your) opponent's card effect)";

        /// <summary>"When this card is discarded [directly] from your hand to the GY by …, " (Electric Snake, Minar).</summary>
        const string DiscardedByOpponent =
            @"^\s*(?:When|If) this card is discarded (?:directly )?from your hand to (?:the|your) (?:GY|Graveyard) " +
            ByOpponentEffect + @"(?:, |: )";

        /// <summary>"When this card is sent directly from your Deck to the GY by …, " (Penguin Knight).</summary>
        const string SentFromDeckByOpponent =
            @"^\s*(?:When|If) this card is sent (?:directly )?from your Deck to (?:the|your) (?:GY|Graveyard) " +
            ByOpponentEffect + @"(?:, |: )";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Electric Snake. The owner draws (Lua: Duel.Draw(tp, 2)).
            new AtomRegistration.Atom(
                DiscardedByOpponent + @"draw (\d+) cards?(?: from your Deck)?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.DiscardedFromHandByOpponentEffect,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),
            // Minar. Damage to the owner's opponent (Lua: Duel.Damage(1-tp, 1000)).
            new AtomRegistration.Atom(
                DiscardedByOpponent +
                @"inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.DiscardedFromHandByOpponentEffect,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 1000),
                    MakesChainLink = true
                }),
            // Penguin Knight. Every card in its owner's GY returns to the Deck, then the Deck is shuffled.
            new AtomRegistration.Atom(
                SentFromDeckByOpponent +
                @"(?:unite your (?:GY|Graveyard) cards and your own Deck, shuffle them and form a new Deck|" +
                @"shuffle all cards in your (?:GY|Graveyard) into (?:the|your) Deck)\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromDeckToGyByOpponentEffect,
                    Action = EffectActionKind.ShuffleGraveyardIntoDeck,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Delinquent Duo. Paying the LP is the cost; the opponent must hold a card to activate (Lua s.target
            // chk: GetFieldGroupCount(tp, 0, LOCATION_HAND) > 0). Nothing is targeted.
            new AtomRegistration.Atom(
                @"^\s*Pay (\d+) (?:LP|Life Points); your opponent discards 1 random card, and if they have any " +
                @"other cards in their hand, discard 1 more card of their choice\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice,
                    Side = EffectSide.Opponent,
                    PayLpAmount = Parse(m, 1, 1000),
                    RequiresOpponentHandAtLeast = 1,
                    MakesChainLink = true
                }),
        };

        /// <summary>
        /// Fear from the Dark (c34193084) and Despair from the Dark (c71200730). Both are the same sentence with a
        /// "When"/"If" difference: "When/If this card is sent from your hand or Deck to your Graveyard by an
        /// opponent's card effect: Special Summon this card." (ygopro: EVENT_TO_GRAVE, previous location hand OR
        /// deck, REASON_EFFECT, rp = 1-tp; SpecialSummon(pos 0/1, tp, tp, false, false, POS_FACEUP)).
        ///
        /// Two clauses, one per timing, so whichever way the card arrived fires the right queue entry
        /// (<see cref="EffectTiming.DiscardedFromHandByOpponentEffect"/> for the hand, see
        /// <see cref="EffectTiming.SentFromDeckToGyByOpponentEffect"/> for the Deck). Both resolve to the same
        /// <see cref="EffectActionKind.SpecialSummonThisFromGy"/> arm in <see cref="TextEffectRuntime.ApplyClause"/>.
        /// Both are optional per PSCT ("You can"), though the trigger itself is mandatory; the arm checks
        /// <see cref="CanSpecialSummonFromGyTarget"/> (hard Nomi / Semi-Nomi / GY-lock) and fails closed.
        /// </summary>
        public static readonly AtomRegistration.Atom[] SelfSsFromGy =
        {
            AtomRegistration.Atom.Sequence(
                @"^\s*(?:When|If) this card is sent from your hand or Deck to (?:your|the) (?:GY|Graveyard) " +
                @"by (?:an effect of a card controlled by your opponent|(?:an|your) opponent's card effect)" +
                @":\s*Special Summon this card(?: from the (?:GY|Graveyard))?\.?\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.DiscardedFromHandByOpponentEffect,
                        Action = EffectActionKind.SpecialSummonThisFromGy,
                        Side = EffectSide.Controller,
                        IsOptional = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromDeckToGyByOpponentEffect,
                        Action = EffectActionKind.SpecialSummonThisFromGy,
                        Side = EffectSide.Controller,
                        IsOptional = true,
                        MakesChainLink = true
                    }
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
