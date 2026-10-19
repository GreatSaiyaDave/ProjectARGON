# Avatar 3D — VRM on map and AR

**Data:** `AvatarAppearance` (`vrmId`, `use3dPreview`).  
**Folder:** `StreamingAssets/WRLDZ/Avatar3D/{vrmId}.vrm`  
**2D fallback:** `StreamingAssets/WRLDZ/Avatar/` (portraits + layered parts).  
**Legal:** no DMO assets. See `DMO_AND_AVATAR.md`.

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

---

## Implemented path (2026-09-22) — modular MakeHuman base, not VRM

Decision: **modular body + swappable parts, Skyrim-grade sliders, Takahashi style, free/modifiable
base.** VRM/VRoid stays optional; nothing free gave anime *and* sliders, so the engine is
MPFB2/MakeHuman (CC0 mesh, 1,258 CC0 targets) restyled toward Takahashi on top. Build pipeline and
licences: `~/argon-avatar/PLAN.md`, `~/argon-avatar/packs/LICENSES.md`.

| piece | where |
|---|---|
| Rigged bases, dressed | `Resources/WRLDZ/Avatar3D/argon_base_{male,female}.fbx` — mixamo skeleton (52 bones → Humanoid), **118 slider blendshapes**, parts `ArgonPart_{Shirt,Bottoms,Shoes,Hair,Eyes,Eyebrows,Eyelashes}` skinned to the same rig |
| Slider catalogue | `Resources/WRLDZ/Avatar3D/argon_base_manifest.json` (groups face / eyes / nose / mouth / ears / body; l-/r- pairs are one slider) |
| Clips | `Resources/WRLDZ/Avatar3D/ArgonClips_UAL.fbx` — Quaternius Universal Animation Library (CC0), Humanoid; `Idle_Loop`, `Walk_Loop` used |
| Import rule | `Assets/Editor/WRLDZ/AvatarImportPostprocessor.cs` — Humanoid, blendshapes kept, materials not imported |
| Data | `AvatarAppearance.morphs` (`List<MorphWeight>`; `GetMorph`/`SetMorph`; clamped in `ClampToCatalog`) |
| Token | `Presentation/Avatar/OverworldAvatar3d.cs` — `Attach(slot, look)` mirrors `OverworldAvatarToken.Attach`; blendshapes from `morphs`, skin/hair tints, Idle↔Walk Playable blend from `Speed` |
| Wiring | `OverworldUI` calls `OverworldAvatar3d.Attach` first and falls back to the OBJ token; `Speed` = map slide rate in m/s |

**Not yet:** cel shader + ink outline (parts currently take Unity's default material), Takahashi
spike hair (placeholder MakeHuman hair), the `ClothingCatalog` items in 3D (default outfit only),
customizer slider pages, and parts following the body sliders (garments do not carry shape keys —
extreme slider values can push skin through cloth; the garment delete-groups hide moderate ones).

**To verify in the Editor:** focus → both FBX import as Humanoid with 118 blendshapes → Overworld
scene: the token shows the dressed base, idles when still and walks when the map pans.

### Addendum, later 2026-09-22 — anime pass + presence rule
- Bases are now the **anime build** (`~/argon-avatar/tools/build_avatar.sh`): smoothed face on every slider,
  **drawn eyes** (CC0 VRoid eye cards: `ArgonPart_EyeL/R`, five alpha layers each), procedural Takahashi spike hair
  (`ArgonPart_Hair`, `crown` style default). Parts and their submesh textures are listed in
  `Resources/WRLDZ/Avatar3D/argon_base_<sex>.parts.json`; textures live in `Resources/WRLDZ/Avatar3D/Textures/`.
- `OverworldAvatar3d.BuildMaterials(sex)` rebuilds all submesh materials as **unlit** from that manifest (materials are
  never imported — the art guide forbids PBR). Eye/lash/brow layers are alpha-blended and drawn after the skin.
- **Presence rule (Dave):** the avatar is *translucent in Zone or Duel mode* — the player's physical form inhabits
  that space. `SetPresence(0.3f)` when `ZoneModePrompt` opens (both Tear and NPC sites in `OverworldUI`), `1f` on close.

### Addendum, night 2026-09-22 — v0.6 hair + face decals
- Bases rebuilt from the ROM-cel construction (`~/argon-avatar/PLAN.md`, status "night"): Yugi-type tri-colour
  star with fringe / lightning streaks / side-locks, Takahashi iris in the VRoid iris slot, and two new alpha
  decal parts **`ArgonPart_Mouth`** and **`ArgonPart_Nose`** (11 parts per base). `BuildMaterials` treats them like
  the eye layers (unlit, alpha-blended, queue 3005). `Textures/` gained `ArgonPart_Mouth_MAT.png`,
  `ArgonPart_Nose_MAT.png`; the iris/highlight PNGs were overwritten in place (same names).
- Limit: the decal cards are skinned to the head bone but carry no blendshapes — mouth/nose sliders move the
  (flattened) skin under them, not the drawn line. Next pass binds them to nearest-vertex shape keys.

### Addendum, 2026-09-23 — hidden during duels and Zone Mode
- Owner rule refined: *"the player's avatar should not be obstructive or really visible to the player during duels or
  zone mode"*. `SetPresence(0)` now **hides** the body (every `Renderer` disabled; GameObject, Playable graph and look
  stay alive); `SetPresence(>0)` restores. `0.3` remains only for the Zone Mode **prompt** sheet on the map.
- Global hide reasons stack via `OverworldAvatar3d.HideBody(reason, bool)` and override any requested presence:
  `ArDuelSpace` holds `duel:<id>` for its lifetime (AR duel, Zone Mode stage, free-view stage), `ZoneModeSession`
  holds `zone:<id>`, and `AppSession.StartArDuel` sets `duel-launch` for the scene hop (cleared on the next
  single-mode scene load, so a refused launch never leaves the map token invisible).

### Addendum, 2026-09-23 — AR floating right hand (`argon_hand_right.fbx`)
- **Asset:** `Resources/WRLDZ/Avatar3D/argon_hand_right.fbx` — the player's right hand + a ~7 cm forearm stub, cut from
  the CC0 rigged base `~/argon-avatar/build/argon_base_male.glb` by **`~/argon-avatar/tools/make_hand.py`** (Blender 5.2
  headless: `blender -b --python tools/make_hand.py [-- in.glb out.fbx render_dir]`). Same MakeHuman (CC0) body and
  mixamorig skeleton as the bases; no third-party mesh added.
- Contents: mesh `ArgonHand_R` (1,749 verts, skinned to the 17 bones from `mixamorig:RightForeArm` down), blend shapes
  **`Relaxed` / `FanHold` / `Pinch`** (finger poses baked with *Apply as Shape Key* from the armature, so Unity needs no
  retarget), and `Anchor_*` empties (Wrist, Palm, PalmNormal, MiddleTip, Grip, IndexTip, PinkyTip, ThumbTip, Pinch)
  that `ArFloatingHand` reads to frame itself. 1:1 metres (wrist → middle fingertip 0.184 m).
- Import: `AvatarImportPostprocessor` treats `argon_hand_*` as `animationType None` (a forearm-only skeleton cannot
  be Humanoid), blend shapes on, materials off (the hand is drawn with `WRLDZ/SpiritGhostUnlit`, skin-tinted, alpha 0.35).
- Runtime + behaviour: `Presentation/ArInteraction/FLOATING_HAND.md`.
- **Confirmed by owner (2026-09-23):** opening free view (`FreeViewScreen`, which mounts an `ArDuelSpace`) hides the map avatar
  body too — same rule as duels and Zone Mode.

### Addendum, 2026-09-23 (evening) — Meshy modular base v1 replaces the procedural v0.6 bases
- **Owner's call:** the procedural MakeHuman v0.6 look was rejected; the owner generated modular parts with Meshy AI.
  `argon_base_{male,female}.fbx` are now built by **`~/argon-avatar/tools/build_meshy_avatar.sh <sex> --install`**
  (details, metrics, defects: `~/argon-avatar/PLAN.md`, status "Meshy modular base v1"; comparison sheet
  `~/argon-avatar/build/render/FINAL_meshy_vs_refs.png`).
