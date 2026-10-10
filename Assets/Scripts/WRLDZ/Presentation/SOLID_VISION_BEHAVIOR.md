# Solid Vision — hologram behavior canon (Duel Monsters era)

**Audience:** Art Bot 1/2, Coder Bots 1–3, QC (roles per `Docs/PRODUCTION_ENGINEERING_GUIDE.md`).
**Scope:** how holograms *behave* in the arena — arm, materialize, attack, impact, destroy,
activate — for phone AR now and lenses later. Rules never live here (`ANIME_TCG_ENGINE.md`).
Everything below is presentation, driven only by `DuelEngine` events; it never invents game state.

## Sources

- From public wiki summaries read 2026-09-20 in the in-app browser: Yugipedia
  *Solid Vision*, *Duel Disk*, *Battle Box*, *Dueling Arena*, *Yu-Gi-Oh! Episode 054*; Fandom
  *Solid Vision*.
- §4 beat grammar is described from the show itself. It is **"described", not "locked"**, until it
  is checked against reference frames (§8). Do not treat §4 as a Stage 1 contract before that.

## 1. What the fiction says the tech is

| Fact (sourced) | What it means for ARGON |
|---|---|
| Cards carry microchips; a compatible device (Battle Box, Dueling Arena, Duel Disk, Duel Runner) reads the chip and projects the hologram. | The **card seating in a disk slot** is the trigger. The hologram belongs to the card, not the zone — it appears where the card's zone maps into the arena and dies when the card leaves. |
| Kaiba prototyped Solid Vision for Kaiba Land; the Duel Disk exists to put distance between duelists and (V1) to screen faces with holographic cards. | Distance is canon. The arena is *between* the duelists, never a tabletop in front of one of them. |
| Takahashi's stated inspiration is the *Dejarik* board from *Star Wars IV*. | The original texture: self-lit, slightly translucent figures with a visible shimmer. Battle City scales that to life size; it does not make them opaque or photoreal. |
| Pre-ARC-V holograms have no mass (fans noticed the explosive force anyway); ARC-V "Real Solid Vision" adds mass. | ARGON canon is **Battle City: light, not mass**. Force is *staged* (flash, shake, push-back), never simulated as physics on the duelist. Shadow Game / Umbrax surfaces may "feel real" as story tone. |
| Death-T Battle Boxes subject the loser to a virtual "Experience of Death". | Origin of the Shadow Game visual tone: violet-black, monsters lunge *at the duelist*, silence between shouts. Umbrax rifts only — never the daily overworld. |

## 2. Three device eras (map to ARGON seasons)

| Era | Device (sourced) | Hologram staging | ARGON |
|---|---|---|---|
| Death-T / manga Duelist Kingdom | **Battle Box** — clear box whose walls project the monsters onto a life-size board below; Duelist Kingdom boxes reflect the terrain within 40 m; Pegasus's castle arena rises on a lift. | Table-scale board inside glass, players seated. | Season 1 *DK Mode* flavor (2000 LP). Not the first shell we build. |
| Duelist Kingdom (anime) | **Dueling Arena** — raised platforms, giant holograms of played cards, terrain Field Power Bonus; Duel Disk V1 is thrown like a yo-yo and screens the duelist's face with holographic cards. | Monsters over the arena table, larger than the players. | Season 1 fields; warm lamps (`anime-manga-visual.md` era palette). |
| **Battle City** | **Duel Disk V2** on the left forearm; the anime launches two projectors to the sides of the duel space (dropped by the finals); the manga projects straight from under the zones. Monsters stand *in the street* at life scale. | Everything we build for AR. | Default living-world look (GDD §1, §7.1). |

## 3. Duel Disk V2 — what the arm does (sourced)

Standby: the Graveyard is locked over the Deck. The M1-side half (SMALL BLADE: M1–M2, ST1–ST2)
starts tucked slightly into the round hub; the other half (BIG BLADE: M3–M5, ST3–ST5) starts on the
opposite side. Both stick out, slide toward each other and meet at the deck, then the joined plate
slides to the locked pose. The hub stays put. This mesh has no cut between M2 and M3, so M3 and ST3
ride the big blade. Owner restated 2026-10-02 (the older forearm-fold / meet-over-the-wrist wording was wrong). Five monster slots on the blade; a Spell/Trap slot directly under each monster slot on
the wearer's side; a button on the round hub activates a Set card; a Field Spell slot opens at the
blade tip; Deck, Graveyard and LP counter sit at the hub; a retractable cable plugs into an arena.

