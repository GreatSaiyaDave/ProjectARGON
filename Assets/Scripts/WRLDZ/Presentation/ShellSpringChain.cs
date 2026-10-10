using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Capsule collision proxy in the rig root's local space. Physics only ever collides
    /// against proxies explicitly assigned to a chain — never "the scene" or the render mesh
    /// (master art guide, Stage 4).
    /// </summary>
    public sealed class ShellCapsule
    {
        public string Name;
        public Vector3 A;      // segment endpoints, root-local
        public Vector3 B;
        public float Radius;
        /// <summary>Rigid displacement applied by <see cref="ShellPoser"/> when the body it
        /// belongs to moves. Without it the proxies sit at the build-time rest pose while the
        /// monster bobs, and the surface a chain collides against drifts away from the surface
        /// it is supposed to be hugging.</summary>
        public Vector3 Offset;

        /// <summary>Push a point out of the capsule; returns true when it was inside.</summary>
        public bool Resolve(ref Vector3 p, float pointRadius)
        {
            var a = A + Offset;
            var ab = B - A;
            var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            var closest = a + ab * t;
            var d = p - closest;
            var minDist = Radius + pointRadius;
            var dist = d.magnitude;
            if (dist >= minDist) return false;
            // Degenerate (point exactly on the axis): push toward the camera side.
            var n = dist > 1e-5f ? d / dist : Vector3.forward;
            p = closest + n * minDist;
            return true;
        }
    }

    /// <summary>
    /// Spring-bone chain for a loose relief shell (cape, skirt, wings, hair). The shell's
    /// MeshRenderer is converted to a SkinnedMeshRenderer driven by N generated bones running
    /// from the attachment edge to the tip; each bone tip is a particle with stiffness toward
    /// its rest pose, damping, gravity, and a collision radius against the assigned capsules.
    /// This is the master guide's "spring-bone / joint-chain physics — default choice for
    /// hair, capes, wing feathers" — cheap enough for the mobile AR budget.
    /// </summary>
    public sealed class ShellSpringChain : MonoBehaviour
    {
        public enum Attach { Top, Inner, Bottom, Left, Right }

        [Range(0f, 1f)] public float Stiffness = 0.55f;
        [Range(0f, 1f)] public float Damping = 0.18f;
        public float Gravity = 0.9f;
        public float ParticleRadius = 0.06f;
        public Vector3 Wind;                       // root-local, units/s^2
        public int Iterations = 2;

        readonly List<ShellCapsule> _proxies = new();
        Transform _root;                           // rig root (collision space)
        Transform[] _bones;
        Vector3[] _restLocal;                      // rest position of each bone, root-local
        Vector3[] _pos;                            // simulated particle positions, root-local
        Vector3[] _prev;
        float[] _segLen;
        Vector3 _up;                               // rest "up" for LookRotation
        bool _built;

        public IReadOnlyList<ShellCapsule> Proxies => _proxies;
        public int BoneCount => _bones?.Length ?? 0;

        /// <summary>
        /// Convert <paramref name="shell"/> (MeshFilter + MeshRenderer, child of <paramref name="rigRoot"/>)
        /// into a skinned chain of <paramref name="boneCount"/> bones hanging from <paramref name="attach"/>.
        /// <paramref name="bodyCenterX"/> disambiguates <see cref="Attach.Inner"/> (wings).
        /// </summary>
        public static ShellSpringChain Build(GameObject shell, Transform rigRoot, Attach attach, int boneCount,
            float bodyCenterX, IEnumerable<ShellCapsule> proxies)
        {
            var mf = shell.GetComponent<MeshFilter>();
            var mr = shell.GetComponent<MeshRenderer>();
            if (mf == null || mr == null || mf.sharedMesh == null) return null;
            boneCount = Mathf.Clamp(boneCount, 2, 16);

            var mesh = Object.Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + "_skinned";
            var b = mesh.bounds;
            Vector3 rootPt, tipPt;
            var inner = attach;
            if (inner == Attach.Inner) inner = b.center.x < bodyCenterX ? Attach.Right : Attach.Left;
            switch (inner)
            {
                case Attach.Bottom: rootPt = new Vector3(b.center.x, b.min.y, b.center.z); tipPt = new Vector3(b.center.x, b.max.y, b.center.z); break;
                case Attach.Left:   rootPt = new Vector3(b.min.x, b.center.y, b.center.z); tipPt = new Vector3(b.max.x, b.center.y, b.center.z); break;
                case Attach.Right:  rootPt = new Vector3(b.max.x, b.center.y, b.center.z); tipPt = new Vector3(b.min.x, b.center.y, b.center.z); break;
                default:            rootPt = new Vector3(b.center.x, b.max.y, b.center.z); tipPt = new Vector3(b.center.x, b.min.y, b.center.z); break;
            }
            // The attach END is a body-plan fact (a cape hangs from the shoulders, a wing hinges
            // at its inner edge), but the TIP is not: taking it from the opposite face of the
            // bounding box forces the bone axis onto a cardinal direction. A wing that sweeps
            // diagonally then gets bones running straight across it, and because skin weights bin
            // vertices along that axis, a whole vertical slice of membrane moves as one -- the
            // motion reads as a horizontal accordion rather than a wing beat. Put the tip at the
            // farthest actual vertex instead, so the chain follows the real spar.
            {
                var vv = mesh.vertices;
                var stride = Mathf.Max(1, vv.Length / 3000);
                var best = -1f;
                for (var i = 0; i < vv.Length; i += stride)
                {
                    var d2 = (vv[i] - rootPt).sqrMagnitude;
                    if (d2 > best) { best = d2; tipPt = vv[i]; }
                }
            }
            var axis = tipPt - rootPt;
            var axisLen = Mathf.Max(axis.magnitude, 1e-4f);
            var axisN = axis / axisLen;

            // bones along the axis, in the shell's local space
            var bones = new Transform[boneCount];
            var bind = new Matrix4x4[boneCount];
            var chain = new ShellSpringChain[1];
            Transform parent = shell.transform;
            for (var i = 0; i < boneCount; i++)
            {
                var bt = new GameObject("bone" + i).transform;
                bt.SetParent(parent, false);
                var worldOfBoneLocal = rootPt + axisN * (axisLen * i / (boneCount - 1));
                bt.position = shell.transform.TransformPoint(worldOfBoneLocal);
                bt.rotation = shell.transform.rotation;
                bones[i] = bt;
                parent = bt;
            }
            for (var i = 0; i < boneCount; i++)
                bind[i] = bones[i].worldToLocalMatrix * shell.transform.localToWorldMatrix;

            // linear skin weights by distance along the axis (two nearest bones)
            var verts = mesh.vertices;
            var weights = new BoneWeight[verts.Length];
            for (var v = 0; v < verts.Length; v++)
            {
                var t = Mathf.Clamp01(Vector3.Dot(verts[v] - rootPt, axisN) / axisLen) * (boneCount - 1);
                var i0 = Mathf.Clamp(Mathf.FloorToInt(t), 0, boneCount - 1);
                var i1 = Mathf.Min(i0 + 1, boneCount - 1);
                var f = t - i0;
                weights[v] = new BoneWeight { boneIndex0 = i0, weight0 = 1f - f, boneIndex1 = i1, weight1 = f };
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bind;

            var mat = mr.sharedMaterial;
            // Destroy() is deferred and refused in edit mode (editor smoke tests, tooling);
            // DestroyImmediate is required there and equivalent at runtime setup time.
            if (Application.isPlaying) { Object.Destroy(mr); Object.Destroy(mf); }
            else { Object.DestroyImmediate(mr); Object.DestroyImmediate(mf); }
            var smr = shell.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = mat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            smr.updateWhenOffscreen = true;

            var c = shell.AddComponent<ShellSpringChain>();
            c._root = rigRoot;
            c._bones = bones;
            c._restLocal = new Vector3[boneCount];
            c._pos = new Vector3[boneCount];
            c._prev = new Vector3[boneCount];
            c._segLen = new float[boneCount];
            for (var i = 0; i < boneCount; i++)
            {
                c._restLocal[i] = rigRoot.InverseTransformPoint(bones[i].position);
                c._pos[i] = c._prev[i] = c._restLocal[i];
                c._segLen[i] = i == 0 ? 0f : Vector3.Distance(c._restLocal[i], c._restLocal[i - 1]);
            }
            c._up = rigRoot.InverseTransformDirection(shell.transform.up);
            if (proxies != null) c._proxies.AddRange(proxies);
            c._built = true;
            return c;
        }

        Vector3 _poseOffset;
        Quaternion _poseRot = Quaternion.identity;

        /// <summary>
        /// Drive this chain's REST pose from <see cref="ShellPoser"/>.
        ///
        /// A spring-boned shell is rendered entirely from its bones' world positions, so moving
        /// the shell GameObject's transform has no visible effect on it -- the pose would be a
        /// silent no-op on precisely the wings and capes it is meant to animate. The pose has to
        /// enter here, as a displacement of what the springs are pulling toward.
        ///
        /// The rotation is about the chain's ANCHOR particle (a wing's inner edge, a cape's top
        /// edge), which is the same hinge the poser uses, so the two cannot disagree.
        /// </summary>
        public void SetPose(Vector3 rootLocalOffset, Quaternion rotationAboutAnchor)
        {
            _poseOffset = rootLocalOffset;
            _poseRot = rotationAboutAnchor;
        }

        /// <summary>Rest position of particle <paramref name="i"/> with the current pose applied.</summary>
        Vector3 Rest(int i)
        {
            var anchor = _restLocal[0];
            return anchor + _poseRot * (_restLocal[i] - anchor) + _poseOffset;
        }

        /// <summary>Kick the chain (summon burst, attack lunge, hit) in root-local units/s.</summary>
        public void AddImpulse(Vector3 rootLocalVelocity)
        {
            if (!_built) return;
            for (var i = 1; i < _pos.Length; i++)
                _prev[i] -= rootLocalVelocity * Time.deltaTime * (i / (float)(_pos.Length - 1));
        }

        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>Advance the simulation by <paramref name="deltaTime"/> (also callable from
        /// editor smoke tests where LateUpdate does not run).</summary>
        public void Step(float deltaTime)
        {
            if (!_built) return;
            var dt = Mathf.Clamp(deltaTime, 1e-4f, 1f / 30f);
            var g = new Vector3(0f, -Gravity, 0f) + Wind;
            for (var it = 0; it < Iterations; it++)
            {
                // root follows the rig rigidly
                _pos[0] = Rest(0);
                for (var i = 1; i < _pos.Length; i++)
                {
                    // verlet with rest-pose spring
                    var vel = (_pos[i] - _prev[i]) * (1f - Damping);
                    var toRest = (Rest(i) - _pos[i]) * Stiffness;
                    _prev[i] = _pos[i];
                    _pos[i] += vel + (toRest + g * dt) * dt;
                    // keep segment length from the parent particle
                    var d = _pos[i] - _pos[i - 1];
                    var len = d.magnitude;
                    if (len > 1e-6f) _pos[i] = _pos[i - 1] + d / len * _segLen[i];
                    // explicit proxies only
                    for (var p = 0; p < _proxies.Count; p++)
                        _proxies[p].Resolve(ref _pos[i], ParticleRadius);
                }
            }
            // orient each bone toward the next particle
            for (var i = 0; i < _bones.Length; i++)
            {
                var world = _root.TransformPoint(_pos[i]);
                _bones[i].position = world;
                if (i < _bones.Length - 1)
                {
                    var next = _root.TransformPoint(_pos[i + 1]);
                    // Deliberately the UNPOSED rest direction. The bind poses were captured at
                    // the original rest, so measuring the delta from there is what carries the
                    // pose rotation into the skinning; measuring from the posed rest would cancel
                    // it and the membrane would translate without turning.
                    var restDir = _root.TransformDirection(_restLocal[i + 1] - _restLocal[i]);
                    var curDir = next - world;
                    if (curDir.sqrMagnitude > 1e-8f && restDir.sqrMagnitude > 1e-8f)
                    {
                        var delta = Quaternion.FromToRotation(restDir.normalized, curDir.normalized);
                        _bones[i].rotation = delta * _root.rotation;
                    }
                }
            }
        }

        /// <summary>Editor / test aid: particles currently inside an assigned proxy (should be 0 after a step).</summary>
        public int PenetrationCount()
        {
            if (!_built) return 0;
            var n = 0;
            for (var i = 1; i < _pos.Length; i++)
                for (var p = 0; p < _proxies.Count; p++)
                {
                    var q = _pos[i];
                    if (_proxies[p].Resolve(ref q, ParticleRadius * 0.98f)) { n++; break; }
                }
            return n;
        }

        /// <summary>Editor / test aid: max displacement of any particle from rest, root-local.</summary>
        public float MaxDisplacement()
        {
            if (!_built) return 0f;
            var m = 0f;
            for (var i = 0; i < _pos.Length; i++) m = Mathf.Max(m, Vector3.Distance(_pos[i], _restLocal[i]));
            return m;
        }
    }
}
