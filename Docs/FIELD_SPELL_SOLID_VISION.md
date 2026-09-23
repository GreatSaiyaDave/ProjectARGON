# Field Spells as Solid Vision environments

How the anime's holograms handle Field Spells, what AR cannot copy, and how
Duel Monsters WRLDZ adapts it. Code: `Assets/Scripts/WRLDZ/Presentation/ArInteraction/`
(`FieldSpellEnvironment.cs`, `ArFieldSpellFloor.cs`, `ArFieldMotes.cs`,
`ArFieldTerrainPad.cs`). Guard: `python3 Tools/field_spell_env_check.py`.

## 1. What the anime does (paraphrased, visual grammar only)

The show handles Field Spells in two different ways, and both matter here.

**Duelist Kingdom: terrain under the monster.** The arena floor is a patchwork
of terrain (sea, mountain, meadow, forest, wasteland, darkness). A monster
standing on terrain that suits it visibly powers up. Terrain is **local**: it is
the ground under that monster, not a picture on the far wall.

**Battle City onward: the arena becomes the field.** A Field Spell slots into
its own place on the disk and the projection **washes outward** from there. The
beats, in order:

| Beat | What you see |
|---|---|
| Slot | Card seats in the disk's field slot. The environment's source is the duelist. |
| Sweep | A wave radiates out and rebuilds the space as it passes. It is an event, not a cut. |
| Build | Ground first, then verticals (cliffs, trees, ruins), then sky, then atmosphere (embers, spray, leaves). |
| Grade | Everything in frame, monsters included, takes the field's colour (Yami goes violet-black, Umi goes blue). |
| React | Monsters the field favours glow; hostile ground visibly weakens them. |
| Replace | A new field wipes out the old one before building. Only one environment at a time. |
| Recede | The field stays as ambience and never competes with a summon or an attack. |

## 2. What AR cannot copy

Passthrough is the real street. We cannot replace the ground or the sky, and
the repo already rules out a painted mat (`FIELD_SPLIT.md`: *midfield stays
empty air, no floor carpet, no far backdrop*). A full-arena floor texture
would also hide real obstacles, which is a safety problem on a phone held at
arm's length.

So every anime layer is translated into a register that **adds to** the
street and never covers it:

| Anime layer | AR-safe register | Component |
|---|---|---|
| Sweep from the disk | Ground ring erupting from the owner's Field drawer side (`FieldSpellArenaLocal`) | `ArFieldSpellFloor` |
| Verticals + sky | The card illustration on two bowed side walls that **rise** as the ring passes, and sink on dissolve | `ArFieldSpellFloor` |
| Ground | Duelist Kingdom terrain: a soft disc of the field's own ground under each monster, not a carpet | `ArFieldTerrainPad` |
| Atmosphere | Motes drifting through the street volume (one draw call) | `ArFieldMotes` |
| Grade | Capped tint (≤ 0.2) on arena holos only. Hand, disk and passthrough stay true colour | `ArAnimePresentation.SetFieldWash` |
| React | Boon aura (accent column, rising) or bane aura (drained, low) on face-up monsters the field changed | `ArFieldTerrainPad` |
| Replace | Newest activation wins: a fast wipe (0.35 s), then the new sweep | `ArFieldSpellFloor` |

Timing: 0.45 s beat (the disk ejects and flips the card), a 1.35 s sweep, a 0.6 s
dissolve when the field leaves.

## 3. The assets: data plus the card's own art

The repo has no prefabs and no custom shaders. Everything is built in C#,
and the owner pulls from GitHub into Unity Hub. So a Field Spell "asset" is
**one row of data plus its illustration**, not an authored scene:

- **Curated row** (`FieldSpellEnvironments`): sky / ground / accent colours,
  mote motion, mote density, wash strength. All 21 Field Spells in the pool
  have one.
- **Illustration bake** (at activation, CPU, once): the walls get the
  illustration window with alpha melting into the street, and the terrain disc
  gets the lower 45% of the illustration on a radial fade, pulled 30% toward
  the ground colour. Umi's pad is literally Umi's water; Forest's is Forest's grass.
- **Fallback** for Field Spells added in later sets: the palette is derived
  from the art (sky = top-third mean, ground = bottom-third mean,
  accent = heaviest saturation-weighted hue bin, mote motion from the accent
  hue). Plain averaging was tried and rejected because it turns two-hue art
  into mud (Centrifugal Field came out brown). The hue-bin method reads 19 of 21
  correctly. The two misses (Luminous Spark, whose vivid pixels are the red
  fiend, and Sanctuary in the Sky, deep blue instead of temple gold) are
  exactly why the curated rows exist.

**Illustration inset.** Field art uses its own measured window,
`IllustrationInset` = (0.125, 0.305, 0.755, 0.505), bottom-left origin. The
shared `CardArtFocus.ArtworkNormRect` still includes the teal "SPELL CARD" strip
and the side frame (the guard shows 61% of its border on the frame). The field
code does not touch the shared crop.

### The 21 rows by family

| Family | Rule shape | Cards | Motes |
|---|---|---|---|
| Duelist Kingdom terrains | +200 ATK/DEF by Type (Umi and Yami also weaken some Types) | Yami, Forest, Mountain, Sogen, Umi, Wasteland | Wisps, Leaves, Wind, Pollen, Bubbles, Dust |
| Attribute zones | +500 ATK / −400 DEF by Attribute | Molten Destruction, Mystic Plasma Zone, Luminous Spark, Gaia Power, Rising Air Current, Umiiruka | Embers, Sparks, Sparks, Leaves, Wind, Bubbles |
| Named fields | Archetype or rule text | A Legendary Ocean, Harpies' Hunting Ground, Necrovalley, Pandemonium, The Sanctuary in the Sky, Chorus of Sanctuary, Array of Revealing Light, Centrifugal Field, Fusion Gate | per row |

