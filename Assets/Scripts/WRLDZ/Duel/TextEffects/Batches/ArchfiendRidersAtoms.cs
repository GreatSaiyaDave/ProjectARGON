namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Card batch "ArchfiendRiders": the sentences the Archfiend monsters (Dark Crisis) print after the Standby
    /// maintenance and the die-roll targeting protection (both compiled by <see cref="ArchfiendTemplates"/> /
    /// PhaseTriggerTemplates), plus Pandemonium, Pandemonium Watchbear and Checkmate. Every regex is one whole sentence
    /// (Checkmate: the whole text). Runtime: <see cref="ArchfiendRidersRuntime"/>.
    /// </summary>
    public static class ArchfiendRidersAtoms
    {
        /// <summary>Sentence start: start of text or after a sentence end.</summary>
        const string Start = @"(?:^|(?<=[.!?)]\s))";
        /// <summary>Sentence end: a period followed by whitespace or the end of the text.</summary>
        const string End = @"\.(?=\s|$)";

        public static readonly AtomRegistration.Atom[] Atoms =
        {
            // Shadowknight Archfiend (c9603356 e3): SINGLE EFFECT_CHANGE_BATTLE_DAMAGE, aux.ChangeBattleDamage(1,
            // HALF_DAMAGE) — only the battle damage it inflicts to its controller's opponent (Susa Soldier's marker).
            new AtomRegistration.Atom(
                Start + @"The Battle Damage this card inflicts to your opponent is halved" + End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.HalveBattleDamageThisInflictsToOpponent,
                    Side = EffectSide.Opponent,
                    MakesChainLink = false
                }),

            // Terrorking Archfiend (c35975813 e1/e2): SINGLE EFFECT_CANNOT_SUMMON + EFFECT_CANNOT_FLIP_SUMMON while its
            // controller controls no face-up "Archfiend" monster (s.excon). A Set stays legal.
            new AtomRegistration.Atom(
                Start + @"This card cannot be Normal Summoned or Flip Summoned unless you have an? ([A-Za-z]+) " +
                @"Monster Card on your side of the field" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),

            // Terrorking Archfiend (c35975813 e5): SINGLE+CONTINUOUS EVENT_BATTLED — its battle target, an Effect Monster
            // destroyed by that battle, gets EFFECT_DISABLE + EFFECT_DISABLE_EFFECT (RESETS_STANDARD_EXC_GRAVE). The
            // Ultimate Obedient Fiend shape (NegationMonstersRuntime.BattleNegator, this card, card kind "Effect").
            new AtomRegistration.Atom(
                Start + @"The effects of an Effect Monster that this card destroyed as a result of battle are negated" +
                End,
                _ => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle,
                    Side = EffectSide.Controller,
                    AffectsThisCardOnly = true,
                    TargetCardKind = "Effect",
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Vilepawn Archfiend (c73219648 e3): FIELD EFFECT_CANNOT_SELECT_BATTLE_TARGET for the opponent's monsters,
            // value s.atktg: a face-up "Archfiend" monster except a card named "Vilepawn Archfiend" (not IsCode(id)).
            // ContinuousProtections.CannotBeAttackTarget (the Marauding Captain "except" path, series filter on
            // NamedCard, the name exception on ExceptNamedCard).
            new AtomRegistration.Atom(
                Start + @"As long as this card remains on the field, your opponent cannot attack any ([A-Za-z]+) " +
                @"Monster Card on your side of the field except ""([^""]+)""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.CannotBeAttackTarget,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    ExceptThisCard = true,
                    ExceptNamedCard = m.Groups[2].Value,
                    MakesChainLink = false
                }),

            // Infernalqueen Archfiend (c8581705 e3): FIELD+TRIGGER_F on EVENT_PHASE+PHASE_STANDBY (every Standby Phase,
            // count limit 1), EFFECT_FLAG_CARD_TARGET: 1 face-up "Archfiend" monster its controller controls gets +1000
            // ATK (RESETS_STANDARD_PHASE_END). ArchfiendRidersRuntime.TryStandbyTargetBoost.
            new AtomRegistration.Atom(
                Start + @"As long as this card remains on the field, during each Standby Phase, increase the ATK of 1 " +
                @"([A-Za-z]+) Monster Card by (\d+) points until the End Phase" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainAtkDefUntilEndOfTurn,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    TargetSeriesName = m.Groups[1].Value,
                    Amount = int.TryParse(m.Groups[2].Value, out var atk) ? atk : 1000,
                    EachStandbyPhase = true,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }),

            // Checkmate (c69313735): ACTIVATE, EFFECT_FLAG_CARD_TARGET; cost: Tribute 1 "Archfiend" monster that leaves a
            // face-up "Terrorking Archfiend" target (s.rfilter: IsExistingTarget(..., c)); the target, if still a face-up
            // monster, gains EFFECT_DIRECT_ATTACK until the End Phase.
            new AtomRegistration.Atom(
                @"^\s*Tribute 1 ([A-Za-z]+) monster on your side of the field\.\s*During this turn, 1 ""([^""]+)"" on " +
                @"your side of the field can attack your opponent['’]s Life Points directly\.?\s*$",
                m => new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.TargetCanAttackDirectlyThisTurn,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[2].Value,
                    RequiresTributeCount = 1,
                    TributeSeriesName = m.Groups[1].Value,
                    RequiresTargetChoice = true,
                    IsPsctTarget = true,
                    RequiresTargetFaceUp = true,
                    MakesChainLink = true
                }),

            // Pandemonium (c94585852 e2): FIELD+CONTINUOUS EFFECT_LPCOST_REPLACE in the Field Zone, s.lrcon: the Standby
            // Phase and an "Archfiend" monster's effect pays the cost — for both players.
            new AtomRegistration.Atom(
                Start + @"Neither player has to pay Life Points during the Standby Phase for ""([^""]+)"" monsters" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.WaiveSeriesMonsterStandbyLpCosts,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    StaysOnField = true,
                    MakesChainLink = false
                }),

            // Pandemonium (c94585852 e4): FIELD+TRIGGER_O on its custom event (raised per player with the highest Level of
            // that player's "Archfiend" monsters destroyed and sent to the GY, except by battle), EFFECT_FLAG_EVENT_PLAYER
            // + DAMAGE_STEP: that player may add 1 "Archfiend" monster with a lower Level from their Deck to their hand.
            new AtomRegistration.Atom(
                Start + @"Each time a player['’]s ""([^""]+)"" monster\(s\) is destroyed and sent to the Graveyard, except " +
                @"by battle, that player can add 1 ""\1"" monster from their Deck to their hand that is a lower Level " +
                @"than the destroyed card" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle,
                    NamedCard = m.Groups[1].Value,
                    NamedCardIsSeries = true,
                    IsOptional = true,
                    StaysOnField = true,
                    MakesChainLink = true
                }),

            // Desrook Archfiend (c72192100 e3): QUICK_O in the hand on EVENT_TO_GRAVE (EFFECT_FLAG_CARD_TARGET +
            // DAMAGE_STEP); cost Cost.SelfToGrave (a send, not a discard); target a "Terrorking Archfiend" in eg that its
            // activator controlled on the field and that was destroyed; Special Summon it face-up.
            new AtomRegistration.Atom(
                Start + @"When an? ""([^""]+)"" on your side of the field is destroyed and sent to the Graveyard, you can " +
                @"send this card from your hand to the Graveyard to Special Summon the ""\1""" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.HandQuickEffectWhenNamedDestroyed,
                    Action = EffectActionKind.SpecialSummonDestroyedNamedFromGyBySendingThisFromHand,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    Side = EffectSide.Controller,
                    NamedCard = m.Groups[1].Value,
                    IsOptional = true,
                    IsPsctTarget = true,
                    MakesChainLink = true
                }),

            // Pandemonium Watchbear (c75375465): FIELD EFFECT_INDESTRUCTABLE_EFFECT, target range LOCATION_SZONE (the Field
            // Zone included) of its controller, s.indtg: face-up "Pandemonium"; s.indval: the opponent's effects.
            new AtomRegistration.Atom(
                Start + @"As long as this card remains face-up on your side of the field, ""([^""]+)"" on your side of " +
                @"the field is not destroyed by your opponent['’]s card effects" + End,
                m => new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects,
                    NamedCard = m.Groups[1].Value,
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }),
        };
    }
}
