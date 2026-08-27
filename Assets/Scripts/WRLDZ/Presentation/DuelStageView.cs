using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// One composed duel stage: Battle City disks as atmosphere + spirit holograms
    /// between them. Renders to a single RawImage (no stacked 3D panels).
    /// Card play stays on the readable 2D field overlay in DuelUI.
    /// </summary>
    public class DuelStageView : MonoBehaviour
    {
        const int Layer = 29;

        RenderTexture _rt;
        Camera _cam;
        Transform _stage;
        Transform _playerDisk;
        Transform _oppDisk;
        Transform _spiritRoot;
        Transform _playerSpirits;
        Transform _oppSpirits;
        MeshRenderer _playerDiskMr;
        MeshRenderer _oppDiskMr;
        Material _playerDiskMat;
        Material _oppDiskMat;
        RawImage _target;
        DiskFxDriver _playerFx = new();
        DiskFxDriver _oppFx = new();
        readonly Dictionary<int, GameObject> _pSlots = new();
        readonly Dictionary<int, GameObject> _oSlots = new();
        float _attackFlash;
        bool _ready;

        static Mesh _diskMesh;

        public static DuelStageView CreateInUi(Transform uiParent, float x0, float y0, float x1, float y1,
            Transform lifetimeParent = null)
        {
            var go = new GameObject("DuelStageSlot", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(uiParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;

            // Soft frame so the RT doesn't look like a floating box
            var frame = new GameObject("StageFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(uiParent, false);
            frame.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(x0 - 0.01f, y0 - 0.008f);
            frt.anchorMax = new Vector2(x1 + 0.01f, y1 + 0.008f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            var fimg = frame.GetComponent<Image>();
            fimg.sprite = UI.UiFoundation.WhiteSprite();
            fimg.color = new Color(0.04f, 0.08f, 0.16f, 0.92f);
            fimg.raycastTarget = false;

            // Gold hairline border
            var border = new GameObject("StageBorder", typeof(RectTransform), typeof(Image));
            border.transform.SetParent(frame.transform, false);
            Stretch(border.GetComponent<RectTransform>());
            var bimg = border.GetComponent<Image>();
            bimg.sprite = UI.UiFoundation.WhiteSprite();
            bimg.color = new Color(0.85f, 0.7f, 0.25f, 0.35f);
            bimg.raycastTarget = false;
            // inset content area
            var inset = new GameObject("Inset", typeof(RectTransform), typeof(Image));
            inset.transform.SetParent(border.transform, false);
            var irt = inset.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.012f, 0.02f);
            irt.anchorMax = new Vector2(0.988f, 0.98f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var iimg = inset.GetComponent<Image>();
            iimg.sprite = UI.UiFoundation.WhiteSprite();
            iimg.color = new Color(0.02f, 0.03f, 0.07f, 1f);
            iimg.raycastTarget = false;

            // Move RawImage into inset so it sits inside the frame
            go.transform.SetParent(inset.transform, false);
            Stretch(go.GetComponent<RectTransform>());

            var host = new GameObject("DuelStageHost");
            if (lifetimeParent != null)
                host.transform.SetParent(lifetimeParent, false);
            var view = host.AddComponent<DuelStageView>();
            view._target = raw;
            view.BuildStage();
            return view;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        void BuildStage()
        {
            EnsureDiskMesh();
            _stage = new GameObject("Stage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("StageCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // Deep void — reads as hologram stage, not a grey slab
            _cam.backgroundColor = new Color(0.015f, 0.02f, 0.05f, 1f);
            _cam.fieldOfView = 34f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 40f;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -70;
            _cam.allowHDR = false;
            _cam.allowMSAA = true;
            // Three-quarter arena look
            _cam.transform.localPosition = new Vector3(0f, 2.05f, -4.1f);
            _cam.transform.LookAt(_stage.position + new Vector3(0f, 0.35f, 0.15f));

            _rt = new RenderTexture(960, 720, 16, RenderTextureFormat.ARGB32)
            {
                name = "DuelStageRT",
                antiAliasing = 2
            };
            _cam.targetTexture = _rt;
            if (_target != null) _target.texture = _rt;

            BuildFloor();
            _playerDisk = BuildDisk(playerSide: true);
            _oppDisk = BuildDisk(playerSide: false);

            _spiritRoot = new GameObject("Spirits").transform;
            _spiritRoot.SetParent(_stage, false);
            _playerSpirits = new GameObject("PlayerSpirits").transform;
            _playerSpirits.SetParent(_spiritRoot, false);
            _playerSpirits.localPosition = new Vector3(0f, 0f, -0.55f);
            _oppSpirits = new GameObject("OppSpirits").transform;
            _oppSpirits.SetParent(_spiritRoot, false);
            _oppSpirits.localPosition = new Vector3(0f, 0f, 0.7f);

            // Key + rim lights for readable metal disk + holograms
            AddLight("Key", new Vector3(1.2f, 3.2f, -1.5f), new Color(0.75f, 0.88f, 1f), 1.35f);
            AddLight("Fill", new Vector3(-1.5f, 1.8f, 0.5f), new Color(0.45f, 0.55f, 0.9f), 0.55f);
            AddLight("Rim", new Vector3(0f, 1.2f, 2.2f), new Color(1f, 0.55f, 0.35f), 0.7f);

            _playerFx.Accent = new Color(0.25f, 0.9f, 1f);
            _playerFx.SpinSpeed = 10f;
            _playerFx.HoverAmp = 0.012f;
            _oppFx.Accent = new Color(1f, 0.35f, 0.55f);
            _oppFx.SpinSpeed = -8f;
            _oppFx.HoverAmp = 0.01f;

            _ready = true;
            Debug.Log("[WRLDZ] DuelStageView ready — unified disk + spirit field.");
        }

        void BuildFloor()
        {
            // Soft arena plate
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "ArenaFloor";
            floor.transform.SetParent(_stage, false);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0.1f);
            floor.transform.localScale = new Vector3(5.2f, 4.0f, 1f);
            UnityEngine.Object.Destroy(floor.GetComponent<Collider>());
            SetLayer(floor);
            floor.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlit(new Color(0.08f, 0.18f, 0.32f, 0.55f));

            // Center duel line
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "DuelLine";
            line.transform.SetParent(_stage, false);
            line.transform.localPosition = new Vector3(0f, 0.005f, 0.08f);
            line.transform.localScale = new Vector3(3.6f, 0.008f, 0.025f);
            UnityEngine.Object.Destroy(line.GetComponent<Collider>());
            SetLayer(line);
            line.GetComponent<MeshRenderer>().sharedMaterial =
                MakeUnlit(new Color(1f, 0.82f, 0.28f, 0.55f));

            // Side glow rails
            for (var s = -1; s <= 1; s += 2)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = s < 0 ? "RailL" : "RailR";
                rail.transform.SetParent(_stage, false);
                rail.transform.localPosition = new Vector3(s * 1.85f, 0.01f, 0.1f);
                rail.transform.localScale = new Vector3(0.03f, 0.01f, 2.8f);
                UnityEngine.Object.Destroy(rail.GetComponent<Collider>());
                SetLayer(rail);
                rail.GetComponent<MeshRenderer>().sharedMaterial =
                    MakeUnlit(s < 0
                        ? new Color(0.2f, 0.85f, 1f, 0.4f)
                        : new Color(1f, 0.35f, 0.55f, 0.4f));
            }
        }

        Transform BuildDisk(bool playerSide)
        {
            var root = new GameObject(playerSide ? "PlayerDisk" : "OppDisk").transform;
            root.SetParent(_stage, false);
            SetLayer(root.gameObject);

            if (playerSide)
            {
                // Lower field — left-arm wearable angle, large enough to read silhouette
                root.localPosition = new Vector3(-0.15f, 0.12f, -1.45f);
                root.localRotation = Quaternion.Euler(55f, -18f, -8f);
                root.localScale = Vector3.one * 1.55f;
            }
            else
            {
                root.localPosition = new Vector3(0.15f, 0.18f, 1.55f);
                root.localRotation = Quaternion.Euler(50f, 162f, 6f);
                root.localScale = Vector3.one * 1.35f;
            }

            if (_diskMesh != null)
            {
                var disk = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                disk.transform.SetParent(root, false);
                // Blade is thin on Y — lay it as wearable surface toward camera
                disk.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                disk.GetComponent<MeshFilter>().sharedMesh = _diskMesh;
                var accent = playerSide
                    ? new Color(0.25f, 0.9f, 1f)
                    : new Color(1f, 0.35f, 0.55f);
                var mat = MakeDiskMaterial(accent);
                disk.GetComponent<MeshRenderer>().sharedMaterial = mat;
                SetLayer(disk);
                if (playerSide)
                {
                    _playerDiskMr = disk.GetComponent<MeshRenderer>();
                    _playerDiskMat = mat;
                }
                else
                {
                    _oppDiskMr = disk.GetComponent<MeshRenderer>();
                    _oppDiskMat = mat;
                }
            }

            // Soft under-glow so disk doesn't float in pure black
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "UnderGlow";
            glow.transform.SetParent(root, false);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            glow.transform.localScale = new Vector3(1.1f, 0.7f, 1f);
            UnityEngine.Object.Destroy(glow.GetComponent<Collider>());
            SetLayer(glow);
            var gCol = playerSide
                ? new Color(0.15f, 0.7f, 1f, 0.28f)
                : new Color(1f, 0.25f, 0.45f, 0.28f);
            glow.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlit(gCol);

            return root;
        }

        void AddLight(string name, Vector3 pos, Color color, float intensity)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(_stage, false);
            go.transform.localPosition = pos;
            go.transform.LookAt(_stage);
            var L = go.GetComponent<Light>();
            L.type = LightType.Point;
            L.range = 12f;
            L.color = color;
            L.intensity = intensity;
            L.cullingMask = 1 << Layer;
        }

        void Update()
        {
            if (!_ready) return;
            _playerFx.Tick(Time.deltaTime, _playerDisk, _playerDiskMat);
            _oppFx.Tick(Time.deltaTime, _oppDisk, _oppDiskMat);

            if (_attackFlash > 0f)
            {
                _attackFlash = Mathf.Max(0f, _attackFlash - Time.deltaTime);
                if (_cam != null)
                {
                    var t = _attackFlash;
                    _cam.backgroundColor = Color.Lerp(
                        new Color(0.015f, 0.02f, 0.05f, 1f),
                        new Color(0.18f, 0.04f, 0.04f, 1f), t);
                }
            }

            BillboardRow(_playerSpirits);
            BillboardRow(_oppSpirits);
        }

        void BillboardRow(Transform row)
        {
            if (row == null || _cam == null) return;
            foreach (Transform t in row)
            {
                if (t.childCount == 0) continue;
                var body = t.GetChild(0);
                if (body == null || !body.name.Contains("Billboard")) continue;
                var look = _cam.transform.position;
                look.y = body.position.y;
                body.LookAt(look);
                body.Rotate(0f, 180f, 0f);
            }
        }

        public void SyncFromEngine(DuelEngine engine, CardDatabase db)
        {
            if (engine?.Player == null || engine.Opponent == null || db == null) return;
            SyncSpirits(engine.Player, db, _playerSpirits, _pSlots, true);
            SyncSpirits(engine.Opponent, db, _oppSpirits, _oSlots, false);
        }

        void SyncSpirits(DuelistState who, CardDatabase db, Transform row,
            Dictionary<int, GameObject> map, bool playerSide)
        {
            var live = new HashSet<int>();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var m = who.MonsterZones[i].Occupant;
                if (m == null) continue;
                // Only face-up (or your own face-down silhouette) on spirit field
                live.Add(i);
                if (!map.TryGetValue(i, out var go) || go == null)
                {
                    go = SpawnSpirit(m, db, row, i, playerSide);
                    map[i] = go;
                }
                else
                {
                    var tag = go.GetComponent<SpiritTag>();
                    if (tag == null || tag.InstanceId != m.InstanceId)
                    {
                        UnityEngine.Object.Destroy(go);
                        go = SpawnSpirit(m, db, row, i, playerSide);
                        map[i] = go;
                    }
                    else
                        ApplySpiritPose(go, m, playerSide);
                }
            }

            var dead = new List<int>();
            foreach (var kv in map)
                if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                if (map[k] != null) UnityEngine.Object.Destroy(map[k]);
                map.Remove(k);
            }
        }

        GameObject SpawnSpirit(CardInstance card, CardDatabase db, Transform row, int zone, bool playerSide)
        {
            var root = new GameObject($"S{zone}_{card.Name}");
            root.transform.SetParent(row, false);
            root.transform.localPosition = new Vector3((zone - 2) * 0.55f, 0f, 0f);
            SetLayer(root);

            var tag = root.AddComponent<SpiritTag>();
            tag.InstanceId = card.InstanceId;

            var mesh = CardModelCatalog.GetMesh(card.CardId);
            var isBillboard = CardModelCatalog.IsBillboardFallback(mesh);
            var body = new GameObject(isBillboard ? "Billboard" : "Model",
                typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(root.transform, false);
            body.GetComponent<MeshFilter>().sharedMesh = mesh;

            var art = db.GetArt(card.CardId);
            var tint = playerSide ? new Color(0.45f, 0.95f, 1f) : new Color(1f, 0.5f, 0.65f);
            if (!card.FaceUp) tint = new Color(0.12f, 0.12f, 0.2f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                CardModelCatalog.MakeArtMaterial(card.FaceUp ? art : null, tint);
            // Billboard scale — taller holograms
            if (isBillboard)
                body.transform.localScale = new Vector3(1.15f, 1.35f, 1.15f);
            SetLayer(body);

            // Ground ring
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            ring.transform.localScale = new Vector3(0.42f, 0.008f, 0.42f);
            UnityEngine.Object.Destroy(ring.GetComponent<Collider>());
            ring.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlit(
                playerSide ? new Color(0.2f, 0.85f, 1f, 0.5f) : new Color(1f, 0.3f, 0.5f, 0.5f));
            SetLayer(ring);

            ApplySpiritPose(root, card, playerSide);
            return root;
        }

        static void ApplySpiritPose(GameObject root, CardInstance card, bool playerSide)
        {
            if (root == null || card == null || root.transform.childCount == 0) return;
            var body = root.transform.GetChild(0);
            if (!card.FaceUp)
            {
                body.localScale = new Vector3(0.7f, 0.55f, 0.7f);
                return;
            }

            if (card.Position == BattlePosition.Defense)
            {
                body.localRotation = Quaternion.Euler(0f, playerSide ? 90f : -90f, 0f);
                body.localScale = Vector3.one * 0.9f;
            }
            else
            {
                body.localRotation = Quaternion.Euler(0f, playerSide ? 0f : 180f, 0f);
                body.localScale = Vector3.one;
            }
        }

        public void PlayFx(bool playerSide, DiskFxEvent evt, float duration = -1f)
        {
            if (playerSide) _playerFx.Play(evt, duration);
            else _oppFx.Play(evt, duration);
        }

        public void SetAttackCharge(bool playerSide, float t01)
        {
            var fx = playerSide ? _playerFx : _oppFx;
            fx.Play(DiskFxEvent.AttackCharge);
            fx.SetCharge01(t01);
            if (!playerSide) _attackFlash = Mathf.Max(_attackFlash, 0.35f);
        }

        public void PulseAttack() => _attackFlash = 1f;

        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
        }

        static void EnsureDiskMesh()
        {
            if (_diskMesh != null) return;
            var stream = Path.Combine(Application.streamingAssetsPath,
                SpiritDuelerDiskView.StreamingRelativePath);
            if (File.Exists(stream))
                _diskMesh = ObjMeshLoader.LoadFromFile(stream, "BattleCityDuelDisk");
            if (_diskMesh != null) return;
            var proj = Path.Combine(Application.dataPath,
                "Models/WRLDZ/SpiritDueler/BattleCityDuelDisk.obj");
            if (File.Exists(proj))
                _diskMesh = ObjMeshLoader.LoadFromFile(proj, "BattleCityDuelDisk");
        }

        static Material MakeDiskMaterial(Color accent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "StageDiskMat" };
            // Cool metal body
            var baseCol = new Color(0.12f, 0.16f, 0.22f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.82f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.78f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accent * 0.55f);
            }

            return mat;
        }

        static Material MakeUnlit(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("UI/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            return mat;
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject);
        }

        class SpiritTag : MonoBehaviour
        {
            public int InstanceId;
        }
    }
}
