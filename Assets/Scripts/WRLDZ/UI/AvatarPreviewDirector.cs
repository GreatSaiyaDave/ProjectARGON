using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Brings the customizer's preview duelist to life (2026-10-19, owner: "provide a better animation in the character
    /// customization screen and include spirit dueler selection and color palette switching"):
    /// <list type="bullet">
    /// <item><b>Duelist tabs</b> (title, spirit, disk, aura): the Duel Disk goes on the left forearm
    /// (<see cref="AvatarSpiritDisk"/>) and the arm takes the Battle City sequence the AI duelists use - the folded standby at
    /// the hip, the snap forward to the shield pose (ease-out-back), held rigid while the disk deploys, then the forearm
    /// level across the navel with the zones toward the viewer, breathing. Leaving the tabs folds the disk away and the arm
    /// eases back into the idle.</item>
    /// <item><b>Reactions</b>: an edit gets a beat - a smile for the face, a hand on the hip for clothes and builds, a fist
    /// pump for a new title or disk, the raised hand of a cast for the spirit and aura colours (with an aura flare).</item>
    /// <item><b>Idle life</b>: after a quiet spell the duelist strikes a pose now and then; the head and eyes find the
    /// camera.</item>
    /// </list>
    /// Runs after the Animator and the avatar's procedural idle, before the disk (890), the face rig (900) and the hair (915).
    /// </summary>
    [DefaultExecutionOrder(880)]
    public sealed class AvatarPreviewDirector : MonoBehaviour
    {
        // the Battle City poses (metres from the left shoulder: x = the duelist's right, y up, z forward; ArOpponentDuelist)
        static readonly Vector3 StandbyPos = new(-0.24f, -0.38f, 0.32f), StandbyFingers = new(-0.25f, -0.12f, 1f), StandbyBack = new(-1f, 0.15f, -0.2f);
        // the activation: forearm out toward the viewer at the chest, the plate facing them like a shield
        static readonly Vector3 ThrustPos = new(0.02f, -0.17f, 0.40f), ThrustFingers = new(0.12f, -0.35f, 1f), ThrustBack = new(0f, 0.35f, 1f);
        // after the lock (Joey's Falsebound Kingdom art, Weevil, Mai): the forearm level across the navel, zones up and
        // tipped toward the viewer, so the disk and its palette face the camera
        static readonly Vector3 LevelPos = new(0.16f, -0.24f, 0.31f), LevelFingers = new(0.8f, 0f, 0.6f), LevelBack = new(0f, 0.9f, 0.42f);
        static readonly Vector3 ElbowHint = new(-0.30f, -0.40f, -0.05f);
        const float AdultArm = 0.55f;     // metres, upper arm + forearm, the poses' scale

        OverworldAvatar3d _av;
        Animator _anim;
        Transform _cam;
        AvatarSpiritDisk _disk;
        Transform _lUpper, _lFore, _lHand, _head, _neck;
        Quaternion _lFrameInv = Quaternion.identity;
        Vector3 _headFwdLocal = Vector3.forward;
        bool _indexed;

        bool _duelist;
        float _duelT = -1f;          // seconds since the duelist pose began (-1 = not in it)
        float _armW;                 // the IK's weight now
        bool _deployed;
        float _quiet, _nextFlourish = 9f, _lastReact = -10f;
        int _flourish;
        System.Random _rng = new(1019);

        public static AvatarPreviewDirector Ensure(OverworldAvatar3d av, Transform cam)
        {
            if (av == null) return null;
            if (!av.TryGetComponent<AvatarPreviewDirector>(out var d)) d = av.gameObject.AddComponent<AvatarPreviewDirector>();
            d._av = av; d._cam = cam;
            d._disk = AvatarSpiritDisk.Ensure(av.gameObject, av.gameObject.layer);
            return d;
        }

        public AvatarSpiritDisk Disk => _disk;
        public bool Duelist => _duelist;

        /// <summary>The look changed: the disk follows its model and palettes.</summary>
        public void Wear(AvatarAppearance look) => _disk?.Wear(look);

        /// <summary>The duelist tabs open (the disk goes on and deploys) or close.</summary>
        public void SetDuelist(bool on)
        {
            if (on == _duelist) return;
            _duelist = on;
            if (on) { _duelT = 0f; _deployed = false; }
            else { _disk?.Show(false); _deployed = false; }
        }

        /// <summary>
        /// An edit landed: "face", "hair", "body", "clothes", "title", "disk", "disk_model", "aura", "spirit". Gestures are
        /// spaced (a slider drag fires every frame); the face answers every time.
        /// </summary>
        public void React(string kind)
        {
            if (!Index()) return;
            _quiet = 0f;
            var face = _av.GetComponent<AvatarFaceRig>();
            var now = Time.unscaledTime;
            var spaced = now - _lastReact > 2.2f;
            switch (kind)
            {
                case "face":
                    face?.SetMood("happy", 1.6f);
                    break;
                case "hair":
                case "body":
                case "clothes":
                    face?.SetMood("smug", 1.8f);
                    if (spaced && !_duelist) { _av.PlayGesture("Duel_HandOnHip", 2.6f); _lastReact = now; }
                    break;
                case "title":
                case "disk":
                    face?.SetMood("determined", 1.8f);
                    if (spaced) { _av.PlayGesture("Duel_FistPump"); _lastReact = now; }
                    break;
                case "disk_model":
                    face?.SetMood("determined", 2f);
                    _disk?.Replay();
                    if (_duelist) { _duelT = 0f; _deployed = false; }
                    break;
                case "aura":
                case "spirit":
                    face?.SetMood("determined", 1.8f);
                    _disk?.Flare();
                    if (spaced) { _av.PlayGesture("Duel_Cast", 2.2f); _lastReact = now; }
                    break;
            }
        }

        bool Index()
        {
            if (_av == null) return false;
            if (_indexed) return _anim != null;
            _indexed = true;
            if (!_av.TryGetComponent(out _anim) || !_anim.isHuman) { _anim = null; return false; }
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            _lUpper = B(HumanBodyBones.LeftUpperArm); _lFore = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
            _head = B(HumanBodyBones.Head); _neck = B(HumanBodyBones.Neck);
            Transform mid = B(HumanBodyBones.LeftMiddleProximal), idx = B(HumanBodyBones.LeftIndexProximal), lit = B(HumanBodyBones.LeftLittleProximal);
            if (_lHand != null && mid != null && idx != null && lit != null)
            {
                var fingers = _lHand.InverseTransformDirection(mid.position - _lHand.position).normalized;
                var thumbSide = _lHand.InverseTransformDirection(idx.position - lit.position).normalized;
                _lFrameInv = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(fingers, thumbSide).normalized, fingers));
            }
            if (_head != null) _headFwdLocal = Quaternion.Inverse(_head.rotation) * _av.transform.forward;
            return true;
        }

        void LateUpdate()
        {
            if (!Index()) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            TickDuelist(dt);
            TickIdle(dt);
            Look(dt);
        }

        // ------------------------------------------------------------------ the disk arm

        void TickDuelist(float dt)
        {
            if (_lUpper == null || _lFore == null || _lHand == null) return;
            var target = _duelist ? 1f : 0f;
            _armW = Mathf.MoveTowards(_armW, target, dt / (_duelist ? 0.4f : 0.55f));
            if (_armW <= 0.001f) { _duelT = -1f; return; }
            if (_duelist) _duelT += dt;
            var t = _duelT < 0f ? 9f : _duelT;

            var body = _av.transform;
            Vector3 right = body.right, up = body.up, facing = body.forward;
            var scale = ((_lFore.position - _lUpper.position).magnitude + (_lHand.position - _lFore.position).magnitude) / AdultArm;
            Vector3 W(Vector3 v) => right * v.x + up * v.y + facing * v.z;
            var shoulder = _lUpper.position;
            Vector3 standby = shoulder + W(StandbyPos) * scale, thrust = shoulder + W(ThrustPos) * scale;
            var standbyRot = Quaternion.LookRotation(W(StandbyBack).normalized, W(StandbyFingers).normalized);
            var thrustRot = Quaternion.LookRotation(W(ThrustBack).normalized, W(ThrustFingers).normalized);

            // standby (folded, at the hip) for 0.45 s, the snap forward (0.34 s, ease-out-back), then held while the
            // disk deploys; the disk goes on as the snap starts
            const float SnapAt = 0.45f;
            Vector3 pos; Quaternion rot;
            if (t < SnapAt) { pos = standby; rot = standbyRot; }
            else
            {
                if (!_deployed && _duelist) { _deployed = true; _disk?.Show(true); }
                var k = Mathf.Clamp01((t - SnapAt) / 0.34f);
                const float c1 = 1.2f, c3 = c1 + 1f;
                var e = 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
                var ctrl = (standby + thrust) * 0.5f + facing * (0.28f * scale);
                pos = Vector3.LerpUnclamped(Vector3.LerpUnclamped(standby, ctrl, e), Vector3.LerpUnclamped(ctrl, thrust, e), e);
                rot = Quaternion.Slerp(standbyRot, thrustRot, k * k * k * (k * (k * 6f - 15f) + 10f));
                // held rigid while the disk deploys (the V2 kit's mechanism; the classic kit arrives deployed), then the
                // forearm comes down level across the navel
                var presentAt = SnapAt + (_disk != null && _disk.Model == AvatarSpiritDisk.Classic ? 0.9f : DiskFxDriver.DeploySeconds + 0.3f);
                var u = Mathf.Clamp01((t - presentAt) / 0.6f);
                u = u * u * u * (u * (u * 6f - 15f) + 10f);
                if (u > 0f)
                {
                    var level = shoulder + W(LevelPos) * scale;
                    var levelRot = Quaternion.LookRotation(W(LevelBack).normalized, W(LevelFingers).normalized);
                    pos = Vector3.Lerp(pos, level, u);
                    rot = Quaternion.Slerp(rot, levelRot, u);
                }
                // a slow breath in the held arm once it has settled
                var held = t - presentAt - 0.6f;
                if (held > 0f) pos += up * (Mathf.Sin(held * 1.6f) * 0.006f * scale);
            }
            var hint = shoulder + W(ElbowHint) * scale;
            TwoBoneIk(_lUpper, _lFore, _lHand, pos, hint, rot * _lFrameInv, Mathf.SmoothStep(0f, 1f, _armW));
        }

        /// <summary>Analytic two-bone IK (as ArOpponentDuelist): the elbow in the hint's plane, the hand takes its rotation.</summary>
        static void TwoBoneIk(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 hint, Quaternion handRot, float w)
        {
            var a = upper.position; var b = fore.position; var c = hand.position;
            var l1 = (b - a).magnitude; var l2 = (c - b).magnitude;
            var toT = target - a; var d = Mathf.Clamp(toT.magnitude, 0.02f * (l1 + l2), (l1 + l2) * 0.999f);
            var dir = toT.normalized;
            var along = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            var h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            var bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(b - a, dir);
            bend = bend.sqrMagnitude > 1e-8f ? bend.normalized : Vector3.Cross(dir, Vector3.up).normalized;
            var elbow = a + dir * along + bend * h;
            var u0 = upper.rotation; var f0 = fore.rotation; var h0 = hand.rotation;
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * u0;
            var c1 = hand.position; var b1 = fore.position;
            fore.rotation = Quaternion.FromToRotation(c1 - b1, a + dir * d - b1) * fore.rotation;
            hand.rotation = handRot;
            if (w < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, w);
                fore.rotation = Quaternion.Slerp(f0, fore.rotation, w);
                hand.rotation = Quaternion.Slerp(h0, hand.rotation, w);
            }
        }

        // ------------------------------------------------------------------ idle life

        void TickIdle(float dt)
        {
            _quiet += dt;
            // the disk tabs hold the Battle City stance still (a body gesture over the held arm hunched the duelist over
            // the disk); elsewhere, after a quiet spell, a pose now and then, never the same twice running
            if (_duelist) { _quiet = 0f; return; }
            if (_quiet < _nextFlourish || _av.GestureNow != null) return;
            string[] pool = { "Duel_HandOnHip", "Duel_Scheme", "Duel_Reveal" };
            _flourish = (_flourish + 1 + _rng.Next(pool.Length - 1)) % pool.Length;
            _av.PlayGesture(pool[_flourish], pool[_flourish] == "Duel_HandOnHip" ? 3.2f : 0f);
            _quiet = 0f;
            _nextFlourish = 11f + (float)_rng.NextDouble() * 8f;
        }

        /// <summary>The head turns a little toward the camera (up to 22°), the eyes all the way.</summary>
        void Look(float dt)
        {
            if (_head == null || _cam == null) return;
            var face = _av.GetComponent<AvatarFaceRig>();
            if (face != null) face.LookTarget = _duelist && _deployed && _disk != null && _disk.Holder != null && _duelT < 3.2f ? _disk.Holder : _cam;
            var fwd = _head.rotation * _headFwdLocal;
            var toCam = (_cam.position - _head.position).normalized;
            if (Vector3.Angle(_av.transform.forward, toCam) > 75f) return;   // the camera is behind the turned duelist
            var turn = Quaternion.FromToRotation(fwd, toCam);
            turn.ToAngleAxis(out var ang, out var axis);
            if (ang > 180f) ang -= 360f;
            var w = 0.45f * (1f - 0.5f * _armW);
            var a = Mathf.Clamp(ang * w, -22f, 22f);
            if (Mathf.Abs(a) < 0.01f || axis.sqrMagnitude < 1e-6f) return;
            var q = Quaternion.AngleAxis(a * 0.5f, axis);
            if (_neck != null) _neck.rotation = q * _neck.rotation;
            _head.rotation = (_neck != null ? q : q * q) * _head.rotation;
        }
    }
}
