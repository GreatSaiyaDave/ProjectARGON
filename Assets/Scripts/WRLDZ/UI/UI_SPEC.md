# WRLDZ / Project ARGON — Production UI Specification

**Status:** Production-ready design authority for menus, chrome, and readability.  
**Integrates with:** `FLOW.md`, `PRODUCT_VISION.md`, `PROGRESSION_CANON.md`, `AR_DUEL_PRESENTATION.md`.  
**Visual tokens:** `DuelystUi`, `WrldzTheme`, `WrldzType`, `FlowChrome`.  
**Code shell:** `UI/Shell/` (`MenuId`, `MenuShell`, `HudTokens`, `ScreenRouter`).

---

## 1. Design principles

| Principle | Rule |
|-----------|------|
| **Readable first** | Pure white / soft gold on near-black. Never gray-on-gray. |
| **Minimal chrome** | Floating translucent panels, soft glow, thin luminous borders only. |
| **One job per screen** | No overlapping modals unless confirmation. |
| **One-handed** | Primary actions in lower 40% or right-thumb zone; left arm may hold disk. |
| **Outdoor legible** | Min body **16** design px (`WrldzType.MinBody`); heavy outline + cyan soft shadow. |
| **Dual presentation** | Every surface has **Non-AR** (portrait canvas) and **AR** (disk-anchored holo) variants. |
| **Engine authority** | UI never invents game state; reads `DuelEngine` / `PlayerProgress` only. |

### 1.1 Color tokens (premium dark anime)

| Token | Hex / role | Use |
|-------|------------|-----|
| `Void` | `#05070F` | Full-screen base |
| `Panel` | `#0C1220` @ 92–96% | Floating sheets |
| `TextPrimary` | `#FAF8F4` | Titles, values |
| `TextGold` | `#FFD647` | LP, currency, phase |
| `TextMuted` | `#B8CEE8` | Secondary labels only (still high contrast) |
| `Cyan` | `#33EBFF` | Player, holos, primary CTA glow |
| `Magenta` | `#FF4798` | Opponent, alerts |
| `Ok` | `#3CFF8C` | Confirm |
| `Danger` | `#FF4455` | Destructive / End Turn |
| `Border` | Cyan or gold @ 55–85% | 2–3 px luminous edge |

### 1.2 Typography

| Role | Font | Design size (pre-scale) | Notes |
|------|------|-------------------------|--------|
| Display / shout | Bangers → Rowdies | 20–32 | Phase, win, format titles |
| UI button | Russo One / Exo 2 Bold | 17–20 | Never thin weight |
| Body / labels | Exo 2 Bold | ≥16 | Status, card names |
| Micro (avoid) | — | — | Forbidden for critical data |

`WrldzType.Scale = 1.42` · always `heavyOutline` on HUD.

### 1.3 Spacing & touch

| Metric | Value |
|--------|-------|
| Min touch target | **48×48** dp (prefer **56** height for primary buttons) |
| Panel padding | 16–24 px |
| Gap between primary actions | ≥12 px |
| Safe bottom (home bar / disk) | 8% of screen height free |
| Safe top (notch) | 4% free |

### 1.4 Motion

| Transition | Duration | Rule |
|------------|----------|------|
| Panel open | 180–220 ms | Fade + 8 px rise; never block input mid-open longer than 220 ms |
| Toast | 2.0 s | Single line, bottom or top center |
| Holo pop (LP / ATK) | 120 ms in / 400 ms out | Auto-dismiss; no stack > 2 |
| Screen change | Crossfade 200 ms | Keep shared HUD where possible |

---

## 2. Information architecture (complete menu graph)

```
Boot (entry only)
  Splash → Title
    → DOB? → Auth → Terms?
    → Prologue → Kuriboh pick → Gifts → Tutorial?
    → Overworld ════════════════════════════════════ HOME

Overworld (Home / Overmap)
  ├─ Tear / Zone pin ──► Zone Mode prompt ──► AR Duel | Digital Duel
  ├─ PRACTICE ─────────► DuelSlice (quick)
  ├─ [DECK] ───────────► Deck & Collection Hub
  ├─ [BAG] ────────────► Inventory / Backpack
  ├─ [STORY] ──────────► Story & Season
  ├─ [⚙] ──────────────► Settings
  ├─ Profile orb ──────► Avatar / Profile
  └─ MENU (center) ────► Systems Hub (MainMenu)

Systems Hub (MainMenu — not home)
  ├─ Shadow / Format Select
  ├─ Deck & Collection
  ├─ Inventory
  ├─ Tome (Raid)
  ├─ Bazaar / Energy
  ├─ Story & Season
  ├─ Profile / Avatar
  ├─ Settings
  └─ ← MAP ────────────► Overworld

DuelSlice / Zone Mode
  ├─ Live duel (minimal HUD)
  ├─ Disk holo menus (pause / inventory peek)
  └─ Map ──────────────► Overworld
```

