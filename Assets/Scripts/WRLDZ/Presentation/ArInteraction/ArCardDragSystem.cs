using System;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.Duel;
using WRLDZ.Presentation;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Drag hand holos onto the Spirit Dueler disk (same phone/Editor AR view — no second device).
    /// Flow: pick floating hand card → drag over disk → release snaps to nearest legal zone
    /// via <see cref="ArSnapRules"/> (engine commit). Shift = prefer Set / face-down.
    /// Short tap on a floating hand card → <see cref="OnHandCardTapped"/> (play / inspect).
    /// Short tap on disk pad cards → <see cref="OnFieldCardTapped"/> (Attack menu).
    /// Arena holos are not drop targets; they ARE tappable while an effect waits for a target.
    /// Combat trap activations: blinking legal S/T zones on the local disk; click to activate.
    /// </summary>
    public class ArCardDragSystem : MonoBehaviour
    {
        public Camera StageCamera;
        public RectTransform ViewportRect;
        public RawImageProxy Viewport;
        public ArHandVolume Hand;
        public ArDuelDiskRig PlayerDisk;
        public ArDuelDiskRig OppDisk;
        public ArArenaHologramManager Arena;
        public DuelEngine Engine;
        /// <summary>When true (hold Shift on PC), prefer face-down Set over face-up play.</summary>
        public bool PreferSet;

        /// <summary>Live rules banner + Summon/Set chooser.</summary>
        public ArDragActionHud Hud;

        /// <summary>Disk card tap only (not arena art) → Attack / Flip menu in DuelUI.</summary>
        public Action<CardInstance, bool /*playerSide*/, bool /*isMonster*/> OnFieldCardTapped;

        /// <summary>Short tap on a floating hand holo → play / inspect menu in DuelUI.</summary>
        public Action<CardInstance> OnHandCardTapped;

        /// <summary>Short tap on a disk GY tray → graveyard browser.</summary>
        public Action<bool /*playerSide*/> OnGraveyardTapped;

        /// <summary>
        /// Short tap on Banished/RFG floater (M1-adjacent). Dilbot: wire to BanishedBrowser.Show.
        /// Stub — Backup-bot presentation assist; connect in DuelUI alongside OnGraveyardTapped.
        /// </summary>
        public Action<bool /*playerSide*/> OnBanishedTapped;

        /// <summary>Short tap on a floating disk phase chip (BATTLE / MAIN 2 / END).</summary>
        public Action<ArDiskPhaseKind> OnPhaseButtonTapped;

        /// <summary>Tap a public card on the opponent glance mat (face-up = full text).</summary>
        public Action<CardInstance, bool /*publicFace*/> OnOppGlanceCardTapped;

        /// <summary>GY count on the glance mat; without a listener it opens the opponent's GY as before.</summary>
        public Action OnOppGlanceGyTapped;

        /// <summary>Empty legal zone tap (disk pad or arena glass) while a hand card is selected.</summary>
        public Action<WRLDZ.Duel.Rules.RulesZoneKind, int> OnEmptyZoneTapped;

        /// <summary>When true, pointer-down prefers a legal zone over a hand card (zone pick).</summary>
        public bool HighlightsActive;

        /// <summary>True while a hand card is being dragged (free-look should not steal the pointer).</summary>
        public bool IsDragging => _drag != null;

        /// <summary>True while this system owns the pointer (armed tap or live drag).</summary>
        public bool OwnsPointer => _drag != null || _armedHand != null || _zoneTapArmed || GestureBusy;
        /// <summary>The floating hand is carrying a card to a zone: pointer input waits for it.</summary>
        public bool GestureBusy =>
            Hand != null && Hand.FloatingHand != null ? Hand.FloatingHand.IsPlacing : _gestureCard != null;
        ArFloatingCard _gestureCard;

        // hover is re-picked every frame while the pointer rests (the fan keeps moving under a still cursor)
        Vector2 _hoverScreen;
        bool _hoverArmed;
        // the drag follows its target every frame, not only on pointer-move events
        Vector3 _dragTarget;
        bool _hasDragTarget;
        float _dragLift;
        Vector3 _dragRestScale = Vector3.one;
        bool _handPeek;
        int _syncedFrame = -1;

        /// <summary>A place gesture started outside this system (DuelUI zone pick): hold the pointer until it ends.</summary>
        public void NoteGesture(ArFloatingCard card) => _gestureCard = card;
        public void ClearGesture() => _gestureCard = null;

        bool _zoneTapArmed;
        WRLDZ.Duel.Rules.RulesZoneKind _zoneTapKind;
        int _zoneTapIndex;

        ArFloatingCard _drag;
        Plane _dragPlane;
        ArDiskZone _hoverZone;
        Vector2 _pointerDownScreen;
        float _pointerDownTime;
        bool _pointerDown;
        ArFloatingCard _pendingCard;
        ArDiskZone _pendingZone;
        /// <summary>Hand holo under the pointer, not yet promoted to a drag.</summary>
        ArFloatingCard _armedHand;

        const float DragStartPx = 22f;

        /// <summary>Lightweight stand-in so we don't depend on UI.RawImage in every caller.</summary>
        public class RawImageProxy
        {
            public RectTransform Rect;
            public bool RaycastTarget;
        }

        public void Bind(Camera stageCam, RectTransform viewport, ArHandVolume hand,
            ArDuelDiskRig disk, DuelEngine engine)
        {
            StageCamera = stageCam;
            ViewportRect = viewport;
            Hand = hand;
            PlayerDisk = disk;
            Engine = engine;
        }

        int _endFrame = -1;

        bool _lensAim;

        void Update()
        {
            if (StageCamera == null || Engine == null) return;
            PreferSet = WrldzInput.KeyHeld(KeyCode.LeftShift) || WrldzInput.KeyHeld(KeyCode.RightShift);
            if (PollHands())
                return;
            if (PollLensAim())
                return;
            if (_pendingCard != null || GestureBusy)
                return;

            // every frame, whatever the pointer did: the dragged card keeps closing on its target (it used to stop
            // short whenever the mouse paused) and the peek follows the fan moving under a resting cursor
            if (_drag != null && !HandOwnsCard) StepDragFollow();
            else if (_hoverArmed && _armedHand == null && !_pointerDown && !HandOwnsCard) RepickHover();

            // EventSystem / ArViewportInput owns the pointer when a viewport is bound.
            if (ViewportRect != null) return;

            // Fallback when no EventSystem bridge (still support raw pointer)
            if (_drag != null)
            {
                if (TryGetPointer(out var screen))
                    MoveFromScreen(screen);
                if (TryGetPointerUp())
                {
                    if (!TryGetPointer(out screen))
                        screen = Input.mousePosition;
                    EndFromScreen(screen);
                }
            }
            else if (_armedHand != null)
            {
                if (TryGetPointer(out var screen))
                    PointerMovedFromScreen(screen);
                if (TryGetPointerUp())
                {
                    if (!TryGetPointer(out screen))
                        screen = _pointerDownScreen;
                    EndFromScreen(screen);
                }
            }
            else if (TryGetPointerDown(out var down))
                BeginFromScreen(down);
            else if (_pointerDown && TryGetPointerUp())
            {
                if (!TryGetPointer(out var up))
                    up = _pointerDownScreen;
                EndFromScreen(up);
            }
        }

        /// <summary>Called from <see cref="ArViewportInput"/> or Update fallback.</summary>
        /// <summary>
        /// Pointer over the viewport with nothing pressed (Editor / desktop): the hand card under it peeks up out
        /// of the fan so the pick is readable before it happens.
        /// </summary>
        public void HoverFromScreen(Vector2 screen)
        {
            if (HandOwnsCard) return;
            if (Hand == null || !Hand.HandLive || StageCamera == null) return;
            if (_drag != null || _pendingCard != null || _pointerDown || GestureBusy) return;
            _hoverScreen = screen;
            _hoverArmed = true;
            RepickHover();
        }

        void RepickHover()
        {
            if (Hand == null || !Hand.HandLive || StageCamera == null) return;
            if (_drag != null || _pendingCard != null || _pointerDown || GestureBusy) return;
            var card = TryScreenToRay(_hoverScreen, out var ray) ? Hand.PickForPointer(StageCamera, _hoverScreen, ray, 12f) : null;
            Hand.FloatingHand.SetHover(card);
        }

        public void ClearHover()
        {
            _hoverArmed = false;
            if (Hand != null && Hand.FloatingHand != null) Hand.FloatingHand.SetHover(null);
        }

        public void BeginFromScreen(Vector2 screen)
        {
            if (HandOwnsCard) return;   // a real hand has the card: a screen press must not hijack its pinch
            if (StageCamera == null || Engine == null) return;
            if (_drag != null || _pendingCard != null || _pointerDown || GestureBusy) return;
            if (_endFrame == Time.frameCount) return;
            _pointerDown = true;
            _pointerDownScreen = screen;
            _pointerDownTime = Time.unscaledTime;
            if (!TryScreenToRay(screen, out var ray)) return;

            // Legal-zone pick wins over hand cards (hand holos sit in front of the disk).
            if (HighlightsActive && TryPickLegalZone(ray, out var zk, out var zi))
            {
                _zoneTapArmed = true;
                _zoneTapKind = zk;
                _zoneTapIndex = zi;
                return;
            }

            // Arm the hand holo — tap opens the play menu; movement promotes to drag.
            if (Hand != null)
            {
                var card = Hand.PickForPointer(StageCamera, screen, ray, 12f);
                if (card != null)
                {
                    _armedHand = card;
                    // touch has no hover: the pressed card peeks up now, before the drag threshold
                    if (Hand.HandLive) Hand.FloatingHand.SetHover(card);
                    return;
                }
            }

            // No hand card — field tap resolved on pointer up if still a short click
        }

        /// <summary>
        /// EventSystem / fallback pointer move. Promotes an armed hand tap into a drag
        /// once the pointer travels past <see cref="DragStartPx"/>.
        /// </summary>
        public void PointerMovedFromScreen(Vector2 screen)
        {
            if (HandOwnsCard) return;
            if (_drag != null)
            {
                MoveFromScreen(screen);
                return;
            }

            if (_armedHand == null || _zoneTapArmed) return;
            if (Vector2.Distance(screen, _pointerDownScreen) < DragStartPx) return;
            StartHandDrag(_armedHand);
            if (_drag != null)
                MoveFromScreen(screen);
        }

        /// <summary>
        /// Floating hand: a tapped card lifts 4 cm out of the fan and tilts toward the eye (tap again to drop
        /// it back). Play / inspect still go through <see cref="OnHandCardTapped"/> exactly as before.
        /// </summary>
        void ToggleHandLift(ArFloatingCard card)
        {
            if (Hand == null || Hand.FloatingHand == null || !Hand.HandLive) return;
            Hand.FloatingHand.ToggleSelected(card);
        }

        /// <summary>Drop a lifted (tapped) hand card back into the fan (DuelUI calls it when the card menu closes).</summary>
        public void ClearHandLift()
        {
            if (Hand != null && Hand.FloatingHand != null) Hand.FloatingHand.ClearSelected();
        }

        void StartHandDrag(ArFloatingCard card, bool realHand = false)
        {
            if (card == null || _drag != null || StageCamera == null) return;
            // a drag leaves the fan from the card's current (possibly lifted) pose; the lift itself ends
            ClearHandLift();
            _armedHand = null;
            _placedByHand = null;   // a new drag: whatever a real hand let go of earlier is settled
            _drag = card;
            _drag.IsDragging = true;
            _hasDragTarget = false;
            _dragLift = 0f;
            _dragRestScale = card.transform.localScale;
            var fwd = StageCamera.transform.forward;
            // the drag plane sits a little toward the eye and up from where the card was: the follow carries the
            // card there over a few frames (it used to be pushed and rescaled in one frame, then yanked back)
            _dragPlane = new Plane(-fwd, card.transform.position + (realHand ? Vector3.zero : -fwd * 0.03f + StageCamera.transform.up * 0.02f));
            if (!realHand)
            {
                // the hand takes the card between two fingers and follows it (no-op while the hand is busy drawing)
                if (Hand != null && Hand.HandLive) Hand.FloatingHand.BeginCarry(card);
            }
            PlayerDisk?.SetZonesHot(true);
            if (card.Card?.Def != null && card.Card.Def.IsFieldSpell)
                PlayerDisk?.NoteFieldSpellDrag(true);
            PlayerDisk?.PlayFx(DiskFxEvent.LegalZonePulse);
            ApplyLegalGlow(card.Card, null);
            EnsureHud();
            Hud?.ShowHint(realHand ? HandHint(card.Card) : StartHint(card.Card));
            WrldzAudio.PlayCardSlide();
            Debug.Log($"[WRLDZ AR] Drag start: {card.Card?.Name}{(realHand ? " (real hand)" : "")}");
        }

        public void MoveFromScreen(Vector2 screen)
        {
            if (_drag == null || StageCamera == null || HandOwnsCard) return;
            if (!TryScreenToRay(screen, out var ray)) return;
            // Free roam on drag plane (no zone magnet until release)
            var plane = DragPlane();
            if (plane.Raycast(ray, out var enter) && enter > 0f)
            {
                var target = ray.GetPoint(enter);
                if (UsingLenses())
                {
                    var up = PlayerDisk != null ? PlayerDisk.transform.up : Vector3.up;
                    target += up * 0.008f;
                    _drag.transform.position = target;
                    _hasDragTarget = false;
                }
                else
                {
                    _dragTarget = target;
                    _hasDragTarget = true;
                    StepDragFollow();
                }
            }

            // Soft hover highlight near legal zones (wider than commit radius)
            UpdateDragHover(UsingLenses() ? 0.09f : 0.42f, PreferSet, hint: true);
        }

        int _followFrame = -1;

        /// <summary>One frame of the dragged card closing on the pointer (once per frame, whoever calls first).</summary>
        void StepDragFollow()
        {
            if (_drag == null || _followFrame == Time.frameCount) return;
            _followFrame = Time.frameCount;
            var dt = Time.unscaledDeltaTime;
            if (_hasDragTarget)
            {
                var follow = 1f - Mathf.Exp(-36f * dt);
                _drag.transform.position = Vector3.Lerp(_drag.transform.position, _dragTarget, follow);
            }

            // the pick-up grows the card 8% over a short ease instead of in one frame
            if (_dragLift < 1f && !_realHandDrag)
            {
                _dragLift = Mathf.Min(1f, _dragLift + dt / 0.12f);
                var e = Mathf.SmoothStep(0f, 1f, _dragLift);
                _drag.transform.localScale = Vector3.Lerp(_dragRestScale, _dragRestScale * 1.08f, e);
            }
        }

        /// <summary>Light the legal zone the dragged card is over (disk slot + arena drop preview) and say what it does.</summary>
        void UpdateDragHover(float reach, bool preferSet, bool hint)
        {
            if (_drag == null) return;
            var zone = PlayerDisk?.FindNearestValid(
                _drag.transform.position, reach, _drag.Card, Engine,
                Engine.Player, preferSet);
            if (_hoverZone != zone)
            {
                _hoverZone?.SetHot(false);
                _hoverZone = zone;
                _hoverZone?.SetHot(true);
                ApplyLegalGlow(_drag.Card, zone);
                // The disk and the arena are one instrument: lighting a slot on the blade has to
                // show the player where that card lands on the table, while they still hold it.
                if (zone != null) Arena?.ShowDropPreview(zone.Kind, zone.Index, true);
                else Arena?.ClearDropPreview();
            }

            if (!hint) return;
            EnsureHud();
            if (zone != null)
            {
                var legal = ArSnapRules.QueryLegal(Engine, Engine.Player, _drag.Card, zone);
                Hud?.ShowHint(legal.Hint);
            }
            else
                Hud?.ShowHint(StartHint(_drag.Card) + " · drop on a legal zone");
        }

        public void EndFromScreen(Vector2 screen)
        {
            if (HandOwnsCard) return;
            if (_endFrame == Time.frameCount) return;
            _endFrame = Time.frameCount;

            if (_drag != null)
            {
                MoveFromScreen(screen);
                // the drop happens where the pointer is, not where the follow had got to
                if (_hasDragTarget && !UsingLenses()) _drag.transform.position = _dragTarget;
                _hasDragTarget = false;
                EndDrag();
                _armedHand = null;
                _pointerDown = false;
                return;
            }

            if (_armedHand != null)
            {
                var held = _armedHand;
                var card = held.Card;
                _armedHand = null;
                _pointerDown = false;
                _zoneTapArmed = false;
                if (card != null)
                {
                    Debug.Log($"[WRLDZ AR] Hand tap: {card.Name}");
                    ToggleHandLift(held);
                    OnHandCardTapped?.Invoke(card);
                }

                return;
            }

            // Short tap on field / disk card → Attack menu (lab + phone)
            if (_pointerDown)
            {
                var dist = Vector2.Distance(screen, _pointerDownScreen);
                var held = Time.unscaledTime - _pointerDownTime;
                if (dist < 28f && held < 0.55f)
                {
                    if (_zoneTapArmed)
                    {
                        OnEmptyZoneTapped?.Invoke(_zoneTapKind, _zoneTapIndex);
                        _zoneTapArmed = false;
                        _pointerDown = false;
                        return;
                    }

                    ClearHandLift();
                    TryFieldTap(screen);
                }
            }

            _zoneTapArmed = false;
            _pointerDown = false;
        }

        /// <summary>
        /// Tap-to-place / tap-to-respond: only a zone that is lit as legal answers. Every lit zone the ray touches
        /// (its glow pad, its wide pick sphere, or its Set card's lip) is a candidate and the one whose pick point
        /// is nearest the ray wins, so overlapping volumes never hand the tap to the wrong zone. A near miss within
        /// <see cref="LegalZoneAimSlack"/> of a lit zone still counts.
        /// </summary>
        bool TryPickLegalZone(Ray ray, out WRLDZ.Duel.Rules.RulesZoneKind kind, out int index)
        {
            kind = WRLDZ.Duel.Rules.RulesZoneKind.Monster;
            index = 0;
            ArDiskZone best = null;
            var bestScore = float.MaxValue;
            var hits = Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide);
            if (hits != null && hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    // A closer hand card owns the click — don't steal it for zone pick
                    var fc = h.collider.GetComponentInParent<ArFloatingCard>();
                    if (fc != null && !fc.IsLockedToZone)
                    {
                        if (best == null) return false;
                        continue;
                    }

                    // Arena holos are presentation-only — never steal a disk zone pick.
                    if (h.collider.GetComponentInParent<ArArenaCardVisual>() != null)
                        continue;
                    var pad = h.collider.GetComponentInParent<ArLegalZonePad>();
                    if (pad != null && !pad.OnDisk) continue;
                    var zone = h.collider.GetComponentInParent<ArDiskZone>();
                    if (zone == null || !zone.IsPlayerSide || !zone.LegalOn) continue;
                    var score = LateralToRay(ray, zone.WorldPeekPoint);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = zone;
                }
            }

            if (best == null) best = NearestLitZone(ray, LegalZoneAimSlack);
            if (best == null) return false;
            kind = ToRulesKind(best.Kind);
            index = best.Index;
            return true;
        }

        /// <summary>Near-miss allowance for a lit zone, as the tangent of the angle off the ray (~2.6°).</summary>
        const float LegalZoneAimSlack = 0.045f;

        ArDiskZone NearestLitZone(Ray ray, float slack)
        {
            if (PlayerDisk?.Zones == null) return null;
            ArDiskZone best = null;
            var bestAngle = slack;
            foreach (var z in PlayerDisk.Zones)
            {
                if (z == null || !z.LegalOn) continue;
                var p = z.WorldPeekPoint;
                var along = Vector3.Dot(p - ray.origin, ray.direction);
                if (along < 0.05f) continue;
                var angle = LateralToRay(ray, p) / along;
                if (angle >= bestAngle) continue;
                bestAngle = angle;
                best = z;
            }

            return best;
        }

        static WRLDZ.Duel.Rules.RulesZoneKind ToRulesKind(ArDuelZoneKind k) => k switch
        {
            ArDuelZoneKind.Monster => WRLDZ.Duel.Rules.RulesZoneKind.Monster,
            ArDuelZoneKind.SpellTrap => WRLDZ.Duel.Rules.RulesZoneKind.SpellTrap,
            ArDuelZoneKind.FieldSpell => WRLDZ.Duel.Rules.RulesZoneKind.FieldSpell,
            ArDuelZoneKind.PendulumLeft => WRLDZ.Duel.Rules.RulesZoneKind.PendulumLeft,
            ArDuelZoneKind.PendulumRight => WRLDZ.Duel.Rules.RulesZoneKind.PendulumRight,
            _ => WRLDZ.Duel.Rules.RulesZoneKind.Monster
        };

        void TryFieldTap(Vector2 screen)
        {
            if (!TryScreenToRay(screen, out var ray)) return;
            TryFieldTap(ray);
        }

        void TryFieldTap(Ray ray)
        {
            // Only physical disk cards / GY are tappable for menus.
            // Arena holograms (artwork billboards) are presentation-only until real 3D models ship.
            var hits = Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            ArDiskCardVisual bestDisk = null;
            ArFloatingCard bestFloat = null;
            ArArenaCardVisual bestHolo = null;
            var bestCardScore = float.MaxValue;
            var frontCardDist = float.MaxValue;
            var holoTargeting = Engine != null && Engine.IsAwaitingEffectTarget;
            // Empty zones answer last: their pick spheres are wide and sit over the S/T lips, so a Set card (or a
            // disk control) anywhere along the ray must win the tap instead of "empty Monster Zone".
            ArLegalZonePad emptyPad = null;
            ArDiskZone emptyZone = null;

            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var cardInFront = bestDisk != null || bestFloat != null || bestHolo != null;

                // Arena holos are presentation-only for summons/drops, but they ARE
                // the thing you click when a trap asks for a target (Trap Hole, MST…).
                var holo = h.collider.GetComponentInParent<ArArenaCardVisual>();
                if (holo != null)
                {
                    if (!holoTargeting || holo.Card == null) continue;
                    if (h.distance > frontCardDist + 0.05f) continue;
                    if (h.distance < frontCardDist) frontCardDist = h.distance;
                    var hScore = LateralToRay(ray, holo.transform.position) + h.distance * 0.02f;
                    if (hScore >= bestCardScore) continue;
                    bestCardScore = hScore;
                    bestHolo = holo;
                    bestDisk = null;
                    bestFloat = null;
                    continue;
                }

                // Disk controls answer at once, unless a card already sits in front of them on this ray.
                if (!cardInFront)
                {
                    var gy = h.collider.GetComponentInParent<ArGraveyardHit>();
                    if (gy != null && gy.gameObject.activeInHierarchy)
                    {
                        var yours = gy.Disk != null ? gy.Disk.IsPlayerSide : gy.IsPlayerSide;
                        OnGraveyardTapped?.Invoke(yours);
                        Debug.Log("[WRLDZ AR] GY tap · you=" + yours);
                        return;
                    }

                    // Banished / RFG floater (M1-adjacent) — Dilbot owns DuelUI open/browser wire-up.
                    var ban = h.collider.GetComponentInParent<ArBanishedHit>();
                    if (ban != null && ban.gameObject.activeInHierarchy)
                    {
                        var yours = ban.Floater != null ? ban.Floater.IsPlayerSide : ban.IsPlayerSide;
                        OnBanishedTapped?.Invoke(yours);
                        Debug.Log("[WRLDZ AR] Banished tap · you=" + yours);
                        return;
                    }

                    var phase = h.collider.GetComponentInParent<ArDiskPhaseHit>();
                    if (phase != null && phase.gameObject.activeInHierarchy)
                    {
                        OnPhaseButtonTapped?.Invoke(phase.Kind);
                        Debug.Log("[WRLDZ AR] Phase tap · " + phase.Kind);
                        return;
                    }

                    var glance = h.collider.GetComponentInParent<ArOppGlanceHit>();
                    if (glance != null && glance.Glance != null && glance.gameObject.activeInHierarchy)
                    {
                        if (glance.Glance.TryPickGy(ray))
                        {
                            // The board carries only the opponent's side; your GY lives on your disk.
                            if (OnOppGlanceGyTapped != null) OnOppGlanceGyTapped();
                            else OnGraveyardTapped?.Invoke(false);
                            Debug.Log("[WRLDZ AR] Glance GY tap · you=false");
                            return;
                        }
                        if (glance.Glance.TryPick(ray, out var gc, out var face) && gc != null)
                        {
                            OnOppGlanceCardTapped?.Invoke(gc, face);
                            Debug.Log("[WRLDZ AR] Opp glance tap · " + gc.Name + " · face=" + face);
                            return;
                        }
                    }
                }

                if (OnFieldCardTapped == null) continue;

                var diskCard = h.collider.GetComponentInParent<ArDiskCardVisual>();
                if (diskCard != null && diskCard.Card != null)
                {
                    if (h.distance > frontCardDist + 0.05f) continue;
                    if (h.distance < frontCardDist) frontCardDist = h.distance;
                    // score against the pick volume (an S/T card's is its lip), not the card's buried centre
                    var score = LateralToRay(ray, h.collider.bounds.center) + h.distance * 0.02f;
                    if (score >= bestCardScore) continue;
                    bestCardScore = score;
                    bestDisk = diskCard;
                    bestFloat = null;
                    continue;
                }

                // Just-snapped floating pad card (before DiskCardVisual takes over)
                var floating = h.collider.GetComponentInParent<ArFloatingCard>();
                if (floating != null && floating.IsLockedToZone && floating.Card != null)
                {
                    if (h.distance > frontCardDist + 0.05f) continue;
                    if (h.distance < frontCardDist) frontCardDist = h.distance;
                    var score = LateralToRay(ray, floating.transform.position) + h.distance * 0.02f;
                    if (score >= bestCardScore) continue;
                    bestCardScore = score;
                    bestFloat = floating;
                    bestDisk = null;
                    continue;
                }

                if (emptyPad != null || emptyZone != null) continue;
                var pad = h.collider.GetComponentInParent<ArLegalZonePad>();
                if (pad != null && pad.OnDisk && pad.gameObject.activeInHierarchy)
                {
                    emptyPad = pad;
                    continue;
                }

                var zoneHit = h.collider.GetComponentInParent<ArDiskZone>();
                if (zoneHit != null && zoneHit.IsEmpty && zoneHit.IsPlayerSide)
                    emptyZone = zoneHit;
            }

            if (bestHolo != null && bestHolo.Card != null)
            {
                WrldzAudio.PlayCardTap();
                OnFieldCardTapped?.Invoke(bestHolo.Card, bestHolo.PlayerSide, bestHolo.IsMonster);
                Debug.Log($"[WRLDZ AR] Field tap holo · {bestHolo.Card.Name} · you={bestHolo.PlayerSide}");
                return;
            }

            if (bestDisk != null && bestDisk.Card != null)
            {
                WrldzAudio.PlayCardTap();
                var yours = ResolveIsPlayerSide(bestDisk.Card, null, bestDisk.transform);
                OnFieldCardTapped?.Invoke(bestDisk.Card, yours, bestDisk.IsMonsterCard);
                Debug.Log($"[WRLDZ AR] Field tap disk · {bestDisk.Card.Name} · you={yours}");
                return;
            }

            if (bestFloat != null && bestFloat.Card != null)
            {
                WrldzAudio.PlayCardTap();
                var zone = bestFloat.LockedZone;
                var isMon = zone == null || zone.Kind == ArDuelZoneKind.Monster
                            || zone.Kind == ArDuelZoneKind.PendulumLeft
                            || zone.Kind == ArDuelZoneKind.PendulumRight;
                var yours = ResolveIsPlayerSide(bestFloat.Card, null, bestFloat.transform);
                OnFieldCardTapped?.Invoke(bestFloat.Card, yours, isMon);
                Debug.Log($"[WRLDZ AR] Field tap float-lock · {bestFloat.Card.Name} · you={yours}");
                return;
            }

            // Forgiving proximity pick: nearest occupied pad to the ray
            if (OnFieldCardTapped != null &&
                TryNearestZoneCard(ray, out var nearCard, out var nearYours, out var nearMon))
            {
                WrldzAudio.PlayCardTap();
                OnFieldCardTapped.Invoke(nearCard, nearYours, nearMon);
                Debug.Log($"[WRLDZ AR] Field tap proximity · {nearCard.Name} · you={nearYours}");
                return;
            }

            if (emptyPad != null)
            {
                OnEmptyZoneTapped?.Invoke(emptyPad.Kind, emptyPad.Index);
                Debug.Log($"[WRLDZ AR] Empty disk zone tap · {emptyPad.Kind}[{emptyPad.Index}]");
                return;
            }

            if (emptyZone != null)
            {
                OnEmptyZoneTapped?.Invoke(ToRulesKind(emptyZone.Kind), emptyZone.Index);
                Debug.Log($"[WRLDZ AR] Empty disk zone tap · {emptyZone.Kind}[{emptyZone.Index}]");
            }
        }

        static float LateralToRay(Ray ray, Vector3 point)
        {
            var to = point - ray.origin;
            var along = Vector3.Dot(to, ray.direction);
            if (along < 0.02f) return float.MaxValue;
            var closest = ray.origin + ray.direction * along;
            return Vector3.Distance(closest, point);
        }

        bool TryNearestZoneCard(Ray ray, out CardInstance card, out bool yours, out bool isMonster)
        {
            card = null;
            yours = true;
            isMonster = true;
            const float maxDist = 0.035f;
            var best = maxDist;
            ArDiskZone bestZone = null;
            Transform bestVisual = null;

            void ConsiderDisk(ArDuelDiskRig disk)
            {
                if (disk?.Zones == null) return;
                foreach (var z in disk.Zones)
                {
                    if (z == null || z.Occupant == null) continue;
                    var p = z.WorldPeekPoint;
                    var to = p - ray.origin;
                    var along = Vector3.Dot(to, ray.direction);
                    if (along < 0.05f) continue;
                    var closest = ray.origin + ray.direction * along;
                    var d = Vector3.Distance(closest, p);
                    if (d >= best) continue;
                    best = d;
                    bestZone = z;
                    bestVisual = z.transform;
                }
            }

            ConsiderDisk(PlayerDisk);
            ConsiderDisk(OppDisk);

            if (bestZone != null)
            {
                card = bestZone.Occupant;
                isMonster = bestZone.Kind == ArDuelZoneKind.Monster
                            || bestZone.Kind == ArDuelZoneKind.PendulumLeft
                            || bestZone.Kind == ArDuelZoneKind.PendulumRight;
                yours = ResolveIsPlayerSide(card, null, bestVisual);
                return card != null;
            }

            return card != null;
        }

        /// <summary>Engine ownership is truth; disk parent is the fallback.</summary>
        bool ResolveIsPlayerSide(CardInstance card, bool? arenaHint, Transform visual)
        {
            if (card != null && Engine != null)
            {
                var you = Engine.Player;
                var opp = Engine.Opponent;
                if (you != null)
                {
                    if (you.TryFindMonster(card, out _) || you.TryFindSpellTrap(card, out _))
                        return true;
                    if (you.FieldSpellZone?.Occupant == card) return true;
                }

                if (opp != null)
                {
                    if (opp.TryFindMonster(card, out _) || opp.TryFindSpellTrap(card, out _))
                        return false;
                    if (opp.FieldSpellZone?.Occupant == card) return false;
                }
            }

            if (visual != null)
            {
                if (OppDisk != null && visual.IsChildOf(OppDisk.transform))
                    return false;
                if (PlayerDisk != null && visual.IsChildOf(PlayerDisk.transform))
                    return true;
            }

            return arenaHint ?? true;
        }

        /// <summary>Card back to the fan: its own spring return, with the hand riding it before letting go.</summary>
        void ReturnCardToHand(ArFloatingCard card)
        {
            if (card == null) return;
            if (_placedByHand == card) _placedByHand = null;
            card.ReturnToHandRest();
            if (Hand != null && Hand.HandLive) Hand.FloatingHand.EndCarry(ArDiskMotion.PadSnap);
        }

        void EndDrag()
        {
            if (_drag == null) return;
            var card = _drag;
            // IsDragging stays on until the card is placed or returned: the hand keeps holding it through the
            // Summon / Set chooser, hovering over the zone

            var byHand = _placedByHand == card;
            var preferSet = PreferSet || (byHand && _faceDown);
            var reach = byHand ? HandZoneReach(card) : UsingLenses() ? 0.09f : 0.42f;
            var zone = PlayerDisk?.FindNearestValid(
                card.transform.position, reach, card.Card, Engine,
                Engine.Player, preferSet);

            PlayerDisk?.SetZonesHot(false);
            _hoverZone?.SetHot(false);
            _hoverZone = null;
            Arena?.ClearDropPreview();
            _drag = null;

            // a real hand has to actually put the card on the disk: no generous second look
            if (zone == null && !UsingLenses() && !byHand)
            {
                zone = PlayerDisk?.FindNearestValid(
                    card.transform.position, 1.15f, card.Card, Engine,
                    Engine.Player, PreferSet);
            }

            if (zone == null)
            {
                Hud?.ShowNotice("Not a legal zone — card returned to hand.");
                Engine.Log("AR snap: no legal zone under the card.");
                ReturnCardToHand(card);
                if (card.Card?.Def != null && card.Card.Def.IsFieldSpell)
                    PlayerDisk?.EndFieldSpellDrag(false);
                ClearLegalGlow();
                return;
            }

            var legal = ArSnapRules.QueryLegal(Engine, Engine.Player, card.Card, zone);
            if (!legal.Any)
            {
                Hud?.ShowNotice(string.IsNullOrEmpty(legal.Hint)
                    ? "Not a legal zone — card returned to hand."
                    : legal.Hint);
                Engine.Log($"AR snap illegal: {legal.BlockedReason}");
                ReturnCardToHand(card);
                if (card.Card?.Def != null && card.Card.Def.IsFieldSpell)
                    PlayerDisk?.EndFieldSpellDrag(false);
                return;
            }

            if (card.Card?.Def != null && card.Card.Def.IsFieldSpell)
                PlayerDisk?.EndFieldSpellDrag(zone.Kind == ArDuelZoneKind.FieldSpell);

            // A real hand says how it is played the way a duelist does: face-down on the disk is a Set,
            // face-up is a Summon / activation. Only a play the card cannot make falls back to the chooser.
            if (byHand)
            {
                var canUp = legal.CanSummonAtk || legal.CanActivateSpell;
                var canSet = legal.CanSetMonster || legal.CanSetSpellTrap;
                if (canSet && (_faceDown || !canUp))
                {
                    FinishCommit(card, zone, preferSet: true);
                    return;
                }

                if (canUp && !_faceDown)
                {
                    FinishCommit(card, zone, preferSet: false);
                    return;
                }
            }

            // Shift / explicit Set: skip the chooser
            if (PreferSet && (legal.CanSetMonster || legal.CanSetSpellTrap))
            {
                FinishCommit(card, zone, preferSet: true);
                return;
            }

            // Always offer a choose/EXIT sheet — even when only one play is legal.
            var title =
                $"{card.Card.Name} — {ArZoneLayout.ZoneDisplayLabel(zone.Kind, zone.Index)}";
            // a one-shot Spell is activated wherever a zone is free (shown to the opponent); only a Set uses this one
            if (legal.CanActivateSpell && ArSnapRules.RevealsFromHand(card.Card))
                title = legal.CanSetSpellTrap
                    ? $"{card.Card.Name} — Activate, or Set in {ArZoneLayout.ZoneDisplayLabel(zone.Kind, zone.Index)}"
                    : $"{card.Card.Name} — Activate";
            if (legal.CanSummonAtk && legal.CanSetMonster)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title,
                    "Summon ATK", false,
                    "Set Face-Down", true);
                return;
            }

            if (legal.CanActivateSpell && legal.CanSetSpellTrap)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title,
                    "Activate", false,
                    "Set (face-down)", true);
                return;
            }

            if (legal.CanSummonAtk)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title, "Summon ATK", false, null, false);
                return;
            }

            if (legal.CanActivateSpell)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title, "Activate", false, null, false);
                return;
            }

            if (legal.CanSetMonster || legal.CanSetSpellTrap)
            {
                ApplyLegalGlow(card.Card, zone);
                AskChoice(card, zone, title, "Set Face-Down", true, null, false);
                return;
            }

            FinishCommit(card, zone, preferSet: false);
        }

        void AskChoice(ArFloatingCard card, ArDiskZone zone, string title,
            string a, bool aIsSet, string b, bool bIsSet)
        {
            _pendingCard = card;
            _pendingZone = zone;
            EnsureHud();
            Engine.Log($"AR: choose how to play {card.Card?.Name}.");
            Hud?.ShowChoice(title, a, b,
                onA: () => ResolveChoice(aIsSet),
                onB: () => ResolveChoice(bIsSet),
                onCancel: CancelPending);
        }

        void ResolveChoice(bool preferSet)
        {
            var card = _pendingCard;
            var zone = _pendingZone;
            _pendingCard = null;
            _pendingZone = null;
            Hud?.HideChoice();
            if (card == null || zone == null)
            {
                ClearLegalGlow();
                return;
            }

            FinishCommit(card, zone, preferSet);
        }

        void CancelPending()
        {
            var card = _pendingCard;
            _pendingCard = null;
            _pendingZone = null;
            Hud?.HideChoice();
            Hud?.HideHint();
            if (card?.Card?.Def != null && card.Card.Def.IsFieldSpell)
                PlayerDisk?.EndFieldSpellDrag(false);
            ReturnCardToHand(card);
            ClearLegalGlow();
            Engine?.Log("AR play cancelled — card returned to hand.");
        }

        /// <summary>
        /// A legal play is carried to the zone by the floating hand and committed when the card touches it;
        /// without the hand (opponent view, old fan, hand busy) it commits at once. An illegal play never leaves
        /// the hand.
        /// </summary>
        void FinishCommit(ArFloatingCard card, ArDiskZone zone, bool preferSet)
        {
            var check = ArSnapRules.Validate(Engine, Engine.Player, card.Card, zone, preferSet);
            if (!check.Ok)
            {
                RejectPlay(card, check.Reason, returnToHand: true);
                return;
            }

            // A one-shot Spell activated from the hand goes to the GY after resolving: the hand shows it to the
            // opponent instead of setting it on the dropped zone, and the engine seats it in a free S/T Zone.
            var reveal = !preferSet && check.Orientation != ArZoneOrientation.FaceDownSet &&
                         ArSnapRules.RevealsFromHand(card.Card) &&
                         (zone.Kind == ArDuelZoneKind.SpellTrap);
            if (_placedByHand == card)
            {
                // the player's own fingers carried it: the disk seats it where they let go, no ghost gesture
                _placedByHand = null;
                if (reveal) CommitRevealed(card, zone, returnOnFail: true, removeCard: true);
                else CommitNow(card, zone, preferSet, returnOnFail: true);
                return;
            }

            var hand = Hand != null && Hand.HandLive ? Hand.FloatingHand : null;
            if (reveal)
            {
                if (hand != null && hand.PlayReveal(card,
                        atReveal: () => CommitRevealed(card, zone, returnOnFail: false),
                        done: _ => _gestureCard = null))
                {
                    _gestureCard = card;
                    Hud?.HideHint();
                    ClearLegalGlow();
                    return;
                }

                Debug.Log($"[WRLDZ AR] Reveal not started ({(hand == null ? "no live hand" : $"drawing={hand.IsDrawing} riffling={hand.IsRiffling} placing={hand.IsPlacing} busy={hand.IsBusy}")}): {card.Card?.Name} activates directly");
                CommitRevealed(card, zone, returnOnFail: true, removeCard: true);
                return;
            }

            if (hand != null && hand.PlayPlace(card, zone, check.Orientation,
                    atContact: () => CommitNow(card, zone, preferSet, returnOnFail: false),
                    done: _ => _gestureCard = null))
            {
                _gestureCard = card;
                Hud?.HideHint();
                return;
            }

            CommitNow(card, zone, preferSet, returnOnFail: true);
        }

        void RejectPlay(ArFloatingCard card, string reason, bool returnToHand)
        {
            Hud?.ShowNotice(reason ?? "Illegal play.");
            Debug.Log($"[WRLDZ AR] Snap rejected: {reason}");
            Engine.Log($"AR snap illegal: {reason}");
            if (card?.Card?.Def != null && card.Card.Def.IsFieldSpell)
                PlayerDisk?.EndFieldSpellDrag(false);
            if (returnToHand) ReturnCardToHand(card);
            ClearLegalGlow();
        }

        bool CommitNow(ArFloatingCard card, ArDiskZone zone, bool preferSet, bool returnOnFail)
        {
            if (card == null || zone == null) return false;
            card.IsDragging = false;
            var result = ArSnapRules.Commit(Engine, Engine.Player, card.Card, zone, preferSet);
            if (result.Ok && result.CommittedToEngine)
            {
                if (result.Orientation == ArZoneOrientation.FaceDownSet)
                    WrldzAudio.PlayCardSet();
                else
                    WrldzAudio.PlayCardPlay();
                zone.SnapLock(card, result.Orientation);
                Hand?.RemoveCard(card);
                var showFace = result.Orientation != ArZoneOrientation.FaceDownSet;
                card.RefreshArtFace(showFace);
                var how = result.Orientation == ArZoneOrientation.FaceDownSet
                    ? "Set face-down Defense"
                    : "face-up Attack";
                Hud?.HideHint();
                Debug.Log(
                    $"[WRLDZ AR] Snap OK → {zone.Kind}[{zone.Index}] {card.Card?.Name} · {how}");
                Engine.Log($"AR disk: {card.Card?.Name} → {zone.Kind} zone {zone.Index} ({how}).");
                ClearLegalGlow();
                return true;
            }

            RejectPlay(card, result.Reason, returnOnFail);
            return false;
        }

        /// <summary>
        /// Activate a one-shot Spell from the hand without seating the floating card on <paramref name="zone"/>: the
        /// engine puts it in a free Spell/Trap Zone and the disk shows it appear there. The floating card leaves the
        /// fan (the reveal dissolves it; <paramref name="removeCard"/> destroys it at once when no reveal runs).
        /// </summary>
        bool CommitRevealed(ArFloatingCard card, ArDiskZone zone, bool returnOnFail, bool removeCard = false)
        {
            if (card == null || zone == null) return false;
            card.IsDragging = false;
            var result = ArSnapRules.Commit(Engine, Engine.Player, card.Card, zone, preferSet: false);
            if (result.Ok && result.CommittedToEngine)
            {
                WrldzAudio.PlayCardPlay();
                Hand?.RemoveCard(card);
                Hud?.HideHint();
                Debug.Log($"[WRLDZ AR] Reveal OK → {card.Card?.Name} activated (engine picks the Spell/Trap Zone)");
                if (removeCard) ArObjectUtil.Destroy(card.gameObject);   // no reveal running to dissolve it
                Engine.Log($"AR: {card.Card?.Name} shown to the opponent and activated.");
                ClearLegalGlow();
                return true;
            }

            RejectPlay(card, result.Reason, returnOnFail);
            return false;
        }

        void ApplyLegalGlow(CardInstance card, ArDiskZone hover)
        {
            var ix = GetComponent<ArDuelInteractionSystem>();
            if (ix == null) ix = FindFirstObjectByType<ArDuelInteractionSystem>();
            ix?.ShowLegalPlacements(card, hover);
        }

        void ClearLegalGlow()
        {
            var ix = GetComponent<ArDuelInteractionSystem>();
            if (ix == null) ix = FindFirstObjectByType<ArDuelInteractionSystem>();
            ix?.ClearLegalPlacements();
        }

        void EnsureHud()
        {
            if (Hud != null) return;
            RectTransform parent = ViewportRect;
            if (parent == null)
            {
                var canvas = FindFirstObjectByType<Canvas>();
                parent = canvas != null ? canvas.transform as RectTransform : null;
            }

            Hud = ArDragActionHud.Create(parent);
        }

        static string StartHint(CardInstance card)
        {
            if (card?.Def == null) return "Drag onto your Duel Disk";
            if (card.Def.IsMonster)
                return $"{card.Name} · drop on a Monster Zone · you will choose Summon or Set";
            if (card.Def.IsTrap)
                return $"{card.Name} · drop on a Spell/Trap Zone to Set";
            if (card.Def.IsFieldSpell)
                return $"{card.Name} · drop on the Field Slot to activate";
            if (card.Def.IsSpell)
                return $"{card.Name} · drop on a Spell/Trap Zone · Activate or Set";
            return $"{card.Name} · drop on a legal zone";
        }

        bool UsingLenses() => PlayerDisk != null && PlayerDisk.Tracker is XrWorldArmTracker;

        Plane DragPlane()
        {
            if (UsingLenses() && PlayerDisk != null)
                return new Plane(PlayerDisk.transform.up, PlayerDisk.transform.position);
            var origin = _drag != null ? _drag.transform.position : Vector3.zero;
            var normal = StageCamera != null ? -StageCamera.transform.forward : Vector3.up;
            return new Plane(normal, origin);
        }

        // ------------------------------------------------------------------ real hands (mixed reality)

        /// <summary>
        /// Hands override (Editor verification with <see cref="SimulatedHandInput"/>); otherwise the tracker's
        /// OpenXR hands (<see cref="XrWorldArmTracker"/>). Either hand selects: a fingertip at a card peeks it, a
        /// pinch takes it, a pinch let go in place taps it (the card menu), a pinch carried to a disk zone plays
        /// it, and the way the card faces when it is let go picks Summon / Set. A pinch away from every card
        /// points: the hand's aim ray takes field and zone taps like the controller trigger.
        /// </summary>
        public IArHandInput Hands;

        IArHandInput HandInput => Hands ?? PlayerDisk?.Tracker as IArHandInput;

        /// <summary>A real hand is carrying a card (screen and controller input leave it alone).</summary>
        public bool RealHandDrag => _realHandDrag;

        bool HandOwnsCard => _realHandDrag || (_grabHand >= 0 && !_rayHand);

        /// <summary>Pinch closes past this and opens below <see cref="PinchOpen"/>: no flicker at the edge.</summary>
        const float PinchClose = 0.85f, PinchOpen = 0.5f;
        /// <summary>A pinch that travels this far (card heights) before it opens is a carry, not a tap.</summary>
        const float CarryStartHeights = 0.18f;
        /// <summary>Pinch reach to take a card and fingertip reach to peek one (card heights from its edge).</summary>
        const float GrabReachHeights = 0.35f, PeekReachHeights = 0.3f;
        /// <summary>A card let go this close to a zone (card heights) lands there (0.09 m for a real card).</summary>
        const float ZoneReachHeights = 1.05f;
        /// <summary>A carried card waits this long for a hand that dropped out of tracking, then goes home.</summary>
        const float HandLostGrace = 0.35f;
        /// <summary>Pose smoothing of a carried card (1/s): steady against tracking jitter, still under the fingers.</summary>
        const float HandFollowRate = 38f;

        readonly ArHandPose[] _hands = new ArHandPose[2];
        readonly bool[] _pinched = new bool[2];
        readonly bool[] _wasPinched = new bool[2];
        /// <summary>0 = left, 1 = right: the hand that armed a card or points; -1 = none.</summary>
        int _grabHand = -1;
        bool _rayHand;
        bool _realHandDrag;
        Vector3 _grabStart;
        float _handLostAt = -1f;
        Vector3 _grabLocalPos;
        Quaternion _grabLocalRot;
        ArFloatingCard _placedByHand;
        bool _faceDown;

        bool PollHands()
        {
            var input = HandInput;
            var any = false;
            for (var h = 0; h < 2; h++)
            {
                _wasPinched[h] = _pinched[h];
                if (input == null || !input.TryGetHand(h == 0, out _hands[h]) || !_hands[h].Tracked)
                {
                    _hands[h] = default;
                    _pinched[h] = false;
                    continue;
                }

                any = true;
                var v = _hands[h].PinchValue;
                _pinched[h] = _pinched[h] ? v > PinchOpen : v >= PinchClose;
            }

            if (_realHandDrag)
            {
                TickHandDrag();
                return true;
            }

            if (_grabHand >= 0)
            {
                TickHandArmed();
                return true;
            }

            if (!any || Hand == null)
            {
                // tracking gone: a card the fingertip was peeking must not stay lifted
                if (_handPeek && Hand != null && Hand.HandLive) Hand.FloatingHand.SetHover(null);
                _handPeek = false;
                return false;
            }

            // the reach tests below read collider poses; physics auto-sync is off, so sync once per frame
            if (_syncedFrame != Time.frameCount)
            {
                Physics.SyncTransforms();
                _syncedFrame = Time.frameCount;
            }

            if (_drag != null || _pendingCard != null || GestureBusy) return true;

            // peek: the card a fingertip is at rises out of the fan (the fan hand only from a card's top half,
            // where its own fingers are not)
            ArFloatingCard peek = null;
            var peekD = float.MaxValue;
            for (var h = 0; h < 2; h++)
            {
                if (!_hands[h].Tracked) continue;
                var c = NearestHandCard(_hands[h].Poke.position, FanHand(h), PeekReachHeights, out var d);
                if (c != null && d < peekD)
                {
                    peek = c;
                    peekD = d;
                }
            }

            if (Hand.HandLive) Hand.FloatingHand.SetHover(peek);
            _handPeek = peek != null;

            for (var h = 0; h < 2; h++)
            {
                if (!_pinched[h] || _wasPinched[h]) continue;
                var pinch = _hands[h].Pinch.position;
                var card = NearestHandCard(pinch, FanHand(h), GrabReachHeights, out _);
                _grabHand = h;
                if (card != null)
                {
                    _rayHand = false;
                    _armedHand = card;
                    _grabStart = pinch;
                    if (Hand.HandLive) Hand.FloatingHand.SetHover(card);
                    _handPeek = true;
                }
                else
                {
                    // nothing within reach: the hand points (disk cards, zones, arena targets)
                    _rayHand = true;
                    BeginFromRay(AimRay(_hands[h]));
                }

                break;
            }

            return true;
        }

        void TickHandArmed()
        {
            var h = _grabHand;
            var hand = _hands[h];
            if (_rayHand)
            {
                var ray = hand.Tracked ? AimRay(hand) : default;
                if (hand.Tracked && _pinched[h])
                {
                    PointerMovedFromRay(ray);
                    // a card pulled out along the ray is still the pointer's: hand it back to the ray path
                    return;
                }

                if (hand.Tracked) EndFromRay(ray);
                else
                {
                    if (_drag != null) CancelHandDrag();
                    _armedHand = null;
                    _zoneTapArmed = false;
                    _pointerDown = false;
                }

                _grabHand = -1;
                _rayHand = false;
                return;
            }

            var card = _armedHand;
            if (card == null || !hand.Tracked)
            {
                if (_handPeek && Hand != null && Hand.HandLive) Hand.FloatingHand.SetHover(null);
                _handPeek = false;
                _armedHand = null;
                _grabHand = -1;
                return;
            }

            if (!_pinched[h])
            {
                // opened where it closed: a tap (lift + play / inspect menu), exactly like a screen tap
                _armedHand = null;
                _grabHand = -1;
                if (card.Card != null)
                {
                    Debug.Log($"[WRLDZ AR] Hand pinch tap: {card.Card.Name}");
                    ToggleHandLift(card);
                    OnHandCardTapped?.Invoke(card.Card);
                }

                return;
            }

            if ((hand.Pinch.position - _grabStart).magnitude > CarryStartHeights * CardHeight(card))
                BeginHandCarry(card, hand);
        }

        void BeginHandCarry(ArFloatingCard card, ArHandPose hand)
        {
            StartHandDrag(card, realHand: true);
            if (_drag != card)
            {
                _armedHand = null;
                _grabHand = -1;
                return;
            }

            _realHandDrag = true;
            _handLostAt = -1f;
            // The fingers hold the card where they closed on it: keep that point under the pinch in the hand's frame.
            // The card catches up the carry threshold the fingers already travelled (and any gap to its edge) through
            // the follow smoothing instead of sliding under them.
            var grip = hand.Grip.rotation;
            var col = card.GetComponent<BoxCollider>();
            var held = col != null ? col.ClosestPoint(_grabStart) : _grabStart;
            _grabLocalPos = Quaternion.Inverse(grip) * (card.transform.position - held);
            _grabLocalRot = Quaternion.Inverse(grip) * card.transform.rotation;
        }

        void TickHandDrag()
        {
            var h = _grabHand;
            var hand = h >= 0 ? _hands[h] : default;
            if (_drag == null)
            {
                _realHandDrag = false;
                _grabHand = -1;
                return;
            }

            if (!hand.Tracked)
            {
                // tracking blinks (the hand passes behind the other one, leaves the cameras): hold the card still
                if (_handLostAt < 0f) _handLostAt = Time.unscaledTime;
                if (Time.unscaledTime - _handLostAt > HandLostGrace) CancelHandDrag();
                return;
            }

            _handLostAt = -1f;
            var grip = hand.Grip.rotation;
            var pos = hand.Pinch.position + grip * _grabLocalPos;
            var rot = grip * _grabLocalRot;
            var k = 1f - Mathf.Exp(-HandFollowRate * Time.unscaledDeltaTime);
            var t = _drag.transform;
            t.SetPositionAndRotation(Vector3.Lerp(t.position, pos, k), Quaternion.Slerp(t.rotation, rot, k));

            var faceDown = IsFaceDown(_drag);
            UpdateDragHover(HandZoneReach(_drag), PreferSet || faceDown, hint: false);
            if (_faceDown != faceDown || _hoverZone != _hintZone)
            {
                _faceDown = faceDown;
                _hintZone = _hoverZone;
                EnsureHud();
                Hud?.ShowHint(_hoverZone != null
                    ? ArSnapRules.QueryLegal(Engine, Engine.Player, _drag.Card, _hoverZone).Hint +
                      (faceDown ? " · face-down" : " · face-up")
                    : HandHint(_drag.Card));
            }

            if (!_pinched[h]) EndHandDrag();
        }

        ArDiskZone _hintZone;

        void EndHandDrag()
        {
            var card = _drag;
            _realHandDrag = false;
            _grabHand = -1;
            _hintZone = null;
            if (card == null) return;
            _faceDown = IsFaceDown(card);
            _placedByHand = card;
            Debug.Log($"[WRLDZ AR] Hand release: {card.Card?.Name} {(_faceDown ? "face-down" : "face-up")}");
            EndDrag();
            // an illegal release returned it (ReturnCardToHand cleared the mark); a chooser keeps it until then
        }

        /// <summary>The carrying hand is gone for good: the card goes back to the fan, nothing is played.</summary>
        void CancelHandDrag()
        {
            var card = _drag;
            _realHandDrag = false;
            _grabHand = -1;
            _rayHand = false;
            _hintZone = null;
            _drag = null;
            _pointerDown = false;
            PlayerDisk?.SetZonesHot(false);
            _hoverZone?.SetHot(false);
            _hoverZone = null;
            Arena?.ClearDropPreview();
            Hud?.HideHint();
            ClearLegalGlow();
            if (card == null) return;
            if (card.Card?.Def != null && card.Card.Def.IsFieldSpell)
                PlayerDisk?.EndFieldSpellDrag(false);
            Debug.Log($"[WRLDZ AR] Hand lost: {card.Card?.Name} back to the hand");
            ReturnCardToHand(card);
        }

        /// <summary>The right hand wears the fan: it can only take a card by the top, above its own fingers.</summary>
        bool FanHand(int h) => h == 1 && Hand != null && Hand.HandLive && Hand.FloatingHand.IsTrackedGrip;

        /// <summary>
        /// The hand card nearest <paramref name="p"/> within <paramref name="reachHeights"/> card heights of its
        /// edge. Overlapping fan cards at about the same distance go to the one in front (nearest the eye).
        /// </summary>
        ArFloatingCard NearestHandCard(Vector3 p, bool topHalfOnly, float reachHeights, out float dist)
        {
            dist = float.MaxValue;
            if (Hand == null || (Hand.HandLive && Hand.FloatingHand.IsRiffling)) return null;
            var eye = StageCamera != null ? StageCamera.transform.position : p;
            ArFloatingCard best = null;
            var bestEye = float.MaxValue;
            var cards = Hand.Cards;
            for (var i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.IsLockedToZone || c.IsDragging || c.IsDrawArriving) continue;
                var col = c.GetComponent<BoxCollider>();
                if (col == null || !col.enabled || !c.gameObject.activeInHierarchy) continue;
                var hgt = CardHeight(c);
                if (hgt <= 1e-5f) continue;
                if (topHalfOnly && c.transform.InverseTransformPoint(p).y < 0f) continue;
                var d = (col.ClosestPoint(p) - p).magnitude / hgt;
                if (d > reachHeights) continue;
                var e = (c.transform.position - eye).sqrMagnitude;
                if (d < dist - 0.04f || (d < dist + 0.04f && e < bestEye))
                {
                    best = c;
                    dist = Mathf.Min(d, dist);
                    bestEye = e;
                }
            }

            return best;
        }

        static float CardHeight(ArFloatingCard c) =>
            c != null ? ArFloatingCard.BodyLocalSize.y * Mathf.Abs(c.transform.lossyScale.y) : 0f;

        static float HandZoneReach(ArFloatingCard c) => ZoneReachHeights * CardHeight(c);

        /// <summary>Hand cards show their face on the +forward side (turned 180° to face the eye in the fan).</summary>
        bool IsFaceDown(ArFloatingCard c)
        {
            if (c == null || StageCamera == null) return false;
            var toEye = StageCamera.transform.position - c.transform.position;
            return Vector3.Dot(c.transform.forward, toEye) < 0f;
        }

        static Ray AimRay(ArHandPose hand) => new(hand.Aim.position, hand.Aim.rotation * Vector3.forward);

        static string HandHint(CardInstance card)
        {
            if (card?.Def == null) return "Place it on a zone";
            if (card.Def.IsTrap) return $"{card.Name} · lay it face-down on a Spell/Trap Zone";
            if (card.Def.IsFieldSpell) return $"{card.Name} · place it on the Field Slot";
            return $"{card.Name} · place it face-up to play · face-down to Set";
        }

        bool PollLensAim()
        {
            if (PlayerDisk?.Tracker is not XrWorldArmTracker xr) return false;
            if (!xr.TryGetAimRay(out var ray)) return false;
            if (xr.AimHeld)
            {
                if (!_lensAim)
                {
                    _lensAim = true;
                    BeginFromRay(ray);
                }
                else
                    PointerMovedFromRay(ray);
                return true;
            }

            if (_lensAim)
            {
                _lensAim = false;
                EndFromRay(ray);
            }

            return false;
        }

        public void BeginFromRay(Ray ray)
        {
            if (StageCamera == null || Engine == null) return;
            if (_drag != null || _pendingCard != null || _pointerDown) return;
            _pointerDown = true;
            _pointerDownTime = Time.unscaledTime;
            if (HighlightsActive && TryPickLegalZone(ray, out var zk, out var zi))
            {
                _zoneTapArmed = true;
                _zoneTapKind = zk;
                _zoneTapIndex = zi;
                return;
            }

            if (Hand != null)
            {
                var hits = Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var card = h.collider != null
                        ? h.collider.GetComponentInParent<ArFloatingCard>()
                        : null;
                    if (card != null && !card.IsLockedToZone && !card.IsDrawArriving &&
                        (Hand == null || (card.transform.IsChildOf(Hand.transform) &&
                                          !(Hand.HandLive && Hand.FloatingHand.IsRiffling))))
                    {
                        _armedHand = card;
                        _lensHit = ray.GetPoint(Mathf.Min(h.distance, 2f));
                        return;
                    }
                }
            }
        }

        Vector3 _lensHit;

        void PointerMovedFromRay(Ray ray)
        {
            if (_drag != null)
            {
                MoveAlong(ray);
                return;
            }

            if (_armedHand == null || _zoneTapArmed) return;
            var plane = new Plane(PlayerDisk != null ? PlayerDisk.transform.up : Vector3.up,
                _armedHand.transform.position);
            if (!plane.Raycast(ray, out var enter)) return;
            var hit = ray.GetPoint(enter);
            if (_lensHit == Vector3.zero) _lensHit = hit;
            if ((hit - _lensHit).sqrMagnitude < 0.012f * 0.012f) return;
            StartHandDrag(_armedHand);
            if (_drag != null)
                MoveAlong(ray);
        }

        public void EndFromRay(Ray ray)
        {
            if (_drag != null)
            {
                MoveAlong(ray);
                EndDrag();
            }
            else if (_armedHand != null)
            {
                var held = _armedHand;
                var card = held.Card;
                _armedHand = null;
                if (card != null)
                {
                    ToggleHandLift(held);
                    OnHandCardTapped?.Invoke(card);
                }
            }
            else if (_zoneTapArmed)
            {
                OnEmptyZoneTapped?.Invoke(_zoneTapKind, _zoneTapIndex);
            }
            else if (_pointerDown && Time.unscaledTime - _pointerDownTime < 0.8f)
            {
                // a short pinch at the disk: same card / GY / control menus as a screen tap
                ClearHandLift();
                TryFieldTap(ray);
            }

            _zoneTapArmed = false;
            _armedHand = null;
            _pointerDown = false;
            _lensHit = Vector3.zero;
        }

        void MoveAlong(Ray ray)
        {
            if (_drag == null) return;
            var plane = DragPlane();
            if (plane.Raycast(ray, out var enter) && enter > 0f)
            {
                var up = PlayerDisk != null ? PlayerDisk.transform.up : Vector3.up;
                _drag.transform.position = ray.GetPoint(enter) + up * 0.008f;
            }

            UpdateDragHover(UsingLenses() ? 0.09f : 0.42f, PreferSet, hint: false);
        }

        bool TryScreenToRay(Vector2 screen, out Ray ray)
        {
            ray = default;
            if (StageCamera == null) return false;

            // If AR is shown via RT RawImage, map screen point into viewport UV → camera ray
            if (ViewportRect != null)
            {
                // Overlay canvas → cam null; otherwise try event/press camera
                Camera uiCam = null;
                var canvas = ViewportRect.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    uiCam = canvas.worldCamera != null ? canvas.worldCamera : StageCamera;

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        ViewportRect, screen, uiCam, out var local))
                    return false;
                var rect = ViewportRect.rect;
                if (rect.width < 1f || rect.height < 1f) return false;
                var u = (local.x - rect.x) / rect.width;
                var v = (local.y - rect.y) / rect.height;
                if (u < -0.02f || u > 1.02f || v < -0.02f || v > 1.02f) return false;
                u = Mathf.Clamp01(u);
                v = Mathf.Clamp01(v);
                ray = StageCamera.ViewportPointToRay(new Vector3(u, v, 0f));
                return true;
            }

            ray = StageCamera.ScreenPointToRay(screen);
            return true;
        }

        static bool TryGetPointer(out Vector2 screen)
        {
            screen = default;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screen = touch.primaryTouch.position.ReadValue();
                return true;
            }

            if (mouse != null && mouse.leftButton.isPressed)
            {
                screen = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButton(0))
            {
                screen = Input.mousePosition;
                return true;
            }
#endif
            return false;
        }

        static bool TryGetPointerDown(out Vector2 screen)
        {
            screen = default;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screen = touch.primaryTouch.position.ReadValue();
                return true;
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screen = mouse.position.ReadValue();
                return true;
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                screen = Input.mousePosition;
                return true;
            }
#endif
            return false;
        }

        static bool TryGetPointerUp()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasReleasedThisFrame) return true;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame) return true;
            return false;
#else
            return Input.GetMouseButtonUp(0);
#endif
        }
    }
}