Code today: `ArDuelDiskRig` (deploy / retract), `ArDiskZone`, `ArDiskLpCounter`,
`ArDiskPhaseButtons`, `DiskFx` (`DiskFxEvent.*`). Layout matches GDD §7.1 exactly.

Beats to lock on the disk:

1. **Arm** (duel start) — halves swing out, LP lights 8000, then the projectors launch and land to
   either side of the arena. This is the *arena-spawn* beat. Today: `ArArenaStartFlash`.
   **Gap:** no projector objects; the arena just appears.
2. **Play a card** — card seats in the slot → slot lights → hologram materializes at the matching
   arena position (§4.1).
3. **Set** — face-down on the disk only; **no arena hologram on either side until activation**
   (owner rule 2026-09-23; `AR_DUEL_PRESENTATION.md` information rules).
4. **Activate Set** — hub button → the card lifts and turns face-up on the disk → arena shows the
   enlarged card (§4.4).
5. **Damage** — LP digits tick down, the disk shakes (`DiskFxEvent.DamageShake`), a direct hit
   pushes the duelist back (§4.5).
6. **Duel end** — the loser's holograms shatter or dissolve; disks retract.

## 4. Hologram beat grammar (arena) — described, pending frames

Each beat: what the show does → the hook we already have → the gap.

### 4.1 Materialize (Summon)

**Show:** card placed → a column of light / ground ring at the zone → the monster fades up inside
it from the feet with a scanline shimmer → snaps solid into a pose (roar / stance). Big monsters
(Blue-Eyes, the Gods) materialize slower with a wider ring and the camera pulls back. A face-down
Set shows nothing in the arena; a Flip Summon rotates the card face-up, then the monster erupts.

**Code:** `ArArenaCombatFx.PlayHoloMaterialize` (ground rings + light column, scaled by
`ArPlaymatLayout.LiveHoloScale`) · `ArAnimeSequenceDirector.PlaySummonSequence` (camera push) ·
`CombatAnimTimings.ForSummon` (5.0–7.0 s — this *is* the Trap Hole reaction window, so the
materialize must stay readable for its whole length) · `ArArenaCardVisual.PulseReveal`, flip path
`PlayBattleFlipThenDestroy`.

**Attribute light (2026-09-24):** a face-up monster materializes in its own colour —
`PlayHoloMaterialize(…, CardDef)` tints the column, ground light and motes by Attribute (DARK violet ·
LIGHT gold · WATER deep blue, kept off the player's cyan · FIRE orange · EARTH ochre · WIND green ·
DIVINE pale gold; a cinematics-table tint overrides) and sizes it by Level: w = (Level − 1) / 7,
length × (1 + 0.6w), column × (0.85 + 0.45w), ring × (0.85 + 0.5w). The owner's side colour stays
as a thin outer ring. A face-down card never passes its def (its Attribute would leak), so Sets and
Spells/Traps keep the side-coloured look.

**Gap:** materialize length follows Level only, not the whole `ForSummon` window; no "snap solid"
pose frame for mesh monsters.

### 4.2 Attack

**Show:** declaration — monster name plus the attack name ("White Lightning", "Dark Magic Attack")
→ charge: the monster tenses and glows for a beat → travel: beam / projectile / lunge → impact:
flash and screen shake → result. The defender may interrupt any moment before impact (Trap).

**Code:** `ActiveCombatPresentation` + `CombatAnimTimings.ForAttack` (impact 5.0–8.0 s by
level/ATK) · `PlayDefendShield` on a DEF target. Reactions race this clock (`ANIME_TCG_ENGINE.md`) —
the rules path is already right.

**Engine fact that shapes the look:** the engine resolves an attack *in the same call* as the
declaration when the defender has no legal response, and the AI answers its own windows at once. A
windup exists only while the **player** holds a response window (5 s decision, shorter than the
impact clock), so the hit must fire when the engine *resolves* the attack, never on the impact clock.

**Attack sequence (2026-09-24, `ArAnimeSequenceDirector`):**

