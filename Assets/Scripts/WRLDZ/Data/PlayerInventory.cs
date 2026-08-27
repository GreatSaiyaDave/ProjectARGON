using System;
using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Data
{
    [Serializable]
    public class CardStackEntry
    {
        public int cardId;
        public int qty;
    }

    /// <summary>
    /// Playable constructed deck (TCG Main / Extra / Side) — what goes into the Spirit Dueler.
    /// Not the same as bulk cardboard storage boxes.
    /// </summary>
    [Serializable]
    public class DeckBoxState
    {
        public string name = "Main Deck Box";
        public int[] main = Array.Empty<int>();
        public int[] extra = Array.Empty<int>();
        public int[] side = Array.Empty<int>();
        public bool occupied = true;
        /// <summary>Deck switcher emblem id (beatdown, burn, attr_dark, type_dragon, …).</summary>
        public string iconId = "";
    }

    /// <summary>
    /// Home bulk storage — cardboard sleeve box for loose C/U/R collection.
    /// Sizes: 100 / 500 / 1000 (default starter is 1000).
    /// When <see cref="atHome"/> is false, the physical box is packed into the backpack grid.
    /// </summary>
    [Serializable]
    public class StorageBoxState
    {
        public string name = "Card Box";
        /// <summary>100, 500, or 1000.</summary>
        public int capacity = PlayerInventory.StorageBoxSizeDefault;
        public CardStackEntry[] stacks = Array.Empty<CardStackEntry>();
        /// <summary>True = sits at home base; false = carried in backpack (RE4-style footprint).</summary>
        public bool atHome = true;
        /// <summary>Unique decorative skin id (1 of each per account when collectible).</summary>
        public string decorativeId = "";

        public int UsedSlots()
        {
            var n = 0;
            if (stacks == null) return 0;
            foreach (var s in stacks)
                if (s != null) n += Mathf.Max(0, s.qty);
            return n;
        }

        public int FreeSlots() => Mathf.Max(0, capacity - UsedSlots());

        /// <summary>Backpack grid footprint (RE4-style) for this box size.</summary>
        public Vector2Int Footprint => PlayerInventory.FootprintForStorageCapacity(capacity);

        public void EnsureValid()
        {
            if (capacity != 100 && capacity != 500 && capacity != 1000)
                capacity = PlayerInventory.StorageBoxSizeDefault;
            stacks ??= Array.Empty<CardStackEntry>();
            if (string.IsNullOrEmpty(name)) name = "Card Box (" + capacity + ")";
        }
    }

    /// <summary>
    /// Binder for organized display: 3×3 per side × double-sided = 18 cards/page.
    /// Free: 5 pages. Max: 20 pages → 360 cards. Extra pages via in-game currency.
    /// </summary>
    [Serializable]
    public class BinderState
    {
        public string name = "Card Binder";
        /// <summary>Unlocked page count (5 free … 20 max).</summary>
        public int pageCount = PlayerInventory.BinderFreePages;
        /// <summary>
        /// Slot contents: length = pageCount × CardsPerPage.
        /// Index = page * 18 + slot (0–17). 0 = empty slot.
        /// Front 0–8 (3×3), back 9–17 (3×3).
        /// </summary>
        public int[] slots = Array.Empty<int>();
        /// <summary>True at home; false packed into backpack.</summary>
        public bool atHome = true;

        public int MaxCards => pageCount * PlayerInventory.CardsPerBinderPage;

        public int UsedSlots()
        {
            if (slots == null) return 0;
            var n = 0;
            foreach (var id in slots)
                if (id > 0) n++;
            return n;
        }

        public Vector2Int Footprint => new(2, 2); // denser than bulk boxes

        public void EnsureValid()
        {
            pageCount = Mathf.Clamp(pageCount, PlayerInventory.BinderFreePages,
                PlayerInventory.BinderMaxPages);
            var need = MaxCards;
            if (slots == null || slots.Length != need)
            {
                var next = new int[need];
                if (slots != null)
                    Array.Copy(slots, next, Mathf.Min(slots.Length, need));
                slots = next;
            }

            if (string.IsNullOrEmpty(name)) name = "Card Binder";
        }
    }

    /// <summary>What sits in one backpack grid cell occupancy record.</summary>
    public enum BackpackItemKind
    {
        Empty = 0,
        /// <summary>1×1 Digizeni pouch.</summary>
        CurrencyDigizeni = 1,
        /// <summary>1×1 Duel Coin pouch.</summary>
        CurrencyDuelCoin = 2,
        /// <summary>1×1 Set Energy cell.</summary>
        CurrencySetEnergy = 3,
        /// <summary>Reference to <see cref="PlayerInventory.storageBoxes"/>[refIndex].</summary>
        StorageBox = 10,
        /// <summary>Reference to <see cref="PlayerInventory.binders"/>[refIndex].</summary>
        Binder = 11,
        /// <summary>Magical deck box for artifact cards only.</summary>
        ArtifactDeckBox = 20
    }

    /// <summary>One item occupying a rectangle in the backpack (RE4-style).</summary>
    [Serializable]
    public class BackpackItem
    {
        public int kind; // BackpackItemKind
        public int refIndex = -1;
        public int gridX;
        public int gridY;
        public int gridW = 1;
        public int gridH = 1;
        public string label = "";

        public BackpackItemKind Kind
        {
            get => (BackpackItemKind)kind;
            set => kind = (int)value;
        }
    }

    /// <summary>
    /// RE4-style backpack grid. Story unlocks grow the case; seamstress
    /// expansions at the bazaar sell the remaining tiers up to 10×8.
    /// </summary>
    [Serializable]
    public class BackpackState
    {
        /// <summary>0 = locked / tiny; 5 = maxed 10×8 case.</summary>
        public int capacityTier = 1;
        public int width = 6;
        public int height = 5;
        public BackpackItem[] items = Array.Empty<BackpackItem>();
        public bool unlocked = true;

        public void ApplyTier()
        {
            // T0 locked → T5 max (10×8). Intermediate sizes are story / seamstress.
            switch (Mathf.Clamp(capacityTier, 0, PlayerInventory.BackpackMaxTier))
            {
                case 0:
                    width = 4;
                    height = 3;
                    unlocked = false;
                    break;
                case 1:
                    width = 6;
                    height = 5;
                    unlocked = true;
                    break;
                case 2:
                    width = 8;
                    height = 5;
                    break;
                case 3:
                    width = 8;
                    height = 6;
                    break;
                case 4:
                    width = 10;
                    height = 6;
                    break;
                default:
                    width = PlayerInventory.BackpackMaxWidth;
                    height = PlayerInventory.BackpackMaxHeight;
                    break;
            }
        }

        public void EnsureValid()
        {
            ApplyTier();
            items ??= Array.Empty<BackpackItem>();
        }
    }

    /// <summary>
    /// Outfit pockets hold play deck boxes only (not bulk storage).
    /// Every outfit has ≥1 pocket; default deck sits in pocket 0.
    /// </summary>
    [Serializable]
    public class AvatarPocketState
    {
        /// <summary>How many pockets the current outfit exposes (min 1).</summary>
        public int pocketCount = 1;
        /// <summary>
        /// deckBoxes index per pocket, or -1 empty.
        /// Length = pocketCount. Pocket 0 = default "hip" pocket.
        /// </summary>
        public int[] pocketDeckBoxIndex = Array.Empty<int>();

        public void EnsureValid(int ownedDeckBoxCount, int maxCarry)
        {
            pocketCount = Mathf.Max(1, pocketCount);
            // Carry limit may be lower than pocket count
            var slots = Mathf.Min(pocketCount, Mathf.Max(1, maxCarry));
            if (pocketDeckBoxIndex == null || pocketDeckBoxIndex.Length != slots)
            {
                var next = new int[slots];
                for (var i = 0; i < slots; i++) next[i] = -1;
                if (pocketDeckBoxIndex != null)
                {
                    for (var i = 0; i < Mathf.Min(slots, pocketDeckBoxIndex.Length); i++)
                        next[i] = pocketDeckBoxIndex[i];
                }

                pocketDeckBoxIndex = next;
            }

            // Default: first deck box in pocket 0 if empty
            if (ownedDeckBoxCount > 0 && pocketDeckBoxIndex.Length > 0 && pocketDeckBoxIndex[0] < 0)
                pocketDeckBoxIndex[0] = 0;

            for (var i = 0; i < pocketDeckBoxIndex.Length; i++)
            {
                if (pocketDeckBoxIndex[i] >= ownedDeckBoxCount)
                    pocketDeckBoxIndex[i] = -1;
            }
        }
    }

    /// <summary>Magical deck box — artifact cards only (not TCG main deck).</summary>
    [Serializable]
    public class ArtifactDeckBoxState
    {
        public bool owned = true;
        public string name = "Artifact Deck Box";
        public int[] artifactCardIds = Array.Empty<int>();
        public int capacity = 40;
        /// <summary>When true, sits in backpack (2×2); else at home.</summary>
        public bool atHome = true;

        public void EnsureValid()
        {
            artifactCardIds ??= Array.Empty<int>();
            capacity = Mathf.Max(10, capacity);
            if (string.IsNullOrEmpty(name)) name = "Artifact Deck Box";
        }
    }

    /// <summary>
    /// Separate raid-only Tome Deck. Pages reference real TCG S/T card IDs.
    /// </summary>
    [Serializable]
    public class TomeState
    {
        public bool hasTomeItem;
        public int[] unlockedPageIds = Array.Empty<int>();
        public int[] equippedPageIds = Array.Empty<int>();
        public int pathTeam;
    }

    /// <summary>
    /// Realistic collection inventory:
    /// · Home bulk <b>card boxes</b> (100/500/1000 cardboard storage)
    /// · <b>Binders</b> for organization (18/page, 5–20 pages, max 360)
    /// · Play <b>deck boxes</b> (Main/Extra/Side for duels) — pockets only, never backpack bulk
    /// · RE4-style <b>backpack</b> grid for travel (boxes / binders / currency / artifacts)
    /// · GO-style view-anywhere; home cards need trade-transport item to move remotely
    /// </summary>
    [Serializable]
    public class PlayerInventory
    {
        // ── Storage boxes (home bulk) ──────────────────────────────────────
        public const int StorageBoxSizeSmall = 100;
        public const int StorageBoxSizeMed = 500;
        public const int StorageBoxSizeDefault = 1000;
        public const int MaxStorageBoxes = 20;

        // ── Binders (organized pages) ──────────────────────────────────────
        /// <summary>3×3 front + 3×3 back.</summary>
        public const int BinderSlotsPerSide = 9;
        public const int CardsPerBinderPage = 18; // double-sided 3×3
        public const int BinderFreePages = 5;
        public const int BinderMaxPages = 20; // 20 × 18 = 360
        public const int BinderMaxCards = BinderMaxPages * CardsPerBinderPage; // 360
        public const int MaxBinders = 20;

        // ── Backpack case (RE4 grid; maxed silhouette is always 10×8) ─────
        public const int BackpackMaxTier = 5;
        public const int BackpackMaxWidth = 10;
        public const int BackpackMaxHeight = 8;

        // ── Play deck boxes (pockets only — not backpack storage) ─────────
        public const int MaxPlayDeckBoxes = 10;
        public const int StarterPlayDeckSlots = 3;
        /// <summary>Default travel carry: 1 deck box (default clothing has a single pocket).</summary>
        public const int DefaultCarryDeckBoxes = 1;
        /// <summary>Hard cap on simultaneous pocket-carried deck boxes.</summary>
        public const int MaxCarryDeckBoxes = 6;

        // ── Shop prices (Digizeni soft / Duel Coins cosmetic-leaning) ─────
        public const int PriceBox100Digi = 150;
        public const int PriceBox500Digi = 600;
        public const int PriceBox1000Digi = 1000;
        public const int PriceBinderDigi = 400;
        public const int PriceBinderPageDigi = 50;
        public const int PriceBinderPageDc = 5;
        public const int PriceThirdDeckCarryDigi = 800;
        public const int PriceExtraDeckCarryDigi = 1200;
        public const int PriceTradeTransportDigi = 200;

        // ── Flags ──────────────────────────────────────────────────────────
        public bool hasBackpack;
        public bool hasDuelDisk;

        /// <summary>Home bulk cardboard boxes (default one × 1000).</summary>
        public StorageBoxState[] storageBoxes = Array.Empty<StorageBoxState>();

        /// <summary>Organization binders (pages of 18).</summary>
        public BinderState[] binders = Array.Empty<BinderState>();

        /// <summary>
        /// Playable constructed decks for the Spirit Dueler.
        /// Strictly Main/Extra/Side — never used as bulk storage; not counted in backpack grid.
        /// </summary>
        public DeckBoxState[] deckBoxes = Array.Empty<DeckBoxState>();

        /// <summary>How many play-deck slots the player owns (owned boxes at home / roster).</summary>
        public int deckBoxSlotCount = 1;

        /// <summary>
        /// How many deck boxes can be carried on outfit pockets while traveling.
        /// Derived from equipped shirt + bottoms (+ tailor extras). Default clothing = 1.
        /// </summary>
        public int carryDeckBoxSlots = DefaultCarryDeckBoxes;

        /// <summary>Story flag: third pocket-carry slot unlocked (sets carry to ≥3).</summary>
        public bool unlockedThirdDeckCarry;

        /// <summary>RE4-style travel pack (grid grows with story tiers).</summary>
        public BackpackState backpack = new();

        /// <summary>Outfit pockets for play deck boxes only.</summary>
        public AvatarPocketState pockets = new();

        /// <summary>Owned clothing ids + tailor-sewn pocket extras.</summary>
        public ClothingWardrobe wardrobe = new();

        /// <summary>Deck box index used for duels (pocket 0 / hip). -1 if none.</summary>
        public int ActivePlayDeckIndex
        {
            get
            {
                EnsureValid();
                pockets.EnsureValid(deckBoxes?.Length ?? 0, carryDeckBoxSlots);
                if (pockets.pocketDeckBoxIndex != null && pockets.pocketDeckBoxIndex.Length > 0)
                {
                    var i = pockets.pocketDeckBoxIndex[0];
                    if (i >= 0 && i < deckBoxes.Length && deckBoxes[i] != null)
                        return i;
                }

                if (deckBoxes == null) return -1;
                for (var i = 0; i < deckBoxes.Length; i++)
                {
                    var b = deckBoxes[i];
                    if (b != null && (b.occupied || (b.main != null && b.main.Length > 0)))
                        return i;
                }

                return deckBoxes.Length > 0 ? 0 : -1;
            }
        }

        /// <summary>Deck boxes currently on-hand (in outfit pockets only — clothing pocket count is the cap).</summary>
        public List<int> OnHandDeckIndices()
        {
            EnsureDeckBoxSlots();
            pockets.EnsureValid(deckBoxes.Length, carryDeckBoxSlots);
            var list = new List<int>();
            if (pockets.pocketDeckBoxIndex != null)
            {
                foreach (var i in pockets.pocketDeckBoxIndex)
                {
                    if (i < 0 || i >= deckBoxes.Length || deckBoxes[i] == null) continue;
                    if (!list.Contains(i)) list.Add(i);
                }
            }

            return list;
        }

        /// <summary>Make <paramref name="boxIndex"/> the main (pocket 0) play deck. Swaps if it was in another pocket.</summary>
        public bool SetActivePlayDeck(int boxIndex)
        {
            EnsureDeckBoxSlots();
            pockets.EnsureValid(deckBoxes.Length, carryDeckBoxSlots);
            if (boxIndex < 0 || boxIndex >= deckBoxes.Length || deckBoxes[boxIndex] == null)
                return false;
            var slots = pockets.pocketDeckBoxIndex;
            if (slots == null || slots.Length == 0) return false;
            var prev = slots[0];
            if (prev == boxIndex) return true;
            for (var i = 1; i < slots.Length; i++)
            {
                if (slots[i] != boxIndex) continue;
                slots[i] = prev;
                break;
            }

            slots[0] = boxIndex;
            deckBoxes[boxIndex].occupied = true;
            return true;
        }

        public string DeckBoxDisplayName(int index)
        {
            if (deckBoxes == null || index < 0 || index >= deckBoxes.Length) return "Deck";
            var b = deckBoxes[index];
            if (b == null || string.IsNullOrEmpty(b.name)) return "Deck " + (index + 1);
            return b.name;
        }

        /// <summary>Magical box for artifact cards (card form).</summary>
        public ArtifactDeckBoxState artifactDeckBox = new();

        /// <summary>
        /// Currency holder pouches always occupy 1 backpack square each when travel pack is active.
        /// Players receive one per currency type.
        /// </summary>
        public bool hasDigizeniHolder = true;
        public bool hasDuelCoinHolder = true;
        public bool hasSetEnergyHolder = true;

        /// <summary>
        /// Special items that let a player transport one home-base card during a remote trade
        /// (Pokémon GO–style: view inventory anywhere, no free teleport without this).
        /// </summary>
        public int tradeTransportCharges;

        /// <summary>
        /// Lab / desktop catalog: every storage box counts as on-hand even if it
        /// cannot physically fit in the backpack grid. Without this, SyncBackpackOccupancy
        /// force-homes 4×3 lab boxes (6×5 pack holds one) and the deck editor greys them.
        /// </summary>
        public bool labCarryAllStorage;

        /// <summary>Owned decorative storage box skin ids (1 of each per account).</summary>
        public string[] ownedDecorativeBoxIds = Array.Empty<string>();

        public TomeState tome = new();

        // ── Legacy migration (pre storage-box schema) ──────────────────────
        /// <summary>Obsolete: old flat binder stack. Migrated into storageBoxes[0] on EnsureValid.</summary>
        public CardStackEntry[] binderCards = Array.Empty<CardStackEntry>();
        /// <summary>Obsolete count field.</summary>
        public int binderCount = 1;

        /// <summary>RE4 footprint for a cardboard storage box capacity.</summary>
        public static Vector2Int FootprintForStorageCapacity(int capacity) => capacity switch
        {
            StorageBoxSizeSmall => new Vector2Int(2, 2),
            StorageBoxSizeMed => new Vector2Int(3, 3),
            StorageBoxSizeDefault => new Vector2Int(4, 3),
            _ => new Vector2Int(3, 3)
        };

        public static PlayerInventory Empty()
        {
            return new PlayerInventory
            {
                hasBackpack = false,
                hasDuelDisk = false,
                storageBoxes = Array.Empty<StorageBoxState>(),
                binders = Array.Empty<BinderState>(),
                deckBoxes = Array.Empty<DeckBoxState>(),
                deckBoxSlotCount = 0,
                carryDeckBoxSlots = DefaultCarryDeckBoxes,
                unlockedThirdDeckCarry = false,
                backpack = new BackpackState { capacityTier = 0, unlocked = false },
                pockets = new AvatarPocketState(),
                wardrobe = new ClothingWardrobe(),
                artifactDeckBox = new ArtifactDeckBoxState { owned = false },
                hasDigizeniHolder = true,
                hasDuelCoinHolder = true,
                hasSetEnergyHolder = true,
                tradeTransportCharges = 0,
                labCarryAllStorage = false,
                ownedDecorativeBoxIds = Array.Empty<string>(),
                binderCards = Array.Empty<CardStackEntry>(),
                binderCount = 0,
                tome = new TomeState()
            };
        }

        public void EnsureValid()
        {
            storageBoxes ??= Array.Empty<StorageBoxState>();
            binders ??= Array.Empty<BinderState>();
            deckBoxes ??= Array.Empty<DeckBoxState>();
            binderCards ??= Array.Empty<CardStackEntry>();
            ownedDecorativeBoxIds ??= Array.Empty<string>();
            tome ??= new TomeState();
            tome.unlockedPageIds ??= Array.Empty<int>();
            tome.equippedPageIds ??= Array.Empty<int>();
            backpack ??= new BackpackState();
            pockets ??= new AvatarPocketState();
            wardrobe ??= new ClothingWardrobe();
            wardrobe.EnsureValid();
            ClothingCatalog.GrantStarter(wardrobe);
            artifactDeckBox ??= new ArtifactDeckBoxState();
            deckBoxSlotCount = Mathf.Clamp(deckBoxSlotCount, 0, MaxPlayDeckBoxes);
            carryDeckBoxSlots = Mathf.Clamp(carryDeckBoxSlots, 1, MaxCarryDeckBoxes);
            tradeTransportCharges = Mathf.Max(0, tradeTransportCharges);

            // Migrate legacy binderCards → first storage box
            if (binderCards.Length > 0)
            {
                if (storageBoxes.Length == 0)
                {
                    storageBoxes = new[]
                    {
                        new StorageBoxState
                        {
                            name = "Home Card Box",
                            capacity = StorageBoxSizeDefault,
                            stacks = binderCards,
                            atHome = true
                        }
                    };
                }
                else
                {
                    storageBoxes[0] ??= new StorageBoxState { capacity = StorageBoxSizeDefault };
                    storageBoxes[0].EnsureValid();
                    if (storageBoxes[0].stacks == null || storageBoxes[0].stacks.Length == 0)
                        storageBoxes[0].stacks = binderCards;
                }

                binderCards = Array.Empty<CardStackEntry>();
            }

            // Starter home box if empty but backpack granted
            if (hasBackpack && storageBoxes.Length == 0)
            {
                storageBoxes = new[]
                {
                    new StorageBoxState
                    {
                        name = "Home Card Box (1000)",
                        capacity = StorageBoxSizeDefault,
                        stacks = Array.Empty<CardStackEntry>(),
                        atHome = true
                    }
                };
            }

            if (hasBackpack && binders.Length == 0 && binderCount > 0)
            {
                binders = new[]
                {
                    new BinderState
                    {
                        name = "Starter Binder",
                        pageCount = BinderFreePages,
                        atHome = true
                    }
                };
            }

            foreach (var b in storageBoxes)
                b?.EnsureValid();
            foreach (var b in binders)
                b?.EnsureValid();

            // Backpack: story unlock follows hasBackpack flag
            if (hasBackpack)
            {
                if (backpack.capacityTier < 1) backpack.capacityTier = 1;
                backpack.unlocked = true;
            }
            backpack.EnsureValid();

            SyncBackpackOccupancy();

            // Pockets for play decks only
            pockets.EnsureValid(deckBoxes?.Length ?? 0, carryDeckBoxSlots);

            artifactDeckBox.EnsureValid();
            if (hasBackpack && !artifactDeckBox.owned)
                artifactDeckBox.owned = true;

            binderCount = binders.Length;
        }

        /// <summary>
        /// Rebuild backpack item list from container atHome flags + currency holders.
        /// Does not auto-place oversized home boxes; only items with atHome=false stay packed.
        /// </summary>
        public void SyncBackpackOccupancy()
        {
            backpack ??= new BackpackState();
            backpack.EnsureValid();
            var list = new List<BackpackItem>();

            void TryAddFixed(BackpackItemKind kind, string label)
            {
                // Keep existing position if already placed
                BackpackItem existing = null;
                if (backpack.items != null)
                {
                    foreach (var it in backpack.items)
                    {
                        if (it != null && it.Kind == kind)
                        {
                            existing = it;
                            break;
                        }
                    }
                }

                if (existing != null)
                {
                    list.Add(existing);
                    return;
                }

                if (!TryFindFreeCell(backpack, 1, 1, out var x, out var y, exclude: list))
                    return; // no room — leave unplaced until player expands pack
                list.Add(new BackpackItem
                {
                    Kind = kind,
                    refIndex = -1,
                    gridX = x,
                    gridY = y,
                    gridW = 1,
                    gridH = 1,
                    label = label
                });
            }

            if (hasDigizeniHolder) TryAddFixed(BackpackItemKind.CurrencyDigizeni, "Digizeni Pouch");
            if (hasDuelCoinHolder) TryAddFixed(BackpackItemKind.CurrencyDuelCoin, "Duel Coin Pouch");
            if (hasSetEnergyHolder) TryAddFixed(BackpackItemKind.CurrencySetEnergy, "Set Energy Cell");

            // Storage boxes marked not-at-home
            for (var i = 0; i < storageBoxes.Length; i++)
            {
                var box = storageBoxes[i];
                if (box == null || box.atHome) continue;
                var fp = box.Footprint;
                BackpackItem existing = null;
                if (backpack.items != null)
                {
                    foreach (var it in backpack.items)
                    {
                        if (it != null && it.Kind == BackpackItemKind.StorageBox && it.refIndex == i)
                        {
                            existing = it;
                            break;
                        }
                    }
                }

                if (existing != null)
                {
                    existing.gridW = fp.x;
                    existing.gridH = fp.y;
                    existing.label = box.name;
                    list.Add(existing);
                    continue;
                }

                if (!TryFindFreeCell(backpack, fp.x, fp.y, out var x, out var y, exclude: list))
                {
                    // Can't fit — real accounts unpack home. Lab catalog stays on-hand
                    // without a grid cell so the deck editor can use every copy.
                    if (!labCarryAllStorage)
                        box.atHome = true;
                    continue;
                }

                list.Add(new BackpackItem
                {
                    Kind = BackpackItemKind.StorageBox,
                    refIndex = i,
                    gridX = x,
                    gridY = y,
                    gridW = fp.x,
                    gridH = fp.y,
                    label = box.name
                });
            }

            for (var i = 0; i < binders.Length; i++)
            {
                var b = binders[i];
                if (b == null || b.atHome) continue;
                var fp = b.Footprint;
                BackpackItem existing = null;
                if (backpack.items != null)
                {
                    foreach (var it in backpack.items)
                    {
                        if (it != null && it.Kind == BackpackItemKind.Binder && it.refIndex == i)
                        {
                            existing = it;
                            break;
                        }
                    }
                }

                if (existing != null)
                {
                    existing.gridW = fp.x;
                    existing.gridH = fp.y;
                    existing.label = b.name;
                    list.Add(existing);
                    continue;
                }

                if (!TryFindFreeCell(backpack, fp.x, fp.y, out var x, out var y, exclude: list))
                {
                    b.atHome = true;
                    continue;
                }

                list.Add(new BackpackItem
                {
                    Kind = BackpackItemKind.Binder,
                    refIndex = i,
                    gridX = x,
                    gridY = y,
                    gridW = fp.x,
                    gridH = fp.y,
                    label = b.name
                });
            }

            if (artifactDeckBox != null && artifactDeckBox.owned && !artifactDeckBox.atHome)
            {
                const int aw = 2, ah = 2;
                BackpackItem existing = null;
                if (backpack.items != null)
                {
                    foreach (var it in backpack.items)
                    {
                        if (it != null && it.Kind == BackpackItemKind.ArtifactDeckBox)
                        {
                            existing = it;
                            break;
                        }
                    }
                }

                if (existing != null)
                {
                    list.Add(existing);
                }
                else if (TryFindFreeCell(backpack, aw, ah, out var x, out var y, exclude: list))
                {
                    list.Add(new BackpackItem
                    {
                        Kind = BackpackItemKind.ArtifactDeckBox,
                        refIndex = 0,
                        gridX = x,
                        gridY = y,
                        gridW = aw,
                        gridH = ah,
                        label = artifactDeckBox.name
                    });
                }
                else
                {
                    artifactDeckBox.atHome = true;
                }
            }

            backpack.items = list.ToArray();
        }

        /// <summary>Find free top-left cell for a w×h item, considering already-planned placements.</summary>
        public static bool TryFindFreeCell(BackpackState pack, int w, int h, out int x, out int y,
            List<BackpackItem> exclude = null)
        {
            x = y = 0;
            if (pack == null || !pack.unlocked) return false;
            pack.EnsureValid();
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            if (w > pack.width || h > pack.height) return false;

            var occ = new bool[pack.width, pack.height];
            void Stamp(BackpackItem it)
            {
                if (it == null) return;
                for (var yy = it.gridY; yy < it.gridY + it.gridH; yy++)
                for (var xx = it.gridX; xx < it.gridX + it.gridW; xx++)
                {
                    if (xx >= 0 && yy >= 0 && xx < pack.width && yy < pack.height)
                        occ[xx, yy] = true;
                }
            }

            if (exclude != null)
            {
                foreach (var it in exclude) Stamp(it);
            }
            else if (pack.items != null)
            {
                foreach (var it in pack.items) Stamp(it);
            }

            for (var yy = 0; yy <= pack.height - h; yy++)
            for (var xx = 0; xx <= pack.width - w; xx++)
            {
                var ok = true;
                for (var dy = 0; dy < h && ok; dy++)
                for (var dx = 0; dx < w && ok; dx++)
                    if (occ[xx + dx, yy + dy]) ok = false;
                if (!ok) continue;
                x = xx;
                y = yy;
                return true;
            }

            return false;
        }

        // ── Capacity helpers ───────────────────────────────────────────────

        public int TotalStorageCapacity()
        {
            EnsureValid();
            var n = 0;
            foreach (var b in storageBoxes)
                if (b != null) n += b.capacity;
            return n;
        }

        public int TotalStorageUsed()
        {
            EnsureValid();
            var n = 0;
            foreach (var b in storageBoxes)
                if (b != null) n += b.UsedSlots();
            return n;
        }

        /// <summary>Legacy name: cards in bulk storage (not binder pages).</summary>
        public int TotalBinderCards() => TotalStorageUsed();

        /// <summary>Legacy constant for old UI — use TotalStorageCapacity().</summary>
        public const int BinderCardCap = StorageBoxSizeDefault;

        public int TotalBinderOrganizedCards()
        {
            EnsureValid();
            var n = 0;
            foreach (var b in binders)
                if (b != null) n += b.UsedSlots();
            return n;
        }

        public int CountOf(int cardId)
        {
            EnsureValid();
            var n = 0;
            foreach (var box in storageBoxes)
            {
                if (box?.stacks == null) continue;
                foreach (var s in box.stacks)
                    if (s != null && s.cardId == cardId)
                        n += s.qty;
            }

            return n;
        }

        /// <summary>Add into first storage box with free room (overflow to next). Returns leftover qty.</summary>
        public int AddCards(int cardId, int qty)
        {
            if (cardId <= 0 || qty <= 0) return 0;
            EnsureValid();
            if (storageBoxes.Length == 0)
            {
                storageBoxes = new[]
                {
                    new StorageBoxState
                    {
                        name = "Home Card Box (1000)",
                        capacity = StorageBoxSizeDefault
                    }
                };
            }

            var left = qty;
            for (var bi = 0; bi < storageBoxes.Length && left > 0; bi++)
            {
                var box = storageBoxes[bi];
                if (box == null) continue;
                box.EnsureValid();
                var free = box.FreeSlots();
                if (free <= 0) continue;
                var put = Mathf.Min(left, free);
                AddToStacks(ref box.stacks, cardId, put);
                left -= put;
            }

            return left; // >0 means collection full
        }

        /// <summary>Remove qty of cardId from storage boxes. Returns false if not enough.</summary>
        public bool TryRemoveCards(int cardId, int qty)
        {
            if (qty <= 0) return true;
            if (CountOf(cardId) < qty) return false;
            EnsureValid();
            var left = qty;
            for (var bi = 0; bi < storageBoxes.Length && left > 0; bi++)
            {
                var box = storageBoxes[bi];
                if (box?.stacks == null) continue;
                var list = new List<CardStackEntry>(box.stacks);
                for (var i = list.Count - 1; i >= 0 && left > 0; i--)
                {
                    if (list[i].cardId != cardId) continue;
                    var take = Mathf.Min(left, list[i].qty);
                    list[i].qty -= take;
                    left -= take;
                    if (list[i].qty <= 0) list.RemoveAt(i);
                }

                box.stacks = list.ToArray();
            }

            return left == 0;
        }

        /// <summary>Cards in storage not assigned to any play deck box (Rare Hunter loot pool).</summary>
        public List<int> ListUnusedCardIds()
        {
            EnsureValid();
            var unused = new List<int>();
            foreach (var box in storageBoxes)
            {
                if (box?.stacks == null) continue;
                foreach (var stack in box.stacks)
                {
                    if (stack == null || stack.qty <= 0) continue;
                    var inDeck = CountInDecks(stack.cardId);
                    // Also count cards sleeved in binders as "used" for organization? No —
                    // binder slots are references/display; ownership is storage. For loot, only decks lock.
                    var free = stack.qty - inDeck;
                    for (var i = 0; i < free; i++)
                        unused.Add(stack.cardId);
                }
            }

            return unused;
        }

        public bool CanBuyStorageBox(int size) =>
            size is StorageBoxSizeSmall or StorageBoxSizeMed or StorageBoxSizeDefault
            && storageBoxes != null && storageBoxes.Length < MaxStorageBoxes;

        public bool CanBuyBinder() =>
            binders != null && binders.Length < MaxBinders;

        public bool CanBuyBinderPage(int binderIndex)
        {
            EnsureValid();
            if (binderIndex < 0 || binderIndex >= binders.Length) return false;
            var b = binders[binderIndex];
            return b != null && b.pageCount < BinderMaxPages;
        }

        public static int DigiPriceForBox(int size) => size switch
        {
            StorageBoxSizeSmall => PriceBox100Digi,
            StorageBoxSizeMed => PriceBox500Digi,
            StorageBoxSizeDefault => PriceBox1000Digi,
            _ => PriceBox1000Digi
        };

        int CountInDecks(int cardId)
        {
            var n = 0;
            foreach (var box in deckBoxes)
            {
                if (box == null || !box.occupied) continue;
                n += CountIn(box.main, cardId);
                n += CountIn(box.extra, cardId);
                n += CountIn(box.side, cardId);
            }

            return n;
        }

        /// <summary>Copies in storage boxes currently packed for travel (<see cref="StorageBoxState.atHome"/> false).</summary>
        public int CountOnHand(int cardId)
        {
            EnsureValid();
            if (labCarryAllStorage) return CountOf(cardId);
            return CountInStorageByHome(cardId, atHome: false);
        }

        /// <summary>Copies sitting at home base only.</summary>
        public int CountAtHome(int cardId)
        {
            EnsureValid();
            if (labCarryAllStorage) return 0;
            return CountInStorageByHome(cardId, atHome: true);
        }

        int CountInStorageByHome(int cardId, bool atHome)
        {
            var n = 0;
            if (storageBoxes == null) return 0;
            foreach (var box in storageBoxes)
            {
                if (box == null || box.atHome != atHome || box.stacks == null) continue;
                foreach (var s in box.stacks)
                    if (s != null && s.cardId == cardId)
                        n += Mathf.Max(0, s.qty);
            }

            return n;
        }

        /// <summary>
        /// Free copies not locked in other play deck boxes (exclude <paramref name="exceptDeckBoxIndex"/> while editing).
        /// </summary>
        public int FreeCopiesForDeck(int cardId, int exceptDeckBoxIndex = -1)
        {
            EnsureValid();
            var owned = CountOf(cardId);
            var locked = 0;
            for (var i = 0; i < deckBoxes.Length; i++)
            {
                if (i == exceptDeckBoxIndex) continue;
                var box = deckBoxes[i];
                if (box == null || !box.occupied) continue;
                locked += CountIn(box.main, cardId);
                locked += CountIn(box.extra, cardId);
                locked += CountIn(box.side, cardId);
            }

            return Mathf.Max(0, owned - locked);
        }

        /// <summary>Ensure deckBoxes array has at least <see cref="deckBoxSlotCount"/> slots (empty names OK).</summary>
        public void EnsureDeckBoxSlots()
        {
            EnsureValid();
            deckBoxSlotCount = Mathf.Clamp(deckBoxSlotCount, 1, MaxPlayDeckBoxes);
            var list = new List<DeckBoxState>(deckBoxes ?? Array.Empty<DeckBoxState>());
            while (list.Count < deckBoxSlotCount)
            {
                list.Add(new DeckBoxState
                {
                    name = "Deck Box " + (list.Count + 1),
                    occupied = false,
                    main = Array.Empty<int>(),
                    extra = Array.Empty<int>(),
                    side = Array.Empty<int>()
                });
            }

            deckBoxes = list.ToArray();
            for (var i = 0; i < deckBoxes.Length; i++)
            {
                deckBoxes[i] ??= new DeckBoxState { name = "Deck Box " + (i + 1) };
                deckBoxes[i].main ??= Array.Empty<int>();
                deckBoxes[i].extra ??= Array.Empty<int>();
                deckBoxes[i].side ??= Array.Empty<int>();
                if (string.IsNullOrEmpty(deckBoxes[i].name))
                    deckBoxes[i].name = "Deck Box " + (i + 1);
            }
        }

        public void ClearDeckBox(int index)
        {
            EnsureDeckBoxSlots();
            if (index < 0 || index >= deckBoxes.Length) return;
            var b = deckBoxes[index];
            b.main = Array.Empty<int>();
            b.extra = Array.Empty<int>();
            b.side = Array.Empty<int>();
            // Keep occupied so it remains a selectable open box
            b.occupied = true;
        }

        public void RenameDeckBox(int index, string name)
        {
            EnsureDeckBoxSlots();
            if (index < 0 || index >= deckBoxes.Length) return;
            if (string.IsNullOrWhiteSpace(name)) return;
            deckBoxes[index].name = name.Trim();
            deckBoxes[index].occupied = true;
        }

        /// <summary>
        /// Occupy an empty box slot, or grow the roster up to <see cref="MaxPlayDeckBoxes"/>.
        /// </summary>
        public bool TryAddDeckBox(out int index, out string error)
        {
            EnsureDeckBoxSlots();
            for (var i = 0; i < deckBoxes.Length; i++)
            {
                var b = deckBoxes[i];
                if (b == null) continue;
                if (b.occupied) continue;
                if ((b.main?.Length ?? 0) + (b.extra?.Length ?? 0) + (b.side?.Length ?? 0) > 0)
                    continue;
                b.occupied = true;
                if (string.IsNullOrEmpty(b.name) || b.name.StartsWith("Deck Box"))
                    b.name = "Deck " + (i + 1);
                index = i;
                error = null;
                return true;
            }

            if (deckBoxSlotCount >= MaxPlayDeckBoxes)
            {
                index = -1;
                error = "Max deck boxes (10).";
                return false;
            }

            deckBoxSlotCount++;
            EnsureDeckBoxSlots();
            index = deckBoxes.Length - 1;
            deckBoxes[index].occupied = true;
            deckBoxes[index].name = "Deck " + (index + 1);
            error = null;
            return true;
        }

        /// <summary>
        /// Drop a constructed deck box from the roster. Cards stay in storage.
        /// Keeps at least one box. Remaps clothing-pocket indices.
        /// </summary>
        public bool TryRemoveDeckBox(int index, out string error)
        {
            EnsureDeckBoxSlots();
            error = null;
            if (index < 0 || index >= deckBoxes.Length || deckBoxes[index] == null)
            {
                error = "Invalid deck box.";
                return false;
            }

            if (deckBoxes.Length <= 1)
            {
                error = "Keep at least one deck box.";
                return false;
            }

            if (pockets?.pocketDeckBoxIndex != null)
            {
                for (var i = 0; i < pockets.pocketDeckBoxIndex.Length; i++)
                {
                    var di = pockets.pocketDeckBoxIndex[i];
                    if (di == index) pockets.pocketDeckBoxIndex[i] = -1;
                    else if (di > index) pockets.pocketDeckBoxIndex[i] = di - 1;
                }
            }

            var list = new List<DeckBoxState>(deckBoxes);
            list.RemoveAt(index);
            deckBoxes = list.ToArray();
            deckBoxSlotCount = Mathf.Clamp(deckBoxes.Length, 1, MaxPlayDeckBoxes);
            pockets.EnsureValid(deckBoxes.Length, carryDeckBoxSlots);
            return true;
        }

        static void AddToStacks(ref CardStackEntry[] stacks, int cardId, int qty)
        {
            var list = new List<CardStackEntry>(stacks ?? Array.Empty<CardStackEntry>());
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].cardId != cardId) continue;
                list[i].qty += qty;
                stacks = list.ToArray();
                return;
            }

            list.Add(new CardStackEntry { cardId = cardId, qty = qty });
            stacks = list.ToArray();
        }

        static int CountIn(int[] arr, int id)
        {
            if (arr == null) return 0;
            var n = 0;
            foreach (var x in arr)
                if (x == id) n++;
            return n;
        }
    }
}
