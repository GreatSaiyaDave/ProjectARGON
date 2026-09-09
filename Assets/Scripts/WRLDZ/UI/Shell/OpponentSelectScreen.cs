using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>Pick a named AI opponent. Standard TCG 8000 LP. Dual presentation.</summary>
    public static class OpponentSelectScreen
    {
        public static RectTransform Build(
            Transform modalHost,
            Action onClose,
            bool labTest,
            UiPresentation? force = null,
            Action<OpponentDef> onPicked = null)
        {
            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "OPPONENTS", "Standard TCG · 8000 LP · your deck vs theirs", onClose, force);
            var body = frame.BodyHost;

            var status = FloatingPanel.Body(body, "Tap a name to duel.", 13);
            FloatingPanel.Place(status.rectTransform, 0.02f, 0.01f, 0.98f, 0.10f);
            status.alignment = TextAnchor.MiddleLeft;
            status.color = DuelystUi.Cyan;

            var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.01f, 0.12f, 0.99f, 0.99f);
            var bg = scroll.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.03f, 0.05f, 0.09f, 0.55f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 4f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = crt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var list = OpponentCatalog.All();
            for (var i = 0; i < list.Count; i++)
            {
                var opp = list[i];
                if (opp == null) continue;
                var missing = !OpponentCatalog.DeckFileExists(opp.file);
                var label = opp.name + "\n" + (missing ? "(deck file missing)" : opp.notes);
                Row(content.transform, label, missing
                    ? null
                    : () =>
                    {
                        if (!labTest && !AppSession.Ensure().CanStartConstructedPvAi())
                        {
                            FreeUiKit.PlayClick();
                            status.text = "ERAZ · LOCKED — finish tutorial";
                            return;
                        }

                        FreeUiKit.PlayConfirm();
                        OpponentCatalog.Pending = opp;
                        if (onPicked != null)
                        {
                            onPicked(opp);
                            return;
                        }

                        var cfg = OpponentCatalog.MakeMatch(opp, labTest, digital: labTest);
                        onClose?.Invoke();
                        AppSession.Ensure().StartArDuel(cfg);
                    });
            }

            return frame.Root;
        }

        /// <summary>Full-screen overlay when MenuShell is not mounted (overworld fallback).</summary>
        public static GameObject OpenOverlay(Transform lifetimeHost, Action onClosed = null,
            Action<OpponentDef> onPicked = null)
        {
            UiFoundation.EnsureEventSystem();
            FreeUiKit.EnsureLoaded();

            var canvas = WrldzTheme.Canvas("OpponentSelectCanvas", 90);
            EgyptianAgesAtmosphere.Attach(canvas, MenuAge.UmbraxRift, showAgeCaption: false);
            var host = canvas.gameObject;
            if (lifetimeHost != null)
                host.transform.SetParent(lifetimeHost, false);

            void Close()
            {
                onClosed?.Invoke();
                if (host != null)
                    UnityEngine.Object.Destroy(host);
            }

            Build(canvas, Close, labTest: false, onPicked: onPicked);
            return host;
        }

        static void Row(Transform host, string text, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 64;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.05f, 0.08f, 0.14f, 0.9f);
            var t = FloatingPanel.Body(go.transform, text, 13);
            FloatingPanel.Place(t.rectTransform, 0.04f, 0.08f, 0.96f, 0.92f);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = DuelystUi.TextCream;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var btn = go.GetComponent<Button>();
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            else
                btn.interactable = false;
        }
    }
}
