using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
// CardShatterPresentation / ArCardShatterFx

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Hologram Field (Solid Vision arena) — one of two game boards.
    ///
    /// Street-scale Solid Vision: every disk play projects here. Never on the disk.
    /// You S/T at the feet → You monsters (full row step further) → street → Opp monsters → Opp S/T.
    /// Monsters = person-scale art holos. S/T = large cards hovering at the feet.
    /// Future: 3D models via <see cref="PreferDynamicModels"/>.
    /// </summary>
    public class ArArenaHologramManager : MonoBehaviour
    {
        public IArTrackingSource Tracker;
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;
        public int Layer = 28;
        /// <summary>
        /// Street plane under the wrists. S/T holos hover at the feet; monsters stand on it.
        /// </summary>
        public float ArenaHeight = 0f;

        /// <summary>
        /// Live stand-off from each wrist to that side's S/T row (anime "in front of you").
        /// </summary>
        public float DiskToHoloMinGap => ArPlaymatLayout.LiveStandOff;

        /// <summary>When true, prefer dedicated OBJ meshes over cropped art billboards.</summary>
        public bool PreferDynamicModels;

        /// <summary>
        /// Editor debug only: draw opaque row pads in midfield.
        /// Default false — anime field has no visible board.
        /// </summary>
        public static bool DebugShowFieldGuides;

        public Transform ArenaRoot { get; private set; }
        /// <summary>All monster holograms parent (player + opp).</summary>
        public Transform MonsterPlane { get; private set; }
        /// <summary>All S/T + field holograms parent (player + opp).</summary>
        public Transform SpellTrapPlane { get; private set; }
        /// <summary>Invisible spawn anchors only (no renderers).</summary>
        public Transform SpawnAnchors { get; private set; }

        // Kept for API compatibility with older code paths
        public Transform PlayerStPlane => SpellTrapPlane;
        public Transform OppStPlane => SpellTrapPlane;

        // Anime field: You S/T (near) → You monsters → mid → Opp monsters → Opp S/T
        public float MonsterColumnSpacing = ArPlaymatLayout.MonsterColumnPitch;
        public float MonsterRowDepth => ArPlaymatLayout.LiveMonsterRowFromMid;
        public float SpellTrapColumnSpacing = ArPlaymatLayout.SpellTrapColumnPitch;
        public float SpellTrapRowDepth => ArPlaymatLayout.LiveSpellTrapRowFromMid;

        readonly Dictionary<int, ArArenaCardVisual> _monP = new();
        readonly Dictionary<int, ArArenaCardVisual> _monO = new();
        readonly Dictionary<int, ArArenaCardVisual> _stP = new();
        readonly Dictionary<int, ArArenaCardVisual> _stO = new();
        ArArenaCardVisual _fieldP;
        ArArenaCardVisual _fieldO;
        ArArenaCardVisual _pendL;
        ArArenaCardVisual _pendR;

        Transform _legalGlowRoot;
        readonly Dictionary<string, MeshRenderer> _legalGlows = new();

        Transform _stageRoot;
        Camera _billboardCam;
        ArFloatingLpCallout _youLpBox;
        ArFloatingLpCallout _oppLpBox;
        ArOppFieldGlance _oppGlance;
        ArFieldSpellFloor _fieldFloor;
        int _billboardFrame;
        bool _anchoredOnce;
        Vector3 _lastA, _lastB;
        /// <summary>Editor: fire once after first wrist midpoint so Scene view can re-frame Opp half.</summary>
        public bool JustAnchoredThisFrame { get; private set; }

        // Reused per Sync to avoid HashSet/List GC on every engine tick
        readonly HashSet<int> _liveScratch = new();
        readonly List<int> _deadScratch = new();

        public static ArArenaHologramManager Create(Transform parent, int layer)
        {
            var go = new GameObject("HologramFieldLayer");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var m = go.AddComponent<ArArenaHologramManager>();
            m.Layer = layer;
            m._stageRoot = parent;
            m.Build();
            return m;
        }

        void Build()
        {
            ArenaRoot = new GameObject("ArenaRoot").transform;
            ArenaRoot.SetParent(transform, false);

            MonsterPlane = new GameObject("MonsterMidfield").transform;
            MonsterPlane.SetParent(ArenaRoot, false);

            SpellTrapPlane = new GameObject("SpellTrapMidfield").transform;
            SpellTrapPlane.SetParent(ArenaRoot, false);

            // Seed live row depths so the first anchors are already player-forward
            ArPlaymatLayout.ApplyAnimeField(ArDuelMatchConfig.DefaultStandM * 2f);

            // Anime hologram field: invisible anchors only — no board mesh
            BuildInvisibleSpawnAnchors();
            _youLpBox = ArFloatingLpCallout.Create(ArenaRoot, Layer, playerSide: true);
            _oppLpBox = ArFloatingLpCallout.Create(ArenaRoot, Layer, playerSide: false);
            // Public opponent field readout (face-up art, set backs) — not a second duel board.
            _oppGlance = ArOppFieldGlance.Create(ArenaRoot, Layer);
            _fieldFloor = ArFieldSpellFloor.Create(ArenaRoot, Layer);
            ArArenaStartFlash.Ensure(ArenaRoot, Layer);
#if UNITY_EDITOR
            if (DebugShowFieldGuides)
                BuildEditorFieldGuides();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Optional debug geometry (off by default). Enable
        /// <see cref="DebugShowFieldGuides"/> when laying out zone spacing.
        /// </summary>
        void BuildEditorFieldGuides()
        {
            var guides = new GameObject("EditorFieldGuides_DEBUG").transform;
            guides.SetParent(ArenaRoot, false);
            guides.gameObject.layer = 0;

            void Solid(string name, Vector3 local, Vector3 scale, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(guides, false);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = scale;
                var col = go.GetComponent<Collider>();
                if (col != null) ArObjectUtil.Destroy(col);
                go.layer = 0;
                ArFieldMaterials.Apply(go.GetComponent<MeshRenderer>(),
                    new Color(c.r, c.g, c.b, 1f));
            }

            var monW = ArPlaymatLayout.MonsterColumnPitch * 4.8f;
            var stW = ArPlaymatLayout.SpellTrapColumnPitch * 4.8f;
            var monZ = ArPlaymatLayout.LiveMonsterRowFromMid;
            var stZ = ArPlaymatLayout.LiveSpellTrapRowFromMid;
            Solid("YouMonsterRow", new Vector3(0f, 0.02f, -monZ),
                new Vector3(monW, 0.02f, 0.48f), new Color(0.15f, 0.75f, 0.95f));
            Solid("OppMonsterRow", new Vector3(0f, 0.02f, monZ),
                new Vector3(monW, 0.02f, 0.48f), new Color(1f, 0.25f, 0.4f));
            Solid("YouSpellTrapRow", new Vector3(0f, 0.015f, -stZ),
                new Vector3(stW, 0.018f, 0.4f), new Color(0.25f, 0.85f, 0.45f));
            Solid("OppSpellTrapRow", new Vector3(0f, 0.015f, stZ),
                new Vector3(stW, 0.018f, 0.4f), new Color(1f, 0.5f, 0.15f));
            Debug.Log("[WRLDZ AR] DEBUG field guides on (anime default is empty air).");
        }

        void OnDrawGizmos()
        {
            if (ArenaRoot == null || !DebugShowFieldGuides) return;
            Gizmos.matrix = ArenaRoot.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.3f, 0.5f, 0.5f);
            for (var i = 0; i < 5; i++)
            {
                Gizmos.DrawWireSphere(ArPlaymatLayout.MonsterArenaLocal(i, false), 0.05f);
                Gizmos.DrawWireSphere(ArPlaymatLayout.SpellTrapArenaLocal(i, false), 0.04f);
            }

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.45f);
            for (var i = 0; i < 5; i++)
            {
                Gizmos.DrawWireSphere(ArPlaymatLayout.MonsterArenaLocal(i, true), 0.05f);
                Gizmos.DrawWireSphere(ArPlaymatLayout.SpellTrapArenaLocal(i, true), 0.04f);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif

        /// <summary>
        /// Empty Transform anchors at each official zone. No meshes, materials, or colliders.
        /// Holos parent under MonsterPlane / SpellTrapPlane at the same local positions.
        /// </summary>
        void BuildInvisibleSpawnAnchors()
        {
            SpawnAnchors = new GameObject("InvisibleSpawnAnchors").transform;
            SpawnAnchors.SetParent(ArenaRoot, false);

            for (var i = 0; i < 5; i++)
            {
                Anchor("YouMon_" + i, MonsterLocal(i, true));
                Anchor("OppMon_" + i, MonsterLocal(i, false));
                Anchor("YouST_" + i, SpellTrapLocal(i, true));
                Anchor("OppST_" + i, SpellTrapLocal(i, false));
            }

            Anchor("YouField", ArPlaymatLayout.FieldSpellArenaLocal(true));
            Anchor("OppField", ArPlaymatLayout.FieldSpellArenaLocal(false));
            Anchor("PendL", ArPlaymatLayout.PendulumArenaLocal(0));
            Anchor("PendR", ArPlaymatLayout.PendulumArenaLocal(1));
        }

        /// <summary>Push invisible anchors (and live holos) to the current anime row depths.</summary>
        void RelayoutSpawnAnchors()
        {
            if (SpawnAnchors != null)
            {
                for (var i = 0; i < 5; i++)
                {
                    MoveAnchor("YouMon_" + i, MonsterLocal(i, true));
                    MoveAnchor("OppMon_" + i, MonsterLocal(i, false));
                    MoveAnchor("YouST_" + i, SpellTrapLocal(i, true));
                    MoveAnchor("OppST_" + i, SpellTrapLocal(i, false));
                }

                MoveAnchor("YouField", ArPlaymatLayout.FieldSpellArenaLocal(true));
                MoveAnchor("OppField", ArPlaymatLayout.FieldSpellArenaLocal(false));
                MoveAnchor("PendL", ArPlaymatLayout.PendulumArenaLocal(0));
                MoveAnchor("PendR", ArPlaymatLayout.PendulumArenaLocal(1));
            }

            RelayoutLiveHolos();
        }

        void MoveAnchor(string name, Vector3 localPos)
        {
            if (SpawnAnchors == null) return;
            var t = SpawnAnchors.Find(name);
            if (t != null)
                t.localPosition = localPos;
        }

        void RelayoutLiveHolos()
        {
            foreach (var kv in _monP)
                RelayoutVisual(kv.Value, MonsterLocal(kv.Key, true));
            foreach (var kv in _monO)
                RelayoutVisual(kv.Value, MonsterLocal(kv.Key, false));
            foreach (var kv in _stP)
                RelayoutVisual(kv.Value, SpellTrapLocal(kv.Key, true));
            foreach (var kv in _stO)
                RelayoutVisual(kv.Value, SpellTrapLocal(kv.Key, false));
            RelayoutVisual(_fieldP, ArPlaymatLayout.FieldSpellArenaLocal(true));
            RelayoutVisual(_fieldO, ArPlaymatLayout.FieldSpellArenaLocal(false));
            RelayoutVisual(_pendL, ArPlaymatLayout.PendulumArenaLocal(0));
            RelayoutVisual(_pendR, ArPlaymatLayout.PendulumArenaLocal(1));
        }

        static float DiskBladeCenterX(ArDuelDiskRig disk, Transform arenaRoot)
        {
            if (disk == null || arenaRoot == null) return float.NaN;
            var pivot = disk.BladePivot != null ? disk.BladePivot : disk.DiskRoot;
            if (pivot == null) return float.NaN;
            var a = pivot.TransformPoint(ArPlaymatLayout.DiskMonsterSurface[0]);
            var b = pivot.TransformPoint(ArPlaymatLayout.DiskMonsterSurface[4]);
            return arenaRoot.InverseTransformPoint((a + b) * 0.5f).x;
        }

        /// <summary>
        /// Disk local +X runs M1 → M5. If that matches arena +X, M1 is on the
        /// player's left (sign +1). If the blade is yawed the other way, flip.
        /// </summary>
        static float DiskColumnSign(ArDuelDiskRig disk, Transform arenaRoot)
        {
            if (disk == null || arenaRoot == null) return 1f;
            var pivot = disk.BladePivot != null ? disk.BladePivot : disk.DiskRoot;
            if (pivot == null) return 1f;
            var diskTowardM5 = pivot.TransformDirection(Vector3.right);
            return Vector3.Dot(diskTowardM5, arenaRoot.right) >= 0f ? 1f : -1f;
        }

        static void RelayoutVisual(ArArenaCardVisual vis, Vector3 localPos)
        {
            if (vis == null) return;
            vis.SetTargetLocalPosition(localPos);
            vis.ApplyLiveHoloScale();
        }

        void Anchor(string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(SpawnAnchors, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            // Explicit: no MeshFilter / MeshRenderer / Collider
            t.gameObject.layer = Layer;
        }

        void LateUpdate()
        {
            JustAnchoredThisFrame = false;
            // Anchor midfield between both left-arm disks (only when arms move)
            Vector3 a, b;
            if (Tracker != null && Tracker.TryGetLeftForearm(out var pPose))
                a = pPose.position;
            else if (PlayerDisk != null)
                a = PlayerDisk.transform.position;
            else
                a = transform.position + new Vector3(-0.4f, 0.3f, -0.7f);

            if (Tracker != null && Tracker.TryGetOpponentLeftForearm(out var oPose))
                b = oPose.position;
            else if (OppDisk != null)
                b = OppDisk.transform.position;
            else
                b = transform.position + new Vector3(0.2f, 0.4f, 0.9f);

            var firstAnchor = !_anchoredOnce;
            var moved = firstAnchor ||
                        (a - _lastA).sqrMagnitude > 0.0004f ||
                        (b - _lastB).sqrMagnitude > 0.0004f;
            if (moved)
            {
                _lastA = a;
                _lastB = b;
                _anchoredOnce = true;

                var forward = b - a;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                var span = Vector3.Distance(a, b);
                var layoutChanged = ArPlaymatLayout.ApplyAnimeField(span);

                // Midpoint of the two duelists; +Z faces the opponent.
                // Rows themselves sit stand-off in front of each person — do not
                // squash Z or the field collapses onto the disks.
                var mid = (a + b) * 0.5f;
                // Street under the duelists — S/T hover at the feet, not at wrist height
                mid.y = Mathf.Min(a.y, b.y) - 0.50f + ArenaHeight;
                if (mid.y < -0.15f) mid.y = 0f;
                ArenaRoot.position = mid;
                ArenaRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
                ArenaRoot.localScale = Vector3.one;

                var px = DiskBladeCenterX(PlayerDisk, ArenaRoot);
                var ox = DiskBladeCenterX(OppDisk, ArenaRoot);
                if (float.IsNaN(px)) px = ArPlaymatLayout.PlayerFieldXOffset;
                if (float.IsNaN(ox)) ox = 0f;
                var ps = DiskColumnSign(PlayerDisk, ArenaRoot);
                var os = DiskColumnSign(OppDisk, ArenaRoot);
                var xChanged = ArPlaymatLayout.SetHoloColumns(px, ps, ox, os);

                if (layoutChanged || xChanged)
                {
                    RelayoutSpawnAnchors();
                    RelayoutLiveHolos();
                }

                if (firstAnchor)
                {
                    JustAnchoredThisFrame = true;
                    Debug.Log(
                        $"[WRLDZ AR] Anime field · span={span:0.00} · " +
                        $"ST±{ArPlaymatLayout.LiveSpellTrapRowFromMid:0.00} · " +
                        $"M±{ArPlaymatLayout.LiveMonsterRowFromMid:0.00} · " +
                        $"step={ArPlaymatLayout.LiveStep:0.00} · " +
                        $"standOff={ArPlaymatLayout.LiveStandOff:0.00} · " +
                        $"holoScale={ArPlaymatLayout.LiveHoloScale:0.00}");
                }
            }

            // Billboard every 2 frames (enough for readable holos, half the cost)
            _billboardFrame++;
            if ((_billboardFrame & 1) != 0) return;
            if (_billboardCam == null)
                _billboardCam = FindStageCamera();
            if (_billboardCam != null)
                FaceCamera(_billboardCam);
        }

        Camera FindStageCamera()
        {
            if (_stageRoot != null)
            {
                var cam = _stageRoot.GetComponentInChildren<Camera>(true);
                if (cam != null) return cam;
            }

            return Camera.main;
        }

        void FaceCamera(Camera cam)
        {
            FaceMap(_monP, cam);
            FaceMap(_monO, cam);
            FaceMap(_stP, cam);
            FaceMap(_stO, cam);
            _fieldP?.FaceCamera(cam);
            _fieldO?.FaceCamera(cam);
            _pendL?.FaceCamera(cam);
            _pendR?.FaceCamera(cam);
        }

        static void FaceMap(Dictionary<int, ArArenaCardVisual> map, Camera cam)
        {
            foreach (var kv in map)
            {
                if (kv.Value != null)
                    kv.Value.FaceCamera(cam);
            }
        }

        public void SyncLifePoints(int you, int opp)
        {
            _youLpBox?.Sync(you);
            _oppLpBox?.Sync(opp);
        }

        public void SyncFromEngine(DuelEngine engine, CardDatabase db)
        {
            if (engine?.Player == null || engine.Opponent == null || db == null) return;
            ArMonsterStatGauge.LivePhase = engine.Phase;
            SyncMonsters(engine.Player, db, _monP, true);
            SyncMonsters(engine.Opponent, db, _monO, false);
            SyncSpellTraps(engine.Player, db, _stP, true);
            SyncSpellTraps(engine.Opponent, db, _stO, false);
            SyncExtra(engine.Player, db, true);
            SyncExtra(engine.Opponent, db, false);
            _fieldFloor?.Sync(
                engine.Player.FieldSpellZone?.Occupant,
                engine.Opponent.FieldSpellZone?.Occupant,
                db);
            // Instant Normal Spells leave the zone same frame — ghost plays rise+fade
            ProcessSpellGhosts(db);

            // Coin tosses / die rolls (Time Wizard, Barrel Dragon, d6 effects): raise a
            // hologram above the field on the acting side and land on the actual result.
            ProcessCoinDice();

            if (_oppGlance != null)
            {
                _oppGlance.PlayerDisk = PlayerDisk;
                _oppGlance.ArenaRoot = ArenaRoot;
                _oppGlance.Sync(engine.Opponent, db);
            }
        }

        /// <summary>Mini-playmat of the opponent's public field (human and AI).</summary>
        public ArOppFieldGlance OppGlance => _oppGlance;

        void SyncMonsters(DuelistState who, CardDatabase db,
            Dictionary<int, ArArenaCardVisual> map, bool playerSide)
        {
            _liveScratch.Clear();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var m = who.MonsterZones[i].Occupant;
                if (m == null) continue;
                _liveScratch.Add(i);
                var localPos = MonsterLocal(i, playerSide);

                if (!map.TryGetValue(i, out var vis) || vis == null)
                {
                    vis = SpawnArenaCard(m, db, MonsterPlane, localPos, playerSide, true,
                        ResolveDiskSpawnWorld(playerSide, ArDuelZoneKind.Monster, i));
                    map[i] = vis;
                }
                else if (vis.InstanceId != m.InstanceId)
                {
                    // Previous occupant left (replaced) — shatter only if rules queued destroy
                    ArCardShatterFx.ConsumeQueuedOrQuietDestroy(vis.gameObject, vis.InstanceId, Layer);
                    vis = SpawnArenaCard(m, db, MonsterPlane, localPos, playerSide, true,
                        ResolveDiskSpawnWorld(playerSide, ArDuelZoneKind.Monster, i));
                    map[i] = vis;
                }
                else
                {
                    var wasFace = vis.FaceUp;
                    vis.SetTargetLocalPosition(localPos);
                    vis.Sync(m, db);
                    if (!wasFace && m.FaceUp && !vis.FlipAnimating)
                        vis.PulseReveal();
                }
            }

            Purge(map, _liveScratch);
        }

        void SyncSpellTraps(DuelistState who, CardDatabase db,
            Dictionary<int, ArArenaCardVisual> map, bool playerSide)
        {
            _liveScratch.Clear();
            for (var i = 0; i < who.SpellTrapZones.Length; i++)
            {
                var c = who.SpellTrapZones[i].Occupant;
                // Always show face-down sets AND face-up S/T (never skip FaceUp=false)
                if (c == null) continue;
                _liveScratch.Add(i);
                var localPos = SpellTrapLocal(i, playerSide);

                if (!map.TryGetValue(i, out var vis) || vis == null)
                {
                    vis = SpawnArenaCard(c, db, SpellTrapPlane, localPos, playerSide, false,
                        ResolveDiskSpawnWorld(playerSide, ArDuelZoneKind.SpellTrap, i));
                    map[i] = vis;
                    Debug.Log(
                        $"[WRLDZ AR] Arena S/T holo spawn · zone={i} · {c.Name} · " +
                        $"face={(c.FaceUp ? "UP" : "DOWN SET")} · you={playerSide}");
                }
                else if (vis.InstanceId != c.InstanceId)
                {
                    ArCardShatterFx.ConsumeQueuedOrQuietDestroy(vis.gameObject, vis.InstanceId, Layer);
                    vis = SpawnArenaCard(c, db, SpellTrapPlane, localPos, playerSide, false,
                        ResolveDiskSpawnWorld(playerSide, ArDuelZoneKind.SpellTrap, i));
                    map[i] = vis;
                }
                else
                {
                    var wasFace = vis.FaceUp;
                    vis.SetTargetLocalPosition(localPos);
                    vis.Sync(c, db);
                    // Spell activation sequence owns face-up S/T reveal (not generic PulseReveal)
                    if (!wasFace && c.FaceUp && !SpellActivationPresentation.HasActivation(c.InstanceId))
                        vis.PulseReveal();
                }
            }

            Purge(map, _liveScratch);
        }

        void SyncExtra(DuelistState who, CardDatabase db, bool playerSide)
        {
            var field = who.FieldSpellZone?.Occupant;
            if (playerSide)
                SyncFieldSlot(field, db, true, ref _fieldP);
            else
                SyncFieldSlot(field, db, false, ref _fieldO);

            if (!playerSide) return;
            SyncPend(who, db, 0, ref _pendL);
            SyncPend(who, db, 1, ref _pendR);
        }

        void SyncFieldSlot(CardInstance field, CardDatabase db, bool playerSide, ref ArArenaCardVisual slot)
        {
            // Yugipedia Field Card Slot is a drawer on the disk, not an S/T stand-up.
            // Arena presentation is ArFieldSpellFloor's left/right wrap only.
            _ = field;
            _ = db;
            _ = playerSide;
            if (slot == null) return;
            ArCardShatterFx.ConsumeQueuedOrQuietDestroy(slot.gameObject, slot.InstanceId, Layer);
            slot = null;
        }

        void SyncPend(DuelistState who, CardDatabase db, int idx, ref ArArenaCardVisual slot)
        {
            var c = who.PendulumZones != null && who.PendulumZones.Length > idx
                ? who.PendulumZones[idx]?.Occupant : null;
            var localPos = ArPlaymatLayout.PendulumArenaLocal(idx);

            if (c == null)
            {
                if (slot != null) { ArObjectUtil.Destroy(slot.gameObject); slot = null; }
                return;
            }

            var kind = idx == 0 ? ArDuelZoneKind.PendulumLeft : ArDuelZoneKind.PendulumRight;
            if (slot == null || slot.InstanceId != c.InstanceId)
            {
                if (slot != null) ArObjectUtil.Destroy(slot.gameObject);
                slot = SpawnArenaCard(c, db, SpellTrapPlane, localPos, true, false,
                    ResolveDiskSpawnWorld(true, kind, 0));
            }
            else
            {
                slot.SetTargetLocalPosition(localPos);
                slot.Sync(c, db);
            }
        }

        ArArenaCardVisual SpawnArenaCard(
            CardInstance card,
            CardDatabase db,
            Transform parent,
            Vector3 localPos,
            bool playerSide,
            bool isMonster,
            Vector3? spawnFromWorld)
        {
            return ArArenaCardVisual.Create(
                parent, card, db, Layer, playerSide, isMonster, localPos, spawnFromWorld,
                PreferDynamicModels);
        }

        Vector3? ResolveDiskSpawnWorld(bool playerSide, ArDuelZoneKind kind, int zoneIndex)
        {
            var disk = playerSide ? PlayerDisk : OppDisk;
            if (disk == null) return null;

            ArDiskZone zone = null;
            switch (kind)
            {
                case ArDuelZoneKind.Monster:
                    if (disk.MonsterZones != null && zoneIndex >= 0 && zoneIndex < disk.MonsterZones.Length)
                        zone = disk.MonsterZones[zoneIndex];
                    break;
                case ArDuelZoneKind.SpellTrap:
                    if (disk.SpellTrapZones != null && zoneIndex >= 0 && zoneIndex < disk.SpellTrapZones.Length)
                        zone = disk.SpellTrapZones[zoneIndex];
                    break;
                case ArDuelZoneKind.FieldSpell:
                    zone = disk.FieldSpellZone;
                    break;
                case ArDuelZoneKind.PendulumLeft:
                    zone = disk.PendulumLeft;
                    break;
                case ArDuelZoneKind.PendulumRight:
                    zone = disk.PendulumRight;
                    break;
            }

            if (zone == null) return disk.transform.position + Vector3.up * 0.05f;
            return zone.transform.position;
        }

        /// <summary>
        /// Arena is cinematic Solid Vision — never a drop / tap target.
        /// Leftover highlight pads from older sessions are destroyed.
        /// </summary>
        public void ApplyLegalHighlights(List<LegalIntentService.LegalSlot> slots) =>
            ClearLegalHighlights();

        public void ClearLegalHighlights()
        {
            foreach (var kv in _legalGlows)
                if (kv.Value != null)
                    ArObjectUtil.Destroy(kv.Value.gameObject);
            _legalGlows.Clear();
            if (_legalGlowRoot != null)
            {
                ArObjectUtil.Destroy(_legalGlowRoot.gameObject);
                _legalGlowRoot = null;
            }
        }

        Vector3 MonsterLocal(int zone, bool playerSide) =>
            ArPlaymatLayout.MonsterArenaLocal(zone, playerSide);

        Vector3 SpellTrapLocal(int zone, bool playerSide) =>
            ArPlaymatLayout.SpellTrapArenaLocal(zone, playerSide);

        public bool TryGetMonsterWorldPos(bool playerSide, int zone, out Vector3 world)
        {
            world = default;
            var map = playerSide ? _monP : _monO;
            if (!map.TryGetValue(zone, out var vis) || vis == null) return false;
            world = vis.transform.position;
            return true;
        }

        /// <summary>Find arena holo by engine card instance (attack/defend VFX).</summary>
        public bool TryGetMonsterVisual(CardInstance card, bool preferPlayerSide, out ArArenaCardVisual vis)
        {
            vis = null;
            if (card == null) return false;

            if (preferPlayerSide)
            {
                if (FindMonsterVisual(_monP, card.InstanceId, out vis)) return true;
                return FindMonsterVisual(_monO, card.InstanceId, out vis);
            }

            if (FindMonsterVisual(_monO, card.InstanceId, out vis)) return true;
            return FindMonsterVisual(_monP, card.InstanceId, out vis);
        }

        static bool FindMonsterVisual(Dictionary<int, ArArenaCardVisual> map, int instanceId,
            out ArArenaCardVisual vis)
        {
            vis = null;
            if (map == null) return false;
            foreach (var kv in map)
            {
                if (kv.Value != null && kv.Value.InstanceId == instanceId)
                {
                    vis = kv.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Clear attack auras on all monster holos.</summary>
        public void ClearAllAttackCharges()
        {
            foreach (var kv in _monP)
                kv.Value?.ClearAttackCharge();
            foreach (var kv in _monO)
                kv.Value?.ClearAttackCharge();
        }

        void Purge(Dictionary<int, ArArenaCardVisual> map, HashSet<int> live)
        {
            _deadScratch.Clear();
            foreach (var kv in map)
                if (!live.Contains(kv.Key)) _deadScratch.Add(kv.Key);
            for (var i = 0; i < _deadScratch.Count; i++)
            {
                var k = _deadScratch[i];
                var vis = map[k];
                if (vis != null)
                {
                    // Only fade when rules queued activation→GY (not MST/destroy shatter)
                    if (vis.IsFadingOut)
                    {
                        // Already fading — leave GO alive until coroutine finishes
                    }
                    else if (SpellActivationPresentation.WantsFadeToGy(vis.InstanceId))
                        vis.RequestFadeToGy();
                    else if (vis.IsMonster && !vis.FaceUp)
                        vis.PlayBattleFlipThenDestroy();
                    else
                        ArCardShatterFx.ConsumeQueuedOrQuietDestroy(vis.gameObject, vis.InstanceId, Layer);
                }

                map.Remove(k);
            }
        }

        /// <summary>
        /// Drain queued coin/die results and raise a hologram over the arena. Placed
        /// above centre, nudged toward the side that tossed/rolled, and slightly
        /// staggered in height so multiple coins (Barrel Dragon = 3) don't overlap.
        /// </summary>
        void ProcessCoinDice()
        {
            var i = 0;
            while (CoinDicePresentation.TryDequeue(out var e))
            {
                var pos = CoinDiceSpawnWorld(e.PlayerSide, i);
                ArCoinDiceFx.Play(e, pos, Layer);
                i++;
            }
        }

        Vector3 CoinDiceSpawnWorld(bool playerSide, int index)
        {
            var up = Vector3.up * (0.55f + index * 0.16f);
            if (ArenaRoot != null)
            {
                // Toward the controller's half of the arena.
                var toward = ArenaRoot.forward * (playerSide ? -0.45f : 0.45f);
                var lateral = ArenaRoot.right * (index * 0.14f);
                return ArenaRoot.position + up + toward + lateral;
            }

            var disk = playerSide ? PlayerDisk : OppDisk;
            if (disk != null)
                return disk.transform.position + up + Vector3.right * (index * 0.14f);

            return up + Vector3.right * (index * 0.14f);
        }

        /// <summary>
        /// Spells that resolve to GY in the same frame as activation never stay as zone
        /// occupants — spawn a short-lived ghost hologram for the rise+fade sequence.
        /// </summary>
        void ProcessSpellGhosts(CardDatabase db)
        {
            while (SpellActivationPresentation.TryDequeueGhost(out var ev))
            {
                if (ev.InstanceId <= 0) continue;
                // Skip if a live visual already owns this instance
                if (FindVisualByInstance(ev.InstanceId) != null) continue;

                var zone = Mathf.Clamp(ev.ZoneIndex >= 0 ? ev.ZoneIndex : 2, 0, 4);
                var local = SpellTrapLocal(zone, ev.PlayerSide);
                var ghostCard = new CardInstance
                {
                    InstanceId = ev.InstanceId,
                    CardId = ev.CardId,
                    FaceUp = true,
                    Position = BattlePosition.Attack
                };
                // Minimal def for art lookup
                if (db != null && db.TryGet(ev.CardId, out var def))
                    ghostCard.Def = def;

                // Create starts face-down→rise sequence because RegisterActivation is live;
                // ResolveToGy is already set so the sequence fades after min hover.
                SpawnArenaCard(ghostCard, db, SpellTrapPlane, local, ev.PlayerSide, false, null);
            }
        }

        ArArenaCardVisual FindVisualByInstance(int instanceId)
        {
            foreach (var kv in _stP)
                if (kv.Value != null && kv.Value.InstanceId == instanceId) return kv.Value;
            foreach (var kv in _stO)
                if (kv.Value != null && kv.Value.InstanceId == instanceId) return kv.Value;
            if (_fieldP != null && _fieldP.InstanceId == instanceId) return _fieldP;
            if (_fieldO != null && _fieldO.InstanceId == instanceId) return _fieldO;
            return null;
        }

    }
}
