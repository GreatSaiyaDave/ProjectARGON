# DK measurements: hair, Duelist Glove side, PaniK's glove, worn-disk mount (measurer, 2026-10-11)

Owner rule: "Do not make stuff up. Precision and accuracy are vital." Every value below cites frame + box + kept pixels, a
code line, or a mesh query. Anything that is a conversion or a judgement is marked **INFERRED** with the reasoning.

Read-only: no Unity, no git writes, no Meshy. Nothing outside this folder was written (proof in section 6).

## 0. Files and tools (all in this folder)

| Path | What |
|---|---|
| `tools/grab.py` | one 1280x720 frame: `ffmpeg -ss <t> -i "<…> Season 1 Episode <n>.mp4" -frames:v 1` |
| `tools/rank.py` | 1 fps decode of an episode, frames ranked by the largest blob in an HSV window (finds candidate shots) → `rank/`, `rank2/` |
| `tools/scan.py`, `tools/pair.py`, `tools/zoomgrid.py`, `tools/montage.py` | contact sheets, gridded frames, gridded native zooms, evidence stacks |
| `tools/meas.py` | **the sampler**: flat filter (every channel within 14 of all 8 neighbours: drops line art and anti-aliasing) → HSV class window → 1-D 2-means on V → per-channel **median** per tone (lit / shade, or one tone if the centres are < 0.07 apart or one cluster < 6 %), plus the 3 most common quantised colours ("modes") as a cross-check |
| `results.json` | every measurement (97): frame, box, HSV window, kept px, fills, modes, evidence image |
| `hair_summary.json` | per character / zone: the frames used, each fill, the per-channel median and the max channel spread |
| `ev/<label>.png` | evidence per measurement: crop (left) and classification (right: kept lit px in colour, kept shade px with cyan ticks, rejected px dimmed) |
| `sheets/` | contact sheets and zooms I judged from (named in the tables) |
| `frames/` | the frames I cut (name `eNN_<seconds>.png` = dub S1 episode NN at t seconds) |
| `disk/` | Task 4: `disk_extents.py/.json`, `armmount_profile.py`, `forearm_sections2.py/.json/_pts.npz` (Blender, read-only import), `clearance_fig.py/.png/.json` |
| `mtimes_before.txt`, `mtimes_after.txt` | the read-only proof |

Frame sources: the owner's evidence frames `~/argon-avatar/research/customization/evidence/dk_anime/`, the DK agents' frames
(`…/138746c4…/scratchpad/dk/*/` cited, frames, scan), the Animations session's `…/a69659a5…/scratchpad/anime/dkframes/`, and my own
cuts from `~/Downloads/YouTube Downloader/<title> ｜ Yu-Gi-Oh Season 1 Episode <n>.mp4` (1280x720, 23.976 fps). All times are dub S1.

**Lighting classes** (from looking at each frame): DAYLIGHT = outdoor sky / grass / sea; excluded from the recommended values:
S1E3 (ship interior / night), S1E12 (sunset), S1E13 (Shadow Realm), S1E15 (night), S1E16 t=279 (night), S1E17 t=1005 (fire-lit),
S1E18 (graveyard / forest), S1E25 (sunset), S1E26-27, S1E31-32 and S1E36 (castle interiors), S1E22 t=169 Téa (outlier, see 1.3).
Night-only characters (PaniK) are reported with that flag.

## 1. Task 1: DK anime hair colours

### 1.1 Table for CharacterLooks rows (recommended = per-channel median of the daylight lit fills listed)

`spread` = largest per-channel difference between the frames' fills (agreement). Pixel counts are kept flat class pixels in that tone.

