# Eldritch Solid Vision — Asset Recommendations

Research for the UGUI main-menu overhaul. Theme pillars: **Egypt** (hieroglyphs, gold, lapis, sandstone) × **Eldritch** (cosmic dread, tentacle/organic motifs, non-Euclidean hints) × **Sorcery** (glowing arcane energy, animated runes, floating particles) × **Solid Vision** (holographic Yu-Gi-Oh!-style slates with card-game readability).

Target: Unity 6 / URP 17 / UGUI, Android (S23) AR + OpenXR lenses. Studio preference: CC0 / OFL / permissive. Already in-project: Bangers, Exo 2, Russo One, Orbitron, Rowdies, Kenney Future, Noto Sans Egyptian Hieroglyphs, Kenney CC0 UI, Open Duelyst CC0 art.

**Verified on 2026-09-21** — every row below was opened and its page read for name / author / price / license. Nothing was downloaded or installed. Prices are USD as displayed on the day; Asset Store sale prices change.

License legend: **CC0** = no attribution, any use. **OFL** = free incl. commercial, may not sell the font alone; renamed derivatives need a new name. **MIT** = attribution in source/credits. **Unity EULA** = Standard Unity Asset Store EULA (per-seat "Extension Asset"; fine in shipped games, no redistribution of raw assets). **UCL** = Unity Companion License (commercial OK, only with a Unity engine license; derivatives of the sample itself belong to Unity).

---

## 1. UI kits / frame packs

| Name | Source / URL | Price | License | Fits | Notes |
|---|---|---|---|---|---|
| **Fantasy UI Borders** (Kenney) | https://kenney.nl/assets/fantasy-ui-borders (mirror: https://opengameart.org/content/fantasy-ui-borders) | Free | CC0 | Egypt / Sorcery — ornate 9-slice frames for card slates and panels | 130–140 PNG sprites + tilesheets + vector source (2023). Recolor to gold/lapis; the "vector source" means you can add tentacle/glyph corners in Inkscape without licensing worries. Best zero-cost frame base. |
| **UI Pack – Sci-Fi** (Kenney) | https://kenney.nl/assets/ui-pack-sci-fi | Free | CC0 | Solid Vision — thin holographic bezels, sliders, toggles | 130 files, remade v2.0. Pair with a Canvas Shader Graph scanline/glow material for the "hologram slate" read. Also **UI Pack** (430 files, https://kenney.nl/assets/ui-pack) for generic buttons/sliders. |
| **Ancient Egypt GUI** (PONETI) | https://assetstore.unity.com/packages/2d/gui/ancient-egypt-gui-143451 | $9.99 | Unity EULA | Egypt — the only ready-made Egyptian GUI on the store: gold-scarab frames, papyrus panels, hieroglyph dividers | 75.8 MB, v1.1 (Nov 2019), Unity 2018.4+. Raster PNG (no shader work), so it imports into Unity 6 UGUI fine. Old but the closest match to the "gold + sandstone" pillar. Check preview images before buying — the store page does not list element counts. |
| **Dynamic Holo Sci-Fi UI Pack** (D.F.Y. STUDIO) | https://assetstore.unity.com/packages/2d/gui/dynamic-holo-sci-fi-ui-pack-288939 | $39.99 | Unity EULA (single entity) | Solid Vision — animated blue-hologram HUD panels, popups, AR/VR-oriented | 297.5 MB, v1.0 (Jun 2024), Unity 2021.3+. Animated UGUI prefabs; you will retint blue → gold/teal. Pricier alternative: **Customizable SciFi Holo Interface** (Excelsior Assets, $32.50, v2.0 Jul 2025, URP-compatible, https://assetstore.unity.com/packages/2d/textures-materials/customizable-scifi-holo-interface-69794). |
| **Dark Fantasy GUI / UI Kit** (HONETi) | https://assetstore.unity.com/packages/2d/gui/dark-fantasy-gui-ui-kit-68828 | $14.99 | Unity EULA | Eldritch / Sorcery — grimdark frames, bars, icons, inventory windows | 14.4 MB, v1.0 (Mar 2017). Cheap source of dark-metal frame language to hybridize with the Egyptian gold. Same-vintage caveat as PONETI. |
| **Golden UI** (Buch) | https://opengameart.org/content/golden-ui | Free | CC0 | Egypt — gold pixel-art RPG frames/panels/bars | PSD + PNG (2013). Pixel-art scale; only useful as a pixel-flavored fallback or for tiny icons. The "Bigger Than Ever" edition is **CC-BY-SA 3.0** (share-alike — avoid). |

