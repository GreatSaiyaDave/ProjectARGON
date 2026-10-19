# App flow — menu structure (canon)

Single navigation graph. Implemented in `AppSession` + scene UIs.  
**Full UI production spec:** [`UI/UI_SPEC.md`](UI/UI_SPEC.md) · **Shell code:** `UI/Shell/`.

```
Boot  (scene 0 — entry only)
  **Editor / Desktop Lab** (no phone, Quest, or GPS):
    → WRLDZ → Lab → Open Desktop Lab App  (or Auth DESKTOP LAB button)
    → Desktop Lab hub (see DESKTOP_LAB.md)  — Play on Boot alone is splash/title
         ├─ QUICK AR DUEL (SIM) → DuelSlice EditorSim
         ├─ INSTANT DUEL        → DuelSlice hands drawn
         ├─ OVERWORLD (WASD)    → map without GPS
         ├─ SYSTEMS HUB         → MainMenu
         └─ ENGINE STRESS       → headless AI-vs-AI
  (Lab doors — Desktop Lab, TEST DUEL, admin grants — exist only in the Editor and Development
   builds: WrldzLab.LabDoorsEnabled = UNITY_EDITOR || DEVELOPMENT_BUILD. Owner rule 2026-09-23 #5.)
  Full cascade (device / release builds; v6.3 boot, BUILD_PLAN P2):
  Genesis (the approved cut, a local VP8 clip; TAP TO SKIP = a 0.4 s dip) → Title (live: the Tears, TOUCH TO BEGIN)
    → returning player (signed in, terms accepted, onboarding complete): straight into MainMenu (the D8 handoff:
      the title canvas kept under the rising menu for 1.5 s)
    (legacy fallback — no v6 title data, kill switch WRLDZ_MenuV6Off, or a build error: Splash → Credits → Title as before)
    → BootLoad (session / age / terms check)
    → DOB?          [if age gate not passed]
    → AuthChoice    [if not logged in]
         ├─ CREATE ACCOUNT
         ├─ LOG IN
         ├─ DESKTOP LAB (NO EQUIPMENT) → Desktop Lab hub          [Editor / Development only]
         └─ TEST DUEL (LAB) → DuelSlice (lab_tester, starter vs Kaiba; Map → Boot)  [Editor / Development only]
         ├─ Create Account → Terms → Onboarding*
         └─ Log In         → Terms? → Onboarding* or WorldLoad
    → Terms?        [if logged in but terms not accepted]
    → Onboarding* (first-time): Prologue → Pick Kuriboh → Gifts → Tutorial ask
      (GDD v1.1: veterans may skip **only** the Referobot tutorial duel)
    → WorldLoad → MainMenu (Duel Disk — Eldritch Solid Vision) → WORLD MAP → Overworld
  * skipped when progress.onboardingComplete (see PROGRESSION_CANON.md)

Overworld  (scene 1 — HOME / default)
  VS AI        → Player vs AI create (STANDING / TABLE / STREET / PRACTICE) → START → DuelSlice (AR, vs AI)
  VS PVP       → distance scan (mark P1 + P2) → START → DuelSlice (AR hotseat)
  Dock         → BAG and STORY orbs flank the Millennium Eye; the deck orb opens DECK.
  MENU (Eye)   → Battle City hub grammar on the map (same capsules as MainMenu), exactly 8 tiles
                 (OverworldUI KcTile list): VS AI · VS PLAYER / DECK · BAG · STORY /
                 BAZAAR · TOME · SET. There is no PRACTICE, TRADE or TOURNEY tile (removed 2026-09;
                 PRACTICE is a card inside VS AI). Destination sheets open as HubChrome overlays
                 (DECK uses DualMenuPresenter). Wallet chips (Đ / ◎ / ⚡) open Artifact Deck Box.
                 Optional full hub scene: Desktop Lab SYSTEMS HUB → MainMenu.
  Tear pin     → Zone Mode prompt (in-range only)
                 ├─ ENTER AR  → DuelSlice (camera passthrough when available)
                 ├─ DIGITAL   → DuelSlice (same stage, no camera)
                 └─ CANCEL    → stay on map
  PRACTICE     → via VS AI → PRACTICE card → DuelSlice (AR PvAI practice, any range, no rewards)
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
  Leaving early (owner rule 2026-09-23 #3): in a rewarded duel, Map / Leave asks for confirmation and
    then records a FORFEIT LOSS (loss rewards, streak reset, Shadow Game soul stakes apply —
    ProgressionService.ForfeitDuel via AppSession.ForfeitUnfinishedDuel). Restart (↻) exists only in
    practice / lab duels. A finished duel's rewards are claimed exactly once (DuelResultId).
  Lab TEST DUEL: Map → Boot
```

## Scene roles

| # | Scene | Role |
|---|--------|------|
| 0 | `Boot` | Onboarding only. Hands off to `MainMenu` (Duel Disk). Never a mid-game destination except after logout. |
| 1 | `Overworld` | **Home.** Battle City map + avatar. Profile orb → avatar customizer. |
| 2 | `MainMenu` | Duel Disk main menu (Eldritch Solid Vision); first scene after Boot. DUEL · DECK · BAG · STORY · BAZAAR · VIEW · SET · WORLD MAP · QUIT. See `UI/MENU_MAIN.md`. |
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
2. Play **Boot** → Genesis → title → DOB (if needed) → Create → **Terms** → load (a 0.3 s fade) → MainMenu (its cold open).
3. Map: **Eye (MENU)** → a destination tile (DECK, BAG, …) → close the sheet to return to the map.
4. Tear pin, or **VS AI → PRACTICE** → duel → **Map** → overworld (a rewarded duel left early is a forfeit).
5. **QUIT** logs out → Boot. Second launch auto-logs in if session exists.

## Reset onboarding

Delete `wrldz_db/` under persistent data, or Overworld **QUIT** then clear app data.
