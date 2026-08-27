# Official rulings notes (engine data layer)

**Authority for card text:** `cards_db.json` `desc` field (Konami / Yugipedia-aligned).  
**Authority for structural rules:** Konami Official Rulebook + [Yugipedia](https://yugipedia.com/).  
**Banlist:** `banlist_advanced.json` — populate from https://www.yugioh-card.com/en/limited/

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

1. Download current Advanced Format list from Konami.  
2. Convert passcodes into `forbidden` / `limited` / `semiLimited` arrays.  
3. Set `effectiveDate`.  
4. Engine uses `OfficialDataSources.MaxCopies(passcode)` for deck construction checks.
