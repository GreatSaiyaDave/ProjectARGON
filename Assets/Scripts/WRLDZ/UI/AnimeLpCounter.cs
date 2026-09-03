using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// 4Kids / DM-style life-point gauge: spiral plate + gold digits that tick
    /// at ~12 fps while damage (or heal) rolls in, with looping point-drop SFX.
    /// </summary>
    public class AnimeLpCounter : MonoBehaviour
    {
        public Text ValueText;
        public Image Plate;
        public bool PlaySfx = true;
        public bool IsRolling => _rolling;
        public float RollRemaining =>
            _rolling ? Mathf.Max(0f, _duration - _elapsed) : 0f;

        int _shown = int.MinValue;
        int _from;
        int _target;
        float _elapsed;
        float _duration;
        bool _rolling;
        int _lastFrame = -1;
        bool _tickOwned;

        static readonly Color Gold = new(1f, 0.84f, 0.12f, 1f);
        static readonly Color GoldLow = new(1f, 0.45f, 0.18f, 1f);
        static readonly Color GoldZero = new(1f, 0.22f, 0.22f, 1f);

        public static AnimeLpCounter Create(Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("AnimeLpCounter", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var plate = go.GetComponent<Image>();
            plate.sprite = ImagineAssets.LpCounterTemplate() ?? UiFoundation.WhiteSprite();
            plate.type = Image.Type.Simple;
            plate.preserveAspect = true;
            plate.color = Color.white;
            plate.raycastTarget = false;
            var fitter = go.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = plate.sprite != null
                ? plate.sprite.rect.width / Mathf.Max(1f, plate.sprite.rect.height)
                : 1.33f;

            var tGo = new GameObject("Value", typeof(RectTransform), typeof(Text));
            tGo.transform.SetParent(go.transform, false);
            var trt = tGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.08f, 0.22f);
            trt.anchorMax = new Vector2(0.92f, 0.82f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var text = tGo.GetComponent<Text>();
            WrldzType.StyleGaugeDigits(text, 118);

            text.color = Gold;
            text.text = "8000";

            var c = go.AddComponent<AnimeLpCounter>();
            c.Plate = plate;
            c.ValueText = text;
            return c;
        }

        public void Snap(int value)
        {
            ReleaseTick();
            _shown = Mathf.Max(0, value);
            _target = _shown;
            _from = _shown;
            _rolling = false;
            _elapsed = 0f;
            Paint();
        }

        public void SetTarget(int value)
        {
            value = Mathf.Max(0, value);
            if (_shown == int.MinValue)
            {
                Snap(value);
                return;
            }

            if (value == _target && (_rolling || _shown == value))
                return;

            _from = _shown == int.MinValue ? value : _shown;
            _target = value;
            if (_from == _target)
            {
                Snap(value);
                return;
            }

            var delta = Mathf.Abs(_target - _from);
            // 4Kids recreation: ~12 fps, ~0.4–1.7 s so the roll is always readable.
            _duration = Mathf.Clamp(0.40f + delta / 4200f, 0.42f, 1.70f);
            _elapsed = 0f;
            _lastFrame = -1;
            _rolling = true;
            if (PlaySfx && !_tickOwned)
            {
                WrldzAudio.BeginLpTick();
                _tickOwned = true;
            }
        }

        /// <summary>World-space spiral plate (AR callout). Parent should be a world canvas.</summary>
        public static AnimeLpCounter CreateFill(Transform parent)
        {
            return Create(parent, 0f, 0f, 1f, 1f);
        }

        void Update()
        {
            if (!_rolling) return;
            _elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
            var frame = Mathf.FloorToInt(_elapsed * 12f);
            if (frame == _lastFrame && t < 1f) return;
            _lastFrame = frame;

            // Fast start, settle on the last few frames (anime digital scramble).
            var e = 1f - (1f - t) * (1f - t);
            _shown = Mathf.RoundToInt(Mathf.Lerp(_from, _target, e));
            if (t >= 1f) _shown = _target;
            Paint();

            if (t >= 1f)
            {
                _rolling = false;
                ReleaseTick();
            }
        }

        void OnDisable() => ReleaseTick();
        void OnDestroy() => ReleaseTick();

        void ReleaseTick()
        {
            if (!_tickOwned) return;
            _tickOwned = false;
            WrldzAudio.EndLpTick();
        }

        void Paint()
        {
            if (ValueText == null) return;
            ValueText.text = _shown.ToString();
            ValueText.color = _shown <= 0 ? GoldZero : _shown < 1000 ? GoldLow : Gold;
        }
    }
}
