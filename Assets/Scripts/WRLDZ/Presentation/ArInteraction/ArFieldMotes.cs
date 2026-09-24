using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Field Spell atmosphere — embers, bubbles, leaves… drifting through the
    /// street volume. One dynamic mesh of camera-facing quads (one draw call on
    /// phone AR). Motion comes from <see cref="FieldMotes"/>, colour from the
    /// environment accent. Driven by <see cref="ArFieldSpellFloor"/>.
    /// </summary>
    [DefaultExecutionOrder(760)]
    public class ArFieldMotes : MonoBehaviour
    {
        const int Pool = 56;

        struct Mote
        {
            public Vector3 P;
            public Vector3 V;
            public float Phase;
            public float Size;
            public float Life;
            public float Age;
        }

        readonly Mote[] _m = new Mote[Pool];
        Mesh _mesh;
        Vector3[] _verts;
        Color32[] _cols;
        MeshRenderer _mr;
        Material _mat;
        static Texture2D _dot;

        FieldMotes _kind = FieldMotes.None;
        Color _color = Color.white;
        float _density;
        float _level;
        Vector3 _center;
        Vector3 _half = Vector3.one;
        uint _rng = 0x9E3779B9u;

        public static ArFieldMotes Create(Transform parent, int layer)
        {
            var go = new GameObject("FieldMotes");
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var m = go.AddComponent<ArFieldMotes>();
            m.Build();
            return m;
        }

        void Build()
        {
            _verts = new Vector3[Pool * 4];
            _cols = new Color32[Pool * 4];
            var uvs = new Vector2[Pool * 4];
            var tris = new int[Pool * 6];
            for (var i = 0; i < Pool; i++)
            {
                var v = i * 4;
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);
                var t = i * 6;
                tris[t] = v;
                tris[t + 1] = v + 2;
                tris[t + 2] = v + 1;
                tris[t + 3] = v;
                tris[t + 4] = v + 3;
                tris[t + 5] = v + 2;
            }

            _mesh = new Mesh { name = "FieldMotes" };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = uvs;
            _mesh.colors32 = _cols;
            _mesh.triangles = tris;

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(sh) { name = "FieldMotesMat", renderQueue = 3050 };
            if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", SoftDot());
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", SoftDot());
            _mr.sharedMaterial = _mat;
            ArAnimePresentation.ConfigureHoloRenderer(_mr);
            _mr.enabled = false;
        }

        void OnDestroy()
        {
            ArObjectUtil.Destroy(_mesh);
            ArObjectUtil.Destroy(_mat);
        }

        public void SetEnvironment(FieldSpellEnvironment env)
        {
            _kind = env.Motes;
            // Linear project: Sprites/Default does not convert vertex colours.
            _color = QualitySettings.activeColorSpace == ColorSpace.Linear ? env.Accent.linear : env.Accent;
            _density = env.MoteDensity;
            _rng ^= (uint)env.CardId * 2654435761u;
            if (_rng == 0) _rng = 0x9E3779B9u; // xorshift never leaves 0
            for (var i = 0; i < Pool; i++)
                Spawn(ref _m[i], scatter: true);
        }

        public void SetLevel(float level) => _level = Mathf.Clamp01(level);

        public void SetVolume(Vector3 center, Vector3 half)
        {
            if (center == _center && half == _half) return;
            _center = center;
            _half = new Vector3(Mathf.Max(0.1f, half.x), Mathf.Max(0.1f, half.y), Mathf.Max(0.1f, half.z));
            _mesh.bounds = new Bounds(_center, _half * 2f + Vector3.one * 0.5f);
        }

        void LateUpdate()
        {
            var alive = Mathf.RoundToInt(Pool * _density);
            if (_kind == FieldMotes.None || _level <= 0.001f || alive <= 0)
            {
                if (_mr.enabled) _mr.enabled = false;
                return;
            }

            var cam = ArStageView.FindCamera(transform);
            if (cam == null) return;
            var right = transform.InverseTransformDirection(cam.transform.right);
            var up = transform.InverseTransformDirection(cam.transform.up);
            var fwd = transform.InverseTransformDirection(cam.transform.forward);
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var sc = Mathf.Max(0.3f, ArPlaymatLayout.LiveHoloScale);
            var stretch = _kind == FieldMotes.Wind ? 6f : _kind == FieldMotes.Sparks ? 2.5f : 1f;

            for (var i = 0; i < Pool; i++)
            {
                var v = i * 4;
                if (i >= alive)
                {
                    _verts[v] = _verts[v + 1] = _verts[v + 2] = _verts[v + 3] = _center;
                    _cols[v] = _cols[v + 1] = _cols[v + 2] = _cols[v + 3] = default;
                    continue;
                }

                ref var m = ref _m[i];
                m.Age += dt;
                m.P += Drift(ref m) * (dt * sc);
                if (m.Age >= m.Life || !InVolume(m.P))
                    Spawn(ref m, scatter: false);

                var life = m.Age / m.Life;
                var a = Mathf.Clamp01(life / 0.15f) * Mathf.Clamp01((1f - life) / 0.25f) * Alpha(ref m) * _level;
                var c = _color;
                c.a = a;
                Color32 c32 = c;
                _cols[v] = _cols[v + 1] = _cols[v + 2] = _cols[v + 3] = c32;

                // Streak along the on-screen direction of travel (wind, sparks).
                var s = m.Size * sc;
                var along = Vector3.ProjectOnPlane(m.V, fwd);
                var ax = stretch > 1f && along.sqrMagnitude > 1e-6f ? along.normalized * (s * stretch) : right * s;
                var ay = stretch > 1f && along.sqrMagnitude > 1e-6f
                    ? Vector3.Cross(fwd, along.normalized) * s
                    : up * s;
                _verts[v] = m.P - ax - ay;
                _verts[v + 1] = m.P + ax - ay;
                _verts[v + 2] = m.P + ax + ay;
                _verts[v + 3] = m.P - ax + ay;
            }

            _mesh.vertices = _verts;
            _mesh.colors32 = _cols;
            if (!_mr.enabled) _mr.enabled = true;
        }

        /// <summary>Per-frame velocity (m/s at full street scale) for each mote kind.</summary>
        Vector3 Drift(ref Mote m)
        {
            var s = Mathf.Sin(m.Age * 1.6f + m.Phase);
            switch (_kind)
            {
                case FieldMotes.Bubbles:
                    return m.V + new Vector3(Mathf.Sin(m.Age * 2.2f + m.Phase) * 0.08f, 0f, 0f);
                case FieldMotes.Leaves:
                    return m.V + new Vector3(s * 0.35f, 0f, Mathf.Cos(m.Age * 1.3f + m.Phase) * 0.2f);
                case FieldMotes.Feathers:
                    return m.V + new Vector3(Mathf.Sin(m.Age * 1.1f + m.Phase) * 0.5f, 0f, 0f);
                case FieldMotes.Pollen:
                    return m.V + new Vector3(s * 0.05f, Mathf.Cos(m.Age * 0.9f + m.Phase) * 0.04f, 0f);
                case FieldMotes.Wisps:
                {
                    // Slow rise with a swirl around the street centre.
                    var r = m.P - _center;
                    var swirl = new Vector3(-r.z, 0f, r.x).normalized * 0.25f;
                    return m.V + swirl;
                }
                default:
                    return m.V;
            }
        }

        float Alpha(ref Mote m)
        {
            switch (_kind)
            {
                case FieldMotes.Embers:
                    return 0.6f + 0.4f * Mathf.Sin(m.Age * 18f + m.Phase);
                case FieldMotes.Sparks:
                    return Mathf.Sin(m.Age * 25f + m.Phase) > 0f ? 1f : 0.35f;
                case FieldMotes.Dust:
                case FieldMotes.Wisps:
                    return 0.45f;
                case FieldMotes.Pollen:
                    return 0.6f + 0.25f * Mathf.Sin(m.Age * 2f + m.Phase);
                default:
                    return 0.8f;
            }
        }

        bool InVolume(Vector3 p)
        {
            var d = p - _center;
            return Mathf.Abs(d.x) <= _half.x && Mathf.Abs(d.z) <= _half.z &&
                   d.y >= -_half.y - 0.05f && d.y <= _half.y + 0.05f;
        }

        /// <summary>
        /// New mote. <paramref name="scatter"/> fills the whole volume with a random
        /// age (first frame looks settled); otherwise rising kinds start low and
        /// falling kinds start high so they cross the street.
        /// </summary>
        void Spawn(ref Mote m, bool scatter)
        {
            float lo = 0f, hi = 1f;
            switch (_kind)
            {
                case FieldMotes.Embers:
                    m.V = new Vector3(R(-0.1f, 0.1f), R(0.35f, 0.7f), R(-0.1f, 0.1f));
                    m.Size = R(0.025f, 0.045f); m.Life = R(2.5f, 4f); hi = 0.3f;
                    break;
                case FieldMotes.Bubbles:
                    m.V = new Vector3(0f, R(0.15f, 0.3f), 0f);
                    m.Size = R(0.03f, 0.06f); m.Life = R(4f, 7f); hi = 0.3f;
                    break;
                case FieldMotes.Leaves:
                    m.V = new Vector3(0f, R(-0.3f, -0.18f), 0f);
                    m.Size = R(0.04f, 0.06f); m.Life = R(5f, 8f); lo = 0.7f;
                    break;
                case FieldMotes.Dust:
                    m.V = new Vector3(R(0.25f, 0.45f) * (R(0f, 1f) < 0.5f ? -1f : 1f), R(-0.02f, 0.04f), R(-0.1f, 0.1f));
                    m.Size = R(0.03f, 0.05f); m.Life = R(4f, 6f); hi = 0.3f;
                    break;
                case FieldMotes.Pollen:
                    m.V = new Vector3(R(-0.03f, 0.03f), R(-0.02f, 0.02f), R(-0.03f, 0.03f));
                    m.Size = R(0.02f, 0.035f); m.Life = R(5f, 9f); lo = 0.2f; hi = 0.9f;
                    break;
                case FieldMotes.Wind:
                    m.V = new Vector3(R(1.2f, 2f) * (R(0f, 1f) < 0.5f ? -1f : 1f), R(0f, 0.1f), 0f);
                    m.Size = R(0.015f, 0.025f); m.Life = R(1.5f, 2.5f); lo = 0.1f;
                    break;
                case FieldMotes.Sparks:
                {
                    var dir = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;
                    m.V = dir * R(0.3f, 0.6f);
                    m.Size = R(0.02f, 0.03f); m.Life = R(0.4f, 0.9f);
                    break;
                }
                case FieldMotes.Wisps:
                    m.V = new Vector3(0f, R(0.12f, 0.25f), 0f);
                    m.Size = R(0.05f, 0.09f); m.Life = R(4f, 7f); hi = 0.4f;
                    break;
                case FieldMotes.Feathers:
                    m.V = new Vector3(0f, R(-0.14f, -0.08f), 0f);
                    m.Size = R(0.04f, 0.06f); m.Life = R(7f, 10f); lo = 0.7f;
                    break;
                default:
                    m.V = Vector3.zero; m.Size = 0f; m.Life = 1f;
                    break;
            }

            if (scatter) { lo = 0f; hi = 1f; }
            m.P = _center + new Vector3(
                R(-_half.x, _half.x),
                -_half.y + 2f * _half.y * R(lo, hi),
                R(-_half.z, _half.z));
            m.Phase = R(0f, 6.283f);
            m.Age = scatter ? R(0f, m.Life) : 0f;
        }

        float R(float a, float b)
        {
            // xorshift32 — deterministic, no GC.
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return a + (b - a) * ((_rng & 0xFFFFFF) / 16777216f);
        }

        static Texture2D SoftDot()
        {
            if (_dot != null) return _dot;
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

            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "FieldMoteDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _dot.SetPixels32(px);
            _dot.Apply(false, true);
            return _dot;
        }
    }
}
