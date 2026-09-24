using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Mystic Plasma Zone's storm. A faint three-armed storm eye turns slowly
    /// high over the middle of the street, like the vortex in the illustration.
    /// Every 0.6–1.8 s (at SigScale 1) a bolt of plasma lightning cracks out of it
    /// and forks down and outward, or skitters between two points under the
    /// ceiling. Now and then a second bolt follows a third of a second later.
    /// Each channel is jagged by midpoint displacement and may throw one or two
    /// short branches that fade out to wisps. A bolt races out from its source in
    /// 30 ms and is gone within a quarter second: a violet glow in the accent
    /// swells once and fades to a dim afterglow, while its thin near-white core
    /// flickers with two or three return strokes. The cloud round the source
    /// lights up in one soft flash per bolt, and the eye brightens with it.
    /// <para>Kept gentle for photosensitive players: everything with any area
    /// (glow, cloud flash, eye) rises once and then only fades, and bolts start at
    /// least <see cref="FlashSpacing"/> apart, so there are never more than three
    /// flashes in any second. Only the core, at most 3.5 cm wide, flickers.</para>
    /// <para>One look (variant 0). Any other variant draws the same storm.
    /// SigScale sets how often bolts strike, how far they reach, how thick they
    /// are and how often they branch and triple-stroke.</para>
    /// Everything stays inside the street box, above head-clear height and at
    /// z ≥ NearZ, clear of the local camera. Channel points keep to a band and a
    /// depth range inset by the widest glow, and every vertex is clamped to the
    /// box and NearZ as a guard, so no bolt ever reaches down into the aisle or
    /// up to the lens. One dynamic mesh of camera-facing ribbons and flash quads
    /// on the soft dot: one draw call, 304 vertices. Scope: street.
    /// </summary>
    public sealed class ArFieldSigArcs : ArFieldSignature
    {
        /// <summary>One bolt at a time: bolts start <see cref="FlashSpacing"/> apart and live at most <see cref="LifeMax"/>.</summary>
        const int MaxBolts = 1;
        /// <summary>Main channel segments: a power of two for midpoint displacement.</summary>
        const int MainSegs = 32;
        const int MainPts = MainSegs + 1;
        const int MaxBranches = 2;
        /// <summary>Branch segments: a power of two for midpoint displacement.</summary>
        const int BranchSegs = 8;
        const int BranchPts = BranchSegs + 1;
        /// <summary>Channel points per bolt: the main channel, then each branch.</summary>
        const int BoltPts = MainPts + MaxBranches * BranchPts;
        /// <summary>Left and right vertex per channel point, in each of the glow and core ribbons.</summary>
        const int RibbonVerts = BoltPts * 2;
        const int EyeArms = 3;
        const int ArmPts = 16;
        const int EyeVerts = EyeArms * ArmPts * 2;

        // Vertex layout = draw order: the eye, flash quads, then every glow ribbon, then every core.
        const int EyeBase = 0;
        const int FlashBase = EyeBase + EyeVerts;
        const int GlowBase = FlashBase + MaxBolts * 4;
        const int CoreBase = GlowBase + MaxBolts * RibbonVerts;
        /// <summary>96 + 4 + 2 × 102 = 304 ≤ <see cref="ArFieldSignature.MaxVertices"/>.</summary>
        const int TotalVerts = CoreBase + MaxBolts * RibbonVerts;

        const float TwoPi = Mathf.PI * 2f;
        const float MinLevel = 0.001f;
        /// <summary>Below this hologram scale the street is too small to hold the band: draw nothing.</summary>
        const float MinHolo = 0.01f;

        // Timing (seconds).
        const float IntervalMin = 0.6f;
        const float IntervalMax = 1.8f;
        const float FirstWaitMin = 0.25f;
        const float FirstWaitMax = 0.8f;
        /// <summary>
        /// Minimum time between bolt starts, whatever the schedule: at most three
        /// flashes in any second. Longer than <see cref="LifeMax"/>, so bolts never overlap.
        /// </summary>
        const float FlashSpacing = 0.34f;
        /// <summary>Chance a bolt is followed by a second one a beat later.</summary>
        const float PairChance = 0.3f;
        const float PairGapMin = FlashSpacing;
        const float PairGapMax = 0.46f;
        const float LifeMin = 0.12f;
        const float LifeMax = 0.25f;
        /// <summary>The channel races from its source to its tip in this long, then the first stroke fires.</summary>
        const float LeaderSeconds = 0.03f;
        /// <summary>Brightness while the leader is still racing out.</summary>
        const float LeaderGlow = 0.55f;
        /// <summary>A branch races out in this share of the leader time once the main leader passes its fork.</summary>
        const float BranchLead = 0.6f;
        /// <summary>E-folding time of each return stroke's flare in the core.</summary>
        const float StrokeDecay = 0.035f;
        /// <summary>E-folding time of the glow's single swell as the leader lands.</summary>
        const float GlowDecay = 0.07f;
        /// <summary>Dim afterglow of the channel (fades over the bolt's life).</summary>
        const float Residual = 0.3f;
        const float TailFade = 0.04f;

        // Where bolts live. Heights are shares of the band or × HoloScale.
        /// <summary>Hard clamp for every vertex: this far (× HoloScale) above clear height and under the box top.</summary>
        const float GuardMargin = 0.02f;
        /// <summary>Storm eye height as a share of the band.</summary>
        const float EyeRise = 0.9f;
        /// <summary>Radius (× HoloScale) of the eye's slow turn round the street centre.</summary>
        const float EyeOrbit = 0.3f;
        /// <summary>Radians per second.</summary>
        const float EyeSpin = 0.35f;
        /// <summary>Scatter (× HoloScale) of a strike's source round the eye.</summary>
        const float EyeJitter = 0.08f;
        /// <summary>Share of bolts that crack out of the eye; the rest crawl under the ceiling.</summary>
        const float StrikeChance = 0.65f;
        const float StrikeReachMin = 1.3f;
        const float StrikeReachMax = 2.5f;
        /// <summary>A strike's tip lands in this lowest share of the band.</summary>
        const float StrikeTipRise = 0.3f;
        const float CrawlReachMin = 1.6f;
        const float CrawlReachMax = 3f;
        /// <summary>Crawler ends stay above this share of the band.</summary>
        const float CrawlFloor = 0.5f;
        /// <summary>Crawler centres, as shares of the free half extents (not over the duelists' heads).</summary>
        const float CrawlSpreadX = 0.7f;
        const float CrawlSpreadZ = 0.55f;
        /// <summary>Channel points stay inside this share of the street half extents.</summary>
        const float BoxInset = 0.92f;
        /// <summary>First midpoint offset as a share of the channel length; each finer level × JagDecay.</summary>
        const float Jag = 0.13f;
        const float JagDecay = 0.7f;
        /// <summary>Vertical jag as a share of the sideways jag.</summary>
        const float JagVertical = 0.6f;

        // Branches.
        const float ForkMin = 0.3f;
        const float ForkMax = 0.62f;
        /// <summary>Turn off the main heading, radians (25°–55°).</summary>
        const float BranchTurnMin = 0.44f;
        const float BranchTurnMax = 0.96f;
        const float BranchReachMin = 0.25f;
        const float BranchReachMax = 0.45f;
        /// <summary>Tip drop as a share of the branch length.</summary>
        const float BranchDrop = 0.25f;
        const float BranchWidth = 0.6f;
        const float BranchAlpha = 0.8f;

        // Look. Widths are metres × max(MinWidthScale, HoloScale) × size.
        const float MinWidthScale = 0.3f;
        const float GlowHalf = 0.07f;
        const float CoreHalf = 0.014f;
        /// <summary>Glow widens by up to this share as the leader lands.</summary>
        const float GlowSwell = 0.25f;
        /// <summary>Core width between strokes, as a share of its peak width.</summary>
        const float CoreRest = 0.75f;
        const float BoltWidthMin = 0.85f;
        const float BoltWidthMax = 1.15f;
        const float TipWidth = 0.35f;
        const float TipAlpha = 0.55f;
        const float GlowAlpha = 0.55f;
        /// <summary>Cap below 1: even the core stays translucent.</summary>
        const float CoreAlpha = 0.85f;
        const float GlowSky = 0.15f;
        const float BranchSky = 0.3f;
        const float CoreWhite = 0.8f;
        /// <summary>Flash ellipse radii (× HoloScale × size): wide and low, lying under the ceiling.</summary>
        const float FlashRadiusX = 0.4f;
        const float FlashRadiusY = 0.18f;
        const float FlashAlpha = 0.25f;
        /// <summary>The cloud flash rises with the leader, then fades over this e-folding time (once per bolt).</summary>
        const float FlashDecay = 0.06f;
        const float FlashWhite = 0.12f;
        /// <summary>Crawlers light the cloud less than strikes out of the eye.</summary>
        const float CrawlFlash = 0.6f;
        /// <summary>Ribbons keep their last side when both neighbouring segments run this close to the view line (summed sines).</summary>
        const float AcrossMin = 0.1f;
        const float BoundsPad = 0.25f;

        // Storm eye: log-spiral arms in the view plane.
        /// <summary>Arm tip radius across the view (× HoloScale × size).</summary>
        const float EyeRadius = 0.28f;
        /// <summary>Eye height as a share of its width: a level vortex seen from below at a slant.</summary>
        const float EyeTilt = 0.45f;
        /// <summary>Radians each arm winds from its inner end to its tip, trailing the spin.</summary>
        const float ArmWind = 3.2f;
        /// <summary>Arms start at e^−ArmTighten (about 0.17) of the tip radius.</summary>
        const float ArmTighten = 1.8f;
        const float EyeHalf = 0.035f;
        /// <summary>Inner end width as a share of the tip width.</summary>
        const float ArmTaper = 0.5f;
        /// <summary>Faint but still legible over daylight passthrough.</summary>
        const float EyeAlpha = 0.15f;
        /// <summary>Extra eye alpha at the peak of a strike's cloud flash.</summary>
        const float EyeFlare = 0.2f;

        /// <summary>One flash. Its channel lives in <see cref="_pts"/> at slot × <see cref="BoltPts"/>.</summary>
        struct Bolt
        {
            public bool Alive;
            /// <summary>Cracks out of the storm eye (flash at its source); otherwise crawls (flash mid-channel).</summary>
            public bool Strike;
            public float Age;
            public float Life;
            /// <summary>× the kit's ribbon widths.</summary>
            public float Width;
            /// <summary>Return strokes: 2 or 3. The first fires as the leader lands, full bright.</summary>
            public int Strokes;
            public float T1;
            public float A1;
            public float T2;
            public float A2;
            public int Branches;
            /// <summary>Main-channel point each branch leaves from.</summary>
            public int Fork0;
            public int Fork1;
        }

        /// <summary>One bolt's ribbon widths and brightness this frame.</summary>
        struct Pulse
        {
            public float GlowHalf;
            public float CoreHalf;
            /// <summary>Glow brightness: rises with the leader, then only fades.</summary>
            public float Glow;
            /// <summary>Core brightness: flickers with the return strokes.</summary>
            public float Core;
        }

        Mesh _mesh;
        MeshRenderer _mr;
        Vector3[] _verts;
        Color32[] _cols;
        Bolt[] _bolts;
        /// <summary>Channel centre lines in floor-local metres, fixed for a bolt's life.</summary>
        Vector3[] _pts;

        // SigScale.
        float _size;
        float _reach;
        float _rate;
        float _branchOne;
        float _branchTwo;
        float _tripleStroke;

        // Vertex colours (converted from the sRGB palette once).
        Color _glow;
        Color _branchGlow;
        Color _core;
        Color _flash;

        float _wait;
        bool _pairPending;
        /// <summary>Seconds since the last bolt started.</summary>
        float _sinceStart = FlashSpacing;
        float _eyePhase;
        Vector3 _viewer;

        // This frame's street, from Frame().
        float _h;
        float _w;
        float _cx;
        float _cz;
        float _hx;
        float _hz;
        float _yFloor;
        float _yCeil;
        /// <summary>Band the channel centre lines keep to: the guard band minus the widest glow.</summary>
        float _yLow;
        float _yHigh;
        float _xLo;
        float _xHi;
        /// <summary>Depth range the channel centre lines keep to: NearZ plus the widest glow up to the inset far wall.</summary>
        float _zNear;
        float _zFar;
        /// <summary>Hard near limit for every vertex: the box's near face or NearZ, whichever is farther.</summary>
        float _zGuard;
        /// <summary>Centre of the eye's orbit along the street, pushed forward when NearZ cuts into it.</summary>
        float _eyeZ;

        protected override void Build()
        {
            var sig = SigScale;
            _size = 0.85f + 0.15f * sig;
            _reach = 0.8f + 0.2f * sig;
            _rate = 0.7f + 0.3f * sig;
            _branchOne = 0.35f + 0.35f * sig;
            _branchTwo = 0.1f + 0.25f * sig;
            _tripleStroke = 0.15f + 0.3f * sig;

            var accent = Env.Accent;
            _glow = VertexColor(Color.Lerp(accent, Env.Sky, GlowSky));
            _branchGlow = VertexColor(Color.Lerp(accent, Env.Sky, BranchSky));
            _core = VertexColor(Color.Lerp(accent, Color.white, CoreWhite));
            _flash = VertexColor(Color.Lerp(accent, Color.white, FlashWhite));

            _bolts = new Bolt[MaxBolts];
            _pts = new Vector3[MaxBolts * BoltPts];
            _verts = new Vector3[TotalVerts];
            _cols = new Color32[TotalVerts];

            _mesh = NewDynamicMesh("FieldArcs");
            _mesh.vertices = _verts;
            _mesh.uv = BuildUvs();
            _mesh.colors32 = _cols;
            _mesh.triangles = BuildTriangles();
            _mr = AddMeshChild("Arcs", _mesh, VertexColorMaterial("FieldArcsMat", StreetQueue));

            _wait = R(FirstWaitMin, FirstWaitMax);
            _eyePhase = R(0f, TwoPi);
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
            if (_mr == null) return;
            if (level <= MinLevel || street.HoloScale <= MinHolo || !Frame(street))
            {
                Hide();
                return;
            }

            level = Mathf.Min(level, 1f);
            dt = Mathf.Max(0f, dt);
            _eyePhase = Mathf.Repeat(_eyePhase + EyeSpin * dt, TwoPi);
            _sinceStart += dt;

            var live = 0;
            for (var i = 0; i < MaxBolts; i++)
            {
                ref var b = ref _bolts[i];
                if (!b.Alive) continue;
                b.Age += dt;
                if (b.Age >= b.Life) b.Alive = false;
                else live++;
            }

            _wait -= dt;
            if (_wait <= 0f && _sinceStart >= FlashSpacing && live < MaxBolts)
            {
                // Back-dated by the overshoot, but never nearer than FlashSpacing to the last start.
                var age = Mathf.Min(Mathf.Min(-_wait, dt), _sinceStart - FlashSpacing);
                if (Spawn(age))
                {
                    _sinceStart = age;
                    Schedule();
                }
            }

            _viewer = Viewer(street);
            var eyeFlare = 0f;
            for (var i = 0; i < MaxBolts; i++)
            {
                if (_bolts[i].Alive) eyeFlare = Mathf.Max(eyeFlare, WriteBolt(i, level));
                else Park(i);
            }

            WriteEye(level, eyeFlare);
            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            // Vertices move every frame: the street box keeps the renderer from being culled.
            _mesh.bounds = new Bounds(street.Center, street.Half * 2f + Vector3.one * BoundsPad);
            if (!_mr.enabled) _mr.enabled = true;
        }

        void Hide()
        {
            for (var i = 0; i < MaxBolts; i++)
                _bolts[i].Alive = false;
            if (_mr.enabled) _mr.enabled = false;
        }

        /// <summary>Street box, height band and free depth for this frame. False when no depth is left clear of the camera.</summary>
        bool Frame(in FieldStreet street)
        {
            _h = street.HoloScale;
            _w = Mathf.Max(MinWidthScale, _h) * _size;
            _cx = street.Center.x;
            _cz = street.Center.z;
            _hx = Mathf.Max(0f, street.Half.x);
            _hz = Mathf.Max(0f, street.Half.z);
            _yFloor = (StreetClearHeight + GuardMargin) * _h;
            _yCeil = Mathf.Max(_yFloor, street.Center.y + street.Half.y - GuardMargin * _h);
            // A camera-facing ribbon reaches at most its half width from its centre line, in any direction.
            var glowMax = GlowHalf * _w * BoltWidthMax * (1f + GlowSwell);
            _yLow = _yFloor + glowMax;
            _yHigh = _yCeil - glowMax;
            if (_yHigh < _yLow) _yLow = _yHigh = (_yFloor + _yCeil) * 0.5f;
            _xLo = _cx - _hx * BoxInset;
            _xHi = _cx + _hx * BoxInset;
            // The local camera is at the −Z end, often inside the box: nothing is drawn nearer than NearZ.
            _zGuard = Mathf.Max(_cz - _hz, street.NearZ);
            _zNear = Mathf.Max(_cz - _hz * BoxInset, street.NearZ + glowMax);
            _zFar = _cz + _hz * BoxInset;
            var orbit = (EyeOrbit + EyeJitter) * _h;
            _eyeZ = Mathf.Clamp(_cz, _zNear + orbit, Mathf.Max(_zNear + orbit, _zFar - orbit));
            return _zNear < _zFar;
        }

        void Schedule()
        {
            if (!_pairPending && R(0f, 1f) < PairChance)
            {
                _wait = R(PairGapMin, PairGapMax);
                _pairPending = true;
            }
            else
            {
                _wait = R(IntervalMin, IntervalMax) / _rate;
                _pairPending = false;
            }
        }

        /// <summary>Stage camera in floor-local space; the player's end of the street without one.</summary>
        Vector3 Viewer(in FieldStreet street)
        {
            var cam = StageCamera;
            if (cam != null) return transform.InverseTransformPoint(cam.transform.position);
            return new Vector3(street.Center.x, street.Center.y, street.Center.z - street.Half.z - 1f);
        }

        Vector3 StormEye() => new Vector3(
            _cx + Mathf.Cos(_eyePhase) * EyeOrbit * _h,
            Mathf.Lerp(_yLow, _yHigh, EyeRise),
            _eyeZ + Mathf.Sin(_eyePhase) * EyeOrbit * _h);

        // ── Spawn ───────────────────────────────────────────────────────────

        /// <summary>New bolt in a free slot, <paramref name="age"/> seconds old. False when none is free.</summary>
        bool Spawn(float age)
        {
            var slot = -1;
            for (var i = 0; i < MaxBolts; i++)
            {
                if (_bolts[i].Alive) continue;
                slot = i;
                break;
            }

            if (slot < 0) return false;
            var p0 = slot * BoltPts;
            var strike = R(0f, 1f) < StrikeChance;
            Vector3 from;
            Vector3 to;
            if (strike)
            {
                // Out of the eye, down and away.
                from = Keep(StormEye() + new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)) * (EyeJitter * _h));
                var ang = R(0f, TwoPi);
                var cos = Mathf.Cos(ang);
                var sin = Mathf.Sin(ang);
                var reach = Fit(from.x, from.z, cos, sin, R(StrikeReachMin, StrikeReachMax) * _reach * _h);
                to = new Vector3(from.x + cos * reach, Mathf.Lerp(_yLow, _yHigh, R(0f, StrikeTipRise)),
                    from.z + sin * reach);
            }
            else
            {
                // Spider lightning crawling under the ceiling, round the middle of the free depth.
                var mx = _cx + R(-1f, 1f) * CrawlSpreadX * _hx;
                var mz = (_zNear + _zFar) * 0.5f + R(-1f, 1f) * CrawlSpreadZ * (_zFar - _zNear) * 0.5f;
                var ang = R(0f, TwoPi);
                var cos = Mathf.Cos(ang);
                var sin = Mathf.Sin(ang);
                // The free depth is not centred on the street, so each end is fitted on its own.
                var want = R(CrawlReachMin, CrawlReachMax) * _reach * _h * 0.5f;
                var half = Mathf.Min(Fit(mx, mz, cos, sin, want), Fit(mx, mz, -cos, -sin, want));
                var dx = cos * half;
                var dz = sin * half;
                from = new Vector3(mx - dx, Mathf.Lerp(_yLow, _yHigh, R(CrawlFloor, 1f)), mz - dz);
                to = new Vector3(mx + dx, Mathf.Lerp(_yLow, _yHigh, R(CrawlFloor, 1f)), mz + dz);
            }

            _pts[p0] = Keep(from);
            _pts[p0 + MainSegs] = Keep(to);
            var span = _pts[p0 + MainSegs] - _pts[p0];
            var len = span.magnitude;
            Jagged(p0, MainSegs, Jag * len);

            ref var b = ref _bolts[slot];
            b.Branches = 0;
            if (R(0f, 1f) < _branchOne) b.Branches = R(0f, 1f) < _branchTwo ? 2 : 1;
            var heading = new Vector3(span.x, 0f, span.z);
            var hm = heading.magnitude;
            heading = hm > 1e-5f ? heading / hm : Vector3.right;
            var side = R(0f, 1f) < 0.5f ? -1f : 1f;
            for (var k = 0; k < b.Branches; k++)
            {
                var fork = Mathf.Clamp(Mathf.RoundToInt(MainSegs * R(ForkMin, ForkMax)), 1, MainSegs - 1);
                if (k == 0) b.Fork0 = fork;
                else b.Fork1 = fork;

                // Opposite sides of the main heading, so two branches splay like the art's forks.
                var turn = (k == 0 ? side : -side) * R(BranchTurnMin, BranchTurnMax);
                var c = Mathf.Cos(turn);
                var s = Mathf.Sin(turn);
                var dir = new Vector3(heading.x * c - heading.z * s, 0f, heading.x * s + heading.z * c);
                var root = _pts[p0 + fork];
                var reach = Fit(root.x, root.z, dir.x, dir.z, R(BranchReachMin, BranchReachMax) * len);
                var q0 = p0 + MainPts + k * BranchPts;
                _pts[q0] = root;
                _pts[q0 + BranchSegs] = Keep(root + dir * reach + Vector3.down * (R(0f, BranchDrop) * reach));
                Jagged(q0, BranchSegs, Jag * reach);
            }

            b.Alive = true;
            b.Strike = strike;
            b.Age = Mathf.Max(0f, age);
            b.Life = R(LifeMin, LifeMax);
            b.Width = R(BoltWidthMin, BoltWidthMax);
            // Later strokes spread between the leader landing and the tail fade.
            var room = b.Life - LeaderSeconds - TailFade * 0.5f;
            b.Strokes = R(0f, 1f) < _tripleStroke ? 3 : 2;
            b.T1 = LeaderSeconds + room * (b.Strokes == 3 ? R(0.3f, 0.45f) : R(0.45f, 0.7f));
            b.T2 = LeaderSeconds + room * R(0.7f, 0.9f);
            b.A1 = R(0.6f, 0.95f);
            b.A2 = R(0.5f, 0.85f);
            return true;
        }

        /// <summary>
        /// Fills the points between <paramref name="start"/> and start + <paramref name="segs"/>
        /// (both set) by midpoint displacement. Each midpoint moves across its own
        /// sub-chord (sideways, and up or down by less), so the channel zigzags without
        /// doubling back on itself.
        /// </summary>
        void Jagged(int start, int segs, float amp)
        {
            for (var step = segs / 2; step >= 1; step /= 2)
            {
                for (var i = step; i < segs; i += 2 * step)
                {
                    var a = _pts[start + i - step];
                    var b = _pts[start + i + step];
                    var chord = b - a;
                    var len = chord.magnitude;
                    var side = new Vector3(-chord.z, 0f, chord.x);
                    var sm = side.magnitude;
                    side = sm > 1e-5f ? side / sm : Vector3.right;
                    var lift = len > 1e-5f ? Vector3.Cross(chord / len, side) : Vector3.up;
                    var m = (a + b) * 0.5f + side * (R(-1f, 1f) * amp) + lift * (R(-1f, 1f) * amp * JagVertical);
                    _pts[start + i] = Keep(m);
                }

                amp *= JagDecay;
            }
        }

        /// <summary>
        /// Longest reach up to <paramref name="reach"/> from (x, z) along the level heading
        /// (cos, sin) that stays inside the channel box (<see cref="Keep"/>), so bolt ends
        /// never squash onto a wall or the NearZ plane.
        /// </summary>
        float Fit(float x, float z, float cos, float sin, float reach)
        {
            var roomX = cos < 0f ? x - _xLo : _xHi - x;
            var roomZ = sin < 0f ? z - _zNear : _zFar - z;
            var dx = Mathf.Abs(cos);
            var dz = Mathf.Abs(sin);
            if (dx > 1e-5f && dx * reach > roomX) reach = Mathf.Max(0f, roomX) / dx;
            if (dz > 1e-5f && dz * reach > roomZ) reach = Mathf.Max(0f, roomZ) / dz;
            return reach;
        }

        /// <summary>Channel centre line: inside the inset street box, the band and the free depth.</summary>
        Vector3 Keep(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, _xLo, _xHi);
            p.y = Mathf.Clamp(p.y, _yLow, _yHigh);
            p.z = Mathf.Clamp(p.z, _zNear, _zFar);
            return p;
        }

        /// <summary>Hard guard on every drawn vertex: inside the street box, above clear height, at z ≥ NearZ.</summary>
        Vector3 Contain(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, _cx - _hx, _cx + _hx);
            p.y = Mathf.Clamp(p.y, _yFloor, _yCeil);
            p.z = Mathf.Clamp(p.z, _zGuard, _cz + _hz);
            return p;
        }

        // ── Write ───────────────────────────────────────────────────────────

        /// <summary>Core flare 0…1: an instant peak per return stroke, decaying.</summary>
        static float Stroke(in Bolt b)
        {
            var t = b.Age;
            var s = t >= LeaderSeconds ? Mathf.Exp(-(t - LeaderSeconds) / StrokeDecay) : 0f;
            if (t >= b.T1) s = Mathf.Max(s, b.A1 * Mathf.Exp(-(t - b.T1) / StrokeDecay));
            if (b.Strokes > 2 && t >= b.T2) s = Mathf.Max(s, b.A2 * Mathf.Exp(-(t - b.T2) / StrokeDecay));
            return s;
        }

        /// <summary>Writes one live bolt. Returns its cloud flash (0…1) when it left the eye, else 0.</summary>
        float WriteBolt(int slot, float level)
        {
            var b = _bolts[slot];
            var p0 = slot * BoltPts;
            var landed = b.Age >= LeaderSeconds;
            var since = b.Age - LeaderSeconds;
            var tail = Mathf.Clamp01((b.Life - b.Age) / TailFade);
            var afterglow = Residual * (1f - b.Age / b.Life);
            // Everything with area swells once as the leader lands; only the thin core follows the strokes.
            var swell = landed ? Mathf.Exp(-since / GlowDecay) : 0f;
            var stroke = Stroke(b);
            Pulse pulse;
            pulse.Glow = (landed ? Mathf.Max(swell, afterglow) : LeaderGlow) * tail * level;
            pulse.Core = (landed ? Mathf.Max(stroke, afterglow) : LeaderGlow) * tail * level;
            pulse.GlowHalf = GlowHalf * _w * b.Width * (1f + GlowSwell * swell);
            pulse.CoreHalf = CoreHalf * _w * b.Width * (CoreRest + (1f - CoreRest) * stroke);
            var lead = Mathf.Clamp01(b.Age / LeaderSeconds);

            WriteRibbon(slot, 0, MainPts, 1f, TipWidth, 1f, TipAlpha, lead, pulse, _glow);
            for (var k = 0; k < MaxBranches; k++)
            {
                var local = MainPts + k * BranchPts;
                var fork = k == 0 ? b.Fork0 : b.Fork1;
                if (k >= b.Branches)
                {
                    ParkRibbon(slot, local, BranchPts, Contain(_pts[p0 + fork]));
                    continue;
                }

                var sf = fork / (float)MainSegs;
                var wf = Mathf.Lerp(1f, TipWidth, sf) * BranchWidth;
                var reveal = Mathf.Clamp01((b.Age - LeaderSeconds * sf) / (LeaderSeconds * BranchLead));
                WriteRibbon(slot, local, BranchPts, wf, wf * TipWidth, BranchAlpha, 0f, reveal, pulse, _branchGlow);
            }

            var cloud = (landed ? Mathf.Exp(-since / FlashDecay) : b.Age / LeaderSeconds) * tail;
            var flash = FlashAlpha * cloud * level * (b.Strike ? 1f : CrawlFlash);
            WriteFlash(slot, b.Strike ? _pts[p0] : _pts[p0 + MainSegs / 2], flash);
            return b.Strike ? cloud : 0f;
        }

        /// <summary>
        /// Glow and core ribbons along one channel, turned across the view at every
        /// point. <paramref name="reveal"/> 0…1 is how far the leader has raced along it.
        /// Width and alpha run from the w0 / a0 end to the w1 / a1 end.
        /// </summary>
        void WriteRibbon(int slot, int local, int n, float w0, float w1, float a0, float a1, float reveal,
            in Pulse pulse, Color glowCol)
        {
            var pt = slot * BoltPts + local;
            var g = GlowBase + slot * RibbonVerts + local * 2;
            var c = CoreBase + slot * RibbonVerts + local * 2;
            var segs = n - 1;
            var across = Vector3.up;
            var before = Vector3.zero;
            for (var j = 0; j < n; j++)
            {
                var p = _pts[pt + j];
                var after = j < segs ? Unit(_pts[pt + j + 1] - p) : Vector3.zero;
                var view = Unit(_viewer - p);
                // Each neighbouring segment votes for a side, weighted by how far it is from
                // end-on, so a segment pointing at the camera cannot twist the ribbon.
                var x = Side(before, view, across) + Side(after, view, across);
                var m = x.magnitude;
                if (m > AcrossMin) across = x / m;
                before = after;

                var s = j / (float)segs;
                var shown = Mathf.Clamp01(reveal * n - j);
                var w = Mathf.Lerp(w0, w1, s);
                var a = Mathf.Lerp(a0, a1, s) * shown;
                var gh = across * (pulse.GlowHalf * w);
                var ch = across * (pulse.CoreHalf * w);
                var o = j * 2;
                _verts[g + o] = Contain(p - gh);
                _verts[g + o + 1] = Contain(p + gh);
                _verts[c + o] = Contain(p - ch);
                _verts[c + o + 1] = Contain(p + ch);
                Color32 gc = WithAlpha(glowCol, GlowAlpha * pulse.Glow * a);
                Color32 cc = WithAlpha(_core, CoreAlpha * pulse.Core * a);
                _cols[g + o] = gc;
                _cols[g + o + 1] = gc;
                _cols[c + o] = cc;
                _cols[c + o + 1] = cc;
            }
        }

        /// <summary>Unit vector, or zero for a zero-length one.</summary>
        static Vector3 Unit(Vector3 v)
        {
            var m = v.magnitude;
            return m > 1e-6f ? v / m : Vector3.zero;
        }

        /// <summary>Side of a segment across the view (length = sine of their angle), turned to agree with <paramref name="prev"/>.</summary>
        static Vector3 Side(Vector3 dir, Vector3 view, Vector3 prev)
        {
            var c = Vector3.Cross(dir, view);
            return Vector3.Dot(c, prev) < 0f ? -c : c;
        }

        /// <summary>Level right and matching up across the view at <paramref name="at"/>.</summary>
        void Facing(Vector3 at, out Vector3 right, out Vector3 up)
        {
            var view = _viewer - at;
            right = new Vector3(view.z, 0f, -view.x);
            var rm = right.magnitude;
            if (rm > 1e-5f)
            {
                right /= rm;
                up = Vector3.Cross(view / view.magnitude, right);
            }
            else
            {
                right = Vector3.right;
                up = Vector3.forward;
            }
        }

        /// <summary>
        /// Moves the centre of a camera-facing patch (half extents <paramref name="rx"/>
        /// along right, <paramref name="ry"/> along up) so all of it fits the guard box
        /// and NearZ, instead of being squashed by <see cref="Contain"/>.
        /// </summary>
        Vector3 Settle(Vector3 at, Vector3 right, Vector3 up, float rx, float ry)
        {
            var ex = rx * Mathf.Abs(right.x) + ry * Mathf.Abs(up.x);
            var ey = ry * Mathf.Abs(up.y);
            var ez = rx * Mathf.Abs(right.z) + ry * Mathf.Abs(up.z);
            at.x = Mathf.Clamp(at.x, _cx - _hx + ex, Mathf.Max(_cx - _hx + ex, _cx + _hx - ex));
            at.y = Mathf.Clamp(at.y, _yFloor + ey, Mathf.Max(_yFloor + ey, _yCeil - ey));
            at.z = Mathf.Clamp(at.z, _zGuard + ez, Mathf.Max(_zGuard + ez, _cz + _hz - ez));
            return at;
        }

        /// <summary>
        /// Soft ellipse of light, wide along the ceiling. Its long axis is kept
        /// level, so it reaches at most its short radius above or below its centre.
        /// </summary>
        void WriteFlash(int slot, Vector3 at, float alpha)
        {
            var rx = FlashRadiusX * _h * _size;
            var ry = FlashRadiusY * _h * _size;
            Facing(at, out var right, out var up);
            at = Settle(at, right, up, rx, ry);
            var r = right * rx;
            var u = up * ry;
            var v = FlashBase + slot * 4;
            _verts[v] = Contain(at - r - u);
            _verts[v + 1] = Contain(at + r - u);
            _verts[v + 2] = Contain(at + r + u);
            _verts[v + 3] = Contain(at - r + u);
            Color32 col = WithAlpha(_flash, alpha);
            _cols[v] = col;
            _cols[v + 1] = col;
            _cols[v + 2] = col;
            _cols[v + 3] = col;
        }

        /// <summary>
        /// The storm eye bolts leave from: three log-spiral arms in the view plane,
        /// squashed like a level vortex seen from below and turning with the eye.
        /// Faint; it brightens with the cloud flash when a strike leaves it.
        /// </summary>
        void WriteEye(float level, float flare)
        {
            var at = StormEye();
            Facing(at, out var right, out var up);
            var rx = EyeRadius * _h * _size;
            var ry = rx * EyeTilt;
            var half = EyeHalf * _w;
            at = Settle(at, right, up, rx + half, ry + half);
            var alpha = (EyeAlpha + EyeFlare * flare) * level;
            for (var arm = 0; arm < EyeArms; arm++)
            {
                var turn = _eyePhase + arm * (TwoPi / EyeArms);
                var v = EyeBase + arm * ArmPts * 2;
                for (var j = 0; j < ArmPts; j++)
                {
                    var s = j / (float)(ArmPts - 1);
                    var r = Mathf.Exp((s - 1f) * ArmTighten);
                    var th = turn - s * ArmWind;
                    var cos = Mathf.Cos(th);
                    var sin = Mathf.Sin(th);
                    // Tangent along the arm in (right, up) metres; the ribbon's side is square to it.
                    var tx = (ArmTighten * cos + ArmWind * sin) * r * rx;
                    var ty = (ArmTighten * sin - ArmWind * cos) * r * ry;
                    var side = Unit(up * tx - right * ty) * (half * Mathf.Lerp(ArmTaper, 1f, s));
                    var p = at + right * (rx * r * cos) + up * (ry * r * sin);
                    _verts[v + j * 2] = Contain(p - side);
                    _verts[v + j * 2 + 1] = Contain(p + side);
                    // Hollow at the centre, fading out at the tip.
                    Color32 col = WithAlpha(_glow, alpha * Mathf.Sin(Mathf.PI * s));
                    _cols[v + j * 2] = col;
                    _cols[v + j * 2 + 1] = col;
                }
            }
        }

        /// <summary>Zero-area, fully transparent vertices for a free slot, parked in the channel box.</summary>
        void Park(int slot)
        {
            var at = Contain(new Vector3(_cx, (_yLow + _yHigh) * 0.5f, (_zNear + _zFar) * 0.5f));
            var v = FlashBase + slot * 4;
            for (var i = 0; i < 4; i++)
            {
                _verts[v + i] = at;
                _cols[v + i] = default;
            }

            ParkRibbon(slot, 0, BoltPts, at);
        }

        void ParkRibbon(int slot, int local, int n, Vector3 at)
        {
            var g = GlowBase + slot * RibbonVerts + local * 2;
            var c = CoreBase + slot * RibbonVerts + local * 2;
            for (var i = 0; i < n * 2; i++)
            {
                _verts[g + i] = at;
                _verts[c + i] = at;
                _cols[g + i] = default;
                _cols[c + i] = default;
            }
        }

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>Ribbons run along the soft dot's centre row; flash quads take the whole dot.</summary>
        static Vector2[] BuildUvs()
        {
            var uvs = new Vector2[TotalVerts];
            for (var i = 0; i < TotalVerts; i += 2)
            {
                uvs[i] = new Vector2(0f, 0.5f);
                uvs[i + 1] = new Vector2(1f, 0.5f);
            }

            for (var b = 0; b < MaxBolts; b++)
            {
                var v = FlashBase + b * 4;
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);
            }

            return uvs;
        }

        static int[] BuildTriangles()
        {
            var quads = EyeArms * (ArmPts - 1) + MaxBolts + 2 * MaxBolts * (MainSegs + MaxBranches * BranchSegs);
            var tris = new int[quads * 6];
            var t = 0;
            for (var arm = 0; arm < EyeArms; arm++)
                t = Strip(tris, t, EyeBase + arm * ArmPts * 2, ArmPts);

            for (var b = 0; b < MaxBolts; b++)
            {
                var v = FlashBase + b * 4;
                tris[t++] = v;
                tris[t++] = v + 2;
                tris[t++] = v + 1;
                tris[t++] = v;
                tris[t++] = v + 3;
                tris[t++] = v + 2;
            }

            for (var layer = 0; layer < 2; layer++)
            for (var b = 0; b < MaxBolts; b++)
            {
                var v = (layer == 0 ? GlowBase : CoreBase) + b * RibbonVerts;
                t = Strip(tris, t, v, MainPts);
                for (var k = 0; k < MaxBranches; k++)
                    t = Strip(tris, t, v + (MainPts + k * BranchPts) * 2, BranchPts);
            }

            return tris;
        }

        static int Strip(int[] tris, int t, int v, int n)
        {
            for (var j = 0; j < n - 1; j++)
            {
                var o = v + j * 2;
                tris[t++] = o;
                tris[t++] = o + 2;
                tris[t++] = o + 1;
                tris[t++] = o + 1;
                tris[t++] = o + 2;
                tris[t++] = o + 3;
            }

            return t;
        }
    }
}