| Character | Pattern | hairHex / zone colours (lit) | Shade (separate) | Frames (episode, t, box, lit px) | Spread | Confidence | Current row |
|---|---|---|---|---|---|---|---|
| Joey | solid | **#B69D3D** | #7E6A3E | E4 t=702 (229,0,900,380) #B59D3D 80,051; E6 t=962 (240,0,940,460) #B89C3C 177,239 (one tone); E17 t=181 (552,60,872,340) #B6A04C 21,474 | 0x0F | high | style colours |
| Kaiba | solid | **#765A3D** | #58442F (E22 only) | E22 t=547 (300,0,1120,392) #765B3D 134,162; t=1043 (292,0,932,392) #765A3D 100,784; t=1123 (180,0,1000,344) #765A3E 80,322; E8 t=1173 (180,0,1120,320) #735539 11,928; t=1178 (160,0,940,330) #725338 11,994 | 0x08 | high | style colours |
| Téa | solid | **#5D2F20** | #2A1B13 | E4 t=502 (300,100,560,400) #5A2E1F 15,232; E6 t=247 (152,0,632,704) #5A2D1C 56,438; E7 t=1151 (760,40,1120,560) #674032 38,827; E8 t=852 (380,0,940,680) #603020 87,604 | 0x13 | medium (E7 lighter) | style colours |
| Tristan | solid | **#62442D** | #3F2A18 | E4 t=502 (100,0,280,100) #583E29 3,631; E7 t=1151 (150,0,400,120) #685039 8,212; E7 t=674 (202,70,372,200) #674D3A 2,773; E8 t=377 (390,0,660,70) #60442C 2,132; E8 t=1085.2 (792,0,1060,120) #62432D 2,976 | 0x12 | medium (small samples) | #442222 (DL) |
| Bakura | solid | **#D7D8DF** | #AFA8B6 | E22 t=154 (full frame) #D8D9E0 72,963; t=169 (152,0,440,660) #D7D8DF 40,781; t=150 (full frame) #D6D7DE 846 | 0x02 | high (one episode, 3 shots) | style colours |
| Yami Bakura | solid | **#DADAE4** (= Bakura within 5) | #AFABBA | E40 t=275 (160,0,620,380) #DADAE4 6,547 / shade 33,034 (castle interior, lit flat). Not used: E13 t=131, t=287 Shadow Realm #CCCBD4 (116,579 / 96,202), shade #A29AAB | – | medium | style colours |
| Mokuba | solid | **#1E1E1E** (neutral near-black) | – (one tone) | E8 t=1080 (780,20,980,180) #1E1E1E 19,137; t=1083.6 (150,0,1120,280) #1F1F1F 182,859; t=924 (780,360,1000,560) #1E1E1E 27,756. Blue-violet highlight streaks #4C557E (t=1083.6 #4D5682 2,917; t=924 #4B5479 479) | 0x01 | high value, one episode | #222233 (DL) |
| Yugi | tri-colour | **root (core) #131212 · tip #9C1843 · fringe (bangs) #C0B84E** | bangs shade #836A2E | bangs: E4 t=358 #BEB84D 12,623; t=452 #C0B94D 11,459; t=502 #BFB74F 9,720; E7 t=111 #C7BD59 21,337 (spread 0x0C). tips: E4 t=358 (450,0,1050,300) #9C1743 9,792; t=452 #991843 4,043; t=502 #9B1743 7,866; E7 t=111 #A82559 17,823 (spread 0x16). core: E4 t=358 (500,0,1000,150) #121212 37,745; t=452 (440,0,1000,150) #131313 25,032; t=502 (640,0,1000,60) #131111 4,919 | see cells | high | style colours |
| Yami Yugi | tri-colour | **root #141411 · tip #AA1454 · fringe #CBC050** | bangs shade #8E7233 | bangs: E5 t=147 (140,0,1140,344) #D2C752 85,201; E6 t=733 #C2B84E 5,967; E7 t=955 #C8BD5A 29,709; E8 t=880 #CEC44E 7,764 (0x10). tips: E5 t=147 #B0135B 27,282; E6 t=733 #9A1544 5,983; E7 t=955 #A9265A 7,484; E8 t=880 #AB144F 19,292 (0x17). core: E5 t=147 (440,0,900,64) #161613 8,103; E8 t=880 (380,0,700,90) #12120F 7,411 | see cells | high | style colours |
| Weevil | solid | **#8ABAB7** | #507C76 | E4 t=506 (380,0,970,290) #87B6B4 27,437; t=637 (280,35,852,330) #87B7B5 41,903; t=900 (168,0,1010,260) #90BEB9 64,891; E5 t=287 (160,0,1120,250) #8EC6C1 22,780 | 0x10 | high | #88BBAA (DL) |
| Rex | two zones | **root (brown) #4D3020 · fringe (lavender) #8D7DB3** | brown dark #271C14 (mode, 807 px) | E4 t=350 only daylight: brown L box (380,60,480,520) #4E3222 1,155; R box (1010,60,1140,520) #4C2F1E 4,747; fringe (680,40,920,240) #8D7DB3 3,376. Sunset check (not used): E12 t=156 brown #5D3D26 16,144, fringe #9388B7 12,220 | 0x04 | **low**: one daylight frame | #5A2A6A (DL) |
| Mako | solid (+streaks) | **base #3C394C** (streaks #595E8F) | shadow #27252F | base: E7 t=329 (152,0,1112,312) #3C3A4C 57,033; E7 t=1013 (150,0,1100,344) #3C394C 110,896; E4 t=270 (560,0,820,160) #2D2740 20,662. streaks: E7 t=1013 #5E6696 18,363; E4 t=270 #545788 2,688 | 0x13 (E4 darker, more violet) | medium | #221133 (DL) |
| Mai | solid | **#C2BD6A** | #736C41 | E4 t=487 (600,0,1120,660) #BEBA6C 12,197; E6 t=478 (152,0,1012,704) #C3BD68 203,156; E6 t=1033 (192,0,712,600) #C2BD6A 19,892 | 0x05 | high | style colours |
| Keith | solid | **#B3AF56** | #747045 | E32 t=708 (392,100,872,300) #B5AF56 6,371 (castle interior); E32 t=1020 (300,20,680,200) #B3AF59 1,167 (interior); E18 t=1086 (660,60,880,220) #A8A352 1,210 (outdoor forest) | 0x0D | medium (no open-daylight frame; the hair is mostly drawn in shade) | #887744 (DL) |
| Bonz | solid | **#505E80** | #3D475A | E17 t=246 (150,0,1120,272) #515E81 74,068; E17 t=428 (540,180,720,330) #505F7F 3,431 | 0x02 | high (one episode, 2 shots) | #445588 (DL) |
| Pegasus | solid | **#C4BDD0** | #827E93 | E4 t=352 (400,20,580,240) #C4BCCE 3,161; E4 t=443 (412,0,1132,692) #C5BED3 90,520. Interior check E36 t=755 #C9C5D6 74,165 | 0x05 | high | #DAD7EA (DL) |
| PaniK | solid | **#2E465C** (dark blue-teal) | #182424 | night only: E15 t=443 (150,30,330,300) #2E475C 18,115; t=477 same box #2D465C 17,862 (same shot). Mid shot E15 t=275 (240,30,380,215) hair #26353F 7,339 | 0x01 | **low**: night, one shot | #1A1A1C |

Notes on the table:

1. **Modes agree with the medians** for every row (results.json `modes`), e.g. Kaiba E22 t=547 mode #765B3D (109,125 px), Mai E6 t=478 mode #C3BD68 (145,522 px).
2. **Yami Bakura = Bakura.** The castle frame E40 t=275 gives lit #DADAE4 / shade #AFABBA against Bakura's #D7D8DF / #AFA8B6 (Δ ≤ 5). The E13 Shadow Realm frames are darker (#CCCBD4) because of that scene's grade; excluded.
3. **Téa:** E22 t=169 (412,0,1032,600) reads #6C4C39 (84,615 px), Δ up to 0x1C from the four-frame median, so it is excluded. That it is a
   recap shot with another grade is **INFERRED** (it sits in the first three minutes of the episode). E7 t=1151 is a real daylight
   frame and is kept; it is the reason for the 0x13 spread.
4. **Mako:** the 2-means split labels the base fill "shade" in E7 t=1013 and E4 t=270 because the blue streaks are the lit cluster;
   the table uses the base fill (largest cluster / mode) in all three frames. E4 t=270 is darker and more violet than E7.
