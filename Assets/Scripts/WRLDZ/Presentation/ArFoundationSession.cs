using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using WRLDZ.Core;
using WRLDZ.Presentation.ArInteraction;
#if WRLDZ_HAS_ARFOUNDATION
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.XR;
#endif
#endif

namespace WRLDZ.Presentation
{
#if !WRLDZ_HAS_ARFOUNDATION
    /// <summary>Stub until AR Foundation packages resolve. See FULL_AR.md.</summary>
    public class ArFoundationSession : MonoBehaviour
    {
        public static ArFoundationSession Instance { get; private set; }
        public Transform Origin => null;
        public Camera EyeCamera => null;
        public Transform Head => null;
        public Transform FloorAnchor => null;
        public Transform ArenaAnchor => null;
        public ArFoundationArmTracker Tracker => null;
        public bool SessionRunning => false;
        public bool FloorLocked => false;
        public bool TrackingReady => false;
        public float SeparationMeters { get; private set; }
        public string Status => "AR Foundation package not loaded";
        public static bool IsBuilt => false;
        public static bool ShouldUseOnDevice() => false;
        public static ArFoundationSession Ensure() => null;
        public void BeginSession(float separationMeters = -1f) { }
        public void ApplySeparation(float meters) { }
        public void Shutdown() { }
    }
#else

    /// <summary>
    /// Full phone AR: ARCore 6DOF + floor planes + passthrough.
    /// Same 1:1 stage graph as OpenXR lenses. Not a HUD attachment.
    /// Editor never starts this — PC stays EditorSim.
    /// </summary>
    public class ArFoundationSession : MonoBehaviour
    {
        public static ArFoundationSession Instance { get; private set; }

        public Transform Origin { get; private set; }
        public Camera EyeCamera { get; private set; }
        public Transform Head { get; private set; }
        public Transform FloorAnchor { get; private set; }
        public Transform ArenaAnchor { get; private set; }
        public ArFoundationArmTracker Tracker { get; private set; }

        public bool SessionRunning { get; private set; }
        public bool FloorLocked { get; private set; }
        public bool TrackingReady { get; private set; }

        public float SeparationMeters { get; private set; } = ArDuelMatchConfig.DefaultStandM;

        ARSession _arSession;
        ARPlaneManager _planes;
        XROrigin _xrOrigin;
        bool _built;
        bool _startStarted;
        string _status = "idle";

        public string Status => _status;

        public static bool IsBuilt => Instance != null && Instance._built;

        public static bool ShouldUseOnDevice()
        {
            if (Application.isEditor) return false;
            if (!Application.isMobilePlatform) return false;
            var match = AppSession.Instance != null ? AppSession.Instance.PendingArMatch : null;
            if (match != null && match.PreferDigital) return false;
            if (ArPresentationTarget.IsSalvageOst || ArSalvageLensesSession.PreferSalvageOst)
                return false;
            if (ArLensesSession.IsXrDisplayRunning()) return false;
            return true;
        }

