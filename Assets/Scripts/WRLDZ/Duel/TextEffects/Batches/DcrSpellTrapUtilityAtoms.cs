using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DcrSpellTrapUtility": Dark Crisis Normal / Continuous Spell and Trap utilities. Every atom owns the
    /// card's whole text (anchored ^…$), so a longer card never turns into a Stub. Runtime:
    /// <see cref="DcrSpellTrapUtilityRuntime"/>.
    /// <list type="bullet">
    /// <item>Ray of Hope (c82529174): target exactly 2 LIGHT monsters in your GY; shuffle them into the Deck.</item>
    /// <item>Spell Reproduction (c29228529): cost — send 2 Spells from your hand to the GY; target 1 Spell in your GY;
    /// add it to your hand.</item>
    /// <item>Frozen Soul (c57069605): only while your LP is at least 2000 lower than your opponent's; the opponent skips
    /// their next Battle Phase.</item>
    /// <item>Arsenal Robber (c55348096): the opponent selects 1 Equip Spell in their Deck and sends it to the GY.</item>
    /// <item>Interdimensional Matter Transporter (c36261276): banish 1 face-up monster you control until the End Phase.</item>
    /// <item>Dragged Down into the Grave (c16435215): each player picks 1 card of the other's revealed hand, both are
    /// discarded, then both players draw 1.</item>
    /// <item>Final Countdown (c95308449): pay 2000 LP; you win at the end of the 20th turn (activation turn = 1st).</item>
    /// <item>Different Dimension Gate (c56460688): banish 1 monster on each side; they return when this card is destroyed
    /// and sent to the GY.</item>
    /// </list>
    /// </summary>
    public static class DcrSpellTrapUtilityAtoms
    {
        const string Gy = @"(?:Graveyard|GY)";
        const string Apos = @"['’]";

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Ray of Hope (c82529174): EFFECT_FLAG_CARD_TARGET, SelectTarget 2..2 LIGHT monsters in your GY; SendtoDeck
            // SEQ_DECKSHUFFLE of the targets still related. A LIGHT Fusion target returns to the Extra Deck.
            new AtomRegistration.Atom(
                @"^\s*Add (\d+) (LIGHT|DARK|EARTH|WATER|FIRE|WIND) monsters from your " + Gy +
                @" to your Deck and shuffle it\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnChosenToDeckShuffle,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyCards,
                    AttributeFilter = m.Groups[2].Value.ToUpperInvariant(),
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = Parse(m, 1, 2),
                    TargetUpTo = false,
                    MakesChainLink = true
                }),

            // Spell Reproduction (c29228529): cost Duel.DiscardHand(Spell filter, 2, REASON_COST) — a send (no discard
            // event); then SelectTarget 1 Spell in your GY; SendtoHand + ConfirmCards.
            new AtomRegistration.Atom(
                @"^\s*Send (\d+) Spells from your hand to the " + Gy + @", then target 1 Spell in your " + Gy +
                @";\s*add it to your hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddFromGyToHand,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGySpells,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    DiscardCostCount = Parse(m, 1, 2),
                    CostSendSpellsFromHandCount = Parse(m, 1, 2),
                    MakesChainLink = true
                }),

            // Frozen Soul (c57069605): condition Duel.GetLP(tp)+2000 <= Duel.GetLP(1-tp); EFFECT_SKIP_BP on the opponent
            // (their next Battle Phase: this turn's when it has not begun yet, otherwise their next turn's).
            new AtomRegistration.Atom(
                @"^\s*You can only activate this card when your Life Points are at least (\d+) lower than your " +
                @"opponent" + Apos + @"s\.\s*Skip your opponent" + Apos + @"s next Battle Phase\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentSkipsNextBattlePhase,
                    Side = EffectSide.Opponent,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.YourLpAtLeastLowerThanOpponent,
                    ConditionAmount = Parse(m, 1, 2000),
                    MakesChainLink = true
                }),

            // Arsenal Robber (c55348096): chk the opponent's Deck is not empty; the opponent selects 1 Equip Spell
            // (TYPE_EQUIP) in their Deck and sends it to the GY. Dark Scorpion Burglars' send, with the Equip filter
            // (Zone DeckEquipSpells: a filter, not a target).
            new AtomRegistration.Atom(
                @"^\s*Your opponent selects 1 Equip Spell Card from (?:his/her|their) Deck and sends it to the " + Gy +
                @"\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentSendsSpellFromDeckToGy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.DeckEquipSpells,
                    Amount = 1,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.OpponentDeckNotEmpty,
                    MakesChainLink = true
                }),

            // Interdimensional Matter Transporter (c36261276): target 1 face-up monster you control; Remove with
            // REASON_TEMPORARY (still face-up and yours), ReturnToField at this turn's End Phase.
            new AtomRegistration.Atom(
                @"^\s*Target 1 face-up monster you control;\s*banish that target until the End Phase\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishTargetUntilEndPhase,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
                    MakesChainLink = true
                }),

            // Dragged Down into the Grave (c16435215): condition — the opponent has a card in hand and you have one
            // besides this card; resolution does nothing if either hand is empty.
            new AtomRegistration.Atom(
                @"^\s*Both players reveal their hands, each chooses 1 card from their opponent" + Apos +
                @"s hand, then you discard the chosen cards from both players" + Apos + @" hands, then both players " +
                @"draw 1 card\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw,
                    Side = EffectSide.Both,
                    Amount = 1,
                    ActivationCondition = DcrSpellTrapUtilityRuntime.BothHandsHaveACard,
                    MakesChainLink = true
                }),

            // Final Countdown (c95308449): Cost.PayLP(2000); a player effect counted at each EVENT_TURN_END.
            new AtomRegistration.Atom(
                @"^\s*Pay (\d+) Life Points\.\s*After (\d+) turns have passed \(counting the turn you activate this " +
                @"card as the 1st turn\), you win the Duel\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.WinAfterTurnCountdown,
                    Side = EffectSide.Controller,
                    PayLpAmount = Parse(m, 1, 2000),
                    Amount = Parse(m, 2, 20),
                    MakesChainLink = true
                }),

            // Different Dimension Gate (c56460688): SelectTarget 1 own + 1 opponent's monster (face-up or face-down);
            // Remove REASON_TEMPORARY while this card is still on the field; SINGLE+CONTINUOUS EVENT_TO_GRAVE with
            // REASON_DESTROY returns them (Duel.ReturnToField, previous position).
            new AtomRegistration.Atom(
                @"^\s*Activate this card by targeting 1 monster on each side of the field;\s*banish those targets\.\s*" +
                @"When this card is destroyed and sent to the " + Gy + @":\s*Return those banished monsters to the " +
                @"field in the same battle positions they were in\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    RequiresSecondTarget = true,
                    SecondZone = EffectZoneFilter.OppMonsters,
                    SecondAction = EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy,
                    MakesChainLink = true
                }),
        };
    }
}
