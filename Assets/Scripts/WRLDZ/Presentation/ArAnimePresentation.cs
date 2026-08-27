using UnityEngine;
using UnityEngine.Rendering;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Anime-style Solid Vision presentation for phone AR + AR lenses.
    ///
    /// Design goals (product):
    /// · Real-world passthrough with <b>variable lighting</b> (noon sun → night indoor).
    /// · Holos read as <b>2D anime on 3D surfaces</b> (cel / unlit), not photoreal PBR.
    /// · Same interaction graph on phone RT and OpenXR lenses (no second rules engine).
    /// · Monsters stay art holograms until dedicated meshes exist; cards get physical thickness now.
    ///
    /// Why Unlit + exposure fit (not full Lit):
    /// Outdoor AR has harsh ambient; URP Lit goes black or blows out without per-device probes.
    /// Unlit keeps card art readable; <see cref="ExposureMul"/> lifts/dims so holos pop on any sky.
    /// </summary>
    public static class ArAnimePresentation
    {
        /// <summary>TCG card aspect ~59×86 mm → height / width.</summary>
        public const float CardAspectY = 1.45f;

        /// <summary>Physical card thickness in local mesh units (before zone scale).</summary>
        public const float CardThickness = 0.055f;

        /// <summary>Soft hologram rim inflate (edge chrome).</summary>
        public const float RimInflate = 1.06f;

        /// <summary>
        /// Multiplier applied to unlit base colors. 1 = authoring default.
        /// Outdoor bright passthrough → slightly higher so cards stay visible.
        /// Dark rooms → slightly lower so they don't blow out the camera.
        /// </summary>
        public static float ExposureMul { get; private set; } = 1f;

        /// <summary>Last estimated scene luminance 0…1 (for HUD / debug).</summary>
        public static float EstimatedLuma { get; private set; } = 0.45f;

        static float _smoothLuma = 0.45f;

        /// <summary>
        /// Call once per frame from AR stage (phone or lenses).
        /// Uses main directional/point light + ambient as a cheap real-world proxy.
        /// </summary>
        public static void TickExposure(Camera stageCam = null)
        {
            // Ambient sky/equator average
            var amb = RenderSettings.ambientLight;
            var ambL = 0.2126f * amb.r + 0.7152f * amb.g + 0.0722f * amb.b;

            // Cheap light proxy — avoid FindObjects every tick (mobile AR)
            var lightL = 0f;
            var main = RenderSettings.sun;
            if (main != null && main.isActiveAndEnabled)
                lightL += main.intensity * 0.35f;
            else if (stageCam != null)
            {
                // Approximate outdoor brightness from camera background when no sun
                // (passthrough stages often keep SolidColor clear under the feed)
                lightL += 0.2f;
            }

            // Camera background as last resort (editor solid void)
            if (stageCam != null && stageCam.clearFlags == CameraClearFlags.SolidColor)
            {
                var bg = stageCam.backgroundColor;
                ambL = Mathf.Max(ambL, 0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b);
            }

            var raw = Mathf.Clamp01(ambL * 0.65f + Mathf.Clamp01(lightL) * 0.55f);
            _smoothLuma = Mathf.Lerp(_smoothLuma, raw, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
            EstimatedLuma = _smoothLuma;

            // Bright world → boost holos slightly (they sit on passthrough)
            // Dark world → pull back so cyan rims don't blow the image
            // Curve centered on 0.4 indoor lab
            if (_smoothLuma > 0.55f)
                ExposureMul = Mathf.Lerp(1.05f, 1.28f, Mathf.InverseLerp(0.55f, 0.95f, _smoothLuma));
            else if (_smoothLuma < 0.28f)
                ExposureMul = Mathf.Lerp(0.88f, 1f, Mathf.InverseLerp(0.05f, 0.28f, _smoothLuma));
            else
                ExposureMul = 1f;
        }

        /// <summary>Apply exposure to a color for unlit materials.</summary>
        public static Color Expose(Color c)
        {
            var m = ExposureMul;
            return new Color(
                Mathf.Clamp01(c.r * m),
                Mathf.Clamp01(c.g * m),
                Mathf.Clamp01(c.b * m),
                c.a);
        }

        public static Shader UnlitShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit")
                   ?? Shader.Find("Unlit/Texture")
                   ?? Shader.Find("Unlit/Color")
                   ?? Shader.Find("Sprites/Default")
                   ?? Shader.Find("UI/Default");
        }

        /// <summary>
        /// Anime face material: unlit, double-sided, exposure-aware, art true-color.
        /// </summary>
        public static Material MakeFaceMaterial(Texture tex, Color tint, string name = "AnimeFace")
        {
            var sh = UnlitShader();
            if (sh == null)
            {
                var fb = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
                fb.color = new Color(0.3f, 0.7f, 1f, 1f);
                return fb;
            }

            var mat = new Material(sh) { name = name };
            if (tex == null) tex = Texture2D.whiteTexture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            var col = tex != Texture2D.whiteTexture
                ? Expose(Color.white)
                : Expose(Color.Lerp(Color.white, tint, 0.4f));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            // CullMode.Off = 0 — required so billboarded opp holos never blank
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            // URP Unlit sometimes needs explicit keyword / render queue for both faces
            mat.EnableKeyword("_DOUBLESIDED_ON");
            return mat;
        }

        /// <summary>Solid unlit accent (edge chrome, rings) with exposure.</summary>
        public static Material MakeSolid(Color c, string name = "AnimeSolid")
        {
            var sh = UnlitShader();
            if (sh == null) return new Material(Shader.Find("UI/Default"));
            var mat = new Material(sh) { name = name };
            var col = Expose(c);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            return mat;
        }

        /// <summary>
        /// Soft additive-ish rim (still unlit; higher alpha on bright worlds via expose).
        /// </summary>
        public static Material MakeHoloRim(Color accent)
        {
            var c = accent;
            c.a = Mathf.Clamp01(0.35f + (1f - EstimatedLuma) * 0.15f);
            return MakeSolid(c, "AnimeHoloRim");
        }

        /// <summary>Disable shadows — holos should not cast realistic shadows in AR passthrough.</summary>
        public static void ConfigureHoloRenderer(MeshRenderer mr)
        {
            if (mr == null) return;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
