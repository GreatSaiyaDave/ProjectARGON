using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Street volume in floor-local metres. The floor is a child of ArenaRoot
    /// with an identity transform: +Z runs player → opponent, +Y is up, y = 0
    /// is the street surface.
    /// </summary>
    public readonly struct FieldStreet
    {
        /// <summary>Centre of the street volume (y = half its height).</summary>
        public readonly Vector3 Center;
        /// <summary>Half extents: x across the street, y up, z along the duel axis.</summary>
        public readonly Vector3 Half;
        /// <summary><see cref="ArPlaymatLayout.LiveHoloScale"/>: 1 on a full street, smaller on short ones.</summary>
        public readonly float HoloScale;
        /// <summary>
        /// Street pieces must stay at z ≥ NearZ: the local duelist's camera is
        /// at the −Z end, and the street box's near face can be only centimetres
        /// in front of it. NearZ keeps <see cref="ArFieldSignature.StreetCameraClear"/>
        /// between the lens and anything drawn.
        /// </summary>
        public readonly float NearZ;
        /// <summary>Stage camera position, floor-local (valid when <see cref="HasCamera"/>).</summary>
        public readonly Vector3 Camera;
        public readonly bool HasCamera;

        public FieldStreet(Vector3 center, Vector3 half, float holoScale, float nearZ,
            Vector3 camera = default, bool hasCamera = false)
        {
            Center = center;
            Half = half;
            HoloScale = holoScale;
            NearZ = nearZ;
            Camera = camera;
            HasCamera = hasCamera;
        }
    }

    /// <summary>One monster a per-monster signature may decorate.</summary>
    public struct FieldAnchor
    {
        /// <summary>Floor-local point on the street surface under the monster (y = street level).</summary>
        public Vector3 Position;
        /// <summary>0…1 sweep ripple × spawn ramp. Multiply every alpha by it.</summary>
        public float Presence;
        /// <summary>+1 boon, −1 bane, 0 none. Always 0 for face-down monsters (hidden information).</summary>
        public int Aura;
        /// <summary>Monster owner (cyan / magenta side).</summary>
        public bool PlayerSide;
        /// <summary>
        /// Set monster (or a flip still animating): its card lies almost flat over
        /// the anchor. Test pieces with <see cref="ArFieldSignature.InSetCard"/> or
        /// clamp them with <see cref="ArFieldSignature.CoverLimit"/>. Being
        /// face-down is public; the monster's identity is not, and kits never learn it.
        /// </summary>
        public bool FaceDown;
        /// <summary>
        /// Face-up art rolled sideways (Defense): its centre sits at street level
        /// beside the anchor instead of standing over it, so the art's middle is
        /// low and at one flank.
        /// </summary>
        public bool ArtSideways;
        /// <summary>
        /// Floor-local metres from the anchor to the art centre along the camera's
        /// right axis: 0 for upright art, about ±<see cref="ArtHalf"/> when sideways.
        /// </summary>
        public float ArtLateral;
        /// <summary>Half the face-up art's width, floor-local metres.</summary>
        public float ArtHalf;
        /// <summary>
        /// Floor-local metres: the tallest a camera-side piece may stand inside the
        /// art's lateral span (feet in grass for upright art; low for sideways art).
        /// </summary>
        public float FrontCoverHeight;
        /// <summary>
        /// Stable per-monster key (the terrain pad's instance id): seed per-monster
        /// variation from this, not from list slot or position, so a monster keeps
        /// its look while others come and go or it lunges.
        /// </summary>
        public int Key;
        /// <summary>
        /// Multiply host-local design sizes by this to get floor-local metres
        /// (e.g. <see cref="ArPlaymatLayout.SummonRingDiameter"/> × Scale = the ring's size here).
        /// It is the monster's RESTING scale: ground pieces stay put while the
        /// hologram flinches on a hit.
        /// </summary>
        public float Scale;
    }

    /// <summary>
    /// A Field Spell's signature set piece (Umi's sea, Sogen's grass…), built
    /// procedurally under <see cref="ArFieldSpellFloor"/> and ticked by it every
    /// frame while the field is visible. One instance per live field; destroyed
    /// when the field dissolves.
    /// <para>Contract for every kit (see <c>Docs/FIELD_SPELL_SOLID_VISION.md</c> §8):</para>
    /// <list type="bullet">
    /// <item>Passthrough first: per-monster pieces stay within
    /// <see cref="MaxAnchorRadius"/> × Scale of an anchor and below
    /// <see cref="MaxAnchorHeight"/> × Scale; street pieces stay thin, translucent and
    /// above head-clear height. Nothing is ever drawn across the open aisle floor.</item>
    /// <item>Every alpha × level (street) or × anchor.Presence (per monster).</item>
    /// <item>≤ 2 draw calls, ≤ 4096 vertices, zero per-frame allocations, unscaled time,
    /// no physics, no Find* calls, unlit materials from <see cref="VertexColorMaterial"/>.</item>
    /// <item>Colours come from <see cref="Env"/> (Sky / Ground / Accent), never hard-coded per card.</item>
    /// </list>
    /// </summary>
    public abstract class ArFieldSignature : MonoBehaviour
    {
        /// <summary>Per-monster pieces stay inside this radius (host-local metres, × anchor.Scale).</summary>
        public const float MaxAnchorRadius = 0.75f;
        /// <summary>Per-monster pieces stay below this height (host-local metres, × anchor.Scale).</summary>
        public const float MaxAnchorHeight = 0.6f;
        /// <summary>Street pieces keep their lowest point above this (floor-local metres, × HoloScale).</summary>
        public const float StreetClearHeight = 1.3f;
        public const int MaxVertices = 4096;
        /// <summary>
        /// Set card footprint half extents, host-local (× Scale): the landscape card
        /// is 1.40 across the street (X) and 0.91 along it (Z), pitched 18° off flat,
        /// with its low edge about 0.012 above the street.
        /// </summary>
        public const float SetCardHalfX = 0.72f;
        public const float SetCardHalfZ = 0.48f;
        /// <summary>Half-diagonal of the Set card footprint: nothing taller than
        /// <see cref="SetCardClearHeight"/> within this radius without <see cref="InSetCard"/>.</summary>
        public const float SetCardClearRadius = 0.87f;
        /// <summary>Pieces inside the Set card's footprint must stay below this (host-local, × Scale).</summary>
        public const float SetCardClearHeight = 0.01f;
        /// <summary>Camera-side cover over upright art: the art's lower ~22% (host-local, × Scale).</summary>
        public const float UprightCoverHeight = 0.30f;
        /// <summary>Camera-side cover over sideways (Defense) art, whose middle starts at the street.</summary>
        public const float SidewaysCoverHeight = 0.10f;
        /// <summary>Metres (× max(HoloScale, 0.5)) kept between the local camera and any street piece.</summary>
        public const float StreetCameraClear = 1.5f;
        /// <summary>
        /// Per-monster pieces stay this far (host-local, × Scale) on their own side of
        /// the street midline (z = 0): facing monsters' rings must not meet.
        /// </summary>
        public const float AisleClear = 0.1f;
        /// <summary>Per-monster pieces reach at most this far sideways (half the column pitch, × Scale).</summary>
        public const float LaneHalfWidth = 0.6f;
        /// <summary>Most face-up cards <see cref="CollectArtCards"/> tracks per frame.</summary>
        public const int MaxArtCards = 10;
        /// <summary>Fade band around a card's silhouette (host-local, × Scale) for <see cref="ArtClear"/>.</summary>
        public const float ArtSoft = 0.15f;

        /// <summary>Render queues: ground decorations sit over terrain pads, street pieces under motes.</summary>
        public const int GroundQueue = 2465;
        public const int StreetQueue = 3040;

        readonly List<Object> _owned = new();
        readonly ArtCard[] _artCards = new ArtCard[MaxArtCards];
        int _artCardCount;
        Vector3 _artEye;

        /// <summary>A face-up card in its camera-facing plane (floor-local metres).</summary>
        struct ArtCard
        {
            public Vector3 Foot;  // anchor on the street: the pivot the holo turns about
            public Vector3 N;     // plane normal, toward the eye
            public Vector3 L;     // camera-right in the plane (horizontal)
            public Vector3 U;     // up in the plane (tilted back toward the eye)
            public float HalfW;
            public float Lateral; // art centre offset along L (sideways Defense art)
            public bool Sideways;
            public float Soft;
        }
        uint _rng = 0x9E3779B9u;
        static Texture2D _softDot;

        protected FieldSpellEnvironment Env { get; private set; }
        protected int Variant => Env.SignatureVariant;
        protected float SigScale => Env.SignatureScale;
        protected int Layer { get; private set; }

        public static ArFieldSignature Create(FieldSpellEnvironment env, Transform parent, int layer)
        {
            var type = ArFieldSignatureKits.TypeFor(env.Signature);
            if (type == null || parent == null) return null;
            var go = new GameObject("FieldSignature_" + env.Signature);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var sig = (ArFieldSignature)go.AddComponent(type);
            sig.Env = env;
            sig.Layer = layer;
            sig._rng ^= (uint)env.CardId * 2654435761u;
            if (sig._rng == 0) sig._rng = 0x9E3779B9u;
            sig.Build();
            return sig;
        }

        /// <summary>Create meshes / materials / renderers once. Start hidden.</summary>
        protected abstract void Build();

        /// <summary>
        /// Called every frame by the floor while the field is visible.
        /// <paramref name="level"/> is the field's global 0…1 presence (sweep in, dissolve out).
        /// <paramref name="anchors"/> is reused by the caller — read it, never keep it.
        /// </summary>
        public abstract void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt);

        protected virtual void OnDestroy()
        {
            foreach (var o in _owned)
                if (o != null) ArObjectUtil.Destroy(o);
            _owned.Clear();
        }

        // ── Helpers (use these so every kit behaves the same) ───────────────

        /// <summary>
        /// Unlit, alpha-blended, vertex-coloured material (Sprites/Default is in
        /// Always Included Shaders). Texture defaults to a soft round dot; pass
        /// <c>Texture2D.whiteTexture</c> for flat colour. Destroyed with the kit.
        /// </summary>
        protected Material VertexColorMaterial(string name, int queue, Texture tex = null)
        {
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh) { name = name, renderQueue = queue };
            if (tex == null) tex = SoftDot();
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            _owned.Add(m);
            return m;
        }

        /// <summary>Empty dynamic mesh, destroyed with the kit.</summary>
        protected Mesh NewDynamicMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            _owned.Add(mesh);
            return mesh;
        }

        /// <summary>Texture owned (and destroyed) by the kit.</summary>
        protected Texture2D OwnTexture(Texture2D tex)
        {
            if (tex != null) _owned.Add(tex);
            return tex;
        }

        /// <summary>Child MeshRenderer on this kit's layer with holo renderer settings. Starts disabled.</summary>
        protected MeshRenderer AddMeshChild(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = Layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            mr.enabled = false;
            return mr;
        }

        /// <summary>Child LineRenderer in floor-local space (useWorldSpace = false). Starts disabled.</summary>
        protected LineRenderer AddLine(string name, Material mat, int positions)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = Layer;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = Mathf.Max(2, positions);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = mat;
            lr.enabled = false;
            return lr;
        }

        /// <summary>AR stage camera (cached per frame by ArStageView). May be null in edit mode.</summary>
        protected Camera StageCamera => ArStageView.FindCamera(transform);

        /// <summary>Deterministic xorshift in [a, b) — seeded per card, no GC.</summary>
        protected float R(float a, float b)
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return a + (b - a) * ((_rng & 0xFFFFFF) / 16777216f);
        }

        /// <summary>
        /// Palette colour → mesh vertex colour. The project renders in Linear
        /// space and Sprites/Default does not convert vertex colours, so raw sRGB
        /// palette values would show washed out. Convert once at build time and
        /// cache the result; alpha is unchanged.
        /// </summary>
        protected static Color VertexColor(Color srgb) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;

        /// <summary>
        /// ≥ 0 when floor-local point <paramref name="p"/> stays on the anchor's own side
        /// of the street midline by <see cref="AisleClear"/> × Scale; negative by how far it crosses.
        /// </summary>
        protected static float MidlineGap(in FieldAnchor a, Vector3 p)
        {
            var side = a.Position.z < 0f ? -1f : 1f;
            return side * p.z - AisleClear * a.Scale;
        }

        /// <summary>
        /// Face-up monster art this frame, for <see cref="ArtClear"/>. Call once per
        /// Tick. Each card turns to face the eye about its foot, as
        /// ArArenaCardVisual.FaceCamera does; sideways (Defense) art is centred at
        /// street level, <see cref="FieldAnchor.ArtLateral"/> to one side. Set cards
        /// lie flat and show no art, so they are skipped. No camera, no cards.
        /// </summary>
        protected void CollectArtCards(IReadOnlyList<FieldAnchor> anchors, in FieldStreet street)
        {
            _artCardCount = 0;
            if (anchors == null || !street.HasCamera) return;
            _artEye = street.Camera;
            for (var i = 0; i < anchors.Count && _artCardCount < MaxArtCards; i++)
            {
                var a = anchors[i];
                if (a.FaceDown || a.Scale <= 0f || a.ArtHalf <= 0f) continue;
                var n = _artEye - a.Position;
                if (n.sqrMagnitude < 1e-8f) continue;
                n.Normalize();
                var l = new Vector3(-n.z, 0f, n.x);
                if (l.sqrMagnitude < 1e-8f) l = Vector3.right;
                l.Normalize();
                ref var c = ref _artCards[_artCardCount++];
                c.Foot = a.Position;
                c.N = n;
                c.L = l;
                c.U = Vector3.Cross(l, n);
                c.HalfW = a.ArtHalf;
                c.Lateral = a.ArtLateral;
                c.Sideways = a.ArtSideways;
                c.Soft = ArtSoft * a.Scale;
            }
        }

        /// <summary>
        /// 0 where something at floor-local <paramref name="point"/> (anything within
        /// <paramref name="reach"/> of it) would draw over a face-up card's art as seen
        /// from the camera, 1 once it clears the art's silhouette by the fade band.
        /// Points behind a card need no fade: the opaque art hides them.
        /// Call <see cref="CollectArtCards"/> first in the same Tick.
        /// </summary>
        protected float ArtClear(Vector3 point, float reach = 0f)
        {
            if (_artCardCount == 0) return 1f;
            var d = point - _artEye;
            var d2 = d.sqrMagnitude;
            var clear = 1f;
            for (var i = 0; i < _artCardCount; i++)
            {
                ref var c = ref _artCards[i];
                // The ray must run toward the card's face to cross it in front of the eye.
                var dn = Vector3.Dot(d, c.N);
                if (dn > -1e-4f) continue;
                var t = Vector3.Dot(c.Foot - _artEye, c.N) / dn;
                if (t <= 0f || (1f - t) * -dn > reach) continue;
                // A sphere of radius reach around the point lands in the plane within
                // reach × t / cos(slant) of where the ray crosses it.
                var rr = reach * t * Mathf.Sqrt(d2 / (dn * dn));
                var q = _artEye + d * t - c.Foot;
                var x = Vector3.Dot(q, c.L);
                var y = Vector3.Dot(q, c.U);
                var gap = c.Sideways
                    ? Mathf.Max(Mathf.Abs(x - c.Lateral) - c.HalfW, Mathf.Abs(y) - c.HalfW)
                    : Mathf.Max(Mathf.Abs(x) - c.HalfW, Mathf.Max(y - 2f * c.HalfW, -y));
                clear = Mathf.Min(clear, Mathf.SmoothStep(0f, 1f, (gap - rr) / c.Soft));
            }

            return clear;
        }

        /// <summary>True when floor-local point <paramref name="p"/> (XZ) lies over a Set card's footprint.</summary>
        protected static bool InSetCard(in FieldAnchor a, Vector3 p, float margin = 0.03f)
        {
            if (!a.FaceDown) return false;
            return Mathf.Abs(p.x - a.Position.x) < (SetCardHalfX + margin) * a.Scale &&
                   Mathf.Abs(p.z - a.Position.z) < (SetCardHalfZ + margin) * a.Scale;
        }

        /// <summary>
        /// Tallest a piece may stand at floor-local point <paramref name="p"/> (XZ)
        /// near anchor <paramref name="a"/>: under a Set card, in front of the face-up
        /// art (camera side, inside its lateral span), or the kit's normal cap.
        /// Clamp heights with this so every kit treats cards the same way.
        /// </summary>
        protected static float CoverLimit(in FieldAnchor a, in FieldStreet street, Vector3 p) =>
            Mathf.Min(MaxAnchorHeight * a.Scale, CardLimit(a, street, p));

        /// <summary>
        /// <see cref="CoverLimit"/> for a piece of <paramref name="own"/>'s set piece,
        /// also honouring every OTHER monster's card: a Set card (0.72 × Scale each
        /// side) is wider than a lane (<see cref="LaneHalfWidth"/>), and sideways
        /// Defense art lies across a whole neighbouring lane, so a neighbour's pieces
        /// can land on them. Call per piece (≤ 10 anchors, no allocation).
        /// </summary>
        protected static float CoverLimitAll(in FieldAnchor own, IReadOnlyList<FieldAnchor> anchors,
            in FieldStreet street, Vector3 p)
        {
            var limit = CoverLimit(own, street, p);
            if (anchors == null) return limit;
            for (var i = 0; i < anchors.Count; i++)
            {
                var b = anchors[i];
                if (b.Key == own.Key) continue;
                // Nothing of b's reaches past its sideways art (2 × ArtHalf) or its Set card corner.
                var reach = Mathf.Max(2f * b.ArtHalf, SetCardClearRadius * b.Scale) + 0.05f * b.Scale;
                var dx = p.x - b.Position.x;
                var dz = p.z - b.Position.z;
                if (dx * dx + dz * dz > reach * reach) continue;
                limit = Mathf.Min(limit, CardLimit(b, street, p));
            }

            return limit;
        }

        /// <summary>
        /// Height limit that anchor <paramref name="a"/>'s card puts on floor-local point
        /// <paramref name="p"/>, or +∞ when its card does not constrain that point.
        /// </summary>
        static float CardLimit(in FieldAnchor a, in FieldStreet street, Vector3 p)
        {
            if (a.FaceDown)
                return InSetCard(a, p) ? SetCardClearHeight * a.Scale : float.PositiveInfinity;
            if (!street.HasCamera) return float.PositiveInfinity;
            var toCam = new Vector3(street.Camera.x - a.Position.x, 0f, street.Camera.z - a.Position.z);
            var d = new Vector3(p.x - a.Position.x, 0f, p.z - a.Position.z);
            if (Vector3.Dot(d, toCam) <= 0f) return float.PositiveInfinity; // behind the art plane: hidden by depth
            var len = toCam.magnitude;
            if (len < 1e-4f) return float.PositiveInfinity;
            var right = new Vector3(-toCam.z / len, 0f, toCam.x / len);
            var lateral = Vector3.Dot(d, right);
            return Mathf.Abs(lateral - a.ArtLateral) < a.ArtHalf ? a.FrontCoverHeight : float.PositiveInfinity;
        }

        /// <summary>Colour with alpha replaced.</summary>
        protected static Color WithAlpha(Color c, float a)
        {
            c.a = Mathf.Clamp01(a);
            return c;
        }

        /// <summary>Stable 0…1 hash for an anchor slot so per-monster detail doesn't reshuffle each frame.</summary>
        protected static float Hash01(int i, int salt)
        {
            unchecked
            {
                var h = (uint)(i * 374761393 + salt * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Shared 32² soft round dot (alpha falloff).</summary>
        protected static Texture2D SoftDot()
        {
            if (_softDot != null) return _softDot;
            const int n = 32;
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var dx = (x + 0.5f) / n * 2f - 1f;
                var dy = (y + 0.5f) / n * 2f - 1f;
                var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * n + x] = new Color(1f, 1f, 1f, a * a);
            }

            _softDot = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "FieldSignatureDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _softDot.SetPixels32(px);
            _softDot.Apply(false, true);
            return _softDot;
        }
    }
}