1. **Declare** — the Attack presentation opens (its "X attacks Y." log line). Mr. Referobot calls the
   attack by name, e.g. "Blue-Eyes White Dragon — White Lightning!" (`SolidVisionCinematics.AttackCall`).
   He is the voice of the Spell/Trap name call-outs too; `ArEffectCallout` is retired. Only sourced
   names are used, so most monsters get no call-out. The whole attack's look is fixed here from the
   attacker: archetype (table, else race), tint (table, else Attribute) and weight (Level + ATK).
2. **Charge** — every frame while the presentation lives: the attacker's aura ramps over the decision
   window, the defender braces once, and an archetype charge tell pulses every 0.3 s
   (`ArArenaCombatFx.PlayChargeTell`). Beam = the old sparse beam, now tinted; Blast = a growing orb;
   Lunge = light tightening under the feet; Projectile = muzzle glints; Swarm = motes drawn in;
   Surge = ripples running out.
3. **Release** — when the presentation closes with `ImpactResolved` (pass, no response, or a
   non-negating reaction). A negated attack, a trap cut, a cancel / fizzle / replay line, or a Battle
   Phase that ended sends nothing. The release (`PlayAttackRelease`) is short because the battle result
   shatters at once. It waits behind any Spell/Trap result still on screen, ends in a tinted arrival
   burst on the real target (the old mid-field impact stands down for it), and shakes the disks.

**Archetype by race:**

| Archetype | Races |
|---|---|
| *Beam* | Dragon, Thunder, Wyrm, Divine-Beast |
| *Blast* | Spellcaster, Fiend, Fairy, Zombie, Psychic |
| *Lunge* | Warrior, Beast, Beast-Warrior, Dinosaur, Winged Beast, Reptile |
| *Projectile* | Machine, Rock, Pyro |
| *Swarm* | Insect, Plant |
| *Surge* | Aqua, Fish, Sea Serpent |

A race with no archetype falls back to Beam; `Tools/solid_vision_cinematics_check.py` fails if any
cards_db race is unmapped.

**Gap:**
- The table's archetype and tint overrides are provisional until checked against frames (§8).
- The releases are built from primitives, with no per-card VFX.
- The shatter is not held for the release, so travel stays ≤ 0.3 s.

### 4.3 Destruction

**Show:** the struck monster freezes for a frame → fractures into glass-like shards or bursts into
light that falls and fades. A bigger overwhelm makes a bigger burst. Effect destruction (Raigeki)
is the strike first, then the same shatter.

**Code:** `CardShatterPresentation` (8–56 pieces by overwhelm up to 8000; 12 for effects) →
`ArCardShatterFx` (card-face shards) · `ArArenaCardVisual.PlayHitFlash`.

**Gap:** the shatter is of the *card plane*. GLB monsters need a mesh shatter or an emissive
burn-out dissolve. No freeze frame before the break.

### 4.4 Spell / Trap activation

**Show:** the card rises off the disk and turns face-up; a giant card image hangs in the arena
long enough to read; then the effect itself plays (Raigeki bolt, Trap Hole pit, Mirror Force
wall); one-shots fade to the Graveyard, Continuous / Field / Equip stay standing.

**Code:** `SpellActivationPresentation` (all timing constants) + `ActivationBeat` (the presentation-only
result gate, same file) · `ArArenaCombatFx.PlaySpellCast` plus card-named looks (Trap Hole pitfall,
Raigeki bolt). Lingering cards settle upright over their zone.

**Beat order (2026-09-23, owner report "Spells/traps do not stay revealed long enough").** The engine
still resolves synchronously (legality is never delayed); only the *visuals* of the results wait:

1. **Reveal** — disk ejects + flips the card (art at 0.90 s); arena waits `ArenaStartDelaySeconds`
   0.90 s, rises `RiseSeconds` 1.10 s, art readable at ≈ **1.39 s** (`ArtReadableDelaySeconds`; the
   visual reports the real moment). Name banner (SPELL / TRAP · name · YOU / OPPONENT) from t0.
2. **Read before result** — `ReadBeforeResultSeconds` 1.4 s own / `OpponentReadBeforeResultSeconds` 1.8 s.
3. **Result** at `ResultAt` (≈ 2.79 s own / 3.19 s opp): monster / S-T shatters and removals, LP
   counters (disk, arena, HUD), drawn cards, revived monsters, flips (Swords), GY / deck counts, the
   2D field, impact FX — all held until here, then shown while the card is still up.
