using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Yami's dark. A pool of void seeps out of the ground in a ring around
    /// each monster, ringed at its outer edge by a flickering crimson line,
    /// and four to six shadow tendrils rise out of it. Each tendril is a dark
    /// core that dims the passthrough, rim-lit in the accent along both edges
    /// like the crimson-edged void in the illustration. Tendrils flare out,
    /// then hook back toward the monster, writhe slowly as waves climb them, and
    /// fade out toward the tip, where the rim light lasts longer than the core.
    /// They rise out of the ground with the sweep and sink back on dissolve.
    /// <para>Boon (the dark empowers): tendrils coil in close around the monster,
    /// stand taller and calmer, and bands of brighter rim light run up them.
    /// Bane (the dark weakens): tendrils stay low, claw inward with hooked tips
    /// and twitch restlessly over a deeper pool with a drained rim.</para>
    /// <para>One look (variant 0). Any other variant draws the same shroud.
    /// SigScale sets the tendril count (4 at 0.5, 5 at 1, 6 at 1.5) and their
    /// length.</para>
    /// <para>The opaque monster art stands in the ring facing the stage camera
    /// and hides its far half, so tendrils are laid out from the camera's side:
    /// tall on the flanks, framing the art the way claws frame the void, and
    /// shorter and fainter on the front flanks, so the monster reads through
    /// them. Each monster's spacing, sway and rhythm come from its Key, so it
    /// keeps its look while it lunges or others come and go.</para>
    /// <para>Nothing reaches under the card: the pool starts outside the
    /// ownership ring, and the tendril tips stop in front of the art. A Set
    /// card lies flat over the ring, so its tendrils sink at once, leaving
    /// only the pool (below the card), and rise again after a flip.</para>
    /// One dynamic mesh (one draw call) with flat colour: soft edges come from
    /// vertex alpha. Scope: per-monster.
    /// </summary>
    public sealed class ArFieldSigShroud : ArFieldSignature
    {
        const int MaxAnchors = 10;
        const int MinTendrils = 4;
        const int MaxTendrils = 6;
        const int Rows = 10;
        /// <summary>Across a tendril row: edge, rim, core, rim, edge.</summary>
        const int RowVerts = 5;
        const int TendrilVerts = Rows * RowVerts;
        const int PoolSegs = 24;
        /// <summary>Pool rings, inside out: clear inner edge, dark band, crimson rim, clear outer edge.</summary>
        const int PoolRings = 4;
        const int PoolVerts = PoolSegs * PoolRings;

        const float TwoPi = Mathf.PI * 2f;

        /// <summary>Hard per-vertex guards (× Scale), just inside the contract.</summary>
        const float GuardRadius = MaxAnchorRadius * 0.97f;
        const float GuardHeight = MaxAnchorHeight * 0.97f;
        /// <summary>Per-vertex alpha cap before presence × level.</summary>
        const float MaxAlpha = 0.55f;
        /// <summary>Anchors at or below this Scale are skipped.</summary>
        const float MinScale = 1e-4f;

        // Tendril size in host-local metres (× anchor.Scale).
        const float TendrilLength = 0.46f;
        const float LengthPerSigScale = 0.08f;
        const float HalfWidth = 0.05f;
        /// <summary>Tip width as a share of the root width.</summary>
        const float TipShare = 0.12f;
        /// <summary>Extra width where a tendril leaves the pool.</summary>
        const float RootFlare = 0.7f;
        /// <summary>Rim light sits this far out from the core (share of the half width).</summary>
        const float RimAt = 0.6f;
        /// <summary>Share of full length (and alpha) as the sweep first reaches a monster.</summary>
        const float GrowFloor = 0.12f;

        // Writhing: waves climbing each tendril (cycles along its length, radians per second).
        const float WaveCyclesLean = 0.8f;
        const float WaveCyclesSide = 0.6f;
        const float WritheSpeedLean = 1.1f;
        const float WritheSpeedSide = 0.75f;
        const float TwitchSpeed = 7f;

        // Layout, in radians either side of the camera direction: the far half of
        // the ring is behind the opaque art, so tendrils stand on the flanks and front flanks.
        const float ArcFrom = 0.7f;
        const float ArcTo = 1.36f;
        /// <summary>Per-monster shift of each tendril inside its share of the arc (share of the spacing).</summary>
        const float ArcJitter = 0.25f;
        /// <summary>Slow sway of each side of the layout round the ring (radians, radians per second).</summary>
        const float SwayAngle = 0.1f;
        const float SwaySpeed = 0.35f;

        // Tendrils over the art's centre (lateral offset × Scale) stay shorter and fainter; flanks keep full length.
        const float CentreFrom = 0.25f;
        const float CentreTo = 0.5f;
        const float CentreLength = 0.6f;
        const float CentreAlpha = 0.7f;

        /// <summary>Seconds for a flipped monster's tendrils to rise back out of the pool.</summary>
        const float FlipRiseSeconds = 0.6f;

        // Per-monster hashes: tendril i uses salts TendrilSalt + i × TendrilSaltStride + 0…7.
        const int TendrilSalt = 100;
        const int TendrilSaltStride = 16;

        const float CoreAlpha = 0.34f;
        const float RimAlpha = 0.45f;
        const float CoreGroundShare = 0.4f;
        const float CoreDarken = 0.3f;

        // Pool radii (× Scale). The ownership ring sits at 0.45.
        const float PoolInner = 0.46f;
        const float PoolDark = 0.55f;
        const float PoolRim = 0.645f;
        const float PoolOuter = 0.71f;
        const float PoolLift = 0.005f;
        const float PoolAlpha = 0.28f;
        const float PoolRimAlpha = 0.28f;
        const float PoolBreath = 0.25f;
        const float BreathSpeed = 0.9f;
        /// <summary>How patchy the crimson rim is, and how fast the patches drift round (segments per second).</summary>
        const float PoolFlicker = 0.6f;
        const float FlickerDrift = 1.4f;

        // Boon: the dark feeds the monster.
        const float BoonRimWhite = 0.25f;
        /// <summary>Pool rim only; tendril rims brighten through the pulses.</summary>
        const float BoonRimGain = 1.15f;
        /// <summary>Rim alpha gain at a pulse peak: 0.45 × 1.22 stays under <see cref="MaxAlpha"/>.</summary>
        const float BoonPulse = 0.22f;
        const float BoonPulseWhite = 0.35f;
        const float PulseCycles = 1.5f;
        const float PulseSpeed = 2.4f;

        // Bane: the dark drains it.
        const float BaneDarken = 0.35f;
        const float BaneRimDrain = 0.5f;
        const float BaneRimGain = 0.7f;
        const float BaneCoreGain = 1.1f;
        const float BanePoolGain = 1.25f;

        /// <summary>How one monster's tendrils stand. Angles are radians.</summary>
        struct Shape
        {
            public float Root;        // root ring radius (host-local m)
            public float Length;      // × the kit's tendril length
            public float Lean;        // tilt from vertical at the root; negative flares outward
            public float Curl;        // extra inward tilt reached at the tip (× s²)
            public float WritheLean;  // climbing-wave amplitude toward / away from the monster
            public float WritheSide;  // climbing-wave amplitude round the ring
            public float Swirl;       // steady sideways tilt reached at the tip: coils round the monster
            public float Twitch;      // fast flick of the upper half
            public float Speed;       // × writhe speed
        }

        static readonly Shape Calm = new Shape
        {
            Root = 0.56f, Length = 1f, Lean = -0.3f, Curl = 1.55f,
            WritheLean = 0.3f, WritheSide = 0.4f, Swirl = 0.35f, Twitch = 0f, Speed = 1f
        };

        static readonly Shape Boon = new Shape
        {
            Root = 0.5f, Length = 1.15f, Lean = -0.1f, Curl = 1.15f,
            WritheLean = 0.22f, WritheSide = 0.3f, Swirl = 0.85f, Twitch = 0f, Speed = 0.8f
        };

        static readonly Shape Bane = new Shape
        {
            Root = 0.56f, Length = 0.7f, Lean = -0.45f, Curl = 2.4f,
            WritheLean = 0.5f, WritheSide = 0.6f, Swirl = 0.3f, Twitch = 0.35f, Speed = 1.8f
        };

        /// <summary>Indexed by Aura + 1.</summary>
        static readonly Shape[] Shapes = { Bane, Calm, Boon };

        /// <summary>One tendril of the monster being written, hashed from its Key.</summary>
        struct Tendril
        {
            public float Root;
            public float Length;
            public float Width;
            public float SwirlDir;
            public float PhaseLean;
            public float PhaseSide;
            public float PhaseTwitch;
            public float PhasePulse;
        }

        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        Tendril[] _tendrils;
        int _count;
        /// <summary>
        /// Pool + tendrils: at most 96 + 6 × 50 = 396 per monster, × <see cref="MaxAnchors"/> = 3960 ≤
        /// <see cref="ArFieldSignature.MaxVertices"/>.
        /// </summary>
        int _slotVerts;
        /// <summary>Slots written last frame; slots past the drawn count are collapsed once.</summary>
        int _slotsLive;
        /// <summary>Tendril length at this SigScale (host-local m).</summary>
        float _length;

        // Per-row tables (index = row).
        float[] _halfWidth;
        float[] _coreAlong;
        float[] _rimAlong;
        Vector3[] _rowP;

        float[] _poolCos;
        float[] _poolSin;
        /// <summary>0…1 per pool segment: breaks the crimson rim into patches.</summary>
        float[] _noise;

        // Draw order: far monsters first, and within a monster the flanks before the front.
        int[] _order;
        float[] _dist;
        int[] _tOrder;
        float[] _tFront;
        float[] _tRx;
        float[] _tRz;

        // Per anchor index, re-keyed whenever a different monster lands there.
        int[] _anchorKey;
        /// <summary>0…1 tendril height: drops to 0 at once when a card is Set, rises again after a flip.</summary>
        float[] _anchorRise;

        /// <summary>Writhe phases per shape (index = Aura + 1), so each shape keeps its own speed.</summary>
        readonly float[] _phLean = new float[3];
        readonly float[] _phSide = new float[3];
        float _phTwitch;
        float _phPulse;
        float _phBreath;
        float _phSway;
        float _flicker;

        Color _core;
        Color _coreBane;
        Color _rim;
        Color _rimBoon;
        Color _rimBane;

        // Anchor being written.
        Vector3 _o;
        float _s;
        Vector3 _view;
        float _camX;
        float _camZ;

        protected override void Build()
        {
            var sig = SigScale;
            _count = Mathf.Clamp(Mathf.RoundToInt(3f + 2f * sig), MinTendrils, MaxTendrils);
            _length = TendrilLength + LengthPerSigScale * (sig - 1f);
            _slotVerts = PoolVerts + _count * TendrilVerts;

            BuildColours();
            BuildTables();

            _verts = new Vector3[MaxAnchors * _slotVerts];
            _cols = new Color32[_verts.Length];
            var uvs = new Vector2[_verts.Length];
            for (var i = 0; i < uvs.Length; i++)
                uvs[i] = new Vector2(0.5f, 0.5f);

            _mesh = NewDynamicMesh("FieldShroud");
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles();
            _mr = AddMeshChild("Shroud", _mesh,
                VertexColorMaterial("FieldShroudMat", GroundQueue, Texture2D.whiteTexture));
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
            if (_mr == null) return;
            var count = anchors == null ? 0 : Mathf.Min(anchors.Count, MaxAnchors);
            if (level <= 0.001f || count == 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            var step = dt > 0f ? dt : 0f;
            Advance(step);
            level = Mathf.Min(level, 1f);
            var eye = EyeLocal(street);

            // Far monsters first so nearer shrouds blend over them.
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                Rise(i, a, step);
                var d = a.Position - eye;
                _dist[i] = -(d.x * d.x + d.z * d.z);
                _order[i] = i;
            }

            SortByKey(_order, _dist, count);

            var bounds = new Bounds(street.Center, street.Half * 2f);
            var drawn = 0;
            for (var k = 0; k < count; k++)
            {
                var i = _order[k];
                var a = anchors[i];
                var fade = Mathf.Clamp01(a.Presence) * level;
                if (fade <= 0.001f || a.Scale <= MinScale) continue;
                WriteAnchor(drawn * _slotVerts, a, fade, _anchorRise[i], eye);
                drawn++;
                var s = a.Scale;
                bounds.Encapsulate(new Bounds(
                    a.Position + new Vector3(0f, MaxAnchorHeight * 0.5f * s, 0f),
                    new Vector3(MaxAnchorRadius * 2f * s, MaxAnchorHeight * s, MaxAnchorRadius * 2f * s)));
            }

            if (drawn == 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            for (var slot = drawn; slot < _slotsLive; slot++)
                Collapse(slot * _slotVerts, _slotVerts, street.Center);
            _slotsLive = drawn;

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box (plus each ring) keeps the renderer from being culled.
            _mesh.bounds = bounds;
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Advance(float dt)
        {
            for (var i = 0; i < Shapes.Length; i++)
            {
                _phLean[i] = Mathf.Repeat(_phLean[i] + WritheSpeedLean * Shapes[i].Speed * dt, TwoPi);
                _phSide[i] = Mathf.Repeat(_phSide[i] + WritheSpeedSide * Shapes[i].Speed * dt, TwoPi);
            }

            _phTwitch = Mathf.Repeat(_phTwitch + TwitchSpeed * dt, TwoPi);
            _phPulse = Mathf.Repeat(_phPulse + PulseSpeed * dt, TwoPi);
            _phBreath = Mathf.Repeat(_phBreath + BreathSpeed * dt, TwoPi);
            _phSway = Mathf.Repeat(_phSway + SwaySpeed * dt, TwoPi);
            _flicker = Mathf.Repeat(_flicker + FlickerDrift * dt, PoolSegs);
        }

        /// <summary>
        /// Tendril height for the monster at anchor index <paramref name="i"/>: a Set
        /// card lies flat over the ring, so its tendrils drop into the pool at once,
        /// and rise over <see cref="FlipRiseSeconds"/> once it is face-up. A different
        /// monster at the index starts at its own target.
        /// </summary>
        void Rise(int i, in FieldAnchor a, float dt)
        {
            var target = a.FaceDown ? 0f : 1f;
            if (_anchorKey[i] != a.Key || target < _anchorRise[i])
            {
                _anchorKey[i] = a.Key;
                _anchorRise[i] = target;
            }
            else
            {
                _anchorRise[i] = Mathf.MoveTowards(_anchorRise[i], target, dt / FlipRiseSeconds);
            }
        }

        /// <summary>Stage camera in floor-local space; the player's end of the street without one.</summary>
        Vector3 EyeLocal(in FieldStreet street)
        {
            var cam = StageCamera;
            if (cam != null) return transform.InverseTransformPoint(cam.transform.position);
            return new Vector3(street.Center.x, street.Center.y, street.Center.z - street.Half.z - 1f);
        }

        /// <summary>One monster: pool first, then its tendrils from the flanks to the front.</summary>
        void WriteAnchor(int v, in FieldAnchor a, float fade, float rise, Vector3 eye)
        {
            _o = a.Position;
            _s = a.Scale;
            var toEye = eye - _o;
            var flat = Mathf.Sqrt(toEye.x * toEye.x + toEye.z * toEye.z);
            _camX = flat > 1e-4f ? toEye.x / flat : 0f;
            _camZ = flat > 1e-4f ? toEye.z / flat : -1f;
            _view = toEye.sqrMagnitude > 1e-8f ? toEye.normalized : Vector3.up;

            var aura = a.Aura > 0 ? 1 : a.Aura < 0 ? -1 : 0;
            WritePool(v, fade, aura, Hash01(a.Key, 47) * TwoPi);

            var tv = v + PoolVerts;
            var risen = Mathf.SmoothStep(0f, 1f, rise);
            if (risen <= 0.001f)
            {
                // Set card flat over the ring: zero-area, clear tendrils keep the slot layout.
                Collapse(tv, _count * TendrilVerts, _o);
                return;
            }

            Layout(a.Key);
            for (var i = 0; i < _count; i++)
            {
                _tFront[i] = _tRx[i] * _camX + _tRz[i] * _camZ;
                _tOrder[i] = i;
            }

            SortByKey(_tOrder, _tFront, _count);

            var grow = Mathf.Lerp(GrowFloor, 1f, Mathf.SmoothStep(0f, 1f, fade)) * risen;
            for (var k = 0; k < _count; k++)
                WriteTendril(tv + k * TendrilVerts, _tOrder[k], fade * risen, grow, aura);
        }

        /// <summary>
        /// Spreads this monster's tendrils over both flanks as seen from the stage
        /// camera (the art hides the far half of the ring), and hashes everything
        /// else from its <paramref name="key"/>.
        /// </summary>
        void Layout(int key)
        {
            var cam = Mathf.Atan2(_camZ, _camX);
            // Odd counts put the extra tendril on one side, chosen per monster.
            var plus = (_count + (Hash01(key, 43) < 0.5f ? 1 : 0)) / 2;
            // Each side sways as a whole, so its tendrils keep their spacing.
            var swayPlus = SwayAngle * Mathf.Sin(_phSway + Hash01(key, 44) * TwoPi);
            var swayMinus = SwayAngle * Mathf.Sin(_phSway + Hash01(key, 45) * TwoPi);
            for (var i = 0; i < _count; i++)
            {
                var onPlus = i < plus;
                var side = onPlus ? 1f : -1f;
                var n = onPlus ? plus : _count - plus;
                var k = onPlus ? i : i - plus;
                var salt = TendrilSalt + i * TendrilSaltStride;
                var at = (k + 0.5f + ArcJitter * (2f * Hash01(key, salt) - 1f)) / n;
                var ang = cam + side * (Mathf.Lerp(ArcFrom, ArcTo, at) + (onPlus ? swayPlus : swayMinus));
                _tRx[i] = Mathf.Cos(ang);
                _tRz[i] = Mathf.Sin(ang);

                ref var t = ref _tendrils[i];
                t.Root = Mathf.Lerp(0.94f, 1.06f, Hash01(key, salt + 1));
                t.Length = Mathf.Lerp(0.85f, 1f, Hash01(key, salt + 2));
                t.Width = Mathf.Lerp(0.85f, 1.1f, Hash01(key, salt + 3));
                // Coil toward the camera: coiling away would carry the tip behind the art.
                t.SwirlDir = -side;
                t.PhaseLean = Hash01(key, salt + 4) * TwoPi;
                t.PhaseSide = Hash01(key, salt + 5) * TwoPi;
                t.PhaseTwitch = Hash01(key, salt + 6) * TwoPi;
                t.PhasePulse = Hash01(key, salt + 7) * TwoPi;
            }
        }

        /// <summary>Flat ring of void outside the ownership ring, with a patchy crimson outer edge.</summary>
        void WritePool(int v, float fade, int aura, float offset)
        {
            var breath = 1f - PoolBreath * (0.5f + 0.5f * Mathf.Sin(_phBreath + offset));
            var dark = Mathf.Min(MaxAlpha, PoolAlpha * (aura < 0 ? BanePoolGain : 1f)) * breath * fade;
            var rimGain = aura > 0 ? BoonRimGain : aura < 0 ? BaneRimGain : 1f;
            var rimA = Mathf.Min(MaxAlpha, PoolRimAlpha * rimGain) * fade;
            var core = aura < 0 ? _coreBane : _core;
            var rim = aura > 0 ? _rimBoon : aura < 0 ? _rimBane : _rim;
            Color32 inner = WithAlpha(core, 0f);
            Color32 band = WithAlpha(core, dark);
            Color32 outer = WithAlpha(rim, 0f);

            var y = _o.y + PoolLift * _s;
            var drift = _flicker + offset / TwoPi * PoolSegs;
            for (var i = 0; i < PoolSegs; i++)
            {
                var cs = _poolCos[i] * _s;
                var sn = _poolSin[i] * _s;
                _verts[v + i] = new Vector3(_o.x + cs * PoolInner, y, _o.z + sn * PoolInner);
                _verts[v + PoolSegs + i] = new Vector3(_o.x + cs * PoolDark, y, _o.z + sn * PoolDark);
                _verts[v + 2 * PoolSegs + i] = new Vector3(_o.x + cs * PoolRim, y, _o.z + sn * PoolRim);
                _verts[v + 3 * PoolSegs + i] = new Vector3(_o.x + cs * PoolOuter, y, _o.z + sn * PoolOuter);
                _cols[v + i] = inner;
                _cols[v + PoolSegs + i] = band;
                _cols[v + 2 * PoolSegs + i] = WithAlpha(rim, rimA * (1f - PoolFlicker * Noise(i + drift)));
                _cols[v + 3 * PoolSegs + i] = outer;
            }
        }

        /// <summary>
        /// One tendril: walk its centre line up from the root one row at a time
        /// (a unit heading per row, so it never stretches), then lay a view-facing
        /// ribbon of edge / rim / core / rim / edge across it.
        /// </summary>
        void WriteTendril(int v, int ti, float fade, float grow, int aura)
        {
            ref var t = ref _tendrils[ti];
            var si = aura + 1;
            var sh = Shapes[si];
            var rx = _tRx[ti];
            var rz = _tRz[ti];

            // Over the art's centre the monster must read through; the flanks frame it at full length.
            var centre = 1f - Ramp(CentreFrom, CentreTo, Mathf.Abs(rx * _camZ - rz * _camX) * sh.Root * t.Root);
            var len = _length * sh.Length * t.Length * Mathf.Lerp(1f, CentreLength, centre) * grow * _s;
            var alpha = fade * Mathf.Lerp(1f, CentreAlpha, centre);
            var ds = len / (Rows - 1);
            var root = sh.Root * t.Root * _s;
            var phLean = _phLean[si] - t.PhaseLean;
            var phSide = _phSide[si] - t.PhaseSide;
            var tw = _phTwitch + t.PhaseTwitch;
            var twitch = sh.Twitch * (0.65f * Mathf.Sin(tw) + 0.35f * Mathf.Sin(3f * tw + 1.3f));

            var p = new Vector3(_o.x + rx * root, _o.y, _o.z + rz * root);
            for (var j = 0; j < Rows; j++)
            {
                _rowP[j] = p;
                if (j == Rows - 1) break;

                // Heading at the middle of this step: flare, curl home, climbing waves.
                var s = (j + 0.5f) / (Rows - 1);
                var lean = sh.Lean + sh.Curl * s * s + twitch * s * s +
                           sh.WritheLean * s * Mathf.Sin(WaveCyclesLean * TwoPi * s - phLean);
                var side = t.SwirlDir * sh.Swirl * s +
                           sh.WritheSide * s * Mathf.Sin(WaveCyclesSide * TwoPi * s - phSide);
                var sl = Mathf.Sin(lean);
                var cl = Mathf.Cos(lean);
                var ss = Mathf.Sin(side) * cl;
                var up = Mathf.Cos(side) * cl;
                // Unit step: inward by the lean, round the ring by the side angle, up by the rest.
                p.x += (-rx * sl - rz * ss) * ds;
                p.y += up * ds;
                p.z += (-rz * sl + rx * ss) * ds;
            }

            var core = aura < 0 ? _coreBane : _core;
            var rim = aura > 0 ? _rimBoon : aura < 0 ? _rimBane : _rim;
            var coreA = Mathf.Min(MaxAlpha, CoreAlpha * (aura < 0 ? BaneCoreGain : 1f)) * alpha;
            // A boon lights the tendrils with climbing pulses instead of a flat gain, so the cap never flattens them.
            var rimBase = RimAlpha * (aura < 0 ? BaneRimGain : 1f);
            var wide = t.Width * _s * (0.5f + 0.5f * grow);
            var phPulse = _phPulse - t.PhasePulse;
            var across = new Vector3(-rz, 0f, rx);

            for (var j = 0; j < Rows; j++)
            {
                // Ribbon across the view so the tendril reads as a round, rim-lit tube from any side.
                // Near-parallel to the view the previous side is kept, so the ribbon never folds over.
                var tangent = _rowP[j < Rows - 1 ? j + 1 : j] - _rowP[j > 0 ? j - 1 : j];
                var cross = Vector3.Cross(tangent, _view);
                var m = cross.magnitude;
                if (m > 0.05f * tangent.magnitude && m > 1e-7f)
                {
                    cross /= m;
                    across = Vector3.Dot(cross, across) < 0f ? -cross : cross;
                }

                var rimA = rimBase;
                var rimCol = rim;
                if (aura > 0)
                {
                    // Narrow bands of light climbing the rims.
                    var q = 0.5f + 0.5f * Mathf.Sin(PulseCycles * TwoPi * j / (Rows - 1) - phPulse);
                    q *= q;
                    q *= q;
                    rimA *= 1f + BoonPulse * q;
                    rimCol = Color.Lerp(rim, Color.white, BoonPulseWhite * q);
                }

                Color32 cCore = WithAlpha(core, coreA * _coreAlong[j]);
                Color32 cRim = WithAlpha(rimCol, Mathf.Min(MaxAlpha, rimA) * alpha * _rimAlong[j]);
                Color32 cEdge = WithAlpha(rimCol, 0f);

                var c = _rowP[j];
                var edge = across * (_halfWidth[j] * wide);
                var lit = edge * RimAt;
                var o = v + j * RowVerts;
                _verts[o] = Contain(c - edge);
                _verts[o + 1] = Contain(c - lit);
                _verts[o + 2] = Contain(c);
                _verts[o + 3] = Contain(c + lit);
                _verts[o + 4] = Contain(c + edge);
                _cols[o] = cEdge;
                _cols[o + 1] = cRim;
                _cols[o + 2] = cCore;
                _cols[o + 3] = cRim;
                _cols[o + 4] = cEdge;
            }
        }

        /// <summary>Clamps a vertex inside the contract cylinder of the anchor being written.</summary>
        Vector3 Contain(Vector3 p)
        {
            var dx = p.x - _o.x;
            var dz = p.z - _o.z;
            var r = GuardRadius * _s;
            var d2 = dx * dx + dz * dz;
            if (d2 > r * r)
            {
                var k = r / Mathf.Sqrt(d2);
                p.x = _o.x + dx * k;
                p.z = _o.z + dz * k;
            }

            p.y = Mathf.Clamp(p.y, _o.y, _o.y + GuardHeight * _s);
            return p;
        }

        /// <summary>Zero-area, fully transparent vertices for unused anchor slots.</summary>
        void Collapse(int v, int n, Vector3 at)
        {
            for (var i = v; i < v + n; i++)
            {
                _verts[i] = at;
                _cols[i] = default;
            }
        }

        /// <summary>Smoothly interpolated pool noise, wrapping round the ring.</summary>
        float Noise(float x)
        {
            x = Mathf.Repeat(x, PoolSegs);
            var i = (int)x;
            var f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(_noise[i % PoolSegs], _noise[(i + 1) % PoolSegs], f);
        }

        /// <summary>Insertion sort of the first <paramref name="n"/> entries of <paramref name="order"/>, ascending key.</summary>
        static void SortByKey(int[] order, float[] key, int n)
        {
            for (var i = 1; i < n; i++)
            {
                var o = order[i];
                var j = i - 1;
                while (j >= 0 && key[order[j]] > key[o])
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = o;
            }
        }

        static float Ramp(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>Mixed in sRGB like the palette, then converted once for the Linear project.</summary>
        void BuildColours()
        {
            var accent = Env.Accent;
            var core = Color.Lerp(Color.Lerp(Env.Sky, Env.Ground, CoreGroundShare), Color.black, CoreDarken);
            _core = VertexColor(core);
            _coreBane = VertexColor(Color.Lerp(core, Color.black, BaneDarken));
            _rim = VertexColor(accent);
            _rimBoon = VertexColor(Color.Lerp(accent, Color.white, BoonRimWhite));
            _rimBane = VertexColor(Color.Lerp(accent, Env.Ground, BaneRimDrain));
        }

        void BuildTables()
        {
            _tendrils = new Tendril[MaxTendrils];
            _halfWidth = new float[Rows];
            _coreAlong = new float[Rows];
            _rimAlong = new float[Rows];
            _rowP = new Vector3[Rows];
            for (var j = 0; j < Rows; j++)
            {
                var s = j / (float)(Rows - 1);
                var u = 1f - s;
                var u2 = u * u;
                _halfWidth[j] = HalfWidth * (TipShare + (1f - TipShare) * Mathf.Pow(u, 1.3f)) *
                                (1f + RootFlare * u2 * u2 * u2);
                // The core thins out first, so the tips end as crimson wisps.
                _coreAlong[j] = Mathf.Lerp(0.55f, 1f, Ramp(0f, 0.15f, s)) * (1f - Ramp(0.35f, 0.95f, s));
                _rimAlong[j] = Mathf.Lerp(0.35f, 1f, Ramp(0f, 0.2f, s)) * (1f - Ramp(0.6f, 1f, s));
            }

            _poolCos = new float[PoolSegs];
            _poolSin = new float[PoolSegs];
            _noise = new float[PoolSegs];
            for (var i = 0; i < PoolSegs; i++)
            {
                var a = i / (float)PoolSegs * TwoPi;
                _poolCos[i] = Mathf.Cos(a);
                _poolSin[i] = Mathf.Sin(a);
                _noise[i] = Hash01(i, 41);
            }

            _order = new int[MaxAnchors];
            _dist = new float[MaxAnchors];
            _anchorKey = new int[MaxAnchors];
            _anchorRise = new float[MaxAnchors];
            _tOrder = new int[MaxTendrils];
            _tFront = new float[MaxTendrils];
            _tRx = new float[MaxTendrils];
            _tRz = new float[MaxTendrils];
        }

        int[] BuildTriangles()
        {
            var poolTris = (PoolRings - 1) * PoolSegs * 6;
            var tendrilTris = (Rows - 1) * (RowVerts - 1) * 6;
            var tris = new int[MaxAnchors * (poolTris + _count * tendrilTris)];
            var ti = 0;
            for (var a = 0; a < MaxAnchors; a++)
            {
                var b = a * _slotVerts;
                for (var r = 0; r < PoolRings - 1; r++)
                for (var i = 0; i < PoolSegs; i++)
                {
                    var i0 = b + r * PoolSegs + i;
                    var i1 = b + r * PoolSegs + (i + 1) % PoolSegs;
                    var i2 = i0 + PoolSegs;
                    var i3 = i1 + PoolSegs;
                    tris[ti++] = i0;
                    tris[ti++] = i2;
                    tris[ti++] = i1;
                    tris[ti++] = i1;
                    tris[ti++] = i2;
                    tris[ti++] = i3;
                }

                for (var k = 0; k < _count; k++)
                {
                    var tb = b + PoolVerts + k * TendrilVerts;
                    for (var j = 0; j < Rows - 1; j++)
                    for (var c = 0; c < RowVerts - 1; c++)
                    {
                        var i0 = tb + j * RowVerts + c;
                        var i1 = i0 + 1;
                        var i2 = i0 + RowVerts;
                        var i3 = i2 + 1;
                        tris[ti++] = i0;
                        tris[ti++] = i2;
                        tris[ti++] = i1;
                        tris[ti++] = i1;
                        tris[ti++] = i2;
                        tris[ti++] = i3;
                    }
                }
            }

            return tris;
        }
    }
}
