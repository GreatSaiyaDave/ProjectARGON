using System.Collections;
using UnityEngine;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// One official zone on the left-arm Battle City disk.
    /// AR: invisible spawn anchor (trigger collider only). Cards appear when played.
    /// Snap target for hand cards; holds locked local pose for disk card visuals.
    /// </summary>
    public class ArDiskZone : MonoBehaviour
    {
        public ArDuelZoneKind Kind;
        public int Index;
        public bool IsPlayerSide = true;
        public float SnapRadius = ArZoneLayout.SnapRadiusDefault;

        /// <summary>Local lock pose for a card on this zone.</summary>
        public Vector3 LockLocalPosition;
        public Quaternion LockLocalRotation = Quaternion.identity;
        public Vector3 LockLocalScale = Vector3.one * ArZoneLayout.CardScaleOnDisk;

        public CardInstance Occupant;
        public ArFloatingCard LockedCard;
        Coroutine _slotInsertCo;
        /// <summary>While &gt; Time.time, LateUpdate will not stomp an insert tween.</summary>
        public float SeatAnimUntil;
        Transform _marker;
        Transform _label;
        Transform _legalGlow;
        MeshRenderer _legalMr;

        /// <summary>
        /// Editor/debug only. Live play never instantiates pad geometry —
        /// empty zones are the sculpted mesh. Do not turn this on for shipping.
        /// </summary>
        public static bool AlwaysShowZoneMarkers;

        /// <summary>
        /// Always-on zone identity labels (M1–M5, ST1–ST5, FIELD). Off by default so
        /// the plate stays clean; enable in lab when verifying layout.
        /// </summary>
        public static bool AlwaysShowZoneLabels = false;

        public static ArDiskZone Create(Transform parent, ArDuelZoneKind kind, int index,
            Vector3 localPos, Color accent, int layer, bool playerSide = true,
            Quaternion? localRot = null)
        {
            var go = new GameObject(ArZoneLayout.ZoneFullName(kind, index));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // Monster/S-T pads follow the disc's angled outer edge (not axis-aligned)
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.layer = layer;

            var zone = go.AddComponent<ArDiskZone>();
            zone.Kind = kind;
            zone.Index = index;
            zone.IsPlayerSide = playerSide;
            zone.SnapRadius = ArZoneLayout.SnapRadius(kind);
            zone.LockLocalPosition = ArZoneLayout.LockOffset(kind, ArZoneOrientation.FaceUpAttack, go.transform);
            zone.LockLocalRotation = ArZoneLayout.RotationFor(ArZoneOrientation.FaceUpAttack, kind);
            zone.LockLocalScale = ArZoneLayout.LocalScaleForWorldSize(go.transform, kind);

            // Empty zones are the sculpted mesh only — no preview plates, lips, or labels.
            // Cards appear when played. Debug overlays stay opt-in.
            if (AlwaysShowZoneMarkers)
            {
                zone._marker = BuildZoneMarker(go.transform, kind, accent, layer);
                zone._marker.gameObject.SetActive(true);
            }

            if (AlwaysShowZoneLabels)
            {
                zone._label = BuildZoneLabel(go.transform, kind, index, accent, layer);
                zone._label.gameObject.SetActive(true);
            }

            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = zone.SnapRadius;
            // S/T toaster: pick volume on the bottom lip that sticks out of the hole
            col.center = ArZoneLayout.IsSlotKind(kind)
                ? new Vector3(0f, 0.0f, 0.028f)
                : Vector3.zero;

            return zone;
        }

        static Transform BuildZoneMarker(Transform parent, ArDuelZoneKind kind, Color accent, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ZoneMarker";
            go.transform.SetParent(parent, false);
            ArObjectUtil.Destroy(go.GetComponent<Collider>());
            go.layer = layer;

            go.transform.localPosition = new Vector3(0f, 0.0005f, 0f);
            // Match smaller pad cards + wider column pitch (clear gutters between zones)
            if (kind == ArDuelZoneKind.Monster || kind == ArDuelZoneKind.PendulumLeft ||
                kind == ArDuelZoneKind.PendulumRight)
                go.transform.localScale = new Vector3(0.095f, 0.0025f, 0.088f);
            else if (kind == ArDuelZoneKind.SpellTrap)
                // Thin mouth lip only (not a second full pad)
                go.transform.localScale = new Vector3(0.085f, 0.004f, 0.018f);
            else if (kind == ArDuelZoneKind.FieldSpell)
                go.transform.localScale = new Vector3(0.045f, 0.006f, 0.070f);
            else
                go.transform.localScale = new Vector3(0.08f, 0.002f, 0.08f);

            var mr = go.GetComponent<MeshRenderer>();
            var dim = Color.Lerp(new Color(0.06f, 0.08f, 0.12f, 0.9f), accent, 0.4f);
            mr.sharedMaterial = ArFieldMaterials.Get(dim);
            return go.transform;
        }

        /// <summary>
        /// Floating TextMesh label so each Battle City part is identifiable on the mesh
        /// (Monster Card Slots M1–M5, Spell Card Slots ST1–ST5, Field, Pendulum).
        /// </summary>
        static Transform BuildZoneLabel(Transform parent, ArDuelZoneKind kind, int index,
            Color accent, int layer)
        {
            var go = new GameObject("ZoneLabel");
            go.transform.SetParent(parent, false);
            go.layer = layer;

            // Monsters: label above pad. S/T: only on the peek lip (outside the hole).
            if (kind == ArDuelZoneKind.SpellTrap || kind == ArDuelZoneKind.FieldSpell)
                go.transform.localPosition = new Vector3(0f, 0.006f, 0.028f);
            else
                go.transform.localPosition = new Vector3(0f, 0.022f, 0f);

            // Face up toward viewer (plate is XY after zone yaw; Rx so text lies on plate)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var tm = go.AddComponent<TextMesh>();
            tm.text = ArZoneLayout.ZoneDisplayLabel(kind, index);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = kind == ArDuelZoneKind.FieldSpell ? 0.0042f : 0.0036f;
            tm.fontStyle = FontStyle.Bold;
            tm.color = Color.Lerp(Color.white, accent, 0.35f);
            // Soft dark outline via mesh shadow not available; bright text on dark disk
            if (tm.GetComponent<MeshRenderer>() is { } mr)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            return go.transform;
        }

        /// <summary>Show pad marker while dragging a legal drop (hidden otherwise in live play).</summary>
        public void SetHot(bool hot)
        {
            if (_marker == null) return;
            _marker.gameObject.SetActive(AlwaysShowZoneMarkers || hot);
        }

        static readonly Color LegalHover = new(0.55f, 1f, 0.65f, 0.55f);
        static readonly Color LegalMonster = new(0.25f, 0.90f, 1f, 0.38f);
        static readonly Color LegalSpell = new(0.40f, 1f, 0.50f, 0.38f);
        bool _legalOn;
        bool _legalHover;

        /// <summary>
        /// Engine-legal placement glass. Off when no card is selected.
        /// Mostly transparent so the sculpted plate stays the board.
        /// </summary>
        public void SetLegalHighlight(bool on, bool hover = false)
        {
            _legalOn = on;
            _legalHover = hover;
            if (!on)
            {
                if (_legalGlow != null)
                    _legalGlow.gameObject.SetActive(false);
                return;
            }

            EnsureLegalGlow();
            _legalGlow.gameObject.SetActive(true);
            ApplyLegalGlowColor();
        }

        void ApplyLegalGlowColor()
        {
            if (_legalMr == null) return;
            var baseCol = Kind == ArDuelZoneKind.Monster ||
                          Kind == ArDuelZoneKind.PendulumLeft ||
                          Kind == ArDuelZoneKind.PendulumRight
                ? LegalMonster
                : LegalSpell;
            var col = _legalHover ? LegalHover : baseCol;
            if (!_legalHover)
                col.a = 0.32f + 0.14f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.4f));
            _legalMr.sharedMaterial = ArFieldMaterials.GetTransparent(col);
        }

        void EnsureLegalGlow()
        {
            if (_legalGlow != null) return;
            // Thin plate sitting just above the sculpted pad — cubes survive URP better than quads.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "LegalGlow";
            go.transform.SetParent(transform, false);
            var existingCol = go.GetComponent<Collider>();
            if (existingCol != null)
            {
                existingCol.isTrigger = true;
            }

            go.layer = gameObject.layer;
            if (Kind == ArDuelZoneKind.SpellTrap || Kind == ArDuelZoneKind.FieldSpell)
            {
                go.transform.localPosition = new Vector3(0f, 0.010f, 0.020f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(0.088f, 0.008f, 0.034f);
            }
            else
            {
                go.transform.localPosition = new Vector3(0f, 0.010f, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(0.108f, 0.008f, 0.098f);
            }

            _legalMr = go.GetComponent<MeshRenderer>();
            _legalMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _legalMr.receiveShadows = false;
            var pad = go.AddComponent<ArLegalZonePad>();
            pad.Kind = Kind switch
            {
                ArDuelZoneKind.Monster => WRLDZ.Duel.Rules.RulesZoneKind.Monster,
                ArDuelZoneKind.SpellTrap => WRLDZ.Duel.Rules.RulesZoneKind.SpellTrap,
                ArDuelZoneKind.FieldSpell => WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell,
                ArDuelZoneKind.PendulumLeft => WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft,
                ArDuelZoneKind.PendulumRight => WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight,
                _ => WRLDZ.Duel.Rules.RulesZoneKind.Monster
            };
            pad.Index = Index;
            pad.OnDisk = true;
            _legalGlow = go.transform;
            go.SetActive(false);
        }

        /// <summary>Toggle zone identity label (M1 / ST1 / …).</summary>
        public void SetLabelVisible(bool visible)
        {
            if (_label == null) return;
            _label.gameObject.SetActive(AlwaysShowZoneLabels && visible);
        }

        public bool IsEmpty => Occupant == null && LockedCard == null;

        public bool IsWithinSnap(Vector3 worldPoint) =>
            Vector3.Distance(transform.position, worldPoint) <= SnapRadius;

        /// <summary>
        /// World point of the S/T peek lip (the bit sticking out of the slot).
        /// Monsters / field return the pad center.
        /// </summary>
        public Vector3 WorldPeekPoint
        {
            get
            {
                if (!ArZoneLayout.IsSlotKind(Kind))
                    return transform.position;
                var localS = ArZoneLayout.LocalScaleForWorldSize(transform, Kind).x;
                var peek = ArAnimePresentation.CardAspectY * localS *
                           Mathf.Max(0f, ArZoneLayout.SlotBottomPeekFraction);
                return transform.TransformPoint(new Vector3(0f, 0f, peek * 0.55f));
            }
        }

        /// <summary>
        /// Snap a hand card onto this zone. Monsters: flush on pad.
        /// S/T / Field: toaster insert under the monster / tip drawer.
        /// </summary>
        public void SnapLock(ArFloatingCard card, ArZoneOrientation orient)
        {
            if (card == null) return;
            LockedCard = card;
            Occupant = card.Card;
            card.IsLockedToZone = true;
            card.LockedZone = this;
            RefreshFlushLock(orient);
            var showFace = orient != ArZoneOrientation.FaceDownSet;
            // Face-down S/T sets always show the back on the disk (physical set look)
            if (ArZoneLayout.IsSlotKind(Kind))
                showFace = orient != ArZoneOrientation.FaceDownSet;
            card.RefreshArtFace(showFace);

            if (ArZoneLayout.IsSlotKind(Kind))
            {
                // Keep the hand-card world pose. Field controller owns the toaster insert
                // so we do not start a second tween that Sync would destroy mid-slide.
                if (_slotInsertCo != null)
                {
                    StopCoroutine(_slotInsertCo);
                    _slotInsertCo = null;
                }

                card.transform.SetParent(transform, true);
                SeatAnimUntil = Time.unscaledTime + ArDiskMotion.SlotInsertHold;
            }
            else
            {
                card.transform.SetParent(transform, false);
                // Monsters: slow pad snap (set = face-down landscape + back texture)
                ArZoneLayout.ApplyFlushLock(this, card.transform, orient);
                // Start slightly above so the settle is visible
                card.transform.localPosition = LockLocalPosition + new Vector3(0f, 0.06f, 0f);
                card.transform.localScale = LockLocalScale * 1.08f;
                card.AnimateSnap(LockLocalPosition, LockLocalRotation, LockLocalScale,
                    duration: ArDiskMotion.PadSnap);
            }
        }

        /// <summary>Anime: card approaches then slides into the under-monster S/T toaster.</summary>
        IEnumerator SlotInsertRoutine(ArFloatingCard card, ArZoneOrientation orient)
        {
            if (card == null) yield break;
            ArZoneLayout.GetSlotInsertStart(Kind, orient, out var startP, out var startR, out var startS,
                transform);
            card.transform.localPosition = startP;
            card.transform.localRotation = startR;
            card.transform.localScale = startS;

            ArZoneLayout.GetSlotMouthPose(Kind, out var mouthP, out var mouthR, out var mouthS,
                transform);
            yield return ArDiskMotion.VisibleLerp(card.transform, startP, mouthP, startR, mouthR,
                startS, mouthS, ArDiskMotion.SlotInsertPresent);
            yield return ArDiskMotion.SlotSlideLerp(card.transform, mouthP, LockLocalPosition, mouthR,
                LockLocalRotation, mouthS, LockLocalScale, ArDiskMotion.SlotInsertSlide);

            if (card != null)
            {
                card.transform.localPosition = LockLocalPosition;
                card.transform.localRotation = LockLocalRotation;
                card.transform.localScale = LockLocalScale;
            }

            _slotInsertCo = null;
        }

        /// <summary>Recompute flush lock pose (scale compensates parent hierarchy).</summary>
        public void RefreshFlushLock(ArZoneOrientation orient)
        {
            // Pass transform so S/T 10% bottom-peek is correct under ZonesRoot scale
            LockLocalPosition = ArZoneLayout.LockOffset(Kind, orient, transform);
            LockLocalRotation = ArZoneLayout.RotationFor(orient, Kind);
            LockLocalScale = ArZoneLayout.LocalScaleForWorldSize(transform, Kind);
        }

        /// <summary>
        /// Retire a just-snapped floating visual so <see cref="ArDiskCardVisual"/> owns the pad.
        /// Keeps <see cref="Occupant"/> (engine-synced).
        /// </summary>
        public void ReleaseFloatingKeepOccupant()
        {
            if (LockedCard == null) return;
            var fc = LockedCard;
            fc.IsLockedToZone = false;
            fc.LockedZone = null;
            LockedCard = null;
            if (fc != null && fc.gameObject != null)
                ArObjectUtil.Destroy(fc.gameObject);
        }

        public void ClearLock()
        {
            if (LockedCard != null)
            {
                LockedCard.IsLockedToZone = false;
                LockedCard.LockedZone = null;
            }

            LockedCard = null;
            Occupant = null;
        }

        /// <summary>
        /// Re-glue every pad visual each frame so cards stay flush through blade scale FX.
        /// Recomputes lossyScale-compensated local scale so pad cards never vanish.
        /// </summary>
        void LateUpdate()
        {
            if (_legalOn && _legalGlow != null && _legalGlow.gameObject.activeSelf)
                ApplyLegalGlowColor();

            if (Occupant == null && LockedCard == null) return;
            // Don't stomp an in-flight slot-insert (drag snap or field-sync seat FX)
            if (_slotInsertCo != null || Time.unscaledTime < SeatAnimUntil) return;
            for (var i = 0; i < transform.childCount; i++)
            {
                var childBusy = transform.GetChild(i);
                if (childBusy == null) continue;
                var visBusy = childBusy.GetComponent<ArDiskCardVisual>();
                if (visBusy != null && visBusy.MotionBusy) return;
            }

            var orient = Occupant != null
                ? (!Occupant.FaceUp
                    ? ArZoneOrientation.FaceDownSet
                    : Occupant.Position == BattlePosition.Defense
                        ? ArZoneOrientation.FaceUpDefense
                        : ArZoneOrientation.FaceUpAttack)
                : ArZoneOrientation.FaceUpAttack;
            RefreshFlushLock(orient);

            if (LockedCard != null && LockedCard.IsLockedToZone && !LockedCard.IsSnapAnimating)
                ArZoneLayout.ApplyFlushLock(this, LockedCard.transform, orient);

            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.GetComponent<ArDiskCardVisual>() == null) continue;
                child.localPosition = LockLocalPosition;
                child.localRotation = LockLocalRotation;
                child.localScale = LockLocalScale;
            }
        }
    }
}
