# Eldritch Solid Vision — design system & integration guide

**Scope:** main-menu overhaul + Zone Mode AR hub for Duel Monsters: WRLDZ (ProjectARGON).
**Binding contract:** `Assets/Scripts/WRLDZ/UI/Eldritch/ELDRITCH_CONTRACT.md` (§ numbers below refer to it).
**Asset research:** `ELDRITCH_ASSET_RECS.md` (same folder). **Menu spec:** `Assets/Scripts/WRLDZ/UI/MENU_MAIN.md`.
**Production UI rules still apply:** `Assets/Scripts/WRLDZ/UI/UI_SPEC.md` (readability, touch targets, chrome prefs).

Runtime code lives in `Assets/Scripts/WRLDZ/UI/Eldritch/` (namespace `WRLDZ.UI.Eldritch`), Editor
tooling in `Assets/Editor/WRLDZ/Eldritch*.cs` (namespace `WRLDZ.EditorTools`), baked art in
`Assets/StreamingAssets/WRLDZ/Eldritch/`. Everything is built at runtime from C#; prefabs are a
convenience export, never the source of truth.

---

## 1. Vision

The Duel Disk is no longer a plastic gauntlet — it is a Solid Vision slate: a wafer of obsidian
glass inlaid with lapis and hammered gold, laid over the street like a projected relic. Cyan
scanlines drift down its face the way light moves through deep water; a gold thread of arcane
energy laps the border of every command, quickening under the thumb. Around the frame, carved
hieroglyphs light up in sequence — the cartouche is reading itself aloud, one sign at a time, and
every pass leaves a different word behind. And under all of it, faint and patient, something older
breathes: violet veins pulse through the glass on a slow tide, the corners curl into tentacle
tracery, and when a panel materialises the reveal line tears with a cyan/magenta fringe, as if the
image were being pulled through from somewhere that does not share our geometry. It reads like a
card game — clear tiers, big labels, one obvious next action — and it feels like handling a
Millennium Item that would rather you did not.

---

## 2. Palette

Tokens are `public static readonly Color` on `EldritchPalette` (§2). Hex values are sRGB; the
project runs Linear color space and Unity converts on load. The legacy `DuelystUi` / `WrldzTheme`
tokens remain valid — Cyan, Gold, TextCream, TextMuted and Void are identical values re-exported
here so old and new chrome sit side by side without a seam.

| Token | Hex | Role | Family |
|---|---|---|---|
| `Void` | `#05070F` | full-screen base, behind everything | frame |
| `Abyss` | `#0B0A1E` | deep indigo-black behind slates; Eldritch-tier fill | eldritch |
| `Obsidian` | `#0E101C` | slate fill (alpha 0.58 / 0.82 / 0.94 by `MenuChromePrefs.PanelAlpha`) | frame |
| `LapisDeep` | `#142A66` | lapis inlay fill — Secondary tiles (DECK · BAG · STORY · BAZAAR) | information |
| `Lapis` | `#1E3A8A` | lapis highlight, tile top gradient | information |
| `SandstoneDark` | `#7A5A34` | cartouche / frame shadow, energy-line tail on gold | frame / lore |
| `Sandstone` | `#C9A36B` | cartouche / frame, dim glyph cells, edge rails | frame / lore |
| `MillenniumGold` | `#FFD647` | primary CTA edge + energy line, glyph read-head glow | action |
| `GoldHot` | `#FFE86B` | hover peak, LP / holographic digits, identity name | action |
| `SolidVisionCyan` | `#33EBFF` | holographic slate edge, scanlines, info caps, Secondary line | information / holo |
| `CyanDeep` | `#0C6B86` | cyan fill undertone, Secondary line tail | information / holo |
| `EldritchViolet` | `#7A2BFF` | Lovecraft accent: veins, UV warp, Zone Mode, tentacle corners | eldritch / AR / zone |
| `AbyssalMagenta` | `#C44BFF` | tear glow, AR anchor tether, Eldritch line head | eldritch / AR / zone |
| `IchorGreen` | `#3CFF8C` | confirm / "ok" bioluminescence | status |
| `BloodDanger` | `#FF4455` | destructive: QUIT, concede, LEAVE ZONE | status |
| `TextCream` | `#FAF8F4` | titles / values | text |
| `TextMuted` | `#B8CEE8` | secondary labels, blurbs, status line | text |

### 2.1 Pairing rules

* **Gold = action.** Only the thing the player is expected to press next carries a gold energy
  line (`EldritchTier.Primary`). One gold hero per screen; WORLD MAP is the single exception and it
  sits at the bottom, small, so the DUEL hero still wins on size.
* **Cyan = information / hologram.** Section caps, scanlines, slate edges, Secondary tiles. Cyan
  never sits on `LapisDeep` as body text (9.3:1 is fine for labels, but use `TextCream` for values).
* **Violet / Magenta = eldritch.** Anything AR, zone, tear or "other side": Zone Mode chip,
  ArHoloHubMenu panel, tether line, vein overlay, tentacle corners. Violet on Abyss is only 3.4:1 —
  it is a *glow*, never a text color. Text on Eldritch surfaces stays `TextCream`.
* **Sandstone = frame / lore.** Cartouche caps, hieroglyph rails, dimmed glyph cells, the trailing
  tail of the gold line. Sandstone text is allowed for epigraphic caps only (8.1:1 on Obsidian).
* **Blood is destructive, Ichor is confirm.** Never use either as a tier decoration.
* **Tier → (edge, line, fill)** — `EldritchPalette.EdgeFor / LineFor / Line2For / FillFor`:

| Tier | Edge | Line head → tail | Fill | Scan | Veins |
|---|---|---|---|---|---|
| Primary | MillenniumGold | MillenniumGold → SandstoneDark | Obsidian @ PanelAlpha | cyan | violet, 0.12 |
| Secondary | SolidVisionCyan | SolidVisionCyan → CyanDeep | LapisDeep @ PanelAlpha | cyan | violet, 0.12 |
| Tertiary | cyan @ 0.55 | cyan @ 0.62 → CyanDeep @ 0.6 | Obsidian @ PanelAlpha − 0.22 | cyan | violet, 0.06 |
| Eldritch | EldritchViolet | AbyssalMagenta → EldritchViolet | Abyss @ PanelAlpha | violet | magenta, 0.55 |
| Danger | BloodDanger | BloodDanger → GoldHot @ 0.9 | Obsidian @ PanelAlpha | blood | blood, 0.22 |

### 2.2 Contrast (outdoor legibility)

Computed WCAG 2.x relative-luminance ratios on the *opaque* fill color. Slates are translucent
(0.58–0.94) over `bg_hub` + LowerDusk, so treat these as best case and keep text outlines on
(`WrldzType.Style(..., heavyOutline: true)` adds a 2-tap dark outline).

| Pair | Ratio | Verdict |
|---|---|---|
| TextCream on Obsidian | 17.8:1 | titles — comfortably ≥ 12:1 target |
| TextCream on Void / Abyss | 19.0 / 18.4:1 | |
| TextCream on LapisDeep | 12.7:1 | Secondary tile labels |
| TextMuted on Obsidian | 11.7:1 | blurbs — fine |
| TextMuted on LapisDeep | 8.4:1 | blurbs on lapis — fine ≥ 24 px |
| MillenniumGold on Obsidian | 13.5:1 | gold caps |
| GoldHot on Obsidian | 15.3:1 | LP digits |
| SolidVisionCyan on Obsidian | 13.0:1 | cyan caps |
| IchorGreen on Obsidian | 14.3:1 | confirm text ok |
| Sandstone on Obsidian | 8.1:1 | epigraphic caps only |
| BloodDanger on Obsidian | 5.6:1 | QUIT label: keep TextCream, blood is the *edge* |
| AbyssalMagenta on Abyss | 5.4:1 | glow only |
| EldritchViolet on Abyss | 3.4:1 | glow only, never text |
| SandstoneDark / CyanDeep on Obsidian | 3.0 / 3.1:1 | tails and undertones only |

