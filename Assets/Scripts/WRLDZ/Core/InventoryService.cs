using System.Collections.Generic;
using System.Text;
using UnityEngine;
using WRLDZ.Data;

namespace WRLDZ.Core
{
    /// <summary>
    /// Pack / carry / view rules for travel inventory.
    /// <list type="bullet">
    /// <item>Deck boxes live in avatar pockets only (never backpack bulk).</item>
    /// <item>Storage boxes, binders, currency holders, artifact box use RE4 backpack grid.</item>
    /// <item>View-anywhere (GO-style); remote card moves need trade-transport charges.</item>
    /// </list>
    /// Home-base 3D placement is deferred.
    /// </summary>
    public static class InventoryService
    {
        public sealed class ViewModel
        {
            public int Level;
            public string TeamName = "";
            public int Digizeni;
            public int DuelCoins;
            public int SetEnergy;

            public bool HasBackpack;
            public bool BackpackUnlocked;
            public int PackTier;
            public int PackW;
            public int PackH;
            public int PackCellsUsed;
            public int PackCellsTotal;
            public string PackSummary = "";

            public int HomeBoxes;
            public int CarriedBoxes;
            public int HomeBoxCards;
            public int HomeBoxCap;
            public int BindersHome;
            public int BindersCarried;
            public int BinderPages;
            public int BinderCards;

            public int OwnedDeckBoxes;
            public int CarrySlots;
            public int PocketCount;
            public string PocketLine = "";
            public string[] PocketLabels = System.Array.Empty<string>();

            public bool ArtifactOwned;
            public bool ArtifactAtHome;
            public int ArtifactCount;
            public int TradeTransportCharges;

            public List<string> PackItemLines = new();
            public List<string> HomeItemLines = new();
            public List<string> CollectionLines = new();
        }

        public static ViewModel BuildView(LocalAccountStore.Account acc)
        {
            var vm = new ViewModel();
            if (acc == null) return vm;
            acc.EnsureProgress();
            acc.EnsureInventory();
            var inv = acc.inventory;
            inv.EnsureValid();
            var p = acc.progress;

            vm.Level = p.level;
            vm.TeamName = KuribohTeamInfo.DisplayName(p.Team);
            vm.Digizeni = p.digizeni;
            vm.DuelCoins = p.duelCoin;
            vm.SetEnergy = p.setEnergy;

            vm.HasBackpack = inv.hasBackpack;
            vm.BackpackUnlocked = inv.backpack != null && inv.backpack.unlocked;
            vm.PackTier = inv.backpack?.capacityTier ?? 0;
            vm.PackW = inv.backpack?.width ?? 0;
            vm.PackH = inv.backpack?.height ?? 0;
            vm.PackCellsTotal = vm.PackW * vm.PackH;
            vm.PackCellsUsed = CountOccupiedCells(inv.backpack);

            vm.HomeBoxes = 0;
            vm.CarriedBoxes = 0;
            if (inv.storageBoxes != null)
            {
                foreach (var b in inv.storageBoxes)
                {
                    if (b == null) continue;
                    if (b.atHome) vm.HomeBoxes++;
                    else vm.CarriedBoxes++;
                }
            }

            vm.HomeBoxCards = inv.TotalStorageUsed();
            vm.HomeBoxCap = inv.TotalStorageCapacity();

            vm.BindersHome = 0;
            vm.BindersCarried = 0;
            vm.BinderPages = 0;
            if (inv.binders != null)
            {
                foreach (var b in inv.binders)
                {
                    if (b == null) continue;
                    vm.BinderPages += b.pageCount;
                    if (b.atHome) vm.BindersHome++;
                    else vm.BindersCarried++;
                }
            }

            vm.BinderCards = inv.TotalBinderOrganizedCards();

            vm.OwnedDeckBoxes = inv.deckBoxes?.Length ?? 0;
            vm.CarrySlots = inv.carryDeckBoxSlots;
            vm.PocketCount = inv.pockets?.pocketCount ?? 1;
            vm.PocketLabels = BuildPocketLabels(inv);
            vm.PocketLine = string.Join(" · ", vm.PocketLabels);

            vm.ArtifactOwned = inv.artifactDeckBox != null && inv.artifactDeckBox.owned;
            vm.ArtifactAtHome = false;
            vm.ArtifactCount = inv.artifactDeckBox?.instances?.Length ?? 0;
            vm.TradeTransportCharges = inv.tradeTransportCharges;

            vm.PackSummary =
                !vm.HasBackpack
                    ? "Backpack locked — story unlock soon."
                    : !vm.BackpackUnlocked
                        ? "Pack locked."
                        : $"Pack {vm.PackW}×{vm.PackH} (T{vm.PackTier}) · {vm.PackCellsUsed}/{vm.PackCellsTotal} cells";

            // Pack item lines
            if (inv.backpack?.items != null)
            {
                foreach (var it in inv.backpack.items)
                {
                    if (it == null) continue;
                    vm.PackItemLines.Add(
                        $"[{it.gridX},{it.gridY}] {it.gridW}×{it.gridH} · {it.label} ({it.Kind})");
                }
            }

            if (inv.storageBoxes != null)
            {
                for (var i = 0; i < inv.storageBoxes.Length; i++)
                {
                    var b = inv.storageBoxes[i];
                    if (b == null) continue;
                    var where = b.atHome ? "HOME" : "PACK";
                    vm.HomeItemLines.Add(
                        $"Box#{i} {b.name} {b.UsedSlots()}/{b.capacity} · {where} · fp {b.Footprint.x}×{b.Footprint.y}");
                }
            }

            if (inv.binders != null)
            {
                for (var i = 0; i < inv.binders.Length; i++)
                {
                    var b = inv.binders[i];
                    if (b == null) continue;
                    var where = b.atHome ? "HOME" : "PACK";
                    vm.HomeItemLines.Add(
                        $"Binder#{i} {b.name} {b.UsedSlots()}/{b.MaxCards} · {b.pageCount}p · {where}");
                }
            }

            // GO-style collection glance (counts only; no remote move without transport)
            var top = TopCardStacks(inv, 12);
            foreach (var line in top)
                vm.CollectionLines.Add(line);

            return vm;
        }

