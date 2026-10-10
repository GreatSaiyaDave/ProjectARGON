using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WRLDZ.Duel;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The chosen Kuriboh as a small spatial presence in AR duels and Zone Mode.
    /// <list type="bullet">
    /// <item>Camera-facing hologram card (~0.28 m tall) built from the existing navi portrait —
    /// translucent art plate over a SpiritGhostUnlit ghost backing, no PBR.</item>
    /// <item>HOME anchor on the player's LEFT (0.55–0.85 m left, 0.15–0.35 m above eye level,
    /// 0.4–0.7 m forward of the stage camera), re-chosen each cycle so it is outside the hand
    /// volume and the Spirit Dueler disk by ≥ <see cref="ClearanceMeters"/> and never on the
    /// line between the player and the opponent's field centre.</item>
    /// <item>Timer: visible 12–25 s, hidden 8–20 s; 0.4 s fade with a soft scale pop.</item>
    /// <item>Behaviours while visible: Hover-bob 40 %, Orbit 25 %, Dart 20 %, Peek 15 %.
    /// <see cref="ReduceMotion"/> collapses everything to Hover.</item>
    /// <item>Duel events (engine state diff): a player summon triggers a Dart to the new card;
    /// an opponent summon sometimes darts to their newest monster; a draw gives a little bounce.</item>
    /// </list>
    /// Smooth-damped kinematics only — no physics. The overworld HUD companion
    /// (<see cref="KuribohCompanion.AttachMap"/>) is untouched.
    /// Mounted by <see cref="KuribohCompanion.EnsureDuel"/> (called from the interaction-system
    /// build, so AR duels and Zone Mode both get it); <see cref="ArDuelSpace"/> binds the engine
    /// through <see cref="BindUnder"/>. Canon: AR_DUEL_PRESENTATION.md → "Kuriboh AR companion".
    /// Stage units: real metres when <see cref="ArDuelInteractionSystem.LifeSize"/>, otherwise
    /// the simulated tracker's <see cref="SimulatedArmTracker.StageUnitsPerMeter"/>.
    /// </summary>
    public class KuribohArBehaviour : MonoBehaviour
    {
        // ── Tunables (metres; scaled to stage units at runtime) ──
        /// <summary>Accessibility: Hover-bob only, no orbit / dart / peek.</summary>
        public static bool ReduceMotion;

        public const float ClearanceMeters = 0.25f;
        public const float CardHeightMeters = 0.28f;
        public const float HomeLeftMin = 0.55f, HomeLeftMax = 0.85f;
        public const float HomeUpMin = 0.15f, HomeUpMax = 0.35f;
        public const float HomeFwdMin = 0.40f, HomeFwdMax = 0.70f;
        public const float VisibleMin = 12f, VisibleMax = 25f;
        public const float HiddenMin = 8f, HiddenMax = 20f;
        public const float FadeSeconds = 0.4f;
        /// <summary>Lateral keep-out from the player→opponent-field line.</summary>
        const float SightLineClearMeters = 0.35f;
        const float OrbitRadiusMeters = 0.25f; // 0.5 m loop
        const float DartStopShortMeters = 0.30f;
        const float PeekStandOffMeters = 0.30f;

        public static KuribohArBehaviour Current { get; private set; }

        enum Mode { Hover, Orbit, Dart, Peek }
        enum Poi { OppNewestMonster, PlayerNewCard, DeckWell, Referobot }
        enum DartPhase { Out, Wiggle, Return }

        ArDuelInteractionSystem _sys;
        Camera _cam;
        int _layer;
        DuelEngine _engine;

        Transform _art;
        /// <summary>The 3D spirit companion (Resources/WRLDZ/Companions); null = the older portrait quads.</summary>
        SpiritCompanionModel _model3d;
        Transform _plate;
        Material _artMat;
        Material _plateMat;
        float _aspect = 1f;
        Color _accent = new(0.55f, 0.95f, 1f);

        // presence / timer
        bool _shown;
        float _presence;          // 0..1 rendered alpha driver
        float _phaseUntil;        // when the current shown / hidden span ends
        float _hiddenSince;

        // home (camera-relative offsets, stage units)
        Vector3 _homeOffset;      // x = left (+), y = up, z = forward
        float _nextHomeCheck;
        bool _warnedFixedHand;

        // leveled, smoothed head basis (yaw only) — "left / above / forward" of the player
        Vector3 _basisFwd;
        bool _basisInit;

        // per-frame keep-out cache (several queries per frame)
        int _boxFrame = -1;
        bool _handOk, _diskOk;
        Box _handBox, _diskBox;
        readonly List<Renderer> _rendScratch = new();
        // Non-card renderers under the hand volume (floating hand, grip glows …). Rebuilt only when the hand
        // hierarchy changes (hierarchyCount), not every frame: the old per-frame GetComponentsInChildren +
        // GetComponentInParent<ArFloatingCard> per renderer was pure waste while the Kuriboh is visible.
        readonly List<Renderer> _handExtraRends = new();
        Transform _handRendsRoot;
        int _handRendsCount = -1;

        // motion
        Vector3 _pos;
        Vector3 _vel;
        bool _posInit;
        float _yaw;
        float _bounce;            // draw excitement, decays

        // behaviour
        Mode _mode = Mode.Hover;
        float _modeStart;
        float _modeUntil;
        float _orbitAngle;
        float _orbitDir = 1f;
        Poi _dartPoi;
        DartPhase _dartPhase;
        Vector3 _dartStop;
        float _dartPhaseUntil;
        Vector3 _peekAnchorAtStart;

        // duel event tracking
        readonly HashSet<int> _oppMon = new();
        readonly HashSet<int> _plyMon = new();
        readonly HashSet<int> _liveScratch = new();
        CardInstance _oppNewest;
        int _oppNewestZone = -1;
        CardInstance _plyNewest;
        int _plyNewestZone = -1;
        int _lastHandCount = -1;
        Transform _referobot;
        Renderer _referobotRenderer;
        Transform _referobotRendererOf;

        // ─────────────────────────── build ───────────────────────────

        /// <summary>
        /// Create (or reuse) the AR companion under <paramref name="stageRoot"/>. Lives beside the
        /// hand volume — NOT under the arena root — so Zone Mode (arena hidden) keeps it.
        /// Returns null when the account has no Kuriboh team / portrait.
        /// </summary>
        public static KuribohArBehaviour Ensure(ArDuelInteractionSystem sys, Transform stageRoot,
            Camera stageCamera, int layer)
        {
            if (stageRoot == null) return null;
            var existing = stageRoot.Find("KuribohAr");
            if (existing != null)
            {
                var have = existing.GetComponent<KuribohArBehaviour>();
                if (have != null)
                {
                    have._sys = sys;
                    have._cam = stageCamera;
                    return have;
                }
            }

            var cid = KuribohCompanion.CurrentId();
            var has3d = SpiritCompanionModel.Exists(cid);
            Sprite spr = null;
            if (!has3d && (!KuribohCompanion.TryDuelSprite(out spr) || spr == null || spr.texture == null))
                return null;

            var go = new GameObject("KuribohAr");
            go.transform.SetParent(stageRoot, false);
            go.layer = layer;
            var kb = go.AddComponent<KuribohArBehaviour>();
            kb._sys = sys;
            kb._cam = stageCamera;
            kb._layer = layer;
            if (sys != null && sys.PlayerDisk != null) kb._accent = sys.PlayerDisk.Accent;
            if (has3d) kb.BuildVisual3d(cid); else kb.BuildVisual(spr);
            kb.PickHome(force: true);
            // Start hidden for a short beat so the disk deploy / opening draw owns the first seconds.
            kb._shown = false;
            kb._presence = 0f;
            kb._hiddenSince = Time.time;
            kb._phaseUntil = Time.time + Random.Range(3f, 6f);
            kb.ApplyPresence();
            Current = kb;
            Debug.Log(
                $"[WRLDZ AR] Kuriboh companion mounted · left {HomeLeftMin:0.00}–{HomeLeftMax:0.00} m · " +
                $"up {HomeUpMin:0.00}–{HomeUpMax:0.00} m · fwd {HomeFwdMin:0.00}–{HomeFwdMax:0.00} m · " +
                $"clearance {ClearanceMeters:0.00} m · reduceMotion={ReduceMotion}");
            return kb;
        }

        /// <summary>
        /// Bind the companion under <paramref name="stageRoot"/> (if any) to <paramref name="engine"/>.
        /// Safe to call on every engine bind; no-op when the same engine is already bound.
        /// </summary>
        public static void BindUnder(Transform stageRoot, DuelEngine engine)
        {
            if (stageRoot == null) return;
            var t = stageRoot.Find("KuribohAr");
            var kb = t != null ? t.GetComponent<KuribohArBehaviour>() : null;
            if (kb != null) kb.Bind(engine);
        }

        void BuildVisual(Sprite spr)
        {
            var tex = spr.texture;
            _aspect = tex.height > 0 ? tex.width / (float)tex.height : 1f;

            // Art plate — sprite alpha respected (keyed backdrop), translucent tint.
            _art = Quad("Art");
            _artMat = BuildArtMaterial(tex);
            _art.GetComponent<MeshRenderer>().sharedMaterial = _artMat;

            // Ghost backing — SpiritGhostUnlit fill + rim, sits a hair behind the art.
            _plate = Quad("GhostPlate");
            _plateMat = BuildPlateMaterial(_accent);
            _plate.GetComponent<MeshRenderer>().sharedMaterial = _plateMat;
            _plate.localPosition = new Vector3(0f, 0f, 0.006f);

            ApplyScale(1f);
        }

        void BuildVisual3d(string id)
        {
            _aspect = 1f;
            _model3d = SpiritCompanionModel.Spawn(id, transform, _layer, 1f);
            // the behaviour billboards +Z away from the camera: the model's face (+Z) turns to you
            _model3d.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _model3d.BobAmount = 0.03f;   // the hover / orbit motion already carries it
            ApplyScale(1f);
        }

        Transform Quad(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) ArObjectUtil.Destroy(col);
            go.transform.SetParent(transform, false);
            go.layer = _layer;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return go.transform;
        }

        static Material BuildArtMaterial(Texture tex)
        {
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(sh) { name = "KuribohArArt" };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + 5;
            return mat;
        }

        static Material BuildPlateMaterial(Color accent)
        {
            var sh = Shader.Find("WRLDZ/SpiritGhostUnlit");
            var ghost = sh != null && sh.isSupported;
            if (!ghost) sh = ArFieldMaterials.HoloShader;
            var mat = new Material(sh) { name = "KuribohArGhost" };
            if (!ghost)
            {
                // HoloUnlit fallback: plain translucent accent glass (no fresnel rim), alpha-blended, double-sided.
                var glass = accent;
                glass.a = 0.30f;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", glass);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
            }

            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (mat.HasProperty("_Fill")) mat.SetFloat("_Fill", 0.30f);
            if (mat.HasProperty("_RimPower")) mat.SetFloat("_RimPower", 2.6f);
            if (mat.HasProperty("_RimStrength")) mat.SetFloat("_RimStrength", 0.55f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + 4;
            return mat;
        }

        // ─────────────────────────── engine hook ───────────────────────────

        /// <summary>Follow duel events (summons / draws) — optional; no engine = idle behaviours only.</summary>
        public void Bind(DuelEngine engine)
        {
            if (ReferenceEquals(engine, _engine)) return;
            Unbind();
            _engine = engine;
            if (_engine == null) return;
            _engine.OnStateChanged += OnState;
            SnapshotZones(_oppMon, _engine.Opponent);
            SnapshotZones(_plyMon, _engine.Player);
            _lastHandCount = _engine.Player?.HandCount ?? -1;
        }

        public void Unbind()
        {
            if (_engine != null) _engine.OnStateChanged -= OnState;
            _engine = null;
            _oppNewest = _plyNewest = null;
            _oppNewestZone = _plyNewestZone = -1;
        }

        void OnDestroy()
        {
            Unbind();
            if (Current == this) Current = null;
            if (_artMat != null) ArObjectUtil.Destroy(_artMat);
            if (_plateMat != null) ArObjectUtil.Destroy(_plateMat);
        }

        static void SnapshotZones(HashSet<int> into, DuelistState who)
        {
            into.Clear();
            if (who?.MonsterZones == null) return;
            foreach (var z in who.MonsterZones)
                if (z?.Occupant != null) into.Add(z.Occupant.InstanceId);
        }

        void OnState()
        {
            if (_engine == null) return;
            var playerSummoned = DiffNew(_plyMon, _engine.Player, _liveScratch, out var pNew, out var pZone);
            if (playerSummoned)
            {
                _plyNewest = pNew;
                _plyNewestZone = pZone;
            }

            var oppSummoned = DiffNew(_oppMon, _engine.Opponent, _liveScratch, out var oNew, out var oZone);
            if (oppSummoned)
            {
                _oppNewest = oNew;
                _oppNewestZone = oZone;
            }

            // A POI that left the field is dropped (TryMonsterWorld would fail anyway).
            if (_plyNewest != null && !_plyMon.Contains(_plyNewest.InstanceId)) _plyNewest = null;
            if (_oppNewest != null && !_oppMon.Contains(_oppNewest.InstanceId)) _oppNewest = null;

            var hand = _engine.Player?.HandCount ?? -1;
            if (_lastHandCount >= 0 && hand > _lastHandCount)
                _bounce = 1f; // draw: little excited bounce
                _model3d?.Bounce();
            _lastHandCount = hand;

            if (ReduceMotion) return;
            if (playerSummoned)
            {
                // Kuriboh gets curious about what you just played.
                WakeIfResting();
                if (_shown && _mode != Mode.Dart) BeginDart(Poi.PlayerNewCard);
            }
            else if (oppSummoned && _shown && _mode != Mode.Dart && Random.value < 0.5f)
            {
                BeginDart(Poi.OppNewestMonster);
            }
        }

        static bool DiffNew(HashSet<int> seen, DuelistState who, HashSet<int> live,
            out CardInstance newest, out int zone)
        {
            newest = null;
            zone = -1;
            if (who?.MonsterZones == null) return false;
            var any = false;
            live.Clear();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var occ = who.MonsterZones[i]?.Occupant;
                if (occ == null) continue;
                live.Add(occ.InstanceId);
                if (seen.Contains(occ.InstanceId)) continue;
                any = true;
                newest = occ;
                zone = i;
            }

            seen.Clear();
            seen.UnionWith(live);
            return any;
        }

        /// <summary>Hidden for a while already → pop in early for the moment.</summary>
        void WakeIfResting()
        {
            if (_shown) return;
            if (Time.time - _hiddenSince < 3f) return;
            Show();
        }

        // ─────────────────────────── frame ───────────────────────────

        void LateUpdate()
        {
            var cam = StageCamera();
            if (cam == null) return;
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            var now = Time.time;
            UpdateBasis(cam, dt);

            // Timer: shown ↔ hidden
            if (_shown && now >= _phaseUntil) Hide();
            else if (!_shown && now >= _phaseUntil) Show();

            // Presence fade
            var target = _shown ? 1f : 0f;
            var step = dt / FadeSeconds;
            _presence = Mathf.MoveTowards(_presence, target, step);
            ApplyPresence();

            // Home re-validation (volumes moved) — interruptible behaviours
            if (now >= _nextHomeCheck)
            {
                _nextHomeCheck = now + 0.5f;
                if (!HomeIsClear(cam))
                {
                    PickHome(force: false);
                    if (_mode != Mode.Hover) BeginHover();
                }
            }

            var home = HomeWorld(cam);
            if (!_posInit)
            {
                _pos = home;
                _posInit = true;
            }

            _bounce = Mathf.MoveTowards(_bounce, 0f, dt * 1.2f);

            // Behaviour tick → desired position
            Vector3 desired;
            var smooth = 0.35f;
            if (!_shown && _presence <= 0.001f)
            {
                desired = home; // park at home while hidden so it pops in place
                if (_mode != Mode.Hover) BeginHover();
            }
            else
            {
                if (ReduceMotion && _mode != Mode.Hover) BeginHover();
                if (now >= _modeUntil) PickNextMode(cam);
                desired = TickMode(cam, home, now, dt, out smooth);
            }

            _pos = Vector3.SmoothDamp(_pos, desired, ref _vel, smooth, Mathf.Infinity, dt);
            // Guard: whatever the path (dart across the arm, head turn), never sit inside the
            // hand / disk keep-outs — slide along their surface instead.
            if (_presence > 0.001f)
            {
                var cleared = KeepClear(cam, _pos);
                if ((cleared - _pos).sqrMagnitude > 1e-8f)
                {
                    _pos = cleared;
                    _vel *= 0.5f;
                }
            }

            transform.position = _pos;

            // Billboard toward the stage camera, slow yaw wobble, slight bank into motion
            var to = _pos - cam.transform.position;
            if (to.sqrMagnitude > 1e-6f)
            {
                var face = Quaternion.LookRotation(to.normalized, cam.transform.up);
                _yaw = Mathf.Sin(now * 0.55f) * 10f;
                var lateral = Vector3.Dot(_vel, cam.transform.right);
                var bank = Mathf.Clamp(-lateral * 18f, -14f, 14f);
                transform.rotation = face * Quaternion.Euler(0f, _yaw, bank);
            }
        }

        Camera StageCamera()
        {
            if (_cam != null && _cam.enabled) return _cam;
            var mask = 1 << gameObject.layer;
            var cams = Camera.allCameras;
            for (var i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c != null && c.enabled && (c.cullingMask & mask) != 0)
                    return c;
            }
            return Camera.main;
        }

        /// <summary>Stage units per metre (1 for lenses / life-size, sim compression otherwise).</summary>
        float U
        {
            get
            {
                if (_sys == null) return 1f;
                if (_sys.LifeSize) return 1f;
                if (_sys.Tracker is SimulatedArmTracker sim) return sim.StageUnitsPerMeter;
                return 0.5f;
            }
        }

        // ─────────────────────────── presence ───────────────────────────

        void Show()
        {
            _shown = true;
            _phaseUntil = Time.time + Random.Range(VisibleMin, VisibleMax);
            PickHome(force: true);
            _posInit = false; // pop in at the new home
            _vel = Vector3.zero;
            BeginHover();
        }

        void Hide()
        {
            _shown = false;
            _hiddenSince = Time.time;
            _phaseUntil = Time.time + Random.Range(HiddenMin, HiddenMax);
        }

        void ApplyPresence()
        {
            var p = Mathf.Clamp01(_presence);
            var e = Mathf.SmoothStep(0f, 1f, p);
            // Soft pop: overshoot on the way in, shrink on the way out.
            float pop;
            if (_shown)
            {
                var back = 1f + 1.70158f;
                var t = p - 1f;
                var easeOutBack = 1f + t * t * ((back + 1f) * t + back);
                pop = Mathf.Lerp(0.72f, 1f, easeOutBack);
            }
            else
                pop = Mathf.Lerp(0.82f, 1f, e);

            ApplyScale(pop * (1f + _bounce * 0.08f));
            if (_model3d != null)
            {
                // a spirit: nearly solid, a touch translucent in the air
                _model3d.SetAlpha(0.94f * e);
            }

            if (_artMat != null)
            {
                var c = Color.Lerp(Color.white, _accent, 0.18f);
                c.a = 0.78f * e;
                _artMat.color = c;
                if (_artMat.HasProperty("_BaseColor")) _artMat.SetColor("_BaseColor", c);
            }

            if (_plateMat != null)
            {
                var c = _accent;
                c.a = 0.14f * e;
                if (_plateMat.HasProperty("_Color")) _plateMat.SetColor("_Color", c);
                if (_plateMat.HasProperty("_BaseColor")) _plateMat.SetColor("_BaseColor", c);
                _plateMat.color = c;
            }

            var on = e > 0.002f;
            if (_art != null && _art.gameObject.activeSelf != on) _art.gameObject.SetActive(on);
            if (_plate != null && _plate.gameObject.activeSelf != on) _plate.gameObject.SetActive(on);
        }

        void ApplyScale(float mul)
        {
            var h = CardHeightMeters * U * mul;
            var w = h * _aspect;
            if (_model3d != null) _model3d.ScaleMul = h * 1.15f;   // the model is 1 unit tall
            if (_art != null) _art.localScale = new Vector3(w, h, 1f);
            if (_plate != null) _plate.localScale = new Vector3(w * 1.12f, h * 1.10f, 1f);
        }

        // ─────────────────────────── home anchor ───────────────────────────

        /// <summary>
        /// Leveled head basis: forward = camera forward flattened onto the ground plane and lightly
        /// smoothed (head jitter / glancing down at the disk must not swing the anchor), up = world
        /// up (so "above eye level" stays above the eyes when the player looks down).
        /// </summary>
        void UpdateBasis(Camera cam, float dt)
        {
            var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up); // looking straight down
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            if (!_basisInit)
            {
                _basisFwd = f;
                _basisInit = true;
                return;
            }

            _basisFwd = Vector3.Slerp(_basisFwd, f, 1f - Mathf.Exp(-6f * dt)).normalized;
        }

        void Basis(Camera cam, out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            if (!_basisInit) UpdateBasis(cam, 0f);
            fwd = _basisFwd;
            up = Vector3.up;
            right = Vector3.Cross(up, fwd).normalized;
        }

        Vector3 HomeWorld(Camera cam) => OffsetToWorld(cam, _homeOffset);

        /// <summary>Offset x = metres LEFT (+), y = above eye level, z = forward (stage units).</summary>
        Vector3 OffsetToWorld(Camera cam, Vector3 off)
        {
            Basis(cam, out var r, out var u, out var f);
            return cam.transform.position - r * off.x + u * off.y + f * off.z;
        }

        bool HomeIsClear(Camera cam) => CandidateOk(cam, HomeWorld(cam));

        /// <summary>
        /// Pick a fresh home offset: random in the spec ranges, rejected while inside the
        /// clearance of the hand volume / disk or on the player→opponent sight line.
        /// Falls back to the best-clearance sample when nothing passes (crowded rig).
        /// </summary>
        void PickHome(bool force)
        {
            var cam = StageCamera();
            var u = U;
            if (cam == null)
            {
                _homeOffset = new Vector3(0.7f, 0.25f, 0.55f) * u;
                return;
            }

            if (!force && HomeIsClear(cam)) return;

            Vector3 best = default;
            var bestScore = float.NegativeInfinity;
            for (var i = 0; i < 24; i++)
            {
                var off = new Vector3(
                    Random.Range(HomeLeftMin, HomeLeftMax),
                    Random.Range(HomeUpMin, HomeUpMax),
                    Random.Range(HomeFwdMin, HomeFwdMax)) * u;
                var world = OffsetToWorld(cam, off);
                var score = ClearanceScore(cam, world);
                if (score >= 0f)
                {
                    _homeOffset = off;
                    return;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = off;
                }
            }

            _homeOffset = best;
        }

        bool CandidateOk(Camera cam, Vector3 world) => ClearanceScore(cam, world) >= 0f;

        /// <summary>Dart / peek stops: only the hand + disk keep-outs (a dart may cross the sight line).</summary>
        bool VolumesOk(Camera cam, Vector3 world) => VolumeScore(cam, world) >= 0f;

        float VolumeScore(Camera cam, Vector3 world)
        {
            var need = ClearanceMeters * U;
            var score = float.PositiveInfinity;
            if (TryHandBox(cam, out var hb))
                score = Mathf.Min(score, hb.Distance(world) - need);
            if (TryDiskBox(out var db))
                score = Mathf.Min(score, db.Distance(world) - need);
            return score;
        }

        /// <summary>
        /// Home anchors: ≥ 0 when every keep-out is satisfied (hand, disk, player→opponent sight
        /// line); the most negative violation otherwise.
        /// </summary>
        float ClearanceScore(Camera cam, Vector3 world)
        {
            var u = U;
            var score = VolumeScore(cam, world);
            if (TryOppFieldCentre(out var opp))
            {
                var d = DistancePointSegment(world, cam.transform.position, opp);
                score = Mathf.Min(score, d - SightLineClearMeters * u);
            }

            return float.IsPositiveInfinity(score) ? 0f : score;
        }

        // ─────────────────────────── keep-out volumes ───────────────────────────

        struct Box
        {
            public Vector3 Min, Max;
            public Vector3 Center => (Min + Max) * 0.5f;

            public static Box FromPoint(Vector3 c, Vector3 ext) => new() { Min = c - ext, Max = c + ext };

            public void Encapsulate(Vector3 p)
            {
                Min = Vector3.Min(Min, p);
                Max = Vector3.Max(Max, p);
            }

            public void Expand(Vector3 ext)
            {
                Min -= ext;
                Max += ext;
            }

            public Vector3 Closest(Vector3 p) =>
                new(Mathf.Clamp(p.x, Min.x, Max.x), Mathf.Clamp(p.y, Min.y, Max.y), Mathf.Clamp(p.z, Min.z, Max.z));

            public float Distance(Vector3 p) => Vector3.Distance(p, Closest(p));
        }

        /// <summary>
        /// World AABB of the player's floating hand cards. When the hand volume is missing or
        /// inactive (Zone Mode hides it) a conservative fixed chest box in front of the camera is used.
        /// </summary>
        bool TryHandBox(Camera cam, out Box box)
        {
            RefreshBoxes(cam);
            box = _handBox;
            return _handOk;
        }

        bool TryDiskBox(out Box box)
        {
            RefreshBoxes(StageCamera());
            box = _diskBox;
            return _diskOk;
        }

        /// <summary>Volumes move every frame (arm tracking, fan re-layout) — compute once per frame.</summary>
        void RefreshBoxes(Camera cam)
        {
            if (_boxFrame == Time.frameCount) return;
            _boxFrame = Time.frameCount;
            _handOk = cam != null && ComputeHandBox(cam, out _handBox);
            _diskOk = ComputeDiskBox(out _diskBox);
        }

        bool ComputeHandBox(Camera cam, out Box box)
        {
            var hv = _sys != null ? _sys.HandVolume : null;
            var u = U;
            if (hv == null || !hv.gameObject.activeInHierarchy)
            {
                if (!_warnedFixedHand)
                {
                    _warnedFixedHand = true;
                    Debug.Log("[WRLDZ AR] Kuriboh: hand volume unavailable — using conservative fixed chest offsets.");
                }

                // Conservative chest box: 0.35 m ahead, 0.25 m below the eyes, 0.8 m wide.
                // Axis-aligned half-extents take the max of x/z so any head yaw stays covered.
                Basis(cam, out _, out _, out var fwd);
                var c = cam.transform.position + fwd * 0.35f * u - Vector3.up * 0.25f * u;
                box = Box.FromPoint(c, new Vector3(0.40f, 0.18f, 0.40f) * u);
                return true;
            }

            var any = false;
            box = default;
            var cards = hv.Cards;
            for (var i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.IsLockedToZone) continue;
                var p = c.transform.position;
                if (!any)
                {
                    box = Box.FromPoint(p, Vector3.zero);
                    any = true;
                }
                else box.Encapsulate(p);
            }

            if (any)
            {
                // Expand by the largest card half-extent (cards yaw in the fan; take the diagonal).
                var ls = cards[0] != null ? cards[0].transform.lossyScale : Vector3.one * hv.CardScale;
                var half = Vector3.Scale(ArFloatingCard.BodyLocalSize * 0.5f, ls);
                var r = half.magnitude;
                box.Expand(new Vector3(r, r, r));
            }

            // Anything else the hand volume renders (the translucent floating right hand that
            // holds the cards, grip glows, …) joins the keep-out. Oversized renderers (> 1.5 m,
            // e.g. a full-screen effect parented here) are ignored so they cannot swallow the stage.
            RefreshHandExtraRenderers(hv.transform);
            var maxSpan = 1.5f * Mathf.Max(u, 0.5f);
            for (var i = 0; i < _handExtraRends.Count; i++)
            {
                var rd = _handExtraRends[i];
                if (rd == null || !rd.enabled || !rd.gameObject.activeInHierarchy) continue;
                var b = rd.bounds;
                if (b.size.x > maxSpan || b.size.y > maxSpan || b.size.z > maxSpan) continue;
                if (!any)
                {
                    box = new Box { Min = b.min, Max = b.max };
                    any = true;
                }
                else
                {
                    box.Encapsulate(b.min);
                    box.Encapsulate(b.max);
                }
            }

            if (any) return true;

            // Empty hand: the fan's rest footprint in front of the torso.
            box = Box.FromPoint(hv.transform.position, new Vector3(0.36f, 0.16f, 0.10f) * Mathf.Max(u, 0.5f));
            return true;
        }

        /// <summary>
        /// Cache the hand volume's non-card, non-particle renderers (inactive ones included; filtered per frame).
        /// Cards are covered by the card loop in <see cref="ComputeHandBox"/>, which skips zone-locked ones.
        /// </summary>
        void RefreshHandExtraRenderers(Transform hvRoot)
        {
            var count = hvRoot.hierarchyCount;
            if (hvRoot == _handRendsRoot && count == _handRendsCount) return;
            _handRendsRoot = hvRoot;
            _handRendsCount = count;
            _handExtraRends.Clear();
            _rendScratch.Clear();
            hvRoot.GetComponentsInChildren(true, _rendScratch);
            for (var i = 0; i < _rendScratch.Count; i++)
            {
                var rd = _rendScratch[i];
                if (rd == null || rd is ParticleSystemRenderer) continue;
                if (rd.GetComponentInParent<ArFloatingCard>(true) != null) continue;
                _handExtraRends.Add(rd);
            }

            _rendScratch.Clear();
        }

        /// <summary>World AABB of the player's Spirit Dueler disk (full mesh bounds, blade pivot space).</summary>
        bool ComputeDiskBox(out Box box)
        {
            box = default;
            var disk = _sys != null ? _sys.PlayerDisk : null;
            if (disk == null || !disk.gameObject.activeInHierarchy) return false;
            var pivot = disk.BladePivot != null ? disk.BladePivot : disk.DiskRoot;
            if (pivot == null)
            {
                box = Box.FromPoint(disk.transform.position, Vector3.one * 0.35f * U);
                return true;
            }

            var mn = ArPlaymatLayout.DiskMeshBoundsMin;
            var mx = ArPlaymatLayout.DiskMeshBoundsMax;
            var first = true;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? mn.x : mx.x,
                    (i & 2) == 0 ? mn.y : mx.y,
                    (i & 4) == 0 ? mn.z : mx.z);
                var w = pivot.TransformPoint(corner);
                if (first)
                {
                    box = Box.FromPoint(w, Vector3.zero);
                    first = false;
                }
                else box.Encapsulate(w);
            }

            // Deck magazine + wrist cuff sit a little outside the sculpted blade.
            box.Expand(Vector3.one * 0.03f * Mathf.Max(U, 0.5f));
            return true;
        }

        /// <summary>Opponent's field centre (middle monster zone) — absent in Zone Mode (arena hidden).</summary>
        bool TryOppFieldCentre(out Vector3 world)
        {
            world = default;
            var arena = _sys != null ? _sys.Arena : null;
            if (arena == null || !arena.gameObject.activeInHierarchy || arena.ArenaRoot == null) return false;
            world = arena.ArenaRoot.TransformPoint(ArPlaymatLayout.MonsterArenaLocal(2, playerSide: false));
            return true;
        }

        static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return Vector3.Distance(p, a);
            var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Push a point out of both keep-outs (used by orbit / peek paths).</summary>
        Vector3 KeepClear(Camera cam, Vector3 p)
        {
            var need = ClearanceMeters * U;
            for (var pass = 0; pass < 2; pass++)
            {
                if (TryHandBox(cam, out var hb)) p = PushOut(p, hb, need);
                if (TryDiskBox(out var db)) p = PushOut(p, db, need);
            }

            return p;
        }

        static Vector3 PushOut(Vector3 p, Box b, float need)
        {
            var closest = b.Closest(p);
            var d = p - closest;
            var dist = d.magnitude;
            if (dist >= need) return p;
            var n = dist > 1e-4f ? d / dist : (p - b.Center).normalized;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
            return closest + n * (need + 0.01f);
        }

        // ─────────────────────────── behaviours ───────────────────────────

        void PickNextMode(Camera cam)
        {
            if (ReduceMotion)
            {
                BeginHover();
                return;
            }

            var roll = Random.value;
            if (roll < 0.40f) BeginHover();
            else if (roll < 0.65f) BeginOrbit();
            else if (roll < 0.85f)
            {
                if (!BeginDart(PickPoi())) BeginHover();
            }
            else
            {
                if (!BeginPeek(cam)) BeginHover();
            }
        }

        void BeginHover()
        {
            _mode = Mode.Hover;
            _modeStart = Time.time;
            _modeUntil = Time.time + Random.Range(4f, 7f);
        }

        void BeginOrbit()
        {
            _mode = Mode.Orbit;
            _modeStart = Time.time;
            _orbitAngle = Mathf.PI; // outer-left edge of the loop — farthest from the cards / disk
            _orbitDir = Random.value < 0.5f ? 1f : -1f;
            _modeUntil = Time.time + (Mathf.PI * 2f) / 0.9f + 0.5f; // one loop at 0.9 rad/s
        }

        Poi PickPoi()
        {
            // Prefer the freshest thing on the field; fall back to fixtures.
            var opts = new List<Poi>(4);
            if (_oppNewest != null) opts.Add(Poi.OppNewestMonster);
            if (_plyNewest != null) opts.Add(Poi.PlayerNewCard);
            if (TryGetPoi(Poi.Referobot, out _)) opts.Add(Poi.Referobot);
            if (TryGetPoi(Poi.DeckWell, out _)) opts.Add(Poi.DeckWell);
            if (opts.Count == 0) return Poi.DeckWell;
            return opts[Random.Range(0, opts.Count)];
        }

        bool BeginDart(Poi poi)
        {
            var cam = StageCamera();
            if (cam == null || !TryGetPoi(poi, out var target)) return false;
            var u = U;
            var dir = target - _pos;
            var dist = dir.magnitude;
            if (dist < 1e-3f) return false;
            dir /= dist;
            var stop = target - dir * DartStopShortMeters * u;
            // Back off along the approach until clear of the hand / disk keep-outs.
            var ok = false;
            for (var i = 0; i < 12; i++)
            {
                if (VolumesOk(cam, stop))
                {
                    ok = true;
                    break;
                }

                stop -= dir * 0.05f * u;
            }

            if (!ok) return false;
            _mode = Mode.Dart;
            _dartPoi = poi;
            _dartPhase = DartPhase.Out;
            _dartStop = stop;
            _modeStart = Time.time;
            _dartPhaseUntil = Time.time + 2.2f;
            _modeUntil = Time.time + 6.5f; // safety cap; Return ends it sooner
            return true;
        }

        bool TryGetPoi(Poi poi, out Vector3 world)
        {
            world = default;
            var u = U;
            switch (poi)
            {
                case Poi.OppNewestMonster:
                    return TryMonsterWorld(_oppNewest, _oppNewestZone, playerSide: false, out world);
                case Poi.PlayerNewCard:
                    return TryMonsterWorld(_plyNewest, _plyNewestZone, playerSide: true, out world);
                case Poi.DeckWell:
                {
                    var disk = _sys != null ? _sys.PlayerDisk : null;
                    if (disk == null || !disk.gameObject.activeInHierarchy) return false;
                    world = disk.MainDeckWorldCenter;
                    return true;
                }
                case Poi.Referobot:
                {
                    var arena = _sys != null ? _sys.Arena : null;
                    if (arena == null || !arena.gameObject.activeInHierarchy || arena.ArenaRoot == null) return false;
                    if (_referobot == null) _referobot = arena.ArenaRoot.Find("MrReferobotBody");
                    if (_referobot == null || !_referobot.gameObject.activeInHierarchy) return false;
                    if (_referobotRendererOf != _referobot || _referobotRenderer == null)
                    {
                        _referobotRendererOf = _referobot;
                        _referobotRenderer = _referobot.GetComponentInChildren<Renderer>();
                    }

                    var r = _referobotRenderer;
                    world = r != null ? r.bounds.center + Vector3.up * r.bounds.extents.y * 0.5f
                                      : _referobot.position + Vector3.up * 0.6f * u;
                    return true;
                }
            }

            return false;
        }

        bool TryMonsterWorld(CardInstance card, int zone, bool playerSide, out Vector3 world)
        {
            world = default;
            var arena = _sys != null ? _sys.Arena : null;
            if (card == null || arena == null || !arena.gameObject.activeInHierarchy) return false;
            if (arena.TryGetMonsterVisual(card, playerSide, out var vis) && vis != null)
            {
                world = vis.transform.position + Vector3.up * 0.12f * U;
                return true;
            }

            if (zone >= 0 && arena.TryGetMonsterWorldPos(playerSide, zone, out world))
            {
                world += Vector3.up * 0.12f * U;
                return true;
            }

            return false;
        }

        bool BeginPeek(Camera cam)
        {
            var hv = _sys != null ? _sys.HandVolume : null;
            if (hv == null || !hv.gameObject.activeInHierarchy) return false;
            if (!TryHandBox(cam, out var hb)) return false;
            _mode = Mode.Peek;
            _modeStart = Time.time;
            _peekAnchorAtStart = hb.Center;
            _modeUntil = Time.time + 3.6f;
            return true;
        }

        Vector3 TickMode(Camera cam, Vector3 home, float now, float dt, out float smooth)
        {
            var u = U;
            var t = cam.transform;
            Basis(cam, out var br, out var bu, out var bf);
            smooth = 0.35f;
            switch (_mode)
            {
                case Mode.Orbit:
                {
                    _orbitAngle += 0.9f * _orbitDir * dt;
                    var r = OrbitRadiusMeters * u;
                    var p = home
                            + br * (Mathf.Cos(_orbitAngle) * r)
                            + bf * (Mathf.Sin(_orbitAngle) * r)
                            + bu * (Mathf.Sin(_orbitAngle * 2f) * 0.06f * u);
                    // Stay on the left hemisphere of the view (≥ 0.30 m left of the eyes).
                    var lateral = Vector3.Dot(p - t.position, br);
                    var minLeft = -0.30f * u;
                    if (lateral > minLeft) p -= br * (lateral - minLeft);
                    smooth = 0.22f;
                    return KeepClear(cam, p);
                }
                case Mode.Dart:
                {
                    smooth = 0.24f;
                    switch (_dartPhase)
                    {
                        case DartPhase.Out:
                            if (TryGetPoi(_dartPoi, out var live))
                            {
                                // Re-aim on a moving target, keep the stop-short distance.
                                var dir = live - _pos;
                                if (dir.sqrMagnitude > 1e-4f)
                                {
                                    var cand = live - dir.normalized * DartStopShortMeters * u;
                                    if (VolumesOk(cam, cand)) _dartStop = cand;
                                }
                            }

                            if (Vector3.Distance(_pos, _dartStop) < 0.04f * u || now >= _dartPhaseUntil)
                            {
                                _dartPhase = DartPhase.Wiggle;
                                _dartPhaseUntil = now + 1f;
                            }

                            return _dartStop;
                        case DartPhase.Wiggle:
                        {
                            smooth = 0.08f;
                            var w = (now - _modeStart) * 14f;
                            var jit = br * (Mathf.Sin(w) * 0.025f * u) + bu * (Mathf.Cos(w * 1.3f) * 0.02f * u);
                            if (now >= _dartPhaseUntil)
                            {
                                _dartPhase = DartPhase.Return;
                                _dartPhaseUntil = now + 2f;
                            }

                            return _dartStop + jit;
                        }
                        default:
                            if (Vector3.Distance(_pos, home) < 0.05f * u || now >= _dartPhaseUntil)
                                BeginHover();
                            return home;
                    }
                }
                case Mode.Peek:
                {
                    if (!TryHandBox(cam, out var hb) ||
                        Vector3.Distance(hb.Center, _peekAnchorAtStart) > 0.12f * u)
                    {
                        BeginHover(); // hand moved — abort the dip
                        return home;
                    }

                    var el = now - _modeStart;
                    // 0–1.2 s dip in, hold to 2.2 s, then retreat.
                    var dipping = el < 2.2f;
                    if (!dipping)
                    {
                        smooth = 0.3f;
                        return home;
                    }

                    var closest = hb.Closest(home);
                    var away = home - closest;
                    if (away.sqrMagnitude < 1e-6f) away = home - hb.Center;
                    if (away.sqrMagnitude < 1e-6f) away = -br;
                    var p = closest + away.normalized * (PeekStandOffMeters * u + 0.02f);
                    smooth = 0.28f;
                    return KeepClear(cam, p);
                }
                default:
                {
                    // Hover-bob: sine bob (+ draw bounce), slow drift.
                    var amp = (0.03f + _bounce * 0.05f) * u;
                    var bob = Mathf.Sin(now * 1.6f) * amp;
                    var sway = Mathf.Sin(now * 0.7f + 1.3f) * 0.015f * u;
                    return home + bu * bob + br * sway;
                }
            }
        }
    }
}
