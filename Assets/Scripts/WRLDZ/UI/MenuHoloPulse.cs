using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Idle holographic life: scan sweep, sheen, filament pulse, press squash.
    /// </summary>
    public class MenuHoloPulse : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Scan = true;
        public bool Breathe = true;
        public bool Sheen = false;
        public bool Press = false;
        public bool PulseFilament = false;
        public float ScanPeriod = 2.4f;
        public float SheenPeriod = 2.8f;
        public float BreatheAmount = 0.018f;
        public float Phase;

        Image _scan;
        RectTransform _scanRt;
        Image _sheen;
        RectTransform _sheenRt;
        Image[] _filament;
        Vector3 _baseScale = Vector3.one;
        float _t;
        bool _held;

        public static MenuHoloPulse Attach(GameObject go, bool scan = true, bool breathe = true,
            float phase = 0f)
        {
            if (go == null) return null;
            var pulse = go.GetComponent<MenuHoloPulse>();
            if (pulse == null) pulse = go.AddComponent<MenuHoloPulse>();
            pulse.Scan = scan;
            pulse.Breathe = breathe;
            pulse.Phase = phase;
            pulse.enabled = true;
            pulse.EnsureScan();
            return pulse;
        }

        public static MenuHoloPulse AttachFeel(GameObject go, bool press = true, bool sheen = true,
            bool pulseFilament = true)
        {
            var pulse = Attach(go, scan: false, breathe: false);
            if (pulse == null) return null;
            pulse.Press = press;
            pulse.Sheen = sheen;
            pulse.PulseFilament = pulseFilament;
            pulse.EnsureSheen();
            pulse.CacheFilament();
            return pulse;
        }

        void Awake()
        {
            _baseScale = transform.localScale;
            if (_baseScale.sqrMagnitude < 0.01f) _baseScale = Vector3.one;
            EnsureScan();
        }

        void OnEnable()
        {
            _baseScale = transform.localScale.sqrMagnitude > 0.01f ? transform.localScale : Vector3.one;
            _t = Phase;
            EnsureScan();
        }

        void OnDisable()
        {
            transform.localScale = _baseScale;
            if (_scan != null) _scan.enabled = false;
            if (_sheen != null) _sheen.enabled = false;
        }

        void EnsureScan()
        {
            if (!Scan) return;
            if (_scan != null)
            {
                _scan.enabled = true;
                return;
            }

            var existing = transform.Find("HoloScan");
            Image img;
            if (existing != null)
            {
                img = existing.GetComponent<Image>();
                _scanRt = existing as RectTransform;
            }
            else
            {
                var go = new GameObject("HoloScan", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.transform.SetAsLastSibling();
                _scanRt = go.GetComponent<RectTransform>();
                img = go.GetComponent<Image>();
            }

            _scanRt.anchorMin = new Vector2(0.06f, 0.78f);
            _scanRt.anchorMax = new Vector2(0.94f, 0.90f);
            _scanRt.offsetMin = Vector2.zero;
            _scanRt.offsetMax = Vector2.zero;
            img.sprite = ImagineAssets.FxHoloScan() ?? UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            img.raycastTarget = false;
            img.color = new Color(0.55f, 0.92f, 1f, 0.0f);
            _scan = img;
        }

        void EnsureSheen()
        {
            if (!Sheen) return;
            if (_sheen != null)
            {
                _sheen.enabled = true;
                return;
            }

            var existing = transform.Find("ChromeSheen");
            if (existing != null)
            {
                _sheenRt = existing as RectTransform;
                _sheen = existing.GetComponent<Image>();
                return;
            }

            var go = new GameObject("ChromeSheen", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();
            _sheenRt = go.GetComponent<RectTransform>();
            _sheenRt.anchorMin = new Vector2(0f, 0.12f);
            _sheenRt.anchorMax = new Vector2(0.16f, 0.88f);
            _sheenRt.offsetMin = Vector2.zero;
            _sheenRt.offsetMax = Vector2.zero;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _sheen = go.GetComponent<Image>();
            _sheen.sprite = UiFoundation.WhiteSprite();
            _sheen.raycastTarget = false;
            _sheen.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// Normalized (x0,y0,x1,y1) box inside this rect that the host Image actually paints.
        /// Full rect unless the host letterboxes its sprite with preserveAspect.
        /// </summary>
        Vector4 ArtBox()
        {
            var full = new Vector4(0f, 0f, 1f, 1f);
            var host = GetComponent<Image>();
            if (host == null || !host.preserveAspect || host.sprite == null) return full;
            if (!(transform is RectTransform rt)) return full;
            var r = rt.rect;
            var s = host.sprite.rect;
            if (r.width <= 1f || r.height <= 1f || s.width <= 0f || s.height <= 0f) return full;
            var k = Mathf.Min(r.width / s.width, r.height / s.height);
            var hx = Mathf.Clamp01(s.width * k / r.width) * 0.5f;
            var hy = Mathf.Clamp01(s.height * k / r.height) * 0.5f;
            return new Vector4(0.5f - hx, 0.5f - hy, 0.5f + hx, 0.5f + hy);
        }

        void CacheFilament()
        {
            _filament = new[]
            {
                FindImg("FilamentT"), FindImg("FilamentB"), FindImg("FilamentL"), FindImg("FilamentR")
            };
        }

        Image FindImg(string name)
        {
            var t = transform.Find(name);
            return t != null ? t.GetComponent<Image>() : null;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (Breathe && !_held)
            {
                var b = 1f + BreatheAmount * Mathf.Sin((_t + Phase) * 2.15f);
                transform.localScale = new Vector3(_baseScale.x * b, _baseScale.y * b, 1f);
            }

            if (Sheen && _sheen != null && _sheenRt != null)
            {
                // Sweep across the art, not across the rect. On a preserveAspect host the painted
                // sprite is letterboxed inside its rect, so a full-rect sweep spends most of its
                // travel over transparent margin — where it reads as a hard grey bar floating
                // beside the icon rather than as light moving over it.
                var box = ArtBox();
                var bw = box.z - box.x;
                var bh = box.w - box.y;
                var su = Mathf.Repeat(_t / Mathf.Max(0.8f, SheenPeriod), 1f);
                var x0 = box.x + bw * Mathf.Lerp(-0.10f, 0.94f, su);
                _sheenRt.anchorMin = new Vector2(x0, box.y + bh * 0.10f);
                _sheenRt.anchorMax = new Vector2(x0 + bw * 0.14f, box.y + bh * 0.90f);
                _sheenRt.offsetMin = Vector2.zero;
                _sheenRt.offsetMax = Vector2.zero;
                var peak = 1f - Mathf.Abs(su * 2f - 1f);
                _sheen.color = new Color(0.85f, 0.96f, 1f, 0.04f + 0.10f * peak);
            }

            if (PulseFilament && _filament != null)
            {
                var pulse = 0.72f + 0.22f * (0.5f + 0.5f * Mathf.Sin(_t * 2.85f));
                for (var i = 0; i < _filament.Length; i++)
                {
                    var img = _filament[i];
                    if (img == null || !img.enabled) continue;
                    var c = img.color;
                    img.color = new Color(c.r, c.g, c.b, pulse);
                }
            }

            if (!Scan || _scan == null || _scanRt == null) return;
            var period = Mathf.Max(0.6f, ScanPeriod);
            var u = Mathf.Repeat((_t + Phase * 0.37f) / period, 1f);
            var sbox = ArtBox();
            var sbw = sbox.z - sbox.x;
            var sbh = sbox.w - sbox.y;
            var y1 = sbox.y + sbh * Mathf.Lerp(0.92f, 0.10f, MenuMotion.OutCubic(u));
            var y0 = y1 - sbh * 0.10f;
            _scanRt.anchorMin = new Vector2(sbox.x + sbw * 0.07f, y0);
            _scanRt.anchorMax = new Vector2(sbox.x + sbw * 0.93f, y1);
            _scanRt.offsetMin = Vector2.zero;
            _scanRt.offsetMax = Vector2.zero;
            var sPeak = 1f - Mathf.Abs(u * 2f - 1f);
            _scan.color = new Color(0.62f, 0.78f, 1f, 0.05f + 0.16f * sPeak);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Press) return;
            _held = true;
            transform.localScale = _baseScale * 0.96f;
        }

        public void OnPointerUp(PointerEventData eventData) => ReleasePress();

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_held) ReleasePress();
        }

        void ReleasePress()
        {
            _held = false;
            transform.localScale = _baseScale;
        }

        public void CaptureBaseScale()
        {
            _baseScale = transform.localScale;
        }
    }

    /// <summary>Tiny host so static builders can run <see cref="MenuMotion"/> coroutines.</summary>
    public sealed class MenuMotionDriver : MonoBehaviour { }

    /// <summary>
    /// Slow Ken Burns on boot Imagine stills. Cover-crop parent clips the zoom.
    /// Edit mode stays at the start pose so MenuSmoke is stable.
    /// </summary>
    public sealed class BootArtDrift : MonoBehaviour
    {
        float _fromS = 1f;
        float _toS = 1.06f;
        Vector2 _fromPos;
        Vector2 _toPos;
        float _dur = 18f;
        float _t;
        RectTransform _rt;

        public void Play(float fromScale, float toScale, Vector2 fromPos, Vector2 toPos, float duration)
        {
            _rt = transform as RectTransform;
            _fromS = fromScale;
            _toS = toScale;
            _fromPos = fromPos;
            _toPos = toPos;
            _dur = Mathf.Max(0.5f, duration);
            _t = 0f;
            enabled = true;
            Apply(0f);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _rt == null) return;
            _t += Time.unscaledDeltaTime;
            Apply(Mathf.Clamp01(_t / _dur));
        }

        void Apply(float u)
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;
            var k = MenuMotion.InOutCubic(u);
            var s = Mathf.Lerp(_fromS, _toS, k);
            _rt.localScale = new Vector3(s, s, 1f);
            _rt.anchoredPosition = Vector2.Lerp(_fromPos, _toPos, k);
        }
    }
}
