using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Public GY browser — walk the pile (newest on top) and inspect any card.
    /// GY is public information; both sides may look.
    /// </summary>
    public class GraveyardBrowser : MonoBehaviour
    {
        GameObject _root;
        Text _title;
        Text _index;
        Text _empty;
        Image _feature;
        Text _name;
        Text _typeLine;
        Transform _strip;
        Button _prev;
        Button _next;
        CardDatabase _db;
        readonly List<CardInstance> _cards = new();
        int _indexAt;
        bool _playerSide;
        string _titleOverride;
        Action<CardInstance> _onPick;

        public bool IsOpen => _root != null && _root.activeSelf;
        public bool ShowingPlayerSide => _playerSide;

        public static GraveyardBrowser Create(Transform canvasRoot)
        {
            var host = new GameObject("GraveyardBrowser", typeof(RectTransform));
            host.transform.SetParent(canvasRoot, false);
            FloatingPanel.Stretch(host.GetComponent<RectTransform>());
            var pop = host.AddComponent<GraveyardBrowser>();
            pop.Build(host.transform);
            host.SetActive(false);
            return pop;
        }

        void Build(Transform root)
        {
            _root = root.gameObject;
            FreeUiKit.EnsureLoaded();

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dim.transform.SetParent(root, false);
            FloatingPanel.Stretch(dim.GetComponent<RectTransform>());
            var dimImg = dim.GetComponent<Image>();
            dimImg.sprite = UiFoundation.WhiteSprite();
            dimImg.color = new Color(0f, 0f, 0f, 0.22f);
            dimImg.raycastTarget = true;
            dim.GetComponent<Button>().onClick.AddListener(Hide);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(root, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.14f, 0.18f);
            prt.anchorMax = new Vector2(0.86f, 0.86f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            var pimg = panel.GetComponent<Image>();
            pimg.sprite = UiFoundation.WhiteSprite();
            pimg.color = new Color(0.04f, 0.06f, 0.10f, 0.62f);
            pimg.raycastTarget = true;
            var glass = ImagineAssets.HudCalloutGlass() ?? ImagineAssets.HudIslandGlass();
            if (glass != null)
            {
                var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
                frame.transform.SetParent(panel.transform, false);
                FloatingPanel.Stretch(frame.GetComponent<RectTransform>());
                var fi = frame.GetComponent<Image>();
                fi.sprite = glass;
                fi.color = Color.white;
                fi.raycastTarget = false;
            }

            _title = MkText(panel.transform, "Title", 18, true, 0.06f, 0.90f, 0.70f, 0.98f,
                WrldzTheme.GoldHot, TextAnchor.MiddleLeft);
            _index = MkText(panel.transform, "Index", 14, false, 0.70f, 0.90f, 0.86f, 0.98f,
                DuelystUi.TextMuted, TextAnchor.MiddleRight);

            var close = MenuCommandButton.Create(panel.transform, "CLOSE", Hide,
                MenuCommandButton.Kind.Secondary);
            FloatingPanel.Place(close.GetComponent<RectTransform>(), 0.86f, 0.90f, 0.98f, 0.98f);

            var featureGo = new GameObject("Feature", typeof(RectTransform), typeof(Image), typeof(Button));
            featureGo.transform.SetParent(panel.transform, false);
            FloatingPanel.Place(featureGo.GetComponent<RectTransform>(), 0.22f, 0.38f, 0.78f, 0.88f);
            _feature = featureGo.GetComponent<Image>();
            _feature.sprite = UiFoundation.WhiteSprite();
            _feature.preserveAspect = true;
            _feature.color = Color.white;
            _feature.raycastTarget = true;
            featureGo.GetComponent<Button>().onClick.AddListener(PickCurrent);

            _name = MkText(panel.transform, "Name", 16, true, 0.08f, 0.30f, 0.92f, 0.37f,
                DuelystUi.TextCream, TextAnchor.MiddleCenter);
            _typeLine = MkText(panel.transform, "Type", 13, false, 0.08f, 0.24f, 0.92f, 0.30f,
                DuelystUi.Cyan, TextAnchor.MiddleCenter);

            _empty = MkText(panel.transform, "Empty", 16, false, 0.10f, 0.42f, 0.90f, 0.72f,
                DuelystUi.TextMuted, TextAnchor.MiddleCenter);
            _empty.text = "Graveyard is empty.";

            _prev = MenuCommandButton.Create(panel.transform, "PREV", () => Step(-1),
                MenuCommandButton.Kind.Secondary);
            FloatingPanel.Place(_prev.GetComponent<RectTransform>(), 0.04f, 0.52f, 0.20f, 0.64f);
            _next = MenuCommandButton.Create(panel.transform, "NEXT", () => Step(1),
                MenuCommandButton.Kind.Primary);
            FloatingPanel.Place(_next.GetComponent<RectTransform>(), 0.80f, 0.52f, 0.96f, 0.64f);

            var scrollGo = new GameObject("StripScroll", typeof(RectTransform), typeof(Image),
                typeof(Mask), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel.transform, false);
            FloatingPanel.Place(scrollGo.GetComponent<RectTransform>(), 0.04f, 0.04f, 0.96f, 0.22f);
            var sImg = scrollGo.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0.02f, 0.03f, 0.06f, 0.35f);
            scrollGo.GetComponent<Mask>().showMaskGraphic = true;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var content = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(scrollGo.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 0.5f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var h = content.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.padding = new RectOffset(8, 8, 6, 6);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            var fit = content.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = crt;
            _strip = content.transform;
        }

        public void Show(IReadOnlyList<CardInstance> gy, CardDatabase db, bool playerSide,
            Action<CardInstance> onPick, string titleOverride = null)
        {
            _db = db;
            _playerSide = playerSide;
            _titleOverride = titleOverride;
            _onPick = onPick;
            _cards.Clear();
            if (gy != null)
            {
                for (var i = gy.Count - 1; i >= 0; i--)
                    if (gy[i] != null)
                        _cards.Add(gy[i]);
            }

            _indexAt = 0;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Paint();
        }

        public void Refresh(IReadOnlyList<CardInstance> gy)
        {
            if (!IsOpen) return;
            CardInstance keep = _indexAt >= 0 && _indexAt < _cards.Count ? _cards[_indexAt] : null;
            _cards.Clear();
            if (gy != null)
            {
                for (var i = gy.Count - 1; i >= 0; i--)
                    if (gy[i] != null)
                        _cards.Add(gy[i]);
            }

            _indexAt = 0;
            if (keep != null)
            {
                for (var i = 0; i < _cards.Count; i++)
                    if (_cards[i] != null && _cards[i].InstanceId == keep.InstanceId)
                    {
                        _indexAt = i;
                        break;
                    }
            }

            Paint();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
            _cards.Clear();
            _titleOverride = null;
            _onPick = null;
        }

        void Step(int delta)
        {
            if (_cards.Count == 0) return;
            _indexAt = Mathf.Clamp(_indexAt + delta, 0, _cards.Count - 1);
            PaintFeature();
            PaintStripHighlight();
        }

        void PickCurrent()
        {
            if (_indexAt < 0 || _indexAt >= _cards.Count) return;
            var card = _cards[_indexAt];
            if (card == null) return;
            WrldzAudio.PlayCardTap();
            _onPick?.Invoke(card);
        }

        void Paint()
        {
            var n = _cards.Count;
            var who = string.IsNullOrEmpty(_titleOverride)
                ? (_playerSide ? "YOUR GY" : "OPP GY")
                : _titleOverride;
            _title.text = n == 0 ? who : $"{who}  ·  {n}";
            var empty = n == 0;
            if (_empty != null) _empty.gameObject.SetActive(empty);
            if (_feature != null) _feature.gameObject.SetActive(!empty);
            if (_name != null) _name.gameObject.SetActive(!empty);
            if (_typeLine != null) _typeLine.gameObject.SetActive(!empty);
            if (_prev != null) _prev.gameObject.SetActive(!empty);
            if (_next != null) _next.gameObject.SetActive(!empty);
            PaintFeature();
            PaintStrip();
        }

        void PaintFeature()
        {
            var n = _cards.Count;
            if (_index != null)
                _index.text = n == 0 ? "" : $"{_indexAt + 1} / {n}";
            if (_prev != null) _prev.interactable = _indexAt > 0;
            if (_next != null) _next.interactable = _indexAt < n - 1;
            if (n == 0 || _indexAt < 0 || _indexAt >= n) return;
            var card = _cards[_indexAt];
            ApplyFace(_feature, card);
            if (_name != null) _name.text = card.Name ?? "Card";
            var def = card.Def ?? _db?.Get(card.CardId);
            if (_typeLine != null)
            {
                if (def != null && def.IsMonster)
                    _typeLine.text = $"ATK {card.CurrentAtk} / DEF {card.CurrentDef}  ·  {def.race}";
                else
                    _typeLine.text = def != null ? (def.type ?? "Spell/Trap") : "";
            }
        }

        void PaintStrip()
        {
            for (var i = _strip.childCount - 1; i >= 0; i--)
                Destroy(_strip.GetChild(i).gameObject);
            for (var i = 0; i < _cards.Count; i++)
            {
                var idx = i;
                var card = _cards[i];
                var go = new GameObject(card.Name ?? "gy", typeof(RectTransform), typeof(Image),
                    typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(_strip, false);
                var le = go.GetComponent<LayoutElement>();
                le.preferredWidth = 72;
                le.preferredHeight = 100;
                le.minWidth = 60;
                le.minHeight = 84;
                var img = go.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = true;
                ApplyFace(img, card);
                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    _indexAt = idx;
                    PaintFeature();
                    PaintStripHighlight();
                    PickCurrent();
                });
            }

            PaintStripHighlight();
        }

        void PaintStripHighlight()
        {
            for (var i = 0; i < _strip.childCount; i++)
            {
                var img = _strip.GetChild(i).GetComponent<Image>();
                if (img == null) continue;
                img.color = i == _indexAt
                    ? Color.white
                    : new Color(0.75f, 0.78f, 0.82f, 0.85f);
            }
        }

        void ApplyFace(Image img, CardInstance card)
        {
            if (img == null || card == null) return;
            var art = _db != null ? _db.GetArt(card.CardId) : null;
            if (art != null)
            {
                img.sprite = art;
                return;
            }

            img.sprite = YgoCardFrames.FrameFor(card.Def) ?? UiFoundation.WhiteSprite();
        }

        static Text MkText(Transform parent, string name, int size, bool display,
            float x0, float y0, float x1, float y1, Color color, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            FloatingPanel.Place(go.GetComponent<RectTransform>(), x0, y0, x1, y1);
            var t = go.GetComponent<Text>();
            WrldzType.Style(t, size, display: display, heavyOutline: true);
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }
    }
}
