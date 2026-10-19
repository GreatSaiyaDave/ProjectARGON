using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmCostLinkedSpells" (v211-tlm-fet): Normal Spells / Traps whose cost or activation event feeds the
    /// effect — Battery Charger, Minefield Eruption, Card of Sanctity, Gift of the Martyr, Double Attack, Hero Signal.
    /// Every atom is the card's whole text (anchored at both ends), so a longer card that only contains part of it
    /// stays uncompiled. Runtime: <see cref="TlmCostLinkedSpellsRuntime"/>.
    /// </summary>
    public static class TlmCostLinkedSpellsAtoms
    {
        const string Begin = @"^\s*";
        const string Finish = @"\.?\s*$";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Battery Charger (c61181383): EFFECT_TYPE_ACTIVATE free chain, Cost.PayLP(500); EFFECT_FLAG_CARD_TARGET on 1
            // SET_BATTERYMAN monster in your GY that can be Special Summoned, with a free Monster Zone (the old-text
            // "Special Summon 1" is a SelectTarget, so it targets); POS_FACEUP — the player picks Attack or Defense.
            new AtomRegistration.Atom(
                Begin + @"Pay (\d+) Life Points\.\s*Special Summon 1 ""([^""]+)"" monster from your Graveyard" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    TargetSeriesName = m.Groups[2].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    TargetCount = 1,
                    PayLpAmount = Parse(m, 1, 500),
                    MakesChainLink = true
                }),

            // Minefield Eruption (c85519211): Normal Trap, free chain; target chk: a face-up "Mine Golem" (IsCode) you
            // control. Resolution: the face-up "Mine Golem" you control now (GetMatchingGroup), Damage(1-tp, #g*1000),
            // then Destroy(g) — one event. Two clauses in resolution order, each counted when it applies.
            AtomRegistration.Atom.Sequence(
                Begin + @"Inflict (\d+) damage to your opponent for each face-up ""([^""]+)"" on your side of the " +
                @"field\.\s*After that, destroy all face-up ""\2""(?:\(s\))? on your side of the field" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Side = EffectSide.Opponent,
                        Amount = Parse(m, 1, 1000),
                        CountSource = TlmCostLinkedSpellsRuntime.FaceUpNamedYouControl,
                        ConditionName = m.Groups[2].Value,
                        ActivationCondition = TlmCostLinkedSpellsRuntime.YouControlFaceUpNamed,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.DestroyAllFaceUpNamedYouControl,
                        Side = EffectSide.Controller,
                        ConditionName = m.Groups[2].Value,
                        MakesChainLink = true
                    }
                }),

            // Card of Sanctity (c42664989): cost — banish every other card you control and in your hand
            // (IsAbleToRemoveAsCost, one Duel.Remove); the printed "(min. 1 from each)" is kept (stricter than the Lua
            // chk, which needs 1 card across both). Target: IsPlayerCanDraw; resolution draws 2 minus the hand size.
            new AtomRegistration.Atom(
                Begin + @"Banish all other cards you control and in your hand \(min\.\s*1 from each\);\s*draw until " +
                @"you have (\d+) cards in your hand" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DrawUntilHandSize,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 2),
                    CostBanishAllOtherHandAndField = true,
                    ActivationCondition = TlmCostLinkedSpellsRuntime.OtherCardsInHandAndOnField,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Gift of the Martyr (c98792570): cost (in s.target) — send 1 monster you control with a printed ATK above 0
            // (GetTextAttack, face-down allowed) that leaves a face-up monster to target; SetTargetParam(its text ATK);
            // then SelectTarget 1 face-up monster you control. Resolution: still face-up and related → EFFECT_UPDATE_ATTACK
            // + that ATK, RESETS_STANDARD_PHASE_END.
            new AtomRegistration.Atom(
                Begin + @"Send 1 monster on your side of the field to the Graveyard\.\s*Select 1 monster on your side " +
                @"of the field\.\s*The selected monster gains ATK equal to the ATK of the sent monster until the end " +
                @"of this turn" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetGainsCostTextAtkUntilEndOfTurn,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    RequiresSendOtherYouControl = true,
                    MakesChainLink = true
                }),

            // Double Attack (c34187685): condition IsAbleToEnterBP; cost (in s.target) — discard 1 Monster with an original
            // Level above 1 while a face-up monster you control has a lower Level and no EFFECT_EXTRA_ATTACK; then
            // SelectTarget 1 such monster below the discarded monster's Level. Resolution: related → EFFECT_EXTRA_ATTACK 1,
            // RESETS_STANDARD_PHASE_END.
            new AtomRegistration.Atom(
                Begin + @"Discard 1 Monster Card from your hand to the Graveyard\.\s*Select 1 monster on your side of " +
                @"the field with a lower Level than the discarded monster\.\s*The selected monster can attack twice " +
                @"this turn" + Finish,
                _ => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetGainsExtraAttackThisTurn,
                    Side = EffectSide.Controller,
                    Zone = EffectZoneFilter.ControllerAnyMonsters,
                    Amount = 1,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "",
                    ActivationCondition = TlmCostLinkedSpellsRuntime.CanEnterBattlePhase,
                    MakesChainLink = true
                }),

            // Hero Signal (c22020907): EFFECT_TYPE_ACTIVATE on EVENT_BATTLE_DESTROYED; condition: a monster in eg destroyed
            // by battle, now in the GY, whose previous controller is you. Target chk: a free Monster Zone and a Level 4 or
            // lower SET_ELEMENTAL_HERO in the hand or Deck that can be Special Summoned. Resolution (no target): select 1,
            // Special Summon it POS_FACEUP.
            new AtomRegistration.Atom(
                Begin + @"When a monster you control is destroyed by battle and sent to the Graveyard:\s*Special Summon " +
                @"1 Level (\d+) or lower ""([^""]+)"" monster from your hand or Deck" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonLowLevelSeriesFromHandOrDeck,
                    Side = EffectSide.Controller,
                    SummonLevelMax = Parse(m, 1, 4),
                    TargetSeriesName = m.Groups[2].Value,
                    ActivationCondition = TlmCostLinkedSpellsRuntime.MonsterYouControlledDestroyedByBattle,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),
        };

        static int Parse(Match m, int group, int fallback) =>
            int.TryParse(m.Groups[group].Value, out var v) && v > 0 ? v : fallback;
    }
}
