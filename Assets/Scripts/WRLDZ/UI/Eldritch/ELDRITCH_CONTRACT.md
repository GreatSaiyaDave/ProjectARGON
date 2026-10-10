# Eldritch Solid Vision — implementation contract

Shared contract for the main-menu overhaul. Every file in this folder is built against it.
Theme: Ancient Egypt (gold · lapis · sandstone · hieroglyphs) × Lovecraft (cosmic dread, organic
veins, subtle non-Euclidean warp) × Sorcery (arcane energy, animated runes, embers) × Yu-Gi-Oh!
Solid Vision (translucent holographic slates, card-game readability).

Project facts that constrain the code:

- Unity 6000.5.10f1 · URP 17.5 · Linear color space · HDR on · UGUI (`com.unity.ugui 2.5.0`).
- All menus are **built at runtime from C#** (no prefabs in the repo). New components MUST offer a
  static `Create(...)` builder in the project style, AND an Editor baker turns them into prefabs.
- Canvases are `ScreenSpaceOverlay` with `UiTheme.ApplyPortraitPhone` (1080×2340 reference).
  `ParticleSystem` cannot render inside an overlay canvas → canvas-safe particles are pooled
  `Image` motes (`UiEmberField`). Real `ParticleSystem` is only for world-space / AR.
- Legacy `UnityEngine.UI.Text` cannot render supplementary-plane hieroglyphs (U+13000 block uses
  surrogate pairs). Hieroglyphs are therefore **baked sprites** (see atlas below).
- Sprites load from StreamingAssets via `StreamingSprite.Load(relativePath)` (sprite, corner
  auto-key) and `StreamingSprite.LoadTexture(relativePath, wrap)` (raw texture, no keying).
  Use `LoadTexture` for the noise tiles.
- Fonts: `FreeUiKit.DisplayFont()` = Bangers, `FreeUiKit.BodyFont()` = Exo 2 Bold,
  `FreeUiKit.UiFont()` = Russo One, `Resources/WRLDZ/Fonts/Orbitron-Bold` for holographic digits.
  `WrldzType.Style / StyleButtonLabel / StyleGoldTitle` handle sizes + outlines.
- SFX: `FreeUiKit.PlayClick() / PlaySelect() / PlayConfirm()`.
- Motion: `MenuMotion` (unscaled time easings, `PlayOn(go, coroutine)`), `MenuHoloPulse`.
- Existing chrome helpers we may reuse: `HubChrome.CornerTicks`, `HubChrome.FilamentRim`,
  `FloatingPanel.Place/Stretch`, `GoTheme.Place`, `WrldzTheme.Canvas`, `WrldzTheme.StretchFill`.
