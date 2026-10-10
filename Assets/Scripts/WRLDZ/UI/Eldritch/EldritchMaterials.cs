using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Shader lookup + per-instance material factory (ELDRITCH_CONTRACT §3).
    /// UGUI has no MaterialPropertyBlock, so every slate / button / border gets its OWN
    /// <see cref="Material"/>; owners destroy it in OnDestroy via <see cref="Release"/>.
    /// Missing shaders fall back to <c>UI/Default</c> (extra properties are then silently ignored).
    /// </summary>
    public static class EldritchMaterials
    {
        public const string SlateShaderName = "WRLDZ/UI/SolidVisionSlate";
        public const string FlowShaderName = "WRLDZ/UI/ArcaneEnergyFlow";
        public const string GlyphShaderName = "WRLDZ/UI/HieroglyphGlow";
        const string FallbackShaderName = "UI/Default";
        const string Tag = "[WRLDZ Eldritch]";

        // ── Shader property ids (shared by the component layer) ─────
        public static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        public static readonly int RectPivotId = Shader.PropertyToID("_RectPivot");
        public static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
        public static readonly int HoverId = Shader.PropertyToID("_Hover");
        public static readonly int OpenId = Shader.PropertyToID("_Open");
        public static readonly int PressedId = Shader.PropertyToID("_Pressed");
        public static readonly int FlickerId = Shader.PropertyToID("_Flicker");
        public static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        public static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        public static readonly int FillColorAId = Shader.PropertyToID("_FillColorA");
        public static readonly int FillColorBId = Shader.PropertyToID("_FillColorB");
        public static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        public static readonly int LineColor2Id = Shader.PropertyToID("_LineColor2");
        public static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        public static readonly int VeinColorId = Shader.PropertyToID("_VeinColor");
        public static readonly int VeinStrengthId = Shader.PropertyToID("_VeinStrength");
        public static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        public static readonly int GlowId = Shader.PropertyToID("_Glow");
        public static readonly int BloomId = Shader.PropertyToID("_Bloom");
        public static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        public static readonly int LineSpeedId = Shader.PropertyToID("_LineSpeed");
        public static readonly int LineLengthId = Shader.PropertyToID("_LineLength");
        public static readonly int LineCountId = Shader.PropertyToID("_LineCount");
        public static readonly int HazeStrengthId = Shader.PropertyToID("_HazeStrength");
        public static readonly int HazeWidthId = Shader.PropertyToID("_HazeWidth");
        public static readonly int PhaseId = Shader.PropertyToID("_Phase");
        public static readonly int GlyphTexelId = Shader.PropertyToID("_GlyphTexel");
        public static readonly int FadeId = Shader.PropertyToID("_Fade");
        public static readonly int EdgeGlowId = Shader.PropertyToID("_EdgeGlow");
        public static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        public static readonly int ScanIntensityId = Shader.PropertyToID("_ScanIntensity");
        public static readonly int ScanDensityId = Shader.PropertyToID("_ScanDensity");
        public static readonly int SweepIntensityId = Shader.PropertyToID("_SweepIntensity");
        public static readonly int ScanBaseId = Shader.PropertyToID("_ScanBase");
        public static readonly int VeinBaseId = Shader.PropertyToID("_VeinBase");
        public static readonly int SweepBaseId = Shader.PropertyToID("_SweepBase");

        static Shader _slate, _flow, _glyph;
        static bool _warnedSlate, _warnedFlow, _warnedGlyph;

        // ── Shader lookup ───────────────────────────────────────────

        public static Shader SlateShader() => Find(ref _slate, SlateShaderName, ref _warnedSlate);
        public static Shader FlowShader() => Find(ref _flow, FlowShaderName, ref _warnedFlow);
        public static Shader GlyphShader() => Find(ref _glyph, GlyphShaderName, ref _warnedGlyph);

        /// <summary>True when <paramref name="m"/> runs one of the Eldritch shaders (not the fallback).</summary>
        public static bool IsEldritch(Material m) =>
            m != null && m.shader != null && m.shader.name.StartsWith("WRLDZ/UI/");

        static Shader Find(ref Shader cache, string name, ref bool warned)
        {
            if (cache != null) return cache;
            var sh = Shader.Find(name);
            if (sh != null)
            {
                cache = sh;   // only cache real hits so a late shader import is picked up
                return sh;
            }

            if (!warned)
            {
                warned = true;
                Debug.LogWarning($"{Tag} shader '{name}' not found — using {FallbackShaderName}.");
            }
            return Shader.Find(FallbackShaderName) ?? Shader.Find("Sprites/Default");
        }

        // ── Factories (always NEW instances) ────────────────────────

        /// <summary>Translucent holographic slate for the given tier.</summary>
        public static Material NewSlate(EldritchTier tier)
        {
            var m = new Material(SlateShader()) { name = "M_SolidVisionSlate_" + tier };
            ApplySlateTier(m, tier);
            AssignNoise(m);
            return m;
        }

        /// <summary>Energy-line button face for the given tier.</summary>
        public static Material NewFlow(EldritchTier tier)
        {
            var m = new Material(FlowShader()) { name = "M_ArcaneEnergyFlow_" + tier };
            ApplyFlowTier(m, tier);
            AssignNoise(m);
            return m;
        }

        /// <summary>Glyph cell material; one per HieroglyphBorder, cells encode phase in color.a.</summary>
        public static Material NewGlyph(Color glow)
        {
            var m = new Material(GlyphShader()) { name = "M_HieroglyphGlow" };
            if (m.HasProperty(GlowColorId)) m.SetColor(GlowColorId, glow);
            // CanvasRenderer binds _MainTex, so _MainTex_TexelSize never reflects the atlas —
            // hand the halo its real texel size or the 4 taps land ~0.75 UV away.
            var atlas = EldritchAssets.HieroglyphAtlasTexture();
            if (atlas != null && m.HasProperty(GlyphTexelId))
                m.SetVector(GlyphTexelId, new Vector4(1f / atlas.width, 1f / atlas.height, 0f, 0f));
            return m;
        }

        /// <summary>Retint an existing slate material to a tier (fill alpha follows MenuChromePrefs).</summary>
        public static void ApplySlateTier(Material m, EldritchTier tier)
        {
            if (m == null) return;
            SetColorSafe(m, EdgeColorId, EldritchPalette.EdgeFor(tier));
            SetColorSafe(m, FillColorId, EldritchPalette.FillFor(tier));
            SetColorSafe(m, ScanColorId, EldritchPalette.ScanFor(tier));
            SetColorSafe(m, VeinColorId, EldritchPalette.VeinFor(tier));
            SetFloatSafe(m, VeinStrengthId, EldritchPalette.VeinStrengthFor(tier));
            switch (tier)
            {
                case EldritchTier.Tertiary:
                    // HUD and chips: a hairline, not a glow band.
                    SetFloatSafe(m, EdgeGlowId, 0.42f);
                    SetFloatSafe(m, EdgeWidthId, 2.2f);
                    SetFloatSafe(m, Shader.PropertyToID("_ScanIntensity"), 0.04f);
                    SetFloatSafe(m, Shader.PropertyToID("_SweepIntensity"), 0.12f);
                    break;
                case EldritchTier.Eldritch:
                    SetFloatSafe(m, EdgeGlowId, 0.85f);
                    SetFloatSafe(m, EdgeWidthId, 4.2f);
                    SetFloatSafe(m, Shader.PropertyToID("_ScanIntensity"), 0.06f);
                    SetFloatSafe(m, Shader.PropertyToID("_SweepIntensity"), 0.18f);
                    SetFloatSafe(m, Shader.PropertyToID("_Warp"), 0.008f);
                    SetFloatSafe(m, FlickerId, 0.03f);
                    break;
                case EldritchTier.Danger:
                    SetFloatSafe(m, EdgeGlowId, 0.7f);
                    SetFloatSafe(m, EdgeWidthId, 2.8f);
                    SetFloatSafe(m, FlickerId, 0.03f);
                    break;
                default:
                    SetFloatSafe(m, EdgeGlowId, 0.62f);
                    SetFloatSafe(m, EdgeWidthId, 3.0f);
                    break;
            }

            // Remember what the tier asked for; SetRect scales the live values down by surface
            // size from these, so tier identity survives and repeated syncs stay idempotent.
            if (m.HasProperty(ScanIntensityId)) SetFloatSafe(m, ScanBaseId, m.GetFloat(ScanIntensityId));
            if (m.HasProperty(VeinStrengthId)) SetFloatSafe(m, VeinBaseId, m.GetFloat(VeinStrengthId));
            if (m.HasProperty(SweepIntensityId)) SetFloatSafe(m, SweepBaseId, m.GetFloat(SweepIntensityId));
        }

        /// <summary>Retint an existing flow material to a tier.</summary>
        public static void ApplyFlowTier(Material m, EldritchTier tier)
        {
            if (m == null) return;
            var fill = EldritchPalette.FillFor(tier);
            var fillB = new Color(fill.r * 0.55f, fill.g * 0.55f, fill.b * 0.6f, fill.a);
            SetColorSafe(m, FillColorAId, fill);
            SetColorSafe(m, FillColorBId, fillB);
            SetColorSafe(m, LineColorId, EldritchPalette.LineFor(tier));
            SetColorSafe(m, LineColor2Id, EldritchPalette.Line2For(tier));
            switch (tier)
            {
                case EldritchTier.Secondary:
                    SetFloatSafe(m, BloomId, 1.3f);
                    SetFloatSafe(m, LineWidthId, 3.5f);
                    SetFloatSafe(m, LineSpeedId, 0.45f);
                    SetFloatSafe(m, HazeStrengthId, 0.30f);
                    SetFloatSafe(m, HazeWidthId, 28f);
                    break;
                case EldritchTier.Tertiary:
                    SetFloatSafe(m, BloomId, 0.8f);
                    SetFloatSafe(m, LineWidthId, 2.5f);
                    SetFloatSafe(m, LineSpeedId, 0.3f);
                    SetFloatSafe(m, LineLengthId, 0.22f);
                    SetFloatSafe(m, HazeStrengthId, 0.12f);
                    SetFloatSafe(m, HazeWidthId, 20f);
                    break;
                case EldritchTier.Eldritch:
                    SetFloatSafe(m, BloomId, 1.8f);
                    SetFloatSafe(m, LineWidthId, 4.5f);
                    SetFloatSafe(m, LineSpeedId, 0.35f);
                    SetFloatSafe(m, LineCountId, 2f);
                    SetFloatSafe(m, HazeStrengthId, 0.45f);
                    SetFloatSafe(m, HazeWidthId, 44f);
                    SetFloatSafe(m, FlickerId, 0.07f);
                    break;
                case EldritchTier.Danger:
                    SetFloatSafe(m, BloomId, 1.5f);
                    SetFloatSafe(m, LineWidthId, 3.5f);
                    SetFloatSafe(m, LineSpeedId, 0.8f);
                    SetFloatSafe(m, LineLengthId, 0.28f);
                    SetFloatSafe(m, HazeStrengthId, 0.35f);
                    SetFloatSafe(m, HazeWidthId, 30f);
                    break;
                default:
                    SetFloatSafe(m, BloomId, 1.6f);
                    SetFloatSafe(m, LineWidthId, 4f);
                    SetFloatSafe(m, LineSpeedId, 0.5f);
                    SetFloatSafe(m, HazeStrengthId, 0.25f); // preview tune: 0.40 read as a bevelled tunnel
                    break;
            }
        }

        // ── Binding helpers ─────────────────────────────────────────

        /// <summary>Assign the material and neutralize the Image tint (shader owns color).</summary>
        public static void Bind(Image img, Material m)
        {
            if (img == null) return;
            img.material = m;
            img.color = Color.white;
            if (img.sprite == null) img.sprite = UiFoundation.WhiteSprite();   // full-rect UVs + raycasts
            img.type = Image.Type.Simple;   // rect-space math assumes a full-rect quad
        }

        /// <summary>
        /// Force every Eldritch surface under <paramref name="root"/> (or the whole scene when null)
        /// to push its laid-out rect to its material. Call after <c>Canvas.ForceUpdateCanvases()</c>
        /// in edit-mode captures and bakers; play mode syncs itself in LateUpdate.
        /// </summary>
        public static int SyncRects(Transform root = null)
        {
            var n = 0;
            if (root != null)
            {
                foreach (var s in root.GetComponentsInChildren<SolidVisionSlate>(true)) { s.SyncNow(); n++; }
                foreach (var b in root.GetComponentsInChildren<ArcaneButton>(true)) { b.SyncNow(); n++; }
                foreach (var h in root.GetComponentsInChildren<HieroglyphBorder>(true)) { h.RelayoutNow(); n++; }
                foreach (var b in root.GetComponentsInChildren<BarrierStone>(true)) { b.SyncNow(); n++; }
                return n;
            }

            foreach (var s in Object.FindObjectsByType<SolidVisionSlate>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { s.SyncNow(); n++; }
            foreach (var b in Object.FindObjectsByType<ArcaneButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.SyncNow(); n++; }
            foreach (var h in Object.FindObjectsByType<HieroglyphBorder>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { h.RelayoutNow(); n++; }
            foreach (var b in Object.FindObjectsByType<BarrierStone>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.SyncNow(); n++; }
            return n;
        }

        /// <summary>Push rect size / pivot so the shader can measure pixels to the nearest edge.</summary>
        public static void SetRect(Material m, RectTransform rt)
        {
            if (m == null || rt == null) return;
            var r = rt.rect;
            var w = Mathf.Max(r.width, 1f);
            var h = Mathf.Max(r.height, 1f);
            m.SetVector(RectSizeId, new Vector4(w, h, 0f, 0f));
            m.SetVector(RectPivotId, new Vector4(rt.pivot.x, rt.pivot.y, 0f, 0f));

            // Scanlines were authored on ~96px chips. Applied per-pixel at panel scale the
            // same density becomes a cyan/violet moire that swallows the art underneath,
            // so fade it out as the surface grows. Lerped from constants, never from the
            // material's current value, so repeated SyncRects calls stay idempotent.
            var k = Mathf.Clamp01(200f / Mathf.Max(w, h));
            if (m.HasProperty(ScanBaseId) && m.HasProperty(ScanIntensityId))
                m.SetFloat(ScanIntensityId, m.GetFloat(ScanBaseId) * Mathf.Lerp(0.10f, 1f, k));
            else if (m.HasProperty(ScanIntensityId))
                m.SetFloat(ScanIntensityId, Mathf.Lerp(0.015f, 0.16f, k));
            if (m.HasProperty(ScanDensityId))
                m.SetFloat(ScanDensityId, Mathf.Lerp(0.15f, 1f, k));

            // Same story for the organic layers: the noise tile is 256px, so on a wide chrome
            // plate the veins stretch into violet blobs and the sweep becomes a broad wash.
            // Scale both from the authored tier bases rather than the live value.
            if (m.HasProperty(VeinBaseId) && m.HasProperty(VeinStrengthId))
                m.SetFloat(VeinStrengthId, m.GetFloat(VeinBaseId) * Mathf.Lerp(0.30f, 1f, k));
            if (m.HasProperty(SweepBaseId) && m.HasProperty(SweepIntensityId))
                m.SetFloat(SweepIntensityId, m.GetFloat(SweepBaseId) * Mathf.Lerp(0.35f, 1f, k));
        }

        public static void SetHover(Material m, float hover01)
        {
            if (m != null) m.SetFloat(HoverId, Mathf.Clamp01(hover01));
        }

        public static void SetOpen(Material m, float open01)
        {
            if (m != null) m.SetFloat(OpenId, Mathf.Clamp01(open01));
        }

        public static void SetPressed(Material m, float pressed01)
        {
            if (m != null) m.SetFloat(PressedId, Mathf.Clamp01(pressed01));
        }

        /// <summary>Slate tint from code (fill under the sprite + edge glow).</summary>
        public static void SetTint(Material m, Color fill, Color edge)
        {
            if (m == null) return;
            SetColorSafe(m, FillColorId, fill);
            SetColorSafe(m, EdgeColorId, edge);
        }

        /// <summary>Assign the eldritch noise tile when it is on disk (shader defaults to flat gray).</summary>
        public static void AssignNoise(Material m)
        {
            if (m == null) return;
            var noise = EldritchAssets.EldritchNoise();
            if (noise != null && m.HasProperty(NoiseTexId)) m.SetTexture(NoiseTexId, noise);
        }

        /// <summary>Edit-mode safe destroy for per-instance materials (call from OnDestroy).</summary>
        public static void Release(Material m)
        {
            if (m == null) return;
            ArObjectUtil.Destroy(m);
        }

        static void SetColorSafe(Material m, int id, Color c)
        {
            if (m.HasProperty(id)) m.SetColor(id, c);
        }

        static void SetFloatSafe(Material m, int id, float v)
        {
            if (m.HasProperty(id)) m.SetFloat(id, v);
        }
    }
}
