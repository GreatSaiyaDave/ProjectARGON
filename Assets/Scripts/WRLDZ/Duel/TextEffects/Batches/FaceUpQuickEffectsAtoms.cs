using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "FaceUpQuickEffects" (v160): Quick Effects (Spell Speed 2) of face-up Continuous Traps, activated
    /// from the Spell &amp; Trap Zone after the card itself is active (ygopro EFFECT_TYPE_QUICK_O, SetRange
    /// LOCATION_SZONE, EVENT_FREE_CHAIN). Each atom is the card's whole text, so a longer card that only contains
    /// part of it stays uncompiled. The card's own activation is the plain Continuous Trap placement; the clause
    /// has <see cref="EffectTiming.FaceUpQuickEffect"/> and is driven by <see cref="FaceUpQuickEffectsRuntime"/>.
    /// </summary>
    public static class FaceUpQuickEffectsAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Type Zero Magic Crusher (c21237481): e2 QUICK_O, EVENT_FREE_CHAIN, LOCATION_SZONE, no count limit.
            // Cost: discard 1 Spell (c:IsSpell() and c:IsDiscardable(), REASON_COST|REASON_DISCARD). Target step:
            // SetTargetPlayer(1-tp), SetTargetParam(500). Operation: only while this card is still on the field
            // (IsRelateToEffect), Duel.Damage(opponent, 500).
            new AtomRegistration.Atom(
                @"^Discard 1 Spell Card from your hand to inflict (\d+) (?:points of )?damage to your opponent['’]s " +
                @"Life Points\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpQuickEffect,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Side = EffectSide.Opponent,
                    Amount = Parse(m, 1, 500),
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = FaceUpQuickEffectsRuntime.DiscardSpellCard,
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Skull Lair (c6733059): e1 QUICK_O, EVENT_FREE_CHAIN, LOCATION_SZONE, SetCountLimit(1,0,
            // EFFECT_COUNT_CODE_CHAIN). Cost (in s.target): banish N >= 1 monsters from your GY (IsAbleToRemoveAsCost,
            // aux.SpElimFilter) such that a face-up monster of exactly Level N is on the field (s.rescon); the count is
            // the chain's target param. Operation: select (no target) 1 face-up monster on either field of that Level
            // and destroy it; none left, nothing happens.
            new AtomRegistration.Atom(
                @"^Remove from play any number of Monster Cards in your (?:Graveyard|GY) to destroy 1 face-up monster " +
                @"on the field whose Level is equal to the number of the cards you removed from play\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.FaceUpQuickEffect,
                    Action = EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.FieldMonsters,
                    ChoiceDoesNotTarget = true,
                    OncePerChain = true,
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Soul Demolition (c76297408): e2 QUICK_O, EVENT_FREE_CHAIN, LOCATION_SZONE, EFFECT_FLAG_CARD_TARGET.
            // Condition (activation only): a face-up Fiend you control. Cost: Cost.PayLP(500). Target step (chk: a
            // monster in each GY): the turn player targets 1 monster in their opponent's GY, then the other player
            // targets 1 monster in their opponent's GY. Operation: banish the targets still in the GY.
            new AtomRegistration.Atom(
                @"^You can only activate this card['’]s effect when you have a ([A-Za-z][A-Za-z -]*?)-Type monster on " +
                @"your side of the field\. Pay (\d+) Life Points to use this effect\. Both players select 1 Monster " +
                @"Card from their opponent['’]s (?:Graveyard|GY)\. Remove the selected cards from play\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpQuickEffect,
                    Action = EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish,
                    Side = EffectSide.Both,
                    Zone = EffectZoneFilter.EitherGyMonsters,
                    // The effect's condition (not the card's activation): you control a face-up monster of this Type.
                    RaceFilter = m.Groups[1].Value.Trim(),
                    PayLpAmount = Parse(m, 2, 500),
                    RequiresTargetChoice = true,
                    IsQuickEffect = true,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // The Dragon's Bead (c92408984): e2 QUICK_O, EVENT_CHAINING, LOCATION_SZONE. Condition: the chained
            // activation is a Trap Card's (re:IsTrapEffect() and IsHasType(EFFECT_TYPE_ACTIVATE)) with
            // EFFECT_FLAG_CARD_TARGET whose targets include a face-up Dragon monster, and it can be negated
            // (IsChainDisablable). Cost: discard 1 card. Operation: if this card is still on the field, negate that
            // effect, and if that Trap Card is still related, destroy it. aux.DoubleSnareValidity.
            new AtomRegistration.Atom(
                @"^Discard 1 card from your hand\. Negate the effect of a Trap Card that targets 1 face-up " +
                @"([A-Za-z][A-Za-z -]*?)-Type monster and destroy the Trap Card\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.FaceUpQuickEffect,
                    Action = EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt,
                    Side = EffectSide.Both,
                    RaceFilter = m.Groups[1].Value.Trim(),
                    RequiresDiscardCost = true,
                    DiscardCostCount = 1,
                    DiscardCostAttribute = "*",
                    ChainResponseOnly = true,
                    NegateRespondsToTrap = true,
                    DestroyNegatedCard = true,
                    IsQuickEffect = true,
                    IsOptional = true,
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
