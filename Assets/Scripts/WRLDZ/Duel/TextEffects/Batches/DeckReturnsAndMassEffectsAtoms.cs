using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DeckReturnsAndMassEffects" (v161 LOD): whole-text atoms, not a passcode list. Each pattern is the
    /// card's whole text (anchored at both ends), so a longer or shorter card with the same phrase is not absorbed.
    /// <list type="bullet">
    /// <item>"FLIP: Each player shuffles all cards from their hand, field, and Graveyard into the Deck, then draws N
    /// cards." (Fiber Jar)</item>
    /// <item>"FLIP: Target 1 face-up monster on the field, and if you control a face-up Type monster, you can target 1
    /// additional face-up monster on the field; return the first target to the top of the Deck, then return the
    /// additional target (if any) to the hand if you still control a face-up Type monster." (Mysterious Guard)</item>
    /// <item>"If this face-up card is returned from the field to your Deck, select and Special Summon 1 Level N or lower
    /// monster from your Deck to your side of the field. Then shuffle your Deck." (Serpentine Princess)</item>
    /// <item>"Each player chooses 1 monster they control and switches control of those monsters with each other. Those
    /// monsters cannot change their battle positions for the rest of this turn." (Creature Swap)</item>
    /// <item>"This card can only be activated when any player has N or more cards on the field and/or in their hand. The
    /// player(s) then select and send cards to their respective Graveyards until the amount remaining on the field and in
    /// their hand is M." (Bubble Crash)</item>
    /// </list>
    /// Runtime: <see cref="DeckReturnsAndMassEffectsRuntime"/>.
    /// </summary>
    public static class DeckReturnsAndMassEffectsAtoms
    {
        const string TypeWord = @"([A-Za-z]+(?:-(?!Type\b)[A-Za-z]+)*)(?:-Type)?";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Fiber Jar (c78706415): FLIP, no target. Hand, field and GY of both players to the Deck (not banished cards,
            // not a monster this battle destroys), shuffle, then the controller draws, then the opponent.
            new AtomRegistration.Atom(
                @"^\s*FLIP:\s*Each player shuffles all cards from their hand, field,? and (?:Graveyard|GY) into the " +
                @"Deck, then draws (\d+) cards?\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws,
                    Side = EffectSide.Both,
                    Amount = Pos(m, 1, 5),
                    MakesChainLink = true
                }),

            // Mysterious Guard (c1347977): FLIP, EFFECT_FLAG_CARD_TARGET. The runtime chooses the targets as the effect
            // activates (first: the Deck; optional additional one while you control a face-up monster of that Type: the
            // hand). Both Type words must be the same Type.
            new AtomRegistration.Atom(
                @"^\s*FLIP:\s*Target 1 face-up monster on the field, and if you control a face-up " + TypeWord +
                @" monster, you can target 1 additional face-up monster on the field;\s*return the first target to " +
                @"the top of the Deck, then return the additional target \(if any\) to the hand if you still control " +
                @"a face-up \1(?:-Type)? monster\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    ConditionName = m.Groups[1].Value.Trim(),
                    MakesChainLink = true
                }),

            // Serpentine Princess (c71829750): mandatory trigger (EFFECT_TYPE_TRIGGER_F, EVENT_TO_DECK; it was face-up on
            // the field). Select 1 Level N or lower monster in your Deck that can be Special Summoned; the Deck is
            // shuffled afterwards.
            new AtomRegistration.Atom(
                @"^\s*If this face-up card is returned from the field to your Deck, select and Special Summon 1 Level " +
                @"(\d+) or lower monster from your Deck to your side of the field\.\s*Then shuffle your Deck\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ReturnedFromFieldToDeck,
                    Action = EffectActionKind.SpecialSummonFromDeck,
                    Zone = EffectZoneFilter.ControllerDeckMonsters,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1, 3),
                    AmountIsLevel = true,
                    RequiresTargetChoice = true, // selected at resolution; not a PSCT target
                    IsPsctTarget = false,
                    IsOptional = false,
                    MakesChainLink = true
                }),

            // Creature Swap (c31036355): not targeted. Each player must control a monster to activate it; at resolution
            // the activator, then the opponent, chooses 1 they control; Duel.SwapControl; both locked this turn.
            new AtomRegistration.Atom(
                @"^\s*Each player chooses 1 monster they control and switches control of those monsters with each " +
                @"other\.\s*Those monsters cannot change their battle positions for the rest of this turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EachPlayerChoosesOwnMonsterSwitchControl,
                    Side = EffectSide.Both,
                    LocksPositionRestOfTurn = true,
                    ActivationCondition = DeckReturnsAndMassEffectsRuntime.EachPlayerControlsMonster,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Bubble Crash (c61622107): activation needs a player with N+ cards in hand + on the field (this card
            // counts). Each such player selects cards there (the activator: not this card) and sends them to the GY
            // until M remain; all are sent together, by rule.
            new AtomRegistration.Atom(
                @"^\s*This card can only be activated when any player has (\d+) or more cards on the field and/or in " +
                @"their hand\.\s*The player\(s\) then select and send cards to their respective (?:Graveyards|GYs) " +
                @"until the amount remaining on the field and in their hand is (\d+)\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EachPlayerSendsHandAndFieldToGyUntilAmount,
                    Side = EffectSide.Both,
                    ConditionAmount = Pos(m, 1, 6),
                    Amount = Pos(m, 2, 5),
                    ActivationCondition = DeckReturnsAndMassEffectsRuntime.AnyPlayerHandAndFieldAtLeast,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
            // Machine Duplication (c63995093), its whole text.
            new AtomRegistration.Atom(
                @"^Target 1 Machine monster you control with (\d+) or less ATK; Special Summon up to 2 monsters from " +
                @"your Deck with the same name as that face-up monster\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromDeck,
                    Zone = EffectZoneFilter.ControllerDeckMonsters,
                    Side = EffectSide.Controller,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    RaceFilter = "Machine",
                    Amount = int.Parse(m.Groups[1].Value),
                    AmountIsAtkMax = true,
                    TargetCount = 1,
                    SummonUpTo = 2,
                    SummonSameNameAsTarget = true,
                    MakesChainLink = true
                }),
        };

        static int Pos(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : fallback;
    }
}
