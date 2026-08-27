# Reference: Dueling Dimension → WRLDZ AR

## What Dueling Dimension is

[Dueling Dimension](https://devpost.com/software/dueling-dimension) (Devpost, Meta Quest 3):

- **Mixed reality** TCG deck builder (Yu-Gi-Oh first)
- **World-space panels** floating in the room (collection vs deck), not a phone 2D board
- Unity + **Meta XR All-in-One SDK**
- Card data from **YGOProDeck API** (same pipeline we use for CardArt)
- Vision roadmap: **summon / spell / battle in MR**

Key product lesson: **spatial first**. The table is your room. UI is large panels in FOV, not stacked GBA chrome.

## What WRLDZ was doing wrong

| Problem | Why it felt broken |
|---------|-------------------|
| AR stage as a thin strip (25% height) | Spatial stage looked like a wallpaper stamp |
| Full 2D Master-Duel-style field **plus** 3D stage | Double board = visual noise, not sustainable |
| Solid void camera | No real “looking through the phone” AR feel |
| HUD stacked: referobot bar, phase pills, mid log, dual fields | Not GO-minimal; not MR-panel clean |

## WRLDZ AR path (aligned) — **LENSES FIRST**

| Phase | Target |
|-------|--------|
| **Priority** | **Meta Quest 3 MR** — passthrough + floor arena + controller/hand disk (month demo) |
| **Now (dev)** | Webcam / Editor spatial stage so we keep iterating without a headset every day |
| **Secondary** | Phone camera passthrough parity |
| **Later** | Other HMDs / full outdoor GPS on lenses |

See **`LENSES_FIRST_ROADMAP.md`** for the 30-day plan.

## Layout canon (phone AR duel)

```
┌─────────────────────────────┐
│  LP · PHASE · status        │  ~8% thin HUD
├─────────────────────────────┤
│                             │
│   PASSTHROUGH + 3D STAGE    │  ~70%  ← hero
│   (disks · arena · holos)   │
│                             │
├─────────────────────────────┤
│  Hand (cards)               │  ~14%
│  Phase / Pass / Map         │  ~8%
└─────────────────────────────┘
```

Field taps stay on **compact zone chips** over the stage bottom — not a second full board.

## Implementation

| Piece | Script |
|-------|--------|
| Spatial stage + passthrough | `ArDuelSpace` |
| AR-first HUD | `DuelUI.BuildUi` |
| Zone Mode entry (overworld) | `OverworldUI` AR prompt |
| Future XR | Meta XR / AR Foundation packages (not required for phone sim) |
