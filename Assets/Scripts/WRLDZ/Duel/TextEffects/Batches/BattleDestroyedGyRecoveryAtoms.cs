using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "BattleDestroyedGyRecovery" (v162, LON / PGD): recoveries of monsters destroyed by battle, read from
    /// the GY-entry stamp (<see cref="GyEntryStamps"/>: the turn, the reason and the reason player of a card's move into
    /// the GY). Each pattern is anchored to the start and the end of the whole printed text (Helpoemer: to the end, after
    /// its "Cannot be Special Summoned from the GY." summon condition), so a longer or reworded text stays uncompiled.
    /// Runtime: <see cref="BattleDestroyedGyRecoveryRuntime"/>.
    /// </summary>
    public static class BattleDestroyedGyRecoveryAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.\s*$";

        /// <summary>"to the bottom of your Deck" / "to the top of your Deck" → the action for that destination.</summary>
        static EffectActionKind DeckDestination(Match m, int group) =>
            m.Groups[group].Value.Equals("top", System.StringComparison.OrdinalIgnoreCase)
                ? EffectActionKind.PlaceTargetOnTopOfDeck
                : EffectActionKind.PlaceGyTargetOnBottomOfDeck;

        /// <summary>
        /// Crimson Sentry / Lady Panther / The Forgiving Maiden / Fairy Guardian (c28358902, c38480590, c84080938,
        /// c22419772): IGNITION in the Monster Zone, Cost.SelfTribute, EFFECT_FLAG_CARD_TARGET on 1 card in your GY
        /// whose GetTurnID() is this turn and that is a monster sent there by REASON_BATTLE (or, Fairy Guardian, a
        /// Spell whose GetReasonPlayer() is the opponent); on resolution, if it is still there, SendtoDeck
        /// (SEQ_DECKBOTTOM / SEQ_DECKTOP) or SendtoHand.
        /// </summary>
        static EffectClause SelfTributeGyRecovery(EffectActionKind action, EffectZoneFilter zone, bool byBattle) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = action,
                Side = EffectSide.Controller,
                Zone = zone,
                RequiresTributeThis = true,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                TargetCount = 1,
                RequiresTargetSentToGyByBattleThisTurn = byBattle,
                RequiresTargetSentToGyByOpponentThisTurn = !byBattle,
                MakesChainLink = true
            };

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Crimson Sentry (bottom) / Lady Panther (top).
            new AtomRegistration.Atom(
                Begin + @"Tribute this face-up card to return 1 of your monsters destroyed as a result of battle " +
                @"during this turn to the (bottom|top) of your Deck" + Finish,
                m => SelfTributeGyRecovery(DeckDestination(m, 1), EffectZoneFilter.ControllerGyMonsters, byBattle: true)),

            // The Forgiving Maiden: to the hand.
            new AtomRegistration.Atom(
                Begin + @"Tribute this face-up card to return to your hand 1 of your monsters destroyed as a result of " +
                @"battle during this turn" + Finish,
                m => SelfTributeGyRecovery(EffectActionKind.AddFromGyToHand, EffectZoneFilter.ControllerGyMonsters,
                    byBattle: true)),

            // Fairy Guardian: a Spell the opponent's card effect sent to your GY this turn, to the bottom of the Deck.
            new AtomRegistration.Atom(
                Begin + @"Tribute this face-up card to return 1 Spell Card sent to your (?:GY|Graveyard) by your " +
                @"opponent's card effect during this turn to the (bottom|top) of your Deck" + Finish,
                m => SelfTributeGyRecovery(DeckDestination(m, 1), EffectZoneFilter.ControllerGySpells, byBattle: false)),

            // Return of the Doomed (c19827717): ACTIVATE (EVENT_FREE_CHAIN), cost DiscardHand of 1 monster
            // (REASON_COST | REASON_DISCARD); the operation registers a FIELD | CONTINUOUS EVENT_PHASE + PHASE_END effect
            // for this turn (count limit 1): if a monster in your GY has GetTurnID() == this turn and REASON_BATTLE,
            // select 1 and SendtoHand. No target; it can be activated with no such monster.
            new AtomRegistration.Atom(
                Begin + @"Discard 1 Monster Card from your hand to the (?:GY|Graveyard)\.\s*Return 1 of your monsters " +
                @"destroyed and sent to your (?:GY|Graveyard) as a result of battle during this turn to your hand at " +
                @"the end of this turn" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase,
                    Side = EffectSide.Controller,
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = "",
                    MakesChainLink = true
                }),

            // Revival Jam (c31709826): SINGLE + TRIGGER_O on EVENT_BATTLE_DESTROYED (in the GY, REASON_BATTLE), cost
            // PayLP(1000), the operation flags it (reset when it leaves the GY); then a FIELD | CONTINUOUS
            // EVENT_PHASE + PHASE_STANDBY effect in the GY, count limit 1: at its controller's Standby Phase, while
            // flagged, the flag is reset and it is Special Summoned in face-up Defense Position (no Chain Link).
            AtomRegistration.Atom.Sequence(
                Begin + @"When this card is destroyed by battle and sent to the (?:GY|Graveyard): You can pay (\d+) " +
                @"(?:Life Points|LP); Special Summon it in face-up Defense Position during your next turn['’]s " +
                @"Standby Phase" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.ArmThisSpecialSummonAtNextStandby,
                        Side = EffectSide.Controller,
                        PayLpAmount = Parse(m, 1, 1000),
                        RequiresThisDestroyedByBattle = true,
                        IsOptional = true,
                        CheckedAt = ConditionCheckedAt.Activation,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby,
                        Side = EffectSide.Controller,
                        ResolvesFromGy = true,
                        SummonInDefense = true,
                        MakesChainLink = false
                    }
                }),

            // Helpoemer (c76052811): a SINGLE | CONTINUOUS EVENT_BATTLE_DESTROYED effect flags it in the GY (reset when
            // it leaves); a FIELD + TRIGGER_F on EVENT_PHASE + PHASE_BATTLE in the GY, count limit 1, while the
            // opponent is the turn player and it is flagged: the opponent sends 1 random card from the hand to the GY
            // (REASON_DISCARD | REASON_EFFECT), if this card is still in the GY. The flag is re-armed each time, so it
            // repeats every opponent's Battle Phase. "Cannot be Special Summoned from the GY." is the summon condition
            // (SpecialSummonLegality), not part of this atom.
            new AtomRegistration.Atom(
                @"(?<=^\s*Cannot be Special Summoned from the (?:GY|Graveyard)\.\s*)" +
                @"At the end of your opponent's Battle Phase, if this card is in the (?:GY|Graveyard) because it was " +
                @"destroyed by battle: Make your opponent discard (\d+) random cards?\.\s*This card must be in the " +
                @"(?:GY|Graveyard) to activate and to resolve this effect" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndOfBattlePhase,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 1),
                    ResolvesFromGy = true,
                    OpponentTurnOnly = true,
                    RequiresThisDestroyedByBattle = true,
                    MakesChainLink = true
                }),

            // Rope of Life (c93382620): ACTIVATE on EVENT_BATTLE_DESTROYED; cost: the whole hand (this card excluded)
            // to the GY (REASON_COST | REASON_DISCARD), at least 1 card, every one discardable; EFFECT_FLAG_CARD_TARGET
            // on 1 of the destroyed monsters (eg) now in your GY that can be Special Summoned, with a free Monster
            // Zone; SpecialSummonStep (POS_FACEUP: the player picks Attack or Defense), then EFFECT_UPDATE_ATTACK +800
            // (RESET_EVENT | RESETS_STANDARD).
            new AtomRegistration.Atom(
                Begin + @"When a monster\(s\) is destroyed by battle and sent to your (?:GY|Graveyard): Discard your " +
                @"entire hand \(min\.\s*1\), then target 1 of those monsters; Special Summon it, and if you do, it " +
                @"gains (\d+) ATK" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    Amount = Parse(m, 1, 800),
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    TargetsThoseMonstersDestroyedByBattle = true,
                    DiscardEntireHandCost = true,
                    ActivationCondition = BattleDestroyedGyRecoveryRuntime.MonsterDestroyedByBattleSentToYourGy,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) && v > 0 ? v : fallback;
    }
}
