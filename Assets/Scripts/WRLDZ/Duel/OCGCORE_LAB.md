# OCG lab path (viewer over ocgcore)

Unity is the TV. **edo9300/ygopro-core** (Ignis / EDOPro, `OCG_CreateDuel`) is the referee. This folder is lab-only.

**Hard rule:** no `if (cardId == …)` under `Duel/Ocg/`. Passcodes live in decks, Lua, tape JSON, and `speed_table.json`. `lab_card_manifest.json` is pool metadata (`full_official_pool` CDB + `playPool: tcg_only`). Not a closed allow-list for the native core.

**Central promise:** any official card with CDB data and scripts can be **loaded** by the native core. It is **playable in Unity** only through the generic message/response types currently implemented. Unsupported waits pause visibly. Loadable ≠ playable.

**Live path:** native spike (pinned ProjectIgnis/bin + readers + preflight). Stub tape is **fallback** if preflight fails or the fingerprint is stale — not a gate in front of native, and **not** the rules oracle. Do not grow `replays/lab_dark_hole_vs_ox.json`. Native refuses to start without SQLite + the full CDB.

## License

| What | Where | License |
|---|---|---|
| ocgcore + Ignis scripts + `.cdb` | `ThirdParty/OcgCore/`, `StreamingAssets/OcgCore/`, `Assets/Plugins/x86_64/` | **AGPL-3.0-or-later** |
| Card art / names / official text | `StreamingAssets/Cards/` | **Konami IP** — do not mix into the AGPL folder |

Putting files in this folder is **separation, not a distribution decision**. Linking/shipping the binary likely copylefts ARGON. Lab-only until legal review. Do not store-ship this path yet.

`~/ygo-agent` `ygopro_ygoenv.so` is **Fluorohydride**, not Ignis. Do not P/Invoke it. Do not use Satellaa cores or old YGOPro dlls.

Pins and hashes: `StreamingAssets/OcgCore/PROVENANCE.json`. Pool: `lab_card_manifest.json`. Extra names listing (not `OCG_CardData`): `extra_index.csv` from YGOPRODeck, rows dropped if not in CDB.

## How to start a test duel

Desktop Lab **OCG LAB DUEL** with decks:

- `StreamingAssets/Decks/ocg_lab_player.json`
- `StreamingAssets/Decks/ocg_lab_ai.json`

`DuelBootstrap` sees `ocg_lab` in the filename and constructs `OcgLabDuelHost` (sets **`OcgLabDuelHost.IsActive`** — the only lab-path bit).

Native is used only when `Library/OcgNativePreflight.last.json` has `ok: true`, `idleSeen: true`, and `preflightFingerprint` matches plugin sha + full CDB sha + CardScripts git commit + decks + manifest + API major.minor. Otherwise stub + log (failed vs stale). SQLite missing → native refused.

Editor: **WRLDZ → Lab → Run OCG Lab Tests**, **WRLDZ → Lab → Run OCG Native Preflight**, **WRLDZ → Lab → Run OCG Official Library Integrity**.

Existing `lab_rules_*.json` duels still use `DuelEngine`.

This slice’s working Extra line is **Reaper on the Nightmare** (85684223) via Polymerization + Spirit Reaper + Nightmare Horse. Extra is not padded to 15. Adding the next Extra is a deck JSON edit (plus Main materials), not a lua/C# copy.

## Where scripts / cdb go

```
StreamingAssets/OcgCore/scripts/constant.lua
StreamingAssets/OcgCore/scripts/utility.lua
StreamingAssets/OcgCore/scripts/procedure.lua   # CreateDuel shim; proc_*.lua hold Extra procedures
StreamingAssets/OcgCore/scripts/chain.lua
StreamingAssets/OcgCore/scripts/proc_fusion.lua
StreamingAssets/OcgCore/scripts/official~/c########.lua   # ~ hides 13k lua from FileHasher; OcgScriptStore still indexes by basename
StreamingAssets/OcgCore/cards.cdb                 # full official datas+texts
StreamingAssets/OcgCore/cards_datas.json          # tiny stub/old-test fallback; native never uses it
StreamingAssets/OcgCore/lab_card_manifest.json
StreamingAssets/OcgCore/extra_index.csv
StreamingAssets/OcgCore/PROVENANCE.json
```

`OCG_CreateDuel` **must** get `cardReader` + `scriptReader`. After every successful create (fresh Lua state), load `constant.lua`, `utility.lua`, then `procedure.lua` via `OCG_LoadScript` before any `OCG_DuelNewCard`. `utility.lua` then `LoadScript`s `proc_*.lua`. `scriptReader` resolves **basename only** through the filename index (`c123.lua` → `official/c123.lua`). Traversal (`..`, `/`, `\`) is rejected.

`OcgScriptStore` extra search (Editor): env `OCG_SCRIPTS_ROOT` or EditorPrefs `WRLDZ.OcgScriptsRoot`. Do not point that at the mixed flat `ygopro-scripts` clone. `unofficial/`, `pre-release/`, `rush/`, `goat/`, `skill/` paths are not indexed.

Battle Ox in lab decks is **5053192** (CDB alias of 5053103). **5053103** is the wrong fixture.

## Native plugin

Prefer the pinned **ProjectIgnis/bin** binaries (see PROVENANCE), not a local compile:

- Linux Editor / standalone: `Assets/Plugins/x86_64/libocgcore.so`
- Windows Editor / standalone: `Assets/Plugins/x86_64/ocgcore.dll`

Source commit for the C API: **edo9300/ygopro-core** `fd2a557167f9e0fe19b867c325e67a3b3f9dca11` (v11). P/Invoke `OCG_CreateDuel` / `OCG_GetVersion` (`void`) / `OCG_DuelProcess` / `OCG_DuelGetMessage` / `OCG_DuelSetResponse` / `OCG_DuelQueryCount` — **not** Fluorohydride `create_duel(seed)`.

If `OCG_GetVersion` major ≠ 11, hash drifts, or `ldd` reports a missing dep: do not call `OCG_CreateDuel`; stub stays.

CardScripts pin is **ProjectIgnis/CardScripts** `e02497b61bd45048230cdfb4cc4a075ecb09b4d3` (newer than the mixed `ygopro-scripts` clone). Fingerprint hashes that git commit, not every lua file.

## Replay compare

Golden `StreamingAssets/OcgCore/replays/edopro_dark_hole_vs_ox.msg.json` must be recorded from **EDOPro desktop** or an edo9300-linked build. Never default ygo-agent. If missing, tests skip. If native ≠ EDOPro, ARGON is wrong — do not edit golden to match Unity.

## Speed

`speed_table.json` scales `TimedResponseClock` on **`MSG_SELECT_CHAIN` only**. Expiry sends int32 `-1` (pass). `0` would activate the first link.

## Implemented waits (generic, no card names)

Idle, Battle, Chain, SelectCard, YesNo / EffectYn (player Activate=yes / Pass=no; opponent always no), Place (first legal), Position (first bit), Tribute (as SelectCard), Unselect, Option (index 0 / `DuelIntent.ZoneIndex`), AnnounceRace / AnnounceAttrib (first available bits), AnnounceNumber (index 0). `ANNOUNCE_CARD`, `SELECT_SUM`, `SELECT_COUNTER`, `SORT_*` stay pause+log.

Layouts match edo9300/EDOPro **non-compat** (Ignis): option/number descs are `u64`; race available is `u64`; attrib available is `u32`. Golden tapes still from EDOPro desktop (`~/Games/ProjectIgnis/EDOPro`), never KaibaPro.
