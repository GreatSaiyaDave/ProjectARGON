using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Body / hand tracking adapter. Lab default = simulated arm (S23 / Editor).
    /// Swap for XR hand / AR body joints without changing disk or arena code.
    /// </summary>
    public interface IArTrackingSource
    {
        bool IsTracking { get; }

        /// <summary>Left forearm pose in world space (disk rigid parent).</summary>
        bool TryGetLeftForearm(out Pose pose);

        /// <summary>Torso / chest center for the floating hand volume.</summary>
        bool TryGetTorso(out Pose pose);

        /// <summary>Optional second player's left forearm (shared arena midpoint).</summary>
        bool TryGetOpponentLeftForearm(out Pose pose);

        /// <summary>Opponent chest — floating hand holos at their end of the street.</summary>
        bool TryGetOpponentTorso(out Pose pose);
    }

    /// <summary>
    /// Simulated forearm locked relative to the AR stage camera —
    /// works on S23 Ultra (phone AR) and PC Editor without a headset.
    /// Default pose = natural Battle City "ready" stance (bent left arm, plate readable).
    /// </summary>
    public class SimulatedArmTracker : MonoBehaviour, IArTrackingSource
    {
        [Tooltip("Stage root (ArDuelSpace stage). Disk offsets are local to this.")]
        public Transform stageRoot;

        [Tooltip("Optional stage camera for torso look.")]
        public Transform stageCamera;

        // Defaults match ApplyPlayerSeparationMeters human-ready stance (sep ≈ 2.5 m).
        // Wrists sit outside the holo volume so the plate is not under the arena.
        public Vector3 leftForearmLocalPos = new(-0.22f, 0.52f, -3.05f);
        public Vector3 leftForearmLocalEuler = new(78f, 8f, -88f);
        // Chest / upper torso — hand holos float in front of the body, above the disk
        public Vector3 torsoLocalPos = new(0.04f, 0.88f, -1.74f);
        public Vector3 opponentForearmLocalPos = new(0.22f, 0.58f, 3.05f);
        public Vector3 opponentForearmLocalEuler = new(78f, 172f, 88f);
        public Vector3 opponentTorsoLocalPos = new(-0.04f, 0.94f, 1.74f);

        /// <summary>
        /// Soft idle sway so the static sim arm does not look frozen.
        /// Disable for screenshot / deterministic tests.
        /// </summary>
        public bool EnableIdleMotion = true;

        /// <summary>Last applied real-world separation (meters) between duelists.</summary>
        public float SeparationMeters { get; private set; } = 2.5f;

        /// <summary>
        /// Stage units per real meter (how far disks sit in the AR viewport).
        /// Tuned so ~2.5m standing duel fills a phone portrait stage comfortably.
        /// </summary>
        public float StageUnitsPerMeter = 0.52f;

        /// <summary>
        /// Extra stage push so YOUR wrist sits well clear of midfield holos.
        /// Disks live outside the holo volume; holos stay in the air between arms.
        /// Must exceed DiskToHoloMinGap + plate reach or the field still kisses the disk.
        /// </summary>
        public float ArmClearanceFromMid = 2.40f;

        /// <summary>Lateral offset of left wrist from body centerline (stage units).</summary>
        public float WristLateral = 0.22f;

        /// <summary>
        /// Wrist height in stage units — roughly belt / lower-ribs when the camera
        /// is looking down at the dual field (human arm bent, disk worn ready).
        /// </summary>
        public float WristHeight = 0.52f;

        /// <summary>Opp wrist height (slightly higher so far side still reads).</summary>
        public float OppWristHeight = 0.58f;

        public bool IsTracking => stageRoot != null;

        /// <summary>
        /// Anchor both duelists so the shared arena midpoint sits between them at
        /// approximately <paramref name="meters"/> real-world separation.
        /// YOU = near camera (bottom); OPP = far (top). Disks track these poses every frame.
        /// </summary>
        public void ApplyPlayerSeparationMeters(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters, 1.2f, 8f);
            StageUnitsPerMeter = Mathf.Clamp(StageUnitsPerMeter, 0.36f, 0.58f);
            var half = SeparationMeters * 0.5f * StageUnitsPerMeter;
            // Disks well outside holo volume (player S/T row is the nearest holo to each arm)
            var clear = Mathf.Max(ArmClearanceFromMid, half * 0.85f);
            var youZ = -(half + clear);
            var oppZ = half + clear;

            // ── Human dual-disk stance (camera looks +Z toward opponent) ──
            // YOU: left forearm across lower torso, slightly forward — Battle City ready.
            // Plate MUST face the camera (not edge-on) so the 5 stages + cards read.
            // Mesh: +X blade, +Y plate normal, +Z wearer. Map plate toward camera.
            leftForearmLocalPos = new Vector3(-WristLateral, WristHeight, youZ);
            leftForearmLocalEuler = new Vector3(78f, 8f, -88f);

            // OPP: mirrored stance, left arm on their body (our right of frame)
            opponentForearmLocalPos = new Vector3(WristLateral, OppWristHeight, oppZ);
            opponentForearmLocalEuler = new Vector3(78f, 172f, 88f);

            // Hand holos at chest, slightly toward camera from the wrist so cards
            // float in the lower-center FOV above the disk, not on midfield.
            torsoLocalPos = new Vector3(
                0.04f,
                WristHeight + 0.36f,
                youZ * 0.55f - 0.06f);

            // Opp chest mirrors yours — cards float above their disk, facing you.
            opponentTorsoLocalPos = new Vector3(
                -0.04f,
                OppWristHeight + 0.36f,
                oppZ * 0.55f + 0.06f);

            Debug.Log(
                $"[WRLDZ AR] Dual-disk frame · sep={SeparationMeters:0.0}m · half={half:0.00} · " +
                $"youZ={youZ:0.00} oppZ={oppZ:0.00} clear={clear:0.00} · " +
                $"wristY={WristHeight:0.00} · YOU ready stance · midfield holos · OPP far");
        }

        /// <summary>
        /// Lab disk pose (independent of the free-look camera).
        /// Arrows / R F move · I J K L U O rotate · P reset.
        /// Camera is WASD Q E + RMB — never steal those here.
        /// </summary>
        public Vector3 LabPoseOffset;
        public Vector3 LabEulerOffset;
        bool _loggedControls;
        bool _diskKeysHeld;

        void LateUpdate()
        {
            if (!Application.isEditor && !Application.isMobilePlatform) return;
            if (!_loggedControls)
            {
                _loggedControls = true;
                Debug.Log(
                    "[WRLDZ AR] View: WASD walk · Q/E height · RMB look · Home reset\n" +
                    "[WRLDZ AR] Disk: Arrows slide · R/F up/down · I/K pitch · J/L yaw · U/O roll · " +
                    "Shift=fine · P reset");
            }

            var dt = Time.unscaledDeltaTime;
            var fine = WrldzInput.KeyHeld(KeyCode.LeftShift) || WrldzInput.KeyHeld(KeyCode.RightShift);
            var move = fine ? 0.32f : 1.35f;
            var ang = fine ? 28f : 95f;

            _diskKeysHeld =
                WrldzInput.KeyHeld(KeyCode.LeftArrow) || WrldzInput.KeyHeld(KeyCode.RightArrow) ||
                WrldzInput.KeyHeld(KeyCode.UpArrow) || WrldzInput.KeyHeld(KeyCode.DownArrow) ||
                WrldzInput.KeyHeld(KeyCode.R) || WrldzInput.KeyHeld(KeyCode.F) ||
                WrldzInput.KeyHeld(KeyCode.I) || WrldzInput.KeyHeld(KeyCode.K) ||
                WrldzInput.KeyHeld(KeyCode.J) || WrldzInput.KeyHeld(KeyCode.L) ||
                WrldzInput.KeyHeld(KeyCode.U) || WrldzInput.KeyHeld(KeyCode.O);

            // Translate on the ground plane, relative to the camera so "up arrow" is toward look.
            var cam = stageCamera != null ? stageCamera : (Camera.main != null ? Camera.main.transform : null);
            var flat = Vector3.forward;
            var right = Vector3.right;
            if (cam != null)
            {
                flat = cam.forward;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1e-6f) flat = cam.up.y > 0.2f ? Vector3.forward : cam.forward;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-6f) flat.Normalize();
                else flat = Vector3.forward;
                right = Vector3.Cross(Vector3.up, flat);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                else right.Normalize();
            }

            var world = Vector3.zero;
            if (WrldzInput.KeyHeld(KeyCode.UpArrow)) world += flat;
            if (WrldzInput.KeyHeld(KeyCode.DownArrow)) world -= flat;
            if (WrldzInput.KeyHeld(KeyCode.LeftArrow)) world -= right;
            if (WrldzInput.KeyHeld(KeyCode.RightArrow)) world += right;
            if (WrldzInput.KeyHeld(KeyCode.R)) world += Vector3.up;
            if (WrldzInput.KeyHeld(KeyCode.F)) world += Vector3.down;
            if (world.sqrMagnitude > 1e-6f)
            {
                world = world.normalized * move * dt;
                if (stageRoot != null)
                    LabPoseOffset += stageRoot.InverseTransformDirection(world);
                else
                    LabPoseOffset += world;
            }

            if (WrldzInput.KeyHeld(KeyCode.J)) LabEulerOffset.y -= ang * dt;
            if (WrldzInput.KeyHeld(KeyCode.L)) LabEulerOffset.y += ang * dt;
            if (WrldzInput.KeyHeld(KeyCode.I)) LabEulerOffset.x -= ang * dt;
            if (WrldzInput.KeyHeld(KeyCode.K)) LabEulerOffset.x += ang * dt;
            if (WrldzInput.KeyHeld(KeyCode.U)) LabEulerOffset.z -= ang * dt;
            if (WrldzInput.KeyHeld(KeyCode.O)) LabEulerOffset.z += ang * dt;

            if (WrldzInput.KeyDown(KeyCode.P))
            {
                LabPoseOffset = Vector3.zero;
                LabEulerOffset = Vector3.zero;
            }

            LabEulerOffset.x = Mathf.Clamp(LabEulerOffset.x, -89f, 89f);
            LabEulerOffset.y = Mathf.Clamp(LabEulerOffset.y, -180f, 180f);
            LabEulerOffset.z = Mathf.Clamp(LabEulerOffset.z, -80f, 80f);
            LabPoseOffset.x = Mathf.Clamp(LabPoseOffset.x, -4.5f, 4.5f);
            LabPoseOffset.y = Mathf.Clamp(LabPoseOffset.y, -2.0f, 2.4f);
            LabPoseOffset.z = Mathf.Clamp(LabPoseOffset.z, -4.5f, 4.5f);
        }

        public bool TryGetLeftForearm(out Pose pose)
        {
            pose = default;
            if (stageRoot == null) return false;
            var local = leftForearmLocalPos + LabPoseOffset + IdlePosOffset(playerSide: true);
            var p = stageRoot.TransformPoint(local);
            var r = stageRoot.rotation
                    * Quaternion.Euler(leftForearmLocalEuler + LabEulerOffset)
                    * IdleRotOffset(playerSide: true);
            pose = new Pose(p, r);
            return true;
        }

        public bool TryGetTorso(out Pose pose)
        {
            pose = default;
            if (stageRoot == null) return false;
            // Subtle chest breathe with the arm idle so hand volume stays coherent
            var local = torsoLocalPos;
            if (EnableIdleMotion)
                local += new Vector3(0f, Mathf.Sin(Time.time * 1.05f) * 0.008f, 0f);
            var p = stageRoot.TransformPoint(local);
            var look = stageCamera != null
                ? Quaternion.LookRotation(stageCamera.forward, Vector3.up)
                : stageRoot.rotation;
            pose = new Pose(p, look);
            return true;
        }

        public bool TryGetOpponentLeftForearm(out Pose pose)
        {
            pose = default;
            if (stageRoot == null) return false;
            var local = opponentForearmLocalPos + IdlePosOffset(playerSide: false);
            var p = stageRoot.TransformPoint(local);
            var r = stageRoot.rotation
                    * Quaternion.Euler(opponentForearmLocalEuler)
                    * IdleRotOffset(playerSide: false);
            pose = new Pose(p, r);
            return true;
        }

        public bool TryGetOpponentTorso(out Pose pose)
        {
            pose = default;
            if (stageRoot == null) return false;
            var local = opponentTorsoLocalPos;
            if (EnableIdleMotion)
                local += new Vector3(0f, Mathf.Sin(Time.time * 1.05f + 1.7f) * 0.008f, 0f);
            var p = stageRoot.TransformPoint(local);
            var toYou = Vector3.zero;
            if (TryGetTorso(out var you))
                toYou = you.position - p;
            toYou.y = 0f;
            var look = toYou.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toYou.normalized, Vector3.up)
                : stageRoot.rotation * Quaternion.Euler(0f, 180f, 0f);
            pose = new Pose(p, look);
            return true;
        }

        /// <summary>Soft mm-scale bob / sway — keeps sim arm from looking frozen.</summary>
        Vector3 IdlePosOffset(bool playerSide)
        {
            if (!EnableIdleMotion || !Application.isPlaying || _diskKeysHeld) return Vector3.zero;
            var t = Time.time;
            var phase = playerSide ? 0f : 1.7f;
            // Vertical breathe + tiny lateral sway (arm holding ready)
            return new Vector3(
                Mathf.Sin(t * 0.85f + phase) * 0.004f,
                Mathf.Sin(t * 1.15f + phase) * 0.007f,
                Mathf.Cos(t * 0.7f + phase) * 0.003f);
        }

        Quaternion IdleRotOffset(bool playerSide)
        {
            if (!EnableIdleMotion || !Application.isPlaying || _diskKeysHeld) return Quaternion.identity;
            var t = Time.time;
            var phase = playerSide ? 0f : 1.7f;
            // ~1° micro-tilt — enough to feel alive, not seasick
            return Quaternion.Euler(
                Mathf.Sin(t * 0.95f + phase) * 1.1f,
                Mathf.Cos(t * 0.75f + phase) * 0.7f,
                Mathf.Sin(t * 0.6f + phase * 0.5f) * 0.9f);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (stageRoot == null) return;
            // Scene view: wrists + mid so Opp side is obvious
            var you = stageRoot.TransformPoint(leftForearmLocalPos);
            var opp = stageRoot.TransformPoint(opponentForearmLocalPos);
            var mid = (you + opp) * 0.5f;
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(you, 0.12f);
            Gizmos.DrawLine(you, mid);
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(opp, 0.16f);
            Gizmos.DrawLine(opp, mid);
            // Dual playmat volume between wrists
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
            Gizmos.DrawCube(mid + Vector3.up * 0.05f, new Vector3(3.2f, 0.08f, 2.8f));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(mid, new Vector3(3.2f, 0.08f, 2.8f));
            // Arrow toward Opp
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(mid, mid + (opp - mid).normalized * 0.8f + Vector3.up * 0.3f);
        }
