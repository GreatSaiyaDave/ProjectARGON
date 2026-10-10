namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Shared sentence shapes, not a passcode list. Each atom is the card's whole text.
    /// <list type="bullet">
    /// <item>Send 1 Type monster from your Deck to the GY; inflict N damage to your opponent.</item>
    /// <item>Discard 1 card from your hand and declare 1 Type of monster. Destroy all face-up monsters of the declared Type on the field.</item>
    /// <item>Destroy all tokens on the field. Increase your Life Points by the number of tokens destroyed x N points.</item>
    /// </list>
    /// The discard-and-declare sentence and the token sentence are the cards_db prints, which are not modern PSCT.
    /// Deliberately does not match a send from the hand or the field, a mill of the top of the Deck, damage "for each",
    /// Token Feastevil's "inflict damage for each Token", or
    /// "Declare 1 Type of monster. Monsters of the declared Type cannot attack."
    /// </summary>
    public static class RockSentenceAtoms
    {
        const string TypeWord = @"([A-Za-z]+(?:-[A-Za-z]+)*)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Cost is the Deck send. The burn is flat. Not "for each".
            new AtomRegistration.Atom(
                @"^\s*Send 1 " + TypeWord + @" monster from your Deck to the GY;\s*" +
                @"inflict (\d+) damage to your opponent\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Pos(m, 2),
                    RaceFilter = m.Groups[1].Value,
                    RequiresSendRaceFromDeck = true,
                    MakesChainLink = true
                }),

            // cards_db print. Not the modern "Discard 1 card; declare 1 Type."
            AtomRegistration.Atom.Sequence(
                @"^\s*Discard 1 card from your hand and declare 1 Type of monster\.\s*" +
                @"Destroy all face-up monsters of the declared Type on the field\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.DeclareMonsterType,
                        RequiresDiscardCost = true,
                        DiscardCostAttribute = "*",
                        DiscardCostCount = 1,
                        RequiresTargetChoice = true,
                        ChoiceDoesNotTarget = true,
                        IsPsctTarget = false,
                        Zone = EffectZoneFilter.DeclaredMonsterTypes,
                        Side = EffectSide.Both,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.FieldMonsters,
                        Side = EffectSide.Both,
                        RaceFilterIsDeclared = true,
                        RequiresTargetChoice = false,
                        MakesChainLink = true
                    }
                }),

            // cards_db print. Not "Destroy as many Tokens as possible, and if you do, inflict damage".
            new AtomRegistration.Atom(
                @"^\s*Destroy all tokens on the field\.\s*Increase your Life Points by the number of tokens destroyed x (\d+) points\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyTokensInflictPer,
                    Amount = Pos(m, 1),
                    TokenDestroyGainsLp = true,
                    MakesChainLink = true
                }),
        };

        static int Pos(System.Text.RegularExpressions.Match m, int group)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return 0;
            return int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;
        }
    }
}
