# ARGON mapping

How community YGO engineering maps onto this tree. Engine spec: `Assets/Scripts/WRLDZ/Duel/MASTER_ENGINE_SPEC.md`. Lab: `OCGCORE_LAB.md`. Compile gate: `docs/superpowers/specs/2026-08-27-eraz-compile-gate-design.md`.

## Two duels

| Path | Flag / host | Authority |
|---|---|---|
| Product | `OcgLabDuelHost.IsActive` false | `DuelEngine` + compiled programs |
| OCG lab | Deck filename contains `ocg_lab` | Native `edo9300` plugin |

Lab needs `Library/OcgNativePreflight.last.json` ok (plugin sha, CDB sha, CardScripts commit). Else stub tape — not the rules oracle.

## Data

| Community | ARGON |
|---|---|
| `cards.cdb` datas/texts | `cards_db.json` (+ lab `StreamingAssets/OcgCore/cards.cdb`) |
| `c########.lua` | `CompiledCardProgram` / `EffectClause` |
| Ignis `category` bits | Search filters + effect categories |
| `ot` TCG bit | `export_tcg_pool.py`, play pool `tcg_only` |
| Banlist `.lflist.conf` | `banlist_advanced.json` from EDOPro 0TCG lists |
| YGOPRODeck extra names | `extra_index.csv` (drop rows not in CDB) |

## Effect pipeline

Printed `desc` → regex / offline compiler → closed `EffectVocabulary`. Uncompiled text is `[UNIMPLEMENTED]` at **compile** time, never a tap that invents a resolution. ERAZ bands freeze pool + banlist + text + rulings; Duelist Kingdom table rules are an overlay, not a second card DB.

Lua mining tools (`Tools/extract_ygopro_triggers.py`, `_continuous.py`, `_st_facts.py`) write JSON facts, not Lua into Unity.

## UI

Deck editor: `DeckCollectionScreen` / `DeckConstructionBoard`. Wide layout: construction board left, CARD LIST right. Pin the PoolGrid **panel**, not ChipGrid's content.

## Licenses

| Content | Where | Constraint |
|---|---|---|
| ocgcore + Ignis scripts + lab CDB | `ThirdParty/OcgCore`, `StreamingAssets/OcgCore`, plugin | AGPL-3.0-or-later |
| Card art / official names / text | `StreamingAssets/Cards` | Konami IP |
| Original ARGON systems | Umbrax, artifacts, AR Tears, DDM overlay, … | Project original |

Separation is not permission to store-ship the native core.

## Editor version

`ProjectSettings/ProjectVersion.txt` is **6000.5.10f1**. Open the **repo root**, not nested leftover ProjectSettings under StreamingAssets.