Rule of thumb: **text is Cream or Muted; color lives in edges, lines and glows.** The only colored
text tokens are Gold / Cyan / Sandstone caps at ≥ 16 design px.

---

## 3. Typography

Fonts are Resources TTFs under `Assets/Resources/WRLDZ/Fonts/`, served by `FreeUiKit` and wrapped
by `EldritchPalette.Display() / Body() / Ui() / Digits()` (each falls back to
`UiFoundation.BuiltinFont()`).

| Role | Face | Accessor | Where |
|---|---|---|---|
| Shout / display titles | **Bangers** (Rowdies Bold fallback) | `EldritchPalette.Display()` | DUEL hero, panel titles, LEAVE THE SHADOW REALM? |
| Body copy, blurbs | **Exo 2 Bold** | `EldritchPalette.Body()` | button blurbs, identity subtitle, status |
| Button labels, caps | **Russo One** | `EldritchPalette.Ui()` | DECK / BAG / STORY / BAZAAR, section caps |
| Holographic digits | **Orbitron Bold** | `EldritchPalette.Digits()` | LP, Lv, counters |
| Hieroglyphs | **Noto Sans Egyptian Hieroglyphs** (OFL) | `EldritchGlyphs.DynamicFont()` | *reference only* — legacy `Text` cannot render U+13000 surrogate pairs, so glyphs are baked sprites |

### 3.1 Recommended additions (documentation only — not downloaded, not referenced by code)

