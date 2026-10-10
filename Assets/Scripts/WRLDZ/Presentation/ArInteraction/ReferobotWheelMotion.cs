using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Mr. Referobot rolls on one self-balancing wheel. The wheel base steers toward where he is going (it
    /// can roll backwards, so a back-pedal never turns him round) while a swivel waist keeps the upper body on
    /// the body transform's facing, so he can roll along the sideline and still watch the field. The whole
    /// robot is an inverted pendulum about the axle: it leans into acceleration, sits back to brake, leans
    /// into turns, and at rest keeps making small balance corrections with the wheel creeping under him.
    /// Drives only Root / Axle / Wheel / Hips (and a talk nod on the neck, set from rest every frame), so
    /// <see cref="ReferobotOfficiating"/> still layers gaze and signals on the spine, head and arms after it.
    /// </summary>
    sealed class ReferobotWheelMotion
    {
        const float Gravity = 9.81f;
        /// <summary>Lean spring: natural frequency (rad/s) and damping ratio of the balance controller.</summary>
        const float LeanOmega = 8f;
        const float LeanDamping = 0.6f;
        const float MaxPitch = 12f;
        const float MaxRoll = 9f;
        /// <summary>Extra forward lean held at speed (drag), degrees per m/s.</summary>
        const float DragLeanPerMps = 1.5f;
        /// <summary>Base steering rate toward the travel direction, degrees per second.</summary>
        const float SteerDegPerSecond = 270f;
        /// <summary>Below this speed (m/s) he is balancing in place and the base holds its heading.</summary>
        const float RollingAbove = 0.08f;
        const float VelocitySmoothing = 0.10f;
        const float AccelSmoothing = 0.08f;
        // Idle balance: slow pitch sway plus a faster correction, and a slower roll.
        const float IdlePitchDeg = 0.8f;
        const float IdlePitchHz = 0.45f;
        const float IdleCorrectionDeg = 0.3f;
        const float IdleCorrectionHz = 1.13f;
        const float IdleRollDeg = 0.45f;
        const float IdleRollHz = 0.31f;
        /// <summary>The wheel turns under him as he balances (degrees of wheel per degree of lean).</summary>
        const float IdleCreep = 1.6f;
        const float NodDeg = 4f;
        const float NodHz = 2.2f;

        readonly Transform _body;
        readonly Transform _root;
        readonly Transform _axle;
        readonly Transform _wheel;
        readonly Transform _waist;
        readonly Transform _neck;
        readonly Quaternion _rootRest;
        readonly Quaternion _axleRest;
        readonly Quaternion _wheelRest;
        readonly Quaternion _waistRest;
        readonly Quaternion _neckRestLocal;
        readonly float _radius;

        bool _primed;
        Vector3 _lastPos;
        Vector3 _vel;
        float _heading;
        float _lastHeading;
        float _lastSpeed;
        float _accel;
        float _pitch;
        float _pitchVel;
        float _roll;
        float _rollVel;
        float _spin;
        float _time;
        float _nodWeight;

        /// <summary>Signed ground speed along the wheel base (m/s, positive = rolling forward).</summary>
        public float Speed { get; private set; }
        /// <summary>Body lean about the axle (degrees, positive = forward) and into turns (positive = right).</summary>
        public float Pitch => _pitch;
        public float Roll => _roll;
        /// <summary>Wheel base heading relative to the body transform's facing (degrees).</summary>
        public float BaseYaw => Mathf.DeltaAngle(BodyYaw, _heading);
        /// <summary>Total wheel turn so far (degrees).</summary>
        public float WheelTurn => _spin;
        public float WheelRadius => _radius;

        float BodyYaw => _body.localEulerAngles.y;

        ReferobotWheelMotion(Transform body, Transform root, Transform axle, Transform wheel, Transform waist,
            Transform neck)
        {
            _body = body;
            _root = root;
            _axle = axle;
            _wheel = wheel;
            _waist = waist;
            _neck = neck;
            var inv = Quaternion.Inverse(body.rotation);
            _rootRest = inv * root.rotation;
            _axleRest = inv * axle.rotation;
            _wheelRest = inv * wheel.rotation;
            _waistRest = waist != null ? inv * waist.rotation : Quaternion.identity;
            _neckRestLocal = neck != null ? neck.localRotation : Quaternion.identity;
            var parent = body.parent;
            var drop = axle.position - root.position;
            _radius = Mathf.Max(0.05f, parent != null ? parent.InverseTransformVector(drop).magnitude : drop.magnitude);
            _heading = BodyYaw;
            _lastHeading = _heading;
        }

        /// <summary>The wheel rig, or null for a legged rig / the static stand-in.</summary>
        public static ReferobotWheelMotion TryCreate(Transform body)
        {
            if (body == null) return null;
            var root = FindDeep(body, "Root");
            var axle = FindDeep(body, "Axle");
            var wheel = FindDeep(body, "Wheel");
            if (root == null || axle == null || wheel == null) return null;
            return new ReferobotWheelMotion(body, root, axle, wheel, FindDeep(body, "Hips"), FindDeep(body, "neck"));
        }

        /// <summary>He was placed rather than rolled there: forget the motion so the jump reads as no speed.</summary>
        public void Settle()
        {
            _primed = false;
            _vel = Vector3.zero;
            _lastSpeed = 0f;
            _accel = 0f;
            _pitchVel = 0f;
            _rollVel = 0f;
            _heading = BodyYaw;
            _lastHeading = _heading;
        }

        /// <param name="talking">He is speaking a line: add a small nod.</param>
        public void Tick(float dt, bool talking)
        {
            if (dt <= 0f) return;
            _time += dt;
            var pos = _body.localPosition;
            if (!_primed)
            {
                _primed = true;
                _lastPos = pos;
            }

            var step = pos - _lastPos;
            step.y = 0f;
            _lastPos = pos;
            _vel = Vector3.Lerp(_vel, step / dt, 1f - Mathf.Exp(-dt / VelocitySmoothing));
            var speed = _vel.magnitude;

            // Steer the base toward the travel line, whichever way along it is nearer (it rolls backwards too).
            if (speed > RollingAbove)
            {
                var travel = Mathf.Atan2(_vel.x, _vel.z) * Mathf.Rad2Deg;
                var reverse = travel + 180f;
                var want = Mathf.Abs(Mathf.DeltaAngle(_heading, travel)) <= Mathf.Abs(Mathf.DeltaAngle(_heading, reverse))
                    ? travel
                    : reverse;
                _heading = Mathf.MoveTowardsAngle(_heading, want, SteerDegPerSecond * dt);
            }

            var headingDir = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
            Speed = Vector3.Dot(_vel, headingDir);
            var accel = (Speed - _lastSpeed) / dt;
            _lastSpeed = Speed;
            _accel = Mathf.Lerp(_accel, accel, 1f - Mathf.Exp(-dt / AccelSmoothing));
            var yawRate = Mathf.DeltaAngle(_lastHeading, _heading) * Mathf.Deg2Rad / dt;
            _lastHeading = _heading;

            // Inverted pendulum: lean into acceleration, a little forward at speed; lean into turns.
            var pitchTarget = Mathf.Clamp(Mathf.Atan2(_accel, Gravity) * Mathf.Rad2Deg + Speed * DragLeanPerMps,
                -MaxPitch, MaxPitch);
            var rollTarget = Mathf.Clamp(Mathf.Atan2(Speed * yawRate, Gravity) * Mathf.Rad2Deg, -MaxRoll, MaxRoll);
            Spring(ref _pitch, ref _pitchVel, pitchTarget, dt);
            Spring(ref _roll, ref _rollVel, rollTarget, dt);

            var still = 1f - Mathf.Clamp01(speed / 0.3f);
            var idlePitch = still * (IdlePitchDeg * Mathf.Sin(_time * IdlePitchHz * 2f * Mathf.PI) +
                                     IdleCorrectionDeg * Mathf.Sin(_time * IdleCorrectionHz * 2f * Mathf.PI + 1.3f));
            var idleRoll = still * IdleRollDeg * Mathf.Sin(_time * IdleRollHz * 2f * Mathf.PI + 0.7f);
            var pitch = _pitch + idlePitch;
            var roll = _roll + idleRoll;

            _spin += Speed / _radius * Mathf.Rad2Deg * dt;
            var wheelTurn = _spin + idlePitch * IdleCreep;
            Pose(pitch, roll, wheelTurn);

            _nodWeight = Mathf.MoveTowards(_nodWeight, talking ? 1f : 0f, dt * 4f);
            if (_neck != null)
            {
                var nod = _nodWeight * NodDeg * Mathf.Sin(_time * NodHz * 2f * Mathf.PI);
                var parent = _neck.parent;
                var axis = _waist != null ? _waist.right : _body.right;
                _neck.localRotation = parent != null
                    ? Quaternion.Inverse(parent.rotation) * Quaternion.AngleAxis(nod, axis) * parent.rotation *
                      _neckRestLocal
                    : _neckRestLocal;
            }
        }

        /// <summary>Sets Root (base yaw + roll), Axle (pitch), Wheel (spin) and the waist counter-yaw from rest.</summary>
        void Pose(float pitch, float roll, float wheelTurn)
        {
            var yaw = Quaternion.AngleAxis(BaseYaw, Vector3.up);
            var baseForward = yaw * Vector3.forward;
            var tilt = Quaternion.AngleAxis(-roll, baseForward);
            var axleAxis = tilt * (yaw * Vector3.right);
            var lean = Quaternion.AngleAxis(pitch, axleAxis);
            var body = _body.rotation;

            _root.rotation = body * (tilt * yaw * _rootRest);
            _axle.rotation = body * (lean * tilt * yaw * _axleRest);
            // The wheel is a child of the axle, which already turned it by the lean; take that back out so
            // the tyre only turns with the ground.
            _wheel.rotation = body * (Quaternion.AngleAxis(wheelTurn - pitch, axleAxis) * lean * tilt * yaw *
                                      _wheelRest);
            if (_waist != null) _waist.rotation = body * (lean * tilt * _waistRest);
        }

        static void Spring(ref float x, ref float v, float target, float dt)
        {
            var k = LeanOmega * LeanOmega;
            var c = 2f * LeanDamping * LeanOmega;
            // Sub-step so a long frame cannot blow the spring up.
            var steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.01f));
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                v += (k * (target - x) - c * v) * h;
                x += v * h;
            }
        }

        // ── Sanity checks (Editor command wrldz_referobot_checks) ───────────

        /// <summary>
        /// Loads the shipped rig and drives the wheel layer by moving the body the way ReferobotWorldBody does:
        /// the wheel turns once per 2πr rolled, he leans into a start and sits back to brake, a side-step
        /// steers the wheel base while the chest keeps facing forward, the tyre stays on the ground, and he
        /// keeps balancing (and nods while talking) when standing still. Builds and destroys a hidden rig.
        /// </summary>
        internal static string RunSanityChecks()
        {
            var sb = new System.Text.StringBuilder();
            var pass = 0;
            var fail = 0;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok) pass++;
                else fail++;
                sb.AppendLine((ok ? "PASS  " : "FAIL  ") + "Referobot wheel: " + name +
                              (ok || string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            }

            var holder = new GameObject("ReferobotWheelChecks") { hideFlags = HideFlags.HideAndDontSave };
            var baked = new Mesh();
            try
            {
                var actor = ReferobotActor.TryCreate(holder.transform, 0);
                Check("rig loads from StreamingAssets", actor != null);
                if (actor == null) return Finish();
                var body = actor.transform;
                var w = TryCreate(body);
                Check("rig is the wheel rig (Root, Axle, Wheel bones)", w != null);
                if (w == null) return Finish();
                Check("wheel radius is about 0.25 m", Mathf.Abs(w.WheelRadius - 0.253f) < 0.02f,
                    $"r {w.WheelRadius:0.000} m");
                var smr = actor.GetComponentInChildren<SkinnedMeshRenderer>();
                var spine = FindDeep(body, "Spine");
                var head = FindDeep(body, "Head");
                const float dt = 1f / 60f;

                float Lowest()
                {
                    smr.BakeMesh(baked, true);
                    var min = float.MaxValue;
                    var verts = baked.vertices;
                    for (var i = 0; i < verts.Length; i++)
                        min = Mathf.Min(min, body.InverseTransformPoint(smr.transform.TransformPoint(verts[i])).y);
                    return min;
                }

                // Standing: balance wobble, no drift, tyre on the ground.
                var axleStart = w._axle.rotation;
                var wobble = 0f;
                for (var i = 0; i < 180; i++)
                {
                    w.Tick(dt, false);
                    wobble = Mathf.Max(wobble, Quaternion.Angle(axleStart, w._axle.rotation));
                }

                Check("standing still he keeps balancing (small wobble)", wobble > 0.2f && wobble < 3f,
                    $"wobble {wobble:0.00}°");
                Check("standing still does not roll", Mathf.Abs(w.Speed) < 0.01f, $"speed {w.Speed:0.000} m/s");
                var restLow = Lowest();
                Check("tyre sits on the ground at rest", Mathf.Abs(restLow) < 0.02f, $"lowest {restLow:0.000} m");
                var spineRest = Quaternion.Inverse(body.rotation) * spine.rotation;
                var headRest = body.InverseTransformPoint(head.position);

                // Roll forward one wheel circumference, then stop.
                const float v = 0.8f;
                var distance = 2f * Mathf.PI * w.WheelRadius;
                var spin0 = w.WheelTurn;
                var startLean = 0f;
                var headForward = 0f;
                var travelled = 0f;
                var t = 0f;
                while (travelled < distance - 1e-4f)
                {
                    var step = Mathf.Min(v * dt, distance - travelled);
                    body.localPosition += body.forward * step;
                    travelled += step;
                    w.Tick(dt, false);
                    t += dt;
                    if (t < 0.35f)
                    {
                        if (w.Pitch > startLean)
                        {
                            startLean = w.Pitch;
                            headForward = body.InverseTransformPoint(head.position).z - headRest.z;
                        }
                    }
                }

                var brakeLean = 0f;
                var lowestLeaning = float.MaxValue;
                for (var i = 0; i < 120; i++)
                {
                    w.Tick(dt, false);
                    brakeLean = Mathf.Min(brakeLean, w.Pitch);
                    if (i % 10 == 0) lowestLeaning = Mathf.Min(lowestLeaning, Lowest());
                }

                var turned = w.WheelTurn - spin0;
                Check("rolling 2πr turns the wheel once", Mathf.Abs(turned - 360f) < 12f, $"{turned:0.0}°");
                Check("he leans forward into a start", startLean > 1f, $"max pitch {startLean:0.0}°");
                Check("a forward lean carries the head forward", headForward > 0.01f, $"head +{headForward:0.000} m");
                Check("he sits back to brake", brakeLean < -1f, $"min pitch {brakeLean:0.0}°");
                Check("the tyre stays on the ground while leaning", lowestLeaning > -0.02f,
                    $"lowest {lowestLeaning:0.000} m");

                // Side-step along the sideline, still facing the field.
                w.Settle();
                for (var i = 0; i < 60; i++)
                {
                    body.localPosition += body.right * (0.7f * dt);
                    w.Tick(dt, false);
                }

                var chest = Quaternion.Inverse(body.rotation) * spine.rotation * Quaternion.Inverse(spineRest);
                var chestYaw = Vector3.SignedAngle(Vector3.forward,
                    Vector3.ProjectOnPlane(chest * Vector3.forward, Vector3.up), Vector3.up);
                Check("a side-step steers the wheel base sideways", Mathf.Abs(Mathf.Abs(w.BaseYaw) - 90f) < 10f,
                    $"base yaw {w.BaseYaw:0.0}°");
                Check("the chest keeps facing the field while side-stepping", Mathf.Abs(chestYaw) < 5f,
                    $"chest yaw {chestYaw:0.0}°");

                // Talking: the neck nods (and settles again afterwards).
                var neckRest = w._neckRestLocal;
                var nod = 0f;
                for (var i = 0; i < 60; i++)
                {
                    w.Tick(dt, true);
                    nod = Mathf.Max(nod, Quaternion.Angle(neckRest, w._neck.localRotation));
                }

                for (var i = 0; i < 60; i++) w.Tick(dt, false);
                Check("he nods while talking", nod > 1f, $"nod {nod:0.0}°");
                Check("the nod settles when he stops talking",
                    Quaternion.Angle(neckRest, w._neck.localRotation) < 0.2f,
                    $"{Quaternion.Angle(neckRest, w._neck.localRotation):0.00}°");
            }
            catch (System.Exception ex)
            {
                Check("checks ran without an exception", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(holder);
            }

            return Finish();

            string Finish()
            {
                sb.AppendLine($"--- Referobot wheel: {pass} passed, {fail} failed ---");
                return sb.ToString();
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }
    }
}
