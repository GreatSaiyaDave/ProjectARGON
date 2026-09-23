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

        public FieldStreet(Vector3 center, Vector3 half, float holoScale)
        {
            Center = center;
            Half = half;
            HoloScale = holoScale;
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
        /// Multiply host-local design sizes by this to get floor-local metres
        /// (e.g. <see cref="ArPlaymatLayout.SummonRingDiameter"/> × Scale = the ring's size here).
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

        /// <summary>Render queues: ground decorations sit over terrain pads, street pieces under motes.</summary>
        public const int GroundQueue = 2465;
        public const int StreetQueue = 3040;

        readonly List<Object> _owned = new();
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