4. **Read after result** — `ReadAfterResultSeconds` 2.0 s own / `OpponentReadAfterResultSeconds` 2.8 s,
   and the total art-readable window is never below `MinHoverSeconds` 4.5 s / `OpponentReadHoverSeconds`
   6.5 s → fade at ≈ 5.89 s own / 7.89 s opp (disk toaster flies to the GY at the same moment).
5. **Exit** — one-shot fades `FadeToGySeconds` 0.75 s (beat end ≈ 6.64 s / 8.64 s); lingering card
   settles `SettleSeconds` 0.45 s and becomes an ordinary face-up S/T (a later MST shatters it).

Chains: each link is revealed when activated; links resolve in reverse order, each with its own result
beat, `ChainLinkResultGapSeconds` 1.5 s after the previous one (removals are tagged to the link that
was resolving when they were queued). A card still waiting on the field (chain window, target pick)
stays presented up to `UnresolvedHoverCapSeconds` 12 s. Every beat is hard-capped at
`BeatHardCapSeconds` 20 s. The AI (pacer) and the player's phase / turn advance (queued) and new
attack declarations wait for the beat end; response windows are never blocked.

**Gap:** the named-look table must grow with the compiled atom families. This is the per-atom FX
map: `DestroyAllOppSpellTraps` = Dark Magic Attack sweep · `PlaceTargetOnTopOfDeck` = card flies
to the Deck · `StartOfDamageStep` destroy = Sasuke's slash *before* the target flips ·
`AddFromDeckToHand` = card streaks out of the Deck well.

### 4.5 Direct attack / LP damage

**Show:** the attack crosses the empty field and hits the duelist, who staggers or is thrown back;
the LP counter drops; heavy hits knock them down. Shadow Games make it hurt.

**Code:** direct attacks aim at the opposing disk (`ArAnimeSequenceDirector.TryAttackEndpoints`) ·
`DiskFxEvent.DamageShake` · `ArFloatingLpCallout`.

**Gap:** the duelist flinch. On phone AR the camera *is* the duelist — the honest equivalent is a
short camera kick + vignette flash at impact. On lenses: flash only, no forced motion (comfort).

### 4.6 Position change / Flip / Tribute

**Show:** Defense = the monster kneels or braces behind a shield glint. Tribute = the monster
dissolves *upward* into light that flows into the summoned monster — Obelisk absorbing its
tributes in episode 54 is the canonical frame.

