# Inventory specification — Duel Monsters WRLDZ

**Status:** design target for GDD + UI.  
**Scope:** home-base collection vs on-hand adventure kit. Artifact *card types* and unique key items are named only; their effects are specified elsewhere.  
**Presentation:** every screen has phone (`NonArPortrait`) and AR-holo (`ArDiskHolo`) layouts.

The fantasy is physical: cardboard at home, a real backpack on your back, deck boxes in coat pockets. Storage pressure is intentional. Silent loss is not.

---

## 1. Player-facing pitch

You keep your collection at **Home Base**. When you walk out, you take a **backpack**, an **Artifact Deck Box**, a **Primary Deck Box**, and whatever extra decks fit in **clothing pockets**.

Cards you earn on the street go into a **carried card box**. If those boxes are full, the card sits **loose in the backpack** with a timer. Sleeve it before the timer ends, or it is gone.

---

## 2. Item hierarchy

```
Account
├── Home Base (not carried)
│   ├── Card boxes (general collection)
│   ├── Binders + loose binder pages (favorites)
│   ├── Unequipped deck boxes
│   └── Parked backpack contents when you “leave the pack at home” (future room)
│
└── On-hand / equipped (adventure)
    ├── Artifact Deck Box     ← permanent, 0 grid cells
    ├── Primary Deck Box      ← permanent, 0 grid cells
    ├── Clothing pockets      ← 0–6 extra deck boxes, 0 grid cells each
    └── Backpack grid         ← 16→48 cells of tetris cargo
        ├── Card boxes
        ├── Binders
        ├── Spare deck boxes
        ├── Unsecured Cards (1×1, timed)
        ├── Packs, consumables, materials, loot, tools
        └── Loose binder pages
```

**Rule of thumb**

| Where | What lives there | Uses backpack cells? |
|---|---|---|
| Home | Full collection, spare storage, unused decks | No |
| Artifact Deck Box | Digizeni, Duel Coins, Set Energy, artifacts, quest/key items | No |
| Primary Deck Box | Active Main / Extra / Side | No |
| Clothing pocket | One complete spare deck | No |
| Backpack | Cargo you physically haul | Yes |

Currencies are **never** backpack tiles. They are always in the Artifact Deck Box.

---

## 3. Core loops

### 3.1 Leave-home prep (satisfying, slow)

1. Open **Home**.
2. Build / swap the Primary Deck.
3. Pocket 0–N spare decks.
4. Pick a **Preferred Card Box** and pack it (plus extras if you have grid room).
5. Pack binders, packs, consumables.
6. Confirm **On-hand summary**: decks, box free slots, backpack fill.
7. Walk out.

### 3.2 On the street (fast, tense)

1. Reward → auto-store into Preferred Card Box, else any carried box.
2. If boxes are full → Unsecured Card on the grid + toast.
3. If grid is also full → **Make Room** modal. Never silent delete.
4. Glance **Decks** to swap Primary with a pocketed / packed deck box.

### 3.3 Come home (relief)

1. Unsecured Cards auto-file into home card boxes; timers die.
2. Packed boxes / binders can be sent home with one tap.
3. Overflow at home prompts “buy a box” — no street timer.

### 3.4 Upgrade (visible, permanent)

- Seamstress: backpack grid expansions (16 → 48).
- Seamstress / tailor: sew pockets onto compatible clothes.
- Bazaar: bigger card boxes, better binders, extra binder pages.

Each upgrade is a **bigger silhouette** (grid) or a **new holster** (pocket). Players should feel the extra cell, not a hidden number.

---

## 4. Home Base

Home Base is the only place the player can:

- Browse the **entire** collection (all boxes + binders + decks).
- Move cards freely between binders, card boxes, deck boxes, and the backpack.
- Buy / craft / upgrade boxes, binders, and pages.
- Change clothes without the “backpack full” street lock (extras go to home storage).
- Clear Unsecured Cards without a timer.

