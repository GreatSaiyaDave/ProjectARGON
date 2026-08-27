using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Battle City Spirit Dueler disk as the **card field**.
    /// Monster + Spell/Trap zone quads sit on the 3D disk; taps raise card actions via callback.
    /// Separate from <see cref="CardModelArena"/> (hologram spirits).
    /// </summary>
    public class DuelDiskField : MonoBehaviour
    {
        public const int Layer = 30;
        public const int MonsterZones = 5;
        public const int SpellTrapZones = 5;

        public bool IsPlayerSide { get; private set; }
        public event Action<CardInstance, bool /*isMonster*/> OnCardClicked;
#pragma warning disable CS0067 // Reserved for empty-zone tap wiring (legacy disk field)
        public event Action<int /*zone*/, bool /*isMonster*/> OnEmptyZoneClicked;
#pragma warning restore CS0067

        RenderTexture _rt;
        Camera _cam;
        Transform _stage;
        Transform _diskRoot;
        MeshRenderer _diskMr;
        Material _diskMat;
        RawImage _target;
        DiskFxDriver _fx = new();
        Color _accent;

        readonly Transform[] _monSlots = new Transform[MonsterZones];
        readonly Transform[] _stSlots = new Transform[SpellTrapZones];
        readonly Dictionary<int, GameObject> _monCards = new();
        readonly Dictionary<int, GameObject> _stCards = new();

        // Local disk-surface layout (X along blade, Z depth on disk face)
        // Monsters near center rim; S/T farther out
        static readonly float[] ZoneX = { -0.36f, -0.18f, 0f, 0.18f, 0.36f };

        public static DuelDiskField CreateInUi(Transform uiParent, bool playerSide,
            float x0, float y0, float x1, float y1, Color accent, Transform lifetimeParent = null)
        {
            var go = new GameObject(playerSide ? "PlayerDiskFieldSlot" : "OppDiskFieldSlot",
                typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(uiParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = true; // click-through to zones via custom ray? zones are 3D — use UI hit on raw + project

            var host = new GameObject(playerSide ? "PlayerDuelDiskField" : "OppDuelDiskField");
            if (lifetimeParent != null)
                host.transform.SetParent(lifetimeParent, false);
            var field = host.AddComponent<DuelDiskField>();
            field.IsPlayerSide = playerSide;
            field._target = raw;
            field._accent = accent;
            field._fx.Accent = accent;
            field.BuildStage();
            // UI click bridge
            var bridge = go.AddComponent<DiskFieldClickBridge>();
            bridge.Field = field;
            return field;
        }

        void BuildStage()
        {
            var mesh = LoadDiskMesh();
            if (mesh == null)
            {
                Debug.LogWarning("[WRLDZ] DuelDiskField: disk mesh missing.");
                return;
            }

            _stage = new GameObject("DiskFieldStage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("DiskFieldCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = IsPlayerSide
                ? new Color(0.02f, 0.05f, 0.12f, 0.95f)
                : new Color(0.12f, 0.02f, 0.06f, 0.95f);
            _cam.fieldOfView = 36f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 25f;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = -55;

            var size = 768;
            _rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "DuelDiskFieldRT_" + (IsPlayerSide ? "You" : "Opp"),
                antiAliasing = 2
            };
            _cam.targetTexture = _rt;
            if (_target != null) _target.texture = _rt;

            _diskRoot = new GameObject("DiskRoot").transform;
            _diskRoot.SetParent(_stage, false);
            SetLayer(_diskRoot.gameObject);

            var disk = new GameObject("BattleCityDisk", typeof(MeshFilter), typeof(MeshRenderer));
            disk.transform.SetParent(_diskRoot, false);
            disk.GetComponent<MeshFilter>().sharedMesh = mesh;
            _diskMat = MakeDiskMaterial(_accent);
            _diskMr = disk.GetComponent<MeshRenderer>();
            _diskMr.sharedMaterial = _diskMat;
            // Orient flat-ish for zone placement (model is thin on Y)
            disk.transform.localRotation = Quaternion.Euler(-75f, 0f, 0f);
            disk.transform.localScale = Vector3.one * 1.35f;
            SetLayer(disk);

            BuildZoneAnchors();
            ApplyCameraPose();

            // Light
            var lightGo = new GameObject("DiskLight", typeof(Light));
            lightGo.transform.SetParent(_stage, false);
            lightGo.transform.localPosition = new Vector3(0.4f, 1.2f, -0.8f);
            var L = lightGo.GetComponent<Light>();
            L.type = LightType.Point;
            L.range = 8f;
            L.intensity = 1.6f;
            L.color = Color.Lerp(Color.white, _accent, 0.4f);
            L.cullingMask = 1 << Layer;

            Debug.Log($"[WRLDZ] DuelDiskField ready ({(IsPlayerSide ? "player" : "opp")}) — disk is card field.");
        }

        void BuildZoneAnchors()
        {
            // Card plane slightly above disk face (local space after disk tilt handled by parent offset)
            var zones = new GameObject("Zones").transform;
            zones.SetParent(_diskRoot, false);
            zones.localPosition = new Vector3(0f, 0.08f, 0f);
            zones.localRotation = Quaternion.Euler(15f, 0f, 0f);
            SetLayer(zones.gameObject);

            for (var i = 0; i < MonsterZones; i++)
            {
                var t = CreateSlot(zones, "M" + i, ZoneX[i], 0.02f, IsPlayerSide ? -0.06f : 0.06f);
                _monSlots[i] = t;
            }

            for (var i = 0; i < SpellTrapZones; i++)
            {
                var t = CreateSlot(zones, "ST" + i, ZoneX[i], 0.02f, IsPlayerSide ? 0.14f : -0.14f);
                _stSlots[i] = t;
            }
        }

        Transform CreateSlot(Transform parent, string name, float x, float y, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, z);
            go.transform.localScale = Vector3.one;
            SetLayer(go);

            // Invisible snap/anchor only — the sculpted disk is the field.
            // Cards spawn here when played; never paint an empty pad.
            return go.transform;
        }

        void ApplyCameraPose()
        {
            if (IsPlayerSide)
            {
                // Looking down onto your disk (left-arm metaphor, lower screen)
                _cam.transform.localPosition = new Vector3(0.05f, 1.15f, -1.35f);
                _cam.transform.LookAt(_diskRoot.position + new Vector3(0f, 0.05f, 0.05f));
                _fx.SpinSpeed = 4f;
            }
            else
            {
                _cam.transform.localPosition = new Vector3(-0.05f, 1.1f, -1.3f);
                _cam.transform.LookAt(_diskRoot.position + new Vector3(0f, 0.05f, -0.05f));
                _fx.SpinSpeed = -3.5f;
            }
        }

        void Update()
        {
            if (_diskRoot == null) return;
            _fx.Tick(Time.deltaTime, _diskRoot, _diskMat);
        }

        public void PlayFx(DiskFxEvent evt, float duration = -1f) => _fx.Play(evt, duration);

        public void SetAttackCharge(float t01)
        {
            _fx.Play(DiskFxEvent.AttackCharge);
            _fx.SetCharge01(t01);
        }

        /// <summary>Sync zone cards from duelist state.</summary>
        public void Sync(DuelistState who, CardDatabase db)
        {
            if (who == null || db == null) return;
            SyncRow(who.MonsterZones, _monSlots, _monCards, db, isMonster: true, who);
            SyncRow(who.SpellTrapZones, _stSlots, _stCards, db, isMonster: false, who);
        }

        void SyncRow(FieldZone[] zones, Transform[] slots, Dictionary<int, GameObject> map,
            CardDatabase db, bool isMonster, DuelistState who)
        {
            var live = new HashSet<int>();
            for (var i = 0; i < zones.Length && i < slots.Length; i++)
            {
                var card = zones[i].Occupant;
                if (card == null) continue;
                live.Add(i);
                if (!map.TryGetValue(i, out var go) || go == null)
                {
                    go = SpawnCardOnZone(card, db, slots[i], isMonster);
                    map[i] = go;
                }
                else
                {
                    var tag = go.GetComponent<ZoneCardTag>();
                    if (tag == null || tag.InstanceId != card.InstanceId)
                    {
                        UnityEngine.Object.Destroy(go);
                        go = SpawnCardOnZone(card, db, slots[i], isMonster);
                        map[i] = go;
                    }
                    else
                        UpdateCardVisual(go, card, db, isMonster);
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

        GameObject SpawnCardOnZone(CardInstance card, CardDatabase db, Transform slot, bool isMonster)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Card_" + card.Name;
            go.transform.SetParent(slot, false);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(0.12f, 0.17f, 1f);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            // Add box collider for future 3D picking
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.05f);

            var tag = go.AddComponent<ZoneCardTag>();
            tag.InstanceId = card.InstanceId;
            tag.Card = card;
            tag.IsMonster = isMonster;

            UpdateCardVisual(go, card, db, isMonster);
            SetLayer(go);
            return go;
        }

        void UpdateCardVisual(GameObject go, CardInstance card, CardDatabase db, bool isMonster)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var showFace = card.FaceUp || IsPlayerSide;
            Sprite art = null;
            if (showFace) art = db.GetArt(card.CardId);
            var tint = showFace
                ? (isMonster ? Color.white : new Color(0.85f, 0.95f, 1f))
                : new Color(0.12f, 0.14f, 0.28f);
            mr.sharedMaterial = CardModelCatalog.MakeArtMaterial(showFace ? art : null, tint, emissive: true);

            // DEF / set orientation on disk
            if (isMonster && card.Position == BattlePosition.Defense && card.FaceUp)
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 90f);
            else
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            go.transform.localScale = new Vector3(0.12f, 0.17f, 1f);
        }

        /// <summary>Map a UI click on the RawImage (normalized 0-1) to a zone card.</summary>
        public bool TryPick(Vector2 normalizedUv, out CardInstance card, out bool isMonster, out int zoneIndex)
        {
            card = null;
            isMonster = false;
            zoneIndex = -1;
            if (_cam == null || _rt == null) return false;

            // Approximate: map UV to viewport ray into disk field camera
            var ray = _cam.ViewportPointToRay(new Vector3(normalizedUv.x, normalizedUv.y, 0f));
            if (!Physics.Raycast(ray, out var hit, 20f, 1 << Layer))
                return false;

            var tag = hit.collider.GetComponentInParent<ZoneCardTag>();
            if (tag == null || tag.Card == null) return false;
            card = tag.Card;
            isMonster = tag.IsMonster;
            // find zone index
            zoneIndex = FindZoneIndex(tag);
            return true;
        }

        int FindZoneIndex(ZoneCardTag tag)
        {
            foreach (var kv in _monCards)
                if (kv.Value != null && kv.Value.GetComponent<ZoneCardTag>() == tag) return kv.Key;
            foreach (var kv in _stCards)
                if (kv.Value != null && kv.Value.GetComponent<ZoneCardTag>() == tag) return kv.Key;
            return -1;
        }

        public void NotifyCardClicked(CardInstance c, bool isMonster)
        {
            WrldzAudio.PlayCardTap();
            OnCardClicked?.Invoke(c, isMonster);
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

        static Mesh LoadDiskMesh()
        {
            var stream = System.IO.Path.Combine(Application.streamingAssetsPath,
                SpiritDuelerDiskView.StreamingRelativePath);
            if (System.IO.File.Exists(stream))
                return ObjMeshLoader.LoadFromFile(stream, "BattleCityDuelDisk");
            var proj = System.IO.Path.Combine(Application.dataPath,
                "Models/WRLDZ/SpiritDueler/BattleCityDuelDisk.obj");
            if (System.IO.File.Exists(proj))
                return ObjMeshLoader.LoadFromFile(proj, "BattleCityDuelDisk");
            return null;
        }

        static Material MakeDiskMaterial(Color accent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "DiskFieldMat" };
            var baseCol = new Color(0.1f, 0.18f, 0.28f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseCol);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.7f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.75f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", accent * 0.4f);
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

        class ZoneCardTag : MonoBehaviour
        {
            public int InstanceId;
            public CardInstance Card;
            public bool IsMonster;
        }

        /// <summary>Forwards RawImage clicks into 3D disk picking.</summary>
        class DiskFieldClickBridge : MonoBehaviour, IPointerClickHandler
        {
            public DuelDiskField Field;
            RectTransform _rt;

            void Awake() => _rt = transform as RectTransform;

            public void OnPointerClick(PointerEventData e)
            {
                if (Field == null || _rt == null) return;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position,
                        e.pressEventCamera, out var local))
                    return;
                var r = _rt.rect;
                var u = (local.x - r.x) / r.width;
                var v = (local.y - r.y) / r.height;
                if (Field.TryPick(new Vector2(u, v), out var card, out var isMon, out _))
                    Field.NotifyCardClicked(card, isMon);
            }
        }
    }
}
