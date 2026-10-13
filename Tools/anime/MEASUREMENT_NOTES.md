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

**These edits were inert on screen** ("I am not seeing a change", 2026-10-13): the paint manifest still never parsed, so
the mask, the paint key and the ref-luminances all stayed empty and the body kept falling through to the flat tint. See
§9.4 — the mask has to bind before any of the above can matter.

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

### 9.4 Why §9.1 changed nothing: the paint manifest never parsed (2026-10-13)

§9.1 pointed `LoadManifest` at the right **file** but not at the right **fields**. `BuildMaterials` read it with
`JsonUtility.FromJson<PaintFile>` — the schema `{"parts":[{"part":"…","key":"…","mask":"…","refLum":[…]}]}` — while
`argon_avatar_<sex>.paint.json` is keyed by part:

```json
{"sex":"male","src":"build/meshy/argon_avatar_male.glb",
 "parts":{"ArgonBody_male":{"morph_max_m":{…},
   "recolor":{"mask":"Meshy_male_Body_mask.png",
     "regions":[{"region":"skin","coverage":0.4322,"mean_hex":"#F9C494","mean_lum_linear":0.62149},
                {"region":"suit","coverage":0.5646,"mean_hex":"#818284","mean_lum_linear":0.22228},
                {"region":"other","coverage":0.0032,"mean_hex":"#383030","mean_lum_linear":0.03775}],
     "clusters":[…]}}}}
```

The part name is a JSON **key**, and the file contains **no `"part"`, no `"key"` and no `"refLum"` string at all**
(`grep` of the male manifest: `"mask"` ×4, nothing else). `JsonUtility` therefore yielded an empty `parts` list,
`paintInfo` stayed empty, `mask` was null for every part, and no material ever took the recolor shader — so the body
stayed `URP/Unlit` with the white default `_BaseMap`, `bodyMasked` was false, and `Apply` kept flat-tinting the whole body
with `skinHex`. §9.1's other edits were correct but unreachable.

A second miss of the same kind: the manifest's `mask` carries the `.png` extension, and `Resources.Load` wants the asset
name without it (the `.parts` reader already stripped it at L1103; the mask load did not).

**Fix.** `MiniPaints()` — a hand reader in the same style as `MiniParts()`, replacing the `JsonUtility` call:
part name → paint-key prefix by name (`Body` / `Shirt` / `Bottoms` / `Shoes`), `mask` with `.png` stripped, and the
`mean_lum_linear` values **in file order**, which is the mask's own channel order (body: R skin / G undersuit / B other;
garments: R main / G accent / B trim) → `_RefLum.x/y/z`, the order `_ColorA/B/C` wants.
Verified against both shipped manifests: 4 parts each, mask names `Meshy_<sex>_{Body,Shirt,Bottoms,Shoes}_mask` — all
present on disk (only the four `Meshy_*` albedos are missing).

**RefLum when there is no albedo.** `Paint()` keeps a region's shading as `detail = (lum / refLum)^0.75`; the shipped
`_BaseMap` is the white default, so `lum = 1` and the manifest's source-albedo means would *scale every paint up*
(coat primary 0.011 → 8×, suit 0.222 → 2.8×, skin 0.621 → 1.4×). A masked part that loaded no albedo therefore takes
`_RefLum (1,1,1)` and each paint lands exactly on its own hex; the manifest values apply again the moment an albedo
texture exists.

**Renderer names match the manifest keys** (so the lookup hits): inside `argon_base_<sex>.fbx` the nodes are
`ArgonBody_<sex>`, `ArgonPart_{Shirt,Bottoms,Shoes,Hair,Eyes,Eyelashes,Eyebrows}`. The FBX's own three garment parts are
set inactive whenever a wardrobe piece is worn (L561 / L600-602), so binding masks to them cannot double-dress the body.

**Effect once the mask binds:** the body takes `WRLDZ/AvatarRecolor` with `_paintKey[body] = "Body"`, `Body.0` =
`look.GetPaint("Body.0")` else `look.skinHex`, `Body.1` = the worn shirt's `Suit` (`shirt_bakura_bc` → `#4F6595`), the
scalp region takes `ApplyScalpColour`'s hair root colour, and the material finally has `_HideOn` — so `SetBodyHide`
actually discards the covered body texels (12.0% / 15.2% / 12.1%).

**Not changed:** the masks are imported `sRGBTexture: 1`, but they are 97.6-99.6% hard 0/255 (measured both sexes, all
four parts), so the sRGB→linear weight error is confined to the thin antialiased edge.

## 10. "The clothing is clipping all over the skin" — the one-mesh outfit hide maps were empty (2026-10-13)

