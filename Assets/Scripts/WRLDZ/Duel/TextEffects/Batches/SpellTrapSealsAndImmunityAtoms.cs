using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpellTrapSealsAndImmunity": Spell/Trap activation locks and negation (Talisman of Spell Sealing, Talisman of
    /// Trap Sealing, Sonic Jammer), Normal Monsters unaffected by the opponent's Spells (Dust Barrier) and a Counter Trap
    /// against Equip Spells (Armor Break). Every atom is the card's WHOLE text (anchored ^…$), so a longer or reworded card
    /// stays uncompiled. Runtime: <see cref="SpellTrapSealsAndImmunityRuntime"/>, plus the ContinuousNegation sources for the
    /// Talismans' lock and negation.
    /// </summary>
    public static class SpellTrapSealsAndImmunityAtoms
    {
        const string Q = "[\"“”]";
        const string Apos = "['’]";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Talisman of Spell Sealing (c71983925, Continuous Trap) / Talisman of Trap Sealing (c19312169, Continuous Spell).
            // Lua: e1 ACTIVATE on EVENT_FREE_CHAIN, s.actcon: a face-up Sealmaster Meisei (2468169) in YOUR Monster Zone;
            // e2 EFFECT_SELF_DESTROY while you control no face-up Meisei in your Monster Zone; e3 EFFECT_CANNOT_TRIGGER
            // (SET_AVAILABLE) on Spells (Traps) in both hands and S/T Zones; e4 EFFECT_DISABLE on Spells (Traps) in both
            // S/T Zones; e5 EVENT_CHAIN_SOLVING negates a Spell (Trap) effect resolving from the S/T Zone; Trap Sealing's
            // e6 EFFECT_DISABLE_TRAPMONSTER. The lock and the negation are ContinuousNegation sources (the Spell Canceller /
            // Jinzo actions: hand + S/T activation lock, face-up negation incl. Trap Monsters, resolving-effect negation).
            AtomRegistration.Atom.Sequence(
                @"^You can only activate this card while there is a face-up " + Q + @"([^""“”]+)" + Q +
                @" on your side of the field\.\s*While this card is face-up on the field, (Spell|Trap) Cards cannot be " +
                @"activated and the effects of face-up \2 Cards on the field are negated\.\s*When " + Q + @"\1" + Q +
                @" is removed from the field, destroy this card\.$",
                m =>
                {
                    var name = m.Groups[1].Value.Trim();
                    var trap = string.Equals(m.Groups[2].Value, "Trap", System.StringComparison.OrdinalIgnoreCase);
                    return new[]
                    {
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = trap
                                ? EffectActionKind.ContinuousCannotActivateTraps
                                : EffectActionKind.ContinuousCannotActivateSpells,
                            Side = EffectSide.Both,
                            // s.actcon: the named monster face-up in your Monster Zone.
                            ActivationCondition = SimpleTriggersAndSwarmRuntime.AllNamedFaceUpInYourMonsterZones,
                            ConditionName = name,
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = trap
                                ? EffectActionKind.ContinuousNegateTrapEffectsOnField
                                : EffectActionKind.ContinuousNegateSpellEffectsOnField,
                            Side = EffectSide.Both,
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.SelfDestroyUnlessNamedFaceUp,
                            RequiresFaceUpName = name,
                            RequiresControllerNamedCard = true,
                            NamedFaceUpInYourMonsterZone = true,
                            StaysOnField = true,
                            MakesChainLink = false
                        },
                    };
                }),

            // Sonic Jammer (c84550200): Lua FLIP (no target) registers a field EFFECT_CANNOT_ACTIVATE for the opponent,
            // value re:IsHasType(EFFECT_TYPE_ACTIVATE) and re:IsSpellEffect() (the activation of a Spell Card), reset
            // RESET_PHASE|PHASE_END, 2 (through the next turn's End Phase).
            new AtomRegistration.Atom(
                @"^FLIP:\s*Your opponent cannot activate any Spell Cards until the end of the End Phase of the next turn\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.OpponentCannotActivateSpellCardsUntilEndOfNextTurn,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }),

            // Dust Barrier (c31476755, Continuous Spell): e2 field EFFECT_IMMUNE_EFFECT on both Monster Zones for TYPE_NORMAL,
            // value: effect owner player ~= this card's controller and re:IsSpellEffect(); s.target seeds a CANNOT_DISABLE
            // Standby Phase counter (your Standby Phases only) that destroys this card at 2.
            AtomRegistration.Atom.Sequence(
                @"^Face-up Normal Monsters on the field are unaffected by your opponent" + Apos + @"s Spell Cards\.\s*" +
                @"This card is destroyed during your (second|2nd) Standby Phase after activation\.$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.NormalMonstersUnaffectedByOpponentSpells,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyThisOnYourNthStandbyAfterActivation,
                        Side = EffectSide.Controller,
                        Amount = 2,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                }),

            // Armor Break (c79649195, Counter Trap): ACTIVATE on EVENT_CHAINING, s.condition: re:IsActiveType(TYPE_EQUIP) and
            // re:IsHasType(EFFECT_TYPE_ACTIVATE) and IsChainNegatable (either player's); s.activate: NegateActivation, then
            // Destroy(eg) while related. A Union's equip effect or a later effect of a face-up Equip Spell is not answered.
            new AtomRegistration.Atom(
                @"^Negate the activation of an Equip Spell Card and destroy it\.$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ChainLinkActivated,
                    Action = EffectActionKind.NegateActivation,
                    ChainResponseOnly = true,
                    NegateCardActivationOnly = true,
                    NegateRespondsToSpell = true,
                    NegateRespondsToEquipSpellOnly = true,
                    DestroyNegatedCard = true,
                    MakesChainLink = true
                }),
        };
    }
}
