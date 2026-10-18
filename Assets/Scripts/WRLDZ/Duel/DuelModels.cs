using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel
{
    /// <summary>Official turn phases (Rulebook v10 Turn Structure).</summary>
    public enum DuelPhase
    {
        Draw,
        Standby,
        Main1,
        Battle,
        Main2,
        End,
        GameOver
    }

    /// <summary>Why the duel ended (see <see cref="DuelEngine.EndReason"/>).</summary>
    public enum DuelEndReason
    {
        None = 0,
        /// <summary>A player's LP reached 0.</summary>
        LifePoints,
        /// <summary>A player had to draw from an empty Deck.</summary>
        DeckOut,
        /// <summary>A card effect declared the winner (Exodia and similar).</summary>
        CardEffect,
        /// <summary>Both players reached 0 LP at the same time (Rulebook: the duel is a Draw).</summary>
        Draw,
        /// <summary>A duelist conceded (<see cref="DuelEngine.Concede"/>): a loss for them.</summary>
        Forfeit
    }

    public enum BattlePosition
    {
        Attack,
        Defense
    }

    public class CardInstance
    {
        public int InstanceId;
        public int CardId;
        public CardDef Def;

        bool _faceUp = true;

        /// <summary>
        /// Face-up on the field / public. Turning a face-up card face-down bumps <see cref="FaceDownStamp"/>
        /// (every path, including a direct assignment), so a grant can end at "turned face-down" and stay ended.
        /// </summary>
        public bool FaceUp
        {
            get => _faceUp;
            set
            {
                if (_faceUp && !value) FaceDownStamp++;
                else if (!_faceUp && value) FaceUpStamp++;
                _faceUp = value;
            }
        }

        /// <summary>
        /// Bumped each time this copy changes from face-up to face-down (ygopro RESET_TURN_SET). A "this turn"
        /// grant records it and ends when it differs (Soul Exchange, TributeSummons batch).
        /// </summary>
        public int FaceDownStamp;

        /// <summary>
        /// Bumped each time this copy changes from face-down to face-up, by any path (Flip Summon, flipped by
        /// battle, flipped by a card effect; ygopro EVENT_FLIP). SpiritsAndMisc batch: the Spirit return reads it
        /// through <see cref="FlippedFaceUpThisTurn"/>.
        /// </summary>
        public int FaceUpStamp;

        /// <summary>
        /// <see cref="FaceUpStamp"/> when the current turn began, or when this copy entered the field this turn
        /// (the engine records it at both points).
        /// </summary>
        public int FaceUpStampAtTurnStart;

        /// <summary>Flipped face-up (face-down → face-up) at least once this turn while on the field.</summary>
        public bool FlippedFaceUpThisTurn => FaceUpStamp != FaceUpStampAtTurnStart;

        /// <summary>
        /// This copy was Special Summoned by the card its text names ("Must first be Special Summoned with "X"":
        /// Wall Shadow, Harpie Lady Sisters; ygopro CompleteProcedure / STATUS_PROC_COMPLETE), so it may later be
        /// Special Summoned from the GY or banishment. Cleared when it is returned to the hand or leaves the hand
        /// for the GY / banishment (it must be properly summoned again); a copy in the Deck is a new instance. Read through <see cref="TextEffects.TributeSummonsRuntime.SemiNomiMayBeRevived"/>.
        /// </summary>
        public bool ProcedureCompleted;
        /// <summary>
        /// NegationsAndGySummons batch: this card's current field presence began with a Special Summon by its own
        /// procedure (Gilasaurus, Toons, Gate Guardian; ygopro SUMMON_TYPE_SPECIAL+1 from EFFECT_SPSUMMON_PROC), not by
        /// an effect. Set by DuelEngine.SpecialSummonToField, cleared on every other field entry. Read by "If Summoned
        /// this way" (<see cref="TextEffects.EffectClause.RequiresThisSummonedByOwnProcedure"/>).
        /// </summary>
        public bool SpecialSummonedByOwnProcedure;
        public BattlePosition Position = BattlePosition.Attack;

        /// <summary>Normal Summoned or Flip Summoned this turn (restricts position change).</summary>
        public bool SummonedThisTurn;

        /// <summary>Normal Set this turn (face-down DEF) — cannot Flip Summon same turn in TCG.</summary>
        public bool SetThisTurn;

        public bool AttackedThisTurn;

        /// <summary>Successful attack declarations resolved this Battle Phase (extra-attack cards).</summary>
        public int AttacksDeclaredThisTurn;

        /// <summary>
        /// Ritual Spell only: the Ritual Monster and Tributes the player chose while activating it. Used at
        /// resolution when still legal (RitualProcedures.TryResolve), then cleared. Runtime only.
        /// </summary>
        public CardInstance ChosenRitualMonster;
        public System.Collections.Generic.List<CardInstance> ChosenRitualTributes;

        public void ClearAttackFlags()
        {
            AttackedThisTurn = false;
            AttacksDeclaredThisTurn = 0;
        }

        public void NoteAttackResolved()
        {
            AttacksDeclaredThisTurn++;
            AttackedThisTurn = true;
            AttackLpCostPaidThisDeclaration = false;
            AttackCostsPaidThisDeclaration = false;
            AttackCoinResolvedThisDeclaration = false;
            AttackSealNotedThisDeclaration = false;
        }

        /// <summary>Dark Elf: the 1000 LP cost for this attack declaration was already paid. A replay does not pay again.</summary>
        public bool AttackLpCostPaidThisDeclaration;
        /// <summary>
        /// GitHub-lineage attack costs (Toll / Gravekeeper's Servant arms: AttackCosts) were already paid for
        /// this declaration. A replay after a declaration-time coin call does not pay again.
        /// </summary>
        public bool AttackCostsPaidThisDeclaration;
        /// <summary>Jirai Gumo: this attack declaration already called the coin. A replay does not toss again.</summary>
        public bool AttackCoinResolvedThisDeclaration;
        /// <summary>Electric Lizard: this declaration already sealed the attacker.</summary>
        public bool AttackSealNotedThisDeclaration;
        /// <summary>0 = none. While <c>TurnNumber</c> equals this, the monster cannot declare an attack.</summary>
        public int CannotAttackDuringTurnNumber;

        public void UndoLastResolvedAttack()
        {
            if (AttacksDeclaredThisTurn > 0)
                AttacksDeclaredThisTurn--;
            AttackedThisTurn = AttacksDeclaredThisTurn > 0;
        }

        /// <summary>Already changed battle position manually this turn.</summary>
        public bool ChangedPositionThisTurn;

        /// <summary>
        /// Ignition "Once per turn" already used this turn (Abyss Soldier, etc.).
        /// Reset on this controller's next turn.
        /// </summary>
        public bool EffectUsedThisTurn;

        /// <summary>Suijin family: once-while-face-up Quick Effect already used.</summary>
        public bool EffectUsedWhileFaceUp;

        /// <summary>Attacking monster ATK is 0 for this damage calculation only (Suijin).</summary>
        public bool AtkBecomesZeroThisCalculation;

        /// <summary>
        /// BattleWindows batch (Fairy Box): ATK is 0 until the end of this Battle Phase (ygopro EFFECT_SET_ATTACK_FINAL 0,
        /// RESET_PHASE+PHASE_BATTLE and RESETS_STANDARD). Kept or cleared by TextEffects.BattleWindowsRuntime on each
        /// board refresh and at the end of the Battle Phase.
        /// </summary>
        public bool AtkZeroUntilEndOfBattlePhase;

        /// <summary>For continuous Spells with turn counters (only when official script sets this).</summary>
        public int ContinuousTurnsRemaining;

        /// <summary>
        /// ygopro Card.GetTurnCounter: the controller's Standby Phases counted while this card is face-up on
        /// the field, reset to 0 by the card's activation (Wave-Motion Cannon, PerCountBurns v141).
        /// </summary>
        public int TurnCounter;

        /// <summary>Spell counters / other counters — only modified by registered effects.</summary>
        public int Counters;

        /// <summary>Xyz materials under this monster.</summary>
        public System.Collections.Generic.List<CardInstance> OverlayMaterials;

        /// <summary>ATK/DEF modifiers from continuous effects (registered only).</summary>
        public int AtkModifier;
        public int DefModifier;

        /// <summary>
        /// Presentation receipt: the part of <see cref="AtkModifier"/> /
        /// <see cref="DefModifier"/> that came from face-up Field Spells on the
        /// last board refresh. Never read by rules; drives the AR field aura.
        /// </summary>
        public int FieldAtkDelta;
        public int FieldDefDelta;

        /// <summary>
        /// Presentation receipt: this copy's position (0-based) within a batch of
        /// Tokens summoned together, so a single source like Ojama Trio can paint its
        /// three Tokens as distinct art variants (Yellow, Green, Black). Never read by
        /// rules; drives <see cref="WRLDZ.Presentation.TokenArtCatalog"/>. Default 0 for
        /// single-Token summons and Tokens created outside the summon loop.
        /// </summary>
        public int TokenArtIndex;

        /// <summary>Until End Phase (Bark of Dark Ruler, Mask of Weakness). Not cleared by aura refresh.</summary>
        public int UntilEndOfTurnAtk;
        public int UntilEndOfTurnDef;
        /// <summary>Shield &amp; Sword: this copy's original ATK and DEF are swapped until the End Phase.</summary>
        public bool SwapOriginalAtkDefThisTurn;
        /// <summary>Damage Step only. Cleared as soon as that battle's damage calculation returns.</summary>
        public int DamageStepAtkBonus;
        /// <summary>
        /// Lingering ATK change that aura refresh must not wipe (Adhesion Trap Hole
        /// halves original ATK; Slate Warrior Flip). Reset when the card leaves the field.
        /// </summary>
        public int LingeringAtkModifier;
        /// <summary>Lingering DEF change (Slate Warrior Flip / destroyer loss). Not wiped by auras.</summary>
        public int LingeringDefModifier;
        /// <summary>This copy's last trip to the GY was destruction by battle.</summary>
        public bool WasDestroyedByBattle;
        /// <summary>The other battler when <see cref="WasDestroyedByBattle"/> (Yomi / Slate).</summary>
        public CardInstance BattleDestroyer;
        /// <summary>Last field→GY was by the effect of a Continuous Spell (Malice Doll).</summary>
        public bool SentByContinuousSpellEffect;
        /// <summary>Last field→GY was by an opponent's card effect (Vampire Lord revival gate).</summary>
        public bool SentByOpponentEffect;
        /// <summary>Turn number when this copy last left the field for the GY (0 = never).</summary>
        public int SentFromFieldTurnNumber;
        /// <summary>Vampire Lord revival: used the once-per-turn Standby Phase revival this turn.</summary>
        public bool OncePerTurnStandbyRevivalUsed;
        /// <summary>
        /// Turn number when this copy was last put into a banished pile (0 = never): "was banished this turn"
        /// (D.D. Scout Plane, BanishedReturn batch). Set by every engine path that banishes a card.
        /// </summary>
        public int BanishedTurnNumber;

        /// <summary>Gear Golem-style: this copy may attack directly this turn only.</summary>
        public bool DirectAttackThisTurn;

        /// <summary>True if this copy was Special Summoned (Jowgen, etc.).</summary>
        public bool WasSpecialSummoned;
        /// <summary>True if this copy was Tribute Summoned (Blast Held by a Tribute).</summary>
        public bool WasTributeSummoned;

        /// <summary>Destroyed an opponent's monster by battle this turn (LV / Insect Queen).</summary>
        public bool DestroyedByBattleThisTurn;

        /// <summary>
        /// Cannot be Tributed for a Tribute Summon while face-up
        /// (Ojama Tokens; Fox Fire leftover sentence).
        /// </summary>
        public bool CannotBeTributedForSummon;

        /// <summary>
        /// Cannot be Tributed at all while face-up — for a Tribute Summon and for any other Tribute (ygopro
        /// EFFECT_UNRELEASABLE_SUM + EFFECT_UNRELEASABLE_NONSUM: Archfiend's Roar's "It cannot be Tributed."). Set together
        /// with <see cref="CannotBeTributedForSummon"/>; a non-Summon Tribute (the Toon procedure) reads only this one.
        /// </summary>
        public bool CannotBeTributed;

        /// <summary>Controller takes this much damage when this Token is destroyed.</summary>
        public int TokenDestroyedDamage;

        /// <summary>
        /// Bumped each time this copy enters the field (Summon / Set / Special Summon; a Spell/Trap Set or activated
        /// from the hand into a Spell &amp; Trap Zone), so a "this turn" grant on it can tell the same card from one
        /// that left and came back (Soul Exchange, TributeSummons batch; Card Shuffle, PgdSpellTrapBasics).
        /// </summary>
        public int FieldEntryStamp;

        /// <summary>
        /// RedirectAndReplace batch (v142), Kotodama: the face-up monsters on the field when this card's same-name
        /// effect last applied (the ygopro EVENT_ADJUST label group), each with its <see cref="FieldEntryStamp"/> /
        /// <see cref="FaceDownStamp"/> then, so one that left and came back, or was turned face-down and up again,
        /// is new. Null until the effect first applies after this copy is face-up on the field and not negated; a
        /// monster outside it that shares a name with another face-up monster is destroyed. Runtime only
        /// (<see cref="TextEffects.RedirectAndReplaceRuntime"/>).
        /// </summary>
        public System.Collections.Generic.List<(CardInstance Card, int EntryStamp, int FaceDownStamp)>
            SameNameSeenFaceUp;

        /// <summary>
        /// <see cref="FieldEntryStamp"/> / <see cref="FaceDownStamp"/> when <see cref="SameNameSeenFaceUp"/> was
        /// recorded: a new field presence, or being turned face-down, starts the effect over (ygopro RESETS_STANDARD).
        /// </summary>
        public int SameNameSeenEntryStamp;
        public int SameNameSeenFaceDownStamp;

        /// <summary>
        /// Temporary Special Summon: destroy this monster during the End Phase of the
        /// turn whose number this equals (Archfiend's Roar). -1 = permanent.
        /// </summary>
        public int TempDestroyOnEndOfTurn = -1;

        /// <summary>
        /// FlipCostsAndFusion batch: the Fusion Monster this copy was sent to the GY as Fusion Material for
        /// (SpellTrapEffects.TryResolveFusion; ygopro REASON_FUSION + GetReasonCard). Null otherwise. Valid only while
        /// this copy stays in the GY: cleared when it is put into the GY again or enters the field, and by
        /// <see cref="TextEffects.FlipCostsAndFusionRuntime.SweepFusionMaterialStamps"/> once it is no longer in its
        /// owner's GY (Supply, De-Fusion).
        /// </summary>
        public CardInstance SentAsFusionMaterialFor;

        /// <summary>
        /// FlipCostsAndFusion batch: the materials used for this monster's Fusion Summon (ygopro GetMaterial), set by
        /// SpellTrapEffects.TryResolveFusion; null when this field presence was not a Fusion Summon (cleared on every
        /// field entry). Read by De-Fusion.
        /// </summary>
        public System.Collections.Generic.List<CardInstance> FusionMaterialsUsed;

        /// <summary>Max Spell Counters (Breaker = 1, Library = 3). 0 = no cap.</summary>
        public int SpellCounterMax;

        /// <summary>Union / Relinquished: this card is equipped to EquippedTo.</summary>
        public CardInstance EquippedTo;

        /// <summary>
        /// The Relinquished-family host whose own "equip that target to this card" effect equipped this monster
        /// (ygopro c64631466: the absorbed card carries that host's flag, s.eqfilter). Only that card sets the host's
        /// ATK/DEF, counts for its max. 1 and is destroyed instead of it by battle; any other Equip Card on the same
        /// host (an Equip Spell, Kiseitai) does not. Valid only while <see cref="EquippedTo"/> is that same host
        /// (<see cref="TextEffects.TextEffectRuntime.AbsorbedEquipOf"/>).
        /// </summary>
        public CardInstance AbsorbedBy;

        /// <summary>
        /// BothSideAndMonsterEquip batch (v161): the monster another card's effect equipped this monster Card to "as an
        /// Equip Spell Card with this effect" (Sword Hunter, ygopro Duel.Equip + an EFFECT_TYPE_EQUIP effect registered
        /// on the equipped card). While it is still equipped to that host it is an Equip Spell Card whose only effect is
        /// the granted one (<see cref="GrantedEquipAtk"/>): its own printed monster text does not apply
        /// (<see cref="IsNegated"/> reads true). Meaningless once <see cref="EquippedTo"/> is another card or null.
        /// </summary>
        public CardInstance GrantedEquipHost;

        /// <summary>BothSideAndMonsterEquip batch (v161): the granted equip effect "The equipped monster gains N ATK".</summary>
        public int GrantedEquipAtk;

        /// <summary>
        /// BothSideAndMonsterEquip batch (v161): this monster Card is an Equip Spell Card with only a granted effect
        /// (<see cref="GrantedEquipHost"/>) right now.
        /// </summary>
        public bool IsEquipSpellWithGrantedEffectOnly =>
            GrantedEquipHost != null && ReferenceEquals(EquippedTo, GrantedEquipHost);

        /// <summary>This monster's current controller is from an Equip take-control (Falling Down).</summary>
        public bool TakenByEquipControl;

        /// <summary>Change of Heart family: control returns at this turn's End Phase.</summary>
        public bool ControlRevertsAtEndPhase;

        /// <summary>Owner before <see cref="ControlRevertsAtEndPhase"/> take-control.</summary>
        public bool OriginalControllerIsPlayer;

        /// <summary>
        /// The owner, set when control of this card moves to the other player (it is null
        /// while the owner controls it). Cards leaving the field go to this player's
        /// GY / banished pile / hand, not the current controller's.
        /// </summary>
        public DuelistState ControlOwner;

        /// <summary>
        /// Temporary control (Change of Heart): return control to <see cref="ControlOwner"/>
        /// during the End Phase of the turn whose number this equals. -1 = none.
        /// </summary>
        public int ReturnControlAtEndOfTurn = -1;

        /// <summary>
        /// Refreshed each board update by a face-up position lock (Dragon Capture Jar):
        /// this monster cannot change its battle position.
        /// </summary>
        public bool PositionLockedByEffect;

        /// <summary>This monster cannot declare an attack while TurnNumber ≤ this value (Electric Lizard).</summary>
        public int CannotAttackThroughTurn = -1;

        // (DamageStepAtkBonus is declared once above; both lineages share it — Insect Soldiers of the Sky.)

        /// <summary>Turn number this copy was Normal (incl. Tribute) or Flip Summoned; -1 otherwise (Infinite Dismissal).</summary>
        public int NormalOrFlipSummonedTurn = -1;

        /// <summary>This monster cannot attack while this card stays face-up on the field (Invitation to a Dark Sleep).</summary>
        public CardInstance AttackLockedBy;

        /// <summary>
        /// Charmers (TakeControlWhileSourceFaceUp): the player who took this monster keeps control of
        /// it only while this source card stays face-up on the field. Checked on each board refresh
        /// (TakeControlRuntime.SweepBoundControl); cleared when this monster leaves or re-enters the field,
        /// is turned face-down (control stays), is switched (TrySwapControl) or when Remove Brainwashing
        /// starts to apply.
        /// </summary>
        public CardInstance ControlBoundTo;

        /// <summary>
        /// The Charmer control record behind <see cref="ControlBoundTo"/>: who took the monster and which
        /// older control effect it was layered over. Read only while its Source is <see cref="ControlBoundTo"/>.
        /// </summary>
        public TextEffects.ControlBinding ControlBinding;

        /// <summary>Monsters/Unions currently equipped to this card.</summary>
        public readonly List<CardInstance> Equips = new();

        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Mirror Wall: how many face-up Mirror Walls halve this
        /// monster's ATK right now (<see cref="CurrentAtk"/> halves the final value once per wall). Written only
        /// by <see cref="TextEffects.StandbyGrowthAndMaintenanceRuntime"/>, recomputed on every board refresh.
        /// </summary>
        public int AtkHalvings;

        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142): this card's Standby-growth record — on Castle of Dark
        /// Illusions, the monsters its FLIP boosted and how many of its controller's Standby Phases counted; on
        /// Pumpking the King of Ghosts, its own count. Read through
        /// <see cref="TextEffects.StandbyGrowthAndMaintenanceRuntime"/>; it ends with the field presence it
        /// was recorded for.
        /// </summary>
        public TextEffects.StandbyGrowthGrant StandbyGrowth;

        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142), Mirror Wall: the opponent's monsters that attacked while this
        /// card was face-up (ygopro's shared group). Cleared when this card is activated again.
        /// </summary>
        public List<TextEffects.AttackerHalvingMark> AttackerHalvingMarks;

        /// <summary>
        /// Gorgon's Eye (ContinuousNegation batch): this face-up monster's effects are negated until the end of
        /// the turn, because it is in face-up Defense Position while that Trap is on the field. Recomputed on
        /// every board refresh (ContinuousNegationRuntime.Refresh), so it never outlives its condition.
        /// </summary>
        public bool GorgonEyeNegated;

        /// <summary>Set only by official continuous effect scripts — never inferred from free text.</summary>
        public bool HasPiercing;
        public bool CannotBeDestroyedByBattle;
        public bool IsToken;
        /// <summary>
        /// Effect negated while face-up (its effects do not apply). Reads true while the stored flag (set
        /// directly by a negation effect), <see cref="NegatedByTurnFamily"/> or
        /// <see cref="ActivationUnresolvedOnChain"/> is set; assigning writes only the stored flag, so a
        /// refresh that clears or sets it never drops a turn-scoped negation, and vice versa.
        /// Also true for a monster Card acting as an Equip Spell with only a granted effect
        /// (<see cref="IsEquipSpellWithGrantedEffectOnly"/>, v161): its own monster text does not apply there.
        /// </summary>
        public bool IsNegated
        {
            get => IsEffectNegatedAsCard || IsEquipSpellWithGrantedEffectOnly;
            set => _isNegated = value;
        }

        /// <summary>
        /// <see cref="IsNegated"/> without the "own text does not apply" term of a monster Card acting as an Equip Spell
        /// with only a granted effect (BothSideAndMonsterEquip, v161): whether this card's effects, the granted one
        /// included, are negated (the stored flag, <see cref="NegatedByTurnFamily"/>, <see cref="ActivationUnresolvedOnChain"/>).
        /// </summary>
        public bool IsEffectNegatedAsCard => _isNegated || NegatedByTurnFamily || ActivationUnresolvedOnChain ||
                                            EffectsNegatedAfterBattle || GorgonEyeNegated;

        bool _isNegated;

        /// <summary>
        /// NegationMonsters batch (v161): Dark Ruler Ha Des / Twin-Headed Wolf negated this monster's effects as it was
        /// destroyed by battle (ygopro EFFECT_DISABLE with RESETS_STANDARD_EXC_GRAVE): <see cref="IsNegated"/> reads true
        /// for the rest of its time on the field, and its effects stay negated while it is in the GY
        /// (<see cref="TextEffects.NegationMonstersRuntime.NegatedInGy"/>). Cleared when it enters the field again
        /// (DuelEngine.ResetOnFieldEntry) or is found in a hand or banished pile (NegationMonstersRuntime.Refresh).
        /// </summary>
        public bool EffectsNegatedAfterBattle;

        /// <summary>
        /// TurnNegationTraps batch (v141): a Field Spell placed face-up in its Field Zone whose activation
        /// is still an unresolved Chain Link (the opponent may chain to it). Its effects do not apply yet
        /// (<see cref="IsNegated"/> reads true) and it does not count as that Field Spell being on the
        /// field ("while Umi is on the field") until the activation resolves (ygopro STATUS_EFFECT_ENABLED).
        /// Set and cleared by DuelEngine around the Field Spell's chain window; a board refresh clears it
        /// once no Chain Link for the card is left.
        /// </summary>
        public bool ActivationUnresolvedOnChain;

        /// <summary>
        /// TurnNegationTraps batch (v141): this face-up card in a Spell &amp; Trap Zone or Field Zone belongs
        /// to a card family negated for this turn (World Suppression: Field Spells, Mystic Probe: Continuous
        /// Spells, Metal Detector: Continuous Traps). Written only by
        /// <see cref="TextEffects.TurnNegationTrapsRuntime"/>, recomputed on every board refresh and at the
        /// start of each turn.
        /// </summary>
        public bool NegatedByTurnFamily;
        /// <summary>Magical Hats dummy: Spell/Trap treated as a 0/0 Normal Monster this Battle Phase.</summary>
        public bool MagicalHatDummy;
        /// <summary>int.MinValue = none. Cocoon of Evolution original ATK replace.</summary>
        public int OriginalAtkOverride = int.MinValue;
        /// <summary>int.MinValue = none. Cocoon of Evolution original DEF replace.</summary>
        public int OriginalDefOverride = int.MinValue;
        /// <summary>
        /// DerivedAtkMonsters batch (v176): "this card's ATK becomes / is N" set by this monster's own effect (ygopro
        /// EFFECT_SET_ATTACK on itself, a SINGLE effect): it replaces the original ATK, a printed "?" included, as the
        /// value every other modifier adds to. int.MinValue = none. Written on each board refresh
        /// (FieldSpellEffects.ApplySelfScale with <see cref="TextEffects.EffectClause.SelfStatIsSet"/>,
        /// <see cref="TextEffects.DerivedAtkMonstersRuntime.ApplyBoard"/>); cleared by the refresh and on every field entry.
        /// </summary>
        public int SelfSetAtk = int.MinValue;
        /// <summary>DerivedAtkMonsters batch (v176): the DEF counterpart of <see cref="SelfSetAtk"/> (ygopro EFFECT_SET_DEFENSE).</summary>
        public int SelfSetDef = int.MinValue;
        /// <summary>
        /// DerivedAtkMonsters batch (v176): the monsters Tributed for this card's Tribute Summon or Tribute Set (ygopro
        /// GetMaterial), each with its <see cref="GyEntryStamp"/> right after the Tribute: one still counts only while it
        /// stays in that GY (REASON_SUMMON and GetReasonCard() == this card). Recorded by
        /// <see cref="TextEffects.DerivedAtkMonstersRuntime.NoteTributeSummon"/>; valid while
        /// <see cref="TributeMaterialsEntryStamp"/> == <see cref="FieldEntryStamp"/> (that field presence, and the trip
        /// to the GY that ended it); cleared on every field entry.
        /// </summary>
        public System.Collections.Generic.List<(CardInstance Card, int GyStamp)> TributeMaterials;
        /// <summary>DerivedAtkMonsters batch (v176): <see cref="FieldEntryStamp"/> when <see cref="TributeMaterials"/> was recorded.</summary>
        public int TributeMaterialsEntryStamp;
        /// <summary>Cocoon of Evolution: controller End Phases while equipped to Petit Moth.</summary>
        public int EquipTurnCounter;

        /// <summary>Level change from face-up Field Spells / registered continuous (e.g. A Legendary Ocean −1).</summary>
        public int LevelModifier;

        /// <summary>
        /// SimpleTargetUtility batch (v173) — Cost Down: the TurnNumber through which
        /// <see cref="LevelReducedAmount"/> is subtracted from this card's Level by
        /// <c>SimpleTargetUtilityRuntime.ApplyLevelStamps</c> (0 = no stamp). The stamp follows the card
        /// from the hand to the field within the same turn ("even after they are Summoned").
        /// </summary>
        public int LevelReducedUntilTurn;
        /// <summary>SimpleTargetUtility batch (v173): Level reduction stamped by Cost Down (usually −2).</summary>
        public int LevelReducedAmount;

        /// <summary>
        /// SimpleTargetUtility batch (v173) — Fairy of the Spring: the TurnNumber through which this
        /// specific card cannot be activated (0 = never blocked). Read in
        /// <c>TextEffectRuntime.CanActivate</c>; self-expiring (TurnNumber mismatch = legal again).
        /// </summary>
        public int ActivationBlockedUntilTurn;

        /// <summary>
        /// Rules name override from a name condition (A Legendary Ocean is always treated as "Umi").
        /// Display <see cref="Name"/> stays the printed title.
        /// </summary>
        public string TreatedAsName;

        int PrintedAtk => OriginalAtkOverride != int.MinValue ? OriginalAtkOverride : (Def != null ? Def.atk : 0);
        int PrintedDef => OriginalDefOverride != int.MinValue ? OriginalDefOverride : (Def != null ? Def.def : 0);
        int ShownAtk => SelfSetAtk != int.MinValue ? SelfSetAtk : SwapOriginalAtkDefThisTurn ? PrintedDef : PrintedAtk;
        int ShownDef => SelfSetDef != int.MinValue ? SelfSetDef : SwapOriginalAtkDefThisTurn ? PrintedAtk : PrintedDef;

        public int CurrentAtk =>
            AtkBecomesZeroThisCalculation || AtkZeroUntilEndOfBattlePhase
                ? 0
                : ShownAtk >= 0
                    ? HalveFinal(System.Math.Max(0, ShownAtk + AtkModifier + UntilEndOfTurnAtk + LingeringAtkModifier + DamageStepAtkBonus))
                    : 0;

        /// <summary>
        /// Mirror Wall (StandbyGrowthAndMaintenance batch, v142): "has its ATK halved" is the final ATK (ygopro
        /// EFFECT_SET_ATTACK_FINAL, <c>GetAttack()/2</c>), so it halves the sum of every other modifier, once per
        /// Mirror Wall that marked this monster, rounded down.
        /// </summary>
        int HalveFinal(int atk) => AtkHalvings > 0 ? atk >> System.Math.Min(AtkHalvings, 30) : atk;
        public int CurrentDef =>
            ShownDef >= 0
                ? System.Math.Max(0, ShownDef + DefModifier + UntilEndOfTurnDef + LingeringDefModifier)
                : 0;
        public string Name => Def?.name ?? $"#{CardId}";

        public string RulesName =>
            !string.IsNullOrEmpty(TreatedAsName) ? TreatedAsName : Name;

        public bool IsNamed(string n) =>
            !string.IsNullOrEmpty(n) &&
            (string.Equals(Name, n, System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(RulesName, n, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Current Monster Type: the printed Type (<see cref="CardDef.race"/>), unless this is a face-up monster
        /// on the field and a face-up continuous effect changes the Type of the monsters there (DNA Surgery,
        /// TypeDeclaration batch v142: <see cref="TextEffects.TypeDeclarationRuntime.CurrentRace"/>). Type checks on
        /// a card instance read this; a Spell/Trap's subtype stays on <see cref="CardDef.race"/>.
        /// </summary>
        public string Race => TextEffects.TypeDeclarationRuntime.CurrentRace(this);

        /// <summary>
        /// Current Attribute: the printed Attribute (<see cref="CardDef.attribute"/>), unless this is a face-up
        /// monster on the field equipped with a face-up, non-negated Equip Card that changes its Attribute (Scroll of
        /// Bewitchment, EquipFamily batch: <see cref="TextEffects.EquipFamilyRuntime.CurrentAttribute"/>). Field
        /// readers (auras, Earthshaker, the Charmers, equip filters) read this; hand / GY readers keep the printed one.
        /// </summary>
        public string Attribute => TextEffects.EquipFamilyRuntime.CurrentAttribute(this);

        /// <summary>Printed Level, or current Level after continuous modifiers (minimum 1 for monsters).</summary>
        public int PrintedLevel => Def?.level ?? 0;

        public int Level
        {
            get
            {
                var printed = PrintedLevel;
                if (printed <= 0) return printed;
                return System.Math.Max(1, printed + LevelModifier);
            }
        }

        /// <summary>Official card text (authority) — never invent alternate wording.</summary>
        public string OfficialText => Rules.OfficialCardAuthority.OfficialText(Def);

        // ── GY-entry stamp (BattleDestroyedGyRecovery batch, v162) ──
        // Set at every move into a GY by TextEffects.GyEntryStamps (RedirectAndReplaceRuntime.PutInGraveyard,
        // DuelEngine.SendCardToGrave / DestroyMonster, the discard and destroyer records), cleared when the card enters
        // the field. Read only while the card is still in that GY.

        /// <summary>The turn this copy last entered a GY (ygopro GetTurnID of a card in the GY); 0 = never stamped.</summary>
        public int GyEntryTurn;

        /// <summary>Why it last entered a GY (ygopro REASON_BATTLE / EFFECT / COST / DISCARD / DESTROY / RULE).</summary>
        public GyEntryReason GyEntryReason;

        /// <summary>The player whose card or action sent it there (ygopro GetReasonPlayer); null when none is known.</summary>
        public DuelistState GyEntryReasonPlayer;

        /// <summary>Bumped at every GY entry: one stay in the GY is told from a later one (Revival Jam's flag).</summary>
        public int GyEntryStamp;

        /// <summary>
        /// ZoneCapsAndPhaseWindows batch: the player who held this copy right before its last GY entry — its controller on
        /// the field, else the holder of the hand / Deck it left (ygopro GetPreviousControler); null when the moving path
        /// does not say. Neko Mane King's "in your possession" reads it.
        /// </summary>
        public DuelistState GyEntryPreviousHolder;
    }

    /// <summary>
    /// BattleDestroyedGyRecovery batch (v162): why a card last entered a GY (<see cref="CardInstance.GyEntryReason"/>),
    /// ygopro's REASON_* flags. Runtime-only (never stored in a compiled program).
    /// </summary>
    [System.Flags]
    public enum GyEntryReason
    {
        None = 0,
        Destroyed = 1,
        Battle = 2,
        Effect = 4,
        Cost = 8,
        Discard = 16,
        Rule = 32,
        /// <summary>RecoveryAndPhaseCosts batch: returned from the banished pile to the GY (ygopro REASON_RETURN, Miracle Dig).</summary>
        Return = 64,
    }

    public class FieldZone
    {
        public CardInstance Occupant;

        /// <summary>
        /// LocksAndZones batch (v142): while set, this zone cannot be used — no card can be placed in it
        /// (Dimensionhole: the Monster Zone of the monster it banished, until that monster returns or stops
        /// being banished). Written only by <see cref="TextEffects.LocksAndZonesRuntime"/>.
        /// </summary>
        public CardInstance UnusableFor;

        /// <summary>
        /// Free for a card to be placed in: no card in it, and not made unusable by an effect
        /// (<see cref="UnusableFor"/>). Every placement path (<c>DuelEngine.FirstEmpty</c>, Summons, Sets,
        /// control changes, the zone pickers) reads this.
        /// </summary>
        public bool IsEmpty => Occupant == null && UnusableFor == null;
    }

    /// <summary>
    /// One Soul Exchange grant (TributeSummons batch): <see cref="Target"/> may and must be Tributed as if its
    /// grantee controlled it while <see cref="Holder"/> still controls the same field presence
    /// (<see cref="EntryStamp"/>) and it was not turned face-down since (<see cref="FaceDownStamp"/>).
    /// </summary>
    public sealed class TributeGrant
    {
        public CardInstance Target;
        /// <summary>The duelist who controlled <see cref="Target"/> when it was granted.</summary>
        public DuelistState Holder;
        /// <summary><see cref="CardInstance.FieldEntryStamp"/> of the target at grant time.</summary>
        public int EntryStamp;
        /// <summary><see cref="CardInstance.FaceDownStamp"/> of the target at grant time.</summary>
        public int FaceDownStamp;
    }

    public class DuelistState
    {
        public string Name;
        public bool IsPlayer;
        public int LifePoints = TcgRules.StartingLifePoints;
        /// <summary>
        /// Main Deck, top first. PerCopyDeck batch (v163): a <see cref="DeckPile"/> (the List&lt;int&gt; surface plus
        /// per-copy owner / face-up metadata).
        /// </summary>
        public DeckPile Deck = new();
        public List<CardInstance> Hand = new();
        public List<CardInstance> Graveyard = new();
        /// <summary>Banished (public face-up by default unless scripted face-down).</summary>
        public List<CardInstance> Banished = new();
        /// <summary>Extra Deck card IDs (face-down private knowledge).</summary>
        public List<int> ExtraDeck = new();

        /// <summary>
        /// Face-up Pendulum Monsters in the Extra Deck. A Pendulum Monster sent from the field to the
        /// Graveyard is placed here face-up instead (Rulebook: Pendulum Monsters). These — and only these
        /// Extra Deck cards — may be chosen for a Pendulum Summon. The regular <see cref="ExtraDeck"/> stays
        /// the face-down pile of card IDs.
        /// </summary>
        public List<CardInstance> ExtraDeckFaceUp = new();
        public FieldZone[] MonsterZones = new FieldZone[TcgRules.MonsterZones];
        public FieldZone[] SpellTrapZones = new FieldZone[TcgRules.SpellTrapZones];

        /// <summary>Official Field Spell Zone (one per player).</summary>
        public FieldZone FieldSpellZone = new();

        /// <summary>Pendulum Zones: [0]=left, [1]=right.</summary>
        public FieldZone[] PendulumZones = { new FieldZone(), new FieldZone() };

        /// <summary>
        /// RedirectAndReplace batch (v142): the duel this player is in (set by <see cref="DuelEngine.StartDuel"/>), so a
        /// GY helper that only has the player (a hand discard) still applies Banisher of the Light's "banished
        /// instead" (<see cref="TextEffects.RedirectAndReplaceRuntime.PutInGraveyard"/>). Null for a detached state.
        /// </summary>
        [System.NonSerialized]
        public DuelEngine Engine;

        /// <summary>Normal Summon / Set / Tribute Summon shared once-per-turn flag.</summary>
        public bool NormalSummonUsed;

        /// <summary>Waboku: no battle damage; cannot be destroyed by battle this turn (registered trap).</summary>
        public bool WabokuActive;

        /// <summary>
        /// Mind on Air: this player's hand is revealed for as long as the effect that revealed it stays face-up on
        /// the field. Unlike the turn-scoped flags below it is <b>not</b> cleared at the End Phase — it is a
        /// continuous rule recomputed from the applying source(s) on every board refresh
        /// (<see cref="TextEffects.ContinuousTrapsAndSpiritSupportRuntime.RefreshHandReveal"/>, from
        /// FieldSpellEffects.RefreshBoard), exactly like the negations and the take-control Equips. Read through
        /// <see cref="TextEffects.ContinuousTrapsAndSpiritSupportRuntime.HandRevealed"/> by the UI
        /// (Presentation.GraveyardBrowser / HandBrowser) and the AR hand volume, so the opponent's hand is only
        /// shown with faces where the rules actually make it public.
        /// </summary>
        public bool HandRevealed;

        /// <summary>Threatening Roar family: this player cannot declare an attack this turn.</summary>
        public bool CannotDeclareAttackThisTurn;

        /// <summary>
        /// A Feint Plan family: this player cannot attack face-down monsters this turn.
        /// Set on both duelists at resolve; cleared End Phase (TextEffectRuntime FirePhaseTriggers).
        /// </summary>
        public bool CannotAttackFaceDownMonstersThisTurn;

        /// <summary>
        /// Absolute End family: this player's monsters must attack directly this turn
        /// (attacks become direct attacks).
        /// </summary>
        public bool MustAttackDirectlyThisTurn;

        /// <summary>
        /// PgdSpellTrapBasics batch (v162): Hieroglyph Lithograph — this player's hand size limit for the rest of the
        /// Duel (0 = the rule's 6). A player effect (ygopro EFFECT_HAND_LIMIT registered to the player): it stays after
        /// the card leaves the field. Read by <see cref="TextEffects.ContinuousProtections.HandSizeLimitEndPhaseFor"/>.
        /// </summary>
        public int HandSizeLimitByEffect;

        /// <summary>Crush Card Virus: no battle or effect damage while TurnNumber ≤ this (0 = off).</summary>
        public int NoDamageThroughTurnNumber;

        /// <summary>
        /// Pikeru's Circle of Enchantment: no EFFECT damage while TurnNumber ≤ this (0 = off). Battle damage still
        /// applies, so this is read only in the effect-damage paths — never beside
        /// <see cref="NoDamageThroughTurnNumber"/>.
        /// </summary>
        public int NoEffectDamageThroughTurnNumber;

        /// <summary>Virus linger: remaining opponent End Phases to check draws (0 = off).</summary>
        public int CrushCardLingerEndsRemaining;
        /// <summary>Virus linger destroy ATK threshold (1500 for Crush / DDV).</summary>
        public int VirusLingerDestroyAtk;
        /// <summary>True: linger destroys drawn ATK ≤ threshold (DDV). False: ≥ (Crush).</summary>
        public bool VirusLingerDestroyAtkLeq;

        /// <summary>
        /// Kuriboh / similar: take no battle damage from the current battle only
        /// (cleared at end of that Damage Step). Official: "you take no battle damage from that battle."
        /// </summary>
        public bool PreventBattleDamageThisBattle;

        /// <summary>Fenrir: skip this player's next Draw Phase.</summary>
        public bool SkipNextDrawPhase;

        /// <summary>
        /// Hino-Kagu-Tsuchi (SpiritsAndMisc batch): this player discards their whole hand at the start of the
        /// next Draw Phase (either player's), before the draw. A Draw Phase skipped by a card effect ends it
        /// with no discard (ygopro c75745607: EVENT_PREDRAW, RESET_PHASE+PHASE_DRAW).
        /// </summary>
        public bool DiscardHandAtNextDrawPhase;

        /// <summary>
        /// Stackable skip-next-N Draw Phases (Offerings / Reckless Greed).
        /// Consumed one per BeginTurn Draw Phase. Checked before
        /// <see cref="SkipNextDrawPhase"/> (Fenrir bool). Surgical hook — not a rewrite.
        /// </summary>
        public int SkipNextDrawPhaseCount;

        /// <summary>Solomon's Lawbook: skip this player's next Standby Phase (consumed at that phase).</summary>
        public bool SkipNextStandbyPhase;

        /// <summary>
        /// SpiritMonsters batch, Great Long Nose: this player skips the Battle Phase of their next turn (ygopro
        /// EFFECT_SKIP_BP; a skip inflicted during their own turn applies to their next turn, never the current one).
        /// Consumed when that turn begins (<see cref="TextEffects.SpiritMonstersRuntime.OnTurnStart"/>), which forbids
        /// the Battle Phase through <see cref="BattlePhaseForbiddenTurn"/>; a turn skipped entirely ends it.
        /// </summary>
        public bool SkipNextBattlePhase;

        /// <summary>The name of the card whose effect set <see cref="SkipNextBattlePhase"/> (log text only).</summary>
        public string SkipNextBattlePhaseSource;

        /// <summary>
        /// SpiritMonsters batch, Maharaghi: during this player's next Draw Phase, before the draw, they look at the top
        /// card of their Deck and put it back on the top or the bottom (ygopro EVENT_PREDRAW, RESET_PHASE+PHASE_DRAW+
        /// RESET_SELF_TURN). A bool: a second registration before that Draw Phase adds nothing (the Lua flag effect).
        /// Consumed by <see cref="TextEffects.SpiritMonstersRuntime.OnDrawPhaseStart"/>.
        /// </summary>
        public bool LookAtTopCardAtNextDrawPhase;

        /// <summary>The name of the card whose effect set <see cref="LookAtTopCardAtNextDrawPhase"/> (log and prompt text only).</summary>
        public string LookAtTopCardSource;

        /// <summary>
        /// HandDisruption batch (v142), Gamble's wrong call: this player's next turn is skipped entirely (it
        /// begins and ends with no phases; the opponent takes the following turn). A bool, not a count: ygopro
        /// EFFECT_SKIP_TURN resets at the first skipped turn, so two wrong calls skip one turn. Consumed by
        /// <see cref="TextEffects.HandDisruptionRuntime.ConsumeSkippedTurn"/> in DuelEngine.BeginTurn.
        /// </summary>
        public bool SkipNextTurn;

        /// <summary>
        /// Sangan / Witch: card names that cannot be activated (cards or effects)
        /// for the rest of the turn they were added from Deck. Scoped by
        /// <see cref="CannotActivateCardNamesTurnNumber"/> == engine.TurnNumber.
        /// Surgical flag — not a DuelEngine rewrite.
        /// </summary>
        public System.Collections.Generic.List<string> CannotActivateCardNamesThisTurn;

        /// <summary>TurnNumber when CannotActivateCardNamesThisTurn was last written.</summary>
        public int CannotActivateCardNamesTurnNumber;

        /// <summary>
        /// SimpleTargetUtility batch (v173) — Cost Down: while equal to the engine's TurnNumber, every
        /// monster this player draws or holds in hand is stamped
        /// (<see cref="CardInstance.LevelReducedUntilTurn"/> / <see cref="CardInstance.LevelReducedAmount"/>,
        /// stamped for the rest of this turn by <c>SimpleTargetUtilityRuntime.ApplyLevelStamps</c>).
        /// </summary>
        public int LevelReducedHandUntilTurn;
        /// <summary>SimpleTargetUtility batch (v173): the hand-wide Level reduction Cost Down is stamping this turn.</summary>
        public int LevelReducedHandAmount;

        /// <summary>
        /// TurnNegationTraps batch (v141): card families this player negated on the whole field, keyed by
        /// family (<see cref="TextEffects.TurnNegationTrapsRuntime.FieldSpell"/> …), value = the TurnNumber
        /// the negation lasts through. Read by <see cref="TextEffects.TurnNegationTrapsRuntime"/> only.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, int> CardFamiliesNegatedThroughTurn;

        /// <summary>Pendulum Summon once per turn (when Pendulum is supported).</summary>
        public bool PendulumSummonedThisTurn;

        // ── TributeSummons batch (v141): Soul Exchange ──

        /// <summary>
        /// Soul Exchange grants of this player, one per resolved copy: the opponent's monster they must Tribute,
        /// as if they controlled it, whenever they Tribute this turn. Cleared when a turn begins. Read them through
        /// <see cref="TextEffects.TributeSummonsRuntime.ForcedTributes"/>, never directly.
        /// </summary>
        public readonly List<TributeGrant> MustTributeGrants = new List<TributeGrant>();
        /// <summary>
        /// Soul Exchange oath: this player cannot conduct their Battle Phase during the turn with this number
        /// (0 = none). Checked by <c>DuelEngine.CanConductBattlePhase</c>.
        /// </summary>
        public int BattlePhaseForbiddenTurn;

        /// <summary>
        /// EmptyHandStandbyDraw: this player's hand and face-up cards at the end of the opponent's last End Phase.
        /// Null until the opponent has ended a turn. Written only by
        /// <see cref="TextEffects.EmptyHandStandbyDrawRuntime.RecordEndOfEndPhase"/>.
        /// </summary>
        public TextEffects.EndPhaseRecord EndOfOpponentsLastEndPhase;

        // ── SpellTrapDestroyedResponse batch (v142): Gust / Driving Snow / Weather Report ──

        /// <summary>
        /// This player's Spell / Trap Cards destroyed and sent from the field to the GY by a card effect their
        /// opponent controls during game turn <see cref="SpellTrapsDestroyedByOpponentTurn"/>, not yet offered
        /// the response window. Written and read only by <see cref="TextEffects.SpellTrapDestroyedResponseRuntime"/>.
        /// </summary>
        public readonly List<CardInstance> SpellTrapsDestroyedByOpponent = new List<CardInstance>();
        /// <summary>Game turn of <see cref="SpellTrapsDestroyedByOpponent"/> (older entries are dropped).</summary>
        public int SpellTrapsDestroyedByOpponentTurn;
        /// <summary>Phase of <see cref="SpellTrapsDestroyedByOpponent"/>: the window opens in that phase or never.</summary>
        public DuelPhase SpellTrapsDestroyedByOpponentPhase;
        /// <summary>
        /// The destroyed cards the open <see cref="ResponseTiming.SpellTrapDestroyedByOpponent"/> window answers
        /// (Gust needs a Spell among them, Driving Snow a Trap). Cleared when that window closes.
        /// </summary>
        public readonly List<CardInstance> SpellTrapsDestroyedWindow = new List<CardInstance>();
        /// <summary>
        /// Weather Report: the game turns in which this player may conduct their Battle Phase twice. Written and
        /// read through <see cref="TextEffects.SpellTrapDestroyedResponseRuntime"/>
        /// (GrantBattlePhaseTwice / SecondBattlePhaseAvailable).
        /// </summary>
        public readonly List<int> BattlePhaseTwiceTurns = new List<int>();
        /// <summary>The game turn in which this player entered their second Battle Phase (0 = none).</summary>
        public int SecondBattlePhaseTurn;

        // ── LocksAndZones batch (v142): Cold Wave, Dimensionhole ──

        /// <summary>
        /// Cold Wave activated by this player: through the turn with this number (0 = none), neither player
        /// can activate Spell/Trap Cards or their effects, nor Set Spell/Trap Cards. Read through
        /// <see cref="TextEffects.LocksAndZonesRuntime.SpellTrapLockActive"/>, never directly.
        /// </summary>
        public int SpellTrapPlayLockedThroughTurn;

        /// <summary>
        /// Dimensionhole resolutions of this player: monsters banished until this player's next Standby Phase,
        /// with the zone each one left. Read through <see cref="TextEffects.LocksAndZonesRuntime"/> only.
        /// </summary>
        public readonly List<TextEffects.TemporaryBanishment> TemporaryBanishments =
            new List<TextEffects.TemporaryBanishment>();

        /// <summary>
        /// Soft residual Fake Trap: InstanceIds of Trap Cards you control Soft-protected from
        /// destroy by sources in SoftFakeTrapAgainstSourceIds. Soft ContinuousProtections
        /// destroy-replacement redirect (Prefer NO DE rewrite). Soft ≠ hard.
        /// </summary>
        public System.Collections.Generic.HashSet<int> SoftFakeTrapProtectedTrapIds;

        /// <summary>
        /// Soft residual Fake Trap: InstanceIds of opponent effect sources whose destroy of
        /// SoftFakeTrapProtectedTrapIds is Soft-redirected (Fake Trap died instead).
        /// </summary>
        public System.Collections.Generic.HashSet<int> SoftFakeTrapAgainstSourceIds;

        public DuelistState(string name, bool isPlayer)
        {
            Name = name;
            IsPlayer = isPlayer;
            for (var i = 0; i < MonsterZones.Length; i++)
            {
                MonsterZones[i] = new FieldZone();
                SpellTrapZones[i] = new FieldZone();
            }
        }

        public int DeckCount => Deck.Count;
        public int HandCount => Hand.Count;

        /// <summary>
        /// Rearrange private hand order (official: you may freely rearrange your hand).
        /// Fisher–Yates on the match's <see cref="DuelEngine.Rng"/> (R1: hand order feeds random discards,
        /// so it must replay); no-op if fewer than 2 cards.
        /// </summary>
        public void ShuffleHand(DuelRng rng)
        {
            if (Hand == null || Hand.Count < 2 || rng == null) return;
            rng.Shuffle(Hand);
        }

        /// <summary>
        /// Move a hand card to a new index (0 = leftmost). Official private rearrange.
        /// <paramref name="newIndex"/> is the target index among the remaining cards
        /// after this card is removed (same semantics as UI insert-from-pointer).
        /// Returns true if order changed.
        /// </summary>
        public bool MoveHandCard(CardInstance card, int newIndex)
        {
            if (Hand == null || card == null || Hand.Count < 2) return false;
            var old = Hand.IndexOf(card);
            if (old < 0) return false;

            // newIndex is among remaining (Count-1) cards after removal → final list size Count
            newIndex = Mathf.Clamp(newIndex, 0, Hand.Count - 1);
            Hand.RemoveAt(old);
            newIndex = Mathf.Clamp(newIndex, 0, Hand.Count);
            // No-op if we re-insert at the same logical slot we left
            if (newIndex == old)
            {
                Hand.Insert(old, card);
                return false;
            }

            Hand.Insert(newIndex, card);
            return true;
        }

        public int MonsterCount
        {
            get
            {
                var n = 0;
                foreach (var z in MonsterZones)
                    if (z.Occupant != null) n++;
                return n;
            }
        }

        public IEnumerable<CardInstance> MonstersOnField()
        {
            foreach (var z in MonsterZones)
                if (z.Occupant != null)
                    yield return z.Occupant;
        }

        public bool TryFindMonster(CardInstance card, out int zoneIndex)
        {
            for (var i = 0; i < MonsterZones.Length; i++)
            {
                if (MonsterZones[i].Occupant == card)
                {
                    zoneIndex = i;
                    return true;
                }
            }

            zoneIndex = -1;
            return false;
        }

        public bool TryFindSpellTrap(CardInstance card, out int zoneIndex)
        {
            for (var i = 0; i < SpellTrapZones.Length; i++)
            {
                if (SpellTrapZones[i].Occupant == card)
                {
                    zoneIndex = i;
                    return true;
                }
            }

            zoneIndex = -1;
            return false;
        }

        public IEnumerable<CardInstance> SpellTrapsOnField()
        {
            foreach (var z in SpellTrapZones)
                if (z.Occupant != null)
                    yield return z.Occupant;
            // Official: Field Spells are Spells on the field (MST / Heavy Storm / "Umi" checks).
            if (FieldSpellZone?.Occupant != null)
                yield return FieldSpellZone.Occupant;
        }
    }
}
