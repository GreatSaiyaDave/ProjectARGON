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
        public bool FaceUp = true;
        public BattlePosition Position = BattlePosition.Attack;

        /// <summary>Normal Summoned or Flip Summoned this turn (restricts position change).</summary>
        public bool SummonedThisTurn;

        /// <summary>Normal Set this turn (face-down DEF) — cannot Flip Summon same turn in TCG.</summary>
        public bool SetThisTurn;

        public bool AttackedThisTurn;

        /// <summary>Successful attack declarations resolved this Battle Phase (extra-attack cards).</summary>
        public int AttacksDeclaredThisTurn;

        public void ClearAttackFlags()
        {
            AttackedThisTurn = false;
            AttacksDeclaredThisTurn = 0;
        }

        public void NoteAttackResolved()
        {
            AttacksDeclaredThisTurn++;
            AttackedThisTurn = true;
        }

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

        /// <summary>For continuous Spells with turn counters (only when official script sets this).</summary>
        public int ContinuousTurnsRemaining;

        /// <summary>Spell counters / other counters — only modified by registered effects.</summary>
        public int Counters;

        /// <summary>Xyz materials under this monster.</summary>
        public System.Collections.Generic.List<CardInstance> OverlayMaterials;

        /// <summary>ATK/DEF modifiers from continuous effects (registered only).</summary>
        public int AtkModifier;
        public int DefModifier;

        /// <summary>Until End Phase (Bark of Dark Ruler, Mask of Weakness). Not cleared by aura refresh.</summary>
        public int UntilEndOfTurnAtk;
        public int UntilEndOfTurnDef;
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
        /// <summary>Turn number when this copy last left the field for the GY (0 = never).</summary>
        public int SentFromFieldTurnNumber;

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

        /// <summary>Controller takes this much damage when this Token is destroyed.</summary>
        public int TokenDestroyedDamage;

        /// <summary>
        /// Temporary Special Summon: destroy this monster during the End Phase of the
        /// turn whose number this equals (Archfiend's Roar). -1 = permanent.
        /// </summary>
        public int TempDestroyOnEndOfTurn = -1;

        /// <summary>Max Spell Counters (Breaker = 1, Library = 3). 0 = no cap.</summary>
        public int SpellCounterMax;

        /// <summary>Union / Relinquished: this card is equipped to EquippedTo.</summary>
        public CardInstance EquippedTo;

        /// <summary>This monster's current controller is from an Equip take-control (Falling Down).</summary>
        public bool TakenByEquipControl;

        /// <summary>Monsters/Unions currently equipped to this card.</summary>
        public readonly List<CardInstance> Equips = new();

        /// <summary>Set only by official continuous effect scripts — never inferred from free text.</summary>
        public bool HasPiercing;
        public bool CannotBeDestroyedByBattle;
        public bool IsToken;
        public bool IsNegated; // effect negated while face-up

        /// <summary>
        /// Soul Exchange: this copy may be Tributed this turn by
        /// <see cref="TributableByOpponent"/> as if they controlled it.
        /// </summary>
        public DuelistState TributableByOpponent;

        /// <summary>Level change from face-up Field Spells / registered continuous (e.g. A Legendary Ocean −1).</summary>
        public int LevelModifier;

        /// <summary>
        /// Rules name override from a name condition (A Legendary Ocean is always treated as "Umi").
        /// Display <see cref="Name"/> stays the printed title.
        /// </summary>
        public string TreatedAsName;

        public int CurrentAtk =>
            AtkBecomesZeroThisCalculation
                ? 0
                : Def != null && Def.atk >= 0
                    ? System.Math.Max(0, Def.atk + AtkModifier + UntilEndOfTurnAtk + LingeringAtkModifier)
                    : 0;
        public int CurrentDef =>
            Def != null && Def.def >= 0
                ? System.Math.Max(0, Def.def + DefModifier + UntilEndOfTurnDef + LingeringDefModifier)
                : 0;
        public string Name => Def?.name ?? $"#{CardId}";

        public string RulesName =>
            !string.IsNullOrEmpty(TreatedAsName) ? TreatedAsName : Name;

        public bool IsNamed(string n) =>
            !string.IsNullOrEmpty(n) &&
            (string.Equals(Name, n, System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(RulesName, n, System.StringComparison.OrdinalIgnoreCase));

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
    }

    public class FieldZone
    {
        public CardInstance Occupant;
        public bool IsEmpty => Occupant == null;
    }

    public class DuelistState
    {
        public string Name;
        public bool IsPlayer;
        public int LifePoints = TcgRules.StartingLifePoints;
        public List<int> Deck = new();
        public List<CardInstance> Hand = new();
        public List<CardInstance> Graveyard = new();
        /// <summary>Banished (public face-up by default unless scripted face-down).</summary>
        public List<CardInstance> Banished = new();
        /// <summary>Extra Deck card IDs (face-down private knowledge).</summary>
        public List<int> ExtraDeck = new();
        public FieldZone[] MonsterZones = new FieldZone[TcgRules.MonsterZones];
        public FieldZone[] SpellTrapZones = new FieldZone[TcgRules.SpellTrapZones];

        /// <summary>Official Field Spell Zone (one per player).</summary>
        public FieldZone FieldSpellZone = new();

        /// <summary>Pendulum Zones: [0]=left, [1]=right.</summary>
        public FieldZone[] PendulumZones = { new FieldZone(), new FieldZone() };

        /// <summary>Normal Summon / Set / Tribute Summon shared once-per-turn flag.</summary>
        public bool NormalSummonUsed;

        /// <summary>Waboku: no battle damage; cannot be destroyed by battle this turn (registered trap).</summary>
        public bool WabokuActive;

        /// <summary>
        /// Absolute End family: this player's monsters must attack directly this turn
        /// (attacks become direct attacks).
        /// </summary>
        public bool MustAttackDirectlyThisTurn;

        /// <summary>
        /// Kuriboh / similar: take no battle damage from the current battle only
        /// (cleared at end of that Damage Step). Official: "you take no battle damage from that battle."
        /// </summary>
        public bool PreventBattleDamageThisBattle;

        /// <summary>Fenrir: skip this player's next Draw Phase.</summary>
        public bool SkipNextDrawPhase;

        /// <summary>Soul Exchange: this player cannot conduct Battle Phase this turn.</summary>
        public bool SkipBattlePhaseThisTurn;

        /// <summary>
        /// Soul Exchange: if this player Tributes, they must include this monster
        /// (opponent's, as if they controlled it).
        /// </summary>
        public CardInstance MustTributeAsIfControlled;

        /// <summary>Pendulum Summon once per turn (when Pendulum is supported).</summary>
        public bool PendulumSummonedThisTurn;

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
        /// Fisher–Yates; no-op if fewer than 2 cards.
        /// </summary>
        public void ShuffleHand()
        {
            if (Hand == null || Hand.Count < 2) return;
            for (var i = Hand.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (Hand[i], Hand[j]) = (Hand[j], Hand[i]);
            }
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