**Report.** "The clothing is clipping all over the skin" after the §9.4 build, on the 3D avatar.

**Localising it (measured, Blender + the same body/garment sources the hide tool uses).** The worn set of the duelist
whose look was being reworked — `shirt_bakura_bc` / `bottoms_bakura_bc` / `shoes_bakura_bc` on `ArgonBody_male`
(1.4227 m² of body surface, 10 455 faces) — is **clean**:

| outcome | area | % body |
|---|---|---|
| hidden by the shipped union of the three hides | 1.0791 m² | 75.8% |
| drawn: no garment within the normal ±3 cm (head/neck/hands, legitimately bare) | 0.3113 m² | 21.9% |
| drawn: garment straight above (hide-map hole) | 0.0322 m² | 2.3% |
| drawn: body outside the garment (poke-through) | 0.0000 m² | 0.0% |

and 0.0% of each of the three garments is buried inside the body. The 2.3% "map hole" sits at z ≈ 1.44 (chest/collar),
which is the tool's own documented per-item `EXCLUDE` case (the +normal ray hits the *far* collar wall; the body between
the collar and the neckline is meant to stay visible). So his set was not the cause.

**The cause: the two one-mesh outfits ship a hide map that hides nothing.**

* `Outfits/Outfit_kaiba_male_hide.png` and `Outfit_yugi_male_hide.png` shipped as **1024 px, all zero**
  (`coverage 0.0000`, every byte 0), while `build/custom/hide_maps_rebuild_report.json` records `cover 0.3012`
  (kaiba, 4904 faces) and `0.3144` (yugi, 5080 faces) for those same two jobs — the tool's output never reached the repo,
  and the 1024 px files are the ones `outfit_build.py` writes through Blender's colour management (the "wrote 76/255"
  bug in the tool's own header).
* `ApplyOutfit` still calls `SetBodyHide(outfit_hide)`, so `_HideOn` turns on with a map that discards nothing and the
  body — skin *and* its own baked base outfit — is drawn straight through the outfit.
* Geometry, body vs `argon_outfit_yugi_male.fbx`: **28.1% of the body surface (0.3992 m²) lies within 5 mm of the outfit
  surface** (kaiba 22.7% / 0.3234 m²), 9.2% / 8.6% at 5 mm–1 cm, 26.4% / 25.8% at 1–3 cm, and 7.4% / 5.7% of the outfit
  surface is buried inside the body. A quarter of the avatar in a ±5 mm shell pair z-fights — dense speckle, which is
  what "clipping all over the skin" looks like.

**Why it reads as clipping now.** The map was empty before this session too, but the body was one flat skin tone (§9.1),
so the same interpenetration read as a skin-coloured figure ("naked"). With the real mask bound (§9.4) the identical
geometry shows the body's own base cloth and skin in patches over the outfit.

**Fix.** Rebuilt both maps in place with the tool's own coverage pass (`rebuild_hide_maps.py`; `GENEROUS` already applies
to `kind == 'outfit'`, which is why the report's 0.30/0.31 exist): 2048 px, plain 0/255, `coverage 0.3012 / 0.3144` —
byte-for-byte the report's numbers. The run was restricted to the outfit jobs (`--only` is a substring test that would
also catch every `*_kaiba*` garment, so a copy with `if kind != 'outfit': continue` was used) and to the male body, so no
garment map and no shared report entry was touched.

**Acceptance (same probe, map swapped).**

| outfit | body drawn, old map | body drawn, new map | drawn within 5 mm of the outfit, old → new |
|---|---|---|---|
| kaiba | 1.4227 m² (100%) | 0.7301 m² (51.3%) | 0.3234 m² (22.7%) → 0.0079 m² (0.6%) |
| yugi | 1.4227 m² (100%) | 0.6176 m² (43.4%) | 0.3992 m² (28.1%) → 0.0061 m² (0.4%) |

The residual 0.6% / 0.4% is the deliberate half-texel erosion at the map's edges, the same order as the garment sets.

**Who wears a one-mesh outfit.** `CharacterLooks.ApplyTo` clears `outfitId` for every canon duelist (the split pieces are
the DK/BC sets), and `AiDuelistLooks` only reaches the outfit loop for names with no look row, so in practice the outfit
tile in the customizer is the path that shows these two assets.

**Still open.** The outfit hides import `sRGBTexture: 1` (the `Clothes/` hides are `sRGBTexture: 0`), which is harmless
for a hard 0/255 map but should be linear if a partial value ever matters; `isReadable: 0` is fine there because
`ApplyOutfit` samples the texture on the GPU instead of compositing it through `CombineHides`.

## 11. "The clothing is clipping all over the skin" — body mask undersuit region covers the bare legs (2026-10-13)