5. **Rex:** the broad box in E4 t=350 picks up his face (skin #D1AF85); the table uses two hair-only boxes. Only one daylight frame
   exists in my search (E3 is the ship at night / interior; E11-E12 are evening / sunset).
6. **Yugi vs Yami bangs:** Yami reads brighter in 3 of 4 frames (#CBC050 against #C0B84E). Whether that is a deliberate paint
   difference or shot lighting is not decidable from these frames.
7. Hair "zones" in code: `HairPattern.TriColor` uses root + tip + fringe (`AvatarAppearance.PatternUses`, AvatarAppearance.cs:289),
   colours `hairHex`, `hairTipHex`, `hairFringeHex`. Rex has no tip zone; using TriColor with tip = root is the builder's choice.

## 2. Task 2: Duelist Glove side

Rule used: a character facing the camera has his right hand on the image left. "Unambiguous" = the arm's root or the other (bare)
hand is visible. Sheets: `sheets/glove_side_a.png`, `glove_side_b.png`, `e17_a.png`, `z_group_hands.png`, `glove_rex.png`,
`glove_rex_e04_428_zoom.png`, `z_kaiba_peg_keith_hands.png`.

| Character | Hand | Frames | Confidence |
|---|---|---|---|
| Joey | **right** | E17 t=181 (facing camera, gloved arm raised on image left); E17 t=1005 (fire-lit); owner frame mokuba_e08_440 (gloved fist image-left of his torso); agent: E21 t=1017 left fist bare | high |
| Mai | **right** | E4 t=487 (gloved hand on the bag strap over her right shoulder); E6 t=1033 (arms crossed: the gloved hand rests on her left arm, image right, where a crossed right hand ends); agent: E6 t=438, t=844, E12 t=620 left hand bare | high |
| Mako | **right** | E7 t=246 (both hands: gloved fist at the chest with the forearm from image left; bare fist image right); E7 t=227 (gloved fist carried across to his left side, forearm from his right) | high |
| Weevil | **right** | E5 t=863 (gloved hand at his glasses, forearm from image left); E4 t=725 (gloved hand holding a card near his left shoulder: back of the hand to camera, index side on image left → a right hand crossing over); E5 t=287 (fist at the chest, forearm from lower left). E4 t=1230 (pointing) is ambiguous | medium-high (no frame with the bare left hand found) |
| Bonz | **right** | E17 t=312 and t=428 (facing camera, gloved hand image left, other hand bare) | high |
| Yugi | **right** | E4 t=384 (gloved hand raised on image left, cuff below); owner frame para_e21_1017 (glove image-left of his body); agent: E8 t=440 | high |
| Yami Yugi | **right** | E7 t=955 (cards in the bare hand, gloved hand + cuff image left); E8 t=880 (pointing gloved fist image left) | high |
| Rex | **right**, but only in E12 | E12 t=126 (star cuff on the arm resting on the table, image left), t=289 and t=292 (gloved hand at the chin, forearm from lower left); all E12 = sunset. **No glove** in E4 t=428 (daylight, both hands bare skin: `glove_rex_e04_428_zoom.png`) | medium |
| Keith | **none** | wide silver wristbands on both wrists: E18 t=1086, E32 t=708, t=1020, t=1104 | high |
| Kaiba | none | E16 t=562, E26 t=785: bare hands (he carries his own disk prototype) | high |
| Pegasus | none | E26 t=285: white shirt cuffs, bare hands | high |
| Téa, Tristan, Bakura, Mokuba | none seen | para_e21_1017 (Tristan, Bakura, Téa hands bare; the gloved hand by Téa is Yugi's right); E16 t=279 Bakura bare; E8 t=1080 Mokuba bare; E25 t=1007 Téa holding cards bare (sunset) | medium-high |
| PaniK | **both** (his own glove, section 3) | E15 t=690, t=880 | high |

**Cuff length / size (Joey E17 t=181, the one near side-on band; `sheets/cuff_d.png`, 2x zoom, source px ±10):**
cuff across the arm ≈157 px (rims (377,102)→(512,185) and (362,142)→(500,217)), cuff along the arm 34-43 px, sleeve width at the
cuff ≈90 px, glove from cuff rim to the knuckle-stud line ≈100-120 px (fist, foreshortened).
Ratios: along/across 0.22-0.27; across/sleeve ≈1.75; along/(rim→knuckle) 0.3-0.4.
Conversions (**INFERRED**: assume the stud line sits at the Middle1 knuckle and the sleeve is at least as wide as the skin):
- length ≈ 0.3-0.4 × our palm (Hand→Middle1 0.102-0.109 m) ≈ **3.2-4.2 cm** (male and female; plan F9 uses 3.2 cm; the agents read 3.0-3.3 cm in E6 t=438);
- outer diameter ≥ 1.75 × wrist width = **≥ 9.3 cm male** (wrist 5.3 cm) / **≥ 6.8 cm female** (3.9 cm). The plan's F9 cuff (skin + 0.6 cm push) is only ≈6.5 / 5.2 cm across. Mako E7 t=227 corroborates a cuff wider than the fist (cuff ≈240-255 px across against the glove's 170 px knuckle width; `sheets/cuff_mako227.png`).

## 3. Task 3: PaniK's own glove

Sheets: `h11.png`, `h12.png`, `h13.png`, `z_panik_690_full.png`, `z_panik_880_full.png`, `panik_drum.png`, evidence `ev/panik_*.png`.

- **Both hands** gloved, each with a giant drum cuff: E15 t=690 and t=880 show both hands (right on image left, left holding the
  cards on image right) with maroon fingerless gloves and drum cuffs. E15 t=690 shows "P K" on the coat the right way round, so the frame is not mirrored.
- **Glove:** maroon, fingerless (bare fingers in t=323, t=764, t=880). Lit #792541 (t=323, (160,0,900,720), 155,469 px), #792542
  (t=338, 45,837), #74263F (t=764, 15,597); shade #291A21-#2C1A21.
- **Knuckle spikes:** 4 large gold pyramid spikes across the knuckles. Right hand t=764 (covering his face, arm from image left);
  left hand t=338 (fist thrust forward on image right; the arm root is out of frame, so the side is **INFERRED** from the arm side).
  Colour lit #C6AA4F (t=323, (160,0,700,720), 26,778 px; mode #C8AB4E), #BEA857 (t=764, 611 px); shade #605D3F.
  At t=690 the left (card) hand's knuckle studs read as gold dots; their shape is not resolvable at that size.
- **Drum cuff:** a wide white-grey cylinder. Base #ACBBC1 (t=338, (300,180,900,720), 106,550 px) / #AAB9BF (t=764, 39,045 px);
  highlight streak #D2D6D6 (t=764, 4,855 px). It carries gold five-point stars (#887D3D, t=764, 2,027 px; shade #413A27) and dark
  empty star slots (t=334).
- **Size (INFERRED):** end-on, the drum is ≈1.2-1.5x his hand length (t=690: 185x200 px against ≈153 px wrist→fingertip;
  t=880: ≈370x345 px against a foreshortened hand), so it is about **22-29 cm across** on the male body. From the side (t=660) its
  length is ≈0.8x its diameter (perspective), so about **18-24 cm**: most of the forearm (male forearm 0.188-0.202 m).
- **Night grade check:** every PaniK frame is S1E15 night. In the same episode the standard glove reads glove #792441 / cuff #ACBCC1 /
  stud #BFAB54 / star #8B7E3A (Yugi, t=565). Daylight: glove #6D1D38-#7A1F3F (planner, five characters), cuff #B2BBC6 (Mako E7 t=246).
  The night grade on character fills is therefore within the day-to-day spread (≤ 0x0C). **INFERRED:** PaniK's glove and drum use the
  same paints as the standard Duelist Glove (equal within 2 per channel in the same episode), so the shared item's daylight colours apply.

## 4. Task 4: where the disk sits on a duelist ("spirit duelers" vs the star-chip glove)

### 4.1 Code (read-only)

- `ArOpponentDuelist.Create` → `NpcDuelDisk.Wear(go.transform, disk, layer, UnitsPerMetre)` (ArOpponentDuelist.cs:177): every AI
  duelist wears the opponent `ArDuelDiskRig` solid, scaled to `unitsPerMetre` only (`rig.transform.localScale = Vector3.one * unitsPerMetre`,
  NpcDuelDisk.cs ~76). **The disk is not scaled by the body's `HeightScale`**; the body is (`OverworldAvatar3d` line 319, clamp 0.75-1.25;
  `HeightScale = heightCm / (165 F | 175 M)`, AvatarAppearance.cs:122-124). Yugi (153 cm) gets a 0.874-scale forearm under a full-size disk.
- **Bone: the LEFT forearm.** `NpcDuelDisk.Tick(_lFore, _lHand, handCanon, dt)` (ArOpponentDuelist calls it after the arm solve). Frame
  (NpcDuelDisk.cs:130-145): `fwd` = LeftLowerArm → LeftHand; `back` = the hand's canonical back-of-hand axis projected off `fwd`;
  `canon = LookRotation(back, fwd)`; `rot = canon * RigToCanon` with `RigToCanon = LookRotation(Vector3.left, Vector3.forward)`;
  cradle centre on the bone axis **6.6 cm up the forearm from the LeftHand bone** (`hand.position - fwd * (0.066 * s)`, s = rig scale);
  `CradleCentre = (0.093, -0.004, -0.007)` m in the rig root frame.
- Rig chain (ArDuelDiskRig.BuildDisk, SpiritDuelerSkin.WristCalibratedDiskRoot, DiskFx.ApplyBladeHierarchy): DiskRoot =
  (0, 0.034, 0) − (BladeDeployedPos (0, 0.02, 0.04) + WellOrigin (−0.0975, 0.0016, 0.228)) = **(0.0975, 0.0124, −0.268)**; BladePivot =
  BladeDeployedPos when Mechanical; mesh = OBJ groups 1:1 (`DiskMeshVisualScale = 1.00`). So rig = mesh + (0.0975, 0.0324, −0.228).
- Mapping I use (derived from the two lines above; `disk/disk_extents.py`): with origin at the LeftHand bone,
  **along** (+ toward the fingers) = −0.0705 − mesh.x, **up** (out of the back of the forearm) = mesh.y + 0.0364,
  **side** (+ away from the body) = 0.221 − mesh.z.
- The player's disk: `ArDuelDiskRig.LateUpdate` → `ApplyWristPose(Tracker.TryGetLeftForearm)` (ArDuelDiskRig.cs:1295-1299, 1266); there is
  no player body in the duel (hidden), and `ArFloatingHand` has no glove code. No glove/disk contact is possible on the player side.
- **Right hand vs the disk** (`KeepRightHandOutOfDisk`, ArOpponentDuelist.cs ~1506-1575 in the working tree): finger joints and the
  RightHand bone get a margin `m = (DiskKeepOutM 0.008 + 0.010 if _gloved) * scale` = **1.8 cm** gloved; the forearm is sampled only at
  25 / 50 / 75 % of elbow→hand with **(0.045 + 0.008) = 5.3 cm**; `_gloved` = any SkinnedMeshRenderer named `*_hands_*` (line 231).
  `scale = UnitsPerMetre * HeightScale`. The arm mount is excluded from the parts (EnsureDiskParts).
  Note: ArOpponentDuelist.cs has an uncommitted 228-line diff from another session (mtime 2026-10-11 20:37), so line numbers may drift.

### 4.2 Disk extents in the worn forearm frame (metres; along 0 = LeftHand bone, − = toward the elbow)

From `disk/disk_extents.json` (OBJ `Assets/StreamingAssets/Models/SpiritDueler/BattleCityDuelDisk.obj`, per-group vertex bounds):

| Part | along | up | side |
|---|---|---|---|
| arm mount (cradle, dark strap) | **−0.132 … 0.000** | −0.039 … +0.015 | −0.040 … +0.039 |
| MAIN BODY (hub) | −0.135 … +0.059 | +0.0185 … +0.058 | −0.086 … +0.106 |
| BOTTOM COVER (hub underside) | −0.135 … +0.035 | **+0.0145** … +0.018 | −0.086 … +0.084 |
| COUNTER TOP | −0.110 … +0.035 | +0.058 … +0.078 | −0.041 … +0.022 |
| DECK FLAP / spring holder | −0.011 … +0.030 | +0.038 … +0.058 | ±0.014 |
| GY COVER | −0.115 … +0.016 | +0.047 … +0.068 | +0.015 … +0.106 |
| Hub, all fixed parts | −0.1405 … +0.0595 | −0.0386 … +0.0779 | −0.0864 … +0.106 |
| Play pose, everything | −0.384 … +0.259 | −0.0386 … +0.0779 | −0.0864 … +0.2447 |
| Standby blades (computed from SpiritDuelerMechanics standby radii / directions) | −0.399 … +0.0595 | +0.018 … +0.039 | −0.2125 … +0.2087 |

Arm-mount profile (`disk/armmount_profile.py` + a below-axis query): a plate at up +0.0085…+0.0145 across ±0.039 and two side
prongs whose inner faces are at side **+0.030** (away from the body) and **−0.032** (body side), reaching down to up −0.039. No
vertex crosses under the arm (none with |side| < 0.02 below the axis), so there is no closed strap. The mount runs from 13.2 cm up
the forearm to exactly the hand bone.

### 4.3 Our body, rest pose (`disk/forearm_sections2.json`, exact edge/plane sections of argon_avatar_{male,female}.glb)

| | male L | male R | female L | female R |
|---|---|---|---|---|
| forearm (ForeArm→Hand) | 0.1884 | 0.2018 | 0.1681 | 0.1810 |
| hand (wrist → farthest finger vertex) | 0.1956 | 0.1830 | 0.1988 | 0.1859 |
| palm (Hand→Middle1) | 0.109 | 0.102 | 0.109 | 0.102 |
| skin at along 0…−0.032: up max / min | +0.024 / −0.0265 | +0.024 / −0.025 | +0.019 / −0.0185 | +0.018 / −0.018 |
| skin side max / min | +0.027 / −0.027 | +0.028 / −0.027 | +0.020 / −0.019 | +0.019 / −0.020 |
| skin radius max | 0.028 | 0.029 | 0.020 | 0.021 |

(The left/right length difference is in the rig; both bodies.)

### 4.4 What clips (rest pose, before HeightScale; figure `disk/clearance_fig.png`)

- **Right-hand glove (Joey, Mai, Mako, Weevil, Bonz, Yugi, Yami, Rex):** the disk is on the other arm, so the cuff never touches it at
  rest. It can clip **only when the right hand works the disk**: the wrist is protected by a 1.8 cm margin, but the cuff's outer radius
  is 3.4-3.5 cm (male) / 2.6-2.7 cm (female) with the plan's F9 cuff, and **≥ 4.7 cm / ≥ 3.4 cm** with the anime-proportioned cuff
  (section 2). The nearest forearm sample (5.3 cm margin) is at 75 % of elbow→hand, i.e. 4.5-5.0 cm up from the wrist, so the
  cuff band (wrist → 3.2-4.2 cm up) is covered only by the wrist's 1.8 cm. When the wrist is the lowest point over a part (forearm
  tilted down to it), the cuff can sink ≈0.9-1.7 cm (plan cuff, female-male) or ≥1.6-2.9 cm (anime cuff) into that part. Fix direction (for whoever owns the code):
  when gloved, test points at the wrist and ~2 cm up the forearm with radius = cuff outer radius × HeightScale + DiskKeepOutM.