**View-anywhere (overworld):** the player may *look* at home collection counts. **Moving** a home card into a remote trade still spends **Trade Transport**. Deck *editing* while away may only use cards in **on-hand** boxes, binders, and deck boxes. Home cardboard is greyed until they return (lab/admin accounts excepted).

Home-base 3D furniture placement is out of this spec.

---

## 5. Binders — favorite storage

Binders are albums, not bulk boxes. They primarily hold cards marked **Favorite**.

### 5.1 Page anatomy

- A **Binder Page** is its own item until it is bound into a binder.
- One page = **front 3×3** + **back 3×3** = **18 cards**.
- Capacity = `pages × 18`.
- A 10-page binder = 180 cards, **20** visible 3×3 sides.

### 5.2 Binder types (max pages)

| Type | Max pages | Max cards | How you get it | Backpack size |
|---|---:|---:|---|---|
| **Starter Album** | 5 | 90 | Starter kit | 2×2 |
| **Duelist Binder** | 10 | 180 | Bazaar | 2×3 |
| **Master Binder** | 20 | 360 | Bazaar / late story | 3×3 |

Pages cannot be added past the binder’s type cap. Extra **unbound pages** sit in the backpack (1×2) or at home.

Empty starter pages: the Starter Album ships with **5 pages installed** (90 slots). Other binders ship with **1 page**; buy more pages at the bazaar.

### 5.3 Binder UI (required)

- Current **page number** and **Front / Back**.
- Flip page (prev / next). Flip side (front ↔ back).
- 3×3 pockets with empty-slot silhouette.
- Footer: `Favorites 42 / 180 · p.3 FRONT`.
- Tap pocket → inspect card; hold empty → insert from collection (home only, or from on-hand boxes).
- Marking a card Favorite at home offers **Auto-sleeve into binder** if a slot is free.

Binders **may** ride in the backpack if the tetris tile fits. They are still *managed* at home.

---

## 6. Card boxes — general storage

Plain cardboard. Not decorative (skins later, 1 of each).

### 6.1 What goes in a box

Non-favorites, duplicates, trade stock, crafting cards/materials, general collection, cards received while away.

### 6.2 Sizes and backpack footprints

Larger boxes **must** eat more grid. Footprints are chosen so a starter 4×4 can still carry a Small box plus a deck and a pack, while Archive is a serious endgame haul.

| Name | Capacity | Grid | Cells | Notes |
|---|---:|---|---:|---|
| **Small Card Box** | 100 | 2×2 | 4 | Street extras, first expansion |
| **Medium Card Box** | 400 | 2×3 | 6 | Comfortable travel box |
| **Large Card Box** | 800 | 3×3 | 9 | Starter home box |
| **Long Card Box** | 1,600 | 4×3 | 12 | Needs 6×5 backpack or better |
| **Archive Card Box** | 3,200 | 4×4 | 16 | Fills an unexpanded pack by itself |

Starter kit: one **Large (800)** at home, empty-enough after the starter deck copies land.

### 6.3 Preferred Card Box

The player stars one **on-hand** card box as **Preferred**. New street cards try that box first. If it is not carried, the game uses the emptiest carried box and toasts: “Preferred box is at home — using [name].”

---

## 7. Receiving cards and overflow

### 7.1 Auto-place order (away from home)

1. Preferred Card Box, if carried and `free > 0`.
2. Other carried card boxes, most free slots first (stable order if tied).
3. Else spawn **Unsecured Card** in the backpack (1×1).
4. Else backpack is full → **Make Room** modal. Reward is held; it is not deleted.

### 7.2 Unsecured Card

- 1×1 backpack tile. **Does not stack.** Two Blue-Eyes are two tiles.
- Shows art + **timer ring**.
- Moving it into any card box (carried or, at home, any home box) **secures** it and clears the timer.
- Sleeve into a binder (home, or a carried binder with a free pocket) also secures it.
- Putting it into a deck box as part of a legal deck list secures it.