        public static string ToastSummary(LocalAccountStore.Account acc)
        {
            var vm = BuildView(acc);
            var sb = new StringBuilder();
            sb.Append($"Lv{vm.Level} · {vm.TeamName}\n");
            sb.Append($"{vm.PackSummary}\n");
            sb.Append(
                $"Boxes home {vm.HomeBoxes} / pack {vm.CarriedBoxes} · cards {vm.HomeBoxCards}/{vm.HomeBoxCap}\n");
            sb.Append(
                $"Binders H{vm.BindersHome}/P{vm.BindersCarried} · decks carry {vm.CarrySlots} · pockets {vm.PocketLine}\n");
            sb.Append(
                $"Đ{vm.Digizeni} · DC{vm.DuelCoins} · SE{vm.SetEnergy} · transport×{vm.TradeTransportCharges}");
            return sb.ToString();
        }

        // ── Pack / unpack ──────────────────────────────────────────────────

        public static bool TryPackStorageBox(LocalAccountStore.Account acc, int boxIndex, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (!inv.hasBackpack || inv.backpack == null || !inv.backpack.unlocked)
            {
                error = "Backpack not unlocked.";
                return false;
            }

            if (boxIndex < 0 || boxIndex >= inv.storageBoxes.Length)
            {
                error = "Invalid box.";
                return false;
            }

            var box = inv.storageBoxes[boxIndex];
            if (box == null)
            {
                error = "Empty box slot.";
                return false;
            }

            if (!box.atHome)
            {
                error = "Box already in pack.";
                return false;
            }

            var fp = box.Footprint;
            // Temporarily mark as packing candidate and test fit
            inv.SyncBackpackOccupancy();
            if (!PlayerInventory.TryFindFreeCell(inv.backpack, fp.x, fp.y, out _, out _))
            {
                error = $"No pack space for {fp.x}×{fp.y} box.";
                return false;
            }

            box.atHome = false;
            inv.SyncBackpackOccupancy();
            if (box.atHome)
            {
                error = "Could not place box in pack.";
                return false;
            }

            Persist(acc);
            Debug.Log($"[WRLDZ Inv] Packed storage box #{boxIndex} {box.name}");
            return true;
        }

