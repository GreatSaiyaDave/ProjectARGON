using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Active Field Spell as a Solid Vision environment — the anime's
    /// "the arena becomes the field" adapted for passthrough
    /// (<c>Docs/FIELD_SPELL_SOLID_VISION.md</c>):
    /// <list type="bullet">
    /// <item>sweep — a ring erupts from the owner's Field drawer side and paints the street</item>
    /// <item>walls — the card illustration on two bowed side walls that rise with the sweep</item>
    /// <item>terrain — art-baked ground under each monster (<see cref="ArFieldTerrainPad"/>)</item>
    /// <item>motes — palette atmosphere in the street volume (<see cref="ArFieldMotes"/>)</item>
    /// <item>wash — a capped tint on arena holos (<see cref="ArAnimePresentation.SetFieldWash"/>)</item>
    /// <item>signature — the field's set piece (Umi's sea, Sogen's grass) from an <see cref="ArFieldSignature"/> kit</item>
    /// </list>
    /// Midfield stays empty air — no floor carpet, no far backdrop. Disk still
    /// holds the physical card. Newest activation wins; a replaced field wipes
    /// out before the new one sweeps in.
    /// </summary>
    [DefaultExecutionOrder(750)]
    public class ArFieldSpellFloor : MonoBehaviour
    {
        const float WallHeight = 1.425f;
        const float WallAlpha = 0.52f;
        const float GroundLift = 0.03f;
        const int ArcSegs = 14;
        const int HeightSegs = 1;

        /// <summary>Beat for the disk to eject and flip the card before the street changes.</summary>
        const float SweepDelay = 0.45f;
        const float SweepSeconds = 1.35f;
        /// <summary>Width of the ramp as the ring passes a point.</summary>
        const float SweepEdge = 0.3f;
        const float DissolveSeconds = 0.6f;
        /// <summary>Faster wipe when a new Field Spell replaces the live one.</summary>
        const float ReplaceDissolveSeconds = 0.35f;
        const int RingSegs = 64;
        const float StreetHeight = 2.2f;

        const int WallTexMax = 256;
        const int TerrainTexSize = 96;

        enum Phase { Idle, SweepIn, Live, Dissolve }

        static ArFieldSpellFloor _active;

        int _layer;
        MeshRenderer _leftMr;
        MeshRenderer _rightMr;
        Material _leftMat;
        Material _rightMat;
        LineRenderer _ring;
        Material _ringMat;
        ArFieldMotes _motes;
        ArFieldSignature _signature;
        readonly System.Collections.Generic.List<FieldAnchor> _anchors = new();

        Phase _phase;
        float _phaseStart;
        float _dissolveSeconds = DissolveSeconds;
        float _dissolveFrom = 1f;
        float _presence;
        Vector3 _originLocal;
        float _sweepMaxR = 1f;
        float _centerX;
        float _halfW;
        float _halfZ;

        // What the engine wants shown (0 = nothing).
        int _targetInstance;
        CardDef _targetDef;
        bool _targetPlayerSide = true;
        int _lastYouLive;
        int _lastOppLive;
        CardDatabase _db;

        // What is rendered (or animating in/out).
        int _shownInstance;
        FieldSpellEnvironment _env = FieldSpellEnvironments.Neutral;
        Texture2D _wallTex;
        Texture2D _terrainTex;

        public Transform Root => transform;

        /// <summary>True while any part of a Field Spell environment is visible.</summary>
        public static bool IsLive => _active != null && _active._phase != Phase.Idle;

        /// <summary>Palette of the environment currently on screen.</summary>
        public static FieldSpellEnvironment Environment =>
            _active != null ? _active._env : FieldSpellEnvironments.Neutral;

        /// <summary>Art-baked ground disc (radial alpha) for terrain pads; null without art.</summary>
        public static Texture2D TerrainTexture => _active != null ? _active._terrainTex : null;

        /// <summary>Bumps whenever <see cref="Environment"/> / <see cref="TerrainTexture"/> change.</summary>
        public static int EnvironmentVersion { get; private set; }

        /// <summary>
        /// 0…1 presence of the field at a world point: ripples outward during
        /// the sweep, fades everywhere together on dissolve.
        /// </summary>
        public static float PresenceAt(Vector3 world)
        {
            var f = _active;
            if (f == null) return 0f;
            switch (f._phase)
            {
                case Phase.SweepIn:
                    return f.SweepRamp(f.transform.InverseTransformPoint(world), Time.unscaledTime);
                case Phase.Live:
                    return 1f;
                case Phase.Dissolve:
                    return f._presence * f._dissolveFrom;
                default:
                    return 0f;
            }
        }

        public static ArFieldSpellFloor Create(Transform arenaRoot, int layer)
        {
            var go = new GameObject("FieldSpellFloor");
            go.transform.SetParent(arenaRoot, false);
            go.layer = layer;
            var f = go.AddComponent<ArFieldSpellFloor>();
            f._layer = layer;
            f.Build();
            _active = f;
            return f;
        }

        void Build()
        {
            _leftMat = MakeArtMat("FieldWrapLeft");
            _rightMat = MakeArtMat("FieldWrapRight");
            _leftMr = MakeWall("WrapL", _leftMat, playerLeft: true);
            _rightMr = MakeWall("WrapR", _rightMat, playerLeft: false);
            BuildRing();
            _motes = ArFieldMotes.Create(transform, _layer);
            SetWallsVisible(false);
        }

        void OnDestroy()
        {
            if (_active == this)
            {
                _active = null;
                ArAnimePresentation.SetFieldWash(Color.white, 0f);
                EnvironmentVersion++;
            }

            ReleaseTextures();
            DestroySignature();
            ArObjectUtil.Destroy(_leftMat);
            ArObjectUtil.Destroy(_rightMat);
            ArObjectUtil.Destroy(_ringMat);
        }

        static Material MakeArtMat(string name)
        {
            var mat = ArAnimePresentation.MakeFaceMaterial(Texture2D.whiteTexture, Color.white, name);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_DOUBLESIDED_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            // URP Unlit writes alpha = 1 unless the surface is Transparent, which
            // would ignore WallAlpha and the baked edge fades.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 2455;
            SetMatColor(mat, new Color(1f, 1f, 1f, WallAlpha));
            return mat;
        }

        MeshRenderer MakeWall(string name, Material mat, bool playerLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = _layer;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = SideWrap(playerLeft);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            return mr;
        }

        void BuildRing()
        {
            var go = new GameObject("FieldSweepRing");
            go.transform.SetParent(transform, false);
            go.layer = _layer;
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false;
            _ring.loop = true;
            _ring.positionCount = RingSegs;
            _ring.numCornerVertices = 2;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.alignment = LineAlignment.View;
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
            _ringMat = new Material(sh) { name = "FieldSweepRingMat" };
            _ringMat.SetOverrideTag("RenderType", "Transparent");
            _ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _ringMat.SetInt("_ZWrite", 0);
            _ringMat.renderQueue = 3100;
            if (_ringMat.HasProperty("_Cull")) _ringMat.SetFloat("_Cull", 0f);
            _ring.sharedMaterial = _ringMat;
            _ring.enabled = false;
        }

        // ── Engine → target ─────────────────────────────────────────────────

        public void Sync(CardInstance youField, CardInstance oppField, CardDatabase db)
        {
            _db = db;
            var you = Live(youField);
            var opp = Live(oppField);
            var youId = you?.InstanceId ?? 0;
            var oppId = opp?.InstanceId ?? 0;

            // Anime: a newly activated Field Spell overwrites whatever is up.
            CardInstance target;
            if (youId != 0 && youId != _lastYouLive) target = you;
            else if (oppId != 0 && oppId != _lastOppLive) target = opp;
            else if (_targetInstance != 0 && _targetInstance == youId) target = you;
            else if (_targetInstance != 0 && _targetInstance == oppId) target = opp;
            else target = you ?? opp;

            _lastYouLive = youId;
            _lastOppLive = oppId;
            _targetInstance = target?.InstanceId ?? 0;
            _targetDef = target?.Def;
            _targetPlayerSide = target == null || target == you;
        }

        static CardInstance Live(CardInstance c)
        {
            if (c == null || !c.FaceUp || c.Def == null) return null;
            return c.Def.IsFieldSpell ? c : null;
        }

        // ── State machine ───────────────────────────────────────────────────

        void LateUpdate()
        {
            var now = Time.unscaledTime;
            switch (_phase)
            {
                case Phase.Idle:
                    if (_targetInstance != 0 && _targetDef != null)
                        Begin(now);
                    break;
                case Phase.SweepIn:
                case Phase.Live:
                    if (_targetInstance != _shownInstance)
                    {
                        _dissolveFrom = GlobalLevel(now);
                        _dissolveSeconds = _targetInstance != 0 ? ReplaceDissolveSeconds : DissolveSeconds;
                        SetPhase(Phase.Dissolve, now);
                        break;
                    }

                    if (_phase == Phase.SweepIn && now - _phaseStart >= SweepDelay + SweepSeconds + SweepEdge)
                        SetPhase(Phase.Live, now);
                    break;
                case Phase.Dissolve:
                    _presence = 1f - Mathf.Clamp01((now - _phaseStart) / _dissolveSeconds);
                    if (_presence <= 0f)
                    {
                        _shownInstance = 0;
                        DestroySignature();
                        SetPhase(Phase.Idle, now);
                    }
                    break;
            }

            Paint(now);
        }

        void Begin(float now)
        {
            _shownInstance = _targetInstance;
            _env = FieldSpellEnvironments.Resolve(_targetDef, _db);
            BakeTextures(_targetDef.id);
            EnvironmentVersion++;

            ApplyWallTexture(_leftMat);
            ApplyWallTexture(_rightMat);

            Layout();
            SyncMoteVolume();
            _motes?.SetEnvironment(_env);
            DestroySignature();
            _signature = ArFieldSignature.Create(_env, transform, _layer);
            _originLocal = ArPlaymatLayout.FieldSpellArenaLocal(_targetPlayerSide);
            _originLocal.y = GroundLift;
            _sweepMaxR = FarthestCorner(_originLocal);
            SetPhase(Phase.SweepIn, now);
        }

        void SetPhase(Phase p, float now)
        {
            _phase = p;
            _phaseStart = now;
            if (p != Phase.Dissolve) _presence = p == Phase.Idle ? 0f : 1f;
        }

        /// <summary>Overall level for wash / motes: sweep progress, 1 live, fade on dissolve.</summary>
        float GlobalLevel(float now)
        {
            switch (_phase)
            {
                case Phase.SweepIn:
                    return Mathf.SmoothStep(0f, 1f,
                        Mathf.Clamp01((now - _phaseStart - SweepDelay) / SweepSeconds));
                case Phase.Live:
                    return 1f;
                case Phase.Dissolve:
                    return _presence * _dissolveFrom;
                default:
                    return 0f;
            }
        }

        float SweepRamp(Vector3 local, float now)
        {
            var t = now - _phaseStart - SweepDelay;
            if (t <= 0f) return 0f;
            var d = new Vector2(local.x - _originLocal.x, local.z - _originLocal.z).magnitude;
            var arrive = Mathf.Clamp01(d / Mathf.Max(0.01f, _sweepMaxR)) * SweepSeconds;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - arrive) / SweepEdge));
        }

        // ── Paint ───────────────────────────────────────────────────────────

        void Paint(float now)
        {
            var level = GlobalLevel(now);
            ArAnimePresentation.SetFieldWash(_env.WashTint, _env.Wash * level);
            _motes?.SetLevel(level);

            if (_phase == Phase.Idle)
            {
                SetWallsVisible(false);
                if (_ring != null) _ring.enabled = false;
                return;
            }

            Layout();
            SyncMoteVolume();
            if (_signature != null)
            {
                ArFieldTerrainPad.CollectAnchors(transform, _anchors);
                _signature.Tick(level, Street(), _anchors, Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            }

            var hasArt = _wallTex != null;
            PaintWall(_leftMr, _leftMat, hasArt ? WallLevel(-1f, now) : 0f);
            PaintWall(_rightMr, _rightMat, hasArt ? WallLevel(1f, now) : 0f);
            PaintRing(now);
        }

        float WallLevel(float side, float now)
        {
            if (_phase == Phase.SweepIn)
                return SweepRamp(new Vector3(_centerX + side * _halfW, 0f, 0f), now);
            return PresenceAtPhase();
        }

        float PresenceAtPhase() =>
            _phase == Phase.Live ? 1f : _phase == Phase.Dissolve ? _presence * _dissolveFrom : 0f;

        void PaintWall(MeshRenderer mr, Material mat, float level)
        {
            if (mr == null) return;
            var on = level > 0.001f;
            if (mr.gameObject.activeSelf != on) mr.gameObject.SetActive(on);
            if (!on) return;

            // Walls rise out of the street as the sweep passes, sink on dissolve.
            var rise = 1f - Mathf.Pow(1f - level, 3f);
            var t = mr.transform;
            t.localPosition = new Vector3(_centerX, GroundLift, 0f);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(_halfW, WallHeight * Mathf.Max(0.02f, rise), _halfZ);
            SetMatColor(mat, new Color(1f, 1f, 1f, WallAlpha * level));
        }

        void PaintRing(float now)
        {
            if (_ring == null) return;
            var t = (now - _phaseStart - SweepDelay) / SweepSeconds;
            if (_phase != Phase.SweepIn || t <= 0f || t >= 1f)
            {
                _ring.enabled = false;
                return;
            }

            var r = Mathf.Max(0.05f, t * _sweepMaxR);
            for (var i = 0; i < RingSegs; i++)
            {
                var a = i / (float)RingSegs * Mathf.PI * 2f;
                _ring.SetPosition(i, _originLocal + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }

            var c = _env.Accent;
            c.a = 0.9f * Mathf.Pow(1f - t, 0.7f);
            _ring.startColor = c;
            _ring.endColor = c;
            _ring.widthMultiplier = 0.09f * Mathf.Max(0.3f, ArPlaymatLayout.LiveHoloScale);
            _ring.enabled = true;
        }

        void Layout()
        {
            // Player faces +Z (opponent). Left = −X, right = +X of the street.
            _halfW = ArPlaymatLayout.MonsterColumnPitch * 2f + 0.72f;
            _halfZ = ArPlaymatLayout.LiveSpellTrapRowFromMid + 0.55f;
            _centerX = (ArPlaymatLayout.PlayerHoloX + ArPlaymatLayout.OppHoloX) * 0.5f;
        }

        void SyncMoteVolume()
        {
            var st = Street();
            _motes?.SetVolume(st.Center, new Vector3(st.Half.x * 0.92f, st.Half.y, st.Half.z * 0.92f));
        }

        FieldStreet Street()
        {
            var halfH = StreetHeight * 0.5f * ArPlaymatLayout.LiveHoloScale;
            return new FieldStreet(new Vector3(_centerX, halfH, 0f), new Vector3(_halfW, halfH, _halfZ),
                ArPlaymatLayout.LiveHoloScale);
        }

        void DestroySignature()
        {
            if (_signature != null) ArObjectUtil.Destroy(_signature.gameObject);
            _signature = null;
        }

        float FarthestCorner(Vector3 o)
        {
            var best = 0f;
            for (var sx = -1; sx <= 1; sx += 2)
            for (var sz = -1; sz <= 1; sz += 2)
            {
                var dx = _centerX + sx * _halfW - o.x;
                var dz = sz * _halfZ - o.z;
                best = Mathf.Max(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return Mathf.Max(1f, best);
        }

        void SetWallsVisible(bool on)
        {
            if (_leftMr != null && _leftMr.gameObject.activeSelf != on) _leftMr.gameObject.SetActive(on);
            if (_rightMr != null && _rightMr.gameObject.activeSelf != on) _rightMr.gameObject.SetActive(on);
        }

        static void SetMatColor(Material mat, Color c)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        // ── Art bake (the card illustration is the asset) ───────────────────

        void ApplyWallTexture(Material mat)
        {
            if (mat == null) return;
            CardArtFocus.ApplyToMaterial(mat, _wallTex != null ? _wallTex : Texture2D.whiteTexture,
                cropToArtwork: false);
            SetMatColor(mat, new Color(1f, 1f, 1f, 0f));
        }

        void BakeTextures(int cardId)
        {
            ReleaseTextures();
            var art = _db?.GetArt(cardId);
            var src = art != null ? art.texture : null;
            if (src != null && !src.isReadable) src = null;

            var r = src != null && CardArtFocus.LooksLikeFullCardScan(src) &&
                    !CardArtFocus.IsPreCroppedIllustration(src)
                ? FieldSpellEnvironments.IllustrationInset
                : new Rect(0f, 0f, 1f, 1f);

            if (src != null)
                _wallTex = BakeWall(src, r, cardId);
            // Ground band = lower 45% of the illustration.
            _terrainTex = BakeTerrain(src, new Rect(r.x, r.y, r.width, r.height * 0.45f), _env.Ground, cardId);
        }

        void ReleaseTextures()
        {
            if (_wallTex != null) ArObjectUtil.Destroy(_wallTex);
            if (_terrainTex != null) ArObjectUtil.Destroy(_terrainTex);
            _wallTex = null;
            _terrainTex = null;
        }

        /// <summary>
        /// Illustration window only (no card frame), with alpha melting into
        /// the street at the bottom, the sky at the top and the wrap ends.
        /// </summary>
        static Texture2D BakeWall(Texture2D src, Rect r, int cardId)
        {
            var w = Mathf.Clamp(Mathf.RoundToInt(r.width * src.width), 16, WallTexMax);
            var h = Mathf.Clamp(Mathf.RoundToInt(r.height * src.height), 16, WallTexMax);
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var v = (y + 0.5f) / h;
                var fadeV = Mathf.SmoothStep(0f, 1f, v / 0.22f) * Mathf.SmoothStep(0f, 1f, (1f - v) / 0.18f);
                for (var x = 0; x < w; x++)
                {
                    var u = (x + 0.5f) / w;
                    var fadeU = Mathf.SmoothStep(0f, 1f, u / 0.08f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.08f);
                    var c = src.GetPixelBilinear(r.x + u * r.width, r.y + v * r.height);
                    c.a = fadeU * fadeV;
                    px[y * w + x] = c;
                }
            }

            return MakeTex(w, h, px, $"FieldWall_{cardId}");
        }

        /// <summary>Ground band on a soft disc, pulled toward the palette ground colour.</summary>
        static Texture2D BakeTerrain(Texture2D src, Rect band, Color ground, int cardId)
        {
            const int n = TerrainTexSize;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            {
                var v = (y + 0.5f) / n;
                for (var x = 0; x < n; x++)
                {
                    var u = (x + 0.5f) / n;
                    var dx = u * 2f - 1f;
                    var dy = v * 2f - 1f;
                    var rad = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = 1f - Mathf.SmoothStep(0.5f, 1f, rad);
                    var c = src != null
                        ? Color.Lerp(src.GetPixelBilinear(band.x + u * band.width, band.y + v * band.height),
                            ground, 0.3f)
                        : ground;
                    c.a = alpha;
                    px[y * n + x] = c;
                }
            }

            return MakeTex(n, n, px, $"FieldTerrain_{cardId}");
        }

        static Texture2D MakeTex(int w, int h, Color32[] px, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// Unit elliptical side wall. Player looks toward +Z; left is −X.
        /// Bows around the long edge so the picture wraps the street instead of
        /// sitting as a flat poster in the aisle.
        /// </summary>
        static Mesh SideWrap(bool playerLeft)
        {
            var sign = playerLeft ? -1f : 1f;
            // ±35° around the left/right axis — half the previous wrap so the
            // illustration is not stretched along the whole street.
            const float span = 35f * Mathf.Deg2Rad;
            var mid = playerLeft ? Mathf.PI : 0f;
            var a0 = mid - span;
            var a1 = mid + span;

            var nx = ArcSegs + 1;
            var ny = HeightSegs + 1;
            var v = new Vector3[nx * ny];
            var uv = new Vector2[nx * ny];
            var t = new int[ArcSegs * HeightSegs * 6];

            for (var iy = 0; iy < ny; iy++)
            {
                var vv = iy / (float)HeightSegs;
                for (var ix = 0; ix < nx; ix++)
                {
                    var u = ix / (float)ArcSegs;
                    var a = Mathf.Lerp(a0, a1, u);
                    var i = iy * nx + ix;
                    v[i] = new Vector3(Mathf.Cos(a), vv, Mathf.Sin(a));
                    // Keep the illustration upright and reading left→right on
                    // both walls when viewed from inside the street.
                    uv[i] = new Vector2(playerLeft ? 1f - u : u, vv);
                }
            }

            var ti = 0;
            for (var iy = 0; iy < HeightSegs; iy++)
            {
                for (var ix = 0; ix < ArcSegs; ix++)
                {
                    var i0 = iy * nx + ix;
                    var i1 = i0 + 1;
                    var i2 = i0 + nx;
                    var i3 = i2 + 1;
                    if (sign < 0f)
                    {
                        t[ti++] = i0;
                        t[ti++] = i2;
                        t[ti++] = i1;
                        t[ti++] = i1;
                        t[ti++] = i2;
                        t[ti++] = i3;
                    }
                    else
                    {
                        t[ti++] = i0;
                        t[ti++] = i1;
                        t[ti++] = i2;
                        t[ti++] = i1;
                        t[ti++] = i3;
                        t[ti++] = i2;
                    }
                }
            }

            var mesh = new Mesh { name = playerLeft ? "FieldWrapL" : "FieldWrapR" };
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
