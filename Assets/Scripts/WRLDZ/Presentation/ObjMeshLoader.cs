using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Minimal OBJ loader for Spirit Dueler assets (StreamingAssets / absolute paths).
    /// Supports v / f (triangulates quads). Sufficient for Meshy export meshes.
    /// </summary>
    public static class ObjMeshLoader
    {
        public static Mesh LoadFromFile(string path, string meshName = "ObjMesh")
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning("[WRLDZ] OBJ not found: " + path);
                return null;
            }

            return Parse(File.ReadAllText(path), meshName);
        }

        public static Mesh Parse(string objText, string meshName = "ObjMesh")
        {
            var positions = new List<Vector3>(4096);
            var normals = new List<Vector3>(4096);
            var uvs = new List<Vector2>(4096);
            var triPos = new List<int>(8192);
            var triNrm = new List<int>(8192);
            var triUv = new List<int>(8192);

            var lines = objText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4)
                        positions.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4)
                        normals.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                }
                else if (line.StartsWith("vt ", StringComparison.Ordinal))
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 3)
                        uvs.Add(new Vector2(F(p[1]), F(p[2])));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    // f v/vt/vn  — fan triangulate
                    var face = new List<(int v, int vt, int vn)>(p.Length - 1);
                    for (var i = 1; i < p.Length; i++)
                        face.Add(ParseFaceVert(p[i], positions.Count, uvs.Count, normals.Count));
                    for (var i = 1; i + 1 < face.Count; i++)
                    {
                        AddTri(face[0], face[i], face[i + 1], triPos, triUv, triNrm);
                    }
                }
            }

            if (positions.Count == 0 || triPos.Count == 0)
            {
                Debug.LogWarning("[WRLDZ] OBJ had no geometry.");
                return null;
            }

            // Expand indexed attributes to unique verts for Unity mesh
            var outV = new List<Vector3>(triPos.Count);
            var outN = new List<Vector3>(triPos.Count);
            var outUv = new List<Vector2>(triPos.Count);
            var outT = new List<int>(triPos.Count);

            for (var i = 0; i < triPos.Count; i++)
            {
                var vi = triPos[i];
                outV.Add(positions[vi]);
                if (triNrm[i] >= 0 && triNrm[i] < normals.Count)
                    outN.Add(normals[triNrm[i]]);
                else
                    outN.Add(Vector3.up);
                if (triUv[i] >= 0 && triUv[i] < uvs.Count)
                    outUv.Add(uvs[triUv[i]]);
                else
                    outUv.Add(Vector2.zero);
                outT.Add(i);
            }

            // If no normals in file, recompute
            var hasFileNormals = normals.Count > 0;
            var mesh = new Mesh { name = meshName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(outV);
            mesh.SetTriangles(outT, 0);
            if (hasFileNormals)
                mesh.SetNormals(outN);
            else
                mesh.RecalculateNormals();

            // Fusion ATF export has no UVs — planar-project XZ so plate skins tile.
            var hasUv = false;
            for (var i = 0; i < outUv.Count; i++)
            {
                if (outUv[i].sqrMagnitude > 1e-8f) { hasUv = true; break; }
            }
            if (!hasUv && outV.Count > 0)
            {
                var bmin = outV[0];
                var bmax = outV[0];
                for (var i = 1; i < outV.Count; i++)
                {
                    bmin = Vector3.Min(bmin, outV[i]);
                    bmax = Vector3.Max(bmax, outV[i]);
                }
                const float tile = 0.16f;
                for (var i = 0; i < outV.Count; i++)
                {
                    var p = outV[i];
                    outUv[i] = new Vector2((p.x - bmin.x) / tile, (p.z - bmin.z) / tile);
                }
            }

            mesh.SetUVs(0, outUv);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void AddTri(
            (int v, int vt, int vn) a,
            (int v, int vt, int vn) b,
            (int v, int vt, int vn) c,
            List<int> triPos, List<int> triUv, List<int> triNrm)
        {
            triPos.Add(a.v); triPos.Add(b.v); triPos.Add(c.v);
            triUv.Add(a.vt); triUv.Add(b.vt); triUv.Add(c.vt);
            triNrm.Add(a.vn); triNrm.Add(b.vn); triNrm.Add(c.vn);
        }

        static (int v, int vt, int vn) ParseFaceVert(string token, int vCount, int vtCount, int vnCount)
        {
            // formats: v | v/vt | v//vn | v/vt/vn  (1-based, negative relative)
            var parts = token.Split('/');
            int V(string s, int count)
            {
                if (string.IsNullOrEmpty(s)) return -1;
                var i = int.Parse(s, CultureInfo.InvariantCulture);
                if (i < 0) i = count + i + 1;
                return i - 1;
            }

            var v = V(parts[0], vCount);
            var vt = parts.Length > 1 ? V(parts[1], vtCount) : -1;
            var vn = parts.Length > 2 ? V(parts[2], vnCount) : -1;
            return (v, vt, vn);
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
