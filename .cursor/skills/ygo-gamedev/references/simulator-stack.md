# Simulator stack (YGOPro → EDOPro → ocgcore)

Summarized from Fluorohydride/ygopro-core, edo9300/ygopro-core, Project Ignis CardScripts, scrapi-book, and ARGON `OCGCORE_LAB.md`.

## Three layers

| Layer | Job |
|---|---|
| **Client** | Zones, hands, animations, clicks, replays (`.yrp` / `.yrpX`) |
| **ocgcore** | State machine + Lua VM. Tick with `process` / `OCG_DuelProcess` until END, AWAITING, or CONTINUE |
| **Scripts + CDB** | Per-card Lua + SQLite `cards.cdb` (`datas` + `texts`) |

YGOPro (Fluorohydride) and EDOPro (Project Ignis / edo9300 core) share this split. Cores are **not** drop-in compatible. ARGON lab pins **edo9300** (`OCG_CreateDuel`). Do not P/Invoke Satellaa, old YGOPro DLLs, or `~/ygo-agent` Fluorohydride `ygopro_ygoenv.so`.

edo9300/ygopro-core is AGPLv3. Linking it into a shipped Unity binary is a **license decision**, not a coding convenience. Keep it under `ThirdParty/OcgCore/` and `StreamingAssets/OcgCore/`. Card art stays in `StreamingAssets/Cards/`.

## Client loop (edo9300 C API)

1. `OCG_CreateDuel` with **both** `cardReader` and `scriptReader` (never null).
2. `OCG_LoadScript` `constant.lua`, then `utility.lua`, then `procedure.lua` (utility loads `proc_*.lua`).
3. `OCG_DuelNewCard` for every deck card.
4. `OCG_StartDuel`.
5. Repeat: `OCG_DuelProcess` → `OCG_DuelGetMessage` → if AWAITING, `OCG_DuelSetResponse`.
6. `OCG_DestroyDuel` when done.

Classic Fluorohydride names: `create_duel` / `start_duel` / `process` / `get_message` / `set_response`. Same idea: the core is a **coroutine**. Unity never "applies Dark Hole" in C# on the lab path.

Statuses: `END`, `AWAITING` (need a player choice), `CONTINUE` (keep ticking). Unsupported waits in ARGON lab must **pause visibly**. Loadable ≠ playable.

## Messages, not methods

The core pushes binary messages (summon, move, chain, select idle, select target, …). The client parses them and either animates or answers. This is why WindBot / ygo-agent can sit on the same core: they are response policies.

## Replays

| Format | Client | Note |
|---|---|---|
| `.yrp` | Classic YGOPro | Action tape; brittle across core versions |
| `.yrpX` | EDOPro | Result-oriented; more stable |

No large public Master Duel / DuelingBook move dump. Prefer ARGON `duel_reviews` JSONL and self-play.

## Product vs lab in ARGON

- **Lab:** Unity is the TV. Rules live in ocgcore + Ignis scripts. No `if (cardId == …)` under `Duel/Ocg/`.
- **Product:** `DuelEngine` interprets `CompiledCardProgram`. Lua at `~/ygopro-scripts` is mined as a **linter** (`scan_engine_gaps.py`), never executed in a store build.

If a C# resolution disagrees with Ignis Lua for an official card, the C# program is unfinished — do not "fix" it by copying Lua into Unity.
