using System;
using UnityEngine;

namespace WRLDZ.Data
{
    /// <summary>One card definition from StreamingAssets/Cards/cards_db.json.</summary>
    [Serializable]
    public class CardDef
    {
        public int id;
        public string name;
        public string type;
        public string frameType;
        public string desc;
        public int atk;   // -1 if N/A
        public int def;   // -1 if N/A
        public int level;
        public string race;
        public string attribute;
        public string archetype;

        /// <summary>
        /// Pendulum Scale printed on the blue (left) scale. 0 = not printed (non-Pendulum card).
        /// Pre-Link Pendulum Monsters carry this in <c>eras/prelink_monsters.json</c> (and, once imported,
        /// in <c>cards_db.json</c>); the engine never invents a scale.
        /// </summary>
        public int scaleLeft;

        /// <summary>Pendulum Scale printed on the red (right) scale. 0 = not printed.</summary>
        public int scaleRight;

        public bool IsMonster =>
            !string.IsNullOrEmpty(type) &&
            type.IndexOf("Monster", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsSpell =>
            type != null && type.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsTrap =>
            type != null && type.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Spell/Trap subtype lives in <see cref="race"/> for this DB
        /// (Normal, Continuous, Equip, Field, Quick-Play, Counter, Ritual).
        /// </summary>
        public bool IsContinuousSpellOrTrap
        {
            get
            {
                if (!IsSpell && !IsTrap) return false;
                var r = race ?? "";
                return r.IndexOf("Continuous", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool IsFieldSpell
        {
            get
            {
                if (!IsSpell) return false;
                var r = race ?? "";
                return r.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool IsEquipSpell
        {
            get
            {
                if (!IsSpell) return false;
                var r = race ?? "";
                return r.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>
        /// Rules: this Spell/Trap remains in its zone after activation
        /// (Continuous / Field / Equip). Arena holograms stand upright with the
        /// card back to the controller; the disk reseats the physical card.
        /// </summary>
        public bool StaysFlatOnFieldWhenActivated =>
            IsContinuousSpellOrTrap || IsFieldSpell || IsEquipSpell;

        public bool IsExtraDeck =>
            type != null && (
                type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Synchro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("XYZ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Fusion Monster (frame or type says Fusion).</summary>
        public bool IsFusionMonster =>
            IsMonster && type != null &&
            type.IndexOf("Fusion", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Synchro Monster.</summary>
        public bool IsSynchroMonster =>
            IsMonster && type != null &&
            type.IndexOf("Synchro", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Xyz Monster (cards_db prints "XYZ"; the check is case-insensitive).</summary>
        public bool IsXyzMonster =>
            IsMonster && type != null &&
            type.IndexOf("XYZ", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Link Monster (post pre-Link era; present so the check stays structural).</summary>
        public bool IsLinkMonster =>
            IsMonster && type != null &&
            type.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// A Tuner monster. In the cards_db / YGOPRODECK convention the Type line is a single string
        /// ("Synchro Tuner Monster", "Tuner Monster"), so "Tuner" is part of <see cref="type"/> — never
        /// <see cref="race"/>, which holds the primary Type (Warrior, Spellcaster, …).
        /// </summary>
        public bool IsTuner =>
            IsMonster && type != null &&
            type.IndexOf("Tuner", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Pendulum Monster (Pendulum frame). A card can be both Pendulum and Extra Deck
        /// (Pendulum Fusion / Synchro / Xyz), so this is independent of <see cref="IsExtraDeck"/>.
        /// </summary>
        public bool IsPendulumMonster =>
            IsMonster && (
                (type != null && type.IndexOf("Pendulum", StringComparison.OrdinalIgnoreCase) >= 0) ||
                (frameType != null && frameType.IndexOf("pendulum", StringComparison.OrdinalIgnoreCase) >= 0));

        /// <summary>Effective Pendulum Scale for the left zone (0 when not printed / not a Pendulum card).</summary>
        public int PendulumScaleLeft => scaleLeft > 0 ? scaleLeft : 0;

        /// <summary>Effective Pendulum Scale for the right zone (0 when not printed / not a Pendulum card).</summary>
        public int PendulumScaleRight => scaleRight > 0 ? scaleRight : 0;

        /// <summary>True when either Pendulum Scale is printed (a usable scale).</summary>
        public bool HasPendulumScales => PendulumScaleLeft > 0 || PendulumScaleRight > 0;

        /// <summary>
        /// Rank of an Xyz Monster. The Xyz Rank is carried in <see cref="level"/> (the ocgcore CDB convention),
        /// so Rank == Level. Valid only when <see cref="IsXyzMonster"/>.
        /// </summary>
        public int Rank => level;

        public bool IsRitualMonster =>
            type != null && type.IndexOf("Ritual", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Ritual Spell (race = Ritual). Distinct from <see cref="IsRitualMonster"/>.</summary>
        public bool IsRitualSpell =>
            IsSpell && race != null &&
            race.IndexOf("Ritual", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Vanilla monster (no effect text mechanics) — Normal Monster frame.</summary>
        public bool IsNormalMonster =>
            IsMonster && !IsExtraDeck &&
            ((frameType != null &&
              frameType.Equals("normal", StringComparison.OrdinalIgnoreCase)) ||
             (type != null &&
              type.IndexOf("Normal Monster", StringComparison.OrdinalIgnoreCase) >= 0));

        /// <summary>Monster with an effect (including Flip / Spirit / Union / Toon / Ritual).</summary>
        public bool IsEffectMonster =>
            IsMonster && !IsNormalMonster;

        /// <summary>Spell/Trap subtype from <see cref="race"/> (Normal, Continuous, Equip…).</summary>
        public string SpellTrapKind =>
            !IsSpell && !IsTrap ? "" : (race ?? "").Trim();

        /// <summary>May be Normal Summoned/Set under structural rules (not Extra/Ritual).</summary>
        public bool CanBeNormalSummonedOrSet =>
            IsMonster && !IsExtraDeck && !IsRitualMonster && level >= 1;

        public string StatLine
        {
            get
            {
                if (!IsMonster) return type ?? "";
                var a = atk < 0 ? "?" : atk.ToString();
                var d = def < 0 ? "?" : def.ToString();
                return $"Lv{level}  {a}/{d}  {attribute} {race}";
            }
        }
    }

    [Serializable]
    public class CardDatabaseFile
    {
        public CardDef[] cards;
    }

    [Serializable]
    public class DeckCardEntry
    {
        public int id;
        public string name;
        public int qty;
    }

    [Serializable]
    public class DeckFile
    {
        public string name;
        public string format;
        public DeckCardEntry[] main;
        public DeckCardEntry[] extra;
        public DeckCardEntry[] side;
    }
}