* **Cinzel Decorative** (OFL) — epigraphic capitals for cartouche caps ("DUEL DISK · SOLID
  VISION", panel titles). Roman-inscription proportions with flourished terminals read as carved
  stone next to Bangers' comic shout. If adopted: drop the TTF into `Resources/WRLDZ/Fonts/`, add
  an accessor beside `Digits()`, and use it only at ≥ 16 design px in all-caps with letter spacing
  emulated by `·` separators (legacy `Text` has no tracking).
* **Cinzel** (regular weights) — lore body on tome/story sheets if Exo 2 feels too technical.
* See `ELDRITCH_ASSET_RECS.md` for licences and sources.

### 3.2 Size ladder (`WrldzType`)

Everything passes through `WrldzType.Readable(designSize, display)`: design sizes ≤ 18 are lifted
first (10→16, 12→18, 14→20, 16→22, 18→24), then scaled by `WrldzType.Scale = 1.78` (display faces
take `max(×1.78, ×1.85)`), then clamped to `MinBody = 20` / `MinDisplay = 24`. Canvas reference is
1080×2340, so the rendered px below are canvas px.

| Design size you pass | Body → px | Display → px | Used for |
|---|---|---|---|
| 12 | 32 | 33 | micro caps, footnotes |
| 14 | 36 | 37 | blurbs, status line, section caps |
| 16 | 39 | 41 | cartouche cap, compact tile labels |
| 18 | 43 | 44 | default button label |
| 22 | 39 | 41 | ArcaneButton default `titleSize` (not lifted: 22 > 18) |
| 24 | 43 | 44 | DECK / BAG / WORLD MAP / QUIT titles |
| 28 | 50 | 52 | identity name, panel titles |

Helpers: `WrldzType.Style(t, size, display, heavyOutline)`, `StyleButtonLabel(t, size, display)`,
`StyleGoldTitle(t, size)`, `StyleCyan(t, size)`, `StyleGaugeDigits(t, px)`. Use `heavyOutline: true`
on anything that sits over a translucent slate in AR.

---

## 4. Component library

All components: `namespace WRLDZ.UI.Eldritch`, `static Create(...)` builders, per-instance
materials released in `OnDestroy` via `ArObjectUtil.Destroy`, unscaled-time motion, edit-mode safe
(`Application.isPlaying == false` snaps to the final state so `MenuSmokeTest` and the prefab baker
can run them). Decorative Images have `raycastTarget = false`. Touch targets are ≥ 56 design px.

### 4.1 `SolidVisionSlate`

**Looks like:** a pane of obsidian glass with a glowing tier-colored edge, cyan scanlines drifting
down, a slow bright sweep top→bottom, faint violet veins that brighten on hover, and a
top→bottom materialise with a chromatic fringe when opened. It is the base surface for every
panel, chip and identity card.

```csharp
using WRLDZ.UI.Eldritch;

// New Image + material + component under `root`.
var slate = SolidVisionSlate.Create(root, "Identity", EldritchTier.Tertiary,
    hoverGlow: false, raycast: false);
GoTheme.Place(slate.Rect, 0.04f, 0.905f, 0.96f, 0.985f);
slate.PlayOpen(0.42f);                      // _Open 0→1 + ember burst (snaps in edit mode)

// Dress an Image you already have (keeps sprite + rect, swaps material).
var dressed = SolidVisionSlate.Attach(existingImage, EldritchTier.Eldritch);
dressed.SetTint(EldritchPalette.WithAlpha(EldritchPalette.Abyss, 0.9f), EldritchPalette.AbyssalMagenta);
dressed.SetHover(1f);                       // eased ~0.15 s
```

API: `Face`, `Mat`, `Tier`, `Rect`, `HoverGlow`, `OpenEmbers`, `SetTier`, `SetHover(float)`,
`PlayOpen(float)`, `SetTint(fill, edge)`; implements `IPointerEnter/ExitHandler` (hover only when
`raycast` is true).

**Tiers:** Primary (gold edge, obsidian), Secondary (cyan edge, lapis), Tertiary (dim cyan,
thinner obsidian — identity slate, chips), Eldritch (violet edge, abyss, veins at 0.55 — Zone
Mode), Danger (blood edge).

**Shader knobs** (`WRLDZ/UI/SolidVisionSlate`, §3.1):

| Property | Meaning |
|---|---|
| `_EdgeColor`, `_EdgeWidth` (px), `_EdgeGlow` | rim glow: `exp(-d/_EdgeWidth) * _EdgeGlow` |
| `_ScanColor`, `_ScanDensity`, `_ScanSpeed`, `_ScanIntensity` | horizontal scanlines (sin on px.y) |
| `_SweepIntensity`, `_SweepSpeed`, `_SweepWidth` | bright band travelling top→bottom |
| `_Hover`, `_HoverGlowMul` | edge glow × `lerp(1, mul, hover)`, veins boosted |
| `_Open` | 0 = invisible, 1 = materialised; reveal line has cyan/magenta fringe |
| `_NoiseTex`, `_Warp` | R channel wobbles UVs (non-Euclidean), keep ≤ 0.03 for text-bearing slates |
| `_VeinColor`, `_VeinStrength` | ridged G-channel veins tinted; B channel = slow breath |
| `_Flicker` | hash flicker on alpha; 0.02–0.06 tasteful, > 0.15 reads as broken |
| `_FillColor` | tint under the sprite; C# `SetTint` writes it |

**Performance:** one Image, one material, one draw. Every slate has its own material (UGUI has
no `MaterialPropertyBlock`), so slates never batch with each other — budget ~20 on screen. The
shader is `#pragma target 2.5`, `half` precision, one noise sample; on mobile it is cheaper than
the `bg_hub` sprite it sits on.

### 4.2 `ArcaneButton`

**Looks like:** a slate with a vertical gradient fill and an *energy line* — a bright head with a
fading tail — lapping the border clockwise, plus a mana haze bleeding inward from the edge. Hover
widens and brightens the line and bursts 10–14 embers; press darkens the fill and snaps the head
brighter; click sparks 6. Title left (or centred), blurb under it, icon in the right 30 % when a
blurb is present. Optional hieroglyph strip along the top for the hero.

```csharp
var hero = ArcaneButton.Create(tiles, "DUEL", OpenDuelChoice, EldritchTier.Primary,
    blurb: "Summon the Solid Vision", icon: null, titleSize: 30,
    centerTitle: false, hieroglyphBorder: true);
GoTheme.Place(hero.Rect, 0.04f, 0.575f, 0.96f, 0.855f);

var deck = ArcaneButton.Create(tiles, "DECK", OpenDeckHub, EldritchTier.Secondary,
    "Build · edit · style", ImagineAssets.IconDeck(), 24);
deck.SetInteractable(false);                // dims the line, keeps the label readable
deck.SetTier(EldritchTier.Tertiary);        // rebinds palette on the existing material
```

API: `Button`, `Face`, `Mat`, `Title`, `Blurb`, `Icon`, `Rect`, `SetInteractable(bool)`,
`SetTier(EldritchTier)`. Child names are `"Title"`, `"Blurb"`, `"Ico"` — the same as `HubChrome`,
so `GoTheme.Place(btn.transform.Find("Title") …)` tweaks written for the old hub keep working.

**Tiers:** Primary = hero / WORLD MAP · Secondary = 2×2 COMMAND grid and Zone Mode rows ·
Tertiary = VIEW / SET / CLOSE / STAY · Eldritch = ZONE MODE choice · Danger = QUIT / LEAVE ZONE.

**Shader knobs** (`WRLDZ/UI/ArcaneEnergyFlow`, §3.2):

| Property | Meaning |
|---|---|
| `_FillColorA`, `_FillColorB` | top → bottom gradient |
| `_BorderInset` (px) | where the ring runs, measured from the edge |
| `_LineColor` → `_LineColor2` | head → tail color along the tail |
| `_LineWidth` (px), `_LineLength` (0–1 of perimeter), `_LineCount` (1–4), `_LineSpeed` (laps/s, negative = anticlockwise) | the energy line |
| `_Bloom` | head brightness multiplier (HDR is on — > 2 blooms in URP post) |
| `_HazeStrength` | inner mana haze near the border |
| `_HazeWidth` | haze falloff in px (fixed, so large plates do not read as a bevelled tunnel) |
| `_Phase` | comet phase offset 0–1; set per instance so neighbouring tiles desynchronise |
| `_Hover`, `_Pressed`, `_Open`, `_Flicker`, `_NoiseTex` | as slate |

**Performance:** same one-draw-per-button budget as the slate; the Title/Blurb/Icon are normal
UGUI and batch with the rest of the canvas text. Disable `hieroglyphBorder` on anything smaller
than a hero — each border is a row of Images.

### 4.3 `HieroglyphBorder`

**Looks like:** a strip (or ring) of carved signs. A "read head" runs along the cells: the active
cell burns gold, the 3 behind it fade through sandstone, everything else sits dim. When the head
passes, that cell is swapped for a new random glyph — the wall is rewriting itself.

```csharp
var strip = HieroglyphBorder.Create(cap, HieroglyphBorder.Edge.Top, thicknessPx: 30f,
    glow: EldritchPalette.MillenniumGold, cellsPerSecond: 3f);          // glyphs: null → atlas

var rail = HieroglyphBorder.Create(root, HieroglyphBorder.Edge.Right, 22f,
    EldritchPalette.WithAlpha(EldritchPalette.Sandstone, 0.55f), 1.2f);
rail.Reverse = true;          // read the other way on the right rail
rail.SetIntensity(0.45f);     // dim decorative rail; hover boost on buttons uses > 1
```

Fields: `CellsPerSecond`, `Glow`, `Reverse`; `SetIntensity(float)`. `Edge.Ring` builds all four
sides (used by `HieroglyphPanel`). Cells are Images with one shared `EldritchMaterials.NewGlyph`
material per border; each cell's `Image.color.a` is its **pulse phase**, which is how 40 cells
share one material and still pulse out of step.

**Shader knobs** (`WRLDZ/UI/HieroglyphGlow`, §3.3): `_GlowColor`, `_Glow` (0–3), `_Pulse`,
`_PulseSpeed`, `_Spread` (halo radius in px via 4 extra taps). `IN.color.rgb` = tint,
`IN.color.a` = phase.

**Performance:** one material per border → all cells in a border batch into one draw as long as
they stay in one sub-canvas. Cell count = edge length / thickness; a 1080-wide top strip at 30 px
is ~36 cells. Keep rails thin (22 px) and slow (1.2 cells/s).

### 4.4 `HieroglyphPanel`

**Looks like:** a Solid Vision slate wrapped in a hieroglyph ring, tentacle tracery in each corner
tinted violet at low alpha, a cartouche-capped title, and a safe-inset `Body` for content. Opens
with the slate materialise, a stagger of the body children, and a burst of 24 dust + 10 embers off
the top edge. Closes with `MenuMotion.SheetOut` then destroys itself.

```csharp
var panel = HieroglyphPanel.Create(root, "DuelChoice", EldritchTier.Primary,
    title: "CHOOSE YOUR DUEL", ring: true, embersOnOpen: true);
GoTheme.Place(panel.Root, 0.06f, 0.30f, 0.94f, 0.80f);

var vsAi = ArcaneButton.Create(panel.Body, "VS AI", () => OpenHubOverlay(MenuId.ArDuelCreate),
    EldritchTier.Primary, "Practice · table · street", ImagineAssets.IconDuel(), 24);
GoTheme.Place(vsAi.Rect, 0.04f, 0.70f, 0.96f, 0.96f);

panel.Open();
// later
panel.Close(onDone: () => _popover = null);
```

API: `Root`, `Body`, `Slate`, `Border`, `TitleText`, `Open()`, `Close(Action)`. Tier drives the
slate, the ring glow and the title color. Use Eldritch for anything AR (ArHoloHubMenu), Danger for
the QUIT confirm.

**Performance:** slate (1 draw) + ring (1 draw) + 4 corner decals + 2 caps + text. Fine for one or
two popovers; do not nest panels.

### 4.5 `UiEmberField`

**Why it exists:** canvases are `ScreenSpaceOverlay` and `ParticleSystem` cannot render inside
one. `UiEmberField` is a pool of `Image` motes (`fx_ember_soft`, `fx_dust_mote`, `fx_arcane_spark`)
simulated on unscaled time with slightly negative gravity (embers rise), `raycastTarget = false`,
zero allocations after warm-up.

```csharp
// Ambient dust across the whole menu canvas (48 motes, ambient on).
var field = UiEmberField.Attach(canvas, poolSize: 48, ambient: true);
field.AmbientRate = 4f;

// Burst from a button's rect (what ArcaneButton does on hover).
var fx = EldritchMenuFx.Field(button.transform);   // find-or-attach on the root canvas
fx.BurstFromRect(button.Rect, 12, UiEmberField.Kind.Ember, EldritchPalette.MillenniumGold);
fx.Burst(new Vector2(0f, 200f), 6, UiEmberField.Kind.Spark, EldritchPalette.SolidVisionCyan);
```

API: `Attach(host, poolSize, ambient)`, `Field(Canvas)` / `Field(Transform)` (also on the
`EldritchMenuFx` helper), `Burst(localPos, count, kind, tint)`, `BurstFromRect(rt, count, kind,
tint[, yMin01, yMax01])`, `Ambient`, `AmbientRate`, `Clear()`, `PoolSize`, `Alive`. Ambient turns
itself off when `FindAnyObjectByType<MenuShell>()?.BatterySaver == true`.

**Performance:** pool of 48 → 48 Images that share the default UI material and batch. Bursts
that exceed the free pool recycle the oldest mote; they never allocate.

### 4.6 `ArcaneEmberParticles`

**Why it exists:** the AR stage is world-space, so a real `ParticleSystem` is allowed there. This
builds a ring of rune-embers around the Spirit Dueler disk hub in Zone Mode.

```csharp
var ps = ArcaneEmberParticles.Create(hubPivot /* child of PlayerDisk.transform at the wrist */,
    EldritchPalette.EldritchViolet, radius: 0.06f, layer: 28, loop: true);
```

Returns the configured `ParticleSystem` (shape = ring of `radius` metres, `fx_ember_soft` /
`fx_arcane_spark` sprites, `layer` 28 = AR stage). Never parent it to a canvas.

---

## 5. Shaders

All three start from the `UI/Default` skeleton (`_MainTex [PerRendererData]`, stencil block,
`_ColorMask`, `UNITY_UI_CLIP_RECT` / `UNITY_UI_ALPHACLIP` multi_compile_local, `_ClipRect`,
`_TextureSampleAdd`), blend `One OneMinusSrcAlpha` with premultiplied output, `#pragma target 3.0`
(the two rect shaders use `fwidth`; the glyph shader stays at 2.5-class cost).
Because the skeleton is intact, `RectMask2D`, `Mask` (stencil) and `CanvasGroup` alpha all work.

### 5.1 The rect-space trick (`_RectSize`, UV-derived px, derivative fallback)

UGUI gives a fragment shader only UVs, and a `Simple` white sprite's UVs are 0–1 across any
size — a 1-px border in UV would be 10 px wide on a 1000-px button. UGUI also batches vertices
into **canvas-root space** before the shader runs (that is why `UI/Default` compares `v.vertex.xy`
against the canvas-space `_ClipRect`), so `v.vertex.xy` is *not* Image-local and cannot be
re-based with the pivot. The shaders therefore rebuild rect-space pixels from the sprite UV of the
full-rect quad, and the C# component writes the RectTransform's size into the material:

```csharp
// EldritchMaterials.SetRect — called on Create, OnRectTransformDimensionsChange, and LateUpdate
// when dirty. Components carry [ExecuteAlways] so this also runs in edit mode (previews, baking).
m.SetVector(RectSizeId,  new Vector4(rt.rect.width, rt.rect.height, 0, 0));
m.SetVector(RectPivotId, new Vector4(rt.pivot.x, rt.pivot.y, 0, 0));   // API compatibility; unused
```

```hlsl
// When C# has not synced yet (first frame, prefab thumbnail, edit-mode still) derive the
// on-screen quad size from the UV gradient instead — fwidth is constant across a flat quad.
float2 szSynced = _RectSize.xy;
float2 szAuto   = 1.0 / max(fwidth(IN.rectUV), 1e-5);
float2 sz = max((szSynced.x < 2.0 || szSynced.y < 2.0) ? szAuto : szSynced, 1.0);
float2 px = IN.rectUV * sz;                                        // px ∈ [0, W] × [0, H]
float  d  = min(min(px.x, sz.x - px.x), min(px.y, sz.y - px.y));   // px to nearest edge
```

Everything sized in *pixels* (`_EdgeWidth`, `_BorderInset`, `_LineWidth`, `_HazeWidth`,
`_Spread`) is derived from `px` / `d`, so a slate looks identical at 300 px or 1000 px. For the
energy line the perimeter parameter `t ∈ [0,1)` (clockwise from the top-left corner) runs along
the ring rect inset by `_BorderInset` (normalized by `2(W′+H′)`), so it is exactly continuous at
the ring corners and the comet walks the border at constant speed regardless of aspect. `_Phase`
offsets the comet so a grid of tiles never fires in lockstep (`ArcaneButton` assigns one per
instance from a golden-ratio sequence).

Consequences: (1) only `Image.Type.Simple` on a full-rect sprite is supported — Sliced / Tiled /
Filled and atlas sub-rects change the UVs (`EldritchMaterials.Bind` forces Simple + the white
sprite); (2) `_RectSize` is per material, which is the other reason materials are per instance;
(3) the derivative fallback measures *screen* pixels while `_RectSize` is in canvas units — on a
scaled canvas the two differ by the scale factor, which only matters for the first frame. Both
shaders declare `_RectSize = (0,0,0,0)` so an unsynced material (a baked `.mat` previewed in the
Inspector, a prefab thumbnail) takes the derivative path instead of silently drawing for some
authored default rect.

### 5.2 Hover / open / pressed drivers

`_Hover`, `_Open`, `_Pressed` are plain floats driven from C# (`EldritchMaterials.SetHover /
SetOpen / SetPressed`), eased by coroutines on unscaled time via `MenuMotion`. In edit mode
(`!Application.isPlaying`) the components write the final value immediately, so a captured PNG or
a baked prefab shows the "open, unhovered" state. `SolidVisionSlate`, `ArcaneButton` and
`HieroglyphBorder` are `[ExecuteAlways]` so rect sync and cell counts follow layout in edit mode
too; `EldritchMaterials.SyncRects(root)` forces a pass after `Canvas.ForceUpdateCanvases()` — the
preview tool calls it before every capture. Time in the shaders is `_Time.y`, so scanlines
and lines animate even when the game is paused (`Time.timeScale = 0`).

### 5.3 Masking compatibility

* `RectMask2D` → `UNITY_UI_CLIP_RECT` path, works.
* `Mask` component → stencil props intact, works; `_ColorMask` respected.
* `CanvasGroup.alpha` → arrives in `IN.color.a`; the slate multiplies its whole output by it.
  `HieroglyphGlow` **repurposes `IN.color.a` as phase**, so a `CanvasGroup` cannot reach the cells
  the usual way. `HieroglyphBorder` mirrors the nearest `CanvasGroup.alpha` into the shader's
  `_Fade` every `LateUpdate`, so panels and the Zone Mode canvas fade their glyph rings correctly.
  Still do not drive a border's opacity through `Image.color.a` — use `SetIntensity`, which scales
  the HDR-capable `_Glow` (vertex colour is clamped to `Color32`, so values above 1 would be lost).
  `EldritchMaterials.NewGlyph` also supplies `_GlyphTexel` from the atlas: `_MainTex` is
  `[PerRendererData]` and bound by `CanvasRenderer`, so `_MainTex_TexelSize` never describes the
  atlas and the 4-tap halo would sample ~0.75 UV away.

### 5.4 What to avoid

* Sharing one material between two Images (rect size + hover bleed). Always `New*`.
* `_Warp` above ~0.03 on any slate that carries text — the text is a separate Image and does not
  warp with the glass, so the mismatch reads as misregistration.
* `_Bloom` > 2.5 or `_EdgeGlow` > 3 outdoors: HDR + URP bloom will white-out the label.
* Sprite atlases with tight packing on the slate face (`_MainTex` is sampled with warped UVs and
  will bleed into neighbours). The white sprite is the intended face.
* Forgetting `_NoiseTex`: veins, warp, flicker and breath all read it; without it the slate is
  flat (not broken). `EldritchMaterials.AssignNoise(m)` handles the StreamingAssets fallback.
* Loading `tex_eldritch_noise.png` as a colour texture. Its R/G/B are *data* (warp · veins ·
  breath), so `EldritchAssets.EldritchNoise()` loads it **linear** and the baker sets
  `sRGBTexture = false`; an sRGB load would gamma-decode every sample under Linear colour space
  (0.5 arriving as ~0.21) and runtime would stop matching the baked materials.

---

## 6. Main menu hierarchy (§6)

Built by `DuelDiskMenuUI.BuildVisuals` on `WrldzTheme.Canvas("DiskMenuCanvas", 50)`. Anchors are
`y0–y1` of the 1080×2340 portrait canvas.

```
 ┌──────────────────────────────────────────────────────────┐ 1.000
 │▒ ╭────────────────────────────────────────────────────╮ ▒│
 │▒ │ (portrait)  DUELIST NAME             ◈ soul badge  │ ▒│ 0.905–0.985  Identity · SolidVisionSlate Tertiary
 │▒ │             Lv 12 · Team Kuriboh · title           │ ▒│
 │▒ ╰────────────────────────────────────────────────────╯ ▒│
 │▒   ]═ DUEL DISK · SOLID VISION ═[  𓀀𓁐𓂀𓃀𓄿𓅓𓆑𓇋𓈖 ▒│ 0.862–0.900  cartouche cap + HieroglyphBorder.Top
 │▒ ╔════════════════════════════════════════════════════╗ ▒│
 │▒ ║ 𓀀𓁐𓂀𓃀𓄿𓅓𓆑𓇋𓈖𓉐𓊃𓋴𓌳𓍿𓎛𓏏𓐍  (glyph strip)     ║ ▒│
 │▒ ║                                                    ║ ▒│
 │▒ ║   DUEL                              ╭──────────╮   ║ ▒│ 0.575–0.855  HERO · ArcaneButton Primary
 │▒ ║   Summon the Solid Vision           │  disk    │   ║ ▒│              gold energy line laps the border
 │▒ ║                                     │ showcase │   ║ ▒│              SpiritDuelerDiskView.HubShowcase right 38 %
 │▒ ║                                     ╰──────────╯   ║ ▒│
 │▒ ╚════════════════════════════════════════════════════╝ ▒│
 │▒  — COMMAND —                                            ▒│ 0.530–0.568  HubChrome.SectionCap cyan
 │▒ ┌───────────────────────┐ ┌───────────────────────┐    ▒│
 │▒ │ DECK            [ico] │ │ BAG             [ico] │    ▒│ 0.440–0.525  ArcaneButton Secondary (lapis)
 │▒ └───────────────────────┘ └───────────────────────┘    ▒│
 │▒ ┌───────────────────────┐ ┌───────────────────────┐    ▒│
 │▒ │ STORY           [ico] │ │ BAZAAR          [ico] │    ▒│ 0.345–0.430
 │▒ └───────────────────────┘ └───────────────────────┘    ▒│
 │▒ ┌───────────────────────┐ ┌───────────────────────┐    ▒│
 │▒ │ VIEW                  │ │ SET                   │    ▒│ 0.250–0.335  ArcaneButton Tertiary (compact)
 │▒ └───────────────────────┘ └───────────────────────┘    ▒│
 │▒ ┌──────────────────────────────────┐ ┌─────────────┐   ▒│
 │▒ │ WORLD MAP  Return to the streets │ │    QUIT     │   ▒│ 0.135–0.235  Primary (compass) | Danger 28 %
 │▒ └──────────────────────────────────┘ └─────────────┘   ▒│
 │▒               status line (_status)                    ▒│ 0.100–0.130
 └──────────────────────────────────────────────────────────┘ 0.000
   ▒ = HieroglyphBorder.Left / .Right rails (22 px, sandstone @ 0.55, intensity 0.45, right rail reversed)
   Background: bg_hub sprite → EgyptianAgesAtmosphere(NewKingdom) → Root → LowerDusk (0–0.58)
               UiEmberField ambient dust on the canvas root
```

DUEL opens a `HieroglyphPanel` popover (Primary, 0.30–0.80) titled CHOOSE YOUR DUEL with three
`ArcaneButton`s: **VS AI** (Primary → `OpenHubOverlay(MenuId.ArDuelCreate)`), **VS PLAYER**
(Secondary → `MenuId.ArDuelPvpCreate`), **ZONE MODE** (Eldritch →
`ZoneModeSession.EnterFromHub(transform)`), plus CLOSE (Tertiary). QUIT opens a Danger panel
"LEAVE THE SHADOW REALM?" → confirm runs `AppSession.Ensure().Logout(); GoBoot();`.

### 6.1 Visual weight: size → color → glow → motion

The eye is guided in that order, and each tier spends less of each budget than the one above it:

| | Primary (DUEL) | Secondary (COMMAND 2×2) | Tertiary (VIEW · SET) | Utility (WORLD MAP · QUIT) |
|---|---|---|---|---|
| **Size** | 28 % of screen height | 8.5 % each, half width | 8.5 %, compact labels | 10 %, split 66 / 28 |
| **Color** | gold on obsidian | cyan on lapis | dim cyan on thin obsidian | gold (map) / blood (quit) |
| **Glow** | edge 1.6 + bloom, hieroglyph strip | edge 1.0 | edge 0.6, no haze | as tier |
| **Motion** | line 1 lap/s, scanlines, disk showcase idles | line 0.6 lap/s | line 0.35 lap/s, no hover embers | as tier |

Only one element on the screen may be all four at once. If a designer adds a promo tile, it takes
Secondary at most.

---

## 7. Zone Mode (§7)

**Player story.** In the Overworld the player walks into a zone (a Tear pin in range) and the
`ZoneModePrompt` offers ENTER AR / DIGITAL and now **ZONE MODE**. Zone Mode does not start a duel:
it puts the player in the zone *with their Duel Disk on*. The AR stage comes up, the Spirit Dueler
disk is traced onto the left forearm (`SimulatedArmTracker` in the Editor, real hand tracking on
device) folded in its cuff. Tapping the disk hub unfolds a holographic menu tethered to the hub —
AR variants of the main-menu destinations — and a second tap folds it away. The Tear is
still there; DUEL HERE launches the fight through the normal `ZoneModePrompt.Launch` path.

### 7.1 Flow

```
Overworld ─ tap Tear pin in range ─▶ ZoneModePrompt.Build(...)
                                         │ ENTER AR / DIGITAL  (existing → duel)
                                         │ ZONE MODE           (new capsule, Eldritch tier)
                                         ▼ onClose(); ZoneModeSession.Enter(parent.root, zoneId, zoneTitle, kind)
MainMenu ─ DUEL ▶ ZONE MODE ─▶ ZoneModeSession.EnterFromHub(transform)   (always the Training "preview" zone — the hub has no GPS context)

ZoneModeSession.Enter
  ├─ WrldzTheme.Canvas("ZoneModeCanvas", 88)
  ├─ ArDuelSpace.CreateInUi(canvas, 0,0,1,1, host)  → stage + StageCamera + ArDuelInteractionSystem
  ├─ wait Space.Ready
  │    ├─ hide Interaction.OppDisk, both hand volumes, Arena root
  │    ├─ PlayerDisk.FadeInRetracted()                          (folded cuff on the left forearm)
  │    ├─ ArDiskHubHit on PlayerDisk.transform (rig root = tracked forearm), centre (0, WristHoverY, 0),
  │    │    BoxCollider isTrigger ≈ 0.20×0.10×0.18 m, disk layer — stays put while the blade folds
  │    ├─ ArcaneEmberParticles.Create(HubEmberPivot under PlayerDisk.transform at the wrist, EldritchViolet)
  │    └─ ArDiskHubTapRouter.Attach(rawImage, Space.StageCamera, OnHubTapped)
  ├─ ScreenRouter.Ensure().Presentation = UiPresentation.ArDiskHolo
  └─ chips: "ZONE MODE · {zoneTitle}" (SolidVisionSlate Eldritch) · hint "Tap your Duel Disk" · LEAVE (top-right)

tap on RawImage ─▶ ArDiskHubTapRouter.OnPointerClick
  ├─ Interaction.Drag.OwnsPointer? → ignore
  ├─ ScreenPointToLocalPointInRectangle → viewport → StageCamera.ViewportPointToRay
  ├─ Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide)
  └─ first hit with ArDiskHubHit in parents → onHubTapped()
        ├─ HubMenu == null → ArHoloHubMenu.Open(session)   (hint fades)
        └─ else            → HubMenu.Close()               (toggle)

