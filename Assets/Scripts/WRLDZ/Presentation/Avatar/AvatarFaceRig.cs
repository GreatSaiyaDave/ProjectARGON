using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// The living face of an ArgonAvatarRig body (2026-10-18): two eyes, two brows, a nose and a mouth, each a small quad
    /// on the face under the Head bone, drawn by WRLDZ/AvatarAnimeFace (signed-distance anime shapes: crisp at any zoom).
    /// Everything is a parameter, so the face is fully animated:
    /// <list type="bullet">
    /// <item>blinks on its own schedule (2.2–6.5 s, the occasional double), the lids closing over the iris;</item>
    /// <item>gaze: the irises drift and settle on their own, or follow <see cref="LookAt"/> (the player's camera, the
    /// opponent) within the eye, with the small saccades a real eye makes;</item>
    /// <item>expressions (<see cref="FaceCatalog.ExpressionFor"/>): brows, lids, corners and mouth ease between moods;
    /// <see cref="PlayBeat"/> maps a duel beat onto one and relaxes back;</item>
    /// <item>speech (<see cref="Say"/>): the mouth opens and shapes vowels in a syllable rhythm for the line's length,
    /// or follows a live level through <see cref="SpeechLevel"/>.</item>
    /// </list>
    /// The painted features of the skin texture under it are flattened by AvatarRecolor's _FaceFlatMap
    /// (OverworldAvatar3d sets it), so the drawn features replace them instead of sitting on top.
    /// </summary>
    // after every pose writer: ArOpponentDuelist.Solve (650) turns the neck and head toward its look target, the highest
    // order in the project is 820; the cards' skin follow must read the final neck / head pose of the frame
    [DefaultExecutionOrder(900)]
    public sealed class AvatarFaceRig : MonoBehaviour
    {
        // ---- placement on the face, metres at body scale 1, in the body's own frame (up / forward) around the head bone:
        // the eye line and face depth are the blink card's measured values; the eye centres sit ±3.1 cm off the midline
        const float EyeUp = 0.033f, EyeOut = 0.031f, EyeW = 0.060f, EyeH = 0.040f;
        const float BrowUp = 0.057f, BrowW = 0.060f, BrowH = 0.034f;
        const float NoseUp = 0.004f, NoseW = 0.040f, NoseH = 0.040f, NoseFwd = 0.004f;
        const float MouthUp = -0.016f, MouthW = 0.084f, MouthH = 0.044f, MouthFwd = -0.003f;
        const float FaceFwdMale = 0.1235f, FaceFwdFemale = 0.1085f;
        const float EyeYawDeg = 11f;   // each eye quad turns a little outward to follow the face's curve

        /// <summary>Opponents / previews look here when set (world); null = the face drifts on its own.</summary>
        public Transform LookTarget;
        /// <summary>A live speech level 0..1 (audio RMS) drives the mouth while &gt; 0; <see cref="Say"/> fakes one.</summary>
        public float SpeechLevel;
        /// <summary>0 hides the whole face (silhouettes), 1 shows it.</summary>
        public float Presence { get; private set; } = 1f;
        /// <summary>Blink on the rig's own schedule (off for card renders, which must never catch a half-closed eye).</summary>
        public bool Blink = true;

        Transform _head, _body;
        bool _female;
        float _scale = 1f;
        Material _eyeL, _eyeR, _browL, _browR, _nose, _mouth;
        Mesh _bake;   // the body's posed mesh, for FitToSurface

        /// <summary>
        /// A fitted card's skinning: the face skin is not rigid to the head (the front of the face carries 6-37 % Neck
        /// weight), so a card that only rides the Head bone slides against the skin whenever the head pitches against
        /// the neck (idle, duel beats). Each card vertex keeps the bone weights of the skin triangle under it, and
        /// <see cref="Follow"/> moves the vertices with non-Head weight by those bones' motion since the fit
        /// (linear-blend skinning in the card's own frame; the Head share is the card's parenting itself).
        /// </summary>
        sealed class CardFollow
        {
            public MeshFilter Filter;
            public Vector3[] Rest;              // the fitted vertices (card-local)
            public Vector3[] Work;
            public int[] Index;                 // the vertices that carry a non-Head weight
            public int[] Bone;                  // 3 per entry: slots in _followBones (-1 = none)
            public float[] Weight;              // 3 per entry
            public Matrix4x4 CardAtFit;         // the card's localToWorld at the fit
            public Matrix4x4[] Last;            // per follow bone: the last applied motion
        }
        readonly List<CardFollow> _follow = new();
        readonly List<Transform> _followBones = new();
        readonly List<Matrix4x4> _followBind = new();   // each follow bone's worldToLocal at the fit
        readonly List<GameObject> _quads = new();
        readonly List<Material> _owned = new();
        Shader _shader;

        // state
        FaceCatalog.EyeShape _eye = FaceCatalog.Eyes[0];
        FaceCatalog.MouthShape _mouthShape = FaceCatalog.Mouths[0];
        FaceCatalog.Expression _cur = FaceCatalog.Expression.Neutral, _target = FaceCatalog.Expression.Neutral;
        string _mood = "neutral";
        float _moodUntil = -1f;
        float _blink = -1f, _nextBlink = 1.8f;
        bool _doubleBlink;
        System.Random _rng;
        Vector2 _gaze, _gazeTarget, _gazeVel;
        float _nextGlance = 1f;
        float _say = -1f, _sayLen, _sayPhase, _sayOpen, _sayShape;
        Color _skin = new(0.98f, 0.77f, 0.58f), _ink = new(0.10f, 0.06f, 0.08f);

        static readonly int PId = Shader.PropertyToID("_Part"), SkinId = Shader.PropertyToID("_Skin"), InkId = Shader.PropertyToID("_Ink");

        // ------------------------------------------------------------------ build

        /// <summary>Attach (or fetch) the face of <paramref name="body"/>: the OverworldAvatar3d root, its Head bone, the sex for the face depth.</summary>
        public static AvatarFaceRig Ensure(GameObject body, Transform head, bool female)
        {
            if (body == null || head == null) return null;
            var rig = body.GetComponent<AvatarFaceRig>();
            if (rig == null) rig = body.AddComponent<AvatarFaceRig>();
            rig.Bind(head, female);
            return rig;
        }

        void Bind(Transform head, bool female)
        {
            if (_head == head && _female == female && _quads.Count > 0) return;
            _head = head; _female = female; _body = transform;
            _rng ??= new System.Random(GetEntityId().GetHashCode());
            _nextBlink = 0.8f + (float)_rng.NextDouble() * 2.5f;
            Rebuild();
        }

        void Rebuild()
        {
            foreach (var q in _quads) if (q != null) Kill(q);
            _quads.Clear();
            foreach (var m in _meshes) if (m != null) Kill(m);
            _meshes.Clear();
            foreach (var m in _owned) if (m != null) Kill(m);
            _owned.Clear();
            _shader = Shader.Find("WRLDZ/AvatarAnimeFace");
            if (_shader == null || _head == null) return;
            _scale = Mathf.Max(0.05f, _body.lossyScale.y);
            var fwd = _female ? FaceFwdFemale : FaceFwdMale;
            _eyeL = Quad("FaceEyeL", 0, -EyeOut, EyeUp, fwd, EyeW, EyeH, -EyeYawDeg, 0);
            _eyeR = Quad("FaceEyeR", 0, EyeOut, EyeUp, fwd, EyeW, EyeH, EyeYawDeg, 1);
            _browL = Quad("FaceBrowL", 3, -EyeOut - 0.002f, BrowUp, fwd - 0.003f, BrowW, BrowH, -EyeYawDeg, 0);
            _browR = Quad("FaceBrowR", 3, EyeOut + 0.002f, BrowUp, fwd - 0.003f, BrowW, BrowH, EyeYawDeg, 1);
            _nose = Quad("FaceNose", 2, 0f, NoseUp, fwd + NoseFwd, NoseW, NoseH, 0, 0);
            _mouth = Quad("FaceMouth", 1, 0f, MouthUp, fwd + MouthFwd, MouthW, MouthH, 0, 0);
            ApplyStatic();
        }

        /// <summary>One feature quad: a child of the Head bone at (out, up, fwd) in the body's frame, facing forward, yawed to the cheek.</summary>
        const int GridX = 24, GridY = 16;  // the feature card's grid (2.5 mm cells on the eye): FitToSurface bends it onto the skin
        const int FitSub = 2;              // the exact skin depth is sampled FitSub times finer than the card grid
        readonly List<Mesh> _meshes = new();

        /// <summary>A unit card (±0.5, z 0, uv 0..1) as a grid, so the fit can drape it over the face.</summary>
        Mesh GridCard(string name)
        {
            var m = new Mesh { name = "FaceCard_" + name };
            var v = new Vector3[(GridX + 1) * (GridY + 1)]; var uv = new Vector2[v.Length];
            for (var j = 0; j <= GridY; j++)
                for (var i = 0; i <= GridX; i++)
                {
                    var k = j * (GridX + 1) + i;
                    v[k] = new Vector3(i / (float)GridX - 0.5f, j / (float)GridY - 0.5f, 0f);
                    uv[k] = new Vector2(i / (float)GridX, j / (float)GridY);
                }
            var tri = new int[GridX * GridY * 6]; var t = 0;
            for (var j = 0; j < GridY; j++)
                for (var i = 0; i < GridX; i++)
                {
                    var a = j * (GridX + 1) + i; var b = a + 1; var c = a + GridX + 1; var d = c + 1;
                    // wound to face -Z like Unity's Quad primitive (the shader culls nothing anyway)
                    tri[t++] = a; tri[t++] = c; tri[t++] = b; tri[t++] = b; tri[t++] = c; tri[t++] = d;
                }
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            _meshes.Add(m);
            return m;
        }

        Material Quad(string name, int part, float outM, float upM, float fwdM, float wM, float hM, float yawDeg, int mirror)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = GridCard(name);
            var s = _scale;
            var up = _body.up; var f = _body.forward; var r = _body.right;
            go.transform.position = _head.position + r * (outM * s) + up * (upM * s) + f * (fwdM * s);
            go.transform.rotation = Quaternion.LookRotation(-f, up) * Quaternion.Euler(0f, -yawDeg, 0f);
            go.transform.localScale = new Vector3(wM * s, hM * s, 1f);
            go.transform.SetParent(_head, true);
            go.layer = gameObject.layer;
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(_shader) { name = "AnimeFace_" + name };
            mat.SetFloat(PId, part);
            mat.SetFloat("_Mirror", mirror);
            mat.renderQueue = 2470 + part;   // after the skin, the decals (2460) and the old blink card (2461)
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _owned.Add(mat);
            _quads.Add(go);
            return mat;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDestroy()
        {
            foreach (var m in _owned) if (m != null) Kill(m);
            _owned.Clear();
        }

        // ------------------------------------------------------------------ the look

        /// <summary>Apply the look's face choices (shapes, iris, brow colour, skin) — call after the body's paints.</summary>
        public void Apply(AvatarAppearance look, Color skin, Color hairRoot)
        {
            if (look == null) return;
            // a canon duelist's face (CharacterLook.FaceId) is measured from the sourced art and overrides the look's picks
            var cf = FaceCatalog.ForCharacter(look.faceId);
            _eye = cf?.Eye ?? FaceCatalog.Eye(string.IsNullOrEmpty(look.eyeId) ? FaceCatalog.DefaultEye : look.eyeId);
            _mouthShape = cf?.Mouth ?? FaceCatalog.Mouth(string.IsNullOrEmpty(look.mouthId) ? FaceCatalog.DefaultMouth : look.mouthId);
            var nose = cf?.Nose ?? FaceCatalog.Nose(string.IsNullOrEmpty(look.noseId) ? FaceCatalog.DefaultNose : look.noseId);
            var brow = cf?.Brow ?? FaceCatalog.Brow(string.IsNullOrEmpty(look.browId) ? FaceCatalog.DefaultBrow : look.browId);
            _hideEye = cf?.HideEye ?? 0;
            _skin = skin;
            var irisHex = cf != null && !string.IsNullOrEmpty(cf.IrisHex) ? cf.IrisHex : string.IsNullOrEmpty(look.irisHex) ? FaceCatalog.DefaultIris : look.irisHex;
            var iris = ColorUtility.TryParseHtmlString(irisHex, out var ic) ? ic : new Color(0.36f, 0.24f, 0.62f);
            Color.RGBToHSV(iris, out var h, out var sa, out var v);
            var irisLight = Color.HSVToRGB(h, Mathf.Clamp01(sa * 0.75f), Mathf.Clamp01(v * 1.25f + 0.12f));
            var irisDark = Color.HSVToRGB(h, Mathf.Clamp01(sa * 1.1f), v * 0.45f);
            // brows take the hair's root colour unless the player chose one; a very light hair keeps a readable brow
            var browHex = cf != null && !string.IsNullOrEmpty(cf.BrowHex) ? cf.BrowHex : look.browHex;
            var browCol = ColorUtility.TryParseHtmlString(browHex, out var bc) ? bc : hairRoot;
            if (browCol.maxColorComponent > 0.75f && string.IsNullOrEmpty(browHex)) browCol *= 0.55f;
            browCol.a = 1f;
            foreach (var m in new[] { _eyeL, _eyeR })
            {
                if (m == null) continue;
                m.SetColor(SkinId, _skin); m.SetColor(InkId, _ink);
                m.SetFloat("_EyeW", _eye.W); m.SetFloat("_EyeH", _eye.H); m.SetFloat("_Tilt", _eye.Tilt); m.SetFloat("_Round", _eye.Round);
                m.SetFloat("_TopBulge", _eye.TopBulge); m.SetFloat("_BotBulge", _eye.BotBulge);
                m.SetFloat("_Lash", _eye.Lash); m.SetFloat("_LashOuter", _eye.LashOuter); m.SetFloat("_LowerLash", _eye.LowerLash);
                m.SetFloat("_Pupil", _eye.Pupil); m.SetFloat("_IrisScaleY", _eye.IrisScaleY);
                m.SetColor("_IrisColor", iris); m.SetColor("_IrisLight", irisLight); m.SetColor("_IrisDark", irisDark);
            }
            foreach (var m in new[] { _browL, _browR })
            {
                if (m == null) continue;
                m.SetColor("_BrowColor", browCol); m.SetFloat("_BrowThick", brow.Thick); m.SetFloat("_BrowArch", brow.Arch);
                m.SetFloat("_BrowPeak", cf?.BrowPeak ?? 0.25f);
            }
            _browRestTilt = brow.Tilt;
            if (_nose != null)
            {
                _nose.SetColor(SkinId, _skin);
                _nose.SetFloat("_NoseStyle", nose.Style); _nose.SetFloat("_NoseSize", nose.Size); _nose.SetFloat("_NoseShade", nose.Shade);
            }
            if (_mouth != null)
            {
                _mouth.SetColor(SkinId, _skin); _mouth.SetColor(InkId, _ink);
                _mouth.SetFloat("_MouthW", _mouthShape.W); _mouth.SetFloat("_LipLine", _mouthShape.LipLine); _mouth.SetFloat("_LowerLip", _mouthShape.LowerLip);
            }
            FitToSurface();
            ApplyStatic();
            Tick(0f);
        }

        /// <summary>
        /// Each feature card drapes over the sculpt under it: the body mesh is baked in its current pose, its triangles
        /// are projected into the card's frame, and the exact skin depth (barycentric, frontmost surface) is sampled on
        /// a grid <see cref="FitSub"/> times finer than the card's. Every card vertex then takes the deepest-forward
        /// sample of all the cells it touches, plus a margin: a card is linear between its vertices, so this keeps it
        /// above the skin everywhere, not only at the sampled points (the first cut took the nearest body VERTEX, and
        /// the skin between sparse vertices - nose bridge, brow ridge, cheekbone - cut through the eyes in jagged
        /// shapes). A decal projection: the eye follows the socket and the cheek instead of standing off the temple as
        /// a flat plane when the head turns (AR duels show every angle). Only skin within <c>reach</c> of the card's
        /// plane counts (a raised hand or the back of the head never lifts it). Measured in the card's frame, so the
        /// pose does not matter; the depth is divided by the card's own world scale on z (the stature scales the head).
        /// </summary>
        /// <summary>Drape the cards again on the body as it is now (OverworldAvatar3d.Apply calls it last, once the stature and the stance have settled).</summary>
        public void Refit() => FitToSurface();

        void FitToSurface()
        {
            if (_body == null || _quads.Count == 0) return;
            SkinnedMeshRenderer smr = null;
            foreach (var r in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name.StartsWith("ArgonBody") && r.sharedMesh != null) { smr = r; break; }
            if (smr == null) return;
            _bake ??= new Mesh();
            smr.BakeMesh(_bake, true);
            var vs = _bake.vertices;
            var tris = _bake.triangles;
            var m = smr.transform.localToWorldMatrix;
            var world = new Vector3[vs.Length];
            for (var i = 0; i < vs.Length; i++) world[i] = m.MultiplyPoint3x4(vs[i]);
            const int FX = GridX * FitSub, FY = GridY * FitSub;
            var fine = new float[(FX + 1) * (FY + 1)];
            var next = new float[fine.Length];
            var fTri = new int[fine.Length]; var fL0 = new float[fine.Length]; var fL1 = new float[fine.Length];
            var nTri = new int[fine.Length]; var nL0 = new float[fine.Length]; var nL1 = new float[fine.Length];
            var bw = smr.sharedMesh.boneWeights;
            var bones = smr.bones;
            _follow.Clear(); _followBones.Clear(); _followBind.Clear();
            var px = new float[world.Length]; var py = new float[world.Length]; var pd = new float[world.Length];
            foreach (var q in _quads)
            {
                if (q == null) continue;
                var t = q.transform;
                var mf = q.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // the margin ahead of the skin: the lips and the nose tip are rounded, so a little more there
                var margin = (q.name == "FaceNose" ? 0.0025f : q.name == "FaceMouth" ? 0.002f : 0.0015f) * _scale;
                var reach = 0.03f * _scale;
                var n = -t.forward;                                  // the card looks down -forward: this is out of the face
                var right = t.right; var up = t.up; var c = t.position;
                var sx = t.lossyScale.x; var sy = t.lossyScale.y; var sz = Mathf.Max(1e-6f, Mathf.Abs(t.lossyScale.z));
                var hw = sx * 0.5f; var hh = sy * 0.5f;
                for (var i = 0; i < world.Length; i++)
                {
                    var w = world[i] - c;
                    px[i] = Vector3.Dot(w, right); py[i] = Vector3.Dot(w, up); pd[i] = Vector3.Dot(w, n);
                }
                for (var k = 0; k < fine.Length; k++) { fine[k] = float.NegativeInfinity; fTri[k] = -1; }
                var minDet = 1e-9f * sx * sx;
                for (var ti = 0; ti + 2 < tris.Length; ti += 3)
                {
                    int a = tris[ti], b = tris[ti + 1], e = tris[ti + 2];
                    if (pd[a] > reach || pd[b] > reach || pd[e] > reach) continue;            // in front of the face: not skin under the card
                    if (pd[a] < -reach && pd[b] < -reach && pd[e] < -reach) continue;         // far behind it
                    float x0 = px[a], y0 = py[a], x1 = px[b], y1 = py[b], x2 = px[e], y2 = py[e];
                    var minx = Mathf.Min(x0, Mathf.Min(x1, x2)); var maxx = Mathf.Max(x0, Mathf.Max(x1, x2));
                    var miny = Mathf.Min(y0, Mathf.Min(y1, y2)); var maxy = Mathf.Max(y0, Mathf.Max(y1, y2));
                    if (maxx < -hw || minx > hw || maxy < -hh || miny > hh) continue;
                    var det = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                    if (Mathf.Abs(det) < minDet) continue;                                    // seen edge-on
                    var i0 = Mathf.Max(0, Mathf.CeilToInt((minx / sx + 0.5f) * FX)); var i1 = Mathf.Min(FX, Mathf.FloorToInt((maxx / sx + 0.5f) * FX));
                    var j0 = Mathf.Max(0, Mathf.CeilToInt((miny / sy + 0.5f) * FY)); var j1 = Mathf.Min(FY, Mathf.FloorToInt((maxy / sy + 0.5f) * FY));
                    for (var j = j0; j <= j1; j++)
                    {
                        var gy = (j / (float)FY - 0.5f) * sy;
                        for (var i = i0; i <= i1; i++)
                        {
                            var gx = (i / (float)FX - 0.5f) * sx;
                            var l0 = ((y1 - y2) * (gx - x2) + (x2 - x1) * (gy - y2)) / det;
                            var l1 = ((y2 - y0) * (gx - x2) + (x0 - x2) * (gy - y2)) / det;
                            var l2 = 1f - l0 - l1;
                            if (l0 < -1e-4f || l1 < -1e-4f || l2 < -1e-4f) continue;
                            var d = l0 * pd[a] + l1 * pd[b] + l2 * pd[e];
                            var k = j * (FX + 1) + i;
                            if (d > fine[k]) { fine[k] = d; fTri[k] = ti; fL0[k] = l0; fL1[k] = l1; }
                        }
                    }
                }
                // samples past the skin's silhouette take their filled neighbours' depth
                var filled = 0;
                for (var k = 0; k < fine.Length; k++) if (!float.IsNegativeInfinity(fine[k])) filled++;
                if (filled == 0) continue;
                for (var pass = 0; pass < FX + FY && filled < fine.Length; pass++)
                {
                    System.Array.Copy(fine, next, fine.Length); System.Array.Copy(fTri, nTri, fine.Length);
                    System.Array.Copy(fL0, nL0, fine.Length); System.Array.Copy(fL1, nL1, fine.Length);
                    for (var j = 0; j <= FY; j++)
                        for (var i = 0; i <= FX; i++)
                        {
                            var k = j * (FX + 1) + i;
                            if (!float.IsNegativeInfinity(fine[k])) continue;
                            var src = -1;
                            if (i > 0 && (src < 0 || fine[k - 1] > fine[src])) src = k - 1;
                            if (i < FX && (src < 0 || fine[k + 1] > fine[src])) src = k + 1;
                            if (j > 0 && (src < 0 || fine[k - FX - 1] > fine[src])) src = k - FX - 1;
                            if (j < FY && (src < 0 || fine[k + FX + 1] > fine[src])) src = k + FX + 1;
                            if (src < 0 || float.IsNegativeInfinity(fine[src])) continue;
                            next[k] = fine[src]; nTri[k] = fTri[src]; nL0[k] = fL0[src]; nL1[k] = fL1[src]; filled++;
                        }
                    System.Array.Copy(next, fine, fine.Length); System.Array.Copy(nTri, fTri, fine.Length);
                    System.Array.Copy(nL0, fL0, fine.Length); System.Array.Copy(nL1, fL1, fine.Length);
                }
                // each card vertex: the most forward sample of every cell it touches (one card cell each way)
                var verts = mf.sharedMesh.vertices;
                for (var j = 0; j <= GridY; j++)
                    for (var i = 0; i <= GridX; i++)
                    {
                        var best = float.NegativeInfinity;
                        for (var fj = Mathf.Max(0, (j - 1) * FitSub); fj <= Mathf.Min(FY, (j + 1) * FitSub); fj++)
                            for (var fi = Mathf.Max(0, (i - 1) * FitSub); fi <= Mathf.Min(FX, (i + 1) * FitSub); fi++)
                                best = Mathf.Max(best, fine[fj * (FX + 1) + fi]);
                        var k = j * (GridX + 1) + i;
                        if (k >= verts.Length || float.IsNegativeInfinity(best)) continue;
                        var z = Mathf.Clamp(best + margin, -reach, reach);
                        verts[k].z = -z / sz;                         // local -z is out of the face
                    }
                mf.sharedMesh.vertices = verts;
                mf.sharedMesh.RecalculateBounds();

                // the skin's bone weights under every card vertex (barycentric over the frontmost triangle there)
                var cf = new CardFollow { Filter = mf, Rest = verts, Work = (Vector3[])verts.Clone(), CardAtFit = t.localToWorldMatrix };
                var idx = new List<int>(); var slot = new List<int>(); var wt = new List<float>();
                var acc = new Dictionary<int, float>();
                for (var j = 0; j <= GridY; j++)
                    for (var i = 0; i <= GridX; i++)
                    {
                        var k = j * (GridX + 1) + i;
                        var fk = (j * FitSub) * (FX + 1) + i * FitSub;
                        var tri = fTri[fk];
                        if (k >= verts.Length || tri < 0 || bw == null || bw.Length == 0) continue;
                        acc.Clear();
                        var l0 = fL0[fk]; var l1 = fL1[fk]; var l2 = 1f - l0 - l1;
                        AddWeights(acc, bw[tris[tri]], l0); AddWeights(acc, bw[tris[tri + 1]], l1); AddWeights(acc, bw[tris[tri + 2]], l2);
                        // the Head share rides the card's parenting; keep the three strongest other bones
                        float total = 0f; foreach (var kv in acc) total += kv.Value;
                        if (total <= 1e-6f) continue;
                        int b0 = -1, b1 = -1, b2 = -1; float w0 = 0, w1 = 0, w2 = 0;
                        foreach (var kv in acc)
                        {
                            if (kv.Key < 0 || kv.Key >= bones.Length || bones[kv.Key] == null || bones[kv.Key] == _head) continue;
                            var w = kv.Value / total;
                            if (w > w0) { b2 = b1; w2 = w1; b1 = b0; w1 = w0; b0 = kv.Key; w0 = w; }
                            else if (w > w1) { b2 = b1; w2 = w1; b1 = kv.Key; w1 = w; }
                            else if (w > w2) { b2 = kv.Key; w2 = w; }
                        }
                        if (w0 < 0.005f) continue;                    // rigid with the head
                        idx.Add(k);
                        foreach (var (bi, w) in new[] { (b0, w0), (b1, w1), (b2, w2) })
                        {
                            if (bi < 0 || w < 0.005f) { slot.Add(-1); wt.Add(0f); continue; }
                            var sIdx = _followBones.IndexOf(bones[bi]);
                            if (sIdx < 0) { sIdx = _followBones.Count; _followBones.Add(bones[bi]); _followBind.Add(bones[bi].worldToLocalMatrix); }
                            slot.Add(sIdx); wt.Add(w);
                        }
                    }
                cf.Index = idx.ToArray(); cf.Bone = slot.ToArray(); cf.Weight = wt.ToArray();
                _follow.Add(cf);
            }
            foreach (var cf in _follow) { cf.Last = new Matrix4x4[_followBones.Count]; for (var b = 0; b < cf.Last.Length; b++) cf.Last[b] = Matrix4x4.identity; }
        }

        static void AddWeights(Dictionary<int, float> acc, BoneWeight w, float l)
        {
            if (l <= 0f) return;
            void Add(int i, float v) { if (v <= 0f) return; acc[i] = (acc.TryGetValue(i, out var o) ? o : 0f) + v * l; }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        }

        /// <summary>
        /// Moves the card vertices that carry non-Head weight with their bones (see <see cref="CardFollow"/>). Per bone the
        /// motion in the card's frame is M = card(now)^-1 * bone(now) * bone(fit)^-1 * card(fit); a vertex is the blend of
        /// its rest position (the Head share) and M applied to it. Skipped while no bone moved against the head.
        /// </summary>
        void Follow()
        {
            if (_follow.Count == 0 || _followBones.Count == 0) return;
            var moved = new Matrix4x4[_followBones.Count];
            foreach (var cf in _follow)
            {
                if (cf.Filter == null || cf.Filter.sharedMesh == null || cf.Index == null || cf.Index.Length == 0) continue;
                var cardInv = cf.Filter.transform.worldToLocalMatrix;
                var changed = false;
                for (var b = 0; b < _followBones.Count; b++)
                {
                    var bone = _followBones[b];
                    moved[b] = bone != null ? cardInv * bone.localToWorldMatrix * _followBind[b] * cf.CardAtFit : Matrix4x4.identity;
                    if (!changed)
                        for (var e = 0; e < 16 && !changed; e++) changed = Mathf.Abs(moved[b][e] - cf.Last[b][e]) > 1e-5f;
                }
                if (!changed) continue;
                for (var b = 0; b < _followBones.Count; b++) cf.Last[b] = moved[b];
                for (var n = 0; n < cf.Index.Length; n++)
                {
                    var k = cf.Index[n]; var v0 = cf.Rest[k];
                    var rigid = 1f; var v = Vector3.zero;
                    for (var c = 0; c < 3; c++)
                    {
                        var sl = cf.Bone[n * 3 + c]; if (sl < 0) continue;
                        var w = cf.Weight[n * 3 + c];
                        v += w * moved[sl].MultiplyPoint3x4(v0); rigid -= w;
                    }
                    cf.Work[k] = v + rigid * v0;
                }
                cf.Filter.sharedMesh.vertices = cf.Work;
                cf.Filter.sharedMesh.RecalculateBounds();
            }
        }

        float _browRestTilt;
        int _hideEye;   // CharacterFace.HideEye: 1 = the character's left eye quad stays off, 2 = the right

        void ApplyStatic()
        {
            foreach (var q in _quads)
            {
                if (q == null) continue;
                var on = Presence > 0.01f;
                if (q.name == "FaceEyeL" && _hideEye == 1) on = false;
                if (q.name == "FaceEyeR" && _hideEye == 2) on = false;
                q.SetActive(on);
            }
        }

        public void SetPresence(float a)
        {
            Presence = Mathf.Clamp01(a);
            ApplyStatic();
        }

        /// <summary>The body's presence fade (OverworldAvatar3d.ApplyAlpha): the features fade with the skin.</summary>
        public void SetAlpha(float alpha)
        {
            foreach (var m in _owned) if (m != null) m.SetFloat("_Alpha", Mathf.Clamp01(alpha));
        }

        // ------------------------------------------------------------------ moods / speech

        /// <summary>Wear a mood ("angry", "happy", "smug", "pain", "sad", "surprised", "determined", "laugh", "scheme", "neutral") for <paramref name="hold"/> s (0 = until the next call).</summary>
        public void SetMood(string mood, float hold = 0f)
        {
            _mood = mood ?? "neutral";
            _target = FaceCatalog.ExpressionFor(_mood);
            _moodUntil = hold > 0f ? Time.unscaledTime + hold : -1f;
        }

        /// <summary>A duel beat (ArOpponentDuelist.Play): the face that goes with it.</summary>
        public void PlayBeat(string beat)
        {
            var (mood, hold) = FaceCatalog.MoodForBeat(beat);
            SetMood(mood, hold);
        }

        /// <summary>Speak for <paramref name="seconds"/>: the mouth cycles syllables with vowel shapes; audio can take over through <see cref="SpeechLevel"/>.</summary>
        public void Say(float seconds)
        {
            _say = 0f; _sayLen = Mathf.Max(0.3f, seconds); _sayPhase = 0f;
        }

        /// <summary>Look at a world point now (eyes only; the head is the duelist's own LookAt).</summary>
        public void LookAt(Vector3 world) { _lookPoint = world; _hasLookPoint = true; _lookStamp = Time.unscaledTime; }
        Vector3 _lookPoint; bool _hasLookPoint; float _lookStamp;

        // ------------------------------------------------------------------ per frame

        int _faults;

        void LateUpdate()
        {
            // a fault breaker: a per-frame exception here once wrote a 10 GB Editor log (the rig's random generator was
            // never created on an instance Ensure() had skipped); whatever breaks next, the face goes still, not the Editor
            try { Tick(Mathf.Min(Time.unscaledDeltaTime, 0.05f)); _faults = 0; }
            catch (System.Exception e)
            {
                if (++_faults >= 3) { Debug.LogError("[AvatarFaceRig] disabled after repeated faults: " + e); enabled = false; }
            }
        }

        /// <summary>Edit-mode previews drive the face by hand (no Player loop).</summary>
        public void Tick(float dt)
        {
            if (_eyeL == null) return;
            // a Play-mode recompile restores the Unity references but not the plain classes: hold still until Apply runs again
            if (_eye == null || _mouthShape == null) return;
            _rng ??= new System.Random(GetEntityId().GetHashCode());
            Follow();
            var now = Time.unscaledTime;
            // mood relax
            if (_moodUntil > 0f && now > _moodUntil) { _moodUntil = -1f; _mood = "neutral"; _target = FaceCatalog.Expression.Neutral; }
            var k = dt > 0f ? 1f - Mathf.Exp(-dt / 0.14f) : 1f;
            _cur = FaceCatalog.Lerp(_cur, _target, k);

            // blink schedule (never while a mood holds the eyes shut)
            float blinkLid = 0f;
            if (!Blink) { _blink = -1f; }
            else if (_blink < 0f)
            {
                _nextBlink -= dt;
                if (_nextBlink <= 0f) _blink = 0f;
            }
            else
            {
                _blink += dt;
                const float close = 0.06f, hold = 0.04f, open = 0.09f;
                if (_blink < close) blinkLid = Mathf.SmoothStep(0f, 1f, _blink / close);
                else if (_blink < close + hold) blinkLid = 1f;
                else if (_blink < close + hold + open) blinkLid = 1f - Mathf.SmoothStep(0f, 1f, (_blink - close - hold) / open);
                else
                {
                    _blink = -1f;
                    if (!_doubleBlink && _rng.NextDouble() < 0.18) { _doubleBlink = true; _nextBlink = 0.14f; }
                    else { _doubleBlink = false; _nextBlink = 2.2f + (float)_rng.NextDouble() * 4.3f; }
                }
            }

            // gaze: a target in the eye's own range (-0.6..0.6 of the eye), from the look point or a wandering glance
            Vector2 want;
            if (LookTarget != null || (_hasLookPoint && now - _lookStamp < 2f))
            {
                var p = LookTarget != null ? LookTarget.position : _lookPoint;
                var local = _head.InverseTransformPoint(p);
                // the head's forward in this rig is the body's forward; take the direction in the body frame instead
                var dir = (p - _head.position).normalized;
                var side = Vector3.Dot(dir, _body.right); var upd = Vector3.Dot(dir, _body.up); var fwd = Vector3.Dot(dir, _body.forward);
                want = fwd > 0.05f ? new Vector2(Mathf.Clamp(side / fwd * 0.9f, -0.6f, 0.6f), Mathf.Clamp(upd / fwd * 0.9f, -0.45f, 0.45f)) : Vector2.zero;
                _ = local;
            }
            else
            {
                _nextGlance -= dt;
                if (_nextGlance <= 0f)
                {
                    // a glance aside, or back to centre (where the eyes spend most of their time)
                    _gazeTarget = _rng.NextDouble() < 0.45 ? Vector2.zero
                        : new Vector2(((float)_rng.NextDouble() - 0.5f) * 0.7f, ((float)_rng.NextDouble() - 0.5f) * 0.35f);
                    _nextGlance = 0.9f + (float)_rng.NextDouble() * 3.2f;
                }
                want = _gazeTarget;
            }
            // saccade: fast, slightly overshooting, then still
            _gaze = Vector2.SmoothDamp(_gaze, want, ref _gazeVel, 0.07f, 12f, Mathf.Max(dt, 1e-3f));

            // speech: syllables at ~5 Hz with vowel shapes; a live level overrides
            float talkOpen = 0f, talkShape = _mouthShape.OpenShape;
            if (SpeechLevel > 0.001f)
            {
                talkOpen = Mathf.Clamp01(SpeechLevel * 1.4f);
                talkShape = Mathf.Lerp(0.2f, 0.7f, Mathf.PerlinNoise(now * 3.1f, 0.3f));
            }
            else if (_say >= 0f)
            {
                _say += dt;
                if (_say > _sayLen) _say = -1f;
                else
                {
                    _sayPhase += dt * (4.2f + 2.2f * Mathf.PerlinNoise(now * 0.9f, 7f));
                    var syl = Mathf.Abs(Mathf.Sin(_sayPhase * Mathf.PI));
                    var stress = 0.55f + 0.45f * Mathf.PerlinNoise(_sayPhase * 0.37f, 2f);
                    var tail = Mathf.Clamp01((_sayLen - _say) / 0.25f) * Mathf.Clamp01(_say / 0.12f);
                    talkOpen = syl * stress * tail;
                    talkShape = Mathf.Lerp(0.1f, 0.9f, Mathf.PerlinNoise(_sayPhase * 0.61f, 5f));
                }
            }
            _sayOpen = Mathf.Lerp(_sayOpen, talkOpen, 1f - Mathf.Exp(-dt / 0.035f));
            _sayShape = Mathf.Lerp(_sayShape, talkShape, 1f - Mathf.Exp(-dt / 0.08f));

            // ---- push to the materials
            var lid = Mathf.Clamp01(Mathf.Max(_eye.RestLid + _cur.Lid, blinkLid));
            var lidCurve = Mathf.Clamp(_cur.LidCurve, -0.3f, 1f) * (1f - blinkLid * 0.6f);
            var irisR = _eye.Iris * _cur.IrisScale;
            foreach (var m in new[] { _eyeL, _eyeR })
            {
                if (m == null) continue;
                m.SetFloat("_Lid", lid);
                m.SetFloat("_LidCurve", Mathf.Max(0f, lidCurve));
                m.SetFloat("_LowerLid", Mathf.Clamp01(_cur.LowerLid));
                m.SetFloat("_Iris", irisR);
                m.SetVector("_IrisPos", new Vector4(m == _eyeR ? -_gaze.x : _gaze.x, _gaze.y, 0f, 0f));
            }
            foreach (var m in new[] { _browL, _browR })
            {
                if (m == null) continue;
                m.SetFloat("_BrowLift", _cur.BrowLift);
                m.SetFloat("_BrowTilt", _browRestTilt + _cur.BrowTilt);
            }
            if (_mouth != null)
            {
                var open = Mathf.Clamp01(Mathf.Max(_cur.Open, _sayOpen));
                var shape = _sayOpen > _cur.Open ? _sayShape : Mathf.Lerp(_mouthShape.OpenShape, _cur.OpenShape, Mathf.Clamp01(_cur.Open * 3f));
                _mouth.SetFloat("_Open", open);
                _mouth.SetFloat("_OpenShape", shape);
                _mouth.SetFloat("_Corner", Mathf.Clamp(_mouthShape.RestCorner + _cur.Corner, -1f, 1f));
                _mouth.SetFloat("_Teeth", Mathf.Clamp01(Mathf.Max(_cur.Teeth, 0.5f)));
                _mouth.SetFloat("_Tongue", 0.4f);
            }
        }
    }
}
