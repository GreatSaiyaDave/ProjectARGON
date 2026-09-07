# Economy, inventory, progression

In-repo: `SET_ENERGY_AND_BAZAAR.md`, `PROGRESSION_CANON.md`, `INVENTORY_SPEC.md`. Hub Bazaar + spatial altars are **not fully implemented** — do not pretend they are.

## Currencies

| Currency | Role | PvP win |
|---|---|---|
| Set Energy (per set id) | Tablet → 10-card pack of **that set** | **None** |
| Digizeni (Đ) | Soft spend, Rare Hunter fees | Possible (not SE) |
| Duel Coins | Cosmetics / auctions path | — |
| Set orbs | Unlock next ladder set through L50 | Yes (through L50) |

`setEnergyBySet: { "LOB": 40, … }`. Legacy `progress.setEnergy` is a sum until migrated.

Tablet lock (GDD): **1,000 SE of that set = one 10-card pack**. Sacrifice cards → SE is rarity-scaled, max return ≈ half-pack. Consecutive **non-PvP** wins: every 3 wins → 1 pack (design). Do not pay SE for ranked wins.

## Inventory (implemented foundation)

`InventoryService` + `PlayerInventory`. Spec is the design target; code may lag.

| Container | Rule |
|---|---|
| Play deck box | Main / Extra / Side **only**. Never backpack bulk. |
| Avatar pockets | Carry extra deck boxes (default 2, story 3rd, max 6). |
| Card box | Home bulk (100 / 500 / 1000). May pack onto backpack grid. |
| Binder | 18/page, 5 free pages, max 20 pages. |
| Backpack | RE4-style grid. Storage boxes, binders, soul cards. **Not** currencies. |
| Artifact Deck Box | Endless, always equipped, 0 cells. Currencies, ERAZ badges, Tome, keys. |

View-anywhere is GO-style. Remote moves into trades need Trade Transport. Soul cards: timed ghosts when boxes are full (`INVENTORY_SPEC.md`) — do not silently delete without the spec’s aether timer.

BAG opens `InventoryScreen`. Wallet chips open `ArtifactBoxScreen`.

## Progression

- Onboarding: prologue + Kuriboh pick + gifts required. Veterans may skip **only** the Referobot tutorial duel.
- Teams: Galactikuriboh / Kuribandit / Junkuriboh → starter deck files.
- XP: `ProgressionService` + `DuelistXpCurve`. Soft cap 100 until story complete. Master wall at 50+.
- Tome: `level / 10` pages (max 10). Classic S/T **once per turn as if in hand** on Tear boss + Raid only.
- Rare Hunter: steal 1 unused binder card else 250 Digizeni.
- Accounts: `PlayerAccountDatabase` schema v3, local hashed passwords — not a live backend.

## Starter kit

Backpack, Spirit Dueler disk, Tome item, home card box (1000), starter binder, play deck box, Digizeni floor 500 (`StarterKitService`).
