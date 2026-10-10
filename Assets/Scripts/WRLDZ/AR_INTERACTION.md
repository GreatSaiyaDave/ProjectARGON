
## Opponent roster, silhouettes and tag teams (2026-09-30)

- **Canon duelists are animated silhouettes** until their real character models exist (owner direction, 2026-09-30):
  `AiDuelistLooks.For` sets `AvatarAppearance.silhouette` + `silhouetteHex` for every named DM duelist;
  `OverworldAvatar3d.ApplySilhouette` swaps body, hair and outfit to one flat `AvatarRecolor` material (dark tint,
  no shadow band, ink outline, `_RimColor` edge light in the duelist's hue) and hides the face decal. The hairstyle and
  outfit meshes still shape the outline, so Kaiba keeps his coat, the Paradox Brothers are bald (`hairStyleId "none"`).
  Flip `AiDuelistLooks.CanonSilhouettes` to draw the textured looks again.
- **Street duelists** (`Data/NpcDuelistRoster.cs`): 12 archetypes × name pools; a name is the whole identity
  (`TryGet(name)` → archetype, sex, LP band, 4000 / 8000 decks, `LookFor` → a full textured random look). The overworld
  field (`OverworldNpcField.Spawn`) names its walkers from `Generate(seed)`; `MapZoneService.PickStreetDeck` and
  `MakeStreetNpc` honour a roster name (deck + `OpponentDisplayName`, so the body and cut-in match the map tap).
- **Vs AI list** (`opponent_roster.json` v2 → `OpponentCatalog.Groups()`): one header per duelist with its decks under it
  (Yugi: *Grandpa's Deck* / *The Forbidden One*), a `members` tag team (Paradox Brothers = Para & Dox), and a
  `STREET DUELISTS · today` section from `NpcDuelistRoster.Today(8)` (`OpponentCatalog.ForStreet`).
- **Tag team on the stage**: `ArDuelMatchConfig.OpponentMembers` ("Para|Dox") makes `ArDuelInteractionSystem` spawn
  `OppDuelist2` with no disk beside the first (`ArOpponentDuelist.StandOffsetM`, `Partner`); both bind the engine, both
  play the deploy cinematic, and victory / defeat / start beats are shared through `Partner`.

## Duel Disk activation, frame by frame (2026-10-04, from the anime reference GIF's 12 frames)

`SpiritDuelerMechanics` no longer scales the blade halves out of the hub. The kit's halves are rigid parts on their own
connectors, and the activation is the anime's (full beat table and the GIF reading in
`Presentation/SPIRIT_DUELER_DISK.md`):

1. **Standby** – both halves lie alongside the round body on opposite sides, parallel to the forearm and pointing
   toward the elbow, their hub ends tucked into the body: the three-stage half (M3–M5 in this mesh) on the Graveyard
   side at the radial its seam will lock to (`BigStandbyDeg` = `PlaySeamDeg` ≈ 248°), the two-stage half (M1–M2 +
   field slot) mirrored on the counter-top side (`SmallStandbyDeg` ≈ 112°); radial angles in the kit's XZ plane.
2. **Extend** (0.06–0.22 of `MechOpen`) – the latch releases and both slide out along their connectors to full radius.
3. **Slide together and meet** (0.27–0.60) – the connectors carry both halves along the ring toward the deck
   (3.5 cm off the hub), the three-stage half leaning out as it goes and the two-stage half hanging by the wrist until
   mid-sweep (`BigTurn0` / `SmallTurn0`), and they arrive end to end beside the deck holder (`MeetSeamDeg` ≈ 172°,
   the hand side) as a shallow inverted V across the hand side of the hub. A click sound marks the meeting.
4. **Swing** (0.66–0.93) – the joined plate (central piece) swings round the hub, past the Graveyard, and slides in
   to the rim as it reaches play position (`CarrierStandbyYaw` ≈ 76° → 0, the kit's assembled pose), with the small
   anime overshoot.
5. **Lock** (0.95) – jolt, latch re-locks, zones light in a sweep M1 → M5 → S/T → field.

The sweep and hinge angles are derived from the kit geometry (`PlaySeamDeg` ≈ −112°), so a new kit needs only the
standby angles and radii. `DiskFx` gives the deploy `DeploySeconds` = 2.4 s (retract 1.1 s); the player showcase, the
rig's safety snap and the opponent cut-in all wait on that constant. Retract plays it backwards.
