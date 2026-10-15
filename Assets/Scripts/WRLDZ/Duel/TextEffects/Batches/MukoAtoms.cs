using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Muko (c80863132). The whole card is one sentence, and its activation is a property of the link it answers
    /// rather than of Muko itself, so the clause carries the answer as a timing and
    /// <see cref="MukoRuntime"/> reads the answered link to decide and to resolve.
    /// </summary>
    public static class MukoAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            new AtomRegistration.Atom(
                Start + @"Activate only when an effect of drawing cards?\(?s?\)? is activated\.\s*Both players see " +
                @"the cards drawn by the effect and discard them all to the Graveyard" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.ReplaceDrawLinkWithDiscard,
                    Side = EffectSide.Controller,
                    ChainResponseOnly = true,
                    MakesChainLink = true
                }),
        };
    }
}
