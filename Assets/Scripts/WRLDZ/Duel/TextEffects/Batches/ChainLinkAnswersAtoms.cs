using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ChainLinkAnswers": Chain answers whose legality reads the answered link (its card, its kind, its
    /// targets, what it would destroy), and one continuous negation read as a link resolves.
    /// <list type="bullet">
    /// <item>Jar Robber (c33784505): Quick-Play; answers only the activation of "Pot of Greed"; negates that EFFECT (not
    /// the activation), then you draw 1.</item>
    /// <item>Spell Shield Type-8 (c38275183): Counter Trap with two separate activations (two EFFECT_TYPE_ACTIVATE in
    /// Lua): a Spell activation that targets exactly 1 monster on the field (Riryoku Field's condition), or any Spell
    /// activation for the cost "Send 1 Spell Card from your hand to the GY"; each negates and destroys.</item>
    /// <item>My Body as a Shield (c69279219): Quick-Play; answers an opponent's Spell/Trap Card activation or monster effect
    /// that would destroy a monster on the field; pay 1500 LP; negate the activation and destroy that card.</item>
    /// <item>Chaos Command Magician (c72630549): a monster effect whose only target is this card is negated.</item>
    /// <item>Dramatic Rescue (c80193355): answers a card activation or monster effect that targets a face-up "Amazoness"
    /// monster; return it to its owner's hand, then Special Summon 1 other monster from your hand.</item>
    /// </list>
    /// Every atom is the card's WHOLE text (anchored ^…$), so a longer or shorter look-alike never compiles a fragment.
    /// Runtime: <see cref="ChainLinkAnswersRuntime"/>.
    /// </summary>
    public static class ChainLinkAnswersAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Jar Robber (c33784505): EFFECT_TYPE_ACTIVATE on EVENT_CHAINING; s.condition re:IsHasType(EFFECT_TYPE_ACTIVATE)
            // and re:GetHandler():IsCode(55144522) and IsChainNegatable(ev) (either player's activation); s.target
            // IsPlayerCanDraw(tp,1); s.activate: if Duel.NegateEffect(ev) then Duel.Draw(tp,1).
            new AtomRegistration.Atom(
                @"^You can only activate this card when ""([^""]+)"" is activated\. Negate the effect of the ""\1"" " +
                @"and draw 1 card from your Deck\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateEffectOfNamedActivationThenDraw,
                    NamedCard = m.Groups[1].Value.Trim(),
                    Amount = 1,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    MakesChainLink = true
                }),
            // Spell Shield Type-8 (c38275183): e1 / e2, two EFFECT_TYPE_ACTIVATE effects on EVENT_CHAINING (either player's
            // Spell Card activation, re:IsSpellEffect() and IsHasType(EFFECT_TYPE_ACTIVATE)). e1 condition1: the link has
            // EFFECT_FLAG_CARD_TARGET and its CHAININFO_TARGET_CARDS are exactly 1 card in LOCATION_MZONE. e2: cost
            // Duel.DiscardHand(tp, IsSpell + IsAbleToGraveAsCost, 1, 1, REASON_COST). Both: NegateActivation, then
            // Destroy(eg) while it is still related. Each bullet is offered only when its own condition holds.
            AtomRegistration.Atom.Sequence(
                @"^Activate 1 of these effects\.\s*●\s*When a Spell Card is activated that targets exactly 1 monster on " +
                @"the field: Negate the activation, and if you do, destroy it\.\s*●\s*When a Spell Card is activated: " +
                @"Send 1 Spell Card from your hand to the (?:GY|Graveyard); negate the activation, and if you do, " +
                @"destroy it\.$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        EffectChoiceIndex = 1,
                        ChainResponseOnly = true,
                        NegateCardActivationOnly = true,
                        NegateRespondsToSpell = true,
                        NegateRequiresSingleMonsterTarget = true,
                        DestroyNegatedCard = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        EffectChoiceIndex = 2,
                        ChainResponseOnly = true,
                        NegateCardActivationOnly = true,
                        NegateRespondsToSpell = true,
                        CostSendSpellsFromHandCount = 1,
                        DestroyNegatedCard = true,
                        MakesChainLink = true
                    }
                }),
            // Divine Wrath (c49010598): EFFECT_TYPE_ACTIVATE on EVENT_CHAINING; s.condition re:IsMonsterEffect() and
            // IsChainNegatable(ev) — the answered link is a monster's own EFFECT (an ignition or a field Quick Effect),
            // NOT a Spell/Trap Card activation, which is what every other answer here reads. Cost: DiscardHand 1
            // (REASON_COST|REASON_DISCARD). NegateActivation, then Destroy(eg) while it still relates. My Body as a
            // Shield reaches the same place from the other direction: it answers a card activation OR a monster effect
            // that would destroy a monster, so it keeps NegateRespondsToSpell/Trap and its own gate.
            new AtomRegistration.Atom(
                @"^When a monster effect is activated: Discard 1 card; negate the activation, and if you do, " +
                @"destroy that monster\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateMonsterEffectActivationAndDestroy,
                    ChainResponseOnly = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    NegateRespondsToMonsterEffect = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // My Body as a Shield (c69279219): EFFECT_TYPE_ACTIVATE on EVENT_CHAINING, EFFECT_FLAG_DAMAGE_STEP; s.condition:
            // tp~=ep (the opponent's), IsChainNegatable(ev), re:IsMonsterEffect() or IsHasType(EFFECT_TYPE_ACTIVATE), and
            // GetOperationInfo(ev, CATEGORY_DESTROY) holds a monster on the field. Cost.PayLP(1500); NegateActivation, then
            // Destroy(eg) while it is still related.
            new AtomRegistration.Atom(
                @"^When your opponent activates a Spell/Trap Card or monster effect that would destroy a monster\(s\) on " +
                @"the field: Pay (\d+) LP; negate the activation, and if you do, destroy it\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    PayLpAmount = int.Parse(m.Groups[1].Value),
                    ChainResponseOnly = true,
                    NegateOpponentActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRespondsToTrap = true,
                    NegateRequiresWouldDestroyMonsterOnField = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // Chaos Command Magician (c72630549): e2 FIELD+CONTINUOUS on EVENT_CHAIN_SOLVING (LOCATION_MZONE):
            // re:IsActiveType(TYPE_EFFECT) with EFFECT_FLAG_CARD_TARGET whose CHAININFO_TARGET_CARDS are exactly this card
            // → Duel.NegateEffect(ev). (e1, EFFECT_DISABLE on a monster whose only lasting card target is this card, has
            // no subject here: the engine's only monster lasting target is a Charmer's control, whose Lua condition
            // reads the card target, not the Charmer's disable.)
            new AtomRegistration.Atom(
                @"^Negate the effect of a Monster Card that targets this 1 card\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis,
                    Side = EffectSide.Controller,
                    AffectsThisCardOnly = true,
                    MakesChainLink = false
                }),
            // Dramatic Rescue (c80193355): EFFECT_TYPE_ACTIVATE on EVENT_BECOME_TARGET; s.condition: re is a card activation
            // or a monster effect, and a target is a face-up "Amazoness" monster in a Monster Zone (either player's) that
            // can go to the hand, with a Monster Zone for you once it left (GetMZoneCount(tp,c)>0). s.target: a monster in
            // your hand that can be Special Summoned. s.activate: return those still-targeted monsters to the hand; if one
            // got there, Special Summon 1 monster from your hand other than them (face-up).
            new AtomRegistration.Atom(
                @"^Activate only when a card is activated that targets an? ""([^""]+)"" monster\. Return the targeted " +
                @"monster to its owner's hand and Special Summon 1 other monster from your hand\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand,
                    NamedCard = m.Groups[1].Value.Trim(),
                    NamedCardIsSeries = true,
                    ChainResponseOnly = true,
                    MakesChainLink = true
                }),
        };
    }
}
