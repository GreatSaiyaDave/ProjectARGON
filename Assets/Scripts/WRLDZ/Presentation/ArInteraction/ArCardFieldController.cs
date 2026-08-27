using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Card Field — physical TCG cards on Spirit Dueler disks (board #1 of 2).
    ///
    /// · Monsters: flush on angled pad segments; face-down = card back on the plate.
    ///   Drop onto pad · ATK/DEF turn · lift-off to GY / shatter.
    /// · S/T: toaster slots — present above the plate, drop to the mouth, slide in
    ///   (10% tip). Activate ejects, flips, then reseats face-up or flies to GY.
    /// </summary>
    public class ArCardFieldController : MonoBehaviour
    {
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;
        public int Layer = 28;

        readonly Dictionary<ArDiskZone, ArDiskCardVisual> _visuals = new();
        /// <summary>Last known FaceUp per zone — detects toaster activate (set → face-up).</summary>
        readonly Dictionary<ArDiskZone, bool> _lastFaceUp = new();
        /// <summary>Last known Defense pose — detects ATK ↔ DEF turn on a monster pad.</summary>
        readonly Dictionary<ArDiskZone, bool> _lastDefense = new();
        /// <summary>Zones that currently hold a disk visual occupant (for leave-field eject).</summary>
        readonly HashSet<ArDiskZone> _seatedZones = new();
        /// <summary>Activate-from-hand ghosts already played on the disk this duel.</summary>
        readonly HashSet<int> _playedActivationGhosts = new();
        readonly List<SpellActivationPresentation.Event> _activationScratch = new();

        public static ArCardFieldController Create(Transform parent, ArDuelDiskRig player,
            ArDuelDiskRig opp, int layer)
        {
            var go = new GameObject("CardFieldLayer");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ArCardFieldController>();
            c.PlayerDisk = player;
            c.OppDisk = opp;
            c.Layer = layer;
            return c;
        }

        public void SyncFromEngine(DuelEngine engine, CardDatabase db)
        {
            if (engine?.Player == null || engine.Opponent == null || db == null) return;
            if (PlayerDisk != null)
            {
                PlayerDisk.SyncOccupantsFrom(engine.Player);
                RefreshDisk(PlayerDisk, db, PlayerDisk.Accent, controllerView: true);
            }

            if (OppDisk != null)
            {
                OppDisk.SyncOccupantsFrom(engine.Opponent);
                RefreshDisk(OppDisk, db, OppDisk.Accent, controllerView: false);
            }
        }

        void RefreshDisk(ArDuelDiskRig disk, CardDatabase db, Color accent, bool controllerView)
        {
            if (disk?.Zones == null) return;
            var diskZones = new HashSet<ArDiskZone>();
            foreach (var z in disk.Zones)
                if (z != null) diskZones.Add(z);

            var live = new HashSet<ArDiskZone>();
            foreach (var z in disk.Zones)
            {
                if (z == null) continue;
                var occ = z.Occupant;
                if (occ == null)
                {
                    if (_visuals.TryGetValue(z, out var leaving) && leaving != null &&
                        Application.isPlaying)
                    {
                        // Keep the visual alive until the leave motion finishes.
                        live.Add(z);
                        if (!leaving.MotionBusy)
                        {
                            if (ArZoneLayout.IsSlotKind(z.Kind))
                                StartCoroutine(LeaveSlotThenClear(z, leaving, db));
                            else
                                StartCoroutine(MonsterLeaveThenClear(z, leaving));
                        }

                        continue;
                    }

                    ClearZone(z);
                    _lastFaceUp.Remove(z);
                    _lastDefense.Remove(z);
                    _seatedZones.Remove(z);
                    continue;
                }

                live.Add(z);

                // Capture drag pose before the floating card is destroyed so insert
                // continues from the hand instead of popping at the seat.
                var hasHandoff = false;
                var handoffP = Vector3.zero;
                var handoffR = Quaternion.identity;
                var handoffS = Vector3.one;
                if (z.LockedCard != null)
                {
                    var fc = z.LockedCard.transform;
                    handoffP = fc.localPosition;
                    handoffR = fc.localRotation;
                    handoffS = fc.localScale;
                    hasHandoff = true;
                    z.ReleaseFloatingKeepOccupant();
                }

                if (!_visuals.TryGetValue(z, out var vis) || vis == null ||
                    vis.InstanceId != occ.InstanceId)
                {
                    ClearZone(z);
                    var isMon = z.Kind == ArDuelZoneKind.Monster
                                || z.Kind == ArDuelZoneKind.PendulumLeft
                                || z.Kind == ArDuelZoneKind.PendulumRight;
                    // Physical disk: always public faces (set = back). No private art on pads.
                    vis = ArDiskCardVisual.Create(z.transform, occ, db, Layer, accent, isMon,
                        controllerView: false);
                    _visuals[z] = vis;
                    _lastFaceUp[z] = occ.FaceUp;
                    _lastDefense[z] = occ.Position == BattlePosition.Defense;
                    // Mark seated only after anim — prevents false GY eject mid-insert

                    if (ArZoneLayout.IsSlotKind(z.Kind) && Application.isPlaying)
                        PlaySlotSeatFx(vis, z, occ, db, hasHandoff, handoffP, handoffR, handoffS);
                    else if (Application.isPlaying)
                        PlayMonsterSeatFx(vis, z, occ);
                    else
                    {
                        ApplyZoneOrientation(z, occ);
                        _seatedZones.Add(z);
                    }

                    EnsureRenderersVisible(vis);
                    Debug.Log(
                        $"[WRLDZ AR] Pad card ON DISK · {occ.Name} · {z.Kind}[{z.Index}] · " +
                        $"face={(occ.FaceUp ? "UP" : "DOWN SET")} · zonePos={z.transform.position} · " +
                        $"cardLocal={vis.transform.localPosition}");
                }
                else
                {
                    // Do not consume FaceUp/Defense edges while a toaster/seat tween owns the card
                    if (vis.MotionBusy) continue;

                    var wasFace = _lastFaceUp.TryGetValue(z, out var lf) && lf;
                    var nowFace = occ.FaceUp;
                    var wasDef = _lastDefense.TryGetValue(z, out var ld) && ld;
                    var nowDef = occ.Position == BattlePosition.Defense;
                    _lastFaceUp[z] = nowFace;
                    _lastDefense[z] = nowDef;

                    // Toaster activate: set S/T turns face-up while still on field
                    if (ArZoneLayout.IsSlotKind(z.Kind) && !wasFace && nowFace &&
                        Application.isPlaying)
                    {
                        vis.Card = occ;
                        vis.InstanceId = occ.InstanceId;
                        StartCoroutine(ToasterActivateStay(vis, z, occ, db));
                    }
                    else if (!ArZoneLayout.IsSlotKind(z.Kind) && !wasFace && nowFace &&
                             !nowDef && Application.isPlaying)
                    {
                        // Flip Summon: back → art (still landscape), then rotate to ATK
                        vis.Card = occ;
                        vis.InstanceId = occ.InstanceId;
                        StartCoroutine(MonsterFlipSummonCo(vis, z, occ, db));
                    }
                    else if (!ArZoneLayout.IsSlotKind(z.Kind) && !wasFace && nowFace &&
                             nowDef && Application.isPlaying)
                    {
                        // Flip by battle: reveal only — stays Defense
                        vis.Card = occ;
                        vis.InstanceId = occ.InstanceId;
                        StartCoroutine(MonsterRevealCo(vis, z, occ, db));
                    }
                    else if (!ArZoneLayout.IsSlotKind(z.Kind) && wasDef != nowDef &&
                             wasFace && nowFace && Application.isPlaying)
                    {
                        vis.MotionBusy = true;
                        vis.Sync(occ, db);
                        StartCoroutine(MonsterTurnCo(vis, z, occ));
                    }
                    else
                    {
                        vis.ControllerView = false;
                        vis.Sync(occ, db);
                        if (Time.unscaledTime >= z.SeatAnimUntil)
                            ApplyZoneOrientation(z, occ);
                        EnsureRenderersVisible(vis);
                        _seatedZones.Add(z);
                    }
                }
            }

            var dead = new List<ArDiskZone>();
            foreach (var kv in _visuals)
            {
                if (kv.Key == null) { dead.Add(kv.Key); continue; }
                if (!diskZones.Contains(kv.Key)) continue;
                if (!live.Contains(kv.Key))
                    dead.Add(kv.Key);
            }

            foreach (var z in dead)
            {
                ClearZone(z);
                _lastFaceUp.Remove(z);
                _lastDefense.Remove(z);
                _seatedZones.Remove(z);
            }

            PlayActivationGhosts(disk, db);
        }

        static void EnsureRenderersVisible(ArDiskCardVisual vis)
        {
            ApplySlotVisibility(vis, exposeSlot: true);
        }

        /// <summary>
        /// S/T stay drawn so the slot tip reads. Rim stays off (no glow through the plate).
        /// </summary>
        static void ApplySlotVisibility(ArDiskCardVisual vis, bool exposeSlot)
        {
            if (vis == null) return;
            vis.gameObject.SetActive(true);
            var show = vis.IsMonsterCard || exposeSlot;
            var sort = vis.IsMonsterCard ? 10 : 2;
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null) continue;
                if (!vis.IsMonsterCard && mr.name == "Rim")
                {
                    mr.enabled = false;
                    continue;
                }

                mr.enabled = show;
                mr.sortingOrder = sort;
            }

            var col = vis.GetComponent<Collider>();
            if (col != null)
                col.enabled = true;
        }

        // ── Monster pad seat (slow drop onto plate) ─────────────────────────

        void PlayMonsterSeatFx(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ)
        {
            if (vis == null || z == null || occ == null) return;
            var orient = !occ.FaceUp
                ? ArZoneOrientation.FaceDownSet
                : occ.Position == BattlePosition.Defense
                    ? ArZoneOrientation.FaceUpDefense
                    : ArZoneOrientation.FaceUpAttack;
            z.RefreshFlushLock(orient);
            z.SeatAnimUntil = Time.unscaledTime + ArDiskMotion.PadSnap + 0.1f;
            vis.MotionBusy = true;
            // Face-down: force back texture before the drop so the set never flashes art
            vis.FaceUp = occ.FaceUp;
            vis.ApplyFace(null);
            var endP = z.LockLocalPosition;
            var endR = z.LockLocalRotation;
            var endS = z.LockLocalScale;
            // Start above the pad, slightly toward the wearer — magnet pulls it down onto the plate
            vis.transform.localPosition = endP + new Vector3(0f, 0.11f, 0.04f);
            vis.transform.localRotation = endR * Quaternion.Euler(-18f, 0f, 0f);
            vis.transform.localScale = endS * 1.18f;
            StartCoroutine(MonsterSeatCo(vis, z, endP, endR, endS));
        }

        IEnumerator MonsterSeatCo(ArDiskCardVisual vis, ArDiskZone z, Vector3 endP, Quaternion endR,
            Vector3 endS)
        {
            if (vis == null) yield break;
            var t = vis.transform;
            var p0 = t.localPosition;
            var r0 = t.localRotation;
            var s0 = t.localScale;
            yield return ArDiskMotion.LerpLocal(t, p0, endP, r0, endR, s0, endS, ArDiskMotion.PadSnap);
            if (vis != null) vis.MotionBusy = false;
            if (z != null) _seatedZones.Add(z);
        }

        // ── S/T toaster insert (present → mouth → pocket) ───────────────────

        void PlaySlotSeatFx(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ,
            CardDatabase db, bool hasHandoff, Vector3 handoffP, Quaternion handoffR,
            Vector3 handoffS)
        {
            if (vis == null || z == null || occ == null) return;
            var orient = !occ.FaceUp
                ? ArZoneOrientation.FaceDownSet
                : ArZoneOrientation.FaceUpAttack;
            z.RefreshFlushLock(orient);
            z.SeatAnimUntil = Time.unscaledTime + ArDiskMotion.SlotInsertHold;
            vis.MotionBusy = true;
            ArZoneLayout.GetSlotInsertStart(z.Kind, orient, out var startP, out var startR, out var startS,
                z.transform);
            if (hasHandoff)
            {
                startP = handoffP;
                startR = handoffR;
                startS = handoffS;
            }

            vis.transform.localPosition = startP;
            vis.transform.localRotation = startR;
            vis.transform.localScale = startS;
            // Ensure set shows back from the first frame
            vis.FaceUp = occ.FaceUp;
            vis.ApplyFace(db);
            ApplySlotVisibility(vis, exposeSlot: true);
            DuelPresentationPacer.HoldSlotInsert(occ.Name);
            StartCoroutine(SlotSeatCo(vis, z, occ, db, startP, startR, startS));
        }

        IEnumerator SlotSeatCo(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ,
            CardDatabase db, Vector3 p0, Quaternion r0, Vector3 s0)
        {
            if (vis == null || z == null) yield break;
            var t = vis.transform;
            ArZoneLayout.GetSlotMouthPose(z.Kind, out var mouthP, out var mouthR, out var mouthS,
                z.transform);
            var seatP = z.LockLocalPosition;
            var seatR = z.LockLocalRotation;
            var seatS = z.LockLocalScale;

            // 1) Present above the plate, then drop to the slot mouth
            yield return ArDiskMotion.VisibleLerp(t, p0, mouthP, r0, mouthR, s0, mouthS,
                ArDiskMotion.SlotInsertPresent);
            // 2) Brief settle so the mouth alignment reads
            yield return ArDiskMotion.VisibleLerp(t, mouthP, mouthP, mouthR, mouthR, mouthS, mouthS,
                ArDiskMotion.SlotInsertApproach * 0.35f);
            // 3) Slide into the pocket — 10% tip stays at the mouth
            yield return ArDiskMotion.SlotSlideLerp(t, mouthP, seatP, mouthR, seatR, mouthS, seatS,
                ArDiskMotion.SlotInsertSlide);

            if (t != null)
            {
                t.localPosition = seatP;
                t.localRotation = seatR;
                t.localScale = seatS;
            }

            if (vis != null)
            {
                vis.MotionBusy = false;
                ApplySlotVisibility(vis, exposeSlot: true);
            }

            if (z != null) _seatedZones.Add(z);
            Debug.Log(
                $"[WRLDZ AR] S/T seated (slide-in, 10% tip) · {z?.Kind}[{z?.Index}] · {occ?.Name} · " +
                $"seatLocal={seatP} · world={t.position}");

            // Occupant already left (activate-from-hand / same-frame GY) — finish the toaster
            if (vis != null && occ != null && (z == null || z.Occupant != occ))
            {
                if (SpellActivationPresentation.HasActivation(occ.InstanceId) ||
                    SpellActivationPresentation.WantsFadeToGy(occ.InstanceId))
                    yield return ToasterActivateStay(vis, z, occ, db);
                else
                    yield return LeaveSlotThenClear(z, vis, db);
            }
        }

        // ── Toaster activate (eject → flip → reseat continuous OR leave for GY) ─

        IEnumerator ToasterActivateStay(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ,
            CardDatabase db)
        {
            if (vis == null || z == null || occ == null) yield break;
            vis.MotionBusy = true;
            ApplySlotVisibility(vis, exposeSlot: true);
            z.SeatAnimUntil = Time.unscaledTime + 4f;
            var t = vis.transform;
            var scale = ArZoneLayout.LocalScaleForWorldSize(z.transform, z.Kind);

            // Seated (buried) pose — eject from here
            var setOrient = ArZoneOrientation.FaceDownSet;
            z.RefreshFlushLock(setOrient);
            var seatP = z.LockLocalPosition;
            var seatR = z.LockLocalRotation;

            // 1) Eject almost fully out of the exterior slot
            var ejectP = ArZoneLayout.SlotEjectedOffset(z.Kind, z.transform);
            var ejectR = ArZoneLayout.SpellTrapEjectedRot;
            yield return ArDiskMotion.LerpLocal(t, seatP, ejectP, seatR, ejectR, scale, scale,
                ArDiskMotion.ToasterEject);

            // 2) Flip over — art revealed at midpoint
            yield return ArDiskMotion.FlipY(t, () =>
            {
                vis.FaceUp = true;
                vis.Card = occ;
                vis.ForceShowArt();
            }, ArDiskMotion.ToasterFlip);

            // 3) Game logic: Continuous / Field / Equip reseat face-up (still 90% in).
            //    One-shots leave for GY (engine will clear the zone).
            var stays = ArZoneLayout.StaysInSlotAfterActivate(occ);
            if (stays && z.Occupant == occ)
            {
                var upOrient = ArZoneOrientation.FaceUpAttack;
                z.RefreshFlushLock(upOrient);
                vis.FaceUp = true;
                vis.Defense = false;
                vis.ApplyFace(db);
                yield return ArDiskMotion.LerpLocal(t, ejectP, z.LockLocalPosition, ejectR,
                    z.LockLocalRotation, scale, z.LockLocalScale, ArDiskMotion.ToasterReseat);

                if (vis != null)
                {
                    vis.MotionBusy = false;
                    vis.Sync(occ, db);
                    ApplySlotVisibility(vis, exposeSlot: true);
                }

                _seatedZones.Add(z);
                z.SeatAnimUntil = Time.unscaledTime + 0.05f;
                Debug.Log($"[WRLDZ AR] Toaster activate → reseat on disk · {occ.Name}");
            }
            else
            {
                // One-shot: rise and fade to GY (do not reseat)
                Debug.Log($"[WRLDZ AR] Toaster activate → GY · {occ.Name}");
                yield return ToasterFlyToGy(t, scale, z);
                ClearZone(z);
                _lastFaceUp.Remove(z);
                _lastDefense.Remove(z);
                _seatedZones.Remove(z);
            }
        }

        // ── Toaster leave field (activate / destroy / send to GY) ───────────

        IEnumerator LeaveSlotThenClear(ArDiskZone z, ArDiskCardVisual vis, CardDatabase db)
        {
            if (z == null || vis == null)
            {
                ClearZone(z);
                yield break;
            }

            if (vis.MotionBusy) yield break;

            var id = vis.InstanceId;
            var activated = SpellActivationPresentation.HasActivation(id) ||
                            SpellActivationPresentation.WantsFadeToGy(id);
            if (activated)
            {
                yield return ToasterActivateStay(vis, z, vis.Card, db);
                yield break;
            }

            vis.MotionBusy = true;
            ApplySlotVisibility(vis, exposeSlot: true);
            z.SeatAnimUntil = Time.unscaledTime + 4f;
            var t = vis.transform;
            var scale = t.localScale;
            var wasFace = vis.FaceUp;
            var seatP = t.localPosition;
            var seatR = t.localRotation;
            var ejectP = ArZoneLayout.SlotEjectedOffset(z.Kind, z.transform);
            var ejectR = ArZoneLayout.SpellTrapEjectedRot;

            yield return ArDiskMotion.LerpLocal(t, seatP, ejectP, seatR, ejectR, scale, scale,
                ArDiskMotion.ToasterEject);

            // Destroyed while set (MST etc.): shatter at the mouth after eject
            if (CardShatterPresentation.HasQueued(id))
            {
                ClearZone(z);
                _lastFaceUp.Remove(z);
                _lastDefense.Remove(z);
                _seatedZones.Remove(z);
                yield break;
            }

            if (!wasFace)
            {
                yield return ArDiskMotion.FlipY(t, () =>
                {
                    vis.FaceUp = true;
                    vis.ForceShowArt();
                }, ArDiskMotion.ToasterFlip);
            }

            yield return ToasterFlyToGy(t, scale, z);

            ClearZone(z);
            _lastFaceUp.Remove(z);
            _lastDefense.Remove(z);
            _seatedZones.Remove(z);
        }

        /// <summary>
        /// Flip Summon: lift the set card, flip it face-up (still landscape), then
        /// rotate into face-up Attack. Same pacing as toaster flip + position turn.
        /// </summary>
        IEnumerator MonsterFlipSummonCo(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ,
            CardDatabase db)
        {
            if (vis == null || z == null || occ == null) yield break;
            vis.MotionBusy = true;
            vis.FaceUp = false;
            vis.ApplyFace(db);
            z.RefreshFlushLock(ArZoneOrientation.FaceDownSet);
            var setP = z.LockLocalPosition;
            var setR = z.LockLocalRotation;
            var setS = z.LockLocalScale;
            z.RefreshFlushLock(ArZoneOrientation.FaceUpAttack);
            var atkP = z.LockLocalPosition;
            var atkR = z.LockLocalRotation;
            var atkS = z.LockLocalScale;
            z.SeatAnimUntil = Time.unscaledTime + ArDiskMotion.FlipSummonTotal + 0.12f;
            DuelPresentationPacer.HoldFlipSummon(occ.Name);

            var t = vis.transform;
            t.localPosition = setP;
            t.localRotation = setR;
            t.localScale = setS;

            // 1) Lift off the pad so the flip reads
            var lift = setP + new Vector3(0f, 0.055f, 0f);
            yield return ArDiskMotion.VisibleLerp(t, setP, lift, setR, setR, setS, setS * 1.04f,
                ArDiskMotion.FlipSummonLift);

            // 2) Flip over — swap back → art at the edge-on midpoint
            yield return ArDiskMotion.FlipY(t, () =>
            {
                if (vis == null) return;
                vis.FaceUp = true;
                vis.ForceShowArt();
            }, ArDiskMotion.FlipSummonReveal);

            if (vis == null) yield break;
            t.localRotation = setR; // still landscape (face-up DEF)
            if (ArDiskMotion.FaceFlipHold > 0f)
                yield return new WaitForSecondsRealtime(ArDiskMotion.FaceFlipHold);

            // 3) Rotate into Attack (portrait) and seat
            yield return ArDiskMotion.VisibleLerp(t, lift, atkP, setR, atkR, t.localScale, atkS,
                ArDiskMotion.FlipSummonTurn);

            vis.FaceUp = true;
            vis.Defense = false;
            vis.Sync(occ, db);
            vis.MotionBusy = false;
            _seatedZones.Add(z);
            Debug.Log($"[WRLDZ AR] Flip Summon · {occ.Name} · flip then ATK");
        }

        /// <summary>
        /// Flip by battle: reveal art in place on the pad. No lift, no yaw, no
        /// ATK/DEF turn — the card stays landscape Defense.
        /// </summary>
        IEnumerator MonsterRevealCo(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ,
            CardDatabase db)
        {
            if (vis == null || z == null || occ == null) yield break;
            vis.MotionBusy = true;
            vis.FaceUp = false;
            vis.Defense = true;
            vis.ApplyFace(db);
            z.RefreshFlushLock(ArZoneOrientation.FaceDownSet);
            var seatP = z.LockLocalPosition;
            var seatR = z.LockLocalRotation;
            var seatS = z.LockLocalScale;
            z.SeatAnimUntil = Time.unscaledTime + ArDiskMotion.BattleFlipDiskTotal + 0.10f;
            DuelPresentationPacer.HoldBattleFlip(occ.Name);

            var t = vis.transform;
            t.localPosition = seatP;
            t.localRotation = seatR;
            t.localScale = seatS;

            yield return ArDiskMotion.FlipInPlace(t, () =>
            {
                if (vis == null) return;
                vis.FaceUp = true;
                vis.ForceShowArt();
            }, ArDiskMotion.BattleFlipReveal);

            if (vis == null) yield break;
            t.localPosition = seatP;
            t.localRotation = seatR;
            t.localScale = seatS;

            vis.FaceUp = true;
            vis.Defense = true;
            vis.Sync(occ, db);
            vis.MotionBusy = false;
            _seatedZones.Add(z);
            Debug.Log($"[WRLDZ AR] Battle flip · {occ.Name} · in-place, stay DEF");
        }

        IEnumerator MonsterTurnCo(ArDiskCardVisual vis, ArDiskZone z, CardInstance occ)
        {
            if (vis == null || z == null || occ == null) yield break;
            vis.MotionBusy = true;
            z.RefreshFlushLock(occ.Position == BattlePosition.Defense
                ? ArZoneOrientation.FaceUpDefense
                : ArZoneOrientation.FaceUpAttack);
            z.SeatAnimUntil = Time.unscaledTime + ArDiskMotion.PositionTurn + 0.08f;
            var t = vis.transform;
            yield return ArDiskMotion.VisibleLerp(t, t.localPosition, z.LockLocalPosition,
                t.localRotation, z.LockLocalRotation, t.localScale, z.LockLocalScale,
                ArDiskMotion.PositionTurn);
            vis.MotionBusy = false;
            _seatedZones.Add(z);
        }

        IEnumerator MonsterLeaveThenClear(ArDiskZone z, ArDiskCardVisual vis)
        {
            if (z == null || vis == null)
            {
                ClearZone(z);
                yield break;
            }

            if (vis.MotionBusy) yield break;
            vis.MotionBusy = true;
            z.SeatAnimUntil = Time.unscaledTime + 3f;
            var t = vis.transform;

            // Same-tick battle flip + destroy: reveal art in place before leave
            if (!vis.FaceUp)
            {
                DuelPresentationPacer.HoldBattleFlip(vis.Card != null ? vis.Card.Name : null);
                yield return ArDiskMotion.FlipInPlace(t, () =>
                {
                    if (vis == null) return;
                    vis.FaceUp = true;
                    vis.ForceShowArt();
                }, ArDiskMotion.BattleFlipReveal);
                if (vis == null)
                {
                    ClearZone(z);
                    yield break;
                }
            }

            var scale = t.localScale;
            var lift = t.localPosition + new Vector3(0f, 0.09f, 0f);
            yield return ArDiskMotion.VisibleLerp(t, t.localPosition, lift, t.localRotation,
                t.localRotation * Quaternion.Euler(-18f, 0f, 0f), scale, scale * 0.92f,
                ArDiskMotion.MonsterLeave);

            if (CardShatterPresentation.HasQueued(vis.InstanceId))
            {
                ClearZone(z);
                _lastFaceUp.Remove(z);
                _lastDefense.Remove(z);
                _seatedZones.Remove(z);
                yield break;
            }

            yield return ToasterFlyToGy(t, scale, z);
            ClearZone(z);
            _lastFaceUp.Remove(z);
            _lastDefense.Remove(z);
            _seatedZones.Remove(z);
        }

        void PlayActivationGhosts(ArDuelDiskRig disk, CardDatabase db)
        {
            if (disk?.SpellTrapZones == null || !Application.isPlaying) return;
            SpellActivationPresentation.CollectActive(_activationScratch);
            for (var i = 0; i < _activationScratch.Count; i++)
            {
                var ev = _activationScratch[i];
                if (ev.InstanceId <= 0) continue;
                if (_playedActivationGhosts.Contains(ev.InstanceId)) continue;
                if (ev.PlayerSide != disk.IsPlayerSide) continue;
                if (ev.StaysOnField && !ev.ResolveToGy) continue;
                var zi = ev.ZoneIndex;
                if (zi < 0 || zi >= disk.SpellTrapZones.Length) continue;
                var z = disk.SpellTrapZones[zi];
                if (z == null || z.Occupant != null) continue;
                if (_visuals.TryGetValue(z, out var existing) && existing != null) continue;

                var ghost = new CardInstance
                {
                    InstanceId = ev.InstanceId,
                    CardId = ev.CardId,
                    Def = db != null ? db.Get(ev.CardId) : null,
                    FaceUp = true,
                    Position = BattlePosition.Attack
                };
                var vis = ArDiskCardVisual.Create(z.transform, ghost, db, Layer, disk.Accent,
                    isMonster: false, controllerView: false);
                _visuals[z] = vis;
                _playedActivationGhosts.Add(ev.InstanceId);
                PlaySlotSeatFx(vis, z, ghost, db, false, Vector3.zero, Quaternion.identity,
                    Vector3.one);
            }
        }

        IEnumerator ToasterFlyToGy(Transform t, Vector3 scale, ArDiskZone z)
        {
            if (t == null) yield break;
            var start = t.position;
            var gy = z != null
                ? z.transform.position + z.transform.up * 0.12f + z.transform.forward * 0.05f
                : start + Vector3.up * 0.15f;
            var disk = z != null ? z.GetComponentInParent<ArDuelDiskRig>() : null;
            if (disk?.GraveyardZone != null)
                gy = disk.GraveyardZone.position + Vector3.up * 0.05f;

            var u = 0f;
            var dur = ArDiskMotion.ToasterToGy;
            while (u < 1f && t != null)
            {
                u += Time.unscaledDeltaTime / dur;
                var e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u));
                t.position = Vector3.Lerp(start, gy, e);
                t.localScale = Vector3.Lerp(scale, scale * 0.35f, e);
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr?.material == null) continue;
                    if (mr.material.HasProperty("_BaseColor"))
                    {
                        var bc = mr.material.GetColor("_BaseColor");
                        bc.a = 1f - e;
                        mr.material.SetColor("_BaseColor", bc);
                    }

                    if (mr.material.HasProperty("_Color"))
                    {
                        var c = mr.material.color;
                        c.a = 1f - e;
                        mr.material.color = c;
                    }
                }

                yield return null;
            }
        }

        static void ApplyZoneOrientation(ArDiskZone z, CardInstance c)
        {
            if (z == null || c == null) return;
            var orient = !c.FaceUp
                ? ArZoneOrientation.FaceDownSet
                : c.Position == BattlePosition.Defense
                    ? ArZoneOrientation.FaceUpDefense
                    : ArZoneOrientation.FaceUpAttack;

            z.RefreshFlushLock(orient);

            foreach (Transform child in z.transform)
            {
                if (child == null) continue;
                var pad = child.GetComponent<ArDiskCardVisual>();
                var floating = child.GetComponent<ArFloatingCard>();
                if (pad == null && floating == null) continue;
                if (pad != null && pad.MotionBusy) continue;
                ArZoneLayout.ApplyFlushLock(z, child, orient);
            }
        }

        void ClearZone(ArDiskZone z)
        {
            if (z == null) return;
            if (_visuals.TryGetValue(z, out var vis) && vis != null)
            {
                var id = vis.InstanceId;
                ArCardShatterFx.ConsumeQueuedOrQuietDestroy(vis.gameObject, id, Layer);
            }

            _visuals.Remove(z);
            _seatedZones.Remove(z);
            _lastDefense.Remove(z);
        }
    }
}
