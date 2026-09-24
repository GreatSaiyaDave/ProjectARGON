using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Duelist Kingdom ground cover: a ring of swaying vegetation around each
    /// monster, rooted outside the summon ring's disc (0.47–0.72 × Scale) so the
    /// disc, pad and aura stay readable. Variant 0 (Sogen) is short, dense meadow
    /// grass with sunlit tips; 1 (Forest) is arching fern fronds over broad-leaf
    /// undergrowth; 2 (Gaia Power) is gnarled roots breaking out of the ground at
    /// the disc's edge with accent pulses running outward along them, plus sparse
    /// grass. Plants run from the palette Ground at the base to Accent at the tips,
    /// sway in one wind that travels across the street, and grow in with the sweep.
    /// Each ring keeps to its lane (half the column pitch sideways) and stops
    /// short of the street midline: items near those lines shrink toward their
    /// base and fade out, so facing and neighbouring rings never meet.
    /// The side of each ring facing the stage camera stays short and sparse, and
    /// ankle-low along the near edge of the disc. Every item's top is clamped by
    /// its own monster's card and by every neighbour's (a Set card is wider than
    /// a lane, and rolled Defense art lies across the next one): each item is
    /// capped at its worst point against each nearby card (a same-row neighbour's
    /// art as the monster's own, one across the aisle within
    /// <see cref="ArFieldSignature.CoverLimitAll"/>'s reach), and each vertex is held
    /// under <see cref="ArFieldSignature.CoverLimitAll"/>, so upright art keeps
    /// all but its feet clear and sideways (Defense) art keeps its middle clear.
    /// While a monster is Set (or its flip is still animating) the items over its
    /// card, from its own ring or a neighbour's, lie flat under it; once the card
    /// stands up they rise again over 0.6 s.
    /// Boon/bane shows only once the card stands. One dynamic mesh (one draw
    /// call): camera-facing tufts, flat fronds and view-aligned root ribbons over
    /// a small procedural atlas.
    /// Scope: per-monster.
    /// </summary>
    public sealed class ArFieldSigGroundCover : ArFieldSignature
    {
        const int MaxAnchors = 10;
        const float TwoPi = Mathf.PI * 2f;
        const float Golden = 0.618034f;

        /// <summary>The summon ring's opaque disc is 0.45 × Scale: every item roots outside this radius.</summary>
        const float DiscClear = 0.47f;
        /// <summary>Each item's worst case (width + lean + full sway) ends inside this radius (× Scale).</summary>
        const float DesignRadius = 0.72f;
        /// <summary>Hard per-vertex guards (× Scale), just inside the contract.</summary>
        const float GuardRadius = MaxAnchorRadius * 0.99f;
        const float GuardHeight = MaxAnchorHeight * 0.98f;
        /// <summary>Mesh bounds = street box plus this, so rings at the street edge are never culled.</summary>
        static readonly Vector3 BoundsPad = new Vector3(1.6f, 0.6f, 1.6f);

        // Camera side of each ring: items there shrink and thin out.
        const float FrontCosFrom = -0.2f;
        const float FrontCosTo = 0.6f;
        /// <summary>Width of the fade band (1 / this) as the cull threshold passes an item's Keep.</summary>
        const float FrontFadeSharpness = 10f;

        // Near edge of the summon disc: camera-side items rooted inside NearDiscRadius stay under
        // NearDiscHeight (× Scale), easing in from front share NearDiscFrontFrom to NearDiscFrontTo
        // and out between NearDiscRadius and NearDiscFadeTo.
        const float NearDiscFrontFrom = 0.35f;
        const float NearDiscFrontTo = 0.5f;
        const float NearDiscRadius = 0.55f;
        const float NearDiscFadeTo = 0.6f;
        const float NearDiscHeight = 0.05f;

        /// <summary>
        /// Lane / aisle fit: the share of its size an item keeps so its worst case clears
        /// both lines. Hidden at or below FitHide, fully opaque from FitShow.
        /// </summary>
        const float FitHide = 0.25f;
        const float FitShow = 0.5f;

        // One wind for the whole street: a steady lean plus a gust wave.
        const float GustLean = 0.08f;
        const float GustSpeed = 1.3f;
        const float GustWaveNumber = 1.8f;
        const float GustVariation = 0.3f;
        const float GustDrift = 0.21f;

        const float GrowFloor = 0.15f;
        const float PlantOpacity = 0.9f;
        const float PlantBaseAlpha = 0.55f;
        const float BaneHeight = 0.8f;
        const float BaneDrain = 0.45f;
        /// <summary>Bane roots darken toward this share of their bark colour (mixed in sRGB).</summary>
        const float BaneBark = 0.7f;
        const float BoonGlow = 0.25f;

        /// <summary>Under a Set card an item's top lies at this share of SetCardClearHeight.</summary>
        const float SetClearMargin = 0.8f;
        const float SetLieHeight = SetCardClearHeight * SetClearMargin;
        /// <summary>Seconds for the items over a Set card to stand back up once the card has stood up.</summary>
        const float FlipRiseSeconds = 0.6f;
        /// <summary>An item lies within this × its reach of its base, square box corners included (√2, rounded up).</summary>
        const float BoxCorner = 1.415f;
        /// <summary>
        /// Slack (× Scale) past a card's sideways art or Set-card corner, as in
        /// <see cref="ArFieldSignature.CoverLimitAll"/>: beyond this its card constrains nothing.
        /// </summary>
        const float CardReachPad = 0.05f;
        /// <summary>
        /// Neighbours within this z (× Scale) share the monster's row. Their face-up art caps
        /// items anywhere on its camera side inside its lateral span, as the monster's own art
        /// does. The camera side of art across the aisle spans the whole near row, so there it
        /// counts only within <see cref="ArFieldSignature.CoverLimitAll"/>'s reach.
        /// </summary>
        const float SameRowZ = 0.3f;

        /// <summary>Tufts are sheared quads: the texture carries the blades, the top row carries lean and sway.</summary>
        const int TuftRows = 2;

        // Variant 0: meadow grass.
        const int MeadowTufts = 69;
        const float MeadowInner = DiscClear;
        const float MeadowHeightMin = 0.16f;
        const float MeadowHeightMax = 0.30f;
        const float MeadowWidthMin = 0.13f;
        const float MeadowWidthMax = 0.20f;
        const float MeadowLean = 0.12f;
        const float MeadowSway = 0.12f;
        const float MeadowFrontCull = 0.3f;
        const float MeadowFrontHeight = 0.5f;

        // Variant 1: fern crowns over leafy undergrowth.
        const int FernCrowns = 6;
        const int FrondsPerCrown = 3;
        const int FrondRows = 5;
        /// <summary>Crowns sit on the disc's edge, up to FernCrownJitter further out.</summary>
        const float FernCrownRadius = DiscClear;
        const float FernCrownJitter = 0.03f;
        /// <summary>Design reach; each frond is shortened until it ends inside <see cref="DesignRadius"/>.</summary>
        const float FrondReachMin = 0.19f;
        const float FrondReachMax = 0.25f;
        const float FrondHeightMin = 0.24f;
        const float FrondHeightMax = 0.34f;
        const float FrondWidthMin = 0.11f;
        const float FrondWidthMax = 0.15f;
        const float FrondSpreadDeg = 55f;
        const float FrondSway = 0.07f;
        const float FernFrontCull = 0.3f;
        const float FernFrontHeight = 0.35f;
        const int LeafClumps = 38;
        const float LeafInner = DiscClear;
        const float LeafHeightMin = 0.18f;
        const float LeafHeightMax = 0.28f;
        const float LeafWidthMin = 0.12f;
        const float LeafWidthMax = 0.17f;
        const float LeafLean = 0.08f;
        const float LeafSway = 0.08f;
        const float LeafFrontCull = 0.35f;
        const float LeafFrontHeight = 0.7f;

        // Variant 2: roots breaking the ground, sparse grass between them.
        const int Roots = 11;
        const int RootRows = 9;
        const float RootInnerMin = DiscClear;
        const float RootInnerMax = 0.50f;
        /// <summary>Design length; each root is shortened until it ends inside <see cref="DesignRadius"/>.</summary>
        const float RootLengthMin = 0.24f;
        const float RootLengthMax = 0.32f;
        /// <summary>Heading off the radial (degrees, either way), so roots splay round the disc.</summary>
        const float RootTurnMin = 10f;
        const float RootTurnMax = 35f;
        /// <summary>Sideways drift of the root tip (host-local metres).</summary>
        const float RootCurve = 0.06f;
        const float RootWiggle = 0.02f;
        const float RootHeightMin = 0.08f;
        const float RootHeightMax = 0.15f;
        /// <summary>Full ribbon width at the trunk end.</summary>
        const float RootWidthMin = 0.08f;
        const float RootWidthMax = 0.11f;
        const float RootTipWidth = 0.35f;
        const float RootKnob = 0.12f;
        /// <summary>Share of roots that sink mid-way and break out again.</summary>
        const float RootTwinShare = 0.3f;
        const float RootEndFade = 0.14f;
        const float RootFrontHeight = 0.4f;
        const float RootPulseSpeed = 0.4f;
        const float RootPulseWidth = 0.18f;
        const float RootPulseGlow = 0.75f;
        const float RootOpacity = 0.92f;
        const int RootGrass = 34;
        const float RootGrassInner = DiscClear;
        const float RootGrassHeightMin = 0.10f;
        const float RootGrassHeightMax = 0.20f;
        const float RootGrassWidthMin = 0.10f;
        const float RootGrassWidthMax = 0.15f;
        const float RootGrassLean = 0.15f;
        const float RootGrassSway = 0.12f;
        const float RootGrassFrontCull = 0.4f;
        const float RootGrassFrontHeight = 0.5f;

        // Atlas: four 64 px columns (grass tuft, leaf clump, fern frond, bark), base at v = 0.
        const int AtlasW = 256;
        const int AtlasH = 128;
        const int RegionW = 64;
        const int AtlasMips = 4;
        const int RegionGrass = 0;
        const int RegionLeaf = 1;
        const int RegionFrond = 2;
        const int RegionBark = 3;
        const int GrassBlades = 14;
        const int LeafCount = 7;
        const int Pinnae = 14;
        /// <summary>Frond length over width, so leaflets are drawn undistorted.</summary>
        const float FrondAspect = 3f;

        enum Kind
        {
            Tuft,  // camera-facing clump (billboard about +Y)
            Frond, // flat arching strip, leaflets spread horizontally
            Root   // view-aligned ribbon along a ground arc
        }

        /// <summary>A run of same-shaped items inside each anchor's vertex block.</summary>
        struct Block
        {
            public Kind Kind;
            public int Region;
            public int First;
            public int Count;
            public int Rows;
            public int VertStart;
            public float Sway;
            public float FrontCull;
            public float FrontHeight;
        }

        /// <summary>One tuft / frond / root of the ring template every anchor shares.</summary>
        struct Blade
        {
            public float DirX;    // base direction from the anchor (template frame)
            public float DirZ;
            public float Radius;  // base distance from the anchor (host-local metres)
            public float HeadCos; // heading relative to the radial
            public float HeadSin;
            public int Row;       // first row in _shape / _halfWidth
            public float Size;    // sway length (host-local metres)
            public float Top;     // highest point of the shape (host-local metres)
            public float Along;   // worst reach from the base along the heading (host-local metres)
            public float Spread;  // worst reach from the base in any direction: width, sway, drift
            public float Rim;     // view-aligned ribbon half width that can stand above the shape (roots)
            public float Phase;   // radians (sway) or 0…1 (root pulse)
            public float Freq;
            public float Keep;    // camera-side items with Keep under the cull share fade out
            public float Mirror;  // ±1 texture flip for tufts
            public Color Base;
            public Color Mid;
            public Color Tip;
            public Color Dark;    // bane-drained base (roots)
        }

        struct TuftSpec
        {
            public float Inner;
            public float HeightMin;
            public float HeightMax;
            public float WidthMin;
            public float WidthMax;
            public float Lean;
            public Color Base;
            public Color Tip;
            public Color Alt; // tips drift toward this, per tuft
        }

        readonly Block[] _blocks = new Block[2];
        readonly int[] _order = new int[MaxAnchors];
        readonly float[] _dist = new float[MaxAnchors];
        // Lie-down state (1 = flat under a Set card) per monster, matched by Key against last
        // frame's anchors so an ease survives others coming and going. Swapped each frame.
        // This frame's (_curDown, by slot) also lays a neighbour's items down over that card.
        int[] _prevKey = new int[MaxAnchors];
        float[] _prevDown = new float[MaxAnchors];
        int[] _curKey = new int[MaxAnchors];
        float[] _curDown = new float[MaxAnchors];
        int _prevCount;
        // This frame's anchors, set in Tick for the write loop and cleared after it (never kept),
        // with each slot's flat direction to the eye, and the neighbours whose cards can reach
        // the ring being written (slots).
        IReadOnlyList<FieldAnchor> _anchors;
        readonly float[] _slotCamX = new float[MaxAnchors];
        readonly float[] _slotCamZ = new float[MaxAnchors];
        readonly int[] _near = new int[MaxAnchors];
        int _nearCount;
        /// <summary>Farthest any item's reach box gets from its anchor (host-local metres).</summary>
        float _ringReach;
        int _blockCount;
        Blade[] _blades;
        int _bladeCursor;
        int _rowCursor;
        /// <summary>Per row: x along the heading, y up, z sideways (host-local metres).</summary>
        Vector3[] _shape;
        float[] _halfWidth;
        Vector3[] _rowP;
        int _anchorVerts;

        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        int _slotsLive;
        Vector3 _wind;
        float _windClock;
        FieldStreet _street;

        // Frame of the anchor being written (set by WriteAnchor, read by WriteBlade / Contain).
        FieldAnchor _anchor;
        /// <summary>The same monster with its card lying flat: tests an item against the Set footprint during the rise too.</summary>
        FieldAnchor _card;
        Vector3 _pos;
        float _sc;
        float _spinCos;
        float _spinSin;
        float _camX;
        float _camZ;
        Vector3 _right;
        Vector3 _view;
        float _lift;
        float _alpha;
        float _gust;
        float _phase;
        float _time;
        int _aura;
        float _down;
        /// <summary>Floor metres an item may reach sideways, and toward the street midline (+z = _towardMid).</summary>
        float _laneReach;
        float _midReach;
        float _towardMid;
        // Item being written: its height cap, and (roots) how much the near-disc cap applies per vertex.
        float _itemTop;
        float _nearFront;

        float WidthScale => 0.75f + 0.25f * SigScale;

        protected override void Build()
        {
            var density = Mathf.Clamp01(0.6f + 0.8f * (SigScale - 0.5f));
            var windAngle = Hash01(Variant, 101) * TwoPi;
            _wind = new Vector3(Mathf.Cos(windAngle), 0f, Mathf.Sin(windAngle));
            var sky = Env.Sky;
            var ground = Env.Ground;
            var accent = Env.Accent;

            switch (Variant)
            {
                case 1:
                {
                    var leaves = Scaled(LeafClumps, density);
                    var crowns = Mathf.Max(3, Scaled(FernCrowns, density));
                    Allocate(leaves + crowns * FrondsPerCrown,
                        leaves * TuftRows + crowns * FrondsPerCrown * FrondRows);
                    ref var low = ref AddBlock(Kind.Tuft, RegionLeaf, leaves, TuftRows, LeafSway,
                        LeafFrontCull, LeafFrontHeight);
                    FillTufts(ref low, new TuftSpec
                    {
                        Inner = LeafInner, HeightMin = LeafHeightMin, HeightMax = LeafHeightMax,
                        WidthMin = LeafWidthMin, WidthMax = LeafWidthMax, Lean = LeafLean,
                        Base = ground * 0.7f,
                        Tip = Color.Lerp(accent, Color.white, 0.2f),
                        Alt = Color.Lerp(ground, sky, 0.5f)
                    }, 20);
                    ref var ferns = ref AddBlock(Kind.Frond, RegionFrond, crowns * FrondsPerCrown, FrondRows,
                        FrondSway, FernFrontCull, FernFrontHeight);
                    FillFerns(ref ferns, crowns, Color.Lerp(sky, ground, 0.5f) * 0.8f,
                        Color.Lerp(ground, accent, 0.35f), accent, 40);
                    break;
                }
                case 2:
                {
                    var grass = Scaled(RootGrass, density);
                    var roots = Mathf.Max(5, Scaled(Roots, density));
                    Allocate(roots + grass, roots * RootRows + grass * TuftRows);
                    ref var arcs = ref AddBlock(Kind.Root, RegionBark, roots, RootRows, 0f, 0f, RootFrontHeight);
                    FillRoots(ref arcs, Color.Lerp(ground, Color.black, 0.25f),
                        Color.Lerp(ground, Color.white, 0.1f), Color.Lerp(accent, Color.white, 0.35f), 60);
                    ref var tufts = ref AddBlock(Kind.Tuft, RegionGrass, grass, TuftRows, RootGrassSway,
                        RootGrassFrontCull, RootGrassFrontHeight);
                    FillTufts(ref tufts, new TuftSpec
                    {
                        Inner = RootGrassInner, HeightMin = RootGrassHeightMin, HeightMax = RootGrassHeightMax,
                        WidthMin = RootGrassWidthMin, WidthMax = RootGrassWidthMax, Lean = RootGrassLean,
                        Base = Color.Lerp(sky, ground, 0.3f),
                        Tip = Color.Lerp(accent, Color.white, 0.12f),
                        Alt = Color.Lerp(accent, ground, 0.4f)
                    }, 80);
                    break;
                }
                default:
                {
                    var n = Scaled(MeadowTufts, density);
                    Allocate(n, n * TuftRows);
                    ref var meadow = ref AddBlock(Kind.Tuft, RegionGrass, n, TuftRows, MeadowSway,
                        MeadowFrontCull, MeadowFrontHeight);
                    FillTufts(ref meadow, new TuftSpec
                    {
                        Inner = MeadowInner, HeightMin = MeadowHeightMin, HeightMax = MeadowHeightMax,
                        WidthMin = MeadowWidthMin, WidthMax = MeadowWidthMax, Lean = MeadowLean,
                        Base = ground * 0.55f,
                        Tip = Color.Lerp(accent, Color.white, 0.22f),
                        Alt = Color.Lerp(accent, sky, 0.35f)
                    }, 0);
                    break;
                }
            }

            // Every mix above (and the bane darkening) is in sRGB, as designed; convert once for the Linear project.
            _ringReach = 0f;
            for (var i = 0; i < _blades.Length; i++)
            {
                ref var b = ref _blades[i];
                _ringReach = Mathf.Max(_ringReach, b.Radius + (b.Along + b.Spread) * BoxCorner);
                b.Dark = VertexColor(b.Base * BaneBark);
                b.Base = VertexColor(b.Base);
                b.Mid = VertexColor(b.Mid);
                b.Tip = VertexColor(b.Tip);
            }

            BuildMesh();
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
            if (_mr == null) return;
            var count = anchors == null ? 0 : Mathf.Min(anchors.Count, MaxAnchors);
            if (level <= 0.001f || count == 0)
            {
                _prevCount = 0;
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            var t = Time.unscaledTime;
            _time = t;
            _windClock += dt * (1f + GustVariation * Mathf.Sin(t * GustDrift));
            _street = street;
            var eye = street.HasCamera
                ? street.Camera
                : new Vector3(street.Center.x, street.Center.y, street.Center.z - street.Half.z - 1f);

            // Every monster's lie-down ease, even ones drawn nowhere this frame, so none loses its state.
            // Far monsters first so nearer rings blend over them.
            for (var i = 0; i < count; i++)
            {
                var a = anchors[i];
                _curKey[i] = a.Key;
                _curDown[i] = LieDown(a, dt);
                var toEye = eye - a.Position;
                var flat2 = toEye.x * toEye.x + toEye.z * toEye.z;
                var flat = Mathf.Sqrt(flat2);
                _slotCamX[i] = flat > 1e-4f ? toEye.x / flat : 0f;
                _slotCamZ[i] = flat > 1e-4f ? toEye.z / flat : -1f;
                _dist[i] = flat2;
                _order[i] = i;
            }

            for (var i = 1; i < count; i++)
            {
                var key = _order[i];
                var j = i - 1;
                while (j >= 0 && _dist[_order[j]] < _dist[key])
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = key;
            }

            // Neighbours' cards are read while writing each ring; the list is the caller's, so drop it after.
            _anchors = anchors;
            var drawn = 0;
            for (var k = 0; k < count; k++)
            {
                var slot = _order[k];
                var a = anchors[slot];
                var vis = Mathf.Clamp01(a.Presence) * level;
                if (vis <= 0.001f || a.Scale <= 1e-4f) continue;
                WriteAnchor(drawn * _anchorVerts, slot, count, a, vis, _curDown[slot], eye);
                drawn++;
            }

            _anchors = null;

            // This frame's lie-down state becomes next frame's lookup (swap, no allocation).
            var keys = _prevKey;
            _prevKey = _curKey;
            _curKey = keys;
            var downs = _prevDown;
            _prevDown = _curDown;
            _curDown = downs;
            _prevCount = count;

            if (drawn == 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            for (var s = drawn; s < _slotsLive; s++)
                Collapse(s * _anchorVerts, _anchorVerts, street.Center);
            _slotsLive = drawn;

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            _mesh.bounds = new Bounds(street.Center, street.Half * 2f + BoundsPad);
            if (!_mr.enabled) _mr.enabled = true;
        }

        /// <summary>
        /// How far the items over this monster's card lie down: 1 at once while it is Set
        /// (a flip counts as Set until the card stands), then easing back to 0 over
        /// <see cref="FlipRiseSeconds"/>. Looked up by Key in last frame's anchors; a
        /// monster not seen then starts at its target.
        /// </summary>
        float LieDown(in FieldAnchor a, float dt)
        {
            var target = a.FaceDown ? 1f : 0f;
            for (var j = 0; j < _prevCount; j++)
            {
                if (_prevKey[j] != a.Key) continue;
                var was = _prevDown[j];
                return target > was ? target : Mathf.MoveTowards(was, target, dt / FlipRiseSeconds);
            }

            return target;
        }

        /// <summary>
        /// Monster <paramref name="slot"/>'s ring into the vertex run at <paramref name="vStart"/>,
        /// each block back to front. <paramref name="count"/> is how many slots Tick filled.
        /// </summary>
        void WriteAnchor(int vStart, int slot, int count, in FieldAnchor a, float vis, float down, Vector3 eye)
        {
            _anchor = a;
            _card = a;
            _card.FaceDown = true;
            _pos = a.Position;
            // Resting scale: the ground stays put while the monster flinches on a hit.
            _sc = a.Scale;
            _down = down;
            _laneReach = LaneHalfWidth * _sc;
            _midReach = MidlineGap(a, a.Position);
            _towardMid = a.Position.z < 0f ? 1f : -1f;
            var toEye = eye - _pos;
            _camX = _slotCamX[slot];
            _camZ = _slotCamZ[slot];
            _right = new Vector3(-_camZ, 0f, _camX);
            _view = toEye.sqrMagnitude > 1e-8f ? toEye.normalized : Vector3.up;

            // Neighbours whose Set card or face-up art any item of this ring can reach.
            _nearCount = 0;
            for (var j = 0; j < count; j++)
            {
                if (j == slot) continue;
                var b = _anchors[j];
                if (b.Scale <= 1e-4f || b.Key == a.Key) continue;
                var ex = b.Position.x - _pos.x;
                var ez = b.Position.z - _pos.z;
                var within = _ringReach * _sc + CardReach(b);
                if (ex * ex + ez * ez <= within * within) _near[_nearCount++] = j;
            }

            // Seeded by the monster, not its slot or spot: the ring keeps its layout while others come and go or it lunges.
            var spin = Hash01(a.Key, 3) * TwoPi;
            _spinCos = Mathf.Cos(spin);
            _spinSin = Mathf.Sin(spin);
            _phase = Hash01(a.Key, 5) * TwoPi;
            // Boon/bane waits until the card stands: a flip still animating reads as Set.
            _aura = a.FaceDown ? 0 : a.Aura;
            _lift = Mathf.Lerp(GrowFloor, 1f, Mathf.SmoothStep(0f, 1f, vis)) * (_aura < 0 ? BaneHeight : 1f);
            _alpha = vis;
            _gust = Mathf.Sin(_windClock * GustSpeed - (_pos.x * _wind.x + _pos.z * _wind.z) * GustWaveNumber);

            // Template angle pointing away from the camera: emit from there toward it.
            var back = (Mathf.Atan2(-_camZ, -_camX) - spin) / TwoPi;
            back -= Mathf.Floor(back);

            for (var bi = 0; bi < _blockCount; bi++)
            {
                ref var blk = ref _blocks[bi];
                var n = blk.Count;
                var start = Mathf.Min(n - 1, (int)(back * n));
                var stride = blk.Rows * 2;
                var v = vStart + blk.VertStart;
                for (var k = 0; k < n; k++)
                {
                    var step = (k + 1) >> 1;
                    var idx = (k & 1) == 1 ? start + step : start - step;
                    if (idx < 0) idx += n;
                    else if (idx >= n) idx -= n;
                    WriteBlade(blk, blk.First + idx, v + k * stride);
                }
            }
        }

        /// <summary>One item: lane fit, camera-side fade, height caps, wind, then its rows as vertex pairs.</summary>
        void WriteBlade(in Block blk, int i, int v)
        {
            ref var b = ref _blades[i];
            var dx = b.DirX * _spinCos - b.DirZ * _spinSin;
            var dz = b.DirZ * _spinCos + b.DirX * _spinSin;
            var hx = dx * b.HeadCos - dz * b.HeadSin;
            var hz = dz * b.HeadCos + dx * b.HeadSin;
            var ox = dx * b.Radius * _sc;
            var oz = dz * b.Radius * _sc;

            // Lane and aisle: the item shrinks toward its base until its worst case clears both lines.
            var along = b.Along * _sc;
            var spread = b.Spread * _sc;
            var fit = Fit(ox, along * Mathf.Max(0f, hx) + spread, _laneReach);
            fit = Mathf.Min(fit, Fit(-ox, along * Mathf.Max(0f, -hx) + spread, _laneReach));
            fit = Mathf.Min(fit, Fit(_towardMid * oz, along * Mathf.Max(0f, _towardMid * hz) + spread, _midReach));

            // Camera side: shorter and sparser so the monster art stays clear.
            var front = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FrontCosFrom, FrontCosTo, dx * _camX + dz * _camZ));
            var fade = Mathf.Clamp01((b.Keep - blk.FrontCull * front) * FrontFadeSharpness + 1f) *
                       Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FitHide, FitShow, fit));
            if (fade <= 0.001f)
            {
                Collapse(v, blk.Rows * 2, _pos);
                return;
            }

            var size = _sc * fit;
            var lift = _lift * Mathf.Lerp(1f, blk.FrontHeight, front) * Mathf.Lerp(0.6f, 1f, fade);
            var bx = _pos.x + ox;
            var bz = _pos.z + oz;

            // Art cover, tested at the item's most camera-ward point, shifted toward the art's centre.
            var reach = (b.Along + b.Spread) * size;
            var shift = Mathf.Clamp(_anchor.ArtLateral - (ox * _right.x + oz * _right.z), -reach, reach);
            var cap = Mathf.Min(GuardHeight * _sc, CoverLimit(_anchor, _street,
                new Vector3(bx + _camX * reach + _right.x * shift, _pos.y, bz + _camZ * reach + _right.z * shift)));

            // Neighbours' cards (a Set card is wider than a lane; rolled Defense art lies across the
            // next one): each nearby face-up card caps the item at its worst point, the same test in
            // that monster's own camera frame (across the aisle, only within CoverLimitAll's reach);
            // a Set card the item's box reaches lays it down with that card's ease, so it rises as
            // the neighbour stands. Contain holds every vertex under CoverLimitAll as well.
            var lie = 0f;
            var lieScale = _sc;
            for (var n = 0; n < _nearCount; n++)
            {
                var j = _near[n];
                var nb = _anchors[j];
                var ex = bx - nb.Position.x;
                var ez = bz - nb.Position.z;
                var within = CardReach(nb) + reach * BoxCorner;
                if (ex * ex + ez * ez > within * within &&
                    Mathf.Abs(nb.Position.z - _pos.z) >= SameRowZ * _sc) continue;
                var nd = _curDown[j];
                if (nd > 0f)
                {
                    var flat = nb;
                    flat.FaceDown = true;
                    if (InSetCard(flat, new Vector3(Mathf.Clamp(nb.Position.x, bx - reach, bx + reach), _pos.y,
                            Mathf.Clamp(nb.Position.z, bz - reach, bz + reach))))
                    {
                        lie = Mathf.Max(lie, nd);
                        lieScale = Mathf.Min(lieScale, nb.Scale);
                    }
                }

                if (nb.FaceDown || nb.ArtHalf <= 0f) continue;
                var nx = _slotCamX[j];
                var nz = _slotCamZ[j];
                var nShift = Mathf.Clamp(nb.ArtLateral - (ex * -nz + ez * nx), -reach, reach);
                var lim = CoverLimit(nb, _street,
                    new Vector3(bx + nx * reach - nz * nShift, _pos.y, bz + nz * reach + nx * nShift));
                // CoverLimit also carries that monster's plain height cap; only its card counts here.
                if (lim < MaxAnchorHeight * nb.Scale) cap = Mathf.Min(cap, lim);
            }

            // Near edge of the summon disc: camera-side items rooted close to it stay ankle-low.
            // Roots apply it per vertex instead, so they dive under the disc edge and break out beyond it.
            var near = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearDiscFrontFrom, NearDiscFrontTo, front));
            _nearFront = 0f;
            if (blk.Kind == Kind.Root)
                _nearFront = near;
            else
                cap = Mathf.Min(cap, Mathf.Lerp(cap, NearDiscHeight * _sc,
                    near * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearDiscRadius, NearDiscFadeTo, b.Radius)))));

            var top = Mathf.Max(b.Top * size, 1e-5f);
            lift = Mathf.Min(lift, Mathf.Max(0f, cap - b.Rim * size) / top);

            // Over a Set card's footprint (its own monster's or a neighbour's, above) the item lies
            // down to a low skirt, shape and all (sway scales with lift too), and stands back up
            // after the card does. Tested at the point of its worst case nearest the card's centre;
            // items in the strips before and behind the card stand as usual.
            if (_down > 0f)
            {
                var sx = Mathf.Sign(ox) * Mathf.Max(0f, Mathf.Abs(ox) - reach);
                var sz = Mathf.Sign(oz) * Mathf.Max(0f, Mathf.Abs(oz) - reach);
                if (InSetCard(_card, new Vector3(_pos.x + sx, _pos.y, _pos.z + sz))) lie = Mathf.Max(lie, _down);
            }

            if (lie > 0f) lift = Mathf.Lerp(lift, Mathf.Min(lift, SetLieHeight * lieScale / top), lie);
            _itemTop = Mathf.Lerp(cap, Mathf.Min(cap, SetCardClearHeight * lieScale), lie);

            var sway = 0f;
            if (blk.Kind != Kind.Root)
            {
                var osc = 0.55f * Mathf.Sin(_time * b.Freq + b.Phase + _phase) + 0.45f * _gust;
                sway = (blk.Sway * osc + GustLean * (0.5f + 0.5f * _gust)) * b.Size * size * lift;
            }

            var last = blk.Rows - 1;
            for (var j = 0; j <= last; j++)
            {
                var q = _shape[b.Row + j];
                var s = j / (float)last;
                var bend = sway * s * s;
                _rowP[j] = new Vector3(
                    bx + (hx * q.x - hz * q.z) * size + _wind.x * bend,
                    _pos.y + q.y * size * lift,
                    bz + (hz * q.x + hx * q.z) * size + _wind.z * bend);
            }

            var alpha = _alpha * fade;
            var across = _right;
            for (var j = 0; j <= last; j++)
            {
                var s = j / (float)last;
                var hw = _halfWidth[b.Row + j] * size;
                Vector3 w;
                Color32 c;
                switch (blk.Kind)
                {
                    case Kind.Tuft:
                        w = _right * (hw * b.Mirror);
                        c = PlantColour(b, s, alpha);
                        break;
                    case Kind.Frond:
                        w = new Vector3(-hz * hw, 0f, hx * hw);
                        c = PlantColour(b, s, alpha);
                        break;
                    default:
                    {
                        // Ribbon across the view so the root reads as a round tube from any side.
                        // Keep the side continuous so a root aimed at the camera never folds over.
                        var tangent = _rowP[j < last ? j + 1 : j] - _rowP[j > 0 ? j - 1 : j];
                        var cross = Vector3.Cross(tangent, _view);
                        var m = cross.magnitude;
                        if (m > 1e-6f)
                        {
                            cross /= m;
                            across = Vector3.Dot(cross, across) < 0f ? -cross : cross;
                        }

                        w = across * hw;
                        c = RootColour(b, s, alpha);
                        break;
                    }
                }

                _verts[v] = Contain(_rowP[j] - w);
                _verts[v + 1] = Contain(_rowP[j] + w);
                _cols[v] = c;
                _cols[v + 1] = c;
                v += 2;
            }
        }

        /// <summary>
        /// Share of its size an item keeps so that, from base offset <paramref name="offset"/>
        /// (along the line's normal) with worst reach <paramref name="reach"/> toward the line,
        /// it stays within <paramref name="limit"/>; 0 when its base is already past the line.
        /// </summary>
        static float Fit(float offset, float reach, float limit)
        {
            var room = limit - offset;
            if (room <= 0f) return 0f;
            return reach > room ? room / reach : 1f;
        }

        /// <summary>Base → Mid → Tip up the plant; a bane drains it, a boon lights the tips.</summary>
        Color32 PlantColour(in Blade b, float s, float alpha)
        {
            var c = s < 0.5f ? Color.Lerp(b.Base, b.Mid, s * 2f) : Color.Lerp(b.Mid, b.Tip, s * 2f - 1f);
            if (_aura < 0) c = Color.Lerp(c, b.Base, BaneDrain);
            else if (_aura > 0) c = Color.Lerp(c, Color.white, BoonGlow * s);
            c.a = PlantOpacity * Mathf.Lerp(PlantBaseAlpha, 1f, Mathf.Clamp01(s * 3f)) * alpha;
            return c;
        }

        /// <summary>Bark with a band of accent light running out from the monster.</summary>
        Color32 RootColour(in Blade b, float s, float alpha)
        {
            var p = _time * RootPulseSpeed + b.Phase;
            p = (p - Mathf.Floor(p)) * (1f + 2f * RootPulseWidth) - RootPulseWidth;
            var g = Mathf.Clamp01(1f - Mathf.Abs(s - p) / RootPulseWidth);
            g = g * g * (3f - 2f * g) * RootPulseGlow * (_aura > 0 ? 1.3f : _aura < 0 ? 0.3f : 1f);
            var c = Color.Lerp(Color.Lerp(b.Base, b.Mid, s), b.Tip, Mathf.Clamp01(g));
            if (_aura < 0) c = Color.Lerp(c, b.Dark, BaneDrain);
            var ends = Mathf.SmoothStep(0f, 1f, s / RootEndFade) * Mathf.SmoothStep(0f, 1f, (1f - s) / RootEndFade);
            c.a = RootOpacity * ends * alpha;
            return c;
        }

        /// <summary>
        /// How far round monster <paramref name="b"/> its card can constrain a piece: past its
        /// sideways art or its Set card's corner, the radius <see cref="ArFieldSignature.CoverLimitAll"/> checks.
        /// </summary>
        static float CardReach(in FieldAnchor b) =>
            Mathf.Max(2f * b.ArtHalf, SetCardClearRadius * b.Scale) + CardReachPad * b.Scale;

        /// <summary>
        /// Backstop for one vertex of the item being written: inside the per-monster radius,
        /// its lane and its side of the midline, under the item's cap, under every card's
        /// <see cref="ArFieldSignature.CoverLimitAll"/> (its own monster's and its neighbours'),
        /// and (roots) low over the near edge of the summon disc.
        /// </summary>
        Vector3 Contain(Vector3 p)
        {
            var dx = p.x - _pos.x;
            var dz = p.z - _pos.z;
            var r = GuardRadius * _sc;
            var d2 = dx * dx + dz * dz;
            if (d2 > r * r)
            {
                var k = r / Mathf.Sqrt(d2);
                dx *= k;
                dz *= k;
                p.z = _pos.z + dz;
                d2 = r * r;
            }

            p.x = _pos.x + Mathf.Clamp(dx, -_laneReach, _laneReach);
            var gap = MidlineGap(_anchor, p);
            if (gap < 0f) p.z += _towardMid * gap;

            // Its own card and every neighbour's: under a Set card, low in front of face-up art.
            var top = Mathf.Min(Mathf.Min(GuardHeight * _sc, _itemTop), CoverLimitAll(_anchor, _anchors, _street, p));
            if (_nearFront > 0f)
            {
                var edge = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(NearDiscRadius, NearDiscFadeTo, Mathf.Sqrt(d2) / _sc));
                top = Mathf.Lerp(top, Mathf.Min(top, NearDiscHeight * _sc), _nearFront * edge);
            }

            p.y = Mathf.Clamp(p.y, _pos.y, _pos.y + top);
            return p;
        }

        /// <summary>Zero-area, fully transparent vertices (hidden items and unused anchor slots).</summary>
        void Collapse(int v, int n, Vector3 at)
        {
            for (var i = v; i < v + n; i++)
            {
                _verts[i] = at;
                _cols[i] = default;
            }
        }

        static int Scaled(int n, float density) => Mathf.Max(4, Mathf.RoundToInt(n * density));

        // ── Template ────────────────────────────────────────────────────────

        void Allocate(int blades, int rows)
        {
            _blades = new Blade[blades];
            _shape = new Vector3[rows];
            _halfWidth = new float[rows];
        }

        ref Block AddBlock(Kind kind, int region, int count, int rows, float sway, float frontCull, float frontHeight)
        {
            ref var b = ref _blocks[_blockCount++];
            b.Kind = kind;
            b.Region = region;
            b.First = _bladeCursor;
            b.Count = count;
            b.Rows = rows;
            b.Sway = sway;
            b.FrontCull = frontCull;
            b.FrontHeight = frontHeight;
            return ref b;
        }

        ref Blade NextBlade(int rows)
        {
            ref var b = ref _blades[_bladeCursor++];
            b.Row = _rowCursor;
            b.HeadCos = 1f;
            b.Mirror = 1f;
            b.Keep = 1f;
            _rowCursor += rows;
            return ref b;
        }

        /// <summary>
        /// Camera-facing clumps leaning outward, rooted between <c>spec.Inner</c> and the
        /// radius where half width + lean + full sway still ends inside <see cref="DesignRadius"/>
        /// (a clump too big for the band is scaled down to fit it).
        /// </summary>
        void FillTufts(ref Block blk, in TuftSpec spec, int salt)
        {
            var n = blk.Count;
            var room = DesignRadius - spec.Inner;
            for (var i = 0; i < n; i++)
            {
                var h = Mathf.Lerp(spec.HeightMin, spec.HeightMax, Hash01(i, salt)) * SigScale;
                var w = Mathf.Lerp(spec.WidthMin, spec.WidthMax, Hash01(i, salt + 1)) * WidthScale;
                var lean = spec.Lean * Mathf.Lerp(0.4f, 1f, Hash01(i, salt + 2));
                var along = lean * h;
                var spread = w * 0.5f + (blk.Sway + GustLean) * h;
                if (along + spread > room)
                {
                    var k = room / (along + spread);
                    h *= k;
                    w *= k;
                    along *= k;
                    spread *= k;
                }

                var outer = Mathf.Max(spec.Inner, DesignRadius - along - spread);
                var r01 = i * Golden + 0.25f * Hash01(i, salt + 3);
                r01 -= Mathf.Floor(r01);
                var angle = (i + Mathf.Lerp(0.15f, 0.85f, Hash01(i, salt + 4))) / n * TwoPi;
                var tone = Hash01(i, salt + 5);

                ref var b = ref NextBlade(blk.Rows);
                b.DirX = Mathf.Cos(angle);
                b.DirZ = Mathf.Sin(angle);
                b.Radius = Mathf.Lerp(spec.Inner, outer, r01);
                b.Size = h;
                b.Top = h;
                b.Along = along;
                b.Spread = spread;
                b.Phase = Hash01(i, salt + 6) * TwoPi;
                b.Freq = Mathf.Lerp(1.5f, 2.6f, Hash01(i, salt + 7));
                b.Keep = Hash01(i, salt + 8);
                b.Mirror = Hash01(i, salt + 9) < 0.5f ? -1f : 1f;
                b.Base = spec.Base;
                b.Tip = Color.Lerp(spec.Tip, spec.Alt, 0.5f * tone);
                b.Mid = Color.Lerp(b.Base, b.Tip, 0.5f);
                for (var j = 0; j < blk.Rows; j++)
                {
                    var s = j / (float)(blk.Rows - 1);
                    _shape[b.Row + j] = new Vector3(lean * h * s * s, h * s, 0f);
                    _halfWidth[b.Row + j] = w * 0.5f;
                }
            }
        }

        /// <summary>
        /// Fern crowns on the disc's edge: one upright centre frond, two side fronds
        /// arching out round the ring and drooping. Each frond is shortened (and a
        /// little lowered) until its tip, half width and full sway end inside
        /// <see cref="DesignRadius"/>, so the outward centre frond is the shortest.
        /// </summary>
        void FillFerns(ref Block blk, int crowns, Color baseC, Color midC, Color tipC, int salt)
        {
            var rows = blk.Rows;
            var give = blk.Sway + GustLean;
            for (var c = 0; c < crowns; c++)
            {
                var angle = (c + Mathf.Lerp(0.2f, 0.8f, Hash01(c, salt))) / crowns * TwoPi;
                var radius = FernCrownRadius + FernCrownJitter * Hash01(c, salt + 11);
                for (var f = 0; f < FrondsPerCrown; f++)
                {
                    var i = c * FrondsPerCrown + f;
                    var side = Mathf.Abs(f - (FrondsPerCrown - 1) * 0.5f);
                    var turn = ((f - (FrondsPerCrown - 1) * 0.5f) * FrondSpreadDeg +
                                Mathf.Lerp(-10f, 10f, Hash01(i, salt + 1))) * Mathf.Deg2Rad;
                    var design = Mathf.Lerp(FrondReachMin, FrondReachMax, Hash01(i, salt + 2));
                    var w = Mathf.Lerp(FrondWidthMin, FrondWidthMax, Hash01(i, salt + 4)) * WidthScale;
                    var reach = FitFrond(radius, Mathf.Cos(turn), Mathf.Sin(turn), design, w * 0.5f, give);
                    var height = Mathf.Lerp(FrondHeightMin, FrondHeightMax, Hash01(i, salt + 3)) * SigScale *
                                 (1f - 0.2f * side) * Mathf.Sqrt(reach / design);

                    ref var b = ref NextBlade(rows);
                    b.DirX = Mathf.Cos(angle);
                    b.DirZ = Mathf.Sin(angle);
                    b.Radius = radius;
                    b.HeadCos = Mathf.Cos(turn);
                    b.HeadSin = Mathf.Sin(turn);
                    b.Size = reach;
                    b.Top = height;
                    b.Along = reach;
                    b.Spread = w * 0.5f + give * reach;
                    b.Phase = Hash01(i, salt + 5) * TwoPi;
                    b.Freq = Mathf.Lerp(1.0f, 1.5f, Hash01(i, salt + 6));
                    b.Keep = Hash01(i, salt + 7);
                    b.Base = baseC;
                    b.Mid = midC;
                    b.Tip = Color.Lerp(tipC, midC, 0.4f * Hash01(i, salt + 8));
                    var up0 = Mathf.Lerp(68f, 84f, Hash01(i, salt + 9)) - 8f * side;
                    var up1 = Mathf.Lerp(-15f, 10f, Hash01(i, salt + 10)) - 15f * side;
                    ArchShape(b.Row, rows, reach, height, up0, up1);
                    for (var j = 0; j < rows; j++)
                        _halfWidth[b.Row + j] = w * 0.5f;
                }
            }
        }

        /// <summary>
        /// Longest reach up to <paramref name="reach"/> for a straight frond from a crown at
        /// <paramref name="radius"/>, heading (cos, sin) off the radial, whose tip plus
        /// <paramref name="pad"/> + <paramref name="give"/> × reach stays inside <see cref="DesignRadius"/>.
        /// </summary>
        static float FitFrond(float radius, float cos, float sin, float reach, float pad, float give)
        {
            for (var it = 0; it < 40; it++)
            {
                var x = radius + reach * cos;
                var z = reach * sin;
                if (Mathf.Sqrt(x * x + z * z) + pad + give * reach <= DesignRadius) break;
                reach *= 0.97f;
            }

            return reach;
        }

        /// <summary>Centre line whose pitch eases from <paramref name="up0"/> to <paramref name="up1"/> degrees, fitted to reach × height.</summary>
        void ArchShape(int row, int rows, float reach, float height, float up0, float up1)
        {
            float x = 0f, y = 0f, maxY = 1e-4f;
            _shape[row] = Vector3.zero;
            for (var j = 1; j < rows; j++)
            {
                var pitch = Mathf.Lerp(up0, up1, (j - 0.5f) / (rows - 1)) * Mathf.Deg2Rad;
                x += Mathf.Cos(pitch);
                y += Mathf.Sin(pitch);
                _shape[row + j] = new Vector3(x, y, 0f);
                maxY = Mathf.Max(maxY, y);
            }

            for (var j = 1; j < rows; j++)
            {
                var q = _shape[row + j];
                _shape[row + j] = new Vector3(q.x / x * reach, Mathf.Max(0f, q.y) / maxY * height, 0f);
            }
        }

        /// <summary>
        /// Roots breaking out at the summon disc's edge and splaying round it: a low
        /// arch (some sink mid-way and break out again), drifting sideways, thick at
        /// the trunk end. Each is shortened until every row ends inside <see cref="DesignRadius"/>.
        /// </summary>
        void FillRoots(ref Block blk, Color bark, Color barkTip, Color glow, int salt)
        {
            var n = blk.Count;
            var rows = blk.Rows;
            for (var i = 0; i < n; i++)
            {
                var angle = (i + Mathf.Lerp(0.2f, 0.8f, Hash01(i, salt))) / n * TwoPi;
                var length = Mathf.Lerp(RootLengthMin, RootLengthMax, Hash01(i, salt + 1));
                var curve = Mathf.Lerp(-RootCurve, RootCurve, Hash01(i, salt + 2));
                var ph = Hash01(i, salt + 3) * TwoPi;
                var h = Mathf.Lerp(RootHeightMin, RootHeightMax, Hash01(i, salt + 4)) * SigScale;
                var twin = Hash01(i, salt + 5) < RootTwinShare;
                var w0 = Mathf.Lerp(RootWidthMin, RootWidthMax, Hash01(i, salt + 6)) * WidthScale * 0.5f;
                var radius = Mathf.Lerp(RootInnerMin, RootInnerMax, Hash01(i, salt + 7));
                var turn = Mathf.Lerp(RootTurnMin, RootTurnMax, Hash01(i, salt + 10)) * Mathf.Deg2Rad *
                           (Hash01(i, salt + 11) < 0.5f ? -1f : 1f);
                var cos = Mathf.Cos(turn);
                var sin = Mathf.Sin(turn);

                ref var b = ref NextBlade(rows);
                for (var it = 0; it < 40; it++)
                {
                    if (RootFar(radius, cos, sin, length, curve, ph, w0, rows) <= DesignRadius) break;
                    length *= 0.97f;
                }

                var drift = 0f;
                var rim = 0f;
                var top = 0f;
                for (var j = 0; j < rows; j++)
                {
                    var s = j / (float)(rows - 1);
                    var z = RootDrift(s, curve, ph);
                    var hump = Mathf.Max(0f, Mathf.Sin(Mathf.PI * s));
                    var arch = twin
                        ? (0.75f * Mathf.Abs(Mathf.Sin(TwoPi * Mathf.Pow(s, 0.8f))) + 0.25f * hump) * (1f - 0.3f * s)
                        : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(s, 0.75f))), 0.85f);
                    var hw = RootHalfWidth(s, w0, ph);
                    _shape[b.Row + j] = new Vector3(length * s, arch * h, z);
                    _halfWidth[b.Row + j] = hw;
                    drift = Mathf.Max(drift, Mathf.Abs(z));
                    rim = Mathf.Max(rim, hw);
                    top = Mathf.Max(top, arch * h);
                }

                b.DirX = Mathf.Cos(angle);
                b.DirZ = Mathf.Sin(angle);
                b.Radius = radius;
                b.HeadCos = cos;
                b.HeadSin = sin;
                b.Size = length;
                b.Top = top;
                b.Along = length;
                b.Spread = drift + rim;
                b.Rim = rim;
                b.Phase = Hash01(i, salt + 8);
                var tone = Mathf.Lerp(0.85f, 1f, Hash01(i, salt + 9));
                b.Base = bark * tone;
                b.Mid = barkTip * tone;
                b.Tip = glow;
            }
        }

        static float RootDrift(float s, float curve, float ph) =>
            curve * s * s + RootWiggle * Mathf.Sin(3f * Mathf.PI * s + ph) * Mathf.Sin(Mathf.PI * s);

        static float RootHalfWidth(float s, float w0, float ph) =>
            w0 * Mathf.Lerp(1f, RootTipWidth, s) * (1f + RootKnob * Mathf.Sin(19f * s + ph));

        /// <summary>Farthest any row of a root (plus its half width) gets from the anchor, template frame.</summary>
        static float RootFar(float radius, float cos, float sin, float length, float curve, float ph, float w0, int rows)
        {
            var far = 0f;
            for (var j = 0; j < rows; j++)
            {
                var s = j / (float)(rows - 1);
                var x = length * s;
                var z = RootDrift(s, curve, ph);
                var px = radius + x * cos - z * sin;
                var pz = x * sin + z * cos;
                far = Mathf.Max(far, Mathf.Sqrt(px * px + pz * pz) + RootHalfWidth(s, w0, ph));
            }

            return far;
        }

        // ── Mesh + atlas ────────────────────────────────────────────────────

        void BuildMesh()
        {
            _anchorVerts = 0;
            var quads = 0;
            var maxRows = 2;
            for (var bi = 0; bi < _blockCount; bi++)
            {
                ref var blk = ref _blocks[bi];
                blk.VertStart = _anchorVerts;
                _anchorVerts += blk.Count * blk.Rows * 2;
                quads += blk.Count * (blk.Rows - 1);
                maxRows = Mathf.Max(maxRows, blk.Rows);
            }

            var total = _anchorVerts * MaxAnchors;
            _verts = new Vector3[total];
            _cols = new Color32[total];
            _rowP = new Vector3[maxRows];
            var uvs = new Vector2[total];
            for (var a = 0; a < MaxAnchors; a++)
            for (var bi = 0; bi < _blockCount; bi++)
            {
                var blk = _blocks[bi];
                var u0 = (blk.Region * RegionW + 2f) / AtlasW;
                var u1 = ((blk.Region + 1) * RegionW - 2f) / AtlasW;
                for (var k = 0; k < blk.Count; k++)
                {
                    var v = a * _anchorVerts + blk.VertStart + k * blk.Rows * 2;
                    for (var j = 0; j < blk.Rows; j++)
                    {
                        var tv = Mathf.Lerp(0.5f / AtlasH, 1f - 0.5f / AtlasH, j / (float)(blk.Rows - 1));
                        uvs[v + j * 2] = new Vector2(u0, tv);
                        uvs[v + j * 2 + 1] = new Vector2(u1, tv);
                    }
                }
            }

            // Triangle order is draw order (no depth write). Slot k of a block's zig-zag sits about
            // πk/Count round from the ring's back (see WriteAnchor), so merge the blocks on that
            // share: far leaves and far fronds draw first, near ones of both kinds last.
            var tris = new int[quads * 6 * MaxAnchors];
            var next = new int[_blockCount];
            var ti = 0;
            for (var a = 0; a < MaxAnchors; a++)
            {
                for (var bi = 0; bi < _blockCount; bi++)
                    next[bi] = 0;
                while (true)
                {
                    var pick = -1;
                    var best = float.MaxValue;
                    for (var bi = 0; bi < _blockCount; bi++)
                    {
                        if (next[bi] >= _blocks[bi].Count) continue;
                        var at = (next[bi] + 0.5f) / _blocks[bi].Count;
                        if (at >= best) continue;
                        best = at;
                        pick = bi;
                    }

                    if (pick < 0) break;
                    var blk = _blocks[pick];
                    var v = a * _anchorVerts + blk.VertStart + next[pick]++ * blk.Rows * 2;
                    for (var j = 0; j < blk.Rows - 1; j++)
                    {
                        var q = v + j * 2;
                        tris[ti++] = q;
                        tris[ti++] = q + 2;
                        tris[ti++] = q + 1;
                        tris[ti++] = q + 1;
                        tris[ti++] = q + 2;
                        tris[ti++] = q + 3;
                    }
                }
            }

            _mesh = NewDynamicMesh("GroundCover");
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.colors32 = _cols;
            _mesh.triangles = tris;
            var atlas = OwnTexture(BuildAtlas());
            _mr = AddMeshChild("GroundCover", _mesh, VertexColorMaterial("GroundCoverMat", GroundQueue, atlas));
        }

        /// <summary>White-on-alpha atlas (vertex colour tints it); paints only the regions this variant uses.</summary>
        Texture2D BuildAtlas()
        {
            var px = new Color32[AtlasW * AtlasH];
            for (var bi = 0; bi < _blockCount; bi++)
            {
                switch (_blocks[bi].Region)
                {
                    case RegionGrass: PaintGrass(px); break;
                    case RegionLeaf: PaintLeaves(px); break;
                    case RegionFrond: PaintFrond(px); break;
                    default: PaintBark(px); break;
                }
            }

            var tex = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, AtlasMips, false)
            {
                name = "GroundCoverAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A tuft of tapered blades rising from the bottom edge and fanning apart.</summary>
        static void PaintGrass(Color32[] px)
        {
            // x = base u, y = height, z = tip offset, w = half width at the base.
            var blades = new Vector4[GrassBlades];
            for (var b = 0; b < GrassBlades; b++)
            {
                var bx = Mathf.Lerp(0.25f, 0.75f, (b + Mathf.Lerp(0.2f, 0.8f, Hash01(b, 41))) / GrassBlades);
                blades[b] = new Vector4(bx, Mathf.Lerp(0.5f, 0.97f, Hash01(b, 42)),
                    (bx - 0.5f) * 0.5f + Mathf.Lerp(-0.07f, 0.07f, Hash01(b, 43)),
                    Mathf.Lerp(0.022f, 0.04f, Hash01(b, 44)));
            }

            for (var y = 0; y < AtlasH; y++)
            {
                var v = (y + 0.5f) / AtlasH;
                for (var x = 0; x < RegionW; x++)
                {
                    var u = (x + 0.5f) / RegionW;
                    float lum = 0f, a = 0f;
                    for (var b = 0; b < GrassBlades; b++)
                    {
                        var g = blades[b];
                        if (v >= g.y) continue;
                        var t = v / g.y;
                        var cx = g.x + g.z * t * t;
                        var cov = Mathf.Clamp01((g.w * (1f - t) - Mathf.Abs(u - cx)) * RegionW + 0.5f);
                        if (cov <= 0f) continue;
                        Over(ref lum, ref a, Mathf.Lerp(0.7f, 1f, Hash01(b, 45)) * Mathf.Lerp(0.8f, 1f, t), cov);
                    }

                    Put(px, RegionGrass, x, y, lum, a);
                }
            }
        }

        /// <summary>
        /// Broad ovate leaves on short stems fanning up from the base (Forest
        /// undergrowth). Taller than wide so it still reads as a plant when a
        /// raised phone foreshortens the billboard.
        /// </summary>
        static void PaintLeaves(Color32[] px)
        {
            var fan = new float[LeafCount];
            var reach = new float[LeafCount];
            var tone = new float[LeafCount];
            var order = new int[LeafCount];
            for (var l = 0; l < LeafCount; l++)
            {
                fan[l] = Mathf.Lerp(-45f, 45f, (l + Mathf.Lerp(0.25f, 0.75f, Hash01(l, 51))) / LeafCount);
                reach[l] = Mathf.Lerp(0.9f, 0.58f, Mathf.Abs(fan[l]) / 45f) * Mathf.Lerp(0.85f, 1f, Hash01(l, 52));
                tone[l] = Mathf.Lerp(0.8f, 1f, Hash01(l, 53));

                // Outer leaves first so the upright centre leaves sit over them.
                var j = l;
                while (j > 0 && Mathf.Abs(fan[order[j - 1]]) < Mathf.Abs(fan[l]))
                {
                    order[j] = order[j - 1];
                    j--;
                }

                order[j] = l;
            }

            for (var y = 0; y < AtlasH; y++)
            {
                var v = (y + 0.5f) / AtlasH;
                for (var x = 0; x < RegionW; x++)
                {
                    var u = (x + 0.5f) / RegionW;
                    float lum = 0f, a = 0f;
                    for (var o = 0; o < LeafCount; o++)
                    {
                        var l = order[o];
                        var dirX = Mathf.Sin(fan[l] * Mathf.Deg2Rad);
                        var dirY = Mathf.Cos(fan[l] * Mathf.Deg2Rad);
                        var stem = reach[l] * 0.25f;
                        var len = reach[l] * 0.75f;
                        var px0 = u - 0.5f;
                        var py0 = v - 0.02f;
                        var along = px0 * dirX + py0 * dirY;
                        var across = px0 * dirY - py0 * dirX;
                        if (along >= 0f && along <= stem)
                            Over(ref lum, ref a, 0.6f, Mathf.Clamp01((0.012f - Mathf.Abs(across)) * RegionW + 0.5f));
                        var t = (along - stem) / len;
                        if (t < 0f || t > 1f) continue;
                        var hw = len * 0.34f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
                        var cov = Mathf.Clamp01((hw - Mathf.Abs(across)) * RegionW + 0.5f);
                        if (cov <= 0f) continue;
                        var shade = tone[l] * Mathf.Lerp(0.75f, 1f, t) * (Mathf.Abs(across) < 0.01f ? 0.82f : 1f);
                        Over(ref lum, ref a, shade, cov);
                    }

                    Put(px, RegionLeaf, x, y, lum, a);
                }
            }
        }

        /// <summary>Fern frond: rachis up the middle, alternate leaflets swept 25° toward the tip.</summary>
        static void PaintFrond(Color32[] px)
        {
            const float pinnaLen = 0.42f;
            const float pinnaHalf = 0.075f;
            const float step = 0.064f;
            var cos25 = Mathf.Cos(25f * Mathf.Deg2Rad);
            var sin25 = Mathf.Sin(25f * Mathf.Deg2Rad);
            for (var y = 0; y < AtlasH; y++)
            {
                var v = (y + 0.5f) / AtlasH;
                for (var x = 0; x < RegionW; x++)
                {
                    var u = (x + 0.5f) / RegionW;
                    float lum = 0f, a = 0f;
                    var dx = Mathf.Abs(u - 0.5f);
                    var right = u >= 0.5f;
                    for (var k = 0; k < Pinnae; k++)
                    {
                        var vk = 0.08f + k * step + (right ? step * 0.5f : 0f);
                        var env = Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01((vk - 0.04f) / 0.94f), 0.75f));
                        var len = pinnaLen * env;
                        if (len <= 0.01f) continue;
                        var dy = (v - vk) * FrondAspect;
                        var along = dx * cos25 + dy * sin25;
                        if (along < 0f || along > len) continue;
                        var across = -dx * sin25 + dy * cos25;
                        var t = along / len;
                        var hw = pinnaHalf * Mathf.Sqrt(env) * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.12f, 1f, t));
                        var cov = Mathf.Clamp01((hw - Mathf.Abs(across)) * 56f + 0.5f);
                        if (cov <= 0f) continue;
                        Over(ref lum, ref a, Mathf.Lerp(0.72f, 1f, t) * ((k & 1) == 1 ? 0.92f : 1f), cov);
                    }

                    var rachis = 0.022f * (1f - 0.6f * v);
                    Over(ref lum, ref a, 0.72f, Mathf.Clamp01((rachis - dx) * RegionW + 0.5f) * (v < 0.97f ? 1f : 0f));
                    Put(px, RegionFrond, x, y, lum, a);
                }
            }
        }

        /// <summary>Rounded bark tube across u, streaks running along v, faint growth rings.</summary>
        static void PaintBark(Color32[] px)
        {
            for (var y = 0; y < AtlasH; y++)
            {
                var v = (y + 0.5f) / AtlasH;
                for (var x = 0; x < RegionW; x++)
                {
                    var u = (x + 0.5f) / RegionW;
                    var a = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / 0.14f);
                    var tube = 0.45f + 0.55f * Mathf.Sqrt(Mathf.Sin(Mathf.PI * u));
                    var streak = 0.5f + 0.3f * Mathf.Sin(u * 37f + Mathf.Sin(v * 4f) * 0.8f + Mathf.Sin(v * 9f + u * 5f) * 0.4f) +
                                 0.2f * Mathf.Sin(u * 83f + v * 2.5f);
                    var rings = 0.9f + 0.1f * Mathf.Sin(v * 23f + u * 3f);
                    Put(px, RegionBark, x, y, tube * (0.75f + 0.25f * streak) * rings * a, a);
                }
            }
        }

        /// <summary>Premultiplied "over" of one shape into a pixel.</summary>
        static void Over(ref float lum, ref float alpha, float shade, float cov)
        {
            lum = shade * cov + lum * (1f - cov);
            alpha = cov + alpha * (1f - cov);
        }

        /// <summary>Write a region pixel; alpha falls to 0 over 3 px at the column edges so mips never bleed.</summary>
        static void Put(Color32[] px, int region, int x, int y, float lum, float alpha)
        {
            var edge = Mathf.Clamp01(Mathf.Min(x, RegionW - 1 - x) / 3f);
            var l = alpha > 1e-4f ? Mathf.Clamp01(lum / alpha) : 0f;
            px[y * AtlasW + region * RegionW + x] = new Color(l, l, l, alpha * edge);
        }
    }
}
