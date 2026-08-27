# Production TCG Engine — Project ARGON / DM WRLDZ

## Single source of truth

| Concern | Authority |
|---------|-----------|
| Card text | `StreamingAssets/Cards/cards_db.json` → `CardDef.desc` (Konami/Yugipedia-aligned) |
| Structural rules | Konami Rulebook + Yugipedia glossary |
| Banlist | `StreamingAssets/Cards/banlist_advanced.json` ← Konami Advanced Format |
| Effect **resolution** | Only registered scripts — never invent (`SpellTrapEffects`, `MonsterEffects`) |
| Script reference | `~/ygopro-scripts` (YGOPro/EDOPro Lua) — port patterns, not the runtime |
| Live game state | `DuelEngine` only |

See **`ENGINE_GUARANTEE.md`** — structural play always works; lab decks guarantee every printed effect is scripted.

AR / UI / holograms **query and command** via `IDuelEngine` — they do not mutate zones.

## Architecture

```
IDuelEngine / DuelEngine
  ├── Rules/BattleMechanics      official damage calculation
  ├── Rules/SummonProcedures     all summon kinds (structural + registry)
  ├── Rules/ChainStack           Spell Speeds, LIFO resolve
  ├── Rules/FastEffectTiming     priority windows
  ├── Rules/ContinuousEffectBoard
  ├── Rules/RulesValidator       pre-snap / pre-activate
  ├── Rules/OfficialCardAuthority
  ├── Rules/OfficialEffectRegistry
  ├── Rules/OfficialDataSources  banlist + OfficialCardRecord (OCR-ready)
  ├── Rules/TcgRegressionTests
  ├── SpellTrapEffects           registered staple scripts only
  └── GameStateView              read-only AR/netcode adapter
```

## Interfaces for AR

```csharp
IDuelEngine engine;
// Before disk snap:
var v = engine.ValidatePlacement(player, card, RulesZoneKind.Monster, zoneIndex, preferSet: false);
if (!v.Legal) { /* reject snap, show v.Reason */ return; }
// Commit:
engine.TryNormalSummonToZone(player, card, asSet: false, zoneIndex);

// Holograms:
IGameStateView view = engine.AsView();
// bind LP, zones, phase from view — never invent state
```

`ArSnapRules` already calls `engine.ValidatePlacement` before commit.

## Turn structure

Draw → Standby → Main1 → Battle (Start → Battle → Damage[substeps] → End) → Main2 → End  

First player: no draw turn 1; no Battle Phase turn 1.

## Battle math (locked)

| Situation | Destroy | Damage |
|-----------|---------|--------|
| ATK > DEF (def position) | Defender | 0 (unless piercing) |
| ATK < DEF | None | Attacker takes DEF−ATK |
| ATK = DEF | None | 0 |
| ATK > ATK | Defender | difference to defender’s controller |
| ATK < ATK | Attacker | difference to attacker’s controller |
| ATK = ATK | Both | 0 |
| Direct | — | ATK to opponent |

Face-down monsters = Defense Position always.

Run tests: **WRLDZ → Rules → Run TCG Regression Tests** (or auto on Editor duel start).

## Summoning

| Method | Structural | Full material recipes |
|--------|------------|------------------------|
| Normal / Tribute / Set / Flip | Yes | — |
| Fusion / Synchro / Xyz / Link / Ritual / Pendulum | Framework + reject unless registered | Per-card scripts from official text |

## Data layer (Wiki / OCR ready)

```csharp
OfficialDataSources.OfficialCardRecord
  passcode, name, text, banlistStatus, textHash, physicalScanKey
```

- Populate banlist JSON from Konami site.  
- OCR pipeline should emit passcodes → load `CardDef` / `OfficialCardRecord`.  
- Core engine does not change when scan is added.

## Bugs fixed (rules accuracy pass)

1. **AI** attacked face-down as if DEF=0 → only attack DEF if ATK > DEF.  
2. **AI** marked `AttackedThisTurn` without declaring. Fixed.  
3. **Battle** face-down/DEF path hardened + sanitize + hard gate.  
4. **Extra Deck** loaded; Fusion recipes for Gaia the Dragon Champion / Black Skull Dragon.  
5. **Swords of Revealing Light** — blocks opponent attack declarations; destroyed on opponent's 3rd End Phase; flips FD monsters face-up DEF.  
6. **Enemy Controller** — changes battle position (ATK↔DEF), not force-DEF; Lord of D. blocks targeting Dragons.  
7. **Ring of Destruction** — opponent's turn only; original ATK effect damage to both; ATK ≤ their LP targeting.  
8. **Polymerization** — only activates if registered materials legal; does not silently fail.  
9. **Flute of Summoning Dragon** — Lord of D. on field required; SS up to 2 Dragons from hand.  
10. **Replay** — uses declaration-time monster presence, not always `true`.  
11. **Position change** — cannot change position the turn a monster was Set (even if flipped by effect).  
12. **Waboku** — cleared for both players at End Phase of the turn it applied.  
13. **Trap Hole** — response on Normal/Flip Summon (including Tribute = Normal).  

## Honest scope

A full EDOPro-class card-text engine for every printed card is multi-year. This production core:

- Enforces **official structural rules** for the playable loop  
- **Never invents** unregistered effects (activation refused)  
- Registered staple scripts match `cards_db.json` official text for those IDs  
- Ships **regression tests** (battle math + structural + registry)  

Add official scripts card-by-card into `OfficialEffectRegistry` + `SpellTrapEffects`.