        public static bool TryUnpackStorageBox(LocalAccountStore.Account acc, int boxIndex, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (boxIndex < 0 || boxIndex >= inv.storageBoxes.Length)
            {
                error = "Invalid box.";
                return false;
            }

            var box = inv.storageBoxes[boxIndex];
            if (box == null)
            {
                error = "Empty box slot.";
                return false;
            }

            if (box.atHome)
            {
                error = "Box already at home.";
                return false;
            }

            box.atHome = true;
            inv.SyncBackpackOccupancy();
            Persist(acc);
            Debug.Log($"[WRLDZ Inv] Unpacked storage box #{boxIndex} → home");
            return true;
        }

        public static bool TryPackBinder(LocalAccountStore.Account acc, int binderIndex, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (!inv.hasBackpack || inv.backpack == null || !inv.backpack.unlocked)
            {
                error = "Backpack not unlocked.";
                return false;
            }

            if (binderIndex < 0 || binderIndex >= inv.binders.Length)
            {
                error = "Invalid binder.";
                return false;
            }

            var b = inv.binders[binderIndex];
            if (b == null)
            {
                error = "Empty binder slot.";
                return false;
            }

            if (!b.atHome)
            {
                error = "Binder already in pack.";
                return false;
            }

            inv.SyncBackpackOccupancy();
            var fp = b.Footprint;
            if (!PlayerInventory.TryFindFreeCell(inv.backpack, fp.x, fp.y, out _, out _))
            {
                error = $"No pack space for binder ({fp.x}×{fp.y}).";
                return false;
            }

            b.atHome = false;
            inv.SyncBackpackOccupancy();
            if (b.atHome)
            {
                error = "Could not place binder in pack.";
                return false;
            }

            Persist(acc);
            Debug.Log($"[WRLDZ Inv] Packed binder #{binderIndex}");
            return true;
        }

        public static bool TryUnpackBinder(LocalAccountStore.Account acc, int binderIndex, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (binderIndex < 0 || binderIndex >= inv.binders.Length)
            {
                error = "Invalid binder.";
                return false;
            }

            var b = inv.binders[binderIndex];
            if (b == null)
            {
                error = "Empty binder slot.";
                return false;
            }

            if (b.atHome)
            {
                error = "Binder already at home.";
                return false;
            }

            b.atHome = true;
            inv.SyncBackpackOccupancy();
            Persist(acc);
            return true;
        }

        public static bool TryPackArtifactBox(LocalAccountStore.Account acc, out string error)
        {
            error = "Artifact Deck Box is always with you.";
            return false;
        }

        public static bool TryUnpackArtifactBox(LocalAccountStore.Account acc, out string error)
        {
            error = "Artifact Deck Box is always with you.";
            return false;
        }

        // ── Pockets (play deck boxes only) ─────────────────────────────────

        /// <summary>Assign a owned deck box index into a pocket slot. -1 clears.</summary>
        public static bool TrySetPocketDeck(LocalAccountStore.Account acc, int pocketIndex, int deckBoxIndex,
            out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            inv.pockets.EnsureValid(inv.deckBoxes?.Length ?? 0, inv.carryDeckBoxSlots);
            if (pocketIndex < 0 || pocketIndex >= inv.pockets.pocketDeckBoxIndex.Length)
            {
                error = "Invalid pocket (outfit / carry limit).";
                return false;
            }

            if (deckBoxIndex < -1 || deckBoxIndex >= (inv.deckBoxes?.Length ?? 0))
            {
                error = "Invalid deck box.";
                return false;
            }

            // Deck boxes are pocket-only — never placed in backpack grid.
            if (deckBoxIndex >= 0)
            {
                // Prevent same deck in two pockets
                for (var i = 0; i < inv.pockets.pocketDeckBoxIndex.Length; i++)
                {
                    if (i == pocketIndex) continue;
                    if (inv.pockets.pocketDeckBoxIndex[i] == deckBoxIndex)
                        inv.pockets.pocketDeckBoxIndex[i] = -1;
                }
            }

            inv.pockets.pocketDeckBoxIndex[pocketIndex] = deckBoxIndex;
            Persist(acc);
            return true;
        }

