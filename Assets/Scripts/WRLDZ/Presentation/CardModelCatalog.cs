using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Resolves 3D assets for cards on the hologram arena field.
    /// Lookup order:
    ///   StreamingAssets/Models/Cards/{id}.obj
    ///   StreamingAssets/Models/Cards/{id}.glb (future)
    ///   Fallback: card-art billboard plane (always works today)
    /// </summary>
    public static class CardModelCatalog
    {
        public const string CardsRelativeDir = "Models/Cards";

        static readonly Dictionary<int, Mesh> MeshCache = new();
        static Mesh _billboardQuad;

        public static string CardsDir =>
            Path.Combine(Application.streamingAssetsPath, CardsRelativeDir);

        public static bool HasDedicatedModel(int cardId)
        {
            var obj = Path.Combine(CardsDir, cardId + ".obj");
            return File.Exists(obj);
        }

        public static Mesh GetMesh(int cardId)
        {
            if (MeshCache.TryGetValue(cardId, out var cached) && cached != null)
                return cached;

            var objPath = Path.Combine(CardsDir, cardId + ".obj");
            if (File.Exists(objPath))
            {
                var m = ObjMeshLoader.LoadFromFile(objPath, "Card_" + cardId);
                if (m != null)
                {
                    // Normalize to ~1 unit tall for arena slots
                    NormalizeMeshHeight(m, 1f);
                    MeshCache[cardId] = m;
                    return m;
                }
            }

            return GetBillboardQuad();
        }

        public static bool IsBillboardFallback(Mesh mesh) =>
            mesh != null && mesh == _billboardQuad;

        static Mesh GetBillboardQuad()
        {
            if (_billboardQuad != null) return _billboardQuad;
            _billboardQuad = new Mesh
            {
                name = "CardBillboard",
                vertices = new[]
                {
                    new Vector3(-0.35f, 0f, 0f),
                    new Vector3(0.35f, 0f, 0f),
                    new Vector3(0.35f, 1f, 0f),
                    new Vector3(-0.35f, 1f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0, 0), new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(0, 1)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            _billboardQuad.RecalculateNormals();
            _billboardQuad.RecalculateBounds();
            return _billboardQuad;
        }

        static void NormalizeMeshHeight(Mesh mesh, float targetHeight)
        {
            var b = mesh.bounds;
            var h = b.size.y;
            if (h < 0.0001f) h = Mathf.Max(b.size.x, b.size.z);
            if (h < 0.0001f) return;
            var s = targetHeight / h;
            var verts = mesh.vertices;
            var c = b.center;
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i] - c;
                v *= s;
                // sit on ground
                v.y += targetHeight * 0.5f;
                verts[i] = v;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>
        /// Card / holo face material — anime unlit + AR exposure fit.
        /// Prefer Unlit over Lit: outdoor/variable AR has no reliable probes; Lit goes black.
        /// Exposure via <see cref="ArAnimePresentation"/> keeps holos readable noon→night.
        /// </summary>
        public static Material MakeArtMaterial(Sprite art, Color tint, bool emissive = true)
        {
            Texture tex = art != null && art.texture != null ? art.texture : Texture2D.whiteTexture;
            return ArAnimePresentation.MakeFaceMaterial(tex, tint, "CardModelMat");
        }
    }
}
