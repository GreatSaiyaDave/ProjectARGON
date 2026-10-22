namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellCounterSpenders": the Spell Counter kit beyond "this card" — counters removed from anywhere on
    /// your side as a cost, counters placed on "a card that you can place a Spell Counter on", a Standby upkeep paid in
    /// counters and a Continuous Trap that moves its counters. Each atom is one whole sentence (start of text or after a
    /// sentence end, through its period) or a whole card text, so a longer card that only contains part of it stays
    /// uncompiled. Runtime: <see cref="SpellCounterSpendersRuntime"/>.
    /// </summary>
    public static class SpellCounterSpendersAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Magical Plant Mandragola (c7802006): SINGLE+FLIP; every face-up card on either field with
            // IsCanAddCounter(COUNTER_SPELL,1) gets 1 (no target).
            new AtomRegistration.Atom(
                @"^FLIP: Place (\d+) Spell Counter on each face-up card on the field that you can place a Spell Counter on\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.PlaceSpellCounterOnEachFaceUpHolder,
                    Amount = int.Parse(m.Groups[1].Value),
                    Side = EffectSide.Both,
                    MakesChainLink = true
                }),
            // Mega Ton Magical Cannon (c32062913): ACTIVATE (free chain); cost Duel.RemoveCounter(tp,1,0,COUNTER_SPELL,10,
            // REASON_COST); target chk: the opponent controls a card; destroys every card in the opponent's LOCATION_ONFIELD.
            new AtomRegistration.Atom(
                @"^Remove (\d+) Spell Counters on your side of the field\. " +
                @"Destroy all cards on your opponent's side of the field\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAnyCardOnField,
                    RemoveSpellCountersOnYourSide = int.Parse(m.Groups[1].Value),
                    ActivationCondition = SpellCounterSpendersRuntime.OpponentControlsACard,
                    MakesChainLink = true
                }),
            // Miracle Restoring (c68334074): ACTIVATE (free chain), EFFECT_FLAG_CARD_TARGET; cost 2 Spell Counters from your
            // side; target 1 "Dark Magician" (46986414) or "Buster Blader" (78193831) in your GY that can be Special
            // Summoned (a free Monster Zone); Special Summon it face-up if it is still there.
            new AtomRegistration.Atom(
                @"^Remove (\d+) Spell Counters from your side of the field, then target 1 ""([^""]+)"" or ""([^""]+)"" " +
                @"in your (?:Graveyard|GY); Special Summon that target\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    NamedCard = m.Groups[2].Value,
                    AltNamedCard = m.Groups[3].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RemoveSpellCountersOnYourSide = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),
            // Anti-Spell (c53112492): Counter Trap on EVENT_CHAINING, re:IsSpellEffect() and re:IsHasType(EFFECT_TYPE_ACTIVATE)
            // (a Spell Card's activation, either player's) and IsChainNegatable; cost 2 Spell Counters from your side;
            // NegateActivation, then destroy it if it is still related (Magic Jammer's answer).
            new AtomRegistration.Atom(
                @"^Remove (\d+) Spell Counters on your side of the field to negate the activation of a Spell Card " +
                @"and destroy it\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    DestroyNegatedCard = true,
                    RemoveSpellCountersOnYourSide = int.Parse(m.Groups[1].Value),
                    MakesChainLink = true
                }),
            // Apprentice Magician (c9156135) e1-e3: SINGLE+TRIGGER_F on Normal / Flip / Special Summon success,
            // EFFECT_FLAG_CARD_TARGET: 1 face-up card on either field with IsCanAddCounter(COUNTER_SPELL,1) (chk==0 is
            // true: it activates with no target and does nothing); 1 Spell Counter if it is still face-up and related.
            new AtomRegistration.Atom(
                S + @"If this card is Summoned: Target 1 face-up card on the field that you can place a Spell Counter on; " +
                @"place (\d+) Spell Counter on that target" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ThisCardSummoned,
                    Action = EffectActionKind.PlaceSpellCounterOnTarget,
                    Zone = EffectZoneFilter.FieldFaceUpSpellCounterHolders,
                    Side = EffectSide.Both,
                    Amount = int.Parse(m.Groups[1].Value),
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),
            // Apprentice Magician (c9156135) e4: SINGLE+TRIGGER_O on EVENT_BATTLE_DESTROYED (not to the Deck); a free
            // Monster Zone and a Level 2 or lower Spellcaster in the Deck; Special Summon it in POS_FACEDOWN_DEFENSE and
            // show it to the opponent (Duel.ConfirmCards).
            new AtomRegistration.Atom(
                S + @"If this card is destroyed by battle: You can Special Summon 1 Level (\d+) or lower (\w+) monster " +
                @"from your Deck in face-down Defense Position" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonLevelRaceFromDeckFaceDownDefense,
                    Amount = int.Parse(m.Groups[1].Value),
                    AmountIsLevel = true,
                    RaceFilter = m.Groups[2].Value,
                    FromDeck = true,
                    SummonInDefense = true,
                    SummonFaceDown = true,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Armor Exe (c7180418) e1-e3: SINGLE+CONTINUOUS on Normal / Special / Flip Summon success registers
            // EFFECT_CANNOT_ATTACK until the End Phase.
            new AtomRegistration.Atom(
                S + @"This card cannot attack in the same turn it is Normal Summoned, Flip Summoned, or Special Summoned" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackTurnSpecialSummoned,
                    CannotAttackAlsoTurnNormalOrFlipSummoned = true,
                    MakesChainLink = false
                }),
            // — 64892035 Tardy Orc: This card cannot attack the turn it is Normal Summoned.
            new AtomRegistration.Atom(
                S + @"This card cannot attack (?:the turn|during the turn) it is Normal Summoned" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotAttackTurnSpecialSummoned,
                    CannotAttackAlsoTurnNormalOrFlipSummoned = true,
                    MakesChainLink = false
                }),
            // Armor Exe (c7180418) e4: FIELD+CONTINUOUS on EVENT_PHASE+PHASE_STANDBY (either player's), count limit 1,
            // EFFECT_FLAG_CANNOT_DISABLE: if 1 Spell Counter can be removed from your side and you choose to
            // (SelectYesNo), remove it (REASON_COST); else destroy this card.
            new AtomRegistration.Atom(
                S + @"During each of your and your opponent's Standby Phases, remove (\d+) Spell Counter on your side of " +
                @"the field\. If you do not, destroy this card" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis,
                    Amount = int.Parse(m.Groups[1].Value),
                    EachStandbyPhase = true,
                    UpkeepIsOptional = true,
                    MakesChainLink = false
                }),
            // Legendary Flame Lord (c60258960) e0/e1: aux.chainreg on EVENT_CHAINING + EVENT_CHAIN_SOLVED: a Spell Card's
            // activation (either player's) that resolves puts 1 Spell Counter on it (no max; Royal Magical Library's
            // runtime, TextEffectRuntime.NotifySpellResolved).
            new AtomRegistration.Atom(
                S + @"Each time you or your opponent activates 1 Spell Card, put (\d+) Spell Counter on this card" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PlaceSpellCounters,
                    Amount = int.Parse(m.Groups[1].Value),
                    PlaceCounterOnSpellActivate = true,
                    MakesChainLink = false
                }),
            // Legendary Flame Lord (c60258960) e2: IGNITION; cost RemoveCounter(tp,COUNTER_SPELL,3) from this card; chk: a
            // monster other than this card is on the field; destroy every monster on the field except this card.
            new AtomRegistration.Atom(
                S + @"Remove (\d+) Spell Counters from this card to destroy all monsters on the field except this card" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters,
                    ExceptThisCard = true,
                    RequiresRemoveSpellCounters = int.Parse(m.Groups[1].Value),
                    ActivationCondition = SpellCounterSpendersRuntime.AnotherMonsterOnField,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Pitch-Black Power Stone (c34029630), the whole text: EnableCounterPermit; e2 ACTIVATE places 3 Spell Counters
            // on it; e3 QUICK_O from the S/T Zone, count limit 1, condition IsTurnPlayer(tp), EFFECT_FLAG_CARD_TARGET (1
            // other face-up card with IsCanAddCounter): remove 1 from this card, place 1 on the target; e4
            // EFFECT_SELF_DESTROY while it holds no Spell Counter.
            AtomRegistration.Atom.Sequence(
                @"^Activate this card by placing (\d+) Spell Counters on it\. Once per turn, during your turn: You can " +
                @"target 1 other face-up card on the field that you can place a Spell Counter on; remove 1 Spell Counter " +
                @"from this card, and if you do, place 1 Spell Counter on that target\. When the last Spell Counter\(s\) " +
                @"is removed from this card, destroy it\.$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.PlaceSpellCounters,
                        Amount = int.Parse(m.Groups[1].Value),
                        PlaceSpellCountersAsActivated = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.FaceUpQuickEffect,
                        Action = EffectActionKind.MoveSpellCounterFromThisToTarget,
                        Zone = EffectZoneFilter.FieldFaceUpSpellCounterHolders,
                        Side = EffectSide.Both,
                        ExceptThisCard = true,
                        Amount = 1,
                        RequiresTargetChoice = true,
                        RequiresTargetFaceUp = true,
                        IsPsctTarget = true,
                        OncePerTurn = true,
                        ControllerTurnOnly = true,
                        IsOptional = true,
                        IsQuickEffect = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyThisWhenLastSpellCounterRemoved,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),
        };
    }
}
