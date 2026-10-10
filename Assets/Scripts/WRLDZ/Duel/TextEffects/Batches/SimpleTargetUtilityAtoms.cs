using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleTargetUtility" (v173): five small utility cards whose printed text otherwise falls
    /// through the generic PSCT path or needs a whole-sentence shape the shared regexes do not match.
    /// <list type="bullet">
    /// <item>Altar for Tribute (c21070956): send 1 monster you control as the cost, gain LP equal to its
    /// printed original ATK.</item>
    /// <item>Arsenal Summoner (c85489096): FLIP, add 1 "Guardian" card from the Deck minus five excepted names.</item>
    /// <item>Cost Down (c23265313): discard, then reduce the Levels of the hand by 2 for the rest of this turn.</item>
    /// <item>Des Feral Imp (c81985784): FLIP, target 1 card in your GY, shuffle it into the Deck.</item>
    /// <item>Fairy of the Spring (c20188127): target 1 Equip Spell in your GY, add it to the hand; that card
    /// cannot be activated this turn.</item>
    /// </list>
    /// Each atom consumes the FULL sentence (or both sentences of the two-sentence cards) so the generic
    /// sentence loop never re-parses a fragment. Runtime: <see cref="SimpleTargetUtilityRuntime"/>.
    /// </summary>
    public static class SimpleTargetUtilityAtoms
    {
        const string SentenceStart = @"(?<=^|\.\s)";
        const string SentenceEnd = @"(?:\.|$)(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Altar for Tribute (c21070956): both sentences are one clause — the "select" sentence IS the
            // cost pick (RequiresSendOtherYouControl, no target choice), the second reads its printed ATK.
            new AtomRegistration.Atom(
                SentenceStart + @"Select 1 monster on your side of the field and send it to the Graveyard\.\s*" +
                @"Increase your Life Points by an amount equal to the original ATK of the monster" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainLpEqualToCostAmount,
                    Side = EffectSide.Controller,
                    RequiresSendOtherYouControl = true,
                    RequiresTargetChoice = false,
                    MakesChainLink = true
                }),

            // Cost Down (c23265313): one discard cost clause; Amount (the printed reduction) is stamped on
            // the hand at resolution by SimpleTargetUtilityRuntime.ApplyHandLevelStamp.
            new AtomRegistration.Atom(
                SentenceStart + @"Discard 1 card;\s*for the rest of this turn, reduce the Levels of all " +
                @"monsters in your hand by (\d+) \(even after they are Summoned\)" + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReduceHandLevelsUntilEndOfTurn,
                    Side = EffectSide.Controller,
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = "*",
                    Amount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),

            // Des Feral Imp (c81985784): FLIP target in the controller's GY, shuffled into the Deck.
            new AtomRegistration.Atom(
                SentenceStart + @"FLIP:\s*Target 1 card in your Graveyard;\s*shuffle that target into the Deck" +
                SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ReturnChosenToDeckShuffle,
                    Zone = EffectZoneFilter.ControllerGyCards,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    TargetUpTo = false,
                    MakesChainLink = true
                }),

            // Arsenal Summoner (c85489096): FLIP Guardian series search from the Deck; the five quoted except
            // names are captured, matched with Regex, and stored pipe-separated (Consumed by the shared
            // ExceptNamedCard filter in TextEffectRuntime.CollectTargets).
            new AtomRegistration.Atom(
                SentenceStart + @"FLIP:\s*Add 1 ""Guardian"" card from your Deck to your hand, except " +
                @"(?<list>.+)" + SentenceEnd,
                m =>
                {
                    var quoted = Regex.Matches(m.Groups["list"].Value, "\"([^\"]+)\"");
                    if (quoted.Count == 0) return null;
                    var names = new string[quoted.Count];
                    for (var i = 0; i < quoted.Count; i++)
                        names[i] = quoted[i].Groups[1].Value;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.AddFromDeckToHand,
                        Zone = EffectZoneFilter.ControllerDeckCards,
                        Side = EffectSide.Controller,
                        RequiresTargetChoice = true,
                        TargetSeriesName = "Guardian",
                        ExceptNamedCard = string.Join("|", names),
                        MakesChainLink = true
                    };
                }),

            // Fairy of the Spring (c20188127): both sentences are one clause — the GY Equip add and its
            // this-turn activation lock resolve together (LocksAddedCardActivationThisTurn stamps the
            // added instance in the AddFromGyToHand arm).
            new AtomRegistration.Atom(
                SentenceStart + @"Target 1 Equip Spell Card in your Graveyard;\s*add that target to your hand\.\s*" +
                @"That Equip Spell Card cannot be activated this turn" + SentenceEnd,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddFromGyToHand,
                    Zone = EffectZoneFilter.ControllerGyEquipSpells,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    TargetUpTo = false,
                    LocksAddedCardActivationThisTurn = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || group >= m.Groups.Count) return fallback;
            var g = m.Groups[group];
            if (g == null || !g.Success || !int.TryParse(g.Value, out var v)) return fallback;
            return v;
        }
    }
}
