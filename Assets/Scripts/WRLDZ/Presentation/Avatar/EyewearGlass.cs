using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The glass in an eyewear sculpt (2026-10-19, owner: "Please clear Weevil's lenses ... a floating asset on the lenses
    /// that moves that represents reflection/refraction"). The sculpt's painted lens texels (Tools/avatar/lens_mask.py,
    /// Clothes/lens_&lt;id&gt;_&lt;sex&gt;.png) are discarded by its AvatarRecolor material, and a WRLDZ/AvatarLensGlass material
    /// on the same renderer draws the glass there. This finds each lens once (the mesh's lens vertices, split left / right
    /// of the head) and feeds the glass every frame: the lens centres, radii and normals in world space, the head's right,
    /// and an occasional glint that sweeps across both lenses.
    /// </summary>
    [DefaultExecutionOrder(910)]   // after every pose writer and the face rig: the lenses ride the final head pose
    public sealed class EyewearGlass : MonoBehaviour
    {
        Material _glass;
        Transform _head, _body;
        SkinnedMeshRenderer _smr;
        Texture2D _mask;
        Material[] _frame;
        bool _measured;
        readonly Vector3[] _centreLocal = new Vector3[2];   // head-bone space
        readonly Vector3[] _normalLocal = new Vector3[2];
        readonly float[] _radiusLocal = new float[2];
        int _lenses;
        float _nextSweep, _sweepT = -1f;
        System.Random _rng;

        static readonly int C0 = Shader.PropertyToID("_LensC0"), C1 = Shader.PropertyToID("_LensC1");
        static readonly int N0 = Shader.PropertyToID("_LensN0"), N1 = Shader.PropertyToID("_LensN1");
        static readonly int RightId = Shader.PropertyToID("_LensRight"), SweepId = Shader.PropertyToID("_Sweep");

        /// <summary>
        /// Drive <paramref name="glass"/> on <paramref name="smr"/>; the lenses (the triangles whose UV lands on
        /// <paramref name="lensMask"/>) are measured on the first frame, once the graft has bound the renderer to this
        /// avatar's skeleton (during the graft its bones still name the source rig's). If none is found, the
        /// <paramref name="frame"/> materials paint their lens again and the glass hides. False on unusable inputs.
        /// </summary>
        public bool Bind(SkinnedMeshRenderer smr, Material glass, Texture2D lensMask, Transform head, Transform body, Material[] frame)
        {
            _glass = glass; _head = head; _body = body; _smr = smr; _mask = lensMask; _frame = frame; _lenses = 0; _measured = false;
            _rng = new System.Random(GetEntityId().GetHashCode());
            _nextSweep = 1.5f + (float)_rng.NextDouble() * 3f;
            return smr != null && smr.sharedMesh != null && lensMask != null && lensMask.isReadable && head != null && body != null;
        }

        bool Measure()
        {
            _measured = true; _lenses = 0;
            var smr = _smr; var lensMask = _mask; var head = _head; var body = _body;
            if (smr == null || smr.sharedMesh == null || lensMask == null || head == null || body == null) return false;
            // lens vertices in world space through the HEAD's bind pose: an eyewear sculpt is skinned to the Head bone
            // alone, so this is exact (BakeMesh on a grafted cloth renderer returned a mis-scaled frame: lens radii 15x
            // too large). Pieces with other weights fall back to the baked mesh.
            var mesh = smr.sharedMesh;
            var vs = mesh.vertices; var ns = mesh.normals; var uv = mesh.uv;
            if (uv == null || uv.Length != vs.Length) return false;
            var headSlot = -1;
            var bones = smr.bones;
            for (var b = 0; b < bones.Length; b++) if (bones[b] == head) { headSlot = b; break; }
            _headSlot = headSlot;
            var bind = mesh.bindposes;
            Matrix4x4 m;
            if (headSlot >= 0 && headSlot < bind.Length) m = head.localToWorldMatrix * bind[headSlot];
            else
            {
                var bake = new Mesh(); smr.BakeMesh(bake, true); vs = bake.vertices; ns = bake.normals; Destroy(bake);
                m = smr.transform.localToWorldMatrix;
            }
            var right = body.right; var fwd = body.forward;
            var pts = new[] { new List<Vector3>(), new List<Vector3>() };
            var nrm = new Vector3[2];
            // lens TRIANGLES by their centroid's texel (a vertex can sit on an island border where Meshy's atlas packs a
            // temple-arm island right against a lens island; a centroid is always inside its own island)
            var tris = mesh.triangles;
            var picked = new bool[vs.Length];
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var cu = (uv[a] + uv[b] + uv[c]) / 3f;
                if (lensMask.GetPixelBilinear(cu.x, cu.y).r < 0.5f) continue;
                picked[a] = picked[b] = picked[c] = true;
            }
            for (var i = 0; i < vs.Length; i++)
            {
                if (!picked[i]) continue;
                // relative to the head: the customizer's preview stage sits at y = -5000, and summing thousands of
                // single-precision positions that large put the centroid 40 cm off
                var w = m.MultiplyPoint3x4(vs[i]) - head.position;
                var side = Vector3.Dot(w, right) < 0f ? 0 : 1;
                pts[side].Add(w);
                if (ns != null && ns.Length == vs.Length)
                {
                    var n = m.MultiplyVector(ns[i]).normalized;
                    nrm[side] += Vector3.Dot(n, fwd) >= 0f ? n : -n;   // both faces of the glass count toward the front
                }
            }
            for (var s = 0; s < 2; s++)
            {
                if (pts[s].Count < 8) continue;
                var c = Vector3.zero; foreach (var p in pts[s]) c += p; c /= pts[s].Count;
                // drop strays (beyond 1.6x the median distance) and centre again
                var dist = new List<float>(pts[s].Count); foreach (var p in pts[s]) dist.Add((p - c).magnitude);
                dist.Sort(); var cut = dist[dist.Count / 2] * 1.6f;
                var c2 = Vector3.zero; var kept = 0;
                foreach (var p in pts[s]) if ((p - c).magnitude <= cut) { c2 += p; kept++; }
                if (kept >= 8) { c = c2 / kept; pts[s].RemoveAll(p => (p - c).magnitude > cut * 1.1f); }
                var n = nrm[s].sqrMagnitude > 1e-8f ? nrm[s].normalized : fwd;
                // the lens radius from the MEDIAN in-plane distance (a disc's median is R / sqrt 2): strays never inflate it
                var d = new List<float>(pts[s].Count);
                foreach (var p in pts[s]) { var q = p - c; d.Add((q - n * Vector3.Dot(q, n)).magnitude); }
                d.Sort();
                var r = d[d.Count / 2] * 1.4142f;
                _centreLocal[_lenses] = head.InverseTransformVector(c);   // c is head-relative
                _normalLocal[_lenses] = head.InverseTransformDirection(n);
                _radiusLocal[_lenses] = r / Mathf.Max(1e-6f, head.lossyScale.x);
                _lenses++;
            }
            if (_lenses == 1) { _centreLocal[1] = _centreLocal[0]; _normalLocal[1] = _normalLocal[0]; _radiusLocal[1] = _radiusLocal[0]; }
            if (_lenses > 0 && headSlot >= 0) SeatOnEyes(smr, headSlot);
            return _lenses > 0;
        }

        /// <summary>
        /// The sculpt was fitted to the base head by the wardrobe pipeline; its lenses can sit off the eye line (Weevil's
        /// sat 2-3 cm low, the eyes at the top rim). The owner's rule allows placing a sculpt, never reshaping it: the
        /// renderer's head binding moves to a mount under the Head bone, shifted along the head's up so the lenses' mean
        /// centre meets the eyes (the living face's eye cards when present, else the face rig's eye line).
        /// </summary>
        void SeatOnEyes(SkinnedMeshRenderer smr, int headSlot)
        {
            var head = _head; var up = _body.up;
            Vector3 eye; var found = 0; var acc = Vector3.zero;
            foreach (Transform t in head)
                if (t.name == "FaceEyeL" || t.name == "FaceEyeR") { acc += t.position - head.position; found++; }
            eye = found > 0 ? acc / found : up * (0.033f * Mathf.Max(0.05f, _body.lossyScale.y));
            var lens = (head.TransformVector(_centreLocal[0]) + head.TransformVector(_centreLocal[1])) * 0.5f;
            var delta = Vector3.Dot(eye - lens, up);
            if (Mathf.Abs(delta) < 0.003f * Mathf.Max(0.05f, _body.lossyScale.y)) return;
            var mount = new GameObject("EyewearMount").transform;
            mount.SetParent(head, false);
            mount.localPosition = head.InverseTransformVector(up * delta);
            mount.localRotation = Quaternion.identity; mount.localScale = Vector3.one;
            var bones = smr.bones; bones[headSlot] = mount; smr.bones = bones;
            var shift = head.InverseTransformVector(up * delta);
            for (var s = 0; s < 2; s++) _centreLocal[s] += shift;
            _mount = mount;
        }

        Transform _mount;
        int _headSlot = -1;

        /// <summary>Lens <paramref name="i"/> (0 or 1) in world space: centre, radius and outward normal. False until measured (HairPhysics tucks bangs behind it).</summary>
        public bool Measured => _measured && _lenses > 0;

        /// <summary>Lens <paramref name="i"/> in head-bone space (the space <see cref="TryMeshToHead"/> maps the mesh into).</summary>
        public bool TryLensLocal(int i, out Vector3 centre, out float radius, out Vector3 normal)
        {
            centre = default; normal = default; radius = 0f;
            if (!Measured || i < 0 || i > 1) return false;
            centre = _centreLocal[i]; normal = _normalLocal[i]; radius = _radiusLocal[i];
            return true;
        }

        /// <summary>The eyewear mesh and its rest mapping into head-bone space (the sculpt rides the head alone; seating included).</summary>
        public bool TryMeshToHead(out Mesh mesh, out Matrix4x4 meshToHead)
        {
            mesh = _smr != null ? _smr.sharedMesh : null; meshToHead = Matrix4x4.identity;
            if (!Measured || mesh == null || !mesh.isReadable || _headSlot < 0 || _headSlot >= mesh.bindposes.Length) return false;
            var seat = _mount != null ? Matrix4x4.TRS(_mount.localPosition, _mount.localRotation, _mount.localScale) : Matrix4x4.identity;
            meshToHead = seat * mesh.bindposes[_headSlot];
            return true;
        }

        public bool TryLens(int i, out Vector3 centre, out float radius, out Vector3 normal)
        {
            centre = default; normal = default; radius = 0f;
            if (!_measured || _lenses == 0 || _head == null || i < 0 || i > 1 || !isActiveAndEnabled) return false;
            centre = _head.TransformPoint(_centreLocal[i]);
            normal = _head.TransformDirection(_normalLocal[i]).normalized;
            radius = _radiusLocal[i] * _head.lossyScale.x;
            return true;
        }

        void OnDestroy() { if (_mount != null) Destroy(_mount.gameObject); }

        void LateUpdate() => Feed(Application.isPlaying ? Time.deltaTime : 0f);

        /// <summary>Edit-mode previews call this by hand (no Player loop).</summary>
        public void Feed(float dt)
        {
            if (_glass == null || _head == null) return;
            if (!_measured && !Measure())
            {
                // no lens found: the sculpt keeps its painted lens, the glass draws nothing
                if (_frame != null) foreach (var fm in _frame) if (fm != null && fm.HasProperty("_LensOn")) fm.SetFloat("_LensOn", 0f);
                _glass.SetFloat("_Alpha", 0f);
            }
            if (_lenses == 0) return;
            var sc = _head.lossyScale.x;
            for (var s = 0; s < 2; s++)
            {
                var c = _head.TransformPoint(_centreLocal[s]);
                var n = _head.TransformDirection(_normalLocal[s]).normalized;
                _glass.SetVector(s == 0 ? C0 : C1, new Vector4(c.x, c.y, c.z, _radiusLocal[s] * sc));
                _glass.SetVector(s == 0 ? N0 : N1, n);
            }
            _glass.SetVector(RightId, _body != null ? _body.right : _head.right);
            // a glint now and then: both bars run across the lens in 0.45 s, a touch brighter (the anime's "shine")
            if (_sweepT < 0f)
            {
                _nextSweep -= dt;
                if (_nextSweep <= 0f) { _sweepT = 0f; _nextSweep = 3.5f + (float)_rng.NextDouble() * 5f; }
            }
            var sweep = 0f; var boost = 0f;
            if (_sweepT >= 0f)
            {
                _sweepT += dt;
                var k = _sweepT / 0.45f;
                if (k >= 1f) _sweepT = -1f;
                else { sweep = Mathf.Lerp(-1.9f, 1.9f, Mathf.SmoothStep(0f, 1f, k)); boost = 0.08f * Mathf.Sin(k * Mathf.PI); }
            }
            _glass.SetVector(SweepId, new Vector4(sweep, boost, 0f, 0f));
        }
    }
}
