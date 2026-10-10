using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// The Spirit Dueler's aura — anime Super Saiyan flames with a Skyrim magic feel, all light, nothing dark:
    /// <list type="bullet">
    /// <item><b>Aura</b> (<c>WRLDZ/SpiritSuperAura</c>) — hugs the disk's own shape. Every frame the body is drawn
    /// into a small mipmapped mask from the viewer's eye, through the exact window a camera-facing quad covers; the
    /// shader rims that silhouette with crisp cel flame bands pulled up into tongues, a white-hot band next to the
    /// body, a thin inner rim light, and a soft haze with wisps drifting through it. It breathes and flares on
    /// summons / attacks / the blade opening.</item>
    /// <item><b>Runes</b> — glowing glyphs rising off the edges and fading, like a word wall giving up its words.</item>
    /// <item><b>Ribbons</b> — two coils of light spiralling along the blade from hub to tip (spell-charge vortex).</item>
    /// <item><b>Edge streaks</b> and <b>sparkles</b> — shed from the model's outline in world space, so they trail
    /// behind a moving arm.</item>
    /// <item><b>Deploy flare</b> — a rune circle that spins open when the blade opens.</item>
    /// </list>
    /// No smoke: the Imagine wrist-smoke PNG has no alpha channel, and alpha-blending it was the "black smoke
    /// from the deck box". Parent is the wrist aura; card zones are never touched. Presence scales everything.
    /// </summary>
    public sealed class SpiritDuelerAura : MonoBehaviour
    {
        public const int FlameMaxParticles = 70;
        public const int SparkMaxParticles = 64;
        public const int RuneMaxParticles = 16;
        public const int RibbonCount = 2;
        const int MaskSize = 256;

        const float FlameRate = 35f;
        const float SparkRate = 24f;
        const float RuneRate = 3.2f;
        const float RingSeconds = 0.9f;

        /// <summary>Shared by both disks (one static mesh) — analysed once per mesh.</summary>
        static readonly Dictionary<Mesh, Outline> OutlineCache = new();

        sealed class Outline
        {
            public Vector3[] Points;    // mesh-local
            public Vector3[] Outward;   // mesh-local, in the plate plane
            public Vector3 Thin;        // mesh-local plate normal
            public float Thickness;     // mesh-local
            public float Cell;          // mesh-local grid cell size
        }

        MeshRenderer _body;
        Bounds _localBounds = new(Vector3.zero, Vector3.one * 0.3f);
        Outline _outline;
        Mesh _bodyMesh;
        MeshRenderer _aura, _ring;
        Material _auraMat, _flameMat, _sparkMat, _trailMat, _runeMat, _ribbonMat, _ringMat;
        int _maskPass = -1;
        RenderTexture _mask;
        CommandBuffer _cb;
        ParticleSystem _flames, _sparks, _runes;
        TrailRenderer[] _ribbons;
        MaterialPropertyBlock _mpb;
        Color _accent = new(0.25f, 0.9f, 1f, 1f);
        float _presence = 1f;
        float _flameAcc, _sparkAcc, _runeAcc;
        float _ringT = -1f;
        float _surge;
        bool _built;

        /// <summary>1 on your own disk; the opponent's aura is a little quieter so it never buries the field.</summary>
        public float Emphasis = 1f;

        /// <summary>
        /// The disk is flying (the pre-duel showcase): the world-space ribbons, sparks and flame tongues would draw a
        /// streak across the view, so they stop and clear until it lands. The aura shell itself keeps hugging the disk.
        /// </summary>
        public bool Travelling
        {
            get => _travelling;
            set
            {
                if (_travelling == value) return;
                _travelling = value;
                if (_ribbons != null)
                    foreach (var r in _ribbons)
                        if (r != null) { r.Clear(); r.emitting = !value && _presence > 0.02f && _body != null; }
                if (value)
                {
                    if (_sparks != null) _sparks.Clear();
                    if (_flames != null) _flames.Clear();
                }
            }
        }
        bool _travelling;

        /// <summary>
        /// Outline points farther than this (mesh-local metres) from <see cref="_outlineCenter"/> shed nothing: with
        /// the mechanical disk in standby the blades are inside the hub, so streaks must not appear where they will be.
        /// </summary>
        public float OutlineReach = float.PositiveInfinity;
        Vector3 _outlineCenter;
        List<MeshRenderer> _bodyParts;
        readonly List<Mesh> _bodyPartMeshes = new();

        public ParticleSystem Flames => _flames;
        public ParticleSystem Sparks => _sparks;
        public ParticleSystem Runes => _runes;
        public MeshRenderer Aura => _aura;
        public TrailRenderer[] Ribbons => _ribbons;
        /// <summary>The live silhouette mask (created on the first rendered frame; null in edit mode).</summary>
        public RenderTexture Mask => _mask;
        public int OutlinePointCount => _outline != null ? _outline.Points.Length : 0;

        /// <param name="body">The DiskMesh renderer. Null = sparkles only (no aura envelope, no edge streaks).</param>
        public static SpiritDuelerAura Create(Transform auraRoot, int layer, Color accent, MeshRenderer body = null)
        {
            var go = new GameObject("SpiritManifest");
            go.transform.SetParent(auraRoot, false);
            go.layer = layer;
            var aura = go.AddComponent<SpiritDuelerAura>();
            aura.Build(layer, accent, body);
            return aura;
        }

        /// <summary>
        /// The body is a set of moving kit parts (SpiritDuelerMechanics): the silhouette mask draws each part where
        /// it is now, so the aura hugs the disk through the deploy. <paramref name="outlineCenter"/> is mesh-local.
        /// </summary>
        public void SetBodyParts(IReadOnlyList<MeshRenderer> parts, Vector3 outlineCenter)
        {
            _bodyParts = parts != null ? new List<MeshRenderer>(parts) : null;
            _bodyPartMeshes.Clear();
            if (_bodyParts != null)
                foreach (var p in _bodyParts)
                {
                    var mf = p != null ? p.GetComponent<MeshFilter>() : null;
                    _bodyPartMeshes.Add(mf != null ? mf.sharedMesh : null);
                }

            _outlineCenter = outlineCenter;
        }

        public void SetPresence(float presence)
        {
            _presence = Mathf.Clamp01(presence);
            var on = _presence > 0.02f && _body != null;
            if (!on && _aura != null) _aura.enabled = false;
            if (!on && _ring != null) _ring.enabled = false;
            if (_ribbons != null)
                foreach (var r in _ribbons)
                    if (r != null) r.emitting = on && !_travelling;
        }

        public void SetAccent(Color accent)
        {
            if (accent.a < 0.01f) accent.a = 1f;
            _accent = accent;
            if (_flames != null) ColorRamp(_flames, Hot(accent, 0.55f), accent, 0.9f);
            // sparkle colour is picked per particle (white / accent / gold); the ramp only fades
            if (_sparks != null) ColorRamp(_sparks, Color.white, Color.white, 1f);
            if (_runes != null) ColorRamp(_runes, Color.white, Hot(accent, 0.6f), 1f);
            if (_auraMat != null)
            {
                _auraMat.SetColor("_Color", Hot(accent, 0.1f));
                _auraMat.SetColor("_CoreColor", Hot(accent, 0.85f));
            }

            if (_ribbons != null)
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Hot(accent, 0.3f), 0.3f), new GradientColorKey(accent, 1f) },
                    new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
                foreach (var r in _ribbons)
                    if (r != null) r.colorGradient = g;
            }
        }

        /// <summary>The blade swung open: aura flare and shockwave ring. No spark / rune bursts (no floating
        /// orbs); spark and rune emission stays for the rest of the duel. The zone-pad sweep at the lock stays.</summary>
        public void PulseDeploy()
        {
            if (!Application.isPlaying) return;
            _surge = 1f;
            _ringT = 0f;
            var scale = BodyScale();
            if (_travelling) return;   // world-space bursts would streak behind a flying disk (pre-duel showcase)
            for (var i = 0; i < 24; i++) EmitFlame(scale, 1.35f);
        }

        /// <summary>
        /// Per frame from <see cref="SpiritDuelerSkin"/>. <paramref name="open"/> is the blade (0 folded, 1 open);
        /// <paramref name="pulse"/> is the disk's event energy (summon / attack / deploy, 0..1).
        /// </summary>
        public void Tick(float presence, float open, float pulse, Camera cam)
        {
            if (!_built) return;
            SetPresence(presence);
            var dt = Application.isPlaying ? Time.unscaledDeltaTime : 0f;
            _surge = Mathf.Max(0f, _surge - dt * 1.4f);
            var t = Time.unscaledTime;
            var breathe = 0.88f + 0.12f * Mathf.Sin(t * 2.3f) + 0.05f * Mathf.Sin(t * 6.1f + 1.3f);
            var energy = Mathf.Clamp01(pulse);
            var power = _presence * Emphasis * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(open)) * breathe
                        * (1f + 0.5f * energy + 0.9f * _surge);
            var scale = BodyScale();

            TickAura(power, cam);
            TickRing(dt, scale, cam);
            if (!Application.isPlaying || _presence <= 0.02f || _travelling) return;

            var rateMul = _presence * Emphasis * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(open))
                          * (1f + 0.8f * energy + 1.5f * _surge);
            _flameAcc += FlameRate * rateMul * dt;
            _sparkAcc += SparkRate * rateMul * dt;
            var n = Mathf.Min((int)_flameAcc, 16);
            _flameAcc -= n;
            for (var k = 0; k < n; k++) EmitFlame(scale, 1f);
            n = Mathf.Min((int)_sparkAcc, 12);
            _sparkAcc -= n;
            for (var k = 0; k < n; k++) EmitSpark(scale, 1f);
            _runeAcc += RuneRate * rateMul * dt;
            n = Mathf.Min((int)_runeAcc, 3);
            _runeAcc -= n;
            for (var k = 0; k < n; k++) EmitRune(scale);
        }

        // ------------------------------------------------------------------ build

        void Build(int layer, Color accent, MeshRenderer body)
        {
            _mpb = new MaterialPropertyBlock();
            if (accent.a < 0.01f) accent.a = 1f;
            _accent = accent;
            _body = body;

            var filter = body != null ? body.GetComponent<MeshFilter>() : null;
            _bodyMesh = filter != null ? filter.sharedMesh : null;
            if (_bodyMesh != null)
            {
                _localBounds = _bodyMesh.bounds;
                if (_bodyMesh.isReadable) _outline = OutlineFor(_bodyMesh);
            }

            var auraShader = Shader.Find("WRLDZ/SpiritSuperAura");
            if (_bodyMesh != null && auraShader != null && auraShader.isSupported)
            {
                _auraMat = new Material(auraShader) { name = "SpiritSuperAura" };
                _maskPass = _auraMat.FindPass("SpiritAuraMask");
                if (_maskPass >= 0)
                    _aura = MakeQuad("SuperAura", layer, _auraMat);
                else
                    ArObjectUtil.Destroy(_auraMat);
            }

            _flameMat = MakeMat("SpiritAuraStreak", SpiritFxTextures.Glow);
            _flames = NewSystem("EdgeStreaks", layer, _flameMat, FlameMaxParticles);
            ConfigureFlames(_flames);

            _sparkMat = MakeMat("SpiritAuraSpark", SpiritFxTextures.Star);
            _trailMat = MakeMat("SpiritAuraTrail", SpiritFxTextures.Trail);
            _sparks = NewSystem("EdgeSparkles", layer, _sparkMat, SparkMaxParticles);
            ConfigureSparks(_sparks);

            _runeMat = MakeMat("SpiritRuneGlyph", SpiritFxTextures.Runes);
            _runes = NewSystem("RisingRunes", layer, _runeMat, RuneMaxParticles);
            ConfigureRunes(_runes);

            _ribbonMat = MakeMat("SpiritRibbon", SpiritFxTextures.Trail);
            _ribbons = new TrailRenderer[RibbonCount];
            for (var i = 0; i < RibbonCount; i++)
            {
                var go = new GameObject("SwirlRibbon_" + i);
                go.layer = layer;
                go.transform.SetParent(transform, false);
                var tr = go.AddComponent<TrailRenderer>();
                tr.sharedMaterial = _ribbonMat;
                tr.time = 0.32f;
                tr.minVertexDistance = 0.004f;
                tr.numCapVertices = 3;
                tr.textureMode = LineTextureMode.Stretch;
                tr.alignment = LineAlignment.View;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.4f, 0.55f), new Keyframe(1f, 0f));
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.emitting = false;
                _ribbons[i] = tr;
            }

            _ringMat = MakeMat("SpiritDeployRing", SpiritFxTextures.Ring);
            _ring = MakeQuad("DeployRing", layer, _ringMat);
            _ring.enabled = false;

            SetAccent(accent);
            _built = true;
            SetPresence(1f);
        }

        MeshRenderer MakeQuad(string name, int layer, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.layer = layer;
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) ArObjectUtil.Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return mr;
        }

        ParticleSystem NewSystem(string name, int layer, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = layer;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 3f;
            // World: shed particles stay where they were born, so they trail off a moving arm.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;   // emitted by hand from the outline in Tick
            var sh = ps.shape;
            sh.enabled = false;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.alignment = ParticleSystemRenderSpace.View;
            rend.sortMode = ParticleSystemSortMode.None;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = LightProbeUsage.Off;
            rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            rend.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            rend.minParticleSize = 0f;
            rend.maxParticleSize = 0.5f;
            rend.sharedMaterial = mat;
            if (Application.isPlaying) ps.Play(true);
            return ps;
        }

        static void ConfigureFlames(ParticleSystem ps)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
            main.startRotation = 0f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.05f);
            noise.frequency = 2.2f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(1.1f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));

            // rising streaks of light (stretched along the velocity)
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.16f;
            rend.lengthScale = 1.7f;
        }

        void ConfigureSparks(ParticleSystem ps)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);

            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.06f);
            noise.frequency = 1.4f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.7f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.12f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));

            var trails = ps.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.28f);
            trails.minVertexDistance = 0.004f;
            trails.worldSpace = true;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.32f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trails.colorOverTrail = new ParticleSystem.MinMaxGradient(fade);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.trailMaterial = _trailMat;
        }

        static void ConfigureRunes(ParticleSystem ps)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);

            // one glyph per rune for its whole life: a random tile from the 4×4 atlas
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = SpiritFxTextures.RuneTiles;
            tsa.numTilesY = SpiritFxTextures.RuneTiles;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            tsa.cycleCount = 1;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.015f);
            noise.frequency = 0.6f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.3f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.85f)));
        }

        /// <summary>Colour over lifetime: born <paramref name="birth"/>, becomes <paramref name="body"/>, fades out.</summary>
        static void ColorRamp(ParticleSystem ps, Color birth, Color body, float peakAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(birth, 0f),
                    new GradientColorKey(body, 0.45f),
                    new GradientColorKey(body, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.1f),
                    new GradientAlphaKey(peakAlpha * 0.75f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>Additive light: HoloUnlit honours _SrcBlend/_DstBlend (Sprites/Default ignored them).</summary>
        static Material MakeMat(string name, Texture tex)
        {
            var mat = new Material(ArFieldMaterials.HoloShader) { name = name };
            if (tex == null) tex = Texture2D.whiteTexture;
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)RenderQueue.Transparent + 10;
            return mat;
        }

        // ------------------------------------------------------------------ per frame

        float BodyScale()
        {
            if (_body == null) return Mathf.Max(0.01f, transform.lossyScale.x);
            var s = _body.transform.lossyScale;
            return Mathf.Max(0.01f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
        }

        static Color Hot(Color c, float towardWhite)
        {
            var h = Color.Lerp(c, Color.white, towardWhite);
            h.a = 1f;
            return h;
        }

        /// <summary>Stand up in the world, or screen-up when looking straight down.</summary>
        static void Frame(Vector3 away, Transform camT, out Vector3 up, out Vector3 right)
        {
            var camUp = Vector3.ProjectOnPlane(camT.up, away);
            camUp = camUp.sqrMagnitude > 1e-8f ? camUp.normalized : Vector3.up;
            var worldUp = Vector3.ProjectOnPlane(Vector3.up, away);
            worldUp = worldUp.sqrMagnitude > 1e-8f ? worldUp.normalized : camUp;
            var steep = Mathf.InverseLerp(0.75f, 0.97f, Mathf.Abs(Vector3.Dot(away, Vector3.up)));
            up = Vector3.Lerp(worldUp, camUp, steep);
            up = up.sqrMagnitude > 1e-8f ? up.normalized : camUp;
            right = Vector3.Cross(up, away).normalized;
        }

        /// <summary>
        /// Place the aura quad just in front of the body, facing the eye, and draw the body's silhouette into the
        /// mask through that same window (an off-axis frustum from the eye), so mask and quad line up exactly.
        /// </summary>
        void TickAura(float power, Camera cam)
        {
            if (_aura == null) return;
            if (_body == null || cam == null || _presence <= 0.02f || !_body.enabled)
            {
                _aura.enabled = false;
                return;
            }

            var m = _body.transform.localToWorldMatrix;
            var c = m.MultiplyPoint3x4(_localBounds.center);
            var camT = cam.transform;
            var eye = camT.position;
            var toC = c - eye;
            var dc = toC.magnitude;
            if (dc < 1e-3f)
            {
                _aura.enabled = false;
                return;
            }

            var away = toC / dc;
            Frame(away, camT, out var up, out var right);
            float hw = 0f, hh = 0f, hd = 0f;
            var e = _localBounds.extents;
            for (var i = 0; i < 8; i++)
            {
                var corner = _localBounds.center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                var d = m.MultiplyPoint3x4(corner) - c;
                hw = Mathf.Max(hw, Mathf.Abs(Vector3.Dot(d, right)));
                hh = Mathf.Max(hh, Mathf.Abs(Vector3.Dot(d, up)));
                hd = Mathf.Max(hd, Mathf.Abs(Vector3.Dot(d, away)));
            }

            TickRibbons(power);

            // window at the body's depth: the silhouette plus room for the haze, and the tongues above
            var s = BodyScale();
            var pad = 0.06f * s + 0.12f * Mathf.Max(hw, hh);
            var t = Time.unscaledTime;
            var tongue = (0.12f + 0.02f * Mathf.Sin(t * 7.3f) + 0.05f * _surge) * s;
            var halfW = hw + pad;
            var below = hh + pad;
            var above = hh + pad + tongue;
            var halfH = (below + above) * 0.5f;
            var offY = (above - below) * 0.5f;

            // the quad sits just in front of the body (cards behind it are hollowed out by the mask)
            var nearBody = dc - hd;
            var dist = Mathf.Clamp(nearBody - 0.01f * s, cam.nearClipPlane * 1.5f, dc);
            var k = dist / dc;
            var rot = Quaternion.LookRotation(away, up);
            var inv = 1f / Mathf.Max(0.0001f, transform.lossyScale.x);
            var qt = _aura.transform;
            qt.SetPositionAndRotation(eye + away * dist + up * (offY * k), rot);
            qt.localScale = new Vector3(halfW * 2f * k, halfH * 2f * k, 1f) * inv;

            var near = Mathf.Max(0.005f, Mathf.Min(dist, nearBody) * 0.5f);
            var f = near / dc;
            var proj = Matrix4x4.Frustum(-halfW * f, halfW * f, (offY - halfH) * f, (offY + halfH) * f,
                near, dc + hd + 1f);
            var view = Matrix4x4.TRS(eye, rot, new Vector3(1f, 1f, -1f)).inverse;
            RenderMask(view, proj, m);

            _auraMat.SetFloat("_TongueUV", tongue / (2f * halfH));
            _auraMat.SetFloat("_Intensity", power);
            _aura.enabled = true;
        }

        void RenderMask(Matrix4x4 view, Matrix4x4 proj, Matrix4x4 model)
        {
            if (_mask == null)
            {
                var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8)
                    ? RenderTextureFormat.R8
                    : RenderTextureFormat.ARGB32;
                _mask = new RenderTexture(MaskSize, MaskSize, 0, fmt, RenderTextureReadWrite.Linear)
                {
                    name = "SpiritAuraMask",
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _mask.Create();
                _auraMat.SetTexture("_MaskTex", _mask);
                _cb = new CommandBuffer { name = "SpiritAuraMask" };
            }

            _cb.Clear();
            _cb.SetRenderTarget(_mask);
            _cb.ClearRenderTarget(false, true, Color.clear);
            _cb.SetViewProjectionMatrices(view, proj);
            if (_bodyParts != null && _bodyParts.Count > 0)
            {
                for (var k = 0; k < _bodyParts.Count; k++)
                {
                    var part = _bodyParts[k];
                    var pm = _bodyPartMeshes[k];
                    if (part == null || pm == null || !part.enabled || part.forceRenderingOff) continue;
                    _cb.DrawMesh(pm, part.transform.localToWorldMatrix, _auraMat, 0, _maskPass);
                }
            }
            else
            {
                for (var i = 0; i < _bodyMesh.subMeshCount; i++)
                    _cb.DrawMesh(_bodyMesh, model, _auraMat, i, _maskPass);
            }
            _cb.GenerateMips(_mask);
            Graphics.ExecuteCommandBuffer(_cb);
        }

        float[] _ribbonU;

        /// <summary>
        /// Two coils of light spiralling along the disk's long axis, hub to tip, half a cycle apart. Mesh-local, so
        /// they follow the blade swing; a coil that wraps back to the hub clears its trail instead of streaking back.
        /// </summary>
        void TickRibbons(float power)
        {
            if (_ribbons == null || _body == null) return;
            _ribbonU ??= new float[_ribbons.Length];
            var e = _localBounds.extents;
            var along = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            var thin = e.x <= e.y && e.x <= e.z ? 0 : e.y <= e.z ? 1 : 2;
            var mid = 3 - along - thin;
            var bt = _body.transform;
            var s = BodyScale();
            var margin = 0.035f / Mathf.Max(0.0001f, bt.lossyScale.x);
            const float period = 1.7f;
            const float turns = 2.5f;
            for (var i = 0; i < _ribbons.Length; i++)
            {
                var r = _ribbons[i];
                if (r == null) continue;
                var u = Mathf.Repeat(Time.unscaledTime / period + i * 0.5f, 1f);
                var wrapped = u < _ribbonU[i];
                _ribbonU[i] = u;
                var th = u * turns * Mathf.PI * 2f + i * Mathf.PI;
                var local = _localBounds.center;
                local[along] += Mathf.Lerp(-0.95f, 0.95f, u) * e[along];
                local[mid] += Mathf.Cos(th) * (e[mid] + margin);
                local[thin] += Mathf.Sin(th) * (e[thin] + margin);
                r.transform.position = bt.TransformPoint(local);
                if (wrapped) r.Clear();
                // fade in off the hub and out at the tip
                var ends = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.12f);
                r.widthMultiplier = 0.011f * s * Mathf.Clamp(power, 0.4f, 1.6f) * ends;
                r.emitting = Application.isPlaying && power > 0.05f;
            }
        }

        void TickRing(float dt, float scale, Camera cam)
        {
            if (_ring == null) return;
            if (_ringT < 0f || _presence <= 0.02f || _body == null)
            {
                _ring.enabled = false;
                return;
            }

            _ringT += dt;
            var u = _ringT / RingSeconds;
            if (u >= 1f)
            {
                _ringT = -1f;
                _ring.enabled = false;
                return;
            }

            _ring.enabled = true;
            var e = 1f - (1f - u) * (1f - u) * (1f - u);
            var t = _ring.transform;
            var center = _body.bounds.center;
            t.position = center;
            if (cam != null)
            {
                // the rune circle spins a quarter turn as it opens
                var look = Quaternion.LookRotation(center - cam.transform.position, cam.transform.up);
                t.rotation = look * Quaternion.Euler(0f, 0f, 90f * e);
            }

            var reach = _body.bounds.extents.magnitude * 2.4f;
            var size = Mathf.Lerp(0.1f * scale, reach, e) / Mathf.Max(0.0001f, transform.lossyScale.x);
            t.localScale = Vector3.one * size;
            var alpha = Mathf.Clamp01(u / 0.12f) * Mathf.Pow(1f - u, 1.5f);
            Tint(_ring, Hot(_accent, 0.35f), alpha * _presence);
        }

        bool SampleOutline(float jitter, out Vector3 pos, out Vector3 outward)
        {
            if (_outline == null || _body == null || _outline.Points.Length == 0)
            {
                pos = transform.position;
                outward = Vector3.up;
                return false;
            }

            var o = _outline;
            var i = Random.Range(0, o.Points.Length);
            if (!float.IsPositiveInfinity(OutlineReach))
            {
                // a few tries for a point on the part of the disk that is out right now
                var tries = 0;
                while ((o.Points[i] - _outlineCenter).sqrMagnitude > OutlineReach * OutlineReach && tries++ < 6)
                    i = Random.Range(0, o.Points.Length);
                if ((o.Points[i] - _outlineCenter).sqrMagnitude > OutlineReach * OutlineReach)
                {
                    pos = transform.position;
                    outward = Vector3.up;
                    return false;
                }
            }

            var local = o.Points[i]
                        + Random.insideUnitSphere * (o.Cell * 0.5f * jitter)
                        + o.Thin * (Random.Range(-0.5f, 0.5f) * o.Thickness);
            var bt = _body.transform;
            pos = bt.TransformPoint(local);
            outward = bt.TransformDirection(o.Outward[i]).normalized;
            return true;
        }

        void EmitFlame(float scale, float speedMul)
        {
            if (_flames == null || !SampleOutline(1f, out var p, out var outward)) return;
            // streaks rise: off the bottom edge they would cross the plate, so shed few there
            if (Vector3.Dot(outward, Vector3.up) < -0.35f && Random.value < 0.75f) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = p + outward * (0.004f * scale),
                velocity = (Vector3.up * Random.Range(0.12f, 0.26f) + outward * Random.Range(0.02f, 0.06f))
                           * (scale * speedMul),
                startSize = Random.Range(0.014f, 0.03f) * scale,
                startLifetime = Random.Range(0.4f, 0.75f),
                startColor = Color.white,
                applyShapeToPosition = false
            };
            _flames.Emit(ep, 1);
        }

        void EmitSpark(float scale, float speedMul)
        {
            if (_sparks == null) return;
            if (!SampleOutline(0.5f, out var p, out var outward))
            {
                p = transform.position + Random.insideUnitSphere * (0.08f * scale);
                outward = Random.onUnitSphere;
            }

            // a sideways swirl so the trails curl instead of rising in straight lines
            var swirl = Vector3.Cross(Vector3.up, outward);
            var ep = new ParticleSystem.EmitParams
            {
                position = p + outward * (0.006f * scale),
                velocity = (Vector3.up * Random.Range(0.08f, 0.2f)
                            + outward * Random.Range(0.04f, 0.1f)
                            + swirl * Random.Range(-0.06f, 0.06f)) * (scale * speedMul),
                startSize = Random.Range(0.009f, 0.02f) * scale,
                startLifetime = Random.Range(0.9f, 1.6f),
                startColor = SparkColor(Random.value),
                applyShapeToPosition = false
            };
            _sparks.Emit(ep, 1);
        }

        void EmitRune(float scale)
        {
            if (_runes == null || !SampleOutline(0.5f, out var p, out var outward)) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = p + outward * (0.02f * scale),
                velocity = (Vector3.up * Random.Range(0.035f, 0.065f) + outward * Random.Range(0.005f, 0.02f)) * scale,
                startSize = Random.Range(0.05f, 0.075f) * scale,
                startLifetime = Random.Range(1.8f, 2.8f),
                startColor = Color.white,
                rotation = Random.Range(-12f, 12f),
                applyShapeToPosition = false
            };
            _runes.Emit(ep, 1);
        }

        /// <summary>Mostly white-hot, some in the Spirit colour, a few gold (Heart of the Cards).</summary>
        Color SparkColor(float roll)
        {
            if (roll < 0.45f) return Color.white;
            if (roll < 0.8f) return Hot(_accent, 0.3f);
            return new Color(1f, 0.9f, 0.55f, 1f);
        }

        void Tint(MeshRenderer mr, Color c, float a)
        {
            if (mr == null) return;
            mr.GetPropertyBlock(_mpb);
            var col = new Color(c.r, c.g, c.b, a);
            _mpb.SetColor("_Color", col);
            _mpb.SetColor("_BaseColor", col);
            mr.SetPropertyBlock(_mpb);
        }

        // ------------------------------------------------------------------ mesh analysis (once per mesh)

        /// <summary>
        /// The model's outline in its own plate plane: rasterise every triangle into a grid on the two long axes,
        /// keep the covered cells that touch an empty one, and remember which way is out. Streaks and sparkles are
        /// shed from these points — off the edges, never across the card stages on the plate.
        /// </summary>
        static Outline OutlineFor(Mesh mesh)
        {
            if (OutlineCache.TryGetValue(mesh, out var cached)) return cached;

            var b = mesh.bounds;
            var ext = b.extents;
            // thinnest axis = plate normal; the other two span the plate
            var thin = ext.x <= ext.y && ext.x <= ext.z ? 0 : ext.y <= ext.z ? 1 : 2;
            var ax = thin == 0 ? 1 : 0;
            var ay = thin == 2 ? 1 : 2;
            var extent = Mathf.Max(ext[ax], ext[ay]);
            const int res = 64;
            var cell = extent * 2f / res;
            var nx = Mathf.Max(1, Mathf.CeilToInt(ext[ax] * 2f / cell));
            var ny = Mathf.Max(1, Mathf.CeilToInt(ext[ay] * 2f / cell));
            var min = b.min;
            var covered = new bool[nx * ny];
            var depthSum = new float[nx * ny];
            var depthN = new int[nx * ny];

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            for (var t = 0; t + 2 < tris.Length; t += 3)
            {
                var p0 = verts[tris[t]];
                var p1 = verts[tris[t + 1]];
                var p2 = verts[tris[t + 2]];
                var a = new Vector2((p0[ax] - min[ax]) / cell, (p0[ay] - min[ay]) / cell);
                var bb = new Vector2((p1[ax] - min[ax]) / cell, (p1[ay] - min[ay]) / cell);
                var c = new Vector2((p2[ax] - min[ax]) / cell, (p2[ay] - min[ay]) / cell);
                var depth = (p0[thin] + p1[thin] + p2[thin]) / 3f;
                var x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(bb.x, c.x))), 0, nx - 1);
                var x1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(bb.x, c.x))), 0, nx - 1);
                var y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(bb.y, c.y))), 0, ny - 1);
                var y1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(bb.y, c.y))), 0, ny - 1);
                var small = x1 - x0 <= 1 && y1 - y0 <= 1;
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    // tiny triangles mark their box; big ones test the cell centre (edge-on faces cover nothing)
                    if (!small && !InTriangle(new Vector2(x + 0.5f, y + 0.5f), a, bb, c)) continue;
                    var k = y * nx + x;
                    covered[k] = true;
                    depthSum[k] += depth;
                    depthN[k]++;
                }
            }

            var pts = new List<Vector3>();
            var outs = new List<Vector3>();
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            {
                var k = y * nx + x;
                if (!covered[k]) continue;
                var dir = Vector2.zero;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var xx = x + dx;
                    var yy = y + dy;
                    var empty = xx < 0 || yy < 0 || xx >= nx || yy >= ny || !covered[yy * nx + xx];
                    if (empty) dir += new Vector2(dx, dy);
                }

                if (dir.sqrMagnitude < 0.01f) continue;   // interior
                var p = Vector3.zero;
                p[ax] = min[ax] + (x + 0.5f) * cell;
                p[ay] = min[ay] + (y + 0.5f) * cell;
                p[thin] = depthN[k] > 0 ? depthSum[k] / depthN[k] : b.center[thin];
                var o = Vector3.zero;
                o[ax] = dir.x;
                o[ay] = dir.y;
                pts.Add(p);
                outs.Add(o.normalized);
            }

            var thinAxis = Vector3.zero;
            thinAxis[thin] = 1f;
            var outline = new Outline
            {
                Points = pts.ToArray(),
                Outward = outs.ToArray(),
                Thin = thinAxis,
                Thickness = ext[thin] * 2f,
                Cell = cell
            };
            OutlineCache[mesh] = outline;
            return outline;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            var d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            var d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            var neg = d1 < 0f || d2 < 0f || d3 < 0f;
            var pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        void OnDestroy()
        {
            ArObjectUtil.Destroy(_auraMat);
            ArObjectUtil.Destroy(_runeMat);
            ArObjectUtil.Destroy(_ribbonMat);
            if (_mask != null)
            {
                _mask.Release();
                ArObjectUtil.Destroy(_mask);
            }

            _cb?.Release();
            ArObjectUtil.Destroy(_flameMat);
            ArObjectUtil.Destroy(_sparkMat);
            ArObjectUtil.Destroy(_trailMat);
            ArObjectUtil.Destroy(_ringMat);
        }
    }
}
