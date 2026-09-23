using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Monsters wade. Around each monster a translucent pool of the field's
    /// water stands at shin height: deepest where the monster stands, dipping
    /// to a soft shoreline that fades to nothing inside
    /// <see cref="ArFieldSignature.MaxAnchorRadius"/>, so the aisle stays street.
    /// The surface rolls with one shared wave field (neighbouring pools agree),
    /// ripple rings spread from the monster's shins and a broken foam line laps
    /// at the waterline — brighter and pulsing on a boon, over murkier water on
    /// a bane. The water rises with the sweep and drains on dissolve.
    /// <para>Variant 0, open sea: deep, dark and choppy. Peaked cross-swell,
    /// three quick broken ripples, white-cap streaks along the crest lines.</para>
    /// <para>Variant 1, tropical tide: bright, shallow and calm. Long swell, two
    /// slow ripples, a white crest running round the pool and sun glints.
    /// Any other variant draws the open sea.</para>
    /// One dynamic mesh, one draw call. Soft edges come from vertex alpha and the
    /// shared soft dot: strips sample its centre line, flecks the whole dot.
    /// </summary>
    public sealed class ArFieldSigWaterline : ArFieldSignature
    {
        const int MaxAnchors = 10;
        const int Segs = 24;
        const int DiscRings = 6;
        const int WaveCount = 3;
        const int RippleCount = 3;
        const int CrestCols = 13;
        const int FleckCount = 8;

        // Vertex layout of one anchor slot: disc, ripple strips, waterline strip, crest strip, fleck quads.
        const int DiscVerts = 1 + DiscRings * Segs;
        const int StripVerts = Segs * 2;
        const int RippleBase = DiscVerts;
        const int LipBase = RippleBase + RippleCount * StripVerts;
        const int CrestBase = LipBase + StripVerts;
        const int FleckBase = CrestBase + CrestCols * 2;
        /// <summary>395 per slot; × <see cref="MaxAnchors"/> = 3950 ≤ <see cref="ArFieldSignature.MaxVertices"/>.</summary>
        const int SlotVerts = FleckBase + FleckCount * 4;
        const int DiscTris = Segs + (DiscRings - 1) * Segs * 2;
        const int SlotTris = DiscTris + (RippleCount + 1) * Segs * 2 + (CrestCols - 1) * 2 + FleckCount * 2;

        // Pool size in host-local metres (× anchor.Scale). Every other radius is a fraction of the rim.
        const float RimBase = 0.52f;
        const float RimPerSigScale = 0.12f;
        /// <summary>Hard cap on the rim, inside MaxAnchorRadius at any SigScale.</summary>
        const float RimCap = 0.7f;
        const float SurfacePerSigScale = 0.03f;
        /// <summary>Surface height at the rim as a share of the middle: the shoreline dips toward the street.</summary>
        const float RimSag = 0.4f;
        const float SagStart = 0.6f;
        const float ShoreFadeStart = 0.58f;
        /// <summary>Waves flatten out toward the shore from here.</summary>
        const float CalmStart = 0.7f;
        /// <summary>Relative water alpha around the monster's legs.</summary>
        const float CentreAlpha = 0.7f;
        /// <summary>Water level (share of full) as the sweep first reaches a monster.</summary>
        const float RiseFloor = 0.3f;
        /// <summary>Foam, ripples and flecks ride this far above the surface (host-local m).</summary>
        const float FoamLift = 0.006f;
        /// <summary>Keeps every foam corner this far inside the rim.</summary>
        const float EdgeMargin = 0.03f;
        /// <summary>Anchors at or below this Scale are skipped, never inflated past their own size.</summary>
        const float MinScale = 0.001f;

        const float RippleStart = 0.14f;
        const float RippleEnd = 0.9f;
        const float RippleWidth0 = 0.05f;
        const float RippleWidth1 = 0.11f;

        const float LipRadius = 0.78f;
        const float LipBreath = 0.025f;
        const float LipWidth = 0.09f;
        const float LipLapSpeed = 1.3f;
        /// <summary>Foam break-up drifting round the waterline (noise segments per second).</summary>
        const float LipFlow = 1.1f;

        const float CrestRadius = 0.64f;
        const float CrestBreath = 0.03f;
        const float CrestWidth = 0.11f;
        /// <summary>Radians of foam trailing the crest head.</summary>
        const float CrestArc = 1.9f;
        const float CrestLift = 0.028f;

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

        /// <summary>One variant's water: depth, chop, motion and foam.</summary>
        struct Look
        {
            public float Surface;      // shin height at SigScale 1 (host-local m)
            public float Amplitude;    // wave height per unit SigScale (host-local m)
            public float Chop;         // 0 round swell … 1 peaked crests, flat troughs
            public float Alpha;
            public float CrestGain;
            public float TroughGain;
            public Wave A, B, C;       // A leads: white caps line up with its crests
            public int Ripples;
            public float RipplePeriod;
            public float RippleAlpha;
            public float Break;        // how broken ripples and foam are round the ring
            public float LipAlpha;
            public float CrestAlpha;   // 0 = no travelling crest
            public float CrestSpeed;
            public bool Glints;        // flecks twinkle as sun glints instead of white-cap streaks
            public float FleckLife;
            public float FleckHalfLength;
            public float FleckHalfWidth;
            public float FleckDrift;
            public float FleckAlpha;
            public float FleckMin;
            public float FleckMax;
        }

        static readonly Look Sea = new Look
        {
            Surface = 0.23f, Amplitude = 0.028f, Chop = 0.9f, Alpha = 0.5f, CrestGain = 0.75f, TroughGain = 0.5f,
            A = new Wave(32f, 0.44f, 4.2f, 0.5f),
            B = new Wave(-48f, 0.34f, 5.1f, 0.3f),
            C = new Wave(105f, 0.66f, 2.9f, 0.2f),
            Ripples = 3, RipplePeriod = 1.5f, RippleAlpha = 0.4f, Break = 0.6f,
            LipAlpha = 0.3f, CrestAlpha = 0f, CrestSpeed = 0f,
            Glints = false, FleckLife = 1.2f, FleckHalfLength = 0.12f, FleckHalfWidth = 0.03f,
            FleckDrift = 0.12f, FleckAlpha = 0.75f, FleckMin = 0.22f, FleckMax = 0.8f
        };

        static readonly Look Tide = new Look
        {
            Surface = 0.2f, Amplitude = 0.016f, Chop = 0.15f, Alpha = 0.4f, CrestGain = 0.5f, TroughGain = 0.2f,
            A = new Wave(80f, 0.72f, 2.0f, 0.6f),
            B = new Wave(20f, 0.5f, 2.5f, 0.25f),
            C = new Wave(150f, 0.4f, 2.9f, 0.15f),
            Ripples = 2, RipplePeriod = 2.6f, RippleAlpha = 0.5f, Break = 0.15f,
            LipAlpha = 0.45f, CrestAlpha = 0.8f, CrestSpeed = 0.75f,
            Glints = true, FleckLife = 0.7f, FleckHalfLength = 0.045f, FleckHalfWidth = 0.045f,
            FleckDrift = 0f, FleckAlpha = 0.9f, FleckMin = 0.2f, FleckMax = 0.75f
        };

        Look _look;
        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        /// <summary>Slots written last frame; slots past the live count are cleared once.</summary>
        int _lit;

        // Kit-wide shape (fractions of anchor.Scale).
        float _rim;
        float _surface;
        float _amplitude;
        /// <summary>Second-harmonic share in <see cref="Swell"/>.</summary>
        float _chop;
        float _fleckReach;

        // Unit-rim disc template, one entry per disc vertex.
        float[] _vx;
        float[] _vz;
        float[] _vShade;
        float[] _vAlpha;
        float[] _vSag;
        float[] _vCalm;
        /// <summary>WaveCount × DiscVerts: each wave's phase across the pool (anchor-independent).</summary>
        float[] _proj;
        /// <summary>Current anchor's surface height per disc vertex; foam reads it so it rides the drawn surface.</summary>
        float[] _hy;
        float[] _cos;
        float[] _sin;
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
        float _crestPh;
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
        Vector3 _o;
        float _s;
        float _r;
        float _lift;

        protected override void Build()
        {
            var tide = Variant == 1;
            _look = tide ? Tide : Sea;
            var sig = SigScale;
            _rim = Mathf.Min(RimCap, RimBase + RimPerSigScale * sig);
            _surface = _look.Surface + SurfacePerSigScale * (sig - 1f);
            _amplitude = _look.Amplitude * sig;
            _chop = 0.5f * Mathf.Clamp01(_look.Chop);

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

            Advance(dt > 0f ? dt : 0f);
            level = Mathf.Min(level, 1f);
            var bounds = new Bounds(street.Center, street.Half * 2f);
            var drew = false;
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                if (WriteSlot(i, a, level))
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
            _crestPh = Mathf.Repeat(_crestPh + _look.CrestSpeed * dt, TwoPi);
            _fleckClock = Mathf.Repeat(_fleckClock + dt, _look.FleckLife * FleckWrapCycles);
        }

        /// <summary>Writes one pool; false when the anchor is not visible yet (slot gets cleared).</summary>
        bool WriteSlot(int slot, in FieldAnchor a, float level)
        {
            var fade = Mathf.Clamp01(a.Presence) * level;
            if (fade <= 0.001f || a.Scale <= MinScale) return false;

            _o = a.Position;
            _s = a.Scale;
            _r = _rim * _s;
            _lift = FoamLift * _s;
            var rise = Mathf.Lerp(RiseFloor, 1f, Smooth(0f, 1f, fade));
            // Continuous in position, so a monster keeps its rhythm when the anchor list reorders.
            var offset = Mathf.Repeat((_o.x * 0.61f + _o.z * 0.37f) / _s, 1f);
            var b = slot * SlotVerts;

            WriteDisc(b, fade, rise, a.Aura < 0);
            WriteRipples(b, fade, offset, a.Aura > 0 ? BoonRippleGain : 1f);
            WriteLip(b, fade, offset, a.Aura);
            if (_look.CrestAlpha > 0f) WriteCrest(b, fade, rise, offset, a.Aura > 0);
            WriteFlecks(b, slot, fade);
            return true;
        }

        void WriteDisc(int b, float fade, float rise, bool bane)
        {
            var surface = _surface * _s * rise;
            var amp = _amplitude * _s * rise;
            for (var j = 0; j < WaveCount; j++)
                _base[j] = _wK[j] * (_o.x * _wDirX[j] + _o.z * _wDirZ[j]) / _s + _ph[j];

            for (var v = 0; v < DiscVerts; v++)
            {
                var w = 0f;
                for (var j = 0; j < WaveCount; j++)
                    w += _wWeight[j] * Swell(_proj[j * DiscVerts + v] + _base[j]);

                var y = Mathf.Max(_lift, surface * _vSag[v] + amp * _vCalm[v] * w);
                _hy[v] = y;
                _verts[b + v] = new Vector3(_o.x + _r * _vx[v], _o.y + y, _o.z + _r * _vz[v]);

                var hi = Mathf.Clamp01((w - 0.3f) / 0.7f) * _vCalm[v];
                var c = Color.Lerp(_deep, _shallow, _vShade[v]);
                c = Color.Lerp(c, _trough, Mathf.Clamp01(-w) * _look.TroughGain);
                c = Color.Lerp(c, _crest, hi * _look.CrestGain);
                if (bane) c = Color.Lerp(c, _murk, BaneMurk);
                _cols[b + v] = WithAlpha(c, _look.Alpha * _vAlpha[v] * (1f + 0.35f * hi) * fade);
            }
        }

        void WriteRipples(int b, float fade, float offset, float gain)
        {
            var n = Mathf.Clamp(_look.Ripples, 0, RippleCount);
            for (var k = 0; k < n; k++)
            {
                var t = Mathf.Repeat(_ripplePh + (k + offset) / n, 1f);
                var grow = 1f - (1f - t) * (1f - t);
                var half = 0.5f * Mathf.Lerp(RippleWidth0, RippleWidth1, t);
                var mid = Mathf.Min(Mathf.Lerp(RippleStart, RippleEnd, grow), 1f - EdgeMargin - half);
                var alpha = _look.RippleAlpha * gain * Mathf.Sin(Mathf.PI * t) * (1f - 0.4f * t) * fade;
                WriteRing(b + RippleBase + k * StripVerts, mid - half, mid + half, _ripple, alpha,
                    _look.Break, 7.3f * k + offset * Segs);
            }
        }

        void WriteLip(int b, float fade, float offset, int aura)
        {
            var mid = LipRadius + LipBreath * Mathf.Sin(_lipPh + offset * TwoPi);
            var half = 0.5f * LipWidth;
            var alpha = _look.LipAlpha * fade;
            var col = _foam;
            var breakUp = _look.Break;
            if (aura > 0)
            {
                alpha *= BoonLipGain * (0.85f + 0.15f * Mathf.Sin(2f * _lipPh));
                col = _foamBoon;
                breakUp *= BoonLipBreak;
            }
            else if (aura < 0)
            {
                alpha *= BaneLipGain;
            }

            WriteRing(b + LipBase, mid - half, mid + half, col, alpha, breakUp, offset * Segs + _lipDrift);
        }

        /// <summary>
        /// Closed soft band between two rim fractions, riding the surface. The
        /// inner row samples the dot's bottom edge and the outer row its top, so
        /// both edges fade.
        /// </summary>
        void WriteRing(int i, float inner, float outer, Color col, float alpha, float breakUp, float noiseAt)
        {
            for (var s = 0; s < Segs; s++)
            {
                var cs = _cos[s];
                var sn = _sin[s];
                _verts[i + s] = new Vector3(_o.x + _r * inner * cs, _o.y + SpokeHeight(inner, s) + _lift,
                    _o.z + _r * inner * sn);
                _verts[i + Segs + s] = new Vector3(_o.x + _r * outer * cs, _o.y + SpokeHeight(outer, s) + _lift,
                    _o.z + _r * outer * sn);
                Color32 c = WithAlpha(col, alpha * (1f - breakUp * Noise(s + noiseAt)));
                _cols[i + s] = c;
                _cols[i + Segs + s] = c;
            }
        }

        /// <summary>White surf running round the pool: brightest just behind its head, lifted as it curls.</summary>
        void WriteCrest(int b, float fade, float rise, float offset, bool boon)
        {
            var i = b + CrestBase;
            var head = _crestPh + offset * TwoPi;
            var alpha = _look.CrestAlpha * fade * (boon ? BoonCrestGain : 1f);
            for (var c = 0; c < CrestCols; c++)
            {
                var taper = _crestTaper[c];
                var ang = head - c * (CrestArc / (CrestCols - 1));
                var mid = CrestRadius + CrestBreath * Mathf.Sin(2f * ang + _lipPh);
                var half = 0.5f * CrestWidth * (0.35f + 0.65f * taper);
                var cs = Mathf.Cos(ang);
                var sn = Mathf.Sin(ang);
                var y = _o.y + SurfaceAt(mid, ang) + _lift + CrestLift * _s * rise * taper;
                _verts[i + c] = new Vector3(_o.x + _r * (mid - half) * cs, y, _o.z + _r * (mid - half) * sn);
                _verts[i + CrestCols + c] = new Vector3(_o.x + _r * (mid + half) * cs, y, _o.z + _r * (mid + half) * sn);
                Color32 col = WithAlpha(_crest, alpha * taper);
                _cols[i + c] = col;
                _cols[i + CrestCols + c] = col;
            }
        }

        /// <summary>
        /// Short-lived flat flecks on the surface: white-cap streaks along the
        /// lead wave's crest lines (sea) or twinkling sun glints (tide). Each
        /// cycle re-seeds from a hash, so there is no per-fleck state.
        /// </summary>
        void WriteFlecks(int b, int slot, float fade)
        {
            var life = _look.FleckLife;
            var stagger = Hash01(slot, 53);
            var lx = _capAlong.x * _look.FleckHalfLength;
            var lz = _capAlong.y * _look.FleckHalfLength;
            var wx = -_capAlong.y * _look.FleckHalfWidth;
            var wz = _capAlong.x * _look.FleckHalfWidth;
            var salt = slot * 131;
            for (var f = 0; f < FleckCount; f++)
            {
                var cycle = _fleckClock / life + (f + stagger) / FleckCount;
                var n = Mathf.FloorToInt(cycle);
                var u = cycle - n;
                var key = n * FleckCount + f;
                var rad = Mathf.Lerp(_look.FleckMin, _look.FleckMax, Mathf.Sqrt(Hash01(key, 17 + salt)));
                var ang = Hash01(key, 71 + salt) * TwoPi;
                var px = rad * Mathf.Cos(ang) + _capDrift.x * u;
                var pz = rad * Mathf.Sin(ang) + _capDrift.y * u;
                var pr = Mathf.Sqrt(px * px + pz * pz);
                if (pr > _fleckReach)
                {
                    px *= _fleckReach / pr;
                    pz *= _fleckReach / pr;
                    pr = _fleckReach;
                }

                var y = _o.y + SurfaceAt(pr, Mathf.Atan2(pz, px)) + _lift;
                var env = Mathf.Sin(Mathf.PI * u);
                if (_look.Glints) env *= env * env;
                Color32 col = WithAlpha(_fleck, _look.FleckAlpha * env * fade);

                var i = b + FleckBase + f * 4;
                var cx = _o.x + _r * px;
                var cz = _o.z + _r * pz;
                _verts[i] = new Vector3(cx - _r * (lx + wx), y, cz - _r * (lz + wz));
                _verts[i + 1] = new Vector3(cx + _r * (lx - wx), y, cz + _r * (lz - wz));
                _verts[i + 2] = new Vector3(cx + _r * (lx + wx), y, cz + _r * (lz + wz));
                _verts[i + 3] = new Vector3(cx - _r * (lx - wx), y, cz - _r * (lz - wz));
                _cols[i] = col;
                _cols[i + 1] = col;
                _cols[i + 2] = col;
                _cols[i + 3] = col;
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

        /// <summary>Surface height along spoke <paramref name="s"/> at rim fraction <paramref name="rn"/>, as drawn.</summary>
        float SpokeHeight(float rn, int s)
        {
            var f = Mathf.Clamp01(rn) * DiscRings;
            var r = Mathf.Min((int)f, DiscRings - 1);
            var h0 = r == 0 ? _hy[0] : _hy[1 + (r - 1) * Segs + s];
            var h1 = _hy[1 + r * Segs + s];
            return h0 + (h1 - h0) * (f - r);
        }

        /// <summary>Surface height at rim fraction <paramref name="rn"/> and angle <paramref name="ang"/> (radians from +X toward +Z).</summary>
        float SurfaceAt(float rn, float ang)
        {
            var f = Mathf.Repeat(ang / TwoPi, 1f) * Segs;
            var s0 = (int)f;
            var t = f - s0;
            s0 %= Segs;
            var s1 = (s0 + 1) % Segs;
            return Mathf.Lerp(SpokeHeight(rn, s0), SpokeHeight(rn, s1), t);
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
            if (tide)
            {
                // Sunlit shallows: turquoise body, white surf, glints off the sky.
                _deep = Color.Lerp(ground, sky, 0.3f);
                _shallow = Color.Lerp(accent, Color.white, 0.35f);
                _trough = Color.Lerp(ground, Color.black, 0.15f);
                _crest = Color.Lerp(accent, Color.white, 0.85f);
                _ripple = Color.Lerp(accent, Color.white, 0.6f);
                _foam = Color.Lerp(accent, Color.white, 0.8f);
                _fleck = Color.Lerp(sky, Color.white, 0.92f);
            }
            else
            {
                // Open sea: navy depths, blue shallows, white caps.
                _deep = Color.Lerp(ground, Color.black, 0.3f);
                _shallow = Color.Lerp(ground, sky, 0.6f);
                _trough = Color.Lerp(ground, Color.black, 0.55f);
                _crest = Color.Lerp(accent, Color.white, 0.5f);
                _ripple = Color.Lerp(sky, Color.white, 0.45f);
                _foam = Color.Lerp(accent, Color.white, 0.7f);
                _fleck = Color.Lerp(accent, Color.white, 0.8f);
            }

            _foamBoon = Color.Lerp(_foam, Color.white, 0.6f);
            _murk = Color.Lerp(ground, Color.black, 0.7f);
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

            _vx = new float[DiscVerts];
            _vz = new float[DiscVerts];
            _vShade = new float[DiscVerts];
            _vAlpha = new float[DiscVerts];
            _vSag = new float[DiscVerts];
            _vCalm = new float[DiscVerts];
            _hy = new float[DiscVerts];
            for (var v = 0; v < DiscVerts; v++)
            {
                var rn = 0f;
                if (v > 0)
                {
                    var s = (v - 1) % Segs;
                    rn = ((v - 1) / Segs + 1) / (float)DiscRings;
                    _vx[v] = rn * _cos[s];
                    _vz[v] = rn * _sin[s];
                }

                _vShade[v] = Smooth(0.1f, 1f, rn);
                _vAlpha[v] = Mathf.Lerp(CentreAlpha, 1f, Smooth(0f, 0.35f, rn)) * (1f - Smooth(ShoreFadeStart, 1f, rn));
                _vSag[v] = Mathf.Lerp(1f, RimSag, Smooth(SagStart, 1f, rn));
                _vCalm[v] = 1f - Smooth(CalmStart, 1f, rn);
            }

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

            _proj = new float[WaveCount * DiscVerts];
            for (var j = 0; j < WaveCount; j++)
            for (var v = 0; v < DiscVerts; v++)
                _proj[j * DiscVerts + v] = _wK[j] * _rim * (_vx[v] * _wDirX[j] + _vz[v] * _wDirZ[j]);

            var raw = new float[Segs];
            for (var s = 0; s < Segs; s++)
                raw[s] = R(0f, 1f);
            _noise = new float[Segs];
            for (var s = 0; s < Segs; s++)
                _noise[s] = 0.5f * raw[s] + 0.25f * (raw[(s + Segs - 1) % Segs] + raw[(s + 1) % Segs]);

            _crestTaper = new float[CrestCols];
            for (var c = 0; c < CrestCols; c++)
                _crestTaper[c] = Mathf.Sin(Mathf.PI * Mathf.Pow(c / (float)(CrestCols - 1), 0.65f));

            _capAlong = new Vector2(-_wDirZ[0], _wDirX[0]);
            _capDrift = new Vector2(_wDirX[0], _wDirZ[0]) * _look.FleckDrift;
            var halfDiag = Mathf.Sqrt(_look.FleckHalfLength * _look.FleckHalfLength +
                                      _look.FleckHalfWidth * _look.FleckHalfWidth);
            _fleckReach = Mathf.Max(0f, 1f - EdgeMargin - halfDiag);

            _ripplePh = R(0f, 1f);
            _lipPh = R(0f, TwoPi);
            _crestPh = R(0f, TwoPi);
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
        /// Disc samples the dot's centre (flat); strips run u = 0.5 with v across
        /// the band (soft both edges); flecks take the whole dot.
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

                for (var c = 0; c < CrestCols; c++)
                {
                    uv[b + CrestBase + c] = low;
                    uv[b + CrestBase + CrestCols + c] = high;
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
        /// pool's water first, then ripples and waterlines, crests, flecks, so
        /// foam always lands on water.
        /// </summary>
        int[] BuildTriangles()
        {
            var tris = new int[MaxAnchors * SlotTris * 3];
            var t = 0;

            for (var slot = 0; slot < MaxAnchors; slot++)
            {
                var b = slot * SlotVerts;
                for (var s = 0; s < Segs; s++)
                {
                    var s1 = (s + 1) % Segs;
                    tris[t++] = b;
                    tris[t++] = b + 1 + s;
                    tris[t++] = b + 1 + s1;
                }

                for (var r = 0; r < DiscRings - 1; r++)
                {
                    var ring = b + 1 + r * Segs;
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
            {
                var i = slot * SlotVerts + CrestBase;
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