Scene map (existing Unity scenes):

| Scene | Spec screens |
|-------|----------------|
| `Boot` | Onboarding cascade |
| `Overworld` | §3 Overmap Home |
| `MainMenu` | §4 Hub + routes to Deck/Inventory/Story |
| `DuelSlice` | §5 Zone Mode / Duel |

---

## 3. Screen specs

### 3.1 Overmap / Home

**Purpose:** GPS living world; default state after login.

#### Layout (portrait 1080×2340 ref)

```
┌─────────────────────────────────────┐
│  [Lv·Orb]  Digizeni  Coins  Energy  │  ← top HUD strip (translucent)
│  [Navi face]              [⚙] [Bag]│
│                                     │
│         GPS MAP (full bleed)        │
│     pins: Tear · Portal · NPC ·     │
│           Treasure · Event          │
│              [Avatar fixed]         │
│                                     │
│  Nearby: 3 lines max                │
│                                     │
│  [DECK]  [BAG]  [● MENU]  [STORY]   │  ← bottom chrome (one-hand)
└─────────────────────────────────────┘
```

#### HUD tokens (always visible, single row)

| Token | Source | Display |
|-------|--------|---------|
| Level | `progress.level` | `Lv##` gold |
| Digizeni | `inventory.digizeni` | icon + number |
| Duel Coins | `inventory.duelCoins` | icon + number |
| Set Energy | `inventory.setEnergy` | icon + number / max |
| Kuriboh | `progress.Team` | small face + name |

#### Map icons (non-overlapping)

| Kind | Glyph | Color accent | Action |
|------|-------|--------------|--------|
| Tear | rift diamond | Magenta | Zone Mode prompt |
| Portal | arch | Cyan | Season / portal window |
| NPC | figure | Gold | Dialogue stub |
| Treasure | tablet shard | Gold | Loot toast |
| Event | star | Cyan | Challenge entry |
| Anchor | node | Soft cyan | Leyline toast |

**Rules:** pin collision resolve by priority Tear > Event > Portal > NPC > Treasure; cluster expand on zoom.

#### Zone Mode prompt (modal)

```
┌─ ZONE MODE ─────────────────────┐
│  Tear · Neon Alley              │
│  Clear space · enter AR arena   │
│  [ ENTER AR ]   [ DIGITAL ]     │
│  [ Cancel ]                     │
└─────────────────────────────────┘
```

Primary CTA = Enter AR (cyan). Digital = same duel, non-AR layout.

---

### 3.2 Deck & Collection Hub (Neuron-style)

**Entry:** Overmap DECK · Hub “Deck Builder” · Disk AR panel.

#### Tabs (exact order)

1. **Main Deck**  
2. **Extra Deck**  
3. **Side Deck**  
4. **Tome Deck** (raid-only S/T pages)  
5. **Binder**  
6. **Trade**

#### Layout

```
┌─ COLLECTION ────────────── [×] ─┐
│ MAIN │ EXTRA │ SIDE │ TOME │ …  │  ← scroll tabs if needed
├──────────────┬──────────────────┤
│              │  Card preview    │
│  Grid/list   │  Name            │
│  of cards    │  ATK / DEF       │
│              │  Type · Rarity   │
│              │  [Add] [Remove]  │
├──────────────┴──────────────────┤
│ 🔍 Search          [Filter] [↕] │  ← collapsed until tapped
│ Count 40/60 · Value est. ####   │
└─────────────────────────────────┘
```

#### Card cell (min 96×132)

- Frame from `YgoCardFrames`  
- Nameplate high contrast  
- Rarity: N / R / SR / UR as gold pip count (not color-only)  
- ATK/DEF always visible on monsters when face shown  

#### Trade (two-step)

1. Select offer cards + request cards → **Review**  
2. Summary: both sides + value estimate → **Confirm trade** / Cancel  

Value estimator: sum rarity weights + binder market stub (display-only until economy live).

#### Tome Deck rules (canon)

- Real S/T card IDs as “pages”  
- Capacity `level/10` (max 10)  
- Raid format only — show lock badge outside Raid  

---

### 3.3 Zone Mode & AR Duel Disk

**Principle:** Cinematic empty stage. UI is glanceable, not permanent chrome.

#### Live duel HUD (maximum allowed)