- **Same contract:** one armature `ArgonAvatarRig`, 52 `mixamorig:*` bones (the MPFB mixamo armature fitted to the Meshy
  body's landmarks), root `mixamorig:Hips`, **5 parts** `ArgonBody_<sex>`, `ArgonPart_Hair`, `ArgonPart_Shirt` (the coat),
  `ArgonPart_Bottoms`, `ArgonPart_Shoes`, all skinned to it (≤ 4 influences). Male 1.75 m body / 1.83 m with hair,
  female 1.65 / 1.77 m. ~70k triangles per avatar.
- **Gone:** eye cards (`ArgonPart_EyeL/R`), brows, lashes, `ArgonPart_Mouth` / `ArgonPart_Nose` — the Meshy face is
  painted into the body texture. Nothing in code requires them (they are only recognised by name when present).
- **No blendshapes.** `OverworldAvatar3d.Apply` skips the slider loop when the body has none; `AvatarAppearance.morphs`
  and `argon_base_manifest.json` stay for the planned bone-scale / procedural-morph sliders.
- **Textures:** every part is textured (`Textures/Meshy_<sex>_{Body,Hair,Shirt,Bottoms,Shoes}.png`, 2048², base colour
  only). `BuildMaterials` records which renderers got a texture; **skin and hair tints now apply only to untextured
  parts**, so `skinHex` / `hairHex` no longer change the Meshy look (they would multiply into the painted face and hair).
  `TintGarments` (NPC palettes) still multiplies over the garment textures.
- **Humanoid:** the two FBX `.meta` files keep their GUIDs, but their stored `humanDescription.human` / `skeleton` lists
  (the old MPFB rest pose) were cleared so the importer re-generates the avatar from the new rig. The old v0.6 FBX, metas,
  parts.json and textures are backed up in `~/argon-avatar/build/meshy/unity_backup_v06/`. Old textures are left in `Textures/`.
- **To verify in the Editor:** both FBX re-import as Humanoid with a valid (green) auto-mapping; Idle_Loop / Walk_Loop
  retarget without twisted forearms/hands (the fitted rig has large roll changes there) and the feet stay planted
  (Meshy feet are plantar-flexed); textures appear on all 5 parts; the token size in the HUD slot (male body 1.75 m vs
  1.715 m in v0.6, and the hair adds 8 cm).
- **Licence:** Meshy output rights depend on the owner's Meshy plan — **not confirmed**. Everything under `Resources/`
  ships in a player build; do not cut a release build with these files until the owner confirms (see
  `~/argon-avatar/packs/meshy_avatar/PROVENANCE.md`).

### Addendum, 2026-09-29 — Customization v1 (height, weight, hairstyles, hair dye, clothes colours)
Built on the Meshy modular base v1 (the owner's Downloads bodies stay the male / female bases). Pipeline and tools:
`~/argon-avatar/tools/custom/` (status "Customization v1" in `~/argon-avatar/PLAN.md`).

| piece | where / how |
|---|---|
| Height | `AvatarAppearance.heightCm` (0 = base 175 / 165 cm; range 152–203 male, 145–190 female) → uniform scale of the body (`HeightScale`); skeleton, clothes and hair scale as one. |
| Weight | `AvatarAppearance.weight` 0..1 (0.5 = as built). Shape keys **`Weight_Thin` / `Weight_Heavy`** on the body AND every garment (`custom_body.py`: inflate-around-bone + belly band, clothes take the nearest body point's displacement, so nothing clips more than at rest). |
| Body type | `AvatarAppearance.bodyType` (0 male, 1 female, -1 legacy = old portrait/body parity rule). `IsFemale` replaces the parity checks in `OverworldAvatar3d`. |
| Hairstyles | `Resources/WRLDZ/Avatar3D/Hair/argon_hair_<id>_<male|female>.fbx` (ArgonAvatarRig + one skinned `ArgonHair`, Generic import) + `Hair_<id>.png` + `thumb_<id>.png`; list in `Hair/hair_catalog.json` (20 styles, 76 named palettes, wild swatches). `OverworldAvatar3d.GraftHair` re-points the hair's bones at the body's by name; the base's `ArgonPart_Hair` is only the fallback. `hairStyleId` "" = the body's own Meshy hair rebuilt as `storm_crown` / `crimson_flame`. |
| Hair dye | `WRLDZ/AvatarHair` shader. Zones baked per vertex by `hair_build.py`: colour R tip, G fringe/bangs, B streak, A side; UV1.x = the original colour's luminance (keeps painted lock shading). `hairPattern` Original / Solid / Ombré tips / Tri-color / Streaks / Split with `hairHex` (root) + `hairTipHex` / `hairFringeHex` / `hairStreakHex` / `hairSideHex`. |
| Clothes / skin colour | `WRLDZ/AvatarRecolor` shader on body + garments with `Textures/Meshy_<sex>_<Slot>_mask.png` (linear; garments R main / G accent / B trim, body R skin / G undersuit) and `argon_base_<sex>.paint.json` (region mean luminance). `AvatarAppearance.paints` "Shirt.0" … "Body.1"; empty = original. Full palette = `AvatarCatalog.ClothingSwatches` + HSV picker. |
| Materials | `Resources/WRLDZ/Avatar3D/Materials/AvatarRecolor.mat` / `AvatarHair.mat` reference the shaders so player builds keep them (no `Shader.Find` stripping). |
| Customizer | `AvatarCustomizerUI` (+ `.Body3d.cs`): live 3D preview (`AvatarPreview3d`, layer 27 stage + RenderTexture, drag to turn, FACE/BODY zoom), tabs BODY / HAIR / HAIR DYE / SKIN / CLOTHES + the old wardrobe tabs; landscape = preview left, editors right; portrait = stacked. |
| Import | `AvatarImportPostprocessor`: `Hair/` FBX → Generic, no materials, no mesh compression; `*_mask.png` → linear, uncompressed. |

Also fixed: the idle clip pick matched any `*Idle_Loop` (Swim/Pistol/Spell…), so the map token idled with raised arms; it now takes exactly `Idle_Loop` / `Walk_Loop`.

Hair generation (Meshy, 648 credits, log `~/argon-avatar/packs/meshy_hair/spend.log`): our bald body rendered in key green →
image-to-image `nano-banana` paints the style on it (3) → multi-view (3) → `multi-image-to-3d` meshy-7.1 textured (30) →
`hair_fit.py` (green = mannequin; top-slice + head ICP onto our body, ~3 mm median) → `hair_build.py` (face-hug filter,
scalp cap from the body's painted skull, push-out, zones, skin) for BOTH bodies → `build_hair_set.py --install`.
Adding a style: add it to `research/customization/hair_catalog.json`, `gen_batch.py images <id>` → review the sheet →
`gen_batch.py meshes <id>` → `build_hair_set.py --install`.

**Not yet:** hair has no weight morph (long hair on a Heavy body can sit inside the coat's back), no hair physics,
garment shapes are still one outfit per body (colours only), 20 of the 58 researched styles are built.

### Addendum, 2026-09-29 — Spirit companions (3D Kuribohs) + admin chooser
- Six Meshy companions (`~/argon-avatar/tools/custom/kuri_gen.py` → `kuri_build.py`): Kuriboh, Winged Kuriboh, Junkuriboh,
  Galactikuriboh, Kuribandit, Darkuriboh — `Resources/WRLDZ/Companions/<id>.fbx` + `<id>.png` + `thumb_<id>.png`. Winged
  ones carry a Root / Wing_L / Wing_R armature (Generic import) that `SpiritCompanionModel` flaps; size and centre come
  from the rendered bounds (static and rigged FBX import at different unit scales).
- `Data/SpiritCompanions` + `PlayerProgress.companionId` ("" = the Kuriboh of the player's team). `KuribohCompanion.CurrentId`
  resolves it; `LabAdminService.IsAdmin` accounts may pick any of the six (profile sheet, SPIRIT column); others see theirs.
- Map token: a 3D companion beside the avatar (falls back to the navi sprite). AR duel: `KuribohArBehaviour` swaps its
  art quads for the model (same hover / orbit / dart behaviour, bounce on draws, fade via `SetAlpha`).

### Addendum, 2026-09-29 — Canon duelist outfits (full outfits, not just colours)
- One Meshy generation per character paints the whole canon outfit onto OUR green body (`tools/custom/render_body.py`
  → `outfit_gen.py images|meshes <id>`, ~36 credits) and `outfit_build.py <id> <sex>` turns it into one garment:
  box + ICP fit on the bare skin the outfit leaves (head, neck, hands; ~5–7 mm), mannequin cut, 4 mm clearance,
  nearest-body skin weights (coat tails blend to the hips), the body's Weight_Thin / Heavy morphs, a main / accent /
  trim recolour mask, and a **body hide map** (`Outfit_<id>_<sex>_hide.png`, body UV space): body faces the outfit
  covers (ray tests along each face normal; feet under shoes; never head / neck / hands) are discarded by
  `WRLDZ/AvatarRecolor` (`_HideMap` / `_HideOn`) while the outfit is worn. `install_outfits.py` copies everything to
  `Resources/WRLDZ/Avatar3D/Outfits/` and writes `outfit_catalog.json` (id, name, character, suit, refLum, sexes).
- `AvatarAppearance.outfitId` ("" = base clothes). `OverworldAvatar3d.ApplyOutfit` grafts `argon_outfit_<id>_<sex>` by bone
  name (shared `GraftSkinned` with the hair), hides ArgonPart_Shirt / Bottoms / Shoes, and paints the body's undersuit
  with the outfit's `suit` colour ("skin" for bare arms; e.g. black under Kaiba's coat = his turtleneck and pants) unless
  the player painted Body.1. Paint keys `Outfit.0/1/2`.
- Customizer: **OUTFIT** tab (base clothes + every outfit built for the current body); CLOTHES recolours the outfit.
  AI duelists: `AiDuelistLooks` puts a character's canon outfit on when one is built (`character` match).
- Built: Battle City Yugi, Battle City Kaiba (male). Specs for the rest of the cast: `research/customization/outfit_catalog.json`.

## Addendum — canon faces as decals (2026-09-29, night)
The Meshy base body's face texture is a scattered atlas (the face is split across many islands), so a character's face
cannot be swapped by editing a texture region. Instead `AvatarAppearance.faceId` names a decal set in
`Resources/WRLDZ/Avatar3D/Faces/`:
- `<id>.png` — eyes + brows in HD anime cel style on a skin-colour patch with a feathered alpha edge (1024×540). Drawn on a
  quad 13.2 × 7.0 cm in front of the base eyes, parented to the head bone: 3.6 cm above the head bone, 12.1 cm ahead
  (calibrated on the male base in Unity renders; `OverworldAvatar3d.ApplyFace`).
- `<id>_mouth.png` (optional) — a mouth on the same kind of patch, 8.4 × 4.2 cm, 1.4 cm below the head bone, 11.8 cm ahead.
- The quads use `WRLDZ/AvatarRecolor` with `_UseTexAlpha = 1` (alpha from the texture, no outline / shadow band), queue 2460.
- `seto_kaiba`: eyes redrawn by Meshy nano-banana (3 credits) from the Capsule Monster Coliseum dialogue bust
  `research/menu_overhaul/game_assets/characters/cmc/kaiba/cmc_dialogue_kaiba_80_464x480.png`; the stern mouth is drawn
  in PIL. `AiDuelistLooks` sets `faceId` for Kaiba (`WithFace`).
- References for the other canon duelists (Yugi, Joey, Mai, Marik, Bakura, Pegasus, Weevil, Rex, Téa …) are in
  `research/menu_overhaul/game_assets/characters/` (CMC / Falsebound Kingdom / DOTR art, `CHARACTERS.md`); no 3D
  character models exist in any game on this machine.

## Addendum, 2026-10-04 — asset audit, Darkuriboh rebuild, clothing hide fix, breathing + blinking
Audit result (sourced models): nothing on this machine is a life-size, rigged, HD-anime DM-era duelist. The Models Resource
rips (`~/yugi300274_work`, `~/kaiba300265_work`: 3DS *Saikyo Card Battle* Yugi / Yami / Kaiba, 256² atlases, chibi, Grok's
FBXs lie on their backs) and the owner's Meshy statues (`~/Downloads/Meshy_AI_Yami_Yugi_*.glb` etc., unrigged single shells)
stay **references**. Duel Links on Steam holds only 2D character art; Master Duel holds a rigged official Kuriboh *mate*
(`mate/m04064`, 4.3k tris, generic rig, Unity-only animation clips) — an upgrade path for the companions, not used yet. The
Meshy modular base therefore remains the player body; the work went into making it hold up.

- **Darkuriboh** (`Resources/WRLDZ/Companions/darkuriboh.*`) is rebuilt from the owner-approved Kuriboh by
  `~/argon-avatar/tools/custom/darkuriboh_build.py` to the card (passcode 1035143, `~/argon-avatar/refs/darkuriboh/`): the
  Kuriboh body (atlas untouched), two curved eye patches 1.2 % of the body radius proud of the fur (half-discs, flat lid
  tilted 30°, iris low, yellow-green ring, white highlights) that cover the painted round eyes, and procedural bat wings
  (humerus / forearm / four finger rays / scalloped double-skinned membrane, dark navy bones, red-violet membrane) on the
  same `Root / Wing_L / Wing_R` contract `SpiritCompanionModel` flaps. 12.1k tris, one 1024² texture (atlas in the top
  80 %, eye cell + wing swatches in the bottom strip). Wing tips reach ~0.5 R above the head (the card's go higher; the
  model is normalised to its bounds height, so the body would shrink further). The previous text-prompt Meshy generation
  (purple fur, slit eyes, fangs) is kept in `~/argon-avatar/build/custom/companions_v2/old_darkuriboh/`.
- **Clothing hide maps.** `AvatarImportPostprocessor` only matched `*_hide.png` / `*_mask.png`; the slot pieces write
  `hide_<id>_<sex>.png` / `mask_<id>.png`, so all 63 hide maps imported sRGB + DXT1 + unreadable and `CombineHides` could
  read none of them (the body showed through every signature set). Rule widened to both spellings and the 65 maps rebuilt
  at 1024² by `~/argon-avatar/tools/custom/rebuild_hide_maps.py` (the 256² originals missed most islands of the 2048²
  scatter atlas; the one-mesh outfit maps had been saved at 76/255 and never passed the shader threshold).
- **Signature looks** (`CharacterLook.ApplyTo`) now clear the garment paints `Shirt/Bottoms/Shoes/Outfit.0-2`: the set is
  its own painted albedo. `AiDuelistLooks` still sets the generic coat colours first; they no longer leak onto the set.
- **Procedural idle** (`OverworldAvatar3d.ProceduralTick`, every body): breathing (chest pitch ±1.4°, shoulder roll, head
  drift, a 3° lean into the walk; rotations only, so the Humanoid Animator's per-frame rewrite keeps them from piling up;
  the Animator is `AlwaysAnimate` for that reason) and blinking (an eyelid card in the look's skin tone over the eyes,
  ~0.1 s every 2.2–6.5 s with the occasional double blink; never on silhouettes or sourced characters). Per-instance seeds,
  so duelists never breathe or blink in step. `DisableLocomotion` turns breathing off (AI duelists pose themselves).
- **`AvatarAppearance.Clone()`** now copies `faceId`, `characterId`, `silhouette`, `silhouetteHex` (every account load /
  save went through it, so a saved face decal was lost on the next session).
- **Edit-mode preview**: `Assets/Editor/WRLDZ/AvatarAssetPreview.cs` — `Render("player"|"npc"|"companion", arg)` spawns
  through the runtime paths and writes front / side / 3-4 / back / face PNGs + a renderer log to `Temp/AvatarPreview/`;
  `seconds, fps` adds a face frame sequence (blink / breathing checks). `OverworldAvatar3d.EvaluatePreview(dt)` steps
  the body outside Play mode.
- Removed from `Textures/`: 17 orphaned v0.6 MakeHuman / VRoid textures (no references by GUID or name), and from
  `Clothes/` the two duplicate `Outfit_*_mask.png` copies — backups in `~/argon-avatar/build/custom/unity_removed_20261004/`.

Still open after this pass: the 16 shell signature looks are flat colour (no texture detail); hair `yugi_muto` shows a
floating scalp cap above the spikes; female base has three unweighted finger bones; feet are plantar-flexed; no LODs; the
Meshy CC BY credit line is not yet in the product UI.
- Open (found 2026-10-04, pre-existing): the **female base** shows a small skin-coloured disc with a dark line floating
  ~15 cm above the head in 3/4 and face views. It is not the hair and not the body (both switched off in a probe; it
  stays) and not the blink card (blink disabled; it stays): it is a stray island of one of the Meshy female garment
  meshes (`ArgonPart_Shoes` / `Shirt` / `Bottoms` — the boots carry a tan toe-cap texture and the Shoes renderer bounds are
  0.93 m tall) skinned to the Head bone by `meshy_parts.py`'s Data Transfer weights. Fix: find the garment island whose
  weights name `mixamorig:Head` and delete it or re-weight it, then rebuild `argon_base_female.fbx`. Present in the 09:31
  baseline renders, i.e. before this pass.

## Addendum, 2026-10-04 (later) — wardrobe pass: every clothing option as a real, named, pictured 3D piece
- **Base wardrobe items are 3D now.** The 13 catalogue items that only had paper-doll art (street cap, night beanie, duel
  goggles, holo visor, path hoodie, cargo jacket, duel coat, fingerless gloves, disk gauntlets, duel slacks, cargo pants,
  neon kicks, path boots) are constructed shells on BOTH bodies — `~/argon-avatar/tools/custom/base_items_build.py`,
  colours from the catalogue's Fallback colour — so picking them changes the 3D avatar. Files follow the slot-clothes
  contract: `Clothes/argon_<id>_<sex>.fbx`, `albedo_<id>.png`, `hide_<id>_<sex>.png`. The three Meshy defaults
  (`shirt_plain` / `bottoms_plain` / `shoes_plain`) stayed the base parts in this pass (superseded: see the night
  addendum, they are shells now).
- **Clean seams.** `slot_clothes.py` shells used to end on the bone-weight boundary (triangle-ragged hems). Every piece now
  reaches past its seam and is cut by a plane (`bisect_keep`): a high neckline (a crew neck for the plain tee), sleeve
  ends across the arm (wrist / past the elbow; sleeveless armholes are relaxed, not cut — see below), hem at the upper hip, waistband tucked 5 cm under the hem, trouser cuff over the shoe collar, shoe collar
  at the ankle, boot top below the knee, glove cuff above the wrist, hat band edges, hood / veil edge at the neck, and
  straight coat-tail hems and inner edges. `push_normals` now also writes the mesh coordinates (the FBX exporter ignores
  shape-key data), so shells really stand off the skin (pushed 9 mm shirts, 8 mm shoes, 6 mm trousers, 5 mm gloves; measured medians 4–8 mm) and crowned hats 2.2 cm; before this every shell exported
  skin-tight and the coat flares never reached the file.
- **Hats hide the hairstyle** (`ClothingCatalog.HatHidesHair`: cap, beanie, Rex's beanie, Bonz's and Odion's hoods, and —
  since the night addendum — Ishizu's veil); bands keep it. `OverworldAvatar3d.ApplyClothes` turns the grafted hair off while one is worn.
- **Thumbnails + names in the customizer.** `tools/custom/clothes_thumbs.py` renders every piece on a dim body ghost
  (`Clothes/thumb_<id>_<sex>.png`, 256², import rule: no mips, transparent); `AvatarCustomizerUI` wardrobe tiles use them
  with the item's catalogue name as the tile label (96 px tiles). Fallback order: thumb → paper doll → slot glyph.
- **Hairstyle `yugi_muto`** lost its floating tan ring: 248 mannequin-skull faces above the hairline removed in both sex
  variants by `tools/custom/hair_fix_ring.py` (Zones colours, RefLum UV, weight keys and groups verified after the round trip).
- The female "floating disc" of the earlier addendum did not reproduce after the Editor was relaunched: it was a stale
  import in the previous Editor session, not an asset defect. Verified on 13 base items per body + the 18 signature looks (15 male, 3 female).
- Tooling: `Packages/manifest.json` moved `com.unity.pipeline` 0.5.0-exp.1 → 0.8.0-exp.1 (`unity pipeline upgrade`); the
  CLI refused every `unity cmd eval` against the relaunched Editor until then. `recompile_status` now prints a bare word.

## Addendum, 2026-10-04 (evening) — wardrobe fit pass: no clipping, headwear on the hairline, distinct pieces
Review of the wardrobe pass found hats cut at the jaw, eyewear inside the face, a sleeveless cut through the ribs, hide
maps erasing 4 cm of skin past every hem, and two identical jackets. Every clothing piece on both bodies was rebuilt with
`~/argon-avatar/tools/custom/slot_clothes.py` v3 (+ `base_items_build.py`, `rebuild_hide_maps.py`, `clothes_thumbs.py`):
- **Head geometry, measured** (Blender, both bodies): `mixamorig:Head` sits at the skull base; eye line = Head + 3.1 cm,
  brows ≈ +5.5 cm, hairline ≈ +10 cm, crown = +16.8 cm (male) / +16.4 cm (female); face surface at the eye line is
  y = −0.076 (male) / −0.051 (female). Crowned hats (cap, beanie, Rex's beanie) now cover Head +2.5 cm up; the rim is two planes
  that meet behind the ear: 8.8 cm above the skull base at the forehead, 1.2 cm over the top of the ear (the ear is
  measured per body as the part of the head more than 8.5 cm from the midline), then down to 3.5 cm at the nape. A fixed
  ear notch used to reach the female temple and open a hole in the rim above the eye. The hem lip below the cut brings
  the installed edge about 1 cm lower; the cap's peak is a
  plate at the hairline. Bands are a lathe across the forehead (+8.5..+12.5 cm) with a hair allowance: 8 mm off the skin at the
  forehead, 3 cm at the sides and back, so a bandana stands over the hair instead of inside it. Hoods and veils are generated, not cut from the head: a
  lathe round the skull's axis whose radius per angle follows the head + neck silhouette (`make_dome`; the body-face
  extraction tore on the female head's ear and jaw geometry), opening at the ear line from the chin to the brows and
  leaning forward over the forehead above them; a hood keeps a cowl round the neck, a veil hangs only behind. The goggles are three
  lens blocks and the visor a ribbon that follows the face, both placed 1 cm off the skin surface under them at the eye
  line (the blocks' corners come nearer where the face curves away).
- **No clipping, by construction.** Shells are welded (the body's UV-seam splits used to open as skin-coloured cracks
  once a shell was smoothed), oriented outward per connected component, smoothed (4 relaxations, 2 for gloves, 3 for hats)
  so pecs / abs / kneecaps do not read through, pushed off the skin, cut by planes, then `ensure_clearance` holds every
  vertex at least 60 % of the push above the skin point it was cut from (a per-vertex `orig_co` / `orig_no` attribute;
  a global nearest-skin rule is applied only where the normals agree, so armpit / sternum / navel creases cannot flip
  it). Ray-parity "inside" tests were tried and rejected: the Meshy body is not a single closed shell, so they walked
  sleeves 3.5 cm inward. Measured on the installed files (`clearance_check.py`, signed distance of every vertex to the nearest skin point): the
  worst generated piece is −8.7 mm (a few side-seam vertices of the male sleeveless tops), −7.7 mm on the female ones,
  −5.5 mm at the female toes; the distance metric reads negative in creases (armpit, between the toes), so these are
  at the noise floor, and no skin shows through any generated piece in the Editor renders. The two authored Meshy sets
  (Yugi, Kaiba) are untouched and intersect the body by up to 2.6 cm (Kaiba's coat sleeves and thighs) — open work.
- **Cuts are region-limited.** A sleeveless top keeps no upper-arm faces at all (the clavicle / trapezius, which the
  weights count as 'arm', stays with the top); its armhole is the bone-weight boundary relaxed along the skin by
  `smooth_boundary` (12 passes within 18 cm of the shoulder joints, below the neckline, snapped back onto the skin) — every
  plane-cut variant either sliced the ribs or left teeth; the hem plane skips the sleeves (the forearms hang
  below the hip in the A-pose, so every shirt lost its cuffs); long / short sleeves and glove cuffs are limited to the
  arm + hand marker groups (`Mk_Sleeve` / `Mk_SleeveGlove`, stripped before export).
- **Skirts and coats are tubes, not leg pairs.** A skirt is one lathe from under the shirt hem to the knee whose radius
  per angle follows the body silhouette at that height (so the spread A-pose legs stay inside), skinned hips → thighs with
  the Weight keys copied from the nearest skin vertex. Knee / full coats (`outer` 2 / 3) hang the same tube from a waist
  seam 2 cm under the shirt hem, open at the front (straight 11 cm opening). Hip-length jackets (`outer` 1) are one closed
  shell to the hip. Flares follow the horizontal normal (the old copysign flare tore the side seams).
- **Distinct pieces.** Path hoodie = soft roll round the back of the neck (hood down) + kangaroo pouch; cargo jacket = two
  chest flaps + two hip pockets (surface patches pushed 2.1 cm with closed sides); disk gauntlets = the glove thickened by 7 mm over the
  palm, hand back and cuff (never between the fingers, where it would cross into the next finger). Duplicate catalogue names are qualified ("Kaiba's knee boots",
  "Joey's sneakers" …); Ishizu's veil now hides the hairstyle like the hoods.
- **Hide maps** (`rebuild_hide_maps.py`): a skin point is hidden only if the garment lies within 3 cm straight above it
  (the old 4 cm proximity test and 9 cm ray erased rings of flesh past every hem and, beside a sleeveless top, the whole
  inner arm, because the ray from the arm reached the shirt on the torso); faces a hem crosses are resolved per texel so
  the hidden region ends on the hem line instead of a face-wide notch. The maps are 2048² (the body atlas's own
  resolution, 1.5 mm at the thigh), not dilated, imported without mipmaps as single-channel R8 (`AvatarImportPostprocessor`):
  at 1024 with a 1-texel dilation and a mip chain the hidden band reached 5–8 mm below every hem and read as grey
  saw-teeth in the Editor. The two authored Meshy sets (Yugi, Kaiba) and the one-mesh outfits keep a generous rule
  (garment within 3 cm above or below, or 1.5 cm around) because they were never pushed off the skin and sit up to
  2.5 cm inside it in places. Hats, bands and eyewear hide nothing and ship no map (`GraftCloth` treats a missing map
  as "hide nothing").
- **Hem lips and two-sided cloth.** Every open edge of a shell that lies within 3.5 cm of the skin is extruded one row
  down to 1.5 mm above the skin (`hem_lip`), so no hem opens into the gap between cloth and body; coat and skirt hems in
  the air stay free. Cloth materials render both sides (`_Cull` property on `AvatarRecolor.shader`, set to Off by
  `GraftCloth`), so the inside of an open coat or skirt is cloth, not a hole.
- **Closed robes.** Ishizu's dress and Odion's robe are closed tubes (`closed=True` in the SHELLS spec); the other knee /
  full coats open at the front.
- **Thumbnails** frame the piece in the upper two thirds of the tile (the label band covers the lower 32 %), gloves on one
  hand. **Code:** `OverworldAvatar3d.SyncHairUnderHat` runs after every apply (the hat / hair toggle used to sit inside the
  keyed clothes block, so changing hairstyle under a cap re-showed the hair); `hair_fix_ring.py` imports and exports the
  Zones vertex colours as linear data (the first run had darkened `yugi_muto`'s zone masks by one sRGB decode; restored
  from git and redone).

### Addendum, 2026-10-04 (night) — review round: colours, defaults, tiles
- **Colours were wrong everywhere.** `slot_clothes.save_u8` wrote the catalogue's sRGB colours into a float-buffer
  Blender image (read as linear) and saved it back as sRGB, so every solid albedo was gamma-lifted (the navy duel coat
  rendered lilac-grey, black boots mid-grey). It now writes the PNG bytes exactly; every `albedo_*.png` regenerated.
- **The three defaults are real pieces.** `shirt_plain` / `bottoms_plain` / `shoes_plain` were the Meshy body's own
  jacket, trousers and boots, which stand 2–3 cm off the skin and pierced every shell they were mixed with, and did not
  match their names. They are now a tee, trousers and sneakers built like every other piece; the Meshy parts switch off
  whenever a graft succeeds (unchanged logic, now always true).
- **Wardrobe tiles** are 128 px with a 9–14 px name, a gold wash marks the selected tile (the Outline alone only echoed
  the sprite's silhouette), the strip is taller on wardrobe tabs in the wide layout, thumbnails ship a mip chain, the
  ghost body in the thumbnails is dark (Workbench took the material's viewport colour, not the node colour), pieces of
  one slot are framed at the same scale (waist to ankle for bottoms, knee to floor for shoes), gloves from outside the arm.
- **Runtime:** a hat hides the hair only when it was actually grafted (`_hatWorn`), not whenever its id is set — a male-only
  hat id on the female body used to leave the avatar bald; face decals and the eyelid card use a per-sex forward offset
  (female face 1.5 cm nearer the head bone); per-piece materials and the hide composite are destroyed with the grafts;
  `CombineHides` builds a one-channel composite and resamples a map of another size instead of dropping it.
- **Remaining one-word signature names** are qualified (Keith's bandana, Weevil's shorts, Mako's spats, …).
- **Neon kicks** are built in the catalogue's cyan; they were near-white and matched the white sneakers.

### Addendum, 2026-10-04 (late night) — re-check round
An independent re-check of the 64 review findings against the rebuilt files (seven agents) confirmed most fixes and
found these, now fixed:
- **NPC variety regression.** The new default shells had no recolour mask, so every street duelist wore the same
  white tee. `GraftCloth` now treats a base shirt / bottoms / shoes piece without an authored mask as one paint region
  (`Texture2D.redTexture` mask, `_RefLum` = the flat colour's own luminance), so `Shirt.0` / `Bottoms.0` / `Shoes.0`
  recolour it exactly. Signature pieces keep their colours.
- **Undersuit.** The base shirts carry their undersuit colour in the catalogue (`N(..., suit)`): plain tee and hoodie
  skin, cargo jacket `#3A3A40`, duel coat `#E8ECEF` (the male default showed grey undersuit forearms).
- **Fallbacks.** A piece that does not fit the body falls back to the slot's default shell, never to the Meshy body
  garments; a signature look's forced skin (Marik's) is cleared by the next look unless the player painted their own.
- **Eyewear** is real: two rims (tori) 1.2 cm off the face, a bridge that meets them, temple arms to the front of the
  ear; thin round rims for Weevil's glasses, thick rims and strap-width arms for the duel goggles.
- **Every generated piece follows the body build** (visor, glasses, bands, cap brim, hood roll): Weight_Thin / Heavy
  copied from the nearest skin (`keys_from_body`), so a heavy face no longer grows through the visor.
- **Hood roll** (hoodie, and now Marik's sleeveless hoodie) sits on the measured back of the neck; the cap and beanie
  lose the flakes the rim planes left behind the ears; pocket patches take torso skin only (no plates on the forearms).
- **Skirts and coat tails** blend left / right thigh weights by angle (a hard centre split tore the hem in motion).
- **Mai's cropped vest** ends 17 cm above the hips (it was the same mesh as Téa's blouse); the plain tee has a crew neck.
- **Hide maps:** the per-texel hem test samples the texel's true point on the triangle (clipped weights pushed the
  probe 5 cm off the body and punched holes); trousers, skirts and shoes never hide arm skin; Kaiba's and Yugi's
  sculpted trousers and boots hide by body region (their meshes sit inside the skin, which left the ankles bare). The
  builders no longer install their 256 px proximity maps over the 2048 ones.
- **Customizer:** wardrobe tabs use a two-row scrolling grid in the wide layout (the single strip showed 10 of 19
  shirts at 720p); the selected tile has a gold frame and backing with the picture drawn on top (the earlier wash
  covered the picture); empty slots show the bare head / face / hand instead of a glyph of the item they remove;
  thumbnails use one fixed frame per slot, eyewear is shot front-on, gloves with the depth clipped to the hand.
- **Lining and coat backs.** Cloth back faces are shaded with the flipped normal and 22 % darker (`AvatarRecolor.shader`,
  `SV_IsFrontFace`), so the inside of an open coat reads as lining, not shadow blotches; the back of a coat / skirt tube
  takes little thigh weight, so a stepping leg no longer swings the whole back panel out into a fin.
- **Sculpted Yugi / Kaiba trousers and shoes** lost their stray weights (`~/argon-avatar/tools/custom/fix_authored_weights.py`):
  shoes kept 190–235 units of mixamorig:Hips weight (they stretched into a spike when the hips moved) and the trousers
  carried forearm / hand weight; trousers now keep Hips / Spine / legs, shoes the lower leg and foot. Originals backed up
  in the session scratchpad; geometry, UVs, materials and shape keys verified unchanged on re-import.
- **Open:** shoes still follow the toes (a voxel toe box was tried and failed: lace from a thin wall, spikes from a
  thick one); several signature pieces share one mesh and differ only in colour (the white sneakers, the dark trousers);
  bands sit in the hair volume of big hairstyles; crowned hats hide the whole hairstyle including long hair below the
  rim, and the painted scalp band under a hat takes the undersuit colour; long hair tips rest inside coat shoulders;
  Kaiba's authored coat sleeves intersect the undershirt.

### Addendum, 2026-10-04 (continued) — hair under hats, two-tone pieces, hollow fill, sculpted sets
- **Hair under hats is clipped, not removed.** `AvatarHair.shader` takes a world-space plane (`_HatClip`,
  `_HatClipOn`) and discards hair fragments above it in both passes. `OverworldAvatar3d.SetupHatClip` builds the plane in
  head-bone space from the hat landmarks (caps / beanies: 7.8 cm above the skull base at the forehead to 2.5 cm at the
  nape, i.e. 1 cm under the cut rim; hoods / Ishizu's veil: level with the hood's lower edge at the neck) and
  `PushHatClip` moves it with the head every frame. Fringe, sideburns and long hair below the rim stay, so a cap no
  longer leaves a bald painted scalp; long hair falls out from under a hood or the veil. `ClothingCatalog.HatHairClip`
  says which hats do this; the built-in fallback hair is still hidden under a hat.
- **Two-tone pieces.** Every generated piece now samples one of two colours through its UVs (u 0.25 main, u 0.75 trim)
  on an 8x8 swatch (`two_tone_image`); the region borders are cut into the mesh along planes, so they are straight:
  every shoe has a sole band (derived from the upper's colour, or `sole_hex`: Neon kicks are a white upper on a cyan
  sole), the base shirts have collar, cuff and hem bands, the base trousers a waistband, the duel coat white edging, and
  Mokuba's puffer vest shows the striped shirt's sleeves (`sleeve_hex` = his undersuit colour). Recolourable base pieces
  use a 2x1 split mask, so paint key .0 recolours the main region and .1 the trim (`GraftCloth.SplitMask`). Swatch
  albedos import point-filtered, uncompressed, without mips and readable.
- **Catalogue pockets on trousers.** Plain pants: one back hip pocket; Duel slacks: one cargo pocket on the right thigh;
  Cargo pants: a deep pocket on each thigh (side-facing `make_patch`). The cargo jacket has the three pockets its blurb
  names.
- **Hollow fill** (`fill_concavities`): shirts relax over the sternum, under-bust creases and navel without sinking
  (trouser waistbands sit underneath); shoes may lose 3 mm on the toe tops so the gaps between toes partly close.
  Toes are still readable on low shoes.
- **Sculpted Yugi / Kaiba pieces** (`push_authored_out.py`): every vertex under or within 4 mm of the skin moved out to
  4 mm (Kaiba's coat and trousers were up to 2.6 cm inside); originals backed up in the session scratchpad.

### Addendum, 2026-10-05 — material lifetime, scalp colour, sculpted-set skin weights
- **Magenta hair fixed.** `DestroySlotGrafts` (runs on every clothes apply) also destroyed the hair, outfit and face-decal
  material instances while those pieces stayed, so a grafted hairstyle kept a null material and drew Unity's error
  magenta (seen on Yami Yugi in the opponent previews). Only the clothes' own materials go there now; every other
  instance the avatar made (`Own()`, kept in `_ownedMats`) is destroyed in `OnDestroy`. `KillMaterials` / `Kill` never
  touch a project template, and `Kill` uses `DestroyImmediate` outside Play mode.
- **Painted hairline under hats.** The body's head region (`Textures/scalp_<sex>_mask.png`, R = scalp in body UV space)
  takes the look's hair colour through `_ScalpMap` / `_ScalpColor` on `AvatarRecolor` (`ApplyScalpColour`), so the strip
  between a hat's rim and the clipped hair no longer shows the undersuit colour (Rex's beanie was a cream band).
- **Hat clip pushed at render time.** `SetupHatClip` hooks `RenderPipelineManager.beginContextRendering` and re-pushes the
  plane for every live avatar that wears a clipping hat, so AR opponents and edit-mode previews (which do not tick the
  avatar) cut the hair where the head is now, not where it was at the last tick.
- **Swatch import rule reads the PNG header.** `AvatarImportPostprocessor` decides the point-filter / no-mip rule for
  `albedo_*` swatches (16 px or less) from the file's IHDR, because `GetSourceTextureWidthAndHeight` throws on a first
  import.
- **Sculpted Yugi / Kaiba tops follow the arms** (`tools/custom/fix_sleeve_weights.py`, found with the opponent-animation
  session's `OpponentDeploySweep` renders). Yugi's sleeves hung on torso / shoulder weights, so a raised disk arm left the
  sleeve behind as bare skin. His two wristbands (separate islands) were 100 % torso, so one stayed at the hip while the
  disk hand rose. Two toe pieces were also on torso weight. Kaiba's raised arm pulled chest-weighted strands out of the
  armpit, and 146 cuff vertices were weighted to both the forearm and the thigh (the hands rest on the thighs in the bind
  pose) and stretched into black blades. The tool blends the base body's own skinning into vertices nearest the shoulder
  or upper arm (fade 6 to 10 cm, smoothed over the mesh), gives detached islands the skin's weights outright, moves loose
  cuff hand weight to the forearm, and gives every arm-plus-leg vertex to one limb (within 7 cm of the arm axis it is
  sleeve). The jacket hem beside the hanging hands keeps its torso weights, so it does not follow the hands. Geometry,
  UVs, materials and shape keys are unchanged (round trip checked); originals are in the session scratchpad
  (`authored_backup3/`). The disk crossing counts of the sweep did not change (Yugi 12, Kaiba 5).
- **Bottoms import rule.** The opponent-animation session added `OnPostprocessModel`, which strips Arm / Hand / Shoulder
  weights from `Clothes/argon_bottoms_*` at import. The source files are already clean (Yugi's trousers 0 such vertices
  since `fix_authored_weights.py`; generated male bottoms carry 10 waist vertices at 3 % or less from the body skin), so
  the rule is a safety net.

### Addendum, 2026-10-05 (later) — posed clipping probe, coat tails, cloth over cloth, hats, item shapes
- **Posed clipping probe** (session scratchpad `poke/poke_tail.cs`, run with `unity cmd eval`): for each look, rays from the
  hips' vertical axis to every trouser / boot / visible leg vertex over 12 idle and 16 walk samples; a hit on the outer piece
  means the vertex sits outside it. Bake with `BakeMesh(m, false)` and map with position + rotation only (the body renderer
  carries a ~100x FBX scale). Hem-lip rings and layered sculpted geometry give false positives; confirm by eye.
- **Coat tails and skirts follow the legs** (`slot_clothes.make_skirt`): the back sectors keep 70 % of the thigh share (they
  used to hang 75 % on the hips), the hem is fully on the thighs and the left / right blend is narrower. Idle probe hits on the
  duel coat (both bodies), Pegasus, Tristan and Odion went from 870-1,320 to 0-29; the trailing calf no longer shows through
  any back panel. The top two rings hug the waist (no +-22 deg window there). Kaiba's sculpted trench keeps its rigid tail:
  on the thighs its halves swung out like wings.
- **Cloth over cloth** (`tools/custom/layer_clearance.py`, run after the builders, then hides and thumbs): every top is pushed
  4 mm outside each bottoms piece it can be worn over (generated pants / shorts of that body, its own look's bottoms; never
  another look's skirt or the sculpted sets), visible outer surface only, dilate-smoothed. Ishizu's skirt is straight
  (`skirt_flare` 0) so it sits inside her robe (idle probe hits 1,587 -> 130).
- **Hair under crowned hats tucks in.** `AvatarHair.shader TuckUnderHat` draws hair within 4 cm under the rim plane in toward
  an ellipsoid round the cranium (fitted once per body mesh from head-skinned vertices above the eye line, 1.8 cm out), so the
  cut edge sits under the rim and long hair flows out from under it. The rim plane is derived from the bind pose
  (`RestBoneToRoot`), not the pose at the moment the hat goes on. Hair and recolour shaders have DepthOnly / DepthNormals
  passes applying the same cuts (SSAO no longer sees clipped hair or hidden skin). In edit mode avatars are not in `s_live`, so
  the render-time push does not run there; previews rely on `EvaluatePreview` -> `ProceduralTick` to push.
- **Two-tone seams.** Cuff and sleeve-colour regions need an arm-weighted vertex past the elbow and a surface facing away from
  the arm's axis: no more white patches at the female duel coat's hips or sleeve colour on Mokuba's torso side.
- **Sleeveless armholes** (`cut_armholes`): one plane cut per side through the armpit and the shoulder top, above armpit
  height only; the zig-zag weight boundary is gone (Tea, Mai, Duke, Ishizu, Marik, Mako).
- **Items match their names.** Sandals (Tea, Mako, Ishizu) are a sole band, a toe strap and an ankle strap with the foot
  visible (`rebuild_hide_maps.py` uses the geometric rule for sandals); Tea's sole band is 3.2 cm. Fingerless gloves stop past
  the knuckles and the thumb's middle joint; Mai's long gloves reach 12 cm further up the forearm; Keith's wristbands are bands.
  Rex's open jacket and Bakura's open overshirt are open at the front over their undersuit colours. Catalogue text that
  promised what nothing builds now describes the piece (Joey: "White short-sleeved tee.", Keith: "Wristbands").
- **Customizer grid:** `WholeColumnGrid` keeps whole tile columns at the right edge (spacing absorbs the remainder) and shows a
  chevron while more tiles lie past it.

## Addendum, 2026-10-18 — Meshy sculpt wardrobe (the `_3d` pieces), Neck slot, head presets

**What arrived.** The owner generated 64 Meshy image-to-3D pieces into `Assets/Models/Characters/<Name>/*.blend`
(plus four in `~/Downloads`): per-character tops, trousers / skirts, shoes, hair, headwear, Weevil's glasses, the Puzzle
cord, the Millennium Ring and Key, Para's and Dox's heads, and generic player pieces (crimson tech jacket, techwear cargo
pants and sneakers). Each is one unrigged 400k-triangle mesh with a 4K base colour, normalised to a ~1.9-unit box.

**Pipeline** (`Tools/avatar/meshy_wardrobe.py`, Blender 5.2 headless; `meshy_wardrobe_install.py` installs). A piece
becomes a skinned graft on the ArgonAvatarRig exactly like the shells: `Clothes/argon_<id>_<sex>.fbx` + `albedo_<id>.png`
(hair: `Hair/argon_hair_<id>_<sex>.fbx` + `Hair_<id>.png` + `thumb_<id>.png` + a `hair_catalog.json` style with pattern
`original`). Steps: decimate (tops 12k, hair 14k, shoes 5k, glasses 3k tris) → fit per kind → skin weights interpolated
from the nearest body surface of the matching region (sleeves from the arms, legs from the legs) → restrict to the slot's
bones → push every vertex ≥ 4-5 mm out of the skin (hair 2 mm) → review renders + JSON report.
- Tops: the shoulder line is the sleeves' outer top (a collar or hood fooled any "x cm under the top" rule); sleeves are
  the vertices near the garment's own sleeve axis; the body's arm is posed onto that axis, the weights bound there, and
  the garment un-posed with inverse linear-blend skinning, so A-pose sleeves at 10-35° land on the hanging arms.
- Bottoms: hip width + 1.2 cm ease, waistband at the waist, full-length trousers stretched to the ankle, legs posed the
  same way. Shoes: per foot, scaled to the foot length + 3 cm, yawed to the foot axis, sole on the floor.
- Hair / hats / heads: a joint ICP of OUR face (against every piece vertex) and skull cap (against the inward-facing
  vertices) finds the Meshy mannequin's head; the piece is mapped onto ours, lifted out of the skull, and the mannequin's
  face / neck / bust faces (within 4.5-7 mm of non-scalp skin) are deleted. Hats fit by cavity only (their faceless).
  Head presets keep everything; the install step writes a hide map that covers the whole head and the Hat entry hides
  the hair (`HatHidesHair`), the look sets no face decal.
- Inside/outside near creases uses the "nearer to the bone axis than the skin point" rule (the body is not one closed
  shell, so face normals and ray parity both lie there), tested against the WHOLE body (a hem beside the thigh measured
  against torso skin only read 13 cm "inside" and exploded). The push itself is radial from that bone axis (from the
  head centre on the head): pushing along the nearest face normal folds a garment through a limb instead of inflating
  it round it. Decimation leaves slivers as long as a trouser leg, so edges over 3 cm are halved until none is left
  before the push; faces with a corner deeper than 3.5 cm (collar linings, inner shirts inside the shoulder mass) are
  dropped as invisible inner structure rather than torn outward.
- Mannequin removal also uses colour: the neutral (white / grey / tan / black) faces in front of our face give the
  mannequin's colour, the faces far from the body give the hair's; when they differ, everything in the mannequin's
  colour within 8 cm of the body goes (Bonz's white head, Mako's white torso). A blond fringe is saturated, so it is
  never taken for the mannequin; a black bust under black hair (Kaiba, Yugi) is only caught geometrically.
- Not converted: the red duelist glove (Meshy sculpted a closed fist; it cannot wrap the animated open hand).

**Data.** `ClothingSlot.Neck` (`AvatarAppearance.neckId`, default `neck_none`, customizer tab NECK) for the cord, Ring
and Key; `CharacterLook.NeckId`. Catalogue rows `N("<slot>_<char>_3d", …)` — painted pieces, no mask, shown as
painted. The DK rows (`yugi_dk`, `kaiba_dk`, `pegasus_dk`, `mako_dk`, `keith_dk`, `bonz_dk`, `panik_dk`, `bakura_dk`,
`yami_bakura_dk`, `tristan_dk`, `tea_dk`, `para`, `dox`) wear the sculpts where one exists; new rows `joey_dk`, `mai_dk`,
`weevil_dk`, `rex_dk`, `mokuba_dk` (ahead of their BC rows, so a plain name resolves to them), `tea_school` (the Domino
uniform sculpts) and `shadi`. `*_3d` hair rows use `HairPattern.Original` and keep `hairHex` only for the scalp strip.
The 2D sprite (`characterId`) still wins while set; clear it on a row to see the 3D body in these pieces.
`AvatarImportPostprocessor` now also fixes texture import flags under Avatar3D (hide maps linear / readable / no mips /
uncompressed / point; `Clothes/albedo_*` sRGB + readable, ≤ 2048).

**Verify.** `python3 Tools/csc_check.py --editor` compiles both assemblies offline with the live Editor's own compiler
arguments. In the Editor: `WRLDZ.EditorTools.AvatarLookPreview.Render("look:kaiba_dk")` (also `npc:<name>`,
`player:male|female`, `json:<AvatarAppearance>`) writes `Temp/AvatarPreview/<label>_{front,q,side}.png` without Play mode.

## Addendum, 2026-10-18 (later) — body range: stature, builds, faces, tattoos, accessories

Owner brief: "models to cover characters like Bonz who is 3 ft tall and Yugi who is 5 ft tall all the way to 6 ft 5 and
body types ranging from thin and gaunt like Bonz to plump like Chumley and big like Mr Huffington. Faces. Noses. Mouth.
Tattoos. Accessories. The customization should fit without clipping or spacing issues on all model types."

- **Stature** (`OverworldAvatar3d.ApplyStature`): heights 91-203 cm on the same two bodies (`AvatarCatalog.HeightMinCm`
  is 91 now). Down to 85 % of the base height the body scales as one; below that the head keeps more of its size
  (world head scale = height-ratio^0.45, the body closes the rest), set on the Head bone so hair, hats, glasses and face
  decals follow. A 91-cm body has a 17-cm head on a 0.49-scale body: anime child proportions, not a shrunk adult.
- **Builds and faces** are blend shapes on the bases (`Tools/avatar/body_morphs.py`, re-exports
  `argon_base_<sex>.fbx` and writes `argon_avatar_<sex>_morphs.glb` for the bake): `Build_Gaunt`, `Build_Plump` (belly
  bulge, double chin), `Build_Big` (broad shoulders, heavy limbs), and `Face_NoseLong / NoseWide / MouthWide / LipsFull
  / JawWide / ChinLong / CheeksFull / BrowHeavy` — Gaussian bulges round landmarks found on the head. Radial inflates
  are proportional to the local radius, so a plump body stays body-shaped. `AvatarAppearance.morphs` holds the sliders
  (`AvatarCatalog.BodyMorphs` lists them for the customizer: BODY tab = builds, FACE SHAPE tab = faces); the weight
  slider's `Weight_Thin / Weight_Heavy` stay.
- **Nothing clips on any build**: every garment, hairstyle and outfit carries the same shape keys, transferred from the
  body's displacement at each vertex's nearest skin point (`Tools/avatar/bake_body_morphs.py`, run after any wardrobe
  change; a radial growth also pushes the garment's stand-off outward). `ApplyCustomization` sets every `Weight_*`,
  `Build_*` and `Face_*` key on every skinned renderer that has it, so a plump body wears its coat plump.
- **Tattoos** (`AdornmentCatalog.Tattoos`, `AvatarAppearance.tattooIds`, TATTOOS tab): a tattoo is an alpha PNG plus an
  anchor in a bone's space. `OverworldAvatar3d.Adornments` copies the body triangles round the anchor into a skinned
  patch 1.5 mm off the skin — same bones, same blend shapes — and projects the art onto it, so ink follows the limb
  through animation, every build / face slider and the stature scale. The body UV atlas is a scatter of islands, which
  is why ink is not painted into the albedo. Entries wait for sourced art (Odion's face marks, Marik's back carving).
- **Accessories** (`AdornmentCatalog.Accessories`, `AvatarAppearance.accessoryIds`, ACCESSORIES tab): a prefab / FBX
  under Resources parented to a bone with a local pose; flat colour for untextured print models. The Millennium Puzzle
  print under Assets/Models/MillenniumPuzzle is a 3.5-cm-thick plate set, not a hanging pyramid, so it is not wired.

### Addendum, 2026-10-18 (later) — the Meshy sculpts integrated as they are
The owner's verdict on the first wardrobe pass: "You changed the model appearances. Don't ever do that without my
permission. They were perfect. I wanted you to perfect them as they are and integrate them into the game." and "Do not
keep the mannequin parts on the assets provided." The first pass had decimated the sculpts (160k–1.9M tris → 5–14k),
shrunk their 4096² textures to 1024–2048, dropped the normal maps and pushed / subdivided / cut the geometry to fit.
`Tools/avatar/meshy_wardrobe.py` was rewritten (v2) and every `*_3d` piece regenerated:
- **What the tool may do to a sculpt:** place it (one uniform scale + translation, a yaw for shoes), articulate it
  (sleeves / legs: the body's arm or leg is posed along the sculpted limb, the piece bound there and un-posed through
  the rig), skin it (nearest body surface, the slot's bones), remove the Meshy mannequin, hide the body underneath. It
  exports the mesh at full density with its own smooth normals (the Meshy custom normals are those normals to 0.04°)
  and writes the packed JPEGs byte for byte: `albedo_<id>.jpg` + `normal_<id>.jpg` (hair: `Hair_<id>.jpg`), 4096².
- **Mannequin removal** is colour-cluster based: a sculpt has a mannequin only when its geometry covers ≥ 60 % of our
  face after the ICP fit (Keith's bandana, Weevil's bowl and Shadi's turban have none — nothing is removed). The
  mannequin clusters are the neutral / skin-toned ones that own the faces lying on our face and do not reach into the
  hat volume; a face is deleted when it is painted nearer the mannequin than any hair colour and lies on / in our head,
  neck or bust (a short style has nothing below the neck base), plus the face column and anything 1.5 cm inside our
  face; the mask is majority-smoothed and leftover islands dropped. Same-colour cases (white hair on a white bust) fall
  back to geometry inside those zones. `--clusters` renders a false-colour tile per piece for review.
- **Hiding instead of pushing:** a sculpt keeps its mannequin's proportions, so where it is slimmer than our body its
  surface sits inside the skin (Yugi's inner shirt 9 cm inside the chest, trouser legs inside the thighs). The hide
  map (now written by the tool, 2048², per-texel on hem faces) therefore hides the torso under a top between hem and
  collar, the legs and hips under trousers, the feet and the leg below a shoe's top, and the generous 3 cm rule
  (8 cm out for loose sleeves) everywhere else. Hair and hats hide the scalp they cover (`Hair/hide_<id>_<sex>.png`,
  inward reach 7 cm because a Meshy mannequin head is smaller than ours); `OverworldAvatar3d` adds that map to the
  clothes' composite (`_hairHide`, part of the clothes key).
- **Shading:** `ClothingItem.Sculpt` (every `*_3d` id, set in the catalogue's static constructor) shows the texture as
  painted — no flat paint regions — and `AvatarRecolor` / `AvatarHair` gained `_NormalMap` / `_NormalOn` (tangent-space,
  Mikk tangents from the importer) so Meshy's folds and strands shape the cel band and rim as in the owner's Blender
  view. `AvatarImportPostprocessor`: sculpt textures at 4096 (`normal_*` as NormalMap), sculpt FBX readable,
  uncompressed, own normals, calculated tangents, no imported materials.
- **Body range without baking:** a 900k-vertex sculpt cannot carry 13 blend-shape frames, so sculpts ship none.
  `OverworldAvatar3d.MorphFollow.cs` ties each sculpt vertex to its nearest body vertex (rest pose, same rig space;
  cached per piece and body) and, whenever the body wears a shape the piece lacks (Weight_*, Build_*, Face_*), applies
  the body's delta on a per-avatar mesh copy — the nearest-skin transfer the bake used, done at run time. Built shells
  keep their baked keys and are left alone.
- **Verified unchanged:** the morph bases match the build-tree bodies (`argon_avatar_<sex>.fbx`) to 0.0000 mm in
  every vertex, normal, UV and bone; the 161 shells the bake re-exported match their originals to 0.0003 mm.
- **Textures per body:** a sculpt that exists for both bodies shares `albedo_<id>` / `normal_<id>`; a body-specific
  sculpt (Yugi's buttoned jacket on the female body shares `shirt_yugi_3d` with the male jacket but is another mesh)
  gets `albedo_<id>_<sex>` / `normal_<id>_<sex>`, which `GraftCloth` prefers. One Meshy shoe sculpt is usually a single
  shoe: the tool mirrors it for the other foot; shoes are placed, bound and hide-mapped with the feet posed flat (the
  GLB rest pose stands on tiptoe) and un-posed through the rig.
- **Sizes:** full-density FBX 3–63 MB each (1.9 GB for the set), 716 MB of JPEG. Beyond what the GitHub workflow can
  carry in one push, so the binaries live in the Editor project's working tree uncommitted until the owner decides.

## 2026-10-18 addendum — PROFILE / CUSTOMIZE rebuilt ("This needs a AAA upgrade")

- **Screens:** `AvatarCustomizerUI` (+ `.Body3d.cs` editors) stands in the v6.3 sanctuary of the main menu (`room.png`,
  MenuV6 fonts and chrome9 plates, `WrldzAnimePalette`, the style guide's measured gold), opaque: the menu underneath no
  longer bleeds through. Landscape (1920x1080 plan): categories on the left pylon (BODY, FACE, HAIR, OUTFITS, WARDROBE,
  COLOURS, ADORNMENTS, DUELIST), the duelist on a gold-ringed plinth in the middle, tabs + card grid + detail strip on
  the right pylon, BACK / RESET / SAVE LOOK over the ceiling, the name on a cartouche under the plinth (tap to edit).
  Portrait phone plan: stage on top, a sideways category strip, 4-column grid. Leaving with unsaved changes asks
  KEEP EDITING / DISCARD / SAVE. Widgets: `UI/CustomizerKit.cs` (`CzKit`, `CzFlow`, `CzHover`, `CzReveal`, `CzStageFit`).
- **3D stage:** `AvatarPreview3d` is one body shared by both screens (perspective camera, transparent background). It
  frames from the live bones and renderer tops, so no height / build / hair can be cut off, and eases between FULL,
  BUST, FACE, LEGS and FEET as the tab changes (FACE SHAPE / hats / eyewear → face, shoes → feet ...). Drag turns with
  inertia, the arrows turn 45°, the wheel zooms, a double click resets. The player is always the 3D body: a look's
  `characterId` (2D sprite set) is cleared when it is worn.
- **Outfits:** tapping a signature look wears its seven slots and garment paints only; FULL COSPLAY also takes the
  character's hair, face decal, height and build (`CharacterLook.ApplyTo`).
- **Card pictures:** `Assets/Editor/WRLDZ/WardrobeThumbs.cs` films every piece, hairstyle, look and body with the game's
  own body and cel shaders (Play mode = idle pose), framed by bones per slot, into `Resources/WRLDZ/Avatar3D/Thumbs/`
  (`item_<id>_<sex>`, `hair_<id>_<sex>`, `look_<id>`, `body_<sex>`; ~290 PNGs, uncommitted like the sculpts):
  `WRLDZ.EditorTools.WardrobeThumbs.Start()` / `.Start("_3d")` / `.Status()`. The Blender thumbs stay as the fallback
  (the 13 sculpted hairs' Blender thumbs were empty). Once the renders exist, a piece without one (no model for that
  body: the seven DK star-chip gloves) is not offered.
- **Idle fix:** the committed `ArgonClips_UAL.fbx` (10-10 restore) has empty takes (`…|Idle_Loop|baselayer`, zero
  curves), so the idle mixer fed the humanoid default pose: hips at the root, every standing body ~1 m into the floor
  (the old customizer's tiny, cut-off figure). `OverworldAvatar3d.BuildGraph` now skips empty takes and falls back to
  `Resources/WRLDZ/Avatar3D/UAL_Standard.fbx` — the CC0 Universal Animation Library pack itself
  (`~/argon-avatar/packs/ual/.../AnimationLibrary_Unity_Standard.fbx`), imported Humanoid. Measured: hips 1.05 m,
  head 1.53 m on the 175 cm base.
- **Open:** in every humanoid clip the bases stand on tiptoe (their rest pose has the ankle ~15 cm up, so the avatar's
  neutral foot is pointed): shoes read like heels in close-ups. A flat-foot avatar definition or a foot settle is a
  rig change for the owner to approve.

## 2026-10-18 addendum — terrain-aware standing (`AvatarFootGrounding`)

Owner: "The models should be realistic. Create a standing animation that adjusts appropriately to the terrain it is on.
This will be important during duels. This is not a simple phone game. It is a phone and AR lens game."

- **What it does** (`Presentation/Avatar/AvatarFootGrounding.cs`, `[DefaultExecutionOrder(-60)]`, so it runs after the
  Animator and before the duelist's arm / disk IK at 650): once per outfit it measures each foot's SOLE from the meshes
  worn on it (body foot, shoe / boot graft, one-piece outfit; heel + inner / outer toe contacts and the sole plane in
  foot-bone space, refined to its own normal). Every frame each foot is cast onto what is under it; a planted foot turns
  its sole onto that surface (pitch + roll follow the slope, heading stays the clip's), a swinging foot keeps the clip's
  lift above it, the hips drop (≤ 45 cm) or rise (≤ 20 cm) so the lower foot can reach, the pelvis rolls a little toward
  the lower foot (≤ 6°), and each leg is solved with two-bone IK in the clip's knee plane. Nothing about a mesh changes.
- **Tiptoe fixed:** the bases' rest pose holds the ankle ~15 cm up with the foot pointed, so every humanoid clip stood
  them on their toes. Planted soles put the ankle at 7.5–9.5 cm (shoe dependent); the hips drop 6.7 cm.
  `OverworldAvatar3d.ApplyStature` scales the body up by that measured drop (`StandingDrop`), so the height slider is the
  STANDING height (175 cm base → hips 1.01 m, head bone 1.52 m).
- **Measured (owner's Editor, Play):** level floor: sole 2 mm into the floor, both feet; 14° ramp: lower foot 7.7 cm
  down, upper 12.5 cm up, hips −4.4 cm, body upright; 12 cm step: the step foot +12.1 cm, the floor foot and hips
  unchanged; walking: no sinking; AR sim (Paradox Brothers at 0.447 scale): grounded, arm + disk IK intact.
- **Ground sources:** any collider under the foot except UI / Ignore Raycast, never the body's own. AR: every detected
  plane now carries an invisible `MeshCollider` (`ArFoundationSession.BuildPlanePrefab`: ARPlane + mesh visualizer +
  collider, no renderer; the template must stay ACTIVE under an inactive holder — the spawner copies its active flag).
  With nothing under a foot (EditorSim, Lenses, the plinth) the body's own floor plane is used. The overworld map token
  lives in a UI canvas: `Raycast = false` there (its floor is its plane).
- **Imports:** `AvatarImportPostprocessor` keeps `Clothes/argon_shoes_*` and `Outfits/*` readable (the sole measurement
  reads vertices; the bases and sculpts already were). The 10-18 reimport flipped 29 shoe + outfit metas.
- **Gotchas:** bind-space units on this rig are tiny (the rig root is scaled ×100: a foot is ~0.0025 units long), so
  any degenerate test must be relative, never an absolute epsilon (the first fit silently failed on `1e-10`). A standing
  clip's toe contact is 1–2 cm off, so the first 2 cm of "lift" are not a lift. The smoothing is on the ground height
  and normal only, never the horizontal (feet would drag behind a walking body).
- **Still 2D in duels:** the DK rows' `characterId` (the 10-15 DA sprite sets) makes `ArOpponentDuelist` seat Yugi /
  Kaiba / Joey … as flat drawings; Bonz, PaniK, Para / Dox, Shadi are 3D bodies. Flipping the cast to 3D is the owner's
  call (it reverts the 10-15 decision).

## 2026-10-18 addendum — the living face (eyes, brows, nose, mouth, head types, ears)

Owner: "Create a complete character customization system with several eyes, noses, mouths, head types, ears, skin
tone, and make all the eyes and mouths fully animated."

- **What the bases have:** no eye, mouth or nose geometry, no jaw / eye bones, and no albedo in the project — the
  skin is the region-mask paint (R skin, G undersuit, B other): the old "painted eyes" were the eye whites and lashes
  painted as undersuit / other regions. So the face is drawn, not modelled.
- **Shader `WRLDZ/AvatarAnimeFace`** (`Presentation/Avatar/AvatarAnimeFace.shader`): one quad per feature, signed-
  distance anime shapes (crisp at any zoom, no sprite frames): `_Part` 0 eye (lid curves, lashes thick to the outer
  corner, three-tone iris, pupil, two highlights, upper lid 0..1, happy ^ curve, squint), 1 mouth (corners, open 0..1,
  wide ↔ 'o' shape, lip line, teeth band, tongue, lower-lip shade), 2 nose (tick / line / point / button), 3 brow
  (stroke with arch, tilt, lift), 4 skin cover. `_Alpha` = presence.
- **`AvatarFaceRig`** (`Presentation/Avatar/AvatarFaceRig.cs`): six quads under the Head bone (eyes ±3.1 cm at +3.3 cm,
  yawed 11° to the cheek; brows +5.7 cm; nose +0.4 cm; mouth −1.6 cm; face depth 0.1235 m / 0.1085 f). Per frame:
  blink schedule (2.2–6.5 s, double blinks), gaze (saccades to a wandering glance or `LookTarget` / `LookAt`),
  expressions eased between `FaceCatalog.ExpressionFor` moods, `PlayBeat(beat)` from `ArOpponentDuelist.Play`
  (attack → angry, hit → pain, good/taunt → smug, victory → laugh, defeat → sad …), `Say(seconds)` syllable rhythm with
  vowel shapes or a live `SpeechLevel`. `Blink = false` for card renders. Edit-mode: `Tick(dt)` from `EvaluatePreview`.
- **`FaceCatalog`** (`Data/FaceCatalog.cs`): 7 eye shapes, 10 iris colours (+ fine tune), 5 mouths, 5 noses, 5 brows,
  the expression table and the beat → mood table. `AvatarAppearance`: `eyeId, irisHex, browId, browHex, noseId,
  mouthId`. Head types `Head_Round / Long / Square / Child` and ears `Ear_Large / Small / Pointed` are blend shapes
  (`Tools/avatar/body_morphs.py` HEADS / EARS, installed 10-18; `AvatarCatalog.BodyMorphs` groups `head`, `ears`;
  the runtime also pushes `Head_*` / `Ear_*` onto garments; hair / hats / eyewear import readable so MorphFollow carries
  the shapes onto them without a bake).
- **Customizer FACE:** EYES (live-drawn cards: a RawImage with the face shader), BROWS, NOSE, MOUTH, HEAD, EARS,
  FEATURES (jaw / chin / cheeks / brow), SKIN (moved here from BODY). BODY keeps base / stature / builds.
- **Flattening the base's painted features** (`AvatarRecolor` `_FaceFlatMap` + `_FaceFlatColor`): where the mask is
  set the texel becomes the mean skin at the skin region's reference luminance (paint lands flat) AND the region mask
  becomes pure skin (so the undersuit / other eye regions vanish). `Tools/avatar/face_flat_mask.py` builds
  `Textures/face_flat_<sex>_mask.png` from the MESH, not from islands: over the whole head band from the chin to the
  hairline (no depth box - brow tails and lash tips sit at the temples), every triangle holding a non-skin region-map
  texel strictly inside it is a feature whole; the short-hair cap counts as hair only as its solid blob (an opening of
  ~5 mm), thin painted strands on the face are features. The weight is 1 within 14 mm of a feature vertex along the
  surface, eased over 10 mm more, and rasterised through the UV triangles with a 1.5 px apron in true pixels - seam-free
  however the atlas is cut. The tool logs "head non-skin texels still showing" as its own leak check (hundreds = the
  painted strands at the back of the head, fine). `OverworldAvatar3d.ApplyFace(look)` turns it on for the rig and for
  canon decals alike; decals (`faceId`) still win over the rig while set.
- **Gotchas:** thin dark / white strokes on the flattened face were UV-seam slivers from the rasteriser (its apron was
  measured in edge-function units, so long edges got none) - not features, cap paint, outline, hide map, shadow, normal
  map, filtering, rig quads, skin weights or mesh slivers (each was filmed off before the apron was found); verify by
  filming (`AvatarCustomizerUI.Ensure().OpenCustomizer()` works in any scene, hide the hair renderers, camera at the
  Head bone), never by the mask image or the counters. A per-vertex sample misses strokes thinner than a triangle (test
  triangles, not vertices, and never their bounding boxes); the dilation must be in 3D (UV dilation stops at island
  borders); a `delayCall` does not fire in an idle Editor - use `EditorApplication.update` one-shots for imports;
  `Material.GetTexture("_BaseMap")` is null on the bases (there is no albedo: do not look for one). `AiDuelistLooks.For`:
  a canon name without a `Look(...)` row must start from `AvatarAppearance.Default()`, not `Seeded()` (Kaiba's AR body
  wore the darkest skin preset).
- **HD pass (owner: "Higher quality please"):** the shader draws with line weight — the upper lash thins to a point at
  the inner corner, is heaviest in the outer third and flicks past the outer corner (`_Flick`, `_FlickLen`); a
  double-eyelid crease (`_Crease`); the iris has a limbal ring (`_LimbalRing`), a lid shadow at the top, a light pool
  at the bottom, a pupil halo, a big soft highlight, a small sharp one and a wet rim arc; brows are tapered strokes
  peaking in the outer third (`_BrowPeak`); the closed mouth is a thin weighted curve with corner marks and a faint lit
  lower lip (`_LipShade`, `_CornerMark`); the nose is a soft side shadow plus the tick (`_NoseSoft`). The new
  properties default sensibly, FaceCatalog and the rig did not change their inputs.
- **Quads hug the sculpt:** `AvatarFaceRig.FitToSurface` bakes the posed body mesh and slides every quad along its own
  normal to the most protruding vertex under its footprint plus a margin (eyes / brows 1.5 mm, mouth 2 mm, nose 2.5 mm).
  The face planes were only a guess: the nose tip stands ~1.5 cm proud of the eye line, so the nose quad drew its tick
  inside the nose; head / face morphs move every feature's depth. Runs in `Apply`, measured in the quad's frame, so the
  pose does not matter.
- **Cards drape over the skin** (`AvatarFaceRig.FitToSurface`, 10-19 rewrite): each feature is a 24x16 grid card. The
  body is baked in its current pose, its triangles are projected into the card's frame, and the exact skin depth
  (barycentric, frontmost surface) is sampled on a grid twice as fine; each card vertex takes the most forward sample of
  every cell it touches plus a margin (eyes / brows 1.5 mm, mouth 2, nose 2.5), so the card is above the skin everywhere,
  not only at its vertices. (The first cut took the nearest body VERTEX; the skin between sparse vertices - nose bridge,
  brow ridge - cut through the eyes in jagged fingers.) Only skin within 3 cm of the card's plane counts; the depth is
  divided by the card's own z scale (the stature scales the head). `OverworldAvatar3d.Apply` drapes again last
  (`Refit`), once the stature and the stance have settled.
- **Cards follow the skin, not just the head:** the front of the face carries 6-37 % Neck weight, so a card riding the
  Head bone slid against the skin whenever the head pitched against the neck (idle) or the AR look-at turned it
  (`ArOpponentDuelist.Solve`, order 650). At the fit every card vertex keeps the bone weights of the skin triangle under
  it; `Follow` (run from `Tick`) moves the vertices with non-Head weight by those bones' motion since the fit -
  linear-blend skinning in the card's frame, the Head share being the parenting itself, skipped while nothing moved.
  The rig runs at `[DefaultExecutionOrder(900)]`, after every pose writer (the highest other order is 820). Verified
  with a clearance probe (scratch `fitcheck.cs`: both surfaces from their own triangles, ~5000 samples per card): the
  player bodies, Kaiba, Téa, Yugi and Joey, idle and through attack / hit / win beats - every card 1.2-2.7 mm above the
  skin, nothing under it.
- **Editor fake null:** `GetComponent<T>()` returns a non-null "missing" object in the Editor, so `GetComponent ?? Add`
  never adds and `GetComponent()?.Call()` calls into it; the face, customizer and opponent code use `TryGetComponent`.
- **Canon duelists animate too:** `FaceCatalog.Characters` (generated from `Tools/avatar/canon_faces.json` by
  `Tools/avatar/canon_faces_to_cs.py`) holds one `CharacterFace` per `CharacterLook.FaceId` — eye / brow / nose / mouth
  parameters MEASURED from the sourced art (the `Faces/<id>.png` bands built from official Duel Links art and series
  frames, checked against the owner's episode library) by the `canon-face-presets` workflow: one measuring agent and one
  independent skeptic per duelist, guard-railed into the shader's range by the merge. `OverworldAvatar3d.ApplyFace`
  gives a faceId with a measured face the living rig (the band and the mouth decal are not built); `HideEye` keeps one
  eye off and shows that half of the band instead (Pegasus: the Millennium Eye, u 0.5..1, at -3.3 cm); `KeepDecal`
  keeps the band over the rig (opaque sunglasses). The rig reads the preset in `Apply` (`FaceCatalog.ForCharacter`),
  so FULL COSPLAY in the customizer wears the character's face as well. Review: `scratch canon_film.py` films every
  row's face next to its band.
- **Open:** Bandit Keith keeps his band (sunglasses) by design; per-character expression tuning (Kaiba's smirk, Joey's
  grin) still uses the shared `Expression` table.

## Eyewear glass + face-card headroom (2026-10-19)

Owner: "Please clear Weevil's lenses. There should be a visible asset showing the glasses lenses are there ... a floating
asset on the lenses that moves that represents reflection/refraction."

- **Clear lenses:** `Tools/avatar/lens_mask.py` (Blender + PIL; `--clothes` points at the checkout's sculpts) finds the
  lens in an eyewear sculpt per TRIANGLE: seven paint samples per triangle (white = lens), 3D components keep the large
  ones, rim/lens edge triangles are decided per texel, rasterised with the pixel apron. Output
  `Clothes/lens_<id>_<sex>.png` (imported linear + readable: the `lens_` rule in `AvatarImportPostprocessor`). The
  sculpt is untouched: `AvatarRecolor._LensMap/_LensOn` discards those texels in the colour, outline and depth passes.
- **The glass:** `WRLDZ/AvatarLensGlass` is appended as an extra material on the sculpt's own renderer (Unity draws the
  last submesh again, so no new renderer enters the avatar's renderer loops), queue 2480 after the face cards: a faint
  cool tint, fresnel sheen, a refraction rim inside the frame, and two hard-edged diagonal anime glare bars that slide
  across the lens as the view angle changes; `EyewearGlass` (order 910) feeds the lens frames each frame and runs a
  glint sweep every 3.5-8.5 s. Lens frames are measured on the first frame (during the graft the renderer's bones still
  name the source rig), through the Head bone's bind pose (exact for head-only sculpts), lens triangles picked by
  their centroid texel (Meshy atlases pack temple islands against lens islands), accumulated head-relative (the
  customizer's preview stage at y = -5000 made a float-summed centroid 40 cm off), radius from the median distance.
- **Seated on the eyes:** the wardrobe fit left Weevil's lenses 1.3 cm under the eye line; `EyewearGlass.SeatOnEyes`
  moves the renderer's head binding to an `EyewearMount` under the Head bone, shifted along the head's up so the lens
  centres meet the face rig's eye cards (placing, never reshaping, per the owner's rule).
- **Face-card headroom:** the eye and brow cards are 1.2 x 1.4 / 1.17 x 1.3 of their reference frames and
  `AvatarAnimeFace._CardScale` maps the card back, so a long flick, tall lashes or a steep canon brow never clip at the
  card edge while every parameter keeps its meaning.
- **Builds:** `AvatarAnimeFace` and `AvatarLensGlass` load through `Resources/WRLDZ/Avatar3D/Materials/*.mat` (a shader
  reached only by `Shader.Find` is stripped from player builds; the living face had no other reference).
- Weevil's bangs pass behind the frame and lenses since the hair-physics pass below (eyewear clearance map).

## Hair physics: sway, colliders, eyewear, manes over coats (2026-10-19)

Owner: "the hair and assets need to obey real physics and not clip". `HairPhysics` (on the avatar root, order 915,
bound at the end of `OverworldAvatar3d.ApplyCustomization`) feeds `WRLDZ/AvatarHair`, which moves each vertex in all
three passes (forward, shadow, depth) through `HairPhysics(p)`:

- **Sway.** One damped spring (1.6 Hz, damping 0.28, plus a little air drag) stands for the hanging mass: it lags the
  head's acceleration (a steady walk carries the hair along) and falls toward real gravity when the head tilts (the
  sculpts are shaped for an upright head). A vertex takes the swing in proportion to how far it reaches out of the
  skull (roots never move), fully below the ears, a quarter above them (gelled spikes stay put).
- **Skull, neck, chest** (measured from the body's bind pose): a swung strand is kept out, but only a strand whose
  sculpted rest position is outside them, so no sculpt changes shape at rest.
- **Arms.** Upper arms and forearms as capsules: the body's median arm radius plus 2 cm for a sleeve. Always kept out,
  because the sculpts were shaped with the arms held out and a lowered arm passed through Mai's mane. Measuring the
  coat instead gave 11 cm round Mai's arm and wrapped her hair round her shoulders.
- **Eyewear.** Once `EyewearGlass` has measured its lenses, a clearance map is drawn in the glasses' plane: how far
  forward a strand may come at each spot. That is 1.5 mm behind the glass and the frame, ramping out past the rim at
  35°, and never closer than 3 mm in front of the face's skin. Weevil's brow sits at the lens plane, so a flat "push
  behind the lens" sank strands into the skin. His bangs now pass behind the frame and show through the clear lenses.
- **Manes over coats.** Mai's Meshy coat stands 8 cm off her back and her mane hung inside it. Pegasus' hair hung
  inside his jacket below the shoulder blades. The worn layers are baked once, a few frames after binding so the
  animator has posed them (a coat's back panels may ride the arm bones; Mai's do), in the upper chest's frame. The
  sleeves and the head are left out. Per spot on the back, the depth of the hair's deepest layer inside them is taken,
  counting only strands whose nearest way out is the back (a five-view hull decides this). That depth is smoothed,
  and every layer at that spot, from the mane's inner layer back, moves back by it. The mane keeps its thickness:
  projecting only the inner layers out crushed the block and turned the shell inside out (holes). Front locks and
  curls through a shoulder are left alone. The maps are cached per set of worn meshes and hair.
- Probes used: per-piece back depth (body 10.5 cm, Mai's coat 18.8 cm, her hair 15 cm behind the upper chest),
  baked-hull-vs-now comparison, off/on contact sheets for Weevil, Yugi, Pegasus, Téa, Bakura, Mai, Joey, Kaiba, Ishizu
  and Yami Bakura.
- **Open:** the coat is still rigid to the hair (the mane rests on it, the coat never yields). The hull comes from
  the pose a few frames after binding: an AR deploy pose at spawn would bake that pose (cached per worn set).
  Curls passing through a coat's shoulder still clip there.

## Spirit Dueler disk panel + a living preview (2026-10-19)

Owner: "provide a better animation in the character customization screen and include spirit dueler selection and color
palette switching. There should be 2 duel disk assets on my system for that menu."

- **The two disks** are the owner's full-size 3D-print Battle City disks in `AssetLibrary/3d_print/duel_disk`
  (`AvatarCatalog.DiskModelIds`, saved as `AvatarAppearance.diskModel`):
  - **Battle City V2** (`""`): the "yugioh duel disk v37" kit the game is built on (`Models/WRLDZ/SpiritDueler/
    BattleCityDuelDisk.obj`), worn as a real `ArDuelDiskRig` with its own mechanism (the approved fold / deploy).
  - **Battle City Classic** (`"classic"`): the nine-section assembly kit (49 STLs + Manual.pdf). Its STLs are stored
    assembled in one millimetre frame. `Tools/avatar/disk_kit_build.py` (Blender) assembles sections 1-6 and 9 and
    skips D1 (the pivot plate, stored at the origin), G/H (the short sleeve and the stand) and F2 (the cosplay grip).
    It colours each part by its file name's print colour, sampled from the kit's cover render (the LP display's raised
    digits are white above 182.8 mm). It writes `Resources/WRLDZ/Avatar3D/Disks/disk_classic.bytes` (WKIT) in the
    canonical forearm frame: the wrist on the sleeve's fitted axis, +Y to the hand, +Z the plate's up. Blade B is its
    own object on its pin (the hub centre). No reference shows its fold, so it arrives deployed with the spirit flare
    and a lock jolt. Nothing is invented.
  - The Jump Force rip was extracted first and dropped: rips are references, never shipped.
- **`AvatarSpiritDisk`** (Presentation/Avatar, order 890) wears either disk on the left forearm, strapped like the AI
  duelists' (`NpcDuelDisk.Strap` / `ForearmFrame`, now shared). Parts are solid cel; the disk palette paints the
  plate parts (each part keeps its own shade); the aura palette drives the Spirit Dueler aura (`SpiritDuelerAura`,
  for the classic kit its own instance over both pieces).
- **`AvatarPreviewDirector`** (UI, order 880):
  - On the Title / Spirit / Duel Disk / Aura tabs the arm runs the AI's Battle City sequence: folded standby at the
    hip, shield snap (ease-out-back), held while the V2 deploys (2.4 s), then the level stance across the navel (Joey's
    Falsebound Kingdom art) with the zones toward the camera. `Focus.Disk` frames it.
  - Each edit gets a reaction: a smile (face), hand on hip + smug look (body, hair, clothes), fist pump (title, disk),
    a cast + aura flare (spirit, aura). After quiet spells (not on the disk tabs) the duelist strikes a pose; the head
    and eyes find the camera.
- **Gestures**: `OverworldAvatar3d.PlayGesture(take, maxSeconds)` plays a duelist-set clip over the idle (third mixer
  input). While one plays the collarbones settle back toward the idle's, keeping 20 % of the Duel clips' ~47° shrug.
- **Fixes on the way:**
  - `SpiritSuperAura` wrote alpha 1 under additive blending, which drew a black box round the disk in the transparent
    preview (and would occlude MR passthrough). It now writes the light it adds as alpha.
  - The face shader has an unused `_MainTex`: uGUI asked the eye / mouth cards for it and logged errors.
- **Open:** AR duels keep the V2 kit's zone layout whichever disk is chosen (the classic kit is not laid out as a
  playmat). The overworld map token does not wear the disk.
