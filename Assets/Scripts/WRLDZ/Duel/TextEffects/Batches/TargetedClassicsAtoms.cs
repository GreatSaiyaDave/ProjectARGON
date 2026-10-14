using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TargetedClassics batch: targeted one-shot removals and boosts whose whole text is one atom. Each pattern is
    /// anchored to a sentence start and a sentence end, so a longer or different card never compiles part of its text
    /// here. Before this batch the v151 WholeResolution fail-close left Remove Trap and Nobleman of Crossout
    /// Unimplemented (the generic PSCT "destroy it" fallback had destroyed any monster), and Deal of Phantom, Miracle
    /// Dig and Tornado Bird had no template. Runtime: <see cref="TargetedClassicsRuntime"/>.
    /// </summary>
    public static class TargetedClassicsAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        /// <summary>
        /// <see cref="EffectClause.CountSource"/> of Deal of Phantom's boost: every monster card in the controller's
        /// GY, counted when the effect resolves (the same name the GainSelfAtkPerCount aura uses).
        /// </summary>
        public const string MonstersInYourGy = "MonstersInYourGy";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Remove Trap (c51482758): EFFECT_FLAG_CARD_TARGET, IsFaceup() and IsTrap() on either field
            // (LOCATION_ONFIELD both sides); destroy it if it is still there. The old "Select 1 …" wording targets.
            new AtomRegistration.Atom(
                Start + @"Select 1 face-up Trap Card on the field and destroy it" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    // Face-up Trap Cards only: CollectTargets drops face-down cards (RequiresTargetFaceUp) and
                    // face-up cards of another kind (TargetCardKind); SpellTrapEffects.CollectLegalTargets mirrors
                    // both filters for the legacy Spell/Trap picker.
                    TargetCardKind = "Trap",
                    RequiresTargetFaceUp = true,
                    MakesChainLink = true
                }),

            // Nobleman of Crossout (c71044499): target 1 face-down monster in either Monster Zone; if it is still
            // face-down, Destroy(tc, REASON_EFFECT, LOCATION_REMOVED); only if that destroyed it and it is TYPE_FLIP,
            // banish every card with its code from both Decks (ConfirmCards both Decks), then shuffle both Decks.
            new AtomRegistration.Atom(
                Start + @"Target 1 face-down monster on the field;\s*destroy that target, and if you do, banish it, " +
                @"then, if it was a Flip monster, each player reveals their Main Deck, then banishes all cards from " +
                @"it with that monster['’]s name" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldFaceDownMonsters,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    BanishIfDestroyed = true,
                    PurgeDecksIfFlipMonster = true,
                    MakesChainLink = true
                }),

            // Deal of Phantom (c69122763): target 1 face-up monster on either field (EFFECT_FLAG_CARD_TARGET); at
            // resolution, if it is still face-up, it gains 100 × the monsters in your GY
            // (GetMatchingGroupCount(Card.IsMonster, tp, LOCATION_GRAVE, 0)) until the End Phase
            // (RESETS_STANDARD_PHASE_END). The number is fixed when the effect resolves ("applied when this card is
            // activated"); monsters sent to the GY later do not change it.
            new AtomRegistration.Atom(
                Start + @"Select 1 face-up monster on the field\.\s*Increase the selected monster['’]s ATK by (\d+) " +
                @"points for each monster in your Graveyard during the turn this card is activated\.\s*" +
                @"The number of monsters is applied when this card is activated" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ModifyTargetUntilEndOfTurn,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    // ATK per counted monster; TargetedClassicsRuntime.ApplyPerCountBoost multiplies it.
                    Amount = Parse(m, 1, 100),
                    CountSource = MonstersInYourGy,
                    MakesChainLink = true
                }),

            // Miracle Dig (c6343408): condition — 5 or more face-up monsters in your banished pile; target exactly 3 of
            // them (SelectTarget 3,3, LOCATION_REMOVED, IsFaceup and IsMonster) and send the ones still there to the
            // GY (REASON_RETURN). A face-down banished card never counts.
            new AtomRegistration.Atom(
                Start + @"If (\d+) or more of your monsters are currently removed from play, return (\d+) of them to " +
                @"the Graveyard" + End,
                m =>
                {
                    var need = Parse(m, 1, 5);
                    var take = Parse(m, 2, 3);
                    if (take < 1 || take > need) return null; // "return N of them" must be among the counted ones
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.ReturnBanishedTargetToGy,
                        Zone = EffectZoneFilter.ControllerBanishedFaceUpMonsters,
                        Side = EffectSide.Controller,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = take,
                        TargetUpTo = false,
                        ActivationCondition = TargetedClassicsRuntime.YourFaceUpBanishedMonstersAtLeast,
                        ConditionAmount = need,
                        MakesChainLink = true
                    };
                }),

            // Tornado Bird (c71283180): FLIP, EFFECT_FLAG_CARD_TARGET — exactly 2 Spell/Trap Cards on the field, Set or
            // face-up, either side. With fewer than 2 it activates with no targets and does nothing; at resolution
            // both return to their owners' hands only while both are still there (#dg==2).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Return (\d+) Spell or Trap Cards on the field to their owners['’] hands" + End,
                m =>
                {
                    var n = Parse(m, 1, 2);
                    if (n < 2) return null; // "Cards" is plural: a single card is another sentence
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.ReturnToHand,
                        Zone = EffectZoneFilter.FieldSpellTraps,
                        Side = EffectSide.Both,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = n,
                        TargetUpTo = false,
                        MakesChainLink = true
                    };
                }),

            // Nobleman-Eater Bug (c65878864): FLIP, EFFECT_FLAG_CARD_TARGET — "FLIP: Destroy 2 monsters on the
            // field." The old PSCT names no target, but the engine models it as an exact-count targeted destroy
            // (like Tornado Bird): pick exactly 2 monsters on either field, Set or face-up, and destroy each while
            // it is still there. With fewer than 2 legal targets the FLIP activates with none and does nothing.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Destroy (\d+) monsters? on the field" + End,
                m =>
                {
                    var n = Parse(m, 1, 2);
                    if (n < 2) return null; // "monsters" is plural: a single monster is another sentence
                    return new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        Side = EffectSide.Both,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        TargetCount = n,
                        TargetUpTo = false,
                        MakesChainLink = true
                    };
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
