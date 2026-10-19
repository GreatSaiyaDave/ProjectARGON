namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmFlipsControlAndZoneEvents" (TLM): FLIP and ignition control effects, a Defense-target FLIP, and the
    /// bounce / banish events. Each atom consumes the card's whole text (Reshef: its second sentence; the Ritual-only
    /// sentence is absorbed by CardTextEffectCompiler.RxRitualLegacyOnlyWith). Runtime:
    /// <see cref="TlmFlipsControlAndZoneEventsRuntime"/>.
    /// <list type="bullet">
    /// <item>White Ninja (c1571945): FLIP, target 1 Defense Position monster on the field (either side, face-down
    /// included, itself included); destroy it if it is still in Defense Position.</item>
    /// <item>Dummy Golem (c13532663): FLIP, the opponent selects 1 monster they control; it and this card switch control.</item>
    /// <item>Reshef the Dark Being (c62420419): once per turn, discard 1 Spell (cost), target 1 monster the opponent
    /// controls; take control of it until the End Phase.</item>
    /// <item>Criosphinx (c18654201): when monster(s) return from the field to the hand, each owner sends 1 card from
    /// their hand to the GY.</item>
    /// <item>D.D. Survivor (c48092532): End Phase, banished face-up from your field this turn: Special Summon it.</item>
    /// </list>
    /// </summary>
    public static class TlmFlipsControlAndZoneEventsAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string SentenceStart = @"(?<=^|\.\s)";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // White Ninja (c1571945): Lua EFFECT_TYPE_FLIP + EFFECT_FLAG_CARD_TARGET; s.target chk is always true, then
            // SelectTarget(IsDefensePos, LOCATION_MZONE both sides, no exception); s.operation destroys it only if it is
            // still in Defense Position.
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Target 1 Defense Position monster on the field;\s*destroy that target" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldDefensePositionMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    TargetMayBeThisCard = true,
                    MakesChainLink = true
                }),

            // Dummy Golem (c13532663): Lua EFFECT_TYPE_FLIP, no target; s.operation — this card still related, not
            // battle-destroyed, both able to change control; the opponent SelectMatchingCard(1 of their monsters), then
            // Duel.SwapControl (permanent).
            new AtomRegistration.Atom(
                Start + @"FLIP:\s*Your opponent selects 1 monster they control\.\s*" +
                @"Switch control of the selected monster and this card" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.OpponentSelectsOwnMonsterSwitchControlWithThis,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),

            // Reshef the Dark Being (c62420419): Lua EFFECT_TYPE_IGNITION, SetCountLimit(1) (soft), cost DiscardHand 1 Spell
            // (REASON_COST|REASON_DISCARD), SelectTarget 1 monster the opponent controls (IsControlerCanBeChanged, face-down
            // included), GetControl until the End Phase.
            new AtomRegistration.Atom(
                SentenceStart + @"Once per turn, by discarding 1 Spell Card from your hand, take control of 1 monster on " +
                @"your opponent" + Apos + @"s side of the field until the end of this turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TakeControlUntilEndPhase,
                    Zone = EffectZoneFilter.OppMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    Amount = 1,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = FaceUpQuickEffectsRuntime.DiscardSpellCard,
                    DiscardCostCount = 1,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Criosphinx (c18654201): Lua e1 FIELD CONTINUOUS EVENT_TO_HAND (range MZONE) raises e2, a SINGLE TRIGGER_F, once
            // per event for the owners of the monsters that came back from a Monster Zone; s.hdop (still related and
            // face-up) DiscardHand(ep, 1, REASON_EFFECT) — a send from the hand to the GY, each such player choosing.
            new AtomRegistration.Atom(
                Start + @"When a monster is returned from the field to the owner" + Apos + @"s hand,\s*the owner of the " +
                @"returned monster selects and sends 1 card in their hand to the (?:GY|Graveyard)" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OwnersOfMonstersReturnedToHandSendOneHandCardToGy,
                    Side = EffectSide.Both
                }),

            // D.D. Survivor (c48092532): Lua e1 SINGLE CONTINUOUS EVENT_REMOVE (face-up, previously face-up on the field
            // under its owner's control) sets a flag until the End Phase; e2 FIELD TRIGGER_F EVENT_PHASE+PHASE_END from
            // LOCATION_REMOVED, once per turn per copy: Special Summon it face-up, or send it to the GY with no free zone.
            new AtomRegistration.Atom(
                Start + @"During the End Phase, if this card was banished while face-up, on your side of the field, this " +
                @"turn:\s*Special Summon this banished card\.\s*This effect can only activate once per turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.SpecialSummonThisFromBanished,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    RequiresBanishedThisTurn = true,
                    RequiresBanishedFaceUpFromOwnField = true,
                    MakesChainLink = true
                }),
        };
    }
}
