# Official rulings notes (engine data layer)

**Authority for card text:** `cards_db.json` `desc` field (Konami / Yugipedia-aligned).  
**Authority for structural rules:** Konami Official Rulebook + [Yugipedia](https://yugipedia.com/).  
**Banlist:** `banlist_advanced.json` — Advanced TCG from EDOPro `0TCG.lflist.conf` (`!2026.05 TCG`), cross-checked against Konami https://www.yugioh-card.com/en/limited/list_2026-05-18/ (Neuron 2026-05-18). Refresh with `Tools/export_tcg_pool.py`.

## Policy

- Never invent effect resolutions. Unregistered card IDs cannot activate effects.
- Structural rules (summons, battle math, turn structure, chains) always apply.
- OCR / physical deck scan should emit passcodes into the same `OfficialCardRecord` shape.

## Battle (damage calculation) — locked tests

See `TcgRegressionTests.RunAll()`.

- ATK vs DEF: ATK ≤ DEF → defender not destroyed.
- Face-down monsters = Defense Position.
- ATK = ATK → both destroyed, 0 damage.
- Direct attack → damage = ATK.

## Refresh banlist

1. Run `python3 Tools/export_tcg_pool.py` (reads EDOPro `0TCG.lflist.conf`, expands CDB aliases).  
2. Confirm `effectiveDate` matches the current Konami Advanced list.  
3. `OfficialDataSources.MaxCopies(passcode)` is Advanced / Modern. Historical ERAZ bands stay 3-of until that era's end-of-format list is loaded.  
4. TCG-only constructed: `TcgLegalPool` (`tcg_pool.json`, `datas.ot & 2`). OCG-only is refused.