**Report.** Same words as §10, still on a duel opponent's 3D avatar after the §10 outfit-hide fix.

**What is actually painted where (measured, `render_probe.py`, Blender + the shipped body glb, body mask and hides).**
For every body face the shipped hide maps leave *drawn*, the body mask's `R` (skin) region is painted with
`look.skinHex` and its `G` (undersuit) region with `Body.1` — set by `OverworldAvatar3d.ApplyPaints` (L1021-1023)
from `_outfit.suit` else `ClothingCatalog.Get(look.shirtId)?.Suit`, where the `"skin"` sentinel means the skin tone.
Per look, visible body area by rendered region (% of 1.4227 m²):

| look | drawn | skin (R) | undersuit (G) | where the G colour shows |
|---|---|---|---|---|
| `bakura_bc` / `yami_bakura` | 24.2% | 9.6% | 14.5% | head 5.3 (scalp), neck 3.4, chest 4.3, forearm/waist 1.4 |
| `weevil_bc` | 32.7% | 9.8% | **22.9%** | head 5.3, neck 1.4, forearm 1.2, thigh 0.8, **shin 14.1** |
| `bonz_dk` | 35.2% | 9.8% | **25.4%** | head 5.3, neck 1.4, forearm 1.2, **thigh 3.4, shin 14.1** |
| `mako_dk` | 58.9% | 17.6% | 41.3% | head 5.3, neck 6.1, arm/torso 7.4, thigh 6.7, **shin 14.1** |
| `keith_dk` | 27.3% | 9.5% | 17.8% | head 5.3, neck 4.1, arm/torso 7.9 |
| `rex_bc` / `mokuba_bc` / `yugi_dk` / `kaiba_dk` | 20.1 / 20.1 / 20.1 / 19.7% | 9.6% | 10.5 / 10.5 / 10.5 / 10.1% | head 5.3, neck 3.4, forearm 1.2 |
| `pegasus_dk` | 17.5% | 9.6% | 7.9% | head 5.3, neck 1.4, forearm 1.2 |

The `head 5.3%` line is the same in every look: that is the scalp band inside `G`, which the shader takes to the hair
colour (`_ScalpColor` / `_ScalpMap`), not `Suit`. The `forearm/waist ~1.2-1.4%` line is the waist/front (source
trousers region, normally covered by the worn bottoms) plus the forearm under a short-sleeve garment.

**The cause is the mask's region, not a stale colour.** `Meshy_<sex>_Body_mask.png` was classified from the source
figure, and that source wears a **tee, full-length trousers and shoes** — all three are the `G` region. `Suit` is a
single value on the *shirt* row, so it paints the *whole* `G` region: torso, arms *and* legs. Whenever the worn
garment leaves `G` exposed, the clothing colour is painted straight onto bare skin. Two bottoms rows make that
visible on the legs:

| look | worn bottoms | mesh z-extent (measured, Blender) | `shirt.*.Suit` | bare shin render |
|---|---|---|---|---|
| `weevil_bc` | "Weevil's shorts" | `argon_bottoms_weevil_bc_male.fbx` **0.603 – 1.218** (ends above the knee) | `#45454C` = **exactly the shorts' fallback colour** | the shorts' dark grey |
| `bonz_dk` | "Wide shorts" | `argon_bottoms_bonz_dk_male.fbx` **0.600 – 1.218** | `#605A9D` = **exactly the measured shorts colour** | the shorts' violet |
| `mako_dk` | "Fisherman spats" | `argon_bottoms_mako_dk_male.fbx` **0.679 – 1.218** | `"skin"` | **skin** |

(The mesh extents and `Suit` values in this table are the **pre-fix** state on 13 Oct; the fixes are below.)

**Control.** `bottoms_mako_dk` is the same short shape (ends at the knee) and Mako's `Suit` is `"skin"` — his bare
shins render skin. Same mask, same hide path, same mesh length; the only difference is the value in the shirt row.
So the `Suit` value decides what an *uncovered* leg renders, and `"skin"` is right exactly when the character's legs
are actually bare in canon (mako, mai, joey). A hex there is the project's way of colouring an uncovered leg to look
like the worn garment — an approximation that holds only while the garment is short. The honest fix is a garment that
covers the leg for real, which is what Weevil needed.

