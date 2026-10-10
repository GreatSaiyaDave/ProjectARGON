using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Eldritch
{
    /// <summary>
    /// Pins a UI panel to a world point seen through the AR RawImage. Every LateUpdate the
    /// hub is projected with the stage camera into the viewport rect and the panel's
    /// bottom-centre is parked <see cref="LiftPx"/> above it (clamped to the safe area), with a
    /// tether Image drawn hub → panel. Wearable targets skip projection and mount a
    /// WorldSpace canvas on the disk instead.
    /// </summary>
    public sealed class ArDiskHoloAnchor : MonoBehaviour
    {
        public const string TetherSpritePath = "WRLDZ/Eldritch/fx_tether_line.png";
        public const float WorldCanvasScale = 0.0008f;
        static readonly Vector2 ReferenceSize = new(1080f, 2340f);

        public Transform Target;
        public Camera StageCamera;
        public RectTransform Viewport;
        public RectTransform Panel;
        public float LiftPx = 40f;
        /// <summary>Safe inset in host px: x=left, y=bottom, z=right, w=top (top keeps the chips clear).</summary>
        public Vector4 SafeInset = new(24f, 24f, 24f, 160f);
        /// <summary>Follow damping (per second). Edit mode always snaps.</summary>
        public float Smoothing = 16f;
        public Color TetherColor = new(0.769f, 0.294f, 1f, 0.75f);

        public bool WorldSpaceMode { get; private set; }
        public bool HasProjection { get; private set; }
        public Image Tether => _tether;

        RectTransform _host;
        RectTransform _worldCanvas;
        Image _tether;
        RectTransform _tetherRt;
        Vector2 _size;
        Vector2 _pos;
        bool _floating;
        bool _tetherHidden;

        public static ArDiskHoloAnchor Attach(RectTransform panel, Transform target, Camera stageCam,
            RectTransform viewport)
        {
            if (panel == null) return null;
            var anchor = panel.gameObject.GetComponent<ArDiskHoloAnchor>();
            if (anchor == null) anchor = panel.gameObject.AddComponent<ArDiskHoloAnchor>();
            anchor.Panel = panel;
            anchor.Target = target;
            anchor.StageCamera = stageCam;
            anchor.Viewport = viewport;
            anchor._host = panel.parent as RectTransform;
            anchor.TetherColor = new Color(EldritchPalette.AbyssalMagenta.r, EldritchPalette.AbyssalMagenta.g,
                EldritchPalette.AbyssalMagenta.b, 0.75f);

            if (ArPresentationTarget.IsWearable && target != null)
                anchor.MountWorldSpace();
            else
            {
                anchor.ConvertToFloating();
                anchor.EnsureTether();
                anchor.Project(snap: true);
            }

            return anchor;
        }

        /// <summary>
        /// Hide/show the hub tether without touching the anchor. The panel owns this component, so
        /// a hidden panel stops LateUpdate and the tether (a sibling Image) would stay drawn.
        /// </summary>
        public void SetTetherVisible(bool on)
        {
            _tetherHidden = !on;
            if (_tether != null && !on && _tether.gameObject.activeSelf)
                _tether.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Panel == null) return;
            if (WorldSpaceMode)
            {
                FaceCamera();
                return;
            }

            Project(snap: !Application.isPlaying);
        }

        Vector2 HostSize()
        {
            if (_host == null) return ReferenceSize;
            var s = _host.rect.size;
            return s.x < 1f || s.y < 1f ? ReferenceSize : s;
        }

        /// <summary>Freeze the panel's authored anchor box into a fixed size so it can float.</summary>
        void ConvertToFloating()
        {
            if (_floating || Panel == null) return;
            var hs = HostSize();
            var w = (Panel.anchorMax.x - Panel.anchorMin.x) * hs.x + (Panel.offsetMax.x - Panel.offsetMin.x);
            var h = (Panel.anchorMax.y - Panel.anchorMin.y) * hs.y + (Panel.offsetMax.y - Panel.offsetMin.y);
            if (w < 8f || h < 8f)
            {
                w = hs.x * 0.72f;
                h = hs.y * 0.48f;
            }

            _size = new Vector2(w, h);
            Panel.anchorMin = new Vector2(0.5f, 0f);
            Panel.anchorMax = new Vector2(0.5f, 0f);
            Panel.pivot = new Vector2(0.5f, 0f);
            Panel.sizeDelta = _size;
            _pos = CenterFallback(hs);
            Panel.anchoredPosition = _pos;
            _floating = true;
        }

        Vector2 CenterFallback(Vector2 hs) => new(0f, Mathf.Max(SafeInset.y, (hs.y - _size.y) * 0.5f));

        /// <summary>Hub position in host-rect px, origin = host bottom-left.</summary>
        bool TryHubInHost(out Vector2 hub)
        {
            hub = default;
            if (StageCamera == null || Target == null || _host == null) return false;
            var vp = StageCamera.WorldToViewportPoint(Target.position);
            if (vp.z <= 0f) return false;
            if (vp.x < -0.25f || vp.x > 1.25f || vp.y < -0.25f || vp.y > 1.25f) return false;

            var hs = HostSize();
            if (Viewport == null || Viewport.rect.width < 1f || Viewport.rect.height < 1f)
            {
                // Viewport not laid out yet (fresh canvas / edit mode) — assume full-bleed.
                hub = new Vector2(vp.x * hs.x, vp.y * hs.y);
                return true;
            }

            var vr = Viewport.rect;
            var local = new Vector2(vr.xMin + vp.x * vr.width, vr.yMin + vp.y * vr.height);
            var world = Viewport.TransformPoint(local);
            var hl = (Vector2)_host.InverseTransformPoint(world);
            var hr = _host.rect;
            hub = new Vector2(hl.x - hr.xMin, hl.y - hr.yMin);
            return true;
        }

        public void Project(bool snap)
        {
            if (Panel == null || WorldSpaceMode) return;
            if (!_floating) ConvertToFloating();
            var hs = HostSize();

            Vector2 target;
            HasProjection = TryHubInHost(out var hub);
            if (HasProjection)
                target = new Vector2(hub.x - hs.x * 0.5f, hub.y + LiftPx);
            else
                target = CenterFallback(hs);

            var minX = -hs.x * 0.5f + SafeInset.x + _size.x * 0.5f;
            var maxX = hs.x * 0.5f - SafeInset.z - _size.x * 0.5f;
            target.x = minX <= maxX ? Mathf.Clamp(target.x, minX, maxX) : 0f;
            var minY = SafeInset.y;
            var maxY = hs.y - SafeInset.w - _size.y;
            target.y = minY <= maxY ? Mathf.Clamp(target.y, minY, maxY) : minY;

            if (snap || Smoothing <= 0f)
                _pos = target;
            else
                _pos = Vector2.Lerp(_pos, target, 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime));
            Panel.anchoredPosition = _pos;

            UpdateTether(hs, hub);
        }

        void EnsureTether()
        {
            if (_tether != null || _host == null) return;
            var go = new GameObject("HubTether", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_host, false);
            // Behind the panel, above the stage view
            go.transform.SetSiblingIndex(Mathf.Max(0, Panel.GetSiblingIndex()));
            _tetherRt = go.GetComponent<RectTransform>();
            _tetherRt.anchorMin = new Vector2(0.5f, 0f);
            _tetherRt.anchorMax = new Vector2(0.5f, 0f);
            _tetherRt.pivot = new Vector2(0.5f, 0f);
            _tether = go.GetComponent<Image>();
            var sp = EldritchAssets.TetherLine();
            if (sp == null) sp = StreamingSprite.Load(TetherSpritePath);
            _tether.sprite = sp != null ? sp : UiFoundation.WhiteSprite();
            _tether.type = Image.Type.Simple;
            _tether.preserveAspect = false;
            _tether.color = TetherColor;
            _tether.raycastTarget = false;
            go.SetActive(false);
        }

        void UpdateTether(Vector2 hs, Vector2 hub)
        {
            if (_tether == null || _tetherRt == null) return;
            var show = !_tetherHidden && HasProjection && Panel != null && Panel.gameObject.activeInHierarchy;
            if (_tether.gameObject.activeSelf != show) _tether.gameObject.SetActive(show);
            if (!show) return;

            var from = new Vector2(hub.x - hs.x * 0.5f, hub.y);
            var to = _pos; // panel bottom-centre (pivot 0.5, 0)
            var dir = to - from;
            var len = dir.magnitude;
            if (len < 2f)
            {
                _tether.gameObject.SetActive(false);
                return;
            }

            _tetherRt.anchoredPosition = from;
            _tetherRt.sizeDelta = new Vector2(10f, len);
            var ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            _tetherRt.localRotation = Quaternion.Euler(0f, 0f, ang);
            var pulse = Application.isPlaying ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.2f)) : 1f;
            var c = TetherColor;
            c.a *= pulse;
            _tether.color = c;
        }

        /// <summary>Lenses: the RawImage is off, so hang a WorldSpace canvas on the disk itself.</summary>
        void MountWorldSpace()
        {
            if (Target == null || Panel == null) return;
            var go = new GameObject("HoloHubCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            go.transform.SetParent(Target, false);
            go.layer = Target.gameObject.layer;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = StageCamera;
            canvas.sortingOrder = 88;
            _worldCanvas = go.GetComponent<RectTransform>();
            _worldCanvas.sizeDelta = new Vector2(ReferenceSize.x, ReferenceSize.y * 0.6f);
            _worldCanvas.pivot = new Vector2(0.5f, 0f);
            _worldCanvas.localScale = Vector3.one * WorldCanvasScale;
            _worldCanvas.localPosition = new Vector3(0f, 0.06f, 0f);
            _worldCanvas.localRotation = Quaternion.identity;

            var hs = new Vector2(ReferenceSize.x, ReferenceSize.y * 0.6f);
            var w = (Panel.anchorMax.x - Panel.anchorMin.x) * ReferenceSize.x;
            var h = (Panel.anchorMax.y - Panel.anchorMin.y) * ReferenceSize.y;
            if (w < 8f || h < 8f)
            {
                w = ReferenceSize.x * 0.72f;
                h = ReferenceSize.y * 0.48f;
            }

            _size = new Vector2(Mathf.Min(w, hs.x), Mathf.Min(h, hs.y));
            Panel.SetParent(_worldCanvas, false);
            Panel.anchorMin = new Vector2(0.5f, 0f);
            Panel.anchorMax = new Vector2(0.5f, 0f);
            Panel.pivot = new Vector2(0.5f, 0f);
            Panel.sizeDelta = _size;
            Panel.anchoredPosition = Vector2.zero;
            Panel.localRotation = Quaternion.identity;
            Panel.localScale = Vector3.one;
            _host = _worldCanvas;
            _floating = true;
            WorldSpaceMode = true;
            HasProjection = true;
            FaceCamera();
        }

        void FaceCamera()
        {
            if (_worldCanvas == null || StageCamera == null) return;
            var toCanvas = _worldCanvas.position - StageCamera.transform.position;
            toCanvas.y = 0f;
            if (toCanvas.sqrMagnitude < 1e-6f) return;
            // UI canvases read correctly when their +Z points away from the viewer.
            _worldCanvas.rotation = Quaternion.LookRotation(toCanvas.normalized, Vector3.up);
        }

        void OnDestroy()
        {
            if (_tether != null) WRLDZ.Presentation.ArInteraction.ArObjectUtil.Destroy(_tether.gameObject);
            // The panel is a child of the world canvas; a deferred Destroy is safe even when the
            // canvas itself is what is cascading down to us.
            if (_worldCanvas != null && Application.isPlaying)
                Object.Destroy(_worldCanvas.gameObject);
        }
    }
}
