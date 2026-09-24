using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Rising Air Current's thermal. Thin wind streaks whip round each monster's
    /// feet and climb in a spiral, like the white cirrus streaks the hawk rides
    /// in the illustration. Each streak is a pair of speed lines: a bright line
    /// with a fainter companion just outside and below it. They run up a vortex
    /// that leaves the feet almost flat and steepens toward the top. A streak has
    /// a near-white head, an accent body and a tail that thins into sky blue. It
    /// lifts off the ground, fades out before the height cap, rests a moment and
    /// respawns at the feet at a new angle. The player's vortices turn one way
    /// and the opponent's the other, so the two rows mirror across the aisle.
    /// The vortex rises with the sweep and sinks back on dissolve.
    /// <para>Calm: about 0.54 × Scale out at the feet and top, drawn in to about
    /// 0.50 at mid-height. Boon (the wind lifts the monster): one extra streak,
    /// whiter, brighter and a little bolder, whipping round faster in more turns
    /// on a near-straight column about 0.54 out. Its centre line stays at least
    /// 0.08 × Scale outside the 0.45 aura column, so it reads as wind round the
    /// column, not as part of it. Bane: a slack draught, low, slow and dim,
    /// drained toward the ground colour. An aura change fades the old streaks out
    /// and the new shape's in.</para>
    /// <para>Room: sized by the monster's resting scale, so it does not swell on
    /// a hit. Every visible ribbon edge stays outside the 0.45 ownership ring and
    /// within <see cref="ArFieldSignature.LaneHalfWidth"/> sideways (at most 0.593
    /// with SigScale 1.5). On the design row (0.7 × Scale from the midline) it
    /// also stays <see cref="ArFieldSignature.AisleClear"/> short of the midline.
    /// A guard pulls in any vertex that would cross either line
    /// (<see cref="ArFieldSignature.MidlineGap"/>), and a streak fades out at the
    /// aisle line. A monster standing much nearer the midline than its row fades
    /// its whole vortex.</para>
    /// <para>Cards: every vertex is clamped with
    /// <see cref="ArFieldSignature.CoverLimitAll"/>: its own monster's card and
    /// every other card within that function's reach, in either row (a Set
    /// card's footprint, or the camera side of face-up art). Streaks ease down,
    /// or fade out where no ribbon fits, before they reach a limit, so the clamp
    /// only moves hidden vertices. In front of upright art the vortex is
    /// squeezed into the art's lower ~22 % (0.30 × Scale), and the full climb
    /// happens behind its own opaque art. A player's vortex also stands in front
    /// of the monsters facing it across the aisle (straight across and
    /// diagonally), so upright art there squeezes its far side too; with its
    /// own art upright as well, the whole vortex stays a low spiral round the
    /// feet.
    /// In front of sideways Defense art (its own, or a neighbour's in either
    /// row) the limit is 0.10 × Scale, too low for most streaks, so they fade
    /// out there. Beside sideways art, or with no stage camera, streaks climb to
    /// full height. A card's cover height drops at once and rises over 0.6 s (a
    /// flip standing up, Defense back to Attack). Streaks fade out before they
    /// reach a neighbour's Set card, and after that card stands up its fade
    /// clears over 0.6 s.</para>
    /// <para>Set monster, or a flip still animating: no vortex. The card lies
    /// almost flat across the street (footprint 1.40 × 0.91 host-local,
    /// <see cref="ArFieldSignature.InSetCard"/>). That is wider than the 1.2
    /// column pitch, so no ring of streaks could circle it without crossing the
    /// card. Turning Set hides the vortex at once. Once the card has stood up
    /// after its flip, the vortex gathers up from the feet over 0.6 s.</para>
    /// <para>Each monster's spin, streak timing and ease state (flip, aura fade,
    /// cover) are keyed by its <see cref="FieldAnchor.Key"/>. It keeps its look,
    /// and an ease in progress carries on, when it moves or other monsters come
    /// and go. A monster not seen last frame starts settled; its entrance is the
    /// pad's presence ramp.</para>
    /// <para>One look (variant 0). Any other variant draws the same vortex.
    /// SigScale sets the streak count (2 at 0.5, 3 at 1, 4 at 1.5), the vortex
    /// height and the line width.</para>
    /// The camera side of each vortex is also fainter, so the monster reads
    /// through it. Palette mixes are done in sRGB and converted with
    /// VertexColor() once at build. One dynamic mesh (one draw call) of ribbons
    /// turned toward the camera; soft edges come from the shared soft dot's
    /// centre line. Scope: per-monster.
    /// </summary>
    public sealed class ArFieldSigUpdraft : ArFieldSignature
    {
        /// <summary>Vortices drawn (mesh slots).</summary>
        const int MaxAnchors = 10;
        /// <summary>
        /// Monsters tracked per frame (eases, cards near a vortex): more than the slots, so
        /// every card <see cref="ArFieldSignature.CoverLimitAll"/> can bind also softens the streaks.
        /// </summary>
        const int MaxTracked = 16;
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
        const float MaxAlpha = 0.65f;
        /// <summary>Anchors at or below this Scale are skipped.</summary>
        const float MinScale = 1e-4f;

        // Vortex size in host-local metres (× anchor.Scale). Radii live in the shapes below.
        const float BaseHeight = 0.5f;
        const float HeightPerSigScale = 0.04f;
        /// <summary>Path starts above the terrain pad, clear of the widest ribbon's lower edge (0.041).</summary>
        const float Lift = 0.05f;
        /// <summary>Slope of the rise at the feet (1 = straight); it steepens toward the top.</summary>
        const float RiseAtFeet = 0.6f;
        const float HalfWidth = 0.032f;
        const float WidthPerSigScale = 0.2f;
        /// <summary>Share of full height as the sweep first reaches a monster.</summary>
        const float GrowFloor = 0.35f;

        // Companion line: just outside, dropping slightly below, a step behind.
        const float EchoOut = 0.025f;
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
        const float RadiusJitter = 0.02f;

        // Camera side of each vortex: fainter, so the monster art reads through it (on top of CoverLimit).
        const float FrontCosFrom = 0.1f;
        const float FrontCosTo = 0.9f;
        const float FrontAlpha = 0.7f;

        // Room (× Scale).
        /// <summary>A vertex fades out over this last stretch before the aisle line (MidlineGap 0).</summary>
        const float MidlineFade = 0.01f;
        /// <summary>
        /// Room left to the aisle line at the anchor itself (MidlineGap / Scale) over which
        /// a monster standing nearer the midline than its row fades its whole vortex. The
        /// design row leaves 0.6.
        /// </summary>
        const float AisleFadeFrom = 0.5f;
        const float AisleFadeTo = 0.25f;

        // Cards (× Scale).
        /// <summary>A streak eases its climb down over this band before a card zone with room for it (upright art).</summary>
        const float SqueezeBand = 0.15f;
        /// <summary>
        /// Before a zone with no room for a ribbon (a Set card, sideways art) a streak fades out
        /// over <see cref="FadeBand"/> first, then drops over <see cref="DiveBand"/> unseen.
        /// </summary>
        const float FadeBand = 0.1f;
        const float DiveBand = 0.02f;
        /// <summary>Squeezed below this share of its climb a streak starts to fade; gone at <see cref="SqueezeHide"/>.</summary>
        const float SqueezeShow = 0.3f;
        const float SqueezeHide = 0.12f;
        /// <summary><see cref="ArFieldSignature.InSetCard"/>'s own margin (host-local).</summary>
        const float SetMargin = 0.03f;
        /// <summary>
        /// A neighbour's art zone eases in over this many times the usual band at its reach edge.
        /// That edge moves with the card, so it sweeps across a vortex at lunge speed.
        /// </summary>
        const float ReachSoft = 2f;

        /// <summary>
        /// Seconds a vortex takes to gather up from the feet once a flipped card stands up
        /// (a flip counts as Set until then), and to grow into a higher cover height.
        /// Turning Set hides the vortex at once.
        /// </summary>
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

        /// <summary>
        /// How one monster's vortex turns. Radii are host-local metres on a quadratic curve
        /// foot → mid → top. Sized so the widest ribbon edge (radius jitter, companion line and
        /// SigScale 1.5 included) stays inside 0.6 and outside the 0.45 ownership ring.
        /// </summary>
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

        /// <summary>Waist about 0.504 at mid-height; no aura column to clear.</summary>
        static readonly Shape Calm = new Shape
        {
            RFoot = 0.54f, RMid = 0.47f, RTop = 0.535f, Turns = 0.85f, Height = 1f,
            Period = 3.2f, Span = 0.3f, Alpha = 0.5f, Width = 1f
        };

        /// <summary>Waist about 0.538: ≥ 0.532 after radius jitter, ≥ 0.08 outside the 0.45 aura column.</summary>
        static readonly Shape Boon = new Shape
        {
            RFoot = 0.54f, RMid = 0.535f, RTop = 0.54f, Turns = 1.1f, Height = 1f,
            Period = 2.5f, Span = 0.34f, Alpha = 0.62f, Width = 1.05f
        };

        /// <summary>Waist about 0.53, 0.29 tall at SigScale 1.5: inside the bane column's height, clear of its wall.</summary>
        static readonly Shape Bane = new Shape
        {
            RFoot = 0.54f, RMid = 0.52f, RTop = 0.54f, Turns = 0.55f, Height = 0.55f,
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

        /// <summary>One monster's eases, found again next frame by its <see cref="FieldAnchor.Key"/>.</summary>
        struct AnchorState
        {
            public int Key;
            /// <summary>0 = hidden (Set), 1 = full vortex.</summary>
            public float Open;
            /// <summary>Aura being drawn (−1, 0, +1); lags a change until the old streaks have faded out.</summary>
            public int Aura;
            /// <summary>0 = fully shown, 1 = faded out for an aura change.</summary>
            public float Dim;
            /// <summary>Front cover height in use (floor-local): follows a lower one at once, a higher one over time.</summary>
            public float Cover;
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

        /// <summary>This frame's anchors (copied: the caller's list is only read) and their states, by index.</summary>
        FieldAnchor[] _anchors;
        /// <summary>
        /// The caller's list, for <see cref="ArFieldSignature.CoverLimitAll"/> while the vortices are
        /// written; cleared before Tick returns (the caller reuses it).
        /// </summary>
        IReadOnlyList<FieldAnchor> _anchorList;
        AnchorState[] _state;
        /// <summary>Last frame's states, looked up by Key; swapped with <see cref="_state"/> each Tick.</summary>
        AnchorState[] _prevState;
        int _prevCount;
        int _anchorCount;
        /// <summary>Horizontal unit vector from each anchor toward the camera, as CoverLimit measures it.</summary>
        float[] _fwdX;
        float[] _fwdZ;
        bool[] _hasFwd;
        /// <summary>Other anchors whose cards the monster being written can reach.</summary>
        int[] _near;
        int _nearCount;
        FieldStreet _street;

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
        int _self;
        FieldAnchor _a;
        Vector3 _o;
        float _s;
        float _side;
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
            var count = anchors == null ? 0 : Mathf.Min(anchors.Count, MaxTracked);
            if (level <= 0.001f || count == 0)
            {
                // Nothing tracked while hidden: monsters met again start settled.
                _prevCount = 0;
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            var step = dt > 0f ? dt : 0f;
            Advance(step);
            level = Mathf.Min(level, 1f);
            _street = street;
            var eye = street.HasCamera
                ? street.Camera
                : new Vector3(street.Center.x, street.Center.y, street.Center.z - street.Half.z - 1f);

            _anchorCount = count;
            for (var i = 0; i < count; i++)
            {
                _anchors[i] = anchors[i];
                Track(i, step);
                FaceCamera(i);
            }

            var bounds = new Bounds(street.Center, street.Half * 2f);
            var drawn = 0;
            _anchorList = anchors;
            for (var i = 0; i < count && drawn < MaxAnchors; i++)
            {
                var a = _anchors[i];
                var fade = Mathf.Clamp01(a.Presence) * level;
                // A Set monster (Open 0) takes no slot: nothing is drawn near its flat card.
                if (fade <= 0.001f || a.Scale <= MinScale || a.FaceDown || _state[i].Open <= 0f) continue;
                // Nearer the midline than its row: the whole vortex fades before it can reach the aisle.
                fade *= Ramp(AisleFadeTo, AisleFadeFrom, MidlineGap(a, a.Position) / a.Scale);
                if (fade <= 0.001f) continue;
                FindNeighbours(i);
                WriteAnchor(drawn * SlotVerts, i, fade, eye);
                drawn++;
                var s = a.Scale;
                bounds.Encapsulate(new Bounds(
                    a.Position + new Vector3(0f, MaxAnchorHeight * 0.5f * s, 0f),
                    new Vector3(MaxAnchorRadius * 2f * s, MaxAnchorHeight * s, MaxAnchorRadius * 2f * s)));
            }

            _anchorList = null;
            // This frame's states are next frame's lookup table.
            var swap = _prevState;
            _prevState = _state;
            _state = swap;
            _prevCount = count;

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
        /// Eases the monster at anchor index <paramref name="i"/>, carrying on from its
        /// state last frame (found by Key, so a monster keeps its ease when others leave).
        /// Turning Set hides its vortex at once (the flat card needs the room); once the
        /// card stands up after a flip it gathers up over <see cref="FlipOpenSeconds"/>.
        /// An aura change on show fades the old streaks out and the new shape's in (each
        /// shape runs its own clock, so a cut would jump). A monster not seen last frame
        /// starts settled.
        /// </summary>
        void Track(int i, float dt)
        {
            var a = _anchors[i];
            var aura = a.Aura > 0 ? 1 : a.Aura < 0 ? -1 : 0;
            var cover = a.FaceDown ? 0f : a.FrontCoverHeight;
            var found = false;
            var st = default(AnchorState);
            for (var k = 0; k < _prevCount; k++)
            {
                if (_prevState[k].Key != a.Key) continue;
                st = _prevState[k];
                found = true;
                break;
            }

            if (!found)
            {
                st.Key = a.Key;
                st.Open = a.FaceDown ? 0f : 1f;
                st.Aura = aura;
                st.Dim = 0f;
                st.Cover = cover;
                _state[i] = st;
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

            // A lower cover applies at once; a higher one (standing up, Defense → Attack) is grown into.
            var rate = a.Scale * UprightCoverHeight / FlipOpenSeconds;
            st.Cover = Mathf.Min(cover, Mathf.MoveTowards(st.Cover, cover, rate * dt));
            _state[i] = st;
        }

        /// <summary>The camera direction <see cref="ArFieldSignature.CoverLimit"/> uses for anchor <paramref name="i"/>.</summary>
        void FaceCamera(int i)
        {
            var p = _anchors[i].Position;
            var tx = _street.Camera.x - p.x;
            var tz = _street.Camera.z - p.z;
            var len = Mathf.Sqrt(tx * tx + tz * tz);
            _hasFwd[i] = _street.HasCamera && len >= 1e-4f;
            _fwdX[i] = _hasFwd[i] ? tx / len : 0f;
            _fwdZ[i] = _hasFwd[i] ? tz / len : 0f;
        }

        /// <summary>
        /// Other anchors whose cards the vortex at index <paramref name="i"/> can reach, in
        /// either row: a Set card (or one still standing up) or face-up art whose
        /// <see cref="CardReach"/> comes within the vortex's radius plus its squeeze and fade
        /// bands (<see cref="ReachSoft"/> times wider at the reach edge).
        /// <see cref="ArFieldSignature.CoverLimitAll"/> counts no card beyond that reach.
        /// </summary>
        void FindNeighbours(int i)
        {
            _nearCount = 0;
            var a = _anchors[i];
            var reach = (MaxAnchorRadius + ReachSoft * (SqueezeBand + FadeBand)) * a.Scale;
            for (var k = 0; k < _anchorCount; k++)
            {
                if (k == i) continue;
                var b = _anchors[k];
                if (b.Scale <= MinScale) continue;
                var card = b.FaceDown || _state[k].Open < 1f;
                var art = !b.FaceDown && _hasFwd[k] && b.ArtHalf > 0f;
                if (!card && !art) continue;
                var dx = b.Position.x - a.Position.x;
                var dz = b.Position.z - a.Position.z;
                var r = CardReach(b) + reach;
                if (dx * dx + dz * dz < r * r) _near[_nearCount++] = k;
            }
        }


        /// <summary>
        /// One monster's vortex: its streaks, each a main line and a companion,
        /// grown and faded in by its Open ease and dimmed by its Dim ease.
        /// </summary>
        void WriteAnchor(int v, int ai, float fade, Vector3 eye)
        {
            _self = ai;
            _a = _anchors[ai];
            var st = _state[ai];
            _o = _a.Position;
            _s = _a.Scale;
            _side = _o.z < 0f ? -1f : 1f;
            var toEye = eye - _o;
            var flat = Mathf.Sqrt(toEye.x * toEye.x + toEye.z * toEye.z);
            _camX = flat > 1e-4f ? toEye.x / flat : 0f;
            _camZ = flat > 1e-4f ? toEye.z / flat : -1f;
            _view = toEye.sqrMagnitude > 1e-8f ? toEye.normalized : Vector3.up;

            // From the monster's stable key: steady through lunges, hit punches, relayouts and list reorders.
            var spin = TwoPi * Hash01(_a.Key, 71);
            var shift = Hash01(_a.Key, 73);

            var si = st.Aura + 1;
            var sh = Shapes[si];
            var opened = Mathf.SmoothStep(0f, 1f, st.Open);
            var n = _count + (st.Aura > 0 ? 1 : 0);
            var grow = Mathf.Lerp(GrowFloor, 1f, Mathf.SmoothStep(0f, 1f, fade) * opened);
            var height = _height * sh.Height * grow;
            var turns = (_a.PlayerSide ? 1f : -1f) * sh.Turns * TwoPi;
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
                var seed = unchecked((_cycle[k] + wrap) * CyclePrime + _a.Key);
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
        /// out of the feet and thins away near the top. Each row's climb is squeezed
        /// under the cards' cover (<see cref="Ceiling"/>) with room for its ribbon, and
        /// a row with too little room left fades out.
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
            var lift = Lift * _s;
            var climbS = climb * _s;
            var midFade = MidlineFade * _s;
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
                var rise = (climb * uc * (RiseAtFeet + (1f - RiseAtFeet) * uc) - drop * uc) * _s;
                var dy = (climb * (RiseAtFeet + 2f * (1f - RiseAtFeet) * uc) - drop) * _s;

                // Squeeze the climb under the cards' cover here, keeping room for the ribbon's half width.
                var pad = halfW * _alongWidth[j] * _s;
                var cx = _o.x + cs * r;
                var cz = _o.z + sn * r;
                var room = Ceiling(cx, cz, pad, climbS, out var show) - _o.y - lift - pad;
                var squeeze = climbS > 1e-6f ? Mathf.Clamp01(room / climbS) : 1f;

                var c = new Vector3(cx, _o.y + lift + rise * squeeze, cz);
                var tangent = new Vector3(dr * cs - r * sn * turns, dy * squeeze, dr * sn + r * cs * turns);

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
                var a = Mathf.Min(MaxAlpha, alpha * _alongAlpha[j]) * pathFade * Mathf.Lerp(1f, FrontAlpha, front) *
                        show * Ramp(SqueezeHide, SqueezeShow, squeeze);
                var col = Color.Lerp(Color.Lerp(cTail, cBody, _mixBody[j]), cHead, _mixHead[j]);

                var e = across * pad;
                var o = v + j * 2;
                _verts[o] = Contain(c - e, out var gap0);
                _verts[o + 1] = Contain(c + e, out var gap1);
                _cols[o] = WithAlpha(col, a * Ramp(0f, midFade, gap0));
                _cols[o + 1] = WithAlpha(col, a * Ramp(0f, midFade, gap1));
            }
        }

        /// <summary>
        /// Smooth, conservative cover height at floor-local (<paramref name="x"/>, <paramref name="z"/>)
        /// for a ribbon row reaching <paramref name="pad"/> either side, and in <paramref name="show"/>
        /// how much of the row may be seen. Never above <see cref="ArFieldSignature.CoverLimitAll"/>
        /// anywhere the ribbon touches: each card's zone is widened by the ribbon's reach.
        /// </summary>
        float Ceiling(float x, float z, float pad, float climbS, out float show)
        {
            show = 1f;
            var c = CardCeiling(_self, x, z, pad, climbS, ref show);
            for (var n = 0; n < _nearCount; n++)
                c = Mathf.Min(c, CardCeiling(_near[n], x, z, pad, climbS, ref show));
            return c;
        }

        /// <summary>
        /// Anchor <paramref name="k"/>'s card as the vortex being written sees it: a Set card's
        /// footprint (<see cref="ArFieldSignature.InSetCard"/>'s box; a neighbour that just stood
        /// up keeps it while its flip ease runs), and the camera side of face-up art inside its
        /// lateral span (<see cref="ArFieldSignature.CoverLimit"/>'s test, at the eased cover height),
        /// for a neighbour only within its <see cref="CardReach"/> as
        /// <see cref="ArFieldSignature.CoverLimitAll"/> counts it.
        /// </summary>
        float CardCeiling(int k, float x, float z, float pad, float climbS, ref float show)
        {
            var b = _anchors[k];
            var free = GuardHeight * _s;
            var c = free;
            var setShare = b.FaceDown ? 1f : k == _self ? 0f : 1f - Mathf.SmoothStep(0f, 1f, _state[k].Open);
            if (setShare > 0f)
            {
                var inCard = Mathf.Min((SetCardHalfX + SetMargin) * b.Scale - Mathf.Abs(x - b.Position.x),
                    (SetCardHalfZ + SetMargin) * b.Scale - Mathf.Abs(z - b.Position.z));
                var clear = SetCardClearHeight * b.Scale;
                c = Zone(inCard + pad, clear, clear, setShare, pad, climbS, free, ref show);
            }

            if (b.FaceDown || !_hasFwd[k] || b.ArtHalf <= 0f) return c;
            var dx = x - b.Position.x;
            var dz = z - b.Position.z;
            var depth = dx * _fwdX[k] + dz * _fwdZ[k];
            var lateral = dz * _fwdX[k] - dx * _fwdZ[k];
            var inFront = Mathf.Min(depth, b.ArtHalf - Mathf.Abs(lateral - b.ArtLateral));
            if (k != _self) inFront = Mathf.Min(inFront, (CardReach(b) - Mathf.Sqrt(dx * dx + dz * dz)) / ReachSoft);
            var cover = Mathf.Min(b.FrontCoverHeight, _state[k].Cover);
            return Mathf.Min(c, Zone(inFront + pad, cover, b.FrontCoverHeight, 1f, pad, climbS, free, ref show));
        }

        /// <summary>
        /// Ceiling near one card zone, <paramref name="t"/> = how far the ribbon reaches into it
        /// (negative outside). A zone whose settled cover (<paramref name="target"/>) leaves room
        /// for the climb squeezes it down over <see cref="SqueezeBand"/>; one without room fades
        /// the streak out over <see cref="FadeBand"/> before it drops over <see cref="DiveBand"/>.
        /// While the cover in use is still growing toward its target the streak shows only as
        /// far as that cover leaves room.
        /// </summary>
        float Zone(float t, float cover, float target, float share, float pad, float climbS, float free, ref float show)
        {
            var floor = _o.y + Lift * _s + pad;
            var band = Mathf.Lerp(SqueezeBand, DiveBand, Low(target - floor, climbS)) * _s;
            show *= 1f - share * Low(cover - floor, climbS) * Ramp(-band - FadeBand * _s, -band, t);
            return Mathf.Lerp(free, Mathf.Min(free, cover), share * Ramp(-band, 0f, t));
        }

        /// <summary>1 when a ceiling leaves no room for a ribbon's climb, 0 when it leaves enough to show it.</summary>
        static float Low(float room, float climbS) =>
            climbS > 1e-6f ? 1f - Ramp(SqueezeHide, SqueezeShow, room / climbS) : 0f;

        /// <summary>
        /// Hard guards for a vertex of the anchor being written: inside the contract radius,
        /// within <see cref="ArFieldSignature.LaneHalfWidth"/> sideways, pulled back to its own
        /// side of the aisle line (<paramref name="gap"/> = MidlineGap after the pull), and no
        /// taller than <see cref="ArFieldSignature.CoverLimitAll"/> (its own card and every
        /// other card that reaches it, in either row).
        /// </summary>
        Vector3 Contain(Vector3 p, out float gap)
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

            var lane = LaneHalfWidth * _s;
            p.x = Mathf.Clamp(p.x, _o.x - lane, _o.x + lane);
            gap = MidlineGap(_a, p);
            if (gap < 0f)
            {
                p.z -= _side * gap;
                gap = 0f;
            }

            var top = Mathf.Min(GuardHeight * _s, CoverLimitAll(_a, _anchorList, _street, p));
            p.y = Mathf.Clamp(p.y, _o.y, _o.y + top);
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
            _anchors = new FieldAnchor[MaxTracked];
            _state = new AnchorState[MaxTracked];
            _prevState = new AnchorState[MaxTracked];
            _fwdX = new float[MaxTracked];
            _fwdZ = new float[MaxTracked];
            _hasFwd = new bool[MaxTracked];
            _near = new int[MaxTracked];
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
