# Avatar 3D — VRM on map and AR

**Data:** `AvatarAppearance` (`vrmId`, `use3dPreview`).  
**Folder:** `StreamingAssets/WRLDZ/Avatar3D/{vrmId}.vrm`  
**2D fallback:** `StreamingAssets/WRLDZ/Avatar/` (portraits + layered parts).  
**Legal:** original WRLDZ looks only. No DMO, no Konami meshes. See `DMO_AND_AVATAR.md`.

Until a VRM file is present, the customizer and HUD stay 2D.

---

## Runtime load

Unity: [UniVRM](https://github.com/vrm-c/UniVRM) (MIT). Humanoid rig. Idle / walk from Mixamo or our own clips.

Spawn:

1. If `use3dPreview` and `Avatar3D/{vrmId}.vrm` exists → load VRM.  
2. Else → `AvatarPortraitView` (2D).

Surfaces: overworld token (`OVERWORLD_3D.md`), customizer preview, AR companion (optional). HUD chips stay 2D.

---

## How to make a look

**VRoid Studio** (free) → export VRM → drop in `Avatar3D/`.

Commercial use of models you create in Studio is allowed for games. Pixiv does not allow an in-game tool that mixes or deforms VRoid preset meshes at runtime. Ship finished looks (one VRM per portrait), not a VRoid part mixer.

Do not import other people’s VRoid Hub files unless that file’s license allows your use.

**In-game part cycling** (hair / coat / colors like the 2D chips): use a pack you own, e.g. BoZo Modular Anime Characters on the Unity Asset Store, mapped onto the existing `AvatarAppearance` indices. Do not use Ready Player Me (service ended 2026-01-31).

M3 Character Studio (MIT) is an alternate VRM/GLB exporter if you already use it.

---

## Look (original)

Spiky hair, school or street coats, strong collars, high-contrast eyes. Palette: ink navy, gold, cyan, warm skin. Imagine is for 2D portraits and chrome only.

---

## Data

| Field | Role |
|-------|------|
| `portraitIndex` | 2D face `portraits/portrait_{n}.png` |
| `bodyIndex` / `hairIndex` / `eyesIndex` / `outfitIndex` / `accessoryIndex` | 2D layers; 3D part slots if a modular pack is wired |
| `skinHex` / `hairHex` / `outfitTintHex` / `accentHex` | Tints |
| `title` | Profile line |
| `vrmId` | File stem under `Avatar3D/` |
| `use3dPreview` | Prefer VRM when the file and UniVRM are present |