### 7.3 Timer

Real-time, paused in these states so a match never eats a card:

- Duel in progress
- Make Room modal open
- Application backgrounded > 2 minutes (resume with a “cards still loose” reminder)

| Card | Duration |
|---|---|
| Common / Rare | **4 hours** |
| Super / Ultra | **8 hours** |
| Secret / collector / Favorite | **12 hours** + extra warning |

**Color on the tile**

| Remaining | Color |
|---|---|
| > 50% | Cream |
| 50–25% | Gold |
| 25–10% | Orange |
| < 10% | Red pulse |
| 60 seconds on a Favorite / Secret | Modal: **Secure this card** (Snooze 5 min once, or Open bag) |

On expiry: the card is **destroyed**. Toast: “Unsecured [name] was lost.” One undo is **not** granted. Favorites and Secret rares fire the 60-second modal; if ignored, they still expire (storage pressure stays real).

### 7.4 Make Room modal (backpack full on reward)

Cannot be dismissed by tapping the dimmer. Choices:

1. **Open bag** — stash the pending card; bag opens; cannot close until the card is placed or discarded.
2. **Discard a backpack item** — pick one tile to drop, then the card files by §7.1.
3. **Refuse reward** — explicit. “You won’t get this card again.”
4. If the reward is a **Favorite / Secret**: Refuse is behind a second confirm.

Pending rewards sit in a one-slot **Hold** that is not a grid cell. The player cannot start another duel until Hold is empty.

---

## 8. Deck boxes

A deck box is **exactly one** playable deck: Main + Extra + Side. It is never bulk storage.

**Backpack size: 1×1.**

### 8.1 Permanent equipment (0 cells)

| Item | Role |
|---|---|
| **Artifact Deck Box** | Currencies, artifact cards, quest items, keys. Always on. Open from wallet / BAG inspect. |
| **Primary Deck Box** | Active duel deck. Always on. Shown on the overworld deck chip. |

These two **cannot** be dropped, sold, or put on the grid.

### 8.2 Extra deck boxes

Owned extras may be:

- At **home**
- On the **backpack** grid (1×1)
- In a **clothing pocket** (equipped spare, 0 cells)

**Swap Primary while away:** only with a deck that is already on-hand (pocket or backpack). The old Primary takes that slot. A deck that exists only at home cannot become Primary until you go home.

Tooltip: “That deck is at home.”

---

## 9. Clothing pockets and sewing

Pockets hold **deck boxes only**. Not card boxes, not packs.

### 9.1 Tiers (shirt + bottoms contribute; hats/hands/shoes do not)

Outfit pocket count = shirt pockets + bottoms pockets + sewn extras, **capped at 6**. Primary is **in addition** to these.

| Tier | Typical pieces | Base pockets | Sewn extras |
|---|---|---:|---:|
| **Basic** | Plain tee | 0 | +2 on the tee (once bought as “pocket kit”) |
| **Practical** | Plain pants | 1 hip | +2 |
| **Tailored** | Path hoodie, duel slacks | 2 + 1 | +2 each |
| **Masterwork** | Cargo jacket, cargo pants | 3 + 2 | +2 each |
| **Rare specialty** | Duel coat | 4 | +2 |

Starter outfit: **plain tee (0) + plain pants (1) = 1 spare pocket** + Primary. First extra deck needs a hoodie or a sewn pocket.

Seamstress / tailor at the bazaar: sew one extra pocket per visit onto a piece with remaining `MaxSewn`, for Digizeni (`ClothingCatalog.PriceSewnPocketDigi`).

### 9.2 Removing a deck from a pocket

1. If backpack has a free 1×1 → deck becomes a backpack tile.
2. If at home → may send the box **home** instead.
3. If backpack is full and away from home → **cannot unequip**. “No room for this deck box. Make a cell or change at home.”

