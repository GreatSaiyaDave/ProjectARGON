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
            // Triangle Ecstasy Spark (c12181376): compiled by the RdsRedoSpellTrap batch (RdsRedoSpellTrapAtoms) since its hollow
            // BoostHarpieLadySistersAtkAndLockTraps atom (v208: no runtime) was retired.
            // Fuh-Rin-Ka-Zan (c1781310) prints "Apply 1 of these effects. ● … ● … ● … ● …": a CHOICE, so its bullets are
            // not always-applied clauses and the card stays a Stub. These four bullet atoms are anchored to the WHOLE
            // text (a standalone bullet fragment, optional "●"): unanchored they matched the tail of other cards'
            // sentences (case-insensitive) and added a bogus Activate clause (3-Hump Lacooda, Bee List Soldier,
            // Wattberyx, X-Saber Airbellum, …; v208 QC). A card whose whole text is one such sentence (Raigeki, Harpie's
            // Feather Duster, Pot of Greed) compiles exactly as it did before these atoms existed (v206).
            // ● Discard 2 random cards from your opponent's hand.
            new AtomRegistration.Atom(
                @"^\s*(?:●\s*)?Discard (\d+) random cards? from your opponent's hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardRandomFromOpponentHand,
                    Side = EffectSide.Opponent,
                    Amount = Pos(m, 1),
                    MakesChainLink = true
                }),
            // ● Destroy all monsters your opponent controls.
            new AtomRegistration.Atom(
                @"^\s*(?:●\s*)?Destroy all monsters your opponent controls\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldMonsters,
                    TargetCount = 0,
                    MakesChainLink = true
                }),
            // ● Destroy all Spell and Trap Cards your opponent controls.
            new AtomRegistration.Atom(
                @"^\s*(?:●\s*)?Destroy all Spell and Trap Cards your opponent controls\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    TargetCount = 0,
                    MakesChainLink = true
                }),
            // ● Draw 2 cards.
            new AtomRegistration.Atom(
                @"^\s*(?:●\s*)?Draw (\d+) cards\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Draw,
                    Side = EffectSide.Controller,
                    Amount = Pos(m, 1),
                    MakesChainLink = true
                }),

            // Xing Zhen Hu (c76515293), its whole text (v208 QC: the loose ".*2 Set Spell.*Trap.*cannot be activated" also
            // compiled Cursed Fig's GY trigger as an ignition). Lua: ACTIVATE free chain, SelectTarget exactly 2 face-down
            // cards in either Spell & Trap Zone except itself; e2 makes those still Set its card targets as it resolves; e3
            // EFFECT_CANNOT_TRIGGER on them while it applies (QcHollowActionsRuntime.LockTargetedSetCard / SetCardActivationLocked).
            new AtomRegistration.Atom(
                @"^\s*Activate by selecting 2 Set Spell or Trap Cards on the field\.\s*The selected Spell or Trap Cards " +
                @"cannot be activated\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetTwoFaceDownSpellTrapsLockActivation,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FaceDownSpellTraps,
                    RequiresTargetChoice = true,
                    TargetCount = 2,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),
            // BattleRiders batch (v210): the v208-fenced B.E.S. atom ("^(?=…cannot be destroyed by battle…)(?=…remove 1
            // counter from this card…).*Place (\d+) counters.*this card") was removed. Its greedy span covered the whole B.E.S.
            // text, so the end-of-Damage-Step removal sentence could never compile, and its clause had no Normal Summon gate.
            // B.E.S. Big Core's counter sentence is SummonTriggersAndTributesAtoms' whole sentence (Normal Summon only); the
            // MK-2 / MK-3 wordings ("When this card is Special Summoned", "If this card is Normal or Special Summoned") have
            // no atom and stay uncompiled.
            // v208 QC: Chain Burst's ".*activates a Trap Card.*take 1000 damage" (compiled as a one-shot Activate burn to the
            // opponent; it is a Continuous Trap that damages the player who activates each Trap) and Covering Fire's two
            // copies of ".*target 1 other face-up monster you control.*gains ATK equal to that target.*ATK" (no Damage Step
            // window; the gain lasted the turn) were removed: both cards are uncompiled again, as in v206.
            // PGD — Dark Designator (c78053598): Declare 1 Monster Card name. If the declared card is in your opponent's
            // Deck, add 1 of that card to your opponent's hand. Declaration is over the declarable monster pool; resolution
            // searches the opponent's Deck by name and adds one copy to their hand.
            new AtomRegistration.Atom(
                @"^\s*Declare 1 Monster Card name\.\s*If the declared card is in your opponent's Deck, add 1 of that card " +
                @"to your opponent's hand\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DeclareCardNameThenAddFromOpponentDeckToHand,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // IOC — D.D. Designator (c33423043): Declare 1 card name. Look at your opponent's hand; if they hold it,
            // banish that copy. Otherwise banish 1 random card from your hand.
            new AtomRegistration.Atom(
                @"^\s*Declare 1 card name\.\s*Look at your opponent's hand, and if they have the declared card in their " +
                @"hand, remove that 1 card from play\.\s*If they do not have the declared card in their hand, remove 1 " +
                @"random card in your hand from play\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DeclareCardNameThenBanishOrSelfDiscardRandom,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // DCR — Archfiend's Oath (c22796548): Once per turn, pay 500 LP, declare 1 card name, excavate the top card
            // of your Deck; if it matches the declared name add it to hand, otherwise send it to the GY.
            new AtomRegistration.Atom(
                @"^\s*Once per turn:\s*You can pay 500 Life Points, then declare 1 card name;\s*excavate the top card of " +
                @"your Deck, and if it is the declared card, add it to your hand\.\s*Otherwise, send it to the Graveyard\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DeclareCardNameThenPayLpExcavateTopMatchAddOrSendToGy,
                    Side = EffectSide.Controller,
                    OncePerTurn = true,
                    MakesChainLink = true
                }),
            // — 81820689: "Select and see 1 card in your opponent's hand."
            new AtomRegistration.Atom(
                @"^\s*Select and see 1 card in your opponent's hand\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.PeekOpponentHandCard,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // — 45895206: "Flip all face-down monsters on your opponent's side of the field face-up."
            new AtomRegistration.Atom(
                @"^\s*Flip all face-down monsters on your opponent's side of the field face-up\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.FlipAllOpponentFaceDownMonstersFaceUp,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // — 87210505: "Tribute 1 face-up "Dark Magician"; Special Summon 1 "Dark Magician Knight" from your hand, Deck or Graveyard."
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 face-up ""Dark Magician""; Special Summon 1 ""Dark Magician Knight"" from your hand, Deck or Graveyard\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    Side = EffectSide.Controller,
                    NamedCard = "Dark Magician Knight",
                    FromHand = true,
                    FromDeck = true,
                    FromGrave = true,
                    RequiresTributeCount = 1,
                    TributeNamedCard = "Dark Magician",
                    TributeFaceUpOnly = true,
                    MakesChainLink = true
                }),
            // — 43580269: "Each time this card destroys an opponent's monster and sends it to the Graveyard as a
            // result of battle, increase the ATK of this card by 700 points."
            new AtomRegistration.Atom(
                @"^\s*Each time this card destroys an opponent's monster and sends it to the Graveyard as a result of " +
                @"battle, increase the ATK of this card by 700 points\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.ChangeThisCardAtkWhileFaceUp,
                    Side = EffectSide.Controller,
                    Amount = 700,
                    MakesChainLink = false
                }),
            // — 87557188: "FLIP: Reveal all face-down cards on the field (Flip Effects are not activated), then
            // return them to their original positions."
            new AtomRegistration.Atom(
                @"^\s*FLIP: Reveal all face-down cards on the field \(Flip Effects are not activated\), then " +
                @"return them to their original positions\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.RevealAllFaceDownCardsThenReturn,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
            // — 10809984 Great Phantom Thief: "If this card inflicts battle damage to your opponent: Declare 1 card
            // name; look at your opponent's hand, and if you do, discard all cards with that name in their hand."
            new AtomRegistration.Atom(
                @"^\s*If this card inflicts battle damage to your opponent: Declare 1 card name; look at your " +
                @"opponent's hand, and if you do, discard all cards with that name in their hand\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.DeclareCardNameThenOpponentDiscardsAllOrSelfDiscardsRandom,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 17092736 Ancient Telescope: "See the top 5 cards of your opponent's Deck. Return the cards to the
            // Deck in the same order."
            new AtomRegistration.Atom(
                @"^\s*See the top 5 cards of your opponent's Deck\. Return the cards to the Deck in the same " +
                @"order\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.PeekTopOpponentDeckCards,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),
            // — 33413638 Cockroach Knight: "When this card is sent to the Graveyard, it is returned to the top of the Deck."
            new AtomRegistration.Atom(
                @"^\s*When this card is sent to the Graveyard, it is returned to the top of the Deck\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.ReturnThisToTopOfDeck,
                    Side = EffectSide.Controller,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 97809599: "Pay 1000 Life Points. Pick up and see all face-down cards on your opponent's side of the field, then return them to their original positions."
            new AtomRegistration.Atom(
                @"^\s*Pay 1000 Life Points\. Pick up and see all face-down cards on your opponent's side of the field, then " +
                @"return them to their original positions\.(?: \(The effects of the face-down cards are not activated\.\))?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.LookAtOppFaceDownCardsPayLp,
                    Side = EffectSide.Controller,
                    PayLpAmount = 1000,
                    MakesChainLink = true
                }),
            // — 85602018: "If a monster on your side of the field was sent to your Graveyard this turn, you can
            // Special Summon 1 monster with an ATK of 1500 points or less from your Deck once during this turn."
            new AtomRegistration.Atom(
                @"^\s*If a monster on your side of the field was sent to your Graveyard this turn, you can " +
                @"Special Summon 1 monster with an ATK of 1500 points or less from your Deck once during this " +
                @"turn\. Then shuffle your Deck\.\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromDeck,
                    Side = EffectSide.Controller,
                    FromDeck = true,
                    AmountIsAtkMax = true,
                    Amount = 1500,
                    OncePerTurn = true,
                    ActivationCondition = "YourLevelOrHigherMonsterSentToGyThisTurn",
                    ConditionAmount = 1,
                    MakesChainLink = true
                }),
            // — 19159413: "Select 1 Spell Card on the field and destroy it."
            new AtomRegistration.Atom(
                @"^\s*Select 1 Spell Card on the field and destroy it\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    TargetCount = 1,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),
            // — 25880422: "Target 1 face-up Attack Position monster your opponent controls; change that target to face-up Defense Position."
            new AtomRegistration.Atom(
                @"^\s*Target 1 face-up Attack Position monster your opponent controls; change that target to face-up Defense Position\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ChangeFaceUpAttackTargetToFaceUpDefense,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppAttackPositionMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    TargetCount = 1,
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
