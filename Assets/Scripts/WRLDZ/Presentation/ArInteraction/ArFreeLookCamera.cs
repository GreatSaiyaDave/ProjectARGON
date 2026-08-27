using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// First-person look/walk on the EditorSim / phone spatial stage so duel mode
    /// can be inspected like IRL AR (turn your head / walk around the field).
    /// Disks stay world-anchored. Lenses XR uses the HMD instead.
    ///
    /// WASD walk · Q/E height · RMB (or empty-viewport drag) look · Home reset.
    /// Arrow keys are reserved for moving the duel disk.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public class ArFreeLookCamera : MonoBehaviour
    {
        public Camera Cam;
        public ArCardDragSystem Drag;
        public RectTransform Viewport;

        public bool UserMoved { get; private set; }

        /// <summary>
        /// Anime director may drive the camera while this is true, but only until
        /// the user looks or walks — free-look then reclaims the transform.
        /// </summary>
        public bool SuppressApply { get; set; }

        const float Walk = 1.55f;
        const float LookSens = 0.18f;
        const float TouchSens = 0.22f;
        const float GyroSens = 1f;
        const float LookArmPx = 14f;
        const float PitchMin = -89f;
        const float PitchMax = 89f;

        float _yaw;
        float _pitch;
        Vector3 _homePos;
        float _homeYaw;
        float _homePitch;
        bool _homeSet;
        bool _rmbLook;
        Vector2 _lastMouse;
        bool _touchLook;
        Vector2 _touchOrigin;
        Vector2 _touchLast;
        Quaternion _gyroBaseInv = Quaternion.identity;
        bool _gyroReady;

        public static ArFreeLookCamera Attach(Camera cam)
        {
            if (cam == null) return null;
            var fl = cam.GetComponent<ArFreeLookCamera>() ?? cam.gameObject.AddComponent<ArFreeLookCamera>();
            fl.Cam = cam;
            fl.CapturePose();
            return fl;
        }

        /// <summary>Store the current framed pose as home (auto-frame / Home key).</summary>
        public void CapturePose()
        {
            if (Cam == null) Cam = GetComponent<Camera>();
            if (Cam == null) return;
            ReadYawPitchFromForward(Cam.transform, out _yaw, out _pitch);
            _homePos = Cam.transform.position;
            _homeYaw = _yaw;
            _homePitch = _pitch;
            _homeSet = true;
            UserMoved = false;
        }

        void Start()
        {
            if (Cam == null) Cam = GetComponent<Camera>();
            if (!_homeSet) CapturePose();
            TryInitGyro();
        }

        void Update()
        {
            if (Cam == null) return;
            // Headset owns the eye
            if (ArPresentationTarget.IsLenses) return;

            var dt = Time.unscaledDeltaTime;
            HandleLook();
            HandleMove(dt);

            if (WrldzInput.KeyDown(KeyCode.Home) && _homeSet)
            {
                Cam.transform.position = _homePos;
                _yaw = _homeYaw;
                _pitch = _homePitch;
                ApplyRotation();
                UserMoved = false;
                SuppressApply = false;
            }
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            if (ArPresentationTarget.IsLenses) return;
            // Re-assert look every frame so summon/attack camera slerps cannot
            // spring the view back to the pavement after the user looks up.
            if (SuppressApply && !UserMoved) return;
            ApplyRotation();
        }

        void HandleLook()
        {
            var rmb = WrldzInput.MouseButtonHeld(1);
            if (rmb)
            {
                var m = WrldzInput.MousePosition;
                if (!_rmbLook)
                {
                    _rmbLook = true;
                    _lastMouse = m;
                }
                else
                {
                    var d = m - _lastMouse;
                    _lastMouse = m;
                    ApplyLookDelta(d.x * LookSens, -d.y * LookSens);
                }
            }
            else
                _rmbLook = false;

            ApplyGyro();
        }

        /// <summary>Empty-viewport drag (no hand card) — phone / LMB look.</summary>
        public void OnPointerDown(Vector2 screen)
        {
            if (Drag != null && Drag.OwnsPointer) return;
            _touchLook = false;
            _touchOrigin = screen;
            _touchLast = screen;
        }

        public void OnPointerDrag(Vector2 screen)
        {
            if (Drag != null && Drag.OwnsPointer)
            {
                _touchLook = false;
                return;
            }

            if (!_touchLook)
            {
                if ((screen - _touchOrigin).sqrMagnitude < LookArmPx * LookArmPx)
                    return;
                _touchLook = true;
                _touchLast = screen;
            }

            var d = screen - _touchLast;
            _touchLast = screen;
            ApplyLookDelta(d.x * TouchSens, -d.y * TouchSens);
        }

        public void OnPointerUp()
        {
            _touchLook = false;
        }

        void HandleMove(float dt)
        {
            var t = Cam.transform;
            var flat = t.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();
            var right = Vector3.Cross(Vector3.up, flat).normalized;

            var v = Vector3.zero;
            if (WrldzInput.KeyHeld(KeyCode.W))
                v += flat;
            if (WrldzInput.KeyHeld(KeyCode.S))
                v -= flat;
            if (WrldzInput.KeyHeld(KeyCode.A))
                v -= right;
            if (WrldzInput.KeyHeld(KeyCode.D))
                v += right;
            if (WrldzInput.KeyHeld(KeyCode.Q))
                v += Vector3.down;
            if (WrldzInput.KeyHeld(KeyCode.E))
                v += Vector3.up;

            if (v.sqrMagnitude < 1e-6f) return;
            UserMoved = true;
            SuppressApply = false;
            t.position += v.normalized * Walk * dt;
        }

        void ApplyLookDelta(float yawDeg, float pitchDeg)
        {
            if (Mathf.Abs(yawDeg) < 0.0001f && Mathf.Abs(pitchDeg) < 0.0001f) return;
            UserMoved = true;
            SuppressApply = false;
            _yaw += yawDeg;
            _pitch = Mathf.Clamp(_pitch + pitchDeg, PitchMin, PitchMax);
            ApplyRotation();
        }

        void ApplyRotation()
        {
            if (Cam == null) return;
            Cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        /// <summary>
        /// Yaw/pitch from the camera forward so LookAt roll cannot gimbal-flip
        /// eulerAngles.x into a downward rest pose.
        /// </summary>
        static void ReadYawPitchFromForward(Transform t, out float yaw, out float pitch)
        {
            var f = t.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, PitchMin, PitchMax);
        }

        static float NormalizePitch(float eulerX)
        {
            if (eulerX > 180f) eulerX -= 360f;
            return Mathf.Clamp(eulerX, PitchMin, PitchMax);
        }

        void TryInitGyro()
        {
            if (Application.isEditor) return;
            try
            {
                if (!SystemInfo.supportsGyroscope) return;
                Input.gyro.enabled = true;
                _gyroBaseInv = Quaternion.Inverse(GyroToUnity(Input.gyro.attitude));
                _gyroReady = true;
            }
            catch
            {
                _gyroReady = false;
            }
        }

        void ApplyGyro()
        {
            if (!_gyroReady) return;
            try
            {
                var g = GyroToUnity(Input.gyro.attitude);
                var look = _gyroBaseInv * g;
                var e = look.eulerAngles;
                var yaw = e.y;
                var pitch = NormalizePitch(e.x);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, yaw)) > 0.05f ||
                    Mathf.Abs(pitch - _pitch) > 0.05f)
                {
                    UserMoved = true;
                    SuppressApply = false;
                }
                _yaw = Mathf.LerpAngle(_yaw, yaw, Time.unscaledDeltaTime * 8f * GyroSens);
                _pitch = Mathf.Lerp(_pitch, pitch, Time.unscaledDeltaTime * 8f * GyroSens);
                ApplyRotation();
            }
            catch
            {
                _gyroReady = false;
            }
        }

        /// <summary>Unity gyro is right-handed, Z-back. Convert to left-handed camera.</summary>
        static Quaternion GyroToUnity(Quaternion q) =>
            new(-q.x, -q.y, q.z, q.w);
    }
}