### 9.3 Changing clothes

Compute pockets on the **new** outfit.

- If new count ≥ decks currently pocketed → keep assignments, leftover holsters empty.
- If new count is smaller → extras try backpack 1×1, then home (if at home).
- If any extra cannot land → **block the wardrobe apply**. Highlight the stuck deck boxes. “Take [deck] out of your coat first.”

Never convert a whole deck box into an Unsecured Card. Decks do not expire.

### 9.4 Pocket UI

Holsters labeled **POCKET 1…N**, not “primary.” Primary is a separate gold slot above the holsters: **PRIMARY — always equipped**.

---

## 10. Backpack

Equipped travel pack. Tetris grid. Not the collection.

### 10.1 What the grid holds

Card boxes, binders, unequipped deck boxes, Unsecured Cards, consumables, crafting materials, loot, tools, unopened packs, unbound binder pages, other portable items.

### 10.2 Grid progression (seamstress, permanent)

| Step | Grid | Cells | Feel |
|---|---|---:|---|
| Start | **4×4** | 16 | One Small box + a pack + tools |
| Expansion 1 | **5×4** | 20 | First “I bought space” beat |
| Expansion 2 | **5×5** | 25 | Square pack |
| Expansion 3 | **6×5** | 30 | Long box fits |
| Expansion 4 | **6×6** | 36 | Comfortable haul |
| Endgame | **8×6** | **48** | Hard cap for launch |

Do not ship a 10×8. 48 is the practical ceiling; a later cosmetic “frame” may draw unused cells as locked, but they must not accept items.

UI always draws the **current** live grid. Locked cells of the *next* expansion may ghost at 20% opacity with “Seamstress” — optional, one expansion ahead only, so the 4×4 does not look like a huge empty case.

### 10.3 Other footprints

| Item | Grid |
|---|---|
| Deck box | 1×1 |
| Unsecured Card | 1×1 |
| Booster / tin pack | 1×2 |
| Potion / ration / small material | 1×1 |
| Tool (sleeves, dice, locater) | 1×2 |
| Loot bundle | 2×1 |
| Unbound binder page | 1×2 |
| Binder (see §5.2) | 2×2 / 2×3 / 3×3 |
| Card box (see §6.2) | 2×2 … 4×4 |

Rotate 90° where `w ≠ h` (packs, Long box, Duelist binder). Rotation is a button on inspect, not a hidden gesture.

### 10.4 Fill, sort, full

- Fill chip: `12/16`. At 16/16 the chip turns red: **FULL**.
- **Sort** compact: largest tiles first, then kind, then name; pack top-left. Never discards Unsecured Cards.
- **Auto-store**: one button. Files every Unsecured Card into carried boxes (Preferred first). Leftover stay loose.
- Full grid: placing from home fails with “Backpack full — 16/16. Sort or visit the seamstress.”

---

## 11. UI screens

Dual layouts: phone sheet and compact AR holo. Same tabs.

### 11.1 BAG (on-hand)

Tabs: **CASE · POCKETS · DECKS · HOME**

| Tab | Content |
|---|---|
| **CASE** | Tetris backpack. Spanning tiles. Tap tile → inspect (rotate / send home if at base / auto-store if Unsecured). SORT + fill chip. Unsecured tiles show timer rings. |
| **POCKETS** | PRIMARY slot + clothing holsters. Tap spare to cycle / unequip. Outfit line: “Duel coat 4 · Cargo pants 2”. |
| **DECKS** | All owned boxes. Badges: PRIMARY, POCKET n, PACK, HOME. CREATE / DELETE / EDIT DECK (editor is the existing 40-card builder). Away from home, EDIT only for on-hand boxes. |
| **HOME** | Glance of home boxes, binders, parked decks, trade transport. “Go home to organize” when not at base. At base this tab is the collection workstation (or a deep-link into §11.2). |

