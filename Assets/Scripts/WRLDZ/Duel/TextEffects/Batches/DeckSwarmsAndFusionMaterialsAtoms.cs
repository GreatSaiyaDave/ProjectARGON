namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// DeckSwarmsAndFusionMaterials batch: Special Summons from the Deck (Hyena's any number of copies, Rescue Cat's 2
    /// negated Beasts), the next-Standby GY revival of Sacred Phoenix of Nephthys, Centrifugal Field's listed-material
    /// revival and the Hex-Sealed Fusions' Tribute-the-materials summon. Each atom is anchored to whole sentences (Hex-Sealed:
    /// its second sentence; the first is <see cref="FusionSubstitutesAtoms"/>). Runtime:
    /// <see cref="DeckSwarmsAndFusionMaterialsRuntime"/>.
    /// </summary>
    public static class DeckSwarmsAndFusionMaterialsAtoms
    {
        const string Gy = @"(?:GY|Graveyard)";
        const string Q = @"[""“”]";

        /// <summary>The start of a sentence of the card text (the text start, or after a full stop).</summary>
        const string SentenceStart = @"(?<=^\s*|\.\s*)";

        /// <summary>The end of a sentence of the card text (whitespace or the text end follows).</summary>
        const string SentenceEnd = @"(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Hyena (c22873798): SINGLE TRIGGER_O on EVENT_BATTLE_DESTROYED, condition IsLocation(GRAVE) and
            // IsReason(REASON_BATTLE); chk: a free Monster Zone and a copy (IsCode(id)) in the Deck; SelectMatchingCard
            // 1..ft, SpecialSummon(g, 0, POS_FACEUP). The printed name is the plural ("Hyenas"): the runtime reads it as the
            // card's own name (<see cref="DeckSwarmsAndFusionMaterialsRuntime.SwarmName"/>).
            new AtomRegistration.Atom(
                @"^\s*When this card is destroyed by battle and sent to the " + Gy + @":\s*You can Special Summon any " +
                @"number of " + Q + @"([^""“”]+)" + Q + @" from your Deck\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.SpecialSummonAnyNumberOfOwnNameFromDeck,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value.Trim(),
                    FromDeck = true,
                    RequiresThisDestroyedByBattle = true,
                    IsOptional = true,
                    CheckedAt = ConditionCheckedAt.Activation,
                    MakesChainLink = true
                }),

            // Rescue Cat (c14878871): IGNITION in the Monster Zone, SetCountLimit(1,id), cost Cost.SelfToGrave; chk:
            // GetMZoneCount(tp,c) > 1 and 2 Level 3 or lower Beasts in the Deck that can be Special Summoned; spop: fewer
            // than 2 zones → nothing; exactly 2 (SelectMatchingCard 2,2), SpecialSummonStep POS_FACEUP, each with
            // EFFECT_DISABLE + EFFECT_DISABLE_EFFECT (RESETS_STANDARD) and a flag fid; a PHASE_END continuous effect destroys
            // the flagged ones still on the field.
            new AtomRegistration.Atom(
                @"^\s*You can send this card to the " + Gy + @"; Special Summon (\d+) Level (\d+) or lower " +
                @"([A-Za-z-]+(?: [A-Za-z-]+)?) monsters from your Deck, but their effects are negated, also they are " +
                @"destroyed during the End Phase\.\s*You can only use this effect of " + Q + @"([^""“”]+)" + Q +
                @" once per turn\.?\s*$",
                m =>
                {
                    if (!int.TryParse(m.Groups[1].Value, out var n) || n < 1) return null;
                    if (!int.TryParse(m.Groups[2].Value, out var lv) || lv < 1) return null;
                    return new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.SpecialSummonRaceFromDeckEffectsNegatedDestroyAtEndPhase,
                        Side = EffectSide.Controller,
                        Amount = n,
                        SummonLevelMax = lv,
                        RaceFilter = m.Groups[3].Value.Trim(),
                        FromDeck = true,
                        RequiresSendThisToGy = true,
                        IsOptional = true,
                        OncePerTurn = true,
                        OptScope = OncePerTurnScope.PerCardName,
                        NamedCard = m.Groups[4].Value.Trim(),
                        ActivationCondition = DeckSwarmsAndFusionMaterialsRuntime.ZonesAndDeckMonstersForPair,
                        MakesChainLink = true
                    };
                }),

            // Sacred Phoenix of Nephthys (c61441708): e1 SINGLE CONTINUOUS EVENT_TO_GRAVE (CANNOT_DISABLE) arms a flag when
            // the reason is REASON_DESTROY|REASON_EFFECT (either player's card), kept to the next Standby Phase of its
            // controller (during its own Standby: the following one, label turn); e2 FIELD TRIGGER_F in the GY on
            // PHASE_STANDBY: Special Summon this card (SUMMON_TYPE_SPECIAL+1, POS_FACEUP); e3 SINGLE TRIGGER_F on that
            // EVENT_SPSUMMON_SUCCESS: destroy every Spell/Trap on the field (one Destroy).
            new AtomRegistration.Atom(
                @"^\s*Once per turn, during your next Standby Phase after this card was destroyed by a card effect and " +
                @"sent to the " + Gy + @":\s*Special Summon this card from the " + Gy + @"\.\s*If you do:\s*" +
                @"Destroy all Spells and Traps on the field\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonThisFromGyNextStandbyAfterEffectDestroyThenDestroyAllSpellTraps,
                    Side = EffectSide.Controller,
                    ResolvesFromGy = true,
                    OncePerTurn = true,
                    MakesChainLink = true
                }),

            // Centrifugal Field (c1801154): a Field Spell (ACTIVATE free chain); a global EVENT_TO_GRAVE check raises one
            // custom event per Fusion Monster destroyed by a card effect (REASON_DESTROY + REASON_EFFECT), event player =
            // that Fusion's owner; e2 FIELD TRIGGER_F in the Field Zone, CARD_TARGET + EVENT_PLAYER: that player targets 1
            // monster in their GY whose code the Fusion lists as a material and Special Summons it (POS_FACEUP).
            new AtomRegistration.Atom(
                @"^\s*When a Fusion Monster\(s\) is destroyed by a card effect and sent to the " + Gy + @":\s*" +
                @"Its owner targets 1 Fusion Material Monster in their " + Gy + @" that is listed on that Fusion " +
                @"Monster; that player Special Summons that target\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.OwnerSpecialSummonsListedMaterialWhenFusionDestroyedByEffect,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // The Light / Dark / Earth - Hex-Sealed Fusion (c15717011 / c52101615 / c88696724), second sentence: IGNITION in
            // the Monster Zone; cost: Release a material set (SelectFusionMaterial with gc = this card) for 1 Fusion Monster
            // of the Attribute in the Extra Deck (IsCanBeSpecialSummoned(e,0,…)); operation: Special Summon that Fusion
            // (summon type 0, POS_FACEUP) — not a Fusion Summon.
            new AtomRegistration.Atom(
                SentenceStart + @"You can Tribute Fusion Material Monsters on the field, including this face-up card; " +
                @"Special Summon 1 corresponding (LIGHT|DARK|EARTH|WATER|FIRE|WIND) Fusion Monster from your Extra " +
                @"Deck\." + SentenceEnd,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TributeMaterialsSpecialSummonCorrespondingAttributeFusion,
                    Side = EffectSide.Controller,
                    AttributeFilter = m.Groups[1].Value.ToUpperInvariant(),
                    IsOptional = true,
                    ActivationCondition = DeckSwarmsAndFusionMaterialsRuntime.AttributeFusionMaterialsOnField,
                    MakesChainLink = true
                }),
        };
    }
}
