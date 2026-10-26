using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>The generated glow atlases (<see cref="MenuV6Glow.Pixels"/>) as Unity textures: one per exponent, sRGB,
    /// no mips, never saved with a scene.</summary>
    public static class MenuV6GlowTex
    {
        static readonly Dictionary<int, Texture2D> Atlases = new();

        public static Texture2D Atlas(float gamma)
        {
            var key = Mathf.RoundToInt(gamma * 100f);
            if (Atlases.TryGetValue(key, out var hit) && hit != null) return hit;
            var tex = new Texture2D(MenuV6Glow.Width, MenuV6Glow.Height, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "MenuV6Glow_" + key
            };
            tex.SetPixels32(MenuV6Glow.Pixels(gamma));
            tex.Apply(false, true);
            Atlases[key] = tex;
            return tex;
        }
    }

    /// <summary>Maps stage px into the local space of a RectTransform that covers the stage rect <see cref="Frame"/>.</summary>
    public struct StageMap
    {
        public Rect Frame;
        public Rect Local;

        public StageMap(Rect frame, Rect local)
        {
            Frame = frame;
            Local = local;
        }

        public Vector3 P(double x, double y)
        {
            var sx = Frame.width > 0 ? Local.width / Frame.width : 1f;
            var sy = Frame.height > 0 ? Local.height / Frame.height : 1f;
            return new Vector3(Local.xMin + (float)(x - Frame.x) * sx, Local.yMax - (float)(y - Frame.y) * sy, 0f);
        }
    }

    /// <summary>A rig's vector layer on UI/Default: the <see cref="ShapeList"/> mapped into this graphic's rect. P8 (PERF_PLAN
    /// step 3, <see cref="MenuV6Perf.ShapesOnAtlas"/>): it samples the 8×8 white block of the atlas page its rig's quads draw
    /// from (white × vertex colour, as UGUI's white texture gives), so the rig's shape and quad layers share one texture and
    /// batch; with no such page (no atlas, the switch off, a stencil mask) it keeps UGUI's white texture.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RigShapes : MaskableGraphic
    {
        public Rect Frame;
        ShapeList _s = new();
        static readonly List<UIVertex> Verts = new();

        public ShapeLayer Layer { get; private set; }

        Texture _page;
        Vector2 _pageUv;
        bool _pageResolved;

        /// <summary>P8: the atlas page this layer draws from (null = UGUI's white texture).</summary>
        public Texture Page
        {
            get
            {
                ResolvePage();
                return _page;
            }
        }

        public override Texture mainTexture
        {
            get
            {
                ResolvePage();
                return _page != null ? _page : s_WhiteTexture;
            }
        }

        /// <summary>Once per layer, at its first canvas update (its rig's quads exist by then; a rebuilt menu makes new
        /// layers, so a switch change takes effect on the next build).</summary>
        void ResolvePage()
        {
            if (_pageResolved) return;
            _pageResolved = true;
            _page = null;
            _pageUv = Vector2.zero;
            // a stencil mask's shape keeps the white texture (its alpha is what it stencils)
            if (!MenuV6Perf.ShapesOn || TryGetComponent(out Mask _)) return;
            var p = MenuV6Perf.HostPage(transform, out var uv);
            if (p == null) return;
            _page = p;
            _pageUv = uv;
        }

        public static RigShapes Create(Transform parent, string name, Rect frame)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RigShapes));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<RigShapes>();
            g.Frame = frame;
            g.raycastTarget = false;
            UiTheme.Stretch(g.rectTransform);
            g.Layer = new ShapeLayer { Name = name };
            g._s = g.Layer.S;
            g.Layer.OnCommit = g.Dirty;
            return g;
        }

        int _lastCount = -1;

        /// <summary>Rebuild the mesh unless it was empty and stays empty (most hover / press layers at rest).</summary>
        void Dirty()
        {
            var n = _s.V.Count;
            if (n == 0 && _lastCount == 0) return;
            _lastCount = n;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_s.V.Count == 0) return;
            ResolvePage();
            var uv0 = _page != null ? _pageUv : Vector2.zero;
            var map = new StageMap(Frame, rectTransform.rect);
            Verts.Clear();
            foreach (var sv in _s.V)
            {
                var v = UIVertex.simpleVert;
                v.position = map.P(sv.X, sv.Y);
                v.color = sv.C;
                v.uv0 = uv0;
                Verts.Add(v);
            }

            vh.AddUIVertexStream(Verts, _s.I);
        }
    }

    /// <summary>A rig's textured layer of one texture on UI/Default: the <see cref="QuadList"/> mapped into this graphic.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RigQuads : MaskableGraphic
    {
        public Rect Frame;
        QuadList _q = new();
        RigTex _tex;
        static readonly List<UIVertex> Verts = new();

        public QuadLayer Layer { get; private set; }

        public override Texture mainTexture => _tex != null && _tex.Tex != null ? _tex.Tex : s_WhiteTexture;

        public static RigQuads Create(Transform parent, string name, Rect frame, RigTex tex)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RigQuads));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<RigQuads>();
            g.Frame = frame;
            g._tex = tex;
            g.raycastTarget = false;
            UiTheme.Stretch(g.rectTransform);
            g.Layer = new QuadLayer { Name = name };
            g._q = g.Layer.Q;
            g.Layer.Tex = tex;
            g.Layer.OnTex = t =>
            {
                g._tex = t;
                g.SetMaterialDirty();
            };
            g.Layer.OnCommit = g.Dirty;
            return g;
        }

        int _lastCount = -1;

        /// <summary>Rebuild the mesh unless it was empty and stays empty.</summary>
        void Dirty()
        {
            var n = _q.V.Count;
            if (n == 0 && _lastCount == 0) return;
            _lastCount = n;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_q.V.Count == 0) return;
            var map = new StageMap(Frame, rectTransform.rect);
            // a sprite inside a bigger texture: its UV rect remaps the quad's 0..1 UVs
            var r = _tex != null ? _tex.Uv : QuadList.Full;
            Verts.Clear();
            foreach (var sv in _q.V)
            {
                var v = UIVertex.simpleVert;
                v.position = map.P(sv.X, sv.Y);
                v.color = sv.C;
                v.uv0 = new Vector2(r.x + sv.U * r.width, r.y + sv.V * r.height);
                Verts.Add(v);
            }

            vh.AddUIVertexStream(Verts, _q.I);
        }
    }

    /// <summary>The UGUI side of <see cref="IRigGfx"/>: graphics stretched over their host, art through MenuV6Art only
    /// (Resources.Load; MenuV6Local paths through LocalOr, so ForceNoLocal / a clone gets null and the rig keeps the
    /// P1a still).</summary>
    public sealed class UguiGfx : IRigGfx
    {
        static Transform T(object host) => host as Transform;

        public ShapeLayer Shapes(object host, string name, Rect frame) => RigShapes.Create(T(host), name, frame).Layer;
        public QuadLayer Quads(object host, string name, Rect frame, RigTex tex) => RigQuads.Create(T(host), name, frame, tex).Layer;

        public object Clip(object host, string name, Rect clip, Rect frame)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(T(host), false);
            var rt = go.GetComponent<RectTransform>();
            MenuV6Layout.Place(rt, clip, frame);
            return rt;
        }

        public object PolyMask(object host, string name, Rect frame, out ShapeLayer shape)
        {
            var g = RigShapes.Create(T(host), name, frame);
            g.Layer.Feather = 0f;
            g.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            shape = g.Layer;
            return g.transform;
        }

        sealed class SpriteMaskImpl : ISpriteMask
        {
            public RectTransform Child;
            public Image Img;

            public void Set(double dx, double dy, float alpha)
            {
                if (Child == null || Img == null) return;
                var off = new Vector2((float)dx, (float)-dy);
                Child.offsetMin = off;
                Child.offsetMax = off;
                Img.color = new Color(0f, 0f, 0f, alpha);
            }
        }

        public ISpriteMask SpriteMask(object host, string name, Rect rect, Rect frame, RigTex stencil, RigTex child)
        {
            var st = stencil?.Sprite;
            var ch = child?.Sprite;
            if (st == null || ch == null) return null;
            var m = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Mask));
            m.transform.SetParent(T(host), false);
            var mi = m.GetComponent<Image>();
            mi.sprite = st;
            mi.raycastTarget = false;
            m.GetComponent<Mask>().showMaskGraphic = false;
            MenuV6Layout.Place(mi.rectTransform, rect, frame);
            var c = new GameObject(name + "_child", typeof(RectTransform), typeof(Image));
            c.transform.SetParent(m.transform, false);
            var ci = c.GetComponent<Image>();
            ci.sprite = ch;
            ci.raycastTarget = false;
            ci.color = new Color(0f, 0f, 0f, 0f);
            UiTheme.Stretch(ci.rectTransform);
            return new SpriteMaskImpl { Child = ci.rectTransform, Img = ci };
        }

        public RigTex Sprite(string path) => Of(path, MenuV6Art.Sprite(path));
        public RigTex Local(string path) => Of(path, MenuV6Art.LocalOr(path, null));

        public RigTex Sheet(string path)
        {
            var t = MenuV6Art.Texture(path);
            return t == null ? null : new RigTex { Key = path, Tex = t, W = t.width, H = t.height };
        }

        public RigTex Glow(float gamma)
        {
            var t = MenuV6GlowTex.Atlas(gamma);
            return new RigTex { Key = "glow:" + Mathf.RoundToInt(gamma * 100f), Tex = t, W = t.width, H = t.height };
        }

        static RigTex Of(string path, Sprite s)
        {
            if (s == null || s.texture == null) return null;
            var tex = s.texture;
            var r = s.textureRect;
            float w = tex.width, h = tex.height;
            return new RigTex
            {
                Key = path, Tex = tex, Sprite = s, W = (int)r.width, H = (int)r.height,
                Uv = new Rect(r.x / w, r.y / h, r.width / w, r.height / h)
            };
        }
    }
}