Wallet chips (Đ / ◎ / ⚡) open the **Artifact Deck Box** sheet.

Inspect footer: name, size `w×h`, location, actions. No second nested glass plate — content sits in the hologram well.

### 11.2 Home collection (at base)

Three columns on phone (stacked on AR):

1. **Sources** — Boxes / Binders / Decks / Backpack.
2. **Grid or 3×3 page** — the open container.
3. **Filters** — set, type, rarity, name, newest, favorite, deck-legal, location, Unsecured timer.

Interactions: drag card → container; long-press → inspect; star → Favorite (offer binder sleeve).

Preferred Card Box: star on the box row.

### 11.3 Binder browser

See §5.3. Page curl / flip animation. Side toggle **FRONT · BACK**. Empty pockets dashed.

### 11.4 Card-box browser

List or packed-art wall. Capacity bar `742 / 800`. Sort same facets as collection. Button **Set as Preferred** (only if the box is on-hand or you are at home planning a trip).

### 11.5 Deck editor

Existing `DeckCollectionScreen`: collection left, Main/Extra/Side right. Away from home, grey home-only copies (`HomeOnly`). On-hand copies (carried boxes + binders + cards already in this deck) stay live.

### 11.6 On-hand deck switcher (overworld / pre-duel)

Horizontal chips: Primary + each pocketed deck. Backpack 1×1 deck boxes appear as a second row **IN PACK**. Tap to make Primary (swap). Hold for rename / icon.

### 11.7 Reward / overflow

- Toast if stored: “Blue-Eyes → Travel Case (12 left).”
- Unsecured: warning toast + bag badge with timer.
- Make Room: blocking modal (§7.4).

---

## 12. Sorting and filters

Available on home collection, card-box browser, and binder insert picker:

| Filter | Notes |
|---|---|
| Set | LOB, MRD, … |
| Type | Monster / Spell / Trap + extra kinds |
| Rarity | |
| Name | A–Z |
| Newest | `acquiredUnix` |
| Favorite | Starred |
| Deck-legal | Copies not locked in *other* deck boxes |
| Location | Home box, binder, pack box, unsecured, deck |
| Timer | Unsecured only; soonest expiry first |

---

## 13. Edge cases (player-friendly)

| Situation | Rule | Player language |
|---|---|---|
| Card reward, carried boxes have room | Auto-file Preferred → other boxes | “Stored in [box] · 12 spaces left.” |
| Boxes full, backpack has a free cell | Unsecured 1×1 + timer | “No box space. [Card] is loose in your pack — sleeve it before the timer runs out.” |
| Boxes full **and** backpack full | Hold + Make Room. No silent loss | “Your pack is full. Make a cell for [card], discard something, or refuse the reward.” |
| Timer expires | Card destroyed. Log in activity | “Unsecured [card] was lost.” |
| Move into a box/binder/deck before expiry | Secured; ring vanishes | “Secured in [container].” |
| Return home with loose cards | Auto-file into home boxes; leftover prompt to buy a box | “Welcome home. Loose cards were filed.” |
| Home boxes all full on return | Cards stay Unsecured **without** ticking until you leave again, plus a buy-box prompt | “Home boxes are full. Buy storage before you head out or those loose cards will be at risk.” |
| Change clothes, fewer pockets, pack has 1×1s | Extras move to pack | “Moved [deck] into the backpack.” |
| Change clothes, pack full, away | Block change | “No room for [deck]. Clear a cell or change at home.” |
| Change clothes **at home**, pack full | Extra decks go to home shelf | “Left [deck] at home.” |
| Unequip pocket, pack full, away | Block | “No room for this deck box.” |
| Equip new Primary away | Only swap with on-hand (pocket or pack) | “That deck is at home.” |
| Carry binder / card box | If the tile fits, it is cargo. Cards inside count as on-hand for the editor | “On-hand · [n] cards” |
| Home storage full when opening packs at base | Pack-open Halt: file, buy box, or stop opening | “No space in any card box.” |
| Auto-sort | Rearranges tiles only | “Sorted.” |
| Auto-store | Files Unsecured into boxes | “3 cards sleeved · 1 still loose.” |
| Favorite / Secret about to expire | 60s modal, one snooze | “Secure [card] now — it will be lost in 1:00.” |
| Duel starts with Unsecured Cards | Timers pause | (no toast unless < 10 min left: “Loose cards are paused during the duel.”) |

