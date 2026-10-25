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

        /// <summary>Append ABOUT / Credits to a built SETTINGS sheet (its "List" under the body); false when it has none.</summary>
        public static bool AddRow(RectTransform settingsRoot, Transform modalHost)
        {
            var list = Find(settingsRoot, "List");
            if (list == null) return false;
            HubChrome.ListHead(list, "ABOUT");
            HubChrome.ListRow(list, RowLabel, () =>
            {
                FreeUiKit.PlaySelect();
                Open(modalHost);
            });
            FitList(list as RectTransform);
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
        }

        const float RowFloor = 48f;

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
            var s = Mathf.Clamp(room / rows, RowFloor / 64f, 1f);
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
}
