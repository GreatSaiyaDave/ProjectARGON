using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Optical-see-through salvage lenses: one eye, black clear, gyro head, 1:1 stage.
    /// Combiner glass is the real world — no webcam passthrough.
    /// The OLED only lights pixels that should appear as holos.
    /// </summary>
    public class ArSalvageLensesSession : MonoBehaviour
    {
        public const string PrefEnabled = "WRLDZ_SalvageOst";
        public const string PrefWindow = "WRLDZ_SalvageWindow";

        public static ArSalvageLensesSession Instance { get; private set; }

        public Transform Origin { get; private set; }
        public Transform Head { get; private set; }
        public Camera EyeCamera { get; private set; }
        public Transform FloorAnchor { get; private set; }
        public Transform ArenaAnchor { get; private set; }
        public SalvageArmTracker Tracker { get; private set; }
        public SalvageHeadTracker HeadTracker { get; private set; }
        public bool SessionRunning { get; private set; }

        public float SeparationMeters { get; private set; } = ArDuelMatchConfig.DefaultStandM;

        /// <summary>Normalized screen rect the combiner actually sees (x,y,w,h).</summary>
        public Rect OpticalWindow { get; private set; } = new(0.22f, 0.22f, 0.56f, 0.56f);

        Camera _clearCam;
        bool _built;

        public static bool PreferSalvageOst
        {
            get => PlayerPrefs.GetInt(PrefEnabled, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static ArSalvageLensesSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ArSalvageLensesSession");
            DontDestroyOnLoad(go);
            return go.AddComponent<ArSalvageLensesSession>();
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
            LoadWindow();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            WrldzLab.ApplyPortraitLock();
        }

        public void BeginSession(float separationMeters = -1f)
        {
            if (separationMeters > 0f)
                SeparationMeters = Mathf.Clamp(separationMeters,
                    ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);

            if (!_built)
                BuildOrigin();

            WrldzLab.ApplyWearableLandscape();
            ArPresentationTarget.SetOverride(ArPresentationMode.SalvageOst);
            ApplySeparation(SeparationMeters);
            ApplyOpticalWindow();
            SessionRunning = true;
            Debug.Log(
                $"[WRLDZ Salvage] OST session · sep={SeparationMeters:0.0}m · " +
                $"window={OpticalWindow} · {ArPresentationTarget.StatusLabel()}");
        }

        public void ApplySeparation(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters,
                ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            Tracker?.ApplyPlayerSeparationMeters(SeparationMeters);
            if (ArenaAnchor != null)
                ArenaAnchor.localPosition = new Vector3(0f, 0.02f, SeparationMeters * 0.5f);
        }

        public void Shutdown()
        {
            SessionRunning = false;
            ArPresentationTarget.SetOverride(null);
            PreferSalvageOst = PreferSalvageOst; // keep pref; just drop override
            WrldzLab.ApplyPortraitLock();
        }

        void BuildOrigin()
        {
            _built = true;

            Origin = new GameObject("Salvage Origin").transform;
            Origin.SetParent(transform, false);

            // Full-screen black so pixels outside the combiner stay "transparent"
            var clearGo = new GameObject("OstClear", typeof(Camera));
            clearGo.transform.SetParent(Origin, false);
            _clearCam = clearGo.GetComponent<Camera>();
            _clearCam.clearFlags = CameraClearFlags.SolidColor;
            _clearCam.backgroundColor = Color.black;
            _clearCam.cullingMask = 0;
            _clearCam.depth = -100;
            _clearCam.allowHDR = false;
            _clearCam.allowMSAA = false;

            var camGo = new GameObject("OstEye", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(Origin, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Head = camGo.transform;
            EyeCamera = camGo.GetComponent<Camera>();
            EyeCamera.clearFlags = CameraClearFlags.SolidColor;
            EyeCamera.backgroundColor = Color.black;
            EyeCamera.nearClipPlane = 0.05f;
            EyeCamera.farClipPlane = 80f;
            EyeCamera.fieldOfView = 40f; // combiner FOV is narrow; don't waste pixels
            EyeCamera.depth = -99;
            EyeCamera.allowHDR = false;
            EyeCamera.allowMSAA = false;
            EyeCamera.stereoTargetEye = StereoTargetEyeMask.None;

            FloorAnchor = new GameObject("FloorAnchor").transform;
            FloorAnchor.SetParent(Origin, false);
            FloorAnchor.localPosition = Vector3.zero;

            ArenaAnchor = new GameObject("ArenaAnchor").transform;
            ArenaAnchor.SetParent(FloorAnchor, false);
            ArenaAnchor.localPosition = new Vector3(0f, 0.02f, SeparationMeters * 0.5f);

            HeadTracker = gameObject.AddComponent<SalvageHeadTracker>();
            HeadTracker.Bind(Head);

            Tracker = gameObject.AddComponent<SalvageArmTracker>();
            Tracker.Bind(Head, FloorAnchor);
            Tracker.ApplyPlayerSeparationMeters(SeparationMeters);

            BuildGlanceHud();
        }

        void BuildGlanceHud()
        {
            var hud = new GameObject("OstGlance", typeof(Canvas));
            hud.transform.SetParent(Head, false);
            hud.transform.localPosition = new Vector3(0f, -0.12f, 1.35f);
            hud.transform.localRotation = Quaternion.identity;
            hud.transform.localScale = Vector3.one * 0.001f;
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = hud.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(640f, 90f);
            var textGo = new GameObject("Hint", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textGo.transform.SetParent(hud.transform, false);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var t = textGo.GetComponent<UnityEngine.UI.Text>();
            t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = 28;
            t.color = new Color(0.95f, 0.9f, 0.55f, 0.9f);
            t.text = "SALVAGE OST  ·  Vol− recenter  ·  [ ] window";
            t.raycastTarget = false;
            if (t.font == null)
                t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Update()
        {
            if (!_built) return;
            NudgeOpticalWindow();
            if (FloorAnchor != null && Origin != null)
                FloorAnchor.position = new Vector3(Origin.position.x, Origin.position.y, Origin.position.z);
        }

        void NudgeOpticalWindow()
        {
            var r = OpticalWindow;
            var step = 0.01f;
            if (WrldzInput.KeyHeld(KeyCode.LeftShift)) step = 0.004f;

            if (WrldzInput.KeyHeld(KeyCode.LeftArrow)) r.x -= step * Time.unscaledDeltaTime * 8f;
            if (WrldzInput.KeyHeld(KeyCode.RightArrow)) r.x += step * Time.unscaledDeltaTime * 8f;
            if (WrldzInput.KeyHeld(KeyCode.UpArrow)) r.y += step * Time.unscaledDeltaTime * 8f;
            if (WrldzInput.KeyHeld(KeyCode.DownArrow)) r.y -= step * Time.unscaledDeltaTime * 8f;
            if (WrldzInput.KeyHeld(KeyCode.LeftBracket))
            {
                r.width -= step * Time.unscaledDeltaTime * 8f;
                r.height -= step * Time.unscaledDeltaTime * 8f;
            }

            if (WrldzInput.KeyHeld(KeyCode.RightBracket))
            {
                r.width += step * Time.unscaledDeltaTime * 8f;
                r.height += step * Time.unscaledDeltaTime * 8f;
            }

            r.width = Mathf.Clamp(r.width, 0.18f, 1f);
            r.height = Mathf.Clamp(r.height, 0.18f, 1f);
            r.x = Mathf.Clamp(r.x, 0f, 1f - r.width);
            r.y = Mathf.Clamp(r.y, 0f, 1f - r.height);

            if (r != OpticalWindow)
            {
                OpticalWindow = r;
                ApplyOpticalWindow();
            }

            if (WrldzInput.KeyDown(KeyCode.P))
                SaveWindow();
        }

        public void SetOpticalWindow(Rect r)
        {
            r.width = Mathf.Clamp(r.width, 0.18f, 1f);
            r.height = Mathf.Clamp(r.height, 0.18f, 1f);
            r.x = Mathf.Clamp(r.x, 0f, 1f - r.width);
            r.y = Mathf.Clamp(r.y, 0f, 1f - r.height);
            OpticalWindow = r;
            ApplyOpticalWindow();
        }

        public void FillOpticalWindow()
        {
            SetOpticalWindow(new Rect(0f, 0f, 1f, 1f));
            SaveWindow();
        }

        void ApplyOpticalWindow()
        {
            if (EyeCamera != null)
                EyeCamera.rect = OpticalWindow;
        }

        void LoadWindow()
        {
            var raw = PlayerPrefs.GetString(PrefWindow, "");
            if (string.IsNullOrEmpty(raw)) return;
            var p = raw.Split(',');
            if (p.Length != 4) return;
            if (float.TryParse(p[0], out var x) &&
                float.TryParse(p[1], out var y) &&
                float.TryParse(p[2], out var w) &&
                float.TryParse(p[3], out var h))
                OpticalWindow = new Rect(x, y, w, h);
        }

        void SaveWindow()
        {
            var r = OpticalWindow;
            PlayerPrefs.SetString(PrefWindow,
                $"{r.x:0.###},{r.y:0.###},{r.width:0.###},{r.height:0.###}");
            PlayerPrefs.Save();
            Debug.Log($"[WRLDZ Salvage] Optical window saved {r}");
        }
    }
}