**Accidental-loss prevention (keeps the pressure):**

- No expiry while a blocking UI is open.
- No expiry mid-duel.
- Secret / Favorite get a modal, not only a toast.
- Make Room cannot be skipped with a mis-tap.
- Decks never become timed loose cards.
- Refusing a reward is always labeled **Refuse**, never “Close.”

---

## 14. Starter kit (target)

- Backpack **4×4**.
- Artifact Deck Box (equipped).
- Primary Deck Box with team starter (Main/Extra/Side).
- Large Card Box **800** at home, containing copies of the starter deck.
- Starter Album **5 pages** at home.
- Outfit: plain tee + plain pants → **1 clothing pocket**.
- Digizeni floor 500.

---

## 15. Suggested copy bank

**Tooltips**

- Primary: “Always equipped. Does not use backpack space.”
- Artifact: “Currencies, artifacts, and key items. Always with you.”
- Pocket: “One complete deck. Sew more pockets at the seamstress.”
- Unsecured: “Not in a box. Store it before the timer ends or it will be lost.”
- Locked cell: “Next expansion — seamstress at the bazaar.”
- Archive box: “4×4 in the pack. Leave it home unless you need the haul.”

**Alerts**

- Full pack: “Backpack full — 16/16.”
- Loose reward: “No box space. [Card] is loose in your pack.”
- Expired: “Unsecured [Card] was lost.”
- Home: “Loose cards were filed.”
- Wardrobe block: “No room for [Deck]. Clear a cell or change at home.”

---

## 16. Implementation mapping (existing code)

| Spec | Today (`PlayerInventory` / bag UI) | Gap |
|---|---|---|
| Tabs CASE / POCKETS / DECKS / HOME | Shipped | Retarget CASE to 4×4→8×6; HOME as collection |
| Card box sizes | 100 / 500 / 1000 | 100 / 400 / 800 / 1600 / 3200 + footprints |
| Backpack tiers | 6×5 … 10×8 | 4×4 … 8×6 |
| Currency pouches on grid | Yes | Move into Artifact Deck Box (0 cells) |
| Primary vs pocket 0 | Pocket 0 *is* primary | Split PRIMARY slot from clothing holsters |
| Unsecured Cards + timers | No | New item kind + reward pipeline |
| Binder front/back browser | No | New sheet |
| Preferred Card Box | No | Flag + auto-place |
| Seamstress expansions | `SetBackpackTier` | Shop row + ghost next size |
| Clothing sew | `MaxSewn` / tailor price | Align copy with tiers in §9 |

Do not invent card effects. `DuelEngine` still owns legality; this spec only moves cardboard.

---

## 17. Acceptance checks (when implementing)

1. Starter 4×4 cannot fit an Archive box (4×4 uses the whole pack).
2. Small 2×2 + deck 1×1 + pack 1×2 fits in 4×4 with leftover cells.
3. Reward with a free carried box never creates Unsecured.
4. Reward with full boxes and a free cell creates Unsecured with a visible timer.
5. Reward with full boxes and full pack opens Make Room; killing the app does not drop the Hold.
6. Favorite Unsecured at 60s opens a modal.
7. Timer does not tick during a duel.
8. Swapping Primary away from home cannot target a home-only deck.
9. Changing to fewer pockets with a full pack is blocked on the street and dumps extras at home.
10. Phone and AR holo both show CASE / POCKETS / DECKS / HOME.