Not recommended: Sunvale "Ancient Rome GUI" (itch, $59.99) — marble Roman, not Egyptian; itch.io has no dedicated Egyptian GUI pack (the `egypt` tag is tilesets/sprites only, checked 2026-09-21).

## 2. Fonts (all OFL via Google Fonts unless noted)

| Name | Source / URL | Price | License | Fits / Role | Notes |
|---|---|---|---|---|---|
| **Cinzel** (Natanael Gama) | https://fonts.google.com/specimen/Cinzel | Free | OFL 1.1 | **Epigraphic title** — Roman-inscription capitals; reads as "carved in stone" over sandstone | Variable weight 400–900. Caps-only feel; use 700–900 for menu titles, letter-spacing +5–10%. Same designer as Exo 2 (already in project) so pairing is coherent. |
| **Cinzel Decorative** (Natanael Gama) | https://fonts.google.com/specimen/Cinzel+Decorative | Free | OFL 1.1 | **Hero wordmark / single-word slate headers** | Regular/Bold/Black. Flourished capitals — use sparingly (logo, mode names), not for lists. |
| **Metamorphous** (James Grieshaber) | https://fonts.google.com/specimen/Metamorphous | Free | OFL 1.1 | **Eldritch subtitle / flavor text** — blackletter-ish arcane edge without unreadability | Regular only. Good for "forbidden tome" labels and tooltips. |
| **Uncial Antiqua** (Astigmatic) | https://fonts.google.com/specimen/Uncial+Antiqua | Free | OFL 1.1 | Sorcery — rune-like uncial for spell/ability names | Regular only. Alternative to Metamorphous; pick one, not both. |
| **Almendra** (Ana Sanfelippo) | https://fonts.google.com/specimen/Almendra | Free | OFL 1.1 | **Body / lore paragraphs** on parchment panels | Regular, Italic, Bold, Bold Italic — the only pick here with a full family, so it is the natural body face for long text. |
| **Michroma** (Vernon Adams) | https://fonts.google.com/specimen/Michroma | Free | OFL 1.1 | **Holographic readout** — wide square techno face, extremely legible at small AR sizes | Regular only. Use for HP/LP counters, phase labels, timers. Complements Orbitron (already in project) rather than replacing it. |
| **Audiowide** (Astigmatic) | https://fonts.google.com/specimen/Audiowide | Free | OFL 1.1 | Solid Vision — rounded sci-fi display for button labels | Regular only. Softer than Orbitron; good for tappable chip text. |
| **Orbitron** / **Exo 2** (already in project) | https://fonts.google.com/specimen/Orbitron · https://fonts.google.com/specimen/Exo+2 | Free | OFL 1.1 | Holographic readout (Orbitron, var 400–900) / UI body (Exo 2, var 100–900 + italics) | Keep as-is. Exo 2 remains the default UI sans; Michroma/Audiowide are accents. |
| **Noto Sans Egyptian Hieroglyphs** (Google, already in project) | https://fonts.google.com/noto/specimen/Noto+Sans+Egyptian+Hieroglyphs | Free | OFL 1.1 (Google Fonts build) | Egypt — all 1,071 Unicode hieroglyphs as glyph decoration / ornament strips | Use TMP fallback + `<font>` tags for hieroglyph cartouches. Note: the same hieroglyphs exist as **1,072 PD SVGs** on Wikimedia Commons (see §5) if you need vectors. |
| Aegyptus (George Douros, UFAS) | https://dn-works.com/ufas/ | Free | **Unverified** — listed as "free for any use" by HieroglyphsEverywhere; actual terms are in the site's License.pdf | Egypt — 7,000+ extra Egyptological glyphs beyond Unicode | **Risk**: license PDF could not be fetched; verify before shipping. Noto covers Unicode already, so only needed for exotic signs. |
| NewGardiner (Mark-Jan Nederhof) | https://mjn.host.cs.st-andrews.ac.uk/egyptian/fonts/newgardiner.html | Free | **Non-commercial only** (per HieroglyphsEverywhere list) | — | **Do not ship.** Listed so nobody grabs it by mistake. |

