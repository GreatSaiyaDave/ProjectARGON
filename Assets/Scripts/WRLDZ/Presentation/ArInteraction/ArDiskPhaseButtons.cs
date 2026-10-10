using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Disk control panel — two key banks mounted in the LP window's plane on the counter housing, so
    /// they line up with the LP counter from any view and turn with it when it flips for the viewer.
    /// Top bank (toward the blade): BATTLE / MAIN 2 / END TURN — fixed keys that light when legal. While the
    /// engine holds the Standby Phase (Blast Juggler) the left key reads MAIN 1 instead: the only way on.
    /// Bottom bank (over the kit's key plate): SHUFFLE / RESTART / MAP — RESTART only where a restart
    /// is allowed. Same gunmetal bezel and dark glass as the LCD; each key has a status strip.
    /// Taps go through <see cref="ArCardDragSystem"/> (AR RawImage ray / hand aim ray) on each key's
    /// trigger box, not a GraphicRaycaster; unlit keys drop their box.
    /// </summary>
    [DefaultExecutionOrder(820)]
    public class ArDiskPhaseButtons : MonoBehaviour
    {
        /// <summary>Metres per canvas unit — the LCD's scale (560 units across its 56 mm glass).</summary>
        const float Unit = 0.0001f;

        const float PhaseW = 310f;
        const float PhaseH = 150f;
        const float UtilW = 272f;
        const float UtilH = 120f;
        const float KeyGap = 14f;
        const float BankPad = 14f;

        /// <summary>Bank centres from the LCD centre (text-up), 12 units clear of its bezel (±122).</summary>
        const float PhaseRowY = 122f + 12f + PhaseH * 0.5f + BankPad;
        const float UtilRowY = -(122f + 12f + UtilH * 0.5f + BankPad);

        const float FlashTime = 0.22f;

        static readonly ArDiskPhaseKind[] Kinds =
        {
            ArDiskPhaseKind.Battle, ArDiskPhaseKind.Main2, ArDiskPhaseKind.End,
            ArDiskPhaseKind.Shuffle, ArDiskPhaseKind.Restart, ArDiskPhaseKind.Map
        };

        static readonly string[] Titles = { "BATTLE", "MAIN 2", "END TURN", "SHUFFLE", "RESTART", "MAP" };

        static readonly Color Bezel = new(0.07f, 0.08f, 0.11f, 1f);
        static readonly Color Cap = new(0.018f, 0.035f, 0.055f, 1f);
        static readonly Color LedOff = new(0.10f, 0.12f, 0.16f, 1f);
        static readonly Color RimOff = new(0.42f, 0.48f, 0.58f, 0.30f);
        static readonly Color LabelOff = new(0.55f, 0.60f, 0.68f, 0.60f);
        static readonly Color Steel = new(0.62f, 0.80f, 0.95f, 1f);

        sealed class Key
        {
            public GameObject Go;
            public RectTransform Rect;
            public BoxCollider Box;
            public Image Glow;
            public Image Cap;
            public Image Rim;
            public Image Led;
            public Text Label;
            public Color Accent;
            public bool Lit;
            public float Flash;
        }

        Key[] _keys;
        RectTransform _utilBank;
        Transform _panel;
        Canvas _canvas;
        ArDuelDiskRig _rig;
        ArDiskLpCounter _lcd;
        ArCardDragSystem _drag;
        int _layer;
        bool _deployed;
        bool _restartShown = true;
        bool _flipped;
        bool _main1Mode;

        public Transform Root => transform;

        /// <summary>The panel canvas that holds both key banks.</summary>
        public Transform Panel => _panel;

        /// <summary>True while the disk is deployed and the panel is on it.</summary>
        public bool IsShowing => _deployed && (_rig == null || _rig.IsDeployed);

        public static ArDiskPhaseButtons Create(Transform parent, int layer)
        {
            var go = new GameObject("DiskPhaseRail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ArPlaymatLayout.DiskLpWindowLocal;
            go.transform.localRotation = Quaternion.Euler(ArPlaymatLayout.DiskLpWindowEuler);
            go.transform.localScale = Vector3.one;
            go.layer = layer;
            var rail = go.AddComponent<ArDiskPhaseButtons>();
            rail._layer = layer;
            rail._rig = parent != null ? parent.GetComponentInParent<ArDuelDiskRig>() : null;
            try { FreeUiKit.EnsureLoaded(); } catch { /* fonts optional */ }
            rail.Build();
            rail.SetDeployed(false);
            return rail;
        }

        void Build()
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Canvas));
            panel.transform.SetParent(transform, false);
            panel.layer = _layer;
            _panel = panel.transform;
            // Same frame as the LCD canvas: uGUI reads on −Z, the window's +Z faces up off the hub.
            _panel.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _panel.localPosition = new Vector3(0f, 0f, 0.0006f);
            _panel.localScale = Vector3.one * Unit;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 700f);
            _canvas = panel.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            if (Application.isPlaying)
                _canvas.worldCamera = ArStageView.FindCamera(transform);
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 41;

            var accent = DuelystUi.Cyan;
            var phaseBank = Bank("PhaseBank", 3, PhaseW, PhaseH, PhaseRowY, accent);
            _utilBank = Bank("UtilityBank", 3, UtilW, UtilH, UtilRowY, accent);

            var accents = new[] { DuelystUi.Gold, DuelystUi.Cyan, DuelystUi.Danger, Steel, Steel, Steel };
            _keys = new Key[Kinds.Length];
            for (var i = 0; i < Kinds.Length; i++)
            {
                var phase = i < 3;
                _keys[i] = BuildKey(phase ? phaseBank : _utilBank, i, accents[i],
                    phase ? PhaseW : UtilW, phase ? PhaseH : UtilH, phase ? 58 : 46);
            }

            LayoutRow(PhaseW, ArDiskPhaseKind.Battle, ArDiskPhaseKind.Main2, ArDiskPhaseKind.End);
            LayoutUtility();
        }

        RectTransform Bank(string name, int keys, float w, float h, float y, Color accent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_panel, false);
            go.layer = _layer;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = BankSize(keys, w, h);
            rt.anchoredPosition = new Vector2(0f, y);
            ArHoloSprites.Apply(go.GetComponent<Image>(), ArHoloSprites.RoundRect(), Bezel, 26f);
            ArHoloSprites.Layer(go.transform, "Rim", ArHoloSprites.RoundRectRim(),
                new Color(accent.r, accent.g, accent.b, 0.45f), 26f, 5f);
            return rt;
        }

        static Vector2 BankSize(int keys, float w, float h) =>
            new(keys * w + (keys - 1) * KeyGap + 2f * BankPad, h + 2f * BankPad);

        Key BuildKey(RectTransform bank, int i, Color accent, float w, float h, int fontSize)
        {
            var go = new GameObject(Kinds[i].ToString(), typeof(RectTransform));
            go.transform.SetParent(bank, false);
            go.layer = _layer;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);

            var hit = go.AddComponent<ArDiskPhaseHit>();
            hit.Kind = Kinds[i];
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(w, h, 60f);

            var k = new Key { Go = go, Rect = rt, Box = box, Accent = accent };
            k.Glow = ArHoloSprites.Layer(go.transform, "Glow", ArHoloSprites.Glow(), Clear(accent), 0f, 22f);
            k.Cap = ArHoloSprites.Layer(go.transform, "Cap", ArHoloSprites.RoundRect(), Cap, 16f);
            var sheen = ArHoloSprites.Layer(go.transform, "Sheen", ArHoloSprites.RoundRect(),
                new Color(1f, 1f, 1f, 0.045f), 12f);
            Place(sheen.rectTransform, 0.03f, 0.55f, 0.97f, 0.94f);
            k.Rim = ArHoloSprites.Layer(go.transform, "Rim", ArHoloSprites.RoundRectRim(), RimOff, 16f);

            // Status strip along the key's lower edge.
            k.Led = ArHoloSprites.Layer(go.transform, "Led", ArHoloSprites.Pill(), LedOff, 5f);
            Place(k.Led.rectTransform, 0.30f, 0.11f, 0.70f, 0.19f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            labelGo.layer = _layer;
            var label = labelGo.GetComponent<Text>();
            WrldzType.Style(label, 18, display: false, heavyOutline: true);
            label.text = Titles[i];
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = fontSize;
            label.resizeTextForBestFit = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            Place(label.rectTransform, 0.04f, 0.26f, 0.96f, 0.96f);
            var outline = label.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = new Color(0f, 0.02f, 0.05f, 0.9f);
                outline.effectDistance = new Vector2(1.6f, -1.6f);
            }

            k.Label = label;
            Paint(k, lit: false);
            return k;
        }

        static Color Clear(Color c) => new(c.r, c.g, c.b, 0f);

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Keys evenly spaced and centred in their bank.</summary>
        void LayoutRow(float w, params ArDiskPhaseKind[] row)
        {
            var span = (row.Length - 1) * (w + KeyGap);
            for (var slot = 0; slot < row.Length; slot++)
                _keys[(int)row[slot]].Rect.anchoredPosition = new Vector2(-span * 0.5f + slot * (w + KeyGap), 0f);
        }

        void LayoutUtility()
        {
            _keys[(int)ArDiskPhaseKind.Restart].Go.SetActive(_restartShown);
            _utilBank.sizeDelta = BankSize(_restartShown ? 3 : 2, UtilW, UtilH);
            if (_restartShown)
                LayoutRow(UtilW, ArDiskPhaseKind.Shuffle, ArDiskPhaseKind.Restart, ArDiskPhaseKind.Map);
            else
                LayoutRow(UtilW, ArDiskPhaseKind.Shuffle, ArDiskPhaseKind.Map);
        }

        void Paint(Key k, bool lit)
        {
            k.Lit = lit;
            k.Box.enabled = lit;
            var a = k.Accent;
            k.Rim.color = lit ? new Color(a.r, a.g, a.b, 0.9f) : RimOff;
            k.Led.color = lit ? a : LedOff;
            k.Label.color = lit ? DuelystUi.TextCream : LabelOff;
            k.Glow.color = lit ? new Color(a.r, a.g, a.b, 0.22f) : Clear(a);
        }

        void SetKey(ArDiskPhaseKind kind, bool lit)
        {
            var k = _keys != null ? _keys[(int)kind] : null;
            if (k == null || k.Lit == lit) return;
            Paint(k, lit);
        }

        public void SetDeployed(bool on)
        {
            _deployed = on;
            if (!on)
            {
                SetEnabled(false, false, false);
                if (_panel != null && _panel.gameObject.activeSelf)
                    _panel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Phase keys: lit (tappable) when legal, dim otherwise. The keys never move. While
        /// <paramref name="standbyPause"/> holds, the left key is MAIN 1 (lit by <paramref name="main1"/>).
        /// </summary>
        public void SetEnabled(bool battle, bool main2, bool end, bool standbyPause = false, bool main1 = false)
        {
            if (!_deployed)
                battle = main2 = end = main1 = false;
            SetMain1Mode(standbyPause);
            SetKey(ArDiskPhaseKind.Battle, standbyPause ? main1 : battle);
            SetKey(ArDiskPhaseKind.Main2, main2);
            SetKey(ArDiskPhaseKind.End, end);
        }

        /// <summary>True while the left phase key reads MAIN 1 (Standby Phase held).</summary>
        public bool Main1Mode => _main1Mode;

        void SetMain1Mode(bool on)
        {
            var k = _keys != null ? _keys[(int)ArDiskPhaseKind.Battle] : null;
            if (k == null || _main1Mode == on) return;
            _main1Mode = on;
            var hit = k.Go.GetComponent<ArDiskPhaseHit>();
            if (hit != null) hit.Kind = on ? ArDiskPhaseKind.Main1 : ArDiskPhaseKind.Battle;
            k.Label.text = on ? "MAIN 1" : Titles[(int)ArDiskPhaseKind.Battle];
            k.Accent = on ? DuelystUi.Cyan : DuelystUi.Gold;
            Paint(k, k.Lit);
        }

        /// <summary>Utility keys: SHUFFLE lights when the hand can be shuffled; RESTART exists only when allowed.</summary>
        public void SetUtility(bool canShuffle, bool restartAllowed)
        {
            if (_keys == null) return;
            if (restartAllowed != _restartShown)
            {
                _restartShown = restartAllowed;
                LayoutUtility();
            }

            SetKey(ArDiskPhaseKind.Shuffle, canShuffle);
            SetKey(ArDiskPhaseKind.Restart, restartAllowed);
            SetKey(ArDiskPhaseKind.Map, true);
        }

        void OnKeyTapped(ArDiskPhaseKind kind)
        {
            if (kind == ArDiskPhaseKind.Main1) kind = ArDiskPhaseKind.Battle; // same key
            var k = _keys != null && (int)kind < _keys.Length ? _keys[(int)kind] : null;
            if (k != null) k.Flash = 1f;
        }

        void LateUpdate()
        {
            if (_panel == null) return;
            var show = IsShowing;
            if (_panel.gameObject.activeSelf != show)
                _panel.gameObject.SetActive(show);
            if (!show) return;

            var cam = ArStageView.FindCamera(transform);
            if (_canvas != null && cam != null) _canvas.worldCamera = cam;

            if (_drag == null)
            {
                _drag = FindAnyObjectByType<ArCardDragSystem>();
                if (_drag != null) _drag.OnPhaseButtonTapped += OnKeyTapped;
            }

            // Turn with the LCD so the keys always read the same way it does.
            if (_lcd == null && _rig != null) _lcd = _rig.LpCounter;
            var flip = _lcd != null && _lcd.Flipped;
            if (flip != _flipped)
            {
                _flipped = flip;
                _panel.localRotation = Quaternion.Euler(0f, 180f, flip ? 180f : 0f);
            }

            var dt = Time.unscaledDeltaTime;
            var breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            foreach (var k in _keys)
            {
                if (k == null || !k.Go.activeInHierarchy) continue;
                var a = k.Accent;
                if (k.Lit)
                {
                    k.Led.color = Color.Lerp(a, Color.white, 0.15f * breathe);
                    k.Glow.color = new Color(a.r, a.g, a.b, 0.16f + 0.10f * breathe + 0.35f * k.Flash);
                }

                if (k.Flash <= 0f) continue;
                k.Flash = Mathf.Max(0f, k.Flash - dt / FlashTime);
                k.Cap.color = Color.Lerp(Cap, new Color(a.r * 0.55f, a.g * 0.55f, a.b * 0.55f, 1f), k.Flash);
                k.Rect.localScale = Vector3.one * (1f - 0.05f * k.Flash);
            }
        }

        void OnDestroy()
        {
            if (_drag != null) _drag.OnPhaseButtonTapped -= OnKeyTapped;
        }
    }
}
