using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Crops full TCG card scans to the illustration window so arena / hologram
    /// projections focus on card artwork (not name, text box, ATK/DEF, or borders).
    /// Disk / inspect UI still use the full card face via <see cref="CardDatabase.GetArt"/>.
    /// </summary>
    public static class CardArtFocus
    {
        /// <summary>
        /// Normalized illustration rect on a standard Konami TCG card scan
        /// (origin bottom-left, matching Unity texture UVs).
        /// Tuned for LOB–era and modern portrait cards in StreamingAssets/CardArt.
        /// </summary>
        public static readonly Rect ArtworkNormRect = new(0.09f, 0.30f, 0.82f, 0.55f);

        /// <summary>Typical full card scan width/height (~59×86 mm).</summary>
        public const float FullCardAspect = 59f / 86f; // ≈ 0.686

        /// <summary>
        /// World-space size for monster arena artwork — Solid Vision on the street
        /// (not tabletop). Pitch is <c>ArPlaymatLayout.MonsterColumnPitch</c>.
        /// </summary>
        public static readonly Vector3 MonsterArtworkScale = new(1.335f, 1.335f, 1f);

        /// <summary>
        /// World-space size for spell/trap arena cards (full TCG face).
        /// Large floor cards in the feet row, still smaller than standing monsters.
        /// </summary>
        public static readonly Vector3 SpellTrapArtworkScale =
            new(0.58f, 0.58f / FullCardAspect, 1f);

        /// <summary>Local Y of standing monster art so feet sit on the street.</summary>
        public static float MonsterArtLift => MonsterArtworkScale.y * 0.5f;

        /// <summary>Local Y of a standing S/T card (one-shot rise).</summary>
        public static float SpellTrapArtLift => SpellTrapArtworkScale.y * 0.5f;

        /// <summary>
        /// True when texture looks like a full TCG card scan (portrait ~0.69).
        /// Square packs (e.g. Kuriboh 624×624) are already illustration-only — do not re-crop.
        /// </summary>
        public static bool LooksLikeFullCardScan(Texture tex)
        {
            if (tex == null || tex.height < 8) return false;
            var a = tex.width / (float)tex.height;
            // Full cards cluster near 0.686; already-cropped art is ~1.0 (or wide).
            return a > 0.58f && a < 0.78f;
        }

        public static bool LooksLikeFullCardScan(Sprite spr) =>
            spr != null && LooksLikeFullCardScan(spr.texture);

        /// <summary>
        /// True for pre-cropped illustration packs (near-square or wide).
        /// These must never receive <see cref="ArtworkNormRect"/> (double-crop).
        /// Kuriboh 624² is the canonical example in StreamingAssets/CardArt.
        /// </summary>
        public static bool IsPreCroppedIllustration(Texture tex)
        {
            if (tex == null || tex.height < 8) return false;
            var a = tex.width / (float)tex.height;
            // Near-square or wider-than-tall = art pack, not a full TCG face
            return a >= 0.85f;
        }

        public static bool IsPreCroppedIllustration(Sprite spr) =>
            spr != null && IsPreCroppedIllustration(spr.texture);

        /// <summary>
        /// Returns a sprite whose rect is only the illustration window.
        /// Shares the full-card texture with <see cref="CardDatabase.GetArt"/> (no second decode).
        /// </summary>
        public static Sprite GetArtworkSprite(CardDatabase db, int cardId)
        {
            if (db == null || cardId == 0) return null;
            return db.GetArtwork(cardId);
        }

        /// <summary>
        /// Apply full-card texture with UV crop so a MeshRenderer quad shows only artwork.
        /// Face-down callers should pass a card-back texture and leave cropFull=false.
        /// Skips crop when the texture is already illustration-only (square Kuriboh, etc.).
        /// </summary>
        public static void ApplyToMaterial(Material mat, Texture tex, bool cropToArtwork)
        {
            if (mat == null || tex == null) return;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            // Never crop square / pre-cropped packs (Kuriboh etc.)
            if (cropToArtwork && LooksLikeFullCardScan(tex) && !IsPreCroppedIllustration(tex))
            {
                var r = ArtworkNormRect;
                SetUv(mat, new Vector2(r.x, r.y), new Vector2(r.width, r.height));
            }
            else
            {
                SetUv(mat, Vector2.zero, Vector2.one);
            }
        }

        /// <summary>
        /// Preferred arena path: use <see cref="CardDatabase.GetArtwork"/> sprite rect
        /// so face-up monster holos show only the illustration window (not name/text/ATK).
        /// Square art packs (Kuriboh) map 1:1 with no UV crop.
        /// </summary>
        public static void ApplyMonsterArtwork(Material mat, CardDatabase db, int cardId)
        {
            if (mat == null) return;

            if (db != null && cardId != 0)
            {
                var art = db.GetArtwork(cardId);
                if (art != null && art.texture != null)
                {
                    // Pre-cropped / square: force full UVs even if sprite rect was wrong
                    if (IsPreCroppedIllustration(art.texture))
                        ApplyToMaterial(mat, art.texture, cropToArtwork: false);
                    else
                        ApplySpriteWindow(mat, art);
                    return;
                }

                var full = db.GetArt(cardId);
                if (full != null && full.texture != null)
                {
                    ApplyToMaterial(mat, full.texture, cropToArtwork: true);
                    return;
                }
            }

            ApplyToMaterial(mat, Texture2D.whiteTexture, cropToArtwork: false);
        }

        /// <summary>Map a sprite's textureRect to URP Unlit / Builtin UV ST.</summary>
        public static void ApplySpriteWindow(Material mat, Sprite sprite)
        {
            if (mat == null || sprite == null || sprite.texture == null) return;
            var tex = sprite.texture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            // Safety: never UV-window a square illustration pack
            if (IsPreCroppedIllustration(tex))
            {
                SetUv(mat, Vector2.zero, Vector2.one);
                return;
            }

            var tr = sprite.textureRect;
            var tw = Mathf.Max(1f, tex.width);
            var th = Mathf.Max(1f, tex.height);
            var offset = new Vector2(tr.x / tw, tr.y / th);
            var scale = new Vector2(tr.width / tw, tr.height / th);
            // Degenerate / full-rect → full UVs
            if (scale.x > 0.98f && scale.y > 0.98f && offset.sqrMagnitude < 1e-4f)
            {
                SetUv(mat, Vector2.zero, Vector2.one);
                return;
            }

            SetUv(mat, offset, scale);
        }

        /// <summary>Soft holographic tint for arena art glow (player cyan / opp magenta).</summary>
        public static void ApplyEmissiveTint(Material mat, Color accent, float strength = 0.22f)
        {
            if (mat == null) return;
            if (!mat.HasProperty("_EmissionColor")) return;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", accent * strength);
            if (mat.HasProperty("_EmissionMap") && mat.HasProperty("_BaseMap"))
            {
                var tex = mat.GetTexture("_BaseMap");
                if (tex != null) mat.SetTexture("_EmissionMap", tex);
            }
        }

        static void SetUv(Material mat, Vector2 offset, Vector2 scale)
        {
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureOffset("_BaseMap", offset);
                mat.SetTextureScale("_BaseMap", scale);
            }

            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTextureOffset("_MainTex", offset);
                mat.SetTextureScale("_MainTex", scale);
            }

            // URP packs ST as Vector4(scale.x, scale.y, offset.x, offset.y)
            if (mat.HasProperty("_BaseMap_ST"))
                mat.SetVector("_BaseMap_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));
            if (mat.HasProperty("_MainTex_ST"))
                mat.SetVector("_MainTex_ST", new Vector4(scale.x, scale.y, offset.x, offset.y));

            // Builtin property block aliases
            mat.mainTextureOffset = offset;
            mat.mainTextureScale = scale;
        }
    }
}
