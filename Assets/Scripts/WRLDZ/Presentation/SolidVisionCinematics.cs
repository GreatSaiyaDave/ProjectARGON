using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// How a monster's attack crosses the field (SOLID_VISION_BEHAVIOR.md §4.2). Presentation only: the rules
    /// engine never reads it.
    /// </summary>
    public enum AttackArchetype
    {
        /// <summary>Breath / energy stream held from the attacker to the target (Blue-Eyes, Summoned Skull).</summary>
        Beam,
        /// <summary>A spell orb gathered at the attacker, lobbed across, bursting on arrival (Dark Magician).</summary>
        Blast,
        /// <summary>Melee: the attacker's light streaks to the target and cuts it (warriors, beasts).</summary>
        Lunge,
        /// <summary>A volley of bolts in quick succession (machines, rock, pyro).</summary>
        Projectile,
        /// <summary>A cloud of small motes converging on the target (insects, plants).</summary>
        Swarm,
        /// <summary>A wave rolling along the field into the target (aqua, fish, sea serpents).</summary>
        Surge
    }

    /// <summary>
    /// Per-card Solid Vision data (SOLID_VISION_BEHAVIOR.md §6): the sourced anime attack name for Mr. Referobot's
    /// named call-out, and the look of the card's summon and attack. One table,
    /// StreamingAssets/WRLDZ/Cinematics/cinematics_v1.json; a card without an entry falls back to its race
    /// (attack archetype) and Attribute (tint). Attack names are never invented: no entry, no call-out.
    /// </summary>
    public static class SolidVisionCinematics
    {
        public const string RelativePath = "WRLDZ/Cinematics/cinematics_v1.json";
        const string Tag = "[WRLDZ SolidVision]";

        [Serializable]
        public sealed class Entry
        {
            public int id;
            public string name;
            /// <summary>English dub attack name.</summary>
            public string attack;
            /// <summary>English rendering of the original Japanese attack name.</summary>
            public string attackOriginal;
            /// <summary><see cref="AttackArchetype"/> name; empty = by race.</summary>
            public string archetype;
            /// <summary>#RRGGBB; empty = by Attribute.</summary>
            public string tint;
            public string source;
        }

        [Serializable]
        public sealed class TableFile
        {
            public string schema;
            public string notes;
            public Entry[] cards;
        }

        static Dictionary<int, Entry> _byId;

        static readonly Dictionary<string, AttackArchetype> ByRace = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Dragon"] = AttackArchetype.Beam,
            ["Thunder"] = AttackArchetype.Beam,
            ["Wyrm"] = AttackArchetype.Beam,
            ["Divine-Beast"] = AttackArchetype.Beam,
            ["Spellcaster"] = AttackArchetype.Blast,
            ["Fiend"] = AttackArchetype.Blast,
            ["Fairy"] = AttackArchetype.Blast,
            ["Zombie"] = AttackArchetype.Blast,
            ["Psychic"] = AttackArchetype.Blast,
            ["Warrior"] = AttackArchetype.Lunge,
            ["Beast"] = AttackArchetype.Lunge,
            ["Beast-Warrior"] = AttackArchetype.Lunge,
            ["Dinosaur"] = AttackArchetype.Lunge,
            ["Winged Beast"] = AttackArchetype.Lunge,
            ["Reptile"] = AttackArchetype.Lunge,
            ["Machine"] = AttackArchetype.Projectile,
            ["Rock"] = AttackArchetype.Projectile,
            ["Pyro"] = AttackArchetype.Projectile,
            ["Insect"] = AttackArchetype.Swarm,
            ["Plant"] = AttackArchetype.Swarm,
            ["Aqua"] = AttackArchetype.Surge,
            ["Fish"] = AttackArchetype.Surge,
            ["Sea Serpent"] = AttackArchetype.Surge,
        };

        public static void Invalidate() => _byId = null;

        public static bool TryGet(int cardId, out Entry entry)
        {
            Load();
            return _byId.TryGetValue(cardId, out entry) && entry != null;
        }

        static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, Entry>();
            if (!StreamingFiles.TryReadAllText(RelativePath, out var json))
            {
                Debug.LogWarning(Tag + " Missing " + StreamingFiles.FullPathForLog(RelativePath) +
                                 " — no named attacks; race / Attribute looks only.");
                return;
            }

            try
            {
                var file = JsonUtility.FromJson<TableFile>(json);
                if (file?.cards == null) return;
                foreach (var e in file.cards)
                    if (e != null && e.id > 0)
                        _byId[e.id] = e;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(Tag + " Parse failed: " + ex.Message);
            }
        }

        /// <summary>The attack's anime name (dub first, then the original), or null when none is recorded.</summary>
        public static string AttackName(int cardId)
        {
            if (!TryGet(cardId, out var e)) return null;
            if (!string.IsNullOrWhiteSpace(e.attack)) return e.attack.Trim();
            return string.IsNullOrWhiteSpace(e.attackOriginal) ? null : e.attackOriginal.Trim();
        }

        /// <summary>
        /// Mr. Referobot's named call-out as <paramref name="attacker"/> attacks ("Blue-Eyes White Dragon — White
        /// Lightning!"), or null for a monster with no recorded attack name.
        /// </summary>
        public static string AttackCall(CardInstance attacker)
        {
            if (attacker == null) return null;
            var name = AttackName(attacker.CardId);
            return name == null ? null : attacker.Name + " — " + name + "!";
        }

        /// <summary>The table's archetype for this card, else its race's; Beam (the legacy look) when unknown.</summary>
        public static AttackArchetype ArchetypeFor(CardDef def)
        {
            if (def == null) return AttackArchetype.Beam;
            if (TryGet(def.id, out var e) && !string.IsNullOrWhiteSpace(e.archetype) &&
                Enum.TryParse(e.archetype.Trim(), true, out AttackArchetype fromTable))
                return fromTable;
            return !string.IsNullOrEmpty(def.race) && ByRace.TryGetValue(def.race.Trim(), out var byRace)
                ? byRace
                : AttackArchetype.Beam;
        }

        /// <summary>Light colour of a monster's summon and attack: the table's tint, else its Attribute's.</summary>
        public static Color TintFor(CardDef def)
        {
            if (def == null) return AttributeColor(null);
            if (TryGet(def.id, out var e) && !string.IsNullOrWhiteSpace(e.tint) &&
                ColorUtility.TryParseHtmlString(e.tint.Trim(), out var c))
                return new Color(c.r, c.g, c.b, 1f);
            return AttributeColor(def.attribute);
        }

        /// <summary>Attribute palette for Ka light (WATER is deeper than the player's cyan side colour).</summary>
        public static Color AttributeColor(string attribute)
        {
            switch ((attribute ?? "").Trim().ToUpperInvariant())
            {
                case "DARK": return Rgb(0x9B, 0x5C, 0xFF);
                case "LIGHT": return Rgb(0xFF, 0xE3, 0x8A);
                case "WATER": return Rgb(0x3D, 0x8B, 0xFF);
                case "FIRE": return Rgb(0xFF, 0x6A, 0x1F);
                case "EARTH": return Rgb(0xD9, 0xA4, 0x41);
                case "WIND": return Rgb(0x5C, 0xE6, 0x8A);
                case "DIVINE": return Rgb(0xFF, 0xF2, 0xB0);
                default: return new Color(0.85f, 0.95f, 1f, 1f);
            }
        }

        /// <summary>0 for a Level 1 monster, 1 at Level 8 and above: how big and long its summon light is.</summary>
        public static float Weight(CardDef def) =>
            def == null ? 0f : Mathf.Clamp01((Mathf.Max(1, def.level) - 1) / 7f);

        /// <summary>
        /// 0–1 heft of an attack: the attacker's current Level and ATK together (Blue-Eyes = 1, Kuriboh ≈ 0).
        /// </summary>
        public static float AttackWeight(CardInstance attacker)
        {
            if (attacker == null) return 0.4f;
            var byLevel = Mathf.Clamp01((Mathf.Max(1, attacker.Level) - 1) / 7f);
            var byAtk = Mathf.Clamp01(attacker.CurrentAtk / 3000f);
            return Mathf.Clamp01(0.5f * byLevel + 0.5f * byAtk);
        }

        static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
