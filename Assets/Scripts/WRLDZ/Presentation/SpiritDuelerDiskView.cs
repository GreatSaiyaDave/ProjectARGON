using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Spirit Dueler wearable presentation — Battle City duel disk 3D model
    /// rendered to a UI RawImage (left-arm metaphor for portrait; AR will re-parent later).
    ///
    /// Model source: Meshy Battle City disk (blade arm) → StreamingAssets/Models/SpiritDueler/
    /// </summary>
    public class SpiritDuelerDiskView : MonoBehaviour
    {
        public const string StreamingRelativePath = "Models/SpiritDueler/BattleCityDuelDisk.obj";
        public const string StreamingGlbRelative = "Models/SpiritDueler/BattleCityDuelDisk.glb";

        public enum PoseMode
        {
            /// <summary>Player disk — lower field, left-arm angle.</summary>
            PlayerArm,
            /// <summary>Mirrored opponent disk — upper field.</summary>
            OpponentMirror,
            /// <summary>Hub hero disk — slow spin showcase.</summary>
            HubShowcase,
            /// <summary>Overworld bottom disk button.</summary>
            MapBadge
        }

        RenderTexture _rt;
        Camera _cam;
        Transform _stage;
        Transform _diskRoot;
        MeshFilter _mf;
        MeshRenderer _mr;
        RawImage _target;
        PoseMode _pose;
        float _spin;
        bool _ready;
        Color _tint = new(0.25f, 0.85f, 1f, 1f);

        static Mesh _sharedMesh;
        static Material _sharedMat;

        /// <summary>
        /// Spawn a disk view that draws into <paramref name="rawImage"/> (must already be on canvas).
        /// </summary>
        public static SpiritDuelerDiskView Attach(RawImage rawImage, PoseMode pose, Color? accent = null,
            Transform lifetimeParent = null)
        {
            if (rawImage == null) return null;
            var host = new GameObject("SpiritDuelerDisk_" + pose);
            if (lifetimeParent != null)
                host.transform.SetParent(lifetimeParent, false);
            var view = host.AddComponent<SpiritDuelerDiskView>();
            view._target = rawImage;
            view._pose = pose;
            if (accent.HasValue) view._tint = accent.Value;
            view.BuildStage();
            return view;
        }

        /// <summary>Create a RawImage slot on a UI parent and attach the disk view.</summary>
        public static SpiritDuelerDiskView CreateInUi(Transform uiParent, PoseMode pose,
            float x0, float y0, float x1, float y1, Color? accent = null, Transform lifetimeParent = null,
            bool raycastTarget = false)
        {
            var go = new GameObject("SpiritDiskSlot_" + pose, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(uiParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = raycastTarget;
            return Attach(raw, pose, accent, lifetimeParent ?? uiParent.root);
        }

        void BuildStage()
        {
            EnsureMesh();
            if (_sharedMesh == null)
            {
                Debug.LogWarning("[WRLDZ] Spirit Dueler disk mesh missing — UI will fall back.");
                return;
            }

            // Isolated render layer 30
            const int layer = 30;

            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("DiskCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.08f, 0f);
            _cam.orthographic = false;
            _cam.fieldOfView = 32f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 20f;
            _cam.cullingMask = 1 << layer;
            _cam.depth = -50;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;

            var size = 512;
            _rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "SpiritDiskRT_" + _pose,
                antiAliasing = 2
            };
            _cam.targetTexture = _rt;
            if (_target != null)
            {
                _target.texture = _rt;
                _target.color = Color.white;
            }

            _diskRoot = new GameObject("DiskRoot").transform;
            _diskRoot.SetParent(_stage, false);
            SetLayerRecurse(_diskRoot.gameObject, layer);

            var disk = new GameObject("DiskMesh", typeof(MeshFilter), typeof(MeshRenderer));
            disk.transform.SetParent(_diskRoot, false);
            _mf = disk.GetComponent<MeshFilter>();
            _mr = disk.GetComponent<MeshRenderer>();
            _mf.sharedMesh = _sharedMesh;
            _mr.sharedMaterial = CreateMaterial(_tint);
            SetLayerRecurse(disk, layer);

            // Soft ground glow plate
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "GlowPlate";
            plate.transform.SetParent(_diskRoot, false);
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plate.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            plate.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
            UnityEngine.Object.Destroy(plate.GetComponent<Collider>());
            var plateMr = plate.GetComponent<MeshRenderer>();
            plateMr.sharedMaterial = CreateGlowMaterial(_tint);
            SetLayerRecurse(plate, layer);

            ApplyPoseCamera();
            _ready = true;
            Debug.Log($"[WRLDZ] Spirit Dueler disk ready ({_pose}) mesh={_sharedMesh.vertexCount}v");
        }

        void ApplyPoseCamera()
        {
            // Model extents ~ 1 x 0.09 x 0.51 after normalize — treat as blade along X
            switch (_pose)
            {
                case PoseMode.PlayerArm:
                    // Left-arm wear: slight pitch, disk faces player
                    _diskRoot.localRotation = Quaternion.Euler(18f, -35f, -12f);
                    _diskRoot.localPosition = Vector3.zero;
                    _cam.transform.localPosition = new Vector3(0.15f, 0.45f, -1.55f);
                    _cam.transform.LookAt(_diskRoot.position + new Vector3(0f, 0.02f, 0f));
                    _spin = 8f;
                    break;
                case PoseMode.OpponentMirror:
                    _diskRoot.localRotation = Quaternion.Euler(-12f, 145f, 8f);
                    _cam.transform.localPosition = new Vector3(-0.1f, 0.4f, -1.5f);
                    _cam.transform.LookAt(_diskRoot.position);
                    _spin = -6f;
                    _tint = new Color(1f, 0.35f, 0.55f, 1f);
                    if (_mr != null) _mr.sharedMaterial = CreateMaterial(_tint);
                    break;
                case PoseMode.HubShowcase:
                    _diskRoot.localRotation = Quaternion.Euler(25f, 0f, 0f);
                    _cam.transform.localPosition = new Vector3(0f, 0.55f, -1.7f);
                    _cam.transform.LookAt(Vector3.zero);
                    _spin = 22f;
                    break;
                case PoseMode.MapBadge:
                    _diskRoot.localRotation = Quaternion.Euler(40f, -20f, 0f);
                    _cam.transform.localPosition = new Vector3(0f, 0.5f, -1.4f);
                    _cam.transform.LookAt(Vector3.zero);
                    _spin = 14f;
                    break;
            }
        }

        void Update()
        {
            if (!_ready || _diskRoot == null) return;
            // Slow showcase spin — Spirit Dueler idle hologram
            _diskRoot.Rotate(Vector3.up, _spin * Time.deltaTime, Space.World);
            // Subtle hover
            var y = Mathf.Sin(Time.time * 1.4f) * 0.02f;
            var lp = _diskRoot.localPosition;
            _diskRoot.localPosition = new Vector3(lp.x, y, lp.z);
        }

        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }

        public void SetAccent(Color c)
        {
            _tint = c;
            if (_mr != null) _mr.sharedMaterial = CreateMaterial(_tint);
        }

        /// <summary>Pulse when reacting / impact window (combat drama).</summary>
        public void PulseCombat(float intensity01)
        {
            if (_mr == null) return;
            var e = Mathf.Clamp01(intensity01) * 2.5f;
            var mat = _mr.material;
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", _tint * e);
        }

        static void EnsureMesh()
        {
            if (_sharedMesh != null) return;

            // Prefer StreamingAssets copy (always present after deploy)
            var stream = Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);
            if (File.Exists(stream))
            {
                _sharedMesh = ObjMeshLoader.LoadFromFile(stream, "BattleCityDuelDisk");
                if (_sharedMesh != null) return;
            }

            // Editor project path fallback
            var proj = Path.Combine(Application.dataPath, "Models/WRLDZ/SpiritDueler/BattleCityDuelDisk.obj");
            if (File.Exists(proj))
                _sharedMesh = ObjMeshLoader.LoadFromFile(proj, "BattleCityDuelDisk");
        }

        static Material CreateMaterial(Color tint) => SpiritDuelerSkin.BuildGhostMaterial(tint);

        static Material CreateGlowMaterial(Color tint) => SpiritDuelerSkin.BuildGlowLayer(tint);

        static void SetLayerRecurse(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayerRecurse(t.gameObject, layer);
        }

        /// <summary>Absolute path helper for tooling / docs.</summary>
        public static string ResolveObjPath()
        {
            var stream = Path.Combine(Application.streamingAssetsPath, StreamingRelativePath);
            if (File.Exists(stream)) return stream;
            return Path.Combine(Application.dataPath, "Models/WRLDZ/SpiritDueler/BattleCityDuelDisk.obj");
        }
    }
}
