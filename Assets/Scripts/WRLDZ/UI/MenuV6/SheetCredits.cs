using UnityEngine;
using UnityEngine.UI;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// BUILD_PLAN D9 / §6 P3: the boot's Splash and Credits left the automatic path, so SETTINGS carries the credit text as a
    /// CREDITS row that opens a small sheet (the same false door) with the boot's own words (BootFlowUI.CreditsText). The row is
    /// appended from outside SettingsScreen (its file carries a peer's uncommitted edits; decision in PROGRESS_p3code).
    /// </summary>
    public static class SheetCredits
    {
        public const string RowLabel = "Credits";
        public const string Title = "CREDITS";
        public const string Subtitle = "Project ARGON · Great SaiyaDave";

        /// <summary>
        /// Add ABOUT / Credits to a built SETTINGS sheet (its "List" under the body); false when it has none.
        /// <para>P5 (the P4 landing: the AI rows another session added pushed CREDITS below the fold): ABOUT · Credits is a
        /// footer pinned under the list, so it is on the sheet without scrolling however many rows the list grows; the list
        /// above it closes up (rows to the 56 px tap floor) and, only if it is still taller than its share, scrolls with a
        /// visible hint (decision, PROGRESS_p5code).</para>
        /// </summary>
        public static bool AddRow(RectTransform settingsRoot, Transform modalHost)
        {
            var list = Find(settingsRoot, "List") as RectTransform;
            if (list == null) return false;
            var body = list.parent as RectTransform;
            if (body == null)
            {
                HubChrome.ListHead(list, "ABOUT");
                HubChrome.ListRow(list, RowLabel, () => Open(modalHost));
                return true;
            }

            // the footer: the same list type (a VerticalLayoutGroup with the list's own spacing / padding), bottom-anchored
            var foot = new GameObject("CreditsFoot", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            foot.transform.SetParent(body, false);
            var frt = foot.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(list.anchorMin.x, list.anchorMin.y);
            frt.anchorMax = new Vector2(list.anchorMax.x, list.anchorMin.y);
            frt.pivot = new Vector2(.5f, 0f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(0f, 0f);
            var src = list.GetComponent<VerticalLayoutGroup>();
            var v = foot.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childForceExpandHeight = false;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childControlWidth = true;
            v.padding = src != null ? new RectOffset(src.padding.left, src.padding.right, 2, src.padding.bottom) : new RectOffset(4, 4, 2, 4);
            foot.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            HubChrome.ListHead(foot.transform, "ABOUT");
            HubChrome.ListRow(foot.transform, RowLabel, () =>
            {
                FreeUiKit.PlaySelect();
                Open(modalHost);
            });
            LayoutRebuilder.ForceRebuildLayoutImmediate(frt);
            var footH = LayoutUtility.GetPreferredHeight(frt);

            // the list's own viewport: the body above the footer (clipped: a scrolled list never runs under it)
            var view = new GameObject("ListView", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(body, false);
            view.transform.SetSiblingIndex(list.GetSiblingIndex());
            var vrt = view.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(0f, footH + 4f);
            vrt.offsetMax = Vector2.zero;
            list.SetParent(vrt, false);
            FitList(list);
            return true;
        }

        /// <summary>
        /// SettingsScreen's list has no ScrollRect and grows about its centre (a ContentSizeFitter on stretched anchors). When
        /// its rows (with ABOUT / Credits, and the AI rows another session is adding) are taller than the body, the list is
        /// top-anchored and scrolls inside its BodyHost (the BodyWell's RectMask2D clips it), so the first rows and the Credits
        /// row stay reachable; a list that fits is left exactly as it was built (decision, PROGRESS_p3review).
        /// </summary>
        static void FitList(RectTransform rt)
        {
            var vp = rt != null ? rt.parent as RectTransform : null;
            if (vp == null || rt.GetComponentInParent<ScrollRect>(true) != null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var need = LayoutUtility.GetPreferredHeight(rt);
            var have = vp.rect.height * (rt.anchorMax.y - rt.anchorMin.y);
            if (have <= 1f || need <= have + .5f) return;
            // P4 (the P3 landing: the CREDITS row sat below the fold): first the rows close up — the list's gaps to 4, its
            // rows (the 64 / 76 px ListRows) down to a 48 px touch floor — so every row, Credits included, is on the sheet;
            // only a list still taller than the body scrolls
            Compact(rt, need, have);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            need = LayoutUtility.GetPreferredHeight(rt);
            if (need <= have + .5f) return;
            rt.anchorMin = new Vector2(rt.anchorMin.x, rt.anchorMax.y);
            rt.pivot = new Vector2(rt.pivot.x, 1f);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, need);
            var sr = vp.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.viewport = vp;
            sr.content = rt;
            sr.scrollSensitivity = 24f;
            ScrollHint.Attach(vp, sr);
        }

        /// <summary>P5: SPEC §9.4's tap floor (56 design px; P4 closed the rows up to 48).</summary>
        const float RowFloor = 56f;

        static void Compact(RectTransform list, float need, float have)
        {
            var vlg = list.GetComponent<VerticalLayoutGroup>();
            if (vlg != null && vlg.spacing > 4f)
            {
                need -= (vlg.spacing - 4f) * Mathf.Max(0, list.childCount - 1);
                vlg.spacing = 4f;
            }

            float rows = 0f, fixedH = 0f;
            for (var i = 0; i < list.childCount; i++)
            {
                var le = list.GetChild(i).GetComponent<LayoutElement>();
                if (le == null) continue;
                if (le.minHeight >= 60f) rows += le.minHeight;
                else fixedH += Mathf.Max(le.minHeight, le.preferredHeight);
            }

            if (rows <= 0f) return;
            var room = rows - (need - have);
            var s = Mathf.Clamp(room / rows, RowFloor / 64f, 1f);   // the 64 px rows close to the floor, no further
            for (var i = 0; i < list.childCount; i++)
            {
                var le = list.GetChild(i).GetComponent<LayoutElement>();
                if (le == null || le.minHeight < 60f) continue;
                le.minHeight = Mathf.Max(RowFloor, Mathf.Round(le.minHeight * s));
                le.preferredHeight = le.minHeight;
            }

            _ = fixedH;
        }

        /// <summary>The credits sheet over the current sheet; its close returns to it.</summary>
        public static RectTransform Open(Transform modalHost)
        {
            DualMenuPresenter.Frame frame = default;
            void Close() => DualMenuPresenter.Dismiss(frame);
            frame = DualMenuPresenter.BuildFrame(modalHost, Title, Subtitle, Close);
            if (frame.BodyHost == null) return frame.Root;
            var body = FloatingPanel.Body(frame.BodyHost, BootFlowUI.CreditsText, 18);
            body.name = "CreditsText";
            body.alignment = TextAnchor.MiddleCenter;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            FloatingPanel.Stretch(body.rectTransform, 12f);
            if (SanctuaryChrome.Under(frame.BodyHost))
            {
                // the sheets' own body type (texts.quit .txt: El Messiri 500, #C9D6E2)
                var slot = SanctuaryPaint.Type(frame.BodyHost, "quit", "txt");
                if (slot != null)
                {
                    slot.NoWrap = false;
                    SanctuaryPaint.Style(body, slot, keepSize: false, align: TextAnchor.MiddleCenter);
                    body.horizontalOverflow = HorizontalWrapMode.Wrap;
                    body.verticalOverflow = VerticalWrapMode.Overflow;
                }
            }

            return frame.Root;
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = Find(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }
    }

    /// <summary>P5: a list that still scrolls says so — a soft gold chevron at the viewport's bottom edge (above the fade of
    /// the last row) while more rows sit below, gone at the end of the list. Not a tap target.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ScrollHint : MaskableGraphic
    {
        ScrollRect _sr;
        float _shown = -1f;

        public static ScrollHint Attach(RectTransform viewport, ScrollRect sr)
        {
            if (viewport == null || sr == null || viewport.parent == null) return null;
            var go = new GameObject("ScrollHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(ScrollHint));
            go.transform.SetParent(viewport.parent, false);
            go.transform.SetSiblingIndex(viewport.GetSiblingIndex() + 1);
            var h = go.GetComponent<ScrollHint>();
            h._sr = sr;
            h.raycastTarget = false;
            h.color = SanctuaryPaint.Gold;
            var rt = h.rectTransform;
            rt.anchorMin = new Vector2(viewport.anchorMin.x, viewport.anchorMin.y);
            rt.anchorMax = new Vector2(viewport.anchorMax.x, viewport.anchorMin.y);
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, viewport.offsetMin.y);
            rt.sizeDelta = new Vector2(0f, 28f);
            return h;
        }

        void LateUpdate()
        {
            if (_sr == null) return;
            // more below while the content is not scrolled to its end (verticalNormalizedPosition 0 = the bottom)
            var more = _sr.content != null && _sr.viewport != null && _sr.content.rect.height > _sr.viewport.rect.height + 1f &&
                       _sr.verticalNormalizedPosition > .02f;
            var a = more ? 1f : 0f;
            if (Mathf.Approximately(a, _shown)) return;
            _shown = a;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_shown <= 0f) return;
            var r = rectTransform.rect;
            var c = color;
            var cx = r.center.x;
            var y0 = r.yMin + 6f;
            var w = 14f;
            var h = 8f;
            var t = 2.2f;
            void V(float x, float y, float a)
            {
                var v = UIVertex.simpleVert;
                v.position = new Vector3(x, y, 0f);
                var k = c;
                k.a *= a;
                v.color = k;
                vh.AddVert(v);
            }

            // a fade band (the list continues) and the chevron
            V(r.xMin, r.yMin, .0f);
            V(r.xMax, r.yMin, .0f);
            V(r.xMax, r.yMin + 2f, .35f);
            V(r.xMin, r.yMin + 2f, .35f);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
            var b = vh.currentVertCount;
            V(cx - w, y0 + h, .9f);
            V(cx - w + t, y0 + h + t * .6f, .9f);
            V(cx, y0 + t * .6f, .9f);
            V(cx, y0, .9f);
            V(cx + w - t, y0 + h + t * .6f, .9f);
            V(cx + w, y0 + h, .9f);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b + 2, b + 3, b);
            vh.AddTriangle(b + 3, b + 2, b + 4);
            vh.AddTriangle(b + 4, b + 5, b + 3);
        }
    }
}
