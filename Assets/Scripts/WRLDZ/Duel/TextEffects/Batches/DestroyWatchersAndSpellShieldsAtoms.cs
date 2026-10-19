namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "DestroyWatchersAndSpellShields" atoms (runtime: <see cref="DestroyWatchersAndSpellShieldsRuntime"/>).
    /// Every atom is a whole sentence (or the whole card text), anchored, so a longer look-alike stays unparsed.
    /// <list type="bullet">
    /// <item>Firebird (c87473172) / Maji-Gire Panda (c60102563): FIELD TRIGGER_F on EVENT_DESTROYED (a monster that was
    /// face-up in a Monster Zone with that Type on the field; Firebird: one its controller controlled) → this card gains
    /// N ATK (RESET_EVENT|RESETS_STANDARD).</item>
    /// <item>Mokey Mokey Smackdown (c1965724): FIELD+CONTINUOUS on EVENT_DESTROYED (a face-up Fairy you controlled, while you
    /// control a face-up "Mokey Mokey") flags the card until the End Phase; while flagged, EFFECT_SET_ATTACK 3000 on each
    /// face-up "Mokey Mokey" you control.</item>
    /// <item>Pole Position (c73578229): EVENT_ADJUST highest-ATK group + EFFECT_IMMUNE_EFFECT vs Spell effects; EVENT_LEAVE_FIELD
    /// destroys the highest-ATK face-up monster(s) unless it was negated / not yet applying when it left.</item>
    /// <item>Blast Magician (c21051146), sentence 2: IGNITION, CARD_TARGET 1 face-up monster on the field; cost: remove
    /// ceil(its ATK / 700) Spell Counters (min. 1) from this card; destroy it if it is still related. Its sentence 1 is the
    /// shared AdvancedEffectTemplates RxPlaceOnSpell counter clause.</item>
    /// </list>
    /// </summary>
    public static class DestroyWatchersAndSpellShieldsAtoms
    {
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        const string End = @"\.?(?=\s|$)";

        /// <summary>One printed Monster Type (old "-Type" print). Longer names first ("Winged Beast" before "Beast").</summary>
        const string Type =
            @"(Aqua|Beast-Warrior|Winged Beast|Divine-Beast|Beast|Cyberse|Dinosaur|Dragon|Fairy|Fiend|Fish|Insect|Machine|" +
            @"Plant|Psychic|Pyro|Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Firebird ("on your side of the field") / Maji-Gire Panda ("on the field"): the whole card text.
            new AtomRegistration.Atom(
                @"^\s*Each time an? " + Type + @"-Type monster\(s\) on (your side of the field|the field) is destroyed, " +
                @"this card gains (\d+) ATK\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainSelfAtkEachTimeRaceMonsterDestroyed,
                    Side = m.Groups[2].Value.StartsWith("your", System.StringComparison.OrdinalIgnoreCase)
                        ? EffectSide.Controller
                        : EffectSide.Both,
                    RaceFilter = m.Groups[1].Value,
                    Amount = int.Parse(m.Groups[3].Value),
                    MakesChainLink = false
                }),

            // Mokey Mokey Smackdown: the whole card text (the "while" name and the "each" name are the same card).
            new AtomRegistration.Atom(
                @"^\s*While a face-up ""([^""]+)"" is on your side of the field, if an? " + Type + @"-Type monster on your " +
                @"side of the field is destroyed, the ATK of each ""\1"" on your side of the field becomes (\d+) until the " +
                @"end of this turn\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NamedMonstersAtkBecomeUntilEndOfTurnWhenRaceMonsterDestroyed,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    RaceFilter = m.Groups[2].Value,
                    Amount = int.Parse(m.Groups[3].Value),
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Pole Position: the whole card text (its leave sentence names the card itself).
            AtomRegistration.Atom.Sequence(
                @"^\s*The face-up monster\(s\) with the highest ATK on the field (?:is|are) unaffected by the effects of " +
                @"Spell Cards\.\s*When ""Pole Position"" is removed from the field, destroy the face-up monster\(s\) with " +
                @"the highest ATK on the field\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.HighestAtkMonstersUnaffectedBySpells,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyHighestAtkMonstersWhenThisLeavesField,
                        Side = EffectSide.Both,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                }),

            // Blast Magician, sentence 2 (sentence 1 is the shared Spell Counter clause).
            new AtomRegistration.Atom(
                Start + @"You can remove any number of Spell Counters from this card, then target 1 face-up monster on the " +
                @"field with ATK less than or equal to the number of counters you removed [x×] (\d+); destroy that target" +
                End,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    // Lua s.destg passes nil: this card itself is a legal target.
                    TargetMayBeThisCard = true,
                    SpellCountersPerTargetAtk = int.Parse(m.Groups[1].Value),
                    IsOptional = true,
                    MakesChainLink = true
                }),
        };
    }
}