**Defense rule (owner, 2026-09-23 — "its card should be visible underneath the artwork hologram in
the rotated position"; Battle City reference):**

- A face-up **Defense** monster stands **upright but braced** over its **card**, which lies flat on
  the street turned **sideways (landscape)**, face-up and glowing in the controller accent (cyan
  you / pink opponent). The card is the full card (frame + art), turned 90° the same way the disk
  card turns for Defense (text reads upright to its controller when turned back). The artwork is
  never rolled onto its side.
- Braced = 2D art height × 0.88 with its feet on the card, leaning back 6°, 20% cool accent tint;
  a shell GLB plays `ShellPoser.State.Defense`. A thin curved **shield glint** pane stands at the
  card's edge toward the opponent and pulses gently.
- **Attack** position stands with **no card** under it — except for the summon beat: on arrival in
  Attack (Normal / Tribute / Special / Flip Summon) the full card lies flat in **portrait** under
  the materializing hologram (after the disk seat), holds ≈1.2 s, then fades over ≈0.5 s.
- ATK → DEF: the card fades in while turning portrait → landscape (0.35 s ease-out) and the art
  settles braced (0.3 s) + defend-shield burst. DEF → ATK: the card turns back and fades while the
  art rises. Spell/Trap-caused changes and summons wait for the activation result gate.
- **Battle flip** (face-down attacked): the set card shows as its sideways card back while it is the
  attack target, flips face-up at the damage step, lies down as the Defense card and the monster
  rises braced out of it (end state = Defense, never Attack art). If it is destroyed: flip → braced
  over the card → shatter. Face-down monsters otherwise stay concealed (no hologram), and a
  face-down monster leaving without a battle reveal (bounce, Change of Heart, tribute, face-down
  banish) is removed quietly — no face leak.

**Code:** `ArArenaCardVisual` (`SetDefenseDisplay`, `BeginSummonCardPlate`, `EnsureDefendShield`
glint, `BattleFlipCo`, `RevealAsAttackTarget`) · `ArAnimeSequenceDirector.TrackFaceDownAttackTarget`
· `ArArenaHologramManager.RemoveNow` · `PlayDefendShield` · tribute = a plain shatter.

**Gap:** tribute-as-absorption (light flows into the summon), distinct from destruction.

## 5. Scale and MR rules

- **Battle City is life-size.** Human ≈ 1.7 m; Level 1–4 ≈ 1.5–2.5 m; Level 5–6 ≈ 3–4 m;
  Level 7–8 ≈ 4–8 m; the Gods tower over buildings. Phone AR degrades *fidelity*, never *scale*
  (`PRODUCT_VISION.md`). `ArPlaymatLayout.LiveHoloScale` is the single knob.
- Holograms are self-lit, slightly translucent, unlit cel with exposure fit
  (`ArAnimePresentation`), rim-glowed cyan (you) / magenta (opponent) (`argon-visual-map.md`).
- Comfort: no camera motion the user did not start on lenses (`UserOwnsCamera` guard exists);
  impact on lenses = flash + light kick only. Phone may add the camera kick.
- No lens-only powers. Every beat here has a phone rendering.

## 6. Data model for per-card cinematics

One table (not a file per card): `StreamingAssets/WRLDZ/Cinematics/cinematics_v1.json`, schema
`wrldz-cinematics/1`, read by `Presentation/SolidVisionCinematics.cs` through `StreamingFiles`
(Android-safe). `DuelEngine` never sees it.

```json
{ "id": 89631139, "name": "Blue-Eyes White Dragon", "attack": "White Lightning",
  "attackOriginal": "Burst Stream of Destruction", "tint": "#D8F4FF",
  "source": "Yugipedia Card Appearances:Blue-Eyes White Dragon" }
```

- `attack` is the English dub name; `attackOriginal` renders the Japanese one. The call-out uses the
  dub, else the original.
- Names are **sourced, never invented**. A card with no recorded attack name has no entry and gets no
  call-out.
- `archetype` and `tint` are optional overrides of the race and Attribute fallbacks.
- `python3 Tools/solid_vision_cinematics_check.py` checks:
  - every id is a cards_db monster whose name matches;
  - archetypes are valid and tints are `#RRGGBB`;
  - every cards_db race has an archetype.
- Later fields (materialize style, impact weight, roar) join the entries when their FX exist.

Ownership: Lore Bot owns name accuracy; Art Bot 2 owns archetype and tint; QC checks them against the
reference frames.

## 7. Priority order (Stage 5 tickets)

1. ~~Named attack callouts (data only)~~ — landed 2026-09-24. 25 sourced names are in the table, and
   Mr. Referobot calls them at the declaration (§4.2).
2. ~~Attribute-colored materialize rings~~ — landed 2026-09-24, sized by Level (§4.1). The duration is
   not yet the whole `ForSummon` window.
3. ~~Attack archetype table (six archetypes)~~ — landed 2026-09-24: charge tells and releases (§4.2).
   Pending QC against frames.
4. Tribute-as-absorption flow.
5. Projector launch at duel start (the arena-spawn beat).
6. Mesh dissolve for GLB monsters (three exist: Dark Magician, Summoned Skull, Flame Swordsman).
7. Camera kick on a direct hit (phone) / flash-only (lenses).

## 8. Reference frames needed (owner: Great SaiyaDave)

Wiki prose gives the *what*; frames give timing, silhouette and camera. Drop screenshots in
`Tools/WRLDZ/external_refs/frames/` (outside `Assets/`) and name the five to ten
duels that are the gold standard:

- Duel Disk arming + the two projectors launching (Battle City)
- a Normal Summon materializing, and a Blue-Eyes Special Summon
- "White Lightning" start → travel → impact; "Dark Magic Attack"
- a monster shattering; a direct attack landing on the duelist
- a Spell and a Trap activating (the enlarged card)
- Obelisk absorbing its tributes (episode 54)
- one Duelist Kingdom arena duel for the era contrast

Until those land, §4 stays "described". When they land, §4 becomes the Stage 1 contract for the
dynamics work and the reference the art bots are QC'd against.
