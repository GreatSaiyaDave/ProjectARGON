# Desktop Lab — test WRLDZ without equipment

No **Quest**, **S23**, **GPS**, or **camera** required.

The **Desktop Lab** is a full-screen hub for PC / Unity Editor Play Mode.

---

## How to launch

### Unity Editor (recommended)

1. Top menu **`WRLDZ → Lab → Open Desktop Lab App`** → **Play Boot + Lab**
2. That sets skip-splash and opens Desktop Lab. Proof line: **BUILD PLATES-0907**
3. **OVERWORLD (WASD MAP)** → tap the Eye → DECK / BAG (runtime HubChrome plates)

Playing the Boot scene with the Play button alone shows splash / **TOUCH TO BEGIN**, then auth. Use **DESKTOP LAB (NO EQUIPMENT)** there if you did not use the Lab menu.

**Do not** type git commands into the Unity Console. See repo-root `GET_THE_GAME.txt`.

### Boot auth screen (device or Editor)

**DESKTOP LAB (NO EQUIPMENT)** on the account / auth choice screen.

### Standalone PC build

```bash
# Optional flag
YourGame.exe -desktopLab
```

Or set PlayerPrefs `WRLDZ_DesktopLab = 1`.

---

## Hub actions

| Button | What it tests |
|--------|----------------|
| **QUICK AR DUEL (SIM)** | EditorSim dual disks + midfield arena · vs AI · pre-duel (unless skipped) |
| **INSTANT DUEL** | Same, hands already drawn (fastest) |
| **OVERWORLD (WASD MAP)** | Battle City map · walk with **WASD** · Tears · Zone Mode |
| **SYSTEMS HUB** | Menu / profile / settings shell |
| **ENGINE STRESS (10)** | Headless rules AI-vs-AI (Console report) |
| **OCG lab duel** | `AppSession.EnterTestDuel("ocg_lab_player.json", "ocg_lab_ai.json")` — viewer over **native ocgcore** when preflight fingerprint matches; **stub tape fallback** if preflight failed or is stale. Tape is not the rules oracle. See `Duel/OCGCORE_LAB.md`. |

### Options

- **Skip pre-duel cinematic** — ON = no deploy/shuffle/draw gesture  
  Also: `WRLDZ → Lab → Toggle Skip Pre-Duel Cinematic`

---

## Controls (equipment-free)

| Where | Input |
|-------|--------|
| Overworld | **WASD** or on-screen pad |
| Pre-duel draw | **DRAW HAND** button · **Space** · click near DECK |
| Duel | 2D hand + phase buttons (same as phone UI) |
| Leave duel | **Map** → Overworld (or Boot if Test Duel mode) |

**Game view:** set aspect **1080 × 2340** (portrait) for real layout.

---

## What is simulated vs real

| System | Desktop Lab |
|--------|-------------|
| Rules engine | **Real** |
| AI opponent | **Real** |
| Dual disks + arena (RT) | **Simulated** (EditorSim) |
| GPS map | **Simulated** (WASD meters) |
| Phone camera passthrough | **Not used** (`PreferDigital`) |
| Quest arm tracking | **Not available** (use Quest build) |

You can fully validate: flow, rules, AI, UI, pre-duel, 2D↔field sync, map pins.  
You **cannot** fully validate: real arm anchoring, life-size meters, outdoor GPS accuracy.

---

## Account

Auto-login: **`lab_tester` / `labtest`**  
Decks: `lab_rules_player.json` · `lab_rules_ai.json` when using Lab Test Duel path.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Empty AR panel | Badge should say **PC EDITOR · dual disks** — not LENSES |
| No opening hand | Tap **DRAW HAND** or enable skip pre-duel |
| Want full boot cascade | Default now — splash/title always run in Editor. Lab only via Auth **DESKTOP LAB** button, or CLI `-desktopLab` / PlayerPrefs `WRLDZ_SkipBootCascade`=1 |
| Want skip splash forever | PlayerPrefs `WRLDZ_SkipBootCascade` = 1, or launch with `-desktopLab` |

Console tags: `[WRLDZ DesktopLab]`, `[WRLDZ PreDuel]`, `[WRLDZ AR]`.
