using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Hieroglyph sprite pool. Legacy <see cref="UnityEngine.UI.Text"/> cannot draw the
    /// U+13000 block (surrogate pairs), so glyphs are baked into an 8×8 atlas and sliced
    /// lazily here. Never returns null or an empty array — falls back to a 1-sprite white
    /// pool so borders still lay out when StreamingAssets are missing.
    /// </summary>
    public static class EldritchGlyphs
    {
        const int Grid = 8;

        static Sprite[] _atlas;
        static Sprite[] _glow;
        static Font _font;
        static bool _fontTried;
        static bool _warnedAtlas;

        /// <summary>64 sprites sliced from hieroglyph_atlas.png (row-major, top-left first).</summary>
        public static Sprite[] Atlas()
        {
            if (Valid(_atlas)) return _atlas;
            _atlas = Slice(EldritchAssets.HieroglyphAtlasTexture(), "glyph_");
            return _atlas;
        }

        /// <summary>Matching halo sprites (hieroglyph_atlas_glow.png), same indices.</summary>
        public static Sprite[] AtlasGlow()
        {
            if (Valid(_glow)) return _glow;
            _glow = Slice(EldritchAssets.HieroglyphAtlasGlowTexture(), "glyph_glow_");
            return _glow;
        }

        public static int Count => Atlas().Length;

        public static Sprite Random(System.Random rng)
        {
            var pool = Atlas();
            if (rng == null) return pool[UnityEngine.Random.Range(0, pool.Length)];
            return pool[rng.Next(pool.Length)];
        }

        /// <summary>Resources NotoSansEgyptianHieroglyphs — may be null; only for debug labels.</summary>
        public static Font DynamicFont()
        {
            if (_fontTried) return _font;   // cache the miss too — the font is optional
            _fontTried = true;
            _font = Resources.Load<Font>("WRLDZ/Fonts/NotoSansEgyptianHieroglyphs-Regular");
            return _font;
        }

        static bool Valid(Sprite[] arr)
        {
            // Domain reload / texture unload leaves destroyed sprites behind; re-slice then.
            return arr != null && arr.Length > 0 && arr[0] != null && arr[0].texture != null;
        }

        static Sprite[] Slice(Texture2D tex, string prefix)
        {
            if (tex == null || tex.width < Grid || tex.height < Grid)
            {
                if (!_warnedAtlas)
                {
                    _warnedAtlas = true;
                    Debug.LogWarning("[WRLDZ Eldritch] hieroglyph atlas missing — using white glyph fallback.");
                }

                return new[] { UiFoundation.WhiteSprite() };
            }

            var cellW = tex.width / Grid;
            var cellH = tex.height / Grid;
            var result = new Sprite[Grid * Grid];
            for (var row = 0; row < Grid; row++)
            {
                for (var col = 0; col < Grid; col++)
                {
                    // Atlas is authored top-left first; Unity rects are bottom-up.
                    var rect = new Rect(col * cellW, (Grid - 1 - row) * cellH, cellW, cellH);
                    var spr = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0,
                        SpriteMeshType.FullRect);
                    spr.name = prefix + (row * Grid + col).ToString("00");
                    result[row * Grid + col] = spr;
                }
            }

            return result;
        }
    }
}
