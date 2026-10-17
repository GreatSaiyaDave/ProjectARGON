using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SimpleSpellTraps" (v160 LOD): whole-text atoms, not a passcode list. Each pattern is the card's
    /// whole text (anchored), so a longer card with the same phrase is not absorbed.
    /// <list type="bullet">
    /// <item>"Return 1 Level N or higher Type monster you control to the hand, and if you do, destroy all Spell and Trap
    /// Cards on the field." (A Wingbeat of Giant Dragon)</item>
    /// <item>"Tribute 1 Type monster; destroy all face-up monsters on the field whose DEF is less than or equal to the
    /// ATK the Tributed monster had on the field." (Burst Breath)</item>
    /// <item>"If you control a Type monster: Activate 1 of these effects; ● Inflict N damage to your opponent.
    /// ● Target 1 monster with M or less DEF; destroy that target." (Dragon's Gunfire)</item>
    /// <item>"Target 1 TypeA or TypeB monster on the field; change that target to face-down Defense Position." (Ready for
    /// Intercepting)</item>
    /// <item>Fiend Comedian's coin call (whole text).</item>
    /// <item>"Send any number of other Spell/Trap Cards you control to the Graveyard; gain N LP for each card sent to the
    /// Graveyard this way." (Emergency Provisions)</item>
    /// <item>"All TypeA-Type monsters you control gain N ATK for each TypeB or TypeC-Type monster you control." (The A.
    /// Forces)</item>
    /// </list>
    /// Runtime: <see cref="SimpleSpellTrapsRuntime"/>.
    /// </summary>
    public static class SimpleSpellTrapsAtoms
    {
        const string TypeWord = @"([A-Za-z]+(?:-(?!Type\b)[A-Za-z]+)*)(?:-Type)?";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // A Wingbeat of Giant Dragon (c28596933): no target (Duel.SelectMatchingCard at resolution); the destroy
            // happens only if the monster reached the hand. Activation needs such a monster and another Spell/Trap.
            new AtomRegistration.Atom(
                @"^\s*Return 1 Level (\d+) or higher " + TypeWord + @" monster you control to the hand, and if you do, " +
                @"destroy all Spell and Trap Cards on the field\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps,
                    Side = EffectSide.Both,
                    ConditionAmount = Pos(m, 1),
                    ConditionName = m.Groups[2].Value.Trim(),
                    ActivationCondition = SimpleSpellTrapsRuntime.OwnRaceLevelMonsterAndOtherSpellTrap,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Burst Breath (c80163754): the Tribute is the cost; its ATK on the field is the cost numeric.
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 " + TypeWord + @" monster;\s*destroy all face-up monsters on the field whose DEF is " +
                @"less than or equal to the ATK the Tributed monster had on the field\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk,
                    Side = EffectSide.Both,
                    RequiresTributeCount = 1,
                    TributeRaceFilter = m.Groups[1].Value.Trim(),
                    MakesChainLink = true
                }),

            // Dragon's Gunfire (c55991637): two EFFECT_TYPE_ACTIVATE effects with the same condition; the player
            // picks one when the card is activated. Bullet 2 targets a face-up monster (s.dfilter IsFaceup).
            AtomRegistration.Atom.Sequence(
                @"^\s*If you control an? " + TypeWord + @" monster:\s*Activate 1 of these effects;\s*" +
                @"●\s*Inflict (\d+) damage to your opponent\.\s*" +
                @"●\s*Target 1 monster with (\d+) or less DEF;\s*destroy that target\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = Pos(m, 2),
                        EffectChoiceIndex = 1,
                        ConditionName = m.Groups[1].Value.Trim(),
                        ActivationCondition = PoisonMeteorCestusRuntime.YouControlRace,
                        CheckedAt = ConditionCheckedAt.Activation,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.DestroyTargetWithDefAtMost,
                        Zone = EffectZoneFilter.FieldAnyMonster,
                        Side = EffectSide.Both,
                        Amount = Pos(m, 3),
                        RequiresTargetChoice = true,
                        RequiresTargetFaceUp = true,
                        IsPsctTarget = true,
                        EffectChoiceIndex = 2,
                        ConditionName = m.Groups[1].Value.Trim(),
                        ActivationCondition = PoisonMeteorCestusRuntime.YouControlRace,
                        CheckedAt = ConditionCheckedAt.Activation,
                        MakesChainLink = true
                    }
                }),

            // Ready for Intercepting (c31785398): a face-up monster of either Type (IsRace(WARRIOR|SPELLCASTER)) that can
            // be turned face-down; RaceFilter "A|B" is read by ClassicEraTemplates.RaceMatches.
            new AtomRegistration.Atom(
                @"^\s*Target 1 " + TypeWord + @" or " + TypeWord + @" monster on the field;\s*" +
                @"change that target to face-down Defense Position\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SetTargetFaceDownDefense,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    RaceFilter = m.Groups[1].Value.Trim() + "|" + m.Groups[2].Value.Trim(),
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),

            // Fiend Comedian (c81172176): Duel.CallCoin at resolution. Activation needs a card in the opponent's GY
            // and at least that many cards in your Deck.
            new AtomRegistration.Atom(
                @"^\s*Toss a coin and call it\.\s*If you call it right, all your opponent's cards in the Graveyard are " +
                @"removed from play immediately\.\s*If you call it wrong, send a number of cards equal to the cards in " +
                @"your opponent's Graveyard from your Deck to your Graveyard\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy,
                    Side = EffectSide.Both,
                    ActivationCondition = SimpleSpellTrapsRuntime.OppGyCardsAndDeckCovers,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Emergency Provisions (c53046408): cost 1..5 other Spell/Traps you control (SelectMatchingCard 1,5);
            // the LP gain is per card sent (e:SetLabel(#g)).
            new AtomRegistration.Atom(
                @"^\s*Send any number of other Spell/Trap Cards you control to the (?:Graveyard|GY);\s*gain (\d+) LP " +
                @"for each card sent to the (?:Graveyard|GY) this way\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainLpPerSpellTrapSentAsCost,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1),
                    ActivationCondition = SimpleSpellTrapsRuntime.OtherSpellTrapYouControl,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // The A. Forces (c403847): EFFECT_UPDATE_ATTACK on your Warriors; the value counts your face-up Warriors and
            // Spellcasters each time it is read (a live count, re-applied on every board refresh).
            new AtomRegistration.Atom(
                @"^\s*All " + TypeWord + @" monsters you control gain (\d+) ATK for each " + TypeWord + @" or " +
                TypeWord + @" monster you control\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value.Trim(),
                    Amount = Pos(m, 2),
                    DefAmount = 0,
                    CountSource = SimpleSpellTrapsRuntime.FaceUpRaceYouControl,
                    CountRaceFilter = m.Groups[3].Value.Trim() + "|" + m.Groups[4].Value.Trim(),
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Triangle Ecstasy Spark (c12181376): Until the end of this turn, the ATK of all "Harpie Lady Sisters"
            // currently on the field becomes 2700, your opponent cannot activate any Trap Cards, also negate all your
            // opponent's Trap effects on the field.
            new AtomRegistration.Atom(
                @"Until the end of this turn, the ATK of all ""Harpie Lady Sisters"" currently on the field becomes 2700, your opponent cannot activate any Trap Cards, also negate all your opponent's Trap effects on the field\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BoostHarpieLadySistersAtkAndLockTraps,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // Fuh-Rin-Ka-Zan (c1781310): choice — Discard 2 random cards from your opponent's hand.
            new AtomRegistration.Atom(
                @"Discard (\d+) random cards? from your opponent's hand",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = Pos(m, 1),
                    MakesChainLink = true
                }),
        };

        /// <summary>The whole text is one of this batch's atoms (an earlier phrase template must leave it alone).</summary>
        public static bool IsWholeTextAtom(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var atom in Atoms)
                if (atom.Pattern.IsMatch(text))
                    return true;
            return false;
        }

        static int Pos(Match m, int group) =>
            int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : 0;
    }
}
