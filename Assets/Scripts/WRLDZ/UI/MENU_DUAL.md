# Dual menu presentation (Phone + AR)

Almost every systems menu ships **two layouts** of the same feature.

| Presentation | Enum | When | Layout |
|--------------|------|------|--------|
| **Phone screen** | `UiPresentation.NonArPortrait` | Overworld, Hub, Desktop Lab, Editor portrait | Full-bleed sheet, large type, full controls |
| **AR disk holo** | `UiPresentation.ArDiskHolo` | During DuelSlice / left-arm disk menus | Compact cyan glass panel, glanceable, limited edit |

## Code pattern

```csharp
// 1) Chrome
var frame = DualMenuPresenter.BuildFrame(modalHost, "TITLE", "subtitle", onClose);
// or force:
// DualMenuPresenter.BuildFrame(..., force: UiPresentation.ArDiskHolo);

// 2) Content into frame.BodyHost
// 3) Optional: DualMenuPresenter.ResolveDefaultPresentation()
//    → AR holo if active scene is Duel*, else phone
```

| Piece | File |
|-------|------|
| Presentation enum | `Shell/MenuId.cs` → `UiPresentation` |
| Frame builder | `Shell/DualMenuPresenter.cs` |
| Router default | `ScreenRouter.Presentation` |
| Deck example | `Shell/DeckCollectionScreen.cs` |

## Deck & Collection (first dual screen)

**Entry:** Hub “Deck & Collection” · bottom nav **DECK** · `ScreenRouter.OpenDeck()`

| | Phone | AR |
|--|-------|-----|
| Tabs | MAIN EXTRA SIDE TOME BINDER TRADE | MAIN EXTRA SIDE BIND |
| List + preview | Full | Compact |
| Add / Remove | Yes | No — “full builder on PHONE” |
| Data | Inventory deck box + binder; starter fallback | Same read |

## Adding the next dual menu

1. Create `SomethingScreen.Build(modal, onClose, force?)`.  
2. Call `DualMenuPresenter.BuildFrame`.  
3. Branch layout with `frame.Presentation == UiPresentation.ArDiskHolo`.  
4. Wire `MenuShell.ShowOverlay(MenuId.…)` like Deck.  
5. Do **not** invent a third full UI — only phone + AR (world panel only for zone prompts).

## Forbidden in AR holo

- Full settings sheets during live duel  
- Dense multi-column binder chrome  
- Permanent opaque full-screen modals that bury the stage  

See also: `UI_SPEC.md` §3.2–3.3.
