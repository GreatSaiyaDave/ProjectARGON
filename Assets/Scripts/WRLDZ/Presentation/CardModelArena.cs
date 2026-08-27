using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Separate hologram arena field where 3D models / card-art spirits appear
    /// for monsters seated on the duel disks. Disks hold the cards; this field
    /// holds the life-size (or stage-scale) manifestations — anime dual-disk layout.
    /// </summary>
    public class CardModelArena : MonoBehaviour
    {
        const int Layer = 31;

        RenderTexture _rt;
        Camera _cam;
        Transform _stage;
        Transform _playerRow;
        Transform _oppRow;
        Transform _floor;
        RawImage _target;
        readonly Dictionary<int, GameObject> _playerSlots = new();
        readonly Dictionary<int, GameObject> _oppSlots = new();
        Material _floorMat;
        float _attackPulse;

        public static CardModelArena CreateInUi(Transform uiParent, float x0, float y0, float x1, float y1,
            Transform lifetimeParent = null)
        {
            var go = new GameObject("CardModelArenaSlot", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(uiParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;

            var host = new GameObject("CardModelArenaHost");
            if (lifetimeParent != null)
                host.transform.SetParent(lifetimeParent, false);
            var arena = host.AddComponent<CardModelArena>();
            arena._target = raw;
            arena.BuildStage();
            return arena;
        }

        void BuildStage()
        {
            _stage = new GameObject("ArenaStage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("ArenaCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.01f, 0.06f, 0.92f);
            _cam.fieldOfView = 40f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 40f;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -60;
            _cam.transform.localPosition = new Vector3(0f, 1.35f, -3.2f);
            _cam.transform.LookAt(_stage.position + new Vector3(0f, 0.55f, 0.2f));

            _rt = new RenderTexture(768, 432, 16, RenderTextureFormat.ARGB32)
            {
                name = "CardModelArenaRT",
                antiAliasing = 2
            };
            _cam.targetTexture = _rt;
            if (_target != null) _target.texture = _rt;

            // Holo floor
            _floor = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            _floor.name = "HoloFloor";
            _floor.SetParent(_stage, false);
            _floor.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _floor.localScale = new Vector3(4.5f, 3.2f, 1f);
            UnityEngine.Object.Destroy(_floor.GetComponent<Collider>());
            SetLayer(_floor.gameObject);
            _floorMat = MakeUnlit(new Color(0.15f, 0.55f, 0.85f, 0.35f));
            _floor.GetComponent<MeshRenderer>().sharedMaterial = _floorMat;

            // Center line
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            line.name = "MidLine";
            line.SetParent(_stage, false);
            line.localPosition = new Vector3(0f, 0.01f, 0f);
            line.localScale = new Vector3(3.8f, 0.01f, 0.03f);
            UnityEngine.Object.Destroy(line.GetComponent<Collider>());
            SetLayer(line.gameObject);
            line.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlit(new Color(1f, 0.85f, 0.3f, 0.6f));

            _oppRow = new GameObject("OppModels").transform;
            _oppRow.SetParent(_stage, false);
            _oppRow.localPosition = new Vector3(0f, 0f, 0.85f);

            _playerRow = new GameObject("PlayerModels").transform;
            _playerRow.SetParent(_stage, false);
            _playerRow.localPosition = new Vector3(0f, 0f, -0.85f);

            // Soft key light
            var lightGo = new GameObject("KeyLight", typeof(Light));
            lightGo.transform.SetParent(_stage, false);
            lightGo.transform.localPosition = new Vector3(0.5f, 2.5f, -1f);
            lightGo.transform.LookAt(_stage);
            var L = lightGo.GetComponent<Light>();
            L.type = LightType.Directional;
            L.color = new Color(0.7f, 0.85f, 1f);
            L.intensity = 1.1f;
            L.cullingMask = 1 << Layer;

            Debug.Log("[WRLDZ] CardModelArena ready — hologram field separate from duel disks.");
        }

        void Update()
        {
            // Gentle arena breath
            if (_floor != null)
            {
                var s = 4.5f + Mathf.Sin(Time.time * 1.2f) * 0.05f;
                _floor.localScale = new Vector3(s, 3.2f, 1f);
            }

            if (_attackPulse > 0f)
            {
                _attackPulse = Mathf.Max(0f, _attackPulse - Time.deltaTime);
                if (_floorMat != null && _floorMat.HasProperty("_BaseColor"))
                    _floorMat.SetColor("_BaseColor",
                        Color.Lerp(new Color(0.15f, 0.55f, 0.85f, 0.35f),
                            new Color(1f, 0.3f, 0.2f, 0.55f), _attackPulse));
            }

            // Billboard fallbacks face camera
            BillboardAll(_playerRow);
            BillboardAll(_oppRow);
        }

        void BillboardAll(Transform row)
        {
            if (row == null || _cam == null) return;
            foreach (Transform t in row)
            {
                if (t.childCount == 0) continue;
                var body = t.GetChild(0);
                if (body.name.Contains("Billboard") || body.GetComponent<MeshFilter>()?.sharedMesh?.name == "CardBillboard")
                {
                    var look = _cam.transform.position;
                    look.y = body.position.y;
                    body.LookAt(look);
                    body.Rotate(0f, 180f, 0f);
                }
            }
        }

        /// <summary>Sync arena models from engine field state.</summary>
        public void SyncFromEngine(DuelEngine engine, CardDatabase db)
        {
            if (engine?.Player == null || engine.Opponent == null || db == null) return;
            SyncSide(engine.Player, db, _playerRow, _playerSlots, playerSide: true);
            SyncSide(engine.Opponent, db, _oppRow, _oppSlots, playerSide: false);
        }

        void SyncSide(DuelistState who, CardDatabase db, Transform row, Dictionary<int, GameObject> map,
            bool playerSide)
        {
            var live = new HashSet<int>();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var m = who.MonsterZones[i].Occupant;
                if (m == null) continue;
                // Face-down set monsters: still show silhouette, not full art spirit
                live.Add(i);
                if (!map.TryGetValue(i, out var go) || go == null)
                {
                    go = SpawnModel(m, db, row, i, playerSide);
                    map[i] = go;
                }
                else
                {
                    // Update facing / pose
                    ApplyPose(go, m, playerSide);
                    // If card identity changed in slot, rebuild
                    var tag = go.GetComponent<CardSlotTag>();
                    if (tag != null && tag.InstanceId != m.InstanceId)
                    {
                        UnityEngine.Object.Destroy(go);
                        go = SpawnModel(m, db, row, i, playerSide);
                        map[i] = go;
                    }
                }
            }

            // Remove empty slots
            var dead = new List<int>();
            foreach (var kv in map)
                if (!live.Contains(kv.Key))
                    dead.Add(kv.Key);
            foreach (var k in dead)
            {
                if (map[k] != null) UnityEngine.Object.Destroy(map[k]);
                map.Remove(k);
            }
        }

        GameObject SpawnModel(CardInstance card, CardDatabase db, Transform row, int zoneIndex, bool playerSide)
        {
            var root = new GameObject($"Zone{zoneIndex}_{card.Name}");
            root.transform.SetParent(row, false);
            var x = (zoneIndex - 2) * 0.7f;
            root.transform.localPosition = new Vector3(x, 0f, 0f);
            SetLayer(root);

            var tag = root.AddComponent<CardSlotTag>();
            tag.InstanceId = card.InstanceId;
            tag.CardId = card.CardId;

            var mesh = CardModelCatalog.GetMesh(card.CardId);
            var body = new GameObject(CardModelCatalog.IsBillboardFallback(mesh) ? "Billboard" : "Model",
                typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(root.transform, false);
            body.GetComponent<MeshFilter>().sharedMesh = mesh;
            var art = db.GetArt(card.CardId);
            var tint = playerSide ? new Color(0.3f, 0.9f, 1f) : new Color(1f, 0.35f, 0.55f);
            if (!card.FaceUp)
                tint = new Color(0.15f, 0.15f, 0.25f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                CardModelCatalog.MakeArtMaterial(card.FaceUp ? art : null, tint);
            SetLayer(body);

            // Scale DEF monsters shorter; ATK upright
            ApplyPose(root, card, playerSide);

            // Ground ring
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            ring.transform.localScale = new Vector3(0.55f, 0.01f, 0.55f);
            UnityEngine.Object.Destroy(ring.GetComponent<Collider>());
            ring.GetComponent<MeshRenderer>().sharedMaterial = MakeUnlit(
                playerSide ? new Color(0.2f, 0.8f, 1f, 0.45f) : new Color(1f, 0.3f, 0.5f, 0.45f));
            SetLayer(ring);

            return root;
        }

        static void ApplyPose(GameObject root, CardInstance card, bool playerSide)
        {
            if (root == null || card == null) return;
            var body = root.transform.childCount > 0 ? root.transform.GetChild(0) : null;
            if (body == null) return;

            if (!card.FaceUp)
            {
                body.localRotation = Quaternion.Euler(0f, playerSide ? 0f : 180f, 0f);
                body.localScale = new Vector3(0.7f, 0.55f, 0.7f);
                return;
            }

            if (card.Position == BattlePosition.Defense)
            {
                body.localRotation = Quaternion.Euler(0f, playerSide ? 90f : -90f, 0f);
                body.localScale = Vector3.one * 0.85f;
            }
            else
            {
                body.localRotation = Quaternion.Euler(0f, playerSide ? 0f : 180f, 0f);
                body.localScale = Vector3.one;
            }
        }

        public void PulseAttack() => _attackPulse = 1f;

        public void PlaySummonBurst(bool playerSide, int zoneIndex)
        {
            var map = playerSide ? _playerSlots : _oppSlots;
            if (!map.TryGetValue(zoneIndex, out var go) || go == null) return;
            // Quick scale pop
            go.transform.localScale = Vector3.one * 1.25f;
        }

        void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.Destroy(_rt);
            }
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject);
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

        class CardSlotTag : MonoBehaviour
        {
            public int InstanceId;
            public int CardId;
        }
    }
}