ArHoloHubMenu.Open
  ├─ HieroglyphPanel (Eldritch) "SPIRIT DUELER", anchors MenuChromePrefs.GetWindowAnchors(ArDiskHolo, …)
  ├─ ArDiskHoloAnchor: target = HubHit (wrist) → panel bottom-centre 40 px above it, fx_tether_line hub→panel
  └─ rows (ArcaneButton Secondary, compact):
       DUEL HERE → ZoneModePrompt.Launch(zoneId, zoneTitle, digital:false, onClose, kind, 0, boss:false)
       DECK      → DeckCollectionScreen.Build(ModalHost, close, UiPresentation.ArDiskHolo)
       BAG       → InventoryScreen.Build(…, ArDiskHolo)
       ARTIFACTS → ArtifactBoxScreen.Build(…, ArDiskHolo)
       TOME      → SystemsSheets.BuildTome(…, ArDiskHolo)
       SETTINGS  → SettingsScreen.Build(ModalHost, null, close)
       LEAVE ZONE→ session.Exit()

ZoneModeSession.Exit
  ├─ PlayerDisk.RetractThenFadeOut()
  ├─ Presentation = NonArPortrait
  └─ destroy stage + canvas after 0.6 s → onExit()
```

### 7.2 How the three AR pieces cooperate

* **`ArDiskHubHit`** (`WRLDZ.Presentation.ArInteraction`) is a marker, like `ArDiskPhaseHit` /
  `ArGraveyardHit`: a trigger `BoxCollider` at the wrist hub, on the disk's layer (28). It is
  parented to the rig root (the tracked left-forearm pose), not `DiskRoot`, so the blade's
  retract/deploy swing never moves the tap target. It has no logic; it exists so a raycast can
  say "that was the hub".
* **`ArDiskHubTapRouter`** owns *input*. It is an `IPointerClickHandler` on the AR `RawImage`
  (the same surface `ArViewportInput` uses), converts the UGUI click into a stage-camera ray and
  fires a callback when the ray meets an `ArDiskHubHit`. It defers to the card drag system
  (`Interaction.Drag.OwnsPointer`) so a dropped card never opens the menu.
* **`ArDiskHoloAnchor`** owns *placement*. Every `LateUpdate` it projects the hub's world position
  through `StageCamera` into the RawImage rect and pins the panel 40 px above it (clamped to the
  safe area), stretching a `fx_tether_line` Image between them. The menu therefore follows the
  forearm as the tracker moves.
* **`ArHoloHubMenu`** owns *content*: the `HieroglyphPanel` and its rows. It knows nothing about
  raycasts or projection — it is handed a `ZoneModeSession` and an anchor.

### 7.3 Phone vs lenses

| | Phone (`ArPresentationTarget.IsPhone`) | Lenses / wearable (`ArPresentationTarget.IsWearable`) |
|---|---|---|
| Menu canvas | the `ZoneModeCanvas` overlay (ScreenSpaceOverlay, sort 88) | a `RenderMode.WorldSpace` canvas, scale 0.0008, parented to the hub target (wrist), facing `StageCamera` |

`ZoneModeSession` carries `StartingLp` from whatever opened it, and **DUEL HERE** passes it to
`ZoneModePrompt.Launch` — without it an unlocked 8000 LP street wanderer entered through Zone Mode
would silently become a 4000 LP duel, because `Launch` picks the street band from that number.
The session is always parented at **scene root**, never under the prompt's host: the prompt's
canvas is destroyed (`ZoneModePromptCanvas`) or deactivated (`MenuShell` overlay) by its own
`onClose` a frame later, which would freeze the session's coroutines.
| Anchoring | `ArDiskHoloAnchor` projection each frame + tether Image | none — the canvas rides the disk; projection skipped |
| Input | tap on RawImage → `ArDiskHubTapRouter` | same router (the stage still renders to the viewport RawImage) |
| Particles | `ArcaneEmberParticles` in world space (real `ParticleSystem`) | same |
| Presentation | `UiPresentation.ArDiskHolo` | `UiPresentation.ArDiskHolo` |

---

## 8. Boot → MainMenu

`BootFlowUI.WorldLoadRoutine` (≈ line 1165) used to hand off to the Overworld. It now shows
"Opening the Duel Disk…" / "Calibrating the Solid Vision…" and ends with
`AppSession.Ensure().GoMainMenu()`. The scene graph is **Boot → MainMenu → Overworld** (via WORLD
MAP) and the Overworld's disk HUB returns to MainMenu. `AppSession.GoOverworld()` is unchanged;
onboarding (starter kit, tutorial badge) still runs inside `WorldLoadRoutine` before the handoff.
Nothing in `ProjectSettings` changes — `MainMenu` was already build index 2.

---

## 9. Step-by-step integration

**(a) Pull the branch and let the Editor import.**
Branch `feat/glb-shell-models`. Open the project in Unity 6000.5.10f1 and let it import
`Assets/Scripts/WRLDZ/UI/Eldritch/` (runtime + `Shaders/` + `ZoneMode/`),
`Assets/Editor/WRLDZ/EldritchAssetBaker.cs`, `EldritchMenuPreview.cs`, and the baked art in
`Assets/StreamingAssets/WRLDZ/Eldritch/`:

| File | Use |
|---|---|
| `hieroglyph_atlas.png` (1024², 8×8 × 128 px, white ink) + `hieroglyph_atlas.json` (64 codepoints) | `EldritchGlyphs.Atlas()` |
| `hieroglyph_atlas_glow.png` | `EldritchGlyphs.AtlasGlow()` halo sprites |
| `fx_ember_soft.png` 64² · `fx_dust_mote.png` 32² · `fx_arcane_spark.png` 64² | `UiEmberField`, `ArcaneEmberParticles` |
| `fx_rune_ring.png` 256² | AR hub pulse ring, panel title ornament |
| `fx_tether_line.png` 8×64 | `ArDiskHoloAnchor` tether |
| `tex_sandstone_tile.png` 256² gray seamless | frame texture (tint Sandstone) |
| `tex_lapis_tile.png` 256² | Secondary tile fill |
| `tex_eldritch_noise.png` 256² (R warp · G veins · B breath) | shader `_NoiseTex` |
| `deco_tentacle_corner.png` 256² (+ `deco_tentacle_corner_mask.png`) | `HieroglyphPanel` corners |
| `deco_cartouche_end.png` 64×192 | title cartouche caps |

Also present: `Assets/Resources/WRLDZ/Fonts/NotoSansEgyptianHieroglyphs-Regular.ttf` (+ OFL
text). StreamingAssets are loaded at runtime by `StreamingSprite.Load` / `LoadTexture`, so they
need no import settings. Wait for the shader compile to finish (`WRLDZ/UI/SolidVisionSlate`,
`ArcaneEnergyFlow`, `HieroglyphGlow` appear under the WRLDZ group in any material's shader picker).

**(b) Bake.** Menu **WRLDZ ▸ Eldritch ▸ Bake All** (or the individual `Bake Materials`,
`Bake Prefabs`, `Bake Hieroglyph Atlas Asset`). Output under `Assets/WRLDZ/Eldritch/`:

```
Assets/WRLDZ/Eldritch/
  Materials/  M_SolidVisionSlate_{Primary,Secondary,Tertiary,Eldritch,Danger}.mat
              M_ArcaneEnergyFlow_{Tier}.mat · M_HieroglyphGlow.mat
  Prefabs/    ArcaneButton.prefab · HieroglyphPanel.prefab · SolidVisionSlate.prefab
              HieroglyphBorder.prefab · ArcaneEmberParticles.prefab
  Textures/   hieroglyph_atlas.png  (Sprite Multiple, 64 rects glyph_00..63, no compression, no mipmaps)
              tex_eldritch_noise.png (linear, Repeat — assigned to every baked material's _NoiseTex)
              Glyphs/glyph_00..63.png  (only when multi-sprite slicing is unavailable — see below)
```

The atlas is sliced through `UnityEditor.U2D.Sprites.ISpriteEditorDataProvider` (2D Sprite
package, reached by reflection so the Editor assembly compiles without it). If that path yields no
sub-sprites the baker cuts 64 single-sprite PNGs into `Textures/Glyphs/` instead. `Bake All` runs
every step and reports each line; only materials + prefabs decide its PASS / FAIL.

Prefabs are built by calling the runtime `Create` builders inside a temporary canvas and saving
with `PrefabUtility.SaveAsPrefabAsset`; the runtime never loads them. Re-run Bake All after
changing palette tokens or shader defaults.

**(c) Dropping components into a scene canvas by hand.**
*From code (preferred, matches the rest of the project):*

```csharp
using UnityEngine;
using WRLDZ.UI;            // GoTheme, WrldzTheme
using WRLDZ.UI.Eldritch;

public class MyPanelBootstrap : MonoBehaviour
{
    void Start()
    {
        var canvas = WrldzTheme.Canvas("MyCanvas", 60);           // ScreenSpaceOverlay + portrait scaler
        UiEmberField.Attach(canvas, 32, ambient: true);

        var panel = HieroglyphPanel.Create(canvas, "MyPanel", EldritchTier.Secondary, "ARCHIVES");
        GoTheme.Place(panel.Root, 0.06f, 0.20f, 0.94f, 0.80f);

        var ok = ArcaneButton.Create(panel.Body, "OPEN", () => Debug.Log("[WRLDZ Eldritch] open"),
            EldritchTier.Primary, "Read the tome", null, 24);
        GoTheme.Place(ok.Rect, 0.06f, 0.06f, 0.94f, 0.22f);

        panel.Open();
    }
}
```

*From prefabs (Editor drag-and-drop):* the scene canvas must be `ScreenSpaceOverlay` (or
`ScreenSpaceCamera`) with a `CanvasScaler` — `WrldzTheme.Canvas` or `UiTheme.ApplyPortraitPhone`.
Drag `Prefabs/ArcaneButton.prefab` under it, set the RectTransform anchors, then in the Inspector
edit the `Title` / `Blurb` child `Text`s and hook `Button.onClick`. Prefab materials are the baked
`M_*` assets: **the component replaces them with a per-instance copy in `Awake`** (it re-binds its
`Image`, rebuilds the material from the serialized `_tier`, and takes a fresh energy-line phase), so
editing a baked material only changes what a *newly baked* prefab starts from. `HieroglyphPanel.prefab` exposes
`Body` — parent your content there, not on `Root`. `ArcaneEmberParticles.prefab` is world-space
only; drop it under an AR stage object, never under a canvas.

**(d) Preview PNG.** **WRLDZ ▸ Eldritch ▸ Preview Main Menu (edit mode)** builds `DuelDiskMenuUI`
in a temporary `ScreenSpaceCamera` canvas, renders it through a temp camera into a 1080×2340
RenderTexture (same RT → `ReadPixels` → `EncodeToPNG` pattern as `PovDuelRecorder`) and writes
`Assets/Screenshots/eldritch_main_menu.png`. **Preview Zone Mode Hub Menu** writes
`eldritch_ar_hub_menu.png` (ArHoloHubMenu on a plain canvas, anchor centred, no AR stage). From
the CLI with a live Editor:

```bash
unity cmd eval --timeout 120000 --code 'return WRLDZ.EditorTools.EldritchMenuPreview.CapturePng("Assets/Screenshots/eldritch_main_menu.png");'
unity cmd eval --timeout 120000 --code 'return WRLDZ.EditorTools.EldritchMenuPreview.RunBatch();'   # "PASS" / "FAIL: …"
```

**(e) Smoke test.** **WRLDZ ▸ Lab ▸ Run Menu Smoke Test** (`MenuSmokeTest.Run()`, or
`RunBatch()` headless). The Eldritch checks: `SolidVisionSlate.Create`, `ArcaneButton.Create`
(Title present; material shader contains "ArcaneEnergyFlow", or the `UI/Default` fallback is
logged), `HieroglyphBorder.Create` ≥ 8 cells, `HieroglyphPanel.Create` + `Open()`,
`UiEmberField.Attach` + `Burst`, `DuelDiskMenuUI.Build` finds DUEL · DECK · BAG · STORY · BAZAAR ·
WORLD MAP · QUIT, and `ArHoloHubMenu` builds standalone with DUEL HERE and LEAVE ZONE.

**(f) Tuning knobs.**

| Knob | Where | Effect |
|---|---|---|
| Slate opacity | `MenuChromePrefs.Opacity` (Settings → cycles Clear 0.58 / Glass 0.82 / Solid 0.94) | `EldritchPalette.PanelAlpha` → every tier fill alpha; Tertiary is −0.22 |
| Sheet size | `MenuChromePrefs.Size` (Compact / Medium / Large) | `GetWindowAnchors` for AR hub menu and deck sheets |
| Tier colors | `EldritchPalette.EdgeFor / LineFor / Line2For / FillFor / ScanFor / VeinFor / VeinStrengthFor` | one switch per role; re-run Bake All afterwards |
| Energy line | `EldritchMaterials.ApplyFlowTier` defaults, or per button `btn.Mat.SetFloat(EldritchMaterials.LineSpeedId, 0.6f)` (also `LineWidthId`, `LineLengthId`, `LineCountId`, `BloomId`, `HazeStrengthId`) | speed in laps/s (negative = anticlockwise), width px, tail fraction, heads, brightness |
| Slate edge / scan | `slate.Mat.SetFloat(EldritchMaterials.EdgeGlowId, 1.4f)`, `EdgeWidthId`; `_ScanDensity`, `_ScanSpeed`, `_ScanIntensity` by name | rim intensity, scanline look |
| Glyph read speed | `HieroglyphBorder.CellsPerSecond`, `Reverse`, `SetIntensity` | cells/s, direction, brightness |
| Ambient particles | `UiEmberField.Ambient`, `AmbientRate`; `MenuShell.SetBatterySaver(true)` disables ambient | density |
| Vein / warp | `_VeinStrength`, `_Warp`, `_Flicker` on any slate material | how much Lovecraft |

**(g) Your own hieroglyph sprites / particle textures.** Everything is drop-in by path under
`Assets/StreamingAssets/WRLDZ/Eldritch/` (constants in `EldritchAssets.File*`):

* **Atlas:** replace `hieroglyph_atlas.png` with a 1024² PNG, 8×8 grid of 128-px cells, white
  ink on transparent (row-major from the top-left), and `hieroglyph_atlas_glow.png` with the
  blurred halo of the same layout. Update `hieroglyph_atlas.json` — `{"cell":128,"grid":8,
  "count":64,"codepoints":["U+13080", …],"license":"…"}`; `count` may be < 64 to use only the
  first N cells. `EldritchGlyphs.Atlas()` slices by `cell`/`grid`. Re-run
  `Bake Hieroglyph Atlas Asset` if you want the sliced Sprite asset in `Textures/` updated.
* **Particles:** `fx_ember_soft.png` (64², soft radial), `fx_dust_mote.png` (32²),
  `fx_arcane_spark.png` (64², 4-point star). White with alpha; tinting happens in code.
* **Noise:** `tex_eldritch_noise.png` must stay 256² seamless with R = warp field, G = ridged
  veins, B = slow breath mask. Any other channel meaning changes the slate look.
* Call `EldritchAssets.ClearCache()` (or `StreamingSprite.ClearCache()`) after swapping files
  while the Editor is open.

**(h) Zone Mode in the Editor.** Play the `Boot` scene → MainMenu → **WORLD MAP**. In the
Overworld the Editor has no GPS; walk with **WASD / arrows** (22 m/s in sim, status reads
"Editor · WASD walks map") until a Tear pin turns in-range (lab interact radius ≥ 70 m). Tap the
pin → `ZoneModePrompt` → **ZONE MODE**. The `ZoneModeCanvas` appears with the AR viewport, the
Spirit Dueler disk fades in folded on the simulated left forearm, and a violet ember ring marks
the hub. Click the hub in the viewport → SPIRIT DUELER panel with DUEL HERE … LEAVE ZONE; click
the hub again to fold it. Shortcut: MainMenu → DUEL → ZONE MODE uses `EnterFromHub`, which always
opens the Training "Spirit Dueler · Preview" zone (the hub has no GPS context; DUEL HERE then runs
`MapZoneService.MakeTrainingConfig`).

**(i) Troubleshooting.**

| Symptom | Cause | Fix |
|---|---|---|
| Pink / magenta slates or buttons | shader not compiled or `Shader.Find` miss (`[WRLDZ Eldritch] shader … not found` in Console) | reimport `Assets/Scripts/WRLDZ/UI/Eldritch/Shaders/`; check the shader name string matches `WRLDZ/UI/…`; components fall back to `UI/Default` (flat) once the warning fires |
| Buttons render but no energy line / no scanlines | materials are `UI/Default` fallback | see above (an unsynced `_RectSize` no longer breaks the look — the shader falls back to the UV gradient) |
| Edge glow / comet look uniform or smeared in a still | preview taken before layout | `EldritchMaterials.SyncRects()` after `Canvas.ForceUpdateCanvases()` (the preview tool does this) |
| No hieroglyphs (empty strip, or white squares) | StreamingAssets path — `Assets/StreamingAssets/WRLDZ/Eldritch/hieroglyph_atlas.png` missing or `.json` malformed | check `EldritchAssets.Present()`; sprites fall back to `UiFoundation.WhiteSprite()` cells |
| Glyph border fades wrong / cells snap | you set `Image.color.a` or a `CanvasGroup` on the border | alpha is *phase* in `HieroglyphGlow`; use `SetIntensity` |
| Particles not visible on a menu | `ParticleSystem` inside an overlay canvas | use `UiEmberField` (Images); `ArcaneEmberParticles` is world-space only |
| No ambient dust | `MenuShell.BatterySaver` is on | expected; bursts still fire |
| Slate looks flat (no veins, no warp) | `_NoiseTex` unassigned — `tex_eldritch_noise.png` missing | `EldritchMaterials.AssignNoise(m)`; restore the file |
| Text warps / misregisters against glass | `_Warp` too high | keep ≤ 0.03 on text-bearing slates |
| Disk tap does nothing in Zone Mode | collider layer ≠ disk layer, `Space.Ready` false, or drag system owns the pointer | wait for Ready; check `ArDiskHubHit` ("HubHit") sits under `PlayerDisk.transform` at `(0, WristHoverY, 0)`, layer 28, `isTrigger` |
| Hub menu floats off the disk | `ArDiskHoloAnchor` has no `StageCamera` (preview mode) | expected in `Preview Zone Mode Hub Menu` — anchor falls back to centre |
| Smoke test says fallback shader | Editor still compiling shaders during a fresh import | re-run after the compile finishes |

---

## 10. Acceptance checklist

- [ ] Editor imports with zero compile errors; `WRLDZ/UI/SolidVisionSlate`, `ArcaneEnergyFlow`, `HieroglyphGlow` compile (no pink).
- [ ] **WRLDZ ▸ Eldritch ▸ Bake All** produces 11 materials (+ `M_ArcaneEmberParticles`), 5 prefabs and the sliced atlas (or `Textures/Glyphs/`) under `Assets/WRLDZ/Eldritch/`.
- [ ] **WRLDZ ▸ Lab ▸ Run Menu Smoke Test** passes all Eldritch checks; `EldritchMenuPreview.RunBatch()` returns `"PASS"`.
- [ ] `Assets/Screenshots/eldritch_main_menu.png` shows the §6 hierarchy: identity slate, cartouche cap with live glyph strip, DUEL hero with gold line + disk showcase, COMMAND 2×2 in lapis, VIEW · SET, WORLD MAP | QUIT, status line.
- [ ] Only one gold-lined element above the fold (DUEL); WORLD MAP gold sits at the bottom.
- [ ] All labels are TextCream / TextMuted (or Gold / Cyan / Sandstone caps ≥ 16 design px) with outlines; nothing reads below ~8:1 on its fill.
- [ ] Hover: energy line widens, 10–14 embers burst; click: 6 sparks, `FreeUiKit.PlayClick()`; panels materialise top→bottom with fringe and stagger their children.
- [ ] Boot lands on MainMenu ("Opening the Duel Disk…"); WORLD MAP reaches the Overworld; Overworld HUB returns to MainMenu; QUIT needs two taps and returns to Boot.
- [ ] DUEL popover: VS AI → `MenuId.ArDuelCreate`, VS PLAYER → `MenuId.ArDuelPvpCreate`, ZONE MODE → `ZoneModeSession.EnterFromHub` (Training preview zone).
- [ ] `ZoneModePrompt` shows ZONE MODE between ENTER AR / DIGITAL and the Tear row; entering shows the folded disk on the forearm, violet ember ring, "ZONE MODE · {title}" chip, hint, LEAVE.
- [ ] Disk hub tap toggles SPIRIT DUELER (DUEL HERE · DECK · BAG · ARTIFACTS · TOME · SETTINGS · LEAVE ZONE); panel tracks the hub with a tether on phone; wearable path uses a world-space canvas.
- [ ] DECK / BAG / ARTIFACTS / TOME open their `ArDiskHolo` variants; LEAVE ZONE retracts the disk and restores `NonArPortrait`.
- [ ] `MenuChromePrefs.Opacity` Clear / Glass / Solid visibly changes slate density; battery saver stops ambient dust.
- [ ] `Time.timeScale = 0` does not freeze menu motion (unscaled) or shader animation (`_Time`).
- [ ] No per-frame allocations from `UiEmberField` after warm-up (Profiler, GC Alloc column).
- [ ] Destroying a menu leaves no leaked materials (Memory Profiler: `M_*` instance count returns to baseline).
- [ ] Edit-mode build (`Application.isPlaying == false`) snaps every component to its final state — no coroutine warnings in the smoke test log.

---

## Appendix — Palette + fonts quick card

```
ELDRITCH SOLID VISION · quick card
──────────────────────────────────────────────────────────────
BASE        Void #05070F · Abyss #0B0A1E · Obsidian #0E101C
LAPIS       LapisDeep #142A66 · Lapis #1E3A8A
SANDSTONE   SandstoneDark #7A5A34 · Sandstone #C9A36B
GOLD        MillenniumGold #FFD647 · GoldHot #FFE86B
CYAN        SolidVisionCyan #33EBFF · CyanDeep #0C6B86
ELDRITCH    EldritchViolet #7A2BFF · AbyssalMagenta #C44BFF
STATUS      IchorGreen #3CFF8C (ok) · BloodDanger #FF4455 (destructive)
TEXT        TextCream #FAF8F4 · TextMuted #B8CEE8

ROLES       gold = action · cyan = information / holo · violet/magenta = eldritch / AR / zone
            sandstone = frame / lore · text is Cream or Muted; color lives in edges + glows
TIERS       Primary gold/obsidian · Secondary cyan/lapis · Tertiary dim cyan/thin obsidian
            Eldritch violet+magenta/abyss · Danger blood/obsidian
OPACITY     Clear 0.58 · Glass 0.82 · Solid 0.94   (MenuChromePrefs.PanelAlpha)
CONTRAST    Cream/Obsidian 17.8:1 · Muted/Obsidian 11.7:1 · Gold/Obsidian 13.5:1
            Cyan/Obsidian 13.0:1 · Cream/LapisDeep 12.7:1 · Violet/Abyss 3.4:1 (glow only)

FONTS       Display  Bangers (Rowdies Bold fallback)     shout titles, panel titles
            Body     Exo 2 Bold                          blurbs, copy, status
            UI       Russo One                           button labels, caps
            Digits   Orbitron Bold                       LP, Lv, counters
            Glyphs   Noto Sans Egyptian Hieroglyphs (OFL) baked to sprite atlas, never live Text
            Recommended (not shipped): Cinzel Decorative for epigraphic caps
SIZES       design 14 → 36 px · 16 → 39 · 18/24 → 43 (display 44) · 28 → 50 (display 52)
            WrldzType.Scale 1.78 · MinBody 20 · MinDisplay 24 · canvas 1080×2340
──────────────────────────────────────────────────────────────
```
