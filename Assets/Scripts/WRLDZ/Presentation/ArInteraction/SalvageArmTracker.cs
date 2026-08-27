using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// 1:1 m arm tracker for salvage optical-see-through.
    /// No controllers — left disk is estimated from the head (ready stance),
    /// opponent sits at match separation along recentered forward.
    /// Same contract as <see cref="XrWorldArmTracker"/>.
    /// </summary>
    public class SalvageArmTracker : MonoBehaviour, IArTrackingSource
    {
        public Transform Head;
        public Transform FloorAnchor;

        public Vector3 LeftForearmFromHead = new(-0.25f, -0.35f, 0.28f);
        public Vector3 LeftForearmEuler = new(65f, 0f, 0f);

        public float SeparationMeters { get; private set; } = ArDuelMatchConfig.DefaultStandM;

        public bool IsTracking => Head != null;

        public void Bind(Transform head, Transform floor)
        {
            Head = head;
            FloorAnchor = floor;
        }

        public void ApplyPlayerSeparationMeters(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters,
                ArDuelMatchConfig.MinSeparationM, ArDuelMatchConfig.MaxSeparationM);
            Debug.Log($"[WRLDZ Salvage] Arm tracker sep={SeparationMeters:0.0}m (1:1)");
        }

        public bool TryGetLeftForearm(out Pose pose)
        {
            pose = default;
            if (Head == null) return false;
            var p = Head.TransformPoint(LeftForearmFromHead);
            var r = Head.rotation * Quaternion.Euler(LeftForearmEuler);
            pose = new Pose(p, r);
            return true;
        }

        public bool TryGetTorso(out Pose pose)
        {
            pose = default;
            if (Head == null) return false;
            var p = Head.position + Head.up * -0.28f + Head.forward * 0.08f;
            var flat = Vector3.ProjectOnPlane(Head.forward, Vector3.up);
            var r = flat.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Head.rotation;
            pose = new Pose(p, r);
            return true;
        }

        public bool TryGetOpponentLeftForearm(out Pose pose)
        {
            pose = default;
            if (!TryGetLeftForearm(out var myArm))
                return false;

            var forward = Head != null
                ? Vector3.ProjectOnPlane(Head.forward, Vector3.up)
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            else forward.Normalize();

            var floorY = FloorAnchor != null ? FloorAnchor.position.y : myArm.position.y - 1.2f;
            var opp = myArm.position + forward * SeparationMeters;
            opp.y = floorY + 0.95f;
            var look = Quaternion.LookRotation(-forward, Vector3.up) * Quaternion.Euler(LeftForearmEuler);
            pose = new Pose(opp, look);
            return true;
        }

        public bool TryGetOpponentTorso(out Pose pose) => ArOppTorso.FromForearm(this, out pose);
    }
}
