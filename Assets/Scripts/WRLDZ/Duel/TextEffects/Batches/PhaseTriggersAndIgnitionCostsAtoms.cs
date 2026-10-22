using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "PhaseTriggersAndIgnitionCosts": Standby triggers, monster and Spell ignitions with LP or Tribute costs,
    /// a battle-destroy draw, a mirror Token and a dice choice. Every atom is one card's whole sentence(s), anchored at a
    /// sentence start and end, so a longer or reworded text stays uncompiled. Runtime:
    /// <see cref="PhaseTriggersAndIgnitionCostsRuntime"/>.
    /// </summary>
    public static class PhaseTriggersAndIgnitionCostsAtoms
    {
        /// <summary>Sentence start: text start or after a sentence-ending mark.</summary>
        const string Start = @"(?<=^|[.!?]\s+)";
        /// <summary>Sentence end: a period followed by whitespace or the end of the text, or the end of the text.</summary>
        const string End = @"(?:\.(?=\s|$)|$)";
        const string Apos = @"['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Amazoness Blowpiper (c73574678): FIELD TRIGGER_F on EVENT_PHASE+PHASE_STANDBY from the Monster Zone, count
            // limit 1, condition Duel.IsTurnPlayer(tp); EFFECT_FLAG_CARD_TARGET ("Select" is the old targeting wording): 1
            // face-up monster the opponent controls; EFFECT_UPDATE_ATTACK -500, RESETS_STANDARD_PHASE_END. No target: nothing.
            new AtomRegistration.Atom(
                Start + @"Select 1 face-up monster your opponent controls during each of your Standby Phases\.\s*" +
                @"It loses (\d+) ATK until the end of this turn" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby,
                    Side = EffectSide.Opponent,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Amount = Parse(m, 1, 500),
                    MakesChainLink = true
                }),

            // Helping Robo for Combat (c47025270): SINGLE TRIGGER_F on EVENT_BATTLE_DESTROYING; Draw(tp,1), then
            // (BreakEffect) its controller selects 1 card in the hand and sends it to the Deck bottom (SEQ_DECKBOTTOM).
            new AtomRegistration.Atom(
                Start + @"If this card destroys an opponent" + Apos + @"s monster by battle:\s*Draw 1 card, then return " +
                @"1 card from your hand to the bottom of the Deck" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ThisCardDestroysByBattle,
                    Action = EffectActionKind.DrawThenPlaceHandCardOnDeckBottom,
                    Side = EffectSide.Controller,
                    Amount = 1,
                    MakesChainLink = true
                }),

            // Mass Driver (c34906152): Continuous Spell; e2 EFFECT_TYPE_IGNITION in the Spell & Trap Zone, no count limit;
            // cost CheckReleaseGroupCost / Release 1 monster you control; Damage(1-tp, 400) if this card is still there.
            new AtomRegistration.Atom(
                Start + @"Tribute 1 monster on your side of the field to inflict (\d+) points of damage to your " +
                @"opponent" + Apos + @"s Life Points" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpFieldIgnition,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 400),
                    RequiresTributeCount = 1,
                    RequiresThisRelatedAtResolution = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // — 14702066 Cannon Soldier MK-2: You can Tribute 2 monsters to inflict 1500 damage to your opponent.
            new AtomRegistration.Atom(
                Start + @"You can Tribute (\d+) monsters? (?:to inflict|; inflict) (\d+) damage to your opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = int.Parse(m.Groups[2].Value),
                    RequiresTributeCount = int.Parse(m.Groups[1].Value),
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // — 27869883 Shadowpriestess of Ohm: You can Tribute 1 face-up DARK monster you control to inflict 800 damage to your opponent.
            new AtomRegistration.Atom(
                Start + @"You can Tribute (\d+) face-up (?:\w+ )?monster(?:s)? you control to inflict (\d+) damage to your opponent" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = int.Parse(m.Groups[2].Value),
                    RequiresTributeCount = int.Parse(m.Groups[1].Value),
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Senri Eye (c60391791): Continuous Spell; FIELD TRIGGER_O on EVENT_PHASE+PHASE_STANDBY from the Spell & Trap
            // Zone, count limit 1, condition: your turn and the opponent's Deck is not empty; cost Cost.PayLP(100);
            // ConfirmCards(tp, the opponent's top card) — the card stays on top, the opponent does not see it.
            new AtomRegistration.Atom(
                Start + @"Once per turn, during each of your Standby Phases, you can pay (\d+) Life Points to look at 1 " +
                @"card on the top of your opponent" + Apos + @"s Deck and return it to its original position\.\s*" +
                @"Your opponent cannot look at the card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 100),
                    OncePerTurn = true,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Magical Scientist (c34206604): EFFECT_TYPE_IGNITION in the Monster Zone, no count limit; cost Cost.PayLP(1000);
            // target chk: a Level 6 or lower Fusion Monster in the Extra Deck that can be Special Summoned and a free zone;
            // SpecialSummon(tc,0,…,POS_FACEUP) (not a Fusion Summon); EFFECT_CANNOT_DIRECT_ATTACK (RESETS_STANDARD) and a
            // PHASE_END continuous effect that sends it to the Extra Deck (RESETS_STANDARD_PHASE_END).
            new AtomRegistration.Atom(
                Start + @"Pay (\d+) Life Points to Special Summon 1 level (\d+) or lower Fusion Monster from your Extra " +
                @"Deck in face-up Attack or Defense Position\.\s*That Fusion Monster cannot attack your opponent" + Apos +
                @"s Life Points directly, and is returned to your Extra Deck at the end of the turn" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase,
                    Side = EffectSide.Controller,
                    PayLpAmount = Parse(m, 1, 1000),
                    Amount = Parse(m, 2, 6),
                    ActivationCondition = PhaseTriggersAndIgnitionCostsRuntime.CanSummonLevelCappedFusionFromExtra,
                    CheckedAt = ConditionCheckedAt.Activation,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Roulette Barrel (c46303688): EFFECT_TYPE_IGNITION in the Monster Zone, count limit 1; TossDice(tp,2); when the
            // results differ AnnounceNumber(d1,d2); SelectMatchingCard 1 face-up monster on either field whose Level is the
            // chosen number (not a target); Destroy.
            new AtomRegistration.Atom(
                Start + @"Once per turn:\s*You can roll a six-sided die twice, choose 1 result, and destroy 1 monster on " +
                @"the field whose Level is equal to that result" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel,
                    Side = EffectSide.Both,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),

            // Physical Double (c63442604): Normal Trap, free chain, condition: not your turn; EFFECT_FLAG_CARD_TARGET 1
            // face-up monster the opponent controls with a Level; a free zone and IsPlayerCanSpecialSummonMonster; at
            // resolution (the target still face-up): a "Mirage Token" with the target's current ATK / DEF (as its base),
            // Level, Type and Attribute, Special Summoned face-up; destroyed in the End Phase (PHASE_END continuous).
            new AtomRegistration.Atom(
                Start + @"During your opponent" + Apos + @"s turn:\s*Target 1 face-up monster your opponent controls;\s*" +
                @"Special Summon 1 ""([^""]+ Token)"" with the same Level, Type, Attribute, ATK, and DEF as that target\.\s*" +
                @"Destroy this Token during the End Phase" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.OppFaceUpMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    OpponentTurnOnly = true,
                    TokenName = m.Groups[1].Value,
                    ActivationCondition = PhaseTriggersAndIgnitionCostsRuntime.CanSummonCopyToken,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback)
        {
            if (m == null || !m.Success || m.Groups.Count <= group) return fallback;
            return int.TryParse(m.Groups[group].Value, out var n) && n > 0 ? n : fallback;
        }
    }
}