#endif
    }

    /// <summary>
    /// XR controller transform as forearm (manual assign). Prefer <see cref="XrWorldArmTracker"/> for Quest.
    /// </summary>
    public class XrControllerArmTracker : MonoBehaviour, IArTrackingSource
    {
        public Transform leftController;
        public Transform headOrTorso;
        public Transform rightControllerAsOpponent;

        public Vector3 forearmOffset = new(0f, -0.04f, 0.06f);
        public Vector3 forearmEuler = new(70f, 0f, 0f);

        public bool IsTracking => leftController != null && leftController.gameObject.activeInHierarchy;

        public bool TryGetLeftForearm(out Pose pose)
        {
            pose = default;
            if (leftController == null) return false;
            var p = leftController.TransformPoint(forearmOffset);
            var r = leftController.rotation * Quaternion.Euler(forearmEuler);
            pose = new Pose(p, r);
            return true;
        }

        public bool TryGetTorso(out Pose pose)
        {
            pose = default;
            if (headOrTorso == null) return false;
            var p = headOrTorso.position + headOrTorso.up * -0.25f + headOrTorso.forward * 0.05f;
            pose = new Pose(p, headOrTorso.rotation);
            return true;
        }

        public bool TryGetOpponentLeftForearm(out Pose pose)
        {
            pose = default;
            if (rightControllerAsOpponent == null) return false;
            pose = new Pose(rightControllerAsOpponent.position, rightControllerAsOpponent.rotation);
            return true;
        }

        public bool TryGetOpponentTorso(out Pose pose) => ArOppTorso.FromForearm(this, out pose);
    }

    /// <summary>
    /// Life-size OpenXR arm tracker for Quest / Android XR lenses.
    /// Player disk follows left controller (or left hand); opponent disk sits at real-world
    /// separation meters in front (1:1). Stage units = meters.
    /// </summary>
    public class XrWorldArmTracker : MonoBehaviour, IArTrackingSource
    {
        public Transform leftController;
        public Transform rightController;
        public Transform head;
        public Transform floorAnchor;

        /// <summary>Offset from controller grip to disk wear surface (meters).</summary>
        public Vector3 leftForearmOffset = new(0f, -0.03f, 0.05f);
        public Vector3 leftForearmEuler = new(65f, 0f, 0f);

        public float SeparationMeters { get; private set; } = 2.5f;

        /// <summary>Always 1.0 for lenses — real world meters.</summary>
        public const float StageUnitsPerMeter = 1f;

        bool _hasLeft;
        bool _hasHead;
        Pose _lastLeft;
        Pose _lastHead;

        public bool IsTracking => _hasLeft || leftController != null;

        public void Bind(Transform left, Transform right, Transform headTf, Transform floor)
        {
            leftController = left;
            rightController = right;
            head = headTf;
            floorAnchor = floor;
        }

        /// <summary>1:1 meters between duelists (opponent along player forward).</summary>
        public void ApplyPlayerSeparationMeters(float meters)
        {
            SeparationMeters = Mathf.Clamp(meters, 1.2f, 8f);
            Debug.Log($"[WRLDZ Lenses] XrWorldArmTracker separation {SeparationMeters:0.0}m (1:1)");
        }

        public bool TryGetLeftForearm(out Pose pose)
        {
            pose = default;
            if (leftController != null && leftController.gameObject.activeInHierarchy)
            {
                // Prefer live controller; fall back if still at origin early in session
                var p = leftController.TransformPoint(leftForearmOffset);
                var r = leftController.rotation * Quaternion.Euler(leftForearmEuler);
                // If controller not tracking yet, estimate from head
                if (leftController.localPosition.sqrMagnitude < 1e-6f && head != null)
                {
                    p = head.position + head.TransformDirection(new Vector3(-0.25f, -0.35f, 0.25f));
                    r = head.rotation * Quaternion.Euler(leftForearmEuler);
                }

                pose = new Pose(p, r);
                _lastLeft = pose;
                _hasLeft = true;
                return true;
            }

            if (_hasLeft)
            {
                pose = _lastLeft;
                return true;
            }

            return false;
        }

        public bool TryGetTorso(out Pose pose)
        {
            pose = default;
            if (head != null)
            {
                var p = head.position + head.up * -0.28f + head.forward * 0.08f;
                var r = Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized,
                    Vector3.up);
                if (r == Quaternion.identity)
                    r = head.rotation;
                pose = new Pose(p, r);
                _lastHead = pose;
                _hasHead = true;
                return true;
            }

            if (_hasHead)
            {
                pose = _lastHead;
                return true;
            }

            return false;
        }

        public bool TryGetOpponentLeftForearm(out Pose pose)
        {
            pose = default;
            // Optional: right controller as guest when present and far enough
            if (rightController != null && rightController.localPosition.sqrMagnitude > 0.01f)
            {
                var dist = head != null
                    ? Vector3.Distance(rightController.position, head.position)
                    : 0f;
                if (dist > 0.4f)
                {
                    pose = new Pose(
                        rightController.TransformPoint(leftForearmOffset),
                        rightController.rotation * Quaternion.Euler(leftForearmEuler));
                    return true;
                }
            }

            // Synthetic opponent at real separation along floor-forward from player mid
            if (!TryGetLeftForearm(out var myArm))
            {
                if (head == null) return false;
                myArm = new Pose(head.position + head.forward * 0.3f, head.rotation);
            }

            var forward = head != null
                ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

            var floorY = floorAnchor != null ? floorAnchor.position.y : myArm.position.y - 1.2f;
            var mid = myArm.position;
            mid.y = floorY + 0.95f; // arm height ~ standing
            var oppPos = mid + forward * SeparationMeters;
            oppPos.y = floorY + 0.95f;
            var look = Quaternion.LookRotation(-forward, Vector3.up) * Quaternion.Euler(leftForearmEuler);
            pose = new Pose(oppPos, look);
            return true;
        }

        public bool TryGetOpponentTorso(out Pose pose) => ArOppTorso.FromForearm(this, out pose);
    }

    /// <summary>Chest pose for the far-side hand fan, derived from their left forearm.</summary>
    public static class ArOppTorso
    {
        public static bool FromForearm(IArTrackingSource tracker, out Pose pose)
        {
            pose = default;
            if (tracker == null || !tracker.TryGetOpponentLeftForearm(out var arm))
                return false;
            var look = Vector3.forward;
            if (tracker.TryGetTorso(out var you))
            {
                look = you.position - arm.position;
                look.y = 0f;
            }

            var rot = look.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : arm.rotation;
            pose = new Pose(arm.position + Vector3.up * 0.34f, rot);
            return true;
        }
    }
}
