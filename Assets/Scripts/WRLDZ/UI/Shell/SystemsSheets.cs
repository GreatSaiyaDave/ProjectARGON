using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
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
                modalHost, "STORY", "Season 1 · Duelist Kingdom", onClose, force);
            var (list, status) = ListHost(frame.BodyHost);

            void AnswerQuiz(PlayerProgress progress, LocalAccountStore.Account account, Text statusText,
                Action refresh, bool kuribohs)
            {
                if (ShellLoops.TryAnswerQuiz(progress, account?.inventory, kuribohs, out var err))
                {
                    FreeUiKit.PlayConfirm();
                    ProgressionService.Persist(account);
                    refresh();
                }
                else
                {
                    FreeUiKit.PlayClick();
                    statusText.text = err ?? "No quiz.";
                }
            }

            void ClaimQuest(PlayerProgress progress, LocalAccountStore.Account account, Text statusText,
                Action refresh, string id)
            {
                if (QuestService.TryClaim(progress, account?.inventory, id, out var err))
                {
                    FreeUiKit.PlayConfirm();
                    ProgressionService.Persist(account);
                    refresh();
                    statusText.text = "Claimed  ·  " + QuestService.RewardLine(id);
                }
                else
                {
                    FreeUiKit.PlayClick();
                    statusText.text = err ?? "Claim failed";
                }
            }

            void QuestRow(Transform host, PlayerProgress progress, LocalAccountStore.Account account,
                Text statusText, Action refresh, QuestSlot slot)
            {
                if (slot == null) return;
                var done = slot.progress >= slot.goal;
                var label = slot.title + "  ·  " + slot.progress + "/" + slot.goal
                            + (slot.claimed ? "  ·  claimed"
                                : done ? "  ·  tap to claim  ·  " + QuestService.RewardLine(slot.id) : "");
                var id = slot.id;
                Row(host, label, slot.claimed || !done
                    ? null
                    : () => ClaimQuest(progress, account, statusText, refresh, id));
            }

            void Refresh()
            {
                FloatingPanel.DestroyChildrenNow(list);
                var acc = AppSession.Ensure().Account;
                acc?.EnsureProgress();
                acc?.EnsureInventory();
                var p = acc?.progress;
                StoryCampaignService.Ensure(p);
                var catalog = StoryCampaignService.Load();
                var current = StoryCampaignService.Current(p);
                status.text = current != null
                    ? $"Lv{p?.level ?? 1} · next {current.opponentName}"
                    : $"Lv{p?.level ?? 1} · Season 1 complete";

                var quests = QuestService.Ensure(p);
                RefereeHead(list);
                Head(list, "TODAY");
                if (quests?.slots != null)
                {
                    for (var i = 0; i < quests.slots.Length; i++)
                        QuestRow(list, p, acc, status, Refresh, quests.slots[i]);
                }

                Head(list, "THIS WEEK");
                if (quests != null)
                {
                    var weekDone = quests.weeklyProgress >= QuestService.WeeklyGoal;
                    var weekLabel = "Win 3 duels  ·  " + quests.weeklyProgress + "/" + QuestService.WeeklyGoal
                                    + (quests.weeklyClaimed ? "  ·  claimed"
                                        : weekDone ? "  ·  tap to claim  ·  " + QuestService.RewardLine(QuestService.WeeklyWins)
                                        : "");
                    Row(list, weekLabel, quests.weeklyClaimed || !weekDone
                        ? null
                        : () => ClaimQuest(p, acc, status, Refresh, QuestService.WeeklyWins));
                }

                Head(list, "BREAKTHROUGH");
                if (quests != null)
                {
                    var ready = quests.stamps >= QuestService.StampsToBreak;
                    var stampLabel = "Referobot stamps  ·  " + quests.stamps + "/" + QuestService.StampsToBreak
                                     + (ready ? "  ·  tap to claim  ·  " + QuestService.RewardLine(QuestService.Breakthrough) : "");
                    Row(list, stampLabel, ready
                        ? () => ClaimQuest(p, acc, status, Refresh, QuestService.Breakthrough)
                        : null);
                }

                Head(list, "NEXT PERK");
                Row(list, QuestService.NextPerkLine(p?.level ?? 1), null);

                Head(list, "KURIBOH BOND");
                Row(list, $"Bond  {p?.kuribohBond ?? 0}/10  ·  {p?.Team}.  A won story duel adds one. No extra prize.",
                    null);

                Head(list, "REFEROBOT QUIZ");
                if (p != null && ShellLoops.QuizReady(p))
                {
                    Row(list, "Who woke Mr. Referobot?", null);
                    Row(list, "The three Kuribohs", () => AnswerQuiz(p, acc, status, Refresh, true));
                    Row(list, "KaibaCorp alone", () => AnswerQuiz(p, acc, status, Refresh, false));
                }
                else
                {
                    Row(list, p != null && p.level >= 10
                        ? $"Answered through level {p.quizClaimedThrough}. Next gate is later."
                        : "Opens at level 10.", null);
                }

                var gates = StoryCampaignService.Gates();
                for (var g = 0; g < gates.Count; g++)
                {
                    var gate = gates[g];
                    if (gate == null) continue;
                    Head(list, gate.title.ToUpperInvariant());
                    var ids = StoryCampaignService.StageIdsForGate(gate);
                    for (var s = 0; s < ids.Length; s++)
                    {
                        var stage = StoryCampaignService.Stage(ids[s]);
                        if (stage == null) continue;
                        var cleared = StoryCampaignService.IsCleared(p, stage.id);
                        var locked = StoryCampaignService.IsLocked(p, stage.id);
                        var pip = string.IsNullOrEmpty(stage.erazBandId)
                            ? catalog.erazBandId
                            : stage.erazBandId;
                        string mark;
                        if (cleared) mark = "CLEARED";
                        else if (locked) mark = "LOCKED";
                        else mark = "OPEN";
                        var line = stage.opponentName + "  ·  " + stage.startingLp + " LP  ·  " + pip
                                   + "  ·  " + mark;
                        if (!cleared && !locked)
                            line += "\n" + stage.blurb;
                        var playable = !locked;
                        var stageId = stage.id;
                        Row(list, line, playable
                            ? () =>
                            {
                                var st = StoryCampaignService.Stage(stageId);
                                if (st == null || !StoryCampaignService.CanPlay(p, st.id))
                                {
                                    FreeUiKit.PlayClick();
                                    return;
                                }

                                FreeUiKit.PlayConfirm();
                                var cfg = MapZoneService.MakeStoryEra(st, st.id, digital: true);
                                cfg.PreferDigital = true;
                                onClose?.Invoke();
                                AppSession.Ensure().StartArDuel(cfg);
                            }
                            : null);
                    }
                }
            }

            Refresh();
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

                Head(list, "WIN STREAK");
                Row(list, $"Non-practice wins  {p?.pveWinStreak ?? 0}/3.  Purses banked  {p?.streakPurses ?? 0}.  "
                          + "The pack is not minted yet — a purse is 150 Digizeni.", null);
                if (p != null && p.streakPurses > 0)
                {
                    Row(list, "Claim streak purse", () =>
                    {
                        if (ShellLoops.TryClaimPurse(p, acc.inventory, out var err))
                        {
                            FreeUiKit.PlayConfirm();
                            ProgressionService.Persist(acc);
                            Refresh();
                        }
                        else
                        {
                            FreeUiKit.PlayClick();
                            status.text = err ?? "No purse.";
                        }
                    });
                }

                Head(list, "ALTAR");
                Row(list, "Spare cards become Set Energy of that card's set. Conversion is not live. Cards stay put.",
                    () =>
                    {
                        FreeUiKit.PlayClick();
                        status.text = "Altar is marked. Conversion is not live — your cards stay put.";
                    });

                Head(list, "TABLETS");
                Row(list, "1,000 SE of a set → 10 cards of that set. Unlocked ERAZ only.", null);
                FillTablets(list, acc, Refresh, status);

                Head(list, "ERAZ BADGE FUSION");
                Row(list, "5 shards + 2,500 SE of an unlocked era fuse the next badge.", null);
                FillErazMerge(list, acc, Refresh, status);

                Head(list, "FORMAT BADGE FUSION");
                Row(list, "Same fuse. Unlocks the format row. Table laws stay off until that format is finalized.", null);
                FillFormatMerge(list, acc, Refresh, status);
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

        static void FillTablets(Transform list, LocalAccountStore.Account acc, Action refresh, Text status)
        {
            var p = acc?.progress;
            var inv = acc?.inventory;
            var codes = new[] { "LOB", "MRD", "SRL" };
            for (var i = 0; i < codes.Length; i++)
            {
                var set = codes[i];
                var have = ArtifactService.SetEnergyOf(inv, set);
                var legal = ArtifactService.CanEarnSetEnergy(p, set);
                var label = legal
                    ? $"{set}  ·  {have} SE  ·  tap to offer {StoneTabletService.PackCost}"
                    : $"{set}  ·  locked (need ERAZ badge)";
                var setId = set;
                Row(list, label, !legal
                    ? null
                    : () =>
                    {
                        var a = AppSession.Ensure().Account;
                        a?.EnsureProgress();
                        a?.EnsureInventory();
                        if (StoneTabletService.TryOpen(a?.progress, a?.inventory, setId, out _, out var err))
                        {
                            FreeUiKit.PlayConfirm();
                            ProgressionService.Persist(a);
                            refresh?.Invoke();
                        }
                        else
                        {
                            FreeUiKit.PlayClick();
                            status.text = err ?? "Tablet refused";
                        }
                    });
            }
        }

        static void FillErazMerge(Transform list, LocalAccountStore.Account acc, Action refresh, Text status)
        {
            var p = acc?.progress;
            var inv = acc?.inventory;
            var next = ErazProgress.NextPieceBand(p);
            if (string.IsNullOrEmpty(next) || string.Equals(next, ErazFormat.Original, StringComparison.OrdinalIgnoreCase))
            {
                Row(list, "Original badge is whole. Clear Season 1 for GX shards.", null);
                return;
            }

            var have = ErazMergeService.PieceCount(inv, next);
            var se = ArtifactService.SetEnergyTotal(inv);
            var label = $"{next.ToUpperInvariant()}  ·  shards {have}/{ErazMergeService.PiecesRequired}  ·  SE {se}/{ErazMergeService.SetEnergyCost}  ·  tap to fuse";
            Row(list, label, () =>
            {
                var a = AppSession.Ensure().Account;
                a?.EnsureProgress();
                a?.EnsureInventory();
                if (ErazMergeService.TryMerge(a?.progress, a?.inventory, next, out var err))
                {
                    FreeUiKit.PlayConfirm();
                    ProgressionService.Persist(a);
                    refresh?.Invoke();
                }
                else
                {
                    FreeUiKit.PlayClick();
                    status.text = err ?? "Fuse failed";
                }
            });
        }

        static void FillFormatMerge(Transform list, LocalAccountStore.Account acc, Action refresh, Text status)
        {
            var p = acc?.progress;
            var inv = acc?.inventory;
            for (var i = 0; i < FormatProgress.Ids.Length; i++)
            {
                var id = FormatProgress.Ids[i];
                var title = FormatProgress.Titles[i];
                if (FormatProgress.HasBadge(p, id))
                {
                    Row(list, title + "  ·  whole", null);
                    continue;
                }

                var have = FormatMergeService.PieceCount(inv, id);
                var se = ArtifactService.SetEnergyTotal(inv);
                var fid = id;
                Row(list,
                    $"{title}  ·  shards {have}/{FormatMergeService.PiecesRequired}  ·  SE {se}/{FormatMergeService.SetEnergyCost}  ·  tap to fuse",
                    () =>
                    {
                        var a = AppSession.Ensure().Account;
                        a?.EnsureProgress();
                        a?.EnsureInventory();
                        if (FormatMergeService.TryMerge(a?.progress, a?.inventory, fid, out var err))
                        {
                            FreeUiKit.PlayConfirm();
                            ProgressionService.Persist(a);
                            refresh?.Invoke();
                        }
                        else
                        {
                            FreeUiKit.PlayClick();
                            status.text = err ?? "Fuse failed";
                        }
                    });
            }
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

        static void RefereeHead(Transform host)
        {
            var row = new GameObject("Referobot", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            row.transform.SetParent(host, false);
            var le = row.GetComponent<LayoutElement>();
            le.minHeight = 108f;
            le.preferredHeight = 108f;
            var plate = row.GetComponent<Image>();
            plate.raycastTarget = false;
            HubChrome.QuietFill(plate, new Color(0.04f, 0.06f, 0.10f, 0.72f), null, true);

            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(row.transform, false);
            var fr = face.GetComponent<RectTransform>();
            fr.anchorMin = new Vector2(0.02f, 0.08f);
            fr.anchorMax = new Vector2(0.28f, 0.92f);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            var img = face.GetComponent<Image>();
            img.sprite = StreamingSprite.ReferobotPortrait() ?? UiFoundation.WhiteSprite();
            img.preserveAspect = true;
            img.raycastTarget = false;

            var name = FloatingPanel.Body(row.transform, "MR. REFEROBOT", 16);
            name.color = DuelystUi.GoldHot;
            name.alignment = TextAnchor.MiddleLeft;
            FloatingPanel.Place(name.rectTransform, 0.30f, 0.52f, 0.98f, 0.92f);
            var sub = FloatingPanel.Caption(row.transform, "Referee  ·  dailies  ·  quiz", 13);
            sub.alignment = TextAnchor.UpperLeft;
            FloatingPanel.Place(sub.rectTransform, 0.30f, 0.10f, 0.98f, 0.50f);
        }

        static void Head(Transform host, string text) => HubChrome.ListHead(host, text);

        static void Row(Transform host, string text, Action onClick) =>
            HubChrome.ListRow(host, text, onClick);

        static (Transform list, Text status) ListHost(Transform body)
        {
            var status = FloatingPanel.Body(body, "", 13);
            FloatingPanel.Place(status.rectTransform, 0.02f, 0.01f, 0.98f, 0.10f);
            status.alignment = TextAnchor.MiddleLeft;
            status.color = DuelystUi.Cyan;
            // P4 (the P3 landing: BAZAAR kept the big cyan currency line): in the v6.3 sheets the status line is a ledger
            // strip — the strip cel behind it, the ledger's row type (Cairo, the label ochre-cream), sized as its rows
            if (WRLDZ.UI.MenuV6.SanctuaryPaint.On(body))
                WRLDZ.UI.MenuV6.SanctuaryPaint.StatusStrip(status);

            var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.01f, 0.12f, 0.99f, 0.99f);
            var bg = scroll.GetComponent<Image>();
            bg.raycastTarget = true;
            HubChrome.PaintWell(bg);

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
