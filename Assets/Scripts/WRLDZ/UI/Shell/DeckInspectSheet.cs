using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Master Duel-style inspect: full TCG face, full text, ±1, banlist pip.
    /// AR holo is glanceable (name · ATK/DEF, no edit, auto-hide).
    /// </summary>
    public static partial class DeckCollectionScreen
    {
        static void BuildInspect(Transform body, State st)
        {
            var root = new GameObject("InspectPopup", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(body, false);
            FloatingPanel.Place(root.GetComponent<RectTransform>(), 0.05f, 0.10f, 0.95f, 0.74f);
            var bg = root.GetComponent<Image>();
            var plate = ImagineAssets.PanelInspectSheet() ?? ImagineAssets.PanelModal();
            bg.sprite = plate ?? UiFoundation.WhiteSprite();
            bg.type = plate != null && plate.border.sqrMagnitude > 0.1f
                ? Image.Type.Sliced
                : Image.Type.Simple;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(root.transform, false);
            FloatingPanel.Place(art.GetComponent<RectTransform>(), 0.05f, 0.22f, 0.36f, 0.94f);
            st.InspectArt = art.GetComponent<Image>();
            st.InspectArt.preserveAspect = true;
            st.InspectArt.raycastTarget = false;

            st.InspectName = L(root.transform, "", 16, DuelystUi.GoldHot, TextAnchor.MiddleLeft);
            FloatingPanel.Place(st.InspectName.rectTransform, 0.39f, 0.84f, 0.86f, 0.96f);
            st.InspectName.horizontalOverflow = HorizontalWrapMode.Wrap;
            st.InspectName.verticalOverflow = VerticalWrapMode.Truncate;

            st.InspectStats = L(root.transform, "", 13, DuelystUi.Cyan, TextAnchor.UpperLeft);
            FloatingPanel.Place(st.InspectStats.rectTransform, 0.39f, 0.68f, 0.96f, 0.84f);
            st.InspectStats.horizontalOverflow = HorizontalWrapMode.Wrap;

            var descHost = new GameObject("DescScroll", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect), typeof(RectMask2D));
            descHost.transform.SetParent(root.transform, false);
            FloatingPanel.Place(descHost.GetComponent<RectTransform>(), 0.39f, 0.22f, 0.96f, 0.66f);
            var dBg = descHost.GetComponent<Image>();
            dBg.sprite = UiFoundation.WhiteSprite();
            dBg.color = new Color(0.03f, 0.05f, 0.08f, 0.35f);

            var descGo = new GameObject("Desc", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
            descGo.transform.SetParent(descHost.transform, false);
            var dRt = descGo.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0f, 1f);
            dRt.anchorMax = new Vector2(1f, 1f);
            dRt.pivot = new Vector2(0.5f, 1f);
            dRt.offsetMin = Vector2.zero;
            dRt.offsetMax = Vector2.zero;
            st.InspectDesc = descGo.GetComponent<Text>();
            st.InspectDesc.font = WrldzType.Body() ?? UiFoundation.BuiltinFont();
            st.InspectDesc.fontSize = WrldzType.Readable(13);
            st.InspectDesc.color = new Color(0.94f, 0.95f, 0.97f, 0.95f);
            st.InspectDesc.alignment = TextAnchor.UpperLeft;
            st.InspectDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            st.InspectDesc.verticalOverflow = VerticalWrapMode.Overflow;
            st.InspectDesc.raycastTarget = false;
            descGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var dSc = descHost.GetComponent<ScrollRect>();
            dSc.viewport = descHost.GetComponent<RectTransform>();
            dSc.content = dRt;
            dSc.horizontal = false;
            dSc.vertical = true;
            dSc.movementType = ScrollRect.MovementType.Clamped;

            st.InspectAdd = Btn(root.transform, "+1", () =>
            {
                if (IsAr(st)) return;
                if (TryAdd(st, st.SelectedId, out var msg))
                {
                    FreeUiKit.PlayConfirm();
                    Status(st, msg, true);
                    st.RefreshCards?.Invoke();
                    ShowInspect(st, st.SelectedId);
                }
                else Status(st, msg, false);
            }, gold: true, compact: true);
            FloatingPanel.Place(st.InspectAdd.GetComponent<RectTransform>(), 0.39f, 0.05f, 0.58f, 0.18f);

            st.InspectRem = Btn(root.transform, "−1", () =>
            {
                if (IsAr(st)) return;
                if (TryRem(st, st.SelectedId, out var msg))
                {
                    FreeUiKit.PlayClick();
                    Status(st, msg, true);
                    st.RefreshCards?.Invoke();
                    ShowInspect(st, st.SelectedId);
                }
                else Status(st, msg, false);
            }, compact: true);
            FloatingPanel.Place(st.InspectRem.GetComponent<RectTransform>(), 0.60f, 0.05f, 0.79f, 0.18f);

            var x = Btn(root.transform, "×", () =>
            {
                FreeUiKit.PlayClick();
                HideInspect(st);
            }, compact: true);
            FloatingPanel.Place(x.GetComponent<RectTransform>(), 0.88f, 0.84f, 0.97f, 0.96f);

            root.SetActive(false);
            st.Inspect = root;
        }

        static void HideInspect(State st)
        {
            if (st?.Inspect != null)
                st.Inspect.SetActive(false);
        }

        static void ShowInspect(State st, int id)
        {
            if (st.Inspect == null) return;
            st.SelectedId = id;
            st.Inspect.SetActive(true);
            st.Inspect.transform.SetAsLastSibling();

            var ar = IsAr(st);
            var sheet = st.Inspect.GetComponent<RectTransform>();
            if (sheet != null)
            {
                if (ar) FloatingPanel.Place(sheet, 0.08f, 0.28f, 0.92f, 0.72f);
                else FloatingPanel.Place(sheet, 0.05f, 0.10f, 0.95f, 0.74f);
            }

            var def = st.Db?.Get(id);
            if (st.InspectArt != null)
            {
                var a = st.Db?.GetArt(id);
                st.InspectArt.sprite = a ?? YgoCardFrames.CardBack() ?? UiFoundation.WhiteSprite();
                st.InspectArt.color = Color.white;
            }

            var limit = OfficialDataSources.StatusOf(id);
            var limitTag = limit switch
            {
                BanlistStatus.Forbidden => "  ·  Forbidden",
                BanlistStatus.Limited => "  ·  Limited",
                BanlistStatus.SemiLimited => "  ·  Semi-Limited",
                _ => ""
            };

            if (st.InspectName != null)
                st.InspectName.text = (def?.name ?? "#" + id) + limitTag;

            if (st.InspectStats != null)
            {
                if (def == null) st.InspectStats.text = "";
                else if (def.IsMonster)
                    st.InspectStats.text =
                        $"Lv{def.level}  ·  {def.attribute} {def.race}\n" +
                        $"ATK {Mathf.Max(0, def.atk)}  /  DEF {Mathf.Max(0, def.def)}" +
                        (string.IsNullOrEmpty(def.archetype) ? "" : "\n" + def.archetype);
                else
                    st.InspectStats.text = (def.type ?? "") +
                        (string.IsNullOrEmpty(def.race) ? "" : "  ·  " + def.race) +
                        (string.IsNullOrEmpty(def.archetype) ? "" : "\n" + def.archetype);
            }

            if (st.InspectDesc != null)
            {
                st.InspectDesc.text = ar ? "" : (def?.desc ?? "");
                var dRt = st.InspectDesc.rectTransform;
                LayoutRebuilder.ForceRebuildLayoutImmediate(dRt);
            }

            var inDeck = Cnt(st.Main, id) + Cnt(st.Extra, id) + Cnt(st.Side, id);
            if (st.InspectAdd != null)
                st.InspectAdd.gameObject.SetActive(!ar && st.Inv != null && st.Inv.CountOnHand(id) > 0);
            if (st.InspectRem != null)
                st.InspectRem.gameObject.SetActive(!ar && inDeck > 0);

            var runner = st.Inspect.GetComponent<InspectTimeout>()
                         ?? st.Inspect.AddComponent<InspectTimeout>();
            if (ar) runner.Arm(st, 2.5f);
            else runner.Cancel();
        }

        sealed class InspectTimeout : MonoBehaviour
        {
            Coroutine _run;

            public void Arm(State st, float sec)
            {
                Cancel();
                _run = StartCoroutine(Go(st, sec));
            }

            public void Cancel()
            {
                if (_run != null) StopCoroutine(_run);
                _run = null;
            }

            IEnumerator Go(State st, float sec)
            {
                yield return new WaitForSeconds(sec);
                HideInspect(st);
                _run = null;
            }
        }
    }
}
