# Main menu — Duel Disk · Eldritch Solid Vision

**Display title:** Duel Monsters: WRLDZ  
**Unity project:** ProjectARGON  
**Scene:** `MainMenu` (build index 2)  
**Code:** `DuelDiskMenuUI.cs` · `MainMenuBootstrap.cs` · `AppSession.GoMainMenu()`  
**Design system:** `UI/Eldritch/ELDRITCH_CONTRACT.md` · `ELDRITCH_SOLID_VISION.md`

## Role in the graph

Boot → **MainMenu** (this scene) → Overworld via **WORLD MAP**. `BootFlowUI.WorldLoadRoutine`
ends with `AppSession.Ensure().GoMainMenu()` ("Opening the Duel Disk…"). The Overworld Disk HUB
returns here. QUIT logs out and returns to Boot after a two-step confirm.

## Hierarchy (portrait anchors, y0–y1 of the 1080×2340 canvas)

| y | Element | Component |
|---|---------|-----------|
| 0.905–0.985 | **Identity slate** — portrait (tap → avatar customizer) · name · `Lv · team · title` · soul gem | `SolidVisionSlate` Tertiary · `AvatarPortraitView` · `SoulBadgeView` |
| 0.862–0.900 | Cap **DUEL DISK · SOLID VISION** + animated hieroglyph strip | `HieroglyphBorder.Top` |
| 0.575–0.855 | **HERO — DUEL** "Summon the Solid Vision" · slow gold summoning ring + spinning Spirit Dueler disk in the right 38% · fill thinned to 0.56/0.74 so the pyramid shows through | `ArcaneButton` Primary (`hieroglyphBorder:true`, 32 px ring) · `RuneRingSpin` · `SpiritDuelerDiskView.HubShowcase` |
| 0.530–0.568 | Cap **COMMAND** (cyan) | `HubChrome.SectionCap` |
| 0.345–0.525 | 2×2 **DECK · BAG · STORY · BAZAAR** (lapis, Imagine icons) | `ArcaneButton` Secondary |
| 0.250–0.335 | **VIEW · SET** compact row | `ArcaneButton` Tertiary |
| 0.135–0.235 | **WORLD MAP** (compass) · **QUIT** (right 28%) | `ArcaneButton` Primary · Danger |
| 0.100–0.130 | status / toast line | `Text` |

Every interactive plate lives under a `Tiles` host so one `MenuMotion.StaggerChildren` reveals
them top-to-bottom in play mode; the identity slate `PlayOpen()`s (materialize sweep).

## Popovers (one at a time, `HieroglyphPanel` over a dim)

| Trigger | Panel | Buttons |
|---------|-------|---------|
| DUEL | **CHOOSE YOUR DUEL** (Primary, 0.30–0.80) | **VS AI** → `MenuId.ArDuelCreate` · **VS PLAYER** → `MenuId.ArDuelPvpCreate` · **ZONE MODE** (Eldritch) → `ZoneModeSession.EnterFromHub` · CLOSE |
| QUIT | **LEAVE THE SHADOW REALM?** (Danger, 0.38–0.62) | **QUIT** → `Logout()` + `GoBoot()` · **STAY** |

Tapping the dim closes the popover. `Close()` sheets the panel out and destroys the host.

## Destinations

| Control | Action |
|---------|--------|
| DECK / BAG / STORY / BAZAAR / VIEW / SET | `OpenHubOverlay(MenuId.*)` on the shared overlay `MenuShell` |
| Portrait | Avatar customizer (`AvatarCustomizerUI.OpenProfile`) |
| WORLD MAP | `AppSession.GoOverworld()` |

Tome is **not** a hub tile — Overworld Eye **TOME** and AR SYS **TOME**. Artifact Deck Box is
nested under **BAG**. Profile is the header portrait.

## Background stack (canvas child order)

1. `HubBg` — Imagine `bg_hub` sprite (the sealed pyramid under the Tear; it ghosts through the DUEL slate)
2. `EgyptianAgesAtmosphere` (`MenuAge.NewKingdom`) — only its star twinkles stay active; the opaque
   sky / sand / veil plates, the tofu-glyph Text stream and the rectangular dust are disabled
   (`EldritchDressing.KeepOnlyStars`)
3. `Root` — `LowerDusk` (bottom 58%), `HieroglyphBorder.Left/.Right` rails (28 px), slates and tiles
4. `UiEmberField` ambient (rate ≈ 1.5) — canvas-safe pooled `Image` motes

## Aesthetic

- Ancient Egypt × Lovecraft × Sorcery × Solid Vision: gold on obsidian (Primary), cyan on lapis
  (Secondary), violet/magenta (Eldritch / Zone Mode), blood (Danger). Tokens: `EldritchPalette`.
- Slates are translucent holograms (`WRLDZ/UI/SolidVisionSlate`): scanlines, top→bottom sweep,
  edge glow, faint noise veins. Buttons carry a travelling energy line (`WRLDZ/UI/ArcaneEnergyFlow`).
- Hieroglyphs are **baked sprites** (`EldritchGlyphs`); legacy `Text` cannot render U+13000.
- Type: **Bangers** display titles · **Exo 2** body · **Russo One** button labels; every label on a
  busy plate goes through `WrldzType.StyleButtonLabel` (heavy black outline) for outdoor readability.
- All motion on unscaled time; everything builds with `Application.isPlaying == false`
  (`MenuSmokeTest` "DuelDiskMenuUI.Build" checks DUEL · DECK · BAG · STORY · BAZAAR · WORLD MAP · QUIT).

See also: `FLOW.md` (full graph), `UI_SPEC.md`.
