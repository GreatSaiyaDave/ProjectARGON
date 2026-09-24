using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Rising Air Current's thermal. Thin wind streaks whip round each monster's
    /// feet and climb in a spiral, like the white cirrus streaks the hawk rides
    /// in the illustration. Each streak is a pair of speed lines, a bright line
    /// with a fainter companion just outside and below it, running up a vortex
    /// path: wide and almost flat round the feet, cinched at mid-height, opening
    /// again near the top. A streak has a near-white head, an accent body and a
    /// tail that thins into sky blue. It lifts off the ground, fades out before
    /// the height cap, rests a moment and respawns at the feet at a new angle.
    /// The player's vortices turn one way and the opponent's the other, so the
    /// two rows mirror across the aisle. Each monster's spin and streak timing
    /// come from its stable key, so a lunge, a hit punch or a relayout never
    /// twists it. The vortex rises with the sweep and sinks back on dissolve.
    /// <para>Boon (the wind lifts the monster): one extra streak, whiter and
    /// brighter, whipping round faster in more turns. Bane: a slack draught,
    /// low, slow and dim, drained toward the ground colour.</para>
    /// <para>Set monster: no vortex. Its flat card back (0.96 × 1.40 host-local,
    /// lying across the street) reaches 0.70 from the feet along its long axis,
    /// so no ribbon inside the per-monster radius could circle it without
    /// cutting its ends. Turning Set hides the vortex at once; a flip gathers
    /// it up from the feet.</para>
    /// <para>One look (variant 0). Any other variant draws the same vortex.
    /// SigScale sets the streak count (2 at 0.5, 3 at 1, 4 at 1.5), the vortex
    /// height and the line width.</para>
    /// Nothing reaches under the card: the path stays outside the ownership
    /// ring. The side of each vortex facing the stage camera is fainter so the
    /// monster reads through it. Palette mixes are done in sRGB and converted
    /// with VertexColor() once at build. One dynamic mesh (one draw call) of
    /// ribbons turned toward the camera; soft edges come from the shared soft
    /// dot's centre line. Scope: per-monster.
    /// </summary>
    public sealed class ArFieldSigUpdraft : ArFieldSignature
    {
        const int MaxAnchors = 10;
        const int MinStreaks = 2;
        const int MaxBaseStreaks = 4;
        /// <summary>Base streaks plus the boon's extra one.</summary>
        const int MaxStreaks = MaxBaseStreaks + 1;
        /// <summary>Main line and its fainter companion.</summary>
        const int Strands = 2;
        const int Rows = 16;
        const int StrandVerts = Rows * 2;
        const int StreakVerts = Strands * StrandVerts;
        /// <summary>
        /// 5 streaks × 2 strands × 32 = 320 per monster, × <see cref="MaxAnchors"/> = 3200 ≤
        /// <see cref="ArFieldSignature.MaxVertices"/>.
        /// </summary>
        const int SlotVerts = MaxStreaks * StreakVerts;

        const float TwoPi = Mathf.PI * 2f;
        /// <summary>Streak start angles step by this, so any streak count spreads evenly round the monster.</summary>
        const float GoldenAngle = 2.3999631f;

        /// <summary>Hard per-vertex guards (× Scale), just inside the contract.</summary>
        const float GuardRadius = MaxAnchorRadius * 0.97f;
        const float GuardHeight = MaxAnchorHeight * 0.97f;
        /// <summary>Per-vertex alpha cap before presence × level.</summary>
        const float MaxAlpha = 0.85f;
        /// <summary>Anchors at or below this Scale are skipped.</summary>
        const float MinScale = 1e-4f;

        // Vortex size in host-local metres (× anchor.Scale). Radii live in the shapes below.
        const float BaseHeight = 0.5f;
        const float HeightPerSigScale = 0.04f;
        /// <summary>Path starts above the terrain pad, clear of the widest ribbon's lower edge (0.048).</summary>
        const float Lift = 0.05f;
        /// <summary>Slope of the rise at the feet (1 = straight); it steepens toward the top.</summary>
        const float RiseAtFeet = 0.6f;
        const float HalfWidth = 0.032f;
        const float WidthPerSigScale = 0.2f;
        /// <summary>Share of full height as the sweep first reaches a monster.</summary>
        const float GrowFloor = 0.35f;

        // Companion line: just outside, dropping slightly below, a step behind.
        const float EchoOut = 0.035f;
        const float EchoDrop = 0.025f;
        const float EchoLag = 0.05f;
        const float EchoWidth = 0.55f;
        const float EchoAlpha = 0.5f;
        const float EchoSky = 0.35f;

        // Life of a streak along the path (u = 0 at the feet, 1 at the top).
        /// <summary>Share of a cycle the streak is travelling; for the rest it is hidden, then respawns at the feet.</summary>
        const float Busy = 0.86f;
        const float FadeInU = 0.12f;
        const float FadeOutFrom = 0.75f;
        /// <summary>Per-respawn jitter (full range): start angle in radians, span and radius as shares.</summary>
        const float AngleJitter = 1f;
        const float SpanJitter = 0.3f;
        const float RadiusJitter = 0.06f;

        // Camera side of each vortex: fainter, so the monster art reads through it.
        const float FrontCosFrom = 0.1f;
        const float FrontCosTo = 0.9f;
        const float FrontAlpha = 0.7f;

        /// <summary>Seconds a flipped monster's vortex takes to gather up from its feet. Turning Set hides it at once.</summary>
        const float FlipOpenSeconds = 0.6f;
        /// <summary>On an aura change the old streaks fade out over this, then the new shape's fade in over it.</summary>
        const float AuraFadeSeconds = 0.25f;
        /// <summary>Mixes the respawn cycle into a monster's key for per-cycle jitter.</summary>
        const int CyclePrime = 7919;

        // Colour roles: white head, accent body, sky tail.
        const float HeadWhite = 0.6f;
        const float BodyWhite = 0.2f;
        const float TailAccent = 0.4f;
        const float BoonHeadWhite = 0.85f;
        const float BoonBodyWhite = 0.4f;
        const float BoonTailAccent = 0.65f;
        const float BaneDrain = 0.5f;
        const float BaneDarken = 0.2f;

        /// <summary>How one monster's vortex turns. Radii are host-local metres on a quadratic curve foot → mid → top.</summary>
        struct Shape
        {
            public float RFoot;
            public float RMid;    // control radius: the curve's waist sits a little outside it
            public float RTop;
            public float Turns;   // full turns from the feet to the top
            public float Height;  // × the kit's vortex height
            public float Period;  // seconds per streak cycle
            public float Span;    // share of the path one streak covers
            public float Alpha;
            public float Width;
        }

        static readonly Shape Calm = new Shape
        {
            RFoot = 0.6f, RMid = 0.44f, RTop = 0.58f, Turns = 0.85f, Height = 1f,
            Period = 3.2f, Span = 0.3f, Alpha = 0.62f, Width = 1f
        };

        static readonly Shape Boon = new Shape
        {
            RFoot = 0.62f, RMid = 0.42f, RTop = 0.6f, Turns = 1.1f, Height = 1f,
            Period = 2.5f, Span = 0.34f, Alpha = 0.8f, Width = 1.2f
        };

        static readonly Shape Bane = new Shape
        {
            RFoot = 0.62f, RMid = 0.54f, RTop = 0.62f, Turns = 0.55f, Height = 0.55f,
            Period = 4.4f, Span = 0.26f, Alpha = 0.42f, Width = 0.85f
        };

        /// <summary>Indexed by Aura + 1.</summary>
        static readonly Shape[] Shapes = { Bane, Calm, Boon };

        /// <summary>One streak of the vortex template, shared by every monster.</summary>
        struct Streak
        {
            public float Angle;
            public float PeriodMul;
            public float Width;
        }

        /// <summary>Which monster an anchor index holds and how its vortex is easing through a flip or an aura change.</summary>
        struct AnchorState
        {
            public int Key;
            /// <summary>0 = hidden (Set), 1 = full vortex.</summary>
            public float Open;
            /// <summary>Aura being drawn (−1, 0, +1); lags a change until the old streaks have faded out.</summary>
            public int Aura;
            /// <summary>0 = fully shown, 1 = faded out for an aura change.</summary>
            public float Dim;
        }

        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        Streak[] _streaks;
        int _count;
        /// <summary>Slots written last frame; slots past the drawn count are collapsed once.</summary>
        int _slotsLive;
        /// <summary>Vortex height and line half width at this SigScale (host-local m).</summary>
        float _height;
        float _halfWidth;

        /// <summary>
        /// Streak phase 0…1 and completed cycles, per shape (index = (Aura + 1) × MaxStreaks + streak),
        /// so each shape keeps its own speed. A monster adds a phase shift from its key on top; one whose
        /// aura changes fades out and back in on the new shape's streaks (<see cref="Track"/>).
        /// </summary>
        float[] _phase;
        int[] _cycle;

        /// <summary>Per anchor index; re-keyed when a different monster takes the index.</summary>
        AnchorState[] _state;

        // Per-row tables along a strand (index = row, tail → head).
        float[] _rowS;
        float[] _alongAlpha;
        float[] _alongWidth;
        float[] _mixBody;
        float[] _mixHead;

        // Colours per shape (index = Aura + 1).
        Color[] _head;
        Color[] _body;
        Color[] _tail;
        Color[] _echoHead;
        Color[] _echoBody;
        Color[] _echoTail;

        // Anchor being written.
        Vector3 _o;
        float _s;
        Vector3 _view;
        float _camX;
        float _camZ;

        protected override void Build()
        {
            var sig = SigScale;
            _count = Mathf.Clamp(Mathf.FloorToInt(1.5f + 2f * sig), MinStreaks, MaxBaseStreaks);
            _height = BaseHeight + HeightPerSigScale * (sig - 1f);
            _halfWidth = HalfWidth * (1f + WidthPerSigScale * (sig - 1f));

            BuildColours();
            BuildTables();

            _verts = new Vector3[MaxAnchors * SlotVerts];
            _cols = new Color32[_verts.Length];
            var uvs = new Vector2[_verts.Length];
            // Every ribbon samples the soft dot's centre column: soft across, even along.
            for (var i = 0; i < uvs.Length; i += 2)
            {
                uvs[i] = new Vector2(0.5f, 0f);
                uvs[i + 1] = new Vector2(0.5f, 1f);
            }

            _mesh = NewDynamicMesh("FieldUpdraft");
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles();
            _mr = AddMeshChild("Updraft", _mesh, VertexColorMaterial("FieldUpdraftMat", GroundQueue));
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

            var bounds = new Bounds(street.Center, street.Half * 2f);
            var drawn = 0;
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                Track(i, a, step);
                var fade = Mathf.Clamp01(a.Presence) * level;
                // A Set monster (Open 0) takes no slot: nothing is drawn near its flat card.
                if (fade <= 0.001f || a.Scale <= MinScale || _state[i].Open <= 0f) continue;
                WriteAnchor(drawn * SlotVerts, a, _state[i], fade, eye);
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
                Collapse(slot * SlotVerts, SlotVerts, street.Center);
            _slotsLive = drawn;

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box (plus each vortex) keeps the renderer from being culled.
            _mesh.bounds = bounds;
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Advance(float dt)
        {
            for (var si = 0; si < Shapes.Length; si++)
            for (var i = 0; i < MaxStreaks; i++)
            {
                var k = si * MaxStreaks + i;
                var p = _phase[k] + dt / (Shapes[si].Period * _streaks[i].PeriodMul);
                if (p >= 1f)
                {
                    var whole = (int)p;
                    _cycle[k] += whole;
                    p -= whole;
                }

                _phase[k] = p;
            }
        }

        /// <summary>
        /// Eases the monster at anchor index <paramref name="i"/>. Turning Set hides
        /// its vortex at once (the flat card needs the room); a flip gathers it up
        /// over <see cref="FlipOpenSeconds"/>. An aura change on show fades the old
        /// streaks out and the new shape's in (each shape runs its own clock, so a
        /// cut would jump). A different monster at the index starts settled.
        /// </summary>
        void Track(int i, in FieldAnchor a, float dt)
        {
            ref var st = ref _state[i];
            var aura = a.Aura > 0 ? 1 : a.Aura < 0 ? -1 : 0;
            if (st.Key != a.Key)
            {
                st.Key = a.Key;
                st.Open = a.FaceDown ? 0f : 1f;
                st.Aura = aura;
                st.Dim = 0f;
                return;
            }

            if (a.FaceDown || st.Open <= 0f)
            {
                // Nothing on show: take the aura (always none while Set) without a fade.
                st.Aura = aura;
                st.Dim = 0f;
            }

            st.Open = a.FaceDown ? 0f : Mathf.MoveTowards(st.Open, 1f, dt / FlipOpenSeconds);
            if (st.Aura != aura)
            {
                st.Dim = Mathf.MoveTowards(st.Dim, 1f, dt / AuraFadeSeconds);
                if (st.Dim >= 1f) st.Aura = aura;
            }
            else
            {
                st.Dim = Mathf.MoveTowards(st.Dim, 0f, dt / AuraFadeSeconds);
            }
        }

        /// <summary>Stage camera in floor-local space; the player's end of the street without one.</summary>
        Vector3 EyeLocal(in FieldStreet street)
        {
            var cam = StageCamera;
            if (cam != null) return transform.InverseTransformPoint(cam.transform.position);
            return new Vector3(street.Center.x, street.Center.y, street.Center.z - street.Half.z - 1f);
        }

        /// <summary>
        /// One monster's vortex: its streaks, each a main line and a companion,
        /// grown and faded in by <c>st.Open</c> and dimmed by <c>st.Dim</c>.
        /// </summary>
        void WriteAnchor(int v, in FieldAnchor a, in AnchorState st, float fade, Vector3 eye)
        {
            _o = a.Position;
            _s = a.Scale;
            var toEye = eye - _o;
            var flat = Mathf.Sqrt(toEye.x * toEye.x + toEye.z * toEye.z);
            _camX = flat > 1e-4f ? toEye.x / flat : 0f;
            _camZ = flat > 1e-4f ? toEye.z / flat : -1f;
            _view = toEye.sqrMagnitude > 1e-8f ? toEye.normalized : Vector3.up;

            // From the monster's stable key: steady through lunges, hit punches, relayouts and list reorders.
            var spin = TwoPi * Hash01(a.Key, 71);
            var shift = Hash01(a.Key, 73);

            var si = st.Aura + 1;
            var sh = Shapes[si];
            var opened = Mathf.SmoothStep(0f, 1f, st.Open);
            var n = _count + (st.Aura > 0 ? 1 : 0);
            var grow = Mathf.Lerp(GrowFloor, 1f, Mathf.SmoothStep(0f, 1f, fade) * opened);
            var height = _height * sh.Height * grow;
            var turns = (a.PlayerSide ? 1f : -1f) * sh.Turns * TwoPi;
            var alpha = sh.Alpha * fade * opened * (1f - Mathf.SmoothStep(0f, 1f, st.Dim));

            for (var i = 0; i < MaxStreaks; i++)
            {
                var sv = v + i * StreakVerts;
                var k = si * MaxStreaks + i;
                var p = _phase[k] + shift;
                var wrap = (int)p;
                var q = p - wrap;
                // Unused slot, or resting between cycles (the respawn happens unseen here).
                if (i >= n || q >= Busy)
                {
                    Collapse(sv, StreakVerts, _o);
                    continue;
                }

                // Respawn jitter per monster and cycle; the cycle only turns over while the streak rests.
                var seed = unchecked((_cycle[k] + wrap) * CyclePrime + a.Key);
                var span = sh.Span * (1f + SpanJitter * (Hash01(seed, 101 + i) - 0.5f));
                var head = q / Busy * (1f + span);
                var theta = _streaks[i].Angle + spin + AngleJitter * (Hash01(seed, 131 + i) - 0.5f);
                var rMul = 1f + RadiusJitter * (Hash01(seed, 151 + i) - 0.5f);
                var w = _halfWidth * sh.Width * _streaks[i].Width;

                WriteStrand(sv, head, span, theta, turns, height, sh, rMul, 0f, 0f, w, alpha,
                    _head[si], _body[si], _tail[si]);
                WriteStrand(sv + StrandVerts, head - EchoLag, span, theta, turns, height, sh, rMul, EchoOut, EchoDrop,
                    w * EchoWidth, alpha * EchoAlpha, _echoHead[si], _echoBody[si], _echoTail[si]);
            }
        }

        /// <summary>
        /// One speed line covering path positions head − span … head. Rows past
        /// either end of the path fold onto it with zero alpha, so a streak rises
        /// out of the feet and thins away near the top.
        /// </summary>
        void WriteStrand(int v, float head, float span, float theta0, float turns, float height, in Shape sh,
            float rMul, float rOut, float drop, float halfW, float alpha, Color cHead, Color cBody, Color cTail)
        {
            var tail = head - span;
            if (head <= 0f || tail >= 1f)
            {
                Collapse(v, StrandVerts, _o);
                return;
            }

            var across = Vector3.up;
            var haveAcross = false;
            var climb = height - Lift;
            for (var j = 0; j < Rows; j++)
            {
                var u = tail + span * _rowS[j];
                var uc = Mathf.Clamp01(u);
                var m = 1f - uc;
                var ang = theta0 + turns * uc;
                var cs = Mathf.Cos(ang);
                var sn = Mathf.Sin(ang);

                // Quadratic radius foot → waist → top, and a rise that starts nearly flat.
                var r = ((sh.RFoot * m * m + 2f * sh.RMid * uc * m + sh.RTop * uc * uc) * rMul + rOut) * _s;
                var dr = 2f * ((sh.RMid - sh.RFoot) * m + (sh.RTop - sh.RMid) * uc) * rMul * _s;
                var y = (Lift + climb * uc * (RiseAtFeet + (1f - RiseAtFeet) * uc) - drop * uc) * _s;
                var dy = (climb * (RiseAtFeet + 2f * (1f - RiseAtFeet) * uc) - drop) * _s;

                var c = new Vector3(_o.x + cs * r, _o.y + y, _o.z + sn * r);
                var tangent = new Vector3(dr * cs - r * sn * turns, dy, dr * sn + r * cs * turns);

                // Ribbon across the view; near-parallel to it the previous side is kept so it never folds over.
                var cross = Vector3.Cross(tangent, _view);
                var cm = cross.magnitude;
                if (cm > 0.05f * tangent.magnitude && cm > 1e-7f)
                {
                    cross /= cm;
                    across = haveAcross && Vector3.Dot(cross, across) < 0f ? -cross : cross;
                    haveAcross = true;
                }

                var pathFade = Ramp(0f, FadeInU, u) * (1f - Ramp(FadeOutFrom, 1f, u));
                var front = Ramp(FrontCosFrom, FrontCosTo, cs * _camX + sn * _camZ);
                var a = Mathf.Min(MaxAlpha, alpha * _alongAlpha[j]) * pathFade * Mathf.Lerp(1f, FrontAlpha, front);
                Color32 col = WithAlpha(Color.Lerp(Color.Lerp(cTail, cBody, _mixBody[j]), cHead, _mixHead[j]), a);

                var e = across * (halfW * _alongWidth[j] * _s);
                var o = v + j * 2;
                _verts[o] = Contain(c - e);
                _verts[o + 1] = Contain(c + e);
                _cols[o] = col;
                _cols[o + 1] = col;
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

        /// <summary>Zero-area, fully transparent vertices for unused slots and resting streaks.</summary>
        void Collapse(int v, int n, Vector3 at)
        {
            for (var i = v; i < v + n; i++)
            {
                _verts[i] = at;
                _cols[i] = default;
            }
        }

        static float Ramp(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        // ── Build ───────────────────────────────────────────────────────────

        void BuildColours()
        {
            var accent = Env.Accent;
            var sky = Env.Sky;
            _head = new Color[3];
            _body = new Color[3];
            _tail = new Color[3];
            _echoHead = new Color[3];
            _echoBody = new Color[3];
            _echoTail = new Color[3];

            _head[1] = Color.Lerp(accent, Color.white, HeadWhite);
            _body[1] = Color.Lerp(accent, Color.white, BodyWhite);
            _tail[1] = Color.Lerp(sky, accent, TailAccent);

            _head[2] = Color.Lerp(accent, Color.white, BoonHeadWhite);
            _body[2] = Color.Lerp(accent, Color.white, BoonBodyWhite);
            _tail[2] = Color.Lerp(sky, accent, BoonTailAccent);

            // Bane: the calm colours drained toward the ground and dimmed.
            _head[0] = Drain(_head[1]);
            _body[0] = Drain(_body[1]);
            _tail[0] = Drain(_tail[1]);

            for (var i = 0; i < 3; i++)
            {
                _echoHead[i] = Color.Lerp(_head[i], sky, EchoSky);
                _echoBody[i] = Color.Lerp(_body[i], sky, EchoSky);
                _echoTail[i] = Color.Lerp(_tail[i], sky, EchoSky);
            }

            // Every mix above is sRGB; convert once for the Linear project's vertex colours.
            for (var i = 0; i < 3; i++)
            {
                _head[i] = VertexColor(_head[i]);
                _body[i] = VertexColor(_body[i]);
                _tail[i] = VertexColor(_tail[i]);
                _echoHead[i] = VertexColor(_echoHead[i]);
                _echoBody[i] = VertexColor(_echoBody[i]);
                _echoTail[i] = VertexColor(_echoTail[i]);
            }
        }

        Color Drain(Color c) => Color.Lerp(Color.Lerp(c, Env.Ground, BaneDrain), Color.black, BaneDarken);

        void BuildTables()
        {
            _streaks = new Streak[MaxStreaks];
            _phase = new float[Shapes.Length * MaxStreaks];
            _cycle = new int[_phase.Length];
            _state = new AnchorState[MaxAnchors];
            for (var i = 0; i < MaxStreaks; i++)
            {
                _streaks[i] = new Streak
                {
                    Angle = i * GoldenAngle,
                    PeriodMul = Mathf.Lerp(0.88f, 1.12f, Hash01(i, 11)),
                    Width = Mathf.Lerp(0.85f, 1.15f, Hash01(i, 13))
                };

                // Stagger the streaks so they leave the feet one after another, not in a volley.
                for (var si = 0; si < Shapes.Length; si++)
                    _phase[si * MaxStreaks + i] = Mathf.Repeat(i * 0.618034f + si * 0.21f + R(0f, 0.1f), 1f);
            }

            _rowS = new float[Rows];
            _alongAlpha = new float[Rows];
            _alongWidth = new float[Rows];
            _mixBody = new float[Rows];
            _mixHead = new float[Rows];
            for (var j = 0; j < Rows; j++)
            {
                var s = j / (float)(Rows - 1);
                _rowS[j] = s;
                // Comet: long faint tail, brightest just behind the head, pointed tip.
                _alongAlpha[j] = Ramp(0f, 0.7f, s) * (1f - Ramp(0.9f, 1f, s));
                _alongWidth[j] = 0.25f + 0.75f * Mathf.Sin(Mathf.PI * Mathf.Pow(s, 1.3f));
                _mixBody[j] = Ramp(0f, 0.55f, s);
                _mixHead[j] = Ramp(0.6f, 0.95f, s);
            }
        }

        static int[] BuildTriangles()
        {
            var strands = MaxAnchors * MaxStreaks * Strands;
            var tris = new int[strands * (Rows - 1) * 6];
            var ti = 0;
            for (var k = 0; k < strands; k++)
            {
                var b = k * StrandVerts;
                for (var j = 0; j < Rows - 1; j++)
                {
                    var i0 = b + j * 2;
                    var i1 = i0 + 1;
                    var i2 = i0 + 2;
                    var i3 = i0 + 3;
                    tris[ti++] = i0;
                    tris[ti++] = i2;
                    tris[ti++] = i1;
                    tris[ti++] = i1;
                    tris[ti++] = i2;
                    tris[ti++] = i3;
                }
            }

            return tris;
        }
    }
}
