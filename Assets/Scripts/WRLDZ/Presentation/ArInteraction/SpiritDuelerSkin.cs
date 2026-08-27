using UnityEngine;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Spirit Dueler plate skin: opaque KC gunmetal so sculpted pads,
    /// triangles, and S/T slits read against the cards. Soft cyan/rose
    /// emission veins. Wrist smoke still tethers the hovering plate.
    /// Deck well is the wrist calibration point.
    /// </summary>
    public class SpiritDuelerSkin : MonoBehaviour
    {
        public const float WristHoverY = 0.034f;
        const int WispCount = 7;

        Material _skin;
        Material _smokeMat;
        Transform _wrist;
        Transform _deck;
        Transform _auraRoot;
        Transform[] _wisps;
        float[] _wispPhase;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);
        bool _playerSide = true;
        Camera _billboardCam;
        DiskFxDriver _fx;

        public Material SkinMaterial => _skin;

        public static SpiritDuelerSkin Attach(ArDuelDiskRig rig)
        {
            if (rig == null) return null;
            var skin = rig.GetComponent<SpiritDuelerSkin>() ?? rig.gameObject.AddComponent<SpiritDuelerSkin>();
            skin._wrist = rig.transform;
            skin._deck = rig.MainDeckZone;
            skin._accent = rig.Accent;
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

            if (rig.TryGetComponent<MeshRenderer>(out _)) { /* rig itself has no mesh */ }

            var diskMr = rig.GetComponentInChildren<MeshRenderer>();
            // Prefer the Battle City mesh renderer (named DiskMesh)
            foreach (var mr in rig.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr != null && mr.gameObject.name == "DiskMesh")
                {
                    diskMr = mr;
                    break;
                }
            }

            if (diskMr != null && diskMr.sharedMaterial != null &&
                diskMr.sharedMaterial.name == "SpiritDuelerSkin")
                _skin = diskMr.sharedMaterial;
            else
            {
                _skin = BuildGhostMaterial(_accent);
                if (diskMr != null)
                    diskMr.sharedMaterial = _skin;
            }

            BuildAura();
        }

        public static Material BuildGhostMaterial(Color accent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "SpiritDuelerSkin" };
            var emit = ImagineAssets.SpiritDiskEmission();

            // Opaque KC gunmetal — sculpted pads, triangles, and S/T slits
            // have to read against the cards. The circular glass albedo is
            // a round-disk painting and has no UVs on this CAD; skip it.
            var plate = new Color(0.10f, 0.13f, 0.18f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", plate);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", plate);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.42f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.48f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.48f);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 2000;

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accent * 0.22f);
            }
            if (mat.HasProperty("_EmissionMap") && emit != null)
                mat.SetTexture("_EmissionMap", emit);
            return mat;
        }

        void BuildAura()
        {
            if (_wrist == null) return;
            _auraRoot = new GameObject("WristAetherAura").transform;
            _auraRoot.SetParent(_wrist, false);
            _auraRoot.localPosition = Vector3.zero;
            _auraRoot.localRotation = Quaternion.identity;

            var smoke = ImagineAssets.SpiritWristSmoke();
            _smokeMat = ArFieldMaterials.GetTransparent(new Color(0.65f, 0.92f, 1f, 0.22f));
            if (smoke != null)
            {
                if (_smokeMat.HasProperty("_MainTex")) _smokeMat.SetTexture("_MainTex", smoke);
                if (_smokeMat.HasProperty("_BaseMap")) _smokeMat.SetTexture("_BaseMap", smoke);
            }

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
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
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
            // Keep the gunmetal plate stable so pad edges stay readable.
            // Only the emission veins breathe with the Spirit accent.
            var plate = new Color(0.10f, 0.13f, 0.18f, 1f);
            if (_skin.HasProperty("_BaseColor")) _skin.SetColor("_BaseColor", plate);
            if (_skin.HasProperty("_Color")) _skin.SetColor("_Color", plate);
            if (_skin.HasProperty("_EmissionColor"))
            {
                _skin.EnableKeyword("_EMISSION");
                var pulse = 0.16f + 0.06f * Mathf.Sin(t * 0.9f);
                var boost = _fx != null ? Mathf.Max(0.2f, _fx.EmissionBoost * 0.35f) : 1f;
                var emit = _playerSide
                    ? new Color(0.25f, 0.75f, 1f) * pulse * boost
                    : new Color(1f, 0.40f, 0.62f) * pulse * boost;
                _skin.SetColor("_EmissionColor", emit);
            }
        }

        void TickAura()
        {
            if (_wisps == null || _wrist == null) return;
            var from = _wrist.position;
            var to = _deck != null ? _deck.position : from + _wrist.up * WristHoverY;
            var mid = Vector3.Lerp(from, to, 0.45f) + _wrist.up * 0.018f;
            if (_billboardCam == null)
                _billboardCam = Camera.main;

            var t = Time.unscaledTime;
            for (var i = 0; i < _wisps.Length; i++)
            {
                var w = _wisps[i];
                if (w == null) continue;
                var u = Mathf.Repeat(t * 0.22f + _wispPhase[i], 1f);
                var p = QuadBezier(from, mid, to, u);
                var swirl = _wrist.right * (Mathf.Sin(t * 1.4f + i) * 0.012f)
                            + _wrist.forward * (Mathf.Cos(t * 1.1f + i * 0.7f) * 0.008f);
                w.position = p + swirl;
                var s = 0.028f + 0.018f * (1f - Mathf.Abs(u - 0.5f) * 2f);
                w.localScale = Vector3.one * s;
                if (_billboardCam != null)
                    w.rotation = Quaternion.LookRotation(w.position - _billboardCam.transform.position,
                        _billboardCam.transform.up);
                var a = 0.10f + 0.10f * Mathf.Sin(u * Mathf.PI);
                if (_smokeMat != null)
                {
                    var col = _playerSide
                        ? new Color(0.65f, 0.92f, 1f, a)
                        : new Color(1f, 0.55f, 0.72f, a * 0.85f);
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
