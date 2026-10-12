namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// TargetingTruth batch (v177) atoms. Every atom is the card's WHOLE text (anchored ^…$), so a longer or shorter
    /// look-alike never compiles a fragment. Runtime: <see cref="TargetingTruthRuntime"/>, NegationMonstersRuntime (Gora
    /// Turtle) and ChainLinkAnswersRuntime (Big Burn).
    /// </summary>
    public static class TargetingTruthAtoms
    {
        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Gora Turtle of Illusion (c42868711): e2 EVENT_CHAIN_SOLVING — rp~=tp, re:IsSpellTrapEffect() with
            // EFFECT_FLAG_CARD_TARGET whose CHAININFO_TARGET_CARDS contain this card: NegateEffect, then Destroy the card
            // while it is still related. e1 EFFECT_DISABLE + e3 EFFECT_SELF_DESTROY on the opponent's S/T cards that keep
            // targeting it (s.distg IsControler(1-tp) and IsHasCardTarget). Freed's marker, Spell OR Trap, opponent only
            // (Side = Opponent; NegationMonstersRuntime.TryNegateTargetingLink / SweepPersistentTargets).
            new AtomRegistration.Atom(
                @"^Negate the effects? of Spell and Trap Cards controlled by your opponent that target this face-up card " +
                @"on the field\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy,
                    Side = EffectSide.Opponent,
                    NegateRespondsToSpell = true,
                    NegateRespondsToTrap = true,
                    DestroyNegatedCard = true,
                    AffectsThisCardOnly = true,
                    IsPsctTarget = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
            // Big Burn (c95472621): EFFECT_TYPE_ACTIVATE on EVENT_BECOME_TARGET; s.condition rp~=tp and a target is a monster
            // in a GY (any effect of the opponent, not only a card activation); s.target a banishable SpElimFilter monster
            // in either Monster Zone / GY; s.activate banishes them all (ChainLinkAnswersRuntime answer).
            new AtomRegistration.Atom(
                @"^You can only activate this card when your opponent activates a card that targets a monster in the " +
                @"(?:Graveyard|GY)\. (?:Remove from play|Banish) all monsters in both players' (?:Graveyards|GYs)\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster,
                    Side = EffectSide.Both,
                    ChainResponseOnly = true,
                    NegateOpponentActivationOnly = true,
                    MakesChainLink = true
                }),
        };
    }
}