Font role stack: **Cinzel** (title) → **Michroma/Orbitron** (holo readouts) → **Exo 2** (UI body) → **Almendra** (lore) → **Metamorphous** (eldritch flavor) → **Noto Egyptian** (ornament). Google Fonts specimen pages are JS-rendered; license/designer facts above were verified from the `METADATA.pb` files in https://github.com/google/fonts/tree/main/ofl/.

## 3. Particle textures & VFX

| Name | Source / URL | Price | License | Fits | Notes |
|---|---|---|---|---|---|
| **Particle Pack** (Kenney) | https://kenney.nl/assets/particle-pack | Free | CC0 | Sorcery — 80 × 512² sprites: smoke, fire, sparks, magic, stars, scorch, twirl | The core CC0 sheet for floating motes, ember drift and glyph-burst puffs. Pair with **Smoke Particles** (70 files, https://kenney.nl/assets/smoke-particles) for eldritch fog. |
| **Particle Pack \| Starter Assets** (Unity Technologies) | https://assetstore.unity.com/packages/vfx/particles/particle-pack-127325 | Free | Unity EULA | Sorcery / Eldritch — full authored systems (fire, explosions, smoke, magic) with URP materials | v3.1 (Apr 2026), URP-compatible with Unity 6000.x. Heavier (157 MB); cherry-pick textures + a few systems. |
| **Magic Effects FREE** (Hovl Studio) | https://assetstore.unity.com/packages/vfx/particles/spells/magic-effects-free-247933 | Free | Unity EULA | Sorcery — magic circles, portals, sparks, hit effects; Built-in/URP/HDRP | v1.6 (Jun 2025), 36 MB, Unity 2021.3+. Best free source of rune-circle textures to bake into UI. |
| **Free Game VFX – Magic Circle (URP)** (Eric VFX Studio) | https://assetstore.unity.com/packages/vfx/particles/free-game-vfx-magic-circle-urp-344984 (sibling: **Free Game VFX Collection (URP)**, https://assetstore.unity.com/packages/vfx/particles/free-game-vfx-collection-urp-345212) | Free | Unity EULA | Sorcery — URP-native animated summoning circle | Tiny (<1 MB / 4.7 MB), updated Jan/Sep 2026, URP-only (no Built-in). Good stencil for a "summon slate" transition. |
| **Horror Texture Pack** (Screaming Brain Studios) | https://screamingbrainstudios.itch.io/horror-texture-pack | Free (PWYW) | CC0 | Eldritch — 300 textures (100 designs × 3 sizes) incl. **15 transparent stain overlays**, decaying stone, misc | Stains double as panel grime/ichor decals. Same author has **Noise Texture Pack** (786 free CC0 noises) for dissolve/hologram-interference masks. |

To render any of these **inside the UGUI Canvas** (masked, sorted with UI) use **UI Particle** from §4.

## 4. Shader / material tools for UGUI holograms