- **Left-hand cuff (only PaniK):** the arm mount spans along −0.132…0 and the hub underside is at up +0.0145. The plan cuff's top
  would reach up +0.030 (male) / +0.025 (female), **1.55 cm / 1.07 cm into the hub underside and the mount plate** (the bare skin
  already reaches +0.024 / +0.019, i.e. 0.95 / 0.47 cm). Sideways the plan cuff reaches ±0.033 (male), 0.3 cm past the outer prong
  face (+0.030) and 0.1 cm past the body-side one (−0.032); female (±0.026) clears them by 0.4-0.6 cm. PaniK's drum (≈ 11-14.5 cm radius, ≈ 18-24 cm long, INFERRED) swallows the whole mount and the hub
  underside. Options, owner's call: lift the rig along `back` by (cuff top + clearance − 0.0145) (≈ +0.019 m for the plan cuff;
  ≈ +0.12-0.13 m to sit on PaniK's drum); or slide it up the forearm (−along) by cuff length + clearance (the hub would then reach
  −0.17…−0.18, near the elbow at −0.188 male L); or hide the left drum while the disk is worn.
- Height scale: the body (and the glove) shrink with HeightScale while the disk and its 6.6 cm cradle offset do not; for Yugi
  (0.874) the skin top at the wrist is ≈ +0.021 instead of +0.024.