**Frame check (`leg_probe.py`, top-colour palette per band; the model cannot view images, so this is pixel
statistics).** `weevil_e04_1052.png` is a head-and-shoulders close-up (olive-green jacket `#78A03C` at y270-450,
face at y180-270, background sky `#2864A0`) — the legs are not in the frame, so the design record's "shorts" stands
so his own frame cannot settle it — the owner's word supersedes the design record there: he wears black trousers.
`bonz_e18_1106.png`
does show the lower body, and it is **clothed and grey**: a grey median of `#8A8B8D` over the legs (y560-700, 38 015
grey pixels) and `#8A8B8E` over the tunic (y360-480), with skin `#C8A078` no more than 3.1% (hands). So Bonz's canon
lower half is *long and grey*, while his shipped `bottoms_bonz_dk` ended at z=0.600 and rendered violet.

**Owner correction (same day): Weevil wears long black trousers in DK, not shorts.** So his legs are clothed, and the
`Suit` hex was never the defect — the **mesh length** was: a shorts cut at z=0.603 with the rest of the leg only
*coloured*, not covered. Two wrong turns were tried first and are recorded so they are not repeated — setting the
shirt's `Suit` to `"skin"` (renders bare skin, wrong) and leaving the shorts' hex on a shorts mesh (a trouser sliced
at the knee, which is what "the clothing is clipping all over the skin" actually looks like).

**Fix.** Gave him the real garment.