| Name | Source / URL | Price | License | Fits | Notes |
|---|---|---|---|---|---|
| **Canvas Shader Graph** (built into URP 17 / Unity 6) | https://docs.unity3d.com/6000.1/Documentation/Manual/urp/canvas-shader.html | Free (engine) | Unity engine license | Solid Vision — author scanline/fresnel/chromatic hologram materials directly for UGUI Images and RawImages | Create via `Assets > Create > Shader Graph > URP > Canvas Shader Graph`. **Limitation:** no vertex blocks (fragment-only); fragment stack = Base Color / Alpha / Emission (+ Alpha Clip). Works with RectMask2D/Mask via the standard UI stencil keywords when you keep the default Canvas target settings. |
| **Shader Graph "UGUI Shaders" sample** (Unity) | https://discussions.unity.com/t/announcing-the-shader-graph-ugui-shaders-sample/1602836 | Free | UCL | Sorcery / Solid Vision — 50+ subgraphs (SDFs, gradients, patterns, ease curves, time) + procedural buttons, meters, progress rings | Import from Package Manager → Shader Graph → Samples → "UGUI Shaders". Requires latest Unity 6. Procedural, texture-free — ideal for crisp AR slates at any DPI. |
| **UIEffect** (mob-sakai / Coffee) | https://github.com/mob-sakai/UIEffect (OpenUPM: `com.coffee.ui-effect`) | Free | MIT | All pillars — dissolve, burn, blaze, melt, shiny sweep, edge luminescence, gradation, outline/shadow, pattern transitions on any Graphic incl. **TextMeshPro** | Unity 2020.3+, URP/HDRP, Shader Graph 2023.2/6.0+, Timeline. Gives "Solid Vision materialize" (dissolve-in) and "eldritch corruption" (melt/burn) with zero shader authoring. Install via git URL `https://github.com/mob-sakai/UIEffect.git?path=Packages/src`. |
| **UI Particle** (mob-sakai) | https://github.com/mob-sakai/ParticleEffectForUGUI (OpenUPM: `com.coffee.ui-particle`) | Free | MIT | Sorcery — bakes ParticleSystems into CanvasRenderer: maskable, sortable, CanvasGroup alpha, trails | Unity 2018.3+, URP/HDRP. No extra camera/RenderTexture, so it is AR-headset friendly. |
| **TextMeshPro SDF shaders** (built in, com.unity.ugui 2.x) | https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/TextMeshPro/Shaders.html | Free (engine) | Unity engine license | Solid Vision / Egypt — outline, underlay (glow), bevel via `Distance Field` / `Distance Field Overlay`; mobile variants | Use the **Mobile Distance Field** variant on S23; underlay = cheap gold glow for Cinzel titles. |
| **Amplify Shader Editor** (Amplify Creations) | https://assetstore.unity.com/packages/tools/visual-scripting/amplify-shader-editor-68570 | $44 (sale, list $88) | Unity EULA | Alternative node editor with UI/Canvas templates, Built-in/URP/HDRP | v1.9.9.12 (Jul 2026). Only worth it if the team already prefers ASE; Canvas Shader Graph covers the hologram needs for free. |
| **Shapes** (Freya Holmér) | https://assetstore.unity.com/packages/tools/particles-effects/shapes-173167 · docs: https://acegikmo.com/shapes/docs/ | $50 (sale, list $100) | Unity EULA | Sorcery — anti-aliased vector arcs/polylines for animated rune rings | v4.6.0 (Apr 2026), URP OK. **Caveat (docs):** works only in Camera/World-space canvases, *not* Screen Space–Overlay; no CanvasRenderer/RectTransform integration. Fine for AR world-space slates, wrong tool for the overlay menu. |

## 5. Hieroglyph & Egyptian ornament vector / PNG sets

