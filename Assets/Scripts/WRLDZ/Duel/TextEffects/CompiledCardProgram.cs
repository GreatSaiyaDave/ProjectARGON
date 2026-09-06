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
        EndOfDamageStep
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
        CannotTargetCardsInGraveyard
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
        /// <summary>Monsters in the opponent's GY (Book of Life second target).</summary>
        OppGyMonsters,
        /// <summary>Monsters the opponent controls, face-up or face-down (Spellbinding Circle).</summary>
        OppMonsters
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
        /// <summary>How many cards the discard cost requires (0/1 = one). Final Destiny = 5.</summary>
        public int DiscardCostCount;
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
        /// RitualSummon: tributes must equal the Level exactly (AddProcEqual).
        /// False = Greater (sum &gt;= Level / Amount).
        /// </summary>
        public bool RitualExactLevel;
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
        /// <summary>Second printed name for SpecialSummonNamed (Elegant Egotist "X" or "Y").</summary>
        public string AltNamedCard;
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
        /// <summary>
        /// CannotBeAttackTarget on OTHER matching monsters, not this card
        /// (Marauding Captain: Warriors except this one).
        /// </summary>
        public bool ExceptThisCard;
        /// <summary>Special Summon in Defense Position (Soul Resurrection).</summary>
        public bool SummonInDefense;
        /// <summary>Special Summon selection may use fewer than Amount cards ("up to"/any number).</summary>
        public bool SpecialSummonUpTo;
        /// <summary>GY target must be a Normal Monster.</summary>
        public bool RequiresNormalMonster;
        /// <summary>ContinuousCannotAttack: Amount is a printed Level (Gravity Bind), not ATK.</summary>
        public bool AmountIsLevel;
        /// <summary>
        /// ThisCardInflictsBattleDamage: only if the battle had no monster target
        /// ("by a direct attack"). White Magical Hat leaves this false.
        /// </summary>
        public bool RequiresDirectAttack;
        /// <summary>Suijin: this card must be the current attack target.</summary>
        public bool RequiresThisIsAttackTarget;
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
        /// True when this clause negates the current activation link.
        public bool ChainResponseOnly;
        /// Response effect destroys the activated card if negation succeeds.
        public bool DestroyNegatedCard;
        /// Big Shield Gardna: flip this monster face-up Defense on resolution.
        public bool FlipSelfFaceUpDefense;
        public bool ActivationNegatable = true;
        public bool EffectNegatable = true;
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
             HasTiming(EffectTiming.EndOfDamageStep));
    }
}