## 5. Corrections to earlier notes

- The planner wrote "Rex: no glove (not seen in these frames)". Rex wears the Duelist Glove on his right hand in S1E12 (t=126, 289, 292); it is absent in S1E4 t=428.
- PaniK's glove is two-handed with drum cuffs and pyramid spikes; it uses the standard glove's paints (section 3).
- The plan's cuff (`push=0.006`) is thinner than the anime cuff: outer diameter ≈1.75x the sleeve width (section 2).
- The "lighter zigzag" on the glove: jagged dark shapes appear at the wrist end of several gloves (Yugi E4 t=384, Rex E12 t=289,
  PaniK E15 t=323). Whether it is a print or a stylised shade terminator is still unresolved.

## 6. Read-only proof

`mtimes_before.txt` (taken at start) and `mtimes_after.txt` (at the end) list every file's mtime under `~/argon-avatar/tools/custom/*.py`,
`~/argon-avatar/build/custom/clothes`, `…/base_items`, `ProjectARGON/Assets/Resources/WRLDZ/Avatar3D/Clothes`,
`ProjectARGON/Assets/StreamingAssets/WRLDZ/Avatar/clothes`: 1,399 lines each, **identical** (md5 9a21c32a0c081c4429a8c9dff11171ec both).
Also unchanged: `argon_avatar_male.glb` (2026-09-29 13:34:57), `argon_avatar_female.glb` (2026-09-29 13:35:11),
`BattleCityDuelDisk.obj` (2026-10-08 12:05:09). Blender was run `-b --factory-startup` and imported only; nothing was saved.