| Name | Source / URL | Price | License | Fits | Notes |
|---|---|---|---|---|---|
| **Category: SVG Egyptian hieroglyphs** (Wikimedia Commons) | https://commons.wikimedia.org/wiki/Category:SVG_Egyptian_hieroglyphs | Free | Per-file; the **Noto Sans Egyptian Hieroglyphs subcategory (1,072 SVGs)** inherits OFL — safe. The **NewGardiner subcategory (4,532 SVGs)** derives from a non-commercial font — **avoid** | Egypt — every Unicode hieroglyph as clean vector for cartouches, dividers, rune-ring glyphs | Check each file's description page; use only the Noto-derived set. |
| **Owen Jones, *The Grammar of Ornament* (1856/1868) — Egyptian plates 4–11** | e.g. https://commons.wikimedia.org/wiki/Category:The_Grammar_of_Ornament_plate_10 | Free | Public domain (author d. 1874) | Egypt — authentic lotus/papyrus borders, tomb-ceiling patterns, gold-on-lapis palettes | Scans up to 3,884 × 5,466 px. Trace in Inkscape for 9-slice edge ornaments; the plates are also the best **color reference** for the gold/lapis/sandstone palette. |
| **Ancient Egypt** (imacubsfan23, OpenGameArt) | https://opengameart.org/content/ancient-egypt-0 | Free | CC0 | Egypt — pixel hieroglyph sheets (A–M, S–L), Rosetta stone, pyramid, **5.2 MB hi-res papyrus texture** | The papyrus texture is the useful part for panel backgrounds. |
| **Egyptian Frame / Border** (GDJ, Pixabay) | https://pixabay.com/vectors/egyptian-frame-border-ancient-egypt-5597112/ | Free | Pixabay Content License | Egypt — 1902×1920 SVG+PNG ornamental frame | **Caveat:** Pixabay license forbids redistributing the asset standalone/unmodified and disclaims third-party IP; fine when baked into the UI. GDJ's originals are also on OpenClipart (PD). |
| Hieroglyph Sprites (Andor Salga, OpenGameArt) | https://opengameart.org/content/hieroglyph-sprites | Free | **CC-BY-SA 3.0** | Egypt — pixel tiles | **Risk:** share-alike could taint derived UI art; skip unless the whole sprite atlas can be released CC-BY-SA. |
| Vecteezy "Egyptian ornament/border" vectors | https://www.vecteezy.com/free-vector/egyptian-ornament | Free tier / Pro from ~$14/mo | **Free tier requires attribution**, caps commercial use (≤50 print copies, ≤$1k production budget, no resale products), and demands deletion 30 days after project end (https://www.vecteezy.com/licensing-agreement) | — | **Not recommended** for a shipped game on the free tier; Pro removes attribution but the 30-day stockpile clause still applies. Prefer Commons/Kenney. |

## 6. Tentacle / eldritch decals & ambient audio (optional)

| Name | Source / URL | Price | License | Fits | Notes |
|---|---|---|---|---|---|
| **Horror Texture Pack** (Screaming Brain Studios) | https://screamingbrainstudios.itch.io/horror-texture-pack | Free (PWYW) | CC0 | Eldritch — stain overlays + decayed stone/metal for corruption creeping over Egyptian frames | Listed in §3 too; the 15 transparent stains are the decal set. |
| **Tentacle** (SomeUnusualGames, OpenGameArt) | https://opengameart.org/content/tentacle | Free | CC0 | Eldritch — 120×96 animated blue tentacle sprite | Tiny/pixel; only as a placeholder or for a hidden-easter-egg UI writhe. No hi-res CC0 tentacle decal set was found — plan to author tentacle corner ornaments in-house (trace over Jones's lotus borders and warp). |
| **Tentacle – Poly** (SnoozyRat, itch.io) | via https://itch.io/game-assets/tag-lovecraft | $9.99 | itch standard (check page) | Eldritch — rigged, animated low-poly 3D tentacle | Useful for a world-space AR menu prop peeking around slates; not a 2D decal. |
| **Interface Sounds** + **UI Audio** (Kenney) | https://kenney.nl/assets/interface-sounds · https://kenney.nl/assets/ui-audio | Free | CC0 | Solid Vision — 100 + 50 clicks/snaps/confirm for holo-slate taps | Layer a low sub-drone under them for dread. |
| **Spooky Ambience** (Sound Effects Library, free-stock-music.com) | https://www.free-stock-music.com/sound-effects-library-spooky-ambience.html | Free | CC0 1.0 | Eldritch — 1:09 dark ambient loop bed | Attribution not needed. |
| **Lovecraft Horror Ambient** (Luis_Humanoide, Pixabay) | https://pixabay.com/music/mystery-lovecraft-horror-ambient-289897/ | Free | Pixabay Content License | Eldritch — 2:06 ambient track | No attribution; same standalone-redistribution caveat as Pixabay images. |
| **Lovecraftian Soundscapes** (Audible Dread, itch.io) | https://audible-dread.itch.io/lovecraftian-soundscapes | $8.90 | Custom: commercial OK, no attribution, no resale/redistribution (License Terms.pdf included) | Eldritch — 24 tracks, WAV 24-bit/48 kHz + OGG (930 MB) | Sibling **Lovecraftian Horror Sound Effects** (70 SFX, $8.90, https://audible-dread.itch.io/lovecraftian-horror-sound-effects) for whispers/impacts. Made without generative AI (author statement). |
| **Lovecraftian Ambient Horror (Music Pack)** (Gwyn Wilson, itch.io) | https://gwcomposer.itch.io/lovecraftian-ambient-horror | $15 | Custom: "use in whatever media you like", attribution optional | Eldritch — 8 loopable tracks, 20+ min, WAV + MP3 | Best single purchase for a menu music bed. |