Wash is strongest on the moody fields (Yami 0.18; Molten, Plasma and
Pandemonium 0.16) and near zero on daylight ones (Sogen, Mountain 0.06).

## 4. Engine hook: the aura never guesses rules

UI never invents game state (`ygo-ui-lore` rule 6). The aura does **not** keep
a Type/Attribute table. `FieldSpellEffects.RefreshBoard` clears modifiers,
applies both Field Spells first, then snapshots each monster's
`CardInstance.FieldAtkDelta` / `FieldDefDelta` before monster auras and
equips stack on. Rules never read these fields. Tests in
`ClassicEraRegressionTests`: Umi boon +200/+200, bane −200/−200, Command
Knight's aura not counted as the field's, and zero once the field leaves.

Hidden information: face-down monsters get terrain but **never** an aura, since
an aura would leak the monster's Type.

## 5. Adding a Field Spell from a new set

1. Do nothing: it already gets an art-derived environment.
2. To curate it, add one `E(id, "Exact Name", sky, ground, accent, FieldMotes.X, density, wash)`
   line in `FieldSpellEnvironment.cs`. Start from the art: look at the card,
   then pick the accent a viewer would name ("gold", "sea blue").
3. Run `python3 Tools/field_spell_env_check.py`. It checks the name against
   `cards_db.json`, the wash cap, accent brightness, the aura contract and the
   inset. Uncurated fields are listed, not failed.

## 6. Verified here vs. needs a device

Verified in the cloud VM: the headless engine suite (1152 pass, 0 fail, 40/40 AI
duels, with the same win split as before the change); all new/changed
presentation files compile against Unity reference assemblies
(`UnityEngine.Modules` 2021.3); the guard passes; the inset is clear of the frame
on all 21 scans.

Needs Play mode / an S23 (not visible from the VM):

- Wall translucency. The wall material now sets URP `_Surface` = Transparent.
  Without it, URP Unlit writes alpha 1, so the old "52%" walls were
  likely opaque. Walls will look lighter than before.
- The sweep reading as an event from the owner's side, and motes staying
  readable against a bright street.
- `Sprites/Default` surviving shader stripping in a device build (same
  dependency as `ArArenaStartFlash` and the legal-zone glows).

## 7. Signature set pieces (one per field)

The layers above make every field feel like a field. What the anime really
sells is the one element a viewer names the field by: Umi's sea, Sogen's grass,
Yami's dark. That element is a **signature kit**: a procedural component under
the floor (`ArFieldSignature` subclasses, `ArFieldSig*.cs`), chosen by the
row's `FieldSignature` and styled by `SignatureVariant` / `SignatureScale` plus
the row palette.

**Easy tier first.** The 12 fields that only change ATK/DEF already have their
whole rules effect on screen (the aura), so they only need a signature. The 9
named fields with rule effects (Necrovalley, Fusion Gate, …) get theirs with
event presentation later.

| Kit | Scope | Fields (variant) | Look |
|---|---|---|---|
| Waterline | per monster | Umi (0), Umiiruka (1) | Monsters wade: shin-high water ring, ripples, foam |
| GroundCover | per monster | Sogen (0), Forest (1), Gaia Power (2) | Meadow grass / fern undergrowth / roots breaking the ground |
| Outcrops | per monster | Wasteland (0), Mountain (1), Molten Destruction (2) | Dusty boulders / crags / lava-cracked rock |
| Shroud | per monster | Yami (0) | Shadow tendrils curling up from the ground |
| Arcs | street | Mystic Plasma Zone (0) | Plasma lightning flashing high over the street |
| Shafts | street | Luminous Spark (0) | Light shafts slanting down, fading before the ground |
| Updraft | per monster | Rising Air Current (0) | Wind ribbons spiralling around the feet |

**Kit contract** (enforced by review and, where static, by the guard):

- Per-monster pieces stay within 0.75 × and below 0.6 × the monster's scale.
  They sit around the monster, never under the card, so the terrain pad,
  ownership ring and aura stay readable. Street pieces stay above 1.3 m × street
  scale. Nothing is drawn across the open aisle floor.
- Every alpha is multiplied by the sweep/dissolve level and each monster's
  presence, so set pieces ripple in with the sweep and never pop.
- At most 2 draw calls and 4096 vertices, zero per-frame allocations, unscaled
  time, no physics, no `Find*`, materials only from the base class helpers.
- Colours come from the row palette, and looks vary only by variant and scale
  (the guard fails a kit that names a card). Face-down monsters report no aura,
  so kits cannot leak a set monster's Type.

Monster positions come from the terrain pads (`ArFieldTerrainPad.CollectAnchors`),
so a kit never touches cards or the engine.

## 8. Deliberately not done

- No scenery-scale cliffs, trees or towers across the street. The walls
  already carry the scenery from the real illustration. Signature kits
  decorate only around monsters or high over the street (§7).
- No wash on the hand or the disk. Those are physical cards, not projections.
- No change to the shared monster crop (`ArtworkNormRect`). It has the same frame
  bleed, but it affects every holo, so it is a separate change.
- Field Spell sounds (the anime's "whoomph") are not wired. `WrldzAudio` would
  be the place.
