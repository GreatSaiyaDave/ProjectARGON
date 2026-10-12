namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// SanctuaryAndZera batch (Ancient Sanctuary): the cards keyed on a face-up "The Sanctuary in the Sky" or "Pandemonium".
    /// Whole-sentence atoms; the "this effect is not applied" re-check is <see cref="EffectClause.NotAppliedUnlessNamedFaceUp"/>.
    /// <list type="bullet">
    /// <item>Archlord Zerato / Mazera DeVille: the Nomi hand procedure "Tributing 1 face-up "X" on your side of the field while
    /// "Y" is on the field" (Lua EFFECT_SPSUMMON_PROC + EnableReviveLimit; Y on either field).</item>
    /// <item>Archlord Zerato's ignition (Y on your side; discard 1 LIGHT monster as the cost; destroy all monsters your
    /// opponent controls) and Mazera DeVille's mandatory Special Summon trigger (the opponent discards 3 random cards).</item>
    /// <item>The Agent of Force - Mars: unaffected by Spell effects; gains ATK/DEF equal to your LP lead while you control Y.</item>
    /// <item>The Agent of Judgment - Saturn: Tribute this card; damage equal to your LP lead (oath: no Battle Phase).</item>
    /// <item>Light of Judgment: discard 1 LIGHT monster; send 1 card the opponent controls to the GY, or look at their hand and
    /// discard 1 card from it (chosen at resolution).</item>
    /// </list>
    /// Runtime: <see cref="SanctuaryAndZeraRuntime"/>.
    /// </summary>
    public static class SanctuaryAndZeraAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.(?=\s|$)";
        const string Attributes = @"LIGHT|DARK|EARTH|WATER|FIRE|WIND|DIVINE";
        const string Q = @"[""“”]";
        const string Name = Q + @"([^""“”]+)" + Q;
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Archlord Zerato (c18378582 e2) / Mazera DeVille (c6133894 e2): EFFECT_SPSUMMON_PROC from LOCATION_HAND, the
            // condition IsExistingMatchingCard / IsEnvironment on either field, Duel.SelectReleaseGroup of 1 named monster
            // you control (face-up), Release as the cost; EnableReviveLimit + EFFECT_SPSUMMON_CONDITION. "This card cannot
            // be Normal Summoned or Set." stays with the structural summon lock (PsctGrammar.BlocksNormalSummonOrSet).
            new AtomRegistration.Atom(
                Start + @"This card cannot be Normal Summoned or Set\.\s*This card cannot be Special Summoned except by " +
                @"Tributing 1 face-up " + Name + @" on your side of the field while " + Name + @" is on the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonFromHandProcedure,
                    Amount = 0, // the engine picks the named Tribute (DuelUI reads HandProcedureOf(...).Amount)
                    TributeNamedCard = m.Groups[1].Value,
                    TributeFaceUpOnly = true,
                    RequiresFaceUpName = m.Groups[2].Value, // on either field (RequiresControllerNamedCard stays false)
                    FromHand = true,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),

            // Archlord Zerato (c18378582 e3): IGNITION, no count limit; descon: a face-up "Y" on your side; descost: discard
            // 1 LIGHT monster (REASON_COST|REASON_DISCARD); destg: your opponent controls a monster; desop: "Y" still on your
            // side → destroy every monster your opponent controls.
            new AtomRegistration.Atom(
                Start + @"If " + Name + @" is on your side of the field, by discarding 1 (" + Attributes +
                @") Monster Card from your hand to the Graveyard, destroy all monsters on your opponent" + Apos +
                @"s side of the field\.\s*If " + Q + @"\1" + Q + @" is not on your side of the field, this effect is not applied" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldMonsters,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = m.Groups[2].Value.ToUpperInvariant(),
                    DiscardCostCount = 1,
                    RequiresFaceUpName = m.Groups[1].Value,
                    RequiresControllerNamedCard = true,
                    NotAppliedUnlessNamedFaceUp = m.Groups[1].Value,
                    ActivationCondition = SanctuaryAndZeraRuntime.OppControlsMonster,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Mazera DeVille (c6133894 e3): SINGLE+TRIGGER_F on EVENT_SPSUMMON_SUCCESS; hdop: Duel.IsEnvironment(94585852)
            // (no player: "Pandemonium" on either field) → the opponent's hand RandomSelect(tp, 3) is discarded (all of it
            // when it holds fewer).
            new AtomRegistration.Atom(
                Start + @"If " + Name + @" is on your side of the field when you Special Summon this card, your opponent " +
                @"discards (\d+) random cards from their hand\.\s*If " + Q + @"\1" + Q + @" is not on your side of the field, this effect " +
                @"is not applied" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = int.TryParse(m.Groups[2].Value, out var n) && n > 0 ? n : 1,
                    RequiresThisSpecialSummoned = true,
                    NotAppliedUnlessNamedFaceUp = m.Groups[1].Value, // either field (Lua IsEnvironment, no player)
                    IsOptional = false,
                    MakesChainLink = true
                }),

            // The Agent of Force - Mars (c91123920): EFFECT_IMMUNE_EFFECT (te:IsSpellEffect(), its controller's too);
            // EFFECT_UPDATE_ATTACK / _DEFENSE s.val: a face-up "Y" on the handler's side → max(0, your LP − theirs).
            AtomRegistration.Atom.Sequence(
                Start + @"This card is unaffected by any Spell Cards\.\s*While you control a face-up " + Name +
                @" and your Life Points are higher than your opponent" + Apos + @"s, this card gains ATK and DEF by the " +
                @"difference between your and your opponent" + Apos + @"s Life Points" + End,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.UnaffectedByCardEffects,
                        UnaffectedByFilter = "Spell",
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.GainSelfAtkPerCount,
                        Side = EffectSide.Controller,
                        Amount = 1,
                        DefAmount = 1,
                        CountSource = SanctuaryAndZeraRuntime.LpLeadWhileNamedYouControl,
                        NamedCard = m.Groups[1].Value,
                        MakesChainLink = false
                    }
                }),

            // The Agent of Judgment - Saturn (c91345518): IGNITION, PLAYER_TARGET; damcost: not Main Phase 2, Release this
            // card, then EFFECT_CANNOT_BP (OATH) for you this turn; damtg: your LP > theirs and a face-up "Y" on your side;
            // damop: "Y" still on your side → damage = your LP − theirs, if positive.
            new AtomRegistration.Atom(
                Start + @"While your Life Points are higher than your opponent" + Apos + @"s, you can Tribute this card to " +
                @"inflict damage to your opponent equal to the difference\.\s*If you do not control a face-up " + Name +
                @", this effect is not applied\.\s*You cannot conduct your Battle Phase the turn you activate this effect" +
                End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictLpLeadAsDamageToOpponent,
                    Side = EffectSide.Opponent,
                    RequiresTributeThis = true,
                    RequiresFaceUpName = m.Groups[1].Value,
                    RequiresControllerNamedCard = true,
                    NotAppliedUnlessNamedFaceUp = m.Groups[1].Value,
                    ActivationCondition = SanctuaryAndZeraRuntime.YourLpHigher,
                    ForbidsBattlePhaseThisTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Light of Judgment (c44595286): Normal Trap, EVENT_FREE_CHAIN; condition: a face-up "Y" on either field (or
            // IsEnvironment); cost: discard 1 LIGHT monster; target chk: the opponent has a card on the field or in the hand;
            // s.activate: Duel.SelectOption between the options that still have cards (field: Select 1 → SendtoGrave; hand:
            // ConfirmCards, Select 1 → SendtoGrave, ShuffleHand).
            new AtomRegistration.Atom(
                Start + @"If " + Name + @" is on the field: Discard 1 (" + Attributes + @") monster to the (?:GY|Graveyard); " +
                @"apply 1 of the following effects\.\s*●\s*Look at your opponent" + Apos + @"s hand and discard 1 card " +
                @"from their hand\.\s*●\s*Send 1 card your opponent controls to the (?:GY|Graveyard)" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand,
                    Side = EffectSide.Opponent,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = m.Groups[2].Value.ToUpperInvariant(),
                    DiscardCostCount = 1,
                    RequiresFaceUpName = m.Groups[1].Value, // on either field
                    ActivationCondition = SanctuaryAndZeraRuntime.OppHandOrFieldCard,
                    MakesChainLink = true
                }),
        };
    }
}
