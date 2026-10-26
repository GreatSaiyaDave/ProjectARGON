using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// CrvHandProceduresAndNamedSummons batch (CRV / EEN): inherent hand Special Summons, Summon triggers that Special
    /// Summon by name, and two Nomi monsters. Whole sentences only (each pattern runs from a sentence start to its period).
    /// The Nomi sentences ("Cannot be Normal Summoned/Set. Must be Special Summoned with "X".") stay structural
    /// (PsctGrammar summon restriction; TributeSummonsRuntime.ProcedureCardName / SemiNomiSummonAllowed).
    /// <list type="bullet">
    /// <item>Cyber Dragon (c70095154): EFFECT_SPSUMMON_PROC from the hand — you control no monster, the opponent controls 1+
    /// and a Monster Zone is free. A Summon, not an effect (no Chain Link; Solemn can negate it), in face-up Attack or Defense
    /// Position (POS_FACEUP, the player's choice): the card's own hand procedure, like Gilasaurus's / Guardian Grarl's
    /// (<see cref="EffectActionKind.SpecialSummonThisFromHandByOwnProcedure"/>, condition
    /// <see cref="CrvHandProceduresAndNamedSummonsRuntime.OnlyOpponentControlsMonsters"/>).</item>
    /// <item>Elemental HERO Bubbleman (c79979666): its procedure sentence is Guardian Grarl's (GuardiansAndEquipEventsAtoms);
    /// e2/e3/e4 TRIGGER_O on a Normal, Special or Flip Summon: draw 2, only while you control no other card and have no
    /// card in your hand (Lua s.condition at activation and at resolution; IsPlayerCanDraw(tp,2) —
    /// <see cref="CrvHandProceduresAndNamedSummonsRuntime.NoOtherCardsOnFieldOrInHand"/>).</item>
    /// <item>King's Knight (c64788463): TRIGGER_O on its Normal Summon while you control a face-up "Queen's Knight": Special
    /// Summon 1 "Jack's Knight" from the Deck (<see cref="EffectActionKind.SpecialSummonNamed"/>,
    /// <see cref="TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed"/>).</item>
    /// <item>Des Frog (c84451804): TRIGGER_O on its Tribute Summon: 1 to min(free zones, "T.A.D.P.O.L.E." in your GY)
    /// "Des Frog" from the hand and/or Deck, together
    /// (<see cref="EffectActionKind.SpecialSummonOwnNameFromHandOrDeckUpToNamedInGy"/>).</item>
    /// <item>Water Dragon (c85066822): e2 FIELD EFFECT_SET_ATTACK 0 on FIRE or Pyro monsters in both Monster Zones
    /// (<see cref="EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField"/>); e3 TRIGGER_O EVENT_TO_GRAVE destroyed: target 2
    /// "Hydrogeddon" and 1 "Oxygeddon" in your GY (3 free zones) and Special Summon them
    /// (<see cref="EffectActionKind.SpecialSummonTargetedNamedGroupsFromYourGy"/>).</item>
    /// <item>Winged Kuriboh LV10 (c98585345): QUICK_O during the opponent's Battle Phase, cost Tribute itself: destroy every
    /// Attack Position monster the opponent controls, then burn the destroyed monsters' printed ATK
    /// (<see cref="EffectActionKind.DestroyOppAttackPositionThenBurnPrintedAtk"/>).</item>
    /// </list>
    /// </summary>
    public static class CrvHandProceduresAndNamedSummonsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.?(?=\s|$)";
        const string Gy = @"(?:GY|Graveyard)";
        const string Apos = @"['’]";
        const string Attributes = @"DARK|LIGHT|EARTH|WATER|FIRE|WIND|DIVINE";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Ancient Gear (c31557782): own hand procedure if you control a face-up "Ancient Gear".
            new AtomRegistration.Atom(
                Start + @"If you control an ""([^""]+)"", you can Special Summon this card \(from your hand\) in face-up Attack Position" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonThisFromHandByOwnProcedure,
                    Side = EffectSide.Controller,
                    FromHand = true,
                    ActivationCondition = TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed,
                    ConditionName = m.Groups[1].Value,
                    MakesChainLink = false
                }),

            // Cyber Dragon.
            new AtomRegistration.Atom(
                Start + @"If only your opponent controls a monster, you can Special Summon this card \(from your hand\)" + End,
                // A Summon procedure (Lua EFFECT_SPSUMMON_PROC, POS_FACEUP), not an activated effect: the card's own hand
                // procedure (Gilasaurus / Guardian Grarl path), so the player chooses face-up Attack or Defense Position and
                // the zone; the condition is checked by NegationsAndGySummonsRuntime.CanSummonByOwnHandProcedure.
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonThisFromHandByOwnProcedure,
                    Side = EffectSide.Controller,
                    FromHand = true,
                    ActivationCondition = CrvHandProceduresAndNamedSummonsRuntime.OnlyOpponentControlsMonsters,
                    MakesChainLink = false
                }),

            // Elemental HERO Bubbleman, sentences 2 and 3 (sentence 1 is Guardian Grarl's procedure atom).
            new AtomRegistration.Atom(
                Start + @"When this card is Summoned: You can draw (\d+) cards\.\s*You must control no other cards and " +
                @"have no cards in your hand to activate and to resolve this effect" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    ActivationCondition = CrvHandProceduresAndNamedSummonsRuntime.NoOtherCardsOnFieldOrInHand,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // King's Knight.
            new AtomRegistration.Atom(
                Start + @"When this card is Normal Summoned while you control ""([^""]+)"": You can Special Summon 1 " +
                @"""([^""]+)"" from your Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    FromDeck = true,
                    RequiresThisNormalSummoned = true,
                    ActivationCondition = TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed,
                    ConditionName = m.Groups[1].Value,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Des Frog.
            new AtomRegistration.Atom(
                Start + @"When this card is Tribute Summoned, you can Special Summon ""([^""]+)""\(s\) from your hand or " +
                @"Deck up to the number of ""([^""]+)""\(s\) in your " + Gy + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.SpecialSummonOwnNameFromHandOrDeckUpToNamedInGy,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    ConditionName = m.Groups[2].Value,
                    FromHand = true,
                    FromDeck = true,
                    RequiresThisNormalSummoned = true,
                    RequiresThisTributeSummoned = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Water Dragon, sentence 3: a field-wide "ATK becomes 0" for an Attribute or a Type.
            new AtomRegistration.Atom(
                Start + @"The ATK of (" + Attributes + @") monsters and ([A-Za-z-]+(?: [A-Za-z-]+)?) monsters on the " +
                @"field becomes 0" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SetAtkZeroOfAttributeOrRaceOnField,
                    Side = EffectSide.Both,
                    AttributeFilter = m.Groups[1].Value.ToUpperInvariant(),
                    RaceFilter = m.Groups[2].Value,
                    MakesChainLink = false
                }),

            // Water Dragon, sentence 4.
            new AtomRegistration.Atom(
                Start + @"When this card is destroyed and sent to the " + Gy + @": You can target (\d+) ""([^""]+)"" and " +
                @"(\d+) ""([^""]+)"" in your " + Gy + @"; Special Summon them" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonTargetedNamedGroupsFromYourGy,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1),
                    NamedCard = m.Groups[2].Value,
                    ConditionAmount = Parse(m, 3, 1),
                    AltNamedCard = m.Groups[4].Value,
                    RequiresDestroyed = true,
                    IsPsctTarget = true,
                    IsOptional = true,
                    // "When … : You can" — it misses the timing like every optional When trigger.
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Winged Kuriboh LV10.
            new AtomRegistration.Atom(
                Start + @"During your opponent" + Apos + @"s Battle Phase \(Quick Effect\): You can Tribute this face-up " +
                @"card; destroy as many Attack Position monsters your opponent controls as possible, and if you do, " +
                @"inflict damage to your opponent equal to the combined original ATK of those destroyed monster\(s\)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.MonsterQuickEffect,
                    Action = EffectActionKind.DestroyOppAttackPositionThenBurnPrintedAtk,
                    Side = EffectSide.Opponent,
                    RequiresTributeThis = true,
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : fallback;
    }
}
