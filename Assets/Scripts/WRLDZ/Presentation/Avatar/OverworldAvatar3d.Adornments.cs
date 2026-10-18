using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Accessories (props on bones) and tattoos (skinned patches cut from the body round an anchor) — 2026-10-18.
    /// Lives in its own file so the avatar's main file only gains the one call in ApplyCustomization.
    /// </summary>
    public sealed partial class OverworldAvatar3d
    {
        readonly List<GameObject> _adornments = new();
        string _adornKey;

        void ApplyAdornments(AvatarAppearance look)
        {
            var key = look == null ? "" : _sex + "|" + string.Join(",", look.accessoryIds ?? new List<string>()) + "|" + string.Join(",", look.tattooIds ?? new List<string>());
            if (key == _adornKey) return;
            _adornKey = key;
            foreach (var go in _adornments)
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) KillMaterials(r);
                var smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null && smr.sharedMesh.name.StartsWith("Tattoo_")) Kill(smr.sharedMesh);
                Kill(go);
            }
            _adornments.Clear();
            if (look == null || _fullCharacter) return;
            var anim = GetComponent<Animator>();
            if (anim == null || !anim.isHuman) return;
            var female = _sex == "female";
            if (look.accessoryIds != null)
                foreach (var id in look.accessoryIds)
                {
                    var it = AdornmentCatalog.Accessory(id);
                    if (it != null && it.FitsBody(female)) AttachAccessory(anim, it);
                }
            if (look.tattooIds != null)
                foreach (var id in look.tattooIds)
                {
                    var it = AdornmentCatalog.Tattoo(id);
                    if (it != null && it.FitsBody(female)) BuildTattoo(anim, it);
                }
        }

        void AttachAccessory(Animator anim, AccessoryItem it)
        {
            var bone = anim.GetBoneTransform(it.Bone);
            if (bone == null) return;
            var prefab = Resources.Load<GameObject>(it.Resource);
            if (prefab == null) { Debug.LogWarning($"[WRLDZ] accessory '{it.Id}': nothing at Resources/{it.Resource}"); return; }
            var go = Instantiate(prefab, bone, false);
            go.name = "Accessory_" + it.Id;
            go.transform.localPosition = it.LocalPos;
            go.transform.localRotation = Quaternion.Euler(it.LocalEuler);
            go.transform.localScale = Vector3.one * it.Scale;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            if (!string.IsNullOrEmpty(it.Hex) && ColorUtility.TryParseHtmlString(it.Hex, out var c))
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = Own(FreeModelCatalog.MakeLitColor(c));
            _adornments.Add(go);
        }

        /// <summary>
        /// The skin round the anchor becomes its own skinned mesh: the body's triangles within 0.75 × Size whose normals
        /// face the anchor's, lifted 1.5 mm, with the body's bone weights and every blend shape copied, UVs = a planar
        /// projection in the skin's tangent frame. The body mesh must import Read/Write (AvatarImportPostprocessor).
        /// </summary>
        void BuildTattoo(Animator anim, TattooItem it)
        {
            if (_smr == null || _smr.sharedMesh == null) return;
            var tex = Resources.Load<Texture2D>(it.Resource);
            if (tex == null) { Debug.LogWarning($"[WRLDZ] tattoo '{it.Id}': nothing at Resources/{it.Resource}"); return; }
            var bone = anim.GetBoneTransform(it.Bone);
            if (bone == null) return;
            var mesh = _smr.sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning("[WRLDZ] tattoos need the base body imported Read/Write"); return; }
            var bones = _smr.bones;
            var bi = System.Array.IndexOf(bones, bone);
            if (bi < 0) return;
            var bind = mesh.bindposes;
            var anchor = bind[bi].inverse.MultiplyPoint3x4(it.LocalPos);
            var verts = mesh.vertices; var norms = mesh.normals; var tris = mesh.triangles; var bw = mesh.boneWeights;
            var ni = 0; var best = float.MaxValue;
            for (var i = 0; i < verts.Length; i++)
            {
                var d = (verts[i] - anchor).sqrMagnitude;
                if (d < best) { best = d; ni = i; }
            }
            var n0 = norms[ni].normalized;
            var r2 = it.Size * 0.75f * it.Size * 0.75f;
            var remap = new Dictionary<int, int>();
            var newTris = new List<int>();
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if ((verts[a] - anchor).sqrMagnitude > r2 || (verts[b] - anchor).sqrMagnitude > r2 || (verts[c] - anchor).sqrMagnitude > r2) continue;
                if (Vector3.Dot(norms[a], n0) < 0.2f || Vector3.Dot(norms[b], n0) < 0.2f || Vector3.Dot(norms[c], n0) < 0.2f) continue;
                if (!remap.ContainsKey(a)) remap[a] = remap.Count;
                if (!remap.ContainsKey(b)) remap[b] = remap.Count;
                if (!remap.ContainsKey(c)) remap[c] = remap.Count;
                newTris.Add(remap[a]); newTris.Add(remap[b]); newTris.Add(remap[c]);
            }
            if (newTris.Count == 0) return;
            var n = remap.Count;
            var pv = new Vector3[n]; var pn = new Vector3[n]; var puv = new Vector2[n]; var pbw = new BoneWeight[n];
            var up = Mathf.Abs(Vector3.Dot(n0, Vector3.up)) < 0.9f ? Vector3.up : Vector3.forward;
            var tan = Vector3.Cross(up, n0).normalized;
            var bit = Vector3.Cross(n0, tan);
            var spin = Quaternion.AngleAxis(it.RotationDeg, n0);
            tan = spin * tan; bit = spin * bit;
            foreach (var kv in remap)
            {
                var src = kv.Key; var dst = kv.Value;
                pv[dst] = verts[src] + norms[src] * 0.0015f;
                pn[dst] = norms[src];
                pbw[dst] = bw[src];
                var d = verts[src] - anchor;
                puv[dst] = new Vector2(Vector3.Dot(d, tan) / it.Size + 0.5f, Vector3.Dot(d, bit) / it.Size + 0.5f);
            }
            var pm = new Mesh { name = "Tattoo_" + it.Id };
            pm.vertices = pv; pm.normals = pn; pm.uv = puv; pm.boneWeights = pbw; pm.bindposes = bind;
            pm.triangles = newTris.ToArray();
            var dv = new Vector3[verts.Length]; var dn = new Vector3[verts.Length]; var dt = new Vector3[verts.Length];
            var sv = new Vector3[n]; var sn = new Vector3[n];
            for (var s = 0; s < mesh.blendShapeCount; s++)
            {
                var frames = mesh.GetBlendShapeFrameCount(s);
                mesh.GetBlendShapeFrameVertices(s, frames - 1, dv, dn, dt);
                foreach (var kv in remap) { sv[kv.Value] = dv[kv.Key]; sn[kv.Value] = dn[kv.Key]; }
                pm.AddBlendShapeFrame(mesh.GetBlendShapeName(s), mesh.GetBlendShapeFrameWeight(s, frames - 1), sv, sn, null);
            }
            pm.RecalculateBounds();
            var go = new GameObject("Tattoo_" + it.Id);
            go.transform.SetParent(_smr.transform.parent, false);
            go.transform.localPosition = _smr.transform.localPosition;
            go.transform.localRotation = _smr.transform.localRotation;
            go.transform.localScale = _smr.transform.localScale;
            go.layer = gameObject.layer;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = pm; smr.bones = bones; smr.rootBone = _smr.rootBone; smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = Own(new Material(Shader.Find("WRLDZ/AvatarRecolor") ?? UnlitShader()) { name = "Tattoo_" + it.Id });
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); else mat.mainTexture = tex;
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
            if (mat.HasProperty("_ShadowAmt")) mat.SetFloat("_ShadowAmt", 0f);
            if (mat.HasProperty("_RimAmt")) mat.SetFloat("_RimAmt", 0f);
            if (mat.HasProperty("_UseTexAlpha")) mat.SetFloat("_UseTexAlpha", 1f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 2460;
            smr.sharedMaterial = mat;
            _adornments.Add(go);
        }
    }
}
