using System;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Full AR arm tracker: disks and arena are <b>world-locked on the floor plane</b>.
    /// The phone is a window, not a HUD. Hand volume stays in front of the camera
    /// so cards remain reachable while you look through the glass.
    /// </summary>
    public class ArFoundationArmTracker : MonoBehaviour, IArTrackingSource
    {
        public Transform Head;
        public Transform FloorAnchor;
        Func<bool> _floorLocked;

        public Vector3 PlayerWristLocal = new(-0.28f, 0.95f, 0.35f);
        public Vector3 PlayerWristEuler = new(70f, 8f, -88f);
        public Vector3 OppWristLocal = new(0.28f, 0.95f, 0f);
        public Vector3 OppWristEuler = new(70f, 172f, 88f);

        public float SeparationMeters { get; private set; } = ArDuelMatchConfig.DefaultStandM;

        Pose _lastLeft;
        Pose _lastHead;
        bool _hasLeft;
        bool _hasHead;

        public bool IsTracking => Head != null;

        public void Bind(Transform head, Transform floor, Func<bool> floorLocked)
        {
            Head = head;
            FloorAnchor = floor;
            _floorLocked = floorLocked;
        }

        public void ApplyPlayerSeparationMeters(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters,
                ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            OppWristLocal = new Vector3(0.28f, 0.95f, SeparationMeters);
            Debug.Log($"[WRLDZ ARCore] Arm tracker sep={SeparationMeters:0.0}m · world-locked disks");
        }

        public void NotifyFloorLocked()
        {
            // Poses recompute from FloorAnchor every query — nothing to cache.
        }

        bool FloorReady => _floorLocked != null && _floorLocked() && FloorAnchor != null;

        public bool TryGetLeftForearm(out Pose pose)
        {
            pose = default;
            if (FloorReady)
            {
                var p = FloorAnchor.TransformPoint(PlayerWristLocal);
                var r = FloorAnchor.rotation * Quaternion.Euler(PlayerWristEuler);
                pose = new Pose(p, r);
                _lastLeft = pose;
                _hasLeft = true;
                return true;
            }

            if (Head == null) return _hasLeft && Assign(out pose, _lastLeft);
            // Scanning: keep a readable disk in view under the camera
            var hp = Head.TransformPoint(new Vector3(-0.18f, -0.28f, 0.55f));
            var hr = Head.rotation * Quaternion.Euler(PlayerWristEuler);
            pose = new Pose(hp, hr);
            _lastLeft = pose;
            _hasLeft = true;
            return true;
        }

        public bool TryGetTorso(out Pose pose)
        {
            pose = default;
            if (Head == null) return _hasHead && Assign(out pose, _lastHead);
            var p = Head.position + Head.forward * 0.42f + Head.up * -0.12f;
            pose = new Pose(p, Head.rotation);
            _lastHead = pose;
            _hasHead = true;
            return true;
        }

        public bool TryGetOpponentLeftForearm(out Pose pose)
        {
            pose = default;
            if (FloorReady)
            {
                var p = FloorAnchor.TransformPoint(OppWristLocal);
                var r = FloorAnchor.rotation * Quaternion.Euler(OppWristEuler);
                pose = new Pose(p, r);
                return true;
            }

            if (!TryGetLeftForearm(out var my)) return false;
            var fwd = Head != null
                ? Vector3.ProjectOnPlane(Head.forward, Vector3.up)
                : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            else fwd.Normalize();
            pose = new Pose(my.position + fwd * SeparationMeters, Quaternion.LookRotation(-fwd, Vector3.up));
            return true;
        }

        public bool TryGetOpponentTorso(out Pose pose) => ArOppTorso.FromForearm(this, out pose);

        static bool Assign(out Pose pose, Pose cached)
        {
            pose = cached;
            return true;
        }
    }
}
