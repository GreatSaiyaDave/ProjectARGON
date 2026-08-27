using UnityEngine;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Builds a physical TCG card with thickness + anime edge chrome.
    /// Used for hand holos, disk zones, and as the default "card object" until
    /// per-monster 3D meshes exist. Look is 2D anime art on a 3D card body.
    /// </summary>
    public static class ArPhysicalCardBuilder
    {
        /// <summary>
        /// Spawn a physical card under <paramref name="parent"/>.
        /// Local space: card face in XY, <b>art normal = +Z</b> (hand face-on toward camera).
        /// Disk pad poses use Rx(-90) so +Z maps to zone +Y (toward viewer) — see <c>ArZoneLayout</c>.
        /// Scale parent for zone size; mesh is unit-ish width 1 × height <see cref="ArAnimePresentation.CardAspectY"/>.
        /// </summary>
        public static CardParts Build(
            Transform parent,
            int layer,
            Texture faceTex,
            Texture backTex,
            Color accent)
        {
            var root = new GameObject("PhysicalCard");
            root.transform.SetParent(parent, false);
            root.layer = layer;

            var w = 1f;
            var h = ArAnimePresentation.CardAspectY;
            var t = ArAnimePresentation.CardThickness;

            // ── Edge body (dark anime frame) ─────────────────────────
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "Edge";
            edge.transform.SetParent(root.transform, false);
            edge.transform.localPosition = Vector3.zero;
            edge.transform.localRotation = Quaternion.identity;
            edge.transform.localScale = new Vector3(w * 1.02f, h * 1.02f, t);
            ArObjectUtil.Destroy(edge.GetComponent<Collider>());
            edge.layer = layer;
            var edgeCol = Color.Lerp(new Color(0.08f, 0.10f, 0.14f, 1f), accent, 0.25f);
            var edgeMr = edge.GetComponent<MeshRenderer>();
            edgeMr.sharedMaterial = ArAnimePresentation.MakeSolid(edgeCol, "CardEdge");
            ArAnimePresentation.ConfigureHoloRenderer(edgeMr);

            // ── Front face (art) ─────────────────────────────────────
            var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
            front.name = "Face";
            front.transform.SetParent(root.transform, false);
            front.transform.localPosition = new Vector3(0f, 0f, t * 0.52f);
            front.transform.localRotation = Quaternion.identity;
            front.transform.localScale = new Vector3(w * 0.96f, h * 0.96f, 1f);
            ArObjectUtil.Destroy(front.GetComponent<Collider>());
            front.layer = layer;
            var faceMr = front.GetComponent<MeshRenderer>();
            var faceMat = ArAnimePresentation.MakeFaceMaterial(faceTex, accent, "CardFace");
            faceMr.sharedMaterial = faceMat;
            ArAnimePresentation.ConfigureHoloRenderer(faceMr);

            // ── Back face ────────────────────────────────────────────
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            back.transform.SetParent(root.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, -t * 0.52f);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            back.transform.localScale = new Vector3(w * 0.96f, h * 0.96f, 1f);
            ArObjectUtil.Destroy(back.GetComponent<Collider>());
            back.layer = layer;
            var backMr = back.GetComponent<MeshRenderer>();
            var backMat = ArAnimePresentation.MakeFaceMaterial(
                backTex != null ? backTex : faceTex, accent, "CardBack");
            backMr.sharedMaterial = backMat;
            ArAnimePresentation.ConfigureHoloRenderer(backMr);

            return new CardParts
            {
                Root = root.transform,
                FaceRenderer = faceMr,
                FaceMaterial = faceMat,
                BackRenderer = backMr,
                BackMaterial = backMat,
                EdgeRenderer = edgeMr,
                RimRenderer = null
            };
        }

        public struct CardParts
        {
            public Transform Root;
            public MeshRenderer FaceRenderer;
            public Material FaceMaterial;
            public MeshRenderer BackRenderer;
            public Material BackMaterial;
            public MeshRenderer EdgeRenderer;
            public MeshRenderer RimRenderer;

            public void SetFaceTexture(Texture tex, Color tint)
            {
                // Always write the material the renderer is actually drawing.
                // Renderer.material instantiates a copy — updating the original leaves
                // Flip Summon / toaster reveal stuck on the set card-back.
                var mat = FaceRenderer != null ? FaceRenderer.sharedMaterial : FaceMaterial;
                if (mat == null) return;
                FaceMaterial = mat;
                if (tex == null) tex = Texture2D.whiteTexture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                var col = ArAnimePresentation.Expose(Color.white);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
                if (FaceRenderer != null) FaceRenderer.sharedMaterial = mat;
            }

            public void SetBackTexture(Texture tex)
            {
                var mat = BackRenderer != null ? BackRenderer.sharedMaterial : BackMaterial;
                if (mat == null) return;
                BackMaterial = mat;
                if (tex == null) tex = Texture2D.whiteTexture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                if (BackRenderer != null) BackRenderer.sharedMaterial = mat;
            }
        }

        /// <summary>
        /// Flip Summon regression: after Unity's Renderer.material clone, SetFaceTexture
        /// must still change what the disk actually draws (not an orphaned original).
        /// </summary>
        public static string RunFaceSwapSanity()
        {
            var go = new GameObject("DiskCardFaceSwapTest");
            try
            {
                var back = Texture2D.grayTexture;
                var face = Texture2D.whiteTexture;
                var parts = Build(go.transform, 0, back, back, Color.cyan);
                // Reproduce the old Build path that instanced materials
                _ = parts.FaceRenderer != null ? parts.FaceRenderer.material : null;
                parts.SetFaceTexture(face, Color.white);
                var drawn = parts.FaceRenderer != null ? parts.FaceRenderer.sharedMaterial : null;
                Texture shown = null;
                if (drawn != null)
                {
                    if (drawn.HasProperty("_BaseMap")) shown = drawn.GetTexture("_BaseMap");
                    if (shown == null && drawn.HasProperty("_MainTex"))
                        shown = drawn.GetTexture("_MainTex");
                }

                var ok = shown == face;
                return ok
                    ? "PASS  Disk card Flip Summon face swap hits the drawn material\n"
                    : "FAIL  Disk card Flip Summon face swap — renderer still has set back\n";
            }
            finally
            {
                if (Application.isPlaying)
                    Object.Destroy(go);
                else
                    Object.DestroyImmediate(go);
            }
        }
    }
}
