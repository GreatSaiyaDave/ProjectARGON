using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Spirit Dueler BODY: translucent ghost-glass + fresnel rim, and the aura (<see cref="SpiritDuelerAura"/>:
    /// silhouette flames, runes, coils, edge streaks, trailing sparkles). Good magic —
    /// every aura layer adds light; nothing on the Spirit Dueler is drawn dark, and there is no smoke.
    /// Does not move card-zone markers. Those stay on OfficialZones.
    /// </summary>
    public class SpiritDuelerSkin : MonoBehaviour
    {
        public const float WristHoverY = 0.034f;
        public const float FadeInSeconds = 0.45f;
        public const float FadeOutSeconds = 0.55f;

        Material _skin;
        Transform _wrist;
        Transform _auraRoot;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);
        Color _auraColor = new(0.25f, 0.9f, 1f, 1f);
        Color _fromDisk, _toDisk, _fromAura, _toAura;
        float _fadeT = -1f, _fadeDur;
        DiskFxDriver _fx;
        MeshRenderer _diskMr;
        SpiritDuelerAura _manifest;
        bool _deployPulsed;
        bool _playerSide = true;
        float _presence = 1f;
        Coroutine _fadeCo;
        SpiritDuelerMechanics _mechanics;
        readonly System.Collections.Generic.List<MeshRenderer> _parts = new();

        public Material SkinMaterial => _skin;
        public float Presence => _presence;

        public static SpiritDuelerSkin Attach(ArDuelDiskRig rig)
        {
            if (rig == null) return null;
            var skin = ArObjectUtil.GetOrAdd<SpiritDuelerSkin>(rig.gameObject);
            skin._wrist = rig.transform;
            skin._accent = rig.Accent;
            skin._auraColor = rig.AuraColor;
            skin._playerSide = rig.IsPlayerSide;
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

            // the Battle City mechanism: every kit part wears the same ghost glass and fades with the body
            _mechanics = rig.Mechanics;
            _parts.Clear();
            if (_mechanics != null)
            {
                foreach (var p in _mechanics.PartRenderers)
                {
                    if (p == null) continue;
                    p.sharedMaterial = _skin;
                    ArAnimePresentation.ConfigureHoloRenderer(p);
                    _parts.Add(p);
                }
            }

            BuildAura();
            ApplyPresence(_presence);
        }

        public void SetAccent(Color accent) => SetColors(accent, accent, 0f);

        /// <summary>The aura colour on screen right now.</summary>
        public Color AuraColorNow => _auraColor;

        /// <summary>
        /// Palette swap: the ghost glass takes <paramref name="disk"/>, the aura (and the mechanism's light-ups)
        /// <paramref name="aura"/>, crossfading over <paramref name="fadeSeconds"/>.
        /// </summary>
        public void SetColors(Color disk, Color aura, float fadeSeconds)
        {
            disk.a = aura.a = 1f;
            if (fadeSeconds <= 0f || !Application.isPlaying)
            {
                _fadeT = -1f;
                _accent = disk;
                _auraColor = aura;
                PushAuraColor();
                ApplyPresence(_presence);
                return;
            }

            _fromDisk = _accent;
            _fromAura = _auraColor;
            _toDisk = disk;
            _toAura = aura;
            _fadeDur = fadeSeconds;
            _fadeT = 0f;
        }

        void PushAuraColor()
        {
            _manifest?.SetAccent(_auraColor);
            _mechanics?.SetAccent(_auraColor);
        }

        void TickPaletteFade()
        {
            if (_fadeT < 0f) return;
            _fadeT += Time.unscaledDeltaTime;
            var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_fadeT / Mathf.Max(0.01f, _fadeDur)));
            _accent = Color.Lerp(_fromDisk, _toDisk, u);
            _auraColor = Color.Lerp(_fromAura, _toAura, u);
            PushAuraColor();
            if (u >= 1f) _fadeT = -1f;
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
            // SpiritGhostUnlit is pulled into player builds by its anchor material
            // (Presentation/Resources/WRLDZ/ShaderAnchors). Fallback: HoloUnlit, which honours the blend that
            // SetTransparent picks (Sprites/Default ignored it); both are always in the build.
            var named = Shader.Find("WRLDZ/SpiritGhostUnlit");
            if (named != null && named.isSupported) return named;
            return ArFieldMaterials.HoloShader;
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
            _manifest?.SetPresence(_presence);
            if (_diskMr != null)
                _diskMr.enabled = _presence > 0.02f;
            foreach (var part in _parts)
                if (part != null) part.enabled = _presence > 0.02f;
        }

        void BuildAura()
        {
            if (_wrist == null) return;
            if (_auraRoot != null)
            {
                if (_manifest == null)
                    _manifest = _auraRoot.GetComponentInChildren<SpiritDuelerAura>(true);
                return;
            }
            _auraRoot = new GameObject("WristAetherAura").transform;
            _auraRoot.SetParent(_wrist, false);
            _auraRoot.localPosition = Vector3.zero;
            _auraRoot.localRotation = Quaternion.identity;

            _manifest = SpiritDuelerAura.Create(_auraRoot, _wrist.gameObject.layer, _auraColor, _diskMr);
            if (_parts.Count > 0) _manifest.SetBodyParts(_parts, SpiritDuelerMechanics.CarrierPivot);
            // your own aura at full strength; the opponent's a little quieter so it never buries the field
            _manifest.Emphasis = _playerSide ? 1f : 0.75f;
        }

        void LateUpdate()
        {
            TickPaletteFade();
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
            if (_manifest == null || _presence <= 0.02f) return;
            // The stage camera, not Camera.main: in EditorSim / phone-RT the stage renders through the untagged
            // ArCam while DuelSlice's MainCamera sits far to the side, and on ARCore two cameras carry the
            // MainCamera tag. FindCamera is cached per frame, so resolving every tick is cheap and follows camera
            // swaps (free-look, lenses).
            var cam = ArStageView.FindCamera(transform);
            var open = _fx != null ? _fx.BladeOpen : 1f;
            var pulse = _fx != null ? _fx.EnergyPulse : 0f;
            if (_mechanics != null && _fx != null)
            {
                // in standby the blades are inside the hub: only the hub's outline sheds streaks and sparkles
                var m = _mechanics.Pose;
                _manifest.OutlineReach = m >= 0.99f
                    ? float.PositiveInfinity
                    : Mathf.Lerp(SpiritDuelerMechanics.HubRadius, 0.6f, m * m);
            }

            _manifest.Tick(_presence, open, pulse, cam);
            if (open >= 0.35f && !_deployPulsed)
            {
                _deployPulsed = true;
                _manifest.PulseDeploy();
            }
            else if (open < 0.2f)
            {
                _deployPulsed = false;
            }
        }
    }
}
