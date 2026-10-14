using System;
using System.Collections.Generic;
using System.Linq;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>When an effect clause is checked / applied (PSCT timing).</summary>
    public enum EffectTiming
    {
        None = 0,
        /// <summary>Normal Spell/Trap activation (free chain or open game state).</summary>
        Activate,
        /// Response to an already activated Chain Link.
        ChainLinkActivated,
        /// <summary>FLIP: …</summary>
        Flip,
        /// <summary>If this card is sent from the field to the GY: …</summary>
        SentFromFieldToGy,
        /// <summary>When an opponent's monster declares an attack: …</summary>
        AttackDeclared,
        /// <summary>When your opponent Normal or Flip Summons …</summary>
        OpponentNormalOrFlipSummon,
        /// <summary>Continuous while face-up on field.</summary>
        ContinuousWhileFaceUp,
        /// <summary>During damage calculation (hand trap style).</summary>
        DamageCalculation,
        /// <summary>This card was Normal, Flip, or Special Summoned (Granadora).</summary>
        ThisCardSummoned,
        /// <summary>During the controller's Standby Phase (Lava Golem, LV send, Bowganian).</summary>
        StandbyPhase,
        /// <summary>During the End Phase (Spirit bounce, LV after battle, Insect Queen token).</summary>
        EndPhase,
        /// <summary>
        /// When you take damage to your Life Points (Numinous Healer / Attack and Receive).
        /// </summary>
        YouTakeLifePointDamage,
        /// <summary>This card destroyed an opponent's monster by battle (Fenrir skip-draw).</summary>
        ThisCardDestroysByBattle,
        /// <summary>
        /// This card inflicted battle damage to the opponent (Spirit Reaper / White Magical Hat).
        /// Fires after LP actually went down. RequiresDirectAttack skips non-direct battles.
        /// </summary>
        ThisCardInflictsBattleDamage,
        /// <summary>
        /// After damage calculation (Wall of Illusion bounce / D.D. Warrior Lady banish-both).
        /// Fires after LP damage is applied, before battle destruction.
        /// </summary>
        AfterDamageCalculation,
        /// <summary>
        /// End of the Damage Step (Hyper Hammerhead: bounce if battled monster survived).
        /// Fires after battle destructions have been applied.
        /// </summary>
        EndOfDamageStep,
        /// <summary>
        /// Start of the Damage Step, before the face-down flip and before damage
        /// calculation (Sasuke Samurai / Mystic Swordsman / Paladin of White Dragon).
        /// Appended — EffectTiming is serialized as an int in the compiled cache.
        /// </summary>
        StartOfDamageStep,
        /// <summary>
        /// This card's battle position changed (manual, Flip Summon, or an effect).
        /// Appended — EffectTiming is serialized as an int.
        /// </summary>
        PositionChanged,
        /// <summary>
        /// This card declared an attack. Distinct from AttackDeclared, which is
        /// an opponent's attack. Appended — EffectTiming is serialized as an int.
        /// </summary>
        ThisCardDeclaresAttack,

        // ---- v103 merge: GitHub-lineage (v51-v55) members appended after the local v100-v102 members. ----
        // Serialized as ints in compiled programs; CardTextEffectCompiler.Version 103 invalidates every older program.
        /// <summary>
        /// A monster the controller controls inflicted battle damage to the opponent
        /// (Robbin' Goblin — continuous Trap "each time").
        /// </summary>
        YourMonsterInflictsBattleDamage,
        /// <summary>
        /// This card's battle position changed. <see cref="EffectClause.PositionChangeToDefense"/>
        /// true = Attack → face-up Defense (Dream Clown / Tainted Wisdom); false = Defense →
        /// Attack, including a Flip Summon (Crass Clown).
        /// </summary>
        ThisCardPositionChanged,
        /// <summary>The controller drew a card or cards — once per draw action (Solemn Wishes).</summary>
        ControllerDraws,
        /// <summary>Control of this face-up card changed (Ameba / Griggle). Resolved for the NEW controller.</summary>
        ThisCardControlChanged,
        /// <summary>
        /// Opponent conducted their normal draw in the Draw Phase (Drop Off).
        /// Distinct from <see cref="ControllerDraws"/> (Solemn Wishes — any draw, controller's face-up cards).
        /// Fail-closed vs Time Seal (skip Draw), effect draws outside the Draw Phase, and mass hand wipe.
        /// </summary>
        OpponentNormalDrawInDrawPhase,
        /// <summary>
        /// Controller's Draw Phase normal draw was a Normal Monster(s) (Heart of the Underdog).
        /// Distinct from <see cref="ControllerDraws"/> (any draw) and
        /// <see cref="OpponentNormalDrawInDrawPhase"/> (Drop Off — opponent).
        /// Fail-closed vs opp draw, Effect Monster draws, Jar / Card Destruction reload any-draw.
        /// </summary>
        ControllerNormalDrawInDrawPhase,
        /// <summary>
        /// Card batch PerCountBurns (v141): a Main Phase ignition effect of this face-up Spell/Trap Card,
        /// activated from the field after the card itself is active (Wave-Motion Cannon). The card's own
        /// activation never resolves it; see <see cref="PerCountBurnsRuntime.TryFaceUpFieldIgnition"/>.
        /// </summary>
        FaceUpFieldIgnition,
        /// <summary>
        /// SummonNegation batch (v141): "When a monster(s) would be Summoned" (Solemn Judgment / Horn of
        /// Heaven). The summon-negation window: the monster is in its zone but its Normal / Tribute / Flip
        /// Summon (or Special Summon by procedure) has not succeeded yet (DuelEngine
        /// ResponseTiming.SummonNegation). Appended — EffectTiming is serialized as an int.
        /// </summary>
        MonsterWouldBeSummoned,
        /// <summary>
        /// DiscardTriggers batch (v142): "When this card is discarded [directly] from your hand to the GY by an
        /// effect of a card controlled by your opponent" (Electric Snake, Minar). A mandatory trigger of the
        /// discarded card, resolved for its owner once the discarding effect has finished resolving. Never for a
        /// cost, the hand size rule, or an effect of a card its owner controls (Card Destruction they activate).
        /// ygopro: previous location hand, REASON_DISCARD|REASON_EFFECT, reason player = the owner's opponent.
        /// Runtime: <see cref="DiscardTriggersRuntime"/>.
        /// </summary>
        DiscardedFromHandByOpponentEffect,
        /// <summary>
        /// DiscardTriggers batch (v142): "When this card is sent directly from your Deck to the GY by an effect of
        /// a card controlled by your opponent" (Penguin Knight): milled, or destroyed in the Deck (Chain
        /// Destruction). Mandatory, resolved for the card's owner once that effect has finished resolving; never
        /// for a cost (Gravekeeper's Servant) or its owner's own effects. ygopro: previous location Deck,
        /// REASON_EFFECT, reason player = the owner's opponent. Runtime: <see cref="DiscardTriggersRuntime"/>.
        /// </summary>
        SentFromDeckToGyByOpponentEffect,
        /// <summary>
        /// BattlePhaseEnd batch (v142): "At the end of the Battle Phase" — the End Step of every Battle Phase,
        /// either player's, however it ends (Main Phase 2, a card that ends the Battle Phase, or ending the turn
        /// from Battle): DuelEngine.EndBattlePhase runs <see cref="BattlePhaseEndRuntime.FireEndOfBattlePhase"/>
        /// once for the face-up monsters on the field (Goblin Attack Force, Giant Orc).
        /// Appended — EffectTiming is serialized as an int.
        /// </summary>
        EndOfBattlePhase,
        /// <summary>
        /// BattlePhaseEnd batch (v142): "Before damage calculation" of a battle this card is in, after the Damage
        /// Calculation response window and before damage is calculated
        /// (<see cref="BattlePhaseEndRuntime.FireBeforeDamageCalculation"/>, Reflect Bounder). Appended —
        /// EffectTiming is serialized as an int.
        /// </summary>
        BeforeDamageCalculation,
        /// <summary>
        /// FaceUpQuickEffects batch (v160): a Quick Effect (Spell Speed 2) of this face-up Continuous Spell/Trap Card,
        /// activated from its Spell &amp; Trap Zone after the card itself is active (ygopro EFFECT_TYPE_QUICK_O with
        /// SetRange(LOCATION_SZONE): Type Zero Magic Crusher, Skull Lair). Usable on either player's turn in the open
        /// game state, in the opponent's open-state / attack / summon windows and as a chain response; never in the
        /// Damage Step. The card's own activation never resolves it; costs are paid when it is activated and it is
        /// a card-effect Chain Link (<see cref="FaceUpQuickEffectsRuntime"/>). Appended — EffectTiming is serialized
        /// as an int.
        /// </summary>
        FaceUpQuickEffect,
        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161): "If this face-up card is returned from the field to your Deck"
        /// (Serpentine Princess; ygopro EVENT_TO_DECK with IsPreviousPosition(POS_FACEUP) and
        /// IsPreviousLocation(LOCATION_ONFIELD)). Raised by the one field-to-Deck path, DuelEngine.ReturnCardToDeck
        /// (<see cref="DeckReturnsAndMassEffectsRuntime.NoteReturnedToDeck"/>), and queued as a mandatory trigger on the
        /// simultaneous-trigger Chain (<see cref="SimultaneousTriggersRuntime"/>). Appended — EffectTiming is serialized
        /// as an int.
        /// </summary>
        ReturnedFromFieldToDeck,
        /// <summary>
        /// BattleBoosts batch (v161): a Quick Effect (Spell Speed 2) of this face-up monster, used from the Monster
        /// Zone in a battle window only: Injection Fairy Lily at damage calculation of a battle it is in (either
        /// player's turn), Spirit Ryu in its controller's Battle Step while it is the attacker or the attack target.
        /// Legality, costs and resolution live in <see cref="BattleBoostsRuntime"/>
        /// (<see cref="BattleBoostsRuntime.CanActivateQuick"/>). Appended — EffectTiming is serialized as an int.
        /// </summary>
        MonsterQuickEffect,
        /// <summary>
        /// TargetNegationsAndHandQuickEffects batch (v163): a free-chain Quick Effect (Spell Speed 2) activated from the
        /// hand (Charm of Shabti, ygopro QUICK_O + SetRange(LOCATION_HAND) on EVENT_FREE_CHAIN). Legality, the cost, the
        /// Chain Link and the resolution live in <see cref="TargetNegationsAndHandQuickEffectsRuntime"/>
        /// (<see cref="TargetNegationsAndHandQuickEffectsRuntime.CanActivateHandQuick"/>). Appended — serialized as an int.
        /// </summary>
        HandQuickEffect,
        /// <summary>
        /// PerCopyDeck batch (v163): "When your opponent draws this card" (Parasite Paracide) / "If you draw this face-up
        /// card that was added to the Deck by this effect" (Pharaoh's Treasure). The trigger the card's own effect
        /// registered on the copy it put face-up in a Deck (ygopro EVENT_DRAW, TRIGGER_F, registered by that operation with
        /// RESETS_STANDARD minus RESET_TOHAND): only that copy (<see cref="DeckCopyInfo.DrawTrigger"/>) has it, and only a
        /// draw fires it. Queued on the simultaneous-trigger Chain by <see cref="PerCopyDeckRuntime.NoteDrawn"/>.
        /// Appended — EffectTiming is serialized as an int.
        /// </summary>
        DrawnFaceUpFromDeck,
        /// <summary>
        /// EquippedMonsterDestroysByBattle batch (v167 Union): the equipped monster would be destroyed by battle
        /// or card effect, so destroy this Union equip instead. The runtime reads the raw text for the trigger
        /// and fires this timing on the equip card.
        /// </summary>
        EquippedMonsterDestroysByBattle,
        /// <summary>
        /// UnionRiders batch (v175): a Union in its Union state (equipped by its own Union effect, not absorbed) whose
        /// equipped monster just inflicted battle damage to the Union controller's opponent (ygopro EVENT_BATTLE_DAMAGE,
        /// SetRange(LOCATION_SZONE), aux.IsUnionState, eg:GetFirst()==GetEquipTarget(); Burning Beast c59364406,
        /// Freezing Beast c85359414). Fired by <see cref="UnionRidersRuntime.FireEquippedMonsterInflictedBattleDamage"/>.
        /// Appended — EffectTiming is serialized as an int.
        /// </summary>
        EquippedMonsterInflictsBattleDamage,
        /// <summary>
        /// UnionRiders batch (v175): an ignition effect of a Union activated from the Spell &amp; Trap Zone while it is in
        /// its Union state (ygopro EFFECT_TYPE_IGNITION, SetRange(LOCATION_SZONE), aux.IsUnionState; Kiryu c84814897,
        /// Second Goblin c19086954). Activated by tapping the equipped Union; when its own unequip is also usable the
        /// player chooses (<see cref="UnionRidersRuntime.TryActivate"/>). Appended — EffectTiming is serialized as an int.
        /// </summary>
        UnionStateIgnition,
        /// <summary>
        /// SimpleTriggersAndSwarm batch: "When this card is sent to the GY" from ANY location — the hand, the Deck or the
        /// field (ygopro SINGLE+TRIGGER_F on EVENT_TO_GRAVE with no previous-location check; Burning Algae c41859700,
        /// Outstanding Dog Marron c11548522). Every GY entry stamps the card (<see cref="GyEntryStamps.Entered"/>), which
        /// queues it; the queue fires once play is idle (<see cref="SimpleTriggersAndSwarmRuntime.FireQueuedSentToGy"/>,
        /// DuelEngine.Notify), for the card's owner, while the card is still in that GY on the same trip. Not
        /// <see cref="SentFromFieldToGy"/>, which only a card leaving the field fires. Appended — EffectTiming is
        /// serialized as an int.
        /// </summary>
        SentToGyFromAnywhere,
        /// <summary>
        /// ArchfiendRiders batch: a Quick Effect (Spell Speed 2) activated from the hand right after a monster named
        /// <see cref="EffectClause.NamedCard"/> its activator controlled was destroyed and sent to the GY (ygopro QUICK_O on
        /// EVENT_TO_GRAVE, EFFECT_FLAG_DAMAGE_STEP; Desrook Archfiend c72192100). Offered in the windows that answer that
        /// send (<see cref="BattleWindowsRuntime.AnsweredSends"/>); <see cref="ArchfiendRidersRuntime.TryActivate"/> seats
        /// the link. Appended — EffectTiming is serialized as an int.
        /// </summary>
        HandQuickEffectWhenNamedDestroyed,
        /// <summary>
        /// SpellEventsAndHandSendTriggers batch: this card was sent from its owner's hand to the GY by an effect of a card
        /// its owner's opponent controls — discarded, or sent / destroyed there (ygopro SINGLE TRIGGER_F on EVENT_TO_GRAVE,
        /// IsPreviousLocation(LOCATION_HAND), rp~=tp, REASON_EFFECT: Elephant Statue of Blessing / of Disaster, Regenerating
        /// Mummy). Queued by <see cref="DiscardTriggersRuntime"/> (NoteDiscard and NoteSentFromHand), fired once the moving
        /// effect is over. Not <see cref="DiscardedFromHandByOpponentEffect"/>, which only a discard fires. Appended —
        /// EffectTiming is serialized as an int.
        /// </summary>
        SentFromHandToGyByOpponentEffect,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch: this card was sent from its owner's hand to the GY for any reason — a cost,
        /// a discard, an effect send, either player's (ygopro SINGLE TRIGGER_F on EVENT_TO_GRAVE, IsPreviousLocation(
        /// LOCATION_HAND): Night Assailant). Queued by <see cref="GyHandAndSummonEventTriggersRuntime.NoteEnteredGy"/> from the
        /// GY-entry stamp of a hand trip, fired once play is idle. Appended — EffectTiming is serialized as an int.
        /// </summary>
        SentFromHandToGy,
    }

    public enum EffectActionKind
    {
        None = 0,
        Draw,
        Destroy,
        SpecialSummonFromGy,
        SpecialSummonFromHand,
        /// <summary>Special Summon a filtered monster from the controller's Deck.</summary>
        SpecialSummonFromDeck,
        AddFromGyToHand,
        AddFromDeckToHand,
        ChangeBattlePosition,
        NegateAttack,
        /// Negate the activation of the current Chain Link.
        NegateActivation,
        EndBattlePhase,
        ApplyWabokuStyle,
        ApplySwordsOfRevealingLight,
        /// <summary>
        /// Both discard hand then redraw. Amount 0 = redraw discarded count (Card Destruction);
        /// Amount &gt; 0 = each draws that many (Morphing Jar Flip).
        /// </summary>
        BothPlayersDiscardAndRedraw,
        FusionSummonRegistered,
        EffectDamageBothFromOriginalAtk,
        CyberJarStyle,
        ContinuousCannotTargetDragons,
        /// <summary>Discard this card from hand; take no battle damage from that battle (Kuriboh).</summary>
        DiscardSelfNoBattleDamageThisBattle,
        /// <summary>Discard Amount random cards from the opponent's hand (empty hand = no-op).</summary>
        DiscardRandomFromOpponentHand,
        /// <summary>While face-up: matching monsters gain Amount ATK and DefAmount DEF.</summary>
        ContinuousGainAtkDef,
        /// <summary>While face-up: matching monsters lose Amount Levels (hand and/or field).</summary>
        ContinuousReduceLevel,
        /// <summary>Rule condition: this card's name is always treated as TreatedAsName.</summary>
        AlwaysTreatedAsName,
        /// <summary>Return the targeted card from the field to its owner's hand.</summary>
        ReturnToHand,
        /// <summary>
        /// Continuous: this card can attack directly. Optional RequiresFaceUpName
        /// (e.g. "Umi") must be face-up on the field.
        /// </summary>
        CanAttackDirectly,
        /// <summary>
        /// Continuous: this card may declare Amount extra attacks (1 = attack twice).
        /// Optional RequiresFaceUpName (Umi).
        /// </summary>
        ExtraAttacks,
        /// <summary>Until End Phase: target loses Amount ATK (and DefAmount DEF if DefAmount != 0, or paid LP if RequiresLpCostMultiple).</summary>
        LoseAtkDefUntilEndOfTurn,
        /// <summary>Target (or this) gains Amount ATK / DefAmount DEF until the End Phase.</summary>
        GainAtkDefUntilEndOfTurn,
        /// <summary>Negate the current attack only (do not end the Battle Phase).</summary>
        NegateThisAttack,
        /// <summary>Inflict effect damage equal to the target's current ATK.</summary>
        InflictDamageEqualToAtk,
        /// <summary>Gain LP equal to the target's current ATK.</summary>
        GainLpEqualToAtk,
        /// <summary>Banish matching monsters (Dark Mirror Force).</summary>
        Banish,
        /// <summary>Halve the summoned monster's original ATK (Adhesion Trap Hole). Lingering.</summary>
        HalveOriginalAtk,
        /// <summary>Destroy all Tokens, then inflict Amount damage per destroyed Token.</summary>
        DestroyTokensInflictPer,
        /// <summary>Return all face-up Fusion Monsters to the Extra Deck.</summary>
        ReturnAllFaceUpFusionsToExtra,
        /// <summary>Banish the summoned monster, then opponent same-name from hand and Deck.</summary>
        BanishThenSameNameFromOppHandDeck,
        /// <summary>Destroy same-name copies in the summoned monster's controller's hand and Deck.</summary>
        DestroySameNameInControllerHandAndDeck,
        /// <summary>Destroy opponent's face-up Attack Position monsters, then inflict Amount if any died.</summary>
        DestroyOppAttackThenDamage,
        /// <summary>Destroy all Equip Cards on the field (Disarmament).</summary>
        DestroyAllEquips,
        /// <summary>Destroy all monsters that have an Equip Card (Eternal Rest).</summary>
        DestroyAllEquippedMonsters,
        /// <summary>While face-up (and no Field Spell): the field is treated as TreatedAsName (Maiden of the Aqua / Umi).</summary>
        FieldTreatedAsName,
        /// <summary>Controller takes no battle damage while this card is face-up (Tornado Wall).</summary>
        PreventControllerBattleDamage,
        /// <summary>Destroy this card unless RequiresFaceUpName is still on the field.</summary>
        SelfDestroyUnlessNamedFaceUp,
        /// <summary>Controller gains Amount LP.</summary>
        GainLifePoints,
        /// <summary>While face-up: opponent LP gains become equal effect damage.</summary>
        ConvertOpponentLpGainToDamage,
        /// <summary>Mandatory upkeep: the controller pays Amount LP.</summary>
        PayLifePoints,
        /// <summary>Controller takes Amount effect damage.</summary>
        TakeEffectDamage,
        /// <summary>Inflict Amount effect damage to the opponent.</summary>
        InflictDamageToOpponent,
        /// <summary>Inflict damage equal to half the tributed monster's ATK (Catapult Turtle).</summary>
        InflictDamageHalfTributedAtk,
        /// <summary>Change this card to face-down Defense Position (Des Lacooda).</summary>
        SetThisFaceDownDefense,
        /// <summary>Change this card's battle position.</summary>
        ChangeThisBattlePosition,
        /// <summary>This card can attack directly this turn (Gear Golem).</summary>
        GrantDirectAttackThisTurn,
        /// <summary>This card gains Amount ATK until the End Phase. ScaleAmountByCostCount uses paid cost count.</summary>
        GainThisAtkUntilEnd,
        /// <summary>Special Summon a card whose printed name is NamedCard from hand/deck/GY flags.</summary>
        SpecialSummonNamed,
        /// <summary>Add up to Amount copies of NamedCard from Deck to hand.</summary>
        AddNamedFromDeckToHand,
        /// <summary>Destroy all Special Summoned monsters on the field (Jowgen).</summary>
        DestroySpecialSummonedMonsters,
        /// <summary>Destroy opponent's monsters with ATK ≤ CostNumeric (Armed Dragon LV7).</summary>
        DestroyOppMonstersAtkLeq,
        /// <summary>Call a coin; right = destroy all opp monsters, wrong = destroy yours + half ATK damage (Time Wizard).</summary>
        CoinCallDestroyOppOrSelf,
        /// <summary>Call a coin; right = double this ATK this turn, wrong = halve (Goddess of Whim).</summary>
        CoinCallDoubleOrHalveAtk,
        /// <summary>Toss CoinCount coins; destroy the target if heads ≥ Amount.</summary>
        CoinTossNDestroyIfHeads,
        /// <summary>Roll a six-sided die (Zorc-style branches via DieLowMax / DieMidMax).</summary>
        RollDieZorc,
        /// <summary>Special Summon a Token described on the clause.</summary>
        SpecialSummonToken,
        /// <summary>Special Summon 1 Fusion Monster from the Extra Deck.</summary>
        SpecialSummonFusionFromExtra,
        /// <summary>Take control of all face-up opponent monsters with Level ≤ Amount.</summary>
        TakeControlLevelLeq,
        /// <summary>Change of Heart family: take control of the targeted monster until the End Phase.</summary>
        TakeControlUntilEndPhase,
        /// <summary>Equip this card to the targeted monster (Union).</summary>
        EquipThisToTarget,
        /// <summary>Unequip this card and Special Summon it (Union).</summary>
        UnequipThisSpecialSummon,
        /// <summary>Equip the targeted monster to this card (Relinquished).</summary>
        EquipTargetToThis,
        /// <summary>Place Amount Spell Counters on this card (max CounterMax).</summary>
        PlaceSpellCounters,
        /// <summary>While face-up: this card gains Amount ATK per Spell Counter.</summary>
        GainAtkPerSpellCounter,
        /// <summary>Continuous: this card cannot be targeted for attacks. Optional RequiresFaceUpName.</summary>
        CannotBeAttackTarget,
        /// <summary>Continuous: unaffected by UnaffectedByFilter (Spell / Trap / Monster).</summary>
        UnaffectedByCardEffects,
        /// <summary>Continuous: this card (or NamedCard / RaceFilter monsters) cannot be targeted by effects.</summary>
        CannotBeTargetedByEffects,
        /// <summary>Book of Moon: target becomes face-down Defense Position.</summary>
        SetTargetFaceDownDefense,
        /// <summary>
        /// Absolute End family: this turn, the opponent's monster attacks become direct attacks.
        /// </summary>
        ForceOpponentDirectAttacksThisTurn,
        /// <summary>Suijin family: attacking monster's ATK becomes 0 for this damage calculation only.</summary>
        SetAttackingMonsterAtkToZeroThisCalc,
        /// <summary>Nomi spirit: Special Summon this card from the hand after paying the GY-banish cost.</summary>
        SpecialSummonThisFromHand,
        /// <summary>Fenrir: opponent skips their next Draw Phase.</summary>
        SkipOpponentNextDrawPhase,
        /// <summary>
        /// Controller skips next Amount Draw Phase(s) (Offerings N=1, Reckless Greed N=2).
        /// Distinct from <see cref="SkipOpponentNextDrawPhase"/> — controller-side only.
        /// </summary>
        SkipControllerNextDrawPhases,

        /// <summary>
        /// Set 1 Spell/Trap from the controller's hand (Dust Tornado optional rider).
        /// Hand only — not Deck/GY; Spell/Trap only — not monsters. Optional via IsOptional.
        /// </summary>
        SetSpellTrapFromHand,
        /// <summary>Threatening Roar family: opponent cannot declare an attack this turn.</summary>
        PreventOpponentAttacksThisTurn,
        /// <summary>Place this card on top of the controller's Deck (Axe / Horn of the Unicorn GY).</summary>
        PlaceThisOnTopOfDeck,
        /// <summary>
        /// While face-up: matching monsters cannot declare an attack
        /// (Gravity Bind / Insect Barrier / Messenger of Peace).
        /// </summary>
        ContinuousCannotAttack,
        /// <summary>Standby: pay PayLpAmount or destroy this card (Messenger of Peace).</summary>
        PayLpOrDestroyThis,
        /// <summary>
        /// This card or the chosen target gains Amount ATK and DefAmount DEF lingering
        /// while it remains on the field (Slate Warrior Flip / destroyer loss).
        /// </summary>
        ApplyLingeringAtkDef,
        /// <summary>Send the top Amount cards of Side's Deck to the GY (Needle Worm).</summary>
        SendFromTopOfDeckToGy,
        /// <summary>
        /// This face-up card cannot be Tributed for a Tribute Summon (Fox Fire).
        /// Honored via CardInstance.CannotBeTributedForSummon.
        /// </summary>
        CannotBeTributedForSummon,
        /// <summary>
        /// Ritual Spell: Tribute hand/field monsters (Greater or Equal Level) and
        /// Special Summon the named / attribute Ritual Monster from the hand.
        /// </summary>
        RitualSummon,
        /// <summary>
        /// After a targeting card effect resolves, if this face-up field card was a
        /// target of that effect, destroy it (Spirit Reaper / Reaper on the Nightmare).
        /// </summary>
        DestroyThisAfterResolvingTargetingEffect,
        /// <summary>
        /// Continuous: this card cannot be destroyed by battle (Spirit Reaper).
        /// Honored via CardInstance.CannotBeDestroyedByBattle + OfficialEffectRegistry.
        /// </summary>
        CannotBeDestroyedByBattle,
        /// <summary>
        /// Continuous: this card (or the equipped monster) inflicts piercing battle damage.
        /// Airknight Parshath / Fairy Meteor Crush family. Honored via CardInstance.HasPiercing.
        /// </summary>
        PiercingBattleDamage,
        /// <summary>
        /// Neither player can banish cards from the GYs (Necrovalley). Shared GY lock.
        /// </summary>
        CannotBanishFromGraveyard,
        /// <summary>
        /// Cards in the GY cannot be targeted. ExceptNamedCard may allow that card's
        /// effects (Necrovalley except by the effect of "Necrovalley").
        /// </summary>
        CannotTargetCardsInGraveyard,
        /// <summary>Magical Hats: SS 2 Deck S/T as 0/0 face-down monsters and shuffle with a Set MMZ monster.</summary>
        MagicalHatsStyle,
        /// <summary>
        /// Virus family (Crush Card / Deck Devastation): tribute DARK ATK gate,
        /// look at opp field+hand (+draws if lingering), destroy by ATK threshold.
        /// </summary>
        CrushCardVirusStyle,
        /// <summary>Dark Sage: tribute Dark Magician immediately after Time Wizard right-call.</summary>
        DarkSageStyle,
        /// <summary>Exodia: win if this card plus the printed named pieces are all in the hand.</summary>
        ExodiaWinStyle,
        // ── Append-only below. Clause Action/Timing/Zone are serialized as ints in
        // the seed and the persistent compiled cache, so inserting a member mid-enum
        // silently re-maps every cached clause after it. Always append. ──
        /// <summary>
        /// Place the chosen target on top of its owner's Deck (A Feather of the Phoenix /
        /// Phoenix Wing Wind Blast / Back to Square One). GY, hand or field target.
        /// </summary>
        PlaceTargetOnTopOfDeck,
        /// <summary>Destroy every Spell/Trap the opponent controls (Dark Magic Attack).</summary>
        DestroyAllOppSpellTraps,
        /// <summary>Cheerful Coffin: send the chosen hand monster to the GY. Amount is the "up to" cap.</summary>
        DiscardUpToMonstersFromHand,
        /// <summary>Share the Pain: the opponent Tributes 1 monster they control. No summon.</summary>
        OpponentTributesOneMonster,
        /// <summary>Shield &amp; Sword: face-up monsters on the field swap original ATK and DEF until the End Phase.</summary>
        SwapOriginalAtkDefUntilEndOfTurn,
        /// <summary>
        /// Insect Soldiers / Penumbral Soldier Lady: Amount ATK during the Damage Step
        /// while the other battler is AttributeFilter. DamageStepOnlyWhenAttacking
        /// limits it to this card's own attacks.
        /// </summary>
        DamageStepAttributeAtk,
        /// <summary>Tainted Wisdom: shuffle the controller's Deck. Not a search.</summary>
        ShuffleControllerDeck,
        /// <summary>Yado Karu: place any number of hand cards on the bottom of the Deck.</summary>
        PlaceHandCardsOnBottomOfDeck,
        /// <summary>Dark Elf: pay Amount LP to declare an attack. Not effect damage.</summary>
        PayLpToDeclareAttack,
        /// <summary>Jirai Gumo: call a coin on this card's attack. A wrong call loses half LP.</summary>
        CallCoinLoseHalfLpIfWrong,
        /// <summary>
        /// Electric Lizard: a monster of a race other than ExcludeRaceFilter that
        /// attacked this card cannot attack on its controller's next turn.
        /// </summary>
        SealNonRaceAttackerNextTurn,
        /// <summary>
        /// Dragon Piper: destroy every face-up copy of NamedCard. If any were
        /// destroyed, change every face-up RaceFilter monster to Attack Position.
        /// </summary>
        DestroyAllFaceUpNamedThenForceRaceAttack,
        /// <summary>Big Eye: look at up to Amount cards on top of the Deck and reorder them.</summary>
        ReorderTopOfOwnDeck,
        /// <summary>Mushroom Man #2: optional End Phase LP pay, then control shifts and stays.</summary>
        OptionalPayLpShiftControl,
        /// <summary>Ring of Magnetism: an opponent's monster can only attack the equipped monster.</summary>
        AttacksMustTargetEquippedHost,
        /// <summary>Mirage Dragon: the opponent cannot activate Traps during the Battle Phase.</summary>
        OpponentCannotActivateTrapsDuringBattle,
        /// <summary>Invasion of Flames: Traps cannot answer this Normal Summon.</summary>
        SealTrapsAnsweringThisSummon,
        /// <summary>
        /// Double Coston / The Trojan Horse family: when Tributed for the Tribute Summon of an
        /// AttributeFilter monster, this card counts as 2 Tributes. Consulted by the Tribute
        /// Summon procedure only; never an activation.
        /// </summary>
        CountsAsTwoTributesForAttribute,
        /// <summary>Exhausting Spell: every Spell Counter on both fields is removed.</summary>
        RemoveAllSpellCounters,
        /// <summary>
        /// Disturbance Strategy / Heavy Slump: the opponent shuffles their whole hand into the
        /// Deck, then draws Amount cards (Amount 0 = the same number they shuffled).
        /// </summary>
        OpponentShufflesHandThenDraws,
        /// <summary>Gravekeeper's Servant: the opponent mills Amount to declare an attack (continuous).</summary>
        MillToDeclareAttack,
        /// <summary>Solomon's Lawbook: the controller skips their next Standby Phase.</summary>
        SkipControllerNextStandbyPhase,
        /// <summary>Fire Princess: each time the controller gains LP, inflict Amount to the opponent (continuous).</summary>
        InflictDamageWhenControllerGainsLp,
        /// <summary>Solemn Wishes: each time the controller draws, gain Amount LP (continuous).</summary>
        GainLpWhenControllerDraws,
        /// <summary>Zolga: when this card is Tributed for a Tribute Summon, its owner gains Amount LP.</summary>
        GainLpWhenTributedForSummon,
        /// <summary>Precious Cards from Beyond: a Tribute Summon using 2+ Tributes draws Amount (continuous).</summary>
        DrawWhenTributeSummonedWithTwoPlus,

        // ---- v103 merge: GitHub-lineage (v51-v55) members appended after the local v100-v102 members. ----
        // Serialized as ints in compiled programs; CardTextEffectCompiler.Version 103 invalidates every older program.
        /// <summary>
        /// Mandatory Standby-Phase upkeep: the controller pays PayLpAmount each of
        /// their Standby Phases (not optional, no destruction) — the classic Archfiend
        /// maintenance cost (Vilepawn / Desrook / Darkbishop / Infernalqueen /
        /// Shadowknight / Terrorking). Waived while "Pandemonium" is face-up.
        /// </summary>
        StandbyMaintenancePayLp,
        /// <summary>
        /// When this card (or, if <see cref="EffectClause.ProtectsAllArchfiendsOnField"/>,
        /// any Archfiend the controller controls) is targeted by an opponent's card
        /// effect, roll a six-sided die as it resolves; on a face in
        /// <see cref="EffectClause.DieNegateFacesMask"/> negate the effect and destroy the
        /// opponent's card. The classic Archfiend die-roll protection.
        /// </summary>
        DieRollNegateWhenTargeted,
        /// <summary>
        /// Exodia: win the Duel while every name in NamedCard ("A|B|C|D", split on '|')
        /// is in the controller's hand together with this card.
        /// </summary>
        WinDuelWithNamedSetInHand,
        /// <summary>
        /// While face-up: face-up RaceFilter monsters on the field are in Defense Position and
        /// cannot change their battle positions (Dragon Capture Jar).
        /// </summary>
        ContinuousForceDefenseLockPosition,
        /// <summary>Change the target to face-up Attack Position (Stop Defense). Flips a Set target.</summary>
        ChangeToFaceUpAttack,
        /// <summary>Change the target to face-up Defense Position (Block Attack).</summary>
        ChangeToFaceUpDefense,
        /// <summary>
        /// The controller loses Amount LP (not damage — no damage windows / reflection).
        /// <see cref="EffectClause.HalveLifePoints"/> loses half instead (Jirai Gumo family).
        /// </summary>
        LoseLifePoints,
        /// <summary>
        /// While face-up: this card gains Amount ATK (and DefAmount DEF) for each counted
        /// thing named by <see cref="EffectClause.CountSource"/> (Battleguards / Shadow Ghoul /
        /// Muka Muka).
        /// </summary>
        GainSelfAtkPerCount,
        /// <summary>Side discards Amount random card(s) from their hand (White Magical Hat).</summary>
        DiscardRandomFromHand,
        /// <summary>
        /// Change all face-up RaceFilter monsters on the field to Attack Position; with
        /// <see cref="EffectClause.RequiresPreviousClauseHit"/>, only if the previous clause
        /// destroyed at least one card (Dragon Piper).
        /// </summary>
        ChangeAllToAttackPosition,
        /// <summary>While face-up: this card must pay Amount LP to declare an attack (Dark Elf).</summary>
        AttackCostLp,
        /// <summary>While face-up: this card gains Amount ATK during the Damage Step when it attacks a
        /// monster matching AttributeFilter / RaceFilter (Insect Soldiers of the Sky).</summary>
        GainAtkWhenAttackingMatching,
        /// <summary>Toss a coin and call it; on a wrong call the controller loses half their LP (Jirai Gumo).</summary>
        CoinCallWrongLoseHalfLp,
        /// <summary>
        /// While face-up: a monster (not ExceptRaceFilter) that attacks this card cannot attack
        /// during its controller's next turn (Electric Lizard).
        /// </summary>
        AttackerCannotAttackNextTurn,
        /// <summary>Send the chosen cards from the hand to the GY — a discard, not a cost (The Cheerful Coffin).</summary>
        DiscardChosenFromHand,
        /// <summary>Shuffle the controller's Deck (Tainted Wisdom).</summary>
        ShuffleDeck,
        /// <summary>
        /// Equip rider: the equipped monster loses Amount ATK for each Standby Phase this Equip
        /// has seen (counted on the Equip). <see cref="EffectClause.DecayOnEquippedControllersStandby"/>
        /// picks whose Standby counts (Germ Infection) vs the Equip controller's (Stim-Pack).
        /// </summary>
        EquipAtkDecayPerStandby,
        /// <summary>Equip rider: the equipped monster cannot attack (Paralyzing Potion).</summary>
        EquippedCannotAttack,
        /// <summary>
        /// Equip rider: the opponent's monsters can only attack the equipped monster (Ring of Magnetism).
        /// </summary>
        EquippedMustBeAttackTarget,
        /// <summary>The target gains Amount ATK and DefAmount DEF until the end of this turn (Rush Recklessly).</summary>
        ModifyTargetUntilEndOfTurn,
        /// <summary>Continuous S/T: every attack declaration costs Amount LP (Toll) — Side Both / Opponent.</summary>
        AttackCostLpForAll,
        /// <summary>Continuous S/T: the opponent sends Amount card(s) from the top of their Deck to the GY to attack (Gravekeeper's Servant).</summary>
        AttackCostMillForOpponent,
        /// <summary>Equip: ATK becomes double its original while your LP is lower, half while higher (Megamorph).</summary>
        EquipAtkByLpComparison,
        /// <summary>Link this card to the target like an Equip (Spellbinding Circle); destroyed when it is.</summary>
        LinkThisToTarget,
        /// <summary>Equip / link rider: the host cannot change its battle position (Spellbinding Circle).</summary>
        EquippedCannotChangePosition,
        /// <summary>Destroy this card (Boar Soldier when Normal Summoned).</summary>
        DestroyThisCard,
        /// <summary>Return the chosen card from the opponent's hand to the Deck and shuffle (The Forceful Sentry).</summary>
        ReturnChosenToDeckShuffle,
        /// <summary>Put 1 NamedCard from the Deck on top after shuffling (Drill Bug).</summary>
        PlaceNamedFromDeckOnTop,
        /// <summary>Change every face-down Defense Position monster to face-up Defense without FLIP effects (Ceasefire).</summary>
        FlipAllFaceDownDefenseNoFlipEffects,
        /// <summary>While face-up: this card inflicts piercing battle damage (Mad Sword Beast).</summary>
        ContinuousPiercing,
        /// <summary>Equip rider: the equipped monster inflicts piercing battle damage (Fairy Meteor Crush).</summary>
        EquippedGainsPiercing,
        /// <summary>
        /// Double the current ATK of every face-up RaceFilter monster you control until the end of
        /// the turn, then destroy them in the End Phase (Limiter Removal).
        /// </summary>
        DoubleAtkOfYourMatchingThenDestroyAtEnd,
        /// <summary>The target cannot attack while this card stays face-up (Invitation to a Dark Sleep).</summary>
        LockTargetCannotAttackWhileFaceUp,
        /// <summary>
        /// Shuffle the target and your whole hand into the Deck, then draw as many as came from the hand
        /// (Monster Recovery).
        /// </summary>
        ShuffleTargetAndHandIntoDeckDraw,
        /// <summary>Special Summon the chosen card (NamedCard names, '|'-separated) from your hand or Deck (Elegant Egotist).</summary>
        SpecialSummonChosenFromHandOrDeck,
        /// <summary>
        /// Destroy every monster with current Level ≤ Amount that was Normal or Flip Summoned this turn
        /// (Infinite Dismissal, each End Phase).
        /// </summary>
        DestroySummonedThisTurnLevelLeq,
        /// <summary>The opponent draws Amount, then discards the Spells among the drawn cards (Hiro's Shadow Scout).</summary>
        OpponentDrawsThenDiscardsDrawnSpells,
        /// <summary>Continuous: destroy any Equip Card equipped to this card (Gearfried the Iron Knight).</summary>
        DestroyEquipsAttachedToThis,
        /// <summary>
        /// Discard the card that was just drawn for the opponent's normal Draw Phase draw (Drop Off).
        /// Exact "they just drew" — not random hand discard, Card Destruction, or Morphing Jar.
        /// </summary>
        DiscardJustDrawnCard,
        /// <summary>
        /// Reveal the just-drawn Normal Monster from your Draw Phase normal draw, then draw Amount more
        /// (Heart of the Underdog). Optional "You can". Fail-closed vs always-draw-2 / Jar reload / any-draw.
        /// </summary>
        RevealJustDrawnNormalAndDraw,
        /// <summary>
        /// Spring of Rebirth: each time monster(s) return from the field to the owner's hand,
        /// the controller gains Amount LP (continuous while face-up). Fail-closed vs Dian Keto
        /// Activate heal, Nutrient Z damage-calc gain, GY-send, and bounce-destroy.
        /// </summary>
        GainLpWhenMonsterReturnsToOwnerHand,
        /// <summary>
        /// Mysterious Puppeteer: each time either player Normal Summons or Flip Summons a monster,
        /// the controller gains Amount LP (continuous while face-up). Fail-closed vs Special Summon,
        /// Spring of Rebirth return-to-hand LP, Dian Keto Activate heal, and controller-only summons.
        /// </summary>
        GainLpWhenEitherPlayerNormalOrFlipSummons,
        /// <summary>
        /// Remove Brainwashing: Activate — control of all monsters on the field returns to their owners
        /// (mass ControlOwner→owner via TryTakeControl). Fail-closed vs take-until-EP, equip steal,
        /// Creature Swap exchange, single-target return, destroy, bounce.
        /// </summary>
        ReturnControlToOwners,
        /// <summary>
        /// Mask of Restrict: ContinuousWhileFaceUp — neither player can Tribute cards
        /// (Tribute Summon/Set, Ritual Tribute, cost Tribute, effect Tribute). Honored via
        /// ContinuousProtections.NeitherPlayerCanTribute. Fail-closed vs Soul Exchange force-tribute,
        /// Lava Golem SS Tribute-bypass, per-monster CannotBeTributedForSummon, Activate one-shot,
        /// destroy/bounce.
        /// </summary>
        NeitherPlayerCanTribute,
        /// <summary>
        /// The Dark Door: ContinuousWhileFaceUp — only 1 monster can attack during each
        /// Battle Phase (either player's BP). Honored via ContinuousProtections Soft gate +
        /// Soft DE MaxAttacksThisTurn BP attack-count residual. Fail-closed vs Gravity Bind
        /// (Level 4+), Messenger of Peace (ATK≥1500), Threatening Roar / Feint Plan this-turn
        /// locks, Extra Attacks / "can attack twice", Activate one-shot, destroy/bounce.
        /// </summary>
        OnlyOneMonsterMayAttackPerBattlePhase,
        /// <summary>
        /// Infinite Cards: ContinuousWhileFaceUp — no hand size limit for either player
        /// (End Phase discard to 6 skipped while face-up). Honored via ContinuousProtections
        /// Soft gate + Soft DE HandSizeLimitEndPhase residual. Fail-closed vs Enervating Mist
        /// (opp-only limit 5), Hieroglyph Lithograph your-limit-becomes-N, Max hand draw
        /// ("draw until"), Activate one-shot, destroy/bounce.
        /// </summary>
        NoHandSizeLimit,
        /// <summary>
        /// Enervating Mist: ContinuousWhileFaceUp — opponent's hand size limit becomes Amount
        /// (default 5). Soft ContinuousProtections + Soft DE HandSizeLimitEndPhase residual
        /// discards the opponent to Amount on their End Phase while face-up. Controller
        /// unaffected (default 6). Fail-closed vs Infinite Cards NoHandSizeLimit (bars remapping),
        /// Hieroglyph your-limit-becomes-N, default hand-size-6, Activate one-shot, destroy/bounce.
        /// Soft residual OK Soft DE HandSizeLimit override for opponent only.
        /// </summary>
        OpponentHandSizeLimitBecomes,
        /// <summary>
        /// Spell Economics: ContinuousWhileFaceUp — controller does not pay Life Points
        /// to activate Spell Cards. Honored via ContinuousProtections Soft gate + Soft
        /// TextEffectRuntime Spell activate PayLp skip (Mask Soft surgical). Fail-closed vs
        /// Trap LP costs, opponent's Spell costs, maintenance / Standby pays (Messenger),
        /// Activate one-shot, destroy/bounce. Soft residual OK Soft ContinuousProtections /
        /// Soft Spell activate PayLp skip.
        /// </summary>
        ControllerDoesNotPayLpToActivateSpells,
        /// <summary>
        /// The Emperor's Holiday: ContinuousWhileFaceUp — negate all Equip Card effects
        /// on the field (both players). Honored via ContinuousProtections Soft gate +
        /// Soft FieldSpellEffects Equip-apply skip (Mask Soft surgical). Fail-closed vs
        /// Royal Decree (negate Traps), Spell Canceller / Imperial Order (negate Spells
        /// generally), destroy Equips / Really Eternal Rest, Activate one-shot,
        /// destroy/bounce. Soft residual OK Soft ContinuousProtections Equip-effect skip
        /// (ApplyEquipStats + ApplyCatalogSpellTraps) / Equip piercing·magnetism·
        /// position-lock / Snatch Steal existing control / Equip Standby decay tick.
        /// </summary>
        NegateAllEquipCardEffectsOnField,
        /// <summary>
        /// Protector of the Sanctuary: ContinuousWhileFaceUp — your opponent cannot draw
        /// cards except during Draw Phases. Honored via ContinuousProtections Soft gate +
        /// Soft DE Draw() gate (Mask Soft surgical). Controller unrestricted. Fail-closed vs
        /// Time Seal (skip Draw Phase), searches / AddToHand / AddFromDeckToHand (≠ draw),
        /// Morphing Jar / Card Destruction both-draw (controller still draws), Activate
        /// one-shot, destroy/bounce. Soft residual OK Soft ContinuousProtections + Soft DE
        /// Draw gate; non-draw AddToHand / Deck search remain Soft residual (not gated).
        /// </summary>
        OpponentCannotDrawExceptDuringDrawPhases,
        /// <summary>
        /// Swords of Concealing Light: Activate/OnResolve — change all monsters the
        /// opponent controls to face-down Defense Position. Resolve-only (new summons
        /// after activation Soft residual = no auto-flip). Amount = controller Standby
        /// destroy counter (2 = destroy during your 2nd Standby after activation).
        /// Soft residual OK: ContinuousTurnsRemaining Standby ticker (Soft TextEffectRuntime).
        /// </summary>
        SetAllOppMonstersFaceDownDefense,
        /// <summary>
        /// Swords of Concealing Light: ContinuousWhileFaceUp — opponent's monsters
        /// cannot change their battle positions while this card is face-up. Honored via
        /// ContinuousProtections Soft gate + Soft DE CanChangePosition (Mask Soft surgical).
        /// Fail-closed vs Dragon Capture Jar force-DEF continuous / Spellbinding Circle
        /// bound-target / Book of Moon one-shot / Swords of Revealing Light attack-lock /
        /// Activate one-shot / destroy·bounce. Soft residual OK Soft ContinuousProtections
        /// + Soft DE BP gate; new summons Soft residual no auto-flip (resolve-only flip).
        /// </summary>
        OpponentCannotChangeBattlePositions,
        /// <summary>
        /// Lightforce Sword: Activate — Banish 1 random card from opponent's hand face-down.
        /// Amount = opponent Standby return counter (4 = return during opponent's 4th Standby
        /// after activation). Soft residual Soft Banish: FaceUp=false after BanishCard + Soft
        /// ContinuousTurnsRemaining on the banished copy; Soft TextEffectRuntime opp Standby
        /// ticker returns it to hand at 0. Fail-closed vs choose/look-at-hand / face-up banish /
        /// discard-random / your·next Standby / Disappear GY. Soft residual Soft Banish OK.
        /// </summary>
        BanishRandomFromOppHandFaceDown,
        /// <summary>
        /// Fake Trap: Activate — destroy this card instead of Trap Card(s) you control that
        /// would be destroyed by the answered opponent Spell/Trap/Effect Monster effect.
        /// Soft ContinuousProtections destroy-replacement redirect (Prefer NO DE rewrite);
        /// Soft residual look-confirm Soft-OK for face-down would-be-destroyed Traps.
        /// Fail-closed vs banish/send/return; only your Trap(s). Soft ≠ hard.
        /// ActivationCondition OppEffectWouldDestroyYourTrap.
        /// </summary>
        DestroyThisInsteadOfControllerTraps,
        /// <summary>
        /// A Feint Plan family: neither player can attack face-down monsters this turn.
        /// Distinct from PreventOpponentAttacksThisTurn (any attack), ContinuousCannotAttack
        /// (Gravity Bind / Messenger face-up continuous), Waboku, Swords, Negate Attack.
        /// (Moved here from mid-enum in v139: members are serialized by int, append-only.)
        /// </summary>
        CannotAttackFaceDownMonstersThisTurn,
        /// <summary>
        /// Graceful Dice / Skull Dice: roll a six-sided die once at resolution; every face-up monster the
        /// Side controls then gains (Amount &gt; 0) or loses (Amount &lt; 0) result × |Amount| ATK and DEF
        /// until the End Phase. Monsters that arrive later are unaffected ("currently control").
        /// </summary>
        ModifyAllAtkDefByDieUntilEndOfTurn,
        /// <summary>
        /// SummonNegation batch (v141): negate the Summon waiting in its summon-negation window and, with
        /// DestroyNegatedCard, destroy that monster (Horn of Heaven / Solemn Judgment). The Summon never
        /// succeeded, so no "when Summoned" effect or summon-response window follows, and the monster was
        /// never on the field (no field-to-GY trigger). A negated Normal Summon still used the Normal Summon.
        /// </summary>
        NegateSummon,
        /// <summary>
        /// ContinuousNegation batch (v141): ContinuousWhileFaceUp — "Trap Cards, and their effects on the
        /// field, cannot be activated" (Jinzo). No Trap Card can be activated from the hand or the field and
        /// no face-up Trap's effect can be activated while the source is face-up and not negated
        /// (GY effects are untouched). Runtime: Batches/ContinuousNegationRuntime.ActivationLocked.
        /// </summary>
        ContinuousCannotActivateTraps,
        /// <summary>
        /// ContinuousNegation batch (v141): ContinuousWhileFaceUp — "Negate all [other] Trap effects on the
        /// field" (Jinzo; Royal Decree with <see cref="EffectClause.ExceptThisCard"/>). Face-up Traps on the
        /// field are negated, and a Trap effect resolving on the field has no effect.
        /// </summary>
        ContinuousNegateTrapEffectsOnField,
        /// <summary>
        /// ContinuousNegation batch (v141): ContinuousWhileFaceUp — "Spell Cards, and their effects on the
        /// field, cannot be activated" (Spell Canceller). The Spell mirror of
        /// <see cref="ContinuousCannotActivateTraps"/>.
        /// </summary>
        ContinuousCannotActivateSpells,
        /// <summary>
        /// ContinuousNegation batch (v141): ContinuousWhileFaceUp — "Negate all Spell effects on the field"
        /// (Imperial Order, Spell Canceller). The Spell mirror of <see cref="ContinuousNegateTrapEffectsOnField"/>:
        /// face-up Spells (S/T and Field Zone) are negated and a Spell effect resolving on the field has no effect.
        /// </summary>
        ContinuousNegateSpellEffectsOnField,
        /// <summary>
        /// TurnNegationTraps batch (v141) — World Suppression / Mystic Probe / Metal Detector: negate every
        /// card of <see cref="EffectClause.NegatedCardFamily"/> in both players' Spell &amp; Trap Zones and
        /// Field Zones for the rest of this turn, including copies activated later that turn (ygopro
        /// EFFECT_DISABLE, LOCATION_SZONE both sides, RESET_PHASE+PHASE_END). Not targeted. A card of the
        /// family whose activation resolves while negated resolves with no effect.
        /// </summary>
        NegateCardFamilyOnFieldThisTurn,
        /// <summary>
        /// TakeControl batch (Charmers: Aussa / Eria / Hiita / Wynn): FLIP — take control of the
        /// targeted face-up monster the opponent controls (AttributeFilter) while this card is
        /// face-up on the field. The taken monster is bound to this card
        /// (<see cref="CardInstance.ControlBoundTo"/>); control returns to its owner as soon as
        /// this card leaves the field or is turned face-down (it is not destroyed); if the taken
        /// monster itself is turned face-down, its taker keeps it. Not until-End-Phase
        /// (<see cref="TakeControlUntilEndPhase"/>), not Equip take-control.
        /// </summary>
        TakeControlWhileSourceFaceUp,
        /// <summary>
        /// TakeControl batch (Invader of the Throne): switch control of the targeted monster the
        /// opponent controls with this card (each moves into the zone the other left; permanent).
        /// Not Dummy Golem (the opponent selects), not Creature Swap (each player picks their own).
        /// </summary>
        SwitchControlWithThis,
        /// <summary>
        /// TributeSummons batch (v141), Soul Exchange: for the rest of this turn the controller may Tribute the
        /// targeted opponent's monster as if they controlled it, and must include it whenever they Tribute
        /// (Tribute Summon/Set, Tribute costs, Ritual Tributes, effect Tributes) while it is an eligible
        /// Tribute there. Each resolved copy adds its own grant. Ends when the target leaves the field, is turned
        /// face-down, or the turn ends. See <see cref="TributeSummonsRuntime.ForcedTributes"/>.
        /// </summary>
        TributeTargetAsIfControlledThisTurn,
        /// <summary>
        /// DiscardTriggers batch (v142), Penguin Knight: every card in the controller's GY goes back to its Deck
        /// (an Extra Deck monster to the Extra Deck), then the Deck is shuffled ("unite your GY cards and your own
        /// Deck, shuffle them and form a new Deck"). Not targeted; a card Necrovalley locks in the GY stays there.
        /// </summary>
        ShuffleGraveyardIntoDeck,
        /// <summary>
        /// DiscardTriggers batch (v142), Delinquent Duo: the opponent discards 1 card at random, then, if they
        /// still hold a card, 1 more card of their own choice. A human opponent chooses through
        /// <see cref="PendingActivation.AwaitingForcedDiscardChoice"/> (TargetKind DiscardFromHand, the Magic Drain
        /// flow; the discard is mandatory); the AI picks with <see cref="DiscardTriggersRuntime.PickOwnDiscard"/>.
        /// </summary>
        OpponentDiscardsRandomThenOneOfTheirChoice,
        /// <summary>
        /// BattlePhaseEnd batch (v142), Goblin Attack Force / Giant Orc — <see cref="EffectTiming.EndOfBattlePhase"/>:
        /// if this card declared an attack this turn, change it to face-up Defense Position (when it is in Attack
        /// Position), and its battle position cannot be changed by its controller until the End Phase of their
        /// next turn (ygopro EFFECT_CANNOT_CHANGE_POSITION, reset at the 3rd End Phase or when it leaves the field
        /// or is turned face-down). Card effects can still change it. See
        /// <see cref="BattlePhaseEndRuntime.ManualPositionChangeLocked"/>.
        /// </summary>
        ChangeThisToDefenseAfterAttackingLockPosition,
        /// <summary>
        /// BattlePhaseEnd batch (v142), Steel Scorpion — <see cref="EffectTiming.AfterDamageCalculation"/>, this card
        /// the attack target: the attacking monster, unless its Type is <see cref="EffectClause.ExcludeRaceFilter"/>,
        /// is destroyed at the End Phase of this card's controller's opponent's <see cref="EffectClause.Amount"/>th
        /// turn after the attack (the attack turn's End Phase counts first). The countdown lives on the attacker:
        /// it goes on after this card leaves the field and ends if the attacker leaves the field or is turned
        /// face-down. See <see cref="BattlePhaseEndRuntime.TickDelayedDestroys"/>.
        /// </summary>
        DestroyAttackerAtOpponentsLaterEndPhase,
        /// <summary>
        /// SpellTrapDestroyedResponse batch (v142), Weather Report: the controller may conduct their Battle
        /// Phase twice — this turn when the effect resolves during their own turn outside the Battle Phase,
        /// otherwise during their next turn (ygopro EFFECT_BP_TWICE). With
        /// <see cref="EffectClause.RequiresPreviousClauseHit"/>, only if the previous Destroy clause destroyed
        /// a card. See <see cref="SpellTrapDestroyedResponseRuntime.GrantBattlePhaseTwice"/>.
        /// </summary>
        ConductBattlePhaseTwice,
        /// <summary>
        /// TypeDeclaration batch (v142), Activate — "Activate this card by declaring 1 Monster Type" (DNA Surgery) /
        /// "Declare 1 Type of monster" (The Regulation of Tribe). The player declares the Type while the card is
        /// activated: a declaration token from <see cref="EffectZoneFilter.DeclaredMonsterTypes"/>, offered through
        /// the ordinary pick (not a target, <see cref="EffectClause.ChoiceDoesNotTarget"/>). The Type is recorded on
        /// this card even when the resolution is negated (the Lua declares it in the target step). Read by
        /// <see cref="AllFaceUpMonstersBecomeDeclaredType"/> and by a ContinuousCannotAttack clause with
        /// <see cref="EffectClause.RaceFilterIsDeclared"/>. Runtime: <see cref="TypeDeclarationRuntime"/>.
        /// </summary>
        DeclareMonsterType,
        /// <summary>
        /// TypeDeclaration batch (v142), ContinuousWhileFaceUp — "All face-up monsters on the field become that
        /// Type" (DNA Surgery). While this card is face-up on the field and not negated, every face-up monster on
        /// the field is the Type this card declared (<see cref="CardInstance.Race"/>, which every Type check reads);
        /// with several such cards the one declared last applies. Face-down monsters and cards off the field keep
        /// their printed Type.
        /// </summary>
        AllFaceUpMonstersBecomeDeclaredType,
        /// <summary>
        /// TypeDeclaration batch (v142), StandbyPhase upkeep — "Tribute 1 monster from your side of the field during
        /// each of your Standby Phases. If you do not, this card is destroyed." (The Regulation of Tribe). In each of
        /// its controller's Standby Phases the controller may Tribute 1 monster they control (face-up or
        /// face-down); if they do not, or cannot, this card is destroyed. A maintenance cost (Lua
        /// EFFECT_FLAG_CANNOT_DISABLE): it still applies while this card is negated. Not a Chain Link.
        /// </summary>
        StandbyTributeOrDestroyThis,
        /// <summary>
        /// TypeDeclaration batch (v142), Activate — "Select 2 Monster Card Attributes. Your opponent then selects 1
        /// of the 2 Attributes and destroys all face-up monsters with that Attribute on the field." (Earthshaker).
        /// At resolution the activator selects 2 Attributes among the face-up monsters on the field, the opponent
        /// selects 1 of those 2, and every face-up monster on the field with it is destroyed by this effect. Cannot
        /// be activated (and does nothing) unless the face-up monsters show at least 2 Attributes
        /// (<see cref="TypeDeclarationRuntime.TwoAttributesFaceUp"/>). Not a target.
        /// </summary>
        DestroyAllFaceUpOfAttributeOpponentPicksFromTwo,
        /// <summary>
        /// LocksAndZones batch (v142), Cold Wave: until the activator's next turn (through the end of the next
        /// turn — ygopro c60682203 RESET_PHASE+PHASE_END, 2), neither player can activate Spell/Trap Cards or
        /// their effects (EFFECT_CANNOT_ACTIVATE, <c>re:IsSpellTrapEffect()</c>) nor Set Spell/Trap Cards
        /// (EFFECT_CANNOT_SSET). Not targeted. Runtime: Batches/LocksAndZonesRuntime.
        /// </summary>
        LockSpellTrapPlayAndSetUntilYourNextTurn,
        /// <summary>
        /// LocksAndZones batch (v142), Dimensionhole: banish the targeted monster until the activator's next
        /// Standby Phase (ygopro c22959079 REASON_TEMPORARY + Duel.ReturnToField); it then returns to the field
        /// in the zone and position it left (a monster you controlled but do not own returns to its owner's
        /// field: control is not kept). While it stays banished, that Monster Zone cannot be used
        /// (EFFECT_DISABLE_FIELD while the monster is in the banishment). A Token ceases to exist instead.
        /// Runtime: Batches/LocksAndZonesRuntime.
        /// </summary>
        BanishTargetUntilYourNextStandbyPhase,
        /// <summary>
        /// LocksAndZones batch (v142), Light of Intervention: ContinuousWhileFaceUp — neither player can Set a
        /// monster (a Normal Set / Tribute Set is made as a Normal / Tribute Summon in face-up Defense Position,
        /// ygopro EFFECT_CANNOT_MSET + EFFECT_LIGHT_OF_INTERVENTION), a face-down Special Summon is made in
        /// face-up Defense Position, and no monster on the field can be changed to face-down
        /// (EFFECT_CANNOT_TURN_SET). Runtime: Batches/LocksAndZonesRuntime.
        /// </summary>
        MonstersCannotBeSetOrTurnedFaceDown,
        /// <summary>
        /// HandDisruption batch (v142), Gamble: the controller calls a coin toss. Right: draw until their hand
        /// has <see cref="EffectClause.Amount"/> cards (counted at resolution; none if already that many).
        /// Wrong: skip their next turn (<see cref="DuelistState.SkipNextTurn"/>). See
        /// <see cref="HandDisruptionRuntime.ResolveCoinCallDrawUntilOrSkip"/>.
        /// </summary>
        CoinCallDrawUntilOrSkipNextTurn,
        /// <summary>
        /// HandDisruption batch (v142), Appropriate: ContinuousWhileFaceUp — each time the opponent draws a
        /// card(s) outside either Draw Phase, the controller draws <see cref="EffectClause.Amount"/> cards
        /// (no Chain Link). See <see cref="HandDisruptionRuntime.OnDrew"/>.
        /// </summary>
        DrawWhenOpponentDrawsOutsideDrawPhase,
        /// <summary>
        /// HandDisruption batch (v142), Painful Choice: the controller selects <see cref="EffectClause.Amount"/>
        /// cards from their Deck and shows them; the opponent selects 1 of them, which is added to the
        /// controller's hand; the rest are sent to the GY. See
        /// <see cref="HandDisruptionRuntime.BeginRevealDeckPick"/>.
        /// </summary>
        RevealDeckCardsOpponentPicksOneToHand,
        /// <summary>
        /// BanishedReturn (D.D. Scout Plane): an End Phase trigger of a card in its owner's banished pile —
        /// "Special Summon it in face-up Attack Position". Resolved only from the banished pile, by
        /// <see cref="BanishedReturnRuntime.FireEndPhase"/>; a copy on the field or in the GY never applies it.
        /// Appended — EffectActionKind is serialized as an int.
        /// </summary>
        SpecialSummonThisFromBanished,
        /// <summary>
        /// FlipMassEffects batch (v142): FLIP marker — this face-up card was flipped during its current field
        /// presence (ygopro flag / delayed effect registered by the Flip). It resolves nothing by itself; a
        /// later clause of the same card reads it: Jigen Bakudan's Standby tribute
        /// (<see cref="TributeThisDestroyYourMonstersBurnHalfAtk"/>) and Spear Cretin's "sent to the GY after
        /// being flipped" (<see cref="EachPlayerSpecialSummonsOneFromOwnGy"/>). The record ends when the card
        /// is turned face-down or leaves the field other than to the GY. See <see cref="FlipMassEffectsRuntime"/>.
        /// </summary>
        RecordThisCardFlipped,
        /// <summary>
        /// FlipMassEffects batch (v142), Jigen Bakudan: StandbyPhase, mandatory, only at the first Standby
        /// Phase of this card's controller after it was flipped (<see cref="RecordThisCardFlipped"/>). Cost:
        /// Tribute this card (not possible under Mask of Restrict: the effect is not activated). Destroy every
        /// monster its controller controls, then inflict half the total ATK of the monsters actually destroyed
        /// to the opponent.
        /// </summary>
        TributeThisDestroyYourMonstersBurnHalfAtk,
        /// <summary>
        /// FlipMassEffects batch (v142), Spear Cretin: SentFromFieldToGy, mandatory, only when this copy was
        /// flipped during the field presence that just ended (<see cref="RecordThisCardFlipped"/>). Each player
        /// targets 1 monster in their own GY (this card excluded) and Special Summons it to their own field in
        /// face-up Attack or face-down Defense Position (<see cref="EffectClause.SummonFaceUpAttackOrFaceDownDefense"/>,
        /// the summoning player's choice). No free Monster Zone: that player's target stays in the GY.
        /// </summary>
        EachPlayerSpecialSummonsOneFromOwnGy,
        /// <summary>
        /// FlipMassEffects batch (v142), Morphing Jar #2: shuffle every monster on the field into its owner's
        /// Deck (Extra Deck monsters to the Extra Deck, Tokens vanish; one about to be destroyed by the battle
        /// that flipped this card stays), then each player excavates from the top of their Deck until they
        /// excavate as many monsters as they shuffled into their Main Deck, Special Summons the excavated
        /// Level <see cref="EffectClause.SummonLevelMax"/> or lower monsters in face-down Defense Position
        /// and sends the other excavated cards to the GY.
        /// </summary>
        ShuffleFieldMonstersIntoDeckExcavateSummon,
        /// <summary>
        /// RedirectAndReplace batch (v142) — Fairy's Hand Mirror / Shift: the targeted card (picked from
        /// <see cref="EffectZoneFilter.AnotherAppropriateTarget"/>) becomes the new target of the Chain Link this
        /// card answered ("that Spell now targets the new target"; ygopro Duel.ChangeTargetCard), or — Shift, whose
        /// <see cref="EffectClause.ActivationCondition"/> is
        /// <see cref="RedirectAndReplaceRuntime.OpponentTargetsOneMonsterYouControl"/> — of the attack it answered
        /// (Duel.ChangeAttackTarget, no replay). Runtime: <see cref="RedirectAndReplaceRuntime.ResolveRedirect"/>.
        /// </summary>
        RedirectToAnotherAppropriateTarget,
        /// <summary>
        /// RedirectAndReplace batch (v142) — Kotodama: ContinuousWhileFaceUp. Each face-up monster that was not
        /// face-up on the field when this card last looked (all of them the first time its effect applies) and
        /// shares its name with another face-up monster is destroyed (ygopro EVENT_ADJUST label group). A monster
        /// Summoned or flipped face-up with the name of one already face-up is destroyed; ones that arrive
        /// together are all destroyed. Runtime: <see cref="RedirectAndReplaceRuntime.SweepSameNameDestruction"/>.
        /// </summary>
        DestroyNewFaceUpMonstersSharingAName,
        /// <summary>
        /// RedirectAndReplace batch (v142) — Banisher of the Light: ContinuousWhileFaceUp. While this monster is
        /// face-up on the field (not negated, not destroyed by battle), any card that would be sent to the GY — from
        /// anywhere, either player's, this card included — is banished instead (ygopro EFFECT_TO_GRAVE_REDIRECT).
        /// Applied on every GY send through <see cref="RedirectAndReplaceRuntime.SendsToBanishedInstead"/>; a card
        /// banished instead fires no "sent to the GY" trigger.
        /// </summary>
        BanishCardsSentToGyInstead,
        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Castle of Dark Illusions: FLIP — every monster of
        /// <see cref="EffectClause.RaceFilter"/> on the field when it resolves (both sides, not ones that arrive
        /// later) gains Amount ATK / DefAmount DEF, and <see cref="EffectClause.StandbyGrowthAmount"/> more on each
        /// of the controller's next <see cref="EffectClause.StandbyGrowthSteps"/> Standby Phases. It lasts while
        /// this card stays face-up on the field and its effects are not negated (ygopro
        /// RESETS_STANDARD_DISABLE). See <see cref="StandbyGrowthAndMaintenanceRuntime"/>.
        /// </summary>
        GrantRaceAtkDefGrowingEachStandbyWhileThisFaceUp,
        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Pumpking the King of Ghosts: ContinuousWhileFaceUp — this
        /// card gains Amount ATK / DefAmount DEF while a face-up <see cref="EffectClause.NamedCard"/> is on the
        /// field, plus <see cref="EffectClause.StandbyGrowthAmount"/> for each of its controller's Standby Phases
        /// (at most <see cref="EffectClause.StandbyGrowthSteps"/>) that passed while that card was on the field.
        /// When the named card is not on the field the whole gain disappears and the count restarts.
        /// </summary>
        GainSelfAtkDefWhileNamedOnFieldGrowingEachStandby,
        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Mirror Wall: each of the opponent's monsters that declares
        /// an attack while this card is face-up (and, on activation, the monster attacking right now) has its
        /// ATK halved (final ATK, after every other modifier) while this card remains on the field. The mark ends
        /// when that monster leaves the field or is turned face-down; it applies only while the opponent of this
        /// card's controller controls it. Activate clause: the activation's operation; ContinuousWhileFaceUp
        /// clause: the attack-declaration record and the halving.
        /// </summary>
        HalveAtkOfOpponentAttackersWhileThisOnField,
        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Skull Invitation: ContinuousWhileFaceUp — each time a
        /// card(s) is sent to a Graveyard, inflict Amount damage to its owner for each card sent (both players at
        /// once). Activate clause: its activation starts watching from the moment it resolves.
        /// </summary>
        InflictDamageToOwnerPerCardSentToGy,
        /// <summary>
        /// SpiritsAndMisc batch — Yamata Dragon: the player named by <see cref="EffectClause.Side"/> draws until
        /// they hold <see cref="EffectClause.Amount"/> cards in hand; no draw when they already hold that many
        /// (ygopro c76862289: Draw(p, 5 - hand count) only while below 5).
        /// </summary>
        DrawUntilHandSize,
        /// <summary>
        /// SpiritsAndMisc batch — Hino-Kagu-Tsuchi: the opponent discards every card in their hand at the start of
        /// the next Draw Phase (either player's), before the draw; a Draw Phase skipped by a card effect ends it
        /// with no discard (ygopro c75745607: EVENT_PREDRAW, RESET_PHASE+PHASE_DRAW).
        /// See <see cref="DuelistState.DiscardHandAtNextDrawPhase"/>.
        /// </summary>
        DiscardOpponentHandAtNextDrawPhase,
        /// <summary>
        /// SpiritsAndMisc batch — Kiseitai (<see cref="EffectTiming.StartOfDamageStep"/>, this card is the attack
        /// target and was face-down Defense when the Damage Step began): this card leaves its Monster Zone and
        /// becomes an Equip Spell Card equipped to the attacking monster, in its controller's Spell &amp; Trap Zone;
        /// no damage calculation is conducted. No free Spell &amp; Trap Zone or a face-down attacker: this card is
        /// destroyed instead (ygopro c4266839 eqop). Resolved by SpiritsAndMiscRuntime.TryBecomeEquipOnFaceDownAttacker.
        /// </summary>
        BecomeEquipOnFaceDownAttacker,
        /// <summary>
        /// SpiritsAndMisc batch — Kiseitai (<see cref="EffectTiming.StandbyPhase"/>, OpponentTurnOnly): gain Life
        /// Points equal to half the current ATK of the monster this card is equipped to; nothing while this card is
        /// not an Equip Card.
        /// </summary>
        GainLpHalfEquippedMonsterAtk,
        /// <summary>
        /// DrawThenDiscard (Graceful Charity): the controller draws Amount cards; only if all of them were drawn,
        /// they then discard <see cref="EffectClause.ThenDiscardCount"/> cards of their choice from the hand (ygopro
        /// Draw, BreakEffect, DiscardHand REASON_EFFECT|REASON_DISCARD — a separate step after the draw). A human
        /// picks through a PendingActivation; the AI discards its lowest AiHeuristicPolicy.DiscardPriority cards.
        /// See <see cref="DrawThenDiscardRuntime"/>.
        /// </summary>
        DrawThenDiscard,
        /// <summary>
        /// FlipDefCheck batch (Acid Trap Hole): change the target to face-up Defense Position, then destroy it
        /// if its DEF is <see cref="EffectClause.ConditionAmount"/> or less, otherwise change it back to face-down Defense
        /// Position. A monster flipped face-up this way activates its FLIP effect after the check, even if it was
        /// destroyed or turned face-down again. See <see cref="FlipDefCheckRuntime"/>.
        /// </summary>
        FlipFaceUpDestroyIfDefLeqElseSetFaceDown,
        /// <summary>
        /// Toons batch: "Must first be Special Summoned (from your hand)[ by Tributing N monster(s)], while you control
        /// "X"." — the card's own Special Summon procedure from the hand (ygopro EFFECT_SPSUMMON_PROC + EnableReviveLimit).
        /// ContinuousWhileFaceUp marker, never resolved as an effect: <see cref="EffectClause.Amount"/> = monsters to
        /// Tribute as the cost (0 = none), <see cref="EffectClause.RequiresFaceUpName"/> = the card the controller must
        /// control face-up. Performed by DuelEngine.CanSpecialSummonProcedure / TrySpecialSummonProcedure
        /// (<see cref="ToonsRuntime"/>), with a summon-negation window. Until this procedure has summoned the copy
        /// (<see cref="CardInstance.ProcedureCompleted"/>) no other Special Summon may place it.
        /// </summary>
        SpecialSummonFromHandProcedure,
        /// <summary>
        /// Toons batch: "Cannot attack the turn it is Special Summoned." (ContinuousWhileFaceUp). A face-up Special
        /// Summon of this card, by any means, forbids its attacks for the rest of that turn
        /// (<see cref="CardInstance.CannotAttackDuringTurnNumber"/>).
        /// </summary>
        CannotAttackTurnSpecialSummoned,
        /// <summary>
        /// Toons batch: "If "X" on the field is destroyed, destroy this card." (ContinuousWhileFaceUp, no chain link).
        /// When a face-up card named <see cref="EffectClause.NamedCard"/> on either field is destroyed (battle or card
        /// effect), every face-up, non-negated monster carrying this clause is destroyed at once.
        /// </summary>
        DestroyThisWhenNamedDestroyed,
        /// <summary>
        /// Toons batch: "Can attack your opponent directly, unless they control a Toon monster, in which case this card
        /// must target a Toon monster for its attacks." (ContinuousWhileFaceUp; ygopro EFFECT_DIRECT_ATTACK /
        /// EFFECT_CANNOT_DIRECT_ATTACK / EFFECT_CANNOT_SELECT_BATTLE_TARGET).
        /// </summary>
        CanAttackDirectlyUnlessOpponentControlsToon,
        /// <summary>
        /// TargetedClassics batch (Miracle Dig): return the target, a face-up monster in the controller's banished
        /// pile (<see cref="EffectZoneFilter.ControllerBanishedFaceUpMonsters"/>), to its owner's GY (ygopro
        /// SendtoGrave REASON_EFFECT|REASON_RETURN). A target that is no longer face-up in that banished pile is not
        /// affected. See <see cref="TargetedClassicsRuntime.ReturnBanishedToGraveyard"/>.
        /// </summary>
        ReturnBanishedTargetToGy,
        /// <summary>
        /// PositionLocks batch: an optional Standby / End Phase trigger of a face-up monster, only during its
        /// controller's opponent's turn (<see cref="EffectClause.OpponentTurnOnly"/>): target 1 face-up monster the
        /// opponent controls (<see cref="EffectZoneFilter.OppFaceUpMonsters"/>) and change its battle position
        /// (face-up Attack ↔ face-up Defense). With <see cref="EffectClause.LocksPositionRestOfTurn"/> that monster's
        /// battle position then cannot be changed for the rest of the turn (Aqua Spirit; Garuda the Wind Spirit has no
        /// lock). The AI turns the strongest Attack Position monster to Defense; a human is asked (Cancel = do not
        /// use it) while the phase waits. See <see cref="PositionLocksRuntime"/>.
        /// </summary>
        ChangeOppFaceUpMonsterPositionAtPhase,
        /// <summary>
        /// PositionLocks batch, Curse of Fiend: every monster on the field changes battle position at once — face-up
        /// Attack → face-up Defense, face-up or face-down Defense → face-up Attack (a monster flipped face-up activates
        /// its FLIP effect after the resolution) — then, for the rest of this turn, no monster on the field can change
        /// its battle position except by a card effect (no manual change, no Flip Summon; monsters that arrive later
        /// included). See <see cref="PositionLocksRuntime"/>.
        /// </summary>
        ChangeAllMonsterPositionsLockRestOfTurn,
        /// <summary>
        /// EquipRiders batch — Mask of the Accursed / Mask of Dispel (<see cref="EffectTiming.StandbyPhase"/>, this
        /// card's controller's Standby Phase, mandatory): inflict <see cref="EffectClause.Amount"/> damage to the
        /// current controller of the card this card is linked to — the monster it is equipped to (Lua
        /// GetEquipTarget():GetControler(), which may be this card's own controller) or the card it selected
        /// (<see cref="LinkThisToTargetSpellTrap"/>, Lua GetFirstCardTarget():GetControler()). No link, no damage.
        /// See <see cref="EquipRidersRuntime"/>.
        /// </summary>
        InflictDamageToLinkedCardController,
        /// <summary>
        /// EquipRiders batch — Mask of Dispel (Activate): link this card to the chosen face-up card in a Spell &amp;
        /// Trap Zone or Field Zone (Lua SetCardTarget), only if both are still on the field and the target is still
        /// face-up. Not an equip: the target never gets this card in its Equips, and this card no EquippedTo. The
        /// link lives in <see cref="EquipRidersRuntime"/> and ends when either card leaves the field.
        /// </summary>
        LinkThisToTargetSpellTrap,
        /// <summary>
        /// EquipRiders batch — Mask of Dispel (ContinuousWhileFaceUp, no Chain Link): "When the selected card is
        /// removed from the field, destroy this card." When the card linked by <see cref="LinkThisToTargetSpellTrap"/>
        /// leaves the field in any way, this card is destroyed (Lua EVENT_LEAVE_FIELD continuous effect); a negated
        /// copy is not, and its link simply ends. Checked on every board refresh (EquipRidersRuntime.SweepLinks).
        /// </summary>
        DestroyThisWhenLinkedCardLeavesField,
        /// <summary>
        /// EquipRiders batch — Ekibyo Drakmord (ContinuousWhileFaceUp marker, no Chain Link of its own): count the End
        /// Phases of the turns of the equipped monster's current controller since this card was equipped (Lua turn
        /// counter, reset on equip, EFFECT_FLAG_CANNOT_DISABLE); at the End Phase the count reaches
        /// <see cref="EffectClause.Amount"/>, destroy the equipped monster, and if it was destroyed return this card to
        /// its owner's hand from wherever it went. Ticked by EquipRidersRuntime.TickEndPhase.
        /// </summary>
        DestroyEquippedAtItsControllersNthEndPhaseThenReturnThis,
        /// <summary>
        /// SelfStatMonsters: "This card cannot attack your opponent directly." Continuous. Read by the same
        /// direct-attack gate as <see cref="ToonsRuntime.ForbidsDirectAttack"/>
        /// (<see cref="SelfStatMonstersRuntime.ForbidsDirectAttack"/>). Not resolved as an effect.
        /// </summary>
        CannotAttackDirectly,
        /// <summary>
        /// FlipCostsAndFusion batch — Summoner of Illusions (Flip, Lua c14644902): at resolution Tribute 1 other monster
        /// you control (an effect Tribute, its controller's choice; only a monster that can be Tributed, and only
        /// while a Fusion Monster in the Extra Deck can be Special Summoned), and if you do, Special Summon 1 Fusion
        /// Monster of your choice from your Extra Deck in face-up Attack or Defense Position (not a Fusion Summon).
        /// It is destroyed during this turn's End Phase (<see cref="CardInstance.TempDestroyOnEndOfTurn"/>; the
        /// destroy ends if it leaves the field). Runtime: <see cref="FlipCostsAndFusionRuntime"/>.
        /// </summary>
        TributeOtherThenSpecialSummonFusionFromExtraDestroyEndPhase,
        /// <summary>
        /// FlipCostsAndFusion batch — De-Fusion (Activate, Lua c95286165): return the targeted face-up Fusion Monster
        /// to the Extra Deck, then, if it was Fusion Summoned (<see cref="CardInstance.FusionMaterialsUsed"/>), every
        /// material used for that Fusion Summon is still in your GY as its material
        /// (<see cref="CardInstance.SentAsFusionMaterialFor"/>), each can be Special Summoned and you have that many
        /// free Monster Zones, you can Special Summon all of them (optional; face-up Attack or Defense each).
        /// Runtime: <see cref="FlipCostsAndFusionRuntime"/>.
        /// </summary>
        ReturnFusionToExtraThenMaySummonMaterials,
        /// <summary>
        /// EquipFamily batch — Collected Power (Activate): every face-up Equip Card on the field (both players' Spell
        /// &amp; Trap Zones: Equip Spells, Unions and monsters acting as Equip Cards) is equipped to the targeted
        /// face-up monster when it is a correct target for that card (<see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>,
        /// Lua CheckEquipTarget); every other one is destroyed, and all of them are destroyed when the target is no
        /// longer a face-up monster on the field. The Equip Cards keep their controllers; a control Equip (Snatch
        /// Steal) moves its control with it.
        /// </summary>
        EquipAllEquipCardsToTarget,
        /// <summary>
        /// EquipFamily batch — Dark Necrofear (End Phase trigger in the GY): this monster card is equipped from its
        /// owner's GY to the targeted face-up monster the opponent controls, and its controller takes control of that
        /// monster while it stays equipped (<see cref="EffectClause.TakeControlOfTarget"/>; control returns when the
        /// card stops being equipped). Its equip limit is that monster only (<see cref="EquipFamilyRuntime"/>).
        /// </summary>
        EquipThisFromGyToTargetTakeControl,
        /// <summary>
        /// BattleWindows batch — Fairy Box (ContinuousWhileFaceUp marker, a mandatory trigger of the face-up card): "When
        /// your opponent's monster declares an attack: Toss a coin and call it. If you call it right, the attacking
        /// monster's ATK becomes 0 until the end of the Battle Phase." (ygopro EVENT_ATTACK_ANNOUNCE, Duel.CallCoin,
        /// EFFECT_SET_ATTACK_FINAL 0 reset at the end of the Battle Phase). Fired from DuelEngine.TryAttack by
        /// <see cref="BattleWindowsRuntime.FireOpponentAttackDeclaredTriggers"/>; never resolved as an ordinary clause.
        /// </summary>
        CallCoinZeroAttackerAtkUntilEndOfBattlePhase,
        /// <summary>
        /// BattleWindows batch — Jam Defender (ContinuousWhileFaceUp marker, an optional trigger of the face-up card):
        /// "When an opponent's monster declares an attack on a monster you control: You can target 1 "X" you control;
        /// switch the attack target to that target." X = <see cref="EffectClause.NamedCard"/>; the new target is a
        /// face-up X that is not the current target and that the attacker could attack (Duel.ChangeAttackTarget).
        /// Fired by <see cref="BattleWindowsRuntime.FireOpponentAttackDeclaredTriggers"/>.
        /// </summary>
        SwitchOpponentAttackToNamedMonsterYouControl,
        /// <summary>
        /// BattleWindows batch — Destruction Punch (Activate, Normal Trap in the end-of-Damage-Step window): destroy the
        /// opponent's attacking monster of the battle that just ended (not targeted; Lua SetTargetCard only for
        /// IsRelateToEffect), if it is still on the field.
        /// </summary>
        DestroyAttackerAtEndOfDamageStep,
        /// <summary>
        /// BattleWindows batch — Dreamsprite (BeforeDamageCalculation, mandatory, this card is the attack target of an
        /// opponent's monster): target another monster you control (face-down allowed); the attacker battles it instead
        /// (Lua Duel.CalculateDamage(a, tc)). Fired by <see cref="BattleWindowsRuntime.BeginBattleConfirmSwitch"/>.
        /// </summary>
        BattleAnotherOwnMonsterInstead,
        /// <summary>
        /// BattleWindows batch — Dark Spirit of the Silent (AttackDeclared, Normal Trap during the opponent's turn):
        /// target the attacking monster and another face-up monster your opponent controls; negate the attack, then if
        /// that other monster is still face-up, change it to Attack Position if it is in Defense Position, and if it can
        /// attack, it battles the original attack target (or attacks directly if there was none) — Lua
        /// Duel.CalculateDamage, no attack declaration (<see cref="BattleWindowsRuntime.RunReplacementBattle"/>).
        /// </summary>
        NegateAttackAndAnotherOpponentMonsterBattlesInstead,
        /// <summary>
        /// RuleLocks batch — Jowgen the Spiritualist (ContinuousWhileFaceUp, no Chain Link): "Neither player can Special
        /// Summon monsters." (ygopro EFFECT_CANNOT_SPECIAL_SUMMON, target range 1,1). Enforced by
        /// <see cref="RuleLocksRuntime.SpecialSummonLocked"/> in DuelEngine.SpecialSummonToField (every Special Summon:
        /// effects, procedures, Fusion, Ritual, Tokens) and by the activation / procedure legality checks.
        /// </summary>
        NeitherPlayerCanSpecialSummon,
        /// <summary>
        /// RuleLocks batch — Jam Breeding Machine (ContinuousWhileFaceUp, no Chain Link): "You cannot Summon any monsters,
        /// except "<see cref="EffectClause.NamedCard"/>" (but you can Set)." Its controller cannot Normal Summon, Flip
        /// Summon or Special Summon a monster unless it has that name (ygopro EFFECT_CANNOT_SUMMON / CANNOT_FLIP_SUMMON /
        /// CANNOT_SPECIAL_SUMMON, target range 1,0); Setting a monster (a Tribute Set too) stays legal.
        /// <see cref="RuleLocksRuntime"/>.
        /// </summary>
        ControllerCannotSummonExceptNamed,
        /// <summary>
        /// RuleLocks batch — Kycoo the Ghost Destroyer (ContinuousWhileFaceUp, no Chain Link): "Your opponent cannot banish
        /// cards from either player's Graveyard." (ygopro EFFECT_CANNOT_REMOVE, target range 0,1, GY cards). The banishing
        /// player (the controller of the effect or cost) is the one locked: DuelEngine.BanishCard and the GY banish
        /// collectors ask <see cref="RuleLocksRuntime.BanishFromGyLocked"/>.
        /// </summary>
        OpponentCannotBanishFromGraveyards,
        /// <summary>
        /// RuleLocks batch — Royal Command (ContinuousWhileFaceUp, no Chain Link): "Flip monsters' effects cannot be
        /// activated, also their effects are negated." Per ygopro EVENT_CHAIN_ACTIVATING + NegateActivation on
        /// re:IsActiveType(TYPE_FLIP): every activated effect of a Flip monster (its FLIP effect and its other triggers or
        /// ignitions) has its activation negated as it is activated; its continuous effects still apply.
        /// <see cref="RuleLocksRuntime.NegatesFlipMonsterActivation"/>.
        /// </summary>
        NegateFlipMonsterEffectActivations,
        /// <summary>
        /// RuleLocks batch — Chain Energy (ContinuousWhileFaceUp, no Chain Link): "Each player must pay
        /// <see cref="EffectClause.Amount"/> Life Points per card to Normal Summon, Special Summon, Set or activate cards
        /// from his/her respective hand." (ygopro EFFECT_ACTIVATE_COST / SUMMON_COST / SPSUMMON_COST / MSET_COST /
        /// SSET_COST, range hand, both players; Amount per face-up copy, and the action is illegal with less LP).
        /// <see cref="RuleLocksRuntime.HandActionLpCost"/>.
        /// </summary>
        HandActionsCostLp,
        /// <summary>
        /// DestinyBoard batch — Destiny Board (<see cref="EffectTiming.EndPhase"/>, opponent's turn only, once per turn,
        /// mandatory): place the next card of <see cref="EffectClause.NamedCard"/> ("|"-separated, in printed order) that
        /// this copy has not placed yet from the controller's hand or Deck face-up in their Spell &amp; Trap Zone. A
        /// placement, not an activation: the placed card makes no Chain Link. Needs a free zone; nothing is placed when
        /// the card is in neither place. See <see cref="DestinyBoardRuntime"/>.
        /// </summary>
        PlaceNextNamedInOrderFromHandOrDeck,
        /// <summary>
        /// DestinyBoard batch — Destiny Board (<see cref="EffectTiming.ContinuousWhileFaceUp"/>, a rule, not resolved):
        /// its controller wins the Duel while this card is face-up in their Spell &amp; Trap Zone and every card named in
        /// <see cref="EffectClause.NamedCard"/> is face-up on their field. Checked on every board refresh (Lua
        /// EVENT_ADJUST, EFFECT_FLAG_CANNOT_DISABLE: negation does not stop it). See <see cref="DestinyBoardRuntime"/>.
        /// </summary>
        WinWithThisAndNamedSetFaceUpOnField,
        /// <summary>
        /// DestinyBoard batch — Destiny Board (<see cref="EffectTiming.ContinuousWhileFaceUp"/>, no Chain Link): when a
        /// card of the <see cref="EffectClause.TargetSeriesName"/> series or named <see cref="EffectClause.NamedCard"/>
        /// leaves its controller's field, every face-up such card that player controls is sent to the GY. Checked on
        /// every board refresh. See <see cref="DestinyBoardRuntime"/>.
        /// </summary>
        SendSeriesAndNamedToGyWhenOneLeavesField,
        /// <summary>
        /// NegationsAndGySummons batch — Gilasaurus: "You can Special Summon this card (from your hand)." An
        /// unconditional Special Summon procedure from the hand with no Tribute (ygopro EFFECT_SPSUMMON_PROC,
        /// SetValue(1)). Not "Must first be Special Summoned": other effects may still Special Summon the card, and it
        /// can be Normal Summoned. Performed by DuelEngine.CanSpecialSummonProcedure / TrySpecialSummonProcedure
        /// (<see cref="NegationsAndGySummonsRuntime"/>); never resolved as an effect.
        /// </summary>
        SpecialSummonThisFromHandByOwnProcedure,
        /// <summary>
        /// NegationsAndGySummons batch — Gilasaurus: "your opponent can Special Summon 1 monster from their GY." The
        /// controller's opponent may select 1 monster in their own GY and Special Summon it face-up to their field
        /// (NecroValleyFilter; their choice, or none). <see cref="NegationsAndGySummonsRuntime.ResolveOpponentGySummon"/>.
        /// </summary>
        OpponentMaySpecialSummonOneFromOwnGy,
        /// <summary>
        /// NegationsAndGySummons batch — Card of Safe Return (ContinuousWhileFaceUp marker): "When a monster is
        /// Special Summoned from your Graveyard, you can draw 1 card." One draw per Summon event, fired by DuelEngine
        /// when a Special Summon whose origin was its controller's GY completes
        /// (<see cref="NegationsAndGySummonsRuntime.OnSpecialSummoned"/>). Not resolved as an effect.
        /// </summary>
        DrawWhenMonsterSpecialSummonedFromYourGy,
        /// <summary>
        /// NegationsAndGySummons batch — Chosen One: "Select 1 Monster Card and 2 non-Monster Cards from your hand.
        /// Your opponent randomly selects 1 card among them. If it is a Monster Card, it is Special Summoned and send
        /// the remaining 2 cards to the Graveyard. If not, send all the cards to the Graveyard." The controller picks 1
        /// monster that can be Special Summoned, then 2 Spell/Trap Cards from the hand; one of the 3 is chosen at random
        /// (engine.Rng); a monster is Special Summoned face-up and the other 2 go to the GY, otherwise all 3 go to the
        /// GY (<see cref="NegationsAndGySummonsRuntime.ResolveChosenOne"/>).
        /// </summary>
        SelectMonsterAndTwoSpellTrapsOpponentPicksOneAtRandom,
        /// <summary>
        /// FaceUpQuickEffects batch (v160) — Skull Lair: "Remove from play any number of Monster Cards in your
        /// Graveyard to destroy 1 face-up monster on the field whose Level is equal to the number of the cards you
        /// removed from play." Cost (paid when the face-up Quick Effect is activated): banish N &gt;= 1 monsters from
        /// your GY, where a face-up monster of Level N is on the field. Resolution: select (not target) 1 face-up
        /// Level-N monster on either field and destroy it (<see cref="FaceUpQuickEffectsRuntime"/>).
        /// </summary>
        DestroyFaceUpMonsterWithLevelEqualToBanishedCost,
        /// <summary>
        /// FaceUpQuickEffects batch (v160) — Soul Demolition: "Both players select 1 Monster Card from their
        /// opponent's Graveyard. Remove the selected cards from play." Chosen when the face-up Quick Effect is
        /// activated (ygopro SelectTarget: the turn player first, then the other player), both targets banished when
        /// it resolves if they are still in the GY (<see cref="FaceUpQuickEffectsRuntime"/>).
        /// </summary>
        BothPlayersTargetMonsterInOpponentsGyAndBanish,
        /// <summary>
        /// FaceUpQuickEffects batch (v160) — The Dragon's Bead: "Negate the effect of a Trap Card that targets 1 face-up
        /// &lt;Type&gt;-Type monster and destroy the Trap Card." A face-up Quick Effect chained to the activation of a
        /// Trap Card (ygopro EVENT_CHAINING) that targets a face-up monster of <see cref="EffectClause.RaceFilter"/>:
        /// negate that effect, then destroy that Trap Card (<see cref="FaceUpQuickEffectsRuntime"/>). It negates Trap
        /// effects (aux.DoubleSnareValidity).
        /// </summary>
        NegateTrapEffectTargetingFaceUpRaceAndDestroyIt,
        /// <summary>
        /// EquipModel batch — Tailor of the Fickle (Quick-Play, Activate): "Switch 1 Equip Card equipped to a monster to
        /// another correct target." Targets 1 face-up Equip Card equipped to a monster (an Equip Spell / Equip Trap or a
        /// Union; Lua s.ecfilter) and another face-up monster that is a correct target for it
        /// (<see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>). At resolution the Equip Card moves to that monster;
        /// if the monster target is gone (or turned face-down), the Equip Card is sent to the GY instead
        /// (<see cref="EquipModelRuntime.ResolveSwitch"/>). The clause's target is the Equip Card; the monster target is
        /// chosen with it (<see cref="EquipModelRuntime"/>).
        /// </summary>
        MoveEquipCardToAnotherCorrectTarget,
        /// <summary>
        /// EngineHygiene batch (v160) — Forced Requisition: the Continuous Trap's activation (legal only in the
        /// "you discarded from your hand" window, <see cref="DiscardEvents.YouDiscardedFromHand"/>) places it; while it is
        /// face-up, each later discard event of its controller makes the opponent discard as many cards of their choice
        /// (<see cref="DiscardEvents"/>, a mandatory trigger resolved once play is idle). The resolution itself is a no-op.
        /// </summary>
        OpponentDiscardsSameNumberWhenYouDiscard,
        /// <summary>
        /// EngineHygiene batch (v160) — Super Rejuvenation: during the End Phase of this turn the controller draws 1 card
        /// per Dragon monster they discarded from the hand or Tributed from the hand or field this turn (counted from the
        /// start of the turn by <see cref="DiscardEvents"/> / <see cref="ReleaseEvents"/>, read at that End Phase).
        /// </summary>
        DrawAtEndPhasePerDragonsDiscardedOrTributedThisTurn,
       /// <summary>
        /// SimpleSpellTraps batch — A Wingbeat of Giant Dragon: "Return 1 Level 5 or higher Dragon-Type monster you
        /// control to the hand, and if you do, destroy all Spell and Trap Cards on the field." Not a target: at
        /// resolution the controller selects 1 face-up monster they control of Type <see cref="EffectClause.ConditionName"/>
        /// with Level &gt;= <see cref="EffectClause.ConditionAmount"/> (not a Token); only if it reached the hand,
        /// every other Spell/Trap Card on the field (both sides, the Field Zone too) is destroyed at once
        /// (<see cref="SimpleSpellTrapsRuntime.ResolveWingbeat"/>).
        /// </summary>
        ReturnOwnRaceLevelMonsterToHandThenDestroyAllSpellTraps,
        /// <summary>
        /// SimpleSpellTraps batch — Burst Breath: "Tribute 1 Dragon-Type monster; destroy all face-up monsters on the
        /// field whose DEF is less than or equal to the ATK the Tributed monster had on the field." The Tribute is the
        /// structured cost (<see cref="EffectClause.RequiresTributeCount"/> / <see cref="EffectClause.TributeRaceFilter"/>,
        /// its ATK recorded as the cost numeric); every face-up monster on both fields with DEF &lt;= that ATK is
        /// destroyed at once. <see cref="SimpleSpellTrapsRuntime.ResolveBurstBreath"/>.
        /// </summary>
        DestroyFaceUpMonstersDefLeqTributedAtk,
        /// <summary>
        /// SimpleSpellTraps batch — Dragon's Gunfire bullet: "Target 1 monster with 800 or less DEF; destroy that
        /// target." A face-up monster on either field whose DEF is at most <see cref="EffectClause.Amount"/>; at
        /// resolution it must still be face-up with that DEF (Lua desop re-checks s.dfilter).
        /// </summary>
        DestroyTargetWithDefAtMost,
        /// <summary>
        /// SimpleSpellTraps batch — Fiend Comedian: "Toss a coin and call it. If you call it right, all your
        /// opponent's cards in the Graveyard are removed from play immediately. If you call it wrong, send a number of
        /// cards equal to the cards in your opponent's Graveyard from your Deck to your Graveyard." The controller
        /// calls at resolution (a human through <see cref="PendingActivation.AwaitingCoinCall"/>, holding the chain).
        /// <see cref="SimpleSpellTrapsRuntime.ResolveFiendComedian"/>.
        /// </summary>
        CallCoinBanishOppGyOrSendDeckEqualToOppGy,
        /// <summary>
        /// SimpleSpellTraps batch — Emergency Provisions: "Send any number of other Spell/Trap Cards you control to the
        /// Graveyard; gain 1000 LP for each card sent to the Graveyard this way." Cost: 1 or more (at most 5) other
        /// Spell/Trap Cards the controller has on the field, sent when the card is activated; the controller gains
        /// <see cref="EffectClause.Amount"/> LP per card sent (<see cref="SimpleSpellTrapsRuntime"/>).
        /// </summary>
        GainLpPerSpellTrapSentAsCost,
        /// <summary>
        /// SpiritMonsters batch — Asura Priest (ContinuousWhileFaceUp marker): "This card can attack all monsters your
        /// opponent controls once each." (ygopro EFFECT_ATTACK_ALL). Its first attack is an ordinary one (a direct attack
        /// when the opponent controls no monsters); every further attack this Battle Phase must target an opponent's
        /// monster it has not attacked yet, never the opponent directly. Read by DuelEngine attack legality through
        /// <see cref="SpiritMonstersRuntime"/>; never resolved as an effect.
        /// </summary>
        AttackAllOpponentMonstersOnceEach,
        /// <summary>
        /// SpiritMonsters batch — Inaba White Rabbit (ContinuousWhileFaceUp marker, paired with
        /// <see cref="CanAttackDirectly"/>): "This monster attacks your opponent's Life Points directly." (ygopro
        /// EFFECT_CANNOT_SELECT_BATTLE_TARGET): it can never declare an attack on a monster. Read by DuelEngine attack
        /// legality through <see cref="SpiritMonstersRuntime"/>; never resolved as an effect.
        /// </summary>
        CannotSelectMonsterAsAttackTarget,
        /// <summary>
        /// SpiritMonsters batch — Susa Soldier (ContinuousWhileFaceUp marker): "The Battle Damage this card inflicts to
        /// your opponent's Life Points is halved." (ygopro EFFECT_CHANGE_BATTLE_DAMAGE HALF_DAMAGE). Applied by DuelEngine's
        /// damage calculation (<see cref="SpiritMonstersRuntime.HalveBattleDamage"/>); never resolved as an effect.
        /// </summary>
        HalveBattleDamageThisInflictsToOpponent,
        /// <summary>
        /// SpiritMonsters batch — Great Long Nose: "your opponent skips their next Battle Phase" (ygopro EFFECT_SKIP_BP,
        /// reset after the opponent's next turn). Sets <see cref="DuelistState.SkipNextBattlePhase"/>; the opponent's next
        /// turn cannot conduct a Battle Phase (<see cref="SpiritMonstersRuntime.OnTurnStart"/>).
        /// </summary>
        OpponentSkipsNextBattlePhase,
        /// <summary>
        /// SpiritMonsters batch — Maharaghi: "look at 1 card on the top of your Deck during your next Draw Phase (before
        /// you draw), and return the card to the top or the bottom of your Deck." (ygopro EVENT_PREDRAW registration).
        /// Sets <see cref="DuelistState.LookAtTopCardAtNextDrawPhase"/>; resolved by
        /// <see cref="SpiritMonstersRuntime.OnDrawPhaseStart"/>.
        /// </summary>
        LookAtTopCardAtNextDrawPhaseTopOrBottom,
        /// <summary>
        /// BothSideAndMonsterEquip batch (v161) — Sword Hunter (<see cref="EffectTiming.EndOfBattlePhase"/>): "Equip all
        /// those monsters [this card destroyed by battle and sent to the GY this Battle Phase] from the GY to this card as
        /// an Equip Spell Card(s) with this effect. ● The equipped monster gains <see cref="EffectClause.Amount"/> ATK."
        /// None when there are more of them than free Spell &amp; Trap Zones (Lua s.eqop). Each one is an Equip Spell
        /// Card with only that granted effect (<see cref="CardInstance.IsEquipSpellWithGrantedEffectOnly"/>);
        /// <see cref="BothSideAndMonsterEquipRuntime"/>.
        /// </summary>
        EquipMonstersThisDestroyedByBattleAsEquipSpells,
        /// <summary>
        /// CoinDiceInterrupts batch — Second Coin Toss (ContinuousWhileFaceUp marker): "When a coin toss is performed, you
        /// can choose to redo the coin toss. (If the toss involved multiple coin flips, redo all of them.)" once per turn
        /// (ygopro EVENT_TOSS_COIN_NEGATE, ep==tp, flag per player). Read by <see cref="CoinDiceInterruptsRuntime"/> on
        /// each of its controller's coin tosses; never resolved as an effect.
        /// </summary>
        RedoOwnCoinTossOncePerTurn,
        /// <summary>
        /// CoinDiceInterrupts batch — Dice Re-Roll: "One time, during the rest of this turn, if either player(s) rolls a
        /// die (or dice), you can have the player(s) re-roll." (ygopro EVENT_TOSS_DICE_NEGATE registered until the End
        /// Phase; flag per player when used). <see cref="CoinDiceInterruptsRuntime.GrantDiceReRoll"/>.
        /// </summary>
        GrantDiceReRollThisTurn,
        /// <summary>
        /// SpecialSummonEffects batch — Skull Knight #2 (ContinuousWhileFaceUp marker, a mandatory trigger resolved by
        /// <see cref="SpecialSummonEffectsRuntime.NotifyTributeSummonMaterials"/>): "If you Tribute Summon a
        /// <see cref="EffectClause.RaceFilter"/>-Type monster by Tributing this monster, Special Summon 1
        /// "<see cref="EffectClause.NamedCard"/>" from your Deck. Then shuffle your Deck." (ygopro EVENT_BE_MATERIAL,
        /// REASON_SUMMON, the summoned monster face-up and of that Type). Never resolved as an effect of its own.
        /// </summary>
        SpecialSummonNamedFromDeckWhenTributedForRaceSummon,
        /// <summary>
        /// SpecialSummonEffects batch — Twin-Headed Behemoth (<see cref="EffectTiming.EndPhase"/>, from the GY,
        /// optional, once per Duel): "During the End Phase, if this card is in the GY because it was destroyed on the
        /// field and sent there this turn: You can Special Summon this card, but its ATK/DEF become
        /// <see cref="EffectClause.Amount"/>." Fired by TextEffectRuntime.FirePhaseTriggersFor through
        /// <see cref="SpecialSummonEffectsRuntime.OnEndPhaseTrigger"/>; the ATK/DEF last while it stays face-up on
        /// the field and its effects are not negated (RESETS_STANDARD_DISABLE).
        /// </summary>
        SpecialSummonThisFromGyAtEndPhaseIfDestroyedThisTurn,
        /// <summary>
        /// SpecialSummonEffects batch — Winged Minion (Activate on a monster, cost Tribute this card, 1 target): the
        /// target gains <see cref="EffectClause.Amount"/> ATK and <see cref="EffectClause.DefAmount"/> DEF while it
        /// stays face-up on the field (ygopro EFFECT_UPDATE_ATTACK / _DEFENSE, RESET_EVENT | RESETS_STANDARD: it ends
        /// when the monster leaves the field or is turned face-down, not on a control change). Applied on each board
        /// refresh by <see cref="SpecialSummonEffectsRuntime.ApplyBoard"/>.
        /// </summary>
        TargetGainsAtkDefWhileFaceUp,
        /// <summary>
        /// SpecialSummonEffects batch — The Puppet Magic of Dark Ruler (Activate, 1 target in your GY): banish
        /// monsters you control whose Levels total exactly the target's Level (each Level &gt; 0), then Special Summon
        /// the target. Targets only a monster whose Level such a group can make. See
        /// <see cref="SpecialSummonEffectsRuntime.ResolvePuppetMagic"/>.
        /// </summary>
        BanishLevelSumThenSpecialSummonTarget,
        /// <summary>
        /// SpecialSummonEffects batch — Gradius' Option (ContinuousWhileFaceUp, no Chain Link): "The ATK and DEF of this
        /// card becomes the same as the ATK and DEF of the selected "<see cref="EffectClause.NamedCard"/>"." The card's
        /// own hand procedure selects 1 face-up monster with that name its controller controls; while this card is
        /// face-up and not negated, its ATK/DEF are that monster's current ATK/DEF (ygopro EFFECT_SET_ATTACK_FINAL /
        /// _DEFENSE_FINAL on GetFirstCardTarget). The program also makes the card summonable only by that procedure.
        /// See <see cref="SpecialSummonEffectsRuntime.OnSpecialSummoned"/> / <see cref="SpecialSummonEffectsRuntime.ApplyBoard"/>.
        /// </summary>
        AtkDefBecomeSelectedNamed,
        /// <summary>
        /// SpecialSummonEffects batch — Gradius' Option (ContinuousWhileFaceUp, no Chain Link): "If the selected
        /// "<see cref="EffectClause.NamedCard"/>" is removed from the field, destroy this card." (ygopro EFFECT_SELF_DESTROY
        /// when its card target is gone, while its summon flag lasts: RESETS_STANDARD_DISABLE). Checked on every board
        /// refresh by <see cref="SpecialSummonEffectsRuntime.ApplyBoard"/>.
        /// </summary>
        DestroyThisWhenSelectedNamedLeavesField,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Fatal Abacus (ContinuousWhileFaceUp marker): "Each time 1 monster is
        /// sent from the field to the Graveyard, inflict 500 points of damage per card to the respective card owner's Life
        /// Points." (ygopro c77910045: continuous EVENT_TO_GRAVE, IsPreviousLocation(MZONE), both damages together). Each
        /// monster that leaves a Monster Zone for a GY while this card is active is recorded; the next GY checkpoint
        /// inflicts Amount per monster to each GY's owner (<see cref="ContinuousTrapsAndSpiritSupportRuntime"/>). No Chain Link.
        /// </summary>
        InflictDamageToOwnerPerMonsterSentFromFieldToGy,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Life Absorbing Machine (StandbyPhase, mandatory trigger): "During your
        /// Standby Phase, gain Life Points equal to half of the total Life Points you paid during your last turn." (ygopro
        /// c14318794: every LP cost the turn player paid during their own turn, EVENT_PAY_LPCOST, half rounded up per
        /// payment). Costs only, never damage (<see cref="ContinuousTrapsAndSpiritSupportRuntime.NoteLpCostPaid"/>).
        /// </summary>
        GainHalfLpCostsPaidLastTurn,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Ominous Fortunetelling (StandbyPhase, mandatory trigger, once per turn):
        /// 1 card is picked at random from the opponent's hand, the controller calls Monster, Spell or Trap, the card is
        /// revealed, and a right call inflicts Amount damage to the opponent (ygopro c56995655). A human calls through
        /// declaration tokens in the ordinary card picker while the Standby Phase waits.
        /// </summary>
        CallTypeOfRandomOpponentHandCardBurn,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Fengsheng Mirror (Activate): look at the opponent's hand; if it holds
        /// Spirit monsters, the activator selects 1 of them and it is discarded (ygopro c37406863: not a target, chosen at
        /// resolution; REASON_DISCARD + REASON_EFFECT). With no Spirit the hand is only looked at.
        /// </summary>
        LookAtOpponentHandDiscardChosenSpirit,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Spirit's Invitation (ContinuousWhileFaceUp trigger marker): "Each time
        /// your Spirit monster returns to your hand, select 1 monster on your opponent's side of the field and return it to
        /// its owner's hand." (ygopro c92394653: TRIGGER_F on EVENT_TO_HAND for a face-up Spirit that left your Monster
        /// Zone for your hand; the OPPONENT selects 1 monster they control, not targeted). Fired by
        /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.FireSpiritReturnTriggers"/>; never resolved as a clause.
        /// </summary>
        OpponentReturnsOwnMonsterWhenYourSpiritReturnsToHand,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Spiritual Energy Settle Machine (ContinuousWhileFaceUp marker): Spirit
        /// monsters on either field do not return to the hand during the End Phase (ygopro EFFECT_SPIRIT_DONOT_RETURN),
        /// read by <see cref="SpiritsAndMiscRuntime.SpiritReturnDue"/>; never resolved as a clause.
        /// </summary>
        SpiritsDoNotReturnWhileThisFaceUp,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Spiritual Energy Settle Machine's maintenance (EndPhase, a cost that
        /// cannot be negated): "Discard 1 card from your hand during each of your End Phases. If you do not, destroy this
        /// card." The controller may discard 1 card of their choice (hand not empty), otherwise this card is destroyed.
        /// </summary>
        EndPhaseDiscardOrDestroyThis,
        /// <summary>
        /// ContinuousTrapsAndSpiritSupport batch — Spiritual Energy Settle Machine (ContinuousWhileFaceUp marker): "When
        /// this card is removed from the field, all face-up Spirit monsters return to the owners' hands." (ygopro
        /// EVENT_LEAVE_FIELD continuous, no Chain Link). Applied by the leave-field sweep of
        /// <see cref="ContinuousTrapsAndSpiritSupportRuntime"/>; never resolved as a clause.
        /// </summary>
        ReturnFaceUpSpiritsWhenThisLeavesField,
        /// <summary>
        /// EquipAndSpellProtection batch — Fusion Sword Murasame Blade (ContinuousWhileFaceUp, no Chain Link): "While
        /// equipped to a monster, this card cannot be destroyed by card effects." (ygopro EFFECT_INDESTRUCTABLE_EFFECT on
        /// itself, range SZONE). Checked where a card effect destroys a Spell/Trap
        /// (<see cref="EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction"/>); not while negated.
        /// </summary>
        ThisCannotBeDestroyedByCardEffectsWhileEquipped,
        /// <summary>
        /// EquipAndSpellProtection batch — Heart of Clear Water (ContinuousWhileFaceUp, no Chain Link): "The monster
        /// equipped with this card is not destroyed as a result of battle or by the effect of a card that targets the
        /// monster." (ygopro EFFECT_INDESTRUCTABLE_BATTLE + EFFECT_INDESTRUCTABLE_EFFECT whose value is a targeting effect
        /// that has the monster among the resolving Chain Link's targets.) Damage calculation applies normally.
        /// <see cref="EquipAndSpellProtectionRuntime.EquipPreventsBattleDestruction"/> /
        /// <see cref="EquipAndSpellProtectionRuntime.PreventsTargetedMonsterDestruction"/>.
        /// </summary>
        EquippedMonsterNotDestroyedByBattleOrTargetingEffects,
        /// <summary>
        /// EquipAndSpellProtection batch — Heart of Clear Water (ContinuousWhileFaceUp, no Chain Link): "If the equipped
        /// monster's ATK is <see cref="EffectClause.Amount"/> or higher, this card is destroyed." (ygopro EFFECT_SELF_DESTROY,
        /// checked on every board refresh by <see cref="EquipAndSpellProtectionRuntime.Refresh"/>; a destruction by this
        /// card's own effect; not while negated.)
        /// </summary>
        DestroyThisWhileEquippedMonsterAtkAtLeast,
        /// <summary>
        /// EquipAndSpellProtection batch — Magic Reflector (Activate): "Target 1 face-up Spell Card you control; place 1
        /// counter on it. If it would ever be destroyed, remove the counter instead." (ygopro c61844784: counter 0x102a,
        /// not a Spell Counter, and an EFFECT_DESTROY_REPLACE on that Spell that removes it instead of any destruction
        /// that is not by rule or another replacement; reset when the Spell leaves the field or is turned face-down.)
        /// <see cref="EquipAndSpellProtectionRuntime.PlaceReflectorCounter"/> /
        /// <see cref="EquipAndSpellProtectionRuntime.ReplacesDestruction"/>.
        /// </summary>
        PlaceDestroyReplacementCounterOnTarget,
        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161) — Fiber Jar: "Each player shuffles all cards from their hand, field,
        /// and Graveyard into the Deck, then draws <see cref="EffectClause.Amount"/> cards." Not banished cards, not a
        /// monster the current battle is destroying (Lua removes STATUS_BATTLE_DESTROYED), not a card unaffected by the
        /// effect, not a GY card Necrovalley locks; Extra Deck monsters go to the Extra Deck, Tokens vanish. Then the
        /// controller draws, then the opponent (<see cref="DeckReturnsAndMassEffectsRuntime.ResolveFiberJar"/>).
        /// </summary>
        ShuffleHandFieldGyIntoDeckThenEachDraws,
        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161) — Mysterious Guard: FLIP, target 1 face-up monster on the field and,
        /// if you control a face-up monster of the Type <see cref="EffectClause.ConditionName"/>, optionally 1 more; the first target
        /// goes on top of its owner's Deck, then the additional one (if any) to the hand if you still control a face-up
        /// monster of that Type. Targets are chosen as the effect activates
        /// (<see cref="DeckReturnsAndMassEffectsRuntime"/>, ordered roles).
        /// </summary>
        ReturnFirstTargetToDeckTopThenSecondToHandIfRace,
        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161) — Creature Swap: not targeted; at resolution the activator, then the
        /// opponent, chooses 1 monster they control; they switch control (each into the zone the other left) and cannot
        /// change battle position for the rest of the turn (<see cref="DeckReturnsAndMassEffectsRuntime"/>).
        /// </summary>
        EachPlayerChoosesOwnMonsterSwitchControl,
        /// <summary>
        /// DeckReturnsAndMassEffects batch (v161) — Bubble Crash: each player with
        /// <see cref="EffectClause.ConditionAmount"/> or more cards in hand + on the field selects cards there (the
        /// activator not this card) and sends them to the GY until <see cref="EffectClause.Amount"/> remain; all are sent
        /// together, by rule (<see cref="DeckReturnsAndMassEffectsRuntime"/>).
        /// </summary>
        EachPlayerSendsHandAndFieldToGyUntilAmount,
        /// <summary>
        /// BattleRules batch — Cave Dragon (rule marker): "Cannot be Normal Summoned while you control a monster."
        /// (ygopro SINGLE EFFECT_CANNOT_SUMMON with a condition: any monster in its controller's Monster Zones). Setting
        /// stays legal. Read by SummonProcedures.CheckNormalOrTribute through
        /// <see cref="BattleRulesRuntime.NormalSummonForbiddenByOwnText"/>; never resolved as an effect.
        /// </summary>
        CannotBeNormalSummonedWhileControllerHasMonster,
        /// <summary>
        /// BattleRules batch — Cave Dragon (ContinuousWhileFaceUp marker): "This card cannot declare an attack unless you
        /// control another [<see cref="EffectClause.RaceFilter"/>]-Type monster." (ygopro SINGLE EFFECT_CANNOT_ATTACK, the
        /// other monster face-up). Read by DuelEngine.ContinuousCannotAttackBlocks through
        /// <see cref="BattleRulesRuntime.AttackForbidden"/>; never resolved as an effect.
        /// </summary>
        CannotAttackUnlessControllerHasAnotherFaceUpRace,
        /// <summary>
        /// BattleRules batch — Thunder Nyan Nyan (ContinuousWhileFaceUp state): "If there is a non-[<see
        /// cref="EffectClause.AttributeFilter"/>] monster on your side of the field, destroy this face-up card." (ygopro
        /// EFFECT_SELF_DESTROY while its controller controls a face-up monster of another Attribute). Swept on each board
        /// refresh by <see cref="BattleRulesRuntime.SweepSelfDestroy"/>; not a Chain Link.
        /// </summary>
        SelfDestroyWhileControllerHasFaceUpNonAttribute,
        /// <summary>
        /// BattleRules batch — After the Struggle (Activate): every monster involved in damage calculation this turn
        /// (attacker and attack target, Lua EVENT_BATTLED flags) and still on the field is destroyed at the end of the
        /// Battle Phase (Lua EVENT_PHASE+PHASE_BATTLE, once). Runtime: <see cref="BattleRulesRuntime.ArmAfterTheStruggle"/>.
        /// </summary>
        DestroyMonstersThatBattledThisTurnAtEndOfBattlePhase,
        /// <summary>
        /// BattleRules batch — The Secret of the Bandit (Activate, targets 1 face-up monster): this turn, each time that
        /// monster inflicts battle damage to the activator's opponent, that opponent discards 1 random card (Lua
        /// EVENT_BATTLE_DAMAGE trigger registered for the turn). Runtime: <see cref="BattleRulesRuntime.ArmBandit"/>.
        /// </summary>
        OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn,
        /// <summary>
        /// BattleRules batch — Patrician of Darkness (ContinuousWhileFaceUp marker): "You choose the attack targets for your
        /// opponent's attacks." (ygopro EFFECT_PATRICIAN_OF_DARKNESS). Read by DuelEngine.TryAttack through
        /// <see cref="BattleRulesRuntime.TryDefenderChoosesTarget"/>; never resolved as an effect.
        /// </summary>
        YouChooseOpponentsAttackTargets,
        /// <summary>
        /// BattleBoosts batch (v161) — Spear Dragon, <see cref="EffectTiming.EndOfDamageStep"/> with
        /// <see cref="EffectClause.DamageStepOnlyWhenAttacking"/>: "If this card attacks, it is changed to Defense
        /// Position at the end of the Damage Step." (ygopro EVENT_DAMAGE_STEP_END continuous effect, Duel.GetAttacker()
        /// == this card and still related to the battle): face-up Defense Position if it is in Attack Position; no
        /// position lock, not a Chain Link.
        /// </summary>
        ChangeThisToDefenseAtEndOfDamageStep,
        /// <summary>
        /// BattleBoosts batch (v161) — Dragon's Rage, <see cref="EffectTiming.ContinuousWhileFaceUp"/>: face-up monsters
        /// of <see cref="EffectClause.RaceFilter"/> (the live Type) its controller controls inflict piercing battle
        /// damage (ygopro EFFECT_PIERCE, EFFECT_TYPE_FIELD, LOCATION_MZONE of the controller). Read by
        /// <see cref="BattleBoostsRuntime.FieldGrantsPiercing"/>.
        /// </summary>
        GrantPiercingToYourRaceMonsters,
        /// <summary>
        /// BattleBoosts batch (v161) — Gray Wing, a monster ignition: "This monster can attack twice during the Battle
        /// Phase of this turn." <see cref="EffectClause.Amount"/> extra attacks for this face-up copy this turn
        /// (ygopro EFFECT_EXTRA_ATTACK with RESETS_STANDARD_PHASE_END and EFFECT_FLAG_CANNOT_DISABLE). Read by
        /// DuelEngine.ExtraAttacksAllowed through <see cref="BattleBoostsRuntime.TurnExtraAttacks"/>.
        /// </summary>
        GrantThisExtraAttackThisTurn,
        /// <summary>
        /// BattleBoosts batch (v161) — The Hunter with 7 Weapons, <see cref="EffectTiming.ThisCardSummoned"/> (Normal
        /// Summon): declare 1 Monster Type (a declaration token from <see cref="EffectZoneFilter.DeclaredMonsterTypes"/>,
        /// not a target); while this copy stays face-up on the field it is the Type its battle boost looks for
        /// (<see cref="BattleBoostsRuntime.DeclaredTypeOf"/>).
        /// </summary>
        DeclareTypeForThisCardBattles,
        /// <summary>
        /// BattleBoosts batch (v161) — The Hunter with 7 Weapons, <see cref="EffectTiming.ContinuousWhileFaceUp"/>: when
        /// this card battles a monster of the Type it declared (attacking or attacked), it gains
        /// <see cref="EffectClause.Amount"/> ATK during that damage calculation only (ygopro EVENT_PRE_DAMAGE_CALCULATE
        /// trigger, RESET_PHASE+PHASE_DAMAGE_CAL).
        /// </summary>
        GainAtkThisDamageCalculationVsDeclaredType,
        /// <summary>
        /// BattleBoosts batch (v161) — Injection Fairy Lily, <see cref="EffectTiming.MonsterQuickEffect"/>: at damage
        /// calculation of a battle this card is in (either turn), pay <see cref="EffectClause.PayLpAmount"/> LP (cost,
        /// once per battle); it gains <see cref="EffectClause.Amount"/> ATK during that damage calculation only.
        /// </summary>
        GainAtkThisDamageCalculation,
        /// <summary>
        /// BattleBoosts batch (v161) — Spirit Ryu, <see cref="EffectTiming.MonsterQuickEffect"/>: during its controller's
        /// Battle Step while it battles, discard 1 monster of <see cref="EffectClause.DiscardCostRace"/> (cost); it gains
        /// <see cref="EffectClause.Amount"/> ATK and <see cref="EffectClause.DefAmount"/> DEF until the end of the Battle
        /// Phase (ygopro RESET_PHASE+PHASE_BATTLE, RESETS_STANDARD_DISABLE).
        /// </summary>
        GainAtkDefUntilEndOfBattlePhase,
        /// <summary>
        /// NegationMonsters batch (v161) — Freed the Matchless General / Frontier Wiseman / Tyrant Dragon
        /// (ContinuousWhileFaceUp marker): "Negate any Spell (Trap) effects that target this card (a Warrior-Type
        /// monster you control), and if you do, destroy that card." (ygopro EVENT_CHAIN_SOLVING + NegateEffect, then
        /// Destroy when the card is still related.) <see cref="EffectClause.NegateRespondsToSpell"/> /
        /// <see cref="EffectClause.NegateRespondsToTrap"/> name the card family; <see cref="EffectClause.AffectsThisCardOnly"/>
        /// protects this card, otherwise <see cref="EffectClause.RaceFilter"/> names the face-up monsters you control it
        /// protects. Applied as each Chain Link resolves (<see cref="NegationMonstersRuntime.TryNegateTargetingLink"/>).
        /// </summary>
        NegateSpellTrapEffectsThatTargetAndDestroy,
        /// <summary>
        /// NegationMonsters batch (v161) — Freed the Matchless General (ContinuousWhileFaceUp marker): "During your Draw
        /// Phase, instead of conducting your normal draw: You can add 1 Level N or lower X-Type monster from your Deck to
        /// your hand. This card must be face-up on the field to activate and to resolve this effect." (ygopro EVENT_PREDRAW
        /// TRIGGER_O, EFFECT_DRAW_COUNT 0). <see cref="EffectClause.Zone"/> DeckMonstersRaceLevelLeq with
        /// <see cref="EffectClause.Amount"/> (the Level cap) and <see cref="EffectClause.RaceFilter"/>. Offered by
        /// <see cref="NegationMonstersRuntime.OfferSearchInsteadOfDraw"/> before the draw.
        /// </summary>
        AddFromDeckInsteadOfNormalDraw,
        /// <summary>
        /// NegationMonsters batch (v161) — Dark Ruler Ha Des / Twin-Headed Wolf (ContinuousWhileFaceUp marker): "Negate the
        /// effects of monsters destroyed by battle with X monsters you control" / "While you control another X monster,
        /// negate the effects of Flip monsters destroyed by battle with this card" (ygopro EVENT_BATTLED, EFFECT_DISABLE
        /// with RESETS_STANDARD_EXC_GRAVE: negated on the field and while it stays in the GY). <see cref="EffectClause.RaceFilter"/>:
        /// the Type of the battling monster you control; <see cref="EffectClause.AffectsThisCardOnly"/>: the battling
        /// monster is this card; <see cref="EffectClause.TargetCardKind"/>: the destroyed monster's card type ("Flip");
        /// <see cref="EffectClause.CountRaceFilter"/> + <see cref="EffectClause.ExceptThisCard"/>: the "while you control
        /// another X monster" condition. Applied by <see cref="NegationMonstersRuntime.OnBattled"/>.
        /// </summary>
        NegateEffectsOfMonstersDestroyedByBattle,
        /// <summary>
        /// NegationMonsters batch (v161) — Lesser Fiend (ContinuousWhileFaceUp marker): "Banish any monster destroyed by
        /// battle with this card." (ygopro EFFECT_BATTLE_DESTROY_REDIRECT, LOCATION_REMOVED): it is banished instead of
        /// being sent to the GY (<see cref="NegationMonstersRuntime.BanishesBattleDestroyed"/>).
        /// </summary>
        BanishMonstersDestroyedByBattleWithThis,
        /// <summary>
        /// EffectDamageEvents batch (v162) — Barrel Behind the Door (ChainLinkActivated, Counter Trap): answers a Chain Link
        /// that would inflict effect damage to its controller; as it resolves, the effect damage that link would inflict to
        /// its controller goes to their opponent instead (ygopro EFFECT_REFLECT_DAMAGE, reset at the end of the Chain).
        /// <see cref="EffectDamageEventsRuntime"/>.
        /// </summary>
        ReflectEffectDamageOfAnsweredLink,
        /// <summary>
        /// EffectDamageEvents batch (v162) — Trap of Board Eraser (ChainLinkActivated, Counter Trap): answers a Chain Link
        /// that would inflict effect damage to its controller (the opponent must hold a card); as it resolves, the effect
        /// damage that link would inflict to its controller becomes 0 (ygopro EFFECT_CHANGE_DAMAGE) and the opponent discards
        /// 1 card of their choice. <see cref="EffectDamageEventsRuntime"/>.
        /// </summary>
        NegateEffectDamageOfAnsweredLinkThenOpponentDiscards,
        /// <summary>
        /// EffectDamageEvents batch (v162) — Dark Room of Nightmare (ContinuousWhileFaceUp marker): each time the controller's
        /// opponent takes effect damage from a card effect other than a card with this card's name, a mandatory trigger
        /// inflicts <see cref="EffectClause.Amount"/> to them (queued as a SEGOC trigger, <see cref="EffectDamageEventsRuntime"/>).
        /// </summary>
        InflictDamageEachTimeOpponentTakesEffectDamage,
        /// <summary>
        /// EffectDamageEvents batch (v162) — Gravekeeper's Vassal (ContinuousWhileFaceUp marker): the battle damage this card
        /// inflicts to its opponent is effect damage instead (ygopro EFFECT_BATTLE_DAMAGE_TO_EFFECT; DuelEngine damage step via
        /// <see cref="EffectDamageEventsRuntime.BattleDamageAsEffect"/>).
        /// </summary>
        BattleDamageThisInflictsIsEffectDamage,
        /// <summary>
        /// EffectDamageEvents batch (v162) — Coffin Seller (ContinuousWhileFaceUp marker): each time a monster(s) is sent to
        /// the controller's opponent's GY (one event, however many monsters), a mandatory trigger inflicts
        /// <see cref="EffectClause.Amount"/> to that opponent (<see cref="EffectDamageEventsRuntime.GySendCheckpoint"/>).
        /// </summary>
        InflictDamageEachTimeMonsterSentToOpponentGy,

        // ── PgdSpellTrapBasics batch (v162 PGD) ──

        /// <summary>
        /// PgdSpellTrapBasics batch: Book of Taiyou (c38699854) — the target, if it is still a face-down monster on the
        /// field, is flipped to face-up Attack Position (Duel.ChangePosition without NO_FLIP_EFFECT): its FLIP effect
        /// activates once this effect has resolved (DuelEngine.QueueFlipEffectAfterResolution).
        /// </summary>
        FlipFaceDownTargetToFaceUpAttack,
        /// <summary>
        /// PgdSpellTrapBasics batch: Curse of Aging (c41398771) / Pyramid Energy (c76754619) — every face-up monster
        /// <see cref="EffectClause.Side"/> controls when this resolves gains <see cref="EffectClause.Amount"/> ATK and
        /// <see cref="EffectClause.DefAmount"/> DEF (negative = loses) until the End Phase. Not a target.
        /// </summary>
        ModifyAllFaceUpMonstersAtkDefUntilEndPhase,
        /// <summary>
        /// PgdSpellTrapBasics batch: D. Tribe (c2833249) — every face-up monster you control when this resolves
        /// becomes <see cref="EffectClause.BecomesRace"/> until the End Phase (EFFECT_CHANGE_RACE,
        /// RESETS_STANDARD_PHASE_END). Monsters that arrive later are not affected.
        /// </summary>
        AllYourFaceUpMonstersBecomeTypeUntilEndPhase,
        /// <summary>
        /// PgdSpellTrapBasics batch: Needle Ceiling (c38411870) — destroy every face-up monster on the field, in one
        /// event. Face-down monsters stay.
        /// </summary>
        DestroyAllFaceUpMonstersOnField,
        /// <summary>
        /// PgdSpellTrapBasics batch: Secret Pass to the Treasures (c77876207) — the target, if it is still a face-up
        /// monster on the field, can attack directly this turn (EFFECT_DIRECT_ATTACK, RESETS_STANDARD_PHASE_END).
        /// </summary>
        TargetCanAttackDirectlyThisTurn,
        /// <summary>
        /// PgdSpellTrapBasics batch: Hieroglyph Lithograph (c10248192) — the activating player's hand size limit becomes
        /// <see cref="EffectClause.Amount"/> for the rest of the Duel (a player effect, not tied to the card).
        /// </summary>
        ControllerHandSizeLimitBecomesForDuel,
        /// <summary>
        /// PgdSpellTrapBasics batch: Card Shuffle (c12183332) — the activating player chooses their own Deck or the
        /// opponent's when this resolves, and that Deck is shuffled.
        /// </summary>
        ShuffleYourOrOpponentsDeck,
        /// <summary>
        /// BattleOutcomeTriggers batch (v162) — Zombyra the Dark (<see cref="EffectTiming.ThisCardDestroysByBattle"/>): "If
        /// this card destroys a monster by battle: This card loses N ATK." (ygopro c88472456 EVENT_BATTLE_DESTROYING
        /// TRIGGER_F, EFFECT_UPDATE_ATTACK with RESETS_STANDARD_DISABLE). <see cref="EffectClause.Amount"/> is the signed
        /// change. It lasts while this card stays face-up on the field and ends for good once its effects are negated
        /// (<see cref="BattleOutcomeTriggersRuntime"/>, a lasting grant of <see cref="SpecialSummonEffectsRuntime"/>).
        /// </summary>
        ChangeThisCardAtkWhileFaceUp,
        /// <summary>
        /// BattleOutcomeTriggers batch (v162) — Timeater (<see cref="EffectTiming.ThisCardDestroysByBattle"/>): "Your
        /// opponent skips their next Main Phase 1." (ygopro c44913552 EFFECT_SKIP_M1, reset at the opponent's Main Phase 1).
        /// The opponent's next turn goes from the Standby Phase to the Battle Phase, or to the End Phase when they cannot
        /// conduct a Battle Phase (DuelEngine.EnterMain1, <see cref="BattleOutcomeTriggersRuntime.MainPhase1SkipPending"/>).
        /// </summary>
        OpponentSkipsNextMainPhase1,
        /// <summary>
        /// BattleOutcomeTriggers batch (v162) — Kryuel (<see cref="EffectTiming.SentFromFieldToGy"/> with
        /// <see cref="EffectClause.RequiresThisDestroyedByBattle"/>): "toss a coin and call it. If you call it right,
        /// destroy 1 monster on your opponent's side of the field." (ygopro c82642348 Duel.CallCoin(tp), then
        /// SelectMatchingCard on the opponent's Monster Zone: a selection, not a target, face-down monsters included).
        /// </summary>
        CoinCallRightDestroyOneOppMonster,
        /// <summary>
        /// BattleOutcomeTriggers batch (v162) — Dark Scorpion Burglars (<see cref="EffectTiming.ThisCardInflictsBattleDamage"/>):
        /// "Your opponent sends 1 Spell Card from their Deck to the Graveyard." (ygopro c40933924: the opponent selects the
        /// Spell, REASON_EFFECT). No Spell in their Deck: nothing happens. A human opponent picks through the target picker.
        /// </summary>
        OpponentSendsSpellFromDeckToGy,
        /// <summary>
        /// BattleOutcomeTriggers batch (v162) — Mucus Yolk (<see cref="EffectTiming.ThisCardInflictsBattleDamage"/>): "Each time
        /// this card inflicts Battle Damage to your opponent's Life Points, increase the ATK of this card by N points during
        /// your next Standby Phase. The ATK increase lasts as long as this card remains face-up on the field." (ygopro
        /// c70307656: a flag reset at the end of your next Standby Phase, then a once-per-turn mandatory Standby trigger
        /// that adds N once, however many times damage was inflicted; RESETS_STANDARD_DISABLE).
        /// </summary>
        GainThisCardAtkAtNextStandbyWhileFaceUp,
        /// <summary>
        /// FlipDownAndPeek batch (v162) — 8-Claws Scorpion (ContinuousWhileFaceUp marker): "When this card attacks an
        /// opponent's face-down Defense Position monster, this card's ATK becomes N during damage calculation only."
        /// (ygopro c14261867: TRIGGER_F at EVENT_PRE_DAMAGE_CALCULATE, attacker whose battle target's battle position is
        /// face-down Defense; EFFECT_SET_ATTACK_FINAL reset at the end of damage calculation). <see cref="EffectClause.Amount"/>:
        /// the ATK. Applied by <see cref="FlipDownAndPeekRuntime.ApplyDamageCalculationAtk"/>.
        /// </summary>
        AtkBecomesAmountWhenAttackingFaceDownDefenseThisCalculation,
        /// <summary>
        /// FlipDownAndPeek batch (v162) — Giant Axe Mummy (<see cref="EffectTiming.EndOfDamageStep"/>): attacked in
        /// face-down Defense Position (its battle position) by a monster whose ATK is lower than this card's DEF, that
        /// attacking monster is destroyed (ygopro c78266168: TRIGGER_F at EVENT_DAMAGE_STEP_END, destroy the attacker if it
        /// still relates to the battle). <see cref="FlipDownAndPeekRuntime.ResolveDestroyLowerAtkAttacker"/>.
        /// </summary>
        DestroyAttackerWithLowerAtkThanThisDefWhenAttackedFaceDown,
        /// <summary>
        /// FlipDownAndPeek batch (v162) — Cobraman Sakuzy: "look at all Set Spell and Trap Cards on your opponent's side of
        /// the field and then return them to their original position" (ygopro Duel.ConfirmCards on the face-down cards of
        /// the opponent's LOCATION_SZONE). Logged for the looking player; the cards stay Set.
        /// </summary>
        LookAtOpponentSetSpellTraps,
        /// <summary>
        /// FlipDownAndPeek batch (v162) — A Man with Wdjat: "select 1 Set card on your opponent's side of the field. Pick it
        /// up and look at it, then return it to its original position" (ygopro c51351302: EFFECT_FLAG_CARD_TARGET, 1
        /// face-down card the opponent controls, Duel.ConfirmCards if it is still face-down). Zone OppAnyCardOnField with
        /// <see cref="EffectClause.RequiresTargetFaceDown"/>; a Standby Phase clause picks through
        /// <see cref="FlipDownAndPeekRuntime.ResolveLookAtSetCard"/>.
        /// </summary>
        LookAtTargetSetCard,
        /// <summary>
        /// FlipDownAndPeek batch (v162) — Trap Dustshoot: "Look at your opponent's hand, select 1 Monster Card in it, and
        /// return that card to its owner's Deck." (ygopro c64697231: ConfirmCards the hand, select 1 monster at resolution,
        /// SendtoDeck SEQ_DECKSHUFFLE). <see cref="FlipDownAndPeekRuntime.ResolveHandMonsterToDeck"/>.
        /// </summary>
        LookAtOpponentHandReturnChosenMonsterToDeck,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Blind Destruction (StandbyPhase, mandatory, once per turn, your Standby): roll
        /// a six-sided die (<see cref="CoinDiceInterruptsRuntime.RollDie"/>); 1-5: destroy every face-up monster on the field
        /// with that Level, 6: every face-up monster of Level 6 or higher, in one event
        /// (<see cref="StandbyBurnsAndDiceRuntime.ResolveBlindDestruction"/>).
        /// </summary>
        RollDieDestroyFaceUpMonstersWithRolledLevel,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Needle Wall (StandbyPhase, mandatory, once per turn, your Standby): roll a die
        /// again while it shows 6, then destroy the monster in the opponent's Main Monster Zone numbered by the result,
        /// counting from your right (ygopro sequence result-1; <see cref="StandbyBurnsAndDiceRuntime.ResolveNeedleWall"/>).
        /// </summary>
        RollDieDestroyOppMonsterInRolledZone,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Dark Snake Syndrome (StandbyPhase, mandatory, your Standby): damage to both
        /// players at once, <see cref="EffectClause.Amount"/> the first time and double the previous amount each later
        /// resolution while this card stays face-up on the field (<see cref="StandbyBurnsAndDiceRuntime.ResolveDarkSnake"/>).
        /// </summary>
        InflictDoublingDamageToBothPlayers,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Mask of Dispel (Activate): the targeted face-up Spell becomes this card's
        /// linked card (ygopro SetCardTarget; <see cref="StandbyBurnsAndDiceRuntime.LinkMask"/>).
        /// </summary>
        LinkThisToTargetSpell,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Mask of Dispel (StandbyPhase, mandatory, your Standby): the current controller
        /// of this card's linked Spell takes <see cref="EffectClause.Amount"/> damage
        /// (<see cref="StandbyBurnsAndDiceRuntime.ResolveMaskBurn"/>).
        /// </summary>
        InflictDamageToLinkedSpellController,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Mask of Dispel (ContinuousWhileFaceUp marker): "When the selected card is
        /// removed from the field, destroy this card" (ygopro EVENT_LEAVE_FIELD continuous effect; not a Chain Link, not
        /// while this card is negated). Applied by <see cref="StandbyBurnsAndDiceRuntime.OnBoardSettled"/>.
        /// </summary>
        DestroyThisWhenLinkedSpellLeavesField,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Nightmare Wheel (StandbyPhase, mandatory, once per turn, your Standby): inflict
        /// <see cref="EffectClause.Amount"/> damage to the opponent, only while the monster this card is bound to is on the
        /// field (<see cref="StandbyBurnsAndDiceRuntime.ResolveWheelBurn"/>).
        /// </summary>
        InflictDamageToOpponentWhileBoundMonsterOnField,
        /// <summary>
        /// StandbyBurnsAndDice batch (v162) — Inspection (StandbyPhase, <see cref="EffectClause.OpponentTurnOnly"/>, optional,
        /// once per turn): while the opponent's hand is not empty, pay <see cref="EffectClause.Amount"/> LP (a cost) to look
        /// at 1 random card in their hand, then they shuffle it (<see cref="StandbyBurnsAndDiceRuntime.ResolveInspection"/>).
        /// </summary>
        PayLpLookAtRandomOppHandCard,
        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162) — Crimson Sentry / Fairy Guardian: the target card in your GY goes to
        /// the bottom of its owner's Deck (a Fusion Monster to the Extra Deck), if it is still there (ygopro SendtoDeck
        /// SEQ_DECKBOTTOM). <see cref="BattleDestroyedGyRecoveryRuntime.PlaceGyTargetOnBottomOfDeck"/>.
        /// </summary>
        PlaceGyTargetOnBottomOfDeck,
        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162) — Return of the Doomed: registers a this-turn End Phase effect of the
        /// activating player: if a monster in their GY was sent there this turn by battle, they choose 1 and add it to
        /// the hand (no target; <see cref="BattleDestroyedGyRecoveryRuntime.OnEndPhase"/>).
        /// </summary>
        AddMonsterDestroyedByBattleThisTurnFromGyToHandAtEndPhase,
        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162) — Revival Jam's optional trigger (destroyed by battle and sent to the
        /// GY; cost <see cref="EffectClause.PayLpAmount"/>): flags this card for its controller's next Standby Phase,
        /// as long as it stays in the GY (<see cref="BattleDestroyedGyRecoveryRuntime.ArmRevival"/>).
        /// </summary>
        ArmThisSpecialSummonAtNextStandby,
        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162) — Revival Jam's Standby Phase (no Chain Link): the flagged card is
        /// Special Summoned from its GY in face-up Defense Position (<see cref="BattleDestroyedGyRecoveryRuntime.OnStandby"/>).
        /// </summary>
        SpecialSummonArmedThisFromGyInDefenseAtStandby,
        /// <summary>
        /// BattleDestroyedGyRecovery batch (v162) — Rope of Life: Special Summon the target (1 of the monsters destroyed
        /// by battle and sent to your GY) in the face-up position its controller picks; if it is Summoned, it gains
        /// <see cref="EffectClause.Amount"/> ATK while it stays face-up on the field.
        /// </summary>
        SpecialSummonBattleDestroyedTargetGainsAtk,
        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Arsenal Bug (<see cref="EffectTiming.ContinuousWhileFaceUp"/>): "If you
        /// control no other <see cref="EffectClause.RaceFilter"/>-Type monsters, this card's ATK and DEF become
        /// <see cref="EffectClause.Amount"/> / <see cref="EffectClause.DefAmount"/>." (ygopro c42364374 EFFECT_SET_ATTACK /
        /// _DEFENSE, SINGLE_RANGE, condition: no other face-up monster of that Type you control). Recomputed on every board
        /// refresh; the value is the base the other ATK/DEF changes add to (<see cref="ContinuousLocksAndIgnitionsRuntime"/>).
        /// </summary>
        ThisCardAtkDefBecomeUnlessControllerHasAnotherFaceUpRace,
        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Timidity: "Neither player can destroy Set Spell and Trap Cards on the
        /// field until your opponent's next End Phase." (ygopro c40350910: a player-registered EFFECT_INDESTRUCTABLE_EFFECT on
        /// every face-down card in either Spell &amp; Trap Zone, Set later included, reset at the 2nd End Phase).
        /// </summary>
        SetSpellTrapsCannotBeDestroyedUntilOpponentsNextEndPhase,
        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Non Aggression Area: "Your opponent cannot Normal Summon, Set and/or
        /// Special Summon during his/her next turn." (ygopro c76848240: EFFECT_CANNOT_SUMMON / _MSET / _SPECIAL_SUMMON on the
        /// opponent, from the next turn, reset at the 2nd End Phase).
        /// </summary>
        OpponentCannotSummonOrSetDuringNextTurn,
        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Call of the Mummy / Frontline Base
        /// (<see cref="EffectTiming.FaceUpFieldIgnition"/>, once per turn): Special Summon 1 monster from your hand
        /// (<see cref="EffectClause.RaceFilter"/>; <see cref="EffectClause.TargetCardKind"/> "Union" with a Level cap in
        /// <see cref="EffectClause.Amount"/> / <see cref="EffectClause.AmountIsLevel"/>). Activated and resolved through
        /// <see cref="ContinuousLocksAndIgnitionsRuntime"/> (PerCountBurnsRuntime's face-up ignition hooks).
        /// </summary>
        SpecialSummonFromHandByFaceUpIgnition,
        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Buster Rancher (<see cref="EffectTiming.ContinuousWhileFaceUp"/> on an
        /// Equip Spell): during damage calculation the equipped monster gains <see cref="EffectClause.Amount"/> ATK if the
        /// opponent's monster battling it is in Attack Position with <see cref="EffectClause.ConditionAmount"/> or more ATK,
        /// or in Defense Position with that much DEF or more (ygopro c84740193 EFFECT_UPDATE_ATTACK, s.atkcon).
        /// </summary>
        EquippedGainsAtkInDamageCalcVsStrongBattler,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Bottomless Shifting Sand (End Phase of the opponent's turn, mandatory): destroy
        /// every face-up monster on the field whose current ATK is the highest (all of them when tied; ygopro GetMaxGroup),
        /// at the same time (<see cref="PhaseTimersAndDeckTopRuntime.DestroyHighestAtkFaceUpMonsters"/>).
        /// </summary>
        DestroyAllFaceUpMonstersWithHighestAtk,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Bottomless Shifting Sand's continuous check (not a Chain Link): in its
        /// controller's Standby Phase, while they hold <see cref="EffectClause.Amount"/> or fewer cards in hand, this card is
        /// destroyed. Run before the Standby triggers by <see cref="PhaseTimersAndDeckTopRuntime.OnStandbyPhase"/>; the
        /// ApplyClause arm does nothing.
        /// </summary>
        DestroyThisInOwnStandbyIfHandAtMost,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Mirage of Nightmare (opponent's Standby Phase, mandatory, once per turn): while
        /// its controller holds fewer than <see cref="EffectClause.Amount"/> cards, they draw until they hold that many, and the
        /// number to draw is recorded on this field presence (<see cref="PhaseTimersAndDeckTopRuntime.ResolveMirageDraw"/>).
        /// </summary>
        DrawUntilHandSizeRecordForNextStandbyDiscard,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Mirage of Nightmare (its controller's Standby Phase, mandatory, once per turn,
        /// only with a record): discard that many cards from the hand at random, or the whole hand
        /// (<see cref="PhaseTimersAndDeckTopRuntime.ResolveMirageDiscard"/>).
        /// </summary>
        DiscardRandomRecordedDrawCountAtStandby,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Different Dimension Capsule (a Normal Spell that stays on the field): its
        /// controller chooses 1 card in their Deck and banishes it face-down; in their 2nd Standby Phase after the activation
        /// this card is destroyed and that card, if it is still banished, is added to the hand
        /// (<see cref="PhaseTimersAndDeckTopRuntime.ResolveCapsule"/> / <see cref="PhaseTimersAndDeckTopRuntime.OnStandbyPhase"/>).
        /// </summary>
        BanishDeckCardFaceDownAddAtSecondStandby,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Pyro Clock of Destiny: choose 1 effect that counts turns (ygopro effect code
        /// 1082946) and run its turn step once (<see cref="PhaseTimersAndDeckTopRuntime.TurnCounts"/>). Unique exception.
        /// </summary>
        AdvanceTurnCountByOne,
        /// <summary>
        /// PhaseTimersAndDeckTop batch (v163) — Royal Tribute: both players discard every monster in their hands, at the same
        /// time (REASON_DISCARD; <see cref="PhaseTimersAndDeckTopRuntime.ResolveRoyalTribute"/>).
        /// </summary>
        BothPlayersDiscardAllMonstersInHand,
        /// <summary>
        /// CallsDiceAndSetTriggers batch (v163) — Dark Coffin (destroyed and sent to the GY while face-down): the
        /// controller's opponent selects and executes 1 available option — discard 1 random card from their hand, or
        /// select 1 monster they control and destroy it (<see cref="CallsDiceAndSetTriggersRuntime.ResolveDarkCoffin"/>).
        /// </summary>
        OpponentDiscardsRandomOrDestroysOwnMonster,
        /// <summary>
        /// CallsDiceAndSetTriggers batch (v163) — Dice Jar (FLIP): each player rolls 1 die (one die-roll event; rolled
        /// again while equal); the lower roller takes the other roll x <see cref="EffectClause.Amount"/> damage, or
        /// <see cref="EffectClause.MaxRollDamage"/> when the higher roll is a 6
        /// (<see cref="CallsDiceAndSetTriggersRuntime.ResolveDiceJar"/>).
        /// </summary>
        BothPlayersRollDieLowerTakesHigherRollDamage,
        /// <summary>
        /// CallsDiceAndSetTriggers batch (v163) — Ordeal of a Traveler: optional trigger of the face-up card when an
        /// opponent's monster declares an attack (needs a card in your hand); the opponent calls the type of 1 random
        /// card of your hand, and a wrong call returns the attacker to the hand. Fired by
        /// BattleWindowsRuntime.FireOpponentAttackDeclaredTriggers (<see cref="CallsDiceAndSetTriggersRuntime.StartOrdeal"/>);
        /// a marker, never applied by ApplyClause.
        /// </summary>
        OpponentCallsRandomHandCardTypeWrongReturnsAttacker,
        /// <summary>
        /// CallsDiceAndSetTriggers batch (v163) — Reversal Quiz: you call the type of your top Deck card, it is revealed,
        /// a right call exchanges both players' current LP (set, not damage or gain); then you shuffle your Deck. Its
        /// cost is <see cref="EffectClause.CostSendAllOtherHandAndFieldCardsToGy"/>
        /// (<see cref="CallsDiceAndSetTriggersRuntime.ResolveReversalQuiz"/>).
        /// </summary>
        CallTopDeckCardTypeRightExchangesLp,
        /// <summary>
        /// CallsDiceAndSetTriggers batch (v163) — Reasoning: the opponent declares a Level 1-12; excavate from the top of
        /// your Deck until a monster that can be Normal Summoned/Set; same Level: every excavated card goes to the GY;
        /// otherwise Special Summon it (if possible) and the rest go to the GY; none: reveal the Deck and shuffle
        /// (<see cref="CallsDiceAndSetTriggersRuntime.ResolveReasoning"/>).
        /// </summary>
        OpponentDeclaresLevelExcavateUntilSummonableMonster,
        /// <summary>
        /// TargetNegationsAndHandQuickEffects batch (v163) — Great Dezard (with <see cref="EffectClause.ConditionAmount"/>
        /// = the monsters it must have destroyed by battle) and Fushioh Richie (0), a <see cref="EffectTiming.ContinuousWhileFaceUp"/>
        /// marker for ygopro QUICK_F on EVENT_CHAINING: when a Spell/Trap Card is activated targeting this face-up card, a
        /// mandatory Chain Link negates that activation and destroys it
        /// (<see cref="TargetNegationsAndHandQuickEffectsRuntime.TrySeatForcedNegations"/>).
        /// </summary>
        NegateSpellTrapActivationTargetingThisAndDestroy,
        /// <summary>
        /// TargetNegationsAndHandQuickEffects batch (v163) — Charm of Shabti: until the End Phase, monsters of the
        /// <see cref="EffectClause.NamedCard"/> series that the activating player controls cannot be destroyed by battle
        /// (a player-registered effect: monsters that arrive later are covered too;
        /// <see cref="TargetNegationsAndHandQuickEffectsRuntime.BattleIndestructible"/>).
        /// </summary>
        SeriesMonstersCannotBeDestroyedByBattleUntilEndPhase,
        /// <summary>
        /// SummonTriggersAndTributes batch (v163) — Byser Shock: every face-down card on the field (both sides; Monster
        /// Zones, Spell &amp; Trap Zones, Field Zone) returns to its owner's hand at once, one return event
        /// (<see cref="SummonTriggersAndTributesRuntime.ReturnAllSetCardsToHand"/>).
        /// </summary>
        ReturnAllSetCardsOnFieldToHand,
        /// <summary>
        /// SummonTriggersAndTributes batch (v163) — King Tiger Wanghu (ContinuousWhileFaceUp marker of a field trigger):
        /// when a monster is Normal (incl. Tribute) or Special Summoned face-up with <see cref="EffectClause.ConditionAmount"/>
        /// or less ATK (either player, not this card), this face-up card destroys it if it is still face-up with that ATK
        /// (<see cref="SummonTriggersAndTributesRuntime.OnSummonSettled"/>, fired with the summon-success notify).
        /// </summary>
        DestroySummonedFaceUpMonstersAtkLeqWhenNormalOrSpecialSummoned,
        /// <summary>
        /// SummonTriggersAndTributes batch (v163) — "You can only control 1 face-up "<see cref="EffectClause.NamedCard"/>""
        /// (ygopro SetUniqueOnField(1,0,code)): a rule of the card, not an effect. A copy cannot be Normal / Flip / Special
        /// Summoned face-up while its summoner controls a face-up one; a duplicate that arrives anyway (flipped by an
        /// effect, control change) is destroyed, the one that arrived first stays
        /// (<see cref="SummonTriggersAndTributesRuntime.UniqueBlocksFaceUp"/>, <see cref="SummonTriggersAndTributesRuntime.SweepUniqueOnField"/>).
        /// </summary>
        YouCanOnlyControlOneFaceUpNamed,
        /// <summary>
        /// SummonTriggersAndTributes batch (v163) — Gravekeeper's Chief: "Your GY is unaffected by "<see cref="EffectClause.NamedCard"/>""
        /// (ygopro EFFECT_NECRO_VALLEY_IM, player target = its controller) while this card is face-up: that card's GY locks
        /// (banish, target, leave the GY) do not apply to this card's controller's GY
        /// (<see cref="SummonTriggersAndTributesRuntime.GyUnaffectedBy"/>).
        /// </summary>
        YourGyUnaffectedByNamedCard,
        /// <summary>
        /// LastTurn batch (v163) — Last Turn: the activating player selects 1 monster they control (no target); every other
        /// card on the field and in both hands is sent to the GY as one event; then the opponent selects 1 monster in their
        /// Deck, Special Summons it in face-up Attack Position, and it attacks the selected monster (no battle damage from
        /// that battle); at the End Phase of this turn the player whose monster(s) remain alone on the field wins the Duel,
        /// otherwise the Duel is a DRAW (<see cref="LastTurnRuntime.ResolveShowdown"/>). Resolution kind SpecialSummon (the
        /// Lua's CATEGORY_SPECIAL_SUMMON).
        /// </summary>
        SelectedMonsterShowdownLoneMonsterWins,
        /// <summary>
        /// SummonPromptsAndCosts batch (v163) — Vampire Baby: at the end of the Battle Phase, if this card destroyed a monster
        /// by battle and sent it to the GY this turn, its controller can Special Summon 1 to as many of those monsters as they
        /// have free Monster Zones (either GY, face-up, their choice of position) to their side of the field; once per turn
        /// (<see cref="SummonPromptsAndCostsRuntime.OnEndOfBattlePhase"/>; a human's choice holds the turn).
        /// </summary>
        SpecialSummonMonstersThisDestroyedByBattleThisTurn,
        /// <summary>
        /// SummonPromptsAndCosts batch (v163) — Tyrant Dragon: "This card cannot be Special Summoned from the GY, unless you
        /// Tribute <see cref="EffectClause.Amount"/> <see cref="EffectClause.TributeRaceFilter"/> monster" (ygopro
        /// EFFECT_SPSUMMON_COST, range GY). Not an effect that resolves: a Special Summon of this card from the GY needs its
        /// summoner to Tribute that monster as the Summon happens
        /// (<see cref="SummonPromptsAndCostsRuntime.GyTributeSummonCost"/>, registered in SpecialSummonLegality).
        /// </summary>
        GySpecialSummonNeedsTributeCost,
        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163) — Spirit Elimination: until the end of the turn, a monster the
        /// activator controls is banished instead of each monster that would be banished from their GY
        /// (<see cref="SpiritEliminationRuntime"/>, read by every banish-from-GY cost and effect).
        /// </summary>
        BanishFieldMonstersInsteadOfGyMonstersThisTurn,
        /// <summary>
        /// ForcedActivationAndSubstitution batch (v163) — Major Riot: return every monster on the field to the hand, then
        /// each player Special Summons from their hand as many Level <see cref="EffectClause.SummonLevelMax"/> or lower
        /// monsters that can be Normal Summoned/Set as went to their hand, in face-down Defense Position
        /// (<see cref="MajorRiotRuntime.Resolve"/>).
        /// </summary>
        ReturnAllMonstersThenEachSummonSameNumberFromHandFaceDown,
        /// <summary>
        /// PerCopyDeck batch (v163) — Parasite Paracide's FLIP: this card leaves the field and is put face-up in the
        /// opponent's Deck (it stays its owner's card), which is shuffled; that copy carries the card's
        /// <see cref="EffectTiming.DrawnFaceUpFromDeck"/> trigger (<see cref="PerCopyDeckRuntime.PutThisFaceUpInOpponentDeck"/>).
        /// </summary>
        PutThisFaceUpInOpponentDeck,
        /// <summary>
        /// PerCopyDeck batch (v163) — Parasite Paracide's draw trigger: the drawing player Special Summons this card from
        /// the hand to their field in face-up Defense Position; if it is Summoned, they take <see cref="EffectClause.Amount"/>
        /// damage, then every monster its controller controls is the <see cref="EffectClause.RaceFilter"/> Type while it
        /// stays face-up on the field (<see cref="PerCopyDeckRuntime.ResolveDrawnSummon"/>).
        /// </summary>
        SpecialSummonDrawnThisInDefenseDamageDrawerAndChangeRace,
        /// <summary>
        /// PerCopyDeck batch (v163) — Pharaoh's Treasure: this card is shuffled face-up into its controller's Deck instead
        /// of going to the GY (ygopro CancelToGrave + SendtoDeck + ReverseInDeck); that copy carries the card's
        /// <see cref="EffectTiming.DrawnFaceUpFromDeck"/> trigger (<see cref="PerCopyDeckRuntime.ShuffleThisFaceUpIntoYourDeck"/>).
        /// </summary>
        ShuffleThisFaceUpIntoYourDeck,
        /// <summary>
        /// PerCopyDeck batch (v163) — Pharaoh's Treasure's draw trigger: target 1 card in your GY other than this card;
        /// send this card from the hand to the GY, then, if it went there, add the target to the hand
        /// (<see cref="PerCopyDeckRuntime.ResolveDrawnRecovery"/>).
        /// </summary>
        SendDrawnThisToGyThenAddGyTargetToHand,
        /// <summary>
        /// PerCopyDeck batch (v163) — Graverobber: the target (a Spell in the opponent's GY, <see cref="EffectZoneFilter.OppGySpells"/>)
        /// is added to the activating player's hand, still owned by the opponent. Each time they activate it this turn
        /// and that activation resolves, they take <see cref="EffectClause.Amount"/> damage (once); at the End Phase, if
        /// they still hold or control it, it is sent to its owner's GY (<see cref="PerCopyDeckRuntime.ResolveGraverobber"/>).
        /// </summary>
        AddOpponentGySpellToHandUsableThisTurn,
        /// <summary>
        /// MfcSimpleSpells batch (v168) — Emblem of Dragon Destroyer: the controller destroys 1 card in hand,
        /// 1 face-up card on the field, and 1 card in their Deck (sequential, each optional).
        /// </summary>
        DestroyOneInHandOneOnFieldOneInDeck,
        /// <summary>
        /// MfcSimpleSpells batch (v168) — Dark Magician Girl: while face-up, this card gains Amount ATK and
        /// DefAmount DEF for each "Dark Magician" in either GY (ygopro EFFECT_UPDATE_ATTACK/DEFENSE, counted
        /// on each board refresh by StandbyGrowthAndMaintenanceRuntime.ApplyBoard).
        /// </summary>
        GainAtkDefPerNamedInBothGyWhileFaceUp,
        /// <summary>
        /// MfcSimpleSpells batch (v168) — Mass Driver: Tribute 1 monster (RequiresTributeCount=1); inflict
        /// 1000 damage to the opponent for each 1000 ATK the Tributed monster had on the field
        /// (Amount=1000, ScaleAmountByTributedAtk).
        /// </summary>
        InflictDamagePer1000TributedAtk,
        /// <summary>
        /// MfcLabCards3 batch (v168) — Blast Sphere: face-down Defense attacked; before damage calculation equip
        /// this card to the attacking monster (no target choice; the attacker is implicit).
        /// </summary>
        EquipThisToAttackingMonster,
        /// <summary>
        /// MfcLabCards3 batch (v168) — Blast Sphere: during opponent's next Standby Phase, destroy the monster
        /// this card is equipped to, and if you do, inflict damage to opponent equal to the destroyed monster's ATK.
        /// </summary>
        BlastSphereEquipThenStandbyDestroyInflict,
        /// <summary>
        /// MfcLabCards3 batch (v168) — Vampire Lord: if this card inflicts battle damage, opponent sends 1
        /// card of the declared type (Monster/Spell/Trap) from their Deck to the GY.
        /// </summary>
        MillCardOfDeclaredTypeFromOpponentDeck,
        /// <summary>
        /// MfcLabCards3 batch (v168) — Vampire Lord: once per turn, during your next Standby Phase after this
        /// card was destroyed by opponent's effect and sent to GY, Special Summon it from GY.
        /// </summary>
        GyRevivalAfterStandbyWhenDestroyedByOpponent,
        /// <summary>
        /// Riryoku: target 2 face-up monsters; halve the ATK of 1 monster, and if you do, add that lost ATK
        /// to the other monster until the end of the turn.
        /// Append-only: this member was first inserted mid-enum (v168 worktree) and renumbered every serialized
        /// program at/after its old index (Upstart Goblin, Tremendous Fire LP clauses among them). It now lives
        /// at the end so existing ints keep their meaning; v169 bumps the version to drop poisoned caches.
        /// </summary>
        HalfAtkAddToOtherUntilEndOfTurn,
        /// <summary>
        /// Inflict Amount effect damage to both players simultaneously (Mecha-Dog Marron).
        /// Append-only remediation: the v170 worktree inserted this member mid-enum (index 49, right after
        /// InflictDamageToOpponent) and renumbered every serialized program at/after it — the same hazard the
        /// member above documents. It now lives at the end so existing ints keep their meaning; the v173
        /// version bump drops the caches written under the old numbering.
        /// </summary>
        InflictDamageToBothPlayers,
        /// <summary>
        /// MfcLabCards3 batch (v168) — Dark Magician Girl: while face-up, this card gains Amount ATK and
        /// DefAmount DEF for each "Dark Magician" or "Magician of Black Chaos" in either GY. Supports
        /// NamedCard and AltNamedCard (both checked in both GYs).
        /// Append-only remediation: the v168 worktree inserted this member mid-enum (index 382, ahead of
        /// InflictDamagePer1000TributedAtk) and renumbered everything after it. It now lives at the end so
        /// existing ints keep their meaning; the v173 version bump drops the caches written under the old numbering.
        /// </summary>
        GainAtkDefPerNamedCardInBothGyWhileFaceUp,
        /// <summary>
        /// SimpleTargetUtility batch (v173) — Altar for Tribute: the activation cost sent a monster
        /// (<c>RequiresSendOtherYouControl</c>); at resolution the controller gains LP equal to the original
        /// (printed) ATK of that cost monster, carried in <c>costNumeric</c>.
        /// </summary>
        GainLpEqualToCostAmount,
        /// <summary>
        /// SimpleTargetUtility batch (v173) — Cost Down: stamp the controller's hand monsters and the
        /// duelist-wide hand stamp for the rest of this turn (Applied each refresh; see
        /// <c>SimpleTargetUtilityRuntime.ApplyLevelStamps</c>).
        /// </summary>
        ReduceHandLevelsUntilEndOfTurn,
        /// <summary>
        /// ChoiceBullets batch (v173) — Dark Scorpion - Chick the Yellow: look at the top card of the opponent's Deck,
        /// then return it to the top or bottom of the Deck (immediate, not deferred to the next Draw Phase).
        /// </summary>
        LookAtOpponentTopCardThenTopOrBottom,
        /// <summary>
        /// DiscardTriggersSelfSs batch (v173) — Fear from the Dark / Despair from the Dark: when this card is sent
        /// from the hand or Deck to the GY by an opponent's card effect, Special Summon it from the GY in face-up
        /// Attack Position. Distinct from <see cref="SpecialSummonThisFromHand"/> (hand-only, immediate) and
        /// <see cref="SpecialSummonFromGy"/> (a targeted GY card): this is the source card itself, resolved inline
        /// from the trigger, with the Semi-Nomi / Nomi / GY-lock gates applied.
        /// </summary>
        SpecialSummonThisFromGy,
        /// <summary>
        /// MfcSimpleSpellsB batch (v174) — Emblem of Dragon Destroyer: add 1 "Buster Blader" from the
        /// controller's Deck or GY to the hand. Deck first, then GY (ygopro s.target LOCATION_DECK +
        /// LOCATION_GRAVE, resolved in that order). The named card is <see cref="EffectClause.NamedCard"/>.
        /// </summary>
        AddNamedFromDeckOrGyToHand,
        /// <summary>
        /// MfcSimpleSpellsB batch (v174) — Hidden Spellbook: target 2 Spell Cards in the controller's
        /// GY, shuffle those targets into the Deck. Each is removed from the GY and appended to the
        /// Deck; the Deck is shuffled once at the end (ygopro s.target LOCATION_GRAVE + s.target
        /// LOCATION_DECK + s.shuffle).
        /// </summary>
        ShuffleGySpellsIntoDeck,
        /// <summary>
        /// MfcSimpleSpellsB batch (v174) — Thunder of Ruler: activate during the opponent's Standby
        /// Phase; there is no Battle Phase this turn. RESERVED, no card compiles to it yet: Thunder
        /// stays fail-closed because the engine has no activation path for a Set Normal Trap on a
        /// pure StandbyPhase timing (FirePhaseTriggersFor skips face-down cards; CanActivateOfficial
        /// refuses programs with no Activate timing). Queued until a Standby priority window for the
        /// non-turn player exists. The ban is read by
        /// <see cref="TributeSummonsRuntime.BattlePhaseForbidden"/> (it stamps
        /// <see cref="DuelistState.BattlePhaseForbiddenTurn"/>), so it is a property of the turn, not
        /// of the card, and survives this card leaving the field.
        /// </summary>
        ForbidBattlePhaseThisTurn,
        /// <summary>
        /// DamageStepRules batch — Ultimate Obedient Fiend (<see cref="EffectTiming.ContinuousWhileFaceUp"/>): this card
        /// cannot attack while its controller has more than 1 card on the field or any card in hand (ygopro c32240937
        /// EFFECT_CANNOT_ATTACK, condition GetFieldGroupCount(LOCATION_ONFIELD) &gt; 1 or LOCATION_HAND &gt; 0). Read by
        /// <see cref="DamageStepRulesRuntime.AttackForbidden"/> (DuelEngine.ContinuousCannotAttackBlocks).
        /// </summary>
        CannotAttackUnlessOnlyCardOnFieldAndEmptyHand,
        /// <summary>
        /// DamageStepRules batch — Meteorain (<see cref="EffectTiming.Activate"/>, your turn only): for the rest of this
        /// turn every monster its controller controls inflicts piercing battle damage (ygopro c64274292: a player
        /// EFFECT_PIERCE, LOCATION_MZONE,0, RESET_PHASE|PHASE_END). Stamped by
        /// <see cref="DamageStepRulesRuntime.GrantTurnPiercing"/>, read by <see cref="DamageStepRulesRuntime.TurnGrantsPiercing"/>.
        /// </summary>
        YourMonstersGainPiercingThisTurn,
        /// <summary>
        /// DamageStepRules batch — Kishido Spirit (<see cref="EffectTiming.ContinuousWhileFaceUp"/>, a Continuous Spell):
        /// monsters its controller controls cannot be destroyed by battle with a monster whose ATK equals theirs
        /// (ygopro c60519422 EFFECT_INDESTRUCTABLE_BATTLE, value: the battling monster's ATK equals this monster's).
        /// Read at damage calculation by <see cref="DamageStepRulesRuntime.BattleIndestructible"/>.
        /// </summary>
        CannotBeDestroyedByBattleWithEqualAtkMonster,
        /// <summary>
        /// DamageStepRules batch — Cat's Ear Tribe (<see cref="EffectTiming.ContinuousWhileFaceUp"/>): during the
        /// opponent's turn, while this card is the attack target, the original ATK of the opponent's monster it battles
        /// becomes <see cref="EffectClause.Amount"/> during the Damage Step (ygopro c95841282 EFFECT_SET_BASE_ATTACK,
        /// PHASE_DAMAGE / PHASE_DAMAGE_CAL). Applied and restored by <see cref="DamageStepRulesRuntime"/>
        /// (CardInstance.OriginalAtkOverride, so the attacker's other modifiers still apply on top).
        /// </summary>
        OpponentBattlerOriginalAtkBecomesAmountDuringDamageStep,
        /// <summary>
        /// DamageStepRules batch — Amazoness Swords Woman (<see cref="EffectTiming.ContinuousWhileFaceUp"/>): the battle
        /// damage its controller would take from battles involving this card is taken by the opponent instead (ygopro
        /// c94004268 EFFECT_REFLECT_BATTLE_DAMAGE). Applied at damage calculation by
        /// <see cref="DamageStepRulesRuntime.ReflectBattleDamage"/>.
        /// </summary>
        ReflectControllerBattleDamageFromBattlesInvolvingThis,
        /// <summary>
        /// EquipLocksAndLeaveField batch — Raregold Armor (c7625614, EFFECT_CANNOT_SELECT_BATTLE_TARGET, target range
        /// (0, LOCATION_MZONE)): while this Equip Card's controller controls the equipped monster, the opponent's monsters
        /// cannot select any other monster as an attack target. Direct attacks are not restricted. Continuous, read by
        /// <see cref="ContinuousProtections.CannotBeAttackTarget"/> through
        /// <see cref="EquipLocksAndLeaveFieldRuntime.OnlyEquippedHostMayBeAttacked"/>.
        /// </summary>
        OpponentCannotAttackOtherThanEquippedHost,
        /// <summary>
        /// EquipLocksAndLeaveField batch — Metalsilver Armor (c33114323, EFFECT_CANNOT_BE_EFFECT_TARGET with aux.tgoval,
        /// ranges Monster Zone + GY + banished on both sides): while this Equip Card's controller controls the equipped
        /// monster, every other monster (face-down ones in a Monster Zone too; face-up only when banished) cannot be
        /// targeted by the opponent's card effects. Continuous, read by
        /// <see cref="EquipLocksAndLeaveFieldRuntime.ShieldedFromTargeting"/> when targets are chosen (both target
        /// mirrors and the other target lists).
        /// </summary>
        OpponentCannotTargetMonstersExceptEquippedHost,
        /// <summary>
        /// DcrEquipSpells batch — Shooting Star Bow - Ceal (ContinuousWhileFaceUp marker on an Equip Spell, ygopro
        /// EFFECT_TYPE_EQUIP + EFFECT_DIRECT_ATTACK): the equipped monster can attack directly. The grant is the Equip
        /// Card's effect, so the monster's own negation does not remove it; a negated or face-down Equip Card gives
        /// nothing. Read by DuelEngine.GrantsDirectAttack through <see cref="DcrEquipSpellsRuntime.GrantsDirectAttack"/>.
        /// </summary>
        EquippedCanAttackDirectly,
        /// <summary>
        /// DcrEquipSpells batch — Twin Swords of Flashing Light - Tryce (ContinuousWhileFaceUp marker, ygopro
        /// EFFECT_TYPE_EQUIP + EFFECT_EXTRA_ATTACK, value <see cref="EffectClause.Amount"/>): the equipped monster can make
        /// that many more attacks during each Battle Phase. Read by DuelEngine.ExtraAttacksAllowed through
        /// <see cref="DcrEquipSpellsRuntime.ExtraAttacks"/> (the Equip Card's effect: kept while the monster is negated).
        /// </summary>
        EquippedGainsExtraAttacks,
        /// <summary>
        /// DcrEquipSpells batch — Rod of the Mind's Eye (ContinuousWhileFaceUp marker, ygopro EFFECT_TYPE_EQUIP +
        /// EFFECT_CHANGE_BATTLE_DAMAGE, aux.ChangeBattleDamage(1, N)): battle damage the equipped monster inflicts to the
        /// opponent of this Equip Card's controller becomes <see cref="EffectClause.Amount"/>. Damage to the Equip's own
        /// controller, and no damage at all, stay as they are. Read at the battle-damage site
        /// (<see cref="DcrEquipSpellsRuntime.ChangeBattleDamage"/>).
        /// </summary>
        EquippedBattleDamageToOpponentBecomes,
        /// <summary>
        /// DcrEquipSpells batch — Butterfly Dagger - Elma (<see cref="EffectTiming.SentFromFieldToGy"/>, with
        /// <see cref="EffectClause.RequiresDestroyedByEffectWhileEquipped"/> and <see cref="EffectClause.IsOptional"/>):
        /// "You can return this card to the hand" — this card, still in the GY, is added to its owner's hand
        /// (<see cref="DcrEquipSpellsRuntime.FireReturnThisToHand"/>).
        /// </summary>
        ReturnThisFromGyToHand,
        /// <summary>
        /// SpellCounterSpenders batch — Magical Plant Mandragola (FLIP, ygopro c7802006): place
        /// <see cref="EffectClause.Amount"/> Spell Counter on each face-up card on the field that can hold one
        /// (Lua IsCanAddCounter; <see cref="SpellCounterSpendersRuntime.CanAddSpellCounter"/>). No target.
        /// </summary>
        PlaceSpellCounterOnEachFaceUpHolder,
        /// <summary>
        /// SpellCounterSpenders batch — Apprentice Magician (ygopro c9156135): place <see cref="EffectClause.Amount"/>
        /// Spell Counter on the chosen target (zone <see cref="EffectZoneFilter.FieldFaceUpSpellCounterHolders"/>), if it
        /// is still face-up on the field and can hold it.
        /// </summary>
        PlaceSpellCounterOnTarget,
        /// <summary>
        /// SpellCounterSpenders batch — Apprentice Magician (ygopro c9156135 e4, TRIGGER_O on EVENT_BATTLE_DESTROYED):
        /// "You can Special Summon 1 Level N or lower X monster from your Deck in face-down Defense Position"
        /// (<see cref="EffectClause.Amount"/> = the Level cap, <see cref="EffectClause.RaceFilter"/> = the Type). The
        /// controller picks the monster (Cancel declines); the AI takes the best one. The opponent is shown it.
        /// </summary>
        SpecialSummonLevelRaceFromDeckFaceDownDefense,
        /// <summary>
        /// SpellCounterSpenders batch — Armor Exe (ygopro c7180418 e4, FIELD+CONTINUOUS on each Standby Phase,
        /// EFFECT_FLAG_CANNOT_DISABLE): remove 1 Spell Counter from a card on its controller's side of the field, or
        /// destroy this card (the controller may decline). A maintenance cost, not a Chain Link; it applies while negated.
        /// </summary>
        RemoveSpellCounterOnYourSideOrDestroyThis,
        /// <summary>
        /// SpellCounterSpenders batch — Pitch-Black Power Stone (ygopro c34029630 e3, QUICK_O from the Spell &amp; Trap
        /// Zone, once per turn, only during its controller's turn): remove 1 Spell Counter from this card and place 1
        /// on the target (another face-up card that can hold one). <see cref="FaceUpQuickEffectsRuntime"/>.
        /// </summary>
        MoveSpellCounterFromThisToTarget,
        /// <summary>
        /// SpellCounterSpenders batch — Pitch-Black Power Stone (ygopro c34029630 e4, EFFECT_SELF_DESTROY while it has
        /// no Spell Counter): "When the last Spell Counter(s) is removed from this card, destroy it." ContinuousWhileFaceUp,
        /// no Chain Link; checked on every board refresh by <see cref="SpellCounterSpendersRuntime.SweepSelfDestroy"/>.
        /// </summary>
        DestroyThisWhenLastSpellCounterRemoved,
        /// <summary>
        /// ChainLinkAnswers batch — Jar Robber (<see cref="EffectTiming.ChainLinkActivated"/>, ygopro c33784505
        /// EVENT_CHAINING): answers only the activation of the card named <see cref="EffectClause.NamedCard"/> (either
        /// player's). Its link negates the EFFECT of that activation (Duel.NegateEffect, not NegateActivation: the card
        /// still resolves, with no effect, and goes to the GY) and, if it did, its controller draws
        /// <see cref="EffectClause.Amount"/> card(s). Runtime: <see cref="ChainLinkAnswersRuntime"/>.
        /// </summary>
        NegateEffectOfNamedActivationThenDraw,
        /// <summary>
        /// ChainLinkAnswers batch — Chaos Command Magician (<see cref="EffectTiming.ContinuousWhileFaceUp"/> marker, ygopro
        /// c72630549 EVENT_CHAIN_SOLVING): while this card is face-up on the field, a monster effect whose only card target
        /// is this card is negated as it resolves (no destroy). Read at chain resolution and when an untargeted-chain
        /// monster effect applies its clause (<see cref="ChainLinkAnswersRuntime.NegateResolvingLinkTargetingOnlyThis"/>,
        /// <see cref="ChainLinkAnswersRuntime.NegatesTargetingMonsterClause"/>).
        /// </summary>
        NegateMonsterEffectsThatTargetOnlyThis,
        /// <summary>
        /// ChainLinkAnswers batch — Dramatic Rescue (<see cref="EffectTiming.ChainLinkActivated"/>, ygopro c80193355
        /// EVENT_BECOME_TARGET): answers a Spell/Trap Card activation or a monster effect that targets a face-up monster
        /// of the series <see cref="EffectClause.NamedCard"/> on the field (either player's). Resolution: that monster
        /// returns to its owner's hand, then its controller Special Summons 1 other monster from their hand.
        /// </summary>
        ReturnTargetedSeriesMonsterThenSpecialSummonFromHand,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Dark Cat with White Tail: FLIP, target <see cref="EffectClause.OpponentTargetCount"/>
        /// monsters the opponent controls and <see cref="EffectClause.ControllerTargetCount"/> you control (chosen as the
        /// effect activates; without both groups no targets and no effect); return them to their owners' hands.
        /// </summary>
        ReturnOpponentAndOwnTargetsToOwnersHands,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Huge Revolution: send every card in the opponent's hand to the GY and destroy
        /// every card they control.
        /// </summary>
        SendOpponentHandToGyAndDestroyAllOpponentCards,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Magical Merchant: excavate from the top of your Deck until a Spell/Trap, add it
        /// to the hand, send the cards above it to the GY (none: reveal the Deck and shuffle it).
        /// </summary>
        ExcavateUntilSpellTrapAddItSendRestToGy,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Pixie Knight: the controller's OPPONENT targets 1 Spell in the controller's GY;
        /// it goes on top of the controller's Deck.
        /// </summary>
        OpponentPlacesSpellFromYourGyOnTopOfYourDeck,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Pineapple Blast: destroy the opponent's monsters so they control as many as you;
        /// the opponent chooses which of their monsters are destroyed.
        /// </summary>
        DestroyOpponentMonstersToMatchYourCountOpponentChooses,
        /// <summary>
        /// FlipMassAndOpponentPicks batch — Dimension Jar: the turn player, then the other player, can each banish up to
        /// <see cref="EffectClause.Amount"/> monsters from their opponent's GY.
        /// </summary>
        EachPlayerMayBanishUpToMonstersFromOpponentGy,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Amazoness Blowpiper: a mandatory trigger in each of its controller's own
        /// Standby Phases (once): target 1 face-up monster the opponent controls; it loses Amount ATK until the end of
        /// this turn (PhaseTriggersAndIgnitionCostsRuntime.ResolveStandbyAtkLoss; the pick holds the Standby Phase).
        /// </summary>
        TargetOppFaceUpLosesAtkAtYourStandby,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Helping Robo for Combat: draw Amount card(s); then (if it drew) its
        /// controller places 1 card from the hand on the bottom of the Deck (PhaseTriggersAndIgnitionCostsRuntime.DrawThenBottom).
        /// </summary>
        DrawThenPlaceHandCardOnDeckBottom,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Senri Eye: an optional trigger in each of its controller's own Standby
        /// Phases (once): pay Amount LP (cost) to look at the top card of the opponent's Deck; it stays where it is.
        /// </summary>
        PayLpLookAtOpponentTopDeckCardAtYourStandby,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Magical Scientist: Special Summon 1 Fusion Monster of Level Amount or
        /// lower from the Extra Deck in face-up Attack or Defense Position (not a Fusion Summon); it cannot attack
        /// directly, and is returned to the Extra Deck in this turn's End Phase.
        /// </summary>
        SpecialSummonFusionFromExtraNoDirectAttackReturnAtEndPhase,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Roulette Barrel: roll a six-sided die twice, choose 1 of the results
        /// (no choice when both are equal), then destroy 1 face-up monster on the field whose Level equals it (not a target).
        /// </summary>
        RollTwoDiceChooseOneDestroyFaceUpMonsterWithThatLevel,
        /// <summary>
        /// PhaseTriggersAndIgnitionCosts batch — Physical Double: Special Summon 1 Token (<see cref="EffectClause.TokenName"/>)
        /// whose Level, Type, Attribute, ATK and DEF are the target's current ones; it is destroyed in this turn's End Phase.
        /// </summary>
        SpecialSummonTokenCopyingTargetDestroyAtEndPhase,
        /// <summary>
        /// UnionRiders batch (v175) — Kiryu (c84814897 e3), on <see cref="EffectTiming.UnionStateIgnition"/>: cost, Tribute
        /// this card (it is in the Spell &amp; Trap Zone); the monster it was equipped to (remembered before the cost, Lua
        /// SetTargetCard without CARD_TARGET) can attack directly this turn if it is still a face-up monster on the field.
        /// </summary>
        TributeThisSoEquippedMonsterAttacksDirectly,
        /// <summary>
        /// UnionRiders batch (v175) — Second Goblin (c19086954 e1), on <see cref="EffectTiming.UnionStateIgnition"/>, once
        /// per turn: change the equipped monster between face-up Attack and face-up Defense Position.
        /// </summary>
        ChangeEquippedMonsterBattlePosition,
        /// <summary>
        /// UnionRiders batch (v175) — Metallizing Parasite - Lunatite (c7369217 e1, EFFECT_IMMUNE_EFFECT with
        /// aux.IsUnionState): while this Union is in its Union state, the equipped monster is unaffected by the effects of
        /// Spell Cards controlled by this card's controller's opponent (<see cref="UnionRidersRuntime.ShieldsFromOpponentSpell"/>).
        /// </summary>
        EquippedUnaffectedByOpponentSpellEffects,
        /// <summary>
        /// UnionRiders batch (v175) — Union Rider (c11743119): target 1 face-up Union Monster the opponent controls; take
        /// control of it and equip it to this card. The equipped Union is not in its Union state (it cannot use its own
        /// unequip, its Union text does not apply) and goes to its owner's GY when it leaves the field.
        /// </summary>
        TakeControlOfOpponentUnionAndEquipToThis,
        /// <summary>
        /// UnionRiders batch (v175) — Combination Attack (c8964854): the target (a face-up monster that declared an attack
        /// this turn, equipped with a Union in its Union state) — each such Union is Special Summoned to its controller's
        /// field in face-up Attack or Defense Position (the activating player chooses); if any was, the target may attack
        /// again this turn (EFFECT_EXTRA_ATTACK = its attacks declared this turn).
        /// </summary>
        SpecialSummonTargetsUnionsThenItAttacksAgain,
        /// <summary>
        /// SummonLocksAndStatRules batch — Amazoness Spellcaster: the first target (Zone) and the second target
        /// (SecondZone) each get the other's original ATK until the End Phase (ygopro EFFECT_SET_BASE_ATTACK,
        /// RESETS_STANDARD_PHASE_END), only if both are still face-up targets at resolution
        /// (<see cref="SummonLocksAndStatRulesRuntime.ApplySwitchStep"/>).
        /// </summary>
        SwitchOriginalAtkOfTwoTargetsUntilEndOfTurn,
        /// <summary>
        /// SummonLocksAndStatRules batch — Rivalry of Warlords: while face-up, each player keeps the face-up monsters of
        /// 1 Type (their choice) and sends the others to the GY (ygopro EVENT_ADJUST), and cannot Normal Summon, Flip
        /// Summon or face-up Special Summon a monster of another Type (<see cref="SummonLocksAndStatRulesRuntime"/>).
        /// </summary>
        EachPlayerKeepsOneMonsterType,
        /// <summary>
        /// SimpleTriggersAndSwarm batch — Outstanding Dog Marron (c11548522): this card, while it is still in its owner's
        /// GY, is added to its owner's Deck, which is then shuffled (ygopro SendtoDeck(c, nil, SEQ_DECKSHUFFLE, REASON_EFFECT)
        /// after IsRelateToEffect). Necrovalley stops it (<see cref="ContinuousProtections.GyMovementLocked"/>).
        /// <see cref="SimpleTriggersAndSwarmRuntime.ShuffleThisFromGyIntoDeck"/>.
        /// </summary>
        ShuffleThisFromGyIntoDeck,
        /// <summary>
        /// SimpleTriggersAndSwarm batch — "Special Summon any number of X from your hand" (Don Turtle c3493978:
        /// <see cref="EffectClause.NamedCard"/> = the name; Mustering of the Dark Scorpions c68191243:
        /// <see cref="EffectClause.NamedCardIsSeries"/>, with <see cref="EffectClause.SummonOnlyOneOfEachName"/>, Lua
        /// aux.dncheck). The player picks 1 to (free Monster Zones) of them one at a time (CANCEL after the first = done)
        /// and they are Special Summoned together, face-up in the Attack or Defense Position the player chose (Lua
        /// SelectMatchingCard / SelectSubGroup 1..ft, POS_FACEUP). <see cref="SimpleTriggersAndSwarmRuntime.StartAnyNumberSummon"/>.
        /// </summary>
        SpecialSummonAnyNumberFromHand,
        /// <summary>
        /// SimpleTriggersAndSwarm batch — Dark Scorpion Combination (c20858318): the monsters its activator controls whose
        /// names are in <see cref="EffectClause.ConditionName"/> are flagged until the End Phase; a flagged monster can
        /// attack directly (field EFFECT_DIRECT_ATTACK) and the battle damage it inflicts to the activator's opponent by a
        /// direct attack becomes <see cref="EffectClause.Amount"/> (field EFFECT_CHANGE_BATTLE_DAMAGE, s.rdcon). The flag
        /// ends when the monster leaves the field or is turned face-down (RESETS_STANDARD).
        /// <see cref="SimpleTriggersAndSwarmRuntime.ApplyCombination"/>.
        /// </summary>
        NamedMonstersAttackDirectlyBattleDamageBecomesThisTurn,
        /// <summary>
        /// DcrSpellTrapUtility batch — Interdimensional Matter Transporter (c36261276): the target (a face-up monster the
        /// activating player still controls) is banished (REASON_TEMPORARY) and returns to the field in the position it
        /// left during this turn's End Phase (Lua EVENT_PHASE+PHASE_END, Duel.ReturnToField;
        /// <see cref="DcrSpellTrapUtilityRuntime.BanishUntilEndPhase"/>). Not a Summon.
        /// </summary>
        BanishTargetUntilEndPhase,
        /// <summary>
        /// DcrSpellTrapUtility batch — Different Dimension Gate (c56460688): each target (Zone, then SecondZone) is
        /// banished (REASON_TEMPORARY) and linked to this card while it stays face-up on the field. When this card is
        /// destroyed and sent to the GY, every linked monster still banished returns to the field in the position it left
        /// (Lua SINGLE+CONTINUOUS EVENT_TO_GRAVE with REASON_DESTROY); if this card leaves any other way they stay banished
        /// (<see cref="DcrSpellTrapUtilityRuntime.BanishLinkedToGate"/>).
        /// </summary>
        BanishTargetsReturnWhenThisDestroyedToGy,
        /// <summary>
        /// DcrSpellTrapUtility batch — Final Countdown (c95308449): a player effect that outlives the card. The activation
        /// turn counts as turn 1; at the end of the <see cref="EffectClause.Amount"/>-th turn (Lua EVENT_TURN_END count)
        /// the activating player wins. Both players' countdowns ending together is a DRAW (Duel.Win PLAYER_NONE)
        /// (<see cref="DcrSpellTrapUtilityRuntime.StartCountdown"/>).
        /// </summary>
        WinAfterTurnCountdown,
        /// <summary>
        /// DcrSpellTrapUtility batch — Dragged Down into the Grave (c16435215): with both hands non-empty at resolution,
        /// each player looks at the other's hand, the activating player picks 1 card of the opponent's hand, the opponent
        /// picks 1 card of the activating player's hand, both are discarded (REASON_EFFECT+REASON_DISCARD), then each
        /// player draws 1 (<see cref="DcrSpellTrapUtilityRuntime.ResolveDraggedDown"/>). Either hand empty: nothing.
        /// </summary>
        BothPickFromOpponentHandDiscardThenBothDraw,
        /// <summary>
        /// BattleDestroyedTriggers batch — Agido (c16135253): its controller rolls a six-sided die (CoinDiceInterrupts) if
        /// they have a free Monster Zone, then may Special Summon 1 <see cref="EffectClause.RaceFilter"/> monster from their
        /// GY whose Level equals the roll (Level 6 or higher on a 6), in face-up Attack or Defense Position. A select at
        /// resolution, not a target (<see cref="BattleDestroyedTriggersRuntime.RollDieThenSummonFromGy"/>).
        /// </summary>
        RollDieSpecialSummonFromGyWithLevelOfRoll,
        /// <summary>
        /// BattleDestroyedTriggers batch — Keldo (c80441106): targets exactly <see cref="EffectClause.TargetCount"/> cards in
        /// the opponent's GY, only if that many can be targeted (otherwise nothing), and shuffles those still there into
        /// their owner's Deck (<see cref="BattleDestroyedTriggersRuntime.ShuffleOpponentGyTargetsIntoDeck"/>).
        /// </summary>
        ShuffleTargetsFromOpponentGyIntoDeck,
        /// <summary>
        /// BattleDestroyedTriggers batch — Different Dimension Dragon (c50939127): this card cannot be destroyed by the
        /// effect of a Spell/Trap Card that does not target it (ygopro EFFECT_INDESTRUCTABLE_EFFECT, value: not
        /// EFFECT_FLAG_CARD_TARGET and a Spell/Trap effect; <see cref="BattleDestroyedTriggersRuntime.PreventsUntargetedSpellTrapDestruction"/>).
        /// </summary>
        CannotBeDestroyedBySpellTrapEffectsThatDoNotTargetThis,
        /// <summary>
        /// BattleDestroyedTriggers batch — Different Dimension Dragon (c50939127): this card cannot be destroyed by battle
        /// with a monster whose ATK (this damage calculation's) is <see cref="EffectClause.Amount"/> or less (ygopro
        /// EFFECT_INDESTRUCTABLE_BATTLE, IsAttackBelow; <see cref="BattleDestroyedTriggersRuntime.BattleIndestructible"/>).
        /// </summary>
        CannotBeDestroyedByBattleWithMonsterAtkAtMost,
        /// <summary>
        /// GuardiansAndEquipEvents batch — Guardian Elma / Chopman the Desperate Outlaw: the summon trigger's target, an Equip
        /// Spell in the controller's GY that can equip to this card (Lua CheckEquipTarget), is equipped to this card
        /// (Duel.Equip from the GY: <see cref="GuardiansAndEquipEventsRuntime.ApplyEquipFromGy"/>).
        /// </summary>
        EquipTargetedGyEquipSpellToThis,
        /// <summary>
        /// GuardiansAndEquipEvents batch — Morale Boost: while face-up, each time an Equip Spell Card is equipped (EVENT_EQUIP),
        /// the controller of that Equip Spell gains <see cref="EffectClause.Amount"/> LP (FIELD TRIGGER_F; a marker read by
        /// <see cref="GuardiansAndEquipEventsRuntime.NoteEquipped"/>).
        /// </summary>
        GainLpWhenEquipSpellEquipped,
        /// <summary>
        /// GuardiansAndEquipEvents batch — Morale Boost: while face-up, each time Equip Spell Cards that were equipped leave the
        /// field (EVENT_LEAVE_FIELD, GetEquipTarget or REASON_LOST_TARGET), each controller of one of them takes
        /// <see cref="EffectClause.Amount"/> damage, once per event (<see cref="GuardiansAndEquipEventsRuntime.NoteEquipLeftField"/>).
        /// </summary>
        DamageWhenEquipSpellLeavesField,
        /// <summary>
        /// DerivedAtkMonsters batch (v176) — Great Maju Garzett / Maju Garzett (ContinuousWhileFaceUp marker): when this
        /// card is Tribute Summoned (not Set), its ATK becomes Amount × the printed ATK ("?" = 0) of its Tributes
        /// (<see cref="EffectClause.CountSource"/> <see cref="DerivedAtkMonstersRuntime.FirstTribute"/> or
        /// <see cref="DerivedAtkMonstersRuntime.AllTributes"/>), for as long as it stays face-up on the field and its
        /// effects are not negated (ygopro EFFECT_MATERIAL_CHECK + EFFECT_SUMMON_COST, SET_ATTACK with RESET_DISABLE).
        /// Set by <see cref="DerivedAtkMonstersRuntime.NoteTributeSummon"/>, applied on each board refresh.
        /// </summary>
        ThisAtkBecomesTributedOriginalAtk,
        /// <summary>
        /// DerivedAtkMonsters batch (v176) — Guardian Tryce (SentFromFieldToGy, RequiresDestroyed, mandatory): Special
        /// Summon the monster(s) Tributed for this card's Tribute Summon that are still in the GY they were sent to by
        /// it, only when there are enough free Monster Zones for all of them
        /// (<see cref="DerivedAtkMonstersRuntime.SpecialSummonTributeMaterials"/>).
        /// </summary>
        SpecialSummonTributeMaterialsFromGy,
        /// <summary>
        /// NamedSummonPairs batch — Exodia Necross (c12600382 e2): SINGLE EFFECT_INDESTRUCTABLE_EFFECT with value
        /// re:IsSpellTrapEffect() while face-up in the Monster Zone; read by DuelEngine.DestroyMonster through
        /// <see cref="NamedSummonPairsRuntime.PreventsSpellTrapEffectDestruction"/>.
        /// </summary>
        CannotBeDestroyedBySpellTrapEffects,
        /// <summary>
        /// NamedSummonPairs batch — Exodia Necross (c12600382 e5): SINGLE EFFECT_SELF_DESTROY while any of the
        /// <see cref="EffectClause.NamedCard"/> names ('|'-separated) is missing from its controller's GY
        /// (<see cref="NamedSummonPairsRuntime.SweepSelfDestroy"/> on each board refresh; not a Chain Link).
        /// </summary>
        SelfDestroyUnlessAllNamedInYourGy,
        /// <summary>
        /// NamedSummonPairs batch — Mirage Knight (c49217579 e1): EFFECT_UPDATE_ATTACK during PHASE_DAMAGE_CAL only, equal
        /// to the battle target's original ATK (GetBaseAttack); applied by
        /// <see cref="NamedSummonPairsRuntime.ApplyDamageCalculationAtk"/>.
        /// </summary>
        GainBattleTargetOriginalAtkDuringDamageCalculation,
        /// <summary>
        /// NamedSummonPairs batch — Mirage Knight (c49217579 e2): FIELD TRIGGER_F at either player's End Phase, banish this
        /// card face-up if it battled this turn (GetBattledGroupCount() &gt; 0; recorded by
        /// <see cref="NamedSummonPairsRuntime.NoteBattled"/>).
        /// </summary>
        BanishThisAtEndPhaseIfBattledThisTurn,
        /// <summary>
        /// ArchfiendRiders batch — Terrorking Archfiend (c35975813 e1/e2: SINGLE EFFECT_CANNOT_SUMMON +
        /// EFFECT_CANNOT_FLIP_SUMMON unless its controller controls a face-up <see cref="EffectClause.NamedCard"/> series
        /// monster). Only the Normal (Tribute) Summon and the Flip Summon are forbidden; a Set stays legal. Read by
        /// <see cref="ArchfiendRidersRuntime.NormalOrFlipSummonForbidden"/>; never resolved as an effect.
        /// </summary>
        CannotBeNormalOrFlipSummonedUnlessControllerHasFaceUpSeries,
        /// <summary>
        /// ArchfiendRiders batch — Pandemonium (c94585852 e2: FIELD+CONTINUOUS EFFECT_LPCOST_REPLACE in the Field Zone):
        /// neither player pays the Life Points an <see cref="EffectClause.NamedCard"/> series monster's effect costs during
        /// the Standby Phase (<see cref="ArchfiendRidersRuntime.StandbyLpCostWaived"/>); never resolved as an effect.
        /// </summary>
        WaiveSeriesMonsterStandbyLpCosts,
        /// <summary>
        /// ArchfiendRiders batch — Pandemonium (c94585852 e4: FIELD TRIGGER_O, EFFECT_FLAG_EVENT_PLAYER + DAMAGE_STEP): each
        /// time a player's <see cref="EffectClause.NamedCard"/> series monster(s) are destroyed and sent to the GY, except by
        /// battle, that player may add 1 series monster with a lower Level than the highest destroyed one from their Deck
        /// to their hand (<see cref="ArchfiendRidersRuntime.NoteSentToGy"/>); a marker, never resolved as a clause.
        /// </summary>
        SearchLowerLevelSeriesWhenSeriesDestroyedExceptByBattle,
        /// <summary>
        /// ArchfiendRiders batch — Pandemonium Watchbear (c75375465: FIELD EFFECT_INDESTRUCTABLE_EFFECT on face-up
        /// <see cref="EffectClause.NamedCard"/> in its controller's Spell &amp; Trap / Field Zone, value: the opponent's
        /// effects). Read by <see cref="ArchfiendRidersRuntime.ProtectsSpellTrapFromEffectDestruction"/>.
        /// </summary>
        NamedSpellTrapCannotBeDestroyedByOpponentEffects,
        /// <summary>
        /// ArchfiendRiders batch — Desrook Archfiend (c72192100 e3, timing
        /// <see cref="EffectTiming.HandQuickEffectWhenNamedDestroyed"/>): cost — send this card from the hand to the GY
        /// (not a discard); target the destroyed <see cref="EffectClause.NamedCard"/> in the GY; Special Summon it
        /// face-up if it is still there (<see cref="ArchfiendRidersRuntime.TryResolveLink"/>).
        /// </summary>
        SpecialSummonDestroyedNamedFromGyBySendingThisFromHand,
        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Cursed Seal of the Forbidden Spell (c58851034), an
        /// <see cref="EffectClause.AfterNegationAction"/>: once the answered Spell Card was negated and destroyed, the
        /// answering card's controller's opponent cannot activate Spell Cards with that card's code for the rest of the Duel
        /// (ygopro EFFECT_CANNOT_ACTIVATE, IsHasType(EFFECT_TYPE_ACTIVATE) and IsCode(label);
        /// <see cref="SpellTrapAnswersAndActivationLocksRuntime"/>).
        /// </summary>
        LockOpponentActivatingNegatedSpellNameForDuel,
        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Spell Vanishing (c29735721), an
        /// <see cref="EffectClause.AfterNegationAction"/>: look at the opponent's hand and Deck and send every card with the
        /// answered Spell's code to the GY (ygopro ConfirmCards, SendtoGrave REASON_EFFECT), then shuffle that Deck.
        /// </summary>
        SendNegatedCardNameFromOpponentHandAndDeckToGy,
        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Judgment of Anubis (c55256016), an
        /// <see cref="EffectClause.AfterNegationAction"/>: then you can select (not target) 1 face-up monster your opponent
        /// controls and destroy it; if it was destroyed, inflict damage to your opponent equal to its ATK on the field.
        /// </summary>
        MayDestroyOpponentFaceUpMonsterInflictItsAtk,
        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Sasuke Samurai #2 (c11760174): until the End Phase of this turn,
        /// neither player can activate Spell/Trap Cards or their effects (ygopro EFFECT_CANNOT_ACTIVATE, target range both
        /// players, re:IsSpellTrapEffect(), RESET_PHASE|PHASE_END).
        /// </summary>
        NeitherPlayerActivatesSpellTrapsThisTurn,
        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Invader of Darkness (c56647086): while this monster is face-up, its
        /// controller's opponent cannot activate Quick-Play Spell Cards (ygopro EFFECT_CANNOT_ACTIVATE, the card's
        /// activation only: IsQuickPlaySpell and IsHasType(EFFECT_TYPE_ACTIVATE)).
        /// </summary>
        ContinuousOpponentCannotActivateQuickPlaySpells,
        /// <summary>
        /// MonsterNegationAndControlLocks batch — Skill Drain (c82732705, Lua EFFECT_DISABLE, target range both Monster
        /// Zones): while face-up, every face-up Effect Monster on the field has its effects negated
        /// (<see cref="CardInstance.IsNegated"/>, set by <see cref="ContinuousNegationRuntime.Refresh"/>); its effects can
        /// still be activated, and one that resolves while its monster is face-up on the field does nothing
        /// (<see cref="ContinuousNegationRuntime.NegatesResolvingClause"/>). Effects that resolve from the GY / hand apply.
        /// </summary>
        ContinuousNegateMonsterEffectsOnField,
        /// <summary>
        /// MonsterNegationAndControlLocks batch — Final Attack Orders (c52503575, Lua EFFECT_SET_POSITION POS_FACEUP_ATTACK
        /// + EFFECT_CANNOT_CHANGE_POSITION): while face-up, every face-up monster on the field is in Attack Position and its
        /// battle position cannot be changed (the Attack sibling of <see cref="ContinuousForceDefenseLockPosition"/>;
        /// <see cref="MonsterNegationAndControlLocksRuntime.ApplyContinuous"/>).
        /// </summary>
        ContinuousForceAttackLockPosition,
        /// <summary>
        /// MonsterNegationAndControlLocks batch — Blindly Loyal Goblin / Mataza the Zapper (Lua EFFECT_CANNOT_CHANGE_CONTROL,
        /// SINGLE_RANGE in the Monster Zone): while this card is face-up on the field (and not negated), no card effect can
        /// take or switch control of it (<see cref="MonsterNegationAndControlLocksRuntime.ControlLocked"/>).
        /// </summary>
        ControlOfThisCannotSwitch,
        /// <summary>
        /// MonsterNegationAndControlLocks batch — Stumbling (c34646691, Lua FIELD TRIGGER_F on EVENT_SUMMON_SUCCESS /
        /// EVENT_FLIP_SUMMON_SUCCESS / EVENT_SPSUMMON_SUCCESS): each monster Normal, Flip or Special Summoned while this card
        /// is face-up is changed from face-up Attack to face-up Defense Position
        /// (<see cref="MonsterNegationAndControlLocksRuntime.OnSummonSettled"/>).
        /// </summary>
        ChangeSummonedMonstersToDefense,
        /// <summary>
        /// MonsterNegationAndControlLocks batch — Non-Spellcasting Area (c20065549, Lua EFFECT_IMMUNE_EFFECT for
        /// non-Effect monsters vs Spell effects): face-up monsters other than Effect Monsters are unaffected by other
        /// Spell effects (<see cref="MonsterNegationAndControlLocksRuntime.ShieldsFromSpell"/>).
        /// </summary>
        NonEffectMonstersUnaffectedByOtherSpells,
        /// <summary>
        /// ConditionalSpellsAndTraps batch — Self-Destruct Button (c57585212, Lua Duel.SetLP(tp,0) + SetLP(1-tp,0)): both
        /// players' LP become 0 (not damage); the Duel ends at once as a DRAW
        /// (<see cref="ConditionalSpellsAndTrapsRuntime.ApplyBothLifePointsBecomeZero"/>).
        /// </summary>
        BothPlayersLifePointsBecomeZero,
        /// <summary>
        /// ConditionalSpellsAndTraps batch — Destruction Ring (c21219755, EFFECT_FLAG_CARD_TARGET): destroy the target face-up
        /// monster you control; if it was destroyed, <see cref="EffectClause.Amount"/> damage to both players at once
        /// (<see cref="ConditionalSpellsAndTrapsRuntime.ApplyDestroyTargetThenDamageBoth"/>).
        /// </summary>
        DestroyTargetThenDamageBothPlayers,
        /// <summary>
        /// ConditionalSpellsAndTraps batch — D.D. Borderline (c60912752, EFFECT_CANNOT_BP target range 1,1): while this card is
        /// face-up and its controller's GY holds no Spell Card, neither player can conduct their Battle Phase
        /// (<see cref="ConditionalSpellsAndTrapsRuntime.BattlePhaseLocked"/>).
        /// </summary>
        NoBattlePhaseWhileNoSpellInControllerGy,
        /// <summary>
        /// EndOfDamageStepRiders batch — Getsu Fuhma (c21887179), Ryu Kokki (c57281778): SINGLE TRIGGER_F at
        /// EVENT_DAMAGE_STEP_END (this card related to the battle or destroyed by it, ygopro aux.dsercon): the monster it
        /// battled, still related to the battle and of a Type in <see cref="EffectClause.BattledMonsterRaceFilter"/>, is
        /// destroyed (<see cref="EndOfDamageStepRidersRuntime.DestroyBattledMonster"/>).
        /// </summary>
        DestroyBattledMonsterAtEndOfDamageStep,
        /// <summary>
        /// EndOfDamageStepRiders batch — Prickle Fairy (c91559748) e1: SINGLE TRIGGER_F at EVENT_DAMAGE_STEP_END: the monster
        /// this card battled, still related to the battle and in Attack Position, is changed to face-up Defense Position
        /// (<see cref="EndOfDamageStepRidersRuntime.ChangeBattledMonsterToDefense"/>).
        /// </summary>
        ChangeBattledMonsterToDefenseAtEndOfDamageStep,
        /// <summary>
        /// EndOfDamageStepRiders batch — Prickle Fairy (c91559748) e2: EFFECT_CANNOT_SELECT_BATTLE_TARGET for the opponent on
        /// its controller's face-up monsters of the Type in <see cref="EffectClause.RaceFilter"/>, this card included
        /// (<see cref="EndOfDamageStepRidersRuntime.CannotBeAttackTarget"/>, read by ContinuousProtections.CannotBeAttackTarget).
        /// </summary>
        OpponentCannotAttackYourFaceUpRaceMonsters,
        /// <summary>
        /// EndOfDamageStepRiders batch — Insect Princess (c37957847) e1: EFFECT_SET_POSITION POS_FACEUP_ATTACK on the
        /// opponent's monsters of the Type in <see cref="EffectClause.RaceFilter"/> (face-down ones too: they are flipped
        /// face-up and their FLIP effects activate), re-applied on each board refresh
        /// (<see cref="EndOfDamageStepRidersRuntime.ApplyContinuous"/>).
        /// </summary>
        ContinuousChangeOpponentRaceMonstersToAttack,
        /// <summary>
        /// EndOfDamageStepRiders batch — Drillago (c99050989): EFFECT_DIRECT_ATTACK while the opponent's Spell &amp; Trap
        /// Zones (Field Zone included) are empty and every monster they control is face-up with ATK at least
        /// <see cref="EffectClause.Amount"/> (<see cref="EndOfDamageStepRidersRuntime.GrantsDirectAttack"/>).
        /// </summary>
        CanAttackDirectlyIfOpponentControlsOnlyFaceUpMonstersWithAtkAtLeast,
        /// <summary>
        /// EndOfDamageStepRiders batch — Legendary Jujitsu Master (c25773409): SINGLE TRIGGER_F at EVENT_DAMAGE_STEP_END,
        /// this card was the attack target in Defense Position (related to the battle or destroyed by it): the attacker,
        /// still related to the battle, is placed on top of its owner's Deck
        /// (<see cref="EndOfDamageStepRidersRuntime.PlaceAttackerOnTopOfDeck"/>).
        /// </summary>
        PlaceAttackerOnTopOfDeckAtEndOfDamageStep,
        /// <summary>
        /// BattleDamageEvents batch — Piranha Army (c50823978, <see cref="EffectClause.RequiresDirectAttack"/>: this card
        /// attacked directly) / Stone Statue of the Aztecs (c31812496, <see cref="EffectClause.RequiresThisIsAttackTarget"/>:
        /// this card is the attack target): continuous marker, the battle damage this card's controller's opponent takes
        /// from that battle is doubled (Lua EFFECT_CHANGE_BATTLE_DAMAGE, aux.ChangeBattleDamage(1, DOUBLE_DAMAGE); read by
        /// <see cref="BattleDamageEventsRuntime.DoubleBattleDamage"/>).
        /// </summary>
        DoubleBattleDamageToOpponent,
        /// <summary>
        /// SpellEventsAndHandSendTriggers batch — Curse of Darkness (c84970821, Lua CONTINUOUS FIELD: aux.chainreg on
        /// EVENT_CHAINING + EVENT_CHAIN_SOLVED of a Spell Card activation): <see cref="EffectTiming.ContinuousWhileFaceUp"/>
        /// marker; each time the activation of a Spell Card resolves, the player who activated it takes
        /// <see cref="EffectClause.Amount"/> damage, if this card was face-up and applying when that Spell was activated
        /// (<see cref="SpellEventsAndHandSendTriggersRuntime.OnSpellResolved"/>). Not a Chain Link.
        /// </summary>
        InflictDamageToSpellActivatorAfterResolve,
        /// <summary>
        /// SpellEventsAndHandSendTriggers batch — Tower of Babel (c94256039 e4/e5): with the card's own
        /// "place 1 Spell Counter when a Spell resolves" clause, when that placement makes its Spell Counters exactly
        /// <see cref="EffectClause.ConditionAmount"/>, a mandatory trigger destroys this card and, if it does, deals
        /// <see cref="EffectClause.Amount"/> damage to the player who activated that Spell; when another card's effect
        /// brings its Spell Counters to ConditionAmount or more, a mandatory trigger destroys it
        /// (<see cref="SpellEventsAndHandSendTriggersRuntime.AfterOwnSpellCounter"/> /
        /// <see cref="SpellEventsAndHandSendTriggersRuntime.AfterSpellCounterAddedByEffect"/>).
        /// <see cref="EffectTiming.ContinuousWhileFaceUp"/> marker.
        /// </summary>
        DestroyThisAtNthSpellCounterDamageActivator,
        /// <summary>
        /// SpellEventsAndHandSendTriggers batch — Spirit of the Pot of Greed (c4896788, Lua FIELD TRIGGER_F on
        /// EVENT_CHAINING, range MZONE, condition IsAttackPos and the card activation of <see cref="EffectClause.NamedCard"/>):
        /// <see cref="EffectTiming.ContinuousWhileFaceUp"/> marker; when either player activates that card while this card is
        /// in face-up Attack Position, a trigger Chain Link forms right above it; as it resolves, if this card is still
        /// face-up in Attack Position, that player may draw <see cref="EffectClause.Amount"/> card
        /// (<see cref="SpellEventsAndHandSendTriggersRuntime.OnCardActivationPlaced"/>).
        /// </summary>
        ActivatorOfNamedCardMayDrawWhileAttackPosition,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Dimension Distortion (c95194279, Lua CARD_TARGET in LOCATION_REMOVED, controller tp):
        /// Special Summon the targeted face-up monster from the controller's own banished pile to their field, face-up in
        /// Attack or Defense Position (Lua POS_FACEUP), if it is still banished face-up and can be Special Summoned
        /// (<see cref="RecoveryAndPhaseCostsRuntime.SummonBanishedTarget"/>).
        /// </summary>
        SpecialSummonBanishedTarget,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Dimension Fusion (c23557835): each player, the activator first, Special Summons as
        /// many of their own face-up banished monsters as their free Monster Zones allow (exactly that many, their own
        /// choice, face-up Attack or Defense), all at once
        /// (<see cref="FlipMassEffectsRuntime.ResolveEachPlayerSummonsFromOwnBanished"/>).
        /// </summary>
        EachPlayerSpecialSummonsFaceUpBanishedMonstersAsPossible,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Recycle (c96316857, FIELD TRIGGER_O in the S/T Zone on EVENT_PHASE+PHASE_STANDBY,
        /// your turn, count limit 1): pay <see cref="EffectClause.Amount"/> LP (cost) and target 1 non-Monster Card in your
        /// GY; it goes to the bottom of your Deck while this card is still face-up on the field
        /// (<see cref="RecoveryAndPhaseCostsRuntime.ResolveRecycle"/>).
        /// </summary>
        PayLpReturnGyNonMonsterToDeckBottomAtYourStandby,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Reload (c22589918): every card in your hand goes to the Deck (shuffled), then you
        /// draw that many cards (<see cref="RecoveryAndPhaseCostsRuntime.ShuffleHandIntoDeckThenDraw"/>).
        /// </summary>
        ShuffleHandIntoDeckThenDrawSameNumber,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Coach Goblin (c42541548, FIELD TRIGGER_O in the Monster Zone on EVENT_PHASE+PHASE_END,
        /// your turn, count limit 1): cost — reveal 1 Normal Monster in your hand and put it on the bottom of your Deck; draw
        /// 1 card while this card is still face-up (<see cref="RecoveryAndPhaseCostsRuntime.ResolveCoachGoblin"/>).
        /// </summary>
        ReturnHandNormalMonsterToDeckBottomDrawAtYourEndPhase,
        /// <summary>
        /// RecoveryAndPhaseCosts batch — Manticore of Darkness (c77121851, FIELD TRIGGER_O in the GY on EVENT_PHASE+PHASE_END,
        /// either player's, count limit 1, only in the turn it was sent to the GY — not by REASON_RETURN): cost — send 1
        /// monster of a <see cref="EffectClause.RaceFilter"/> Type from your hand or face-up from your Monster Zone to the GY
        /// (a field monster when no zone is free); Special Summon this card from the GY face-up
        /// (<see cref="RecoveryAndPhaseCostsRuntime.TryFirePhaseClause"/>; the ApplyClause arm is a marker).
        /// </summary>
        SendRaceFromHandOrFieldSpecialSummonThisFromGyAtEndPhase,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — DNA Transplant (Continuous Trap, Lua c56769674 s.target AnnounceAttribute
        /// ATTRIBUTE_ALL): the Attribute is declared as the card is activated (a declaration token from
        /// <see cref="EffectZoneFilter.DeclaredAttributes"/>, not a target) and kept on the card
        /// (<see cref="TokensDeclarationsAndPicksRuntime.RecordDeclaredAttribute"/>), even if the resolution is negated.
        /// </summary>
        DeclareMonsterAttribute,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — DNA Transplant (Lua EFFECT_CHANGE_ATTRIBUTE, LOCATION_MZONE both sides): while
        /// this card is face-up in a Spell &amp; Trap Zone and not negated, every face-up monster on the field has the
        /// declared Attribute (read live by <see cref="EquipFamilyRuntime.CurrentAttribute"/>; the latest change wins).
        /// </summary>
        AllFaceUpMonstersBecomeDeclaredAttribute,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — Jade Insect Whistle (Lua c95214051): the activator's opponent selects 1
        /// monster of <see cref="EffectClause.RaceFilter"/> from their own Deck (if any), shuffles that Deck and places the
        /// card on top (revealed). Not a target; the opponent's pick goes through AskCardPick.
        /// </summary>
        OpponentPlacesRaceMonsterFromTheirDeckOnTop,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — A Hero Emerges (Lua c21597117 s.activate): with a free Monster Zone, a random
        /// card of the activator's hand is revealed; a monster that can be Special Summoned is Special Summoned face-up,
        /// any other card is sent to the GY.
        /// </summary>
        RandomHandCardSpecialSummonOrSendToGy,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — Wild Nature's Release (Lua c61166988): the targeted face-up monster gains ATK
        /// equal to its current DEF until the End Phase and is destroyed during this turn's End Phase
        /// (<see cref="CardInstance.TempDestroyOnEndOfTurn"/>; both end if it is flipped face-down or leaves the field).
        /// </summary>
        TargetGainsAtkEqualToDefDestroyAtEndPhase,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — Balloon Lizard (Lua c39892082 e1): during its controller's Standby Phase, put
        /// <see cref="EffectClause.Amount"/> counter(s) on this face-up card (<see cref="CardInstance.Counters"/>).
        /// </summary>
        PlaceCounterOnThisCard,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — Balloon Lizard (Lua c39892082 e2/e3): destroyed with 1 or more counters on it,
        /// inflict counters x <see cref="EffectClause.Amount"/> damage to the player whose card destroyed it (the reason
        /// player: the other battler's controller, or the resolving player of the destroying effect).
        /// </summary>
        InflictCountersTimesAmountToDestroyer,
        /// <summary>
        /// ChaosBanishSummons batch — Chaos Sorcerer / Black Luster Soldier - Envoy of the Beginning (c9596126 / c72989439
        /// e2, Lua EFFECT_TYPE_IGNITION + EFFECT_FLAG_CARD_TARGET): banish the targeted monster on the field, still a monster
        /// in a Monster Zone (face-up when <see cref="EffectClause.RequiresTargetFaceUp"/>), face-up
        /// (<see cref="ChaosBanishSummonsRuntime.BanishTargetedFieldMonster"/>). Face-down monsters and this card itself are
        /// legal targets unless the clause says otherwise.
        /// </summary>
        BanishTargetedFieldMonster,
        /// <summary>
        /// ChaosBanishSummons batch — Black Luster Soldier - Envoy of the Beginning (c72989439 e3, EVENT_BATTLE_DESTROYING,
        /// Duel.ChainAttack): this attacking monster may attack again next (a second attack in a row); the right lapses as
        /// soon as another monster declares an attack (<see cref="ChaosBanishSummonsRuntime.GrantChainAttack"/>).
        /// </summary>
        MakeSecondAttackInARow,
        /// <summary>
        /// ChaosBanishSummons batch — Chaosrider Gustaph (c47829960): at resolution choose 1 to
        /// <see cref="EffectClause.PickUpTo"/> Spells in your GY (no target) and banish them; this card gains
        /// <see cref="EffectClause.Amount"/> ATK for each card banished until the end of the opponent's next turn
        /// (<see cref="ChaosBanishSummonsRuntime.ResolveGustaph"/>).
        /// </summary>
        BanishUpToSpellsFromYourGyGainAtkEach,
        /// <summary>
        /// ChaosBanishSummons batch — Soul Absorption (c68073522, FIELD TRIGGER_F on EVENT_REMOVE): while face-up, each time
        /// a card(s) is banished its controller gains <see cref="EffectClause.Amount"/> LP for each non-Token card banished in
        /// that one event (<see cref="BanishEvents"/>).
        /// </summary>
        GainLpPerCardBanished,
        /// <summary>
        /// ChaosBanishSummons batch — Primal Seed (c23701465): each target still in your banished pile is added to your hand
        /// (<see cref="EffectZoneFilter.ControllerBanishedCards"/>).
        /// </summary>
        AddBanishedTargetsToHand,
        /// <summary>
        /// ChaosBanishSummons batch — Strike Ninja (c41006930, QUICK_O): banish this face-up card until the End Phase
        /// (REASON_TEMPORARY; it returns to the field in this turn's End Phase).
        /// </summary>
        BanishThisUntilEndPhase,
        /// <summary>
        /// TargetingTruth batch — Big Burn (c95472621, Lua EFFECT_TYPE_ACTIVATE on EVENT_BECOME_TARGET): a Chain answer to
        /// an effect of the opponent (rp~=tp) whose targets include a monster in either GY
        /// (<see cref="TargetingTruthRuntime.OpponentTargetsGyMonster"/>); banish every monster in both GYs (SpElimFilter:
        /// Spirit Elimination banishes that player's field monsters instead) — <see cref="TargetingTruthRuntime.BanishAllGyMonsters"/>.
        /// </summary>
        BanishAllGyMonstersWhenOpponentTargetsGyMonster,
        /// <summary>
        /// AttackObligations batch — Berserk Gorilla (c39168895, SINGLE EFFECT_MUST_ATTACK; ContinuousWhileFaceUp marker): "This
        /// card must attack if able." Read by <see cref="AttackObligationsRuntime"/> (End Battle / End Turn legality, the AI's
        /// attack duty); never resolved as an effect.
        /// </summary>
        MustAttackIfAble,
        /// <summary>
        /// AttackObligations batch — Berserk Gorilla (c39168895, SINGLE EFFECT_SELF_DESTROY, range MZONE, condition
        /// IsDefensePos; ContinuousWhileFaceUp marker): "If this card is in face-up Defense Position, destroy this card." Swept
        /// on each board refresh by <see cref="AttackObligationsRuntime.SweepSelfDestroy"/>; not a Chain Link.
        /// </summary>
        SelfDestroyWhileFaceUpDefense,
        /// <summary>
        /// AttackObligations batch — Amazoness Archers (c67987611): every monster the opponent controls that is not in
        /// face-up Attack Position is changed to it (FLIP effects do not activate); every face-up monster they control then
        /// loses <see cref="EffectClause.Amount"/> ATK (RESETS_STANDARD) and must attack this turn, if able
        /// (EFFECT_MUST_ATTACK, RESETS_STANDARD_PHASE_END). <see cref="AttackObligationsRuntime.ApplyAllOpponentMonstersMustAttack"/>.
        /// </summary>
        OpponentMonstersToFaceUpAttackLoseAtkMustAttack,
        /// <summary>
        /// AttackObligations batch — Diffusion Wave-Motion (c87880531): the target (a face-up Level <see cref="EffectClause.Amount"/>
        /// or higher <see cref="EffectClause.RaceFilter"/> monster you control) must attack every monster the opponent controls
        /// once each this turn (EFFECT_MUST_ATTACK + EFFECT_ATTACK_ALL), the controller's other monsters cannot attack this
        /// turn, and monsters it destroys by battle have their effects negated (EVENT_BATTLED + EFFECT_CANNOT_TRIGGER).
        /// <see cref="AttackObligationsRuntime.ApplyTargetMustAttackAll"/>.
        /// </summary>
        TargetMustAttackAllOthersCannotAttack,
        /// <summary>
        /// AttackObligations batch — Staunch Defender (c92854392): until the end of this Battle Phase the opponent can only
        /// attack the target (EFFECT_MUST_ATTACK_MONSTER) and every monster they control must attack, if able
        /// (EFFECT_MUST_ATTACK). <see cref="AttackObligationsRuntime.ApplyOpponentMustAttackTarget"/>.
        /// </summary>
        OpponentAttacksOnlyTargetAndMustAttackIt,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch — Emissary of the Afterlife (c75043725): each player selects 1 Level Amount or
        /// lower Normal Monster in their own Deck (none if they have none), the activator first, then both are added to the
        /// hands together and revealed (<see cref="GyHandAndSummonEventTriggersRuntime.EachPlayerAddsFromDeck"/>).
        /// </summary>
        EachPlayerAddsLevelOrLowerNormalMonsterFromOwnDeck,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch — Backfire (c82705573): FIELD TRIGGER_F marker; each time 1+ face-up monsters of
        /// <see cref="EffectClause.AttributeFilter"/> its controller controlled are destroyed and sent to that player's GY, one
        /// trigger inflicts Amount to the opponent (<see cref="GyHandAndSummonEventTriggersRuntime"/>).
        /// </summary>
        InflictDamageWhenFaceUpAttributeMonsterYouControlDestroyedToGy,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch — Blessings of the Nile (c30653113): FIELD TRIGGER_F marker; each time 1+ cards
        /// are discarded from its controller's hand to the GY by an effect of a card the opponent controls, one trigger gains
        /// Amount LP (<see cref="GyHandAndSummonEventTriggersRuntime"/>).
        /// </summary>
        GainLpEachTimeDiscardedByOpponentEffect,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch — "7" (c67048711): as its activation resolves, if exactly
        /// <see cref="EffectClause.ConditionAmount"/> face-up <see cref="EffectClause.NamedCard"/> are in your Spell & Trap
        /// Zones (this one counts), a mandatory trigger: draw Amount (if still that many), then destroy every face-up
        /// <see cref="EffectClause.NamedCard"/> on the field (<see cref="GyHandAndSummonEventTriggersRuntime.OnSevenActivated"/>).
        /// </summary>
        DrawThenDestroyAllFaceUpNamedWhenCountFaceUpNamed,
        /// <summary>
        /// GyHandAndSummonEventTriggers batch — Beckoning Light (c16255442): discard your entire hand; then, if your GY holds at
        /// least as many <see cref="EffectClause.AttributeFilter"/> monsters as cards discarded to the GY, add exactly that
        /// many of your choice to the hand (otherwise none) (<see cref="GyHandAndSummonEventTriggersRuntime.DiscardHandThenAdd"/>).
        /// </summary>
        DiscardHandThenAddAttributeMonstersFromGyPerDiscard,
        /// <summary>
        /// SpellTrapSealsAndImmunity batch — Sonic Jammer (c84550200, FLIP: field EFFECT_CANNOT_ACTIVATE, target range (0,1),
        /// value re:IsHasType(EFFECT_TYPE_ACTIVATE) and re:IsSpellEffect(), RESET_PHASE|PHASE_END, 2): until the end of the
        /// next turn's End Phase the opponent cannot activate Spell Cards (from the hand or Set); the effects of face-up Spells
        /// stay usable (<see cref="SpellTrapSealsAndImmunityRuntime.RegisterSpellCardLock"/>).
        /// </summary>
        OpponentCannotActivateSpellCardsUntilEndOfNextTurn,
        /// <summary>
        /// SpellTrapSealsAndImmunity batch — Dust Barrier (c31476755, field EFFECT_IMMUNE_EFFECT on both Monster Zones, target
        /// TYPE_NORMAL, value: the effect's owner player is not this card's controller and re:IsSpellEffect()): while this card
        /// is face-up, face-up Normal Monsters (Tokens included) on the field are unaffected by Spell effects of this card's
        /// controller's opponent (<see cref="SpellTrapSealsAndImmunityRuntime.ShieldsFromOpponentSpell"/>).
        /// </summary>
        NormalMonstersUnaffectedByOpponentSpells,
        /// <summary>
        /// SpellTrapSealsAndImmunity batch — Dust Barrier (Lua s.target: SetTurnCounter(0) and a CANNOT_DISABLE
        /// EVENT_PHASE+PHASE_STANDBY effect, condition tp == turn player, ct==2 -> Destroy): this face-up card counts its
        /// controller's Standby Phases (even while negated) and is destroyed in the <see cref="EffectClause.Amount"/>th one
        /// after its activation (<see cref="SpellTrapSealsAndImmunityRuntime.OnStandbyPhase"/>).
        /// </summary>
        DestroyThisOnYourNthStandbyAfterActivation,
        /// <summary>
        /// NormalMonsterSpells batch — Delta Attacker (c39719977): each face-up non-Token Normal Monster you control with 2
        /// others of its name you control can attack directly this turn (Lua EFFECT_DIRECT_ATTACK per monster,
        /// RESETS_STANDARD_PHASE_END: <see cref="CardInstance.DirectAttackThisTurn"/>;
        /// <see cref="NormalMonsterSpellsRuntime.ResolveDeltaAttacker"/>).
        /// </summary>
        NormalsSharingNameAttackDirectlyThisTurn,
        /// <summary>
        /// NormalMonsterSpells batch — Thousand Energy (c5703682) / Triangle Power (c32298781): the original ATK and DEF of
        /// each face-up non-Token Normal Monster you control of Level <see cref="EffectClause.MonsterLevelMin"/> become their
        /// original value + <see cref="EffectClause.Amount"/> (EFFECT_SET_BASE_ATTACK / _DEFENSE, RESETS_STANDARD); in this
        /// turn's End Phase every face-up Normal Monster (Tokens too) of that Level you control is destroyed
        /// (<see cref="NormalMonsterSpellsRuntime.ResolveOriginalBoost"/>, <see cref="NormalMonsterSpellsRuntime.OnEndPhase"/>).
        /// </summary>
        RaiseOriginalAtkDefOfLevelNormalsDestroyThemAtEndPhase,
        /// <summary>
        /// NormalMonsterSpells batch — The Law of the Normal (c66926224): both players discard their whole hands (one event,
        /// REASON_DISCARD), then every card on the field except face-up Normal Monsters of Level
        /// <see cref="EffectClause.MonsterLevelMax"/> or lower is destroyed
        /// (<see cref="NormalMonsterSpellsRuntime.ResolveLawOfTheNormal"/>).
        /// </summary>
        BothDiscardHandsDestroyAllButLowLevelNormals,
        /// <summary>
        /// NormalMonsterSpells batch — Order to Smash (c39019325): Tribute the targeted face-up Normal Monster by the effect;
        /// if it was Tributed, choose 1 to <see cref="EffectClause.PickUpTo"/> Spells/Traps the opponent controls (no target)
        /// and destroy them (<see cref="NormalMonsterSpellsRuntime.ResolveOrderToSmash"/>).
        /// </summary>
        TributeTargetedNormalThenDestroyUpToOppSpellTraps,
        /// <summary>
        /// BattleAndPositionRecords batch — Atomic Firefly (c87340664, SINGLE TRIGGER_F EVENT_BATTLE_DESTROYED): destroyed by
        /// battle, the player who destroyed it (the reason player: the other battler's controller) takes
        /// <see cref="EffectClause.Amount"/> effect damage (<see cref="BattleAndPositionRecordsRuntime.InflictDamageToDestroyer"/>).
        /// </summary>
        InflictDamageToPlayerWhoDestroyedThis,
        /// <summary>
        /// BattleAndPositionRecords batch — Disc Fighter (c19612721, SINGLE TRIGGER_F EVENT_BATTLE_CONFIRM): this card attacks a
        /// Defense Position monster with <see cref="EffectClause.Amount"/> or more DEF — destroy it before damage calculation,
        /// so no damage calculation is applied (<see cref="BattleAndPositionRecordsRuntime.FireBattleConfirm"/>).
        /// </summary>
        DestroyAttackedDefenseMonsterWithDefAtLeastNoDamageCalculation,
        /// <summary>
        /// BattleAndPositionRecords batch — Ghost Knight of Jackal (c13386503, SINGLE TRIGGER_O EVENT_BATTLE_DESTROYING,
        /// aux.bdogcon): you can target the opponent's monster this card destroyed by battle (now in the GY) and Special Summon it
        /// to your field in face-up Defense Position (<see cref="BattleAndPositionRecordsRuntime.TryFireBattleDestroying"/>).
        /// </summary>
        SpecialSummonBattleDestroyedMonsterToYourFieldInDefense,
        /// <summary>
        /// BattleAndPositionRecords batch — Legacy Hunter (c87010442, SINGLE TRIGGER_F EVENT_BATTLE_DESTROYING): the monster it
        /// destroyed by battle was in face-down Defense Position at the battle — the opponent returns 1 random card from their
        /// hand to the Deck, which is shuffled (<see cref="BattleAndPositionRecordsRuntime.TryFireBattleDestroying"/>).
        /// </summary>
        OpponentReturnsRandomHandCardToDeckShuffle,
        /// <summary>
        /// BattleAndPositionRecords batch — The Unhappy Girl (c27618634, SINGLE EFFECT_INDESTRUCTABLE_BATTLE while
        /// POS_FACEUP_ATTACK): not destroyed by battle while in face-up Attack Position
        /// (<see cref="BattleAndPositionRecordsRuntime.BattleIndestructible"/>).
        /// </summary>
        CannotBeDestroyedByBattleWhileInFaceUpAttackPosition,
        /// <summary>
        /// BattleAndPositionRecords batch — The Unhappy Girl (c27618634, EVENT_BATTLED record + FIELD EFFECT_CANNOT_ATTACK /
        /// EFFECT_CANNOT_CHANGE_POSITION, target range 0,MZONE): each opponent's monster that battled it while it was in
        /// face-up Attack Position cannot attack or change its battle position by hand while this card stays in face-up
        /// Attack Position (<see cref="BattleAndPositionRecordsRuntime.NoteBattled"/>).
        /// </summary>
        OpponentMonstersThatBattledThisCannotAttackOrChangePosition,
        /// <summary>
        /// BattleAndPositionRecords batch — Judgment of the Desert (c4869446, Continuous Trap: EVENT_CHANGE_POS SetCardTarget +
        /// FIELD EFFECT_CANNOT_CHANGE_POSITION on both fields): every monster flipped face-up after this card's activation
        /// resolved is recorded and cannot change its battle position by hand while this card stays face-up
        /// (<see cref="BattleAndPositionRecordsRuntime.StartFlipRecord"/>).
        /// </summary>
        MonstersFlippedFaceUpAfterActivationCannotChangePosition,
        /// <summary>
        /// BattleAndPositionRecords batch — Curse of Anubis (c66742250, Normal Trap): every face-up Attack Position Effect
        /// Monster on the field becomes face-up Defense Position; until the end of the turn their original DEF becomes 0 and
        /// they cannot change battle positions by hand (<see cref="BattleAndPositionRecordsRuntime.ChangeToDefenseZeroDefLock"/>).
        /// </summary>
        ChangeFaceUpAttackEffectMonstersToDefenseOriginalDefZeroLock,
        /// <summary>
        /// ZoneCapsAndPhaseWindows batch — Continuous: limit the number of Monster Zones the controller can use
        /// (EFFECT_MAX_MZONE). Cards like Kaiser Colosseum, Spatial Collapse. Honored by every placement/legality
        /// path through DuelEngine.GetUsableMonsterZoneCount.
        /// </summary>
        ContinuousMaxMonsterZoneCap,
        /// <summary>
        /// ZoneCapsAndPhaseWindows batch — Continuous: limit the number of Spell/Trap Zones the controller can use
        /// (EFFECT_MAX_SZONE). Cards like Spatial Collapse. Honored by every placement/legality path through
        /// DuelEngine.GetUsableSpellTrapZoneCount.
        /// </summary>
        ContinuousMaxSpellTrapZoneCap,
        /// <summary>
        /// ZoneCapsAndPhaseWindows batch — "It becomes the End Phase" (Neko Mane King). Queues a safe phase jump
        /// that executes at the next legal transition point (post-Chain idle, end of Damage Step, or end of Battle Phase).
        /// Does NOT jump from Draw/Standby/Main1 directly; waits for a safe gate. See DuelEngine.SkipToEndPhase.
        /// </summary>
        PhaseJumpToEndPhase,
        /// <summary>
        /// LocksRedirectsAndActivity batch — Continuous: "You cannot discard cards as a cost to activate cards or
        /// effects" (Goblin of Greed's per-player, per-effect variant; ygopro EFFECT_CANNOT_DISCARD_AS_COST).
        /// Read by every discard-cost site through <see cref="LocksRedirectsAndActivityRuntime.DiscardAsCostLocked"/>.
        /// Side picks whose discards the lock covers (Controller = yours only, Opponent, Both).
        /// </summary>
        ContinuousCannotDiscardAsCost,
        /// <summary>
        /// LocksRedirectsAndActivity batch — Continuous: "Neither player can activate cards or effects"
        /// (EFFECT_CANNOT_ACTIVATE, every activation site — S/T, monster ignition / trigger / quick, hand traps).
        /// Chaos Emperor Dragon - Envoy of the End's share of this row. The per-player half is
        /// <see cref="ContinuousThisPlayerCannotActivate"/>.
        /// </summary>
        ContinuousNeitherPlayerCanActivate,
        /// <summary>
        /// LocksRedirectsAndActivity batch — Continuous: "You cannot activate cards or effects" — the per-player share
        /// of the activation lock (Side picks which duelist is locked). Same sites as
        /// <see cref="ContinuousNeitherPlayerCanActivate"/>.
        /// </summary>
        ContinuousThisPlayerCannotActivate,
        /// <summary>
        /// LocksRedirectsAndActivity batch — "If this card would leave the field, return it to its owner's hand instead"
        /// (EFFECT_LEAVE_FIELD_REDIRECT: Dark Magician of Chaos). One funnel every field exit goes through
        /// (<see cref="LocksRedirectsAndActivityRuntime.RedirectFieldExit"/>), unlike the GY-send hub.
        /// </summary>
        LeaveFieldReturnsToOwnerHandInstead,
        /// <summary>
        /// LocksRedirectsAndActivity batch — a cost that requires "you have not activated any other cards or effects this
        /// turn" (Lua ACTIVITY_CHAIN counter check). Honored by
        /// <see cref="LocksRedirectsAndActivityRuntime.ActivityCostLegal"/> against
        /// <see cref="LocksRedirectsAndActivityRuntime.NoteActivation"/>.
        /// </summary>
        RequiresNoOtherActivationThisTurn,
        /// IgnitionCostsAndSimpleSpells batch — Sand Gambler (c50593156): toss <see cref="EffectClause.CoinCount"/> coins
        /// through the coin bus; all Heads destroys every monster the opponent controls, all Tails every monster you control,
        /// anything else does nothing (<see cref="IgnitionCostsAndSimpleSpellsRuntime.ResolveCoinGamble"/>).
        /// </summary>
        CoinTossAllHeadsDestroyOppMonstersAllTailsDestroyYours,
        /// <summary>
        /// IgnitionCostsAndSimpleSpells batch — Mystik Wok (c80161395): gain LP equal to the current ATK or DEF of the
        /// monster Tributed as the cost, chosen as the card is activated (Lua SelectEffect in s.target; automatic when one
        /// of them is 0) — <see cref="IgnitionCostsAndSimpleSpellsRuntime.ResolveTributedStatGain"/>.
        /// </summary>
        GainLpEqualToTributedAtkOrDef,
        /// <summary>
        /// AttackShieldsAndDamageZero batch — Lua EFFECT_AVOID_BATTLE_DAMAGE / EFFECT_NO_BATTLE_DAMAGE on the matching monsters
        /// (<see cref="EffectClause.RaceFilter"/>, <see cref="EffectClause.NormalMonsterMaxLevel"/>): battle damage from a battle
        /// involving one becomes 0. <see cref="EffectSide.Controller"/> = only this card's controller, whichever side the
        /// matching monster is on (Emissary of the Oasis); <see cref="EffectSide.Both"/> = the controller of each matching
        /// battler (The Sanctuary in the Sky). Read by <see cref="AttackShieldsAndDamageZeroRuntime.AvoidsBattleDamage"/>.
        /// </summary>
        AvoidBattleDamageFromBattlesInvolving,
        /// <summary>
        /// AttackShieldsAndDamageZero batch — Soul-Absorbing Bone Tower (c63012333, FIELD TRIGGER_F on EVENT_SPSUMMON_SUCCESS):
        /// each time 1+ face-up <see cref="EffectClause.RaceFilter"/> monster(s) are Special Summoned (not with this card),
        /// send the top <see cref="EffectClause.Amount"/> cards of the opponent's Deck to the GY. An engine hook
        /// (<see cref="AttackShieldsAndDamageZeroRuntime.OnSpecialSummoned"/>), never resolved by ApplyClause.
        /// </summary>
        MillOpponentWhenRaceSpecialSummoned,
        /// SanctuaryAndZera batch — The Agent of Judgment - Saturn (c91345518 s.damop): inflict damage to the opponent equal
        /// to your LP minus theirs, read at resolution (nothing when that is not positive).
        /// </summary>
        InflictLpLeadAsDamageToOpponent,
        /// <summary>
        /// SanctuaryAndZera batch — Light of Judgment (c44595286 s.activate): at resolution the activator chooses
        /// (Duel.SelectOption over the options that still have cards) to send 1 card the opponent controls to the GY
        /// (selected, not targeted) or to look at the opponent's hand and discard 1 card from it
        /// (<see cref="SanctuaryAndZeraRuntime.ResolveSendOrDiscard"/>).
        /// </summary>
        SendOppFieldCardToGyOrDiscardFromOppHand,
        /// <summary>
        /// LvChain batch — Dark Mimic LV3 (c1102515 s.activate/TRIGGER_F): destroyed by battle and sent to the GY,
        /// draw <see cref="EffectClause.Amount"/> cards — or
        /// <see cref="EffectClause.AmountIfSpecialSummonedByNamed"/> when this card was Special Summoned by the
        /// effect of <see cref="EffectClause.SpecialSummonedByNamedCard"/> (Dark Mimic LV1). Read at resolution
        /// through <see cref="LvChainRuntime.ResolveDraw"/>.
        /// </summary>
        DrawAmountWhenSpecialSummonedByNamed,
        /// <summary>
        /// FusionSubstitutes batch (v180, coverage family F2) — Mystical Sheep #1, Versago the Destroyer, Goddess with
        /// the Third Eye, Beastking of the Swamps, The Light / Dark / Earth - Hex-Sealed Fusion: "You can substitute
        /// this card for any 1 Fusion Material Monster." A static (CONTINUOUS) ability on the monster itself. Read by
        /// <see cref="FusionSubstitutesRuntime.IsFusionSubstitute"/> from the fusion material search in
        /// SpellTrapEffects.TryCollectMaterials, which may satisfy at most one named material with such a card.
        /// </summary>
        FusionSubstitute,
        /// <summary>
        /// Hallowed Life Barrier (c88789641): "Discard 1 card from your hand. This turn, any damage you take from your
        /// opponent's cards and effects becomes 0." Sets the activator's <see cref="DuelistState.NoDamageThroughTurnNumber"/>
        /// for the current turn; the same flag Crush Card Virus reads, checked by both battle (ApplyDamage) and effect
        /// (ApplyEffectDamage / simultaneous) damage. Runtime: TextEffectRuntime.
        /// </summary>
        NoDamageThisTurn,
        /// <summary>
        /// Ninjitsu Art of Decoy (c89628781): a bound Continuous Trap ("Target 1 face-up "Ninja" monster you
        /// control; it cannot be destroyed by battle."). The Activate clause binds this card to the target
        /// (like <see cref="ContinuousCannotAttack"/> / Spellbinding Circle); the bound host is then immune to
        /// battle destruction while the Trap stays face-up.
        /// <see cref="WRLDZ.Duel.Rules.OfficialEffectRegistry.CannotBeDestroyedByBattle"/> scans the host's bound cards
        /// for this action's <see cref="EffectTiming.ContinuousWhileFaceUp"/> clause.
        /// </summary>
        ContinuousCannotBeDestroyedByBattle,
        /// <summary>
        /// Taunt (c90740329): a Normal Trap the controller activates during the opponent's Main Phase 1, selecting 1
        /// monster on their own field. Until the end of that turn the opponent may attack only that monster — and is
        /// never obliged to attack at all. Read by
        /// <see cref="BattleRulesRuntime.ForbidsAttackTarget"/> from DuelEngine.TryAttack and CanBeAttacked; armed at
        /// resolution by <see cref="BattleRulesRuntime.ArmOpponentAttackLock"/>.
        /// </summary>
        OpponentAttacksOnlySelectedMonsterThisTurn,
        /// <summary>
        /// Two-Man Cell Battle (c25578802): a Continuous Spell's field trigger (ygopro EFFECT_TYPE_TRIGGER_O with
        /// EFFECT_FLAG_BOTH_SIDE) — "During each player's End Phase, the turn player can Special Summon 1 Level 4
        /// Normal Monster from their hand." Fired by FirePhaseTriggersFor through
        /// <see cref="SimpleTriggersAndSwarmRuntime.TryOfferEndPhaseNormalLevelFromHand"/>; the acting player is the
        /// turn player, not this card's controller (that is what BOTH_SIDE buys).
        /// </summary>
        SpecialSummonLevelNormalFromHandAtEachPlayersEndPhase,
        /// <summary>
        /// Gorgon's Eye (c52648457): a Normal Trap. Until the end of this turn the effects of every face-up
        /// monster in face-up Defense Position on the field are negated (ygopro a field EFFECT_DISABLE with
        /// RESET_PHASE|PHASE_END over both Monster Zones). Recomputed on each board refresh by
        /// <see cref="ContinuousNegationRuntime.Refresh"/>; read through
        /// <see cref="CardInstance.GorgonEyeNegated"/>.
        /// </summary>
        NegateEffectsOfDefensePositionMonstersThisTurn,
        /// <summary>
        /// Mind on Air (c66690411): a Continuous Trap — "Your opponent must play with their hand revealed." While it
        /// is face-up on the field, its controller's opponent has <see cref="DuelistState.HandRevealed"/> set (a
        /// continuous field rule, not a one-shot, and not turn-scoped). Recomputed by
        /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.RefreshHandReveal"/>; read by
        /// <see cref="ContinuousTrapsAndSpiritSupportRuntime.HandRevealed"/>.
        /// </summary>
        OpponentMustPlayWithHandRevealed,
        /// <summary>
        /// Level Up! (c25290459): a Normal Spell — "Send 1 face-up "LV" monster you control to the GY;
        /// Special Summon 1 monster from your hand or Deck that is listed in the sent monster's text, ignoring its
        /// Summoning conditions." The target is the LV monster (<see cref="EffectClause.TargetSeriesName"/>); the
        /// monster to Special Summon is whatever that monster's own LV-progression clause names, so no LV table is
        /// hardcoded. Resolved by <see cref="LvChainRuntime.ResolveProgression"/>.
        /// </summary>
        SpecialSummonLvProgressionFromSentMonster,
    }


    public enum EffectSide
    {
        Controller,
        Opponent,
        Both,
        Either,
    }

    public enum EffectZoneFilter
    {
        None = 0,
        FieldMonsters,
        FieldSpellTraps,
        OppAttackPositionMonsters,
        OppFaceUpMonsters,
        EitherGyMonsters,
        ControllerGySpells,
        ControllerHandDragons,
        DeckMonstersAtkLeq,
        FieldAnyMonster,
        AttackingMonster,
        /// <summary>The opponent's monster in the current battle (Bark of Dark Ruler).</summary>
        OpponentBattlingMonster,
        /// <summary>Opponent's Defense Position monsters (Dark Mirror Force).</summary>
        OppDefensePositionMonsters,
        /// <summary>Any card on either field (monsters, S/T, Field Spell).</summary>
        AnyCardOnField,
        /// <summary>Monsters in the controller's hand (discard cost).</summary>
        ControllerHandMonsters,
        /// <summary>Filtered monsters in the controller's Deck (Special Summon).</summary>
        ControllerDeckMonsters,
        /// <summary>
        /// Every other card on either field except the effect source
        /// (Daedalus: destroy all other cards on the field).
        /// </summary>
        AllOtherCardsOnField,
        /// <summary>Monsters the controller currently controls (tribute cost).</summary>
        ControllerMonsters,
        /// <summary>Monsters in the controller's GY (banish cost / SS / add to hand).</summary>
        ControllerGyMonsters,
        /// <summary>Any card the opponent controls.</summary>
        OppAnyCardOnField,
        /// <summary>Field Spells in the controller's Deck (Terraforming).</summary>
        DeckFieldSpells,
        /// <summary>Main-deck monsters matching RaceFilter with Level ≤ Amount (ROTA).</summary>
        DeckMonstersRaceLevelLeq,
        /// <summary>Traps in the controller's GY (Mask of Darkness).</summary>
        ControllerGyTraps,
        /// <summary>Face-up Field Spell Zones on either field (Burning Land).</summary>
        FieldSpellsOnField,
        /// <summary>Equip Spells in the controller's Deck (Iron Blacksmith Kotetsu).</summary>
        DeckEquipSpells,
        /// <summary>Spell Cards in the controller's Deck (Dark Sage).</summary>
        ControllerDeckSpells,
        /// <summary>Monsters in the opponent's GY (Book of Life second target).</summary>
        OppGyMonsters,
        /// <summary>Monsters the opponent controls, face-up or face-down (Spellbinding Circle).</summary>
        OppMonsters,
        /// <summary>Any cards in either GY (Soul Release).</summary>
        EitherGyCards,
        /// <summary>Any cards in the controller's GY (A Feather of the Phoenix).</summary>
        ControllerGyCards,
        /// <summary>Ritual Monsters in the controller's Deck (Senju of the Thousand Hands).</summary>
        DeckRitualMonsters,
        /// <summary>Ritual Spells in the controller's Deck (Sonic Bird).</summary>
        DeckRitualSpells,
        /// <summary>Either Ritual Monsters or Ritual Spells in the Deck (Manju).</summary>
        DeckRitualMonstersOrSpells,

        // ---- v103 merge: GitHub-lineage (v51-v55) members appended after the local v100-v102 members. ----
        // Serialized as ints in compiled programs; CardTextEffectCompiler.Version 103 invalidates every older program.
        /// <summary>Cards in the opponent's hand, revealed to choose (Confiscation / The Forceful Sentry).</summary>
        OppHandCards,
        /// <summary>The opponent's face-down monsters (Bombardment Beetle).</summary>
        OppFaceDownMonsters,
        /// <summary>Face-down Spell/Trap Cards on either field (Nobleman of Extermination).</summary>
        FaceDownSpellTraps,
        /// <summary>Cards in your hand or Deck whose rules name matches NamedCard (Elegant Egotist).</summary>
        HandOrDeckNamed,
        /// <summary>Any card in either GY (Soul Release).</summary>
        AnyGyCards,
        /// <summary>Monsters the controller controls, face-up or face-down (Two-Pronged Attack).</summary>
        ControllerAnyMonsters,
        /// <summary>Monsters the opponent controls, face-up or face-down (Change of Heart).</summary>
        OppAnyMonsters,
        /// <summary>Face-up Spell/Trap Cards on either field with the printed name NamedCard (Dragon Piper).</summary>
        FaceUpNamedSpellTraps,
        /// <summary>Any card in the opponent's GY (Disappear — exact-1 banish; not monsters-only / either-GY).</summary>
        OppGyCards,
        /// <summary>
        /// Spell/Trap Cards the opponent controls: their Spell &amp; Trap Zones and Field Zone (Spiritualism).
        /// FieldSpellTraps collects both sides whatever the Side, and MST / Dust Tornado rely on that.
        /// </summary>
        OppSpellTraps,
        /// <summary>
        /// TypeDeclaration batch (v142): the Monster Types a player may declare (<see cref="EffectActionKind.DeclareMonsterType"/>).
        /// Declaration tokens, not cards (<see cref="TypeDeclarationRuntime.MonsterTypeTokens"/>).
        /// </summary>
        DeclaredMonsterTypes,
        /// <summary>
        /// RedirectAndReplace batch (v142): another monster that would be an appropriate target for the Chain Link
        /// being answered (its own legal target list, in a Monster Zone, not its current target) — or, for Shift,
        /// another monster the attacker could attack. <see cref="EffectClause.Side"/> Controller keeps it to the
        /// answering player's monsters (Shift); Both allows either field (Fairy's Hand Mirror).
        /// See <see cref="RedirectAndReplaceRuntime.Candidates"/>.
        /// </summary>
        AnotherAppropriateTarget,
        /// <summary>Face-down monsters on either field (Acid Trap Hole). Not the opponent-only OppFaceDownMonsters.</summary>
        FieldFaceDownMonsters,
        /// <summary>
        /// TargetedClassics batch (Miracle Dig): face-up monsters in the controller's banished pile. A card banished
        /// face-down (Lightforce Sword) is never counted and never a target (ygopro IsFaceup() in LOCATION_REMOVED).
        /// </summary>
        ControllerBanishedFaceUpMonsters,
        /// <summary>
        /// EquipRiders batch (Mask of Dispel): face-up Spell Cards on either field — Spell &amp; Trap Zones and Field
        /// Zones — never the activating card itself (Lua IsFaceup() and IsSpell(), LOCATION_SZONE both sides, except c).
        /// </summary>
        FieldFaceUpSpells,
        /// <summary>
        /// FlipCostsAndFusion batch — De-Fusion: a face-up Fusion Monster on either field (Lua IsFaceup +
        /// IsType(TYPE_FUSION)). Text path only: CollectTargets via
        /// <see cref="FlipCostsAndFusionRuntime.CollectFaceUpFusions"/> (prompt kind FaceUpMonsterOnField); the legacy
        /// SpellTrapEffects.MapCompiledTargetKind mirror leaves it None, so the text path picks the target.
        /// </summary>
        FieldFaceUpFusionMonsters,
        /// <summary>
        /// EquipModel batch (Tailor of the Fickle): face-up Equip Cards equipped to a monster in either player's Spell
        /// &amp; Trap Zones (Equip Spells, Equip Traps, Unions; not a monster that became an Equip Card by another
        /// effect) for which another face-up monster on the field is a correct target (Lua s.ecfilter). Text path only:
        /// CollectTargets via <see cref="EquipModelRuntime.SwitchCandidates"/> (picker kind SpellTrapOnField); the legacy
        /// SpellTrapEffects.MapCompiledTargetKind mirror leaves it None, so the text path picks the target.
        /// </summary>
        FieldEquipCardsWithAnotherCorrectTarget,
        /// <summary>
        /// EquipAndSpellProtection batch (Magic Reflector): face-up Spell Cards the controller controls in their Spell
        /// &amp; Trap Zones and Field Zone, never the activating card itself (Lua s.filter IsFaceup() and IsSpell(),
        /// LOCATION_SZONE, controller tp, except c). Text path only: CollectTargets via
        /// <see cref="EquipAndSpellProtectionRuntime.CollectControllerFaceUpSpells"/> (picker kind SpellTrapOnField); the
        /// legacy SpellTrapEffects.MapCompiledTargetKind mirror leaves it None, so the text path picks the target.
        /// </summary>
        ControllerFaceUpSpellsExceptThis,
        /// <summary>
        /// NegationMonsters batch (v161) — Double Snare: a face-up card on either field that has an effect that negates
        /// Trap Cards' effects (ygopro aux.DoubleSnareValidity), never the activating card. Derived from the card's
        /// compiled clause kinds (<see cref="NegationMonstersRuntime.NegatesTrapEffects"/>). Text path only: CollectTargets
        /// (picker kind AnyCardOnField); the legacy SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        FieldFaceUpCardsNegatingTrapEffects,
        /// <summary>
        /// PerCopyDeck batch (v163): a Spell Card in the opponent's GY (Graverobber). Text target path only
        /// (<see cref="SpellTrapEffects"/> routes it there through UsesCompiledActivationPending).
        /// </summary>
        OppGySpells,
        /// <summary>
        /// SimpleTargetUtility batch (v173): Equip Spells in the controller's GY (Fairy of the Spring).
        /// </summary>
        ControllerGyEquipSpells,
        /// <summary>
        /// SimpleTargetUtility batch (v173): any card in the controller's main Deck — filtered by the
        /// clause's TargetSeriesName / ExceptNamedCard in CollectTargets (Arsenal Summoner).
        /// </summary>
        ControllerDeckCards,
        /// <summary>
        /// ChoiceBullets batch (v173): face-up Equip Cards on the field currently equipped to a monster (either
        /// player's Spell &amp; Trap Zones; Lua LOCATION_SZONE + TYPE_EQUIP + GetEquipTarget()). Text path only:
        /// CollectTargets (picker kind SpellTrapOnField); the legacy SpellTrapEffects.MapCompiledTargetKind mirror
        /// leaves it None.
        /// </summary>
        FieldEquipCards,
        /// <summary>
        /// MfcSimpleSpellsB batch (v174) — Emblem of Dragon Destroyer: a monster in the controller's Deck
        /// or GY whose printed name matches <see cref="EffectClause.NamedCard"/> (Deck first, then GY).
        /// Text target path only: CollectTargets (picker kind MonsterInYourDeckFiltered); the legacy
        /// SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        ControllerDeckOrGyNamed,
        /// <summary>
        /// SpellCounterSpenders batch — "1 face-up card on the field that you can place a Spell Counter on" (Lua
        /// IsFaceup + IsCanAddCounter(COUNTER_SPELL,1), LOCATION_ONFIELD on both sides; with
        /// <see cref="EffectClause.ExceptThisCard"/>: "1 other …"). Text target path only (picker kind AnyCardOnField);
        /// the legacy SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        FieldFaceUpSpellCounterHolders,
        /// <summary>
        /// SummonLocksAndStatRules batch — Amazoness Spellcaster: a face-up monster the controller controls whose name
        /// contains <see cref="EffectClause.NamedCard"/> (an archetype, <see cref="EffectClause.NamedCardIsSeries"/>).
        /// Text target path only: CollectTargets (picker kind YourMonster); the legacy
        /// SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        ControllerFaceUpSeriesMonsters,
        /// <summary>
        /// Big Wave Small Wave: every face-up monster the controller controls whose live Attribute is
        /// <see cref="EffectClause.AttributeFilter"/> (the printed "all face-up WATER monsters you control"). A mass
        /// destroy only — no target, so the legacy MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        ControllerFaceUpAttributeMonsters,
        /// <summary>
        /// SpellEventsAndHandSendTriggers batch — Fiend's Hand Mirror: another Spell/Trap Card on the field that would be an
        /// appropriate target for the answered Spell Card's Chain Link (ygopro Duel.CheckChainTarget), not its current target.
        /// See <see cref="RedirectAndReplaceRuntime.Candidates"/>. Text target path only (picker kind SpellTrapOnField); the
        /// legacy SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        AnotherAppropriateSpellTrapTarget,
        /// <summary>
        /// TokensDeclarationsAndPicks batch — DNA Transplant: the monster Attributes a player may declare (one declaration
        /// token per Attribute, DIVINE included: Lua ATTRIBUTE_ALL). Picker kind DeclareAttribute in both target mirrors.
        /// </summary>
        DeclaredAttributes,
        /// <summary>
        /// ChaosBanishSummons batch — Primal Seed: any card (face-up or face-down) in the controller's banished pile (Lua
        /// LOCATION_REMOVED, Card.IsAbleToHand). Text target path only (picker kind CardInYourBanished); the legacy
        /// SpellTrapEffects.MapCompiledTargetKind mirror leaves it None.
        /// </summary>
        ControllerBanishedCards,
    }

    /// <summary>One parsed clause from official card text.</summary>
    [Serializable]
    public class EffectClause
    {
        public EffectTiming Timing;
        public EffectActionKind Action;
        public EffectSide Side = EffectSide.Opponent;
        public EffectZoneFilter Zone = EffectZoneFilter.None;
        /// <summary>Numeric parameter (draw count, ATK threshold, turn count, …).</summary>
        public int Amount;
        /// <summary>True if the player must choose a target.</summary>
        public bool RequiresTargetChoice;
        /// <summary>True only when the PSCT effect actually targets the selected card; distinct from UI choice.</summary>
        public bool IsPsctTarget;
        /// <summary>Lord of D. on field required to activate/resolve.</summary>
        public bool RequiresLordOfDOnField;
        /// <summary>Activation only during opponent's turn.</summary>
        public bool OpponentTurnOnly;
        /// <summary>Card remains on field after activation (continuous).</summary>
        public bool StaysOnField;
        /// <summary>Source snippet that produced this clause (audit).</summary>
        public string SourceSnippet;
        /// <summary>Attribute filter for continuous stat/level mods (e.g. WATER). Empty = all.</summary>
        public string AttributeFilter;
        /// <summary>Type/race filter (Warrior, Aqua, …). Empty = all. Used when the text is not an Attribute.</summary>
        public string RaceFilter;
        /// <summary>Name this card is always treated as (AlwaysTreatedAsName).</summary>
        public string TreatedAsName;
        /// <summary>DEF change for ContinuousGainAtkDef. 0 = ATK-only (Star Boy). ALO sets this equal to Amount.</summary>
        public int DefAmount;
        /// <summary>ContinuousReduceLevel also applies in both players' hands.</summary>
        public bool ApplyToHand;
        /// <summary>ContinuousReduceLevel also applies to face-up monsters on the field.</summary>
        public bool ApplyToField;
        /// <summary>Ignition: only once per turn while this copy is face-up.</summary>
        public bool OncePerTurn;
        /// <summary>Must discard a monster (optionally of DiscardCostAttribute) as cost.</summary>
        public bool RequiresDiscardCost;
        /// <summary>Discard-cost attribute filter (WATER, etc.). Empty = any monster. "*" = any card.</summary>
        public string DiscardCostAttribute;
        /// <summary>
        /// How many cards the discard cost requires (0/1 = one). Final Destiny = 5, Darkness Approaches = 2.
        /// Shared by both lineages (same name, same meaning): with <see cref="RequiresDiscardCost"/> the local
        /// discard-then-target flow pays it; without, the GitHub multi-discard flow does.
        /// </summary>
        public int DiscardCostCount;
        /// <summary>
        /// <see cref="EffectActionKind.DrawThenDiscard"/>: cards discarded at resolution after the draw (Graceful
        /// Charity = 2). Not a cost: <see cref="DiscardCostCount"/> is the cost count.
        /// </summary>
        public int ThenDiscardCount;
        /// <summary>
        /// Cost: send 1 face-up card you control whose rules name is RequiresFaceUpName
        /// to the GY (Daedalus / A Legendary Ocean as "Umi"). Not Maiden environment.
        /// </summary>
        public bool RequiresSendNamedToGy;
        /// <summary>Cost: Tribute this card.</summary>
        public bool RequiresTributeThis;
        /// <summary>Cost: Tribute this many monsters you control (0 = none).</summary>
        public int RequiresTributeCount;
        /// <summary>True if the tribute count is "up to N" (minimum 1).</summary>
        public bool TributeUpTo;
        /// <summary>Tribute monsters must match this race (Pyro, Gravekeeper's via NamedCard on cost).</summary>
        public string TributeRaceFilter;
        /// <summary>Tribute except this card (Cannonholder).</summary>
        public bool TributeExceptThis;
        /// <summary>Cost: discard this card from the hand (Thunder Dragon, King of the Swamp).</summary>
        public bool RequiresDiscardSelf;
        /// <summary>True when this ignition activates from the hand, not the field.</summary>
        public bool ActivatesFromHand;
        /// <summary>Cost: send this card from the field to the GY.</summary>
        public bool RequiresSendThisToGy;
        /// <summary>Cost: send 1 monster from the hand to the GY.</summary>
        public bool RequiresSendHandToGy;
        /// <summary>Cost: send 1 other face-up monster you control to the GY.</summary>
        public bool RequiresSendOtherYouControl;
        /// <summary>
        /// Cost: send 1 monster of <see cref="RaceFilter"/> from your Deck to the GY
        /// (Rock Bombardment). Not a mill of the top card, not a send from the hand or field.
        /// </summary>
        public bool RequiresSendRaceFromDeck;
        /// <summary>Pay this many LP as cost (0 = none). Distinct from Bark's multiples picker.</summary>
        public int PayLpAmount;
        /// <summary>Banish this many cards from your GY as cost (0 = none).</summary>
        public int BanishFromGyCount;
        /// <summary>Banish "up to" BanishFromGyCount.</summary>
        public bool BanishFromGyUpTo;
        /// <summary>Quoted name for SpecialSummonNamed / AddNamedFromDeckToHand / tribute name filter.</summary>
        public string NamedCard;
        /// <summary>
        /// Target cannot have this printed / rules name
        /// (Lord Poison: except "Lord Poison"). Distinct from TributeExceptThis.
        /// </summary>
        public string ExceptNamedCard;
        /// <summary>
        /// Rite of Spirit: this card's activation and effect ignore GY locks from
        /// a face-up card named UnaffectedByNamedCard (Necrovalley).
        /// </summary>
        public string UnaffectedByNamedCard;
        /// <summary>SpecialSummonNamed may use the hand.</summary>
        public bool FromHand;
        /// <summary>SpecialSummonNamed / add may use the Deck.</summary>
        public bool FromDeck;
        /// <summary>SpecialSummonNamed may use the GY.</summary>
        public bool FromGrave;
        /// <summary>
        /// RitualSummon: tributes must equal the Level exactly (AddProcEqual) — "...exactly equal the
        /// Level of the Ritual Monster..." (Earth Chant / Contract with the Abyss).
        /// False = Greater (sum &gt;= Level / Amount). Shared by both lineages (same name, same meaning).
        /// </summary>
        public bool RitualExactLevel;
        /// <summary>Targets must have ATK ≤ the numeric cost just paid (Armed Dragon).</summary>
        public bool RequiresAtkLeqCost;
        /// <summary>Tribute/cost monster must have ATK ≥ Amount (Deck Devastation Virus).</summary>
        public bool RequiresAtkGeqCost;
        /// <summary>GainThisAtkUntilEnd: Amount is per paid cost card (Bazoo, Gaia Soul).</summary>
        public bool ScaleAmountByCostCount;
        /// <summary>Remove this many Spell Counters from this card as cost.</summary>
        public int RequiresRemoveSpellCounters;
        /// <summary>This card must have at least this many Spell Counters (Skilled Magician tribute).</summary>
        public int RequiresSpellCounters;
        /// <summary>Max Spell Counters this card can hold (0 = unlimited).</summary>
        public int CounterMax;
        /// <summary>Place a Spell Counter on this card each time a Spell resolves.</summary>
        public bool PlaceCounterOnSpellActivate;
        /// <summary>Coins to toss (Barrel Dragon = 3).</summary>
        public int CoinCount;
        /// <summary>Zorc: destroy all opp if roll ≤ DieLowMax.</summary>
        public int DieLowMax;
        /// <summary>Zorc: destroy 1 opp if roll ≤ DieMidMax (and &gt; DieLowMax).</summary>
        public int DieMidMax;
        /// <summary>
        /// DieRollNegateWhenTargeted: bitmask of six-sided faces that negate — bit
        /// (face-1) set means that face triggers the negate+destroy (e.g. faces {1,3,6}
        /// → 0b100101 = 37; {2,5} → 0b010010 = 18; {3} → 0b000100 = 4).
        /// </summary>
        public int DieNegateFacesMask;
        /// <summary>
        /// DieRollNegateWhenTargeted: protection covers any "Archfiend" monster the
        /// controller controls (Darkbishop), not just this card.
        /// </summary>
        public bool ProtectsAllArchfiendsOnField;
        /// <summary>
        /// Battle-Scarred: while this card is linked to the chosen monster, the opponent
        /// pays the same Standby-Phase maintenance LP the controller pays for it.
        /// </summary>
        public bool MirrorStandbyPaymentToOpponent;
        /// <summary>Archfiend's Roar: the Special Summoned monster cannot be Tributed.</summary>
        public bool SummonCannotBeTributed;
        /// <summary>Archfiend's Roar: destroy the Special Summoned monster in the End Phase.</summary>
        public bool SummonDestroyAtEndPhase;
        /// <summary>
        /// Restrict targets to a card "series" (archetype / treated-as name), e.g. the
        /// "Archfiend" GY target of Archfiend's Roar. Applied as a post-filter.
        /// </summary>
        public string TargetSeriesName;
        public string TokenName;
        public string TokenRace;
        public string TokenAttribute;
        public int TokenLevel;
        public int TokenAtk;
        public int TokenDef;
        public int TokenCount = 1;
        public bool TokenToOpponent;
        public bool TokenCannotTribute;
        /// <summary>Fill every empty Main Monster Zone (Multiply "as many as possible").</summary>
        public bool TokenFillRemainingZones;
        public int TokenDestroyedDamage;
        /// <summary>
        /// <see cref="EffectActionKind.DestroyTokensInflictPer"/>: Amount × destroyed Tokens is LP the
        /// controller gains (Token Thanksgiving), not damage to the opponent (Token Feastevil).
        /// </summary>
        public bool TokenDestroyGainsLp;
        /// <summary>Cocoon of Evolution: host original ATK/DEF become this card's printed stats.</summary>
        public bool ReplaceHostOriginalAtkDef;
        /// <summary>Only legal in the Battle Phase (Magical Hats).</summary>
        public bool RequiresBattlePhase;
        /// <summary>Magical Hats anime: spawn Token hats instead of Deck Spell/Traps.</summary>
        public bool HatsAreTokens;
        /// <summary>Magical Hats anime: spawn this many hat tokens (4).</summary>
        public int HatTokenCount;
        /// <summary>Modern Crush Card: opponent may destroy up to 3 matching monsters in Deck.</summary>
        public bool CrushCardDeckDestroyUpTo;
        /// <summary>Lingering draw-check for this many of the opponent's End Phases (DDV / pre-errata Crush).</summary>
        public int CrushCardLingerOpponentEnds;
        /// <summary>Modern Crush Card: no damage until end of next turn.</summary>
        public bool CrushCardNoDamageUntilNextTurn;
        /// <summary>Destroy monsters at this ATK threshold (default 1500).</summary>
        public int VirusDestroyAtk = 1500;
        /// <summary>True: destroy ATK ≤ threshold (Deck Devastation). False: ATK ≥ (Crush Card).</summary>
        public bool VirusDestroyAtkLeq;
        /// <summary>Union: equip only to a monster named NamedCard / EquipHostName.</summary>
        public string EquipHostName;
        /// <summary>Second printed name for SpecialSummonNamed (Elegant Egotist "X" or "Y").</summary>
        public string AltNamedCard;
        /// <summary>
        /// Continuous: this card gains Amount ATK / DefAmount DEF for each matching
        /// face-up monster counted on the field (Machine King / Amazoness Paladin).
        /// </summary>
        public bool ScaleThisAtkByMatchingCount;
        /// <summary>
        /// With ScaleThisAtkByMatchingCount: count the controller's HAND cards instead of
        /// field monsters (Muka Muka / Flash Assailant "for each card in your hand").
        /// </summary>
        public bool ScaleCountHandCards;
        /// <summary>
        /// With ScaleThisAtkByMatchingCount: count ONLY monsters in the controller's GY
        /// (Shadow Ghoul / Mudora "for each monster in your Graveyard"); the field is
        /// not counted. Distinct from ScaleCountIncludesGraveyard, which adds GY to field.
        /// </summary>
        public bool ScaleCountGraveyardOnly;
        /// <summary>Dark Paladin: matching count includes GY copies as well as the field.</summary>
        public bool ScaleCountIncludesGraveyard;

        /// <summary>
        /// Maha Vailo: ContinuousGainAtkDef Amount applies to this card only,
        /// scaled by Equips.Count (Equip Cards equipped to this card).
        /// Unequipped → 0. Does NOT unlock ExtraAttacks-per-Equip (Ben Kei).
        /// </summary>
        public bool ScaleThisAtkByEquippedEquipCount;
        /// <summary>Destroy: choose among face-up monsters with the lowest ATK (Fissure; ties offered).</summary>
        public bool PickLowestAtk;
        /// <summary>Destroy: choose among face-up monsters with the highest DEF (Smashing Ground; ties offered).</summary>
        public bool PickHighestDef;
        /// <summary>Destroy: choose among face-up monsters with the highest ATK (Hammer Shot; ties offered).</summary>
        public bool PickHighestAtk;
        /// <summary>Target must be in Attack Position (Hammer Shot).</summary>
        public bool RequiresAttackPosition;
        /// <summary>ChangeBattlePosition: set to face-up Attack (Stop Defense), do not toggle.</summary>
        public bool ForceAttackPosition;
        /// <summary>ChangeBattlePosition: set to face-up Defense (Block Attack), do not toggle.</summary>
        public bool ForceDefensePosition;
        /// <summary>Destroy only face-up Continuous Spells (Spell Purification).</summary>
        public bool FaceUpContinuousSpellsOnly;
        /// <summary>Destroy only face-up Continuous Traps.</summary>
        public bool FaceUpContinuousTrapsOnly;
        /// <summary>Falling Down / Snatch Steal: take control of the equipped target.</summary>
        public bool TakeControlOfTarget;
        public int EquipAtkBonus;
        /// <summary>DEF change while equipped (Steel Shell −200). Independent of EquipAtkBonus.</summary>
        public int EquipDefBonus;
        /// <summary>UnaffectedByCardEffects: Spell, Trap, Monster, SpellTrap, or all.</summary>
        public string UnaffectedByFilter;
        /// <summary>CannotBeAttackTarget: opponent may still attack directly (Fisherman).</summary>
        public bool AllowsDirectAttackWhileProtected;
        /// <summary>
        /// Extra Amount for each copy of NamedCard (or this card) already in your GY
        /// (Numinous Healer +500 each). Counted at resolution, before this card is sent.
        /// </summary>
        public int ExtraAmountPerCopyInGy;
        /// <summary>Phase trigger only if this card destroyed a monster by battle this turn.</summary>
        public bool RequiresDestroyedByBattleThisTurn;
        /// <summary>
        /// LvChain batch — <see cref="DrawAmountWhenSpecialSummonedByNamed"/>: the draw count when this card was
        /// Special Summoned by the effect of <see cref="SpecialSummonedByNamedCard"/> (Dark Mimic LV3: 1, or 2
        /// when it was Special Summoned by "Dark Mimic LV1").
        /// </summary>
        public int AmountIfSpecialSummonedByNamed;
        /// <summary>LvChain batch — the card whose effect may have Special Summoned this one (Dark Mimic LV1).</summary>
        public string SpecialSummonedByNamedCard;
        /// <summary>Phase trigger only if this copy is in the GY (Darklord Marie).</summary>
        public bool ResolvesFromGy;
        /// <summary>Cure Mermaid family: this copy must be face-up Attack Position.</summary>
        public bool RequiresThisAttackPosition;
        /// <summary>Dancing Fairy family: this copy must be face-up Defense Position.</summary>
        public bool RequiresThisDefensePosition;
        /// <summary>Vampire Lord revival: this copy must have been destroyed by an opponent's card effect.</summary>
        public bool RequiresThisDestroyedByOpponent;
        /// <summary>Pikeru / United We Stand: Amount is per face-up monster you control.</summary>
        public bool ScaleAmountByControllerMonsters;
        /// <summary>Gift of The Mystical Elf: Amount is per monster on the field.</summary>
        public bool ScaleAmountByFieldMonsters;
        /// <summary>
        /// Senju / Sonic Bird / Manju: fires on a Normal OR Flip Summon of this card,
        /// but not on a Special Summon. Distinct from RequiresThisNormalSummoned,
        /// which also excludes the Flip Summon.
        /// </summary>
        public bool RequiresThisNormalOrFlipSummoned;
        /// <summary>
        /// Start of Damage Step: the battled monster must be a face-down Defense
        /// Position monster (Sasuke Samurai / Mystic Swordsman / Paladin).
        /// </summary>
        public bool RequiresTargetFaceDownDefense;
        /// <summary>
        /// Start of Damage Step: the battled monster must be a face-up Defense
        /// Position monster (Ninja Grandmaster Sasuke).
        /// </summary>
        public bool RequiresTargetFaceUpDefense;
        /// <summary>
        /// Absorbing Kid / KA-2 Des Scissors: Amount is per Level of the battle-destroyed
        /// monster passed in as the clause target.
        /// </summary>
        public bool ScaleAmountByTargetLevel;
        /// <summary>
        /// Guardian Angel Joan: the amount is the target's printed original ATK, not Amount.
        /// </summary>
        public bool AmountFromTargetOriginalAtk;
        /// <summary>
        /// Inferno Fire Blast: on an <see cref="EffectActionKind.InflictDamageToOpponent"/> that damages the
        /// opponent, the clause target cannot attack for the rest of this turn
        /// (<see cref="CardInstance.CannotAttackDuringTurnNumber"/>).
        /// </summary>
        public bool TargetCannotAttackThisTurn;
        /// <summary>Mage Power: Equip ATK/DEF is per Spell/Trap you control (including this card).</summary>
        public bool ScaleAmountByControllerSpellTraps;
        /// <summary>Spirit: return to hand if Normal Summoned or flipped this turn.</summary>
        public bool RequiresSummonedOrFlippedThisTurn;
        /// <summary>Des Lacooda: "When this card is Flip Summoned" — not NS/SS or battle flip.</summary>
        public bool RequiresThisFlipSummoned;
        /// <summary>Vampiric Orchis family: "When this card is Normal Summoned" — not FS/SS.</summary>
        public bool RequiresThisNormalSummoned;
        /// <summary>Monarch family: "If this card is Tribute Summoned" — not a tribute-less NS/FS/SS.</summary>
        public bool RequiresThisTributeSummoned;
        /// <summary>
        /// Ectoplasmer: the turn player tributes (not the card's controller).
        /// Damage goes to that player's opponent.
        /// </summary>
        public bool TurnPlayerTributes;
        /// <summary>Tribute cost/effect only counts face-up monsters.</summary>
        public bool TributeFaceUpOnly;
        /// <summary>
        /// Phase trigger subject is the turn player (Labyrinth positions, Burning Land damage),
        /// not the card's controller.
        /// </summary>
        public bool TurnPlayerIsSubject;
        /// <summary>Call of the Haunted: when this card leaves the field, destroy the summoned monster.</summary>
        public bool DestroyHostWhenThisLeaves;
        /// <summary>Spellbinding Circle family: destroy this card only when its bound host is destroyed.</summary>
        public bool DestroyThisWhenBoundHostDestroyed;
        /// <summary>
        /// Spellbinding Circle family: also lock battle position on the bound host
        /// (Cannot attack or change battle position).
        /// </summary>
        public bool AlsoCannotChangeBattlePosition;
        /// <summary>
        /// Two-target activation (Book of Life): after the first pick, collect SecondZone.
        /// </summary>
        public bool RequiresSecondTarget;
        public EffectZoneFilter SecondZone;
        public EffectActionKind SecondAction;
        /// <summary>Two-Pronged Attack: this many of your monsters (then OpponentTargetCount of theirs).</summary>
        public int ControllerTargetCount;
        /// <summary>Two-Pronged Attack: this many of the opponent's monsters after yours.</summary>
        public int OpponentTargetCount;
        /// <summary>Armed Ninja / Reaper: after revealing a Set card, destroy only if Spell or Trap.</summary>
        public string DestroyIfType;
        /// <summary>
        /// CannotBeAttackTarget on OTHER matching monsters, not this card
        /// (Marauding Captain: Warriors except this one).
        /// </summary>
        public bool ExceptThisCard;
        /// <summary>Special Summon in Defense Position (Soul Resurrection).</summary>
        public bool SummonInDefense;
        /// <summary>Special Summon in Attack Position (Call of the Haunted / Premature Burial).</summary>
        public bool SummonInAttack;
        /// <summary>
        /// First chosen target is Tributed (sent to GY), not destroyed
        /// (Order to Charge: Tribute that Normal Monster, then destroy 1 opp monster).
        /// </summary>
        public bool TributeChosenTarget;
        /// <summary>Special Summon selection may use fewer than Amount cards ("up to"/any number).</summary>
        public bool SpecialSummonUpTo;
        /// <summary>GY target must be a Normal Monster.</summary>
        public bool RequiresNormalMonster;
        /// <summary>ContinuousCannotAttack: Amount is a printed Level (Gravity Bind), not ATK.</summary>
        public bool AmountIsLevel;

        /// <summary>
        /// Beast Soul Swap SS rider: hand monster Level must equal the prior clause target
        /// (the monster returned to the hand). Exact match — not AmountIsLevel (≤).
        /// </summary>
        public bool MatchPriorTargetLevel;

        /// <summary>
        /// Freed family: target monster CurrentAtk must be strictly greater than this card's
        /// CurrentAtk. Also requires the target face-up. Not RequiresAtkLeqCost (≤ cost ATK).
        /// </summary>
        public bool RequiresHigherAtkThanSource;

        /// <summary>
        /// Shadowslayer / Black Tyranno / Rocket Jumper: CanAttackDirectly only while every
        /// monster the opponent controls is in Defense Position (face-down counts as DEF).
        /// Not an ATK-gate (Drillago) and not empty-field-only text.
        /// </summary>
        public bool RequiresAllOpponentMonstersDefense;
        /// <summary>
        /// ThisCardInflictsBattleDamage: only if the battle had no monster target
        /// ("by a direct attack"). White Magical Hat leaves this false.
        /// </summary>
        public bool RequiresDirectAttack;
        /// <summary>Suijin: this card must be the current attack target.</summary>
        public bool RequiresThisIsAttackTarget;
        /// <summary>
        /// DamageCalculation: controller must be about to take at least this much battle damage
        /// (Nutrient Z: 2000+). 0 = no threshold. Distinct from AfterDamageCalculation /
        /// YouTakeLifePointDamage / heal-equal-to-damage riders.
        /// </summary>
        public int RequiresBattleDamageAtLeast;
        /// <summary>
        /// EndOfDamageStep: opponent's monster that battled this card must still be on the field
        /// (Hyper Hammerhead).
        /// </summary>
        public bool RequiresBattledMonsterNotDestroyed;
        /// <summary>
        /// Resolution also banishes this card (D.D. Warrior Lady / D.D. Assailant
        /// "banish that monster, also banish this card").
        /// </summary>
        public bool AlsoBanishThis;
        /// <summary>Suijin: once while this copy remains face-up.</summary>
        public bool OnceWhileFaceUp;
        /// <summary>Konami PSCT: this sentence uses ":" or ";" and therefore makes a Chain Link.</summary>
        public bool MakesChainLink;
        /// <summary>Monster Quick Effect (Speed 2). " (Quick Effect)" or "during either player's".</summary>
        public bool IsQuickEffect;
        /// <summary>Optional "You can" activation.</summary>
        public bool IsOptional;
        /// <summary>
        /// Continuous condition: a face-up card with this rules name must be on the field
        /// (Amphibious Bugroth MK-3: "Umi"). Empty = unconditional direct attack.
        /// </summary>
        public string RequiresFaceUpName;
        /// <summary>Falling Down: YOU must control the named card, not either field.</summary>
        public bool RequiresControllerNamedCard;
        /// <summary>"an Archfiend card": name / treated-as / archetype contains the quoted string.</summary>
        public bool NamedCardIsSeries;
        /// <summary>Pay LP in this multiple as cost (Bark of Dark Ruler = 100). 0 = none.</summary>
        public int RequiresLpCostMultiple;
        /// <summary>GY trigger only if the card was destroyed (not tributed / discarded).</summary>
        public bool RequiresDestroyed;
        /// <summary>
        /// Field→GY trigger only if this copy was destroyed by battle
        /// (Yomi Ship / Newdoria / Slate Warrior destroyer-loss). Distinct from
        /// <see cref="RequiresDestroyedByBattleThisTurn"/> (this card destroyed a monster).
        /// </summary>
        public bool RequiresThisDestroyedByBattle;
        /// <summary>Field→GY trigger only if this copy was NOT destroyed by battle (Mecha-Dog Marron "except by battle").</summary>
        public bool RequiresNotDestroyedByBattle;
        /// <summary>Resolution subject is the monster that destroyed this card (no targeting).</summary>
        public bool ImplicitTargetIsBattleDestroyer;
        /// <summary>Standby GY SS only if this copy was sent by a Continuous Spell effect.</summary>
        public bool RequiresSentByContinuousSpell;
        /// <summary>
        /// Standby trigger is the controller's next Standby after SentFromFieldTurnNumber
        /// (Malice Doll of Demise).
        /// </summary>
        public bool RequiresNextControllerStandby;
        /// <summary>
        /// EmptyHandStandbyDraw: a controller's Standby Phase trigger that applies only "if you controlled this
        /// face-up card and had no cards in your hand at the end of the opponent's last End Phase" (The Agent of
        /// Wisdom - Mercury). Checked by <see cref="EmptyHandStandbyDrawRuntime.ControlledFaceUpWithEmptyHand"/>.
        /// </summary>
        public bool RequiresEmptyHandAtOpponentsLastEndPhase;
        /// <summary>
        /// BanishedReturn: the card must be face-up in its owner's banished pile and have been banished during
        /// this turn ("if this card is currently banished, and was banished this turn", D.D. Scout Plane).
        /// Checked by <see cref="BanishedReturnRuntime.Qualifies"/> against <see cref="CardInstance.BanishedTurnNumber"/>.
        /// </summary>
        public bool RequiresBanishedThisTurn;
        /// <summary>
        /// Destroy sends the card to the Banished pile (skip GY). Bottomless Trap Hole:
        /// "Destroy … and if you do, banish it."
        /// </summary>
        public bool BanishIfDestroyed;
        /// <summary>Also answers Special Summon (Bottomless / Adhesion / Torrential). Trap Hole does not.</summary>
        public bool AnswersSpecialSummon;
        /// <summary>Also answers the controller's own summon (Torrential). Default is opponent only.</summary>
        public bool AnswersControllerSummon;
        /// <summary>Amount is an ATK ceiling (Eatgaboon), not a floor.</summary>
        public bool AmountIsAtkMax;
        /// <summary>Amount is a DEF ceiling (House of Adhesive Tape).</summary>
        public bool AmountIsDefMax;

        /// <summary>
        /// Sangan / Witch of the Black Forest: after this AddFromDeckToHand resolves,
        /// controller cannot activate cards or effects with the added card's name
        /// for the rest of this turn. Other searchers must leave this false.
        /// </summary>
        public bool LocksAddedCardNameThisTurn;
        /// <summary>
        /// SimpleTargetUtility batch (v173) — Fairy of the Spring: the card this clause just added from
        /// the GY (or wherever the add resolved) cannot be activated for the rest of this turn; stamped on
        /// the instance (<c>CardInstance.ActivationBlockedUntilTurn</c>) when the add resolves.
        /// </summary>
        public bool LocksAddedCardActivationThisTurn;
        /// <summary>Token Feastevil: the summoned card must be a Token.</summary>
        public bool RequiresSummonedIsToken;
        /// <summary>Mispolymerization: the summoned card must be a Fusion Monster.</summary>
        public bool RequiresSummonedIsFusion;
        /// <summary>Blast Held by a Tribute: the attacker was Tribute Summoned.</summary>
        public bool RequiresAttackerTributeSummoned;
        /// <summary>Master spec §8: condition re-check (when vs if).</summary>
        public ConditionCheckedAt CheckedAt = ConditionCheckedAt.Both;
        /// <summary>Master spec §7. None unless OncePerTurn is set.</summary>
        public OncePerTurnScope OptScope = OncePerTurnScope.None;
        /// True when this clause negates the current activation link.
        public bool ChainResponseOnly;
        /// Response effect destroys the activated card if negation succeeds.
        public bool DestroyNegatedCard;
        /// Answers a Spell Card activation (Magic Jammer, Dark Paladin, Horus LV8, Gardna).
        public bool NegateRespondsToSpell;
        /// Answers a Trap Card activation (Seven Tools of the Bandit).
        public bool NegateRespondsToTrap;
        /// Big Shield Gardna: flip this monster face-up Defense on resolution.
        public bool FlipSelfFaceUpDefense;
        public bool ActivationNegatable = true;
        public bool EffectNegatable = true;
        /// <summary>
        /// ApplyLingeringAtkDef changes the monster this Equip Spell is equipped to
        /// (Stim-Pack / Germ Infection standby), not the Spell card.
        /// EquipLocksAndLeaveField batch: on <see cref="EffectActionKind.ContinuousReduceLevel"/>, the Level change
        /// applies only to the monster this Equip Card is equipped to (Demotion, EFFECT_TYPE_EQUIP + EFFECT_UPDATE_LEVEL).
        /// </summary>
        public bool AppliesToEquippedHost;
        /// <summary>
        /// ContinuousCannotAttack / equipped standby: skip when the host's race
        /// contains this word (Paralyzing Potion / Germ Infection "non Machine-Type").
        /// </summary>
        public string ExcludeRaceFilter;
        /// <summary>
        /// ThisCardInflictsBattleDamage on a face-up card that is not the attacker
        /// (Robbin' Goblin: each time a monster you control inflicts battle damage).
        /// </summary>
        public bool AnyControllerMonsterBattleDamage;
        /// <summary>
        /// DamageStepAttributeAtk applies only when this card is the attacker
        /// (Insect Soldiers). False means either side of the battle (Penumbral / Luminous).
        /// </summary>
        public bool DamageStepOnlyWhenAttacking;
        /// <summary>PositionChanged: the card was in Attack Position.</summary>
        public bool PositionFromAttack;
        /// <summary>PositionChanged: the card moved to Defense Position.</summary>
        public bool PositionToDefense;
        /// <summary>PositionChanged: the card was in Defense Position.</summary>
        public bool PositionFromDefense;
        /// <summary>PositionChanged: the card moved to Attack Position.</summary>
        public bool PositionToAttack;
        /// <summary>PositionChanged: the destination must be face-up (Dream Clown, Tainted Wisdom).</summary>
        public bool RequireDestinationFaceUp;
        /// <summary>PositionChanged also fires on a Flip Summon (Crass Clown). Default does not.</summary>
        public bool FiresOnFlipSummon;
        /// <summary>Ignition may be activated only during the controller's Standby Phase.</summary>
        public bool OnlyDuringOwnStandby;
        /// <summary>
        /// Ignition limited to its controller's Main Phase 1: "Once per turn, during your Main Phase 1: …" (Gear Golem
        /// the Moving Fortress). Checked by TextEffectRuntime.CanActivate; see <see cref="PsctConditionGate"/>.
        /// </summary>
        public bool OnlyDuringMainPhase1;
        /// <summary>Mass position change also turns face-down monsters face-up (Desert Sunlight).</summary>
        public bool AlsoFlipFaceDown;

        /// <summary>
        /// Standby / End Phase trigger fires during <b>either</b> player's phase (ygopro EFFECT_FLAG_BOTH_SIDE) —
        /// "During each player's End Phase …" (Two-Man Cell Battle). The ordinary rule is
        /// <see cref="OpponentTurnOnly"/> (only the other player's phase); this one clears the check in
        /// <see cref="TextEffectRuntime"/>'s FirePhaseTriggersFor for both passes.
        /// </summary>
        public bool FiresAtEachPlayersPhase;

        /// <summary>
        /// An exact monster Level for this clause's filter (0: unused). <see cref="AmountIsLevel"/> is a maximum;
        /// this is the printed "Level N" of Two-Man Cell Battle.
        /// </summary>
        public int ExactLevel;

        /// <summary>ThisCardSummoned: fires only when this card was Special Summoned (Sacred Crane).</summary>
        public bool RequiresThisSpecialSummoned;

        /// <summary>
        /// ThisCardSummoned: fires only when this card was Special Summoned from the Graveyard
        /// (Molten Zombie). The engine reports the origin through
        /// <c>DuelEngine.NextSpecialSummonFromGraveyard</c>.
        /// </summary>
        public bool RequiresThisSpecialSummonedFromGy;

        /// <summary>Tribute cost: only Normal Monsters are legal Tributes (Man-Thro' Tro').</summary>
        public bool TributeNormalMonsterOnly;

        /// <summary>Tribute cost: Tokens are not a legal Tribute (Man-Thro' Tro' "(except a Token)").</summary>
        public bool TributeExceptTokens;

        /// <summary>
        /// Target filter: a face-up monster whose current DEF is at most this card's current ATK
        /// (Throwstone Unit). Measured at target selection.
        /// </summary>
        public bool RequiresTargetDefLeqSourceAtk;

        /// <summary>Activation condition: the opponent holds at least N cards (Heavy Slump).</summary>
        public int RequiresOpponentHandAtLeast;

        /// <summary>LoseAtkDefUntilEndOfTurn: the target's DEF becomes 0 until the End Phase (Micro Ray).</summary>
        public bool SetDefToZero;

        /// <summary>Amount / DefAmount are multiplied by the opponent's hand size at resolution (Energy Drain).</summary>
        public bool ScaleAmountByOpponentHandCards;

        /// <summary>Amount is multiplied by the opponent's MonsterCount at resolution (Just Desserts).</summary>
        public bool ScaleAmountByOpponentMonsterCount;

        /// <summary>
        /// Amount is multiplied by (opp HandCount + cards they control) at resolution (Secret Barrel).
        /// Compound only — distinct from hand-only (Des Koala) and monster-only (Just Desserts).
        /// </summary>
        public bool ScaleAmountByOpponentHandAndControlledCards;

        /// <summary>Target filter: face-up cards only.</summary>
        public bool RequiresTargetFaceUp;

        /// <summary>Continuous aura applies only during the controller's own turn (Banner of Courage with RequiresBattlePhase).</summary>
        public bool ControllerTurnOnly;

        /// <summary>PreventControllerBattleDamage applies only while the controller controls a monster (Spirit Barrier).</summary>
        public bool RequiresControllerHasMonster;

        /// <summary>Monster-borne PreventControllerBattleDamage: only battles this card takes part in (Amazoness Fighter).</summary>
        public bool OnlyBattlesInvolvingThis;
        /// <summary>
        /// Targets this clause takes (Two-Pronged Attack: 2 of yours + 1 of theirs).
        /// Values ≤ 1 mean a single target. With <see cref="TargetUpTo"/>, 1..TargetCount.
        /// </summary>
        public int TargetCount = 1;
        /// <summary>"up to N" targets (Gravedigger Ghoul / Soul Release). Cancel ends picking early.</summary>
        public bool TargetUpTo;
        /// <summary>
        /// This targeted clause is its own target group with distinct targets from the
        /// card's other targeted clauses (Two-Pronged Attack: yours + theirs). Without it,
        /// several targeted clauses share the single chosen target (legacy behavior).
        /// </summary>
        public bool DistinctTargetGroup;
        /// <summary>
        /// Spell/Trap target must be this card kind ("Spell" or "Trap"). A Set card is a
        /// legal target; it is revealed on resolution and only destroyed if it matches
        /// (Armed Ninja / Reaper of the Cards).
        /// </summary>
        public string TargetCardKind;
        /// <summary>Only the monster(s) with the lowest ATK are eligible (Fissure).</summary>
        public bool LowestAtkOnly;
        /// <summary>
        /// The player chooses among eligible cards but does not target them (Fissure):
        /// no targeting protections or target-negation apply.
        /// </summary>
        public bool ChoiceDoesNotTarget;
        /// <summary>LoseLifePoints: lose half of the current LP (rounded up).</summary>
        public bool HalveLifePoints;
        /// <summary>
        /// GainSelfAtkPerCount source: "NamedYouControl" (NamedCard), "MonstersInYourGy",
        /// "CardsInYourHand".
        /// InflictDamageToOpponent (PerCountBurns, v141): Amount × the count at resolution —
        /// "OppSpellTrapCards", "OppBanishedCards", "OppGyCards", "PreviousClauseHits", and
        /// "ThisCardTurnCounter" (<see cref="CardInstance.TurnCounter"/>: your Standby Phases since this
        /// card was activated, fixed when the effect is activated) (see <see cref="PerCountBurnsRuntime"/>).
        /// </summary>
        public string CountSource;
        /// <summary>
        /// This clause only applies if the previous clause of the same timing destroyed something
        /// (Dragon Piper; Thunder Crash's "and if you do, inflict … for each monster destroyed").
        /// </summary>
        public bool RequiresPreviousClauseHit;
        /// <summary>
        /// "You can activate 1 of these effects: ● … ● …" (Dark Scorpion - Cliff the Trap Remover / Gorg the
        /// Strong): this clause is bullet N (1-based) of one choice. The bullets of a timing are offered together,
        /// once, by <see cref="EffectChoiceRuntime"/>, and only the chosen one resolves. 0 = not part of a choice.
        /// </summary>
        public int EffectChoiceIndex;
        /// <summary>
        /// Targeted Destroy: if the target is destroyed, then send this many cards from the top of the opponent's
        /// Deck to the GY (Assault on GHQ; Lua: Duel.Destroy(tc)&gt;0, then DiscardDeck). 0 = no mill.
        /// </summary>
        public int ThenMillOpponentDeck;
        /// <summary>
        /// Targeted Destroy: if that target is destroyed, inflict this much damage to its controller
        /// (the controller it had when it left the field). 0 = no damage. "and if you do, inflict N damage
        /// to its controller" — not "to your opponent".
        /// </summary>
        public int DamageItsController;
        /// <summary>
        /// GainLifePoints: the amount is the battle damage just inflicted, not <see cref="Amount"/>.
        /// </summary>
        public bool AmountIsEquippedBattleDamage;
        /// <summary>
        /// YourMonsterInflictsBattleDamage: only when the monster is the one this card is equipped to.
        /// </summary>
        public bool OnlyEquippedMonster;
        /// <summary>
        /// EquipThisToTarget: ATK or DEF was chosen when this card was activated. Only that bonus applies.
        /// Both <see cref="EquipAtkBonus"/> and <see cref="EquipDefBonus"/> hold the printed number.
        /// </summary>
        public bool ChooseAtkOrDef;
        /// <summary>
        /// Draw / DiscardRandomFromHand act on the opponent of the card's controller
        /// (The Bistro Butcher: "your opponent draws"). <see cref="Side"/> keeps its
        /// legacy default, so this is the explicit subject flag.
        /// </summary>
        public bool OpponentIsSubject;
        /// <summary>Targets / attackers of this Type are excluded ("a non Machine-Type monster").</summary>
        public string ExceptRaceFilter;
        /// <summary>ThisCardPositionChanged: true = Attack → face-up Defense, false = Defense → Attack.</summary>
        public bool PositionChangeToDefense;
        /// <summary>EquipAtkDecayPerStandby: count the equipped monster's controller's Standby Phases.</summary>
        public bool DecayOnEquippedControllersStandby;
        /// <summary>Stat aura only affects Defense Position monsters (Chorus of Sanctuary).</summary>
        public bool AffectsDefensePositionOnly;
        /// <summary>ThisCardSummoned: Normal or Flip Summon only, not Special Summon (Senju).</summary>
        public bool RequiresNormalOrFlipSummon;
        /// <summary>GainThisAtkUntilEnd: the gain equals this card's original ATK (Karate Man doubles it).</summary>
        public bool AmountIsOriginalAtk;
        /// <summary>After this effect, destroy this card during the End Phase (Karate Man).</summary>
        public bool DestroyThisAtEndPhase;
        /// <summary>SpecialSummonNamed: summon every copy that fits (Nimble Momonga "any number").</summary>
        public bool SummonAllCopies;
        /// <summary>Special Summon in face-down Defense Position (Nimble Momonga / The Shallow Grave).</summary>
        public bool SummonFaceDown;
        /// <summary>
        /// SpecialSummonFromGy: summon to the GY owner's field, not the activator's
        /// (The Shallow Grave — each player SS their own target). Fail-closed vs Monster Reborn
        /// / Call of the Haunted which summon to the activator.
        /// </summary>
        public bool SummonToTargetsOwner;
        /// <summary>GainLifePoints: Amount per monster on the field, both sides, face-down included (Gift of the Mystical Elf).</summary>
        public bool ScaleAmountByAllFieldMonsters;
        /// <summary>Damage: Amount per face-up Effect Monster on the field, counted at resolution (Ceasefire).</summary>
        public bool ScaleAmountByFaceUpEffectMonsters;
        /// <summary>Phase trigger only if this is the only monster its controller controls (Dark Zebra).</summary>
        public bool RequiresOnlyMonsterYouControl;
        /// <summary>
        /// Activation-only condition (checked when the card is activated, not at resolution):
        /// "AnyMonsterOnField", "OppLpAtMost" (ConditionAmount), "YourGyMonstersAtLeast" (ConditionAmount),
        /// "FaceDownDefOrFaceUpEffect", "YouControlFaceUpRace" (RaceFilter), "FaceUpNamedMonster" (ConditionName),
        /// "OppMonsterLeadAtLeast" (ConditionAmount), "HandHasOtherCard".
        /// PerCountBurns (v141): "OppBanishedAtLeast", "OppGyAtLeast", "YouControlMonstersAtLeast" (ConditionAmount).
        /// TurnNegationTraps (v141): "ChainLinkActivatesCardFamily" (ConditionName = the family).
        /// RedirectAndReplace (v142): "OpponentSpellTargetsOneMonster" (Fairy's Hand Mirror) and
        /// "OpponentTargetsOneMonsterYouControl" (Shift — also an attack on your monster); ConditionName = the
        /// answered card kind, "Spell" or "SpellTrap" (<see cref="RedirectAndReplaceRuntime"/>).
        /// </summary>
        public string ActivationCondition;
        public int ConditionAmount;
        public string ConditionName;
        /// <summary>GY target must be a non-Effect monster (Backup Soldier); includes non-effect Fusions.</summary>
        public bool RequiresNonEffectMonster;
        /// <summary>Target must be owned by the controller, not borrowed (Monster Recovery).</summary>
        public bool RequiresTargetOwnedByController;
        /// <summary>Summon window: the summoned monster was Set face-down (Shadow of Eyes).</summary>
        public bool RequiresSummonedFaceDown;
        /// <summary>Position change does not activate FLIP effects (Shadow of Eyes / Ceasefire).</summary>
        public bool SuppressFlipEffects;
        /// <summary>
        /// With DestroyHostWhenThisLeaves: destroy the linked monster only when this card is DESTROYED,
        /// not when it is bounced or banished (Premature Burial).
        /// </summary>
        public bool DestroyHostOnlyIfThisDestroyed;
        /// <summary>After destroying and banishing a Trap, banish every copy from both Decks (Nobleman of Extermination).</summary>
        public bool PurgeDecksIfTrap;
        /// <summary>SummonNegation batch (v141): cost "Pay half your LP", rounded down (Solemn Judgment).</summary>
        public bool PayHalfLpCost;
        /// <summary>
        /// SummonNegation batch (v141): a chain negation that answers only the activation of a Spell/Trap Card
        /// (from the hand or Set), never an effect of a card that is already face-up (Solemn Judgment, Magic Drain).
        /// </summary>
        public bool NegateCardActivationOnly;
        /// <summary>SummonNegation batch (v141): "When your opponent activates …" — never its controller's own link (Magic Drain).</summary>
        public bool NegateOpponentActivationOnly;
        /// <summary>
        /// SummonNegation batch (v141, Magic Drain): at resolution the player whose activation is answered may
        /// discard 1 Spell Card to negate this effect; otherwise the negation (and destroy) applies.
        /// </summary>
        public bool OpponentMayDiscardSpellToNegateThis;
        /// <summary>
        /// ContinuousNegation batch (v141), on an EquipThisToTarget clause (Amplifier): while this card is
        /// equipped, the equipped monster's Trap lock / Trap negation does not apply to Trap Cards its
        /// controller controls on the field.
        /// </summary>
        public bool EquipSparesControllerTrapsFromHostNegation;
        /// <summary>
        /// ContinuousNegation batch (v141), on a StandbyPhase clause: fires during EVERY Standby Phase
        /// (either player's turn), not only the controller's — "Once per turn, during the Standby Phase"
        /// (Imperial Order), as opposed to "during your Standby Phase" (Messenger of Peace).
        /// </summary>
        public bool EachStandbyPhase;
        /// <summary>
        /// TurnNegationTraps batch (v141): the card family <see cref="EffectActionKind.NegateCardFamilyOnFieldThisTurn"/>
        /// negates — "FieldSpell", "ContinuousSpell" or "ContinuousTrap"
        /// (<see cref="TurnNegationTrapsRuntime"/>). The same names are the <see cref="ConditionName"/> of the
        /// "ChainLinkActivatesCardFamily" <see cref="ActivationCondition"/>.
        /// </summary>
        public string NegatedCardFamily;
        /// <summary>
        /// TakeControl batch (Brain Control): the target must be a monster that can be Normal
        /// Summoned/Set — not a Token, Extra Deck or Ritual Monster, nor one printed with an
        /// unconditional "Cannot be Normal Summoned/Set" (ygopro IsSummonableCard; a conditional
        /// restriction such as Cave Dragon's or Mystic Swordsman LV4's still qualifies).
        /// </summary>
        public bool TargetMustBeNormalSummonable;
        /// <summary>
        /// TakeControl batch (Invader of the Throne): "This effect cannot be activated during the
        /// Battle Phase." A Flip effect with this flag does not activate while the Phase is Battle
        /// (a Set monster flipped by an attack).
        /// </summary>
        public bool CannotActivateInBattlePhase;

        // ── TributeSummons batch (v141): Tribute-then-Summon Spells and their summon locks ──

        /// <summary>
        /// Special Summon pick: the monster's current Level must equal this exactly (Tribute Doll "1 Level 7
        /// monster"). 0 = no check. Distinct from <see cref="AmountIsLevel"/>, which is "Level N or lower".
        /// </summary>
        public int RequiresExactLevel;
        /// <summary>
        /// Special Summon pick: only a monster that can be Normal Summoned/Set — not Extra Deck, Ritual, or a
        /// printed "Cannot be Normal Summoned/Set" / "Must (first) be Special Summoned" monster (Tribute Doll).
        /// </summary>
        public bool RequiresCanBeNormalSummonedOrSet;
        /// <summary>The monster this effect Special Summons cannot attack this turn (Tribute Doll).</summary>
        public bool SummonCannotAttackThisTurn;
        /// <summary>
        /// Insect Imitation: the Special Summoned monster's Level must equal the Tributed (cost) monster's Level
        /// plus <see cref="TributedLevelOffset"/>. The cost records that Level (not its ATK) as the numeric cost,
        /// and only a monster with a matching Deck monster may be Tributed.
        /// </summary>
        public bool MatchTributedLevel;
        /// <summary>With <see cref="MatchTributedLevel"/>: Level difference ("1 higher" = 1).</summary>
        public int TributedLevelOffset;
        /// <summary>
        /// Cost: Tribute the monster this Equip Card is equipped to, which you must control (Magical Labyrinth).
        /// Only usable while this card is equipped; the Equip Card leaves the field with it.
        /// </summary>
        public bool RequiresTributeEquippedMonster;
        /// <summary>
        /// Oath: the controller cannot conduct their Battle Phase the turn they activate this card, so it cannot
        /// be activated once the Battle Phase is over (Main Phase 2) (Soul Exchange).
        /// </summary>
        public bool ForbidsBattlePhaseThisTurn;
        /// <summary>
        /// BattlePhaseEnd batch (v142), Reflect Bounder: "If you do, after damage calculation: Destroy this card."
        /// On a <see cref="EffectTiming.BeforeDamageCalculation"/> burn, destroy this card after that battle's
        /// damage calculation when the burn reduced the opponent's LP.
        /// </summary>
        public bool DestroyThisAfterDamageCalculation;
        /// <summary>
        /// TypeDeclaration batch (v142), ContinuousCannotAttack: the Type filter is the Type this card declared when
        /// it was activated (The Regulation of Tribe: "Monsters of the declared Type cannot attack"), not
        /// <see cref="RaceFilter"/>. Nothing declared yet: no monster is locked.
        /// </summary>
        public bool RaceFilterIsDeclared;

        // ── FlipMassEffects batch (v142): mass Special Summons of revealed / excavated / GY monsters ──

        /// <summary>
        /// "Level N or lower" for the monsters a mass Flip effect Special Summons from the revealed or excavated
        /// cards (Cyber Jar, Morphing Jar #2). 0 = no Level check.
        /// </summary>
        public int SummonLevelMax;
        /// <summary>
        /// "in face-up Attack Position or face-down Defense Position": the summoning player chooses, for each
        /// monster, face-up Attack or face-down Defense (never face-up Defense) (Cyber Jar, Spear Cretin).
        /// </summary>
        public bool SummonFaceUpAttackOrFaceDownDefense;

        // ── StandbyGrowthAndMaintenance batch (v142): ATK/DEF that grows on the controller's Standby Phases ──

        /// <summary>
        /// ATK and DEF added on each counted Standby Phase of the controller ("gains 200 more ATK and DEF":
        /// Castle of Dark Illusions; "This card gains 100 ATK/DEF": Pumpking the King of Ghosts).
        /// </summary>
        public int StandbyGrowthAmount;
        /// <summary>How many of those Standby Phases count at most ("your next 4 Standby Phases").</summary>
        public int StandbyGrowthSteps;
        /// <summary>
        /// PayLpOrDestroyThis: the upkeep is the player's choice ("pay 2000 LP or destroy this card": Mirror Wall,
        /// Messenger of Peace, Fairy Box, Mask of Brutality; Lua CheckLPCost + SelectYesNo), not the mandatory
        /// "you must pay N LP (this is not optional), or this card is destroyed" (Imperial Order). An optional
        /// upkeep is never paid with the last Life Points: that payment would lose the Duel, so the card is
        /// destroyed instead (StandbyGrowthAndMaintenance batch, v142).
        /// </summary>
        public bool UpkeepIsOptional;
        /// <summary>
        /// Activate clause of a Continuous Spell/Trap: a "When this card is activated:" effect that resolves even
        /// when this card has left the field before its activation resolves (Burning Land: the ygopro activation
        /// operation has no <c>c:IsRelateToEffect(e)</c> check). Without it, a Continuous card's own activation
        /// link resolves with no effect once the card is gone (StandbyGrowthAndMaintenance batch, v142).
        /// </summary>
        public bool ResolvesEvenIfThisLeftField;
        /// <summary>
        /// Cost: "send 1 Equip Spell Card you control that is equipped to this card to the Graveyard" (Guardian Ceal;
        /// ygopro c10755153 s.costfilter: face-up, GetEquipTarget() == this card, in your Spell &amp; Trap Zone). The
        /// effect cannot be activated while no such card exists; the player picks when there are several
        /// (EquipSpellCost fix, 2026-09-30).
        /// </summary>
        public bool RequiresSendEquipSpellOnThisToGy;

        // ── TargetedClassics batch ──

        /// <summary>
        /// Destroy clause (Nobleman of Crossout): destroy the face-down target and banish it
        /// (<see cref="BanishIfDestroyed"/>); only if it was destroyed and it is a Flip monster, each player reveals
        /// their Main Deck and banishes every card with that monster's name from it, then both Decks are shuffled
        /// (ygopro c71044499). The Flip-monster counterpart of <see cref="PurgeDecksIfTrap"/> (Nobleman of
        /// Extermination). A target that is no longer face-down at resolution is not affected.
        /// </summary>
        public bool PurgeDecksIfFlipMonster;

        // ── PositionLocks batch ──

        /// <summary>
        /// PositionLocks batch, <see cref="EffectActionKind.ChangeOppFaceUpMonsterPositionAtPhase"/>: "Once changed, the
        /// monster must remain in this position for the rest of the turn." (Aqua Spirit). The changed monster cannot
        /// change its battle position manually or be Flip Summoned for the rest of the turn, even after it is turned
        /// face-down; the lock ends when it leaves the field (ygopro EFFECT_CANNOT_CHANGE_POSITION,
        /// RESETS_STANDARD_PHASE_END without RESET_TURN_SET).
        /// </summary>
        public bool LocksPositionRestOfTurn;

        /// <summary>
        /// ContinuousGainAtkDef changes only this card, not every monster on its side. Battle-phase self auras
        /// (during your Battle Phase, or during your opponent's Battle Phase). Banner of Courage leaves this false.
        /// </summary>
        public bool AffectsThisCardOnly;

        /// <summary>
        /// Coverage family F1 (v180, AttributeBullets): a continuous clause that is live only while a face-up monster
        /// with this printed Attribute is on the field (either player's Monster Zone) — the Element monsters'
        /// "● FIRE: …" bullets. Empty on every other clause. Honored through
        /// <see cref="AttributeBulletsRuntime.FieldAttributePresent"/> at every site that applies a continuous clause.
        /// </summary>
        public string RequiresFieldAttribute;

        // ── FlipCostsAndFusion batch ──

        /// <summary>
        /// FlipCostsAndFusion batch, cost of a FLIP effect (Fire Sorcerer, Lua SetCost): banish this many cards chosen
        /// at random from your hand, face-up. With fewer cards in hand the FLIP effect cannot be activated. Paid when
        /// the FLIP effect activates (<see cref="FlipCostsAndFusionRuntime.TryPayFlipCost"/>). Only Flip clauses set it.
        /// </summary>
        public int CostBanishRandomHandCards;

        /// <summary>
        /// FlipCostsAndFusion batch, cost of a FLIP effect (Lady Assailant of Flames, Lua SetCost): banish the top this
        /// many cards of your Deck, face-up. With fewer cards in the Deck the FLIP effect cannot be activated. Paid
        /// when the FLIP effect activates (<see cref="FlipCostsAndFusionRuntime.TryPayFlipCost"/>).
        /// </summary>
        public int CostBanishTopDeckCards;

        /// <summary>
        /// FlipCostsAndFusion batch (Supply, Lua IsReason(REASON_FUSION)): a GY monster target must have been sent
        /// to the GY as Fusion Material and not have left the GY since
        /// (<see cref="FlipCostsAndFusionRuntime.IsFusionMaterialInGy"/>). Fusion Monsters qualify too.
        /// </summary>
        public bool RequiresSentAsFusionMaterial;

        // ── EquipFamily batch ──

        /// <summary>
        /// EquipThisToTarget (Dragonic Attack, Lua EFFECT_CHANGE_RACE): while this Equip Card applies, the equipped
        /// monster is this Monster Type (<see cref="CardInstance.Race"/>, layered with DNA Surgery by timestamp in
        /// <see cref="TypeDeclarationRuntime.CurrentRace"/>). The equip stays correct on its current host after the
        /// change (Lua eqlimit: the current target, or a <see cref="RaceFilter"/> monster).
        /// </summary>
        public string EquipChangesRaceTo;

        /// <summary>
        /// EquipThisToTarget (Scroll of Bewitchment, Lua AnnounceAnotherAttribute in the equip target step): when the
        /// card is activated, its controller declares an Attribute other than the target's current one; while this
        /// Equip Card applies, the equipped monster has that Attribute (<see cref="CardInstance.Attribute"/>).
        /// </summary>
        public bool EquipDeclaresAnotherAttribute;

        /// <summary>
        /// EquipThisToTarget equip limit (Ring of Magnetism, Lua eqlimit: GetHandlerPlayer() == the monster's
        /// controller): this Equip Card is a correct target only for monsters its own controller controls. Read by
        /// <see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/> (Collected Power, Tailor of the Fickle).
        /// </summary>
        public bool EquipLimitSameController;

        /// <summary>
        /// Dark Necrofear (Lua tgop flag): this card is in its owner's GY because it was destroyed in its owner's
        /// Monster Zone (by battle or by a card effect) by a card the opponent controls, and sent there this turn
        /// (<see cref="EquipFamilyRuntime.DestroyedByOpponentThisTurn"/>).
        /// </summary>
        public bool RequiresDestroyedByOpponentThisTurn;

        // ── RuleLocks batch ──

        /// <summary>
        /// RuleLocks batch: the <see cref="RequiresDiscardCost"/> card is discarded at random ("discard 1 random card
        /// from your hand to the Graveyard" — Jowgen the Spiritualist, ygopro Group.RandomSelect). Nobody picks it; the
        /// cost needs at least 1 card in the hand.
        /// </summary>
        public bool DiscardCostRandom;

        // ── NegationsAndGySummons batch ──

        /// <summary>
        /// NegationsAndGySummons batch, on a <see cref="EffectTiming.ThisCardSummoned"/> clause: "If Summoned this
        /// way" (Gilasaurus) — only when this card was Special Summoned by its own procedure
        /// (<see cref="CardInstance.SpecialSummonedByOwnProcedure"/>; ygopro GetSummonType()==SUMMON_TYPE_SPECIAL+1),
        /// not by another card's effect (Monster Reborn).
        /// </summary>
        public bool RequiresThisSummonedByOwnProcedure;

        /// <summary>
        /// NegationsAndGySummons batch, on a <see cref="EffectTiming.ChainLinkActivated"/> negation (Riryoku Field):
        /// the answered Chain Link activates a Spell Card that targets exactly 1 monster on the field and no other
        /// cards (ygopro EFFECT_FLAG_CARD_TARGET, #CHAININFO_TARGET_CARDS == 1, in LOCATION_MZONE; a face-down monster
        /// counts). <see cref="NegationsAndGySummonsRuntime.TargetsExactlyOneMonster"/>.
        /// </summary>
        public bool NegateRequiresSingleMonsterTarget;

        // ── FaceUpQuickEffects batch (v160) ──

        /// <summary>
        /// FaceUpQuickEffects batch, on a <see cref="EffectTiming.FaceUpQuickEffect"/> clause: this card's effect can
        /// be activated only once per Chain (ygopro SetCountLimit(1,0,EFFECT_COUNT_CODE_CHAIN); Skull Lair).
        /// </summary>
        public bool OncePerChain;

       // ── SimpleSpellTraps batch ──

        /// <summary>
        /// SimpleSpellTraps batch, on a <see cref="EffectActionKind.ContinuousGainAtkDef"/> aura with
        /// <see cref="CountSource"/> <see cref="SimpleSpellTrapsRuntime.FaceUpRaceYouControl"/> (The A. Forces): the
        /// Types counted ("Warrior|Spellcaster"), distinct from <see cref="RaceFilter"/>, the Types that gain the ATK.
        /// </summary>
        public string CountRaceFilter;

        // ── SpiritMonsters batch ──

        /// <summary>
        /// SpiritMonsters batch, on a <see cref="EffectTiming.ThisCardSummoned"/> clause with
        /// <see cref="RequiresThisNormalOrFlipSummoned"/>: "When this card is Normal Summoned or flipped face-up" — it also
        /// fires when this card is flipped face-up by battle or by a card effect, not only on a Flip Summon (ygopro
        /// EVENT_FLIP; Otohime, Maharaghi). <see cref="SpiritMonstersRuntime.NotifyFlippedFaceUp"/>.
        /// </summary>
        public bool AlsoWhenFlippedFaceUp;

        // ── SpecialSummonEffects batch (v161) ──

        /// <summary>
        /// SpecialSummonEffects batch: "You can only use this effect of "<see cref="NamedCard"/>" once per Duel." (ygopro
        /// SetCountLimit(1, id, EFFECT_COUNT_CODE_DUEL)): each player may activate this effect of a card with that name
        /// once in the whole Duel, whichever copy it is. Counted by <see cref="SpecialSummonEffectsRuntime"/>
        /// (<see cref="SpecialSummonEffectsRuntime.OncePerDuelUsed"/>) when the effect is activated.
        /// </summary>
        public bool OncePerDuel;

        // ── EquipAndSpellProtection batch ──

        /// <summary>
        /// EquipAndSpellProtection batch, on a <see cref="EffectTiming.SentFromFieldToGy"/> trigger: "If / When this card
        /// is destroyed by a card effect while equipped" (Smoke Grenade of the Thief, Blast with Chain; Lua EVENT_LEAVE_FIELD
        /// with REASON_DESTROY+REASON_EFFECT and GetEquipTarget()~=nil). Only a destruction by a card effect recorded while
        /// this card was equipped fires it (<see cref="EquipAndSpellProtectionRuntime.TryResolveDestroyedWhileEquipped"/>);
        /// an Equip Card sent to the GY because its monster left the field (by rule) does not.
        /// </summary>
        public bool RequiresDestroyedByEffectWhileEquipped;

        /// <summary>
        /// EquipAndSpellProtection batch, with <see cref="RequiresSendEquipSpellOnThisToGy"/>: "Send 1 Equip Card equipped
        /// to this card to the Graveyard" (Woodland Sprite; ygopro c6979239 s.costfilter GetEquipTarget()==c in either
        /// player's Spell &amp; Trap Zone): any face-up Equip Card equipped to this card (an Equip Spell, an Equip Trap or a
        /// monster acting as an Equip Card), whoever controls it — not only an Equip Spell Card you control.
        /// </summary>
        public bool EquipCostAnyEquipCard;

        // ── BattleBoosts batch (v161) ──

        /// <summary>
        /// BattleBoosts batch, with <see cref="RequiresDiscardCost"/>: the discarded card must be a monster of this Type
        /// (Spirit Ryu: "discard 1 Dragon monster"). Paid by <see cref="BattleBoostsRuntime"/>, which owns the pick.
        /// </summary>
        public string DiscardCostRace;

        // ── PgdSpellTrapBasics batch (v162 PGD) ──

        /// <summary>
        /// PgdSpellTrapBasics batch, with <see cref="EffectActionKind.AllYourFaceUpMonstersBecomeTypeUntilEndPhase"/>:
        /// the Type the monsters are treated as (D. Tribe: "Dragon").
        /// </summary>
        public string BecomesRace;

        // ── BattleOutcomeTriggers batch (v162) ──

        /// <summary>
        /// BattleOutcomeTriggers batch, on a <see cref="EffectTiming.ThisCardDestroysByBattle"/> trigger: the clause's target
        /// is the monster this card just destroyed by battle, while it is in the opponent's GY (Mystical Knight of Jackal,
        /// Winged Sage Falcos; ygopro Duel.SetTargetCard(c:GetBattleTarget()) with bc:IsLocation(LOCATION_GRAVE) and
        /// bc:IsControler(1-tp)). Offered by <see cref="BattleOutcomeTriggersRuntime"/>, never by a target picker of its own.
        /// </summary>
        public bool TargetIsMonsterDestroyedByThisBattle;

        /// <summary>
        /// BattleOutcomeTriggers batch, with <see cref="TargetIsMonsterDestroyedByThisBattle"/>: the destroyed monster was in
        /// face-up Attack Position on the field (Winged Sage Falcos; ygopro bc:IsPreviousPosition(POS_FACEUP_ATTACK)).
        /// </summary>
        public bool RequiresDestroyedWasFaceUpAttack;

        // ── FlipDownAndPeek batch (v162) ──

        /// <summary>
        /// FlipDownAndPeek batch, with <see cref="EffectActionKind.SetThisFaceDownDefense"/>: "After you use this effect,
        /// rearrange the face-down Defense Position monsters in your Main Monster Zones" (Wandering Mummy; ygopro
        /// Duel.ShuffleSetCard after a successful Duel.ChangePosition). Only when this card was flipped face-down.
        /// </summary>
        public bool ThenShuffleFaceDownMonstersInMainZones;

        /// <summary>
        /// FlipDownAndPeek batch: the target must be a face-down (Set) card (A Man with Wdjat: "1 Set card"). Filtered in
        /// TextEffectRuntime.CollectTargets.
        /// </summary>
        public bool RequiresTargetFaceDown;

        // ── StandbyBurnsAndDice batch (v162) ──

        /// <summary>
        /// StandbyBurnsAndDice batch: "When it leaves the field, destroy this card" on a monster-binding Continuous Trap
        /// (Nightmare Wheel; ygopro EVENT_LEAVE_FIELD on its persistent target). Unlike
        /// <see cref="DestroyThisWhenBoundHostDestroyed"/>, any way the bound monster leaves the field (destroyed, Tributed,
        /// returned to the hand or Deck, banished) sends this card away (DuelEngine.UnlinkEquips).
        /// </summary>
        public bool DestroyThisWhenBoundHostLeaves;

        /// <summary>
        /// StandbyBurnsAndDice batch, on <see cref="EffectActionKind.StandbyTributeOrDestroyThis"/>: the Tribute is not
        /// optional (The Unfriendly Amazon, Lua SelectReleaseGroup min 1 with no Yes/No): while a monster can be Tributed
        /// one must be, and Cancel does not decline it. Only when none can be Tributed is this card destroyed.
        /// </summary>
        public bool UpkeepTributeMandatory;

        // ── BattleDestroyedGyRecovery batch (v162) ──

        /// <summary>
        /// The target must be a monster in your GY that was sent there this turn by battle (ygopro GetTurnID() == turn
        /// and REASON_BATTLE; <see cref="GyEntryStamps.DestroyedByBattleThisTurn"/>). Crimson Sentry family.
        /// </summary>
        public bool RequiresTargetSentToGyByBattleThisTurn;

        /// <summary>
        /// The target must be a card in your GY that was sent there this turn by the opponent (ygopro GetTurnID() == turn
        /// and GetReasonPlayer() == 1-tp; <see cref="GyEntryStamps.SentByOpponentThisTurn"/>). Fairy Guardian.
        /// </summary>
        public bool RequiresTargetSentToGyByOpponentThisTurn;

        /// <summary>
        /// The target is 1 of "those monsters": the monsters destroyed by battle and sent to your GY that the open
        /// response window answers (Rope of Life; <see cref="BattleDestroyedGyRecoveryRuntime.FilterTargets"/>).
        /// </summary>
        public bool TargetsThoseMonstersDestroyedByBattle;

        /// <summary>
        /// Cost: "Discard your entire hand (min. 1)" — every card in your hand except this one, at least 1, paid when the
        /// response is seated on the chain (Rope of Life; <see cref="BattleDestroyedGyRecoveryRuntime.PaySeatCost"/>).
        /// </summary>
        public bool DiscardEntireHandCost;

        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Buster Rancher: "Only a monster with an ATK of N points or less can be
        /// equipped with this card." The activation's target and the equip limit (re-checked except during damage
        /// calculation: an Equip Card whose monster has more ATK is destroyed by the game rules). 0 = no ATK limit.
        /// </summary>
        public int EquipLimitAtkMax;

        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — Call of the Mummy: "You must control no monsters to activate and to
        /// resolve this effect." Checked when the effect is activated and again when it resolves.
        /// </summary>
        public bool RequiresControllerControlsNoMonsters;

        /// <summary>
        /// ContinuousLocksAndIgnitions batch (v163) — the effect of this face-up card resolves only while this card is still
        /// face-up in its zone (ygopro IsRelateToEffect; Call of the Mummy). Frontline Base resolves without it.
        /// </summary>
        public bool RequiresThisRelatedAtResolution;

        // ── PhaseTimersAndDeckTop batch (v163) ──

        /// <summary>
        /// <see cref="EffectActionKind.PlaceNamedFromDeckOnTop"/> with no <see cref="NamedCard"/>: the kind of card its
        /// controller chooses from their Deck — "Trap" (A Cat of Ill Omen) or "FieldSpell" (An Owl of Luck). Drill Bug keeps
        /// its <see cref="NamedCard"/>.
        /// </summary>
        public string DeckPickCardKind;

        /// <summary>
        /// <see cref="EffectActionKind.PlaceNamedFromDeckOnTop"/>: while a face-up card of this name whose activation resolved
        /// is on the field (ygopro Duel.IsEnvironment), the controller may add the chosen card to the hand instead of
        /// placing it on top of the Deck ("Necrovalley").
        /// </summary>
        public string AddToHandInsteadIfNamedOnField;

        // ── CallsDiceAndSetTriggers batch (v163) ──

        /// <summary>
        /// A "sent from the field to the GY" trigger that applies only when this card was destroyed while face-down on the
        /// field and sent to the GY on this trip (Statue of the Wicked, Dark Coffin: Lua REASON_DESTROY,
        /// IsPreviousLocation(LOCATION_ONFIELD), IsPreviousPosition(POS_FACEDOWN)). Any destroyer counts
        /// (<see cref="CallsDiceAndSetTriggersRuntime.WasDestroyedFaceDownThisTrip"/>).
        /// </summary>
        public bool RequiresDestroyedWhileFaceDown;

        /// <summary>
        /// On a <see cref="EffectActionKind.SpecialSummonToken"/> clause: each Token Summoned carries a lasting effect of
        /// the resolving player — when that Token is destroyed by battle, their opponent takes this much damage (Cobra
        /// Jar's Poisonous Snake Token; <see cref="CallsDiceAndSetTriggersRuntime.BindTokenBattleBurn"/>).
        /// </summary>
        public int TokenBattleDestroyedDamageToOpponent;

        /// <summary>
        /// Dice Jar (<see cref="EffectActionKind.BothPlayersRollDieLowerTakesHigherRollDamage"/>): the damage the loser
        /// takes when the winner rolled a 6.
        /// </summary>
        public int MaxRollDamage;

        /// <summary>
        /// Cost: send every other card in your hand and on your field to the GY (at least 1), paid as the card is
        /// activated (Reversal Quiz; <see cref="CallsDiceAndSetTriggersRuntime.PayActivationCost"/>).
        /// </summary>
        public bool CostSendAllOtherHandAndFieldCardsToGy;

        // ── SummonTriggersAndTributes batch (v163) ──

        /// <summary>
        /// Moisture Creature: the card has an extra Tribute Summon procedure of exactly this many Tributes (ygopro
        /// aux.AddNormalSummonProcedure(c,true,true,N,N,SUMMON_TYPE_TRIBUTE+1)), and this <see cref="EffectTiming.ThisCardSummoned"/>
        /// clause fires only for a Tribute Summon made that way (<see cref="SummonTriggersAndTributesRuntime.TributesToUse"/>,
        /// <see cref="SummonTriggersAndTributesRuntime.SummonedByTributeProcedure"/>).
        /// </summary>
        public int RequiresThisTributeSummonedByTributingExactly;

        /// <summary>
        /// Jowls of Dark Demise, on <see cref="EffectActionKind.TakeControlUntilEndPhase"/>: once control is taken, that
        /// monster can attack directly this turn (ygopro EFFECT_DIRECT_ATTACK, RESETS_STANDARD_PHASE_END; a face-down
        /// monster keeps the grant if it is flipped face-up, turning it face-down ends it).
        /// </summary>
        public bool TakenMonsterCanAttackDirectlyThisTurn;

        /// <summary>
        /// EquipLocksAndLeaveField batch — Big Bang Shot (c61127349, e5 EVENT_LEAVE_FIELD single continuous): when this
        /// Equip Card leaves the field for any reason while it is still equipped, the monster it was equipped to is
        /// banished face-up by this card's effect (<see cref="DuelEngine"/> UnlinkCardFromHostOnLeave). Not when the
        /// monster left first (no equip target then).
        /// </summary>
        public bool BanishHostWhenThisLeaves;

        /// <summary>
        /// EquipLocksAndLeaveField batch, on an <see cref="EffectActionKind.EquipThisToTarget"/> clause that may equip a
        /// monster on either field: the AI equips its own strongest face-up monster (AutoPick), because the card only
        /// helps its controller's monster (Big Bang Shot's ATK and piercing; Raregold / Metalsilver Armor apply only
        /// while you control the equipped monster). AI preference only; the legal targets do not change.
        /// </summary>
        public bool AiEquipsOwnMonster;

        // ── DcrEquipSpells batch ──

        /// <summary>
        /// DcrEquipSpells batch, with <see cref="RequiresDiscardCost"/>: the hand card is SENT to the GY as the cost, not
        /// discarded ("Send 1 card from your hand to the Graveyard"; ygopro Duel.DiscardHand(..., REASON_COST) without
        /// REASON_DISCARD — Twin Swords of Flashing Light - Tryce, Wicked-Breaking Flamberge - Baou). The pick and refund
        /// are the discard cost's; no discard event is recorded (MonsterEffects.SendFromHandToGraveByInstance).
        /// </summary>
        public bool DiscardCostIsSend;

        // ── SpellCounterSpenders batch ──

        /// <summary>
        /// Cost: "Remove N Spell Counters on/from your side of the field" (Lua Duel.RemoveCounter(tp,1,0,COUNTER_SPELL,N,
        /// REASON_COST)): N counters from any face-up cards its controller controls, the player choosing the cards
        /// (<see cref="SpellCounterSpendersRuntime"/>). Distinct from <see cref="RequiresRemoveSpellCounters"/> (this card only).
        /// </summary>
        public int RemoveSpellCountersOnYourSide;

        /// <summary>
        /// On <see cref="EffectActionKind.CannotAttackTurnSpecialSummoned"/>: the lock also follows a Normal Summon and a
        /// Flip Summon ("cannot attack in the same turn it is Normal Summoned, Flip Summoned, or Special Summoned": Armor Exe).
        /// </summary>
        public bool CannotAttackAlsoTurnNormalOrFlipSummoned;

        /// <summary>
        /// On an Activate <see cref="EffectActionKind.PlaceSpellCounters"/>: "Activate this card by placing N Spell Counters
        /// on it" (Pitch-Black Power Stone; Lua AddCounter in the target step). The counters are placed as the card is
        /// activated, before anyone can respond, and not again when its Chain Link resolves
        /// (<see cref="SpellCounterSpendersRuntime.PlaceOnActivation"/>).
        /// </summary>
        public bool PlaceSpellCountersAsActivated;

        // ── ChainLinkAnswers batch ──

        /// <summary>
        /// ChainLinkAnswers batch — My Body as a Shield (ygopro c69279219 s.condition): on a
        /// <see cref="EffectActionKind.NegateActivation"/> answer, the answered link is the opponent's Spell/Trap Card
        /// activation or monster effect whose resolution would destroy a monster on the field (either side; Destroy family
        /// only, fail-closed: <see cref="ChainLinkAnswersRuntime.LinkWouldDestroyMonsterOnField"/>).
        /// </summary>
        public bool NegateRequiresWouldDestroyMonsterOnField;

        /// <summary>
        /// ChainLinkAnswers batch — Spell Shield Type-8 bullet 2: the activation cost "Send N Spell Card(s) from your hand to
        /// the GY" (ygopro Duel.DiscardHand(..., REASON_COST) on a Spell filter; a send, not a discard: no discard event).
        /// </summary>
        public int CostSendSpellsFromHandCount;

        // ── UnionRiders batch (v175) ──

        /// <summary>
        /// On a Union's <see cref="EffectActionKind.EquipThisToTarget"/>: the Union names no monster and may equip to any
        /// face-up monster its controller controls (Metallizing Parasite - Lunatite: aux.AddUnionProcedure(c,nil)).
        /// Read by <see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/> and <see cref="UnionRidersRuntime.FilterTargets"/>.
        /// </summary>
        public bool UnionEquipsAnyFaceUpMonster;

        // ── SimpleTriggersAndSwarm batch ──

        /// <summary>
        /// On <see cref="EffectActionKind.SpecialSummonAnyNumberFromHand"/>: "but only 1 copy of each" — no two chosen
        /// monsters share a name (Mustering of the Dark Scorpions; Lua aux.dncheck).
        /// </summary>
        public bool SummonOnlyOneOfEachName;

        /// <summary>
        /// SimpleTriggersAndSwarm batch: a <see cref="EffectZoneFilter.FieldAnyMonster"/> target may be this card itself
        /// (Tsukuyomi: Lua SelectTarget(…, LOCATION_MZONE, LOCATION_MZONE, 1, 1, nil) — no exception). Read by
        /// TextEffectRuntime.CollectTargets.
        /// </summary>
        public bool TargetMayBeThisCard;

        // ── BattleDestroyedTriggers batch ──

        /// <summary>
        /// On a <see cref="EffectTiming.ThisCardDestroysByBattle"/> clause: the monster this card destroyed was in Defense
        /// Position when it battled (ygopro GetBattlePosition()&amp;POS_DEFENSE) and is in the GY (Shinato, King of a Higher
        /// Plane; <see cref="BattleDestroyedTriggersRuntime.SkipsBattleDestroying"/>).
        /// </summary>
        public bool RequiresDestroyedWasDefensePosition;

        // ── GuardiansAndEquipEvents batch ──

        /// <summary>
        /// On a <see cref="EffectTiming.ThisCardSummoned"/> clause: "When this card is Normal or Special Summoned" (Guardian
        /// Elma: Lua EVENT_SUMMON_SUCCESS + EVENT_SPSUMMON_SUCCESS) — a Flip Summon does not fire it.
        /// </summary>
        public bool RequiresThisNormalOrSpecialSummoned;

        /// <summary>
        /// On a <see cref="EffectActionKind.SpecialSummonThisFromHandByOwnProcedure"/> clause: "If this is the only card in your
        /// hand, you can Special Summon it (from your hand)" (Guardian Grarl: Lua spcon GetFieldGroupCount(tp,LOCATION_HAND,0)==1).
        /// </summary>
        public bool RequiresOnlyCardInHand;

        // ── DerivedAtkMonsters batch (v176) ──

        /// <summary>
        /// On <see cref="EffectActionKind.GainSelfAtkPerCount"/>: "This card's ATK (ATK/DEF) is (are each) equal to … x N"
        /// — the ATK (and the DEF when <see cref="DefAmount"/> is not 0) becomes Amount × the count instead of gaining it
        /// (ygopro EFFECT_SET_ATTACK / _DEFENSE on itself): a printed "?" counts too (<see cref="CardInstance.SelfSetAtk"/>).
        /// </summary>
        public bool SelfStatIsSet;

        // ── NamedSummonPairs batch ──

        /// <summary>
        /// A Tribute cost that names one card exactly ("Tribute 1 "Dark Magician"", Dedication through Light and Darkness):
        /// only a monster with that name may be Tributed (TextEffectRuntime.CollectTributeCost), while
        /// <see cref="NamedCard"/> names the monster the effect Special Summons.
        /// </summary>
        public string TributeNamedCard;

        // ── ArchfiendRiders batch ──

        /// <summary>
        /// ArchfiendRiders batch — Checkmate (c69313735 s.rfilter): the Tribute cost (<see cref="RequiresTributeCount"/>)
        /// must be a monster of this series (<see cref="TextEffectRuntime.CardMatchesSeries"/>: archetype, name or
        /// "treated as" text). When set, <see cref="NamedCard"/> names the target, not the Tribute, and a monster whose
        /// Tribute would leave no legal target is not offered (Lua: IsExistingTarget(..., c)).
        /// </summary>
        public string TributeSeriesName;

        // ── SpellTrapAnswersAndActivationLocks batch ──

        /// <summary>
        /// On a <see cref="EffectActionKind.NegateActivation"/> Chain answer: what the answer does after the generic
        /// negation (and destruction) of the answered activation, as its link resolves
        /// (<see cref="SpellTrapAnswersAndActivationLocksRuntime.TryResolveLink"/>): Cursed Seal's name lock, Spell
        /// Vanishing's same-name send, Judgment of Anubis's optional destroy and burn. None: nothing more.
        /// </summary>
        public EffectActionKind AfterNegationAction;

        /// <summary>
        /// SpellTrapAnswersAndActivationLocks batch — Judgment of Anubis (ygopro c55256016 s.condition): on a
        /// <see cref="EffectActionKind.NegateActivation"/> answer, the answered Spell Card activation's resolution would
        /// destroy a Spell/Trap Card on the field (GetOperationInfo CATEGORY_DESTROY; plain Destroy family only, fail-closed:
        /// <see cref="SpellTrapAnswersAndActivationLocksRuntime.LinkWouldDestroySpellTrapOnField"/>).
        /// </summary>
        public bool NegateRequiresWouldDestroySpellTrapOnField;

        // ── EndOfDamageStepRiders batch ──

        /// <summary>
        /// EndOfDamageStepRiders batch: the monster this card battled must be of one of these Types ("A|B", read with
        /// ClassicEraTemplates.RaceMatches). On <see cref="EffectActionKind.DestroyBattledMonsterAtEndOfDamageStep"/> (Getsu
        /// Fuhma, Ryu Kokki) its live Type at the end of the Damage Step; on a
        /// <see cref="EffectTiming.ThisCardDestroysByBattle"/> trigger (Insect Princess) its Type when it battled
        /// (<see cref="EndOfDamageStepRidersRuntime.SkipsBattleDestroying"/>). Null: any monster.
        /// </summary>
        public string BattledMonsterRaceFilter;

        /// <summary>
        /// BattleDamageEvents batch — Begone, Knave! / Des Counterblow (FIELD+TRIGGER_F EVENT_BATTLE_DAMAGE in the S/T
        /// Zone): a <see cref="EffectTiming.ThisCardInflictsBattleDamage"/> clause of a face-up Spell/Trap that fires when
        /// ANY monster (either player's) inflicts battle damage to either player; the clause applies to that monster (not
        /// a target), only while it is still on the field (activated in <see cref="BattleDamageEventsRuntime.FireAnyMonsterInflicted"/>,
        /// resolved after battle destruction in <see cref="BattleDamageEventsRuntime.ResolvePending"/>).
        /// </summary>
        public bool AnyMonsterBattleDamageToAnyPlayer;

        // ── TokensDeclarationsAndPicks batch ──

        /// <summary>
        /// TokensDeclarationsAndPicks batch — Stray Lambs / Multiplication of Ants (Lua GetLocationCount&gt;1 in s.target and
        /// s.activate): a <see cref="EffectActionKind.SpecialSummonToken"/> clause Summons all <see cref="TokenCount"/>
        /// Tokens or none. The activation needs that many free Monster Zones (a Tribute cost from your Main Monster Zone
        /// frees one); with fewer at resolution nothing is Summoned.
        /// </summary>
        public bool TokensNeedAllZones;

        /// <summary>
        /// TokensDeclarationsAndPicks batch — Stray Lambs (Lua s.cost: ACTIVITY_SUMMON / FLIPSUMMON / SPSUMMON == 0, then
        /// EFFECT_CANNOT_SUMMON / CANNOT_FLIP_SUMMON / CANNOT_SPECIAL_SUMMON with EFFECT_FLAG_OATH until the End Phase): the
        /// activator cannot have Normal, Flip or Special Summoned this turn, and after activating cannot Normal, Flip or
        /// Special Summon for the rest of the turn, except this card's own Special Summon. Setting stays legal.
        /// </summary>
        public bool ForbidsOtherSummonsThisTurn;

        // ── ChaosBanishSummons batch ──

        /// <summary>
        /// On a <see cref="EffectActionKind.SpecialSummonFromHandProcedure"/> banish procedure: "by banishing 1
        /// <see cref="AttributeFilter"/> and 1 SecondAttributeFilter monster from your GY" (Chaos Sorcerer, Black Luster
        /// Soldier - Envoy of the Beginning; Lua rescon): exactly one monster of each Attribute is banished.
        /// </summary>
        public string SecondAttributeFilter;

        /// <summary>
        /// "This card cannot attack the turn you activate this effect" (Lua rmcost: GetAttackAnnouncedCount()==0, then
        /// EFFECT_CANNOT_ATTACK with EFFECT_FLAG_OATH): the activation needs a card that declared no attack this turn, and
        /// the card cannot attack for the rest of the turn even when the effect is negated.
        /// </summary>
        public bool ThisCannotAttackTurnActivated;

        /// <summary>
        /// Chaosrider Gustaph: the resolution chooses 1 to this many cards (not targets).
        /// </summary>
        public int PickUpTo;

        // ── SpellTrapSealsAndImmunity batch ──

        /// <summary>
        /// SpellTrapSealsAndImmunity batch — on a <see cref="EffectActionKind.SelfDestroyUnlessNamedFaceUp"/> clause: the named
        /// card must be face-up in its controller's Monster Zone (Talismans, Lua descon: LOCATION_MZONE of the handler player),
        /// not merely anywhere on the field or in a Spell & Trap Zone.
        /// </summary>
        public bool NamedFaceUpInYourMonsterZone;

        /// <summary>
        /// SpellTrapSealsAndImmunity batch — Armor Break (Lua re:IsActiveType(TYPE_EQUIP) and re:IsHasType(EFFECT_TYPE_ACTIVATE)):
        /// a <see cref="EffectTiming.ChainLinkActivated"/> answer that only answers the activation of an Equip Spell Card
        /// (<see cref="SpellTrapSealsAndImmunityRuntime.ChainAnswerAllowed"/>).
        /// </summary>
        public bool NegateRespondsToEquipSpellOnly;

        // ── NormalMonsterSpells batch ──

        /// <summary>
        /// NormalMonsterSpells batch: the lowest current Level (Lua GetLevel) a monster this clause picks, equips or affects
        /// may have; 0 = no lower bound. On an <see cref="EffectActionKind.EquipThisToTarget"/> clause it is part of the
        /// equip limit (<see cref="EquipFamilyRuntime.IsCorrectEquipTarget"/>; Opti-Camouflage Armor: Level 1).
        /// </summary>
        public int MonsterLevelMin;

        /// <summary>
        /// NormalMonsterSpells batch: the highest current Level such a monster may have (Lua IsLevelBelow); 0 = no upper
        /// bound (Sword of the Soul-Eater: 3, Order to Smash and The Law of the Normal: 2).
        /// </summary>
        public int MonsterLevelMax;

        /// <summary>SOD simple weapon unique extras: the equip target must be a Fusion Monster.</summary>
        public bool RequiresFusionMonsterTarget;

        /// <summary>SOD simple weapon unique extras: the equip target must be a Ritual Monster.</summary>
        public bool RequiresRitualMonsterTarget;

        /// <summary>
        /// NormalMonsterSpells batch: such a monster must be a Normal Monster in the ygopro sense (IsType(TYPE_NORMAL): every
        /// Token is TYPE_NORMAL too). On an equip clause it is part of the equip limit (Sword of the Soul-Eater).
        /// </summary>
        public bool RequiresNormalType;

        /// <summary>
        /// NormalMonsterSpells batch — Sword of the Soul-Eater (c5371656, Lua s.cost/s.target): as this Equip Spell is
        /// activated (its target chosen, before anyone may respond), every other non-Token Normal Monster its controller
        /// controls is Tributed (REASON_COST); once it resolves, the equipped monster gains this much ATK for each one
        /// (an Equip effect; nothing when the card is equipped any other way).
        /// </summary>
        public int EquipAtkPerTributedOnActivation;
        // ── AttackShieldsAndDamageZero batch ──

        /// <summary>
        /// A condition on a continuous effect of this card, read where the effect applies
        /// (<see cref="AttackShieldsAndDamageZeroRuntime.WhileConditionMet"/>): "YouControlAnotherFaceUpRace" /
        /// "YouControlFaceUpRace" (<see cref="ConditionName"/> = the Type), "YouControlFaceUpLowLevelNormal"
        /// (<see cref="ConditionAmount"/> = the highest Level), "NoCardsInHandOrSpellTrapZones". Null = no condition.
        /// </summary>
        public string WhileCondition;

        /// <summary>
        /// <see cref="EffectActionKind.AvoidBattleDamageFromBattlesInvolving"/>: the matching monster is a Normal Monster of this
        /// Level or lower (Emissary of the Oasis). 0 = no Level filter.
        /// </summary>
        public int NormalMonsterMaxLevel;

        /// <summary>
        /// <see cref="TributeRaceFilter"/> matches the monster's current Type only, never a name that contains the word
        /// (Arcane Archer of the Forest: "Magical Plant Mandragola" is a Spellcaster, not a Plant).
        /// </summary>
        public bool TributeRaceStrict;
        // ── SanctuaryAndZera batch ──

        /// <summary>
        /// SanctuaryAndZera batch — "If "X" is not on (your side of) the field, this effect is not applied." (Archlord
        /// Zerato, The Agent of Judgment - Saturn, Mazera DeVille; ygopro: the operation re-checks IsExistingMatchingCard /
        /// IsEnvironment): at resolution the clause does nothing unless a face-up card with this name is on the field — on
        /// its controller's side when <see cref="RequiresControllerNamedCard"/> is set
        /// (<see cref="SanctuaryAndZeraRuntime.NotApplied"/>, asked by TextEffectRuntime.ApplyClause).
        /// </summary>
        public string NotAppliedUnlessNamedFaceUp;
    }

    /// <summary>When the activation condition is tested (PSCT "when" vs "if").</summary>
    public enum ConditionCheckedAt
    {
        Both = 0,
        Activation = 1,
        Resolution = 2
    }

    public enum OncePerTurnScope
    {
        None = 0,
        PerInstance = 1,
        PerCardName = 2
    }

    /// <summary>
    /// Compiled program for one passcode, produced from official <c>desc</c> text.
    /// Cached after first play so subsequent resolutions skip re-parse.
    /// </summary>
    [Serializable]
    public class CompiledCardProgram
    {
        public int CardId;
        public string CardName;
        public string TextHash;
        public string SourceText;
        /// <summary>ERAZ band id this program was compiled for (cache key with CardId).</summary>
        public string EraId;
        /// <summary>JsonUtility-friendly array (Lists don't serialize).</summary>
        public EffectClause[] Clauses = Array.Empty<EffectClause>();
        /// <summary>True when every non-empty fragment was matched to at least one clause.</summary>
        public bool FullyCompiled;
        public string[] UnparsedFragments = Array.Empty<string>();
        public string CompiledUtc;
        public int CompilerVersion = CardTextEffectCompiler.Version;
        /// <summary>"regex" | "ai" | "seed" — audit trail; live resolution ignores this.</summary>
        public string CompileSource = "regex";

        [NonSerialized] List<EffectClause> _clauseList;

        public List<EffectClause> ClauseList
        {
            get
            {
                if (_clauseList != null) return _clauseList;
                _clauseList = Clauses != null
                    ? new List<EffectClause>(Clauses)
                    : new List<EffectClause>();
                return _clauseList;
            }
        }

        public void SetClauses(List<EffectClause> list)
        {
            _clauseList = list ?? new List<EffectClause>();
            Clauses = _clauseList.ToArray();
        }

        public void SetUnparsed(List<string> list)
        {
            UnparsedFragments = list != null ? list.ToArray() : Array.Empty<string>();
        }

        public bool HasTiming(EffectTiming t)
        {
            foreach (var c in ClauseList)
                if (c != null && c.Timing == t) return true;
            return false;
        }

        public List<EffectClause> ClausesFor(EffectTiming t)
        {
            var list = new List<EffectClause>();
            foreach (var c in ClauseList)
                if (c != null && c.Timing == t) list.Add(c);
            return list;
        }

        public bool CanResolveAny =>
            ClauseList.Count > 0 &&
            (FullyCompiled || HasTiming(EffectTiming.Activate) || HasTiming(EffectTiming.Flip) ||
             HasTiming(EffectTiming.SentFromFieldToGy) || HasTiming(EffectTiming.AttackDeclared) ||
             HasTiming(EffectTiming.OpponentNormalOrFlipSummon) ||
             HasTiming(EffectTiming.DamageCalculation) ||
             HasTiming(EffectTiming.ThisCardSummoned) ||
             HasTiming(EffectTiming.StandbyPhase) ||
             HasTiming(EffectTiming.EndPhase) ||
             HasTiming(EffectTiming.ContinuousWhileFaceUp) ||
             HasTiming(EffectTiming.YouTakeLifePointDamage) ||
             HasTiming(EffectTiming.ThisCardDestroysByBattle) ||
             HasTiming(EffectTiming.ThisCardInflictsBattleDamage) ||
             HasTiming(EffectTiming.AfterDamageCalculation) ||
             HasTiming(EffectTiming.EndOfDamageStep) ||
             HasTiming(EffectTiming.StartOfDamageStep) ||
             HasTiming(EffectTiming.PositionChanged) ||
             HasTiming(EffectTiming.ThisCardDeclaresAttack) ||
             HasTiming(EffectTiming.ChainLinkActivated) ||
             HasTiming(EffectTiming.YourMonsterInflictsBattleDamage) ||
             HasTiming(EffectTiming.ThisCardPositionChanged) ||
             HasTiming(EffectTiming.ControllerDraws) ||
             HasTiming(EffectTiming.ThisCardControlChanged) ||
             HasTiming(EffectTiming.OpponentNormalDrawInDrawPhase) ||
              HasTiming(EffectTiming.ControllerNormalDrawInDrawPhase) ||
              HasTiming(EffectTiming.MonsterWouldBeSummoned) ||
              HasTiming(EffectTiming.EquippedMonsterDestroysByBattle));
    }
}
