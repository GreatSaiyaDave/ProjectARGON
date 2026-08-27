# How we stabilize the slice

## In Unity (you)

1. Let scripts recompile (bottom-right spinner).
2. Open **Assets/Scenes/DuelSlice**.
3. Press **Play**.

## What “stable” means now

| Check | Behavior |
|-------|----------|
| Opening hands | Each side draws **5** |
| First turn draw | **Skipped** for you (TCG) |
| First turn attacks | **Locked** (status shows `NO ATTACKS`) |
| Buttons | Greyed out when illegal |
| Hint bar | Tells you what to do next |
| Win / lose | Full-screen overlay + message |
| Restart | Bar button **or** overlay **Restart Duel** (new shuffle) |
| Decks | 40-card mains; size logged at start |
| Art | Loads from `StreamingAssets/CardArt/{id}.jpg` |

## Demo flow to verify

1. Play → see 5 cards, “no draw / no attacks” on turn 1.  
2. Select Lv≤4 monster → **Summon ATK** → **End Turn**.  
3. AI acts → your turn 2 (you draw, can **Battle**).  
4. Reduce LP to 0 (or lose) → overlay → **Restart Duel**.

## If clicks do nothing

- Project uses **Input System**. EventSystem is created at runtime with `InputSystemUIInputModule` when available.
- Confirm Console has no red errors on Play.
- Try clicking buttons after selecting a hand card (Summon needs a selection).
