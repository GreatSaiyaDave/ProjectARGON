using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DestinyBoard": Destiny Board and the Spirit Messages. Shared sentence shapes, not a passcode list.
    /// <list type="bullet">
    /// <item>The placer (Destiny Board's whole text, three sentences that name each other): "When this card and all N
    /// "S" cards with different names are placed on your field, you win the Duel. Once per turn, during your
    /// opponent's End Phase: Place 1 "S" card from your hand or Deck in your Spell &amp; Trap Zone face-up, in the
    /// proper order of "a", "b", … and "z". When any "S" card or "B" you control leaves the field, send all "S" cards
    /// and "B" you control to the Graveyard." → the field win
    /// (<see cref="EffectActionKind.WinWithThisAndNamedSetFaceUpOnField"/>), the in-order End Phase placement
    /// (<see cref="EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck"/>) and the leave-field send
    /// (<see cref="EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField"/>). The named cards are "S" + " " + each
    /// quoted letter ("Spirit Message "I"" …), and the printed count must equal the number of letters.</item>
    /// <item>The placed card: "This card can only be placed on the field by the effect of "X"." A rule, no effect: the
    /// compiler absorbs it as structural text (<see cref="IsPlacementLockSentence"/>), the engine forbids activating
    /// or Setting the card (<see cref="DestinyBoardRuntime.PlacerOf"/>), and CardEffectStatus calls it Structural only
    /// while X compiles to a placement that names it (<see cref="DestinyBoardRuntime.HasLivePlacementPath"/>).</item>
    /// </list>
    /// Deliberately does not match a placement in any order, a placement during your own End Phase, a win with the
    /// cards in the hand, or a leave-field rule that destroys instead of sending.
    /// </summary>
    public static class DestinyBoardAtoms
    {
        static readonly Regex RxLetters = new Regex(@"""([^""]+)""", RegexOptions.Compiled);

        static readonly Regex RxPlacementLock = new Regex(
            @"^\s*This card can only be placed on the field by the effect of ""([^""]+)""\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The whole sentence "This card can only be placed on the field by the effect of "X"."</summary>
        public static bool IsPlacementLockSentence(string sentence) =>
            !string.IsNullOrEmpty(sentence) && RxPlacementLock.IsMatch(sentence);

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            AtomRegistration.Atom.Sequence(
                @"^\s*When this card and all (\d+) ""([^""]+)"" cards with different names are placed on your field, " +
                @"you win the Duel\.\s*" +
                @"Once per turn, during your opponent's End Phase: Place 1 ""\2"" card from your hand or Deck in your " +
                @"Spell & Trap Zone face-up, in the proper order of ((?:""[^""]+"",\s*)+and ""[^""]+"")\.\s*" +
                @"When any ""\2"" card or ""([^""]+)"" you control leaves the field, send all ""\2"" cards and ""\4"" " +
                @"you control to the (?:GY|Graveyard)\.?\s*$",
                m =>
                {
                    var series = m.Groups[2].Value;
                    var names = new List<string>();
                    foreach (Match l in RxLetters.Matches(m.Groups[3].Value))
                        names.Add(series + " \"" + l.Groups[1].Value + "\"");
                    if (!int.TryParse(m.Groups[1].Value, out var count) || count < 2 || count != names.Count)
                        return null; // fail closed: the printed count is not the listed order
                    var named = string.Join("|", names);
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.WinWithThisAndNamedSetFaceUpOnField,
                            Side = EffectSide.Controller,
                            NamedCard = named,
                            TargetSeriesName = series,
                            Amount = count,
                            StaysOnField = true,
                            MakesChainLink = false,
                            EffectNegatable = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.EndPhase,
                            Action = EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck,
                            Side = EffectSide.Controller,
                            OpponentTurnOnly = true,
                            OncePerTurn = true,
                            NamedCard = named,
                            TargetSeriesName = series,
                            Amount = count,
                            StaysOnField = true,
                            MakesChainLink = true
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField,
                            Side = EffectSide.Controller,
                            NamedCard = m.Groups[4].Value,
                            TargetSeriesName = series,
                            StaysOnField = true,
                            MakesChainLink = false
                        }
                    };
                }),
        };
    }
}
