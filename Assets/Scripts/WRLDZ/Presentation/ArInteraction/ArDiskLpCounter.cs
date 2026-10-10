using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Hub LP window — a TM1637-style LCD in the counter housing: gunmetal bezel, owner-coloured
    /// rim (cyan YOU / magenta OPP), deep glass with a faint "8888" ghost behind the lit digits and
    /// a small LP tag. Powers on at deploy (8888 lamp test → start LP), then ticks at ~12 fps on
    /// damage/heal — same cadence as the HUD spiral counter.
    /// The digits read for whoever is looking: they turn 180° in the window when the viewer would
    /// otherwise see them upside down (the opponent's disk from across the field, a raised arm).
    /// </summary>
    public class ArDiskLpCounter : MonoBehaviour
    {
        const float TickHz = 12f;

        /// <summary>Canvas units across the digit glass (1 unit = 0.1 mm at the v37 window size).</summary>
        const int CanvasW = 560;
        const int CanvasH = 200;

        Text _value;
        Text _ghost;
        Text _tag;
        Image _rim;
        Image _glow;
        Color _accent = DuelystUi.Cyan;

        int _shown = int.MinValue;
        int _from;
        int _target;
        float _elapsed;
        float _duration;
        bool _rolling;
        bool _powered;
        bool _tickOwned;
        int _lastFrame = -1;
        Coroutine _bootCo;
        int _layer;
        Transform _lcd;
        bool _flipped;

        /// <summary>True while the digits are turned 180° for the viewer (the disk panel follows it).</summary>
        public bool Flipped => _flipped;

        static readonly Color Bezel = new(0.07f, 0.08f, 0.11f, 1f);
        static readonly Color Glass = new(0.012f, 0.035f, 0.055f, 1f);
        static readonly Color OnGold = new(1f, 0.86f, 0.28f, 1f);
        static readonly Color OnLow = new(1f, 0.42f, 0.22f, 1f);
        static readonly Color OnZero = new(1f, 0.28f, 0.28f, 1f);

        public static ArDiskLpCounter Create(Transform parent, int layer)
        {
            var go = new GameObject("HubLpCounter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ArPlaymatLayout.DiskLpWindowLocal;
            go.transform.localRotation = Quaternion.Euler(ArPlaymatLayout.DiskLpWindowEuler);
            go.transform.localScale = Vector3.one;
            go.layer = layer;
            var c = go.AddComponent<ArDiskLpCounter>();
            c._layer = layer;
            var rig = parent != null ? parent.GetComponentInParent<ArDuelDiskRig>() : null;
            if (rig != null && !rig.IsPlayerSide)
                c._accent = DuelystUi.Magenta;
            try { FreeUiKit.EnsureLoaded(); } catch { /* fonts optional */ }
            c.Build();
            c.PowerOff();
            return c;
        }

        void Build()
        {
            var size = ArPlaymatLayout.DiskLpWindowSize;
            var lcd = new GameObject("Lcd", typeof(RectTransform), typeof(Canvas));
            lcd.transform.SetParent(transform, false);
            _lcd = lcd.transform;
            lcd.layer = _layer;
            // uGUI reads on −Z; the window's +Z faces up off the hub.
            lcd.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            lcd.transform.localPosition = new Vector3(0f, 0f, 0.0005f);
            var scale = size.x / CanvasW;
            lcd.transform.localScale = new Vector3(scale, scale, scale);

            var rt = lcd.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);

            var canvas = lcd.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (Application.isPlaying)
                canvas.worldCamera = ArStageView.FindCamera(transform);
            canvas.overrideSorting = true;
            canvas.sortingOrder = 40;

            var root = lcd.transform;
            _glow = ArHoloSprites.Layer(root, "Glow", ArHoloSprites.Glow(), Fade(_accent, 0.22f), 0f, 70f);
            ArHoloSprites.Layer(root, "Bezel", ArHoloSprites.RoundRect(), Bezel, 34f, 22f);
            _rim = ArHoloSprites.Layer(root, "Rim", ArHoloSprites.RoundRectRim(), Fade(_accent, 0.9f), 26f, 9f);
            ArHoloSprites.Layer(root, "Glass", ArHoloSprites.RoundRect(), Glass, 18f);
            var sheen = ArHoloSprites.Layer(root, "Sheen", ArHoloSprites.RoundRect(),
                new Color(1f, 1f, 1f, 0.045f), 14f);
            Place(sheen.rectTransform, 0.02f, 0.56f, 0.98f, 0.95f);

            _tag = MakeLcdText(root, "Tag", "LP", 40, TextAnchor.MiddleLeft);
            Place(_tag.rectTransform, 0.035f, 0.52f, 0.20f, 0.92f);
            _tag.color = Fade(_accent, 0.75f);

            _ghost = MakeLcdText(root, "Ghost", "8888", 150, TextAnchor.MiddleRight);
            Place(_ghost.rectTransform, 0.16f, 0.02f, 0.965f, 0.98f);
            _ghost.color = Fade(_accent, 0.08f);
            var ghostOutline = _ghost.GetComponent<Outline>();
            if (ghostOutline != null) ghostOutline.enabled = false;
            foreach (var sh in _ghost.GetComponents<Shadow>())
                sh.enabled = false;

            _value = MakeLcdText(root, "Value", "8000", 150, TextAnchor.MiddleRight);
            Place(_value.rectTransform, 0.16f, 0.02f, 0.965f, 0.98f);
            _value.color = Healthy;
        }

        Color Healthy => Color.Lerp(_accent, Color.white, 0.28f);

        static Color Fade(Color c, float a) => new(c.r, c.g, c.b, a);

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Text MakeLcdText(Transform parent, string name, string text, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var t = go.GetComponent<Text>();
            // Body face — Bangers (display) is too wide for a 4-digit LCD.
            WrldzType.Style(t, 18, display: false, heavyOutline: true);
            t.alignment = align;
            t.raycastTarget = false;
            t.text = text ?? "";
            t.fontSize = size;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignByGeometry = true;
            var o = t.GetComponent<Outline>();
            if (o != null)
            {
                o.effectColor = new Color(0f, 0.03f, 0.06f, 0.9f);
                o.effectDistance = new Vector2(2.4f, -2.4f);
            }

            return t;
        }

        /// <summary>Engine LP → window. Dark when the blade is folded.</summary>
        public void Sync(int lifePoints, bool diskDeployed)
        {
            if (!diskDeployed)
            {
                PowerOff();
                return;
            }

            lifePoints = Mathf.Clamp(lifePoints, 0, 9999);
            if (!_powered)
                PowerOn(lifePoints);
            else
                SetTarget(lifePoints);
        }

        public void PowerOn(int startLp)
        {
            startLp = Mathf.Clamp(startLp, 0, 9999);
            if (_bootCo != null) StopCoroutine(_bootCo);
            _powered = true;
            gameObject.SetActive(true);
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                _shown = startLp;
                _target = startLp;
                Paint(startLp);
                return;
            }

            _bootCo = StartCoroutine(BootCo(startLp));
        }

        public void PowerOff()
        {
            if (_bootCo != null)
            {
                StopCoroutine(_bootCo);
                _bootCo = null;
            }

            ReleaseTick();
            _powered = false;
            _rolling = false;
            _shown = int.MinValue;
            _target = 0;
            PaintBlank();
        }

        public void SetTarget(int value)
        {
            value = Mathf.Clamp(value, 0, 9999);
            if (!_powered) return;
            if (_shown == int.MinValue)
            {
                _shown = value;
                _target = value;
                Paint(_shown);
                return;
            }

            if (value == _target && (_rolling || _shown == value))
                return;

            _from = _shown;
            _target = value;
            if (_from == _target)
            {
                Paint(_shown);
                return;
            }

            var delta = Mathf.Abs(_target - _from);
            _duration = Mathf.Clamp(0.40f + delta / 4200f, 0.42f, 1.70f);
            _elapsed = 0f;
            _lastFrame = -1;
            _rolling = true;
            if (!_tickOwned)
            {
                WrldzAudio.BeginLpTick();
                _tickOwned = true;
            }
        }

        System.Collections.IEnumerator BootCo(int startLp)
        {
            PaintRaw(8888);
            yield return new WaitForSecondsRealtime(0.18f);
            PaintBlank();
            yield return new WaitForSecondsRealtime(0.08f);
            _shown = 0;
            _target = 0;
            Paint(0);
            while (WrldzAudio.SecondsUntilLpMayTick() > 0.02f)
                yield return null;
            SetTarget(startLp);
            _bootCo = null;
        }

        void Update()
        {
            if (!_rolling || !_powered) return;
            _elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
            var frame = Mathf.FloorToInt(_elapsed * TickHz);
            if (frame == _lastFrame && t < 1f) return;
            _lastFrame = frame;
            var e = 1f - (1f - t) * (1f - t);
            _shown = Mathf.RoundToInt(Mathf.Lerp(_from, _target, e));
            if (t >= 1f) _shown = _target;
            Paint(_shown);
            if (t >= 1f)
            {
                _rolling = false;
                ReleaseTick();
            }
        }

        /// <summary>
        /// Keep the digits upright for the viewer. The window's text-up is transform.up and its face
        /// is transform.forward; the viewer's up is the camera's up laid onto the glass (its forward
        /// when looking square at it). Only 0° / 180° fit the landscape window, with hysteresis so a
        /// sideways glance does not flicker.
        /// </summary>
        void LateUpdate()
        {
            if (_lcd == null || !_powered) return;
            var cam = ArStageView.FindCamera(transform);
            if (cam == null) return;
            var n = transform.forward;
            if (Vector3.Dot(cam.transform.position - transform.position, n) <= 0f) return;
            var ct = cam.transform;
            var up = ct.up - n * Vector3.Dot(ct.up, n);
            if (up.sqrMagnitude < 0.01f)
                up = ct.forward - n * Vector3.Dot(ct.forward, n);
            if (up.sqrMagnitude < 1e-6f) return;
            var d = Vector3.Dot(up.normalized, transform.up);
            var flip = _flipped ? d < 0.2f : d < -0.2f;
            if (flip == _flipped) return;
            _flipped = flip;
            _lcd.localRotation = Quaternion.Euler(0f, 180f, flip ? 180f : 0f);
        }

        void OnDisable() => ReleaseTick();
        void OnDestroy() => ReleaseTick();

        void ReleaseTick()
        {
            if (!_tickOwned) return;
            _tickOwned = false;
            WrldzAudio.EndLpTick();
        }

        void Paint(int value)
        {
            if (_value != null)
            {
                if (value <= 0) _value.color = OnZero;
                else if (value < 1000) _value.color = OnLow;
                else if (value < 4000) _value.color = OnGold;
                else _value.color = Healthy;
            }

            PaintRaw(value);
        }

        /// <summary>Unpowered: dark glass, no ghost segments, rim dimmed.</summary>
        void PaintBlank()
        {
            if (_value != null) _value.text = "";
            SetLit(false);
        }

        void PaintRaw(int value)
        {
            value = Mathf.Clamp(value, 0, 9999);
            if (_value != null) _value.text = value.ToString();
            SetLit(true);
        }

        void SetLit(bool lit)
        {
            if (_ghost != null) _ghost.enabled = lit;
            if (_tag != null) _tag.enabled = lit;
            if (_rim != null) _rim.color = Fade(_accent, lit ? 0.9f : 0.28f);
            if (_glow != null) _glow.enabled = lit;
        }
    }
}
