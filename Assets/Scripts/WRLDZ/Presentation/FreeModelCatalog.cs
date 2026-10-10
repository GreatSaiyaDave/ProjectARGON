using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Loads runtime meshes from StreamingAssets/Models.
    /// Referobot is the portrait mesh. Duelists and props still use the CC0 packs.
    /// </summary>
    public static class FreeModelCatalog
    {
        public const string CharactersDir = "Models/Characters";
        public const string ReferobotDir = "Models/Referobot";
        public const string ArenaDir = "Models/Arena";

        static readonly Dictionary<string, Mesh> Cache = new();
        static readonly Dictionary<string, Texture2D> TexCache = new();

        /// <summary>StreamingAssets root for logs only (a jar: URL on Android). Reads go through <see cref="StreamingFiles"/>.</summary>
        public static string StreamingRoot => StreamingFiles.Root;

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

            // StreamingFiles normalises '\\' → '/', which covered the old second File.Exists try.
            if (!StreamingFiles.Exists(relativeUnderStreaming))
                return null;

            m = ObjMeshLoader.LoadFromStreaming(relativeUnderStreaming, meshName);
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
            var shader = ArAnimePresentation.LitShader();
            var mat = new Material(shader) { name = "FreeModelMat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
            return mat;
        }

        public static Material MakeAlbedo(Texture2D tex, float metallic = 0.08f, float smoothness = 0.4f)
        {
            var shader = ArAnimePresentation.LitShader();
            var mat = new Material(shader) { name = "FreeModelAlbedo" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        /// <summary>
        /// Sleek black-and-white referee. Blue KC on the viewer's left chest, gold DM on the right.
        /// Mesh faces +Z, feet at y=0, 1.55 m. Soldier OBJ remains Referobot_soldier.obj.
        /// </summary>
        public static GameObject SpawnReferobot(Transform parent, int layer)
        {
            var mesh = LoadReferobot();
            if (mesh == null) return null;
            var go = new GameObject("MrReferobotBody", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var tex = LoadTexture(Path.Combine(ReferobotDir, "Referobot_base_color.png"));
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = tex != null
                ? MakeUnlitAlbedo(tex)
                : MakeLitColor(new Color(0.22f, 0.28f, 0.38f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetLayer(go, layer);
            return go;
        }

        public static Material MakeUnlitAlbedo(Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Texture")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "FreeModelUnlit" };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
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

        public static Texture2D LoadTexture(string relativeUnderStreaming)
        {
            var key = relativeUnderStreaming.Replace('\\', '/');
            if (TexCache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            if (!StreamingFiles.TryReadAllBytes(key, out var bytes))
                return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(key) };
            if (!tex.LoadImage(bytes))
                return null;
            TexCache[key] = tex;
            return tex;
        }

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayer(t.gameObject, layer);
        }
    }
}
