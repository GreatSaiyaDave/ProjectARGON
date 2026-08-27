using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Presentation;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Real sheets for Tome / Story / Bazaar / Trade — same DualMenu chrome as backpack.
    /// Replaces the leftover stub panel that painted VS AI / VS PVP on every destination.
    /// </summary>
    public static class SystemsSheets
    {
        public static RectTransform BuildTome(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            var acc = AppSession.Ensure().Account;
            if (acc != null) TomeService.SyncCapacityUnlocks(acc);

            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "TOME", "Raid + Tear boss · 1 page / 10 levels", onClose, force);
            var db = CardDatabase.Load();
            var (list, status) = ListHost(frame.BodyHost);

            void Refresh()
            {
                FloatingPanel.DestroyChildrenNow(list);
                acc = AppSession.Ensure().Account;
                if (acc == null)
                {
                    status.text = "No account.";
                    return;
                }

                TomeService.SyncCapacityUnlocks(acc);
                acc.EnsureInventory();
                var cap = TomeService.Capacity(acc);
                var unlocked = acc.inventory.tome.unlockedPageIds ?? Array.Empty<int>();
                var equipped = acc.inventory.tome.equippedPageIds ?? Array.Empty<int>();

                status.text = cap <= 0
                    ? "Reach Lv10 to unlock the first page."
                    : $"Capacity {cap} · unlocked {unlocked.Length} · equipped {equipped.Length}";

                Head(list, "EQUIPPED (raid only)");
                if (equipped.Length == 0)
                    Row(list, "(empty)", null);
                else
                {
                    foreach (var id in equipped)
                    {
                        var cardId = id;
                        Row(list, PageLabel(db, cardId) + "  ·  tap to unequip", () =>
                        {
                            TomeService.TryUnequipPage(acc, cardId);
                            FreeUiKit.PlayClick();
                            Refresh();
                        });
                    }
                }

                Head(list, "UNLOCKED PAGES");
                if (unlocked.Length == 0)
                    Row(list, cap <= 0 ? "Level gates: 10 / 20 / 30" : "(none yet)", null);
                else
                {
                    foreach (var id in unlocked)
                    {
                        var cardId = id;
                        var on = Array.IndexOf(equipped, cardId) >= 0;
                        Row(list, PageLabel(db, cardId) + (on ? "  ·  equipped" : "  ·  tap to equip"), () =>
                        {
                            if (on) TomeService.TryUnequipPage(acc, cardId);
                            else if (!TomeService.TryEquipPage(acc, cardId))
                                status.text = "Tome full — unequip a page first.";
                            else
                                FreeUiKit.PlayConfirm();
                            Refresh();
                        });
                    }
                }
            }

            Refresh();
            return frame.Root;
        }

        public static RectTransform BuildStory(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "STORY ZONE", "Era missions on the map", onClose, force);
            var (list, status) = ListHost(frame.BodyHost);
            var acc = AppSession.Ensure().Account;
            acc?.EnsureProgress();
            var p = acc?.progress;
            var lvl = p?.level ?? 1;
            SetOrbService.Ensure(p);
            var next = SetOrbService.NextLockedSet(p);
            var orbLine = string.IsNullOrEmpty(next)
                ? "all L50 sets unlocked"
                : $"{next} {SetOrbService.OrbCount(p, next)}/{SetOrbService.OrbsPerUnlock}";
            status.text = $"Lv{lvl} · sets {p?.unlockedSetsCsv} · {orbLine}";

            Head(list, "SET ORBS");
            Row(list, "Starter unlocked: LOB · MRD · SRL", null);
            Row(list,
                "Orbs drop from Tear harvests, street NPC wins, and PvP wins through L50. " +
                $"{SetOrbService.OrbsPerUnlock} orbs unlock the next set.", null);
            if (!string.IsNullOrEmpty(next))
                Row(list, $"Next set  {next}  ·  {SetOrbService.OrbCount(p, next)}/{SetOrbService.OrbsPerUnlock}", null);
            else
                Row(list, "Later sets follow story / era cadence after L50.", null);

            Head(list, "STORY ZONE");
            Chapter(list, "Story Zone", "Open", true, "Walk a Story Zone pin for the era mission.");
            return frame.Root;
        }

        public static RectTransform BuildBazaar(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "BAZAAR", "Clothier · tailor · tablets · storage", onClose, force);
            var (list, status) = ListHost(frame.BodyHost);

            void Refresh()
            {
                FloatingPanel.DestroyChildrenNow(list);
                var acc = AppSession.Ensure().Account;
                acc?.EnsureProgress();
                acc?.EnsureInventory();
                if (acc != null) ClothingService.EnsureWardrobe(acc);
                var p = acc?.progress;
                var se = p?.setEnergy ?? 0;
                var digi = p?.digizeni ?? 0;
                var dc = p?.duelCoin ?? 0;
                var pockets = acc != null
                    ? ClothingCatalog.ComputeOutfitPockets(acc.avatar, acc.inventory?.wardrobe)
                    : 1;
                status.text = $"Đ{digi}  ·  DC{dc}  ·  SE{se}  ·  {pockets} on-hand deck{(pockets == 1 ? "" : "s")}";

                Head(list, "CLOTHIER NPC");
                Row(list, "Buy shirts and pants with extra deck pockets. Equip from Customize.", null);
                FillClothier(list, acc, Refresh, status);

                Head(list, "TAILOR NPC");
                Row(list, "Sew +1 pocket onto a shirt or bottoms you own. Price rises per extra stitch.", null);
                FillTailor(list, acc, Refresh, status);

                Head(list, "CURRENCIES");
                Row(list, $"Digizeni  Đ{digi}", null);
                Row(list, $"Duel Coins  {dc}", null);
                Row(list, $"Set Energy  {se}  ·  offer at tablets for 10-card packs", null);

                Head(list, "HOME STORAGE");
                Shop(list, "Buy box 100",
                    a => InventoryShopService.TryBuyStorageBox(a, 100, out var e) ? null : e, Refresh, status);
                Shop(list, "Buy box 500",
                    a => InventoryShopService.TryBuyStorageBox(a, 500, out var e) ? null : e, Refresh, status);
                Shop(list, "Buy box 1000",
                    a => InventoryShopService.TryBuyStorageBox(a, 1000, out var e) ? null : e, Refresh, status);
                Shop(list, "Buy binder",
                    a => InventoryShopService.TryBuyBinder(a, out var e) ? null : e, Refresh, status);

                Head(list, "TABLETS");
                Row(list, "Walk a bazaar pin on the map to offer Set Energy.", null);
            }

            Refresh();
            return frame.Root;
        }

        static void FillClothier(Transform list, LocalAccountStore.Account acc, Action refresh, Text status)
        {
            ClothingSlot? last = null;
            for (var i = 0; i < ClothingCatalog.All.Length; i++)
            {
                var item = ClothingCatalog.All[i];
                if (item.EmptySlot) continue;
                if (last != item.Slot)
                {
                    Row(list, ClothingCatalog.SlotLabel(item.Slot).ToUpperInvariant(), null);
                    last = item.Slot;
                }

                var owned = item.StarterOwned || (acc?.inventory?.wardrobe != null && acc.inventory.wardrobe.Owns(item.Id));
                var extra = acc?.inventory?.wardrobe != null ? acc.inventory.wardrobe.SewnExtra(item.Id) : 0;
                var pockets = item.BasePockets + extra;
                var pocketBit = item.CanHoldPockets
                    ? $"  ·  {pockets} pocket{(pockets == 1 ? "" : "s")}"
                    : "";
                var equipped = acc?.avatar != null && ClothingService.IsEquipped(acc.avatar, item.Id);
                string label;
                if (!owned)
                    label = $"{item.Name}{pocketBit}  ·  Đ{item.PriceDigi}  ·  tap to buy & equip";
                else if (equipped)
                    label = $"{item.Name}{pocketBit}  ·  wearing";
                else
                    label = $"{item.Name}{pocketBit}  ·  owned  ·  tap to equip";

                var id = item.Id;
                Row(list, label, () =>
                {
                    var a = AppSession.Ensure().Account;
                    if (!owned)
                    {
                        if (ClothingService.TryBuyAndEquip(a, id, out var err))
                        {
                            FreeUiKit.PlayConfirm();
                            refresh?.Invoke();
                        }
                        else
                        {
                            FreeUiKit.PlayClick();
                            status.text = err ?? "Buy failed";
                        }
                    }
                    else if (ClothingService.TryEquip(a, id, out var err2))
                    {
                        FreeUiKit.PlayConfirm();
                        refresh?.Invoke();
                    }
                    else
                    {
                        FreeUiKit.PlayClick();
                        status.text = err2 ?? "Cannot equip";
                    }
                });
            }
        }

        static void FillTailor(Transform list, LocalAccountStore.Account acc, Action refresh, Text status)
        {
            var wardrobe = acc?.inventory?.wardrobe;
            var any = false;
            foreach (var item in ClothingService.SewTargets(wardrobe))
            {
                any = true;
                var extra = wardrobe != null ? wardrobe.SewnExtra(item.Id) : 0;
                var now = item.BasePockets + extra;
                var price = ClothingService.SewPrice(extra);
                var cap = extra >= item.MaxSewn;
                var id = item.Id;
                var label = cap
                    ? $"{item.Name}  ·  {now} pockets  ·  max stitches"
                    : $"{item.Name}  ·  {now}→{now + 1} pockets  ·  sew Đ{price}";
                Row(list, label, cap
                    ? null
                    : () =>
                    {
                        var a = AppSession.Ensure().Account;
                        if (ClothingService.TrySewPocket(a, id, out var err))
                        {
                            FreeUiKit.PlayConfirm();
                            refresh?.Invoke();
                        }
                        else
                        {
                            FreeUiKit.PlayClick();
                            status.text = err ?? "Sew failed";
                        }
                    });
            }

            if (!any)
                Row(list, "Bring a shirt or bottoms.", null);
        }

        public static RectTransform BuildTrade(Transform modalHost, Action onClose, UiPresentation? force = null)
        {
            var frame = DualMenuPresenter.BuildFrame(
                modalHost, "TRADE", "Remote move needs transport", onClose, force);
            var (list, status) = ListHost(frame.BodyHost);

            void Refresh()
            {
                FloatingPanel.DestroyChildrenNow(list);
                var acc = AppSession.Ensure().Account;
                var vm = InventoryService.BuildView(acc);
                status.text = $"Transport ×{vm.TradeTransportCharges}  ·  no free teleport";
                Head(list, "TRANSPORT");
                Row(list, $"Charges on hand: {vm.TradeTransportCharges}", null);
                Row(list, "Buy +1 Trade Transport", () =>
                {
                    if (InventoryService.TryBuyTradeTransport(acc, 1, out var err))
                    {
                        FreeUiKit.PlayConfirm();
                        Refresh();
                    }
                    else
                    {
                        FreeUiKit.PlayClick();
                        status.text = err ?? "Buy failed";
                    }
                });
                Head(list, "RULES");
                Row(list, "Cards stay at home unless packed or transported.", null);
            }

            Refresh();
            return frame.Root;
        }

        static string PageLabel(CardDatabase db, int id)
        {
            var def = db?.Get(id);
            return def != null ? def.name : "#" + id;
        }

        static void Shop(Transform list, string label, Func<LocalAccountStore.Account, string> act,
            Action refresh, Text status)
        {
            Row(list, label, () =>
            {
                var acc = AppSession.Ensure().Account;
                var err = act(acc);
                if (err == null)
                {
                    FreeUiKit.PlayConfirm();
                    refresh?.Invoke();
                }
                else
                {
                    FreeUiKit.PlayClick();
                    status.text = err;
                }
            });
        }

        static void Chapter(Transform body, string title, string gate, bool open, string blurb)
        {
            Row(body, $"{title}  ·  {gate}\n{blurb}", null);
            var last = body.childCount > 0 ? body.GetChild(body.childCount - 1) : null;
            if (last == null) return;
            var t = last.GetComponentInChildren<Text>();
            if (t != null && !open) t.color = DuelystUi.TextMuted;
        }

        static void Head(Transform host, string text)
        {
            var go = new GameObject("H", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = 28;
            var t = FloatingPanel.Body(go.transform, text, 13);
            FloatingPanel.Place(t.rectTransform, 0.02f, 0.1f, 0.98f, 0.9f);
            t.color = DuelystUi.GoldHot;
            t.alignment = TextAnchor.MiddleLeft;
        }

        static void Row(Transform host, string text, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().minHeight = text != null && text.IndexOf('\n') >= 0 ? 68 : 48;
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

        static (Transform list, Text status) ListHost(Transform body)
        {
            var status = FloatingPanel.Body(body, "", 13);
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
            return (content.transform, status);
        }
    }
}
