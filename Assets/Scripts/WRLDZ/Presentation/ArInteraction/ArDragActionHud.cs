using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Drag-time rules banner + Summon/Set (or Activate/Set) chooser.
    /// Same inspect-style glass + chips as <see cref="CardInspectPopup"/>, with EXIT.
    /// Engine is the only source of which play buttons appear.
    /// </summary>
    public class ArDragActionHud : MonoBehaviour
    {
        Text _banner;
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
            var glass = ImagineAssets.HudChipCyan() ?? ImagineAssets.HudIslandGlass();
            if (glass != null)
            {
                bg.sprite = glass;
                bg.color = Color.white;
            }
            else
                bg.color = new Color(0.04f, 0.05f, 0.08f, 0.26f);
            bg.raycastTarget = false;
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
            sheet.sprite = UiFoundation.WhiteSprite();
            sheet.color = new Color(0.04f, 0.06f, 0.10f, 0.55f);
            sheet.raycastTarget = true;
            var sheetGlass = ImagineAssets.HudCalloutGlass() ?? ImagineAssets.HudIslandGlass();
            if (sheetGlass != null)
            {
                var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(_choice.transform, false);
                Stretch(frame.GetComponent<RectTransform>());
                var fi = frame.GetComponent<Image>();
                fi.sprite = sheetGlass;
                fi.color = Color.white;
                fi.raycastTarget = false;
            }

            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(_choice.transform, false);
            Place(edge.GetComponent<RectTransform>(), 0.03f, 0.97f, 0.97f, 0.995f);
            var eimg = edge.GetComponent<Image>();
            eimg.sprite = UiFoundation.WhiteSprite();
            eimg.color = new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.75f);
            eimg.raycastTarget = false;

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

        public void ShowHint(string text)
        {
            if (_choice != null && _choice.activeSelf) return;
            if (string.IsNullOrEmpty(text))
            {
                HideHint();
                return;
            }

            if (_banner == null) return;
            _banner.text = text;
            if (_banner.transform.parent != null)
                _banner.transform.parent.gameObject.SetActive(true);
        }

        public void HideHint()
        {
            if (_banner != null && _banner.transform.parent != null)
                _banner.transform.parent.gameObject.SetActive(false);
        }

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
            if (!ChoiceOpen) return;
            if (WrldzInput.KeyDown(KeyCode.Escape) || WrldzInput.KeyDown(KeyCode.Backspace))
                _onCancel?.Invoke();
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
