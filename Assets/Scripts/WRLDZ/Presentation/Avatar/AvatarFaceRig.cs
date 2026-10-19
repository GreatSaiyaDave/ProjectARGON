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
        const int GridX = 14, GridY = 8;   // the feature card's grid: FitToSurface bends it onto the skin
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
        /// Each feature card drapes over the sculpt under it: the body mesh is baked in its current pose and every grid
        /// vertex of the card slides along the card's normal to the skin depth under it (the most protruding body vertex
        /// within a small window, else the nearest ones) plus a margin — a decal projection, so the eye follows the
        /// socket and the cheek instead of standing off the temple as a flat plane when the head turns (AR duels show
        /// every angle). The face planes (FaceFwdMale / Female) are only the first guess: the nose tip stands ~1.5 cm
        /// proud of the eye line and the head / face morphs move every feature's depth. Measured in the card's frame,
        /// so the pose does not matter.
        /// </summary>
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
            var m = smr.transform.localToWorldMatrix;
            var world = new Vector3[vs.Length];
            for (var i = 0; i < vs.Length; i++) world[i] = m.MultiplyPoint3x4(vs[i]);
            var px = new List<float>(); var py = new List<float>(); var pd = new List<float>();
            var depth = new float[(GridX + 1) * (GridY + 1)];
            foreach (var q in _quads)
            {
                if (q == null) continue;
                var t = q.transform;
                var mf = q.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // the margin ahead of the skin: the lips and the nose tip are rounded, so a little more there
                var margin = (q.name == "FaceNose" ? 0.0025f : q.name == "FaceMouth" ? 0.002f : 0.0015f) * _scale;
                var n = -t.forward;                                  // the card looks down -forward: this is out of the face
                var right = t.right; var up = t.up; var c = t.position;
                var sx = t.lossyScale.x; var sy = t.lossyScale.y;
                var hw = sx * 0.5f; var hh = sy * 0.5f;
                var skirt = 0.008f * _scale;
                px.Clear(); py.Clear(); pd.Clear();
                var overall = float.NegativeInfinity;
                for (var i = 0; i < world.Length; i++)
                {
                    var w = world[i] - c;
                    var x = Vector3.Dot(w, right); var y = Vector3.Dot(w, up);
                    if (Mathf.Abs(x) > hw + skirt || Mathf.Abs(y) > hh + skirt) continue;
                    var d = Vector3.Dot(w, n);
                    px.Add(x); py.Add(y); pd.Add(d);
                    if (d > overall) overall = d;
                }
                if (px.Count == 0) continue;
                var win = 0.006f * _scale;
                for (var j = 0; j <= GridY; j++)
                    for (var i = 0; i <= GridX; i++)
                    {
                        var gx = (i / (float)GridX - 0.5f) * sx; var gy = (j / (float)GridY - 0.5f) * sy;
                        var best = float.NegativeInfinity;
                        // the most protruding skin vertex within the window ...
                        for (var k = 0; k < px.Count; k++)
                            if (Mathf.Abs(px[k] - gx) <= win && Mathf.Abs(py[k] - gy) <= win && pd[k] > best) best = pd[k];
                        if (float.IsNegativeInfinity(best))
                        {
                            // ... else the three nearest, past the skin's edge (the outer corner over the temple)
                            float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue; float v0 = 0, v1 = 0, v2 = 0;
                            for (var k = 0; k < px.Count; k++)
                            {
                                var dd = (px[k] - gx) * (px[k] - gx) + (py[k] - gy) * (py[k] - gy);
                                if (dd < d0) { d2 = d1; v2 = v1; d1 = d0; v1 = v0; d0 = dd; v0 = pd[k]; }
                                else if (dd < d1) { d2 = d1; v2 = v1; d1 = dd; v1 = pd[k]; }
                                else if (dd < d2) { d2 = dd; v2 = pd[k]; }
                            }
                            best = Mathf.Max(v0, Mathf.Max(v1, v2));
                        }
                        depth[j * (GridX + 1) + i] = best;
                    }
                // a light smoothing pass keeps the sparse mesh from printing steps into the card
                var sm = (float[])depth.Clone();
                for (var j = 0; j <= GridY; j++)
                    for (var i = 0; i <= GridX; i++)
                    {
                        float acc = 0; var cnt = 0;
                        for (var dj = -1; dj <= 1; dj++)
                            for (var di = -1; di <= 1; di++)
                            {
                                var ii = i + di; var jj = j + dj;
                                if (ii < 0 || jj < 0 || ii > GridX || jj > GridY) continue;
                                acc += depth[jj * (GridX + 1) + ii]; cnt++;
                            }
                        sm[j * (GridX + 1) + i] = Mathf.Max(acc / cnt, depth[j * (GridX + 1) + i] - 0.002f * _scale);
                    }
                var verts = mf.sharedMesh.vertices;
                for (var k = 0; k < verts.Length && k < sm.Length; k++)
                {
                    var z = Mathf.Clamp(sm[k] + margin, -0.03f * _scale, 0.03f * _scale);
                    verts[k].z = -z;                                  // local -z is out of the face (lossyScale.z = 1)
                }
                mf.sharedMesh.vertices = verts;
                mf.sharedMesh.RecalculateBounds();
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
