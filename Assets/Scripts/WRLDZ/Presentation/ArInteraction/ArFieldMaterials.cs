using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Shared unlit materials for arena/disk primitives.
    /// Avoids allocating a new Material per CreatePrimitive (major GC + draw cost).
    /// </summary>
    public static class ArFieldMaterials
    {
        static readonly Dictionary<int, Material> Cache = new();
        static Shader _shader;

        static Shader Shader
        {
            get
            {
                if (_shader == null)
                    _shader = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit")
                              ?? UnityEngine.Shader.Find("Unlit/Color")
                              ?? UnityEngine.Shader.Find("UI/Default");
                return _shader;
            }
        }

        /// <summary>Quantize color and return a cached shared material.</summary>
        public static Material Get(Color c)
        {
            // 32-step quantize keeps palette small while preserving look
            var key = ((int)(c.r * 31.9f) << 24) |
                      ((int)(c.g * 31.9f) << 16) |
                      ((int)(c.b * 31.9f) << 8) |
                      (int)(c.a * 31.9f);
            if (Cache.TryGetValue(key, out var mat) && mat != null)
                return mat;

            mat = new Material(Shader) { name = "ArFieldMat_" + key.ToString("X8") };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            Cache[key] = mat;
            return mat;
        }

        public static void Apply(MeshRenderer mr, Color c)
        {
            if (mr != null) mr.sharedMaterial = Get(c);
        }

        static readonly Dictionary<int, Material> AlphaCache = new();

        /// <summary>Mostly-transparent unlit glass for legal-zone highlights.</summary>
        public static Material GetTransparent(Color c)
        {
            var key = ((int)(c.r * 31.9f) << 24) |
                      ((int)(c.g * 31.9f) << 16) |
                      ((int)(c.b * 31.9f) << 8) |
                      (int)(c.a * 31.9f);
            if (AlphaCache.TryGetValue(key, out var mat) && mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                return mat;
            }

            var sh = UnityEngine.Shader.Find("Sprites/Default")
                     ?? UnityEngine.Shader.Find("Unlit/Color")
                     ?? UnityEngine.Shader.Find("Unlit/Transparent")
                     ?? Shader;
            mat = new Material(sh) { name = "ArLegalGlow_" + key.ToString("X8") };
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3100;
            AlphaCache[key] = mat;
            return mat;
        }

        static readonly Dictionary<int, Material> AddCache = new();

        /// <summary>Additive unlit for Ka-style hologram light (columns, rings, beams).</summary>
        public static Material GetAdditive(Color c)
        {
            var key = ((int)(c.r * 31.9f) << 24) |
                      ((int)(c.g * 31.9f) << 16) |
                      ((int)(c.b * 31.9f) << 8) |
                      (int)(c.a * 31.9f);
            if (AddCache.TryGetValue(key, out var mat) && mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                return mat;
            }

            var sh = UnityEngine.Shader.Find("Sprites/Default")
                     ?? UnityEngine.Shader.Find("Unlit/Color")
                     ?? Shader;
            mat = new Material(sh) { name = "ArHoloLight_" + key.ToString("X8") };
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3150;
            AddCache[key] = mat;
            return mat;
        }

        public static void ApplyAdditive(MeshRenderer mr, Color c)
        {
            if (mr != null) mr.sharedMaterial = GetAdditive(c);
        }

        /// <summary>
        /// Unlit textured material with culling off so flat set cards stay visible
        /// from both sides (ground-parallel cards edge-on to AR camera).
        /// </summary>
        public static Material CreateUnlitTextureDoubleSided(Texture tex)
        {
            var sh = Shader;
            if (sh == null)
            {
                // Last-resort so deck build never aborts pre-duel
                sh = UnityEngine.Shader.Find("Sprites/Default")
                     ?? UnityEngine.Shader.Find("UI/Default");
            }

            if (sh == null)
                return Get(new Color(0.15f, 0.25f, 0.5f, 1f));

            var mat = new Material(sh) { name = "ArFieldTexDoubleSided" };
            if (tex == null) tex = Texture2D.whiteTexture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            // Cull Off — critical for face-down flat cards
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            return mat;
        }

        public static void ForceDoubleSided(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_DOUBLESIDED_ON");
            mat.doubleSidedGI = true;
        }

        /// <summary>
        /// Cull backfaces so the controller looking at −Z sees the card-back
        /// quad, not art bleeding through a double-sided face.
        /// </summary>
        public static void ForceFrontFacesOnly(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f); // CullMode.Back
            mat.DisableKeyword("_DOUBLESIDED_ON");
            mat.doubleSidedGI = false;
        }
    }
}
