using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// LvChain batch — the "LV" monster family of AST/SOD (Horus the Black Flame Dragon LV4/LV6/LV8,
    /// Ultimate Insect LV1/LV3/LV5, Dark Mimic LV1/LV3, Silent Swordsman LV3/LV5) plus the cards that
    /// move them around (Level Up!, The Graveyard in the Fourth Dimension). The families share three
    /// printed sentences:
    /// <list type="bullet">
    /// <item><b>"[While this card is on the field,] [this card is] unaffected by [the effects of] Spell
    /// [Cards][ effects]"</b> — the shared <see cref="EffectActionKind.UnaffectedByCardEffects"/> clause
    /// with a Spell filter and no named requirement (Horus LV6, Ultimate Insect LV1). A monster's own
    /// text, so it is a <see cref="EffectTiming.ContinuousWhileFaceUp"/> marker, never a Chain Link.</item>
    /// <item><b>An End Phase / Standby Phase "send this card to the GY, Special Summon the next LV"</b>
    /// clause — the generic PSCT path already compiles that sentence; the "(You cannot activate this
    /// effect the turn this card was Normal Summoned, Special Summoned, or flipped face-up.)" rider is a
    /// <see cref="LvChainRuntime.NotTurnSummoned"/> activation condition.</item>
    /// <item><b>A conditional draw</b> (Dark Mimic LV3): destroyed by battle and sent to the GY, draw 1,
    /// or 2 when this card was Special Summoned by the named LV1's effect.</item>
    /// </list>
    /// Runtime: <see cref="LvChainRuntime"/>.
    /// </summary>
    public static class LvChainAtoms
    {
        /// <summary>Sentence start: text start, or after a sentence end / closing parenthesis.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";

        /// <summary>Sentence end: the period, then whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Horus the Black Flame Dragon LV6 ("This card is unaffected by Spell effects.") and
            // Ultimate Insect LV1 ("While this card is on the field, it is unaffected by the effects of
            // Spell Cards."). ProtectionTemplates.UnaffectedClause always carries a named requirement
            // (The Agent of Force - Mars needs the Sanctuary), so a monster's own Spell immunity is here.
            new AtomRegistration.Atom(
                Start + @"(?:(?:While this card is on the field, )?this card is unaffected by Spell effects|" +
                @"While this card is on the field, it is unaffected by the effects of Spell Cards)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.UnaffectedByCardEffects,
                    UnaffectedByFilter = "Spell",
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Ultimate Insect LV1, the LV progression sentence with its "summoned this turn" rider.
            // "During your Standby Phase, by sending this face-up card to the GY, Special Summon 1 X from
            // your hand or Deck. (You cannot activate this effect the turn this card was Normal Summoned,
            // Special Summoned, or flipped face-up.)"
            new AtomRegistration.Atom(
                Start + @"During your Standby Phase, by sending this face-up card to the " +
                @"(?:GY|Graveyard), Special Summon 1 ""([^""]+)"" from your hand or Deck" + End +
                @"(?:\s*(\((?:You cannot activate this effect the turn this card (?:was|is) Normal Summoned, " +
                @"Special Summoned, or flipped face-up|You cannot activate this effect the turn this card " +
                @"(?:was|is) Normal Summoned or Special Summoned, or flipped face-up)[.)]?\)[.)]?))?",
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    RequiresSendThisToGy = true,
                    FromHand = true,
                    FromDeck = true,
                    IsOptional = true,
                    OnlyDuringOwnStandby = true,
                    // The rider is what carries the gate: "During your Standby Phase, by sending this
                    // face-up card to the GY, Special Summon 1 X from your hand or Deck." with no
                    // "(You cannot activate this effect the turn this card was Normal Summoned, Special
                    // Summoned, or flipped face-up.)" is an ordinary Standby effect (the near-miss case).
                    ActivationCondition = m.Groups[2].Success ? LvChainRuntime.NotTurnSummoned : null,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Dark Mimic LV3: destroyed by battle and sent to the GY → draw 1, but 2 when this card was
            // Special Summoned by the named card's effect (Lua RaiseSingleEvent, Duel.Draw(tp, 1 or 2)).
            new AtomRegistration.Atom(
                Start + @"If this card is destroyed by battle and sent to the (?:GY|Graveyard): Draw (\d+) card" +
                @"(?:s)?, but if this card was Special Summoned by the effect of ""([^""]+)"", draw (\d+) card" +
                @"(?:s)? instead" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.DrawAmountWhenSpecialSummonedByNamed,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    AmountIfSpecialSummonedByNamed = int.Parse(m.Groups[3].Value),
                    SpecialSummonedByNamedCard = m.Groups[2].Value,
                    // The same battle-GY gate the generic RxBattleGyDraw clause carries (TextEffectRuntime's
                    // SentFromFieldToGy loop skips a clause whose RequiresThisDestroyedByBattle is set and the
                    // monster did not die in battle).
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // The Graveyard in the Fourth Dimension: "Add 2 "LV" monsters from your Graveyard to
            // your Deck and shuffle it." Same SendtoDeck/SEQ_DECKSHUFFLE arm as Ray of Hope, filtered by
            // the printed "LV" series rather than an Attribute.
            new AtomRegistration.Atom(
                @"^\s*Add (\d+) ""([^""]+)"" monsters from your (?:Graveyard|GY) to your Deck and shuffle it" +
                @"\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnChosenToDeckShuffle,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyCards,
                    // The printed "LV" is a name-substring series, not a card name: TargetSeriesName drives
                    // CollectTargets' CardMatchesSeries filter. A bare NamedCard here would leave every GY card legal
                    // (ControllerGyCards never reads NamedCard).
                    TargetSeriesName = m.Groups[2].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = int.Parse(m.Groups[1].Value),
                    TargetUpTo = false,
                    MakesChainLink = true
                }),
        };
    }
}
