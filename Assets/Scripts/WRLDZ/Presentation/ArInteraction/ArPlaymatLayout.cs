using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Solid Vision street — every disk play projects a large holo in front of
    /// that duelist. Not a tabletop mat and not on the disk.
    ///
    /// From the player looking toward the opponent:
    /// <code>
    ///   [You + disk]  --few feet--  You S/T at the feet  --  You monsters
    ///       -- street --  Opp monsters  --  Opp S/T at their feet  --  [Opp]
    /// </code>
    ///
    /// S/T = a parallel row of large cards hovering just above the ground.
    /// Monsters = person-scale holos further into the street (clear of the S/T row).
    /// </summary>
    public static class ArPlaymatLayout
    {
        // ── Anime hologram field (in front of the player) ───────────────────

        /// <summary>Center-to-center X between adjacent monster columns (street-scale).</summary>
        public const float MonsterColumnPitch = 1.20f;

        /// <summary>Gap between neighbouring S/T card faces at full hologram scale.</summary>
        public const float SpellTrapFaceGap = 0.10f;

        /// <summary>
        /// Center-to-center X between adjacent S/T holos at full hologram scale: one card face plus a narrow gap, so
        /// the row stands as a tight group round the M3 column (owner 2026-10-04: "grouped tighter together ... so
        /// that the artwork of all activated spells and traps are facing the opponent"). It shrinks with the
        /// holograms (<see cref="LiveSpellTrapColumnPitch"/>). The earlier row used the 1.20 monster pitch,
        /// staggered into the gaps of the monster wall: 4.8 wide, its side cards had to turn to the camera to read.
        /// </summary>
        public const float SpellTrapColumnPitch = 0.58f + SpellTrapFaceGap;   // CardArtFocus.SpellTrapArtworkScale.x

        /// <summary>
        /// Fallback X shift when the disk pose is unknown: toward the body
        /// (right of a left-arm disk) so summons are not out at the blade tip.
        /// Live values are <see cref="PlayerHoloX"/> / <see cref="OppHoloX"/>,
        /// which track the disk blade center.
        /// </summary>
        public const float PlayerFieldXOffset = 0.55f;

        /// <summary>Arena-local X of the player's hologram column center (M3).</summary>
        public static float PlayerHoloX { get; private set; } = PlayerFieldXOffset;

        /// <summary>Arena-local X of the opponent's hologram column center (M3).</summary>
        public static float OppHoloX { get; private set; }

        /// <summary>+1 if disk M1 is to the player's left; −1 if M1 is to their right.</summary>
        public static float PlayerColumnSign { get; private set; } = 1f;

        /// <summary>+1 if opp M1 is on arena −X; −1 if flipped with their disk.</summary>
        public static float OppColumnSign { get; private set; } = 1f;

        /// <summary>
        /// Invisible playmat: M3 in front of the disk blade center, M1–M5 in the
        /// same left/right order as the Battle City pads (disk local −X = M1).
        /// </summary>
        public static bool SetHoloColumns(float playerX, float playerSign, float oppX, float oppSign)
        {
            playerSign = playerSign < 0f ? -1f : 1f;
            oppSign = oppSign < 0f ? -1f : 1f;
            var changed = !Mathf.Approximately(PlayerHoloX, playerX) ||
                          !Mathf.Approximately(OppHoloX, oppX) ||
                          !Mathf.Approximately(PlayerColumnSign, playerSign) ||
                          !Mathf.Approximately(OppColumnSign, oppSign);
            PlayerHoloX = playerX;
            OppHoloX = oppX;
            PlayerColumnSign = playerSign;
            OppColumnSign = oppSign;
            return changed;
        }

        /// <summary>
        /// Distance from the duelist to their S/T row — a few feet in front of
        /// the feet, never on the disk.
        /// </summary>
        public const float DiskToHoloMinGap = 0.85f;

        /// <summary>
        /// How far in front of the S/T row the monster row stands at full street scale.
        /// Clears a set S/T (~0.40 m half-depth) plus a summon pad (~0.45 m radius)
        /// with a visible aisle (~1.25 m).
        /// </summary>
        public const float MonsterStepFromSpellTrap = 2.10f;

        /// <summary>
        /// Minimum |Z| of the monster row from mid at full scale so two DEF sets
        /// do not occupy the same street.
        /// </summary>
        public const float MinMonsterRowFromMid = 0.70f;

        /// <summary>
        /// Design |Z| of the monster row from mid on a field that is exactly
        /// <see cref="DesignHalfSpan"/> wide. Live value is <see cref="LiveMonsterRowFromMid"/>.
        /// </summary>
        public const float MonsterRowFromMid = MinMonsterRowFromMid;

        /// <summary>
        /// Design |Z| of the S/T row from mid (nearer each duelist) on a field
        /// that is exactly <see cref="DesignHalfSpan"/> wide.
        /// </summary>
        public const float SpellTrapRowFromMid = MinMonsterRowFromMid + MonsterStepFromSpellTrap;

        /// <summary>Never let the usable half-field collapse below this (keeps two rows).</summary>
        public const float MinUsableHalf = 0.72f;

        /// <summary>
        /// Half-span that fits stand-off + monster step + mid gap at full hologram size.
        /// Wider streets keep this scale and grow the mid-field aisle.
        /// </summary>
        public const float DesignHalfSpan =
            DiskToHoloMinGap + MonsterStepFromSpellTrap + MinMonsterRowFromMid;

        /// <summary>Ground pad under a standing monster (design meters, scales with the field).</summary>
        public const float SummonRingDiameter = 0.90f;

        /// <summary>Live |Z| of monster row from ArenaRoot mid (updated each arm move).</summary>
        public static float LiveMonsterRowFromMid { get; private set; } = MonsterRowFromMid;

        /// <summary>Live |Z| of S/T row from ArenaRoot mid (updated each arm move).</summary>
        public static float LiveSpellTrapRowFromMid { get; private set; } = SpellTrapRowFromMid;

        /// <summary>Live meters from each duelist to their S/T row.</summary>
        public static float LiveStandOff { get; private set; } = DiskToHoloMinGap;

        /// <summary>Live meters from that S/T row to the monster row (the aisle).</summary>
        public static float LiveStep { get; private set; } = MonsterStepFromSpellTrap;

        /// <summary>
        /// 1 on a street that fits the design half-span; smaller on short duels so
        /// hologram size shrinks with the aisle instead of overlapping it.
        /// </summary>
        public static float LiveHoloScale { get; private set; } = 1f;

        /// <summary>Field Spell zone X offset (left of column 0).</summary>
        public const float FieldSpellX = -2.85f;

        /// <summary>Pendulum X offsets (outside monster columns).</summary>
        public const float PendulumX = 2.85f;

        /// <summary>Monster holo origin at street level (art is lifted to standing height).</summary>
        public const float MonsterHoverY = 0.02f;
        /// <summary>S/T cards hover just above the ground by the player's feet.</summary>
        public const float SpellTrapHoverY = 0.14f;
        public const float FieldHoverY = 0.14f;

        /// <summary>Parent scale for arena holograms so art size tracks the live aisle.</summary>
        public static Vector3 LiveVisualScale => Vector3.one * LiveHoloScale;

        /// <summary>
        /// Player-relative street: S/T a few feet in front of each duelist,
        /// monsters further toward mid with a full row gap. Extra span grows the
        /// mid-field. Short spans scale stand / step / holograms together — they
        /// never collapse the S/T row onto the monsters.
        /// Returns true when live depths or hologram scale changed.
        /// </summary>
        public static bool ApplyAnimeField(float playerToOppDistance)
        {
            var halfSpan = Mathf.Max(MinUsableHalf, playerToOppDistance * 0.5f);
            float stand;
            float step;
            float minMid;
            float scale;
            if (halfSpan >= DesignHalfSpan)
            {
                stand = DiskToHoloMinGap;
                step = MonsterStepFromSpellTrap;
                minMid = MinMonsterRowFromMid;
                scale = 1f;
            }
            else
            {
                scale = Mathf.Clamp(halfSpan / DesignHalfSpan, 0.15f, 1f);
                stand = DiskToHoloMinGap * scale;
                step = MonsterStepFromSpellTrap * scale;
                minMid = MinMonsterRowFromMid * scale;
            }

            var stZ = halfSpan - stand;
            var monZ = stZ - step;
            if (monZ < minMid)
                monZ = minMid;

            // The S/T row sits 40% of the gap closer to the monsters (the aisle goes from 2.10 m to 1.26 m
            // at full scale). The monster row stays; the field-spell slot uses the same Z, so it comes with
            // the row. The disk pads on the arm do not move.
            stZ = monZ + (stZ - monZ) * 0.6f;

            var changed = !Mathf.Approximately(LiveSpellTrapRowFromMid, stZ) ||
                          !Mathf.Approximately(LiveMonsterRowFromMid, monZ) ||
                          !Mathf.Approximately(LiveHoloScale, scale);
            LiveSpellTrapRowFromMid = stZ;
            LiveMonsterRowFromMid = monZ;
            LiveStandOff = halfSpan - stZ;
            LiveStep = stZ - monZ;
            LiveHoloScale = scale;
            return changed;
        }

        static float ColumnX(int zone, float pitch, bool playerSide)
        {
            zone = Mathf.Clamp(zone, 0, 4);
            var sign = playerSide ? PlayerColumnSign : OppColumnSign;
            var center = playerSide ? PlayerHoloX : OppHoloX;
            return (zone - 2) * pitch * sign + center;
        }

        /// <summary>Arena local position for a monster zone (0–4), player or opp side.</summary>
        public static Vector3 MonsterArenaLocal(int zone, bool playerSide)
        {
            var side = playerSide ? -1f : 1f;
            return new Vector3(ColumnX(zone, MonsterColumnPitch, playerSide), MonsterHoverY,
                side * LiveMonsterRowFromMid);
        }

        /// <summary>Live S/T column pitch: the card face plus its gap, at the live hologram scale.</summary>
        public static float LiveSpellTrapColumnPitch => SpellTrapColumnPitch * LiveHoloScale;

        /// <summary>S/T column X: S/T 1-5 in the disk's M1→M5 order, grouped tight round the M3 column.</summary>
        static float SpellTrapColumnX(int zone, bool playerSide) => ColumnX(zone, LiveSpellTrapColumnPitch, playerSide);

        /// <summary>Arena local position for an S/T zone (0–4).</summary>
        public static Vector3 SpellTrapArenaLocal(int zone, bool playerSide)
        {
            var side = playerSide ? -1f : 1f;
            return new Vector3(SpellTrapColumnX(zone, playerSide), SpellTrapHoverY,
                side * LiveSpellTrapRowFromMid);
        }

        public static Vector3 FieldSpellArenaLocal(bool playerSide)
        {
            var side = playerSide ? -1f : 1f;
            var pitch = LiveSpellTrapColumnPitch;
            var st0 = SpellTrapColumnX(0, playerSide);
            var st1 = SpellTrapColumnX(1, playerSide);
            var dir = Mathf.Sign(st0 - st1);
            if (dir == 0f) dir = -1f;
            return new Vector3(st0 + dir * (pitch * 0.55f + 0.08f), FieldHoverY,
                side * LiveSpellTrapRowFromMid);
        }

        public static Vector3 PendulumArenaLocal(int idx)
        {
            // Player-side only (engine mirrors extra zones as needed)
            float x;
            if (idx == 0)
            {
                var m1 = ColumnX(0, MonsterColumnPitch, true);
                var m2 = ColumnX(1, MonsterColumnPitch, true);
                var dir = Mathf.Sign(m1 - m2);
                if (dir == 0f) dir = -1f;
                x = m1 + dir * 0.48f;
            }
            else
            {
                var m5 = ColumnX(4, MonsterColumnPitch, true);
                var m4 = ColumnX(3, MonsterColumnPitch, true);
                var dir = Mathf.Sign(m5 - m4);
                if (dir == 0f) dir = 1f;
                x = m5 + dir * 0.48f;
            }

            return new Vector3(x, FieldHoverY, -LiveMonsterRowFromMid);
        }

        // ── Disk (BattleCityDuelDisk.obj) — mesh-local under BladePivot ───────
        //
        // Kit: benjohnson789 "Working Full Size Yu-Gi-Oh! Battle City Duel Disk"
        //   https://cults3d.com/en/3d-model/gadget/working-full-size-yu-gi-oh-battle-city-duel-disk
        // Assembled Fusion v37, cm→m, Y-up, X-mirrored so the Deck Holder is
        // on the player's right (left-arm wear). OPEN / duel pose.
        //
        //   X −0.3292 .. +0.3139   SMALL BLADE + field (−X) → BIG BLADE tip (+X)
        //   Y −0.0750 .. +0.0415   plate ≈ 0; arm mount hangs −Y
        //   Z −0.0237 .. +0.3074   outer rim → wearer / hub
        //
        // This printable kit's card mechanics (CAD + print notes, not Yugipedia):
        //   · Blades printed with the spell/trap slots facing UP — the S/T
        //     openings are top-face slits on the OUTER rail of each pad.
        //   · 5 monster stages = recessed rectangles on the plate top
        //     (SMALL 2-card + BIG 3-card). Yellow triangles are inner-half
        //     labels only. 8 "card holder edges" clip the divider walls.
        //   · 5 S/T slots = shallow top-face grooves on the OUTER rim
        //     (Z ≈ −0.024 .. 0 on the straight blade). Cards insert from
        //     outside; the pocket is ~24 mm, not an underside toaster well.
        //   · Field spell holder is the tab on the SMALL BLADE tip (−X).
        //   · Deck spring holder + GY on the hub.
        // Official anime V2 puts S/T on the wearer underside — do not retag
        // this mesh to that layout. Empty zones = sculpted mesh only.

        /// <summary>Full mesh AABB min (BladePivot / mesh local).</summary>
        public static readonly Vector3 DiskMeshBoundsMin = new(-0.3292f, -0.0750f, -0.0237f);

        /// <summary>Full mesh AABB max (BladePivot / mesh local).</summary>
        public static readonly Vector3 DiskMeshBoundsMax = new(0.3139f, 0.0415f, 0.3074f);

        /// <summary>
        /// Flat blade plate / monster-stage top (hub is higher; do not place pads there).
        /// Fusion v37 pad faces sit at Y ≈ 0.001.
        /// </summary>
        public const float DiskPlateSurfaceY = 0.0010f;

        /// <summary>
        /// Uniform scale on the disk mesh under BladePivot. v37 is already metres
        /// (Fusion cm × 0.01); keep 1 so zone tags stay 1:1 with the mesh.
        /// </summary>
        public const float DiskMeshVisualScale = 1.00f;

        /// <summary>
        /// Center-to-center X of the three BIG BLADE stages (~92 mm).
        /// SMALL BLADE pitch is similar; M1–M2 follow the hinge taper.
        /// </summary>
        public const float DiskMonsterColumnPitch = 0.092f;

        /// <summary>
        /// Monster Card Zones — the centre of each blade pocket: the 66 × 69 mm Spell/Trap card cavity that runs
        /// under every monster stage, open at the outer rim (the S/T slit). The plate renders see-through, so these
        /// pockets are the rectangles the player reads as the zone spots, and a monster card sits square over its
        /// pocket with the Set card under it (Battle City). Measured on the v37 mesh (pocket floors at −3.2 / −5.2 /
        /// −6.8 mm): the BIG blade's three share z 0.020 at the 92 mm pitch; the SMALL blade's two follow its 26° bend
        /// (<see cref="DiskSmallBladeYawDeg"/>). The old flat-top centres (z 0.038–0.044, M1 z 0.116) sat every card
        /// 18–30 mm toward the wearer, off its spot. M1/M2 = SMALL blade (field-spell end, −X), M3–M5 = BIG blade.
        /// </summary>
        public static readonly Vector3[] DiskMonsterSurface =
        {
            new(-0.2045f, DiskPlateSurfaceY, 0.0861f), // M1 · SMALL tip pocket
            new(-0.1216f, DiskPlateSurfaceY, 0.0461f), // M2 · SMALL join pocket
            new(-0.0040f, DiskPlateSurfaceY, 0.0200f), // M3 · BIG join pocket
            new(0.0880f, DiskPlateSurfaceY, 0.0200f),  // M4 · BIG mid pocket
            new(0.1800f, DiskPlateSurfaceY, 0.0200f),  // M5 · BIG tip pocket
        };

        /// <summary>
        /// S/T slit mouth: this far out from its pocket's centre along the pocket's own outward axis (−zone Z), and
        /// centred on it across (the slit spans the pocket's full 69 mm width). Same on every pad: the pockets are
        /// identical on both blades.
        /// </summary>
        public const float DiskSpellTrapMouthFromPocket = 0.0291f;

        static Vector3 SlotMouthOnPocket(int index)
        {
            var outward = Quaternion.Euler(0f, DiskMonsterEdgeYawDeg[index], 0f) * Vector3.back;
            var p = DiskMonsterSurface[index] + outward * DiskSpellTrapMouthFromPocket;
            return new Vector3(p.x, DiskSpellTrapPocketFloorY, p.z);
        }

        /// <summary>
        /// Yaw of the SMALL BLADE's pads, outer edge and Field Card Slot drawer (measured: the M2 pad floor fits a
        /// 64.5 × 82.5 mm rectangle at 26.0°; the outer edge runs ≈ 26–27°). The inner rail's 19.3° taper
        /// (<see cref="DiskTaperRailSlope"/>) left the M1 / M2 cards ~7° crooked on their pads.
        /// </summary>
        public const float DiskSmallBladeYawDeg = 26f;

        /// <summary>
        /// Unity Y yaw so zone +X follows the pad rails toward the BIG tip.
        /// Unity: +X = (cos y, 0, −sin y). SMALL blade pads sit at <see cref="DiskSmallBladeYawDeg"/>;
        /// the BIG blade is straight.
        /// </summary>
        public static readonly float[] DiskMonsterEdgeYawDeg =
        {
            DiskSmallBladeYawDeg, // M1 · SMALL tip — square on its pad
            DiskSmallBladeYawDeg, // M2 · SMALL join — square on its pad
            0f,     // M3 · BIG blade is straight
            0f,     // M4
            0f      // M5
        };

        /// <summary>
        /// S/T groove floor — just below the plate so the card body is under
        /// the pad, but the lip sits in the top-face slit (visible from above).
        /// −10 mm buried the ends under solid mesh.
        /// </summary>
        public const float DiskSpellTrapPocketFloorY = -0.0035f;

        /// <summary>
        /// Outer-slit mouths (measured groove, not past the rim).
        /// Card peek is along +zone Z from here, in the opening.
        /// </summary>
        public static readonly float[] DiskSpellTrapMouthZ =
        {
            SlotMouthOnPocket(0).z, // ST1 · M1 slit
            SlotMouthOnPocket(1).z, // ST2 · M2 slit
            SlotMouthOnPocket(2).z, // ST3 · M3 slit
            SlotMouthOnPocket(3).z, // ST4 · M4 slit
            SlotMouthOnPocket(4).z, // ST5 · M5 slit
        };

        /// <summary>Legacy name — groove depth below the plate top.</summary>
        public const float DiskSpellTrapUnderPlateY = DiskPlateSurfaceY - DiskSpellTrapPocketFloorY;

        /// <summary>Straight-blade inner (wearer) rail Z.</summary>
        public const float DiskInnerRailZ = 0.091f;

        /// <summary>X where SMALL BLADE leaves the straight BIG BLADE.</summary>
        public const float DiskTaperRailStartX = -0.05f;

        /// <summary>dZ/dX of the SMALL BLADE inner rail (toward the field tip).</summary>
        public const float DiskTaperRailSlope = 0.35f;

        /// <summary>Inner-rail Z at blade X — straight BIG BLADE, then SMALL taper.</summary>
        public static float DiskInnerRailZAt(float x) =>
            x >= DiskTaperRailStartX
                ? DiskInnerRailZ
                : DiskInnerRailZ + (DiskTaperRailStartX - x) * DiskTaperRailSlope;

        /// <summary>
        /// S/T groove mouths — the outer-rim slit of each monster pocket, on that pocket's own along/outer axes
        /// (SMALL blade is hinged), so the Set card slides in under its monster. Was hand-measured per slit, which
        /// left ST3–ST5 2.7 mm and ST1 7.4 mm off their pockets' centre lines.
        /// </summary>
        public static readonly Vector3[] DiskSpellTrapSlots =
        {
            SlotMouthOnPocket(0), // ST1 · M1
            SlotMouthOnPocket(1), // ST2 · M2
            SlotMouthOnPocket(2), // ST3 · M3
            SlotMouthOnPocket(3), // ST4 · M4
            SlotMouthOnPocket(4), // ST5 · M5
        };

        /// <summary>Legacy name — outer groove mouths.</summary>
        public static readonly Vector3[] DiskSpellTrapExterior = DiskSpellTrapSlots;

        /// <summary>
        /// S/T mouth of column i — outer top-face slit of that monster stage.
        /// </summary>
        public static Vector3 SlotMouthFromMonster(int index)
        {
            index = Mathf.Clamp(index, 0, 4);
            return DiskSpellTrapSlots[index];
        }

        /// <summary>S/T mouth for column i (outer groove, paired to monster i).</summary>
        public static Vector3 SpellTrapFromMonster(int index)
        {
            index = Mathf.Clamp(index, 0, 4);
            return DiskSpellTrapSlots[index];
        }

        /// <summary>
        /// Main deck — hub magazine (Fusion v37 DECK FLAP).
        /// Do not seat a second pack in the LP / counter window.
        /// </summary>
        public static readonly Vector3 DiskDeckHubLocal = ArDeckWellCards.WellOrigin;

        /// <summary>
        /// TM1637 4-digit window in the sculpted <c>duel counter</c> housing
        /// (benjohnson789 v37). Mesh vertices of the lip:
        /// X −0.0590…0.0086, Z 0.2070…0.2370, Y top 0.0415.
        /// Centered in that opening, 0.9 mm proud so it is not buried.
        /// </summary>
        public static readonly Vector3 DiskLpWindowLocal = new(-0.0170f, 0.0424f, 0.2220f);

        /// <summary>
        /// LCD lies in the counter housing (planar in XZ). Mesh +Z is the
        /// wearer; text-up is −Z (toward the blade) so 8000 reads upright when
        /// the player looks down at the disk.
        /// </summary>
        public static readonly Vector3 DiskLpWindowEuler = new(-90f, 0f, 0f);

        /// <summary>
        /// Digit glass inside the 67.6 × 30 mm housing. Big-cathode TM1637
        /// 4-digit module is ~50 × 16 mm of lit segments.
        /// </summary>
        public static readonly Vector2 DiskLpWindowSize = new(0.056f, 0.020f);


        /// <summary>
        /// GY tray — the lower rectangular opening on the hub, under the H-slot,
        /// facing the wearer. Fly-to-GY lands here.
        /// </summary>
        public static readonly Vector3 DiskGraveyardHubLocal = new(-0.021f, 0.008f, 0.162f);

        /// <summary>
        /// Extra Deck is not a Battle City V2 well. Invisible engine anchor only,
        /// tucked inboard so it cannot read as a second magazine.
        /// </summary>
        public static readonly Vector3 DiskExtraDeckHubLocal = new(0.000f, -0.030f, 0.220f);

        /// <summary>
        /// Field Spell holder — SMALL BLADE tip tab (past M1, −X). Open drawer. The card seats at the CENTRE of the
        /// kit's Field Card Slot pocket: measured in the zone's own frame the pocket (8.9 x 10.8 cm) is centred
        /// (+0.0172, +0.0008, −0.0021) from the zone origin, so that offset (turned by the zone's yaw) is added here.
        /// The drawer part itself keeps the kit's authored place (SpiritDuelerMechanics offsets from this point).
        /// </summary>
        public static Vector3 DiskFieldSpellSurface =>
            new Vector3(-0.2871f, -0.0132f, 0.1641f)
            + Quaternion.Euler(0f, DiskFieldSpellYawDeg, 0f) * new Vector3(0.0172f, 0.0008f, -0.0021f);

        /// <summary>
        /// Field Card Slot stowed in the plate. The anime drawer slides straight out of
        /// the tip only when a Field Spell is played; this is that same travel, inward.
        /// </summary>
        public static Vector3 DiskFieldSpellClosed
        {
            get
            {
                var outward = Quaternion.Euler(0f, DiskFieldSpellYawDeg, 0f) * Vector3.left;
                return DiskFieldSpellSurface - outward * 0.062f;
            }
        }

        /// <summary>
        /// Field Card Slot yaw — along the SMALL BLADE, the axis the drawer slides on (Unity: outward =
        /// Euler(0, yaw, 0) · −X, toward the tip). −28° slid the drawer sideways off the blade and turned the
        /// Field Spell against the drawer.
        /// </summary>
        public const float DiskFieldSpellYawDeg = DiskSmallBladeYawDeg;

        /// <summary>Column X of the five monster pads (for debug / UI).</summary>
        public static readonly float[] DiskColumnX =
        {
            DiskMonsterSurface[0].x,
            DiskMonsterSurface[1].x,
            DiskMonsterSurface[2].x,
            DiskMonsterSurface[3].x,
            DiskMonsterSurface[4].x
        };

        /// <summary>
        /// Monster zone centre Z on the straight BIG BLADE: the pocket centre (<see cref="DiskMonsterSurface"/>), not
        /// the flat top's 0.044 midline. Nothing reads it; kept in step for debug.
        /// </summary>
        public const float DiskMonsterZ = 0.020f;

        /// <summary>Typical S/T mouth Z (outer slit on the straight blade rim).</summary>
        public const float DiskSpellTrapZ = -0.009f;

        public const float DiskMonsterY = DiskPlateSurfaceY;
        public const float DiskSpellTrapY = DiskSpellTrapPocketFloorY;

        /// <summary>Monster zone anchor in BladePivot / mesh local (on model surface).</summary>
        public static Vector3 DiskMonsterLocal(int index)
        {
            index = Mathf.Clamp(index, 0, 4);
            return DiskMonsterSurface[index];
        }

        /// <summary>
        /// S/T groove mouth for column i — outer top-face slit paired to monster i.
        /// </summary>
        public static Vector3 DiskSpellTrapLocal(int index) => SpellTrapFromMonster(index);

        /// <summary>
        /// Zone local rotation (pad-aligned).
        /// Monsters: local +Z toward the wearer.
        /// S/T: +180° so local +Z points out of the outer top-face groove
        /// (cards insert from the outside on this kit).
        /// </summary>
        public static Quaternion DiskZoneRotation(int index, bool spellTrap = false)
        {
            index = Mathf.Clamp(index, 0, 4);
            var yaw = DiskMonsterEdgeYawDeg[index];
            if (spellTrap)
                yaw += 180f;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Field drawer: opens off the tip edge of the plate.</summary>
        public static Quaternion DiskFieldSpellRotation() =>
            Quaternion.Euler(0f, DiskFieldSpellYawDeg, 0f);

        /// <summary>
        /// Layout invariants for the hologram street. Restores live values after running.
        /// </summary>
        public static string RunSanityChecks()
        {
            var sb = new System.Text.StringBuilder();
            var pass = 0;
            var fail = 0;
            var savedSt = LiveSpellTrapRowFromMid;
            var savedMon = LiveMonsterRowFromMid;
            var savedStand = LiveStandOff;
            var savedStep = LiveStep;
            var savedScale = LiveHoloScale;
            var savedPx = PlayerHoloX;
            var savedPs = PlayerColumnSign;
            var savedOx = OppHoloX;
            var savedOs = OppColumnSign;

            void Check(string name, bool ok, string detail = "")
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("PASS  " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("FAIL  " + name +
                                  (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
                }
            }

            try
            {
                ApplyAnimeField(DesignHalfSpan * 2f);
                Check("Design span: full hologram scale", Mathf.Approximately(LiveHoloScale, 1f),
                    $"scale={LiveHoloScale:0.000}");
                Check("Design span: S/T–monster aisle is 1.26 m (40% closer)",
                    Mathf.Abs(LiveStep - MonsterStepFromSpellTrap * 0.6f) < 0.01f &&
                    Mathf.Abs((LiveSpellTrapRowFromMid - LiveMonsterRowFromMid) - 1.26f) < 0.02f,
                    $"step={LiveStep:0.00} gap={LiveSpellTrapRowFromMid - LiveMonsterRowFromMid:0.00}");
                Check("Design span: monsters sit at min mid",
                    Mathf.Abs(LiveMonsterRowFromMid - MinMonsterRowFromMid) < 0.02f,
                    $"monZ={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(8f);
                Check("Wide street: scale stays 1", Mathf.Approximately(LiveHoloScale, 1f));
                Check("Wide street: extra span goes to mid, not onto S/T",
                    LiveStandOff > DiskToHoloMinGap - 0.01f &&
                    LiveStep > MonsterStepFromSpellTrap * 0.6f - 0.01f &&
                    LiveMonsterRowFromMid > MinMonsterRowFromMid + 0.2f,
                    $"stand={LiveStandOff:0.00} step={LiveStep:0.00} monZ={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(2.5f);
                Check("Standing 2.5 m: aisle stays a real step (not the 0.35 m collapse)",
                    LiveStep > 0.35f && LiveSpellTrapRowFromMid - LiveMonsterRowFromMid > 0.35f,
                    $"step={LiveStep:0.00} gap={LiveSpellTrapRowFromMid - LiveMonsterRowFromMid:0.00}");
                Check("Standing 2.5 m: holos scale down with the street",
                    LiveHoloScale < 0.5f && LiveHoloScale > 0.2f,
                    $"scale={LiveHoloScale:0.00}");

                ApplyAnimeField(4f);
                Check("Street 4.0 m: S/T still further from mid than monsters",
                    LiveSpellTrapRowFromMid > LiveMonsterRowFromMid + 0.5f,
                    $"ST={LiveSpellTrapRowFromMid:0.00} M={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(DesignHalfSpan * 2f);
                foreach (var mine in new[] { true, false })
                {
                    // The S/T row is one tight group centred on the M3 column (both sides).
                    var mid = SpellTrapArenaLocal(2, mine).x;
                    Check($"S/T row centred on M3 ({(mine ? "you" : "opp")})",
                        Mathf.Abs(mid - MonsterArenaLocal(2, mine).x) < 0.002f, $"S/T3 x={mid:0.00}");
                    var width = Mathf.Abs(SpellTrapArenaLocal(4, mine).x - SpellTrapArenaLocal(0, mine).x);
                    Check($"S/T row grouped tight ({(mine ? "you" : "opp")})",
                        width < MonsterColumnPitch * 2.5f, $"S/T1→S/T5={width:0.00}");
                }
                Check("S/T pitch leaves a gap between 0.58 m faces",
                    SpellTrapColumnPitch > 0.58f + 0.08f,
                    $"pitch={SpellTrapColumnPitch:0.00}");
                for (var i = 0; i < 4; i++)
                {
                    var a = SpellTrapArenaLocal(i, true).x;
                    var b = SpellTrapArenaLocal(i + 1, true).x;
                    Check($"S/T {i}→{i + 1}: pitch {SpellTrapColumnPitch:0.00} m",
                        Mathf.Abs(Mathf.Abs(b - a) - SpellTrapColumnPitch) < 0.002f,
                        $"dx={Mathf.Abs(b - a):0.000}");
                }
                {
                    var you0 = SpellTrapArenaLocal(0, true).x;
                    var opp0 = SpellTrapArenaLocal(0, false).x;
                    Check("Opp S/T uses same pitch as You (mirrored)",
                        Mathf.Abs(Mathf.Abs(SpellTrapArenaLocal(1, false).x - opp0) -
                                  Mathf.Abs(SpellTrapArenaLocal(1, true).x - you0)) < 0.002f);
                }

                Check("M1/M2 yaw is square on the SMALL-blade pads",
                    Mathf.Abs(DiskMonsterEdgeYawDeg[0] - DiskSmallBladeYawDeg) < 0.2f &&
                    Mathf.Abs(DiskMonsterEdgeYawDeg[1] - DiskSmallBladeYawDeg) < 0.2f,
                    $"M1={DiskMonsterEdgeYawDeg[0]:0.00} M2={DiskMonsterEdgeYawDeg[1]:0.00} pads={DiskSmallBladeYawDeg:0.00}");
                {
                    // The drawer slides along the small blade toward its tip (−X), not sideways off it.
                    var outward = Quaternion.Euler(0f, DiskFieldSpellYawDeg, 0f) * Vector3.left;
                    var bladeAxis = Quaternion.Euler(0f, DiskSmallBladeYawDeg, 0f) * Vector3.left;
                    Check("Field Card Slot drawer slides along the small blade",
                        Vector3.Dot(outward, bladeAxis) > 0.99f && outward.x < -0.8f,
                        $"outward={outward}");
                }
                {
                    // Each Set card slides in under its own monster: slit centred on the pocket, same depth on all five.
                    var ok = true;
                    var worst = 0f;
                    for (var i = 0; i < 5; i++)
                    {
                        var inv = Quaternion.Inverse(Quaternion.Euler(0f, DiskMonsterEdgeYawDeg[i], 0f));
                        var d = inv * (DiskSpellTrapSlots[i] - DiskMonsterSurface[i]);
                        var err = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(-d.z - DiskSpellTrapMouthFromPocket));
                        worst = Mathf.Max(worst, err);
                        ok &= err < 0.0005f;
                    }

                    Check("S/T slits are centred on their monster pockets", ok, $"worst={worst * 1000f:0.0} mm");
                }
                Check("M3–M5 yaw is square on the straight BIG blade",
                    Mathf.Abs(DiskMonsterEdgeYawDeg[2]) < 0.05f &&
                    Mathf.Abs(DiskMonsterEdgeYawDeg[3]) < 0.05f &&
                    Mathf.Abs(DiskMonsterEdgeYawDeg[4]) < 0.05f,
                    $"M3={DiskMonsterEdgeYawDeg[2]:0.00}");

                Check("Hand fan: 2-card rest bodies do not overlap",
                    !ArHandVolume.RestBodiesOverlap(0, 1, 2, 0.22f));
                Check("Hand fan: 6-card rest bodies do not overlap",
                    !ArHandVolume.RestBodiesOverlap(0, 1, 6, 0.22f) &&
                    !ArHandVolume.RestBodiesOverlap(2, 3, 6, 0.22f) &&
                    !ArHandVolume.RestBodiesOverlap(4, 5, 6, 0.22f));
                Check("Hand fan: 8-card rest bodies do not overlap",
                    !ArHandVolume.RestBodiesOverlap(0, 1, 8, 0.22f) &&
                    !ArHandVolume.RestBodiesOverlap(3, 4, 8, 0.22f));
                Check("Hand fan: spacing exceeds projected body width",
                    ArHandVolume.RestCenterSpacing(6, 0.22f) >
                    ArHandVolume.ProjectedWidth(0.22f, 16f) + 0.001f);

                // Mr. Referobot stood in the aisle beside M3 and blocked the holograms.
                foreach (var span in new[] { 2.5f, DesignHalfSpan * 2f, 8f })
                {
                    ApplyAnimeField(span);
                    var margin = float.MaxValue;
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        SetHoloColumns(-0.2f * sign, sign, 0.22f * sign, sign);
                        for (var p = 0; p < ReferobotWorldBody.PostCount; p++)
                            margin = Mathf.Min(margin,
                                NearestZoneDistance(ReferobotWorldBody.Post(p)) - ReferobotWorldBody.ZoneClearance);
                    }

                    Check($"Referobot {span:0.0} m street: every sideline post clears all hologram zones",
                        margin > 0f, $"margin={margin:0.00}");
                }
            }
            finally
            {
                LiveSpellTrapRowFromMid = savedSt;
                LiveMonsterRowFromMid = savedMon;
                LiveStandOff = savedStand;
                LiveStep = savedStep;
                LiveHoloScale = savedScale;
                SetHoloColumns(savedPx, savedPs, savedOx, savedOs);
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }

        /// <summary>Flat distance from an arena-local point to the nearest zone centre of either player.</summary>
        static float NearestZoneDistance(Vector3 p)
        {
            var best = float.MaxValue;

            void Try(Vector3 zone)
            {
                var dx = zone.x - p.x;
                var dz = zone.z - p.z;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            for (var zone = 0; zone < 5; zone++)
            {
                Try(MonsterArenaLocal(zone, true));
                Try(MonsterArenaLocal(zone, false));
                Try(SpellTrapArenaLocal(zone, true));
                Try(SpellTrapArenaLocal(zone, false));
            }

            Try(FieldSpellArenaLocal(true));
            Try(FieldSpellArenaLocal(false));
            Try(PendulumArenaLocal(0));
            Try(PendulumArenaLocal(1));
            return best;
        }
    }
}
