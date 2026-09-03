using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Hub LP window — dark LCD well + large outlined digits on the Battle City lip.
    /// Powers on at deploy (8888 lamp test → start LP), then ticks at ~12 fps
    /// on damage/heal — same cadence as the HUD spiral counter.
    /// </summary>
    public class ArDiskLpCounter : MonoBehaviour
    {
        const float TickHz = 12f;
        const int CanvasW = 512;
        const int CanvasH = 176;

        Text _value;
        Text _tag;
        Material _wellMat;
        Material _rimMat;

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

        static readonly Color Well = new(0.03f, 0.07f, 0.11f, 1f);
        static readonly Color Rim = new(0.28f, 0.88f, 1f, 1f);
        static readonly Color OnCyan = new(0.55f, 0.98f, 1f, 1f);
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
            try { FreeUiKit.EnsureLoaded(); } catch { /* fonts optional */ }
            c.Build();
            c.PowerOff();
            return c;
        }

        void Build()
        {
            var size = ArPlaymatLayout.DiskLpWindowSize;
            _wellMat = ArAnimePresentation.MakeSolid(Well, "LpWell");
            _rimMat = ArAnimePresentation.MakeSolid(Rim, "LpRim");
            if (_rimMat.HasProperty("_EmissionColor"))
            {
                _rimMat.EnableKeyword("_EMISSION");
                _rimMat.SetColor("_EmissionColor", Rim * 0.55f);
            }

            var rim = Quad("Rim", new Vector3(size.x * 1.12f, size.y * 1.22f, 1f),
                new Vector3(0f, 0f, -0.0007f), _rimMat);
            rim.transform.SetAsFirstSibling();

            Quad("Well", new Vector3(size.x, size.y, 1f),
                new Vector3(0f, 0f, -0.0002f), _wellMat);

            var lcd = new GameObject("Lcd", typeof(RectTransform), typeof(Canvas));
            lcd.transform.SetParent(transform, false);
            lcd.layer = _layer;
            // uGUI reads on −Z; the window's +Z faces up off the hub.
            lcd.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            lcd.transform.localPosition = new Vector3(0f, 0f, 0.0005f);
            // Inset so outlines never spill past the well (was clipping the last 0 of 8000).
            var scale = size.x * 0.94f / CanvasW;
            lcd.transform.localScale = new Vector3(scale, scale, scale);

            var rt = lcd.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);

            var canvas = lcd.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (Application.isPlaying)
                canvas.worldCamera = ArStageView.FindCamera(transform);
            canvas.overrideSorting = true;
            canvas.sortingOrder = 40;

            _tag = MakeLcdText(lcd.transform, "Tag", "", 12, TextAnchor.MiddleCenter);
            _tag.gameObject.SetActive(false);

            _value = MakeLcdText(lcd.transform, "Value", "8000", 64, TextAnchor.MiddleCenter);
            var vrt = _value.rectTransform;
            vrt.anchorMin = new Vector2(0.04f, 0.04f);
            vrt.anchorMax = new Vector2(0.96f, 0.96f);
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            _value.color = OnCyan;
        }

        static Text MakeLcdText(Transform parent, string name, string text, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var t = go.GetComponent<Text>();
            // Body face — Bangers (display) is too wide for a 4-digit LCD.
            WrldzType.Style(t, size, display: false, heavyOutline: true);
            t.alignment = align;
            t.raycastTarget = false;
            t.text = text ?? "";
            t.fontSize = Mathf.Max(size, 96);
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.alignByGeometry = true;
            var o = t.GetComponent<Outline>();
            if (o != null)
            {
                o.effectColor = new Color(0f, 0.04f, 0.08f, 0.95f);
                o.effectDistance = new Vector2(1.6f, -1.6f);
            }

            return t;
        }

        MeshRenderer Quad(string name, Vector3 scale, Vector3 pos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            go.layer = _layer;
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ArAnimePresentation.ConfigureHoloRenderer(mr);
            return mr;
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
                else _value.color = OnCyan;
            }

            PaintRaw(value);
        }

        void PaintBlank()
        {
            if (_value != null) _value.text = "";
            if (_tag != null) _tag.gameObject.SetActive(false);
        }

        void PaintRaw(int value)
        {
            value = Mathf.Clamp(value, 0, 9999);
            if (_value != null) _value.text = value.ToString();
            if (_tag != null) _tag.gameObject.SetActive(false);
        }
    }
}
