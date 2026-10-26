using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// The batch and quad budget's own counts (BUILD_PLAN §6 P5 step 7, SPEC §v2.6: ≤ 25 batches, ≤ 150 animated quads, ≤ 5
    /// quads / px mean in portrait). The batches come from the Profiler in Play mode (MenuV6Capture's ProfilerRecorder);
    /// these are what the rigs draw: every enabled graphic under a root, the rigs' quads (RigQuads / RigShapes / the cel
    /// layers / the dread ladder's rungs), the crack meshes' vertices, and the overdraw estimate (each enabled graphic's
    /// screen rect over the screen: an upper bound, rects not pixels).
    /// </summary>
    public static class MenuV6Budget
    {
        public struct Counts
        {
            public int Graphics, Images, RawImages, Texts, RigQuadVerts, RigShapeVerts, CelLayers, CrackVerts, DreadQuads, SmokeQuads;
            public double Overdraw;

            /// <summary>The animated quads (the rigs' per-frame meshes: 4 vertices a quad, the cel layers 1 each).</summary>
            public int AnimatedQuads => RigQuadVerts / 4 + RigShapeVerts / 4 + CelLayers + DreadQuads;
        }

        static readonly Vector3[] Corners = new Vector3[4];

        public static Counts Measure(Transform root, MenuV6Stage stage = null)
        {
            var c = new Counts();
            if (root == null) return c;
            var screen = (double)Mathf.Max(1, Screen.width) * Mathf.Max(1, Screen.height);
            double area = 0;
            foreach (var g in root.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) continue;
                var cel = g as CelSmokeGraphic;
                if (g.color.a <= .002f && cel == null) continue;
                c.Graphics++;
                // `as` + Unity's == null (never a type pattern: memory unity-fake-null-pitfall)
                var q = g as RigQuads;
                var sh = g as RigShapes;
                var n = g as CrackNetGraphic;
                if (g as Image != null) c.Images++;
                else if (g as RawImage != null) c.RawImages++;
                else if (g as Text != null) c.Texts++;
                else if (q != null) c.RigQuadVerts += q.Layer != null ? q.Layer.Q.V.Count : 0;
                else if (sh != null) c.RigShapeVerts += sh.Layer != null ? sh.Layer.S.V.Count : 0;
                else if (n != null)
                {
                    if (n.Mesh != null) c.CrackVerts += n.Mesh.V.Count;
                    else if (n.Quads != null) c.CrackVerts += n.Quads.V.Count;
                }
                else if (cel != null) c.CelLayers++;

                g.rectTransform.GetWorldCorners(Corners);
                var w = Mathf.Abs(Corners[2].x - Corners[0].x);
                var h = Mathf.Abs(Corners[2].y - Corners[0].y);
                area += Mathf.Min(w, Screen.width) * (double)Mathf.Min(h, Screen.height);
            }

            c.Overdraw = area / screen;
            if (stage != null)
            {
                if (stage.Dread != null) c.DreadQuads = stage.Dread.Quads;
                if (stage.Smoke != null) c.SmokeQuads = stage.Smoke.Quads;
            }

            return c;
        }

        public static string Json(Counts c)
        {
            var I = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"graphics\": ").Append(c.Graphics).Append(", \"images\": ").Append(c.Images).Append(", \"rawImages\": ")
                .Append(c.RawImages).Append(", \"texts\": ").Append(c.Texts).Append(", \"rigQuadVerts\": ").Append(c.RigQuadVerts)
                .Append(", \"rigShapeVerts\": ").Append(c.RigShapeVerts).Append(", \"celLayers\": ").Append(c.CelLayers)
                .Append(", \"crackVerts\": ").Append(c.CrackVerts).Append(", \"dreadQuads\": ").Append(c.DreadQuads)
                .Append(", \"smokeQuads\": ").Append(c.SmokeQuads).Append(", \"animatedQuads\": ").Append(c.AnimatedQuads)
                .Append(", \"overdrawRectsPerPx\": ").Append(c.Overdraw.ToString("0.###", I)).Append('}');
            return sb.ToString();
        }
    }
}
