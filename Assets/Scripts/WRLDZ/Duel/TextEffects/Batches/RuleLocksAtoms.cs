namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "RuleLocks": continuous rule locks enforced at the DuelEngine entry points and in the legality checks
    /// (LegalIntentService and the AI read the same checks). Each atom is the card's whole text (every sentence,
    /// anchored at both ends), so a longer or reworded card stays uncompiled. Runtime: <see cref="RuleLocksRuntime"/>.
    /// </summary>
    public static class RuleLocksAtoms
    {
        /// <summary>Jam Breeding Machine: the rest of the text after its Standby sentence is exactly its summon lock.</summary>
        const string JamLockAhead =
            @"(?=\s*You cannot Summon any monsters, except ""[^""]+"" \(but you can Set\)\.?\s*$)";

        /// <summary>Jam Breeding Machine: the text before its summon lock is exactly its Standby Token sentence.</summary>
        const string JamTokenBehind =
            @"(?<=^\s*During each of your Standby Phases: Special Summon 1 ""[^""]+ Token"" \([A-Za-z]+-Type/[A-Z]+/" +
            @"LEVEL \d+/ATK \d+/DEF \d+\) in Attack Position\.\s*)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Jowgen the Spiritualist (41855169). ygopro: e2 EFFECT_TYPE_IGNITION (no count limit), cost = discard 1
            // random card from the hand (every hand card must be discardable), target check = 1+ Special Summoned
            // monster on the field, operation = destroy every Special Summoned monster on the field (itself included);
            // e1 EFFECT_CANNOT_SPECIAL_SUMMON, target range (1,1), while it is in the Monster Zone.
            AtomRegistration.Atom.Sequence(
                @"^\s*You can discard 1 random card from your hand to the (?:Graveyard|GY); " +
                @"destroy all Special Summoned monsters on the field\.\s*" +
                @"Neither player can Special Summon monsters\.?\s*$",
                _ => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.DestroySpecialSummonedMonsters,
                        Side = EffectSide.Both,
                        RequiresDiscardCost = true,
                        DiscardCostAttribute = "*",
                        DiscardCostRandom = true,
                        ActivationCondition = RuleLocksRuntime.SpecialSummonedMonsterOnField,
                        CheckedAt = ConditionCheckedAt.Activation,
                        IsOptional = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.NeitherPlayerCanSpecialSummon,
                        Side = EffectSide.Both,
                        MakesChainLink = false
                    }
                }),

            // Jam Breeding Machine (21770260). ygopro: e2 EFFECT_TYPE_FIELD+TRIGGER_F, EVENT_PHASE+PHASE_STANDBY,
            // condition IsTurnPlayer(tp): Special Summon 1 Slime Token (Aqua/WATER/1/500/500) in face-up Attack
            // Position if a zone is free; e3 EFFECT_CANNOT_SPECIAL_SUMMON (except TOKEN_SLIME), e4 EFFECT_CANNOT_SUMMON,
            // e5 EFFECT_CANNOT_FLIP_SUMMON, all target range (1,0). Setting is not restricted.
            // Two atoms, each anchored to the whole text by a lookaround, so the mandatory Standby trigger keeps its own
            // snippet (the compiler marks a clause optional when its snippet says "you can").
            new AtomRegistration.Atom(
                @"^\s*During each of your Standby Phases: Special Summon 1 ""(?<tok>[^""]+ Token)"" " +
                @"\((?<race>[A-Za-z]+)-Type/(?<attr>[A-Z]+)/LEVEL (?<lv>\d+)/ATK (?<atk>\d+)/DEF (?<def>\d+)\) " +
                @"in Attack Position\." + JamLockAhead,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.SpecialSummonToken,
                    Side = EffectSide.Controller,
                    TokenName = m.Groups["tok"].Value,
                    TokenRace = m.Groups["race"].Value,
                    TokenAttribute = m.Groups["attr"].Value,
                    TokenLevel = int.Parse(m.Groups["lv"].Value),
                    TokenAtk = int.Parse(m.Groups["atk"].Value),
                    TokenDef = int.Parse(m.Groups["def"].Value),
                    TokenCount = 1,
                    IsOptional = false,
                    StaysOnField = true,
                    MakesChainLink = true
                }),
            new AtomRegistration.Atom(
                JamTokenBehind + @"You cannot Summon any monsters, except ""(?<ex>[^""]+?)s?"" \(but you can Set\)\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ControllerCannotSummonExceptNamed,
                    NamedCard = m.Groups["ex"].Value,
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Kycoo the Ghost Destroyer (88240808). ygopro: e1 SINGLE+TRIGGER_O, EVENT_BATTLE_DAMAGE (ep ~= tp),
            // EFFECT_FLAG_CARD_TARGET + DAMAGE_STEP: target 1-2 monsters in the opponent's GY, banish those still there;
            // e2 EFFECT_CANNOT_REMOVE, target range (0,1), GY cards — the opponent cannot banish cards from either GY
            // (costs included).
            AtomRegistration.Atom.Sequence(
                @"^\s*When this card inflicts battle damage to your opponent: You can target up to (?<n>\d+) monsters " +
                @"in their Graveyard; banish those targets\.\s*" +
                @"Your opponent cannot banish cards from either player's Graveyard\.?\s*$",
                m => new[]
                {
                    new EffectClause
                    {
                        Timing = EffectTiming.ThisCardInflictsBattleDamage,
                        Action = EffectActionKind.Banish,
                        Zone = EffectZoneFilter.OppGyMonsters,
                        Side = EffectSide.Opponent,
                        // "up to N" (Soul Release / Gravedigger Ghoul shape: Banish with Amount = N targets).
                        Amount = int.Parse(m.Groups["n"].Value),
                        RequiresTargetChoice = true,
                        IsPsctTarget = true,
                        IsOptional = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.OpponentCannotBanishFromGraveyards,
                        Side = EffectSide.Opponent,
                        MakesChainLink = false
                    }
                }),

            // Royal Command (33950246). ygopro: e2 FIELD+CONTINUOUS EVENT_CHAIN_ACTIVATING in the S/T Zone —
            // if re:IsActiveType(TYPE_FLIP) then NegateActivation. Continuous effects of Flip monsters still apply.
            new AtomRegistration.Atom(
                @"^\s*Flip monsters' effects cannot be activated, also their effects are negated\.?\s*$",
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateFlipMonsterEffectActivations,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Chain Energy (79323590). ygopro: e1 ACTIVATE (free chain, no effect); e2 EFFECT_ACTIVATE_COST (player target
            // 1,1; the activated effect's handler is in the hand), e3 SUMMON_COST, e4 SPSUMMON_COST, e5 MSET_COST,
            // e6 SSET_COST (target range hand, both players) — costchk CheckLPCost(ct × 500), ct = copies applying;
            // each copy's costop pays 500. A Flip Summon and anything done from the field or another zone cost nothing.
            new AtomRegistration.Atom(
                @"^\s*Each player must pay (?<lp>\d+) Life Points per card to Normal Summon, Special Summon, Set or " +
                @"activate cards from his/her respective hand\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.HandActionsCostLp,
                    Amount = int.Parse(m.Groups["lp"].Value),
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }),
        };
    }
}
