using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Idle holographic life for overworld menus: scan sweep, breathe, optional ring.
    /// Attach to a card or sheet after it is built.
    /// </summary>
    public class MenuHoloPulse : MonoBehaviour
    {
        public bool Scan = true;
        public bool Breathe = true;
        public float ScanPeriod = 2.4f;
        public float BreatheAmount = 0.018f;
        public float Phase;

        Image _scan;
        RectTransform _scanRt;
        Vector3 _baseScale = Vector3.one;
        float _t;

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

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (Breathe)
            {
                var b = 1f + BreatheAmount * Mathf.Sin((_t + Phase) * 2.15f);
                transform.localScale = new Vector3(_baseScale.x * b, _baseScale.y * b, 1f);
            }

            if (!Scan || _scan == null || _scanRt == null) return;
            var period = Mathf.Max(0.6f, ScanPeriod);
            var u = Mathf.Repeat((_t + Phase * 0.37f) / period, 1f);
            var y1 = Mathf.Lerp(0.92f, 0.10f, MenuMotion.OutCubic(u));
            var y0 = y1 - 0.10f;
            _scanRt.anchorMin = new Vector2(0.07f, y0);
            _scanRt.anchorMax = new Vector2(0.93f, y1);
            _scanRt.offsetMin = Vector2.zero;
            _scanRt.offsetMax = Vector2.zero;
            var peak = 1f - Mathf.Abs(u * 2f - 1f);
            _scan.color = new Color(0.55f, 0.95f, 1f, 0.08f + 0.28f * peak);
        }

        public void CaptureBaseScale()
        {
            _baseScale = transform.localScale;
        }
    }

    /// <summary>Tiny host so static builders can run <see cref="MenuMotion"/> coroutines.</summary>
    public sealed class MenuMotionDriver : MonoBehaviour { }
}
