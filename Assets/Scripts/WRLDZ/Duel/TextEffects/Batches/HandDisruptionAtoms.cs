using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "HandDisruption" (v142): hand / draw manipulation. Each atom is the card's whole text (start
    /// of text or after a sentence end, through its final period), so a longer card that only contains part of
    /// it stays uncompiled. Runtime: <see cref="HandDisruptionRuntime"/>.
    /// · Gamble — hand-size activation condition, a called coin, draw until 5 or skip your next turn.
    /// · Appropriate — Continuous Trap activated only in the window after the opponent draws outside either Draw
    ///   Phase; while face-up, each such draw makes its controller draw 2.
    /// · Painful Choice — you select 5 cards from your Deck, your opponent picks the 1 you add to your hand, the
    ///   other 4 go to the GY.
    /// Deferred (stays unimplemented): Graverobber (no support for an opponent-owned card in your hand). Forced
    /// Requisition is EngineHygiene's (v160, the central discard event).
    /// </summary>
    public static class HandDisruptionAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text, or the end itself.</summary>
        const string E = @"(?:\.(?=\s|$)|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gamble (c37313786): Normal Trap, free chain. Activation condition (checked at activation only):
            // your hand 2 or less and your opponent's hand 6 or more. Resolution: Duel.CallCoin — right: draw
            // until your hand has 5 cards (5 - hand size at resolution, none if already 5+); wrong: skip your
            // next turn (EFFECT_SKIP_TURN, one turn however many copies).
            new AtomRegistration.Atom(
                S + @"You can only activate this card when your opponent's hand is (\d+) or more cards and your " +
                @"hand is (\d+) or less\.\s*Toss a coin and call heads or tails\.\s*If you call it right, " +
                @"draw until your hand has (\d+) cards\.\s*If you call it wrong, skip your next turn" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.CoinCallDrawUntilOrSkipNextTurn,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 3, 5),
                    RequiresOpponentHandAtLeast = Parse(m, 1, 6),
                    ActivationCondition = HandDisruptionRuntime.HandSizes,
                    ConditionAmount = Parse(m, 2, 2),
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Appropriate (c48539234): Continuous Trap. Its activation (EVENT_DRAW, ep ~= tp, not the Draw Phase)
            // is legal only in the window HandDisruptionRuntime opens after the opponent drew outside either Draw
            // Phase; the activation itself does nothing but leave the card face-up. While it is face-up (and not
            // negated), each time the opponent draws outside either Draw Phase its controller draws 2
            // (EFFECT_TYPE_CONTINUOUS: no Chain Link).
            new AtomRegistration.Atom(
                S + @"Activate only when your opponent draws a card\(s\) outside of either Draw Phase\.\s*" +
                @"After that, each time your opponent draws a card\(s\) outside of either Draw Phase, " +
                @"immediately draw (\d+) cards" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    ActivationCondition = HandDisruptionRuntime.OpponentDrewOutsideDrawPhase,
                    CheckedAt = ConditionCheckedAt.Activation,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Painful Choice (c74191942): Normal Spell. Needs 5 cards in the Deck to activate (Lua target chk).
            // Resolution: you select exactly 5 cards from your Deck and show them; your opponent selects 1 of them;
            // it is added to your hand and the other 4 are sent to the GY (a send from the Deck, not a discard).
            // Nothing happens if your Deck has fewer than 5 cards by then.
            new AtomRegistration.Atom(
                S + @"Select (\d+) cards from your Deck and show them to your opponent\.\s*Your opponent selects 1 " +
                @"card among them\.\s*Add that card to your hand and discard the remaining cards to the " +
                @"(?:Graveyard|GY)" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RevealDeckCardsOpponentPicksOneToHand,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 5),
                    ActivationCondition = HandDisruptionRuntime.ControllerDeckAtLeast,
                    ConditionAmount = Parse(m, 1, 5),
                    CheckedAt = ConditionCheckedAt.Activation,
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
