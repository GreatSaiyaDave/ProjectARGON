using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Live-duel HUD as KaibaCorp LCD islands. TCG-client grammar:
    /// YOU LP (cyan, left) · gold phase + pips (center) · OPP LP (magenta, right).
    /// Deck / GY / Extra stay on the YOU island. Glanceable, mostly non-blocking.
    /// </summary>
    public class DuelFloatingHud : MonoBehaviour
    {
        public RectTransform Root;
        public Text YouLp;
        public Text YouName;
        public Text OppLp;
        public Text OppName;
        public Text PhaseLabel;
        public Text TurnChip;
        public Text StatusLine;
        public Text DeckCount;
        public Text GyCount;
        public System.Action OnGyClicked;
        public Text ExtraCount;
        public Image PillMp1;
        public Image PillBattle;
        public Image PillMp2;
        public Image PillEnd;
        public RectTransform ActionWindow;
        public RectTransform ActionRow;
        public RectTransform ContextWindow;
        public RectTransform ContextRow;
        public CanvasGroup StatusGroup;
        GameObject _youScoreGo;
        GameObject _phaseGo;
        GameObject _oppScoreGo;

        Color _youLpBase = DuelystUi.Cyan;
        Color _oppLpBase = DuelystUi.Magenta;
        int _lastYouLp = int.MinValue;
        int _lastOppLp = int.MinValue;
        float _youPulse;
        float _oppPulse;
        string _lastStatus;
        float _statusFreshUntil;

        public static DuelFloatingHud Create(Transform parent)
        {
            FreeUiKit.EnsureLoaded();
            var go = new GameObject("DuelFloatingHud", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            FloatingPanel.Stretch(rt);
            var hud = go.AddComponent<DuelFloatingHud>();
            hud.Root = rt;
            hud.Build();
            return hud;
        }

        public const float IslandFillAlpha = 0.16f;
        public const float PipFillAlpha = 0.28f;

        void Build()
        {
            // YOU LP orb — cyan, left. Master Duel / Duel Links always show LP.
            var youIsland = Island(Root, "YouScore", 0.012f, 0.900f, 0.278f, 0.988f,
                new Color(DuelystUi.Cyan.r, DuelystUi.Cyan.g, DuelystUi.Cyan.b, 0.50f), blockRaycasts: false);
            _youScoreGo = youIsland;
            YouName = Label(youIsland.transform, "Who", "YOU", 11, TextAnchor.MiddleLeft, DuelystUi.TextMuted);
            Place(YouName.rectTransform, 0.07f, 0.58f, 0.42f, 0.94f);
            YouLp = Label(youIsland.transform, "LP", "8000", 22, TextAnchor.MiddleRight, DuelystUi.Cyan,
                display: true);
            Place(YouLp.rectTransform, 0.38f, 0.42f, 0.95f, 0.96f);
            DeckCount = Label(youIsland.transform, "Deck", "D—", 12, TextAnchor.MiddleLeft, DuelystUi.TextCream);
            Place(DeckCount.rectTransform, 0.07f, 0.06f, 0.36f, 0.44f);
            GyCount = Label(youIsland.transform, "GY", "GY 0", 12, TextAnchor.MiddleCenter,
                new Color(0.85f, 0.72f, 0.95f, 1f));
            Place(GyCount.rectTransform, 0.36f, 0.06f, 0.66f, 0.44f);
            var gyHit = new GameObject("GyHit", typeof(RectTransform), typeof(Image), typeof(Button));
            gyHit.transform.SetParent(youIsland.transform, false);
            Place(gyHit.GetComponent<RectTransform>(), 0.36f, 0.06f, 0.66f, 0.44f);
            var gyImg = gyHit.GetComponent<Image>();
            gyImg.sprite = UiFoundation.WhiteSprite();
            gyImg.color = new Color(1f, 1f, 1f, 0.01f);
            gyImg.raycastTarget = true;
            gyHit.GetComponent<Button>().onClick.AddListener(() => OnGyClicked?.Invoke());
            ExtraCount = Label(youIsland.transform, "EX", "EX 0", 12, TextAnchor.MiddleRight, DuelystUi.Gold);
            Place(ExtraCount.rectTransform, 0.66f, 0.06f, 0.95f, 0.44f);

            // Phase island — gold banner + rulebook pips (M1 / BP / M2 / EP).
            var phase = Island(Root, "Phase", 0.330f, 0.900f, 0.670f, 0.988f,
                new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.48f), blockRaycasts: false);
            _phaseGo = phase;
            PhaseLabel = Label(phase.transform, "PhaseTxt", "MAIN PHASE 1", 17, TextAnchor.MiddleCenter,
                DuelystUi.GoldHot, display: true);
            Place(PhaseLabel.rectTransform, 0.04f, 0.46f, 0.96f, 0.94f);
            TurnChip = Label(phase.transform, "Turn", "T1", 12, TextAnchor.MiddleLeft, DuelystUi.TextMuted);
            Place(TurnChip.rectTransform, 0.06f, 0.08f, 0.22f, 0.44f);
            var pips = new GameObject("Pips", typeof(RectTransform));
            pips.transform.SetParent(phase.transform, false);
            Place(pips.GetComponent<RectTransform>(), 0.24f, 0.06f, 0.94f, 0.40f);
            PillMp1 = Pip(pips.transform, "MP1", "M1", 0.00f, 0.24f);
            PillBattle = Pip(pips.transform, "BP", "BP", 0.26f, 0.50f);
            PillMp2 = Pip(pips.transform, "MP2", "M2", 0.52f, 0.76f);
            PillEnd = Pip(pips.transform, "EP", "EP", 0.78f, 1.00f);

            // OPP LP orb — magenta, right. Always on; cinematic AR does not hide LP.
            var oppIsland = Island(Root, "OppScore", 0.722f, 0.900f, 0.988f, 0.988f,
                new Color(DuelystUi.Magenta.r, DuelystUi.Magenta.g, DuelystUi.Magenta.b, 0.50f),
                blockRaycasts: false);
            _oppScoreGo = oppIsland;
            OppName = Label(oppIsland.transform, "Who", "OPP", 11, TextAnchor.MiddleRight, DuelystUi.TextMuted);
            Place(OppName.rectTransform, 0.52f, 0.58f, 0.93f, 0.94f);
            OppLp = Label(oppIsland.transform, "LP", "8000", 22, TextAnchor.MiddleLeft, DuelystUi.Magenta,
                display: true);
            Place(OppLp.rectTransform, 0.06f, 0.12f, 0.70f, 0.78f);

            // One-sentence status — toast, not a permanent bar. Hidden until a line lands.
            var status = Island(Root, "Status", 0.34f, 0.848f, 0.66f, 0.898f,
                new Color(0.04f, 0.06f, 0.10f, 0.22f), blockRaycasts: false);
            StatusGroup = status.AddComponent<CanvasGroup>();
            StatusGroup.blocksRaycasts = false;
            StatusGroup.interactable = false;
            StatusGroup.alpha = 0f;
            StatusLine = Label(status.transform, "Line", "", 12, TextAnchor.MiddleCenter, DuelystUi.TextCream);
            Place(StatusLine.rectTransform, 0.03f, 0.08f, 0.97f, 0.92f);
            StatusLine.horizontalOverflow = HorizontalWrapMode.Wrap;
            StatusLine.verticalOverflow = VerticalWrapMode.Truncate;
            status.SetActive(false);

            // Phase CTAs — floating chips, no black well behind them.
            ActionWindow = Island(Root, "Actions", 0.28f, 0.128f, 0.72f, 0.186f,
                new Color(0f, 0f, 0f, 0f), blockRaycasts: false).GetComponent<RectTransform>();
            ClearIslandPlate(ActionWindow);
            ActionRow = Row(ActionWindow, "Row", 0.02f, 0.08f, 0.98f, 0.92f);

            // Pass / Cancel / Tributes — same band, only while a window is open.
            ContextWindow = Island(Root, "Context", 0.28f, 0.128f, 0.72f, 0.186f,
                new Color(0f, 0f, 0f, 0f), blockRaycasts: false).GetComponent<RectTransform>();
            ClearIslandPlate(ContextWindow);
            ContextRow = Row(ContextWindow, "Row", 0.02f, 0.08f, 0.98f, 0.92f);
            ContextWindow.gameObject.SetActive(false);
        }

        void Update()
        {
            _youPulse = Mathf.MoveTowards(_youPulse, 0f, Time.unscaledDeltaTime * 5.5f);
            _oppPulse = Mathf.MoveTowards(_oppPulse, 0f, Time.unscaledDeltaTime * 5.5f);
            ApplyLpPulse(YouLp, _youPulse, _youLpBase);
            ApplyLpPulse(OppLp, _oppPulse, _oppLpBase);

            if (StatusGroup == null) return;
            var target = Time.unscaledTime < _statusFreshUntil ? 1f : 0f;
            StatusGroup.alpha = Mathf.MoveTowards(StatusGroup.alpha, target, Time.unscaledDeltaTime * 2.4f);
        }

        public void SetLifePoints(int you, int opp)
        {
            if (YouLp != null) YouLp.text = you.ToString();
            if (_lastYouLp != int.MinValue && you != _lastYouLp)
                _youPulse = 1f;
            _lastYouLp = you;
            _youLpBase = you <= 0 ? DuelystUi.Danger : you < 2000 ? DuelystUi.Magenta : DuelystUi.Cyan;

            if (OppLp != null) OppLp.text = opp.ToString();
            if (_lastOppLp != int.MinValue && opp != _lastOppLp)
                _oppPulse = 1f;
            _lastOppLp = opp;
            _oppLpBase = opp <= 0 ? DuelystUi.Danger : opp < 2000 ? new Color(1f, 0.55f, 0.35f, 1f) : DuelystUi.Magenta;
        }

        public void SetNames(string you, string opp)
        {
            if (YouName != null) YouName.text = string.IsNullOrEmpty(you) ? "YOU" : you;
            if (OppName != null) OppName.text = string.IsNullOrEmpty(opp) ? "OPP" : opp;
        }

        public void SetCounts(int deck, int gy, int extra)
        {
            if (DeckCount != null) DeckCount.text = "D" + deck;
            if (GyCount != null) GyCount.text = "GY " + gy;
            if (ExtraCount != null) ExtraCount.text = "EX " + extra;
        }

        public void SetTurn(int turn)
        {
            if (TurnChip != null)
                TurnChip.text = turn > 0 ? "T" + turn : "";
        }

        public void SetStatus(string line)
        {
            if (StatusLine == null) return;
            line ??= "";
            StatusLine.text = line;
            if (line != _lastStatus)
            {
                _lastStatus = line;
                _statusFreshUntil = Time.unscaledTime + 3.2f;
                if (StatusGroup != null) StatusGroup.alpha = 1f;
            }

            var host = StatusLine.transform.parent;
            if (host != null)
                host.gameObject.SetActive(!string.IsNullOrWhiteSpace(line));
        }

        /// <summary>AR cluster owns You / Opp / Phase. Overlay keeps action chips only.</summary>
        public void SetScreenScoreVisible(bool on)
        {
            if (_youScoreGo != null) _youScoreGo.SetActive(on);
            if (_phaseGo != null) _phaseGo.SetActive(on);
            if (_oppScoreGo != null) _oppScoreGo.SetActive(on);
        }

        public void SetActionsVisible(bool on)
        {
            if (ActionWindow != null)
                ActionWindow.gameObject.SetActive(on);
        }

        public void SetContextVisible(bool on)
        {
            if (ContextWindow != null)
                ContextWindow.gameObject.SetActive(on);
            // Context and phase CTAs share the thumb band — never stack.
            if (on) SetActionsVisible(false);
        }

        static void ApplyLpPulse(Text t, float pulse, Color baseCol)
        {
            if (t == null) return;
            t.color = Color.Lerp(baseCol, Color.white, pulse * 0.55f);
        }

        static void ClearIslandPlate(RectTransform rt)
        {
            if (rt == null) return;
            var img = rt.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = false;
            }
            var ol = rt.GetComponent<Outline>();
            if (ol != null) ol.enabled = false;
        }

        static GameObject Island(Transform parent, string name, float x0, float y0, float x1, float y1,
            Color edge, bool blockRaycasts)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.raycastTarget = blockRaycasts;
            if (edge.a <= 0.01f)
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                var lcd = ImagineAssets.HudIslandGlass();
                if (lcd != null)
                {
                    img.sprite = lcd;
                    img.type = Image.Type.Simple;
                    img.color = Color.white;
                    HubChrome.HideFilament(img.transform);
                }
                else
                    HubChrome.QuietFill(img, new Color(0.03f, 0.04f, 0.09f, 0.88f), edge);
            }
            return go;
        }

        static RectTransform Row(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Place(rt, x0, y0, x1, y1);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.padding = new RectOffset(6, 6, 4, 4);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return rt;
        }

        static Image Pip(Transform parent, string name, string label, float x0, float x1)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, 0.08f, x1, 0.92f);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.06f, 0.07f, 0.12f, 0.92f);
            img.raycastTarget = false;
            HubChrome.FilamentRim(go.transform, DuelystUi.Gold, px: 1f);
            var t = Label(go.transform, "L", label, 10, TextAnchor.MiddleCenter, DuelystUi.TextCream);
            FloatingPanel.Stretch(t.rectTransform);
            return img;
        }

        static Text Label(Transform parent, string name, string text, int size, TextAnchor align,
            Color color, bool display = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: display, heavyOutline: true);
            t.text = text ?? "";
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
