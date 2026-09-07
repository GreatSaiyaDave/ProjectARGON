# App flow — menu structure (canon)

Single navigation graph. Implemented in `AppSession` + scene UIs.  
**Full UI production spec:** [`UI/UI_SPEC.md`](UI/UI_SPEC.md) · **Shell code:** `UI/Shell/`.

```
Boot  (scene 0 — entry only)
  **Editor / Desktop Lab** (no phone, Quest, or GPS):
    → Desktop Lab hub auto-opens (see DESKTOP_LAB.md)
         ├─ QUICK AR DUEL (SIM) → DuelSlice EditorSim
         ├─ INSTANT DUEL        → DuelSlice hands drawn
         ├─ OVERWORLD (WASD)    → map without GPS
         ├─ SYSTEMS HUB         → MainMenu
         └─ ENGINE STRESS       → headless AI-vs-AI
  Full cascade (device / when lab disabled):
  Splash (~1s) → Title (touch to begin)
    → BootLoad (session / age / terms check)
    → DOB?          [if age gate not passed]
    → AuthChoice    [if not logged in]
         ├─ CREATE ACCOUNT
         ├─ LOG IN
         ├─ DESKTOP LAB (NO EQUIPMENT) → Desktop Lab hub
         └─ TEST DUEL (LAB) → DuelSlice (lab_tester, starter vs Kaiba; Map → Boot)
         ├─ Create Account → Terms → Onboarding*
         └─ Log In         → Terms? → Onboarding* or WorldLoad
    → Terms?        [if logged in but terms not accepted]
    → Onboarding* (first-time): Prologue → Pick Kuriboh → Gifts → Tutorial ask
      (GDD v1.1: veterans may skip **only** the Referobot tutorial duel)
    → WorldLoad → Overworld
  * skipped when progress.onboardingComplete (see PROGRESSION_CANON.md)

Overworld  (scene 1 — HOME / default)
  VS AI        → Player vs AI create → START → DuelSlice (AR, vs AI)
  VS PVP       → distance scan (mark P1 + P2) → START → DuelSlice (AR hotseat)
  MENU (Eye)   → Battle City hub grammar on the map (same capsules as MainMenu):
                 DUEL: VS AI / VS PLAYER; COMMAND: DECK / BAG / STORY / BAZAAR / TOME / SET;
                 extra row PRACTICE / TOURNEY. Destination sheets then open as HubChrome overlays
                 (DECK uses DualMenuPresenter). Wallet chips (Đ / ◎ / ⚡) open Artifact Deck Box.
                 Optional full hub scene: Desktop Lab SYSTEMS HUB → MainMenu.
  Tear pin     → Zone Mode prompt (in-range only)
                 ├─ ENTER AR  → DuelSlice (camera passthrough when available)
                 ├─ DIGITAL   → DuelSlice (same stage, no camera)
                 └─ CANCEL    → stay on map
  PRACTICE     → DuelSlice (AR PvAI practice, any range)
  Anchor pin   → toast only (stub)
  QUIT         → Logout → Boot

MainMenu  (scene 2 — hub / systems)
  Optional deep hub (Desktop Lab SYSTEMS HUB). Live overworld systems stay as overlays.
  Player vs AI     → create / formats → DuelSlice (AR vs AI, separation meters)
  Player vs Player → auto distance scan → DuelSlice (AR hotseat, no AI)
  DECK / BAG / STORY / BAZAAR / VIEW / SET. Tome is Eye + AR SYS, not a hub tile.
  Artifact Deck Box is nested under BAG (wallet / ON YOU), not a hub tile.
  ← MAP → Overworld

DuelSlice  (scene 3 — always AR stage)
  Requires login · PendingArMatch sets disk separation
  NearbyPeer / LocalPass → both humans (hotseat); AiLocal → SimpleAi
  Map button / exit → Overworld (home)
  Lab TEST DUEL: Map → Boot
```

## Scene roles

| # | Scene | Role |
|---|--------|------|
| 0 | `Boot` | Onboarding only. Never a mid-game destination except after logout. |
| 1 | `Overworld` | **Home.** Battle City map + avatar. Profile orb → avatar customizer. |
| 2 | `MainMenu` | Systems hub (not home). |
| 3 | `DuelSlice` | TCG vertical slice. Exit → Overworld. |

## Local accounts

- Path: `Application.persistentDataPath/wrldz_db/`
  - `meta.json` — age gate + terms
  - `session.json` + PlayerPrefs blob — active session
  - `accounts/<user>.json` — one file per account
- Passwords hashed (SHA-256) on device — **not** a real online service yet
- Facade: `LocalAccountStore` → `PlayerAccountDatabase`

## Gates

| Destination | Requirement |
|-------------|-------------|
| Overworld / MainMenu / Duel | `AppSession.IsLoggedIn` else → Boot |
| Boot cascade Terms | After create always; after login only if not yet accepted |
| Age DOB | Once per device (`meta.ageGatePassed`) |

## How to test

### Equipment-free (recommended for PC / Editor)

1. Open **Boot** → Play — **Desktop Lab** opens automatically.
2. Or menu: **WRLDZ → Lab → Open Desktop Lab App**.
3. Details: [`DESKTOP_LAB.md`](DESKTOP_LAB.md).

### Full cascade / device

1. Build Settings: **Boot** first (0), then Overworld, MainMenu, DuelSlice.
2. Play **Boot** → splash → DOB (if needed) → Create → **Terms** → load → map.
3. Map: **HUB** → systems grid → **MAP** back.
4. Tear pin or **PRACTICE** → duel → **Map** → overworld.
5. **QUIT** logs out → Boot. Second launch auto-logs in if session exists.

## Reset onboarding

Delete `wrldz_db/` under persistent data, or Overworld **QUIT** then clear app data.
