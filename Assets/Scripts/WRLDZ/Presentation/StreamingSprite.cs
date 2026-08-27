using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>Load PNG/JPG from StreamingAssets into sprites (cached).</summary>
    public static class StreamingSprite
    {
        static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Load(string relativePath) =>
            LoadInternal(relativePath, Vector4.zero, autoChromaKey: true);

        static readonly Dictionary<string, Texture2D> TexCache = new();

        /// <summary>Raw texture (no sprite). Does not chroma-key full-bleed spirit skins.</summary>
        public static Texture2D LoadTexture(string relativePath, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            var key = relativePath + "|tex|" + wrap;
            if (TexCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var full = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!File.Exists(full)) return null;
            var bytes = File.ReadAllBytes(full);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            tex.name = Path.GetFileNameWithoutExtension(relativePath);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = wrap;
            tex.anisoLevel = 4;
            TexCache[key] = tex;
            return tex;
        }

        /// <summary>Load with 9-slice borders (L,B,R,T in pixels).</summary>
        public static Sprite LoadSliced(string relativePath, Vector4 border)
        {
            var key = relativePath + "|slice|" + border;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var spr = LoadInternal(relativePath, border, autoChromaKey: true);
            if (spr != null) Cache[key] = spr;
            return spr;
        }

        /// <summary>
        /// Force reload after an on-disk replace. Null clears everything.
        /// A path ending in / or \ clears every cached key under that prefix.
        /// </summary>
        public static void ClearCache(string relativePath = null)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                Cache.Clear();
                return;
            }

            var doomed = new List<string>();
            var folder = relativePath.EndsWith("/") || relativePath.EndsWith("\\");
            foreach (var k in Cache.Keys)
            {
                if (k == relativePath || k.StartsWith(relativePath + "|"))
                    doomed.Add(k);
                else if (folder && k.StartsWith(relativePath))
                    doomed.Add(k);
            }

            foreach (var k in doomed)
                Cache.Remove(k);
        }

        static Sprite LoadInternal(string relativePath, Vector4 border, bool autoChromaKey)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            var cacheKey = border.sqrMagnitude > 0
                ? relativePath + "|slice|" + border
                : relativePath;
            if (Cache.TryGetValue(cacheKey, out var s) && s != null) return s;

            var full = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!File.Exists(full)) return null;

            var bytes = File.ReadAllBytes(full);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            // Safety net: Imagine-style isolation still on chroma green → key out.
            // Skips full-bleed scene backgrounds (path contains /bg/).
            if (autoChromaKey && ShouldAutoChroma(relativePath, tex))
                ApplyChromaKeyInPlace(tex);

            tex.name = Path.GetFileNameWithoutExtension(relativePath);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            spr.name = tex.name;
            Cache[cacheKey] = spr;
            return spr;
        }

        static bool ShouldAutoChroma(string relativePath, Texture2D tex)
        {
            if (tex == null || tex.width < 8 || tex.height < 8) return false;
            var p = relativePath.Replace('\\', '/');
            // Full-bleed scenes must keep every pixel
            if (p.Contains("/bg/") || p.Contains("/Bg/")) return false;
            if (p.EndsWith("bg_splash.png") || p.EndsWith("bg_hub.png") ||
                p.EndsWith("bg_menu_void.png") || p.EndsWith("bg_overworld_map.png") ||
                p.EndsWith("bg_duel_stage.png") || p.EndsWith("bg_duel_stage_portrait.png") ||
                p.EndsWith("bg_title.png") || p.EndsWith("bg_credits.png") ||
                p.EndsWith("bg_auth.png") || p.EndsWith("bg_overworld_map.png") ||
                p.EndsWith("bg_overworld_district.png"))
                return false;

            // Only auto-key WRLDZ chrome / icons / pins / fx that commonly ship on green
            var underWrldz = p.StartsWith("WRLDZ/") || p.StartsWith("WRLDZ\\");
            if (!underWrldz) return false;
            // Official Yugipedia attribute/type medallions — keep their painted color.
            if (p.IndexOf("/icons/ygo/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (p.IndexOf("/icons/deckstyle/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            // Detect green corners (cheap 4-sample)
            var w = tex.width;
            var h = tex.height;
            var c0 = tex.GetPixel(2, 2);
            var c1 = tex.GetPixel(w - 3, 2);
            var c2 = tex.GetPixel(2, h - 3);
            var c3 = tex.GetPixel(w - 3, h - 3);
            return IsChromaGreen(c0) && IsChromaGreen(c1) && IsChromaGreen(c2) && IsChromaGreen(c3);
        }

        static bool IsChromaGreen(Color c) =>
            c.g > 0.38f && c.g > c.r + 0.12f && c.g > c.b + 0.12f && c.r < 0.48f && c.b < 0.48f && c.a > 0.85f;

        /// <summary>
        /// Remove isolation green screen + mild despill. Operates in-place on readable texture.
        /// </summary>
        public static void ApplyChromaKeyInPlace(Texture2D tex)
        {
            if (tex == null) return;
            var pixels = tex.GetPixels32();
            if (pixels == null || pixels.Length == 0) return;

            // Key from average of four corners
            var w = tex.width;
            var h = tex.height;
            Color32 Sample(int x, int y) => pixels[y * w + x];
            var k0 = Sample(2, 2);
            var k1 = Sample(w - 3, 2);
            var k2 = Sample(2, h - 3);
            var k3 = Sample(w - 3, h - 3);
            var kr = (k0.r + k1.r + k2.r + k3.r) * 0.25f;
            var kg = (k0.g + k1.g + k2.g + k3.g) * 0.25f;
            var kb = (k0.b + k1.b + k2.b + k3.b) * 0.25f;

            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var r = (float)p.r;
                var g = (float)p.g;
                var b = (float)p.b;
                var maxRb = r > b ? r : b;
                var dominance = g - maxRb;
                var dr = r - kr;
                var dg = g - kg;
                var db = b - kb;
                var dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);

                var greenScreen = g > 95f && r < 130f && b < 130f &&
                                  dominance > 35f && g > r + 25f && g > b + 25f;
                var pure = g > 170f && r < 80f && b < 80f && dominance > 60f;

                float score = 0f;
                if (pure) score = 1f;
                else if (greenScreen && dist < 55f) score = 1f;
                else if (greenScreen && dist < 90f) score = 0.85f;
                else if (greenScreen && dominance > 50f) score = 0.75f;
                else if (g > 90f && dominance > 18f && dominance <= 50f && r < 140f && b < 140f && dist < 100f)
                    score = Mathf.Clamp01((dominance - 10f) / 45f) * 0.65f;

                if (score <= 0.01f) continue;

                var na = Mathf.Clamp01(p.a / 255f * (1f - score));
                // Despill residual green on fringe
                if (score < 0.98f && dominance > 10f)
                {
                    var gfix = Mathf.Max(maxRb, (r + b) * 0.5f);
                    g = g * (1f - score * 0.85f) + gfix * (score * 0.85f);
                    if (score > 0.15f && score < 0.7f)
                    {
                        r = Mathf.Min(255f, r + dominance * 0.15f * score);
                        b = Mathf.Min(255f, b + dominance * 0.15f * score);
                    }
                }

                if (na < 0.01f)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                }
                else
                {
                    pixels[i] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(na * 255f), 0, 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
        }

        /// <summary>
        /// Card back for hand / set / disk face-down.
        /// Primary: HD swirl back from Downloads (Back_card_yugioh_hd).
        /// </summary>
        public static Sprite CardBack() =>
            Load("WRLDZ/CardBack/card_back.png")
            ?? YgoCardFrames.CardBack()
            ?? Load("WRLDZ/CardBack/card_back_wrldz.png")
            ?? ImagineAssets.CardBackWrldz();

        /// <summary>Texture for 3D backs (arena S/T, sets, disk). Never a white placeholder if a back exists.</summary>
        public static Texture CardBackTexture()
        {
            var spr = CardBack();
            return spr != null && spr.texture != null ? spr.texture : Texture2D.grayTexture;
        }
        public static Sprite ReferobotPortrait() => Load("WRLDZ/Referobot/referobot_portrait.png");

        // GO chrome pack
        public static Sprite GoOrbMain() =>
            ImagineAssets.BtnCircle()
            ?? Load("WRLDZ/GoChrome/main_orb.png")
            ?? Load("WRLDZ/GoChrome/orb_main.png");
        public static Sprite GoOrbMenu() =>
            ImagineAssets.IconMenu()
            ?? ImagineAssets.BtnCircle()
            ?? Load("WRLDZ/GoChrome/orb_menu.png");
        public static Sprite GoPinTear() =>
            ImagineAssets.PinTear() ?? Load("WRLDZ/GoChrome/pin_tear.png");
        public static Sprite GoPinArena() =>
            ImagineAssets.PinArena() ?? Load("WRLDZ/GoChrome/pin_arena.png");
        public static Sprite GoPinAnchor() =>
            ImagineAssets.PinPortal() ?? Load("WRLDZ/GoChrome/pin_anchor.png");
    }
}
