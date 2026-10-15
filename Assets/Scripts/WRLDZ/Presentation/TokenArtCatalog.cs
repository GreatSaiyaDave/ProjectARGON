using System;
using System.Collections.Generic;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Maps a Token's printed name to one or more real card-art ids, so the arena hologram,
    /// disk card, field glance and 2D UI can give Tokens the illustration they represent
    /// instead of the blank "no art" glyph they currently show.
    /// </summary>
    /// <remarks>
    /// The engine is untouched: Tokens keep their printed name and their negative FNV-1a
    /// <see cref="DuelEngine.StableTokenId"/>. The variant is a cosmetic index
    /// (<see cref="CardInstance.TokenArtIndex"/>) set by the summon loop, so a single source
    /// like Ojama Trio (29843091) — which spawns three identically-named Tokens — can paint
    /// them as Yellow, Green and Black. Tokens with no entry, and every non-Token, resolve
    /// to their own <see cref="CardInstance.CardId"/> so existing behaviour is unchanged.
    /// </remarks>
    public static class TokenArtCatalog
    {
        /// <summary>Token name -> ordered art card ids (cycles by TokenArtIndex).</summary>
        static readonly Dictionary<string, int[]> Table = new(StringComparer.OrdinalIgnoreCase)
        {
            // 3 "Ojama Tokens" (Beast/LIGHT/Lv2 0/1000) summoned by Ojama Trio (29843091):
            // Yellow (42941100), Green (12482652), Black (79335209).
            ["Ojama Tokens"] = new[] { 42941100, 12482652, 79335209 },
        };

        /// <summary>
        /// Card id to load art for. For a real card this is just its CardId; for a mapped
        /// Token it is the chosen variant. Tokens with no mapping (and every non-Token)
        /// resolve to their own CardId, preserving the prior blank behaviour.
        /// </summary>
        public static int ResolveArtId(CardInstance card)
        {
            if (card == null) return 0;
            if (!card.IsToken) return card.CardId;
            var name = card.Def?.name;
            if (string.IsNullOrEmpty(name)) return card.CardId;

            if (!Table.TryGetValue(name, out var ids))
            {
                // Tolerate a singular token name printed differently (e.g. "Ojama Token").
                if (name.Length > 1 && name.EndsWith("s", StringComparison.Ordinal))
                    Table.TryGetValue(name.Substring(0, name.Length - 1), out ids);
                if (ids == null) return card.CardId;
            }

            if (ids.Length == 0) return card.CardId;
            var idx = card.TokenArtIndex;
            if (idx < 0) idx = 0;
            return ids[idx % ids.Length];
        }
    }
}
