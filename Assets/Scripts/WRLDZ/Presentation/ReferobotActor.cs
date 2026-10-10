using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Skinned Mr. Referobot. Loads the rigged GLB plus idle, walk, run, and talk
    /// clips, and plays them on one skeleton. Falls back to nothing when the rig
    /// file is missing so the static OBJ can still stand in.
    /// </summary>
    public class ReferobotActor : MonoBehaviour
    {
        const float Height = 1.55f;

        Transform[] _nodes;
        string[] _nodeNames;
        Clip _clip;
        string _clipName;
        bool _loop;
        float _time;
        readonly Dictionary<string, Clip> _clips = new();

        public static ReferobotActor TryCreate(Transform parent, int layer)
        {
            // StreamingAssets-relative (APK-safe through StreamingFiles).
            var dir = FreeModelCatalog.ReferobotDir;
            var rig = dir + "/ReferobotRig.glb";
            if (!StreamingFiles.Exists(rig)) return null;

            var go = new GameObject("MrReferobotBody");
            go.transform.SetParent(parent, false);
            var actor = go.AddComponent<ReferobotActor>();
            try
            {
                actor.Build(rig, dir, layer);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WRLDZ] Referobot rig failed: " + e.Message);
                UnityEngine.Object.Destroy(go);
                return null;
            }

            return actor;
        }

        public float ClipLength(string name) =>
            _clips.TryGetValue(name, out var c) ? Mathf.Max(0.1f, c.Duration) : 1f;

        public void Play(string name, bool loop)
        {
            if (string.IsNullOrEmpty(name) || !_clips.TryGetValue(name, out var clip)) return;
            if (_clipName == name && _loop == loop && (_loop || _time < clip.Duration)) return;
            _clip = clip;
            _clipName = name;
            _loop = loop;
            _time = 0f;
            Apply(_time);
        }

        void Update()
        {
            if (_clip == null) return;
            _time += Time.deltaTime;
            if (_loop)
            {
                if (_clip.Duration > 0.0001f)
                    _time %= _clip.Duration;
            }
            else if (_time > _clip.Duration)
            {
                _time = _clip.Duration;
            }

            Apply(_time);
        }

        void Build(string rigPath, string dir, int layer)
        {
            var doc = Gltf.Read(StreamingFiles.ReadAllBytes(rigPath));
            var nodes = L(doc, "nodes");
            _nodes = new Transform[nodes.Count];
            _nodeNames = new string[nodes.Count];
            for (var i = 0; i < nodes.Count; i++)
            {
                var n = D(nodes[i]);
                var name = Str(n, "name");
                if (string.IsNullOrEmpty(name)) name = "n" + i;
                _nodeNames[i] = name;
                var go = new GameObject(name);
                _nodes[i] = go.transform;
            }

            var sceneRoots = new List<int>();
            var scenes = L(doc, "scenes");
            if (scenes.Count > 0 && D(scenes[0]).TryGetValue("nodes", out var sn))
                foreach (var n in (List<object>)sn) sceneRoots.Add(I(n));

            // Visual front of the Meshy rig points along Unity −Z after the glTF flip.
            // Turn the whole skeleton so +Z is the chest, matching the street placement.
            var turn = new GameObject("Face").transform;
            turn.SetParent(transform, false);
            turn.localRotation = Quaternion.Euler(0f, 180f, 0f);

            for (var i = 0; i < nodes.Count; i++)
            {
                var n = D(nodes[i]);
                if (n.TryGetValue("children", out var ch))
                    foreach (var c in (List<object>)ch)
                        _nodes[I(c)].SetParent(_nodes[i], false);
                SetLocal(n, _nodes[i]);
            }

            foreach (var r in sceneRoots)
                _nodes[r].SetParent(turn, false);

            var meshNode = -1;
            for (var i = 0; i < nodes.Count; i++)
                if (D(nodes[i]).ContainsKey("mesh")) meshNode = i;
            if (meshNode < 0) throw new InvalidDataException("rig has no mesh");

            var skinIndex = I(D(nodes[meshNode])["skin"]);
            var skin = D(L(doc, "skins")[skinIndex]);
            var jointIds = (List<object>)skin["joints"];
            var bones = new Transform[jointIds.Count];
            for (var i = 0; i < jointIds.Count; i++) bones[i] = _nodes[I(jointIds[i])];

            // glTF skinning ignores the skinned-mesh node's own transform: vertices live in scene
            // space (here: the "Face" turn), and inverseBindMatrices map scene space → joint space.
            // The Meshy rig nests the mesh node under "Armature" (scale 0.01) while its vertices are
            // already in metres, so treating the node transform as mesh space (old code:
            // bind = bone.worldToLocal * meshNode.localToWorld) shrank the bind mesh 100× relative to
            // the skeleton. Every animated pose then flung vertices by bone-offset × 100, and the
            // 1.55 m height normalise (×~102) blew idle/talk into ~110–230 m smeared triangles
            // (white/black/cyan referee albedo) across the duel view. Put the renderer in scene space
            // and use the file's inverse bind matrices.
            var meshGo = _nodes[meshNode].gameObject;
            meshGo.transform.SetParent(turn, false);
            meshGo.transform.localPosition = Vector3.zero;
            meshGo.transform.localRotation = Quaternion.identity;
            meshGo.transform.localScale = Vector3.one;
            var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = BuildMesh(doc, D(L(doc, "meshes")[I(D(nodes[meshNode])["mesh"])]), bones.Length);
            smr.bones = bones;
            smr.rootBone = bones.Length > 0 ? bones[0] : meshGo.transform;
            var ibm = skin.TryGetValue("inverseBindMatrices", out var ibmAcc)
                ? ReadFloats((byte[])doc["__bin"], L(doc, "accessors"), L(doc, "bufferViews"), I(ibmAcc))
                : null;
            var bind = new Matrix4x4[bones.Length];
            var meshToWorld = smr.transform.localToWorldMatrix;
            for (var i = 0; i < bones.Length; i++)
                bind[i] = ibm != null && ibm.Length >= (i + 1) * 16
                    ? GmatInverseBind(ibm, i * 16)
                    : bones[i].worldToLocalMatrix * meshToWorld;
            smr.sharedMesh.bindposes = bind;
            smr.sharedMesh.RecalculateBounds();
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            var tex = FirstImage(doc);
            smr.sharedMaterial = tex != null
                ? FreeModelCatalog.MakeUnlitAlbedo(tex)
                : FreeModelCatalog.MakeLitColor(new Color(0.2f, 0.22f, 0.26f));
            SetLayer(gameObject, layer);

            AddClip("rest", doc);
            AddFile("idle", dir + "/Referobot_idle.glb");
            AddFile("walk", dir + "/Referobot_walk.glb");
            AddFile("run", dir + "/Referobot_run.glb");
            AddFile("talk", dir + "/Referobot_talk.glb");

            // Renderer sits at identity under the turn, so mesh bounds are already in the turn's
            // (== this actor's, == parent-local) units.
            var localH = smr.sharedMesh.bounds.size.y;
            if (localH > 1e-3f)
                turn.localScale = Vector3.one * (Height / localH);

            Play(_clips.ContainsKey("idle") ? "idle" : "rest", true);
            GuardSkinnedExtent(smr, localH);
        }

        /// <summary>
        /// A broken rig must never paint the arena: bake the posed mesh once and bail to the
        /// static OBJ (via <see cref="TryCreate"/>'s catch) if it spans far beyond a body.
        /// </summary>
        static void GuardSkinnedExtent(SkinnedMeshRenderer smr, float restH)
        {
            if (restH <= 1e-3f) return;
            var baked = new Mesh();
            try
            {
                smr.BakeMesh(baked, true);
                var size = baked.bounds.size;
                var limit = restH * 3f;
                if (size.x > limit || size.y > limit || size.z > limit)
                    throw new InvalidDataException(
                        "posed skin spans " + size + " (rest height " + restH + "); bind poses do not match the clips");
            }
            finally
            {
                DestroyImmediate(baked);
            }
        }

        /// <summary>glTF column-major inverse bind matrix → Unity (Z mirrored, same as <see cref="Gpos"/>).</summary>
        static Matrix4x4 GmatInverseBind(float[] f, int o)
        {
            var m = new Matrix4x4();
            for (var c = 0; c < 4; c++)
            for (var r = 0; r < 4; r++)
            {
                var v = f[o + c * 4 + r];
                // C·M·C with C = diag(1,1,−1,1): negate entries where exactly one index is Z.
                if ((r == 2) != (c == 2)) v = -v;
                m[r, c] = v;
            }

            return m;
        }

        /// <param name="path">StreamingAssets-relative clip GLB.</param>
        void AddFile(string name, string path)
        {
            if (!StreamingFiles.TryReadAllBytes(path, out var bytes)) return;
            try { AddClip(name, Gltf.Read(bytes)); }
            catch (Exception e) { Debug.LogWarning("[WRLDZ] Referobot clip " + name + ": " + e.Message); }
        }

        void AddClip(string name, Dictionary<string, object> doc)
        {
            var anims = L(doc, "animations");
            if (anims.Count == 0) return;
            var anim = D(anims[0]);
            var clipNodes = L(doc, "nodes");
            var map = new int[clipNodes.Count];
            for (var i = 0; i < map.Length; i++)
            {
                var n = Str(D(clipNodes[i]), "name");
                map[i] = -1;
                for (var j = 0; j < _nodeNames.Length; j++)
                    if (_nodeNames[j] == n) { map[i] = j; break; }
            }

            var accessors = L(doc, "accessors");
            var views = L(doc, "bufferViews");
            var bin = (byte[])doc["__bin"];
            var samplers = (List<object>)anim["samplers"];
            var channels = (List<object>)anim["channels"];
            var tracks = new List<Track>();
            float duration = 0f;
            foreach (var ch in channels)
            {
                var c = D(ch);
                var target = D(c["target"]);
                var node = map[I(target["node"])];
                if (node < 0) continue;
                var path = Str(target, "path");
                var sampler = D(samplers[I(c["sampler"])]);
                var times = ReadFloats(bin, accessors, views, I(sampler["input"]));
                var values = ReadFloats(bin, accessors, views, I(sampler["output"]));
                if (times.Length == 0) continue;
                duration = Mathf.Max(duration, times[times.Length - 1]);
                if (path == "translation")
                    for (var k = 0; k + 2 < values.Length; k += 3)
                    {
                        var p = Gpos(values[k], values[k + 1], values[k + 2]);
                        values[k] = p.x; values[k + 1] = p.y; values[k + 2] = p.z;
                    }
                else if (path == "rotation")
                    for (var k = 0; k + 3 < values.Length; k += 4)
                    {
                        var q = Grot(values[k], values[k + 1], values[k + 2], values[k + 3]);
                        values[k] = q.x; values[k + 1] = q.y; values[k + 2] = q.z; values[k + 3] = q.w;
                    }

                tracks.Add(new Track { Node = node, Path = path, Times = times, Values = values });
            }

            if (tracks.Count == 0) return;
            _clips[name] = new Clip { Duration = Mathf.Max(0.05f, duration), Tracks = tracks.ToArray() };
        }

        void Apply(float time)
        {
            foreach (var track in _clip.Tracks)
            {
                var t = _nodes[track.Node];
                if (track.Path == "translation")
                {
                    var v = Sample3(track, time);
                    t.localPosition = v;
                }
                else if (track.Path == "rotation")
                {
                    t.localRotation = Sample4(track, time);
                }
                else if (track.Path == "scale")
                {
                    t.localScale = Sample3(track, time);
                }
            }
        }

        static Vector3 Sample3(Track track, float time)
        {
            var i = Key(track.Times, time, out var u);
            var a = i * 3;
            var b = Mathf.Min(i + 1, track.Times.Length - 1) * 3;
            return Vector3.Lerp(
                new Vector3(track.Values[a], track.Values[a + 1], track.Values[a + 2]),
                new Vector3(track.Values[b], track.Values[b + 1], track.Values[b + 2]), u);
        }

        static Quaternion Sample4(Track track, float time)
        {
            var i = Key(track.Times, time, out var u);
            var a = i * 4;
            var b = Mathf.Min(i + 1, track.Times.Length - 1) * 4;
            return Quaternion.Slerp(
                new Quaternion(track.Values[a], track.Values[a + 1], track.Values[a + 2], track.Values[a + 3]),
                new Quaternion(track.Values[b], track.Values[b + 1], track.Values[b + 2], track.Values[b + 3]), u);
        }

        static int Key(float[] times, float time, out float u)
        {
            var i = 0;
            while (i + 1 < times.Length && times[i + 1] <= time) i++;
            if (i + 1 >= times.Length) { u = 0f; return Mathf.Max(0, times.Length - 1); }
            var span = times[i + 1] - times[i];
            u = span > 1e-5f ? Mathf.Clamp01((time - times[i]) / span) : 0f;
            return i;
        }

        Mesh BuildMesh(Dictionary<string, object> doc, Dictionary<string, object> mesh, int boneCount)
        {
            var prim = D(((List<object>)mesh["primitives"])[0]);
            var attr = D(prim["attributes"]);
            var accessors = L(doc, "accessors");
            var views = L(doc, "bufferViews");
            var bin = (byte[])doc["__bin"];
            var pos = ReadFloats(bin, accessors, views, I(attr["POSITION"]));
            var nrm = attr.ContainsKey("NORMAL") ? ReadFloats(bin, accessors, views, I(attr["NORMAL"])) : null;
            var uv = attr.ContainsKey("TEXCOORD_0") ? ReadFloats(bin, accessors, views, I(attr["TEXCOORD_0"])) : null;
            var joints = ReadBytes(bin, accessors, views, I(attr["JOINTS_0"]));
            var weights = ReadFloats(bin, accessors, views, I(attr["WEIGHTS_0"]));
            var indexAcc = I(prim["indices"]);
            var indexType = I(D(accessors[indexAcc])["componentType"]);
            var rawIndex = ReadRaw(bin, accessors, views, indexAcc);

            var count = pos.Length / 3;
            var verts = new Vector3[count];
            var normals = new Vector3[count];
            var uvs = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                verts[i] = Gpos(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                if (nrm != null && nrm.Length >= (i + 1) * 3)
                    normals[i] = Gpos(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                if (uv != null && uv.Length >= (i + 1) * 2)
                    uvs[i] = new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]);
            }

            var indexCount = I(D(accessors[indexAcc])["count"]);
            var tris = new int[indexCount];
            for (var i = 0; i + 2 < indexCount; i += 3)
            {
                tris[i] = IndexAt(rawIndex, indexType, i);
                tris[i + 1] = IndexAt(rawIndex, indexType, i + 2);
                tris[i + 2] = IndexAt(rawIndex, indexType, i + 1);
            }

            var bw = new BoneWeight[count];
            for (var i = 0; i < count; i++)
            {
                var w = new BoneWeight();
                int J(int k) => Mathf.Clamp(joints[i * 4 + k], 0, Mathf.Max(0, boneCount - 1));
                float W(int k) => weights[i * 4 + k];
                w.boneIndex0 = J(0); w.weight0 = W(0);
                w.boneIndex1 = J(1); w.weight1 = W(1);
                w.boneIndex2 = J(2); w.weight2 = W(2);
                w.boneIndex3 = J(3); w.weight3 = W(3);
                bw[i] = w;
            }

            var m = new Mesh { name = "ReferobotSkin" };
            m.vertices = verts;
            if (nrm != null) m.normals = normals;
            if (uv != null) m.uv = uvs;
            m.triangles = tris;
            m.boneWeights = bw;
            if (nrm == null) m.RecalculateNormals();
            return m;
        }

        static void SetLocal(Dictionary<string, object> node, Transform t)
        {
            if (node.TryGetValue("translation", out var tr))
            {
                var l = (List<object>)tr;
                t.localPosition = Gpos(F(l[0]), F(l[1]), F(l[2]));
            }
            if (node.TryGetValue("rotation", out var rr))
            {
                var l = (List<object>)rr;
                t.localRotation = Grot(F(l[0]), F(l[1]), F(l[2]), F(l[3]));
            }
            if (node.TryGetValue("scale", out var sc))
            {
                var l = (List<object>)sc;
                t.localScale = new Vector3(F(l[0]), F(l[1]), F(l[2]));
            }
        }

        static Vector3 Gpos(float x, float y, float z) => new(x, y, -z);
        static Quaternion Grot(float x, float y, float z, float w) => new Quaternion(x, y, -z, -w).normalized;

        static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        sealed class Clip
        {
            public float Duration;
            public Track[] Tracks;
        }

        sealed class Track
        {
            public int Node;
            public string Path;
            public float[] Times;
            public float[] Values;
        }

        static Texture2D FirstImage(Dictionary<string, object> doc)
        {
            var images = L(doc, "images");
            if (images.Count == 0) return null;
            var img = D(images[0]);
            if (!img.ContainsKey("bufferView")) return null;
            var bytes = ReadRaw((byte[])doc["__bin"], L(doc, "accessors"), L(doc, "bufferViews"), -1, I(img["bufferView"]));
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "ReferobotSkin" };
            return tex.LoadImage(bytes) ? tex : null;
        }

        static float[] ReadFloats(byte[] bin, List<object> accessors, List<object> views, int accessor)
        {
            var raw = ReadRaw(bin, accessors, views, accessor);
            var acc = D(accessors[accessor]);
            var count = I(acc["count"]) * Components(Str(acc, "type"));
            var o = new float[count];
            Buffer.BlockCopy(raw, 0, o, 0, Mathf.Min(raw.Length, count * 4));
            return o;
        }

        static byte[] ReadBytes(byte[] bin, List<object> accessors, List<object> views, int accessor) =>
            ReadRaw(bin, accessors, views, accessor);

        static byte[] ReadRaw(byte[] bin, List<object> accessors, List<object> views, int accessor, int viewOverride = -1)
        {
            int viewIndex;
            int accOffset = 0;
            int countBytes;
            if (accessor >= 0)
            {
                var acc = D(accessors[accessor]);
                viewIndex = I(acc["bufferView"]);
                accOffset = acc.ContainsKey("byteOffset") ? I(acc["byteOffset"]) : 0;
                countBytes = I(acc["count"]) * Components(Str(acc, "type")) * ComponentBytes(I(acc["componentType"]));
            }
            else
            {
                viewIndex = viewOverride;
                var viewLen = I(D(views[viewIndex])["byteLength"]);
                countBytes = viewLen;
            }

            var view = D(views[viewIndex]);
            var start = (view.ContainsKey("byteOffset") ? I(view["byteOffset"]) : 0) + accOffset;
            var bytes = new byte[countBytes];
            Buffer.BlockCopy(bin, start, bytes, 0, countBytes);
            return bytes;
        }

        static int IndexAt(byte[] raw, int componentType, int i)
        {
            if (componentType == 5123) return BitConverter.ToUInt16(raw, i * 2);
            if (componentType == 5125) return (int)BitConverter.ToUInt32(raw, i * 4);
            return raw[i];
        }

        static int Components(string type) => type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => 1
        };

        static int ComponentBytes(int t) => t switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            _ => 4
        };

        static List<object> L(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();
        static Dictionary<string, object> D(object o) => (Dictionary<string, object>)o;
        static int I(object o) => Convert.ToInt32(o);
        static float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);
        static string Str(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is string s ? s : "";

        static class Gltf
        {
            public static Dictionary<string, object> Read(byte[] glb)
            {
                if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67)
                    throw new InvalidDataException("not a GLB");
                var pos = 12;
                string json = null;
                byte[] bin = null;
                while (pos + 8 <= glb.Length)
                {
                    var len = (int)BitConverter.ToUInt32(glb, pos);
                    var type = BitConverter.ToUInt32(glb, pos + 4);
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(glb, pos + 8, len).TrimEnd('\0', ' ');
                    else if (type == 0x004E4942)
                    {
                        bin = new byte[len];
                        Buffer.BlockCopy(glb, pos + 8, bin, 0, len);
                    }
                    pos += 8 + ((len + 3) & ~3);
                }

                if (json == null || bin == null) throw new InvalidDataException("GLB chunks missing");
                var doc = (Dictionary<string, object>)Mini.Parse(json);
                doc["__bin"] = bin;
                return doc;
            }
        }

        static class Mini
        {
            public static object Parse(string s) { var i = 0; return Value(s, ref i); }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
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
                i++;
                while (true)
                {
                    Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    var k = Str(s, ref i);
                    Ws(s, ref i); i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
            }

            static List<object> Arr(string s, ref int i)
            {
                var l = new List<object>();
                i++;
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
                i++;
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        var e = s[i++];
                        sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e);
                        if (e == 'u') { sb.Length--; sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            static object Num(string s, ref int i)
            {
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        }
    }
}
