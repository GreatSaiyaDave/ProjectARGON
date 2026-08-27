using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// 4Kids spiral LP plate in the street. Not the primary readout —
    /// the hub LCD is. These pop on change (and, for the opponent, on a pulse
    /// interval) so the other duelist can read the total.
    ///
    /// Player box faces the opponent. Opponent box faces the player.
    /// </summary>
    [DefaultExecutionOrder(800)]
    public class ArFloatingLpCallout : MonoBehaviour
    {
        const int CanvasW = 512;
        const int CanvasH = 288;
        public const float WorldWidth = 0.92f;
        const float FadeIn = 0.12f;
        const float FadeOut = 0.28f;
        const float HoldAfterRoll = 1.55f;
        const float PulseHold = 2.35f;
        const float PulseInterval = 10f;

        public bool PlayerSide { get; private set; }

        AnimeLpCounter _counter;
        CanvasGroup _cg;
        int _layer;
        int _lp = int.MinValue;
        float _hideAt = -1f;
        float _nextPulse;
        bool _visible;

        public static ArFloatingLpCallout Create(Transform arenaRoot, int layer, bool playerSide)
        {
            var go = new GameObject(playerSide ? "YouLpCallout" : "OppLpCallout");
            go.transform.SetParent(arenaRoot, false);
            go.layer = layer;
            var c = go.AddComponent<ArFloatingLpCallout>();
            c.PlayerSide = playerSide;
            c._layer = layer;
            c.Build();
            c.HideImmediate();
            return c;
        }

        void Build()
        {
            var plate = new GameObject("Plate", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup));
            plate.transform.SetParent(transform, false);
            plate.layer = _layer;
            var rt = plate.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            var canvas = plate.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 90;
            canvas.worldCamera = ArStageView.FindCamera(transform);
            _cg = plate.GetComponent<CanvasGroup>();
            _cg.interactable = false;
            _cg.blocksRaycasts = false;
            _cg.alpha = 0f;

            _counter = AnimeLpCounter.CreateFill(plate.transform);
            _counter.PlaySfx = false;

            var scale = WorldWidth / CanvasW;
            transform.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// Player box: show only when LP changes.
        /// Opponent box: show on change and on a reminder pulse.
        /// </summary>
        public void Sync(int lifePoints)
        {
            lifePoints = Mathf.Max(0, lifePoints);
            var changed = _lp != int.MinValue && lifePoints != _lp;
            var first = _lp == int.MinValue;
            _lp = lifePoints;

            if (first)
            {
                _counter.Snap(lifePoints);
                _nextPulse = Time.unscaledTime + (PlayerSide ? 99f : 1.4f);
                return;
            }

            if (changed)
            {
                _counter.SetTarget(lifePoints);
                Reveal(_counter.RollRemaining + HoldAfterRoll);
                if (!PlayerSide)
                    _nextPulse = Time.unscaledTime + PulseInterval;
                return;
            }

            _counter.SetTarget(lifePoints);
        }

        void Reveal(float hold)
        {
            _visible = true;
            _hideAt = Time.unscaledTime + Mathf.Max(0.35f, hold);
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        void HideImmediate()
        {
            _visible = false;
            _hideAt = -1f;
            if (_cg != null) _cg.alpha = 0f;
        }

        void LateUpdate()
        {
            if (!PlayerSide && _lp != int.MinValue && Time.unscaledTime >= _nextPulse)
            {
                _counter?.Snap(_lp);
                Reveal(PulseHold);
                _nextPulse = Time.unscaledTime + PulseInterval;
            }

            var want = _visible && (_hideAt < 0f || Time.unscaledTime < _hideAt);
            if (_counter != null && _counter.IsRolling)
                want = true;

            if (!want && _visible && Time.unscaledTime >= _hideAt)
                _visible = false;

            var targetA = _visible ? 1f : 0f;
            if (_cg != null)
            {
                var speed = _visible ? (1f / FadeIn) : (1f / FadeOut);
                _cg.alpha = Mathf.MoveTowards(_cg.alpha, targetA, Time.unscaledDeltaTime * speed);
            }

            PlaceAndFace();
        }

        void PlaceAndFace()
        {
            // Outside the monster columns so standing art cannot cover the plate.
            var outer = ArPlaymatLayout.MonsterColumnPitch * 2.15f + 0.35f;
            var st = ArPlaymatLayout.LiveSpellTrapRowFromMid + 0.20f;
            transform.localPosition = new Vector3(
                PlayerSide ? outer : -outer,
                1.62f,
                st * (PlayerSide ? -1f : 1f));

            var cam = ArStageView.FindCamera(transform);
            transform.rotation = ArStageView.UiFacing(transform.position, cam);
        }
    }
}
