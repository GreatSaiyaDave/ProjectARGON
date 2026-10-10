namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SummonNegation" (v141): Counter Traps that negate a Summon or an activation. Each atom is
    /// one whole sentence (start of text or after a sentence end, through its final period), so a longer card
    /// that only contains part of it stays uncompiled.
    /// "When a monster(s) would be Summoned" answers the summon-negation window
    /// (<see cref="EffectTiming.MonsterWouldBeSummoned"/>, DuelEngine ResponseTiming.SummonNegation), which
    /// opens before a Normal / Tribute / Flip Summon or a Special Summon by procedure succeeds; the runtime is
    /// <see cref="SummonNegationRuntime"/>.
    /// </summary>
    public static class SummonNegationAtoms
    {
        /// <summary>Sentence start: beginning of the text, or right after ". ".</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>
        /// Sentence end: the final period followed by whitespace or the end of the text (or an unpunctuated end
        /// of text). A bare space does not end the sentence, so the words cannot match inside a longer sentence.
        /// </summary>
        const string E = @"(?:\.|$)(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Solemn Judgment (c41420027): one sentence, two effects. e1-e3 (EVENT_SUMMON / FLIP_SUMMON /
            // SPSUMMON, only outside a chain) negate the Summon and destroy the monster; e4 (EVENT_CHAINING,
            // re:IsHasType(EFFECT_TYPE_ACTIVATE)) negates the activation of any Spell/Trap Card, either
            // player's, and destroys it. Cost for both: pay half your LP (rounded down).
            AtomRegistration.Atom.Sequence(
                S + @"When a monster\(s\) would be Summoned, OR a Spell/Trap Card is activated: " +
                @"Pay half your (?:LP|Life Points); negate the Summon or activation, and if you do, " +
                @"destroy that card" + E,
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.MonsterWouldBeSummoned,
                        Action = EffectActionKind.NegateSummon,
                        PayHalfLpCost = true,
                        DestroyNegatedCard = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        ChainResponseOnly = true,
                        PayHalfLpCost = true,
                        DestroyNegatedCard = true,
                        NegateRespondsToSpell = true,
                        NegateRespondsToTrap = true,
                        NegateCardActivationOnly = true,
                        MakesChainLink = true
                    }
                }),
            // Horn of Heaven (c98069388): EVENT_SUMMON / FLIP_SUMMON / SPSUMMON, only outside a chain. Cost:
            // Tribute 1 monster you control (CheckReleaseGroupCost, any face-up or face-down monster).
            new AtomRegistration.Atom(
                S + @"When a monster\(s\) would be Summoned: Tribute 1 monster; negate the Summon, and if you do, " +
                @"destroy that monster\(s\)" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.MonsterWouldBeSummoned,
                    Action = EffectActionKind.NegateSummon,
                    RequiresTributeCount = 1,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
            // Magic Drain (c59344077): EVENT_CHAINING, rp~=tp, an activated Spell Card (not a Spell effect).
            // No cost. At resolution the Spell's controller may discard 1 Spell Card to negate Magic Drain's
            // effect; otherwise their Spell's activation is negated and the card destroyed.
            new AtomRegistration.Atom(
                S + @"When your opponent activates a Spell Card: They can discard 1 Spell Card to negate this card's " +
                @"effect, otherwise negate the activation of their Spell Card, and if you do, destroy it" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    DestroyNegatedCard = true,
                    NegateRespondsToSpell = true,
                    NegateOpponentActivationOnly = true,
                    NegateCardActivationOnly = true,
                    OpponentMayDiscardSpellToNegateThis = true,
                    MakesChainLink = true
                }),
        };
    }
}
