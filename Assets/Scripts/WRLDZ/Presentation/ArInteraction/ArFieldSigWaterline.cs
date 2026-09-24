using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Monsters wade. Around each monster a ring of the field's water stands
    /// level at shin height. The ring opens just outside the cyan/magenta
    /// ownership disc: the water is clear inside 0.46 × Scale and full by 0.52,
    /// so the disc, the middle of the terrain pad and the aura stay dry and
    /// readable. It runs out to a soft shore at the rim (0.70 × Scale at
    /// SignatureScale 1, 0.64–0.72 across the range). Sideways the shore is
    /// pulled in to <see cref="ArFieldSignature.LaneHalfWidth"/>, and toward
    /// the street midline it stops <see cref="ArFieldSignature.AisleClear"/>
    /// short (<see cref="ArFieldSignature.MidlineGap"/>), so neighbouring and
    /// facing rings never meet. A monster lunging toward the midline loses its
    /// water on that side. The surface rolls with one shared wave field,
    /// so neighbouring rings agree. A broken foam line laps at the inner
    /// waterline and ripple rings spread outward from it. Foam, ripples, surf
    /// and flecks all stay outside 0.5 × Scale and peak at alpha 0.6, boon
    /// included. On a boon the foam is brighter and pulses; on a bane the
    /// water turns murky. The water rises with the sweep and drains on dissolve.
    /// <para>Cards: every vertex is clamped with
    /// <see cref="ArFieldSignature.CoverLimit"/>. In front of upright art the
    /// water stays inside the art's lower band (at most 0.30 × Scale at any
    /// SignatureScale up to 1.5). When face-up art turns sideways into Defense,
    /// the whole level drops at once, so its tallest point (swell, surf and
    /// foam included) sits at the art's FrontCoverHeight. A Set card lies flat
    /// on the water. Face-down, and through a flip until the card has stood up,
    /// the whole pool drops at once to a thin sheet under SetCardClearHeight.
    /// This is stricter than the contract, which only limits the card's
    /// footprint (InSetCard): level water cannot stand shin-high in the narrow
    /// strips in front of and behind the card and also stay 1 cm high over the
    /// card. Once the card stands, or the art rolls
    /// upright, the water swells back over 0.6 s. Each monster's depth ease,
    /// and its ripple, foam and fleck timing, is keyed by its anchor Key, so
    /// nothing jumps while other monsters come and go.</para>
    /// <para>Variant 0, open sea: deep, dark and choppy. Peaked cross-swell,
    /// three quick broken ripples, short white-cap streaks along the crest lines.</para>
    /// <para>Variant 1, tropical tide: bright, shallow and calm. Long swell, two
    /// slow ripples and sun glints. Surf breaks white along each swell crest
    /// where it rolls past the monster's flanks; the dry disc splits it into two runs.
    /// Any other variant draws the open sea.</para>
    /// One dynamic mesh, one draw call, 396 vertices per monster (3960 for ten).
    /// Soft edges come from vertex alpha and the shared soft dot: strips sample
    /// its centre line, flecks the whole dot.
    /// </summary>
    public sealed class ArFieldSigWaterline : ArFieldSignature
    {
        const int MaxAnchors = 10;
        const int Segs = 24;
        /// <summary>Rings per spoke: the dry edge, the wet edge, then four more out to the shore.</summary>
        const int DiscRings = 6;
        const int WaveCount = 3;
        const int RippleCount = 3;
        /// <summary>The dry disc splits the surf line into up to two runs.</summary>
        const int CrestRuns = 2;
        const int CrestCols = 7;
        const int FleckCount = 8;

        // Vertex layout of one anchor slot: water ring, ripple strips, waterline strip, surf runs, fleck quads.
        const int DiscVerts = DiscRings * Segs;
        const int StripVerts = Segs * 2;
        const int RippleBase = DiscVerts;
        const int LipBase = RippleBase + RippleCount * StripVerts;
        const int CrestBase = LipBase + StripVerts;
        const int RunVerts = CrestCols * 2;
        const int FleckBase = CrestBase + CrestRuns * RunVerts;
        /// <summary>396 per slot; × <see cref="MaxAnchors"/> = 3960 ≤ <see cref="ArFieldSignature.MaxVertices"/>.</summary>
        const int SlotVerts = FleckBase + FleckCount * 4;
        const int DiscTris = (DiscRings - 1) * Segs * 2;
        const int SlotTris = DiscTris + (RippleCount + 1) * Segs * 2 + CrestRuns * (CrestCols - 1) * 2 + FleckCount * 2;

        // Radii in host-local metres (× anchor.Scale). The ownership disc is 0.45.
        /// <summary>Water alpha is 0 here and there is no water inside it.</summary>
        const float DryRadius = 0.46f;
        /// <summary>Full water from here out to where the shore fade starts.</summary>
        const float WetRadius = 0.52f;
        /// <summary>Foam, ripples, surf and flecks all stay outside this.</summary>
        const float LightInner = 0.5f;
        const float RimBase = 0.58f;
        const float RimPerSigScale = 0.12f;
        /// <summary>Hard cap on the rim, inside MaxAnchorRadius at any SigScale.</summary>
        const float RimCap = 0.72f;
        /// <summary>A spoke that the lane or aisle cuts shorter than DryRadius + this fades out.</summary>
        const float MinWetBand = 0.1f;
        /// <summary>Light pieces fade out on spokes with less room than this outside LightInner.</summary>
        const float MinLightBand = 0.05f;
        /// <summary>Light pieces stay this far inside the shore.</summary>
        const float EdgeMargin = 0.015f;
        /// <summary>Shaved off the lane and aisle limits so rounding never crosses them.</summary>
        const float LimitSafety = 0.005f;
        const float SurfacePerSigScale = 0.03f;
        /// <summary>Share of the wet band (wet edge → shore) where the surface starts fading out.</summary>
        const float ShoreFadeStart = 0.3f;
        /// <summary>Share of the wet band where waves start flattening toward the shore.</summary>
        const float CalmStart = 0.6f;
        /// <summary>Water level (share of full) as the sweep first reaches a monster.</summary>
        const float RiseFloor = 0.3f;
        /// <summary>Foam, ripples and flecks ride this far above the surface.</summary>
        const float FoamLift = 0.006f;
        /// <summary>Lowest the water sheet sits above the street.</summary>
        const float WaterFloor = 0.003f;
        /// <summary>Anchors at or below this Scale are skipped, never inflated past their own size.</summary>
        const float MinScale = 0.001f;
        /// <summary>Seconds for the water to swell back once a flipped card has stood up (or art rolls upright).</summary>
        const float FlipRiseSeconds = 0.6f;
        /// <summary>Peak alpha of any light piece (foam, ripples, surf, flecks), boon gains included.</summary>
        const float LightCap = 0.6f;

        const float RippleStart = 0.53f;
        const float RippleWidth0 = 0.025f;
        const float RippleWidth1 = 0.05f;

        const float LipRadius = 0.545f;
        const float LipBreath = 0.01f;
        const float LipWidth = 0.05f;
        const float LipLapSpeed = 1.3f;
        /// <summary>Foam break-up drifting round the waterline (noise segments per second).</summary>
        const float LipFlow = 1.1f;

        const float CrestWidth = 0.07f;
        const float CrestLift = 0.028f;
        /// <summary>Surf runs shorter than this are not drawn; twice this is full strength.</summary>
        const float CrestMinRun = 0.04f;

        const int FleckWrapCycles = 4096;

        const float BoonLipGain = 1.9f;
        const float BoonLipBreak = 0.4f;
        const float BoonRippleGain = 1.25f;
        const float BoonCrestGain = 1.2f;
        const float BaneMurk = 0.3f;
        const float BaneLipGain = 0.6f;

        const float TwoPi = Mathf.PI * 2f;

        /// <summary>One travelling wave train in floor-local space.</summary>
        readonly struct Wave
        {
            /// <summary>Travel direction, degrees from +X toward +Z.</summary>
            public readonly float Direction;
            /// <summary>Host-local metres (× anchor.Scale).</summary>
            public readonly float Length;
            /// <summary>Radians per second.</summary>
            public readonly float Speed;
            public readonly float Weight;

            public Wave(float direction, float length, float speed, float weight)
            {
                Direction = direction;
                Length = length;
                Speed = speed;
                Weight = weight;
            }
        }

        /// <summary>One variant's water: depth, chop, motion and foam. Lengths are host-local metres.</summary>
        struct Look
        {
            public float Surface;      // shin height at SigScale 1
            public float Amplitude;    // wave height per unit SigScale
            public float Chop;         // 0 round swell … 1 peaked crests, flat troughs
            public float Alpha;
            public float CrestGain;
            public float TroughGain;
            public Wave A, B, C;       // A leads: white caps and surf line up with its crests
            public int Ripples;
            public float RipplePeriod;
            public float RippleAlpha;
            public float Break;        // how broken ripples and foam are round the ring
            public float LipAlpha;
            public float CrestAlpha;   // 0 = no surf line
            public bool Glints;        // flecks twinkle as sun glints instead of white-cap streaks
            public float FleckLife;
            public float FleckHalfLength;
            public float FleckHalfWidth;
            public float FleckDrift;   // per life, along wave A
            public float FleckAlpha;
        }

        static readonly Look Sea = new Look
        {
            Surface = 0.23f, Amplitude = 0.028f, Chop = 0.9f, Alpha = 0.5f, CrestGain = 0.75f, TroughGain = 0.5f,
            A = new Wave(32f, 0.44f, 4.2f, 0.5f),
            B = new Wave(-48f, 0.34f, 5.1f, 0.3f),
            C = new Wave(105f, 0.66f, 2.9f, 0.2f),
            Ripples = 3, RipplePeriod = 1.5f, RippleAlpha = 0.4f, Break = 0.6f,
            LipAlpha = 0.3f, CrestAlpha = 0f,
            Glints = false, FleckLife = 1.2f, FleckHalfLength = 0.04f, FleckHalfWidth = 0.014f,
            FleckDrift = 0.05f, FleckAlpha = 0.6f
        };

        static readonly Look Tide = new Look
        {
            Surface = 0.2f, Amplitude = 0.016f, Chop = 0.15f, Alpha = 0.4f, CrestGain = 0.5f, TroughGain = 0.2f,
            A = new Wave(80f, 0.72f, 2.0f, 0.6f),
            B = new Wave(20f, 0.5f, 2.5f, 0.25f),
            C = new Wave(150f, 0.4f, 2.9f, 0.15f),
            Ripples = 2, RipplePeriod = 2.6f, RippleAlpha = 0.5f, Break = 0.15f,
            LipAlpha = 0.45f, CrestAlpha = 0.5f,
            Glints = true, FleckLife = 0.7f, FleckHalfLength = 0.028f, FleckHalfWidth = 0.028f,
            FleckDrift = 0f, FleckAlpha = 0.6f
        };

        Look _look;
        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        /// <summary>Slots written last frame; slots past the live count are cleared once.</summary>
        int _lit;

        // Kit-wide shape (host-local metres).
        float _rim;
        float _surface;
        float _amplitude;
        /// <summary>Second-harmonic share in <see cref="Swell"/>.</summary>
        float _chop;
        /// <summary>Tallest point per unit depth: level + swell + surf curl.</summary>
        float _peak;
        float _fleckHalfDiag;
        /// <summary>Wave A's length: the surf rides its crest nearest the monster.</summary>
        float _crestSpan;
        /// <summary>Dry-edge ring radius whose chords between spokes stay outside DryRadius.</summary>
        float _dryRing;

        // Last frame's (Key, depth) pairs, so an ease survives others coming and going. Swapped every Tick.
        int[] _prevKey;
        float[] _prevDepth;
        int _prevCount;
        int[] _nextKey;
        float[] _nextDepth;

        // Per ring, the same on every spoke. Ring 0 is the dry edge; rings 1… run wet edge (f = 0) → shore (f = 1).
        float[] _ringF;
        float[] _ringAlpha;
        float[] _ringShade;
        float[] _ringCalm;

        // Current anchor: per spoke, then per water vertex.
        /// <summary>How far the water reaches along each spoke (rim, lane and aisle).</summary>
        float[] _reach;
        /// <summary>0…1: water fades on spokes the lane or aisle cuts short.</summary>
        float[] _wet;
        /// <summary>0…1: light pieces fade on spokes without room outside LightInner.</summary>
        float[] _light;
        float[] _ringR;
        /// <summary>Surface height per water vertex; foam reads it so it rides the drawn surface.</summary>
        float[] _hy;

        float[] _cos;
        float[] _sin;
        /// <summary>WaveCount × Segs: each spoke's direction · each wave's travel direction.</summary>
        float[] _spokeDot;
        /// <summary>0…1 per segment: breaks ripples and foam into patches.</summary>
        float[] _noise;
        float[] _crestTaper;

        float[] _wDirX;
        float[] _wDirZ;
        float[] _wK;
        float[] _wSpeed;
        float[] _wWeight;
        float[] _ph;
        float[] _base;

        float _ripplePh;
        float _lipPh;
        float _lipDrift;
        float _fleckClock;
        Vector2 _capAlong;
        Vector2 _capDrift;

        Color _deep;
        Color _shallow;
        Color _trough;
        Color _crest;
        Color _ripple;
        Color _foam;
        Color _foamBoon;
        Color _fleck;
        Color _murk;

        // Anchor being written.
        FieldAnchor _a;
        FieldStreet _street;
        Vector3 _o;
        float _s;
        /// <summary>−1 on the player's half, +1 on the opponent's (MidlineGap's rule).</summary>
        float _side;
        /// <summary>Room from the anchor toward the midline, less AisleClear.</summary>
        float _aisle;
        float _lane;

        protected override void Build()
        {
            var tide = Variant == 1;
            _look = tide ? Tide : Sea;
            var sig = SigScale;
            _rim = Mathf.Min(RimCap, RimBase + RimPerSigScale * sig);
            _surface = _look.Surface + SurfacePerSigScale * (sig - 1f);
            _amplitude = _look.Amplitude * sig;
            _chop = 0.5f * Mathf.Clamp01(_look.Chop);
            _peak = Mathf.Max(1e-4f, _surface + _amplitude + (_look.CrestAlpha > 0f ? CrestLift : 0f));
            _prevKey = new int[MaxAnchors];
            _prevDepth = new float[MaxAnchors];
            _nextKey = new int[MaxAnchors];
            _nextDepth = new float[MaxAnchors];
            _reach = new float[Segs];
            _wet = new float[Segs];
            _light = new float[Segs];

            BuildColours(tide);
            BuildTables();

            _verts = new Vector3[MaxAnchors * SlotVerts];
            _cols = new Color32[_verts.Length];
            _mesh = NewDynamicMesh("FieldWaterline");
            _mesh.vertices = _verts;
            _mesh.uv = BuildUvs();
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles();
            _mr = AddMeshChild("Waterline", _mesh, VertexColorMaterial("FieldWaterlineMat", GroundQueue));
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
            _street = street;
            var bounds = new Bounds(street.Center, street.Half * 2f);
            var drew = false;
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                var depth = Depth(a, step);
                _nextKey[i] = a.Key;
                _nextDepth[i] = depth;
                if (WriteSlot(i, a, level, depth))
                {
                    drew = true;
                    var s = a.Scale;
                    bounds.Encapsulate(new Bounds(
                        a.Position + new Vector3(0f, MaxAnchorHeight * 0.5f * s, 0f),
                        new Vector3(MaxAnchorRadius * 2f * s, MaxAnchorHeight * s, MaxAnchorRadius * 2f * s)));
                }
                else
                {
                    ClearSlot(i);
                }
            }

            // This frame's depths become next frame's lookup (swap, no allocation).
            var keys = _prevKey;
            _prevKey = _nextKey;
            _nextKey = keys;
            var depths = _prevDepth;
            _prevDepth = _nextDepth;
            _nextDepth = depths;
            _prevCount = count;

            for (var i = count; i < _lit; i++)
                ClearSlot(i);
            _lit = count;

            if (!drew)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box (plus each pool) keeps the renderer from being culled.
            _mesh.bounds = bounds;
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Advance(float dt)
        {
            for (var j = 0; j < WaveCount; j++)
                _ph[j] = Mathf.Repeat(_ph[j] - _wSpeed[j] * dt, TwoPi);
            _ripplePh = Mathf.Repeat(_ripplePh + dt / Mathf.Max(0.1f, _look.RipplePeriod), 1f);
            _lipPh = Mathf.Repeat(_lipPh + LipLapSpeed * dt, TwoPi);
            _lipDrift = Mathf.Repeat(_lipDrift + LipFlow * dt, Segs);
            _fleckClock = Mathf.Repeat(_fleckClock + dt, _look.FleckLife * FleckWrapCycles);
        }

        /// <summary>
        /// Share of full depth for this monster. It drops at once when the target
        /// falls and eases back up over <see cref="FlipRiseSeconds"/>. State is
        /// looked up by Key in last frame's anchors; a monster not seen then
        /// starts at its target.
        /// </summary>
        float Depth(in FieldAnchor a, float dt)
        {
            var target = DepthTarget(a);
            for (var j = 0; j < _prevCount; j++)
            {
                if (_prevKey[j] != a.Key) continue;
                var was = _prevDepth[j];
                return target < was ? target : Mathf.MoveTowards(was, target, dt / FlipRiseSeconds);
            }

            return target;
        }

        /// <summary>
        /// Depth whose tallest point (level, swell, surf curl and foam lift)
        /// meets the card's ceiling. Face-down, including a flip until the card
        /// stands: under SetCardClearHeight. Face-up: the art's FrontCoverHeight,
        /// which is low for sideways Defense art. Upright art stays at full depth.
        /// </summary>
        float DepthTarget(in FieldAnchor a)
        {
            var ceiling = MaxAnchorHeight;
            if (a.FaceDown) ceiling = SetCardClearHeight;
            else if (a.FrontCoverHeight > 0f && a.Scale > MinScale)
                ceiling = Mathf.Min(ceiling, a.FrontCoverHeight / a.Scale);
            return Mathf.Clamp01((ceiling - FoamLift) / _peak);
        }

        /// <summary>Writes one pool; false when the anchor is not visible yet (slot gets cleared).</summary>
        bool WriteSlot(int slot, in FieldAnchor a, float level, float depth)
        {
            var fade = Mathf.Clamp01(a.Presence) * level;
            if (fade <= 0.001f || a.Scale <= MinScale) return false;

            _a = a;
            _o = a.Position;
            _s = a.Scale;
            // MidlineGap's side rule: the pool keeps AisleClear on its own side of the midline.
            _side = a.Position.z < 0f ? -1f : 1f;
            _aisle = _side * a.Position.z / _s - AisleClear - LimitSafety;
            _lane = LaneHalfWidth - LimitSafety;
            for (var s = 0; s < Segs; s++)
                _reach[s] = Reach(_cos[s], _sin[s], 0f);
            // A spoke cut inside the dry disc (deep in a lunge) folds its vertices inward. Its
            // neighbours fade with it, so no visible triangle stretches into the disc.
            for (var s = 0; s < Segs; s++)
            {
                var room = Mathf.Min(_reach[s], Mathf.Min(_reach[(s + 1) % Segs], _reach[(s + Segs - 1) % Segs]));
                _wet[s] = Mathf.Clamp01((room - DryRadius) / MinWetBand);
                _light[s] = Mathf.Clamp01((room - EdgeMargin - LightInner) / MinLightBand);
            }

            // Every height scales with rise, so a lowered pool keeps surf and foam low too.
            var rise = Mathf.Lerp(RiseFloor, 1f, Smooth(0f, 1f, fade)) * depth;
            // Seeded by the monster, not its slot or spot: it keeps its rhythm while others come and go or it lunges.
            var offset = Hash01(a.Key, 19);
            var b = slot * SlotVerts;

            WriteDisc(b, fade, rise, a.Aura < 0);
            WriteRipples(b, fade, offset, a.Aura > 0);
            WriteLip(b, fade, offset, a.Aura);
            if (_look.CrestAlpha > 0f) WriteCrest(b, fade, rise, offset, a.Aura > 0);
            WriteFlecks(b, offset, Mathf.FloorToInt(Hash01(a.Key, 131) * 65536f), fade);
            return true;
        }

        /// <summary>
        /// Distance from the current anchor along unit direction (c, sn), kept
        /// <paramref name="inset"/> inside the rim, the lane (LaneHalfWidth
        /// sideways) and the aisle (AisleClear short of the midline). 0 when the
        /// anchor has no aisle room left, for example deep in a lunge.
        /// </summary>
        float Reach(float c, float sn, float inset)
        {
            var aisle = _aisle - inset;
            if (aisle <= 0f) return 0f;
            var r = _rim - inset;
            var lane = _lane - inset;
            var ac = Mathf.Abs(c);
            if (ac * r > lane) r = lane / ac;
            var toMid = -_side * sn;
            if (toMid * r > aisle) r = aisle / toMid;
            return Mathf.Max(0f, r);
        }

        /// <summary>
        /// Floor-local vertex at offset (x, z) from the anchor and height y, all
        /// host-local. Its top is clamped with CoverLimit, which covers the Set card
        /// footprint and the camera side of the art.
        /// </summary>
        Vector3 Place(float x, float y, float z)
        {
            var p = new Vector3(_o.x + _s * x, _o.y + _s * y, _o.z + _s * z);
            var top = _o.y + CoverLimit(_a, _street, p);
            if (p.y > top) p.y = top;
            return p;
        }

        void WriteDisc(int b, float fade, float rise, bool bane)
        {
            var surface = _surface * rise;
            var amp = _amplitude * rise;
            // One sea under every pool: wave phase follows the floor position, so neighbours agree
            // and a lunging monster wades through the swell instead of dragging it along.
            for (var j = 0; j < WaveCount; j++)
                _base[j] = _wK[j] * (_o.x * _wDirX[j] + _o.z * _wDirZ[j]) / _s + _ph[j];

            for (var s = 0; s < Segs; s++)
            {
                var reach = _reach[s];
                var wet = Mathf.Min(WetRadius, reach);
                var cs = _cos[s];
                var sn = _sin[s];
                for (var k = 0; k < DiscRings; k++)
                {
                    var v = k * Segs + s;
                    var r = k == 0 ? Mathf.Min(_dryRing, reach) : Mathf.Lerp(wet, reach, _ringF[k]);
                    var w = 0f;
                    for (var j = 0; j < WaveCount; j++)
                        w += _wWeight[j] * Swell(_wK[j] * r * _spokeDot[j * Segs + s] + _base[j]);

                    var calm = _ringCalm[k];
                    var y = Mathf.Max(WaterFloor, surface + amp * calm * w);
                    _ringR[v] = r;
                    _hy[v] = y;
                    _verts[b + v] = Place(r * cs, y, r * sn);

                    var hi = Mathf.Clamp01((w - 0.3f) / 0.7f) * calm;
                    var c = Color.Lerp(_deep, _shallow, _ringShade[k]);
                    c = Color.Lerp(c, _trough, Mathf.Clamp01(-w) * _look.TroughGain);
                    c = Color.Lerp(c, _crest, hi * _look.CrestGain);
                    if (bane) c = Color.Lerp(c, _murk, BaneMurk);
                    _cols[b + v] = WithAlpha(c, _look.Alpha * _ringAlpha[k] * _wet[s] * (1f + 0.35f * hi) * fade);
                }
            }
        }

        void WriteRipples(int b, float fade, float offset, bool boon)
        {
            var n = Mathf.Clamp(_look.Ripples, 0, RippleCount);
            var peak = Mathf.Min(LightCap, _look.RippleAlpha * (boon ? BoonRippleGain : 1f));
            for (var k = 0; k < n; k++)
            {
                var t = Mathf.Repeat(_ripplePh + (k + offset) / n, 1f);
                var grow = 1f - (1f - t) * (1f - t);
                var half = 0.5f * Mathf.Lerp(RippleWidth0, RippleWidth1, t);
                var alpha = peak * Mathf.Sin(Mathf.PI * t) * (1f - 0.4f * t) * fade;
                WriteRing(b + RippleBase + k * StripVerts, RippleStart, grow, half, _ripple, alpha,
                    _look.Break, 7.3f * k + offset * Segs);
            }
        }

        void WriteLip(int b, float fade, float offset, int aura)
        {
            var mid = LipRadius + LipBreath * Mathf.Sin(_lipPh + offset * TwoPi);
            var alpha = _look.LipAlpha;
            var col = _foam;
            var breakUp = _look.Break;
            if (aura > 0)
            {
                // The gain is clamped before the pulse, so the pulse still shows under the cap.
                alpha = Mathf.Min(LightCap, alpha * BoonLipGain) * (0.85f + 0.15f * Mathf.Sin(2f * _lipPh));
                col = _foamBoon;
                breakUp *= BoonLipBreak;
            }
            else if (aura < 0)
            {
                alpha *= BaneLipGain;
            }

            WriteRing(b + LipBase, mid, 0f, 0.5f * LipWidth, col, Mathf.Min(LightCap, alpha) * fade, breakUp,
                offset * Segs + _lipDrift);
        }

        /// <summary>
        /// Closed soft band riding the surface. Its centre moves from
        /// <paramref name="start"/> toward each spoke's shore by
        /// <paramref name="grow"/>, and the band stays EdgeMargin inside the
        /// spoke's reach. The inner row samples the dot's bottom edge and the
        /// outer row its top, so both edges fade.
        /// </summary>
        void WriteRing(int i, float start, float grow, float half, Color col, float alpha, float breakUp, float noiseAt)
        {
            for (var s = 0; s < Segs; s++)
            {
                var edge = _reach[s] - EdgeMargin;
                var mid = Mathf.Lerp(start, Mathf.Max(start, edge - half), grow);
                var outer = Mathf.Min(mid + half, edge);
                var inner = Mathf.Min(mid - half, outer);
                var cs = _cos[s];
                var sn = _sin[s];
                _verts[i + s] = Place(inner * cs, SpokeHeight(inner, s) + FoamLift, inner * sn);
                _verts[i + Segs + s] = Place(outer * cs, SpokeHeight(outer, s) + FoamLift, outer * sn);
                Color32 c = WithAlpha(col, alpha * _light[s] * (1f - breakUp * Noise(s + noiseAt)));
                _cols[i + s] = c;
                _cols[i + Segs + s] = c;
            }
        }

        /// <summary>
        /// Surf on wave A's crest nearest the monster: a straight line square to
        /// the swell that builds as the crest comes in and fades as it leaves,
        /// lifted as it curls. The dry disc splits it into two runs, and every
        /// run stays inside the rim, the lane and the aisle. Its phase is the
        /// shared swell's, so neighbouring rings break together.
        /// </summary>
        void WriteCrest(int b, float fade, float rise, float offset, bool boon)
        {
            // Crest nearest the centre (wave A's phase wraps to 0 there); t runs 0…1 as it crosses.
            var t = 1f - Mathf.Repeat(_base[0] + Mathf.PI, TwoPi) / TwoPi;
            var half = 0.5f * CrestWidth;
            var outer = _rim - EdgeMargin;
            var q = Mathf.Clamp((t - 0.5f) * _crestSpan, half - outer, outer - half);
            var alpha = Mathf.Min(LightCap, _look.CrestAlpha * (boon ? BoonCrestGain : 1f)) *
                        Smooth(0f, 0.2f, t) * (1f - Smooth(0.8f, 1f, t)) * fade;
            var dx = _wDirX[0];
            var dz = _wDirZ[0];

            // The run is the line q·d + u·(−dz, dx), up to ±half wide along d. Find the u it may span.
            var aq = Mathf.Abs(q);
            var far = aq + half;
            var lo = 0f;
            var hi = -1f;
            if (far < outer && _aisle > 0f)
            {
                hi = Mathf.Sqrt(outer * outer - far * far);
                lo = -hi;
                // Lane: |x| = |q·dx − u·dz ± half·dx| ≤ lane − EdgeMargin.
                Slab(ref lo, ref hi, q * dx, -dz, _lane - EdgeMargin - half * Mathf.Abs(dx));
                // Aisle: toward the midline, −side·(q·dz + u·dx ± half·dz) ≤ aisle − EdgeMargin.
                HalfPlane(ref lo, ref hi, -_side * q * dz, -_side * dx, _aisle - EdgeMargin - half * Mathf.Abs(dz));
            }

            // The dry disc: the strip's near edge must stay outside LightInner.
            var near = Mathf.Max(0f, aq - half);
            var hole = near < LightInner ? Mathf.Sqrt(LightInner * LightInner - near * near) : 0f;
            var i = b + CrestBase;
            var noise = offset * Segs;
            if (hole <= 0f)
            {
                WriteRun(i, lo, hi, q, half, alpha, rise, noise);
                Collapse(i + RunVerts, RunVerts);
            }
            else
            {
                WriteRun(i, lo, Mathf.Min(hi, -hole), q, half, alpha, rise, noise);
                WriteRun(i + RunVerts, Mathf.Max(lo, hole), hi, q, half, alpha, rise, noise + 7f);
            }
        }

        /// <summary>One surf run from u0 to u1 along the crest line, tapered at both ends.</summary>
        void WriteRun(int i, float u0, float u1, float q, float half, float alpha, float rise, float noiseAt)
        {
            var runAlpha = alpha * Smooth(CrestMinRun, 2f * CrestMinRun, u1 - u0);
            if (runAlpha <= 0f)
            {
                Collapse(i, RunVerts);
                return;
            }

            var dx = _wDirX[0];
            var dz = _wDirZ[0];
            for (var c = 0; c < CrestCols; c++)
            {
                var taper = _crestTaper[c];
                var u = Mathf.Lerp(u0, u1, c / (float)(CrestCols - 1));
                var px = q * dx - u * dz;
                var pz = q * dz + u * dx;
                var w = half * (0.35f + 0.65f * taper);
                var y = SurfaceAt(Mathf.Sqrt(px * px + pz * pz), Mathf.Atan2(pz, px)) + FoamLift +
                        CrestLift * rise * taper;
                _verts[i + c] = Place(px - w * dx, y, pz - w * dz);
                _verts[i + CrestCols + c] = Place(px + w * dx, y, pz + w * dz);
                Color32 col = WithAlpha(_crest, runAlpha * taper * (1f - _look.Break * Noise(c + noiseAt)));
                _cols[i + c] = col;
                _cols[i + CrestCols + c] = col;
            }
        }

        /// <summary>Narrows [lo, hi] to where |a + b·u| ≤ m.</summary>
        static void Slab(ref float lo, ref float hi, float a, float b, float m)
        {
            if (m < 0f)
            {
                hi = lo - 1f;
                return;
            }

            if (Mathf.Abs(b) < 1e-5f)
            {
                if (Mathf.Abs(a) > m) hi = lo - 1f;
                return;
            }

            var u1 = (-m - a) / b;
            var u2 = (m - a) / b;
            lo = Mathf.Max(lo, Mathf.Min(u1, u2));
            hi = Mathf.Min(hi, Mathf.Max(u1, u2));
        }

        /// <summary>Narrows [lo, hi] to where a + b·u ≤ m.</summary>
        static void HalfPlane(ref float lo, ref float hi, float a, float b, float m)
        {
            if (Mathf.Abs(b) < 1e-5f)
            {
                if (a > m) hi = lo - 1f;
                return;
            }

            var u = (m - a) / b;
            if (b > 0f) hi = Mathf.Min(hi, u);
            else lo = Mathf.Max(lo, u);
        }

        /// <summary>
        /// Short-lived flat flecks on the surface: white-cap streaks along the
        /// lead wave's crest lines (sea) or twinkling sun glints (tide). Each
        /// cycle re-seeds from a hash, so there is no per-fleck state. A fleck
        /// takes a bearing and a radius in the room that bearing has between
        /// LightInner and the shore. A life whose start or end bearing has no
        /// room is skipped, so no fleck pops out mid-life.
        /// </summary>
        void WriteFlecks(int b, float stagger, int seed, float fade)
        {
            var life = _look.FleckLife;
            var lx = _capAlong.x * _look.FleckHalfLength;
            var lz = _capAlong.y * _look.FleckHalfLength;
            var wx = -_capAlong.y * _look.FleckHalfWidth;
            var wz = _capAlong.x * _look.FleckHalfWidth;
            var inner = LightInner + _fleckHalfDiag;
            var inset = EdgeMargin + _fleckHalfDiag;
            var peak = Mathf.Min(LightCap, _look.FleckAlpha);
            var salt = seed * 131;
            for (var f = 0; f < FleckCount; f++)
            {
                var i = b + FleckBase + f * 4;
                var cycle = _fleckClock / life + (f + stagger) / FleckCount;
                var n = Mathf.FloorToInt(cycle);
                var u = cycle - n;
                var id = n * FleckCount + f;
                var ang = Hash01(id, 71 + salt) * TwoPi;
                var ca = Mathf.Cos(ang);
                var sa = Mathf.Sin(ang);
                var rad = Mathf.Lerp(inner, Reach(ca, sa, inset), Hash01(id, 17 + salt));
                var x0 = rad * ca;
                var z0 = rad * sa;
                if (!HasRoom(x0, z0, inner, inset) ||
                    !HasRoom(x0 + _capDrift.x, z0 + _capDrift.y, inner, inset))
                {
                    Collapse(i, 4);
                    continue;
                }

                // Drift, then hold the fleck inside the room along its current bearing.
                var px = x0 + _capDrift.x * u;
                var pz = z0 + _capDrift.y * u;
                var pr = Mathf.Sqrt(px * px + pz * pz);
                var bc = px / pr;
                var bs = pz / pr;
                var room = Reach(bc, bs, inset);
                if (room < inner)
                {
                    Collapse(i, 4);
                    continue;
                }

                pr = Mathf.Clamp(pr, inner, room);
                px = bc * pr;
                pz = bs * pr;

                var y = SurfaceAt(pr, Mathf.Atan2(pz, px)) + FoamLift;
                var env = Mathf.Sin(Mathf.PI * u);
                if (_look.Glints) env *= env * env;
                Color32 col = WithAlpha(_fleck, peak * env * fade);

                _verts[i] = Place(px - (lx + wx), y, pz - (lz + wz));
                _verts[i + 1] = Place(px + (lx - wx), y, pz + (lz - wz));
                _verts[i + 2] = Place(px + (lx + wx), y, pz + (lz + wz));
                _verts[i + 3] = Place(px - (lx - wx), y, pz - (lz - wz));
                _cols[i] = col;
                _cols[i + 1] = col;
                _cols[i + 2] = col;
                _cols[i + 3] = col;
            }
        }

        /// <summary>True when the bearing of (x, z) leaves a fleck room between <paramref name="inner"/> and the shore.</summary>
        bool HasRoom(float x, float z, float inner, float inset)
        {
            var r = Mathf.Sqrt(x * x + z * z);
            return r > 1e-5f && Reach(x / r, z / r, inset) >= inner;
        }

        /// <summary>Folds <paramref name="n"/> vertices onto the anchor, fully transparent: nothing drawn.</summary>
        void Collapse(int i, int n)
        {
            for (var k = 0; k < n; k++)
            {
                _verts[i + k] = _o;
                _cols[i + k] = default;
            }
        }

        void ClearSlot(int slot)
        {
            System.Array.Clear(_verts, slot * SlotVerts, SlotVerts);
            System.Array.Clear(_cols, slot * SlotVerts, SlotVerts);
        }

        /// <summary>Wave profile in −1…1; <see cref="_chop"/> adds a second harmonic that peaks crests and flattens troughs.</summary>
        float Swell(float phase)
        {
            var c = Mathf.Cos(phase);
            return (c + _chop * (2f * c * c - 1f)) / (1f + _chop);
        }

        /// <summary>Surface height along spoke <paramref name="s"/> at radius <paramref name="r"/>, as drawn.</summary>
        float SpokeHeight(float r, int s)
        {
            var r0 = _ringR[s];
            if (r <= r0) return _hy[s];
            for (var k = 1; k < DiscRings; k++)
            {
                var v = k * Segs + s;
                var r1 = _ringR[v];
                if (r <= r1)
                {
                    var span = r1 - r0;
                    return span > 1e-5f ? Mathf.Lerp(_hy[v - Segs], _hy[v], (r - r0) / span) : _hy[v];
                }

                r0 = r1;
            }

            return _hy[(DiscRings - 1) * Segs + s];
        }

        /// <summary>Surface height at radius <paramref name="r"/> and angle <paramref name="ang"/> (radians from +X toward +Z).</summary>
        float SurfaceAt(float r, float ang)
        {
            var f = Mathf.Repeat(ang / TwoPi, 1f) * Segs;
            var s0 = (int)f;
            var t = f - s0;
            s0 %= Segs;
            var s1 = (s0 + 1) % Segs;
            return Mathf.Lerp(SpokeHeight(r, s0), SpokeHeight(r, s1), t);
        }

        float Noise(float at)
        {
            var f = Mathf.Repeat(at, Segs);
            var i0 = (int)f;
            var t = f - i0;
            i0 %= Segs;
            return Mathf.Lerp(_noise[i0], _noise[(i0 + 1) % Segs], t);
        }

        /// <summary>GLSL-style smoothstep: 0 at or below e0, 1 at or above e1.</summary>
        static float Smooth(float e0, float e1, float x)
        {
            var t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ── Build ───────────────────────────────────────────────────────────

        void BuildColours(bool tide)
        {
            var sky = Env.Sky;
            var ground = Env.Ground;
            var accent = Env.Accent;
            Color deep, shallow, trough, crest, ripple, foam, fleck;
            if (tide)
            {
                // Sunlit shallows: saturated body brightening toward the shore; white only in surf and glints.
                deep = Color.Lerp(ground, sky, 0.3f);
                shallow = Color.Lerp(ground, accent, 0.6f);
                trough = Color.Lerp(ground, Color.black, 0.15f);
                crest = Color.Lerp(accent, Color.white, 0.85f);
                ripple = Color.Lerp(accent, Color.white, 0.6f);
                foam = Color.Lerp(accent, Color.white, 0.8f);
                fleck = Color.Lerp(sky, Color.white, 0.92f);
            }
            else
            {
                // Open sea: deep blue depths, bluer shallows, white caps. Kept off black so
                // the pool never reads as a dark hole in passthrough at night.
                deep = Color.Lerp(ground, Color.black, 0.1f);
                shallow = Color.Lerp(ground, sky, 0.6f);
                trough = Color.Lerp(ground, Color.black, 0.3f);
                crest = Color.Lerp(accent, Color.white, 0.5f);
                ripple = Color.Lerp(sky, Color.white, 0.45f);
                foam = Color.Lerp(accent, Color.white, 0.7f);
                fleck = Color.Lerp(accent, Color.white, 0.8f);
            }

            // Mixed in sRGB above, converted once here: mesh vertex colours reach the shader untouched.
            _deep = VertexColor(deep);
            _shallow = VertexColor(shallow);
            _trough = VertexColor(trough);
            _crest = VertexColor(crest);
            _ripple = VertexColor(ripple);
            _foam = VertexColor(foam);
            _foamBoon = VertexColor(Color.Lerp(foam, Color.white, 0.6f));
            _fleck = VertexColor(fleck);
            _murk = VertexColor(Color.Lerp(ground, Color.black, 0.7f));
        }

        void BuildTables()
        {
            _cos = new float[Segs];
            _sin = new float[Segs];
            for (var s = 0; s < Segs; s++)
            {
                var a = s * TwoPi / Segs;
                _cos[s] = Mathf.Cos(a);
                _sin[s] = Mathf.Sin(a);
            }

            _ringF = new float[DiscRings];
            _ringAlpha = new float[DiscRings];
            _ringShade = new float[DiscRings];
            _ringCalm = new float[DiscRings];
            for (var k = 0; k < DiscRings; k++)
            {
                var f = k == 0 ? 0f : (k - 1) / (float)(DiscRings - 2);
                _ringF[k] = f;
                // Clear at the dry edge, full at the wet edge, thinning out to the shore.
                _ringAlpha[k] = k == 0 ? 0f : 1f - Smooth(ShoreFadeStart, 1f, f);
                // Deepest in colour round the monster's legs, shallower toward the shore.
                _ringShade[k] = Smooth(0f, 1f, f);
                _ringCalm[k] = 1f - Smooth(CalmStart, 1f, f);
            }

            _dryRing = DryRadius / Mathf.Cos(Mathf.PI / Segs);
            _ringR = new float[DiscVerts];
            _hy = new float[DiscVerts];

            _wDirX = new float[WaveCount];
            _wDirZ = new float[WaveCount];
            _wK = new float[WaveCount];
            _wSpeed = new float[WaveCount];
            _wWeight = new float[WaveCount];
            _ph = new float[WaveCount];
            _base = new float[WaveCount];
            SetWave(0, _look.A);
            SetWave(1, _look.B);
            SetWave(2, _look.C);
            var total = Mathf.Max(1e-4f, _wWeight[0] + _wWeight[1] + _wWeight[2]);
            for (var j = 0; j < WaveCount; j++)
                _wWeight[j] /= total; // |sum| ≤ 1, so the height bound holds for any tuning

            _spokeDot = new float[WaveCount * Segs];
            for (var j = 0; j < WaveCount; j++)
            for (var s = 0; s < Segs; s++)
                _spokeDot[j * Segs + s] = _cos[s] * _wDirX[j] + _sin[s] * _wDirZ[j];

            var raw = new float[Segs];
            for (var s = 0; s < Segs; s++)
                raw[s] = R(0f, 1f);
            _noise = new float[Segs];
            for (var s = 0; s < Segs; s++)
                _noise[s] = 0.5f * raw[s] + 0.25f * (raw[(s + Segs - 1) % Segs] + raw[(s + 1) % Segs]);

            _crestTaper = new float[CrestCols];
            for (var c = 0; c < CrestCols; c++)
                _crestTaper[c] = Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * c / (CrestCols - 1))));
            _crestSpan = TwoPi / _wK[0];

            _capAlong = new Vector2(-_wDirZ[0], _wDirX[0]);
            _capDrift = new Vector2(_wDirX[0], _wDirZ[0]) * _look.FleckDrift;
            _fleckHalfDiag = Mathf.Sqrt(_look.FleckHalfLength * _look.FleckHalfLength +
                                        _look.FleckHalfWidth * _look.FleckHalfWidth);

            _ripplePh = R(0f, 1f);
            _lipPh = R(0f, TwoPi);
        }

        void SetWave(int j, Wave w)
        {
            var a = w.Direction * Mathf.Deg2Rad;
            _wDirX[j] = Mathf.Cos(a);
            _wDirZ[j] = Mathf.Sin(a);
            _wK[j] = TwoPi / Mathf.Max(0.05f, w.Length);
            _wSpeed[j] = w.Speed;
            _wWeight[j] = Mathf.Max(0f, w.Weight);
            _ph[j] = R(0f, TwoPi);
        }

        /// <summary>
        /// Water samples the dot's centre (flat); strips and surf runs take
        /// u = 0.5 with v across the band (soft both edges); flecks take the whole dot.
        /// </summary>
        Vector2[] BuildUvs()
        {
            var uv = new Vector2[_verts.Length];
            var centre = new Vector2(0.5f, 0.5f);
            var low = new Vector2(0.5f, 0f);
            var high = new Vector2(0.5f, 1f);
            for (var slot = 0; slot < MaxAnchors; slot++)
            {
                var b = slot * SlotVerts;
                for (var v = 0; v < DiscVerts; v++)
                    uv[b + v] = centre;

                for (var k = 0; k <= RippleCount; k++)
                {
                    var i = b + RippleBase + k * StripVerts;
                    for (var s = 0; s < Segs; s++)
                    {
                        uv[i + s] = low;
                        uv[i + Segs + s] = high;
                    }
                }

                for (var run = 0; run < CrestRuns; run++)
                for (var c = 0; c < CrestCols; c++)
                {
                    var i = b + CrestBase + run * RunVerts;
                    uv[i + c] = low;
                    uv[i + CrestCols + c] = high;
                }

                for (var f = 0; f < FleckCount; f++)
                {
                    var i = b + FleckBase + f * 4;
                    uv[i] = new Vector2(0f, 0f);
                    uv[i + 1] = new Vector2(1f, 0f);
                    uv[i + 2] = new Vector2(1f, 1f);
                    uv[i + 3] = new Vector2(0f, 1f);
                }
            }

            return uv;
        }

        /// <summary>
        /// Index order is the draw order inside the single draw call: every
        /// pool's water first, then ripples and waterlines, surf, flecks, so
        /// foam always lands on water.
        /// </summary>
        int[] BuildTriangles()
        {
            var tris = new int[MaxAnchors * SlotTris * 3];
            var t = 0;

            for (var slot = 0; slot < MaxAnchors; slot++)
            for (var r = 0; r < DiscRings - 1; r++)
            {
                var ring = slot * SlotVerts + r * Segs;
                for (var s = 0; s < Segs; s++)
                {
                    var s1 = (s + 1) % Segs;
                    tris[t++] = ring + s;
                    tris[t++] = ring + Segs + s;
                    tris[t++] = ring + s1;
                    tris[t++] = ring + s1;
                    tris[t++] = ring + Segs + s;
                    tris[t++] = ring + Segs + s1;
                }
            }

            for (var slot = 0; slot < MaxAnchors; slot++)
            for (var k = 0; k <= RippleCount; k++)
            {
                var i = slot * SlotVerts + RippleBase + k * StripVerts;
                for (var s = 0; s < Segs; s++)
                {
                    var s1 = (s + 1) % Segs;
                    tris[t++] = i + s;
                    tris[t++] = i + Segs + s;
                    tris[t++] = i + s1;
                    tris[t++] = i + s1;
                    tris[t++] = i + Segs + s;
                    tris[t++] = i + Segs + s1;
                }
            }

            for (var slot = 0; slot < MaxAnchors; slot++)
            for (var run = 0; run < CrestRuns; run++)
            {
                var i = slot * SlotVerts + CrestBase + run * RunVerts;
                for (var c = 0; c < CrestCols - 1; c++)
                {
                    tris[t++] = i + c;
                    tris[t++] = i + CrestCols + c;
                    tris[t++] = i + c + 1;
                    tris[t++] = i + c + 1;
                    tris[t++] = i + CrestCols + c;
                    tris[t++] = i + CrestCols + c + 1;
                }
            }

            for (var slot = 0; slot < MaxAnchors; slot++)
            for (var f = 0; f < FleckCount; f++)
            {
                var i = slot * SlotVerts + FleckBase + f * 4;
                tris[t++] = i;
                tris[t++] = i + 2;
                tris[t++] = i + 1;
                tris[t++] = i;
                tris[t++] = i + 3;
                tris[t++] = i + 2;
            }

            return tris;
        }
    }
}
