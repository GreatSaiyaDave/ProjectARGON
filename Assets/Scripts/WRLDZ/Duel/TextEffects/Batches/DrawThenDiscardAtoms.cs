using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DrawThenDiscard": Graceful Charity (79571449), "Draw 3 cards, then discard 2 cards." The atom is
    /// the card's whole text, anchored at both ends, so a longer card with the same sentence (Spiral Spear Strike's
    /// Gaia-conditioned "Draw 2 cards, then discard 1 card.") never matches it: that card has its own whole-text atom
    /// (BattleRidersAtoms, a battle-damage trigger reusing <see cref="DrawThenDiscardRuntime"/>).
    /// ygopro c79571449: EFFECT_TYPE_ACTIVATE, target <c>Duel.IsPlayerCanDraw(tp,3)</c>; operation
    /// <c>if Duel.Draw(p,d,REASON_EFFECT)==3 then ShuffleHand; BreakEffect; DiscardHand(p,nil,2,2,REASON_EFFECT|REASON_DISCARD)</c>.
    /// Before this atom the generic PSCT "draw N cards" fallback compiled only the draw (a free +2).
    /// Runtime: <see cref="DrawThenDiscardRuntime"/>.
    /// </summary>
    public static class DrawThenDiscardAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Graceful Charity (79571449).
            new AtomRegistration.Atom(
                @"^\s*Draw (\d+) cards?, then discard (\d+) cards?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DrawThenDiscard,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1),
                    ThenDiscardCount = Parse(m, 2),
                    // ygopro IsPlayerCanDraw(tp, N): N cards in the Deck and no "cannot draw" effect.
                    ActivationCondition = DrawThenDiscardRuntime.CanDrawAmount,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 1;
    }
}