| file | change |
|---|---|
| `Clothes/argon_bottoms_weevil_bc_male.fbx` | replaced with the long base trouser silhouette (`bottoms_plain_male.fbx`, z **0.186..1.218**); the `.fbx.meta` GUID is untouched, so Unity reimports in place |
| `Clothes/hide_bottoms_weevil_bc_male.png` | rebuilt with `rebuild_hide_maps.py` (`--only bottoms_weevil_bc`), 2048 px hard 0/255; cover **0.105 → 0.152**, `piece_z` now `[0.186, 1.218]` |
| `Clothes/albedo_bottoms_weevil_bc.png` | flat `#45454C` → flat **`#222324`** (the project's dark-trouser black, as `bottoms_duke_bc`) |
| `ClothingCatalog.cs` | `shirt_weevil_bc` `Suit` → `#86B05A` — with the legs covered by the real trouser the only visible `Suit` area is the neck/collar, and his blurb says the jacket is closed to the neck, so the hex is the jacket's own rendered green (its albedo measures `#86B05A`), not the trousers' black; `bottoms_weevil_bc` Name/Blurb "Weevil's shorts / Dark shorts." → "Black trousers / Black trousers."; its fallback `(0.27f,0.27f,0.30f)` → `(0.133f,0.137f,0.141f)` |

A mesh swap is safe here because an FBX's own materials are ignored — the runtime assigns the recolor material and the
per-`id` albedo (which is why `bottoms_bakura_bc` renders its own `#B3ADC2` from the same base trouser mesh) — and
the base trouser is already a known-good graft on this body. Precedent for one silhouette under several `id`s:
`bottoms_bakura_bc` / `bottoms_cargo` / `bottoms_plain` are the same mesh.

**Acceptance (same probes).**

| measure | before (shorts mesh) | after (trouser mesh) |
|---|---|---|
| `render_probe.py`: body drawn | 32.7% | **17.5%** |
| shin zone drawn + undersuit-coloured | **14.1%** | 0.0% (gone) |
| thigh zone drawn + undersuit-coloured | 0.8% | 0.0% (gone) |
| whole visible profile | — | identical to `pegasus_dk` (17.5 / 9.6 / 7.9%) |
| new `hide_bottoms_weevil_bc_male.png` vs the `bottoms_plain` / `bottoms_cargo` / `bottoms_bakura_bc` hides | — | **byte-identical** (frac 0.1519) |

The last row is the strongest: his leg hide is now exactly the accepted base trouser's, so his legs are hidden under
the trousers by the same map the default wardrobe already uses.

**Same defect on Bonz — fixed with the same method.** Bonz is the one case a frame settles outright, and it settles two
things at once: his lower half is *long*, and it is *grey*, not the "Violet shorts" the row claimed.

| measure | reading |
|---|---|
| `bonz_e18_1106.png`, tunic band y360-480: median of grey pixels (max-min channel < 14, mean > 60) | **`#8A8B8E`** — exactly his shipped `shirt_bonz_dk` albedo, so the tunic colour was already right |
| the same frame, leg band y560-700 | **`#8A8B8D`** over 38 015 grey pixels; the same grey, and no `#C8A078` skin anywhere down there |
| the old `bottoms_bonz_dk` albedo | `#605A9D` violet — the stale "Violet shorts" value |
| band width down the frame | wide through the torso, narrowing to a ~150-230 px band at y600-690 with a blue `#608090` element at the feet — legs, ankle-length, blue shoes, **not** a bare or short leg |

| file | change |
|---|---|
| `Clothes/argon_bottoms_bonz_dk_male.fbx` | replaced with the long base trouser silhouette (as Weevil's; `.fbx.meta` untouched) |
| `Clothes/hide_bottoms_bonz_dk_male.png` | rebuilt (`--only bottoms_bonz_dk`); cover **0.105 → 0.152**, `piece_z` `[0.186, 1.218]` |
| `Clothes/albedo_bottoms_bonz_dk.png` | flat `#605A9D` → flat **`#8A8B8D`** (frame-measured) |
| `ClothingCatalog.cs` | `shirt_bonz_dk` `Suit` → `#8A8B8D`; `bottoms_bonz_dk` Name/Blurb "Wide shorts / Violet shorts." → "Grey trousers / Grey trousers."; its fallback `(0.38,0.35,0.62)` → `(0.541,0.545,0.553)` |

Acceptance is identical to Weevil's: `bonz_dk` body drawn **35.2% → 17.5%** (the 14.1% shin and 3.4% thigh gone), the
visible profile now identical to `pegasus_dk` (17.5 / 9.6 / 7.9%), and `hide_bottoms_bonz_dk_male.png` **byte-identical**
to the `bottoms_plain` / `bottoms_weevil_bc` hides (frac 0.1519).

**Still open (all measured, none fixed).**

* **`bottoms_tea_bc`** is `argon_bottoms_tea_bc_female.fbx` z **-0.004 – 1.137** (4831 faces, a full-length
  legging+boot, it even reaches the sole) while the row reads "Short shorts / Dark shorts". Name and mesh disagree;
  which is canon was not read from a frame, so nothing was renamed.
* **The forearm**: under a short-sleeve garment (`bakura_bc`, `joey_bc`) the forearm sits in the source tee's `G`
  region, so a hex `Suit` paints it (`bakura_bc` `#4F6595` on ~1.4% of body area). The general fix is to move the
  body's *forearm* (and the *legs*) out of `G` into `R` in the mask, keeping `G` for the torso, which leaves every
  long-garment look unchanged (hidden) and makes every bare arm/leg skin. It is a mask-island edit whose boundary
  needs visual confirmation, so it was not attempted blind.

---

## 12. The undersuit region also covers the neck — measured, and it is load-bearing

Same root as §11, found while checking Bakura: the mask's `G` (undersuit) region is the **source figure's t-shirt**,
and a t-shirt's *collar* sits on the neck. `ArgonBody_male` is 10455 tris, z 0.000-1.750. Sampled per body zone by
per-face UVs against `Textures/Meshy_male_Body_mask.png` (Blender pixel rows are bottom-up, so `v*height` addresses
the same texel the hide npys address under `(1-v)*height`):

| zone (body coords) | texels | R (skin) | G (undersuit) |
|---|---|---|---|
| head-top, z > 1.55 | 4269 | 0.419 | 0.552 |
| **NECK, 1.40 < z <= 1.55, r < 0.11** | 1506 | 0.169 | **0.831** |
| shoulder, 1.30-1.45, r > 0.13 | 1842 | 0.014 | 0.983 |
| torso, 0.95-1.40, r < 0.16 | 4848 | 0.007 | 0.993 |
| arm, 0.95-1.40, r > 0.16 | 3060 | 0.241 | 0.741 |
| *control* hand/wrist, 0.85-1.00, r > 0.16 | 3777 | **0.959** | 0.040 |

The control matches the shipped zone probe (hand/wrist R 0.905), so the sampling is right. **The neck is 83% the
undersuit region.** Its 465 texels are almost cleanly separable: 2.8% shared with the torso, 8.0% with the head-top
(scalp, already repainted by `_ScalpMap`), and 0% with the shoulder, the arm and the hand.

**It is visible.** Intersecting the neck's texels with each of the 22 male `shirt_*` hide maps — which is exactly what
`SetBodyHide` discards — the neck is **77-93% visible under every shirt**, median **0.802**. Control: the hand/wrist
texels are 0.856 visible under the same hides. So no shipped shirt mesh covers the neck, and every male look renders
the `Suit` hex across it.

**Canon says skin where the collar is low.** `bakura_e16_276.png` (Bakura, his own duel; the left figure of the
two-shot, x330-655): skin runs from the face (40.0% at y305) continuously through chin and neck (13.1% at y355) and
stops at y380; the torso below is a pale *garment* (`#B4B4A8`, 17-41% of the strip) and the lower body stays a pale
garment to the feet with no skin except the hands. So his neck is bare skin and his chest is clothed.

**Why the shared mask was not edited.** `Suit` is one value for the whole `G` region — neck, the chest visible under
an open top, the upper arm, and (until §11) the legs — so it cannot be both "skin" at the neck and "undershirt" on a
visible chest. Repainting the neck texels `G -> R` would fix the bare-necked looks but break the high-collared ones,
where that band is doing the work: `shirt_kaiba_dk` `#26272B` is "over a black **high-neck** shirt",
`shirt_kaiba_bc` `#0B0B10` is "over a black **turtleneck**", `shirt_weevil_bc` is "**closed to the neck**",
`shirt_yugi_dk` / `shirt_yami_dk` `#1F1E2A` is "over the black tank". Those rows read correctly today *because* the
neck band is garment-coloured. Per the colour rule, unread canon keeps the current value, and here the current value
is provably load-bearing, so nothing was changed.

**What the right fix looks like.** The `Suit` values already encode the design intent and are right wherever the
visible `G` is one kind of surface: the sleeveless tops are `"skin"` (`shirt_mako_bc`, `shirt_marik_bc`,
`shirt_duke_bc`) because their only visible `G` is bare arms, and the closed/high collars carry the collar's own hex
(`shirt_weevil_bc` is now the jacket's `#86B05A`). Only looks whose visible `G` spans *both* a garment and bare skin
are compromises — `bakura_bc` (chest `#4F6595` + bare neck/forearm), `keith_dk` (`#82302A` chest + bare forearm),
`rex_bc`, `yugi_dk` / `yami_dk`. They need the neck and forearm separable from the chest, i.e. a *third* region: the
mask's `B` channel is only 0.3% used ("other"), so the honest fix is neck+forearm+legs → their own channel with a
per-look colour (`"skin"` by default, the collar hex where the blurb says high-neck / turtleneck / closed). That is
shader + `OverworldAvatar3d` work, not a texture edit, so it is recorded here measured and unfixed.

---

## 13. Wiring audit, and the one defect it found (2026-10-13)

Owner report: "bits of clothing all over the model, the face is unreadable, and the hair isn't placed right."
That reads as grafted pieces (hair / hats / clothes) landing wrong, so the lookup chain was measured rather than
guessed. `Tools/anime/look_assets_audit.py` (new, run from the repo root, exits non-zero on a miss) checks every
`CharacterLooks` row against what actually ships: hair → `Hair/argon_hair_<id>_<sex>.fbx`, face → `Faces/<id>.png`,
and the six clothing ids → `Clothes/argon_<id>_<sex>.fbx`.

**Result: 27 rows, 0 missing** — every look resolves, so nothing falls back to a default generic shell.

| what was checked | how | result |
|---|---|---|
| face decals | size + alpha coverage | the 22 named decals are all **1024×540, aspect 1.90**, matching `FaceQuad`'s 0.132 × 0.070 quad; alpha 10-46% (a feathered eyes/brows patch) |
| grafted rigs | per-bone rest transforms, base vs each piece | 52/52 shared bone names; every piece's rig is a **uniform +0.176 m** translation of the base's (Hips z 0.920 → 1.096) |
| graft placement | apply `W_host · inv(W_piece)` to the piece's vertices | hair ryou_bakura raw z 1.179-1.889 → **1.003-1.713** (jaw to crown on a 1.707-tall body); trousers raw 0.186-1.218 → **0.010-1.042** (ankles to natural waist) |
| body hides | `SetBodyHide` / `CombineHides` | unconditional, applied to the body renderer only — the one-mesh outfit maps rebuilt in §10 cover that path |
| silhouette path | `ApplySilhouette` vs `ApplyClothes` | swaps *every* material to a flat silhouette and hides the decal; it never runs for a canon duelist |

The rig translation is the designed compensation, not damage: the pieces are authored on a rig whose origin sits
17.6 cm higher and the graft's own bindpose maths lowers them exactly that far. So the grafts are correct.

**The defect found.** The `weevil_bc` row passed the string `"face_weevil_bc"` into the **`facial`** parameter (the
13th argument, the glasses slot) instead of leaving it empty, so a *decal* filename was resolved as a
`Clothes/argon_<id>_<sex>` garment lookup. It silently fell back to `face_none`, so nothing visibly broke — but
`Faces/face_weevil_bc.png` is the one odd asset in that folder: a **720×1280, 100%-opaque raw screencap**, not a
1024×540 feathered patch. Anything that ever did load it as a decal would paint an opaque rectangle over the face.
The stray argument is removed (Weevil wears no facial piece) and the reason is recorded in the row, because the
positional `L(...)` signature puts `facial` immediately after `hands`, where a decal name looks like it belongs.

**Not yet explained by measurement:** which of the three symptoms the owner means. The closest measured match is
still the §12 undersuit region — clothing colour painted on the neck and scalp of *every* look (`head 5.3%`,
`neck 1.4-6.1%`) — with `render_probe.py` (23 Oct) per-look totals: Keith 17.8% of body area in clothing colour,
Bakura 14.5%, Weevil / Bonz / Pegasus / Tristan 7.9% after the §11 fixes.


---

## 14. Yami Bakura: the dark undersuit band over an all-pale outfit (2026-10-13)

Owner narrowed the report to Yami Bakura. His look row builds him from the base body (no `Characters/`
model ships, so `AttachWorld`'s `characterId` path falls through with a warning):

| field | value |
|---|---|
| slot set | `shirt_bakura_bc` + `bottoms_bakura_bc` + `shoes_bakura_bc` (shared with `bakura_bc`) |
| hair / face | `ryou_bakura` (his own mesh does not ship; Ryou's is borrowed) / `yami_bakura` + `_mouth` |
| skin | `#CFAC8D` |
| hat / facial / hands | none, so `ApplyTo` substitutes `DefaultHat` `hat_none` and `DefaultHands` `hands_none`, both empty shells with **no FBX** — nothing extraneous is grafted |

**Measurement 1 — what the coat actually is.** Grafting onto the base rig
(`v_graft = Σ w · W_host · inv(W_piece)`), `argon_shirt_bakura_bc_male.fbx` lands at **z 0.904 – 1.496**:
852 verts in the arm/torso band, 335 in the forearm band, **627 in the neck band**. That is a long,
long-sleeved coat reaching mid-thigh and closing at the throat — while the row read "*Short-sleeve* blue
overshirt".

**Measurement 2 — the colour that was painting him.** `albedo_shirt_bakura_bc.png` is flat **`#8ABED0`**
(a pale blue) and the row's own fallback is `(0.54, 0.75, 0.82)` = `#8ABFD1`, so the row said the coat was
pale twice. But `Suit` — the colour the body's undersuit region takes wherever the coat leaves him
exposed — was **`#4F6595`**, a dark blue-grey: the same row contradicted itself, and exposed skin rendered
dark.

**Measurement 3 — his own frame says pale everywhere.** `bakura_e16_276.png` (he is the left figure of the
two-shot, x 330-655): hair `#BAB4C6`, torso a pale garment `#B4B4A8` (41% of the band at y455), neck **bare
skin** (13.1% at y355, 0.0% at y380-505), lower body a pale garment to the feet. `#4F6595` appears in none
of it — his palette is `#BAB4C6` / `#B4B4A8` / `#B3ADC2` (his trousers, already correct).

The dark band was therefore smeared across the neck, chest, sleeve edges and scalp of an outfit that is pale
from head to foot: a foreign garment-coloured patch on all-pale clothes — the "bits of clothing all over the
model" — reaching the jaw (face unreadable) and into the hairline (hair reads as misplaced).

**Fix.** `shirt_bakura_bc` `Suit` `#4F6595` → **`#8ABED0`**, the coat's own rendered colour, and the row is
renamed from "Open overshirt / Short-sleeve blue overshirt." to "**Bakura's long coat / Long pale coat over a
pale undersuit.**" so name, blurb, fallback, albedo and `Suit` finally agree. `Id` is built from
`SlotPrefix + character + arc`, so a rename cannot break item resolution, and both look rows that use the
piece (`bakura_bc`, `yami_bakura`) pick it up. No geometry, mask, albedo or hide was touched, so the change
is a colour constant on the body's undersuit region only.

**Known and deliberately left alone.** `ApplyScalpColour` falls back to a hard-coded `#2A2A30` for the
body's painted hairline when a look sets no `hairHex` (most rows set `hairPattern: 0`, "the style's own canon
colours"), so pale-haired looks get a dark scalp line under the hair. Setting `hairHex` on a pattern-0 row is
unprecedented and the shader's `_Original` path may then override the style's own texture colours, so it was
not changed blind — a pale-haired look would need its hairline measured against a rendered frame first.

## 15. "The clothing is clipping all over the skin" (third report) — the hide maps were rasterised on a body the game does not draw (2026-10-13)

**Report.** Same words as §10 and §11, on Yami Bakura after the §14 undersuit fix and after the
`ApplyScalpColour` catalogue-root fallback (also landed this day): the pale coat `#8ABED0` and the
lavender jeans `#B3ADC2` still show the body through them in jagged tan plates, and the hairline
still read near-black. The owner's screenshot is the reference (`bakura_hide_before.png`).

**Root cause — two bodies.** The game draws `Assets/Resources/WRLDZ/Avatar3D/argon_base_male.fbx`;
every hide map and every colour-region mask was rasterised on the pipeline body
`build/custom/argon_avatar_male.glb` / `.fbx`. They are different meshes:

| | game body (`argon_base_male.fbx`) | pipeline body (`argon_avatar_male.glb`) |
|---|---|---|
| verts / distinct UVs | 13,380 / 14,517 | 9,238 / 9,237 |
| rest pose | arms out | arms down |
| height / head-bone z | 1.741 m / 1.489 | 1.750 m / 1.582 |
| provenance | committed 2026-10-10, `f93fcb1ed` "Merge restored project into live"; **no tool in `argon-avatar` produces it** | `meshy_rig_body.py` → `custom_body.py` |

The two unwraps are disjoint: **2 of 14,517** UV coordinates coincide. Sampled on the game body the
shipped maps discard the wrong texels — they hide 61% of the torso by accident and still leave
24–46% of the torso / arms / legs drawn, and the old shoe map alone hides 0.0368 m² of neck and
0.0041 m² of hand skin. The garment shells were also modelled and fitted on the pipeline body:
grafted onto the game body with the runtime formula (`v' = Σ w · W_host · inv(W_piece)`, 0 verts
flew) they sit 1–3 cm inside it, so the body pokes through in plates. That is what the screenshot
shows, and why the §11 numbers (measured on the glb) never matched the game.

**Validation of the port.** Running the same coverage test on the *pipeline* body reproduces the
shipped maps almost exactly (torso drawn 0.0341 vs 0.0359; legs 0.0029 vs 0.0031). The test is
faithful; only the body basis was wrong.

**Fix.** `rebuild_hide_unity.py` (kept at `/tmp/rebuild_hide_unity.py`, this run) rebuilds the three
bakura maps against `argon_base_male.fbx`: the pipeline's coverage test (garment within 3 cm above
or 3 cm below the skin point along its normal, 8 mm tangent erosion so the hidden region stops
short of every hem, per-texel resolution where a hem crosses the face, head/neck/hands never
hidden, closed shoes hide the foot wholesale, bottoms/shoes never hide arm skin) **unioned with**
the 5 mm near-shell fill — a drawn face whose centroid sits within 5 mm of the shell is discarded
(the boundary bands the erosion leaves on this body; 96% of the still-drawn leg area sits inside
5 mm of the jeans). Rasterised at 2048 px into the game body's own UV space, written in place as
plain 0/255 grey PNG (the `.meta` GUIDs stay). Body surface still drawn (m², of 1.6687 m²):

| region | no map | shipped maps (wrong basis) | rebuilt maps |
|---|---|---|---|
| torso | 0.3948 | 0.2418 | 0.2713 |
| arms | 0.2981 | 0.1663 | 0.1044 |
| legs | 0.7210 | 0.3892 | **0.0157** |
| feet | 0.0281 | 0.0191 | **0.0000** |
| neck | 0.1772 | 0.1059 | 0.1772 (kept visible) |
| hands | 0.0493 | 0.0380 | 0.0493 (kept visible) |

The torso figure *rises* against the shipped map because the shipped map over-hid by accident
(61% of torso texels discarded on the wrong basis, faces the garment does not cover included). The
rebuilt number is the honest one: 39% of the still-drawn torso lies more than 12 cm from any
shell — that is the coat's open front, which the body mask paints mostly undersuit `#8ABED0`
(§14) and which the frame shows as a pale garment. The forearms stay bare because the coat's
sleeves end at the elbow **on this body** (mesh fit, see below). `bakura_bc` shares the three
pieces and is fixed by the same maps; no other look's map changed. Stills:
`Tools/anime/out/bakura_hide_before.png` / `bakura_hide_after.png`.

**Known and deliberately left alone.**
- **The shared `Meshy_<sex>_Body_mask.png`, the scalp masks and every other look's hide map have
  the same orphan-body problem** — they are all keyed to the pipeline body. The in-game placement
  of the mask still reads correct (face, hairline, hands), so they were left; the root fix is a
  decision for the owner: either reinstall the current pipeline body into Unity (the game then
  draws the body the whole pipeline authors on — every look's silhouette changes) or re-key every
  body-space map to the orphan body. This pass fixes Bakura's maps only.
- The coat's sleeves and the body's forearms are a **mesh-fit** problem, not a hide-map problem:
  hiding the forearms would float the cuffs. Refitting `argon_shirt_bakura_bc_male.fbx` (and the
  other pieces) to the game body is the real fix and belongs with the decision above.
- `argon_base_female.fbx` is presumably the same orphan; no female bakura pieces exist, so nothing
  was rebuilt for female.
- `Tools/anime/audit.py` verdicts are unchanged (`pegasus_dk` exact; the two `close` rows are the
  pre-existing Yami Yugi pair). No `hairHex` was written; Yami's scalp now resolves to
  `ryou_bakura`'s catalogue root `#D7D8DF` at runtime, Yugi's stays his dark root `#131212`.