        /// <summary>Set the main play deck (pocket 0). Used by the overworld deck switcher.</summary>
        public static bool TrySetActivePlayDeck(LocalAccountStore.Account acc, int deckBoxIndex,
            out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (!inv.SetActivePlayDeck(deckBoxIndex))
            {
                error = "Could not set main deck.";
                return false;
            }

            Persist(acc);
            return true;
        }

        /// <summary>Set outfit pocket count (≥1). Carry slots may clamp active pockets.</summary>
        public static bool TrySetOutfitPockets(LocalAccountStore.Account acc, int pocketCount, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            inv.pockets.pocketCount = Mathf.Max(1, pocketCount);
            inv.pockets.EnsureValid(inv.deckBoxes?.Length ?? 0, inv.carryDeckBoxSlots);
            Persist(acc);
            return true;
        }

        /// <summary>Story gift: sew one extra pocket onto the equipped shirt (or bottoms).</summary>
        public static bool UnlockThirdDeckCarry(LocalAccountStore.Account acc, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            inv.unlockedThirdDeckCarry = true;
            ClothingService.EnsureWardrobe(acc);
            var shirt = acc.avatar != null ? acc.avatar.shirtId : ClothingCatalog.DefaultShirt;
            var bottoms = acc.avatar != null ? acc.avatar.bottomsId : ClothingCatalog.DefaultBottoms;
            var sewnShirt = inv.wardrobe.SewnExtra(shirt);
            var shirtItem = ClothingCatalog.Get(shirt);
            if (shirtItem != null && sewnShirt < shirtItem.MaxSewn)
                inv.wardrobe.SetSewnExtra(shirt, sewnShirt + 1);
            else
            {
                var sewnB = inv.wardrobe.SewnExtra(bottoms);
                var bItem = ClothingCatalog.Get(bottoms);
                if (bItem != null && sewnB < bItem.MaxSewn)
                    inv.wardrobe.SetSewnExtra(bottoms, sewnB + 1);
            }

            ClothingService.SyncPockets(acc);
            Persist(acc);
            Debug.Log("[WRLDZ Inv] Story pocket sewn onto outfit");
            return true;
        }

        /// <summary>Legacy shop row — carry slots now come from clothing / tailor.</summary>
        public static bool TryBuyExtraDeckCarry(LocalAccountStore.Account acc, out string error)
        {
            error = "On-hand decks come from clothing pockets. Buy a shirt or pants at a bazaar clothier, or visit the tailor to sew extra pockets.";
            return false;
        }

        /// <summary>Grow backpack grid tier (story / seamstress expansion).</summary>
        public static bool SetBackpackTier(LocalAccountStore.Account acc, int tier, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            inv.hasBackpack = true;
            inv.backpack.capacityTier = Mathf.Clamp(tier, 0, PlayerInventory.BackpackMaxTier);
            inv.backpack.unlocked = inv.backpack.capacityTier >= 1;
            inv.backpack.EnsureValid();
            inv.SyncBackpackOccupancy();
            Persist(acc);
            return true;
        }

        /// <summary>Add a constructed play-deck box (pockets / Decks tab — never backpack bulk).</summary>
        public static bool TryCreateDeckBox(LocalAccountStore.Account acc, out int index, out string error)
        {
            index = -1;
            if (!Ready(acc, out var inv, out error)) return false;
            if (!inv.TryAddDeckBox(out index, out error)) return false;
            Persist(acc);
            Debug.Log($"[WRLDZ Inv] Created deck box #{index}");
            return true;
        }

        /// <summary>Delete a constructed play-deck box. Cards stay in storage. Keeps ≥1 box.</summary>
        public static bool TryDeleteDeckBox(LocalAccountStore.Account acc, int index, out string error)
        {
            if (!Ready(acc, out var inv, out error)) return false;
            if (!inv.TryRemoveDeckBox(index, out error)) return false;
            Persist(acc);
            Debug.Log($"[WRLDZ Inv] Deleted deck box #{index}");
            return true;
        }

