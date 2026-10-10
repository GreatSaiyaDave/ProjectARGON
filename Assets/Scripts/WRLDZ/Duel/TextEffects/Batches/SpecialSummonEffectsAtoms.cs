namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "SpecialSummonEffects" (v161, LOD): whole-text atoms for Skull Knight #2, Twin-Headed Behemoth,
    /// Winged Minion, The Puppet Magic of Dark Ruler and Gradius' Option. Each pattern is anchored to the start and
    /// the end of the whole printed text, so only a card whose entire text is that wording compiles here; a longer or
    /// reworded text stays uncompiled (fail closed). Runtime: <see cref="SpecialSummonEffectsRuntime"/>.
    /// </summary>
    public static class SpecialSummonEffectsAtoms
    {
        /// <summary>The whole text starts here.</summary>
        const string Begin = @"^\s*";

        /// <summary>The whole text ends here: the final period, then only whitespace.</summary>
        const string Finish = @"\.\s*$";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Skull Knight #2 (c15653824): SINGLE + TRIGGER_F on EVENT_BE_MATERIAL, r == REASON_SUMMON, the Tribute
            // Summoned monster (GetReasonCard) face-up and of the Type. Duel.SpecialSummon of the first copy of the
            // named card in the Deck (POS_FACEUP), nothing when no Monster Zone is free. A marker clause: the
            // Tribute Summon completion (DuelEngine.CompleteNormalSummon) resolves it through the runtime.
            new AtomRegistration.Atom(
                Begin + @"If you Tribute Summon a (\w+)-Type monster by Tributing this monster, Special Summon 1 " +
                @"""([^""]+)"" from your Deck\.\s*Then shuffle your Deck" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonNamedFromDeckWhenTributedForRaceSummon,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value,
                    NamedCard = m.Groups[2].Value,
                    FromDeck = true,
                    MakesChainLink = true
                }),

            // Twin-Headed Behemoth (c43586926): a flag set on EVENT_TO_GRAVE when destroyed (REASON_DESTROY) from the
            // field registers a FIELD + TRIGGER_O on EVENT_PHASE+PHASE_END in the GY until the end of that turn
            // (either player's End Phase), SetCountLimit(1, id, EFFECT_COUNT_CODE_DUEL). Duel.SpecialSummon itself,
            // then EFFECT_SET_ATTACK / EFFECT_SET_DEFENSE (RESET_EVENT | RESETS_STANDARD_DISABLE).
            new AtomRegistration.Atom(
                Begin + @"During the End Phase, if this card is in the GY because it was destroyed on the field and " +
                @"sent there this turn: You can Special Summon this card, but its ATK/DEF become (\d+)\.\s*" +
                @"You can only use this effect of ""([^""]+)"" once per Duel" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn,
                    Side = EffectSide.Controller,
                    Amount = Parse(m, 1, 1000),
                    DefAmount = Parse(m, 1, 1000),
                    NamedCard = m.Groups[2].Value,
                    ResolvesFromGy = true,
                    IsOptional = true,
                    OncePerDuel = true,
                    MakesChainLink = true
                }),

            // Winged Minion (c89258225): IGNITION in the Monster Zone, Cost.SelfTribute, EFFECT_FLAG_CARD_TARGET on 1
            // face-up monster of the Type on either field (other than this card), EFFECT_UPDATE_ATTACK / _DEFENSE
            // (RESET_EVENT | RESETS_STANDARD) if it is still a face-up monster of that Type on resolution.
            new AtomRegistration.Atom(
                Begin + @"Tribute this face-up card\.\s*Select 1 face-up (\w+)-Type monster on the field\.\s*" +
                @"Increase the ATK and DEF of the monster by (\d+) points as long as it remains face-up on the field" +
                Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetGainsAtkDefWhileFaceUp,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    RaceFilter = m.Groups[1].Value,
                    Amount = Parse(m, 2, 700),
                    DefAmount = Parse(m, 2, 700),
                    RequiresTributeThis = true,
                    RequiresTargetChoice = true,
                    RequiresTargetFaceUp = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),

            // The Puppet Magic of Dark Ruler (c40703393): ACTIVATE, EFFECT_FLAG_CARD_TARGET on 1 monster of the Type in
            // your GY with Level > 0 that can be Special Summoned and whose Level the Levels of monsters you control
            // (Level > 0, IsAbleToRemove) can total exactly (CheckWithSumEqual). On resolution: the target must still be
            // in the GY; select such monsters (SelectWithSumEqual), banish them face-up, then Special Summon the target.
            new AtomRegistration.Atom(
                Begin + @"Select 1 (\w+)-Type monster from your Graveyard\.\s*Remove from play monsters on your side " +
                @"of the field whose total Levels exactly equal the Level of the selected \1-Type monster\.\s*" +
                @"Then Special Summon the selected monster from your Graveyard" + Finish,
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.BanishLevelSumThenSpecialSummonTarget,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    Side = EffectSide.Controller,
                    RaceFilter = m.Groups[1].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),

            // Gradius' Option (c14291024): EnableReviveLimit; EFFECT_SPSUMMON_PROC from the hand (a free Monster Zone and
            // a face-up "Gradius" in your Monster Zone, no Tribute); on EVENT_SPSUMMON_SUCCESS it selects 1 such
            // "Gradius" (SetCardTarget); EFFECT_SET_ATTACK_FINAL / _DEFENSE_FINAL = that monster's ATK/DEF; EFFECT_SELF_DESTROY
            // once that target is gone. The printed "can only be Special Summoned by selecting …" is read as its own
            // procedure only (DuelEngine.SpecialSummonToField, SpecialSummonLegality's own-way-only Nomi). "This card
            // cannot be Normal Summoned or Set." stays with the structural summon lock (PsctGrammar.BlocksNormalSummonOrSet).
            AtomRegistration.Atom.Sequence(
                Begin + @"This card cannot be Normal Summoned or Set\.\s*This monster can only be Special Summoned by " +
                @"selecting 1 face-up ""([^""]+)"" on your side of the field\.\s*The ATK and DEF of this card becomes the " +
                @"same as the ATK and DEF of the selected ""\1""\.\s*If the selected ""\1"" is removed from the field, " +
                @"destroy this card" + Finish,
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.SpecialSummonFromHandProcedure,
                        Amount = 0,
                        RequiresFaceUpName = m.Groups[1].Value,
                        RequiresControllerNamedCard = true,
                        FromHand = true,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.AtkDefBecomeSelectedNamed,
                        NamedCard = m.Groups[1].Value,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.DestroyThisWhenSelectedNamedLeavesField,
                        NamedCard = m.Groups[1].Value,
                        Side = EffectSide.Controller,
                        MakesChainLink = false
                    }
                }),
        };

        static int Parse(System.Text.RegularExpressions.Match m, int group, int fallback) =>
            m != null && m.Success && m.Groups.Count > group && int.TryParse(m.Groups[group].Value, out var n)
                ? n
                : fallback;
    }
}
