using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Hub dest / featured well: StreamingAssets OBJ rendered to a UI RawImage.
    /// Missing mesh or live-cap → caller keeps the 2D Imagine icon.
    /// </summary>
    public class HubPropView : MonoBehaviour
    {
        public const string PropsFolder = "Models/Props/";
        public const int DestRt = 256;
        public const int FeaturedRt = 384;
        const int Layer = 31;

        static int _live;
        static int _slot;
        static readonly List<HubPropView> Live = new();

        RenderTexture _rt;
        Camera _cam;
        Transform _root;
        Transform _body;
        bool _ready;
        float _spin = 22f;

        public static int LiveCount => _live;
        public static int LiveCap => Application.isMobilePlatform ? 3 : 8;
        public static bool CanSpawn => _live < LiveCap;

        /// <summary>
        /// Spawn a spinning prop in a UI well. Returns null if the OBJ is missing
        /// or the live cap is full — never leaves a black quad.
        /// </summary>
        public static HubPropView CreateInUi(Transform uiParent, string fileStem,
            float x0, float y0, float x1, float y1, Color? accent = null, int rtSize = DestRt)
        {
            if (uiParent == null || string.IsNullOrEmpty(fileStem) || !CanSpawn)
                return null;

            var mesh = LoadMesh(fileStem);
            if (mesh == null) return null;

            var slot = new GameObject("HubPropSlot_" + fileStem, typeof(RectTransform), typeof(RawImage));
            slot.transform.SetParent(uiParent, false);
            var rect = slot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var raw = slot.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;

            var host = new GameObject("HubPropStage_" + fileStem);
            host.transform.SetParent(WorldRoot(), false);
            var view = host.AddComponent<HubPropView>();
            view.Build(raw, mesh, fileStem, accent ?? DuelystUi.Cyan, Mathf.Max(128, rtSize));
            if (!view._ready)
            {
                Object.Destroy(host);
                Object.Destroy(slot);
                return null;
            }

            _live++;
            Live.Add(view);
            return view;
        }

        public static void DestroyAll()
        {
            foreach (var v in Live.ToArray())
            {
                if (v != null) Object.Destroy(v.gameObject);
            }
            Live.Clear();
            _live = 0;
        }

        public static Transform EnsureWorldRoot()
        {
            var go = GameObject.Find("HubPropWorld");
            if (go == null) go = new GameObject("HubPropWorld");
            return go.transform;
        }

        static Transform WorldRoot() => EnsureWorldRoot();

        public static bool MeshExists(string fileStem)
        {
            if (string.IsNullOrEmpty(fileStem)) return false;
            var path = Path.Combine(Application.streamingAssetsPath, PropsFolder + fileStem + ".obj");
            return File.Exists(path);
        }

        static Mesh LoadMesh(string stem)
        {
            var path = Path.Combine(Application.streamingAssetsPath, PropsFolder + stem + ".obj");
            if (!File.Exists(path)) return null;
            return ObjMeshLoader.LoadFromFile(path, stem);
        }

        static Texture2D LoadAlbedo(string stem)
        {
            var dir = Path.Combine(Application.streamingAssetsPath, PropsFolder);
            string[] names =
            {
                stem + ".png",
                stem + "_albedo.png",
                stem + "_basecolor.png",
                stem + "_BaseColor.png"
            };
            foreach (var n in names)
            {
                var p = Path.Combine(dir, n);
                if (!File.Exists(p)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(p);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = stem + "_albedo",
                        wrapMode = TextureWrapMode.Repeat,
                        filterMode = FilterMode.Bilinear
                    };
                    if (tex.LoadImage(bytes)) return tex;
                    Object.Destroy(tex);
                }
                catch
                {
                    // keep ghost material
                }
            }

            return null;
        }

        void Build(RawImage target, Mesh mesh, string stem, Color accent, int rtSize)
        {
            var world = new Vector3(90f + _slot * 14f, 0f, 0f);
            _slot++;

            _root = new GameObject("Root").transform;
            _root.SetParent(transform, false);
            _root.position = world;

            var camGo = new GameObject("Cam", typeof(Camera));
            camGo.transform.SetParent(_root, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.orthographic = false;
            _cam.fieldOfView = 28f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 12f;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -40;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            _cam.enabled = true;

            _rt = new RenderTexture(rtSize, rtSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "HubPropRT_" + stem,
                antiAliasing = 2
            };
            _cam.targetTexture = _rt;
            if (target != null)
            {
                target.texture = _rt;
                target.color = Color.white;
            }

            var body = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(_root, false);
            _body = body.transform;
            var mf = body.GetComponent<MeshFilter>();
            var mr = body.GetComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = MakeMaterial(accent, LoadAlbedo(stem));

            // Center + fit so framing matches the disk showcase.
            var b = mesh.bounds;
            var ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (ext < 0.0001f) ext = 1f;
            var scale = 0.55f / ext;
            _body.localPosition = -b.center * scale;
            _body.localScale = Vector3.one * scale;
            _body.localRotation = Quaternion.Euler(12f, 35f, 0f);

            SetLayerRecurse(_root.gameObject, Layer);

            _cam.transform.localPosition = new Vector3(0f, 0.12f, -1.35f);
            _cam.transform.LookAt(_root.position);
            _ready = true;
        }

        static Material MakeMaterial(Color accent, Texture2D albedo)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Texture")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("UI/Default");
            var mat = new Material(shader) { name = "HubPropUnlit" };
            var tint = Color.Lerp(Color.white, accent, 0.18f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accent * 0.28f);
            }

            return mat;
        }

        void Update()
        {
            if (!_ready || _body == null) return;
            _body.Rotate(Vector3.up, _spin * Time.unscaledDeltaTime, Space.World);
        }

        void OnDestroy()
        {
            Live.Remove(this);
            if (_ready) _live = Mathf.Max(0, _live - 1);
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }

        static void SetLayerRecurse(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayerRecurse(t.gameObject, layer);
        }
    }
}
