using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation.ArInteraction;
using WRLDZ.UI;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// AR duel spatial stage — anime dual-board presentation.
    /// Product: OpenXR lenses (Quest 3) — life-size arm disks via <see cref="ArLensesSession"/>.
    /// Fallback: S23 phone RT + PC EditorSim. Same interaction graph; never a second engine.
    ///
    /// Boards:
    /// · Card Field — Spirit Dueler disks (physical cards on both arms)
    /// · Hologram Field — empty air midfield (no visible playmat); holos when cards project
    /// </summary>
    public class ArDuelSpace : MonoBehaviour
    {
        const int Layer = 28;
        /// <summary>S23-class portrait RT (good clarity without cooking the SoC).</summary>
        const int RtW = 1080;
        const int RtH = 1920;

        RenderTexture _rt;
        Camera _cam;
        Transform _stage;
        Transform _arena;
        Transform _playerHolos;
        Transform _oppHolos;
        Transform _passthroughPlane;
        Material _passthroughMat;
        RawImage _target;
        Text _badge;
        WebCamTexture _webCam;
        ArPhoneCamera.Session _camSession;
        readonly Dictionary<int, GameObject> _pH = new();
        readonly Dictionary<int, GameObject> _oH = new();
        float _impactFlash;
        bool _ready;
        /// <summary>True after <see cref="BuildStage"/> completed (interaction may still open camera async).</summary>
        public bool Ready => _ready;
        bool _arLive;
        float _ptBaseW = 7.2f;
        float _ptBaseH = 4.5f;
        ArDuelInteractionSystem _interaction;
        ArFreeLookCamera _freeLook;
        DuelEngine _boundEngine;
        DuelEngine _interactionEngine;
        CardDatabase _boundDb;

        /// <summary>True when device camera feed is active as passthrough.</summary>
        public bool ArLive => _arLive;

        /// <summary>Human-readable AR status for HUD.</summary>
        public string ArStatus { get; private set; } = "starting…";

        /// <summary>Core AR interaction stack (arm disk, hand, arena, anime, sync).</summary>
        public ArDuelInteractionSystem Interaction => _interaction;

        /// <summary>Stage camera that renders the disk / arena into the UI viewport.</summary>
        public Camera StageCamera => _cam;

        public static ArDuelSpace CreateInUi(Transform uiParent, float x0, float y0, float x1, float y1,
            Transform lifetimeParent = null)
        {
            // Full-bleed AR viewport (Solid Vision stage is the hero — no cyan chrome bar)
            var frame = new GameObject("ArDuelFrame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(uiParent, false);
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(x0, y0);
            frt.anchorMax = new Vector2(x1, y1);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            var fimg = frame.GetComponent<Image>();
            fimg.sprite = UiFoundation.WhiteSprite();
            // Nearly clear under RT so lab chrome doesn't dim Solid Vision
            fimg.color = new Color(0.04f, 0.05f, 0.07f, 0.12f);
            fimg.raycastTarget = false;

            var go = new GameObject("ArDuelSpaceView", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(frame.transform, false);
            Stretch(go.GetComponent<RectTransform>(), 0, 0);
            var raw = go.GetComponent<RawImage>();
            raw.color = Color.white;
            // Must receive pointer for AR hand-card drag into disk zones
            raw.raycastTarget = true;

            var badge = new GameObject("ArBadge", typeof(RectTransform), typeof(Text));
            badge.transform.SetParent(frame.transform, false);
            var brt = badge.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.02f, 0.92f);
            brt.anchorMax = new Vector2(0.55f, 0.99f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            var bt = badge.GetComponent<Text>();
            WrldzType.Style(bt, 11, display: false, heavyOutline: true);
            bt.alignment = TextAnchor.MiddleLeft;
            bt.color = new Color(0.85f, 0.88f, 0.92f, 0.85f);
            bt.raycastTarget = false;

            var host = new GameObject("ArDuelSpaceHost");
            if (lifetimeParent != null)
                host.transform.SetParent(lifetimeParent, false);
            var space = host.AddComponent<ArDuelSpace>();
            space._target = raw;
            space._badge = bt;
            space.BuildStage();
            space.RefreshBadge();
            return space;
        }

        void RefreshBadge()
        {
            if (_badge == null) return;
            // Quiet corner label — Editor hides it (status already in top bar)
            if (Application.isEditor)
            {
                _badge.text = "";
                _badge.gameObject.SetActive(false);
                return;
            }

            _badge.gameObject.SetActive(true);
            if (_arLive)
                _badge.text = Application.isMobilePlatform ? "AR · LIVE" : "PHONE AR";
            else if (ArPresentationTarget.IsSim)
                _badge.text = "SIM";
            else
                _badge.text = ArPresentationTarget.StatusLabel();
        }

        static void Stretch(RectTransform r, float p = 0, float q = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(p, q);
            r.offsetMax = new Vector2(-p, -q);
        }

        void BuildStage()
        {
            var match = AppSession.Instance != null ? AppSession.Instance.PendingArMatch : null;
            var sep = match != null ? match.SeparationMeters : ArDuelMatchConfig.DefaultStandM;

            // Clear stale OpenXR override from a previous device session
            if (Application.isEditor && ArPresentationTarget.IsLenses &&
                !ArLensesSession.IsXrDisplayRunning() &&
                !ArPresentationTarget.IsSalvageOst)
                ArPresentationTarget.SetOverride(null);

            if (ArPresentationTarget.IsSalvageOst || ArSalvageLensesSession.PreferSalvageOst)
            {
                ArPresentationTarget.SetOverride(ArPresentationMode.SalvageOst);
                var salvage = ArSalvageLensesSession.Ensure();
                salvage.BeginSession(sep);
                BuildSalvageOstStage(salvage, sep);
                return;
            }

            var lenses = !Application.isEditor &&
                         (ArLensesSession.ShouldUseLensesPath() ||
                          ArPresentationTarget.IsLenses ||
                          ArLensesSession.IsXrDisplayRunning());

            if (lenses)
            {
                ArPresentationTarget.SetOverride(ArPresentationMode.LensesXr);
                var session = ArLensesSession.Ensure();
                session.BeginSession(sep);
                BuildLensesStage(session, sep);
                return;
            }

            if (ArFoundationSession.ShouldUseOnDevice())
            {
                var arf = ArFoundationSession.Ensure();
                arf.BeginSession(sep);
                BuildArFoundationStage(arf, sep);
                return;
            }

            BuildPhoneOrEditorStage();
        }

        /// <summary>
        /// Full ARCore stage: phone is a 6DOF window. Arena + disks world-lock to the floor.
        /// Passthrough is ARCameraBackground (not a webcam quad).
        /// </summary>
        void BuildArFoundationStage(ArFoundationSession session, float sepMeters)
        {
            _stage = new GameObject("ArStage_ARCore").transform;
            if (session.FloorAnchor != null)
                _stage.SetParent(session.FloorAnchor, false);
            else
                _stage.SetParent(transform, false);
            _stage.localPosition = Vector3.zero;
            _stage.localRotation = Quaternion.identity;
            _stage.localScale = Vector3.one;

            _cam = session.EyeCamera;
            if (_cam == null)
            {
                var camGo = new GameObject("ArCam_ARCore", typeof(Camera));
                camGo.transform.SetParent(_stage, false);
                _cam = camGo.GetComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
            }

            if (_target != null)
            {
                _target.enabled = false;
                var parent = _target.transform.parent;
                if (parent != null) parent.gameObject.SetActive(false);
            }

            EnsureEmptyArenaRoot();
            AddLight("HoloKey", new Vector3(0.4f, 2.0f, sepMeters * 0.3f), new Color(0.9f, 0.95f, 1f), 0.9f);

            try
            {
                _interaction = ArDuelInteractionSystem.Mount(
                    _stage, _cam, null, lifetimeHost: transform);
                if (session.Tracker != null)
                    _interaction.UseExternalTracker(session.Tracker, lifeSize: true);
                Debug.Log("[WRLDZ] ARCore interaction mounted — 6DOF floor arena.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] ARCore interaction failed: " + ex.Message);
                EnsureLegacyFallbackVisuals();
            }

            _ready = true;
            _arLive = true;
            ArStatus = "ARCORE · 6DOF · point at the floor";
            RefreshBadge();
            Debug.Log($"[WRLDZ] ArDuelSpace ARCore ready · sep={sepMeters:0.0}m");
        }

        /// <summary>
        /// Optical-see-through salvage: black clear, gyro head, 1:1 disks.
        /// Combiner glass is the real world — no webcam plane.
        /// </summary>
        void BuildSalvageOstStage(ArSalvageLensesSession session, float sepMeters)
        {
            _stage = new GameObject("ArStage_SalvageOst").transform;
            if (session.FloorAnchor != null)
                _stage.SetParent(session.FloorAnchor, false);
            else
                _stage.SetParent(transform, false);
            _stage.localPosition = Vector3.zero;
            _stage.localRotation = Quaternion.identity;
            _stage.localScale = Vector3.one;

            _cam = session.EyeCamera;
            if (_cam == null)
            {
                var camGo = new GameObject("OstEye_Fallback", typeof(Camera));
                camGo.transform.SetParent(_stage, false);
                _cam = camGo.GetComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
            }

            if (_target != null)
            {
                _target.enabled = false;
                var parent = _target.transform.parent;
                if (parent != null) parent.gameObject.SetActive(false);
            }

            if (_badge != null)
            {
                _badge.gameObject.SetActive(false);
            }

            EnsureEmptyArenaRoot();
            AddLight("HoloKey", new Vector3(0.5f, 2.2f, sepMeters * 0.3f), new Color(0.9f, 0.95f, 1f), 0.85f);

            try
            {
                _interaction = ArDuelInteractionSystem.Mount(
                    _stage, _cam, null, lifetimeHost: transform);
                if (session.Tracker != null)
                    _interaction.UseExternalTracker(session.Tracker, lifeSize: true);
                Debug.Log("[WRLDZ] Salvage OST interaction mounted — combiner holos @ 1:1 m.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Salvage OST interaction failed: " + ex.Message);
                EnsureLegacyFallbackVisuals();
            }

            _ready = true;
            _arLive = true;
            ArStatus = "salvage OST · combiner · 1:1";
            RefreshBadge();
            Debug.Log($"[WRLDZ] ArDuelSpace SALVAGE OST ready · sep={sepMeters:0.0}m");
        }

        /// <summary>1:1 life-size stage for Quest 3 / OpenXR — HMD is the eye.</summary>
        void BuildLensesStage(ArLensesSession session, float sepMeters)
        {
            _stage = new GameObject("ArStage_Lenses").transform;
            if (session.FloorAnchor != null)
                _stage.SetParent(session.FloorAnchor, false);
            else
                _stage.SetParent(transform, false);
            _stage.localPosition = Vector3.zero;
            _stage.localRotation = Quaternion.identity;
            _stage.localScale = Vector3.one;

            _cam = session.XrCamera;
            if (_cam == null)
            {
                var camGo = new GameObject("ArCam_Fallback", typeof(Camera));
                camGo.transform.SetParent(_stage, false);
                _cam = camGo.GetComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 1f);
            }

            if (_target != null)
            {
                _target.enabled = false;
                var parent = _target.transform.parent;
                if (parent != null) parent.gameObject.SetActive(false);
            }

            if (_badge != null)
            {
                _badge.gameObject.SetActive(true);
                _badge.text = "LENSES XR · life-size";
            }

            // Empty AR volume — no floor geometry; holos only when cards play
            EnsureEmptyArenaRoot();
            AddLight("HoloKey", new Vector3(0.5f, 2.2f, sepMeters * 0.3f), new Color(0.9f, 0.95f, 1f), 0.85f);

            try
            {
                _interaction = ArDuelInteractionSystem.Mount(
                    _stage, _cam, null, lifetimeHost: transform);
                Debug.Log("[WRLDZ] Lenses interaction mounted — arm disks + floor @ 1:1 m.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Lenses interaction failed: " + ex.Message);
                EnsureLegacyFallbackVisuals();
            }

            var boot = FindAnyObjectByType<ArLensesBootstrap>();
            if (boot == null)
                boot = new GameObject("ArLensesBootstrap").AddComponent<ArLensesBootstrap>();
            boot.TryBindSpatialStage(_stage,
                _interaction?.PlayerDisk != null ? _interaction.PlayerDisk.transform : null,
                _interaction?.Arena != null ? _interaction.Arena.transform : null);

            _ready = true;
            _arLive = true;
            ArStatus = "lenses XR · life-size · arm disks";
            RefreshBadge();
            Debug.Log($"[WRLDZ] ArDuelSpace LENSES ready · sep={sepMeters:0.0}m · {ArPresentationTarget.StatusLabel()}");
        }

        void BuildPhoneOrEditorStage()
        {
            // Ensure UI RawImage is on (lenses path may have disabled it earlier in session)
            if (_target != null)
            {
                _target.enabled = true;
                var parent = _target.transform.parent;
                if (parent != null) parent.gameObject.SetActive(true);
            }

            _stage = new GameObject("ArStage").transform;
            _stage.SetParent(transform, false);

            var camGo = new GameObject("ArCam", typeof(Camera));
            camGo.transform.SetParent(_stage, false);
            _cam = camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // Empty AR air (Editor sim). Phone live path swaps to near-black over passthrough.
            _cam.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            // Wide enough: your arm disk + midfield holos + opp disk
            _cam.fieldOfView = Application.isEditor ? 62f : (Application.isMobilePlatform ? 60f : 58f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 50f;
            _cam.cullingMask = (1 << Layer) | (1 << 0);
            _cam.depth = -80;
            _cam.allowMSAA = !Application.isMobilePlatform;
            _cam.transform.localPosition = new Vector3(0f, 1.65f, -2.35f);
            _cam.transform.LookAt(_stage.TransformPoint(new Vector3(0f, 0.28f, 0.15f)));
            _freeLook = ArFreeLookCamera.Attach(_cam);

            var aa = Application.isMobilePlatform ? 1 : 2;
            _rt = new RenderTexture(RtW, RtH, 16, RenderTextureFormat.ARGB32)
            {
                name = "ArDuelSpaceRT",
                antiAliasing = aa
            };
            _cam.targetTexture = _rt;
            if (_target != null)
            {
                _target.texture = _rt;
                _target.color = Color.white;
            }

            // No floor / board / props — empty volume + one key light for holograms
            EnsureEmptyArenaRoot();
            AddLight("HoloKey", new Vector3(0.6f, 2.4f, -0.8f), new Color(0.9f, 0.94f, 1f), 1.1f);

            try
            {
                var viewport = _target != null ? _target.rectTransform : null;
                _interaction = ArDuelInteractionSystem.Mount(
                    _stage, _cam, viewport, lifetimeHost: transform);
                FrameStageForEditor();
                Debug.Log("[WRLDZ] ArDuelInteractionSystem mounted — empty AR field (invisible spawns only).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ] Interaction system mount failed: " + ex.Message);
                EnsureLegacyFallbackVisuals();
            }

            _ready = true;
            ArStatus = Application.isEditor
                ? "PC EDITOR · dual disks + arena (sim)"
                : "spatial stage ready · opening camera…";
            RefreshBadge();
            Debug.Log($"[WRLDZ] ArDuelSpace stage ready — phone/Editor. mode={ArPresentationTarget.Current}");
            if (!Application.isEditor)
                StartCoroutine(OpenPhoneArRoutine());
            else
            {
                // Editor: skip phone cam coroutine — RT stage is the view
                _arLive = false;
                RefreshBadge();
#if UNITY_EDITOR
                // Re-frame after ArenaRoot LateUpdate anchors mid between wrists (Opp half)
                StartCoroutine(EditorReframeAfterArenaAnchor());
#endif
            }
        }

#if UNITY_EDITOR
        IEnumerator EditorReframeAfterArenaAnchor()
        {
            // Wait until hologram arena has placed ArenaRoot between both arms
            for (var i = 0; i < 30; i++)
            {
                yield return null;
                var arena = _interaction != null ? _interaction.Arena : null;
                if (arena != null && arena.ArenaRoot != null &&
                    (arena.JustAnchoredThisFrame || i >= 2))
                {
                    FrameStageForEditor();
                    if (i >= 2) yield break;
                }
            }

            FrameStageForEditor();
        }
#endif

        /// <summary>
        /// Player-first RT camera: worn disk + hand in the near FOV, hologram arena
        /// beyond and above — never stacked on the disk. Scene view still frames both arms.
        /// </summary>
        void FrameStageForEditor()
        {
            if (_cam == null || _stage == null) return;
            if (_freeLook != null && _freeLook.UserMoved) return;

            // Fallback matches SimulatedArmTracker ready stance (wrists outside holo volume)
            Vector3 you = _stage.TransformPoint(new Vector3(-0.22f, 0.52f, -2.52f));
            Vector3 opp = _stage.TransformPoint(new Vector3(0.22f, 0.58f, 2.52f));
            var tracker = _interaction != null
                ? _interaction.GetComponentInChildren<SimulatedArmTracker>(true)
                : null;
            if (tracker != null)
            {
                if (tracker.TryGetLeftForearm(out var p)) you = p.position;
                if (tracker.TryGetOpponentLeftForearm(out var o)) opp = o.position;
            }

            var mid = (you + opp) * 0.5f;
            mid.y = Mathf.Max(you.y, opp.y) + 0.28f;
            if (_interaction?.Arena != null && _interaction.Arena.ArenaRoot != null)
                mid = Vector3.Lerp(mid, _interaction.Arena.ArenaRoot.position, 0.35f);

            var span = Vector3.Distance(you, opp);
            var along = opp - you;
            along.y = 0f;
            if (along.sqrMagnitude < 1e-6f) along = _stage.forward;
            along.Normalize();
            var right = Vector3.Cross(Vector3.up, along).normalized;

            // Sit behind the player at eye height. Look into the standing holos
            // (not the pavement at your feet — that made look-up feel like a spring).
            var lookDist = ArPlaymatLayout.LiveStandOff +
                           ArPlaymatLayout.LiveStep * 0.55f;
            var look = you + along * lookDist + Vector3.up * 1.22f;
            var camWorld = you - along * 0.28f + Vector3.up * 1.46f + right * 0.08f;
            _cam.transform.position = camWorld;
            _cam.transform.LookAt(look);
            _cam.fieldOfView = Mathf.Clamp(48f + span * 1.15f, 48f, 62f);
            _freeLook?.CapturePose();
            Debug.Log(
                $"[WRLDZ AR] Frame player-first · span={span:0.00} · fov={_cam.fieldOfView:0} · " +
                $"you={you} look={look} cam={camWorld}");

#if UNITY_EDITOR
            // Scene view: frame You wrist + Opp wrist + full dual playmat (Opp S/T at +1.14 local Z)
            try
            {
                var arenaRoot = _interaction?.Arena != null ? _interaction.Arena.ArenaRoot : null;
                var bounds = new Bounds(mid, Vector3.one * 0.4f);
                bounds.Encapsulate(you);
                bounds.Encapsulate(opp);
                if (arenaRoot != null)
                {
                    // Explicit Opp half extents in ArenaRoot space
                    var stZ = ArPlaymatLayout.LiveSpellTrapRowFromMid + 0.35f;
                    var monW = ArPlaymatLayout.MonsterColumnPitch * 2.6f;
                    bounds.Encapsulate(arenaRoot.TransformPoint(new Vector3(monW, 0.6f, stZ)));
                    bounds.Encapsulate(arenaRoot.TransformPoint(new Vector3(-monW, 0.05f, stZ)));
                    bounds.Encapsulate(arenaRoot.TransformPoint(new Vector3(monW, 0.6f, -stZ)));
                    bounds.Encapsulate(arenaRoot.TransformPoint(new Vector3(-monW, 0.05f, -stZ)));
                    bounds.Encapsulate(arenaRoot.position + Vector3.up * 0.9f);
                }
                else
                {
                    bounds.Encapsulate(mid + _stage.forward * 1.6f);
                    bounds.Encapsulate(mid - _stage.forward * 1.6f);
                }

                bounds.Expand(1.0f);
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null)
                {
                    sv.Frame(bounds, false);
                    // Ensure Scene view draws Default + AR layers
                    sv.drawGizmos = true;
                }

                Debug.Log(
                    "[WRLDZ AR] Scene framed · dual boards: CardField (PlayerArmDiskRig + " +
                    "OppArmDiskRig) + HologramField (empty air, holos only). " +
                    "No playmat mesh. Layers: 28 + Default.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ AR] Scene frame skipped: " + ex.Message);
            }
#endif
        }

        /// <summary>Empty parent only — no floor mesh (true AR empty volume).</summary>
        void EnsureEmptyArenaRoot()
        {
            if (_arena != null) return;
            _arena = new GameObject("EmptyDuelSpace").transform;
            _arena.SetParent(_stage, false);
            _arena.localPosition = Vector3.zero;
            SetLayer(_arena.gameObject);
        }

        /// <summary>Bind rules engine so drag-snap + arena sync drive from official state.</summary>
        public void BindEngine(DuelEngine engine, CardDatabase db)
        {
            _boundEngine = engine;
            _boundDb = db;
            _interaction?.BindEngine(engine, db);
            // Re-frame after disks bind so midfield holos + both arms stay in AR view
            FrameStageForEditor();
        }

        /// <summary>
        /// S23 / phone: permission → rear camera → passthrough plane behind holos.
        /// Editor: optional webcam if PreferEditorCamera; else pure spatial sim.
        /// </summary>
        IEnumerator OpenPhoneArRoutine()
        {
            _arLive = false;

            // Zone Mode [DIGITAL]: same spatial stage, no camera (F2P decline path)
            var match = AppSession.Instance != null ? AppSession.Instance.PendingArMatch : null;
            if (match != null && match.PreferDigital)
            {
                ArStatus = "digital duel · spatial stage (no camera)";
                RefreshBadge();
                Debug.Log("[WRLDZ] PreferDigital — skip phone AR camera");
                yield break;
            }

            // Editor default = sim (layout/rules iterate on PC). Webcam only if opted in.
            if (Application.isEditor && !ArPresentationTarget.PreferEditorCamera)
            {
                ArStatus = "editor sim (enable PreferEditorCamera for webcam)";
                RefreshBadge();
                yield break;
            }

            _camSession = new ArPhoneCamera.Session();
            // S23 Ultra: 1280x720@30 is enough for passthrough plane and cooler thermals
            yield return ArPhoneCamera.Open(_camSession, 1280, 720, 30);

            if (_camSession == null || !_camSession.Live || _camSession.Texture == null)
            {
                ArStatus = _camSession?.Status ?? "camera unavailable";
                RefreshBadge();
                Debug.Log("[WRLDZ] ArDuelSpace: spatial sim only — " + ArStatus);
                yield break;
            }

            _webCam = _camSession.Texture;

            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "Passthrough";
            // Ride the eye camera so free-look keeps the feed full-screen (phone AR sim)
            var ptParent = _cam != null ? _cam.transform : _stage;
            plane.transform.SetParent(ptParent, false);
            plane.transform.localPosition = new Vector3(0f, 0f, 4.0f);
            plane.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _ptBaseW = 7.4f;
            _ptBaseH = 4.6f;
            plane.transform.localScale = new Vector3(_ptBaseW, _ptBaseH, 1f);
            Object.Destroy(plane.GetComponent<Collider>());
            SetLayer(plane);

            _passthroughMat = new Material(
                Shader.Find("Unlit/Texture")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default"));
            _passthroughMat.mainTexture = _webCam;
            if (_passthroughMat.HasProperty("_BaseMap"))
                _passthroughMat.SetTexture("_BaseMap", _webCam);
            plane.GetComponent<MeshRenderer>().sharedMaterial = _passthroughMat;
            _passthroughPlane = plane.transform;

            ArPhoneCamera.ApplyOrientationToPlane(_passthroughPlane, _webCam, _ptBaseW, _ptBaseH);

            // When live, camera clear can be pure black so plane dominates "room"
            if (_cam != null)
                _cam.backgroundColor = new Color(0.01f, 0.02f, 0.03f, 1f);

            _arLive = true;
            ArStatus = _camSession.Status;
            RefreshBadge();
            Debug.Log("[WRLDZ] ArDuelSpace PHONE AR LIVE — " + ArStatus);
        }

        /// <summary>
        /// Only used if ArDuelInteractionSystem fails to mount.
        /// Empty parents only — still no floor geometry.
        /// </summary>
        void EnsureLegacyFallbackVisuals()
        {
            EnsureEmptyArenaRoot();
            if (_playerHolos == null)
            {
                _playerHolos = new GameObject("PlayerHolos_Legacy").transform;
                _playerHolos.SetParent(_arena, false);
                _playerHolos.localPosition = new Vector3(0f, 0f, -0.55f);
            }

            if (_oppHolos == null)
            {
                _oppHolos = new GameObject("OppHolos_Legacy").transform;
                _oppHolos.SetParent(_arena, false);
                _oppHolos.localPosition = new Vector3(0f, 0f, 0.65f);
            }
        }

        void AddLight(string name, Vector3 pos, Color c, float intensity)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(_stage, false);
            go.transform.localPosition = pos;
            var L = go.GetComponent<Light>();
            L.type = LightType.Point;
            L.range = 14f;
            L.color = c;
            // Soft fill only — holos are unlit; this feeds ambient/exposure estimate + any Lit fallbacks
            L.intensity = intensity * 0.55f;
            L.cullingMask = 1 << Layer;
        }

        void Update()
        {
            if (!_ready) return;

            // Variable real-world lighting: keep anime holos readable outdoors/indoors
            if ((Time.frameCount & 3) == 0)
                ArAnimePresentation.TickExposure(_cam);

            // Legacy holo billboards only when interaction failed
            if (_interaction == null && (Time.frameCount & 1) == 0)
            {
                Billboard(_playerHolos);
                Billboard(_oppHolos);
            }

            // Keep passthrough orientation correct as Android reports rotation
            if (_arLive && _passthroughPlane != null && _webCam != null && _webCam.isPlaying
                && Time.frameCount % 15 == 0)
            {
                ArPhoneCamera.ApplyOrientationToPlane(_passthroughPlane, _webCam, _ptBaseW, _ptBaseH);
            }

            if (_impactFlash > 0f)
            {
                _impactFlash = Mathf.Max(0f, _impactFlash - Time.deltaTime);
                if (_cam != null)
                    _cam.backgroundColor = Color.Lerp(
                        new Color(0.06f, 0.09f, 0.14f, 1f),
                        new Color(0.25f, 0.05f, 0.05f, 1f),
                        _impactFlash);
            }
        }

        void Billboard(Transform row)
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
            _boundEngine = engine;
            _boundDb = db;

            // Primary path: interaction system (arm disk + hand volume + shared arena)
            if (_interaction != null)
            {
                // Bind only when engine instance changes; always SyncNow for board updates
                if (!ReferenceEquals(_interactionEngine, engine))
                {
                    _interaction.BindEngine(engine, db);
                    _interactionEngine = engine;
                }
                else
                    _interaction.SyncNow();
                return;
            }

            // Fallback: legacy midfield holos only (interaction mount failed)
            EnsureLegacyFallbackVisuals();
            SyncHolos(engine.Player, db, _playerHolos, _pH, true);
            SyncHolos(engine.Opponent, db, _oppHolos, _oH, false);
        }

        void SyncHolos(DuelistState who, CardDatabase db, Transform row,
            Dictionary<int, GameObject> map, bool playerSide)
        {
            var live = new HashSet<int>();
            for (var i = 0; i < who.MonsterZones.Length; i++)
            {
                var m = who.MonsterZones[i].Occupant;
                if (m == null || !m.FaceUp) continue; // only face-up → arena hologram
                live.Add(i);
                if (!map.TryGetValue(i, out var go) || go == null)
                {
                    go = SpawnHolo(m, db, row, i, playerSide);
                    map[i] = go;
                }
                else
                {
                    var tag = go.GetComponent<HoloTag>();
                    if (tag == null || tag.InstanceId != m.InstanceId)
                    {
                        Object.Destroy(go);
                        go = SpawnHolo(m, db, row, i, playerSide);
                        map[i] = go;
                    }
                }
            }

            var dead = new List<int>();
            foreach (var kv in map)
                if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                if (map[k] != null) Object.Destroy(map[k]);
                map.Remove(k);
            }
        }

        GameObject SpawnHolo(CardInstance card, CardDatabase db, Transform row, int zone, bool playerSide)
        {
            var root = new GameObject($"H{zone}_{card.Name}");
            root.transform.SetParent(row, false);
            root.transform.localPosition = new Vector3((zone - 2) * 0.48f, 0f, 0f);
            SetLayer(root);

            var tag = root.AddComponent<HoloTag>();
            tag.InstanceId = card.InstanceId;

            var mesh = CardModelCatalog.GetMesh(card.CardId);
            var isBb = CardModelCatalog.IsBillboardFallback(mesh);
            var body = new GameObject(isBb ? "Billboard" : "Model", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(root.transform, false);
            body.GetComponent<MeshFilter>().sharedMesh = mesh;
            var art = db.GetArt(card.CardId);
            var tint = playerSide ? new Color(0.5f, 0.95f, 1f) : new Color(1f, 0.55f, 0.7f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                CardModelCatalog.MakeArtMaterial(art, tint);
            if (isBb) body.transform.localScale = new Vector3(1.3f, 1.55f, 1.3f);
            SetLayer(body);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(0.4f, 0.01f, 0.4f);
            Object.Destroy(ring.GetComponent<Collider>());
            SetLayer(ring);
            ring.GetComponent<MeshRenderer>().sharedMaterial = ArFieldMaterials.Get(
                playerSide ? new Color(0.2f, 0.9f, 1f, 0.55f) : new Color(1f, 0.35f, 0.55f, 0.55f));

            // Summon pop
            root.transform.localScale = Vector3.one * 0.01f;
            return root;
        }

        public void PlayFx(bool playerSide, DiskFxEvent evt, float duration = -1f)
        {
            if (_interaction == null) return;
            if (playerSide) _interaction.PlayerDisk?.PlayFx(evt, duration);
            else _interaction.OppDisk?.PlayFx(evt, duration);
        }

        /// <summary>Anime deploy both arm disks (also triggered by BindEngine).</summary>
        public void DeployDisks() => _interaction?.DeployDisksForDuel();

        /// <summary>Fold both arm disks to compact wrist form.</summary>
        public void RetractDisks() => _interaction?.RetractDisks();

        public void SetAttackCharge(bool playerSide, float t01)
        {
            if (_interaction != null)
            {
                var disk = playerSide ? _interaction.PlayerDisk : _interaction.OppDisk;
                disk?.PlayFx(DiskFxEvent.AttackCharge);
                disk?.Fx?.SetCharge01(t01);
            }

            if (!playerSide) _impactFlash = Mathf.Max(_impactFlash, 0.4f);
        }

        public void PulseAttack()
        {
            _impactFlash = 1f;
            _interaction?.Anime?.PlayImpact();
        }

        void LateUpdate()
        {
            // Legacy fallback scale-in only
            if (_interaction != null) return;
            ScaleInRow(_playerHolos);
            ScaleInRow(_oppHolos);
        }

        static void ScaleInRow(Transform row)
        {
            if (row == null) return;
            foreach (Transform t in row)
            {
                if (t.localScale.x < 0.99f)
                    t.localScale = Vector3.Lerp(t.localScale, Vector3.one, Time.deltaTime * 6f);
            }
        }

        void OnDestroy()
        {
            if (ArPresentationTarget.IsSalvageOst)
                ArSalvageLensesSession.Instance?.Shutdown();
            if (ArFoundationSession.Instance != null)
                ArFoundationSession.Instance.Shutdown();

            if (_camSession != null)
            {
                ArPhoneCamera.Stop(_camSession);
                _camSession = null;
                _webCam = null;
            }
            else if (_webCam != null)
            {
                if (_webCam.isPlaying) _webCam.Stop();
                Object.Destroy(_webCam);
                _webCam = null;
            }

            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Object.Destroy(_rt);
            }
        }

        static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject);
        }

        class HoloTag : MonoBehaviour
        {
            public int InstanceId;
        }
    }
}