## 7. Duelist Kingdom outfit albedos — Phase 3 + Phase 4 (avatar session, 2026-10-12)

Owner scope: **every canon DK duelist who dueled Yugi or Joey**. Colour rule: anime-frame measurement > sourced design
record (`argon-avatar/research/customization/outfit_catalog.json`) > `ClothingCatalog.C()` fallback; where a frame can't be
read, keep the authored/design value. Frames: `…/evidence/dk_anime/` (1280×704 dub S1 stills), read with `Tools/anime/meas.py`
(flat filter → HSV window → V 2-means → per-channel median) plus flat-colour grid scans.

**Why the albedo is the lever.** A DK garment is a `ClothingCatalog.C(...)` signature piece (`BodyType = 0`, `albedo = null`,
`mask = null`). In `OverworldAvatar3d.GraftCloth` that gives `flatPaint = false` and no `_MaskMap`, so no paint key is
registered and the shader's `col = the BaseMap` (`AvatarRecolor.shader` `Paint()`) — i.e. **the 8×8 albedo swatch IS the
rendered outfit colour**. Repainting the swatch is exactly right; `C()`'s fallback only feeds the wardrobe/portrait swatch
(`AvatarPortraitView.cs`) and the `tex == null` branch.

### Own `_dk` albedo swatch (22 files on disk, current values)

| character | slot : hex |
|---|---|
| Pegasus | shirt `#BA5858` · bottoms `#A84C4E` · shoes `#B8BCC4` · hands `#D7DBDB` |
| Bandit Keith | shirt `#30334A` · bottoms `#D8CBA8` · shoes `#3E2321` · hands `#AEAEC0` · hat `#B23A3A` |
| Bonz | shirt `#8A8B8E` · bottoms `#605A9D` · shoes `#5B8DAF` · hat `#8A8B8E` |
| Kaiba *(Phase 4)* | shirt `#443D7A` · bottoms `#36515E` · shoes `#333535` |
| Yugi *(Phase 4)* | shirt `#404D96` · bottoms `#404D96` · shoes `#D7D9D4` |
| Yami Yugi *(Phase 4)* | shirt `#212275` · bottoms `#212275` |
| Mako *(Phase 4)* | bottoms `#4E4A4F` |

The Phase-4 values are the **authored** swatches (the art shipped with the meshes) and are the per-slot DK design record;
the DK frames corroborate them by quantised-cluster match — Kaiba e16:562 + e26:785 give dark navy/teal clusters plus a
blue-violet bucket that contains `#443D7A`; Yugi e06:733 gives a dark-indigo bucket and a light-grey shoes bucket containing
`#D7D9D4`. Mako e04:270 is a wide beach shot (blue top + dark values only), so the authored value is kept.

### Shared shells + look hexes (no own albedo)

| character | shells | colour |
|---|---|---|
| PaniK | `shirt_duelcoat` / `bottoms_plain` / `shoes_boots` | painted via the `panik_dk` look |
| Para | `shirt_plain` / `bottoms_plain` / `shoes_plain` | `ShirtHex #96473E` (e20:602 `#97423F` + e21:614 `#944C3C`), `ShirtTrimHex #E3B64C` |
| Dox | ″ | `ShirtHex #576EAF` (e20:623 `#546BAF`, 141k px + e21:248 `#5A71B0`, 101k px), same trim |

Para/Dox are floor-length robes in the frames (no separated legs) and split red=Para / blue=Dox per the independent
`AiDuelistLooks` assignment. The gold collar/cuff/hem trim is not isolable in any frame, so the design value is kept.
`CharacterLook.ShirtTrimHex` was added because `ApplyTo` used to set all three shirt channels to `ShirtHex`, flattening the
trim that `AiDuelistLooks` sets on `Shirt.1`.

### Phase-4 wiring (the four orphans)

The `kaiba_dk` / `yugi_dk` / `yami_yugi_dk` / `mako_dk` meshes + albedos shipped with **no `C()` row**, so `GraftCloth`
(`Clothes/argon_<item.Id>_<sex>.fbx` + `albedo_<Id>`) could never load them. Rows added to `ClothingCatalog`
(`shirt/bottoms/shoes_kaiba_dk`, `…_yugi_dk`, `shirt/bottoms_yami_dk`, `bottoms_mako_dk`) and look rows added to
`CharacterLooks.All` **ahead of** the Battle City rows, so `Match()` (the first row wins an equal-length match) resolves a
plain `yugi` / `kaiba` / `mako` to the DK set while the BC set stays equippable in the customizer. Replaying `Match()` over
the table: `Yugi Muto → yugi_dk`, `Yami Yugi → yami_yugi_dk`, `Seto Kaiba → kaiba_dk`, `Mako Tsunami → mako_dk`.

`yami_dk` is **Yami Yugi's** uniform, not Yami Bakura's: `hide_shirt_yami_dk_male.png` and `hide_bottoms_yami_dk_male.png` are
**byte-identical** (same md5) to Yugi's, while every other garment's mask differs (Yugi dk vs bc = 28,212 differing bytes;
Pegasus vs Yugi = 35,199) — identical body coverage means the same garment re-coloured, not a different outfit.

### Gap — DK duelists with no `_dk` garment mesh

`GraftCloth` resolves the mesh as `Clothes/argon_<item.Id>_<sex>` with no override, and the `_dk` meshes are distinct authored
FBXes (Meshy; no mesh-build tooling in the repo). These DK duelists therefore cannot be repainted as signature pieces and still
render on their Battle City (`*_bc`) garments:

