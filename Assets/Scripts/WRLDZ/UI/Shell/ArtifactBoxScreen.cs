using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>Endless Artifact Deck Box — grey YGO faces, dual phone / AR holo.</summary>
    public static class ArtifactBoxScreen
    {
        enum Filter
        {
            All = 0,
            Currency = 1,
            Badge = 2,
            Tablet = 3,
            Key = 4
        }

        public static RectTransform Build(
            Transform modalHost,
            Action onClose,
            UiPresentation? force = null,
            string focusDefId = null)
        {
            var presentation = force ?? DualMenuPresenter.ResolveDefaultPresentation();
            var ar = presentation == UiPresentation.ArDiskHolo;
            DualMenuPresenter.Frame frame = default;
            GameObject inspectGo = null;

            void HideInspect()
            {
                if (inspectGo != null)
                    FloatingPanel.DestroyDeferred(inspectGo);
                inspectGo = null;
            }

            void Close()
            {
                HideInspect();
                DualMenuPresenter.Dismiss(frame);
                onClose?.Invoke();
            }

            frame = DualMenuPresenter.BuildFrame(
                modalHost, "ARTIFACTS", "Endless deck box · always with you", Close, presentation);
            var body = frame.BodyHost;
            var acc = AppSession.Instance != null ? AppSession.Instance.Account : null;
            acc?.EnsureInventory();
            if (acc?.progress != null && acc.inventory != null)
                ArtifactService.MigrateFromLegacy(acc.progress, acc.inventory);

            var filter = InferFilter(focusDefId);

            var filterBar = new GameObject("Filters", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            filterBar.transform.SetParent(body, false);
            FloatingPanel.Place(filterBar.GetComponent<RectTransform>(), 0f, ar ? 0.86f : 0.88f, 1f, 1f);
            var hg = filterBar.GetComponent<HorizontalLayoutGroup>();
            hg.spacing = 4;
            hg.childForceExpandWidth = true;
            hg.childForceExpandHeight = true;

            var scroll = new GameObject("ArtScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0f, 0f, 1f, ar ? 0.86f : 0.88f);
            var sImg = scroll.GetComponent<Image>();
            sImg.sprite = UiFoundation.WhiteSprite();
            sImg.color = new Color(0.03f, 0.05f, 0.09f, 0.35f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 4f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var grid = content.GetComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize = ar ? new Vector2(88f, 126f) : new Vector2(118f, 168f);
            grid.spacing = new Vector2(ar ? 6f : 8f, ar ? 6f : 8f);
            grid.padding = new RectOffset(6, 6, 6, 6);
            grid.childAlignment = TextAnchor.UpperCenter;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = crt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            Action rebuild = null;
            var chipFaces = new List<(Filter f, Image face, Outline edge)>();
            void Chip(Filter f, string label)
            {
                var b = MenuCommandButton.Create(filterBar.transform, label, () =>
                {
                    filter = f;
                    rebuild?.Invoke();
                }, f == filter ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Secondary);
                b.GetComponent<LayoutElement>().minHeight = ar ? 32 : 40;
                chipFaces.Add((f, b.GetComponent<Image>(), b.GetComponent<Outline>()));
            }

            Chip(Filter.All, "ALL");
            Chip(Filter.Currency, "CURRENCY");
            Chip(Filter.Badge, "BADGE");
            Chip(Filter.Tablet, "TABLET");
            Chip(Filter.Key, "KEY");

            rebuild = () =>
            {
                HideInspect();
                for (var i = 0; i < chipFaces.Count; i++)
                {
                    var on = chipFaces[i].f == filter;
                    if (chipFaces[i].face != null)
                        chipFaces[i].face.color = on
                            ? MenuCommandButton.FillGold
                            : MenuCommandButton.FillSecondary;
                    if (chipFaces[i].edge != null)
                        chipFaces[i].edge.effectColor = on
                            ? MenuCommandButton.EdgeGold
                            : MenuCommandButton.EdgeSecondary;
                }

                FloatingPanel.DestroyChildrenNow(content.transform);
                FillGrid(content.transform, acc, filter, focusDefId, ar, (def, inst) =>
                {
                    inspectGo = ShowInspect(frame.Root, inspectGo, def, inst, ar, HideInspect);
                });
            };
            rebuild();
            return frame.Root;
        }

        static Filter InferFilter(string focusDefId)
        {
            if (string.IsNullOrEmpty(focusDefId)) return Filter.All;
            if (focusDefId.StartsWith("se.", StringComparison.OrdinalIgnoreCase))
                return Filter.Tablet;
            if (focusDefId.StartsWith("currency.", StringComparison.OrdinalIgnoreCase))
                return Filter.Currency;
            if (focusDefId.StartsWith("eraz.", StringComparison.OrdinalIgnoreCase)) return Filter.Badge;
            return Filter.Key;
        }

        static bool Passes(ArtifactDef def, Filter filter)
        {
            if (def == null) return false;
            if (filter == Filter.All) return true;
            return filter switch
            {
                Filter.Currency => def.Kind == ArtifactKind.Currency,
                Filter.Badge => def.Kind == ArtifactKind.ErazBadge,
                Filter.Tablet => def.Kind == ArtifactKind.SetEnergy,
                Filter.Key => def.Kind is ArtifactKind.StoryKey or ArtifactKind.Tome
                    or ArtifactKind.TradeTransport or ArtifactKind.Millennium,
                _ => true
            };
        }

        static void FillGrid(Transform host, LocalAccountStore.Account acc, Filter filter, string focusDefId,
            bool ar, Action<ArtifactDef, ArtifactInstance> onInspect)
        {
            var inv = acc?.inventory;
            var instances = inv?.artifactDeckBox?.instances ?? Array.Empty<ArtifactInstance>();
            var shown = new List<ArtifactInstance>();
            foreach (var inst in instances)
            {
                if (inst == null || string.IsNullOrEmpty(inst.defId)) continue;
                var def = ArtifactCatalog.Get(inst.defId);
                if (!Passes(def, filter)) continue;
                shown.Add(inst);
            }

            if (shown.Count == 0)
            {
                var empty = FloatingPanel.Body(host, "No artifact cards in this filter.", ar ? 13 : 15);
                empty.alignment = TextAnchor.MiddleCenter;
                empty.color = new Color(0.70f, 0.74f, 0.80f, 1f);
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.minHeight = ar ? 72 : 96;
                le.preferredWidth = 280;
                le.minWidth = 200;
                return;
            }

            for (var i = 0; i < shown.Count; i++)
            {
                var inst = shown[i];
                var def = ArtifactCatalog.Get(inst.defId);
                var face = BuildFace(host, def, inst, ar, () => onInspect?.Invoke(def, inst));
                if (string.Equals(inst.defId, focusDefId, StringComparison.OrdinalIgnoreCase))
                {
                    var img = face.GetComponent<Image>();
                    if (img != null) img.color = new Color(1f, 0.92f, 0.7f, 1f);
                }
            }
        }

        static GameObject BuildFace(Transform parent, ArtifactDef def, ArtifactInstance inst, bool ar,
            Action onClick)
        {
            var go = new GameObject(def?.id ?? "artifact", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var frame = go.GetComponent<Image>();
            frame.sprite = YgoCardFrames.FrameForArtifact() ?? UiFoundation.WhiteSprite();
            frame.preserveAspect = true;
            frame.color = Color.white;
            frame.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = frame;
            btn.transition = Selectable.Transition.None;
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());

            var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(go.transform, false);
            var artRt = art.GetComponent<RectTransform>();
            artRt.anchorMin = YgoCardFrames.ArtAnchorMin;
            artRt.anchorMax = YgoCardFrames.ArtAnchorMax;
            artRt.offsetMin = Vector2.zero;
            artRt.offsetMax = Vector2.zero;
            var artImg = art.GetComponent<Image>();
            artImg.sprite = LoadArt(def) ?? ImagineAssets.IconStory() ?? UiFoundation.WhiteSprite();
            artImg.preserveAspect = true;
            artImg.raycastTarget = false;
            artImg.color = Color.white;

            var name = FloatingPanel.Body(go.transform, def?.name ?? "Artifact", ar ? 9 : 11);
            name.alignment = TextAnchor.MiddleLeft;
            name.color = DuelystUi.TextCream;
            name.raycastTarget = false;
            FloatingPanel.Place(name.rectTransform, 0.12f, 0.86f, 0.88f, 0.96f);

            var type = FloatingPanel.Body(go.transform, def?.typeLine ?? "ARTIFACT", ar ? 8 : 10);
            type.alignment = TextAnchor.MiddleLeft;
            type.color = new Color(0.78f, 0.82f, 0.88f);
            type.raycastTarget = false;
            FloatingPanel.Place(type.rectTransform, 0.12f, 0.255f, 0.72f, 0.30f);

            var orb = new GameObject("Orb", typeof(RectTransform), typeof(Image));
            orb.transform.SetParent(go.transform, false);
            FloatingPanel.Place(orb.GetComponent<RectTransform>(), 0.78f, 0.248f, 0.90f, 0.305f);
            var oimg = orb.GetComponent<Image>();
            oimg.sprite = ImagineAssets.IconArtifactOrb() ?? ImagineAssets.IconSoul();
            oimg.preserveAspect = true;
            oimg.raycastTarget = false;

            var qtyText = QtyLine(def, inst);
            if (!string.IsNullOrEmpty(qtyText))
            {
                var qty = FloatingPanel.Body(go.transform, qtyText, ar ? 9 : 11);
                qty.alignment = TextAnchor.MiddleRight;
                qty.color = DuelystUi.GoldHot;
                qty.raycastTarget = false;
                FloatingPanel.Place(qty.rectTransform, 0.12f, 0.03f, 0.88f, 0.10f);
            }

            return go;
        }

        static GameObject ShowInspect(RectTransform host, GameObject inspectGo, ArtifactDef def,
            ArtifactInstance inst, bool ar, Action hide)
        {
            if (inspectGo != null)
                FloatingPanel.DestroyDeferred(inspectGo);

            var go = new GameObject("Inspect", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            FloatingPanel.Stretch(go.GetComponent<RectTransform>());
            var dim = go.GetComponent<Image>();
            dim.sprite = UiFoundation.WhiteSprite();
            dim.color = new Color(0f, 0f, 0f, ar ? 0.45f : 0.55f);
            var dimBtn = go.GetComponent<Button>();
            dimBtn.targetGraphic = dim;
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(() => hide?.Invoke());

            var plate = FloatingPanel.Create(go.transform, "InspectPlate", goldEdge: false);
            if (ar) FloatingPanel.Place(plate, 0.08f, 0.18f, 0.92f, 0.82f);
            else FloatingPanel.Place(plate, 0.08f, 0.16f, 0.92f, 0.84f);

            var title = FloatingPanel.Title(plate, def?.name ?? "Artifact", ar ? 16 : 20);
            FloatingPanel.Place(title.rectTransform, 0.06f, 0.82f, 0.78f, 0.96f);

            var close = MenuCommandButton.Create(plate, "X", hide, MenuCommandButton.Kind.Secondary,
                centerTitle: true);
            close.name = "InspectClose";
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(56f, 56f);
            closeRt.anchoredPosition = new Vector2(-8f, -8f);

            var type = FloatingPanel.Body(plate, def?.typeLine ?? "ARTIFACT", ar ? 11 : 13);
            type.color = DuelystUi.Cyan;
            FloatingPanel.Place(type.rectTransform, 0.06f, 0.72f, 0.94f, 0.82f);

            var qty = QtyLine(def, inst);
            var desc = string.IsNullOrEmpty(def?.desc) ? "No text." : def.desc;
            if (!string.IsNullOrEmpty(qty))
                desc = qty + "\n\n" + desc;
            var body = FloatingPanel.Body(plate, desc, ar ? 12 : 14);
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            FloatingPanel.Place(body.rectTransform, 0.06f, 0.08f, 0.94f, 0.70f);

            return go;
        }

        static string QtyLine(ArtifactDef def, ArtifactInstance inst)
        {
            if (def == null || string.IsNullOrEmpty(def.qtyLabel) || inst == null) return "";
            if (string.Equals(def.qtyLabel, "USES", StringComparison.OrdinalIgnoreCase))
                return "USES " + Mathf.Max(0, inst.charges);
            return def.qtyLabel + " " + Mathf.Max(0, inst.qty);
        }

        static Sprite LoadArt(ArtifactDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.art)) return null;
            return StreamingSprite.Load(def.art);
        }
    }
}
