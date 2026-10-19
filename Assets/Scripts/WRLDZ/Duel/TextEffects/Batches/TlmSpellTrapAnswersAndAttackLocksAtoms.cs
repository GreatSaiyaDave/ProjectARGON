namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "TlmSpellTrapAnswersAndAttackLocks" (v211, TLM): whether a Spell/Trap Card activation is legal or answered —
    /// Counter Traps against a Continuous Spell/Trap activation (Royal Surrender, Spell-Stopping Statute), Mid Shield Gardna's
    /// answer to a Spell that targets it face-down, and the Ancient Gear lock on Spell/Trap Cards while it attacks (Ancient Gear
    /// Soldier, Golem, Beast) with Ancient Gear Beast's battle negation. Each atom is a whole sentence (or the card's whole
    /// text), so a longer or reworded card stays uncompiled. Runtime: <see cref="TlmSpellTrapAnswersAndAttackLocksRuntime"/>.
    /// </summary>
    public static class TlmSpellTrapAnswersAndAttackLocksAtoms
    {
        /// <summary>Sentence start: the text start or right after a sentence end.</summary>
        const string S = @"(?<=^|\.\s)";

        /// <summary>Sentence end: the period, then a space or the text end.</summary>
        const string E = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Royal Surrender (c56058888) / Spell-Stopping Statute (c10069180), Counter Trap: ACTIVATE on EVENT_CHAINING,
            // s.condition: rp~=tp and re:IsHasType(EFFECT_TYPE_ACTIVATE) and re:GetActiveType()==TYPE_TRAP+TYPE_CONTINUOUS
            // (TYPE_SPELL+TYPE_CONTINUOUS) and Duel.IsChainNegatable(ev); s.activate: NegateActivation, then Destroy(eg) while
            // related. The card's whole text; a later effect of a face-up Continuous card is not a card activation.
            new AtomRegistration.Atom(
                @"^Activate only when your opponent activates a Continuous (Trap|Spell) Card\.\s*Negate the activation and " +
                @"the effect of the card and destroy it\.$",
                m =>
                {
                    var trap = string.Equals(m.Groups[1].Value, "Trap", System.StringComparison.OrdinalIgnoreCase);
                    return new EffectClause
                    {
                        Timing = EffectTiming.ChainLinkActivated,
                        Action = EffectActionKind.NegateActivation,
                        ChainResponseOnly = true,
                        NegateCardActivationOnly = true,
                        NegateOpponentActivationOnly = true,
                        NegateRespondsToSpell = !trap,
                        NegateRespondsToTrap = trap,
                        NegateRespondsToContinuousOnly = true,
                        DestroyNegatedCard = true,
                        MakesChainLink = true
                    };
                }),

            // Mid Shield Gardna (c75487237), sentences 2-3: e2 QUICK_F on EVENT_CHAINING (DAMAGE_STEP, DAMAGE_CAL,
            // SET_AVAILABLE), s.negcon: re:IsSpellEffect() and re:IsHasType(EFFECT_TYPE_ACTIVATE) and CARD_TARGET, the chain's
            // targets are exactly this card and it is face-down; s.negop: NegateActivation, SendtoGrave(eg) while related, then
            // ChangePosition(this, POS_FACEUP_DEFENSE). Sentence 1 (the once-per-turn turn-set) compiles on its own.
            new AtomRegistration.Atom(
                S + @"Negate the activation of any Spell Card that targets this 1 face-down monster\.\s*At that time, flip " +
                @"this card into face-up Defense Position" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    FlipSelfFaceUpDefense = true,
                    SendNegatedCardToGyThenFlipSelf = true,
                    MakesChainLink = true
                }),

            // Ancient Gear Soldier (c56094445) / Golem (c83104731) / Beast (c10509340): FIELD EFFECT_CANNOT_ACTIVATE,
            // LOCATION_MZONE, PLAYER_TARGET (0,1), aclimit re:IsHasType(EFFECT_TYPE_ACTIVATE), actcon
            // Duel.GetAttacker()==e:GetHandler().
            new AtomRegistration.Atom(
                S + @"If this card attacks, your opponent cannot activate any Spell/Trap Cards until the end of the Damage " +
                @"Step" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousOpponentCannotActivateSpellTrapCardsWhileThisAttacks,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Ancient Gear Beast (c10509340), sentence 3: e2 SINGLE+CONTINUOUS EVENT_BATTLED — the other battler is
            // battle-destroyed and this card is not → EFFECT_DISABLE + EFFECT_DISABLE_EFFECT with RESETS_STANDARD_EXC_GRAVE
            // (the Twin-Headed Wolf / Ultimate Obedient Fiend runtime, any card kind).
            new AtomRegistration.Atom(
                S + @"Negate the effects of an opponent['’]s monster destroyed by battle with this card \(including in the " +
                @"Graveyard\)" + E,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                    Side = EffectSide.Controller,
                    AffectsThisCardOnly = true,
                    RequiresNotDestroyedByBattle = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };
    }
}
