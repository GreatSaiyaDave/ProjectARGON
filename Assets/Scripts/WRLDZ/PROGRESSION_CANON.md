# Progression & inventory canon (implemented foundation)

## Onboarding cascade (`BootFlowUI`)

```
Splash → Credits → Title (touch) → BootLoad
  → DOB? → Auth → Terms?
  → Prologue → Pick Kuriboh → Gift summary → Tutorial ask
  → WorldLoad → Overworld
  (or Practice Duel from tutorial ask)
```

**GDD v1.1:** first-time veterans may skip **only** the Mr. Referobot tutorial duel. Prologue, Kuriboh pick, and gifts are required.

Returning users with `progress.onboardingComplete` skip to WorldLoad after auth/terms.

## Kuriboh teams

| Team | Starter deck file |
|------|-------------------|
| Galactikuriboh | `Decks/starter_galactikuriboh.json` |
| Kuribandit | `Decks/starter_kuribandit.json` |
| Junkuriboh | `Decks/starter_junkuriboh.json` |

## Starter kit (`StarterKitService`)

- Backpack, Spirit Dueler disk, **Tome item**
- **Home Card Box (1000)** — bulk cardboard storage (commons / uncommons / rares)
- **Starter Binder** — 5 free pages (18 cards/page double-sided 3×3); max 20 pages / 360 cards
- **Play Deck Box** + starter deck (Main/Extra/Side)
- Starter deck copies also land in the home card box
- Digizeni floor 500

## Inventory (realistic collection)

**Design target:** `INVENTORY_SPEC.md` (home vs on-hand, 4×4→8×6 pack, Unsecured Cards, PRIMARY vs pockets). Table below is the **currently implemented** foundation until that spec is coded.

| Container | Role | Capacity / rules |
|-----------|------|------------------|
| **Card box** | Home bulk storage (cardboard) | **100 / 500 / 1000** (starter: one × 1000). Optional pack into backpack (footprint by size). |
| **Binder** | Organized display / sort | **18/page** (9 front + 9 back), **5 free pages**, max **20 pages = 360**. Can pack (2×2). |
| **Play deck box** | Constructed decks for duels | Main / Extra / Side **only**. **Never** bulk storage. **Never** backpack grid. |
| **Avatar pockets** | Carry play decks while traveling | Default **2** carry slots; story unlock **3rd**; purchase more (max 6). Outfits ≥1 pocket. |
| **Backpack** | Travel pack (RE4-style grid) | Story unlock + tier growth. No currency pouches. Soul cards occupy 1×1 when boxes are full. |
| **Artifact deck box** | Key-item artifact cards | Endless, always equipped, 0 cells. Currencies, ERAZ badges, Tome, Trade Transport, Millennium, story keys. |
| **Trade transport** | Remote trade move | View inventory anywhere (GO-style); **cannot** teleport home cards into trades without this item. |

### Travel vs home (life-like)

- Like life: only take what fits in pack / pockets when you leave home.
- **CASE** tab (`InventoryScreen`): tetris backpack. Storage boxes / binders pack onto the grid. Artifact Deck Box is never a backpack tile.
- **HOME** tab: glance home boxes / binders / trade transport. Artifact Deck Box sits under **ON YOU** (always equipped).
- **Pockets** tab: assign deck boxes to outfit pockets (default deck in pocket 0).
- Decorative storage box skins: unlock **1 of each** per account (collectible; later).
- Home-base 3D placement: deferred.

### Code

- Model: `PlayerInventory` · `BackpackState` · `AvatarPocketState` · `ArtifactDeckBoxState`
- Pack/carry/view: `InventoryService`
- Shop (boxes/binders/pages): `InventoryShopService`
- UI: `InventoryScreen` (MenuId.Inventory) · hub / Eye **BAG** opens it · `ArtifactBoxScreen` (`MenuId.Artifacts`) from wallet

## Tome (`TomeService`)

- **Separate deck** — real TCG S/T IDs as spell-book pages
- **Raid only**
- Capacity = `level / 10` (max 10 at L100); 0 before L10
- Early path seeds 3 classic S/T per Kuriboh team at L10/20/30

## Level / XP (`ProgressionService` + `DuelistXpCurve`)

Pokémon GO–paced **Duelist Level**:

| Band | Levels | Feel |
|------|--------|------|
| Rookie → Elite | 1–49 | Fast early, steady mid (GO 1–40 energy) |
| **Master wall** | **50+** | **Hard slowdown** — many duels per level |
| Soft cap | 100 | Until story complete |

- Soft cap **100** until story complete (`PlayerProgress.SoftLevelCap`)
- XP primarily from duels; **practice = 0 XP**
- Desktop Lab / LabTest = small XP (smoke-test leveling)
- Win awards more than loss; longer duels slight bonus
- Level-up grants **Digizeni** (milestone bumps at ×10 and L50/L100)
- `spiritRank` mirrors level
- UI: overworld XP bar under trainer orb; duel end overlay shows `+XP` / LEVEL UP

Curve code: `Data/DuelistXpCurve.cs` · awards: `ProgressionService.AwardDuelRewards`

## Rare Hunters (`RareHunterService`)

1. Steal **1 random unused binder card** (copies not locked in deck boxes)
2. Else **250 Digizeni** (or remaining balance)

## Account schema

`PlayerAccountDatabase` schema **v3**: nested `progress` + `inventory` on account JSON.

## Hub

- **Backpack & Inventory** — opens dual Pack / View / Pockets screen (fallback toast if no MenuShell)  
- **Tome (Raid)** — capacity / unlocked / equipped  

## Set Energy · stone tablets · Bazaar (design lock)

Full deep dive: **`SET_ENERGY_AND_BAZAAR.md`** (Blueprint v1.1 reconciled).

| Rule | Canon |
|------|--------|
| **Primary sink** | SE of set X → **stone tablet** → **10-card pack** of set X |
| Story SE | Low **random**; set tags follow **story position** |
| CPU / campaign | **Larger** SE of the **opponent deck’s set(s)** |
| Walk / tears / raids | Blueprint faucets (caps); still set-tagged |
| PvP | **No SE** |
| Bazaar | **Pure economic zone** at **partner shops / businesses** |
| Altars | Cards → SE of card’s set (max ~half-pack) |
| NPCs | Dynamic shopkeepers + **Witty Phantom** auctions |
| S1–4 / S5+ | Era lock → Extra Monster mayhem |

UI stub: hub **Bazaar**. Spatial pins = partner geofences.

## Still later

- Home-base 3D storage placement  
- Full binder UI, deck builder editor  
- Decorative unique storage boxes (1 of each / account)  
- Raid duel mode wiring Tome equipped pages into engine  
- Trivia quizzes every 10 levels  
- Physical deck scan  
- Zone Mode / possession AR  
- Per-set SE balances + altar convert service + story SE grant table  