        // ── Trade transport (GO-style) ─────────────────────────────────────

        public static bool TryBuyTradeTransport(LocalAccountStore.Account acc, int qty, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            qty = Mathf.Max(1, qty);
            var price = PlayerInventory.PriceTradeTransportDigi * qty;
            if (!ArtifactService.TrySpend(acc, ArtifactService.Digizeni, price, out error))
                return false;
            ArtifactService.Grant(acc, ArtifactService.TradeTransport, qty);
            Persist(acc);
            return true;
        }

        /// <summary>
        /// Move one card copy remotely (e.g. into a trade offer) without being at home base.
        /// Consumes a trade-transport charge. View-only never needs this.
        /// </summary>
        public static bool TryConsumeTradeTransport(LocalAccountStore.Account acc, out string error)
        {
            error = null;
            if (!Ready(acc, out var inv, out error)) return false;
            if (!ArtifactService.TrySpend(acc, ArtifactService.TradeTransport, 1, out error))
            {
                error = "Need a Trade Transport item to move a home card remotely.";
                return false;
            }

            Persist(acc);
            return true;
        }

        /// <summary>True if player can currently move a home-owned card into a remote trade.</summary>
        public static bool CanTransportCardForTrade(LocalAccountStore.Account acc) =>
            acc?.inventory != null && acc.inventory.tradeTransportCharges > 0;

        // ── helpers ────────────────────────────────────────────────────────

        static bool Ready(LocalAccountStore.Account acc, out PlayerInventory inv, out string error)
        {
            inv = null;
            error = null;
            if (acc == null)
            {
                error = "No account.";
                return false;
            }

            acc.EnsureProgress();
            acc.EnsureInventory();
            inv = acc.inventory;
            inv.EnsureValid();
            ClothingService.EnsureWardrobe(acc);
            return true;
        }

        static void Persist(LocalAccountStore.Account acc)
        {
            ProgressionService.Persist(acc);
            AppSession.Ensure()?.RefreshFromStore();
        }

        static int CountOccupiedCells(BackpackState pack)
        {
            if (pack?.items == null) return 0;
            var n = 0;
            foreach (var it in pack.items)
            {
                if (it == null) continue;
                n += Mathf.Max(1, it.gridW) * Mathf.Max(1, it.gridH);
            }

            return n;
        }

        static string[] BuildPocketLabels(PlayerInventory inv)
        {
            inv.pockets ??= new AvatarPocketState();
            inv.pockets.EnsureValid(inv.deckBoxes?.Length ?? 0, inv.carryDeckBoxSlots);
            var labels = new string[inv.pockets.pocketDeckBoxIndex.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var di = inv.pockets.pocketDeckBoxIndex[i];
                if (di < 0 || inv.deckBoxes == null || di >= inv.deckBoxes.Length)
                {
                    labels[i] = i == 0 ? "P0:empty" : $"P{i}:—";
                    continue;
                }

                var name = inv.deckBoxes[di]?.name;
                if (string.IsNullOrEmpty(name)) name = "Deck " + di;
                labels[i] = $"P{i}:{name}";
            }

            return labels;
        }

        static List<string> TopCardStacks(PlayerInventory inv, int maxLines)
        {
            var map = new Dictionary<int, int>();
            if (inv.storageBoxes != null)
            {
                foreach (var box in inv.storageBoxes)
                {
                    if (box?.stacks == null) continue;
                    foreach (var s in box.stacks)
                    {
                        if (s == null || s.cardId <= 0 || s.qty <= 0) continue;
                        map.TryGetValue(s.cardId, out var q);
                        map[s.cardId] = q + s.qty;
                    }
                }
            }

            var list = new List<KeyValuePair<int, int>>(map);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            var lines = new List<string>();
            var db = CardDatabase.Load();
            for (var i = 0; i < list.Count && i < maxLines; i++)
            {
                var id = list[i].Key;
                var name = db?.Get(id)?.name ?? ("#" + id);
                lines.Add($"×{list[i].Value}  {name}");
            }

            if (lines.Count == 0)
                lines.Add("(no cards in storage yet)");
            return lines;
        }
    }
}
