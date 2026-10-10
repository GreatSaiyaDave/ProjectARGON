using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Strip / ring of hieroglyph cells with a cycling "read head": the head cell plus three
    /// trailing cells light gold → sandstone, and a cell swaps to a new random glyph when the
    /// head passes it. All cells share one HieroglyphGlow material; Image.color.a carries
    /// the per-cell pulse phase. Edit mode renders a static frame.
    /// </summary>
    // ExecuteAlways so cell counts follow the laid-out rect in edit mode too (previews / baking).
    [ExecuteAlways]
    public sealed class HieroglyphBorder : MonoBehaviour
    {
        public enum Edge { Top, Bottom, Left, Right, Ring /*all four*/ }

        public float CellsPerSecond = 3f;
        public Color Glow = new(1f, 0.84f, 0.28f, 1f);
        public bool Reverse;

        public Edge Side { get; private set; }
        public float ThicknessPx { get; private set; }
        public int CellCount => _cells != null ? _cells.Length : 0;
        public Material Mat => _mat;
        public RectTransform Rect => transform as RectTransform;

        /// <summary>Authored _Glow of the HieroglyphGlow shader; SetIntensity scales this.</summary>
        const float BaseGlow = 1.2f;
        static readonly int GlowId = Shader.PropertyToID("_Glow");

        const int TrailLength = 3;
        const int MinCellsPerEdge = 8;
        const int MaxCellsPerEdge = 40;
        const float PitchMul = 1.35f;
        // Normalized span along each edge so ring corners do not stack cells.
        const float HSpan0 = 0.05f, HSpan1 = 0.95f;
        const float VSpan0 = 0.07f, VSpan1 = 0.93f;

        static int _seedCounter;

        Image[] _cells;
        Sprite[] _glyphs;
        Material _mat;
        System.Random _rng;
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        CanvasGroup _fadeGroup;
        bool _fadeSearched;
        float _fadeApplied = 1f;
        float _head;
        int _headIndex = -1;
        float _intensity = 1f;
        bool _rectDirty;
        bool _glowShader;
        int _nH = MinCellsPerEdge, _nV = MinCellsPerEdge;

        public static HieroglyphBorder Create(Transform parent, Edge edge, float thicknessPx = 34f,
            Color? glow = null, float cellsPerSecond = 3f, Sprite[] glyphs = null /* null → atlas */)
        {
            var go = new GameObject("HieroglyphBorder", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            var b = go.AddComponent<HieroglyphBorder>();
            b.Side = edge;
            b.ThicknessPx = Mathf.Max(8f, thicknessPx);
            b.Glow = glow ?? EldritchPalette.MillenniumGold;
            b.CellsPerSecond = cellsPerSecond;
            b._glyphs = glyphs != null && glyphs.Length > 0 ? glyphs : EldritchGlyphs.Atlas();
            b._rng = new System.Random(unchecked(System.Environment.TickCount * 31 + (++_seedCounter)));
            b._mat = EldritchMaterials.NewGlyph(b.Glow);
            b._glowShader = b._mat != null && b._mat.shader != null &&
                            b._mat.shader.name.Contains("HieroglyphGlow");
            b.ComputeCounts();
            b.Rebuild();
            return b;
        }

        /// <summary>Hover boost: multiplies lit / tail brightness (1 = rest).</summary>
        public void SetIntensity(float mul)
        {
            _intensity = Mathf.Clamp(mul, 0.05f, 4f);
            // Brightness above 1 cannot survive Image.color (Color32) — push it through the shader.
            if (_mat != null && _mat.HasProperty(GlowId))
                _mat.SetFloat(GlowId, BaseGlow * _intensity);
            PaintAll();
        }

        public void SetGlow(Color glow)
        {
            Glow = glow;
            if (_mat != null && _mat.HasProperty("_GlowColor")) _mat.SetColor("_GlowColor", glow);
        }

        // ------------------------------------------------------------------ layout

        void ComputeCounts()
        {
            var rt = Rect;
            var w = rt != null ? rt.rect.width : 0f;
            var h = rt != null ? rt.rect.height : 0f;
            _nH = CountFor(w * (HSpan1 - HSpan0));
            _nV = CountFor(h * (VSpan1 - VSpan0));
        }

        int CountFor(float lengthPx)
        {
            if (lengthPx <= 1f) return MinCellsPerEdge; // unlaid-out (edit mode / first frame)
            var n = Mathf.FloorToInt(lengthPx / (ThicknessPx * PitchMul));
            return Mathf.Clamp(n, MinCellsPerEdge, MaxCellsPerEdge);
        }

        int TotalCells()
        {
            switch (Side)
            {
                case Edge.Top:
                case Edge.Bottom: return _nH;
                case Edge.Left:
                case Edge.Right: return _nV;
                default: return 2 * (_nH + _nV);
            }
        }

        void Rebuild()
        {
            if (_cells != null)
            {
                for (var i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] == null) continue;
                    // Object.Destroy is deferred in play mode; hide first or the old and new
                    // strips both draw for one frame after a resize.
                    _cells[i].gameObject.SetActive(false);
                    ArObjectUtil.Destroy(_cells[i].gameObject);
                }
            }

            var total = TotalCells();
            _cells = new Image[total];
            var k = 0;
            switch (Side)
            {
                case Edge.Top:
                    for (var i = 0; i < _nH; i++) _cells[k++] = MakeCell(Edge.Top, i, _nH);
                    break;
                case Edge.Bottom:
                    for (var i = 0; i < _nH; i++) _cells[k++] = MakeCell(Edge.Bottom, i, _nH);
                    break;
                case Edge.Left:
                    for (var i = 0; i < _nV; i++) _cells[k++] = MakeCell(Edge.Left, i, _nV);
                    break;
                case Edge.Right:
                    for (var i = 0; i < _nV; i++) _cells[k++] = MakeCell(Edge.Right, i, _nV);
                    break;
                default:
                    // Clockwise from top-left: Top L→R, Right T→B, Bottom R→L, Left B→T.
                    for (var i = 0; i < _nH; i++) _cells[k++] = MakeCell(Edge.Top, i, _nH);
                    for (var i = 0; i < _nV; i++) _cells[k++] = MakeCell(Edge.Right, i, _nV);
                    for (var i = 0; i < _nH; i++) _cells[k++] = MakeCell(Edge.Bottom, _nH - 1 - i, _nH);
                    for (var i = 0; i < _nV; i++) _cells[k++] = MakeCell(Edge.Left, _nV - 1 - i, _nV);
                    break;
            }

            _head = 0f;
            _headIndex = -1;
            PaintAll();
        }

        /// <param name="edge">Which edge the cell sits on.</param>
        /// <param name="i">Index along the edge: Top/Bottom left→right, Left/Right top→bottom.</param>
        Image MakeCell(Edge edge, int i, int n)
        {
            var go = new GameObject("Glyph", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = PickGlyph(null);
            img.material = _mat;
            img.raycastTarget = false;
            img.maskable = false;
            img.preserveAspect = true;
            img.type = Image.Type.Simple;

            var rt = go.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            var cell = ThicknessPx * 0.82f;
            rt.sizeDelta = new Vector2(cell, cell);
            var u = (i + 0.5f) / n;
            var half = ThicknessPx * 0.5f;
            switch (edge)
            {
                case Edge.Top:
                    rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Lerp(HSpan0, HSpan1, u), 1f);
                    rt.anchoredPosition = new Vector2(0f, -half);
                    break;
                case Edge.Bottom:
                    rt.anchorMin = rt.anchorMax = new Vector2(Mathf.Lerp(HSpan0, HSpan1, u), 0f);
                    rt.anchoredPosition = new Vector2(0f, half);
                    break;
                case Edge.Left:
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, Mathf.Lerp(VSpan1, VSpan0, u));
                    rt.anchoredPosition = new Vector2(half, 0f);
                    break;
                default: // Right
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, Mathf.Lerp(VSpan1, VSpan0, u));
                    rt.anchoredPosition = new Vector2(-half, 0f);
                    break;
            }

            return img;
        }

        Sprite PickGlyph(Sprite avoid)
        {
            if (_glyphs == null || _glyphs.Length == 0) return UiFoundation.WhiteSprite();
            if (_glyphs.Length == 1) return _glyphs[0];
            var s = _glyphs[_rng.Next(_glyphs.Length)];
            if (s == avoid) s = _glyphs[_rng.Next(_glyphs.Length)];
            return s;
        }

        // ------------------------------------------------------------------ paint

        void PaintAll()
        {
            if (_cells == null) return;
            var n = _cells.Length;
            var headIdx = HeadIndex();
            for (var i = 0; i < n; i++) Paint(i, headIdx, n);
        }

        int HeadIndex()
        {
            var n = _cells != null ? _cells.Length : 0;
            if (n == 0) return 0;
            var h = Mathf.FloorToInt(_head) % n;
            if (h < 0) h += n;
            return h;
        }

        /// <summary>Distance behind the head (in movement direction); 0 = head.</summary>
        int Trail(int i, int headIdx, int n)
        {
            var d = Reverse ? i - headIdx : headIdx - i;
            d %= n;
            if (d < 0) d += n;
            return d;
        }

        void Paint(int i, int headIdx, int n)
        {
            var img = _cells[i];
            if (img == null) return;
            var d = Trail(i, headIdx, n);
            var frac = _head - Mathf.Floor(_head);
            if (Reverse) frac = 1f - frac;
            Color rgb;
            float bright;
            if (d <= TrailLength)
            {
                // Smooth ramp as the head crosses into the next cell.
                var lit = Mathf.Clamp01(1f - (d + frac) / (TrailLength + 1f));
                rgb = Color.Lerp(EldritchPalette.Sandstone, EldritchPalette.GoldHot, lit * lit);
                // Stay in [0,1]: UGUI packs vertex colors to Color32, so >1 clamps and the
                // hover boost would be invisible on exactly the lit cells it should brighten.
                bright = Mathf.Lerp(0.62f, 1f, lit);
            }
            else
            {
                // Resting cells must still read as carved glyphs, not texture noise.
                rgb = Color.Lerp(EldritchPalette.SandstoneDark, EldritchPalette.Sandstone, 0.55f);
                bright = 0.40f;
            }

            var c = new Color(rgb.r * bright, rgb.g * bright, rgb.b * bright, 1f);
            // Glow shader reads alpha as pulse phase; UI/Default fallback reads it as alpha.
            c.a = _glowShader ? (i + 0.37f) / n : Mathf.Clamp01(bright);
            img.color = c;
        }

        // ------------------------------------------------------------------ sim

        void Update()
        {
            if (!Application.isPlaying || _cells == null || _cells.Length == 0) return;
            var n = _cells.Length;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _head += CellsPerSecond * dt * (Reverse ? -1f : 1f);
            if (_head >= n) _head -= n;
            if (_head < 0f) _head += n;

            var headIdx = HeadIndex();
            if (headIdx != _headIndex)
            {
                // Head just arrived: swap this cell's glyph and repaint the old trail tail.
                var img = _cells[headIdx];
                if (img != null) img.sprite = PickGlyph(img.sprite);
                if (_headIndex >= 0)
                {
                    var oldTail = Reverse ? _headIndex + TrailLength + 1 : _headIndex - TrailLength - 1;
                    oldTail %= n;
                    if (oldTail < 0) oldTail += n;
                    Paint(oldTail, headIdx, n);
                }

                _headIndex = headIdx;
            }

            // Only the lit window changes between head steps.
            for (var d = 0; d <= TrailLength + 1; d++)
            {
                var i = Reverse ? headIdx + d : headIdx - d;
                i %= n;
                if (i < 0) i += n;
                Paint(i, headIdx, n);
            }
        }

        void OnRectTransformDimensionsChange()
        {
            _rectDirty = true;
        }

        void LateUpdate()
        {
            PushFade();
            if (!_rectDirty) return;
            RelayoutNow();
        }

        /// <summary>
        /// Vertex alpha carries the per-cell pulse phase, so a CanvasGroup fade cannot reach the
        /// cells the normal way — mirror the group's alpha into the shader's _Fade instead.
        /// </summary>
        void PushFade()
        {
            if (_mat == null || !_glowShader) return;
            if (!_fadeSearched)
            {
                _fadeSearched = true;
                _fadeGroup = GetComponentInParent<CanvasGroup>();
            }

            var a = _fadeGroup != null ? Mathf.Clamp01(_fadeGroup.alpha) : 1f;
            if (Mathf.Approximately(a, _fadeApplied)) return;
            _fadeApplied = a;
            if (_mat.HasProperty(FadeId)) _mat.SetFloat(FadeId, a);
        }

        /// <summary>Recount cells against the current rect and rebuild when the count changed.</summary>
        public void RelayoutNow()
        {
            _rectDirty = false;
            if (_cells == null) return;
            var oldH = _nH;
            var oldV = _nV;
            ComputeCounts();
            if (oldH != _nH || oldV != _nV) Rebuild();
        }

        void OnDestroy()
        {
            if (_mat != null)
            {
                ArObjectUtil.Destroy(_mat);
                _mat = null;
            }
        }
    }
}
