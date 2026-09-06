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
