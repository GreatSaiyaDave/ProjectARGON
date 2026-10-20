using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ContinuousTrapsAndSpiritSupport" (v161): LOD Continuous Spells/Traps with Standby / End Phase effects,
    /// field-to-GY burns and Spirit support — Fatal Abacus, Life Absorbing Machine, Ominous Fortunetelling, Fengsheng
    /// Mirror, Spirit's Invitation (its trigger sentence; the 500 LP upkeep is the shared ContinuousRestrictionTemplates
    /// pay-or-destroy clause), Spiritual Energy Settle Machine and Mind on Air (the hand-reveal rule). Every atom is a
    /// whole sentence (or the whole text where
    /// the sentences refer to each other), anchored at a sentence start and a sentence end, so a longer or reworded card
    /// stays uncompiled. Runtime: <see cref="ContinuousTrapsAndSpiritSupportRuntime"/>.
    /// </summary>
    public static class ContinuousTrapsAndSpiritSupportAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the final period followed by whitespace or the end of the text.</summary>
        const string E = @"\.(?=\s|$)";

        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Greed (c89405199), its whole text — one sentence carrying both of the Trap's rules.
            // Lua: e2 a FIELD+CONTINUOUS effect on EVENT_DRAW with (r&REASON_EFFECT)~=0 that adds the drawn count to
            // a per-player flag (id for its controller, id+1 for the opponent, RESETS_STANDARD_PHASE_END); e3 a
            // FIELD+TRIGGER_F on EVENT_PHASE+PHASE_END (count 1) that inflicts 500 x each player's flag.
            AtomRegistration.Atom.Sequence(
                S + @"Each time a player draws cards because of a card effect, they take (\d+)(?: points of)? damage " +
                @"during the End Phase for each card drawn by the card effect" + E,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.TrackEffectDrawForEndPhaseDamage,
                        Side = EffectSide.Both,
                        Amount = Parse(m, 1, 500),
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.EndPhase,
                        Action = EffectActionKind.InflictDamagePerEffectDrawThisTurn,
                        Side = EffectSide.Both,
                        Amount = Parse(m, 1, 500),
                        StaysOnField = true,
                        // The Lua's e3 is EVENT_PHASE+PHASE_END with no player condition: the Trap pays out at either
                        // player's End Phase, so it fires on each player's phase like Two-Man Cell Battle.
                        FiresAtEachPlayersPhase = true,
                        MakesChainLink = true
                    }
                }),
            // Fatal Abacus (c77910045): continuous EVENT_TO_GRAVE (range SZONE, no Chain Link); every monster that left a
            // Monster Zone for a GY in that event costs its owner (the GY's controller) 500, both players together.
            new AtomRegistration.Atom(
                S + @"Each time 1 monster is sent from the field to the Graveyard, inflict (\d+) points of damage per " +
                @"card to the respective card owner" + Apos + @"s Life Points" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy,
                    Amount = Parse(m, 1, 500),
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Life Absorbing Machine (c14318794): TRIGGER_F in the controller's Standby Phase (reccon: tp is the turn
            // player); gains half (rounded up) of each LP cost the controller paid during their previous turn.
            new AtomRegistration.Atom(
                S + @"During your Standby Phase, gain Life Points equal to half of the total Life Points you paid " +
                @"during your last turn" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainHalfLpCostsPaidLastTurn,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Ominous Fortunetelling (c56995655): the whole text. TRIGGER_F in the controller's Standby Phase, count limit
            // 1: a random card of the opponent's hand, a Monster / Spell / Trap call, the card is confirmed, 700 damage to
            // the opponent on a right call.
            new AtomRegistration.Atom(
                S + @"During your Standby Phase, select 1 random card from your opponent" + Apos + @"s hand\.\s*" +
                @"Call the type of card \(Monster, Spell, or Trap\)\.\s*" +
                @"If you call it right, inflict (\d+) points of damage to your opponent" + Apos + @"s Life Points\.\s*" +
                @"You can use this effect only once per turn" + E,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.CallTypeOfRandomOpponentHandCardBurn,
                    Amount = Parse(m, 1, 700),
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Fengsheng Mirror (c37406863): the whole text. The activator looks at the opponent's hand and, if it holds
            // Spirit monsters, selects 1 to discard (not a target: the pick is made at resolution).
            new AtomRegistration.Atom(
                S + @"Look at your opponent" + Apos + @"s hand\.\s*" +
                @"Select and discard 1 Spirit monster to the Graveyard if Spirit monsters exist in his/her hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LookAtOpponentHandDiscardChosenSpirit,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),
            // Spirit's Invitation (c92394653), first sentence: TRIGGER_F on EVENT_TO_HAND for a face-up Spirit that left
            // its controller's Monster Zone for this card's controller's hand; the opponent selects 1 monster they control
            // and returns it (SelectMatchingCard(1-tp, IsAbleToHand)).
            new AtomRegistration.Atom(
                S + @"Each time your Spirit monster returns to your hand, select 1 monster on your opponent" + Apos +
                @"s side of the field and return it to its owner" + Apos + @"s hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Spiritual Energy Settle Machine (c99173029), first sentence: EFFECT_SPIRIT_DONOT_RETURN on both Monster Zones.
            new AtomRegistration.Atom(
                S + @"As long as this card remains face-up on the field, all Spirit monsters remain face-up on the field " +
                @"and do not return to their owners" + Apos + @" hands during the End Phase even if they are Normal " +
                @"Summoned or flipped face-up" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpiritsDoNotReturnWhileThisFaceUp,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Spiritual Energy Settle Machine: its maintenance (continuous EVENT_PHASE+PHASE_END in the controller's turn,
            // EFFECT_FLAG_CANNOT_DISABLE): discard 1 card (hand not empty, the controller's choice) or destroy this card.
            new AtomRegistration.Atom(
                S + @"Discard 1 card from your hand during each of your End Phases\.\s*" +
                @"If you do not, destroy this card" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.EndPhaseDiscardOrDestroyThis,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Spiritual Energy Settle Machine: SINGLE+CONTINUOUS EVENT_LEAVE_FIELD — every face-up Spirit monster on the
            // field returns to its owner's hand.
            new AtomRegistration.Atom(
                S + @"When this card is removed from the field, all face-up Spirit monsters return to the owners" + Apos +
                @" hands" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ReturnFaceUpSpiritsWhenThisLeavesField,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Mind on Air (c66690411): SOD Continuous Trap whose whole text is one sentence. ygopro registers a
            // player-target field effect (EFFECT_FLAG_PLAYER_TARGET with player 1-tp) — "Your opponent must play
            // with their hand revealed." No cost, no target, no chain bullet: a continuous rule only.
            new AtomRegistration.Atom(
                S + @"Your opponent must play with their hand revealed" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentMustPlayWithHandRevealed,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Ceremonial Bell (c20228463): SRL Continuous Trap whose whole text is one sentence — "Both players must
            // keep their hands revealed." Continuous field rule like Mind on Air but reveals both players' hands.
            new AtomRegistration.Atom(
                S + @"Both players must keep their hands revealed" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothPlayersMustPlayWithHandRevealed,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // Respect Play (c8951260): PSV Normal Trap — "During their respective turns, each player must show
            // their opponent their hand." Reuses the BothPlayersMustPlayWithHandRevealed rule (both hands public).
            new AtomRegistration.Atom(
                S + @"During their respective turns, each player must show their opponent their hand" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothPlayersMustPlayWithHandRevealed,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // The Eye of Truth (c34694160): PSV Trap — sentence 1: "Your opponent must keep their hand revealed."
            // Reuses the OpponentMustPlayWithHandRevealed rule.
            new AtomRegistration.Atom(
                S + @"Your opponent must keep their hand revealed" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentMustPlayWithHandRevealed,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            // The Eye of Truth (c34694160): PSV Trap — sentence 2: "Once per turn, during your opponent's Standby Phase,
            // if they have a Spell(s) in their hand: They gain 1000 LP if they have a Spell(s) in their hand at resolution."
            new AtomRegistration.Atom(
                S + @"Once per turn, during your opponent's Standby Phase, if they have a Spell\(s\) in their hand: They " +
                @"gain 1000 LP if they have a Spell\(s\) in their hand at resolution" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.StandbyPhaseOpponentHasSpellInHandGainLp,
                    Side = EffectSide.Controller,
                    EachStandbyPhase = true,
                    OpponentTurnOnly = true,
                    MakesChainLink = false
                }),
            // The Eye of Truth atoms end here. Narrow Pass (c40172183): PGD Continuous Trap.
            // Sentence 1: "This card can only be activated when both players have 2 monsters or less on
            // their respective sides of the field." ActivationCondition gates activation.
            new AtomRegistration.Atom(
                S + @"This card can only be activated when both players have 2 monsters or less on" +
                @" their respective sides of the field" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ContinuousLimitNormalSummonsPerTurn,
                    Side = EffectSide.Controller,
                    ActivationCondition = SimpleSpellTrapsRuntime.BothPlayersMonstersLeqN,
                    ConditionAmount = 2,
                    MakesChainLink = true
                }),
            // Sentence 2: "Both players can only Normal Summon up to 2 additional monsters on their
            // sides of the field." ContinuousWhileFaceUp marker; enforced in DuelEngine.
            new AtomRegistration.Atom(
                S + @"Both players can only Normal Summon up to 2 additional monsters on their" +
                @" sides of the field" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousLimitNormalSummonsPerTurn,
                    Side = EffectSide.Controller,
                    Amount = 2,
                    MakesChainLink = false
                }),
            // PGD — Question (c38723936): PGD Normal Trap.
            // Sentence 1: "When activating this card, your opponent cannot check cards in the Graveyard."
            new AtomRegistration.Atom(
                S + @"When activating this card, your opponent cannot check cards in the Graveyard" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LockOpponentGraveyardInspection,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Sentences 2-4: "Your opponent calls the name of the first monster found at the bottom of
            // your Graveyard. If he/she calls it right, the monster is removed from play. If he/she calls it
            // wrong, the monster is Special Summoned to your side of the field."
            new AtomRegistration.Atom(
                S + @"Your opponent calls the name of the first monster found at the bottom of your Graveyard\. " +
                @"If he/she calls it right, the monster is removed from play\. " +
                @"If he/she calls it wrong, the monster is Special Summoned to your side of the field" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DeclareCardNameThenCheckGyBottomMatchBanishOrSpsummon,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) ? n : fallback;
        }
    }
}
