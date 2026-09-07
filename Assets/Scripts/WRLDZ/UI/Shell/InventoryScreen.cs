using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Travel bag: RE4 tetris case + Pokémon-style pocket tabs.
    /// <list type="number">
    /// <item><b>Case</b> — 10×8 silhouette; live cells span their footprint; locked cells are later expansions</item>
    /// <item><b>Pockets</b> — clothing holsters, one deck box each</item>
    /// <item><b>Decks</b> — create / delete / open the 40-card editor</item>
    /// <item><b>Home</b> — unpack targets, collection glance, trade transport</item>
    /// </list>
    /// Deck boxes never occupy backpack bulk — they live in clothing pockets.
    /// </summary>
    public static class InventoryScreen
    {
        enum Tab { Case = 0, Pockets = 1, Decks = 2, Home = 3 }
        enum PickKind { None = 0, Box = 1, Binder = 2, Artifact = 3, Deck = 4, Locked = 5 }

        class State
        {
            public Tab Tab = Tab.Case;
            public InventoryService.ViewModel Vm;
            public UiPresentation Presentation;
            public Transform ModalHost;
            public RectTransform Root;
            public Text InspectName, InspectBlurb, FillLabel;
            public string Toast;
            public Image FillBar;
            public Transform ListHost, GridHost, Wallet, ListPane;
            public Image InspectIcon;
            public Action Refresh;
            public PickKind Pick;
            public int PickIndex = -1;
            public readonly List<Image> TabFaces = new();
            public readonly List<Tab> TabIds = new();
            public Button PackBtn, UnpackBtn, EditBtn, DeleteBtn;
            public GameObject ActionRow;
        }

        public static RectTransform Build(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            var presentation = force ?? DualMenuPresenter.ResolveDefaultPresentation();
            var ar = presentation == UiPresentation.ArDiskHolo;
            var frame = DualMenuPresenter.BuildFrame(
                modalHost,
                "BAG",
                "",
                onClose,
                presentation);

            var st = new State
            {
                Presentation = presentation,
                Tab = Tab.Case,
                PickIndex = -1,
                ModalHost = modalHost,
                Root = frame.Root
            };
            var body = frame.BodyHost;

            BuildTabs(body, st, ar);
            BuildCurrencyAndFill(body, st, ar);

            var stage = FlatPane(body, "Stage", new Color(0.025f, 0.04f, 0.06f, 0.55f));
            if (ar) FloatingPanel.Place(stage, 0.00f, 0.18f, 1f, 0.76f);
            else FloatingPanel.Place(stage, 0.00f, 0.16f, 1f, 0.80f);

            var listPane = new GameObject("ListPane", typeof(RectTransform), typeof(Image));
            listPane.transform.SetParent(stage, false);
            FloatingPanel.Stretch(listPane.GetComponent<RectTransform>(), 4f);
            var lpImg = listPane.GetComponent<Image>();
            lpImg.sprite = UiFoundation.WhiteSprite();
            lpImg.color = new Color(1f, 1f, 1f, 0.01f);
            lpImg.raycastTarget = false;
            st.ListPane = listPane.transform;
            st.ListHost = ScrollColumn(listPane.GetComponent<RectTransform>(), ar);

            var grid = new GameObject("GridHost", typeof(RectTransform), typeof(Image));
            grid.transform.SetParent(stage, false);
            FloatingPanel.Stretch(grid.GetComponent<RectTransform>(), 6f);
            var gImg = grid.GetComponent<Image>();
            gImg.sprite = UiFoundation.WhiteSprite();
            gImg.color = new Color(0.03f, 0.05f, 0.07f, 0.92f);
            gImg.raycastTarget = true;
            st.GridHost = grid.transform;

            BuildInspect(body, st, ar);

            st.Refresh = () => Rebuild(st);
            st.Refresh();
            return frame.Root;
        }

        static void BuildTabs(Transform body, State st, bool ar)
        {
            var tabBar = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            tabBar.transform.SetParent(body, false);
            FloatingPanel.Place(tabBar.GetComponent<RectTransform>(), 0.00f, ar ? 0.88f : 0.90f, 1f, 1f);
            var h = tabBar.GetComponent<HorizontalLayoutGroup>();
            h.spacing = ar ? 4 : 6;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.padding = new RectOffset(2, 2, 2, 2);

            void TabChip(Tab tab, string label, Sprite icon)
            {
                var b = HubChrome.Capsule(tabBar.transform, label, () =>
                {
                    st.Tab = tab;
                    st.Pick = PickKind.None;
                    st.PickIndex = -1;
                    st.Toast = null;
                    FreeUiKit.PlaySelect();
                    st.Refresh?.Invoke();
                }, tab == Tab.Case ? MenuCommandButton.Kind.Gold : MenuCommandButton.Kind.Primary,
                    centerTitle: true, titleSize: 14);
                b.GetComponent<LayoutElement>().minHeight = ar ? 36 : 44;
                st.TabFaces.Add(b.GetComponent<Image>());
                st.TabIds.Add(tab);
                if (icon == null) return;
                var ico = new GameObject("Ico", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(b.transform, false);
                var irt = ico.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.04f, 0.18f);
                irt.anchorMax = new Vector2(0.22f, 0.82f);
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
                var title = b.transform.Find("Title") as RectTransform;
                if (title != null)
                    FloatingPanel.Place(title, 0.24f, 0.12f, 0.96f, 0.88f);
            }

            TabChip(Tab.Case, "CASE", ImagineAssets.IconBag());
            TabChip(Tab.Pockets, "POCKETS", ImagineAssets.IconDeck());
            TabChip(Tab.Decks, "DECKS", ImagineAssets.IconDeckMain() ?? ImagineAssets.IconDeck());
            TabChip(Tab.Home, "HOME", ImagineAssets.IconCompass());
        }

        static void BuildCurrencyAndFill(Transform body, State st, bool ar)
        {
            var strip = new GameObject("Wallet", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            strip.transform.SetParent(body, false);
            st.Wallet = strip.transform;
            FloatingPanel.Place(strip.GetComponent<RectTransform>(), 0.00f, ar ? 0.78f : 0.82f, 0.72f,
                ar ? 0.86f : 0.88f);
            var hg = strip.GetComponent<HorizontalLayoutGroup>();
            hg.spacing = 6;
            hg.childForceExpandWidth = true;
            hg.childForceExpandHeight = true;

            WalletChip(strip.transform, ImagineAssets.IconDigizeni(), "Đ", st, () => ArtifactService.Digizeni);
            WalletChip(strip.transform, ImagineAssets.IconDuelCoin(), "◎", st, () => ArtifactService.DuelCoin);
            WalletChip(strip.transform, ImagineAssets.IconSetEnergy(), "⚡", st, () =>
                ArtifactService.FirstSetEnergyId(AppSession.Ensure().Account?.inventory)
                ?? ArtifactService.SetEnergyFocus);

            var fillHost = FlatPane(body, "Fill", new Color(0.04f, 0.06f, 0.09f, 0.55f));
            FloatingPanel.Place(fillHost, 0.74f, ar ? 0.78f : 0.82f, 1f, ar ? 0.86f : 0.88f);
            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(fillHost, false);
            FloatingPanel.Place(track.GetComponent<RectTransform>(), 0.06f, 0.18f, 0.94f, 0.52f);
            var tImg = track.GetComponent<Image>();
            tImg.sprite = UiFoundation.WhiteSprite();
            tImg.color = new Color(0.04f, 0.06f, 0.10f, 0.9f);
            var fill = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0.01f, 1f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            st.FillBar = fill.GetComponent<Image>();
            st.FillBar.sprite = UiFoundation.WhiteSprite();
            st.FillBar.color = DuelystUi.Gold;
            st.FillLabel = FloatingPanel.Body(fillHost, "", ar ? 10 : 11);
            FloatingPanel.Place(st.FillLabel.rectTransform, 0.06f, 0.52f, 0.94f, 0.94f);
            st.FillLabel.alignment = TextAnchor.MiddleCenter;
            st.FillLabel.color = DuelystUi.GoldHot;
        }

        static void WalletChip(Transform parent, Sprite icon, string prefix, State st, Func<string> focusDefId)
        {
            var go = new GameObject("Chip_" + prefix, typeof(RectTransform), typeof(Image), typeof(LayoutElement),
                typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            var spr = ImagineAssets.HudChip() ?? UiFoundation.WhiteSprite();
            img.sprite = spr;
            img.type = spr != null && spr.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            HubChrome.LiftPlate(img, DuelystUi.Gold);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                FreeUiKit.PlaySelect();
                OpenArtifactBox(st, focusDefId != null ? focusDefId() : null);
            });
            if (icon != null)
            {
                var ico = new GameObject("I", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(go.transform, false);
                var irt = ico.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.04f, 0.14f);
                irt.anchorMax = new Vector2(0.30f, 0.86f);
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }

            var t = FloatingPanel.Body(go.transform, prefix, 12);
            t.name = "Val";
            FloatingPanel.Place(t.rectTransform, 0.30f, 0.08f, 0.96f, 0.92f);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = DuelystUi.GoldHot;
        }

        static void BuildInspect(Transform detail, State st, bool ar)
        {
            var pane = FlatPane(detail, "Inspect", new Color(0.04f, 0.06f, 0.09f, 0.62f));
            if (ar) FloatingPanel.Place(pane, 0.00f, 0.00f, 1f, 0.16f);
            else FloatingPanel.Place(pane, 0.00f, 0.00f, 1f, 0.14f);
            var rule = new GameObject("Rule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(pane, false);
            FloatingPanel.Place(rule.GetComponent<RectTransform>(), 0.04f, 0.96f, 0.96f, 1f);
            var ruleImg = rule.GetComponent<Image>();
            ruleImg.sprite = UiFoundation.WhiteSprite();
            ruleImg.color = new Color(DuelystUi.Gold.r, DuelystUi.Gold.g, DuelystUi.Gold.b, 0.55f);
            ruleImg.raycastTarget = false;

            var iconGo = new GameObject("Ico", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(pane, false);
            FloatingPanel.Place(iconGo.GetComponent<RectTransform>(), 0.02f, 0.18f, 0.12f, 0.88f);
            st.InspectIcon = iconGo.GetComponent<Image>();
            st.InspectIcon.sprite = ImagineAssets.IconBag() ?? UiFoundation.WhiteSprite();
            st.InspectIcon.preserveAspect = true;
            st.InspectIcon.raycastTarget = false;

            st.InspectName = FloatingPanel.Title(pane, "Select an item", ar ? 14 : 16);
            FloatingPanel.Place(st.InspectName.rectTransform, 0.14f, 0.52f, 0.62f, 0.92f);
            st.InspectBlurb = FloatingPanel.Body(pane, "Tap a tile in the case.", ar ? 10 : 12);
            FloatingPanel.Place(st.InspectBlurb.rectTransform, 0.14f, 0.08f, 0.62f, 0.52f);
            st.InspectBlurb.color = DuelystUi.TextMuted;
            st.InspectBlurb.alignment = TextAnchor.UpperLeft;

            st.ActionRow = new GameObject("Actions", typeof(RectTransform));
            st.ActionRow.transform.SetParent(pane, false);
            FloatingPanel.Place(st.ActionRow.GetComponent<RectTransform>(), 0.64f, 0.08f, 0.98f, 0.92f);

            st.PackBtn = HubChrome.Capsule(st.ActionRow.transform, "PACK", () =>
            {
                ActPackSelected(st);
                st.Refresh?.Invoke();
            }, MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 13, plated: false);
            FloatingPanel.Place(st.PackBtn.GetComponent<RectTransform>(), 0.00f, 0.52f, 0.48f, 1f);

            st.UnpackBtn = HubChrome.Capsule(st.ActionRow.transform, "SEND HOME", () =>
            {
                ActUnpackSelected(st);
                st.Refresh?.Invoke();
            }, MenuCommandButton.Kind.Secondary, centerTitle: true, titleSize: 13, plated: false);
            FloatingPanel.Place(st.UnpackBtn.GetComponent<RectTransform>(), 0.52f, 0.52f, 1f, 1f);

            st.EditBtn = HubChrome.Capsule(st.ActionRow.transform, "EDIT DECK", () =>
            {
                OpenDeckEditor(st);
            }, MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 13, plated: false);
            FloatingPanel.Place(st.EditBtn.GetComponent<RectTransform>(), 0.00f, 0.00f, 0.48f, 0.46f);

            st.DeleteBtn = HubChrome.Capsule(st.ActionRow.transform, "DELETE", () =>
            {
                ActDeleteDeck(st);
                st.Refresh?.Invoke();
            }, MenuCommandButton.Kind.Danger, centerTitle: true, titleSize: 13, plated: false);
            FloatingPanel.Place(st.DeleteBtn.GetComponent<RectTransform>(), 0.52f, 0.00f, 1f, 0.46f);
        }

        static RectTransform FlatPane(Transform parent, string name, Color fill)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.type = Image.Type.Simple;
            img.color = fill.a > 0.5f ? HubChrome.WellFill : fill;
            img.raycastTarget = true;
            return go.GetComponent<RectTransform>();
        }

        static Transform ScrollColumn(RectTransform pane, bool ar)
        {
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(pane, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 8f);
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
            vlg.spacing = ar ? 4 : 6;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = pane.gameObject.GetComponent<ScrollRect>() ?? pane.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content.transform;
        }

        static void Rebuild(State st)
        {
            var acc = AppSession.Ensure().Account;
            st.Vm = InventoryService.BuildView(acc);
            FloatingPanel.DestroyChildrenNow(st.ListHost);
            FloatingPanel.DestroyChildrenNow(st.GridHost);

            for (var i = 0; i < st.TabFaces.Count; i++)
            {
                var on = st.TabIds[i] == st.Tab;
                var face = st.TabFaces[i];
                if (face == null) continue;
                face.color = Color.white;
                var btn = face.GetComponent<Button>();
                if (btn != null)
                {
                    btn.transition = Selectable.Transition.None;
                    btn.targetGraphic = face;
                }
                var plate = on
                    ? ImagineAssets.TileHubGold() ?? ImagineAssets.BtnGold()
                    : ImagineAssets.BtnPrimary() ?? ImagineAssets.TileHub();
                if (plate != null)
                {
                    face.sprite = plate;
                    face.type = plate.border.sqrMagnitude > 0.1f ? Image.Type.Sliced : Image.Type.Simple;
                }

                HubChrome.LiftPlate(face, on ? DuelystUi.Gold : DuelystUi.Cyan);
            }

            RefreshWallet(st);
            var vm = st.Vm;
            var used = Mathf.Max(0, vm.PackCellsUsed);
            var total = Mathf.Max(1, vm.PackCellsTotal);
            if (st.FillBar != null)
            {
                var t = vm.BackpackUnlocked ? Mathf.Clamp01(used / (float)total) : 0f;
                var frt = st.FillBar.rectTransform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = new Vector2(Mathf.Max(0.02f, t), 1f);
                frt.offsetMin = new Vector2(2f, 2f);
                frt.offsetMax = new Vector2(-2f, -2f);
            }

            if (st.FillLabel != null)
                st.FillLabel.text = vm.BackpackUnlocked
                    ? $"{used}/{total}  ·  {vm.PackW}×{vm.PackH}"
                    : "LOCKED";

            var showGrid = st.Tab == Tab.Case || st.Tab == Tab.Pockets;
            if (st.ListPane != null) st.ListPane.gameObject.SetActive(!showGrid);
            if (st.GridHost != null) st.GridHost.gameObject.SetActive(showGrid);

            switch (st.Tab)
            {
                case Tab.Case: BuildCaseTab(st); break;
                case Tab.Pockets: BuildPocketsTab(st); break;
                case Tab.Decks: BuildDecksTab(st); break;
                case Tab.Home: BuildHomeTab(st); break;
            }

            if (!string.IsNullOrEmpty(st.Toast) && st.InspectBlurb != null)
                st.InspectBlurb.text = st.Toast;
        }

        static void Note(State st, string msg)
        {
            st.Toast = msg;
            if (st.InspectBlurb != null) st.InspectBlurb.text = msg ?? "";
        }

        static void RefreshWallet(State st)
        {
            if (st.Wallet == null) return;
            SetChip(st.Wallet, 0, "Đ " + st.Vm.Digizeni);
            SetChip(st.Wallet, 1, "◎ " + st.Vm.DuelCoins);
            SetChip(st.Wallet, 2, "⚡ " + st.Vm.SetEnergy);
        }

        static void SetChip(Transform wallet, int index, string text)
        {
            if (index < 0 || index >= wallet.childCount) return;
            var val = wallet.GetChild(index).Find("Val");
            var t = val != null ? val.GetComponent<Text>() : null;
            if (t != null) t.text = text;
        }

        static void BuildCaseTab(State st)
        {
            DrawCaseGrid(st);
            if (st.Pick == PickKind.None)
                ShowInspect(st, ImagineAssets.IconBag(), "Your case",
                    st.Vm.BackpackUnlocked
                        ? "Tetris tiles. Expansions: story, then seamstress."
                        : st.Vm.PackSummary, showPack: false, showUnpack: false);
            else if (st.Pick == PickKind.Locked)
                ShowInspect(st, ImagineAssets.IconBazaar() ?? ImagineAssets.IconBag(), "Locked expansion",
                    "Story grows the case. Remaining tiers are sewn by the seamstress at the bazaar.",
                    showPack: false, showUnpack: false);
            else
                RefreshInspectFromPick(st, AppSession.Ensure().Account?.inventory);
        }

        static void BuildHomeTab(State st)
        {
            var acc = AppSession.Ensure().Account;
            acc?.EnsureInventory();
            var inv = acc?.inventory;
            var ar = st.Presentation == UiPresentation.ArDiskHolo;
            var vm = st.Vm;

            AddHeader(st.ListHost, "ON YOU");
            if (inv?.artifactDeckBox != null && inv.artifactDeckBox.owned)
            {
                ItemRow(st.ListHost, ImagineAssets.IconStory(), "Artifact Deck Box",
                    $"{st.Vm.ArtifactCount} key-item cards  ·  always with you",
                    false,
                    () => OpenArtifactBox(st), ar);
            }
            else
            {
                ItemRow(st.ListHost, ImagineAssets.IconStory(), "Artifact Deck Box",
                    "Always with you once granted.", false, null, ar);
            }

            AddHeader(st.ListHost, "AT HOME");
            var anyHome = false;
            if (inv?.storageBoxes != null)
            {
                for (var i = 0; i < inv.storageBoxes.Length; i++)
                {
                    var b = inv.storageBoxes[i];
                    if (b == null || !b.atHome) continue;
                    anyHome = true;
                    var idx = i;
                    ItemRow(st.ListHost, ImagineAssets.IconBag(), b.name,
                        $"{b.UsedSlots()}/{b.capacity} cards  ·  {b.Footprint.x}×{b.Footprint.y}",
                        st.Pick == PickKind.Box && st.PickIndex == idx,
                        () => Select(st, PickKind.Box, idx), ar);
                }
            }

            if (inv?.binders != null)
            {
                for (var i = 0; i < inv.binders.Length; i++)
                {
                    var b = inv.binders[i];
                    if (b == null || !b.atHome) continue;
                    anyHome = true;
                    var idx = i;
                    ItemRow(st.ListHost, ImagineAssets.IconTome(), b.name,
                        $"{b.UsedSlots()}/{b.MaxCards}  ·  {b.pageCount} pages  ·  2×2",
                        st.Pick == PickKind.Binder && st.PickIndex == idx,
                        () => Select(st, PickKind.Binder, idx), ar);
                }
            }

            if (!anyHome)
                ItemRow(st.ListHost, null, "Nothing at home", "SEND HOME from the case lands boxes here.",
                    false, null, ar);

            AddHeader(st.ListHost, "COLLECTION");
            ItemRow(st.ListHost, ImagineAssets.IconBag(), "Storage",
                $"{vm.HomeBoxCards} / {vm.HomeBoxCap} cards", false, null, ar);
            ItemRow(st.ListHost, ImagineAssets.IconTome(), "Binders",
                $"{vm.BinderCards} cards · {vm.BinderPages} pages", false, null, ar);
            ItemRow(st.ListHost, ImagineAssets.IconBazaar(), "Trade transport",
                $"×{vm.TradeTransportCharges}  ·  tap to buy +1", false, () =>
                {
                    if (InventoryService.TryBuyTradeTransport(acc, 1, out var err))
                    {
                        FreeUiKit.PlayConfirm();
                        st.Refresh?.Invoke();
                    }
                    else
                    {
                        FreeUiKit.PlayClick();
                        Note(st, err ?? "Buy failed");
                    }
                }, ar);

            AddHeader(st.ListHost, "TOP STACKS");
            if (vm.CollectionLines == null || vm.CollectionLines.Count == 0)
                ItemRow(st.ListHost, ImagineAssets.IconDeck(), "No stacks yet", "Win duels to fill storage.",
                    false, null, ar);
            else
            {
                foreach (var line in vm.CollectionLines)
                    ItemRow(st.ListHost, ImagineAssets.IconDeck(), line, "", false, null, ar);
            }

            if (st.Pick == PickKind.None)
                ShowInspect(st, ImagineAssets.IconCompass(), "Home base",
                    "View anywhere. PACK a box into the case.", showPack: false, showUnpack: false);
            else
                RefreshInspectFromPick(st, inv);
        }

        static void BuildPocketsTab(State st)
        {
            var acc = AppSession.Ensure().Account;
            acc?.EnsureInventory();
            var inv = acc?.inventory;
            inv?.EnsureValid();
            var ar = st.Presentation == UiPresentation.ArDiskHolo;
            var host = st.GridHost;

            var shirt = ClothingCatalog.Get(acc?.avatar != null ? acc.avatar.shirtId : ClothingCatalog.DefaultShirt);
            var bottoms = ClothingCatalog.Get(acc?.avatar != null ? acc.avatar.bottomsId : ClothingCatalog.DefaultBottoms);
            var shirtP = ClothingCatalog.PocketsOnItem(shirt, inv?.wardrobe);
            var bottomP = ClothingCatalog.PocketsOnItem(bottoms, inv?.wardrobe);

            var slots = inv?.pockets?.pocketDeckBoxIndex ?? Array.Empty<int>();
            var n = Mathf.Max(1, slots.Length);
            var cols = Mathf.Min(ar ? 2 : 3, n);
            var rows = Mathf.CeilToInt(n / (float)cols);

            for (var i = 0; i < n; i++)
            {
                var col = i % cols;
                var row = i / cols;
                var pi = i;
                var di = i < slots.Length ? slots[i] : -1;
                var filled = di >= 0 && inv?.deckBoxes != null && di < inv.deckBoxes.Length && inv.deckBoxes[di] != null;
                var name = filled ? (inv.deckBoxes[di].name ?? ("Deck " + (di + 1))) : "EMPTY";

                var go = new GameObject("Holster_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(host, false);
                var rt = go.GetComponent<RectTransform>();
                var x0 = col / (float)cols;
                var x1 = (col + 1) / (float)cols;
                var y1 = 1f - row / (float)(rows + 0.35f);
                var y0 = 1f - (row + 1) / (float)(rows + 0.35f);
                rt.anchorMin = new Vector2(x0 + 0.02f, y0 + 0.03f);
                rt.anchorMax = new Vector2(x1 - 0.02f, y1 - 0.03f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var img = go.GetComponent<Image>();
                img.sprite = UiFoundation.WhiteSprite();
                img.color = filled
                    ? new Color(0.28f, 0.22f, 0.08f, 0.72f)
                    : new Color(0.08f, 0.10f, 0.14f, 0.72f);
                var ol = go.AddComponent<Outline>();
                ol.effectColor = filled ? MenuCommandButton.EdgeGold : MenuCommandButton.EdgeSecondary;
                ol.effectDistance = new Vector2(1.2f, -1.2f);
                ol.useGraphicAlpha = false;

                var ico = new GameObject("I", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(go.transform, false);
                FloatingPanel.Place(ico.GetComponent<RectTransform>(), 0.18f, 0.42f, 0.82f, 0.90f);
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = ImagineAssets.DeckStyleIcon(filled ? inv.deckBoxes[di].iconId : "")
                              ?? ImagineAssets.IconDeck();
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
                iimg.color = filled ? Color.white : new Color(1f, 1f, 1f, 0.35f);

                var title = FloatingPanel.Body(go.transform, "POCKET " + (pi + 1), ar ? 10 : 12);
                FloatingPanel.Place(title.rectTransform, 0.06f, 0.28f, 0.94f, 0.42f);
                title.alignment = TextAnchor.MiddleCenter;
                title.color = DuelystUi.GoldHot;

                var body = FloatingPanel.Body(go.transform,
                    filled ? name : "Empty",
                    ar ? 11 : 13);
                FloatingPanel.Place(body.rectTransform, 0.06f, 0.06f, 0.94f, 0.28f);
                body.alignment = TextAnchor.MiddleCenter;
                body.color = filled ? DuelystUi.TextCream : DuelystUi.TextMuted;
                body.horizontalOverflow = HorizontalWrapMode.Wrap;
                body.verticalOverflow = VerticalWrapMode.Truncate;

                go.GetComponent<Button>().onClick.AddListener(() =>
                {
                    CyclePocket(acc, pi);
                    FreeUiKit.PlaySelect();
                    st.Refresh?.Invoke();
                });
            }

            var note = FloatingPanel.Body(host,
                $"{shirt?.Name ?? "Shirt"} {shirtP}  ·  {bottoms?.Name ?? "Bottoms"} {bottomP}  ·  " +
                "One deck box per holster. Clothier sells extra-pocket clothes; tailor / seamstress sew more.",
                ar ? 10 : 12);
            FloatingPanel.Place(note.rectTransform, 0.04f, 0.01f, 0.96f, 0.14f);
            note.alignment = TextAnchor.MiddleCenter;
            note.color = DuelystUi.TextMuted;
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            ShowInspect(st, ImagineAssets.IconDeck(), "Clothing holsters",
                "One deck box per pocket. Tap a holster to cycle.",
                showPack: false, showUnpack: false);
        }

        static void BuildDecksTab(State st)
        {
            var acc = AppSession.Ensure().Account;
            acc?.EnsureInventory();
            var inv = acc?.inventory;
            inv?.EnsureDeckBoxSlots();
            var ar = st.Presentation == UiPresentation.ArDiskHolo;

            var create = HubChrome.Capsule(st.ListHost, "CREATE DECK", () =>
            {
                if (InventoryService.TryCreateDeckBox(acc, out var idx, out var err))
                {
                    st.Pick = PickKind.Deck;
                    st.PickIndex = idx;
                    FreeUiKit.PlayConfirm();
                    st.Refresh?.Invoke();
                    Note(st, "New deck box — EDIT DECK opens the builder.");
                }
                else
                {
                    FreeUiKit.PlayClick();
                    Note(st, err ?? "Could not create deck.");
                }
            }, MenuCommandButton.Kind.Gold, centerTitle: true, titleSize: 16, plated: false);
            create.GetComponent<LayoutElement>().minHeight = ar ? 40 : 48;

            AddHeader(st.ListHost, "DECK BOXES");
            if (inv?.deckBoxes == null || inv.deckBoxes.Length == 0)
            {
                ItemRow(st.ListHost, ImagineAssets.IconDeck(), "No deck boxes", "CREATE DECK to start a list.",
                    false, null, ar);
            }
            else
            {
                for (var i = 0; i < inv.deckBoxes.Length; i++)
                {
                    var d = inv.deckBoxes[i];
                    if (d == null) continue;
                    var idx = i;
                    var pocket = PocketOf(inv, idx);
                    var where = pocket >= 0 ? "pocket " + (pocket + 1) : "at home";
                    var selected = st.Pick == PickKind.Deck && st.PickIndex == idx;
                    ItemRow(st.ListHost, ImagineAssets.DeckStyleIcon(d.iconId) ?? ImagineAssets.IconDeck(),
                        d.name ?? ("Deck " + (idx + 1)),
                        $"Main {d.main?.Length ?? 0} · Extra {d.extra?.Length ?? 0} · Side {d.side?.Length ?? 0}  ·  {where}",
                        selected, () => Select(st, PickKind.Deck, idx), ar);
                }
            }

            if (st.Pick == PickKind.Deck)
                RefreshInspectFromPick(st, inv);
            else
                ShowInspect(st, ImagineAssets.IconDeck(), "Deck boxes",
                    "CREATE / DELETE here. EDIT DECK opens the 40-card builder.",
                    showPack: false, showUnpack: false, showEdit: false, showDelete: false);
        }

        static int PocketOf(PlayerInventory inv, int deckIndex)
        {
            var slots = inv?.pockets?.pocketDeckBoxIndex;
            if (slots == null) return -1;
            for (var i = 0; i < slots.Length; i++)
                if (slots[i] == deckIndex) return i;
            return -1;
        }

        static void RefreshInspectFromPick(State st, PlayerInventory inv)
        {
            if (inv == null) return;
            if (st.Pick == PickKind.Box && inv.storageBoxes != null &&
                st.PickIndex >= 0 && st.PickIndex < inv.storageBoxes.Length)
            {
                var b = inv.storageBoxes[st.PickIndex];
                if (b == null) return;
                ShowInspect(st, ImagineAssets.IconBag(), b.name,
                    $"{b.UsedSlots()} of {b.capacity} cards · {b.Footprint.x}×{b.Footprint.y} · {(b.atHome ? "at home" : "in the case")}",
                    showPack: b.atHome, showUnpack: !b.atHome);
            }
            else if (st.Pick == PickKind.Binder && inv.binders != null &&
                     st.PickIndex >= 0 && st.PickIndex < inv.binders.Length)
            {
                var b = inv.binders[st.PickIndex];
                if (b == null) return;
                ShowInspect(st, ImagineAssets.IconTome(), b.name,
                    $"{b.UsedSlots()} of {b.MaxCards} · {b.pageCount} pages · {(b.atHome ? "at home" : "in the case")}",
                    showPack: b.atHome, showUnpack: !b.atHome);
            }
            else if (st.Pick == PickKind.Artifact && inv.artifactDeckBox != null)
            {
                ShowInspect(st, ImagineAssets.IconStory(), "Artifact box",
                    $"{st.Vm.ArtifactCount} artifact cards · always with you",
                    showPack: false, showUnpack: false);
            }
            else if (st.Pick == PickKind.Deck && inv.deckBoxes != null &&
                     st.PickIndex >= 0 && st.PickIndex < inv.deckBoxes.Length)
            {
                var d = inv.deckBoxes[st.PickIndex];
                if (d == null) return;
                var pocket = PocketOf(inv, st.PickIndex);
                var where = pocket >= 0 ? "in pocket " + (pocket + 1) : "not holstered — assign on Pockets";
                ShowInspect(st, ImagineAssets.DeckStyleIcon(d.iconId) ?? ImagineAssets.IconDeck(),
                    d.name ?? "Deck",
                    $"Main {d.main?.Length ?? 0} · Extra {d.extra?.Length ?? 0} · Side {d.side?.Length ?? 0}\n{where}",
                    showPack: false, showUnpack: false, showEdit: true, showDelete: true);
            }
        }

        static void Select(State st, PickKind kind, int index)
        {
            st.Pick = kind;
            st.PickIndex = index;
            FreeUiKit.PlaySelect();
            st.Refresh?.Invoke();
        }

        static void ShowInspect(State st, Sprite icon, string name, string blurb,
            bool showPack, bool showUnpack, bool showEdit = false, bool showDelete = false)
        {
            if (st.InspectIcon != null)
                st.InspectIcon.sprite = icon ?? ImagineAssets.IconBag() ?? UiFoundation.WhiteSprite();
            if (st.InspectName != null) st.InspectName.text = name ?? "";
            if (st.InspectBlurb != null) st.InspectBlurb.text = blurb ?? "";
            var ar = st.Presentation == UiPresentation.ArDiskHolo;
            var any = (showPack || showUnpack) && !ar || showEdit || showDelete;
            if (st.ActionRow != null)
                st.ActionRow.SetActive(any);
            if (st.PackBtn != null) st.PackBtn.gameObject.SetActive(showPack && !ar);
            if (st.UnpackBtn != null) st.UnpackBtn.gameObject.SetActive(showUnpack && !ar);
            if (st.EditBtn != null) st.EditBtn.gameObject.SetActive(showEdit);
            if (st.DeleteBtn != null) st.DeleteBtn.gameObject.SetActive(showDelete);
        }

        static void CyclePocket(LocalAccountStore.Account acc, int pocketIndex)
        {
            if (acc == null) return;
            acc.EnsureInventory();
            var inv = acc.inventory;
            inv.EnsureValid();
            inv.pockets.EnsureValid(inv.deckBoxes?.Length ?? 0, inv.carryDeckBoxSlots);
            if (pocketIndex < 0 || pocketIndex >= inv.pockets.pocketDeckBoxIndex.Length) return;
            var current = inv.pockets.pocketDeckBoxIndex[pocketIndex];
            var n = inv.deckBoxes?.Length ?? 0;
            var next = current + 1;
            if (next >= n) next = -1;
            InventoryService.TrySetPocketDeck(acc, pocketIndex, next, out _);
        }

        static void ActPackSelected(State st)
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null || st.Pick == PickKind.None)
            {
                Note(st, "Select a container first.");
                return;
            }

            string err = null;
            var ok = st.Pick switch
            {
                PickKind.Box => InventoryService.TryPackStorageBox(acc, st.PickIndex, out err),
                PickKind.Binder => InventoryService.TryPackBinder(acc, st.PickIndex, out err),
                PickKind.Artifact => InventoryService.TryPackArtifactBox(acc, out err),
                _ => false
            };
            if (ok)
            {
                FreeUiKit.PlayConfirm();
                Note(st, "Packed into the case.");
            }
            else
            {
                FreeUiKit.PlayClick();
                Note(st, err ?? "Pack failed");
            }
        }

        static void ActUnpackSelected(State st)
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null || st.Pick == PickKind.None)
            {
                Note(st, "Select a container first.");
                return;
            }

            string err = null;
            var ok = st.Pick switch
            {
                PickKind.Box => InventoryService.TryUnpackStorageBox(acc, st.PickIndex, out err),
                PickKind.Binder => InventoryService.TryUnpackBinder(acc, st.PickIndex, out err),
                PickKind.Artifact => InventoryService.TryUnpackArtifactBox(acc, out err),
                _ => false
            };
            if (ok)
            {
                FreeUiKit.PlayConfirm();
                Note(st, "Sent home.");
                st.Pick = PickKind.None;
                st.PickIndex = -1;
            }
            else
            {
                FreeUiKit.PlayClick();
                Note(st, err ?? "Unpack failed");
            }
        }

        static void ActDeleteDeck(State st)
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null || st.Pick != PickKind.Deck)
            {
                Note(st, "Select a deck box first.");
                return;
            }

            if (InventoryService.TryDeleteDeckBox(acc, st.PickIndex, out var err))
            {
                FreeUiKit.PlayConfirm();
                Note(st, "Deck box deleted. Cards stay in storage.");
                st.Pick = PickKind.None;
                st.PickIndex = -1;
            }
            else
            {
                FreeUiKit.PlayClick();
                Note(st, err ?? "Could not delete.");
            }
        }

        static void OpenArtifactBox(State st, string focusDefId = null)
        {
            if (st.Root != null) st.Root.gameObject.SetActive(false);
            ArtifactBoxScreen.Build(st.ModalHost, () =>
            {
                if (st.Root != null) st.Root.gameObject.SetActive(true);
                st.Refresh?.Invoke();
            }, st.Presentation, focusDefId);
        }

        static void OpenDeckEditor(State st)
        {
            var acc = AppSession.Ensure().Account;
            if (acc == null || st.Pick != PickKind.Deck)
            {
                Note(st, "Select a deck box first.");
                return;
            }

            if (!InventoryService.TrySetActivePlayDeck(acc, st.PickIndex, out var err))
            {
                FreeUiKit.PlayClick();
                Note(st, err ?? "Could not set play deck.");
                return;
            }

            FreeUiKit.PlayConfirm();
            if (st.Root != null) st.Root.gameObject.SetActive(false);
            DeckCollectionScreen.Build(st.ModalHost, () =>
            {
                if (st.Root != null) st.Root.gameObject.SetActive(true);
                st.Refresh?.Invoke();
            }, st.Presentation);
        }

        static void DrawCaseGrid(State st)
        {
            const int maxW = PlayerInventory.BackpackMaxWidth;
            const int maxH = PlayerInventory.BackpackMaxHeight;
            var host = st.GridHost;
            var vm = st.Vm;
            var liveW = vm.BackpackUnlocked ? Mathf.Max(0, vm.PackW) : 0;
            var liveH = vm.BackpackUnlocked ? Mathf.Max(0, vm.PackH) : 0;

            var occupied = new bool[maxW, maxH];
            var items = AppSession.Ensure().Account?.inventory?.backpack?.items;
            if (items != null)
            {
                foreach (var it in items)
                {
                    if (it == null) continue;
                    for (var yy = it.gridY; yy < it.gridY + it.gridH; yy++)
                    for (var xx = it.gridX; xx < it.gridX + it.gridW; xx++)
                    {
                        if (xx < 0 || yy < 0 || xx >= maxW || yy >= maxH) continue;
                        occupied[xx, yy] = true;
                    }
                }
            }

            // Empty + locked cells (1×1). Spanning items draw on top.
            for (var yy = 0; yy < maxH; yy++)
            for (var xx = 0; xx < maxW; xx++)
            {
                var live = xx < liveW && yy < liveH;
                if (live && occupied[xx, yy]) continue;
                PlaceCell(host, xx, yy, 1, 1, maxW, maxH, live
                    ? new Color(0.10f, 0.13f, 0.18f, 0.70f)
                    : new Color(0.05f, 0.05f, 0.07f, 0.55f),
                    live ? null : "locked",
                    live ? null : () => Select(st, PickKind.Locked, 0),
                    null, null);
            }

            if (items == null) return;
            foreach (var it in items)
            {
                if (it == null) continue;
                var pick = it.Kind switch
                {
                    BackpackItemKind.StorageBox => PickKind.Box,
                    BackpackItemKind.Binder => PickKind.Binder,
                    BackpackItemKind.ArtifactDeckBox => PickKind.Artifact,
                    _ => PickKind.None
                };
                var idx = it.refIndex;
                var selected = pick != PickKind.None && st.Pick == pick && st.PickIndex == idx;
                var tint = it.Kind switch
                {
                    BackpackItemKind.StorageBox => new Color(0.42f, 0.32f, 0.10f, selected ? 0.95f : 0.82f),
                    BackpackItemKind.Binder => new Color(0.12f, 0.32f, 0.42f, selected ? 0.95f : 0.82f),
                    BackpackItemKind.ArtifactDeckBox => new Color(0.38f, 0.16f, 0.40f, selected ? 0.95f : 0.82f),
                    BackpackItemKind.SoulCard => new Color(0.55f, 0.62f, 0.72f, selected ? 0.95f : 0.80f),
                    BackpackItemKind.CurrencyDigizeni => new Color(0.36f, 0.30f, 0.08f, 0.88f),
                    BackpackItemKind.CurrencyDuelCoin => new Color(0.28f, 0.26f, 0.10f, 0.88f),
                    BackpackItemKind.CurrencySetEnergy => new Color(0.10f, 0.28f, 0.36f, 0.88f),
                    _ => new Color(0.18f, 0.20f, 0.24f, 0.85f)
                };
                Action tap = pick == PickKind.None
                    ? null
                    : () => Select(st, pick, idx);
                var label = it.Kind == BackpackItemKind.SoulCard
                    ? SoulCaption(it)
                    : (it.gridW >= 2 ? it.label : null);
                PlaceCell(host, it.gridX, it.gridY, it.gridW, it.gridH, maxW, maxH, tint,
                    it.label, tap, SpriteForPackItem(it), label);
            }
        }

        static void PlaceCell(Transform host, int x, int y, int w, int h, int maxW, int maxH,
            Color tint, string name, Action onClick, Sprite icon, string caption)
        {
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            var go = new GameObject(string.IsNullOrEmpty(name) ? $"c{x}_{y}" : name,
                typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = go.GetComponent<RectTransform>();
            var x0 = x / (float)maxW;
            var x1 = (x + w) / (float)maxW;
            var y1 = 1f - y / (float)maxH;
            var y0 = 1f - (y + h) / (float)maxH;
            const float gap = 0.004f;
            rt.anchorMin = new Vector2(x0 + gap, y0 + gap);
            rt.anchorMax = new Vector2(x1 - gap, y1 - gap);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = tint;
            img.raycastTarget = onClick != null;
            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => onClick());
            }

            if (icon != null)
            {
                var ico = new GameObject("I", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(go.transform, false);
                var top = string.IsNullOrEmpty(caption) ? 0.88f : 0.92f;
                var bot = string.IsNullOrEmpty(caption) ? 0.12f : 0.32f;
                FloatingPanel.Place(ico.GetComponent<RectTransform>(), 0.12f, bot, 0.88f, top);
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
            }

            if (string.IsNullOrEmpty(caption)) return;
            var t = FloatingPanel.Body(go.transform, caption, 10);
            FloatingPanel.Place(t.rectTransform, 0.06f, 0.04f, 0.94f, 0.30f);
            t.alignment = TextAnchor.MiddleCenter;
            t.color = DuelystUi.TextCream;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        static Sprite SpriteForPackItem(BackpackItem it) => it.Kind switch
        {
            BackpackItemKind.CurrencyDigizeni => ImagineAssets.IconDigizeni(),
            BackpackItemKind.CurrencyDuelCoin => ImagineAssets.IconDuelCoin(),
            BackpackItemKind.CurrencySetEnergy => ImagineAssets.IconSetEnergy(),
            BackpackItemKind.StorageBox => ImagineAssets.IconBag(),
            BackpackItemKind.Binder => ImagineAssets.IconTome(),
            BackpackItemKind.ArtifactDeckBox => ImagineAssets.IconStory(),
            BackpackItemKind.SoulCard => ImagineAssets.FxSoulDestinyGhost() ?? ImagineAssets.IconSoul(),
            _ => ImagineAssets.IconMenu()
        };

        static string SoulCaption(BackpackItem it)
        {
            if (it.expiresUnix <= 0) return "FROZEN";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var left = Math.Max(0, it.expiresUnix - now);
            var h = left / 3600;
            var m = (left % 3600) / 60;
            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }

        static void AddHeader(Transform host, string text) => HubChrome.ListHead(host, text);

        static void ItemRow(Transform host, Sprite icon, string title, string blurb, bool selected,
            Action onClick, bool ar)
        {
            var line = string.IsNullOrEmpty(blurb) ? title : title + "\n" + blurb;
            var btn = HubChrome.ListRow(host, line, onClick, gold: selected);
            btn.GetComponent<LayoutElement>().minHeight = ar ? 52 : 64;
            if (icon != null)
            {
                var ico = new GameObject("I", typeof(RectTransform), typeof(Image));
                ico.transform.SetParent(btn.transform, false);
                FloatingPanel.Place(ico.GetComponent<RectTransform>(), 0.03f, 0.16f, 0.20f, 0.84f);
                var iimg = ico.GetComponent<Image>();
                iimg.sprite = icon;
                iimg.preserveAspect = true;
                iimg.raycastTarget = false;
                var titleRt = btn.transform.Find("Title") as RectTransform;
                if (titleRt != null)
                    HubChrome.Place(titleRt, 0.22f, 0.10f, 0.96f, 0.90f);
            }
        }
    }
}