        public static ArFoundationSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ArFoundationSession");
            DontDestroyOnLoad(go);
            return go.AddComponent<ArFoundationSession>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_planes != null)
                _planes.planesChanged -= OnPlanesChanged;
        }

        public void BeginSession(float separationMeters = -1f)
        {
            if (separationMeters > 0f)
                SeparationMeters = Mathf.Clamp(separationMeters,
                    ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);

            if (!_built)
                BuildHierarchy();

            ApplySeparation(SeparationMeters);

            if (!_startStarted)
            {
                _startStarted = true;
                StartCoroutine(StartArRoutine());
            }
        }

        public void ApplySeparation(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters,
                ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            Tracker?.ApplyPlayerSeparationMeters(SeparationMeters);
            PlaceArenaAnchor();
        }

        void BuildHierarchy()
        {
            if (_built) return;
            _built = true;

            var sessionGo = new GameObject("AR Session");
            sessionGo.transform.SetParent(transform, false);
            _arSession = sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();

            var originGo = new GameObject("XR Origin");
            originGo.transform.SetParent(transform, false);
            Origin = originGo.transform;
            _xrOrigin = originGo.AddComponent<XROrigin>();
            _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(Origin, false);

            var camGo = new GameObject("AR Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset, false);
            Head = camGo.transform;
            EyeCamera = camGo.GetComponent<Camera>();
            EyeCamera.clearFlags = CameraClearFlags.SolidColor;
            EyeCamera.backgroundColor = Color.black;
            EyeCamera.nearClipPlane = 0.05f;
            EyeCamera.farClipPlane = 40f;
            EyeCamera.depth = -90;
            EyeCamera.allowHDR = false;
            EyeCamera.allowMSAA = false;
            EyeCamera.cullingMask = ~0;
            EyeCamera.stereoTargetEye = StereoTargetEyeMask.None;

            var camMgr = camGo.AddComponent<ARCameraManager>();
            camMgr.requestedFacingDirection = CameraFacingDirection.World;
            camGo.AddComponent<ARCameraBackground>();
#if ENABLE_INPUT_SYSTEM
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
#endif

            _xrOrigin.Camera = EyeCamera;
            _xrOrigin.Origin = originGo;
            _xrOrigin.CameraFloorOffsetObject = offset.gameObject;

            _planes = originGo.AddComponent<ARPlaneManager>();
            _planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            _planes.planesChanged += OnPlanesChanged;
            originGo.AddComponent<ARRaycastManager>();
            originGo.AddComponent<ARAnchorManager>();

            FloorAnchor = new GameObject("FloorAnchor").transform;
            FloorAnchor.SetParent(Origin, false);

            ArenaAnchor = new GameObject("ArenaAnchor").transform;
            ArenaAnchor.SetParent(FloorAnchor, false);

            Tracker = gameObject.AddComponent<ArFoundationArmTracker>();
            Tracker.Bind(Head, FloorAnchor, () => FloorLocked);
            Tracker.ApplyPlayerSeparationMeters(SeparationMeters);
            PlaceArenaAnchor();

            _status = "hierarchy";
            Debug.Log("[WRLDZ ARCore] XR Origin + AR Session built (6DOF · horizontal planes)");
        }

        void PlaceArenaAnchor()
        {
            if (ArenaAnchor == null) return;
            ArenaAnchor.localPosition = new Vector3(0f, 0.02f, SeparationMeters * 0.5f);
            ArenaAnchor.localRotation = Quaternion.identity;
        }

        IEnumerator StartArRoutine()
        {
            _status = "checking ARCore";
            yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                _status = "installing ARCore";
                yield return ARSession.Install();
            }

            if (ARSession.state == ARSessionState.Unsupported ||
                ARSession.state == ARSessionState.NeedsInstall)
            {
                _status = "ARCore unavailable · " + ARSession.state;
                Debug.LogWarning("[WRLDZ ARCore] " + _status);
                yield break;
            }

            TryStartXrLoader();

            if (_arSession != null)
                _arSession.enabled = true;

            for (var i = 0; i < 90; i++)
            {
                if (ARSession.state == ARSessionState.SessionTracking)
                    break;
                yield return null;
            }

            TrackingReady = ARSession.state == ARSessionState.SessionTracking
                            || ARSession.state == ARSessionState.Ready;
            SessionRunning = TrackingReady || ARSession.state >= ARSessionState.Ready;
            _status = SessionRunning
                ? (FloorLocked ? "ARCORE · floor locked" : "ARCORE · point at the floor")
                : "ARCore not tracking · " + ARSession.state;

            Debug.Log($"[WRLDZ ARCore] Start · state={ARSession.state} · tracking={TrackingReady} · {_status}");
        }

        static void TryStartXrLoader()
        {
            try
            {
                var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                if (mgr == null) return;
                if (mgr.activeLoader == null)
                    mgr.InitializeLoaderSync();
                if (mgr.activeLoader != null && !mgr.isInitializationComplete)
                    mgr.StartSubsystems();
                else if (mgr.activeLoader != null)
                    mgr.StartSubsystems();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ ARCore] XR loader: " + ex.Message);
            }
        }

        void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            ARPlane best = null;
            var bestArea = 0f;
            foreach (var p in _planes.trackables)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (p.alignment != PlaneAlignment.HorizontalUp &&
                    p.alignment != PlaneAlignment.HorizontalDown)
                    continue;
                var area = p.size.x * p.size.y;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = p;
                }
            }

            if (best == null || bestArea < 0.25f) return;

            var yaw = Head != null
                ? Vector3.ProjectOnPlane(Head.forward, Vector3.up)
                : Vector3.forward;
            if (yaw.sqrMagnitude < 0.01f) yaw = Vector3.forward;
            else yaw.Normalize();

            FloorAnchor.position = best.transform.position;
            FloorAnchor.rotation = Quaternion.LookRotation(yaw, Vector3.up);
            PlaceArenaAnchor();
            Tracker?.NotifyFloorLocked();

            if (!FloorLocked)
            {
                FloorLocked = true;
                _status = $"ARCORE · floor {bestArea:0.0}m²";
                Debug.Log($"[WRLDZ ARCore] Floor locked · area={bestArea:0.00}m² · {best.trackableId}");
            }
        }

        void LateUpdate()
        {
            if (!_built || Head == null) return;
            // Backup pose if TrackedPoseDriver has no device yet
            if (Head.localPosition.sqrMagnitude < 1e-8f)
                DriveCenterEye();
        }

        static void DriveCenterEye()
        {
            if (Instance == null || Instance.Head == null) return;
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, devices);
            if (devices.Count == 0)
                InputDevices.GetDevicesAtXRNode(XRNode.Head, devices);
            if (devices.Count == 0) return;
            var d = devices[0];
            if (d.TryGetFeatureValue(CommonUsages.devicePosition, out var p) &&
                d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r))
            {
                Instance.Head.localPosition = p;
                Instance.Head.localRotation = r;
            }
        }

        public void Shutdown()
        {
            SessionRunning = false;
            TrackingReady = false;
            FloorLocked = false;
            try
            {
                if (_arSession != null)
                    _arSession.enabled = false;
            }
            catch { /* ignore */ }
        }
    }
#endif
}
