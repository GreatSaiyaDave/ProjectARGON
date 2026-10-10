# Duel arena FX map — what each compiled card has to look like

A card is not finished when its clauses compile. If a card is Implemented and the arena has no look for
what it does, the player sees a card that resolves silently. **Every card group in a batch ships its FX
wiring in the same change** — that is the rule this file exists to make checkable.

Companion to `ENGINE_MAP.md` (which tracks rules-engine subsystems). This one tracks the presentation side.

## The seam

The engine never references `Presentation/`, and it must not: `Tools/HeadlessEngine` compiles only
`Data/**`, `Duel/**` and a few listed `Core/` files, so a duel-core file that touches a `MonoBehaviour`
breaks the batch's own gate. The seam is three small pieces:

| Piece | File | Role |
|-------|------|------|
| `SummonKind` | `Duel/Rules/EffectTypes.cs` | The procedure, already known by the engine at every Summon |
| `ActiveCombatPresentation.Procedure` | `Duel/CombatAnimTimings.cs` | Carries the procedure to the arena with the appear |
| `ArArenaCombatFx.PlaySummonProcedure` | `Presentation/ArInteraction/ArArenaCombatFx.cs` | Maps a procedure to its hologram |
| `ArAnimeSequenceDirector.WatchSummonAppear` | `Presentation/ArInteraction/ArAnimeSequenceDirector.cs` | Plays it once per appear |

`OpenSummonResponseOrContinue` tags the appear (`NormalSummon` / `NormalSet` / `TributeSummon` /
`TributeSet` / `FlipSummon` / `SpecialSummon`), and `TryOpenSummonNegationWindow` tags the ones that open a
summon-negation window. `CombatAnimTimings.MotionLineForSummon` gives each procedure its own motion line,
so the duel log reads "Fusion Summon — X fuses into one!" rather than the generic materialize line.

### Do not detect summons from the log

`OnLog` used to fire summon FX on the text `"normal summon"`, `"tribute summon"`, `"special summon"`,
`"flip summon"` and `"summons "`. The engine's Extra Deck procedures log `"Fusion Summon! {name}!"`,
`"Ritual Summon"`, `"Synchro Summon"` and so on, which none of those patterns match — so **every Fusion,
Ritual, Synchro, Xyz, Pendulum and Link Summon landed with no summon FX at all**, and the string match
could not tell the procedures apart even if it had. Read `ActivePresentation.Procedure` instead. The log
match is still there for the camera flourish; the FX come from the procedure.

### One hologram, not two

The arena card visual (`ArArenaCardVisual`) plays `PlayHoloMaterialize` itself as a card reaches its zone,
so the director calls `PlaySummonProcedure(..., arrive: false)`: the procedure plays only its lead-in and
the card visual owns the arrival. Call `arrive: true` only when nothing else will materialize the card.

## Summon procedure coverage

Landing = the card visual's own materialize. Lead-in = this table.

| `SummonKind` | Lead-in | Read |
|--------------|---------|------|
| `NormalSummon` | — (arrival only) | The plain materialize is correct here |
| `NormalSet`, `TributeSet` | `SetPinRoutine` | Deliberately quiet and low; a Set never reveals |
| `TributeSummon` | `TributeAshRoutine` | The spent Tributes burn out below the arrival |
| `FlipSummon` | `FlipPlateRoutine` | The face-down card turns over with a light wipe |
| `SpecialSummon` | `SummonPortalRoutine` | A ring opens and the monster steps through |
| `FusionSummon` | `FusionFuseRoutine` | The Fusion Materials pull into one knot, then release |
| `RitualSummon` | `RitualCircleRoutine` | The circle is drawn, closes, and stands the monster up |
| `SynchroSummon` | `OrbitAlignRoutine(2, counterSpin)` | Two orbits counter-spin into alignment |
| `XyzSummon` | `OrbitAlignRoutine(3, counterSpin: false)` | Three layers nest downward and settle |
| `PendulumSummon` | `PendulumSwingRoutine` | Two scales swing in and level out |
| `LinkSummon` | `LinkJoinRoutine` | Separate nodes drawn together by the links between them |

A face-up monster's lead-in takes its Attribute from `SolidVisionCinematics.TintFor(def)`; a Set never
passes its `Def` or its Attribute would leak before the reveal.

## Effect-family coverage (what a card *does*, not how it arrives)

This is the half that still has gaps. `ArArenaCombatFx` covers the arrival and battle; these families are
either covered by a named look, covered only generically, or not covered at all.

| Family | Look | State |
|--------|------|-------|
| Trap Hole | `PitfallRoutine` | Named (card-id switch) |
| Raigeki | `RaigekiRoutine` | Named |
| Dark Hole | `DarkHoleRoutine` | Named |
| Mirror Force | `MirrorForceRoutine` | Named |
| Swords of Revealing Light | `SwordsRoutine` | Named |
| Reborn | `RebornRoutine` | Named |
| Spell/Trap activation | `SpellCastRoutine` | Generic ring; `PlaySpellCast` takes a `cardId` for named looks |
| Battle impact / defend | `ImpactRoutine`, `DefendRoutine` | Covered |
| Attack archetypes | Beam / Blast / Lunge / Projectile / Swarm / Surge | Covered |
| Chain link seating | — | **Gap** — v163 seats real chain links; the arena has no link beat |
| Negate / activate / set aside | — | **Gap** — spells resolve silently |
| Tribute cost paid | `TributeAshRoutine` (Summon only) | **Gap** for a tribute paid by other effects |
| Destroy / banish / send | `ImpactRoutine` only | **Gap** — a banish should not read as a destroy |
| Draw / mill / search | — | **Gap** — the deck does not move |
| LP change | — | **Gap** — no gauge beat on an orb pulse |
| Standby-phase growth | — | **Gap** |
| Spell Counter placement / spend | Counter pips on the hologram (`ArArenaCardVisual`, from `CardInstance.Counters`) | **Partial** — v166: the count is visible (it was hardcoded 0, and monsters were skipped). No arrival/spend beat yet; the batch report records that as acceptable generic |
| Union equip / unequip | SpellCast generic + SpecialSummon portal | **Gap** — v167: the Union leaving the Monster Zone for a Spell/Trap Zone has no unique look; unequip SS uses existing `SpecialSummon`. Acceptable generic; recorded in `Docs/CURRICULUM_BATCH_v167-union.md` |

A card group that touches a **Gap** row either fills the row or records in its batch report why the look
is acceptable generic for now. Filling a row once fixes every card that uses it, which is why groups are
grouped by shape in the first place.

## Per-group checklist

1. Does the group introduce a new `SummonKind` path, or use one for the first time in the arena?
   → the table above already covers it; no new FX needed.
2. Does it introduce a new effect family (a clause arm, a runtime, an atom)?
   → check the family table. Fill the row, or note the gap in the report.
3. New runtime that runs a ritual-like sequence (tribute, mill, excavate, shuffle, discard)?
   → it needs a beat; the generic arrival will not read as that effect.
4. Never add the FX to a file another session owns uncommitted work in
   (`ArArenaCardVisual.cs`, `DiskFx.cs`, `SpellActivationPresentation.cs`, the `ArDuel*Rig` family).
   Read them for the current design; add new behaviour to `ArArenaCombatFx.cs` or a new file, and hand the
   rest over as a diff.