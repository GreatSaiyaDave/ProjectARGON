using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// EDOPro / Master Duel construction table: Main, Extra, and Side
    /// stay on screen together as overlapping card faces.
    /// </summary>
    public static partial class DeckCollectionScreen
    {
        const float BoardCardAspect = 1.44f;
        const float TargetCardW = 112f;
        const float MaxCardW = 156f;
        const float MinCardW = 72f;
        const int MainColsCap = 10;
        const float ExtraRowMin = 176f;
        const float HeadBarH = 26f;

        static bool IsAr(State st) =>
            st.Presentation == UiPresentation.ArDiskHolo
            || st.Presentation == UiPresentation.ArWorldPanel;

        static bool IsWideLayout(RectTransform phase)
        {
            var rt = phase;
            while (rt.parent is RectTransform p) rt = p;
            var r = rt.rect;
            if (r.width < 16f)
                return Screen.width > Screen.height * 1.15f;
            return r.width > r.height * 1.15f;
        }

        static void BuildConstructionBoard(RectTransform phase, State st,
            float x0, float y0, float x1, float y1)
        {
            var board = new GameObject("ConstructionBoard", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            board.SetParent(phase, false);
            FloatingPanel.Place(board, x0, y0, x1, y1);
            var bg = board.GetComponent<Image>();
            var boardPlate = ImagineAssets.PanelMenuGlass() ?? ImagineAssets.PanelHolo();
            if (boardPlate != null)
            {
                bg.sprite = boardPlate;
                bg.type = boardPlate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                bg.color = new Color(1f, 1f, 1f, 0.92f);
            }
            else
            {
                bg.sprite = UiFoundation.WhiteSprite();
                bg.color = new Color(0.02f, 0.04f, 0.08f, 0.22f);
            }
            bg.raycastTarget = false;
            var vlg = board.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 4, 4);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            st.BoardHost = board;

            st.MainTray = MakePileTray(board, st, Section.Main, flex: true, minH: 200f);
            st.ExtraTray = MakePileTray(board, st, Section.Extra, flex: false, minH: ExtraRowMin);
            st.SideTray = MakePileTray(board, st, Section.Side, flex: false, minH: ExtraRowMin);
        }

        static RectTransform MakePileTray(RectTransform board, State st, Section section,
            bool flex, float minH)
        {
            var name = section.ToString();
            var tray = new GameObject(name + "Tray", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(VerticalLayoutGroup), typeof(LayoutElement))
                .GetComponent<RectTransform>();
            tray.SetParent(board, false);
            var img = tray.GetComponent<Image>();
            var plate = ImagineAssets.PanelHolo() ?? ImagineAssets.PanelMenuGlass();
            if (plate != null)
            {
                img.sprite = plate;
                img.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                img.color = section == Section.Extra
                    ? new Color(1f, 0.96f, 0.88f, 1f)
                    : Color.white;
            }
            else
            {
                img.sprite = UiFoundation.WhiteSprite();
                img.type = Image.Type.Simple;
                img.color = new Color(0.05f, 0.09f, 0.14f, 0.82f);
            }
            img.raycastTarget = true;
            var le = tray.GetComponent<LayoutElement>();
            le.minHeight = minH;
            le.preferredHeight = minH;
            le.flexibleHeight = flex ? 1f : 0f;
            le.flexibleWidth = 1f;
            var inner = tray.GetComponent<VerticalLayoutGroup>();
            inner.padding = new RectOffset(8, 8, 4, 6);
            inner.spacing = 2f;
            inner.childAlignment = TextAnchor.UpperLeft;
            inner.childControlWidth = true;
            inner.childControlHeight = true;
            inner.childForceExpandWidth = true;
            inner.childForceExpandHeight = false;
            var btn = tray.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                if (IsAr(st)) return;
                st.Section = section;
                FreeUiKit.PlaySelect();
                HighlightPiles(st);
            });

            var headBar = new GameObject("Head", typeof(RectTransform), typeof(LayoutElement));
            headBar.transform.SetParent(tray, false);
            headBar.GetComponent<LayoutElement>().minHeight = HeadBarH;
            headBar.GetComponent<LayoutElement>().preferredHeight = HeadBarH;
            headBar.GetComponent<LayoutElement>().flexibleHeight = 0f;

            var head = Label(headBar.transform, PileTitle(section, 0), 13, DuelystUi.GoldHot,
                TextAnchor.MiddleLeft);
            FloatingPanel.Place(head.rectTransform, 0.00f, 0.00f, 0.62f, 1f);
            head.horizontalOverflow = HorizontalWrapMode.Overflow;
            head.gameObject.name = name + "Count";

            if (section == Section.Main)
            {
                var meter = new GameObject("Meter", typeof(RectTransform), typeof(Image));
                meter.transform.SetParent(headBar.transform, false);
                FloatingPanel.Place(meter.GetComponent<RectTransform>(), 0.64f, 0.28f, 0.98f, 0.72f);
                var mBg = meter.GetComponent<Image>();
                mBg.sprite = UiFoundation.WhiteSprite();
                mBg.color = new Color(0.04f, 0.06f, 0.10f, 0.65f);
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                fill.transform.SetParent(meter.transform, false);
                var fRt = fill.GetComponent<RectTransform>();
                fRt.anchorMin = Vector2.zero;
                fRt.anchorMax = new Vector2(0.4f, 1f);
                fRt.offsetMin = new Vector2(1f, 1f);
                fRt.offsetMax = new Vector2(-1f, -1f);
                var fImg = fill.GetComponent<Image>();
                fImg.sprite = UiFoundation.WhiteSprite();
                fImg.color = DuelystUi.Cyan;
                st.MainMeter = fImg;
            }

            var well = new GameObject(name + "Well", typeof(RectTransform), typeof(RectMask2D),
                typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            well.transform.SetParent(tray, false);
            well.GetComponent<LayoutElement>().flexibleHeight = 1f;
            well.GetComponent<LayoutElement>().minHeight = flex ? 80f : (ExtraRowMin - HeadBarH - 16f);
            well.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var wImg = well.GetComponent<Image>();
            wImg.sprite = UiFoundation.WhiteSprite();
            wImg.color = new Color(1f, 1f, 1f, 0.01f);
            wImg.raycastTarget = true;

            var content = new GameObject("C", typeof(RectTransform));
            content.transform.SetParent(well.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            var sc = well.GetComponent<ScrollRect>();
            sc.viewport = well.GetComponent<RectTransform>();
            sc.content = crt;
            sc.horizontal = section != Section.Main;
            sc.vertical = section == Section.Main;
            sc.movementType = ScrollRect.MovementType.Clamped;
            sc.scrollSensitivity = GridScrollSensitivity;
            sc.inertia = true;

            var fit = content.AddComponent<DeckPileFit>();
            fit.SingleRow = section != Section.Main;
            fit.Aspect = BoardCardAspect;
            fit.Viewport = well.GetComponent<RectTransform>();

            switch (section)
            {
                case Section.Extra:
                    st.ExtraSlots = crt;
                    st.ExtraScroll = sc;
                    st.ExtraCountLab = head;
                    break;
                case Section.Side:
                    st.SideSlots = crt;
                    st.SideScroll = sc;
                    st.SideCountLab = head;
                    break;
                default:
                    st.MainSlots = crt;
                    st.MainScroll = sc;
                    st.MainCountLab = head;
                    break;
            }

            return tray;
        }

        static string PileTitle(Section section, int have)
        {
            return section switch
            {
                Section.Extra => "EXTRA  " + have + "/" + TcgRules.ExtraDeckMax,
                Section.Side => "SIDE  " + have + "/" + TcgRules.SideDeckMax,
                // Legal Main is a range (40–60); show it so an underbuilt deck reads clearly.
                _ => "MAIN  " + have + "  (" + TcgRules.MainDeckMin + "–" + TcgRules.MainDeckMax + ")"
            };
        }

        static void FillConstructionBoard(State st)
        {
            if (st?.MainSlots == null) return;
            FillPile(st, st.MainSlots, st.Main, TcgRules.MainDeckMin, Section.Main, st.MainScroll);
            FillPile(st, st.ExtraSlots, st.Extra, TcgRules.ExtraDeckMax, Section.Extra, st.ExtraScroll);
            FillPile(st, st.SideSlots, st.Side, TcgRules.SideDeckMax, Section.Side, st.SideScroll);
            WritePileHeaders(st);
            HighlightPiles(st);
        }

        static void FillPile(State st, Transform host, List<int> ids, int ghostMin,
            Section section, ScrollRect scroll)
        {
            if (host == null) return;
            FloatingPanel.DestroyChildrenNow(host);
            var count = ids?.Count ?? 0;
            var slots = section == Section.Main
                ? Mathf.Max(ghostMin, count)
                : Mathf.Max(count, TcgRules.ExtraDeckMax);

            for (var i = 0; i < slots; i++)
            {
                if (ids != null && i < count)
                {
                    var id = ids[i];
                    var go = ArtChip(st, host, id, 1, id == st.SelectedId);
                    BindChipInteract(go, st, id, fromPool: false, section, scroll);
                }
                else
                {
                    GhostFace(host);
                }
            }

            var fit = host.GetComponent<DeckPileFit>();
            fit?.Fit();
        }

        static void GhostFace(Transform host)
        {
            var go = new GameObject("Ghost", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.sprite = YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.color = new Color(1f, 1f, 1f, 0.16f);
            img.raycastTarget = false;
        }

        static void WritePileHeaders(State st)
        {
            var mainN = st.Main?.Count ?? 0;
            var extraN = st.Extra?.Count ?? 0;
            var sideN = st.Side?.Count ?? 0;
            if (st.MainCountLab != null)
            {
                st.MainCountLab.text = PileTitle(Section.Main, mainN);
                var legal = mainN >= TcgRules.MainDeckMin && mainN <= TcgRules.MainDeckMax;
                st.MainCountLab.color = legal ? DuelystUi.Cyan : DuelystUi.Danger;
            }

            if (st.ExtraCountLab != null)
            {
                st.ExtraCountLab.text = PileTitle(Section.Extra, extraN);
                st.ExtraCountLab.color = extraN > TcgRules.ExtraDeckMax
                    ? DuelystUi.Danger
                    : extraN > 0 ? DuelystUi.GoldHot : DuelystUi.TextMuted;
            }

            if (st.SideCountLab != null)
            {
                st.SideCountLab.text = PileTitle(Section.Side, sideN);
                st.SideCountLab.color = sideN > TcgRules.SideDeckMax
                    ? DuelystUi.Danger
                    : sideN > 0 ? DuelystUi.GoldHot : DuelystUi.TextMuted;
            }

            if (st.MainMeter != null)
            {
                var t = Mathf.Clamp01(mainN / (float)TcgRules.MainDeckMax);
                var rt = st.MainMeter.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = new Vector2(Mathf.Max(0.04f, t), 1f);
                rt.offsetMin = new Vector2(1f, 1f);
                rt.offsetMax = new Vector2(-1f, -1f);
                var legal = mainN >= TcgRules.MainDeckMin && mainN <= TcgRules.MainDeckMax;
                st.MainMeter.color = legal ? DuelystUi.Cyan : DuelystUi.Danger;
            }
        }

        static void HighlightPiles(State st)
        {
            GlowTray(st.MainTray, st.Section == Section.Main && !IsAr(st));
            GlowTray(st.ExtraTray, st.Section == Section.Extra && !IsAr(st));
            GlowTray(st.SideTray, st.Section == Section.Side && !IsAr(st));
        }

        static void GlowTray(RectTransform tray, bool on)
        {
            if (tray == null) return;
            var ol = tray.GetComponent<Outline>() ?? tray.gameObject.AddComponent<Outline>();
            ol.effectColor = on
                ? new Color(0.55f, 0.92f, 1f, 0.70f)
                : new Color(0.35f, 0.80f, 0.95f, 0.12f);
            ol.effectDistance = on ? new Vector2(2.2f, -2.2f) : new Vector2(1.2f, -1.2f);
            ol.useGraphicAlpha = false;
        }

        sealed class DeckPileFit : MonoBehaviour
        {
            public bool SingleRow;
            public float Aspect = 1.44f;
            public RectTransform Viewport;
            bool _fitting;

            void OnEnable() => Fit();
            void Start() => Fit();
            void OnRectTransformDimensionsChange() => Fit();

            public void Fit()
            {
                if (_fitting) return;
                var rt = transform as RectTransform;
                if (rt == null) return;
                var vp = Viewport != null ? Viewport : rt.parent as RectTransform;
                if (vp == null) return;
                var w = vp.rect.width;
                var h = vp.rect.height;
                if (w < 8f || h < 8f) return;
                _fitting = true;
                var n = rt.childCount;
                const float pad = 4f;
                if (n == 0)
                {
                    rt.sizeDelta = new Vector2(0f, 8f);
                    _fitting = false;
                    return;
                }

                if (SingleRow)
                {
                    var cardH = Mathf.Clamp(h - pad * 2f, 48f, MaxCardW * Aspect);
                    var cardW = cardH / Mathf.Max(0.8f, Aspect);
                    var gap = 4f;
                    var need = pad * 2f + n * cardW + Mathf.Max(0, n - 1) * gap;
                    float step;
                    if (need <= w || n <= 1)
                        step = cardW + gap;
                    else
                        step = (w - pad * 2f - cardW) / (n - 1);
                    var totalW = pad * 2f + cardW + step * (n - 1);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(w, totalW));
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                    for (var i = 0; i < n; i++)
                    {
                        var child = rt.GetChild(i) as RectTransform;
                        if (child == null) continue;
                        child.anchorMin = new Vector2(0f, 1f);
                        child.anchorMax = new Vector2(0f, 1f);
                        child.pivot = new Vector2(0f, 1f);
                        child.sizeDelta = new Vector2(cardW, cardH);
                        child.anchoredPosition = new Vector2(pad + i * step, -pad);
                    }
                }
                else
                {
                    var inner = Mathf.Max(8f, w - pad * 2f);
                    var cols = MainColsCap;
                    if (inner / cols < MinCardW)
                        cols = Mathf.Max(5, Mathf.FloorToInt(inner / MinCardW));
                    var cardW = Mathf.Min(MaxCardW, inner / cols);
                    var cardH = cardW * Aspect;
                    var used = cols * cardW;
                    var x0 = pad + Mathf.Max(0f, (inner - used) * 0.5f);
                    var rows = Mathf.CeilToInt(n / (float)cols);
                    var stepY = cardH + 3f;
                    var totalH = pad * 2f + rows * stepY;
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                    rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(h, totalH));
                    for (var i = 0; i < n; i++)
                    {
                        var child = rt.GetChild(i) as RectTransform;
                        if (child == null) continue;
                        var col = i % cols;
                        var row = i / cols;
                        child.anchorMin = new Vector2(0f, 1f);
                        child.anchorMax = new Vector2(0f, 1f);
                        child.pivot = new Vector2(0f, 1f);
                        child.sizeDelta = new Vector2(cardW - 2f, cardH);
                        child.anchoredPosition = new Vector2(x0 + col * cardW + 1f, -pad - row * stepY);
                    }
                }

                _fitting = false;
            }
        }
    }
}
