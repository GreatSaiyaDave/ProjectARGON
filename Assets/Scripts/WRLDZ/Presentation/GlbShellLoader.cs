using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Minimal GLB (glTF 2.0 binary) loader for per-shell monster reliefs
    /// (StreamingAssets/Models/Cards/{id}.glb). Self-contained on purpose: no registry
    /// package to resolve, same spirit as <see cref="ObjMeshLoader"/>.
    ///
    /// Supports: node hierarchy, one primitive per mesh, POSITION / NORMAL / TEXCOORD_0,
    /// uint8/16/32 indices, embedded PNG/JPG images referenced by emissive or base-colour
    /// textures, alphaMode MASK. Not supported: Draco/meshopt compression, skins, animations.
    ///
    /// Axis mapping. The reliefs are authored in Blender with X = image right, Y = image up,
    /// Z = depth toward the viewer; Blender's exporter writes them as glTF (X, depth, -up).
    /// Unity is left-handed and the arena convention is "local +Z faces the camera" (see
    /// ArArenaCardVisual), so a camera on +Z looking down -Z sees +X on its LEFT. Hence:
    ///   Unity = (-gX, -gZ, gY)  ->  image right = -X, image up = +Y, nearer = +Z.
    /// That matrix has determinant -1 (the right->left handed flip), so triangle winding is
    /// reversed to keep front faces facing +Z. UV v is flipped (glTF origin is top-left).
    /// </summary>
    public static class GlbShellLoader
    {
        public sealed class ShellPart
        {
            public string Name;
            public Mesh Mesh;
            public Texture2D Texture;
            public GameObject Go;
            public MeshRenderer Renderer;
        }

        public sealed class Result
        {
            public GameObject Root;
            public readonly List<ShellPart> Parts = new();
            public Bounds LocalBounds;   // union of part meshes, in root-local space, after axis mapping
            /// <summary>The parsed GLB whose meshes / textures the parts share (never null after a load).</summary>
            public Template Source;
        }

        /// <summary>
        /// A parsed GLB: node hierarchy plus the Meshes and (non-readable) Textures it owns. Any number of
        /// <see cref="Instantiate"/> calls share these assets; only the per-instance materials are new. The owner
        /// (e.g. <see cref="CardModelCatalog"/>'s per-card cache) frees them with <see cref="DestroyAssets"/>.
        /// </summary>
        public sealed class Template
        {
            internal sealed class Node
            {
                public string Name;
                public int Parent = -1;
                public Vector3 LocalPosition;
                public Vector3 LocalScale = Vector3.one;
                public Mesh Mesh;
                public Texture2D Texture;
            }

            internal readonly List<Node> Nodes = new();
            public readonly List<Mesh> Meshes = new();
            public readonly List<Texture2D> Textures = new();
            public Bounds LocalBounds;
            public bool Destroyed { get; private set; }

            /// <summary>Approximate GPU bytes held (RGBA32 textures + vertex data), for logs / budgets.</summary>
            public long ApproxBytes
            {
                get
                {
                    long n = 0;
                    foreach (var t in Textures) if (t != null) n += (long)t.width * t.height * 4;
                    foreach (var m in Meshes) if (m != null) n += (long)m.vertexCount * 32 + m.triangles.LongLength * 2;
                    return n;
                }
            }

            public bool Owns(Mesh m) => m != null && Meshes.Contains(m);

            public void DestroyAssets()
            {
                if (Destroyed) return;
                Destroyed = true;
                foreach (var m in Meshes) DestroyAsset(m);
                foreach (var t in Textures) DestroyAsset(t);
                Meshes.Clear();
                Textures.Clear();
                Nodes.Clear();
            }
        }

        static void DestroyAsset(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        const uint Magic = 0x46546C67;      // "glTF"
        const uint ChunkJson = 0x4E4F534A;  // "JSON"
        const uint ChunkBin = 0x004E4942;   // "BIN\0"

        /// <param name="materialFactory">Given the shell's texture, returns the material to use
        /// (the caller owns the presentation rules, e.g. unlit + exposure + alpha clip).</param>
        public static Result LoadFromStreaming(string relativePath, string rootName,
            Func<Texture2D, Material> materialFactory)
        {
            if (string.IsNullOrEmpty(relativePath) || !StreamingFiles.TryReadAllBytes(relativePath, out var bytes))
            {
                Debug.LogWarning("[WRLDZ] GLB not found: " + StreamingFiles.FullPathForLog(relativePath));
                return null;
            }

            try
            {
                return Parse(bytes, rootName, materialFactory);
            }
            catch (Exception e)
            {
                Debug.LogError("[WRLDZ] GLB load failed for " + StreamingFiles.FullPathForLog(relativePath) + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Parse a StreamingAssets GLB into a shareable <see cref="Template"/> (null when missing / invalid).</summary>
        public static Template LoadTemplateFromStreaming(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || !StreamingFiles.TryReadAllBytes(relativePath, out var bytes))
            {
                Debug.LogWarning("[WRLDZ] GLB not found: " + StreamingFiles.FullPathForLog(relativePath));
                return null;
            }

            try
            {
                return ParseTemplate(bytes);
            }
            catch (Exception e)
            {
                Debug.LogError("[WRLDZ] GLB load failed for " + StreamingFiles.FullPathForLog(relativePath) + ": " + e.Message);
                return null;
            }
        }

        /// <summary>GLB at an absolute disk path (Editor tooling). StreamingAssets content uses <see cref="LoadFromStreaming"/>.</summary>
        public static Result LoadFromFile(string path, string rootName, Func<Texture2D, Material> materialFactory)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ] GLB not found: " + path);
                return null;
            }

            try
            {
                return Parse(File.ReadAllBytes(path), rootName, materialFactory);
            }
            catch (Exception e)
            {
                Debug.LogError("[WRLDZ] GLB load failed for " + path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Parse + instantiate in one go. The caller owns the result's meshes and textures
        /// (<see cref="Result.Source"/>); shared use goes through <see cref="ParseTemplate"/> + <see cref="Instantiate"/>.</summary>
        public static Result Parse(byte[] glb, string rootName, Func<Texture2D, Material> materialFactory) =>
            Instantiate(ParseTemplate(glb), rootName, materialFactory);

        public static Template ParseTemplate(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != Magic)
                throw new InvalidDataException("not a GLB");
            var total = BitConverter.ToUInt32(glb, 8);
            var pos = 12;
            string json = null;
            var binOffset = -1;
            var binLength = 0;
            while (pos + 8 <= total && pos + 8 <= glb.Length)
            {
                var len = (int)BitConverter.ToUInt32(glb, pos);
                var type = BitConverter.ToUInt32(glb, pos + 4);
                pos += 8;
                if (type == ChunkJson) json = Encoding.UTF8.GetString(glb, pos, len);
                else if (type == ChunkBin) { binOffset = pos; binLength = len; }
                pos += len;
            }
            if (json == null || binOffset < 0) throw new InvalidDataException("GLB missing JSON/BIN chunk");

            var doc = MiniJson.Parse(json) as Dictionary<string, object>;
            if (doc == null) throw new InvalidDataException("GLB JSON root is not an object");

            var accessors = L(doc, "accessors");
            var bufferViews = L(doc, "bufferViews");
            var meshes = L(doc, "meshes");
            var materials = L(doc, "materials");
            var textures = L(doc, "textures");
            var images = L(doc, "images");
            var nodes = L(doc, "nodes");

            var template = new Template();
            var textureCache = new Dictionary<int, Texture2D>();
            var first = true;
            var bounds = new Bounds();

            // scene roots (fall back to every node without a parent)
            var roots = new List<int>();
            var scenes = L(doc, "scenes");
            if (scenes.Count > 0 && D(scenes[0]).TryGetValue("nodes", out var sn))
                foreach (var n in (List<object>)sn) roots.Add(I(n));
            else
            {
                var isChild = new HashSet<int>();
                foreach (var n in nodes)
                    if (D(n).TryGetValue("children", out var ch)) foreach (var c in (List<object>)ch) isChild.Add(I(c));
                for (var i = 0; i < nodes.Count; i++) if (!isChild.Contains(i)) roots.Add(i);
            }

            try
            {
                void Visit(int nodeIdx, int parent)
                {
                    var node = D(nodes[nodeIdx]);
                    var tn = new Template.Node
                    {
                        Name = node.TryGetValue("name", out var nm) ? (string)nm : "node" + nodeIdx,
                        Parent = parent
                    };
                    ReadNodeTransform(node, tn);
                    var self = template.Nodes.Count;
                    template.Nodes.Add(tn);

                    if (node.TryGetValue("mesh", out var mi))
                    {
                        var mesh = D(meshes[I(mi)]);
                        var prims = L(mesh, "primitives");
                        if (prims.Count > 0)
                        {
                            var prim = D(prims[0]);
                            var m = BuildMesh(prim, accessors, bufferViews, glb, binOffset, binLength, tn.Name);
                            template.Meshes.Add(m);
                            Texture2D tex = null;
                            if (prim.TryGetValue("material", out var mat))
                            {
                                var before = textureCache.Count;
                                tex = ResolveTexture(D(materials[I(mat)]), textures, images, bufferViews, glb, binOffset, textureCache);
                                if (tex != null && textureCache.Count > before) template.Textures.Add(tex);
                            }

                            tn.Mesh = m;
                            tn.Texture = tex;
                            var b = m.bounds;
                            b.center = tn.LocalPosition + Vector3.Scale(b.center, tn.LocalScale);
                            if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
                        }
                    }

                    if (node.TryGetValue("children", out var children))
                        foreach (var c in (List<object>)children) Visit(I(c), self);
                }

                foreach (var r in roots) Visit(r, -1);
            }
            catch
            {
                template.DestroyAssets();
                throw;
            }

            template.LocalBounds = bounds;
            return template;
        }

        /// <summary>
        /// Build a GameObject hierarchy from <paramref name="template"/>. Meshes and textures are shared with the
        /// template (never destroy them per instance); <paramref name="materialFactory"/> makes one material per part,
        /// which the caller owns.
        /// </summary>
        public static Result Instantiate(Template template, string rootName, Func<Texture2D, Material> materialFactory)
        {
            if (template == null || template.Destroyed) return null;
            var result = new Result { Root = new GameObject(rootName), Source = template, LocalBounds = template.LocalBounds };
            var gos = new Transform[template.Nodes.Count];
            for (var i = 0; i < template.Nodes.Count; i++)
            {
                var tn = template.Nodes[i];
                var go = new GameObject(tn.Name);
                var parent = tn.Parent >= 0 ? gos[tn.Parent] : result.Root.transform;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = tn.LocalPosition;
                go.transform.localScale = tn.LocalScale;
                gos[i] = go.transform;
                if (tn.Mesh == null) continue;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = tn.Mesh;
                mr.sharedMaterial = materialFactory != null ? materialFactory(tn.Texture) : new Material(ArAnimePresentation.UnlitShader());
                result.Parts.Add(new ShellPart { Name = tn.Name, Mesh = tn.Mesh, Texture = tn.Texture, Go = go, Renderer = mr });
            }

            return result;
        }

        static void ReadNodeTransform(Dictionary<string, object> node, Template.Node t)
        {
            // Reliefs are exported without node transforms; honour them if present, in the
            // same axis mapping as the vertices.
            if (node.TryGetValue("translation", out var tr))
            {
                var v = V3((List<object>)tr);
                t.LocalPosition = Map(v);
            }
            if (node.TryGetValue("scale", out var sc))
            {
                var v = V3((List<object>)sc);
                t.LocalScale = new Vector3(v.x, v.z, v.y);
            }
            // rotations are not expected on these assets; a general quaternion remap is
            // deliberately left out until an asset needs it.
        }

        static Vector3 Map(Vector3 g) => new(-g.x, -g.z, g.y);

        static Mesh BuildMesh(Dictionary<string, object> prim, List<object> accessors, List<object> bufferViews,
            byte[] glb, int binOffset, int binLength, string name)
        {
            var attrs = D(prim["attributes"]);
            var positions = ReadVec3(D(accessors[I(attrs["POSITION"])]), bufferViews, glb, binOffset);
            Vector2[] uvs = null;
            if (attrs.TryGetValue("TEXCOORD_0", out var uvAcc))
                uvs = ReadVec2(D(accessors[I(uvAcc)]), bufferViews, glb, binOffset);
            int[] indices;
            if (prim.TryGetValue("indices", out var ia))
                indices = ReadIndices(D(accessors[I(ia)]), bufferViews, glb, binOffset);
            else
            {
                indices = new int[positions.Length];
                for (var i = 0; i < indices.Length; i++) indices[i] = i;
            }

            var verts = new Vector3[positions.Length];
            for (var i = 0; i < positions.Length; i++) verts[i] = Map(positions[i]);
            if (uvs != null)
                for (var i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uvs[i].x, 1f - uvs[i].y);
            // handedness flip -> reverse winding so front faces look toward +Z
            for (var i = 0; i + 2 < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

            var mesh = new Mesh { name = name };
            mesh.indexFormat = verts.Length > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            if (uvs != null && uvs.Length == verts.Length) mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D ResolveTexture(Dictionary<string, object> material, List<object> textures, List<object> images,
            List<object> bufferViews, byte[] glb, int binOffset, Dictionary<int, Texture2D> cache)
        {
            // Blender exports the relief's emission shader as emissiveTexture; baseColor is the
            // fallback for assets authored the other way round.
            int texIndex = -1;
            if (material.TryGetValue("emissiveTexture", out var et)) texIndex = I(D(et)["index"]);
            else if (material.TryGetValue("pbrMetallicRoughness", out var pbr) &&
                     D(pbr).TryGetValue("baseColorTexture", out var bct)) texIndex = I(D(bct)["index"]);
            if (texIndex < 0 || texIndex >= textures.Count) return null;
            if (cache.TryGetValue(texIndex, out var cached)) return cached;

            var srcIdx = I(D(textures[texIndex])["source"]);
            var image = D(images[srcIdx]);
            if (!image.TryGetValue("bufferView", out var bvIdx)) return null;   // external URIs unsupported
            var bv = D(bufferViews[I(bvIdx)]);
            var off = binOffset + (bv.TryGetValue("byteOffset", out var bo) ? I(bo) : 0);
            var len = I(bv["byteLength"]);
            var bytes = new byte[len];
            Buffer.BlockCopy(glb, off, bytes, 0, len);
            // No mip chain. A relief's decimated terraces present steep slivers whose UV
            // derivatives select a tiny mip; its average is pulled dark by the transparent
            // (black-RGB) margin and shows as contour lines across every surface. Blender's
            // preview samples without mips, which is why it never showed there.
            // markNonReadable: nothing reads these pixels back, so drop the CPU copy after upload (halves the
            // memory of every relief; a 4-shell 1024x1236 monster held ~25 MB twice before).
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "shell_tex_" + texIndex };
            if (!tex.LoadImage(bytes, true))
            {
                DestroyAsset(tex);
                return null;
            }
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            cache[texIndex] = tex;
            return tex;
        }

        // ---- accessor readers -------------------------------------------------------------

        static (int offset, int stride, int count, int comp) Locate(Dictionary<string, object> acc,
            List<object> bufferViews, int binOffset, int elemSize)
        {
            var bv = D(bufferViews[I(acc["bufferView"])]);
            var offset = binOffset + (bv.TryGetValue("byteOffset", out var bo) ? I(bo) : 0)
                                   + (acc.TryGetValue("byteOffset", out var ao) ? I(ao) : 0);
            var stride = bv.TryGetValue("byteStride", out var bs) ? I(bs) : elemSize;
            return (offset, stride, I(acc["count"]), I(acc["componentType"]));
        }

        static Vector3[] ReadVec3(Dictionary<string, object> acc, List<object> bufferViews, byte[] glb, int binOffset)
        {
            var (off, stride, count, comp) = Locate(acc, bufferViews, binOffset, 12);
            if (comp != 5126) throw new InvalidDataException("VEC3 accessor is not float");
            var outv = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var p = off + i * stride;
                outv[i] = new Vector3(BitConverter.ToSingle(glb, p), BitConverter.ToSingle(glb, p + 4), BitConverter.ToSingle(glb, p + 8));
            }
            return outv;
        }

        static Vector2[] ReadVec2(Dictionary<string, object> acc, List<object> bufferViews, byte[] glb, int binOffset)
        {
            var (off, stride, count, comp) = Locate(acc, bufferViews, binOffset, 8);
            if (comp != 5126) throw new InvalidDataException("VEC2 accessor is not float");
            var outv = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var p = off + i * stride;
                outv[i] = new Vector2(BitConverter.ToSingle(glb, p), BitConverter.ToSingle(glb, p + 4));
            }
            return outv;
        }

        static int[] ReadIndices(Dictionary<string, object> acc, List<object> bufferViews, byte[] glb, int binOffset)
        {
            var comp = I(acc["componentType"]);
            var size = comp == 5125 ? 4 : comp == 5123 ? 2 : 1;
            var (off, stride, count, _) = Locate(acc, bufferViews, binOffset, size);
            var outv = new int[count];
            for (var i = 0; i < count; i++)
            {
                var p = off + i * stride;
                outv[i] = size == 4 ? (int)BitConverter.ToUInt32(glb, p) : size == 2 ? BitConverter.ToUInt16(glb, p) : glb[p];
            }
            return outv;
        }

        // ---- json helpers ------------------------------------------------------------------

        static List<object> L(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();
        static Dictionary<string, object> D(object o) => (Dictionary<string, object>)o;
        static int I(object o) => Convert.ToInt32(o);
        static Vector3 V3(List<object> l) => new(Convert.ToSingle(l[0]), Convert.ToSingle(l[1]), Convert.ToSingle(l[2]));

        /// <summary>Tiny recursive-descent JSON parser: objects -> Dictionary, arrays -> List,
        /// numbers -> double, plus string/bool/null. Enough for glTF headers.</summary>
        static class MiniJson
        {
            public static object Parse(string s) { var i = 0; return Value(s, ref i); }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) return null;
                var c = s[i];
                if (c == '{') return Obj(s, ref i);
                if (c == '[') return Arr(s, ref i);
                if (c == '"') return Str(s, ref i);
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return Num(s, ref i);
            }

            static Dictionary<string, object> Obj(string s, ref int i)
            {
                var d = new Dictionary<string, object>();
                i++; // {
                while (true)
                {
                    Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    var k = Str(s, ref i);
                    Ws(s, ref i); i++; // :
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
            }

            static List<object> Arr(string s, ref int i)
            {
                var l = new List<object>();
                i++; // [
                while (true)
                {
                    Ws(s, ref i);
                    if (s[i] == ']') { i++; return l; }
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
            }

            static string Str(string s, ref int i)
            {
                var sb = new StringBuilder();
                i++; // "
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        var e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            static object Num(string s, ref int i)
            {
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
            }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        }
    }
}
