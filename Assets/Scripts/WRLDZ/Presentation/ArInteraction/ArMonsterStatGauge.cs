using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// 4Kids DM point gauge next to a monster hologram: gold ATK over DEF
    /// on the blue-purple spiral plate (viewer overlay, not a diegetic board).
    /// </summary>
    [DefaultExecutionOrder(800)]
    public class ArMonsterStatGauge : MonoBehaviour
    {
        public const float CanvasWorldWidth = 0.55f;
        const int CanvasW = 420;
        const int CanvasH = 240;
        const float FadeIn = 0.12f;
        const float FadeOut = 0.28f;
        const float HoldAfterRoll = 1.55f;
        const float SummonHold = 2.20f;

        /// <summary>Set from the arena sync so gauges know Battle Phase.</summary>
        public static DuelPhase LivePhase { get; set; } = DuelPhase.Main1;

        ArArenaCardVisual _host;
        Transform _plate;
        CanvasGroup _cg;
        Text _atk;
        Text _def;
        int _atkShown = int.MinValue;
        int _defShown = int.MinValue;
        int _atkFrom, _defFrom, _atkTo, _defTo;
        float _rollElapsed;
        float _rollDuration;
        bool _rolling;
        int _rollFrame = -1;
        bool _visible;
        float _hideAt = -1f;

        static readonly Color Gold = new(1f, 0.84f, 0.12f, 1f);

        public static ArMonsterStatGauge Ensure(ArArenaCardVisual host)
        {
            if (host == null) return null;
            var existing = host.GetComponentInChildren<ArMonsterStatGauge>(true);
            if (existing != null)
            {
                existing._host = host;
                existing.Refresh();
                return existing;
            }

            var go = new GameObject("StatGauge");
            go.transform.SetParent(host.transform, false);
            go.layer = host.gameObject.layer;
            var g = go.AddComponent<ArMonsterStatGauge>();
            g._host = host;
            g.Build();
            g.Refresh();
            return g;
        }

        void Build()
        {
            var layer = gameObject.layer;
            var plateGo = new GameObject("Plate", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            plateGo.transform.SetParent(transform, false);
            plateGo.layer = layer;
            var rt = plateGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            var canvas = plateGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 80;
            canvas.worldCamera = ArStageView.FindCamera(transform);
            _plate = plateGo.transform;
            _cg = plateGo.AddComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(plateGo.transform, false);
            bgGo.layer = layer;
            FloatingPanel.Stretch(bgGo.GetComponent<RectTransform>());
            var raw = bgGo.GetComponent<Image>();
            var spr = ImagineAssets.StatGaugeTemplate() ?? ImagineAssets.LpCounterTemplate();
            raw.sprite = spr != null ? spr : UiFoundation.WhiteSprite();
            raw.color = Color.white;
            raw.preserveAspect = true;
            raw.raycastTarget = false;

            _atk = MakeNum(plateGo.transform, "ATK", 0.08f, 0.52f, 0.92f, 0.92f);
            _def = MakeNum(plateGo.transform, "DEF", 0.08f, 0.08f, 0.92f, 0.48f);
        }

        static Text MakeNum(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, 22, display: true, heavyOutline: true);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Gold;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 16;
            t.resizeTextMaxSize = 48;
            t.raycastTarget = false;
            t.text = "0";
            return t;
        }

        public void Refresh()
        {
            var card = _host != null ? _host.Card : null;
            var live = _host != null && _host.IsMonster && _host.FaceUp && card != null && card.Def != null;
            if (!live)
            {
                HideImmediate();
                _atkShown = _defShown = int.MinValue;
                return;
            }

            var atk = card.CurrentAtk;
            var def = card.CurrentDef;
            var first = _atkShown == int.MinValue;
            if (first)
            {
                Snap(atk, def);
                Reveal(SummonHold);
                return;
            }

            if (atk != _atkTo || def != _defTo)
            {
                StartRoll(atk, def);
                Reveal(_rollDuration + HoldAfterRoll);
            }
        }

        void Snap(int atk, int def)
        {
            _rolling = false;
            _atkShown = _atkTo = atk;
            _defShown = _defTo = def;
            Paint();
        }

        void StartRoll(int atk, int def)
        {
            _atkFrom = _atkShown;
            _defFrom = _defShown;
            _atkTo = atk;
            _defTo = def;
            var delta = Mathf.Max(Mathf.Abs(_atkTo - _atkFrom), Mathf.Abs(_defTo - _defFrom));
            _rollDuration = Mathf.Clamp(0.40f + delta / 4200f, 0.42f, 1.70f);
            _rollElapsed = 0f;
            _rollFrame = -1;
            _rolling = true;
        }

        void Reveal(float hold)
        {
            _visible = true;
            _hideAt = Time.unscaledTime + Mathf.Max(0.35f, hold);
            if (_plate != null && !_plate.gameObject.activeSelf)
                _plate.gameObject.SetActive(true);
        }

        void HideImmediate()
        {
            _visible = false;
            _hideAt = -1f;
            _rolling = false;
            if (_cg != null) _cg.alpha = 0f;
        }

        void Paint()
        {
            if (_atk != null) _atk.text = Mathf.Max(0, _atkShown).ToString();
            if (_def != null) _def.text = Mathf.Max(0, _defShown).ToString();
        }

        void Update()
        {
            if (!_rolling) return;
            _rollElapsed += Time.unscaledDeltaTime;
            var u = Mathf.Clamp01(_rollElapsed / Mathf.Max(0.01f, _rollDuration));
            var frame = Mathf.FloorToInt(_rollElapsed * 12f);
            if (frame == _rollFrame && u < 1f) return;
            _rollFrame = frame;
            var e = 1f - (1f - u) * (1f - u);
            _atkShown = Mathf.RoundToInt(Mathf.Lerp(_atkFrom, _atkTo, e));
            _defShown = Mathf.RoundToInt(Mathf.Lerp(_defFrom, _defTo, e));
            if (u >= 1f)
            {
                _atkShown = _atkTo;
                _defShown = _defTo;
                _rolling = false;
            }

            Paint();
        }

        void LateUpdate()
        {
            if (_host == null || _plate == null) return;

            var live = _host.IsMonster && _host.FaceUp && _host.Card?.Def != null;
            var battle = live && LivePhase == DuelPhase.Battle;
            if (battle)
            {
                _visible = true;
                _hideAt = -1f;
            }

            var want = live && (_rolling || battle || (_visible && (_hideAt < 0f || Time.unscaledTime < _hideAt)));
            if (!want && _visible && !battle && !_rolling)
                _visible = false;

            var targetA = want ? 1f : 0f;
            if (_cg != null)
            {
                var speed = want ? (1f / FadeIn) : (1f / FadeOut);
                _cg.alpha = Mathf.MoveTowards(_cg.alpha, targetA, Time.unscaledDeltaTime * speed);
            }

            if (_cg != null && _cg.alpha <= 0.01f && !want) return;

            var cam = ArStageView.FindCamera(transform);
            var hostPos = _host.transform.position;
            var scale = Mathf.Abs(_host.transform.lossyScale.x);
            var artHalf = CardArtFocus.MonsterArtworkScale.x * 0.5f * Mathf.Max(0.15f, scale);
            var gaugeHalf = CanvasWorldWidth * 0.5f;
            const float gap = 0.22f;

            Vector3 towardCam, right;
            if (cam != null)
            {
                towardCam = cam.transform.position - hostPos;
                towardCam.y = 0f;
                if (towardCam.sqrMagnitude < 1e-6f) towardCam = -cam.transform.forward;
                towardCam.y = 0f;
                if (towardCam.sqrMagnitude < 1e-6f) towardCam = Vector3.back;
                towardCam.Normalize();
                right = Vector3.Cross(Vector3.up, towardCam);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
                else right.Normalize();
            }
            else
            {
                towardCam = Vector3.back;
                right = Vector3.right;
            }

            // Keep both sides' gauges outside the standing art, not on the illustration.
            if (!_host.PlayerSide) right = -right;
            var chest = hostPos + Vector3.up * (CardArtFocus.MonsterArtLift * Mathf.Max(0.15f, scale));
            var pos = chest
                      + right * (artHalf + gaugeHalf + gap)
                      + towardCam * 0.28f;

            var parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
            transform.localScale = Vector3.one * (CanvasWorldWidth / CanvasW / Mathf.Max(1e-4f, parentScale));
            transform.SetPositionAndRotation(pos, ArStageView.UiFacing(pos, cam));
        }
    }
}
