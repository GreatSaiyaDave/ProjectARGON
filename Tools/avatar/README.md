# Tools/avatar — Meshy sculpt wardrobe

- `meshy_wardrobe.py` — Blender 5.2 headless. Fits the owner's Meshy image-to-3D pieces
  (`Assets/Models/Characters/**/*.blend`) onto the ArgonAvatarRig bodies as skinned wardrobe items (`*_3d` ids).
  `blender -b --python Tools/avatar/meshy_wardrobe.py -- --out <dir> [--only id,id] [--sex male|female] [--list]`
- `meshy_wardrobe_install.py <out dir>` — copies the FBX / textures into `Assets/Resources/WRLDZ/Avatar3D/{Clothes,Hair}`,
  runs the hide-map and thumbnail tools (`~/argon-avatar/tools/custom/`), writes the head / hand hide maps and the
  hair catalogue entries.
- Catalogue and looks are code: `ClothingCatalog.cs` (`N(...)` rows), `CharacterLooks.cs`, `hair_catalog.json`.
- Design notes and fitting rules: `Assets/Scripts/WRLDZ/AVATAR_3D_INTEGRATION.md`, addendum 2026-10-18.
- Offline compile of a checkout: `python3 Tools/csc_check.py --editor`.
- `body_morphs.py` — authors the build / face blend shapes on the bases (`--render` for a sheet, `--install` to replace
  `Assets/Resources/WRLDZ/Avatar3D/argon_base_<sex>.fbx`); writes `argon_avatar_<sex>_morphs.glb` for the bake.
- `bake_body_morphs.py --body <dir>` — transfers every body shape onto every installed garment / hair / outfit FBX so
  the build and face sliders never clip. Run it after any wardrobe change.
