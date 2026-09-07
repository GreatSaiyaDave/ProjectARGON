using UnityEngine;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Battle City disk layout for benjohnson789's working full-size kit
    /// (Cults3D) + SPIRIT_DUELER_DISK.md.
    ///
    /// Worn on the <b>left arm</b>. Local disk axes (ZonesRoot):
    ///   +X = along the plate (SMALL/field −X → BIG BLADE tip +X)
    ///   +Y = off the plate face (up)
    ///   +Z = toward the wearer's body. S/T zones add 180° yaw so local +Z
    ///        points out of the outer top-face groove.
    ///
    /// This kit (blades printed S/T-slots-up):
    ///   · Monster stages — five recessed rectangles on the plate top
    ///   · Spell/Trap — five shallow top-face slits on the OUTER rail
    ///   · Field Card Slot — SMALL BLADE tip tab
    ///   · Deck Holder + Graveyard on the hub
    /// </summary>
    public static class ArZoneLayout
    {
        /// <summary>Snap radius — keep under half column pitch so pads don't steal neighbors.</summary>
        public const float SnapRadiusDefault = 0.072f;
        /// <summary>S/T snap — mouth only, so sets do not steal the monster pad.</summary>
        public const float SnapRadiusSlot = 0.055f;
        public const float SnapRadiusFieldDrawer = 0.09f;

        /// <summary>
        /// World width of a monster card on a setting stage.
        /// Straight-blade pad Z span is ~91 mm; portrait height
        /// (× <see cref="ArAnimePresentation.CardAspectY"/>) at 0.046 fills
        /// that rectangle with a thin gutter. Do not size to pad X.
        /// </summary>
        public const float CardScaleOnDisk = 0.046f;
        /// <summary>S/T in the outer groove — slightly shorter so the inner
        /// end does not reach the monster stage.</summary>
        public const float CardScaleInSlot = 0.040f;
        public const float CardScaleField = 0.048f;

        /// <summary>
        /// Fraction of card height at the slit mouth. Higher peek = less card
        /// sliding under the monster pad.
        /// </summary>
        public const float SlotBottomPeekFraction = 0.28f;

        /// <summary>Five monster/S-T column X values (mesh-local surface points).</summary>
        public static readonly float[] ColumnX = ArPlaymatLayout.DiskColumnX;

        // ── Positions (BladePivot / mesh local — ZonesRoot is identity) ──────

        /// <summary>Monster pad i — center of the recessed top-face stage.</summary>
        public static Vector3 MonsterLocal(int index) => ArPlaymatLayout.DiskMonsterLocal(index);

        /// <summary>S/T mouth i — outer top-face groove of the same column.</summary>
        public static Vector3 SpellTrapLocal(int index) => ArPlaymatLayout.DiskSpellTrapLocal(index);

        /// <summary>Field Spell — plate tip surface point.</summary>
        public static Vector3 FieldSpellLocal => ArPlaymatLayout.DiskFieldSpellSurface;

        /// <summary>Yaw so zone +X follows the pad rails (0° — rails stay +X even on the taper).</summary>
        public static Quaternion MonsterZoneRotation(int index) =>
            ArPlaymatLayout.DiskZoneRotation(index, spellTrap: false);

        public static Quaternion SpellTrapZoneRotation(int index) =>
            ArPlaymatLayout.DiskZoneRotation(index, spellTrap: true);

        public static Quaternion FieldSpellZoneRotation() =>
            ArPlaymatLayout.DiskFieldSpellRotation();

        /// <summary>Left Pendulum — hub-side of M1, outside the main five.</summary>
        public static Vector3 PendulumLeftLocal
        {
            get
            {
                var m0 = ArPlaymatLayout.DiskMonsterSurface[0];
                return new Vector3(m0.x - ArPlaymatLayout.DiskMonsterColumnPitch * 0.55f, m0.y, m0.z);
            }
        }

        /// <summary>Right Pendulum — tip-side of M5, outside the main five.</summary>
        public static Vector3 PendulumRightLocal
        {
            get
            {
                var m4 = ArPlaymatLayout.DiskMonsterSurface[4];
                return new Vector3(m4.x + ArPlaymatLayout.DiskMonsterColumnPitch * 0.55f, m4.y, m4.z);
            }
        }

        // ── Deck / GY / Extra — mesh hub (BladePivot local, matches OBJ hub) ──
        // Parent under BladePivot (same space as mesh) so the stack sits in the
        // sculpted deck cradle, not floating on an arbitrary DiskRoot offset.

        /// <summary>Main Deck — the single −X hub magazine (covers the sculpted deck).</summary>
        public static Vector3 MainDeckLocal => ArPlaymatLayout.DiskDeckHubLocal;

        /// <summary>Graveyard tray — beside Main Deck on the hub.</summary>
        public static Vector3 GraveyardLocal => ArPlaymatLayout.DiskGraveyardHubLocal;

        /// <summary>Extra Deck chamber — inboard of GY on the hub.</summary>
        public static Vector3 ExtraDeckLocal => ArPlaymatLayout.DiskExtraDeckHubLocal;

        /// <summary>
        /// Deck-well card size — aliases of <see cref="ArDeckWellCards"/> so
        /// shuffle / draw / hit volumes stay in lockstep with the dedicated assets.
        /// Mesh-local meters (ZonesRoot already applies DiskMeshVisualScale).
        /// </summary>
        public const float DeckCardWidth = ArDeckWellCards.Width;
        public const float DeckCardHeight = ArDeckWellCards.Height;
        public const float DeckCardThickness = ArDeckWellCards.Thickness;
        public const int DeckStackVisibleCards = ArDeckWellCards.LooseCards;
        public const float DeckBrickCards = ArDeckWellCards.BrickCards;

        // ── Card lock poses ──────────────────────────────────────────────────
        // Physical cards (ArPhysicalCardBuilder): art face normal = local +Z.
        // Flush on pad: Rx(−90) maps +Z → +Y (art toward viewer), card sits in zone XY.
        // All zone cards are CENTERED on their zone anchor (no free-float offsets).

        /// <summary>Face-up Attack — portrait, flush on plate, art toward viewer.</summary>
        public static Quaternion FaceUpAttackRot => Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>Face-up Defense — landscape, flush on plate.</summary>
        public static Quaternion FaceUpDefenseRot => Quaternion.Euler(-90f, 0f, 90f);

        /// <summary>Face-down set monster — landscape flush, card-back texture.</summary>
        public static Quaternion FaceDownSetMonsterRot => Quaternion.Euler(-90f, 0f, 90f);

        /// <summary>
        /// Seated face-up S/T — past flat (Rx &lt; −90) so the outer lip lifts into the
        /// slit. The extra 180° yaw puts the card's TITLE (top) edge at the slot mouth
        /// instead of the bottom edge, so the controller can read the name of a face-up
        /// Continuous/Field/Equip card sticking out of the slot for easy self-ID.
        /// (Rx −80 was the wrong tip; it shoved card tops onto M1–M5.)
        /// </summary>
        public static Quaternion SpellTrapInSlotRot => Quaternion.Euler(-100f, 180f, 0f);

        /// <summary>
        /// Face-down set S/T — bottom-edge peek (unchanged): a Set card shows its back, so
        /// there is no title to reveal; keep the original indent seat.
        /// </summary>
        public static Quaternion FaceDownSetSpellTrapRot => Quaternion.Euler(-100f, 0f, 0f);

        /// <summary>Legacy alias (monster set).</summary>
        public static Quaternion FaceDownSetRot => FaceDownSetMonsterRot;

        /// <summary>Field Spell in tip edge drawer — flat in the drawer cavity.</summary>
        public static Quaternion FieldDrawerRot => Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>Ejected / activate pose — card slides out of the hole, tipped up for flip.</summary>
        public static Quaternion SpellTrapEjectedRot => Quaternion.Euler(-50f, 0f, 0f);

        public static Quaternion RotationFor(ArZoneOrientation orient, ArDuelZoneKind kind = ArDuelZoneKind.Monster)
        {
            if (kind == ArDuelZoneKind.SpellTrap)
            {
                return orient == ArZoneOrientation.FaceDownSet
                    ? FaceDownSetSpellTrapRot
                    : SpellTrapInSlotRot;
            }

            if (kind == ArDuelZoneKind.FieldSpell)
                return FieldDrawerRot;

            return orient switch
            {
                ArZoneOrientation.FaceUpDefense => FaceUpDefenseRot,
                ArZoneOrientation.FaceDownSet => FaceDownSetMonsterRot,
                _ => FaceUpAttackRot
            };
        }

        /// <summary>
        /// Local offset of the card root relative to the zone anchor (zone-local units).
        /// Monsters: flush on pad.
        /// S/T pocket (flat <see cref="SpellTrapInSlotRot"/> Rx−90):
        ///   · Body in the slot; <see cref="SlotBottomPeekFraction"/> tip at the mouth.
        /// Pass <paramref name="zoneTf"/> so heights match the lossyScale-compensated card.
        /// </summary>
        public static Vector3 LockOffset(ArDuelZoneKind kind,
            ArZoneOrientation orient = ArZoneOrientation.FaceUpAttack,
            Transform zoneTf = null)
        {
            // Card local uniform scale (zone space) — matches what ApplyFlushLock applies
            var localS = zoneTf != null
                ? LocalScaleForWorldSize(zoneTf, kind).x
                : CardScale(kind);
            var halfThick = ArAnimePresentation.CardThickness * localS * 0.5f;
            if (kind == ArDuelZoneKind.SpellTrap || kind == ArDuelZoneKind.FieldSpell)
            {
                var fullH = ArAnimePresentation.CardAspectY * localS;
                var halfH = fullH * 0.5f;
                var peek = fullH * SlotBottomPeekFraction;
                var centerZ = peek - halfH;
                // Field stays in the tip drawer. S/T: a hair above the groove
                // floor so the lip reads in the slit instead of vanishing in it.
                var slotY = kind == ArDuelZoneKind.FieldSpell ? halfThick : 0.0012f;
                return new Vector3(0f, slotY, centerZ);
            }

            // Monsters sit ON the plate. Raised rails / triangle sit at Y ≈ 0.0023;
            // keep the card underside above that or the mesh z-fights the art.
            var y = halfThick + (orient == ArZoneOrientation.FaceDownSet ? 0.0040f : 0.0035f);
            return new Vector3(0f, y, 0f);
        }

        /// <summary>
        /// Toaster eject pose — card almost fully out of the slot (activate / reveal).
        /// Bottom edge well clear of the mouth so the flip reads.
        /// </summary>
        public static Vector3 SlotEjectedOffset(ArDuelZoneKind kind, Transform zoneTf = null)
        {
            var localS = zoneTf != null
                ? LocalScaleForWorldSize(zoneTf, kind).x
                : CardScale(kind);
            var fullH = ArAnimePresentation.CardAspectY * localS;
            var halfH = fullH * 0.5f;
            // Center just outside mouth so ~95% of card is free of the slot
            return new Vector3(0f, 0.010f, halfH * 0.95f);
        }

        /// <summary>
        /// Present pose: card is fully outside the mouth and lifted above the plate
        /// so the wearer can see it before it goes into the slot.
        /// </summary>
        public static void GetSlotInsertStart(ArDuelZoneKind kind, ArZoneOrientation orient,
            out Vector3 localPos, out Quaternion localRot, out Vector3 localScale,
            Transform zoneTf)
        {
            var endScale = LocalScaleForWorldSize(zoneTf, kind);
            var fullH = ArAnimePresentation.CardAspectY * endScale.x;
            var halfH = fullH * 0.5f;
            // High + out — readable from a top-down disk view, then drops to the mouth
            localPos = new Vector3(0f, 0.078f, halfH * 1.55f);
            localRot = Quaternion.Euler(-36f, 0f, 0f);
            localScale = endScale * 1.12f;
        }

        /// <summary>
        /// Mouth pose: card aligned with the slot, almost fully out, ready to slide in.
        /// </summary>
        public static void GetSlotMouthPose(ArDuelZoneKind kind,
            out Vector3 localPos, out Quaternion localRot, out Vector3 localScale,
            Transform zoneTf)
        {
            var endScale = LocalScaleForWorldSize(zoneTf, kind);
            localPos = SlotEjectedOffset(kind, zoneTf) + new Vector3(0f, 0.004f, 0.006f);
            // Match the seated indent tilt so the last slide is a straight push in
            localRot = SpellTrapInSlotRot;
            localScale = endScale;
        }

        /// <summary>
        /// Rules: Continuous / Field / Equip stay on the disk after activate (reseat face-up).
        /// One-shot Normal/Quick-Play Spells and Normal/Counter Traps leave for GY.
        /// </summary>
        public static bool StaysInSlotAfterActivate(CardInstance card) =>
            card?.Def != null && card.Def.StaysFlatOnFieldWhenActivated;

        public static bool IsSlotKind(ArDuelZoneKind kind) =>
            kind == ArDuelZoneKind.SpellTrap || kind == ArDuelZoneKind.FieldSpell;

        /// <summary>Desired world-space uniform scale for a pad card of this zone kind.</summary>
        public static float CardScale(ArDuelZoneKind kind) => kind switch
        {
            ArDuelZoneKind.SpellTrap => CardScaleInSlot,
            ArDuelZoneKind.FieldSpell => CardScaleField,
            _ => CardScaleOnDisk
        };

        /// <summary>
        /// Local scale that yields <see cref="CardScale"/> in world units under <paramref name="parent"/>.
        /// Compensates blade/zone hierarchy scale so cards stay readable on the plate.
        /// </summary>
        public static Vector3 LocalScaleForWorldSize(Transform parent, ArDuelZoneKind kind)
        {
            var world = CardScale(kind);
            if (parent == null)
                return Vector3.one * world;
            // Uniform compensate so non-uniform blade squash doesn't shear the card
            var ls = parent.lossyScale;
            var parentS = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z), 0.001f);
            return Vector3.one * (world / parentS);
        }

        /// <summary>
        /// Write flush lock pose onto the zone and apply it to a card transform.
        /// </summary>
        public static void ApplyFlushLock(ArDiskZone zone, Transform cardTf, ArZoneOrientation orient)
        {
            if (zone == null || cardTf == null) return;
            zone.LockLocalPosition = LockOffset(zone.Kind, orient, zone.transform);
            zone.LockLocalRotation = RotationFor(orient, zone.Kind);
            zone.LockLocalScale = LocalScaleForWorldSize(zone.transform, zone.Kind);
            if (cardTf.parent != zone.transform)
                cardTf.SetParent(zone.transform, false);
            cardTf.localPosition = zone.LockLocalPosition;
            cardTf.localRotation = zone.LockLocalRotation;
            cardTf.localScale = zone.LockLocalScale;
        }

        public static float SnapRadius(ArDuelZoneKind kind) => kind switch
        {
            ArDuelZoneKind.SpellTrap => SnapRadiusSlot,
            ArDuelZoneKind.FieldSpell => SnapRadiusFieldDrawer,
            _ => SnapRadiusDefault
        };

        public static int ZoneCount(ArDuelZoneKind kind) => kind switch
        {
            ArDuelZoneKind.Monster => 5,
            ArDuelZoneKind.SpellTrap => 5,
            ArDuelZoneKind.FieldSpell => 1,
            ArDuelZoneKind.PendulumLeft => 1,
            ArDuelZoneKind.PendulumRight => 1,
            _ => 0
        };

        /// <summary>
        /// Human-readable Battle City label for a pad (1-based columns, hub → tip).
        /// </summary>
        public static string ZoneDisplayLabel(ArDuelZoneKind kind, int index)
        {
            var n = index + 1;
            return kind switch
            {
                ArDuelZoneKind.Monster => "M" + n,
                ArDuelZoneKind.SpellTrap => "ST" + n,
                ArDuelZoneKind.FieldSpell => "FIELD",
                ArDuelZoneKind.PendulumLeft => "P-L",
                ArDuelZoneKind.PendulumRight => "P-R",
                _ => kind.ToString()
            };
        }

        /// <summary>Full name for debug hierarchy / inspect.</summary>
        public static string ZoneFullName(ArDuelZoneKind kind, int index)
        {
            var n = index + 1;
            return kind switch
            {
                ArDuelZoneKind.Monster => "MonsterCardSlot_" + n,
                ArDuelZoneKind.SpellTrap => "SpellCardSlot_" + n,
                ArDuelZoneKind.FieldSpell => "FieldCardSlot",
                ArDuelZoneKind.PendulumLeft => "PendulumLeft",
                ArDuelZoneKind.PendulumRight => "PendulumRight",
                _ => kind + "_" + index
            };
        }
    }
}
