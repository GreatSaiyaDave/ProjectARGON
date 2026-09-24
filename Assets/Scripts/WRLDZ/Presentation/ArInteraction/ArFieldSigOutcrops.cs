using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Faceted low-poly rock shards ringing each monster, set in a belt just
    /// outside the ownership ring so the terrain pad, ring and aura stay
    /// readable. Each shard is a flat-shaded pentagonal solid (two side bands
    /// and a peaked cap) coloured from the palette ground and lit by a fixed
    /// fake sun: tops light, sides darker, feet shaded. Shards break out of the
    /// street one after another as a monster's presence ramps in (height eases
    /// up with a small overshoot) and sink back on dissolve. Sizes follow the
    /// anchor's resting Scale, so the rocks stay put while the hologram flinches.
    /// <para>Layout: "toward the duelist" is fixed from the street's player end,
    /// so rings never turn as the phone moves. The front stays open over the
    /// monster's feet, shards in front of the art as seen from there are built
    /// under <see cref="ArFieldSignature.UprightCoverHeight"/>, and the front
    /// flanks carry the tallest shards the duelist sees. Each monster's layout
    /// and heat phase are seeded from its anchor Key, so it keeps them while
    /// others come and go or it lunges.</para>
    /// <para>Lane and aisle, per shard every frame in the floor frame (outer
    /// columns' rings are turned up to ~36°): a rock that would reach further
    /// sideways than <see cref="ArFieldSignature.LaneHalfWidth"/>, or past the
    /// line <see cref="ArFieldSignature.AisleClear"/> short of the street midline
    /// (<see cref="ArFieldSignature.MidlineGap"/>), is first pulled in toward the
    /// monster, no closer than the belt's inner edge, then narrowed until it
    /// fits (height × √fit); its ground fan shrinks to fit as well. On the real
    /// layout every shard keeps at least about half its footprint. Squeezed below
    /// <see cref="FitShow"/> a shard fades, and at <see cref="FitHide"/> it is
    /// not drawn (deep in a lunge).</para>
    /// <para>Cards: each shard's top is clamped every frame against the real
    /// camera with <see cref="ArFieldSignature.CoverLimitAll"/>: every card near
    /// it counts, its own monster's and the neighbours'. Face-up art:
    /// <see cref="ArFieldSignature.CoverLimit"/> of that card, tested at the rock's
    /// most camera-ward point as seen from it, shifted toward the art's centre. In
    /// front of upright art a rock stays in the art's lower band (0.30 × Scale).
    /// Defense art is rolled onto its side and centred at street level beside its
    /// monster, so in front of it the rocks drop to 0.10 × Scale while the other
    /// flank keeps its tall ones. Rolled art lies across the whole neighbouring
    /// lane, so the neighbour's rocks in front of it drop as well, and a facing
    /// monster's art stands behind a ring's back shards as seen from the camera,
    /// so those stay under its cover too. A Set card lies almost flat (1.40 × 0.91,
    /// <see cref="ArFieldSignature.InSetCard"/>), and a flip counts as Set until
    /// the card stands. A shard whose footprint overlaps any Set card (its own,
    /// or a neighbour's, whose ends reach past the lane line at full size) drops
    /// to rubble under <see cref="ArFieldSignature.SetCardClearHeight"/>. Most of
    /// the belt lies under the card; the few shards in the strip behind their
    /// own card keep standing, and those in the strip between it and the camera
    /// stay under <see cref="SetFrontHeight"/>. Drops are instant. When a shard
    /// may stand taller again (a card stands up, art rolls upright, or the
    /// camera moves off it) it grows back over <see cref="RiseSeconds"/>.
    /// This ceiling is kept per shard and looked up by anchor Key, so an ease
    /// survives others coming and going. Boon and bane wait until the card
    /// stands.</para>
    /// <para>Variant 0, dusty boulders: six low, rounded tan boulders with a pale
    /// strata line and a flat-topped mesa slab at one front corner. A contact
    /// shadow under each puffs into dust as the boulder breaks the ground.</para>
    /// <para>Variant 1, crags: six leaning cool-grey spires with snow on their
    /// upper facets, tall ones on the front flanks and the tallest behind the
    /// monster. A breathing cloud bank is drawn over each foot.</para>
    /// <para>Variant 2, lava-cracked: five dark basalt chunks split by glowing
    /// accent seams over a pooled lava glow, with a crater cone glowing at one
    /// front corner. The heat pulses slowly round the ring. Any other variant
    /// draws the boulders.</para>
    /// A face-up monster the field favours gets an accent sheen on its rock tops.
    /// Palette colours are mixed in sRGB and converted with VertexColor() once
    /// at build time. One dynamic mesh, one draw call, at most 3800 vertices,
    /// no per-frame allocation. Sprites/Default writes no depth, so each frame
    /// the shards are written far-to-near into fixed slots and faces turned
    /// away from the camera are collapsed; convex shards then need no depth
    /// buffer of their own.
    /// </summary>
    public sealed class ArFieldSigOutcrops : ArFieldSignature
    {
        const int MaxAnchors = 10;
        /// <summary>Prebuilt ring layouts; each monster picks one from its anchor Key.</summary>
        const int Templates = 4;
        /// <summary>Sides per shard: pentagonal solids read as faceted rock at phone distance.</summary>
        const int Sides = 5;
        const int FanSegs = 6;
        const int MaxCracks = 2;

        // Vertex layout of one shard slot: ground fan, side band A, side band B, cap, lava seams.
        const int FanVerts = 1 + FanSegs;
        const int BandABase = FanVerts;
        const int BandBBase = BandABase + Sides * 4;
        const int CapBase = BandBBase + Sides * 4;
        /// <summary>62 per shard: boulders and crags, 6 × 62 × <see cref="MaxAnchors"/> = 3720 vertices.</summary>
        const int CrackBase = CapBase + Sides * 3;
        /// <summary>Seam quad up band A plus a tapering sliver on band B: basalt, 5 × 76 × 10 = 3800.</summary>
        const int CrackVerts = 7;
        const int Faces = Sides * 3;
        const int ShardTris = Sides * 4 + Sides;

        const byte KindRock = 0;
        const byte KindFan = 1;
        const byte KindCrack = 2;

        // Belt around the anchor (host-local metres, × anchor.Scale).
        /// <summary>Solid rock starts outside the ownership ring (SummonRingDiameter / 2 = 0.45).</summary>
        const float BeltInner = 0.47f;
        /// <summary>Hard cap on any vertex's reach, inside MaxAnchorRadius (0.75).</summary>
        const float ReachCap = 0.72f;
        /// <summary>
        /// Build-time sideways reach in the ring's own frame (= LaneHalfWidth, half the
        /// column pitch). Fit re-checks the lane in the floor frame, where outer rings are turned.
        /// </summary>
        const float SideReach = 0.6f;
        /// <summary>Soft ground fans stop here on the monster side.</summary>
        const float FanInner = 0.4f;
        /// <summary>Hard cap on any vertex's height at the peak of the rise, inside MaxAnchorHeight (0.6).</summary>
        const float HeightCap = 0.57f;
        /// <summary>A shard over a Set card, ground fan included, tops out at this share of SetCardClearHeight.</summary>
        const float SetClearMargin = 0.8f;
        /// <summary>InSetCard's own margin (host-local): a footprint is tested with this plus its radius.</summary>
        const float SetCardMargin = 0.03f;
        /// <summary>
        /// Kit rule, stricter than CoverLimit: a shard in the strip between a Set
        /// card and the camera stays this low (× Scale). The card's near edge is
        /// about 1 cm up, so a 0.3 rock there would hide its near third.
        /// </summary>
        const float SetFrontHeight = 0.1f;
        /// <summary>
        /// Seconds for a shard to grow back once it may stand taller: a Set card
        /// standing up after its flip, Defense art rolling upright, or the camera
        /// moving so the shard no longer stands in front of the art. Drops are instant.
        /// </summary>
        const float RiseSeconds = 0.6f;
        /// <summary>
        /// Lane / aisle fit: a shard squeezed below FitShow of its footprint fades
        /// out by FitHide and is then not drawn. On the real layout (rows 0.7 ×
        /// Scale from the midline, columns turned up to ~36° from the player's end)
        /// no shard falls below about 0.52, so these only act deep in a lunge or
        /// on a crowded street.
        /// </summary>
        const float FitHide = 0.3f;
        const float FitShow = 0.5f;
        const float MinScale = 0.001f;
        /// <summary>Duelist's spot behind the street box (floor-local m); rings orient away from it.</summary>
        const float NominalEyeBack = 1f;

        /// <summary>Ring rotation per template and per-shard wander (degrees).</summary>
        const float TemplateSpin = 8f;
        const float AngleJitter = 6f;
        /// <summary>Column angle jitter round a shard (share of one side).</summary>
        const float ColumnJitter = 0.15f;
        const float FootJitter = 0.1f;
        /// <summary>Band-top height jitter (share of the shard's height).</summary>
        const float BandRough = 0.06f;
        const float MesaLift = 1.2f;
        const float MesaRough = 0.01f;

        /// <summary>Latest share of the monster's presence at which a shard starts to rise.</summary>
        const float StaggerMax = 0.4f;
        /// <summary>easeOutBack constant: 1 gives a ~4% overshoot as a shard breaks the ground.</summary>
        const float RiseBack = 1f;
        /// <summary>Shards fade in over this share of their own rise, so no flat patch shows at the start.</summary>
        const float EmergeFade = 0.3f;

        // Fake light in the template frame (+Z away from the duelist), so every ring reads lit from the same side.
        static readonly Vector3 Sun = new Vector3(-0.45f, 0.8f, -0.4f).normalized;
        const float Ambient = 0.12f;
        const float UpLight = 0.45f;
        const float SunLight = 0.5f;
        const float FootShade = 0.68f;

        const float SnowLow = 0.5f;
        const float SnowHigh = 0.85f;
        const float DustAlpha = 0.68f;
        const float CrackAlpha = 0.95f;
        const float CrackWidth0 = 0.12f;
        const float CrackWidth1 = 0.05f;
        const float CrackWander = 0.15f;
        // Glow and drain weights blend linear vertex colours: over near-black basalt a
        // linear blend toward lava reads far brighter than the same sRGB weight, so these
        // sit low to keep the heat pulse's range (and a bane's drain reads weaker, so higher).
        const float CraterRim = 0.1f;
        /// <summary>Radians per second: a ~3.5 s heat cycle round the ring.</summary>
        const float PulseRate = 1.8f;
        const float GlowFloor = 0.1f;
        const float BreathRate = 0.6f;
        const float SheenRate = 2.2f;
        const float BoonSheen = 0.4f;
        const float BoonHeat = 1.3f;
        const float BaneDrain = 0.5f;

        const float TwoPi = Mathf.PI * 2f;

        enum FanKind { Dust, Cloud, Lava }

        /// <summary>One shard per ring (<c>Look.KeystoneAt</c>) can take a special silhouette.</summary>
        enum Keystone { None, Mesa, Crater }

        /// <summary>Side profile of a shard: band tops as shares of its height and base radius.</summary>
        readonly struct Shape
        {
            public readonly float Y1, R1, Y2, R2;
            /// <summary>Apex offset as a share of the base radius.</summary>
            public readonly float Lean;

            public Shape(float y1, float r1, float y2, float r2, float lean)
            {
                Y1 = y1;
                R1 = r1;
                Y2 = y2;
                R2 = r2;
                Lean = lean;
            }
        }

        /// <summary>One variant's shards: count, silhouette, colour touches and ground fan.</summary>
        struct Look
        {
            public float[] At;         // ring angles: degrees from straight toward the duelist, + to their right
            public float[] Height;     // per shard, host-local m at SigScale 1
            public int KeystoneAt;     // index in At of the keystone shard (−1 none)
            public float HJitter;
            public float FootMin;      // base radius range, shortest to tallest shard (host-local m)
            public float FootMax;
            public Shape Body;
            public float Wobble;       // per-vertex radius jitter
            public Keystone Keystone;
            public float Alpha;
            public float Strata;       // pale line where the side bands meet
            public float Snow;
            public float FootGlow;     // lava light on the base ring
            public int Cracks;
            public FanKind Fan;
            public bool FanOver;       // drawn after its shard (cloud round the foot) instead of under it
            public float FanReach;     // fan radius as a multiple of the shard's footprint
            public float FanAlpha;
            public float FanLift;      // host-local m
            public float FanGrow;      // extra radius at the peak of the dust puff / breath / pulse
        }

        static readonly Shape MesaShape = new Shape(0.5f, 1f, 0.93f, 0.86f, 0.04f);
        static readonly Shape CraterShape = new Shape(0.45f, 0.72f, 0.88f, 0.36f, 0.05f);

        static readonly Look Boulders = new Look
        {
            At = new[] { -145f, -75f, -33f, 30f, 72f, 140f },
            Height = new[] { 0.1f, 0.13f, 0.07f, 0.12f, 0.12f, 0.09f },
            KeystoneAt = 3, HJitter = 0.2f,
            FootMin = 0.065f, FootMax = 0.1f, Body = new Shape(0.42f, 1.06f, 0.8f, 0.7f, 0.1f),
            Wobble = 0.08f, Keystone = Keystone.Mesa, Alpha = 0.94f, Strata = 0.35f, Snow = 0f, FootGlow = 0f,
            Cracks = 0, Fan = FanKind.Dust, FanOver = false, FanReach = 1.6f, FanAlpha = 0.55f, FanLift = 0.003f,
            FanGrow = 0.35f
        };

        static readonly Look Crags = new Look
        {
            At = new[] { -132f, -62f, -26f, 28f, 60f, 128f },
            Height = new[] { 0.3f, 0.27f, 0.14f, 0.12f, 0.26f, 0.32f },
            KeystoneAt = -1, HJitter = 0.12f,
            FootMin = 0.07f, FootMax = 0.115f, Body = new Shape(0.3f, 0.66f, 0.64f, 0.34f, 0.28f),
            Wobble = 0.1f, Keystone = Keystone.None, Alpha = 0.95f, Strata = 0f, Snow = 0.9f, FootGlow = 0f,
            Cracks = 0, Fan = FanKind.Cloud, FanOver = true, FanReach = 2f, FanAlpha = 0.36f, FanLift = 0.035f,
            FanGrow = 0.1f
        };

        static readonly Look Basalt = new Look
        {
            At = new[] { -135f, -68f, -28f, 42f, 105f },
            Height = new[] { 0.17f, 0.22f, 0.12f, 0.26f, 0.2f },
            KeystoneAt = 3, HJitter = 0.15f,
            FootMin = 0.075f, FootMax = 0.11f, Body = new Shape(0.5f, 0.9f, 0.8f, 0.55f, 0.2f),
            Wobble = 0.12f, Keystone = Keystone.Crater, Alpha = 0.96f, Strata = 0f, Snow = 0f, FootGlow = 0.35f,
            Cracks = 2, Fan = FanKind.Lava, FanOver = false, FanReach = 1.7f, FanAlpha = 0.5f, FanLift = 0.004f,
            FanGrow = 0.08f
        };

        Look _look;
        int _style;
        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        /// <summary>Shards per monster after the vertex budget.</summary>
        int _rocks;
        int _cracks;
        int _slotVerts;
        /// <summary>Slots written last frame; slots past this frame's count are cleared once.</summary>
        int _slotsLive;
        float _risePeak;
        /// <summary>The ground fan stands taller than a Set card's clearance (cloud), so it counts in that test.</summary>
        bool _tallFan;

        // Template shards (Templates × _rocks) in the anchor frame at unit Scale: +Z away from the duelist.
        Vector3[] _tp;
        /// <summary>Rock: lit, converted vertex colour. Fan / seam: white with the alpha weight in a.</summary>
        Color[] _tc;
        /// <summary>Lava glow weight (seams, basalt feet, crater tip).</summary>
        float[] _tg;
        /// <summary>Boon sheen weight (up-facing facets).</summary>
        float[] _ts;
        /// <summary>Face whose facing decides whether the vertex is drawn (−1 = always).</summary>
        int[] _th;
        Vector3[] _fn;
        Vector3[] _fc;
        Vector3[] _rc;
        float[] _rh;
        /// <summary>Angle round the ring (radians); phases the heat wave.</summary>
        float[] _rTheta;
        /// <summary>Share of the monster's presence before this shard starts to rise.</summary>
        float[] _rDelay;
        /// <summary>Highest template vertex of the shard, ground fan included (unit Scale).</summary>
        float[] _rTop;
        /// <summary>Rock body footprint radius round <see cref="_rc"/>, seams included (unit Scale).</summary>
        float[] _rRock;
        /// <summary>Ground fan radius round <see cref="_rc"/> (unit Scale).</summary>
        float[] _rFan;
        /// <summary>Per slot vertex: fan, rock face or seam (same for every slot).</summary>
        byte[] _kind;

        // Live anchors this frame.
        Vector3[] _aPos;
        float[] _aScale;
        float[] _aFade;
        float[] _aBx;
        float[] _aBz;
        int[] _aTpl;
        int[] _aAura;
        float[] _aPhase;
        int[] _aKey;
        /// <summary>
        /// Per shard (live anchor × _rocks + k): the tallest it may stand, as a
        /// multiple of its template height. Drops at once, grows back over RiseSeconds.
        /// </summary>
        float[] _ceil;

        // Last frame's live anchors and their shards' ceilings, matched by Key so an
        // ease survives others coming and going (swapped with the arrays above, no allocation).
        int[] _prevKey;
        float[] _prevCeil;
        int _prevLive;

        // Anchor being fitted: flat unit vector toward the camera, lane and aisle room (floor metres).
        float _camX;
        float _camZ;
        float _lane;
        float _aisle;
        /// <summary>+1 when the midline is at +Z from the anchor (player half), −1 on the opponent's.</summary>
        float _toMid;
        /// <summary>
        /// This frame's Set anchors. A Set card is 1.40 × Scale wide, so at full size
        /// its ends reach past the neighbouring rings' lane line (0.6 × Scale from
        /// their anchor, 0.6 from its own): every shard is tested against every card.
        /// </summary>
        FieldAnchor[] _sets;
        int _setCount;
        /// <summary>
        /// This frame's face-up anchors (with a camera), for the art half of
        /// <see cref="ArFieldSignature.CoverLimitAll"/>. Sideways Defense art lies
        /// across a whole neighbouring lane, and a facing monster's art stands
        /// behind a ring's back shards as seen from the camera, so every shard is
        /// tested against every card near it, its own included.
        /// </summary>
        FieldAnchor[] _arts;
        /// <summary>Per face-up anchor: flat unit vector toward the camera.</summary>
        float[] _artCamX;
        float[] _artCamZ;
        /// <summary>Per face-up anchor: CoverLimitAll's reach round it (floor metres).</summary>
        float[] _artReach;
        int _artCount;

        // Candidate shards this frame, sorted far-to-near through _order.
        int[] _sAnchor;
        int[] _sRock;
        float[] _sProg;
        /// <summary>Height as a multiple of the template's (rise × √fit, under the ceiling).</summary>
        float[] _sRise;
        /// <summary>Footprint as a share of the template's (lane / aisle fit).</summary>
        float[] _sFit;
        /// <summary>Share of the fitted ground fan's radius that still clears the lines and the ring.</summary>
        float[] _sFan;
        /// <summary>Floor-local shard centre at street level, after any pull-in.</summary>
        Vector3[] _sBase;
        float[] _sDist;
        Vector3[] _sCentre;
        int[] _order;
        bool[] _faceVis;

        float _pulsePh;
        float _breathPh;
        float _sheenPh;

        // Build-time rock inputs, sRGB: faces are mixed and lit from these, then converted per vertex.
        Color _lit;
        Color _dark;
        Color _strata;
        Color _snow;
        // Tick blend targets, already converted with VertexColor().
        Color _shadow;
        Color _dust;
        Color _cloud;
        Color _emberHot;
        Color _emberDim;
        Color _lavaPool;
        Color _boonTint;
        Color _drained;

        // Shard being built.
        Keystone _bKey;
        float _bH;
        float _hMin;
        float _hSpan;

        protected override void Build()
        {
            _style = Variant == 1 || Variant == 2 ? Variant : 0;
            _look = _style == 1 ? Crags : _style == 2 ? Basalt : Boulders;
            _cracks = Mathf.Clamp(_look.Cracks, 0, MaxCracks);
            _slotVerts = CrackBase + _cracks * CrackVerts;
            var budget = MaxVertices / (MaxAnchors * _slotVerts);
            _rocks = Mathf.Clamp(Mathf.Min(_look.At.Length, _look.Height.Length), 1, budget);
            var q = -2f * RiseBack / (3f * (RiseBack + 1f));
            _risePeak = EaseOutBack(1f + q);
            _tallFan = _look.FanLift * _risePeak > SetClearMargin * SetCardClearHeight;

            _kind = new byte[_slotVerts];
            for (var v = 0; v < _slotVerts; v++)
                _kind[v] = v < FanVerts ? KindFan : v < CrackBase ? KindRock : KindCrack;

            BuildColours();
            BuildTemplates();

            var slots = MaxAnchors * _rocks;
            _verts = new Vector3[slots * _slotVerts];
            _cols = new Color32[_verts.Length];

            _aPos = new Vector3[MaxAnchors];
            _aScale = new float[MaxAnchors];
            _aFade = new float[MaxAnchors];
            _aBx = new float[MaxAnchors];
            _aBz = new float[MaxAnchors];
            _aTpl = new int[MaxAnchors];
            _aAura = new int[MaxAnchors];
            _aPhase = new float[MaxAnchors];
            _aKey = new int[MaxAnchors];
            _ceil = new float[slots];
            _sets = new FieldAnchor[MaxAnchors];
            _arts = new FieldAnchor[MaxAnchors];
            _artCamX = new float[MaxAnchors];
            _artCamZ = new float[MaxAnchors];
            _artReach = new float[MaxAnchors];
            _prevKey = new int[MaxAnchors];
            _prevCeil = new float[slots];
            _sAnchor = new int[slots];
            _sRock = new int[slots];
            _sProg = new float[slots];
            _sRise = new float[slots];
            _sFit = new float[slots];
            _sFan = new float[slots];
            _sBase = new Vector3[slots];
            _sDist = new float[slots];
            _sCentre = new Vector3[slots];
            _order = new int[slots];
            _faceVis = new bool[Faces];

            _mesh = NewDynamicMesh("FieldOutcrops");
            _mesh.vertices = _verts;
            _mesh.uv = new Vector2[_verts.Length];
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles(slots);
            _mr = AddMeshChild("Outcrops", _mesh,
                VertexColorMaterial("FieldOutcropsMat", GroundQueue, Texture2D.whiteTexture));

            _pulsePh = R(0f, TwoPi);
            _breathPh = R(0f, TwoPi);
            _sheenPh = R(0f, TwoPi);
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
            if (_mr == null) return;
            var count = anchors == null ? 0 : Mathf.Min(anchors.Count, MaxAnchors);
            if (level <= 0.001f || count == 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                _prevLive = 0;
                return;
            }

            level = Mathf.Min(level, 1f);
            var step = dt > 0f ? dt : 0f;
            Advance(step);
            var viewX = street.Center.x;
            var viewZ = street.Center.z - street.Half.z - NominalEyeBack;
            // The stage camera as CoverLimit sees it; the duelist's end of the street without one.
            var eye = street.HasCamera ? street.Camera : new Vector3(viewX, street.Center.y, viewZ);
            var bounds = new Bounds(street.Center, street.Half * 2f);

            _setCount = 0;
            _artCount = 0;
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                if (a.Scale <= MinScale) continue;
                if (a.FaceDown)
                {
                    _sets[_setCount++] = a;
                    continue;
                }

                // Face-up art constrains nothing without a camera (CoverLimit's own rule).
                if (!street.HasCamera || a.ArtHalf <= 0f) continue;
                var tx = street.Camera.x - a.Position.x;
                var tz = street.Camera.z - a.Position.z;
                var tl = Mathf.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-4f) continue;
                _arts[_artCount] = a;
                _artCamX[_artCount] = tx / tl;
                _artCamZ[_artCount] = tz / tl;
                // CoverLimitAll's reach round a card: its sideways art, or its Set card's corner.
                _artReach[_artCount] = CardReach(a);
                _artCount++;
            }

            var live = 0;
            var shards = 0;
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                var fade = Mathf.Clamp01(a.Presence) * level;
                if (fade <= 0.001f || a.Scale <= MinScale) continue;

                var p = a.Position;
                var s = a.Scale;
                // "Behind" points away from the duelist's end, so the ring never turns as the phone moves.
                var dx = p.x - viewX;
                var dz = p.z - viewZ;
                var len = Mathf.Sqrt(dx * dx + dz * dz);
                _aPos[live] = p;
                _aScale[live] = s;
                _aFade[live] = fade;
                _aBx[live] = len > 1e-4f ? dx / len : 0f;
                _aBz[live] = len > 1e-4f ? dz / len : 1f;
                // Seeded by the monster, not its slot or spot: it keeps its look while others come and go or it lunges.
                _aTpl[live] = Mathf.Min(Templates - 1, (int)(Hash01(a.Key, 29) * Templates));
                // Boon/bane waits until the card stands: a flip still animating reads as Set.
                _aAura[live] = a.FaceDown ? 0 : a.Aura;
                _aPhase[live] = Hash01(a.Key, 41) * TwoPi;
                _aKey[live] = a.Key;
                BeginAnchor(a, eye);
                var prev = PrevSlot(a.Key);

                var first = _aTpl[live] * _rocks;
                for (var k = 0; k < _rocks; k++)
                {
                    var q = first + k;
                    var fit = Fit(live, q, out var cx, out var cz, out var fan);
                    // Every shard's ceiling is eased, hidden or not, so it never jumps when it reappears.
                    var ceil = Ceiling(a, street, q, fit, fan, cx, cz);
                    if (prev >= 0)
                    {
                        var was = _prevCeil[prev * _rocks + k];
                        if (ceil > was) ceil = Mathf.MoveTowards(was, ceil, step / RiseSeconds);
                    }

                    _ceil[live * _rocks + k] = ceil;
                    if (fit <= FitHide) continue;

                    var delay = _rDelay[q];
                    var prog = Mathf.Clamp01((fade - delay) / (1f - delay));
                    if (prog <= 0f) continue;

                    // A squeezed shard narrows more than it shortens (height × √fit), so the tall
                    // spires still read as tall; the ceiling still caps its top.
                    var rise = Mathf.Min(Mathf.Sqrt(fit) * EaseOutBack(prog), ceil);
                    var basePt = new Vector3(p.x + cx, p.y, p.z + cz);
                    var centre = basePt + new Vector3(0f, s * _rh[q] * 0.5f * rise, 0f);
                    _sAnchor[shards] = live;
                    _sRock[shards] = q;
                    _sProg[shards] = prog;
                    _sRise[shards] = rise;
                    _sFit[shards] = fit;
                    _sFan[shards] = fan;
                    _sBase[shards] = basePt;
                    _sDist[shards] = (centre - eye).sqrMagnitude;
                    _sCentre[shards] = centre;
                    shards++;
                }

                bounds.Encapsulate(new Bounds(
                    p + new Vector3(0f, MaxAnchorHeight * 0.5f * s, 0f),
                    new Vector3(MaxAnchorRadius * 2f * s, MaxAnchorHeight * s, MaxAnchorRadius * 2f * s)));
                live++;
            }

            // This frame's ceilings become next frame's lookup (swap, no allocation).
            var keys = _prevKey;
            _prevKey = _aKey;
            _aKey = keys;
            var ceils = _prevCeil;
            _prevCeil = _ceil;
            _ceil = ceils;
            _prevLive = live;

            if (shards == 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            SortFarToNear(shards);
            for (var slot = 0; slot < shards; slot++)
                WriteShard(slot, _order[slot], eye);
            for (var slot = shards; slot < _slotsLive; slot++)
                ClearSlot(slot);
            _slotsLive = shards;

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box (plus each ring) keeps the renderer from being culled.
            _mesh.bounds = bounds;
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Advance(float dt)
        {
            _pulsePh = Mathf.Repeat(_pulsePh + PulseRate * dt, TwoPi);
            _breathPh = Mathf.Repeat(_breathPh + BreathRate * dt, TwoPi);
            _sheenPh = Mathf.Repeat(_sheenPh + SheenRate * dt, TwoPi);
        }

        /// <summary>Last frame's slot for this Key, or −1 (a monster not seen then starts at its targets).</summary>
        int PrevSlot(int key)
        {
            for (var j = 0; j < _prevLive; j++)
                if (_prevKey[j] == key)
                    return j;
            return -1;
        }

        /// <summary>Per-anchor inputs for <see cref="Fit"/> and <see cref="Ceiling"/>.</summary>
        void BeginAnchor(in FieldAnchor a, Vector3 eye)
        {
            var tx = eye.x - a.Position.x;
            var tz = eye.z - a.Position.z;
            var len = Mathf.Sqrt(tx * tx + tz * tz);
            _camX = len > 1e-4f ? tx / len : 0f;
            _camZ = len > 1e-4f ? tz / len : -1f;
            _lane = LaneHalfWidth * a.Scale;
            // MidlineGap at the anchor itself is the room it has toward the midline.
            _aisle = MidlineGap(a, a.Position);
            _toMid = a.Position.z < 0f ? 1f : -1f;
        }

        /// <summary>
        /// Lane and aisle fit for template shard <paramref name="q"/> of live anchor
        /// <paramref name="la"/>. Returns the share of its size it keeps (1 = as built;
        /// at or below <see cref="FitHide"/> it is not drawn) and its centre relative to
        /// the anchor in floor metres. A shard whose rock would cross the lane
        /// (<see cref="ArFieldSignature.LaneHalfWidth"/> sideways) or the aisle line
        /// (<see cref="ArFieldSignature.MidlineGap"/>) is first pulled in toward the
        /// anchor, no closer than the belt's inner edge, then shrunk about its centre.
        /// <paramref name="fan"/> is the share of the fitted ground fan that still clears
        /// both lines and stays off the ownership ring.
        /// </summary>
        float Fit(int la, int q, out float cx, out float cz, out float fan)
        {
            var s = _aScale[la];
            var bx = _aBx[la];
            var bz = _aBz[la];
            var c = _rc[q];
            cx = s * (c.x * bz + c.z * bx);
            cz = s * (c.z * bz - c.x * bx);
            fan = 1f;
            var d = Mathf.Sqrt(cx * cx + cz * cz);
            if (d < 1e-5f) return 1f;

            var ux = cx / d;
            var uz = cz / d;
            var mid = _toMid * uz;
            var rock = Mathf.Max(1e-5f, _rRock[q] * s);
            var inner = BeltInner * s;
            var fit = Mathf.Min(1f, LineFit(ux, d, rock, inner, _lane));
            fit = Mathf.Min(fit, LineFit(-ux, d, rock, inner, _lane));
            fit = Mathf.Min(fit, LineFit(mid, d, rock, inner, _aisle));
            if (fit <= 0f) return 0f;

            var r = rock * fit;
            var dn = Pull(Pull(Pull(d, ux, r, _lane), -ux, r, _lane), mid, r, _aisle);
            cx = ux * dn;
            cz = uz * dn;

            var room = Mathf.Min(Mathf.Min(_lane - dn * ux, _lane + dn * ux), _aisle - dn * mid);
            room = Mathf.Min(room, dn - FanInner * s);
            var fanR = _rFan[q] * s * fit;
            fan = fanR > 1e-6f ? Mathf.Clamp01(room / fanR) : 1f;
            return fit;
        }

        /// <summary>
        /// Largest size share a rock of radius <paramref name="rock"/>, at distance
        /// <paramref name="d"/> on a bearing whose cosine with the line's outward normal
        /// is <paramref name="k"/>, can keep inside a line <paramref name="limit"/> from
        /// the anchor: where it stands, or pulled in until it meets the belt's inner edge.
        /// </summary>
        static float LineFit(float k, float d, float rock, float inner, float limit)
        {
            var stay = (limit - d * k) / rock;
            if (k <= 1e-4f) return stay;
            return Mathf.Max(stay, (limit - inner * k) / (rock * (1f + k)));
        }

        /// <summary>Distance along the bearing at which a rock of radius r just meets the line.</summary>
        static float Pull(float d, float k, float r, float limit) =>
            k > 1e-4f ? Mathf.Min(d, (limit - r) / k) : d;

        /// <summary>
        /// Tallest shard <paramref name="q"/> may stand, as a multiple of its template
        /// height (<see cref="_risePeak"/> = free): <see cref="ArFieldSignature.CoverLimitAll"/>
        /// over the shard's footprint, or a stricter kit rule. Over any Set card's
        /// footprint (its own or a neighbour's; a flip counts as Set until the card
        /// stands): under <see cref="ArFieldSignature.SetCardClearHeight"/>, tested with
        /// <see cref="ArFieldSignature.InSetCard"/> over the shard's footprint. Face-down
        /// otherwise: under <see cref="SetFrontHeight"/> in the strip between its card
        /// and the camera, free in the strip behind it. Every face-up card near the
        /// shard, its own and any neighbour's (sideways Defense art lies across a whole
        /// neighbouring lane; a facing monster's art stands behind the back shards):
        /// <see cref="ArFieldSignature.CoverLimit"/> of that card at the rock's most
        /// camera-ward point as seen from it, shifted toward the art's centre, so any
        /// rock reaching in front of the art (upright or rolled sideways) meets its
        /// FrontCoverHeight.
        /// </summary>
        float Ceiling(in FieldAnchor a, in FieldStreet street, int q, float fit, float fan, float cx, float cz)
        {
            var s = a.Scale;
            var o = a.Position;
            var rock = _rRock[q] * fit;
            // The cloud fan stands above a Set card's clearance, so it counts in the card test.
            var setReach = _tallFan ? Mathf.Max(rock, _rFan[q] * fit * fan) : rock;
            var centre = new Vector3(o.x + cx, o.y, o.z + cz);
            var limit = float.MaxValue;
            if (a.FaceDown)
            {
                if (InSetCard(a, centre, SetCardMargin + setReach))
                    limit = SetClearMargin * SetCardClearHeight * s;
                else if (cx * _camX + cz * _camZ > 0f)
                    limit = SetFrontHeight * s;
            }

            for (var j = 0; j < _setCount; j++)
            {
                ref var b = ref _sets[j];
                if (b.Key == a.Key) continue;
                if (InSetCard(b, centre, SetCardMargin + setReach * s / b.Scale))
                    limit = Mathf.Min(limit, SetClearMargin * SetCardClearHeight * b.Scale);
            }

            // Face-up art: CoverLimitAll evaluated at each card's worst footprint point. Ground
            // fans lie under any art's cover (FanLift ≤ 0.035 × Scale), so the rock radius is enough.
            var r = rock * s;
            for (var j = 0; j < _artCount; j++)
            {
                ref var b = ref _arts[j];
                var dx = centre.x - b.Position.x;
                var dz = centre.z - b.Position.z;
                // CoverLimitAll ignores the card wherever the whole footprint is past its reach.
                var reach = _artReach[j] + r;
                if (dx * dx + dz * dz > reach * reach) continue;
                var tx = _artCamX[j];
                var tz = _artCamZ[j];
                // Most camera-ward point as seen from b (right = (−tz, tx)), shifted toward its art's centre.
                var shift = Mathf.Clamp(b.ArtLateral - (tx * dz - tz * dx), -r, r);
                var lb = CoverLimit(b, street,
                    new Vector3(centre.x + tx * r - tz * shift, o.y, centre.z + tz * r + tx * shift));
                // Only b's card counts here: CoverLimit's MaxAnchorHeight cap is b's, not this ring's.
                if (lb < MaxAnchorHeight * b.Scale) limit = Mathf.Min(limit, lb);
            }

            return Mathf.Min(_risePeak, limit / Mathf.Max(1e-6f, _rTop[q] * s));
        }

        /// <summary>Insertion sort of candidate shards, farthest first (painter's order, no depth writes).</summary>
        void SortFarToNear(int n)
        {
            for (var i = 0; i < n; i++)
                _order[i] = i;
            for (var i = 1; i < n; i++)
            {
                var key = _order[i];
                var d = _sDist[key];
                var j = i - 1;
                while (j >= 0 && _sDist[_order[j]] < d)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = key;
            }
        }

        /// <summary>
        /// One shard into slot <paramref name="slot"/>: template points about the
        /// shard's own centre, turned into the ring's frame, scaled by fit × Scale
        /// across and by <see cref="_sRise"/> × Scale up, and set on its (possibly
        /// pulled-in) base point.
        /// </summary>
        void WriteShard(int slot, int si, Vector3 eye)
        {
            var la = _sAnchor[si];
            var q = _sRock[si];
            var prog = _sProg[si];
            var rise = _sRise[si];
            var fit = _sFit[si];
            var o = _aPos[la];
            var s = _aScale[la];
            var fs = fit * s;
            var bx = _aBx[la];
            var bz = _aBz[la];
            var basePt = _sBase[si];
            var rc = _rc[q];
            var alpha = _aFade[la] * Smooth(0f, EmergeFade, prog) * Smooth(FitHide, FitShow, fit);

            // Faces turned away from the camera are collapsed. Normals follow the fit and
            // rise scales (inverse transpose: across × rise, up × fit).
            var fb = q * Faces;
            for (var f = 0; f < Faces; f++)
            {
                var n = _fn[fb + f];
                var c = _fc[fb + f];
                var ux = c.x - rc.x;
                var uz = c.z - rc.z;
                var nx = (n.x * bz + n.z * bx) * rise;
                var nz = (n.z * bz - n.x * bx) * rise;
                var cx = basePt.x + fs * (ux * bz + uz * bx);
                var cy = o.y + s * c.y * rise;
                var cz = basePt.z + fs * (uz * bz - ux * bx);
                _faceVis[f] = nx * (eye.x - cx) + n.y * fit * (eye.y - cy) + nz * (eye.z - cz) > 0f;
            }

            var phase = _rTheta[q] + _aPhase[la];
            var aura = _aAura[la];
            var pw = 0.5f + 0.5f * Mathf.Sin(_pulsePh - phase);
            var heat = Mathf.Lerp(GlowFloor, 1f, pw) * (aura > 0 ? BoonHeat : 1f);
            var lava = Color.Lerp(_emberDim, _emberHot, pw);
            var sheen = aura > 0 ? BoonSheen * (0.75f + 0.25f * Mathf.Sin(_sheenPh + phase)) : 0f;
            var drain = aura < 0 ? BaneDrain : 0f;
            FanStyle(prog, pw, phase, out var fanCol, out var fanAlpha, out var fanSize);
            fanSize *= _sFan[si];
            var collapse = _sCentre[si];

            var tb = q * _slotVerts;
            var vb = slot * _slotVerts;
            for (var v = 0; v < _slotVerts; v++)
            {
                var tv = tb + v;
                var host = _th[tv];
                if (host >= 0 && !_faceVis[host])
                {
                    _verts[vb + v] = collapse;
                    _cols[vb + v] = default;
                    continue;
                }

                var p = _tp[tv];
                var tc = _tc[tv];
                Color col;
                float a;
                switch (_kind[v])
                {
                    case KindFan:
                        p.x = rc.x + (p.x - rc.x) * fanSize;
                        p.z = rc.z + (p.z - rc.z) * fanSize;
                        col = fanCol;
                        a = fanAlpha * tc.a;
                        break;
                    case KindCrack:
                        col = Color.Lerp(_emberDim, lava, _tg[tv] * heat);
                        a = CrackAlpha * tc.a;
                        break;
                    default:
                        col = tc;
                        var g = _tg[tv];
                        if (g > 0f) col = Color.Lerp(col, lava, g * heat);
                        if (sheen > 0f) col = Color.Lerp(col, _boonTint, _ts[tv] * sheen);
                        if (drain > 0f) col = Color.Lerp(col, _drained, drain);
                        a = _look.Alpha;
                        break;
                }

                col.a = a * alpha;
                _cols[vb + v] = col;
                var px = p.x - rc.x;
                var pz = p.z - rc.z;
                _verts[vb + v] = new Vector3(basePt.x + fs * (px * bz + pz * bx), o.y + s * p.y * rise,
                    basePt.z + fs * (pz * bz - px * bx));
            }
        }

        /// <summary>
        /// Ground fan per variant: a contact shadow that puffs into dust while its
        /// boulder breaks the ground, a slowly breathing cloud, or a lava pool
        /// pulsing with the seams. <paramref name="size"/> ≤ 1 of the template fan.
        /// </summary>
        void FanStyle(float prog, float pw, float phase, out Color col, out float alpha, out float size)
        {
            float g;
            switch (_look.Fan)
            {
                case FanKind.Cloud:
                    g = 0.5f + 0.5f * Mathf.Sin(_breathPh + phase);
                    col = _cloud;
                    alpha = _look.FanAlpha * Mathf.Lerp(0.7f, 1f, g);
                    break;
                case FanKind.Lava:
                    g = pw;
                    col = Color.Lerp(_emberDim, _lavaPool, pw);
                    alpha = _look.FanAlpha * Mathf.Lerp(0.55f, 1f, pw);
                    break;
                default:
                    g = Mathf.Sin(Mathf.PI * prog);
                    col = Color.Lerp(_shadow, _dust, g);
                    alpha = Mathf.Lerp(_look.FanAlpha, DustAlpha, g);
                    break;
            }

            size = (1f + _look.FanGrow * g) / (1f + _look.FanGrow);
        }

        void ClearSlot(int slot)
        {
            System.Array.Clear(_verts, slot * _slotVerts, _slotVerts);
            System.Array.Clear(_cols, slot * _slotVerts, _slotVerts);
        }

        /// <summary>easeOutBack: 0 → 1 with a small overshoot (<see cref="RiseBack"/>).</summary>
        static float EaseOutBack(float p)
        {
            var q = p - 1f;
            return 1f + (RiseBack + 1f) * q * q * q + RiseBack * q * q;
        }

        /// <summary>GLSL-style smoothstep: 0 at or below e0, 1 at or above e1.</summary>
        static float Smooth(float e0, float e1, float x)
        {
            var t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        /// <summary>Point on a side face: u across (p0 → p1 at the bottom), v up (bottom → top).</summary>
        static Vector3 Bilerp(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u, float v) =>
            Vector3.Lerp(Vector3.Lerp(p0, p1, u), Vector3.Lerp(p3, p2, u), v);

        // ── Build ───────────────────────────────────────────────────────────

        void BuildColours()
        {
            var sky = Env.Sky;
            var ground = Env.Ground;
            var accent = Env.Accent;
            var white = Color.white;
            var black = Color.black;
            switch (_style)
            {
                case 1:
                {
                    // Cool grey stone under blue sky, snow on the heights, cloud at the feet.
                    var stone = Color.Lerp(ground, sky, 0.25f);
                    _lit = Color.Lerp(stone, white, 0.4f);
                    _dark = Color.Lerp(Color.Lerp(ground, black, 0.4f), sky, 0.12f);
                    _snow = Color.Lerp(white, sky, 0.14f);
                    _cloud = Color.Lerp(sky, white, 0.7f);
                    break;
                }
                case 2:
                    // Near-black basalt with ash-dusted tops; lava from the accent.
                    _lit = Color.Lerp(Color.Lerp(ground, sky, 0.45f), white, 0.1f);
                    _dark = Color.Lerp(ground, black, 0.75f);
                    _emberHot = Color.Lerp(accent, white, 0.35f);
                    _emberDim = Color.Lerp(accent, ground, 0.55f);
                    _lavaPool = accent;
                    break;
                default:
                    // Sun-baked earth: sandy tops, dusty brown sides, pale strata.
                    _lit = Color.Lerp(Color.Lerp(ground, accent, 0.7f), white, 0.08f);
                    _dark = Color.Lerp(Color.Lerp(ground, black, 0.3f), sky, 0.12f);
                    _strata = Color.Lerp(accent, white, 0.35f);
                    _shadow = Color.Lerp(ground, black, 0.7f);
                    _dust = Color.Lerp(Color.Lerp(sky, accent, 0.35f), white, 0.15f);
                    break;
            }

            _boonTint = Color.Lerp(accent, white, 0.3f);
            _drained = Color.Lerp(ground, black, 0.55f);

            // Tick blends these straight into vertex colours: convert once (unset ones stay black).
            _shadow = VertexColor(_shadow);
            _dust = VertexColor(_dust);
            _cloud = VertexColor(_cloud);
            _emberHot = VertexColor(_emberHot);
            _emberDim = VertexColor(_emberDim);
            _lavaPool = VertexColor(_lavaPool);
            _boonTint = VertexColor(_boonTint);
            _drained = VertexColor(_drained);
        }

        void BuildTemplates()
        {
            var shards = Templates * _rocks;
            _tp = new Vector3[shards * _slotVerts];
            _tc = new Color[_tp.Length];
            _tg = new float[_tp.Length];
            _ts = new float[_tp.Length];
            _th = new int[_tp.Length];
            _fn = new Vector3[shards * Faces];
            _fc = new Vector3[_fn.Length];
            _rc = new Vector3[shards];
            _rh = new float[shards];
            _rTheta = new float[shards];
            _rDelay = new float[shards];
            _rTop = new float[shards];
            _rRock = new float[shards];
            _rFan = new float[shards];

            // The tallest possible shard, at the top of its overshoot, stays under HeightCap at any SigScale.
            var tallest = 0f;
            var highest = 0f;
            var shortest = float.MaxValue;
            for (var k = 0; k < _rocks; k++)
            {
                var h = _look.Height[k];
                var lift = IsKeystone(k) && _look.Keystone == Keystone.Mesa ? MesaLift : 1f;
                tallest = Mathf.Max(tallest, h * lift * (1f + _look.HJitter));
                highest = Mathf.Max(highest, h);
                shortest = Mathf.Min(shortest, h);
            }

            var heightMul = Mathf.Min(SigScale, HeightCap / (Mathf.Max(1e-3f, tallest) * _risePeak));
            var footMul = 0.85f + 0.15f * SigScale;
            _hMin = shortest;
            _hSpan = Mathf.Max(1e-3f, highest - shortest);

            // Build-time scratch: base ring, band A top, band B top, apex.
            var ring = new Vector3[Sides * 3 + 1];
            for (var t = 0; t < Templates; t++)
            {
                // Odd templates mirror the table, so the keystone and the tall flank swap sides.
                var mirror = (t & 1) == 1 ? -1f : 1f;
                var spin = R(-TemplateSpin, TemplateSpin);
                for (var k = 0; k < _rocks; k++)
                    BuildShard(t * _rocks + k, k, mirror, spin, heightMul, footMul, ring);
            }

            ClampExtents();

            // Runtime fit and ceiling inputs: top, rock radius and fan radius round each shard's centre.
            for (var q = 0; q < shards; q++)
            {
                var c = _rc[q];
                var top = 0f;
                var rock = 0f;
                var fan = 0f;
                for (var v = 0; v < _slotVerts; v++)
                {
                    var p = _tp[q * _slotVerts + v];
                    top = Mathf.Max(top, p.y);
                    var r = Flat(p - c);
                    if (_kind[v] == KindFan) fan = Mathf.Max(fan, r);
                    else rock = Mathf.Max(rock, r);
                }

                _rTop[q] = Mathf.Max(1e-4f, top);
                _rRock[q] = rock;
                _rFan[q] = fan;
            }
        }

        bool IsKeystone(int k) => k == _look.KeystoneAt && _look.Keystone != Keystone.None;

        void BuildShard(int q, int k, float mirror, float spin, float heightMul, float footMul, Vector3[] ring)
        {
            // phi: 0 = between the monster and the duelist, + to the duelist's right (template +X).
            var phi = (mirror * _look.At[k] + spin + R(-AngleJitter, AngleJitter)) * Mathf.Deg2Rad;
            _bKey = IsKeystone(k) ? _look.Keystone : Keystone.None;

            var h = _look.Height[k] * R(1f - _look.HJitter, 1f + _look.HJitter) * heightMul;
            var hn = Mathf.Clamp01((_look.Height[k] - _hMin) / _hSpan);
            var foot = Mathf.Lerp(_look.FootMin, _look.FootMax, hn) * R(1f - FootJitter, 1f + FootJitter) * footMul;
            var shape = _look.Body;
            var wobble = _look.Wobble;
            var rough = BandRough;
            if (_bKey == Keystone.Mesa)
            {
                shape = MesaShape;
                h *= MesaLift;
                wobble *= 0.5f;
                rough = MesaRough;
            }
            else if (_bKey == Keystone.Crater)
            {
                shape = CraterShape;
            }

            // Shared column angles, so side edges run straight up the shard.
            var yaw = R(0f, TwoPi);
            for (var i = 0; i < Sides; i++)
            {
                var a = yaw + (i + R(-ColumnJitter, ColumnJitter)) * (TwoPi / Sides);
                var cs = Mathf.Cos(a);
                var sn = Mathf.Sin(a);
                var r0 = foot * R(1f - wobble, 1f + wobble);
                var r1 = foot * shape.R1 * R(1f - wobble, 1f + wobble);
                var r2 = foot * shape.R2 * R(1f - wobble, 1f + wobble);
                ring[i] = new Vector3(cs * r0, 0f, sn * r0);
                ring[Sides + i] = new Vector3(cs * r1, h * shape.Y1 * R(1f - rough, 1f + rough), sn * r1);
                ring[2 * Sides + i] = new Vector3(cs * r2, h * shape.Y2 * R(1f - rough, 1f + rough), sn * r2);
            }

            var leanAt = R(0f, TwoPi);
            ring[3 * Sides] = new Vector3(Mathf.Cos(leanAt) * shape.Lean * foot, h,
                Mathf.Sin(leanAt) * shape.Lean * foot);

            // Fit the footprint in the belt: clear of the ownership ring, inside ReachCap, and no
            // further sideways than SideReach. With sideness s the largest radius that fits is
            // min((ReachCap − BeltInner) / 2, (SideReach − BeltInner·s) / (1 + s)).
            var side = Mathf.Abs(Mathf.Sin(phi));
            var reach = 0f;
            for (var i = 0; i <= 3 * Sides; i++)
                reach = Mathf.Max(reach, Flat(ring[i]));
            var room = Mathf.Min(0.5f * (ReachCap - BeltInner), (SideReach - BeltInner * side) / (1f + side));
            if (reach > room)
            {
                var f = room / reach;
                for (var i = 0; i <= 3 * Sides; i++)
                {
                    ring[i].x *= f;
                    ring[i].z *= f;
                }

                reach = room;
            }

            var far = ReachCap - reach;
            if (side > 1e-3f) far = Mathf.Min(far, (SideReach - reach) / side);
            var d = Mathf.Lerp(BeltInner + reach, Mathf.Max(BeltInner + reach, far), R(0.25f, 0.75f));
            var c = new Vector3(Mathf.Sin(phi) * d, 0f, -Mathf.Cos(phi) * d);
            // Layout: a shard reaching in front of the art plane as seen from the street's player end is
            // built under the upright art's cover height. CoverLimit re-checks every shard against the
            // real camera each frame (outer columns turn, Defense art rolls).
            var fy = c.z < reach && h * _risePeak > UprightCoverHeight ? UprightCoverHeight / (h * _risePeak) : 1f;
            h *= fy;
            _bH = h;
            for (var i = 0; i <= 3 * Sides; i++)
            {
                ring[i].y *= fy;
                ring[i] += c;
            }

            var b = q * _slotVerts;
            var fanR = Mathf.Min(Mathf.Min(_look.FanReach * reach, SideReach - d * side),
                Mathf.Min(ReachCap - d, d - FanInner));
            FlatPoint(b, c + new Vector3(0f, _look.FanLift, 0f), 1f);
            for (var i = 0; i < FanSegs; i++)
            {
                var a = yaw + i * (TwoPi / FanSegs);
                FlatPoint(b + 1 + i, c + new Vector3(Mathf.Cos(a) * fanR, _look.FanLift, Mathf.Sin(a) * fanR), 0f);
            }

            // Bottom-up, which is also the painter's order for concave crag flanks seen from above.
            var axis = c + new Vector3(0f, 0.35f * h, 0f);
            const int apex = 3 * Sides;
            for (var i = 0; i < Sides; i++)
            {
                var i1 = (i + 1) % Sides;
                Face(q, i, b + BandABase + i * 4, 4, ring[i], ring[i1], ring[Sides + i1], ring[Sides + i],
                    0, 1, axis);
                Face(q, Sides + i, b + BandBBase + i * 4, 4, ring[Sides + i], ring[Sides + i1],
                    ring[2 * Sides + i1], ring[2 * Sides + i], 1, 2, axis);
                Face(q, 2 * Sides + i, b + CapBase + i * 3, 3, ring[2 * Sides + i], ring[2 * Sides + i1],
                    ring[apex], ring[apex], 2, 3, axis);
            }

            var col = Mathf.Min(Sides - 1, (int)R(0f, Sides));
            for (var j = 0; j < _cracks; j++)
            {
                if (j > 0) col = (col + 2 + (R(0f, 1f) < 0.5f ? 0 : 1)) % Sides;
                var i1 = (col + 1) % Sides;
                Crack(b + CrackBase + j * CrackVerts, col, ring[col], ring[i1], ring[Sides + i1], ring[Sides + col],
                    ring[2 * Sides + i1], ring[2 * Sides + col]);
            }

            _rc[q] = c;
            _rh[q] = h;
            _rTheta[q] = phi;
            _rDelay[q] = R(0f, StaggerMax);
        }

        /// <summary>
        /// One flat-shaded face on its own vertices: outward normal and centre for
        /// the facing test, one lit colour, then per-corner touches (shaded foot,
        /// strata, snow, lava). Corners run bottom edge p0 → p1, top edge p2 → p3.
        /// <paramref name="tv"/> is the first corner's absolute template index.
        /// </summary>
        void Face(int q, int f, int tv, int corners, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            int lowRing, int highRing, Vector3 axis)
        {
            var n = corners == 4 ? Vector3.Cross(p2 - p0, p3 - p1) : Vector3.Cross(p1 - p0, p2 - p0);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            var centre = corners == 4 ? (p0 + p1 + p2 + p3) * 0.25f : (p0 + p1 + p2) / 3f;
            if (Vector3.Dot(n, centre - axis) < 0f) n = -n;
            _fn[q * Faces + f] = n;
            _fc[q * Faces + f] = centre;

            var light = Mathf.Clamp01(Ambient + UpLight * Mathf.Max(0f, n.y) +
                                      SunLight * Mathf.Max(0f, Vector3.Dot(n, Sun)));
            var shade = Color.Lerp(_dark, _lit, light);
            Corner(tv, p0, shade, n.y, lowRing, f);
            Corner(tv + 1, p1, shade, n.y, lowRing, f);
            Corner(tv + 2, p2, shade, n.y, highRing, f);
            if (corners == 4) Corner(tv + 3, p3, shade, n.y, highRing, f);
        }

        void Corner(int tv, Vector3 p, Color shade, float up, int ringId, int host)
        {
            var c = shade;
            var glow = 0f;
            switch (ringId)
            {
                case 0:
                    c *= FootShade;
                    glow = _look.FootGlow;
                    break;
                case 1:
                    c = Color.Lerp(c, _strata, _look.Strata);
                    break;
                case 2:
                    if (_bKey == Keystone.Crater) glow = CraterRim;
                    break;
                default:
                    if (_bKey == Keystone.Crater) glow = 1f;
                    break;
            }

            if (_look.Snow > 0f)
            {
                var line = Smooth(SnowLow, SnowHigh, p.y / Mathf.Max(1e-4f, _bH));
                c = Color.Lerp(c, _snow, _look.Snow * line * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(up * 2f)));
            }

            // Mixed and lit in sRGB as tuned; converted once here so it shows as designed.
            c.a = 1f;
            _tp[tv] = p;
            _tc[tv] = VertexColor(c);
            _tg[tv] = glow;
            _ts[tv] = Mathf.Clamp01(up);
            _th[tv] = host;
        }

        /// <summary>Always-drawn ground fan vertex: white, alpha weight 1 at the centre, 0 on the rim.</summary>
        void FlatPoint(int tv, Vector3 p, float weight)
        {
            _tp[tv] = p;
            _tc[tv] = new Color(1f, 1f, 1f, weight);
            _tg[tv] = 0f;
            _ts[tv] = 0f;
            _th[tv] = -1;
        }

        /// <summary>
        /// A lava seam up side column <paramref name="face"/>: wide and white-hot
        /// where it leaves the ground, wandering up band A, tapering to a dim tip
        /// on band B. Each half is drawn only while its host face is.
        /// </summary>
        void Crack(int v, int face, Vector3 a0, Vector3 a1, Vector3 a2, Vector3 a3, Vector3 b2, Vector3 b3)
        {
            var u0 = R(0.35f, 0.65f);
            var u1 = Mathf.Clamp(u0 + R(-CrackWander, CrackWander), 0.2f, 0.8f);
            var u2 = Mathf.Clamp(u1 + R(-CrackWander, CrackWander), 0.15f, 0.85f);
            var tip = R(0.45f, 0.8f);
            SeamPoint(v, Bilerp(a0, a1, a2, a3, u0 - CrackWidth0, 0f), 1f, 1f, face);
            SeamPoint(v + 1, Bilerp(a0, a1, a2, a3, u0 + CrackWidth0, 0f), 1f, 1f, face);
            SeamPoint(v + 2, Bilerp(a0, a1, a2, a3, u1 + CrackWidth1, 1f), 0.8f, 1f, face);
            SeamPoint(v + 3, Bilerp(a0, a1, a2, a3, u1 - CrackWidth1, 1f), 0.8f, 1f, face);
            SeamPoint(v + 4, Bilerp(a3, a2, b2, b3, u1 - CrackWidth1, 0f), 0.8f, 1f, Sides + face);
            SeamPoint(v + 5, Bilerp(a3, a2, b2, b3, u1 + CrackWidth1, 0f), 0.8f, 1f, Sides + face);
            SeamPoint(v + 6, Bilerp(a3, a2, b2, b3, u2, tip), 0.45f, 0.7f, Sides + face);
        }

        void SeamPoint(int tv, Vector3 p, float glow, float weight, int host)
        {
            _tp[tv] = p;
            _tc[tv] = new Color(1f, 1f, 1f, weight);
            _tg[tv] = glow;
            _ts[tv] = 0f;
            _th[tv] = host;
        }

        /// <summary>
        /// Safety net over every template vertex. The belt fit and the height
        /// budget already keep them inside <see cref="ReachCap"/> and
        /// <see cref="HeightCap"/>; this rescales only if a retune ever breaks that.
        /// </summary>
        void ClampExtents()
        {
            var reach = 0f;
            var top = 0f;
            for (var i = 0; i < _tp.Length; i++)
            {
                reach = Mathf.Max(reach, Flat(_tp[i]));
                top = Mathf.Max(top, _tp[i].y);
            }

            var fx = reach > ReachCap ? ReachCap / reach : 1f;
            var fy = top * _risePeak > HeightCap ? HeightCap / (top * _risePeak) : 1f;
            if (fx >= 1f && fy >= 1f) return;

            var s = new Vector3(fx, fy, fx);
            for (var i = 0; i < _tp.Length; i++)
                _tp[i] = Vector3.Scale(_tp[i], s);
            for (var i = 0; i < _fn.Length; i++)
            {
                var n = _fn[i];
                _fn[i] = new Vector3(n.x * fy, n.y * fx, n.z * fy).normalized;
                _fc[i] = Vector3.Scale(_fc[i], s);
            }

            for (var i = 0; i < _rc.Length; i++)
            {
                _rc[i] = Vector3.Scale(_rc[i], s);
                _rh[i] *= fy;
            }
        }

        /// <summary>
        /// Index order is the draw order inside the single draw call. Per slot
        /// (slots are filled far-to-near): ground fan under the shard, faces
        /// bottom band to cap, seams, or the cloud fan over the shard's foot.
        /// </summary>
        int[] BuildTriangles(int slots)
        {
            var slotTris = FanSegs + ShardTris + _cracks * 3;
            var tris = new int[slots * slotTris * 3];
            var t = 0;
            for (var s = 0; s < slots; s++)
            {
                var b = s * _slotVerts;
                if (!_look.FanOver) t = AddFan(tris, t, b);
                for (var i = 0; i < Sides; i++)
                    t = AddQuad(tris, t, b + BandABase + i * 4);
                for (var i = 0; i < Sides; i++)
                    t = AddQuad(tris, t, b + BandBBase + i * 4);
                for (var i = 0; i < Sides; i++)
                    t = AddTri(tris, t, b + CapBase + i * 3);
                for (var j = 0; j < _cracks; j++)
                {
                    t = AddQuad(tris, t, b + CrackBase + j * CrackVerts);
                    t = AddTri(tris, t, b + CrackBase + j * CrackVerts + 4);
                }

                if (_look.FanOver) t = AddFan(tris, t, b);
            }

            return tris;
        }

        static int AddFan(int[] tris, int t, int b)
        {
            for (var i = 0; i < FanSegs; i++)
            {
                tris[t++] = b;
                tris[t++] = b + 1 + i;
                tris[t++] = b + 1 + (i + 1) % FanSegs;
            }

            return t;
        }

        static int AddQuad(int[] tris, int t, int v)
        {
            tris[t++] = v;
            tris[t++] = v + 1;
            tris[t++] = v + 2;
            tris[t++] = v;
            tris[t++] = v + 2;
            tris[t++] = v + 3;
            return t;
        }

        static int AddTri(int[] tris, int t, int v)
        {
            tris[t++] = v;
            tris[t++] = v + 1;
            tris[t++] = v + 2;
            return t;
        }
    }
}
