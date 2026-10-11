using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellTrapAnswersAndActivationLocks": Counter Traps answering Spell/Trap Card activations, and activation
    /// locks.
    /// <list type="bullet">
    /// <item>Judgment of Anubis (c55256016): answers the opponent's Spell Card activation that would destroy a Spell/Trap Card
    /// on the field; discard 1; negate, destroy; then you can destroy 1 face-up monster the opponent controls (selected, not
    /// targeted) and burn its ATK.</item>
    /// <item>Spell Vanishing (c29735721): discard 2; negate a Spell Card activation (either player's), destroy it; look at the
    /// opponent's hand and Deck and send every card with that code to the GY.</item>
    /// <item>Cursed Seal of the Forbidden Spell (c58851034): discard 1 Spell; negate a Spell Card activation, destroy it; if
    /// destroyed, the opponent cannot activate Spell Cards with that code for the rest of the Duel.</item>
    /// <item>Trap Jammer (c19252988): the opponent's Trap Card activation during the Battle Phase; negate, destroy.</item>
    /// <item>Sasuke Samurai #2 (c11760174): ignition; pay 800 LP; until the End Phase neither player can activate Spell/Trap
    /// Cards or their effects; a player flag (GetFlagEffect(tp, id)), not a per-copy count.</item>
    /// <item>Invader of Darkness (c56647086): the opponent cannot activate Quick-Play Spell Cards.</item>
    /// </list>
    /// Every atom is the card's WHOLE text (anchored ^…$). Runtime: <see cref="SpellTrapAnswersAndActivationLocksRuntime"/>.
    /// Deferred: Goblin of Greed (EFFECT_CANNOT_DISCARD_HAND: a player-wide "cannot discard as a cost" lock read by every
    /// discard-cost site; see the batch notes).
    /// </summary>
    public static class SpellTrapAnswersAndActivationLocksAtoms
    {
        const string Apos = "['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Judgment of Anubis (c55256016): ACTIVATE on EVENT_CHAINING; s.condition: tp ~= ep (the opponent's),
            // IsChainNegatable, re:IsSpellEffect() and IsHasType(EFFECT_TYPE_ACTIVATE), GetOperationInfo(ev, CATEGORY_DESTROY)
            // holds a Spell/Trap on the field. Cost: DiscardHand(IsDiscardable, 1). s.operation: NegateActivation, Destroy(eg)
            // while related; then the opponent's face-up monsters: SelectYesNo, Select 1 (no target), atk = GetAttack(),
            // Destroy; if > 0, Damage(1-tp, atk).
            new AtomRegistration.Atom(
                @"^When your opponent activates a Spell Card that would destroy a Spell/Trap Card\(s\) on the field: " +
                @"Discard 1 card; negate the activation, and if you do, destroy it, then you can destroy 1 face-up monster " +
                @"your opponent controls, and if you do that, inflict damage to your opponent equal to the destroyed " +
                @"monster" + Apos + @"s ATK on the field\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateOpponentActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRequiresWouldDestroySpellTrapOnField = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = 1,
                    DestroyNegatedCard = true,
                    AfterNegationAction = EffectActionKind.MayDestroyOpponentFaceUpMonsterInflictItsAtk,
                    MakesChainLink = true
                }),
            // Spell Vanishing (c29735721): ACTIVATE on EVENT_CHAINING; s.condition: re:IsSpellEffect() and
            // IsHasType(EFFECT_TYPE_ACTIVATE) and IsChainNegatable (either player's). Cost: DiscardHand(IsDiscardable, 2).
            // s.target chk: the opponent has a card in hand or Deck. s.activate: NegateActivation, Destroy(eg) while related;
            // then ConfirmCards(opponent's hand + Deck), SendtoGrave(every IsCode(rc:GetCode())), shuffle.
            new AtomRegistration.Atom(
                @"^Discard (\d+) cards from your hand\. Negate the activation of a Spell Card and destroy it\. Also, look at " +
                @"your opponent" + Apos + @"s hand and Deck and if you find any Spell Cards of the same name as the destroyed " +
                @"Spell Card, send all of them to the (?:Graveyard|GY)\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = "*",
                    DiscardCostCount = int.Parse(m.Groups[1].Value),
                    DestroyNegatedCard = true,
                    AfterNegationAction = EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy,
                    MakesChainLink = true
                }),
            // Cursed Seal of the Forbidden Spell (c58851034): ACTIVATE on EVENT_CHAINING; s.condition: re:IsSpellEffect() and
            // IsHasType(EFFECT_TYPE_ACTIVATE) and IsChainNegatable (either player's). Cost: DiscardHand(IsSpell and
            // IsDiscardable, 1). s.activate: if NegateActivation and related and Destroy(eg) ~= 0: EFFECT_CANNOT_ACTIVATE for
            // the opponent (target range 0,1), value IsHasType(EFFECT_TYPE_ACTIVATE) and IsCode(label), no reset.
            new AtomRegistration.Atom(
                @"^When a Spell Card is activated: Discard 1 Spell; negate the activation, and if you do, destroy it, and if " +
                @"you do that, your opponent cannot activate Spell Cards with that name for the rest of this Duel\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    RequiresDiscardCost = true,
                    DiscardCostAttribute = FaceUpQuickEffectsRuntime.DiscardSpellCard,
                    DiscardCostCount = 1,
                    DestroyNegatedCard = true,
                    AfterNegationAction = EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel,
                    MakesChainLink = true
                }),
            // Trap Jammer (c19252988): ACTIVATE on EVENT_CHAINING; s.condition: Duel.IsBattlePhase() and rp == 1-tp and
            // re:IsTrapEffect() and IsHasType(EFFECT_TYPE_ACTIVATE) and IsChainNegatable. s.activate: NegateActivation,
            // Destroy(eg) while related.
            new AtomRegistration.Atom(
                @"^When your opponent activates a Trap Card during the Battle Phase: Negate the activation, and if you do, " +
                @"destroy it\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateOpponentActivationOnly = true,
                    NegateRespondsToTrap = true,
                    RequiresBattlePhase = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // Sasuke Samurai #2 (c11760174): IGNITION (LOCATION_MZONE), Cost.PayLP(800); s.target chk GetFlagEffect(tp, id) == 0
            // (a player flag); s.operation: EFFECT_CANNOT_ACTIVATE, PLAYER_TARGET (1,1), value re:IsSpellTrapEffect(),
            // RESET_PHASE|PHASE_END; RegisterFlagEffect(tp, id, RESET_PHASE|PHASE_END).
            new AtomRegistration.Atom(
                @"^Once per turn, during your Main Phase, you can pay (\d+) Life Points\. If you do this, until the End " +
                @"Phase, Spell and Trap Cards cannot be activated\.$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn,
                    Side = EffectSide.Both,
                    PayLpAmount = int.Parse(m.Groups[1].Value),
                    ActivationCondition = SpellTrapAnswersAndActivationLocksRuntime.TurnLockNotAppliedByYouThisTurn,
                    IsOptional = true,
                    MakesChainLink = true
                }),
            // Invader of Darkness (c56647086): FIELD, PLAYER_TARGET (0,1), EFFECT_CANNOT_ACTIVATE, LOCATION_MZONE; value
            // re:GetHandler():IsQuickPlaySpell() and re:IsHasType(EFFECT_TYPE_ACTIVATE).
            new AtomRegistration.Atom(
                @"^Your opponent cannot activate Quick-Play Spell Cards\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells,
                    Side = EffectSide.Opponent,
                    MakesChainLink = false
                }),
        };
    }
}
