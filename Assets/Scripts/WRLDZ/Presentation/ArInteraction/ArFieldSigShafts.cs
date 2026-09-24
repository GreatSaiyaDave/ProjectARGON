using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Luminous Spark's radiance. Four to six thin shafts of white-gold light
    /// pour in above the street and slant down across it, as if from a blinding
    /// source beyond its far left corner. Each shaft is near-white where it
    /// enters, turns to the palette's accent (gold) as it falls and fades to
    /// nothing at its own low line, which sits above head-clear height, so the
    /// aisle floor stays street. Shafts further right lean further, so they fan
    /// out like the rays in the illustration. Each shaft's top wanders slowly
    /// across and along the street, its slant sways a few degrees, it breathes in
    /// brightness and width on its own phase, and thin speed-line glints race
    /// down it. The shafts pour down from above as the sweep arrives and draw
    /// back up on dissolve; every alpha is × level.
    /// <para>One look (variant 0). Any other variant draws the same shafts.
    /// SigScale sets the shaft count (4 at 0.5, 5 at 1, 6 at 1.5) and their
    /// width.</para>
    /// Every shaft is a ribbon turned toward the stage camera
    /// (<see cref="FieldStreet.Camera"/>) around its own axis. It reads as a beam
    /// from any side and never as a curtain. The bright band sits low enough to
    /// stay in a portrait frame aimed at the field. Any part of a shaft or glint
    /// that would draw over a face-up monster's art from the camera fades out
    /// (<see cref="ArFieldSignature.ArtClear"/>: each card in its camera-facing
    /// plane and its actual pose, upright or sideways in Defense), as does the
    /// part right at the phone, and the centre of the view dims a little. With
    /// no stage camera the ribbons face a stand-in eye behind the player's end and
    /// nothing fades or dims. Every drawn point stays at or above head-clear
    /// height and at z ≥ the street's NearZ. One dynamic mesh (one draw call,
    /// at most 168 vertices) with a kit-owned beam texture: ribbons sample its
    /// solid-cored centre row, glints the whole texture.
    /// Scope: street.
    /// </summary>
    public sealed class ArFieldSigShafts : ArFieldSignature
    {
        const int MinShafts = 4;
        const int MaxShafts = 6;
        /// <summary>Rows along a shaft, two vertices each (left and right edge).</summary>
        const int Rows = 10;
        const int ShaftVerts = Rows * 2;
        const int GlintsPerShaft = 2;
        const int GlintVerts = 4;
        const int BeamTexSize = 32;

        const float TwoPi = Mathf.PI * 2f;
        /// <summary>Streets at or below this HoloScale draw nothing.</summary>
        const float MinScale = 1e-4f;

        /// <summary>Per-vertex alpha cap before level.</summary>
        const float MaxAlpha = 0.6f;
        /// <summary>
        /// Shaft and glint alpha at full breath. A glint over its shaft composites to
        /// at most 0.42 + 0.3 × (1 − 0.42) ≈ 0.594 ≤ <see cref="MaxAlpha"/>
        /// (about 0.595 after 8-bit vertex colour rounding).
        /// </summary>
        const float ShaftAlpha = 0.42f;
        const float GlintAlpha = 0.3f;

        // Heights (× HoloScale). Every vertex is held at or above ClearMargin × StreetClearHeight.
        const float ClearMargin = 1.02f;
        /// <summary>
        /// Shaft tops start this far above the street box. With the long soft entry and hold
        /// below, the bright band sits about 2.1–2.5 m × HoloScale up, inside a portrait frame
        /// aimed at the field.
        /// </summary>
        const float TopOver = 0.7f;
        const float TopJitter = 0.15f;
        /// <summary>Some shafts end up to this much higher than the lowest line.</summary>
        const float LiftJitter = 0.25f;
        /// <summary>Shortest drop a shaft keeps, however low the street box is.</summary>
        const float MinDrop = 0.5f;

        // Half widths (× HoloScale): full width about 0.15 m at the top, 0.24 m at the bottom,
        // at most 0.30 m (SigScale 1.5, widest jitter, top of a breath).
        const float TopHalfWidth = 0.075f;
        const float BottomHalfWidth = 0.12f;
        const float WidthJitter = 0.1f;
        /// <summary>Width × (1 + this × (SigScale − 1)).</summary>
        const float WidthPerSigScale = 0.15f;
        const float WidthBreath = 0.06f;

        // Slant: run per metre of drop. Across the street (+X, so the light comes from the
        // player's left) and a little toward the player (−Z).
        const float SlantX = 0.65f;
        /// <summary>Extra lean from the left-most shaft (−) to the right-most (+): the fan.</summary>
        const float FanX = 0.2f;
        const float SlantZ = -0.15f;
        /// <summary>Slow sway of the slant, about ±3°.</summary>
        const float Wobble = 0.06f;

        // Layout: shaft tops as a share of the street half extents. Tops sit left of centre
        // because the shafts run right as they fall. Shafts alternate between a far band and
        // a band around mid, never over the player's end of the street. Where NearZ reaches
        // past the mid band's near edge, both bands squeeze toward the far end.
        const float TopXMin = -0.85f;
        const float TopXMax = 0.3f;
        const float CellJitter = 0.3f;
        const float FarZMin = 0.2f;
        const float FarZMax = 0.8f;
        const float MidZMin = -0.3f;
        const float MidZMax = 0.2f;
        /// <summary>Shaft axes stay inside this share of the street half extents.</summary>
        const float BoxInset = 0.96f;

        // Motion (rates in radians per second; amplitudes × HoloScale).
        const float SwayAmp = 0.22f;
        const float SwayRateMin = 0.22f;
        const float SwayRateMax = 0.4f;
        const float DriftAmp = 0.1f;
        const float DriftRateMin = 0.15f;
        const float DriftRateMax = 0.3f;
        const float WobbleRateMin = 0.3f;
        const float WobbleRateMax = 0.5f;
        /// <summary>Brightness dips to 1 − this at the bottom of each breath.</summary>
        const float BreathDepth = 0.4f;
        const float BreathRateMin = 0.7f;
        const float BreathRateMax = 1.3f;
        /// <summary>Share of the full drop drawn as the sweep first arrives.</summary>
        const float GrowFloor = 0.2f;

        // Alpha along a shaft (0 = top, 1 = bottom): a long soft entry (the shaft emerges from the
        // air), hold, then a smooth fall to 0.
        const float TopSoft = 0.3f;
        const float HoldTo = 0.6f;

        // Glints: thin speed lines racing down a shaft.
        /// <summary>Glint length as a share of its shaft.</summary>
        const float GlintLength = 0.34f;
        const float GlintHalfWidth = 0.035f;
        /// <summary>Share of each glint cycle spent travelling; the rest it rests hidden.</summary>
        const float GlintActive = 0.35f;
        const float GlintPeriodMin = 1.6f;
        const float GlintPeriodMax = 2.6f;
        /// <summary>Glints shorter than this share of the shaft (entering or leaving) collapse.</summary>
        const float MinGlint = 0.02f;

        // Stage camera: the centre of the view dims a little and the space right at the phone stays clear.
        const float ConeInnerDeg = 6f;
        const float ConeOuterDeg = 20f;
        /// <summary>Alpha share left at the very centre of the view.</summary>
        const float ConeFloor = 0.85f;
        /// <summary>Floor-local metres from the camera: clear inside NearIn, full from NearOut.</summary>
        const float NearIn = 0.4f;
        const float NearOut = 1.2f;
        /// <summary>Stand-in eye (× HoloScale up, metres behind the player's end) with no camera.</summary>
        const float FallbackEyeHeight = 1.5f;
        const float FallbackEyeBack = 0.5f;

        // Colours: near-white where the light enters, the accent's gold lower down.
        const float TopWhite = 0.8f;
        const float GlintWhite = 0.9f;

        /// <summary>One shaft of the fan. Positions and sizes are shares or × HoloScale; phases in radians.</summary>
        struct Shaft
        {
            public float X;       // top x, share of the street half width
            public float Z;       // top z, share of the street half length (before a short street squeezes it)
            public float Top;     // top height above the street box (× HoloScale)
            public float Lift;    // bottom height above the clear line (× HoloScale); covers the half width
            public float Width;   // × the half widths
            public float Slant;   // run across the street per metre of drop
            public float SwayRate;
            public float SwayPhase;
            public float DriftRate;
            public float DriftPhase;
            public float WobbleRate;
            public float WobblePhase;
            public float BreathRate;
            public float BreathPhase;
            public float GlintRate;  // glint cycles per second
            public float GlintPhase; // 0…1
        }

        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        Shaft[] _shafts;
        int _count;
        /// <summary>First glint vertex: all shaft ribbons are drawn first, glints over them.</summary>
        int _glintBase;

        // Per-row tables (index = row). Colours are vertex colours (converted for the colour space).
        float[] _rowT;
        float[] _rowProfile;
        /// <summary>This shaft's view factor per row (scratch, rewritten per shaft).</summary>
        float[] _rowView;
        Color[] _rowCol;
        Color _glintCol;

        float _cosInner;
        float _cosOuter;

        // This frame: view, clear line, near share of the layout and the extent of every written vertex.
        Vector3 _eye;
        Vector3 _fwd;
        bool _hasCam;
        float _clearY;
        float _zMin;
        Vector3 _lo;
        Vector3 _hi;

        protected override void Build()
        {
            var sig = SigScale;
            _count = Mathf.Clamp(Mathf.RoundToInt(3f + 2f * sig), MinShafts, MaxShafts);
            _cosInner = Mathf.Cos(ConeInnerDeg * Mathf.Deg2Rad);
            _cosOuter = Mathf.Cos(ConeOuterDeg * Mathf.Deg2Rad);

            BuildShafts(1f + WidthPerSigScale * (sig - 1f));
            BuildTables();

            _glintBase = _count * ShaftVerts;
            var total = _glintBase + _count * GlintsPerShaft * GlintVerts;
            _verts = new Vector3[total];
            _cols = new Color32[total];
            var uvs = new Vector2[total];
            for (var i = 0; i < _count; i++)
            for (var j = 0; j < Rows; j++)
            {
                var v = i * ShaftVerts + j * 2;
                uvs[v] = new Vector2(0f, 0.5f);
                uvs[v + 1] = new Vector2(1f, 0.5f);
            }

            for (var g = _glintBase; g < total; g += GlintVerts)
            {
                uvs[g] = new Vector2(0f, 0f);
                uvs[g + 1] = new Vector2(1f, 0f);
                uvs[g + 2] = new Vector2(1f, 1f);
                uvs[g + 3] = new Vector2(0f, 1f);
            }

            _mesh = NewDynamicMesh("FieldShafts");
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles(total);
            _mr = AddMeshChild("Shafts", _mesh,
                VertexColorMaterial("FieldShaftsMat", StreetQueue, OwnTexture(BuildBeamTexture())));
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
            if (_mr == null) return;
            var h = street.HoloScale;
            if (level <= 0.001f || h <= MinScale)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            level = Mathf.Min(level, 1f);
            Advance(Mathf.Clamp(dt, 0f, 0.1f));
            UpdateView(street, h);
            CollectArtCards(anchors, street);

            _clearY = StreetClearHeight * ClearMargin * h;
            _zMin = Mathf.Min(FarZMax,
                Mathf.Max(MidZMin, (street.NearZ - street.Center.z) / Mathf.Max(street.Half.z, 1e-3f)));
            _lo = street.Center - street.Half;
            _hi = street.Center + street.Half;
            var grow = Mathf.Lerp(GrowFloor, 1f, Mathf.SmoothStep(0f, 1f, level));
            for (var i = 0; i < _count; i++)
                WriteShaft(i, street, h, level, grow);

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box, grown to the shaft tops, keeps the renderer from being culled.
            var bounds = new Bounds();
            bounds.SetMinMax(_lo, _hi);
            _mesh.bounds = bounds;
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Advance(float dt)
        {
            for (var i = 0; i < _count; i++)
            {
                ref var s = ref _shafts[i];
                s.SwayPhase = Mathf.Repeat(s.SwayPhase + s.SwayRate * dt, TwoPi);
                s.DriftPhase = Mathf.Repeat(s.DriftPhase + s.DriftRate * dt, TwoPi);
                s.WobblePhase = Mathf.Repeat(s.WobblePhase + s.WobbleRate * dt, TwoPi);
                s.BreathPhase = Mathf.Repeat(s.BreathPhase + s.BreathRate * dt, TwoPi);
                s.GlintPhase = Mathf.Repeat(s.GlintPhase + s.GlintRate * dt, 1f);
            }
        }

        /// <summary>
        /// The street's stage camera (floor-local), the same eye <see cref="ArFieldSignature.ArtClear"/>
        /// tests from; its facing comes from the camera itself. Without one, an eye behind the
        /// player's end that only turns the ribbons (no fading or dimming).
        /// </summary>
        void UpdateView(in FieldStreet street, float h)
        {
            if (street.HasCamera)
            {
                _eye = street.Camera;
                var cam = StageCamera;
                var fwd = cam != null
                    ? transform.InverseTransformDirection(cam.transform.forward)
                    : street.Center - _eye;
                _fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
                _hasCam = true;
                return;
            }

            _eye = new Vector3(street.Center.x, FallbackEyeHeight * h,
                street.Center.z - street.Half.z - FallbackEyeBack);
            _fwd = Vector3.forward;
            _hasCam = false;
        }

        /// <summary>
        /// One shaft: a ribbon from its top down to its own low line, turned to the
        /// eye around its axis, then its glints on top.
        /// </summary>
        void WriteShaft(int i, in FieldStreet street, float h, float level, float grow)
        {
            ref var s = ref _shafts[i];
            var pulse = 0.5f + 0.5f * Mathf.Sin(s.BreathPhase);
            var bright = 1f - BreathDepth * (1f - pulse);
            var wide = s.Width * (1f + WidthBreath * (2f * pulse - 1f));

            // The axis ends at the shaft's low line, which sits its widest half width above the
            // clear line, so no edge vertex dips below it whichever way the ribbon turns.
            var low = _clearY + s.Lift * h;
            var topY = Mathf.Max(street.Center.y + street.Half.y + s.Top * h, low + MinDrop * h);
            var zShare = Mathf.Lerp(_zMin, FarZMax, (s.Z - MidZMin) / (FarZMax - MidZMin));
            var top = new Vector3(
                street.Center.x + s.X * street.Half.x + SwayAmp * h * Mathf.Sin(s.SwayPhase),
                topY,
                street.Center.z + zShare * street.Half.z + DriftAmp * h * Mathf.Sin(s.DriftPhase));
            var axis = new Vector3(s.Slant + Wobble * Mathf.Sin(s.WobblePhase), -1f, SlantZ) * ((topY - low) * grow);
            var bottom = top + axis;
            // Clear of NearZ by the widest half width: the turned ribbon's edge can reach that far in z.
            Fit(ref top, ref bottom, street, street.NearZ + BottomHalfWidth * s.Width * (1f + WidthBreath) * h);

            var side = Vector3.Cross(axis, _eye - (top + bottom) * 0.5f);
            if (side.sqrMagnitude < 1e-10f)
                side = new Vector3(-axis.y, axis.x, 0f);
            side.Normalize();

            var v = i * ShaftVerts;
            var hwTop = TopHalfWidth * wide * h;
            var hwLow = BottomHalfWidth * wide * h;
            // Alpha is interpolated between rows, so each row's art test reaches one row further.
            var step = axis.magnitude / (Rows - 1);
            for (var j = 0; j < Rows; j++)
            {
                var t = _rowT[j];
                var p = top + axis * t;
                var hw = Mathf.Lerp(hwTop, hwLow, t);
                var off = side * hw;
                Put(v, p - off);
                Put(v + 1, p + off);
                _rowView[j] = View(p, hw + step);
                Color32 c = WithAlpha(_rowCol[j],
                    Mathf.Min(MaxAlpha, ShaftAlpha * bright * _rowProfile[j] * _rowView[j]) * level);
                _cols[v] = c;
                _cols[v + 1] = c;
                v += 2;
            }

            var gOff = side * (GlintHalfWidth * h);
            for (var g = 0; g < GlintsPerShaft; g++)
            {
                var gv = _glintBase + (i * GlintsPerShaft + g) * GlintVerts;
                // Glints of one shaft share a period half a cycle apart, so they never overlap.
                var travel = Mathf.Repeat(s.GlintPhase + g / (float)GlintsPerShaft, 1f) / GlintActive;
                var run = travel * (1f + GlintLength);
                var head = Mathf.Min(1f, run);
                var tail = Mathf.Max(0f, run - GlintLength);
                if (travel >= 1f || head - tail < MinGlint)
                {
                    Collapse(gv, GlintVerts, top);
                    continue;
                }

                var pt = top + axis * tail;
                var ph = top + axis * head;
                Put(gv, pt - gOff);
                Put(gv + 1, pt + gOff);
                Put(gv + 2, ph + gOff);
                Put(gv + 3, ph - gOff);
                // A glint spans several rows: it takes the dimmest view of the rows around it,
                // so it never streaks across a card between its two ends.
                var view = 1f;
                var jHi = Mathf.Min(Rows - 1, Mathf.CeilToInt(head * (Rows - 1)));
                for (var j = Mathf.Max(0, Mathf.FloorToInt(tail * (Rows - 1))); j <= jHi; j++)
                    view = Mathf.Min(view, _rowView[j]);
                Color32 ct = WithAlpha(_glintCol,
                    Mathf.Min(MaxAlpha, GlintAlpha * bright * Profile(tail) * view) * level);
                Color32 ch = WithAlpha(_glintCol,
                    Mathf.Min(MaxAlpha, GlintAlpha * bright * Profile(head) * view) * level);
                _cols[gv] = ct;
                _cols[gv + 1] = ct;
                _cols[gv + 2] = ch;
                _cols[gv + 3] = ch;
            }
        }

        /// <summary>Writes a vertex, holding it above the clear line, and grows this frame's bounds.</summary>
        void Put(int v, Vector3 p)
        {
            if (p.y < _clearY) p.y = _clearY;
            _verts[v] = p;
            _lo = Vector3.Min(_lo, p);
            _hi = Vector3.Max(_hi, p);
        }

        void Collapse(int v, int n, Vector3 at)
        {
            for (var i = v; i < v + n; i++)
            {
                Put(i, at);
                _cols[i] = default;
            }
        }

        /// <summary>
        /// 0…1 view factor at <paramref name="p"/>, for a piece drawn up to <paramref name="reach"/>
        /// around it: fades out over face-up monster art (<see cref="ArFieldSignature.ArtClear"/>,
        /// cards from this Tick's <see cref="ArFieldSignature.CollectArtCards"/>) and right at the
        /// phone, and dims a little toward the centre of the stage camera's view.
        /// </summary>
        float View(Vector3 p, float reach)
        {
            if (!_hasCam) return 1f;
            var d = p - _eye;
            var dist = d.magnitude;
            if (dist <= 1e-4f) return 0f;
            var near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearIn, NearOut, dist));
            var off = Mathf.InverseLerp(_cosInner, _cosOuter, Vector3.Dot(d, _fwd) / dist);
            return near * Mathf.Lerp(ConeFloor, 1f, Mathf.SmoothStep(0f, 1f, off)) * ArtClear(p, reach);
        }

        /// <summary>Alpha share along a shaft (0 = top, 1 = bottom): soft entry, hold, smooth fall to 0.</summary>
        static float Profile(float t)
        {
            var entry = Mathf.SmoothStep(0f, 1f, t / TopSoft);
            return entry * (1f - Mathf.SmoothStep(0f, 1f, (t - HoldTo) / (1f - HoldTo)));
        }

        /// <summary>
        /// Slides a shaft (both ends together) inside the street's x extent and between
        /// <paramref name="zNear"/> and the far end. The near limit wins if it cannot fit.
        /// </summary>
        static void Fit(ref Vector3 a, ref Vector3 b, in FieldStreet street, float zNear)
        {
            var hx = street.Half.x * BoxInset;
            var hz = street.Half.z * BoxInset;
            var dx = Shift(Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x), street.Center.x - hx, street.Center.x + hx);
            var dz = Shift(Mathf.Min(a.z, b.z), Mathf.Max(a.z, b.z),
                Mathf.Max(street.Center.z - hz, zNear), street.Center.z + hz);
            a.x += dx;
            b.x += dx;
            a.z += dz;
            b.z += dz;
        }

        /// <summary>Offset that brings [lo, hi] inside [min, max]; min wins when it cannot fit.</summary>
        static float Shift(float lo, float hi, float min, float max)
        {
            if (hi > max) return Mathf.Max(max - hi, min - lo);
            return lo < min ? min - lo : 0f;
        }

        /// <summary>
        /// Stratified fan: one shaft per slice of the street's width, alternating
        /// far / mid bands so neighbours never line up into a wall.
        /// </summary>
        void BuildShafts(float sigWidth)
        {
            _shafts = new Shaft[_count];
            var farFirst = R(0f, 1f) < 0.5f;
            for (var i = 0; i < _count; i++)
            {
                ref var s = ref _shafts[i];
                var cell = Mathf.Clamp01((i + 0.5f + R(-CellJitter, CellJitter)) / _count);
                s.X = Mathf.Lerp(TopXMin, TopXMax, cell);
                s.Z = (i % 2 == 0) == farFirst ? R(FarZMin, FarZMax) : R(MidZMin, MidZMax);
                s.Top = TopOver + R(-TopJitter, TopJitter);
                s.Width = sigWidth * R(1f - WidthJitter, 1f + WidthJitter);
                s.Lift = BottomHalfWidth * s.Width * (1f + WidthBreath) + R(0f, LiftJitter);
                s.Slant = SlantX + FanX * (cell * 2f - 1f);
                s.SwayRate = R(SwayRateMin, SwayRateMax);
                s.SwayPhase = R(0f, TwoPi);
                s.DriftRate = R(DriftRateMin, DriftRateMax);
                s.DriftPhase = R(0f, TwoPi);
                s.WobbleRate = R(WobbleRateMin, WobbleRateMax);
                s.WobblePhase = R(0f, TwoPi);
                s.BreathRate = R(BreathRateMin, BreathRateMax);
                s.BreathPhase = R(0f, TwoPi);
                s.GlintRate = 1f / R(GlintPeriodMin, GlintPeriodMax);
                s.GlintPhase = R(0f, 1f);
            }
        }

        /// <summary>Mixes the palette in sRGB, then converts once to vertex colours.</summary>
        void BuildTables()
        {
            var top = VertexColor(Color.Lerp(Env.Accent, Color.white, TopWhite));
            var low = VertexColor(Env.Accent);
            _glintCol = VertexColor(Color.Lerp(Env.Accent, Color.white, GlintWhite));
            _rowT = new float[Rows];
            _rowProfile = new float[Rows];
            _rowView = new float[Rows];
            _rowCol = new Color[Rows];
            for (var j = 0; j < Rows; j++)
            {
                var t = j / (float)(Rows - 1);
                _rowT[j] = t;
                _rowProfile[j] = Profile(t);
                _rowCol[j] = Color.Lerp(top, low, t);
            }
        }

        /// <summary>
        /// Beam texture: alpha (1 − x²)² across u and v (x = 0 at the centre, 1 at the edge).
        /// Its centre row gives the ribbons a solid core with soft edges; glints use it whole.
        /// </summary>
        static Texture2D BuildBeamTexture()
        {
            const int n = BeamTexSize;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                px[y * n + x] = new Color(1f, 1f, 1f, Beam((x + 0.5f) / n) * Beam((y + 0.5f) / n));

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "FieldShaftBeam",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Beam(float u)
        {
            var x = u * 2f - 1f;
            var a = 1f - x * x;
            return a * a;
        }

        int[] BuildTriangles(int total)
        {
            var tris = new int[_count * (Rows - 1) * 6 + (total - _glintBase) / GlintVerts * 6];
            var k = 0;
            for (var i = 0; i < _count; i++)
            for (var j = 0; j < Rows - 1; j++)
            {
                var v = i * ShaftVerts + j * 2;
                tris[k++] = v;
                tris[k++] = v + 2;
                tris[k++] = v + 1;
                tris[k++] = v + 1;
                tris[k++] = v + 2;
                tris[k++] = v + 3;
            }

            for (var g = _glintBase; g < total; g += GlintVerts)
            {
                tris[k++] = g;
                tris[k++] = g + 2;
                tris[k++] = g + 1;
                tris[k++] = g;
                tris[k++] = g + 3;
                tris[k++] = g + 2;
            }

            return tris;
        }
    }
}