- Existing palette: `DuelystUi` / `WrldzTheme` (Cyan #33EBFF, Gold #FFD647, Magenta #FF4798,
  TextCream #FAF8F4, TextMuted #B8CEE8, Void #05070F). We extend, never replace.
- AR stack: `ArDuelSpace.CreateInUi(uiParent, 0,0,1,1, lifetimeParent)` builds the stage + camera
  + `ArDuelInteractionSystem` (PlayerDisk/OppDisk `ArDuelDiskRig`, `SimulatedArmTracker` in Editor,
  `ArViewportInput` on the RawImage). Disk taps go through `Physics.RaycastAll` on marker
  components (`ArDiskPhaseHit`, `ArGraveyardHit`) with `BoxCollider isTrigger` on layer 28.
  `ArDuelDiskRig.FadeInRetracted()` shows the folded cuff; `DeployForDuel()` swings the blade.
- Navigation: `ScreenRouter`, `MenuId`, `UiPresentation { NonArPortrait, ArDiskHolo, ArWorldPanel }`,
  `DualMenuPresenter.BuildFrame(host, title, sub, onClose, force)` (phone or AR frame),
  `MenuShell.CreateOverlay(sort)` + `ShowOverlay(MenuId)`. AR variants exist for
  `DeckCollectionScreen.Build(host, close, UiPresentation.ArDiskHolo)`, `InventoryScreen.Build`,
  `ArtifactBoxScreen.Build`, `SystemsSheets.BuildTome(host, close, presentation)`.
- Scene graph: Boot → (now) **MainMenu** → Overworld (WORLD MAP) … `AppSession.GoMainMenu()`.

## 1. Files (owner in brackets; do not create files outside your set)

```
Assets/Scripts/WRLDZ/UI/Eldritch/
  ELDRITCH_CONTRACT.md                 (this file)
  ELDRITCH_SOLID_VISION.md             [docs]  design system, palette, fonts, integration guide
  ELDRITCH_ASSET_RECS.md               [research] asset packs / fonts / particle textures
  EldritchPalette.cs                   [shaders]  color + font tokens (static)
  EldritchMaterials.cs                 [shaders]  shader lookup + per-instance material factory
  EldritchAssets.cs                    [shaders]  StreamingAssets loaders for the baked textures
  Shaders/SolidVisionSlate.shader      [shaders]  "WRLDZ/UI/SolidVisionSlate"
  Shaders/ArcaneEnergyFlow.shader      [shaders]  "WRLDZ/UI/ArcaneEnergyFlow"
  Shaders/HieroglyphGlow.shader        [shaders]  "WRLDZ/UI/HieroglyphGlow"
  Shaders/BarrierStoneSeal.shader      [shaders]  "WRLDZ/UI/BarrierStoneSeal"  (see §12)
  SolidVisionSlate.cs                  [components]
  BarrierStone.cs                      [components]  SealState + per-instance seal driver (§12)
  EldritchDressing.cs                  [components]  CarveFace / SealFace / SealPlate / tentacles
  ArcaneButton.cs                      [components]
  HieroglyphBorder.cs                  [components]
  HieroglyphPanel.cs                   [components]
  EldritchGlyphs.cs                    [components]  atlas slicing + glyph pool
  UiEmberField.cs                      [components]  canvas-safe particles
  ArcaneEmberParticles.cs              [components]  world-space ParticleSystem builder (AR)
  ZoneMode/ZoneModeSession.cs          [zone]
  ZoneMode/ArDiskHubHit.cs             [zone]   marker on the disk hub (namespace WRLDZ.Presentation.ArInteraction)
  ZoneMode/ArDiskHubTapRouter.cs       [zone]   IPointerClickHandler on the AR RawImage → raycast
  ZoneMode/ArDiskHoloAnchor.cs         [zone]   projects a world point to canvas anchors each frame
  ZoneMode/ArHoloHubMenu.cs            [zone]   AR variant of the main menu
Assets/Editor/WRLDZ/EldritchAssetBaker.cs      [editor]  bake materials / prefabs / atlas asset
Assets/Editor/WRLDZ/EldritchMenuPreview.cs     [editor]  edit-mode build + PNG capture of the menu
Assets/StreamingAssets/WRLDZ/Eldritch/*        (already baked, see §5)
Assets/Resources/WRLDZ/Fonts/NotoSansEgyptianHieroglyphs-Regular.ttf (OFL, already copied)
```

Existing files edited (owner): `DuelDiskMenuUI.cs` [menu], `BootFlowUI.cs` [menu] (WorldLoad →
`GoMainMenu`), `AppSession.cs` doc comment only [menu], `ZoneModePrompt.cs` [zone] (ZONE MODE
capsule + make `Launch` internal), `MenuSmokeTest.cs` [editor] (new checks appended),
`MENU_MAIN.md` [docs].

Namespaces: runtime `WRLDZ.UI.Eldritch`; zone-mode AR markers `WRLDZ.Presentation.ArInteraction`;
editor `WRLDZ.EditorTools`. No asmdefs exist — everything is Assembly-CSharp / -Editor.

## 2. Palette tokens (`EldritchPalette`, all `public static readonly Color`)

| Token | Hex | Role |
|---|---|---|
| `Void` | #05070F | full-screen base |
| `Abyss` | #0B0A1E | deep indigo-black behind slates |
| `Obsidian` | #0E101C | slate fill (alpha 0.82–0.94 by `MenuChromePrefs.PanelAlpha`) |
| `LapisDeep` | #142A66 | lapis inlay fill (secondary tiles) |
| `Lapis` | #1E3A8A | lapis highlight |
| `SandstoneDark` | #7A5A34 | cartouche / frame shadow |
| `Sandstone` | #C9A36B | cartouche / frame |
| `MillenniumGold` | #FFD647 | primary CTA, glyph glow, gold energy line |
| `GoldHot` | #FFE86B | hover peak, LP digits |
| `SolidVisionCyan` | #33EBFF | holographic slate edge, scanlines, info tiles |
| `CyanDeep` | #0C6B86 | cyan fill undertone |
| `EldritchViolet` | #7A2BFF | Lovecraft accent: veins, warp, Zone Mode |
| `AbyssalMagenta` | #C44BFF | tear glow, AR anchor tether |
| `IchorGreen` | #3CFF8C | confirm / bioluminescent ok |
| `BloodDanger` | #FF4455 | destructive, QUIT, concede |
| `TextCream` | #FAF8F4 | titles / values |
| `TextMuted` | #B8CEE8 | secondary labels |

Tiers (`EldritchTier` enum: `Primary, Secondary, Tertiary, Eldritch, Danger`) map to
(edge, line, fill): Primary=gold on obsidian; Secondary=cyan on lapis-deep; Tertiary=muted cyan on
obsidian at lower alpha; Eldritch=violet/magenta on abyss; Danger=blood on obsidian.

Fonts (`EldritchPalette.Display()/Body()/Ui()/Digits()`): Bangers → Rowdies fallback for shout
titles; Exo 2 Bold body; Russo One button labels; Orbitron Bold digits. Recommended additions are
documentation only (Cinzel Decorative for epigraphic titles) — do not download anything.

## 3. Shader contracts (UGUI-compatible, ShaderLab + CG, URP-safe)

All three shaders start from the `UI/Default` skeleton: `[PerRendererData] _MainTex`, `_Color`,
stencil props (`_StencilComp _Stencil _StencilOp _StencilWriteMask _StencilReadMask`),
`_ColorMask`, `[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip`, tags
`Queue=Transparent IgnoreProjector RenderType=Transparent PreviewType=Plane CanUseSpriteAtlas`,
`Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend One OneMinusSrcAlpha
ColorMask [_ColorMask]`, `#include "UnityCG.cginc"` + `"UnityUI.cginc"`,
`#pragma multi_compile_local _ UNITY_UI_CLIP_RECT` and `_ UNITY_UI_ALPHACLIP`, `_ClipRect`,
`_TextureSampleAdd`, `#pragma target 2.5`, premultiplied output (`col.rgb *= col.a`). `half`
precision where safe (mobile). Time = `_Time.y`.

Rect-space coordinates: the vertex shader passes `v.vertex.xy` (Image local units) as
`TEXCOORD1`; the fragment computes `float2 px = localPos + _RectSize.xy * _RectPivot.xy` so
`px ∈ [0, _RectSize]` and `float d = min(min(px.x, W - px.x), min(px.y, H - px.y))` (pixels to the
nearest edge). The C# component keeps `_RectSize` / `_RectPivot` in sync on `OnRectTransformDimensionsChange`.

### 3.1 `WRLDZ/UI/SolidVisionSlate`
Properties (exact names):
`_RectSize` (Vector, px) · `_RectPivot` (Vector) · `_EdgeColor` (Color) · `_EdgeWidth` (Range 0.5–24 px)
· `_EdgeGlow` (Range 0–4) · `_ScanColor` (Color) · `_ScanDensity` (Range 0–2, lines per px ×0.1)
· `_ScanSpeed` (Range -4–4) · `_ScanIntensity` (Range 0–1) · `_SweepIntensity` (Range 0–2)
· `_SweepSpeed` (Range 0–2) · `_SweepWidth` (Range 0.02–0.6) · `_Hover` (Range 0–1)
· `_HoverGlowMul` (Range 1–4) · `_Open` (Range 0–1, 1 = fully materialized)
· `_NoiseTex` (2D, "gray") · `_Warp` (Range 0–0.08 non-Euclidean UV wobble)
· `_VeinColor` (Color) · `_VeinStrength` (Range 0–1) · `_Flicker` (Range 0–0.3)
· `_FillColor` (Color; multiplied under the sprite for tint-from-code).
Look: sprite × `_Color` × `_FillColor`; horizontal scanlines (sin on px.y) tinted `_ScanColor`;
bright band sweeping top→bottom; edge glow `exp(-d/_EdgeWidth) * _EdgeGlow * lerp(1,_HoverGlowMul,_Hover)`;
veins = `_NoiseTex.g` ridges warped by `_NoiseTex.r` and time, tinted `_VeinColor`, strength
scaled by `_VeinStrength` and boosted by `_Hover`; `_Open` reveals top→bottom with a chromatic
cyan/magenta fringe at the reveal line and 0→1 alpha; `_Flicker` = tiny hash flicker.

### 3.2 `WRLDZ/UI/ArcaneEnergyFlow`
Base props as above (`_RectSize _RectPivot _NoiseTex _Hover _Open _Flicker`) plus:
`_FillColorA` `_FillColorB` (vertical gradient) · `_BorderInset` (Range 0–24 px, ring center)
· `_LineColor` `_LineColor2` (Colors) · `_LineWidth` (Range 0.5–16 px) · `_LineSpeed` (Range -2–2 laps/s)
· `_LineLength` (Range 0.05–1 fraction of perimeter) · `_LineCount` (Range 1–4) · `_Bloom` (Range 0–3)
· `_HazeStrength` (Range 0–1 inner mana haze near border) · `_Pressed` (Range 0–1).
Perimeter parameter `t ∈ [0,1)` clockwise from the top-left corner derived from `px` and the
nearest edge; energy head = `smoothstep` on `frac(t·_LineCount − _Time.y·_LineSpeed)` with a
trailing tail of length `_LineLength`, color lerp `_LineColor→_LineColor2` along the tail; ring
band = `exp(-abs(d-_BorderInset)/_LineWidth)`; result = gradient fill + band×head×`_Bloom` +
haze; `_Hover` widens/brightens the line, `_Pressed` darkens fill and snaps the head brighter.

### 3.3 `WRLDZ/UI/HieroglyphGlow`
Props: `_GlowColor` (Color) · `_Glow` (Range 0–3) · `_Pulse` (Range 0–1) · `_PulseSpeed` (Range 0–6)
· `_Spread` (Range 0–8 px halo radius via 4 extra taps). Uses `IN.color` (Image color) as tint and
`IN.color.a` as per-cell **phase** (0–1) for the pulse, so 40 cells share one material. Output =
glyph alpha × tint + dilated halo × `_GlowColor` × glow.

`EldritchMaterials`: `static Shader SlateShader()/FlowShader()/GlyphShader()` via `Shader.Find`
with fallback `UI/Default`; `static Material NewSlate(EldritchTier)`, `NewFlow(EldritchTier)`,
`NewGlyph(Color)` returning **new per-instance materials** (never shared — UGUI has no
MaterialPropertyBlock); `static void Bind(Image img, Material m)` sets `img.material` and
`img.color = Color.white`; helpers `SetRect(Material, RectTransform)`. Assign `_NoiseTex` from
`EldritchAssets.EldritchNoise()`.

## 4. Component contracts (`WRLDZ.UI.Eldritch`)

```csharp
public enum EldritchTier { Primary, Secondary, Tertiary, Eldritch, Danger }

public sealed class SolidVisionSlate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static SolidVisionSlate Create(Transform parent, string name, EldritchTier tier,
        bool hoverGlow = true, bool raycast = true);     // Image + material + this
    public static SolidVisionSlate Attach(Image image, EldritchTier tier); // dress an existing Image
    public Image Face { get; }  public Material Mat { get; }  public EldritchTier Tier { get; }
    public RectTransform Rect { get; }
    public void SetHover(float target01);   // eased over ~0.15 s (unscaled)
    public void PlayOpen(float duration = 0.42f);  // _Open 0→1 + embers burst
    public void SetTint(Color fill, Color edge);
}

public sealed class ArcaneButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public static ArcaneButton Create(Transform parent, string title, Action onClick,
        EldritchTier tier = EldritchTier.Primary, string blurb = null, Sprite icon = null,
        int titleSize = 22, bool centerTitle = false, bool hieroglyphBorder = false);
    public Button Button { get; }  public Image Face { get; }  public Material Mat { get; }
    public Text Title { get; }  public Text Blurb { get; }  public Image Icon { get; }
    public RectTransform Rect { get; }
    public void SetInteractable(bool on);   // dims line, keeps text readable
    public void SetTier(EldritchTier tier);
}
// Title/Blurb/Icon child names: "Title", "Blurb", "Ico" (matches HubChrome so existing
// GoTheme.Place(...Find("Title")) tweaks keep working). Icon anchors: right 30% when blurb present.

public sealed class HieroglyphBorder : MonoBehaviour
{
    public enum Edge { Top, Bottom, Left, Right, Ring /*all four*/ }
    public static HieroglyphBorder Create(Transform parent, Edge edge, float thicknessPx = 34f,
        Color? glow = null, float cellsPerSecond = 3f, Sprite[] glyphs = null /* null → atlas */);
    public float CellsPerSecond;  public Color Glow;  public bool Reverse;
    public void SetIntensity(float mul);  // hover boost
}
// Cycles a "read head" along the cells: the active cell + 3 trailing cells light up (gold →
// dim sandstone), every cell swaps to a new random glyph when the head passes. Cells are Image
// components using EldritchMaterials.NewGlyph (shared per border), color.a = phase.

public sealed class HieroglyphPanel : MonoBehaviour
{
    public static HieroglyphPanel Create(Transform parent, string name, EldritchTier tier,
        string title = null, bool ring = true, bool embersOnOpen = true);
    public RectTransform Root { get; }  public RectTransform Body { get; }  // Body = safe inset
    public SolidVisionSlate Slate { get; }  public HieroglyphBorder Border { get; }
    public Text TitleText { get; }
    public void Open();   // PlayOpen + StaggerChildren(Body) + ember burst
    public void Close(Action onDone = null);  // MenuMotion.SheetOut then destroy
}
// Corners carry tentacle decals (deco_tentacle_corner) tinted EldritchViolet at low alpha,
// rotated per corner; cartouche end caps frame the title.

public static class EldritchGlyphs
{
    public static Sprite[] Atlas();            // 64 sprites sliced from hieroglyph_atlas.png
    public static Sprite[] AtlasGlow();        // matching halo sprites
    public static Sprite Random(System.Random rng);
    public static Font DynamicFont();          // Resources NotoSansEgyptianHieroglyphs (may be null)
}

public sealed class UiEmberField : MonoBehaviour
{
    public enum Kind { Ember, Dust, Spark }
    public static UiEmberField Attach(RectTransform host, int poolSize = 48, bool ambient = true);
    public void Burst(Vector2 localPos, int count, Kind kind, Color tint);
    public void BurstFromRect(RectTransform rt, int count, Kind kind, Color tint);
    public bool Ambient;   // slow floating dust + rare embers across the host rect
    public float AmbientRate;
}
// Uses fx_ember_soft / fx_dust_mote / fx_arcane_spark sprites; pooled Images with
// raycastTarget=false; unscaled time; gravity slightly negative (embers rise). Never allocates
// per frame after warm-up. Respect MenuShell battery-saver: Ambient off when
// FindAnyObjectByType<MenuShell>()?.BatterySaver == true.

public static class ArcaneEmberParticles
{
    public static ParticleSystem Create(Transform parent, Color accent, float radius = 0.06f,
        int layer = 28, bool loop = true);   // world-space rune-ember ring for the AR disk hub
}
```

Hover/open particles: `ArcaneButton` bursts 10–14 embers from its rect on pointer-enter and
6 sparks on click; `HieroglyphPanel.Open` bursts 24 dust + 10 embers from the panel top edge.
Both find (or `Attach`) a `UiEmberField` on the nearest `Canvas` root (`EldritchMenuFx.Field(canvas)`
helper inside `UiEmberField`).

## 5. Baked StreamingAssets (`Assets/StreamingAssets/WRLDZ/Eldritch/`)

| File | Use |
|---|---|
| `hieroglyph_atlas.png` 1024² (8×8, 128 px cells, white ink, transparent) + `.json` (codepoints) | `EldritchGlyphs.Atlas()` |
| `hieroglyph_atlas_glow.png` | halo sprites |
| `fx_ember_soft.png` 64² · `fx_dust_mote.png` 32² · `fx_arcane_spark.png` 64² | particles |
| `fx_rune_ring.png` 256² | AR hub pulse ring, panel title ornament |
| `fx_tether_line.png` 8×64 | AR menu tether |
| `tex_sandstone_tile.png` 256² gray seamless | frame texture (tint Sandstone) |
| `tex_lapis_tile.png` 256² seamless lapis + gold flecks | secondary tile fill |
| `tex_eldritch_noise.png` 256² seamless (R warp · G ridged veins · B slow breath) | shader `_NoiseTex` |
| `deco_tentacle_corner.png` 256² white/alpha | HieroglyphPanel corners |
| `deco_cartouche_end.png` 64×192 | title cartouche caps |

`EldritchAssets` exposes one static loader per file (`Sprite` or `Texture2D`), all null-safe.

## 6. Main menu hierarchy (`DuelDiskMenuUI.BuildVisuals`, portrait anchors y0–y1)

```
0.905–0.985  Identity slate (SolidVisionSlate Tertiary): portrait · name · Lv/team · SoulBadgeView
0.862–0.900  Cartouche cap "DUEL DISK · SOLID VISION" with HieroglyphBorder.Top strip
0.575–0.855  HERO  ArcaneButton "DUEL" (Primary, gold energy line, hieroglyphBorder:true)
             blurb "Summon the Solid Vision" · SpiritDuelerDiskView.HubShowcase in right 38%
             onClick → DuelChoice HieroglyphPanel (popover 0.30–0.80): VS AI · VS PLAYER · ZONE MODE
0.530–0.568  Cap "COMMAND" (cyan)
0.345–0.525  2×2 ArcaneButton Secondary (lapis): DECK · BAG · STORY · BAZAAR  (icons from ImagineAssets)
0.250–0.335  Tertiary row: VIEW · SET (ArcaneButton Tertiary, compact)
0.135–0.235  WORLD MAP (ArcaneButton Primary-outline, compass icon)  |  QUIT (Danger, 28% width)
0.100–0.130  status line (existing _status)
Background: bg_hub sprite · LowerDusk · EgyptianAgesAtmosphere(NewKingdom) attached to canvas
            · UiEmberField ambient dust · HieroglyphBorder.Left + .Right rails (thin, dim).
```
Keep: `Build()`, `duelSceneName`, `GameTitle`, `ProjectCode`, `OpenHubOverlay(MenuId)`,
`EnsureHubShell()`, `OpenAvatarMenus()`, `ShowInventory()`, `Toast()`, `OnDestroy` disk cleanup.
Primary DUEL popover: VS AI → `OpenHubOverlay(MenuId.ArDuelCreate)`; VS PLAYER →
`OpenHubOverlay(MenuId.ArDuelPvpCreate)`; ZONE MODE → `ZoneModeSession.EnterFromHub(transform)`
(preview zone at the player's location; falls back to a toast when GPS/zone unavailable).
QUIT → `AppSession.Ensure().Logout(); GoBoot();` after a two-step confirm slate.
Edit-mode safety: everything must build with `Application.isPlaying == false` (MenuSmokeTest).

## 7. Zone Mode contract

```csharp
public sealed class ZoneModeSession : MonoBehaviour
{
    public static ZoneModeSession Active { get; }
    public static bool IsActive => Active != null;
    public static ZoneModeSession Enter(Transform lifetimeHost, string zoneId, string zoneTitle,
        MapZoneKind kind, Action onExit = null);   // from ZoneModePrompt "ZONE MODE"
    public static ZoneModeSession EnterFromHub(Transform lifetimeHost, Action onExit = null);
    public void Exit();                            // retract disk, destroy stage + canvas
    public ArDuelSpace Space { get; }  public ArDuelDiskRig PlayerDisk { get; }
    public ArHoloHubMenu HubMenu { get; }          // null until first disk tap
    public string ZoneId { get; } public string ZoneTitle { get; } public MapZoneKind Kind { get; }
}
```
Enter: `WrldzTheme.Canvas("ZoneModeCanvas", 88)`; `ArDuelSpace.CreateInUi(canvas, 0,0,1,1, host)`;
after `Space.Ready`: hide `Interaction.OppDisk`, both hand volumes, `Arena` root; `PlayerDisk.FadeInRetracted()`;
attach `ArDiskHubHit` (BoxCollider isTrigger ≈ 0.16×0.05×0.12 m mesh-local on `PlayerDisk.DiskRoot`, layer = disk layer)
and `ArcaneEmberParticles.Create(PlayerDisk.DiskRoot, EldritchViolet)`; `ArDiskHubTapRouter.Attach(rawImage, Space.StageCamera, onHubTapped)`;
`ScreenRouter.Ensure().Presentation = UiPresentation.ArDiskHolo`; top chip "ZONE MODE · {zoneTitle}"
(SolidVisionSlate Eldritch) + hint "Tap your Duel Disk" that fades after first tap; LEAVE chip top-right.
Exit: `PlayerDisk.RetractThenFadeOut()`, `Presentation = NonArPortrait`, destroy after 0.6 s, `onExit`.
`ArDiskHubTapRouter`: `IPointerClickHandler` on the AR `RawImage`; converts the click to RT pixel
coords via `RectTransformUtility.ScreenPointToLocalPointInRectangle` → `StageCamera.ViewportPointToRay`;
`Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide)`; first `ArDiskHubHit` in parents
fires. Ignore clicks the drag system owns (`Interaction.Drag.OwnsPointer`).
`ArDiskHoloAnchor`: every `LateUpdate`, project `Target.position` with `StageCamera` into the
RawImage rect and place the anchored panel so its bottom-center sits 40 px above the hub (clamped
inside the safe area); draws a `fx_tether_line` Image from hub to panel. Lenses/wearable
(`ArPresentationTarget.IsWearable`): instead parent a `RenderMode.WorldSpace` canvas (scale 0.0008)
to `DiskRoot`, facing the stage camera, and skip projection.
`ArHoloHubMenu.Open(ZoneModeSession s)`: `HieroglyphPanel` (Eldritch tier, AR anchors via
`MenuChromePrefs.GetWindowAnchors(ArDiskHolo, …)`) titled "SPIRIT DUELER"; rows (ArcaneButton
Secondary, compact): DUEL HERE · DECK · BAG · ARTIFACTS · TOME · SETTINGS · LEAVE ZONE. DUEL HERE
→ `ZoneModePrompt.Launch(zoneId, zoneTitle, digital:false, onClose, kind, 0, boss:false)` (make
`Launch` `internal static`); DECK → `DeckCollectionScreen.Build(ModalHost, close, ArDiskHolo)`;
BAG → `InventoryScreen.Build(…, ArDiskHolo)`; ARTIFACTS → `ArtifactBoxScreen.Build(…, ArDiskHolo)`;
TOME → `SystemsSheets.BuildTome(…, ArDiskHolo)`; SETTINGS → `SettingsScreen.Build(ModalHost, null, close)`;
LEAVE → `s.Exit()`. Toggle: a second disk tap closes the menu.
`ZoneModePrompt.Build`: add capsule "ZONE MODE" (Eldritch tier via `HubChrome.Capsule` Kind.Primary
recolored, or `ArcaneButton` Eldritch) between the ENTER AR/DIGITAL pair and the Tear row; on
click → `onClose()` then `ZoneModeSession.Enter(parent.root, zoneId, zoneTitle, kind)`.

## 8. Editor tooling contract

`EldritchAssetBaker` MenuItems: `WRLDZ/Eldritch/Bake Materials`, `…/Bake Prefabs`,
`…/Bake Hieroglyph Atlas Asset`, `…/Bake All`. Output under `Assets/WRLDZ/Eldritch/{Materials,Prefabs,Textures}`.
Materials: `M_SolidVisionSlate_{Tier}.mat`, `M_ArcaneEnergyFlow_{Tier}.mat`, `M_HieroglyphGlow.mat`.
Prefabs: `ArcaneButton.prefab`, `HieroglyphPanel.prefab`, `SolidVisionSlate.prefab`,
`HieroglyphBorder.prefab`, `ArcaneEmberParticles.prefab` — built by calling the runtime `Create`
builders inside a temporary canvas, saved with `PrefabUtility.SaveAsPrefabAsset`, temp destroyed.
Atlas asset: copy `hieroglyph_atlas.png` into `Textures/`, set `TextureImporter` to Sprite
(Multiple) with 64 rects named `glyph_00..63`, `alphaIsTransparency`, no compression, mipmaps off.
`EldritchMenuPreview`: `WRLDZ/Eldritch/Preview Main Menu (edit mode)` builds `DuelDiskMenuUI` in a
temporary `ScreenSpaceCamera` canvas rendered by a temp camera into a 1080×2340 RT and writes
`Assets/Screenshots/eldritch_main_menu.png` (static method `CapturePng(string path)` returns path);
also `…/Preview Zone Mode Hub Menu` → `eldritch_ar_hub_menu.png` (builds `ArHoloHubMenu` on a plain
canvas without the AR stage; the anchor falls back to center when no camera). Both callable from
`unity cmd eval`. Public static `RunBatch()` returns `"PASS"`/`"FAIL: …"` strings — never `EditorApplication.Exit`.

`MenuSmokeTest.Run()` gains checks (append near the existing hub check): `SolidVisionSlate.Create`,
`ArcaneButton.Create` (Title text present, material shader name contains "ArcaneEnergyFlow" or
fallback "UI/Default" logged), `HieroglyphBorder.Create` (≥ 8 cells), `HieroglyphPanel.Create` +
`Open()`, `UiEmberField.Attach` + `Burst`, `DuelDiskMenuUI.Build` finds labels "DUEL", "DECK",
"BAG", "STORY", "BAZAAR", "WORLD MAP", "QUIT", `ArHoloHubMenu` builds standalone with labels
"DUEL HERE" and "LEAVE ZONE".

## 9. Coding rules

- Null-safe against missing StreamingAssets (fall back to `UiFoundation.WhiteSprite()`, `UI/Default`).
- Edit mode safe: guard coroutines with `Application.isPlaying`; snap to final state otherwise.
- Unscaled time for all UI motion. No `FindObjectOfType` (use `FindAnyObjectByType` / `FindObjectsByType`).
- Per-instance materials are destroyed in `OnDestroy` (`ArObjectUtil.Destroy`).
- `raycastTarget=false` on decorative Images. Touch targets ≥ 56 px tall (design px).
- Log prefix `[WRLDZ Eldritch]`. Comments: terse, why-not-what, no marketing.
- Do not touch files owned by another agent; extend via new files if you need a helper.

## 10. Amendments (as built, 2026-09-21)

Deviations from the sections above that the implementation deliberately made; the code and
`ELDRITCH_SOLID_VISION.md` follow these, not the original wording.

1. **Rect space (§3).** UGUI batches vertices into canvas-root space, so `v.vertex.xy` is not
   Image-local. Both rect shaders compute `px = uv * _RectSize` (full-rect Simple sprite) and fall
   back to `1 / fwidth(uv)` when `_RectSize` is unsynced. `_RectPivot` is written but unused.
   `#pragma target 3.0`. Sliced / tiled sprites are unsupported on these two shaders.
2. **New shader props (§3.2).** `_HazeWidth` (px falloff for the inner haze; the old
   fraction-of-rect falloff made large plates read as a bevelled tunnel) and `_Phase` (comet
   offset, assigned per `ArcaneButton` instance).
3. **Edit mode (§4/§9).** `SolidVisionSlate`, `ArcaneButton`, `HieroglyphBorder` are
   `[ExecuteAlways]`; `SyncNow()` / `RelayoutNow()` and `EldritchMaterials.SyncRects(root)` exist
   so previews and bakers see laid-out rects.
4. **Hub hit (§7).** `ArDiskHubHit` is parented to `PlayerDisk.transform` (rig root / forearm),
   centre `(0, SpiritDuelerSkin.WristHoverY, 0)`, size `0.20×0.10×0.18 m`; the ember ring and the
   holo anchor target the same wrist point. `EnterFromHub` always opens the Training preview
   zone (no GPS lookup from the hub).
5. **ZoneModePrompt (§7).** The Tear ENTER AR / DIGITAL pair moves up to make room for ZONE
   MODE above HARVEST / BOSS; other kinds place ZONE MODE directly under the pair.
6. **Baker (§8).** Atlas slicing via `ISpriteEditorDataProvider` (types live in `UnityEditor`
   on 6000.x); fallback writes 64 single-sprite PNGs to `Textures/Glyphs/`. `Bake All` runs every
   step and fails only on materials / prefabs. The noise tile is copied to `Textures/` so baked
   materials keep `_NoiseTex`.
7. **Main menu (§6).** Atmosphere keeps only its star twinkles so `bg_hub` (pyramid + Tear)
   stays visible; the hero slate fill is thinned (0.56 top / 0.74 bottom) and carries a slow
   `fx_rune_ring` behind the disk. WORLD MAP uses the Primary tier (no outline tier exists).
8. **Preview (§8).** `EldritchMenuPreview` hides `GlowPlate` quads (URP-only shader drawn flat by a
   manual `Camera.Render`) and calls `SyncRects` before capture.

## 11. Review fixes (2026-09-21, post-review)

Applied after a three-lens adversarial review (shader / requirement-fidelity / integration).

1. **Shader stripping (critical).** The three shaders ship in no scene or Resources folder, so a
   player build would strip them and every surface would fall back to flat `UI/Default`. All three
   are now in `ProjectSettings/GraphicsSettings.asset` → `m_AlwaysIncludedShaders`.
2. **`_RectSize` default is `(0,0,0,0)`** in both rect shaders, so the `fwidth` fallback engages
   for materials no C# has synced (baked `.mat`, prefab thumbnails) instead of drawing an
   authored 420×140 rect.
3. **`_GlyphTexel`** replaces `_MainTex_TexelSize` in `HieroglyphGlow` (CanvasRenderer binds
   `_MainTex`, so the texel size uniform describes Unity's 4×4 "white", putting the halo taps
   ~0.75 UV off). `EldritchMaterials.NewGlyph` fills it from the atlas.
4. **`_Fade`** added to `HieroglyphGlow` and driven by `HieroglyphBorder` from the nearest
   `CanvasGroup`: vertex alpha is the pulse phase, so panel/canvas fades never reached the cells.
5. **`SetIntensity` drives `_Glow`**, not a >1 `Image.color` multiplier (UGUI clamps to `Color32`,
   so the hover boost was invisible on exactly the lit cells).
6. **Noise tile loads linear** (`EldritchAssets.LoadLinear`) to match the baker's `sRGBTexture=false`.
7. **Prefabs self-heal.** `SolidVisionSlate` / `ArcaneButton` serialize `_tier` and rebuild face +
   per-instance material in `Awake`, so an instantiated baked prefab is live, not inert.
8. **Zone Mode**: `StartingLp` carried through the session (8000 LP street NPCs no longer
   downgrade); the session parents at scene root, never under the prompt's soon-to-close host;
   `ArHoloHubMenu` hides the hub tether while a submenu covers the panel and destroys the fallback
   canvas it created.
9. **`LeaveDuel` routes by `DuelEntrySource`** — duels launched from the Duel Disk return to
   MainMenu; tear pins / street NPCs / tourney rooms still return to the Overworld.
10. **Touch**: the hover SFX is gated to mouse pointers (`pointerId < 0`); UGUI raises
    `pointerEnter` on touch-down, so every tap played a double blip.
11. **Preview** restores the `GlowPlate` renderers it hides for the capture; **baker** finds
    `SpriteRect` under `UnityEditor` (its 6000.x home) so the atlas slices through the modern
    provider, `Bake All` runs every step and fails only on materials/prefabs, and a 64-PNG
    fallback exists if multi-sprite slicing is unavailable.
12. Minor: `HieroglyphBorder.Rebuild` deactivates old cells before the deferred destroy (one-frame
    double draw); `EldritchGlyphs.DynamicFont` caches its miss; the boot skip button reads
    "SKIP TO DUEL DISK".

13. **Hub submenus hide the panel with its `CanvasGroup`, never `SetActive(false)`.** The slate's
    `_Open` reveal and `MenuMotion.StaggerChildren(Body)` are coroutines on the panel GameObject
    (0.42 s each); deactivating it stopped both with no restore path, so a row tapped inside that
    window brought the panel back cut in half with faded rows. `ArHoloHubMenu.SetPanelHidden`
    drives alpha / `blocksRaycasts` / `interactable` instead and the motion driver keeps ticking;
    the tether is still struck explicitly (it is a sibling the anchor keeps drawing), and a failed
    submenu restores it. Belt-and-braces: `SolidVisionSlate.OnEnable` snaps `_Open` to 1 when it
    finds a live open handle (the coroutine can only have been killed by a deactivate), and
    `StaggerChildren` clears `blocksRaycasts` until each child has finished fading in, so
    invisible rows are not tappable.
14. **`EntrySource` is stamped from the live scene, not hard-coded.** §11.9 routes `LeaveDuel` by
    `DuelEntrySource`, but all three Duel-Disk launches (`MakeTrainingConfig` for Zone Mode /
    DUEL HERE, `ArDuelCreateScreen`, `PlayerVsPlayerCreateScreen`) assigned
    `AppSession.SceneOverworld`, so the MainMenu branch never fired and hub duels dropped the
    player into Battle City. They now call `AppSession.LaunchEntrySource()` (MainMenu when the
    active scene is MainMenu, else Overworld) — tear pins, street NPCs and tourney rooms keep
    their Overworld routes because that is the scene they launch from.
15. **Zone chip materializes after it is placed.** `PlayOpen`'s ember burst samples the rect
    synchronously, and `ZoneModeSession.BuildChrome` fired it while the chip was still a fresh
    0×0 `RectTransform` at the canvas corner, so every Zone Mode entry showed a gold plume from
    the wrong corner. `PlayOpen` now runs after `FloatingPanel.Place`.

**Known, not fixed (pre-existing, outside this work):** `BootFlowUI.RevealChrome` starts one
untracked fade coroutine per panel child and never stops them, so tapping within ~0.2 s of a step
build can throw `MissingReferenceException` on the destroyed `CanvasGroup`. It predates this
overhaul and the fix belongs in shared `MenuMotion` code.

## 12. Barrier-stone seal (as built, 2026-09-22)

A fourth shader, a fifth enum, three StreamingAssets textures and the whole approved dialect
existed only in code and in one screen's usage — §1–§11 never mention `BarrierStone`, `SealState`,
Umbrax or the Shadow Realm. This section is the binding record.

### 12.1 Canon and material metaphor

Ptah and Anubis sealed Umbrax's soul and skull behind a contract-barrier; Akun carried a
barrier-stone into Egypt and that stone is the seed of the Millennium Items; Crawford Pretoria
made a battery of the skull and Tears opened across realities.

So **every panel is a fragment of that failing seal**. Intact reads as carved lapis with
Millennium-gold inlay held by Andrax's blue barrier light. A breach is a hole INTO the Shadow
Realm — void, drifting stars, violet pressure, seeping smoke — **never a glowing crack**.
Interaction strains the seal: hover widens the breach and brightens what is behind it.

### 12.2 `WRLDZ/UI/BarrierStoneSeal` (Shaders/BarrierStoneSeal.shader)

Rect space is §10.1's: `px = uv * _RectSize` with a `1/fwidth(uv)` fallback, full-rect **Simple**
sprite only. Sliced / tiled sprites are unsupported, and `_MainTex.a` is read as the plate's
SHAPE (so the face sprite still matters after the material swap). Vertex RGB is ignored —
only `IN.color.a` — which makes every `Selectable` `ColorTint` transition inert on a sealed face.

| group | properties |
| --- | --- |
| rect | `_RectSize` `_RectPivot` |
| stone | `_StoneTex` `_StoneTint` `_StoneScale` (px/tile) `_Brightness` |
| breach | `_CrackTex` `_CrackCell` `_CrackScale` `_CrackOffset` `_Breach` `_VoidColor` `_PressureColor` `_BleedColor` `_LipColor` |
| shadow realm | `_StarTex` `_StarDrift` `_SwirlTex` `_SwirlSpeed` `_SmokeSpeed` `_SmokeAmount` |
| barrier | `_BarrierColor` `_SealColor` `_BarrierWidth` `_SealWidth` `_BarrierGlow` |
| life | `_Breath` `_Strain` `_Spasm` `_Phase` `_Hover` `_Pressed` `_Open` |

`_Brightness` multiplies **only** the stone; void, lip, pressure, bleed, smoke, barrier and inlay
all add on top — one number buys darker stone and a relatively hotter hole.

Already listed in `ProjectSettings/GraphicsSettings.asset` → `m_AlwaysIncludedShaders`
(guid `89ea08c5c0373ec0096554086da6a04f`), per §11.1. **Do not author a fifth shader**; if anyone
ever does, it goes in that list or it is stripped and falls back to flat `UI/Default`.

### 12.3 `SealState` → role

| state | role |
| --- | --- |
| `Intact` | hairline, gold dominant — primary CTAs |
| `Fractured` | a real fracture — the workhorse for command tiles and sheets |
| `Sealed` | quiet chrome: title bars, footers, wells, form slots |
| `Breached` | shattered — Zone Mode, Tears, anything touching the Shadow Realm |
| `Condemned` | blood-lit — destructive actions, and failure feedback |

### 12.4 Entry points (never build a seal material by hand)

- `EldritchDressing.SealFace(ArcaneButton, SealState)` — buttons.
- `EldritchDressing.SealPlate(Image, SealState, bool reactive = false)` — everything else: panels,
  input slots, dropdown bars, decorative plinths. Strips any `SolidVisionSlate` first so two
  components never both claim `Image.material`, and forces full-rect / Simple / white.
- `BarrierStone.Attach` / `Create` underneath both.

`BarrierStone.OnDestroy` owns the material's lifetime (§9). Hand-rolling one anywhere leaks
through a per-step cascade. A sealed button legitimately holds two materials — `ArcaneButton`
still owns its now-unbound `ArcaneEnergyFlow` material — which is the approved menu's cost too.

**`CarveFace` must run BEFORE `SealFace`**: `CarveFace` swaps `Face.sprite` to the sandstone
grain and the seal reads `_MainTex.a` as the plate's shape.

### 12.5 Aspect ceiling — `FitCrackToAspect`

The shader squares its fracture tile off the LONG side (`unit = max(sz) * 0.55 * _CrackScale`), so
a 6:1 slab stretches one fracture over six times the plate's height and reads as a smear.
`BarrierStone.FitCrackToAspect` (default **false**, so every already-approved plate keeps the scale
its material was born with) makes `SyncRect` write
`_CrackScale = clamp(jitter * 2.9 / aspect, 0.4, 3)`. The constant reproduces the main menu's look
at its own ~3:1 tiles and gives wider rects several separate breaks instead of one smear.
Boot turns it on; the menu must not.

Below the ceiling there is also a floor: **do not seal thin rails or small pips.** The step
indicator's rail is ~42:1 and its pips 74×44 design px, while the stone tiles every 300 px and the
starfield divides pixels by 420 — they would sample one flat sliver of every channel. Those stay
flat Images driven by colour. Same verdict for the boot loading Track (~10:1).

### 12.6 Restate semantics

`SetState(SealState, bool keepFracture = true)` re-tunes the live material (state floats and
colours only) instead of rebuilding it, so an escalation is the **same crack widening** rather than
four unrelated breaks, and a running `PlayOpen` is not snapped shut. `keepFracture: false` rebuilds
and re-rolls. `Bind` is split into `Tune` (state) + `Vary` (per-instance fracture identity).
This changes the old `SetState` behaviour; it had zero callers project-wide when it changed.

Also added, all additive: `SetBreach` / `SetSmoke` (churn-free ramps), `SetBarrierColor` /
`SetSealColor` (remembered, so a later restate does not reset them), `SetStrain` (external strain,
held separately from hover so a touch-up's `pointerExit` cannot clear a focused field's glow),
`FocusStrain` (opt-in `ISelectHandler` strain, for `InputField`s sharing the GameObject — off for
buttons, which UGUI leaves selected after a tap), and `Dress` — an `Action<Material, SealState>`
re-invoked after every `Apply` / `SetState` so a caller's property profile survives a restate.

### 12.7 Boot cascade (`FlowChrome`, `BootFlowUI`)

`FlowChrome.BeginStep(SealState, context, quiet)` parks the step's state in the static factory, so
all 13 `PanelBox` and ~22 `Btn` call sites inherit the arc without naming a state. Safe only
because `FlowChrome` has exactly one consumer; a second consumer means threading a context object
instead. Per-step overrides are optional parameters (`PanelBox(..., SealState?)`,
`Btn(..., SealState?)`, `Field(..., SealState)`, `Dropdown(..., SealState)`); `BtnEx` returns the
`ArcaneButton` for steps that escalate later.

`FlowChrome.Dread(BarrierStone, SealState)` pushes boot past the menu through the eleven properties
the shared state table never writes (`_StarDrift` `_SwirlSpeed` `_SmokeSpeed` `_Strain` `_Spasm`
`_Breath` `_StoneScale` `_BarrierWidth` `_SealWidth` `_VoidColor` `_LipColor`) plus boot-local
`_Breach` / `_SmokeAmount` / `_Brightness` / `_StoneTint`. It registers itself as the stone's
`Dress` hook. **Do not re-tune `BarrierStone`'s own state table to make one screen louder** — that
silently restyles `DuelDiskMenuUI`, which is the approved reference.

Layering: boot's persistent dressing (`FlowChrome.BootDressing`) lives inside the panel host, above
the backdrop and below the panels, and re-seats itself after every backdrop swap. Nothing may hang
it on `Root`: the panel host is parented to `Root` and every step paints an opaque full-bleed
backdrop as that host's first child. The ambient ember field moved to the CANVAS for the same
reason — on `Root` it was buried, so boot had no ambient motion at all.

`EgyptianAgesAtmosphere` is gone from boot (`FlowChrome.RootAtmosphere`,
`BootFlowUI.ApplyBootAge`): its `Attach` forces `SetAsFirstSibling` so it could never be above the
art, every step's backdrop switched it off anyway, and its glyph stream is legacy `Text` over the
U+13000 block (tofu — the reason `KeepOnlyStars` exists). `MenuAge` / `EgyptianAgesAtmosphere`
themselves stay: `DuelDiskMenuUI` and `DesktopLabApp` still use them.

### 12.8 Known exceptions

1. **`FlowChrome.Dropdown`'s `Template` stays a flat Obsidian plate.** `Dropdown.Show()`
   Instantiates it, the Viewport's `Mask` drives UGUI's stencil path and caches `StencilMaterial`
   variants of the graphic's material, and a per-instance material rebuilt on every Show/Hide is
   churn we refuse. Its 44 px item rows are far under the 300 px stone tile anyway. The closed bar
   IS sealed.
2. **The boot backdrop itself can never be sealed** — it carries a real sprite with
   `preserveAspect` and an `AspectRatioFitter` in `EnvelopeParent` mode, while the seal needs a
   full-rect Simple quad and reads only `tex.a`.
3. **No gold hairline over a seal.** The shader lays its own Millennium inlay right around the rect
   (`exp(-d/_SealWidth)`) and correctly retreats it near a breach, so a painted rule double-draws
   it and paints gold straight across a hole into the Shadow Realm. `PanelBox`'s `GoldTop` and
   `Field`'s `Rule` are deleted; the header gets `CartoucheEnd` caps instead.
4. **Canvas-level ember motes draw over an open dropdown list.** Cosmetic only —
   `raycastTarget = false`, `maskable = false` — and it is the accepted look in the main menu.
