using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Body-shape following for grafted pieces that carry no blend shapes of their own (the Meshy sculpts,
    /// Tools/avatar/meshy_wardrobe.py, 2026-10-18). A sculpt keeps its full density (160k–1.9M triangles), so baking the
    /// body's 13 shape frames into every one (Tools/avatar/bake_body_morphs.py, what the built shells carry) would cost
    /// tens of megabytes per piece. Instead each sculpt vertex is tied once to its nearest body vertex in the shared rest
    /// pose (identical rig and object space) and, whenever the body wears a non-zero shape the piece does not have, the
    /// piece's vertices take that body vertex's delta — the same nearest-skin transfer the bake used, done at run time
    /// on a per-avatar mesh copy. Nothing is touched while every missing shape is at zero: the shared mesh is drawn as
    /// imported. Lives in its own file so the avatar's main file only gains the one call in Apply.
    /// </summary>
    public sealed partial class OverworldAvatar3d
    {
        sealed class FollowMap
        {
            public int[] Nearest;        // body vertex per piece vertex (rest pose)
            public Vector3[] Base;       // the piece's imported vertices
        }

        static readonly Dictionary<(Mesh piece, Mesh body), FollowMap> _followMaps = new();
        static readonly Dictionary<(Mesh body, int shape), Vector3[]> _bodyDeltas = new();
        readonly Dictionary<SkinnedMeshRenderer, (Mesh original, Mesh copy)> _followed = new();
        static readonly List<(int shape, float w)> _activeShapes = new();
        static readonly List<(int shape, float w)> _missingShapes = new();

        /// <summary>
        /// Called after the shape weights were set on every renderer: pieces lacking a shape the body wears follow the
        /// body's deltas for it; pieces that carry every active shape (the built shells, tattoo patches) are left alone.
        /// </summary>
        void FollowBodyShapes()
        {
            if (_smr == null || _smr.sharedMesh == null) return;
            var body = _smr.sharedMesh;
            _activeShapes.Clear();
            for (var s = 0; s < body.blendShapeCount; s++)
            {
                var w = _smr.GetBlendShapeWeight(s);
                if (w > 0.01f) _activeShapes.Add((s, w / 100f));
            }
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == _smr || r.sharedMesh == null) continue;
                if (r.name.StartsWith("ArgonPart_") || r.name.StartsWith("Tattoo_")) continue;   // the base's own parts and the skin patches carry the body's shapes
                var m = _followed.TryGetValue(r, out var f) ? f.original : r.sharedMesh;
                _missingShapes.Clear();
                foreach (var (s, w) in _activeShapes)
                    if (m.GetBlendShapeIndex(body.GetBlendShapeName(s)) < 0) _missingShapes.Add((s, w));
                if (_missingShapes.Count == 0)
                {
                    if (_followed.TryGetValue(r, out var done)) { r.sharedMesh = done.original; Kill(done.copy); _followed.Remove(r); }
                    continue;
                }
                if (!m.isReadable || !body.isReadable) continue;   // (the import rules make sculpts and bases readable)
                var map = FollowMapFor(m, body);
                if (map == null) continue;
                if (!_followed.TryGetValue(r, out var pair))
                {
                    var copy = Instantiate(m); copy.name = m.name + "_follow";
                    pair = (m, copy); _followed[r] = pair; r.sharedMesh = copy;
                }
                var verts = new Vector3[map.Base.Length];
                System.Array.Copy(map.Base, verts, verts.Length);
                foreach (var (s, w) in _missingShapes)
                {
                    var d = BodyDelta(body, s);
                    for (var i = 0; i < verts.Length; i++) verts[i] += d[map.Nearest[i]] * w;
                }
                pair.copy.SetVertices(verts);
                pair.copy.RecalculateBounds();
            }
        }

        static Vector3[] BodyDelta(Mesh body, int shape)
        {
            var key = (body, shape);
            if (_bodyDeltas.TryGetValue(key, out var d)) return d;
            d = new Vector3[body.vertexCount];
            body.GetBlendShapeFrameVertices(shape, body.GetBlendShapeFrameCount(shape) - 1, d, null, null);
            _bodyDeltas[key] = d;
            return d;
        }

        /// <summary>Nearest body vertex per piece vertex through a k-d tree over the body's vertices; once per piece and body.</summary>
        static FollowMap FollowMapFor(Mesh piece, Mesh body)
        {
            var key = (piece, body);
            if (_followMaps.TryGetValue(key, out var map)) return map;
            var bv = body.vertices; var pv = piece.vertices;
            if (bv.Length == 0 || pv.Length == 0) return null;
            var tree = BodyTree.For(body, bv);
            var nearest = new int[pv.Length];
            for (var i = 0; i < pv.Length; i++) nearest[i] = tree.Nearest(pv[i]);
            map = new FollowMap { Nearest = nearest, Base = pv };
            _followMaps[key] = map;
            return map;
        }

        /// <summary>
        /// A static k-d tree over one body mesh's vertices (a 2 cm grid walked ring by ring took minutes for a bandana
        /// tail 30 cm off the skin; the tree answers a nearest-vertex query in a few dozen visits whatever the distance).
        /// </summary>
        sealed class BodyTree
        {
            static readonly Dictionary<Mesh, BodyTree> _trees = new();
            Vector3[] _p; int[] _idx; // vertices and the index permutation the tree is built over
            public static BodyTree For(Mesh body, Vector3[] verts)
            {
                if (_trees.TryGetValue(body, out var t)) return t;
                t = new BodyTree { _p = verts, _idx = new int[verts.Length] };
                for (var i = 0; i < verts.Length; i++) t._idx[i] = i;
                t.Build(0, verts.Length, 0);
                _trees[body] = t;
                return t;
            }
            // in-place build: the index range [lo, hi) is partitioned around the median along axis (depth % 3)
            void Build(int lo, int hi, int depth)
            {
                if (hi - lo <= 8) return;
                var axis = depth % 3; var mid = (lo + hi) >> 1;
                Select(lo, hi - 1, mid, axis);
                Build(lo, mid, depth + 1); Build(mid + 1, hi, depth + 1);
            }
            float C(int i, int axis) { var v = _p[_idx[i]]; return axis == 0 ? v.x : axis == 1 ? v.y : v.z; }
            void Select(int lo, int hi, int k, int axis)   // quickselect: the k-th smallest along axis lands at k
            {
                while (hi > lo)
                {
                    var pivot = C((lo + hi) >> 1, axis); var i = lo; var j = hi;
                    while (i <= j)
                    {
                        while (C(i, axis) < pivot) i++;
                        while (C(j, axis) > pivot) j--;
                        if (i <= j) { (_idx[i], _idx[j]) = (_idx[j], _idx[i]); i++; j--; }
                    }
                    if (k <= j) hi = j; else if (k >= i) lo = i; else return;
                }
            }
            int _best; float _bestD;
            public int Nearest(Vector3 q)
            {
                _best = _idx[0]; _bestD = float.MaxValue;
                Search(0, _p.Length, 0, q);
                return _best;
            }
            void Search(int lo, int hi, int depth, Vector3 q)
            {
                if (hi - lo <= 8)
                {
                    for (var i = lo; i < hi; i++) { var d = (_p[_idx[i]] - q).sqrMagnitude; if (d < _bestD) { _bestD = d; _best = _idx[i]; } }
                    return;
                }
                var axis = depth % 3; var mid = (lo + hi) >> 1;
                var split = C(mid, axis); var qa = axis == 0 ? q.x : axis == 1 ? q.y : q.z;
                var dm = (_p[_idx[mid]] - q).sqrMagnitude; if (dm < _bestD) { _bestD = dm; _best = _idx[mid]; }
                var diff = qa - split;
                if (diff < 0) { Search(lo, mid, depth + 1, q); if (diff * diff < _bestD) Search(mid + 1, hi, depth + 1, q); }
                else { Search(mid + 1, hi, depth + 1, q); if (diff * diff < _bestD) Search(lo, mid, depth + 1, q); }
            }
        }

        /// <summary>A grafted piece is about to be destroyed: its mesh copy goes with it.</summary>
        void ForgetFollowed(SkinnedMeshRenderer r)
        {
            if (r == null || !_followed.TryGetValue(r, out var pair)) return;
            r.sharedMesh = pair.original;
            Kill(pair.copy);
            _followed.Remove(r);
        }

        void ReleaseFollowedMeshes()
        {
            foreach (var kv in _followed)
            {
                if (kv.Key != null) kv.Key.sharedMesh = kv.Value.original;
                Kill(kv.Value.copy);
            }
            _followed.Clear();
        }
    }
}
