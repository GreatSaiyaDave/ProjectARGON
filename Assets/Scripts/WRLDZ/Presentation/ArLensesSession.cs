using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using WRLDZ.Core;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Quest 3 / OpenXR lenses session for life-size spatial duels.
    /// Builds XR Origin (camera + controllers), floor anchor, and 1:1 world scale.
    /// Phone AR is a fallback — this is the product path for arm-anchored disks.
    /// </summary>
    public class ArLensesSession : MonoBehaviour
    {
        public static ArLensesSession Instance { get; private set; }

        public Transform Origin { get; private set; }
        public Transform CameraOffset { get; private set; }
        public Camera XrCamera { get; private set; }
        public Transform LeftController { get; private set; }
        public Transform RightController { get; private set; }
        public Transform Head { get; private set; }
        /// <summary>World floor under the player — arena midfield parents here (1:1 meters).</summary>
        public Transform FloorAnchor { get; private set; }
        /// <summary>World midfield point between duelists (life-size holograms).</summary>
        public Transform ArenaAnchor { get; private set; }

        public bool SessionRunning { get; private set; }
        public bool TrackingReady { get; private set; }

        /// <summary>Real-world meters between player and opponent disks (1:1).</summary>
        public float SeparationMeters { get; private set; } = ArDuelMatchConfig.DefaultStandM;

        XrWorldArmTracker _tracker;
        bool _built;
        bool _initStarted;

        public XrWorldArmTracker Tracker => _tracker;

        /// <summary>Ensure a lenses session exists (creates if needed). No-op on pure Editor without XR.</summary>
        public static ArLensesSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ArLensesSession");
            DontDestroyOnLoad(go);
            return go.AddComponent<ArLensesSession>();
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
        }

        /// <summary>
        /// Start OpenXR subsystems (if available) and build origin hierarchy.
        /// Safe to call from duel load even when XR is not installed yet.
        /// </summary>
        public void BeginSession(float separationMeters = -1f)
        {
            if (separationMeters > 0f)
                SeparationMeters = Mathf.Clamp(separationMeters,
                    ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);

            if (!_built)
                BuildOriginHierarchy();

            if (!_initStarted)
            {
                _initStarted = true;
                StartCoroutine(StartXrRoutine());
            }
            else
            {
                ApplySeparation(SeparationMeters);
                TrackingReady = true;
            }
        }

        public void ApplySeparation(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters,
                ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            _tracker?.ApplyPlayerSeparationMeters(SeparationMeters);
            PlaceArenaAnchor();
            Debug.Log($"[WRLDZ Lenses] Separation {SeparationMeters:0.0}m · 1:1 world");
        }

        void BuildOriginHierarchy()
        {
            if (_built) return;
            _built = true;

            // XR Origin root (session space)
            Origin = new GameObject("XR Origin").transform;
            Origin.SetParent(transform, false);
            Origin.localPosition = Vector3.zero;
            Origin.localRotation = Quaternion.identity;
            Origin.localScale = Vector3.one; // 1:1 meters

            CameraOffset = new GameObject("Camera Offset").transform;
            CameraOffset.SetParent(Origin, false);

            // HMD camera — driven by CenterEye each frame (tracking space = Origin)
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            // Parent to Origin so device poses apply as local tracking-space
            camGo.transform.SetParent(Origin, false);
            Head = camGo.transform;
            XrCamera = camGo.GetComponent<Camera>();
            XrCamera.clearFlags = CameraClearFlags.SolidColor;
            // Near-black: Quest passthrough (with Meta XR SDK) can composite under;
            // without SDK this is still a valid spatial view of life-size holos.
            XrCamera.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 0f);
            XrCamera.nearClipPlane = 0.05f;
            XrCamera.farClipPlane = 80f;
            XrCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            XrCamera.allowHDR = false;

            LeftController = new GameObject("Left Controller").transform;
            LeftController.SetParent(Origin, false);
            RightController = new GameObject("Right Controller").transform;
            RightController.SetParent(Origin, false);

            // Floor under feet — arena life-size field
            FloorAnchor = new GameObject("FloorAnchor").transform;
            FloorAnchor.SetParent(Origin, false);
            FloorAnchor.localPosition = Vector3.zero;

            // Visual floor grid (subtle) so scale reads in MR
            var floorVis = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floorVis.name = "FloorGrid";
            floorVis.transform.SetParent(FloorAnchor, false);
            floorVis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floorVis.transform.localPosition = new Vector3(0f, 0.005f, 1.5f);
            floorVis.transform.localScale = new Vector3(6f, 6f, 1f);
            Object.Destroy(floorVis.GetComponent<Collider>());
            var fmr = floorVis.GetComponent<MeshRenderer>();
            fmr.sharedMaterial = MakeFloorMat();

            ArenaAnchor = new GameObject("ArenaAnchor").transform;
            ArenaAnchor.SetParent(FloorAnchor, false);
            PlaceArenaAnchor();

            // 1:1 arm tracker (controllers / head)
            _tracker = gameObject.AddComponent<XrWorldArmTracker>();
            _tracker.Bind(LeftController, RightController, Head, FloorAnchor);
            _tracker.ApplyPlayerSeparationMeters(SeparationMeters);

            Debug.Log("[WRLDZ Lenses] XR Origin built · 1:1 scale · floor + controllers");
        }

        void PlaceArenaAnchor()
        {
            if (ArenaAnchor == null) return;
            // Midfield between player (near origin) and opponent (forward by separation)
            var midZ = SeparationMeters * 0.5f;
            ArenaAnchor.localPosition = new Vector3(0f, 0.02f, midZ);
            ArenaAnchor.localRotation = Quaternion.identity;
        }

        IEnumerator StartXrRoutine()
        {
            // Prefer XR Management OpenXR loader when present (no yield inside try/catch)
            var started = false;
            XRManagerSettings mgr = null;
            try
            {
                mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WRLDZ Lenses] XR settings: " + ex.Message);
            }

            if (mgr != null)
            {
                if (mgr.activeLoader == null)
                    yield return mgr.InitializeLoader();

                if (mgr.activeLoader != null)
                {
                    try
                    {
                        mgr.StartSubsystems();
                        started = true;
                        SessionRunning = true;
                        Debug.Log("[WRLDZ Lenses] XR subsystems started · " + mgr.activeLoader.name);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[WRLDZ Lenses] StartSubsystems: " + ex.Message);
                    }
                }
                else
                    Debug.LogWarning("[WRLDZ Lenses] No XR loader active — enable OpenXR for Android.");
            }
            else
                Debug.LogWarning("[WRLDZ Lenses] XRGeneralSettings missing — Project Settings → XR Plug-in Management.");

            for (var i = 0; i < 10; i++)
                yield return null;

            TrackingReady = true;
            ApplySeparation(SeparationMeters);

            if (started || IsXrDisplayRunning())
            {
                ArPresentationTarget.SetOverride(ArPresentationMode.LensesXr);
                SessionRunning = true;
            }

            Debug.Log($"[WRLDZ Lenses] Session ready · running={SessionRunning} · {ArPresentationTarget.StatusLabel()}");
        }

        void Update()
        {
            if (!_built) return;
            DriveNode(XRNode.CenterEye, Head, true);
            DriveNode(XRNode.LeftHand, LeftController, false);
            DriveNode(XRNode.RightHand, RightController, false);

            // Floor stays under player feet (Y of origin / head projected)
            if (FloorAnchor != null && Head != null)
            {
                var o = Origin.position;
                // Keep floor at origin height (tracking origin floor mode) — only update XZ if needed
                FloorAnchor.position = new Vector3(o.x, o.y, o.z);
            }
        }

        static void DriveNode(XRNode node, Transform target, bool isHead)
        {
            if (target == null) return;
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesAtXRNode(node, devices);
            if (devices.Count == 0)
                return;

            var d = devices[0];
            if (d.TryGetFeatureValue(CommonUsages.devicePosition, out var p) &&
                d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r))
            {
                // Poses are typically in tracking/session space — parent is Origin
                target.localPosition = p;
                target.localRotation = r;
            }
        }

        public static bool IsXrDisplayRunning()
        {
            try
            {
                if (XRSettings.enabled &&
                    !string.IsNullOrEmpty(XRSettings.loadedDeviceName) &&
                    XRSettings.loadedDeviceName != "None")
                    return true;
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays)
                    if (d != null && d.running) return true;
            }
            catch
            {
                // XR module edge cases
            }

            return false;
        }

        /// <summary>True when we should prefer life-size lenses path for the duel stage.</summary>
        public static bool ShouldUseLensesPath()
        {
            // Editor Play Mode: never hijack the phone/sim RT stage
            if (Application.isEditor)
                return ArPresentationTarget.IsLenses; // only if explicitly overridden

            if (ArPresentationTarget.IsLenses) return true;
            if (Instance != null && Instance.SessionRunning) return true;
            return IsXrDisplayRunning();
        }

        static Material MakeFloorMat()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "LensesFloor" };
            var c = new Color(0.15f, 0.55f, 0.75f, 0.12f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        public void Shutdown()
        {
            try
            {
                var general = XRGeneralSettings.Instance;
                if (general?.Manager != null && general.Manager.activeLoader != null)
                {
                    general.Manager.StopSubsystems();
                    general.Manager.DeinitializeLoader();
                }
            }
            catch
            {
                // ignore
            }

            SessionRunning = false;
            TrackingReady = false;
            ArPresentationTarget.SetOverride(null);
        }
    }
}
