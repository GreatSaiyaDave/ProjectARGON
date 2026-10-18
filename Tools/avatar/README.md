# Tools/avatar — Meshy sculpt wardrobe

- `meshy_wardrobe.py` — Blender 5.2 headless. Integrates the owner's Meshy image-to-3D pieces
  (`Assets/Models/Characters/**/*.blend`) onto the ArgonAvatarRig bodies as skinned wardrobe items (`*_3d` ids)
  **as they are**: full mesh density, the packed 4096² base colour and normal map byte for byte, no reshaping. The tool
  only places (one uniform scale + translation), articulates (sleeves / legs posed and un-posed through the rig), skins,
  removes the Meshy mannequin bust (colour clusters; nothing when a sculpt has none) and writes the body hide map.
  `blender -b --python Tools/avatar/meshy_wardrobe.py -- --out <dir> [--only id,id] [--sex male|female] [--list]
  [--clusters] [--no-render] [--no-hide]`
  Review: `review_<id>_<sex>_{front,q,side,back,alone}.png` (the body already wears its hide map), `--clusters` adds
  `{cfront,cside}` false-colour tiles (killed faces black). Per-piece overrides in `PIECES`: `fit_mode`, `fit_dz`,
  `fit_scale_fix`, `fit_w`, `mann_rgb`, `no_mannequin`, `has_mannequin`, `cut_below`, `long`, `full`, `shoulder_frac`.
- `meshy_wardrobe_install.py <out dir>` — copies FBX, textures, hide maps and thumbnails into
  `Assets/Resources/WRLDZ/Avatar3D/{Clothes,Hair}` and adds the hair catalogue entries. The Editor's next refresh imports
  them (`AvatarImportPostprocessor`: 4096 textures, normal maps, readable uncompressed meshes).
- Catalogue and looks are code: `ClothingCatalog.cs` (`N(...)` rows; every `*_3d` id is `Sculpt`), `CharacterLooks.cs`,
  `hair_catalog.json`. Sculpts carry no blend shapes: `OverworldAvatar3d.MorphFollow.cs` makes them follow the body's
  shapes at run time.
- Design notes: `Assets/Scripts/WRLDZ/AVATAR_3D_INTEGRATION.md`, addenda 2026-10-18.
- Offline compile of a checkout: `python3 Tools/csc_check.py --editor`.
- `body_morphs.py` — authors the build / face blend shapes on the bases (`--render` for a sheet, `--install` to replace
  `Assets/Resources/WRLDZ/Avatar3D/argon_base_<sex>.fbx`; the mesh, normals, UVs and rig stay identical to the build-tree
  bodies); writes `argon_avatar_<sex>_morphs.glb` for the bake.
- `bake_body_morphs.py --body <dir>` — transfers every body shape onto the installed *shell* garments / hair / outfits
  (the built wardrobe; sculpts are followed at run time instead). Run it after any shell change.
