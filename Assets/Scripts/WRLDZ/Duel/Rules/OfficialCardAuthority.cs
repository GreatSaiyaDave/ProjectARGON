using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Single source of truth for card text: official card database <c>desc</c>
    /// (YGOPRODECK / Konami text as mirrored in StreamingAssets — equivalent to Yugipedia current text).
    ///
    /// Policy (requirement 1):
    /// · Never invent, simplify, or approximate an effect at resolution time.
    /// · Display and legality always use <see cref="OfficialText"/>.
    /// · Resolution only runs for cards registered in <see cref="OfficialEffectRegistry"/>
    ///   whose script is documented against that official text.
    /// · Unregistered activated effects are ILLEGAL to resolve (activation rejected),
    ///   not partially faked.
    /// </summary>
    public static class OfficialCardAuthority
    {
        public const string TextSource = "StreamingAssets/Cards/cards_db.json (official Konami/Yugipedia-aligned card text)";
        public const string RulingsReference = "https://yugipedia.com/ — Rulebook + card pages + rulings";

        static readonly Dictionary<string, Dictionary<int, EraTextCard>> EraTextById =
            new Dictionary<string, Dictionary<int, EraTextCard>>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> EraTextTried =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string OfficialText(CardDef def) =>
            def?.desc?.Trim() ?? string.Empty;

        public static string OfficialText(CardInstance card) =>
            OfficialText(card?.Def);

        /// <summary>
        /// Official text for an ERAZ band. Uses <c>StreamingAssets/WRLDZ/eras/text/&lt;era&gt;.json</c>
        /// when present; otherwise equals <see cref="OfficialText(CardDef)"/>.
        /// </summary>
        public static string OfficialText(CardDef def, string eraId)
        {
            if (TryGetEraCard(def?.id ?? 0, eraId, out var entry) && entry != null &&
                !string.IsNullOrEmpty(entry.desc))
                return entry.desc.Trim();
            return OfficialText(def);
        }

        /// <summary>Stable hash of official text for registry version checks.</summary>
        public static string TextHash(CardDef def)
        {
            var t = OfficialText(def);
            return HashOf(t);
        }

        /// <summary>
        /// Text hash for an ERAZ band. Prefers snapshot <c>textHash</c>, else hashes snapshot
        /// <c>desc</c>; when no snapshot file/entry, equals <see cref="TextHash(CardDef)"/>.
        /// </summary>
        public static string TextHash(CardDef def, string eraId)
        {
            if (TryGetEraCard(def?.id ?? 0, eraId, out var entry) && entry != null)
            {
                if (!string.IsNullOrEmpty(entry.textHash))
                    return entry.textHash;
                if (!string.IsNullOrEmpty(entry.desc))
                    return HashOf(entry.desc.Trim());
            }
            return TextHash(def);
        }

        static string HashOf(string t)
        {
            if (string.IsNullOrEmpty(t)) return "empty";
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(t));
            var sb = new StringBuilder(16);
            for (var i = 0; i < 8 && i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        static bool TryGetEraCard(int cardId, string eraId, out EraTextCard entry)
        {
            entry = null;
            if (cardId <= 0 || string.IsNullOrEmpty(eraId)) return false;
            var map = LoadEraText(eraId);
            if (map == null) return false;
            return map.TryGetValue(cardId, out entry) && entry != null;
        }

        static Dictionary<int, EraTextCard> LoadEraText(string eraId)
        {
            if (EraTextById.TryGetValue(eraId, out var cached))
                return cached;
            if (EraTextTried.Contains(eraId))
                return null;
            EraTextTried.Add(eraId);

            var path = Path.Combine(
                Application.streamingAssetsPath, "WRLDZ", "eras", "text", eraId + ".json");
            if (!File.Exists(path))
                return null;

            try
            {
                var wrapper = JsonUtility.FromJson<EraTextFile>(File.ReadAllText(path));
                var map = new Dictionary<int, EraTextCard>();
                if (wrapper?.cards != null)
                {
                    foreach (var c in wrapper.cards)
                    {
                        if (c == null || c.id <= 0) continue;
                        map[c.id] = c;
                    }
                }
                EraTextById[eraId] = map;
                return map;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WRLDZ ERAZ] text snapshot load failed ({eraId}): " + ex.Message);
                return null;
            }
        }

        [Serializable]
        class EraTextFile
        {
            public EraTextCard[] cards;
        }

        [Serializable]
        class EraTextCard
        {
            public int id;
            public string desc;
            public string textHash;
        }

        public static string WikiCardUrl(int cardId) =>
            $"https://yugipedia.com/wiki/Card_Gallery:#{cardId}"; // name-based pages preferred; ID for tooling

        /// <summary>
        /// True if the card has flavor-only / Normal Monster text with no activated effect.
        /// Normal Monsters have no effects to invent.
        /// </summary>
        public static bool IsNormalMonsterNoEffect(CardDef def) =>
            def != null && def.IsMonster &&
            def.type != null &&
            def.type.IndexOf("Normal Monster", StringComparison.OrdinalIgnoreCase) >= 0 &&
            def.type.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) < 0 &&
            def.type.IndexOf("Pendulum", StringComparison.OrdinalIgnoreCase) < 0;

        /// <summary>
        /// True for monsters with <b>no activatable effect text</b> to script:
        /// Normal Monsters, and Extra Deck monsters that are not Effect monsters
        /// (e.g. classic Fusion materials-only like Black Skull Dragon, Gaia the Dragon Champion).
        /// Their "text" is materials / treat-as boilerplate only — fusion procedure is structural.
        /// </summary>
        public static bool HasNoActivatableEffect(CardDef def)
        {
            if (def == null) return true;
            if (IsNormalMonsterNoEffect(def)) return true;
            if (!def.IsMonster) return false;

            // Non-Effect Extra Deck monsters (Fusion / Synchro / Xyz / Link without Effect)
            if (def.IsExtraDeck)
            {
                var t = def.type ?? "";
                var f = def.frameType ?? "";
                // Explicit Effect in type or frame → has effect text that needs a script
                if (ContainsIgnoreCase(t, "Effect") || ContainsIgnoreCase(f, "effect"))
                    return false;
                // Classic non-effect Fusion Monster, etc.
                if (ContainsIgnoreCase(t, "Fusion") || EqualsIgnoreCase(f, "fusion") ||
                    ContainsIgnoreCase(t, "Synchro") || EqualsIgnoreCase(f, "synchro") ||
                    ContainsIgnoreCase(t, "XYZ") || EqualsIgnoreCase(f, "xyz") ||
                    ContainsIgnoreCase(t, "Link") || EqualsIgnoreCase(f, "link"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Cards that only have structural play (NS/Set/Fusion summon procedure)
        /// without activated text we must script at resolution time.
        /// </summary>
        public static bool StructuralPlayOnly(CardDef def)
        {
            if (def == null) return true;
            if (HasNoActivatableEffect(def)) return true;
            // Spells / Traps / Effect monsters need registry or compiled program
            return false;
        }

        static bool ContainsIgnoreCase(string hay, string needle) =>
            !string.IsNullOrEmpty(hay) &&
            hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool EqualsIgnoreCase(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
