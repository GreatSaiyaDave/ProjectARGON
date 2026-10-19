using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Hair that obeys physics and does not clip (2026-10-19, owner: "the hair and assets need to obey real physics and
    /// not clip"). A damped spring stands for the hanging mass of the hair: it trails the head's motion (inertia), and when
    /// the head tilts the hair falls toward real gravity (the sculpts are shaped for an upright head, so only the change
    /// counts). WRLDZ/AvatarHair applies the swing per vertex in proportion to how far a strand reaches out of the scalp
    /// (roots never move; spikes above the ears move a quarter as much) and keeps swung strands out of the skull, the neck
    /// and the chest - never moving a strand whose sculpted rest position is inside them, so a sculpt keeps its shape.
    /// Hair lying over eyewear is tucked behind the lens plane (<see cref="EyewearGlass"/>).
    /// The colliders are measured once per body from the body mesh's own bind pose (exact, no baking).
    /// </summary>
    [DefaultExecutionOrder(915)]   // after every pose writer, the face rig (900) and the eyewear (910)
    public sealed class HairPhysics : MonoBehaviour
    {
        const float Frequency = 1.6f;      // Hz: a long ponytail / Mai's mane swings about this slowly
        const float Damping = 0.28f;       // damping ratio: a couple of visible swings, then still
        const float StiffAboveEars = 0.25f;  // gelled spikes swing a quarter as far as hanging hair
        const float AirDrag = 0.8f;          // 1/s: a brisk walk trails the hair ~1 cm
        const float SleeveAllowance = 0.02f; // metres round the arm's skin: a sleeve and the strand's own thickness

        Transform _head, _neck, _chest, _body;
        SkinnedMeshRenderer _hair;
        EyewearGlass _eyewear;
        readonly List<Material> _mats = new();
        // all in head-bone space, measured at rest from the bindposes (the pose at Bind time never matters)
        Vector3 _skullC, _axR, _axU, _axF;   // skull centre; the body's right / up / forward at rest
        Vector3 _skullR;                     // skull radii along those axes (head-bone units)
        float _reachLocal;                   // how far the hair reaches beyond the scalp (head-bone units)
        float _neckRadiusLocal;              // neck-bone units
        float _chestHalfWidth;               // metres
        // the arms: upper arms and forearms as capsules (bone -> child), radius in the bone's units (the outermost sleeve)
        readonly Transform[] _limbA = new Transform[4], _limbB = new Transform[4];
        readonly float[] _limbR = new float[4];
        static readonly (HumanBodyBones a, HumanBodyBones b)[] Limbs =
        {
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
        };
        static readonly int[] LimbAId = { Shader.PropertyToID("_HairArm0A"), Shader.PropertyToID("_HairArm1A"), Shader.PropertyToID("_HairArm2A"), Shader.PropertyToID("_HairArm3A") };
        static readonly int[] LimbBId = { Shader.PropertyToID("_HairArm0B"), Shader.PropertyToID("_HairArm1B"), Shader.PropertyToID("_HairArm2B"), Shader.PropertyToID("_HairArm3B") };
        // measurements per mesh (a 400k-vertex mane is read once, not on every customizer change)
        static readonly Dictionary<(Mesh, Mesh), float> ReachCache = new();
        static readonly Dictionary<Mesh, float[]> LimbCache = new();
        Vector3 _p, _v;                      // the spring's mass (world)
        Vector3 _lastAnchor;
        bool _bound, _primed;

        static readonly int OnId = Shader.PropertyToID("_HairPhysOn"), HeadId = Shader.PropertyToID("_HairHead");
        static readonly int AxRId = Shader.PropertyToID("_HairAxisR"), AxUId = Shader.PropertyToID("_HairAxisU"), AxFId = Shader.PropertyToID("_HairAxisF");
        static readonly int SkullId = Shader.PropertyToID("_HairSkull"), SwingId = Shader.PropertyToID("_HairSwing");
        static readonly int NeckAId = Shader.PropertyToID("_HairNeckA"), NeckBId = Shader.PropertyToID("_HairNeckB");
        static readonly int ChestId = Shader.PropertyToID("_HairChest"), ChestRId = Shader.PropertyToID("_HairChestR");
        static readonly int BodyRId = Shader.PropertyToID("_HairBodyR"), BodyUId = Shader.PropertyToID("_HairBodyU"), BodyFId = Shader.PropertyToID("_HairBodyF");
        static readonly int GlassOnId = Shader.PropertyToID("_HairGlassOn"), GlassMapId = Shader.PropertyToID("_HairGlassMap");
        static readonly int GlassOId = Shader.PropertyToID("_HairGlassO"), GlassUId = Shader.PropertyToID("_HairGlassU");
        static readonly int GlassVId = Shader.PropertyToID("_HairGlassV"), GlassNId = Shader.PropertyToID("_HairGlassN"), GlassRectId = Shader.PropertyToID("_HairGlassRect");

        // ---- the glasses' clearance map (built once the eyewear has measured its lenses): in the glasses' own plane
        // (head-bone space), the depth a strand may come forward to at each spot - behind the glass and the frame, ramping
        // back out past the rim, but never into the face's skin. The hair shader moves a strand in front of it back to it.
        const int MapW = 128, MapH = 88;
        const float MapSlope = 0.7f;        // past the frame a strand comes forward again at 35 degrees
        SkinnedMeshRenderer _bodySmr;
        Texture2D _glassMap;
        bool _glassTried;
        Vector3 _gO, _gU, _gV, _gN;         // head-bone space: origin (lens 0 centre), across, up, outward
        Vector4 _gRect;                     // u0, v0, u span, v span (head-bone units)

        // ---- the torso: long hair hangs over the outermost layer there - the body, or the coat over it (Mai's Meshy
        // coat stands 8 cm off her back and her mane hung inside it; Pegasus' hair hung inside his jacket). A mane is a
        // thick block of layers, so it is moved as a block: for each spot on the back, the depth the DEEPEST layer of hair
        // sits inside the worn layers (measured once from the posed meshes, in the upper chest's frame), smoothed, and
        // every layer there moves back by that much - pushing only the inner layers out crushed the block inside out.
        const int HullUV = 64, HullV = 104;   // cells across and down the back
        static readonly Vector2 HullMin = new(-0.36f, -0.85f), HullMax = new(0.36f, 0.32f);   // metres right / up of the upper chest
        const float HullMargin = 0.012f;    // a strand plus the idle's sway of the spine under the upper chest
        const int SideW = 60; const float SideMin = -0.30f, SideSpan = 0.66f;   // through the body, metres back of the upper chest
        Vector3 _bR, _bU, _bN;              // upper-chest space: the body's right, up and BACKWARD at rest
        Texture2D _hullPush;
        Animator _hullAnim;
        int _hullDue;
        static readonly Dictionary<string, float[]> PushCache = new();
        static readonly int HullOnId = Shader.PropertyToID("_HairHullOn"), HullOId = Shader.PropertyToID("_HairHullO");
        static readonly int HullUId = Shader.PropertyToID("_HairHullU"), HullVId = Shader.PropertyToID("_HairHullV"), HullNId = Shader.PropertyToID("_HairHullN");
        static readonly int HullPushId = Shader.PropertyToID("_HairHullPush");

        /// <summary>Attach (or fetch) the hair physics of the avatar <paramref name="root"/>.</summary>
        public static HairPhysics Ensure(GameObject root)
        {
            if (root == null) return null;
            if (!root.TryGetComponent<HairPhysics>(out var hp)) hp = root.AddComponent<HairPhysics>();
            return hp;
        }

        /// <summary>The swing (world) the hair carries now: zero at rest; previews and probes read it.</summary>
        public Vector3 Swing { get; private set; }

        /// <summary>Bind to the body (<paramref name="bodySmr"/>) and the grafted hair; null hair turns the physics off.</summary>
        public void Bind(Animator anim, SkinnedMeshRenderer bodySmr, SkinnedMeshRenderer hair)
        {
            foreach (var m in _mats) if (m != null && m.HasProperty(OnId)) m.SetFloat(OnId, 0f);
            _mats.Clear(); _bound = false; _primed = false; Swing = Vector3.zero;
            _hair = hair; _body = transform; _bodySmr = bodySmr;
            _eyewear = GetComponentInChildren<EyewearGlass>(true);
            _glassTried = false;
            if (_glassMap != null) { Destroy(_glassMap); _glassMap = null; }
            KillHull();
            if (anim == null || !anim.isHuman || bodySmr == null || hair == null) return;
            _head = anim.GetBoneTransform(HumanBodyBones.Head);
            _neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            _chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            if (_head == null) return;
            if (!RestFrame(bodySmr, _head, out var bodyBind, out var meshToBody)) return;

            // the body's axes at rest, in head-bone space (mesh space -> bone space through the head's bind pose)
            _axR = bodyBind.MultiplyVector(meshToBody.MultiplyVector(Vector3.right)).normalized;
            _axU = bodyBind.MultiplyVector(meshToBody.MultiplyVector(Vector3.up)).normalized;
            _axF = bodyBind.MultiplyVector(meshToBody.MultiplyVector(Vector3.forward)).normalized;

            // the skull: the body's vertices that ride the head alone, boxed along the body's axes
            var skull = BoneLocalVertices(bodySmr, _head, bodyBind, 0.9f);
            if (skull.Count < 50) return;
            Vector3 mn = Vector3.one * 1e9f, mx = -mn;
            foreach (var p in skull)
            {
                var l = new Vector3(Vector3.Dot(p, _axR), Vector3.Dot(p, _axU), Vector3.Dot(p, _axF));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            var cb = (mn + mx) * 0.5f;
            _skullC = _axR * cb.x + _axU * cb.y + _axF * cb.z;
            _skullR = (mx - mn) * 0.5f * 0.97f;   // a hair's breadth inside the skin: strands resting on it stay free

            // how far the hair reaches beyond the scalp: the 90th percentile of the strands' distance outside the skull
            if (!ReachCache.TryGetValue((hair.sharedMesh, bodySmr.sharedMesh), out var reach))
            {
                var beyond = new List<float>();
                if (RestFrame(hair, _head, out var hairBind, out _))
                    foreach (var p in BoneLocalVertices(hair, _head, hairBind, 0.5f))
                        beyond.Add(Mathf.Max(0f, SkullMetric(p) - 1f) * Mathf.Min(_skullR.x, _skullR.z));
                beyond.Sort();
                reach = beyond.Count > 0 ? beyond[Mathf.Clamp((int)(beyond.Count * 0.9f), 0, beyond.Count - 1)] : -1f;
                ReachCache[(hair.sharedMesh, bodySmr.sharedMesh)] = reach;
            }
            _reachLocal = reach >= 0f ? Mathf.Max(0.03f * MetresToHead(), reach) : 0.08f * MetresToHead();

            // the neck: the median distance of its own vertices from the neck->head segment, at rest, in neck-bone space
            _neckRadiusLocal = 0f;
            if (_neck != null && RestFrame(bodySmr, _neck, out var neckBind, out _))
            {
                var tip = _neck.InverseTransformPoint(_head.position);
                var d = new List<float>();
                foreach (var p in BoneLocalVertices(bodySmr, _neck, neckBind, 0.6f))
                {
                    var t = Mathf.Clamp01(Vector3.Dot(p, tip) / Mathf.Max(tip.sqrMagnitude, 1e-10f));
                    d.Add((p - tip * t).magnitude);
                }
                if (d.Count > 10) { d.Sort(); _neckRadiusLocal = d[d.Count / 2]; }
            }
            var s = Mathf.Max(1e-4f, _body.lossyScale.y);
            var lu = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var ru = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _chestHalfWidth = lu != null && ru != null ? (lu.position - ru.position).magnitude * 0.5f / s : 0.18f;

            // the arms: the body's own arm (its median radius round the bone) plus a sleeve's allowance - the sculpts were
            // shaped for arms held out, and lowered arms passed through a long mane (Mai's). A flared coat is cloth, not
            // a rigid collider: measuring it (Mai's coat: 11 cm round the upper arm) wrapped her hair round her shoulders.
            var tips = new Vector3[4];
            for (var k = 0; k < 4; k++)
            {
                _limbA[k] = anim.GetBoneTransform(Limbs[k].a); _limbB[k] = anim.GetBoneTransform(Limbs[k].b); _limbR[k] = 0f;
                if (_limbA[k] != null && _limbB[k] != null) tips[k] = _limbA[k].InverseTransformPoint(_limbB[k].position);
            }
            var radii = LimbRadii(bodySmr, _limbA, tips);
            for (var k = 0; k < 4; k++)
                if (radii[k] > 0f) _limbR[k] = radii[k] + SleeveAllowance * s / Mathf.Max(1e-6f, _limbA[k].lossyScale.x);

            foreach (var m in hair.sharedMaterials) if (m != null && m.HasProperty(OnId)) _mats.Add(m);
            _bound = _mats.Count > 0;
            // the hull is taken from the pose on screen: a few frames on, once the animator has moved the body off its
            // bind pose (a hull baked in the bind pose would hold the arms out and the coat's panels with them)
            _hullAnim = anim; _hullDue = _bound ? Time.frameCount + 3 : 0;
        }

        void KillHull()
        {
            if (_hullPush != null) Destroy(_hullPush);
            _hullPush = null;
        }

        /// <summary>
        /// The mane's push: rasterise how far back the worn layers reach (the body and its garments as they stand now -
        /// a coat's back panels may ride the arm bones, so only the posed mesh has them in place; the sleeves and the
        /// head are left to their own colliders), then find, per spot, how deep the hair's deepest layer sits inside
        /// them, and smooth it so the mane moves as one. Cached per set of worn meshes and hair.
        /// </summary>
        void BuildHull(Animator anim, SkinnedMeshRenderer hair)
        {
            if (_chest == null || _bodySmr == null || hair == null || !RestFrame(_bodySmr, _chest, out var cb, out var meshToBody)) return;
            _bR = cb.MultiplyVector(meshToBody.MultiplyVector(Vector3.right)).normalized;
            _bU = cb.MultiplyVector(meshToBody.MultiplyVector(Vector3.up)).normalized;
            _bN = -cb.MultiplyVector(meshToBody.MultiplyVector(Vector3.forward)).normalized;
            var pieces = new List<SkinnedMeshRenderer>(); var key = new System.Text.StringBuilder();
            key.Append(hair.sharedMesh != null ? hair.sharedMesh.GetHashCode() : 0).Append('|');
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r == hair || r.name.StartsWith("ArgonHair") || r.sharedMesh == null || !r.sharedMesh.isReadable) continue;
                pieces.Add(r); key.Append(r.sharedMesh.GetHashCode()).Append(';');
            }
            if (!PushCache.TryGetValue(key.ToString(), out var push))
            {
                const float Big = 1e4f;
                var size = HullMax - HullMin;
                var back = Fill(HullUV * HullV, -Big);
                // the other four views decide only which hair is the mane (its nearest way out of the worn layers is the
                // back); a curl through a coat's shoulder is not, and dragging it back with the mane stretched it
                var front = Fill(HullUV * HullV, Big);
                var left = Fill(SideW * HullV, Big); var right = Fill(SideW * HullV, -Big); var top = Fill(HullUV * SideW, -Big);
                // the frame now (world -> metres right / up / back of the upper chest), relative to the bone: no large sums
                var s = Mathf.Max(1e-4f, _body.lossyScale.y);
                var o = _chest.position; var wr = _chest.TransformDirection(_bR).normalized; var wu = _chest.TransformDirection(_bU).normalized; var wn = _chest.TransformDirection(_bN).normalized;
                Vector3 Local(Vector3 d) => new Vector3(Vector3.Dot(d, wr), Vector3.Dot(d, wu), Vector3.Dot(d, wn)) / s;
                Vector3 Cell(Vector3 l) => new((l.x - HullMin.x) / size.x * HullUV, (l.y - HullMin.y) / size.y * HullV, l.z);
                Vector3 Side(Vector3 l) => new((l.z - SideMin) / SideSpan * SideW, (l.y - HullMin.y) / size.y * HullV, l.x);
                Vector3 Above(Vector3 l) => new((l.x - HullMin.x) / size.x * HullUV, (l.z - SideMin) / SideSpan * SideW, l.y);
                // the arms as they hang now, a little thicker than their colliders, from a quarter down the upper arm (the
                // shoulder and a coat's shoulder blades stay); the forearm on through the hand
                var capA = new Vector3[4]; var capB = new Vector3[4]; var capR = new float[4];
                for (var k = 0; k < 4; k++)
                    if (_limbA[k] != null && _limbB[k] != null && _limbR[k] > 0f)
                    {
                        capA[k] = _limbA[k].position - o; capB[k] = _limbB[k].position - o;
                        capR[k] = _limbR[k] * _limbA[k].lossyScale.x * 1.15f;
                        if (k < 2) capA[k] += (capB[k] - capA[k]) * 0.25f;
                        else capB[k] += (capB[k] - capA[k]) * 0.6f;
                    }
                bool InArm(Vector3 d)
                {
                    for (var k = 0; k < 4; k++)
                    {
                        if (capR[k] <= 0f) continue;
                        var ab = capB[k] - capA[k];
                        var t = Mathf.Clamp01(Vector3.Dot(d - capA[k], ab) / Mathf.Max(ab.sqrMagnitude, 1e-10f));
                        if ((d - (capA[k] + ab * t)).sqrMagnitude < capR[k] * capR[k]) return true;
                    }
                    return false;
                }
                var bake = new Mesh();
                foreach (var r in pieces)
                {
                    var mesh = r.sharedMesh;
                    // the head is the skull collider's
                    var bw = mesh.boneWeights; var bones = r.bones; var headSlot = -1;
                    for (var bi = 0; bi < bones.Length; bi++) if (bones[bi] == _head) { headSlot = bi; break; }
                    r.BakeMesh(bake, true);
                    var vs = bake.vertices; var m = r.transform.localToWorldMatrix;
                    var q = new Vector3[vs.Length]; var keep = new bool[vs.Length];
                    for (var i = 0; i < vs.Length; i++)
                    {
                        if (headSlot >= 0 && bw.Length == vs.Length && bw[i].boneIndex0 == headSlot && bw[i].weight0 > 0.5f) continue;
                        var d = m.MultiplyPoint3x4(vs[i]) - o;
                        if (InArm(d)) continue;
                        q[i] = Local(d); keep[i] = true;
                    }
                    var tr = mesh.triangles;
                    for (var t = 0; t + 2 < tr.Length; t += 3)
                    {
                        if (!keep[tr[t]] || !keep[tr[t + 1]] || !keep[tr[t + 2]]) continue;
                        Vector3 a0 = q[tr[t]], a1 = q[tr[t + 1]], a2 = q[tr[t + 2]];
                        Raster(Cell(a0), Cell(a1), Cell(a2), back, false, HullUV, HullV);
                        Raster(Cell(a0), Cell(a1), Cell(a2), front, true, HullUV, HullV);
                        Raster(Side(a0), Side(a1), Side(a2), left, true, SideW, HullV);
                        Raster(Side(a0), Side(a1), Side(a2), right, false, SideW, HullV);
                        Raster(Above(a0), Above(a1), Above(a2), top, false, HullUV, SideW);
                    }
                }
                // the hair's deepest layer inside them, per spot, and how far forward that layer sits (the mane behind the
                // spine only: front locks and the side hair beside the neck, in front of the mane's inner layer, stay)
                var pen = new float[HullUV * HullV]; var inner = Fill(HullUV * HullV, Big);
                hair.BakeMesh(bake, true);
                {
                    var hv = bake.vertices; var hm = hair.transform.localToWorldMatrix;
                    for (var i = 0; i < hv.Length; i++)
                    {
                        var l = Local(hm.MultiplyPoint3x4(hv[i]) - o);
                        if (l.z < 0f) continue;
                        var c = Cell(l);
                        int cx = (int)c.x, cy = (int)c.y;
                        if (cx < 0 || cy < 0 || cx >= HullUV || cy >= HullV) continue;
                        var ci = cy * HullUV + cx;
                        var b = back[ci];
                        if (b < -Big * 0.5f) continue;
                        var d = b + HullMargin - l.z;
                        if (d <= 0f) continue;
                        // the mane only: inside all five views, and the back its nearest way out
                        var sc = Side(l); var ac = Above(l);
                        int sx = (int)sc.x, ax = (int)ac.y;
                        if (sx < 0 || sx >= SideW || ax < 0 || ax >= SideW) continue;
                        var dFront = l.z - front[ci];
                        var dLeft = l.x - left[cy * SideW + sx]; var dRight = right[cy * SideW + sx] - l.x;
                        var dTop = top[ax * HullUV + cx] - l.y;
                        if (dFront <= 0f || dLeft <= 0f || dRight <= 0f || dTop <= 0f) continue;
                        if (d > Mathf.Min(Mathf.Min(dFront, dTop), Mathf.Min(dLeft, dRight))) continue;
                        if (d > pen[ci]) pen[ci] = Mathf.Min(d, 0.15f);
                        if (l.z < inner[ci]) inner[ci] = l.z;
                    }
                }
                Destroy(bake);
                // spread a cell (a strand's triangle spans neighbours), then smooth twice over 5x5: the mane moves as one;
                // the inner layer's depth carried out to where the smoothed push reaches, then smoothed the same way
                pen = Dilate(pen, HullUV, HullV);
                pen = Blur(Blur(pen, HullUV, HullV, 2), HullUV, HullV, 2);
                inner = FillOut(inner, HullUV, HullV, Big);
                inner = Blur(inner, HullUV, HullV, 2);
                push = new float[HullUV * HullV * 2];
                for (var i = 0; i < pen.Length; i++) { push[2 * i] = pen[i]; push[2 * i + 1] = inner[i]; }
                PushCache[key.ToString()] = push;
            }
            var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf) ? TextureFormat.RGHalf : TextureFormat.RGFloat;
            var px = new Color[HullUV * HullV];
            for (var i = 0; i < px.Length; i++) px[i] = new Color(push[2 * i], push[2 * i + 1], 0f, 0f);
            _hullPush = new Texture2D(HullUV, HullV, fmt, false, true) { name = "HairHullPush", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _hullPush.SetPixels(px); _hullPush.Apply(false, true);
        }

        static float[] Fill(int n, float v) { var a = new float[n]; for (var i = 0; i < n; i++) a[i] = v; return a; }

        /// <summary>Cells at <paramref name="empty"/> take their nearest set neighbours' value, ring by ring (none set: 0.5 m back, out of reach).</summary>
        static float[] FillOut(float[] a, int w, int h, float empty)
        {
            var o = (float[])a.Clone();
            var any = false; foreach (var v in o) any |= v < empty * 0.5f;
            if (!any) { for (var i = 0; i < o.Length; i++) o[i] = 0.5f; return o; }
            for (var pass = 0; pass < w + h; pass++)
            {
                var changed = false; var next = (float[])o.Clone();
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        if (o[y * w + x] < empty * 0.5f) continue;
                        var sum = 0f; var n = 0;
                        for (var yy = Mathf.Max(0, y - 1); yy <= Mathf.Min(h - 1, y + 1); yy++)
                            for (var xx = Mathf.Max(0, x - 1); xx <= Mathf.Min(w - 1, x + 1); xx++)
                                if (o[yy * w + xx] < empty * 0.5f) { sum += o[yy * w + xx]; n++; }
                        if (n > 0) { next[y * w + x] = sum / n; changed = true; }
                    }
                o = next;
                if (!changed) break;
            }
            return o;
        }

        static float[] Dilate(float[] a, int w, int h)
        {
            var o = new float[a.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var m = 0f;
                    for (var yy = Mathf.Max(0, y - 1); yy <= Mathf.Min(h - 1, y + 1); yy++)
                        for (var xx = Mathf.Max(0, x - 1); xx <= Mathf.Min(w - 1, x + 1); xx++)
                            m = Mathf.Max(m, a[yy * w + xx]);
                    o[y * w + x] = m;
                }
            return o;
        }

        static float[] Blur(float[] a, int w, int h, int r)
        {
            var o = new float[a.Length];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var sum = 0f; var n = 0;
                    for (var yy = Mathf.Max(0, y - r); yy <= Mathf.Min(h - 1, y + r); yy++)
                        for (var xx = Mathf.Max(0, x - r); xx <= Mathf.Min(w - 1, x + r); xx++) { sum += a[yy * w + xx]; n++; }
                    o[y * w + x] = sum / n;
                }
            return o;
        }

        /// <summary>
        /// The median distance (bone units) of <paramref name="smr"/>'s vertices that ride each limb bone from the bone's
        /// axis: how thick the arm is. Zero where the mesh has no such vertices.
        /// </summary>
        static float[] LimbRadii(SkinnedMeshRenderer smr, Transform[] limbA, Vector3[] tips)
        {
            var mesh = smr.sharedMesh;
            if (LimbCache.TryGetValue(mesh, out var hit)) return hit;
            var radii = new float[4];
            LimbCache[mesh] = radii;
            if (!mesh.isReadable) return radii;
            var bones = smr.bones; var bind = mesh.bindposes;
            var slotOf = new int[4];
            for (var k = 0; k < 4; k++)
            {
                slotOf[k] = -1;
                for (var b = 0; b < bones.Length; b++) if (limbA[k] != null && bones[b] == limbA[k]) { slotOf[k] = b; break; }
            }
            if (slotOf[0] < 0 && slotOf[1] < 0 && slotOf[2] < 0 && slotOf[3] < 0) return radii;
            var bw = mesh.boneWeights; var vs = mesh.vertices;
            if (bw.Length != vs.Length) return radii;
            var dist = new[] { new List<float>(), new List<float>(), new List<float>(), new List<float>() };
            for (var i = 0; i < vs.Length; i++)
            {
                var w = bw[i];
                if (w.weight0 < 0.6f) continue;
                for (var k = 0; k < 4; k++)
                {
                    if (w.boneIndex0 != slotOf[k] || slotOf[k] >= bind.Length) continue;
                    var q = bind[slotOf[k]].MultiplyPoint3x4(vs[i]);
                    var t = Mathf.Clamp01(Vector3.Dot(q, tips[k]) / Mathf.Max(tips[k].sqrMagnitude, 1e-10f));
                    dist[k].Add((q - tips[k] * t).magnitude);
                }
            }
            for (var k = 0; k < 4; k++)
                if (dist[k].Count > 20) { dist[k].Sort(); radii[k] = dist[k][dist[k].Count / 2]; }
            return radii;
        }

        /// <summary>Head-bone units per metre (the avatar root's scale is world units per metre).</summary>
        float MetresToHead() => Mathf.Max(1e-4f, _body.lossyScale.y) / Mathf.Max(1e-6f, _head.lossyScale.x);

        float SkullMetric(Vector3 p)
        {
            var q = p - _skullC;
            return new Vector3(Vector3.Dot(q, _axR) / _skullR.x, Vector3.Dot(q, _axU) / _skullR.y, Vector3.Dot(q, _axF) / _skullR.z).magnitude;
        }

        /// <summary>The bind pose of <paramref name="bone"/> in <paramref name="smr"/> (mesh space -> bone space) and the mesh's rotation into the body root.</summary>
        bool RestFrame(SkinnedMeshRenderer smr, Transform bone, out Matrix4x4 bind, out Matrix4x4 meshToBody)
        {
            bind = Matrix4x4.identity; meshToBody = Matrix4x4.identity;
            var mesh = smr.sharedMesh; if (mesh == null || !mesh.isReadable) return false;
            var bones = smr.bones; var slot = -1;
            for (var b = 0; b < bones.Length; b++) if (bones[b] == bone) { slot = b; break; }
            if (slot < 0 || slot >= mesh.bindposes.Length) return false;
            bind = mesh.bindposes[slot];
            // the renderer hangs fixed under the body root: its rotation relative to the root maps the body's axes
            meshToBody = Matrix4x4.Rotate(Quaternion.Inverse(smr.transform.rotation) * _body.rotation);
            return true;
        }

        /// <summary>Rest positions, in <paramref name="bone"/> space, of the vertices weighted to it at least <paramref name="minWeight"/>.</summary>
        static List<Vector3> BoneLocalVertices(SkinnedMeshRenderer smr, Transform bone, Matrix4x4 bind, float minWeight)
        {
            var list = new List<Vector3>();
            var mesh = smr.sharedMesh; var bones = smr.bones; var slot = -1;
            for (var b = 0; b < bones.Length; b++) if (bones[b] == bone) { slot = b; break; }
            var bw = mesh.boneWeights; var vs = mesh.vertices;
            for (var i = 0; i < vs.Length; i++)
            {
                if (bw.Length == vs.Length)
                {
                    var w = bw[i];
                    var hw = (w.boneIndex0 == slot ? w.weight0 : 0f) + (w.boneIndex1 == slot ? w.weight1 : 0f) + (w.boneIndex2 == slot ? w.weight2 : 0f) + (w.boneIndex3 == slot ? w.weight3 : 0f);
                    if (hw < minWeight) continue;
                }
                list.Add(bind.MultiplyPoint3x4(vs[i]));
            }
            return list;
        }

        void LateUpdate() => Step(Application.isPlaying ? Time.deltaTime : 0f);

        /// <summary>One physics step and the shader feed (edit-mode previews may call it by hand).</summary>
        public void Step(float dt)
        {
            if (!_bound || _head == null || _hair == null) return;
            if (_hullDue > 0 && Time.frameCount >= _hullDue && dt > 0f) { _hullDue = 0; BuildHull(_hullAnim, _hair); }
            var s = Mathf.Max(1e-4f, _body.lossyScale.y);
            var axR = _head.TransformDirection(_axR); var axU = _head.TransformDirection(_axU); var axF = _head.TransformDirection(_axF);
            var skullC = _head.TransformPoint(_skullC);
            var toMetres = 1f / s;
            var skullR = new Vector3(_head.TransformVector(_axR * _skullR.x).magnitude, _head.TransformVector(_axU * _skullR.y).magnitude, _head.TransformVector(_axF * _skullR.z).magnitude) * toMetres;
            var reach = _head.TransformVector(_axU * _reachLocal).magnitude * toMetres;

            // the hanging mass: below and behind the skull, as far out as the hair reaches
            var anchor = skullC + (-axU * (skullR.y + reach * 0.6f) - axF * (skullR.z * 0.4f)) * s;
            // gravity's change since the sculpt (shaped for an upright head): the hair falls toward real down
            var target = anchor + (axU - Vector3.up) * (reach * 0.5f * s);
            var step = Mathf.Min(dt, 1f / 20f);
            if (!_primed || (anchor - _lastAnchor).sqrMagnitude > 0.25f * s * s) { _p = target; _v = Vector3.zero; _primed = true; _lastAnchor = anchor; }
            // inertia against the head's ACCELERATION (a steady walk carries the hair along), plus a little air drag
            var va = step > 1e-5f ? (anchor - _lastAnchor) / step : Vector3.zero;
            _lastAnchor = anchor;
            var w = 2f * Mathf.PI * Frequency; var k = w * w; var c = 2f * Damping * w;
            var n = Mathf.Clamp(Mathf.CeilToInt(step * 240f), 1, 12);
            var h = step / n;
            for (var i = 0; i < n; i++)
            {
                var acc = k * (target - _p) - c * (_v - va) - AirDrag * _v;
                _v += acc * h; _p += _v * h;
            }
            var swing = _p - target;
            var maxSwing = reach * 0.8f * s;
            if (swing.sqrMagnitude > maxSwing * maxSwing) swing = swing.normalized * maxSwing;
            Swing = swing + (target - anchor);

            // eyewear: the clearance map, once the glasses have measured their lenses
            if (!_glassTried && _eyewear != null && _eyewear.Measured) BuildGlassMap();
            var glassOn = _glassMap != null && _eyewear != null && _eyewear.isActiveAndEnabled;
            var gO = glassOn ? _head.TransformPoint(_gO) : Vector3.zero;
            var hs = _head.lossyScale.x;
            var neckA = _neck != null ? _neck.position : skullC - axU * (0.12f * s);
            var neckR = _neck != null && _neckRadiusLocal > 0f ? _neck.TransformVector(Vector3.right * _neckRadiusLocal).magnitude : 0.05f * s;
            var chestC = _chest != null ? _chest.position + _body.up * (0.03f * s) : skullC - _body.up * (0.35f * s);
            foreach (var m in _mats)
            {
                if (m == null) continue;
                m.SetFloat(OnId, 1f);
                m.SetVector(HeadId, new Vector4(skullC.x, skullC.y, skullC.z, s));
                m.SetVector(AxRId, axR.normalized); m.SetVector(AxUId, axU.normalized); m.SetVector(AxFId, axF.normalized);
                m.SetVector(SkullId, new Vector4(skullR.x, skullR.y, skullR.z, reach));
                m.SetVector(SwingId, new Vector4(Swing.x, Swing.y, Swing.z, StiffAboveEars));
                m.SetVector(NeckAId, new Vector4(neckA.x, neckA.y, neckA.z, neckR));
                m.SetVector(NeckBId, _head.position);
                m.SetVector(ChestId, chestC);
                m.SetVector(ChestRId, new Vector4(_chestHalfWidth * 1.05f * s, 0.12f * s, 0.11f * s, 0f));
                for (var j = 0; j < 4; j++)
                {
                    if (_limbA[j] == null || _limbB[j] == null || _limbR[j] <= 0f) { m.SetVector(LimbAId[j], Vector4.zero); continue; }
                    var a = _limbA[j].position;
                    m.SetVector(LimbAId[j], new Vector4(a.x, a.y, a.z, _limbR[j] * _limbA[j].lossyScale.x));
                    m.SetVector(LimbBId[j], _limbB[j].position);
                }
                m.SetVector(BodyRId, _body.right); m.SetVector(BodyUId, _body.up); m.SetVector(BodyFId, _body.forward);
                var hullOn = _hullPush != null && _chest != null;
                m.SetFloat(HullOnId, hullOn ? 1f : 0f);
                if (hullOn)
                {
                    var ho = _chest.position;
                    m.SetVector(HullOId, new Vector4(ho.x, ho.y, ho.z, s));
                    m.SetVector(HullUId, _chest.TransformDirection(_bR).normalized); m.SetVector(HullVId, _chest.TransformDirection(_bU).normalized); m.SetVector(HullNId, _chest.TransformDirection(_bN).normalized);
                    m.SetTexture(HullPushId, _hullPush);
                }
                m.SetFloat(GlassOnId, glassOn ? 1f : 0f);
                if (!glassOn) continue;
                m.SetTexture(GlassMapId, _glassMap);
                m.SetVector(GlassOId, new Vector4(gO.x, gO.y, gO.z, hs));
                m.SetVector(GlassUId, _head.TransformDirection(_gU)); m.SetVector(GlassVId, _head.TransformDirection(_gV)); m.SetVector(GlassNId, _head.TransformDirection(_gN));
                m.SetVector(GlassRectId, _gRect);
            }
        }

        /// <summary>
        /// Rasterise, in the glasses' plane, the glasses' front (frame and glass; temple arms excluded) and the face's skin
        /// (the body's head-weighted triangles): the target is 1.5 mm behind the glasses wherever they are, rising at
        /// <see cref="MapSlope"/> away from them, and never closer than 3 mm in front of the skin (the face cards ride on it).
        /// </summary>
        void BuildGlassMap()
        {
            _glassTried = true;
            if (!_eyewear.TryLensLocal(0, out var c0, out var r0, out var n0) || !_eyewear.TryLensLocal(1, out var c1, out var r1, out var n1)) return;
            if (!_eyewear.TryMeshToHead(out var eMesh, out var eToHead)) return;
            if (_bodySmr == null || !RestFrame(_bodySmr, _head, out var bodyBind, out _)) return;
            var R = Mathf.Max(r0, r1);
            var n = (n0 + n1).normalized;
            var across = c1 - c0; across -= n * Vector3.Dot(across, n);
            if (across.sqrMagnitude < 1e-12f || R <= 0f) return;
            var u = across.normalized;
            var v = Vector3.Cross(n, u); if (Vector3.Dot(v, _axU) < 0f) v = -v;
            var L = across.magnitude;
            _gO = c0; _gU = u; _gV = v; _gN = n;
            _gRect = new Vector4(-2.2f * R, -2.2f * R, L + 4.4f * R, 4.8f * R);
            var cw = _gRect.z / MapW; var ch = _gRect.w / MapH;
            const float Big = 1e4f;
            var obs = new float[MapW * MapH]; var skin = new float[MapW * MapH];
            for (var i = 0; i < obs.Length; i++) { obs[i] = Big; skin[i] = -Big; }
            Vector3 Plane(Vector3 p) { var d = p - c0; return new Vector3((Vector3.Dot(d, u) - _gRect.x) / cw, (Vector3.Dot(d, v) - _gRect.y) / ch, Vector3.Dot(d, n)); }

            // the glasses' front: triangles whose corners all lie within a quarter lens radius behind the glass plane
            var ev = eMesh.vertices; var ep = new Vector3[ev.Length];
            for (var i = 0; i < ev.Length; i++) ep[i] = Plane(eToHead.MultiplyPoint3x4(ev[i]));
            var et = eMesh.triangles;
            for (var t = 0; t + 2 < et.Length; t += 3)
            {
                Vector3 a = ep[et[t]], b = ep[et[t + 1]], c = ep[et[t + 2]];
                if (Mathf.Min(a.z, Mathf.Min(b.z, c.z)) < -0.25f * R) continue;
                Raster(a, b, c, obs, true);
            }
            // the face's skin: the body's triangles that ride the head
            var bm = _bodySmr.sharedMesh; var bv = bm.vertices; var bw = bm.boneWeights; var bt = bm.triangles;
            var slot = -1; var bones = _bodySmr.bones;
            for (var i = 0; i < bones.Length; i++) if (bones[i] == _head) { slot = i; break; }
            var bp = new Vector3[bv.Length]; var onHead = new bool[bv.Length];
            for (var i = 0; i < bv.Length; i++)
            {
                if (bw.Length == bv.Length)
                {
                    var w = bw[i];
                    var hw = (w.boneIndex0 == slot ? w.weight0 : 0f) + (w.boneIndex1 == slot ? w.weight1 : 0f) + (w.boneIndex2 == slot ? w.weight2 : 0f) + (w.boneIndex3 == slot ? w.weight3 : 0f);
                    if (hw < 0.5f) continue;
                }
                onHead[i] = true; bp[i] = Plane(bodyBind.MultiplyPoint3x4(bv[i]));
            }
            for (var t = 0; t + 2 < bt.Length; t += 3)
            {
                int ia = bt[t], ib = bt[t + 1], ic = bt[t + 2];
                if (!onHead[ia] || !onHead[ib] || !onHead[ic]) continue;
                if (Mathf.Max(bp[ia].z, Mathf.Max(bp[ib].z, bp[ic].z)) < -1.5f * R) continue;   // the back of the head never matters
                Raster(bp[ia], bp[ib], bp[ic], skin, false);
            }

            // the target: behind the glasses, ramping out past them (a two-pass chamfer), never into the skin
            var target = new float[obs.Length];
            var margin = 0.0015f * MetresToHead();
            for (var i = 0; i < obs.Length; i++) target[i] = obs[i] < Big * 0.5f ? obs[i] - margin : Big;
            var dx = MapSlope * cw; var dy = MapSlope * ch; var dd = MapSlope * Mathf.Sqrt(cw * cw + ch * ch);
            for (var y = 0; y < MapH; y++)
                for (var x = 0; x < MapW; x++)
                {
                    var i = y * MapW + x; var m = target[i];
                    if (x > 0) m = Mathf.Min(m, target[i - 1] + dx);
                    if (y > 0) m = Mathf.Min(m, target[i - MapW] + dy);
                    if (x > 0 && y > 0) m = Mathf.Min(m, target[i - MapW - 1] + dd);
                    if (x < MapW - 1 && y > 0) m = Mathf.Min(m, target[i - MapW + 1] + dd);
                    target[i] = m;
                }
            for (var y = MapH - 1; y >= 0; y--)
                for (var x = MapW - 1; x >= 0; x--)
                {
                    var i = y * MapW + x; var m = target[i];
                    if (x < MapW - 1) m = Mathf.Min(m, target[i + 1] + dx);
                    if (y < MapH - 1) m = Mathf.Min(m, target[i + MapW] + dy);
                    if (x < MapW - 1 && y < MapH - 1) m = Mathf.Min(m, target[i + MapW + 1] + dd);
                    if (x > 0 && y < MapH - 1) m = Mathf.Min(m, target[i + MapW - 1] + dd);
                    target[i] = m;
                }
            var skinMargin = 0.003f * MetresToHead();
            var cap = 2f * R;   // nothing to keep clear: far in front of anything a strand reaches
            var px = new Color[obs.Length];
            for (var y = 0; y < MapH; y++)
                for (var x = 0; x < MapW; x++)
                {
                    // the skin, dilated a cell (a strand's triangle spans neighbouring cells)
                    var sk = -Big;
                    for (var yy = Mathf.Max(0, y - 1); yy <= Mathf.Min(MapH - 1, y + 1); yy++)
                        for (var xx = Mathf.Max(0, x - 1); xx <= Mathf.Min(MapW - 1, x + 1); xx++)
                            sk = Mathf.Max(sk, skin[yy * MapW + xx]);
                    var i = y * MapW + x;
                    var f = Mathf.Min(target[i], cap);
                    if (sk > -Big * 0.5f) f = Mathf.Max(f, sk + skinMargin);
                    px[i] = new Color(f, 0f, 0f, 0f);
                }
            var fmt = SystemInfo.SupportsTextureFormat(TextureFormat.RHalf) ? TextureFormat.RHalf : TextureFormat.RFloat;
            _glassMap = new Texture2D(MapW, MapH, fmt, false, true) { name = "HairGlassMap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _glassMap.SetPixels(px); _glassMap.Apply(false, true);
        }

        /// <summary>Rasterise a triangle (x, y in cells, z depth) into <paramref name="map"/>: the min depth (glasses) or the max (skin).</summary>
        static void Raster(Vector3 a, Vector3 b, Vector3 c, float[] map, bool keepMin, int mapW = MapW, int mapH = MapH)
        {
            var x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))); var x1 = Mathf.Min(mapW - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))); var y1 = Mathf.Min(mapH - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            if (x0 > x1 || y0 > y1) return;
            var den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    float z;
                    if (Mathf.Abs(den) > 1e-9f)
                    {
                        float qx = x + 0.5f, qy = y + 0.5f;
                        var l0 = ((b.y - c.y) * (qx - c.x) + (c.x - b.x) * (qy - c.y)) / den;
                        var l1 = ((c.y - a.y) * (qx - c.x) + (a.x - c.x) * (qy - c.y)) / den;
                        var l2 = 1f - l0 - l1;
                        // a little slack: a cell the triangle only grazes still counts (no pin-holes between triangles)
                        if (l0 < -0.15f || l1 < -0.15f || l2 < -0.15f) continue;
                        z = Mathf.Clamp01(l0) * a.z + Mathf.Clamp01(l1) * b.z + Mathf.Clamp01(l2) * c.z;
                        var sum = Mathf.Clamp01(l0) + Mathf.Clamp01(l1) + Mathf.Clamp01(l2); if (sum > 1e-6f) z /= sum;
                    }
                    else z = keepMin ? Mathf.Min(a.z, Mathf.Min(b.z, c.z)) : Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    var i = y * mapW + x;
                    map[i] = keepMin ? Mathf.Min(map[i], z) : Mathf.Max(map[i], z);
                }
        }

        void OnDisable() { foreach (var m in _mats) if (m != null && m.HasProperty(OnId)) m.SetFloat(OnId, 0f); }

        void OnDestroy() { if (_glassMap != null) Destroy(_glassMap); KillHull(); }
    }
}
