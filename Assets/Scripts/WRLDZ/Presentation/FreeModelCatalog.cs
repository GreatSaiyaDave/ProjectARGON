using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Loads free CC0 3D models from StreamingAssets/Models (Quaternius, Kenney).
    /// Used for Referobot, duelists, arena props — not hand-made primitives.
    /// </summary>
    public static class FreeModelCatalog
    {
        public const string CharactersDir = "Models/Characters";
        public const string ReferobotDir = "Models/Referobot";
        public const string ArenaDir = "Models/Arena";

        static readonly Dictionary<string, Mesh> Cache = new();

        public static string StreamingRoot => Application.streamingAssetsPath;

        public static Mesh LoadCharacter(string name)
        {
            // e.g. Suit_Male, Warrior, Wizard — human height
            var rel = Path.Combine(CharactersDir, name + ".obj");
            return LoadMesh(rel, name, humanHeight: 1.7f);
        }

        public static Mesh LoadReferobot()
        {
            return LoadMesh(Path.Combine(ReferobotDir, "Referobot.obj"), "Referobot", humanHeight: 1.55f)
                   ?? LoadCharacter("Soldier_Male")
                   ?? LoadCharacter("Warrior");
        }

        public static Mesh LoadArena(string name)
        {
            // props — do not force human height
            return LoadMesh(Path.Combine(ArenaDir, name + ".obj"), name, humanHeight: -1f);
        }

        public static Mesh LoadMesh(string relativeUnderStreaming, string meshName, float humanHeight = -1f)
        {
            var key = relativeUnderStreaming + "@" + humanHeight;
            if (Cache.TryGetValue(key, out var m) && m != null)
                return m;

            var full = Path.Combine(StreamingRoot, relativeUnderStreaming);
            if (!File.Exists(full))
                full = Path.Combine(StreamingRoot, relativeUnderStreaming.Replace('\\', '/'));

            if (!File.Exists(full))
                return null;

            m = ObjMeshLoader.LoadFromFile(full, meshName);
            if (m != null)
            {
                if (humanHeight > 0f)
                    NormalizeToHeight(m, humanHeight);
                Cache[key] = m;
            }

            return m;
        }

        /// <summary>Normalize mesh so tallest axis ≈ targetHeight, feet at y=0.</summary>
        public static void NormalizeToHeight(Mesh mesh, float targetHeight)
        {
            if (mesh == null) return;
            var b = mesh.bounds;
            var h = b.size.y;
            if (h < 0.0001f) h = Mathf.Max(b.size.x, b.size.z);
            if (h < 0.0001f) return;
            var s = targetHeight / h;
            var verts = mesh.vertices;
            var minY = b.min.y;
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                v = (v - b.center) * s;
                // re-center XZ, sit on ground
                v.y = (verts[i].y - minY) * s;
                verts[i] = v;
            }

            // recenter XZ only
            mesh.vertices = verts;
            mesh.RecalculateBounds();
            var c = mesh.bounds.center;
            verts = mesh.vertices;
            for (var i = 0; i < verts.Length; i++)
            {
                verts[i].x -= c.x;
                verts[i].z -= c.z;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        public static Material MakeLitColor(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { name = "FreeModelMat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
            return mat;
        }

        public static GameObject SpawnMeshObject(string name, Mesh mesh, Color color, Transform parent, int layer)
        {
            if (mesh == null) return null;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = MakeLitColor(color);
            SetLayer(go, layer);
            return go;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject, layer);
        }
    }
}