| Element | Placement | Behavior |
|---------|-----------|----------|
| LP you / opp | Top corners | Always on; large digits |
| Phase pill | Top center | Gold; single line |
| Hint | Below phase | Cyan; one sentence |
| Hand | Bottom tray | Cards only; no chrome walls |
| Phase CTAs | Bottom (thumb) | Battle / Main2 / End when legal |
| Log | Optional thin strip | Last 1–2 events |

**Forbidden during active duel:** full settings sheets, dense binder grids, multi-column debug.

#### Eye-tracked / focus pop-ups (AR)

| Trigger | Content | Lifetime |
|---------|---------|----------|
| Gaze / long-press monster | Name · ATK/DEF · position | 2.5 s |
| Gaze LP orb | Exact LP | 1.5 s |
| Select card | Full effect text panel | Until dismiss or 8 s |
| Illegal action | Red toast | 2 s |

#### Disk-anchored secondary menus (AR variant)

Locked to **left forearm** disk local space:

- Pause / Concede  
- Quick inventory peek (read-only)  
- Pass response / phase shortcuts  

Non-AR: same actions as bottom sheet over portrait duel.

---

### 3.4 Bazaar, Progression & Economy

#### Currencies (always labeled with icon + name)

| Currency | Use |
|----------|-----|
| **Digizeni** | Soft premium / Rare Hunter fee |
| **Duel Coins** | Packs, cosmetics |
| **Set Energy** | **Set-tagged** collection fuel — see `SET_ENERGY_AND_BAZAAR.md` (story / CPU / altars; **not** PvP) |

#### Stone tablet pack open

```
1. Closed tablet (single hero object)
2. Crack → glow (short)
3. Fan 1–N cards large, sequential reveal
4. [ Collect ] only — no auto-skip of first SR+
```

No particle spam that covers names.

#### Challenges

- Daily: 3 rows max  
- Weekly: 3 rows max  
- Each: title · progress bar · reward icon  

#### Level-up

- Full-bleed dark panel  
- `LEVEL ##` gold display  
- Rewards list (≤5 lines)  
- Trivia every 10 levels → quiz modal (4 choices, large)  

---

### 3.5 Format & Story Select

#### Format cards (large, full-width list or 1×N scroll)

| Format | Rules summary on card | Unlock |
|--------|----------------------|--------|
| **Quick Duel** | Practice AI · standard LP | Always |
| **Shadow Duel** | Local / ranked stub · 8000 LP | Always |
| **Duelist Kingdom** | Story LP / field rules | Story progress |
| **Raid** | Tome Deck enabled | Level ≥ 10 |

Each card shows: title, 2-line rules, LP, **LOCKED** or **PLAY**.

#### ERAZ badge tray (TCG formats only)

- Not a separate format row. Shown on TCG format sheets (`pvai`, `quick`, etc.) via `ErazFormat.ShowsBadgeTray`.
- Hidden for `ddm`, `genesys`, `raid`, `speed`, `deckmaster`.
- One-line status under the hint: **ERAZ · Original** when the account owns the Original badge; otherwise **ERAZ · LOCKED — finish tutorial**.
- Practice / tutorial Start still works without a badge. Hub non-practice Start requires the Original badge.

#### Story / Season

- Linear chapter list (readable titles)  
- Season progress bar separate visual (gold) from competitive rank (cyan)  
- Portal windows: date range + remaining days  

---

### 3.6 Inventory, Avatar & Settings

#### Inventory (Backpack)

Shipped tabs: **CASE · POCKETS · DECKS · HOME** (`INVENTORY_SPEC.md` §11.1). Wallet chips open the Artifact Deck Box.

Older draft tabs (not built): **Items** · **Binders** · **Deck Boxes** · **Cosmetics** · **Placeables**  

- Row: icon · name · qty · [Use]  
- AR base of operations: list of placeables → **Place in AR** (drag gizmo, confirm)  

#### Avatar

- Live portrait center  
- Parts: body, hair, eyes, outfit, accessory  
- Colors: skin, hair, outfit, accent  
- Title picker  
- **Save look** primary  

#### Settings (essentials only)

| Toggle | Default | Notes |
|--------|---------|-------|
| AR quality | High | Low / Med / High |
| Battery saver | Off | Caps FPS, dims neon, simplifies VFX |
| Eye-tracking sensitivity | Med | AR pop-up aggressiveness |
| Right-arm disk support | Off | Mirrors disk to right forearm |
| High contrast text | On | Forces cream + heavier outline |
| Master volume | 80% | Single slider |

No nested “advanced” labyrinth. One screen, scroll if needed.

---

## 4. Component library (modular)

### 4.1 Hierarchy

