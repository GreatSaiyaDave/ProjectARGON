using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// GyHandAndSummonEventTriggers batch: triggers on sent-to-GY / sent-from-hand / discard / Special Summon events, Flip
    /// monster targets in the GY, and a discard-the-whole-hand recovery (<see cref="GyHandAndSummonEventTriggersRuntime"/>).
    /// Whole sentences only (each pattern runs from a sentence start to its period).
    /// <list type="bullet">
    /// <item>Night Assailant (c16226786) e2: SINGLE TRIGGER_F EVENT_TO_GRAVE, IsPreviousLocation(LOCATION_HAND) (any reason),
    /// EFFECT_FLAG_CARD_TARGET: 1 Flip monster in your GY, not IsCode(id) → SendtoHand.</item>
    /// <item>Soul Reversal (c78864369): Quick-Play, EFFECT_FLAG_CARD_TARGET: 1 Flip monster in your GY → top of the Deck
    /// (SEQ_DECKTOP).</item>
    /// <item>Emissary of the Afterlife (c75043725): SINGLE TRIGGER_F EVENT_TO_GRAVE, IsPreviousLocation(LOCATION_ONFIELD): each
    /// player selects 1 Level 3 or lower Normal Monster from their own Deck (not a target).</item>
    /// <item>Backfire (c82705573): FIELD TRIGGER_F EVENT_TO_GRAVE (LOCATION_SZONE): a face-up FIRE monster you controlled in
    /// the Monster Zone, destroyed and now in your GY → 500 to the opponent.</item>
    /// <item>Blessings of the Nile (c30653113): FIELD TRIGGER_F EVENT_TO_GRAVE: a card from your hand, REASON_EFFECT +
    /// REASON_DISCARD, rp~=tp → gain 1000 LP.</item>
    /// <item>"7" (c67048711): the activation raises EVENT_CUSTOM when 3 face-up copies are in your S/T Zones (TRIGGER_F: draw 3
    /// if still 3, then destroy every face-up copy on the field); SINGLE TRIGGER_F EVENT_TO_GRAVE from the field → gain 700.</item>
    /// <item>The Kick Man (c90407382): SINGLE TRIGGER_O EVENT_SPSUMMON_SUCCESS, EFFECT_FLAG_CARD_TARGET: 1 Equip Spell in your
    /// GY that CheckEquipTarget(this card) (Guardian Elma's clause with the Special Summon timing).</item>
    /// <item>Beckoning Light (c16255442): Normal Trap; chk: hand (not this card) &gt; 0 and that many LIGHT monsters in the GY;
    /// discard the hand, then add exactly that many LIGHT monsters (a selection) if the GY holds enough.</item>
    /// </list>
    /// </summary>
    public static class GyHandAndSummonEventTriggersAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.?(?=\s|$)";
        const string Gy = @"(?:GY|Graveyard)";
        const string Attr = @"(DARK|LIGHT|EARTH|WATER|FIRE|WIND|DIVINE)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Night Assailant, sentence 2 (its FLIP sentence compiles through the generic PSCT path).
            new AtomRegistration.Atom(
                Start + @"When this card is sent from the hand to the " + Gy + @": Target 1 Flip Effect Monster in your " +
                Gy + @", except this card; return that target to the hand" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromHandToGy,
                    Action = EffectActionKind.AddFromGyToHand,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    TargetCardKind = GyHandAndSummonEventTriggersRuntime.FlipKind,
                    ExceptThisCard = true,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),

            // Soul Reversal.
            new AtomRegistration.Atom(
                @"^\s*Return 1 Flip Effect Monster from your " + Gy + @" to the top of your Deck\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.PlaceTargetOnTopOfDeck,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    TargetCardKind = GyHandAndSummonEventTriggersRuntime.FlipKind,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),

            // Emissary of the Afterlife.
            new AtomRegistration.Atom(
                Start + @"When this card is sent from the field to the " + Gy + @": Each player adds 1 Level (\d+) or lower " +
                @"Normal Monster from their Deck to their hand" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck,
                    Side = EffectSide.Both,
                    Amount = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),

            // Backfire.
            new AtomRegistration.Atom(
                @"^\s*If a face-up " + Attr + @" monster\(s\) you control is destroyed and sent to the " + Gy +
                @": Inflict (\d+) damage to your opponent\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy,
                    Side = EffectSide.Opponent,
                    AttributeFilter = m.Groups[1].Value,
                    Amount = int.Parse(m.Groups[2].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Blessings of the Nile.
            new AtomRegistration.Atom(
                @"^\s*Each time card\(s\) are discarded from your hand to the " + Gy + @" by the effect of a card controlled " +
                @"by your opponent, increase your Life Points by (\d+) points\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainLpEachTimeDiscardedByOpponentEffect,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // "7", sentences 1–2: the activation's count check, draw, then destroy every copy.
            new AtomRegistration.Atom(
                Start + @"When there are (\d+) face-up ""([^""]+)"" cards on your side of the field, draw (\d+) cards from " +
                @"your Deck\. Then destroy all ""\2"" cards" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    ConditionAmount = int.Parse(m.Groups[1].Value),
                    Amount = int.Parse(m.Groups[3].Value),
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // "7", sentence 3: sent from the field to the GY (any reason) → gain LP (mandatory).
            new AtomRegistration.Atom(
                Start + @"When this card is sent directly from the field to your " + Gy + @", increase your Life Points by " +
                @"(\d+) points" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.GainLifePoints,
                    Side = EffectSide.Controller,
                    Amount = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),

            // The Kick Man.
            new AtomRegistration.Atom(
                Start + @"When this card is Special Summoned, you can equip 1 appropriate Equip Spell Card from your " + Gy +
                @" to this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.EquipTargetedGyEquipSpellToThis,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyEquipSpells,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    RequiresThisSpecialSummoned = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Beckoning Light.
            new AtomRegistration.Atom(
                @"^\s*Discard your entire hand, then for each card you discarded to the " + Gy + @" by this effect, add 1 " +
                Attr + @" monster from your " + Gy + @" to your hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard,
                    Side = EffectSide.Controller,
                    AttributeFilter = m.Groups[1].Value,
                    ActivationCondition = GyHandAndSummonEventTriggersRuntime.HandAndAttributeMonstersInGy,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };
    }
}
