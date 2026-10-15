using System.Linq;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RecoveryAndPhaseCosts": GY / banished recovery, hand recycling, Standby and End Phase triggers with
    /// costs. Every atom is one card's whole text (anchored at both ends), so a longer or reworded text stays uncompiled.
    /// Old-text "Select" cards follow their official Lua script (targeting flags set explicitly). Runtime:
    /// <see cref="RecoveryAndPhaseCostsRuntime"/>.
    /// </summary>
    public static class RecoveryAndPhaseCostsAtoms
    {
        const string Start = @"^\s*";
        const string End = @"\.?\s*$";
        const string Gy = @"(?:GY|Graveyard)";

        static int Parse(Match m, int group, int fallback) =>
            m != null && group < m.Groups.Count && int.TryParse(m.Groups[group].Value, out var v) ? v : fallback;

        /// <summary>"Beast, Beast-Warrior, or Winged Beast" → "Beast|Beast-Warrior|Winged Beast".</summary>
        static string RaceList(string printed) =>
            string.Join("|", Regex.Split(printed ?? "", @"\s*,\s*(?:or\s+)?|\s+or\s+")
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Dimension Distortion (c95194279): condition GetFieldGroupCount(tp, LOCATION_GRAVE, 0) == 0; EFFECT_FLAG_CARD_TARGET
            // ("Select" is the old targeting wording) — 1 monster in YOUR banished pile (LOCATION_REMOVED, 0) that can be
            // Special Summoned, with a free Monster Zone; SpecialSummon(tc, 0, tp, tp, false, false, POS_FACEUP) if it still
            // relates. A face-down banished card can never be Special Summoned (ocgcore is_can_be_special_summoned).
            new AtomRegistration.Atom(
                Start + @"You can only activate this card if there are no cards in your " + Gy + @"\.\s*" +
                @"Select 1 removed from play monster and Special Summon it on your side of the field" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonBanishedTarget,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerBanishedFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    ChoiceDoesNotTarget = false,
                    TargetCount = 1,
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.NoCardsInYourGy,
                    MakesChainLink = true
                }),

            // Dimension Fusion (c23557835): cost Cost.PayLP(2000); chk — either player has a free zone and a face-up banished
            // monster of their own that can be Special Summoned; each player SelectMatchingCard(ft, ft) (not a target) and
            // SpecialSummonStep POS_FACEUP, one SpecialSummonComplete.
            new AtomRegistration.Atom(
                Start + @"Pay (\d+) Life Points\.\s*Both players Special Summon as many of their removed from play " +
                @"monsters as possible" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EachPlayerSpecialSummonsFaceUpBanishedMonstersAsPossible,
                    Side = EffectSide.Both,
                    PayLpAmount = Parse(m, 1, 2000),
                    IsPsctTarget = false,
                    ChoiceDoesNotTarget = true,
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.EitherPlayerCanSummonFaceUpBanished,
                    MakesChainLink = true
                }),

            // Salvage (c96947648): EFFECT_FLAG_CARD_TARGET — exactly 2 monsters in your GY, WATER, ATK 1500 or less
            // (IsAttackBelow), able to go to the hand; at resolution the targets that still relate are added (#sg > 0).
            new AtomRegistration.Atom(
                Start + @"Target (\d+) (DARK|LIGHT|EARTH|WATER|FIRE|WIND|DIVINE) monsters with (\d+) or less ATK in your " +
                Gy + @";\s*add those targets to your hand" + End,
                m =>
                {
                    var n = Parse(m, 1, 0);
                    if (n < 2) return null; // "monsters ... those targets": a plural, exact count
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.AddFromGyToHand,
                        Side = EffectSide.Controller,
                        Zone = EffectZoneFilter.ControllerGyMonsters,
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        ChoiceDoesNotTarget = false,
                        TargetCount = n,
                        TargetUpTo = false,
                        AttributeFilter = m.Groups[2].Value.ToUpperInvariant(),
                        Amount = Parse(m, 3, 0),
                        AmountIsAtkMax = true,
                        MakesChainLink = true
                    };
                }),

            // Recycle (c96316857): Continuous Spell (an empty Activate); FIELD TRIGGER_O in the S/T Zone on
            // EVENT_PHASE+PHASE_STANDBY, condition IsTurnPlayer(tp), count limit 1, cost Cost.PayLP(300); CARD_TARGET 1
            // non-Monster card in your GY ("select" = old targeting wording); SendtoDeck(SEQ_DECKBOTTOM) if Recycle and the
            // target both still relate.
            new AtomRegistration.Atom(
                Start + @"During your Standby Phase, by paying (\d+) Life Points, select 1 non-Monster Card in your " + Gy +
                @" and return it to the bottom of your Deck" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.PayLpReturnGyNonMonsterToDeckBottomAtYourStandby,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyCards,
                    Amount = Parse(m, 1, 300),
                    IsPsctTarget = true,
                    ChoiceDoesNotTarget = false,
                    OncePerTurn = true,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Reload (c22589918): Quick-Play; chk IsPlayerCanDraw(tp) and a card in the hand other than this one; the whole
            // hand to the Deck (SEQ_DECKSHUFFLE), shuffle, BreakEffect, draw #g.
            new AtomRegistration.Atom(
                Start + @"Add all cards in your hand to your Deck and shuffle it\.\s*Then draw the same number of cards " +
                @"that you added to your Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber,
                    Side = EffectSide.Controller,
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.AnotherCardInYourHand,
                    MakesChainLink = true
                }),

            // Mind Wipe (c52817046): Quick-Play; chk GetHandcount(1-tp) <= 3, then the OPPONENT's hand is added to
            // its owners' Decks (SEQ_DECKSHUFFLE), shuffled, and they draw #g. Same runtime as Reload
            // (ShuffleHandIntoDeckThenDraw), with Side = Opponent selecting the shuffled player.
            new AtomRegistration.Atom(
                Start + @"Activate only if your opponent has 3 or less cards in their hand\.\s*" +
                @"Your opponent adds their hand to the Deck and shuffles it, then draws the same number of cards " +
                @"they added to the Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber,
                    Side = EffectSide.Opponent,
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.OpponentHasAtMostThreeInHand,
                    MakesChainLink = true
                }),

            // Coach Goblin (c42541548): FIELD TRIGGER_O in the Monster Zone on EVENT_PHASE+PHASE_END, condition IsTurnPlayer,
            // count limit 1; cost: 1 Normal Monster in the hand, ConfirmCards, SendtoDeck(SEQ_DECKBOTTOM, REASON_COST); draw 1
            // if this card still relates and is face-up.
            new AtomRegistration.Atom(
                Start + @"While this card is face-up on your side of the field, you can return 1 Normal Monster Card in " +
                @"your hand to the bottom of your Deck during your End Phase to draw 1 card from your Deck\.\s*" +
                @"You can only use this effect once per turn" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    IsPsctTarget = false,
                    ChoiceDoesNotTarget = true,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Lighten the Load (c37231541), its whole text. Spell: once per turn, add 1 Level 7 or higher monster from
            // your hand to your Deck, shuffle it, then draw 1 card (Lua Duel.SelectMatchingCard + SEQ_DECKSHUFFLE,
            // gated on Duel.IsPlayerCanDraw). Coach Goblin places on the bottom at the End Phase, so this shuffles in
            // and carries its own Level floor rather than the Normal-Monster filter.
            new AtomRegistration.Atom(
                @"^Once per turn, you can add 1 Level (\d+) or higher monster from your hand to your Deck and " +
                @"shuffle it, then draw 1 card\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnHandLevelMonsterToDeckShuffleThenDraw,
                    Side = EffectSide.Controller,
                    MinLevelOrHigher = int.Parse(m.Groups[1].Value),
                    Amount = 1,
                    ActivationCondition = RecoveryAndPhaseCostsRuntime.HandHasLevelOrHigherMonster,
                    CheckedAt = ConditionCheckedAt.Activation,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Manticore of Darkness (c77121851): EVENT_TO_GRAVE (not REASON_RETURN / REASON_ADJUST) flags it until the end of
            // the turn; FIELD TRIGGER_O in the GY on EVENT_PHASE+PHASE_END (either player's), count limit 1; cost: send 1
            // Beast / Beast-Warrior / Winged Beast from the hand or face-up Monster Zone (a zone must be free after it); chk
            // IsCanBeSpecialSummoned; SpecialSummon(c, POS_FACEUP) if it still relates.
            new AtomRegistration.Atom(
                Start + @"During the End Phase of the turn this card was sent to the " + Gy + @":\s*You can send to the " +
                Gy + @" 1 ((?:[A-Za-z-]+(?: [A-Za-z-]+)?)(?:,\s*(?:or\s+)?[A-Za-z-]+(?: [A-Za-z-]+)?)*)-Type monster from " +
                @"your hand or your side of the field;\s*Special Summon this card from your " + Gy + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase,
                    Side = EffectSide.Controller,
                    RaceFilter = RaceList(m.Groups[1].Value),
                    ResolvesFromGy = true,
                    IsPsctTarget = false,
                    ChoiceDoesNotTarget = true,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };
    }
}