```
MenuShell (canvas + safe area + battery-saver overlay)
  ├── HudBar          // currencies + level + navi
  ├── ContentHost     // active screen root
  ├── BottomNav       // 4–5 primary destinations
  ├── ModalHost       // confirmations, zone prompt
  └── ToastHost

Screens (IMenuScreen)
  OvermapScreen
  DeckCollectionScreen
  ZoneDuelHudScreen
  BazaarScreen
  FormatSelectScreen
  StorySeasonScreen
  InventoryScreen
  AvatarScreen
  SettingsScreen
  HubScreen
```

### 4.2 Prefab / runtime builders (code)

| Builder | Responsibility |
|---------|----------------|
| `MenuShell` | Portrait canvas, safe area, router host |
| `HudTokens` | Level, Digizeni, Coins, Energy, Kuriboh |
| `FloatingPanel` | Translucent panel + luminous border |
| `PrimaryButton` | 56h min, gold/cyan variants |
| `IconButton` | 56×56, label under optional |
| `CardPreviewCell` | Large preview + stats |
| `FormatSelectCard` | Format rules + lock |
| `CurrencyChip` | Icon + value |
| `ConfirmTwoStep` | Review → Confirm |

### 4.3 Presentation modes

```csharp
enum UiPresentation { NonArPortrait, ArDiskHolo, ArWorldPanel }
```

- `NonArPortrait` — default S23 / Editor  
- `ArDiskHolo` — panels parented to left-arm disk rig  
- `ArWorldPanel` — rare world-locked prompts (zone entry)  

Same `MenuId` + intents; only layout transform changes.

---

## 5. Navigation intents

| Intent | From | To |
|--------|------|-----|
| `GoHome` | Anywhere | Overworld |
| `OpenDeck` | Home / Hub | DeckCollection |
| `OpenInventory` | Home / Hub | Inventory |
| `OpenStory` | Home / Hub | StorySeason |
| `OpenSettings` | Home / Hub | Settings |
| `OpenHub` | Home MENU | Hub |
| `OpenFormatSelect` | Hub | FormatSelect |
| `StartQuickDuel` | Format / Practice | DuelSlice |
| `EnterZoneAr` | Tear prompt | DuelSlice + AR |
| `EnterZoneDigital` | Tear prompt | DuelSlice non-AR |
| `OpenBazaar` | Hub | Bazaar |
| `OpenAvatar` | Profile | Avatar |
| `LeaveDuel` | Duel | Overworld |

Implement via `AppSession` + `ScreenRouter` (do not hard-wire random `SceneManager` calls inside deep widgets).

---

## 6. Accessibility & outdoor / battery

| Mode | Changes |
|------|---------|
| **Default** | Full neon, 60 FPS target |
| **High contrast** | Text forced cream; borders 100% opacity; larger type |
| **Battery saver** | 30 FPS, disable particles, solid panels (no blur), reduce glow alpha 50% |
| **Outdoor** | Always: outlines on; never pure cyan text on cyan glass |

---

## 7. Implementation map (existing project)

| Spec area | Existing code | Gap / next |
|-----------|---------------|------------|
| Overmap HUD | `OverworldUI` | Align currency chips + 4 bottom buttons |
| Hub | `DuelDiskMenuUI` | Expand rows to match §2–6 routes |
| Duel HUD | `DuelUI` + `ArDuelSpace` | Keep minimal; hide nonessential mid-combat |
| Deck tabs | Live dual (Phone + AR holo) | `DeckCollectionScreen` + `DualMenuPresenter` |
| Avatar | `AvatarCustomizerUI` | Already; restyle to FloatingPanel |
| Boot | `BootFlowUI` | Keep cascade; apply shell tokens |
| Theme | `DuelystUi` / `WrldzType` | Tokens locked in this spec |

---

## 8. Acceptance checklist

- [ ] No critical text smaller than comfortable reading size on 1080×2340  
- [ ] All primary buttons ≥ 56 px height  
- [ ] Overmap: 5 HUD currencies/companion visible without opening a menu  
- [ ] Deck: 6 tabs present; Trade is two-step  
- [ ] Duel: no full binder UI during active phase  
- [ ] Settings: only the essential toggles listed  
- [ ] Battery saver visibly simplifies neon without hiding LP  
- [ ] AR and Non-AR share the same intents  

---

## 9. Visual reference (in-engine)

```
Background:  Battle City night void
Panel:       glass navy + 2px cyan/gold edge
Title:       soft gold display face
Body:        cream Exo 2
Accent CTA:  cyan you / magenta opponent / gold phase
Icons:       monochrome line + single accent fill
Duel HUD:    YOU LP left · PHASE center · OPP LP right
```

**Not this product:** dense Master Duel deck grids on the map, GBA brick spam, low-contrast gray icons, simultaneous multi-modal stacks.

---

*End of UI Specification — Project ARGON / Duel Monsters: WRLDZ*
