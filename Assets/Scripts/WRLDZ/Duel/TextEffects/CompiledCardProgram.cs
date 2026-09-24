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
        /// This card inflicted battle damage to the opponent (Masked Sorcerer / Bistro Butcher /
        /// White Magical Hat).
        /// </summary>
        ThisCardInflictsBattleDamage,
        /// <summary>
        /// A monster the controller controls inflicted battle damage to the opponent
        /// (Robbin' Goblin — continuous Trap "each time").
        /// </summary>
        YourMonsterInflictsBattleDamage,
        /// <summary>This card declared an attack (Jirai Gumo).</summary>
        ThisCardDeclaresAttack,
        /// <summary>
        /// This card's battle position changed. <see cref="EffectClause.PositionChangeToDefense"/>
        /// true = Attack → face-up Defense (Dream Clown / Tainted Wisdom); false = Defense →
        /// Attack, including a Flip Summon (Crass Clown).
        /// </summary>
        ThisCardPositionChanged,
        /// <summary>The controller drew a card or cards — once per draw action (Solemn Wishes).</summary>
        ControllerDraws,
        /// <summary>Control of this face-up card changed (Ameba / Griggle). Resolved for the NEW controller.</summary>
        ThisCardControlChanged
    }

    public enum EffectActionKind
    {
        None = 0,
        Draw,
        Destroy,
        SpecialSummonFromGy,
        SpecialSummonFromHand,
        AddFromGyToHand,
        AddFromDeckToHand,
        ChangeBattlePosition,
        NegateAttack,
        EndBattlePhase,
        ApplyWabokuStyle,
        ApplySwordsOfRevealingLight,
        BothPlayersDiscardAndRedraw,
        FusionSummonRegistered,
        EffectDamageBothFromOriginalAtk,
        CyberJarStyle,
        ContinuousCannotTargetDragons,
        /// <summary>Discard this card from hand; take no battle damage from that battle (Kuriboh).</summary>
        DiscardSelfNoBattleDamageThisBattle,
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
        /// <summary>
        /// Ritual Summon: activated by a Ritual Spell. Special Summon the named Ritual
        /// Monster (<see cref="EffectClause.NamedCard"/>, or any Ritual Monster of
        /// <see cref="EffectClause.AttributeFilter"/>) from the hand by Tributing monsters
        /// from hand/field whose total Level ≥ <see cref="EffectClause.Amount"/>.
        /// </summary>
        RitualSummon,
        /// <summary>Take control of all face-up opponent monsters with Level ≤ Amount.</summary>
        TakeControlLevelLeq,
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
        /// Mandatory Standby-Phase upkeep: the controller pays PayLpAmount each of
        /// their Standby Phases (not optional, no destruction) — the classic Archfiend
        /// maintenance cost (Vilepawn / Desrook / Darkbishop / Infernalqueen /
        /// Shadowknight / Terrorking). Waived while "Pandemonium" is face-up.
        /// </summary>
        StandbyMaintenancePayLp,
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
        /// <summary>Take control of the target until the End Phase (Change of Heart).</summary>
        TakeControlUntilEndPhase,
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
        /// <summary>Switch the original ATK and DEF of all face-up monsters until the end of this turn (Shield &amp; Sword).</summary>
        SwapOriginalAtkDefUntilEndOfTurn,
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
        /// <summary>The controller skips their next Standby Phase (Solomon's Lawbook).</summary>
        SkipControllerNextStandbyPhase,
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
        DestroyEquipsAttachedToThis
    }

    public enum EffectSide
    {
        Controller,
        Opponent,
        Both,
        Either
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
        /// <summary>Ritual Monsters in the controller's Deck (Senju of the Thousand Hands).</summary>
        DeckRitualMonsters,
        /// <summary>Ritual Spells in the controller's Deck (Sonic Bird).</summary>
        DeckRitualSpells,
        /// <summary>Cards in the opponent's hand, revealed to choose (Confiscation / The Forceful Sentry).</summary>
        OppHandCards,
        /// <summary>The opponent's face-down monsters (Bombardment Beetle).</summary>
        OppFaceDownMonsters,
        /// <summary>Face-down Spell/Trap Cards on either field (Nobleman of Extermination).</summary>
        FaceDownSpellTraps,
        /// <summary>Cards in your hand or Deck whose rules name matches NamedCard (Elegant Egotist).</summary>
        HandOrDeckNamed,
        /// <summary>Monsters in the opponent's GY (Gravedigger Ghoul).</summary>
        OppGyMonsters,
        /// <summary>Any card in either GY (Soul Release).</summary>
        AnyGyCards,
        /// <summary>Monsters the controller controls, face-up or face-down (Two-Pronged Attack).</summary>
        ControllerAnyMonsters,
        /// <summary>Monsters the opponent controls, face-up or face-down (Change of Heart).</summary>
        OppAnyMonsters,
        /// <summary>Face-up Spell/Trap Cards on either field with the printed name NamedCard (Dragon Piper).</summary>
        FaceUpNamedSpellTraps
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
        /// <summary>SpecialSummonNamed may use the hand.</summary>
        public bool FromHand;
        /// <summary>SpecialSummonNamed / add may use the Deck.</summary>
        public bool FromDeck;
        /// <summary>SpecialSummonNamed may use the GY.</summary>
        public bool FromGrave;
        /// <summary>Targets must have ATK ≤ the numeric cost just paid (Armed Dragon).</summary>
        public bool RequiresAtkLeqCost;
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
        public int TokenDestroyedDamage;
        /// <summary>Union: equip only to a monster named NamedCard / EquipHostName.</summary>
        public string EquipHostName;
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
        /// <summary>Phase trigger only if this copy is in the GY (Darklord Marie).</summary>
        public bool ResolvesFromGy;
        /// <summary>Cure Mermaid family: this copy must be face-up Attack Position.</summary>
        public bool RequiresThisAttackPosition;
        /// <summary>Dancing Fairy family: this copy must be face-up Defense Position.</summary>
        public bool RequiresThisDefensePosition;
        /// <summary>Pikeru / United We Stand: Amount is per face-up monster you control.</summary>
        public bool ScaleAmountByControllerMonsters;
        /// <summary>Mage Power: Equip ATK/DEF is per Spell/Trap you control (including this card).</summary>
        public bool ScaleAmountByControllerSpellTraps;
        /// <summary>Spirit: return to hand if Normal Summoned or flipped this turn.</summary>
        public bool RequiresSummonedOrFlippedThisTurn;
        /// <summary>Des Lacooda: "When this card is Flip Summoned" — not NS/SS or battle flip.</summary>
        public bool RequiresThisFlipSummoned;
        /// <summary>Vampiric Orchis family: "When this card is Normal Summoned" — not FS/SS.</summary>
        public bool RequiresThisNormalSummoned;
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
        /// <summary>Special Summon in Defense Position (Soul Resurrection).</summary>
        public bool SummonInDefense;
        /// <summary>GY target must be a Normal Monster.</summary>
        public bool RequiresNormalMonster;
        /// <summary>
        /// RitualSummon: Tribute Levels must EXACTLY equal the summoned monster's Level
        /// ("...exactly equal the Level of the Ritual Monster..." — Earth Chant / Contract
        /// with the Abyss) instead of the usual "equal N or more" (≥).
        /// </summary>
        public bool RitualExactLevel;
        /// <summary>ContinuousCannotAttack: Amount is a printed Level (Gravity Bind), not ATK.</summary>
        public bool AmountIsLevel;
        /// <summary>Suijin: this card must be the current attack target.</summary>
        public bool RequiresThisIsAttackTarget;
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
        public bool ActivationNegatable = true;
        public bool EffectNegatable = true;
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
        /// </summary>
        public string CountSource;
        /// <summary>This clause only applies if the previous clause of the same timing destroyed something.</summary>
        public bool RequiresPreviousClauseHit;
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
        /// <summary>Special Summon in face-down Defense Position (Nimble Momonga).</summary>
        public bool SummonFaceDown;
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
        /// <summary>Number of cards discarded as the activation cost (Darkness Approaches 2, Final Destiny 5).</summary>
        public int DiscardCostCount;
        /// <summary>After destroying and banishing a Trap, banish every copy from both Decks (Nobleman of Extermination).</summary>
        public bool PurgeDecksIfTrap;
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
             HasTiming(EffectTiming.YourMonsterInflictsBattleDamage) ||
             HasTiming(EffectTiming.ThisCardDeclaresAttack) ||
             HasTiming(EffectTiming.ThisCardPositionChanged) ||
             HasTiming(EffectTiming.ControllerDraws) ||
             HasTiming(EffectTiming.ThisCardControlChanged));
    }
}
