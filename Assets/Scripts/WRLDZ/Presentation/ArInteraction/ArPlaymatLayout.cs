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

        /// <summary>
        /// S/T columns match monster pitch so each spell sits behind its monster,
        /// not clustered into the infield.
        /// </summary>
        public const float SpellTrapColumnPitch = MonsterColumnPitch;

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

            var changed = !Mathf.Approximately(LiveSpellTrapRowFromMid, stZ) ||
                          !Mathf.Approximately(LiveMonsterRowFromMid, monZ) ||
                          !Mathf.Approximately(LiveHoloScale, scale);
            LiveSpellTrapRowFromMid = stZ;
            LiveMonsterRowFromMid = monZ;
            LiveStandOff = stand;
            LiveStep = step;
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

        /// <summary>Arena local position for an S/T zone (0–4).</summary>
        public static Vector3 SpellTrapArenaLocal(int zone, bool playerSide)
        {
            var side = playerSide ? -1f : 1f;
            return new Vector3(ColumnX(zone, SpellTrapColumnPitch, playerSide), SpellTrapHoverY,
                side * LiveSpellTrapRowFromMid);
        }

        public static Vector3 FieldSpellArenaLocal(bool playerSide)
        {
            var side = playerSide ? -1f : 1f;
            var m1 = ColumnX(0, MonsterColumnPitch, playerSide);
            var m2 = ColumnX(1, MonsterColumnPitch, playerSide);
            var dir = Mathf.Sign(m1 - m2);
            if (dir == 0f) dir = -1f;
            return new Vector3(m1 + dir * 0.48f, FieldHoverY, side * LiveSpellTrapRowFromMid);
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
        /// Monster stages — geometric center of each recessed top-face
        /// rectangle (not the triangle decal, not the outer S/T groove).
        /// M1/M2 = SMALL 2-card BLADE (field-spell end, −X). M3–M5 = BIG
        /// BLADE (dividers at X ≈ 0.042 and 0.134). Straight-blade pad
        /// floor Z ≈ 0.022 … 0.090; center Z = 0.056 (not the triangle).
        /// </summary>
        public static readonly Vector3[] DiskMonsterSurface =
        {
            new(-0.2000f, DiskPlateSurfaceY, 0.1100f), // M1 · SMALL tip stage (nudged off inner rail)
            new(-0.1110f, DiskPlateSurfaceY, 0.0650f), // M2 · SMALL join stage (not the triangle)
            new(-0.0050f, DiskPlateSurfaceY, 0.0380f), // M3 · BIG join stage (nudged off inner rail)
            new(0.0870f, DiskPlateSurfaceY, 0.0380f),  // M4 · BIG mid stage (nudged off inner rail)
            new(0.1790f, DiskPlateSurfaceY, 0.0380f),  // M5 · BIG tip stage (nudged off inner rail)
        };

        /// <summary>
        /// Unity Y yaw so zone +X follows each pad's divider-wall normal
        /// (along the blade toward the BIG tip). Unity: +X = (cos y, 0, −sin y).
        /// SMALL BLADE is hinged; those yaws are opposite the previous sign
        /// (negative yaw twisted M1/M2 against the rails).
        /// </summary>
        public static readonly float[] DiskMonsterEdgeYawDeg =
        {
            27f, // M1 · SMALL tip — triangle/pad PCA
            26f, // M2 · SMALL join — triangle/pad PCA
            2f,  // M3 · BIG join — triangle/pad PCA
            2f,  // M4 · BIG mid — triangle/pad PCA
            2f   // M5 · BIG tip — triangle/pad PCA
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
            0.0609f,  // ST1 · M1 slit
            0.0227f,  // ST2 · M2 slit
            -0.0091f, // ST3 · M3 slit
            -0.0094f, // ST4 · M4 slit
            -0.0089f, // ST5 · M5 slit
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
        /// S/T groove mouths — one outer-rim slit per signed-off monster pad,
        /// on that pad's own along/outer axes (SMALL blade is hinged).
        /// </summary>
        public static readonly Vector3[] DiskSpellTrapSlots =
        {
            new(-0.2250f, DiskSpellTrapPocketFloorY, 0.0609f),  // ST1 · M1
            new(-0.1316f, DiskSpellTrapPocketFloorY, 0.0227f),  // ST2 · M2
            new(-0.0068f, DiskSpellTrapPocketFloorY, -0.0091f), // ST3 · M3
            new(0.0853f, DiskSpellTrapPocketFloorY, -0.0094f),  // ST4 · M4
            new(0.1774f, DiskSpellTrapPocketFloorY, -0.0089f),  // ST5 · M5
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
        /// Phase CTAs share the LP counter's hub band (same Y/Z, same facing).
        /// They sit +X of the LCD (screen-left of the deck, away from the magazine).
        /// 0 = BATTLE, 1 = MAIN 2, 2 = END.
        /// </summary>
        public static Vector3 DiskPhaseRailLocal(int index)
        {
            index = Mathf.Clamp(index, 0, 2);
            var lp = DiskLpWindowLocal;
            var gap = 0.006f;
            var halfChip = DiskPhaseChipSize.x * 0.5f;
            var first = lp.x + DiskLpWindowSize.x * 0.5f + gap + halfChip;
            var pitch = DiskPhaseChipSize.x + gap;
            return new Vector3(first + index * pitch, lp.y, lp.z);
        }

        /// <summary>Same facing as the LP glass (no pad yaw).</summary>
        public static float DiskPhaseRailYawDeg(int index) => 0f;

        /// <summary>World size of one phase chip — matches LP glass height.</summary>
        public static readonly Vector2 DiskPhaseChipSize = new(0.040f, 0.020f);

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

        /// <summary>Field Spell holder — SMALL BLADE tip tab (past M1, −X).</summary>
        public static Vector3 DiskFieldSpellSurface =>
            new(-0.2871f, -0.0132f, 0.1641f);

        /// <summary>Field zone yaw (matches the tip taper).</summary>
        public const float DiskFieldSpellYawDeg = -28f;

        /// <summary>Column X of the five monster pads (for debug / UI).</summary>
        public static readonly float[] DiskColumnX =
        {
            DiskMonsterSurface[0].x,
            DiskMonsterSurface[1].x,
            DiskMonsterSurface[2].x,
            DiskMonsterSurface[3].x,
            DiskMonsterSurface[4].x
        };

        /// <summary>Typical monster stage-center Z (straight BIG BLADE).</summary>
        public const float DiskMonsterZ = 0.056f;

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
                Check("Design span: S/T–monster aisle is the design step",
                    Mathf.Abs(LiveStep - MonsterStepFromSpellTrap) < 0.01f,
                    $"step={LiveStep:0.00}");
                Check("Design span: monsters sit at min mid",
                    Mathf.Abs(LiveMonsterRowFromMid - MinMonsterRowFromMid) < 0.02f,
                    $"monZ={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(8f);
                Check("Wide street: scale stays 1", Mathf.Approximately(LiveHoloScale, 1f));
                Check("Wide street: extra span goes to mid, not onto S/T",
                    LiveStandOff > DiskToHoloMinGap - 0.01f &&
                    LiveStep > MonsterStepFromSpellTrap - 0.01f &&
                    LiveMonsterRowFromMid > MinMonsterRowFromMid + 0.2f,
                    $"stand={LiveStandOff:0.00} step={LiveStep:0.00} monZ={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(2.5f);
                Check("Standing 2.5 m: aisle stays a real step (not the 0.35 m collapse)",
                    LiveStep > 0.55f && LiveSpellTrapRowFromMid - LiveMonsterRowFromMid > 0.55f,
                    $"step={LiveStep:0.00} gap={LiveSpellTrapRowFromMid - LiveMonsterRowFromMid:0.00}");
                Check("Standing 2.5 m: holos scale down with the street",
                    LiveHoloScale < 0.5f && LiveHoloScale > 0.2f,
                    $"scale={LiveHoloScale:0.00}");

                ApplyAnimeField(4f);
                Check("Street 4.0 m: S/T still further from mid than monsters",
                    LiveSpellTrapRowFromMid > LiveMonsterRowFromMid + 0.7f,
                    $"ST={LiveSpellTrapRowFromMid:0.00} M={LiveMonsterRowFromMid:0.00}");

                ApplyAnimeField(DesignHalfSpan * 2f);
                for (var i = 0; i < 5; i++)
                {
                    var mx = MonsterArenaLocal(i, true).x;
                    var sx = SpellTrapArenaLocal(i, true).x;
                    Check($"Column {i}: S/T X matches monster X",
                        Mathf.Abs(mx - sx) < 0.001f,
                        $"M={mx:0.00} ST={sx:0.00}");
                }
            }
            finally
            {
                LiveSpellTrapRowFromMid = savedSt;
                LiveMonsterRowFromMid = savedMon;
                LiveStandOff = savedStand;
                LiveStep = savedStep;
                LiveHoloScale = savedScale;
            }

            sb.AppendLine($"--- {pass} passed, {fail} failed ---");
            return sb.ToString();
        }
    }
}
