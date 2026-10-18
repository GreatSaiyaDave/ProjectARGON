using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Terrain-aware standing (owner 2026-10-18: "The models should be realistic. Create a standing animation that adjusts
    /// appropriately to the terrain it is on. This will be important during duels."). Runs right after the Animator,
    /// before any arm / disk IK, on any humanoid body:
    /// <list type="number">
    /// <item>each foot's SOLE is measured once per outfit from the meshes themselves (the body's foot, or the shoe / boot
    /// worn over it): the heel and toe contacts and the sole plane in foot-bone space. A flat shoe stands flat, a heeled
    /// boot stands pitched on heel and toe, a bare foot on heel and ball;</item>
    /// <item>every frame each foot is cast onto what is under it (the map's ground, an AR plane or scanned mesh, a table,
    /// a step) — or, with nothing under it, the body's own floor plane;</item>
    /// <item>a planted foot turns its sole onto that surface (pitch and roll follow the slope, the heading stays the
    /// animation's), a lifted foot (walk swing) keeps the animation's lift above the surface;</item>
    /// <item>the hips drop (or rise a little) so the lower foot can reach, and each leg is solved with two-bone IK
    /// keeping the animation's knee direction.</item>
    /// </list>
    /// This is also what takes the bases off tiptoe: their rest pose holds the ankle ~15 cm up with the foot pointed,
    /// so every humanoid clip stood them on their toes; with the soles planted the ankle sits where the sole puts it.
    /// Nothing about a model's mesh or textures changes — only the leg and foot bones are posed.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    [DisallowMultipleComponent]
    public sealed class AvatarFootGrounding : MonoBehaviour
    {
        /// <summary>0 = the animation as authored, 1 = fully grounded (eased by whoever changes it).</summary>
        [Range(0f, 1f)] public float Weight = 1f;

        /// <summary>What counts as ground: everything but UI and Ignore Raycast (the body itself is skipped by parentage).</summary>
        public LayerMask GroundMask = ~0;

        /// <summary>Cast for the surface under each foot. Off = the body's own floor plane only (a body inside a UI canvas).</summary>
        public bool Raycast = true;

        /// <summary>How far the hips may drop to let a foot reach lower ground (m, at stature 1), and rise.</summary>
        public float MaxDrop = 0.45f, MaxRise = 0.20f;

        /// <summary>Steepest surface a sole lies on (degrees); steeper hits are treated as this slope.</summary>
        public float MaxSlope = 40f;

        /// <summary>Pelvis roll toward the lower foot, as a share of the feet's height difference angle (0 = none), capped at <see cref="MaxPelvisRoll"/>°.</summary>
        public float PelvisRoll = 0.4f, MaxPelvisRoll = 6f;

        Animator _anim;
        Transform _hips;
        readonly Leg _left = new(), _right = new();
        bool _ready;
        float _hipsOffset, _pelvisRoll;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        sealed class Leg
        {
            public Transform Upper, Lower, Foot, Toes;
            public bool HasSole;
            public Vector3 SoleNormal;          // foot-local, pointing out of the sole (down when planted)
            public Vector3[] Contacts;          // foot-local sole contact points: heel, toe, toe inner, toe outer
            public float GroundRel;             // smoothed ground height above the body's floor plane (m)
            public Vector3 Normal = Vector3.up; // smoothed ground normal (world)
            public bool Primed;
        }

        // ------------------------------------------------------------------ setup

        public static AvatarFootGrounding Ensure(GameObject body)
        {
            if (body == null) return null;
            var g = body.GetComponent<AvatarFootGrounding>();
            if (g == null) g = body.AddComponent<AvatarFootGrounding>();
            return g;
        }

        void Awake() => GroundMask = ~((1 << 2) | (1 << 5));

        bool Bind()
        {
            _anim = GetComponent<Animator>();
            if (_anim == null || !_anim.isHuman) return false;
            _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            _left.Upper = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _left.Lower = _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _left.Foot = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _left.Toes = _anim.GetBoneTransform(HumanBodyBones.LeftToes);
            _right.Upper = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _right.Lower = _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _right.Foot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            _right.Toes = _anim.GetBoneTransform(HumanBodyBones.RightToes);
            return _hips != null && _left.Upper && _left.Lower && _left.Foot && _right.Upper && _right.Lower && _right.Foot;
        }

        /// <summary>
        /// Measure both soles from the skinned meshes now on the body (call after the shoes / body change). Points are
        /// taken in their bone's bind space, so the measurement is the same in any pose.
        /// </summary>
        public void RebuildSoles()
        {
            _ready = Bind();
            if (!_ready) return;
            var lp = new List<Vector3>(4096);
            var rp = new List<Vector3>(4096);
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (smr == null || !smr.enabled || smr.sharedMesh == null) continue;
                // the body's own feet, a shoe / boot graft, the Meshy body's boots, a one-piece outfit: nothing else
                // reaches the sole (and a 1.9M-triangle coat would cost a second to read for nothing)
                var n = smr.gameObject.name;
                if (n.StartsWith("ArgonBody")) _bodySmr = smr;
                if (!(n.StartsWith("ArgonBody") || n.StartsWith("ArgonCloth_shoes") || n.StartsWith("ArgonPart_Shoes")
                      || n.StartsWith("ArgonPart_Body") || n.StartsWith("ArgonOutfit"))) continue;
                Collect(smr, _left, lp);
                Collect(smr, _right, rp);
            }
            Fit(_left, lp);
            Fit(_right, rp);
            _left.Primed = _right.Primed = false;
            StandingDrop = MeasureStandingDrop();
        }

        /// <summary>
        /// How much lower the body stands on flat soles than in its rest pose, in metres per unit of body scale (the
        /// stature calibration assumed the rest pose; the body is scaled up by this so "175 cm" is the standing height).
        /// </summary>
        public float StandingDrop { get; private set; }

        float MeasureStandingDrop()
        {
            if (!_left.HasSole || !_right.HasSole) return 0f;
            var s = Mathf.Max(0.2f, transform.lossyScale.y);
            var up = transform.up;
            var drop = 0f; var n = 0;
            foreach (var leg in new[] { _left, _right })
            {
                // the ankle's rest height from the body's bind pose (the body renderer's bindpose for the foot bone)
                var smr = _bodySmr;
                if (smr == null || smr.sharedMesh == null) return 0f;
                var bones = smr.bones; var fi = System.Array.IndexOf(bones, leg.Foot);
                if (fi < 0 || smr.sharedMesh.bindposes.Length <= fi) continue;
                var restAnkle = smr.transform.TransformPoint(smr.sharedMesh.bindposes[fi].inverse.MultiplyPoint3x4(Vector3.zero));
                var restH = Vector3.Dot(restAnkle - transform.position, up) / s;
                // the ankle's height over a level floor once the sole lies flat
                var flat = Quaternion.FromToRotation(leg.Foot.rotation * leg.SoleNormal, -up) * leg.Foot.rotation;
                var depth = 0f;
                foreach (var c in leg.Contacts) depth = Mathf.Max(depth, Vector3.Dot(flat * (c * leg.Foot.lossyScale.x), -up));
                drop += restH - depth / s; n++;
            }
            return n > 0 ? Mathf.Max(0f, drop / n) : 0f;
        }

        SkinnedMeshRenderer _bodySmr;

        /// <summary>
        /// Vertices of <paramref name="smr"/> mostly skinned to this leg's foot or toes, in foot-bone space. Read through
        /// the bind poses: a grafted piece shares this body's bone transforms, so its bind space is the foot's own.
        /// </summary>
        static void Collect(SkinnedMeshRenderer smr, Leg leg, List<Vector3> into)
        {
            var mesh = smr.sharedMesh;
            if (!mesh.isReadable) return;
            var bones = smr.bones;
            int fi = -1, ti = -1;
            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == leg.Foot) fi = i;
                else if (leg.Toes != null && bones[i] == leg.Toes) ti = i;
            }
            if (fi < 0 && ti < 0) return;
            var bind = mesh.bindposes;
            if (bind == null || bind.Length != bones.Length) return;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            if (weights == null || weights.Length != verts.Length) return;
            // the toes' bind space mapped into the foot's (rest relation of the two bones)
            var toesToFoot = fi >= 0 && ti >= 0 ? bind[fi] * bind[ti].inverse : Matrix4x4.identity;
            var step = verts.Length > 200000 ? 3 : 1;   // a 1.9M-triangle sculpt: a third of its vertices is plenty
            for (var v = 0; v < verts.Length; v += step)
            {
                var w = weights[v];
                float wf = 0f, wt = 0f;
                Acc(w.boneIndex0, w.weight0, fi, ti, ref wf, ref wt);
                Acc(w.boneIndex1, w.weight1, fi, ti, ref wf, ref wt);
                Acc(w.boneIndex2, w.weight2, fi, ti, ref wf, ref wt);
                Acc(w.boneIndex3, w.weight3, fi, ti, ref wf, ref wt);
                if (wf + wt < 0.5f) continue;
                Vector3 p;
                if (fi >= 0 && (wf >= wt || ti < 0)) p = bind[fi].MultiplyPoint3x4(verts[v]);
                else if (fi >= 0) p = toesToFoot.MultiplyPoint3x4(bind[ti].MultiplyPoint3x4(verts[v]));
                else continue;
                into.Add(p);
            }
        }

        static void Acc(int bone, float weight, int fi, int ti, ref float wf, ref float wt)
        {
            if (bone == fi) wf += weight;
            else if (bone == ti) wt += weight;
        }

        /// <summary>
        /// The sole plane of a point cloud in foot space: the lowest heel point and the lowest inner / outer toe points
        /// (lowest along the current guess of "down"), refined three times so the guess becomes the sole's own normal.
        /// </summary>
        void Fit(Leg leg, List<Vector3> pts)
        {
            leg.HasSole = false;
            if (pts.Count < 24 || leg.Foot == null) return;
            // first guesses from the live pose: down = the world's down in foot space, forward = toward the toes bone
            var d = leg.Foot.InverseTransformDirection(-transform.up).normalized;
            var f = leg.Toes != null ? leg.Foot.InverseTransformPoint(leg.Toes.position).normalized : Vector3.Cross(Vector3.right, d).normalized;
            Vector3 heel = default, toeIn = default, toeOut = default, toe = default;
            for (var iter = 0; iter < 4; iter++)
            {
                var lat = Vector3.Cross(d, f).normalized;
                f = Vector3.Cross(lat, d).normalized;
                float sMin = float.MaxValue, sMax = float.MinValue;
                foreach (var p in pts) { var s = Vector3.Dot(p, f); if (s < sMin) sMin = s; if (s > sMax) sMax = s; }
                var len = Mathf.Max(1e-4f, sMax - sMin);
                // the toe band's centre line (left / right of it = inner / outer)
                float tSum = 0f; var tN = 0;
                foreach (var p in pts) if (Vector3.Dot(p, f) > sMax - 0.35f * len) { tSum += Vector3.Dot(p, lat); tN++; }
                var tMid = tN > 0 ? tSum / tN : 0f;
                float hHeel = float.MinValue, hIn = float.MinValue, hOut = float.MinValue, hToe = float.MinValue;
                foreach (var p in pts)
                {
                    var s = Vector3.Dot(p, f);
                    var h = Vector3.Dot(p, d);
                    if (s < sMin + 0.28f * len) { if (h > hHeel) { hHeel = h; heel = p; } }
                    else if (s > sMax - 0.35f * len)
                    {
                        if (h > hToe) { hToe = h; toe = p; }
                        if (Vector3.Dot(p, lat) < tMid) { if (h > hIn) { hIn = h; toeIn = p; } }
                        else if (h > hOut) { hOut = h; toeOut = p; }
                    }
                }
                if (hHeel == float.MinValue || hIn == float.MinValue || hOut == float.MinValue) return;
                var n = Vector3.Cross(toeIn - heel, toeOut - heel);
                // bind-space units are tiny on this rig (the rig root is scaled x100, a foot is ~0.0025 units long), so
                // the degenerate test is relative to the foot's own length, never an absolute epsilon
                var nMag = n.magnitude;
                if (nMag < 1e-6f * len * len) return;
                n /= nMag;
                if (Vector3.Dot(n, d) < 0f) n = -n;
                d = n;
            }
            leg.SoleNormal = d;
            leg.Contacts = new[] { heel, toe, toeIn, toeOut };
            leg.HasSole = true;
        }

        // ------------------------------------------------------------------ per frame

        Vector3 _lastPos;

        /// <summary>Forget the eased ground (after a teleport: the feet must not slide up from the old height).</summary>
        public void Reprime() { _left.Primed = _right.Primed = false; _hipsOffset = 0f; _pelvisRoll = 0f; }

        void LateUpdate() => Ground(Time.deltaTime);

        /// <summary>One grounding pass on the pose the Animator (or an edit-mode evaluate) has just written.</summary>
        public void Ground(float deltaTime)
        {
            if (!_ready || Weight <= 0.001f || !_left.HasSole || !_right.HasSole) return;
            if ((transform.position - _lastPos).sqrMagnitude > 4f) Reprime();
            _lastPos = transform.position;
            var dt = Mathf.Clamp(deltaTime, 1e-4f, 0.05f);
            _dt = dt;
            var up = transform.up;
            var floor = transform.position;   // the body's own floor plane, when nothing is under a foot

            // animated pose (the Animator has written it this frame)
            Plan(_left, up, floor, out var aL, out var qL, out var tL, out var rotL);
            Plan(_right, up, floor, out var aR, out var qR, out var tR, out var rotR);

            // hips: let the lower foot reach (a terrain lower than the animation's floor), rise a little for a higher one
            var s = Mathf.Max(0.2f, transform.lossyScale.y);
            var dropL = Vector3.Dot(tL - aL, up);
            var dropR = Vector3.Dot(tR - aR, up);
            var want = Mathf.Clamp(Mathf.Min(dropL, dropR), -MaxDrop * s, MaxRise * s) * Weight;
            _hipsOffset = Mathf.Lerp(_hipsOffset, want, 1f - Mathf.Exp(-dt / 0.07f));
            _hips.position += up * _hipsOffset;

            // the pelvis rolls a little toward the lower foot (a stance on a slope or a step is never level-hipped)
            var across = tR - tL;
            var lateral = Vector3.ProjectOnPlane(across, up).magnitude;
            var rollWant = lateral > 0.05f * s
                ? Mathf.Clamp(Mathf.Atan2(Vector3.Dot(across, up), lateral) * Mathf.Rad2Deg * PelvisRoll, -MaxPelvisRoll, MaxPelvisRoll) * Weight
                : 0f;
            _pelvisRoll = Mathf.Lerp(_pelvisRoll, rollWant, 1f - Mathf.Exp(-dt / 0.12f));
            if (Mathf.Abs(_pelvisRoll) > 0.01f)
            {
                // about the axis from the left to the right foot's ground points crossed with up: i.e. the body's forward
                var axis = Vector3.Cross(up, Vector3.ProjectOnPlane(across, up).normalized);
                _hips.rotation = Quaternion.AngleAxis(_pelvisRoll, axis) * _hips.rotation;
            }

            Solve(_left, Vector3.Lerp(aL + up * _hipsOffset, tL, Weight), Quaternion.Slerp(qL, rotL, Weight));
            Solve(_right, Vector3.Lerp(aR + up * _hipsOffset, tR, Weight), Quaternion.Slerp(qR, rotR, Weight));
        }

        /// <summary>
        /// One foot's goal: the ankle position and foot rotation that put its sole on the surface below it (or the
        /// animation's lift above it for a swinging foot).
        /// </summary>
        float _dt = 0.016f;

        void Plan(Leg leg, Vector3 up, Vector3 floor, out Vector3 animPos, out Quaternion animRot, out Vector3 target, out Quaternion rot)
        {
            animPos = leg.Foot.position;
            animRot = leg.Foot.rotation;
            var scale = leg.Foot.lossyScale.x;   // bind-space points are in the foot's units

            // how high the animation lifts this sole above its own floor (0 when standing, > 0 in a walk's swing)
            var lift = float.MaxValue;
            foreach (var c in leg.Contacts) lift = Mathf.Min(lift, Vector3.Dot(leg.Foot.TransformPoint(c) - floor, up));
            lift = Mathf.Max(0f, lift);
            var planted = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.025f, 0.10f, lift));
            // a standing clip's toe contact is never exact (the tiptoe rest pose leaves it 1-2 cm up): the first 2 cm are
            // not a lift, a swing's 5-15 cm are
            var s0 = Mathf.Max(0.2f, transform.lossyScale.y);
            lift = Mathf.Max(0f, lift - 0.02f * s0);

            // the surface under the foot (an AR plane or scanned mesh, the map's ground, a table, a step); with nothing
            // there, the body's own floor plane. Only the height and the normal are eased (so a scanned mesh never makes
            // the feet jitter); the horizontal follows the animation at once, so nothing drags behind a walking body.
            var s = Mathf.Max(0.2f, transform.lossyScale.y);
            var probe = animPos + up * 0.6f * s;
            Vector3 n; float rel;
            if (Raycast && CastDown(probe, -up, 1.6f * s, out var hit)) { rel = Vector3.Dot(hit.point - floor, up); n = hit.normal; }
            else { rel = 0f; n = up; }
            var tilt = Vector3.Angle(up, n);
            if (tilt > MaxSlope) n = Vector3.Slerp(up, n, MaxSlope / tilt);
            if (!leg.Primed) { leg.Normal = n; leg.GroundRel = rel; leg.Primed = true; }
            var k = 1f - Mathf.Exp(-_dt / 0.06f);
            leg.Normal = Vector3.Slerp(leg.Normal, n, k).normalized;
            leg.GroundRel = Mathf.Lerp(leg.GroundRel, rel, k);
            n = leg.Normal;
            var g = animPos - up * Vector3.Dot(animPos - floor, up) + up * leg.GroundRel;

            // planted: the sole onto the surface (minimal turn: the animation's heading stays); lifted: as animated
            var soleWorld = animRot * leg.SoleNormal;
            var onGround = Quaternion.FromToRotation(soleWorld, -n) * animRot;
            rot = Quaternion.Slerp(animRot, onGround, planted);

            // ankle height above the surface: the deepest sole contact under this rotation, plus the animation's lift
            var depth = 0f;
            foreach (var c in leg.Contacts) depth = Mathf.Max(depth, Vector3.Dot(rot * (c * scale), -n));
            var onPlane = animPos - n * Vector3.Dot(animPos - g, n);
            target = onPlane + n * (depth + lift);
        }

        bool CastDown(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            var n = Physics.RaycastNonAlloc(from, dir, Hits, dist, GroundMask, QueryTriggerInteraction.Ignore);
            var found = false;
            for (var i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;   // never stand on ourselves
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            return found;
        }

        /// <summary>Two-bone IK: the ankle to <paramref name="target"/>, the knee kept in the animation's bend plane.</summary>
        void Solve(Leg leg, Vector3 target, Quaternion footRot)
        {
            var a = leg.Upper; var b = leg.Lower; var c = leg.Foot;
            var aPos = a.position; var bPos = b.position; var cPos = c.position;
            var ab = (bPos - aPos).magnitude; var bc = (cPos - bPos).magnitude;
            if (ab < 1e-4f || bc < 1e-4f) { c.rotation = footRot; return; }
            var toT = target - aPos;
            var len = Mathf.Clamp(toT.magnitude, Mathf.Abs(ab - bc) + 1e-4f, (ab + bc) * 0.9995f);
            var dir = toT.sqrMagnitude > 1e-10f ? toT.normalized : (cPos - aPos).normalized;
            // the knee's side: the animated knee's offset from the hip→ankle line (forward of the leg when it is straight)
            var bend = (bPos - aPos) - dir * Vector3.Dot(bPos - aPos, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(transform.forward, dir);   // a dead-straight leg bends forward
            bend.Normalize();
            var cosA = Mathf.Clamp((ab * ab + len * len - bc * bc) / (2f * ab * len), -1f, 1f);
            var sinA = Mathf.Sqrt(1f - cosA * cosA);
            var knee = aPos + dir * (cosA * ab) + bend * (sinA * ab);
            a.rotation = Quaternion.FromToRotation(bPos - aPos, knee - aPos) * a.rotation;
            bPos = b.position; cPos = c.position;
            var ankle = aPos + dir * len;
            b.rotation = Quaternion.FromToRotation(cPos - bPos, ankle - bPos) * b.rotation;
            c.rotation = footRot;
        }
    }
}
