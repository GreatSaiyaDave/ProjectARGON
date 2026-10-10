using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Monster states on a per-shell relief model: <b>Idle, Float, Fly, Defense, Activate, Hit</b>.
    ///
    /// These are the states the product actually asks for -- "Standing. Flying. Floating. A
    /// defense pose. Effect activation attacks. Reactions." -- and they are reachable on today's
    /// 2.5D assets without waiting for Stage 3, because every one of them is a RIGID TRANSFORM
    /// PER SHELL. A wing beat is the wing shell hinging about its inner edge; a float is the root
    /// bobbing; a lunge is the root driving forward with the loose shells trailing. None of it
    /// needs a deformable body.
    ///
    /// The motion envelope is not invented. `tools/tear_check.py` in ygo-recon lifts each dynamic
    /// shell through exactly these displacements and rotations and counts voids that open inside a
    /// single shell's own material; the limits below are the verified range from that test, so a
    /// pose cannot drive a shell somewhere its amodal backing was never checked. Anything past the
    /// clamp is a hole waiting to be seen.
    ///
    /// Spring chains keep running underneath: the poser moves a shell's REST transform, the chain
    /// reacts to it and collides against <see cref="ShellRig"/>'s capsule proxies, so cloth and
    /// membranes trail the motion instead of being rigidly carried by it.
    /// </summary>
    // Must settle the rest targets BEFORE the chains step on them; otherwise every pose lands a
    // frame late and a fast state change visibly snaps.
    [DefaultExecutionOrder(-50)]
    public sealed class ShellPoser : MonoBehaviour
    {
        public enum State { Idle, Float, Fly, Defense, Activate, Hit }

        // Verified envelope -- see tools/tear_check.py poses. Do NOT widen without re-running it.
        public const float MaxHingeDeg = 32f;   // yaw, about Y
        public const float MaxSweepDeg = 28f;   // sweep, about Z
        public const float MaxOffsetFrac = 0.18f;

        [Tooltip("Seconds to cross-fade between states.")]
        public float Blend = 0.22f;

        public State Current { get; private set; } = State.Idle;

        ShellRig _rig;
        Transform _root;
        float _width = 1f, _height = 1f;
        float _t, _stateAge, _blendT = 1f;
        State _from = State.Idle;

        sealed class Shell
        {
            public Transform Tr;
            public Role Role;
            public Vector3 RestPos;
            public Quaternion RestRot;
            public Vector3 Hinge;     // local-space pivot the shell rotates about
            public float Side;        // -1 left of body centre, +1 right
            public ShellSpringChain Chain;   // when set, the pose goes through the chain's rest
        }

        enum Role { Body, Head, Wing, Cape, Tail, Held, Other }

        readonly List<Shell> _shells = new();

        public static ShellPoser Attach(ShellRig rig, GlbShellLoader.Result model)
        {
            if (rig == null || model == null) return null;
            var p = rig.gameObject.AddComponent<ShellPoser>();
            p._rig = rig;
            p._root = rig.transform;
            var b = model.LocalBounds;
            p._width = Mathf.Max(b.size.x, 1e-3f);
            p._height = Mathf.Max(b.size.y, 1e-3f);
            var centreX = b.center.x;

            foreach (var part in model.Parts)
            {
                var role = RoleOf(part.Name);
                var mb = part.Mesh != null ? part.Mesh.bounds : new Bounds();
                var side = mb.center.x < centreX ? -1f : 1f;
                // A wing hinges at the edge nearest the body, a cape at its top edge, everything
                // else about its own centre. Same convention ShellSpringChain uses for its root,
                // so the chain's anchor and the pose pivot cannot disagree.
                Vector3 hinge;
                switch (role)
                {
                    case Role.Wing: hinge = new Vector3(side < 0 ? mb.max.x : mb.min.x, mb.center.y, mb.center.z); break;
                    case Role.Cape:
                    case Role.Tail: hinge = new Vector3(mb.center.x, mb.max.y, mb.center.z); break;
                    default:        hinge = mb.center; break;
                }
                p._shells.Add(new Shell
                {
                    Tr = part.Go.transform, Role = role, Side = side, Hinge = hinge,
                    Chain = part.Go.GetComponent<ShellSpringChain>(),
                    RestPos = part.Go.transform.localPosition,
                    RestRot = part.Go.transform.localRotation,
                });
            }
            return p;
        }

        public void Set(State s)
        {
            if (s == Current) return;
            _from = Current; Current = s; _blendT = 0f; _stateAge = 0f;
            // Reactions read as a kick to the loose elements, not as a new resting shape.
            if (s == State.Hit) _rig?.AddImpulse(new Vector3(0f, -1.6f, -0.9f) * _height);
            else if (s == State.Activate) _rig?.AddImpulse(new Vector3(0f, 0.9f, -1.4f) * _height);
        }

        static Role RoleOf(string n)
        {
            var s = (n ?? "").ToLowerInvariant();
            if (s.Contains("wing")) return Role.Wing;
            // hair sways like cloth; it is a dynamic shell in the manifest and must not fall to Other
            if (s.Contains("cape") || s.Contains("cloak") || s.Contains("skirt") || s.Contains("robe") || s.Contains("hair")) return Role.Cape;
            if (s.Contains("tail")) return Role.Tail;
            if (s.Contains("head") || s.Contains("helmet") || s.Contains("hat") || s.Contains("face")) return Role.Head;
            if (s.Contains("weapon") || s.Contains("sword") || s.Contains("staff") || s.Contains("spear")) return Role.Held;
            if (s.Contains("body") || s.Contains("torso")) return Role.Body;
            return Role.Other;
        }

        /// <summary>Pose for one shell in one state: local offset (fractions of asset size) and
        /// hinge rotation in degrees. Pure function of state, role, side and time.</summary>
        /// <param name="sweep">degrees about Z -- a wing beating up and down in the image plane</param>
        /// <param name="yaw">degrees about Y -- a wing folding toward the viewer</param>
        static void Sample(State st, Role role, float side, float t, float age,
                           out Vector2 off, out float sweep, out float yaw)
        {
            off = Vector2.zero; sweep = 0f; yaw = 0f;
            switch (st)
            {
                case State.Idle:
                    // a breath, not an animation: everything stays inside a couple of percent
                    off.y = Mathf.Sin(t * 1.1f) * 0.012f;
                    if (role == Role.Wing) { sweep = Mathf.Sin(t * 0.9f + side) * 3f * side; yaw = Mathf.Sin(t * 0.9f) * 2f; }
                    if (role == Role.Cape || role == Role.Tail) sweep = Mathf.Sin(t * 0.7f) * 4f;
                    break;

                case State.Float:
                    off.y = Mathf.Sin(t * 1.5f) * 0.045f;
                    off.x = Mathf.Sin(t * 0.6f) * 0.010f;
                    if (role == Role.Wing)
                    {
                        sweep = Mathf.Sin(t * 1.5f + Mathf.PI * 0.35f) * 9f * side;
                        yaw = Mathf.Sin(t * 1.5f) * 4f * -side;
                    }
                    if (role == Role.Cape || role == Role.Tail) sweep = Mathf.Sin(t * 1.2f) * 7f;
                    break;

                case State.Fly:
                    // A beat is mostly an up-and-down SWEEP in the image plane; the yaw only adds
                    // the foreshortening that sells the downstroke. Yaw alone reads as the wing
                    // folding shut, which is not flight.
                    // The body rises AFTER the downstroke -- that lag is what makes it read as lift
                    // rather than as a shell wobbling on its own.
                    var beat = t * 3.4f;
                    if (role == Role.Wing)
                    {
                        sweep = Mathf.Sin(beat) * 26f * side;
                        yaw = Mathf.Max(0f, Mathf.Sin(beat)) * 10f * -side;
                    }
                    off.y = Mathf.Sin(beat - 0.9f) * 0.055f;
                    if (role == Role.Body || role == Role.Head) off.y += 0.02f;
                    if (role == Role.Cape || role == Role.Tail) sweep = Mathf.Sin(beat - 1.3f) * 12f;
                    break;

                case State.Defense:
                    // braced and closed up: wings drawn down and in, weight dropped, head tucked
                    off.y = -0.030f + Mathf.Sin(t * 0.9f) * 0.006f;
                    if (role == Role.Wing) { sweep = -10f * side; yaw = 16f * side; }
                    if (role == Role.Head) off.y -= 0.012f;
                    if (role == Role.Held) { off.x = -0.045f * side; sweep = -12f * side; }
                    if (role == Role.Cape || role == Role.Tail) sweep = -6f;
                    break;

                case State.Activate:
                    // one anticipation dip, then a surge that settles -- a beat, not a loop
                    var a = Mathf.Clamp01(age / 0.75f);
                    var surge = Mathf.Sin(a * Mathf.PI) * (1f - a * 0.35f);
                    off.y = Mathf.Lerp(-0.02f, 0f, Mathf.Clamp01(a * 4f)) + surge * 0.05f;
                    if (role == Role.Wing) { sweep = surge * 22f * side; yaw = surge * 12f * -side; }
                    if (role == Role.Held) { off.y += surge * 0.06f; sweep = surge * -18f * side; }
                    if (role == Role.Cape || role == Role.Tail) sweep = surge * 14f;
                    break;

                case State.Hit:
                    // recoil away from the camera, decaying; chains carry the follow-through
                    var d = Mathf.Exp(-age * 5.5f);
                    off.y = -0.035f * d * Mathf.Cos(age * 26f);
                    off.x = 0.022f * d * Mathf.Cos(age * 21f);
                    if (role == Role.Wing) { sweep = d * 10f * -side; yaw = d * 8f * side; }
                    if (role == Role.Head) off.y -= 0.012f * d;
                    break;
            }
        }

        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>Advance the poser by <paramref name="deltaTime"/>. Also callable from editor
        /// smoke tests, where LateUpdate does not run.</summary>
        public void Step(float deltaTime)
        {
            if (_shells.Count == 0) return;
            var dt = deltaTime;
            _t += dt; _stateAge += dt;
            if (_blendT < 1f) _blendT = Mathf.Min(1f, _blendT + dt / Mathf.Max(Blend, 1e-3f));
            var k = Mathf.SmoothStep(0f, 1f, _blendT);

            // The proxies were built at the rest pose. Carry them with the body so a chain keeps
            // colliding against where the body actually is, not where it started.
            Sample(_from, Role.Body, 1f, _t, _stateAge, out var bA, out _, out _);
            Sample(Current, Role.Body, 1f, _t, _stateAge, out var bB, out _, out _);
            var bodyMove = Vector2.Lerp(bA, bB, k);
            _rig?.SetProxyOffset(new Vector3(bodyMove.x * _width, bodyMove.y * _height, 0f));

            for (var i = 0; i < _shells.Count; i++)
            {
                var sh = _shells[i];
                Sample(_from, sh.Role, sh.Side, _t, _stateAge, out var offA, out var swA, out var yawA);
                Sample(Current, sh.Role, sh.Side, _t, _stateAge, out var offB, out var swB, out var yawB);
                var off = Vector2.Lerp(offA, offB, k);
                var sweep = Mathf.Lerp(swA, swB, k);
                var yaw = Mathf.Lerp(yawA, yawB, k);

                // Clamp to the envelope tear_check.py actually verified, on BOTH axes. A pose that
                // wants more is asking to be drawn somewhere its backing was never checked.
                sweep = Mathf.Clamp(sweep, -MaxSweepDeg, MaxSweepDeg);
                yaw = Mathf.Clamp(yaw, -MaxHingeDeg, MaxHingeDeg);
                off.x = Mathf.Clamp(off.x, -MaxOffsetFrac, MaxOffsetFrac);
                off.y = Mathf.Clamp(off.y, -MaxOffsetFrac, MaxOffsetFrac);

                var rot = Quaternion.AngleAxis(sweep, Vector3.forward) * Quaternion.AngleAxis(yaw, Vector3.up);
                var move = new Vector3(off.x * _width, off.y * _height, 0f);

                if (sh.Chain != null)
                {
                    // Spring-boned: the mesh follows its bones, not this transform. Push the pose
                    // into the chain's rest targets and let the springs trail it, which is what
                    // gives cloth and membranes their lag instead of carrying them rigidly.
                    sh.Chain.SetPose(move, rot);
                    continue;
                }
                // rotate about the shell's hinge, not its origin, so a wing pivots at its root
                var pivoted = rot * (-sh.Hinge) + sh.Hinge;
                sh.Tr.localRotation = sh.RestRot * rot;
                sh.Tr.localPosition = sh.RestPos + pivoted + move;
            }
        }
    }
}
