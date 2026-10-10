using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Canvas-safe particles: ParticleSystem cannot render inside a ScreenSpaceOverlay canvas,
    /// so embers / dust / sparks are pooled <see cref="Image"/> motes under an "EmberField"
    /// child of the host. Unscaled time, no per-frame allocations after warm-up.
    /// </summary>
    public sealed class UiEmberField : MonoBehaviour
    {
        public enum Kind { Ember, Dust, Spark }

        /// <summary>Slow floating dust + rare embers across the host rect.</summary>
        public bool Ambient = true;

        /// <summary>Ambient motes spawned per second.</summary>
        public float AmbientRate = 5f;

        public RectTransform Host { get; private set; }
        public int PoolSize => _pool != null ? _pool.Length : 0;
        public int Alive => _alive;

        struct Mote
        {
            public Image Img;
            public RectTransform Rt;
            public Vector2 Pos;
            public Vector2 Vel;
            public float Life;
            public float MaxLife;
            public float Size;
            public float Rot;
            public float Spin;
            public float Wobble;
            public Kind Kind;
            public Color Tint;
            public bool Alive;
        }

        static int _seedCounter;

        RectTransform _layer;
        Mote[] _pool;
        Sprite _ember;
        Sprite _dust;
        Sprite _spark;
        System.Random _rng;
        float _ambientAcc;
        float _saverPoll;
        bool _saver;
        int _alive;
        int _cursor;

        const float MaxDt = 0.05f;

        // ------------------------------------------------------------------ builders

        public static UiEmberField Attach(RectTransform host, int poolSize = 48, bool ambient = true)
        {
            if (host == null) return null;
            var existing = host.GetComponent<UiEmberField>();
            if (existing != null)
            {
                if (ambient) existing.Ambient = true;
                return existing;
            }

            var f = host.gameObject.AddComponent<UiEmberField>();
            f.Ambient = ambient;
            f.Build(Mathf.Clamp(poolSize, 8, 256));
            return f;
        }

        /// <summary>Find-or-attach the shared field on the canvas root (ambient off by default).</summary>
        public static UiEmberField Field(Canvas canvas)
        {
            return canvas != null ? Field(canvas.transform) : null;
        }

        /// <summary>Find-or-attach on the root canvas above <paramref name="any"/>.</summary>
        public static UiEmberField Field(Transform any)
        {
            if (any == null) return null;
            var canvas = any.GetComponentInParent<Canvas>();
            Transform rootT = canvas != null ? canvas.rootCanvas.transform : any.root;
            var rt = rootT as RectTransform;
            if (rt == null) return null;
            var f = rt.GetComponent<UiEmberField>();
            return f != null ? f : Attach(rt, 48, ambient: false);
        }

        void Build(int poolSize)
        {
            Host = (RectTransform)transform;
            _rng = new System.Random(unchecked(System.Environment.TickCount * 17 + (++_seedCounter)));

            _ember = EldritchAssets.EmberSoft() ?? UiFoundation.WhiteSprite();
            _dust = EldritchAssets.DustMote() ?? _ember;
            _spark = EldritchAssets.ArcaneSpark() ?? _ember;

            var existing = transform.Find("EmberField");
            if (existing != null)
            {
                _layer = existing as RectTransform;
            }
            else
            {
                var go = new GameObject("EmberField", typeof(RectTransform), typeof(LayoutElement));
                go.transform.SetParent(transform, false);
                _layer = go.GetComponent<RectTransform>();
                go.GetComponent<LayoutElement>().ignoreLayout = true;
            }

            // Motes move every frame: their own canvas keeps the host canvas from re-batching.
            // No override sorting — it still draws in hierarchy order (last sibling, on top).
            UiFoundation.GetOrAdd<Canvas>(_layer.gameObject);

            _layer.anchorMin = Vector2.zero;
            _layer.anchorMax = Vector2.one;
            _layer.offsetMin = Vector2.zero;
            _layer.offsetMax = Vector2.zero;
            _layer.pivot = new Vector2(0.5f, 0.5f);
            _layer.SetAsLastSibling();

            _pool = new Mote[poolSize];
            for (var i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Mote", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_layer, false);
                var img = go.GetComponent<Image>();
                img.sprite = _ember;
                img.raycastTarget = false;
                img.maskable = false;
                img.preserveAspect = true;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12f, 12f);
                go.SetActive(false);
                _pool[i].Img = img;
                _pool[i].Rt = rt;
                _pool[i].Alive = false;
            }

            _alive = 0;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Burst at a position in the host's local space.</summary>
        public void Burst(Vector2 localPos, int count, Kind kind, Color tint)
        {
            if (_pool == null || Host == null || _layer == null) return;
            if (!Application.isPlaying) return; // edit mode: static frame, nothing to animate
            var world = Host.TransformPoint(localPos);
            var p = (Vector2)_layer.InverseTransformPoint(world);
            for (var i = 0; i < count; i++)
                Spawn(p + Jitter(6f), kind, tint);
        }

        /// <summary>Burst from random points inside <paramref name="rt"/>.</summary>
        public void BurstFromRect(RectTransform rt, int count, Kind kind, Color tint)
        {
            BurstFromRect(rt, count, kind, tint, 0f, 1f);
        }

        /// <summary>Burst from a vertical band of <paramref name="rt"/> (0 = bottom, 1 = top).</summary>
        public void BurstFromRect(RectTransform rt, int count, Kind kind, Color tint, float yMin01, float yMax01)
        {
            if (_pool == null || _layer == null || rt == null) return;
            if (!Application.isPlaying) return;
            var r = rt.rect;
            for (var i = 0; i < count; i++)
            {
                var lx = Mathf.Lerp(r.xMin, r.xMax, Rand01());
                var ly = Mathf.Lerp(r.yMin, r.yMax, Mathf.Lerp(yMin01, yMax01, Rand01()));
                var world = rt.TransformPoint(new Vector3(lx, ly, 0f));
                var p = (Vector2)_layer.InverseTransformPoint(world);
                Spawn(p, kind, tint);
            }
        }

        /// <summary>Kill every live mote (menu teardown / battery saver flip).</summary>
        public void Clear()
        {
            if (_pool == null) return;
            for (var i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;
                _pool[i].Alive = false;
                if (_pool[i].Img != null) _pool[i].Img.gameObject.SetActive(false);
            }

            _alive = 0;
        }

        // ------------------------------------------------------------------ sim

        void Update()
        {
            if (_pool == null || !Application.isPlaying) return;
            var dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
            if (dt <= 0f) return;

            _saverPoll -= dt;
            if (_saverPoll <= 0f)
            {
                _saverPoll = 1f;
                _saver = WrldzTheme.BatterySaver; // persisted static — no scene search
            }

            if (Ambient && !_saver && Host != null)
            {
                _ambientAcc += Mathf.Max(0f, AmbientRate) * dt;
                var rect = Host.rect;
                while (_ambientAcc >= 1f)
                {
                    _ambientAcc -= 1f;
                    var lx = Mathf.Lerp(rect.xMin, rect.xMax, Rand01());
                    var ly = Mathf.Lerp(rect.yMin, rect.yMax, Rand01() * 0.9f);
                    var world = Host.TransformPoint(new Vector3(lx, ly, 0f));
                    var p = (Vector2)_layer.InverseTransformPoint(world);
                    var ember = Rand01() < 0.14f;
                    Spawn(p, ember ? Kind.Ember : Kind.Dust,
                        ember ? EldritchPalette.MillenniumGold : EldritchPalette.Sandstone);
                }
            }
            else
            {
                _ambientAcc = 0f;
            }

            if (_alive == 0) return;
            var time = Time.unscaledTime;
            for (var i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Alive) continue;
                Step(ref _pool[i], dt, time);
            }
        }

        void Step(ref Mote m, float dt, float time)
        {
            m.Life -= dt;
            if (m.Life <= 0f || m.Img == null)
            {
                m.Alive = false;
                _alive--;
                if (m.Img != null) m.Img.gameObject.SetActive(false);
                return;
            }

            var u = 1f - m.Life / m.MaxLife; // 0 → 1 over life
            float alpha;
            float size = m.Size;
            switch (m.Kind)
            {
                case Kind.Ember:
                    m.Vel.y += 90f * dt;            // negative gravity: embers rise
                    m.Vel.x += Mathf.Sin(time * 3.1f + m.Wobble) * 26f * dt;
                    m.Vel *= 1f - 0.9f * dt;        // drag
                    alpha = 1f - u * u;
                    size = m.Size * (1f - 0.45f * u);
                    break;
                case Kind.Dust:
                    m.Vel.y += 12f * dt;
                    m.Vel.x += Mathf.Sin(time * 0.9f + m.Wobble) * 9f * dt;
                    m.Vel *= 1f - 0.4f * dt;
                    alpha = Mathf.Sin(u * Mathf.PI); // fade in, fade out
                    break;
                default: // Spark
                    m.Vel.y -= 320f * dt;           // sparks arc and fall
                    m.Vel *= 1f - 2.2f * dt;
                    alpha = 1f - u;
                    size = m.Size * (1f - 0.6f * u);
                    m.Rot = Mathf.Atan2(m.Vel.y, m.Vel.x) * Mathf.Rad2Deg;
                    break;
            }

            m.Pos += m.Vel * dt;
            if (m.Kind != Kind.Spark) m.Rot += m.Spin * dt;

            m.Rt.anchoredPosition = m.Pos;
            m.Rt.localScale = new Vector3(size / 12f, size / 12f, 1f);
            m.Rt.localRotation = Quaternion.Euler(0f, 0f, m.Rot);
            var c = m.Tint;
            c.a = m.Tint.a * Mathf.Clamp01(alpha);
            m.Img.color = c;
        }

        void Spawn(Vector2 pos, Kind kind, Color tint)
        {
            // Round-robin: steal the oldest slot when saturated so bursts never stall.
            var idx = -1;
            for (var n = 0; n < _pool.Length; n++)
            {
                var i = (_cursor + n) % _pool.Length;
                if (_pool[i].Alive) continue;
                idx = i;
                break;
            }

            if (idx < 0)
            {
                idx = _cursor;
                _alive--;
            }

            _cursor = (idx + 1) % _pool.Length;
            ref var m = ref _pool[idx];
            m.Kind = kind;
            m.Pos = pos;
            m.Tint = tint;
            m.Wobble = Rand01() * 6.28f;
            m.Rot = Rand01() * 360f;
            m.Spin = (Rand01() - 0.5f) * 90f;
            switch (kind)
            {
                case Kind.Ember:
                    m.MaxLife = Mathf.Lerp(0.9f, 1.7f, Rand01());
                    m.Size = Mathf.Lerp(9f, 22f, Rand01());
                    m.Vel = new Vector2((Rand01() - 0.5f) * 90f, Mathf.Lerp(40f, 130f, Rand01()));
                    m.Img.sprite = _ember;
                    break;
                case Kind.Dust:
                    m.MaxLife = Mathf.Lerp(2.4f, 4.6f, Rand01());
                    m.Size = Mathf.Lerp(5f, 13f, Rand01());
                    m.Vel = new Vector2((Rand01() - 0.5f) * 30f, Mathf.Lerp(6f, 26f, Rand01()));
                    m.Tint.a = tint.a * Mathf.Lerp(0.35f, 0.7f, Rand01());
                    m.Img.sprite = _dust;
                    break;
                default:
                    m.MaxLife = Mathf.Lerp(0.22f, 0.5f, Rand01());
                    m.Size = Mathf.Lerp(8f, 18f, Rand01());
                    var a = Rand01() * Mathf.PI * 2f;
                    var sp = Mathf.Lerp(260f, 540f, Rand01());
                    m.Vel = new Vector2(Mathf.Cos(a) * sp, Mathf.Abs(Mathf.Sin(a)) * sp * 0.8f + 60f);
                    m.Img.sprite = _spark;
                    break;
            }

            m.Life = m.MaxLife;
            m.Rt.anchoredPosition = pos;
            m.Rt.localScale = new Vector3(m.Size / 12f, m.Size / 12f, 1f);
            m.Rt.localRotation = Quaternion.Euler(0f, 0f, m.Rot);
            var c = m.Tint;
            c.a = kind == Kind.Dust ? 0f : m.Tint.a;
            m.Img.color = c;
            m.Img.gameObject.SetActive(true);
            m.Alive = true;
            _alive++;
        }

        Vector2 Jitter(float px) => new((Rand01() - 0.5f) * px * 2f, (Rand01() - 0.5f) * px * 2f);

        float Rand01() => (float)_rng.NextDouble();

        void OnDisable()
        {
            Clear();
        }

    }

    /// <summary>
    /// Contract §4 names this helper for the hover / open bursts. Forwards to
    /// <see cref="UiEmberField.Field(Canvas)"/> so both spellings work.
    /// </summary>
    public static class EldritchMenuFx
    {
        public static UiEmberField Field(Canvas canvas) => UiEmberField.Field(canvas);
        public static UiEmberField Field(Transform any) => UiEmberField.Field(any);
    }
}
