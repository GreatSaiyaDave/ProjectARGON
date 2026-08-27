using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Head pose for salvage optical-see-through lenses.
    /// Device on the brow (S23 or donor AMOLED): gyro / attitude sensor.
    /// Editor: RMB mouse-look so the bench can be iterated without hardware.
    /// Recenter captures "forward" — volume-down / R / gamepad North.
    /// </summary>
    public class SalvageHeadTracker : MonoBehaviour
    {
        public Transform Head { get; private set; }

        Quaternion _recenter = Quaternion.identity;
        Vector2 _editorEuler;

        public void Bind(Transform head)
        {
            Head = head;
        }

        public void Recenter()
        {
            _recenter = Quaternion.Inverse(RawRotation());
            _editorEuler = Vector2.zero;
            Apply();
            Debug.Log("[WRLDZ Salvage] Recenter — forward is now arena.");
        }

        void Start()
        {
            WrldzInput.TryGetGyroAttitude(out _);
            Recenter();
        }

        void Update()
        {
            if (WrldzRecenterPressed())
                Recenter();
            Apply();
        }

        void Apply()
        {
            if (Head == null) return;
            Head.localRotation = _recenter * RawRotation();
        }

        Quaternion RawRotation()
        {
            if (!Application.isEditor && WrldzInput.TryGetGyroAttitude(out var g))
            {
                // Android RHS → Unity LHS (same remap as Input.gyro.attitude)
                return new Quaternion(g.x, g.y, -g.z, -g.w);
            }

            if (Application.isEditor)
            {
                if (WrldzInput.MouseButtonHeld(1))
                {
                    var d = WrldzInput.MouseDelta;
                    _editorEuler.y += d.x * 0.12f;
                    _editorEuler.x -= d.y * 0.12f;
                    _editorEuler.x = Mathf.Clamp(_editorEuler.x, -80f, 80f);
                }

                return Quaternion.Euler(_editorEuler.x, _editorEuler.y, 0f);
            }

            return Quaternion.identity;
        }

        static bool WrldzRecenterPressed()
        {
            if (WrldzInput.KeyDown(KeyCode.R) && !WrldzInput.KeyHeld(KeyCode.LeftControl))
                return true;
            if (WrldzInput.GamepadNorthDown())
                return true;
            if (!Application.isEditor && WrldzInput.VolumeDownDown())
                return true;
            return false;
        }
    }
}
