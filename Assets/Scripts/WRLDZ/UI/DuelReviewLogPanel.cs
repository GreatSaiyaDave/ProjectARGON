using System.Text;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.UI
{
    /// <summary>
    /// Always-on duel review log panel (scrollable) for human + AI post-play analysis.
    /// Toggle expands/collapses; auto-refreshes when <see cref="DuelReviewLog"/> changes.
    /// </summary>
    public class DuelReviewLogPanel : MonoBehaviour
    {
        DuelReviewLog _log;
        Text _body;
        Text _header;
        ScrollRect _scroll;
        RectTransform _panelRt;
        Button _toggleBtn;
        Button _exportBtn;
        bool _expanded = true;
        int _lastCount = -1;

        /// <summary>
        /// Build under duel root. Anchors: bottom-right floating panel.
        /// </summary>
        public static DuelReviewLogPanel Create(Transform root, DuelReviewLog log)
        {
            var go = new GameObject("DuelReviewLogPanel", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var panel = go.AddComponent<DuelReviewLogPanel>();
            panel.Build(go.GetComponent<RectTransform>());
            panel.Bind(log);
            return panel;
        }

        public void Bind(DuelReviewLog log)
        {
            if (_log != null)
                _log.OnChanged -= OnLogChanged;
            _log = log;
            if (_log != null)
                _log.OnChanged += OnLogChanged;
            RefreshText(forceScroll: true);
        }

        public void Unbind()
        {
            if (_log != null)
                _log.OnChanged -= OnLogChanged;
            _log = null;
        }

        void OnDestroy() => Unbind();

        void Build(RectTransform rt)
        {
            _panelRt = rt;
            // Expanded: right strip over field; collapsed: thin chip under OPP LP
            Place(rt, 0.78f, 0.48f, 0.985f, 0.74f);

            var bg = goAddImage(rt.gameObject, new Color(0.03f, 0.05f, 0.08f, 0.28f));
            bg.raycastTarget = true;
            var ol = rt.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(DuelystUi.Cyan.r, DuelystUi.Cyan.g, DuelystUi.Cyan.b, 0.40f);
            ol.effectDistance = new Vector2(1.1f, -1.1f);
            ol.useGraphicAlpha = false;

            // Header bar
            var headGo = new GameObject("Header", typeof(RectTransform), typeof(Image));
            headGo.transform.SetParent(rt, false);
            var headImg = headGo.GetComponent<Image>();
            headImg.color = new Color(0.06f, 0.12f, 0.22f, 0.98f);
            headImg.raycastTarget = true;
            Place(headGo.GetComponent<RectTransform>(), 0f, 0.88f, 1f, 1f);

            _header = CreateText(headGo.transform, "HeaderTxt", 12, TextAnchor.MiddleLeft, FontStyle.Bold);
            _header.color = DuelystUi.Cyan;
            Place(_header.rectTransform, 0.03f, 0.05f, 0.55f, 0.95f);
            _header.text = "DUEL REVIEW LOG (AI)";

            _toggleBtn = CreateSmallBtn(headGo.transform, "Toggle", "HIDE", 0.56f, 0.08f, 0.76f, 0.92f,
                DuelystUi.Gold, () => SetExpanded(!_expanded));
            _exportBtn = CreateSmallBtn(headGo.transform, "Export", "SAVE", 0.78f, 0.08f, 0.98f, 0.92f,
                DuelystUi.Green, ExportNow);

            // Scroll body
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                typeof(RectMask2D));
            scrollGo.transform.SetParent(rt, false);
            var scrollImg = scrollGo.GetComponent<Image>();
            scrollImg.color = new Color(0.01f, 0.02f, 0.05f, 0.55f);
            scrollImg.raycastTarget = true;
            Place(scrollGo.GetComponent<RectTransform>(), 0.02f, 0.02f, 0.98f, 0.86f);

            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;

            var content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
            content.transform.SetParent(scrollGo.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 200f);
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _body = CreateText(content.transform, "Body", 11, TextAnchor.UpperLeft, FontStyle.Normal);
            _body.color = DuelystUi.TextCream;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.alignment = TextAnchor.UpperLeft;
            _body.raycastTarget = false;
            var bodyRt = _body.rectTransform;
            bodyRt.anchorMin = new Vector2(0f, 1f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.anchoredPosition = Vector2.zero;
            bodyRt.sizeDelta = new Vector2(-8f, 0f);
            var bodyFitter = _body.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.content = contentRt;
            _scroll.viewport = scrollGo.GetComponent<RectTransform>();

            // Start collapsed so the AR stage stays clean; expand when needed
            SetExpanded(false);
        }

        void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            if (_panelRt != null)
            {
                if (_expanded)
                    Place(_panelRt, 0.78f, 0.48f, 0.985f, 0.74f);
                else
                    Place(_panelRt, 0.92f, 0.808f, 0.988f, 0.846f);
            }

            if (_scroll != null)
                _scroll.gameObject.SetActive(_expanded);
            if (_exportBtn != null)
                _exportBtn.gameObject.SetActive(_expanded);
            if (_toggleBtn != null)
            {
                var t = _toggleBtn.GetComponentInChildren<Text>();
                if (t != null) t.text = _expanded ? "HIDE" : "LOG";
            }

            if (_header != null)
                _header.text = _expanded ? "REVIEW" : "LOG";

            // Quieter panel chrome when collapsed
            var bg = _panelRt != null ? _panelRt.GetComponent<Image>() : null;
            if (bg != null)
                bg.color = _expanded
                    ? new Color(0.02f, 0.04f, 0.09f, 0.42f)
                    : new Color(0.03f, 0.06f, 0.12f, 0.22f);

            RefreshText(forceScroll: true);
        }

        void OnLogChanged() => RefreshText(forceScroll: true);

        void RefreshText(bool forceScroll)
        {
            if (_body == null) return;
            if (_log == null)
            {
                _body.text = "(no review log bound)";
                return;
            }

            if (!forceScroll && _log.Count == _lastCount) return;
            _lastCount = _log.Count;

            // Show more lines when expanded
            var n = _expanded ? 40 : 3;
            _body.text = _log.FormatVisibleBlock(n);

            if (_header != null && _expanded)
                _header.text = $"DUEL REVIEW LOG (AI) · {_log.Count} events";

            if (forceScroll && _scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0f; // bottom = newest
            }
        }

        void ExportNow()
        {
            if (_log == null) return;
            var path = _log.ExportToDisk();
            if (_header != null)
                _header.text = string.IsNullOrEmpty(path)
                    ? "EXPORT FAILED (see Console)"
                    : "SAVED · " + System.IO.Path.GetFileName(path);
        }

        static Image goAddImage(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        static Text CreateText(Transform parent, string name, int size, TextAnchor align, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                     ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = Color.white;
            return t;
        }

        static Button CreateSmallBtn(Transform parent, string name, string label,
            float x0, float y0, float x1, float y1, Color col, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var img = go.GetComponent<Image>();
            img.color = new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.95f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var t = CreateText(go.transform, "L", 11, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.text = label;
            t.color = col;
            Stretch(t.rectTransform);
            return btn;
        }

        static void Place(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
