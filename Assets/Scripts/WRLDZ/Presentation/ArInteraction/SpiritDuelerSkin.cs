using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Spirit Dueler BODY only: translucent ghost-glass + fresnel rim + wrist wisps.
    /// Does not move card-zone markers. Those stay on OfficialZones.
    /// </summary>
    public class SpiritDuelerSkin : MonoBehaviour
    {
        public const float WristHoverY = 0.034f;
        const int WispCount = 7;
        public const float FadeInSeconds = 0.45f;
        public const float FadeOutSeconds = 0.55f;

        Material _skin;
        Material _smokeMat;
        Transform _wrist;
        Transform _deck;
        Transform _auraRoot;
        Transform[] _wisps;
        float[] _wispPhase;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);
        Camera _billboardCam;
        DiskFxDriver _fx;
        MeshRenderer _diskMr;
        float _presence = 1f;
        Coroutine _fadeCo;

        public Material SkinMaterial => _skin;
        public float Presence => _presence;

        public static SpiritDuelerSkin Attach(ArDuelDiskRig rig)
        {
            if (rig == null) return null;
            var skin = rig.GetComponent<SpiritDuelerSkin>() ?? rig.gameObject.AddComponent<SpiritDuelerSkin>();
            skin._wrist = rig.transform;
            skin._deck = rig.MainDeckZone;
            skin._accent = rig.Accent;
            skin._fx = rig.Fx;
            skin.Build(rig);
            return skin;
        }

        /// <summary>
        /// Place DiskRoot so the deployed deck well sits on the wrist origin
        /// and the plate hovers a few centimeters above the arm.
        /// </summary>
        public static Vector3 WristCalibratedDiskRoot()
        {
            var s = ArPlaymatLayout.DiskMeshVisualScale;
            var deckInDisk = DiskFxDriver.BladeDeployedPos + ArDeckWellCards.WellOrigin * s;
            return new Vector3(0f, WristHoverY, 0f) - deckInDisk;
        }

        void Build(ArDuelDiskRig rig)
        {
            if (rig.DiskRoot != null)
            {
                rig.DiskRoot.localPosition = WristCalibratedDiskRoot();
                rig.Fx?.CaptureBase(rig.DiskRoot);
            }

            _diskMr = null;
            foreach (var mr in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.gameObject.name == "DiskMesh")
                {
                    _diskMr = mr;
                    break;
                }
            }

            if (_diskMr == null)
                _diskMr = rig.GetComponentInChildren<MeshRenderer>();

            var extra = _diskMr != null ? _diskMr.transform.Find("DiskMeshGlow") : null;
            if (extra != null)
                Object.Destroy(extra.gameObject);

            _skin = BuildGhostMaterial(_accent);
            if (_diskMr != null)
            {
                _diskMr.sharedMaterial = _skin;
                ArAnimePresentation.ConfigureHoloRenderer(_diskMr);
            }

            BuildAura();
            ApplyPresence(_presence);
        }

        public void SetAccent(Color accent)
        {
            _accent = accent;
            if (_accent.a < 0.01f) _accent.a = 1f;
            ApplyPresence(_presence);
        }

        public void SnapPresence(float value)
        {
            if (_fadeCo != null)
            {
                StopCoroutine(_fadeCo);
                _fadeCo = null;
            }

            ApplyPresence(Mathf.Clamp01(value));
        }

        public void FadePresence(float to, float seconds)
        {
            if (!isActiveAndEnabled || !Application.isPlaying)
            {
                SnapPresence(to);
                return;
            }

            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(FadeCo(to, Mathf.Max(0.05f, seconds)));
        }

        IEnumerator FadeCo(float to, float seconds)
        {
            var from = _presence;
            var t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                ApplyPresence(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }

            ApplyPresence(to);
            _fadeCo = null;
        }

        static Shader GhostShader()
        {
            var named = Shader.Find("WRLDZ/SpiritGhostUnlit");
            if (named != null && named.isSupported) return named;
            return Shader.Find("Sprites/Default")
                   ?? Shader.Find("Universal Render Pipeline/Unlit")
                   ?? Shader.Find("Unlit/Color")
                   ?? Shader.Find("UI/Default");
        }

        /// <summary>
        /// Translucent ghost plate — fill + fresnel, not additive white, not umbra black.
        /// </summary>
        public static Material BuildGhostMaterial(Color accent)
        {
            var mat = new Material(GhostShader()) { name = "SpiritDuelerSkin" };
            var tex = ImagineAssets.SpiritDiskAlbedo() ?? Texture2D.whiteTexture;
            BindTex(mat, tex, new Vector2(1.35f, 1.35f));
            SetTransparent(mat, alphaBlend: true);
            if (mat.HasProperty("_Fill")) mat.SetFloat("_Fill", 0.34f);
            if (mat.HasProperty("_RimPower")) mat.SetFloat("_RimPower", 2.6f);
            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 1.15f);
            ApplyGhostColors(mat, accent, 1f, 0.34f);
            return mat;
        }

        /// <summary>Dim underglow for the hub badge plate — never a second body copy.</summary>
        public static Material BuildGlowLayer(Color accent)
        {
            var mat = new Material(GhostShader()) { name = "SpiritDuelerUnderglow" };
            BindTex(mat, Texture2D.whiteTexture, Vector2.one);
            SetTransparent(mat, alphaBlend: true);
            if (mat.HasProperty("_Fill")) mat.SetFloat("_Fill", 0.18f);
            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0.35f);
            ApplyGhostColors(mat, accent, 1f, 0.18f);
            return mat;
        }

        static Material BuildWispMaterial(Color accent, Texture smoke)
        {
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? GhostShader();
            var mat = new Material(sh) { name = "SpiritShadowWisp" };
            BindTex(mat, smoke ?? Texture2D.whiteTexture, Vector2.one);
            SetTransparent(mat, alphaBlend: false);
            ApplyGhostColors(mat, accent, 1f, 0.55f);
            return mat;
        }

        static void BindTex(Material mat, Texture tex, Vector2 tiling)
        {
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", tex);
                mat.SetTextureScale("_MainTex", tiling);
            }

            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", tiling);
            }
        }

        static void SetTransparent(Material mat, bool alphaBlend)
        {
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", alphaBlend ? 0f : 2f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", alphaBlend ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_DOUBLESIDED_ON");
            mat.doubleSidedGI = true;
            mat.renderQueue = 3000;
        }

        static void ApplyGhostColors(Material mat, Color accent, float presence, float fill)
        {
            if (mat == null) return;
            var glass = Color.Lerp(Color.white, accent, 0.78f);
            glass = ArAnimePresentation.Expose(glass);
            glass.a = Mathf.Clamp01((0.34f + 0.12f * presence) * presence);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", glass);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", glass);
            if (mat.HasProperty("_Fill"))
                mat.SetFloat("_Fill", Mathf.Clamp01(fill) * (0.75f + 0.25f * presence));
            if (mat.HasProperty("_RimStrength"))
            {
                var pulse = 1.05f + 0.18f * presence;
                mat.SetFloat("_RimStrength", pulse);
            }
        }

        void ApplyPresence(float p)
        {
            _presence = Mathf.Clamp01(p);
            if (_auraRoot != null)
                _auraRoot.gameObject.SetActive(_presence > 0.02f);
            if (_diskMr != null)
                _diskMr.enabled = _presence > 0.02f;
        }

        void BuildAura()
        {
            if (_wrist == null) return;
            if (_auraRoot != null) return;
            _auraRoot = new GameObject("WristAetherAura").transform;
            _auraRoot.SetParent(_wrist, false);
            _auraRoot.localPosition = Vector3.zero;
            _auraRoot.localRotation = Quaternion.identity;

            var smoke = ImagineAssets.SpiritWristSmoke();
            _smokeMat = BuildWispMaterial(
                Color.Lerp(new Color(0.45f, 0.22f, 0.85f, 1f), _accent, 0.55f),
                smoke);

            _wisps = new Transform[WispCount];
            _wispPhase = new float[WispCount];
            for (var i = 0; i < WispCount; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "AetherWisp_" + i;
                go.transform.SetParent(_auraRoot, false);
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _smokeMat;
                ArAnimePresentation.ConfigureHoloRenderer(mr);
                go.layer = _wrist.gameObject.layer;
                _wisps[i] = go.transform;
                _wispPhase[i] = i * 0.37f;
            }
        }

        void LateUpdate()
        {
            TickColor();
            TickAura();
        }

        void TickColor()
        {
            if (_skin == null) return;
            var t = Time.unscaledTime;
            var pulse = 0.08f * Mathf.Sin(t * 0.9f);
            var boost = _fx != null ? Mathf.Clamp01(_fx.EmissionBoost * 0.12f) : 0f;
            ApplyGhostColors(_skin, _accent, _presence, 0.32f + pulse + boost);
            Scroll(_skin, t * 0.03f, t * 0.018f);
        }

        static void Scroll(Material mat, float u, float v)
        {
            if (mat == null) return;
            var o = new Vector2(u, v);
            if (mat.HasProperty("_MainTex")) mat.SetTextureOffset("_MainTex", o);
            if (mat.HasProperty("_BaseMap")) mat.SetTextureOffset("_BaseMap", o);
        }

        void TickAura()
        {
            if (_wisps == null || _wrist == null || _presence <= 0.02f) return;
            var from = _wrist.position;
            var to = _deck != null ? _deck.position : from + _wrist.up * WristHoverY;
            var mid = Vector3.Lerp(from, to, 0.45f) + _wrist.up * 0.018f;
            if (_billboardCam == null)
                _billboardCam = Camera.main;

            var t = Time.unscaledTime;
            var shadow = Color.Lerp(new Color(0.55f, 0.28f, 0.95f, 1f), _accent, 0.55f);
            for (var i = 0; i < _wisps.Length; i++)
            {
                var w = _wisps[i];
                if (w == null) continue;
                var u = Mathf.Repeat(t * 0.22f + _wispPhase[i], 1f);
                var p = QuadBezier(from, mid, to, u);
                var swirl = _wrist.right * (Mathf.Sin(t * 1.4f + i) * 0.012f)
                            + _wrist.forward * (Mathf.Cos(t * 1.1f + i * 0.7f) * 0.008f);
                w.position = p + swirl;
                var s = 0.042f + 0.028f * (1f - Mathf.Abs(u - 0.5f) * 2f);
                w.localScale = Vector3.one * s;
                if (_billboardCam != null)
                    w.rotation = Quaternion.LookRotation(w.position - _billboardCam.transform.position,
                        _billboardCam.transform.up);
                var a = (0.22f + 0.28f * Mathf.Sin(u * Mathf.PI)) * _presence;
                if (_smokeMat != null)
                {
                    var col = new Color(shadow.r, shadow.g, shadow.b, a);
                    if (_smokeMat.HasProperty("_Color")) _smokeMat.SetColor("_Color", col);
                    if (_smokeMat.HasProperty("_BaseColor")) _smokeMat.SetColor("_BaseColor", col);
                }
            }
        }

        static Vector3 QuadBezier(Vector3 a, Vector3 b, Vector3 c, float u)
        {
            var o = 1f - u;
            return o * o * a + 2f * o * u * b + u * u * c;
        }
    }
}
