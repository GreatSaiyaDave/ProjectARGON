# WRLDZ Unity Vertical Slice

**Project ARGON** — free indie YGO duel prototype (anime TCG rules + AR stage).

**Lab rig (now):** **Galaxy S23 Ultra + PC** — no headset.  
**Product north star:** AR lenses later (same stage).  
See [`PRODUCT_VISION.md`](PRODUCT_VISION.md) · [`S23_LAB.md`](S23_LAB.md) · [`LENSES_FIRST_ROADMAP.md`](LENSES_FIRST_ROADMAP.md).

Unity **6000.4.8f1** (Unity 6).

## Open & play (PC Editor)

1. Open **Unity Hub** → project:
   ```
   ~/DMWRLDZUnityProject/ProjectARGON
   ```
2. Editor **6000.4.8f1**.
3. Scene `Assets/Scenes/Boot.unity` (full flow) or `DuelSlice.unity` (duel only).
4. Game view aspect **1080×2340** (portrait).
5. Press **Play**.

## Build to S23 Ultra

**WRLDZ → Lab → Build APK for S23 Ultra** → install `Builds/Android/WRLDZ_S23_Debug.apk`  
Full steps: [`S23_LAB.md`](S23_LAB.md).

## Controls (in Play mode)

| Button | Action |
|--------|--------|
| Click card in hand | Select it |
| **Summon ATK** | Normal Summon selected Lv≤4 monster face-up ATK |
| **Set DEF** | Set selected Lv≤4 monster face-down DEF |
| **Set S/T** | Set selected Spell/Trap (no activation in slice) |
| **Battle** | Enter Battle Phase |
| Click your monster | Select attacker |
| Click enemy monster | Attack that target |
| **Direct Atk** | Attack LP if opponent has no monsters |
| **End Turn** | End turn → AI plays automatically |

Turn 1: you cannot attack (TCG-style).

## What this slice includes

- Full S1 card DB (`StreamingAssets/Cards/cards_db.json` — 1556 cards)
- Card art (`StreamingAssets/CardArt/`)
- 40-card player + AI decks
- Draw / Main / Battle / End
- Normal Summon (Lv ≤ 4 only — no tributes yet)
- Battle (ATK vs ATK, ATK vs DEF)
- Direct attacks, LP win, deck-out loss
- Simple greedy AI

## What it intentionally skips

- Full card effects / chains / banlist engine  
- Tributes, Fusion summons, Extra Deck play  
- Spell/Trap activation  
- Map / GPS overworld + tears (GO-style shell)  
- Encounter proximity → **AR entry prompt**  
- Dual menus (AR + non-AR); `IArBackend` Lens + PhoneCamera  
- Multiplayer / live events

## Refresh card data from repo root

```bash
cd ~/Downloads/WRLDZ
python3 tools/fetch_ygoprodeck.py   # if pool updates
# re-run StreamingAssets prepare (see tools/prepare_unity_streaming_assets.py)
python3 tools/prepare_unity_streaming_assets.py
```

## Decks

| File | Role |
|------|------|
| `StreamingAssets/Decks/player_starter.json` | You |
| `StreamingAssets/Decks/ai_kaiba.json` | AI |

Export more from the web card browser and drop them here.
