using System.IO;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// StreamingAssets loaders for the baked Eldritch textures (ELDRITCH_CONTRACT §5).
    /// Every getter is null-safe: a missing file returns null and callers fall back to
    /// <c>UiFoundation.WhiteSprite()</c> / flat noise. Results are cached in statics on top of
    /// the <see cref="StreamingSprite"/> cache so hot paths never touch the dictionary.
    /// </summary>
    public static class EldritchAssets
    {
        public const string Root = "WRLDZ/Eldritch/";
        const string Tag = "[WRLDZ Eldritch]";

        // ── File names (single source of truth for the baker / smoke test) ──
        public const string FileHieroglyphAtlas = "hieroglyph_atlas.png";
        public const string FileHieroglyphAtlasJson = "hieroglyph_atlas.json";
        public const string FileHieroglyphAtlasGlow = "hieroglyph_atlas_glow.png";
        public const string FileEmberSoft = "fx_ember_soft.png";
        public const string FileDustMote = "fx_dust_mote.png";
        public const string FileArcaneSpark = "fx_arcane_spark.png";
        public const string FileRuneRing = "fx_rune_ring.png";
        public const string FileTetherLine = "fx_tether_line.png";
        public const string FileSandstoneTile = "tex_sandstone_tile.png";
        public const string FileLapisTile = "tex_lapis_tile.png";
        public const string FileEldritchNoise = "tex_eldritch_noise.png";
        public const string FileSealStone = "tex_seal_stone.png";
        public const string FileSealCracks = "tex_seal_cracks.png";
        public const string FileSealStars = "tex_seal_stars.png";
        public const string FileTentacleCorner = "deco_tentacle_corner.png";
        public const string FileTentacleCornerMask = "deco_tentacle_corner_mask.png";
        public const string FileCartoucheEnd = "deco_cartouche_end.png";

        /// <summary>Atlas layout (matches hieroglyph_atlas.json; used when the json is missing).</summary>
        public const int AtlasCell = 128;
        public const int AtlasGrid = 8;
        public const int AtlasCount = AtlasGrid * AtlasGrid;

        static Sprite _atlas, _atlasGlow, _ember, _dust, _spark, _runeRing, _tether,
            _tentacle, _tentacleMask, _cartouche;
        static Texture2D _atlasTex, _atlasGlowTex, _sandstone, _lapis, _noise;
        static Texture2D _sealStone, _sealCracks, _sealStars;
        static string _atlasJson;
        static bool _atlasJsonTried;
        static bool _warnedMissing;

        // ── Sprites ─────────────────────────────────────────────────

        /// <summary>Whole 1024² glyph sheet as one sprite (EldritchGlyphs slices it).</summary>
        public static Sprite HieroglyphAtlas() => S(ref _atlas, FileHieroglyphAtlas);
        /// <summary>Matching halo sheet.</summary>
        public static Sprite HieroglyphAtlasGlow() => S(ref _atlasGlow, FileHieroglyphAtlasGlow);

        public static Sprite EmberSoft() => S(ref _ember, FileEmberSoft);
        public static Sprite DustMote() => S(ref _dust, FileDustMote);
        public static Sprite ArcaneSpark() => S(ref _spark, FileArcaneSpark);
        /// <summary>AR hub pulse ring / panel title ornament.</summary>
        public static Sprite RuneRing() => S(ref _runeRing, FileRuneRing);
        /// <summary>8×64 tether strip (AR menu → disk hub).</summary>
        public static Sprite TetherLine() => S(ref _tether, FileTetherLine);
        /// <summary>White/alpha tentacle corner decal (tint EldritchViolet, rotate per corner).</summary>
        public static Sprite TentacleCorner() => S(ref _tentacle, FileTentacleCorner);
        public static Sprite TentacleCornerMask() => S(ref _tentacleMask, FileTentacleCornerMask);
        /// <summary>64×192 cartouche end cap (flip X for the right cap).</summary>
        public static Sprite CartoucheEnd() => S(ref _cartouche, FileCartoucheEnd);

        // ── Textures ────────────────────────────────────────────────

        /// <summary>Raw atlas texture (Clamp) for <c>Sprite.Create</c> slicing.</summary>
        public static Texture2D HieroglyphAtlasTexture() => T(ref _atlasTex, FileHieroglyphAtlas, TextureWrapMode.Clamp);
        public static Texture2D HieroglyphAtlasGlowTexture() => T(ref _atlasGlowTex, FileHieroglyphAtlasGlow, TextureWrapMode.Clamp);
        /// <summary>256² gray seamless sandstone (tint Sandstone). Repeat.</summary>
        public static Texture2D SandstoneTile() => T(ref _sandstone, FileSandstoneTile, TextureWrapMode.Repeat);
        /// <summary>256² seamless lapis + gold flecks. Repeat.</summary>
        public static Texture2D LapisTile() => T(ref _lapis, FileLapisTile, TextureWrapMode.Repeat);
        /// <summary>256² seamless shader noise: R warp · G ridged veins · B slow breath. Repeat.</summary>
        // contract note: R/G/B are DATA channels (warp · veins · breath), not color. StreamingSprite
        // .LoadTexture builds sRGB textures, which the Linear-space GPU would gamma-decode on every
        // sample (0.5 arrives as ~0.21) — and the baker sets sRGBTexture=false on its asset copy, so
        // runtime and baked materials would disagree. Load this one linear, by hand.
        // ── Barrier-stone seal (BarrierStoneSeal.shader) ──
        // The fracture sheet and the starfield are data — they pack core / lip / wide / seep and
        // stars / twinkle phase across channels — so they load linear and mean what they were
        // authored to mean. The stone is NOT data: its RGB is albedo. Loading albedo linear reads
        // sRGB bytes as linear values, and the sRGB framebuffer then re-encodes them on output,
        // which lifted deep lapis (14,21,44) to a washed grey-blue (69,81,111) on screen.
        // Alpha is never colour-converted, so the height channel survives the sRGB load intact.

        /// <summary>Carved lapis barrier-stone, seamless. RGB = lit stone (sRGB), A = height.</summary>
        public static Texture2D SealStone()
        {
            if (_sealStone != null) return _sealStone;
            _sealStone = LoadSrgb(FileSealStone, TextureWrapMode.Repeat);
            if (_sealStone == null) WarnOnce(FileSealStone);
            return _sealStone;
        }

        /// <summary>2x2 atlas of fracture variants. R core · G chipped lip · B wide bleed · A seep.</summary>
        public static Texture2D SealCracks()
        {
            if (_sealCracks != null) return _sealCracks;
            _sealCracks = LoadLinear(FileSealCracks, TextureWrapMode.Clamp);
            if (_sealCracks == null) WarnOnce(FileSealCracks);
            return _sealCracks;
        }

        /// <summary>Shadow Realm starfield seen through a breach. R = stars, G = twinkle phase.</summary>
        public static Texture2D SealStars()
        {
            if (_sealStars != null) return _sealStars;
            _sealStars = LoadLinear(FileSealStars, TextureWrapMode.Repeat);
            if (_sealStars == null) WarnOnce(FileSealStars);
            return _sealStars;
        }

        public static Texture2D EldritchNoise()
        {
            if (_noise != null) return _noise;
            _noise = LoadLinear(FileEldritchNoise, TextureWrapMode.Repeat);
            if (_noise == null) WarnOnce(FileEldritchNoise);
            return _noise;
        }

        /// <summary>StreamingAssets → sRGB Texture2D, for albedo. Null when the file is missing.</summary>
        static Texture2D LoadSrgb(string file, TextureWrapMode wrap) => LoadRaw(file, wrap, false);

        /// <summary>StreamingAssets → linear (non-sRGB) Texture2D. Null when the file is missing.</summary>
        static Texture2D LoadLinear(string file, TextureWrapMode wrap) => LoadRaw(file, wrap, true);

        static Texture2D LoadRaw(string file, TextureWrapMode wrap, bool linear)
        {
            try
            {
                if (!StreamingFiles.TryReadAllBytes(Root + file, out var bytes)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
                if (!tex.LoadImage(bytes))
                {
                    Object.Destroy(tex);
                    return null;
                }

                tex.name = System.IO.Path.GetFileNameWithoutExtension(file);
                tex.wrapMode = wrap;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{Tag} linear load failed for {file}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Raw json text listing the 64 codepoints (null when missing).</summary>
        public static string HieroglyphAtlasJson()
        {
            if (_atlasJsonTried) return _atlasJson;
            _atlasJsonTried = true;
            try
            {
                if (StreamingFiles.TryReadAllText(Root + FileHieroglyphAtlasJson, out var json)) _atlasJson = json;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"{Tag} atlas json unreadable: {e.Message}");
                _atlasJson = null;
            }
            return _atlasJson;
        }

        /// <summary>True when the noise tile is on disk — shaders degrade to flat gray without it.</summary>
        public static bool Present() =>
            StreamingFiles.Exists(Root + FileEldritchNoise);

        /// <summary>Drop the static caches (after an on-disk rebake). StreamingSprite cache too.</summary>
        public static void ClearCache()
        {
            _atlas = _atlasGlow = _ember = _dust = _spark = _runeRing = _tether =
                _tentacle = _tentacleMask = _cartouche = null;
            _atlasTex = _atlasGlowTex = _sandstone = _lapis = _noise = null;
            _atlasJson = null;
            _atlasJsonTried = false;
            _warnedMissing = false;
            StreamingSprite.ClearCache(Root);
        }

        // ── Internals ───────────────────────────────────────────────

        static Sprite S(ref Sprite cache, string file)
        {
            if (cache != null) return cache;
            cache = StreamingSprite.Load(Root + file);
            if (cache == null) WarnOnce(file);
            return cache;
        }

        static Texture2D T(ref Texture2D cache, string file, TextureWrapMode wrap)
        {
            if (cache != null) return cache;
            cache = StreamingSprite.LoadTexture(Root + file, wrap);
            if (cache == null) WarnOnce(file);
            return cache;
        }

        static void WarnOnce(string file)
        {
            if (_warnedMissing) return;
            _warnedMissing = true;
            Debug.LogWarning($"{Tag} StreamingAssets/{Root}{file} missing — Eldritch chrome falls back to flat sprites.");
        }
    }
}