---

## Verdict — "if only three purchases" (≈ $34)

1. **Ancient Egypt GUI** (PONETI, $9.99) — the only real Egyptian frame/ornament kit; gives the gold-scarab-papyrus language instantly, and its PNGs slot straight into Kenney-style 9-slice prefabs.
2. **Lovecraftian Soundscapes** (Audible Dread, $8.90) — cheapest way to make the menu *feel* eldritch; visuals alone will not sell cosmic dread on a phone screen.
3. **Lovecraftian Ambient Horror (Music Pack)** (Gwyn Wilson, $15) — loopable menu bed; swap for **Dark Fantasy GUI / UI Kit** ($14.99) if audio is handled elsewhere and you want a second frame vocabulary to hybridize.

Skip for now: Dynamic Holo Sci-Fi UI Pack ($39.99), Amplify ($44), Shapes ($50) — Canvas Shader Graph + UIEffect + the free UGUI Shaders sample cover the hologram pillar with no spend.

## Zero-budget path (all free, all CC0 / OFL / MIT / UCL / engine-bundled)

- **Frames:** Kenney *Fantasy UI Borders* (recolor gold/lapis, add hieroglyph corners from the Noto SVG set) + Kenney *UI Pack – Sci-Fi* for the inner holo bezel.
- **Ornament & palette:** Owen Jones Egyptian plates (PD) traced into 9-slice edges; imacubsfan23 papyrus texture (CC0) for panel fill; Noto Sans Egyptian Hieroglyphs SVGs (OFL) for cartouches.
- **Type:** Cinzel (title) · Michroma / Orbitron (holo readouts) · Exo 2 (UI) · Almendra (lore) · Metamorphous (eldritch accents) · Noto Egyptian (glyph strips) — all OFL.
- **Hologram materials:** URP 17 Canvas Shader Graph (scanline + fresnel + chromatic offset) built from the Shader Graph *UGUI Shaders* sample subgraphs; TMP Mobile Distance Field with underlay glow for text.
- **Motion & corruption:** UIEffect (dissolve-in "Solid Vision materialize", burn/melt "eldritch taint", shiny sweep on hover) + UI Particle rendering Kenney *Particle Pack* motes and *Smoke Particles* fog inside the Canvas; Hovl *Magic Effects FREE* / Eric VFX *Magic Circle (URP)* for summoning-ring textures.
- **Decals:** Screaming Brain *Horror Texture Pack* stains + *Noise Texture Pack* dissolve masks; author tentacle corners in-house (no adequate CC0 set exists).
- **Audio:** Kenney *Interface Sounds* + *UI Audio*; *Spooky Ambience* (CC0) or Pixabay *Lovecraft Horror Ambient* as bed.

## License-risk flags (summary)

- **Share-alike:** OGA *Hieroglyph Sprites* (CC-BY-SA 3.0), *Golden UI – Bigger Than Ever* (CC-BY-SA 3.0) — avoid.
- **Non-commercial:** NewGardiner font and its 4,532 Commons SVG derivatives — avoid.
- **Attribution + usage caps:** Vecteezy Free tier — avoid for a shipped title.
- **Unverified terms:** Aegyptus / UFAS (License.pdf not reachable 2026-09-21) — verify before use; Tiny Hieroglyphs Asset Pack (Laurie Raye, $3, seen on the itch `egypt` tag) — page not reachable, license unknown.
- **Pixabay Content License:** no attribution, but no standalone redistribution and you assume third-party IP risk — fine baked into UI atlases.
- **Unity Companion License (UGUI Shaders sample):** commercial OK, only within Unity-licensed projects; derivative *of the sample itself* belongs to Unity — irrelevant for shipping a game, relevant if you ever publish the subgraphs.
- **Unity Asset Store EULA:** per-seat; do not commit raw store assets to a public repo.
