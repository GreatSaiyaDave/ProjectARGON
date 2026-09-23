# OCG live-handoff — smallest chip plan (Instruction 1)

Date: 2026-09-06  
Author: Backup-bot (from Dilbot handoff)  
Policy lock: `Docs/RESOLVE_OWNERSHIP.md` — **OCG owns resolve when a lua file exists. C# only draws the room and refuses if there is no script.**

## Current reality (do not change in this chip)

| Path | Behavior today |
|------|----------------|
| Lab decks (`ocg_lab_*.json` via `StartLabTestDuel` / Boot / DesktopLab) | `DuelBootstrap` starts `OcgLabDuelHost` (native if preflight OK, else stub). `DuelCommandService` routes intents to the host while `IsActive`. |
| Non-lab AR / normal `DuelEngine` | C# activate path. `OfficialEffectRegistry.WarnIfLuaOwnsResolve` **warns only** when `c{id}.lua` exists and lab host is inactive — compatibility demos keep working. |

Chip3 monster-gate is **CLEAR** (Perfect-O). Tray `DuelUI.cs` WIP is **orthogonal** — leave alone unless Dilbot/Perfect-O FAIL.

---

## 1. What flips when

### Option A — Lab-only verify (Instruction 2; **preferred next**, no AR flip)
- **Flips:** nothing in live AR routing.
- **Does:** smoke `StartLabTestDuel("ocg_lab_player.json","ocg_lab_ai.json")` / native host; confirm lua card activate→resolve via OCG; document gaps (stub fallback, missing scripts, UI).
- **When:** user or Dilbot picks “verify lab OCG”.

### Option B — Live AR enable (later chip; **needs explicit user OK**)
- **Flips:** non-lab duels that should use native OCG when scripts exist (gate TBD: match flag / format / “lua-covered deck” — **not** “all AR decks”).
- **Likely:** promote or mirror lab host start for selected AR matches; keep C# for cards **without** lua (refuse invent).
- **When:** only after Option A evidence + Dave/Dilbot choose enable scope.

**This Instruction 1 file does not implement A or B.**

---

## 2. Files likely touched (when implementing later)

| File | Role |
|------|------|
| `Assets/Scripts/WRLDZ/Core/DuelBootstrap.cs` | Lab deck → `OcgLabDuelHost.StartNative` / stub; live AR still `new DuelEngine()`. Any live flip starts here (smallest match gate). |
| `Assets/Scripts/WRLDZ/Duel/Ocg/OcgLabDuelHost.cs` | `IsActive` branch bit; `StartNative` / `TryExecute` / pump. |
| `Assets/Scripts/WRLDZ/Duel/DuelCommandService.cs` | Already prefers active lab host for intents — verify no bypass on live path. |
| `Assets/Scripts/WRLDZ/Duel/Rules/OfficialEffectRegistry.cs` | `WarnIfLuaOwnsResolve` — keep warn until live handoff; **do not** delete C# compat or invent Wok/DDV C# for lua cards. |
| `Assets/Scripts/WRLDZ/Core/AppSession.cs` | `StartLabTestDuel` deck injection / `TestDuelMode`. |
| `Assets/Scripts/WRLDZ/Duel/Ocg/OcgScriptStore.cs` (if present) | Lua existence checks. |
| `Docs/RESOLVE_OWNERSHIP.md` | Policy; extend only if acceptance wording needs a one-liner. |

**Avoid:** broad `DuelEngine` rewrite; committing CardArt / `ignore.conf`; push without Dave.

---

## 3. Acceptance checks

### Lab verify (Option A)
1. Start lab with `ocg_lab_*` decks → log shows OCG lab host active (native preferred).
2. Activate a card that has `c{id}.lua` → resolve goes through OCG host (`TryExecute` / core), not a new C# recipe.
3. Card **without** lua → C# path refuses invent / no fake effect; duel stays stable.
4. Headless/batch compile clean (`error CS` = 0) when Editor free; Perfect-O QC after any code.

### Live enable (Option B — later)
1. Only scoped AR matches flip; default demos unchanged until flag on.
2. Lua-present cards resolve via OCG; non-lua refuse invent.
3. Warning may harden or stay until Dave OK to remove compat.
4. Perfect-O source + headless PASS; no unauthorized commit/push.

---

## 4. Explicit out of scope

- Flipping **all** live AR decks to OCG in one shot.
- Removing C# compatibility / `WarnIfLuaOwnsResolve` without user OK.
- Inventing C# effects for cards that have Lua (Wok, DDV, etc.).
- Chip3 NegateActivation beyond existing monster-gate (Perfect-O CLEAR 2026-09-06).
- Committing tray `DuelUI.cs` or CardArt unless Dave asks (Instruction 3).
- Push to origin unless Dave asks.
- Chasing parked harness suite failures (844/61).

---

## Recommended next pick for Dave / Dilbot

1. **verify lab OCG** (Instruction 2) — safest evidence before any AR flip.  
2. Optionally later: commit tray only (Instruction 3).  
3. Live AR enable = new chip after A, with explicit scope.

## Backup-bot status

- Instruction 1 = **this plan** (done).  
- Steps 2–3 stay in inbox until chosen.  
- No engine code changed in this step; Perfect-O QC not required for plan-only.


## Dilbot endorse

2026-09-06: Dilbot reviewed — plan accepted as the smallest chip. No live AR flip until Option A evidence + explicit OK.
