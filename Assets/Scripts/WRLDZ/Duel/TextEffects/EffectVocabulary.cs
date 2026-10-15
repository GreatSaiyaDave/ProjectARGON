using System;
using System.Collections.Generic;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Finite effect kinds. Yu-Gi-Oh has thousands of cards and a few dozen
    /// timings / costs / resolutions. New cards must map onto this vocabulary.
    /// A unique card (Cyber Jar, Time Wizard's wrong-call, Ring of Destruction)
    /// is an <see cref="EffectResolutionKind.UniqueException"/> — one named
    /// action, never a silent approximation, never <c>if (cardId == …)</c>
    /// unless no other card shares the mechanic.
    /// </summary>
    public enum EffectCostKind
    {
        None = 0,
        Discard,
        Tribute,
        SendToGy,
        PayLp,
        BanishFromGy,
        RemoveCounters,
    }

    public enum EffectResolutionKind
    {
        None = 0,
        Destroy,
        Banish,
        ReturnToHand,
        /// <summary>Place a card on top of the Deck (Axe of Despair GY family).</summary>
        ReturnToDeck,
        Draw,
        Search,
        SpecialSummon,
        Damage,
        GainLp,
        GainLpReplacement,
        PayLp,
        ChangePosition,
        ModifyStats,
        DirectAttack,
        ExtraAttack,
        Token,
        Equip,
        TakeControl,
        PlaceCounters,
        NegateAttack,
        NegateActivation,
        PreventDamage,
        TreatAsName,
        /// <summary>Send cards from hand to GY as the resolution (not a cost).</summary>
        Discard,
        /// <summary>Coin or die then a vocabulary resolution (Barrel: toss then Destroy).</summary>
        Randomize,
        /// <summary>Unaffected / cannot-be-attacked / cannot-be-targeted (Fisherman family).</summary>
        Protection,
        /// <summary>One-card or tiny-family mechanic. Handle as an exception, not a new kind.</summary>
        UniqueException,
    }

    public static class EffectVocabulary
    {
        /// <summary>Shared resolution kinds the runtime already executes.</summary>
        public static readonly EffectResolutionKind[] SharedResolutions =
        {
            EffectResolutionKind.Destroy,
            EffectResolutionKind.Banish,
            EffectResolutionKind.ReturnToHand,
            EffectResolutionKind.ReturnToDeck,
            EffectResolutionKind.Draw,
            EffectResolutionKind.Search,
            EffectResolutionKind.SpecialSummon,
            EffectResolutionKind.Damage,
            EffectResolutionKind.GainLp,
            EffectResolutionKind.GainLpReplacement,
            EffectResolutionKind.PayLp,
            EffectResolutionKind.ChangePosition,
            EffectResolutionKind.ModifyStats,
            EffectResolutionKind.DirectAttack,
            EffectResolutionKind.ExtraAttack,
            EffectResolutionKind.Token,
            EffectResolutionKind.Equip,
            EffectResolutionKind.TakeControl,
            EffectResolutionKind.PlaceCounters,
            EffectResolutionKind.NegateAttack,
            EffectResolutionKind.NegateActivation,
            EffectResolutionKind.PreventDamage,
            EffectResolutionKind.TreatAsName,
            EffectResolutionKind.Discard,
            EffectResolutionKind.Randomize,
            EffectResolutionKind.Protection
        };

        /// <summary>
        /// Actions that are unique (or a 1–3 card family). Adding a 4th card
        /// with the same text means promote it to a shared kind instead.
        /// </summary>
        public static bool IsUniqueException(EffectActionKind action) =>
            action == EffectActionKind.CyberJarStyle ||
            // PhaseTimersAndDeckTop batch (v163): Pyro Clock of Destiny.
            action == EffectActionKind.AdvanceTurnCountByOne ||
            action == EffectActionKind.ApplySwordsOfRevealingLight ||
            action == EffectActionKind.BothPlayersDiscardAndRedraw ||
            action == EffectActionKind.FusionSummonRegistered ||
            action == EffectActionKind.EffectDamageBothFromOriginalAtk ||
            action == EffectActionKind.DiscardSelfNoBattleDamageThisBattle ||
            action == EffectActionKind.CoinCallDestroyOppOrSelf ||
            action == EffectActionKind.CoinCallDoubleOrHalveAtk ||
            action == EffectActionKind.RollDieZorc ||
            action == EffectActionKind.ApplyWabokuStyle ||
            action == EffectActionKind.SkipOpponentNextDrawPhase ||
            action == EffectActionKind.SkipControllerNextDrawPhases ||
            action == EffectActionKind.SkipControllerNextStandbyPhase ||
            action == EffectActionKind.MagicalHatsStyle ||
            action == EffectActionKind.DarkSageStyle ||
            action == EffectActionKind.ExodiaWinStyle ||
            // Exodia: the only hand win condition in the pool (a named rule, not a shared kind).
            action == EffectActionKind.WinDuelWithNamedSetInHand ||
            // DestinyBoard batch: Destiny Board's field win, its in-order placement of the Spirit Messages and its
            // leave-field send (one card's rule set, not shared kinds).
            action == EffectActionKind.WinWithThisAndNamedSetFaceUpOnField ||
            action == EffectActionKind.PlaceNextNamedInOrderFromHandOrDeck ||
            action == EffectActionKind.SendSeriesAndNamedToGyWhenOneLeavesField ||
            // ChoiceBullets batch (v173): Chick's immediate top-or-bottom look at the opponent's Deck.
            action == EffectActionKind.LookAtOpponentTopCardThenTopOrBottom ||
            // DiscardTriggersSelfSs batch (v173): Fear from the Dark / Despair from the Dark — a 2-card family
            // (both Zombies, same sentence with a When/If difference), so it stays a unique exception rather
            // than a shared kind.
            action == EffectActionKind.SpecialSummonThisFromGy;

        public static EffectCostKind CostOf(EffectClause c)
        {
            if (c == null) return EffectCostKind.None;
            // DcrEquipSpells batch: "Send 1 card from your hand to the Graveyard" is a send, not a discard.
            if (c.RequiresDiscardCost && c.DiscardCostIsSend)
                return EffectCostKind.SendToGy;
            if (c.RequiresDiscardCost || c.RequiresDiscardSelf)
                return EffectCostKind.Discard;
            if (c.RequiresTributeThis || c.RequiresTributeCount > 0)
                return EffectCostKind.Tribute;
            if (c.RequiresSendThisToGy || c.RequiresSendNamedToGy ||
                c.RequiresSendHandToGy || c.RequiresSendOtherYouControl ||
                c.RequiresSendEquipSpellOnThisToGy || c.RequiresSendRaceFromDeck ||
                // SimpleSpellTraps batch: Emergency Provisions sends other Spell/Trap Cards it controls.
                c.Action == EffectActionKind.GainLpPerSpellTrapSentAsCost ||
                // CallsDiceAndSetTriggers batch (v163): Reversal Quiz sends every other card in your hand and field.
                c.CostSendAllOtherHandAndFieldCardsToGy ||
                // ChainLinkAnswers batch: Spell Shield Type-8 sends a Spell Card from the hand.
                c.CostSendSpellsFromHandCount > 0)
                return EffectCostKind.SendToGy;
            // PayHalfLpCost: Solemn Judgment "Pay half your LP" (SummonNegation batch, v141).
            if (c.PayLpAmount > 0 || c.RequiresLpCostMultiple > 0 || c.PayHalfLpCost)
                return EffectCostKind.PayLp;
            if (c.BanishFromGyCount > 0)
                return EffectCostKind.BanishFromGy;
            // SpellCounterSpenders batch: "Remove N Spell Counters on your side of the field".
            if (c.RequiresRemoveSpellCounters > 0 || c.RequiresSpellCounters > 0 || c.RemoveSpellCountersOnYourSide > 0)
                return EffectCostKind.RemoveCounters;
            return EffectCostKind.None;
        }

        public static EffectResolutionKind ResolutionOf(EffectClause c)
        {
            if (c == null) return EffectResolutionKind.None;
            if (IsUniqueException(c.Action))
                return EffectResolutionKind.UniqueException;
            return c.Action switch
            {
                EffectActionKind.Destroy or
                    EffectActionKind.DestroyAllFaceUpNamedThenForceRaceAttack or
                    EffectActionKind.DestroySpecialSummonedMonsters or
                    EffectActionKind.DestroyOppMonstersAtkLeq or
                    EffectActionKind.SelfDestroyUnlessNamedFaceUp or
                    EffectActionKind.DestroyThisAfterResolvingTargetingEffect or
                    EffectActionKind.DestroyTokensInflictPer or
                    EffectActionKind.DestroySameNameInControllerHandAndDeck or
                    EffectActionKind.DestroyOppAttackThenDamage or
                    EffectActionKind.DestroyAllEquips or
                    EffectActionKind.DestroyAllEquippedMonsters or
                    EffectActionKind.DestroyAllOppSpellTraps or
                    EffectActionKind.CrushCardVirusStyle or
                    EffectActionKind.DestroyThisInsteadOfControllerTraps =>
                    EffectResolutionKind.Destroy,
                EffectActionKind.Banish or
                    EffectActionKind.BanishThenSameNameFromOppHandDeck or
                    EffectActionKind.BanishRandomFromOppHandFaceDown =>
                    EffectResolutionKind.Banish,
                EffectActionKind.ReturnToHand or
                    EffectActionKind.ReturnAllFaceUpFusionsToExtra =>
                    EffectResolutionKind.ReturnToHand,
                EffectActionKind.PlaceThisOnTopOfDeck or
                    EffectActionKind.PlaceTargetOnTopOfDeck or
                    EffectActionKind.ShuffleControllerDeck or
                    EffectActionKind.PlaceHandCardsOnBottomOfDeck or
                    EffectActionKind.ReorderTopOfOwnDeck =>
                    EffectResolutionKind.ReturnToDeck,
                EffectActionKind.SendFromTopOfDeckToGy => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.Draw or
                    // Graceful Charity: draw, then discard (DrawThenDiscard batch).
                    EffectActionKind.DrawThenDiscard or
                    EffectActionKind.OpponentShufflesHandThenDraws or
                    EffectActionKind.DrawWhenTributeSummonedWithTwoPlus => EffectResolutionKind.Draw,
                EffectActionKind.DiscardRandomFromOpponentHand => EffectResolutionKind.Discard,
                EffectActionKind.AddFromGyToHand or
                    EffectActionKind.AddFromDeckToHand or
                    EffectActionKind.AddNamedFromDeckToHand or
                    // MfcSimpleSpellsB batch (v174) — Emblem of Dragon Destroyer: add 1 "Buster Blader"
                    // from the Deck or the GY. Deck first, then GY (ygopro LOCATION_DECK + LOCATION_GRAVE).
                    EffectActionKind.AddNamedFromDeckOrGyToHand =>
                    EffectResolutionKind.Search,
                EffectActionKind.SpecialSummonFromGy or
                    EffectActionKind.SpecialSummonFromHand or
                    EffectActionKind.SpecialSummonFromDeck or
                    EffectActionKind.SpecialSummonNamed or
                    EffectActionKind.SpecialSummonThisFromHand or
                    EffectActionKind.SpecialSummonThisFromBanished or
                    EffectActionKind.SpecialSummonFusionFromExtra or
                    EffectActionKind.RitualSummon =>
                    EffectResolutionKind.SpecialSummon,
                EffectActionKind.SpecialSummonToken => EffectResolutionKind.Token,
                EffectActionKind.TakeEffectDamage or
                    EffectActionKind.InflictDamageToOpponent or
                    EffectActionKind.InflictDamageHalfTributedAtk or
                    EffectActionKind.InflictDamageEqualToAtk =>
                    EffectResolutionKind.Damage,
                EffectActionKind.GainLifePoints or
                    EffectActionKind.GainLpEqualToAtk or
                    EffectActionKind.GainLpWhenControllerDraws or
                    EffectActionKind.GainLpWhenMonsterReturnsToOwnerHand or
                    EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons or
                    EffectActionKind.GainLpWhenTributedForSummon =>
                    EffectResolutionKind.GainLp,
                EffectActionKind.InflictDamageWhenControllerGainsLp => EffectResolutionKind.Damage,
                EffectActionKind.RemoveAllSpellCounters => EffectResolutionKind.PlaceCounters,
                EffectActionKind.ConvertOpponentLpGainToDamage =>
                    EffectResolutionKind.GainLpReplacement,
                EffectActionKind.PayLifePoints or
                    EffectActionKind.PayLpToDeclareAttack => EffectResolutionKind.PayLp,
                EffectActionKind.ChangeBattlePosition or
                    EffectActionKind.SetThisFaceDownDefense or
                    EffectActionKind.ChangeThisBattlePosition =>
                    EffectResolutionKind.ChangePosition,
                EffectActionKind.ContinuousGainAtkDef or
                    EffectActionKind.ContinuousReduceLevel or
                    EffectActionKind.LoseAtkDefUntilEndOfTurn or
                    EffectActionKind.GainAtkDefUntilEndOfTurn or
                    EffectActionKind.ModifyAllAtkDefByDieUntilEndOfTurn or
                    EffectActionKind.GainThisAtkUntilEnd or
                    EffectActionKind.GainAtkPerSpellCounter or
                    EffectActionKind.SetAttackingMonsterAtkToZeroThisCalc or
                    EffectActionKind.HalveOriginalAtk or
                    EffectActionKind.ApplyLingeringAtkDef =>
                    EffectResolutionKind.ModifyStats,
                EffectActionKind.CanAttackDirectly or
                    EffectActionKind.GrantDirectAttackThisTurn or
                    EffectActionKind.ForceOpponentDirectAttacksThisTurn =>
                    EffectResolutionKind.DirectAttack,
                EffectActionKind.ExtraAttacks or
                    // SpellTrapDestroyedResponse (v142), Weather Report: a second Battle Phase — more attacks.
                    EffectActionKind.ConductBattlePhaseTwice => EffectResolutionKind.ExtraAttack,
                EffectActionKind.EquipThisToTarget or
                    EffectActionKind.EquipThisToAttackingMonster or
                    EffectActionKind.UnequipThisSpecialSummon or
                    EffectActionKind.EquipTargetToThis =>
                    EffectResolutionKind.Equip,
                EffectActionKind.TakeControlLevelLeq or
                    EffectActionKind.TakeControlUntilEndPhase or
                    EffectActionKind.OptionalPayLpShiftControl or
                    EffectActionKind.TakeControlWhileSourceFaceUp or
                    EffectActionKind.SwitchControlWithThis or
                    EffectActionKind.ReturnControlToOwners or
                    // Soul Exchange: Tribute the opponent's monster as if you controlled it (TributeSummons batch).
                    EffectActionKind.TributeTargetAsIfControlledThisTurn => EffectResolutionKind.TakeControl,
                EffectActionKind.PlaceSpellCounters => EffectResolutionKind.PlaceCounters,
                EffectActionKind.NegateAttack or
                    EffectActionKind.NegateThisAttack or
                    EffectActionKind.PreventOpponentAttacksThisTurn =>
                    EffectResolutionKind.NegateAttack,
                EffectActionKind.CannotAttackFaceDownMonstersThisTurn =>
                    EffectResolutionKind.Protection,
                EffectActionKind.NegateActivation or
                    // SummonNegation batch (v141): negate a Summon (Horn of Heaven / Solemn Judgment).
                    EffectActionKind.NegateSummon =>
                    EffectResolutionKind.NegateActivation,
                EffectActionKind.PreventControllerBattleDamage or
                    EffectActionKind.NoDamageThisTurn or
                    EffectActionKind.NoEffectDamageThisTurn =>
                    EffectResolutionKind.PreventDamage,
                EffectActionKind.AlwaysTreatedAsName or
                    EffectActionKind.FieldTreatedAsName =>
                    EffectResolutionKind.TreatAsName,
                EffectActionKind.CoinTossNDestroyIfHeads or
                    EffectActionKind.CallCoinLoseHalfLpIfWrong =>
                    EffectResolutionKind.Randomize,
                EffectActionKind.CannotBeAttackTarget or
                    EffectActionKind.UnaffectedByCardEffects or
                    EffectActionKind.CannotBeTargetedByEffects or
                    EffectActionKind.ContinuousCannotTargetDragons or
                    EffectActionKind.ContinuousCannotAttack or
                    EffectActionKind.SealNonRaceAttackerNextTurn or
                    EffectActionKind.AttacksMustTargetEquippedHost or
                    EffectActionKind.OpponentCannotActivateTrapsDuringBattle or
                    EffectActionKind.SealTrapsAnsweringThisSummon or
                    EffectActionKind.CountsAsTwoTributesForAttribute or
                    EffectActionKind.MillToDeclareAttack or
                    EffectActionKind.CannotBeTributedForSummon or
                    EffectActionKind.NeitherPlayerCanTribute or
                    EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase or
                    EffectActionKind.NoHandSizeLimit or
                    EffectActionKind.OpponentHandSizeLimitBecomes or
                    EffectActionKind.ControllerDoesNotPayLpToActivateSpells or
                    EffectActionKind.NegateAllEquipCardEffectsOnField or
                    EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases or
                    EffectActionKind.OpponentCannotChangeBattlePositions or
                    EffectActionKind.CannotBeDestroyedByBattle or
                    EffectActionKind.ContinuousCannotBeDestroyedByBattle or
                    EffectActionKind.PiercingBattleDamage or
                    EffectActionKind.CannotBanishFromGraveyard or
                    EffectActionKind.CannotTargetCardsInGraveyard or
                    EffectActionKind.DieRollNegateWhenTargeted =>
                    EffectResolutionKind.Protection,
                EffectActionKind.PayLpOrDestroyThis =>
                    EffectResolutionKind.Destroy,
                // Mandatory upkeep: the controller pays LP (a cost-like payment, not damage) — the same
                // resolution as the local lineage's shared Standby PayLifePoints upkeep.
                EffectActionKind.StandbyMaintenancePayLp =>
                    EffectResolutionKind.PayLp,
                EffectActionKind.SetTargetFaceDownDefense or
                    EffectActionKind.ChangeToFaceUpAttack or
                    EffectActionKind.ChangeToFaceUpDefense or
                    EffectActionKind.ChangeAllToAttackPosition or
                    EffectActionKind.ContinuousForceDefenseLockPosition =>
                    EffectResolutionKind.ChangePosition,
                EffectActionKind.LoseLifePoints => EffectResolutionKind.Damage,
                EffectActionKind.GainSelfAtkPerCount => EffectResolutionKind.ModifyStats,
                // Random discard is a hand → GY send (Destroy family of removals).
                EffectActionKind.DiscardRandomFromHand => EffectResolutionKind.Destroy,
                // A card that destroys itself (Gaia Soul's End Phase upkeep, Toon World's ignition) is still a
                // removal, so it shares the Destroy kind rather than being a unique exception.
                EffectActionKind.DestroyThisCard => EffectResolutionKind.Destroy,
                EffectActionKind.EndBattlePhase => EffectResolutionKind.NegateAttack,
                // PSV tranche 1: shared shapes, not new kinds.
                EffectActionKind.DestroySummonedThisTurnLevelLeq or
                    EffectActionKind.DestroyEquipsAttachedToThis =>
                    EffectResolutionKind.Destroy,
                EffectActionKind.PlaceNamedFromDeckOnTop => EffectResolutionKind.Search,
                EffectActionKind.SpecialSummonChosenFromHandOrDeck => EffectResolutionKind.SpecialSummon,
                EffectActionKind.ShuffleTargetAndHandIntoDeckDraw or
                    EffectActionKind.OpponentDrawsThenDiscardsDrawnSpells =>
                    EffectResolutionKind.Draw,
                EffectActionKind.DiscardJustDrawnCard => EffectResolutionKind.Discard,
                EffectActionKind.RevealJustDrawnNormalAndDraw => EffectResolutionKind.Draw,
                EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects or
                    EffectActionKind.SetAllOppMonstersFaceDownDefense => EffectResolutionKind.ChangePosition,
                EffectActionKind.DoubleAtkOfYourMatchingThenDestroyAtEnd => EffectResolutionKind.ModifyStats,
                EffectActionKind.LockTargetCannotAttackWhileFaceUp => EffectResolutionKind.Protection,
                // Taunt (BattleRules batch): the opponent may attack only the selected monster this turn — the same
                // attack-target restriction family (Staunch Defender's EFFECT_MUST_ATTACK_MONSTER shape).
                EffectActionKind.OpponentAttacksOnlySelectedMonsterThisTurn => EffectResolutionKind.Protection,
                // Piercing: battle damage reaches the player, like a direct-attack grant.
                EffectActionKind.ContinuousPiercing or
                    EffectActionKind.EquippedGainsPiercing =>
                    EffectResolutionKind.DirectAttack,
                // ContinuousNegation batch (v141): continuous Trap / Spell activation locks and negation.
                EffectActionKind.ContinuousCannotActivateTraps or
                    EffectActionKind.ContinuousNegateTrapEffectsOnField or
                    EffectActionKind.ContinuousCannotActivateSpells or
                    EffectActionKind.ContinuousNegateSpellEffectsOnField =>
                    EffectResolutionKind.Protection,
                // TurnNegationTraps (v141): same kind as The Emperor's Holiday's field-wide negation.
                EffectActionKind.NegateCardFamilyOnFieldThisTurn => EffectResolutionKind.Protection,
                // ContinuousNegation batch: Gorgon's Eye — the same turn-scoped negation family (a field
                // EFFECT_DISABLE with RESET_PHASE|PHASE_END over the Defense Position monsters).
                EffectActionKind.NegateEffectsOfDefensePositionMonstersThisTurn => EffectResolutionKind.Protection,
                // ContinuousTrapsAndSpiritSupport batch: Mind on Air — a Continuous Trap that imposes one continuous
                // rule on the opponent (their hand is public), the same family as NegateCardFamilyOnFieldThisTurn.
                EffectActionKind.OpponentMustPlayWithHandRevealed => EffectResolutionKind.Protection,
                // ContinuousTrapsAndSpiritSupport batch: Greed — 500 per card drawn by a card effect, at the End
                // Phase. Its tracker clause records; the damage clause is the readable one.
                EffectActionKind.InflictDamagePerEffectDrawThisTurn => EffectResolutionKind.Damage,
                // Muko: the answered draw is replaced by a discard, so the effect's outcome is a hand/Graveyard send.
                EffectActionKind.ReplaceDrawLinkWithDiscard => EffectResolutionKind.Discard,
                // TypeDeclaration batch: Mind Crush — declare a card name, and a hand goes to the GY either way.
                EffectActionKind.DeclareCardNameThenOpponentDiscardsAllOrSelfDiscardsRandom =>
                    EffectResolutionKind.Discard,
                // The tracker clause is a ContinuousWhileFaceUp marker, not a resolution, so it maps to None like
                // the other markers (it exists to make the card a Greed for NoteEffectDraw).
                EffectActionKind.TrackEffectDrawForEndPhaseDamage => EffectResolutionKind.None,
                // DiscardTriggers (v142): GY → Deck, shuffled (Penguin Knight); hand → GY discards (Delinquent Duo).
                EffectActionKind.ShuffleGraveyardIntoDeck => EffectResolutionKind.ReturnToDeck,
                // MfcSimpleSpellsB batch (v174): Hidden Spellbook — GY Spell targets into the Deck, shuffled.
                EffectActionKind.ShuffleGySpellsIntoDeck => EffectResolutionKind.ReturnToDeck,
                // UnionRiders batch (v175): Union-state riders (Kiryu, Second Goblin, Lunatite) and Union Rider's take-and-equip.
                EffectActionKind.TributeThisSoEquippedMonsterAttacksDirectly => EffectResolutionKind.DirectAttack,
                EffectActionKind.ChangeEquippedMonsterBattlePosition => EffectResolutionKind.ChangePosition,
                EffectActionKind.EquippedUnaffectedByOpponentSpellEffects => EffectResolutionKind.Protection,
                EffectActionKind.TakeControlOfOpponentUnionAndEquipToThis => EffectResolutionKind.TakeControl,
                EffectActionKind.SpecialSummonTargetsUnionsThenItAttacksAgain => EffectResolutionKind.SpecialSummon,
                // SummonLocksAndStatRules batch: Amazoness Spellcaster's original-ATK switch; Rivalry of Warlords' one-Type
                // rule (a continuous lock, like the summon / attack locks above).
                EffectActionKind.SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn => EffectResolutionKind.ModifyStats,
                EffectActionKind.EachPlayerKeepsOneMonsterType => EffectResolutionKind.Protection,
                // SimpleTriggersAndSwarm batch: Outstanding Dog Marron (back into the Deck), Don Turtle / Mustering of the
                // Dark Scorpions (hand Special Summons), Dark Scorpion Combination (direct attacks).
                EffectActionKind.ShuffleThisFromGyIntoDeck => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.SpecialSummonAnyNumberFromHand => EffectResolutionKind.SpecialSummon,
                // SimpleTriggersAndSwarm batch: Two-Man Cell Battle — the same Special Summon from the hand, only
                // during each player's End Phase and only of a Level 4 Normal Monster.
                EffectActionKind.SpecialSummonLevelNormalFromHandAtEachPlayersEndPhase =>
                    EffectResolutionKind.SpecialSummon,
                EffectActionKind.NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn => EffectResolutionKind.DirectAttack,
                // DcrSpellTrapUtility batch: Interdimensional Matter Transporter / Different Dimension Gate banish (they
                // return later), Final Countdown's countdown (a lasting player effect; its Lua has no category),
                // Dragged Down into the Grave's discards.
                EffectActionKind.BanishTargetUntilEndPhase or
                    EffectActionKind.BanishTargetsReturnWhenThisDestroyedToGy => EffectResolutionKind.Banish,
                EffectActionKind.WinAfterTurnCountdown => EffectResolutionKind.Protection,
                EffectActionKind.BothPickFromOpponentHandDiscardThenBothDraw => EffectResolutionKind.Discard,
                // BattleDestroyedTriggers batch: Agido's die-roll revival, Keldo's GY-to-Deck, Different Dimension Dragon's
                // two destruction protections.
                EffectActionKind.RollDieSpecialSummonFromGyWithLevelOfRoll => EffectResolutionKind.SpecialSummon,
                EffectActionKind.ShuffleTargetsFromOpponentGyIntoDeck => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis or
                    EffectActionKind.CannotBeDestroyedByBattleWithMonsterAtkAtMost => EffectResolutionKind.Protection,
                // DerivedAtkMonsters batch (v176): Tribute-derived ATK (Garzetts); Guardian Tryce's revival of its Tributes.
                EffectActionKind.ThisAtkBecomesTributedOriginalAtk => EffectResolutionKind.ModifyStats,
                EffectActionKind.SpecialSummonTributeMaterialsFromGy => EffectResolutionKind.SpecialSummon,
                // ArchfiendRiders batch: Terrorking's summon lock, Pandemonium's waiver (continuous rules, like the locks
                // above), Pandemonium's search, Pandemonium Watchbear's protection.
                EffectActionKind.CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries => EffectResolutionKind.Protection,
                EffectActionKind.WaiveSeriesMonsterStandbyLpCosts => EffectResolutionKind.Protection,
                EffectActionKind.SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle => EffectResolutionKind.Search,
                EffectActionKind.NamedSpellTrapCannotBeDestroyedByOpponentEffects => EffectResolutionKind.Protection,
                EffectActionKind.SpecialSummonDestroyedNamedFromGyBySendingThisFromHand => EffectResolutionKind.SpecialSummon,
                // MonsterNegationAndControlLocks batch: Skill Drain's field-wide monster negation, Blindly Loyal Goblin's
                // control lock and Non-Spellcasting Area's Spell immunity (continuous rules, like the locks above); Final
                // Attack Orders' Attack lock and Stumbling's summon-time change (position changes).
                EffectActionKind.ContinuousNegateMonsterEffectsOnField or
                    EffectActionKind.ControlOfThisCannotSwitch or
                    EffectActionKind.NonEffectMonstersUnaffectedByOtherSpells => EffectResolutionKind.Protection,
                // FusionSubstitutes batch (v180, coverage family F2): the substitute ability is a passive continuous rule.
                EffectActionKind.FusionSubstitute => EffectResolutionKind.Protection,
                // SpellEventsAndHandSendTriggers batch: Curse of Darkness, Tower of Babel, Spirit of the Pot of Greed.
                EffectActionKind.InflictDamageToSpellActivatorAfterResolve => EffectResolutionKind.Damage,
                EffectActionKind.DestroyThisAtNthSpellCounterDamageActivator => EffectResolutionKind.Destroy,
                EffectActionKind.ActivatorOfNamedCardMayDrawWhileAttackPosition => EffectResolutionKind.Draw,
                    EffectActionKind.BanishAllGyMonstersWhenOpponentTargetsGyMonster => EffectResolutionKind.Banish,
                // GyHandAndSummonEventTriggers batch: Emissary of the Afterlife (each player adds from their own Deck),
                // "7" (draw, then destroy every face-up copy) and Beckoning Light (discard the hand, then add).
                // LvChain batch: Dark Mimic LV3's conditional draw (1, or 2 when "Dark Mimic LV1" Summoned it).
                EffectActionKind.DrawAmountWhenSpecialSummonedByNamed => EffectResolutionKind.Draw,
                EffectActionKind.EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck => EffectResolutionKind.Draw,
                EffectActionKind.DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed => EffectResolutionKind.Draw,
                EffectActionKind.DiscardHandThenAddAttributeMonstersFromGyPerDiscard => EffectResolutionKind.Discard,
                // IgnitionCostsAndSimpleSpells batch: Sand Gambler's 3 coins then a destroy (Randomize, like Barrel Dragon);
                // Mystik Wok's LP gain from the Tributed monster's ATK or DEF.
                EffectActionKind.CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours => EffectResolutionKind.Randomize,
                EffectActionKind.GainLpEqualToTributedAtkOrDef => EffectResolutionKind.GainLp,
                // AttackShieldsAndDamageZero batch: Emissary of the Oasis / The Sanctuary in the Sky; Soul-Absorbing Bone Tower.
                EffectActionKind.AvoidBattleDamageFromBattlesInvolving => EffectResolutionKind.PreventDamage,
                EffectActionKind.MillOpponentWhenRaceSpecialSummoned => EffectResolutionKind.ReturnToDeck,
                // SanctuaryAndZera batch: Saturn's LP-lead damage; Light of Judgment's send-to-GY-or-discard choice.
                EffectActionKind.InflictLpLeadAsDamageToOpponent => EffectResolutionKind.Damage,
                EffectActionKind.SendOppFieldCardToGyOrDiscardFromOppHand => EffectResolutionKind.Discard,
                EffectActionKind.ContinuousForceAttackLockPosition or
                    EffectActionKind.ChangeSummonedMonstersToDefense => EffectResolutionKind.ChangePosition,
                EffectActionKind.OpponentDiscardsRandomThenOneOfTheirChoice => EffectResolutionKind.Discard,
                // TokensDeclarationsAndPicks batch: DNA Transplant's declaration (naming, as DeclareMonsterType) and its
                // Attribute change (a stat change, as AllFaceUpMonstersBecomeDeclaredType); Jade Insect Whistle's Deck
                // order; A Hero Emerges' random Special Summon; Wild Nature's Release; Balloon Lizard's counters and burn.
                EffectActionKind.DeclareMonsterAttribute => EffectResolutionKind.TreatAsName,
                EffectActionKind.AllFaceUpMonstersBecomeDeclaredAttribute => EffectResolutionKind.ModifyStats,
                EffectActionKind.OpponentPlacesRaceMonsterFromTheirDeckOnTop => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.RandomHandCardSpecialSummonOrSendToGy => EffectResolutionKind.SpecialSummon,
                EffectActionKind.TargetGainsAtkEqualToDefDestroyAtEndPhase => EffectResolutionKind.ModifyStats,
                EffectActionKind.PlaceCounterOnThisCard => EffectResolutionKind.PlaceCounters,
                EffectActionKind.InflictCountersTimesAmountToDestroyer => EffectResolutionKind.Damage,
                // BattlePhaseEnd (v142): end-of-Battle-Phase and battle-aftermath effects.
                EffectActionKind.ChangeThisToDefenseAfterAttackingLockPosition => EffectResolutionKind.ChangePosition,
                EffectActionKind.DestroyAttackerAtOpponentsLaterEndPhase => EffectResolutionKind.Destroy,
                // TypeDeclaration (v142): declaring a Type is naming it (DNA Surgery, The Regulation of Tribe); every
                // face-up monster becoming that Type is a stat change (as ContinuousReduceLevel), not a rename, so the
                // AI's Solemn Judgment counts DNA Surgery as worth half its LP. The Tribute-or-destroy upkeep and
                // Earthshaker are removals.
                EffectActionKind.DeclareMonsterType => EffectResolutionKind.TreatAsName,
                EffectActionKind.AllFaceUpMonstersBecomeDeclaredType => EffectResolutionKind.ModifyStats,
                EffectActionKind.StandbyTributeOrDestroyThis or
                    EffectActionKind.DestroyAllFaceUpOfAttributeOpponentPicksFromTwo => EffectResolutionKind.Destroy,
                // LocksAndZones (v142): Cold Wave's turn-scoped Spell/Trap lock is the Spell Canceller family of
                // activation locks; Dimensionhole's temporary banishment is a Banish.
                EffectActionKind.LockSpellTrapPlayAndSetUntilYourNextTurn => EffectResolutionKind.Protection,
                EffectActionKind.BanishTargetUntilYourNextStandbyPhase => EffectResolutionKind.Banish,
                // Light of Intervention: a continuous position lock, like Swords of Concealing Light's.
                EffectActionKind.MonstersCannotBeSetOrTurnedFaceDown => EffectResolutionKind.Protection,
                // HandDisruption (v142): Gamble is a called coin then a draw (Randomize, like Barrel / Jirai Gumo);
                // Appropriate is a continuous draw; Painful Choice adds a Deck card to the hand (Search).
                EffectActionKind.CoinCallDrawUntilOrSkipNextTurn => EffectResolutionKind.Randomize,
                EffectActionKind.DrawWhenOpponentDrawsOutsideDrawPhase => EffectResolutionKind.Draw,
                EffectActionKind.RevealDeckCardsOpponentPicksOneToHand => EffectResolutionKind.Search,
                // FlipMassEffects (v142): the Flip marker resolves nothing by itself (its card's later
                // clause does); the Standby tribute destroys (then burns); the mass summons Special Summon.
                EffectActionKind.RecordThisCardFlipped => EffectResolutionKind.None,
                EffectActionKind.TributeThisDestroyYourMonstersBurnHalfAtk => EffectResolutionKind.Destroy,
                EffectActionKind.EachPlayerSpecialSummonsOneFromOwnGy or
                    EffectActionKind.ShuffleFieldMonstersIntoDeckExcavateSummon =>
                    EffectResolutionKind.SpecialSummon,
                // RedirectAndReplace (v142): moving an effect / attack off a monster protects it (Fairy's Hand
                // Mirror, Shift); Kotodama destroys; Banisher of the Light banishes what would go to the GY.
                EffectActionKind.RedirectToAnotherAppropriateTarget => EffectResolutionKind.Protection,
                EffectActionKind.DestroyNewFaceUpMonstersSharingAName => EffectResolutionKind.Destroy,
                EffectActionKind.BanishCardsSentToGyInstead => EffectResolutionKind.Banish,
                // StandbyGrowthAndMaintenance (v142): Castle of Dark Illusions / Pumpking / Mirror Wall change
                // ATK/DEF; Skull Invitation inflicts damage per card sent to the GY.
                EffectActionKind.GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp or
                    EffectActionKind.GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby or
                    EffectActionKind.HalveAtkOfOpponentAttackersWhileThisOnField =>
                    EffectResolutionKind.ModifyStats,
                EffectActionKind.InflictDamageToOwnerPerCardSentToGy => EffectResolutionKind.Damage,
                // SpiritsAndMisc batch: Yamata Dragon / Hino-Kagu-Tsuchi / Kiseitai.
                EffectActionKind.DrawUntilHandSize => EffectResolutionKind.Draw,
                EffectActionKind.DiscardOpponentHandAtNextDrawPhase => EffectResolutionKind.Discard,
                EffectActionKind.BecomeEquipOnFaceDownAttacker => EffectResolutionKind.Equip,
                EffectActionKind.GainLpHalfEquippedMonsterAtk => EffectResolutionKind.GainLp,
                // FlipDefCheck (Acid Trap Hole): flip, then destroy or re-Set by DEF; the removal is the point.
                EffectActionKind.FlipFaceUpDestroyIfDefLeqElseSetFaceDown => EffectResolutionKind.Destroy,
                // Toons batch: the Toon Special Summon procedure and the continuous Toon rules.
                EffectActionKind.SpecialSummonFromHandProcedure => EffectResolutionKind.SpecialSummon,
                EffectActionKind.CannotAttackTurnSpecialSummoned => EffectResolutionKind.Protection,
                EffectActionKind.DestroyThisWhenNamedDestroyed => EffectResolutionKind.Destroy,
                EffectActionKind.CanAttackDirectlyUnlessOpponentControlsToon => EffectResolutionKind.DirectAttack,
                // PositionLocks batch: Aqua Spirit / Garuda the Wind Spirit (one targeted change, Aqua's with a
                // rest-of-turn lock) and Curse of Fiend (every monster changes, then a field-wide lock this turn).
                EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase or
                    EffectActionKind.ChangeAllMonsterPositionsLockRestOfTurn => EffectResolutionKind.ChangePosition,
                // EquipRiders batch: the equipped monster's attack lock is the Equip form of ContinuousCannotAttack;
                // Mask of the Accursed / Mask of Dispel burn the linked card's controller; Mask of Dispel's link
                // attaches this card to its target (the Equip kind, without equipping); its leave-field
                // self-destruction and Ekibyo Drakmord's countdown are removals.
                EffectActionKind.EquippedCannotAttack => EffectResolutionKind.Protection,
                // EquipLocksAndLeaveField batch: Raregold / Metalsilver Armor locks.
                EffectActionKind.OpponentCannotAttackOtherThanEquippedHost or
                    EffectActionKind.OpponentCannotTargetMonstersExceptEquippedHost => EffectResolutionKind.Protection,
                EffectActionKind.InflictDamageToLinkedCardController => EffectResolutionKind.Damage,
                EffectActionKind.LinkThisToTargetSpellTrap => EffectResolutionKind.Equip,
                EffectActionKind.DestroyThisWhenLinkedCardLeavesField or
                    EffectActionKind.DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis =>
                    EffectResolutionKind.Destroy,
                // SelfStatMonsters: a continuous attack lock, same kind as the other attack restrictions.
                EffectActionKind.CannotAttackDirectly => EffectResolutionKind.Protection,
                // FlipCostsAndFusion batch: Summoner of Illusions (Tribute, then a Fusion Monster from the Extra Deck) and
                // De-Fusion (return to the Extra Deck, then its materials from the GY) are Special Summons.
                EffectActionKind.TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase or
                    EffectActionKind.ReturnFusionToExtraThenMaySummonMaterials => EffectResolutionKind.SpecialSummon,
                // EquipFamily batch: Collected Power equips every Equip Card to its target (or destroys it).
                EffectActionKind.EquipAllEquipCardsToTarget => EffectResolutionKind.Equip,
                // EquipModel batch: Tailor of the Fickle moves an Equip Card to another correct target.
                EffectActionKind.MoveEquipCardToAnotherCorrectTarget => EffectResolutionKind.Equip,
                // EquipFamily batch: Dark Necrofear equips itself from the GY and takes control of that monster.
                EffectActionKind.EquipThisFromGyToTargetTakeControl => EffectResolutionKind.TakeControl,
                // BattleWindows batch: Fairy Box (coin, ATK becomes 0), Jam Defender / Dreamsprite (the battle moves to
                // another monster), Destruction Punch (destroy the attacker), Dark Spirit of the Silent (negate the attack).
                EffectActionKind.CallCoinZeroAttackerAtkUntilEndOfBattlePhase => EffectResolutionKind.Randomize,
                EffectActionKind.SwitchOpponentAttackToNamedMonsterYouControl or
                    EffectActionKind.BattleAnotherOwnMonsterInstead => EffectResolutionKind.Protection,
                EffectActionKind.DestroyAttackerAtEndOfDamageStep => EffectResolutionKind.Destroy,
                EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead => EffectResolutionKind.NegateAttack,
                // RuleLocks batch: continuous summon / banish locks (the Mask of Restrict / Necrovalley kind) and Royal
                // Command's negation of Flip monsters' activations.
                EffectActionKind.NeitherPlayerCanSpecialSummon or
                    EffectActionKind.ControllerCannotSummonExceptNamed or
                    EffectActionKind.OpponentCannotBanishFromGraveyards => EffectResolutionKind.Protection,
                EffectActionKind.NegateFlipMonsterEffectActivations => EffectResolutionKind.NegateActivation,
                // RuleLocks batch: Chain Energy's LP toll on hand actions (a continuous rule, like the locks above).
                EffectActionKind.HandActionsCostLp => EffectResolutionKind.Protection,
                // NegationsAndGySummons batch: Gilasaurus's own hand procedure and its gift to the opponent (a Special
                // Summon from the GY), Card of Safe Return's draw trigger.
                EffectActionKind.SpecialSummonThisFromHandByOwnProcedure or
                    EffectActionKind.OpponentMaySpecialSummonOneFromOwnGy => EffectResolutionKind.SpecialSummon,
                EffectActionKind.DrawWhenMonsterSpecialSummonedFromYourGy => EffectResolutionKind.Draw,
                // NegationsAndGySummons batch: Chosen One Special Summons the randomly chosen monster.
                EffectActionKind.SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom =>
                    EffectResolutionKind.SpecialSummon,
                // FaceUpQuickEffects batch (v160): Skull Lair destroys 1 face-up monster of the banished count's Level.
                EffectActionKind.DestroyFaceUpMonsterWithLevelEqualToBanishedCost => EffectResolutionKind.Destroy,
                // FaceUpQuickEffects batch (v160): Soul Demolition banishes 1 monster from each GY; The Dragon's Bead
                // negates a Trap Card's effect (and destroys it).
                EffectActionKind.BothPlayersTargetMonsterInOpponentsGyAndBanish => EffectResolutionKind.Banish,
                EffectActionKind.NegateTrapEffectTargetingFaceUpRaceAndDestroyIt => EffectResolutionKind.NegateActivation,
                // EngineHygiene batch (v160): Forced Requisition's opponent discards; Super Rejuvenation's End Phase draw.
                EffectActionKind.OpponentDiscardsSameNumberWhenYouDiscard => EffectResolutionKind.Discard,
                EffectActionKind.DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn => EffectResolutionKind.Draw,
                // SimpleSpellTraps batch: A Wingbeat of Giant Dragon (return, then destroy), Burst Breath, Dragon's
                // Gunfire's destroy bullet, Fiend Comedian (a called coin), Emergency Provisions (LP per card sent).
                EffectActionKind.ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps =>
                    EffectResolutionKind.ReturnToHand,
                EffectActionKind.DestroyFaceUpMonstersDefLeqTributedAtk or
                    EffectActionKind.DestroyTargetWithDefAtMost or
                    EffectActionKind.DestroyOneInHandOneOnFieldOneInDeck or
                    EffectActionKind.BlastSphereEquipThenStandbyDestroyInflict => EffectResolutionKind.Destroy,
                EffectActionKind.GainAtkDefPerNamedInBothGyWhileFaceUp or
                    EffectActionKind.GainAtkDefPerNamedCardInBothGyWhileFaceUp or
                    EffectActionKind.GainAtkDefUntilEndOfTurn or
                    EffectActionKind.HalfAtkAddToOtherUntilEndOfTurn => EffectResolutionKind.ModifyStats,
                EffectActionKind.InflictDamagePer1000TributedAtk or
                    EffectActionKind.InflictDamageToOpponent => EffectResolutionKind.Damage,
                EffectActionKind.CallCoinBanishOppGyOrSendDeckEqualToOppGy => EffectResolutionKind.Randomize,
                EffectActionKind.GainLpPerSpellTrapSentAsCost => EffectResolutionKind.GainLp,
                // SpiritMonsters batch: Asura Priest attacks each opponent's monster once (more attacks); Inaba White
                // Rabbit's "never a monster" and Great Long Nose's skipped Battle Phase are attack restrictions; Susa
                // Soldier halves the battle damage it inflicts; Maharaghi puts the top card back on the top or bottom.
                EffectActionKind.AttackAllOpponentMonstersOnceEach => EffectResolutionKind.ExtraAttack,
                EffectActionKind.CannotSelectMonsterAsAttackTarget or
                    EffectActionKind.OpponentSkipsNextBattlePhase => EffectResolutionKind.Protection,
                EffectActionKind.HalveBattleDamageThisInflictsToOpponent => EffectResolutionKind.PreventDamage,
                // BattleDamageEvents batch: Piranha Army / Stone Statue of the Aztecs double a battle's damage (marker).
                EffectActionKind.DoubleBattleDamageToOpponent => EffectResolutionKind.Damage,
                EffectActionKind.LookAtTopCardAtNextDrawPhaseTopOrBottom => EffectResolutionKind.ReturnToDeck,
                // BattleBoosts batch (v161): Spear Dragon to Defense at the end of the Damage Step; Dragon's Rage piercing for
                // your Dragons; Gray Wing's second attack this turn; The Hunter with 7 Weapons' declaration and its
                // damage-calculation ATK; Injection Fairy Lily's +3000 at damage calculation; Spirit Ryu's +1000 ATK/DEF.
                EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep => EffectResolutionKind.ChangePosition,
                EffectActionKind.GrantPiercingToYourRaceMonsters => EffectResolutionKind.DirectAttack, // piercing, as above
                EffectActionKind.GrantThisExtraAttackThisTurn => EffectResolutionKind.ExtraAttack,
                EffectActionKind.DeclareTypeForThisCardBattles => EffectResolutionKind.TreatAsName,
                EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType or
                    EffectActionKind.GainAtkThisDamageCalculation or
                    EffectActionKind.GainAtkDefUntilEndOfBattlePhase => EffectResolutionKind.ModifyStats,
                // BothSideAndMonsterEquip batch (v161): Sword Hunter.
                EffectActionKind.EquipMonstersThisDestroyedByBattleAsEquipSpells => EffectResolutionKind.Equip,
                // CoinDiceInterrupts batch: Second Coin Toss / Dice Re-Roll redo a coin toss / die roll.
                EffectActionKind.RedoOwnCoinTossOncePerTurn or
                    EffectActionKind.GrantDiceReRollThisTurn => EffectResolutionKind.Randomize,
                // SpecialSummonEffects batch (v161): Skull Knight #2, Twin-Headed Behemoth, The Puppet Magic of Dark
                // Ruler Special Summon; Winged Minion's lasting ATK/DEF gain; Gradius' Option copy and self-destruction.
                EffectActionKind.SpecialSummonNamedFromDeckWhenTributedForRaceSummon or
                    EffectActionKind.SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn or
                    EffectActionKind.BanishLevelSumThenSpecialSummonTarget => EffectResolutionKind.SpecialSummon,
                EffectActionKind.TargetGainsAtkDefWhileFaceUp or
                    EffectActionKind.AtkDefBecomeSelectedNamed => EffectResolutionKind.ModifyStats,
                EffectActionKind.DestroyThisWhenSelectedNamedLeavesField => EffectResolutionKind.Destroy,
                // ContinuousTrapsAndSpiritSupport batch: Fatal Abacus (burn per monster sent from the field), Life
                // Absorbing Machine (gain half the LP costs paid last turn), Ominous Fortunetelling (a called random
                // card), Fengsheng Mirror (discard a Spirit from the opponent's hand), Spirit's Invitation (the opponent
                // returns a monster), Spiritual Energy Settle Machine (no Spirit return; discard-or-destroy upkeep;
                // Spirits return when it leaves the field).
                EffectActionKind.InflictDamageToOwnerPerMonsterSentFromFieldToGy => EffectResolutionKind.Damage,
                EffectActionKind.GainHalfLpCostsPaidLastTurn => EffectResolutionKind.GainLp,
                EffectActionKind.CallTypeOfRandomOpponentHandCardBurn => EffectResolutionKind.Randomize,
                EffectActionKind.LookAtOpponentHandDiscardChosenSpirit => EffectResolutionKind.Discard,
                EffectActionKind.OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand or
                    EffectActionKind.ReturnFaceUpSpiritsWhenThisLeavesField => EffectResolutionKind.ReturnToHand,
                EffectActionKind.SpiritsDoNotReturnWhileThisFaceUp => EffectResolutionKind.Protection,
                EffectActionKind.EndPhaseDiscardOrDestroyThis => EffectResolutionKind.Destroy,
                // EquipAndSpellProtection batch: Fusion Sword Murasame Blade's and Heart of Clear Water's destruction
                // protections, Heart's self-destruction, Magic Reflector's destroy-replacement counter.
                EffectActionKind.ThisCannotBeDestroyedByCardEffectsWhileEquipped or
                    EffectActionKind.EquippedMonsterNotDestroyedByBattleOrTargetingEffects => EffectResolutionKind.Protection,
                EffectActionKind.DestroyThisWhileEquippedMonsterAtkAtLeast => EffectResolutionKind.Destroy,
                EffectActionKind.PlaceDestroyReplacementCounterOnTarget => EffectResolutionKind.PlaceCounters,
                // DeckReturnsAndMassEffects batch (v161): Fiber Jar (everything to the Deck, then draws), Mysterious
                // Guard (top of the Deck, then the hand), Creature Swap (switch control), Bubble Crash (send to the GY).
                EffectActionKind.ShuffleHandFieldGyIntoDeckThenEachDraws or
                    EffectActionKind.ReturnFirstTargetToDeckTopThenSecondToHandIfRace => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.EachPlayerChoosesOwnMonsterSwitchControl => EffectResolutionKind.TakeControl,
                EffectActionKind.EachPlayerSendsHandAndFieldToGyUntilAmount => EffectResolutionKind.Discard,
                // BattleRules batch: Cave Dragon's summon and attack conditions and Patrician of Darkness are rule
                // restrictions; Thunder Nyan Nyan and After the Struggle destroy; the Bandit makes the opponent discard.
                EffectActionKind.CannotBeNormalSummonedWhileControllerHasMonster or
                    EffectActionKind.CannotAttackUnlessControllerHasAnotherFaceUpRace or
                    EffectActionKind.YouChooseOpponentsAttackTargets => EffectResolutionKind.Protection,
                EffectActionKind.SelfDestroyWhileControllerHasFaceUpNonAttribute or
                    EffectActionKind.DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase => EffectResolutionKind.Destroy,
                EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn =>
                    EffectResolutionKind.Discard,
                // NegationMonsters batch (v161): Freed / Frontier Wiseman / Tyrant Dragon negate a targeting Spell/Trap
                // effect (and destroy that card); Freed searches instead of the draw; Ha Des / Twin-Headed Wolf negate
                // monsters destroyed by battle; Lesser Fiend banishes them.
                EffectActionKind.NegateSpellTrapEffectsThatTargetAndDestroy => EffectResolutionKind.NegateActivation,
                EffectActionKind.AddFromDeckInsteadOfNormalDraw => EffectResolutionKind.Search,
                EffectActionKind.NegateEffectsOfMonstersDestroyedByBattle => EffectResolutionKind.Protection,
                EffectActionKind.BanishMonstersDestroyedByBattleWithThis => EffectResolutionKind.Banish,
                // EffectDamageEvents batch (v162): reflect / negate the damage of the answered Chain Link; "each time" burns;
                // battle damage treated as effect damage.
                EffectActionKind.ReflectEffectDamageOfAnsweredLink => EffectResolutionKind.Damage,
                EffectActionKind.NegateEffectDamageOfAnsweredLinkThenOpponentDiscards => EffectResolutionKind.PreventDamage,
                EffectActionKind.InflictDamageEachTimeOpponentTakesEffectDamage => EffectResolutionKind.Damage,
                EffectActionKind.BattleDamageThisInflictsIsEffectDamage => EffectResolutionKind.Damage,
                EffectActionKind.InflictDamageEachTimeMonsterSentToOpponentGy => EffectResolutionKind.Damage,
                // PgdSpellTrapBasics batch (v162 PGD): Book of Taiyou, Curse of Aging / Pyramid Energy, D. Tribe, Needle
                // Ceiling, Secret Pass to the Treasures, Hieroglyph Lithograph, Card Shuffle.
                EffectActionKind.FlipFaceDownTargetToFaceUpAttack => EffectResolutionKind.ChangePosition,
                EffectActionKind.ModifyAllFaceUpMonstersAtkDefUntilEndPhase or
                    EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase => EffectResolutionKind.ModifyStats,
                EffectActionKind.DestroyAllFaceUpMonstersOnField => EffectResolutionKind.Destroy,
                EffectActionKind.TargetCanAttackDirectlyThisTurn => EffectResolutionKind.DirectAttack,
                EffectActionKind.ControllerHandSizeLimitBecomesForDuel => EffectResolutionKind.Protection,
                EffectActionKind.ShuffleYourOrOpponentsDeck => EffectResolutionKind.ReturnToDeck,
                // BattleOutcomeTriggers batch (v162): Zombyra's ATK loss and Mucus Yolk's Standby gain change this card's ATK;
                // Timeater's Main Phase 1 skip is a rule restriction; Kryuel destroys; Dark Scorpion Burglars mills a Spell.
                EffectActionKind.ChangeThisCardAtkWhileFaceUp or
                    EffectActionKind.GainThisCardAtkAtNextStandbyWhileFaceUp => EffectResolutionKind.ModifyStats,
                EffectActionKind.OpponentSkipsNextMainPhase1 => EffectResolutionKind.Protection,
                EffectActionKind.CoinCallRightDestroyOneOppMonster => EffectResolutionKind.Destroy,
                EffectActionKind.OpponentSendsSpellFromDeckToGy => EffectResolutionKind.Discard,
                EffectActionKind.MillCardOfDeclaredTypeFromOpponentDeck => EffectResolutionKind.Discard,
                // BattleAndPositionRecords batch: Abyssal Designator — declare Type + Attribute, the opponent sends 1
                // matching monster from hand or Deck to the GY. Same "declare and the opponent sends" family as
                // MillCardOfDeclaredTypeFromOpponentDeck.
                EffectActionKind.DeclareTypeAndAttributeThenOpponentSendsFromHandOrDeck =>
                    EffectResolutionKind.Discard,
                // FlipDownAndPeek batch (v162): 8-Claws Scorpion's damage-calculation ATK; Giant Axe Mummy destroys the
                // attacker; Cobraman Sakuzy / A Man with Wdjat look at Set cards that return to their original position;
                // Trap Dustshoot returns a monster from the opponent's hand to the Deck.
                EffectActionKind.AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation => EffectResolutionKind.ModifyStats,
                EffectActionKind.DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown => EffectResolutionKind.Destroy,
                EffectActionKind.LookAtOpponentSetSpellTraps or
                    EffectActionKind.LookAtTargetSetCard => EffectResolutionKind.ChangePosition,
                EffectActionKind.LookAtOpponentHandReturnChosenMonsterToDeck => EffectResolutionKind.ReturnToDeck,
                // StandbyBurnsAndDice batch (v162): Blind Destruction / Needle Wall die-roll destruction; Dark Snake Syndrome,
                // Mask of Dispel and Nightmare Wheel Standby burns; Mask of Dispel's link and leave-field destruction;
                // Inspection's random look at the opponent's hand.
                EffectActionKind.RollDieDestroyFaceUpMonstersWithRolledLevel => EffectResolutionKind.Destroy,
                EffectActionKind.RollDieDestroyOppMonsterInRolledZone => EffectResolutionKind.Destroy,
                EffectActionKind.InflictDoublingDamageToBothPlayers => EffectResolutionKind.Damage,
                EffectActionKind.InflictDamageToBothPlayers => EffectResolutionKind.Damage,
                // ConditionalSpellsAndTraps batch: Self-Destruct Button's LP change (as LoseLifePoints), Destruction Ring's
                // destroy-then-burn, D.D. Borderline's Battle Phase lock (as Frozen Soul's skip).
                EffectActionKind.BothPlayersLifePointsBecomeZero => EffectResolutionKind.Damage,
                EffectActionKind.DestroyTargetThenDamageBothPlayers => EffectResolutionKind.Destroy,
                EffectActionKind.NoBattlePhaseWhileNoSpellInControllerGy => EffectResolutionKind.Protection,
                EffectActionKind.LinkThisToTargetSpell => EffectResolutionKind.Equip,
                EffectActionKind.InflictDamageToLinkedSpellController => EffectResolutionKind.Damage,
                EffectActionKind.DestroyThisWhenLinkedSpellLeavesField => EffectResolutionKind.Destroy,
                EffectActionKind.InflictDamageToOpponentWhileBoundMonsterOnField => EffectResolutionKind.Damage,
                EffectActionKind.PayLpLookAtRandomOppHandCard or
                    EffectActionKind.PayLpLookAtOpponentHandAndSetCards => EffectResolutionKind.Randomize,
                // BattleDestroyedGyRecovery batch (v162): Crimson Sentry / Fairy Guardian, Return of the Doomed, Revival
                // Jam's flag and Standby Summon, Rope of Life.
                EffectActionKind.PlaceGyTargetOnBottomOfDeck => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase => EffectResolutionKind.Search,
                EffectActionKind.ArmThisSpecialSummonAtNextStandby or
                    EffectActionKind.SpecialSummonArmedThisFromGyInDefenseAtStandby or
                    EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk or
                    EffectActionKind.GyRevivalAfterStandbyWhenDestroyedByOpponent => EffectResolutionKind.SpecialSummon,
                // ContinuousLocksAndIgnitions batch (v163): Arsenal Bug's ATK/DEF, Timidity's Set-card protection, Non
                // Aggression Area's summon lock, Call of the Mummy / Frontline Base, Buster Rancher's damage-calculation ATK.
                EffectActionKind.ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace or
                    EffectActionKind.EquippedGainsAtkInDamageCalcVsStrongBattler => EffectResolutionKind.ModifyStats,
                EffectActionKind.SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase or
                    EffectActionKind.OpponentCannotSummonOrSetDuringNextTurn => EffectResolutionKind.Protection,
                EffectActionKind.SpecialSummonFromHandByFaceUpIgnition => EffectResolutionKind.SpecialSummon,
                // PhaseTimersAndDeckTop batch (v163): Bottomless Shifting Sand, Mirage of Nightmare, Different Dimension
                // Capsule, Royal Tribute (Pyro Clock of Destiny is a unique exception).
                EffectActionKind.DestroyAllFaceUpMonstersWithHighestAtk or
                    EffectActionKind.DestroyThisInOwnStandbyIfHandAtMost => EffectResolutionKind.Destroy,
                EffectActionKind.DrawUntilHandSizeRecordForNextStandbyDiscard => EffectResolutionKind.Draw,
                EffectActionKind.DiscardRandomRecordedDrawCountAtStandby or
                    EffectActionKind.BothPlayersDiscardAllMonstersInHand => EffectResolutionKind.Discard,
                EffectActionKind.BanishDeckCardFaceDownAddAtSecondStandby => EffectResolutionKind.Banish,
                // CallsDiceAndSetTriggers batch (v163): Dark Coffin (the opponent discards at random or destroys their own
                // monster), Dice Jar (dice, then damage), Ordeal of a Traveler / Reversal Quiz (a type call on a random or
                // top card), Reasoning (a Level declaration, then an excavation Special Summon).
                EffectActionKind.OpponentDiscardsRandomOrDestroysOwnMonster => EffectResolutionKind.Destroy,
                EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage or
                    EffectActionKind.OpponentCallsRandomHandCardTypeWrongReturnsAttacker or
                    EffectActionKind.CallTopDeckCardTypeRightExchangesLp => EffectResolutionKind.Randomize,
                EffectActionKind.OpponentDeclaresLevelExcavateUntilSummonableMonster => EffectResolutionKind.SpecialSummon,
                // TargetNegationsAndHandQuickEffects batch (v163): Great Dezard / Fushioh Richie, Charm of Shabti.
                EffectActionKind.NegateSpellTrapActivationTargetingThisAndDestroy => EffectResolutionKind.NegateActivation,
                EffectActionKind.SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase => EffectResolutionKind.Protection,
                // SummonTriggersAndTributes batch (v163): Byser Shock's return, King Tiger Wanghu's destruction, the "only 1
                // face-up" rule and Gravekeeper's Chief's Necrovalley immunity.
                EffectActionKind.ReturnAllSetCardsOnFieldToHand => EffectResolutionKind.ReturnToHand,
                EffectActionKind.DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned => EffectResolutionKind.Destroy,
                EffectActionKind.YouCanOnlyControlOneFaceUpNamed or
                    EffectActionKind.YourGyUnaffectedByNamedCard => EffectResolutionKind.Protection,
                // LastTurn batch (v163): Last Turn — the Lua's one category is CATEGORY_SPECIAL_SUMMON (the opponent's
                // Summon from the Deck); its pieces are shared primitives (LastTurnRuntime, DuelEngine.BeginForcedAttack).
                EffectActionKind.SelectedMonsterShowdownLoneMonsterWins => EffectResolutionKind.SpecialSummon,
                // SummonPromptsAndCosts batch (v163): Vampire Baby; Tyrant Dragon's GY summon cost (a summon rule).
                EffectActionKind.SpecialSummonMonstersThisDestroyedByBattleThisTurn or
                    EffectActionKind.GySpecialSummonNeedsTributeCost => EffectResolutionKind.SpecialSummon,
                // ForcedActivationAndSubstitution batch (v163): Spirit Elimination's turn-long banish substitution (a rule
                // change, like the RuleLocks), Major Riot's mass return then hand Summons.
                EffectActionKind.BanishFieldMonstersInsteadOfGyMonstersThisTurn => EffectResolutionKind.Protection,
                EffectActionKind.ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown =>
                    EffectResolutionKind.ReturnToHand,
                // PerCopyDeck batch (v163): Parasite Paracide (FLIP into the opponent's Deck; its draw trigger), Pharaoh's
                // Treasure (face-up into your Deck; its draw trigger), Graverobber (an opponent's Spell into your hand).
                EffectActionKind.PutThisFaceUpInOpponentDeck or
                    EffectActionKind.ShuffleThisFaceUpIntoYourDeck => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace =>
                    EffectResolutionKind.SpecialSummon,
                EffectActionKind.SendDrawnThisToGyThenAddGyTargetToHand or
                    EffectActionKind.AddOpponentGySpellToHandUsableThisTurn => EffectResolutionKind.Search,
                // SimpleTargetUtility batch (v173): Altar for Tribute's LP gain (Cost: send a monster you
                // control to the GY), Cost Down's hand Level stamps.
                EffectActionKind.GainLpEqualToCostAmount => EffectResolutionKind.GainLp,
                EffectActionKind.ReduceHandLevelsUntilEndOfTurn => EffectResolutionKind.ModifyStats,
                // v173: Des Feral Imp (GY card → Deck, shuffled) shares Forceful Sentry's kind — the
                // "return the chosen card to the Deck (shuffle)" resolution, previously unmapped.
                EffectActionKind.ReturnChosenToDeckShuffle => EffectResolutionKind.ReturnToDeck,
                // DamageStepRules batch: Ultimate Obedient Fiend's attack condition and Kishido Spirit's equal-ATK battle
                // protection are rule clauses; Meteorain's turn piercing reads as piercing (DirectAttack, as above);
                // Cat's Ear Tribe sets the battler's original ATK; Amazoness Swords Woman's reflected damage is damage the
                // controller does not take.
                EffectActionKind.CannotAttackUnlessOnlyCardOnFieldAndEmptyHand or
                    EffectActionKind.CannotBeDestroyedByBattleWithEqualAtkMonster => EffectResolutionKind.Protection,
                EffectActionKind.YourMonstersGainPiercingThisTurn => EffectResolutionKind.DirectAttack,
                EffectActionKind.OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep => EffectResolutionKind.ModifyStats,
                EffectActionKind.ReflectControllerBattleDamageFromBattlesInvolvingThis => EffectResolutionKind.PreventDamage,
                // DcrEquipSpells batch: Equip Spell grants (direct attack, second attack, fixed battle damage) and
                // Butterfly Dagger's return to the hand.
                EffectActionKind.EquippedCanAttackDirectly => EffectResolutionKind.DirectAttack,
                EffectActionKind.EquippedBattleDamageToOpponentBecomes => EffectResolutionKind.Damage,
                EffectActionKind.EquippedGainsExtraAttacks => EffectResolutionKind.ExtraAttack,
                EffectActionKind.ReturnThisFromGyToHand => EffectResolutionKind.ReturnToHand,
                // SpellCounterSpenders batch: Mandragola / Apprentice Magician / Pitch-Black Power Stone's counters,
                // Apprentice Magician's face-down Deck summon, Armor Exe's upkeep and Pitch-Black's self-destruction.
                EffectActionKind.PlaceSpellCounterOnEachFaceUpHolder or
                    EffectActionKind.PlaceSpellCounterOnTarget or
                    EffectActionKind.MoveSpellCounterFromThisToTarget => EffectResolutionKind.PlaceCounters,
                EffectActionKind.SpecialSummonLevelRaceFromDeckFaceDownDefense => EffectResolutionKind.SpecialSummon,
                EffectActionKind.RemoveSpellCounterOnYourSideOrDestroyThis or
                    EffectActionKind.DestroyThisWhenLastSpellCounterRemoved => EffectResolutionKind.Destroy,
                // ChainLinkAnswers batch: Jar Robber (negate the named activation's effect, then draw), Chaos Command
                // Magician (negate a monster effect that targets only this card), Dramatic Rescue (return the targeted
                // series monster, then Special Summon from the hand).
                EffectActionKind.NegateEffectOfNamedActivationThenDraw or
                    EffectActionKind.NegateMonsterEffectsThatTargetOnlyThis => EffectResolutionKind.NegateActivation,
                EffectActionKind.ReturnTargetedSeriesMonsterThenSpecialSummonFromHand => EffectResolutionKind.ReturnToHand,
                // FlipMassAndOpponentPicks batch: Dark Cat with White Tail, Huge Revolution, Magical Merchant, Pixie
                // Knight, Pineapple Blast, Dimension Jar.
                EffectActionKind.ReturnOpponentAndOwnTargetsToOwnersHands => EffectResolutionKind.ReturnToHand,
                EffectActionKind.SendOpponentHandToGyAndDestroyAllOpponentCards or
                    EffectActionKind.DestroyOpponentMonstersToMatchYourCountOpponentChooses => EffectResolutionKind.Destroy,
                EffectActionKind.ExcavateUntilSpellTrapAddItSendRestToGy => EffectResolutionKind.Search,
                EffectActionKind.OpponentPlacesSpellFromYourGyOnTopOfYourDeck => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.EachPlayerMayBanishUpToMonstersFromOpponentGy => EffectResolutionKind.Banish,
                // PhaseTriggersAndIgnitionCosts batch: Amazoness Blowpiper, Helping Robo for Combat, Senri Eye (a look,
                // like Inspection), Magical Scientist, Roulette Barrel, Physical Double.
                EffectActionKind.TargetOppFaceUpLosesAtkAtYourStandby => EffectResolutionKind.ModifyStats,
                EffectActionKind.DrawThenPlaceHandCardOnDeckBottom => EffectResolutionKind.Draw,
                EffectActionKind.PayLpLookAtOpponentTopDeckCardAtYourStandby => EffectResolutionKind.Randomize,
                EffectActionKind.SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase =>
                    EffectResolutionKind.SpecialSummon,
                EffectActionKind.RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel => EffectResolutionKind.Destroy,
                EffectActionKind.SpecialSummonTokenCopyingTargetDestroyAtEndPhase => EffectResolutionKind.Token,
                // GuardiansAndEquipEvents batch: Guardian Elma / Chopman equip a GY Equip Spell to themselves; Morale Boost's
                // equip / leave-field triggers.
                EffectActionKind.EquipTargetedGyEquipSpellToThis => EffectResolutionKind.Equip,
                EffectActionKind.GainLpWhenEquipSpellEquipped => EffectResolutionKind.GainLp,
                EffectActionKind.DamageWhenEquipSpellLeavesField => EffectResolutionKind.Damage,
                // NamedSummonPairs batch: Exodia Necross (Spell/Trap destruction protection, its GY-piece self-destruction),
                // Mirage Knight (damage-calculation ATK, End Phase banish).
                EffectActionKind.CannotBeDestroyedBySpellTrapEffects => EffectResolutionKind.Protection,
                EffectActionKind.SelfDestroyUnlessAllNamedInYourGy => EffectResolutionKind.Destroy,
                EffectActionKind.GainBattleTargetOriginalAtkDuringDamageCalculation => EffectResolutionKind.ModifyStats,
                EffectActionKind.BanishThisAtEndPhaseIfBattledThisTurn => EffectResolutionKind.Banish,
                // SpellTrapAnswersAndActivationLocks batch: the riders of Counter Trap answers (Cursed Seal's name lock, Spell
                // Vanishing's same-name send from the opponent's hand and Deck, Judgment of Anubis's destroy-and-burn),
                // Sasuke Samurai #2's turn-long Spell/Trap lock and Invader of Darkness's Quick-Play lock.
                EffectActionKind.LockOpponentActivatingNegatedSpellNameForDuel or
                    EffectActionKind.NeitherPlayerActivatesSpellTrapsThisTurn or
                    EffectActionKind.ContinuousOpponentCannotActivateQuickPlaySpells => EffectResolutionKind.Protection,
                EffectActionKind.SendNegatedCardNameFromOpponentHandAndDeckToGy => EffectResolutionKind.Discard,
                EffectActionKind.MayDestroyOpponentFaceUpMonsterInflictItsAtk => EffectResolutionKind.Destroy,
                // EndOfDamageStepRiders batch: end-of-Damage-Step riders on the battled monster (Getsu Fuhma / Ryu Kokki
                // destroy, Prickle Fairy's position change, Legendary Jujitsu Master's top-of-Deck), Prickle Fairy's attack
                // shield, Insect Princess's Attack Position rule and Drillago's conditional direct attack.
                EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep => EffectResolutionKind.Destroy,
                EffectActionKind.ChangeBattledMonsterToDefenseAtEndOfDamageStep or
                    EffectActionKind.ContinuousChangeOpponentRaceMonstersToAttack => EffectResolutionKind.ChangePosition,
                EffectActionKind.OpponentCannotAttackYourFaceUpRaceMonsters => EffectResolutionKind.Protection,
                EffectActionKind.CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast =>
                    EffectResolutionKind.DirectAttack,
                EffectActionKind.PlaceAttackerOnTopOfDeckAtEndOfDamageStep => EffectResolutionKind.ReturnToDeck,
                // RecoveryAndPhaseCosts batch: Dimension Distortion, Dimension Fusion, Recycle, Reload, Coach Goblin, Manticore
                // of Darkness.
                EffectActionKind.SpecialSummonBanishedTarget or
                    EffectActionKind.EachPlayerSpecialSummonsFaceUpBanishedMonstersAsPossible or
                    EffectActionKind.SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase => EffectResolutionKind.SpecialSummon,
                EffectActionKind.PayLpReturnGyNonMonsterToDeckBottomAtYourStandby => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.ShuffleHandIntoDeckThenDrawSameNumber or
                    EffectActionKind.ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase or
                    EffectActionKind.ReturnHandLevelMonsterToDeckShuffleThenDraw => EffectResolutionKind.Draw,
                // ChaosBanishSummons batch: Chaos Sorcerer / Black Luster Soldier - Envoy of the Beginning, Chaosrider Gustaph,
                // Soul Absorption, Primal Seed, Strike Ninja.
                EffectActionKind.BanishTargetedFieldMonster or
                    EffectActionKind.BanishUpToSpellsFromYourGyGainAtkEach or
                    EffectActionKind.BanishThisUntilEndPhase => EffectResolutionKind.Banish,
                EffectActionKind.MakeSecondAttackInARow => EffectResolutionKind.ExtraAttack,
                EffectActionKind.GainLpPerCardBanished => EffectResolutionKind.GainLp,
                EffectActionKind.AddBanishedTargetsToHand => EffectResolutionKind.ReturnToHand,
                // BattleAndPositionRecords batch: Atomic Firefly, Disc Fighter, Ghost Knight of Jackal, Legacy Hunter, The
                // Unhappy Girl (protection + battle-partner locks), Judgment of the Desert (lock), Curse of Anubis.
                EffectActionKind.InflictDamageToPlayerWhoDestroyedThis => EffectResolutionKind.Damage,
                EffectActionKind.DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation => EffectResolutionKind.Destroy,
                EffectActionKind.SpecialSummonBattleDestroyedMonsterToYourFieldInDefense => EffectResolutionKind.SpecialSummon,
                EffectActionKind.OpponentReturnsRandomHandCardToDeckShuffle => EffectResolutionKind.ReturnToDeck,
                EffectActionKind.CannotBeDestroyedByBattleWhileInFaceUpAttackPosition or
                    EffectActionKind.OpponentMonstersThatBattledThisCannotAttackOrChangePosition or
                    EffectActionKind.MonstersFlippedFaceUpAfterActivationCannotChangePosition => EffectResolutionKind.Protection,
                EffectActionKind.ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock => EffectResolutionKind.ChangePosition,
                EffectActionKind.None => EffectResolutionKind.None,
                _ => EffectResolutionKind.UniqueException
            };
        }

        public static string Describe(EffectClause c)
        {
            if (c == null) return "none";
            var cost = CostOf(c);
            var res = ResolutionOf(c);
            var costBit = cost == EffectCostKind.None ? "" : cost + " → ";
            return $"{c.Timing}: {costBit}{res}";
        }

        public static bool IsSharedKind(EffectClause c)
        {
            if (c == null || c.Action == EffectActionKind.None)
                return false;
            var r = ResolutionOf(c);
            return r != EffectResolutionKind.None && r != EffectResolutionKind.UniqueException;
        }

        /// <summary>
        /// How to grow the engine when a new card shows up.
        /// </summary>
        public static string NewCardRule =>
            "1) Name the timing, cost, and resolution from EffectVocabulary. " +
            "2) If all three exist, compile a PSCT fragment — do not add an Action. " +
            "3) If a kind is missing and 2+ cards need it, add one shared kind + a test. " +
            "4) If the mechanic is unique, add UniqueException (fail loud). " +
            "Never if (cardId == …) unless no other card shares it.";

        public static List<string> KindsUsed(CompiledCardProgram prog)
        {
            var list = new List<string>();
            if (prog == null) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in prog.ClauseList)
            {
                if (c == null) continue;
                var d = Describe(c);
                if (seen.Add(d)) list.Add(d);
            }

            return list;
        }
    }
}
