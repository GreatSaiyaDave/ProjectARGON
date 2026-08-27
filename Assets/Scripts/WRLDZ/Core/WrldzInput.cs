using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WRLDZ.Core
{
    /// <summary>
    /// Safe keyboard / GPS access. Project may run Input System–only;
    /// never call UnityEngine.Input.GetKey* directly (throws InvalidOperationException).
    /// </summary>
    public static class WrldzInput
    {
        public static bool KeyHeld(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return Map(key)?.isPressed ?? false;
#else
            try { return Input.GetKey(key); }
            catch { return false; }
#endif
        }

        public static bool KeyDown(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            return Map(key)?.wasPressedThisFrame ?? false;
#else
            try { return Input.GetKeyDown(key); }
            catch { return false; }
#endif
        }

        public static bool MouseButtonHeld(int button)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return false;
            return button switch
            {
                0 => mouse.leftButton.isPressed,
                1 => mouse.rightButton.isPressed,
                2 => mouse.middleButton.isPressed,
                _ => false
            };
#else
            try { return Input.GetMouseButton(button); }
            catch { return false; }
#endif
        }

        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var mouse = Mouse.current;
                return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
                try { return Input.mousePosition; }
                catch { return Vector2.zero; }
#endif
            }
        }

        public static Vector2 MouseDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var mouse = Mouse.current;
                return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
#else
                try { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 20f; }
                catch { return Vector2.zero; }
#endif
            }
        }

        public static bool VolumeDownDown()
        {
            // Unity 6 dropped KeyCode.VolumeDown. Keep a numeric fallback for
            // the old Android mapping (317) and never reference the missing enum.
            try
            {
#if !ENABLE_INPUT_SYSTEM
                return Input.GetKeyDown((KeyCode)317);
#else
                return false;
#endif
            }
            catch { return false; }
        }

        public static bool GamepadNorthDown()
        {
#if ENABLE_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null && pad.buttonNorth.wasPressedThisFrame) return true;
#endif
            return false;
        }

        /// <summary>Device attitude if a gyro/attitude sensor exists (Android RHS from Input.gyro).</summary>
        public static bool TryGetGyroAttitude(out Quaternion attitude)
        {
            attitude = Quaternion.identity;
            try
            {
#if ENABLE_INPUT_SYSTEM
                var att = AttitudeSensor.current;
                if (att != null)
                {
                    if (!att.enabled) InputSystem.EnableDevice(att);
                    attitude = att.attitude.ReadValue();
                    return true;
                }
#endif
                if (SystemInfo.supportsGyroscope)
                {
                    Input.gyro.enabled = true;
                    Input.gyro.updateInterval = 1f / 60f;
                    attitude = Input.gyro.attitude;
                    return true;
                }
            }
            catch
            {
                // Input System–only players throw on some legacy sensor paths
            }

            return false;
        }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl Map(KeyCode key)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            return key switch
            {
                KeyCode.W => kb.wKey,
                KeyCode.A => kb.aKey,
                KeyCode.S => kb.sKey,
                KeyCode.D => kb.dKey,
                KeyCode.Q => kb.qKey,
                KeyCode.E => kb.eKey,
                KeyCode.Home => kb.homeKey,
                KeyCode.LeftControl => kb.leftCtrlKey,
                KeyCode.RightControl => kb.rightCtrlKey,
                KeyCode.I => kb.iKey,
                KeyCode.J => kb.jKey,
                KeyCode.K => kb.kKey,
                KeyCode.L => kb.lKey,
                KeyCode.U => kb.uKey,
                KeyCode.O => kb.oKey,
                KeyCode.P => kb.pKey,
                KeyCode.R => kb.rKey,
                KeyCode.LeftBracket => kb.leftBracketKey,
                KeyCode.RightBracket => kb.rightBracketKey,
                KeyCode.UpArrow => kb.upArrowKey,
                KeyCode.DownArrow => kb.downArrowKey,
                KeyCode.LeftArrow => kb.leftArrowKey,
                KeyCode.RightArrow => kb.rightArrowKey,
                KeyCode.Return => kb.enterKey,
                KeyCode.KeypadEnter => kb.numpadEnterKey,
                KeyCode.Escape => kb.escapeKey,
                KeyCode.Space => kb.spaceKey,
                KeyCode.LeftShift => kb.leftShiftKey,
                KeyCode.RightShift => kb.rightShiftKey,
                KeyCode.LeftAlt => kb.leftAltKey,
                KeyCode.RightAlt => kb.rightAltKey,
                _ => null
            };
        }
#endif

        // ── Location (UnityEngine.Input.location — works with new Input System too) ──

        /// <summary>
        /// Device can provide location (GPS/network). On Android, user must enable
        /// Location in system settings; app still needs runtime permission separately.
        /// </summary>
        public static bool LocationSupported
        {
            get
            {
                try
                {
                    // isEnabledByUser = system location toggle (not app permission)
                    return Input.location.isEnabledByUser;
                }
                catch { return false; }
            }
        }

        public static LocationServiceStatus LocationStatus
        {
            get
            {
                try { return Input.location.status; }
                catch { return LocationServiceStatus.Failed; }
            }
        }

        /// <summary>
        /// Start GPS. Accuracy ~5–10m for GO-style walking; update every few meters.
        /// </summary>
        public static void LocationStart(float desiredAccuracyMeters = 8f, float updateDistanceMeters = 3f)
        {
            try
            {
                if (Input.location.status == LocationServiceStatus.Running)
                    return;
                Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);
                Debug.Log(
                    $"[WRLDZ] Location.Start accuracy={desiredAccuracyMeters}m update={updateDistanceMeters}m " +
                    $"enabledByUser={Input.location.isEnabledByUser}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WRLDZ] Location.Start: " + e.Message);
            }
        }

        public static void LocationStop()
        {
            try
            {
                if (Input.location.status == LocationServiceStatus.Running)
                    Input.location.Stop();
            }
            catch { /* ignore */ }
        }

        public static bool TryGetLocation(out float lat, out float lon)
        {
            lat = lon = 0f;
            try
            {
                if (Input.location.status != LocationServiceStatus.Running)
                    return false;
                var data = Input.location.lastData;
                lat = data.latitude;
                lon = data.longitude;
                // Reject unset / invalid fix (0,0 with absurd accuracy)
                if (Mathf.Abs(lat) < 0.0001f && Mathf.Abs(lon) < 0.0001f)
                    return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Horizontal accuracy in meters when running; large number if unknown.</summary>
        public static float LocationAccuracyMeters
        {
            get
            {
                try
                {
                    if (Input.location.status != LocationServiceStatus.Running) return 9999f;
                    return Input.location.lastData.horizontalAccuracy;
                }
                catch { return 9999f; }
            }
        }
    }
}
