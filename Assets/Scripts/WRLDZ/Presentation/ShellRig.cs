using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Builds the physics rig for a per-shell monster: capsule proxies for rigid shells
    /// (body, head, helmet, boots) and spring-bone chains for loose ones (cape, skirt,
    /// wings, hair, tail), from the {id}.shells.json manifest exported next to the GLB.
    /// Without a manifest, shell names decide (cape/skirt/wing/hair/tail -> dynamic).
    /// </summary>
    public sealed class ShellRig : MonoBehaviour
    {
        [System.Serializable] public class DynamicSpec
        {
            public string shell; public string attach = "top"; public int bones = 6;
            public float stiffness = 0.55f; public float damping = 0.18f; public float gravity = 0.9f; public float radius = 0.06f;
        }
        [System.Serializable] public class RigSpec { public List<DynamicSpec> dynamic = new(); public List<string> proxies = new(); public List<string> held = new(); }
        [System.Serializable] public class Manifest { public string schema; public int card_id; public string name; public float lifesize_m; public RigSpec rig = new(); }

        public readonly List<ShellCapsule> Proxies = new();
        public readonly List<ShellSpringChain> Chains = new();
        public Transform BodyShell { get; private set; }

        static readonly string[] DefaultDynamic = { "cape", "skirt", "wing", "hair", "tail", "cloak", "scarf", "ribbon" };

        public static ShellRig Build(GlbShellLoader.Result model, Manifest manifest)
        {
            var rig = model.Root.AddComponent<ShellRig>();
            var spec = manifest?.rig ?? new RigSpec();
            var byName = new Dictionary<string, GlbShellLoader.ShellPart>();
            foreach (var p in model.Parts) byName[p.Name] = p;

            // proxies first — chains collide against them
            var proxyNames = spec.proxies.Count > 0 ? spec.proxies : DefaultProxies(byName.Keys);
            foreach (var n in proxyNames)
                if (byName.TryGetValue(n, out var part)) AddCapsulesFor(rig.Proxies, part, model.Root.transform);
            if (byName.TryGetValue("body", out var body)) rig.BodyShell = body.Go.transform;
            var bodyCenterX = rig.BodyShell != null
                ? model.Root.transform.InverseTransformPoint(rig.BodyShell.TransformPoint(byName["body"].Mesh.bounds.center)).x
                : model.LocalBounds.center.x;

            var dyn = spec.dynamic.Count > 0 ? spec.dynamic : DefaultDynamicSpecs(byName.Keys);
            foreach (var d in dyn)
            {
                if (!byName.TryGetValue(d.shell, out var part)) continue;
                var attach = ParseAttach(d.attach);
                var chain = ShellSpringChain.Build(part.Go, model.Root.transform, attach, d.bones, bodyCenterX, rig.Proxies);
                if (chain == null) continue;
                chain.Stiffness = d.stiffness; chain.Damping = d.damping; chain.Gravity = d.gravity; chain.ParticleRadius = d.radius;
                rig.Chains.Add(chain);
            }
            return rig;
        }

        /// <summary>Move every capsule proxy rigidly with the posed body.</summary>
        public void SetProxyOffset(Vector3 rootLocalOffset)
        {
            for (var i = 0; i < Proxies.Count; i++) Proxies[i].Offset = rootLocalOffset;
        }

        public void AddImpulse(Vector3 rootLocalVelocity)
        {
            foreach (var c in Chains) c.AddImpulse(rootLocalVelocity);
        }

        /// <summary>
        /// Capsule proxies "slightly larger than the visible mesh" (Stage 4), built from the
        /// mesh's own vertices rather than from its bounding box.
        ///
        /// The previous version took the radius from <c>max(size.x, size.z)</c> and always laid
        /// the axis vertically. Both are wrong for the shapes that matter most: a relief shell is
        /// thin in Z, so that max always selected the SILHOUETTE WIDTH -- a 0.6-wide wing got a
        /// 0.19 radius blob swallowing the torso it is supposed to collide against -- and a
        /// vertical axis cannot represent a wing spar, an outstretched arm or a tail at all.
        /// With no usable proxy there, Rule 2 ("nothing clips, ever") had no mechanism to enforce.
        ///
        /// Instead: find the part's principal axis in the plate plane by PCA over its vertices,
        /// bin along it, and chain the per-bin CENTROIDS into a capsule path that bends with the
        /// part. Each link's radius comes from the perpendicular spread about that link alone,
        /// floored by the shell's own Z depth so a chain cannot slip behind it. One capsule for a
        /// compact part; a path of them following the spar or the torso for a long one.
        /// </summary>
        const int MaxProxySamples = 4000;

        static void AddCapsulesFor(List<ShellCapsule> into, GlbShellLoader.ShellPart part, Transform root)
        {
            var verts = part.Mesh.vertices;
            if (verts == null || verts.Length < 8) return;
            var l2r = part.Go.transform.localToWorldMatrix;
            var stride = Mathf.Max(1, verts.Length / MaxProxySamples);

            var pts = new List<Vector3>(Mathf.Min(verts.Length, MaxProxySamples) + 1);
            var mean = Vector3.zero;
            for (var i = 0; i < verts.Length; i += stride)
            {
                var v = root.InverseTransformPoint(l2r.MultiplyPoint3x4(verts[i]));
                pts.Add(v); mean += v;
            }
            if (pts.Count < 4) return;
            mean /= pts.Count;

            // PCA in the plate plane (XY). Z is the relief direction and carries no pose
            // information, so letting it into the covariance would only add noise.
            double sxx = 0, sxy = 0, syy = 0;
            foreach (var p in pts)
            {
                double dx = p.x - mean.x, dy = p.y - mean.y;
                sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            }
            var n = pts.Count;
            sxx /= n; sxy /= n; syy /= n;
            // dominant eigenvector of the symmetric 2x2 covariance, closed form
            var tr = sxx + syy;
            var det = sxx * syy - sxy * sxy;
            var disc = System.Math.Sqrt(Mathf.Max((float)(tr * tr * 0.25 - det), 0f));
            var lam = tr * 0.5 + disc;
            Vector2 dir = Mathf.Abs((float)sxy) > 1e-9f
                ? new Vector2((float)(lam - syy), (float)sxy).normalized
                : (sxx >= syy ? Vector2.right : Vector2.up);
            var axis = new Vector3(dir.x, dir.y, 0f);

            // project onto the axis, and collect perpendicular distances
            float tMin = float.MaxValue, tMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
            var ts = new float[pts.Count];
            for (var i = 0; i < pts.Count; i++)
            {
                var d = pts[i] - mean;
                var t = Vector3.Dot(d, axis);
                ts[i] = t;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
                if (pts[i].z < zMin) zMin = pts[i].z;
                if (pts[i].z > zMax) zMax = pts[i].z;
            }
            var length = Mathf.Max(tMax - tMin, 1e-4f);
            var halfDepth = (zMax - zMin) * 0.5f;
            var midZ = (zMin + zMax) * 0.5f;

            // perpendicular spread sets the radius scale and how many links are warranted
            var perpAll = new List<float>(pts.Count);
            for (var i = 0; i < pts.Count; i++)
                perpAll.Add((pts[i] - mean - axis * ts[i]).magnitude);
            var thickness = Mathf.Max(Percentile(perpAll, 0.80f), 1e-4f);
            var bins = Mathf.Clamp(Mathf.RoundToInt(length / (thickness * 1.6f)), 1, 6);

            // Bin along the axis and chain the per-bin CENTROIDS, not the axis itself. A single
            // straight capsule cannot follow an L-shaped part -- a dragon runs neck -> torso ->
            // legs -- and a straight axis bulges outside the silhouette at the bend, shoving any
            // colliding chain into empty space. The centroid path bends with the part.
            var sum = new Vector3[bins];
            var count = new int[bins];
            var bin = new int[pts.Count];
            for (var i = 0; i < pts.Count; i++)
            {
                var f = (ts[i] - tMin) / length;
                var bIdx = Mathf.Clamp(Mathf.FloorToInt(f * bins), 0, bins - 1);
                bin[i] = bIdx; sum[bIdx] += pts[i]; count[bIdx]++;
            }
            var nodes = new List<Vector3>();
            var nodeBin = new List<int>();
            for (var b = 0; b < bins; b++)
                if (count[b] >= 3) { nodes.Add(sum[b] / count[b]); nodeBin.Add(b); }
            if (nodes.Count == 0) return;

            if (nodes.Count == 1)
            {
                var c = nodes[0];
                var dists = new List<float>(pts.Count);
                for (var i = 0; i < pts.Count; i++) dists.Add((pts[i] - c).magnitude);
                var rr = Mathf.Max(Percentile(dists, 0.80f), halfDepth) * 1.08f;
                c.z = midZ;
                into.Add(new ShellCapsule { Name = part.Name, A = c, B = c, Radius = rr });
                return;
            }

            for (var i = 0; i < nodes.Count - 1; i++)
            {
                var a = nodes[i];
                var e = nodes[i + 1];
                // Extend the END links to the part's ACTUAL extremes along the principal axis.
                // Extending by a fraction of the link length instead overshoots: where the last
                // bin is short, the final cap landed well below the feet and stopped chains
                // against a collider sitting in empty space under the monster.
                var u = e - a;
                var ul = u.magnitude;
                if (ul > 1e-9f)
                {
                    u /= ul;
                    if (i == 0)
                        a -= u * Mathf.Max(0f, Vector3.Dot(nodes[i] - mean, axis) - tMin);
                    if (i == nodes.Count - 2)
                        e += u * Mathf.Max(0f, tMax - Vector3.Dot(nodes[i + 1] - mean, axis));
                }
                // radius from the spread about THIS link only
                var seg = e - a;
                var l2 = Mathf.Max(Vector3.Dot(seg, seg), 1e-9f);
                var d2 = new List<float>();
                for (var k = 0; k < pts.Count; k++)
                {
                    if (bin[k] != nodeBin[i] && bin[k] != nodeBin[i + 1]) continue;
                    var t = Mathf.Clamp01(Vector3.Dot(pts[k] - a, seg) / l2);
                    d2.Add((pts[k] - (a + seg * t)).magnitude);
                }
                if (d2.Count < 3) continue;
                var r = Mathf.Max(Percentile(d2, 0.80f), halfDepth) * 1.08f;
                a.z = midZ; e.z = midZ;
                into.Add(new ShellCapsule
                {
                    Name = nodes.Count == 2 ? part.Name : part.Name + "#" + i,
                    A = a, B = e, Radius = r
                });
            }
        }

        static float Percentile(List<float> v, float q)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            var i = Mathf.Clamp(Mathf.RoundToInt(q * (v.Count - 1)), 0, v.Count - 1);
            return v[i];
        }

        static List<string> DefaultProxies(IEnumerable<string> names)
        {
            var l = new List<string>();
            foreach (var n in names) if (!IsDynamicName(n) && n != "sword" && n != "staff" && n != "weapon") l.Add(n);
            return l;
        }

        static List<DynamicSpec> DefaultDynamicSpecs(IEnumerable<string> names)
        {
            var l = new List<DynamicSpec>();
            foreach (var n in names)
                if (IsDynamicName(n))
                    l.Add(new DynamicSpec { shell = n, attach = n.StartsWith("wing") ? "inner" : "top" });
            return l;
        }

        static bool IsDynamicName(string n)
        {
            var low = n.ToLowerInvariant();
            foreach (var k in DefaultDynamic) if (low.Contains(k)) return true;
            return false;
        }

        static ShellSpringChain.Attach ParseAttach(string s)
        {
            switch ((s ?? "top").ToLowerInvariant())
            {
                case "inner": return ShellSpringChain.Attach.Inner;
                case "bottom": return ShellSpringChain.Attach.Bottom;
                case "left": return ShellSpringChain.Attach.Left;
                case "right": return ShellSpringChain.Attach.Right;
                default: return ShellSpringChain.Attach.Top;
            }
        }
    }
}