| duelist | DK story stage | reason |
|---|---|---|
| Weevil Underwood | `season1_dk` stage 1 | no `argon_*_weevil_dk_male.fbx` |
| Rex Raptor | stage 2 | no `…_rex_dk…` |
| Mai Valentine | stage 4 | no `…_mai_dk…` |
| Joey Wheeler | stage 5 | no `…_joey_dk…` |
| Yami Bakura | stage 9 | no `…_bakura_dk…` (the `yami_dk` art is Yami Yugi's — above) |
| Téa Gardner | — | no `…_tea_dk…` |
| Mokuba | — | no `…_mokuba_dk…` |

(Tristan also has no `_dk` mesh but is not a DK-roster opponent.) Closing the gap needs those garment sets authored
externally; once an `argon_<slot>_<name>_dk_male.fbx` + `albedo_<slot>_<name>_dk.png` (8×8 swatch) + `hide_…` exist per slot,
the same `C()` + look-row pattern wires them with no further code.

Audit after the change: `Tools/anime/audit.py` reports the new looks with unchanged hair verdicts — `yugi_dk` / `kaiba_dk` /
`mako_dk` `exact`, and `yami_yugi_dk` `close` (the pre-existing Yami-Yugi hair gap, now reported on both Yami rows).

## 8. Yami Bakura + the all-canon face pass (avatar session, 2026-10-12)

Owner: "Yami Bakura needs to look 1000x better", and the face pass applied to **every** canon duelist, not just him.

### 8.1 The two defects

The `yami_bakura` row (`CharacterLooks.cs`) was `L("yami_bakura", "Yami Bakura", "yami bakura", 0, "ryou_bakura", "", …,
skin: "#404858")`:

1. **`FaceId = ""`** — nothing passed to `OverworldAvatar3d.ApplyFace` (line 342: `ApplyFace(look.silhouette ? "" :
   look.faceId)`), so his authored HD `Faces/yami_bakura.png` (+ `_mouth`, + `_body`) was **dead art** and the blank base
   face was drawn. Only Kaiba was wired (`seto_kaiba`).
2. **`skin: "#404858"`** — a near-black blue-grey. `ApplyTo` sets `a.skinHex` + `SetPaint("Body.0", SkinHex)`, so the whole
   body *including the head* rendered that colour: a white-haired figure with a near-black face.

### 8.2 Face wiring (all 25 canon looks)

Each canon row now passes its decal id as the 6th positional `face` argument — the id map is the one the audit already
carried (`FACE_BY_LOOK`): `yugi_muto` (yugi_dk/bc), `joey_wheeler`, `mai_valentine`, `tea`, `bakura`, `marik`, `pegasus`,
`weevil`, `rex_raptor`, `mako` (dk/bc), `bandit_keith`, `bonz`, `ishizu`, `odion`, `duke`, `tristan`, `mokuba_kaiba`,
`yami_yugi` (dk/bc), `yami_bakura`, `panik`, `para`, `dox`. All 25 decal pngs exist on disk (checked); 20 also have a
`_mouth`, 3 have no mouth (`marik`, `bonz`, `panik`, `para`, `dox`), and `para`/`dox` additionally ship `_forehead`.

`Tools/anime/audit.py` used `FACE_BY_LOOK.get(id, row["faceId"])` — it **preferred its own expectation table**, so a row with
a blank `FaceId` was reported as wired. It now reads the parsed code value (`code_face = row["faceId"] or ""`), keeps
`FACE_BY_LOOK` only as the expectation, adds a `wired` column, lists unwired rows, and prints the count. Result: **0 unwired**
(the two remaining `no` rows, `face_weevil_bc` / `yami_marik`, are decal files with no look row — inventory only).

### 8.3 Yami Bakura's skin (measured, then chosen)

Measured with a tight HSV skin window (H 8–42°, S 0.12–0.55, V 0.45–0.97) so that brown hair/leather is not counted:

| source | reading |
|---|---|
| DK frame `bakura_e16_276.png` (ep16 possession duel, dim night scene) | `#B99B87` 17% · `#B9AF9B` 15% · `#C3AF9B` 9% · `#B9A591` 8% (loose mask: `#BAAE96` 43%, `#BAA28A` 20%) |
| DK frame control `weevil_e04_1052.png` | `#CDAF87` 36% |
| DK frame control `joey_e06_268.png` | `#D7B99B` 19% · `#D7A591` 13% |
| lit Bakura game art `cmc_dialogue_bakura_52_448x424.png` | `#F5C39B` 14% · `#F5B991` 13% |
| lit Bakura game art `cmc_cutin_bakura_15_224x352.png` | `#F5B991` 23% |

The DK frame reads **greyer/darker** than the same-era lit controls, but *both* skin clusters inside that frame are grey-tan,
so part of that is scene lighting — the absolute value is not trustworthy. Chosen: **`#CFAC8D`** = Ryou's default `#F9C494`
greyed 35% toward neutral grey (128,128,128). That lands inside the DK-era measured band (`#B99B87` dim … `#CDAF87`/`#D7B99B`
lit), is greyer than Ryou's default (so the possession still reads), and matches the design intent ("a grey skin") without the
near-black of the old `#404858`. If it reads too dusty in the lit customizer, the lit-art reading `#F5C39B` is the next
candidate.

### 8.4 Stale `C()` fallbacks aligned (fallback must approximate the shipped albedo)

QC rule: the fallback must appear in `albedo_<Id>.png` within 12/channel. 8 rows failed; 2 (`shoes_kaiba_dk` `#333535`,
`bottoms_mako_dk` `#4E4A4F`) were **deliberate averages** of a 2-tone swatch and were kept. `shoes_bakura_bc`'s `#EDEDF2`
is the **left/main** half of its 2-tone swatch — correct, not stale (an earlier flat average, `#B7B7BB`, would have been a
wrong "fix"). The genuinely stale rows, each now corroborated by the design record:

| item | old fallback | new | source |
|---|---|---|---|
| `bottoms_bakura_bc` | `#3A4259` | `#B3ADC2` | albedo is single-colour `#B3ADC2` |
| `bottoms_mokuba_bc` | `#3B4254` | `#4F5A86` | record "Jeans `#4F5A86`" |
| `shoes_mokuba_bc` | `#4F5987` | `#4E78B8` | record "Sneakers `#4E78B8`" (old value was the jeans colour) |
| `shoes_marik_bc` | `#C9A84F` | `#1E1E1E` | record "Shoes `#1E1E1E`" (old value was the neck-ring gold) |
| `shoes_tea_bc` | `#F0E8A8` | `#44718D` | record "Platform sandals `#44718D`" (old value was the collar cream) |
| `face_weevil_bc` | `#21262B` | `#E3C84A` | record "Round glasses `#E3C84A`" |

### 8.5 Open (not changed here)

- **No `argon_*_bakura_dk` garment mesh** — Yami Bakura still wears Ryou's Battle City garment *shapes* (see §7 gap). Only a
  commissioned `_dk` mesh set changes the silhouette.
- **No `yami_bakura` hair mesh** (only `ryou_bakura`) — he borrows Ryou's hair. Canon: the same body, so this is defensible.
- **Paint-manifest name mismatch — RESOLVED, see §9.1.** `OverworldAvatar3d.BuildMaterials` loaded
  `Resources.Load<TextAsset>("WRLDZ/Avatar3D/argon_base_" + sex + ".paint")` / `.parts`, but disk ships
  `argon_avatar_male.paint.json` / `argon_avatar_male.parts.json`. The load returned null, so the region paints never
  registered and the parts fell back to the flat `Tint` path — which is exactly what painted the body one flat
  `skinHex`. It mattered: see §9.1 for the measurement.
- **Decal quad shape.** `Faces/*.png` content shapes differ (only marik, pegasus, seto_kaiba, tristan, weevil, yami_bakura are
  band-like; `yugi_muto` bbox y 33..494, `bakura` 66..434) while `ApplyFace` maps the whole 1024×540 texture onto one fixed
  13.2×7 cm quad — possible squash/float for the newly-wired rows. Needs a Unity visual check.

## 9. "He looks naked now" — the flat-body diagnosis (avatar session, 2026-10-12)

### 9.1 Root cause, 3D (the skin hex was never the bug)

`BuildMaterials` loaded `argon_base_<sex>.paint` / `.parts` while disk ships `argon_avatar_<sex>.*.json` (§8.5). The load
returned null, so no part mask bound and `_paintKey` stayed empty. Two consequences:

1. `Apply` fell through to `Tint(_smr, skinHex)`, which sets `_BaseColor` on the **whole** body. The Meshy body's own
   mask is **45.1% skin / 54.3% undersuit** (`Meshy_male_Body_mask.png`) — the body mesh carries its undersuit geometry —
   so a flat tint paints that undersuit region skin. With `#404858` that read as dark clothing; with `#CFAC8D` it read as
   bare skin. **Same defect, both reports.** The skin reading in §8.3 was correct; the surface it landed on was not.
2. A `Universal Render Pipeline/Unlit` material has no `_HideOn`, so `SetBodyHide` (L896) was a no-op — the body was
   never discarded under the clothes it wore. `hide_shirt_bakura_bc_male.png` / `_bottoms_` / `_shoes_` discard 12.0% /
   15.2% / 12.1% of the body texels; all of that was live skin-toned surface before the fix.

**Measured:** body surface skin-toned **100.0% → 45.1%**; the undersuit region takes `#4F6595`, the authored
`shirt_bakura_bc` suit hex, on 54.3%.

Fixes: `LoadManifest` accepts `argon_avatar_*` then `argon_base_*` (`.paint` and `.parts`); `Apply` no longer flat-tints a
region-painted body; `ApplyPaints` falls back to `look.skinHex` for `Body.0` so a region-painted body can never fall back
to the white `_BaseMap`; `AiDuelistLooks` yami bakura `WithSkin("#CFAC8D")` replaces the stale `#404858` so the name route
and the catalogue row agree.

### 9.2 Second defect, 2D full-body paper-doll (duel HUD face, opponent select, customizer preview, companion/HUD/map)

`StreamingAssets/WRLDZ/Avatar/clothes/` ships sprites for the **base wardrobe only** (`shirt_plain`, `bottoms_cargo`,
`shoes_boots`, …). No canon signature id has one — not `shirt_bakura_bc`, not any `_dk` piece. `ApplyClothing` then drew
`WhiteSprite()` at `item.Fallback` with **0.42 alpha**: a translucent centred square, not a garment, over `body_full.png`,
which is a **nude mannequin** (55.4% skin-toned). A canon duelist's portrait was therefore a bare mannequin under a wash
of its outfit colour, and the §8.4 `bottoms_bakura_bc` repair (dark navy → pale lavender) is what turned the dark wash
pale and made the mannequin read as naked.

Fix: a piece with no sprite of its own borrows the **same slot's base silhouette nearest in pockets** and is painted with
its own colour at 0.94 alpha; `hat`/`facial` layers stay empty (no honest silhouette to borrow, and a wash would tint the
whole figure).

**Measured**, distance from the bare skin tone (`#CDA382`) over the garment area of the mannequin silhouette
(0 = identical to bare skin): **81/255 before → 164/255 after**; the base wardrobe's own art reads 141/255.
Overlay area also drops from a 720×720 square (519k px) onto a 157k px figure.

### 9.3 Still open

- No per-`id` paper-doll sprite for any canon piece — a borrowed silhouette approximates the cut, it is not it.
- `appearance.portraitIndex` is still never set, so every duelist's `look_<n>.png` (face + hair) is `look_0.png`.
- `Textures/` ships the `*_mask.png` set but no `Meshy_<sex>_<Part>.png` albedo. Parts render the right colour flat,
  with no fold detail, and `_textured` stays empty (so `_BaseMap` is the white default).
