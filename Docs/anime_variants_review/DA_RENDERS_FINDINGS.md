# DeviantArt character renders — findings & attribution

**Date:** 2026-10-15
**Status:** live. The Duelist Kingdom (Season 1) renders below are now the in-game sprites for
their characters.

---

## Source

All renders come from one artist:

- **GravekeepersBrewer** — <https://www.deviantart.com/gravekeepersbrewer>
- Gallery used: **“Duelist Kingdom Yu-Gi-Oh Character Renders”**
  (<https://www.deviantart.com/gravekeepersbrewer/gallery/102195510>), 45 deviations.
- The same account has one gallery per era (Season 0, Legendary Heroes, Battle City, Virtual World,
  Waking the Dragons, Grand Championship, Millennium World, Capsule Monsters, Pyramid of Light,
  The Falsebound Kingdom, Duelist of the Roses, …), so the Battle City / later-outfit pass can reuse
  this exact pipeline.

> **Credit:** the art is by GravekeepersBrewer on DeviantArt. The underlying characters are
> © Kazuki Takahashi / Konami / Shueisha; these are fan renders of that art. Keep this file with the
> project as the attribution record.

## Why this source (and what it replaces)

The earlier pass searched DeviantArt by keyword and produced a pile of mismatched 600 px images
(several Joey hits were actually titled “Mai Jonouchi Render”; Ryo Bakura only turned up as an opaque
RGB scan). Those are dropped.

This account wins on three objective points:

1. **Correct character, by construction** — each deviation is titled with the character name and the
   gallery is era-specific.
2. **Full resolution** — the bare original (`…/f/<folder>/<file>.png?token=…`, no `/v1/` transform)
   is ~930–1120 × 1400–1700, not a 600 px cap.
3. **Clean cutouts** — every render is a front-facing, full-body figure composited on **pure black**,
   which keys to real alpha with no halo (verified: mean brightness just outside the matte is 2–13/255).

## Pipeline

`research/fetch_da_gallery.py` → `research/process_da_gallery.py` → `research/build_normal_depth.py`

1. **Fetch** — the web gallery page only shows the first 11 items; the RSS mirror
   (`backend.deviantart.com/rss.xml?q=gallery:gravekeepersbrewer/<id>&type=deviation`) returns the whole
   gallery. For each item the deviation page is opened and the full-resolution original downloaded to
   `research/anime_variants/da_gallery/<era>/`.
2. **Key** — flood-fill the near-black region connected to the image border to alpha (interior
   near-black is kept, so a dark costume is not punched out), then erode the matte 1 px to drop the
   black fringe left where the figure meets the background.
3. **Place** — crop to the subject, scale to ≤92 % of the 1024 canvas, centre horizontally, feet 8 %
   up from the bottom (the project’s sprite convention).
4. **Write** — the 12 contract files per set (4 angles + 3 expressions + 5 gestures; all identical,
   the billboard always faces the camera) into
   `Assets/Resources/WRLDZ/AnimeCharacters/<id>_da/`, plus the `<id>_normal.png` form map.

## Sets built

| Set | Character | Source deviation | Original px |
|-----|-----------|------------------|-------------|
| `yugi_da` | Yugi Muto | [Yugi Muto](https://www.deviantart.com/gravekeepersbrewer/art/Yugi-Muto-1347114762) | 1024×1536 |
| `yami_yugi_da` | Yami Yugi | [Yami Yugi](https://www.deviantart.com/gravekeepersbrewer/art/Yami-Yugi-1345109914) | 929×1693 |
| `kaiba_da` | Seto Kaiba | [Seto Kaiba](https://www.deviantart.com/gravekeepersbrewer/art/Seto-Kaiba-1345721407) | 1023×1537 |
| `joey_da` | Joey Wheeler | [Joey Wheeler](https://www.deviantart.com/gravekeepersbrewer/art/Joey-Wheeler-1345123124) | 987×1594 |
| `mai_da` | Mai Valentine | [Mai Valentine](https://www.deviantart.com/gravekeepersbrewer/art/Mai-Valentine-1345494816) | 925×1701 |
| `tea_da` | Téa Gardner | [Tea Gardner](https://www.deviantart.com/gravekeepersbrewer/art/Tea-Gardner-1345770006) | 1023×1537 |
| `tristan_da` | Tristan Taylor | [Tristan Taylor](https://www.deviantart.com/gravekeepersbrewer/art/Tristan-Taylor-1347577645) | 1023×1537 |
| `bakura_da` | Ryo Bakura | [Bakura Ryou](https://www.deviantart.com/gravekeepersbrewer/art/Bakura-Ryou-1349601936) | 934×1683 |
| `yami_bakura_da` | Yami Bakura | [Yami Bakura](https://www.deviantart.com/gravekeepersbrewer/art/Yami-Bakura-1345709311) | 941×1672 |
| `pegasus_da` | Maximillion Pegasus | [Maximillion Pegasus](https://www.deviantart.com/gravekeepersbrewer/art/Maximillion-Pegasus-1345132332) | 1024×1536 |
| `weevil_da` | Weevil Underwood | [Weevil Underwood](https://www.deviantart.com/gravekeepersbrewer/art/Weevil-Underwood-1345505057) | 1024×1536 |
| `rex_da` | Rex Raptor | [Rex Raptor](https://www.deviantart.com/gravekeepersbrewer/art/Rex-Raptor-1345506188) | 1024×1537 |
| `mako_da` | Mako Tsunami | [Mako Tsunami](https://www.deviantart.com/gravekeepersbrewer/art/Mako-Tsunami-1345707389) | 936×1680 |
| `bandit_keith_da` | Bandit Keith | [Bandit Keith](https://www.deviantart.com/gravekeepersbrewer/art/Bandit-Keith-1345503434) | 930×1691 |
| `mokuba_da` | Mokuba Kaiba | [Mokuba Kaiba](https://www.deviantart.com/gravekeepersbrewer/art/Mokuba-Kaiba-1346450362) | 1122×1402 |

## How it reaches the game

`CharacterLooks.cs` is the only live look selector. `CharacterLooks.Match(name)` returns the first row
whose `Match` string is contained in the NPC name (longest match wins; ties → earliest row). Each
character’s name-reachable row now points its `CharacterId` at the DK set, so the name-match alone
lights up the DK render:

| Look row | Match | CharacterId now |
|----------|-------|-----------------|
| `yugi_dk` | `yugi` | `yugi_da` |
| `yami_yugi_dk` | `yami yugi` | `yami_yugi_da` |
| `kaiba_dk` | `kaiba` | `kaiba_da` |
| `joey_bc` | `joey` | `joey_da` |
| `mai_bc` | `mai` | `mai_da` |
| `tea_bc` | `tea` | `tea_da` |
| `tristan_bc` | `tristan` | `tristan_da` |
| `bakura_bc` | `bakura` | `bakura_da` |
| `yami_bakura` | `yami bakura` | `yami_bakura_da` |
| `pegasus_dk` | `pegasus` | `pegasus_da` |
| `weevil_bc` | `weevil` | `weevil_da` |
| `rex_bc` | `rex` | `rex_da` |
| `mako_dk` | `mako` | `mako_da` |
| `keith_dk` | `keith` | `bandit_keith_da` |
| `mokuba_bc` | `mokuba` | `mokuba_da` |

`AnimeCharacterCatalog.cs` registers the `*_da` sets so `AnimeCharacterActor` can find them.

**Two pre-existing issues fixed as a side effect:**

- **Ryo Bakura** used to render as **Yami Bakura** (the catalog `bakura` set held Yami Bakura art).
  `bakura_bc` now points at `bakura_da`, the DK Ryo Bakura render.
- **Yugi Muto** had **no** anime set at all and fell back to the modular avatar. `yugi_dk` now points
  at `yugi_da`.

## Reverting one character

Change its `characterId` back in `CharacterLooks.cs` — the previous value is listed in the header
comment of that file, e.g. `tea_bc` `"tea_da"` → `"tea"`, `yugi_dk` `"yugi_da"` → `""` (modular).
Marik, Ishizu and Odion are Battle City only and were left on their existing sets.

## Review sheets (for visual sign-off)

- `Docs/anime_variants_review/da_pick/DK_render_all.png` — every DK render with names.
- `Docs/anime_variants_review/da_pick/DK_render_vs_official.png` — previous look beside the new render.

## Coverage & next

- Covered by this pass: 15 sets (the 13 matching the catalog’s 16 plus Yugi Muto and Yami Bakura).
- **Not covered:** Marik, Ishizu, Odion (Battle City only — not in the DK gallery). They keep their
  existing sets.
- **Ready to wire in a follow-up** (downloaded in the same gallery, not yet built as sets): Bonz,
  Duke Devlin, PaniK, Para, Dox, Serenity Wheeler, Rebecca Hawkins, Ghost Kaiba, Zygor, Shadi,
  Arthur Hawkins, Cecelia Pegasus, Hobson, Sam, Sid, Yugi’s Mother — plus the DK **outfit variants**
  “Joey Wheeler (Flame Swordsman)”, “Tristan Taylor (Cyber Commander)”, “Yugi Muto (Dark Magician)”,
  “Téa Gardner (Magician of Faith)”, “Bakura Ryou (Change of Heart)”, “Bakura Ryou (Lady of Faith)”,
  “Yami Bakura (Lady of Faith)”.
- **Next eras:** the same account has one gallery per season, so Battle City (and later) outfit
  variants reuse `fetch_da_gallery.py` unchanged.
