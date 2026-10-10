using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Drag-time rules banner + Summon/Set (or Activate/Set) chooser.
    /// Same quiet menu slate as the hub, with EXIT.
    /// Engine is the only source of which play buttons appear.
    /// </summary>
    public class ArDragActionHud : MonoBehaviour
    {
        Text _banner;
        CanvasGroup _bannerGroup;
        /// <summary>Seconds left on a one-shot notice. Negative means the banner stays until replaced.</summary>
        float _noticeLeft = -1f;
        bool _noticeFading;
        public const float NoticeSeconds = 3.2f;
        GameObject _choice;
        GameObject _dim;
        Button _optA;
        Button _optB;
        Button _optExit;
        Text _optALabel;
        Text _optBLabel;
        Text _title;
        Action _onA;
        Action _onB;
        Action _onCancel;

        public static ArDragActionHud Create(Transform uiParent)
        {
            var host = new GameObject("ArDragActionHud", typeof(RectTransform));
            host.transform.SetParent(uiParent != null ? uiParent : FindCanvas(), false);
            var rt = host.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hud = host.AddComponent<ArDragActionHud>();
            hud.Build();
            host.transform.SetAsLastSibling();
            return hud;
        }

        static Transform FindCanvas()
        {
            var c = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            return c != null ? c.transform : null;
        }

        void Build()
        {
            var plate = new GameObject("DragBanner", typeof(RectTransform), typeof(Image));
            plate.transform.SetParent(transform, false);
            Place(plate.GetComponent<RectTransform>(), 0.24f, 0.786f, 0.76f, 0.836f);
            var bg = plate.GetComponent<Image>();
            HubChrome.QuietFill(bg, new Color(0.055f, 0.063f, 0.110f, 0.34f),
                DuelystUi.Gold);
            bg.raycastTarget = false;
            _bannerGroup = plate.AddComponent<CanvasGroup>();
            _bannerGroup.blocksRaycasts = false;
            _bannerGroup.interactable = false;
            _bannerGroup.alpha = 1f;
            _banner = MakeText(plate.transform, "Label", 15, TextAnchor.MiddleCenter);
            Place(_banner.rectTransform, 0.03f, 0.08f, 0.97f, 0.92f);
            _banner.color = new Color(0.95f, 0.92f, 0.75f, 0.95f);
            plate.SetActive(false);

            _dim = new GameObject("ChoiceDim", typeof(RectTransform), typeof(Image), typeof(Button));
            _dim.transform.SetParent(transform, false);
            Stretch(_dim.GetComponent<RectTransform>());
            var dimImg = _dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.18f);
            dimImg.raycastTarget = true;
            _dim.GetComponent<Button>().onClick.AddListener(() => _onCancel?.Invoke());
            _dim.SetActive(false);

            _choice = new GameObject("DropChoice", typeof(RectTransform), typeof(Image));
            _choice.transform.SetParent(transform, false);
            Place(_choice.GetComponent<RectTransform>(), 0.22f, 0.28f, 0.78f, 0.66f);
            var sheet = _choice.GetComponent<Image>();
            HubChrome.QuietFill(sheet, new Color(0.055f, 0.063f, 0.110f, 0.72f),
                DuelystUi.Gold);
            sheet.raycastTarget = true;

            _title = MakeText(_choice.transform, "Title", 18, TextAnchor.MiddleCenter);
            Place(_title.rectTransform, 0.06f, 0.82f, 0.84f, 0.96f);
            _title.text = "Choose a legal play";
            _title.color = WrldzTheme.GoldHot;
            WrldzType.Style(_title, 18, display: true);

            var close = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            close.transform.SetParent(_choice.transform, false);
            Place(close.GetComponent<RectTransform>(), 0.86f, 0.84f, 0.97f, 0.97f);
            var cimg = close.GetComponent<Image>();
            cimg.sprite = DuelystUi.BtnClose() ?? UiFoundation.WhiteSprite();
            cimg.color = Color.white;
            close.GetComponent<Button>().onClick.AddListener(() => _onCancel?.Invoke());
            var xt = MakeText(close.transform, "X", 16, TextAnchor.MiddleCenter);
            Stretch(xt.rectTransform);
            xt.text = "✕";
            xt.color = Color.white;

            var acts = new GameObject("Actions", typeof(RectTransform), typeof(VerticalLayoutGroup));
            acts.transform.SetParent(_choice.transform, false);
            Place(acts.GetComponent<RectTransform>(), 0.06f, 0.05f, 0.94f, 0.80f);
            var v = acts.GetComponent<VerticalLayoutGroup>();
            v.spacing = 8;
            v.padding = new RectOffset(4, 4, 4, 4);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            _optA = MakeChip(acts.transform, "OptA", ImagineAssets.HudChipCyan(),
                new Color(0.10f, 0.32f, 0.44f, 0.48f), () => _onA?.Invoke());
            _optALabel = _optA.GetComponentInChildren<Text>();

            _optB = MakeChip(acts.transform, "OptB", ImagineAssets.HudChipCyan(),
                new Color(0.10f, 0.32f, 0.44f, 0.48f), () => _onB?.Invoke());
            _optBLabel = _optB.GetComponentInChildren<Text>();

            _optExit = MakeChip(acts.transform, "Exit", ImagineAssets.HudChipDanger(),
                new Color(0.42f, 0.10f, 0.14f, 0.50f), () => _onCancel?.Invoke());
            var exitLabel = _optExit.GetComponentInChildren<Text>();
            if (exitLabel != null) exitLabel.text = "EXIT";

            _choice.SetActive(false);
        }

        /// <summary>Drag guidance. Stays until the next hint or <see cref="HideHint"/>.</summary>
        public void ShowHint(string text) => Present(text, notice: false);

        /// <summary>
        /// One-shot result ("Not a legal zone"). Holds for <see cref="NoticeSeconds"/>, then fades.
        /// </summary>
        public void ShowNotice(string text) => Present(text, notice: true);

        void Present(string text, bool notice)
        {
            if (_choice != null && _choice.activeSelf) return;
            if (string.IsNullOrEmpty(text))
            {
                HideHint();
                return;
            }

            if (_banner == null) return;
            _banner.text = text;
            if (_bannerGroup != null) _bannerGroup.alpha = 1f;
            _noticeFading = false;
            _noticeLeft = notice ? NoticeSeconds : -1f;
            if (_banner.transform.parent != null)
                _banner.transform.parent.gameObject.SetActive(true);
        }

        public void HideHint()
        {
            _noticeLeft = -1f;
            _noticeFading = false;
            if (_bannerGroup != null) _bannerGroup.alpha = 1f;
            if (_banner != null && _banner.transform.parent != null)
                _banner.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>Edit-mode smoke: jump the notice clock without waiting on Update.</summary>
        public void AdvanceNotices(float seconds)
        {
            if (_noticeLeft < 0f || seconds <= 0f) return;
            _noticeLeft -= seconds;
            if (_noticeLeft > 0f) return;
            HideHint();
        }

        public bool HintVisible =>
            _banner != null && _banner.transform.parent != null &&
            _banner.transform.parent.gameObject.activeSelf;

        public void ShowChoice(string title, string a, string b, Action onA, Action onB, Action onCancel)
        {
            _onA = onA;
            _onB = onB;
            _onCancel = onCancel;
            HideHint();
            if (_choice == null) return;
            if (_title != null) _title.text = string.IsNullOrEmpty(title) ? "Choose a legal play" : title;
            if (_optALabel != null) _optALabel.text = a ?? "";
            if (_optBLabel != null) _optBLabel.text = b ?? "";
            if (_optA != null) _optA.gameObject.SetActive(!string.IsNullOrEmpty(a));
            if (_optB != null) _optB.gameObject.SetActive(!string.IsNullOrEmpty(b));
            if (_optExit != null) _optExit.gameObject.SetActive(true);
            if (_dim != null) _dim.SetActive(true);
            _choice.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void HideChoice()
        {
            _onA = _onB = _onCancel = null;
            if (_choice != null)
                _choice.SetActive(false);
            if (_dim != null)
                _dim.SetActive(false);
        }

        public bool ChoiceOpen => _choice != null && _choice.activeSelf;

        void Update()
        {
            if (ChoiceOpen)
            {
                if (WrldzInput.KeyDown(KeyCode.Escape) || WrldzInput.KeyDown(KeyCode.Backspace))
                    _onCancel?.Invoke();
                return;
            }

            if (_noticeFading)
            {
                if (_bannerGroup == null)
                {
                    HideHint();
                    return;
                }

                _bannerGroup.alpha = Mathf.MoveTowards(
                    _bannerGroup.alpha, 0f, Time.unscaledDeltaTime * 2.4f);
                if (_bannerGroup.alpha <= 0.02f)
                    HideHint();
                return;
            }

            if (_noticeLeft < 0f) return;
            _noticeLeft -= Time.unscaledDeltaTime;
            if (_noticeLeft > 0f) return;
            _noticeFading = true;
        }

        static Text MakeText(Transform parent, string name, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: false, heavyOutline: true);
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static Button MakeChip(Transform parent, string name, Sprite chip, Color fallback, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 56;
            le.minHeight = 50;
            le.flexibleWidth = 1f;
            var img = go.GetComponent<Image>();
            img.sprite = chip ?? UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = chip != null ? Color.white : fallback;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlayClick();
                click?.Invoke();
            });
            var t = MakeText(go.transform, "L", 16, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 8, 4);
            t.raycastTarget = false;
            WrldzType.StyleButtonLabel(t, 16, display: true);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 13;
            t.resizeTextMaxSize = 22;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return btn;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }
    }
}
